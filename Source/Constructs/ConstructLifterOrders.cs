using System.Numerics;

namespace CallOfSlop;

/// <summary>Why a hand order waits (the panel's row; sent with the lifts).</summary>
public enum LiftOrderWhy : byte
{
    None,
    NoPiece, // nothing of its kind at its pick point now
    Refused, // its put point doesn't take it now (full, not wanted, another hand bound for it)
    Unreached, // one of its points is out of this lifter's reach now (moved, taken down, a rail gone)
    TooBig, // the lifter can't carry its kind
    Gone, // its kind is no longer known
}

/// <summary>A player's move order at a lifter's panel (arms-and-cranes.md section 5 "By hand", phase 7): carry Count of a kind from one point to another.</summary>
public struct LiftOrder
{
    public byte Id; // the lifter's own number for it (1 to 255), for a cancel
    public ushort Def; // the kind (a run-time piece by its key on the wire and in a save)
    public byte Count, Done;
    public LiftOrderWhy Why;
    public LiftPoint From, To;
}

public enum LiftOrderOp : byte
{
    Add,
    Cancel, // the order with this id
    Clear, // all of them
    Grab, // (phase 8) F in a Lifter Cab: body and part are the cab's
}

/// <summary>
/// Lifters phase 7 (docs/sandbox/design/arms-and-cranes.md section 5 "By hand", the user's answer 6): hand orders. At a lifter's panel a
/// player picks a kind at one of its pick points (a rack, a pad, a machine's Piece out or tray) and one of its put points, and a count;
/// the lifter carries them before any automatic work, in the order given, at most <see cref="MaxOrders"/> a lifter. An order whose
/// piece isn't there yet, or whose put point refuses it now, waits with its reason and the lifter goes on with its own work. The server
/// decides (<see cref="CActKind.Lifter"/>, under the rule of a finger on a console); the list rides the lift word and the LIFT block.
/// Nothing allocates in the step: an order list is made once a lifter, on its first order.
/// </summary>
public static partial class ConstructLifters
{
    public const int MaxOrders = 8;
    public const int MaxOrderCount = 99;

    private static bool Same(in LiftPoint a, in LiftPoint b) => a.Body == b.Body && a.Part == b.Part && a.Kind == b.Kind && a.Bay == b.Bay;

    private static int PointIndex(CLift l, in LiftPoint pt)
    {
        for (var k = 0; k < l.PointN; k++)
            if (Same(l.Points![k], pt))
                return k;
        return -1;
    }

    /// <summary>Can a hand order pick from this kind of point?</summary>
    public static bool Picks(LiftPointKind k) => k is LiftPointKind.Rack or LiftPointKind.Out;

    /// <summary>Can a hand order put to this kind of point?</summary>
    public static bool Puts(LiftPointKind k) => k is not (LiftPointKind.Out or LiftPointKind.None);

    /// <summary>Server: the idle lifter's first look. True when an order's carry began.</summary>
    private static bool OrderCarry(ConstructWorld world, Construct c, CLift l, LifterDef ld)
    {
        if (l.Points == null || world.LiftTick - l.PointsAt >= PointEvery)
            FindPoints(world, c, l, ld);
        Frame(c, l.Part, ld, out var lp, out var lr, out var shoulder);
        var changed = false;
        var began = false;
        for (var oi = 0; oi < l.OrderN && !began; oi++)
        {
            ref var o = ref l.Orders![oi];
            var why = OrderTry(world, c, l, ld, lp, lr, shoulder, o);
            began = why == LiftOrderWhy.None;
            if (o.Why != why)
            {
                o.Why = why;
                changed = true;
            }
            if (began)
                l.OrderId = o.Id;
        }
        if (changed && !began)
            world.LiftChanged(c.Id); // (a carry's start sends it anyway)
        return began;
    }

    /// <summary>One order: its carry begun (None), or why not.</summary>
    private static LiftOrderWhy OrderTry(ConstructWorld world, Construct c, CLift l, LifterDef ld, Vector3 lp, Quaternion lr, Vector3 shoulder, in LiftOrder o)
    {
        if (ConstructParts.Get(o.Def) is not { } d)
            return LiftOrderWhy.Gone;
        if (!Carries(ld, d))
            return LiftOrderWhy.TooBig;
        var from = PointIndex(l, o.From);
        var to = PointIndex(l, o.To);
        if (from < 0 || to < 0 || world.Get(o.From.Body) is not { } fb || world.Get(o.To.Body) is not { } tb)
            return LiftOrderWhy.Unreached;
        // Where it goes first: a hand order skips the Routers' rule (storage to storage, pad to pad are fine), not the point's own.
        if (!OrderPut(world, l, ld, shoulder, lr, tb, o.To, d, out var toAt))
            return LiftOrderWhy.Refused;
        var lift = WristLift(d);
        if (o.From.Kind == LiftPointKind.Rack)
        {
            if (world.StoreOf(fb, o.From.Part) is not { } st || ConstructCrafting.Available(world, st, (ushort)d.Id) <= 0)
                return LiftOrderWhy.NoPiece;
            if (!RackSpot(fb, o.From.Part, ld, shoulder, lr, SafeLift, lift, out var at))
                return LiftOrderWhy.Unreached;
            if (!world.StationTake(fb, o.From.Part, st, d))
                return LiftOrderWhy.NoPiece;
            world.CraftChanged(fb);
            at.Y += (d.BoundsMax.Y - d.BoundsMin.Y) * 0.5f + 0.05f;
            Freeform.PartWorld(fb, o.From.Part, out _, out var rr);
            StartCarry(world, c, l, ld, lp, lr, d, 255, 0, at, rr, to, toAt);
            return LiftOrderWhy.None;
        }
        if (!PointAt(fb, o.From, out var outAt))
            return LiftOrderWhy.Unreached;
        // A machine's out: the loose piece it dropped there, else its output tray (a press's or an Assembler's).
        var bodies = world.Bodies;
        for (var bi = 0; bi < bodies.Count; bi++)
        {
            var lo = bodies[bi];
            if (lo.PartCount != 1 || lo == fb || lo == c || lo.Parts[0].Def != o.Def || lo.HeldBy != 0 || lo.GrabbedBy != 255 || lo.CarriedBy != 0 || lo.Anchored || lo.Parts[0].Broken || PickWhy(world, lo) != null
                || o.To.Kind is LiftPointKind.Rack or LiftPointKind.Tray && (lo.Parts[0].Health < d.Health * 0.999f || lo.Parts[0].Paint != 0)) // (the one pick rule; a damaged or painted piece never becomes a count)
                continue;
            var reach = 1f + 0.5f * MathF.Max(d.BoundsMax.X - d.BoundsMin.X, MathF.Max(d.BoundsMax.Y - d.BoundsMin.Y, d.BoundsMax.Z - d.BoundsMin.Z));
            if (Vector3.DistanceSquared(lo.WorldCom, outAt) > reach * reach || lo.Vel.LengthSquared() > 0.25f)
                continue;
            Freeform.PartWorld(lo, 0, out var pp, out var pr);
            var at = pp + Vector3.Transform((d.BoundsMin + d.BoundsMax) * 0.5f, pr);
            if (!InReach(ld, shoulder, lr, at, lift, out _))
                continue;
            var health = (byte)Math.Clamp(255 * lo.Parts[0].Health / MathF.Max(1, d.Health), 1, 255);
            var paint = lo.Parts[0].Paint;
            world.Remove(lo);
            StartCarry(world, c, l, ld, lp, lr, d, health, paint, at, pr, to, toAt);
            return LiftOrderWhy.None;
        }
        var md = fb.DefOf(o.From.Part);
        if ((md.IsPress || md.IsAssembler) && world.StoreOf(fb, o.From.Part) is { } tray && ConstructCrafting.Available(world, tray, (ushort)d.Id) > 0
            && InReach(ld, shoulder, lr, outAt, lift, out _) && ConstructCrafting.TakeOne(world, fb, o.From.Part, tray, d))
        {
            world.CraftChanged(fb);
            Wake(fb, o.From.Part);
            Freeform.PartWorld(fb, o.From.Part, out _, out var mr);
            StartCarry(world, c, l, ld, lp, lr, d, 255, 0, outAt, mr, to, toAt);
            return LiftOrderWhy.None;
        }
        return LiftOrderWhy.NoPiece;
    }

    /// <summary>Does this put point take the piece now, and where does its middle go?</summary>
    private static bool OrderPut(ConstructWorld world, CLift l, LifterDef ld, Vector3 shoulder, Quaternion lr, Construct b, in LiftPoint pt, ConstructPartDef d, out Vector3 at, bool asNew = true)
    {
        at = default;
        var lift = WristLift(d);
        if (pt.Part >= b.PartCount || b.Parts[pt.Part].Broken || !asNew && pt.Kind is LiftPointKind.Rack or LiftPointKind.Tray)
            return false;
        if (pt.Kind == LiftPointKind.Rack)
        {
            if (world.StoreOf(b, pt.Part) is not { } st || ConstructConveyor.RackCheck(b, pt.Part, st, d) != 0 || !StockTargets.Accepts(world, b, pt.Part, st, d))
                return false;
            if (!RackSpot(b, pt.Part, ld, shoulder, lr, lift, lift, out at))
                return false;
        }
        else
        {
            if (!Wants(world, b, pt, d) || Bound(world, l, pt))
                return false;
            if (pt.Kind == LiftPointKind.Tray ? !RackSpot(b, pt.Part, ld, shoulder, lr, lift, lift, out at) : !PointAt(b, pt, out at))
                return false;
        }
        at.Y += (d.BoundsMax.Y - d.BoundsMin.Y) * 0.5f + 0.05f;
        return InReach(ld, shoulder, lr, at, lift, out _);
    }

    /// <summary>Server: the carry for an order put its piece: one done; the order goes when all are.</summary>
    private static void OrderPutDone(ConstructWorld world, CLift l)
    {
        var id = l.OrderId;
        l.OrderId = 0;
        if (id == 0 || l.Orders == null)
            return;
        for (var oi = 0; oi < l.OrderN; oi++)
        {
            ref var o = ref l.Orders[oi];
            if (o.Id != id)
                continue;
            o.Done++;
            o.Why = LiftOrderWhy.None;
            if (o.Done >= o.Count)
                OrderRemove(l, oi);
            return;
        }
    }

    private static void OrderRemove(CLift l, int at)
    {
        for (var k = at; k < l.OrderN - 1; k++)
            l.Orders![k] = l.Orders[k + 1];
        l.OrderN--;
    }

    /// <summary>
    /// Server: a player's order at the lifter (checked by <see cref="ConstructEdits.LifterAct"/>). Null, or why not. The points must be
    /// among the lifter's points now: picks from a rack, a pad or a machine's out, puts anywhere else.
    /// </summary>
    public static string? Order(ConstructWorld world, Construct c, int part, LiftOrderOp op, byte id, ushort def, int count, in LiftPoint from, in LiftPoint to)
    {
        if (DefOf(c, part) is not { } ld)
            return "that isn't a lifter";
        var lb = ScanBody(world, c, server: true);
        CLift? l = null;
        for (var i = 0; i < lb.N; i++)
            if (lb.Lifts[i].Part == part)
                l = lb.Lifts[i];
        if (l == null)
            return "that lifter isn't working yet";
        var name = c.DefOf(part).Name;
        switch (op)
        {
            case LiftOrderOp.Clear:
                if (l.OrderN == 0)
                    return "it has no orders";
                l.OrderN = 0;
                world.LiftChanged(c.Id);
                return null;
            case LiftOrderOp.Cancel:
                for (var oi = 0; oi < l.OrderN; oi++)
                    if (l.Orders![oi].Id == id)
                    {
                        OrderRemove(l, oi);
                        world.LiftChanged(c.Id);
                        return null;
                    }
                return "that order is done or gone";
        }
        if (ConstructParts.Get(def) is not { } d)
            return "no such part";
        if (count < 1 || count > MaxOrderCount)
            return $"from 1 to {MaxOrderCount} at a time";
        if (l.OrderN >= MaxOrders)
            return $"the {name} has {MaxOrders} orders already: cancel one";
        if (!Picks(from.Kind) || !Puts(to.Kind) || Same(from, to))
            return "pick from a rack, a pad or a machine's out, and put somewhere else";
        if (!Carries(ld, d))
            return CarryRefusal(ld, d);
        FindPoints(world, c, l, ld); // (fresh: the panel's list was this peer's own, worked out from what it sees)
        if (PointIndex(l, from) < 0 || PointIndex(l, to) < 0)
            return $"that is out of the {name}'s reach";
        l.Orders ??= new LiftOrder[MaxOrders];
        var nid = (byte)(l.OrderSeq = (byte)(l.OrderSeq % 255 + 1));
        l.Orders[l.OrderN++] = new LiftOrder { Id = nid, Def = (ushort)d.Id, Count = (byte)count, From = from, To = to };
        l.OrderLook = CarryLook; // (looked at on its next step)
        world.LiftChanged(c.Id);
        Console.WriteLine($"[lifter] body {c.Id} part {part}: hand order {nid}: {count} {d.Name} from {from.Kind} part {from.Part} of body {from.Body} to {to.Kind} part {to.Part} of body {to.Body}");
        return null;
    }

    /// <summary>"too heavy: 2.4 t, this Arm carries 1 t", "too long: 6.0 m, it takes 5 m".</summary>
    public static string CarryRefusal(LifterDef ld, ConstructPartDef d)
    {
        static string Kg(float kg) => kg >= 1000 ? $"{kg / 1000f:0.#} t" : $"{kg:0} kg";
        if (d.Mass > ld.Payload)
            return $"too heavy: {Kg(d.Mass)}, it carries {Kg(ld.Payload)}";
        var s = d.BoundsMax - d.BoundsMin;
        var longest = MathF.Max(s.X, MathF.Max(s.Y, s.Z));
        return longest > ld.PieceLong ? $"too long: {longest:0.0} m, it takes {ld.PieceLong:0.#} m" : $"too big across: it takes {ld.PieceAcross:0.#} m";
    }

    // ---- The panel (either peer) -------------------------------------------------------------------------------------------

    /// <summary>The points a lifter reaches as this peer sees them (the panel's list; the server checks an order against its own). How many.</summary>
    public static int PanelPoints(ConstructWorld world, Construct c, int part, LiftPoint[] into)
    {
        if (DefOf(c, part) is not { } ld)
            return 0;
        Frame(c, part, ld, out _, out var lr, out var shoulder);
        var n = 0;
        var bodies = world.Bodies;
        for (var bi = 0; bi < bodies.Count && n < into.Length; bi++)
        {
            var b = bodies[bi];
            if (Vector3.Distance(b.Pos, shoulder) > ld.ReachMax + ld.Up + ld.Down + b.Radius)
                continue;
            PointsOn(b, b == c ? part : -1, ld, shoulder, lr, into, ref n);
        }
        return n;
    }

    /// <summary>A point's name on the panel: "Piece Rack", "Press bay 2", "Extruder (out)", "Crafting Terminal (tray)".</summary>
    public static string PointName(ConstructWorld world, in LiftPoint pt)
    {
        var name = world.Get(pt.Body) is { } b && pt.Part < b.PartCount ? b.DefOf(pt.Part).Name : "gone";
        return pt.Kind switch
        {
            LiftPointKind.Out => name + " (out)",
            LiftPointKind.PressBay => $"{name} bay {pt.Bay + 1}",
            LiftPointKind.Tray => name + " (tray)",
            LiftPointKind.Rack => name,
            _ => name + " (in)",
        };
    }

    /// <summary>
    /// What a pick point holds as this peer sees it: kinds and counts into the arrays, how many kinds. A rack's or pad's hold; a machine's
    /// out: the loose pieces at it and its tray.
    /// </summary>
    public static int PointHolds(ConstructWorld world, in LiftPoint pt, ushort[] defs, int[] counts)
    {
        var n = 0;
        if (world.Get(pt.Body) is not { } b || pt.Part >= b.PartCount)
            return 0;
        void Add(ushort def, int k)
        {
            if (def == ConstructParts.None || k <= 0)
                return;
            for (var i = 0; i < n; i++)
                if (defs[i] == def)
                {
                    counts[i] += k;
                    return;
                }
            if (n < defs.Length)
                (defs[n], counts[n++]) = (def, k);
        }
        if (pt.Kind == LiftPointKind.Rack)
        {
            if (world.StoreOf(b, pt.Part) is { KindDef: { } kd } st)
                for (var q = 0; q < st.KindN; q++)
                    Add(kd[q], ConstructCrafting.Holds(st, kd[q]));
            return n;
        }
        if (pt.Kind != LiftPointKind.Out || !PointAt(b, pt, out var outAt))
            return 0;
        foreach (var lo in world.Bodies)
        {
            if (lo.PartCount != 1 || lo == b || lo.Parts[0].Broken || lo.Anchored)
                continue;
            var d = lo.DefOf(0);
            var reach = 1f + 0.5f * MathF.Max(d.BoundsMax.X - d.BoundsMin.X, MathF.Max(d.BoundsMax.Y - d.BoundsMin.Y, d.BoundsMax.Z - d.BoundsMin.Z));
            if (Vector3.DistanceSquared(lo.WorldCom, outAt) <= reach * reach)
                Add(lo.Parts[0].Def, 1);
        }
        var md = b.DefOf(pt.Part);
        if ((md.IsPress || md.IsAssembler) && world.StoreOf(b, pt.Part) is { } tray)
            for (var q = 0; q < tray.SlotCount; q++)
                Add(tray.Def[q], tray.Count[q]);
        return n;
    }

    /// <summary>An order's row: "3 Hull Plate: Lay-down Pad -> Press bay 1 (1 done) - waiting: none at the pick point".</summary>
    public static string OrderText(ConstructWorld world, in LiftOrder o)
    {
        var kind = ConstructParts.Get(o.Def)?.Name ?? "part";
        var why = o.Why switch
        {
            LiftOrderWhy.NoPiece => " - waiting: none there yet",
            LiftOrderWhy.Refused => " - waiting: the put point doesn't take it now",
            LiftOrderWhy.Unreached => " - waiting: out of reach",
            LiftOrderWhy.TooBig => " - it can't carry that",
            LiftOrderWhy.Gone => " - that part is gone",
            _ => "",
        };
        return $"{o.Count} {kind}: {PointName(world, o.From)} -> {PointName(world, o.To)}{(o.Done > 0 ? $" ({o.Done} done)" : "")}{why}";
    }

    // ---- The wire (after each lifter in the lift word, protocol 312) --------------------------------------------------------

    private static void WritePoint(PacketWriter w, in LiftPoint p) => w.U16(p.Body).U16(p.Part).U8((byte)p.Kind).U8(p.Bay);

    private static LiftPoint ReadPoint(ref PacketReader r) => new() { Body = r.U16(), Part = r.U16(), Kind = (LiftPointKind)r.U8(), Bay = r.U8() };

    private static void WriteOrders(PacketWriter w, CLift l)
    {
        w.U8((byte)(l.Steered ? 1 : 0)); // (phase 8: its cab steers it)
        w.U8(l.OrderId).U8((byte)l.OrderN);
        for (var oi = 0; oi < l.OrderN; oi++)
        {
            ref readonly var o = ref l.Orders![oi];
            w.U8(o.Id);
            ConstructStorage.WriteDef(w, o.Def);
            w.U8(o.Count).U8(o.Done).U8((byte)o.Why);
            WritePoint(w, o.From);
            WritePoint(w, o.To);
        }
    }

    /// <summary>A lifter's orders off the wire (into <paramref name="l"/> when it is one this peer knows, else read past).</summary>
    private static void ReadOrders(ref PacketReader r, CLift? l)
    {
        var steered = r.U8() != 0; // (phase 8)
        var run = r.U8();
        int n = r.U8();
        if (l != null)
        {
            l.OrderId = run;
            l.Steered = steered;
            l.OrderN = 0;
            if (n > 0)
                l.Orders ??= new LiftOrder[MaxOrders];
        }
        for (var oi = 0; oi < n; oi++)
        {
            var o = new LiftOrder { Id = r.U8(), Def = ConstructStorage.ReadDef(ref r), Count = r.U8(), Done = r.U8(), Why = (LiftOrderWhy)r.U8() };
            o.From = ReadPoint(ref r);
            o.To = ReadPoint(ref r);
            if (l != null && l.OrderN < MaxOrders)
                l.Orders![l.OrderN++] = o;
        }
    }

    // ---- The save (the LIFT block's second half) ----------------------------------------------------------------------------

    /// <summary>A body's lifters' orders for a save (a point on this body is saved as body 0), or null.</summary>
    public static (ushort Lifter, LiftOrder Order)[]? SnapshotOrders(ConstructWorld world, Construct c)
    {
        if (!world.LiftSeen.TryGetValue(c.Id, out var lb) || lb.N == 0)
            return null;
        var n = 0;
        for (var i = 0; i < lb.N; i++)
            n += lb.Lifts[i].OrderN;
        if (n == 0)
            return null;
        var o = new (ushort, LiftOrder)[n];
        n = 0;
        for (var i = 0; i < lb.N; i++)
            for (var oi = 0; oi < lb.Lifts[i].OrderN; oi++)
            {
                var e = lb.Lifts[i].Orders![oi];
                if (e.From.Body == c.Id)
                    e.From.Body = 0;
                if (e.To.Body == c.Id)
                    e.To.Body = 0;
                o[n++] = (lb.Lifts[i].Part, e);
            }
        return o;
    }

    /// <summary>A load: the saved orders back on their lifters (body 0: this body). An order for a part that is no longer a lifter goes.</summary>
    public static void ApplyOrders(ConstructWorld world, Construct c, (ushort Lifter, LiftOrder Order)[] orders)
    {
        var lb = ScanBody(world, c, server: true);
        foreach (var (part, saved) in orders)
        {
            var o = saved;
            if (o.Def == ConstructParts.None || o.Count == 0)
                continue;
            if (o.From.Body == 0)
                o.From.Body = c.Id;
            if (o.To.Body == 0)
                o.To.Body = c.Id;
            for (var i = 0; i < lb.N; i++)
            {
                var l = lb.Lifts[i];
                if (l.Part != part || l.OrderN >= MaxOrders)
                    continue;
                l.Orders ??= new LiftOrder[MaxOrders];
                o.Id = l.OrderSeq = (byte)(l.OrderSeq % 255 + 1);
                o.Why = LiftOrderWhy.None;
                l.Orders[l.OrderN++] = o;
            }
        }
        world.LiftChanged(c.Id);
    }
}

public sealed partial class ConstructEdits
{
    /// <summary>
    /// Server: a player's hand order at a lifter (CActKind.Lifter), under the rule of a finger on a console: reach to the lifter (a crane's
    /// cabinet from anywhere under its rails' height: its panel is a pendant), who may configure its construct. Null, or why not.
    /// </summary>
    public string? LifterAct(ConstructWorld world, byte player, Vector3 eye, ushort body, ushort part, LiftOrderOp op, byte id, ushort def, int count, in LiftPoint from, in LiftPoint to, string? refusal)
    {
        if (world.Get(body) is not { } c || part >= c.PartCount)
            return "that lifter is gone";
        if (ConstructLifters.DefOf(c, part) is not { } ld)
            return $"a {c.DefOf(part).Name} takes no orders";
        if (!InfiniteParts && ConstructGrab.Defended(c, player, AliveAt, NameOf) is { } defended)
            return defended;
        if (refusal != null)
            return refusal;
        if (Vector3.Distance(eye, ReachPoint(c, part, eye)) > ConstructLifters.PanelReach(ld))
            return "too far away";
        var why = ConstructLifters.Order(world, c, part, op, id, def, count, from, to);
        if (why == null)
            Done.Add($"player {player}: lifter order {op} at part {part} of construct {c.Id}");
        return why;
    }
}

public static partial class ConstructLifters
{
    /// <summary>How far a player's eye may be from a lifter to use its panel: an arm's at hand; a crane's from the floor under its rails (a pendant).</summary>
    public static float PanelReach(LifterDef ld) => ld.Bridge ? ConstructConfig.Reach + ld.Depth + ld.Down + 2f : ConstructConfig.Reach;
}
