using Brutal.RakNetApi;
using System.Numerics;
using CallOfSlop.Story;

namespace CallOfSlop;

/// <summary>
/// STAND-IN (placeholder, Source/StandIns/README.md): scripted craft that carry the story's people and the factions'
/// couriers, so a story can be watched while the real vehicle and ship system is redone. They are pictures only: no
/// collision, no physics, no construct, nothing saved. The host decides each craft's script and sends it once
/// (<see cref="Msg.StandIn"/>, again every few seconds for anyone who joined); every PC poses it from the server tick.
/// <list type="bullet">
/// <item>A person arriving by road: a craft sets down beside the road's end just as their body appears there, waits while
/// they step out, and goes. A long road (<see cref="StandInShuttleRoad"/> minutes or more) is a shuttle out of the sky that
/// leaves with a jump flash; a short one a hover hauler along the ground.</item>
/// <item>A person setting off: a craft comes for them; their body waits at the road's end until it has landed (at most
/// <see cref="StandInBoardWait"/> s), and the craft goes once they are aboard.</item>
/// <item>A sealed Requisition Crate: the faction's courier flyer sets down beside it when the courier comes and leaves
/// once it has been and gone.</item>
/// </list>
/// Delete the folder and the lines marked "STAND-IN" (README.md lists them) to take it all out.
/// </summary>
public sealed partial class Game
{
    public const int StandInMax = 48; // (the people's craft, and the traffic's with its stations: GameTraffic.cs)
    public const double StandInShuttleRoad = 5; // story minutes: a road this long or longer is flown
    public const float StandInSide = 7f; // m: a craft stands this far to the side of the road's end
    public const double StandInStand = 8; // s an arrival stands before going
    public const double StandInBoardWait = 30; // s: the longest a body waits at the road's end for its craft
    public const double StandInResend = 3; // s between resends of every craft (for anyone who joined)
    public const float StandInApart = 14f; // m: craft standing at once keep this far apart

    /// <summary>The host's switch (the Story window's People tab).</summary>
    public bool StandInsOn = true;

    /// <summary>The craft now (both sides: the host's own list, or what a client heard).</summary>
    public readonly StandInCraft[] StandIns = new StandInCraft[StandInMax];
    public int StandInCount;
    private Map? _standInMap;

    // Server only: what each craft is for.
    private enum StandInFor : byte { Arrival, Pickup, Courier, Test, Duty, Traffic, Pad } // (Duty: the quarry's crew bus, GameQuarry.cs)
    private readonly (StandInFor For, int Who, double Step)[] _standInWhy = new (StandInFor, int, double)[StandInMax]; // Who: the person, or a courier's key
    private ushort _standInNextId = 1;
    private double _standInSentAt, _standInAt;
    private PacketWriter? _standInW;

    /// <summary>Craft called this session (the window).</summary>
    public int StandInsMade;

    /// <summary>Are the craft listed this map's (a client that changed map hears new ones)?</summary>
    public bool StandInsOf(Map map) => _standInMap == map;

    /// <summary>Both sides: no craft (a new story, a new world).</summary>
    private void StandInsForget()
    {
        StandInCount = 0;
        _standInMap = World.Map;
        StandInPath.ForgetRoutes();
        TrafficForget(); // (the ambient traffic: GameTraffic.cs)
    }

    private uint StandInTicks(double seconds) => (uint)Math.Max(1, Math.Round(seconds * World.TickRate));

    /// <summary>Server: a new craft. Returns its slot, or -1 when there's no room.</summary>
    private int StandInCall(StandInKind kind, Vector3 land, Vector3 way, double inSeconds, double wait, StandInFor why, int who, double step, bool jump)
    {
        if (_standInMap != World.Map)
            StandInsForget();
        StandInsDropEnded();
        if (StandInCount >= StandInMax)
            return -1;
        var (_, outs) = StandInCraft.Times(kind);
        way = StandInPath.Along(way, land);
        for (var k = 0; k < StandInCount && kind != StandInKind.Pad; k++)
            if (StandIns[k].Kind != StandInKind.Pad && !StandIns[k].Over && Vector3.DistanceSquared(StandIns[k].Land, land) < StandInApart * StandInApart)
            {
                land = StandInPath.Ground(land + way * StandInApart + StandInPath.Up(land) * 30f); // (two people on one road at once: side by side)
                k = -1;
            }
        var i = StandInCount++;
        StandIns[i] = new StandInCraft
        {
            Id = _standInNextId++ is var id and not 0 ? id : _standInNextId++,
            Kind = kind,
            Flags = jump ? StandInCraft.FlagJump : (byte)0,
            Start = ServerTick,
            In = (ushort)Math.Min(ushort.MaxValue, StandInTicks(inSeconds)),
            Wait = (ushort)Math.Min(ushort.MaxValue, StandInTicks(wait)),
            Out = (ushort)StandInTicks(outs),
            Land = land,
            Way = way,
        };
        _standInWhy[i] = (why, who, step);
        StandInsMade++;
        StandInSend(i, resend: false);
        return i;
    }

    /// <summary>Server: a standing craft goes in <paramref name="seconds"/> (or as soon as it has landed).</summary>
    private void StandInLeave(int i, double seconds)
    {
        ref var c = ref StandIns[i];
        var leave = Math.Max(c.LandsAt, ServerTick + StandInTicks(seconds));
        var wait = (ushort)Math.Clamp(leave - c.LandsAt, 0, ushort.MaxValue);
        if (wait >= c.Wait)
            return; // (already going sooner)
        c.Wait = wait;
        if (_standInWhy[i].For == StandInFor.Pickup && Story is { } s)
            Console.WriteLine($"[standin] {s.World.NameOf(_standInWhy[i].Who)} is aboard (the craft {(c.Landed(ServerTick) ? $"landed {(ServerTick - c.LandsAt) / (double)World.TickRate:0} s ago" : "hasn't landed yet")}); it goes");
        StandInSend(i, resend: false);
    }

    private void StandInsDropEnded()
    {
        for (var i = StandInCount - 1; i >= 0; i--)
            if (ServerTick >= StandIns[i].EndsAt)
            {
                StandInCount--;
                StandIns[i] = StandIns[StandInCount];
                _standInWhy[i] = _standInWhy[StandInCount];
            }
    }

    private int StandInFind(StandInFor why, int who)
    {
        for (var i = 0; i < StandInCount; i++)
            if (_standInWhy[i].For == why && _standInWhy[i].Who == who && ServerTick < StandIns[i].LeavesAt)
                return i;
        return -1;
    }

    private bool StandInCalledFor(StandInFor why, int who, double step)
    {
        for (var i = 0; i < StandInCount; i++)
            if (_standInWhy[i].For == why && _standInWhy[i].Who == who && _standInWhy[i].Step == step)
                return true;
        return false;
    }

    /// <summary>Where a craft stands beside a spot: to the side of the line from the place's middle, on the ground.</summary>
    private Vector3 StandInBeside(Vector3 spot, out Vector3 way)
    {
        var up = StandInPath.Up(spot);
        var outward = StandInPath.Along(spot - _storyMiddle, spot);
        way = Vector3.Cross(up, outward); // (it travels across the road, not along it into the place)
        var at = spot + outward * StandInSide;
        return StandInPath.Ground(at + up * 30f);
    }

    /// <summary>Server, four times a second with the bodies (from <see cref="StoryBodiesTick"/>'s look): craft for arrivals and departures.</summary>
    private void StandInsTick(StoryEngine s, double now)
    {
        if (_standInMap != World.Map)
            StandInsForget();
        StandInsDropEnded();
        if (StandInCount > 0 && now - _standInSentAt >= StandInResend)
        {
            _standInSentAt = now;
            for (var i = 0; i < StandInCount; i++)
                StandInSend(i, resend: true);
        }
        if (now < _standInAt)
            return;
        _standInAt = now + StoryBodiesEvery;
        StandInCouriersLook();
        TrafficTick(now); // (the ambient traffic: GameTraffic.cs)
        if (!StandInsOn || !StoryBodiesOn || Phase != MatchPhase.Playing || !StoryMiddle())
            return;
        var ag = s.Agents;
        var people = s.Rules.People;
        var place = StoryPlace();
        if (place < 0)
            return;
        var clock = s.Time;

        // Arrivals: a craft that sets down at the road's end as the body appears there.
        foreach (var a in ag.All)
        {
            if (a.Dead || !a.Busy || a.Doing.Place != place || a.DoingFromPlace < 0 || a.DoingFromPlace == place || !people.Actions[a.Doing.Action].Road)
                continue;
            var kind = StandInRoadKind(s, a.DoingFromPlace, place);
            var (ins, _) = StandInCraft.Times(kind);
            var left = a.DoingUntil - clock - StoryArriveWalk; // (story seconds until the body shows)
            if (left > ins || left < 2 || StandInCalledFor(StandInFor.Arrival, a.Entity, a.DoingFrom))
                continue;
            var end = StorySpot(StoryNameKey(s.World.NameOf(a.DoingFromPlace)) ^ 0x524F4144UL, StoryRoadMin, StoryRoadMax); // (StoryRoadEnd's spot)
            var land = StandInBeside(end, out var way);
            if (StandInCall(kind, land, way, left, StandInStand, StandInFor.Arrival, a.Entity, a.DoingFrom, kind == StandInKind.Shuttle) >= 0)
                Console.WriteLine($"[standin] a {kind} brings {s.World.NameOf(a.Entity)} from {s.World.NameOf(a.DoingFromPlace)}: lands in {left:0} s");
        }

        // Departures: a craft for a body walking out; it goes once they're aboard (the body gone).
        for (var i = 0; i < _bodyCount; i++)
        {
            ref var b = ref _bodies[i];
            if (!b.Leaving || ag.Of(b.Entity) is not { } a || StandInCalledFor(StandInFor.Pickup, a.Entity, a.DoingFrom))
                continue;
            var kind = StandInRoadKind(s, place, a.Doing.Place);
            var (ins, _) = StandInCraft.Times(kind);
            var land = StandInBeside(StoryRoadEnd(s, ref b, a.Doing.Place), out var way);
            if (StandInCall(kind, land, way, ins, StandInBoardWait + 10, StandInFor.Pickup, a.Entity, a.DoingFrom, kind == StandInKind.Shuttle) >= 0)
                Console.WriteLine($"[standin] a {kind} comes for {s.World.NameOf(a.Entity)}, off to {s.World.NameOf(a.Doing.Place)}");
        }
        for (var i = 0; i < StandInCount; i++)
            if (_standInWhy[i].For == StandInFor.Pickup && ServerTick < StandIns[i].LeavesAt && StoryBodyIndexOf(_standInWhy[i].Who) < 0)
                StandInLeave(i, 1.5); // (aboard, or gone some other way)
    }

    /// <summary>A shuttle for a long road, a hauler for a short one.</summary>
    private static StandInKind StandInRoadKind(StoryEngine s, int from, int to)
    {
        var road = s.Rules.People.Road;
        var minutes = road >= 0 ? Math.Max(s.World.Get(from, road, to), s.World.Get(to, road, from)) : 0;
        return minutes >= StandInShuttleRoad ? StandInKind.Shuttle : StandInKind.Hauler;
    }

    /// <summary>
    /// Server, from the bodies' look (GameStoryBodies.cs): may a body that has walked out to the road's end go now? Yes
    /// unless its craft is on the way and hasn't landed, for at most <see cref="StandInBoardWait"/> s.
    /// </summary>
    private bool StandInMayBoard(int entity)
    {
        if (!StandInsOn)
            return true;
        for (var i = 0; i < StandInCount; i++)
            if (_standInWhy[i].For == StandInFor.Pickup && _standInWhy[i].Who == entity)
            {
                ref var c = ref StandIns[i];
                return c.Landed(ServerTick) || ServerTick - c.Start > StandInTicks(StandInBoardWait);
            }
        return true;
    }

    // ---- Couriers (from GameStoryJobs.cs) -----------------------------------------------------------------------------

    private static int StandInCourierKey(int party, int faction) => party * 64 + faction;

    /// <summary>Server: a crate was sealed; the faction's courier sets down beside it in <paramref name="seconds"/>.</summary>
    private void StandInCourierCalled(int party, int faction, Construct crate, int part, double seconds)
    {
        if (!StandInsOn || part < 0 || part >= crate.PartCount)
            return;
        var key = StandInCourierKey(party, faction);
        if (StandInFind(StandInFor.Courier, key) >= 0)
            return;
        ref var p = ref crate.Parts[part];
        crate.WorldFrame(p.Seg, out var q, out var t);
        var at = t + Vector3.Transform(p.Pos, q);
        var up = StandInPath.Up(at);
        var side = StandInPath.Along(new Vector3(MathF.Sin(key), 0, MathF.Cos(key)), at);
        var land = StandInPath.Ground(at + side * 6f + up * 30f);
        var (ins, _) = StandInCraft.Times(StandInKind.Courier);
        var wait = Math.Max(0, seconds - ins);
        var i = StandInCall(StandInKind.Courier, land, Vector3.Cross(up, side), ins, 600, StandInFor.Courier, key, 0, jump: false);
        if (i >= 0)
        {
            StandIns[i].Start += StandInTicks(wait); // (it sets off so as to land when the courier comes)
            StandInSend(i, resend: false);
        }
    }

    /// <summary>Server: a courier's flyer whose crate isn't sealed any more (short again, the job dropped) goes.</summary>
    private void StandInCouriersLook()
    {
        for (var i = 0; i < StandInCount; i++)
        {
            if (_standInWhy[i].For != StandInFor.Courier || ServerTick >= StandIns[i].LeavesAt)
                continue;
            var key = _standInWhy[i].Who;
            if (!_jobWatch.TryGetValue((key / 64, key % 64), out var w) || !w.Full)
                StandInLeave(i, 2);
        }
    }

    /// <summary>Server: the courier has been (collected, sent back) or isn't coming (short again, dropped): its flyer goes.</summary>
    private void StandInCourierDone(int party, int faction, double seconds)
    {
        var i = StandInFind(StandInFor.Courier, StandInCourierKey(party, faction));
        if (i >= 0)
            StandInLeave(i, seconds);
    }

    // ---- The host's test (the Story window) ---------------------------------------------------------------------------

    /// <summary>Host: a craft of each kind comes in near the player, stands a while and goes (to see them without a story trip).</summary>
    public void StandInTest(Vector3 near)
    {
        if (!IsServer)
            return;
        var up = StandInPath.Up(near);
        var k = 0;
        for (var kind = StandInKind.Shuttle; kind < StandInKind.Count; kind++, k++)
        {
            var side = StandInPath.Along(new Vector3(MathF.Sin(k * 2.1f + 0.4f), 0, MathF.Cos(k * 2.1f + 0.4f)), near);
            var land = StandInPath.Ground(near + side * 18f + up * 30f);
            var (ins, _) = StandInCraft.Times(kind);
            StandInCall(kind, land, Vector3.Cross(up, side), ins, 10, StandInFor.Test, -1, k, jump: kind == StandInKind.Shuttle);
        }
        Console.WriteLine($"[standin] test: {StandInCount} craft near {near.X:0},{near.Z:0}");
    }

    // ---- The wire (Msg.StandIn) ---------------------------------------------------------------------------------------

    /// <summary>Server: one craft to every client: u16 id, u8 kind, u8 flags, u32 start, u16 in, u16 wait, u16 out, vec land, vec way, vec from, 3 x u8 tint.</summary>
    private void StandInSend(int i, bool resend)
    {
        if (Net.IsOffline)
            return;
        ref var c = ref StandIns[i];
        var w = _standInW ??= new PacketWriter(72);
        w.Begin(Msg.StandIn).U16(c.Id).U8((byte)c.Kind).U8(c.Flags).U32(c.Start).U16(c.In).U16(c.Wait).U16(c.Out).Vec(c.Land).Vec(c.Way).Vec(c.From)
            .U8((byte)(c.Tint.X * 255f + 0.5f)).U8((byte)(c.Tint.Y * 255f + 0.5f)).U8((byte)(c.Tint.Z * 255f + 0.5f));
        Net.Broadcast(w.Span, resend ? PacketReliability.Unreliable : PacketReliability.ReliableOrdered);
    }

    /// <summary>Client: a <see cref="Msg.StandIn"/> (after its id): the craft is added, or replaced by its id.</summary>
    private void ReadStandIn(ref PacketReader r)
    {
        if (!r.Has(53))
            return;
        var c = new StandInCraft
        {
            Id = r.U16(),
            Kind = (StandInKind)Math.Min(r.U8(), (byte)(StandInKind.Count - 1)),
            Flags = r.U8(),
            Start = r.U32(),
            In = r.U16(),
            Wait = r.U16(),
            Out = r.U16(),
            Land = r.Vec(),
            Way = r.Vec(),
            From = r.Vec(),
        };
        c.Tint = new Vector3(r.U8(), r.U8(), r.U8()) / 255f;
        if (_standInMap != World.Map)
            StandInsForget();
        if (!float.IsFinite(c.Land.X + c.Land.Y + c.Land.Z) || !float.IsFinite(c.Way.X + c.Way.Y + c.Way.Z) || c.Way.LengthSquared() < 0.5f
            || !float.IsFinite(c.From.X + c.From.Y + c.From.Z))
            return;
        for (var i = 0; i < StandInCount; i++)
            if (StandIns[i].Id == c.Id)
            {
                c.Track = StandIns[i].Land == c.Land && StandIns[i].From == c.From ? StandIns[i].Track : null;
                c.Boomed = StandIns[i].Start == c.Start ? StandIns[i].Boomed : (byte)0;
                StandIns[i] = c;
                return;
            }
        // Gone ones make room (a client has no tick of its own to drop them on).
        var now = MoverRenderTick;
        for (var i = StandInCount - 1; i >= 0; i--)
            if (now >= StandIns[i].EndsAt)
                StandIns[i] = StandIns[--StandInCount];
        if (StandInCount < StandInMax)
            StandIns[StandInCount++] = c;
    }
}
