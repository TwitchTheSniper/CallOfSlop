using System.Numerics;

namespace CallOfSlop;

/// <summary>What the camera head bob is driven by this frame (the local player's drawn movement state).</summary>
public struct HeadBobInput
{
    /// <summary>Seconds since the last update (clamped to 0.1).</summary>
    public float Dt;

    public Vector3 Velocity;
    public bool Grounded, Mantling;
    public Stance Stance;

    /// <summary>0..1 aim-down-sights blend (Game.AimT): fully aimed = no bob, so scopes stay steady.</summary>
    public float Aim;

    /// <summary>The "Head bob" setting, 0..1 (0 = no bob at all, exactly the old camera).</summary>
    public float Strength;

    /// <summary>
    /// The legs' gait phase (<see cref="LegAnimator.GaitPhase"/>: foot 0 touches down at 0, foot 1 at 0.5), or NaN when
    /// the legs aren't being animated; the bob then keeps its own phase at the same cadence.
    /// </summary>
    public float GaitPhase;
}

/// <summary>
/// First-person camera head bob, presentation only: an offset to the rendered eye and a small roll, never to the aim, the
/// bullets (they leave from the unbobbed eye along the look direction) or anything sent over the network. Two dips per
/// stride, timed to the footfalls of the leg gait, and one side-to-side sway (with a little roll) per stride. Amplitude
/// and frequency follow ground speed: subtle at a walk, stronger at a full sprint. Nothing while airborne, mantling or
/// prone; half while crouch-walking; fading out as you aim down sights. It ramps in and out smoothly (starting, stopping,
/// jumping, landing), so the camera never pops. Deterministic: the same inputs give the same offsets. No allocations.
/// </summary>
public sealed class HeadBob
{
    // ---- Tuning: at walking speed vs at full sprint (metres, radians) ----
    public const float WalkVertical = 0.010f, SprintVertical = 0.045f; // dip depth (half the peak-to-peak travel)
    public const float WalkSway = 0.004f, SprintSway = 0.014f; // side to side
    public const float WalkRoll = 0.2f * MathF.PI / 180f, SprintRoll = 0.8f * MathF.PI / 180f;

    /// <summary>Crouch-walking bobs this much of the standing amount.</summary>
    public const float CrouchScale = 0.5f;

    /// <summary>Ramp rates (per second): easing in on the ground, out when leaving it (jump, fall, mantle) and when going prone.</summary>
    public const float FadeIn = 3f, FadeOutAir = 12f, FadeOutProne = 5f;

    /// <summary>The dip trails the footfall by this much of a stride: a heel strike at a walk, mid-stance compression at a sprint.</summary>
    public const float WalkLag = 0.03f, SprintLag = 0.09f;

    /// <summary>This frame's camera offset: <see cref="Offset"/> in view space (X right, Y up) and <see cref="Roll"/> (radians, positive = tilted right).</summary>
    public Vector3 Offset;

    /// <inheritdoc cref="Offset"/>
    public float Roll;

    /// <summary>Stride phase (0..1), and the dip phase (stride phase minus the footfall lag) the viewmodel locks its own bob to.</summary>
    public float Phase, DipPhase;

    /// <summary>
    /// How much of the bob is on right now (ground ramp x stance x setting, 0..1, not counting aim), for the viewmodel:
    /// the gun rides on the camera, so its own bob eases off by this much instead of doubling the motion.
    /// </summary>
    public float Weight;

    /// <summary>Smoothed ground speed (m/s) and this frame's vertical dip depth (m, after every scale), for the self-test and the smoke log.</summary>
    public float Speed, Amplitude;

    /// <summary>Stride cycles per second this frame.</summary>
    public float Cadence;

    private float _ground, _stance = 1f, _lag;
    private bool _init;

    /// <summary>Forgets everything (the next update starts from rest, with no bob).</summary>
    public void Reset()
    {
        _init = false;
        _ground = 0;
        _stance = 1f;
        Phase = DipPhase = Weight = Speed = Amplitude = Cadence = Roll = 0;
        Offset = Vector3.Zero;
    }

    /// <summary>The vertical dip depth at a ground speed (standing, full setting): 0 at rest, rising monotonically to the sprint value.</summary>
    public static float VerticalAt(float speed) => Curve(speed, WalkVertical, SprintVertical);

    /// <summary>The sway at a ground speed (standing, full setting).</summary>
    public static float SwayAt(float speed) => Curve(speed, WalkSway, SprintSway);

    /// <summary>The roll at a ground speed (standing, full setting).</summary>
    public static float RollAt(float speed) => Curve(speed, WalkRoll, SprintRoll);

    /// <summary>Linear from 0 at rest to <paramref name="walk"/> at walking speed, then to <paramref name="sprint"/> at sprint speed (held above it).</summary>
    private static float Curve(float speed, float walk, float sprint)
    {
        speed = MathF.Max(speed, 0f);
        if (speed <= World.WalkSpeed)
            return walk * speed / World.WalkSpeed;
        return float.Lerp(walk, sprint, MathF.Min((speed - World.WalkSpeed) / (World.SprintSpeed - World.WalkSpeed), 1f));
    }

    /// <summary>Advances the bob by one rendered frame and sets <see cref="Offset"/> / <see cref="Roll"/>.</summary>
    public void Update(in HeadBobInput i)
    {
        var dt = Math.Clamp(float.IsFinite(i.Dt) ? i.Dt : 0f, 0f, 0.1f);
        var raw = MathF.Sqrt(i.Velocity.X * i.Velocity.X + i.Velocity.Z * i.Velocity.Z);
        if (!float.IsFinite(raw))
            raw = 0;
        if (!_init)
        {
            _init = true;
            Speed = raw;
            _ground = 0;
            _stance = i.Stance == Stance.Crouch ? CrouchScale : 1f;
            Phase = float.IsFinite(i.GaitPhase) ? i.GaitPhase : 0f;
            _lag = i.Stance == Stance.Crouch ? WalkLag : float.Lerp(WalkLag, SprintLag, LegAnimator.RunWeight(raw));
        }
        else
            Speed = float.Lerp(Speed, raw, 1f - MathF.Exp(-10f * dt)); // snapshot and tick steps don't jitter the amplitude

        // ---- Ground ramp: on (eased in) only standing or crouching on the ground; out quickly in the air ----
        var prone = i.Stance == Stance.Prone;
        var onGround = i.Grounded && !i.Mantling && !prone;
        var rate = onGround ? FadeIn : prone && i.Grounded && !i.Mantling ? FadeOutProne : FadeOutAir;
        _ground = Approach(_ground, onGround ? 1f : 0f, rate * dt);
        _stance = Approach(_stance, i.Stance == Stance.Crouch ? CrouchScale : 1f, 3f * dt);

        // ---- Stride phase: the leg cadence at this speed, locked softly onto the legs' own phase when they're animated ----
        var crouch = i.Stance == Stance.Crouch;
        Cadence = crouch ? LegAnimator.CrouchCadence(Speed) : LegAnimator.Cadence(Speed);
        Phase += Cadence * dt;
        if (float.IsFinite(i.GaitPhase))
        {
            var e = i.GaitPhase - Phase;
            e -= MathF.Round(e); // shortest way round the cycle
            Phase += e * (1f - MathF.Exp(-12f * dt));
        }
        Phase -= MathF.Floor(Phase);
        var run = crouch ? 0f : LegAnimator.RunWeight(Speed);
        _lag = float.Lerp(_lag, float.Lerp(WalkLag, SprintLag, run), 1f - MathF.Exp(-4f * dt)); // eased: crouching mid-sprint doesn't jump the phase
        DipPhase = Phase - _lag;
        DipPhase -= MathF.Floor(DipPhase);

        // ---- Offsets ----
        var strength = Math.Clamp(float.IsFinite(i.Strength) ? i.Strength : 1f, 0f, 1f);
        var ground = _ground * _ground * (3 - 2 * _ground);
        Weight = ground * _stance * strength;
        var aim = 1f - Math.Clamp(i.Aim, 0f, 1f);
        var k = Weight * aim * aim;
        if (k <= 0f)
        {
            Offset = Vector3.Zero;
            Roll = 0;
            Amplitude = 0;
            return;
        }
        Amplitude = VerticalAt(Speed) * k;
        var dip = MathF.Cos(MathF.Tau * 2f * DipPhase); // 1 at each footfall (both feet)
        var side = MathF.Sin(MathF.Tau * DipPhase); // + over foot 0 (the right), - over foot 1
        Offset = new Vector3(SwayAt(Speed) * k * side, -Amplitude * dip, 0);
        Roll = RollAt(Speed) * k * side;
    }

    /// <summary>The bobbed camera position for an unbobbed eye looking along <paramref name="yaw"/> (the look direction itself never changes).</summary>
    public Vector3 Apply(Vector3 eye, float yaw) => eye + World.FlatRight(yaw) * Offset.X + Vector3.UnitY * Offset.Y;

    private static float Approach(float v, float target, float step) => v < target ? MathF.Min(v + step, target) : MathF.Max(v - step, target);
}
