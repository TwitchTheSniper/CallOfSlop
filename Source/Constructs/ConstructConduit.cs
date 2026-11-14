using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace CallOfSlop;

/// <summary>
/// A pre-built's wiring laid as conduit (docs/sandbox/constructs/power-and-links.md, "Conduit in pre-builts"): a blueprint's
/// <c>"wires"</c> on an anchored build (a colony building's light switch to its lamps) are routed once, as the blueprint is built,
/// by the linking tool's own auto-router in its conduit mode (<see cref="RouteAsk.Conduit"/>: a metre off every surface costs
/// six times one along it), so the cable runs along walls, floors and ceilings, round corners and through doorways
/// instead of across the room. The route is an ordinary routed link with nodes, checked with the server's rule, so it is saved,
/// sent and drawn as any other. A wire the router can't lay stays the straight one (drawn only with the linking tool out:
/// <see cref="RouteRender.Concealed"/>), with a log line. Routes are kept per blueprint and wire, so a second copy of a building
/// searches nothing; nothing here runs per frame or per tick.
/// </summary>
public static class ConstructConduit
{
    /// <summary>
    /// The searches tried in turn, the cost a metre off every surface and the lattice's gap: a dear one keeps it on the walls, a cheaper one
    /// finds a way where the dear one's search gives up (the weaker its guess, the more it searches), and the tool's own 5 cm lattice is smaller still.
    /// </summary>
    private static readonly (float Air, byte Gap)[] Tries = [(6f, 0), (3f, 0), (6f, 5)];
    public const float BudgetMs = 250f; // one wire's search at most: past it the wire stays straight (concealed)
    private static readonly RouteSearch Search = new(); // (its tables are made on the first anchored build with wires: about 19 MB)
    private static readonly ConditionalWeakTable<ConstructBuild.Blueprint, CRouteNode[]?[]> Cache = new();
    private static readonly object Gate = new();
    public static int Routed, Kept, Failed; // (since start: the log line, a check)
    public static double Ms;

    /// <summary>
    /// Lays blueprint <paramref name="bp"/>'s wire <paramref name="k"/> (parts <paramref name="from"/> to <paramref name="to"/>,
    /// into <paramref name="toPort"/>) as conduit on <paramref name="c"/>. False: not laid (the caller adds the straight wire).
    /// </summary>
    public static bool TryLay(ConstructWorld world, Construct c, ConstructBuild.Blueprint bp, int k, int from, int to, byte toPort)
    {
        var ms = c.MoveSeg(c.Parts[from].Seg);
        if (ms != c.MoveSeg(c.Parts[to].Seg))
            return false; // (across a joint, a turret's or a gate's: it would flex in the open, so it stays in the walls)
        lock (Gate)
        {
            var known = Cache.GetValue(bp, b => new CRouteNode[]?[b.Wires.Count]);
            if (k >= known.Length)
                return false;
            if (known[k] is { } kept)
            {
                if (kept.Length == 0 || !Fits(world, c, from, to, toPort, kept))
                    return false; // (it failed before, or this copy differs: straight)
                Kept++;
                return Add(world, c, from, to, toPort, kept);
            }
            var sw = Stopwatch.StartNew();
            var nodes = Find(world, c, from, to, toPort, out var why);
            Ms += sw.Elapsed.TotalMilliseconds;
            known[k] = nodes ?? [];
            if (nodes == null)
            {
                Failed++;
                Console.WriteLine($"[conduit] {bp.Name}: wire {k} ({c.DefOf(from).Name} -> {c.DefOf(to).Name}) stays straight, in the walls: {why}");
                return false;
            }
            Routed++;
            return Add(world, c, from, to, toPort, nodes);
        }
    }

    private static CRouteNode[]? Find(ConstructWorld world, Construct c, int from, int to, byte toPort, out string? why)
    {
        why = null;
        foreach (var (air, gap) in Tries)
            if (Find(world, c, from, to, toPort, air, gap, out why) is { } nodes)
                return nodes;
        return null;
    }

    private static CRouteNode[]? Find(ConstructWorld world, Construct c, int from, int to, byte toPort, float air, byte gap, out string? why)
    {
        Search.Clear();
        var ask = new RouteAsk { World = world, C = c, Type = RouteType.Data, From = from, FromPort = 255, Parent = 255, Nodes = [], Count = 0, To = to, ToPort = toPort, Conduit = air, ConduitGap = gap };
        var end = Stopwatch.GetTimestamp() + (long)(BudgetMs * Stopwatch.Frequency / 1000);
        RouteSearch.Result r;
        do
            r = Search.Poll(ask, 50f);
        while (r.State == RouteSearchState.Busy && Stopwatch.GetTimestamp() < end);
        if (r.State == RouteSearchState.Busy)
        {
            Search.Cancel();
            why = $"no route in {BudgetMs:0} ms";
            return null;
        }
        if (r.State != RouteSearchState.Found || r.Count == 0)
        {
            why = r.State == RouteSearchState.Found ? "the straight line is the route" : r.Why ?? "no route";
            return null;
        }
        var nodes = r.Nodes.AsSpan(0, r.Count).ToArray();
        why = OnMoving(c, from, nodes) ? "it would ride a moving part" : ConstructRoutes.Check(world, c, RouteType.Data, from, 255, 255, 0, default, nodes, to, toPort);
        return why == null ? nodes : null;
    }

    private static bool Fits(ConstructWorld world, Construct c, int from, int to, byte toPort, CRouteNode[] nodes) =>
        !OnMoving(c, from, nodes) && ConstructRoutes.Check(world, c, RouteType.Data, from, 255, 255, 0, default, nodes, to, toPort) == null;

    /// <summary>A node on a part gone, or on one that moves apart from the wire's output (a gate's leaf).</summary>
    private static bool OnMoving(Construct c, int from, CRouteNode[] nodes)
    {
        var ms = c.MoveSeg(c.Parts[from].Seg);
        foreach (var n in nodes)
            if (n.Host >= c.PartCount || c.MoveSeg(c.Parts[n.Host].Seg) != ms)
                return true;
        return false;
    }

    private static bool Add(ConstructWorld world, Construct c, int from, int to, byte toPort, CRouteNode[] nodes)
    {
        if (c.RouteCount >= Construct.MaxRoutes || world.TotalRoutes >= world.WireCap)
            return false;
        return ConstructRoutes.Add(world, c, RouteType.Data, from, 255, 255, 0, default, nodes, to, toPort) >= 0;
    }

    /// <summary>
    /// <c>--conduit-check [NAME,NAME...]</c>: builds each anchored blueprint with wires (or those named) and says how each wire was laid:
    /// its nodes, its length against the straight line, and how much of it runs in the open (over 8 cm from every part). To the console.
    /// </summary>
    public static int Cli(string[] args)
    {
        var at = Array.FindIndex(args, a => a.Equals("--conduit-check", StringComparison.OrdinalIgnoreCase));
        var names = at >= 0 && at + 1 < args.Length && !args[at + 1].StartsWith("--") ? args[at + 1].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) : [];
        var bad = 0;
        foreach (var bp in ConstructBuild.Blueprints)
        {
            if (bp.Wires.Count == 0 || names.Length > 0 && !names.Any(n => n.Equals(bp.Name, StringComparison.OrdinalIgnoreCase)))
                continue;
            var world = new ConstructWorld();
            if (ConstructBuild.Build(world, bp, System.Numerics.Vector3.Zero, System.Numerics.Quaternion.Identity, out var why) is not { } c)
            {
                Console.WriteLine($"[conduit] {bp.Name}: doesn't build: {why}");
                bad++;
                continue;
            }
            if (!c.Anchored && names.Length == 0)
                continue;
            Console.WriteLine($"[conduit] {bp.Name}: {c.PartCount} parts");
            for (var r = 0; r < c.RouteCount; r++)
            {
                ref var rt = ref c.Routes[r];
                if (rt.Type != RouteType.Data || rt.Parent != 255)
                    continue;
                var a = ConstructRoutes.PortPoint(c, rt.From, rt.FromPort, RouteType.Data);
                var b = ConstructRoutes.PortPoint(c, rt.To, rt.ToPort, RouteType.Data);
                float len = 0f, open = 0f;
                var prev = ConstructRoutes.RestPoint(c, r, -1);
                for (var i = 0; i <= rt.Count; i++)
                {
                    var q = ConstructRoutes.RestPoint(c, r, i);
                    var l = System.Numerics.Vector3.Distance(prev, q);
                    len += l;
                    var steps = Math.Max(1, (int)(l / 0.1f));
                    for (var k = 0; k < steps; k++)
                    {
                        var m = System.Numerics.Vector3.Lerp(prev, q, (k + 0.5f) / steps);
                        if (ConstructRoutes.SignedDistance(c, m, -1, -1, out _) > 0.08f)
                            open += l / steps;
                    }
                    prev = q;
                }
                if (args.Contains("--trace"))
                    for (var i = -1; i <= rt.Count; i++)
                    {
                        var q = ConstructRoutes.RestPoint(c, r, i);
                        var sd = ConstructRoutes.SignedDistance(c, q, -1, -1, out var near);
                        Console.WriteLine($"    {q.X:0.00},{q.Y:0.00},{q.Z:0.00}: {sd:0.000} m off {(near >= 0 ? c.DefOf(near).Name : "-")}");
                    }
                Console.WriteLine($"  {c.DefOf(rt.From).Name}#{rt.From} -> {c.DefOf(rt.To).Name}#{rt.To}: {(rt.Count == 0 ? "straight" : $"{rt.Count} nodes")}, {len:0.0} m (straight {System.Numerics.Vector3.Distance(a, b):0.0} m), {open:0.0} m in the open");
            }
        }
        Console.WriteLine($"[conduit] {Routed} wires routed, {Failed} left straight, {Ms:0} ms of searching; {Kept} copies from the cache");
        return bad;
    }
}
