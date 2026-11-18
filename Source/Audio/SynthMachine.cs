namespace CallOfSlop;

/// <summary>
/// Machine sounds (Game/GameMachineSounds.cs plays them; Karl, 2026-11-17): the Shell Control Chair's helmet and neural
/// link, a robot shell starting up and powering down, a reactor's steady hum at light and heavy load (exact loops), and a Robot
/// Shell Dock's clamps closing and letting go. All made here from tones and noise, like every other sound in the game:
/// no samples, no licences. Deterministic for its arguments.
/// </summary>
public static partial class Synth
{
    /// <summary>A machine sound, or null for an effect this file doesn't make.</summary>
    private static short[]? MachineFoley(Sfx sfx)
    {
        if (sfx is < Sfx.ShellHelmetDown or > Sfx.DockRelease)
            return null;
        var rng = new Random(52000 + (int)sfx * 977);
        Buf b;
        switch (sfx)
        {
            case Sfx.ShellHelmetDown:
                // The helmet's arm comes over: a servo whir climbing as it swings, a pneumatic hiss, the seal's soft thunk
                // round your head, then the inside going quiet bar two little ready pips.
                b = new Buf(1.25f);
                Whir(b, rng, 0, 0.7f, 190, 320, 0.45f);
                Band(b, rng, 0.05f, 0.18f, 0.25f, 0.25f, 2500, 9000, length: 0.7f);
                Clunk(b, rng, 0.72f, 0.7f, 0.55f);
                Tone(b, 0.72f, 0.5f, 120, 70, 0.05f, 0.002f, 0.12f);
                Noise(b, rng, 0.73f, 0.25f, 0.002f, 0.08f, 600); // (the seal's rubber taking up)
                Tone(b, 0.95f, 0.12f, 1760, 1760, 1, 0.003f, 0.04f, length: 0.06f);
                Tone(b, 1.06f, 0.12f, 2349, 2349, 1, 0.003f, 0.05f, length: 0.07f);
                Echo(b, 0.12f, 0.35f); // (inside the helmet: a small, close room)
                return Finish(b, 0.55f);
            case Sfx.ShellLinkWake:
                // The link takes: a low throb that rises into a bright sweep, a shimmer of data chirps over it, and a soft
                // two-note chord as the robot's eyes open.
                b = new Buf(1.9f);
                Glide(b, 0, 1.2f, 70, 140, 0.35f, 0.3f, 0.3f, vibrato: 6f);
                Glide(b, 0.15f, 1.05f, 180, 2400, 0.32f, 0.5f, 0.15f, vibrato: 11f, harmonics: 0.25f);
                Glide(b, 0.2f, 1.0f, 270, 3600, 0.12f, 0.5f, 0.15f, vibrato: 13f);
                for (var k = 0; k < 22; k++) // (data: short random blips, thickest as the sweep tops out)
                {
                    var u = (float)rng.NextDouble();
                    var at = 0.25f + 0.95f * MathF.Sqrt(u);
                    Tone(b, at, 0.05f + 0.06f * u, 2600 + 3800 * (float)rng.NextDouble(), 2400 + 3000 * (float)rng.NextDouble(), 0.01f, 0.001f, 0.012f, length: 0.03f);
                }
                Band(b, rng, 0.9f, 0.2f, 0.25f, 0.18f, 3000, 12000, length: 0.6f); // (a whoosh as the view comes up)
                Tone(b, 1.22f, 0.3f, 659, 659, 1, 0.02f, 0.35f, length: 0.65f);
                Tone(b, 1.25f, 0.24f, 988, 988, 1, 0.02f, 0.3f, length: 0.62f);
                Tone(b, 1.27f, 0.1f, 1976, 1976, 1, 0.01f, 0.18f, length: 0.5f);
                Echo(b, 0.28f, 0.9f);
                return Finish(b, 0.55f);
            case Sfx.ShellUnlink:
                // Back to the body: the link spins down with a stutter, a falling two-note, then the helmet's seal lets go
                // with a hiss and its arm whirs away.
                b = new Buf(1.7f);
                Glide(b, 0, 0.75f, 2200, 150, 0.3f, 0.02f, 0.2f, vibrato: 9f, harmonics: 0.2f, stutter: 14f);
                Glide(b, 0.05f, 0.7f, 70, 40, 0.3f, 0.05f, 0.3f);
                Tone(b, 0.05f, 0.2f, 988, 988, 1, 0.01f, 0.12f, length: 0.18f);
                Tone(b, 0.2f, 0.2f, 659, 659, 1, 0.01f, 0.16f, length: 0.25f);
                Click(b, rng, 0.78f, 0.45f, 0.7f); // (the seal's catch)
                Band(b, rng, 0.8f, 0.4f, 0.01f, 0.16f, 1800, 9000, length: 0.5f); // (air in)
                Whir(b, rng, 0.9f, 0.65f, 320, 200, 0.4f);
                Clunk(b, rng, 1.55f, 0.35f, 0.7f);
                Echo(b, 0.15f, 0.5f);
                return Finish(b, 0.55f);
            case Sfx.ShellHelmetUp:
                // No link: a short low "no", the seal letting go and the arm whirring back up.
                b = new Buf(1.0f);
                Tone(b, 0, 0.3f, 330, 330, 1, 0.005f, 0.07f, length: 0.1f);
                Tone(b, 0.12f, 0.3f, 247, 247, 1, 0.005f, 0.1f, length: 0.14f);
                Click(b, rng, 0.25f, 0.4f, 0.7f);
                Band(b, rng, 0.26f, 0.3f, 0.01f, 0.12f, 1800, 9000, length: 0.4f);
                Whir(b, rng, 0.3f, 0.6f, 320, 200, 0.4f);
                return Finish(b, 0.5f);
            case Sfx.ShellBoot:
                return ShellBoot(rng);
            case Sfx.ShellPowerDown:
                // The wearer gone: the whine falls away, each joint's servo lets go in turn, the frame settles with a clunk
                // and the fan winds down.
                b = new Buf(1.9f);
                Glide(b, 0, 1.1f, 5200, 300, 0.05f, 0.01f, 0.4f);
                Whir(b, rng, 0.05f, 0.22f, 260, 150, 0.4f);
                Whir(b, rng, 0.3f, 0.2f, 330, 190, 0.35f);
                Whir(b, rng, 0.52f, 0.25f, 210, 120, 0.4f);
                Clunk(b, rng, 0.8f, 0.8f, 0.45f);
                Tone(b, 0.8f, 0.45f, 90, 55, 0.05f, 0.002f, 0.15f);
                Fan(b, rng, 0.1f, 1.7f, 1f, 0f, 0.22f);
                Click(b, rng, 1.2f, 0.25f, 1.1f); // (a relay opening)
                Echo(b, 0.2f, 0.8f);
                return Finish(b, 0.6f);
            case Sfx.ReactorHumLight:
                return ReactorHum(rng, heavy: false);
            case Sfx.ReactorHumHeavy:
                return ReactorHum(rng, heavy: true);
            case Sfx.DockClamp:
                // The clamp arms swing in, a heavy magnetic thunk takes the shell, two locking pins shoot home, a short
                // hiss as the hydraulics hold, and a low blip: charging.
                b = new Buf(1.35f);
                Whir(b, rng, 0, 0.32f, 160, 260, 0.45f);
                Clunk(b, rng, 0.34f, 1f, 0.4f);
                Tone(b, 0.34f, 0.8f, 95, 48, 0.04f, 0.002f, 0.2f);
                Tone(b, 0.34f, 0.18f, 612, 600, 0.2f, 0.001f, 0.25f); // (the frame ringing)
                Click(b, rng, 0.52f, 0.6f, 0.8f);
                Click(b, rng, 0.58f, 0.55f, 0.75f);
                Band(b, rng, 0.62f, 0.3f, 0.01f, 0.14f, 1500, 7000, length: 0.35f);
                Tone(b, 0.95f, 0.18f, 440, 440, 1, 0.01f, 0.08f, length: 0.13f);
                Tone(b, 1.08f, 0.18f, 880, 880, 1, 0.01f, 0.1f, length: 0.16f);
                Echo(b, 0.3f, 0.9f);
                return Finish(b, 0.65f);
            case Sfx.DockRelease:
                // A rising chirp, the pins drawing back, the hydraulics venting in a long hiss and the arms swinging out
                // with a lighter clack.
                b = new Buf(1.4f);
                Tone(b, 0, 0.18f, 660, 990, 0.05f, 0.005f, 0.12f, length: 0.18f);
                Click(b, rng, 0.2f, 0.55f, 0.7f);
                Click(b, rng, 0.25f, 0.5f, 0.65f);
                Band(b, rng, 0.28f, 0.45f, 0.01f, 0.3f, 1200, 8000, tremolo: 23f, length: 0.8f);
                Whir(b, rng, 0.45f, 0.35f, 260, 160, 0.4f);
                Clunk(b, rng, 0.82f, 0.6f, 0.6f);
                Tone(b, 0.82f, 0.4f, 140, 90, 0.04f, 0.002f, 0.1f);
                Echo(b, 0.3f, 0.9f);
                return Finish(b, 0.6f);
        }
        return null;
    }

    /// <summary>
    /// A robot shell starting up: a relay chain, a capacitor whine climbing out of hearing, each joint's servo tested in
    /// turn (low to high, a little apart), two actuator clunks as the frame takes its own weight, a cooling fan spinning up
    /// under it all, and a "ready" pair of tones.
    /// </summary>
    private static short[] ShellBoot(Random rng)
    {
        var b = new Buf(2.6f);
        for (var k = 0; k < 4; k++)
            Click(b, rng, 0.02f + 0.07f * k + 0.015f * (float)rng.NextDouble(), 0.5f - 0.06f * k, 0.6f + 0.1f * k);
        Tone(b, 0.05f, 0.35f, 60, 60, 1, 0.01f, 0.08f, length: 0.25f); // (power on: a thump of mains)
        Glide(b, 0.2f, 1.4f, 600, 7200, 0.05f, 0.3f, 0.2f);
        ReadOnlySpan<float> servo = [150f, 205f, 240f, 300f, 360f];
        for (var k = 0; k < servo.Length; k++)
        {
            var at = 0.45f + 0.22f * k;
            Whir(b, rng, at, 0.16f, servo[k], servo[k] * 1.6f, 0.38f);
            Whir(b, rng, at + 0.18f, 0.12f, servo[k] * 1.5f, servo[k], 0.28f);
        }
        Clunk(b, rng, 1.55f, 0.85f, 0.45f);
        Tone(b, 1.55f, 0.4f, 110, 60, 0.04f, 0.002f, 0.12f);
        Clunk(b, rng, 1.72f, 0.7f, 0.5f);
        Fan(b, rng, 0.3f, 2.2f, 0f, 1f, 0.2f);
        Tone(b, 2.0f, 0.22f, 784, 784, 1, 0.01f, 0.08f, length: 0.12f);
        Tone(b, 2.13f, 0.22f, 1175, 1175, 1, 0.01f, 0.16f, length: 0.3f);
        Echo(b, 0.22f, 0.8f);
        return Finish(b, 0.62f);
    }

    /// <summary>The loops' length (s): every tone in them is a whole number of cycles in it, so the seam can't click.</summary>
    private const float HumSeconds = 4f;

    /// <summary>
    /// A reactor's hum, an exact loop and a steady tone (Karl, 2026-11-18: no pulse, no beat, no climb). Both loops sit on
    /// the same 55 Hz, so fading between them by load changes how full the hum is, never its pitch. Light: the 55 Hz hum
    /// with its octaves, a faint steady high whine and a breath of coolant. Heavy: the same hum with a sub octave under it,
    /// its harmonics driven harder and more coolant rush. Nothing in either is modulated, and the coolant's noise runs four
    /// seconds before it repeats, too long to hear as a pattern.
    /// </summary>
    private static short[] ReactorHum(Random rng, bool heavy)
    {
        var b = new Buf(HumSeconds);
        var len = b.Len;
        // The coolant: noise through a band, twice round so the filters' state matches at the seam.
        var noise = new float[len];
        float lp = 0, hp = 0;
        var aLo = heavy ? 0.02f : 0.012f;
        var aHi = heavy ? 0.12f : 0.07f;
        for (var pass = 0; pass < 2; pass++)
            for (var i = 0; i < len; i++)
            {
                if (pass == 0)
                    noise[i] = (float)(rng.NextDouble() * 2 - 1);
                lp += aHi * (noise[i] - lp);
                hp += aLo * (lp - hp);
                if (pass == 1)
                    b.S[i] = (lp - hp) * (heavy ? 0.26f : 0.12f);
            }
        for (var i = 0; i < len; i++)
        {
            var t = i / (float)Sr; // (0 .. 4 s; every frequency below is a multiple of 0.25 Hz)
            var core = 0.55f * MathF.Sin(MathF.Tau * 55f * t)
                + 0.3f * MathF.Sin(MathF.Tau * 110f * t + 0.4f)
                + 0.12f * MathF.Sin(MathF.Tau * 165f * t + 1.1f)
                + 0.07f * MathF.Sin(MathF.Tau * 220f * t + 2.0f);
            float s;
            if (!heavy)
                s = core + 0.025f * MathF.Sin(MathF.Tau * 1760f * t);
            else
            {
                s = MathF.Tanh(core * 1.8f) * 0.75f; // (driven: fuller harmonics)
                s += 0.35f * MathF.Sin(MathF.Tau * 27.5f * t + 0.2f); // (the sub)
                s += 0.06f * MathF.Sin(MathF.Tau * 275f * t + 0.7f);
                s += 0.03f * MathF.Sin(MathF.Tau * 1760f * t);
            }
            b.S[i] += s;
        }
        return FinishLoop(b, heavy ? 0.7f : 0.6f);
    }

    /// <summary>
    /// A tone gliding from <paramref name="f0"/> to <paramref name="f1"/> evenly in pitch over <paramref name="dur"/>,
    /// faded in and out, with a little vibrato, optional square-ish harmonics and an optional stutter (on and off at that rate).
    /// </summary>
    private static void Glide(Buf b, float start, float dur, float f0, float f1, float gain, float fadeIn, float fadeOut,
        float vibrato = 0f, float harmonics = 0f, float stutter = 0f)
    {
        var i0 = (int)(start * Sr);
        var n = (int)(dur * Sr);
        double phase = 0;
        var ratio = f1 / f0;
        for (var k = 0; k < n && i0 + k < b.Len; k++)
        {
            var t = k / (float)Sr;
            var u = t / dur;
            var f = f0 * MathF.Pow(ratio, u) * (1f + (vibrato > 0 ? 0.008f * MathF.Sin(MathF.Tau * vibrato * t) : 0f));
            phase += 2 * Math.PI * f / Sr;
            var s = (float)Math.Sin(phase);
            if (harmonics > 0)
                s += harmonics * ((float)Math.Sin(phase * 3) * 0.33f + (float)Math.Sin(phase * 5) * 0.2f);
            var env = MathF.Min(1f, t / MathF.Max(fadeIn, 1e-3f)) * MathF.Min(1f, (dur - t) / MathF.Max(fadeOut, 1e-3f));
            if (stutter > 0)
                env *= MathF.Sin(MathF.Tau * stutter * t) > -0.3f ? 1f : 0.25f;
            b.S[i0 + k] += s * gain * env;
        }
    }

    /// <summary>A servo motor: a buzzy tone gliding <paramref name="f0"/> to <paramref name="f1"/>, its gear teeth as a rattle of noise at a quarter of its pitch, faded in and out.</summary>
    private static void Whir(Buf b, Random rng, float start, float dur, float f0, float f1, float gain)
    {
        var i0 = (int)(start * Sr);
        var n = (int)(dur * Sr);
        double phase = 0, teeth = 0;
        float lp = 0;
        var ratio = f1 / f0;
        var fade = MathF.Min(0.04f, dur * 0.3f);
        for (var k = 0; k < n && i0 + k < b.Len; k++)
        {
            var t = k / (float)Sr;
            var f = f0 * MathF.Pow(ratio, t / dur);
            phase += 2 * Math.PI * f / Sr;
            teeth += 2 * Math.PI * f * 0.25 / Sr;
            var s = (float)Math.Sin(phase) * 0.6f + (float)Math.Sin(phase * 2) * 0.3f + (float)Math.Sin(phase * 3) * 0.2f + (float)Math.Sin(phase * 4.02) * 0.1f;
            lp += 0.25f * ((float)(rng.NextDouble() * 2 - 1) - lp);
            var gear = lp * MathF.Pow(0.5f + 0.5f * (float)Math.Sin(teeth), 4f) * 0.8f;
            var env = MathF.Min(1f, t / fade) * MathF.Min(1f, (dur - t) / fade);
            b.S[i0 + k] += (s + gear) * gain * env;
        }
    }

    /// <summary>A small cooling fan: blade-pass noise whose speed goes from <paramref name="from"/> to <paramref name="to"/> (0..1 of full) over <paramref name="dur"/>.</summary>
    private static void Fan(Buf b, Random rng, float start, float dur, float from, float to, float gain)
    {
        var i0 = (int)(start * Sr);
        var n = (int)(dur * Sr);
        double blade = 0;
        float lp = 0, hp = 0;
        for (var k = 0; k < n && i0 + k < b.Len; k++)
        {
            var t = k / (float)Sr;
            var u = t / dur;
            var speed = from + (to - from) * (to > from ? 1f - (1f - u) * (1f - u) : u * (2f - u));
            blade += 2 * Math.PI * (8 + 140 * speed) / Sr;
            var a = 0.02f + 0.2f * speed;
            lp += a * ((float)(rng.NextDouble() * 2 - 1) - lp);
            hp += 0.01f * (lp - hp);
            var am = 0.7f + 0.3f * (float)Math.Sin(blade);
            var env = MathF.Min(1f, t / 0.05f) * MathF.Min(1f, (dur - t) / 0.15f);
            b.S[i0 + k] += (lp - hp) * am * gain * (0.2f + 0.8f * speed) * env + (float)Math.Sin(blade * 2) * 0.05f * gain * speed * env;
        }
    }
}
