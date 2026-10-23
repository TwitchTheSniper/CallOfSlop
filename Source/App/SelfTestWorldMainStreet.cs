using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The "World Tour: Main Street" self-test section: Maps/World/01-MainStreet.json, the small-town American Main Street
/// with cornfields round it (small, 12 players). Checks the shape (small, a non-rectangular outline with lanes poking
/// out, real terrain: the tilted town, the rail cutting and embankment), the hand-placed objectives, that the corn
/// conceals people from bots (Map.Concealed: hidden a step inside the field, seen on the open street and up close),
/// that no Blue spawn can see a Red one, the nav walk from each team's spawns to the hills and the enemy flag within
/// 10%, that bots reach every room of every building and the far side of the train, and prints the object counts. No bot
/// matches (the generic short matches cover every map).
/// </summary>
public static partial class SelfTest
{
    private const string MainStreetMap = "Main Street";

    private static void CheckWorldMainStreet()
    {
        if (!Directory.Exists(Maps.Folder))
            return;
        var m = Maps.All.FirstOrDefault(x => x.Name == MainStreetMap);
        if (m == null)
        {
            Fail($"no map \"{MainStreetMap}\" (Maps/World/01-MainStreet.json)");
            return;
        }
        var notes = new List<string>();
        void Note(string s)
        {
            Line(s);
            notes.Add(s);
        }
        if (m.SourceFile == null || !Path.GetFileName(m.SourceFile).Equals("01-MainStreet.json", StringComparison.Ordinal) || m.Pack?.Folder != "World")
            Fail($"{m.Name}: should be Maps/World/01-MainStreet.json (is {m.SourceFile}, pack {m.Pack?.Folder})");
        if (m.Boundary is not { } bd || bd.Spec.Shape == BoundaryShape.Rect)
        {
            Fail($"{m.Name}: needs a non-rectangular boundary");
            return;
        }
        if (m.Size != MapSize.Small || m.MaxPlayers != 12)
            Fail($"{m.Name}: size {m.Size}, {bd.Area:0} m2, max {m.MaxPlayers} players (expected Small, 12)");
        if (!m.HasTerrain)
            Fail($"{m.Name}: needs terrain (the town tilts, the tracks cut through and run on an embankment)");

        // ---- Terrain: the north alley is higher than the south one, the tracks are level through the cutting and on the embankment.
        float north = m.GroundHeight(-12f, -19.7f), south = m.GroundHeight(-12f, 19.8f);
        float cut = m.GroundHeight(37f, -24f), bank = m.GroundHeight(37f, 24f), beside = m.GroundHeight(30f, 24f);
        if (north - south < 1.5f || MathF.Abs(cut - bank) > 0.2f || bank - beside < 1.0f)
            Fail($"{m.Name}: terrain isn't as designed (north alley {north:0.00} m, south alley {south:0.00} m, track {cut:0.00}/{bank:0.00} m, beside the embankment {beside:0.00} m)");

        // ---- Objectives: hand-placed, spawns for everyone.
        var o = m.Objectives;
        if (o.TeamSpawnsGenerated || o.HillsGenerated || o.FlagsGenerated)
            Fail($"{m.Name}: objectives should be hand-placed");
        if (m.Spawns.Count < m.MaxPlayers)
            Fail($"{m.Name}: {m.Spawns.Count} spawns for {m.MaxPlayers} players");

        // ---- Counts.
        var props = m.PropInstances;
        var corn = props.Count(p => p.Def.Concealment);
        var drawn = Enumerable.Range(0, m.Solid.Count).Count(m.IsDrawnBox);
        Note($"{m.Name}: {bd.Spec.Shape} outline, {bd.SegmentCount} segments, {bd.Area:0} m2 ({m.Size}), max {m.MaxPlayers}; {m.PlacedBuildings.Count} buildings, "
             + $"{m.Solid.Count} boxes ({drawn} drawn one by one), {props.Count} props ({props.Count(p => p.Def.IsSolid)} solid, {props.Count(p => p.IsBreakable)} breakable, "
             + $"{corn} corn patches), {m.FoliageInstances.Length} plants, {m.MeshPlacements.Count} meshes, {m.Spawns.Count} spawns, {m.Waypoints.Count} waypoints; "
             + $"ground {north:0.0} m (north alley) to {south:0.0} m (south alley)");
        if (m.PlacedBuildings.Count < 7)
            Fail($"{m.Name}: {m.PlacedBuildings.Count} buildings (the street has 7)");
        if (corn < 80 || m.ConcealingProps != corn)
            Fail($"{m.Name}: {corn} corn patches ({m.ConcealingProps} concealing props); the fields want at least 80");
        if (drawn > 200)
            Fail($"{m.Name}: {drawn} boxes drawn one by one (keep it low: buildings, props and meshes are instanced)");

        // ---- Concealment: a bot on the north alley can't see someone 3 m into the corn, but sees them in the open.
        static Vector3 Eye(Map map, float x, float z) => new(x, map.GroundHeight(x, z) + 1.6f, z);
        static Vector3 Chest(Map map, float x, float z) => new(x, map.GroundHeight(x, z) + 1.2f, z);
        var inCorn = m.PropInstances.Where(p => p.Def.Concealment && p.Position.Z < -24f).Select(p => p.Position).FirstOrDefault();
        var hidden = m.Concealed(Eye(m, inCorn.X, -19.7f), Chest(m, inCorn.X, inCorn.Z - 1.0f));
        var open = m.Concealed(Eye(m, -25f, 0f), Chest(m, 20f, 0.5f));
        var close = m.Concealed(Eye(m, inCorn.X, inCorn.Z + 1.0f), Chest(m, inCorn.X, inCorn.Z - 1.0f));
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var hits = 0;
        for (var i = 0; i < 2000; i++)
            if (m.Concealed(Eye(m, -40f + i % 80, -19.7f), Chest(m, 40f - i % 80, -27f)))
                hits++;
        var us = sw.Elapsed.TotalMilliseconds * 1000 / 2000;
        Note($"{m.Name} concealment: into the corn from the alley hidden {hidden}, along the open street hidden {open}, 2 m apart in the corn hidden {close}; "
             + $"{us:0.0} us a query ({hits} of 2000 sight lines across the north field hidden)");
        if (!hidden || open || close)
            Fail($"{m.Name}: corn concealment is wrong (into the field {hidden}, open street {open}, up close {close})");
        if (us > 50)
            Fail($"{m.Name}: a concealment query takes {us:0.0} us (bots ask hundreds a second)");

        // ---- No Blue spawn sees a Red spawn (walls, the train, the corn).
        var g = ClutteredLargeNav(m); // also makes it the current map, for the ray casts below
        var seen = 0;
        foreach (var a in o.TeamSpawns[0])
            foreach (var b in o.TeamSpawns[1])
            {
                Vector3 ea = a + new Vector3(0, 1.6f, 0), eb = b + new Vector3(0, 1.6f, 0);
                var d = eb - ea;
                var dist = d.Length();
                if (World.Query.Raycast(ea, d / dist, dist, QueryFilter.All) >= dist - 0.05f && !m.Concealed(ea, eb))
                    seen++;
            }
        if (seen > 0)
            Fail($"{m.Name}: {seen} Blue spawn(s) can see a Red spawn");

        // ---- Fair walks (the map isn't mirrored): hills and the enemy flag within 10% for both teams.
        var search = new NavSearch();
        var path = new int[4096];
        float Walk(int team, Vector3 goal)
        {
            var total = 0f;
            var gi = g.Nearest(goal, needSight: false);
            foreach (var s in o.TeamSpawns[team])
            {
                var count = search.Find(g, g.Nearest(s, needSight: false), gi, path, maxExpand: 40000);
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
        void Fair(string name, float blue, float red)
        {
            var diff = (blue - red) / MathF.Max(blue, red);
            walks.Add($"{name} {blue:0}/{red:0} m ({diff * 100:+0.0;-0.0;0.0}%)");
            if (!float.IsFinite(blue + red) || MathF.Abs(diff) > 0.1f)
                Fail($"{m.Name}: {name} is a {blue:0} m walk for Blue and {red:0} m for Red (more than 10% apart)");
        }
        for (var i = 0; i < o.Hills.Count; i++)
            Fair($"hill {i + 1}", Walk(0, o.Hills[i].Center), Walk(1, o.Hills[i].Center));
        Fair("enemy flag", Walk(0, o.Flags[1]), Walk(1, o.Flags[0]));

        // ---- Reach: every building's front door, the corn bands and both sides of the train from Blue's spawns.
        var start = g.Nearest(o.TeamSpawns[0][0], needSight: false);
        bool Reach(Vector3 p)
        {
            var k = g.Nearest(p, needSight: false);
            return k >= 0 && start >= 0 && search.Find(g, start, k, path, maxExpand: 60000) > 0;
        }
        var unreached = new List<string>();
        foreach (var b in m.PlacedBuildings)
        {
            var front = b.Plan.Doors.FirstOrDefault(d => d.Front) ?? b.Plan.Doors.FirstOrDefault();
            if (front != null && !Reach(front.SwingSide))
                unreached.Add(b.Def.Name);
        }
        foreach (var (name, x, z) in new[] { ("north corn", -15f, -27f), ("south corn", 15f, 27f), ("behind the train", 42f, -8f), ("the square", 11.9f, 12f), ("the yard", -40f, 0f) })
            if (!Reach(new Vector3(x, m.GroundHeight(x, z), z)))
                unreached.Add(name);
        Note($"{m.Name} balance (nav walk averaged over team spawns): {string.Join(", ", walks)}; spawn-to-spawn sight lines {seen}; "
             + $"unreached from Blue's spawn: {(unreached.Count == 0 ? "none" : string.Join(", ", unreached))}");
        if (unreached.Count > 0)
            Fail($"{m.Name}: bots can't reach {string.Join(", ", unreached)}");

        CheckBuildingNav(m); // SelfTestBuildings.cs: every room and stair top of every building on the map
        World.SetMap(Maps.All[0]);
        Line("::notice title=World Tour - Main Street::" + string.Join("%0A", notes.Select(n => n.Replace("%", "%25").Replace("\r", "").Replace("\n", " "))));
    }
}
