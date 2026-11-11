using System.Numerics;
using System.Text;

namespace CallOfSlop;

/// <summary>
/// <c>--route-ends [BLUEPRINT]</c>: how the auto-router's routes leave and arrive (docs/sandbox/constructs/power-and-links.md,
/// "Auto-routing"). Builds the blueprint (default the Production Showcase) and routes, with the linking tool's own search,
/// every output to every input of its type in reach, and every input from a waypoint laid a little way out in front of it (the
/// player's last node before the final connection: on the port's line, a few cm off it, and well off it). For each route it
/// counts the turns, and the turns and short segments within 2 m of either end. The table goes to <c>route-ends.txt</c> next to
/// the DLL; the summary to the console.
/// </summary>
public static class RouteEndsProbe
{
    private const float Near = 2f; // "near an end"

    private struct Stats
    {
        public int Asked, Found, Failed, Straight, Bad, ShortNear, TurnyNear, Turns, Invalid, Tight, Narrowed;
        public double Ms, MaxMs;
    }

    public static int Cli(string[] args)
    {
        var at = Array.FindIndex(args, a => a.Equals("--route-ends", StringComparison.OrdinalIgnoreCase));
        var name = at >= 0 && at + 1 < args.Length && !args[at + 1].StartsWith("--") ? args[at + 1] : "Production Showcase";
        if (ConstructBuild.FindBlueprint(name) is not { } bp)
        {
            Console.WriteLine($"[route-ends] no blueprint named \"{name}\"");
            return 1;
        }
        var world = new ConstructWorld();
        if (ConstructBuild.Build(world, bp, Vector3.Zero, Quaternion.Identity, out var why) is not { } c)
        {
            Console.WriteLine($"[route-ends] {name} doesn't build: {why}");
            return 1;
        }
        var ports = new List<(int Part, ConstructRoutes.LinkPort P)>();
        var list = new ConstructRoutes.LinkPort[ConstructRoutes.MaxLinkPorts];
        for (var i = 0; i < c.PartCount; i++)
        {
            var n = ConstructRoutes.AllPorts(c, i, list);
            for (var k = 0; k < n; k++)
                if (list[k].Type != RouteType.Shaft)
                    ports.Add((i, list[k]));
        }
        var search = new RouteSearch();
        var detail = new StringBuilder();
        var byType = new Dictionary<RouteType, Stats>();
        var waypointStats = new Stats();
        var nodes = new CRouteNode[Construct.MaxNodesPerRoute];
        var sw = new System.Diagnostics.Stopwatch();

        RouteSearch.Result Run(in RouteAsk ask, ref Stats s)
        {
            search.Clear();
            sw.Restart();
            RouteSearch.Result r;
            do
                r = search.Poll(ask, 50f);
            while (r.State == RouteSearchState.Busy);
            var ms = sw.Elapsed.TotalMilliseconds;
            s.Ms += ms;
            s.MaxMs = Math.Max(s.MaxMs, ms);
            return r;
        }

        void Measure(Construct c, RouteType t, int from, byte fromPort, ReadOnlySpan<CRouteNode> pre, RouteSearch.Result r, int to, byte toPort, float width, ref Stats s, string label)
        {
            var start = ConstructRoutes.PortPoint(c, from, fromPort, t);
            var whole = new CRouteNode[pre.Length + r.Count];
            pre.CopyTo(whole);
            Array.Copy(r.Nodes, 0, whole, pre.Length, r.Count);
            if (ConstructRoutes.Check(world, c, t, from, fromPort, 255, 0, default, whole, to, toPort, checkFrom: Math.Max(1, pre.Length)) is { } bad) // (as the tool checks it: the nodes laid before were checked as they were laid)
            {
                s.Invalid++;
                detail.Append(label).Append(": INVALID: ").Append(bad).AppendLine();
            }
            Span<Vector3> pts = stackalloc Vector3[Construct.MaxNodesPerRoute + 2];
            var n = 0;
            pts[n++] = start;
            foreach (var p in pre)
                pts[n++] = c.ToRest(c.Parts[p.Host].Seg, p.Pos);
            var first = n - 1; // (the measured part starts at the last node laid)
            for (var i = 0; i < r.Count; i++)
                pts[n++] = c.ToRest(c.Parts[r.Nodes[i].Host].Seg, r.Nodes[i].Pos);
            pts[n++] = c.ToRest(c.Parts[to].Seg, ConstructRoutes.PortPoint(c, to, toPort, t));
            var path = pts[first..n];
            var total = 0f;
            for (var i = 1; i < path.Length; i++)
                total += Vector3.Distance(path[i - 1], path[i]);
            var min = MathF.Max(0.5f, width);
            var tightMin = t == RouteType.Conveyor ? (r.Width > 0 ? r.Width / 100f : width) : MathF.Max(0.1f, ConstructRoutes.Radius(t) * 4f); // (what the type needs to turn in)
            if (t == RouteType.Conveyor && r.Width > 0 && r.Width / 100f < width - 0.001f)
                s.Narrowed++;
            var tight = 0;
            int turns = 0, turnsS = 0, turnsE = 0, shortNear = 0;
            var along = 0f;
            for (var i = 1; i < path.Length; i++)
            {
                var len = Vector3.Distance(path[i - 1], path[i]);
                var a0 = along;
                along += len;
                if (len < min && (a0 <= Near || total - along <= Near))
                    shortNear++;
                if (len < (i == 1 || i == path.Length - 1 ? tightMin * 0.5f : tightMin) - 0.005f && (a0 <= Near || total - along <= Near))
                    tight++;
                if (i < path.Length - 1)
                {
                    var d0 = Vector3.Normalize(path[i] - path[i - 1]);
                    var d1 = Vector3.Normalize(path[i + 1] - path[i]);
                    if (Vector3.Dot(d0, d1) < 0.9994f)
                    {
                        turns++;
                        if (along <= Near)
                            turnsS++;
                        if (total - along <= Near)
                            turnsE++;
                    }
                }
            }
            s.Turns += turns;
            var turny = turnsS > 2 || turnsE > 2;
            if (shortNear > 0)
                s.ShortNear++;
            if (tight > 0)
                s.Tight++;
            if (turny)
                s.TurnyNear++;
            if (shortNear > 0 || turny)
                s.Bad++;
            detail.Append(label).Append(": ").Append(turns).Append(" turns (").Append(turnsS).Append(" by the start, ").Append(turnsE).Append(" by the end), ")
                .Append(shortNear).Append(" short near an end, ").Append(total.ToString("0.00")).Append(" m:");
            foreach (var p in path)
                detail.Append(' ').Append(p.X.ToString("0.00")).Append(',').Append(p.Y.ToString("0.00")).Append(',').Append(p.Z.ToString("0.00"));
            detail.AppendLine();
        }

        float Width(RouteType t, int from, byte fp, int to, byte tp)
        {
            if (t != RouteType.Conveyor)
                return 0f;
            ConstructConveyor.Limit(c, from, fp, to, tp, out var w, out _);
            return w / 100f;
        }

        // 1. Output to input, every pair of a type in reach (a sample of at most 60 a type).
        foreach (var t in new[] { RouteType.Data, RouteType.Power, RouteType.Pipe, RouteType.Chute, RouteType.Feed, RouteType.Conveyor })
        {
            var s = new Stats();
            var pairs = new List<(int, byte, int, byte)>();
            foreach (var (fp, fo) in ports)
            {
                if (fo.Type != t || !fo.Out)
                    continue;
                foreach (var (tp, ti) in ports)
                {
                    if (ti.Type != t || !ti.In || tp == fp)
                        continue;
                    var a = ConstructRoutes.PortPoint(c, fp, fo.Byte, t);
                    var b = ConstructRoutes.PortPoint(c, tp, ti.Byte, t);
                    if (Vector3.Distance(a, b) > 16f || Vector3.Distance(a, b) < 0.5f)
                        continue;
                    if (ConstructRoutes.CheckEnds(world, c, t, fp, fo.Byte, 255, 0, default, 0, tp, ti.Byte) != null)
                        continue;
                    pairs.Add((fp, fo.Byte, tp, ti.Byte));
                }
            }
            var step = Math.Max(1, pairs.Count / 60);
            for (var k = 0; k < pairs.Count; k += step)
            {
                var (from, fb, to, tb) = pairs[k];
                s.Asked++;
                var ask = new RouteAsk { World = world, C = c, Type = t, From = from, FromPort = fb, Parent = 255, Nodes = nodes, Count = 0, To = to, ToPort = tb };
                var r = Run(ask, ref s);
                if (r.State != RouteSearchState.Found)
                {
                    s.Failed++;
                    detail.Append($"{t} {c.DefOf(from).Name}#{from} -> {c.DefOf(to).Name}#{to}: failed: {r.Why}").AppendLine();
                    continue;
                }
                s.Found++;
                Measure(c, t, from, fb, [], r, to, tb, Width(t, from, fb, to, tb), ref s, $"{t} {c.DefOf(from).Name}#{from} -> {c.DefOf(to).Name}#{to}");
            }
            byType[t] = s;
        }

        // 2. The final connection: from a waypoint in front of each input (the nearest output of its type first routed to it).
        var offsets = new[] { (1.5f, 0f, 0f), (3f, 0f, 0f), (2f, -0.03f, 0f), (2f, 0f, 0.04f), (3f, -0.25f, 0f), (2.5f, 0f, 0.4f), (3f, -0.6f, 0.7f), (1.2f, 0.05f, 0.02f) };
        foreach (var (tp, ti) in ports)
        {
            if (!ti.In || ti.Type is not (RouteType.Pipe or RouteType.Chute or RouteType.Conveyor or RouteType.Power))
                continue;
            var t = ti.Type;
            var p = ConstructRoutes.PortPoint(c, tp, ti.Byte, t);
            var f = ConstructRoutes.PortDir(c, tp, ti.Byte, t);
            if (f == Vector3.Zero)
                continue;
            var from = -1;
            byte fb = 0;
            var best = float.MaxValue;
            foreach (var (fp, fo) in ports)
                if (fo.Type == t && fo.Out && fp != tp && Vector3.Distance(ConstructRoutes.PortPoint(c, fp, fo.Byte, t), p) is var dd && dd < best
                    && ConstructRoutes.CheckEnds(world, c, t, fp, fo.Byte, 255, 0, default, 0, tp, ti.Byte) == null)
                {
                    best = dd;
                    from = fp;
                    fb = fo.Byte;
                }
            if (from < 0 || best > 20f)
                continue;
            var side = Vector3.Normalize(Vector3.Cross(f, MathF.Abs(f.Y) > 0.9f ? Vector3.UnitX : Vector3.UnitY));
            var up = Vector3.Normalize(Vector3.Cross(side, f));
            foreach (var (d, h, sd) in offsets)
            {
                var w = ConstructWorld.Quantise(p + f * d + up * h + side * sd);
                var hn = c.PartsInBox(w - new Vector3(1f), w + new Vector3(1f), Construct.PartScratch);
                var host = -1;
                var hd = float.MaxValue;
                for (var k = 0; k < hn; k++)
                {
                    c.PartBounds(Construct.PartScratch[k], out var lo, out var hi);
                    var dist = Vector3.Distance(w, Vector3.Clamp(w, lo, hi));
                    if (dist < hd)
                    {
                        hd = dist;
                        host = Construct.PartScratch[k];
                    }
                }
                if (host < 0 || ConstructRoutes.SignedDistance(c, w, -1, -1, out _) < ConstructRoutes.Radius(t))
                    continue;
                var open = new RouteAsk { World = world, C = c, Type = t, From = from, FromPort = fb, Parent = 255, Nodes = nodes, Count = 0, To = -1, OpenAt = w, OpenHost = (ushort)host };
                var dummy = new Stats();
                var r0 = Run(open, ref dummy);
                if (r0.State != RouteSearchState.Found)
                    continue;
                var pre = new CRouteNode[r0.Count];
                Array.Copy(r0.Nodes, pre, r0.Count);
                var ask = new RouteAsk { World = world, C = c, Type = t, From = from, FromPort = fb, Parent = 255, Nodes = pre, Count = pre.Length, To = tp, ToPort = ti.Byte };
                waypointStats.Asked++;
                var label = $"{t} waypoint ({d},{h},{sd}) -> {c.DefOf(tp).Name}#{tp} {ti.Name}";
                // (the tool's own first choice: the straight line when it fits and is square or short)
                var straightWhy = ConstructRoutes.Check(world, c, t, from, fb, 255, 0, default, pre, tp, ti.Byte);
                var dd2 = Vector3.Abs(p - w);
                if (straightWhy == null && (dd2.X + dd2.Y + dd2.Z < 0.75f || (dd2.X > 0.02f ? 1 : 0) + (dd2.Y > 0.02f ? 1 : 0) + (dd2.Z > 0.02f ? 1 : 0) <= 1))
                {
                    waypointStats.Straight++;
                    detail.Append(label).AppendLine(": straight");
                    continue;
                }
                var r = Run(ask, ref waypointStats);
                if (r.State != RouteSearchState.Found)
                {
                    waypointStats.Failed++;
                    detail.Append(label).Append(": failed: ").Append(r.Why).AppendLine();
                    continue;
                }
                waypointStats.Found++;
                Measure(c, t, from, fb, pre, r, tp, ti.Byte, Width(t, from, fb, tp, ti.Byte), ref waypointStats, label);
            }
        }

        var sb = new StringBuilder();
        void Line(string what, in Stats s) =>
            sb.AppendLine($"[route-ends] {what,-22} asked {s.Asked,4}, found {s.Found,4}, failed {s.Failed,3}, invalid {s.Invalid}, straight {s.Straight,3} | bad ends {s.Bad,3} ({(s.Found > 0 ? 100.0 * s.Bad / s.Found : 0):0}%: {s.ShortNear} short, {s.TurnyNear} >2 turns) | tight for the type {s.Tight} | conveyors narrowed {s.Narrowed} | mean turns {(s.Found > 0 ? (double)s.Turns / s.Found : 0):0.00} | search {(s.Asked > 0 ? s.Ms / s.Asked : 0):0.0} ms mean, {s.MaxMs:0} max");
        var all = new Stats();
        foreach (var (t, s) in byType)
        {
            Line(t.ToString(), s);
            all.Asked += s.Asked;
            all.Found += s.Found;
            all.Failed += s.Failed;
            all.Invalid += s.Invalid;
            all.Tight += s.Tight;
            all.Narrowed += s.Narrowed;
            all.Bad += s.Bad;
            all.ShortNear += s.ShortNear;
            all.TurnyNear += s.TurnyNear;
            all.Turns += s.Turns;
            all.Ms += s.Ms;
            all.MaxMs = Math.Max(all.MaxMs, s.MaxMs);
        }
        Line("all port pairs", all);
        Line("from a waypoint", waypointStats);
        Console.Write(sb.ToString());
        var file = Path.Combine(AppContext.BaseDirectory, "route-ends.txt");
        File.WriteAllText(file, sb + detail.ToString());
        Console.WriteLine($"[route-ends] the routes: {file}");
        return 0;
    }
}
