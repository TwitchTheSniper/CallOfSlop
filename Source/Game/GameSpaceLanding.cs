using System.Numerics;
using CallOfSlop.Space;

namespace CallOfSlop;

/// <summary>
/// Landing on another planetoid (docs/space/SPACE.md "Landing on another planetoid"; Karl, 2026-11-04: the group travels
/// together). The game holds one planetoid at a time, so a landing moves the whole game: a ship in space that comes within
/// the sky's edge of another body with a map (<c>"map"</c> in its system file) lands there when every player sits in it.
/// Until then it is held at the edge and its riders are told who they wait for. A ship that has jumped to another star
/// system lands on that system's worlds too: the bubble moves to that system, and every system's ships fly on
/// (<see cref="MakeHostSpace"/>). A world may take ships only up to a mass (<see cref="LandOverLimit"/>).
/// <para>
/// The landing goes through the world save (it needs a saved world): the planetoid left behind is written to the world's
/// <c>planets/&lt;map&gt;</c> folder without the ship and what it carries, the whole world (people, story, the ship) to its
/// own file, and the map changes. As the new map starts, the world loads from two files (<see cref="WorldLandApply"/>):
/// the people, their things and the story from the world's own file, the planetoid arrived at from its folder (if the
/// group has been there before), then the ship and what it carries, with fresh ids and stores. The space clock and every
/// ship in flight carry on (the same system's flight), so the ship comes down through the new sky as it would at home,
/// and its riders sit in it again. Ships left in space round the planetoid left behind keep flying; their constructs
/// wait in its folder and fly on from their ships when the group comes back.
/// </para>
/// </summary>
public sealed partial class Game
{
    /// <summary>A landing on its way: the ship's bodies by their ids in the world's file, their stores, and the maps.</summary>
    private sealed class LandingPlan
    {
        public readonly HashSet<ushort> Bodies = [];
        public readonly HashSet<ushort> Stores = [];
        public string From = "", To = "";
        public bool All; // every body and store in the file is the ship's (a planetoid's game on the host opening: GameTravelPlace.cs)
        public bool Travel; // the file's space section starts this game's space clock and ships (GameTravelPlace.cs)
        public bool KeepPlaces; // everyone keeps their place, here or not (the riders sit in their seats again as they join: GameTravelPlace.cs)
    }

    private LandingPlan? _landing;
    private (Construct Ship, Map To)? _landNext;
    private double _landSaidAt = -1;

    /// <summary>
    /// The bodies a ship could land on, in every star system (galaxy index, body index, its map): another body of the
    /// bubble's own system, or any world of a system a ship has jumped to. Worked out for one bubble.
    /// </summary>
    private (int Galaxy, int Body, Map Map, double Edge)[] _landable = [];
    private SpaceSession? _landableFor;

    /// <summary>The folder a planetoid left behind is kept in, inside a world's folder.</summary>
    private static string StashFolder(WorldSession ws, string map) => Path.Combine(ws.Folder, "planets", WorldStore.FolderFor(map));

    /// <summary>Where a map's sky ends round its planetoid (m from the centre): as <see cref="SpaceEdge"/> works it out there.</summary>
    private static double LandEdge(Map m) => Math.Max(Math.Max(m.HalfX, m.HalfZ), m.Planetoid!.Radius + 500);

    /// <summary>
    /// Host, after the ships step: a ship in space within another planetoid's sky lands, or is held at its edge. A ship
    /// that has jumped to another star system lands on that system's worlds the same way (the whole group: free travel
    /// stays inside the bubble's own system).
    /// </summary>
    internal void SpaceLandingStep(SpaceSession s, SpaceFlight f)
    {
        if (_landing != null || _landNext != null || _travelNext != null || Host != null || !SandboxOn || SpaceLaunched == 0)
            return;
        if (!ReferenceEquals(_landableFor, s))
            LandableFor(s);
        if (_landable.Length == 0)
            return;
        foreach (var c in Constructs.Bodies)
        {
            if (c.SpaceShip == 0)
                continue;
            var g = SystemOfConstruct(s, c);
            if (g != s.GalaxyIndex && s.FlightOf(g) == null)
                continue;
            var sf = g == s.GalaxyIndex ? f : s.FlightOf(g)!;
            var i = sf.Ships.IndexOfId(c.SpaceShip);
            if (i < 0)
                continue;
            var pos = sf.Ships.PositionOf(i);
            var t = sf.Ships.Time;
            foreach (var (lg, b, m, edge) in _landable)
            {
                if (lg != g)
                    continue;
                sf.System.StateOf(b, t, out var bp, out var bv);
                var r = pos - bp;
                if (r.LengthSquared >= edge * edge)
                    continue;
                if (g == s.GalaxyIndex && TravelToPlace(c, m, b))
                    return; // (free travel: the planetoid runs beside this game and the riders join it, GameTravel.cs)
                var why = PlaceOf(m.Name) != null ? $"others are on {m.Name}: a ship without the host aboard can land there" : LandProblem(c);
                if (why == null)
                {
                    _landNext = (c, m);
                    return;
                }
                // Held at the edge until it can land: no closer, and no speed inwards.
                var n = r.Normalized();
                var v = sf.Ships.VelocityOf(i) - bv;
                var vr = Vec3d.Dot(v, n);
                if (vr < 0)
                    v -= n * vr;
                sf.Ships.SetState(i, b, n * edge, v);
                if (SandboxNow > _landSaidAt + 3)
                {
                    _landSaidAt = SandboxNow;
                    foreach (var p in Players.Values)
                        if (SeatedIn(p, c))
                            SandboxHands.Notices.Add((p.Id, $"Landing on {m.Name}: {why}"));
                }
                break;
            }
        }
    }

    private void LandableFor(SpaceSession s)
    {
        _landableFor = s;
        var list = new List<(int, int, Map, double)>();
        for (var g = 0; g < Galaxy.Count; g++)
        {
            var sys = Galaxy.System(g);
            for (var b = 1; b < sys.Count; b++)
                if ((g != s.GalaxyIndex || b != s.Bubble.Body) && sys.Bodies[b].Map is { } name && Maps.Find(name) is { Planetoid: not null } m && m != World.Map)
                    list.Add((g, b, m, LandEdge(m)));
        }
        if (s.GalaxyIndex < 0) // (a system outside the galaxy: its own bodies only, as before)
            for (var b = 1; b < s.System.Count; b++)
                if (b != s.Bubble.Body && s.System.Bodies[b].Map is { } name && Maps.Find(name) is { Planetoid: not null } m && m != World.Map)
                    list.Add((-1, b, m, LandEdge(m)));
        _landable = list.ToArray();
    }

    /// <summary>
    /// Landing limits (docs/planets/WORLDS.md; Karl, 2026-11-10: some worlds won't take a capital ship, so a shuttle goes
    /// down). A world can set the heaviest ship that may come down through its sky (<see cref="Planetoid.LandingTonnes"/>,
    /// the ship with what it carries). The group still arrives: the bubble moves to the world, and the heavy ship is held
    /// at the sky's edge as a ship in space, where a craft it carries can be let go and flown down. True: this ship is
    /// over the limit here (its mass in <paramref name="tonnes"/>). No allocation.
    /// </summary>
    private bool LandOverLimit(Construct ship, out double tonnes)
    {
        tonnes = 0;
        if (World.Map.Planetoid is not { LandingTonnes: > 0 } p)
            return false;
        foreach (var c in Constructs.Bodies)
            if (Aboard(ship, c))
                tonnes += c.Mass;
        tonnes /= 1000;
        return tonnes > p.LandingTonnes;
    }

    /// <summary>Host: a ship over this world's landing limit stays at the sky's edge (no closer, no speed inwards), and its riders hear why.</summary>
    private void LandHoldOut(SpaceFlight f, int i, Construct c, double edge, double tonnes)
    {
        var body = Space!.Bubble.Body;
        f.System.StateOf(body, f.Ships.Time, out var bp, out var bv);
        var r = f.Ships.PositionOf(i) - bp;
        var n = r.LengthSquared > 1 ? r.Normalized() : Vec3d.UnitZ;
        var v = f.Ships.VelocityOf(i) - bv;
        var vr = Vec3d.Dot(v, n);
        if (vr < 0)
            v -= n * vr;
        f.Ships.SetState(i, body, n * edge, v);
        if (SandboxNow > _landSaidAt + 4)
            LandLimitSay(c, tonnes);
    }

    private void LandLimitSay(Construct c, double tonnes)
    {
        _landSaidAt = SandboxNow;
        var p = World.Map.Planetoid!;
        var line = FormattableString.Invariant($"{World.Map.Name} takes ships up to {p.LandingTonnes:0} t and this one is {tonnes:0} t")
                   + (p.LandingWhy.Length > 0 ? $" ({p.LandingWhy})" : "") + ": leave it in orbit and take a shuttle down";
        foreach (var pl in Players.Values)
            if (SeatedIn(pl, c))
                SandboxHands.Notices.Add((pl.Id, line));
    }

    private bool SeatedIn(Player p, Construct c)
    {
        ref var h = ref SandboxHands.Hand(p.Id);
        return p.Alive && h.Seated && h.SeatBody == c.Id;
    }

    /// <summary>Why this ship can't land on another planetoid now (null: it can).</summary>
    private string? LandProblem(Construct c, bool everyone = true)
    {
        if (WorldSession is not { Applied: true })
            return "it needs a saved world (the lobby's New world)";
        string? waiting = null;
        foreach (var p in Players.Values)
            if (p.Bot == null && !SeatedIn(p, c) && p.AwayOn == null)
                waiting = waiting == null ? p.Name : waiting + ", " + p.Name;
        if (waiting != null && everyone)
            return $"everyone travels together: waiting for {waiting} to sit aboard";
        var id = c.Id;
        var linked = false;
        for (var k = 0; k < Constructs.CouplingCount; k++)
            linked |= Constructs.Couplings[k].BodyA == id || Constructs.Couplings[k].BodyB == id;
        for (var k = 0; k < Constructs.XWireCount; k++)
            linked |= Constructs.XWires[k].BodyFrom == id || Constructs.XWires[k].BodyTo == id;
        for (var k = 0; k < Constructs.HoseCount; k++)
            linked |= Constructs.Hoses[k].BodyOut == id || Constructs.Hoses[k].BodyIn == id;
        for (var k = 0; k < Constructs.WorldLinkCount; k++)
            linked |= Constructs.WorldLinks[k].BodyFrom == id || Constructs.WorldLinks[k].BodyTo == id;
        for (var k = 0; k < Constructs.ConnectorCount; k++)
            linked |= Constructs.Connectors[k].A.Body == id || Constructs.Connectors[k].B.Body == id;
        return linked ? "unhook it from what it is coupled, wired, piped or tied to first" : null;
    }

    /// <summary>Is this body the ship or something it carries (cargo held in a cargo volume, however deep)?</summary>
    private bool Aboard(Construct ship, Construct c)
    {
        for (var guard = 0; guard < 16 && c != null; guard++)
        {
            if (c == ship)
                return true;
            if (c.CarriedBy == 0 || Constructs.Get(c.CarriedBy) is not { } up)
                return false;
            c = up;
        }
        return false;
    }

    /// <summary>Host, after the space tick: a landing that is due goes (the map changes; the world loads on the next tick).</summary>
    internal void ServerTickLanding(double now)
    {
        if (_landNext is not { } next)
            return;
        _landNext = null;
        Land(next.Ship, next.To, now); // (its own method: a closure over the ship would allocate here, on every tick)
    }

    private void Land(Construct ship, Map to, double now)
    {
        if (WorldSession is not { } ws || Constructs.Get(ship.Id) != ship || ship.SpaceShip == 0 || LandProblem(ship) != null)
            return;
        FirstForRiders(ship, Milestone.Landing, to.Name); // (before the save: the records carry it to the new world; GameMilestones.cs)
        WorldSaveNow(wait: true); // (the whole world, the ship and everyone's things with it, in the world's own file)
        var from = World.Map.Name;
        var why = WorldCopy(ws, StashFolder(ws, from), c => !Aboard(ship, c)); // (the planetoid left behind, without the ship)
        if (why != null)
        {
            foreach (var p in Players.Values)
                if (SeatedIn(p, ship))
                    SandboxHands.Notices.Add((p.Id, $"Couldn't land on {to.Name}: {from} wasn't saved ({why})"));
            return;
        }
        var plan = new LandingPlan { From = from, To = to.Name };
        foreach (var c in Constructs.Bodies)
            if (Aboard(ship, c))
            {
                plan.Bodies.Add(c.Id);
                for (var k = 0; k < c.PartCount; k++)
                    if (c.Parts[k].Store != 0)
                        plan.Stores.Add(c.Parts[k].Store);
            }
        _landing = plan;
        ws.HasSave = true;
        ws.Meta.MapName = to.Name;
        ws.PlayedBefore = ws.Meta.PlayedSeconds;
        ws.PlayStarted = -1;
        ws.Live = false;
        Console.WriteLine($"[space] construct {ship.Id} (ship {ship.SpaceShip}) lands on {to.Name}: everyone aboard, {from} kept in {StashFolder(ws, from)}");
        SetMapHere(to);
        foreach (var p in Players.Values)
            p.Snaps.Clear();
        FitPlayersToMap();
        Log($"Landing on {to.Name}", true);
        StartNewMatch(now, travel: true); // (the world loads as the new map starts: WorldLandApply)
    }

    /// <summary>
    /// Host: the world as the group lands: people, seats and the story from the world's own file; the planetoid arrived at
    /// from its folder; then the ship and what it carries from the world's file, with fresh ids and stores.
    /// </summary>
    private void WorldLandApply(WorldSession ws, LandingPlan plan)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        byte[] main;
        byte[]? there = null;
        try
        {
            main = WorldStore.Read(ws.Folder, out var meta);
            ws.PlayedBefore = meta.PlayedSeconds;
        }
        catch (WorldFileException ex)
        {
            Log($"World {ws.Meta.Name} can't be read: {ex.Message}", true);
            WorldDetach();
            return;
        }
        var thereFolder = StashFolder(ws, plan.To);
        if (File.Exists(WorldStore.PathOf(thereFolder)))
            try
            {
                there = WorldStore.Read(thereFolder, out _);
            }
            catch (WorldFileException ex)
            {
                Console.WriteLine($"[worlds] {ws.Meta.Name}: {plan.To}'s builds can't be read ({ex.Message}): it starts empty, the file stays");
            }
        ws.Players.Clear();
        Constructs.Clear();
        Constructs.PowerRequired = World.Map.Sandbox?.PowerRequired ?? false;
        Constructs.Resources = World.Map.Resources;
        int kept = 0, lost = 0;
        ushort nextTag = 1, thereTag = 1;
        var skipped = 0;

        // 1. People, where they sat, the story.
        var names = new WorldNamesRead();
        WorldSections(ws, main, (tag, r) =>
        {
            if (tag is WorldSaveWriter.Names or WorldSaveWriter.Misc or WorldSaveWriter.Players or WorldSaveWriter.SpaceSeats or WorldSaveWriter.Story
                || plan.Travel && tag == WorldSaveWriter.Space)
                WorldReadSection(tag, r, ws, names, ref kept, ref lost, ref nextTag);
        }, ref lost);
        skipped += names.Skipped;
        // 2. The planetoid arrived at, as the group left it.
        if (there != null)
        {
            var thereNames = new WorldNamesRead();
            WorldSections(ws, there, (tag, r) =>
            {
                if (tag is WorldSaveWriter.Names or WorldSaveWriter.Misc or WorldSaveWriter.Stores or WorldSaveWriter.Bodies or WorldSaveWriter.Links
                    or WorldSaveWriter.Connectors or WorldSaveWriter.NoCollide or WorldSaveWriter.Ground or WorldSaveWriter.Resources)
                    WorldReadSection(tag, r, ws, thereNames, ref kept, ref lost, ref thereTag);
            }, ref lost);
            skipped += thereNames.Skipped;
        }
        // 3. The ship and what it carries, into free ids and stores.
        var idAs = new Dictionary<ushort, ushort>();
        skipped += WorldReadShip(ws, main, plan.All ? _ => true : plan.Bodies.Contains, plan.All ? _ => true : plan.Stores.Contains, idAs, ref kept, ref lost);
        foreach (var (_, now) in idAs)
            if (Constructs.Get(now) is { CarriedBy: not 0 } cargo)
                cargo.CarriedBy = idAs.TryGetValue(cargo.CarriedBy, out var up) ? up : (ushort)0;
        // Ships: the ship flies on from its own; a construct of the planetoid arrived at that was in space flies on from
        // its ship if that still flies (else it is a body again where it was kept).
        foreach (var c in Constructs.Bodies)
        {
            if (c.SpaceShip == 0 || plan.Travel)
                continue; // (a traveller's game: the space section read above makes the bubble again, ships and all, WorldSpaceShips)
            c.SpaceAway = 0;
            var found = Space?.Flight?.Ships.IndexOfId(c.SpaceShip) >= 0;
            if (!found && Space != null)
                for (var g = 0; g < Space.Away.Length && !found; g++)
                    if (Space.Away[g]?.Ships.IndexOfId(c.SpaceShip) >= 0)
                    {
                        found = true;
                        SpacePark(c, g);
                    }
            if (!found)
                c.SpaceShip = 0;
        }
        // People: the travellers sit in the ship again; anyone away from the game comes back at a spawn.
        var here = new HashSet<WorldPlayer>();
        foreach (var p in Players.Values)
            if (WorldKnown(p) && ws.Find(WorldKeyOf(p), TrustedSteamId(p)) is { } rec)
                here.Add(rec);
        foreach (var rec in ws.Players)
        {
            rec.RideBody = 0;
            rec.SpaceSeatBody = rec.SpaceSeatBody != 0 && idAs.TryGetValue(rec.SpaceSeatBody, out var seat) ? seat : (ushort)0;
            if (!here.Contains(rec) && !plan.KeepPlaces)
                rec.HasPlace = false;
        }
        Constructs.AfterLoad(Math.Max(nextTag, thereTag));
        if (skipped > 0)
            Constructs.RelinkAfterSkips();
        Constructs.StructureDirty.Clear();
        Constructs.Removed.Clear();
        ws.Meta.MapName = plan.To;
        foreach (var p in Players.Values)
            if (WorldKnown(p))
                WorldArrive(ws, p);
        foreach (var (guid, _) in _clients.Values)
            if (guid.G != 0)
            {
                SendTerrainDigState(guid);
                SendResourceState(guid);
                SendSandboxState(guid);
            }
        ws.LastBody = $"landed in {clock.Elapsed.TotalMilliseconds:0} ms";
        Console.WriteLine($"[worlds] {ws.Meta.Name}: landed on {plan.To} from {plan.From}: {kept} constructs ({idAs.Count} aboard, {(there != null ? "builds from before" : "a first visit")}), {ws.Players.Count} people{(lost > 0 ? $", {lost} left out" : "")}");
        Log($"Landed on {plan.To}", true);
        ws.Live = true;
        if (!plan.Travel)
            WorldSaveNow(); // (the world's own file is the planetoid arrived at now; a traveller's game isn't in its seat yet, and the host keeps the world)
    }

    /// <summary>
    /// The bodies of a world file that <paramref name="takeBody"/> picks (and the stores <paramref name="takeStore"/>
    /// picks) into this world, with free ids and store handles: a ship landing (step 3 above), or one coming back from a
    /// planetoid kept on someone's own game (GameTravel.cs). <paramref name="idAs"/> gets file id to new id; returns the
    /// parts left out because this build doesn't have them.
    /// </summary>
    private int WorldReadShip(WorldSession ws, byte[] main, Func<ushort, bool> takeBody, Func<ushort, bool> takeStore,
        Dictionary<ushort, ushort> idAs, ref int kept, ref int lost)
    {
        var shipNames = new WorldNamesRead();
        var storeAs = new Dictionary<ushort, ushort>();
        var scratch = new CStore();
        var keptHere = 0;
        var lostHere = 0;
        WorldSections(ws, main, (tag, r) =>
        {
            switch (tag)
            {
                case WorldSaveWriter.Names:
                    shipNames.Read(r);
                    break;
                case WorldSaveWriter.Stores:
                {
                    var n = r.Count(8);
                    for (var k = 0; k < n; k++)
                    {
                        var handle = r.U16();
                        ushort free = 0;
                        if (takeStore(handle))
                            for (ushort h = 1; h <= ConstructWorld.MaxStores && free == 0; h++)
                                if (!Constructs.Stores[h].Live)
                                    free = h;
                        ConstructWorld.ReadStore(r, free != 0 ? Constructs.Stores[free] : scratch, shipNames);
                        if (free == 0)
                            continue;
                        Constructs.Stores[free].Live = true;
                        Constructs.StoreDirty.Add(free);
                        storeAs[handle] = free;
                    }
                    break;
                }
                case WorldSaveWriter.Bodies:
                {
                    var n = r.Count(32);
                    for (var k = 0; k < n; k++)
                    {
                        var len = r.I32();
                        var next = r.Pos + len;
                        var at = r.Pos;
                        var id = r.U16();
                        r.Pos = at;
                        if (takeBody(id))
                            try
                            {
                                if (Constructs.ReadBody(r, shipNames, rec => WorldOwnerByte(ws, rec), next, freshId: true,
                                        storeAs: h => storeAs.TryGetValue(h, out var x) ? x : (ushort)0) is { } c)
                                {
                                    idAs[id] = c.Id;
                                    keptHere++;
                                }
                            }
                            catch (Exception ex) when (ex is WorldFileException or IndexOutOfRangeException or ArgumentException or InvalidOperationException)
                            {
                                lostHere++;
                                Console.WriteLine($"[worlds] {ws.Meta.Name}: the ship's construct {id} skipped: {ex.Message}");
                            }
                        r.Pos = next;
                    }
                    break;
                }
            }
        }, ref lost);
        kept += keptHere;
        lost += lostHere;
        return shipNames.Skipped;
    }

    /// <summary>Every tagged section of a world file, each to <paramref name="read"/> (a bad section is counted and skipped).</summary>
    private static void WorldSections(WorldSession ws, byte[] body, Action<uint, SaveReader> read, ref int lost)
    {
        var r = new SaveReader(body) { Shift = WorldShift(body) };
        while (r.Has(8))
        {
            var tag = r.U32();
            var length = r.I32();
            var end = r.Pos + length;
            if (length < 0 || !r.Has(length))
                throw new WorldFileException("a section that doesn't fit");
            var whole = r.End;
            r.End = end;
            try
            {
                read(tag, r);
            }
            catch (WorldFileException ex)
            {
                Console.WriteLine($"[worlds] {ws.Meta.Name}: section {tag:x8} left out: {ex.Message}");
                lost++;
            }
            r.End = whole;
            r.Pos = end;
        }
    }
}
