using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>The Launcher class's frenzy signature (<see cref="FrenzySignature.Shockwave"/>), hooked from the launchers' blast effects.</summary>
public static partial class FrenzyFx
{
    /// <summary>
    /// A launcher blast (Game.OnRocketBoom): a frenzied shooter's blast throws out a flat ring of sparks in the class
    /// colour (a shockwave that grows with the blast and the level) and a short column of embers. Allocation-free; any
    /// class gets its colours, the ring is the Launcher look's own.
    /// </summary>
    public static void OnLauncherBlast(Game g, Player? shooter, Vector3 pos, float radius, double now)
    {
        if (Mode == FrenzyVisualMode.Off || shooter == null)
            return;
        var level = LevelOf(g, shooter);
        if (level <= 0)
            return;
        var t0 = Stopwatch.GetTimestamp();
        var look = FrenzyLooks.Get(ClassOf(g, shooter));
        var k = FrenzyLooks.Intensity(level, Mode);
        var lod = Lod(pos);
        if (lod > 0f)
        {
            var tc = look.Rainbow ? FrenzyLooks.Hue((float)(now * 0.35 % 1.0)) : look.Color;
            var ring = look.Signature == FrenzySignature.Shockwave ? Count((10 + look.ImpactSparks * 1.5f) * k * lod) : Count(look.ImpactSparks * k * lod);
            var speed = look.SparkSpeed * (0.6f + radius * 0.12f);
            for (var i = 0; i < ring; i++)
            {
                var a = i * MathF.Tau / Math.Max(1, ring) + Rand() * 0.2f;
                var v = new Vector3(MathF.Cos(a), 0.05f + Rand() * 0.1f, MathF.Sin(a)) * speed;
                FrenzyParticles.Spark(pos + new Vector3(0, 0.2f, 0), v, Vector3.Lerp(tc, look.Accent, Rand() * 0.5f), 0.03f + 0.02f * k, look.SparkLife * (0.7f + 0.5f * Rand()), 0f, 2.2f, 0.08f);
            }
            if (look.Signature == FrenzySignature.Shockwave)
            {
                var embers = Count(8 * k * lod);
                for (var i = 0; i < embers; i++)
                {
                    var v = new Vector3((Rand() - 0.5f) * 2f, 3f + Rand() * 4f, (Rand() - 0.5f) * 2f);
                    FrenzyParticles.Spark(pos, v, look.Accent, 0.04f, 0.9f + Rand() * 0.5f, look.SparkGravity, 1.2f, 0.05f);
                }
            }
        }
        _cpuTicks += Stopwatch.GetTimestamp() - t0;
    }
}
