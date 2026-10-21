using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The Cluttered pack's harbour map, "Dockside": a crescent-shaped polygon round a bay (quays, two piers and a mole on
/// the bay side, a curved land side), mirrored about x = 0 (Blue in the west horn, Red in the east). Checks the shape
/// and its bay, the size and density, that nothing is outside the outline except the floaters on the water, that the
/// sea really is a drop below the quays, the placement rules (dumpsters by walls with trash round them, desk and kitchen
/// things under a roof, rooftop plant on roofs, boats on water), the hand-placed objectives, mirrored solid props, equal
/// walks and a 12-player bot match.
/// </summary>
public static partial class SelfTest
{
    private const string ClutteredDocksideMap = "Dockside";

    static partial void CheckClutteredDockside()
    {
        var m = Maps.Find(ClutteredDocksideMap);
        if (m == null)
        {
            Fail($"no \"{ClutteredDocksideMap}\" map (Maps/Cluttered/05-Dockside.json)");
            return;
        }
        if (Maps.FindPack(ClutteredPack)?.Maps.Contains(m) != true)
            Fail($"{m.Name} isn't in the {ClutteredPack} pack");

        // ---- Shape: a polygon crescent round a bay that opens to the north ----
        var b = m.Boundary;
        if (b == null || b.Spec.Shape != BoundaryShape.Polygon)
        {
            Fail($"{m.Name}: expected a \"polygon\" boundary, got {(b == null ? "none" : b.Spec.Shape.ToString())}");
            return;
        }
        var horns = b.Contains(-45, -30) && b.Contains(45, -30);
        var bay = !b.Contains(0, -40) && !b.Contains(-18, -12) && !b.Contains(18, -12);
        if (!horns || !bay)
            Fail($"{m.Name}: no crescent (both horns inside {horns}, the bay between them outside {bay})");
        if (m.Size != MapSize.Medium || m.MaxPlayers != 12 || m.MaxPlayersSetting != 12 || b.Area is < 7000 or > 9000)
            Fail($"{m.Name}: {m.Size}, {b.Area:0} m2, {m.MaxPlayers} players (expected Medium, 7,000-9,000 m2, \"maxPlayers\": 12)");

        // ---- Density ----
        var props = m.PropInstances;
        var solid = props.Count(p => p.Def.IsSolid);
        var breakable = props.Count(p => p.IsBreakable);
        var cosmetic = props.Count - solid;
        var boxes = Enumerable.Range(0, m.Solid.Count).Count(i => m.IsDrawnBox(i) && !m.IsTrunk(i));
        var plants = m.FoliageInstances.Length;
        var meshes = m.MeshPlacements.Count - m.BoundaryMeshCount;
        var afloat = props.Where(p => p.Position.Y < -0.3f).ToList();
        ClutteredLine($"{m.Name}: {props.Count} props ({props.Count - m.ClutterStart} clutter): {solid} solid, {cosmetic} cosmetic, {breakable} breakable, "
            + $"{afloat.Count} afloat; {boxes} boxes, {m.TrunkCount} trees, {plants} plants, {meshes} meshes, {b.SegmentCount} outline segments; by set: "
            + string.Join(", ", props.GroupBy(p => p.Def.Category).OrderBy(g => g.Key).Select(g => $"{g.Key} {g.Count(p => p.Def.IsSolid)}/{g.Count(p => !p.Def.IsSolid)}")));
        if (solid < 350 || cosmetic < 1000 || breakable < 250 || plants < 15000 || meshes < 500 || m.TrunkCount < 20)
            Fail($"{m.Name}: not cluttered enough (solid props {solid}/350, cosmetic {cosmetic}/1000, breakable {breakable}/250, plants {plants}/15000, "
                 + $"meshes {meshes}/500, trees {m.TrunkCount}/20)");
        if (boxes > 400)
            Fail($"{m.Name}: {boxes} boxes (each is a draw call; keep it under 400 and use props and meshes)");

        // ---- Nothing outside the outline, except boats, buoys and ducks floating on the sea ----
        var outside = new List<string>();
        foreach (var p in props)
        {
            var isIn = b.Contains(p.Position.X, p.Position.Z);
            if (p.Position.Y < -0.3f ? isIn || m.GroundHeight(p.Position.X, p.Position.Z) > -1.5f : !isIn)
                outside.Add($"{(p.Position.Y < -0.3f ? "floater off the water: " : "")}{p.Def.Name} ({p.Position.X:0.0},{p.Position.Z:0.0})");
        }
        foreach (var f in m.FoliageInstances)
            if (!b.Contains(f.Position.X, f.Position.Z))
                outside.Add($"{f.Type} ({f.Position.X:0.0},{f.Position.Z:0.0})");
        for (var i = 0; i < m.Solid.Count; i++)
            if (m.IsDrawnBox(i) && !b.Contains(m.Solid[i].Center.X, m.Solid[i].Center.Z))
                outside.Add($"box ({m.Solid[i].Center.X:0.0},{m.Solid[i].Center.Z:0.0})");
        foreach (var w in m.Waypoints)
            if (!b.Contains(w.X, w.Z))
                outside.Add($"waypoint ({w.X:0.0},{w.Z:0.0})");
        if (outside.Count > 0)
            Fail($"{m.Name}: {outside.Count} thing(s) outside the outline: {string.Join(", ", outside.Take(5))}");

        // ---- The sea: level quays right to the edge, a drop into the water beyond ----
        int dryQuay = 0, noDrop = 0;
        foreach (var (x, z, dx, dz) in new (float, float, float, float)[]
                 { (-30, -38, 1, 0), (-30, -20, 1, 0), (-18, -5, 0, -1), (-8, -5, 0, -1), (-4, -15, -1, 0), (-23, -34, 0, -1), (-13, -19, 0, -1), (-45, -45, 0, -1) })
            foreach (var s in new[] { 1f, -1f })
            {
                float px = x * s, pdx = dx * s;
                if (MathF.Abs(m.GroundHeight(px - pdx * 0.5f, z - dz * 0.5f)) > 0.02f)
                    dryQuay++;
                if (m.GroundHeight(px + pdx * 3f, z + dz * 3f) > -2f)
                    noDrop++;
            }
        if (dryQuay > 0 || noDrop > 0)
            Fail($"{m.Name}: the quay edge is wrong: {dryQuay} quay spot(s) not level, {noDrop} spot(s) 3 m out with no drop into the sea");

        // ---- Placement rules ----
        var drawn = Enumerable.Range(0, m.Solid.Count).Where(i => m.IsDrawnBox(i) && !m.IsTrunk(i)).Select(i => m.Solid[i]).ToList();
        static float Gap(Box bx, Vector3 p)
        {
            var dx = MathF.Max(0, MathF.Max(bx.Min.X - p.X, p.X - bx.Max.X));
            var dz = MathF.Max(0, MathF.Max(bx.Min.Z - p.Z, p.Z - bx.Max.Z));
            return MathF.Sqrt(dx * dx + dz * dz);
        }
        var rules = new List<string>();
        var dumpsters = props.Where(p => p.Def.Name == "Dumpster").ToList();
        foreach (var d in dumpsters)
        {
            var bags = props.Count(p => p.Def.Name == "Trash Bag" && Vector3.Distance(p.Position, d.Position) < 3.5f);
            var wall = drawn.Any(bx => bx.Max.Y > 2.5f && Gap(bx, d.Position) < 1.5f);
            if (bags < 2 || !wall)
                rules.Add($"dumpster ({d.Position.X:0.0},{d.Position.Z:0.0}) with {bags} trash bags, {(wall ? "by" : "not by")} a wall");
        }
        string[] appliances = ["Fridge", "Cooker", "Microwave", "Kettle", "Coffee Maker", "Toaster", "Dishwasher", "Kitchen Sink", "Kitchen Counter"];
        var indoor = props.Where(p => (p.Def.Category is "Office" or "Household" || appliances.Contains(p.Def.Name)) && p.Position.Y < 1.5f).ToList();
        foreach (var p in indoor)
            if (!drawn.Any(bx => bx.Min.Y > p.Position.Y + 1.8f && p.Position.X > bx.Min.X && p.Position.X < bx.Max.X && p.Position.Z > bx.Min.Z && p.Position.Z < bx.Max.Z))
                rules.Add($"{p.Def.Name} ({p.Position.X:0.0},{p.Position.Z:0.0}) isn't under a roof");
        var rooftop = props.Where(p => p.Def.Category == "Rooftop" && p.Def.Name is not ("Vent Grille" or "Window AC Unit")).ToList(); // those two go on walls
        foreach (var p in rooftop)
            if (!drawn.Any(bx => MathF.Abs(bx.Max.Y - p.Position.Y) < 0.05f && p.Position.X > bx.Min.X && p.Position.X < bx.Max.X && p.Position.Z > bx.Min.Z && p.Position.Z < bx.Max.Z))
                rules.Add($"{p.Def.Name} ({p.Position.X:0.0},{p.Position.Z:0.0}) isn't on a roof");
        var lamps = props.Count(p => p.Def.Name is "Street Lamp" or "Flickering Street Lamp");
        var vehicles = props.Count(p => p.Def.Category == "Vehicles");
        ClutteredLine($"{m.Name} placement: {dumpsters.Count} dumpsters, {indoor.Count} indoor props, {rooftop.Count} rooftop props, {lamps} street lamps, "
            + $"{vehicles} vehicles, {afloat.Count} floaters; {rules.Count} rule break(s)");
        if (dumpsters.Count < 4 || indoor.Count < 30 || rooftop.Count < 20 || lamps < 12 || afloat.Count < 12)
            Fail($"{m.Name}: missing dressing ({dumpsters.Count} dumpsters, {indoor.Count} indoor, {rooftop.Count} rooftop, {lamps} lamps, {afloat.Count} floaters)");
        if (rules.Count > 0)
            Fail($"{m.Name}: {rules.Count} prop(s) out of place: {string.Join("; ", rules.Take(5))}");

        // ---- Spawns and objectives ----
        var o = m.Objectives;
        var authored = m.AuthoredSpawns >= 0 ? m.AuthoredSpawns : m.Spawns.Count;
        if (authored < 12)
            Fail($"{m.Name}: {authored} hand-placed spawns (12 players need 12)");
        if (o.TeamSpawnsGenerated || o.HillsGenerated || o.FlagsGenerated)
            Fail($"{m.Name}: objectives should be hand-placed");
        if (o.TeamSpawns[0].Count < 6 || o.TeamSpawns[1].Count < 6 || o.Hills.Count != 3)
            Fail($"{m.Name}: {o.TeamSpawns[0].Count} + {o.TeamSpawns[1].Count} team spawns and {o.Hills.Count} hills (expected 6 + 6 and 3)");
        if (props.Skip(m.ClutterStart).Any(p => p.Def.IsSolid))
            Fail($"{m.Name}: Clutter placed solid props (solid cover is hand-placed in mirrored pairs; Clutter is for litter)");
        var blocked = new List<string>();
        foreach (var p in props.Where(p => p.Def.IsSolid))
            for (var k = p.SolidStart; k < p.SolidStart + p.SolidCount; k++)
            {
                var bx = m.Solid[k];
                var why = m.Spawns.Any(s => Gap(bx, s) < 1f && bx.Max.Y > s.Y + 0.6f) ? "a spawn"
                    : o.Hills.Any(h => Gap(bx, h.Center) < h.Radius && bx.Max.Y > h.Center.Y + 0.6f) ? "a hill zone"
                    : o.Flags.Any(f => Gap(bx, f) < 2f && bx.Max.Y > f.Y + 0.6f) ? "a flag" : null;
                if (why != null)
                {
                    blocked.Add($"{p.Def.Name} ({p.Position.X:0.0},{p.Position.Z:0.0}) on {why}");
                    break;
                }
            }
        if (blocked.Count > 0)
            Fail($"{m.Name}: {blocked.Count} solid prop(s) in the way: {string.Join("; ", blocked.Take(4))}");

        // ---- Fairness: every solid prop has a twin mirrored about x = 0 ----
        var unmatched = new List<string>();
        var solidIdx = Enumerable.Range(0, props.Count).Where(i => props[i].Def.IsSolid).ToList();
        var used = new HashSet<int>();
        foreach (var i in solidIdx)
        {
            if (used.Contains(i))
                continue;
            var a = props[i];
            if (MathF.Abs(a.Position.X) < 0.05f)
            {
                used.Add(i);
                continue;
            }
            var twin = solidIdx.FirstOrDefault(j => j != i && !used.Contains(j) && props[j].Def == a.Def
                && MathF.Abs(props[j].Position.X + a.Position.X) < 0.05f && MathF.Abs(props[j].Position.Z - a.Position.Z) < 0.05f
                && MathF.Abs(props[j].Position.Y - a.Position.Y) < 0.05f && MathF.Abs(props[j].Scale - a.Scale) < 0.01f
                && MathF.Abs(MathF.IEEERemainder(props[j].Yaw + a.Yaw, MathF.Tau)) < 0.02f, -1);
            if (twin < 0)
                unmatched.Add($"{a.Def.Name} ({a.Position.X:0.0},{a.Position.Z:0.0})");
            else
            {
                used.Add(i);
                used.Add(twin);
            }
        }
        if (unmatched.Count > 0)
            Fail($"{m.Name}: {unmatched.Count} solid prop(s) without a mirrored twin: {string.Join(", ", unmatched.Take(5))}");

        // ---- Nav graph: everything reachable, equal walks for both teams ----
        World.SetMap(m);
        for (var wait = 0; wait < 2000 && NavGraph.Current == NavGraph.Empty; wait++)
            NavGraph.Pump(50);
        var g = NavGraph.Current;
        var search = new NavSearch();
        var path = new int[g.Nodes.Length];
        float Walk(Vector3 from, Vector3 to)
        {
            var n = search.Find(g, g.Nearest(from, needSight: false), g.Nearest(to, needSight: false), path, maxExpand: g.Nodes.Length);
            if (n == 0)
                return float.NaN;
            var len = 0f;
            for (var k = 1; k < n; k++)
                len += Vector3.Distance(g.Nodes[path[k - 1]], g.Nodes[path[k]]);
            return len;
        }
        float TeamWalk(int team, Vector3 to) => o.TeamSpawns[team].Sum(s => Walk(s, to)) / o.TeamSpawns[team].Count;
        var walks = new List<string>();
        void Compare(string what, float blue, float red)
        {
            walks.Add($"{what} {blue:0}/{red:0} m");
            if (!float.IsFinite(blue) || !float.IsFinite(red))
                Fail($"{m.Name}: no route to {what} (blue {blue:0}, red {red:0})");
            else if (MathF.Abs(blue - red) > 0.1f * MathF.Max(blue, red))
                Fail($"{m.Name}: unfair walk to {what}: blue {blue:0.0} m, red {red:0.0} m (over 10% apart)");
        }
        for (var h = 0; h < o.Hills.Count; h++)
            Compare($"hill {h + 1}", TeamWalk(0, o.Hills[h].Center), TeamWalk(1, o.Hills[h].Center));
        Compare("enemy flag", TeamWalk(0, o.Flags[1]), TeamWalk(1, o.Flags[0]));
        ClutteredLine($"{m.Name}: nav {g.Nodes.Length} nodes, walks blue/red: {string.Join(", ", walks)}");
        var unreachable = m.Spawns.Concat(o.Hills.Select(h => h.Center)).Concat(o.Flags)
            .Count(p => g.Nearest(p, needSight: false) is var k && (k < 0 || g.Component[k] != g.MainComponent));
        if (unreachable > 0)
            Fail($"{m.Name}: {unreachable} spawn(s) or objective(s) off the main nav area");

        ClutteredMediumBotMatch(m); // SelfTestCluttered.Medium.cs: a full free-for-all for 60 s
    }
}
