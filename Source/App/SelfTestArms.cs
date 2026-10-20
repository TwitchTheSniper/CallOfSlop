using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Self-test section "Arms": the two-bone IK (exact on reachable targets, clamped on unreachable ones, bone lengths kept),
/// the quaternion/matrix conventions the animator relies on, and a sweep of every weapon (stock and with the attachments
/// that change where the hands go) through every animation: no NaNs, hands on their anchors, the sights still lined up
/// when aiming, nothing in a magnified lens, third-person arms too, and the cost per update.
/// </summary>
public static partial class SelfTest
{
    private static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);

    private static bool Finite(in Matrix4x4 m) =>
        Finite(new Vector3(m.M11, m.M12, m.M13)) && Finite(new Vector3(m.M21, m.M22, m.M23)) && Finite(new Vector3(m.M31, m.M32, m.M33)) && Finite(m.Translation);

    private static void CheckArms()
    {
        // ---- Two-bone IK -----------------------------------------------------------------
        var rng = new Random(1234);
        Vector3 R3() => new((float)rng.NextDouble() * 2 - 1, (float)rng.NextDouble() * 2 - 1, (float)rng.NextDouble() * 2 - 1);
        int exact = 0, clamped = 0;
        for (var n = 0; n < 2000; n++)
        {
            var s = R3();
            var l1 = 0.2f + (float)rng.NextDouble() * 0.3f;
            var l2 = 0.2f + (float)rng.NextDouble() * 0.3f;
            var dir = Vector3.Normalize(R3() + new Vector3(0.01f, 0, 0));
            var dist = (float)rng.NextDouble() * (l1 + l2) * 1.5f;
            var t = s + dir * dist;
            var pole = R3();
            var ok = Arms.SolveTwoBone(s, t, l1, l2, pole, out var e, out var w);
            if (!Finite(e) || !Finite(w))
            {
                Fail($"IK gave NaN (dist {dist:0.000}, bones {l1:0.00}/{l2:0.00})");
                continue;
            }
            if (MathF.Abs((e - s).Length() - l1) > 1e-4f || MathF.Abs((w - e).Length() - l2) > 1e-4f)
                Fail($"IK changed the bone lengths: {(e - s).Length():0.0000}/{(w - e).Length():0.0000} vs {l1:0.0000}/{l2:0.0000}");
            var reachable = dist < l1 + l2 - 1e-3f && dist > MathF.Abs(l1 - l2) + 1e-3f;
            if (reachable)
            {
                exact++;
                if (!ok || (w - t).Length() > 1e-4f)
                    Fail($"IK missed a reachable target by {(w - t).Length() * 1000:0.00} mm (dist {dist:0.000}, bones {l1:0.00}/{l2:0.00})");
            }
            else if (dist > l1 + l2 + 1e-3f)
            {
                clamped++;
                var along = Vector3.Dot(w - s, dir);
                if (ok || MathF.Abs(along - (l1 + l2)) > 1e-3f || (w - (s + dir * along)).Length() > 1e-3f)
                    Fail($"IK didn't clamp an unreachable target onto the line at full reach (dist {dist:0.000}, reach {l1 + l2:0.000}, got {along:0.000})");
            }
        }
        Line($"  IK: {exact} reachable targets hit exactly, {clamped} unreachable ones clamped");

        // Conventions: quaternion concatenation matches the row-vector matrix order the animator builds the gun pose with.
        var qa = Quaternion.Concatenate(Quaternion.Concatenate(Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.4f), Quaternion.CreateFromAxisAngle(Vector3.UnitY, -0.7f)),
            Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.3f));
        var ma = Matrix4x4.CreateRotationX(0.4f) * Matrix4x4.CreateRotationY(-0.7f) * Matrix4x4.CreateRotationZ(0.3f);
        var probe = new Vector3(0.3f, -0.2f, 0.9f);
        if ((Vector3.Transform(probe, qa) - Vector3.Transform(probe, ma)).Length() > 1e-5f)
            Fail("quaternion order doesn't match the matrix order (hands would twist off the gun)");

        // ---- Every weapon through every animation ------------------------------------------
        var defs = new List<WeaponDef>();
        foreach (var w in Weapons.All)
        {
            defs.Add(w);
            foreach (var opt in Attachments.For(w))
                if (opt.Slot is AttachSlot.Underbarrel or AttachSlot.Magazine or AttachSlot.Optic or AttachSlot.Barrel)
                    defs.Add(Attachments.Apply(w, Attachments.With(0, opt.Slot, opt.Index)));
        }
        int frames = 0, problems = 0;
        var styles = new int[8];
        var anim = new ViewModelAnimator();
        var aspect = 16f / 9f;
        var lensWorst = 9f;
        string lensWho = "";
        foreach (var d in defs)
        {
            var a = ArmAnchors.For(d);
            if (d.Kit == 0)
                styles[(int)a.Style]++;
            var (g, sp, mw) = ArmAnchors.HandPoints(d);
            if (!Finite(g) || !Finite(sp) || !Finite(mw) || !Finite(a.Action) || !Finite(a.Muzzle))
                Fail($"{d.Name}: hand anchors aren't finite");
            if (d.Class != WeaponClass.Pistol && sp.Z >= g.Z - 0.05f)
                Fail($"{d.Name}: the support hand ({sp.Z:0.000}) isn't in front of the grip ({g.Z:0.000})");
            if (a.Muzzle.Z >= sp.Z && d.Class != WeaponClass.Pistol)
                Fail($"{d.Name}: the muzzle isn't in front of the support hand");

            foreach (var mode in ArmModes)
            {
                var steps = mode is "reload" or "melee" or "equip" or "mantle" ? 60 : 30;
                var i = BaseInput(d);
                anim = new ViewModelAnimator();
                for (var k = 0; k <= steps; k++)
                {
                    var f = k / (float)steps;
                    i.Now += 1.0 / 60;
                    switch (mode)
                    {
                        case "ads":
                            i.Aim = 1;
                            break;
                        case "sprint":
                            i.SprintHeld = true;
                            i.Velocity = new Vector3(0, 0, -World.SprintSpeed);
                            break;
                        case "jump":
                            i.Grounded = k < 5 || k > 20;
                            i.Velocity = new Vector3(0, i.Grounded ? 0 : 6 - (k - 5) * 0.8f, 0);
                            break;
                        case "reload":
                            i.Reload = f;
                            break;
                        case "melee":
                            i.Melee = f < 1 ? f : -1;
                            break;
                        case "equip":
                            i.Equip = 1 - f;
                            break;
                        case "fire":
                            i.Kick = new WeaponKick { Back = 0.05f * (1 - f), Up = 0.06f * (1 - f), Roll = 0.03f, Side = 0.004f };
                            break;
                        case "mantle":
                            i.MantleTicks = (byte)Math.Max(0, 20 - k / 2);
                            i.MantleTo = new Vector3(0, 1.2f, -0.8f);
                            i.Eye = new Vector3(0, 1.6f + f * 1.2f, -f * 0.8f);
                            i.Grounded = i.MantleTicks == 0;
                            break;
                    }
                    anim.Update(i);
                    frames++;
                    if (!CheckArmFrame(d, mode, f, anim, a))
                    {
                        problems++;
                        break; // one report per weapon and animation
                    }
                }

                // Aimed: the gun sits exactly where Program's sight alignment puts it.
                if (mode == "ads")
                {
                    var t = anim.GunPose.Translation;
                    if ((t - new Vector3(0, -GunModel.SightHeight(d), GunModel.AdsZ(d))).Length() > 1e-4f || MathF.Abs(anim.GunPose.M11 - 1) > 1e-5f || MathF.Abs(anim.GunPose.M33 - 1) > 1e-5f)
                        Fail($"{d.Name}: aimed gun pose is off the sight line ({t.X:0.0000}, {t.Y:0.0000}, {t.Z:0.0000})");
                    if (a.Magnified)
                    {
                        // Nothing of the arms near the middle of the screen (an ACOG's eyepiece is ~0.57 of the half-height).
                        foreach (var rig in (ReadOnlySpan<ArmRig>)[anim.Left, anim.Right])
                            foreach (var pt in (ReadOnlySpan<Vector3>)[rig.HandWorld.Translation, rig.Wrist, rig.Elbow])
                            {
                                if (pt.Z >= -0.01f)
                                    continue;
                                var nx = pt.X / (-pt.Z * MathF.Tan(35f * MathF.PI / 180) * aspect);
                                var ny = pt.Y / (-pt.Z * MathF.Tan(35f * MathF.PI / 180));
                                var r = MathF.Sqrt(nx * nx + ny * ny);
                                if (r < lensWorst)
                                    (lensWorst, lensWho) = (r, d.Name + " " + d.Optic);
                            }
                    }
                }
            }
        }
        if (lensWorst < 0.6f)
            Fail($"arms reach into a magnified optic's lens when aiming: {lensWho} at {lensWorst:0.00} of the half-height");
        Line($"  {defs.Count} weapon setups, {frames} animated frames, {problems} with problems; closest arm part to a magnified lens: {lensWorst:0.00} ({lensWho})");
        Line($"  reload styles: {string.Join(", ", Enum.GetValues<ReloadStyle>().Select(s => $"{s} {styles[(int)s]}"))}");

        // ---- Third person ----------------------------------------------------------------------
        var p = new Player { Id = 3 };
        p.Teleport(new Vector3(2, 0, 5));
        var tpBad = 0;
        foreach (var d in Weapons.All)
            foreach (var stance in (ReadOnlySpan<Stance>)[Stance.Stand, Stance.Crouch, Stance.Prone])
            {
                p.RenderStance = stance;
                ThirdPersonArms.OnReload(p.Id, d.Reload, 100);
                for (var k = 0; k < 40; k++)
                {
                    var now = 100 + k * d.Reload / 40.0;
                    if (k == 20)
                        ThirdPersonArms.OnMelee(p.Id, now);
                    p.RenderPosition = new Vector3(2, 0, 5 - k * 0.12f); // running
                    var body = Matrix4x4.CreateRotationY(-0.6f) * Matrix4x4.CreateTranslation(p.RenderPosition);
                    var y = stance == Stance.Prone ? 0.3f : stance == Stance.Crouch ? 0.82f : 1.3f;
                    var gunPose = Matrix4x4.CreateScale(1.3f) * Matrix4x4.CreateRotationX(0.2f) * Matrix4x4.CreateTranslation(0.3f, y, -0.3f) * body;
                    var pose = ThirdPersonArms.Animate(p, d, gunPose, body, now);
                    if (!Finite(pose) || !ThirdPersonFinite())
                    {
                        tpBad++;
                        if (tpBad < 5)
                            Fail($"{d.Name} ({stance}): third-person arms gave NaN");
                    }
                }
                p.RenderPosition = new Vector3(2, 0, 5);
                var still = Matrix4x4.CreateTranslation(p.RenderPosition);
                ThirdPersonArms.OnReload(p.Id, 0.1f, -100);
                ThirdPersonArms.Animate(p, d, Matrix4x4.CreateScale(1.3f) * Matrix4x4.CreateTranslation(0.3f, stance == Stance.Prone ? 0.3f : stance == Stance.Crouch ? 0.82f : 1.3f, -0.3f) * still, still, 200);
                if (!ThirdPersonReached())
                    Fail($"{d.Name} ({stance}): third-person hands can't reach the gun");
            }

        // ---- Cost ------------------------------------------------------------------------------
        var bench = BaseInput(Weapons.DefaultPrimary);
        var sw = Stopwatch.StartNew();
        const int runs = 20000;
        for (var k = 0; k < runs; k++)
        {
            bench.Now += 1.0 / 240;
            bench.Reload = (k % 240) / 240f;
            anim.Update(bench);
        }
        var us = sw.Elapsed.TotalMilliseconds * 1000 / runs;
        Line($"  first-person update: {us:0.00} us per frame (+ ~20 box draws for the arms)");
        if (us > 100)
            Fail($"the arms animation costs {us:0.0} us per frame");
    }

    private static readonly string[] ArmModes = ["idle", "ads", "sprint", "jump", "reload", "melee", "equip", "fire", "mantle"];

    private static ViewModelInput BaseInput(WeaponDef d) =>
        new() { Def = d, Now = 10, Dt = 1f / 60, Reload = -1, Melee = -1, Grounded = true, Eye = new Vector3(0, 1.6f, 0) };

    private static bool ThirdPersonFinite()
    {
        var (r, l) = ThirdPersonArms.Rigs;
        return Finite(r.HandWorld) && Finite(l.HandWorld) && Finite(r.Elbow) && Finite(l.Elbow);
    }

    private static bool ThirdPersonReached()
    {
        var (r, l) = ThirdPersonArms.Rigs;
        return r.Reached && l.Reached;
    }

    /// <summary>One animated frame: no NaNs, bone lengths kept, both hands where they belong.</summary>
    private static bool CheckArmFrame(WeaponDef d, string mode, float f, ViewModelAnimator anim, in ArmAnchorSet a)
    {
        var ok = true;
        if (!Finite(anim.GunPose))
        {
            Fail($"{d.Name} {mode} {f:0.00}: gun pose is NaN");
            return false;
        }
        Span<Vector3> anchors =
        [
            Vector3.Transform(a.Grip, anim.GunPose), Vector3.Transform(a.Support, anim.GunPose), Vector3.Transform(a.MagWell, anim.GunPose),
            Vector3.Transform(a.Action, anim.GunPose), Vector3.Transform(a.Muzzle, anim.GunPose), Vector3.Transform(a.SupportAds, anim.GunPose),
        ];
        foreach (var (rig, right) in (ReadOnlySpan<(ArmRig, bool)>)[(anim.Right, true), (anim.Left, false)])
        {
            if (!Finite(rig.HandWorld) || !Finite(rig.Elbow) || !Finite(rig.Shoulder) || !Finite(rig.UpperWorld) || !Finite(rig.ForeWorld))
            {
                Fail($"{d.Name} {mode} {f:0.00}: {(right ? "right" : "left")} arm is NaN");
                return false;
            }
            if (MathF.Abs((rig.Elbow - rig.Shoulder).Length() - rig.Upper) > 1e-3f || MathF.Abs((rig.Wrist - rig.Elbow).Length() - rig.Fore) > 1e-3f)
            {
                Fail($"{d.Name} {mode} {f:0.00}: {(right ? "right" : "left")} arm bones changed length");
                ok = false;
            }
            if (!rig.Reached)
            {
                Fail($"{d.Name} {mode} {f:0.00}: {(right ? "right" : "left")} hand can't reach its target");
                ok = false;
            }

            // Hands on the gun: exactly on their anchor while nothing is moving them, near one during the animations.
            var hand = rig.HandWorld.Translation;
            var nearest = float.MaxValue;
            foreach (var p in anchors)
                nearest = MathF.Min(nearest, (hand - p).Length());
            var held = mode is "idle" or "sprint" or "jump" or "fire" or "equip" || (mode == "melee" && (right || d.Class != WeaponClass.Pistol)) || (mode == "ads" && (right || !a.Magnified));
            if (!right && (mode == "mantle" || (mode == "melee" && d.Class == WeaponClass.Pistol)))
                continue; // planted on the ledge / let go for a pistol whip
            var limit = held ? 1e-3f : mode == "melee" ? 0.4f : 0.5f;
            if (nearest > limit)
            {
                Fail($"{d.Name} {mode} {f:0.00}: {(right ? "right" : "left")} hand is {nearest * 100:0.0} cm from the gun");
                ok = false;
            }
        }
        return ok;
    }
}
