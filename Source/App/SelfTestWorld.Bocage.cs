using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The World Tour pack's map 05, "Bocage" (France, large, 22 players): Normandy hedgerow country. Checks the map loads in
/// its pack with its size, outline, terrain and hand-placed objectives; that the hedgerows are what the map is about
/// (hundreds of solid pieces, tall enough that nobody climbs them, and bullets stop in them); that the new country
/// buildings stand on it and bots reach their rooms; and that it's fair: the layout is point-symmetric, so the nav walk
/// from each team's spawns to every hill and the enemy flag must be within 10%, and no Blue spawn may see a Red one.
/// No bot match here (the short one in "Matches" covers it, like every map).
/// </summary>
public static partial class SelfTest
{
    private const string BocagePackFolder = "World";
    private const string BocageMap = "Bocage";

    private static void CheckWorldBocage()
    {
        if (!Directory.Exists(Maps.Folder))
            return;
        var m = Maps.FindPack("World Tour")?.Maps.FirstOrDefault(x => x.Name == BocageMap) ?? Maps.Find(BocageMap);
        if (m == null)
        {
            Fail($"no map \"{BocageMap}\" (Maps/{BocagePackFolder}/05-Bocage.json)");
            return;
        }
        var notes = new List<string>();
        void Note(string s)
        {
            Line(s);
            notes.Add(s);
        }
        if (m.SourceFile == null || !m.SourceFile.Replace('\\', '/').EndsWith($"{BocagePackFolder}/05-Bocage.json", StringComparison.OrdinalIgnoreCase))
            Fail($"{m.Name}: file should be Maps/{BocagePackFolder}/05-Bocage.json (is {m.SourceFile})");
        if (m.Boundary is not { } bd || bd.Spec.Shape == BoundaryShape.Rect)
        {
            Fail($"{m.Name}: the World Tour maps have no rectangular outline");
            return;
        }
        if (m.Size != MapSize.Large || m.MaxPlayers != 22)
            Fail($"{m.Name}: size {m.Size}, max players {m.MaxPlayers} (expected Large, 22)");
        if (!m.HasTerrain)
            Fail($"{m.Name}: no terrain");
        var o = m.Objectives;
        if (o.TeamSpawnsGenerated || o.HillsGenerated || o.FlagsGenerated || o.Hills.Count != 3)
            Fail($"{m.Name}: objectives should be hand-placed, three hills");
        var authored = m.AuthoredSpawns >= 0 ? m.AuthoredSpawns : m.Spawns.Count;
        if (authored < m.MaxPlayers)
            Fail($"{m.Name}: {authored} hand-placed spawns for {m.MaxPlayers} players");

        // ---- The hedgerows: many, solid, tall, bullet-proof.
        var hedges = m.PropInstances.Where(p => p.Def.Name.StartsWith("Bocage Hedgerow", StringComparison.Ordinal)).ToList();
        var lowest = hedges.Count == 0 ? 0f : hedges.Min(p => p.Bounds.Max.Y - m.GroundHeight(p.Position.X, p.Position.Z));
        var drawn = Enumerable.Range(0, m.Solid.Count).Count(m.IsDrawnBox);
        Note($"{m.Name}: {m.Size} {bd.Spec.Shape} outline {bd.Area:0} m2, max {m.MaxPlayers}, {m.Spawns.Count} spawns, {m.PlacedBuildings.Count} buildings, "
             + $"{m.PropInstances.Count} props ({m.PropInstances.Count(p => p.Def.IsSolid)} solid), {hedges.Count} hedgerow pieces (lowest top {lowest:0.00} m over the ground), "
             + $"{m.FoliageInstances.Length} plants (trees {m.TrunkCount}), {m.MeshPlacements.Count} meshes, {drawn} boxes drawn one by one, {m.Solid.Count} boxes in all");
        if (hedges.Count < 300 || hedges.Any(p => !p.Def.IsSolid))
            Fail($"{m.Name}: {hedges.Count} hedgerow pieces (want 300+, all solid)");
        if (lowest < 3.6f)
            Fail($"{m.Name}: a hedgerow piece's top is only {lowest:0.00} m over the ground (climbable)");
        if (drawn > 800)
            Fail($"{m.Name}: {drawn} boxes drawn one by one (keep it under 800)");
        World.SetMap(m);
        int stopped = 0, tried = 0;
        foreach (var p in hedges.Where((_, i) => i % 17 == 0))
        {
            // shoot straight across the piece's middle, at chest and at head height over the bank
            var yaw = p.Yaw;
            var across = new Vector3(MathF.Sin(yaw), 0, MathF.Cos(yaw)); // the prop's +Z (its thin side)
            foreach (var h in new[] { 1.4f, 3.2f })
            {
                var mid = new Vector3(p.Position.X, p.Position.Y + h, p.Position.Z);
                var from = mid - across * 3f;
                tried++;
                if (World.Query.Raycast(from, across, 6f, QueryFilter.Shots) < 4.2f)
                    stopped++;
            }
        }
        Note($"{m.Name}: shots across hedgerows stopped {stopped} of {tried}");
        if (stopped < tried)
            Fail($"{m.Name}: {tried - stopped} of {tried} shots went through a hedgerow");

        // ---- The new country buildings stand on it, and bots get into their rooms.
        foreach (var name in new[] { "Norman Farmhouse", "Half-Timbered Barn", "Norman Cottage", "Church" })
            if (!m.PlacedBuildings.Any(b => b.Def.Name == name))
                Fail($"{m.Name}: no {name}");
        CheckBuildingNav(m);

        // ---- Fair: walks to every objective, spawn sight lines, the nav graph reaching everything.
        var g = ClutteredLargeNav(m);
        var search = new NavSearch();
        var path = new int[4096];
        float Walk(int team, Vector3 goal)
        {
            var total = 0f;
            var gi = g.Nearest(goal, needSight: false);
            foreach (var s in o.TeamSpawns[team])
            {
                var count = search.Find(g, g.Nearest(s, needSight: false), gi, path, maxExpand: 60000);
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
        var seen = 0;
        foreach (var a in o.TeamSpawns[0])
            foreach (var b in o.TeamSpawns[1])
            {
                var ea = a + new Vector3(0, World.EyeHeight, 0);
                var eb = b + new Vector3(0, World.EyeHeight, 0);
                var d = eb - ea;
                var len = d.Length();
                if (World.RaycastWorld(ea, d / len, len) >= len - 0.05f)
                    seen++;
            }
        var missing = new List<string>();
        void Reach(string what, Vector3 p)
        {
            var reach = MathF.Max(3f, g.Spacing * 1.5f);
            for (var i = 0; i < g.Nodes.Length; i++)
            {
                var q = g.Nodes[i];
                if (g.Component[i] == g.MainComponent && MathF.Abs(q.Y - p.Y) < 1.2f && (q.X - p.X) * (q.X - p.X) + (q.Z - p.Z) * (q.Z - p.Z) < reach * reach)
                    return;
            }
            missing.Add($"{what} ({p.X:0},{p.Z:0})");
        }
        foreach (var s in m.Spawns)
            Reach("spawn", s);
        foreach (var h in o.Hills)
            Reach("hill", h.Center);
        for (var i = 0; i < o.Flags.Length; i++)
            Reach(i == 0 ? "blue flag" : "red flag", o.Flags[i]);
        Note($"{m.Name} balance (Blue/Red nav walk): {string.Join(", ", walks)}; spawn-to-spawn sight lines {seen}; nav graph {g.Nodes.Length} nodes, "
             + $"{Enumerable.Range(0, g.Nodes.Length).Count(i => g.Component[i] == g.MainComponent)} in the main area, unreachable objectives {missing.Count}");
        if (seen > 0)
            Fail($"{m.Name}: {seen} Blue/Red spawn pair(s) in sight of each other");
        if (missing.Count > 0)
            Fail($"{m.Name}: the nav graph doesn't reach {string.Join(", ", missing.Take(6))}");
        World.SetMap(Maps.All[0]);
        Line("::notice title=World Tour: Bocage::" + string.Join("%0A", notes.Select(n => n.Replace("%", "%25"))));
    }
}
