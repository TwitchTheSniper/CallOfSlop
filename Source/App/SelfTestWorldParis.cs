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
        CheckCaveOpenings(m, fail: true);
        foreach (var other in Maps.All.Where(x => x != m && x.Terrain is { HasVoids: true }))
            CheckCaveOpenings(other, fail: false);
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

    /// <summary>
    /// Cave mouths and tunnels are really open: no map box or mesh lies over a terrain hole cell between its floor and
    /// head height above the surface (a slab or paving piece over a metro stairwell would hide the opening and leave
    /// players and bots walking through it), and no box stands in the walking column along a cave's centre line. Paris
    /// Quarter fails on any; other maps with caves only report theirs (<paramref name="fail"/> false).
    /// </summary>
    private static void CheckCaveOpenings(Map m, bool fail)
    {
        if (m.Terrain is not { Voids: { } v } t)
            return;
        var cx = t.CountX - 1;
        var covered = new List<string>();
        var openCells = 0;
        for (var j = 0; j < t.CountZ - 1; j++)
        for (var i = 0; i < cx; i++)
        {
            if (v.Kinds[j * cx + i] != TerrainVoids.Open)
                continue;
            openCells++;
            int k = j * t.CountX + i;
            var floor = MathF.Min(MathF.Min(v.Floor[k], v.Floor[k + 1]), MathF.Min(v.Floor[k + t.CountX], v.Floor[k + t.CountX + 1]));
            var top = MathF.Max(MathF.Max(t.Heights[k], t.Heights[k + 1]), MathF.Max(t.Heights[k + t.CountX], t.Heights[k + t.CountX + 1]));
            float x0 = t.OriginX + i * t.Cell + 0.05f, z0 = t.OriginZ + j * t.Cell + 0.05f, x1 = x0 + t.Cell - 0.1f, z1 = z0 + t.Cell - 0.1f;
            float y0 = floor + 0.15f, y1 = top + 2.2f;
            foreach (var b in m.Solid)
            {
                var lo = b.Center - b.Half;
                var hi = b.Center + b.Half;
                if (hi.X > x0 && lo.X < x1 && hi.Z > z0 && lo.Z < z1 && hi.Y > y0 && lo.Y < y1)
                {
                    covered.Add($"box ({lo.X:0.0}..{hi.X:0.0}, {lo.Y:0.0}..{hi.Y:0.0}, {lo.Z:0.0}..{hi.Z:0.0}) over the hole at ({x0:0.0}, {z0:0.0})");
                    break;
                }
            }
            foreach (var mp in m.MeshPlacements)
            {
                var bb = mp.Bounds;
                // a mesh lying on the floor (stair treads) is fine; one hanging over it (paving at the surface) hides the opening
                if (bb.Max.X > x0 && bb.Min.X < x1 && bb.Max.Z > z0 && bb.Min.Z < z1 && bb.Min.Y > floor + 0.6f && bb.Min.Y < y1)
                {
                    covered.Add($"mesh {mp.Source} ({bb.Min.X:0.0}..{bb.Max.X:0.0}, {bb.Min.Y:0.0}..{bb.Max.Y:0.0}, {bb.Min.Z:0.0}..{bb.Max.Z:0.0}) over the hole at ({x0:0.0}, {z0:0.0})");
                    break;
                }
            }
        }
        var blockedPath = new List<string>();
        foreach (var p in v.NavPoints)
        {
            float x0 = p.X - 0.35f, x1 = p.X + 0.35f, z0 = p.Z - 0.35f, z1 = p.Z + 0.35f, y0 = p.Y + 0.4f, y1 = p.Y + 1.6f;
            foreach (var b in m.Solid)
            {
                var lo = b.Center - b.Half;
                var hi = b.Center + b.Half;
                if (hi.X > x0 && lo.X < x1 && hi.Z > z0 && lo.Z < z1 && hi.Y > y0 && lo.Y < y1)
                {
                    blockedPath.Add($"box ({lo.X:0.0}..{hi.X:0.0}, {lo.Y:0.0}..{hi.Y:0.0}, {lo.Z:0.0}..{hi.Z:0.0}) in the tunnel at ({p.X:0.0}, {p.Y:0.0}, {p.Z:0.0})");
                    break;
                }
            }
        }
        var distinct = covered.Distinct().ToList();
        Line($"cave openings on {m.Name}: {openCells} open cells, {covered.Count} covered by a box or mesh, {v.NavPoints.Length} tunnel points, {blockedPath.Count} blocked"
             + (distinct.Count > 0 ? "; e.g. " + string.Join("; ", distinct.Take(3)) : "") + (blockedPath.Count > 0 ? "; " + string.Join("; ", blockedPath.Take(3)) : ""));
        if (!fail)
            return;
        foreach (var c in distinct.Take(6))
            Fail($"{m.Name}: cave opening covered: {c}");
        foreach (var c in blockedPath.Take(6))
            Fail($"{m.Name}: cave path blocked: {c}");
    }
}
