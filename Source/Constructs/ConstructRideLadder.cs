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
        low = new Vector3(cx, d.LadderFoot + 0.02f, d.BoundsMin.Z - LadderBack);
        high = new Vector3(cx, d.LadderTop + 0.02f, d.BoundsMin.Z - LadderBack);
    }

    /// <summary>A ladder's ends as the player stands to use it, in its own frame: in front of the foot, and on the landing behind the top.</summary>
    public static void LadderEnds(ConstructPartDef d, out Vector3 foot, out Vector3 top)
    {
        var cx = (d.BoundsMin.X + d.BoundsMax.X) * 0.5f;
        var stand = World.BodyRadius + 0.12f;
        foot = new Vector3(cx, d.LadderFoot + 0.02f, d.BoundsMin.Z - stand);
        top = new Vector3(cx, d.LadderTop + 0.02f, d.BoundsMax.Z + stand);
    }

    /// <summary>
    /// F on a vertical ladder: from near its foot, climbs it to the landing; from up there, climbs down to its foot. Returns
    /// null when the climb started, else why not (too far from either end, a stowed boarding ladder, already climbing).
    /// </summary>
    public static string? ClimbLadder(ref MoveState s, Construct c, int part, float height, bool frame = false, float look = 0)
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
        if (FootMoving(c, part))
            return "the ladder is still coming down"; // (a boarding hatch's sections telescoping out: climbed once they rest)
        if (ConstructDoor.EventDoor(c, part) is var hatch and >= 0 && hatch != part && (ConstructDoors.IsMoving(c, hatch) || !ConstructDoors.SettledOpen(c, hatch)))
            return ConstructDoors.IsMoving(c, hatch) ? "the hatch is still moving" : "the ladder is stowed: F opens the hatch"; // (a ladder on a door's leaf, the Dray's belly hatch: its pose is the leaf's only once the door rests open; crews too)
        if (NextLid(c, part) is var lid and >= 0)
            return ConstructDoors.IsMoving(c, c.Doors[lid].Part) ? "the hatch is still moving" : ConstructDoors.Locked(c, c.Doors[lid].Part) ? "the hatch is locked" : "the hatch is shut: F opens it"; // (a deck hatch over its shaft, a belly door under it: F on the ladder opens them first, GameSandbox Use; crews too, CrewNav)
        if (s.RideBody == c.Id && s.RideUp != Vector3.Zero && s.RideSeg <= c.JointCount)
            s.Position = ConstructRide.ToWorld(c, s.RideSeg, s.RideLocal); // (a rider in its frame is where the frame holds them now: a ship posed in orbit moves metres a tick after the riders' step, so the world feet lag it and the wrong end was taken; Karl, 2026-11-22)
        c.WorldFrame(p.Seg, out var q, out var t);
        var pos = p.Pos;
        var rot = p.Rot;
        var lq = q * Rot24.Quat(rot); // (the ladder's own frame in the world)
        Vector3 ToWorld(Vector3 local) => t + Vector3.Transform(pos + Rot24.Apply(rot, local), q);
        var feetLocal = Vector3.Transform(Vector3.Transform(s.Position - t, Quaternion.Conjugate(q)) - pos, Quaternion.Conjugate(Rot24.Quat(rot))); // (the feet in the ladder's own frame)
        LadderEnds(d, out var foot, out var top);
        LadderLine(d, out var low, out var high);
        var atTop = feetLocal.Y > (foot.Y + top.Y) * 0.5f;
        // Where the climber looks picks the way when they can reach both ends (Karl, 2026-11-27: "facing up will go up, facing down will
        // go down"): looking up, from the foot; looking down, from the top. Level, or out of reach of that end: the nearer end, as before.
        if (look > LookPicks && atTop && CanStart(feetLocal, foot))
            atTop = false;
        else if (look < -LookPicks && !atTop && CanStart(feetLocal, top))
            atTop = true;
        var from = atTop ? top : foot;
        var across = new Vector2(feetLocal.X - from.X, feetLocal.Z - from.Z).Length();
        if (across > GrabReach + World.BodyRadius || MathF.Abs(feetLocal.Y - from.Y) > 1.2f)
            return "get to the ladder's foot or its top first";
        var up = Vector3.Normalize(Vector3.Transform(Vector3.UnitY, lq));
        var onEnd = ToWorld(atTop ? low : high); // (where the climb leaves the rungs)
        Vector3 exit;
        if (Chained(Bodies, c, part, !atTop, out var oc, out var op) is var dock and >= 0)
            return ConstructDoors.IsMoving(c, c.Doors[dock].Part) ? "the docking hatch is still moving" : "the docking hatch is shut: nothing is docked there";
        if (oc != null)
        {
            // (a ladder that meets another's end through two open docking hatches, a docked craft's: one climb on through both, to
            // the far end of the other; both bodies are held together while docked, so the climb's points in this one's frame hold)
            var o = oc.Parts[op];
            oc.WorldFrame(o.Seg, out var oq, out var ot);
            var olq = oq * Rot24.Quat(o.Rot);
            LadderEnds(o.D, out var ofoot, out var otop);
            LadderLine(o.D, out var olow, out var ohigh);
            Vector3 OWorld(Vector3 local) => ot + Vector3.Transform(o.Pos + Rot24.Apply(o.Rot, local), oq);
            onEnd = OWorld(atTop ? olow : ohigh);
            exit = LadderExit(oc, op, OWorld(atTop ? ofoot : otop), onEnd, olq, up, atTop ? -1f : 1f, height);
        }
        else
            exit = LadderExit(c, part, ToWorld(atTop ? foot : top), onEnd, lq, up, atTop ? -1f : 1f, height);
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

    /// <summary>A look this far up or down (radians, about 20 degrees) picks which way a ladder is climbed (<see cref="ClimbLadder"/>).</summary>
    private const float LookPicks = 0.35f;

    /// <summary>Whether feet at <paramref name="feetLocal"/> (the ladder's frame) can start a climb from end <paramref name="end"/>.</summary>
    private static bool CanStart(Vector3 feetLocal, Vector3 end) =>
        new Vector2(feetLocal.X - end.X, feetLocal.Z - end.Z).Length() <= GrabReach + World.BodyRadius && MathF.Abs(feetLocal.Y - end.Y) <= 1.2f;

    /// <summary>A door linked to the ladder (a boarding hatch's sections, a stowed foot) still moving: the climb waits for it.</summary>
    internal static bool FootMoving(Construct c, int ladder)
    {
        if (c.DoorMovingCount == 0)
            return false;
        for (var l = 0; l < c.LinkCount; l++)
        {
            ref var ln = ref c.Links[l];
            var other = ln.A == ladder ? ln.B : ln.B == ladder ? ln.A : -1;
            if (other >= 0 && c.DoorOf(other) is var k and >= 0 && c.Doors[k].Part == other && ConstructDoors.IsMoving(c, other))
                return true;
        }
        return false;
    }

    /// <summary>
    /// The ladder a door part is the foot of (a boarding hatch's sections: linked to a ladder, which they lower), or -1: F on the foot
    /// is F on its ladder (Karl, 2026-11-27: the lowered ladder showed no line and F there did nothing).
    /// </summary>
    public static int LadderOfFoot(Construct c, int part)
    {
        if ((uint)part >= (uint)c.PartCount || c.Parts[part].D.Door is not { Seq: > 0 } || c.DoorOfListed(part) is not (var k and >= 0) || c.Doors[k].Part != part)
            return -1;
        for (var l = 0; l < c.LinkCount; l++)
        {
            ref var ln = ref c.Links[l];
            var other = ln.A == part ? ln.B : ln.B == part ? ln.A : -1;
            if (other >= 0 && other < c.PartCount && c.Parts[other].D.LadderTop > 0 && !c.Parts[other].Broken)
                return other;
        }
        return -1;
    }

    /// <summary>
    /// A ladder's way on through a dock (Karl, 2026-11-22: "ladder is connected to dock, two docks connect two ladders so you end
    /// up on either end"): when the end it is climbed towards (<paramref name="up"/>: its top) lies by a docking hatch of its own
    /// body, the ladder on the craft docked there (carrying this body, or carried by it) whose near end meets this one's, the two
    /// lined up, comes back in <paramref name="other"/> and <paramref name="part"/>. Returns -1, or the door slot of a docking
    /// hatch by that end that isn't open (the climb waits for it: nothing docked, or the hatches still sliding). No docking hatch
    /// there, or one open with no ladder meeting this one: -1 and no other ladder, an ordinary climb. Shared by the climb and its drawing (allocates nothing).
    /// </summary>
    public static int Chained(ConstructWorld? w, Construct c, int ladder, bool up, out Construct? other, out int part)
    {
        other = null;
        part = -1;
        if ((uint)ladder >= (uint)c.PartCount || !c.EnsureDoors() || c.DoorCount == 0)
            return -1;
        var end = LadderPoint(c, ladder, up, out var dir);
        var dock = DockBy(c, end);
        if (dock < 0)
            return -1;
        if (ConstructDoors.IsMoving(c, c.Doors[dock].Part) || !ConstructDoors.SettledOpen(c, c.Doors[dock].Part) || w == null)
            return dock;
        if (c.CarriedBy != 0 && w.Get(c.CarriedBy) is { } carrier && Meets(carrier, end, dir, up, out part))
        {
            other = carrier;
            return -1;
        }
        var bodies = w.Bodies;
        for (var b = 0; b < bodies.Count; b++)
            if (bodies[b].CarriedBy == c.Id && bodies[b] != c && Meets(bodies[b], end, dir, up, out part))
            {
                other = bodies[b];
                return -1;
            }
        return -1; // (open, but no ladder meets it: a ladder that runs on into the docked craft by itself, or a hatch opened by hand)
    }

    /// <summary>A ladder's feet line's end (world): its top (<paramref name="top"/>) or its foot, and its up.</summary>
    private static Vector3 LadderPoint(Construct c, int ladder, bool top, out Vector3 up)
    {
        ref var p = ref c.Parts[ladder];
        c.WorldFrame(p.Seg, out var q, out var t);
        LadderLine(p.D, out var low, out var high);
        up = Vector3.Normalize(Vector3.Transform(Rot24.Apply(p.Rot, Vector3.UnitY), q));
        return t + Vector3.Transform(p.Pos + Rot24.Apply(p.Rot, top ? high : low), q);
    }

    /// <summary>A docking hatch of body <paramref name="c"/> whose middle, shut, lies within 1.6 m of <paramref name="at"/> (world): its door slot, else -1.</summary>
    private static int DockBy(Construct c, Vector3 at)
    {
        for (var k = 0; k < c.DoorCount; k++)
        {
            var dp = c.Doors[k].Part;
            if (c.Parts[dp].Broken || c.Parts[dp].D.Door is not { Dock: true })
                continue;
            if (Vector3.DistanceSquared(c.ToWorld(ConstructDoors.ShutPos(c, dp)), at) < 1.6f * 1.6f) // (where it lies shut: open, it has slid off)
                return k;
        }
        return -1;
    }

    /// <summary>A ladder of body <paramref name="o"/> lined up with one climbed along <paramref name="up"/> whose near end meets <paramref name="end"/> (within 0.35 m across, up to 1.2 m on along the climb).</summary>
    private static bool Meets(Construct o, Vector3 end, Vector3 dir, bool up, out int part)
    {
        part = -1;
        for (var i = 0; i < o.PartCount; i++)
        {
            if (o.Parts[i].Broken || o.Parts[i].D.LadderTop <= 0)
                continue;
            var near = LadderPoint(o, i, !up, out var odir);
            if (Vector3.Dot(odir, dir) < 0.95f)
                continue;
            var gap = near - end;
            var along = Vector3.Dot(gap, dir) * (up ? 1f : -1f);
            if (along < -0.3f || along > 1.2f || (gap - dir * Vector3.Dot(gap, dir)).Length() > 0.35f)
                continue;
            part = i;
            return true;
        }
        return false;
    }

    /// <summary>
    /// The next lid across a ladder from door slot <paramref name="from"/> on (Karl, 2026-11-22: ladders in shafts under flush
    /// hatches): an event door other than one the ladder rides, lying flat while shut (a Deck Hatch over the shaft's top, a belly
    /// door under a boarding shaft), a solid box of which crosses the ladder's line from its foot to its landing. Its slot, else
    /// -1. An open lid hangs upright down the shaft's side and isn't one, so only shut or moving lids are found.
    /// </summary>
    public static int NextLid(Construct c, int ladder, int from = 0)
    {
        if (c.DoorCount == 0 || (uint)ladder >= (uint)c.PartCount)
            return -1;
        ref var p = ref c.Parts[ladder];
        var d = p.D;
        var own = ConstructDoor.EventDoor(c, ladder);
        c.WorldFrame(p.Seg, out var q, out var t);
        float cx = (d.BoundsMin.X + d.BoundsMax.X) * 0.5f, cz = (d.BoundsMin.Z + d.BoundsMax.Z) * 0.5f;
        var foot = t + Vector3.Transform(p.Pos + Rot24.Apply(p.Rot, new Vector3(cx, d.LadderFoot, cz)), q);
        var top = t + Vector3.Transform(p.Pos + Rot24.Apply(p.Rot, new Vector3(cx, d.LadderTop, cz)), q);
        for (var k = Math.Max(0, from); k < c.DoorCount; k++)
        {
            var dp = c.Doors[k].Part;
            if (dp == own || dp == ladder || c.Parts[dp].Broken || c.Parts[dp].D.Door is { Dock: true } or { Launch: true } or { Gear: > 0 })
                continue; // (a docking hatch opens as its body docks, a launch hatch for a launch, landing gear from a seat: none is a ladder's to work)
            c.WorldFrame(c.Parts[dp].Seg, out var dq, out var dt);
            var iq = Quaternion.Conjugate(dq);
            Vector3 a = Vector3.Transform(foot - dt, iq), b = Vector3.Transform(top - dt, iq); // (the ladder's line in the door's segment frame)
            for (var n = 0; n < c.SolidCount(dp); n++)
            {
                c.SolidBox(dp, n, out var centre, out var half);
                if (half.Y > 0.1f || half.X < 0.2f || half.Z < 0.2f)
                    continue; // (not a lid lying flat: a door in a wall, an open lid hanging down the shaft, a socket's strip)
                var y0 = MathF.Min(a.Y, b.Y) - 0.3f;
                var y1 = MathF.Max(a.Y, b.Y) + 0.3f;
                var o = Vector3.Abs(a - centre);
                if (o.X <= half.X + 0.35f && o.Z <= half.Z + 0.35f && centre.Y + half.Y >= y0 && centre.Y - half.Y <= y1)
                    return k;
            }
        }
        return -1;
    }

    /// <summary>
    /// Server: opens everything that keeps a ladder from being climbed (its shut lids, NextLid, and a stowed foot or sections
    /// linked to it, StowedFoot). True when something was set moving.
    /// </summary>
    public static bool OpenWay(ConstructWorld world, Construct c, int ladder)
    {
        var any = false;
        for (var k = NextLid(c, ladder); k >= 0; k = NextLid(c, ladder, k + 1))
            if (!ConstructDoors.TargetOpen(c, c.Doors[k].Part) && !ConstructDoors.Locked(c, c.Doors[k].Part)) // (a locked lid stays shut: F on the ladder is no key)
            {
                ConstructDoors.Command(world, c, k, true);
                any = true;
            }
        for (var l = 0; l < c.LinkCount; l++)
        {
            ref var ln = ref c.Links[l];
            var other = ln.A == ladder ? ln.B : ln.B == ladder ? ln.A : -1;
            if (other >= 0 && c.DoorOf(other) is var k and >= 0 && c.Doors[k].Part == other && !ConstructDoors.TargetOpen(c, other) && !ConstructDoors.Locked(c, other))
            {
                ConstructDoors.Command(world, c, k, true);
                any = true;
            }
        }
        return any;
    }

    /// <summary>Settles a ladder's way open at once (checks and tools: what OpenWay starts, finished).</summary>
    public static void SettleWay(Construct c, int ladder)
    {
        for (var k = NextLid(c, ladder); k >= 0; k = NextLid(c, ladder, k + 1))
            ConstructDoors.Settle(c, k, true);
        for (var l = 0; l < c.LinkCount; l++)
        {
            ref var ln = ref c.Links[l];
            var other = ln.A == ladder ? ln.B : ln.B == ladder ? ln.A : -1;
            if (other >= 0 && c.DoorOf(other) is var k and >= 0 && c.Doors[k].Part == other)
                ConstructDoors.Settle(c, k, true);
        }
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
                if (rel.X < half.X + World.BodyRadius * 0.7f && rel.Z < half.Z + World.BodyRadius * 0.7f && rel.Y < half.Y + height * 0.45f
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
        Cx = (d.BoundsMin.X + d.BoundsMax.X) * 0.5f, FrontZ = d.BoundsMin.Z, RungZ = d.BoundsMin.Z + 0.04f, Base = d.LadderFoot,
        Top = d.LadderTop, RailTop = MathF.Max(d.BoundsMax.Y, d.LadderTop + 0.3f),
    };
}

