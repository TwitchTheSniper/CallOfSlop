using System.Numerics;

namespace CallOfSlop;

/// <summary>What a lifter's point is (arms-and-cranes.md 3.5): where it picks, where it puts, and through what.</summary>
public enum LiftPointKind : byte
{
    None, // (a station step or a put-back: not a carry between points)
    Rack, // a Piece Rack or a Lay-down Pad: pick (a want's piece) and put (a want, or storage)
    Out, // a machine's Piece out with no conveyor on it: pick (the piece it holds there for a lifter)
    Assembler, // an Assembler's Piece in with no conveyor: put (a part its recipe uses)
    PressBay, // a press bay's Piece in with no conveyor: put (a blank it takes, or a kit part for its hopper)
    Former, // a Panel Former's Piece in with no conveyor: put (sheet for its bin)
}

/// <summary>A point in a lifter's reach (server; found every <see cref="ConstructLifters.PointEvery"/> ticks, never per tick).</summary>
public struct LiftPoint
{
    public ushort Body, Part;
    public LiftPointKind Kind;
    public byte Bay; // a press bay's index
}

/// <summary>A piece a machine with no conveyor on its Piece out holds there for a lifter (server; at most one kind, <see cref="ConstructLifters.OutCap"/> of it).</summary>
public struct LiftOut
{
    public ushort Body, Part, Def;
    public byte Count, Health;
    public uint Paint;
}

/// <summary>
/// Lifters phase 4 (docs/sandbox/design/arms-and-cranes.md section 5, the user's answer 5): carrying between points by the Routers'
/// rule. An idle arm with no station step looks at its points (racks, Lay-down Pads, machines' Piece in and Piece out with no
/// conveyor on them, on any construct in its reach) about once a second: a piece a machine holds at its Piece out goes to a put point
/// that wants it, else to storage (the nearest rack or pad with room that takes it); a piece in a rack or pad goes only to a want
/// (never from storage to storage). A put refused at the end looks for another taker, else the piece waits in the hand (Stuck,
/// Alarm) and is looked at again every 2 s. Server only; the wire is the lift word. Nothing allocates after a lifter's first look.
/// </summary>
public static partial class ConstructLifters
{
    public const int MaxPoints = 16; // a lifter's points
    public const int PointEvery = 120; // ticks between looks for a lifter's points (2 s)
    public const int OutCap = 8; // pieces a machine holds at its Piece out for lifters
    public const int CarryLook = 6; // 10 Hz steps between an idle lifter's looks for a carry (about 1 s)

    // ---- Points ------------------------------------------------------------------------------------------------------------

    /// <summary>A part's world point for a part-frame point.</summary>
    private static Vector3 PartPoint(Construct b, int part, Vector3 local)
    {
        ref readonly var p = ref b.Parts[part];
        b.WorldFrame(p.Seg, out var q, out var t);
        return t + Vector3.Transform(p.Pos + Rot24.Apply(p.Rot, local), q);
    }

    private static NamedPort? ConveyorPort(ConstructPartDef d, string name)
    {
        foreach (var np in d.NamedPorts)
            if (np.Type == RouteType.Conveyor && np.Name == name)
                return np;
        return null;
    }

    /// <summary>Does a conveyor end at this part's port?</summary>
    private static bool ConveyorInto(Construct b, int part, byte port)
    {
        for (var k = 0; k < b.RouteCount; k++)
            if (b.Routes[k].Type == RouteType.Conveyor && b.Routes[k].To == part && b.Routes[k].ToPort == port)
                return true;
        return false;
    }

    /// <summary>A point's place in the world: where a piece is put or picked (its middle, before the piece's own half height).</summary>
    public static bool PointAt(Construct b, in LiftPoint pt, out Vector3 at)
    {
        at = default;
        if (pt.Part >= b.PartCount)
            return false;
        var d = b.DefOf(pt.Part);
        switch (pt.Kind)
        {
            case LiftPointKind.Rack:
                at = PartPoint(b, pt.Part, new Vector3((d.RackHoldMin.X + d.RackHoldMax.X) * 0.5f, d.RackHoldMin.Y, (d.RackHoldMin.Z + d.RackHoldMax.Z) * 0.5f));
                return true;
            case LiftPointKind.Out:
                at = PartPoint(b, pt.Part, d.IsExtruder ? d.ExtDieAt : d.AsmOutAt + d.AsmOutDir * 0.6f);
                return true;
            default:
                var name = pt.Kind == LiftPointKind.PressBay ? BayPortNames[Math.Min((int)pt.Bay, BayPortNames.Length - 1)] : "Piece in";
                if (ConveyorPort(d, name) is not { } np)
                    return false;
                at = PartPoint(b, pt.Part, np.At);
                return true;
        }
    }

    /// <summary>The rack or pad point nearest a shoulder that the wrist reaches with both lifts: over its hold, on a grid every half metre.</summary>
    private static bool RackSpot(Construct b, int part, LifterDef ld, Vector3 shoulder, Quaternion rot, float lift, float lift2, out Vector3 at)
    {
        var d = b.DefOf(part);
        var lo = d.RackHoldMin;
        var hi = d.RackHoldMax;
        var nx = Math.Clamp((int)MathF.Ceiling((hi.X - lo.X) / 0.5f), 1, 32);
        var nz = Math.Clamp((int)MathF.Ceiling((hi.Z - lo.Z) / 0.5f), 1, 64);
        var best = float.MaxValue;
        at = default;
        for (var xi = 0; xi <= nx; xi++)
            for (var zi = 0; zi <= nz; zi++)
            {
                var p = PartPoint(b, part, new Vector3(lo.X + (hi.X - lo.X) * xi / nx, lo.Y, lo.Z + (hi.Z - lo.Z) * zi / nz));
                var dist = Vector3.DistanceSquared(p, shoulder);
                if (dist >= best || !InReach(ld, shoulder, rot, p, lift, out _) || lift2 != lift && !InReach(ld, shoulder, rot, p, lift2, out _))
                    continue;
                best = dist;
                at = p;
            }
        return best < float.MaxValue;
    }

    /// <summary>Server: a lifter's points again (every 2 s, or when it has none yet): what it reaches with an empty hand, 16 at most.</summary>
    private static void FindPoints(ConstructWorld world, Construct c, CLift l, LifterDef ld)
    {
        l.Points ??= new LiftPoint[MaxPoints];
        l.PointsAt = world.LiftTick;
        l.PointN = 0;
        Frame(c, l.Part, ld, out _, out var lr, out var shoulder);
        var bodies = world.Bodies;
        for (var bi = 0; bi < bodies.Count && l.PointN < MaxPoints; bi++)
        {
            var b = bodies[bi];
            if (Vector3.Distance(b.Pos, shoulder) > ld.ReachMax + ld.Up + ld.Down + b.Radius)
                continue;
            for (var i = 0; i < b.PartCount && l.PointN < MaxPoints; i++)
            {
                if (b == c && i == l.Part || b.Parts[i].Broken)
                    continue;
                var d = b.DefOf(i);
                if (d.IsPieceRack)
                {
                    if (!d.IsBuildStation && !d.IsCraftTerminal && RackSpot(b, i, ld, shoulder, lr, SafeLift, SafeLift, out _))
                        AddPoint(l, b, i, LiftPointKind.Rack, 0);
                    continue;
                }
                var maker = d.IsAssembler || d.IsPress || d.IsExtruder && !d.IsPanelFormer;
                if (maker && ConstructConveyor.Out(b, i) < 0 && PointAt(b, new LiftPoint { Body = b.Id, Part = (ushort)i, Kind = LiftPointKind.Out }, out var o)
                    && InReach(ld, shoulder, lr, o, SafeLift, out _))
                    AddPoint(l, b, i, LiftPointKind.Out, 0);
                if (d.IsAssembler || d.IsPanelFormer)
                    TryIn(l, b, i, d, d.IsAssembler ? LiftPointKind.Assembler : LiftPointKind.Former, 0, "Piece in", ld, shoulder, lr);
                else if (d.IsPress)
                    for (var bay = 0; bay < d.PressBays.Length && bay < 8; bay++)
                        TryIn(l, b, i, d, LiftPointKind.PressBay, bay, bay == 0 ? "Piece in" : BayPortNames[bay], ld, shoulder, lr);
            }
        }
    }

    private static readonly string[] BayPortNames = ["Piece in", "Piece in 2", "Piece in 3", "Piece in 4", "Piece in 5", "Piece in 6", "Piece in 7", "Piece in 8"];

    private static void TryIn(CLift l, Construct b, int i, ConstructPartDef d, LiftPointKind kind, int bay, string port, LifterDef ld, Vector3 shoulder, Quaternion lr)
    {
        if (l.PointN >= MaxPoints || ConveyorPort(d, port) is not { } np || ConveyorInto(b, i, np.Byte))
            return;
        if (InReach(ld, shoulder, lr, PartPoint(b, i, np.At), SafeLift, out _))
            AddPoint(l, b, i, kind, (byte)bay);
    }

    private static void AddPoint(CLift l, Construct b, int part, LiftPointKind kind, byte bay) =>
        l.Points![l.PointN++] = new LiftPoint { Body = b.Id, Part = (ushort)part, Kind = kind, Bay = bay };

    // ---- What a machine holds at its Piece out --------------------------------------------------------------------------------

    /// <summary>Server: is a machine's Piece out one of a lifter's points (so it holds what it makes there instead of dropping it)?</summary>
    public static bool Serves(ConstructWorld world, Construct c, int machine)
    {
        for (var bi = 0; bi < world.LiftBodies.Count; bi++)
        {
            var lb = world.LiftBodies[bi];
            for (var i = 0; i < lb.N; i++)
            {
                var l = lb.Lifts[i];
                for (var k = 0; k < l.PointN; k++)
                    if (l.Points![k].Kind == LiftPointKind.Out && l.Points[k].Body == c.Id && l.Points[k].Part == machine)
                        return true;
            }
        }
        return false;
    }

    private static int OutIndex(ConstructWorld world, ushort body, int part)
    {
        for (var k = 0; k < world.LiftOuts.Count; k++)
            if (world.LiftOuts[k].Body == body && world.LiftOuts[k].Part == part)
                return k;
        return -1;
    }

    /// <summary>
    /// Server, a machine with no conveyor on its Piece out (ConstructAssembling.Leave: Assemblers and presses): 0 no lifter serves it
    /// (it drops the piece as before), 1 the piece is held there for a lifter, 2 a lifter serves it but its out is full (it waits).
    /// </summary>
    public static int HoldOut(ConstructWorld world, Construct c, int machine, ConstructPartDef d, byte health = 255, uint paint = 0)
    {
        if (world.LiftBodies.Count == 0 || !Serves(world, c, machine))
            return 0;
        var k = OutIndex(world, c.Id, machine);
        if (k < 0)
        {
            world.LiftOuts.Add(new LiftOut { Body = c.Id, Part = (ushort)machine, Def = (ushort)d.Id, Count = 1, Health = health, Paint = paint });
            return 1;
        }
        var o = world.LiftOuts[k];
        if (o.Def != d.Id || o.Count >= OutCap)
            return 2;
        o.Count++;
        world.LiftOuts[k] = o;
        return 1;
    }

    /// <summary>
    /// Server, an Extruder's plain cut with no conveyor on its Piece out (ConstructExtruder): 0 no lifter serves it (it drops as before),
    /// 1 the piece left the die into the machine's out for a lifter (the part is gone: indices moved), 2 the out is full (it waits on the die).
    /// </summary>
    public static int CutToHand(ConstructWorld world, Construct c, int machine, int product)
    {
        if (world.LiftBodies.Count == 0 || !Serves(world, c, machine))
            return 0;
        ref var pp = ref c.Parts[product];
        if (Extrusion.Def(pp.D.ExKey with { Growing = false }) is not { } piece)
            return 0;
        var health = (byte)Math.Clamp(255 * pp.Health / MathF.Max(1, pp.D.Health), 1, 255);
        var r = HoldOut(world, c, machine, piece, health, pp.Paint);
        if (r == 1)
        {
            world.RemovePart(c, product);
            c.Wake();
        }
        return r;
    }

    /// <summary>Server: a machine's held pieces when it or its body went (set down loose at its out).</summary>
    private static void DropOuts(ConstructWorld world)
    {
        for (var k = world.LiftOuts.Count - 1; k >= 0; k--)
        {
            var o = world.LiftOuts[k];
            var b = world.Get(o.Body);
            if (b != null && o.Part < b.PartCount && b.DefOf(o.Part) is { } md && (md.IsAssembler || md.IsPress || md.IsExtruder))
                continue;
            world.LiftOuts.RemoveAt(k);
            if (ConstructParts.Get(o.Def) is { } d && b != null)
                for (var n = 0; n < o.Count; n++)
                    ConstructBuild.PlaceNew(world, d, b.Pos + new Vector3(0, 2 + n * 0.5f, 0), 0);
        }
    }

    // ---- The rule ----------------------------------------------------------------------------------------------------------

    /// <summary>Would this point take this piece now (a want)? Racks are storage, never a want here.</summary>
    private static bool Wants(ConstructWorld world, Construct b, in LiftPoint pt, ConstructPartDef d)
    {
        if (pt.Part >= b.PartCount || b.Parts[pt.Part].Broken)
            return false;
        switch (pt.Kind)
        {
            case LiftPointKind.Assembler:
                return !d.IsExtrusion && world.WantsPart(b, pt.Part, (ushort)d.Id);
            case LiftPointKind.PressBay:
                var md = b.DefOf(pt.Part);
                if (!d.IsExtrusion)
                    return !md.IsLathe && ConstructPress.WantsKitPart(world, b, pt.Part, (ushort)d.Id);
                if (d.ExKey.Shape == ExShape.Wheel || ConstructPress.PieceIn(b, pt.Part, pt.Bay) >= 0 || !md.IsLathe && pt.Bay == ConstructPress.HopperBay)
                    return false;
                return ConstructPress.Refusal(PressTable.Current, b, pt.Part, pt.Bay, d.ExKey) == BayRefuse.None;
            case LiftPointKind.Former:
                if (!d.IsExtrusion || d.ExKey.Shape != ExShape.Section || Math.Min(d.ExKey.W, d.ExKey.H) > ConstructPanelFormer.SheetMaxCm || world.StoreOf(b, pt.Part) is not { } st)
                    return false;
                var units = Math.Max(1, (int)MathF.Round(d.Mass / MathF.Max(0.01f, RecyclingTable.Current.WeightOf(d.ExKey.Material))));
                return (st.Amount == 0 || st.Material == d.ExKey.Material) && st.Amount + units <= b.DefOf(pt.Part).BulkCapacity;
            default:
                return false;
        }
    }

    /// <summary>Is another hand already bound for this put point (a press bay or a machine's input: one at a time)?</summary>
    private static bool Bound(ConstructWorld world, CLift self, in LiftPoint pt)
    {
        if (pt.Kind == LiftPointKind.Rack)
            return false;
        for (var bi = 0; bi < world.LiftBodies.Count; bi++)
        {
            var lb = world.LiftBodies[bi];
            for (var i = 0; i < lb.N; i++)
            {
                var o = lb.Lifts[i];
                if (o != self && o.State == LiftState.Carry && o.ToKind == pt.Kind && o.StationBody == pt.Body && o.ToPart == pt.Part && o.ToBay == pt.Bay)
                    return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Where a piece goes (the Routers' rule over this lifter's points): a put point that wants it, else (<paramref name="storage"/>)
    /// the nearest rack or pad with room that takes it. <paramref name="skip"/>: a point it came from (never back to it). -1: none.
    /// </summary>
    private static int PutFor(ConstructWorld world, CLift l, LifterDef ld, Vector3 shoulder, Quaternion lr, ConstructPartDef d, bool storage, int skip, out Vector3 at)
    {
        at = default;
        var lift = WristLift(d);
        for (var k = 0; k < l.PointN; k++)
        {
            ref readonly var pt = ref l.Points![k];
            if (k == skip || pt.Kind is LiftPointKind.Rack or LiftPointKind.Out || world.Get(pt.Body) is not { } b)
                continue;
            if (!Wants(world, b, pt, d) || Bound(world, l, pt) || !PointAt(b, pt, out var p))
                continue;
            p.Y += (d.BoundsMax.Y - d.BoundsMin.Y) * 0.5f + 0.05f;
            if (!InReach(ld, shoulder, lr, p, lift, out _))
                continue;
            at = p;
            return k;
        }
        if (!storage)
            return -1;
        var best = -1;
        var bestDist = float.MaxValue;
        for (var k = 0; k < l.PointN; k++)
        {
            ref readonly var pt = ref l.Points![k];
            if (k == skip || pt.Kind != LiftPointKind.Rack || world.Get(pt.Body) is not { } b || pt.Part >= b.PartCount || world.StoreOf(b, pt.Part) is not { } st)
                continue;
            if (ConstructConveyor.RackCheck(b, pt.Part, st, d) != 0 || !StockTargets.Accepts(world, b, pt.Part, st, d))
                continue;
            if (!RackSpot(b, pt.Part, ld, shoulder, lr, lift, lift, out var p))
                continue;
            var dist = Vector3.DistanceSquared(p, shoulder) - StockTargets.Priority(b, pt.Part) * 1e4f;
            if (dist >= bestDist)
                continue;
            bestDist = dist;
            best = k;
            at = p + Vector3.UnitY * ((d.BoundsMax.Y - d.BoundsMin.Y) * 0.5f + 0.05f);
        }
        return best;
    }

    /// <summary>
    /// Server: an idle lifter's look for a carry. A machine's held piece goes to a want, else storage; a rack's or pad's piece only to
    /// a want. True when a carry began.
    /// </summary>
    private static bool Carry(ConstructWorld world, Construct c, CLift l, LifterDef ld)
    {
        if (l.Points == null || world.LiftTick - l.PointsAt >= PointEvery)
            FindPoints(world, c, l, ld);
        if (l.PointN == 0)
        {
            Why(world, l, LiftWhy.NoPoint);
            return false;
        }
        Frame(c, l.Part, ld, out var lp, out var lr, out var shoulder);
        for (var k = 0; k < l.PointN; k++)
        {
            var pt = l.Points![k];
            if (pt.Kind != LiftPointKind.Out || OutIndex(world, pt.Body, pt.Part) is not (var oi and >= 0) || world.Get(pt.Body) is not { } b)
                continue;
            var o = world.LiftOuts[oi];
            if (ConstructParts.Get(o.Def) is not { } d || !Carries(ld, d) || !PointAt(b, pt, out var from))
                continue;
            from.Y += (d.BoundsMax.Y - d.BoundsMin.Y) * 0.5f + 0.05f;
            if (!InReach(ld, shoulder, lr, from, WristLift(d), out _))
                continue;
            var to = PutFor(world, l, ld, shoulder, lr, d, storage: true, skip: k, out var toAt);
            if (to < 0)
                continue;
            o.Count--;
            if (o.Count == 0)
                world.LiftOuts.RemoveAt(oi);
            else
                world.LiftOuts[oi] = o;
            Wake(b, pt.Part);
            StartCarry(world, c, l, ld, lp, lr, d, o.Health, o.Paint, from, Quaternion.Identity, to, toAt);
            return true;
        }
        for (var k = 0; k < l.PointN; k++)
        {
            var pt = l.Points![k];
            if (pt.Kind != LiftPointKind.Rack || world.Get(pt.Body) is not { } b || pt.Part >= b.PartCount || world.StoreOf(b, pt.Part) is not { ItemCount: > 0 } st || st.KindDef == null)
                continue;
            for (var q = 0; q < st.KindN; q++)
            {
                var def = st.KindDef[q];
                if (ConstructCrafting.Holds(st, def) <= 0 || ConstructParts.Get(def) is not { } d || !Carries(ld, d))
                    continue;
                var to = PutFor(world, l, ld, shoulder, lr, d, storage: false, skip: k, out var toAt);
                if (to < 0 || !RackSpot(b, pt.Part, ld, shoulder, lr, SafeLift, WristLift(d), out var from))
                    continue;
                if (!world.StationTake(b, pt.Part, st, d))
                    continue;
                world.CraftChanged(b);
                from.Y += (d.BoundsMax.Y - d.BoundsMin.Y) * 0.5f + 0.05f;
                Freeform.PartWorld(b, pt.Part, out _, out var rr);
                StartCarry(world, c, l, ld, lp, lr, d, 255, 0, from, rr, to, toAt);
                return true;
            }
        }
        Why(world, l, LiftWhy.NoPoint);
        return false;
    }

    private static void Wake(Construct b, int part)
    {
        if (b.Parts[part].Store != 0)
            b.Wake();
    }

    /// <summary>A carry begins: <paramref name="from"/> and <paramref name="toAt"/> are the piece's middle in the world.</summary>
    private static void StartCarry(ConstructWorld world, Construct c, CLift l, LifterDef ld, Vector3 lp, Quaternion lr, ConstructPartDef d, byte health, uint paint, Vector3 from, Quaternion fromRot, int to, Vector3 toAt)
    {
        var inv = Quaternion.Conjugate(lr);
        var mid = (d.BoundsMin + d.BoundsMax) * 0.5f;
        var pt = l.Points![to];
        Quaternion toRot = fromRot;
        if (world.Get(pt.Body) is { } tb && pt.Part < tb.PartCount)
            Freeform.PartWorld(tb, pt.Part, out _, out toRot);
        l.Def = (ushort)d.Id;
        l.Paint = paint;
        l.Health = health;
        l.FromRot = Quaternion.Normalize(inv * fromRot);
        l.ToRot = Quaternion.Normalize(inv * toRot);
        l.From = Vector3.Transform(from - Vector3.Transform(mid, fromRot) - lp, inv);
        l.To = Vector3.Transform(toAt - Vector3.Transform(mid, toRot) - lp, inv);
        l.Rise = RiseOf(c, l.Part, ld);
        l.Dur = Ticks(ld, l.From, l.To, true, WristLift(d), l.Rise);
        Begin(l);
        (l.State, l.Why, l.ToHold, l.Moved, l.Fitted) = (LiftState.Carry, LiftWhy.Carrying, false, true, false);
        (l.StationBody, l.StationPart, l.Step, l.Job, l.Plan) = (pt.Body, -1, -1, null, null);
        (l.ToKind, l.ToPart, l.ToBay) = (pt.Kind, pt.Part, pt.Bay);
        LifterProbe.CarriesHeard++;
        LifterProbe.Carries++;
        world.LiftChanged(c.Id);
    }

    /// <summary>The carry's end: the piece into its point. Refused: another taker in reach, else it waits in the hand (Stuck).</summary>
    private static void Put(ConstructWorld world, IWorldQuery? query, Construct c, CLift l, LifterDef ld)
    {
        var d = ConstructParts.Get(l.Def);
        var ok = false;
        if (d != null && world.Get(l.StationBody) is { } b && l.ToPart < b.PartCount)
        {
            int part = l.ToPart;
            switch (l.ToKind)
            {
                case LiftPointKind.Rack:
                    ok = world.RackPut(b, part, d);
                    if (ok && world.StockTargetN > 0)
                        ConstructLogisticsPlan.Arrived(world, b, part, 0, d); // (a stock target's refill counts it, as a conveyor's delivery does)
                    break;
                case LiftPointKind.Assembler:
                    ok = world.WantsPart(b, part, (ushort)d.Id) && world.FeedPart(b, part, d);
                    break;
                case LiftPointKind.PressBay:
                    ok = query != null && ConstructPress.TakeFromConveyor(world, query, b, part, l.ToBay, d, l.Health / 255f, l.Paint);
                    break;
                case LiftPointKind.Former:
                    ok = ConstructPanelFormer.TakeFromConveyor(world, b, part, d);
                    break;
            }
            if (ok)
            {
                world.CraftChanged(b);
                b.Wake();
            }
        }
        if (ok)
        {
            Console.WriteLine($"[lifter] body {l.Body} part {l.Part}: put a {d!.Name} into {l.ToKind} part {l.ToPart} of body {l.StationBody}");
            l.Moves++;
            l.Fitted = true;
            l.Why = LiftWhy.Carrying; // (kept on the way back: the look line says it goes back for the next piece)
            Back(c, l, ld, null, -1); // (the empty hand goes back to where it picked)
            l.ToKind = LiftPointKind.None;
            return;
        }
        if (d != null && Redirect(world, c, l, ld, d))
            return;
        (l.State, l.ToHold, l.Why, l.Age, l.From, l.FromRot, l.ToKind) = (LiftState.Stuck, false, LiftWhy.NoTaker, l.Dur, l.To, l.ToRot, LiftPointKind.None);
        Console.WriteLine($"[lifter] body {l.Body} part {l.Part}: its {d?.Name ?? "piece"} was refused; nothing in reach takes it (stuck)");
    }

    /// <summary>Server: a piece in the hand to another taker in reach (a want, else storage). True when it set off.</summary>
    private static bool Redirect(ConstructWorld world, Construct c, CLift l, LifterDef ld, ConstructPartDef d)
    {
        if (l.Points == null || world.LiftTick - l.PointsAt >= PointEvery)
            FindPoints(world, c, l, ld);
        Frame(c, l.Part, ld, out var lp, out var lr, out var shoulder);
        var to = PutFor(world, l, ld, shoulder, lr, d, storage: true, skip: -1, out var toAt);
        if (to < 0)
            return false;
        var hr = l.ToRot;
        var here = l.State == LiftState.Carry ? Pose(ld, l, l.Age, out hr) : l.To;
        var mid = (d.BoundsMin + d.BoundsMax) * 0.5f;
        var worldRot = lr * hr;
        var worldMid = lp + Vector3.Transform(here, lr) + Vector3.Transform(mid, worldRot);
        StartCarry(world, c, l, ld, lp, lr, d, l.Health, l.Paint, worldMid, worldRot, to, toAt);
        return true;
    }

    /// <summary>Text for a point kind on the look line (both peers).</summary>
    private static string PointText(ConstructWorld world, CLift l)
    {
        var name = world.Get(l.StationBody) is { } b && l.ToPart < b.PartCount ? b.DefOf(l.ToPart).Name : "a machine";
        return l.ToKind switch
        {
            LiftPointKind.Rack => $"the {name}",
            LiftPointKind.PressBay => $"the {name}'s bay {l.ToBay + 1}",
            _ => $"the {name}'s Piece in",
        };
    }
}

public sealed partial class ConstructWorld
{
    public readonly List<LiftOut> LiftOuts = new(8); // server: pieces machines hold at their Piece out for lifters (ConstructLifterCarry.cs)
}
