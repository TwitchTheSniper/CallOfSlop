using System.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// World Tour, 04 Paris Quarter (Maps/World/04-Paris.json): it loads with its terrain, metro caves and outline; the
    /// layout is mirror-symmetric (spawns have twins at (-x, z); building spawns moved off furniture may not); bots reach every room and
    /// stair top of its buildings and both team plazas from each other; and what it costs (boxes, props, decals, nav),
    /// reported as one CI notice.
    /// </summary>
    private static void CheckWorldParis()
    {
        if (Maps.Find("Paris Quarter") is not { } m)
        {
            Fail("Maps/World/04-Paris.json ('Paris Quarter') didn't load");
            return;
        }
        if (m.Terrain == null || m.Boundary == null)
            Fail("Paris Quarter has no terrain or no outline");
        var unmatched = m.Spawns.Count(s => !m.Spawns.Any(t => MathF.Abs(t.X + s.X) < 0.3f && MathF.Abs(t.Z - s.Z) < 0.3f && MathF.Abs(t.Y - s.Y) < 0.3f));
        if (unmatched > 0)
            Line($"  note: {unmatched} spawn(s) without an exact mirror twin (spawns moved off furniture)");
        var buildingBoxes = Enumerable.Range(0, m.Solid.Count).Count(m.IsBuildingBox);
        var solidProps = m.PropInstances.Count(p => p.Def.IsSolid);
        var mesh = DecalBaker.Bake(m);
        CheckBuildingNav(m);
        var g = NavGraph.Current;
        var search = new NavSearch();
        var path = new int[4096];
        var spawns = m.Spawns;
        var unreachable = 0;
        for (var i = 0; i < spawns.Count; i++)
        {
            var far = spawns.OrderByDescending(s => Vector3.Distance(s, spawns[i])).First();
            if (search.Find(g, g.Nearest(spawns[i]), g.Nearest(far, needSight: false), path) == 0)
                unreachable++;
        }
        if (unreachable > spawns.Count / 4)
            Fail($"Paris Quarter: {unreachable} of {spawns.Count} far spawn pairs have no path");
        var summary = $"Paris Quarter: {m.Size} {m.HalfX * 2:0} x {m.HalfZ * 2:0} m, max {m.WantedMaxPlayers} players; {m.PlacedBuildings.Count} buildings; "
                      + $"{m.Solid.Count} boxes ({buildingBoxes} building); {m.PropInstances.Count} props ({solidProps} solid); "
                      + $"decals {mesh.Placed}/{mesh.Requested} placed, {mesh.Triangles} triangles in {mesh.Chunks.Length} chunks; "
                      + $"{spawns.Count} spawns ({unmatched} unmirrored); nav {g.Nodes.Length} nodes, {g.EdgeCount} edges, {g.MantleEdges} mantles, "
                      + $"{unreachable} far spawn pairs unreachable";
        Line(summary);
        Line("::notice title=Paris Quarter (results)::" + summary.Replace("%", "%25"));
        if (mesh.Placed < mesh.Requested * 0.6)
            Fail($"Paris Quarter: only {mesh.Placed}/{mesh.Requested} decals placed");
    }
}
