using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The Cluttered pack's map 10, "Timberline": a forest with log cabins all over and two bunkers hidden underground
/// (large, 16 players). It gets the checks every Cluttered map gets (<see cref="CheckClutteredSmallMap"/>: density quota,
/// everything inside the outline, hand-placed objectives, nav reach, free-for-all and team bot matches at the player
/// limit), plus its own. The map is deliberately not mirrored (Blue's lake and ranger station low in the west, Red's
/// logging camp up the hill in the east), so fairness is measured instead of checked by twins:
/// <list type="bullet">
/// <item>the nav walk (averaged over each team's spawns) to every hill and to the enemy flag is within 10%;</item>
/// <item>no Blue spawn sees a Red spawn (eye to eye, through the world's ray casts: terrain, boxes, props, trunks);</item>
/// <item>comparable cover: solid cover 0.9 m or taller within 6 m of each team's routes to the hills (at least 70%);</item>
/// <item>the high ground is Red's (the camp stands higher), so Blue must get at least as much cover as Red.</item>
/// </list>
/// The bunkers: each has a floor the nav graph reaches from the main area, a roof over it (no sky inside), and two ways
/// in that the nav graph walks: down the stairs from the hatch in its cabin's floor, and along the trench to its door.
/// </summary>
public static partial class SelfTest
{
    private const string ClutteredTimberlineMap = "Timberline";

    /// <summary>
    /// Bunker A in the generator's plan (tools/cluttered/timberline.py, BUNKER_PLAN): the main room's centre, the landing
    /// at the top of the stairs (on the hatch cabin's floor) and the far end of the trench. Bunker B is the same plan
    /// turned 180 degrees about the map centre.
    /// </summary>
    private static readonly (Vector2 Room, Vector2 Landing, Vector2 Trench)[] TimberlineBunkers =
    [
        (new(-39.0f, -29.5f), new(-52.9f, -30.4f), new(-8.0f, -28.8f)),
        (new(39.0f, 29.5f), new(52.9f, 30.4f), new(8.0f, 28.8f)),
    ];

    static partial void CheckClutteredTimberline()
    {
        var m = Maps.FindPack(ClutteredPack)?.Maps.FirstOrDefault(x => x.Name == ClutteredTimberlineMap);
        if (m == null)
        {
            Fail($"{ClutteredPack}: no map \"{ClutteredTimberlineMap}\" (10-Timberline.json)");
            return;
        }
        if (m.SourceFile == null || !Path.GetFileName(m.SourceFile).StartsWith("10-", StringComparison.Ordinal))
            Fail($"{m.Name}: file should be Maps/Cluttered/10-Timberline.json (is {m.SourceFile})");
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

        // Draw-call side of the counts: boxes and trunks are one draw call each (the cabins are kit props: instanced).
        var drawn = Enumerable.Range(0, m.Solid.Count).Count(m.IsDrawnBox);
        var trees = m.FoliageInstances.Count(f => Foliage.IsTree(f.Type));
        var bySet = string.Join(", ", m.PropInstances.GroupBy(p => p.Def.Category).OrderByDescending(g => g.Count())
            .Select(g => $"{g.Key} {g.Count(p => p.Def.IsSolid)}/{g.Count(p => !p.Def.IsSolid)}"));
        ClutteredLine($"{m.Name}: {drawn} boxes drawn one by one ({trees} of them trunks), {m.PropInstances.Count(p => p.Def.Parts.Any(q => q.Anim != null))} animated props, "
            + $"{m.PropInstances.Count(p => p.Def.Breakable is { ExplodeRadius: > 0 })} explosive; props by set (solid/cosmetic): {bySet}");
        if (drawn > 800)
            Fail($"{m.Name}: {drawn} boxes drawn one by one (keep it under 800: use props, meshes and foliage)");
        if (trees is < 150 or > 600)
            Fail($"{m.Name}: {trees} trees (expected 150-600: a forest, but every trunk is a box)");

        CheckClutteredSmallMap(m, new SmallClutterQuota(Props: 2800, SolidProps: 450, CosmeticProps: 1800, Breakable: 100, Plants: 30000, Trees: 150, Boxes: 700));

        var o = m.Objectives;
        var g = ClutteredLargeNav(m);
        var search = new NavSearch();
        var path = new int[2048];

        // ---- Fairness by route: the nav walk (averaged over each team's spawns) to every hill and the enemy flag.
        var routeNodes = new[] { new List<Vector3>(), new List<Vector3>() };
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

        // ---- No spawn-to-spawn sight line (eye to eye; terrain, boxes, props and trunks block).
        var seen = new List<string>();
        foreach (var a in o.TeamSpawns[0])
            foreach (var b in o.TeamSpawns[1])
            {
                var ea = a + new Vector3(0, World.EyeHeight, 0);
                var eb = b + new Vector3(0, World.EyeHeight, 0);
                var d = eb - ea;
                var len = d.Length();
                if (World.RaycastWorld(ea, d / len, len) >= len - 0.05f)
                    seen.Add($"({a.X:0},{a.Z:0})-({b.X:0},{b.Z:0})");
            }
        if (seen.Count > 0)
            Fail($"{m.Name}: {seen.Count} Blue spawn(s) see a Red spawn: {string.Join(", ", seen.Take(4))}");

        // ---- Comparable cover: solid cover 0.9 m or taller within 6 m of each team's routes to the hills. Red has the
        // high ground, so Blue gets at least as much cover (within 70% the other way for Red).
        var cover = new List<Vector3>();
        foreach (var p in m.PropInstances.Where(p => p.Def.IsSolid && p.Bounds.Max.Y - m.GroundHeight(p.Position.X, p.Position.Z) >= 0.9f))
            cover.Add(p.Bounds.Center);
        for (var i = 0; i < m.Solid.Count; i++)
            if (m.IsDrawnBox(i) && m.Solid[i].Max.Y - m.GroundHeight(m.Solid[i].Center.X, m.Solid[i].Center.Z) >= 0.9f && m.Solid[i].Max.Y - m.Solid[i].Min.Y < 6f)
                cover.Add(m.Solid[i].Center);
        int CoverNear(List<Vector3> route)
        {
            var c = 0;
            foreach (var q in cover)
                foreach (var r in route)
                    if ((q.X - r.X) * (q.X - r.X) + (q.Z - r.Z) * (q.Z - r.Z) < 36f && MathF.Abs(q.Y - r.Y) < 4f)
                    {
                        c++;
                        break;
                    }
            return c;
        }
        int coverBlue = CoverNear(routeNodes[0]), coverRed = CoverNear(routeNodes[1]);
        float Height(int team) => o.TeamSpawns[team].Count == 0 ? 0 : o.TeamSpawns[team].Average(s => m.GroundHeight(s.X, s.Z));
        float blueY = Height(0), redY = Height(1);
        ClutteredLine($"{m.Name} balance (Blue/Red nav walk): {string.Join(", ", walks)}; spawn-to-spawn sight lines {seen.Count}; cover near the routes to the hills "
            + $"{coverBlue}/{coverRed}; spawn ground {blueY:0.0}/{redY:0.0} m (Red holds the high ground)");
        if (coverRed < 0.7f * coverBlue || (redY > blueY + 2f && coverBlue < coverRed))
            Fail($"{m.Name}: cover on the routes to the hills is out of balance (Blue {coverBlue}, Red {coverRed}; Red's spawns are {redY - blueY:0.0} m higher)");

        // ---- The bunkers: a covered floor the nav graph reaches, and both ways in walkable.
        int NodeNear(Vector2 at, float y, float reach)
        {
            var best = -1;
            var bestD = reach * reach;
            for (var i = 0; i < g.Nodes.Length; i++)
            {
                var q = g.Nodes[i];
                var d2 = (q.X - at.X) * (q.X - at.X) + (q.Z - at.Y) * (q.Z - at.Y);
                if (g.Component[i] == g.MainComponent && MathF.Abs(q.Y - y) < 0.7f && d2 < bestD)
                {
                    best = i;
                    bestD = d2;
                }
            }
            return best;
        }
        float RouteLength(int a, int b)
        {
            var count = search.Find(g, a, b, path, maxExpand: 30000);
            if (count == 0)
                return float.PositiveInfinity;
            var len = 0f;
            for (var k = 1; k < count; k++)
                len += Vector3.Distance(g.Nodes[path[k - 1]], g.Nodes[path[k]]);
            return len;
        }
        var bunkerNotes = new List<string>();
        foreach (var (roomAt, landingAt, trenchAt) in TimberlineBunkers)
        {
            var floor = m.GroundHeight(roomAt.X, roomAt.Y); // the pit's floor (the floor slab sits 3 cm on it)
            var surface = m.GroundHeight(landingAt.X, landingAt.Y); // the ground beside the stairs' top: the cabin floor is on the roof
            var up = World.RaycastWorld(new Vector3(roomAt.X, floor + 1f, roomAt.Y), Vector3.UnitY, 20f);
            if (up > 4f)
                Fail($"{m.Name}: the bunker room at ({roomAt.X:0},{roomAt.Y:0}) has no roof over it (open {up:0.0} m up)");
            if (surface - floor < 2.8f)
                Fail($"{m.Name}: the bunker at ({roomAt.X:0},{roomAt.Y:0}) isn't underground (floor {floor:0.00}, surface {surface:0.00})");
            int room = NodeNear(roomAt, floor + 0.03f, 5f), landing = NodeNear(landingAt, surface + 0.06f, 2.5f);
            int trench = NodeNear(trenchAt, m.GroundHeight(trenchAt.X, trenchAt.Y), 4f);
            if (room < 0 || landing < 0 || trench < 0)
            {
                Fail($"{m.Name}: the nav graph's main area doesn't reach the bunker at ({roomAt.X:0},{roomAt.Y:0}) (room {room >= 0}, hatch landing {landing >= 0}, trench {trench >= 0})");
                continue;
            }
            float viaHatch = RouteLength(landing, room), viaTrench = RouteLength(trench, room);
            bunkerNotes.Add($"({roomAt.X:0},{roomAt.Y:0}) floor {floor:0.0} m under {surface - floor:0.0} m of ground, hatch {viaHatch:0} m, trench {viaTrench:0} m");
            if (!float.IsFinite(viaHatch) || viaHatch > 45f || !float.IsFinite(viaTrench) || viaTrench > 60f)
                Fail($"{m.Name}: the bunker at ({roomAt.X:0},{roomAt.Y:0}) needs two ways in (hatch {viaHatch:0} m, trench {viaTrench:0} m on the nav graph)");
        }
        ClutteredLine($"{m.Name} bunkers: {string.Join("; ", bunkerNotes)}");
    }
}
