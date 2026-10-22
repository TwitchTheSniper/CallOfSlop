using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The Cluttered pack's map 09, "Beachhead": a beach that leads up a bluff into a military base, on terrain (large, 16
/// players). It gets the checks every Cluttered map gets (<see cref="CheckClutteredSmallMap"/>: density quota, everything
/// inside the outline, hand-placed objectives, nav reach, free-for-all and team bot matches at the player limit), plus
/// its own. It is deliberately not mirrored (Blue lands on the beach, Red holds the base 12 m up), so instead of twin
/// checks it checks fairness by measurement: the nav walk and the nav walking time (uphill costs more) from each team's
/// spawns to every hill and the enemy flag within 10%, a scripted walker reaching every objective from both sides, no
/// line of sight between the two teams' spawns, and the beach side's routes passing at least as much cover as the
/// base side's (the base has the height; the beach gets more cover and more ways up). It also checks that the map
/// really is asymmetric, and that the ways up the bluff (the drainage tunnel, the pillboxes dug into the top of the
/// face, the overlook and the dune post) are on the bots' nav graph.
/// </summary>
public static partial class SelfTest
{
    private const string ClutteredBeachheadMap = "Beachhead";

    private static void CheckClutteredBeachhead()
    {
        var m = Maps.FindPack(ClutteredPack)?.Maps.FirstOrDefault(x => x.Name == ClutteredBeachheadMap);
        if (m == null)
        {
            Fail($"{ClutteredPack}: no map \"{ClutteredBeachheadMap}\" (09-Beachhead.json)");
            return;
        }
        if (m.SourceFile == null || !Path.GetFileName(m.SourceFile).StartsWith("09-", StringComparison.Ordinal))
            Fail($"{m.Name}: file should be Maps/Cluttered/09-Beachhead.json (is {m.SourceFile})");
        if (m.Boundary is not { } bd || bd.Spec.Shape != BoundaryShape.Smooth || bd.Spec.Noise is not { Amplitude: > 0 })
        {
            Fail($"{m.Name}: boundary should be a noisy smooth outline");
            return;
        }
        if (m.Size != MapSize.Large || bd.Area is < 16000 or > 22000)
            Fail($"{m.Name}: size class {m.Size}, {bd.Area:0} m2 (expected Large, 16,000-22,000 m2)");
        if (m.MaxPlayersSetting != 16 || m.MaxPlayers != 16)
            Fail($"{m.Name}: maxPlayers {m.MaxPlayersSetting} in the file, {m.MaxPlayers} in effect (expected 16)");
        if (!m.HasTerrain || m.TerrainCollision is not { } terrain)
        {
            Fail($"{m.Name}: no terrain");
            return;
        }

        // Draw-call side of the counts, the animated props and the explosive barrels.
        var props = m.PropInstances;
        var drawn = Enumerable.Range(0, m.Solid.Count).Count(m.IsDrawnBox);
        var trees = m.FoliageInstances.Count(f => Foliage.IsTree(f.Type));
        var animated = props.Count(p => p.Def.Parts.Any(q => q.Anim != null));
        var explosive = props.Count(p => p.Def.Breakable is { ExplodeRadius: > 0 });
        var pack = props.Count(p => p.Def.Category == "Beachhead");
        ClutteredLine($"{m.Name}: {drawn} boxes drawn one by one, {trees} trees, {animated} animated props, {explosive} explosive, {pack} pack props from Props/Beachhead");
        if (drawn > 700)
            Fail($"{m.Name}: {drawn} boxes drawn one by one (keep it under 700: use props, meshes and foliage)");
        if (trees is < 40 or > 300)
            Fail($"{m.Name}: {trees} trees (expected 40-300)");
        if (explosive < 8)
            Fail($"{m.Name}: only {explosive} explosive props (the fuel depot should have its barrels)");
        if (pack < 100)
            Fail($"{m.Name}: only {pack} props from the pack's Beachhead folder (hedgehogs, wrecks, army kit)");

        CheckClutteredSmallMap(m, new SmallClutterQuota(Props: 2600, SolidProps: 380, CosmeticProps: 2000, Breakable: 700, Plants: 30000, Trees: 40, Boxes: 700));

        // Asymmetric by design: few solid props have a mirror twin (about x = 0, z = 0 or the centre), and Red's spawns
        // are on the high ground (the plateau), Blue's down on the beach.
        var solid = props.Where(p => p.Def.IsSolid).ToList();
        var twins = solid.Count(p => solid.Any(q => q != p && q.Def == p.Def && (
            (MathF.Abs(q.Position.X + p.Position.X) < 0.3f && MathF.Abs(q.Position.Z - p.Position.Z) < 0.3f) ||
            (MathF.Abs(q.Position.X - p.Position.X) < 0.3f && MathF.Abs(q.Position.Z + p.Position.Z) < 0.3f) ||
            (MathF.Abs(q.Position.X + p.Position.X) < 0.3f && MathF.Abs(q.Position.Z + p.Position.Z) < 0.3f))));
        var o = m.Objectives;
        float MeanY(List<Vector3> s) => s.Count == 0 ? 0 : s.Average(p => terrain.Data.Sample(p.X, p.Z));
        float blueY = MeanY(o.TeamSpawns[0]), redY = MeanY(o.TeamSpawns[1]);
        ClutteredLine($"{m.Name} asymmetry: {twins} of {solid.Count} solid props have a mirrored twin; spawn ground Blue {blueY:0.0} m, Red {redY:0.0} m");
        if (twins > solid.Count / 5)
            Fail($"{m.Name}: {twins} of {solid.Count} solid props have a mirror twin (the map should not be mirrored)");
        if (redY - blueY < 6f)
            Fail($"{m.Name}: Red's spawns should be on the high ground ({redY:0.0} m against Blue's {blueY:0.0} m)");

        // Fairness by route: the nav walk and walking time (averaged over each team's spawns) to every hill and the enemy flag.
        var g = ClutteredLargeNav(m);
        var search = new NavSearch();
        var path = new int[4096];
        var routes = new[] { new List<Vector3>(), new List<Vector3>() };
        (float Len, float Time) Walk(int team, Vector3 goal, bool keep)
        {
            float len = 0, time = 0;
            var gi = g.Nearest(goal, needSight: false);
            foreach (var s in o.TeamSpawns[team])
            {
                var count = search.Find(g, g.Nearest(s, needSight: false), gi, path, maxExpand: 40000);
                if (count == 0)
                    return (float.PositiveInfinity, float.PositiveInfinity);
                var ends = Vector3.Distance(s, g.Nodes[path[0]]) + Vector3.Distance(g.Nodes[path[count - 1]], goal);
                len += ends;
                time += ends;
                for (var k = 1; k < count; k++)
                {
                    len += Vector3.Distance(g.Nodes[path[k - 1]], g.Nodes[path[k]]);
                    var cost = Vector3.Distance(g.Nodes[path[k - 1]], g.Nodes[path[k]]);
                    for (var e = g.EdgeStart[path[k - 1]]; e < g.EdgeStart[path[k - 1] + 1]; e++)
                        if (g.EdgeTo[e] == path[k])
                        {
                            cost = g.EdgeCost[e];
                            break;
                        }
                    time += cost;
                    if (keep)
                        routes[team].Add(g.Nodes[path[k]]);
                }
            }
            var n = Math.Max(1, o.TeamSpawns[team].Count);
            return (len / n, time / n);
        }
        var walks = new List<string>();
        void Check(string name, Vector3 blueGoal, Vector3 redGoal, bool keep)
        {
            var (bl, bt) = Walk(0, blueGoal, keep);
            var (rl, rt) = Walk(1, redGoal, keep);
            var dl = (bl - rl) / MathF.Max(bl, rl);
            var dt = (bt - rt) / MathF.Max(bt, rt);
            walks.Add($"{name} {bl:0}/{rl:0} m ({dl * 100:+0.0;-0.0;0.0}%), time {bt:0}/{rt:0} ({dt * 100:+0.0;-0.0;0.0}%)");
            if (!float.IsFinite(bl + rl + bt + rt) || MathF.Abs(dl) > 0.1f || MathF.Abs(dt) > 0.1f)
                Fail($"{m.Name}: {name} is a {bl:0} m / {bt:0} s-equivalent walk for Blue and {rl:0} m / {rt:0} for Red (more than 10% apart)");
        }
        for (var i = 0; i < o.Hills.Count; i++)
            Check($"hill {i + 1}", o.Hills[i].Center, o.Hills[i].Center, true);
        Check("enemy flag", o.Flags[1], o.Flags[0], false);
        ClutteredLine($"{m.Name} balance (Blue/Red nav walk): {string.Join("; ", walks)}");

        // A scripted walker (a player holding forward along the bots' route) from each team's middle spawn reaches every
        // objective; the times are reported (one spawn each, so they're a sample, not the average above).
        var runs = new List<string>();
        for (var team = 0; team < 2; team++)
        {
            var from = SlopeTeamStart(o.TeamSpawns[team]);
            var goals = o.Hills.Select((h, i) => ($"h{i + 1}", h.Center)).Append(("flag", o.Flags[1 - team]));
            foreach (var (name, at) in goals)
            {
                var r = SlopeFollow(g, search, from, at, sprint: false);
                runs.Add($"{(team == 0 ? "B" : "R")}>{name} {(r.Reached ? $"{r.Time:0.0}s" : $"STUCK@{r.End.X:0},{r.End.Y:0},{r.End.Z:0}")}");
                if (r.Length < 0)
                    Fail($"{m.Name}: no route for {(team == 0 ? "Blue" : "Red")} to {name}");
                else if (!r.Reached)
                    Fail($"{m.Name}: a walker from {(team == 0 ? "Blue" : "Red")}'s spawns didn't reach {name} ({r.Length:0} m) in {r.Limit:0} s: stopped at ({r.End.X:0.0}, {r.End.Y:0.0}, {r.End.Z:0.0})");
            }
        }
        ClutteredLine($"{m.Name} scripted walks: {string.Join("  ", runs)}");

        // No line of sight between the two teams' spawns at eye height.
        var seen = new List<string>();
        foreach (var a in o.TeamSpawns[0])
            foreach (var b in o.TeamSpawns[1])
            {
                var eyeA = new Vector3(a.X, terrain.Data.Sample(a.X, a.Z) + 1.6f, a.Z);
                var eyeB = new Vector3(b.X, terrain.Data.Sample(b.X, b.Z) + 1.6f, b.Z);
                var d = eyeB - eyeA;
                var len = d.Length();
                if (World.Query.Raycast(eyeA, d / len, len, QueryFilter.Navigation) >= len - 0.05f)
                    seen.Add($"({a.X:0},{a.Z:0})-({b.X:0},{b.Z:0})");
            }
        if (seen.Count > 0)
            Fail($"{m.Name}: {seen.Count} Blue/Red spawn pair(s) can see each other: {string.Join(", ", seen.Take(4))}");

        // Cover: solid cover at least 0.9 m tall within 6 m of each team's routes to the hills. The beach side gets at least
        // as much as the base side (it has no height and comes uphill).
        var cover = new List<Vector3>();
        foreach (var p in solid.Where(p => p.Bounds.Max.Y - m.GroundHeight(p.Position.X, p.Position.Z) >= 0.9f))
            cover.Add(p.Bounds.Center);
        for (var i = 0; i < m.Solid.Count; i++)
            if (m.IsDrawnBox(i) && m.Solid[i].Max.Y - m.GroundHeight(m.Solid[i].Center.X, m.Solid[i].Center.Z) >= 0.9f)
                cover.Add(m.Solid[i].Center);
        int CoverNear(List<Vector3> route) => cover.Count(q => route.Any(r => (q.X - r.X) * (q.X - r.X) + (q.Z - r.Z) * (q.Z - r.Z) < 36f));
        int coverBlue = CoverNear(routes[0]), coverRed = CoverNear(routes[1]);
        ClutteredLine($"{m.Name} cover within 6 m of the routes to the hills: Blue (beach) {coverBlue}, Red (base) {coverRed}");
        if (coverBlue < coverRed)
            Fail($"{m.Name}: the beach side's routes pass less cover ({coverBlue}) than the base side's ({coverRed})");

        // The ways up the bluff are on the nav graph: inside the drainage tunnel, inside each pillbox, the overlook, the dune post.
        var places = new (string Name, float X, float Z, float Y)[]
        {
            ("tunnel (under the bluff)", 40f, 4f, float.NaN), ("tunnel (under the plateau)", 40f, -1f, float.NaN),
            ("west pillbox", -38f, 12.2f, 9.6f), ("centre pillbox", 24f, 9.5f, 9.6f), ("east pillbox", 52f, 10.5f, 9.6f),
            ("ravine", -62.5f, 10f, float.NaN), ("hairpin", -20f, 15f, float.NaN),
        };
        var missing = new List<string>();
        foreach (var (name, x, z, y0) in places)
        {
            var y = float.IsNaN(y0) ? terrain.Data.Sample(x, z) : y0;
            var found = false;
            for (var i = 0; i < g.Nodes.Length && !found; i++)
            {
                var q = g.Nodes[i];
                found = g.Component[i] == g.MainComponent && MathF.Abs(q.Y - y) < 1.0f && (q.X - x) * (q.X - x) + (q.Z - z) * (q.Z - z) < 9f;
            }
            if (!found)
                missing.Add(name);
        }
        ClutteredLine($"{m.Name} ways up on the nav graph: {places.Length - missing.Count}/{places.Length}{(missing.Count > 0 ? " (missing: " + string.Join(", ", missing) + ")" : "")}");
        if (missing.Count > 0)
            Fail($"{m.Name}: no nav node in {string.Join(", ", missing)}");
    }
}
