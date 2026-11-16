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
/// searches nothing; nothing here runs per frame or per tick. A <c>"conduit"</c> ship's <c>"links"</c> with no nodes (chutes, pipes,
/// cables) are laid the same way, after its wires, and a conduit may pass through a thin wall, deck or hull plate where it must
/// (<see cref="RouteSearch.PierceMax"/>; power-and-links.md, "Ship links and walls").
/// </summary>
public static class ConstructConduit
{
    /// <summary>
    /// The searches tried in turn, the cost a metre off every surface and the lattice's gap: a dear one keeps it on the walls, a cheaper one
    /// finds a way where the dear one's search gives up (the weaker its guess, the more it searches), and the tool's own 5 cm lattice is smaller still.
    /// </summary>
    private static readonly (float Air, byte Gap, bool Exact)[] Tries = [(6f, 0, false), (3f, 0, false), (6f, 5, false)]; // (a building's)
    /// <summary>
    /// A conduit ship's: open air dear first (a metre of it dearer than several walls passed and every bend round them, so a
    /// link never cuts across a room or a corridor to save a run along its walls), searched again by the true cost to the goal
    /// when the plain guess's route still runs in the open (<see cref="OpenMax"/>), then the building's, then one cheap in the air.
    /// </summary>
    private static readonly (float Air, byte Gap, bool Exact)[] ShipTries = [(AirShip, 0, false), (AirShip, 0, true), (6f, 0, false), (3f, 0, false), (6f, 5, false), (1.5f, 8, false)];
    public const float AirShip = 30f;
    public const float ExactBudgetMs = 1000f; // the true cost's search, for the few routes the plain guess left in the open (its pass over the lattice comes first), as the blueprints load
    public const float BudgetMs = 250f; // one search at most, as the blueprints load: past it the wire stays straight (concealed)
    public const float PlayMs = 20f; // a whole build's searching at most in play (a blueprint the load didn't route: a mod's, one built while it loads); past it the rest stay straight and are tried again next build
    private static RouteSearch? _search; // (its tables, about 19 MB, are made for the load's routing and let go after it: Release)
    private static bool _loading;
    private static readonly ConditionalWeakTable<ConstructBuild.Blueprint, CRouteNode[]?[]> Cache = new();
    private static readonly object Gate = new();
    public static int Routed, Kept, Failed; // (since start: the log line, a check)
    public static double Ms;

    /// <summary>
    /// Lays blueprint <paramref name="bp"/>'s wire <paramref name="k"/> (parts <paramref name="from"/> to <paramref name="to"/>,
    /// into <paramref name="toPort"/>) as conduit on <paramref name="c"/>. False: not laid (the caller adds the straight wire).
    /// </summary>
    public static bool TryLay(ConstructWorld world, Construct c, ConstructBuild.Blueprint bp, int k, int from, int to, byte toPort, long deadline) =>
        TryLay(world, c, bp, k, RouteType.Data, from, 255, to, toPort, deadline);

    /// <summary>
    /// Lays one of blueprint <paramref name="bp"/>'s links as conduit: entry <paramref name="k"/> (its wires first, then its <c>"links"</c>
    /// given with no nodes: chutes, pipes, power and data cables), a link of <paramref name="type"/> from part <paramref name="from"/>'s
    /// port <paramref name="fromPort"/> to <paramref name="to"/>'s <paramref name="toPort"/>. A chute or pipe leaves and meets its ports
    /// along their faces, as the tool's do; a cable any way. False: not laid (the caller lays it straight).
    /// </summary>
    public static bool TryLay(ConstructWorld world, Construct c, ConstructBuild.Blueprint bp, int k, RouteType type, int from, byte fromPort, int to, byte toPort, long deadline)
    {
        if (!Routable(type))
            return false;
        lock (Gate)
        {
            var known = Cache.GetValue(bp, b => new CRouteNode[]?[Entries(b)]);
            if (k >= known.Length)
                return false;
            if (known[k] is { } kept)
            {
                if (kept.Length == 0 || !Fits(world, c, bp.Conduit, type, from, fromPort, to, toPort, kept))
                    return false; // (it failed before, or this copy differs: straight)
                Kept++;
                return Add(world, c, type, from, fromPort, to, toPort, kept);
            }
            if (Stopwatch.GetTimestamp() >= deadline)
                return false; // (this build's time is up: straight this once, tried again on the next)
            var sw = Stopwatch.StartNew();
            var nodes = Find(world, c, bp.Conduit, type, from, fromPort, to, toPort, deadline, out var why, out var outOfTime);
            Ms += sw.Elapsed.TotalMilliseconds;
            if (nodes == null && outOfTime)
                return false; // (not kept: a busy moment isn't the blueprint's fault)
            if (outOfTime)
            {
                Routed++;
                return Add(world, c, type, from, fromPort, to, toPort, nodes!); // (laid, not kept: a later try ran out of time before it could judge this one)
            }
            known[k] = nodes ?? [];
            if (nodes == null)
            {
                Failed++;
                Console.WriteLine(k < bp.Wires.Count
                    ? $"[conduit] {bp.Name}: wire {k} ({c.DefOf(from).Name} -> {c.DefOf(to).Name}) stays straight, in the walls: {why}"
                    : $"[conduit] {bp.Name}: link {k - bp.Wires.Count} ({ConstructRoutes.Name(type)}, {c.DefOf(from).Name} -> {c.DefOf(to).Name}) stays straight: {why}");
                return false;
            }
            Routed++;
            return Add(world, c, type, from, fromPort, to, toPort, nodes);
        }
    }

    /// <summary>The kinds laid as conduit: everything the square search routes but a conveyor (a bed has its own turns' rules) and a shaft.</summary>
    public static bool Routable(RouteType type) => type is RouteType.Data or RouteType.Power or RouteType.Pipe or RouteType.Chute;

    /// <summary>A blueprint's entries: its wires, then its links (only those with no nodes are laid as conduit).</summary>
    public static int Entries(ConstructBuild.Blueprint bp) => bp.Wires.Count + bp.Links.Count;

    /// <summary>
    /// Blueprint <paramref name="bp"/>'s link <paramref name="k"/> is laid as conduit on a pre-built: it names no nodes of its own, isn't a
    /// branch nor has one (a branch's segment and point were measured on the straight line), and is of a kind that can be.
    /// </summary>
    public static bool Lays(ConstructBuild.Blueprint bp, int k)
    {
        var s = bp.Links[k];
        if (s.Nodes.Count > 0 || s.Branch >= 0 || s.Laid || !Routable(s.Type))
            return false;
        foreach (var l in bp.Links)
            if (l.Branch == k)
                return false;
        return true;
    }

    private static CRouteNode[]? Find(ConstructWorld world, Construct c, bool ship, RouteType type, int from, byte fromPort, int to, byte toPort, long deadline, out string? why, out bool outOfTime)
    {
        why = null;
        outOfTime = false;
        CRouteNode[]? kept = null;
        var keptAir = 0f;
        foreach (var (air, gap, exact) in !ship ? Tries : _loading ? ShipTries : ShipTries.AsSpan(2)) // (in play, with 20 ms for a whole build, a ship skips the dear tries: they would spend it all)
        {
            var nodes = Find(world, c, ship, type, from, fromPort, to, toPort, air, gap, exact, deadline, out why, out var late, out var open);
            outOfTime |= late;
            if (exact && kept != null)
            {
                outOfTime = late; // (laid but not kept when the true cost's search ran out of time to judge it: tried again next start)
                return nodes != null && open < keptAir ? nodes : kept; // (the plain guess's route, unless the true cost's ran less in the open)
            }
            if (nodes == null)
                continue; // (a plain search that failed, even refused by the check, is tried with the true cost too: its route differs)
            if (ship && !exact && air >= AirShip && open > OpenMax)
            {
                // (the plain guess wanders on a big ship and settles for a run across a room: the true cost's search, the dearer pass, judges)
                kept = nodes;
                keptAir = open;
                continue;
            }
            outOfTime = false;
            return nodes;
        }
        return kept;
    }

    /// <summary>How much of a route (m) runs in the open: over 7 cm past its own radius from every part, sampled every 10 cm (as <c>--conduit-check</c> reports it).</summary>
    public static float OpenLength(Construct c, RouteType type, int from, byte fromPort, int to, byte toPort, ReadOnlySpan<CRouteNode> nodes)
    {
        var open = 0f;
        var prev = ConstructRoutes.PortPoint(c, from, fromPort, type);
        var gap = ConstructRoutes.Radius(type) + 0.07f;
        for (var i = 0; i <= nodes.Length; i++)
        {
            var q = i < nodes.Length ? nodes[i].Pos : ConstructRoutes.PortPoint(c, to, toPort, type);
            var l = System.Numerics.Vector3.Distance(prev, q);
            var steps = Math.Max(1, (int)(l / 0.1f));
            for (var k = 0; k < steps; k++)
                if (ConstructRoutes.SignedDistance(c, System.Numerics.Vector3.Lerp(prev, q, (k + 0.5f) / steps), -1, -1, out _) > gap)
                    open += l / steps;
            prev = q;
        }
        return open;
    }

    /// <summary>A ship's route found by the plain guess that runs more than this in the open (m) is searched again with the true cost to the goal.</summary>
    public const float OpenMax = 0.3f;

    private static CRouteNode[]? Find(ConstructWorld world, Construct c, bool ship, RouteType type, int from, byte fromPort, int to, byte toPort, float air, byte gap, bool exact, long deadline, out string? why, out bool late, out float open)
    {
        open = 0f;
        var search = _search ??= new RouteSearch();
        search.Clear();
        var ask = new RouteAsk { World = world, C = c, Type = type, From = from, FromPort = fromPort, Parent = 255, Nodes = [], Count = 0, To = to, ToPort = toPort, Conduit = air, ConduitGap = gap, Exact = exact };
        var end = Math.Min(deadline, Stopwatch.GetTimestamp() + (long)((exact ? ExactBudgetMs : BudgetMs) * Stopwatch.Frequency / 1000));
        RouteSearch.Result r;
        do
            r = search.Poll(ask, MathF.Max(1f, MathF.Min(50f, (float)((end - Stopwatch.GetTimestamp()) * 1000.0 / Stopwatch.Frequency))));
        while (r.State == RouteSearchState.Busy && Stopwatch.GetTimestamp() < end);
        late = r.State == RouteSearchState.Busy;
        if (late)
        {
            search.Cancel();
            why = "out of time";
            return null;
        }
        if (r.State != RouteSearchState.Found || r.Count == 0)
        {
            why = r.State == RouteSearchState.Found ? "the straight line is the route" : r.Why ?? "no route";
            return null;
        }
        var nodes = r.Nodes.AsSpan(0, r.Count).ToArray();
        open = OpenLength(c, type, from, fromPort, to, toPort, nodes);
        why = OnMoving(c, ship, from, to, nodes) ? "it would ride a moving part" : ConstructRoutes.Check(world, c, type, from, fromPort, 255, 0, default, nodes, to, toPort, straight: r.Pierced);
        return why == null ? nodes : null;
    }

    /// <summary>When a build's searching must stop: as the blueprints load, a search's own budget only; in play, <see cref="PlayMs"/> from now.</summary>
    public static long Deadline() => _loading ? long.MaxValue : Stopwatch.GetTimestamp() + (long)(PlayMs * Stopwatch.Frequency / 1000);

    /// <summary>The blueprints' load builds each one with wires, so their routes are found then, not in a server tick (<see cref="ConstructBuild.LoadBlueprints"/>).</summary>
    public static void BeginLoad() => _loading = true;

    /// <summary>A blueprint with something a pre-built's build lays as conduit: wires, or (a <c>"conduit"</c> ship's) links with no nodes.</summary>
    public static bool Any(ConstructBuild.Blueprint bp)
    {
        if (bp.Wires.Count > 0)
            return true;
        if (bp.Conduit) // (a ship's links; an anchored building's chutes and pipes keep the routes their blueprint gives)
            for (var k = 0; k < bp.Links.Count; k++)
                if (Lays(bp, k))
                    return true;
        return false;
    }

    /// <summary>A blueprint whose wires will be laid as conduit: it has wires and stands on a foundation or a frozen base (the load builds it to find them).</summary>
    public static bool Wanted(ConstructBuild.Blueprint bp)
    {
        if (!Any(bp))
            return false;
        if (bp.FrozenBase || bp.Conduit)
            return true;
        foreach (var st in bp.Steps)
            if (ConstructParts.Find(st.Part) is { Role: PartRole.Foundation })
                return true;
        return false;
    }

    // ---- Dev builds: the routes found are kept on disk with the blueprint check's cache (BlueprintCheckCache.cs), so a blueprint that
    // passed unchanged isn't built at every start just to route its wires (a first build costs seconds of JIT in a headless run).

    public static string File => Path.Combine(AppFolders.Exe, "Cache", "conduit-routes.txt");
    private static Dictionary<string, string>? _disk;
    /// <summary>Bumped whenever the router lays a blueprint differently (its costs, its tries), so a disk cache from before is laid again.</summary>
    private const int RouterVersion = 3; // 2: ships price open air dearly (AirShip); 3: a route in the open is searched again by the true cost (RouteAsk.Exact)
    private static string DiskKey(string text) => $"{BlueprintCheckCache.KeyOf(text)}.r{RouterVersion}";
    private const string NotWanted = "none"; // (a blueprint with wires that isn't a pre-built: kept so the next start needn't load the parts to find that out)
    private static bool _diskDirty;

    /// <summary>Takes blueprint <paramref name="bp"/>'s routes (from its <paramref name="text"/>) from the disk cache. False: not there, build it.</summary>
    public static bool Restore(ConstructBuild.Blueprint bp, string text)
    {
        if (!Disk().TryGetValue(DiskKey(text), out var line))
            return false;
        if (line == NotWanted)
            return true; // (a vehicle's: nothing to route)
        var wires = line.Split('|');
        if (wires.Length != Entries(bp))
            return false;
        var known = new CRouteNode[]?[wires.Length];
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        try
        {
            for (var k = 0; k < wires.Length; k++)
            {
                if (wires[k].Length == 0)
                    continue; // (not tried: a build that ran out of time; the next build tries)
                if (wires[k] == "-")
                {
                    known[k] = [];
                    continue;
                }
                var pts = wires[k].Split(';');
                var nodes = new CRouteNode[pts.Length];
                for (var i = 0; i < pts.Length; i++)
                {
                    var f = pts[i].Split(',');
                    nodes[i] = new CRouteNode { Pos = new System.Numerics.Vector3(float.Parse(f[0], inv), float.Parse(f[1], inv), float.Parse(f[2], inv)), Host = ushort.Parse(f[3], inv) };
                }
                known[k] = nodes;
            }
        }
        catch (Exception ex) when (ex is FormatException or IndexOutOfRangeException or OverflowException)
        {
            return false;
        }
        lock (Gate)
            Cache.AddOrUpdate(bp, known);
        return true;
    }

    /// <summary>Keeps blueprint <paramref name="bp"/>'s routes, as its build found them, for the next start (or that it has none to find). Always true.</summary>
    public static bool Remember(ConstructBuild.Blueprint bp, string text)
    {
        if (!Any(bp))
            return true;
        CRouteNode[]?[]? known = null;
        if (Wanted(bp))
            lock (Gate)
                if (!Cache.TryGetValue(bp, out known))
                    return true;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var sb = new System.Text.StringBuilder(known == null ? NotWanted : "");
        for (var k = 0; known != null && k < known.Length; k++)
        {
            if (k > 0)
                sb.Append('|');
            if (known[k] is not { } nodes)
                continue;
            if (nodes.Length == 0)
                sb.Append('-');
            for (var i = 0; i < nodes.Length; i++)
                sb.Append(i > 0 ? ";" : "").Append(nodes[i].Pos.X.ToString("R", inv)).Append(',').Append(nodes[i].Pos.Y.ToString("R", inv)).Append(',')
                    .Append(nodes[i].Pos.Z.ToString("R", inv)).Append(',').Append(nodes[i].Host.ToString(inv));
        }
        var line = sb.ToString();
        var key = DiskKey(text);
        if (!Disk().TryGetValue(key, out var old) || old != line)
        {
            _disk![key] = line;
            _diskDirty = true;
        }
        return true;
    }

    /// <summary>Writes the routes kept if they changed. A folder that can't be written just means no file.</summary>
    public static void SaveDisk()
    {
        if (!_diskDirty || _disk == null)
            return;
        _diskDirty = false;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(File)!);
            var tmp = File + "." + Environment.ProcessId + ".tmp";
            System.IO.File.WriteAllLines(tmp, _disk.OrderBy(e => e.Key, StringComparer.Ordinal).Select(e => e.Key + " " + e.Value).Prepend(BlueprintCheckCache.CurrentStamp));
            System.IO.File.Move(tmp, File, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static Dictionary<string, string> Disk()
    {
        if (_disk != null)
            return _disk;
        _disk = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            if (System.IO.File.Exists(File) && System.IO.File.ReadAllLines(File) is { Length: > 0 } lines && lines[0] == BlueprintCheckCache.CurrentStamp)
                foreach (var l in lines.Skip(1))
                    if (l.IndexOf(' ') is var sp and > 0)
                        _disk[l[..sp]] = l[(sp + 1)..];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
        return _disk;
    }

    /// <summary>The load is done: the search's tables go (a later build with nothing kept makes them again).</summary>
    public static void EndLoad()
    {
        _loading = false;
        lock (Gate)
            _search = null;
    }

    private static bool Fits(ConstructWorld world, Construct c, bool ship, RouteType type, int from, byte fromPort, int to, byte toPort, CRouteNode[] nodes) =>
        !OnMoving(c, ship, from, to, nodes) && (ConstructRoutes.Check(world, c, type, from, fromPort, 255, 0, default, nodes, to, toPort) == null
            || ConstructRoutes.Check(world, c, type, from, fromPort, 255, 0, default, nodes, to, toPort, straight: true) == null); // (one through a wall: its ends and spans, as it was found)

    /// <summary>
    /// A node on a part gone, or on one that moves apart from both of the link's ends (a gate's leaf the wire only passes). A link whose
    /// ends lie across a joint (a turret's guns, a drill on its ram) runs on the fixed side to the joint and crosses it there once,
    /// in one flexing section, as the tool's own routes do (power-and-links.md, "Joints and constructs"); on a conduit ship only: a
    /// building's wire keeps every node with its source (a wall's wire to a gate's leaf stays straight).
    /// </summary>
    private static bool OnMoving(Construct c, bool ship, int from, int to, CRouteNode[] nodes)
    {
        var a = c.MoveSeg(c.Parts[from].Seg);
        var b = ship ? c.MoveSeg(c.Parts[to].Seg) : a;
        foreach (var n in nodes)
            if (n.Host >= c.PartCount || c.MoveSeg(c.Parts[n.Host].Seg) is var m && m != a && m != b)
                return true;
        return false;
    }

    private static bool Add(ConstructWorld world, Construct c, RouteType type, int from, byte fromPort, int to, byte toPort, CRouteNode[] nodes)
    {
        if (c.RouteCount >= Construct.MaxRoutes || world.TotalRoutes >= world.WireCap)
            return false;
        return ConstructRoutes.Add(world, c, type, from, fromPort, 255, 0, default, nodes, to, toPort) >= 0;
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
            if (!Any(bp) || names.Length > 0 && !names.Any(n => n.Equals(bp.Name, StringComparison.OrdinalIgnoreCase)))
                continue;
            var world = new ConstructWorld();
            if (ConstructBuild.Build(world, bp, System.Numerics.Vector3.Zero, System.Numerics.Quaternion.Identity, out var why) is not { } c)
            {
                Console.WriteLine($"[conduit] {bp.Name}: doesn't build: {why}");
                bad++;
                continue;
            }
            if (!c.Anchored && !bp.Conduit && names.Length == 0)
                continue;
            Console.WriteLine($"[conduit] {bp.Name}: {c.PartCount} parts");
            for (var r = 0; r < c.RouteCount; r++)
            {
                ref var rt = ref c.Routes[r];
                if (!Routable(rt.Type) || rt.Parent != 255)
                    continue;
                var a = ConstructRoutes.PortPoint(c, rt.From, rt.FromPort, rt.Type);
                var b = ConstructRoutes.PortPoint(c, rt.To, rt.ToPort, rt.Type);
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
                        if (ConstructRoutes.SignedDistance(c, m, -1, -1, out _) > ConstructRoutes.Radius(rt.Type) + 0.07f)
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
                Console.WriteLine($"  {(rt.Type == RouteType.Data ? "" : ConstructRoutes.Name(rt.Type) + " ")}{c.DefOf(rt.From).Name}#{rt.From} -> {c.DefOf(rt.To).Name}#{rt.To}: {(rt.Count == 0 ? "straight" : $"{rt.Count} nodes")}, {len:0.0} m (straight {System.Numerics.Vector3.Distance(a, b):0.0} m), {open:0.0} m in the open");
            }
        }
        Console.WriteLine($"[conduit] {Routed} links routed, {Failed} left straight, {Ms:0} ms of searching; {Kept} copies from the cache");
        return bad;
    }
}
