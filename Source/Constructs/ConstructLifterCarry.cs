using System.Numerics;

namespace CallOfSlop;

/// <summary>What a lifter's point is (arms-and-cranes.md 3.5): where it picks, where it puts, and through what.</summary>
public enum LiftPointKind : byte
{
    None, // (a station step or a put-back: not a carry between points)
    Rack, // a Piece Rack or a Lay-down Pad: pick (a want's piece) and put (a want, or storage)
    Out, // a machine's Piece out with no conveyor on it: pick (the loose piece it dropped there, as it always has)
    Assembler, // an Assembler's Piece in with no conveyor: put (a part its recipe uses)
    PressBay, // a press bay's Piece in with no conveyor: put (a blank it takes, or a kit part for its hopper)
    Former, // a Panel Former's Piece in with no conveyor: put (sheet for its bin)
    Tray, // (phase 5b) a Crafting Terminal's tray: put (a part one of its open orders still waits for)
    RunEnd, // a Roller Tile run's last tile with no way on (RollerRuns.EndRunAt): pick (the lead piece waiting at its end)
    RunStart, // a Roller Tile run's own first tile, nothing butted on its in end (RollerRuns.StartRunAt): put (a piece handed on toward a station: ConstructLifterHandOff.cs)
}

/// <summary>A point in a lifter's reach (server; found every <see cref="ConstructLifters.PointEvery"/> ticks, never per tick).</summary>
public struct LiftPoint
{
    public ushort Body, Part;
    public LiftPointKind Kind;
    public byte Bay; // a press bay's index
}


/// <summary>
/// Lifters phase 4 (docs/sandbox/design/arms-and-cranes.md section 5, the user's answer 5): carrying between points by the Routers'
/// rule. An idle arm with no station step looks at its points (racks, Lay-down Pads, machines' Piece in and Piece out with no
/// conveyor on them, on any construct in its reach) about once a second: a loose piece a machine dropped at its Piece out goes to a put point
/// that wants it, else to storage (the nearest rack or pad with room that takes it); a piece in a rack or pad goes only to a want
/// (never from storage to storage). A put refused at the end looks for another taker, else the piece waits in the hand (Stuck,
/// Alarm) and is looked at again every 2 s. Server only; the wire is the lift word. Nothing allocates after a lifter's first look.
/// </summary>
public static partial class ConstructLifters
{
    public const int MaxPoints = 32; // a lifter's points (16 until 2026-11-22: a bay crane over four presses, an Assembler and its stores ran out)
    public const int PointEvery = 120; // ticks between looks for a lifter's points (2 s)
    public const int CarryLook = 6; // 10 Hz steps between an idle lifter's looks for a carry (about 1 s)
    public const int RefuseHold = 30 * 60; // lift ticks a put point that refused this lifter's piece is passed over (30 s)

    /// <summary>The idle look for a carry (about once a second): true when one began.</summary>
    private static bool LookCarry(ConstructWorld world, Construct c, CLift l, LifterDef ld)
    {
        l.Look = 0;
        return Carry(world, c, l, ld);
    }

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

    /// <summary>Where the next piece of a kind would lie on a rack or pad (its middle, world), as the rack would stack it now.</summary>
    private static bool SlotFor(Construct b, int part, CStore st, ConstructPartDef d, out Vector3 at)
    {
        at = default;
        part = ConstructPads.Owner(b, part);
        if (!ConstructRackPack.NextOrigin(ConstructPads.RackDef(b, part), st, d, out var o))
            return false;
        at = PartPoint(b, part, o + (d.BoundsMin + d.BoundsMax) * 0.5f);
        return true;
    }

    /// <summary>
    /// Where a put onto a rack or pad lowers, in <see cref="RackSpot"/>'s terms (the hold's floor under the piece's middle, less the
    /// 5 cm the callers add back): the slot the rack will stack the piece in when the hand reaches it there, else the nearest point.
    /// </summary>
    internal static bool PutSpot(Construct b, int part, CStore st, ConstructPartDef d, LifterDef ld, Vector3 shoulder, Quaternion lr, float lift, out Vector3 at)
    {
        if (SlotFor(b, part, st, d, out at) && InReach(ld, shoulder, lr, at, lift, out _))
        {
            at.Y -= (d.BoundsMax.Y - d.BoundsMin.Y) * 0.5f + 0.05f;
            return true;
        }
        return RackSpot(b, part, ld, shoulder, lr, lift, lift, out at);
    }

    /// <summary>The top piece of a kind's last stack on a rack or pad (its middle, world), when the hand reaches it: where a pick lifts from.</summary>
    internal static bool TopSlot(Construct b, int part, CStore st, ConstructPartDef d, LifterDef ld, Vector3 shoulder, Quaternion lr, out Vector3 at)
    {
        at = default;
        part = ConstructPads.Owner(b, part);
        if (!ConstructRackPack.LastOf(st, (ushort)d.Id, out var slab, out var lane))
            return false;
        ConstructWorld.RackPieceAt(b, part, st, slab, lane, out at, out _);
        return InReach(ld, shoulder, lr, at, SafeLift, out _) && InReach(ld, shoulder, lr, at, WristLift(d), out _);
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
                d = ConstructPads.RackDef(b, pt.Part); // (a pad of tiles: its whole rectangle's middle)
                at = PartPoint(b, pt.Part, new Vector3((d.RackHoldMin.X + d.RackHoldMax.X) * 0.5f, d.RackHoldMin.Y, (d.RackHoldMin.Z + d.RackHoldMax.Z) * 0.5f));
                return true;
            case LiftPointKind.Out:
                if (d.IsBakeOven)
                {
                    // (an oven's out is its bed's middle)
                    if (!OutZone.BedOf(b, pt.Part, out var toCore, out var bmin, out var bmax) || !Matrix4x4.Invert(toCore, out var core))
                        return false;
                    at = Vector3.Transform(new Vector3((bmin.X + bmax.X) * 0.5f, bmin.Y, (bmin.Z + bmax.Z) * 0.5f), core);
                    return true;
                }
                at = PartPoint(b, pt.Part, d.IsExtruder ? d.ExtDieAt : d.AsmOutAt + d.AsmOutDir * 0.6f);
                return true;
            case LiftPointKind.RunEnd:
                var run = RollerRuns.EndRunAt(b, pt.Part);
                if (run < 0 && (run = RollerRuns.BedRunAt(b, pt.Part)) < 0)
                    return false; // (a run's end with no way on, or a run into a bed, where pieces made for others wait: ConstructConveyor.PastBed)
                b.WorldFrame(b.Parts[pt.Part].Seg, out var rq, out var rt);
                at = rt + Vector3.Transform(b.RouteNodes[b.Routes[run].Start + 1].Pos, rq); // (the run's last node, over its end tile)
                return true;
            case LiftPointKind.RunStart:
                if (RollerRuns.StartRunAt(b, pt.Part) is not (var srun and >= 0))
                    return false;
                b.WorldFrame(b.Parts[pt.Part].Seg, out var sq, out var st);
                at = st + Vector3.Transform(b.RouteNodes[b.Routes[srun].Start].Pos, sq); // (the run's first node, over its first tile)
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
        var d = ConstructPads.RackDef(b, part);
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
            PointsOn(b, b == c ? l.Part : -1, ld, shoulder, lr, l.Points, ref l.PointN);
        }
    }

    /// <summary>
    /// The points on body <paramref name="b"/> a lifter reaches with an empty hand (<paramref name="self"/>: its own part on that body,
    /// else -1), added to <paramref name="pts"/> while there is room. Either peer: the logistics map's lifter edges use it too (phase 5b).
    /// </summary>
    private static void PointsOn(Construct b, int self, LifterDef ld, Vector3 shoulder, Quaternion lr, LiftPoint[] pts, ref int n)
    {
        var far = ld.ReachMax + MathF.Max(ld.Up, ld.Down);
        var local = Vector3.Transform(shoulder - b.Pos, Quaternion.Conjugate(b.Rot)); // (the shoulder in the body's frame: a cheap cut for parts on it)
        for (var i = 0; i < b.PartCount && n < pts.Length; i++)
        {
            if (i == self || b.Parts[i].Broken)
                continue;
            var d = b.DefOf(i);
            if (!d.IsPieceRack && !d.IsAssembler && !d.IsPress && !d.IsExtruder && !d.IsPanelFormer && !d.IsRollerTile && !d.IsBakeOven)
                continue;
            if (d.IsPieceRack && ConstructPads.IsMember(b, i))
                continue; // (a tile of a bigger pad: its owner tile is the pad's one point)
            ref readonly var p = ref b.Parts[i];
            var half = (d.BoundsMax - d.BoundsMin).Length() * 0.5f + 0.5f;
            Vector3 bedMin = default, bedMax = default;
            var bed = (d.IsAssembler || d.IsBakeOven) && OutZone.BedOf(b, i, out _, out bedMin, out bedMax);
            if (bed)
                half = MathF.Max(half, Vector3.Max(Vector3.Abs(bedMin), Vector3.Abs(bedMax)).Length() + 0.5f); // (a block Assembler: its whole bed, measured from the Core)
            if (d.IsPieceRack && ConstructPads.RackDef(b, i) is var pd && !ReferenceEquals(pd, d))
                half = MathF.Max(MathF.Abs(pd.RackHoldMin.X), MathF.Abs(pd.RackHoldMax.X)) + MathF.Max(MathF.Abs(pd.RackHoldMin.Z), MathF.Abs(pd.RackHoldMax.Z)) + pd.RackHoldMax.Y + 0.5f; // (a pad's owner: its whole rectangle, measured from the tile)
            if (p.Seg == 0 && Vector3.DistanceSquared(p.Pos + Rot24.Apply(p.Rot, (d.BoundsMin + d.BoundsMax) * 0.5f), local) > (far + half) * (far + half))
                continue; // (a big body's far parts; one on a moving segment is tested whole)
            if (d.IsRollerTile)
            {
                // A run's end tile with no way on: its pieces wait there for a crane (the Hall Crane picks from a run's end).
                var re = new LiftPoint { Body = b.Id, Part = (ushort)i, Kind = LiftPointKind.RunEnd };
                if (PointAt(b, re, out var ra) && InReach(ld, shoulder, lr, ra, SafeLift, out _))
                    pts[n++] = re;
                // A run's own first tile with nothing butted on its in end: a lifter may put a piece on it (a hand-off toward a station).
                var rs = new LiftPoint { Body = b.Id, Part = (ushort)i, Kind = LiftPointKind.RunStart };
                if (n < pts.Length && PointAt(b, rs, out var sa) && InReach(ld, shoulder, lr, sa, SafeLift, out _))
                    pts[n++] = rs;
                continue;
            }
            if (d.IsPieceRack)
            {
                // A rack or a pad: pick and put. A Crafting Terminal's tray (phase 5b): put only, for its orders. A station's hold: its claims only.
                if (!d.IsBuildStation && RackSpot(b, i, ld, shoulder, lr, SafeLift, SafeLift, out _))
                    pts[n++] = new LiftPoint { Body = b.Id, Part = (ushort)i, Kind = d.IsCraftTerminal ? LiftPointKind.Tray : LiftPointKind.Rack };
                continue;
            }
            var maker = d.IsAssembler || d.IsPress || d.IsExtruder && !d.IsPanelFormer || d.IsBakeOven; // (an oven's bed: the part a kit baked into, ConstructBakeOvenOrders.cs)
            // A machine's Piece out with no conveyor on it; a block Assembler's is its bed too, where it leaves what it builds (LeaveOnBed).
            if (maker && ConstructConveyor.Out(b, i) < 0 && PointAt(b, new LiftPoint { Body = b.Id, Part = (ushort)i, Kind = LiftPointKind.Out }, out var o)
                && (InReach(ld, shoulder, lr, o, SafeLift, out _) || bed && BedSpot(b, i, ld, shoulder, lr)))
                pts[n++] = new LiftPoint { Body = b.Id, Part = (ushort)i, Kind = LiftPointKind.Out };
            if (d.IsAssembler || d.IsPanelFormer && !(d.BlockRole == BlockRole.Core && RollerRuns.PortByte(d, ConstructParts.BedInPort) is var bi and >= 0 && ConveyorInto(b, i, (byte)bi))) // (a bed a run feeds isn't a crane's too)
                TryIn(pts, ref n, b, i, d, d.IsAssembler ? LiftPointKind.Assembler : LiftPointKind.Former, 0, "Piece in", ld, shoulder, lr);
            else if (d.IsPress)
                for (var bay = 0; bay < d.PressBays.Length && bay < 8; bay++)
                    TryIn(pts, ref n, b, i, d, LiftPointKind.PressBay, bay, bay == 0 ? "Piece in" : BayPortNames[bay], ld, shoulder, lr);
        }
    }

    private static readonly string[] BayPortNames = ["Piece in", "Piece in 2", "Piece in 3", "Piece in 4", "Piece in 5", "Piece in 6", "Piece in 7", "Piece in 8"];

    private static void TryIn(LiftPoint[] pts, ref int n, Construct b, int i, ConstructPartDef d, LiftPointKind kind, int bay, string port, LifterDef ld, Vector3 shoulder, Quaternion lr)
    {
        if (n >= pts.Length || ConveyorPort(d, port) is not { } np || ConveyorInto(b, i, np.Byte))
            return;
        if (InReach(ld, shoulder, lr, PartPoint(b, i, np.At), SafeLift, out _))
            pts[n++] = new LiftPoint { Body = b.Id, Part = (ushort)i, Kind = kind, Bay = (byte)bay };
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
                return ConstructPanelFormer.Wants(world, b, pt.Part, d); // (a blank for one of its orders, onto an empty bed)
            case LiftPointKind.Tray:
                return TrayWant(world, b, pt.Part, d) != 0; // (phase 5b: an open order of its kind still waits for it)
            case LiftPointKind.Rack:
                return PadWant(world, b, pt.Part, d) != 0; // (phase 5b: a station's large part, to a pick pad its arms fit from)
            default:
                return false;
        }
    }

    /// <summary>Is another hand already bound for this put point (a press bay or a machine's input: one at a time)?</summary>
    private static bool Bound(ConstructWorld world, CLift self, in LiftPoint pt)
    {
        if (pt.Kind is LiftPointKind.Rack or LiftPointKind.Tray)
            return false; // (their wants count the hands bound for them)
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
    private static int PutFor(ConstructWorld world, CLift l, LifterDef ld, Vector3 shoulder, Quaternion lr, ConstructPartDef d, bool storage, int skip, out Vector3 at, bool asNew = true)
    {
        at = default;
        var lift = WristLift(d);
        var fromPad = skip >= 0 && l.Points![skip].Kind == LiftPointKind.Rack && world.Get(l.Points[skip].Body) is { } sb && l.Points[skip].Part < sb.PartCount && sb.DefOf(l.Points[skip].Part).IsLayDownPad;
        for (var k = 0; k < l.PointN; k++)
        {
            ref readonly var pt = ref l.Points![k];
            if (k == skip || pt.Kind == LiftPointKind.Out || !asNew && pt.Kind is LiftPointKind.Rack or LiftPointKind.Tray || world.Get(pt.Body) is not { } b // (a damaged or painted piece is never made a count)
                || RefusedNow(world, l, pt))
                continue;
            if (pt.Kind == LiftPointKind.Rack && (fromPad || pt.Part >= b.PartCount || !b.DefOf(pt.Part).IsLayDownPad))
                continue; // (a rack is a want only as a station's pick pad, and never from another pad)
            if (!Wants(world, b, pt, d) || Bound(world, l, pt))
                continue;
            Vector3 p;
            if (pt.Kind is LiftPointKind.Rack or LiftPointKind.Tray)
            {
                // (a want with no room is no taker: a full pick pad said it wanted the piece and refused it at the end, 2026-11-23)
                if (world.StoreOf(b, pt.Part) is not { } wst || ConstructConveyor.RackCheck(b, pt.Part, wst, d) != 0
                    || !PutSpot(b, pt.Part, wst, d, ld, shoulder, lr, lift, out p))
                    continue;
            }
            else if (!PointAt(b, pt, out p))
                continue;
            p.Y += (d.BoundsMax.Y - d.BoundsMin.Y) * 0.5f + 0.05f;
            if (!InReach(ld, shoulder, lr, p, lift, out _))
                continue;
            at = p;
            return k;
        }
        // A piece a building station still lacks, one hop nearer to it: its pick pad, or a run that leads there (ConstructLifterHandOff.cs).
        if (asNew && HandOffFor(world, l, ld, shoulder, lr, d, skip, out at) is var hand and >= 0)
            return hand;
        if (!storage)
            return -1;
        var best = -1;
        var bestDist = float.MaxValue;
        for (var k = 0; k < l.PointN; k++)
        {
            ref readonly var pt = ref l.Points![k];
            if (k == skip || !asNew || pt.Kind != LiftPointKind.Rack || RefusedNow(world, l, pt) || world.Get(pt.Body) is not { } b || pt.Part >= b.PartCount || world.StoreOf(b, pt.Part) is not { } st)
                continue;
            if (ConstructConveyor.RackCheck(b, pt.Part, st, d) != 0 || !StockTargets.Accepts(world, b, pt.Part, st, d))
                continue;
            if (!PutSpot(b, pt.Part, st, d, ld, shoulder, lr, lift, out var p))
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
            return false; // (the caller sets the idle reason)
        }
        Frame(c, l.Part, ld, out var lp, out var lr, out var shoulder);
        // A run's end takes its turn with the outs: tried first when it comes before every out from where the turn stands (else an
        // Extruder's every angle would keep a run's pieces waiting for good).
        var runFirst = false;
        for (var j = 0; j < l.PointN; j++)
            if (l.Points![(j + l.OutTurn) % l.PointN].Kind is LiftPointKind.Out or LiftPointKind.RunEnd)
            {
                runFirst = l.Points[(j + l.OutTurn) % l.PointN].Kind == LiftPointKind.RunEnd;
                break;
            }
        if (runFirst && CarryRunEnd(world, c, l, ld, lp, lr, shoulder))
            return true;
        for (var j = 0; j < l.PointN; j++)
        {
            var k = (j + l.OutTurn) % l.PointN; // (the outs take turns: lifters phase 7, a press's brackets waited behind an Extruder's every angle)
            var pt = l.Points![k];
            if (pt.Kind != LiftPointKind.Out || world.Get(pt.Body) is not { } b || !PointAt(b, pt, out var outAt))
                continue;
            // The piece the machine dropped at its out as before (a loose one-part body at rest there: seen, saved and
            // takeable by hand as any loose piece), or left on its bed; the arm takes it only when it can carry it and something in reach takes it.
            var zone = OutZone.Of(b, pt.Part, outAt);
            var bodies = world.Bodies;
            for (var bi = 0; bi < bodies.Count; bi++)
            {
                var lo = bodies[bi];
                if (lo.PartCount != 1 || lo == b || lo == c || lo.HeldBy != 0 || lo.GrabbedBy != 255 || lo.CarriedBy != 0 || lo.Anchored || lo.Parts[0].Broken)
                    continue;
                var d = lo.DefOf(0);
                if (!zone.Holds(lo, d) || lo.Vel.LengthSquared() > 0.25f || !Carries(ld, d) || !MadeBy(b, pt.Part, d) || PickWhy(world, lo) != null)
                    continue;
                Freeform.PartWorld(lo, 0, out var pp, out var pr);
                var from = pp + Vector3.Transform((d.BoundsMin + d.BoundsMax) * 0.5f, pr);
                if (!InReach(ld, shoulder, lr, from, WristLift(d), out _))
                    continue;
                var to = PutFor(world, l, ld, shoulder, lr, d, storage: true, skip: k, out var toAt, lo.Parts[0].Health >= d.Health * 0.999f && lo.Parts[0].Paint == 0);
                if (to < 0)
                    continue;
                var health = (byte)Math.Clamp(255 * lo.Parts[0].Health / MathF.Max(1, d.Health), 1, 255);
                var paint = lo.Parts[0].Paint;
                l.OutTurn = k + 1;
                world.Remove(lo);
                StartCarry(world, c, l, ld, lp, lr, d, health, paint, from, pr, to, toAt);
                return true;
            }
        }
        for (var j = 0; j < l.PointN; j++)
        {
            var k = (j + l.OutTurn) % l.PointN; // (the outs take turns: lifters phase 7, a press's brackets waited behind an Extruder's every angle)
            // A machine's own output tray (2026-11-14, verification 4: since sized parts phase 6 a press's preset part, a Button, goes
            // to its tray, not to the floor at its out, and an arm never lifted it, so a terminal's order served by an arm never filled):
            // what the tray holds goes on from the out point by the same rule, a want first, else storage.
            var pt = l.Points![k];
            if (pt.Kind != LiftPointKind.Out || world.Get(pt.Body) is not { } b || pt.Part >= b.PartCount || world.StoreOf(b, pt.Part) is not { } st
                || !b.DefOf(pt.Part).IsPress && !b.DefOf(pt.Part).IsAssembler || !PointAt(b, pt, out var outAt))
                continue;
            for (var q = 0; q < st.SlotCount; q++)
            {
                if (st.Count[q] <= 0 || st.Def[q] == ConstructParts.None || ConstructParts.Get(st.Def[q]) is not { } d || !Carries(ld, d)
                    || ConstructCrafting.Available(world, st, (ushort)d.Id) <= 0 || !InReach(ld, shoulder, lr, outAt, WristLift(d), out _))
                    continue;
                var to = PutFor(world, l, ld, shoulder, lr, d, storage: true, skip: k, out var toAt);
                if (to < 0 || !ConstructCrafting.TakeOne(world, b, pt.Part, st, d))
                    continue;
                world.CraftChanged(b);
                Wake(b, pt.Part);
                l.OutTurn = k + 1;
                Freeform.PartWorld(b, pt.Part, out _, out var mr);
                StartCarry(world, c, l, ld, lp, lr, d, 255, 0, outAt, mr, to, toAt);
                return true;
            }
        }
        if (!runFirst && CarryRunEnd(world, c, l, ld, lp, lr, shoulder))
            return true;
        for (var k = 0; k < l.PointN; k++)
        {
            var pt = l.Points![k];
            if (pt.Kind != LiftPointKind.Rack || world.Get(pt.Body) is not { } b || pt.Part >= b.PartCount || world.StoreOf(b, pt.Part) is not { ItemCount: > 0 } st || st.KindDef == null)
                continue;
            for (var q = 0; q < st.KindN; q++)
            {
                var def = st.KindDef[q];
                if (ConstructCrafting.Available(world, st, def) <= 0 || ConstructParts.Get(def) is not { } d || !Carries(ld, d)) // (phase 5b: never what an order or a build has claimed)
                    continue;
                var to = PutFor(world, l, ld, shoulder, lr, d, storage: false, skip: k, out var toAt);
                if (to < 0)
                    continue;
                var slot = TopSlot(b, pt.Part, st, d, ld, shoulder, lr, out var from);
                if (!slot && !RackSpot(b, pt.Part, ld, shoulder, lr, SafeLift, WristLift(d), out from))
                    continue;
                if (!world.StationTake(b, pt.Part, st, d))
                    continue;
                world.CraftChanged(b);
                if (!slot)
                    from.Y += (d.BoundsMax.Y - d.BoundsMin.Y) * 0.5f + 0.05f;
                Freeform.PartWorld(b, pt.Part, out _, out var rr);
                StartCarry(world, c, l, ld, lp, lr, d, 255, 0, from, rr, to, toAt);
                return true;
            }
        }
        // (the caller sets the idle reason)
        return false;
    }

    /// <summary>
    /// A piece waiting at the end of a Roller Tile run (a run end point): off the run into the hand, to the store its rider was going to
    /// when that is in reach and takes it, else by the Routers' rule (a want, else storage). True when a carry began.
    /// </summary>
    private static bool CarryRunEnd(ConstructWorld world, Construct c, CLift l, LifterDef ld, Vector3 lp, Quaternion lr, Vector3 shoulder)
    {
        for (var j = 0; j < l.PointN; j++)
        {
            var k = (j + l.OutTurn) % l.PointN; // (run ends take turns with the outs)
            var pt = l.Points![k];
            if (pt.Kind != LiftPointKind.RunEnd || world.Get(pt.Body) is not { } b)
                continue;
            var run = RollerRuns.EndRunAt(b, pt.Part);
            var lead = run >= 0 ? RollerRuns.WaitingLead(b, run) : (run = RollerRuns.BedRunAt(b, pt.Part)) >= 0 ? RollerRuns.PassingLead(b, run) : -1;
            if (lead < 0)
                continue;
            var rider = b.Conveyed[lead];
            var d = ConstructParts.Def(rider.Def);
            if (d == null || !Carries(ld, d))
                continue;
            ConstructConveyor.WorldPose(b, run, rider, d, out var from, out var fromRot);
            if (!InReach(ld, shoulder, lr, from, WristLift(d), out _))
                continue;
            var asNew = rider.Health == 255 && rider.Paint == 0;
            var to = DestPoint(world, l, ld, shoulder, lr, d, rider.Dest, out var toAt);
            if (to < 0 && asNew && IsLeadPadHandle(world, b.Id, rider.Dest))
                to = HandOffFor(world, l, ld, shoulder, lr, d, k, out toAt, rider.Dest); // (handed on toward its station's pick pad, out of this lifter's reach)
            if (to < 0)
                to = PutFor(world, l, ld, shoulder, lr, d, storage: true, skip: k, out toAt, asNew);
            if (to < 0 || ConstructConveyor.TakeRider(world, b, lead, out from, out fromRot, out var health, out var paint) == null)
                continue;
            l.OutTurn = k + 1;
            StartCarry(world, c, l, ld, lp, lr, d, health, paint, from, fromRot, to, toAt);
            return true;
        }
        return false;
    }

    /// <summary>The put point whose store is <paramref name="dest"/> (a rider's destination: a rack, pad, tray or machine's input), when it takes the piece now and is in reach; else -1.</summary>
    private static int DestPoint(ConstructWorld world, CLift l, LifterDef ld, Vector3 shoulder, Quaternion lr, ConstructPartDef d, ushort dest, out Vector3 at)
    {
        at = default;
        if (dest == 0)
            return -1;
        var lift = WristLift(d);
        for (var k = 0; k < l.PointN; k++)
        {
            ref readonly var pt = ref l.Points![k];
            if (pt.Kind is LiftPointKind.Out or LiftPointKind.RunEnd or LiftPointKind.None || RefusedNow(world, l, pt) || world.Get(pt.Body) is not { } b || pt.Part >= b.PartCount
                || b.Parts[pt.Part].Store != dest || world.StoreOf(b, pt.Part) is not { } st)
                continue;
            Vector3 p;
            if (pt.Kind is LiftPointKind.Rack or LiftPointKind.Tray)
            {
                if (ConstructConveyor.RackCheck(b, pt.Part, st, d, ordered: true) != 0)
                    continue;
                if (pt.Kind == LiftPointKind.Rack && SlotFor(b, pt.Part, st, d, out p) && InReach(ld, shoulder, lr, p, lift, out _))
                {
                    at = p; // (Sean 2026-11-23: down where the piece will lie on the pad, not at the pad's corner nearest the crane)
                    return k;
                }
                if (!RackSpot(b, pt.Part, ld, shoulder, lr, lift, lift, out p))
                    continue;
            }
            else if (!Wants(world, b, pt, d) || Bound(world, l, pt) || !PointAt(b, pt, out p))
                continue; // (a machine's input it was riding to: a chained order's piece, by its own rule)
            p.Y += (d.BoundsMax.Y - d.BoundsMin.Y) * 0.5f + 0.05f;
            if (!InReach(ld, shoulder, lr, p, lift, out _))
                continue;
            at = p;
            return k;
        }
        return -1;
    }

    /// <summary>
    /// Could this machine have made this piece (an Assembler's recipe output, a press's row output, an Extruder's piece)? A loose part
    /// someone left by a machine's out is not taken. An Assembler: any part its machine's recipes make, not only its recipe now (Zero Day
    /// works, 2026-11-22: once its order queue moved on, "waiting for orders", the keel it had left on its bed was never lifted).
    /// </summary>
    private static bool MadeBy(Construct b, int part, ConstructPartDef d)
    {
        var md = b.DefOf(part);
        if (md.IsBakeOven)
            return BakeKits.Has(d); // (a part its kit baked into)
        if (md.IsAssembler)
            return AssemblingTable.Current.Of((ushort)d.Id) is { } r && r.Output == d && r.Machine == md.AsmMachine;
        if (md.IsPress && ConstructPress.SizedDieOf(PressTable.Current, b, part) is { } die)
        {
            // A press with a sized die (phase 5: phase 4 read only the lathe's rows, so a press's output was never picked): one of the
            // die's preset parts, or a part pressed from its key ("Pressed Part (die.pattern ...)").
            foreach (var p in die.Presets)
                if (p.Part == d)
                    return true;
            var name = d.Name.AsSpan();
            return name.StartsWith(Pressed.Prefix) && name[Pressed.Prefix.Length..].StartsWith(die.Id) && name.Length > Pressed.Prefix.Length + die.Id.Length
                && name[Pressed.Prefix.Length + die.Id.Length] == '.';
        }
        if (md.IsPress)
            return ConstructPress.RowFor(ConstructPress.RowsOf(PressTable.Current, b, part), b, part) is { } row && row.Output == d;
        return md.IsExtruder && d.IsExtrusion;
    }

    /// <summary>Does a lifter reach any spot over a block Assembler's bed (on a grid a cell apart, at most 16 by 16)? Only from FindPoints (every 2 s).</summary>
    private static bool BedSpot(Construct b, int part, LifterDef ld, Vector3 shoulder, Quaternion lr)
    {
        if (!OutZone.BedOf(b, part, out var toCore, out var min, out var max) || !Matrix4x4.Invert(toCore, out var core))
            return false;
        var cell = b.Parts[part].D.BlockCell;
        var nx = Math.Clamp((int)MathF.Ceiling((max.X - min.X) / MathF.Max(0.5f, cell)), 1, 16);
        var nz = Math.Clamp((int)MathF.Ceiling((max.Z - min.Z) / MathF.Max(0.5f, cell)), 1, 16);
        for (var xi = 0; xi <= nx; xi++)
            for (var zi = 0; zi <= nz; zi++)
            {
                var p = Vector3.Transform(new Vector3(min.X + (max.X - min.X) * xi / nx, min.Y + OutZone.BedSlack, min.Z + (max.Z - min.Z) * zi / nz), core);
                if (InReach(ld, shoulder, lr, p, SafeLift, out _))
                    return true;
            }
        return false;
    }

    /// <summary>
    /// Where a machine's out holds its made pieces: near its out point (1 m and half the piece's size), and for a block Assembler with a
    /// bed anywhere over the bed, up to its clear height (ConstructAssembling.LeaveOnBed sets what it builds there). Worked out once a point.
    /// </summary>
    internal readonly struct OutZone
    {
        public const float BedSlack = 0.1f; // m: under the bed's top a piece's middle may sit (it never does) and how far a piece may lie past its edge

        private readonly Vector3 _at, _min, _max;
        private readonly Matrix4x4 _toCore;
        private readonly bool _bed;

        private OutZone(Vector3 at, bool bed, Matrix4x4 toCore, Vector3 min, Vector3 max) => (_at, _bed, _toCore, _min, _max) = (at, bed, toCore, min, max);

        public static OutZone Of(Construct b, int part, Vector3 outAt)
        {
            if (part < b.PartCount && (b.DefOf(part).IsAssembler || b.DefOf(part).IsBakeOven) && BedOf(b, part, out var toCore, out var min, out var max))
                return new OutZone(outAt, true, toCore, min, max);
            return new OutZone(outAt, false, default, default, default);
        }

        public bool Holds(Construct lo, ConstructPartDef d)
        {
            var reach = 1f + 0.5f * MathF.Max(d.BoundsMax.X - d.BoundsMin.X, MathF.Max(d.BoundsMax.Y - d.BoundsMin.Y, d.BoundsMax.Z - d.BoundsMin.Z));
            if (Vector3.DistanceSquared(lo.WorldCom, _at) <= reach * reach)
                return true;
            if (!_bed)
                return false;
            var p = Vector3.Transform(lo.WorldCom, _toCore);
            return p.X >= _min.X - BedSlack && p.X <= _max.X + BedSlack && p.Z >= _min.Z - BedSlack && p.Z <= _max.Z + BedSlack && p.Y >= _min.Y - BedSlack && p.Y <= _max.Y;
        }

        /// <summary>
        /// A block Assembler's bed as a box in its Core part's frame, from the bed's top to its clear height (the hazard box's, AddHazards),
        /// and the world-to-Core matrix. False for a bare Core or any other machine.
        /// </summary>
        public static bool BedOf(Construct b, int part, out Matrix4x4 toCore, out Vector3 min, out Vector3 max)
        {
            toCore = default;
            (min, max) = (default, default);
            if (part < b.PartCount && b.Parts[part].D.IsBakeOven)
            {
                // (an oven's bed: its clear height over the whole bed, ConstructBakeOven.Bed)
                if (!MachineBlocks.TryOf(b, part, out var oasm) || oasm.W == 0 || !Matrix4x4.Invert(ConstructBakeOven.CoreFrame(b, part), out toCore))
                    return false;
                var obed = new ConstructBakeOven.Bed(b, oasm);
                var oo = b.Parts[part].D.BlockOrigin ?? Vector3.Zero;
                min = new Vector3(oo.X + (obed.X0 - 0.5f) * obed.Cell, obed.Top, oo.Z + (obed.Z0 - 0.5f) * obed.Cell);
                max = min + new Vector3(obed.W * obed.Cell, MathF.Max(obed.Clear, 0.5f), obed.L * obed.Cell);
                return true;
            }
            if (ConstructAssembling.FitOf(b, part) is not { Limited: true, W: > 0 } fit || !MachineBlocks.TryOf(b, part, out var asm)
                || !Matrix4x4.Invert(ConstructBakeOven.CoreFrame(b, part), out toCore))
                return false;
            var o = b.Parts[part].D.BlockOrigin ?? Vector3.Zero;
            var bedTop = o.Y * 2f; // (the Core's cell middle is the bed layer's: its top is as far over it as the foot is under)
            min = new Vector3(o.X + (asm.X0 - 0.5f) * fit.Cell, bedTop, o.Z + (asm.Z0 - 0.5f) * fit.Cell);
            max = min + new Vector3(asm.W * fit.Cell, MathF.Max(fit.Clear, 0.5f), asm.L * fit.Cell);
            return true;
        }
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
        l.OrderId = 0; // (phase 7: OrderCarry sets it after, for a hand order's carry)
        l.Def = (ushort)d.Id;
        l.Paint = paint;
        l.Health = health;
        l.FromRot = Quaternion.Normalize(inv * fromRot);
        l.ToRot = Quaternion.Normalize(inv * toRot);
        l.From = Vector3.Transform(from - Vector3.Transform(mid, fromRot) - lp, inv);
        l.To = Vector3.Transform(toAt - Vector3.Transform(mid, toRot) - lp, inv);
        l.Rise = ClearRise(world, c, l, ld, d);
        l.Dur = CarryTicks(ld, l, d);
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
        var ok = d != null && world.Get(l.StationBody) is { } b && l.ToPart < b.PartCount && PutPiece(world, query, b, l.ToKind, l.ToPart, l.ToBay, d, l.Health, l.Paint);
        if (ok)
        {
            Console.WriteLine($"[lifter] body {l.Body} part {l.Part}: put a {d!.Name} into {l.ToKind} part {l.ToPart} of body {l.StationBody}");
            ForgetRefused(l); // (a put went in: what refused before may take the next piece)
            l.Moves++;
            l.Fitted = true;
            OrderPutDone(world, l); // (phase 7: a hand order's piece counts for it)
            l.Why = LiftWhy.Carrying; // (kept on the way back: the look line says it goes back for the next piece)
            Back(c, l, ld, null, -1); // (the empty hand goes back to where it picked)
            l.ToKind = LiftPointKind.None;
            return;
        }
        // Remembered for a while (Sean, 2026-11-22: a Hall Crane carried a piece between two put points that each said they wanted it
        // and then refused it at the end, for good): the next taker is another point, else the piece waits in the hand.
        if (l.ToKind != LiftPointKind.None)
        {
            (l.Refused2, l.Refused2At) = (l.Refused, l.RefusedAt);
            (l.Refused, l.RefusedAt) = (new LiftPoint { Body = l.StationBody, Part = l.ToPart, Kind = l.ToKind, Bay = l.ToBay }, world.LiftTick);
        }
        if (l.OrderId != 0 && l.Orders != null)
            for (var oi = 0; oi < l.OrderN; oi++)
                if (l.Orders[oi].Id == l.OrderId)
                    l.Orders[oi].Why = LiftOrderWhy.Refused; // (its put point refused it at the end: the piece goes on by the Routers' rule, the order waits)
        l.OrderId = 0;
        if (d != null && Redirect(world, c, l, ld, d))
            return;
        (l.State, l.ToHold, l.Why, l.Age, l.From, l.FromRot, l.ToKind) = (LiftState.Stuck, false, LiftWhy.NoTaker, l.Dur, l.To, l.ToRot, LiftPointKind.None);
        Console.WriteLine($"[lifter] body {l.Body} part {l.Part}: its {d?.Name ?? "piece"} was refused; nothing in reach takes it (stuck)");
    }

    /// <summary>The hand is empty again (a put, a stow into a hold, a drop): the points that refused its last piece are takers again.</summary>
    private static void ForgetRefused(CLift l) => l.RefusedAt = l.Refused2At = int.MinValue / 2;

    /// <summary>Did this point refuse this lifter's piece at the end of a carry in the last <see cref="RefuseHold"/> ticks? (Not a want or a store for now.)</summary>
    private static bool RefusedNow(ConstructWorld world, CLift l, in LiftPoint pt) =>
        world.LiftTick - l.RefusedAt < RefuseHold && Same(l.Refused, pt) || world.LiftTick - l.Refused2At < RefuseHold && Same(l.Refused2, pt);

    /// <summary>Server: a piece in the hand to another taker in reach (a want, else storage). True when it set off.</summary>
    private static bool Redirect(ConstructWorld world, Construct c, CLift l, LifterDef ld, ConstructPartDef d)
    {
        if (l.Points == null || world.LiftTick - l.PointsAt >= PointEvery)
            FindPoints(world, c, l, ld);
        Frame(c, l.Part, ld, out var lp, out var lr, out var shoulder);
        var to = PutFor(world, l, ld, shoulder, lr, d, storage: true, skip: -1, out var toAt, AsNewInHand(l));
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
            LiftPointKind.Rack or LiftPointKind.Tray => $"the {name}",
            LiftPointKind.PressBay => $"the {name}'s bay {l.ToBay + 1}",
            LiftPointKind.RunStart => $"the start of the {name} run",
            _ => $"the {name}'s Piece in",
        };
    }
}


public static partial class ConstructLifters
{
    /// <summary>Server: a piece into a point by that point's own rule (a carry's end, a cab's set-down). True when it went in.</summary>
    internal static bool PutPiece(ConstructWorld world, IWorldQuery? query, Construct b, LiftPointKind kind, int part, byte bay, ConstructPartDef d, byte health, uint paint)
    {
        var ok = false;
        switch (kind)
        {
            case LiftPointKind.Rack:
                ok = world.RackPut(b, part, d);
                if (ok && world.StockTargetN > 0)
                    ConstructLogisticsPlan.Arrived(world, b, part, 0, d); // (a stock target's refill counts it, as a conveyor's delivery does)
                if (ok)
                    PadArrived(world, b, part, 0, d); // (phase 5b: a station's large part on its pick pad counts for its order)
                break;
            case LiftPointKind.Tray:
                ok = world.RackPut(b, part, d);
                if (ok)
                    ConstructCrafting.Arrived(world, b, part, d); // (phase 5b: it counts for the oldest open order of its kind, as a conveyor's delivery does)
                break;
            case LiftPointKind.Assembler:
                ok = world.WantsPart(b, part, (ushort)d.Id) && world.FeedPart(b, part, d);
                break;
            case LiftPointKind.PressBay:
                ok = query != null && ConstructPress.TakeFromConveyor(world, query, b, part, bay, d, health / 255f, paint);
                break;
            case LiftPointKind.Former:
                ok = ConstructPanelFormer.TakeFromConveyor(world, b, part, d);
                break;
            case LiftPointKind.RunStart:
                ok = health == 255 && paint == 0 && PutOnRun(world, b, part, d); // (a run carries a piece as new: a damaged or painted one stays in the hand)
                break;
        }
        if (ok && kind is not (LiftPointKind.Rack or LiftPointKind.Tray or LiftPointKind.RunStart) && world.ChainCount > 0)
            ConstructLogisticsPlan.Arrived(world, b, part, 0, d); // (a chained order's input: it counts for its link by kind, as a conveyor's delivery does)
        if (ok)
        {
            world.CraftChanged(b);
            b.Wake();
        }
        return ok;
    }
}
