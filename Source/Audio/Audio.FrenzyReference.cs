namespace CallOfSlop;

/// <summary>
/// The loudness reference the MAX FRENZY music level (6) is calibrated against: measured from the game's own
/// synthesised sounds, the way the game plays them, instead of a guess (see <see cref="FrenzyLayer.SfxReferenceRms"/>).
/// Headless: no OpenAL needed.
/// </summary>
public sealed partial class Audio
{
    /// <summary>The gain <see cref="Shot"/> plays your own (unsuppressed) gun at, and a frag grenade's explosion.</summary>
    public const float OwnShotGain = 0.75f, FragExplosionGain = 1f;

    /// <summary>
    /// The combat loudness reference (sample RMS, effects volume 1, master volume not applied): for every real firearm
    /// (not suppressed, toy, zap or pneumatic) a second of it firing at its rate as your own gun (<see cref="OwnShotGain"/>,
    /// tails overlapping), a frag explosion (<see cref="FragExplosionGain"/>) landing at the start of that second on top;
    /// the median over the weapons. Also returns the gunfire-only and explosion-only RMS and the quietest and loudest
    /// weapons' combined RMS, for the log.
    /// </summary>
    public static (double Combined, double GunfireMedian, double Explosion, double Min, double Max, int Weapons) CombatReferenceRms()
    {
        const int sr = SampleRate;
        var explosion = Synth.Effect(Sfx.Explosion);
        var window = new float[sr];
        for (var i = 0; i < window.Length && i < explosion.Length; i++)
            window[i] = explosion[i] / 32768f * FragExplosionGain;
        var explosionRms = Rms(window);

        var combined = new List<double>();
        var gunfire = new List<double>();
        foreach (var w in Weapons.All)
        {
            if (w.Suppressor || w.Sound is SoundStyle.Suppressed or SoundStyle.Toy or SoundStyle.Zap or SoundStyle.Pneumatic || w.Rpm <= 0)
                continue;
            var shot = Synth.Gunshot(w);
            var gun = new float[sr];
            var period = 60.0 * sr / w.Rpm;
            for (var t = 0.0; t < sr; t += period)
            {
                var start = (int)t;
                for (var i = 0; i < shot.Length && start + i < gun.Length; i++)
                    gun[start + i] += shot[i] / 32768f * OwnShotGain;
            }
            gunfire.Add(Rms(gun));
            for (var i = 0; i < gun.Length; i++)
                gun[i] += window[i];
            combined.Add(Rms(gun));
        }
        if (combined.Count == 0)
            return (explosionRms, 0, explosionRms, explosionRms, explosionRms, 0);
        combined.Sort();
        gunfire.Sort();
        return (combined[combined.Count / 2], gunfire[gunfire.Count / 2], explosionRms, combined[0], combined[^1], combined.Count);
    }

    private static double Rms(float[] s)
    {
        double sum = 0;
        foreach (var x in s)
            sum += x * (double)x;
        return Math.Sqrt(sum / Math.Max(1, s.Length));
    }
}
