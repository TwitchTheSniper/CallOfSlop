using System.Numerics;
using CallOfSlop.Space;

namespace CallOfSlop;

/// <summary>
/// The Board order (Karl, 2026-11-17; docs/sandbox/constructs/limpet.md, docs/space/COMMAND.md "Boarding"): in the command
/// view the captain of a ship with a boarding pod docked on it (a craft with hull grapples and a breaching cutter: the Zero
/// Day's Limpet) right-clicks a door or a hull panel of another ship and picks Board. The dock lets the pod go and the host
/// flies it, nobody at its controls: out to a spot a few metres off the target's skin, turned nose-in, then slowly in until its
/// mag locks reach the hull, when they take hold (<see cref="ConstructBreach.Grip"/>: from then on it rides on that ship). On a
/// door it seals round it and both doors open; on the hull the cutter burns through and then the pod's door opens.
/// <para>
/// The pod is flown as a physics body (its speed and turn steered straight, as a tractor would, its RCS for show), not as a
/// space ship: it stays a body of the bubble while the order lasts (GameSpaceLaunch.cs leaves it be), so it boards in space as
/// in a sky. The order is the pod's (its card shows it); the wire is the nav order's as ever, Point holding the target's id,
/// the part and 1 for a door.
/// </para>
/// </summary>
public sealed partial class Game
{
    public const float BoardRange = 3000; // m from the carrier to the target: further than that the pod isn't sent
    public const float BoardStandoff = 4; // m off the skin where it lines up before going in
    public const float BoardSpeed = 14; // m/s at most on the way
    public const float BoardInSpeed = 0.8f; // m/s going in the last metres
    public const float BoardAccel = 12; // m/s² (above a world's gravity, so it holds against it)
    public const float BoardTurn = 1.2f; // rad/s at most
    public const int BoardDropTicks = 90; // ticks it drops clear of its dock first, at most
    public const float BoardDropDown = 0.3f; // m it drops out of its dock (the Zero Day's Limpet has 0.8 m under it on the ground)
    public const float BoardClimb = 12; // m at most it climbs over the straight way, so it doesn't drag along the ground
    public const float BoardTouch = 0.15f; // m: a mag lock this near the hull is on it (the skirt's gasket stands a little proud of the locks)
    public const float BoardDropSpeed = 2.5f; // m/s at most out of its dock
    public const int BoardFlyTicks = 120 * World.TickRate; // it gives up past this on its way
    public const int BoardCutTicks = 180 * World.TickRate; // and past this cutting

    /// <summary>The boarding pod docked on <paramref name="carrier"/> (any peer: the copy's carry is told), or null.</summary>
    public static Construct? BoardPodOf(ConstructWorld world, Construct carrier)
    {
        foreach (var b in world.Bodies)
            if (b.CarriedBy == carrier.Id && b.CarryTag != 0 && ConstructBreach.IsPod(b) && NoseCutter(b))
                return b; // (only a pod that cuts with its nose, the Limpet: a craft that grips and cuts through its floor isn't flown nose-in)
        return null;
    }

    /// <summary>Does <paramref name="b"/>'s cutter face forward (its face, the part's -y, along the body's -z)?</summary>
    private static bool NoseCutter(Construct b)
    {
        for (var i = 0; i < b.PartCount; i++)
            if (b.DefOf(i).Breach == BreachKind.Cutter && Rot24.Apply(b.Parts[i].Rot, -Vector3.UnitY).Z < -0.9f)
                return true;
        return false;
    }

    /// <summary>Is <paramref name="part"/> of <paramref name="c"/> an event door (a pod seals on it rather than cuts)?</summary>
    public static bool BoardIsDoor(Construct c, int part) => part >= 0 && part < c.PartCount && c.DoorOf(part) >= 0;

    /// <summary>Host: a player's Board order for <paramref name="carrier"/> (Point: the target's id, its part, 1 for a door). Null, or why not.</summary>
    private string? NavBoardAct(SpaceSession s, Construct carrier, Vec3d point, Player p)
    {
        if (!SandboxOn)
            return null;
        if (NavProblem(carrier) is { } why)
            return "It takes no orders: " + why;
        if (BoardPodOf(Constructs, carrier) is not { } pod)
            return "It has no boarding pod docked";
        var tid = (ushort)Math.Clamp(point.X, 0, ushort.MaxValue);
        var part = (int)Math.Clamp(point.Y, -1, ushort.MaxValue);
        if (Constructs.Get(tid) is not { } t || t == carrier || t == pod || t.PartCount == 0)
            return "That ship is gone";
        if (t.CarriedBy != 0)
            return "That ship is docked in another: board the one that holds it";
        if (t.SpaceAway != 0 || carrier.SpaceAway != 0)
            return "That ship is too far off";
        if (Vector3.Distance(t.WorldCom, carrier.WorldCom) > BoardRange)
            return FormattableString.Invariant($"That ship is over {BoardRange / 1000:0} km off: the pod can't reach it");
        if (part < 0 || part >= t.PartCount)
            part = 0;
        if (!BoardSpot(t, part, !(point.Z >= 0.5 && BoardIsDoor(t, part)), out var seg, out var spot, out var outward, out var up, out var hitPart))
            return "There is no hull there to board by";
        var door = point.Z >= 0.5 && BoardIsDoor(t, part) ? part : BoardIsDoor(t, hitPart) && point.Z >= 0.5 ? hitPart : -1;
        var o = NavOf(pod.Id);
        if (o == null)
        {
            if (NavOrders.Count >= MaxNavOrders)
                return "Too many ships are under orders";
            NavOrders.Add(o = new NavOrder { Construct = pod.Id });
        }
        o.Kind = NavKind.Board;
        o.Body = (byte)Math.Max(0, s.Bubble.Body);
        o.Point = new Vec3d(t.Id, door >= 0 ? door : hitPart, door >= 0 ? 1 : 0);
        o.Radius = 0;
        o.By = p.Id;
        o.Phase = NavPhase.None;
        o.PhaseSent = NavPhase.None;
        o.Engaged = false;
        o.BoardTarget = t.Id;
        o.BoardSeg = seg;
        o.BoardSpot = spot;
        o.BoardOut = outward;
        o.BoardUp = up;
        o.BoardDoor = door;
        o.BoardTicks = o.BoardAll = 0;
        o.BoardSide = 0;
        o.BoardDropFor = BoardDropTicks;
        // Where it drops to first (half a metre down out of its dock, in the carrier's frame), and its turn in that frame,
        // held until it is out from under the carrier.
        if (BoardPart(pod, BreachKind.Cutter) is var cut and >= 0)
        {
            ConstructBreach.Face(pod, cut, out var nose0, out _);
            var cinv = Quaternion.Conjugate(carrier.Rot);
            o.BoardFrom = carrier.Id;
            var noseIn = Vector3.Transform(nose0 - carrier.Pos, cinv);
            var drop = BoardDropDown;
            if (carrier.SpaceShip != 0)
            {
                // In space there is no ground under it: it drops clear of the carrier's lowest point (its legs' feet) first, its roof
                // a metre under them, and goes straight from there (Karl, 2026-11-18: it fires away and clears the ship first).
                var roof = Vector3.Transform(pod.WorldCom - nose0, cinv).Y + (pod.LocalMax.Y - pod.Com.Y);
                drop = MathF.Max(drop, noseIn.Y + roof - carrier.LocalMin.Y + 1f);
            }
            o.BoardDrop = noseIn - Vector3.UnitY * drop;
            o.BoardDropFor = BoardDropTicks + (int)(drop / BoardDropSpeed * World.TickRate);
            o.BoardRot = Quaternion.Normalize(cinv * pod.Rot);
        }
        ConstructCarrier.LetGo(Constructs, pod);
        pod.Vel = carrier.PointVel(pod.WorldCom);
        Console.WriteLine($"[nav] {p.Name} sent construct {carrier.Id}'s boarding pod {pod.Id} to {(door >= 0 ? "the door" : "the hull")} of construct {t.Id} (its {t.DefOf(door >= 0 ? door : hitPart).Name})");
        return null;
    }

    /// <summary>
    /// Where a pod sets its nose on <paramref name="t"/> by part <paramref name="part"/>: the way out of the skin there (a plate's
    /// or a door's thin side, else the side of the body it lies furthest towards), then a ray in from outside along it through the
    /// part's middle finds the skin. In the frame of the part's segment, so the spot moves with that segment.
    /// </summary>
    private bool BoardSpot(Construct t, int part, bool flat, out byte seg, out Vector3 spot, out Vector3 outward, out Vector3 up, out int hitPart)
    {
        if (t.Dirty)
            t.Rebuild();
        seg = t.Parts[part].Seg;
        t.WorldFrame(seg, out var q, out var tt);
        var inv = Quaternion.Conjugate(q);
        t.PartBounds(part, out var mn, out var mx);
        var mid = (mn + mx) * 0.5f;
        var size = mx - mn;
        var com = Vector3.Transform(t.WorldCom - tt, inv);
        var off = mid - com;
        // The plate's thin side when it is a plate, else the side of the body it is furthest out to.
        int axis;
        var sorted = (Min: MathF.Min(size.X, MathF.Min(size.Y, size.Z)), Mid: size.X + size.Y + size.Z - MathF.Min(size.X, MathF.Min(size.Y, size.Z)) - MathF.Max(size.X, MathF.Max(size.Y, size.Z)));
        if (sorted.Min < 0.5f * sorted.Mid)
            axis = size.X == sorted.Min ? 0 : size.Y == sorted.Min ? 1 : 2;
        else
        {
            var half = Vector3.Max((t.LocalMax - t.LocalMin) * 0.5f, new Vector3(0.1f));
            var rel = Vector3.Abs(off) / half;
            axis = rel.X >= rel.Y && rel.X >= rel.Z ? 0 : rel.Y >= rel.Z ? 1 : 2;
        }
        var n = axis == 0 ? Vector3.UnitX : axis == 1 ? Vector3.UnitY : Vector3.UnitZ;
        if ((axis == 0 ? off.X : axis == 1 ? off.Y : off.Z) < 0)
            n = -n;
        var nw = Vector3.Transform(n, q);
        var from = tt + Vector3.Transform(mid, q) + nw * (t.Radius * 2 + 5);
        hitPart = part;
        Vector3 at;
        if (Constructs.Raycast(from, -nw, t.Radius * 4 + 10, out var hit, only: t))
        {
            at = hit.Point;
            hitPart = hit.Part >= 0 && hit.Part < t.PartCount ? hit.Part : part;
            if (Vector3.Dot(hit.Normal, nw) > 0.5f)
                nw = Vector3.Normalize(hit.Normal); // (the face it struck, when it faces out much as asked)
        }
        else
            at = tt + Vector3.Transform(mid + n * Vector3.Dot(size, Vector3.Abs(n)) * 0.5f, q);
        outward = Vector3.Normalize(Vector3.Transform(nw, inv));
        up = BoardUpOf(outward);
        if (flat)
        {
            // The hull: a patch the whole nose sits flat on, near the spot asked (Karl, 2026-11-18: it met the hull at an angle by
            // its edge, its skirt over the side). The way out first, then the part's other sides, nearest the body's skin first
            // (a pointed bow has no flat tip, but its flanks are).
            Span<Vector3> ways = stackalloc Vector3[5];
            var count = 0;
            ways[count++] = n;
            for (var a = 0; a < 3; a++)
            {
                if (a == axis)
                    continue;
                var e = a == 0 ? Vector3.UnitX : a == 1 ? Vector3.UnitY : Vector3.UnitZ;
                var o = a == 0 ? off.X : a == 1 ? off.Y : off.Z;
                ways[count++] = o >= 0 ? e : -e;
                ways[count++] = o >= 0 ? -e : e;
            }
            var found = false;
            for (var w = 0; w < count && !found; w++)
            {
                var dw = Vector3.Transform(ways[w], q);
                var start = at;
                if (w > 0)
                {
                    if (!Constructs.Raycast(tt + Vector3.Transform(mid, q) + dw * (t.Radius * 2 + 5), -dw, t.Radius * 4 + 10, out var side, only: t))
                        continue;
                    start = side.Point;
                }
                var upw = Vector3.Transform(BoardUpOf(ways[w]), q);
                if (!BoardFlat(t, start, w == 0 ? nw : dw, upw, out var flatAt, out var flatN, out var flatPart))
                    continue;
                at = flatAt;
                hitPart = flatPart;
                outward = Vector3.Normalize(Vector3.Transform(flatN, inv));
                up = BoardUpOf(outward);
                found = true;
            }
            if (!found)
                Console.WriteLine($"[nav] board: no flat patch of construct {t.Id}'s hull within {BoardFlatReach:0} m of its {t.DefOf(part).Name} on any side: the pod goes for the spot itself");
        }
        spot = Vector3.Transform(at - tt, inv);
        return true;
    }

    /// <summary>The pod's up on a face whose way out is <paramref name="outward"/>: the target's own up, square to it (on its top or belly, the target's forward).</summary>
    private static Vector3 BoardUpOf(Vector3 outward)
    {
        var u = Vector3.UnitY - outward * outward.Y;
        if (u.LengthSquared() < 0.04f)
            u = -Vector3.UnitZ - outward * -outward.Z;
        return Vector3.Normalize(u);
    }

    private const float BoardNoseHalfW = 1.35f, BoardNoseHalfH = 1.05f; // m: the Limpet's skirt, half its width and height
    private const float BoardFlatStep = 0.5f, BoardFlatReach = 6f; // m: how far round the spot asked it looks, and how finely
    private const float BoardFlatDepth = 0.3f; // m: how far the skin may stand off flat under the skirt's corners

    /// <summary>
    /// Host, once per Board order: the nearest spot to <paramref name="at"/> (in rings out to <see cref="BoardFlatReach"/>, along the
    /// face) where rays in along <paramref name="nw"/> at the skirt's middle and four corners all strike <paramref name="t"/> within
    /// <see cref="BoardFlatDepth"/> of one another, on faces that look out much the same way. False: nowhere near is flat enough.
    /// </summary>
    private bool BoardFlat(Construct t, Vector3 at, Vector3 nw, Vector3 uw, out Vector3 flatAt, out Vector3 flatN, out int flatPart)
    {
        flatAt = at;
        flatN = nw;
        flatPart = -1;
        var rw = Vector3.Normalize(Vector3.Cross(uw, nw));
        var back = t.Radius * 2 + 5;
        var reach = t.Radius * 4 + 10;
        var rings = (int)(BoardFlatReach / BoardFlatStep);
        for (var ring = 0; ring <= rings; ring++)
            for (var i = -ring; i <= ring; i++)
                for (var j = -ring; j <= ring; j++)
                {
                    if (Math.Max(Math.Abs(i), Math.Abs(j)) != ring)
                        continue; // (the ring's edge only: nearer ones were tried)
                    var c = at + rw * (i * BoardFlatStep) + uw * (j * BoardFlatStep);
                    if (!Constructs.Raycast(c + nw * back, -nw, reach, out var mid, only: t) || Vector3.Dot(mid.Normal, nw) < 0.5f)
                        continue;
                    // The skirt square to the face struck (a sloped bow's face too), its corners cast in along that face's normal.
                    var n0 = Vector3.Normalize(mid.Normal);
                    var u0 = uw - n0 * Vector3.Dot(uw, n0);
                    if (u0.LengthSquared() < 0.01f)
                        continue;
                    u0 = Vector3.Normalize(u0);
                    var r0 = Vector3.Cross(u0, n0);
                    var ok = true;
                    for (var k = 0; k < 4 && ok; k++)
                    {
                        var corner = mid.Point + r0 * ((k & 1) == 0 ? BoardNoseHalfW : -BoardNoseHalfW) + u0 * ((k & 2) == 0 ? BoardNoseHalfH : -BoardNoseHalfH);
                        var hitc = Constructs.Raycast(corner + n0 * 3, -n0, 6, out var h, only: t);
                        ok = hitc && MathF.Abs(h.Distance - 3) < BoardFlatDepth && Vector3.Dot(h.Normal, n0) > 0.9f;
                    }
                    if (!ok)
                        continue;
                    flatAt = mid.Point;
                    flatN = Vector3.Normalize(mid.Normal);
                    flatPart = mid.Part >= 0 && mid.Part < t.PartCount ? mid.Part : -1;
                    if (flatPart < 0)
                        continue;
                    return true;
                }
        return false;
    }

    /// <summary>Host, every tick: a Board order flies its pod, grips, opens or cuts. Allocation-free.</summary>
    private void NavBoardTick(NavOrder o, Construct? pod)
    {
        var t = Constructs.Get(o.BoardTarget);
        if (pod == null || pod.Wreck || !SandboxOn)
        {
            NavEnd(o, pod, "the pod can't fly it any more");
            return;
        }
        if (t == null || t.PartCount == 0)
        {
            NavEnd(o, pod, "the ship it was boarding is gone");
            return;
        }
        if (pod.Dirty)
            pod.Rebuild();
        o.BoardTicks++;
        o.BoardAll++;
        var lit = pod.ThrustPuppet;
        pod.ThrustPuppet = false; // (NavBoardSteer lights them again while it flies)
        NavBoardFly(o, pod, t);
        if (lit && !pod.ThrustPuppet)
            NavBoardJetsOut(pod);
    }

    private void NavBoardFly(NavOrder o, Construct pod, Construct t)
    {
        if (pod.CarriedBy != 0)
        {
            if (pod.CarriedBy != t.Id || o.Phase is not (NavPhase.Arrived or NavPhase.Hold))
            {
                if (o.Phase == NavPhase.None && o.BoardAll < 3)
                    return; // (the dock lets go at its next look)
                NavEnd(o, pod, pod.CarriedBy == t.Id ? "it is on the ship" : "it was caught by another");
                return;
            }
            NavBoardOn(o, pod, t);
            return;
        }
        if (o.Phase is NavPhase.Arrived or NavPhase.Hold)
        {
            NavEnd(o, pod, "its grapples let go");
            return;
        }
        if (o.BoardAll > BoardFlyTicks)
        {
            NavEnd(o, pod, "it couldn't reach the spot");
            return;
        }
        var cutter = BoardPart(pod, BreachKind.Cutter);
        if (cutter < 0)
        {
            NavEnd(o, pod, "it has no cutter on its nose");
            return;
        }
        if (t.Dirty)
            t.Rebuild();
        pod.DockAway = Constructs.CarrierClock + 60; // (no dock catches it again on its way: its own carrier's least of all)
        var seg = o.BoardSeg <= t.JointCount ? o.BoardSeg : 0;
        t.WorldFrame(seg, out var q, out var tt);
        var spot = tt + Vector3.Transform(o.BoardSpot, q);
        var nw = Vector3.Normalize(Vector3.Transform(o.BoardOut, q));
        var uw = Vector3.Normalize(Vector3.Transform(o.BoardUp, q));
        ConstructBreach.Face(pod, cutter, out var nose, out _);
        Vector3 aim;
        float most;
        var goal = Quaternion.CreateFromRotationMatrix(Matrix4x4.CreateWorld(Vector3.Zero, -nw, uw)); // (nose in along the way out of the skin, roof up the target's way)
        var frame = t;
        var from = Constructs.Get(o.BoardFrom);
        var standoff = spot + nw * BoardStandoff;
        if (o.Phase == NavPhase.None && from != null)
        {
            // First it drops out of its dock, as it hung there.
            aim = from.Pos + Vector3.Transform(o.BoardDrop, from.Rot);
            most = BoardDropSpeed;
            frame = from;
            goal = Quaternion.Normalize(from.Rot * o.BoardRot);
            if (o.BoardTicks >= o.BoardDropFor || Vector3.Distance(nose, aim) < 0.12f)
                NavBoardStage(o, NavPhase.Transfer);
        }
        else if (o.Phase is NavPhase.None or NavPhase.Transfer)
        {
            if (o.Phase == NavPhase.None)
                NavBoardStage(o, NavPhase.Transfer);
            var up = GravityFrames.UpAt(nose);
            if (from != null && Under(from, pod.WorldCom, standoff))
            {
                // Out from under its carrier first, level and sideways (between its legs, fore and aft of the pod), to the target's
                // side, still turned as it hung.
                var side = Vector3.Transform(Vector3.UnitX, from.Rot);
                if (o.BoardSide == 0)
                {
                    // Chosen once: the target's side, or (a target fore or aft, along the carrier) the side the pod is already on.
                    // Picked each tick, a target dead astern flipped it from side to side and it never left.
                    var way = Vector3.Dot(standoff - from.WorldCom, side);
                    if (MathF.Abs(way) < 2)
                        way = Vector3.Dot(pod.WorldCom - from.WorldCom, side) + 1e-3f;
                    o.BoardSide = (sbyte)(way >= 0 ? 1 : -1);
                }
                aim = nose + side * (o.BoardSide * 4);
                most = 4;
                frame = from;
                goal = Quaternion.Normalize(from.Rot * o.BoardRot);
            }
            else if (from != null && from.SpaceShip != 0 && InPlan(from, pod.WorldCom))
            {
                // Dropped clear under its carrier in space: out from under it at that depth, level in the carrier's frame (a straight
                // way to a target astern rose into its aft legs), still turned as it hung.
                var cup = Vector3.Transform(Vector3.UnitY, from.Rot);
                var way = standoff - nose;
                way -= cup * Vector3.Dot(way, cup);
                aim = nose + (way.LengthSquared() > 1e-4f ? Vector3.Normalize(way) : Vector3.Transform(Vector3.UnitX, from.Rot)) * 4;
                most = 6;
                frame = from;
                goal = Quaternion.Normalize(from.Rot * o.BoardRot);
            }
            else
            {
                // Then over to the spot off the skin (climbing a little over the straight way while far), turning nose-in.
                var gap = standoff - nose;
                var level = (gap - up * Vector3.Dot(gap, up)).Length();
                aim = t.SpaceShip != 0 ? standoff : standoff + up * MathF.Min(BoardClimb, 0.3f * MathF.Max(0, level - 6)); // (in space nothing to drag on)
                most = BoardSpeed;
            }
            if (Vector3.Distance(nose, standoff) < 0.6f && NavBoardTurnLeft(pod, goal) < 0.1f && (pod.Vel - t.PointVel(pod.WorldCom)).Length() < 1f)
                NavBoardStage(o, NavPhase.Insert);
        }
        else
        {
            aim = spot - nw * 0.4f; // (in past the skin: it presses on until the locks take hold, or the hull stops it)
            most = BoardInSpeed;
            var locks = NavBoardLocksReach(pod, t, out var gripPart, out var gap);
            if (gap > BoardTouch)
                locks = 0; // (in reach, but not yet on it: it closes until its skirt is on the hull, so the seal is whole, Karl 2026-11-18)
            if (locks < 2 && o.BoardTicks > (int)((BoardStandoff / BoardInSpeed + 3) * World.TickRate) && (NavBoardLocksReach(pod, t, out gripPart, out _) >= 1 || NavBoardNoseOn(pod, cutter, t, out gripPart)))
                locks = 2; // (pressed on a while past its time to close in: one lock, or the nose itself, on the hull is enough)
            if (locks >= 2 || o.BoardTicks > 20 * World.TickRate)
            {
                if (gripPart < 0)
                {
                    NavEnd(o, pod, "its locks found no hull to hold");
                    return;
                }
                BoardFeed(pod, BreachKind.Grapple, true);
                pod.Vel = t.PointVel(pod.WorldCom);
                pod.AngVel = t.AngVel;
                ConstructBreach.Grip(Constructs, pod, t, gripPart);
                pod.BreachSealed = true;
                pod.DockAway = 0;
                NavBoardStage(o, NavPhase.Arrived);
                return;
            }
        }
        o.Distance = Vector3.Distance(nose, aim);
        o.SpeedError = (pod.Vel - t.PointVel(pod.WorldCom)).Length();
        NavBoardSteer(pod, frame, nose, aim, most, goal);
    }

    /// <summary>Is <paramref name="at"/> within the carrier's plan (its outline seen from above, in its own frame, and a margin)?</summary>
    private static bool InPlan(Construct carrier, Vector3 at)
    {
        var local = Vector3.Transform(at - carrier.WorldCom, Quaternion.Conjugate(carrier.Rot));
        var half = (carrier.LocalMax - carrier.LocalMin) * 0.5f + new Vector3(1.5f);
        var mid = (carrier.LocalMax + carrier.LocalMin) * 0.5f - carrier.Com;
        return MathF.Abs(local.X - mid.X) < half.X && MathF.Abs(local.Z - mid.Z) < half.Z;
    }

    /// <summary>Is <paramref name="at"/> still under (or over, or in) the carrier, its way out not yet clear: within its plan, short of the target?</summary>
    private static bool Under(Construct carrier, Vector3 at, Vector3 target)
    {
        var local = Vector3.Transform(at - carrier.WorldCom, Quaternion.Conjugate(carrier.Rot));
        var half = (carrier.LocalMax - carrier.LocalMin) * 0.5f + new Vector3(1.5f);
        var mid = (carrier.LocalMax + carrier.LocalMin) * 0.5f - carrier.Com;
        var inPlan = MathF.Abs(local.X - mid.X) < half.X && MathF.Abs(local.Z - mid.Z) < half.Z
                     && local.Y > carrier.LocalMin.Y - carrier.Com.Y - 1.5f; // (dropped clear under its lowest point: out from under it)
        var targetLocal = Vector3.Transform(target - carrier.WorldCom, Quaternion.Conjugate(carrier.Rot));
        var targetInPlan = MathF.Abs(targetLocal.X - mid.X) < half.X && MathF.Abs(targetLocal.Z - mid.Z) < half.Z;
        return inPlan && !targetInPlan;
    }

    private static void NavBoardStage(NavOrder o, NavPhase phase)
    {
        o.Phase = phase;
        o.BoardTicks = 0;
    }

    /// <summary>The pod's speed and turn, steered straight (no faster than <see cref="BoardAccel"/> and <see cref="BoardTurn"/>).</summary>
    private static void NavBoardSteer(Construct pod, Construct t, Vector3 nose, Vector3 aim, float most, Quaternion goal)
    {
        var dt = World.TickDt;
        var to = aim - nose;
        var d = to.Length();
        var speed = MathF.Min(most, MathF.Sqrt(2 * BoardAccel * 0.5f * d)); // (half its push to brake with: it doesn't overshoot)
        var want = t.PointVel(pod.WorldCom) + (d > 1e-3f ? to / d * speed : Vector3.Zero);
        var dv = want - pod.Vel;
        var step = BoardAccel * dt;
        if (dv.LengthSquared() > step * step)
            dv = Vector3.Normalize(dv) * step;
        pod.Vel += dv - ConstructGravity.GravityFor(pod, pod.WorldCom) * dt; // (its jets hold it up: the step's gravity taken back before it is added)
        pod.SpeedKept = MathF.Max(pod.SpeedKept, MathF.Max(want.Length(), pod.Vel.Length()) + 2 * BoardAccel * dt); // (in orbit its frame's 200 m/s and more is past the physics' limit for a free body: it keeps what it is steered to, Construct.SpeedKept)
        var e = Quaternion.Normalize(goal * Quaternion.Conjugate(pod.Rot));
        if (e.W < 0)
            e = -e;
        var s = MathF.Sqrt(MathF.Max(0, 1 - e.W * e.W));
        var angle = 2 * MathF.Acos(Math.Clamp(e.W, -1, 1));
        var spin = s > 1e-4f ? new Vector3(e.X, e.Y, e.Z) / s * MathF.Min(angle * 1.5f, BoardTurn) : Vector3.Zero;
        var dw = t.AngVel + spin - pod.AngVel;
        var turnStep = 3f * dt;
        if (dw.LengthSquared() > turnStep * turnStep)
            dw = Vector3.Normalize(dw) * turnStep;
        pod.AngVel += dw;
        pod.Asleep = false;
        pod.SleepTicks = 0;
        NavBoardJets(pod, dv / dt - ConstructGravity.GravityFor(pod, pod.WorldCom), dw / dt); // (what its jets give it: the change asked, and holding it up)
    }

    /// <summary>
    /// Its RCS jets lit as it is steered (Karl, 2026-11-18: it fires away from the carrier to clear it): each thruster burns by how
    /// much its push lies along the acceleration it is given (and the turn), for its flame and the clients' (the thrust step
    /// pushes nothing while <see cref="Construct.ThrustPuppet"/> is set: the steering moves it).
    /// </summary>
    private static void NavBoardJets(Construct pod, Vector3 accel, Vector3 turn)
    {
        pod.ThrustPuppet = true;
        var com = pod.WorldCom;
        var rate = World.TickDt / 0.12f; // (a quick spool, as an RCS jet's)
        for (var k = 0; k < pod.ThrusterCount; k++)
        {
            ref var t = ref pod.Thrusters[k];
            pod.WorldFrame(t.Seg, out var q, out var o);
            var dir = Vector3.Transform(t.Dir, q);
            var arm = o + Vector3.Transform(t.At, q) - com;
            var want = Vector3.Dot(dir, accel) / (BoardAccel * 0.35f) + Vector3.Dot(Vector3.Cross(arm, dir), turn) / 1.5f;
            want = Math.Clamp(want, 0, 1);
            if (want < 0.08f)
                want = 0; // (no flicker of jets with almost nothing to do)
            t.Level = Math.Clamp(t.Level + Math.Clamp(want - t.Level, -2 * rate, rate), 0, 1);
        }
    }

    /// <summary>Its jets out when the order stops flying it (it gripped, or the order ended).</summary>
    private static void NavBoardJetsOut(Construct pod)
    {
        pod.ThrustPuppet = false;
        for (var k = 0; k < pod.ThrusterCount; k++)
            pod.Thrusters[k].Level = 0;
    }

    /// <summary>How far (radians) the pod has still to turn to face in.</summary>
    private static float NavBoardTurnLeft(Construct pod, Quaternion goal)
    {
        var dot = MathF.Abs(Quaternion.Dot(goal, pod.Rot));
        return 2 * MathF.Acos(MathF.Min(1, dot));
    }

    /// <summary>Does a mag lock of the pod reach the target's hull (the part it would hold by, or -1)? The nearest lock's hit.</summary>
    private int NavBoardLocksReach(Construct pod, Construct t, out int part, out float best)
    {
        part = -1;
        best = float.MaxValue;
        var n = 0;
        for (var i = 0; i < pod.PartCount; i++)
        {
            if (pod.DefOf(i).Breach != BreachKind.Grapple || pod.Parts[i].Broken)
                continue;
            ConstructBreach.Face(pod, i, out var at, out var down);
            if (Constructs.Raycast(at, down, pod.DefOf(i).BreachReach, out var hit, pod, only: t))
            {
                n++;
                if (hit.Distance < best)
                {
                    best = hit.Distance;
                    part = hit.Part;
                }
            }
        }
        return n; // (two locks on the hull: it sits square enough to seal; best: how far the nearest is off it)
    }

    /// <summary>Is the cutter's face on the target's hull (within half a metre), and on which part?</summary>
    private bool NavBoardNoseOn(Construct pod, int cutter, Construct t, out int part)
    {
        part = -1;
        ConstructBreach.Face(pod, cutter, out var at, out var down);
        if (!Constructs.Raycast(at - down * 0.1f, down, 0.6f, out var hit, pod, only: t))
            return false;
        part = hit.Part;
        return part >= 0;
    }

    /// <summary>On the ship: a door opens on both sides and it is done; on the hull it cuts, and once through its door opens.</summary>
    private void NavBoardOn(NavOrder o, Construct pod, Construct t)
    {
        if (o.Phase == NavPhase.Arrived)
        {
            if (t.Dirty)
                t.Rebuild(); // (it took on the pod's weight: its door list waits for a rebuild)
            if (o.BoardDoor >= 0 && o.BoardDoor < t.PartCount && BoardIsDoor(t, o.BoardDoor))
            {
                var why = ConstructDoors.Use(Constructs, t, o.BoardDoor) is { Length: > 0 } no ? no : null;
                if (why == null && o.BoardDoor < t.PartCount && t.DoorOf(o.BoardDoor) is var k and >= 0)
                    ConstructDoors.Command(Constructs, t, k, true); // (Use toggles: it is open whichever way it stood)
                NavBoardOpen(pod);
                NavEnd(o, pod, why == null ? "it sealed on the door and opened it" : "it sealed on the door, but " + why);
                return;
            }
            BoardFeed(pod, BreachKind.Cutter, true);
            NavBoardStage(o, NavPhase.Hold);
            return;
        }
        if (ConstructBreach.CutThrough(pod) && o.BoardTicks > ConstructBreach.StepEvery * 2)
        {
            NavBoardOpen(pod); // (the cutter stays on, wedged in its hole: it cuts no deeper)
            NavEnd(o, pod, "it cut through the hull");
            return;
        }
        if (o.BoardTicks > BoardCutTicks)
            NavEnd(o, pod, "its cutter couldn't get through");
    }

    /// <summary>The pod's own door opens (its switch, and the leaves straight away so it opens whatever the wiring).</summary>
    private void NavBoardOpen(Construct pod)
    {
        if (!pod.EnsureDoors())
            return;
        for (var i = 0; i < pod.PartCount; i++)
            if (pod.DoorOf(i) is var k and >= 0 && pod.Doors[k].Part == i)
            {
                BoardFeedPart(pod, i, true);
                ConstructDoors.Command(Constructs, pod, k, true);
            }
    }

    /// <summary>The first unbroken part of a breach kind (-1: none).</summary>
    private static int BoardPart(Construct pod, BreachKind kind)
    {
        for (var i = 0; i < pod.PartCount; i++)
            if (pod.DefOf(i).Breach == kind && !pod.Parts[i].Broken)
                return i;
        return -1;
    }

    /// <summary>Every switch wired into the pod's parts of a kind set on or off (as a hand on it would: it stays so).</summary>
    private static void BoardFeed(Construct pod, BreachKind kind, bool on)
    {
        for (var i = 0; i < pod.PartCount; i++)
            if (pod.DefOf(i).Breach == kind)
            {
                BoardFeedPart(pod, i, on);
                pod.Parts[i].Out = on ? 1 : 0; // (on now, not at the logic's next tick: a grip checks its grapples straight away)
            }
    }

    private static void BoardFeedPart(Construct pod, int part, bool on)
    {
        for (var w = 0; w < pod.WireCount; w++)
        {
            ref var wire = ref pod.Wires[w];
            if (wire.To != part || wire.From >= pod.PartCount || pod.DefOf(wire.From).Logic != CLogic.Switch)
                continue;
            ref var sw = ref pod.Parts[wire.From];
            sw.State = (byte)(on ? sw.State | 1 : sw.State & ~1);
            sw.Out = on ? 1 : 0; // (its output now: what it feeds reads it this tick)
        }
        pod.Wake();
    }

    /// <summary>Is <paramref name="c"/> a pod being flown to board (it stays a body of the bubble: GameSpaceLaunch.cs)?</summary>
    internal bool NavBoarding(Construct c)
    {
        if (NavOrders.Count == 0)
            return false;
        foreach (var o in NavOrders)
            if (o.Construct == c.Id && o.Kind == NavKind.Board)
                return true;
        return false;
    }
}
