using System.Numerics;

namespace CallOfSlop;

/// <summary>Drawing the lingering patches built grenades leave (GameGrenades.cs): a few boxes each, a fixed pool, no allocation.</summary>
public static partial class Program
{
    private static void DrawGrenadeZones(double now)
    {
        var zones = _game.GrenadeZones;
        for (var i = 0; i < zones.Length; i++)
        {
            ref readonly var z = ref zones[i];
            if (!z.Active)
                continue;
            var age = (float)(now - z.Born);
            var left = (float)(z.Until - now);
            var fade = Math.Clamp(left / 1.2f, 0f, 1f) * Math.Clamp(age / 0.3f, 0f, 1f);
            var tint = GrenadeDef.PayloadTint(z.Kind);
            switch (z.Kind)
            {
                case PayloadKind.Incendiary:
                    for (var k = 0; k < 8; k++)
                    {
                        // Tongues of flame round the patch, flickering.
                        var a = k * MathF.Tau / 8 + i;
                        var d = z.Radius * (0.25f + 0.55f * ((k * 37 + i * 11) % 7) / 7f);
                        var flick = 0.6f + 0.4f * MathF.Sin(age * 13f + k * 2.1f);
                        var at = z.Pos + new Vector3(MathF.Cos(a) * d, 0.25f * flick, MathF.Sin(a) * d);
                        Gfx.Draw(Matrix4x4.CreateScale(0.45f, 0.6f * flick, 0.45f) * Matrix4x4.CreateTranslation(at), Vector3.Lerp(tint, new Vector3(1f, 0.85f, 0.3f), flick * 0.5f), 0.7f * fade, mode: 2);
                    }
                    Gfx.Draw(Matrix4x4.CreateScale(z.Radius * 1.8f, 0.04f, z.Radius * 1.8f) * Matrix4x4.CreateTranslation(z.Pos with { Y = z.Pos.Y - 0.1f }), tint * 0.6f, 0.35f * fade, mode: 2);
                    break;
                case PayloadKind.Gas:
                    for (var k = 0; k < 7; k++)
                    {
                        var a = k * MathF.Tau / 7 + age * 0.15f;
                        var d = z.Radius * 0.55f;
                        var at = z.Pos + new Vector3(MathF.Cos(a) * d, 0.8f + 0.2f * MathF.Sin(age + k), MathF.Sin(a) * d);
                        Gfx.Draw(Matrix4x4.CreateScale(z.Radius * 0.7f) * Matrix4x4.CreateRotationY(k) * Matrix4x4.CreateTranslation(at), tint, 0.3f * fade, mode: 2);
                    }
                    break;
                case PayloadKind.Lure:
                {
                    var pulse = 0.5f + 0.5f * MathF.Sin(age * 9f);
                    Gfx.Draw(Matrix4x4.CreateScale(0.12f, 3f, 0.12f) * Matrix4x4.CreateTranslation(z.Pos + new Vector3(0, 1.5f, 0)), tint, (0.3f + 0.4f * pulse) * fade, mode: 2);
                    Gfx.Draw(Matrix4x4.CreateScale(0.6f + pulse * 0.5f) * Matrix4x4.CreateTranslation(z.Pos + new Vector3(0, 0.3f, 0)), tint, 0.35f * fade, mode: 2);
                    break;
                }
            }
        }
    }
}
