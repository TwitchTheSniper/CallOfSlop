using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The physics gun (board item dev-sandbox-physics-gun-pg01; docs/sandbox/CONSTRUCTS.md, "The physics gun"): its numbers,
/// the spring that pulls a held construct after the aim (run inside the physics step, so what it holds still collides
/// and pushes rather than passing through things), the rotation helpers the client turns a held thing with, and the
/// contested rule every editing tool shares (decision 19). The server's grab, steer, freeze and unfreeze rules are in
/// <see cref="ConstructEdits"/> (below). Nothing here runs for a construct nobody holds.
/// </summary>
public static class ConstructGrab
{
    /// <summary>How far the beam reaches (metres): creative, survival.</summary>
    public const float Range = 30f, SurvivalRange = 12f;

    /// <summary>The nearest a held thing comes (the mouse wheel pulls it in this far at most).</summary>
    public const float MinDist = 1.2f;

    /// <summary>
    /// How hard the beam pulls (newtons) and the heaviest thing it takes (kg): creative, survival. pr02: the spring of
    /// before pr01 again, stronger (was 200 kN and 8 kN): a 15 t truck now gets 100 m/s2 in creative. Survival's are where a
    /// player starts (Karl, 2026-11-08: 1,500 kg, was 500 kg at 20 kN), 30 m/s2 at the limit; tool upgrades raise both per
    /// player (<see cref="ToolUpgrades"/>, <see cref="ConstructEdits.LiftOf"/>, <see cref="ConstructEdits.PullOf"/>).
    /// </summary>
    public const float Strength = 1_500_000f, MaxMass = 20_000_000f, SurvivalStrength = SurvivalMaxMass * ToolUpgrades.PullPerKg, SurvivalMaxMass = 1500f;

    /// <summary>The most the beam accelerates anything (m/s2, on top of holding it up against gravity; was 40): light parts don't snap about.</summary>
    public const float MaxAccel = 120f;

    /// <summary>The spring: the gap closes over about this long (s), at most this fast (m/s); the turn likewise (s, rad/s). Were 0.1 s, 18 m/s, 0.12 s, 9 rad/s.</summary>
    public const float SettleTime = 0.06f, MaxPull = 30f, TurnTime = 0.07f, MaxTurn = 15f;

    /// <summary>The share of the beam's acceleration the spring plans to brake with (the rest is margin for gravity and contacts).</summary>
    public const float BrakeShare = 0.7f;

    /// <summary>Beside another construct the held thing moves at most this far a tick (m) and turns this fast (rad/s): overlap contacts can't be jumped.</summary>
    public const float NearStep = 0.2f, NearTurn = 12f;

    /// <summary>The acceleration the beam can give a construct of this mass.</summary>
    public static float AccelFor(float mass, bool creative) => AccelFor(mass, creative, SurvivalStrength);

    /// <summary>The same with a survival beam of this pull (N: a player's own, with their upgrades).</summary>
    public static float AccelFor(float mass, bool creative, float pull) => creative ? Math.Clamp(Strength / MathF.Max(mass, 1f), CreativeMinAccel, MaxAccel) : MathF.Min(MaxAccel, pull / MathF.Max(mass, 1f));

    /// <summary>Creative: however heavy (a capital hull block, 20,000 t at most): building a ship is not a wait for it to move.</summary>
    public const float CreativeMinAccel = 8f;

    /// <summary>How fast the beam can change the held thing's spin (rad/s2): strong enough to beat the floor's and the wheels' twist on a heavy body.</summary>
    public static float SpinFor(float accel) => Math.Clamp(accel * 4f, 20f, 480f);

    /// <summary>
    /// The client's drawn pose of what it holds may lead the server's by this much (m, rad) plus the faster of the server's
    /// motion and the aim's own motion (m/s, rad/s) times <see cref="LeadSeconds"/> (about a round trip).
    /// </summary>
    public const float LeadMin = 0.15f, LeadAngleMin = 0.1f, LeadSeconds = 0.2f;

    /// <summary>
    /// cj01: below this aim speed (m/s, or rad/s of turn) the fixed lead shrinks towards <see cref="RestLead"/> (m) and
    /// <see cref="RestAngle"/> (rad): an aim at rest draws the piece on the server's pose, give or take this.
    /// </summary>
    public const float RestSpeed = 0.5f, RestLead = 0.01f, RestAngle = 0.004f;

    /// <summary>Decision 19: an enemy construct is contested while its owner is alive within this many metres (plus its own size).</summary>
    public static float ContestRadius = 40f;

    /// <summary>Two presses of reload within this long (s) with the physics gun out unfreeze everything you froze.</summary>
    public const double DoubleTap = 0.35;

    /// <summary>The rotation snap's step (Shift held while turning).</summary>
    public const float SnapStep = MathF.PI / 4;

    /// <summary>The look's yaw as a turn about up (World.FlatForward(yaw) is this turn of -Z).</summary>
    public static Quaternion YawQ(float yaw) => Quaternion.CreateFromAxisAngle(Vector3.UnitY, -yaw);

    /// <summary>The turn a held thing's <see cref="SandboxHand.GrabRel"/> is relative to: the look's yaw, or for a rider their look's yaw in the construct's frame.</summary>
    public static Quaternion LookQ(in SandboxHand h, float yaw) => h.RideBody != 0 ? h.RideTurn : YawQ(yaw);

    /// <summary>
    /// Inside the physics step, after gravity (the spring of before pr01, stronger: pr02): the held construct's velocity is
    /// pushed towards the one that brings its grab point onto the beam's point, and its spin towards the one that brings its
    /// turn onto the wanted turn, by no more than the beam's strength allows this tick (plus gravity, so it hangs in the
    /// air). The contacts are solved after this, so it still stops on what it meets.
    /// </summary>
    public static void Drive(Construct c, float dt, bool nearOther = false)
    {
        var maxPull = nearOther ? MathF.Min(MaxPull, NearStep / dt) : MaxPull; // (another construct close by: a step short enough that the overlap tests can't be jumped)
        var maxTurn = nearOther ? MathF.Min(MaxTurn, NearTurn) : MaxTurn;
        // The turn first: the rotation still to go, as an axis and angle, closed over TurnTime.
        var q = c.GrabRot * Quaternion.Conjugate(c.Rot);
        if (q.W < 0)
            q = new Quaternion(-q.X, -q.Y, -q.Z, -q.W);
        var s = MathF.Sqrt(q.X * q.X + q.Y * q.Y + q.Z * q.Z);
        var w = Vector3.Zero;
        if (s > 1e-6f)
        {
            var angle = 2f * MathF.Atan2(s, q.W);
            w = new Vector3(q.X, q.Y, q.Z) / s * MathF.Min(angle / TurnTime, maxTurn);
        }
        var dw = w + c.GrabFrameSpin - c.AngVel; // (in a rider's hands, on top of their deck's turn)
        var spin = SpinFor(c.GrabAccel) * dt;
        if (dw.LengthSquared() > spin * spin)
            dw *= spin / dw.Length();
        if (float.IsFinite(dw.X + dw.Y + dw.Z))
            c.AngVel += dw;
        // The move: the grab point's wanted velocity closes the gap over SettleTime; the centre takes it, less what the
        // spin already gives the grab point (pr02: a point off the centre is carried, not swung round the centre).
        var at = c.ToWorld(c.GrabLocal);
        var gap = c.GrabTarget - at;
        var want = gap / SettleTime;
        // At most the top speed, and no faster than the beam can brake from within the gap (a stronger beam overshot otherwise).
        var cap = MathF.Min(maxPull, MathF.Sqrt(2f * BrakeShare * c.GrabAccel * gap.Length()));
        if (want.LengthSquared() > cap * cap)
            want *= cap / want.Length();
        if (!float.IsFinite(want.X + want.Y + want.Z))
            return;
        var dv = want + c.GrabFrameVel - Vector3.Cross(c.AngVel, at - c.WorldCom) - c.Vel; // (the same: on top of their deck's speed)
        var lim = (c.GrabAccel + ConstructPhysics.GravityAccel) * dt;
        if (dv.LengthSquared() > lim * lim)
            dv *= lim / dv.Length();
        c.Vel += dv;
        var own = c.Vel - c.GrabFrameVel;
        if (nearOther && own.LengthSquared() > maxPull * maxPull)
            c.Vel = c.GrabFrameVel + own * (maxPull / own.Length());
    }

    // ---- A free joint in the beam: a slider's carriage, a free rotation part's leaf, and what they carry --------------------

    /// <summary>The joint a beam on part <paramref name="part"/> moves on its own, or -1: the part's own carriage or leaf (<paramref name="leaf"/>), else a free joint it hangs on. -1 too with a carriage that has no joint yet.</summary>
    public static int LeafJoint(Construct c, int part, bool leaf)
    {
        if ((uint)part >= (uint)c.PartCount)
            return -1;
        if (leaf)
            return c.Parts[part].D.HasJoint ? c.RailJointOf(part) : -1;
        var seg = c.MoveSeg(c.Parts[part].Seg);
        return seg > 0 && c.Joints[seg - 1].FreeMoving ? seg - 1 : -1;
    }

    /// <summary>
    /// Why the beam can't move this joint on its own, or null (peers work it out alike; holds by others and the contested rule
    /// are the caller's). Only what has no motor moves: an Actuator is run by its Set signal and a shaft rail's screw doesn't back-drive.
    /// </summary>
    public static string? LeafRefusal(Construct c, int part, bool leaf, bool creative, float lift = SurvivalMaxMass)
    {
        var r = leaf && c.Parts[part].D.IsRail ? c.RailRun(part) : -1;
        var drive = r >= 0 && r < c.RailCount ? c.DefOf(c.Rails[r].Drive).RailDrive : RailDriveKind.Free;
        if (drive == RailDriveKind.Electric)
            return "its motor holds it: use its Set signal";
        if (drive == RailDriveKind.Shaft)
            return c.Rails[r].Joint >= 0 && c.Joints[c.Rails[r].Joint].BackDrive ? "its lead screw takes a load, not a beam: turn its shaft" : "its lead screw holds it: turn its shaft";
        if (c.GrabbedBy != 255)
            return "someone else is holding it";
        var j = LeafJoint(c, part, leaf);
        if (j < 0)
            return null;
        ref var jt = ref c.Joints[j];
        if (jt.Locked)
            return "its Lock signal holds it";
        for (var i = 0; i < c.PartCount; i++)
            if (c.Parts[i].SitterPlus1 != 0 && c.Under(i, j + 1))
                return "someone is on it";
        var cap = creative ? MaxMass : lift;
        if (c.SegMass[j + 1] > cap)
            return $"too heavy for the physics gun ({c.SegMass[j + 1]:0} kg; {cap:0} kg at most{(creative ? "" : " in survival")})";
        return null;
    }

    /// <summary>Why a part that hangs on a powered rail stays put, or null: what the look says when the construct can't be taken as a whole either.</summary>
    public static string? PoweredRefusal(Construct c, int part)
    {
        var seg = (uint)part < (uint)c.PartCount ? c.MoveSeg(c.Parts[part].Seg) : 0;
        return seg > 0 && c.Joints[seg - 1] is { Rail: true } jt ? LeafRefusal(c, jt.Hinge, true, true) : null;
    }

    /// <summary>
    /// The nearest carriage or free rotation part's moving side along a ray (a carriage is drawn, not solid, so the ordinary ray
    /// goes through it), up to <paramref name="maxDist"/>: false when there is none. Nothing allocates.
    /// </summary>
    public static bool AimLeaf(ConstructWorld world, Vector3 eye, Vector3 dir, float maxDist, List<Construct> near, out Construct? body, out int part, out float dist)
    {
        body = null;
        part = -1;
        dist = maxDist;
        world.NearRay(eye, dir, maxDist, near);
        for (var n = 0; n < near.Count; n++)
        {
            var c = near[n];
            for (var k = 0; k < c.FreeLeafCount; k++)
            {
                int i = c.FreeLeaves[k];
                if (i >= c.PartCount || c.Parts[i].Broken)
                    continue;
                var j = c.JointOf(i);
                c.WorldFrame(j >= 0 ? j + 1 : c.Parts[i].Seg, out var q, out var t);
                var inv = Quaternion.Conjugate(q);
                var o = Vector3.Transform(eye - t, inv);
                var d = Vector3.Transform(dir, inv);
                var boxes = c.Parts[i].D.Boxes;
                for (var b = 0; b < boxes.Length; b++)
                {
                    if (!boxes[b].IsLeaf)
                        continue;
                    c.BoxOf(i, b, out var centre, out var half);
                    if (RayBox(o - centre, d, half + new Vector3(0.02f), dist, out var at))
                    {
                        dist = at;
                        body = c;
                        part = i;
                    }
                }
            }
        }
        return body != null;
    }

    private static bool RayBox(Vector3 o, Vector3 d, Vector3 half, float maxDist, out float dist)
    {
        float lo = 0, hi = maxDist;
        dist = 0;
        for (var a = 0; a < 3; a++)
        {
            if (MathF.Abs(d[a]) < 1e-7f)
            {
                if (MathF.Abs(o[a]) > half[a])
                    return false;
                continue;
            }
            var t0 = (-half[a] - o[a]) / d[a];
            var t1 = (half[a] - o[a]) / d[a];
            if (t0 > t1)
                (t0, t1) = (t1, t0);
            lo = MathF.Max(lo, t0);
            hi = MathF.Min(hi, t1);
            if (lo > hi)
                return false;
        }
        dist = lo;
        return true;
    }

    /// <summary>
    /// Where the joint on <paramref name="seg"/> should be to bring the held point (<paramref name="local"/>, the segment's frame) onto
    /// the beam: along a rail or about a swivel's axis, inside its limits. <paramref name="radius"/>: the point's reach from the axis.
    /// </summary>
    public static float JointTarget(Construct c, int seg, Vector3 local, Vector3 beam, out float radius)
    {
        ref var jt = ref c.Joints[seg - 1];
        radius = 1;
        c.WorldFrame(seg, out var sq, out var st);
        var at = st + Vector3.Transform(local, sq);
        c.WorldFrame(jt.ParentSeg, out var pq, out var pt);
        var axis = Vector3.Transform(jt.Axis, pq);
        float want;
        if (jt.Slides)
            want = jt.Angle + Vector3.Dot(beam - at, axis);
        else
        {
            var pivot = pt + Vector3.Transform(jt.Pivot, pq);
            var from = at - pivot;
            var to = beam - pivot;
            from -= axis * Vector3.Dot(from, axis);
            to -= axis * Vector3.Dot(to, axis);
            radius = from.Length();
            if (radius < 0.05f || to.LengthSquared() < 0.0025f)
                return jt.Angle; // (held on the axis, or the beam through it: no way to tell a turn)
            want = jt.Angle + MathF.Atan2(Vector3.Dot(axis, Vector3.Cross(from, to)), Vector3.Dot(from, to));
        }
        return float.IsFinite(jt.Min) ? Math.Clamp(want, jt.Min, jt.Max) : want;
    }

    /// <summary>Does the held piece hang on another free joint? Then the beam pulls through both, and only the server knows how it shares out.</summary>
    public static bool InChain(Construct c, int seg)
    {
        for (int s = c.Joints[seg - 1].ParentSeg, guard = 0; s > 0 && s <= c.JointCount && guard <= Construct.MaxJoints; guard++)
        {
            ref var jt = ref c.Joints[s - 1];
            if (jt is { FreeMoving: true, FrozenLock: false, Locked: false })
                return true;
            s = jt.ParentSeg;
        }
        return false;
    }

    /// <summary>The piece of a mechanism a press would freeze: a segment (0: the base), or -1 where there is no such thing (no joints, a base a foundation holds).</summary>
    public static int PieceOf(Construct c, int part, bool leaf)
    {
        if ((uint)part >= (uint)c.PartCount || c.MovingJoints == 0)
            return -1;
        if (leaf)
            return c.Parts[part].D.HasJoint && c.RailJointOf(part) is var j and >= 0 ? j + 1 : -1;
        var seg = c.MoveSeg(c.Parts[part].Seg);
        return seg == 0 && c.Anchored && !c.Frozen ? -1 : seg;
    }

    /// <summary>
    /// The holder's own screen (client, every frame, after the poses are blended): the held thing drawn at the pose the
    /// aim asks for, now, instead of where the server's last snapshots put it, but never further from the server's pose
    /// than <see cref="LeadMin"/> plus the speed of the motion in hand times <see cref="LeadSeconds"/> (a wall the server
    /// stopped it at shows as a wall once the aim rests, and a disagreement beyond that blends back to the server).
    /// <paramref name="aimSpeed"/> and <paramref name="aimTurn"/> are how fast the aim's point and turn are moving (m/s, rad/s);
    /// <paramref name="bodySpeed"/> and <paramref name="bodySpin"/> the body's own, smoothed (NaN: its last pose's velocity,
    /// which on wheels dragged over the ground jumps from pose to pose, and the lead allowed jumped with it: the holder's
    /// buggy kinked at hundreds of m/s2 whenever the server's pose was more than the lead behind).
    /// </summary>
    public static void Predict(Construct c, Vector3 eye, Vector3 look, float yaw, float dist, Quaternion rel, float aimSpeed, float aimTurn, float leadSeconds = LeadSeconds,
        float bodySpeed = float.NaN, float bodySpin = float.NaN) =>
        Predict(c, eye, look, YawQ(yaw), dist, rel, aimSpeed, aimTurn, leadSeconds, bodySpeed, bodySpin);

    /// <summary>As above, the turn the held thing is relative to given as a turn (a rider's look: <see cref="LookQ"/>).</summary>
    public static void Predict(Construct c, Vector3 eye, Vector3 look, Quaternion lookTurn, float dist, Quaternion rel, float aimSpeed, float aimTurn, float leadSeconds = LeadSeconds,
        float bodySpeed = float.NaN, float bodySpin = float.NaN)
    {
        if (float.IsNaN(bodySpeed))
            bodySpeed = c.Vel.Length();
        if (float.IsNaN(bodySpin))
            bodySpin = c.AngVel.Length();
        if (!float.IsFinite(eye.X + look.X + dist + rel.X + rel.W + leadSeconds))
            return;
        var wantRot = Quaternion.Normalize(lookTurn * rel);
        var rot = c.Rot;
        var dot = Quaternion.Dot(rot, wantRot);
        var angle = 2f * MathF.Acos(Math.Clamp(MathF.Abs(dot), 0f, 1f));
        // cj01: the fixed part of the lead only while the aim moves (or just did): once it rests the drawn pose settles
        // on the server's, so a piece the server holds back (a wall, the floor, its own wheels) is drawn where it is.
        var moving = Math.Clamp(MathF.Max(aimSpeed, aimTurn) / RestSpeed, 0f, 1f);
        var allowedAngle = MathF.Max(LeadAngleMin * moving, RestAngle) + MathF.Max(bodySpin, aimTurn) * leadSeconds;
        if (angle > 1e-5f)
            rot = angle <= allowedAngle ? wantRot : Quaternion.Slerp(rot, dot < 0 ? -wantRot : wantRot, allowedAngle / angle);
        var wantPos = eye + look * dist - Vector3.Transform(c.GrabLocal, rot);
        var lead = wantPos - c.Pos;
        var allowed = MathF.Max(LeadMin * moving, RestLead) + MathF.Max(bodySpeed, aimSpeed) * leadSeconds;
        var l2 = lead.LengthSquared();
        if (l2 > allowed * allowed)
            wantPos = c.Pos + lead * (allowed / MathF.Sqrt(l2));
        c.Rot = Quaternion.Normalize(rot);
        c.Pos = wantPos;
    }

    /// <summary>A held thing turned by the mouse, in the look's yaw frame: about up (<paramref name="yaw"/>) and about the look's right (<paramref name="pitch"/>).</summary>
    public static Quaternion Turn(Quaternion rel, float yaw, float pitch) =>
        Quaternion.Normalize(Quaternion.CreateFromAxisAngle(Vector3.UnitX, pitch) * Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw) * rel);

    /// <summary>The look's right as a direction in the world (the held thing's turn up and down is about the axis nearest it).</summary>
    public static Vector3 RightOf(float yaw) => Vector3.Transform(Vector3.UnitX, YawQ(yaw));

    /// <summary>A world turn as the turn relative to the look's yaw (what the hands hold and the server applies).</summary>
    public static Quaternion RelOf(float yaw, Quaternion world) => Quaternion.Normalize(Quaternion.Conjugate(YawQ(yaw)) * world);

    /// <summary>
    /// pw01: the turn snapped to whole 45 degree steps about the axes of <paramref name="frame"/> (identity: the world's
    /// own axes; another construct's turn: that one's axes), as a world turn.
    /// </summary>
    public static Quaternion SnapIn(Quaternion frame, Quaternion world)
    {
        var local = Quaternion.Conjugate(frame) * world;
        return Quaternion.Normalize(frame * Snap(local));
    }

    /// <summary>
    /// pw01: one snapped turn step of <paramref name="world"/>: <paramref name="yawStep"/> about the frame's up, then
    /// <paramref name="pitchStep"/> about the frame's horizontal axis (X or Z, either way round) nearest the look's right
    /// (<paramref name="viewRight"/>), each re-snapped, so every step is a clean 45 or 90 degrees in the frame's terms.
    /// </summary>
    public static Quaternion TurnSnapped(Quaternion world, Quaternion frame, float yawStep, float pitchStep, Vector3 viewRight)
    {
        var q = world;
        if (yawStep != 0)
            q = SnapIn(frame, Quaternion.CreateFromAxisAngle(Vector3.Transform(Vector3.UnitY, frame), yawStep) * q);
        if (pitchStep != 0)
        {
            var fx = Vector3.Transform(Vector3.UnitX, frame);
            var fz = Vector3.Transform(Vector3.UnitZ, frame);
            var dx = Vector3.Dot(fx, viewRight);
            var dz = Vector3.Dot(fz, viewRight);
            var axis = MathF.Abs(dx) >= MathF.Abs(dz) ? fx * MathF.Sign(dx) : fz * MathF.Sign(dz);
            q = SnapIn(frame, Quaternion.CreateFromAxisAngle(axis, pitchStep) * q);
        }
        return q;
    }

    /// <summary>The snap uses a construct within <see cref="SnapNear"/> instead of the world (off: the user's default, 2026-11-10).</summary>
    public static bool SnapToNearby;

    /// <summary>
    /// The world's axes as gravity has them at <paramref name="at"/>: identity where up is +Y; inside a gravity generator's
    /// box or on a round world, the map's axes turned the short way so that Y is up there (a snapped piece sits level on
    /// the local ground).
    /// </summary>
    public static Quaternion GravityFrame(Vector3 at)
    {
        Vector3 up;
        if (GravityFrames.VolumeGravity(at, out var g) && g.LengthSquared() > 1e-6f)
            up = -Vector3.Normalize(g);
        else if (GravityFrames.PlainUpAt(at))
            return Quaternion.Identity;
        else
        {
            var x = GravityFrames.ToWorld(at, Vector3.UnitX);
            var y = GravityFrames.ToWorld(at, Vector3.UnitY);
            var z = GravityFrames.ToWorld(at, Vector3.UnitZ);
            return Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(new Matrix4x4(x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0, z.X, z.Y, z.Z, 0, 0, 0, 0, 1)));
        }
        var d = Vector3.Dot(Vector3.UnitY, up);
        if (d > 0.99999f)
            return Quaternion.Identity;
        if (d < -0.99999f)
            return Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI);
        return Quaternion.Normalize(Quaternion.CreateFromAxisAngle(Vector3.Normalize(Vector3.Cross(Vector3.UnitY, up)), MathF.Acos(d)));
    }

    /// <summary>
    /// pw01: how close another construct must be (m between their bounds) for the snap to use its axes instead of the world's.
    /// The nearest other (not loose) construct to <paramref name="held"/> within that, or null (the world's axes).
    /// </summary>
    public static Construct? SnapReference(ConstructWorld world, Construct held)
    {
        Construct? best = null;
        var bestGap = SnapNear;
        var bodies = world.Bodies;
        for (var i = 0; i < bodies.Count; i++)
        {
            var o = bodies[i];
            if (o == held || o.Loose || o.PartCount == 0)
                continue;
            var gap = Vector3.Distance(held.WorldCom, o.WorldCom) - held.Radius - o.Radius;
            if (gap < bestGap)
            {
                bestGap = gap;
                best = o;
            }
        }
        return best;
    }

    public const float SnapNear = 2f;

    private static Quaternion[]? _snaps;

    /// <summary>The nearest turn whose yaw, pitch and roll are all whole 45 degree steps (Shift snaps the turn to these).</summary>
    public static Quaternion Snap(Quaternion q)
    {
        var all = _snaps ??= MakeSnaps();
        q = Quaternion.Normalize(q);
        var best = Quaternion.Identity;
        var bestDot = -1f;
        foreach (var s in all)
        {
            var d = MathF.Abs(Quaternion.Dot(s, q));
            if (d > bestDot + 1e-6f)
            {
                bestDot = d;
                best = s;
            }
        }
        return Quaternion.Dot(best, q) < 0 ? -best : best;
    }

    /// <summary>Is this turn one of the snapped ones (within <paramref name="tolerance"/> of the dot product)?</summary>
    public static bool IsSnapped(Quaternion q, float tolerance = 1e-4f) => MathF.Abs(Quaternion.Dot(Snap(q), Quaternion.Normalize(q))) > 1 - tolerance;

    private static Quaternion[] MakeSnaps()
    {
        var list = new List<Quaternion>(512);
        for (var y = 0; y < 8; y++)
        for (var x = 0; x < 8; x++)
        for (var z = 0; z < 8; z++)
        {
            var q = Quaternion.Normalize(Quaternion.CreateFromYawPitchRoll(y * SnapStep, x * SnapStep, z * SnapStep));
            var dup = false;
            foreach (var o in list)
                if (MathF.Abs(Quaternion.Dot(o, q)) > 0.99999f)
                {
                    dup = true;
                    break;
                }
            if (!dup)
                list.Add(q);
        }
        return list.ToArray();
    }

    /// <summary>
    /// Decision 19's contested rule, for every editing tool (the physics gun, the wrench, the welder, the linking tool; a
    /// tool still to come calls it too): someone else's construct is refused while its owner (players or bots; a member of
    /// your faction counts as you, <see cref="Factions.Shares"/>) is alive within <see cref="ContestRadius"/> of it or inside it. The
    /// map's constructs (nobody's), wrecks and your own are never contested. <paramref name="aliveAt"/> says where a player
    /// is (null: dead or gone). Null, or why not ("Owned by Name - defended"). Survival only: creative calls nothing.
    /// </summary>
    public static string? Defended(Construct c, byte player, Func<byte, Vector3?>? aliveAt, Func<byte, string>? nameOf)
    {
        if (c.Owner == 255 || Factions.Shares(c.Owner, player) || c.Wreck || aliveAt == null || aliveAt(c.Owner) is not { } at)
            return null;
        var reach = ContestRadius + c.Radius;
        if (Vector3.DistanceSquared(at, c.WorldCom) > reach * reach)
            return null;
        return $"Owned by {nameOf?.Invoke(c.Owner) ?? $"player {c.Owner}"} - defended";
    }
}

/// <summary>The physics gun on the server (ConstructGrab.cs): grab, steer, let go, freeze, unfreeze; and the contested rule.</summary>
public sealed partial class ConstructEdits
{
    /// <summary>Where a player is (null: dead or gone), for the contested rule (the server sets it; the client's own look text too).</summary>
    public Func<byte, Vector3?>? AliveAt;

    /// <summary>A player's name, for "Owned by Name - defended".</summary>
    public Func<byte, string>? NameOf;

    /// <summary>Why a player aboard stops this one moving a construct, or null (the game's friendly rule: Game.SandboxAboardRefusal).</summary>
    public Func<Construct, byte, string?>? Aboard;

    /// <summary>Whether this construct is the player's own reference frame: the body they stand on or sit in (the game's rule: Game.SandboxIsFrame).</summary>
    public Func<Construct, byte, bool>? IsFrame;

    /// <summary>Why the physics gun won't take or freeze the body a player stands on.</summary>
    public const string FrameRefusal = "you are standing on it";

    /// <summary>
    /// The physics gun never takes or freezes the player's own reference frame (user, 2026-11-10): building on a moving vehicle,
    /// a stray grab or freeze would stop it dead. With no game behind it, the hand's own ride and seat. Null, or why not.
    /// </summary>
    public string? FrameWhy(Construct c, byte player)
    {
        var frame = IsFrame != null ? IsFrame(c, player) : Hands[player].RideBody == c.Id || Hands[player].SeatBody == c.Id;
        return frame ? FrameRefusal : null;
    }

    /// <summary>The physics gun's and the Nudge tool's rule for who is aboard. With no game behind it, anyone in a seat refuses.</summary>
    public string? AboardWhy(Construct c, byte player)
    {
        if (Aboard != null)
            return Aboard(c, player);
        if (c.Driver != 255)
            return "someone is in it";
        for (var i = 0; i < c.PartCount; i++)
            if (c.Parts[i].SitterPlus1 != 0)
                return "someone is in it";
        return null;
    }

    /// <summary>Decision 19 (survival only; creative is unrestricted): why this player may not edit or grab this construct now, or null.</summary>
    public string? Contested(Construct c, byte player) => InfiniteParts ? null : ConstructGrab.Defended(c, player, AliveAt, NameOf);

    /// <summary>How far this player's beam holds something (their reach: <see cref="ReachOf"/>).</summary>
    public float GrabRange(byte player) => ReachOf(player, InfiniteParts);

    /// <summary>
    /// Why the physics gun can't take hold of this construct (or null): not with anyone unfriendly aboard, not something anchored
    /// to the ground (a foundation, a pinned construct), not one someone else holds, not too heavy (survival's cap is much
    /// lower), not a contested enemy construct in survival. A frozen one can be taken (it unfreezes).
    /// </summary>
    public string? GrabRefusal(Construct c, byte player)
    {
        if (c.GrabbedBy != 255 && c.GrabbedBy != player || c.NudgeBy != 255)
            return "someone else is holding it";
        if (AboardWhy(c, player) is { } aboard)
            return aboard;
        if (c.Anchored && !c.Frozen)
            return "it is anchored to the ground";
        var cap = LiftOf(player, InfiniteParts);
        if (c.Mass > cap)
            return $"too heavy for the physics gun ({c.Mass:0} kg; {cap:0} kg at most{(InfiniteParts ? "" : " in survival")})";
        return Contested(c, player);
    }

    /// <summary>
    /// The physics gun takes hold of construct <paramref name="body"/> at <paramref name="local"/> (its body frame, where
    /// the beam met it), keeping that point's distance and the thing's turn relative to the look. Null, or why not.
    /// </summary>
    public string? Grab(ConstructWorld world, byte player, Vector3 eye, float yaw, ushort body, Vector3 local, int part = -1, bool leaf = false)
    {
        ref var h = ref Hands[player];
        if (part >= 0 && h.Tool == ConstructTool.PhysicsGun && !h.Seated && world.Get(body) is { } holder && ConstructPress.MachineOf(holder, part) is var machine and >= 0)
        {
            // What a machine holds: the beam draws it out, and has it.
            if (TakeFromMachine(world, player, eye, holder, machine, part, out var piece) is { } why0)
                return why0;
            return piece == null ? null : Grab(world, player, eye, yaw, piece.Id, piece.Com);
        }
        if (part >= 0 && h.Tool == ConstructTool.PhysicsGun && !h.Seated && world.Get(body) is { } hook && ConstructOrdnance.RackOf(hook, part) >= 0)
        {
            // A bomb on a hook: one pull and it is off, in the beam (ConstructOrdnance.cs).
            if (GrabRefusal(hook, player) is { } why1)
                return why1;
            if (ConstructOrdnance.PullOff(world, hook, part) is not { } off)
                return "the world is full";
            var from = hook.Id;
            var answer = Grab(world, player, eye, yaw, off.Id, off.Com);
            off.TakenFrom = from;
            return answer;
        }
        if (part >= 0)
            return GrabJoint(world, player, eye, body, local, part, leaf);
        if (h.Seated)
            return "get out of the seat first";
        if (h.Tool != ConstructTool.PhysicsGun)
            return "take out the physics gun to grab things";
        if (world.Get(body) is not { } c || c.PartCount == 0)
            return "that is gone";
        if (!float.IsFinite(local.X + local.Y + local.Z) || Vector3.Clamp(local, c.LocalMin - new Vector3(0.3f), c.LocalMax + new Vector3(0.3f)) != local)
            return "the beam missed it";
        if (FrameWhy(c, player) is { } frame)
            return frame;
        if (GrabRefusal(c, player) is { } why)
            return why;
        var at = c.ToWorld(local);
        var dist = Vector3.Distance(eye, at);
        if (dist > CatchOf(player, InfiniteParts) + 1f)
            return "too far for the beam"; // (with the tractor beam: twice the reach, and the clamp below reels it in)
        if (h.GrabBody != 0 && h.GrabBody != body)
            Release(world, player);
        Unfreeze(world, c);
        c.TakenFrom = 0;
        if (c.CarriedBy != 0)
            ConstructCargo.Release(world, c); // (taken out of a frozen cargo volume: it isn't caught again while held)
        c.GrabbedBy = player;
        c.GrabLocal = local;
        c.GrabTarget = at;
        c.GrabRot = c.Rot;
        c.GrabAccel = ConstructGrab.AccelFor(c.Mass, InfiniteParts, PullOf(player, InfiniteParts));
        c.Wake();
        h.GrabBody = body;
        h.GrabSeg = 0;
        h.GrabLocal = local;
        h.GrabDist = Math.Clamp(dist, ConstructGrab.MinDist, GrabRange(player));
        h.GrabRel = Quaternion.Normalize(Quaternion.Conjugate(ConstructGrab.LookQ(h, yaw)) * c.Rot);
        h.Edit = EditKind.None;
        Done.Add($"player {player}: grabbed construct {body} with the physics gun");
        return null;
    }

    /// <summary>
    /// The physics gun on a free joint (docs/sandbox/CONSTRUCTS.md, "The physics gun"): the carriage or leaf moves alone, and what it
    /// carries with it; the rest of the construct is left as it is, frozen or anchored too. A carriage with nothing on it gets its
    /// joint now. <paramref name="local"/> is in the joint's segment's frame. Null, or why not.
    /// </summary>
    private string? GrabJoint(ConstructWorld world, byte player, Vector3 eye, ushort body, Vector3 local, int part, bool leaf)
    {
        ref var h = ref Hands[player];
        if (h.Seated)
            return "get out of the seat first";
        if (h.Tool != ConstructTool.PhysicsGun)
            return "take out the physics gun to grab things";
        if (world.Get(body) is not { } c || (uint)part >= (uint)c.PartCount)
            return "that is gone";
        if (!float.IsFinite(local.X + local.Y + local.Z) || Vector3.Distance(local, c.Com) > c.Radius + 2f)
            return "the beam missed it";
        if ((ConstructGrab.LeafRefusal(c, part, leaf, InfiniteParts, LiftOf(player, false)) ?? Contested(c, player)) is { } why)
            return why;
        var j = ConstructGrab.LeafJoint(c, part, leaf);
        if (j < 0)
        {
            if (!leaf)
                return "that doesn't move on its own";
            var hinge = c.Parts[part].D.IsRail && c.RailRun(part) is var run and >= 0 ? c.Rails[run].Hinge : part;
            if (world.AddJoint(c, hinge) == 0)
                return $"at most {Construct.MaxJoints} joints a construct";
            c.Rebuild();
            world.StructureDirty.Add(c.Id);
            j = c.JointCount - 1;
        }
        ref var jt = ref c.Joints[j];
        if (!jt.FreeMoving)
            return "that doesn't move on its own";
        if (jt.HeldFor > 0 && jt.HeldBy != player)
            return "someone else is holding it";
        if (jt.Frozen)
            SetPiece(world, c, j + 1, false, player); // (taking a frozen piece unfreezes it, that piece alone)
        c.WorldFrame(j + 1, out var q, out var t);
        var dist = Vector3.Distance(eye, t + Vector3.Transform(local, q));
        if (dist > CatchOf(player, InfiniteParts) + 1f)
            return "too far for the beam";
        if (h.GrabBody != 0)
            Release(world, player);
        h.GrabBody = body;
        h.GrabSeg = (byte)(j + 1);
        h.GrabHinge = jt.Hinge;
        h.GrabLocal = local;
        h.GrabDist = Math.Clamp(dist, ConstructGrab.MinDist, GrabRange(player));
        h.Edit = EditKind.None;
        Done.Add($"player {player}: took joint {j} of construct {body} with the physics gun");
        return null;
    }

    /// <summary>Every tick: a held joint is pulled towards the beam, for a few ticks at a time (a hold can't outlive the beam). The body stays as it is.</summary>
    private void GrabJointTick(ConstructWorld world, byte player, Vector3 eye, Vector3 look)
    {
        ref var h = ref Hands[player];
        var seg = (int)h.GrabSeg;
        var c = world.Get(h.GrabBody);
        if (c == null || h.Tool != ConstructTool.PhysicsGun || h.Seated || seg > c.JointCount || c.Joints[seg - 1].Hinge != h.GrabHinge || !c.Joints[seg - 1].FreeMoving
            || c.Joints[seg - 1].Locked || c.GrabbedBy != 255)
        {
            h.GrabBody = 0;
            h.GrabSeg = 0;
            return;
        }
        // What the beam moves: this piece and every joint behind it that can give (it shares the pull out in the solve).
        var moved = (int)h.GrabSeg;
        for (int s = seg, guard = 0; s > 0 && s <= c.JointCount && guard <= Construct.MaxJoints; guard++)
        {
            ref var jt = ref c.Joints[s - 1];
            if (jt.Freeform)
            {
                s = jt.ParentSeg;
                continue;
            }
            if (jt.FrozenLock || jt.Locked)
                break;
            moved = s;
            jt.HeldBy = player;
            jt.HeldFor = 3;
            s = jt.ParentSeg;
        }
        if (world.BeamCount < ConstructWorld.MaxBeams)
            world.Beams[world.BeamCount++] = new CBeam
            {
                Body = c.Id,
                Seg = (byte)seg,
                Local = h.GrabLocal,
                Target = eye + look * h.GrabDist,
                Mass = c.SegMass[moved],
                Accel = ConstructGrab.AccelFor(c.SegMass[moved], InfiniteParts, PullOf(player, InfiniteParts)),
            };
        c.JointHeld = 3;
        c.Wake();
    }

    /// <summary>The client's wheel and turn: the held thing's distance and its turn relative to the look's yaw.</summary>
    public void SteerGrab(byte player, float dist, Quaternion rel)
    {
        ref var h = ref Hands[player];
        if (h.GrabBody == 0 || !float.IsFinite(dist + rel.X + rel.Y + rel.Z + rel.W) || rel.LengthSquared() < 0.5f)
            return;
        h.GrabDist = Math.Clamp(dist, ConstructGrab.MinDist, GrabRange(player));
        h.GrabRel = Quaternion.Normalize(rel);
    }

    /// <summary>Lets go (it keeps the speed it had: a flick throws it, as before pr01).</summary>
    public void Release(ConstructWorld world, byte player)
    {
        ref var h = ref Hands[player];
        if (h.GrabBody == 0)
            return;
        var letGo = h.GrabSeg == 0 && world.Get(h.GrabBody) is { } c && c.GrabbedBy == player ? c : null;
        if (letGo != null)
            LetGo(letGo);
        h.GrabBody = 0;
        h.GrabSeg = 0;
        if (letGo != null)
            Shelve(world, letGo, player); // (let go over a shelf: it goes on the shelf)
    }

    /// <summary>Let go: it keeps the speed it had (pr02: as before pr01; a flick throws it).</summary>
    private static void LetGo(Construct c)
    {
        c.GrabbedBy = 255;
        c.Wake();
    }

    /// <summary>Every tick for every living player: the held thing follows their look (or is let go when it can't be held any more).</summary>
    public void GrabTick(ConstructWorld world, byte player, Vector3 eye, Vector3 look, float yaw, Vector3 frameVel = default, Vector3 frameSpin = default)
    {
        ref var h = ref Hands[player];
        if (h.GrabBody == 0)
            return;
        if (h.GrabSeg != 0)
        {
            GrabJointTick(world, player, eye, look);
            return;
        }
        var c = world.Get(h.GrabBody);
        var boarded = c != null && c.GrabbedBy == player ? AboardWhy(c, player) : null; // (someone unfriendly got in: it goes, and they say why)
        if (c == null || c.GrabbedBy != player || h.Tool != ConstructTool.PhysicsGun || h.Seated || boarded != null || c.Anchored)
        {
            if (c != null && c.GrabbedBy == player)
                LetGo(c);
            h.GrabBody = 0;
            if (boarded != null)
                Notices.Add((player, boarded));
            return;
        }
        // (a rider's look moves with their deck: the step below moves both on by the deck's speed, so the piece keeps its place in the frame)
        c.GrabTarget = eye + look * h.GrabDist + frameVel * World.TickDt;
        c.GrabRot = ConstructGrab.LookQ(h, yaw) * h.GrabRel;
        c.GrabFrameVel = frameVel;
        c.GrabFrameSpin = frameSpin;
        c.GrabAccel = ConstructGrab.AccelFor(c.Mass, InfiniteParts, PullOf(player, InfiniteParts));
        if (c.PartCount == 1 && c.Parts[0].D.Explosive != null && ConstructOrdnance.TrySnap(world, c))
        {
            LetGo(c);
            h.GrabBody = 0;
            world.Remove(c); // (it is on the hook now: ConstructOrdnance.cs)
        }
    }

    /// <summary>Aim while holding: the held thing freezes where it is (held still like an anchored construct, tinted) and is let go.</summary>
    public string? Freeze(ConstructWorld world, byte player)
    {
        ref var h = ref Hands[player];
        if (h.GrabSeg != 0 && world.Get(h.GrabBody) is { } held)
        {
            int seg = h.GrabSeg;
            Release(world, player);
            if (FrameWhy(held, player) is { } onIt)
                return onIt; // (a piece of the body they stand on, frozen in the world, would tear at the moving rest: let go, not frozen)
            SetPiece(world, held, seg, true, player); // (holding one piece: that piece)
            return null;
        }
        if (h.GrabBody == 0 || world.Get(h.GrabBody) is not { } c || c.GrabbedBy != player)
            return "hold something with the physics gun to freeze it";
        Release(world, player);
        if (FrameWhy(c, player) is { } frame)
            return frame; // (they stepped onto what they held: it goes free, never frozen under them)
        FreezeBody(world, c, player);
        return null;
    }

    /// <summary>A whole construct held still where it is, every piece of it (the physics gun's freeze, and a nudge landing).</summary>
    private void FreezeBody(ConstructWorld world, Construct c, byte player)
    {
        c.Frozen = true;
        c.FrozenBy = player;
        c.Anchored = true;
        c.Vel = c.AngVel = Vector3.Zero;
        for (var j = 0; j < c.JointCount; j++)
            if (!c.Joints[j].Freeform)
            {
                c.Joints[j].Frozen = true; // (every piece of it: they unfreeze one by one)
                c.Joints[j].Vel = 0;
            }
        c.Rebuild();
        world.StructureDirty.Add(c.Id); // (its frozen flag and pose go out with its structure)
        Done.Add($"player {player}: froze construct {c.Id}");
    }

    /// <summary>
    /// A press of Aim at a piece of a mechanism, or a held piece let go frozen (docs/sandbox/CONSTRUCTS.md, "Pieces of a mechanism"):
    /// one segment frozen or unfrozen on its own (0: the base). Frozen, it stays where it is in the world whatever the others do. Null, or why not.
    /// </summary>
    public string? FreezePiece(ConstructWorld world, byte player, Vector3 eye, ushort body, int seg)
    {
        if (Hands[player].Tool != ConstructTool.PhysicsGun)
            return "take out the physics gun to freeze things";
        if (world.Get(body) is not { } c || seg < 0 || seg > c.JointCount)
            return "that is gone";
        if (Vector3.Distance(eye, c.WorldCom) > GrabRange(player) + c.Radius + 1f)
            return "too far for the beam";
        if (Contested(c, player) is { } why)
            return why;
        seg = c.MoveSeg(seg);
        if (seg == 0 && c.Anchored && !c.Frozen)
            return "it is anchored to the ground";
        var freeze = seg == 0 ? !c.Frozen : !c.Joints[seg - 1].Frozen;
        if (freeze && FrameWhy(c, player) is { } frame)
            return frame; // (unfreezing the body underfoot is fine; freezing it is not)
        SetPiece(world, c, seg, freeze, player);
        return null;
    }

    /// <summary>One piece's frozen state, and what that does to the rest. A flexible joint's ball goes as one.</summary>
    private void SetPiece(ConstructWorld world, Construct c, int seg, bool frozen, byte player)
    {
        if (seg == 0)
        {
            c.Frozen = frozen;
            c.Anchored = frozen || c.Pinned;
            if (frozen)
                c.Vel = c.AngVel = Vector3.Zero;
        }
        else
        {
            var hinge = c.Joints[seg - 1].Hinge;
            for (var j = seg - 1; j < c.JointCount && c.Joints[j].Hinge == hinge && !c.Joints[j].Freeform; j++)
            {
                ref var jt = ref c.Joints[j];
                jt.Frozen = frozen;
                jt.RefSet = false; // (a motor picks up from where it stands)
                if (frozen)
                    jt.Vel = 0;
            }
        }
        c.FrozenBy = player;
        c.Dirty = true;
        c.Rebuild();
        c.Wake();
        world.StructureDirty.Add(c.Id);
        Done.Add($"player {player}: {(frozen ? "froze" : "unfroze")} piece {seg} of construct {c.Id}");
    }

    /// <summary>A frozen construct moves again (grabbed, or unfreeze-all).</summary>
    public static void Unfreeze(ConstructWorld world, Construct c)
    {
        if (!c.Frozen && c.FrozenPieces == 0)
            return;
        c.Frozen = false;
        c.FrozenBy = 255;
        c.Anchored = c.Pinned;
        for (var j = 0; j < c.JointCount; j++)
            if (c.Joints[j].Frozen)
            {
                c.Joints[j].Frozen = false;
                c.Joints[j].RefSet = false;
            }
        c.Dirty = true; // (a foundation anchors it again when it rebuilds)
        c.Wake();
        world.StructureDirty.Add(c.Id);
    }

    /// <summary>Double-tap reload with the physics gun out: everything this player froze moves again (<paramref name="everyone"/>: the host's option, everything). Returns how many.</summary>
    public int UnfreezeAll(ConstructWorld world, byte player, bool everyone)
    {
        var n = 0;
        foreach (var c in world.Bodies)
            if ((c.Frozen || c.FrozenPieces > 0) && (everyone || c.FrozenBy == player))
            {
                Unfreeze(world, c);
                n++;
            }
        if (n > 0)
            Done.Add($"player {player}: unfroze {n} construct{(n == 1 ? "" : "s")}{(everyone ? " (everyone's)" : "")}");
        return n;
    }

    /// <summary>A player leaves: what they hold is let go, and what they froze is nobody's (the host's unfreeze-all still reaches it).</summary>
    public void GrabLeave(ConstructWorld world, byte player)
    {
        Release(world, player);
        foreach (var c in world.Bodies)
            if (c.FrozenBy == player)
                c.FrozenBy = 255;
    }
}
