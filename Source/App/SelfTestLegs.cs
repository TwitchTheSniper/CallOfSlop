using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>The "Legs" self-test section: leg IK, the gait, foot planting, foot IK on stairs and slopes, hitboxes, cost.</summary>
public static partial class SelfTest
{
    private static void CheckLegs()
    {
        var hitboxesBefore = HitboxFingerprint();
        var savedViewer = LegAnimator.Viewer;
        var savedTick = LegAnimator.MoverTick;
        LegAnimator.Viewer = new Vector3(float.NaN);
        LegAnimator.MoverTick = double.NaN;
        var savedMap = World.Map;
        var savedMover = World.Mover;
        try
        {
            World.Mover = ClassicMover.Instance;
            SetLab(PhysicsLab());
            LegsIk();
            LegsShape();
            LegsPlanting();
            LegsContinuity();
            LegsIdleAndAir();
            LegsOnStairsAndSlopes();
            LegsCost();
        }
        finally
        {
            LegAnimator.Viewer = savedViewer;
            LegAnimator.MoverTick = savedTick;
            World.Mover = savedMover;
            World.SetMap(savedMap);
        }
        var hitboxesAfter = HitboxFingerprint();
        if (hitboxesAfter != hitboxesBefore)
            Fail($"legs: hit volumes changed while animating ({hitboxesBefore:X16} -> {hitboxesAfter:X16})");
        var stand = World.Hitbox(Vector3.Zero, Stance.Stand, 0);
        var crouch = World.Hitbox(Vector3.Zero, Stance.Crouch, 0);
        var prone = World.Hitbox(Vector3.Zero, Stance.Prone, 0);
        if (stand.Half != new Vector3(0.4f, 0.9f, 0.4f) || crouch.Half != new Vector3(0.4f, 0.6f, 0.4f) || MathF.Abs(prone.Half.Z - 0.9f) > 1e-6f
            || World.HeightOf(Stance.Stand) != 1.8f || World.HeadStart != 1.4f || World.PlayerRadius != 0.4f)
            Fail($"legs: player hit/collision volumes are not the original ones (stand {stand.Half}, crouch {crouch.Half}, prone {prone.Half})");
        Line($"  hit volumes unchanged: fingerprint {hitboxesAfter:X16} before and after (stand half {stand.Half}, crouch {crouch.Half}, prone {prone.Half}; "
             + $"radius {World.PlayerRadius}, heights {World.HeightOf(Stance.Stand)}/{World.HeightOf(Stance.Crouch)}/{World.HeightOf(Stance.Prone)}, head from {World.HeadStart})");
    }

    /// <summary>A hash of every hit and collision volume answer over a grid of positions, yaws, stances and probe points.</summary>
    private static ulong HitboxFingerprint()
    {
        var h = 14695981039346656037UL;
        void Mix(float f)
        {
            h ^= BitConverter.SingleToUInt32Bits(f);
            h *= 1099511628211UL;
        }
        foreach (var stance in (ReadOnlySpan<Stance>)[Stance.Stand, Stance.Crouch, Stance.Prone])
        {
            Mix(World.HeightOf(stance));
            Mix(World.EyeOf(stance));
            for (var k = 0; k < 8; k++)
            {
                var feet = new Vector3(k * 1.7f - 5, k * 0.3f, 3 - k);
                var yaw = k * 0.8f;
                var b = World.Hitbox(feet, stance, yaw);
                Mix(b.Center.X); Mix(b.Center.Y); Mix(b.Center.Z); Mix(b.Half.X); Mix(b.Half.Y); Mix(b.Half.Z);
                for (var q = 0; q < 12; q++)
                {
                    var pt = feet + new Vector3(MathF.Sin(q) * 0.5f, q * 0.16f, MathF.Cos(q * 1.3f) * 0.8f);
                    Mix(World.IsHeadshot(feet, stance, yaw, pt) ? 1 : 0);
                }
            }
        }
        return h;
    }

    private static Player LegPlayer(byte id, Vector3 at, Stance stance = Stance.Stand)
    {
        var p = new Player { Id = id };
        p.Teleport(at);
        p.RenderStance = stance;
        p.RenderGrounded = true;
        LegAnimator.Reset(id);
        return p;
    }

    // ---- Two-bone IK -------------------------------------------------------------------------------------

    private static void LegsIk()
    {
        var rng = new Random(11);
        float worst = 0, worstKnee = 1;
        int reached = 0, n = 2000, clampedOk = 0;
        for (var i = 0; i < n; i++)
        {
            var hip = new Vector3(0.19f, 0.86f, 0);
            var dir = Vector3.Normalize(new Vector3((float)rng.NextDouble() - 0.5f, -1f - (float)rng.NextDouble(), (float)rng.NextDouble() - 0.5f));
            var dist = 0.05f + (float)rng.NextDouble() * (LegAnimator.MaxReach - 0.05f);
            var target = hip + dir * dist;
            var pole = Vector3.Normalize(new Vector3(0.15f, 0, -1f));
            if (Arms.SolveTwoBone(hip, target, LegAnimator.Thigh, LegAnimator.Shin, pole, out var knee, out var ankle))
                reached++;
            worst = MathF.Max(worst, Vector3.Distance(ankle, target));
            // bone lengths kept, knee bent towards the pole (forward)
            worst = MathF.Max(worst, MathF.Abs(Vector3.Distance(hip, knee) - LegAnimator.Thigh));
            worst = MathF.Max(worst, MathF.Abs(Vector3.Distance(knee, ankle) - LegAnimator.Shin));
            var mid = (hip + ankle) / 2;
            var bend = Vector3.Dot(knee - mid, pole);
            if (dist < LegAnimator.MaxReach * 0.95f)
                worstKnee = MathF.Min(worstKnee, bend);
            // out of reach: straight leg along the line to the target
            var far = hip + dir * 1.5f;
            if (!Arms.SolveTwoBone(hip, far, LegAnimator.Thigh, LegAnimator.Shin, pole, out _, out var a2)
                && MathF.Abs(Vector3.Distance(hip, a2) - (LegAnimator.Thigh + LegAnimator.Shin)) < 1e-3f && Vector3.Dot(Vector3.Normalize(a2 - hip), dir) > 0.9999f)
                clampedOk++;
        }
        if (reached != n || worst > 1e-4f)
            Fail($"legs IK: {reached}/{n} targets reached, worst error {worst * 1000:0.000} mm");
        if (worstKnee <= 0)
            Fail($"legs IK: a knee bent backwards (offset towards the pole {worstKnee:0.000} m)");
        if (clampedOk != n)
            Fail($"legs IK: out-of-reach targets not clamped onto a straight leg ({clampedOk}/{n})");
        Line($"  IK: {reached}/{n} targets within reach hit, worst error {worst * 1000:0.0000} mm, knees always forward (min {worstKnee * 100:0.0} cm), "
             + $"{clampedOk}/{n} out-of-reach targets clamped straight");
    }

    // ---- Shape: the legs fill the old body box's space -----------------------------------------------------

    private static void LegsShape()
    {
        foreach (var stance in (ReadOnlySpan<Stance>)[Stance.Stand, Stance.Crouch, Stance.Prone])
        {
            var p = LegPlayer(200, new Vector3(0, 0, 20), stance);
            for (var f = 0; f < 60; f++)
                LegAnimator.Animate(p, 10 + f / 60.0);
            var legs = LegAnimator.Pose;
            Vector3 min = new(float.MaxValue), max = new(float.MinValue);
            void Add(in LegPart part, in Matrix4x4 bone)
            {
                for (var c = 0; c < 8; c++)
                {
                    var corner = new Vector3((c & 1) - 0.5f, ((c >> 1) & 1) - 0.5f, ((c >> 2) & 1) - 0.5f) * part.Size + part.Center;
                    var w = Vector3.Transform(corner, bone);
                    min = Vector3.Min(min, w);
                    max = Vector3.Max(max, w);
                }
            }
            Add(LegAnimator.PelvisPart, legs.Pelvis);
            for (var i = 0; i < 2; i++)
                foreach (var part in LegAnimator.LegParts)
                    Add(part, part.Bone switch { 1 => legs.Thigh[i], 2 => legs.Shin[i], _ => legs.Foot[i] });
            var hb = World.Hitbox(Vector3.Zero, stance, 0);
            var slack = 0.06f;
            var ok = min.Y >= (stance == Stance.Prone ? -0.1f : -0.03f) && max.Y <= hb.Max.Y && min.X >= hb.Min.X - slack && max.X <= hb.Max.X + slack
                     && min.Z >= hb.Min.Z - slack - (stance == Stance.Prone ? 0 : 0.1f) && max.Z <= hb.Max.Z + slack;
            if (!ok)
                Fail($"legs ({stance}, idle): drawn legs {min:0.00} .. {max:0.00} spill out of the hit box {hb.Min:0.00} .. {hb.Max:0.00}");
            Line($"  {stance,-6} idle legs span {min:0.00} .. {max:0.00} (hit box {hb.Min:0.00} .. {hb.Max:0.00}), feet reached {legs.Reached[0]}/{legs.Reached[1]}");
        }
    }

    // ---- Foot planting on flat ground ----------------------------------------------------------------------

    private static void LegsPlanting()
    {
        var cases = new (string Name, Stance Stance, float Speed, Vector2 Dir)[]
        {
            ("walk fwd", Stance.Stand, World.WalkSpeed, new(0, 1)),
            ("run fwd", Stance.Stand, World.SprintSpeed, new(0, 1)),
            ("jog fwd", Stance.Stand, 9.5f, new(0, 1)),
            ("walk back", Stance.Stand, World.WalkSpeed * 0.8f, new(0, -1)),
            ("strafe", Stance.Stand, World.WalkSpeed, new(1, 0)),
            ("diagonal", Stance.Stand, World.WalkSpeed, Vector2.Normalize(new(-1, 1))),
            ("slow (ADS)", Stance.Stand, 2.5f, new(0, 1)),
            ("crouch", Stance.Crouch, 3f, new(0, 1)),
        };
        const double dt = 1 / 120.0;
        Span<bool> was = stackalloc bool[2];
        Span<Vector3> start = stackalloc Vector3[2];
        foreach (var c in cases)
        {
            was.Clear();
            var yaw = 0.7f;
            var p = LegPlayer(201, new Vector3(-50, 0, 20), c.Stance);
            p.RenderYaw = yaw;
            var vel = (World.FlatRight(yaw) * c.Dir.X + World.FlatForward(yaw) * c.Dir.Y) * c.Speed;
            // keep inside the lab's open ground: walk back and forth along x (the body turns round, which resets the planting)
            p.RenderPosition = new Vector3(0, 0, 25) - vel * 2.5f;
            p.RenderVelocity = vel;
            var now = 100.0;
            float worstSlide = 0;
            int stances = 0, misses = 0, frames = 0;
            float cycles = 0, lastPhase = 0;
            for (var f = 0; f < 600; f++)
            {
                now += dt;
                p.RenderPosition += vel * (float)dt;
                var legs = LegAnimator.Animate(p, now);
                if (f < 180)
                {
                    lastPhase = legs.Phase;
                    continue; // settle (velocity smoothing)
                }
                frames++;
                var dp = legs.Phase - lastPhase;
                cycles += dp < 0 ? dp + 1 : dp;
                lastPhase = legs.Phase;
                for (var i = 0; i < 2; i++)
                {
                    if (!legs.Reached[i])
                        misses++;
                    if (legs.Planted[i])
                    {
                        if (!was[i])
                        {
                            start[i] = legs.AnkleWorld[i];
                            stances++;
                        }
                        worstSlide = MathF.Max(worstSlide, Vector3.Distance(start[i], legs.AnkleWorld[i]));
                    }
                    was[i] = legs.Planted[i];
                }
            }
            var secs = (float)(frames * dt);
            var stride = c.Speed / MathF.Max(cycles / secs, 1e-3f);
            if (worstSlide > 0.03f)
                Fail($"legs planting ({c.Name}, {c.Speed:0.0} m/s): a planted foot slid {worstSlide * 100:0.0} cm");
            if (misses > 0)
                Fail($"legs ({c.Name}): {misses} foot poses out of reach on flat ground");
            if (stances < 4)
                Fail($"legs ({c.Name}): only {stances} foot plants in {secs:0.0} s");
            Line($"  {c.Name,-10} {c.Speed,5:0.0} m/s: {cycles / secs:0.00} cycles/s, stride {stride:0.00} m, run weight {LegAnimator.Pose.Run:0.00}, "
                 + $"{stances} plants, worst slide {worstSlide * 1000:0.0} mm, out of reach {misses}, pelvis drop {LegAnimator.Pose.Drop * 100:0.0} cm");
        }
    }

    // ---- Walk <-> run blend: no pops ------------------------------------------------------------------------

    private static void LegsContinuity()
    {
        const double dt = 1 / 240.0;
        var p = LegPlayer(202, new Vector3(-55, 0, 30));
        p.RenderYaw = MathF.PI / 2; // facing +X
        var now = 300.0;
        Span<Vector3> prev = stackalloc Vector3[2];
        var prevUp = Vector3.Zero;
        float worstFoot = 0, worstHip = 0, worstRate = 0, prevRate = -1, worstAt = 0;
        var prevPhase = 0f;
        var total = 4800; // 0 -> sprint -> 0 over 20 s, plus a sudden walk -> sprint jump as the game does it
        for (var f = 0; f < total; f++)
        {
            var t = f * (float)dt;
            var speed = t < 8 ? World.SprintSpeed * t / 8 : t < 12 ? (t < 10 ? World.SprintSpeed : World.WalkSpeed) : MathF.Max(0, World.WalkSpeed * (1 - (t - 12) / 6));
            var vel = World.FlatForward(p.RenderYaw) * speed;
            now += dt;
            p.RenderPosition += vel * (float)dt;
            if (p.RenderPosition.X > 55)
                p.RenderPosition = p.RenderPosition with { X = -55 }; // (a teleport: skip the frame below)
            p.RenderVelocity = vel;
            var legs = LegAnimator.Animate(p, now);
            var up = legs.Upper.Translation;
            var dp = legs.Phase - prevPhase;
            if (dp < 0)
                dp += 1;
            var rate = dp / (float)dt;
            if (f > 0 && MathF.Abs(p.RenderPosition.X + 55) > 0.5f)
            {
                for (var i = 0; i < 2; i++)
                {
                    var d = Vector3.Distance(prev[i], legs.Ankle[i]);
                    if (d > worstFoot)
                        (worstFoot, worstAt) = (d, legs.Speed);
                }
                worstHip = MathF.Max(worstHip, Vector3.Distance(prevUp, up));
                if (prevRate >= 0)
                    worstRate = MathF.Max(worstRate, MathF.Abs(rate - prevRate));
            }
            prev[0] = legs.Ankle[0];
            prev[1] = legs.Ankle[1];
            prevUp = up;
            prevPhase = legs.Phase;
            prevRate = rate;
        }
        // At 240 fps a swing foot moves at most ~1.6x running speed relative to the body: about 9 cm a frame at sprint.
        if (worstFoot > 0.1f)
            Fail($"legs blend: a foot jumped {worstFoot * 100:0.0} cm in one 240 Hz frame (at {worstAt:0.0} m/s)");
        if (worstHip > 0.02f)
            Fail($"legs blend: the hips jumped {worstHip * 100:0.0} cm in one frame");
        if (worstRate > 0.2f)
            Fail($"legs blend: gait cadence jumped {worstRate:0.000} cycles/s between frames");
        Line($"  walk/run blend over 0..{World.SprintSpeed} m/s and back (240 Hz): worst foot step {worstFoot * 1000:0.0} mm/frame (at {worstAt:0.0} m/s), "
             + $"hips {worstHip * 1000:0.0} mm/frame, cadence change {worstRate * 1000:0.00} mHz/frame (phase continuous)");
    }

    // ---- Idle turning, jump, fall, landing, mantle ---------------------------------------------------------

    private static void LegsIdleAndAir()
    {
        const double dt = 1 / 120.0;
        var p = LegPlayer(203, new Vector3(10, 0, 40));
        var now = 500.0;
        for (var f = 0; f < 60; f++)
            LegAnimator.Animate(p, now += dt);
        var a0 = LegAnimator.Pose.AnkleWorld[0];
        var b0 = LegAnimator.Pose.AnkleWorld[1];
        int moves = 0;
        Span<Vector3> last = [a0, b0];
        Span<bool> moving = stackalloc bool[2];
        for (var f = 0; f < 240; f++)
        {
            p.RenderYaw = MathF.Min(f / 120f, 1f) * MathF.PI / 2; // turn 90 degrees in place over 1 s
            var legs = LegAnimator.Animate(p, now += dt);
            for (var i = 0; i < 2; i++)
            {
                var m = Vector3.Distance(last[i], legs.AnkleWorld[i]) > 1e-4f;
                if (m && !moving[i])
                    moves++;
                moving[i] = m;
                last[i] = legs.AnkleWorld[i];
            }
        }
        var home = Vector3.Transform(new Vector3(0.17f, LegAnimator.AnkleHeight, 0.03f), Matrix4x4.CreateRotationY(-p.RenderYaw)) + p.RenderPosition;
        var off = Vector2.Distance(new Vector2(home.X, home.Z), new Vector2(LegAnimator.Pose.AnkleWorld[0].X, LegAnimator.Pose.AnkleWorld[0].Z));
        if (moves < 2 || off > 0.25f)
            Fail($"legs idle turn: {moves} foot steps for a 90 degree turn, right foot {off * 100:0} cm from home afterwards");

        // Jump: tuck; fall: legs down; land: hips dip; mantle: a leg up.
        p.RenderVelocity = new Vector3(0, World.JumpSpeed, 0);
        p.RenderGrounded = false;
        for (var f = 0; f < 30; f++)
            LegAnimator.Animate(p, now += dt);
        var tuck = MathF.Min(LegAnimator.Pose.Ankle[0].Y, LegAnimator.Pose.Ankle[1].Y);
        p.RenderVelocity = new Vector3(0, -9f, 0);
        for (var f = 0; f < 60; f++)
            LegAnimator.Animate(p, now += dt);
        var fall = LegAnimator.Pose.Ankle[0].Y;
        var standY = LegAnimator.Pose.Upper.Translation.Y;
        p.RenderGrounded = true;
        p.RenderVelocity = Vector3.Zero;
        var dip = 0f;
        for (var f = 0; f < 60; f++)
        {
            LegAnimator.Animate(p, now += dt);
            dip = MathF.Min(dip, LegAnimator.Pose.Upper.Translation.Y);
        }
        var settled = LegAnimator.Pose.Upper.Translation.Y;
        p.RenderMantling = true;
        for (var f = 0; f < 30; f++)
            LegAnimator.Animate(p, now += dt);
        var mantle = LegAnimator.Pose.Ankle[0].Y - LegAnimator.Pose.Ankle[1].Y;
        p.RenderMantling = false;
        if (tuck < 0.3f)
            Fail($"legs jump: feet only tucked to {tuck:0.00} m");
        if (fall > 0.2f)
            Fail($"legs fall: feet at {fall:0.00} m while falling (should hang down)");
        if (dip > settled - 0.05f)
            Fail($"legs landing: hips dipped only {(settled - dip) * 100:0.0} cm after a 9 m/s fall");
        if (mantle < 0.3f)
            Fail($"legs mantle: lead foot only {mantle:0.00} m above the other");
        Line($"  idle: {moves} shuffle steps turning 90 degrees in place, right foot {off * 100:0} cm from home after; jump tuck feet at {tuck:0.00} m, "
             + $"falling {fall:0.00} m, landing dip {(settled - dip) * 100:0.0} cm, mantle lead foot +{mantle:0.00} m");
    }

    // ---- Foot IK against the physics query: stairs and ramps ----------------------------------------------

    private static void LegsOnStairsAndSlopes()
    {
        // The lab: a 0.4 m staircase at x = -22 and a 30 degree ramp at x = -12, both rising towards -Z from z = -3.
        Span<float> plantedFor = stackalloc float[2];
        // (the ramps are oriented boxes, which only the swept mover walks on)
        foreach (var (name, x, buttons, mover) in new (string, float, Buttons, IMover)[]
                 {
                     ("stairs walk", -22f, Buttons.Forward, ClassicMover.Instance), ("stairs sprint", -22f, Buttons.Forward | Buttons.Sprint, ClassicMover.Instance),
                     ("30 deg ramp", -12f, Buttons.Forward, SweptMover.Instance), ("40 deg ramp", -4f, Buttons.Forward, SweptMover.Instance),
                 })
        {
            World.Mover = mover;
            var p = LegPlayer(204, new Vector3(x, 0, 3));
            var s = new MoveState { Position = new Vector3(x, 0, 3), Grounded = true };
            var now = 700.0;
            float worst = 0, sum = 0, worstDrop = 0;
            int samples = 0, bad = 0, misses = 0, frames = 0;
            var maxY = 0f;
            plantedFor.Clear();
            for (var tick = 0; tick < 150; tick++)
            {
                var prevPos = s.Position;
                World.Simulate(ref s, new InputCmd { Buttons = buttons, Yaw = 0, SpeedPct = 100 });
                maxY = MathF.Max(maxY, s.Position.Y);
                for (var sub = 1; sub <= 2; sub++) // two rendered frames per tick (120 fps), tick-blended like the game
                {
                    now += World.TickDt / 2;
                    p.RenderPosition = Vector3.Lerp(prevPos, s.Position, sub / 2f);
                    p.RenderVelocity = s.Velocity;
                    p.RenderGrounded = s.Grounded;
                    p.RenderYaw = 0;
                    var legs = LegAnimator.Animate(p, now);
                    frames++;
                    worstDrop = MathF.Max(worstDrop, legs.Drop);
                    for (var i = 0; i < 2; i++)
                    {
                        if (!legs.Reached[i])
                            misses++;
                        plantedFor[i] = legs.Planted[i] ? plantedFor[i] + World.TickDt / 2 : 0;
                        if (plantedFor[i] < 0.06f)
                            continue; // just landed: the smoothed ground height is still settling
                        var a = legs.AnkleWorld[i];
                        var truth = World.Query.Raycast(new Vector3(a.X, a.Y + 0.7f, a.Z), -Vector3.UnitY, 2f, QueryFilter.All, out var hit) ? hit.Point.Y : float.NaN;
                        if (float.IsNaN(truth))
                            continue;
                        var err = MathF.Abs(a.Y - LegAnimator.AnkleHeight - truth);
                        worst = MathF.Max(worst, err);
                        sum += err;
                        samples++;
                        if (err > 0.05f)
                            bad++;
                    }
                }
            }
            if (maxY < 1.5f)
                Fail($"legs {name}: the test walker only climbed to {maxY:0.00} m (lab layout changed?)");
            if (samples < 20 || bad > samples / 20)
                Fail($"legs {name}: {bad}/{samples} planted feet more than 5 cm off the ground (worst {worst * 100:0.0} cm)");
            if (misses > frames / 50)
                Fail($"legs {name}: {misses} of {frames * 2} foot poses out of reach");
            Line($"  {name,-13}: climbed to {maxY:0.0} m, planted feet vs ground ray: mean {sum / Math.Max(samples, 1) * 1000:0.0} mm, worst {worst * 1000:0.0} mm "
                 + $"({samples} samples, {bad} over 5 cm), out of reach {misses}/{frames * 2}, max pelvis drop {worstDrop * 100:0.0} cm");
        }
    }

    // ---- Cost and allocations --------------------------------------------------------------------------------

    private static void LegsCost()
    {
        var players = new Player[8];
        var rigs = new PlayerRig[8];
        for (var k = 0; k < players.Length; k++)
        {
            players[k] = LegPlayer((byte)(205 + k), new Vector3(-30 + k * 7, 0, 20 + k), (Stance)(k % 3 == 2 ? 1 : 0));
            rigs[k] = new PlayerRig(players[k].Id);
        }
        var now = 900.0;
        void Frame(int f, bool rig)
        {
            now += 1 / 240.0;
            for (var k = 0; k < players.Length; k++)
            {
                var p = players[k];
                var speed = (f / 200 + k) % 3 switch { 0 => World.WalkSpeed, 1 => World.SprintSpeed, _ => 0.3f };
                p.RenderYaw = k + f * 0.002f;
                var vel = World.FlatForward(p.RenderYaw) * speed;
                p.RenderVelocity = vel;
                p.RenderPosition += vel / 240f;
                if (MathF.Abs(p.RenderPosition.X) > 50 || p.RenderPosition.Z > 55 || p.RenderPosition.Z < 5)
                    p.RenderPosition = new Vector3(-30 + k * 7, 0, 20 + k);
                if (rig)
                    rigs[k].Pose(p, now);
                else
                    LegAnimator.Animate(p, now);
            }
        }
        for (var f = 0; f < 1200; f++)
            Frame(f, true);
        _ = LegAnimator.Stats;
        const int frames = 1200;
        var sw = new Stopwatch();
        var sw2 = new Stopwatch();
        var before = GC.GetAllocatedBytesForCurrentThread();
        sw.Start();
        for (var f = 0; f < frames; f++)
            Frame(f, false);
        sw.Stop();
        var legsBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        var stats = LegAnimator.Stats;
        before = GC.GetAllocatedBytesForCurrentThread();
        sw2.Start();
        for (var f = 0; f < frames; f++)
            Frame(f, true);
        sw2.Stop();
        var rigBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        var perPlayer = sw.Elapsed.TotalMilliseconds * 1000 / (frames * players.Length);
        var perRig = sw2.Elapsed.TotalMilliseconds * 1000 / (frames * players.Length);
        if (legsBytes > 0 || rigBytes > 0)
            Fail($"legs allocate per frame: {legsBytes} bytes (animation) and {rigBytes} bytes (whole player rig pose) over {frames} frames of 8 players");
        if (perPlayer > 50)
            Fail($"legs cost {perPlayer:0.0} us per player per frame (budget 50 us)");

        // Far players: no rays.
        LegAnimator.Viewer = new Vector3(500, 0, 500);
        var p0 = players[0];
        LegAnimator.Animate(p0, now += 0.01);
        var farRays = LegAnimator.Pose.Rays;
        LegAnimator.Viewer = new Vector3(float.NaN);
        if (farRays != 0)
            Fail($"legs: a player {Vector3.Distance(new Vector3(500, 0, 500), p0.RenderPosition):0} m away still cast {farRays} foot rays");
        Line($"  cost: {perPlayer:0.00} us per player per frame for the legs (2 foot rays each), {perRig:0.00} us for the whole player rig pose; "
             + $"{legsBytes} + {rigBytes} bytes allocated over {frames} frames x 8 players; [legs] {stats}; players beyond {LegAnimator.IkRange:0} m: {farRays} rays");
        Line($"[legs] cost {perPlayer:0.00} us/player/frame (legs + 2 rays), {perRig:0.00} us/player/frame whole rig, 0-alloc {(legsBytes + rigBytes == 0 ? "yes" : "NO")}");
    }
}
