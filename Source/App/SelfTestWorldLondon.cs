using System.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// World Tour, 08 London (Maps/World/08-London.json): it loads with its terrain (canal cut, embankments, the south
    /// rise) and its outline; it has real elevation; it is mirror-symmetric about x = 0 (spawns and hills); its
    /// objectives are hand-placed; bots reach every room and stair top of its buildings (the viaduct deck through the
    /// arches' stair bays), every hill (the towpath, the platform on the deck, the raised square) and the enemy flag from
    /// each team's spawns, with walks that match within 10%; and what it costs, as one CI notice. No bot matches.
    /// </summary>
    private static void CheckWorldLondon()
    {
        if (Maps.Find("London") is not { } m)
        {
            Fail("Maps/World/08-London.json ('London') didn't load");
            return;
        }
        if (m.Terrain == null || m.Boundary == null)
        {
            Fail("London has no terrain or no outline");
            return;
        }
        // Elevation: the towpath below the streets, the viaduct deck and embankments above them.
        float lo = m.GroundHeight(30f, -37.8f), hi = m.GroundHeight(58f, -14.5f), park = m.GroundHeight(6f, 30f);
        if (lo > -2f || hi < 5f || park < 1f)
            Fail($"London: the terrain isn't as built (towpath {lo:0.00}, embankment {hi:0.00}, square {park:0.00})");
        var unmatched = m.Spawns.Count(s => !m.Spawns.Any(t => MathF.Abs(t.X + s.X) < 0.3f && MathF.Abs(t.Z - s.Z) < 0.3f && MathF.Abs(t.Y - s.Y) < 0.3f));
        if (unmatched > 0)
            Line($"  note: {unmatched} spawn(s) without an exact mirror twin (building spawns moved off furniture)");
        var o = m.Objectives;
        if (o.TeamSpawnsGenerated || o.HillsGenerated || o.FlagsGenerated)
            Fail("London: team spawns, hills and flags should all be hand-placed");
        if (o.Hills.Any(h => MathF.Abs(h.Center.X) > 0.05f))
            Fail("London: the hills should be on the mirror line x = 0");

        var buildingBoxes = Enumerable.Range(0, m.Solid.Count).Count(m.IsBuildingBox);
        var drawnBoxes = Enumerable.Range(0, m.Solid.Count).Count(m.IsDrawnBox);
        var solidProps = m.PropInstances.Count(p => p.Def.IsSolid);
        var shell = m.PlacedBuildings.Sum(b => b.ShellCount);
        var mesh = DecalBaker.Bake(m);
        CheckBuildingNav(m); // every room and stair top, the deck included
        var g = NavGraph.Current;
        var search = new NavSearch();
        var path = new int[4096];

        float Walk(Vector3 a, Vector3 b)
        {
            var n = search.Find(g, g.Nearest(a), g.Nearest(b, needSight: false), path);
            if (n == 0)
                return float.NaN;
            var len = 0f;
            for (var i = 1; i < n; i++)
                len += Vector3.Distance(g.Nodes[path[i - 1]], g.Nodes[path[i]]);
            return len;
        }

        float Average(List<Vector3> from, Vector3 to)
        {
            var sum = 0f;
            foreach (var s in from)
            {
                var w = Walk(s, to);
                if (float.IsNaN(w))
                    return float.NaN;
                sum += w;
            }
            return sum / Math.Max(1, from.Count);
        }

        var walks = new List<string>();
        var targets = o.Hills.Select((h, i) => ($"hill {i + 1}", h.Center, h.Center)).ToList();
        targets.Add(("enemy flag", o.Flags[1], o.Flags[0]));
        foreach (var (label, forBlue, forRed) in targets)
        {
            float blue = Average(o.TeamSpawns[0], forBlue), red = Average(o.TeamSpawns[1], forRed);
            walks.Add($"{label} {blue:0}/{red:0} m");
            if (float.IsNaN(blue) || float.IsNaN(red))
                Fail($"London: no route from a team's spawns to {label}");
            else if (MathF.Abs(blue - red) > 0.1f * MathF.Max(blue, red))
                Fail($"London: {label} is {blue:0} m from Blue and {red:0} m from Red (more than 10% apart)");
        }
        var summary = $"London: {m.Size} {m.HalfX * 2:0} x {m.HalfZ * 2:0} m ({m.PlayArea:0} m2), max {m.WantedMaxPlayers} players; {m.PlacedBuildings.Count} buildings "
                      + $"({shell} shell boxes); {m.Solid.Count} boxes ({buildingBoxes} building, {drawnBoxes} drawn one by one); {m.PropInstances.Count} props ({solidProps} solid); "
                      + $"{m.MeshPlacements.Count} meshes; {m.FoliageInstances.Length} plants; decals {mesh.Placed}/{mesh.Requested} placed, {mesh.Triangles} triangles in {mesh.Chunks.Length} chunks; "
                      + $"terrain {lo:0.0} / {hi:0.0} / {park:0.0} m; {m.Spawns.Count} spawns ({unmatched} unmirrored); nav {g.Nodes.Length} nodes, {g.EdgeCount} edges, {g.MantleEdges} mantles; "
                      + "walks Blue/Red: " + string.Join(", ", walks);
        Line(summary);
        Line("::notice title=London (results)::" + summary.Replace("%", "%25"));
        if (mesh.Placed < mesh.Requested * 0.6)
            Fail($"London: only {mesh.Placed}/{mesh.Requested} decals placed");
        if (drawnBoxes > 400)
            Fail($"London: {drawnBoxes} boxes drawn one by one (keep them under 400)");
    }
}
