using System.Numerics;

namespace CallOfSlop;

/// <summary>What the first-person animation is driven by this frame (all existing game state).</summary>
public struct ViewModelInput
{
    public WeaponDef Def;
    public double Now;
    public float Dt;

    /// <summary>0..1 aim-down-sights blend (Game.AimT).</summary>
    public float Aim;

    /// <summary>0..1 through the reload, or -1 (Game.ReloadProgress).</summary>
    public float Reload;

    /// <summary>0..1 through the melee swing, or -1 (Game.MeleeProgress).</summary>
    public float Melee;

    /// <summary>
    /// Puts the gun here in camera space instead of the animated pose (a pushed cart sits on the ground in the world: its
    /// pose is its world matrix times the view). The hands still solve onto its grip and support anchors.
    /// </summary>
    public Matrix4x4? PoseOverride;

    /// <summary>1 when the gun has just been drawn, falling to 0 when it's ready.</summary>
    public float Equip;

    public WeaponKick Kick;
    public Vector3 Velocity;
    public bool Grounded;
    public byte MantleTicks;
    public Vector3 MantleTo;
    public Vector3 Eye;
    public float Yaw, Pitch;
    public bool SprintHeld;

    /// <summary>
    /// How much camera head bob is on (<see cref="CallOfSlop.HeadBob.Weight"/>, 0 = none) and its dip phase
    /// (<see cref="CallOfSlop.HeadBob.DipPhase"/>). The gun rides on the camera, so with head bob on its own bob locks to
    /// the camera's footfalls and eases off instead of doubling the motion. 0 = the gun's own bob exactly as before.
    /// </summary>
    public float HeadBob, HeadBobPhase;
}

/// <summary>
/// Procedural first-person animation: works out the gun's pose in camera space (hip/ADS, idle sway, walk bob,
/// sprint, jump/land, equip, melee, reloads per <see cref="ReloadStyle"/>, mantling, recoil kick) and where both
/// hands go, then poses the two <see cref="ArmRig"/>s with IK. No allocations per frame.
/// Camera space: -Z forward, +X right, +Y up. Gun space as GunModel: origin = grip, -Z = muzzle.
/// </summary>
public sealed class ViewModelAnimator
{
    private const float Walk = World.WalkSpeed;

    // The shoulders are fixed to the camera (not to the screen edges), so wide aspect ratios just show more
    // of the sides instead of stretching the arms; the upper arms stay below the bottom of the view.
    public readonly ArmRig Right = new(+1, new Vector3(0.19f, -0.28f, 0.1f), 0.31f, 0.31f, new Vector3(0.55f, -1f, 0.25f), 0.22f, 0.085f);
    public readonly ArmRig Left = new(-1, new Vector3(-0.17f, -0.27f, -0.05f), 0.31f, 0.31f, new Vector3(-0.5f, -1f, 0.1f), 0.22f, 0.085f);

    /// <summary>The gun's pose this frame (pass to GunModel.Draw).</summary>
    public Matrix4x4 GunPose;

    public HandTarget RightHand, LeftHand;

    /// <summary>The off hand is carrying a magazine / shell / speedloader (drawn by <see cref="DrawArms"/>).</summary>
    public bool Carrying;

    /// <summary>
    /// Where the gun's own magazine is during a reload (gun space offset from its seat), and whether it's out of the gun
    /// altogether: box-magazine, pistol and LMG reloads drop the old one, the off hand brings the new one and seats it.
    /// Program copies these to <see cref="GunModel.MagOffset"/> / <see cref="GunModel.MagHidden"/> for the first-person draw.
    /// </summary>
    public Vector3 MagOffset;

    /// <inheritdoc cref="MagOffset"/>
    public bool MagHidden;

    public ArmAnchorSet Anchors;
    private WeaponDef? _anchorDef;

    // State
    private float _sprintW, _bobPhase, _landPos, _landVel, _prevVy, _mantleRecover;
    private bool _prevGrounded = true, _headBobLocked;
    private float _headBobPrev;
    private int _mantleStart;

    // ---- Key tables -------------------------------------------------------------------
    private enum At : byte
    {
        Support,
        Mag,
        Action,
        Muzzle,
        Grip,
    }

    /// <summary>A hand key: at reload time T the hand is on anchor A, offset by G (gun space) and C (camera space).</summary>
    private readonly record struct Key(float T, At A, Vector3 G, Vector3 C);

    private static Key K(float t, At a) => new(t, a, Vector3.Zero, Vector3.Zero);
    private static Key K(float t, At a, float gx, float gy, float gz) => new(t, a, new Vector3(gx, gy, gz), Vector3.Zero);
    private static Key KC(float t, At a, float cx, float cy, float cz) => new(t, a, Vector3.Zero, new Vector3(cx, cy, cz));

    private static readonly Key[] BoxKeys =
    [
        K(0, At.Support), K(0.05f, At.Support), K(0.2f, At.Mag), K(0.25f, At.Mag, 0, -0.02f, 0), K(0.3f, At.Mag), KC(0.45f, At.Mag, -0.06f, -0.32f, 0.12f),
        K(0.64f, At.Mag, 0, -0.07f, 0.01f), K(0.73f, At.Mag), K(0.76f, At.Mag, 0, -0.035f, 0), K(0.79f, At.Mag, 0, 0.005f, 0),
        K(0.82f, At.Mag), K(0.96f, At.Support), K(1, At.Support),
    ];

    private static readonly Key[] PistolKeys =
    [
        K(0, At.Support), K(0.04f, At.Support), KC(0.2f, At.Mag, -0.05f, -0.3f, 0.1f), K(0.4f, At.Mag, 0, -0.07f, 0.01f), K(0.52f, At.Mag),
        K(0.56f, At.Mag, 0, -0.02f, 0), K(0.6f, At.Mag), K(0.68f, At.Action, 0, 0, -0.03f), K(0.76f, At.Action, 0, 0, 0.005f),
        K(0.8f, At.Action, 0, 0.02f, 0.01f), K(0.95f, At.Support), K(1, At.Support),
    ];

    private static readonly Key[] RevolverKeys =
    [
        K(0, At.Support), K(0.03f, At.Support), K(0.14f, At.Mag), K(0.3f, At.Mag), KC(0.46f, At.Mag, -0.05f, -0.3f, 0.1f),
        K(0.66f, At.Mag, -0.05f, 0, 0), K(0.74f, At.Mag), K(0.8f, At.Mag, -0.01f, 0, 0), K(0.95f, At.Support), K(1, At.Support),
    ];

    private static readonly Key[] BreakKeys =
    [
        K(0, At.Support), K(0.12f, At.Support), KC(0.28f, At.Mag, -0.05f, -0.3f, 0.1f), K(0.48f, At.Mag, -0.03f, 0.03f, 0.05f), K(0.58f, At.Mag),
        K(0.64f, At.Mag, 0, 0.03f, 0), K(0.78f, At.Support), K(1, At.Support),
    ];

    private static readonly Key[] BoltKeys =
    [
        K(0, At.Support), K(0.18f, At.Support), K(0.28f, At.Mag), KC(0.38f, At.Mag, -0.06f, -0.32f, 0.12f), K(0.52f, At.Mag, 0, -0.07f, 0.01f), K(0.6f, At.Mag),
        K(0.64f, At.Mag, 0, -0.03f, 0), K(0.67f, At.Mag), K(0.8f, At.Support), K(1, At.Support),
    ];

    private static readonly Key[] LmgKeys =
    [
        K(0, At.Support), K(0.04f, At.Support), K(0.18f, At.Mag), K(0.24f, At.Mag, 0, -0.02f, 0), KC(0.38f, At.Mag, -0.12f, -0.36f, 0.1f),
        K(0.6f, At.Mag, -0.03f, -0.08f, 0), K(0.7f, At.Mag), K(0.8f, At.Action), K(0.84f, At.Action, 0, 0.03f, 0), K(0.87f, At.Action, 0, -0.005f, 0),
        K(0.9f, At.Action), K(0.98f, At.Support), K(1, At.Support),
    ];

    private static readonly Key[] MuzzleKeys =
    [
        K(0, At.Support), K(0.05f, At.Support), K(0.18f, At.Muzzle, 0, 0, -0.12f), K(0.28f, At.Muzzle, 0, 0, -0.02f), K(0.38f, At.Muzzle, 0, 0, -0.12f),
        K(0.48f, At.Muzzle, 0, 0, -0.02f), K(0.58f, At.Muzzle, 0, 0, -0.12f), K(0.68f, At.Muzzle, 0, 0, -0.02f), K(0.76f, At.Muzzle, 0, 0, -0.14f),
        K(0.92f, At.Support), K(1, At.Support),
    ];

    /// <summary>The grip hand during a bolt-action reload: up, back, (mag swap), forward, down.</summary>
    private static readonly Key[] BoltGripKeys =
    [
        K(0, At.Grip), K(0.06f, At.Grip), K(0.14f, At.Action), K(0.18f, At.Action, 0, 0.02f, 0), K(0.24f, At.Action, 0, 0.02f, 0.07f),
        K(0.72f, At.Action, 0, 0.02f, 0.07f), K(0.78f, At.Action, 0, 0.02f, 0), K(0.82f, At.Action), K(0.92f, At.Grip), K(1, At.Grip),
    ];

    private static readonly Vector3 ShellLow = new(-0.02f, -0.16f, 0.06f);

    /// <summary>One shell of a tube reload (looped <see cref="ArmAnchorSet.Shells"/> times).</summary>
    private static readonly Key[] ShellKeys =
    [
        new(0, At.Mag, Vector3.Zero, ShellLow), K(0.45f, At.Mag, 0, -0.04f, 0.03f), K(0.7f, At.Mag), K(0.82f, At.Mag, 0, 0, -0.03f), new(1, At.Mag, Vector3.Zero, ShellLow),
    ];

    private static Key[] KeysFor(ReloadStyle s) =>
        s switch
        {
            ReloadStyle.PistolMag => PistolKeys,
            ReloadStyle.Revolver => RevolverKeys,
            ReloadStyle.BreakOpen => BreakKeys,
            ReloadStyle.BoltAction => BoltKeys,
            ReloadStyle.LmgBox => LmgKeys,
            ReloadStyle.MuzzleLoad => MuzzleKeys,
            _ => BoxKeys,
        };

    /// <summary>When the off hand holds the new magazine / ammo during each style's reload.</summary>
    internal static (float From, float To) CarryWindow(ReloadStyle s) =>
        s switch
        {
            ReloadStyle.PistolMag => (0.22f, 0.52f),
            ReloadStyle.Revolver => (0.48f, 0.74f),
            ReloadStyle.BreakOpen => (0.3f, 0.58f),
            ReloadStyle.BoltAction => (0.4f, 0.6f),
            ReloadStyle.LmgBox => (0.42f, 0.7f),
            ReloadStyle.MuzzleLoad => (0.18f, 0.76f),
            _ => (0.4f, 0.73f),
        };

    /// <summary>Where the gun's magazine is during a magazine reload.</summary>
    public enum MagPhase : byte
    {
        /// <summary>In the gun (before and after the swap, and for reloads that don't swap a magazine).</summary>
        Seated,
        /// <summary>The old magazine, pulled out and held in the off hand.</summary>
        OldInHand,
        /// <summary>The old magazine, let go and falling away.</summary>
        Falling,
        /// <summary>Gone: the off hand is reaching for the fresh one.</summary>
        Gone,
        /// <summary>The fresh magazine in the off hand, on its way into the gun.</summary>
        NewInHand,
    }

    /// <summary>
    /// Reload times (0..1) of a magazine swap: grabbed (the hand takes the old magazine; -1 = it drops free on a button
    /// press, as on pistols), let go, fresh one in hand, seated. Matched to the off-hand keys above. Null for the other styles.
    /// </summary>
    public static (float Grab, float Release, float Fresh, float Seat)? MagTiming(ReloadStyle s) =>
        s switch
        {
            ReloadStyle.BoxMag => (0.3f, 0.39f, 0.45f, 0.73f),
            ReloadStyle.PistolMag => (-1f, 0.04f, 0.2f, 0.52f),
            ReloadStyle.LmgBox => (0.18f, 0.32f, 0.38f, 0.7f),
            _ => null,
        };

    /// <summary>How long a let-go magazine is drawn falling before it's gone (seconds).</summary>
    public const float MagFallSeconds = 0.35f;

    /// <summary>The magazine's phase at reload time <paramref name="r"/> of a reload lasting <paramref name="reloadSeconds"/>.</summary>
    public static MagPhase PhaseAt(ReloadStyle s, float r, float reloadSeconds)
    {
        if (MagTiming(s) is not { } m || r < 0)
            return MagPhase.Seated;
        if (r >= m.Seat)
            return MagPhase.Seated;
        if (r >= m.Fresh)
            return MagPhase.NewInHand;
        if (r >= m.Release)
            return (r - m.Release) * MathF.Max(reloadSeconds, 0.1f) < MagFallSeconds ? MagPhase.Falling : MagPhase.Gone;
        if (m.Grab >= 0 && r >= m.Grab)
            return MagPhase.OldInHand;
        return MagPhase.Seated;
    }

    /// <summary>The magazine's phase this frame (set by <see cref="Update"/>).</summary>
    public MagPhase Mag;

    private Vector3 _magLast; // last in-hand offset (where a let-go magazine starts falling from)

    /// <summary>
    /// Moves the gun's magazine with the off hand: the old one comes out in the hand and is let go to fall, the fresh one
    /// rides in the hand up into the well. Uses the solved hand (so it stays in the hand even if the arm can't quite reach).
    /// </summary>
    private void UpdateMag(in ViewModelInput i, bool reloading)
    {
        Mag = reloading ? PhaseAt(Anchors.Style, i.Reload, i.Def.Reload) : MagPhase.Seated;
        MagOffset = Vector3.Zero;
        MagHidden = false;
        switch (Mag)
        {
            case MagPhase.OldInHand:
            case MagPhase.NewInHand:
                if (Matrix4x4.Invert(GunPose, out var inv))
                    MagOffset = Vector3.Transform(Left.HandWorld.Translation, inv) - Anchors.MagWell;
                _magLast = MagOffset;
                break;
            case MagPhase.Falling:
            {
                var m = MagTiming(Anchors.Style)!.Value;
                var t = (i.Reload - m.Release) * MathF.Max(i.Def.Reload, 0.1f);
                MagOffset = _magLast + new Vector3(0, -4.9f * t * t - 0.4f * t, 0.15f * t); // drops away, a little towards you
                break;
            }
            case MagPhase.Gone:
                MagHidden = true;
                break;
            default:
                _magLast = Vector3.Zero; // a pistol mag drops straight out of the well
                break;
        }
    }

    // ---- Curves -------------------------------------------------------------------------
    public static float Ease(float x)
    {
        x = Math.Clamp(x, 0f, 1f);
        return x * x * (3 - 2 * x);
    }

    /// <summary>Eased 0 -> 1 as t goes from a to b.</summary>
    public static float E(float t, float a, float b) => Ease((t - a) / (b - a));

    /// <summary>Eased up over a..b, held, eased back down over c..d.</summary>
    public static float Hump(float t, float a, float b, float c, float d) => E(t, a, b) * (1 - E(t, c, d));

    // ---- Update -------------------------------------------------------------------------

    /// <summary>Advances the animation and poses the gun and both arms. Call once per rendered frame.</summary>
    public void Update(in ViewModelInput i)
    {
        var d = i.Def;
        if (!ReferenceEquals(d, _anchorDef))
        {
            Anchors = ArmAnchors.For(d);
            _anchorDef = d;
        }
        ref readonly var A = ref Anchors;
        var dt = Math.Clamp(i.Dt, 0f, 0.1f);
        var a = Math.Clamp(i.Aim, 0f, 1f);
        var vel = i.Velocity;
        var speed = MathF.Sqrt(vel.X * vel.X + vel.Z * vel.Z);
        var reloading = i.Reload >= 0;
        var meleeing = i.Melee >= 0;
        var mantling = i.MantleTicks > 0;

        // ---- State: sprint blend, bob phase, jump/land spring, mantle progress ----
        var want = i.SprintHeld && i.Grounded && speed > Walk * 0.75f && a < 0.05f && !reloading && !meleeing && !mantling && i.Equip <= 0;
        var step = dt * (want ? 4.5f : 6f);
        _sprintW = want ? MathF.Min(_sprintW + step, 1f) : MathF.Max(_sprintW - step, 0f);
        var s = Ease(_sprintW) * (1 - a);
        var move = i.Grounded ? MathF.Min(speed / Walk, 1.5f) : 0f;
        var headBob = Math.Clamp(float.IsFinite(i.HeadBob) ? i.HeadBob : 0f, 0f, 1f);
        if (headBob > 0 && float.IsFinite(i.HeadBobPhase))
        {
            // Camera head bob on: run at the camera's stride rate and lock on to it, so the gun dips (sin = -1) when the
            // camera dips at a footfall instead of bobbing against it.
            const float cycle = MathF.Tau * 2;
            var own = dt * (10 + 6 * s);
            var target = cycle * i.HeadBobPhase - MathF.PI / 2;
            var rate = _headBobLocked ? target - _headBobPrev : own;
            rate -= cycle * MathF.Round(rate / cycle);
            _headBobPrev = target;
            _headBobLocked = true;
            var err = target - _bobPhase;
            err -= cycle * MathF.Round(err / cycle);
            _bobPhase += float.Lerp(own, rate, headBob) + err * (1f - MathF.Exp(-12f * dt)) * headBob;
            _bobPhase -= cycle * MathF.Floor(_bobPhase / cycle);
        }
        else
        {
            _headBobLocked = false;
            _bobPhase = (_bobPhase + dt * (10 + 6 * s)) % (MathF.Tau * 2); // stride rate grows with about the square root of speed: 13.5 m/s sprint ~1.6x the walk cadence
        }

        if (_prevGrounded && !i.Grounded && vel.Y > 0.5f)
            _landVel += 0.9f; // takeoff: the gun lifts
        if (!_prevGrounded && i.Grounded)
            _landVel -= 1.5f * Math.Clamp(-_prevVy / 12f, 0f, 1.3f); // landing dip, scaled by how fast we fell
        for (var rem = dt; rem > 1e-5f;)
        {
            var h = MathF.Min(rem, 1f / 120);
            _landVel += (-150f * _landPos - 16f * _landVel) * h;
            _landPos += _landVel * h;
            rem -= h;
        }
        _prevGrounded = i.Grounded;
        _prevVy = vel.Y;

        float mp = 0, mw;
        if (mantling)
        {
            if (i.MantleTicks > _mantleStart)
                _mantleStart = i.MantleTicks;
            mp = 1 - i.MantleTicks / (float)_mantleStart;
            _mantleRecover = 1;
            mw = E(mp, 0, 0.2f);
        }
        else
        {
            _mantleStart = 0;
            _mantleRecover = MathF.Max(0, _mantleRecover - dt / 0.35f);
            mw = Ease(_mantleRecover);
        }

        // ---- Gun pose: hip/ADS as before (sight line on the screen centre when aimed), then the layers ----
        var k = i.Kick;
        var x = float.Lerp(0.24f, 0f, a) + k.Side;
        var y = float.Lerp(-0.22f, -GunModel.SightHeight(d), a);
        var z = float.Lerp(-0.42f, GunModel.AdsZ(d), a) + k.Back;
        float pitch = k.Up, yaw = 0, roll = -k.Roll;
        var t = (float)(i.Now % 10000.0);

        var idle = (1 - a) * (1 - s); // breathing sway (none when fully aimed, so the sights stay put)
        x += MathF.Sin(t * 0.9f) * 0.0016f * idle;
        y += MathF.Sin(t * 1.7f) * 0.0022f * idle;
        pitch += MathF.Sin(t * 1.7f + 0.7f) * 0.006f * idle;
        roll += MathF.Sin(t * 0.9f + 1.3f) * 0.008f * idle;

        var bob = move * (1 - a * 0.8f) * (1 - 0.6f * headBob); // walk bob, scaled by speed (less when the camera bobs too)
        y += MathF.Sin(_bobPhase) * 0.012f * bob;
        x += MathF.Cos(_bobPhase * 0.5f) * 0.008f * bob * (1 - a);
        roll += MathF.Cos(_bobPhase * 0.5f) * 0.02f * bob * (1 - a);

        // Sprint: lowered and canted, with an arm pump
        x += -0.05f * s + MathF.Cos(_bobPhase * 0.5f) * 0.02f * s * move;
        y += 0.02f * s + MathF.Sin(_bobPhase) * 0.02f * s * move;
        z += 0.04f * s;
        pitch += -0.25f * s;
        yaw += 0.35f * s;
        roll += 0.45f * s;

        // Jump / land
        var air = i.Grounded ? 0f : Math.Clamp(vel.Y * 0.004f, -0.02f, 0.02f);
        var jl = 1 - a * 0.7f;
        y += (_landPos * 0.06f + air) * jl;
        pitch += _landPos * 0.15f * jl;

        // Equip: up from below
        var e = Ease(i.Equip);
        y -= 0.3f * e;
        pitch -= 1.0f * e;
        roll += 0.3f * e;
        z += 0.06f * e;

        // Melee: pistol-whip or rifle-butt arc
        var pistol = d.Class == WeaponClass.Pistol;
        if (meleeing)
        {
            var w = Hump(i.Melee, 0, 0.22f, 0.22f, 0.42f); // wind-up
            var hit = Hump(i.Melee, 0.22f, 0.42f, 0.55f, 1f); // strike and recover
            if (pistol)
            {
                x += 0.04f * w - 0.12f * hit;
                y += 0.08f * w - 0.04f * hit;
                z += -0.16f * hit;
                pitch += 0.7f * w - 0.5f * hit;
                roll += -0.4f * w + 0.5f * hit;
                yaw += 0.4f * hit;
            }
            else
            {
                x += 0.05f * w - 0.12f * hit;
                y += 0.02f * w + 0.04f * hit;
                z += 0.06f * w - 0.2f * hit;
                pitch += 0.1f * w + 0.35f * hit;
                yaw += -0.35f * w + 0.9f * hit;
                roll += -0.2f * w + 0.6f * hit;
            }
        }

        // Reload: the gun turns to show the hands the magazine well (timing is a fraction of def.Reload)
        if (reloading)
        {
            var r = i.Reload;
            switch (A.Style)
            {
                case ReloadStyle.BoxMag:
                {
                    var tl = Hump(r, 0, 0.12f, 0.82f, 1);
                    var j = Hump(r, 0.76f, 0.79f, 0.79f, 0.86f); // slap
                    roll += -0.6f * tl;
                    pitch += 0.22f * tl + 0.06f * j;
                    y += 0.15f * tl + 0.012f * j;
                    x += -0.1f * tl;
                    z += 0.06f * tl;
                    yaw += 0.15f * tl;
                    break;
                }
                case ReloadStyle.PistolMag:
                {
                    var tl = Hump(r, 0, 0.1f, 0.85f, 1);
                    var j = Hump(r, 0.76f, 0.79f, 0.79f, 0.86f); // slide release
                    roll += -0.4f * tl;
                    pitch += 0.2f * tl - 0.04f * j;
                    y += 0.12f * tl;
                    z += 0.03f * tl + 0.01f * j;
                    x += -0.09f * tl;
                    yaw += -0.1f * tl;
                    break;
                }
                case ReloadStyle.Revolver:
                {
                    var sw = Hump(r, 0, 0.14f, 0.78f, 0.86f); // cylinder out (snaps shut at the end)
                    var fl = Hump(r, 0.16f, 0.22f, 0.26f, 0.36f); // muzzle-up flick to eject
                    var dn = Hump(r, 0.36f, 0.46f, 0.72f, 0.8f); // muzzle down to load
                    roll += 0.85f * sw;
                    pitch += 0.2f * sw + 0.7f * fl - 0.45f * dn;
                    y += 0.12f * sw;
                    x += -0.09f * sw;
                    z += 0.03f * sw;
                    break;
                }
                case ReloadStyle.TubeShells:
                {
                    var tl = Hump(r, 0, 0.1f, 0.84f, 0.94f);
                    roll += -0.6f * tl;
                    pitch += 0.15f * tl;
                    y += 0.12f * tl;
                    x += -0.08f * tl;
                    z += 0.05f * tl;
                    break;
                }
                case ReloadStyle.BreakOpen:
                {
                    var br = Hump(r, 0, 0.14f, 0.66f, 0.74f);
                    var j = Hump(r, 0.72f, 0.76f, 0.76f, 0.84f); // snap shut
                    pitch += -0.5f * br + 0.08f * j;
                    y += 0.12f * br;
                    z += 0.05f * br;
                    roll += -0.15f * br;
                    x += -0.07f * br;
                    break;
                }
                case ReloadStyle.BoltAction:
                {
                    var tl = Hump(r, 0, 0.12f, 0.86f, 1);
                    roll += 0.25f * tl;
                    pitch += 0.1f * tl;
                    y += 0.1f * tl;
                    x += -0.08f * tl;
                    z += 0.05f * tl;
                    break;
                }
                case ReloadStyle.LmgBox:
                {
                    var tl = Hump(r, 0, 0.12f, 0.86f, 1);
                    var j = Hump(r, 0.85f, 0.87f, 0.87f, 0.93f); // feed cover slam
                    roll += -0.35f * tl;
                    pitch += 0.15f * tl - 0.04f * j;
                    y += 0.12f * tl - 0.01f * j;
                    x += -0.1f * tl;
                    z += 0.06f * tl;
                    break;
                }
                case ReloadStyle.MuzzleLoad:
                {
                    var up = Hump(r, 0, 0.15f, 0.8f, 0.95f);
                    pitch += 1.35f * up; // stood up in front of you, muzzle at chest height
                    y += -0.47f * up;
                    x += -0.1f * up;
                    z += 0.02f * up;
                    roll += 0.15f * up;
                    break;
                }
            }
        }

        // Mantle: tuck the gun down to the side while the off hand plants on the ledge
        x += 0.05f * mw;
        y += -0.06f * mw;
        z += 0.06f * mw;
        pitch += -0.35f * mw;
        roll += -0.6f * mw;
        yaw += -0.25f * mw;

        GunPose = Matrix4x4.CreateRotationX(pitch) * Matrix4x4.CreateRotationY(yaw) * Matrix4x4.CreateRotationZ(roll) * Matrix4x4.CreateTranslation(x, y, z);
        var steady = Sights.Steadiness(d, a);
        if (steady < 1 && Sights.Lens(d) is { } lens)
        {
            // A high-power lens sits a couple of centimetres from the eye: only part of the sway, bob and kick moves it
            // (the rotations stay, so the reticle still climbs with the recoil). See Sights.Steadiness.
            var rest = lens.Center + new Vector3(float.Lerp(0.24f, 0f, a), float.Lerp(-0.22f, -GunModel.SightHeight(d), a), float.Lerp(-0.42f, GunModel.AdsZ(d), a));
            GunPose.Translation -= (Vector3.Transform(lens.Center, GunPose) - rest) * (1 - steady);
        }
        var gunRot = Quaternion.Concatenate(Quaternion.Concatenate(Quaternion.CreateFromAxisAngle(Vector3.UnitX, pitch), Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw)),
            Quaternion.CreateFromAxisAngle(Vector3.UnitZ, roll));
        if (i.PoseOverride is { } po && Matrix4x4.Decompose(po, out _, out var poRot, out _))
        {
            GunPose = po;
            gunRot = poRot;
        }

        // ---- Hand targets ----
        var right = OnGun(At.Grip, Vector3.Zero, Vector3.Zero, gunRot);
        var left = OnGun(At.Support, Vector3.Zero, Vector3.Zero, gunRot);
        if (A.Magnified && a > 0)
        {
            // Keep the off hand out of a magnified lens: slide it back onto the magazine / under the receiver.
            var ads = new HandTarget(Vector3.Transform(A.SupportAds, GunPose), gunRot, A.SupportAdsShape);
            left = HandTarget.Blend(left, ads, E(a, 0.3f, 0.9f));
        }
        Carrying = false;
        if (reloading)
        {
            var r = i.Reload;
            if (A.Style == ReloadStyle.TubeShells)
            {
                if (r < 0.1f)
                    left = HandTarget.Blend(OnGun(At.Support, Vector3.Zero, Vector3.Zero, gunRot), OnGun(At.Mag, Vector3.Zero, ShellLow, gunRot), E(r, 0, 0.1f));
                else if (r < 0.86f)
                {
                    var u = (r - 0.1f) / 0.76f * A.Shells;
                    var cyc = u - MathF.Floor(u);
                    left = EvalKeys(ShellKeys, cyc, gunRot);
                    Carrying = cyc is >= 0.1f and < 0.78f;
                }
                else if (r < 0.93f)
                    left = HandTarget.Blend(OnGun(At.Mag, Vector3.Zero, ShellLow, gunRot), OnGun(At.Support, Vector3.Zero, Vector3.Zero, gunRot), E(r, 0.86f, 0.93f));
                else // rack the pump
                    left = HandTarget.Blend(OnGun(At.Support, Vector3.Zero, Vector3.Zero, gunRot), OnGun(At.Support, new Vector3(0, 0, 0.07f), Vector3.Zero, gunRot), Hump(r, 0.93f, 0.96f, 0.96f, 1f));
            }
            else
            {
                left = EvalKeys(KeysFor(A.Style), r, gunRot);
                var (c0, c1) = CarryWindow(A.Style);
                Carrying = r >= c0 && r < c1 && MagTiming(A.Style) == null; // magazine swaps carry the gun's own magazine
            }
            if (A.Style == ReloadStyle.BoltAction)
                right = EvalKeys(BoltGripKeys, r, gunRot);
        }
        if (meleeing && pistol) // the off hand lets go for a pistol whip
            left = HandTarget.Blend(left, new HandTarget(new Vector3(-0.14f, -0.3f, -0.3f), Quaternion.CreateFromAxisAngle(Vector3.UnitX, -0.3f), HandShape.Flat),
                Hump(i.Melee, 0, 0.15f, 0.7f, 1f));
        if (mantling)
        {
            var plant = E(mp, 0, 0.15f) * (1 - E(mp, 0.7f, 0.95f));
            if (plant > 0)
                left = HandTarget.Blend(left, LedgeHand(i), plant);
        }

        RightHand = right;
        LeftHand = left;
        var rootKick = new Vector3(0, 0, k.Back * 0.6f); // the shoulders soak up some of the kick: the elbows flex
        Right.Solve(right, Matrix4x4.Identity, rootKick);
        Left.Solve(left, Matrix4x4.Identity, rootKick);
        UpdateMag(i, reloading);
    }

    /// <summary>A hand on one of the gun's anchors (offset in gun space, then in camera space).</summary>
    private HandTarget OnGun(At at, Vector3 g, Vector3 c, Quaternion gunRot)
    {
        ref readonly var A = ref Anchors;
        Vector3 p;
        Quaternion r;
        HandShape sh;
        switch (at)
        {
            case At.Support:
                (p, r, sh) = (A.Support, A.SupportRot, A.SupportShape);
                break;
            case At.Mag:
                (p, r, sh) = (A.MagWell, A.MagRot, A.MagShape);
                break;
            case At.Action:
                (p, r, sh) = (A.Action, A.ActionRot, HandShape.Flat);
                break;
            case At.Muzzle:
                (p, r, sh) = (A.Muzzle, Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2), HandShape.Wrap);
                break;
            default:
                (p, r, sh) = (A.Grip, A.GripRot, HandShape.Wrap);
                break;
        }
        return new HandTarget(Vector3.Transform(p + g, GunPose) + c, Quaternion.Concatenate(r, gunRot), sh);
    }

    private HandTarget EvalKeys(Key[] keys, float t, Quaternion gunRot)
    {
        var n = keys.Length - 1;
        var idx = n - 1;
        var f = 1f;
        for (var j = 0; j < n; j++)
            if (t < keys[j + 1].T)
            {
                idx = j;
                f = Ease((t - keys[j].T) / MathF.Max(keys[j + 1].T - keys[j].T, 1e-6f));
                break;
            }
        var k0 = keys[idx];
        var k1 = keys[idx + 1];
        return HandTarget.Blend(OnGun(k0.A, k0.G, k0.C, gunRot), OnGun(k1.A, k1.G, k1.C, gunRot), f);
    }

    /// <summary>The off hand planted palm-down on the ledge being climbed (kept inside the lower view so it reads).</summary>
    private static HandTarget LedgeHand(in ViewModelInput i)
    {
        var fwd = World.Forward(i.Yaw, i.Pitch);
        var view = Matrix4x4.CreateLookAt(i.Eye, i.Eye + fwd, Vector3.UnitY);
        var ff = World.FlatForward(i.Yaw);
        var fr = World.FlatRight(i.Yaw);
        var p = Vector3.Transform(i.MantleTo - ff * 0.25f - fr * 0.18f, view);
        p = new Vector3(Math.Clamp(p.X, -0.3f, -0.06f), Math.Clamp(p.Y * 0.5f, -0.26f, -0.14f), Math.Clamp(p.Z, -0.6f, -0.42f));
        var up = Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitY, view));
        var zf = Vector3.Normalize(-Vector3.TransformNormal(ff, view));
        var xa = Vector3.Normalize(Vector3.Cross(up, zf));
        var za = Vector3.Cross(xa, up);
        var m = new Matrix4x4(xa.X, xa.Y, xa.Z, 0, up.X, up.Y, up.Z, 0, za.X, za.Y, za.Z, 0, 0, 0, 0, 1);
        return new HandTarget(p, Quaternion.CreateFromRotationMatrix(m), HandShape.Flat);
    }

    /// <summary>Draws both arms (and whatever the off hand is carrying) in camera space.</summary>
    public void DrawArms(Renderer g, in ArmColors colors)
    {
        Right.Draw(g, colors);
        Left.Draw(g, colors);
        if (Carrying)
            Arms.DrawCarry(g, Left, Anchors);
    }
}
