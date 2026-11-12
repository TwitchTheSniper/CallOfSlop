namespace CallOfSlop;

/// <summary>
/// The cold storm's wind (<see cref="Audio.StormWind"/>): a stereo loop of a low roar, a howl that rises and falls with the
/// gusts (noise through a narrow band whose note wanders) and the hiss of snow, each side its own noise so it is wide.
/// Everything that moves is a sum of whole cycles over the loop, and the last half second is crossfaded into the first,
/// so it loops without a seam. Made once, off the main thread, the first time a storm is heard.
/// </summary>
public static partial class Synth
{
    /// <summary>The wind loop's length, seconds.</summary>
    public const float WindSeconds = 12f;

    /// <summary>The wind loop: interleaved stereo, 16-bit, <see cref="Audio.SampleRate"/>.</summary>
    public static short[] Wind()
    {
        const int rate = Audio.SampleRate;
        var n = (int)(WindSeconds * rate);
        var fade = rate / 2;
        var raw = new float[2 * (n + fade)];
        for (var ch = 0; ch < 2; ch++)
        {
            var seed = 0x9E3779B9u ^ (uint)(ch * 0x85EBCA6B + 17);
            float lp1 = 0, lp2 = 0, hp = 0, low = 0, band = 0, low2 = 0, band2 = 0;
            var phase = ch * 0.9;
            for (var i = 0; i < n + fade; i++)
            {
                var t = (double)i / n; // (0..1 over the loop: whole cycles of it repeat exactly)
                seed ^= seed << 13;
                seed ^= seed >> 17;
                seed ^= seed << 5;
                var w = (seed * (1f / uint.MaxValue)) * 2f - 1f;

                // The gusts: how hard it blows from moment to moment.
                var gust = (float)(0.55 + 0.25 * Math.Sin(Math.Tau * (3 * t) + phase) + 0.12 * Math.Sin(Math.Tau * (7 * t) + 1.7 + phase)
                    + 0.08 * Math.Sin(Math.Tau * (13 * t) + 0.4));

                // The roar: noise twice through a low one-pole (about 300 Hz).
                lp1 += (w - lp1) * 0.042f;
                lp2 += (lp1 - lp2) * 0.042f;
                var roar = lp2 * 5.5f * (0.6f + 0.6f * gust);

                // The howl: a narrow band (Chamberlin state-variable filter) whose note climbs with the gust.
                var fc = 330.0 + 300.0 * gust + 60.0 * Math.Sin(Math.Tau * (5 * t) + phase * 2);
                var f = (float)(2 * Math.Sin(Math.PI * fc / rate));
                low += f * band;
                var high = w - low - 0.09f * band;
                band += f * high;
                // A second, higher whistle in the strongest gusts.
                var fc2 = fc * 2.03;
                var f2 = (float)(2 * Math.Sin(Math.PI * fc2 / rate));
                low2 += f2 * band2;
                var high2 = w - low2 - 0.06f * band2;
                band2 += f2 * high2;
                var g2 = MathF.Max(gust - 0.6f, 0f) * 2.5f;
                var howl = band * 0.11f * gust * gust + band2 * 0.05f * g2 * g2;

                // The hiss of snow driven past: noise above a few kHz.
                hp += (w - hp) * 0.35f;
                var hiss = (w - hp) * 0.16f * (0.4f + 0.8f * gust);

                raw[2 * i + ch] = roar + howl + hiss;
            }
        }

        // Crossfade the tail into the head (equal power: noise against noise), then scale to a safe peak.
        var mixd = new float[2 * n];
        for (var i = 0; i < n; i++)
            for (var ch = 0; ch < 2; ch++)
            {
                var v = raw[2 * i + ch];
                if (i < fade)
                {
                    var a = (float)i / fade;
                    v = v * MathF.Sqrt(a) + raw[2 * (n + i) + ch] * MathF.Sqrt(1f - a);
                }
                mixd[2 * i + ch] = v;
            }
        var peak = 1e-6f;
        foreach (var v in mixd)
            peak = MathF.Max(peak, MathF.Abs(v));
        var scale = 0.8f / peak * short.MaxValue;
        var pcm = new short[2 * n];
        for (var i = 0; i < pcm.Length; i++)
            pcm[i] = (short)Math.Clamp(mixd[i] * scale, short.MinValue, short.MaxValue);
        return pcm;
    }
}
