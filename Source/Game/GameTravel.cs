using System.Numerics;
using CallOfSlop.Space;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>
/// Free travel, step 2 (docs/space/FREE-TRAVEL.md): a planetoid opened on the traveller's own game. When a ship in space
/// comes within the sky of another planetoid and the only person aboard is a client, the host doesn't move the whole
/// game: it sends that client the ship (a world file of the ship and what it carries, with the traveller's record and the
/// space clock) and the planetoid's kept save, takes the ship out of its own world, and keeps the traveller "away".
/// The client's game opens the planetoid as a server of its own (<see cref="Keep"/>, run by Program.Travel.cs beside
/// the link to the host) and lands the ship there as the group landing would. When the ship flies out of that sky
/// again, the client sends the ship and the planetoid back; the host keeps the planetoid in the world's
/// <c>planets/&lt;map&gt;</c> folder, puts the ship back in its space, and the traveller sits in it again.
/// Off unless the host turns it on (<see cref="FreeTravel"/>, <c>--free-travel</c>): step 5 makes it the default.
/// </summary>
public sealed partial class Game
{
    /// <summary>Host (dev, step 2): a ship with one client aboard lands on its own game rather than taking everyone.</summary>
    public bool FreeTravel;

    /// <summary>What this game keeps for the host: a planetoid opened on this PC for a traveller (null: an ordinary game).</summary>
    public TravelKeeping? Keep { get; private set; }

    /// <summary>Client: the host handed me a planetoid to open (Program.Travel.cs opens it and clears this).</summary>
    public TravelOpen? PendingTravel;

    /// <summary>Keeper: the ship flew out; what goes back to the host (Program.Travel.cs sends it through the link).</summary>
    public byte[]? PendingReturn;

    /// <summary>A planetoid handed to a traveller's game: the world's name, the map, the ship's file and the planetoid's.</summary>
    public sealed class TravelOpen
    {
        public string World = "", Map = "";
        public byte[] Ship = [];
        public byte[]? There;
    }

    /// <summary>A keeper's state: the map, its local folder, whether the ship has come down yet.</summary>
    public sealed class TravelKeeping
    {
        public string World = "", Map = "", Folder = "";
        public bool Down; // the ship has been a body in this sky: flying out again is going home
        public ushort Ship; // its construct, as it came in
    }

    private const byte TravelBegin = 0, TravelChunk = 1;
    private const byte PurposeOpen = 1, PurposeReturn = 2; // host -> traveller: keep this planetoid; keeper -> host: it and my ship, back
    private const byte PurposeArrive = 3, PurposeArrived = 4; // host -> keeper: a visitor's ship; keeper -> host: it is in, let them in (GameTravelMeet.cs)
    private const byte PurposeVisit = 5, PurposeLeave = 6, PurposeHome = 7; // host -> visitor: join the keeper; keeper -> host: a visitor's ship going; host -> visitor: you're back
    private const int TravelChunkBytes = 48 * 1024;
    private const int TravelMaxBytes = 64 * 1024 * 1024;

    private sealed class TravelIncoming
    {
        public byte Purpose;
        public byte[] Data = [];
        public int Got;
    }

    private readonly Dictionary<(ulong, uint), TravelIncoming> _travelIn = new();
    private uint _travelSeq;
    private (Construct Ship, Map To, byte Rider)? _travelNext;
    private readonly List<(byte Player, byte[] Payload)> _travelBack = new();

    /// <summary>The folder a traveller's game keeps a planetoid in (beside the worlds, never listed among them).</summary>
    public static string TravelFolder(string world, string map) => Path.Combine("_travel", WorldStore.FolderFor(world + " " + map));

    // ---- Sending large payloads in pieces ---------------------------------------------------------------------------

    private void TravelSend(RakNetGUID to, byte purpose, byte[] payload)
    {
        var id = ++_travelSeq;
        var w = new PacketWriter(64);
        w.Begin(Msg.Travel).U8(TravelBegin).U32(id).U8(purpose).U32((uint)payload.Length);
        Net.SendTo(to, w.Span, PacketReliability.ReliableOrdered);
        var chunk = new PacketWriter(TravelChunkBytes + 32);
        for (var at = 0; at < payload.Length; at += TravelChunkBytes)
        {
            var n = Math.Min(TravelChunkBytes, payload.Length - at);
            chunk.Begin(Msg.Travel).U8(TravelChunk).U32(id).U32((uint)at).U32((uint)n).Bytes(payload.AsSpan(at, n));
            Net.SendTo(to, chunk.Span, PacketReliability.ReliableOrdered);
        }
    }

    private void OnTravelPacket(RakNetGUID sender, ref PacketReader r)
    {
        var kind = r.U8();
        var id = r.U32();
        var key = (sender.G, id);
        if (kind == TravelBegin)
        {
            var purpose = r.U8();
            var total = (int)r.U32();
            if (total < 0 || total > TravelMaxBytes || purpose is < PurposeOpen or > PurposeHome || _travelIn.Count > 8)
                throw new MalformedPacketException("travel: a payload that can't be");
            if (IsServer != (purpose is PurposeReturn or PurposeArrived or PurposeLeave))
                return;
            _travelIn[key] = new TravelIncoming { Purpose = purpose, Data = new byte[total] };
            if (total == 0)
                TravelArrived(sender, _travelIn[key]);
            return;
        }
        if (kind != TravelChunk || !_travelIn.TryGetValue(key, out var t))
            return;
        var at = (int)r.U32();
        var n = (int)r.U32();
        if (at < 0 || n < 0 || at + n > t.Data.Length || n > r.Remaining)
            throw new MalformedPacketException("travel: a piece out of place");
        r.Take(n).CopyTo(t.Data.AsSpan(at, n));
        t.Got += n;
        if (t.Got >= t.Data.Length)
        {
            _travelIn.Remove(key);
            TravelArrived(sender, t);
        }
    }

    private void TravelArrived(RakNetGUID sender, TravelIncoming t)
    {
        if (t.Purpose == PurposeOpen && !IsServer)
        {
            var r = new SaveReader(t.Data);
            var open = new TravelOpen { World = r.Str(), Map = r.Str() };
            open.Ship = r.Bytes(r.I32());
            var thereLen = r.I32();
            open.There = thereLen > 0 ? r.Bytes(thereLen) : null;
            PendingTravel = open;
            Console.WriteLine($"[travel] the host handed this game {open.Map} to keep ({open.Ship.Length} bytes of ship, {open.There?.Length ?? 0} of planetoid)");
        }
        else if (t.Purpose == PurposeReturn && IsServer && _clients.TryGetValue(sender.G, out var c))
            _travelBack.Add((c.Id, t.Data));
        else
            MeetArrived(sender, t.Purpose, t.Data);
    }

    // ---- Host: a ship lands on its rider's own game ------------------------------------------------------------------

    /// <summary>
    /// Host, in the landing step: with free travel on, can this ship land on its rider's own game? The rider is the one
    /// person aboard, a client (the host's own game holds the world, so the host travels with everyone).
    /// </summary>
    private bool TravelAlone(Construct c, Map to)
    {
        if (TravelRider(c) is not (var rider and not 255))
            return false;
        _travelNext = (c, to, rider);
        return true;
    }

    /// <summary>Host, free travel: the one person aboard this ship, a client, when it may go to another game (255: it can't).</summary>
    private byte TravelRider(Construct c)
    {
        if (!FreeTravel || WorldSession is not { Applied: true })
            return 255;
        byte rider = 255;
        var people = 0;
        foreach (var p in Players.Values)
            if (p.Bot == null && SeatedIn(p, c))
            {
                people++;
                rider = p.Id;
            }
        if (people != 1 || rider == LocalId || GuidOfId(rider) is null || LandProblem(c, everyone: false) != null)
            return 255;
        return rider;
    }

    /// <summary>The connection of a client by player id (null: the host, a bot, nobody).</summary>
    private RakNetGUID? GuidOfId(byte id)
    {
        foreach (var c in _clients.Values)
            if (c.Id == id && c.Guid.G != 0)
                return c.Guid;
        return null;
    }

    /// <summary>Host, after the space tick: a ship due to land on its rider's game goes to them.</summary>
    internal void ServerTickTravel(double now)
    {
        TravelBackStep();
        MeetTick();
        if (_travelNext is not { } next)
            return;
        _travelNext = null;
        TravelSend(next.Ship, next.To, next.Rider); // (its own method: the lambdas there would make a closure each tick here)
    }

    private void TravelSend(Construct ship, Map to, byte rider)
    {
        if (WorldSession is not { } ws || Constructs.Get(ship.Id) != ship || ship.SpaceShip == 0 || !Players.TryGetValue(rider, out var p)
            || GuidOfId(rider) is not { } client)
            return;
        if (TravelPackShip(ws, ship, rider) is not { } shipFile)
        {
            SandboxHands.Notices.Add((rider, $"Couldn't land on {to.Name}: the ship wasn't saved"));
            return;
        }
        byte[]? there = null;
        var therePath = WorldStore.PathOf(StashFolder(ws, to.Name));
        try
        {
            if (File.Exists(therePath))
                there = File.ReadAllBytes(therePath);
        }
        catch (IOException ex)
        {
            Console.WriteLine($"[travel] {to.Name}'s kept builds couldn't be read ({ex.Message}): it opens empty");
        }
        var w = new SaveWriter();
        w.Str(ws.Meta.Name).Str(to.Name).I32(shipFile.Length).Bytes(shipFile).I32(there?.Length ?? 0);
        if (there != null)
            w.Bytes(there);
        TravelSend(client, PurposeOpen, w.Span.ToArray());
        var count = TravelAway(ws, ship, p, to);
        _keepers[to.Name] = rider;
        Log($"{p.Name} lands on {to.Name}", true);
        Console.WriteLine($"[travel] construct {ship.Id} (ship {ship.SpaceShip}) lands on {to.Name} on {p.Name}'s game: {count} constructs, {shipFile.Length + (there?.Length ?? 0)} bytes sent");
        BroadcastRoster();
    }

    /// <summary>Host: the ship and what it carries as a world file (with everyone's records and the space clock), or null.</summary>
    private byte[]? TravelPackShip(WorldSession ws, Construct ship, byte rider)
    {
        WorldSaveNow(wait: true);
        var shipFolder = Path.Combine("_travel", $"out-{rider}-{Environment.TickCount64}");
        var why = WorldCopy(ws, shipFolder, c => Aboard(ship, c));
        byte[]? file = null;
        try
        {
            if (why == null)
                file = File.ReadAllBytes(WorldStore.PathOf(shipFolder));
        }
        catch (IOException ex)
        {
            why = ex.Message;
        }
        TryDelete(shipFolder);
        if (why != null)
            Console.WriteLine($"[travel] construct {ship.Id} wasn't packed: {why}");
        return file;
    }

    /// <summary>Host: the ship leaves this world (its bodies, its ship in space) and its rider is away on <paramref name="to"/>.</summary>
    private int TravelAway(WorldSession ws, Construct ship, Player p, Map to)
    {
        SandboxHands.Stand(Constructs, p.Id);
        var gone = Constructs.Bodies.Where(c => Aboard(ship, c)).ToList();
        if (Space?.Flight is { } f && f.Ships.IndexOfId(ship.SpaceShip) is var si and >= 0)
            f.Ships.Remove(si);
        foreach (var c in gone)
            Constructs.Remove(c);
        p.Alive = false;
        p.Health = 0;
        p.AwayOn = to.Name;
        p.WantsRespawn = false;
        foreach (var rec in ws.Players)
            if (rec.Online == p.Id)
                rec.HasPlace = false;
        return gone.Count;
    }

    /// <summary>Host: ships and planetoids sent back by travellers' games, applied on the tick.</summary>
    private void TravelBackStep()
    {
        if (_travelBack.Count == 0)
            return;
        foreach (var (id, payload) in _travelBack)
            try
            {
                TravelBackApply(id, payload);
            }
            catch (Exception ex) when (ex is WorldFileException or IndexOutOfRangeException or ArgumentException or InvalidOperationException or IOException)
            {
                Console.WriteLine($"[travel] player {id}'s return couldn't be read: {ex.Message}");
                if (Players.TryGetValue(id, out var p))
                {
                    p.AwayOn = null; // (back at a spawn, without the ship)
                    p.WantsRespawn = true;
                }
            }
        _travelBack.Clear();
    }

    private void TravelBackApply(byte id, byte[] payload)
    {
        if (!Players.TryGetValue(id, out var p) || p.AwayOn == null || WorldSession is not { } ws || Space?.Flight is not { } f)
            return;
        var r = new SaveReader(payload);
        var map = r.Str();
        var shipFile = r.Bytes(r.I32());
        var planet = r.Bytes(r.I32());
        var primary = r.I32();
        var rel = new Vec3d(r.F64(), r.F64(), r.F64());
        var vel = new Vec3d(r.F64(), r.F64(), r.F64());
        if (map != p.AwayOn || primary < 0 || primary >= f.System.Count)
            throw new InvalidOperationException($"a return from {map} for a traveller on {p.AwayOn}");

        // The planetoid as they left it, kept for whoever goes there next.
        var stash = StashFolder(ws, map);
        Directory.CreateDirectory(Path.Combine(WorldStore.Root, stash));
        if (WorldStore.Write(stash, planet) is { } why)
            Console.WriteLine($"[travel] {map} wasn't kept: {why}");
        if (_keepers.TryGetValue(map, out var k) && k == id)
            _keepers.Remove(map);
        TravelShipHome(ws, f, map, shipFile, primary, rel, vel, [p]);
        Console.WriteLine($"[travel] {map} kept in {stash}");
    }

    /// <summary>
    /// Host: a ship back from a planetoid kept on someone's game, into free ids and this game's space where it left that
    /// sky, and each of <paramref name="riders"/> (away there until now) in their seat aboard with what they carry.
    /// </summary>
    private void TravelShipHome(WorldSession ws, SpaceFlight f, string map, byte[] shipFile, int primary, Vec3d rel, Vec3d vel, List<Player> riders)
    {
        var folder = Path.Combine("_travel", $"back-{riders[0].Id}-{Environment.TickCount64}");
        Directory.CreateDirectory(Path.Combine(WorldStore.Root, folder));
        WorldStore.Write(folder, shipFile);
        var body = WorldStore.Read(folder, out _);
        TryDelete(folder);
        var idAs = new Dictionary<ushort, ushort>();
        int kept = 0, lost = 0;
        var skipped = WorldReadShip(ws, body, _ => true, _ => true, idAs, ref kept, ref lost);
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
        var theirs = riders.Select(p => WorldTravellerIn(ws, body, WorldKeyOf(p), p.SteamId, ref lost)).ToList();
        var ship = theirs.FirstOrDefault(t => t != null && idAs.ContainsKey(t.SpaceSeatBody)) is { } first ? Constructs.Get(idAs[first.SpaceSeatBody]) : null;
        ship ??= idAs.Values.Select(Constructs.Get).FirstOrDefault(c => c is { CarriedBy: 0 });
        if (ship != null)
        {
            var si = f.Ships.AddRelative(primary, rel, vel);
            if (si >= 0)
            {
                ship.SpaceShip = f.Ships.Ids[si];
                TravelPoseNow(ship, f, si, idAs.Values);
            }
            ship.Wake();
        }
        for (var i = 0; i < riders.Count; i++)
        {
            var p = riders[i];
            var t = theirs[i];
            var mine = WorldRecordOf(ws, p);
            if (t != null)
                WorldTravellerCopy(t, mine);
            var seat = t != null && idAs.TryGetValue(t.SpaceSeatBody, out var s) ? Constructs.Get(s) : null;
            seat ??= i == 0 ? ship : null;
            mine.HasPlace = seat is { SpaceShip: not 0 };
            mine.SpaceSeatBody = seat?.Id ?? 0;
            mine.SpaceSeatPart = t?.SpaceSeatPart ?? 0;
            mine.Spawned = false;
            mine.Online = 255; // (WorldArrive below finds the record again and gives them its things)
            WorldArrive(ws, p);
            p.AwayOn = null;
            p.WantsRespawn = true; // (the spawn puts them in their seat: WorldOnSpawn)
            p.RespawnAt = 0;
            Log($"{p.Name} is back from {map}", true);
        }
        Console.WriteLine($"[travel] {string.Join(", ", riders.Select(p => p.Name))} back from {map}: {kept} constructs{(lost > 0 ? $", {lost} left out" : "")}, ship {ship?.SpaceShip ?? 0}");
        BroadcastRoster();
    }

    /// <summary>A traveller's record from a world file sent between games (key or Steam ID), with the seat they sit in there.</summary>
    private static WorldPlayer? WorldTravellerIn(WorldSession ws, byte[] body, ulong key, ulong steam, ref int lost)
    {
        var names = new WorldNamesRead();
        WorldPlayer? theirs = null;
        WorldSections(ws, body, (tag, rr) =>
        {
            if (tag == WorldSaveWriter.Names)
                names.Read(rr);
            else if (tag == WorldSaveWriter.Players)
            {
                var n = rr.Count(8);
                for (var k = 0; k < n; k++)
                    if (WorldSaveWriter.ReadPlayer(rr, names) is var rec && (rec.Key == key && key != 0 || rec.SteamId == steam && steam != 0))
                        theirs = rec;
            }
            else if (tag == WorldSaveWriter.SpaceSeats && theirs != null)
            {
                var n = rr.Count(12);
                for (var k = 0; k < n; k++)
                {
                    var k2 = rr.U64();
                    var seatBody = rr.U16();
                    var seatPart = rr.U16();
                    if (k2 == theirs.Key)
                        (theirs.SpaceSeatBody, theirs.SpaceSeatPart) = (seatBody, seatPart);
                }
            }
        }, ref lost);
        return theirs;
    }

    /// <summary>What a traveller carries, from the record their other game kept into this game's.</summary>
    private static void WorldTravellerCopy(WorldPlayer from, WorldPlayer to)
    {
        to.Parts = from.Parts;
        to.Materials = from.Materials;
        WorldStores.Copy(from.Pack, to.Pack);
        to.Tool = from.Tool;
        to.Held = from.Held;
        to.Builds = from.Builds;
        to.Health = from.Health;
    }

    private static void TryDelete(string folder)
    {
        try
        {
            Directory.Delete(Path.Combine(WorldStore.Root, folder), recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    // ---- The traveller's game: keeping a planetoid -------------------------------------------------------------------

    /// <summary>
    /// A new offline game for a planetoid the host handed over (Program.Travel.cs makes it): the files go to this PC's
    /// own folder for it, and the world loads as a landing does, the ship coming down through the sky.
    /// </summary>
    public string? KeepOpen(TravelOpen open)
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
            _landing = new LandingPlan { From = "space", To = open.Map, All = true, Travel = true };
            Keep = new TravelKeeping { World = open.World, Map = open.Map, Folder = folder };
            WorldAutosaveMinutes = 0; // (the host keeps the world: this copy goes back with the ship)
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or WorldFileException)
        {
            return ex.Message;
        }
    }

    /// <summary>Keeper, every tick: once down, the ship flying out of this sky again goes home with the planetoid.</summary>
    internal void KeepTick()
    {
        if (Keep is not { } k || PendingReturn != null || WorldSession is not { Applied: true } ws || Local is not { } me)
            return;
        MeetKeepTick(ws, k); // (visitors' ships coming in and going: GameTravelMeet.cs)
        if (!me.Alive && !me.WantsRespawn && Phase == MatchPhase.Playing)
            RequestRespawn(); // (the traveller sits in the ship as it comes in: WorldOnSpawn)
        ref var h = ref SandboxHands.Hand(me.Id);
        if (!me.Alive || !h.Seated || Constructs.Get(h.SeatBody) is not { } ship)
            return;
        if (ship.SpaceShip == 0)
        {
            k.Down = true;
            return;
        }
        if (!k.Down || Space?.Flight is not { } f || f.Ships.IndexOfId(ship.SpaceShip) is not (var si and >= 0))
            return;
        if (Net.ConnectionCount > 0)
        {
            if (SandboxNow > _keepSaidAt + 5)
            {
                _keepSaidAt = SandboxNow;
                SandboxSay($"Others are still on {k.Map}: it stays on your game until they leave");
            }
            return; // (the ship flies on in this space; it goes home once they have gone)
        }
        KeepLeave(k, ws, ship, f, si); // (its own method: the lambdas there would make a closure each tick here)
    }

    private void KeepLeave(TravelKeeping k, WorldSession ws, Construct ship, SpaceFlight f, int si)
    {
        f.Ships.RelativeOf(si, out var primary, out var rel, out var vel);
        WorldSaveNow(wait: true);
        var shipFolder = Path.Combine(k.Folder, "ship");
        var planetFolder = Path.Combine(k.Folder, "planet");
        var w1 = WorldCopy(ws, shipFolder, c => Aboard(ship, c));
        var w2 = WorldCopy(ws, planetFolder, c => !Aboard(ship, c));
        if (w1 != null || w2 != null)
        {
            SandboxSay($"The planetoid couldn't be packed to go home: {w1 ?? w2}");
            return;
        }
        var shipFile = File.ReadAllBytes(WorldStore.PathOf(shipFolder));
        var planetFile = File.ReadAllBytes(WorldStore.PathOf(planetFolder));
        var w = new SaveWriter();
        w.Str(k.Map).I32(shipFile.Length).Bytes(shipFile).I32(planetFile.Length).Bytes(planetFile).I32(primary);
        w.F64(rel.X).F64(rel.Y).F64(rel.Z).F64(vel.X).F64(vel.Y).F64(vel.Z);
        PendingReturn = w.Span.ToArray();
        Console.WriteLine($"[travel] ship {ship.SpaceShip} left {k.Map}'s sky: the ship ({shipFile.Length} bytes) and {k.Map} ({planetFile.Length} bytes) go back to the host");
    }

    /// <summary>
    /// The traveller's game for a planetoid the host handed over: an offline server on that map, playing Sandbox, the
    /// ship landing in it as the world loads. Null (and why) when the map or the files won't do. The new game is the
    /// entered world when this returns.
    /// </summary>
    public static Game? OpenKeeper(Game link, TravelOpen open, out string why)
    {
        if (Maps.Find(open.Map) is not { Planetoid: not null } map)
        {
            why = $"this build doesn't have the planetoid {open.Map}";
            return null;
        }
        var g = new Game(new NetPeer(RelayTransport.Hosting(link)), link.Audio, link.LocalName, link.ChosenPrimary, link.ChosenSecondary, bots: 0, mapId: map.Id)
        {
            LocalWorldKey = link.LocalWorldKey,
            MapVoteEnabled = false,
            Progress = link.Progress,
            ChosenHat = link.ChosenHat,
            ChosenLook = link.ChosenLook,
        };
        g.Mode = GameMode.Sandbox;
        g.Link = link;
        if (g.KeepOpen(open) is { } bad)
        {
            g.Dispose();
            World.Enter(link.Place);
            why = bad;
            return null;
        }
        why = "";
        Console.WriteLine($"[travel] keeping {open.Map} on this game for {open.World}");
        return g;
    }

    /// <summary>The link, as the keeper closes: what the keeper packed goes to the host.</summary>
    public void TravelReturn(byte[] payload)
    {
        if (!IsServer && Net.ServerGuid.G != 0)
            TravelSend(Net.ServerGuid, PurposeReturn, payload);
    }
}
