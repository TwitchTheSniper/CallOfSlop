namespace CallOfSlop;

/// <summary>
/// The gunshot mix: how loud your own shot plays against someone else's, and how another player's shot changes with
/// distance (the near layer crossfading into the distant one, their falloffs and the distance filter). One pure function
/// (<see cref="PlanShot"/>) that <see cref="Shot"/> plays and the "Gun mix" self-test renders offline, so the
/// measured numbers are the game's.
/// <para>
/// Your own gun plays listener-relative at <see cref="OwnShotGain"/> (OpenAL's per-source cap of 1, so the baked
/// peak of 0.85 is never pushed past it; the mixer's level then scales the voice and the cap together); another
/// player's shot plays at <see cref="OtherShotGain"/> of that up close
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

    /// <summary>The distant layer's level against the near shot's (RMS over <see cref="FarTrimWindow"/>): a far shot is mostly the echo.</summary>
    public const float FarLevel = 0.8f;

    /// <summary>The window <see cref="FarTrimFor"/> compares the near and far layers over (s).</summary>
    public const float FarTrimWindow = 0.3f;

    /// <summary>
    /// A gunshot from another player: inverse distance from 5 m, a little gentler than the physical law (about -5 dB per
    /// doubling, so a fight 80 m away still carries), still falling at 250 m; thinned by distance.
    /// </summary>
    internal static Falloff ShotFalloff => AudioMix.Defaults.ShotFalloff; // 5 m, 0.8, 250 m: mixer rows (AudioMixDefaults.cs)

    /// <summary>A suppressed shot from another player: fades sooner (a suppressor's report is quiet and doesn't carry).</summary>
    internal static readonly Falloff SuppressedFalloff = new(3f, 1.3f, 120f, Thin: true);

    /// <summary>
    /// How one gunshot plays: the near and far layers' gains (before distance) and their falloffs; and the mixer buses
    /// they play on with those buses' gains (<c>NearBusGain</c>, <c>FarBusGain</c>: the built-in mix has your own gun at
    /// +20 dB = 10, another player's near layer +14.1 dB and the distant one +10 dB), which scale the voice and its
    /// OpenAL cap together (Audio.Mixer.cs), so a boost is heard in full even for a voice at the cap.
    /// </summary>
    internal readonly record struct ShotPlan(float Near, float Far, Falloff NearFalloff, Falloff FarFalloff,
        MixBus NearBus = MixBus.GunOtherNear, MixBus FarBus = MixBus.GunOtherFar, float NearBusGain = 1f, float FarBusGain = 1f)
    {
        /// <summary>The near layer's gain with the mix (what plays, before effects volume, jitter and distance).</summary>
        public float NearMixed => Near * NearBusGain;

        /// <summary>The far layer's gain with the mix.</summary>
        public float FarMixed => Far * FarBusGain;
    }

    /// <summary>
    /// The plan for a shot: <paramref name="own"/> = your own gun (listener-relative, no distance), else another
    /// player's at <paramref name="distance"/>; <paramref name="hasFar"/> = the weapon has a distant layer (launchers,
    /// energy and novelty guns don't: their one layer just fades with distance).
    /// </summary>
    internal static ShotPlan PlanShot(bool own, bool suppressed, bool hasFar, float distance) => PlanShot(AudioMix.Defaults, own, suppressed, hasFar, distance);

    /// <summary>
    /// <see cref="PlanShot(bool, bool, bool, float)"/> through a mix: its gunshot falloff and crossfade distances, and
    /// the buses (own gun, own suppressed, launchers, other players' near and far layers, their suppressed shots) with
    /// their gains. <paramref name="launcher"/>: a launcher's shot (its own buses).
    /// </summary>
    internal static ShotPlan PlanShot(AudioMix mix, bool own, bool suppressed, bool hasFar, float distance, bool launcher = false)
    {
        var fall = mix.ShotFalloff;
        if (own)
        {
            var ob = launcher ? MixBus.LauncherOwn : suppressed ? MixBus.GunOwnSuppressed : MixBus.GunOwn;
            return new(OwnShotGain * (suppressed ? OwnSuppressedGain : 1f), 0f, fall, fall, ob, ob, mix.Gain(ob), mix.Gain(ob));
        }
        if (suppressed && !launcher)
            return new(OtherShotGain * OtherSuppressedGain, 0f, SuppressedFalloff, SuppressedFalloff, MixBus.GunOtherSuppressed, MixBus.GunOtherSuppressed,
                mix.Gain(MixBus.GunOtherSuppressed), mix.Gain(MixBus.GunOtherSuppressed));
        var nb = launcher ? MixBus.LauncherOther : MixBus.GunOtherNear;
        var fb = launcher ? MixBus.LauncherOther : MixBus.GunOtherFar;
        if (suppressed)
            return new(OtherShotGain * OtherSuppressedGain, 0f, SuppressedFalloff, SuppressedFalloff, nb, nb, mix.Gain(nb), mix.Gain(nb));
        if (!hasFar)
            return new(OtherShotGain, 0f, fall, fall, nb, nb, mix.Gain(nb), mix.Gain(nb));
        // Equal-power handover (the two layers are different sounds, so their power adds).
        var far = mix.FarMix(distance);
        var near = far < 0.98f ? MathF.Sqrt(1f - far) : 0f;
        return new(OtherShotGain * near, far > 0.02f ? OtherShotGain * MathF.Sqrt(far) : 0f, fall, fall, nb, fb, mix.Gain(nb), mix.Gain(fb));
    }

    /// <summary>The falloff of a positional effect (<see cref="Effect3D"/>: explosions, bounces, beeps).</summary>
    internal static Falloff EffectFalloff => Falloff.Normal;

    /// <summary>
    /// Low-shelf gain (below about 250 Hz) of the distance filter on gunshots: 1 within 8 m, about 0.53 at 30 m, 0.23
    /// at 60 m, never below 0.15 (far gunfire loses its boom, not its crack).
    /// </summary>
    public static float DistanceLf(float distance) => AudioMix.Defaults.DistanceLf(distance); // the mixer's Distance rows (AudioMixDefaults.cs)

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
