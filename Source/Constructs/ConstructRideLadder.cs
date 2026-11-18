using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Vertical ladders (a part with "ladder": its landing's height in its own frame, up +y, climbed from its front, -z). F at
/// its foot or at its top grabs it and plays the climb: a short step onto the rungs, the climb up or down them facing the
/// ladder, and a step off onto the floor at the other end. The three legs run one after another in the construct's frame
/// like any climb (<see cref="MoveState.RideClimb"/>: a moving ship carries the climber, and the server, the client's
/// prediction and its replays step them the same way). The end is worked out when the climb starts: the floor behind the
/// top (or in front of the foot), or, when something stands there, the nearest spot a body fits with a floor under it, so
/// a fitting moved onto a landing no longer stops the ladder. The body, its hands and its feet on the rungs are drawn
/// from where the feet are on the ladder (Source/Render/LadderAnim.cs).
/// </summary>
public static partial class ConstructRide
{
    /// <summary>A vertical ladder's climb: metres a second up or down its rungs.</summary>
    public const float LadderSpeed = 2.0f;

    /// <summary>Stepping onto and off a ladder: metres a second.</summary>
    public const float LadderStepSpeed = 1.6f;

    /// <summary>The feet on a ladder: this far in front of its front face (m).</summary>
    public const float LadderBack = 0.36f;

    /// <summary><see cref="MoveState.RideLadder"/>: on a ladder; going down it; the legs still to come after this one (low 2 bits).</summary>
    public const byte LadderOn = 0x80, LadderDown = 0x40, LadderLegs = 3;

    /// <summary>How far round a blocked end the climb looks for a spot to stand (m).</summary>
    public const float LadderExitReach = 1.4f;

    private static int[]? _ladderHits;

    /// <summary>A ladder's feet line, in its own frame: the climber's feet at its foot and at its landing, facing it.</summary>
    public static void LadderLine(ConstructPartDef d, out Vector3 low, out Vector3 high)
    {
        var cx = (d.BoundsMin.X + d.BoundsMax.X) * 0.5f;
        low = new Vector3(cx, d.BoundsMin.Y + 0.02f, d.BoundsMin.Z - LadderBack);
        high = new Vector3(cx, d.LadderTop + 0.02f, d.BoundsMin.Z - LadderBack);
    }

    /// <summary>A ladder's ends as the player stands to use it, in its own frame: in front of the foot, and on the landing behind the top.</summary>
    public static void LadderEnds(ConstructPartDef d, out Vector3 foot, out Vector3 top)
    {
        var cx = (d.BoundsMin.X + d.BoundsMax.X) * 0.5f;
        var stand = World.PlayerRadius + 0.12f;
        foot = new Vector3(cx, d.BoundsMin.Y + 0.02f, d.BoundsMin.Z - stand);
        top = new Vector3(cx, d.LadderTop + 0.02f, d.BoundsMax.Z + stand);
    }

    /// <summary>
    /// F on a vertical ladder: from near its foot, climbs it to the landing; from up there, climbs down to its foot. Returns
    /// null when the climb started, else why not (too far from either end, a stowed boarding ladder, already climbing).
    /// </summary>
    public static string? ClimbLadder(ref MoveState s, Construct c, int part, float height, bool frame = false)
    {
        if (s.RideClimb > 0)
            return "already climbing";
        if (part < 0 || part >= c.PartCount)
            return "nothing to climb there";
        if (c.Dirty)
            c.Rebuild();
        ref var p = ref c.Parts[part];
        var d = p.D;
        if (d.LadderTop <= 0 || p.Broken)
            return "nothing to climb there";
        if (StowedFoot(c, part))
            return "the ladder is stowed: F on its foot rolls it down";
        c.WorldFrame(p.Seg, out var q, out var t);
        var pos = p.Pos;
        var rot = p.Rot;
        var lq = q * Rot24.Quat(rot); // (the ladder's own frame in the world)
        Vector3 ToWorld(Vector3 local) => t + Vector3.Transform(pos + Rot24.Apply(rot, local), q);
        var feetLocal = Vector3.Transform(Vector3.Transform(s.Position - t, Quaternion.Conjugate(q)) - pos, Quaternion.Conjugate(Rot24.Quat(rot))); // (the feet in the ladder's own frame)
        LadderEnds(d, out var foot, out var top);
        LadderLine(d, out var low, out var high);
        var atTop = feetLocal.Y > (foot.Y + top.Y) * 0.5f;
        var from = atTop ? top : foot;
        var across = new Vector2(feetLocal.X - from.X, feetLocal.Z - from.Z).Length();
        if (across > GrabReach + World.PlayerRadius || MathF.Abs(feetLocal.Y - from.Y) > 1.2f)
            return "get to the ladder's foot or its top first";
        var up = Vector3.Normalize(Vector3.Transform(Vector3.UnitY, lq));
        var onEnd = ToWorld(atTop ? low : high); // (where the climb leaves the rungs)
        var exit = LadderExit(c, part, ToWorld(atTop ? foot : top), onEnd, lq, up, atTop ? -1f : 1f, height);
        StartClimb(ref s, c, p.Seg, ToWorld(atTop ? high : low), 1, frame);
        var seg = s.RideSeg;
        var on = s.RideClimbTo;
        s.RideClimb = LegTicks(Vector3.Distance(s.RideLocal, on), LadderStepSpeed);
        s.RideClimbNext = ToLocal(c, seg, onEnd);
        s.RideClimbLast = ToLocal(c, seg, exit);
        s.RideLadder = (byte)(LadderOn | (atTop ? LadderDown : 0) | 2);
        s.RideLadderPart = (ushort)part;
        return null;
    }

    /// <summary>Ticks a leg of a ladder's climb takes at <paramref name="speed"/> (a long ladder is climbed a little faster: a leg fits a byte).</summary>
    public static byte LegTicks(float dist, float speed) => (byte)Math.Clamp((int)MathF.Ceiling(dist / speed * World.TickRate), 6, 250);

    /// <summary>
    /// A climb's leg just ended (<see cref="MoveState.RideClimb"/> reached 0): on a ladder, the next leg starts at once (on,
    /// the climb, off); after the last, the ladder lets go. Every machine steps it the same way from the same state.
    /// </summary>
    public static void NextLeg(ref MoveState s)
    {
        if (s.RideClimb > 0 || s.RideLadder == 0)
            return;
        if ((s.RideLadder & LadderLegs) == 0)
        {
            s.RideLadder = 0; // (off the ladder, on the floor at its other end)
            return;
        }
        s.RideLadder--;
        s.RideClimbTo = s.RideClimbNext;
        s.RideClimbNext = s.RideClimbLast;
        var climb = (s.RideLadder & LadderLegs) == 1;
        s.RideClimb = LegTicks(Vector3.Distance(s.RideLocal, s.RideClimbTo), climb ? LadderSpeed : LadderStepSpeed);
    }

    /// <summary>
    /// Where a ladder's climb ends (world feet): <paramref name="nominal"/> (behind its top, or in front of its foot) when a
    /// body fits there with a floor under it; else the nearest spot within <see cref="LadderExitReach"/> that does, not back
    /// through the ladder and with nothing solid between it and the rungs. Nothing fits: the nominal spot all the same (the
    /// movement step pushes a body out of what it stands in), with a line in the construct log.
    /// </summary>
    public static Vector3 LadderExit(Construct c, int ladder, Vector3 nominal, Vector3 onEnd, Quaternion lq, Vector3 up, float away, float height)
    {
        var hits = _ladderHits ??= new int[Construct.MaxParts]; // (a climb's start, not every tick: one scratch for them all)
        var right = Vector3.Transform(Vector3.UnitX, lq);
        var back = Vector3.Transform(Vector3.UnitZ, lq) * away; // (away from the ladder, on the landing's side)
        Vector3? open = null;
        const float step = 0.2f;
        const int n = (int)(LadderExitReach / step);
        // Rings round the nominal spot, nearest first (a few dozen tries at most).
        for (var r = 0; r <= n; r++)
            for (var ix = -r; ix <= r; ix++)
                for (var iz = -r; iz <= r; iz++)
                {
                    if (Math.Max(Math.Abs(ix), Math.Abs(iz)) != r)
                        continue;
                    if (iz < -1)
                        continue; // (not back past the ladder)
                    var at = nominal + right * (ix * step) + back * (iz * step);
                    if (!StandAt(c, ladder, at, up, height, hits, out var feet, out var floored))
                        continue;
                    if (!PathClear(c, ladder, onEnd, feet, up, height, hits))
                        continue;
                    if (floored)
                        return feet;
                    open ??= feet; // (no floor under it: the ground, a drop; taken if nothing better)
                }
        if (open is { } o)
            return o;
        if (Bodies != null)
            Bodies.Events.Add($"construct {c.Id}: ladder #{ladder} has no room at its end: the climb ends at its usual spot");
        return nominal;
    }

    /// <summary>
    /// Could a body stand at <paramref name="at"/> (feet, world)? A floor under it (a solid box's top within a step up or
    /// half a metre down, along <paramref name="up"/>) settles the feet on it; then nothing of the construct or the map may be
    /// where the body would be.
    /// </summary>
    private static bool StandAt(Construct c, int ladder, Vector3 at, Vector3 up, float height, int[] hits, out Vector3 feet, out bool floored)
    {
        feet = at;
        floored = false;
        var floorPart = -1;
        var here = c.ToLocal(at);
        var pad = new Vector3(1.2f);
        var count = c.PartsInBox(here - pad, here + pad, hits);
        var best = float.MaxValue;
        for (var ci = 0; ci < count; ci++)
        {
            var i = hits[ci];
            ref var p = ref c.Parts[i];
            if (p.Broken || i == ladder)
                continue;
            c.WorldFrame(p.Seg, out var q, out var t);
            var iq = Quaternion.Conjugate(q);
            var upLocal = Vector3.Transform(up, iq);
            if (!TopFace(upLocal, out var ax, out var sign))
                continue;
            var lp = Vector3.Transform(at - t, iq);
            for (var b = 0; b < p.D.SolidBoxes; b++)
            {
                c.BoxOf(i, b, out var centre, out var half);
                var over = true;
                for (var a = 0; a < 3 && over; a++)
                    over = a == ax || MathF.Abs(lp[a] - centre[a]) <= half[a] + 0.05f;
                if (!over)
                    continue;
                var dist = (lp[ax] - (centre[ax] + sign * half[ax])) * sign; // (the feet above the face)
                if (dist < -World.StepHeight || dist > 0.5f || MathF.Abs(dist) >= MathF.Abs(best))
                    continue;
                best = dist;
                floorPart = i;
            }
        }
        if (floorPart >= 0)
        {
            feet = at - up * (best - 0.02f);
            floored = true;
        }
        else if (World.Query.Raycast(at + up * 0.3f, -up, 0.85f, QueryFilter.All, out var hit) && Vector3.Dot(hit.Normal, up) > 0.6f)
        {
            feet = hit.Point + up * 0.02f; // (the map's ground: a ladder down to the ground outside)
            floored = true;
        }
        return !BodyHits(c, feet, height, floorPart, ladder, hits) && !World.BlockedByBoxes(feet, height);
    }

    /// <summary>Nothing solid between the rungs' end and the spot (two points along the way; the ladder itself doesn't count).</summary>
    private static bool PathClear(Construct c, int ladder, Vector3 from, Vector3 to, Vector3 up, float height, int[] hits)
    {
        for (var k = 1; k <= 2; k++)
        {
            var at = Vector3.Lerp(from, to, 0.33f * k + 0.17f);
            if (BodyHits(c, at + up * 0.25f, height - 0.3f, -1, ladder, hits))
                return false;
        }
        return true;
    }

    /// <summary>As <see cref="BlockedBy"/> (a body standing at the feet in the construct's boxes), the ladder left out, into a caller's scratch.</summary>
    private static bool BodyHits(Construct c, Vector3 feet, float height, int onPart, int ignore, int[] hits)
    {
        var mid = feet + new Vector3(0, height * 0.5f + 0.05f, 0);
        var here = c.ToLocal(mid);
        var pad = new Vector3(height + 1f);
        var count = c.PartsInBox(here - pad, here + pad, hits);
        for (var ci = 0; ci < count; ci++)
        {
            var i = hits[ci];
            ref var p = ref c.Parts[i];
            if (p.Broken || i == ignore)
                continue;
            c.WorldFrame(p.Seg, out var q, out var t);
            var lp = Vector3.Transform(mid - t, Quaternion.Conjugate(q));
            for (var b = 0; b < p.D.SolidBoxes; b++)
            {
                c.BoxOf(i, b, out var centre, out var half);
                var rel = Vector3.Abs(lp - centre);
                if (rel.X < half.X + World.PlayerRadius * 0.7f && rel.Z < half.Z + World.PlayerRadius * 0.7f && rel.Y < half.Y + height * 0.45f
                    && !(i == onPart && rel.Y >= half.Y + height * 0.45f - 0.1f))
                    return true;
            }
        }
        return false;
    }
}

/// <summary>A vertical ladder's shape as the climb animation needs it, in the ladder's own frame (ConstructRideLadder.cs).</summary>
public struct LadderGeom
{
    /// <summary>Its middle across; its front face (climbed from, -z); its rungs' faces; its foot; its landing; its rails' top.</summary>
    public float Cx, FrontZ, RungZ, Base, Top, RailTop;

    /// <summary>Rungs are this far apart, the first one this far above the foot (the shipped ladders: TorpedoFrigateParts.Ladder).</summary>
    public const float Rung = 0.3f;

    public static LadderGeom Of(ConstructPartDef d) => new()
    {
        Cx = (d.BoundsMin.X + d.BoundsMax.X) * 0.5f, FrontZ = d.BoundsMin.Z, RungZ = d.BoundsMin.Z + 0.04f, Base = d.BoundsMin.Y,
        Top = d.LadderTop, RailTop = MathF.Max(d.BoundsMax.Y, d.LadderTop + 0.3f),
    };
}
