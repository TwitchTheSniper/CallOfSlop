using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The Cluttered pack's map 07, "Supermarket": a run-down supermarket on a noisy polygon outline (medium, 12 players),
/// most of it indoors (aisles, departments, checkouts, a staff mezzanine, the stock room). It gets the checks every
/// Cluttered map gets (<see cref="CheckClutteredSmallMap"/>: density quota, everything inside the outline, hand-placed
/// objectives, nav reach, free-for-all and team bot matches at the player limit), plus its own:
/// <list type="bullet">
/// <item>shape and size: a noisy polygon, Medium, 7,000-9,000 m2, 12 players;</item>
/// <item>it reads as a supermarket: enough shelving, checkouts, freezers, chillers, deli counters and produce bins, the
/// ceiling lights and hanging signs up under the roof, and no plant under the roof;</item>
/// <item>it is NOT mirrored (most solid props have no twin across either axis or the centre), so fairness is measured
/// instead: the nav walk from each team's spawns to every hill and to the enemy flag within 10%, comparable solid
/// cover along both teams' routes to the hills, and no line of sight from any Blue spawn to any Red spawn;</item>
/// <item>the mezzanine (the high ground) is on the main nav graph, so bots from both teams can get up there;</item>
/// <item>every dumpster stands with its back to a wall with trash bags round it.</item>
/// </list>
/// The asymmetric checks live here, so the mirrored maps' twin checks (01, 02, 04, 05) stay exactly as strict.
/// </summary>
public static partial class SelfTest
{
    private const string ClutteredSupermarketMap = "Supermarket";

    private static void CheckClutteredSupermarket()
    {
        var m = Maps.FindPack(ClutteredPack)?.Maps.FirstOrDefault(x => x.Name == ClutteredSupermarketMap);
        if (m == null)
        {
            Fail($"{ClutteredPack}: no map \"{ClutteredSupermarketMap}\" (07-Supermarket.json)");
            return;
        }
        if (m.SourceFile == null || !Path.GetFileName(m.SourceFile).StartsWith("07-", StringComparison.Ordinal))
            Fail($"{m.Name}: file should be Maps/Cluttered/07-Supermarket.json (is {m.SourceFile})");
        if (m.Boundary is not { } bd || bd.Spec.Shape != BoundaryShape.Polygon || bd.Spec.Noise is not { Amplitude: > 0 })
        {
            Fail($"{m.Name}: boundary should be a noisy polygon");
            return;
        }
        if (m.Size != MapSize.Medium || bd.Area is < 7000 or > 9000)
            Fail($"{m.Name}: size class {m.Size}, {bd.Area:0} m2 (expected Medium, 7,000-9,000 m2)");
        if (m.MaxPlayersSetting != 12 || m.MaxPlayers != 12)
            Fail($"{m.Name}: maxPlayers {m.MaxPlayersSetting} in the file, {m.MaxPlayers} in effect (expected 12)");

        var props = m.PropInstances;
        var drawn = Enumerable.Range(0, m.Solid.Count).Count(m.IsDrawnBox);
        var byName = props.GroupBy(p => p.Def.Name).ToDictionary(g => g.Key, g => g.Count());
        int Count(string prefix) => byName.Where(kv => kv.Key.StartsWith(prefix, StringComparison.Ordinal)).Sum(kv => kv.Value);
        var animated = props.Count(p => p.Def.Parts.Any(q => q.Anim != null));
        ClutteredLine($"{m.Name}: {drawn} boxes drawn one by one, {animated} animated props; shelves {Count("Grocery Shelf")} + {Count("Shelf End Cap")} end caps, "
            + $"checkouts {Count("Checkout Counter")}, freezers {Count("Freezer Case")} + {Count("Freezer Island")} islands, chillers {Count("Dairy Chiller")}, "
            + $"deli {Count("Deli Counter")}, produce {Count("Produce Bin")}, carts {Count("Shopping Cart")}, lights {Count("Ceiling Light Panel") + Count("Flickering Ceiling Light")}, "
            + $"signs {Count("Hanging Aisle Sign") + Count("Department Sign")}");
        if (drawn > 300)
            Fail($"{m.Name}: {drawn} boxes drawn one by one (keep it under 300: use props, meshes and foliage)");

        CheckClutteredSmallMap(m, new SmallClutterQuota(Props: 2700, SolidProps: 450, CosmeticProps: 2200, Breakable: 1400, Plants: 6000, Trees: 12, Boxes: 1200));

        // It reads as a supermarket: the fittings are there, the lights and signs hang under the roof, nothing grows indoors.
        foreach (var (what, need) in new[] { ("Grocery Shelf", 36), ("Shelf End Cap", 36), ("Checkout Counter", 8), ("Freezer Case", 5), ("Freezer Island", 4),
                     ("Dairy Chiller", 6), ("Deli Counter", 4), ("Produce Bin", 12), ("Pallet Rack", 10), ("Shopping Cart", 6), ("Cart Corral", 2) })
            if (Count(what) < need)
                Fail($"{m.Name}: only {Count(what)} \"{what}\" props (expected at least {need})");
        const float roofY = 6.5f;
        static bool UnderRoof(float x, float z) => x is > -36f and < 36f && z is > -34f and < 20f;
        var hung = props.Where(p => p.Def.Name.Contains("Ceiling Light", StringComparison.Ordinal) || p.Def.Name == "Hanging Aisle Sign"
                                    || p.Def.Name.StartsWith("Department Sign", StringComparison.Ordinal)).ToList();
        var lowHung = hung.Where(p => p.Bounds.Max.Y > roofY + 0.05f || p.Bounds.Min.Y < 2.0f).Select(p => $"{p.Def.Name} ({p.Position.X:0},{p.Position.Z:0}) y {p.Bounds.Min.Y:0.0}-{p.Bounds.Max.Y:0.0}").ToList();
        if (lowHung.Count > 0)
            Fail($"{m.Name}: {lowHung.Count} ceiling lights or hanging signs not hung under the ceiling: {string.Join(", ", lowHung.Take(4))}");
        var indoorPlants = m.FoliageInstances.Count(f => UnderRoof(f.Position.X, f.Position.Z));
        if (indoorPlants > 0)
            Fail($"{m.Name}: {indoorPlants} plants inside the store (nothing grows under the roof)");

        // Not mirrored: most solid props have no twin across x = 0, z = 0 or the centre.
        var solid = props.Where(p => p.Def.IsSolid).ToList();
        var twins = 0;
        foreach (var p in solid)
        {
            var x = p.Position.X;
            var z = p.Position.Z;
            if (solid.Any(q => q.Def == p.Def && !(q.Position.X == x && q.Position.Z == z)
                               && (Near(q, -x, z) || Near(q, x, -z) || Near(q, -x, -z))))
                twins++;
        }
        static bool Near(PropInstance q, float x, float z) => MathF.Abs(q.Position.X - x) < 0.3f && MathF.Abs(q.Position.Z - z) < 0.3f;
        var twinShare = twins / (float)Math.Max(1, solid.Count);
        if (twinShare > 0.35f)
            Fail($"{m.Name}: {twinShare:P0} of the solid props have a mirror twin (the map should be asymmetric)");

        // Fairness by measurement: the nav walk (averaged over each team's spawns) to every hill and to the enemy flag.
        var o = m.Objectives;
        var g = ClutteredLargeNav(m);
        var search = new NavSearch();
        var path = new int[2048];
        var routeNodes = new[] { new List<Vector3>(), new List<Vector3>() };
        float Walk(int team, Vector3 goal, bool keepRoute)
        {
            var total = 0f;
            var gi = g.Nearest(goal, needSight: false);
            foreach (var s in o.TeamSpawns[team])
            {
                var count = search.Find(g, g.Nearest(s, needSight: false), gi, path, maxExpand: 40000);
                if (count == 0)
                    return float.PositiveInfinity;
                var len = Vector3.Distance(s, g.Nodes[path[0]]) + Vector3.Distance(g.Nodes[path[count - 1]], goal);
                for (var k = 1; k < count; k++)
                    len += Vector3.Distance(g.Nodes[path[k - 1]], g.Nodes[path[k]]);
                if (keepRoute)
                    for (var k = 0; k < count; k++)
                        routeNodes[team].Add(g.Nodes[path[k]]);
                total += len;
            }
            return total / Math.Max(1, o.TeamSpawns[team].Count);
        }
        var walks = new List<string>();
        void Check(string name, float blue, float red)
        {
            var diff = (blue - red) / MathF.Max(blue, red);
            walks.Add($"{name} {blue:0}/{red:0} m ({diff * 100:+0.0;-0.0;0.0}%)");
            if (!float.IsFinite(blue + red) || MathF.Abs(diff) > 0.1f)
                Fail($"{m.Name}: {name} is a {blue:0} m walk for Blue and {red:0} m for Red (more than 10% apart)");
        }
        for (var i = 0; i < o.Hills.Count; i++)
            Check($"hill {i + 1}", Walk(0, o.Hills[i].Center, true), Walk(1, o.Hills[i].Center, true));
        Check("enemy flag", Walk(0, o.Flags[1], false), Walk(1, o.Flags[0], false));

        // Comparable cover: solid things at least 0.9 m tall within 6 m of each team's routes to the hills.
        var cover = new List<Vector3>();
        foreach (var p in solid.Where(p => p.Bounds.Max.Y - m.GroundHeight(p.Position.X, p.Position.Z) >= 0.9f))
            cover.Add(p.Bounds.Center);
        for (var i = 0; i < m.Solid.Count; i++)
            if (m.IsDrawnBox(i) && m.Solid[i].Max.Y - m.Solid[i].Min.Y < 4f && m.Solid[i].Max.Y >= 0.9f)
                cover.Add(m.Solid[i].Center);
        int CoverNear(List<Vector3> route)
        {
            var c = 0;
            foreach (var q in cover)
                foreach (var r in route)
                    if ((q.X - r.X) * (q.X - r.X) + (q.Z - r.Z) * (q.Z - r.Z) < 36f)
                    {
                        c++;
                        break;
                    }
            return c;
        }
        int coverBlue = CoverNear(routeNodes[0]), coverRed = CoverNear(routeNodes[1]);
        var coverRatio = MathF.Min(coverBlue, coverRed) / (float)Math.Max(1, Math.Max(coverBlue, coverRed));
        if (coverRatio < 0.7f)
            Fail($"{m.Name}: uneven cover on the routes to the hills (Blue {coverBlue}, Red {coverRed})");

        // No line of sight between the teams' spawns (eye height, through everything solid and the outline's barrier).
        var eye = new Vector3(0, 1.6f, 0);
        var seen = new List<string>();
        foreach (var a in o.TeamSpawns[0])
            foreach (var b in o.TeamSpawns[1])
            {
                var p = a + eye;
                var d = b + eye - p;
                var len = d.Length();
                if (World.RaycastWorld(p, d / len, len) >= len - 0.05f)
                    seen.Add($"({a.X:0},{a.Z:0})-({b.X:0},{b.Z:0})");
            }
        if (seen.Count > 0)
            Fail($"{m.Name}: {seen.Count} Blue/Red spawn pair(s) can see each other: {string.Join(", ", seen.Take(4))}");

        // The mezzanine (staff office and break room, about 3.6 m up over the deli) is on the main nav graph.
        var mezz = Enumerable.Range(0, g.Nodes.Length).Count(i => g.Component[i] == g.MainComponent && g.Nodes[i].Y is > 3.3f and < 4.0f
                                                                && g.Nodes[i].X is > 10.5f and < 35.5f && g.Nodes[i].Z is > -14f and < -7f);
        if (mezz < 10)
            Fail($"{m.Name}: only {mezz} nav nodes on the mezzanine in the main area (bots can't get up the stairs?)");

        // Dumpsters stand with their backs to a wall, with trash bags round them.
        var bags = props.Where(p => p.Def.Name == "Trash Bag").Select(p => new Vector2(p.Position.X, p.Position.Z)).ToList();
        foreach (var dmp in props.Where(p => p.Def.Name == "Dumpster"))
        {
            var c = new Vector2(dmp.Position.X, dmp.Position.Z);
            var wall = Enumerable.Range(0, m.Solid.Count).Any(i => m.IsDrawnBox(i) && m.Solid[i].Max.Y > 2.5f
                && MathF.Max(0, MathF.Max(m.Solid[i].Min.X - c.X, c.X - m.Solid[i].Max.X)) + MathF.Max(0, MathF.Max(m.Solid[i].Min.Z - c.Y, c.Y - m.Solid[i].Max.Z)) < 1.6f);
            var near = bags.Count(b => Vector2.Distance(b, c) < 4f);
            if (!wall || near < 2)
                Fail($"{m.Name}: dumpster at ({c.X:0.0},{c.Y:0.0}) has {(wall ? "" : "no wall behind it and ")}{near} trash bags round it");
        }

        var balance = $"{m.Name} balance (asymmetric; Blue/Red nav walk averaged over team spawns): {string.Join(", ", walks)}; cover within 6 m of the routes to the hills "
            + $"{coverBlue}/{coverRed} ({coverRatio:P0}); spawn sight lines {seen.Count}; mirror twins {twinShare:P0} of {solid.Count} solid props; mezzanine nav nodes {mezz}";
        ClutteredLine(balance);
        Line("::notice title=Cluttered 07 Supermarket::" + balance.Replace("%", "%25")); // its own notice: the pack's one is long enough to get cut off
    }
}
