using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// A rectangle of a planetoid's ground the bots' nav graph covers (<see cref="NavGraph.RequestSites"/>): x and z in a
/// frame laid on the ground (the penal colony's own, or the quarry works'), up along the planet's up at its origin.
/// </summary>
public readonly record struct NavSite(string Name, Vector3 Origin, Vector3 AxisX, Vector3 AxisZ, float MinX, float MaxX, float MinZ, float MaxZ)
{
    public Vector2 Local(Vector3 p) => new(Vector3.Dot(p - Origin, AxisX), Vector3.Dot(p - Origin, AxisZ));

    public bool Holds(Vector3 p, float margin = 0)
    {
        var l = Local(p);
        return l.X >= MinX - margin && l.X <= MaxX + margin && l.Y >= MinZ - margin && l.Y <= MaxZ + margin;
    }

    public Vector3 At(float x, float z) => Origin + AxisX * x + AxisZ * z;
}

/// <summary>
/// A door of a construct on a site: a panel on a hinge (<see cref="Joint"/>, ConstructDoor.cs) or an event door
/// (<see cref="Joint"/> -1, Construct.Doors.cs). Bots open it on their way, as F does (GameNavSites.cs).
/// </summary>
/// <summary>A door on a site: an event door's part (<see cref="Joint"/> -1) or a passive hinge's, and how far open the graph has it standing (radians; GameNavSites.cs learns a short one).</summary>
public readonly record struct NavSiteDoor(ushort Body, ushort Part, short Joint, Vector3 At, float Stands = 0);

/// <summary>
/// Bot navigation on a planetoid's sites (docs/story/PATHS.md; Karl, 2026-11-14: "sort out pathing for the penal colony"). A
/// planetoid is kilometres of ground all round, too big for a nav graph, so bots on one steered straight at their goals and
/// walked into the colony's buildings. The game names the few places where people live and work (<see cref="NavSite"/>s:
/// the colony, the quarry), and the graph is the ordinary one (A*, path following, shortcuts and the stuck watch all as in
/// the arena modes and Zombies), built over those rectangles only, from the ground and the constructs standing there.
/// <list type="bullet">
/// <item>Constructs aren't in the map's collision world, so the build copies the solid boxes of every anchored construct on a
/// site into its own copy of the map (<see cref="PhysicsWorld.AsBuilt"/> plus oriented boxes), and every probe below sees
/// walls, floors, stairs and furniture. Event doors are left out (a door is a way through) and listed in
/// <see cref="SiteDoors"/>, for the bots to open.</item>
/// <item>Nodes sit on a grid <see cref="SiteSpacing"/> apart in the site's frame, every surface under each point (a roof, an
/// upper floor, the ground floor). A grid point on a wall or a door jamb is moved within its cell until a standing body fits,
/// so a doorway always gets a node in it.</item>
/// <item>Links join neighbours a player could walk between (<see cref="World.CanWalkCurved"/>), so a wall, a step too high or a
/// table gets no link.</item>
/// </list>
/// Built a slice per tick like any graph (<see cref="Pump"/>), never baked (the constructs are the world's, not the map's), and
/// built again when the game asks again (a site's constructs changed). Off the sites bots steer straight, as before.
/// </summary>
public sealed partial class NavGraph
{
    public const float SiteSpacing = 1.5f; // m between grid points: a 100 m walk is under the 128 nodes a bot's path holds
    private const float SiteTop = 14f; // m over a site's origin the column probes start (the boiler house's stack is taller, but no one walks there)
    private const float SiteBottom = 6f; // m under it they stop (a quarry's floor dug down)

    /// <summary>The sites this graph covers (empty on every other graph).</summary>
    public NavSite[] Sites = [];

    /// <summary>The event doors on the sites (left out of the graph's walls: bots open them).</summary>
    public NavSiteDoor[] SiteDoors = [];

    /// <summary>Construct boxes the build saw, and the constructs they came from (the log line).</summary>
    public int SiteBoxes, SiteBodies;

    /// <summary>The longest slice of the site build (ms: a tick's share of it, the log line).</summary>
    public double SiteSliceMs;

    private double Slice(long t0)
    {
        var ms = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
        SiteSliceMs = Math.Max(SiteSliceMs, ms);
        return ms;
    }

    private static Map? _siteMap;

    /// <summary>
    /// Hinged doors seen standing open short of where a build put them (something the build doesn't hold stops them: the
    /// ground at the leaf's foot, a contact): (body id, joint) to the angle, used by the next build. GameNavSites.cs learns
    /// them; <see cref="LeafStandsVersion"/> counts the changes, so the host's look sees one and builds again.
    /// </summary>
    public static readonly Dictionary<(ushort Body, int Joint), float> LeafStands = new();
    public static int LeafStandsVersion;
    private static NavSite[] _siteAsk = [];
    private static ConstructWorld? _siteWorld;

    /// <summary>
    /// Asks for a graph over <paramref name="sites"/> of <paramref name="map"/> (a planetoid), from the ground and the anchored
    /// constructs of <paramref name="world"/> there. The graph the bots have now is dropped, and the new one is built a slice
    /// per tick (bots steer straight meanwhile). No sites: the planetoid has no graph again.
    /// </summary>
    public static void RequestSites(Map map, NavSite[] sites, ConstructWorld world)
    {
        if (!ReferenceEquals(_siteMap, map) && sites.Length > 0)
            LeafStands.Clear(); // (another world's bodies)
        _siteMap = sites.Length > 0 ? map : null;
        _siteAsk = sites;
        _siteWorld = world;
        if (ReferenceEquals(_builderMap, map))
        {
            _builder = null;
            _builderPhysics = null;
        }
        Forget(map);
    }

    /// <summary>Is a site graph asked for on <paramref name="map"/>?</summary>
    private static bool SitesAsked(Map map) => ReferenceEquals(_siteMap, map) && _siteAsk.Length > 0;

    /// <summary>The site holding <paramref name="p"/> (within <paramref name="margin"/> m), or -1.</summary>
    public int SiteOf(Vector3 p, float margin = 0)
    {
        for (var i = 0; i < Sites.Length; i++)
            if (Sites[i].Holds(p, margin))
                return i;
        return -1;
    }

    /// <summary>
    /// A bot on a site heading off it (BotMove.cs, BotPlan): the node to make for, on the site's edge towards the goal (the
    /// goal pulled into the site's rectangle, then back towards the bot until a node is near). -1 if none.
    /// </summary>
    public int NearestToward(Vector3 from, Vector3 goal)
    {
        var si = SiteOf(from, 2f);
        if (si < 0)
            return -1;
        var site = Sites[si];
        var g = site.Local(goal);
        var f = site.Local(from);
        var x = Math.Clamp(g.X, site.MinX + 1, site.MaxX - 1);
        var z = Math.Clamp(g.Y, site.MinZ + 1, site.MaxZ - 1);
        for (var k = 0; k < 12; k++)
        {
            var t = k / 12f;
            var p = site.At(float.Lerp(x, f.X, t), float.Lerp(z, f.Y, t));
            p += GravityFrames.UpAt(p) * Vector3.Dot(from - p, GravityFrames.UpAt(from)); // (at the bot's own level)
            var n = Nearest(p, needSight: false);
            if (n >= 0)
                return n;
        }
        return -1;
    }

    /// <summary>
    /// The node nearest <paramref name="goal"/> (within <paramref name="reach"/> m) that <paramref name="joined"/>'s part of the
    /// graph holds: a goal in a room the bot can't get into (walled off by its furniture) is walked as near as the way goes. -1 if none.
    /// </summary>
    public int NearestJoined(Vector3 goal, int joined, float reach)
    {
        if ((uint)joined >= (uint)Component.Length || _cols == 0)
            return -1;
        var want = Component[joined];
        var c0 = Math.Clamp((int)((goal.X - reach - _minX) / Spacing), 0, _cols - 1);
        var c1 = Math.Clamp((int)((goal.X + reach - _minX) / Spacing), 0, _cols - 1);
        var r0 = Math.Clamp((int)((goal.Z - reach - _minZ) / Spacing), 0, _rows - 1);
        var r1 = Math.Clamp((int)((goal.Z + reach - _minZ) / Spacing), 0, _rows - 1);
        var best = -1;
        var bestD = reach * reach;
        for (var cz = r0; cz <= r1; cz++)
        for (var cx = c0; cx <= c1; cx++)
        {
            var cell = cz * _cols + cx;
            for (var k = _cellStart[cell]; k < _cellStart[cell + 1]; k++)
            {
                var i = _cellNodes[k];
                if (Component[i] != want || NodeOff.Length != 0 && NodeOff[i])
                    continue;
                var d = Vector3.DistanceSquared(Nodes[i], goal);
                if (d < bestD)
                    (best, bestD) = (i, d);
            }
        }
        return best;
    }

    /// <summary>The site build (from <see cref="BuildSteps"/> on a planetoid whose game asked for sites).</summary>
    private static IEnumerable<NavGraph?> BuildSiteSteps(Map map)
    {
        var work = 0.0;
        var t0 = Stopwatch.GetTimestamp();
        var sites = _siteAsk;
        var world = _siteWorld;
        var g = new NavGraph { MapName = map.Name, MapHash = HashOf(map), DestructionAtStart = DestructionStamp(map), Origin = "sites", Sites = sites };
        var planet = GravityFrames.Point;
        if (planet == null || world == null || !World.NavBuildAsBuilt)
        {
            g.Origin = "sites: no planet"; // (a site graph needs the planet's frames and its own copy of the map to add the constructs to)
            g.Sites = [];
            yield return g;
            yield break;
        }
        var s = SiteSpacing;
        g.Spacing = s;

        // ---- The constructs on the sites, into this build's copy of the map ----
        var doors = new List<NavSiteDoor>();
        var leaves = new List<(Construct C, int Joint)>();
        var bodies = 0;
        var boxes = 0;
        var near = new List<Construct>();
        foreach (var site in sites)
        {
            near.Clear();
            var mid = site.At((site.MinX + site.MaxX) / 2, (site.MinZ + site.MaxZ) / 2);
            var reach = 0.5f * MathF.Sqrt((site.MaxX - site.MinX) * (site.MaxX - site.MinX) + (site.MaxZ - site.MinZ) * (site.MaxZ - site.MinZ)) + 4f;
            foreach (var c in world.Bodies)
                if ((c.Anchored || c.Pinned || c.Frozen) && Vector3.Distance(c.WorldCom, mid) < reach + c.Radius && !near.Contains(c))
                    near.Add(c);
            foreach (var c in near)
            {
                if (g.SiteCounted(c.Id))
                    continue;
                bodies++;
                boxes += AddConstruct(World.Physics, c, sites, doors, leaves);
                work += g.Slice(t0);
                yield return null;
                t0 = Stopwatch.GetTimestamp();
            }
        }
        World.Physics.Raycast(sites[0].Origin, Vector3.UnitY, 0.1f, QueryFilter.Navigation); // (the collision tree is built with the new boxes now, in a slice of its own)
        work += g.Slice(t0);
        yield return null;
        t0 = Stopwatch.GetTimestamp();
        // Doors and gates as they stand open: swung as far as F swings them, or to where they first meet something (a wall,
        // a bench, the ground), as the physics stops them; then the tree is built again with them.
        foreach (var (c, j) in leaves)
        {
            boxes += AddLeaf(World.Physics, c, j, out var stands);
            for (var k = 0; k < doors.Count; k++)
                if (doors[k].Body == c.Id && doors[k].Joint == j)
                    doors[k] = doors[k] with { Stands = stands };
        }
        g.SiteBoxes = boxes;
        g.SiteBodies = bodies;
        g.SiteDoors = doors.ToArray();
        g._siteCounted.Clear();
        work += g.Slice(t0);
        yield return null;
        t0 = Stopwatch.GetTimestamp();
        World.Physics.Raycast(sites[0].Origin, Vector3.UnitY, 0.1f, QueryFilter.Navigation);
        work += g.Slice(t0);
        yield return null;
        t0 = Stopwatch.GetTimestamp();

        // ---- Nodes: every surface under each grid point, a point on a wall moved within its cell ----
        var nodes = new List<Vector3>();
        var fine = new List<bool>(); // (per node: found half a spacing from others, so linked only as far)
        var nudges = SiteNudges;
        foreach (var site in sites)
        {
            for (var x = site.MinX + s / 2; x < site.MaxX; x += s)
            for (var z = site.MinZ + s / 2; z < site.MaxZ; z += s)
            {
                var from = nodes.Count;
                var col = ColumnNodes(planet, site.At(x, z), nodes, fine, from, s * 0.6f, false);
                // Under a roof (rooms, furniture, doorways) or on a wall: the cell's quarters too, half a spacing apart.
                if ((col & (ColumnFloor | ColumnRoofed)) != ColumnFloor)
                    for (var k = 0; k < 4; k++)
                        col |= ColumnNodes(planet, site.At(x + ((k & 1) - 0.5f) * s * 0.5f, z + ((k >> 1) - 0.5f) * s * 0.5f), nodes, fine, from, s * 0.3f, true);
                // Still no node on the lowest floor (a wall or a door jamb there, a table over it, a body that doesn't fit so
                // near a wall)? Look round the cell for a spot that has.
                for (var k = 0; k < nudges.Length && (col & ColumnFloor) == 0; k++)
                    col |= ColumnNodes(planet, site.At(x + nudges[k].X * s, z + nudges[k].Y * s), nodes, fine, from, s * 0.3f, true);
                if (Stopwatch.GetElapsedTime(t0).TotalMilliseconds > 1)
                {
                    work += g.Slice(t0);
                    yield return null;
                    t0 = Stopwatch.GetTimestamp();
                }
            }
        }
        g.Nodes = nodes.ToArray();
        g.BuildSiteCells();
        work += g.Slice(t0);
        yield return null;
        t0 = Stopwatch.GetTimestamp();

        // ---- Links: neighbours within 1.6 spacings a player could walk between ----
        var edges = new List<(int From, int To, float Cost)>(nodes.Count * 8);
        var within = new List<int>();
        var linkR = s * 1.6f;
        for (var i = 0; i < g.Nodes.Length; i++)
        {
            var a = g.Nodes[i];
            within.Clear();
            g.Within(a, linkR, within);
            foreach (var j in within)
            {
                if (j <= i)
                    continue;
                var b = g.Nodes[j];
                var len = Vector3.Distance(a, b);
                if (len > (fine[i] && fine[j] ? linkR * 0.5f : linkR) || len < 0.05f)
                    continue; // (two of a cell's quarters link to their neighbours only: a room's many nodes, not a room's worth of links each)
                if (Clear(a, b) && World.CanWalkCurved(a, b) && World.CanWalkCurved(b, a)) // (both ways: a step down is a step up back)
                {
                    edges.Add((i, j, len));
                    edges.Add((j, i, len));
                }
            }
            if (Stopwatch.GetElapsedTime(t0).TotalMilliseconds > 1)
            {
                work += g.Slice(t0);
                yield return null;
                t0 = Stopwatch.GetTimestamp();
            }
        }

        // ---- Drop unlinked nodes, pack into CSR ----
        g.PackEdges(edges);
        work += g.Slice(t0);
        yield return null;
        t0 = Stopwatch.GetTimestamp();
        g.BuildSiteCells();
        work += g.Slice(t0);
        yield return null;
        t0 = Stopwatch.GetTimestamp();
        g.BuildComponents();
        g.Openness = new byte[g.Nodes.Length]; // (marksmen and anchors read it: nobody picks perches on a site)
        Array.Fill(g.Openness, (byte)4);
        g.BuildMs = work + g.Slice(t0);
        g.FinishRuntime(map);
        yield return g;
    }

    private readonly HashSet<ushort> _siteCounted = [];

    private bool SiteCounted(ushort id) => !_siteCounted.Add(id);

    /// <summary>Where a grid point's column is looked at again when its lowest floor has no node (a share of the spacing).</summary>
    private static readonly Vector2[] SiteNudges =
    [
        new(0.5f, 0), new(-0.5f, 0), new(0, 0.5f), new(0, -0.5f), new(0.25f, 0), new(-0.25f, 0), new(0, 0.25f), new(0, -0.25f),
        new(0.4f, 0.4f), new(-0.4f, 0.4f), new(0.4f, -0.4f), new(-0.4f, -0.4f),
    ];

    private const int ColumnFloor = 1, ColumnRoofed = 2;

    /// <summary>
    /// Adds the nodes under one grid point (top down: a roof, each floor, the ground floor) that aren't within
    /// <paramref name="apart"/> of one the cell has (from <paramref name="from"/> on). Flags: <see cref="ColumnFloor"/> when
    /// the ground floor there has a node (within a step and a foundation of the planet's ground) or there is no ground (false
    /// on a wall, a jamb, under a table); <see cref="ColumnRoofed"/> when something is built over it.
    /// </summary>
    private static int ColumnNodes(PointGravity planet, Vector3 at, List<Vector3> nodes, List<bool> fine, int from, float apart, bool isFine)
    {
        var c = World.Physics;
        var up = GravityFrames.UpOf(planet, at);
        var top = at + up * SiteTop;
        if (!c.Raycast(top, -up, SiteTop + SiteBottom, new QueryFilter(QueryLayers.Terrain), out var ground))
            ground.Distance = float.PositiveInfinity;
        var groundH = SiteTop - ground.Distance;
        var start = top;
        var flags = 0;
        for (var guard = 0; guard < 40; guard++)
        {
            var depth = SiteTop + SiteBottom - Vector3.Dot(top - start, up);
            if (depth <= 0.05f || !c.Raycast(start, -up, depth, QueryFilter.Navigation, out var hit))
                break;
            if (hit.Distance < 0.001f)
            {
                if (hit.Kind == ShapeKind.Terrain)
                    break; // (under the ground: nothing to stand on below)
                start -= up * 0.25f; // (inside a slab or a wall: on down through it)
                continue;
            }
            var feet = hit.Point + up * 0.03f;
            start = hit.Point - up * 0.05f;
            var height = Vector3.Dot(feet - at, up);
            if (height > groundH + 2.2f)
                flags |= ColumnRoofed;
            if (Vector3.Dot(hit.Normal, up) < World.MinWalkNormalY)
                continue;
            var have = Near(nodes, feet, from, apart);
            if (!have && World.BodyFitsOverStep(feet, up))
            {
                nodes.Add(feet);
                fine.Add(isFine);
                have = true;
            }
            if (have && height < groundH + 1.2f)
                flags |= ColumnFloor;
        }
        return float.IsPositiveInfinity(ground.Distance) ? flags | ColumnFloor : flags;
    }

    private static bool Near(List<Vector3> nodes, Vector3 p, int from, float within)
    {
        for (var i = from; i < nodes.Count; i++)
            if (Vector3.DistanceSquared(nodes[i], p) < within * within)
                return true;
        return false;
    }

    /// <summary>One construct's solid boxes into the build's world (oriented boxes): its event doors left out, its hinged doors and gates as they stand open, both listed.</summary>
    private static int AddConstruct(PhysicsWorld into, Construct c, NavSite[] sites, List<NavSiteDoor> doors, List<(Construct C, int Joint)> leaves)
    {
        var added = 0;
        for (var i = 0; i < c.PartCount; i++)
        {
            if (c.Parts[i].Broken)
                continue;
            c.WorldFrame(c.Parts[i].Seg, out var q, out var t);
            var seg = c.MoveSeg(c.Parts[i].Seg);
            if (seg > 0 && c.Joints[seg - 1].Type == JointType.Passive)
            {
                if (ConstructDoor.JointOf(c, i) == seg - 1 && !HasDoor(doors, c.Id, seg - 1))
                    AddDoor(c, i, (short)(seg - 1), sites, doors);
                // What hangs on a hinge is a door or a gate, a way through: the bots open it. It goes in as it stands open
                // (a leaf swung out past the wall: AddLeaf, once every construct is in), so a path doesn't hug the jamb it opens against.
                if (!leaves.Contains((c, seg - 1)))
                    leaves.Add((c, seg - 1));
                continue;
            }
            else if (c.DoorOf(i) is var d and >= 0)
            {
                if (c.Doors[d].Part == i)
                    AddDoor(c, i, -1, sites, doors);
                continue;
            }
            var count = c.SolidCount(i);
            for (var k = 0; k < count; k++)
            {
                c.SolidBox(i, k, out var centre, out var half);
                if (half.X <= 0 || half.Y <= 0 || half.Z <= 0)
                    continue;
                into.AddObb(new Obb(t + Vector3.Transform(centre, q), half, q));
                added++;
            }
        }
        return added;
    }

    private static bool HasDoor(List<NavSiteDoor> doors, ushort body, int joint)
    {
        foreach (var d in doors)
            if (d.Body == body && d.Joint == joint)
                return true;
        return false;
    }

    /// <summary>
    /// A passive hinge's segment as it stands open: F drives it to 0.95 of its far limit (ConstructPhysics), and it stops
    /// short where it first meets something solid (its boxes a little smaller, so the jamb it hangs on doesn't count),
    /// tried every 0.1 radians. Its boxes go into the build there; parts on joints beyond it are left out.
    /// </summary>
    private static int AddLeaf(PhysicsWorld into, Construct c, int joint, out float angle)
    {
        ref var jt = ref c.Joints[joint];
        var far = jt.Locked ? jt.Angle : OpenAngle(jt);
        if (!float.IsFinite(far))
            far = jt.Angle;
        if (!jt.Locked && LeafStands.TryGetValue((c.Id, joint), out var seen))
            far = seen; // (seen standing there)
        var filter = new QueryFilter(QueryLayers.Nav & ~QueryLayers.Doors);
        var steps = Math.Clamp((int)MathF.Ceiling(MathF.Abs(far) / 0.1f), 1, 32);
        angle = far;
        for (var k = 1; k <= steps && !jt.Locked; k++)
        {
            var a = far * k / steps;
            if (LeafMeets(into, c, joint, a, filter))
            {
                angle = far * (k - 1) / steps;
                break;
            }
        }
        LeafPose(c, joint, angle, out var q, out var t);
        var added = 0;
        for (var i = 0; i < c.PartCount; i++)
        {
            if (c.Parts[i].Broken || c.Parts[i].Seg != joint + 1)
                continue;
            for (var k = c.SolidCount(i) - 1; k >= 0; k--)
            {
                c.SolidBox(i, k, out var centre, out var half);
                if (half.X <= 0 || half.Y <= 0 || half.Z <= 0)
                    continue;
                into.AddObb(new Obb(t + Vector3.Transform(centre, q), half, q));
                added++;
            }
        }
        return added;
    }

    private static bool LeafMeets(PhysicsWorld into, Construct c, int joint, float angle, in QueryFilter filter)
    {
        LeafPose(c, joint, angle, out var q, out var t);
        for (var i = 0; i < c.PartCount; i++)
        {
            if (c.Parts[i].Broken || c.Parts[i].Seg != joint + 1)
                continue;
            for (var k = c.SolidCount(i) - 1; k >= 0; k--)
            {
                c.SolidBox(i, k, out var centre, out var half);
                half -= new Vector3(0.04f);
                if (half.X <= 0 || half.Y <= 0 || half.Z <= 0)
                    continue;
                if (into.OverlapBox(new Obb(t + Vector3.Transform(centre, q), half, q), filter))
                    return true;
            }
        }
        return false;
    }

    /// <summary>Where F drives a passive hinge open: 0.95 of its far limit (ConstructPhysics).</summary>
    public static float OpenAngle(in CJoint jt) => (jt.Max >= -jt.Min ? jt.Max : jt.Min) * 0.95f;

    /// <summary>A passive hinge's segment in the world at <paramref name="angle"/> (Construct.SegPose, from its parent as it stands).</summary>
    private static void LeafPose(Construct c, int joint, float angle, out Quaternion q, out Vector3 t)
    {
        ref var jt = ref c.Joints[joint];
        c.WorldFrame(jt.ParentSeg, out var pq, out var pt);
        var turn = Quaternion.CreateFromAxisAngle(jt.Axis, angle);
        q = pq * turn;
        t = pt + Vector3.Transform(jt.Pivot - Vector3.Transform(jt.Pivot, turn), pq);
    }

    private static void AddDoor(Construct c, int part, short joint, NavSite[] sites, List<NavSiteDoor> doors)
    {
        var at = c.SegToWorld(c.Parts[part].Seg, c.Parts[part].Pos);
        foreach (var site in sites)
            if (site.Holds(at, 2f))
            {
                doors.Add(new NavSiteDoor(c.Id, (ushort)part, joint, at));
                return;
            }
    }

    /// <summary>The spatial cells from the nodes' own bounds (a planetoid's map is kilometres wide).</summary>
    private void BuildSiteCells()
    {
        var min = new Vector2(float.MaxValue);
        var max = new Vector2(float.MinValue);
        foreach (var p in Nodes)
        {
            min = Vector2.Min(min, new Vector2(p.X, p.Z));
            max = Vector2.Max(max, new Vector2(p.X, p.Z));
        }
        if (Nodes.Length == 0)
            min = max = Vector2.Zero;
        _minX = min.X - 1;
        _minZ = min.Y - 1;
        _cols = Math.Max(1, (int)MathF.Ceiling((max.X - min.X + 2) / Spacing));
        _rows = Math.Max(1, (int)MathF.Ceiling((max.Y - min.Y + 2) / Spacing));
        var counts = new int[_cols * _rows + 1];
        foreach (var p in Nodes)
            counts[CellOf(p) + 1]++;
        for (var i = 0; i < _cols * _rows; i++)
            counts[i + 1] += counts[i];
        _cellStart = (int[])counts.Clone();
        _cellNodes = new int[Nodes.Length];
        var fill = (int[])counts.Clone();
        for (var i = 0; i < Nodes.Length; i++)
            _cellNodes[fill[CellOf(Nodes[i])]++] = i;
    }

    /// <summary>Drops the nodes no link reaches and packs the links into the CSR arrays.</summary>
    private void PackEdges(List<(int From, int To, float Cost)> edges)
    {
        var degree = new int[Nodes.Length];
        foreach (var e in edges)
            degree[e.From]++;
        var remap = new int[Nodes.Length];
        var kept = new List<Vector3>();
        for (var i = 0; i < Nodes.Length; i++)
        {
            remap[i] = degree[i] > 0 ? kept.Count : -1;
            if (degree[i] > 0)
                kept.Add(Nodes[i]);
        }
        Nodes = kept.ToArray();
        var count = Nodes.Length;
        EdgeStart = new int[count + 1];
        foreach (var e in edges)
            EdgeStart[remap[e.From] + 1]++;
        for (var i = 0; i < count; i++)
            EdgeStart[i + 1] += EdgeStart[i];
        EdgeTo = new int[edges.Count];
        EdgeCost = new float[edges.Count];
        EdgeFlags = new byte[edges.Count];
        EdgeLedge = new Vector3[edges.Count];
        var slot = (int[])EdgeStart.Clone();
        foreach (var e in edges)
        {
            var at = slot[remap[e.From]]++;
            EdgeTo[at] = remap[e.To];
            EdgeCost[at] = e.Cost;
        }
    }
}
