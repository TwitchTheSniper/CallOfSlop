using CallOfSlop.Space;

namespace CallOfSlop;

/// <summary>
/// Free travel, a planetoid's side (docs/space/FREE-TRAVEL.md, route 1): a server Game in the host's process for one
/// planetoid someone is on (<see cref="Host"/> is the host's main game, which ticks it after its own, GameTravel.cs). It
/// opens from the world's kept copy of that planetoid with the first ship landing in it (<see cref="KeepOpen"/>), holds
/// each ship at the edge of its sky until its riders have joined and sit in it again, and when a ship that came down flies
/// out of the sky again, hands it with its riders back to the host's space (<see cref="MeetLeaveSend"/>). Its players are
/// the travellers' games, joined over <see cref="RelayTransport"/> through their own connections to the host. It sends
/// the host's world folder a copy of itself every <see cref="KeepSaveEvery"/> seconds and as it closes.
/// </summary>
public sealed partial class Game
{
    /// <summary><c>Msg.Relay</c> kinds: a packet for the other game, a connection opened, one closed.</summary>
    public const byte RelayData = 0, RelayOpen = 1, RelayShut = 2;

    /// <summary>A planetoid's game: the host's main game it runs beside (null: an ordinary game).</summary>
    public Game? Host { get; private set; }

    /// <summary>A planetoid's game: its transport, which the host's main game hands travellers' packets to.</summary>
    public RelayTransport? PlaceNet { get; private set; }

    /// <summary>A planetoid's game: which planetoid it is, and how it is getting on (null: an ordinary game).</summary>
    public TravelKeeping? Keep { get; private set; }

    /// <summary>A planetoid opening on the host: the world's name, the map, the first ship's file and the planetoid's kept copy.</summary>
    public sealed class TravelOpen
    {
        public string World = "", Map = "";
        public byte[] Ship = [];
        public byte[]? There;
        public readonly List<(byte Id, ulong Key, ulong Steam)> Others = new(); // the riders, by id on the host: they join as it opens
    }

    /// <summary>A planetoid's game's state: the map, its number on the host, its scratch folder.</summary>
    public sealed class TravelKeeping
    {
        public string World = "", Map = "", Folder = "";
        public byte Number; // its number in Msg.Relay
        public bool Told; // open (its world loaded): the first riders have been sent here
        public List<(byte Id, ulong Key, ulong Steam)> Others = new(); // the first ship's riders: held in space until they sit in it
        public double SavedAt; // the last copy into the host's world folder
        public double EmptySince = -1; // nobody on it since (s; -1: someone is)
        public bool Done; // empty long enough: the host saves and closes it
    }

    /// <summary>How often a planetoid's game copies itself into the world's folder (s).</summary>
    public const double KeepSaveEvery = 120;

    /// <summary>A planetoid's game: ships the host sent in, taken in on its next tick.</summary>
    private readonly List<byte[]> _meetIn = new();

    /// <summary>A ship's rider: held at the edge of the sky until they sit in it, then watched until it leaves.</summary>
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

    /// <summary>The folder a planetoid's game keeps its world in while it runs (beside the worlds, never listed among them).</summary>
    public static string TravelFolder(string world, string map) => Path.Combine("_travel", WorldStore.FolderFor(world + " " + map));

    /// <summary>
    /// A new planetoid's game (GameTravel.cs makes it): the files go to its own folder, and the world loads as a landing
    /// does, the ship coming down through the sky.
    /// </summary>
    private string? KeepOpen(TravelOpen open, byte number)
    {
        var folder = TravelFolder(open.World, open.Map);
        try
        {
            TryDelete(folder);
            Directory.CreateDirectory(Path.Combine(WorldStore.Root, folder));
            if (WorldStore.Write(folder, open.Ship) is { } why)
                return why;
            if (open.There != null)
            {
                var stash = Path.Combine(folder, "planets", WorldStore.FolderFor(open.Map));
                Directory.CreateDirectory(Path.Combine(WorldStore.Root, stash));
                if (WorldStore.Write(stash, open.There) is { } why2)
                    return why2;
            }
            WorldStore.Read(folder, out var meta);
            var ws = new WorldSession { Folder = folder, Meta = meta, HasSave = true, PlayedBefore = meta.PlayedSeconds };
            WorldAttach(ws);
            _landing = new LandingPlan { From = "space", To = open.Map, All = true, Travel = true, KeepPlaces = true }; // (the riders keep their seats: they sit in them again as they join)
            Keep = new TravelKeeping { World = open.World, Map = open.Map, Folder = folder, Number = number, Others = [.. open.Others] };
            WorldAutosaveMinutes = 0; // (the host keeps the world: copies go to its folder, PlaceSave)
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or WorldFileException)
        {
            return ex.Message;
        }
    }

    /// <summary>A planetoid's game, every tick: ships coming in, waiting for their riders, going home; copies; closing when empty.</summary>
    internal void KeepTick()
    {
        if (Keep is not { } k || WorldSession is not { Applied: true } ws || Host is not { } host)
            return;
        if (!k.Told)
        {
            k.Told = true;
            k.SavedAt = SandboxNow;
            KeepOpened(ws, k, host);
        }
        else if (SandboxNow >= k.SavedAt + KeepSaveEvery)
        {
            k.SavedAt = SandboxNow;
            PlaceSave();
        }
        if (_meetIn.Count > 0)
            MeetInStep(ws, k);
        if (_visitors.Count > 0 && Space?.Flight is { } f)
            VisitorsStep(ws, k, f);
        if (PlaceNet is { ConnectionCount: > 0 } || _visitors.Count > 0)
            k.EmptySince = -1;
        else if (k.EmptySince < 0)
            k.EmptySince = SandboxNow;
        else if (SandboxNow >= k.EmptySince + PlaceEmptyClose)
            k.Done = true;
    }

    /// <summary>A planetoid's game as it opens: the first ship waits in space for its riders, who are sent here now.</summary>
    private void KeepOpened(WorldSession ws, TravelKeeping k, Game host)
    {
        KeepHoldOthers(ws, k);
        foreach (var (id, _, _) in k.Others)
            host.PlaceVisit(id, this);
    }

    private void MeetInStep(WorldSession ws, TravelKeeping k)
    {
        foreach (var payload in _meetIn)
            try
            {
                MeetArriveApply(ws, k, payload);
            }
            catch (Exception ex) when (ex is WorldFileException or IndexOutOfRangeException or ArgumentException or InvalidOperationException or IOException)
            {
                Console.WriteLine($"[travel] a ship for {k.Map} couldn't be read: {ex.Message}");
            }
        _meetIn.Clear();
    }

    /// <summary>A planetoid's game: the first ship's riders' records say where they sit; the ship waits in space until they do.</summary>
    private void KeepHoldOthers(WorldSession ws, TravelKeeping k)
    {
        if (k.Others.Count == 0 || Space?.Flight is not { } f || !StarSystems.ForMap(k.Map, out _, out var body))
            return;
        foreach (var (id, key, steam) in k.Others)
        {
            if (ws.Find(key, steam) is not { SpaceSeatBody: not 0 } rec || Constructs.Get(rec.SpaceSeatBody) is not { SpaceShip: not 0 } ship
                || f.Ships.IndexOfId(ship.SpaceShip) is not (var si and >= 0))
                continue;
            f.System.StateOf(body, f.Ships.Time, out var bp, out var bv);
            _visitors.RemoveAll(x => x.HostId == id);
            _visitors.Add(new Visitor { HostId = id, Ship = ship.Id, Body = body, Rel = f.Ships.PositionOf(si) - bp, Vel = f.Ships.VelocityOf(si) - bv, Since = SandboxNow });
        }
    }

    /// <summary>Is anyone still to sit in this ship (it waits at the edge until they have)?</summary>
    private bool AnyUnseatedIn(ushort ship)
    {
        foreach (var v in _visitors)
            if (v.Ship == ship && !v.Seated)
                return true;
        return false;
    }

    /// <summary>A planetoid's game, every tick with riders here: ships held for them, let go once they sit, sent home once they fly out.</summary>
    private void VisitorsStep(WorldSession ws, TravelKeeping k, SpaceFlight f)
    {
        for (var i = _visitors.Count - 1; i >= 0 && i < _visitors.Count; i--)
        {
            var v = _visitors[i];
            var p = _clients.TryGetValue(RelayTransport.GuidFor(v.HostId).G, out var c) && Players.TryGetValue(c.Id, out var cp) ? cp : null;
            if (!v.Seated)
            {
                if (p != null && Constructs.Get(v.Ship) is { } mine && SeatedIn(p, mine))
                {
                    v.Seated = true;
                    if (mine.SpaceShip != 0 && f.Ships.IndexOfId(mine.SpaceShip) is var mi and >= 0 && !AnyUnseatedIn(v.Ship))
                        f.Ships.SetState(mi, v.Body, v.Rel, v.Vel); // (everyone aboard is in: on its way again, as it came)
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
            if (v.Down && f.Ships.IndexOfId(ship.SpaceShip) is var si and >= 0)
                MeetLeaveSend(ws, k, f, ship, si);
        }
    }

    /// <summary>A planetoid's game: a ship from the host, into this world's space at the edge of the sky, its riders' records with it.</summary>
    private void MeetArriveApply(WorldSession ws, TravelKeeping k, byte[] payload)
    {
        if (Space?.Flight is not { } f || Host is not { } host)
            return;
        var r = new SaveReader(payload);
        var map = r.Str();
        var n = r.U8();
        var who = new List<(byte Id, ulong Key, ulong Steam, string Name)>(n);
        for (var i = 0; i < n; i++)
            who.Add((r.U8(), r.U64(), r.U64(), r.Str()));
        var body = r.I32();
        var rel = new Vec3d(r.F64(), r.F64(), r.F64());
        var vel = new Vec3d(r.F64(), r.F64(), r.F64());
        var shipFile = r.Bytes(r.I32());
        if (map != k.Map || body < 1 || body >= f.System.Count || who.Count == 0)
            throw new InvalidOperationException($"a ship for {map} on the game of {k.Map}");

        var folder = Path.Combine(k.Folder, $"visit-{who[0].Id}");
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
        var theirs = who.Select(x => WorldTravellerIn(ws, file, x.Key, x.Steam, ref lost)).ToList();
        var ship = theirs.FirstOrDefault(t => t != null && idAs.ContainsKey(t.SpaceSeatBody)) is { } first ? Constructs.Get(idAs[first.SpaceSeatBody]) : null;
        ship ??= idAs.Values.Select(Constructs.Get).FirstOrDefault(c => c is { CarriedBy: 0 });
        if (ship == null)
            throw new InvalidOperationException("the ship had nothing in it");

        // At the edge of this sky, a little outside, coming no closer until its riders sit in it.
        rel = rel.Normalized() * (SpaceEdge + 100);
        var si = f.Ships.AddRelative(body, rel, Vec3d.Zero);
        if (si < 0)
            throw new InvalidOperationException("this game's space holds no more ships");
        ship.SpaceShip = f.Ships.Ids[si];
        ship.Wake();
        TravelPoseNow(ship, f, si, idAs.Values);

        for (var i = 0; i < who.Count; i++)
        {
            var (id, key, steam, name) = who[i];
            var t = theirs[i];
            var rec = ws.Find(key, steam) ?? ws.Add(key, steam, name);
            if (t != null)
                WorldTravellerCopy(t, rec);
            var seat = t != null && idAs.TryGetValue(t.SpaceSeatBody, out var sb) ? Constructs.Get(sb) : null;
            seat ??= ship;
            rec.HasPlace = true;
            rec.SpaceSeatBody = seat.Id;
            rec.SpaceSeatPart = t?.SpaceSeatPart ?? 0;
            rec.Spawned = false;
            rec.Online = 255;
            _visitors.RemoveAll(x => x.HostId == id);
            _visitors.Add(new Visitor { HostId = id, Ship = seat.Id, Body = body, Rel = rel, Vel = vel, Since = SandboxNow });
            host.PlaceVisit(id, this);
            Log($"{name} is coming in to land", true);
        }
        Console.WriteLine($"[travel] a ship comes to {map} with {string.Join(", ", who.Select(x => x.Name))}: construct {ship.Id} (ship {ship.SpaceShip}), {kept} constructs{(lost > 0 ? $", {lost} left out" : "")}");
    }

    /// <summary>A planetoid's game: a ship flew out of this sky: it goes to the host's space, and its riders' games close the visit.</summary>
    private void MeetLeaveSend(WorldSession ws, TravelKeeping k, SpaceFlight f, Construct ship, int si)
    {
        if (Host is not { } host)
            return;
        var riders = new List<(byte Here, byte Host)>();
        foreach (var (guid, id) in _clients.Values)
            if (RelayTransport.HostIdOf(guid) is var hid and not 255 && Players.TryGetValue(id, out var p) && SeatedIn(p, ship))
                riders.Add((id, hid));
        if (riders.Count == 0)
            return;
        f.Ships.RelativeOf(si, out var primary, out var rel, out var vel);
        WorldSaveNow(wait: true);
        var folder = Path.Combine(k.Folder, $"leave-{ship.Id}");
        if (WorldCopy(ws, folder, c => Aboard(ship, c)) is { } why)
        {
            SandboxSay($"A ship couldn't be packed to go home: {why}");
            return;
        }
        var shipFile = File.ReadAllBytes(WorldStore.PathOf(folder));
        TryDelete(folder);
        var w = new SaveWriter();
        w.Str(k.Map).U8((byte)riders.Count);
        foreach (var (_, hid) in riders)
            w.U8(hid);
        w.I32(shipFile.Length).Bytes(shipFile).I32(primary);
        w.F64(rel.X).F64(rel.Y).F64(rel.Z).F64(vel.X).F64(vel.Y).F64(vel.Z);
        host._placeLeave.Add((k.Number, w.Span.ToArray()));

        // Gone from here: the ship, and its riders (their records stay here, as anyone's who leaves).
        foreach (var (here, _) in riders)
            SandboxHands.Stand(Constructs, here);
        var gone = Constructs.Bodies.Where(c => Aboard(ship, c)).ToList();
        f.Ships.Remove(si);
        foreach (var c in gone)
            Constructs.Remove(c, keepRiders: true); // (its riders went with its copy)
        foreach (var (here, hid) in riders)
        {
            _visitors.RemoveAll(x => x.HostId == hid);
            foreach (var rec in ws.Players)
                if (rec.Online == here)
                    rec.HasPlace = false;
            Net.Kick(RelayTransport.GuidFor(hid));
        }
        Console.WriteLine($"[travel] construct {ship.Id} left {k.Map}'s sky with {riders.Count} aboard: {shipFile.Length} bytes back to the host's space");
    }

    /// <summary>A planetoid's game (entered): a copy of the whole planetoid into the host's world folder, kept for next time.</summary>
    internal void PlaceSave()
    {
        if (Keep is not { } k || Host?.WorldSession is not { } hws || WorldSession is not { Applied: true } ws)
            return;
        WorldSaveNow(wait: true);
        if (WorldCopy(ws, StashFolder(hws, k.Map), _ => true) is { } why)
            Console.WriteLine($"[travel] {k.Map} wasn't kept in the world's folder: {why}");
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
}
