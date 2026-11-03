namespace CallOfSlop;

/// <summary>
/// A modular engine's sound (EngineSounds.cs plays it): a loop of the firing pulses of a given number of cylinders over two
/// turns of the crank, so a single thumps, a V8 burbles and a W16 hums, and pitching it by the rpm keeps its character. The
/// loop is exactly periodic: a pulse's tail wraps round to the start. Every second variant has one dead cylinder.
/// </summary>
public static partial class Synth
{
    /// <summary>The cylinder counts that have a loop (others take the nearest at or below).</summary>
    public static readonly int[] EngineLoopCylinders = [1, 2, 3, 4, 6, 8, 12, 16];

    public const int EngineLoopVariants = 16;

    /// <summary>The rpm the loops are recorded at: playback pitch is rpm over this.</summary>
    public const float EngineLoopRpm = 1800f;

    private const int EngineLoopCycles = 8; // lengths of two turns: pulses differ a little, so it doesn't buzz

    /// <summary>The variant for a cylinder count, misfiring or not.</summary>
    public static int EngineLoopVariant(int cylinders, bool misfire)
    {
        var k = 0;
        for (var i = 0; i < EngineLoopCylinders.Length; i++)
            if (EngineLoopCylinders[i] <= cylinders)
                k = i;
        return k * 2 + (misfire ? 1 : 0);
    }

    private static short[] EngineLoop(int v)
    {
        var cylinders = EngineLoopCylinders[v / 2];
        var misfire = v % 2 == 1;
        var rng = new Random(83000 + v * 131);
        var seconds = EngineLoopCycles * 2 * 60f / EngineLoopRpm;
        var b = new Buf(seconds);
        var len = b.Len;
        var gap = (float)len / (EngineLoopCycles * cylinders);
        var lumps = new float[cylinders];
        for (var k = 0; k < cylinders; k++)
            lumps[k] = 0.75f + 0.5f * (float)rng.NextDouble(); // (each cylinder has its own weight: the engine's lope)
        var tail = (int)(0.09f * Sr);
        var body = 95f + 900f / (cylinders + 4); // (few cylinders: a barkier note)
        for (var p = 0; p < EngineLoopCycles * cylinders; p++)
        {
            var cyl = p % cylinders;
            if (misfire && cyl == 0 && (cylinders > 1 || p % 2 == 1))
                continue; // (a single only misses every other beat: it keeps sounding)
            var gain = lumps[cyl] * (0.9f + 0.2f * (float)rng.NextDouble());
            var start = (int)(p * gap);
            float lp = 0;
            for (var i = 0; i < tail; i++)
            {
                var t = i / (float)Sr;
                var noise = (float)(rng.NextDouble() * 2 - 1);
                lp += 0.09f * (noise - lp);
                var sample = (MathF.Sin(MathF.Tau * body * t) * 0.8f * MathF.Exp(-t * 60f) + MathF.Sin(MathF.Tau * body * 0.5f * t) * 0.6f * MathF.Exp(-t * 28f) + lp * 2.4f * MathF.Exp(-t * 90f)) * gain;
                b.S[(start + i) % len] += sample;
            }
        }
        return FinishLoop(b, 0.7f);
    }

    /// <summary>Like <c>Finish</c> with no fade at the end: a loop runs straight into its start.</summary>
    private static short[] FinishLoop(Buf b, float loudness)
    {
        var peak = 1e-6f;
        for (var i = 0; i < b.Len; i++)
        {
            b.S[i] = MathF.Tanh(b.S[i] * 1.2f);
            peak = MathF.Max(peak, MathF.Abs(b.S[i]));
        }
        var scale = loudness / peak;
        var pcm = new short[b.Len];
        for (var i = 0; i < b.Len; i++)
            pcm[i] = (short)Math.Clamp(b.S[i] * scale * 32767f, short.MinValue, short.MaxValue);
        return pcm;
    }
}
