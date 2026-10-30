using System.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>The building pieces (Constructs/Parts/Building, tools/constructs/building_parts.py).</summary>
    private static List<ConstructPartDef> BuildingParts() =>
        ConstructParts.All.Where(d => d.Folder.StartsWith("Building", StringComparison.Ordinal)).ToList();

    private const float BuildingY = 300f; // (the headless checks build up here, clear of whatever map is loaded; the walker's ground is at this height)

    private static readonly string[] BuildingBlueprints = ["Two-Storey House", "Bunker", "Watchtower", "Small Outpost"];

    /// <summary>
    /// "Building parts" (docs/sandbox/CONSTRUCTS.md, "Building pieces"): every file in Constructs/Parts/Building loads in
    /// the inventory's Building group, keeps the 0.25 m cell rules (so it lines up with the beams and panels), is cheap
    /// (few boxes) and its materials order by mass, health and armour; walls lose chunks through their damage stages
    /// (same sockets, links kept, then breaking off); pieces snap edge to edge on the module (four corners close a room
    /// on a pad, a slab welds onto the walls' tops a storey up, the next storey's corners on the slab); openings let a
    /// player through (an opened door, a garage, a breach crouching) where walls, windows and slits stop them; stairs,
    /// the half-turn stair, the ship ladder and a ramp are climbed by a player (BlockPlayer), and a straight stair by a
    /// bot through the server's own movement on Construct Yard; the four example blueprints build anchored and stand.
    /// Walking about the house allocates nothing.
    /// </summary>
    private static void CheckBuildingParts()
    {
        var parts = BuildingParts();
        var refused = ConstructParts.Refused.Where(r => !r.Contains("missing")).ToList();
        var stages = parts.Count(d => d.Stage);
        Line($"building parts: {parts.Count} in Constructs/Parts/Building ({stages} damage stages), {ConstructParts.All.Count} parts in all; refused: {(refused.Count == 0 ? "none" : string.Join("; ", refused))}");
        if (refused.Count > 0)
            Fail($"construct part files refused: {string.Join("; ", refused)}");
        if (parts.Count < 150)
            Fail($"only {parts.Count} building parts loaded (178 ship)");
        string[] sections = ["Walls", "Wall openings", "Corners and ends", "Floors", "Foundations", "Roofs", "Stairs and access", "Doors and gates", "Defences", "Watchtower", "Columns and beams", "Damaged"];
        var bad = new List<string>();
        var cat = SandboxCatalogue.Make();
        foreach (var d in parts)
        {
            if (d.Group != "Building" || Array.IndexOf(sections, d.Section) < 0)
                bad.Add($"{d.Name}: group \"{d.Group}\" section \"{d.Section}\"");
            if (d.Stage != (d.Section == "Damaged"))
                bad.Add($"{d.Name}: a stage outside Damaged, or a Damaged part not marked a stage");
            if (cat.PartGroup[d.Id] != "Building")
                bad.Add($"{d.Name}: in the inventory's {cat.PartGroup[d.Id]} group");
            // Cheap: few boxes (walls six solid at most), every one drawn from boxes the renderer batches.
            if (d.SolidBoxes > 16 || d.Section is "Walls" or "Corners and ends" && d.SolidBoxes > 6)
                bad.Add($"{d.Name}: {d.SolidBoxes} solid boxes");
            if (d.Role is PartRole.Door)
                continue; // (leaves hang on a Hinge: not on the lattice)
            foreach (var s in d.Sockets)
            {
                var axis = s.Dir >> 1;
                for (var a = 0; a < 3; a++)
                {
                    var v = s.At[a];
                    var ok = a == axis ? HalfCm(v) : OnCell(v) || d.Role == PartRole.Panel && a == 2 && OnCm(v)
                        || d.Section is "Stairs and access" or "Defences" or "Watchtower" && d.Name.Contains("Railing") | d.Name.Contains("Fence") | d.Name.Contains("Brace") && OnCm(v);
                    if (!ok)
                    {
                        bad.Add($"{d.Name} socket {s.Name} at {s.At}");
                        break;
                    }
                }
            }
        }
        foreach (var name in new[] { "Concrete Stair Floor 4x4 m", "Concrete Straight Stair 3 m", "Concrete Half-Turn Stair 3 m", "Ship Ladder 3 m", "Concrete Ramp 2x4 m", "Concrete Foundation 4x4 m" })
            if (ConstructParts.Find(name) is not { Walk: not WalkKind.None })
                bad.Add($"{name}: not marked walkable");
        var offeredStages = cat.Parts.Sections.SelectMany(s => s.Items).Count(i => ConstructParts.All[i].Stage);
        Line($"building parts: groups, sections, cell rules, box counts and walk marks {(bad.Count == 0 ? "kept by every part" : "broken: " + string.Join("; ", bad.Take(8)))}; damage stages offered in the inventory: {offeredStages}");
        if (bad.Count > 0 || offeredStages > 0)
            Fail($"{bad.Count} building part rule(s) broken: {string.Join("; ", bad.Take(8))}{(offeredStages > 0 ? $"; {offeredStages} damage stages are offered" : "")}");

        // Materials: wood < corrugated < steel < concrete < reinforced in armour; concrete heavy, wood burns.
        string[] ladder = ["Wood Wall 4 m", "Corrugated Wall 4 m", "Concrete Wall 4 m", "Steel Wall 4 m", "Reinforced Wall 4 m"];
        var walls = ladder.Select(ConstructParts.Need).ToList();
        var byArmour = walls.OrderBy(w => w.Armour).Select(w => w.Name).ToArray();
        var matOk = byArmour.SequenceEqual(["Wood Wall 4 m", "Corrugated Wall 4 m", "Concrete Wall 4 m", "Steel Wall 4 m", "Reinforced Wall 4 m"])
                    && walls[2].Mass > walls[3].Mass && walls[3].Mass > walls[0].Mass && walls[4].Health > walls[2].Health && walls[2].Health > walls[0].Health
                    && walls[0].Flammable && !walls[2].Flammable;
        Line($"building materials: {string.Join(", ", walls.Select(w => $"{w.Name} {w.Mass:0} kg {w.Health:0} hp armour {w.Armour:0}{(w.Flammable ? " (burns)" : "")}"))}");
        if (!matOk)
            Fail("building materials: wood, corrugated, concrete, steel and reinforced walls don't order by armour, mass and health (or wood doesn't burn)");

        BuildingBotStairs(); // (first: it loads Construct Yard, whose small map the physics below then steps against)
        var world = new ConstructWorld();
        BuildingSnap(world);
        world = new ConstructWorld();
        BuildingStages(world);
        world = new ConstructWorld();
        BuildingOpenings(world);
        world = new ConstructWorld();
        BuildingStairs(world);
        BuildingBlueprintsStand();
    }

    /// <summary>A walker for the checks: walks along <paramref name="dir"/> (per position) at walking speed, falls under gravity to the ground at BuildingY or a construct's top, kept out of constructs by BlockPlayer.</summary>
    private static MoveState BuildingWalk(ConstructWorld w, Vector3 start, Func<Vector3, Vector3> dir, int ticks, float height, out float highest, float speed = 4f)
    {
        var s = new MoveState { Position = start, Grounded = true };
        highest = start.Y;
        for (var t = 0; t < ticks; t++)
        {
            var d = dir(s.Position);
            s.Velocity.X = d.X * speed;
            s.Velocity.Z = d.Z * speed;
            s.Position.X += s.Velocity.X * World.TickDt;
            s.Position.Z += s.Velocity.Z * World.TickDt;
            s.Velocity.Y -= 20f * World.TickDt;
            s.Position.Y += s.Velocity.Y * World.TickDt;
            if (s.Position.Y <= BuildingY)
            {
                s.Position.Y = BuildingY;
                s.Velocity.Y = 0;
            }
            s.Grounded = s.Position.Y <= BuildingY;
            w.BlockPlayer(ref s, height, server: true);
            highest = MathF.Max(highest, s.Position.Y);
        }
        return s;
    }

    private static Construct? BuildingBlueprint(ConstructWorld w, string name, Vector3 at, out string why)
    {
        why = "";
        if (ConstructBuild.FindBlueprint(name) is not { } bp)
        {
            why = "no such blueprint";
            return null;
        }
        return ConstructBuild.BuildOnGround(w, bp, at, 0, out why);
    }

    /// <summary>The module: four corners close a 4 m room on a pad, a slab on their tops welds a storey up, a straight wall fills between corners, the next storey's corners stand on the slab.</summary>
    private static void BuildingSnap(ConstructWorld w)
    {
        var pad = ConstructParts.Need("Concrete Foundation 4x4 m");
        var corner = ConstructParts.Need("Concrete Wall Corner");
        var slab = ConstructParts.Need("Concrete Floor 4x4 m");
        if (ConstructBuild.PlaceNew(w, pad, new Vector3(0, BuildingY, 0), 0) is not { } c)
        {
            Fail("building snap: couldn't set down a pad");
            return;
        }
        // Fit a part on the first free socket (of any part) that puts its bounds' min corner at a body point (cells), turned rot.
        int Fit(ConstructPartDef d, Vector3 minCell, byte rot, out string why)
        {
            why = "no socket puts it there";
            var want = minCell * 0.25f - new Vector3(0.125f);
            for (var i = 0; i < c.PartCount; i++)
            for (var s = 0; s < c.SocketCount(i); s++)
            {
                if (c.SocketUsed(i, s))
                    continue;
                for (var v = 0; v < d.Sockets.Length * 4; v++)
                {
                    if (ConstructBuild.Place(c, i, s, d, v / 4, v % 4, out var g) is { } no)
                    {
                        if (no.Contains("in the way"))
                            why = no;
                        continue;
                    }
                    if (g.Rot != rot)
                        continue;
                    var mn = g.Pos + Vector3.Min(Rot24.Apply(rot, d.BoundsMin), Rot24.Apply(rot, d.BoundsMax));
                    if (Vector3.Distance(mn, want) > 0.006f)
                        continue;
                    return ConstructBuild.Attach(w, c, d, g);
                }
            }
            return -1;
        }
        byte Yaw(int quarter) => (byte)Enumerable.Range(0, Rot24.Count).First(r => Rot24.ApplyDir((byte)r, 2) == 2 && Rot24.ApplyDir((byte)r, 0) == (quarter switch { 0 => 0, 1 => 5, 2 => 1, _ => 4 }));
        var problems = new List<string>();
        // The pad is cells 0..15 (x, z) and 0..1 (y): walls stand on row 2. Corner k at its corner, legs along the sides.
        (Vector3 Min, int Q)[] corners = [(new(0, 2, 0), 0), (new(8, 2, 0), 3), (new(8, 2, 8), 2), (new(0, 2, 8), 1)];
        var placed = 0;
        foreach (var (mn, q) in corners)
            if (Fit(corner, mn, Yaw(q), out var why) >= 0)
                placed++;
            else
                problems.Add($"corner at {mn} turned {q}: {why}");
        // Their footprint: exactly the room's perimeter (60 cells), nothing over the inside.
        var cells = new HashSet<(int, int)>();
        var overlap = 0;
        for (var i = 1; i < c.PartCount; i++)
            for (var b = 0; b < c.DefOf(i).SolidBoxes; b++)
            {
                c.BoxOf(i, b, out var bc, out var bh);
                for (var x = (int)MathF.Round((bc.X - bh.X + 0.125f) / 0.25f); x < (int)MathF.Round((bc.X + bh.X + 0.125f) / 0.25f); x++)
                for (var z = (int)MathF.Round((bc.Z - bh.Z + 0.125f) / 0.25f); z < (int)MathF.Round((bc.Z + bh.Z + 0.125f) / 0.25f); z++)
                    if (!cells.Add((x, z)))
                        overlap++;
            }
        var perimeter = cells.All(p => p.Item1 is 0 or 15 || p.Item2 is 0 or 15) && cells.Count == 60;
        var linksBefore = c.LinkCount;
        var slabAt = Fit(slab, new Vector3(0, 2 + 11, 0), 0, out var whySlab);
        var slabLinks = c.LinkCount - linksBefore;
        if (slabAt < 0)
            problems.Add($"slab: {whySlab}");
        c.PartBounds(Math.Max(0, slabAt), out var smn, out var smx);
        c.PartBounds(0, out var pmn, out var pmx);
        var storey = smx.Y - pmx.Y;
        var up = Fit(corner, new Vector3(0, 2 + 12, 0), Yaw(0), out var whyUp);
        if (up < 0)
            problems.Add($"upper corner: {whyUp}");
        Line($"building snap: {placed} of 4 corners on the pad, their footprint {(perimeter ? "the room's 60 perimeter cells" : $"{cells.Count} cells")} ({overlap} overlapping); the slab on their tops welded at {slabLinks} sockets, {storey:0.###} m floor to floor; the next storey's corner {(up >= 0 ? "on the slab" : "refused")}");
        if (placed < 4 || !perimeter || overlap > 0 || slabLinks < 4 || MathF.Abs(storey - 3f) > 0.001f || up < 0)
            Fail($"building snap: the module doesn't close a room and stack a storey: {string.Join("; ", problems)}");

        // A 4 m wall fills the module between two corners on an 8 m side (two pads), and walls weld end to end.
        var w2 = new ConstructWorld();
        var bp = ConstructBuild.FindBlueprint("Two-Storey House");
        var house = bp == null ? null : ConstructBuild.Build(w2, bp, Vector3.Zero, Quaternion.Identity, out _);
        var ends = 0;
        if (house != null)
            for (var i = 0; i < house.PartCount; i++)
                if (house.DefOf(i).Name == "Concrete Window Wall 4 m")
                    for (var k = 0; k < house.LinkCount; k++)
                        if ((house.Links[k].A == i || house.Links[k].B == i) && house.DefOf(house.Links[k].A == i ? house.Links[k].B : house.Links[k].A).Name.EndsWith("Corner"))
                            ends++;
        Line($"building snap: in the house the 4 m window walls weld to their corners at {ends} sockets");
        if (ends < 8)
            Fail("building snap: the house's 4 m walls don't weld to the corners at both ends");
    }

    /// <summary>Walls lose chunks: below 60% a wall turns into its Damaged stage, below 30% Breached, then breaks off; sockets and links stay.</summary>
    private static void BuildingStages(ConstructWorld w)
    {
        var pad = ConstructParts.Need("Concrete Foundation 4x4 m");
        var wall = ConstructParts.Need("Concrete Wall 4 m");
        if (ConstructBuild.PlaceNew(w, pad, new Vector3(0, BuildingY, 0), 0) is not { } c)
            return;
        var ok = false;
        for (var s = 0; s < pad.Sockets.Length && !ok; s++)
            for (var v = 0; v < wall.Sockets.Length * 4 && !ok; v++)
                if (ConstructBuild.Place(c, 0, s, wall, v / 4, v % 4, out var g) == null && Rot24.ApplyDir(g.Rot, 2) == 2)
                    ok = ConstructBuild.Attach(w, c, wall, g) >= 0;
        if (!ok)
        {
            Fail("building stages: couldn't fit a wall on a pad");
            return;
        }
        var links = c.LinkCount;
        var seen = new List<string>();
        var boxes = new List<int>();
        var hp = wall.Health;
        for (var hit = 0; hit < 40 && c.PartCount == 2; hit++)
        {
            w.DamagePart(c, 1, hp * 0.1f + wall.Armour, out _);
            if (c.Dirty)
                c.Rebuild();
            if (c.PartCount == 2 && (seen.Count == 0 || seen[^1] != c.DefOf(1).Name))
            {
                seen.Add(c.DefOf(1).Name);
                boxes.Add(c.DefOf(1).SolidBoxes);
            }
            if (c.PartCount == 2 && c.LinkCount != links)
                break;
        }
        var linkKept = seen.Count == 3;
        Line($"building stages: a concrete 4 m wall shot down went {string.Join(" -> ", seen.Select((n, i) => $"{n} ({boxes[i]} boxes)"))}, then {(c.PartCount == 1 ? "broke off" : "stayed")}; its weld kept through the stages: {linkKept}; chunks lost {w.ChunksLost}");
        if (seen.Count != 3 || !seen[1].EndsWith("Damaged") || !seen[2].EndsWith("Breached") || c.PartCount != 1 || w.ChunksLost != 2)
            Fail("building stages: a wall doesn't lose chunks (whole, damaged, breached) before it breaks off");
    }

    /// <summary>Openings: through an opened door, a garage and a breach (crouching) a player passes; walls, windows, slits and a shut door stop them.</summary>
    private static void BuildingOpenings(ConstructWorld w)
    {
        var results = new List<string>();
        var wrong = new List<string>();
        void Expect(string what, bool passed, bool want)
        {
            results.Add($"{what} {(passed ? "passed" : "stopped")}");
            if (passed != want)
                wrong.Add(what);
        }
        // The house (x 0..4 m, z 0..8 m from its first cell's corner): its front door is on the east side, z 0.125..1.625 m.
        if (BuildingBlueprint(w, "Two-Storey House", new Vector3(0, BuildingY, 0), out var why) is not { } house)
        {
            Fail($"building openings: the house doesn't build: {why}");
            return;
        }
        var o = house.Pos - new Vector3(0.125f); // (its first cell's min corner: the pads' bottom, 2 cm over the ground)
        bool Through(Vector3 from, Vector3 dir, float height, float pastX)
        {
            var s = BuildingWalk(w, from, _ => dir, 120, height, out _);
            return dir.X < 0 ? s.Position.X < pastX : s.Position.X > pastX;
        }
        var west = new Vector3(-1, 0, 0);
        Expect("the shut front door", Through(o + new Vector3(5f, 0, 0.875f), west, World.PlayerHeight, o.X + 3.5f), false);
        var j = -1;
        for (var k = 0; k < house.JointCount; k++)
            j = k;
        if (j >= 0)
        {
            house.Joints[j].Open = true;
            house.Wake();
            for (var t = 0; t < 120; t++)
                w.Step(World.Query, World.TickDt);
        }
        Expect("the opened front door", Through(o + new Vector3(5f, 0, 0.875f), west, World.PlayerHeight, o.X + 3.5f), true);
        Expect("the east window wall", Through(o + new Vector3(5f, 0, 3.0f), west, World.PlayerHeight, o.X + 3.5f), false);
        w.Remove(house);
        // A garage wall, a breached wall and a slit wall, each set down on its own.
        Construct? One(string name, float x) => ConstructBuild.PlaceNew(w, ConstructParts.Need(name), new Vector3(x, BuildingY, 0), 0);
        var garage = One("Concrete Garage Wall 4 m", 20);
        var breach = One("Concrete Wall 4 m Breached", 30);
        var slit = One("Concrete Slit Wall 2 m", 40);
        var plain = One("Concrete Wall 2 m", 50);
        var south = new Vector3(0, 0, 1);
        bool Across(Construct? c, float alongX, float height)
        {
            if (c == null)
                return false;
            var start = new Vector3(c.Pos.X - 0.125f + alongX, BuildingY, c.Pos.Z - 1.5f);
            var s = BuildingWalk(w, start, _ => south, 90, height, out _);
            return s.Position.Z > c.Pos.Z + 0.6f;
        }
        Expect("the garage opening", Across(garage, 2.0f, World.PlayerHeight), true);
        Expect("the garage's jamb", Across(garage, 0.2f, World.PlayerHeight), false);
        Expect("a breach, crouching", Across(breach, 1.5f, World.HeightOf(Stance.Crouch)), true);
        Expect("a breach, standing", Across(breach, 1.5f, World.PlayerHeight), false);
        Expect("a slit wall", Across(slit, 0.5f, World.PlayerHeight), false);
        Expect("a plain wall", Across(plain, 1.0f, World.PlayerHeight), false);
        Line($"building openings: {string.Join(", ", results)}");
        if (wrong.Count > 0)
            Fail($"building openings: wrong for {string.Join(", ", wrong)}");
    }

    /// <summary>Stairs climbed by a player: the house's straight stair a storey up, the half-turn stair, the ship ladder, a ramp.</summary>
    private static void BuildingStairs(ConstructWorld w)
    {
        var lines = new List<string>();
        var wrong = new List<string>();
        if (BuildingBlueprint(w, "Two-Storey House", new Vector3(0, BuildingY, 0), out var why) is not { } house)
        {
            Fail($"building stairs: the house doesn't build: {why}");
            return;
        }
        var o = house.Pos - new Vector3(0.125f);
        var ground = o.Y + 0.5f;
        // In at the stair's foot (z 4.4 m), up to -z, out onto the first floor.
        var s = BuildingWalk(w, o + new Vector3(0.75f, 0.5f, 4.4f), _ => new Vector3(0, 0, -1), 150, World.PlayerHeight, out var top);
        var upstairs = s.Position.Y > ground + 2.95f && s.Position.Z < o.Z + 1.0f;
        lines.Add($"the house's stair: from {ground:0.00} m to {s.Position.Y:0.00} m (the floor above at {ground + 3:0.00}), ending {s.Position.Z - o.Z:0.00} m from the south wall");
        if (!upstairs)
            wrong.Add("the house's stair");
        // Walking about upstairs and down allocates nothing.
        BuildingWalk(w, s.Position, _ => new Vector3(0, 0, 1), 20, World.PlayerHeight, out _);
        Func<Vector3, Vector3> about = p => p.Z > o.Z + 7f ? new Vector3(0, 0, -1) : new Vector3(0, 0, 1);
        var before = GC.GetAllocatedBytesForCurrentThread();
        BuildingWalk(w, s.Position, about, 240, World.PlayerHeight, out _);
        var alloc = GC.GetAllocatedBytesForCurrentThread() - before;
        lines.Add($"walking about the house {alloc} B");
        if (alloc > 0)
            wrong.Add("walking allocates");
        w.Remove(house);

        // Single pieces set down on the ground, climbed from the foot: they rise to +z.
        void Climb(string name, float x, Func<Vector3, Vector3, Vector3> dir, float want, int ticks = 180)
        {
            if (ConstructBuild.PlaceNew(w, ConstructParts.Need(name), new Vector3(x, BuildingY, 0), 0) is not { } c)
            {
                wrong.Add($"{name}: couldn't set down");
                return;
            }
            var origin = new Vector3(c.Pos.X - 0.125f, BuildingY, c.Pos.Z - 0.125f);
            var st = BuildingWalk(w, origin + new Vector3(0.5f, 0, -1.0f), p => dir(p - origin, origin), ticks, World.PlayerHeight, out var hi);
            lines.Add($"{name}: up to {hi - BuildingY:0.00} m");
            if (hi - BuildingY < want)
                wrong.Add(name);
        }
        Climb("Steel Straight Stair 3 m", 10, (_, _) => new Vector3(0, 0, 1), 2.95f);
        Climb("Ship Ladder 3 m", 20, (p, _) => new Vector3(0, 0, 1), 2.95f);
        // Up flight A (x 0..1 m), across the landing, back down flight B's lane (x 1..2 m) to -z.
        Climb("Concrete Half-Turn Stair 3 m", 30, (p, _) => p.Z < 1.9f && p.X < 1.0f ? new Vector3(0, 0, 1) : p.X < 1.5f ? new Vector3(1, 0, 0) : new Vector3(0, 0, -1), 2.95f, 240);
        Climb("Concrete Ramp 2x4 m", 40, (_, _) => new Vector3(0, 0, 1), 0.95f);
        Line($"building stairs: {string.Join("; ", lines)}");
        if (wrong.Count > 0)
            Fail($"building stairs: not climbed: {string.Join(", ", wrong)}");
    }

    /// <summary>The four example buildings build anchored, every part joined to the root, and stand still under physics.</summary>
    private static void BuildingBlueprintsStand()
    {
        var w = new ConstructWorld();
        var lines = new List<string>();
        var x = 0f;
        foreach (var name in BuildingBlueprints)
        {
            var c = BuildingBlueprint(w, name, new Vector3(x, BuildingY, 0), out var why);
            x += 20;
            if (c == null)
            {
                lines.Add($"{name}: refused ({why})");
                Fail($"building blueprints: {name} doesn't build: {why}");
                continue;
            }
            var bodies = w.Bodies.Count;
            var pos = c.Pos;
            var n = c.PartCount;
            for (var t = 0; t < 120; t++)
                w.Step(World.Query, World.TickDt);
            var stands = c.Anchored && w.Bodies.Count == bodies && c.PartCount == n && Vector3.Distance(c.Pos, pos) < 1e-4f;
            lines.Add($"{name}: {n} parts, {c.Mass / 1000:0.0} t, {c.LocalMax.Y - c.LocalMin.Y:0.00} m tall, anchored {c.Anchored}, {(stands ? "stands" : "MOVED or split")}");
            if (!stands)
                Fail($"building blueprints: {name} doesn't stand anchored");
        }
        Line($"building blueprints: {string.Join("; ", lines)}");
    }

    /// <summary>A bot walks up a straight stair on Construct Yard through the server's own movement (World.Simulate, then the constructs).</summary>
    private static void BuildingBotStairs()
    {
        if (Maps.Find(ConstructYardMap) is not { } yard || yard.Sandbox is not { } sb)
        {
            Fail($"building stairs: the {ConstructYardMap} map is missing");
            return;
        }
        yard.RestoreProps();
        World.SetMap(yard);
        var keep = sb.DebrisSeconds;
        sb.DebrisSeconds = 0;
        try
        {
            var now = 0.0;
            using var g = ModeGame(1, GameMode.Sandbox, yard.Id);
            Run(g, ref now, Game.CountdownSeconds + 1.0);
            var bot = g.Players.Values.FirstOrDefault(p => p.Bot != null && p.Alive);
            if (bot == null || OpenSpot(g.Constructs) is not { } spot)
            {
                Fail("building stairs: no live bot or no open ground on the yard");
                return;
            }
            var stair = ConstructBuild.PlaceNew(g.Constructs, ConstructParts.Need("Concrete Straight Stair 3 m"), spot, 0);
            if (stair == null)
            {
                Fail("building stairs: couldn't set a stair down on the yard");
                return;
            }
            var foot = stair.Pos + new Vector3(0.375f, 0, -1.0f);
            bot.Teleport(spot with { X = foot.X, Z = foot.Z });
            var y0 = bot.Position.Y;
            var hi = y0;
            uint seq = 1;
            for (var t = 0; t < 150; t++)
            {
                g.SelfTestApplyCmd(bot, new InputCmd { Seq = seq++, Buttons = Buttons.Forward, Yaw = MathF.PI, Weapon = bot.Weapon, SpeedPct = 100 });
                hi = MathF.Max(hi, bot.Position.Y);
            }
            Line($"building stairs: a bot through the server's movement on the yard climbed a concrete stair from {y0:0.00} to {hi:0.00} m");
            if (hi < y0 + 2.9f)
                Fail("building stairs: a bot doesn't climb a stair through the server's movement");
        }
        finally
        {
            sb.DebrisSeconds = keep;
        }
    }
}
