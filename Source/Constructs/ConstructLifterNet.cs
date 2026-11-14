using System.Numerics;

namespace CallOfSlop;

public sealed partial class Construct
{
    internal int HasLiftStamp = int.MinValue, HasLiftParts = -1; // (ConstructLifters.HasLifter: the parts it was worked out for)
    internal bool HasLift;
}

/// <summary>One lifter edge of the logistics map (<see cref="LogiMap"/>): the lifter part carries from a pick part to a put part.</summary>
public struct LiftEdge
{
    public ushort Lifter, From, To; // parts on the map's body
    public float Len; // metres: the hand's way out and back in, plus the handling (ConstructLogistics.JunctionCost)
    public byte Narrow; // the widest piece it takes across, in conveyor width steps (ConstructConveyor.StepCm)
}

/// <summary>
/// Lifters on the logistics network (docs/sandbox/design/arms-and-cranes.md section 5, phase 5; the notes are
/// docs/sandbox/design/lifters-phase5b-notes.md). A lifter is a node of its body's logistics map and each of its pick-to-put
/// point pairs on that body an edge, worked out on both peers from the parts alone (nothing on the wire), so a Router sends a
/// piece toward a lifter's pick point when its way goes on by the lifter, chains and fetches cross it, and a terminal's or a
/// station's network reaches the machines an arm serves. A Building Station's large parts (longer or wider than its hold) are
/// asked for as before and delivered to a pick pad its arms fit from: a Router sends them there, an arm carries them there,
/// a fetch from stock rides there; what lies on its pick pads for it is reserved for the build (<see cref="ConstructReserve"/>).
/// </summary>
public static partial class ConstructLifters
{
    public const int MaxEdges = 128; // a body's lifter edges on its map
    public const ushort PadClaim = 0xFFFE; // the OwnerId of a station's claims on its pick pads (its tray's handle is the Owner)
    private static readonly LiftPoint[] EdgePts = new LiftPoint[MaxPoints];

    // ---- The map's edges (either peer) -------------------------------------------------------------------------------------

    /// <summary>Is a point one a piece is put to (a want or storage), or picked from?</summary>
    private static bool PutKind(LiftPointKind k) => k is LiftPointKind.Rack or LiftPointKind.Assembler or LiftPointKind.PressBay or LiftPointKind.Former or LiftPointKind.Tray;

    /// <summary>
    /// The lifter edges of body <paramref name="c"/> into <paramref name="edges"/>: for each whole lifter that carries (Auto or Carry
    /// only), each pair of its points on this body from a rack, a pad or a machine's out to a put point. A pad to a pad or a rack to a
    /// rack is no edge (storage to storage is never carried). How many. Allocates nothing; called when the map is made again.
    /// </summary>
    public static int MapEdges(Construct c, Span<LiftEdge> edges)
    {
        var n = 0;
        for (var i = 0; i < c.PartCount && n < edges.Length; i++)
        {
            var d = c.DefOf(i);
            if (d.Lifter == null || c.Parts[i].Broken || DefOf(c, i) is not { } ld)
                continue;
            var mode = (int)ConstructConfig.Get(c.Parts[i], d, CfgKey.LiftMode, 0);
            if (mode is 1 or 3)
                continue; // (Station only, Off: it carries nothing between points)
            Frame(c, i, ld, out _, out var lr, out var shoulder);
            var pn = 0;
            PointsOn(c, i, ld, shoulder, lr, EdgePts, ref pn);
            var narrow = (byte)Math.Clamp((int)(ld.PieceAcross * 100f / ConstructConveyor.StepCm), 1, 254);
            for (var a = 0; a < pn; a++)
            {
                ref readonly var pa = ref EdgePts[a];
                if (pa.Kind is not (LiftPointKind.Rack or LiftPointKind.Out) || !PointAt(c, pa, out var at))
                    continue;
                for (var b = 0; b < pn && n < edges.Length; b++)
                {
                    ref readonly var pb = ref EdgePts[b];
                    if (pb.Part == pa.Part || !PutKind(pb.Kind) || pa.Kind == LiftPointKind.Rack && pb.Kind == LiftPointKind.Rack && !(c.DefOf(pb.Part).IsLayDownPad && !c.DefOf(pa.Part).IsLayDownPad))
                        continue; // (rack to pad only: a stock rack's piece to a station's pick pad)
                    if (!PointAt(c, pb, out var bt))
                        continue;
                    var len = Vector3.Distance(at, shoulder) + Vector3.Distance(bt, shoulder);
                    var dup = -1;
                    for (var e = 0; e < n; e++)
                        if (edges[e].From == pa.Part && edges[e].To == pb.Part)
                            dup = e;
                    if (dup >= 0)
                    {
                        if (len < edges[dup].Len)
                            (edges[dup].Len, edges[dup].Lifter, edges[dup].Narrow) = (len, (ushort)i, narrow);
                        continue;
                    }
                    edges[n++] = new LiftEdge { Lifter = (ushort)i, From = pa.Part, To = pb.Part, Len = len, Narrow = narrow };
                }
            }
        }
        return n;
    }

    /// <summary>
    /// A terminal's network walk (ConstructCrafting.Build): what part <paramref name="at"/> is joined to by a lifter edge, added to
    /// <paramref name="seen"/>, so the walk goes on past it (a pick rack's conveyors and their Routers and stores). The new count.
    /// </summary>
    public static int Joined(Construct c, int at, Span<int> seen, int n)
    {
        if (c.Logi is not { LiftN: > 0 } m)
            return n; // (KeyOf made the map up to date just before)
        for (var e = 0; e < m.LiftN && n < seen.Length; e++)
        {
            int other = m.Lifts[e].From == at ? m.Lifts[e].To : m.Lifts[e].To == at ? m.Lifts[e].From : -1;
            if (other < 0 || other >= c.PartCount || seen[..n].Contains(other))
                continue;
            seen[n++] = other;
        }
        return n;
    }

    /// <summary>Does a lifter's edge carry from store <paramref name="from"/> to machine <paramref name="to"/> (what lies there counts as riding to it: <see cref="AtPicks"/>)?</summary>
    public static bool LiftsTo(Construct c, int from, int to)
    {
        if (c.Logi is not { LiftN: > 0 } m)
            return false;
        for (var e = 0; e < m.LiftN; e++)
            if (m.Lifts[e].From == from && m.Lifts[e].To == to)
                return true;
        return false;
    }

    /// <summary>
    /// Is a store joined to the rest by lifters only (no conveyor or chute touches it)? It is not a terminal's stock: it would be
    /// claimed to be collected by hand, and an arm never carries what is claimed.
    /// </summary>
    public static bool LiftOnly(Construct c, int part)
    {
        if (c.Logi is not { LiftN: > 0 })
            return false;
        for (var k = 0; k < c.RouteCount; k++)
            if (c.Routes[k].Type is RouteType.Conveyor or RouteType.Chute && (c.Routes[k].Source == part || c.Routes[k].To == part))
                return false;
        return true;
    }

    /// <summary>
    /// Server: pieces of a kind on their way to machine <paramref name="dest"/> by a lifter: lying at a pick rack or pad whose lifter puts
    /// to it, or in a hand bound for it. A chained order counts them as riding (a piece that reached a pick point lost its rider's tag),
    /// so it doesn't send more while the arm brings them.
    /// </summary>
    public static int AtPicks(ConstructWorld world, Construct c, int dest, ushort kind)
    {
        if (c.Logi is not { LiftN: > 0 } m)
            return 0;
        var n = 0;
        for (var e = 0; e < m.LiftN; e++)
        {
            if (m.Lifts[e].To != dest || m.Lifts[e].From >= c.PartCount || !c.DefOf(m.Lifts[e].From).IsPieceRack)
                continue;
            var dup = false;
            for (var o = 0; o < e && !dup; o++)
                dup = m.Lifts[o].To == dest && m.Lifts[o].From == m.Lifts[e].From;
            if (!dup && world.StoreOf(c, m.Lifts[e].From) is { } st)
                n += ConstructCrafting.Available(world, st, kind); // (what nobody claimed: an arm never carries a claimed piece)
        }
        return n + (world.LiftBusy > 0 ? HandsTo(world, c.Id, dest, kind) : 0);
    }

    private static readonly string[] Blocked =
    [
        "waiting on the Loader Arm: it is broken", "waiting on the Loader Arm: no power", "waiting on the Loader Arm: its Run is held off",
        "waiting on the Loader Arm: its Works setting keeps it from carrying", "waiting on the Loader Arm: it is stuck with a piece nothing takes",
        "waiting on the Bridge Crane: it is off its rails", "waiting on the Bridge Crane: it is broken", "waiting on the Bridge Crane: no power",
        "waiting on the Bridge Crane: its Run is held off", "waiting on the Bridge Crane: its Works setting keeps it from carrying",
        "waiting on the Bridge Crane: it is stuck with a piece nothing takes",
    ];

    /// <summary>
    /// Server: why the pieces waiting at the pick points for machine <paramref name="dest"/> don't move: every lifter whose edge puts to
    /// it can't work now (a fixed text for the chained order's row: "waiting on the Loader Arm: no power"), or null (one can, or none waits).
    /// </summary>
    public static string? PickBlocked(ConstructWorld world, Construct c, int dest, ushort kind)
    {
        if (c.Logi is not { LiftN: > 0 } m || !world.LiftSeen.TryGetValue(c.Id, out var lb))
            return null;
        string? why = null;
        for (var e = 0; e < m.LiftN; e++)
        {
            if (m.Lifts[e].To != dest || m.Lifts[e].From >= c.PartCount || world.StoreOf(c, m.Lifts[e].From) is not { } st || ConstructCrafting.Available(world, st, kind) == 0)
                continue;
            int part = m.Lifts[e].Lifter;
            CLift? l = null;
            for (var i = 0; i < lb.N && l == null; i++)
                if (lb.Lifts[i].Part == part)
                    l = lb.Lifts[i];
            if (l == null || part >= c.PartCount)
                continue;
            var mode = (int)ConstructConfig.Get(c.Parts[part], c.DefOf(part), CfgKey.LiftMode, 0);
            var w = DefOf(c, part) is { Bridge: true } bd ? (bd.RailWhy != 0 ? 5 : 6) : 0; // (fixed texts: an arm's, a crane's)
            var now = c.Parts[part].Broken ? Blocked[w]
                : w == 5 ? Blocked[5]
                : !ConstructRecycler.Powered(world, c, part) ? Blocked[w == 6 ? 7 : 1]
                : ConstructExtruder.RunHeld(c, part) ? Blocked[w == 6 ? 8 : 2]
                : mode is 1 or 3 ? Blocked[w == 6 ? 9 : 3]
                : l.State == LiftState.Stuck && l.Why != LiftWhy.Holding ? Blocked[w == 6 ? 10 : 4]
                : null;
            if (now == null)
                return null; // (one that puts here can work: the pieces move)
            why ??= now;
        }
        return why;
    }

    /// <summary>Has the body a lifter part? Kept on the body until its parts change (a terminal's network key asks on every look).</summary>
    public static bool HasLifter(Construct c)
    {
        if (c.HasLiftStamp == c.RebuildStamp && c.HasLiftParts == c.PartCount)
            return c.HasLift;
        (c.HasLiftStamp, c.HasLiftParts, c.HasLift) = (c.RebuildStamp, c.PartCount, false);
        for (var i = 0; i < c.PartCount && !c.HasLift; i++)
            c.HasLift = c.DefOf(i).Lifter != null;
        return c.HasLift;
    }

    /// <summary>
    /// Either peer: a hash of what a body's lifter edges follow from (each lifter's and each possible point's part, kind, place, whether
    /// broken, and a lifter's Works setting), so a rebuild that changed none of them keeps the edges (a machine's piece leaving the body).
    /// </summary>
    public static ulong EdgeKey(Construct c)
    {
        var h = 14695981039346656037UL ^ (ulong)c.PartCount;
        for (var i = 0; i < c.PartCount; i++)
        {
            var d = c.DefOf(i);
            if (d.Lifter == null && !d.IsPieceRack && !d.IsAssembler && !d.IsPress && !d.IsExtruder && !d.IsPanelFormer)
                continue;
            ref readonly var p = ref c.Parts[i];
            h = (h ^ (ulong)i ^ (ulong)d.Id << 16 ^ (ulong)p.Rot << 40 ^ (ulong)p.Seg << 48 ^ (p.Broken ? 1UL << 56 : 0)) * 1099511628211UL;
            h = (h ^ (ulong)BitConverter.SingleToUInt32Bits(p.Pos.X) ^ (ulong)BitConverter.SingleToUInt32Bits(p.Pos.Y) << 21 ^ (ulong)BitConverter.SingleToUInt32Bits(p.Pos.Z) << 42) * 1099511628211UL;
            if (d.Lifter != null)
                h = (h ^ (ulong)ConstructConfig.Get(p, d, CfgKey.LiftMode, 0) ^ 0x5A00) * 1099511628211UL;
            for (var k = 0; k < c.RouteCount && !d.IsPieceRack && d.Lifter == null; k++)
                if (c.Routes[k].Type == RouteType.Conveyor && (c.Routes[k].From == i || c.Routes[k].To == i))
                    h = (h ^ (ulong)k << 8 ^ c.Routes[k].FromPort ^ (ulong)c.Routes[k].ToPort << 32) * 1099511628211UL; // (a machine's port with a conveyor on it is no point)
        }
        return h;
    }

    /// <summary>Either peer, when the map is made: each lifter part on the body and its Works setting, to see a setting change (a setting is no rebuild).</summary>
    public static void NoteModes(Construct c, LogiMap m)
    {
        m.LiftPartN = 0;
        for (var i = 0; i < c.PartCount && m.LiftPartN < LogiMap.MaxLiftParts; i++)
            if (c.DefOf(i).Lifter != null)
                (m.LiftParts[m.LiftPartN], m.LiftModes[m.LiftPartN++]) = ((ushort)i, (byte)ConstructConfig.Get(c.Parts[i], c.DefOf(i), CfgKey.LiftMode, 0));
    }

    /// <summary>Either peer, each time the map is asked for: did a lifter's Works setting change since it was made (its edges come or go)?</summary>
    public static bool ModesChanged(Construct c, LogiMap m)
    {
        for (var k = 0; k < m.LiftPartN; k++)
        {
            int i = m.LiftParts[k];
            if (i >= c.PartCount || c.DefOf(i).Lifter == null || (byte)ConstructConfig.Get(c.Parts[i], c.DefOf(i), CfgKey.LiftMode, 0) != m.LiftModes[k])
                return true;
        }
        return false;
    }

    // ---- A terminal's tray as a put point (server) -------------------------------------------------------------------------

    /// <summary>Hands bound for this point with a piece of this kind (a carry under way).</summary>
    private static int HandsTo(ConstructWorld world, ushort body, int part, ushort def)
    {
        var n = 0;
        for (var bi = 0; bi < world.LiftBodies.Count; bi++)
        {
            var lb = world.LiftBodies[bi];
            for (var i = 0; i < lb.N; i++)
            {
                var o = lb.Lifts[i];
                if (o.State == LiftState.Carry && o.ToKind != LiftPointKind.None && o.StationBody == body && o.ToPart == part && o.Def == def && !o.Fitted)
                    n++;
            }
        }
        return n;
    }

    /// <summary>Riders on body <paramref name="b"/> bound for store <paramref name="handle"/> for order <paramref name="tag"/> (0: any).</summary>
    private static int RidingTo(Construct b, ushort handle, ushort tag)
    {
        var n = 0;
        for (var i = 0; i < b.ConveyedCount; i++)
            if (b.Conveyed[i].Dest == handle && (tag == 0 || b.Conveyed[i].Tag == tag))
                n++;
        return n;
    }

    /// <summary>Server: the open order of a Crafting Terminal's tray that still waits for a part of this kind (made or fetched, less what rides or is carried to it): its id, or 0.</summary>
    private static ushort TrayWant(ConstructWorld world, Construct b, int t, ConstructPartDef d)
    {
        if (b.Parts[t].Craft is not { Count: > 0 } q || world.StoreOf(b, t) is not { } tray || ConstructConveyor.RackCheck(b, t, tray, d) != 0)
            return 0;
        var hands = -1;
        for (var k = 0; k < q.Count; k++)
        {
            ref var o = ref q.Items[k];
            if (o.Done || o.Cancelled || o.Asked + o.Sent == 0 || ConstructCrafting.KindOf(ref o) != d.Id)
                continue;
            if (hands < 0)
                hands = HandsTo(world, b.Id, t, (ushort)d.Id);
            if (o.Asked + o.Sent - RidingTo(b, tray.Handle, o.Id) - hands > 0)
                return o.Id;
        }
        return 0;
    }

    // ---- Pick pads for a station's large parts (server) -------------------------------------------------------------------

    /// <summary>Does a station's hold refuse this kind for its size (it goes to a pick pad instead)?</summary>
    public static bool TooBigForHold(Construct sc, int t, CStore tray, ConstructPartDef d) => ConstructConveyor.RackCheck(sc, t, tray, d) == 3;

    /// <summary>Is pad (<paramref name="padBody"/>, <paramref name="pad"/>) a pick pad of an arm that can fit for station <paramref name="t"/> now?</summary>
    private static bool ServesPad(ConstructWorld world, Construct sc, int t, ushort padBody, int pad)
    {
        ConstructStation.Bay(sc, t, out var floor, out _, out var bay);
        for (var bi = 0; bi < world.LiftBodies.Count; bi++)
        {
            var lb = world.LiftBodies[bi];
            Construct? c = null;
            for (var i = 0; i < lb.N; i++)
            {
                var l = lb.Lifts[i];
                if ((c ??= world.Get(lb.Body)) == null || l.Part >= c.PartCount || ConstructLifters.DefOf(c, l.Part) is not { } ld)
                    continue;
                if (l.Points == null)
                    FindPoints(world, c, l, ld); // (just loaded or placed)
                var at = -1;
                for (var k = 0; k < l.PointN && at < 0; k++)
                    if (l.Points![k].Body == padBody && l.Points[k].Part == pad && l.Points[k].Kind == LiftPointKind.Rack)
                        at = k;
                if (at < 0 || (int)ConstructConfig.Get(c.Parts[l.Part], c.DefOf(l.Part), CfgKey.LiftMode, 0) is 2 or 3 || !CanWorkNow(world, c, l))
                    continue;
                Frame(c, l.Part, ld, out _, out _, out var shoulder);
                if (Vector3.Distance(floor, shoulder) <= ld.ReachMax + bay.Length() * 0.5f + ld.Down)
                    return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Server: the building stations a pad serves, one at a time: <paramref name="si"/> and <paramref name="t"/> walk them (start
    /// both at 0 and -1). True while there is one: its body, its part, its job and its hold.
    /// </summary>
    private static bool NextStation(ConstructWorld world, ref int si, ref int t, out Construct sc, out StationJob j, out CStore tray)
    {
        for (; si < world.StationOpen.Count; si++, t = -1)
        {
            if (world.Get(world.StationOpen[si]) is not { } c)
                continue;
            while (++t < c.PartCount)
                if (c.Parts[t].Station is { State: StationState.Building } sj && c.DefOf(t).IsBuildStation && world.StoreOf(c, t) is { } st)
                {
                    (sc, j, tray) = (c, sj, st);
                    return true;
                }
        }
        (sc, j, tray) = (null!, null!, null!);
        return false;
    }

    /// <summary>The station order of this kind still open (by <paramref name="tag"/> when it isn't 0): its index, or -1.</summary>
    private static int OrderOf(Construct sc, int t, ushort def, ushort tag)
    {
        if (sc.Parts[t].Craft is not { Count: > 0 } q)
            return -1;
        for (var k = 0; k < q.Count; k++)
        {
            ref var o = ref q.Items[k];
            if (!o.Done && !o.Cancelled && (tag == 0 || o.Id == tag) && ConstructCrafting.KindOf(ref o) == def)
                return k;
        }
        return -1;
    }

    /// <summary>
    /// Server: does a building station want a part of this kind on pad <paramref name="pad"/> (a Lay-down Pad of body
    /// <paramref name="b"/>): too big for its hold, one of its open orders still waits for it (less what rides or is carried to the
    /// pad), and one of its arms fits from the pad. The order's id (a rider's tag), or 0.
    /// </summary>
    public static ushort PadWant(ConstructWorld world, Construct b, int pad, ConstructPartDef d)
    {
        if (world.StationOpen.Count == 0 || world.LiftBodies.Count == 0 || pad >= b.PartCount || !b.DefOf(pad).IsLayDownPad || world.StoreOf(b, pad) is not { } ps)
            return 0;
        int si = 0, t = -1;
        while (NextStation(world, ref si, ref t, out var sc, out var j, out var tray))
            if (PadLack(world, b, pad, ps, sc, t, j, tray, d) > 0)
                return OrderOf(sc, t, (ushort)d.Id, 0) is var k and >= 0 ? sc.Parts[t].Craft!.Items[k].Id : PadClaim; // (no order: nothing linked makes it, but an arm may bring it)
        return 0;
    }

    /// <summary>
    /// How many more of a kind station <paramref name="t"/> wants on this pad: it is too big for the hold, an arm that fits from the pad
    /// serves the station, and the build lacks it (needed, less fitted, on its arms' pick pads, riding or carried to the pad).
    /// </summary>
    private static int PadLack(ConstructWorld world, Construct b, int pad, CStore ps, Construct sc, int t, StationJob j, CStore tray, ConstructPartDef d)
    {
        var kind = Array.IndexOf(j.KindDef, (ushort)d.Id, 0, j.KindN);
        if (kind < 0 || !TooBigForHold(sc, t, tray, d) || !ServesPad(world, sc, t, b.Id, pad))
            return 0;
        return j.KindNeed[kind] - j.KindFit[kind] - PickPadHolds(world, sc, t, (ushort)d.Id) - RidingTo(b, ps.Handle, 0) - HandsTo(world, b.Id, pad, (ushort)d.Id);
    }

    /// <summary>Server: is a rider's way to this pad (a station's large part) still wanted? Its order, or the build's lack.</summary>
    public static bool PadOrderOpen(ConstructWorld world, Construct b, int pad, ushort tag, ConstructPartDef d)
    {
        if (world.StoreOf(b, pad) is not { } ps)
            return false;
        int si = 0, t = -1;
        while (NextStation(world, ref si, ref t, out var sc, out var j, out var tray))
            if (TooBigForHold(sc, t, tray, d) && ServesPad(world, sc, t, b.Id, pad) && (OrderOf(sc, t, (ushort)d.Id, tag) >= 0 || PadLack(world, b, pad, ps, sc, t, j, tray, d) + 1 > 0))
                return true; // (+1: this rider is one of those riding there)
        return false;
    }

    /// <summary>
    /// Server: a part landed on a pick pad (a conveyor's delivery, an arm's put, a piece set down on it): it counts for the station
    /// order that waits for it there (the rider's <paramref name="tag"/> first), as a part landing in the station's hold does.
    /// </summary>
    public static void PadArrived(ConstructWorld world, Construct b, int pad, ushort tag, ConstructPartDef d)
    {
        if (world.StationOpen.Count == 0 || world.LiftBodies.Count == 0 || !b.DefOf(pad).IsLayDownPad)
            return;
        for (var pass = tag == 0 ? 1 : 0; pass < 2; pass++)
        {
            int si = 0, t = -1;
            while (NextStation(world, ref si, ref t, out var sc, out _, out var tray))
            {
                if (OrderOf(sc, t, (ushort)d.Id, pass == 0 ? tag : (ushort)0) < 0 || !TooBigForHold(sc, t, tray, d) || !ServesPad(world, sc, t, b.Id, pad))
                    continue;
                ConstructCrafting.Arrived(world, sc, t, d);
                Console.WriteLine($"[lifter] a {d.Name} landed on the pick pad (body {b.Id} part {pad}) for the station (body {sc.Id} part {t})");
                return;
            }
        }
    }

    /// <summary>
    /// Server: a Lay-down Pad on the station's body that one of its arms fits from, that takes a piece of kind <paramref name="d"/> now,
    /// and that conveyor <paramref name="via"/> reaches (straight, through Joins, or a Router with a way there): a fetch of a large part
    /// rides there. -1: none (the fetch goes as before).
    /// </summary>
    public static int PickPadFor(ConstructWorld world, Construct sc, int t, ConstructPartDef d, int via)
    {
        if (world.LiftBodies.Count == 0 || via < 0 || via >= sc.RouteCount)
            return -1;
        for (var i = 0; i < sc.PartCount; i++)
            if (sc.DefOf(i).IsLayDownPad && !sc.Parts[i].Broken && world.StoreOf(sc, i) is { } st && ConstructConveyor.RackCheck(sc, i, st, d) == 0
                && WayReaches(sc, via, i) && ServesPad(world, sc, t, sc.Id, i))
                return i;
        return -1;
    }

    /// <summary>Does a piece put on conveyor <paramref name="via"/> reach part <paramref name="to"/>: its end, through Joins, or a Router with a way there?</summary>
    private static bool WayReaches(Construct c, int via, int to)
    {
        int at = c.Routes[via].To;
        for (var hop = 0; hop < 8; hop++)
        {
            if (at == to)
                return true;
            if (at >= c.PartCount)
                return false;
            if (c.DefOf(at).IsRouter)
                return ConstructLogistics.Reaches(c, at, to);
            int next = -1, outs = 0;
            for (var k = 0; k < c.RouteCount; k++)
                if (c.Routes[k].Type == RouteType.Conveyor && c.Routes[k].From == at && c.Routes[k].FedEnd)
                    (next, outs) = (c.Routes[k].To, outs + 1);
            if (outs != 1 || !c.DefOf(at).IsConveyorJunction)
                return false; // (a splitter deals in turn: no way)
            at = next;
        }
        return false;
    }

    // ---- What lies on the pick pads for a build is reserved for it ---------------------------------------------------------

    private const int MaxPadClaims = 64;
    private static readonly ushort[] ClaimStore = new ushort[MaxPadClaims], ClaimDef = new ushort[MaxPadClaims], ClaimCount = new ushort[MaxPadClaims];
    private static readonly int[] ClaimLeft = new int[256];

    /// <summary>
    /// Server, with the station's orders (2 Hz while building): the pieces its arms' pick pads hold that the build counts as held
    /// (<see cref="PickPadHolds"/>) are claimed for it, so a terminal's order or another arm's carry never takes them; claims it no
    /// longer counts go. A claim is changed only when its count changes (each change is sent with its store). Allocates nothing.
    /// </summary>
    public static void PadClaims(ConstructWorld world, Construct sc, int t, CStore tray, StationJob j)
    {
        var want = 0;
        if (j.State == StationState.Building && world.PadBodies.Count > 0 && world.LiftBodies.Count > 0 && j.KindN <= ClaimLeft.Length)
        {
            for (var q = 0; q < j.KindN; q++)
                ClaimLeft[q] = PickPadHolds(world, sc, t, j.KindDef[q]); // (worked out once for all kinds: PadCounts)
            for (var s = 0; s < world.PadSeenN && want < MaxPadClaims; s++)
            {
                if (world.Get((ushort)(world.PadSeen[s] >> 16)) is not { } b || world.StoreOf(b, (int)(world.PadSeen[s] & 0xFFFF)) is not { } st)
                    continue;
                for (var q = 0; q < j.KindN && want < MaxPadClaims; q++)
                {
                    if (ClaimLeft[q] <= 0)
                        continue;
                    var n = Math.Min(ClaimLeft[q], ConstructCrafting.Holds(st, j.KindDef[q]));
                    if (n <= 0)
                        continue;
                    ClaimLeft[q] -= n;
                    (ClaimStore[want], ClaimDef[want], ClaimCount[want]) = (st.Handle, j.KindDef[q], (ushort)n);
                    want++;
                }
            }
        }
        var res = world.Reserve;
        for (var i = res.Count - 1; i >= 0; i--)
        {
            ref var r = ref res.Items[i];
            if (r.Owner != tray.Handle || r.OwnerId != PadClaim)
                continue;
            var at = -1;
            for (var w = 0; w < want && at < 0; w++)
                if (ClaimStore[w] == r.Store && ClaimDef[w] == r.Def)
                    at = w;
            if (at >= 0 && ClaimCount[at] == r.Count)
            {
                ClaimCount[at] = 0; // (as it is: nothing to send)
                continue;
            }
            if (at >= 0 && ClaimCount[at] > r.Count)
            {
                ClaimCount[at] -= r.Count; // (the rest is added below, onto this claim)
                continue;
            }
            res.Shrink(i, at >= 0 ? r.Count - ClaimCount[at] : r.Count, world.StoreByHandle(r.Store));
            if (at >= 0)
                ClaimCount[at] = 0;
        }
        for (var w = 0; w < want; w++)
            if (ClaimCount[w] > 0 && world.StoreByHandle(ClaimStore[w]) is { } st)
                res.Add(st, ClaimDef[w], ClaimCount[w], tray.Handle, PadClaim);
    }
}
