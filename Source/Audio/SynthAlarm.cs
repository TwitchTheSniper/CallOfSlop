namespace CallOfSlop;

/// <summary>The penal colony's lockdown siren (Game/GameColonyLockdown.cs), synthesised like every other effect.</summary>
public static partial class Synth
{
    /// <summary>The siren, or null for an effect this file doesn't make.</summary>
    private static short[]? AlarmFoley(Sfx sfx)
    {
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
}
