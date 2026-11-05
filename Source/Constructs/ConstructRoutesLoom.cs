using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// One Loom laid out (docs/sandbox/CONSTRUCTS.md, "Cable alignment"): the nodes each link of a bundle gets. Made by the client for its
/// preview and by the server for the edit, from the same picks, with <see cref="ConstructRoutes.LoomPlanFor"/>.
/// </summary>
public sealed class LoomPlan
{
    public readonly CRouteNode[][] Nodes = new CRouteNode[ConstructRoutes.LoomMaxLinks][];
    public readonly int[] Counts = new int[ConstructRoutes.LoomMaxLinks];
    public readonly int[] Links = new int[ConstructRoutes.LoomMaxLinks]; // the links, in the order Nodes holds them
    public int Count;

    public LoomPlan()
    {
        for (var i = 0; i < Nodes.Length; i++)
            Nodes[i] = new CRouteNode[ConstructRoutes.LoomMaxGuide + 2 * ConstructRoutes.LoomStub];
    }
}

public static partial class ConstructRoutes
{
    public const int LoomMaxLinks = 12; // links in a bundle
    public const int LoomMaxGuide = 32; // points of its run
    public const int LoomStub = 2; // nodes a link takes from its end to the bundle, at most
    public const float LoomGap = 0.01f; // the air between two links of a bundle
    private const int LoomPoints = LoomMaxGuide + 2 * LoomStub + 2;

    /// <summary>What a link has that the Loom can't take, or null: a shaft can't lie in a bundle, and nodes with branches on them have to stay.</summary>
    public static string? LoomRefuses(Construct c, int k)
    {
        if (c.Routes[k].Type == RouteType.Shaft)
            return "a drive shaft can't lie in a bundle";
        for (var i = 0; i < c.RouteCount; i++)
            if (c.Routes[i].Parent == k)
                return "that link has branches off it: take them off first";
        return null;
    }

    /// <summary>
    /// Lays <paramref name="links"/> side by side along <paramref name="guide"/> (points in their hosts' frames, on the bundle's middle
    /// line), one link-width apart across the run: every bend of the run is every link's bend, mitred, so the spacing holds round it. The
    /// side the run faces is <paramref name="normal"/>'s. Each link joins its own run from its own end, straight when it can and square
    /// otherwise, and every link passes the one path rule. Null, or why not; <paramref name="plan"/> holds the nodes.
    /// </summary>
    public static string? LoomPlanFor(Construct c, ReadOnlySpan<int> links, ReadOnlySpan<CRouteNode> guide, Vector3 normal, LoomPlan plan)
    {
        plan.Count = 0;
        var n = guide.Length;
        if (links.Length is < 2 or > LoomMaxLinks)
            return $"a bundle is 2 to {LoomMaxLinks} links";
        if (n is < 2 or > LoomMaxGuide)
            return "lay the run first: at least two points";
        Span<Vector3> g = stackalloc Vector3[LoomMaxGuide];
        Span<Vector3> tan = stackalloc Vector3[LoomMaxGuide];
        for (var j = 0; j < n; j++)
        {
            if (guide[j].Host >= c.PartCount)
                return "a node must sit on this construct";
            g[j] = c.ToRest(c.Parts[guide[j].Host].Seg, guide[j].Pos);
        }
        for (var j = 0; j + 1 < n; j++)
        {
            var d = g[j + 1] - g[j];
            if (d.Length() < MinSegment)
                return "those nodes are too close together";
            tan[j] = Vector3.Normalize(d);
            if (j > 0 && Vector3.Dot(tan[j - 1], tan[j]) < -0.5f)
                return "the run doubles back too sharply";
        }
        var side = Vector3.Cross(tan[0], normal.LengthSquared() > 1e-6f ? Vector3.Normalize(normal) : Vector3.UnitY);
        if (side.LengthSquared() < 0.01f)
            side = Vector3.Cross(tan[0], MathF.Abs(tan[0].Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX);
        side = Vector3.Normalize(side);
        // Spacing, and which way each link runs and where it lies: in the order their ends come across the run, so they don't cross.
        var pitch = 0f;
        Span<float> rank = stackalloc float[LoomMaxLinks];
        Span<bool> back = stackalloc bool[LoomMaxLinks];
        Span<int> order = stackalloc int[LoomMaxLinks];
        for (var i = 0; i < links.Length; i++)
        {
            var k = links[i];
            if (k < 0 || k >= c.RouteCount)
                return "that link is gone";
            for (var o = 0; o < i; o++)
                if (links[o] == k)
                    return "a link can only be in the bundle once";
            if (LoomRefuses(c, k) is { } refused)
                return refused;
            pitch = MathF.Max(pitch, 2 * Radius(c.Routes[k].Type) + LoomGap);
            var a = RestPoint(c, k, -1);
            var b = RestPoint(c, k, c.Routes[k].Count);
            back[i] = Vector3.Distance(a, g[0]) + Vector3.Distance(b, g[n - 1]) > Vector3.Distance(a, g[n - 1]) + Vector3.Distance(b, g[0]);
            rank[i] = Vector3.Dot((back[i] ? b : a) - g[0], side);
            var at = i;
            while (at > 0 && rank[order[at - 1]] > rank[i])
            {
                order[at] = order[at - 1];
                at--;
            }
            order[at] = i;
        }
        Span<Vector3> run = stackalloc Vector3[LoomMaxGuide];
        Span<int> ht = stackalloc int[LoomMaxGuide];
        for (var i = 0; i < links.Length; i++)
        {
            var li = order[i];
            var k = links[li];
            var off = (i - (links.Length - 1) * 0.5f) * pitch;
            var s = side;
            run[0] = g[0] + s * off;
            for (var j = 1; j < n; j++)
            {
                if (j == n - 1)
                    run[j] = g[j] + s * off;
                else
                {
                    var mitre = Vector3.Normalize(tan[j - 1] + tan[j]);
                    run[j] = g[j] + s * off - tan[j - 1] * (off * Vector3.Dot(s, mitre) / Vector3.Dot(tan[j - 1], mitre));
                    s = Vector3.Normalize(Vector3.Transform(s, Turn(tan[j - 1], tan[j])));
                }
            }
            if (back[li])
                run[..n].Reverse();
            for (var j = 0; j < n; j++)
                ht[j] = guide[back[li] ? n - 1 - j : j].Host;
            var why = LoomLink(c, k, run[..n], ht[..n], plan, plan.Count);
            if (why != null)
                return links.Length > 1 ? $"{Name(c.Routes[k].Type)} from {c.DefOf(c.Routes[k].Source).Name}: {why}" : why;
            plan.Links[plan.Count++] = k;
        }
        return null;
    }

    private static Quaternion Turn(Vector3 from, Vector3 to)
    {
        var axis = Vector3.Cross(from, to);
        var sin = axis.Length();
        return sin < 1e-5f ? Quaternion.Identity : Quaternion.CreateFromAxisAngle(axis / sin, MathF.Atan2(sin, Vector3.Dot(from, to)));
    }

    /// <summary>One link of a bundle: its end, a way to its run, the run, a way to its other end, all through the path rule.</summary>
    private static string? LoomLink(Construct c, int k, ReadOnlySpan<Vector3> run, ReadOnlySpan<int> runHost, LoomPlan plan, int slot)
    {
        ref var r = ref c.Routes[k];
        var type = r.Type;
        var junction = r.Parent != 255;
        var fromPort = junction ? (byte)255 : r.FromPort;
        Span<Vector3> pts = stackalloc Vector3[LoomPoints];
        Span<int> hosts = stackalloc int[LoomPoints];
        Span<Vector3> way = stackalloc Vector3[LoomStub];
        pts[0] = Point(c, k, -1, out _);
        hosts[0] = junction ? HostOf(c, r.Parent, r.ParentNode) : r.From;
        var startRest = RestPoint(c, k, -1);
        var endRest = RestPoint(c, k, r.Count);
        // The way to the run: the first of the ways that passes, with the run after it (the link still open at the far end).
        string? why = null;
        var count = 1;
        for (var w = 0; w < LoomWays; w++)
        {
            count = 1;
            if (LoomWay(startRest, run[0], w, way) is not { } legs)
                continue;
            for (var i = 0; i < legs; i++)
                count = LoomPut(c, pts, hosts, count, way[i], -1);
            for (var i = 0; i < run.Length; i++)
                count = LoomPut(c, pts, hosts, count, run[i], runHost[i]);
            why = PathRule(c, type, pts[..count], hosts[..count], junction, fromPort, false, 255);
            if (why == null)
                break;
        }
        if (why != null)
            return why;
        var body = count;
        for (var w = 0; w < LoomWays; w++)
        {
            count = body;
            if (LoomWay(run[^1], endRest, w, way) is not { } legs)
                continue;
            for (var i = 0; i < legs; i++)
                count = LoomPut(c, pts, hosts, count, way[i], -1);
            pts[count] = Point(c, k, r.Count, out _);
            hosts[count++] = r.To;
            why = PathRule(c, type, pts[..count], hosts[..count], junction, fromPort, true, r.ToPort);
            if (why == null)
                break;
        }
        if (why != null)
            return why;
        var nodes = plan.Nodes[slot];
        plan.Counts[slot] = count - 2;
        for (var i = 1; i + 1 < count; i++)
            nodes[i - 1] = new CRouteNode { Pos = pts[i], Host = (ushort)hosts[i] };
        return null;
    }

    private const int LoomWays = 7;

    private static int LoomPut(Construct c, Span<Vector3> pts, Span<int> hosts, int n, Vector3 rest, int host)
    {
        if (host < 0)
            host = NearestPart(c, rest);
        pts[n] = ConstructWorld.Quantise(FromRest(c, c.Parts[host].Seg, rest));
        hosts[n] = host;
        return n + 1;
    }

    /// <summary>Way <paramref name="w"/> from one point to another: 0 straight, then square, each order of the three axes. How many points it adds between them, or null when it is no new way.</summary>
    private static int? LoomWay(Vector3 a, Vector3 b, int w, Span<Vector3> way)
    {
        if (w == 0)
            return 0;
        // (a run shorter than a segment can be stays in the last leg: at that size it bends nothing that matters)
        var order = (w - 1) switch { 0 => (0, 1, 2), 1 => (0, 2, 1), 2 => (1, 0, 2), 3 => (1, 2, 0), 4 => (2, 0, 1), _ => (2, 1, 0) };
        var n = 0;
        var at = a;
        foreach (var axis in new[] { order.Item1, order.Item2 })
        {
            if (MathF.Abs(b[axis] - at[axis]) < MinSegment)
                continue;
            at[axis] = b[axis];
            if (Vector3.Distance(at, b) >= MinSegment)
                way[n++] = at;
        }
        return n == 0 ? null : n;
    }

    /// <summary>Puts <paramref name="nodes"/> in place of link <paramref name="route"/>'s nodes (the Loom): its ends stay, every segment is new.</summary>
    public static void Replace(ConstructWorld? world, Construct c, int route, ReadOnlySpan<CRouteNode> nodes)
    {
        var delta = nodes.Length - c.Routes[route].Count;
        c.ReserveRoutes(c.RouteCount, c.RouteNodeCount + Math.Max(delta, 0));
        ref var r = ref c.Routes[route];
        var end = r.Start + r.Count;
        Array.Copy(c.RouteNodes, end, c.RouteNodes, end + delta, c.RouteNodeCount - end);
        for (var i = 0; i < nodes.Length; i++)
            c.RouteNodes[r.Start + i] = new CRouteNode { Pos = ConstructWorld.Quantise(nodes[i].Pos), Host = nodes[i].Host, Health = Health(r.Type) };
        c.RouteNodeCount += delta;
        r.Count = (ushort)nodes.Length;
        r.EndHealth = Health(r.Type);
        for (var k = 0; k < c.RouteCount; k++)
            if (k != route && c.Routes[k].Start >= end && c.Routes[k].Start > r.Start)
                c.Routes[k].Start = (ushort)(c.Routes[k].Start + delta);
        Changed(world, c);
    }
}
