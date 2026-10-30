using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The routing rules for world links (board item wl01; docs/sandbox/CONSTRUCTS.md, "World links"): the construct links'
/// rules (<see cref="PathRule"/>) for a path whose points may be hosted by the world (the terrain, static map geometry)
/// or by any construct. The same limits, angle rules, clipping grace (<see cref="Grace"/>, <see cref="ExitAlong"/>) and
/// embedding forgiveness (<see cref="EmbedDepth"/>, <see cref="NodeSinks"/>, <see cref="SegmentBlocked"/>), checked
/// against every construct near the path in its rest frame, and against the static world by rays.
/// </summary>
public static partial class ConstructRoutes
{
    /// <summary>What a point of a world link is on: a construct's part, or the world (<see cref="C"/> null).</summary>
    public struct WHost
    {
        public Construct? C;
        public int Part;
    }

    /// <summary>Is part type <paramref name="d"/> an input of a world link of type <paramref name="t"/> (from another construct)? Fuel goes into a tank, charge into a battery or anything electric, signals into an input, items into storage.</summary>
    public static bool IsWorldInput(ConstructPartDef d, RouteType t) => t switch
    {
        RouteType.Data => d.MaxInputs > 0 && d.Logic != CLogic.Reader,
        RouteType.Power => d.Electric || d.Role == PartRole.Battery,
        RouteType.Pipe => d.Role == PartRole.FuelTank,
        RouteType.Shaft => false,
        _ => d.Role == PartRole.Storage,
    };

    public static string WorldInputHint(RouteType t) => t switch
    {
        RouteType.Pipe => "a pipe between constructs fills a tank: the engine runs from its own tank",
        RouteType.Power => "end it at a battery or a part that draws power",
        RouteType.Shaft => "torque stays on one construct",
        _ => InputHint(t),
    };

    /// <summary>
    /// Does a click on part <paramref name="part"/> of construct <paramref name="c"/> end a world link of type
    /// <paramref name="type"/> from part <paramref name="from"/> of <paramref name="cf"/>? On another part's world input,
    /// never with free routing (the device is then only a surface).
    /// </summary>
    public static bool WorldClickEnds(Construct cf, int from, Construct c, int part, RouteType type, bool free) =>
        !free && part >= 0 && part < c.PartCount && IsWorldInput(c.DefOf(part), type) && !(c == cf && part == from);

    private static Vector3 ToBody(Construct c, Vector3 p)
    {
        c.WorldFrame(0, out var q, out var t);
        return Vector3.Transform(p - t, Quaternion.Conjugate(q));
    }

    private static bool Near(Construct c, Vector3 a, Vector3 b, float radius)
    {
        Geometry.ClosestSegmentSegment(a, b, c.WorldCom, c.WorldCom, out var on, out _);
        var reach = c.Radius + radius + 0.1f;
        return Vector3.DistanceSquared(on, c.WorldCom) <= reach * reach;
    }

    /// <summary>How far along ab (from a, world) it runs inside its host part before it leaves it (0 on the world).</summary>
    private static float ExitAlongWorld(in WHost h, Vector3 a, Vector3 b) => h.C is { } c ? ExitAlong(c, h.Part, ToBody(c, a), ToBody(c, b)) : 0f;

    /// <summary>Is a world node at <paramref name="p"/> deeper in the static world than the embedding forgiveness (a ray down from just above it lands above it)?</summary>
    public static bool SinksWorld(IWorldQuery q, Vector3 p, float radius)
    {
        const float above = 0.3f;
        var probe = above - EmbedDepth(radius) - EmbedSlack;
        return q.Raycast(p + Vector3.UnitY * above, -Vector3.UnitY, probe, QueryFilter.StaticOnly, out _);
    }

    /// <summary>
    /// The routing rule for a world link's path (world points), one rule for the server's
    /// <see cref="ConstructWorld.CheckWorldLink"/> and the client's preview: <paramref name="pts"/>[0] the output's port,
    /// then the nodes, then, when <paramref name="closed"/>, the input's port. <paramref name="hosts"/>: what each point is on.
    /// Null when it is fine, else the reason.
    /// </summary>
    public static string? WorldPathRule(ConstructWorld world, IWorldQuery? q, RouteType type, ReadOnlySpan<Vector3> pts, ReadOnlySpan<WHost> hosts, bool closed)
    {
        var n = pts.Length;
        if (n < 2 || hosts.Length < n)
            return null;
        if (type == RouteType.Shaft)
            return "a drive shaft can't run between constructs: torque stays on one body";
        var radius = Radius(type);
        Span<float> along = stackalloc float[Construct.MaxNodesPerRoute + 3];
        if (n > along.Length)
            return $"a link has at most {Construct.MaxNodesPerRoute} nodes";
        along[0] = 0;
        for (var i = 1; i < n; i++)
        {
            var len = Vector3.Distance(pts[i - 1], pts[i]);
            if (len < MinSegment)
                return "those nodes are too close together";
            if (len > MaxSegment)
                return $"a segment reaches at most {MaxSegment:0} m: add a node";
            along[i] = along[i - 1] + len;
        }
        var length = along[n - 1];
        if (length > MaxLength)
            return $"a link reaches at most {MaxLength:0} m";
        var graceStart = Grace(radius) + ExitAlongWorld(hosts[0], pts[0], pts[1]);
        var graceEnd = closed ? Grace(radius) + ExitAlongWorld(hosts[n - 1], pts[n - 1], pts[n - 2]) : 0f;
        var last = closed ? n - 1 : n;
        for (var i = 1; i < last; i++)
        {
            if (along[i] <= graceStart || closed && length - along[i] <= graceEnd)
                continue;
            foreach (var c in world.Bodies)
                if (Near(c, pts[i], pts[i], radius) && NodeSinks(c, ToBody(c, pts[i]), radius) is { } inside)
                    return $"that node would sink into the {c.DefOf(inside).Name}: place it on the surface";
            if (q != null && hosts[i].C == null && SinksWorld(q, pts[i], radius))
                return "that node would sink into the ground: place it on the surface";
        }
        for (var i = 1; i < n; i++)
        {
            var a = pts[i - 1];
            var b = pts[i];
            var len = along[i] - along[i - 1];
            var t0 = MathF.Max(0f, graceStart - along[i - 1]);
            var t1 = len - (closed ? MathF.Max(0f, graceEnd - (length - along[i])) : 0f);
            if (t1 <= t0 || len < 1e-6f)
                continue;
            var d = (b - a) / len;
            var sa = a + d * t0;
            var sb = a + d * t1;
            foreach (var c in world.Bodies)
            {
                if (!Near(c, sa, sb, radius))
                    continue;
                var skipA = i == 1 && hosts[0].C == c ? hosts[0].Part : -1;
                var skipB = closed && i == n - 1 && hosts[n - 1].C == c ? hosts[n - 1].Part : -1;
                if (SegmentBlocked(c, ToBody(c, sa), ToBody(c, sb), radius, skipA, skipB) is { } wall)
                    return $"it would pass through the {c.DefOf(wall).Name}: route it round (or through an opening)";
            }
            // The static world: the centreline may sit the embedding forgiveness inside a surface (lifted by it).
            var lift = Vector3.UnitY * (EmbedDepth(radius) + EmbedSlack);
            if (q != null && q.Raycast(sa + lift, d, t1 - t0, QueryFilter.StaticOnly, out _))
                return "it would pass through the ground or a wall: add a node on the surface";
        }
        var prev = Vector3.Zero;
        for (var i = 1; i < n; i++)
        {
            var d = Vector3.Normalize(pts[i] - pts[i - 1]);
            if (prev != Vector3.Zero)
            {
                var bend = MathF.Acos(Math.Clamp(Vector3.Dot(prev, d), -1f, 1f)) * 180f / MathF.PI;
                if (BendRule(type, bend) is { } why)
                    return why;
            }
            prev = d;
        }
        return null;
    }

    /// <summary>
    /// Where the next node of a world link goes for a world surface point (the terrain, a wall, a box; world frame): off the
    /// surface by the link's radius, on the <see cref="Grid"/> across it (on the ground: the height found again under the
    /// snapped spot), then the type's angle rule (<see cref="AngleSnap"/>). Quantised to 1 cm.
    /// </summary>
    public static Vector3 WorldSnap(IWorldQuery? q, RouteType type, Vector3 prev, Vector3 prevDir, Vector3 surface, Vector3 normal)
    {
        var radius = Radius(type);
        var nrm = normal.LengthSquared() > 1e-6f ? Vector3.Normalize(normal) : Vector3.UnitY;
        var p = surface + nrm * radius;
        var axis = MathF.Abs(nrm.X) > MathF.Abs(nrm.Y) ? MathF.Abs(nrm.X) > MathF.Abs(nrm.Z) ? 0 : 2 : MathF.Abs(nrm.Y) > MathF.Abs(nrm.Z) ? 1 : 2;
        for (var a = 0; a < 3; a++)
            if (a != axis)
                p[a] = MathF.Round(p[a] / Grid) * Grid;
        if (axis == 1 && nrm.Y > 0.5f && q != null && q.Raycast(p + Vector3.UnitY * 0.5f, -Vector3.UnitY, 1f + radius, QueryFilter.StaticOnly, out var hit))
            p.Y = p.Y + 0.5f - hit.Distance + radius / MathF.Max(0.5f, hit.Normal.Y); // (the ground again under the snapped spot; on a slope a little higher, so the round link stays clear)
        return AngleSnap(type, prev, prevDir, p);
    }

    /// <summary>
    /// The rule for a world link being routed (open) with its next node at a spot (the adjust search tries spots through it):
    /// the output's port, the nodes so far (world points with their hosts), then the spot on <c>Host</c>.
    /// </summary>
    public struct WorldOpenRule : INodeRule
    {
        public ConstructWorld World;
        public IWorldQuery? Query;
        public RouteType Type;
        public Vector3[] Pts; // the start and the nodes so far (world)
        public WHost[] Hosts;
        public int Count;
        public WHost Host; // what the next node is on

        public readonly string? Why(Vector3 pos)
        {
            if (Count > Construct.MaxNodesPerRoute)
                return $"a link has at most {Construct.MaxNodesPerRoute} nodes";
            if (Host.C == null && Query != null && ConstructWorld.FindSupport(Query, pos, Radius(Type)) == Vector3.Zero)
                return "a node must sit on a surface (the ground, a wall, a construct)";
            Pts[Count] = pos;
            Hosts[Count] = Host;
            return WorldPathRule(World, Query, Type, Pts.AsSpan(0, Count + 1), Hosts.AsSpan(0, Count + 1), false);
        }
    }
}
