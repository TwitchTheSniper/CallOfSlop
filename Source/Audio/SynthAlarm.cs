namespace CallOfSlop;

/// <summary>The penal colony's lockdown siren (Game/GameColonyLockdown.cs), synthesised like every other effect.</summary>
public static partial class Synth
{
    /// <summary>The siren, or null for an effect this file doesn't make.</summary>
    private static short[]? AlarmFoley(Sfx sfx)
    {
        if (sfx == Sfx.Tannoy)
            return TannoyFoley();
        if (sfx != Sfx.ColonyAlarm)
            return null;
        // One wail of an old air-raid style siren over a tannoy: a buzzy tone rising and falling over 2 s, a fifth above
        // it, a slow wobble, and a long outdoor echo.
        var b = new Buf(2.0f);
        double ph = 0, ph5 = 0;
        for (var i = 0; i < b.Len; i++)
        {
            var t = i / (float)Sr;
            var rise = 0.5f - 0.5f * MathF.Cos(t * MathF.Tau / 2f); // 0, up to 1 at 1 s, back to 0
            var f = 420f + 460f * rise + 6f * MathF.Sin(t * MathF.Tau * 5.5f);
            ph += 2 * Math.PI * f / Sr;
            ph5 += 2 * Math.PI * f * 1.5 / Sr;
            var saw = (float)(ph / (2 * Math.PI) % 1.0) * 2f - 1f;
            var s = 0.5f * (float)Math.Sin(ph) + 0.22f * saw + 0.18f * (float)Math.Sin(ph5);
            var env = MathF.Min(1f, t / 0.06f) * MathF.Min(1f, (2f - t) / 0.12f);
            b.S[i] = s * env;
        }
        Echo(b, 0.35f, 2.2f);
        return Finish(b, 0.85f);
    }

    /// <summary>
    /// A tannoy: two chimes ("bing, bong"), then four seconds of an announcement nobody can make out: a buzzy voice
    /// through a horn loudspeaker (no bass, a nasal peak), in syllables, with the yard's echo.
    /// </summary>
    private static short[] TannoyFoley()
    {
        var b = new Buf(5.6f);
        var rng = new Random(4107);
        // The chimes.
        for (var n = 0; n < 2; n++)
        {
            var f = n == 0 ? 659.3f : 523.3f;
            var at = (int)(n * 0.55f * Sr);
            for (var i = at; i < Math.Min(b.Len, at + (int)(1.1f * Sr)); i++)
            {
                var t = (i - at) / (float)Sr;
                b.S[i] += (MathF.Sin(t * MathF.Tau * f) + 0.35f * MathF.Sin(t * MathF.Tau * f * 2.76f)) * MathF.Exp(-t * 4.5f) * MathF.Min(1f, t / 0.004f) * 0.6f;
            }
        }
        // The voice: a sawtooth whose pitch wanders, cut into syllables, through two moving formants.
        double ph = 0;
        float lp1 = 0, lp2 = 0, hp = 0, prev = 0;
        float syl = 0, sylLen = 0.16f, pitch = 150f, f1 = 700f, f2 = 1500f, loud = 1f;
        for (var i = (int)(1.45f * Sr); i < b.Len; i++)
        {
            var t = i / (float)Sr;
            syl += 1f / Sr;
            if (syl >= sylLen)
            {
                syl = 0;
                sylLen = 0.09f + 0.16f * (float)rng.NextDouble();
                pitch = 125f + 70f * (float)rng.NextDouble();
                f1 = 500f + 500f * (float)rng.NextDouble();
                f2 = 1200f + 900f * (float)rng.NextDouble();
                loud = rng.NextDouble() < 0.14 ? 0f : 0.6f + 0.4f * (float)rng.NextDouble(); // (a breath now and then)
            }
            ph += pitch / Sr;
            var saw = (float)(ph % 1.0) * 2f - 1f;
            // (two one-pole band shapes: cheap formants)
            lp1 += (saw - lp1) * MathF.Min(1f, MathF.Tau * f1 / Sr);
            lp2 += (saw - lp2) * MathF.Min(1f, MathF.Tau * f2 / Sr);
            var voiced = lp1 * 0.6f + (lp2 - lp1) * 0.9f;
            hp += (voiced - hp) * MathF.Min(1f, MathF.Tau * 420f / Sr); // (the horn has no bass)
            var s = voiced - hp;
            var env = MathF.Min(1f, syl / 0.015f) * MathF.Min(1f, (sylLen - syl) / 0.03f) * loud;
            var tail = MathF.Min(1f, (b.Len / (float)Sr - 0.5f - t) / 0.2f);
            var o = MathF.Tanh(s * 3.2f) * env * MathF.Max(tail, 0f) * 0.5f; // (overdriven: it is a cheap loudspeaker)
            b.S[i] += o * 0.8f + prev * 0.2f;
            prev = o;
        }
        Echo(b, 0.3f, 1.4f);
        return Finish(b, 0.8f);
    }
}
