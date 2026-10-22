using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The World Tour pack's 07 Village (small, England, 12 players): a Cotswold village on a hillside, built from the
/// building library (church, cottages, terrace, village shop, pub) with the Countryside props and UK road markings.
/// Checks the file and the pack, a non-rectangular outline, the small-map size and player limit, real elevation (the
/// hill, the raised churchyard, the stream), the UK flag pool, its buildings, nothing outside the outline, hand-placed
/// objectives, solid props off spawns, hills and flags, the nav graph reaching every spawn, hill and flag and every
/// room of every building, and fair walks for both teams. No bot match here (the "Matches" section plays every map).
/// </summary>
public static partial class SelfTest
{
    private const string WorldPack = "World Tour";
    private const string WorldVillageMap = "Cotswold Village";

    private static void CheckWorldVillage()
    {
        if (!Directory.Exists(Maps.Folder))
            return;
        var m = Maps.All.FirstOrDefault(x => x.Name == WorldVillageMap && x.Pack?.Name == WorldPack);
        if (m == null)
        {
            Fail($"{WorldPack}: no map \"{WorldVillageMap}\" (Maps/World/07-Village.json)");
            return;
        }
        if (m.SourceFile == null || !Path.GetFileName(m.SourceFile).StartsWith("07-", StringComparison.Ordinal))
            Fail($"{m.Name}: file should be Maps/World/07-Village.json (is {m.SourceFile})");
        if (m.Boundary is not { } bd || bd.Spec.Shape == BoundaryShape.Rect)
        {
            Fail($"{m.Name}: the World Tour maps have a non-rectangular outline");
            return;
        }
        if (m.Size != MapSize.Small || m.MaxPlayersSetting != 12 || m.MaxPlayers != 12)
            Fail($"{m.Name}: {m.Size} with {m.MaxPlayers} players (expected Small, \"maxPlayers\": 12)");
        if (bd.Area is < 4500 or > 5600)
            Fail($"{m.Name}: {bd.Area:0} m2 inside the outline (expected 4,500 to 5,600)");
        // Lanes jut out of the outline: the High Street east and west, Church Lane east, the footpaths north and south.
        var juts = new[] { (-44f, 12f), (44f, 12f), (44f, -12.3f), (4.5f, -39f), (-5.5f, 38f) };
        var missingJuts = juts.Where(j => !bd.Contains(j.Item1, j.Item2)).ToList();
        if (missingJuts.Count > 0 || bd.Contains(44f, 0f) || bd.Contains(-44f, 0f))
            Fail($"{m.Name}: the lanes don't jut out of the outline ({missingJuts.Count} missing)");
        // Elevation: the churchyard is well above the High Street, the stream bed below it.
        var t = m.Terrain;
        float Ground(float x, float z) => m.GroundHeight(x, z);
        var yard = Ground(-24f, -25f);
        var street = Ground(0f, 12.3f);
        var bed = Ground(-31.2f, 12f);
        if (t == null || yard - street < 3.5f || street - bed < 2f)
            Fail($"{m.Name}: not enough elevation (churchyard {yard:0.0} m, High Street {street:0.0} m, stream under the bridge {bed:0.0} m)");
        if (m.FlagPoolIds is not { Length: 1 } pool || pool[0] != "uk")
            Fail($"{m.Name}: flag pool should be the UK flag");

        // Buildings.
        var names = m.PlacedBuildings.Select(b => b.Def.Name).ToList();
        foreach (var want in new[] { "Church", "Pub", "Village Shop", "Stone Cottage", "Thatched Cottage", "Row Houses" })
            if (!names.Contains(want))
                Fail($"{m.Name}: no {want}");

        var props = m.PropInstances;
        var solid = props.Count(p => p.Def.IsSolid);
        var o = m.Objectives;
        Line($"{m.Name}: {bd.Spec.Shape} outline, {bd.SegmentCount} segments, {bd.Area:0} m2 ({m.Size}), max {m.MaxPlayers}, ground {t?.MinHeight:0.0}..{t?.MaxHeight:0.0} m "
             + $"(churchyard {yard:0.0}, High Street {street:0.0}, stream {bed:0.0}); {m.PlacedBuildings.Count} buildings, {m.Solid.Count} boxes, "
             + $"{props.Count} props ({solid} solid, {props.Count(p => p.IsBreakable)} breakable), {m.FoliageInstances.Length} plants, {m.MeshPlacements.Count} meshes, "
             + $"{m.DecalRequests.Count} decal ops, spawns {m.Spawns.Count} (teams {o.TeamSpawns[0].Count}+{o.TeamSpawns[1].Count}), hills {o.Hills.Count}");

        // Nothing outside the outline.
        var outside = new List<string>();
        void In(string what, float x, float z, float margin = 0f)
        {
            if (!bd.Inside(x, z, margin))
                outside.Add($"{what} ({x:0.0},{z:0.0})");
        }
        foreach (var s in m.Spawns)
            In("spawn", s.X, s.Z, 0.3f);
        foreach (var w in m.Waypoints)
            In("waypoint", w.X, w.Z);
        foreach (var p in props.Take(m.ClutterStart))
            In(p.Def.Name, p.Position.X, p.Position.Z);
        foreach (var h in o.Hills)
            In("hill", h.Center.X, h.Center.Z, h.Radius * 0.5f);
        for (var i = 0; i < o.Flags.Length; i++)
            In(i == 0 ? "blue flag" : "red flag", o.Flags[i].X, o.Flags[i].Z, 0.5f);
        if (outside.Count > 0)
            Fail($"{m.Name}: {outside.Count} thing(s) outside the outline: {string.Join(", ", outside.Take(6))}");

        // Hand-placed objectives and enough spawns.
        var authored = m.AuthoredSpawns >= 0 ? m.AuthoredSpawns : m.Spawns.Count;
        if (authored < m.MaxPlayers)
            Fail($"{m.Name}: {authored} hand-placed spawns for {m.MaxPlayers} players");
        if (o.TeamSpawnsGenerated || o.HillsGenerated || o.FlagsGenerated)
            Fail($"{m.Name}: objectives should be hand-placed");
        if (o.TeamSpawns[0].Count < 6 || o.TeamSpawns[1].Count < 6 || o.Hills.Count < 2)
            Fail($"{m.Name}: {o.TeamSpawns[0].Count} + {o.TeamSpawns[1].Count} team spawns and {o.Hills.Count} hills");

        // Solid props keep off spawns, hill zones and flags.
        static float Dist(Box b, Vector3 p)
        {
            var dx = MathF.Max(0, MathF.Max(b.Min.X - p.X, p.X - b.Max.X));
            var dz = MathF.Max(0, MathF.Max(b.Min.Z - p.Z, p.Z - b.Max.Z));
            return MathF.Sqrt(dx * dx + dz * dz);
        }
        var blocked = new List<string>();
        foreach (var p in props.Where(p => p.Def.IsSolid))
            for (var k = p.SolidStart; k < p.SolidStart + p.SolidCount; k++)
            {
                var b = m.Solid[k];
                var bad = m.Spawns.Any(sp => Dist(b, sp) < 1f && b.Max.Y > sp.Y + 0.05f && b.Min.Y < sp.Y + World.PlayerHeight)
                          || o.Hills.Any(h => Dist(b, h.Center) < h.Radius && b.Max.Y > h.Center.Y - 1f && b.Min.Y < h.Center.Y + 3f)
                          || o.Flags.Any(f => Dist(b, f) < 2f);
                if (bad)
                {
                    blocked.Add($"{p.Def.Name} ({p.Position.X:0.0},{p.Position.Z:0.0})");
                    break;
                }
            }
        if (blocked.Count > 0)
            Fail($"{m.Name}: {blocked.Count} solid prop(s) on a spawn, hill or flag: {string.Join("; ", blocked.Take(4))}");

        // The nav graph: every spawn, hill and flag, and every room of every building, in the main area.
        World.SetMap(m);
        for (var wait = 0; wait < 4000 && NavGraph.Current == NavGraph.Empty; wait++)
            NavGraph.Pump(50);
        var g = NavGraph.Current;
        var missing = new List<string>();
        bool Reach(Vector3 p, float reach, float dy)
        {
            for (var i = 0; i < g.Nodes.Length; i++)
            {
                var q = g.Nodes[i];
                if (g.Component[i] == g.MainComponent && MathF.Abs(q.Y - p.Y) < dy && (q.X - p.X) * (q.X - p.X) + (q.Z - p.Z) * (q.Z - p.Z) < reach * reach)
                    return true;
            }
            return false;
        }
        var reachXZ = MathF.Max(3f, g.Spacing * 1.5f);
        foreach (var s in m.Spawns)
            if (!Reach(s, reachXZ, 1.2f))
                missing.Add($"spawn ({s.X:0},{s.Z:0})");
        foreach (var h in o.Hills)
            if (!Reach(h.Center, reachXZ, 1.2f))
                missing.Add($"hill ({h.Center.X:0},{h.Center.Z:0})");
        foreach (var f in o.Flags)
            if (!Reach(f, reachXZ, 1.2f))
                missing.Add($"flag ({f.X:0},{f.Z:0})");
        var rooms = 0;
        foreach (var b in m.PlacedBuildings)
            foreach (var r in b.Plan.Rooms)
            {
                rooms++;
                if (!Reach(r.Waypoint, 1.6f, 0.4f))
                    missing.Add($"{b.Def.Name} room {r.Floor}:{r.Id}");
            }
        Line($"  nav graph: {g.Nodes.Length} nodes, {g.MainNodes.Length} in the main area; {rooms} building rooms");
        if (missing.Count > 0)
            Fail($"{m.Name}: the nav graph doesn't reach {string.Join(", ", missing.Take(6))}{(missing.Count > 6 ? $" and {missing.Count - 6} more" : "")}");

        // Fair walks (the village isn't mirrored: the two ends differ, the walks shouldn't by much).
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
            else if (MathF.Abs(blue - red) > 0.15f * MathF.Max(blue, red))
                Fail($"{m.Name}: unfair walk to {what}: blue {blue:0.0} m, red {red:0.0} m (over 15% apart)");
        }
        for (var h = 0; h < o.Hills.Count; h++)
            Compare($"hill {h + 1}", TeamWalk(0, o.Hills[h].Center), TeamWalk(1, o.Hills[h].Center));
        Compare("enemy flag", TeamWalk(0, o.Flags[1]), TeamWalk(1, o.Flags[0]));
        Line($"  walks blue/red: {string.Join(", ", walks)}");
        World.SetMap(Maps.All[0]);
    }
}
