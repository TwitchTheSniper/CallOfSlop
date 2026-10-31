using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Freeform welds (board item dev-sandbox-freeform-weld-fw01, the developer's decision 25; docs/sandbox/CONSTRUCTS.md,
/// "Freeform welds"). The welder's second mode welds two bodies exactly where they are: the piece (its whole body) joins
/// the other construct as a new rigid segment whose rest frame sits at a fixed, arbitrary pose on its parent segment
/// (<see cref="JointType.Fixed"/>: <see cref="CJoint.FixRot"/> / <see cref="CJoint.FixPos"/>; the anchor part is
/// <see cref="CJoint.Hinge"/>, the welded-on part <see cref="CJoint.Child"/>). Nothing solves it: its parts are hull points
/// of the body, its mass and inertia are the body's, and inside it every part stays on its own Rot24 grid, so socket parts
/// still fit onto it. The welder cuts the seam free again; losing the anchor part (or the welded-on one) splits it off
/// like any part. Nothing here allocates.
/// </summary>
public static class Freeform
{
    public const float MaxGap = 0.25f; // the closest boxes of the two parts at most this far apart (touching not needed: the bead bridges it)
    public const int MaxPerConstruct = 8; // freeform welds a construct (they share Construct.MaxJoints with the joints)
    public const float WeldSeconds = 2.5f; // survival: a freeform weld takes as long as welding a part on (creative: at once)
    public const float SeamReach = 0.6f; // the welder this near a seam's bead (on one of its two parts) cuts it

    /// <summary>Part <paramref name="part"/>'s bounds as a box in the world: its turn, centre and half size.</summary>
    public static void PartBox(Construct c, int part, out Quaternion q, out Vector3 centre, out Vector3 half)
    {
        c.WorldFrame(c.Parts[part].Seg, out q, out var t);
        c.PartBounds(part, out var mn, out var mx);
        centre = t + Vector3.Transform((mn + mx) * 0.5f, q);
        half = (mx - mn) * 0.5f;
    }

    private static Vector3 Closest(Quaternion q, Vector3 c, Vector3 h, Vector3 p)
    {
        var l = Vector3.Transform(p - c, Quaternion.Conjugate(q));
        return c + Vector3.Transform(Vector3.Clamp(l, -h, h), q);
    }

    /// <summary>
    /// The gap between two parts' boxes (0 when they overlap) and its closest points (the weld bead's ends), by alternating
    /// projections between the two boxes (convex: it converges on the closest pair).
    /// </summary>
    public static float Gap(Construct a, int pa, Construct b, int pb, out Vector3 onA, out Vector3 onB)
    {
        PartBox(a, pa, out var qa, out var ca, out var ha);
        PartBox(b, pb, out var qb, out var cb, out var hb);
        onA = ca;
        onB = cb;
        for (var k = 0; k < 24; k++)
        {
            onA = Closest(qa, ca, ha, onB);
            var nb = Closest(qb, cb, hb, onA);
            var moved = Vector3.DistanceSquared(nb, onB);
            onB = nb;
            if (moved < 1e-10f)
                break;
        }
        return Vector3.Distance(onA, onB);
    }

    /// <summary>Held to the ground for real (a working foundation or pinned; not just frozen by the physics gun).</summary>
    public static bool Grounded(Construct c)
    {
        if (c.Pinned)
            return true;
        for (var i = 0; i < c.PartCount; i++)
            if (c.Parts[i].D.Role == PartRole.Foundation && c.Parts[i].Working && c.Parts[i].Seg == 0)
                return true;
        return false;
    }

    /// <summary>
    /// Which of the two keeps its frame (the other is welded on as the freeform segment): one held to the ground, else
    /// not a loose part, else the one with more parts, else <paramref name="b"/> (the second picked: the vehicle a piece
    /// is held against).
    /// </summary>
    public static bool KeepsFirst(Construct a, Construct b)
    {
        bool ga = Grounded(a), gb = Grounded(b);
        if (ga != gb)
            return ga;
        if (a.Loose != b.Loose)
            return b.Loose;
        if (a.PartCount != b.PartCount)
            return a.PartCount > b.PartCount;
        return false;
    }

    /// <summary>
    /// The physical rules for welding part <paramref name="pa"/> of <paramref name="a"/> to part <paramref name="pb"/> of
    /// <paramref name="b"/> where they are (the server's and the preview's; permissions are the server's,
    /// <see cref="ConstructEdits.BeginFreeform"/>). Null, or why not.
    /// </summary>
    public static string? Check(ConstructWorld world, Construct a, int pa, Construct b, int pb)
    {
        if (a == b)
            return "both parts are on one body already: pick a part on another body";
        if (pa < 0 || pa >= a.PartCount || pb < 0 || pb >= b.PartCount)
            return "that part is gone";
        if (a.Parts[pa].Broken || b.Parts[pb].Broken)
            return "that part is wrecked";
        var gap = Gap(a, pa, b, pb, out _, out _);
        if (gap > MaxGap)
            return gap < 10 ? $"too far apart: {gap:0.00} m (at most {MaxGap:0.00} m)" : "too far apart";
        var keep = KeepsFirst(a, b) ? a : b;
        var mover = keep == a ? b : a;
        if (keep.FreeformCount + mover.FreeformCount + 1 > MaxPerConstruct)
            return $"at most {MaxPerConstruct} freeform welds a construct";
        if (keep.JointCount + mover.JointCount + 1 > Construct.MaxJoints)
            return $"together they'd have more than {Construct.MaxJoints} joints and freeform welds";
        if (world.TotalJoints + 1 > world.JointCap)
            return "the world has too many joints";
        if (mover.WheelCount > 0)
            return "that piece has wheels: weld it on with socket welds (wheels roll in the body's own frame)";
        if (a.CarriedBy != 0 || b.CarriedBy != 0)
            return "it rides in a cargo volume: let it go first";
        if (mover.CargoCount > 0)
            return "that piece carries cargo: let the cargo go first";
        var anchor = keep == a ? pa : pb;
        if (OnLeafSide(keep, anchor, mover, keep == a ? pb : pa))
        {
            // It hangs on the joint (board item wh01): room for the joint, and it mustn't touch the joint's fixed side too.
            if (keep.JointOf(anchor) < 0 && (keep.JointCount + mover.JointCount + 2 > Construct.MaxJoints || world.TotalJoints + 2 > world.JointCap))
                return $"together they'd have more than {Construct.MaxJoints} joints and freeform welds";
            if (SocketWeld.LockRefusal(keep, anchor, mover, mover.Pos, mover.Rot) is { } locks)
                return locks;
        }
        return world.CanMerge(keep, mover);
    }

    /// <summary>
    /// Is the freeform weld onto joint part <paramref name="anchor"/>'s moving side (board item wh01)? The welded part's
    /// nearest point on the joint part's box lies on its leaf's end (the outer quarter along the leaf socket's facing). The
    /// piece then hangs on that joint, as anything the wrench puts on the leaf does, and moves with it.
    /// </summary>
    public static bool OnLeafSide(Construct keep, int anchor, Construct mover, int welded)
    {
        if ((uint)anchor >= (uint)keep.PartCount || (uint)welded >= (uint)mover.PartCount || keep.DefOf(anchor) is not { HasJoint: true, LeafIndex: >= 0 } d)
            return false;
        Gap(keep, anchor, mover, welded, out var onA, out _);
        PartBox(keep, anchor, out var q, out var centre, out var half);
        keep.SocketAt(anchor, d.LeafIndex, out _, out var dir);
        var n = ConstructParts.DirVector(dir); // (the part box's frame is its segment's: the leaf's facing in it)
        var along = Vector3.Dot(Vector3.Transform(onA - centre, Quaternion.Conjugate(q)), n);
        var extent = Vector3.Dot(Vector3.Abs(n), half);
        return along > extent * 0.5f;
    }

    /// <summary>The segment a freeform weld onto joint part <paramref name="anchor"/>'s leaf side hangs on: the joint's (made now at rest, or the one it has). 0: no room.</summary>
    public static byte LeafSeg(ConstructWorld world, Construct keep, int anchor)
    {
        var j = keep.JointOf(anchor);
        return j >= 0 && keep.SegCarries(j + 1) ? (byte)(j + 1) : ConstructBuild.HangOnLeaf(world, keep, anchor);
    }

    public const float AxleReach = 0.35f; // a wheel's axle socket this near a suspension's (and facing it) goes onto it

    /// <summary>
    /// A freeform weld of a wheel to a suspension whose axle sockets (both free) are near each other and face each other
    /// (within <see cref="AxleReach"/>; board item wheel-weld): instead of a fixed-pose segment, which would hold the wheel
    /// still, it is the socket weld of the piece's axle onto the other's at the nearest grid turn (<see cref="SocketWeld"/>),
    /// so the wheel rolls on its suspension as one the wrench fitted does. Which body is the piece follows
    /// <see cref="KeepsFirst"/>. False: an ordinary freeform weld (a wheel welded any other way is fixed: it doesn't roll).
    /// </summary>
    public static bool AxleSnap(Construct a, int pa, Construct b, int pb, out AxleSnapPlan plan)
    {
        plan = default;
        if (a == b || pa < 0 || pa >= a.PartCount || pb < 0 || pb >= b.PartCount)
            return false;
        var ra = a.Parts[pa].D.Role;
        var rb = b.Parts[pb].D.Role;
        if (!(ra == PartRole.Wheel && rb == PartRole.Suspension || ra == PartRole.Suspension && rb == PartRole.Wheel))
            return false;
        var first = KeepsFirst(a, b);
        var keep = first ? a : b;
        var mover = first ? b : a;
        int anchor = first ? pa : pb, welded = first ? pb : pa;
        var ms = FreeAxle(mover, welded);
        var ts = FreeAxle(keep, anchor);
        if (ms < 0 || ts < 0 || mover.Parts[welded].Seg != 0)
            return false;
        mover.SocketAt(welded, ms, out var mAt, out var mDir);
        keep.SocketAt(anchor, ts, out var tAt, out var tDir);
        keep.WorldFrame(keep.Parts[anchor].Seg, out var qs, out var t0);
        var mWorld = mover.ToWorld(mAt);
        var tWorld = t0 + Vector3.Transform(tAt, qs);
        var facing = Vector3.Dot(Vector3.Transform(ConstructParts.DirVector(mDir), mover.Rot), Vector3.Transform(ConstructParts.DirVector(tDir), qs));
        if (Vector3.Distance(mWorld, tWorld) > AxleReach || facing > -0.7f)
            return false;
        var w = new WeldTarget { Body = keep.Id, Part = (byte)anchor, Socket = (byte)ts };
        var degrees = SocketWeld.AngleOf(mDir, tDir, Quaternion.Normalize(Quaternion.Conjugate(qs) * mover.Rot));
        if (!SocketWeld.OnRotGrid(mover, welded, ms, keep, w, degrees))
            return false;
        plan = new AxleSnapPlan { Keep = keep, Mover = mover, Part = welded, Socket = ms, Target = w, Degrees = degrees };
        return true;
    }

    private static int FreeAxle(Construct c, int part)
    {
        var d = c.DefOf(part);
        for (var s = 0; s < d.Sockets.Length; s++)
            if (d.Sockets[s].Kind == SocketKind.Axle && !c.SocketUsed(part, s))
                return s;
        return -1;
    }

    /// <summary>
    /// The freeform weld's physical rules with the axle snap (<see cref="AxleSnap"/>): a wheel going onto a suspension's
    /// axle takes the socket weld's rules (<see cref="SocketWeld.Check"/>), anything else <see cref="Check"/>'s. Null, or why not.
    /// </summary>
    public static string? Rules(ConstructWorld world, IWorldQuery? q, Construct a, int pa, Construct b, int pb, ReadOnlySpan<Vector3> feet, out bool snapped)
    {
        snapped = AxleSnap(a, pa, b, pb, out var plan);
        return snapped
            ? SocketWeld.Check(world, q, plan.Mover, plan.Part, plan.Socket, plan.Keep, plan.Target, plan.Degrees, feet)
            : Check(world, a, pa, b, pb);
    }

    /// <summary>Does a freeform weld of these two leave a fixed wheel (one of them a wheel, not going onto a suspension's axle)? The look line says so.</summary>
    public static bool FixedWheel(Construct a, int pa, Construct b, int pb) =>
        (a.Parts[pa].D.Role == PartRole.Wheel || b.Parts[pb].D.Role == PartRole.Wheel) && !AxleSnap(a, pa, b, pb, out _);

    /// <summary>
    /// The ray-through pick: past the part the crosshair hit (<paramref name="first"/>) to the next part along the ray on a
    /// different body (no stored item, no routed link). False when nothing is behind it (the two-click fallback).
    /// </summary>
    public static bool Behind(ConstructWorld world, in ConstructHit first, Vector3 eye, Vector3 dir, float reach, out ConstructHit behind) =>
        world.Raycast(eye, dir, reach, out behind, ignore: first.Body) && behind.SlotPlus1 == 0 && behind.RoutePlus1 == 0;

    /// <summary>The freeform weld whose seam is at world point <paramref name="at"/> on part <paramref name="part"/> (its anchor or its welded-on part), or -1.</summary>
    public static int SeamAt(Construct c, int part, Vector3 at)
    {
        for (var j = 0; j < c.JointCount; j++)
        {
            ref var jt = ref c.Joints[j];
            if (!jt.Freeform || jt.Hinge >= c.PartCount || jt.Child >= c.PartCount || jt.Hinge != part && jt.Child != part)
                continue;
            Gap(c, jt.Hinge, c, jt.Child, out var a, out var b);
            if (Vector3.Distance(at, (a + b) * 0.5f) <= SeamReach)
                return j;
        }
        return -1;
    }

    /// <summary>
    /// A freeform weld as a blueprint step's "weld" (ConstructBuild, "weld": on, pos, rot): the welded-on part's own frame
    /// (its origin and turn) in its anchor's segment frame.
    /// </summary>
    public static void StepPose(Construct c, int joint, out Vector3 pos, out Quaternion rot)
    {
        ref var jt = ref c.Joints[joint];
        ref var p = ref c.Parts[jt.Child];
        rot = Quaternion.Normalize(jt.FixRot * Rot24.Quat(p.Rot));
        pos = jt.FixPos + Vector3.Transform(p.Pos, jt.FixRot);
    }

    /// <summary>A pose's world position and turn for part <paramref name="part"/> (its origin and turn): the self-test compares them.</summary>
    public static void PartWorld(Construct c, int part, out Vector3 pos, out Quaternion rot)
    {
        c.WorldFrame(c.Parts[part].Seg, out var q, out var t);
        rot = Quaternion.Normalize(q * Rot24.Quat(c.Parts[part].Rot));
        pos = t + Vector3.Transform(c.Parts[part].Pos, q);
    }
}

/// <summary>A freeform weld that goes on as a socket weld of a wheel's axle onto a suspension's (<see cref="Freeform.AxleSnap"/>).</summary>
public struct AxleSnapPlan
{
    public Construct Keep, Mover;
    public int Part, Socket;
    public WeldTarget Target;
    public float Degrees;
}

public sealed partial class ConstructWorld
{
    /// <summary>
    /// Welds <paramref name="mover"/> onto <paramref name="keep"/> where it is (call <see cref="Freeform.Check"/> first):
    /// every part, link, joint and routed link of the mover comes across unchanged in its own frame, on a new freeform
    /// segment hanging on the anchor part's segment at the pose the two have now (or on <paramref name="parentSeg"/>: the
    /// segment of the joint whose leaf it hangs on, board item wh01). The mover goes. Returns the new weld's joint index, or -1.
    /// </summary>
    public int FreeformWeld(Construct keep, int anchor, Construct mover, int welded, int parentSeg = -1)
    {
        if (keep == mover || CanMerge(keep, mover) != null || keep.JointCount + mover.JointCount + 1 > Construct.MaxJoints)
            return -1;
        if (keep.Dirty)
            keep.Rebuild();
        if (mover.Dirty)
            mover.Rebuild();
        for (var k = CouplingCount - 1; k >= 0; k--)
            if (Couplings[k].BodyA == keep.Id && Couplings[k].BodyB == mover.Id || Couplings[k].BodyA == mover.Id && Couplings[k].BodyB == keep.Id)
                Decouple(k, "the two were welded into one");
        // The new segment's rest frame (the mover's body frame) in the anchor segment's rest frame, as they are now.
        var parent = parentSeg >= 0 && parentSeg <= keep.JointCount ? (byte)parentSeg : keep.Parts[anchor].Seg;
        keep.WorldFrame(parent, out var qs, out var ts);
        var iq = Quaternion.Conjugate(qs);
        var fixRot = Quaternion.Normalize(iq * mover.Rot);
        var fixPos = Vector3.Transform(mover.Pos - ts, iq);
        var off = (byte)keep.PartCount;
        var joint = keep.JointCount;
        var seg = (byte)(joint + 1);
        var momentum = keep.Vel * keep.Mass + mover.Vel * mover.Mass;
        var mass = keep.Mass + mover.Mass;
        keep.Joints[keep.JointCount++] = new CJoint
        {
            Type = JointType.Fixed,
            Hinge = (byte)anchor,
            Child = (byte)(welded + off),
            ParentSeg = parent,
            FixRot = fixRot,
            FixPos = fixPos,
            Axis = Vector3.UnitY,
        };
        for (var j = 0; j < mover.JointCount; j++)
        {
            var jt = mover.Joints[j];
            jt.Hinge = (byte)(jt.Hinge + off);
            if (jt.Freeform)
                jt.Child = (byte)(jt.Child + off);
            jt.ParentSeg = (byte)(jt.ParentSeg + seg);
            keep.Joints[keep.JointCount++] = jt;
        }
        for (var i = 0; i < mover.PartCount; i++)
        {
            var p = mover.Parts[i];
            p.Seg = (byte)(p.Seg + seg);
            keep.Parts[keep.PartCount++] = p;
            mover.Parts[i].Store = 0; // (physical storage: the store moves with its part)
        }
        for (var l = 0; l < mover.LinkCount; l++)
        {
            var link = mover.Links[l];
            link.A = (byte)(link.A + off);
            link.B = (byte)(link.B + off);
            keep.Links[keep.LinkCount++] = link;
        }
        ConstructRoutes.Merge(keep, mover, off, Vector3.Zero, 0); // (routed links: their nodes stay in their hosts' frames)
        for (var k = XWireCount - 1; k >= 0; k--)
        {
            ref var x = ref XWires[k];
            var km = x.BodyFrom == keep.Id && x.BodyTo == mover.Id;
            var mk = x.BodyFrom == mover.Id && x.BodyTo == keep.Id;
            if (!km && !mk)
                continue;
            var from = km ? x.From : x.From + off;
            var to = km ? x.To + off : x.To;
            if (from < keep.PartCount && to < keep.PartCount)
                ConstructRoutes.Add(this, keep, RouteType.Data, from, 255, 255, 0, default, default, to, 255);
            XWires[k] = XWires[--XWireCount];
            LinksDirty = true;
        }
        keep.AddFuel(mover.Fuel);
        keep.Charge += mover.Charge;
        keep.Damaged |= mover.Damaged;
        keep.Wreck = false;
        var from2 = mover.Id;
        Remove(mover);
        keep.Dirty = true;
        keep.Rebuild();
        keep.Vel = mass > 0 ? momentum / mass : keep.Vel;
        keep.Wake();
        StructureDirty.Add(keep.Id);
        Events.Add($"construct {from2} freeform-welded onto construct {keep.Id} (weld {joint}, {keep.PartCount} parts)");
        return joint;
    }

    /// <summary>
    /// The welder cuts freeform weld <paramref name="joint"/> of <paramref name="c"/>: what it held falls away as a body of
    /// its own where it is (moving as it moved). Returns that body (null when nothing came off).
    /// </summary>
    public Construct? FreeformCut(Construct c, int joint)
    {
        if (joint < 0 || joint >= c.JointCount || !c.Joints[joint].Freeform)
            return null;
        c.Joints[joint].Cut = true;
        var piece = Resplit(c, out var _);
        if (piece != null)
        {
            piece.Wreck = false; // (cut free, not broken off)
            piece.Damaged = c.Damaged;
            piece.Owner = c.Owner;
            StructureDirty.Add(piece.Id);
        }
        return piece;
    }

    /// <summary>
    /// A blueprint's weld step: part <paramref name="def"/> as a new freeform segment on part <paramref name="anchor"/>,
    /// its own frame at <paramref name="pos"/> / <paramref name="rot"/> in the anchor's segment frame. Its index, or -1.
    /// </summary>
    public int AddFreeform(Construct c, int anchor, ConstructPartDef def, Vector3 pos, Quaternion rot)
    {
        if (c.JointCount >= Construct.MaxJoints || TotalJoints >= JointCap)
            return -1;
        var n = 0;
        for (var j = 0; j < c.JointCount; j++)
            if (c.Joints[j].Freeform)
                n++;
        if (n >= Freeform.MaxPerConstruct)
            return -1;
        var seg = (byte)(c.JointCount + 1);
        var i = AddPart(c, def, Vector3.Zero, 0, seg);
        if (i < 0)
            return -1;
        c.Joints[c.JointCount++] = new CJoint
        {
            Type = JointType.Fixed,
            Hinge = (byte)anchor,
            Child = (byte)i,
            ParentSeg = c.Parts[anchor].Seg,
            FixRot = Quaternion.Normalize(rot),
            FixPos = pos,
            Axis = Vector3.UnitY,
        };
        c.Dirty = true;
        return i;
    }

    /// <summary>Splits off every piece of <paramref name="c"/> no longer joined to its root (a cut weld) as bodies of their own; the first one, and how many.</summary>
    private Construct? Resplit(Construct c, out int pieces)
    {
        Span<int> comp = stackalloc int[Construct.MaxParts];
        for (var i = 0; i < c.PartCount; i++)
            comp[i] = -1;
        var comps = 0;
        if (c.PartCount > 0)
            Flood(c, c.Root >= 0 && c.Root < c.PartCount ? c.Root : 0, comps++, comp);
        for (var i = 0; i < c.PartCount; i++)
            if (comp[i] == -1)
                Flood(c, i, comps++, comp);
        Construct? first = null;
        for (var k = 1; k < comps; k++)
            first ??= SplitOff(c, comp, k);
        pieces = comps - 1;
        Compact(c, comp, 0);
        c.Dirty = true;
        c.Wake();
        StructureDirty.Add(c.Id);
        return first;
    }
}
