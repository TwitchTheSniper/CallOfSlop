using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Hand-offs to a building station (Zero Day works, 2026-11-22: a part made in bay 3 had no way to the yard station 70 m south).
/// A lifter carries a piece a building station still lacks one hop nearer to it: onto the free start of a Roller Tile run whose
/// end another lifter picks from, and so on, to a pick pad that one of the station's arms fits from. The ways are worked out from
/// the lifters' points about once a second (<see cref="Leads"/>): every pick pad that serves a building station is a lead with no
/// hops; the free start of a run whose end a lifter reaching a lead picks from is a lead one hop further, up to
/// <see cref="MaxHops"/>. A piece put on a leading run rides with the pad as its destination (and the station's order as its tag),
/// so the next lifter at the run's end takes it on (<c>DestPoint</c>, else another lead). Server only; nothing allocates after the
/// first look. Docs: production.md "Lifters", "Hand-offs to a station".
/// </summary>
public static partial class ConstructLifters
{
    public const int MaxLeads = 64, MaxHops = 4, LeadEvery = 60; // leads kept, run hops followed, ticks between looks (1 s)

    private struct Lead
    {
        public ushort Body, Part; // the put point: a pick pad's owner tile (Hops 0) or a run's first tile (a RunStart)
        public LiftPointKind Kind;
        public ushort PadBody, Pad; // the pick pad it leads to
        public ushort StBody, St; // the station that pad serves
        public byte Hops;
    }

    private static readonly Lead[] _leads = new Lead[MaxLeads];
    private static int _leadN, _leadTick = int.MinValue;
    private static ConstructWorld? _leadWorld;

    /// <summary>The run whose own first tile is <paramref name="tile"/> and that starts at the tile's Run start (nothing butted on its in end), or -1. Either peer.</summary>
    public static int StartRunAt(Construct c, int tile) => RollerRuns.StartRunAt(c, tile);

    /// <summary>Server: the leads as they are now, worked out again at most once a second.</summary>
    private static void Leads(ConstructWorld world)
    {
        if (ReferenceEquals(_leadWorld, world) && world.LiftTick - _leadTick < LeadEvery && world.LiftTick >= _leadTick)
            return;
        _leadWorld = world;
        _leadTick = world.LiftTick;
        _leadN = 0;
        if (world.StationOpen.Count == 0 || world.LiftBodies.Count == 0)
            return;
        var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        LeadsWork(world);
        LifterProbe.Cost(ref LifterProbe.LeadCost, System.Diagnostics.Stopwatch.GetTimestamp() - t0);
    }

    private static void LeadsWork(ConstructWorld world)
    {
        // Hop 0: each pick pad a station's arm (or its own crane) fits from.
        int si = 0, t = -1;
        while (NextStation(world, ref si, ref t, out var sc, out _, out _))
            for (var bi = 0; bi < world.LiftBodies.Count; bi++)
            {
                var lb = world.LiftBodies[bi];
                if (world.Get(lb.Body) is not { } c)
                    continue;
                for (var i = 0; i < lb.N; i++)
                {
                    var l = lb.Lifts[i];
                    if (l.Part >= c.PartCount || DefOf(c, l.Part) is not { } ld)
                        continue;
                    if (l.Points == null)
                        FindPoints(world, c, l, ld); // (just loaded or placed; else its own looks keep them, every 2 s)
                    for (var k = 0; k < l.PointN && _leadN < MaxLeads; k++)
                    {
                        ref readonly var pt = ref l.Points![k];
                        if (IsPickPad(world, pt, out _) && !HasLead(pt.Body, pt.Part, sc.Id, t) && ServesPad(world, sc, t, pt.Body, pt.Part))
                            _leads[_leadN++] = new Lead { Body = pt.Body, Part = pt.Part, Kind = LiftPointKind.Rack, PadBody = pt.Body, Pad = pt.Part, StBody = sc.Id, St = (ushort)t };
                    }
                }
            }
        // Hops 1 to MaxHops: a lifter that puts to a lead and picks at a run's end makes that run's free start a lead.
        var from = 0;
        for (var hop = 1; hop <= MaxHops && from < _leadN; hop++)
        {
            var to = _leadN;
            for (var bi = 0; bi < world.LiftBodies.Count; bi++)
            {
                var lb = world.LiftBodies[bi];
                if (world.Get(lb.Body) is not { } c)
                    continue;
                for (var i = 0; i < lb.N; i++)
                {
                    var l = lb.Lifts[i];
                    if (l.Points == null || l.Part >= c.PartCount || (int)ConstructConfig.Get(c.Parts[l.Part], c.DefOf(l.Part), CfgKey.LiftMode, 0) is 1 or 3)
                        continue; // (Station only, Off: it carries nothing between points)
                    for (var k = 0; k < l.PointN; k++)
                    {
                        ref readonly var pt = ref l.Points[k];
                        for (var e = from; e < to; e++)
                        {
                            if (_leads[e].Body != pt.Body || _leads[e].Part != pt.Part || _leads[e].Kind != pt.Kind)
                                continue;
                            AddRunLeads(world, l, _leads[e], (byte)hop);
                            break;
                        }
                    }
                }
            }
            from = to;
        }
    }

    /// <summary>The free starts of the runs whose ends lifter <paramref name="l"/> picks from, as leads one hop further than <paramref name="via"/>.</summary>
    private static void AddRunLeads(ConstructWorld world, CLift l, in Lead via, byte hops)
    {
        for (var k = 0; k < l.PointN && _leadN < MaxLeads; k++)
        {
            ref readonly var pt = ref l.Points![k];
            if (pt.Kind != LiftPointKind.RunEnd || world.Get(pt.Body) is not { } b || RollerRuns.EndRunAt(b, pt.Part) is not (var run and >= 0))
                continue;
            var first = b.Routes[run].From;
            if (first >= b.PartCount || RollerRuns.StartRunAt(b, first) != run || HasLead(pt.Body, first, via.StBody, via.St))
                continue; // (a run fed by what is butted on its in end: nothing is put on it by hand)
            _leads[_leadN++] = new Lead { Body = pt.Body, Part = first, Kind = LiftPointKind.RunStart, PadBody = via.PadBody, Pad = via.Pad, StBody = via.StBody, St = via.St, Hops = hops };
        }
    }

    private static bool HasLead(ushort body, int part, ushort stBody, int st)
    {
        for (var e = 0; e < _leadN; e++)
            if (_leads[e].Body == body && _leads[e].Part == part && _leads[e].StBody == stBody && _leads[e].St == st)
                return true;
        return false;
    }

    /// <summary>
    /// How many more of a kind the lead's station lacks that aren't yet on their way to it: needed, less fitted, in its hold, on its
    /// arms' pick pads, riding to the lead's pad and in hands bound for the pad or a run that leads to it.
    /// </summary>
    private static int LeadLack(ConstructWorld world, in Lead ld, ConstructPartDef d)
    {
        if (world.Get(ld.StBody) is not { } sc || ld.St >= sc.PartCount || sc.Parts[ld.St].Station is not { State: StationState.Building } j || world.StoreOf(sc, ld.St) is not { } tray)
            return 0;
        var def = (ushort)d.Id;
        var kind = Array.IndexOf(j.KindDef, def, 0, j.KindN);
        if (kind < 0)
            return 0;
        var lack = j.KindNeed[kind] - j.KindFit[kind] - ConstructCrafting.Holds(tray, def) - PickPadHolds(world, sc, ld.St, def);
        if (lack <= 0 || world.Get(ld.PadBody) is not { } pb || ld.Pad >= pb.PartCount)
            return 0;
        var handle = pb.Parts[ld.Pad].Store;
        for (var i = 0; i < pb.ConveyedCount; i++)
            if (pb.Conveyed[i].Dest == handle && pb.Conveyed[i].Def == def)
                lack--;
        for (var bi = 0; bi < world.LiftBodies.Count; bi++)
        {
            var lb = world.LiftBodies[bi];
            for (var i = 0; i < lb.N; i++)
            {
                var o = lb.Lifts[i];
                if (o.State != LiftState.Carry || o.Def != def || o.Fitted || o.ToKind is not (LiftPointKind.Rack or LiftPointKind.RunStart))
                    continue;
                for (var e = 0; e < _leadN; e++)
                    if (_leads[e].Body == o.StationBody && _leads[e].Part == o.ToPart && _leads[e].Kind == o.ToKind && _leads[e].PadBody == ld.PadBody && _leads[e].Pad == ld.Pad)
                    {
                        lack--;
                        break;
                    }
            }
        }
        return lack;
    }

    /// <summary>The lead at a point (the fewest hops) whose station lacks this kind (<paramref name="lacking"/>: else any), or -1.</summary>
    private static int LeadAt(ConstructWorld world, ushort body, int part, LiftPointKind kind, ConstructPartDef d, ushort padHandle = 0, bool lacking = true, int self = 0)
    {
        var best = -1;
        for (var e = 0; e < _leadN; e++)
        {
            ref readonly var ld = ref _leads[e];
            if (ld.Body != body || ld.Part != part || ld.Kind != kind || best >= 0 && _leads[best].Hops <= ld.Hops)
                continue;
            if (padHandle != 0 && (world.Get(ld.PadBody) is not { } pb || ld.Pad >= pb.PartCount || pb.Parts[ld.Pad].Store != padHandle))
                continue;
            if (!lacking || LeadLack(world, ld, d) + self > 0)
                best = e;
        }
        return best;
    }

    /// <summary>Is this point a station's pick pad (a lead with no hops)? A piece there waits for the station's arm: it is never handed on.</summary>
    private static bool IsLeadPad(ConstructWorld world, in LiftPoint pt)
    {
        if (pt.Kind != LiftPointKind.Rack)
            return false;
        Leads(world);
        for (var e = 0; e < _leadN; e++)
            if (_leads[e].Hops == 0 && _leads[e].Body == pt.Body && _leads[e].Part == pt.Part)
                return true;
        return false;
    }

    /// <summary>
    /// Server: where a piece goes one hop nearer a building station that lacks it (a pick pad its arm fits from, else the free start of a
    /// run that leads there), among this lifter's points: the fewest hops first. <paramref name="padHandle"/> (not 0): only toward that
    /// pad (a rider's destination). -1: none.
    /// </summary>
    private static int HandOffFor(ConstructWorld world, CLift l, LifterDef ld, Vector3 shoulder, Quaternion lr, ConstructPartDef d, int skip, out Vector3 at, ushort padHandle = 0)
    {
        at = default;
        if (world.StationOpen.Count == 0)
            return -1;
        Leads(world);
        if (_leadN == 0 || skip >= 0 && skip < l.PointN && IsLeadPad(world, l.Points![skip]))
            return -1;
        var lift = WristLift(d);
        int best = -1, bestHops = int.MaxValue;
        for (var k = 0; k < l.PointN; k++)
        {
            ref readonly var pt = ref l.Points![k];
            if (k == skip || pt.Kind is not (LiftPointKind.Rack or LiftPointKind.RunStart) || world.Get(pt.Body) is not { } b || pt.Part >= b.PartCount)
                continue;
            var e = LeadAt(world, pt.Body, pt.Part, pt.Kind, d, padHandle, self: padHandle != 0 ? 1 : 0); // (a rider bound for the pad counts itself among those riding there)
            if (e < 0 || _leads[e].Hops >= bestHops)
                continue;
            Vector3 p;
            if (pt.Kind == LiftPointKind.Rack)
            {
                if (world.StoreOf(b, pt.Part) is not { } st || ConstructConveyor.RackCheck(b, pt.Part, st, d, ordered: true) != 0 || !RackSpot(b, pt.Part, ld, shoulder, lr, lift, lift, out p))
                    continue;
            }
            else if (!RunRoom(b, RollerRuns.StartRunAt(b, pt.Part), d) || Bound(world, l, pt) || !PointAt(b, pt, out p))
                continue;
            p.Y += (d.BoundsMax.Y - d.BoundsMin.Y) * 0.5f + 0.05f;
            if (!InReach(ld, shoulder, lr, p, lift, out _))
                continue;
            (best, bestHops, at) = (k, _leads[e].Hops, p);
        }
        return best;
    }

    /// <summary>Has run <paramref name="run"/> room at its start for one piece of this kind (as <see cref="ConstructConveyor.PutMade"/> asks)?</summary>
    private static bool RunRoom(Construct b, int run, ConstructPartDef d)
    {
        if (run < 0 || b.Routes[run].FirstCut <= b.Routes[run].Count || b.ConveyedCount + 1 > Construct.MaxConveyed)
            return false;
        ConstructConveyor.Size(d, out var across, out var up, out var along);
        if (!ConstructConveyor.Fits(across, up, b.Routes[run]) || along > ConstructConveyor.Length(b, run) + 0.01f)
            return false;
        var last = -1;
        for (var i = 0; i < b.ConveyedCount; i++)
            if (b.Conveyed[i].Route == run)
                last = i;
        if (last < 0)
            return true;
        ConstructConveyor.Size(ConstructParts.Def(b.Conveyed[last].Def), out _, out _, out var lastAlong);
        return b.Conveyed[last].At - lastAlong - ConstructConveyor.Gap >= along;
    }

    /// <summary>Server: a piece put on a run's free start (a carry's end or a cab's set-down): it rides toward the pad the run leads to, for the station's order of its kind.</summary>
    private static bool PutOnRun(ConstructWorld world, Construct b, int tile, ConstructPartDef d)
    {
        var run = RollerRuns.StartRunAt(b, tile);
        if (run < 0)
            return false;
        Leads(world);
        ushort dest = 0, tag = 0;
        var e = LeadAt(world, b.Id, tile, LiftPointKind.RunStart, d, lacking: false); // (the hand putting it is one of those counted on their way)
        if (e >= 0 && world.Get(_leads[e].PadBody) is { } pb && _leads[e].Pad < pb.PartCount && world.Get(_leads[e].StBody) is { } sc)
        {
            dest = pb.Parts[_leads[e].Pad].Store;
            tag = OrderOf(sc, _leads[e].St, (ushort)d.Id, 0) is var k and >= 0 ? sc.Parts[_leads[e].St].Craft!.Items[k].Id : (ushort)0;
            if (pb != b)
                dest = 0; // (a rider's destination is a store on its own body: the next lifter's own rule takes it on)
        }
        if (!ConstructConveyor.PutMade(world, b, run, d, 1, out _, dest, tag))
            return false;
        Console.WriteLine($"[lifter] a {d.Name} on the run at body {b.Id} part {tile}{(e >= 0 ? $", for the station (body {_leads[e].StBody} part {_leads[e].St}) by its pick pad, {_leads[e].Hops} hop(s)" : "")}");
        return true;
    }

    /// <summary>A rider's destination is a station's pick pad the hand-off led it to (a lead with no hops)?</summary>
    private static bool IsLeadPadHandle(ConstructWorld world, ushort body, ushort handle)
    {
        if (handle == 0)
            return false;
        Leads(world);
        for (var e = 0; e < _leadN; e++)
            if (_leads[e].Hops == 0 && _leads[e].PadBody == body && world.Get(body) is { } pb && _leads[e].Pad < pb.PartCount && pb.Parts[_leads[e].Pad].Store == handle)
                return true;
        return false;
    }

    /// <summary>Window tool and checks: each lead, one a line.</summary>
    public static string LeadsText(ConstructWorld world)
    {
        _leadTick = int.MinValue;
        Leads(world);
        var s = $"{_leadN} hand-off lead(s)";
        for (var e = 0; e < _leadN; e++)
        {
            ref readonly var ld = ref _leads[e];
            var name = world.Get(ld.Body) is { } b && ld.Part < b.PartCount ? b.DefOf(ld.Part).Name : "?";
            s += $"\n  {ld.Kind} {name} (body {ld.Body} part {ld.Part}) -> pad part {ld.Pad} -> station body {ld.StBody} part {ld.St}, {ld.Hops} hop(s)";
        }
        return s;
    }
}
