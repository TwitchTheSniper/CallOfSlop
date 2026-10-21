using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>One box of the legs, on one bone (0 pelvis, 1 thigh, 2 shin, 3 foot), in that bone's frame.</summary>
public readonly record struct LegPart(int Bone, Vector3 Size, Vector3 Center, int Paint);

/// <summary>Colours of a pair of legs: belt, trousers (the player colour, darkened), knee pads, boots, soles.</summary>
public struct LegColors
{
    public Vector3 Belt, Trousers, Knee, Boot, Sole;

    public static LegColors For(Vector3 color) => new()
    {
        Belt = new Vector3(0.16f, 0.15f, 0.14f),
        Trousers = Vector3.Lerp(color, new Vector3(0.2f, 0.21f, 0.2f), 0.45f) * 0.72f,
        Knee = new Vector3(0.22f, 0.22f, 0.24f),
        Boot = new Vector3(0.14f, 0.115f, 0.09f),
        Sole = new Vector3(0.07f, 0.07f, 0.07f),
    };

    public readonly Vector3 Of(int paint) => paint switch { 0 => Belt, 1 => Trousers, 2 => Knee, 3 => Boot, _ => Sole };
}

/// <summary>
/// The result of <see cref="LegAnimator.Animate"/> for one player, in body space (feet at the origin, -Z = facing; the
/// player's world matrix is RotY(-yaw) * T(position)). Each bone frame has local -Z along the bone (the foot: towards the
/// toes) and is rigid. The *Local matrices are each bone relative to its parent bone (pelvis relative to the body), which
/// is what the scene graph nodes hold and what the immediate path multiplies up, so both draw identical matrices.
/// Index 0 is the right leg, 1 the left.
/// </summary>
public sealed class LegPose
{
    /// <summary>Upper body (torso, head, gun, arms) relative to the body: the hip bob, landing dip, pelvis drop and lean.</summary>
    public Matrix4x4 Upper = Matrix4x4.Identity;
    public Matrix4x4 Pelvis = Matrix4x4.Identity, PelvisLocal = Matrix4x4.Identity;
    public readonly Matrix4x4[] Thigh = new Matrix4x4[2], Shin = new Matrix4x4[2], Foot = new Matrix4x4[2];
    public readonly Matrix4x4[] ThighLocal = new Matrix4x4[2], ShinLocal = new Matrix4x4[2], FootLocal = new Matrix4x4[2];

    // ---- Diagnostics (the self-test and the smoke log read these) ----
    public readonly float[] GroundY = new float[2], RayY = new float[2]; // smoothed and raw ground height under each foot (world)
    public readonly Vector3[] Target = new Vector3[2], Ankle = new Vector3[2], AnkleWorld = new Vector3[2];
    public readonly bool[] Reached = new bool[2], Planted = new bool[2], Lifted = new bool[2]; // Lifted: raised to stay within reach
    public float Phase, Speed, Run, Drop, Air, Moving;
    public int Rays;
    public bool GroundIk;
}

/// <summary>
/// Procedural legs for the third-person player model: a gait driven by the player's drawn velocity relative to their facing
/// (walk cycle at low speed, run cycle at sprint speed, blended by speed with one continuous phase), crouch-walk, prone
/// crawl, idle stance with foot shuffles when turning in place, jump tuck, falling, landing compression and a mantle pose;
/// two-bone IK per leg (<see cref="Arms.SolveTwoBone"/>, knee pole forward) and foot IK on the ground: one short ray per
/// foot per frame through <see cref="IWorldQuery"/> for players within <see cref="IkRange"/> of <see cref="Viewer"/>
/// (stairs, slopes, terrain, props, moving platforms), the pelvis lowered so both feet stay reachable and the feet tilted
/// a little to the slope. State is kept per player id; nothing allocates per frame.
/// </summary>
public static class LegAnimator
{
    // ---- Proportions (metres) ----
    public const float Thigh = 0.44f, Shin = 0.42f, AnkleHeight = 0.09f;

    /// <summary>The longest hip-to-ankle distance the gait asks for (a slight bend kept).</summary>
    public const float MaxReach = (Thigh + Shin) * 0.98f;

    /// <summary>How far the pelvis may sink to keep the feet in reach, and how much of that the upper body follows (keeps the head near the hit box).</summary>
    public const float MaxDrop = 0.6f, MaxUpperDrop = 0.25f;

    /// <summary>Hip joints below the pelvis frame.</summary>
    private const float HipDrop = 0.04f;

    /// <summary>Pelvis height per stance (stand, crouch); prone has its own fixed frame.</summary>
    public const float StandPelvis = 0.9f, CrouchPelvis = 0.56f;

    /// <summary>Crouching, the hips sit this far back (so the knees stay near the body).</summary>
    public const float CrouchHipBack = 0.08f;

    // ---- Gait parameters: walk (at or below walking speed) vs run (at sprint speed) ----
    public const float WalkDuty = 0.58f, RunDuty = 0.32f; // fraction of the cycle a foot is planted
    public const float WalkLift = 0.11f, RunLift = 0.32f; // swing foot height
    public const float WalkBob = 0.025f, RunBob = 0.06f; // hip bob (walk: high at mid-stance; run: low at mid-stance)
    public const float WalkLean = 0.05f, RunLean = 0.2f; // forward lean of the upper body (radians)
    public const float RunCrouch = 0.08f; // the hips sit a little lower when sprinting

    /// <summary>Cycles per second at a speed (standing): 1 + 0.45 s up to walking speed (3.7 Hz at 6 m/s), then up to 5 Hz at sprint.</summary>
    public static float Cadence(float speed) =>
        speed <= World.WalkSpeed ? 1f + 0.45f * speed : 1f + 0.45f * World.WalkSpeed + (speed - World.WalkSpeed) * (1.3f / (World.SprintSpeed - World.WalkSpeed));

    /// <summary>Crouch-walk cadence.</summary>
    public static float CrouchCadence(float speed) => 1.3f + 0.55f * speed;

    /// <summary>How much of the run cycle to use at a speed: 0 at walking speed and below, 1 at sprint speed (smoothstep).</summary>
    public static float RunWeight(float speed)
    {
        var x = Math.Clamp((speed - World.WalkSpeed) / (World.SprintSpeed - World.WalkSpeed), 0f, 1f);
        return x * x * (3 - 2 * x);
    }

    /// <summary>The boxes, in draw order: pelvis, then per leg thigh, knee pad, shin, boot, sole.</summary>
    public static readonly LegPart PelvisPart = new(0, new Vector3(0.56f, 0.32f, 0.4f), new Vector3(0, 0.07f, 0), 0); // tall enough to fill the gap when the hips drop below the torso

    public static readonly LegPart[] LegParts =
    [
        new(1, new Vector3(0.22f, 0.22f, Thigh + 0.06f), new Vector3(0, 0, -Thigh / 2), 1),
        new(2, new Vector3(0.2f, 0.2f, 0.16f), new Vector3(0, 0.03f, -0.02f), 2),
        new(2, new Vector3(0.18f, 0.18f, Shin), new Vector3(0, 0, -Shin / 2), 1),
        new(3, new Vector3(0.17f, 0.13f, 0.3f), new Vector3(0, -0.025f, -0.07f), 3),
        new(3, new Vector3(0.18f, 0.04f, 0.31f), new Vector3(0, -0.07f, -0.07f), 4),
    ];

    /// <summary>Where the camera is this frame: players farther than <see cref="IkRange"/> use the flat gait (no rays). NaN = everyone gets foot IK.</summary>
    public static Vector3 Viewer = new(float.NaN);

    public static float IkRange = 40f;

    /// <summary>Mover pose tick for the foot rays (the tick the movers are drawn at); NaN = the physics clock.</summary>
    public static double MoverTick = double.NaN;

    /// <summary>The last result (one shared instance, like <see cref="ThirdPersonArms"/>; read it right after Animate).</summary>
    public static readonly LegPose Pose = new();

    private struct State
    {
        public double Last;
        public bool Init;
        public Vector2 Vel; // smoothed velocity relative to facing: x = right, y = forward
        public float Phase, PelvisH, Drop, AirTime, Air, Fall, Mantle, Land, MinVy, Twist;
        public Stance Stance;
        public Vector3 Plant0, Plant1; // idle: planted feet (world)
        public float PlantYaw0, PlantYaw1; // idle: planted foot yaw (world)
        public int Stepping; // idle: the foot taking a step, -1 = none
        public float StepT;
        public Vector3 StepFrom;
        public float StepYawFrom;
        public float Ground0, Ground1; // smoothed ground height under each foot (world)
        public Vector3 Normal0, Normal1;
        public bool GroundInit;
        public bool Locked0, Locked1; // a planted foot is locked to its touchdown point (world)
        public Vector3 Lock0, Lock1;
        public Vector3 Off0, Off1; // after a lock is released: where the foot was vs the gait (body space), fading out
        public float OffT0, OffT1, OffDur0, OffDur1;
    }

    private static readonly State[] States = new State[256];

    // ---- Stats ----
    private static long _ticks;
    private static int _calls, _rays;

    /// <summary>One line for the smoke log: calls, rays and CPU per call since the last read.</summary>
    public static string Stats
    {
        get
        {
            var c = Math.Max(_calls, 1);
            var s = $"{_calls} leg poses, {_rays} foot rays ({_rays / (float)c:0.0} per pose), {_ticks * 1e6 / Stopwatch.Frequency / c:0.0} us per pose";
            _ticks = 0;
            _calls = _rays = 0;
            return s;
        }
    }

    private static bool _cacheValid, _cacheGrounded, _cacheMantling;
    private static byte _cacheId;
    private static double _cacheNow;
    private static Vector3 _cachePos, _cacheVel;
    private static float _cacheYaw;
    private static Stance _cacheStance;

    /// <summary>Forgets a player's leg state (the next pose starts fresh).</summary>
    public static void Reset(byte id)
    {
        States[id].Init = false;
        _cacheValid = false;
    }

    /// <summary>
    /// Animates <paramref name="p"/>'s legs at <paramref name="now"/> (the game clock) into <see cref="Pose"/>. Calling it
    /// twice at the same time gives the same pose (state only advances with time).
    /// </summary>
    public static LegPose Animate(Player p, double now)
    {
        // The same player at the same moment again (e.g. the scene graph and the reference drawing both posing it): same pose.
        if (_cacheValid && p.Id == _cacheId && now == _cacheNow && p.RenderPosition == _cachePos && p.RenderYaw == _cacheYaw && p.RenderStance == _cacheStance
            && p.RenderVelocity == _cacheVel && p.RenderGrounded == _cacheGrounded && p.RenderMantling == _cacheMantling && States[p.Id].Init)
            return Pose;
        _cacheValid = true;
        _cacheId = p.Id;
        _cacheNow = now;
        _cachePos = p.RenderPosition;
        _cacheYaw = p.RenderYaw;
        _cacheStance = p.RenderStance;
        _cacheVel = p.RenderVelocity;
        _cacheGrounded = p.RenderGrounded;
        _cacheMantling = p.RenderMantling;
        var t0 = Stopwatch.GetTimestamp();
        ref var st = ref States[p.Id];
        var pose = Pose;
        var pos = p.RenderPosition;
        var yaw = p.RenderYaw;
        var stance = p.RenderStance;
        var vel = p.RenderVelocity;

        var dt = (float)(now - st.Last);
        var fresh = !st.Init || dt > 0.5f || dt < 0;
        if (fresh)
        {
            st = default;
            st.Init = true;
            st.Stepping = -1;
            st.PelvisH = stance == Stance.Crouch ? CrouchPelvis : StandPelvis;
            st.Stance = stance;
            dt = 0;
        }
        st.Last = now;
        dt = MathF.Min(dt, 0.1f);

        var right = World.FlatRight(yaw);
        var fwd = World.FlatForward(yaw);
        var rotYaw = Matrix4x4.CreateRotationY(yaw); // world -> body (about the feet)
        var bodyRot = Matrix4x4.CreateRotationY(-yaw);

        // ---- Velocity relative to facing, smoothed (snapshot steps and tick blends don't jitter the gait) ----
        var lv = new Vector2(Vector3.Dot(vel, right), Vector3.Dot(vel, fwd));
        if (fresh)
            st.Vel = lv;
        else
            st.Vel = Vector2.Lerp(st.Vel, lv, 1f - MathF.Exp(-10f * dt));
        var speed = st.Vel.Length();
        var dir = speed > 1e-3f ? st.Vel / speed : new Vector2(0, 1);
        var moving = SmoothStep(0.25f, 1.0f, speed);

        // ---- Air, falling, landing, mantling ----
        var mantling = p.RenderMantling;
        var grounded = p.RenderGrounded || mantling;
        if (!grounded)
        {
            st.AirTime += dt;
            st.MinVy = MathF.Min(st.MinVy, vel.Y);
        }
        else
        {
            if (st.AirTime > 0.12f)
                st.Land = MathF.Max(st.Land, Math.Clamp(-st.MinVy / 14f, 0.15f, 1f));
            st.AirTime = 0;
            st.MinVy = 0;
        }
        st.Air = Approach(st.Air, st.AirTime > 0.1f && stance != Stance.Prone ? 1 : 0, dt * 8f);
        st.Fall = Approach(st.Fall, vel.Y < -3f ? 1 : 0, dt * 4f);
        st.Mantle = Approach(st.Mantle, mantling ? 1 : 0, dt * 10f);
        st.Land *= MathF.Exp(-7f * dt);

        // ---- Gait phase: cycles per second from speed, so stance feet move with the ground ----
        var crouch = stance == Stance.Crouch;
        var prone = stance == Stance.Prone;
        var run = crouch || prone ? 0f : RunWeight(speed);
        var lateral = MathF.Abs(dir.X);
        var strideK = float.Lerp(1f, 0.65f, lateral); // shorter, quicker steps sideways
        // ... and on slopes: the ground's rise along the direction of travel (from last frame's foot normals)
        var avgN = st.GroundInit ? Vector3.Normalize(st.Normal0 + st.Normal1) : Vector3.UnitY;
        var travel = right * dir.X + fwd * dir.Y;
        var rise = MathF.Abs(Vector3.Dot(avgN, travel)) / MathF.Max(avgN.Y, 0.3f); // tan of the slope along the travel
        strideK *= float.Lerp(1f, 0.55f, Math.Clamp(rise / 0.85f, 0f, 1f));
        var cadence = (prone ? speed / 0.7f : crouch ? CrouchCadence(speed) : Cadence(speed)) / strideK;
        var stride = speed / MathF.Max(cadence, 1e-3f);
        st.Phase += cadence * dt;
        st.Phase -= MathF.Floor(st.Phase);
        var duty = crouch ? WalkDuty : float.Lerp(WalkDuty, RunDuty, run);
        var lift = crouch ? 0.075f : float.Lerp(WalkLift, RunLift, run);
        var early = 0.3f * run; // running: the foot comes up early (heel kick), smoothly
        var sweep = duty * stride; // how far a planted foot travels (relative to the body) during its stance

        // Stance height, eased between stand and crouch
        if (stance != st.Stance)
        {
            if (stance == Stance.Prone || st.Stance == Stance.Prone)
                st.PelvisH = crouch ? CrouchPelvis : StandPelvis;
            st.Stance = stance;
        }
        var baseH = crouch ? CrouchPelvis : StandPelvis;
        st.PelvisH = fresh ? baseH : float.Lerp(st.PelvisH, baseH, 1f - MathF.Exp(-12f * dt));

        var rays = 0;
        var near = float.IsNaN(Viewer.X) || Vector3.DistanceSquared(Viewer, pos) <= IkRange * IkRange;
        pose.GroundIk = false;

        if (prone)
        {
            ProneLegs(ref st, pose, speed, moving);
        }
        else
        {
            // ---- Foot targets in body space ----
            Span<Vector3> target = stackalloc Vector3[2];
            Span<float> footYaw = stackalloc float[2];
            Span<float> footPitch = stackalloc float[2];
            Span<float> needW = stackalloc float[2]; // how much each foot pulls the pelvis down (a swing foot mid-air: not at all)
            var walkHipX = crouch ? 0.17f : 0.15f;
            var idleHipX = crouch ? 0.19f : 0.17f;
            for (var i = 0; i < 2; i++)
            {
                var side = i == 0 ? 1f : -1f;
                var u = st.Phase + i * 0.5f;
                u -= MathF.Floor(u);
                float along, up = 0, pitch = 0, pull = 1;
                bool planted;
                if (u < duty)
                {
                    along = sweep * (0.5f - u / duty);
                    planted = true;
                }
                else
                {
                    var s = (u - duty) / (1 - duty);
                    along = sweep * (-0.5f + s * s * (3 - 2 * s));
                    var arc = (MathF.Sin(MathF.PI * s) + early * MathF.Sin(MathF.Tau * s)) / (1f + 0.55f * early);
                    up = lift * arc;
                    pitch = -0.45f * run * arc;
                    pull = 0.5f + 0.5f * MathF.Cos(MathF.Tau * s);
                    planted = false;
                }
                var gait = new Vector3(side * walkHipX + dir.X * along, AnkleHeight + up, -dir.Y * along);
                // Idle: feet stay where they were planted (world) and shuffle back under the body when it turns or drifts.
                var idleZ = IdleZ(crouch, i);
                var idleLocal = Vector3.Transform((i == 0 ? st.Plant0 : st.Plant1) - pos, rotYaw);
                var idleYaw = World.WrapAngle((i == 0 ? st.PlantYaw0 : st.PlantYaw1) + yaw);
                if (fresh)
                {
                    idleLocal = new Vector3(side * idleHipX, AnkleHeight, idleZ);
                    idleYaw = -side * 0.08f;
                }
                idleLocal.Y = AnkleHeight;
                target[i] = Vector3.Lerp(idleLocal, gait, moving);
                footYaw[i] = float.Lerp(idleYaw, -side * 0.08f, moving);
                footPitch[i] = pitch * moving;
                pose.Planted[i] = planted && moving >= 0.999f;
                needW[i] = float.Lerp(1f, pull, moving);

                // Foot lock: a planted foot stays exactly where it touched down (the gait only decides when and where),
                // dragged along only if the body gets more than 20 cm away from where the gait wants it. When it lifts, the
                // difference fades out over the first part of the swing, so nothing pops.
                ref var locked = ref i == 0 ? ref st.Locked0 : ref st.Locked1;
                ref var lockAt = ref i == 0 ? ref st.Lock0 : ref st.Lock1;
                ref var off = ref i == 0 ? ref st.Off0 : ref st.Off1;
                ref var offT = ref i == 0 ? ref st.OffT0 : ref st.OffT1;
                ref var offDur = ref i == 0 ? ref st.OffDur0 : ref st.OffDur1;
                if (pose.Planted[i] && !fresh && st.Air < 0.001f && st.Mantle < 0.001f)
                {
                    var wT = Vector3.Transform(target[i] with { Y = 0 }, bodyRot) + pos;
                    if (!locked)
                    {
                        lockAt = wT;
                        locked = true;
                    }
                    var e = new Vector2(lockAt.X - wT.X, lockAt.Z - wT.Z);
                    var el = e.Length();
                    if (el > 0.2f)
                        lockAt = new Vector3(wT.X + e.X * 0.2f / el, 0, wT.Z + e.Y * 0.2f / el);
                    var l = Vector3.Transform(lockAt - pos, rotYaw);
                    target[i].X = l.X;
                    target[i].Z = l.Z;
                }
                else if (locked)
                {
                    locked = false;
                    var l = Vector3.Transform(lockAt - pos, rotYaw);
                    off = new Vector3(l.X - target[i].X, 0, l.Z - target[i].Z);
                    offT = 1;
                    offDur = Math.Clamp((1 - duty) / MathF.Max(cadence, 0.1f) * 0.7f, 0.05f, 0.15f);
                }
                if (offT > 0)
                {
                    target[i] += off * (offT * offT * (3 - 2 * offT));
                    offT = MathF.Max(0, offT - dt / offDur);
                }
            }

            if (st.Stepping >= 0 && moving < 0.999f)
            {
                // the stepping foot's position comes from UpdateIdle (lifted)
                var i = st.Stepping;
                var e = st.StepT * st.StepT * (3 - 2 * st.StepT);
                var side = i == 0 ? 1f : -1f;
                var idleZ = IdleZ(crouch, i);
                var home = new Vector3(side * idleHipX, AnkleHeight, idleZ);
                var from = Vector3.Transform(st.StepFrom - pos, rotYaw);
                from.Y = AnkleHeight;
                var stepPos = Vector3.Lerp(from, home, e) + new Vector3(0, 0.08f * MathF.Sin(MathF.PI * st.StepT), 0);
                target[i] = Vector3.Lerp(stepPos, target[i], moving);
                footYaw[i] = float.Lerp(float.Lerp(World.WrapAngle(st.StepYawFrom + yaw), -side * 0.08f, e), footYaw[i], moving);
            }

            // (after the step above, so a step cut short by moving off blends out instead of popping)
            UpdateIdle(ref st, pos, yaw, bodyRot, target, footYaw, moving, dt, crouch, idleHipX, fresh);

            // ---- Air and mantle poses ----
            if (st.Air > 0 || st.Mantle > 0)
            {
                for (var i = 0; i < 2; i++)
                {
                    var side = i == 0 ? 1f : -1f;
                    var tuck = new Vector3(side * 0.17f, 0.42f, i == 0 ? -0.1f : 0.06f);
                    var fall = new Vector3(side * 0.2f, 0.05f, i == 0 ? -0.18f : 0.16f);
                    var air = Vector3.Lerp(tuck, fall, st.Fall);
                    var mantle = i == 0 ? new Vector3(0.18f, 0.6f, -0.42f) : new Vector3(-0.17f, 0.1f, 0.16f);
                    target[i] = Vector3.Lerp(Vector3.Lerp(target[i], air, st.Air), mantle, st.Mantle);
                    footPitch[i] = float.Lerp(footPitch[i], -0.35f, MathF.Max(st.Air * (1 - st.Fall), st.Mantle * (i == 0 ? 1 : 0)));
                    pose.Planted[i] &= st.Air < 0.001f && st.Mantle < 0.001f;
                }
            }

            // ---- Foot IK on the ground ----
            var groundW = 1f - MathF.Max(st.Air, st.Mantle);
            Span<float> groundOff = stackalloc float[2];
            Span<Vector3> normal = stackalloc Vector3[2];
            var filter = QueryFilter.All.AtTick(MoverTick);
            var useRays = near && groundW > 0.001f;
            for (var i = 0; i < 2; i++)
            {
                var h = pos.Y;
                var n = Vector3.UnitY;
                if (useRays)
                {
                    var w = Vector3.Transform(new Vector3(target[i].X, 0, target[i].Z), bodyRot) + pos;
                    rays++;
                    // from above the highest step a foot can stand on (stairs two steps up at a sprint) to below the lowest
                    var top = crouch ? 0.95f : 1.1f;
                    if (World.Query.Raycast(new Vector3(w.X, pos.Y + top, w.Z), -Vector3.UnitY, top + 0.8f, filter, out var hit) && hit.Normal.Y > 0.5f)
                    {
                        h = hit.Point.Y;
                        n = hit.Normal;
                    }
                }
                ref var g = ref i == 0 ? ref st.Ground0 : ref st.Ground1;
                ref var gn = ref i == 0 ? ref st.Normal0 : ref st.Normal1;
                if (!st.GroundInit || fresh)
                {
                    g = h;
                    gn = n;
                }
                else
                {
                    var k = 1f - MathF.Exp(-25f * dt);
                    g = float.Lerp(g, h, k);
                    gn = Vector3.Lerp(gn, n, k);
                }
                groundOff[i] = Math.Clamp(g - pos.Y, -0.75f, 0.95f) * groundW;
                pose.GroundY[i] = g;
                pose.RayY[i] = h;
                normal[i] = Vector3.TransformNormal(Vector3.Normalize(Vector3.Lerp(Vector3.UnitY, gn, groundW)), rotYaw); // into body space
                target[i].Y += groundOff[i];
            }
            st.GroundInit = true;
            pose.GroundIk = useRays;

            // ---- Pelvis: bob, landing dip, then lowered until both feet are in reach ----
            var bobAmp = crouch ? 0.015f : float.Lerp(WalkBob, -RunBob, run);
            var hipY = st.PelvisH - RunCrouch * run * moving + bobAmp * moving * MathF.Cos(MathF.Tau * 2 * (st.Phase - duty / 2)) - 0.16f * st.Land;
            var hipZ = CrouchHipBack * Math.Clamp((StandPelvis - st.PelvisH) / (StandPelvis - CrouchPelvis), 0f, 1f);
            var drop = 0f;
            for (var i = 0; i < 2; i++)
            {
                var hipX = (i == 0 ? 1f : -1f) * 0.19f;
                var dx = target[i].X - hipX;
                var dz = target[i].Z - hipZ;
                var dy = hipY - HipDrop - target[i].Y;
                var room = MaxReach * MaxReach - dx * dx - dz * dz;
                var need = dy - MathF.Sqrt(MathF.Max(room, 0f));
                drop = MathF.Max(drop, need * needW[i]);
            }
            drop = Math.Clamp(drop, 0f, MaxDrop);
            // Eased a little: the need is steep when a foot is near full stretch (end of stance). The lag shows as a
            // slight heel lift, which the reach clamp below takes care of.
            st.Drop = fresh ? drop : float.Lerp(st.Drop, drop, 1f - MathF.Exp(-30f * dt));
            drop = st.Drop;
            hipY -= drop;
            var twistT = float.Lerp(0.07f, 0.12f, run) * moving * MathF.Sin(MathF.Tau * st.Phase) * MathF.Max(dir.Y * dir.Y, 0.3f);
            pose.Pelvis = Matrix4x4.CreateRotationY(twistT) * Matrix4x4.CreateTranslation(0, hipY, hipZ);
            pose.Drop = drop;

            // Upper body: follows the hips' height and leans into the run (less when backing up, a little into strafes).
            var leanAmt = float.Lerp(WalkLean, RunLean, run) * MathF.Min(speed / World.WalkSpeed, 1f) * moving;
            var lean = -leanAmt * (dir.Y >= 0 ? dir.Y : dir.Y * 0.4f);
            var roll = -0.04f * dir.X * MathF.Min(speed / World.WalkSpeed, 1f) * moving;
            lean -= 0.12f * st.Air * (1 - st.Fall) + 0.15f * st.Mantle;
            pose.Upper = Matrix4x4.CreateTranslation(0, -baseH, 0) * Matrix4x4.CreateRotationX(lean) * Matrix4x4.CreateRotationZ(roll)
                         * Matrix4x4.CreateTranslation(0, baseH + MathF.Max(hipY - baseH, -MaxUpperDrop), 0);

            // ---- Two-bone IK per leg, knee forward ----
            for (var i = 0; i < 2; i++)
            {
                var side = i == 0 ? 1f : -1f;
                var hip = Vector3.Transform(new Vector3(side * 0.19f, -HipDrop, 0), pose.Pelvis);
                // Still out of reach after the pelvis drop (a foot far down a staircase): lift the foot to where the leg reaches.
                var d = target[i] - hip;
                var dxz2 = d.X * d.X + d.Z * d.Z;
                const float reach = MaxReach * 1.01f;
                pose.Lifted[i] = d.LengthSquared() > reach * reach;
                if (pose.Lifted[i])
                    target[i] = dxz2 < reach * reach ? target[i] with { Y = hip.Y - MathF.Sqrt(reach * reach - dxz2) } : hip + d * (reach / d.Length());
                var pole = Vector3.Normalize(new Vector3(side * 0.15f, 0.1f * st.Air, -1f));
                Solve(pose, i, hip, target[i], pole, FootFrame(footYaw[i], footPitch[i], normal[i]));
            }
        }

        // ---- Locals for the scene graph / immediate path ----
        pose.PelvisLocal = pose.Pelvis;
        var invPelvis = ArmNodes.RigidInverse(pose.Pelvis);
        var body = bodyRot * Matrix4x4.CreateTranslation(pos);
        for (var i = 0; i < 2; i++)
        {
            pose.ThighLocal[i] = pose.Thigh[i] * invPelvis;
            pose.ShinLocal[i] = pose.Shin[i] * ArmNodes.RigidInverse(pose.Thigh[i]);
            pose.FootLocal[i] = pose.Foot[i] * ArmNodes.RigidInverse(pose.Shin[i]);
            pose.AnkleWorld[i] = Vector3.Transform(pose.Ankle[i], body);
        }
        pose.Phase = st.Phase;
        pose.Speed = speed;
        pose.Run = run;
        pose.Air = MathF.Max(st.Air, st.Mantle);
        pose.Moving = moving;
        pose.Rays = rays;
        _rays += rays;
        _calls++;
        _ticks += Stopwatch.GetTimestamp() - t0;
        return pose;
    }

    /// <summary>Idle feet: planted in the world; when the body turns or drifts away from one, it steps back under the hips.</summary>
    private static void UpdateIdle(ref State st, Vector3 pos, float yaw, in Matrix4x4 bodyRot, Span<Vector3> target, Span<float> footYaw, float moving, float dt,
        bool crouch, float idleHipX, bool fresh)
    {
        if (moving > 0 || fresh)
        {
            // Moving: the idle feet follow the gait, so stopping leaves them where they were.
            st.Plant0 = Vector3.Transform(target[0], bodyRot) + pos;
            st.Plant1 = Vector3.Transform(target[1], bodyRot) + pos;
            st.PlantYaw0 = World.WrapAngle(footYaw[0] - yaw);
            st.PlantYaw1 = World.WrapAngle(footYaw[1] - yaw);
            st.Stepping = -1;
            return;
        }
        if (st.Stepping >= 0)
        {
            st.StepT += dt / 0.24f;
            if (st.StepT < 1f)
                return;
            // landed: plant it at home
            var i = st.Stepping;
            var side = i == 0 ? 1f : -1f;
            var home = new Vector3(side * idleHipX, AnkleHeight, IdleZ(crouch, i));
            var w = Vector3.Transform(home, bodyRot) + pos;
            if (i == 0)
            {
                st.Plant0 = w;
                st.PlantYaw0 = World.WrapAngle(-side * 0.08f - yaw);
            }
            else
            {
                st.Plant1 = w;
                st.PlantYaw1 = World.WrapAngle(-side * 0.08f - yaw);
            }
            st.Stepping = -1;
            target[i] = home;
            footYaw[i] = -side * 0.08f;
            return;
        }
        // The foot farthest from home (position or turn) steps first.
        var worst = -1;
        var worstErr = 0f;
        for (var i = 0; i < 2; i++)
        {
            var side = i == 0 ? 1f : -1f;
            var home = new Vector3(side * idleHipX, AnkleHeight, IdleZ(crouch, i));
            var d = new Vector2(target[i].X - home.X, target[i].Z - home.Z).Length();
            var yawErr = MathF.Abs(World.WrapAngle(footYaw[i] + side * 0.08f));
            var err = MathF.Max(d / 0.2f, yawErr / 0.55f);
            if (err > 1f && err > worstErr)
            {
                worst = i;
                worstErr = err;
            }
        }
        if (worst < 0)
            return;
        st.Stepping = worst;
        st.StepT = 0;
        st.StepFrom = worst == 0 ? st.Plant0 : st.Plant1;
        st.StepYawFrom = worst == 0 ? st.PlantYaw0 : st.PlantYaw1;
    }

    /// <summary>Prone: legs straight back, knees on the floor; the crawl draws one knee up and out at a time.</summary>
    private static void ProneLegs(ref State st, LegPose pose, float speed, float moving)
    {
        var crawl = MathF.Min(speed / 1.2f, 1f) * moving;
        var sway = 0.06f * crawl * MathF.Sin(MathF.Tau * st.Phase);
        pose.Pelvis = Matrix4x4.CreateRotationY(sway) * Matrix4x4.CreateTranslation(0, 0.18f, -0.24f);
        pose.Upper = Matrix4x4.CreateRotationY(-sway * 0.5f);
        pose.Drop = 0;
        // toes back, soles up
        var foot = Matrix4x4.CreateRotationY(MathF.PI) * Matrix4x4.CreateRotationZ(MathF.PI) * Matrix4x4.CreateRotationX(0.6f); // toes back and down
        for (var i = 0; i < 2; i++)
        {
            var side = i == 0 ? 1f : -1f;
            var u = st.Phase + i * 0.5f;
            var c = MathF.Max(0f, MathF.Sin(MathF.Tau * u)) * crawl;
            var target = new Vector3(side * (0.14f + 0.14f * c), 0.13f + 0.04f * c, 0.6f - 0.2f * c);
            var hip = Vector3.Transform(new Vector3(side * 0.13f, 0, 0), pose.Pelvis);
            var pole = Vector3.Normalize(new Vector3(side * (0.3f + 0.9f * c), -1f, 0));
            pose.Planted[i] = false;
            Solve(pose, i, hip, target, pole, foot);
        }
    }

    private static void Solve(LegPose pose, int i, Vector3 hip, Vector3 target, Vector3 pole, in Matrix4x4 footRot)
    {
        pose.Target[i] = target;
        pose.Reached[i] = Arms.SolveTwoBone(hip, target, Thigh, Shin, pole, out var knee, out var ankle);
        pose.Thigh[i] = Arms.BoneFrame(hip, knee, pole);
        pose.Shin[i] = Arms.BoneFrame(knee, ankle, pole);
        var f = footRot;
        f.Translation = ankle;
        pose.Foot[i] = f;
        pose.Ankle[i] = ankle;
    }

    /// <summary>A foot's rotation in body space: its yaw, toe pitch, then tilted (a little) to the ground normal.</summary>
    private static Matrix4x4 FootFrame(float yaw, float pitch, Vector3 normalBody)
    {
        var slopePitch = Math.Clamp(MathF.Atan2(normalBody.Z, normalBody.Y) * 0.6f, -0.35f, 0.35f);
        var slopeRoll = Math.Clamp(-MathF.Atan2(normalBody.X, normalBody.Y) * 0.6f, -0.35f, 0.35f);
        return Matrix4x4.CreateRotationY(yaw) * Matrix4x4.CreateRotationX(pitch + slopePitch) * Matrix4x4.CreateRotationZ(slopeRoll);
    }

    /// <summary>Idle foot placement front-to-back (crouching: one foot back, knee forward).</summary>
    private static float IdleZ(bool crouch, int i) => crouch ? (i == 0 ? 0.2f : -0.05f) : (i == 0 ? 0.03f : -0.03f);

    private static float Approach(float v, float target, float step) => v < target ? MathF.Min(v + step, target) : MathF.Max(v - step, target);

    private static float SmoothStep(float a, float b, float x)
    {
        var t = Math.Clamp((x - a) / (b - a), 0f, 1f);
        return t * t * (3 - 2 * t);
    }
}
