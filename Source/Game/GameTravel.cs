using CallOfSlop.Space;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>
/// Free travel, the host's side (docs/space/FREE-TRAVEL.md; route 1, Karl 2026-11-05: the host runs every planetoid).
/// When a ship in space comes within the sky of another planetoid and everyone aboard is a client, the host doesn't move
/// the whole game: that planetoid runs as a Game of its own in the host's process (<see cref="Places"/>, opened from the
/// world's kept <c>planets/&lt;map&gt;</c> copy, GameTravelPlace.cs), the ship moves into it, and the riders' PCs open a
/// client game for it that talks to it over their own connection to the host (<c>Msg.Relay</c>, RelayTransport.cs,
/// GameTravelVisit.cs). Flying out of that sky again, the ship comes back into this game's space with them in it. A
/// planetoid nobody is on is saved and closed after <see cref="PlaceEmptyClose"/> seconds (Karl, 2026-11-05).
/// On unless the host ticks "Travel together" (<see cref="FreeTravel"/>, Settings.TravelTogether; <c>--free-travel</c> forces it on).
/// </summary>
public sealed partial class Game
{
    /// <summary>Host: a ship with only clients aboard lands on a planetoid of its own rather than taking everyone (off: "Travel together").</summary>
    public bool FreeTravel;

    /// <summary>Host: the planetoids running beside this game, one Game each (GameTravelPlace.cs).</summary>
    public IReadOnlyList<Game> Places => _places;

    private readonly List<Game> _places = new(4);

    /// <summary>Seconds a planetoid with nobody on it keeps running before it is saved and closed.</summary>
    public const double PlaceEmptyClose = 30;

    /// <summary><c>Msg.Travel</c> purposes, host to traveller: join a planetoid's game; you're back in the host's space.</summary>
    private const byte PurposeVisit = 1, PurposeHome = 2;

    private (Construct Ship, Map To, List<byte> Riders, int Body)? _travelNext;

    /// <summary>Host: travellers and the planetoid (by number) each is on: relayed packets go only there.</summary>
    private readonly Dictionary<byte, byte> _travelOn = new();

    /// <summary>Host: ships the planetoids sent back on their ticks, taken in after them.</summary>
    private readonly List<(byte Place, byte[] Payload)> _placeLeave = new();

    private readonly PacketWriter _relayW = new(4096);
    private readonly PacketWriter _relayCtl = new(16);
    private byte _placeSeq;
    private Audio? _placeQuiet;
    private double _travelNow;

    /// <summary>The planetoid running beside this game on <paramref name="map"/> (host; null: none).</summary>
    public Game? PlaceOf(string map)
    {
        foreach (var g in _places)
            if (g.Keep!.Map == map)
                return g;
        return null;
    }

    private Game? PlaceNumbered(byte number)
    {
        foreach (var g in _places)
            if (g.Keep!.Number == number)
                return g;
        return null;
    }

    // ---- Into a planetoid's sky -------------------------------------------------------------------------------------

    /// <summary>
    /// Host, in the landing step: with free travel on, can this ship land on a planetoid of its own? The riders are
    /// everyone aboard, all clients (the host's own game holds space and its own planetoid, so the host travels with
    /// everyone).
    /// </summary>
    private bool TravelToPlace(Construct c, Map to, int body)
    {
        if (!TravelRiders(c, _ridersScratch))
            return false;
        _travelNext = (c, to, [.. _ridersScratch], body);
        return true;
    }

    private readonly List<byte> _ridersScratch = new(8); // (the landing step asks every tick a ship waits at an edge: no allocation)

    /// <summary>Host, free travel: the people aboard this ship, by id, when they may go to a planetoid of their own (false: they can't).</summary>
    private bool TravelRiders(Construct c, List<byte> into)
    {
        into.Clear();
        if (!FreeTravel || WorldSession is not { Applied: true })
            return false;
        foreach (var p in Players.Values)
            if (SeatedIn(p, c))
            {
                if (p.Bot != null || p.Id == LocalId || GuidOfId(p.Id) is null)
                    return false; // (a bot or the host aboard: the group landing, as without free travel)
                into.Add(p.Id);
            }
        into.Sort();
        return into.Count > 0 && LandProblem(c, everyone: false) == null;
    }

    /// <summary>The connection of a client by player id (null: the host, a bot, nobody).</summary>
    private RakNetGUID? GuidOfId(byte id)
    {
        foreach (var c in _clients.Values)
            if (c.Id == id && c.Guid.G != 0)
                return c.Guid;
        return null;
    }

    /// <summary>A client's connection by player id, for a planetoid's transport (RelayTransport.cs).</summary>
    internal RakNetGUID? ClientGuid(byte id) => GuidOfId(id);

    /// <summary>
    /// Host, every frame after its own ticks (Game.Update): a ship due to land goes to its planetoid, the planetoids tick
    /// in their own worlds, ships they sent back come into this game's space, and empty ones close. Nothing to do: no cost.
    /// </summary>
    private void UpdatePlaces(double now, double frameDt)
    {
        if (_travelNext == null && _places.Count == 0)
            return;
        _travelNow = now;
        if (_travelNext is { } next)
        {
            _travelNext = null;
            TravelSend(next.Ship, next.To, next.Riders, next.Body);
        }
        if (_places.Count > 0)
            TickPlaces(now, frameDt);
    }

    private void TickPlaces(double now, double frameDt)
    {
        for (var i = _places.Count - 1; i >= 0; i--)
        {
            var g = _places[i];
            g.Update(now, frameDt);
            if (g.Keep is { Done: true })
                ClosePlace(g, "nobody is on it");
        }
        World.Enter(Place);
        if (_placeLeave.Count > 0)
            PlaceLeaveStep();
    }

    private void TravelSend(Construct ship, Map to, List<byte> riders, int body)
    {
        var rider = riders[0];
        if (WorldSession is not { } ws || Space?.Flight is not { } f || Constructs.Get(ship.Id) != ship || ship.SpaceShip == 0
            || f.Ships.IndexOfId(ship.SpaceShip) is not (var si and >= 0) || !Players.TryGetValue(rider, out var p))
            return;
        var all = riders.Select(id => Players.GetValueOrDefault(id)).OfType<Player>().ToList();
        if (all.Count != riders.Count)
            return;
        if (TravelPackShip(ws, ship, rider) is not { } shipFile)
        {
            SandboxHands.Notices.Add((rider, $"Couldn't land on {to.Name}: the ship wasn't saved"));
            return;
        }
        var place = PlaceOf(to.Name);
        var opened = place == null;
        if (place == null)
        {
            byte[]? there = null;
            try
            {
                var therePath = WorldStore.PathOf(StashFolder(ws, to.Name));
                if (File.Exists(therePath))
                    there = File.ReadAllBytes(therePath);
            }
            catch (IOException ex)
            {
                Console.WriteLine($"[travel] {to.Name}'s kept builds couldn't be read ({ex.Message}): it opens empty");
            }
            var open = new TravelOpen { World = ws.Meta.Name, Map = to.Name, Ship = shipFile, There = there };
            foreach (var o in all)
                open.Others.Add((o.Id, WorldKeyOf(o), TrustedSteamId(o)));
            place = OpenPlace(open, out var why);
            if (place == null)
            {
                SandboxHands.Notices.Add((rider, $"Couldn't land on {to.Name}: {why}"));
                Console.WriteLine($"[travel] {to.Name} didn't open: {why}");
                return;
            }
        }
        else
        {
            // Into the planetoid already running: its game takes the ship on its next tick (MeetArriveApply).
            f.System.StateOf(body, f.Ships.Time, out var bp, out var bv);
            var rel = f.Ships.PositionOf(si) - bp;
            var vel = f.Ships.VelocityOf(si) - bv;
            var w = new SaveWriter();
            w.Str(to.Name).U8((byte)all.Count);
            foreach (var o in all)
                w.U8(o.Id).U64(WorldKeyOf(o)).U64(TrustedSteamId(o)).Str(o.Name);
            w.I32(body).F64(rel.X).F64(rel.Y).F64(rel.Z).F64(vel.X).F64(vel.Y).F64(vel.Z);
            w.I32(shipFile.Length).Bytes(shipFile);
            place._meetIn.Add(w.Span.ToArray());
        }
        var count = TravelAway(ws, ship, all, to);
        foreach (var o in all)
        {
            _travelOn[o.Id] = place.Keep!.Number;
            Log($"{o.Name} lands on {to.Name}", true);
        }
        TravelRadio($"{string.Join(" and ", all.Select(x => x.Name))} landed on {to.Name}");
        Console.WriteLine($"[travel] construct {ship.Id} (ship {ship.SpaceShip}) lands on {to.Name} ({(opened ? "opened now" : "already running")}, place {place.Keep!.Number}) with {all.Count} aboard: {count} constructs, {shipFile.Length} bytes");
        BroadcastRoster();
    }

    /// <summary>Host: a planetoid's Game in this process, its ship and riders landing as it opens (null, and why: it can't).</summary>
    private Game? OpenPlace(TravelOpen open, out string why)
    {
        if (Maps.Find(open.Map) is not { Planetoid: not null } map)
        {
            why = $"this build doesn't have the planetoid {open.Map}";
            return null;
        }
        byte number;
        do
            number = ++_placeSeq;
        while (number == 0 || number == 255 || PlaceNumbered(number) != null);
        _placeQuiet ??= new Audio(); // (never started: a planetoid nobody here is on plays nothing)
        var net = RelayTransport.OnHost(this, number);
        var g = new Game(new NetPeer(net), _placeQuiet, LocalName, ChosenPrimary, ChosenSecondary, bots: 0, mapId: map.Id)
        {
            LocalWorldKey = 0, // (the host's own player there is never in play: it has no record)
            MapVoteEnabled = false,
            Progress = Progress,
        };
        g.Mode = GameMode.Sandbox;
        g.Host = this;
        g.PlaceNet = net;
        if (g.KeepOpen(open, number) is { } bad)
        {
            g.Dispose();
            World.Enter(Place);
            why = bad;
            return null;
        }
        if (g.Local is { } nobody)
            nobody.AwayOn = "space"; // (never respawned, never waited for by a landing)
        World.Enter(Place);
        _places.Add(g);
        why = "";
        Console.WriteLine($"[travel] {open.Map} runs on the host as place {number}");
        return g;
    }

    /// <summary>Host: a planetoid saved into the world's folder and closed; anyone still there comes back at a spawn.</summary>
    private void ClosePlace(Game g, string why)
    {
        var k = g.Keep!;
        World.Enter(g.Place);
        g.PlaceSave();
        g.Dispose();
        World.Enter(Place);
        _places.Remove(g);
        TryDelete(k.Folder);
        foreach (var (id, n) in _travelOn.ToList())
            if (n == k.Number)
            {
                _travelOn.Remove(id);
                if (Players.TryGetValue(id, out var p) && p.AwayOn != null)
                {
                    p.AwayOn = null;
                    p.WantsRespawn = true;
                    p.RespawnAt = 0;
                    SandboxHands.Notices.Add((id, $"{k.Map} closed: you're back at a spawn"));
                    if (GuidOfId(id) is { } at)
                        TravelSend(at, PurposeHome, []);
                }
            }
        Console.WriteLine($"[travel] {k.Map} (place {k.Number}) saved and closed: {why}");
        BroadcastRoster();
    }

    /// <summary>Host, as it closes: every planetoid running beside it is saved and closed.</summary>
    private void ClosePlaces()
    {
        for (var i = _places.Count - 1; i >= 0; i--)
            ClosePlace(_places[i], "the host is closing");
    }

    /// <summary>A planetoid's game, opened (its world loaded, the ship in its space): the riders join it now.</summary>
    internal void PlaceVisit(byte id, Game place)
    {
        if (WorldSession is not { } ws || place.Keep is not { } k || GuidOfId(id) is not { } at || !Players.TryGetValue(id, out var p)
            || p.AwayOn != k.Map || _travelOn.GetValueOrDefault(id, (byte)0) != k.Number)
        {
            Console.WriteLine($"[travel] player {id} isn't sent to {place.Keep?.Map}: gone, or no longer on their way there");
            return;
        }
        var w = new SaveWriter(256);
        w.Str(ws.Meta.Name).Str(k.Map).U8(k.Number);
        TravelSend(at, PurposeVisit, w.Span.ToArray());
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

    /// <summary>Host: the ship leaves this world (its bodies, its ship in space) and its riders are away on <paramref name="to"/>.</summary>
    private int TravelAway(WorldSession ws, Construct ship, List<Player> riders, Map to)
    {
        foreach (var p in riders)
            SandboxHands.Stand(Constructs, p.Id);
        var gone = Constructs.Bodies.Where(c => Aboard(ship, c)).ToList();
        if (Space?.Flight is { } f && f.Ships.IndexOfId(ship.SpaceShip) is var si and >= 0)
            f.Ships.Remove(si);
        foreach (var c in gone)
            Constructs.Remove(c, keepRiders: true); // (its riders went with its copy)
        foreach (var p in riders)
        {
            p.Alive = false;
            p.Health = 0;
            p.AwayOn = to.Name;
            p.WantsRespawn = false;
            foreach (var rec in ws.Players)
                if (rec.Online == p.Id)
                    rec.HasPlace = false;
        }
        return gone.Count;
    }

    // ---- Out of a planetoid's sky -----------------------------------------------------------------------------------

    /// <summary>Host, after the planetoids' ticks: ships they sent back, into this game's space with their riders in them.</summary>
    private void PlaceLeaveStep()
    {
        foreach (var (from, payload) in _placeLeave)
            try
            {
                PlaceLeaveApply(from, payload);
            }
            catch (Exception ex) when (ex is WorldFileException or IndexOutOfRangeException or ArgumentException or InvalidOperationException or IOException)
            {
                Console.WriteLine($"[travel] a ship from place {from} couldn't be read: {ex.Message}");
            }
        _placeLeave.Clear();
    }

    private void PlaceLeaveApply(byte place, byte[] payload)
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
            if (_travelOn.TryGetValue(id, out var k) && k == place && Players.TryGetValue(id, out var p) && p.AwayOn == map)
                riders.Add(p);
        }
        var shipFile = r.Bytes(r.I32());
        var primary = r.I32();
        var rel = new Vec3d(r.F64(), r.F64(), r.F64());
        var vel = new Vec3d(r.F64(), r.F64(), r.F64());
        if (riders.Count == 0 || primary < 0 || primary >= f.System.Count)
            throw new InvalidOperationException($"a ship from {map} for nobody there");
        TravelShipHome(ws, f, map, shipFile, primary, rel, vel, riders);
        foreach (var p in riders)
        {
            _travelOn.Remove(p.Id);
            if (GuidOfId(p.Id) is { } at)
                TravelSend(at, PurposeHome, []);
        }
    }

    /// <summary>
    /// Host: a ship back from a planetoid, into free ids and this game's space where it left that sky, and each of
    /// <paramref name="riders"/> (away there until now) in their seat aboard with what they carry.
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
        var theirs = riders.Select(p => WorldTravellerIn(ws, body, WorldKeyOf(p), TrustedSteamId(p), ref lost)).ToList();
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
        TravelRadio($"{string.Join(" and ", riders.Select(p => p.Name))} left {map}");
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
        to.Made = from.Made;
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

    // ---- Messages and relayed packets -------------------------------------------------------------------------------

    private void TravelSend(RakNetGUID to, byte purpose, byte[] payload)
    {
        var w = new PacketWriter(payload.Length + 8);
        w.Begin(Msg.Travel).U8(purpose).Bytes(payload);
        Net.SendTo(to, w.Span, PacketReliability.ReliableOrdered);
    }

    /// <summary>Host: a client left. If they were on a planetoid, its game hears it (what they had stays in their record there).</summary>
    private void TravelGone(byte id)
    {
        if (_travelOn.Remove(id, out var n) && PlaceNumbered(n) is { PlaceNet: { } t })
            t.Closed(id);
    }

    /// <summary>Host: a traveller's packet for the planetoid they're on, handed straight to that planetoid's game.</summary>
    private void OnRelayHost(RakNetGUID sender, byte kind, byte place, ref PacketReader r)
    {
        if (!_clients.TryGetValue(sender.G, out var c) || !_travelOn.TryGetValue(c.Id, out var on) || on != place
            || PlaceNumbered(place) is not { PlaceNet: { } t })
            return;
        if (kind == RelayData)
        {
            var how = (PacketReliability)r.U8();
            if (how is not (PacketReliability.Unreliable or PacketReliability.UnreliableSequenced or PacketReliability.Reliable
                or PacketReliability.ReliableOrdered or PacketReliability.ReliableSequenced))
                throw new MalformedPacketException("relay: no such reliability");
            t.Deliver(c.Id, r.Take(r.Remaining));
        }
        else if (kind == RelayOpen)
            t.Opened(c.Id);
        else if (kind == RelayShut)
            t.Closed(c.Id);
    }

    /// <summary>Host: a planetoid's packet for one of its travellers, down their connection with the planetoid's number.</summary>
    internal void PlaceRelaySend(byte place, byte to, ReadOnlySpan<byte> data, PacketReliability how)
    {
        if (GuidOfId(to) is not { } at)
            return;
        _relayW.Begin(Msg.Relay).U8(RelayData).U8(place);
        _relayW.Reserve(data.Length);
        _relayW.Bytes(data);
        Net.SendTo(at, _relayW.Span, how);
    }

    /// <summary>Host: a planetoid's game closed its connection to a traveller (or opened one).</summary>
    internal void PlaceRelayControl(byte place, byte kind, byte to)
    {
        if (GuidOfId(to) is { } at)
            RelayTo(at, kind, place);
    }

    private void RelayTo(RakNetGUID to, byte kind, byte from)
    {
        _relayCtl.Begin(Msg.Relay).U8(kind).U8(from);
        Net.SendTo(to, _relayCtl.Span, PacketReliability.ReliableOrdered);
    }

    // ---- Who is where -----------------------------------------------------------------------------------------------

    /// <summary>Host: a line on everyone's radio ([RADIO] in the chat) about who went where.</summary>
    private void TravelRadio(string line)
    {
        line = ChatRules.Clean(line);
        if (line.Length == 0)
            return;
        AddChat(ChatRules.SystemId, "", line, _travelNow, ChatFlags.System | ChatFlags.Radio); // (a [RADIO] line, as the story's news: GameStoryTalk.cs)
        if (Net.IsOffline)
            return;
        _w.Begin(Msg.ChatLine).U8(ChatRules.SystemId).Str(line).U8((byte)(ChatFlags.System | ChatFlags.Radio)).U8(NoTeam);
        Net.Broadcast(_w.Span, PacketReliability.ReliableOrdered);
    }

    /// <summary>Client: who is on another planetoid, by id (from the roster: the Tab list says where they are).</summary>
    private readonly Dictionary<byte, string> _awayOf = new();

    private void WriteRosterAway(PacketWriter w)
    {
        var at = w.Length;
        byte n = 0;
        w.U8(0);
        foreach (var p in Players.Values)
            if (p.AwayOn != null && n < 255 && (Host == null || p.Id != LocalId))
            {
                w.U8(p.Id).Str(p.AwayOn);
                n++;
            }
        w.PatchU8(at, n);
    }

    private void ReadRosterAway(ref PacketReader r)
    {
        if (!r.Has(1))
            return;
        _awayOf.Clear();
        int n = r.U8();
        for (var i = 0; i < n && r.Has(2); i++)
        {
            var id = r.U8();
            var map = r.Str();
            _awayOf[id] = Maps.Find(map)?.Name ?? Settings.Clean(map); // (a map this build has: its own name string, kept)
        }
        foreach (var p in Players.Values)
            p.AwayOn = _awayOf.GetValueOrDefault(p.Id);
    }
}
