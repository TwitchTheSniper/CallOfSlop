using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Riding constructs in their frame (board items dev-sandbox-ride-frame-rf01 and dev-sandbox-board-assist-ba01;
/// docs/sandbox/CONSTRUCTS.md, "Riding in a vehicle's frame"). A player standing on a construct's part is kept in the
/// frame of that part's segment: <see cref="MoveState.RideLocal"/> holds the feet there, and each tick the frame's new
/// pose puts them back in the world exactly (a rigid transform: no drift on turns, no slide on a pitching deck). Their
/// <see cref="MoveState.Velocity"/> is their own, relative to the surface, so walking towards a truck's front goes
/// towards its front however it turns. A jump stays in the frame while they are within the construct's bounds (or a
/// short coyote time after leaving its surface), so it lands where it took off; leaving (walking off, jumping clear, a
/// crash over <see cref="ThrowAccel"/>, a blast) turns the velocity back into the world's and adds the surface's
/// (linear and turning), so stepping off a moving vehicle keeps its momentum (<see cref="MoveState.RideFree"/>: weak air
/// steering a while, so the momentum carries).
///
/// <para>All of it runs inside <see cref="ConstructWorld.BlockPlayer"/>, the movement step the server, the client's
/// prediction and its replays share, so every machine evolves the frame-local state the same way whatever pose it has
/// for the construct: the server is authoritative in the vehicle's frame, the snapshots carry the frame-local feet,
/// and lag shows as the vehicle drawn a little behind, never as the rider sliding on it.</para>
///
/// <para>Boarding assists (ba01): touching a moving deck from above within <see cref="MatchAbove"/> matches its speed at
/// once; a construct's tops are stepped up onto up to <see cref="DeckStep"/> when walking into them; jumping into a
/// ledge up to <see cref="VaultReach"/> high vaults onto it (a short climb in the frame, <see cref="MoveState.RideClimb"/>);
/// Use near a construct's side or a boarding part grabs on (<see cref="GrabOn"/>). Grip parts (a "grip" setting, the
/// Grip Deck) hold riders on faces up to nearly vertical and through harder accelerations.</para>
/// </summary>
public static class ConstructRide
{
    /// <summary>Ticks a rider stays in the frame after leaving its surface, wherever they are (0.3 s).</summary>
    public const int CoyoteTicks = 18;

    /// <summary>Ticks in the frame off its surface at most, even inside its bounds (a long fall inside a tall build).</summary>
    public const int MaxAirTicks = 180;

    /// <summary>The construct's bounds count this far round it (m), and this far above its top, for staying in the frame mid-jump.</summary>
    public const float BoundsPad = 0.5f, BoundsAbove = 2.5f;

    /// <summary>A deck's acceleration (m/s^2) that throws its riders off: across, or dropping away; on grip parts, more.</summary>
    public const float ThrowAccel = 2.5f * 9.81f, DropAccel = 1.6f * 9.81f, GripThrowAccel = 5f * 9.81f;

    /// <summary>Ticks of weak air steering after leaving a construct moving faster than <see cref="FreeSpeed"/> (m/s).</summary>
    public const int FreeTicks = 45;

    public const float FreeSpeed = 1.5f;

    /// <summary>Thrown off by a crash: up off the deck at least this fast (m/s), and not taken back into a frame for this many ticks.</summary>
    public const float ThrowPop = 2.5f;

    public const byte RejoinTicks = 30;

    /// <summary>Riders thrown off by a crash (the self-test).</summary>
    public static int Throws;

    /// <summary>Boarding assist 1: feet this far (m) above a moving construct's top, coming down, land on it at its speed.</summary>
    public const float MatchAbove = 0.3f;

    /// <summary>Boarding assist 2: a construct's tops are stepped up onto this high (m) when walking into them.</summary>
    public const float DeckStep = 0.6f;

    /// <summary>Boarding assist 2: jumping into a construct's ledge up to this high (m) above the feet vaults onto it.</summary>
    public const float VaultReach = 1.4f;

    /// <summary>Boarding assist 3: Use within this (m) of a construct's part grabs on, and climbs onto a top this high (m) above the feet at most.</summary>
    public const float GrabReach = 1.5f, GrabRise = 2.6f;

    /// <summary>A grip face holds a rider while its up is at least this much of world up (about 84 degrees from level).</summary>
    public const float GripUp = 0.1f;

    /// <summary>The local view turns with the construct under you (a per-frame camera effect; the server just gets the turned yaw).</summary>
    public static bool TurnView = true;

    // ---- Accelerations (server) -----------------------------------------------------------------------------------

    public static void BeforePhysics(ConstructWorld w)
    {
        var bodies = w.Bodies;
        for (var i = 0; i < bodies.Count; i++)
            bodies[i].VelBefore = bodies[i].Vel;
    }

    public static void AfterPhysics(ConstructWorld w, float dt)
    {
        var bodies = w.Bodies;
        for (var i = 0; i < bodies.Count; i++)
        {
            var c = bodies[i];
            var a = c.Anchored || c.Asleep ? Vector3.Zero : (c.Vel - c.VelBefore) / dt;
            c.Accel = Vector3.Lerp(c.Accel, a, 0.5f); // (a one-tick contact spike counts half)
        }
    }

    // ---- The frame ------------------------------------------------------------------------------------------------

    /// <summary>The velocity of the surface of segment <paramref name="seg"/> at a world point (the body's motion, linear and turning, and its joints').</summary>
    public static Vector3 SurfaceVel(Construct c, int seg, Vector3 at) =>
        (c.Anchored || c.Asleep ? Vector3.Zero : c.PointVel(at)) + c.SegPointVel(seg, at);

    /// <summary>Is standing on this segment riding it? (An anchored build's own floor never moves: no frame needed.)</summary>
    public static bool Rideable(Construct c, int seg) => !c.Anchored || seg != 0;

    /// <summary>World feet from a frame-local point.</summary>
    public static Vector3 ToWorld(Construct c, int seg, Vector3 local)
    {
        c.WorldFrame(seg, out var q, out var t);
        return t + Vector3.Transform(local, q);
    }

    /// <summary>A frame-local point from world feet.</summary>
    public static Vector3 ToLocal(Construct c, int seg, Vector3 world)
    {
        c.WorldFrame(seg, out var q, out var t);
        return Vector3.Transform(world - t, Quaternion.Conjugate(q));
    }

    /// <summary>The ride ends: the relative velocity becomes the world's (plus the surface's, when <paramref name="momentum"/>).</summary>
    public static void Leave(ref MoveState s, Construct? c, bool momentum)
    {
        if (momentum && c != null && s.RideSeg <= c.JointCount)
        {
            var v = SurfaceVel(c, s.RideSeg, s.Position);
            s.Velocity += v;
            if (v.LengthSquared() > FreeSpeed * FreeSpeed)
                s.RideFree = FreeTicks;
        }
        Clear(ref s);
    }

    public static void Clear(ref MoveState s)
    {
        s.RideBody = 0;
        s.RideSeg = 0;
        s.RideAir = 0;
        s.RideClimb = 0;
        s.RideGripUp = Vector3.Zero;
    }

    /// <summary>
    /// The start of a player's construct step (<see cref="ConstructWorld.BlockPlayer"/>): a rider's own move this tick (what
    /// the mover did since the frame last put them down) goes into the frame, and the frame's pose now puts them back in
    /// the world. Returns the construct ridden (null: not riding, or the ride just ended), the surface's velocity at the
    /// feet, and whether a climb is under way (then nothing else moves them this tick).
    /// </summary>
    public static Construct? Begin(ConstructWorld w, ref MoveState s, float height, bool server, out Vector3 surfaceVel, out bool climbing)
    {
        surfaceVel = Vector3.Zero;
        climbing = false;
        if (s.RideBody == 0)
        {
            if (s.RideAir > 0)
                s.RideAir--; // (thrown off: a while before a deck takes them into its frame again)
            return null;
        }
        var c = w.Get(s.RideBody);
        if (c == null || s.RideSeg > c.JointCount)
        {
            Clear(ref s); // (the construct went: the velocity is what it was)
            return null;
        }
        if (s.MantleTicks > 0 || s.KnockTicks == Knockback.StaggerTicks - 1)
        {
            Leave(ref s, c, momentum: true); // a mantle onto the map, or a blast's push (an impulse): off, with the deck's speed
            return null;
        }
        c.WorldFrame(s.RideSeg, out var q, out var t);
        if (s.RideClimb > 0)
        {
            // A climb (a vault, a grab-on): glide to its end in the frame; nothing else moves them.
            s.RideLocal += (s.RideClimbTo - s.RideLocal) / s.RideClimb;
            s.RideClimb--;
            s.Position = t + Vector3.Transform(s.RideLocal, q);
            s.Velocity = Vector3.Zero;
            s.Grounded = s.RideClimb == 0;
            s.RideWorld = s.Position;
            s.RideAir = 0;
            surfaceVel = SurfaceVel(c, s.RideSeg, s.Position);
            climbing = s.RideClimb > 0;
            return c;
        }
        var d = s.Position - s.RideWorld; // the mover's own step this tick (walking, falling, a push)
        if (d.LengthSquared() > 9f)
        {
            Clear(ref s); // moved by something else (a teleport): not in the frame any more
            return null;
        }
        var iq = Quaternion.Conjugate(q);
        if (s.RideGripUp != Vector3.Zero)
        {
            // On a grip face: gravity doesn't pull them down it, and the walk goes along it (forward into a steep face is up it).
            var n = Vector3.Normalize(Vector3.Transform(s.RideGripUp, q));
            var flat = new Vector3(d.X, MathF.Max(0, d.Y), d.Z);
            d = Vector3.Transform(flat, TurnBetween(Vector3.UnitY, n));
        }
        s.RideLocal += Vector3.Transform(d, iq);
        var to = t + Vector3.Transform(s.RideLocal, q);
        // The relative velocity turns with the frame.
        var spin = (c.Anchored || c.Asleep ? Vector3.Zero : c.AngVel) + SegSpin(c, s.RideSeg);
        var angle = spin.Length() * World.TickDt;
        if (angle > 1e-6f)
            s.Velocity = Vector3.Transform(s.Velocity, Quaternion.CreateFromAxisAngle(spin / spin.Length(), angle));
        surfaceVel = SurfaceVel(c, s.RideSeg, to);
        // The server: a crash or a sudden drop throws them off at the deck's speed before it (an impulse: the blast above).
        if (server && !c.Anchored && !c.Asleep)
        {
            var a = c.Accel;
            var limit = s.RideGripUp != Vector3.Zero ? GripThrowAccel : ThrowAccel;
            if (a.X * a.X + a.Z * a.Z > limit * limit || -a.Y > (s.RideGripUp != Vector3.Zero ? GripThrowAccel : DropAccel))
            {
                s.Position = to;
                s.Velocity += surfaceVel - (c.Vel - c.VelBefore); // (the deck's speed before this step's jolt)
                Throws++;
                s.Velocity.Y = MathF.Max(s.Velocity.Y, ThrowPop);
                s.Grounded = false;
                Clear(ref s);
                s.RideFree = FreeTicks;
                s.RideAir = RejoinTicks; // (not back in its frame at once: thrown)
                w.Events.Add($"a rider was thrown off construct {c.Id} ({a.Length() / 9.81f:0.0} g)");
                return null;
            }
        }
        // Carried into the map's geometry (a low bridge, a wall it scrapes past): off the deck where they stood.
        if (World.BlockedByBoxes(to, height))
        {
            Leave(ref s, c, momentum: true);
            return null;
        }
        s.Position = to;
        s.RideWorld = to;
        return c;
    }

    /// <summary>
    /// The end of a player's construct step: standing on a part, they ride its segment (joining it matches its speed);
    /// off every surface, they stay in the frame mid-jump inside its bounds, or leave it with its momentum.
    /// </summary>
    public static void End(ref MoveState s, Construct? ride, Construct? standing, int standSeg, Vector3 gripUp)
    {
        var feet = s.Position;
        if (standing != null && Rideable(standing, standSeg) && (ride != null || s.RideAir == 0))
        {
            if (ride != standing || s.RideSeg != standSeg)
            {
                var v = SurfaceVel(standing, standSeg, feet);
                if (ride != null)
                    s.Velocity += SurfaceVel(ride, s.RideSeg, feet) - v; // (from one frame into another)
                else
                {
                    s.Velocity -= v;
                    if (v.X * v.X + v.Z * v.Z > 1f)
                    {
                        // Boarding a moving construct: matched at once, no slide off its back.
                        s.Velocity.X = 0;
                        s.Velocity.Z = 0;
                    }
                }
                s.RideBody = standing.Id;
                s.RideSeg = (byte)standSeg;
                s.RideClimb = 0;
            }
            s.RideAir = 0;
            s.RideFree = 0;
            s.RideGripUp = gripUp;
            s.RideLocal = ToLocal(standing, standSeg, feet);
            s.RideWorld = feet;
            return;
        }
        if (ride == null)
            return;
        if (standing != null || s.Grounded)
        {
            Leave(ref s, ride, momentum: true); // onto an anchored floor or the map's ground: off the frame, with its speed
            return;
        }
        s.RideGripUp = Vector3.Zero;
        if (s.RideAir < 255)
            s.RideAir++;
        var lb = ride.ToLocal(feet);
        var inside = lb.X > ride.LocalMin.X - BoundsPad && lb.X < ride.LocalMax.X + BoundsPad
                     && lb.Z > ride.LocalMin.Z - BoundsPad && lb.Z < ride.LocalMax.Z + BoundsPad
                     && lb.Y > ride.LocalMin.Y - BoundsPad && lb.Y < ride.LocalMax.Y + BoundsAbove;
        if ((inside || s.RideAir <= CoyoteTicks) && s.RideAir < MaxAirTicks)
        {
            s.RideLocal = ToLocal(ride, s.RideSeg, feet);
            s.RideWorld = feet;
            return;
        }
        Leave(ref s, ride, momentum: true);
    }

    /// <summary>A grip face's up (an axis of the segment's frame) in a byte for the wire: 0 none, else 1 + axis * 2 (+1 negative).</summary>
    public static byte GripByte(Vector3 up)
    {
        for (var a = 0; a < 3; a++)
            if (up[a] != 0)
                return (byte)(1 + a * 2 + (up[a] < 0 ? 1 : 0));
        return 0;
    }

    public static Vector3 GripVec(byte b)
    {
        var v = Vector3.Zero;
        if (b is >= 1 and <= 6)
            v[(b - 1) / 2] = (b - 1) % 2 == 0 ? 1 : -1;
        return v;
    }

    /// <summary>The turn rate a joint chain adds to segment <paramref name="seg"/> (its hinges' and turntables', world axes).</summary>
    private static Vector3 SegSpin(Construct c, int seg)
    {
        var w = Vector3.Zero;
        for (var guard = 0; seg > 0 && seg <= c.JointCount && guard <= Construct.MaxJoints; guard++)
        {
            ref var j = ref c.Joints[seg - 1];
            if (j.Vel != 0 && !j.Slides)
            {
                c.WorldFrame(j.ParentSeg, out var q, out _);
                w += Vector3.Transform(j.Axis, q) * j.Vel;
            }
            seg = j.ParentSeg;
        }
        return w;
    }

    /// <summary>The shortest turn taking unit vector <paramref name="from"/> to <paramref name="to"/>.</summary>
    public static Quaternion TurnBetween(Vector3 from, Vector3 to)
    {
        var dot = Vector3.Dot(from, to);
        if (dot > 0.99999f)
            return Quaternion.Identity;
        var axis = Vector3.Cross(from, to);
        if (axis.LengthSquared() < 1e-10f)
            axis = MathF.Abs(from.X) < 0.9f ? Vector3.Cross(from, Vector3.UnitX) : Vector3.Cross(from, Vector3.UnitZ);
        return Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), MathF.Acos(Math.Clamp(dot, -1f, 1f)));
    }

    /// <summary>Starts a climb in a construct's frame from where the feet are to a world point on it, over <paramref name="ticks"/>.</summary>
    public static void StartClimb(ref MoveState s, Construct c, int seg, Vector3 to, int ticks)
    {
        s.RideBody = c.Id;
        s.RideSeg = (byte)seg;
        s.RideLocal = ToLocal(c, seg, s.Position);
        s.RideClimbTo = ToLocal(c, seg, to);
        s.RideClimb = (byte)Math.Clamp(ticks, 1, 60);
        s.RideAir = 0;
        s.RideFree = 0;
        s.RideGripUp = Vector3.Zero;
        s.RideWorld = s.Position;
        s.Velocity = Vector3.Zero;
        s.MantleTicks = 0;
        s.Stance = Stance.Stand;
    }

    /// <summary>Ticks a vault or a pull-up of <paramref name="rise"/> metres takes.</summary>
    public static int ClimbTicks(float rise) => Math.Clamp((int)(6 + rise * 9), 6, 30);

    /// <summary>
    /// Boarding assist 3, Use near a construct's side (or a boarding part: a grab handle, a boarding ladder, a step plate):
    /// the nearest standable top of it within <see cref="GrabReach"/> across and <see cref="GrabRise"/> above the feet;
    /// the player is latched on in its frame and pulled up onto it (half a second or so). A boarding part's own top, or
    /// the top nearest it, is taken first. Returns null when it started, else why not.
    /// </summary>
    public static string? GrabOn(ref MoveState s, Construct c, int part, float height, float slack = 0)
    {
        if (s.RideClimb > 0)
            return "already climbing";
        if (c.Dirty)
            c.Rebuild();
        var feet = s.Position;
        var prefer = part >= 0 && part < c.PartCount && c.DefOf(part).Boarding ? ConstructRender.PartCentre(c, part) : (Vector3?)null;
        var best = float.MaxValue;
        var bestSeg = 0;
        var bestAt = Vector3.Zero;
        for (var i = 0; i < c.PartCount; i++)
        {
            ref var p = ref c.Parts[i];
            if (p.Broken)
                continue;
            c.WorldFrame(p.Seg, out var q, out var t);
            var iq = Quaternion.Conjugate(q);
            var upLocal = Vector3.Transform(Vector3.UnitY, iq);
            for (var b = 0; b < p.D.SolidBoxes; b++)
            {
                c.BoxOf(i, b, out var centre, out var half);
                if (!TopFace(upLocal, out var ax, out var sign))
                    continue;
                var wide = 0;
                for (var a = 0; a < 3; a++)
                    if (a != ax && half[a] >= 0.1f)
                        wide++;
                if (wide < 2)
                    continue; // (a rail, a rung, a standoff: too small to stand on)
                // The point of the top face nearest the feet, a body's radius in from its edges where it is wide enough.
                var lf = Vector3.Transform(feet - t, iq);
                var top = lf;
                for (var a = 0; a < 3; a++)
                {
                    if (a == ax)
                        continue;
                    var inset = MathF.Min(World.PlayerRadius + 0.05f, half[a]);
                    top[a] = Math.Clamp(lf[a], centre[a] - half[a] + inset, centre[a] + half[a] - inset);
                }
                top[ax] = centre[ax] + sign * half[ax];
                var at = t + Vector3.Transform(top, q);
                var rise = at.Y - feet.Y;
                var across = new Vector2(at.X - feet.X, at.Z - feet.Z).Length();
                if (rise < -0.3f || rise > GrabRise || across > GrabReach + World.PlayerRadius + 0.5f + slack)
                    continue;
                if (BlockedOn(c, i, at, height))
                    continue;
                var score = across + MathF.Max(0, rise) * 0.3f + (prefer is { } pf ? Vector3.Distance(at, pf) * 2f : 0);
                if (score < best)
                {
                    best = score;
                    bestSeg = p.Seg;
                    bestAt = at;
                }
            }
        }
        if (best == float.MaxValue)
            return "nothing to climb onto there";
        StartClimb(ref s, c, bestSeg, bestAt + new Vector3(0, 0.02f, 0), ClimbTicks(MathF.Max(0.5f, bestAt.Y - feet.Y)));
        return null;
    }

    /// <summary>The box face nearest world up (its axis and side), when it is up enough to stand on (the walkable slope).</summary>
    public static bool TopFace(Vector3 upLocal, out int ax, out int sign)
    {
        ax = MathF.Abs(upLocal.X) > MathF.Abs(upLocal.Y) ? (MathF.Abs(upLocal.X) > MathF.Abs(upLocal.Z) ? 0 : 2) : (MathF.Abs(upLocal.Y) > MathF.Abs(upLocal.Z) ? 1 : 2);
        sign = upLocal[ax] > 0 ? 1 : -1;
        return MathF.Abs(upLocal[ax]) >= 0.7f;
    }

    /// <summary>
    /// A grip face under the feet (box frame): the face nearest world up, even nearly vertical (<see cref="GripUp"/>), with
    /// the feet over it and within a step below to <paramref name="above"/> above it along its up. The point on it and its up.
    /// </summary>
    public static bool GripAt(Vector3 lf, Vector3 centre, Vector3 half, Vector3 upLocal, float above, out Vector3 point, out Vector3 normal)
    {
        point = normal = Vector3.Zero;
        var best = float.MaxValue;
        for (var a = 0; a < 3; a++)
            for (var sign = -1; sign <= 1; sign += 2)
            {
                if (upLocal[a] * sign < GripUp)
                    continue; // (a face looking down, or nearly level with the horizon)
                var over = true;
                for (var b = 0; b < 3 && over; b++)
                    over = b == a || MathF.Abs(lf[b] - centre[b]) <= half[b] + 0.05f;
                if (!over)
                    continue;
                var face = centre[a] + sign * half[a];
                var dist = (lf[a] - face) * sign;
                if (dist > above || dist < -World.StepHeight || MathF.Abs(dist) >= best)
                    continue;
                best = MathF.Abs(dist); // (the face the feet are on, of the faces up enough)
                point = lf;
                point[a] = face;
                normal = Vector3.Zero;
                normal[a] = sign;
            }
        return best < float.MaxValue;
    }

    /// <summary>Would a body standing at <paramref name="feet"/> on part <paramref name="onPart"/> be inside another of the construct's solid boxes?</summary>
    public static bool BlockedOn(Construct c, int onPart, Vector3 feet, float height)
    {
        var mid = feet + new Vector3(0, height * 0.5f + 0.05f, 0);
        for (var i = 0; i < c.PartCount; i++)
        {
            ref var p = ref c.Parts[i];
            if (p.Broken)
                continue;
            c.WorldFrame(p.Seg, out var q, out var t);
            var lp = Vector3.Transform(mid - t, Quaternion.Conjugate(q));
            for (var b = 0; b < p.D.SolidBoxes; b++)
            {
                c.BoxOf(i, b, out var centre, out var half);
                var rel = Vector3.Abs(lp - centre);
                // (a body: radius across, half its height up and down; the floor it stands on is just below)
                if (rel.X < half.X + World.PlayerRadius * 0.7f && rel.Z < half.Z + World.PlayerRadius * 0.7f && rel.Y < half.Y + height * 0.45f
                    && !(i == onPart && rel.Y >= half.Y + height * 0.45f - 0.1f))
                    return true;
            }
        }
        return false;
    }
}
