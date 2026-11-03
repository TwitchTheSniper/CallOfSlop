using Brutal.RakNetApi;
using CallOfSlop.Space;

namespace CallOfSlop;

/// <summary>
/// The space bubble in play (phase 3 of the space plan, docs/space/SPACE.md "The bubble"). A map that a star system's body
/// names (<c>"map"</c> in <c>Systems/*.json</c>, e.g. the Dev system's Planetoid) is played as that body's ground: the host
/// runs the system's space clock and ships (<see cref="SpaceFlight"/>) once a tick, and every peer works out the sky from
/// the clock (<see cref="SpaceBubble"/>: the real sun and moon, day and night as the body turns). What goes over the wire
/// (<see cref="Msg.Space"/>, protocol 125) is only what a client can't work out: which system and body, the clock (time and
/// warp), and the ships' states.
/// </summary>
public sealed partial class Game
{
    /// <summary>This map's bubble (null: the map isn't in a star system, or a client hasn't heard it yet).</summary>
    public SpaceSession? Space { get; private set; }

    /// <summary>Host: how often the clock goes out with no change (ticks), how often a ships packet goes out, how many ships fit in one.</summary>
    public const int SpaceClockEvery = 60, SpaceShipsEvery = 6, SpaceShipsPerPacket = 18;

    /// <summary>The most ships a bubble's system holds (the host's <see cref="SpaceShips"/>).</summary>
    public const int SpaceShipCapacity = 256;

    /// <summary>Client: a ship not heard of for this many ticks is gone (every ship comes round at least every 3 s at the cursor's pace up to 540 ships).</summary>
    public const int SpaceShipTimeoutTicks = 180;

    private Map? _spaceMap;
    private int _spaceSentWarp = -1;
    private int _spaceShipCursor;

    // Client: the bubble the host announced, applied once this PC has that map loaded.
    private string? _spacePendingMap, _spacePendingFile;
    private uint _spacePendingHash, _spacePendingTick;
    private int _spacePendingBody, _spacePendingWarp;
    private long _spacePendingNs;
    private bool _spaceWarned;

    private enum SpaceMsg : byte
    {
        Bubble, // reliable, on a join and when the map changes: map name, system file ("" for none), file hash, body, then a clock
        Clock, // unreliable once a second, reliable on a warp change: host tick, time (ns), warp
        Ships, // unreliable, 10 Hz: host tick, count, then per ship its id and ShipState (a cursor takes turns past one packet)
    }

    /// <summary>The space time to draw with: the clock between ticks (the frame's share of the next tick at the current warp).</summary>
    public SpaceTime SpaceRenderTime => Space is { } s ? s.Now + (long)(s.Warp * (double)_alpha * SpaceTime.NsPerSecond / World.TickRate) : default;

    /// <summary>Host: asks for a warp (0 pauses, up to 100,000x). The provisional rule (<see cref="SpaceWarpRule"/>) decides what runs. False on a client or with no bubble.</summary>
    public bool RequestSpaceWarp(int warp) => IsServer && Space?.Flight is { } f && f.RequestWarp(warp, fromHost: true);

    /// <summary>
    /// Host, every tick: the bubble follows the map, then the clock and the ships step and their news goes out.
    /// Allocation-free once the map's bubble is made.
    /// </summary>
    private void ServerTickSpace()
    {
        if (!ReferenceEquals(_spaceMap, World.Map))
        {
            MakeHostSpace();
            WriteSpaceBubble(_w);
            Net.Broadcast(_w.Span, PacketReliability.ReliableOrdered);
        }
        if (Space?.Flight is not { } f)
            return;
        f.AnyoneBusy = SpaceAnyoneBusy();
        SpaceLaunchBefore(f); // (ships that are constructs: their thrust and turn, GameSpaceLaunch.cs)
        f.Tick();
        SpaceLaunchAfter(f); // (constructs past the sky's edge become ships; ships pose their constructs or come back in)
        if (f.Clock.Warp != _spaceSentWarp)
        {
            _spaceSentWarp = f.Clock.Warp;
            WriteSpaceClock(_w, f);
            Net.Broadcast(_w.Span, PacketReliability.ReliableOrdered);
        }
        else if (ServerTick % SpaceClockEvery == 0)
        {
            WriteSpaceClock(_w, f);
            Net.Broadcast(_w.Span, PacketReliability.Unreliable);
        }
        if (f.Ships.Count > 0 && ServerTick % SpaceShipsEvery == 0)
        {
            WriteSpaceShips(_w, f.Ships, ref _spaceShipCursor, ServerTick);
            Net.Broadcast(_w.Span, PacketReliability.Unreliable);
        }
    }

    /// <summary>Host: the bubble for the map now loaded (the space time carries on when the new map is in the same system).</summary>
    private void MakeHostSpace()
    {
        _spaceMap = World.Map;
        _spaceShipCursor = 0;
        _spaceSentWarp = -1;
        var old = Space;
        Space = null;
        if (!StarSystems.ForMap(World.Map.Name, out var e, out var body))
            return;
        var sameSystem = old?.Flight is { } of && ReferenceEquals(of.System, e.System);
        var start = sameSystem ? old!.Now : e.System.Start;
        var flight = sameSystem ? old!.Flight! : new SpaceFlight(e.System, SpaceShipCapacity, start);
        Space = new SpaceSession(new SpaceBubble(e.System, body), e.Hash, World.Map.Name, flight);
        Console.WriteLine($"[space] {World.Map.Name} is {e.System.Bodies[body].Name} in {e.System.Name} ({e.System.File}), space time {flight.Clock.Now}");
    }

    /// <summary>
    /// The provisional "is anyone busy" test of the warp rule (docs/space/SPACE.md "Time warp"; Karl hasn't decided the
    /// rule, so it stays in this one place): during a match, a person (not a bot) alive and moving or riding a construct,
    /// or any construct awake, keeps space at 1x. Nobody busy (everyone standing still, or the lobby) lets the host's warp run.
    /// </summary>
    internal bool SpaceAnyoneBusy()
    {
        if (Phase != MatchPhase.Playing)
            return false;
        foreach (var p in Players.Values)
        {
            if (p.Bot == null && p.Alive && SpaceSeated(p))
                continue; // (aboard a ship in space: coasting isn't busy; its burn is, below)
            if (p.Bot == null && p.Alive && (p.Sim.RideBody != 0 || p.Sim.Velocity.LengthSquared() > 0.25f))
                return true;
        }
        foreach (var c in Constructs.Bodies)
            if (!c.Asleep && (c.SpaceShip == 0 || c.ThrustShow > 0))
                return true;
        return false;
    }

    /// <summary>Host: a joiner hears the bubble (and the clock in it) before anything else of space.</summary>
    private void SendSpaceState(RakNetGUID to)
    {
        if (Space == null)
            return; // no bubble: the joiner's Space stays null
        WriteSpaceBubble(_w);
        Net.SendTo(to, _w.Span, PacketReliability.ReliableOrdered);
    }

    private void WriteSpaceBubble(PacketWriter w)
    {
        w.Begin(Msg.Space).U8((byte)SpaceMsg.Bubble);
        if (Space?.Flight is not { } f)
        {
            w.Str(World.Map.Name).Str("").U32(0).U8(0).U32(ServerTick).U64(0).U32(1);
            return;
        }
        w.Str(Space.MapName).Str(f.System.File).U32(Space.Hash).U8((byte)Space.Bubble.Body)
            .U32(ServerTick).U64((ulong)f.Clock.Now.Ns).U32((uint)f.Clock.Warp);
    }

    private void WriteSpaceClock(PacketWriter w, SpaceFlight f) =>
        w.Begin(Msg.Space).U8((byte)SpaceMsg.Clock).U32(ServerTick).U64((ulong)f.Clock.Now.Ns).U32((uint)f.Clock.Warp);

    /// <summary>
    /// One ships packet: up to <see cref="SpaceShipsPerPacket"/> ships from the cursor on (so past one packet's worth every
    /// ship still takes its turn), each its id and <see cref="ShipState"/>. Returns how many it holds.
    /// </summary>
    public static int WriteSpaceShips(PacketWriter w, SpaceShips ships, ref int cursor, uint tick)
    {
        var n = Math.Min(SpaceShipsPerPacket, ships.Count);
        w.Begin(Msg.Space).U8((byte)SpaceMsg.Ships).U32(tick).U8((byte)n);
        Span<byte> buf = stackalloc byte[ShipState.Size];
        for (var k = 0; k < n; k++)
        {
            if (cursor >= ships.Count)
                cursor = 0;
            ShipState.Of(ships, cursor).Write(buf);
            w.U16(ships.Ids[cursor]).Bytes(buf);
            cursor++;
        }
        return n;
    }

    /// <summary>Any peer: a <see cref="Msg.Space"/> (after the id). The host ignores them (it sends them).</summary>
    private void ReadSpace(ref PacketReader r)
    {
        if (IsServer || !r.Has(1))
            return;
        switch ((SpaceMsg)r.U8())
        {
            case SpaceMsg.Bubble when r.Has(2):
            {
                _spacePendingMap = r.Str();
                _spacePendingFile = r.Has(1) ? r.Str() : "";
                if (!r.Has(4 + 1 + 4 + 8 + 4))
                {
                    _spacePendingMap = null;
                    return;
                }
                _spacePendingHash = r.U32();
                _spacePendingBody = r.U8();
                _spacePendingTick = r.U32();
                _spacePendingNs = (long)r.U64();
                _spacePendingWarp = (int)r.U32();
                _spaceWarned = false;
                ApplySpacePending();
                break;
            }
            case SpaceMsg.Clock when r.Has(4 + 8 + 4):
            {
                var tick = r.U32();
                var ns = (long)r.U64();
                var warp = (int)r.U32();
                if (_spacePendingMap != null && tick >= _spacePendingTick)
                    (_spacePendingTick, _spacePendingNs, _spacePendingWarp) = (tick, ns, warp); // not applied yet: keep the newest
                Space?.HostClock(tick, new SpaceTime(ns), warp);
                break;
            }
            case SpaceMsg.Ships when r.Has(5) && Space is { } s:
            {
                r.U32();
                var n = r.U8();
                for (var k = 0; k < n && r.Has(2 + ShipState.Size); k++)
                {
                    var id = r.U16();
                    s.HostShip(id, ShipState.Read(r.Take(ShipState.Size)));
                }
                break;
            }
        }
    }

    /// <summary>Client: the announced bubble, once this PC has the host's map (a map change can land after the message).</summary>
    private void ApplySpacePending()
    {
        if (_spacePendingMap is not { } map || !string.Equals(World.Map.Name, map, StringComparison.OrdinalIgnoreCase))
            return;
        var file = _spacePendingFile ?? "";
        _spacePendingMap = null;
        Space = null;
        _spaceMap = World.Map;
        if (file.Length == 0)
            return;
        if (!StarSystems.ByFile(file, out var e) || e.Hash != _spacePendingHash || (uint)_spacePendingBody >= (uint)e.System.Count)
        {
            if (!_spaceWarned)
                Console.WriteLine($"[space] the host's star system {file} isn't the same as this PC's (missing or a different file): no sky from it");
            _spaceWarned = true;
            return;
        }
        Space = new SpaceSession(new SpaceBubble(e.System, _spacePendingBody), e.Hash, map, null);
        Space.HostClock(_spacePendingTick, new SpaceTime(_spacePendingNs), _spacePendingWarp);
    }

    /// <summary>Client, every tick: the clock runs on between the host's, ships not heard of go, a waiting bubble is applied.</summary>
    private void ClientTickSpace()
    {
        if (_spacePendingMap != null)
            ApplySpacePending();
        if (Space is not { } s)
            return;
        if (!ReferenceEquals(_spaceMap, World.Map))
        {
            Space = null; // a new map: the host's next bubble says what it is
            return;
        }
        s.ClientTick();
    }
}

/// <summary>A client's copy of one host ship: its last states and when the last one came (the client's ticks).</summary>
public sealed class ShipView
{
    public readonly ShipTrack Track = new();
    public long HeardTick;
}

/// <summary>
/// The bubble on one peer: the system and body (<see cref="SpaceBubble"/>), and the clock. On the host the clock and the
/// ships are <see cref="Flight"/>'s; a client runs its own clock between the host's (set from each one it hears) and keeps
/// a <see cref="ShipTrack"/> per host ship.
/// </summary>
public sealed class SpaceSession
{
    public readonly SpaceBubble Bubble;
    public readonly uint Hash;
    public readonly string MapName;

    /// <summary>Host only: the system's clock and ships.</summary>
    public readonly SpaceFlight? Flight;

    private readonly SpaceClock _clock = new();
    private uint _hostTick;
    private bool _heardClock;
    private long _ticks;

    /// <summary>Client: the host's ships by id.</summary>
    public readonly Dictionary<ushort, ShipView> Ships = new(64);

    public SpaceSession(SpaceBubble bubble, uint hash, string mapName, SpaceFlight? flight)
    {
        Bubble = bubble;
        Hash = hash;
        MapName = mapName;
        Flight = flight;
    }

    public bool IsHost => Flight != null;
    public StarSystem System => Bubble.System;
    public SpaceTime Now => Flight?.Clock.Now ?? _clock.Now;
    public int Warp => Flight?.Clock.Warp ?? _clock.Warp;

    /// <summary>Is this the bubble of that map?</summary>
    public bool IsFor(Map map) => string.Equals(MapName, map.Name, StringComparison.OrdinalIgnoreCase);

    /// <summary>Client: a clock from the host (older ones, by host tick, are dropped).</summary>
    public void HostClock(uint tick, SpaceTime t, int warp)
    {
        if (_heardClock && tick < _hostTick)
            return;
        _heardClock = true;
        _hostTick = tick;
        _clock.Warp = warp;
        _clock.Set(t);
    }

    /// <summary>Client: a ship's state from the host.</summary>
    public void HostShip(ushort id, in ShipState s)
    {
        if (s.Primary >= System.Count)
            return;
        if (!Ships.TryGetValue(id, out var v))
            Ships[id] = v = new ShipView();
        v.Track.Push(s);
        v.HeardTick = _ticks;
    }

    /// <summary>Client, once a tick.</summary>
    public void ClientTick()
    {
        _clock.Tick();
        _ticks++;
        if (_ticks % 60 != 0 || Ships.Count == 0)
            return;
        foreach (var (id, v) in Ships)
            if (_ticks - v.HeardTick > Game.SpaceShipTimeoutTicks)
                Ships.Remove(id);
    }

    /// <summary>Client: where a host ship is at a time (ecliptic). False when unknown.</summary>
    public bool SampleShip(ushort id, SpaceTime t, out SpacePos pos, out Vec3d vel)
    {
        pos = default;
        vel = default;
        return Ships.TryGetValue(id, out var v) && v.Track.Sample(System, t, out pos, out vel);
    }
}
