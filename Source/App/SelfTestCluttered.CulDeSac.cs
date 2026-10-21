using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The Cluttered pack's 04 Cul-de-sac (small, a keyhole: the turning circle plus the street stub, 10 players),
/// mirrored about z = 0. Checks the keyhole shape, the shared small-map checks (density floors, nothing outside the
/// outline, hand-placed objectives, the nav graph, free-for-all and team bot matches), that every solid prop has a
/// twin mirrored across the street's axis, and equal walks for both teams to every hill and the enemy flag.
/// </summary>
public static partial class SelfTest
{
    private const string ClutteredCulDeSacMap = "Cul-de-sac";

    /// <summary>04 Cul-de-sac's checks (called from <see cref="CheckCluttered"/>).</summary>
    static partial void CheckClutteredCulDeSac();

    static partial void CheckClutteredCulDeSac()
    {
        var m = Maps.FindPack(ClutteredPack)?.Maps.FirstOrDefault(x => x.Name == ClutteredCulDeSacMap);
        if (m == null)
        {
            Fail($"{ClutteredPack}: no map \"{ClutteredCulDeSacMap}\" (04-CulDeSac.json)");
            return;
        }
        if (m.SourceFile == null || !Path.GetFileName(m.SourceFile).StartsWith("04-", StringComparison.Ordinal))
            Fail($"{m.Name}: file should be Maps/Cluttered/04-CulDeSac.json (is {m.SourceFile})");
        if (m.Boundary is not { } b || b.Spec.Shape != BoundaryShape.Polygon)
        {
            Fail($"{m.Name}: boundary should be a polygon keyhole (is {(m.Boundary == null ? "none" : m.Boundary.Spec.Shape.ToString())})");
            return;
        }
        // Keyhole: the round part (north and south of the turning circle) and the street stub are inside, the corners
        // beside the stub are outside, and the area is that of a small map.
        var shapeOk = b.Contains(-14, -30) && b.Contains(-14, 30) && b.Contains(-45, 0) && b.Contains(36, 0)
                      && !b.Contains(30, -16) && !b.Contains(30, 16) && !b.Contains(36, -12);
        if (!shapeOk)
            Fail($"{m.Name}: the outline isn't a keyhole (circle plus street stub)");
        if (b.Area is < 3500 or > 4600)
            Fail($"{m.Name}: {b.Area:0} m2 inside the outline (expected 3,500 to 4,600)");
        if (m.Size != MapSize.Small || m.MaxPlayersSetting != 10 || m.MaxPlayers != 10)
            Fail($"{m.Name}: {m.Size} with {m.MaxPlayers} players (expected Small, \"maxPlayers\": 10)");

        // ---- Fairness: every solid prop has a twin across z = 0 (same prop, same x, z negated) ----
        var props = m.PropInstances;
        if (props.Skip(m.ClutterStart).Any(p => p.Def.IsSolid))
            Fail($"{m.Name}: Clutter placed solid props (solid cover is hand-placed in mirrored pairs; Clutter is for litter)");
        var solidIdx = Enumerable.Range(0, m.ClutterStart).Where(i => props[i].Def.IsSolid).ToList();
        var used = new HashSet<int>();
        var unmatched = new List<string>();
        foreach (var i in solidIdx)
        {
            if (used.Contains(i))
                continue;
            var a = props[i];
            if (MathF.Abs(a.Position.Z) < 0.05f)
            {
                used.Add(i);
                continue;
            }
            var twin = solidIdx.FirstOrDefault(j => j != i && !used.Contains(j) && props[j].Def == a.Def
                && MathF.Abs(props[j].Position.X - a.Position.X) < 0.05f && MathF.Abs(props[j].Position.Z + a.Position.Z) < 0.05f
                && MathF.Abs(props[j].Position.Y - a.Position.Y) < 0.05f, -1);
            if (twin < 0)
                unmatched.Add($"{a.Def.Name} ({a.Position.X:0.0},{a.Position.Z:0.0})");
            else
            {
                used.Add(i);
                used.Add(twin);
            }
        }
        if (unmatched.Count > 0)
            Fail($"{m.Name}: {unmatched.Count} solid prop(s) without a twin across the street's axis: {string.Join(", ", unmatched.Take(5))}");

        // ---- The shared small-map checks: density, outline, objectives, nav, bot matches ----
        CheckClutteredSmallMap(m, new SmallClutterQuota(Props: 1500, SolidProps: 220, CosmeticProps: 1200, Breakable: 800, Plants: 12500, Trees: 20, Boxes: 500));

        // ---- Equal walks for both teams (the nav graph is built by the shared checks above) ----
        World.SetMap(m);
        for (var wait = 0; wait < 2000 && NavGraph.Current == NavGraph.Empty; wait++)
            NavGraph.Pump(50);
        var g = NavGraph.Current;
        var o = m.Objectives;
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
        float TeamWalk(int team, Vector3 to) => o.TeamSpawns[team].Average(s => Walk(s, to));
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
        ClutteredLine($"  {m.Name} walks blue/red: {string.Join(", ", walks)}; {solidIdx.Count} hand-placed solid props, {unmatched.Count} without a twin");
    }
}
