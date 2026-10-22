using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The World Tour pack's map 03, "Silo" (Maps/World/03-Silo.json, written by tools/world/silo.py): forested hills over a
/// missile complex built from terrain caves and holes, with buildings standing in cave chambers at an absolute height.
/// Checks the map's shape and size, what it costs (boxes drawn one by one, trees, props, meshes, decals), that the buried
/// buildings keep plants out of their rooms but let them grow on the ground above, the tunnels (a cave floor at the
/// right height, a nav node, no plants), the silo's levels, and fairness on the bots' nav graph: every room of every
/// building reachable, and each team's walk to every hill and to the enemy flag within 8% of the other's (the map is
/// mirrored east to west, so they should be nearly equal). Nav-graph searches only: no bot matches beyond the usual one.
/// </summary>
public static partial class SelfTest
{
    private const string WorldSiloMap = "Silo";

    /// <summary>Points on tunnel floors (x, z, floor height) on the west side; each is checked mirrored too.</summary>
    private static readonly (string Name, float X, float Z, float Floor)[] SiloTunnelProbes =
    [
        ("portal", -56f, 0f, -4f), ("cableway west", -32f, 0f, -4f), ("cableway east", -18f, 0f, -4f),
        ("junction to crew", -27.2f, -12f, -4f), ("crew to control", -17.8f, -35f, -4f), ("junction to armoury", -27.6f, 8f, -4f),
        ("armoury to plant", -20f, 29.5f, -8f), ("control to silo", 0f, -32f, -4f), ("plant to silo", 0f, 25f, -13f),
    ];

    private static void CheckWorldSilo()
    {
        if (!Directory.Exists(Maps.Folder))
            return;
        var m = Maps.All.FirstOrDefault(x => x.Name == WorldSiloMap);
        if (m == null)
        {
            Fail($"World Tour: no map \"{WorldSiloMap}\" (Maps/World/03-Silo.json)");
            return;
        }
        if (m.Pack?.Name != "World Tour" || m.SourceFile == null || !Path.GetFileName(m.SourceFile).StartsWith("03-", StringComparison.Ordinal))
            Fail($"{m.Name}: should be Maps/World/03-Silo.json in the World Tour pack (is {m.SourceFile} in {m.Pack?.Name})");
        if (m.Size != MapSize.Large || m.MaxPlayers != 24)
            Fail($"{m.Name}: size {m.Size}, {m.MaxPlayers} players (expected Large, 24)");
        if (m.Boundary is not { } bd || bd.Spec.Shape != BoundaryShape.Smooth || bd.Spec.Noise is not { Amplitude: > 0 })
            Fail($"{m.Name}: the outline should be a noisy smooth shape (no rectangle)");
        if (m.Terrain?.Voids is not { } voids)
        {
            Fail($"{m.Name}: no terrain caves and holes");
            return;
        }
        var terrain = m.Terrain!;
        if (m.Movers.Count != 2)
            Fail($"{m.Name}: {m.Movers.Count} movers (expected the two silo lifts)");

        // Costs.
        var drawn = Enumerable.Range(0, m.Solid.Count).Count(m.IsDrawnBox);
        var props = m.PropInstances;
        var shell = m.PlacedBuildings.Sum(b => b.ShellCount);
        Line($"{m.Name}: {drawn} boxes drawn one by one ({m.TrunkCount} tree trunks), {m.PlacedBuildings.Count} buildings ({shell} instanced shell boxes, "
             + $"{m.BuildingDoors.Count} doors), {props.Count} props ({props.Count(p => p.Def.IsSolid)} solid, {props.Count(p => p.IsBreakable)} breakable), "
             + $"{m.FoliageInstances.Length} plants, {m.MeshPlacements.Count} meshes, {m.DecalCount} decals, {voids.OpenCells} hole cells, {voids.CaveCells} cave cells");
        if (drawn > 650)
            Fail($"{m.Name}: {drawn} boxes drawn one by one (keep it under 650: props, meshes, buildings and foliage are instanced)");
        if (m.TrunkCount is < 150 or > 500)
            Fail($"{m.Name}: {m.TrunkCount} trees (expected a forest: 150-500)");
        if (m.PlacedBuildings.Count < 20)
            Fail($"{m.Name}: only {m.PlacedBuildings.Count} buildings");

        // Buried buildings: nothing grows in their rooms, but the forest floor above them is planted.
        int buried = 0, above = 0, inside = 0;
        foreach (var b in m.PlacedBuildings)
        {
            var fp = b.Plan.Footprint.Aggregate((a, c) => a.Union(c));
            var (cx, cz) = ((fp.X0 + fp.X1) / 2, (fp.Z0 + fp.Z1) / 2);
            if (b.Top > m.GroundHeight(cx, cz) - 0.5f)
                continue;
            buried++;
            foreach (var f in m.FoliageInstances)
            {
                if (!fp.Contains(f.Position.X, f.Position.Z, 0f))
                    continue;
                if (f.Position.Y > b.Top + 0.5f)
                    above++;
                else
                    inside++;
            }
        }
        Line($"{m.Name}: {buried} buried buildings, {above} plants on the ground over them, {inside} under it");
        if (buried < 11)
            Fail($"{m.Name}: {buried} buildings under the ground (expected the 11 rooms of the complex)");
        if (inside > 0)
            Fail($"{m.Name}: {inside} plants inside the buried buildings");
        if (above < 200)
            Fail($"{m.Name}: only {above} plants on the ground over the buried buildings (foliage must grow over a building under the terrain)");

        var g = ClutteredLargeNav(m);
        bool NodeNear(Vector3 p, float up = 1.2f, float across = 3f)
        {
            for (var i = 0; i < g.Nodes.Length; i++)
            {
                var q = g.Nodes[i];
                if (g.Component[i] == g.MainComponent && MathF.Abs(q.Y - p.Y) < up && (q.X - p.X) * (q.X - p.X) + (q.Z - p.Z) * (q.Z - p.Z) < across * across)
                    return true;
            }
            return false;
        }

        // Tunnels: a roofed cave floor at the right height, a bot nav node on it, no plants.
        var notes = new List<string>();
        foreach (var (name, x0, z, floor) in SiloTunnelProbes)
            foreach (var x in x0 == 0 ? new[] { x0 } : new[] { x0, -x0 })
            {
                var gy = m.GroundHeight(x, z, floor + 1f);
                var kind = terrain.KindAt(x, z);
                var plants = m.FoliageInstances.Count(f => MathF.Abs(f.Position.X - x) < 2f && MathF.Abs(f.Position.Z - z) < 2f && f.Position.Y < floor + 3f);
                notes.Add($"{name}{(x > 0 ? " (east)" : "")} {gy:0.0}");
                if (kind != TerrainVoids.Cave || MathF.Abs(gy - floor) > 0.35f)
                    Fail($"{m.Name}: tunnel \"{name}\" at ({x:0.0}, {z:0.0}): cell kind {kind}, floor {gy:0.00} m (expected a roofed cave, floor {floor:0.00} m)");
                if (!NodeNear(new Vector3(x, floor, z)))
                    Fail($"{m.Name}: tunnel \"{name}\" at ({x:0.0}, {z:0.0}): no nav node on the floor in the main area");
                if (plants > 0)
                    Fail($"{m.Name}: tunnel \"{name}\" at ({x:0.0}, {z:0.0}): {plants} plant(s) growing in the tunnel");
            }
        Line($"{m.Name} tunnels: {string.Join(", ", notes)}");

        // The silo's levels: the floor, the service-arm platform round the missile, the top gantry ring.
        foreach (var (name, p) in new[] { ("silo floor", new Vector3(0f, -22f, 7.5f)), ("service arms", new Vector3(-4f, -12.9f, -2.4f)),
                                         ("gantry ring", new Vector3(0f, -3.9f, -10.5f)), ("tower roof", new Vector3(-8f, -3.9f, 2.6f)) })
            if (!NodeNear(p, 0.6f, 2.5f))
                Fail($"{m.Name}: no nav node on the {name} at ({p.X:0.0}, {p.Y:0.0}, {p.Z:0.0}) in the main area");

        // Every room of every building, from its front door; and every building's front door from Blue's spawns.
        var o = m.Objectives;
        var search = new NavSearch();
        var path = new int[4096];
        var blueStart = g.Nearest(o.TeamSpawns[0][0], needSight: false);
        int rooms = 0, reached = 0;
        var missing = new List<string>();
        foreach (var b in m.PlacedBuildings)
            foreach (var r in b.Plan.Rooms)
            {
                rooms++;
                var k = g.Nearest(r.Waypoint, needSight: false);
                if (k >= 0 && MathF.Abs(g.Nodes[k].Y - r.Waypoint.Y) < 0.5f && Vector2.Distance(new(g.Nodes[k].X, g.Nodes[k].Z), new(r.Waypoint.X, r.Waypoint.Z)) < 1.8f
                    && search.Find(g, blueStart, k, path, maxExpand: 80000) > 0)
                    reached++;
                else
                    missing.Add($"{b.Def.Name} ({r.Waypoint.X:0},{r.Waypoint.Z:0}) {r.Id}");
            }
        Line($"{m.Name}: bots reach {reached} of {rooms} rooms from Blue's spawn" + (missing.Count > 0 ? ": missing " + string.Join(", ", missing.Take(8)) : ""));
        if (reached < rooms)
            Fail($"{m.Name}: {rooms - reached} room(s) the bots can't reach: {string.Join(", ", missing.Take(8))}");

        // Fairness by route: the average nav walk from each team's spawns to every hill and to the enemy flag.
        float Walk(int team, Vector3 goal)
        {
            var total = 0f;
            var gi = g.Nearest(goal, needSight: false);
            foreach (var s in o.TeamSpawns[team])
            {
                var count = search.Find(g, g.Nearest(s, needSight: false), gi, path, maxExpand: 80000);
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
        void Compare(string name, float blue, float red)
        {
            var diff = (blue - red) / MathF.Max(blue, red);
            walks.Add($"{name} {blue:0}/{red:0} m ({diff * 100:+0.0;-0.0;0.0}%)");
            if (!float.IsFinite(blue + red) || MathF.Abs(diff) > 0.08f)
                Fail($"{m.Name}: {name} is a {blue:0} m walk for Blue and {red:0} m for Red (more than 8% apart)");
        }
        if (o.Hills.Count != 3)
            Fail($"{m.Name}: {o.Hills.Count} hills (expected launch control, the silo floor and the reactor)");
        for (var i = 0; i < o.Hills.Count; i++)
            Compare($"hill {i + 1} (y {o.Hills[i].Center.Y:0})", Walk(0, o.Hills[i].Center), Walk(1, o.Hills[i].Center));
        Compare("enemy flag", Walk(0, o.Flags[1]), Walk(1, o.Flags[0]));
        var line = $"{m.Name} balance (Blue/Red nav walk): {string.Join(", ", walks)}";
        Line(line);
        Line("::notice title=World Tour Silo::" + line.Replace("%", "%25"));
        World.SetMap(Maps.All[0]);
    }
}
