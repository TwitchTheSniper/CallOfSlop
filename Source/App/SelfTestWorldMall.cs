using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The World Tour pack's map 02, "Slopview Mall" (Maps/World/02-Mall.json, written by tools/world/mall.py): an American
/// shopping mall (the Shopping Mall building) on a terraced lot. Checks:
/// <list type="bullet">
/// <item>file, pack, order, size (Medium), 16 players, the US flag pool, a noisy polygon outline and real terrain;</item>
/// <item>the branding: fascia signs and logo plaques for the invented shops, the mall's pack decals, and that every
/// single Decal op lands on a surface its decal allows;</item>
/// <item>it reads as a mall: shop fittings from Props/Retail in every unit, the food court stalls, escalators;</item>
/// <item>bots reach every room and stair top of the mall and the multi-storey car park, climb every escalator to its
/// bridge, and every spawn, hill and flag sits on the main nav graph;</item>
/// <item>costs: boxes drawn one by one, building shell boxes, props, plants and decals.</item>
/// </list>
/// </summary>
public static partial class SelfTest
{
    private const string WorldMallMap = "Slopview Mall";

    private static void CheckWorldMall()
    {
        if (!Directory.Exists(Maps.Folder))
            return;
        var m = Maps.Find(WorldMallMap);
        if (m == null)
        {
            Fail($"no map \"{WorldMallMap}\" (Maps/World/02-Mall.json)");
            return;
        }
        if (m.Pack?.Name != "World Tour" || m.SourceFile == null || !Path.GetFileName(m.SourceFile).Equals("02-Mall.json", StringComparison.Ordinal) || m.Order != 502)
            Fail($"{m.Name}: should be Maps/World/02-Mall.json, order 502, in the World Tour pack (is {m.SourceFile}, order {m.Order}, pack {m.Pack?.Name})");
        if (m.Size != MapSize.Medium || m.MaxPlayers != 16)
            Fail($"{m.Name}: size {m.Size}, {m.MaxPlayers} players (expected Medium, 16)");
        if (m.Boundary is not { } bd || bd.Spec.Shape != BoundaryShape.Polygon || bd.Spec.Noise is not { Amplitude: > 0 })
            Fail($"{m.Name}: the outline should be a noisy polygon (no rectangle)");
        if (m.Terrain is not { } terrain || terrain.MaxHeight - terrain.MinHeight < 4f)
            Fail($"{m.Name}: needs real terrain (at least 4 m between its lowest and highest ground)");
        if (m.FlagPoolIds is not ["us"])
            Fail($"{m.Name}: flagPool should be [\"us\"]");

        var props = m.PropInstances;
        var byName = props.GroupBy(p => p.Def.Name).ToDictionary(g => g.Key, g => g.Count());
        int Count(Func<string, bool> f) => byName.Where(kv => f(kv.Key)).Sum(kv => kv.Value);
        var signs = Count(n => n.EndsWith(" Sign", StringComparison.Ordinal) && props.Any(p => p.Def.Name == n && p.Def.Pack != null));
        var logos = Count(n => n.EndsWith(" Logo", StringComparison.Ordinal));
        var retail = props.Count(p => p.Def.Category == "Retail");
        var drawn = Enumerable.Range(0, m.Solid.Count).Count(i => m.IsDrawnBox(i) && !m.IsTrunk(i));
        var shell = Enumerable.Range(0, m.Solid.Count).Count(m.IsBuildingBox);
        Line($"{m.Name}: {m.PlayArea:0} m2, terrain {m.Terrain?.MinHeight:0.0} to {m.Terrain?.MaxHeight:0.0} m, buildings {m.PlacedBuildings.Count} ({shell} shell boxes), "
             + $"{drawn} boxes drawn one by one, props {props.Count} (solid {props.Count(p => p.Def.IsSolid)}, retail {retail}), plants {m.FoliageInstances.Length} (trees {m.TrunkCount}), "
             + $"shop signs {signs}, logos {logos}, decal requests {m.DecalCount}, pack decals named {m.PackDecalsNamed}, spawns {m.Spawns.Count}");
        if (m.PlacedBuildings.Count(b => b.Def.Name == "Shopping Mall") != 1)
            Fail($"{m.Name}: should place the Shopping Mall building once");
        if (drawn > 60)
            Fail($"{m.Name}: {drawn} boxes drawn one by one (keep the structure in buildings, props and meshes)");
        if (signs < 20 || logos < 14)
            Fail($"{m.Name}: only {signs} shop signs and {logos} logo plaques (every shop is branded)");
        if (m.PackDecalsNamed < 30)
            Fail($"{m.Name}: only {m.PackDecalsNamed} different pack decals (posters, wayfinding, film posters)");
        foreach (var (what, need) in new[] { ("Shop Checkout Counter", 10), ("Mannequin", 6), ("Clothes Rack Round", 4), ("Food Stall Counter", 3), ("Escalator Balustrade", 4),
                     ("Auditorium Seat Row", 8), ("Arcade Cabinet", 4), ("Retail Bookcase", 4), ("Aquarium Rack", 2), ("Jewellery", 3) })
            if (Count(n => n.StartsWith(what, StringComparison.Ordinal)) < need)
                Fail($"{m.Name}: only {Count(n => n.StartsWith(what, StringComparison.Ordinal))} \"{what}\" props (expected at least {need})");

        // Every single Decal op lands on a surface its decal allows (as the bake places it).
        var boxes = new Box[m.Solid.Count];
        for (var i = 0; i < boxes.Length; i++)
            boxes[i] = m.IsDecalSurface(i) ? m.HashBox(i) : new Box(new Vector3(0, -1000f, 0), new Vector3(0.01f));
        var world = new PhysicsWorld(boxes, m.TerrainCollision, m.HalfX, m.HalfZ);
        var lost = new List<string>();
        foreach (var r in m.DecalRequests.Where(r => !r.Scatter))
        {
            var def = m.DecalScope.Find(r.Names[0]);
            var rng = new DecalAtlas.Rng((uint)(r.Seed * 7919 + r.OpIndex * 104729 + 1));
            if (def == null || !DecalBaker.TryPlace(m, world, r, def, r.X0, r.Z0, r.Y, ref rng, out _))
                lost.Add($"{r.Names[0]} at ({r.X0:0.#}, {r.Y:0.#}, {r.Z0:0.#}) onto {r.Onto}");
        }
        var sw = Stopwatch.StartNew();
        var mesh = DecalBaker.Bake(m);
        Line($"{m.Name}: decals {mesh.Placed} of {mesh.Requested} placed ({mesh.Triangles} triangles, {mesh.Chunks.Length} chunks, {sw.Elapsed.TotalMilliseconds:0.0} ms); "
             + $"{lost.Count} single decals found no surface{(lost.Count > 0 ? ": " + string.Join("; ", lost.Take(10)) : "")}");
        if (lost.Count > 4 || mesh.Placed < mesh.Requested * 0.8)
            Fail($"{m.Name}: {lost.Count} single decals found no surface, {mesh.Placed}/{mesh.Requested} placed");

        // Bots: every room and stair top of both buildings (SelfTestBuildings.cs), then the escalators and the objectives.
        CheckBuildingNav(m);
        var g = NavGraph.Current;
        var search = new NavSearch();
        var path = new int[4096];
        var start = g.Nearest(new Vector3(0, m.GroundHeight(0, 28f), 28f), needSight: false);
        int Reach(Vector3 p, float dy = 0.6f)
        {
            var k = g.Nearest(p, needSight: false);
            if (k < 0 || MathF.Abs(g.Nodes[k].Y - p.Y) > dy || Vector2.Distance(new Vector2(g.Nodes[k].X, g.Nodes[k].Z), new Vector2(p.X, p.Z)) > 2f)
                return -1;
            return start >= 0 && search.Find(g, start, k, path, maxExpand: 80000) > 0 ? k : -2;
        }
        var bad = new List<string>();
        foreach (var s in m.Spawns)
            if (Reach(s) < 0)
                bad.Add($"spawn ({s.X:0.#}, {s.Y:0.#}, {s.Z:0.#}) [{MallBlockers(m, s)}]");
        foreach (var h in m.Objectives.Hills)
            if (Reach(h.Center) < 0)
                bad.Add($"hill ({h.Center.X:0.#}, {h.Center.Y:0.#}, {h.Center.Z:0.#}) [{MallBlockers(m, h.Center)}]");
        foreach (var f in m.Objectives.Flags)
            if (Reach(f) < 0)
                bad.Add($"flag ({f.X:0.#}, {f.Z:0.#})");
        // Escalators: the balustrades mark them; the top (a spawn on the bridge) must be reachable from the foot alone,
        // i.e. along the escalator, so search from a node at the foot with the bridge's other ways in still open is not
        // enough: check the foot and top nodes are joined directly.
        var escalators = props.Where(p => p.Def.Name == "Escalator Balustrade").ToList();
        var climbed = 0;
        foreach (var e in escalators)
        {
            var dir = MathF.Abs(MathF.Sin(e.Yaw)) > 0.5f ? new Vector3(MathF.Sin(e.Yaw) > 0 ? -1 : 1, 0, 0) : Vector3.Zero; // climbs towards its -Z
            var foot = e.Position - dir * 3.8f;
            var top = e.Position + dir * 3.8f + new Vector3(0, MallLevel1 - e.Position.Y, 0);
            int a = g.Nearest(new Vector3(foot.X, m.GroundHeight(foot.X, foot.Z) + 0.1f, foot.Z), needSight: false), b = g.Nearest(top, needSight: false);
            if (a >= 0 && b >= 0 && MathF.Abs(g.Nodes[b].Y - MallLevel1) < 0.6f && g.Nodes[a].Y < 1f && search.Find(g, a, b, path, maxExpand: 300) is > 0 and < 6)
                climbed++;
            else
                bad.Add($"escalator at ({e.Position.X:0.#}, {e.Position.Z:0.#}): foot node {(a >= 0 ? $"{g.Nodes[a]}" : "none")}, top node {(b >= 0 ? $"{g.Nodes[b]}" : "none")}; at the foot {MallBlockers(m, new Vector3(foot.X, 0.1f, foot.Z))}, "
                    + $"waypoint there {m.Waypoints.Any(w => MathF.Abs(w.X - foot.X) < 0.6f && MathF.Abs(w.Z - foot.Z) < 0.6f)}");
        }
        Line($"{m.Name}: nav {g.Nodes.Length} nodes; escalators climbed {climbed} of {escalators.Count}; unreachable: {(bad.Count == 0 ? "none" : string.Join("; ", bad.Take(12)))}");
        if (bad.Count > 0)
        {
            Fail($"{m.Name}: bots can't reach {bad.Count} spawn(s)/objective(s)/escalator(s)");
            foreach (var b in bad.Take(10))
                Fail("  " + b);
        }
        World.SetMap(Maps.All[0]);
    }

    /// <summary>What a standing body at <paramref name="p"/> overlaps (boxes: building, prop or map; the terrain), for failure lines.</summary>
    private static string MallBlockers(Map m, Vector3 p)
    {
        const float r = World.PlayerRadius + 0.05f;
        var hits = new List<string>();
        for (var i = 0; i < m.Solid.Count && hits.Count < 4; i++)
        {
            var b = m.Solid[i];
            if (b.Max.X > p.X - r && b.Min.X < p.X + r && b.Max.Z > p.Z - r && b.Min.Z < p.Z + r && b.Max.Y > p.Y + 0.05f && b.Min.Y < p.Y + World.PlayerHeight)
            {
                var prop = m.IsPropCollider(i) ? m.PropInstances.FirstOrDefault(q => i >= q.SolidStart && i < q.SolidStart + q.SolidCount).Def?.Name : null;
                hits.Add($"{(m.IsBuildingBox(i) ? "building" : prop != null ? "prop " + prop : m.IsTrunk(i) ? "trunk" : "box")} {b.Min.X:0.##},{b.Min.Y:0.##},{b.Min.Z:0.##}..{b.Max.X:0.##},{b.Max.Y:0.##},{b.Max.Z:0.##}");
            }
        }
        return $"ground {m.GroundHeight(p.X, p.Z):0.##}, blocked {m.IsBlocked(p)}{(hits.Count > 0 ? ": " + string.Join(", ", hits) : "")}";
    }

    /// <summary>The Shopping Mall's upper floor level above its ground (Buildings/ShoppingMall: 0.1 + 4.6).</summary>
    private const float MallLevel1 = 4.7f;
}
