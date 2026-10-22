using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The Cluttered pack's map 08, "Truck Stop": a desert highway truck stop at dusk (medium, 12 players), the pack's first
/// map that is NOT mirrored. Blue holds the two-storey motel (balconies, a drained pool: high ground), Red the truck lot
/// (rows of semis and trailers, the tire shop with a reachable roof: cover and flanks). It gets the checks every Cluttered
/// map gets (<see cref="CheckClutteredSmallMap"/>: density quota, everything inside the outline, hand-placed
/// objectives, explosives away from spawns, nav reach, free-for-all and team bot matches at the player limit), plus
/// fairness measured instead of mirrored: the nav walk from each team's spawns to every hill and the enemy flag within
/// 10%, comparable solid cover along each team's routes to the hills, and no line of sight from any Blue spawn to any Red
/// one. It also checks the map's levels: the pool really is a pit, and bots can reach the balconies and the pool floor.
/// </summary>
public static partial class SelfTest
{
    private const string ClutteredTruckStopMap = "Truck Stop";

    static partial void CheckClutteredTruckStop()
    {
        var m = Maps.FindPack(ClutteredPack)?.Maps.FirstOrDefault(x => x.Name == ClutteredTruckStopMap);
        if (m == null)
        {
            Fail($"{ClutteredPack}: no map \"{ClutteredTruckStopMap}\" (08-TruckStop.json)");
            return;
        }
        if (m.SourceFile == null || !Path.GetFileName(m.SourceFile).StartsWith("08-", StringComparison.Ordinal))
            Fail($"{m.Name}: file should be Maps/Cluttered/08-TruckStop.json (is {m.SourceFile})");
        if (m.Boundary is not { } bd || bd.Spec.Shape != BoundaryShape.Smooth || bd.Spec.Noise is not { Amplitude: > 0 })
        {
            Fail($"{m.Name}: boundary should be a noisy smooth outline");
            return;
        }
        if (m.Size != MapSize.Medium || bd.Area is < 7000 or > 9000)
            Fail($"{m.Name}: size class {m.Size}, {bd.Area:0} m2 (expected Medium, 7,000-9,000 m2)");
        if (m.MaxPlayersSetting != 12 || m.MaxPlayers != 12)
            Fail($"{m.Name}: maxPlayers {m.MaxPlayersSetting} in the file, {m.MaxPlayers} in effect (expected 12)");

        // Draw-call side of the counts (boxes are one draw call each, trunks are boxes), the sets the props come from.
        var props = m.PropInstances;
        var drawn = Enumerable.Range(0, m.Solid.Count).Count(m.IsDrawnBox);
        var explosive = props.Count(p => p.Def.Breakable is { ExplodeRadius: > 0 });
        ClutteredLine($"{m.Name}: {drawn} boxes drawn one by one, {m.TrunkCount} trees, {props.Count(p => p.Def.Parts.Any(q => q.Anim != null))} animated props, "
            + $"{explosive} explosive; by set (solid/cosmetic): "
            + string.Join(", ", props.GroupBy(p => p.Def.Category).OrderBy(g => g.Key).Select(g => $"{g.Key} {g.Count(p => p.Def.IsSolid)}/{g.Count(p => !p.Def.IsSolid)}")));
        if (drawn > 400)
            Fail($"{m.Name}: {drawn} boxes drawn one by one (keep it under 400: use props, meshes and foliage)");
        if (explosive < 4)
            Fail($"{m.Name}: {explosive} explosive props (the tire shop and the truck lot should have a few barrels)");

        // The drained pool is a pit in the terrain: shallow end 1 m down, deep end 1.8 m, the deck level round it.
        float shallow = m.GroundHeight(-35f, -11f), deep = m.GroundHeight(-28.5f, -11f), deck = m.GroundHeight(-40f, -11f);
        if (shallow > -0.8f || deep > -1.6f || MathF.Abs(deck) > 0.05f)
            Fail($"{m.Name}: the pool isn't a pit (shallow end {shallow:0.00} m, deep end {deep:0.00} m, deck {deck:0.00} m)");

        CheckClutteredSmallMap(m, new SmallClutterQuota(Props: 2000, SolidProps: 350, CosmeticProps: 1200, Breakable: 400, Plants: 11000, Trees: 12, Boxes: 600));

        // ---- Fairness by measurement (the map isn't mirrored) ----
        var o = m.Objectives;
        var g = ClutteredLargeNav(m);
        var search = new NavSearch();
        var path = new int[4096];
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

        // Cover parity: solid cover at least 0.9 m tall within 6 m of each team's routes to the hills.
        var cover = new List<Vector3>();
        foreach (var p in props.Where(p => p.Def.IsSolid && p.Bounds.Max.Y - m.GroundHeight(p.Position.X, p.Position.Z) >= 0.9f))
            cover.Add(p.Bounds.Center);
        for (var i = 0; i < m.Solid.Count; i++)
            if (m.IsDrawnBox(i) && m.Solid[i].Max.Y - m.GroundHeight(m.Solid[i].Center.X, m.Solid[i].Center.Z) >= 0.9f && m.Solid[i].Min.Y < 2f)
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

        // No Blue spawn sees a Red spawn (eye to eye, through everything that blocks a shot).
        var seen = new List<string>();
        foreach (var a in o.TeamSpawns[0])
            foreach (var b in o.TeamSpawns[1])
            {
                Vector3 ea = a + new Vector3(0, 1.6f, 0), eb = b + new Vector3(0, 1.6f, 0);
                var d = eb - ea;
                var dist = d.Length();
                if (World.Query.Raycast(ea, d / dist, dist, QueryFilter.All) >= dist - 0.05f)
                    seen.Add($"({a.X:0},{a.Z:0})-({b.X:0},{b.Z:0})");
            }
        ClutteredLine($"{m.Name} balance (Blue motel / Red truck lot, nav walk averaged over team spawns): {string.Join(", ", walks)}; "
            + $"cover within 6 m of the routes to the hills {coverBlue}/{coverRed} ({coverRatio:P0}); spawn-to-spawn sight lines {seen.Count}");
        if (coverRatio < 0.7f)
            Fail($"{m.Name}: uneven cover on the routes to the hills (Blue {coverBlue}, Red {coverRed})");
        if (seen.Count > 0)
            Fail($"{m.Name}: {seen.Count} Blue spawn(s) can see a Red spawn: {string.Join(", ", seen.Take(4))}");

        // ---- Levels: bots can reach the balconies and the pool floor (the tire shop roof and trailer tops are reported) ----
        int Reach(Func<Vector3, bool> pick) => Enumerable.Range(0, g.Nodes.Length).Count(i => g.Component[i] == g.MainComponent && pick(g.Nodes[i]));
        var balcony = Reach(q => q.Y is > 3.0f and < 3.9f && ((q.X is > -46f and < -44f && q.Z is > -26f and < 12f) || (q.X is > -46f and < -22f && q.Z is > -26f and < -24f)));
        var upstairs = Reach(q => q.Y is > 3.0f and < 3.9f && q.X is > -53f and < -22f && q.Z is > -33f and < 12f);
        var pool = Reach(q => q.Y < -0.7f && q.X is > -38f and < -27f && q.Z is > -14f and < -8f);
        var roof = Reach(q => q.Y > 4.0f && q.X is > 20f and < 44f && q.Z is > -38f and < -26f);
        var trailerTops = Reach(q => q.Y > 3.5f && q.X > 16f && q.Z is > -24f and < 21f);
        ClutteredLine($"{m.Name} levels (nav nodes in the main area): balconies {balcony}, motel upstairs in all {upstairs}, pool floor {pool}, tire shop roof {roof}, trailer roofs {trailerTops}");
        if (balcony < 6 || pool < 3)
            Fail($"{m.Name}: bots can't get round the levels (balcony nodes {balcony}, pool floor nodes {pool} in the main nav area)");
    }
}
