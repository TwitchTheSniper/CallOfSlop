namespace CallOfSlop;

/// <summary>
/// The gunshot mix: how loud your own shot plays against someone else's, and how another player's shot changes with
/// distance (the near layer crossfading into the distant one, their falloffs and the distance filter). One pure function
/// (<see cref="PlanShot"/>) that <see cref="Shot"/> plays and the "Gun mix" self-test renders offline, so the
/// measured numbers are the game's.
/// <para>
/// Your own gun plays listener-relative at <see cref="OwnShotGain"/> (OpenAL's per-source cap of 1, so the baked
/// peak of 0.85 is never pushed past it); another player's shot plays at <see cref="OtherShotGain"/> of that up close
/// and then follows the inverse-distance law (<see cref="ShotFalloff"/>: about 5 dB down for every doubling from 5 m), the
/// near layer handing over to the distant one on the same curve, level-matched (<see cref="FarTrimFor"/>), so the
/// handover never makes a far shot louder. With EFX the distance filter thins it as it goes: a low shelf takes the boom
/// away (<see cref="DistanceLf"/>) and the high shelf (<see cref="DistanceHf"/>) the air above a few kHz, leaving the
/// mid crack and echo that place it.
/// </para>
/// </summary>
public sealed partial class Audio
{
    /// <summary>Another player's shot up close, relative to the same shot as your own (about -7.5 dB: yours is at your ear).</summary>
    public const float OtherShotGain = 0.42f;

    /// <summary>Another player's suppressed shot, on top of <see cref="OtherShotGain"/> (your own suppressed gun is <see cref="OwnSuppressedGain"/>).</summary>
    public const float OtherSuppressedGain = 0.5f, OwnSuppressedGain = 0.5f;

    /// <summary>Another player's launcher (a rocket's roar, a grenade launcher's thoomp) up close: a little more than a gun, it has no distant layer.</summary>
    public const float OtherLauncherGain = 0.7f;

    /// <summary>The distant layer's level against the near shot's (RMS over <see cref="FarTrimWindow"/>): a far shot is mostly the echo.</summary>
    public const float FarLevel = 0.8f;

    /// <summary>The window <see cref="FarTrimFor"/> compares the near and far layers over (s).</summary>
    public const float FarTrimWindow = 0.3f;

    /// <summary>
    /// A gunshot from another player: inverse distance from 5 m, a little gentler than the physical law (about -5 dB per
    /// doubling, so a fight 80 m away still carries), still falling at 250 m; thinned by distance.
    /// </summary>
    internal static readonly Falloff ShotFalloff = new(5f, 0.8f, 250f, Thin: true);

    /// <summary>A suppressed shot from another player: fades sooner (a suppressor's report is quiet and doesn't carry).</summary>
    internal static readonly Falloff SuppressedFalloff = new(3f, 1.3f, 120f, Thin: true);

    /// <summary>How one gunshot plays: the near and far layers' gains (before distance) and their falloffs.</summary>
    internal readonly record struct ShotPlan(float Near, float Far, Falloff NearFalloff, Falloff FarFalloff);

    /// <summary>
    /// The plan for a shot: <paramref name="own"/> = your own gun (listener-relative, no distance), else another
    /// player's at <paramref name="distance"/>; <paramref name="hasFar"/> = the weapon has a distant layer;
    /// <paramref name="launcher"/> = a rocket or grenade launcher.
    /// </summary>
    internal static ShotPlan PlanShot(bool own, bool suppressed, bool hasFar, float distance, bool launcher = false)
    {
        if (own)
            return new(OwnShotGain * (suppressed ? OwnSuppressedGain : 1f), 0f, ShotFalloff, ShotFalloff);
        if (suppressed)
            return new(OtherShotGain * OtherSuppressedGain, 0f, SuppressedFalloff, SuppressedFalloff);
        if (!hasFar)
            return new(launcher ? OtherLauncherGain : OtherShotGain, 0f, ShotFalloff, ShotFalloff);
        // Equal-power handover (the two layers are different sounds, so their power adds).
        var far = FarMix(distance);
        var near = far < 0.98f ? MathF.Sqrt(1f - far) : 0f;
        return new(OtherShotGain * near, far > 0.02f ? OtherShotGain * MathF.Sqrt(far) : 0f, ShotFalloff, ShotFalloff);
    }

    /// <summary>The falloff of a positional effect (<see cref="Effect3D"/>: explosions, bounces, beeps).</summary>
    internal static Falloff EffectFalloff => Falloff.Normal;

    /// <summary>
    /// Low-shelf gain (below about 250 Hz) of the distance filter on gunshots: 1 within 8 m, about 0.53 at 30 m, 0.23
    /// at 60 m, never below 0.15 (far gunfire loses its boom, not its crack).
    /// </summary>
    public static float DistanceLf(float distance) => Math.Clamp(MathF.Exp(-MathF.Max(0f, distance - 8f) / 35f), 0.15f, 1f);

    /// <summary>The distance filter's (low-shelf, high-shelf) gains for a layer with <paramref name="falloff"/> at <paramref name="distance"/>.</summary>
    internal static (float Lf, float Hf) DistanceFilter(Falloff falloff, float distance) =>
        (falloff.Thin ? DistanceLf(distance) : 1f, DistanceHf(distance));

    /// <summary>
    /// The far layer's level trim for a weapon: <see cref="FarLevel"/> times the near shot's RMS over its far layer's
    /// (both over <see cref="FarTrimWindow"/>), so the handover keeps the level (1 without a far layer).
    /// </summary>
    internal static float FarTrimFor(short[] near, short[]? far)
    {
        if (far == null || far.Length == 0 || near.Length == 0)
            return 1f;
        var n = (int)(FarTrimWindow * SampleRate);
        var f = PcmRms(far, n);
        return f < 1e-6 ? 1f : Math.Clamp(FarLevel * (float)(PcmRms(near, n) / f), 0.05f, 2f);
    }

    /// <summary>RMS of the first <paramref name="n"/> samples (silence past the end counts: a short shot is quieter over the window).</summary>
    private static double PcmRms(short[] x, int n)
    {
        double sum = 0;
        for (var i = 0; i < Math.Min(n, x.Length); i++)
            sum += x[i] / 32768.0 * (x[i] / 32768.0);
        return Math.Sqrt(sum / Math.Max(1, n));
    }

    private float[] _farTrims = [];

    /// <summary>The far layer's level trim per weapon (worked out at start-up).</summary>
    private float FarTrim(int weapon) => weapon < _farTrims.Length ? _farTrims[weapon] : 1f;
}
