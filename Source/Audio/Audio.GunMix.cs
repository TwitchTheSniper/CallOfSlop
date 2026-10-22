namespace CallOfSlop;

/// <summary>
/// The gunshot mix: how loud your own shot plays against someone else's, and how another player's shot changes with
/// distance (the near layer crossfading into the distant one, their falloffs and the distance filter). One pure function
/// (<see cref="PlanShot"/>) that <see cref="Shot"/> plays and the "Gun mix" self-test renders offline, so the
/// measured numbers are the game's.
/// </summary>
public sealed partial class Audio
{
    /// <summary>How one gunshot plays: the near and far layers' gains (before distance) and their falloffs.</summary>
    internal readonly record struct ShotPlan(float Near, float Far, Falloff NearFalloff, Falloff FarFalloff);

    /// <summary>
    /// The plan for a shot: <paramref name="own"/> = your own gun (listener-relative, no distance), else another
    /// player's at <paramref name="distance"/>; <paramref name="hasFar"/> = the weapon has a distant layer.
    /// </summary>
    internal static ShotPlan PlanShot(bool own, bool suppressed, bool hasFar, float distance)
    {
        if (own)
            return new(OwnShotGain * (suppressed ? 0.5f : 1f), 0f, Falloff.Normal, Falloff.Far);
        if (suppressed || !hasFar)
            return new(suppressed ? 0.5f : 1f, 0f, Falloff.Normal, Falloff.Far);
        var far = FarMix(distance);
        return new(far < 0.98f ? 1f - 0.85f * far : 0f, far > 0.02f ? 0.9f * far : 0f, Falloff.Normal, Falloff.Far);
    }

    /// <summary>The falloff of a positional effect (<see cref="Effect3D"/>: explosions, bounces, beeps).</summary>
    internal static Falloff EffectFalloff => Falloff.Normal;

    /// <summary>The distance filter's (low-shelf, high-shelf) gains for a layer with <paramref name="falloff"/> at <paramref name="distance"/>.</summary>
    internal static (float Lf, float Hf) DistanceFilter(Falloff falloff, float distance) => (1f, DistanceHf(distance));

    /// <summary>The far layer's level trim for a weapon from its near and far buffers (1 = as baked).</summary>
    internal static float FarTrimFor(short[] near, short[]? far) => 1f;

    /// <summary>The far layer's level trim per weapon (1 = as baked).</summary>
    private float FarTrim(int weapon) => 1f;
}
