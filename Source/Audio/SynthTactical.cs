namespace CallOfSlop;

/// <summary>The tactical equipment's sounds, synthesised like every other effect (no samples). Deterministic per variant.</summary>
public static partial class Synth
{
    /// <summary>A tactical effect, variant <paramref name="v"/>; null for an effect this file doesn't make.</summary>
    private static short[]? TacticalFoley(Sfx sfx, int v)
    {
        var rng = new Random(9100 + (int)sfx * 197 + v * 7919);
        Buf b;
        switch (sfx)
        {
            case Sfx.FlashBang:
                // A hard, bright crack (much sharper than a frag's boom), a short metallic ring and a thin tail.
                b = new Buf(1.1f);
                Noise(b, rng, 0, 1f, 0.0002f, 0.012f, 9000, highpass: true);
                Noise(b, rng, 0, 0.9f, 0.0005f, 0.05f, 3500);
                Tone(b, 0, 0.6f, 180, 60, 0.03f, 0.001f, 0.08f);
                Tone(b, 0.002f, 0.25f, 3100, 2900, 0.2f, 0.001f, 0.18f, harmonics: 0.3f);
                Band(b, rng, 0.01f, 0.25f, 0.01f, 0.25f, 1500, 6000);
                Echo(b, 0.35f, 1.2f);
                return Finish(b, 0.85f);
            case Sfx.StunPop:
                // A dull thump with a wobbling electric warble.
                b = new Buf(0.9f);
                Tone(b, 0, 0.9f, 140, 50, 0.05f, 0.002f, 0.12f);
                Noise(b, rng, 0, 0.6f, 0.001f, 0.06f, 1200);
                Band(b, rng, 0.02f, 0.4f, 0.01f, 0.25f, 600, 2600, tremolo: 17);
                Tone(b, 0.02f, 0.3f, 900, 420, 0.25f, 0.01f, 0.3f, harmonics: 0.5f);
                return Finish(b, 0.75f);
            case Sfx.Tinnitus:
                // A clean, high ringing (two close sines beating slowly) that fades over about three seconds. Soft: it's a cue, not a punishment.
                b = new Buf(3.4f);
                Tone(b, 0, 0.35f, 3520, 3500, 2, 0.05f, 1.1f);
                Tone(b, 0, 0.2f, 3527, 3507, 2, 0.08f, 1.0f);
                return Finish(b, 0.4f);
            case Sfx.TrophyZap:
                // A fast electric snap with a falling zip, then a small pop where the grenade dies.
                b = new Buf(0.4f);
                Tone(b, 0, 0.6f, 4200, 900, 0.03f, 0.0005f, 0.05f, harmonics: 0.6f);
                Noise(b, rng, 0, 0.5f, 0.0003f, 0.01f, 7000, highpass: true);
                Noise(b, rng, 0.05f, 0.5f, 0.001f, 0.04f, 1800);
                Tone(b, 0.05f, 0.4f, 300, 120, 0.03f, 0.001f, 0.05f);
                return Finish(b, 0.65f);
            case Sfx.MotionPing:
                // HUD ping: a soft sonar blip, quick and unobtrusive.
                b = new Buf(0.35f);
                Tone(b, 0, 0.5f, 1320, 1180, 0.08f, 0.003f, 0.08f);
                Tone(b, 0.06f, 0.25f, 1760, 1600, 0.08f, 0.003f, 0.06f);
                return Finish(b, 0.4f);
            case Sfx.SensorBeep:
                // The sensor in the world: a short, dry electronic chirp.
                b = new Buf(0.15f);
                Tone(b, 0, 0.6f, 2400, 2400, 1, 0.001f, 0.025f, harmonics: 0.4f);
                Tone(b, 0.04f, 0.4f, 2900, 2900, 1, 0.001f, 0.02f, harmonics: 0.4f);
                return Finish(b, 0.4f);
            case Sfx.StimHiss:
                // An injector: a click, a pressurised hiss and a low rush.
                b = new Buf(0.6f);
                Click(b, rng, 0, 0.6f, 1.2f);
                Noise(b, rng, 0.01f, 0.6f, 0.02f, 0.12f, 6500, highpass: true);
                Tone(b, 0.03f, 0.35f, 90, 140, 0.2f, 0.05f, 0.2f);
                return Finish(b, 0.55f);
            case Sfx.DeployDevice:
                // Setting something down: a plastic-and-metal clack and a small servo whirr.
                b = new Buf(0.45f);
                Clunk(b, rng, 0, 0.7f, 1.1f);
                Band(b, rng, 0.06f, 0.25f, 0.03f, 0.08f, 900, 2200, tremolo: 60);
                Tone(b, 0.07f, 0.2f, 700, 1100, 0.1f, 0.02f, 0.08f);
                return Finish(b, 0.5f);
        }
        return null;
    }
}
