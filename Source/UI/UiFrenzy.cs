using Brutal.ImGuiApi;
using Brutal.Numerics;
using Vec3 = System.Numerics.Vector3;

namespace CallOfSlop;

/// <summary>
/// The frenzy visuals on the screen (state from <see cref="FrenzyFx"/>, looks from <see cref="FrenzyLooks"/>): a tint in
/// the class colour at the screen edges that pulses with the frenzy music, the class's screen effect (SMG speed lines,
/// Sniper kill pulse, Shotgun embers, Special chromatic flicker), and the FRENZY meter (class icon, six segments, label)
/// just above the kill streak HUD. Everything stays in the outer band of the screen (<see cref="FrenzyLooks.EdgeDepth"/>),
/// low alpha, nothing faster than <see cref="FrenzyLooks.MaxFlashHz"/>. Drawn on the background draw list (menus cover
/// it), no allocations.
/// </summary>
public sealed partial class Ui
{
    private static ImColor8 FCol(Vec3 c, float a) =>
        new((byte)(Math.Clamp(c.X, 0f, 1f) * 255), (byte)(Math.Clamp(c.Y, 0f, 1f) * 255), (byte)(Math.Clamp(c.Z, 0f, 1f) * 255), (byte)(Math.Clamp(a, 0f, 1f) * 255));

    private void DrawFrenzyHud(Game game, double now, float2 pos, float2 size)
    {
        if (FrenzyFx.Mode == FrenzyVisualMode.Off || game.Local is not { Alive: true } me || game.Phase != MatchPhase.Playing)
            return;
        var level = FrenzyFx.LocalShownLevel;
        if (level <= 0)
            return;
        var look = FrenzyFx.LocalLook;
        var dl = ImGui.GetBackgroundDrawList();
        var screen = ImGui.GetIO().DisplaySize;
        var mode = FrenzyFx.Mode;
        var k = FrenzyLooks.Intensity(level, mode);
        var col = look.Rainbow ? FrenzyLooks.Hue((float)(now * 0.15 % 1.0)) : look.Color;

        // ---- Edge tint: the class colour at the edges, fading to nothing a sixth of the way in -------------------------
        var depth = screen.Y * FrenzyLooks.EdgeDepth;
        var edge = FrenzyLooks.EdgeAlpha(look, level, FrenzyFx.MusicLevel, mode, now);
        EdgeBands(dl, screen, depth, col, edge);

        if (FrenzyLooks.ScreenEffects(mode))
        {
            switch (look.Signature)
            {
                case FrenzySignature.KillPulse:
                {
                    // One slow swell of a deeper, wider vignette after each kill.
                    var p = FrenzyFx.KillPulse(now);
                    if (p > 0)
                        EdgeBands(dl, screen, depth * 1.35f, new Vec3(0.05f, 0.1f, 0.25f), MathF.Min(0.22f * p * k, FrenzyLooks.MaxEdgeAlpha));
                    break;
                }
                case FrenzySignature.SpeedLines:
                    SpeedLines(dl, screen, now, col, k, me.RenderVelocity);
                    break;
                case FrenzySignature.Embers:
                    ScreenEmbers(dl, screen, now, look, k);
                    break;
                case FrenzySignature.Glitch:
                {
                    // Chromatic split at the edges: a red band left, cyan right, eased on and off at 2.5 Hz.
                    var f = FrenzyLooks.Flicker(now) * 0.14f * k;
                    if (f > 0.004f)
                    {
                        var w = screen.Y * 0.05f;
                        var r = FCol(new Vec3(1f, 0.1f, 0.2f), f);
                        var c = FCol(new Vec3(0.1f, 1f, 0.95f), f);
                        var clear = FCol(Vec3.Zero, 0f);
                        dl.AddRectFilledMultiColor(new float2(0, 0), new float2(w, screen.Y), r, clear, clear, r);
                        dl.AddRectFilledMultiColor(new float2(screen.X - w, 0), new float2(screen.X, screen.Y), clear, c, c, clear);
                    }
                    break;
                }
            }
        }

        // ---- The meter: icon, six segments, label; just above the kill streak HUD -----------------------------------
        var font = ImGui.GetFontSize();
        var seg = 18 * _scale;
        var gap = 3 * _scale;
        var segH = 6 * _scale;
        var total = 6 * seg + 5 * gap;
        var cy = pos.Y + size.Y - 70 * _scale - 19 * _scale - font * 3.2f - 22 * _scale;
        var x0 = pos.X + size.X / 2 - total / 2 + 12 * _scale;
        var beat = 0.75f + 0.25f * FrenzyLooks.Pulse(now, FrenzyLooks.PulseHz(FrenzyFx.MusicLevel));
        DrawFrenzyIcon(dl, look.Class, new float2(x0 - 18 * _scale, cy + segH / 2), 9 * _scale, col);
        for (var i = 0; i < 6; i++)
        {
            var a = new float2(x0 + i * (seg + gap), cy);
            var lit = i < level;
            var c = look.Rainbow ? FrenzyLooks.Hue(i / 6f + (float)(now * 0.15 % 1.0)) : col;
            dl.AddRectFilled(a, a + new float2(seg, segH), lit ? FCol(c, i == level - 1 ? beat : 0.9f) : new ImColor8(50, 52, 60, 170), 1.5f * _scale);
        }
        var label = FrenzyLabel(look, level);
        SizedText(dl, new float2(x0 + total / 2, cy - font - 3 * _scale), font * 0.9f, FCol(Vec3.Lerp(col, Vec3.One, 0.35f), 0.95f), label, 0.5f);
    }

    private static readonly string?[] _frenzyLabels = new string?[8 * 7];
    private static readonly string?[] _frenzyLabelNames = new string?[8];

    /// <summary>"SMG FRENZY 3" / "MAX SMG FRENZY", made once per class and level (no per-frame strings).</summary>
    private static string FrenzyLabel(FrenzyLook look, int level)
    {
        var ci = Math.Clamp((int)look.Class, 0, 7);
        level = Math.Clamp(level, 0, 6);
        if (!ReferenceEquals(_frenzyLabelNames[ci], look.Name))
        {
            _frenzyLabelNames[ci] = look.Name;
            for (var l = 0; l < 7; l++)
                _frenzyLabels[ci * 7 + l] = null;
        }
        return _frenzyLabels[ci * 7 + level] ??= level >= FrenzyMeter.MaxLevel ? $"MAX {look.Name} FRENZY" : $"{look.Name} FRENZY {level}";
    }

    /// <summary>Four gradient bands, <paramref name="alpha"/> at the screen edge to 0 at <paramref name="depth"/> pixels in.</summary>
    private static void EdgeBands(ImDrawListPtr dl, float2 screen, float depth, Vec3 col, float alpha)
    {
        if (alpha <= 0.003f)
            return;
        var o = FCol(col, alpha);
        var z = FCol(col, 0f);
        dl.AddRectFilledMultiColor(new float2(0, 0), new float2(screen.X, depth), o, o, z, z);
        dl.AddRectFilledMultiColor(new float2(0, screen.Y - depth), new float2(screen.X, screen.Y), z, z, o, o);
        dl.AddRectFilledMultiColor(new float2(0, 0), new float2(depth, screen.Y), o, z, z, o);
        dl.AddRectFilledMultiColor(new float2(screen.X - depth, 0), new float2(screen.X, screen.Y), z, o, o, z);
    }

    /// <summary>SMG: streaks sliding in from the edges towards the centre (stopping well short of it) while you move fast.</summary>
    private static void SpeedLines(ImDrawListPtr dl, float2 screen, double now, Vec3 col, float k, Vec3 velocity)
    {
        var speed = MathF.Sqrt(velocity.X * velocity.X + velocity.Z * velocity.Z);
        var s = Math.Clamp((speed - 3.5f) / 4f, 0f, 1f) * k;
        if (s <= 0.01f)
            return;
        var c = new float2(screen.X / 2, screen.Y / 2);
        var reach = MathF.Sqrt(c.X * c.X + c.Y * c.Y);
        var lines = (int)(8 + 16 * s);
        for (var i = 0; i < lines; i++)
        {
            var h = Hash01(i * 7 + 3);
            var ang = Hash01(i * 13 + 1) * MathF.Tau;
            var ph = (float)((now * (1.2 + h) + h * 5) % 1.0);
            // From just past the corner in to 72% of the way out from the centre: never near the crosshair.
            var r0 = reach * (1.05f - 0.33f * ph);
            var r1 = r0 + reach * 0.09f;
            var dir = new float2(MathF.Cos(ang), MathF.Sin(ang));
            var a = (1f - ph) * 0.35f * s;
            dl.AddLine(new float2(c.X + dir.X * r0, c.Y + dir.Y * r0), new float2(c.X + dir.X * r1, c.Y + dir.Y * r1), FCol(Vec3.Lerp(col, Vec3.One, 0.4f), a), 1.5f + 1.5f * h);
        }
    }

    /// <summary>Shotgun: a few embers drifting up from the bottom edge, fading out low on the screen.</summary>
    private static void ScreenEmbers(ImDrawListPtr dl, float2 screen, double now, FrenzyLook look, float k)
    {
        var n = (int)(6 + 14 * k);
        var band = screen.Y * FrenzyLooks.EdgeDepth;
        for (var i = 0; i < n; i++)
        {
            var h = Hash01(i * 31 + 7);
            var ph = (float)((now * (0.25 + 0.3 * h) + h * 3) % 1.0);
            var x = Hash01(i * 17 + 5) * screen.X + MathF.Sin((float)(now * 1.3 % Math.Tau) + i) * 8f;
            var y = screen.Y - ph * band;
            var a = MathF.Sin(ph * MathF.PI) * 0.55f * k;
            dl.AddCircleFilled(new float2(x, y), 1.5f + 2f * h, FCol(Vec3.Lerp(look.Color, look.Accent, h), a), 6);
        }
    }

    private static float Hash01(int i)
    {
        var x = (uint)i * 2654435761u;
        x ^= x >> 15;
        x *= 2246822519u;
        x ^= x >> 13;
        return (x & 0xffff) / 65536f;
    }

    /// <summary>A small glyph per class, in its frenzy colour: the meter's icon.</summary>
    private static void DrawFrenzyIcon(ImDrawListPtr dl, WeaponClass cls, float2 c, float r, Vec3 col)
    {
        var fg = FCol(col, 1f);
        dl.AddCircleFilled(c, r * 1.25f, new ImColor8(20, 22, 28, 200), 16);
        var t = MathF.Max(1.5f, r * 0.22f);
        switch (cls)
        {
            case WeaponClass.Pistol: // a little pistol: slide and grip
                dl.AddRectFilled(c + new float2(-r * 0.7f, -r * 0.45f), c + new float2(r * 0.7f, -r * 0.05f), fg);
                dl.AddRectFilled(c + new float2(-r * 0.7f, -r * 0.05f), c + new float2(-r * 0.25f, r * 0.65f), fg);
                break;
            case WeaponClass.SMG: // a lightning bolt
                dl.AddLine(c + new float2(r * 0.3f, -r * 0.8f), c + new float2(-r * 0.25f, r * 0.05f), fg, t);
                dl.AddLine(c + new float2(-r * 0.25f, r * 0.05f), c + new float2(r * 0.25f, r * 0.05f), fg, t);
                dl.AddLine(c + new float2(r * 0.25f, r * 0.05f), c + new float2(-r * 0.3f, r * 0.8f), fg, t);
                break;
            case WeaponClass.AssaultRifle: // a flame
                dl.AddTriangleFilled(c + new float2(0, -r * 0.85f), c + new float2(r * 0.55f, r * 0.6f), c + new float2(-r * 0.55f, r * 0.6f), fg);
                break;
            case WeaponClass.DMR: // a ring and a dot
                dl.AddCircle(c, r * 0.7f, fg, 16, t);
                dl.AddCircleFilled(c, r * 0.18f, fg, 8);
                break;
            case WeaponClass.Sniper: // a scope: circle and cross
                dl.AddCircle(c, r * 0.75f, fg, 16, t * 0.8f);
                dl.AddLine(c + new float2(-r * 0.75f, 0), c + new float2(r * 0.75f, 0), fg, t * 0.6f);
                dl.AddLine(c + new float2(0, -r * 0.75f), c + new float2(0, r * 0.75f), fg, t * 0.6f);
                break;
            case WeaponClass.Shotgun: // three pellets
                dl.AddCircleFilled(c + new float2(0, -r * 0.4f), r * 0.24f, fg, 8);
                dl.AddCircleFilled(c + new float2(-r * 0.4f, r * 0.3f), r * 0.24f, fg, 8);
                dl.AddCircleFilled(c + new float2(r * 0.4f, r * 0.3f), r * 0.24f, fg, 8);
                break;
            case WeaponClass.LMG: // an ammo belt
                for (var i = 0; i < 4; i++)
                {
                    var x = c.X - r * 0.75f + i * r * 0.42f;
                    dl.AddRectFilled(new float2(x, c.Y - r * 0.5f), new float2(x + r * 0.26f, c.Y + r * 0.5f), fg);
                }
                break;
            default: // Special: a diamond, half in the accent hue
                dl.AddTriangleFilled(c + new float2(0, -r * 0.85f), c + new float2(r * 0.6f, 0), c + new float2(-r * 0.6f, 0), fg);
                dl.AddTriangleFilled(c + new float2(-r * 0.6f, 0), c + new float2(r * 0.6f, 0), c + new float2(0, r * 0.85f), FCol(new Vec3(0.2f, 1f, 0.9f), 1f));
                break;
        }
    }
}
