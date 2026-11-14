using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The window tool's lifter phase 5b verbs (Program.WindowShot.cs, under "lifter"; host only, a throwaway world's):
/// "lifter net [TIER]" the press line on one body with a Router: a stock rack of blanks, a conveyor to a Conveyor Router, one on to the
/// arm's pick rack, the arm, a Press with a Button Die, a Crafting Terminal whose tray the arm reaches, a Lay-down Pad (nothing between
/// the pick rack, the press and the tray but the arm); "lifter edges" the body's lifter edges as its map has them; "lifter bigparts"
/// the blueprints with a part the first Building Station's hold is too small for; "lifter crowd N" N loose pieces strewn round the
/// player (the pads' scan in a world of many bodies).
/// </summary>
public static partial class LifterProbe
{
    public static ushort NetBody;

    private static byte Port(ConstructPartDef d, bool output)
    {
        foreach (var np in d.NamedPorts)
            if (np.Type == RouteType.Conveyor && (output ? np.Out : np.In))
                return np.Byte;
        return 255;
    }

    /// <summary>Body <paramref name="b"/> into <paramref name="a"/> where it stands (both square to the world: placed with no turn).</summary>
    private static bool Into(ConstructWorld world, Construct a, Construct b)
    {
        var inv = Quaternion.Conjugate(a.Rot);
        var turn = Rot24.Nearest(inv * b.Rot, out var match);
        if (match < 0.99f || world.CanMerge(a, b) != null) // (a probe: an arm a little tilted by the ground is squared by the join)
            return false;
        var plan = new JoinPlan { Other = b, PartA = 0, SockA = 0, PartB = 0, SockB = 0, Turn = turn, Offset = Vector3.Transform(b.Pos - a.Pos, inv) };
        return world.Merge(a, plan);
    }

    public static string Net(ConstructWorld world, Vector3 at, string args)
    {
        var feedPad = args.EndsWith("pad", StringComparison.Ordinal); // ("lifter net [TIER] pad": the Router feeds the pad by conveyor too)
        var tier = feedPad ? args[..^3].Trim() : args;
        var line = Press(world, at, tier);
        if (world.Get(LineArm) is not { } arm || world.Get(LinePress) is not { } pr || world.Get(LineRack) is not { } rk || world.Get(LinePad) is not { } pd)
            return "net: " + line;
        if (ConstructParts.Find("Crafting Terminal") is not { } term || ConstructParts.Find("Conveyor Router") is not { } router || ConstructParts.Find("Piece Rack") is not { } rack || Blank() is not { } blank)
            return "net: a part is missing (Crafting Terminal, Conveyor Router, Piece Rack)";
        var ld = arm.DefOf(0).Lifter!;
        float G(float x, float z) => World.Map.GroundHeight(x, z);
        // The pick rack is emptied: what the press gets comes by the Router.
        if (world.StoreOf(rk, 0) is { } pst)
            for (var n = 0; n < 8; n++)
                ConstructCrafting.TakeOne(world, rk, 0, pst, blank);
        var tm = (term.BoundsMin + term.BoundsMax) * 0.5f;
        var tx = at + new Vector3(-tm.X, 0, ld.ReachMax * 0.45f + Half(term).Z - tm.Z);
        var tb = ConstructBuild.PlaceNew(world, term, tx with { Y = G(tx.X, tx.Z) }, 0);
        var rkMid = rk.Pos + Vector3.Transform((rack.BoundsMin + rack.BoundsMax) * 0.5f, rk.Rot);
        var ro = rkMid + new Vector3(-(Half(rack).X + 0.9f), 0, 0);
        var rb = ConstructBuild.PlaceNew(world, router, ro with { Y = G(ro.X, ro.Z) }, 0);
        var so = ro + new Vector3(-(Half(rack).X + 1.2f), 0, 0);
        var sb = ConstructBuild.PlaceNew(world, rack, so with { Y = G(so.X, so.Z) }, 0);
        if (tb == null || rb == null || sb == null)
            return "net: no room for the terminal, the Router or the stock rack";
        var stocked = 0;
        for (var n = 0; n < 6; n++)
            stocked += world.RackPut(sb, 0, blank) ? 1 : 0;
        var parts = new[] { pr, rk, pd, tb, rb, sb };
        var idx = new int[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            idx[i] = arm.PartCount;
            if (!Into(world, arm, parts[i]))
                return $"net: body {parts[i].Id} would not join the arm's";
        }
        int pPress = idx[0], pRack = idx[1], pPad = idx[2], pTerm = idx[3], pRouter = idx[4], pStock = idx[5];
        var k1 = ConstructRoutes.Add(world, arm, RouteType.Conveyor, pStock, Port(arm.DefOf(pStock), true), 255, 0, default, ReadOnlySpan<CRouteNode>.Empty, pRouter, Port(router, false));
        var k2 = ConstructRoutes.Add(world, arm, RouteType.Conveyor, pRouter, Port(router, true), 255, 0, default, ReadOnlySpan<CRouteNode>.Empty, pRack, Port(arm.DefOf(pRack), false));
        var k3 = -1;
        if (feedPad)
        {
            byte second = 255;
            var outs = 0;
            foreach (var np in router.NamedPorts)
                if (np.Type == RouteType.Conveyor && np.Out && ++outs == 2)
                    second = np.Byte;
            k3 = ConstructRoutes.Add(world, arm, RouteType.Conveyor, pRouter, second, 255, 0, default, ReadOnlySpan<CRouteNode>.Empty, pPad, Port(arm.DefOf(pPad), false));
        }
        arm.Dirty = true;
        arm.Rebuild();
        world.StructureDirty.Add(arm.Id);
        NetBody = arm.Id;
        if (world.StoreOf(arm, pRack) is { } left && left.ItemCount > 0)
            line += $" (the pick rack still holds {left.ItemCount})";
        if (feedPad)
            line += $"; conveyor {k3} (Router to pad)";
        LinePad = arm.Id; // (the pad is on this body now: "lifter drop" finds it by its first pad)
        return $"net: one body {arm.Id}: arm 0, Press {pPress}, pick rack {pRack}, pad {pPad}, terminal {pTerm}, Router {pRouter}, stock rack {pStock} ({stocked} {blank.Name}); conveyors {k1} (stock to Router), {k2} (Router to pick rack); {line}";
    }

    /// <summary>Host: an unordered laminate plate blank CM long onto the net rig's first conveyor (stock rack to Router): "lifter ride CM".</summary>
    public static string Ride(ConstructWorld world, string cm)
    {
        var shut = cm.EndsWith("shut", StringComparison.Ordinal); // ("lifter ride CM shut": the pick rack takes nothing as storage first)
        cm = shut ? cm[..^4].Trim() : cm;
        if (world.Get(NetBody) is not { RouteCount: > 0 } c || Blank(int.TryParse(cm, out var n) ? n : 300) is not { } d)
            return "ride: no net rig (lifter net first)";
        var ok = ConstructConveyor.PutMade(world, c, 0, d, 1, out var why);
        var rack = -1;
        for (var i = 0; i < c.PartCount && rack < 0; i++)
            if (c.DefOf(i).IsPieceRack && !c.DefOf(i).IsLayDownPad && !c.DefOf(i).IsCraftTerminal)
                rack = i;
        if (shut && rack >= 0)
        {
            ConstructConfig.Put(ref c.Parts[rack], c.DefOf(rack), CfgKey.StoreTakes, StockTargets.TakesNothing);
            world.StructureDirty.Add(c.Id);
        }
        return $"ride: {d.Name} {(ok ? "rides" : $"refused ({why})")}{(shut ? $", the pick rack (part {rack}) takes nothing as storage" : "")}; a Piece Rack {(rack >= 0 && world.StoreOf(c, rack) is { } st ? ConstructConveyor.RackRefusal(c, rack, st, d) ?? "takes it" : "?")}";
    }

    /// <summary>Host: the newest arm and the probe's pad joined into the station's body (a pick pad on the station's own network).</summary>
    public static string Weld(ConstructWorld world, Construct sc)
    {
        Construct? arm = null;
        foreach (var b in world.Bodies)
            if (b.PartCount == 1 && b.DefOf(0).Lifter != null && (arm == null || b.Id > arm.Id))
                arm = b;
        var s = "weld:";
        if (arm != null)
            s += Into(world, sc, arm) ? $" arm joined as part {sc.PartCount - 1};" : " the arm would not join;";
        if (world.Get(LinePad) is { PartCount: 1 } pd && pd != sc)
            s += Into(world, sc, pd) ? $" pad joined as part {sc.PartCount - 1}" : " the pad would not join";
        return s;
    }

    /// <summary>
    /// Host: the newest arm joined into the station's body, then the cost of what a rebuild of a big body with a lifter pays: the lifter
    /// edges (MapEdges), the whole map made again, and a terminal's network key (each NetOf look).
    /// </summary>
    public static string MapCost(ConstructWorld world, Construct sc)
    {
        Construct? arm = null;
        foreach (var b in world.Bodies)
            if (b.PartCount == 1 && b.DefOf(0).Lifter != null && (arm == null || b.Id > arm.Id))
                arm = b;
        if (arm != null && !Into(world, sc, arm))
            return $"mapcost: the arm would not join the station's body ({world.CanMerge(sc, arm) ?? "not square to it"}; turn match {Rot24.Nearest(Quaternion.Conjugate(sc.Rot) * arm.Rot, out var mt)} {mt:0.0000})";
        Span<LiftEdge> edges = stackalloc LiftEdge[ConstructLifters.MaxEdges];
        var f = System.Diagnostics.Stopwatch.Frequency / 1e6;
        var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        var n = 0;
        for (var k = 0; k < 100; k++)
            n = ConstructLifters.MapEdges(sc, edges);
        var edgesUs = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) / f / 100;
        t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        for (var k = 0; k < 20; k++)
        {
            sc.LogiStamp++;
            if (sc.Logi != null)
                sc.Logi.LinkHash = 0;
            ConstructLogistics.Map(sc);
        }
        var mapUs = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) / f / 20;
        var term = -1;
        for (var i = 0; i < sc.PartCount && term < 0; i++)
            if (sc.DefOf(i).IsCraftTerminal)
                term = i;
        var keyUs = 0.0;
        if (term >= 0)
        {
            ConstructCrafting.NetOf(world, sc, term);
            t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            for (var k = 0; k < 1000; k++)
                ConstructCrafting.NetOf(world, sc, term);
            keyUs = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) / f / 1000;
        }
        return $"mapcost: body {sc.Id} ({sc.PartCount} parts, {sc.RouteCount} links): {n} lifter edges in {edgesUs:0.0} us; the map made whole {mapUs:0.0} us; a terminal's NetOf look {keyUs:0.00} us";
    }

    /// <summary>The body's lifter edges as its map has them, and each Router's ways to the machines and trays.</summary>
    public static string Edges(ConstructWorld world)
    {
        if (world.Get(NetBody) is not { } c)
            return "edges: no net body (lifter net first)";
        var m = ConstructLogistics.Map(c);
        var s = $"edges: body {c.Id}, {m.LiftN} lifter edges, {m.NodeN} nodes, {m.DestN} destinations";
        for (var e = 0; e < m.LiftN; e++)
            s += $"; {c.DefOf(m.Lifts[e].From).Name}#{m.Lifts[e].From} -> {c.DefOf(m.Lifts[e].To).Name}#{m.Lifts[e].To} by {c.DefOf(m.Lifts[e].Lifter).Name}#{m.Lifts[e].Lifter} {m.Lifts[e].Len:0.0} m";
        for (var r = 0; r < m.RouterN; r++)
            for (var ds = 0; ds < m.DestN; ds++)
            {
                ConstructLogistics.Column(c, m, ds, force: true);
                var at = r * LogiMap.MaxDests + ds;
                int dp = m.NodePart[m.Dests[ds]];
                s += $"; Router#{m.NodePart[m.Routers[r]]} to {c.DefOf(dp).Name}#{dp}: {(m.Hop[at] == LogiMap.None ? "no way" : $"conveyor {m.Hop[at]}, {m.Dist[at]:0.0} m{(m.ViaLift[at] ? ", by a lifter" : "")}")}";
            }
        for (var i = 0; i < c.PartCount; i++)
            if (c.DefOf(i).IsCraftTerminal)
            {
                var net = ConstructCrafting.NetOf(world, c, i);
                s += $"; terminal#{i}'s network: {net.MachineN} machines, {net.StoreN} stores (via";
                for (var k = 0; k < net.StoreN; k++)
                    s += $" {c.DefOf(net.Stores[k]).Name}#{net.Stores[k]}={net.Via[k]}";
                s += ")";
            }
        return s;
    }

    /// <summary>The blueprints with a part the first station's hold is too small for (fewest steps first).</summary>
    public static string BigParts(ConstructWorld world, Construct sc, int t)
    {
        if (world.StoreOf(sc, t) is not { } tray)
            return "bigparts: no hold";
        var found = new List<(int Steps, string Text)>();
        var bay = ConstructStation.BaySize(sc, t);
        var bs = new[] { bay.X, bay.Y, bay.Z }.OrderByDescending(v => v).ToArray();
        foreach (var bp in ConstructBuild.Blueprints)
        {
            string? big = null;
            var fits = bp.Steps.Count <= 12;
            foreach (var st in bp.Steps)
                if (fits && ConstructParts.Find(st.Part) is { } d)
                {
                    var s = d.BoundsMax - d.BoundsMin;
                    var ps = new[] { s.X, s.Y, s.Z }.OrderByDescending(v => v).ToArray();
                    fits = ps[0] <= bs[0] && ps[1] <= bs[1] && ps[2] <= bs[2];
                    if (big == null && ConstructLifters.TooBigForHold(sc, t, tray, d))
                        big = $"{d.Name} {s.X:0.0}x{s.Y:0.0}x{s.Z:0.0} {d.Role} {d.Mass:0} kg";
                }
            if (big != null && fits)
                found.Add((bp.Steps.Count, $"{bp.Name} ({bp.Steps.Count} steps: {big})"));
        }
        found.Sort((a, b) => a.Steps.CompareTo(b.Steps));
        return $"bigparts: {found.Count} blueprints of 12 steps or fewer whose parts each fit the bay; " + string.Join("; ", found.Take(25).Select(f => f.Text));
    }

    /// <summary>Each unfitted step of the station's job as the newest arm sees it: ready, carried, in reach, in the hold, on its pick pads.</summary>
    public static string Steps(ConstructWorld world, Construct sc, int t)
    {
        if (sc.Parts[t].Station is not { Plan: { } plan } j || world.StoreOf(sc, t) is not { } tray)
            return "steps: no job";
        CLift? l = null;
        Construct? ac = null;
        foreach (var lb in world.LiftBodies)
            if (lb.N > 0 && (ac == null || lb.Body > ac.Id) && world.Get(lb.Body) is { } b)
                (l, ac) = (lb.Lifts[0], b);
        if (l == null || ac == null || ConstructLifters.DefOf(ac, l.Part) is not { } ld)
            return "steps: no arm";
        ConstructLifters.Frame(ac, l.Part, ld, out _, out var lr, out var sh);
        Freeform.PartWorld(sc, t, out var stp, out _);
        var s = $"steps: cursor {j.Cursor}, job at {j.Pos:0.0}, station part at {stp:0.0}, shoulder {sh:0.0}, body {(world.Get(j.Body) is { } jb ? jb.Pos.ToString("0.0") : "none")}";
        for (var k = 0; k < j.PartOf.Length; k++)
        {
            if (j.PartOf[k] != -1 || j.StepKind[k] < 0 || ConstructParts.Get(j.KindDef[j.StepKind[k]]) is not { } d)
                continue;
            var place = j.Pos + Vector3.Transform(plan.At[k], j.Rot);
            ConstructLifters.InReach(ld, sh, lr, place, ConstructLifters.WristLift(d), out var far);
            s += $"; {k} {d.Name}: ready {ConstructStation.ReadyStep(j, k)}, carries {ConstructLifters.Carries(ld, d)}, reach {ConstructLifters.InReach(ld, sh, lr, place, ConstructLifters.WristLift(d), out _)} ({far:0.0} m, up {Vector3.Transform(place - sh, Quaternion.Conjugate(lr)).Y:0.0}), hold {ConstructCrafting.Holds(tray, d.Id)}, pad {ConstructLifters.PickPadHolding(world, l, d.Id)}, counted {ConstructLifters.PickPadHolds(world, sc, t, d.Id)}";
        }
        return s;
    }

    /// <summary>Host: a Piece Rack beside the newest arm (along the bay, away from the station), stocked with up to 4 of a part: "lifter bigrack NAME".</summary>
    public static string BigRack(ConstructWorld world, Construct sc, int t, string name)
    {
        if (ConstructParts.Find("Piece Rack") is not { } rack || ConstructParts.Find(name) is not { } d)
            return $"bigrack: no Piece Rack or no part \"{name}\"";
        Construct? arm = null;
        foreach (var b in world.Bodies)
            if (b.PartCount == 1 && b.DefOf(0).Lifter != null && (arm == null || b.Id > arm.Id))
                arm = b;
        if (arm == null)
            return "bigrack: no arm";
        Freeform.PartWorld(sc, t, out _, out var sr);
        var ad = arm.DefOf(0);
        var armMid = arm.Pos + Vector3.Transform((ad.BoundsMin + ad.BoundsMax) * 0.5f, arm.Rot);
        var along = Vector3.Transform(Vector3.UnitX, sr);
        var centre = armMid + along * (Half(ad).X + 0.6f + Half(rack).X);
        var pos = centre - Vector3.Transform((rack.BoundsMin + rack.BoundsMax) * 0.5f, sr);
        pos.Y = World.Map.GroundHeight(centre.X, centre.Z) - rack.BoundsMin.Y + 0.01f;
        if (ConstructBuild.PlaceNew(world, rack, new FreePlacement { Pos = pos, Rot = sr }) is not { } rb)
            return "bigrack: no room";
        rb.Anchored = true;
        var n = 0;
        for (var i = 0; i < 4; i++)
            n += world.RackPut(rb, 0, d) ? 1 : 0;
        return $"bigrack: Piece Rack body {rb.Id} beside arm body {arm.Id}, {n} {d.Name} in it{(n == 0 && world.StoreOf(rb, 0) is { } st ? $" ({ConstructConveyor.RackRefusal(rb, 0, st, d)})" : "")}";
    }

    /// <summary>N loose pieces strewn round a point (2 to 60 m out), for the pads' scan cost with many bodies.</summary>
    public static string Crowd(ConstructWorld world, Vector3 at, int n)
    {
        if (Blank(20) is not { } d)
            return "crowd: no blank";
        var made = 0;
        var rng = new Random(7);
        for (var i = 0; i < n; i++)
        {
            var a = rng.NextSingle() * MathF.Tau;
            var r = 6f + rng.NextSingle() * 54f;
            var p = at + new Vector3(MathF.Cos(a) * r, 0, MathF.Sin(a) * r);
            p.Y = World.Map.GroundHeight(p.X, p.Z) + 0.3f;
            made += ConstructEdits.DropLoose(world, d, p, Quaternion.Identity) != null ? 1 : 0;
        }
        TakeIn = PadCount = default;
        return $"crowd: {made} loose pieces round {at:0} ({world.Bodies.Count} bodies in the world); the pads' cost counters start again";
    }
}
