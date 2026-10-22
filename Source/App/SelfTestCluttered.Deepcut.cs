using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The Cluttered pack's map 11, "Deepcut Mine": a large, NOT mirrored mine site (16 players) on terrain, with the low
/// camp and plant on the west, a terraced massif with the headframe on the east and tunnels through the hills between.
/// It gets the checks every Cluttered map gets (<see cref="CheckClutteredSmallMap"/>: density quota, everything inside
/// the outline, hand-placed objectives, nav reach, free-for-all and team bot matches at the player limit), plus its own:
/// the outline and size, the terrain and the shaft cage, the tunnels (a trench floor at the right height under a roof,
/// no plants under it, a bot nav node on the floor), the asymmetry (Red spawns high, Blue low), and fairness measured
/// instead of mirrored: the nav walk from each team's spawns to every hill and to the enemy flag within 10%, no line of
/// sight between the two teams' spawns, and more cover along the low team's routes to the hills than the high team's
/// (the height is the high side's advantage), but not bare ones for the high side.
/// </summary>
public static partial class SelfTest
{
    private const string ClutteredDeepcutMap = "Deepcut Mine";

    /// <summary>Points under each tunnel (x, z, the floor's height there) from tools/cluttered/deepcut.py.</summary>
    private static readonly (string Name, float X, float Z, float Floor)[] DeepcutTunnelProbes =
    [
        ("North Drift west", -25f, -40f, 1.26f), ("North Drift east", 5f, -40f, 3.55f), ("North Drift adit", -6f, -35f, 1.6f),
        ("South Adit low", -2f, 32f, -4f), ("South Adit middle", -2f, 45f, -1.2f), ("South Adit high", 14f, 52.5f, 3.5f),
        ("Main Haulage", 50f, 6f, 4.5f), ("Shaft North", 67f, -10f, 7.6f), ("Shaft South", 69.6f, 20f, 4.5f), ("shaft station", 64f, 3f, 4.5f),
    ];

    private static void CheckClutteredDeepcut()
    {
        var m = Maps.FindPack(ClutteredPack)?.Maps.FirstOrDefault(x => x.Name == ClutteredDeepcutMap);
        if (m == null)
        {
            Fail($"{ClutteredPack}: no map \"{ClutteredDeepcutMap}\" (11-DeepcutMine.json)");
            return;
        }
        if (m.SourceFile == null || !Path.GetFileName(m.SourceFile).StartsWith("11-", StringComparison.Ordinal))
            Fail($"{m.Name}: file should be Maps/Cluttered/11-DeepcutMine.json (is {m.SourceFile})");
        if (m.Boundary is not { } bd || bd.Spec.Shape != BoundaryShape.Smooth || bd.Spec.Noise is not { Amplitude: > 0 })
        {
            Fail($"{m.Name}: boundary should be a noisy smooth outline");
            return;
        }
        if (m.Size != MapSize.Large || bd.Area is < 16000 or > 22000)
            Fail($"{m.Name}: size class {m.Size}, {bd.Area:0} m2 (expected Large, 16,000-22,000 m2)");
        if (m.MaxPlayersSetting != 16 || m.MaxPlayers != 16)
            Fail($"{m.Name}: maxPlayers {m.MaxPlayersSetting} in the file, {m.MaxPlayers} in effect (expected 16)");
        if (!m.HasTerrain)
            Fail($"{m.Name}: no terrain");
        if (m.Movers.Count != 1)
            Fail($"{m.Name}: {m.Movers.Count} movers (expected the shaft cage)");

        // Draw-call side of the counts, and the props by set.
        var drawn = Enumerable.Range(0, m.Solid.Count).Count(m.IsDrawnBox);
        var trees = m.FoliageInstances.Count(f => Foliage.IsTree(f.Type));
        var props = m.PropInstances;
        var bySet = string.Join(", ", props.GroupBy(p => p.Def.Category).OrderByDescending(g => g.Count())
            .Select(g => $"{g.Key} {g.Count(p => p.Def.IsSolid)}/{g.Count(p => !p.Def.IsSolid)}"));
        var explosive = props.Count(p => p.Def.Breakable is { ExplodeRadius: > 0 });
        ClutteredLine($"{m.Name}: {drawn} boxes drawn one by one, {trees} trees, {props.Count(p => p.Def.Parts.Any(q => q.Anim != null))} animated props, {explosive} explosive; "
                      + $"props by set (solid/cosmetic): {bySet}");
        if (drawn > 700)
            Fail($"{m.Name}: {drawn} boxes drawn one by one (keep it under 700: use props, meshes and foliage)");
        if (trees is < 30 or > 400)
            Fail($"{m.Name}: {trees} trees (expected 30-400)");
        if (explosive is < 4 or > 12)
            Fail($"{m.Name}: {explosive} explosive props (expected a few: 4-12)");

        CheckClutteredSmallMap(m, new SmallClutterQuota(Props: 3400, SolidProps: 250, CosmeticProps: 3000, Breakable: 600, Plants: 30000, Trees: 40, Boxes: 500));

        var o = m.Objectives;
        var g = ClutteredLargeNav(m);

        // Asymmetric: Red spawns on the high ground, Blue down low.
        float blueY = o.TeamSpawns[0].Average(s => s.Y), redY = o.TeamSpawns[1].Average(s => s.Y);
        if (redY - blueY < 10f)
            Fail($"{m.Name}: Red's spawns should be on the high ground (average height {redY:0.0} m vs Blue's {blueY:0.0} m)");

        // Tunnels: a trench floor at the right height, a roof over it, no plants under it, a nav node on the floor.
        var tunnelNotes = new List<string>();
        foreach (var (name, x, z, floor) in DeepcutTunnelProbes)
        {
            var gy = m.GroundHeight(x, z);
            var up = World.RaycastBoxes(new Vector3(x, gy + 0.3f, z), Vector3.UnitY, 8f);
            var plants = m.FoliageInstances.Count(f => MathF.Abs(f.Position.X - x) < 1.5f && MathF.Abs(f.Position.Z - z) < 1.5f && f.Position.Y < gy + 1.5f);
            var node = false;
            for (var i = 0; i < g.Nodes.Length && !node; i++)
            {
                var q = g.Nodes[i];
                node = g.Component[i] == g.MainComponent && MathF.Abs(q.Y - gy) < 1.2f && (q.X - x) * (q.X - x) + (q.Z - z) * (q.Z - z) < 9f;
            }
            tunnelNotes.Add($"{name} floor {gy:0.0} m roof +{up + 0.3f:0.0}");
            if (MathF.Abs(gy - floor) > 0.6f)
                Fail($"{m.Name}: {name} ({x:0},{z:0}): the floor is at {gy:0.00} m, expected {floor:0.00} m (the trench isn't cut)");
            if (up + 0.3f is < 2.4f or > 4.2f)
                Fail($"{m.Name}: {name} ({x:0},{z:0}): no roof over the tunnel (first box above the floor at +{up + 0.3f:0.0} m)");
            if (plants > 0)
                Fail($"{m.Name}: {name} ({x:0},{z:0}): {plants} plant(s) growing inside the tunnel");
            if (!node)
                Fail($"{m.Name}: {name} ({x:0},{z:0}): no bot nav node on the tunnel floor");
        }
        ClutteredLine($"{m.Name} tunnels: {string.Join(", ", tunnelNotes)}");

        // No line of sight between the two teams' spawns (eye height, terrain, boxes and props).
        var seen = new List<string>();
        foreach (var a in o.TeamSpawns[0])
            foreach (var b in o.TeamSpawns[1])
            {
                Vector3 ea = a + new Vector3(0, 1.6f, 0), eb = b + new Vector3(0, 1.6f, 0);
                var d = Vector3.Distance(ea, eb);
                if (World.RaycastWorld(ea, (eb - ea) / d, d) >= d - 0.05f)
                    seen.Add($"({a.X:0},{a.Z:0})-({b.X:0},{b.Z:0})");
            }
        if (seen.Count > 0)
            Fail($"{m.Name}: {seen.Count} Blue/Red spawn pair(s) can see each other: {string.Join(", ", seen.Take(4))}");

        // Fairness by route: the nav walk (averaged over each team's spawns) to every hill and to the enemy flag.
        var search = new NavSearch();
        var path = new int[2048];
        var routes = new[] { new List<Vector3>(), new List<Vector3>() };
        float Walk(int team, Vector3 goal, bool keep)
        {
            var total = 0f;
            var gi = g.Nearest(goal, needSight: false);
            foreach (var s in o.TeamSpawns[team])
            {
                var count = search.Find(g, g.Nearest(s, needSight: false), gi, path, maxExpand: 30000);
                if (count == 0)
                    return float.PositiveInfinity;
                var len = Vector3.Distance(s, g.Nodes[path[0]]) + Vector3.Distance(g.Nodes[path[count - 1]], goal);
                for (var k = 1; k < count; k++)
                    len += Vector3.Distance(g.Nodes[path[k - 1]], g.Nodes[path[k]]);
                if (keep)
                    for (var k = 0; k < count; k++)
                        routes[team].Add(g.Nodes[path[k]]);
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

        // Cover along the routes: solid cover at least 0.9 m tall within 6 m of each team's routes to the hills.
        var cover = new List<Vector3>();
        foreach (var p in props.Where(p => p.Def.IsSolid && p.Bounds.Max.Y - m.GroundHeight(p.Position.X, p.Position.Z) >= 0.9f))
            cover.Add(p.Bounds.Center);
        for (var i = 0; i < m.Solid.Count; i++)
            if (m.IsDrawnBox(i) && m.Solid[i].Max.Y - m.GroundHeight(m.Solid[i].Center.X, m.Solid[i].Center.Z) >= 0.9f)
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
        int coverBlue = CoverNear(routes[0]), coverRed = CoverNear(routes[1]);
        var ratio = MathF.Min(coverBlue, coverRed) / (float)Math.Max(1, Math.Max(coverBlue, coverRed));
        ClutteredLine($"{m.Name} balance (Blue/Red nav walk): {string.Join(", ", walks)}; cover near the routes to the hills {coverBlue}/{coverRed} ({ratio:P0}); "
                      + $"spawn heights {blueY:0.0}/{redY:0.0} m; spawn pairs in sight {seen.Count}");
        // Not mirrored, so not equal: the low side (Blue) gets at least as much cover as the high side to make up for the
        // height, and the high side's routes aren't bare either.
        if (coverBlue < coverRed || coverRed < coverBlue * 0.3f)
            Fail($"{m.Name}: cover on the routes to the hills doesn't balance the height (Blue, low: {coverBlue}; Red, high: {coverRed}; "
                 + "the low side should have at least as much, the high side at least 30% of it)");
    }
}
