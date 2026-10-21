using System.Numerics;

namespace CallOfSlop;

/// <summary>The "Head bob" self-test section: the first-person camera bob (Source/Render/HeadBob.cs) and its setting.</summary>
public static partial class SelfTest
{
    private const float BobDt = 1f / 240;

    private static HeadBobInput BobIn(float speed, bool grounded = true, Stance stance = Stance.Stand, float aim = 0, float strength = 1, bool mantling = false) =>
        new() { Dt = BobDt, Velocity = new Vector3(0, 0, -speed), Grounded = grounded, Stance = stance, Aim = aim, Strength = strength, Mantling = mantling, GaitPhase = float.NaN };

    /// <summary>Runs <paramref name="seconds"/> of frames and returns the largest |vertical| and |roll| seen.</summary>
    private static (float Y, float Roll) BobRun(HeadBob b, HeadBobInput i, float seconds)
    {
        float y = 0, r = 0;
        for (var t = 0f; t < seconds; t += i.Dt)
        {
            b.Update(i);
            y = MathF.Max(y, MathF.Abs(b.Offset.Y));
            r = MathF.Max(r, MathF.Abs(b.Roll));
        }
        return (y, r);
    }

    private static void CheckHeadBob()
    {
        HeadBobZeroCases();
        HeadBobAmplitude();
        HeadBobFrequency();
        HeadBobContinuity();
        HeadBobAimUnaffected();
        HeadBobViewModel();
        HeadBobDeterminism();
        HeadBobSettings();
    }

    // ---- No bob in the air, prone, mantling, fully aimed, or at 0% ----
    private static void HeadBobZeroCases()
    {
        var b = new HeadBob();
        BobRun(b, BobIn(World.SprintSpeed), 2f);
        var sprinting = MathF.Abs(b.Offset.Y) + MathF.Abs(b.Offset.X);
        var air = BobIn(World.SprintSpeed, grounded: false);
        air.Velocity.Y = 4f;
        BobRun(b, air, 0.15f);
        var inAir = b.Offset != Vector3.Zero || b.Roll != 0;
        var (airY, _) = BobRun(b, air, 0.5f);
        if (sprinting <= 0 || inAir || airY != 0)
            Fail($"head bob: airborne must give exactly no bob after the fade (sprinting {sprinting * 100:0.00} cm, 0.15 s up offset {b.Offset}, peak {airY * 100:0.000} cm)");

        b.Reset();
        BobRun(b, BobIn(World.WalkSpeed), 1f);
        BobRun(b, BobIn(1f, stance: Stance.Prone), 0.3f);
        var (proneY, proneR) = BobRun(b, BobIn(1f, stance: Stance.Prone), 1f);
        if (proneY != 0 || proneR != 0)
            Fail($"head bob: prone crawling still bobs ({proneY * 100:0.000} cm, roll {proneR:0.0000})");

        b.Reset();
        BobRun(b, BobIn(World.SprintSpeed), 1f);
        BobRun(b, BobIn(World.SprintSpeed, mantling: true), 0.15f);
        var (mantleY, _) = BobRun(b, BobIn(World.SprintSpeed, mantling: true), 0.5f);
        if (mantleY != 0)
            Fail($"head bob: mantling still bobs ({mantleY * 100:0.000} cm)");

        b.Reset();
        var (aimY, aimR) = BobRun(b, BobIn(World.SprintSpeed, aim: 1f), 2f);
        var (halfY, _) = BobRun(b, BobIn(World.SprintSpeed, aim: 0.5f), 2f);
        var (hipY, _) = BobRun(b, BobIn(World.SprintSpeed), 2f);
        if (aimY != 0 || aimR != 0 || !(halfY < hipY * 0.5f) || halfY <= 0)
            Fail($"head bob: aiming must steady the camera (full aim {aimY * 100:0.000} cm, half {halfY * 100:0.00} cm, hip {hipY * 100:0.00} cm)");

        b.Reset();
        var eye = new Vector3(3, 1.6f, -2);
        var moved = false;
        for (var f = 0; f < 600; f++)
        {
            b.Update(BobIn(f < 300 ? World.SprintSpeed : World.WalkSpeed, grounded: f % 97 > 10, strength: 0));
            moved |= b.Offset != Vector3.Zero || b.Roll != 0 || b.Apply(eye, 0.7f) != eye;
        }
        if (moved)
            Fail("head bob: the 0% setting must leave the camera exactly as it was");

        b.Reset();
        var (crouchY, _) = BobRun(b, BobIn(3f, stance: Stance.Crouch), 2f);
        b.Reset();
        var (standY, _) = BobRun(b, BobIn(3f), 2f);
        if (!(crouchY > 0.3f * standY && crouchY < 0.7f * standY))
            Fail($"head bob: crouch-walking should bob about half as much ({crouchY * 100:0.00} cm vs {standY * 100:0.00} cm standing at 3 m/s)");
        Line($"head bob: zero airborne / prone / mantling / fully aimed / 0% setting; half aim {halfY / hipY * 100:0}% of hip; crouch-walk {crouchY / standY * 100:0}% of standing");
    }

    // ---- Amplitude grows with speed, walk to sprint ----
    private static void HeadBobAmplitude()
    {
        var speeds = new[] { 0f, 2f, 4f, World.WalkSpeed, 7.5f, 9f, 10.5f, 12f, World.SprintSpeed };
        var prev = -1f;
        var mono = true;
        var text = "";
        float walkY = 0, walkR = 0, sprintY = 0, sprintR = 0;
        foreach (var s in speeds)
        {
            var b = new HeadBob();
            BobRun(b, BobIn(s), 2f);
            var (y, r) = BobRun(b, BobIn(s), 2f);
            if (s > 0 && !(y > prev) || s == 0 && y != 0)
                mono = false;
            prev = y;
            text += $" {s:0.#}:{y * 100:0.00}";
            if (s == World.WalkSpeed)
                (walkY, walkR) = (y, r);
            if (s == World.SprintSpeed)
                (sprintY, sprintR) = (y, r);
        }
        if (!mono)
            Fail($"head bob: the dip must grow monotonically with speed (m/s:cm{text})");
        var deg = 180f / MathF.PI;
        if (MathF.Abs(walkY - HeadBob.WalkVertical) > 0.002f || MathF.Abs(sprintY - HeadBob.SprintVertical) > 0.004f)
            Fail($"head bob: dip {walkY * 100:0.00} cm at a walk, {sprintY * 100:0.00} cm at a sprint (want ~{HeadBob.WalkVertical * 100:0.0} / ~{HeadBob.SprintVertical * 100:0.0})");
        if (MathF.Abs(walkR * deg - 0.2f) > 0.05f || MathF.Abs(sprintR * deg - 0.8f) > 0.1f)
            Fail($"head bob: roll {walkR * deg:0.00} deg at a walk, {sprintR * deg:0.00} deg at a sprint (want ~0.2 / ~0.8)");
        for (var s = 0f; s < 16f; s += 0.25f)
            if (HeadBob.VerticalAt(s + 0.25f) < HeadBob.VerticalAt(s) || HeadBob.RollAt(s + 0.25f) < HeadBob.RollAt(s) || HeadBob.SwayAt(s + 0.25f) < HeadBob.SwayAt(s))
                Fail($"head bob: the amplitude curves fall between {s} and {s + 0.25f} m/s");
        Line($"head bob: dip by speed (m/s:cm){text}; roll {walkR * deg:0.00} deg walk, {sprintR * deg:0.00} deg sprint");
    }

    // ---- Two dips per stride at the leg cadence, on the footfalls ----
    private static void HeadBobFrequency()
    {
        var text = "";
        foreach (var s in new[] { 3f, World.WalkSpeed, World.SprintSpeed })
        {
            var b = new HeadBob();
            BobRun(b, BobIn(s), 1.5f);
            int dips = 0, sways = 0;
            float y0 = b.Offset.Y, y1 = y0, x0 = b.Offset.X;
            const float secs = 6f;
            for (var t = 0f; t < secs; t += BobDt)
            {
                b.Update(BobIn(s));
                var y = b.Offset.Y;
                if (y1 < y0 && y1 <= y) // a local minimum: a dip
                    dips++;
                if (x0 < 0 && b.Offset.X >= 0)
                    sways++;
                (y0, y1) = (y1, y);
                x0 = b.Offset.X;
            }
            var want = LegAnimator.Cadence(s);
            var got = dips / secs / 2f;
            if (MathF.Abs(got - want) > want * 0.08f || MathF.Abs(sways / secs - want) > want * 0.1f)
                Fail($"head bob: at {s} m/s {dips / secs:0.00} dips/s and {sways / secs:0.00} sways/s, want 2 x and 1 x the stride rate {want:0.00}/s");
            text += $" {s:0.#} m/s: {dips / secs:0.0} dips/s ({want:0.00} strides/s);";
        }

        // Locked to the legs' gait phase: the deepest point comes just after each foot lands (phase 0 and 0.5).
        {
            var b = new HeadBob();
            var gait = 0.37f;
            var worst = 0f;
            int found = 0;
            float y0 = 0, y1 = 0, p1 = 0;
            for (var f = 0; f < 240 * 5; f++)
            {
                gait += LegAnimator.Cadence(World.SprintSpeed) * BobDt;
                gait -= MathF.Floor(gait);
                var i = BobIn(World.SprintSpeed);
                i.GaitPhase = gait;
                b.Update(i);
                var y = b.Offset.Y;
                if (f > 240 * 2 && y1 < y0 && y1 <= y)
                {
                    var lagged = p1 - HeadBob.SprintLag; // gait phase at the dip, minus the intended lag: should be a footfall
                    var e = lagged * 2 - MathF.Round(lagged * 2);
                    worst = MathF.Max(worst, MathF.Abs(e) / 2);
                    found++;
                }
                (y0, y1, p1) = (y1, y, gait);
            }
            if (found < 10 || worst > 0.02f)
                Fail($"head bob: dips not on the footfalls when locked to the leg gait ({found} dips, worst {worst:0.000} of a stride off)");
            text += $" gait-locked dips within {worst:0.000} stride of the footfalls";
        }

        // The legs report their phase for the camera, carried forward in time, and nothing for a stale or unknown player.
        var p = new Player { Id = 249, Name = "Bob" };
        p.Teleport(new Vector3(0, 0, 0));
        p.RenderVelocity = new Vector3(0, 0, -World.SprintSpeed);
        p.RenderGrounded = true;
        LegAnimator.Reset(p.Id);
        var saved = LegAnimator.Viewer;
        LegAnimator.Viewer = new Vector3(1e6f); // far away: no foot rays needed
        try
        {
            for (var f = 0; f < 30; f++)
            {
                p.RenderPosition += p.RenderVelocity / 60f;
                LegAnimator.Animate(p, 100 + f / 60.0);
            }
            var at = LegAnimator.GaitPhase(p.Id, 100 + 29 / 60.0);
            var later = LegAnimator.GaitPhase(p.Id, 100 + 30 / 60.0);
            var stepped = later - at - LegAnimator.Cadence(LegAnimator.Pose.Speed) / 60f;
            stepped -= MathF.Round(stepped);
            if (!(MathF.Abs(at - LegAnimator.Pose.Phase) < 1e-5f) || !(MathF.Abs(stepped) < 0.02f) || !float.IsNaN(LegAnimator.GaitPhase(p.Id, 101))
                || !float.IsNaN(LegAnimator.GaitPhase(248, 100)))
                Fail($"head bob: LegAnimator.GaitPhase {at:0.000} vs pose {LegAnimator.Pose.Phase:0.000}, next frame off by {stepped:0.000}, stale {LegAnimator.GaitPhase(p.Id, 101)}");
        }
        finally
        {
            LegAnimator.Viewer = saved;
            LegAnimator.Reset(p.Id);
        }
        Line($"head bob:{text}");
    }

    // ---- No pops: start, walk -> sprint, jump, land, stop, crouch, prone, aim ----
    private static void HeadBobContinuity()
    {
        var b = new HeadBob();
        (float From, float To, HeadBobInput In)[] script =
        [
            (0f, 0.5f, BobIn(0)),
            (0.5f, 1.5f, BobIn(World.WalkSpeed)),
            (1.5f, 3f, BobIn(World.SprintSpeed)),
            (3f, 3.6f, BobIn(World.SprintSpeed, grounded: false) with { Velocity = new Vector3(0, 3f, -World.SprintSpeed) }),
            (3.6f, 4.6f, BobIn(World.SprintSpeed)),
            (4.6f, 5.4f, BobIn(3f, stance: Stance.Crouch)),
            (5.4f, 6f, BobIn(1f, stance: Stance.Prone)),
            (6f, 6.8f, BobIn(World.WalkSpeed)),
            (6.8f, 7.4f, BobIn(World.WalkSpeed, aim: 1f)),
            (7.4f, 8f, BobIn(World.SprintSpeed, mantling: true)),
            (8f, 9f, BobIn(World.SprintSpeed)),
            (9f, 10f, BobIn(0)),
        ];
        // The fastest the bob itself moves at a sprint, with room for the ramps: anything above this in one frame is a pop.
        var maxRate = HeadBob.SprintVertical * MathF.Tau * 2 * LegAnimator.Cadence(World.SprintSpeed) * 1.6f;
        var maxRollRate = HeadBob.SprintRoll * MathF.Tau * LegAnimator.Cadence(World.SprintSpeed) * 2.2f;
        float worstY = 0, worstR = 0, landed = -1, stopped = 1;
        var prev = Vector3.Zero;
        var prevR = 0f;
        var where = "";
        var aim = 0f; // eased like Game.AimT
        foreach (var (from, to, input) in script)
            for (var t = from; t < to - 1e-4f; t += BobDt)
            {
                aim = input.Aim > aim ? MathF.Min(aim + BobDt / 0.25f, input.Aim) : MathF.Max(aim - BobDt / 0.25f, input.Aim);
                b.Update(input with { Aim = aim });
                var dy = MathF.Abs(b.Offset.Y - prev.Y) / BobDt;
                var dx = MathF.Abs(b.Offset.X - prev.X) / BobDt;
                var dr = MathF.Abs(b.Roll - prevR) / BobDt;
                if (MathF.Max(dy, dx) > worstY)
                {
                    worstY = MathF.Max(dy, dx);
                    where = $"{t:0.00} s";
                }
                worstR = MathF.Max(worstR, dr);
                prev = b.Offset;
                prevR = b.Roll;
                if (from == 3.6f && t >= 3.7f && landed < 0)
                    landed = MathF.Abs(b.Amplitude) / HeadBob.SprintVertical; // 0.1 s after landing: still easing back in
                if (from == 9f && t >= 9.9f)
                    stopped = MathF.Abs(b.Offset.Y);
            }
        if (worstY > maxRate || worstR > maxRollRate)
            Fail($"head bob: the camera pops: {worstY:0.00} m/s at {where} (limit {maxRate:0.00}), roll {worstR:0.000} rad/s (limit {maxRollRate:0.000})");
        if (!(landed < 0.6f))
            Fail($"head bob: the bob is back at {landed * 100:0}% only 0.1 s after landing (should ease in)");
        if (stopped > 0.0005f)
            Fail($"head bob: still {stopped * 100:0.00} cm of bob a second after stopping");
        Line($"head bob: script start/walk/sprint/jump/land/crouch/prone/aim/mantle/stop, fastest {worstY:0.00} m/s (limit {maxRate:0.00}), roll {worstR:0.000} rad/s; "
             + $"{landed * 100:0}% back 0.1 s after landing");
    }

    // ---- Presentation only: aim direction and the shot origin don't move ----
    private static void HeadBobAimUnaffected()
    {
        var p = new Player { Id = 248, Name = "Aim" };
        p.Teleport(new Vector3(2, 0, 5));
        var eye0 = p.RenderEye;
        const float yaw = 0.8f, pitch = -0.1f;
        var fwd0 = World.Forward(yaw, pitch);
        var b = new HeadBob();
        var maxOff = 0f;
        for (var f = 0; f < 480; f++)
        {
            b.Update(BobIn(World.SprintSpeed));
            var cam = b.Apply(p.RenderEye, yaw);
            maxOff = MathF.Max(maxOff, Vector3.Distance(cam, eye0));
        }
        if (p.RenderEye != eye0 || World.Forward(yaw, pitch) != fwd0 || maxOff <= 0.01f || maxOff > 0.06f)
            Fail($"head bob: the shot origin / aim moved, or the camera offset is off ({maxOff * 100:0.0} cm)");
        Line($"head bob: shot origin and aim direction unchanged; camera moves up to {maxOff * 100:0.0} cm from the eye at a sprint");
    }

    // ---- The gun rides on the camera: with head bob on, its own bob follows the camera's dips and is smaller ----
    private static void HeadBobViewModel()
    {
        var def = Weapons.DefaultPrimary;
        float Swing(float headBob)
        {
            var vm = new ViewModelAnimator();
            var b = new HeadBob();
            var vin = new ViewModelInput { Def = def, Now = 10, Dt = BobDt, Reload = -1, Melee = -1, Grounded = true, Eye = new Vector3(0, 1.6f, 0), Velocity = new Vector3(0, 0, -World.WalkSpeed) };
            float lo = 1e9f, hi = -1e9f;
            for (var f = 0; f < 240 * 4; f++)
            {
                b.Update(BobIn(World.WalkSpeed));
                vin.Now += BobDt;
                vin.HeadBob = b.Weight * headBob;
                vin.HeadBobPhase = b.DipPhase;
                vm.Update(vin);
                if (f < 240 * 2)
                    continue;
                var gy = vm.GunPose.Translation.Y;
                lo = MathF.Min(lo, gy);
                hi = MathF.Max(hi, gy);
            }
            return hi - lo;
        }
        // correlation of the gun's vertical bob with the camera's (after removing the mean)
        float Corr()
        {
            var vm = new ViewModelAnimator();
            var b = new HeadBob();
            var vin = new ViewModelInput { Def = def, Now = 10, Dt = BobDt, Reload = -1, Melee = -1, Grounded = true, Eye = new Vector3(0, 1.6f, 0), Velocity = new Vector3(0, 0, -World.WalkSpeed) };
            var gs = new List<float>();
            var cs = new List<float>();
            for (var f = 0; f < 240 * 4; f++)
            {
                b.Update(BobIn(World.WalkSpeed));
                vin.Now += BobDt;
                vin.HeadBob = b.Weight;
                vin.HeadBobPhase = b.DipPhase;
                vm.Update(vin);
                if (f >= 240 * 2)
                {
                    gs.Add(vm.GunPose.Translation.Y);
                    cs.Add(b.Offset.Y);
                }
            }
            float gm = gs.Average(), cm = cs.Average();
            double sxy = 0, sxx = 0, syy = 0;
            for (var j = 0; j < gs.Count; j++)
            {
                sxy += (gs[j] - gm) * (cs[j] - cm);
                sxx += (gs[j] - gm) * (gs[j] - gm);
                syy += (cs[j] - cm) * (cs[j] - cm);
            }
            return (float)(sxy / Math.Sqrt(Math.Max(sxx * syy, 1e-30)));
        }
        var off = Swing(0);
        var on = Swing(1);
        var corr = Corr();
        if (!(on < off * 0.7f) || !(corr > 0.8f))
            Fail($"head bob: the gun's own bob should ease off and follow the camera's dips (swing {off * 100:0.00} -> {on * 100:0.00} cm, correlation {corr:0.00})");
        Line($"head bob: gun's own vertical swing {off * 100:0.00} cm alone, {on * 100:0.00} cm with head bob, in step with the camera (correlation {corr:0.00})");
    }

    // ---- Same inputs, same bob ----
    private static void HeadBobDeterminism()
    {
        var a = new HeadBob();
        var c = new HeadBob();
        var same = true;
        var rng = new Random(7);
        for (var f = 0; f < 2000; f++)
        {
            var i = BobIn((float)rng.NextDouble() * 14f, grounded: rng.Next(20) != 0, stance: (Stance)(f / 400 % 3), aim: f % 600 > 500 ? 1 : 0);
            i.Dt = 1f / (60 + rng.Next(200));
            i.GaitPhase = f % 2 == 0 ? float.NaN : (f * 0.013f) % 1f;
            a.Update(i);
            c.Update(i);
            same &= a.Offset == c.Offset && a.Roll == c.Roll && a.DipPhase == c.DipPhase;
            if (!float.IsFinite(a.Offset.X + a.Offset.Y + a.Roll))
                same = false;
        }
        if (!same)
            Fail("head bob: two bobs fed the same frames disagree (or produced NaN)");
        Line("head bob: deterministic over 2000 random frames");
    }

    // ---- settings.txt: head_bob, old files load with the default ----
    private static void HeadBobSettings()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "callofslop-headbob-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            File.WriteAllLines(tmp, ["name=Old", "fov=90", "foliage=High"]); // a settings file from before head bob
            var old = Settings.Load(tmp);
            var s = new Settings { HeadBob = 35 };
            s.Save(tmp);
            var back = Settings.Load(tmp);
            File.WriteAllLines(tmp, ["head_bob=250"]);
            var high = Settings.Load(tmp);
            File.WriteAllLines(tmp, ["head_bob=junk"]);
            var junk = Settings.Load(tmp);
            if (new Settings().HeadBob != 100 || old.HeadBob != 100 || old.Fov != 90 || back.HeadBob != 35 || high.HeadBob != 100 || junk.HeadBob != 100)
                Fail($"head bob: settings default {new Settings().HeadBob}, old file {old.HeadBob}, saved 35 -> {back.HeadBob}, 250 -> {high.HeadBob}, junk -> {junk.HeadBob}");
        }
        finally
        {
            File.Delete(tmp);
        }
        Line("head bob: settings.txt head_bob saves and loads; older files get 100%");
    }
}
