using Brutal.RakNetApi;
using CallOfSlop.Space;

namespace CallOfSlop;

/// <summary>
/// Free travel, step 3 (docs/space/FREE-TRAVEL.md): meeting on a planetoid kept on someone's game. A ship with one client
/// aboard coming into the sky of a planetoid that another player's game keeps goes to that game: the host sends the
/// keeper the ship (as it sends a traveller theirs, GameTravel.cs), the keeper puts it in its own space at the edge of its
/// sky and holds it there, and the host tells the visitor's game to join the keeper. The visitor's game joins as an
/// ordinary client over <see cref="RelayTransport"/> (through the host, so nobody needs a port open), sits in its ship
/// again (its record came with the ship) and flies in. Flying out of that sky again, the keeper sends the ship back to the
/// host, which puts it in its own space with the visitor in it, and the visitor's game closes.
/// <para>
/// The keeper stays the keeper while anyone else is there: its own ship flies on in its space until they have gone
/// (handing the planetoid to someone still on it is step 4).
/// </para>
/// </summary>
public sealed partial class Game
{
    /// <summary><c>Msg.Relay</c> kinds: a packet for the other game, a connection opened, one closed.</summary>
    public const byte RelayData = 0, RelayOpen = 1, RelayShut = 2;

    /// <summary>A link to the host: the keeper's or visitor's game this link carries (null: none).</summary>
    public RelayTransport? Relay;

    /// <summary>A keeper's or a visitor's game: the game connected to the host that it runs beside (null: an ordinary game).</summary>
    public Game? Link { get; private set; }

    /// <summary>A visitor's game: whom it visits.</summary>
    public TravelVisit? Visit { get; private set; }

    /// <summary>A link: the host says to join this keeper (<see cref="TravelStep"/> opens the visit).</summary>
    public TravelVisit? PendingVisit;

    /// <summary>A link: the host has the visitor back in its space (<see cref="TravelStep"/> closes the visit).</summary>
    public bool PendingHome;

    /// <summary>A planetoid to visit: the world, the map, and the keeper's id on the host.</summary>
    public sealed class TravelVisit
    {
        public string World = "", Map = "";
        public byte Keeper;
    }

    /// <summary>A link of a keeper: visitors' ships from the host, for the keeper to take in on its tick.</summary>
    private readonly List<byte[]> _meetIn = new();

    /// <summary>Host: who keeps which planetoid (map name to player id).</summary>
    private readonly Dictionary<string, byte> _keepers = new();

    /// <summary>Host: visitors and the keeper each visits (relayed packets go only between these pairs).</summary>
    private readonly Dictionary<byte, byte> _meetWith = new();

    private (Construct Ship, Map To, byte Rider, byte Keeper, int Body)? _meetNext;
    private readonly List<(byte Keeper, byte[] Payload)> _meetLeave = new();
    private readonly PacketWriter _relayW = new(4096);
    private readonly PacketWriter _relayCtl = new(16);
    private readonly HashSet<byte> _meetMoving = new(); // host: visitors waiting for their planetoid's new keeper to open
    /// <summary>How often a keeper sends the host a copy of its planetoid (s): what is kept if its game goes.</summary>
    public const double KeepSaveEvery = 120;

    /// <summary>A keeper: a visitor's ship, held at the edge of the sky until they sit in it, then watched until it leaves.</summary>
    private sealed class Visitor
    {
        public byte HostId;
        public ushort Ship;
        public int Body;
        public Vec3d Rel, Vel;
        public bool Seated, Down;
        public double Since;
    }

    private readonly List<Visitor> _visitors = new();

    /// <summary>Who keeps this planetoid on their game now (host; null: nobody, it's in the world's folder).</summary>
    public Player? KeeperOf(string map) => _keepers.TryGetValue(map, out var id) && Players.TryGetValue(id, out var p) ? p : null;

    // ---- Host: a ship coming into a kept sky ------------------------------------------------------------------------

    /// <summary>Host, in the landing step: the one client aboard visits the planetoid's keeper (false: it waits at the edge).</summary>
    private bool TravelMeet(Construct c, Map to, Player keeper, int body)
    {
        if (TravelRider(c) is not (var rider and not 255) || rider == keeper.Id || GuidOfId(keeper.Id) is null)
            return false;
        _meetNext = (c, to, rider, keeper.Id, body);
        return true;
    }

    /// <summary>Host, after the space tick: visitors' ships sent back by keepers, then a ship due to visit one.</summary>
    private void MeetTick()
    {
        if (_meetLeave.Count > 0)
        {
            foreach (var (from, payload) in _meetLeave)
                try
                {
                    MeetLeaveApply(from, payload);
                }
                catch (Exception ex) when (ex is WorldFileException or IndexOutOfRangeException or ArgumentException or InvalidOperationException or IOException)
                {
                    Console.WriteLine($"[travel] a visitor's ship from player {from}'s game couldn't be read: {ex.Message}");
                }
            _meetLeave.Clear();
        }
        if (_meetNext is not { } next)
            return;
        _meetNext = null;
        var (ship, to, rider, keeper, body) = next;
        if (WorldSession is not { } ws || Space?.Flight is not { } f || Constructs.Get(ship.Id) != ship || ship.SpaceShip == 0
            || f.Ships.IndexOfId(ship.SpaceShip) is not (var si and >= 0) || !Players.TryGetValue(rider, out var p)
            || GuidOfId(keeper) is not { } keeperAt || _keepers.GetValueOrDefault(to.Name, (byte)255) != keeper)
            return;
        f.System.StateOf(body, f.Ships.Time, out var bp, out var bv);
        var rel = f.Ships.PositionOf(si) - bp;
        var vel = f.Ships.VelocityOf(si) - bv;
        if (TravelPackShip(ws, ship, rider) is not { } shipFile)
        {
            SandboxHands.Notices.Add((rider, $"Couldn't land on {to.Name}: the ship wasn't saved"));
            return;
        }
        var w = new SaveWriter();
        w.Str(to.Name).U8(rider).U64(WorldKeyOf(p)).U64(p.SteamId).Str(p.Name).I32(body);
        w.F64(rel.X).F64(rel.Y).F64(rel.Z).F64(vel.X).F64(vel.Y).F64(vel.Z);
        w.I32(shipFile.Length).Bytes(shipFile);
        TravelSend(keeperAt, PurposeArrive, w.Span.ToArray());
        var count = TravelAway(ws, ship, p, to);
        _meetWith[rider] = keeper;
        Log($"{p.Name} lands on {to.Name} with {NameOf(keeper)}", true);
        Console.WriteLine($"[travel] construct {ship.Id} lands on {to.Name}, kept on {NameOf(keeper)}'s game: {count} constructs, {shipFile.Length} bytes sent");
        BroadcastRoster();
    }

    /// <summary>Both ends: a payload for meeting (GameTravel.cs hands over the rest).</summary>
    private void MeetArrived(RakNetGUID sender, byte purpose, byte[] data)
    {
        if (!IsServer)
        {
            switch (purpose)
            {
                case PurposeArrive:
                    _meetIn.Add(data);
                    break;
                case PurposeVisit:
                {
                    var r = new SaveReader(data);
                    PendingVisit = new TravelVisit { World = r.Str(), Map = r.Str(), Keeper = r.U8() };
                    Console.WriteLine($"[travel] the host sends this game to {PendingVisit.Map}, kept on player {PendingVisit.Keeper}'s game");
                    break;
                }
                case PurposeHome:
                    PendingHome = true;
                    break;
            }
            return;
        }
        if (!_clients.TryGetValue(sender.G, out var c))
            return;
        if (purpose == PurposeLeave)
            _meetLeave.Add((c.Id, data));
        else if (purpose == PurposeSave)
            MeetKeepCopy(c.Id, data);
        else if (purpose == PurposeArrived && data.Length >= 1 && data[0] == 255)
            MeetOpened(c.Id);
        else if (purpose == PurposeArrived && data.Length >= 1)
        {
            var visitor = data[0];
            if (!_meetWith.TryGetValue(visitor, out var k) || k != c.Id || GuidOfId(visitor) is not { } at || WorldSession is not { } ws
                || !Players.TryGetValue(visitor, out var vp) || vp.AwayOn == null)
                return;
            var w = new SaveWriter(256);
            w.Str(ws.Meta.Name).Str(vp.AwayOn).U8(c.Id);
            TravelSend(at, PurposeVisit, w.Span.ToArray());
        }
    }

    /// <summary>Host: a visitor's ship back from the keeper's sky, into this game's space with the visitors in it.</summary>
    private void MeetLeaveApply(byte keeper, byte[] payload)
    {
        if (WorldSession is not { } ws || Space?.Flight is not { } f)
            return;
        var r = new SaveReader(payload);
        var map = r.Str();
        var n = r.U8();
        var riders = new List<Player>();
        for (var i = 0; i < n; i++)
        {
            var id = r.U8();
            if (_meetWith.TryGetValue(id, out var k) && k == keeper && Players.TryGetValue(id, out var p) && p.AwayOn == map)
                riders.Add(p);
        }
        var shipFile = r.Bytes(r.I32());
        var primary = r.I32();
        var rel = new Vec3d(r.F64(), r.F64(), r.F64());
        var vel = new Vec3d(r.F64(), r.F64(), r.F64());
        if (riders.Count == 0 || primary < 0 || primary >= f.System.Count)
            throw new InvalidOperationException($"a ship from {map} for nobody visiting there");
        TravelShipHome(ws, f, map, shipFile, primary, rel, vel, riders);
        foreach (var p in riders)
        {
            _meetWith.Remove(p.Id);
            if (GuidOfId(p.Id) is { } at)
                TravelSend(at, PurposeHome, []);
        }
    }

    /// <summary>Host: a client left. A visitor's keeper hears it; a keeper's visitors come back here (at a spawn: the planetoid went with its game).</summary>
    private void MeetGone(byte id)
    {
        if (_meetWith.Remove(id, out var keeper) && GuidOfId(keeper) is { } kAt)
            RelayTo(kAt, RelayShut, id);
        string? kept = null;
        foreach (var (map, k) in _keepers)
            if (k == id)
                kept = map;
        if (kept == null)
            return;
        _keepers.Remove(kept);
        byte[]? copy = null;
        if (WorldSession is { } ws)
            try
            {
                var at = WorldStore.PathOf(StashFolder(ws, kept));
                if (File.Exists(at))
                    copy = File.ReadAllBytes(at);
            }
            catch (IOException ex)
            {
                Console.WriteLine($"[travel] {kept}'s last copy couldn't be read: {ex.Message}");
            }
        if (copy != null && WorldSession is { } ws2 && MeetTakeOver(ws2, kept, id, copy))
        {
            Console.WriteLine($"[travel] {kept}'s keeper left (player {id}): it goes on from its last copy on another game there");
            return;
        }
        foreach (var (v, k) in _meetWith.ToList())
            if (k == id && Players.TryGetValue(v, out var p))
            {
                _meetWith.Remove(v);
                p.AwayOn = null;
                p.WantsRespawn = true;
                p.RespawnAt = 0;
                SandboxHands.Notices.Add((v, $"{kept} closed with its keeper's game: you're back at a spawn"));
                if (GuidOfId(v) is { } at)
                    TravelSend(at, PurposeHome, []);
            }
        Console.WriteLine($"[travel] {kept} closed with its keeper's game (player {id} left): the world keeps it as it was last brought back");
    }

    /// <summary>
    /// A ship read in from another game, posed where its ship in space is at once (and what it carries moved with it), so
    /// this tick's physics never sees it where it was on the other map (it would fall out of the world before the space
    /// step poses it).
    /// </summary>
    private void TravelPoseNow(Construct ship, SpaceFlight f, int si, IEnumerable<ushort> aboard)
    {
        if (Space is not { } s || GravityFrames.Point is not { } pt)
            return;
        var pose = s.Bubble.PoseAt(f.Ships.Time);
        var at = pose.FromEcliptic(f.Ships.PositionOf(si)).ToVector3();
        var vel = pose.VelocityFromEcliptic(f.Ships.PositionOf(si), f.Ships.VelocityOf(si)).ToVector3();
        var before = ship.Pos;
        SpacePose(ship, at, vel, pt.Center, World.TickDt);
        var by = ship.Pos - before;
        foreach (var id in aboard)
            if (id != ship.Id && Constructs.Get(id) is { } c)
            {
                c.Pos += by;
                c.PrevPos = c.Pos;
                c.Vel = ship.Vel;
            }
        ship.PrevPos = ship.Pos;
        ship.PrevRot = ship.Rot;
    }

    // ---- Host: handing a planetoid to someone still on it (step 4) -------------------------------------------------

    /// <summary>
    /// Host: the keeper of <paramref name="map"/> went home, or its game went, while others were there: the first of them
    /// keeps it now, from <paramref name="planet"/> (the whole planetoid, everyone where they were), and the rest join that
    /// game once it is open. False: nobody else was there.
    /// </summary>
    private bool MeetTakeOver(WorldSession ws, string map, byte oldKeeper, byte[] planet)
    {
        byte next = 255;
        foreach (var (v, k) in _meetWith)
            if (k == oldKeeper && GuidOfId(v) != null && (next == 255 || v < next))
                next = v;
        if (next == 255 || GuidOfId(next) is not { } at || !Players.TryGetValue(next, out var np))
            return false;
        _meetWith.Remove(next);
        _meetMoving.Remove(next);
        foreach (var (v, k) in _meetWith.ToList())
            if (k == oldKeeper)
            {
                _meetWith[v] = next;
                _meetMoving.Add(v);
            }
        _keepers[map] = next;
        var w = new SaveWriter();
        w.Str(ws.Meta.Name).Str(map).I32(planet.Length).Bytes(planet).I32(0).U8(1);
        TravelSend(at, PurposeOpen, w.Span.ToArray());
        Log($"{np.Name}'s game keeps {map} now", true);
        Console.WriteLine($"[travel] {map} goes to {np.Name}'s game ({planet.Length} bytes), {_meetMoving.Count} to follow");
        return true;
    }

    /// <summary>Host: a keeper's game is open: visitors waiting for it join it.</summary>
    private void MeetOpened(byte keeper)
    {
        if (_meetMoving.Count == 0 || WorldSession is not { } ws)
            return;
        foreach (var v in _meetMoving.ToList())
            if (_meetWith.TryGetValue(v, out var k) && k == keeper && Players.TryGetValue(v, out var vp) && vp.AwayOn != null && GuidOfId(v) is { } at)
            {
                _meetMoving.Remove(v);
                var w = new SaveWriter(256);
                w.Str(ws.Meta.Name).Str(vp.AwayOn).U8(keeper);
                TravelSend(at, PurposeVisit, w.Span.ToArray());
            }
    }

    /// <summary>Host: a keeper's copy of its planetoid, kept in the world's folder in case its game goes.</summary>
    private void MeetKeepCopy(byte keeper, byte[] data)
    {
        if (WorldSession is not { } ws)
            return;
        var r = new SaveReader(data);
        var map = r.Str();
        var file = r.Bytes(r.I32());
        if (_keepers.GetValueOrDefault(map, (byte)255) != keeper)
            return;
        var stash = StashFolder(ws, map);
        Directory.CreateDirectory(Path.Combine(WorldStore.Root, stash));
        if (WorldStore.Write(stash, file) is { } why)
            Console.WriteLine($"[travel] {map}'s copy from its keeper wasn't kept: {why}");
    }

    /// <summary>Keeper, every tick: tell the host once it is open, and send it a copy of the planetoid now and then.</summary>
    private void KeepReport(WorldSession ws, TravelKeeping k, Game link)
    {
        if (!k.Told)
        {
            k.Told = true;
            k.SavedAt = SandboxNow;
            link.TravelSend(link.Net.ServerGuid, PurposeArrived, [255]);
            return;
        }
        if (SandboxNow < k.SavedAt + KeepSaveEvery)
            return;
        k.SavedAt = SandboxNow;
        WorldSaveNow(wait: true);
        var folder = Path.Combine(k.Folder, "copy");
        if (WorldCopy(ws, folder, _ => true) is { } why)
        {
            Console.WriteLine($"[travel] {k.Map} couldn't be copied to the host: {why}");
            return;
        }
        var file = File.ReadAllBytes(WorldStore.PathOf(folder));
        TryDelete(folder);
        var w = new SaveWriter(file.Length + 64);
        w.Str(k.Map).I32(file.Length).Bytes(file);
        link.TravelSend(link.Net.ServerGuid, PurposeSave, w.Span.ToArray());
    }

    // ---- Relaying packets between a keeper and its visitors ---------------------------------------------------------

    private void OnRelayPacket(RakNetGUID sender, ref PacketReader r)
    {
        var kind = r.U8();
        var other = r.U8();
        if (!IsServer)
        {
            if (Relay is not { } t)
                return;
            if (kind == RelayData)
                t.Deliver(other, r.Take(r.Remaining));
            else if (kind == RelayOpen)
                t.Opened(other);
            else if (kind == RelayShut)
                t.Closed(other);
            return;
        }
        if (!_clients.TryGetValue(sender.G, out var c) || !RelayPair(c.Id, other) || GuidOfId(other) is not { } to)
            return;
        if (kind == RelayData)
        {
            var how = (PacketReliability)r.U8();
            if (how is not (PacketReliability.Unreliable or PacketReliability.UnreliableSequenced or PacketReliability.Reliable
                or PacketReliability.ReliableOrdered or PacketReliability.ReliableSequenced))
                throw new MalformedPacketException("relay: no such reliability");
            var data = r.Take(r.Remaining);
            _relayW.Begin(Msg.Relay).U8(RelayData).U8(c.Id);
            _relayW.Reserve(data.Length);
            _relayW.Bytes(data);
            Net.SendTo(to, _relayW.Span, how);
        }
        else if (kind is RelayOpen or RelayShut)
            RelayTo(to, kind, c.Id);
    }

    /// <summary>Host: may these two pass packets (a visitor and the keeper it visits, either way)?</summary>
    private bool RelayPair(byte a, byte b) =>
        _meetWith.TryGetValue(a, out var k) && k == b || _meetWith.TryGetValue(b, out var k2) && k2 == a;

    private void RelayTo(RakNetGUID to, byte kind, byte from)
    {
        _relayCtl.Begin(Msg.Relay).U8(kind).U8(from);
        Net.SendTo(to, _relayCtl.Span, PacketReliability.ReliableOrdered);
    }

    /// <summary>A link: a packet from the game it carries to <paramref name="to"/>'s (by id on the host).</summary>
    internal void RelaySend(byte to, ReadOnlySpan<byte> data, PacketReliability how)
    {
        _relayW.Begin(Msg.Relay).U8(RelayData).U8(to).U8((byte)how);
        _relayW.Reserve(data.Length);
        _relayW.Bytes(data);
        Net.SendToServer(_relayW.Span, how);
    }

    /// <summary>A link: the game it carries opened or closed its connection to <paramref name="other"/>'s.</summary>
    internal void RelayControl(byte kind, byte other)
    {
        _relayCtl.Begin(Msg.Relay).U8(kind).U8(other);
        Net.SendToServer(_relayCtl.Span, PacketReliability.ReliableOrdered);
    }

    // ---- The keeper: visitors' ships ---------------------------------------------------------------------------------

    /// <summary>Keeper, every tick: visitors' ships come in, wait at the edge for their riders, and go home again.</summary>
    private void MeetKeepTick(WorldSession ws, TravelKeeping k)
    {
        if (Link is not { } link)
            return;
        KeepReport(ws, k, link);
        if (link._meetIn.Count > 0)
        {
            foreach (var payload in link._meetIn)
                try
                {
                    MeetArriveApply(ws, k, payload);
                }
                catch (Exception ex) when (ex is WorldFileException or IndexOutOfRangeException or ArgumentException or InvalidOperationException or IOException)
                {
                    Console.WriteLine($"[travel] a visitor's ship couldn't be read: {ex.Message}");
                }
            link._meetIn.Clear();
        }
        if (_visitors.Count == 0 || Space?.Flight is not { } f)
            return;
        for (var i = _visitors.Count - 1; i >= 0; i--)
        {
            var v = _visitors[i];
            var p = _clients.TryGetValue(RelayTransport.GuidFor(v.HostId).G, out var c) && Players.TryGetValue(c.Id, out var cp) ? cp : null;
            if (!v.Seated)
            {
                if (p != null && Constructs.Get(v.Ship) is { } mine && SeatedIn(p, mine))
                {
                    v.Seated = true;
                    if (mine.SpaceShip != 0 && f.Ships.IndexOfId(mine.SpaceShip) is var mi and >= 0)
                        f.Ships.SetState(mi, v.Body, v.Rel, v.Vel); // (on its way again, as it came)
                    continue;
                }
                if (Constructs.Get(v.Ship) is { SpaceShip: not 0 } held && f.Ships.IndexOfId(held.SpaceShip) is var hi and >= 0)
                    f.Ships.SetState(hi, v.Body, v.Rel, Vec3d.Zero); // (waits at the edge for its rider to connect)
                if (SandboxNow > v.Since + 120)
                    _visitors.RemoveAt(i); // (they never came: the ship stays here, theirs when they do)
                continue;
            }
            if (p == null)
            {
                _visitors.RemoveAt(i); // (gone: what they had stays in their record here)
                continue;
            }
            ref var h = ref SandboxHands.Hand(p.Id);
            if (!p.Alive || !h.Seated || Constructs.Get(h.SeatBody) is not { } ship)
                continue;
            if (ship.SpaceShip == 0)
            {
                v.Down = true;
                continue;
            }
            if (v.Down && f.Ships.IndexOfId(ship.SpaceShip) is var si and >= 0 && !(Local is { } me && SeatedIn(me, ship)))
                MeetLeaveSend(ws, k, f, ship, si);
        }
    }

    /// <summary>Keeper: a visitor's ship from the host, into this world's space at the edge of the sky, the visitor's record with it.</summary>
    private void MeetArriveApply(WorldSession ws, TravelKeeping k, byte[] payload)
    {
        if (Space?.Flight is not { } f || Link is not { } link)
            return;
        var r = new SaveReader(payload);
        var map = r.Str();
        var visitor = r.U8();
        var key = r.U64();
        var steam = r.U64();
        var name = r.Str();
        var body = r.I32();
        var rel = new Vec3d(r.F64(), r.F64(), r.F64());
        var vel = new Vec3d(r.F64(), r.F64(), r.F64());
        var shipFile = r.Bytes(r.I32());
        if (map != k.Map || body < 1 || body >= f.System.Count)
            throw new InvalidOperationException($"a visitor for {map} on a game keeping {k.Map}");

        var folder = Path.Combine(k.Folder, $"visit-{visitor}");
        Directory.CreateDirectory(Path.Combine(WorldStore.Root, folder));
        WorldStore.Write(folder, shipFile);
        var file = WorldStore.Read(folder, out _);
        TryDelete(folder);
        var idAs = new Dictionary<ushort, ushort>();
        int kept = 0, lost = 0;
        var skipped = WorldReadShip(ws, file, _ => true, _ => true, idAs, ref kept, ref lost);
        foreach (var (_, now) in idAs)
            if (Constructs.Get(now) is { } c)
            {
                if (c.CarriedBy != 0)
                    c.CarriedBy = idAs.TryGetValue(c.CarriedBy, out var up) ? up : (ushort)0;
                c.SpaceShip = 0;
                c.SpaceAway = 0;
                Constructs.StructureDirty.Add(c.Id);
            }
        Constructs.AfterLoad(Constructs.NextTagNow);
        if (skipped > 0)
            Constructs.RelinkAfterSkips();
        var theirs = WorldTravellerIn(ws, file, key, steam, ref lost);
        var ship = theirs != null && idAs.TryGetValue(theirs.SpaceSeatBody, out var seat) ? Constructs.Get(seat) : null;
        ship ??= idAs.Values.Select(Constructs.Get).FirstOrDefault(c => c is { CarriedBy: 0 });
        if (ship == null)
            throw new InvalidOperationException("the visitor's ship had nothing in it");

        // At the edge of this sky, a little outside, coming no closer until its rider sits in it.
        var edge = SpaceEdge;
        rel = rel.Normalized() * (edge + 100);
        var si = f.Ships.AddRelative(body, rel, Vec3d.Zero);
        if (si < 0)
            throw new InvalidOperationException("this game's space holds no more ships");
        ship.SpaceShip = f.Ships.Ids[si];
        ship.Wake();
        TravelPoseNow(ship, f, si, idAs.Values);

        var rec = ws.Find(key, steam) ?? ws.Add(key, steam, name);
        if (theirs != null)
            WorldTravellerCopy(theirs, rec);
        rec.HasPlace = true;
        rec.SpaceSeatBody = ship.Id;
        rec.SpaceSeatPart = theirs?.SpaceSeatPart ?? 0;
        rec.Spawned = false;
        rec.Online = 255;
        _visitors.RemoveAll(x => x.HostId == visitor);
        _visitors.Add(new Visitor { HostId = visitor, Ship = ship.Id, Body = body, Rel = rel, Vel = vel, Since = SandboxNow });
        link.TravelSend(link.Net.ServerGuid, PurposeArrived, [visitor]);
        Log($"{name} is coming in to land", true);
        Console.WriteLine($"[travel] {name}'s ship comes to {map}: construct {ship.Id} (ship {ship.SpaceShip}), {kept} constructs{(lost > 0 ? $", {lost} left out" : "")}");
    }

    /// <summary>Keeper: a visitor's ship flew out of this sky: it goes to the host, and its riders' games close the visit.</summary>
    private void MeetLeaveSend(WorldSession ws, TravelKeeping k, SpaceFlight f, Construct ship, int si)
    {
        if (Link is not { } link)
            return;
        var riders = new List<(byte Here, byte Host)>();
        foreach (var (guid, id) in _clients.Values)
            if (RelayTransport.HostIdOf(guid) is var host and not 255 && Players.TryGetValue(id, out var p) && SeatedIn(p, ship))
                riders.Add((id, host));
        if (riders.Count == 0)
            return;
        f.Ships.RelativeOf(si, out var primary, out var rel, out var vel);
        WorldSaveNow(wait: true);
        var folder = Path.Combine(k.Folder, $"leave-{ship.Id}");
        if (WorldCopy(ws, folder, c => Aboard(ship, c)) is { } why)
        {
            SandboxSay($"A visitor's ship couldn't be packed to go home: {why}");
            return;
        }
        var shipFile = File.ReadAllBytes(WorldStore.PathOf(folder));
        TryDelete(folder);
        var w = new SaveWriter();
        w.Str(k.Map).U8((byte)riders.Count);
        foreach (var (_, host) in riders)
            w.U8(host);
        w.I32(shipFile.Length).Bytes(shipFile).I32(primary);
        w.F64(rel.X).F64(rel.Y).F64(rel.Z).F64(vel.X).F64(vel.Y).F64(vel.Z);
        link.TravelSend(link.Net.ServerGuid, PurposeLeave, w.Span.ToArray());

        // Gone from here: the ship, and its riders (their records stay here, as anyone's who leaves).
        foreach (var (here, _) in riders)
            SandboxHands.Stand(Constructs, here);
        var gone = Constructs.Bodies.Where(c => Aboard(ship, c)).ToList();
        f.Ships.Remove(si);
        foreach (var c in gone)
            Constructs.Remove(c);
        foreach (var (here, host) in riders)
        {
            _visitors.RemoveAll(x => x.HostId == host);
            if (WorldSession is { } s)
                foreach (var rec in s.Players)
                    if (rec.Online == here)
                        rec.HasPlace = false;
            Net.Kick(RelayTransport.GuidFor(host));
        }
        Console.WriteLine($"[travel] construct {ship.Id} left {k.Map}'s sky with {riders.Count} visitor(s): {shipFile.Length} bytes back to the host");
    }

    // ---- Both sides of a traveller's PC: which game plays -----------------------------------------------------------

    /// <summary>
    /// A traveller's PC, after the games' updates: opens a planetoid the host handed over (a keeper), a visit to someone
    /// else's (a visitor), or goes back to the link. <paramref name="here"/> is the keeper's or visitor's game, if any;
    /// returns the game to play now (the link, or a new or the same one). Program.Travel.cs and the dev check run this.
    /// </summary>
    public static Game TravelStep(Game link, Game? here, out string? said)
    {
        said = null;
        if (link.PendingTravel is { } open && here is not { Keep: not null })
        {
            link.PendingTravel = null;
            here?.Dispose(); // (a visitor who keeps the planetoid now: their visit closes, their own game opens it)
            if (OpenKeeper(link, open, out var why) is { } keeper)
                return keeper;
            said = $"couldn't open {open.Map}: {why}";
            World.Enter(link.Place);
            return link;
        }
        if (link.PendingVisit is { } visit && here is not { Keep: not null })
        {
            link.PendingVisit = null;
            link.PendingHome = false;
            here?.Dispose();
            return OpenVisitor(link, visit);
        }
        if (here == null)
        {
            link.PendingHome = false;
            return link;
        }
        if (here.PendingReturn is { } back)
        {
            link.TravelReturn(back);
            here.Dispose();
            World.Enter(link.Place);
            return link;
        }
        if (here.Keep == null && (link.PendingHome || here.Disconnected))
        {
            said = here.Disconnected ? $"the visit to {here.Visit?.Map} ended ({here.Status})" : null;
            link.PendingHome = false;
            here.Dispose();
            World.Enter(link.Place);
            return link;
        }
        if (here.Keep == null && here.Local is { Alive: false } && !here.RespawnRequested && here.Phase == MatchPhase.Playing)
            here.RequestRespawn(); // (a visitor sits in its ship as it comes in: WorldOnSpawn on the keeper)
        return here;
    }

    /// <summary>A visitor's game: a client of the keeper's game, through the host (<see cref="RelayTransport"/>).</summary>
    public static Game OpenVisitor(Game link, TravelVisit visit)
    {
        var g = new Game(new NetPeer(RelayTransport.Joining(link, visit.Keeper)), link.Audio, link.LocalName, link.ChosenPrimary, link.ChosenSecondary)
        {
            LocalWorldKey = link.LocalWorldKey,
            Progress = link.Progress,
            ChosenHat = link.ChosenHat,
            ChosenLook = link.ChosenLook,
        };
        g.Link = link;
        g.Visit = visit;
        Console.WriteLine($"[travel] visiting {visit.Map} on player {visit.Keeper}'s game");
        return g;
    }
}
