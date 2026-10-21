using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The Cluttered pack's map 06, "Old Town": an overgrown small-town centre on a noisy smooth outline (large, 16 players).
/// It gets the checks every Cluttered map gets (<see cref="CheckClutteredSmallMap"/>: density quota, everything inside
/// the outline, hand-placed objectives, nav reach, free-for-all and team bot matches at the player limit), plus its own:
/// the outline, the size class, the draw-call side of the counts (boxes drawn one by one, trees), and fairness by route
/// (the nav walk from each team's spawns to every hill and the enemy flag within 10%).
/// </summary>
public static partial class SelfTest
{
    private const string ClutteredOldTownMap = "Old Town";

    static partial void CheckClutteredOldTown()
    {
        var m = Maps.FindPack(ClutteredPack)?.Maps.FirstOrDefault(x => x.Name == ClutteredOldTownMap);
        if (m == null)
        {
            Fail($"{ClutteredPack}: no map \"{ClutteredOldTownMap}\" (06-OldTown.json)");
            return;
        }
        if (m.SourceFile == null || !Path.GetFileName(m.SourceFile).StartsWith("06-", StringComparison.Ordinal))
            Fail($"{m.Name}: file should be Maps/Cluttered/06-OldTown.json (is {m.SourceFile})");
        if (m.Boundary is not { } bd || bd.Spec.Shape != BoundaryShape.Smooth || bd.Spec.Noise is not { Amplitude: > 0 })
        {
            Fail($"{m.Name}: boundary should be a noisy smooth outline");
            return;
        }
        if (m.Size != MapSize.Large || bd.Area is < 18225 or > 20500)
            Fail($"{m.Name}: size class {m.Size}, {bd.Area:0} m2 (expected Large, 18,225-20,500 m2)");
        if (m.MaxPlayersSetting != 16 || m.MaxPlayers != 16)
            Fail($"{m.Name}: maxPlayers {m.MaxPlayersSetting} in the file, {m.MaxPlayers} in effect (expected 16)");

        // Draw-call side of the counts: boxes are one draw call each, trunks are boxes.
        var drawn = Enumerable.Range(0, m.Solid.Count).Count(m.IsDrawnBox);
        var trees = m.FoliageInstances.Count(f => Foliage.IsTree(f.Type));
        ClutteredLine($"{m.Name}: {drawn} boxes drawn one by one, {trees} trees, {m.PropInstances.Count(p => p.Def.Parts.Any(q => q.Anim != null))} animated props");
        if (drawn > 700)
            Fail($"{m.Name}: {drawn} boxes drawn one by one (keep it under 700: use props, meshes and foliage)");
        if (trees is < 80 or > 400)
            Fail($"{m.Name}: {trees} trees (expected 80-400)");

        CheckClutteredSmallMap(m, new SmallClutterQuota(Props: 4000, SolidProps: 550, CosmeticProps: 3000, Breakable: 1200, Plants: 30000, Trees: 80, Boxes: 900));

        // Fairness by route: the nav walk (averaged over each team's spawns) to every hill and to the enemy flag.
        var o = m.Objectives;
        var g = ClutteredLargeNav(m);
        var search = new NavSearch();
        var path = new int[2048];
        float Walk(int team, Vector3 goal)
        {
            var total = 0f;
            var gi = g.Nearest(goal, needSight: false);
            foreach (var s in o.TeamSpawns[team])
            {
                var count = search.Find(g, g.Nearest(s, needSight: false), gi, path, maxExpand: 20000);
                if (count == 0)
                    return float.PositiveInfinity;
                var len = Vector3.Distance(s, g.Nodes[path[0]]) + Vector3.Distance(g.Nodes[path[count - 1]], goal);
                for (var k = 1; k < count; k++)
                    len += Vector3.Distance(g.Nodes[path[k - 1]], g.Nodes[path[k]]);
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
            Check($"hill {i + 1}", Walk(0, o.Hills[i].Center), Walk(1, o.Hills[i].Center));
        Check("enemy flag", Walk(0, o.Flags[1]), Walk(1, o.Flags[0]));
        ClutteredLine($"{m.Name} balance (Blue/Red nav walk): {string.Join(", ", walks)}");
    }
}
