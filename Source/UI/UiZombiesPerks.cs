using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// The Zombies perks on the HUD (2026-10-27): a row of round badges, one per perk held, each with its own glyph (the
/// rules' "icon": <see cref="ZombiePerkIcons"/>) in the perk's colour; the armour plate's badge goes dark while the plate
/// grows back. Over every perk machine within 9 m, its name in its colour ("NEEDS POWER" under it while the power is off).
/// </summary>
public sealed partial class Ui
{
    /// <summary>The badges, bottom-left beside the round.</summary>
    private void DrawZPerkBadges(Game game, ZPlayer zp, float2 pos, float2 size, float pad)
    {
        if (zp.Perks == 0)
            return;
        var r = 15f * _scale;
        var gap = 5f * _scale;
        var n = Game.PerkCount(zp);
        ImGui.SetNextWindowPos(new float2(pos.X + pad + 170 * _scale, pos.Y + size.Y - pad - 200 * _scale), ImGuiCond.Always, new float2(0, 1));
        if (ImGui.Begin("##zperks"u8, Overlay))
        {
            var at = ImGui.GetCursorScreenPos();
            ImGui.Dummy(new float2(n * (2 * r + gap), 2 * r + 2));
            var dl = ImGui.GetWindowDrawList();
            var x = 0;
            for (var i = 0; i < game.ZRules.Perks.Count; i++)
            {
                if (!zp.Has(i))
                    continue;
                var d = game.ZRules.Perks[i];
                var dim = d.PlateSeconds > 0 && !zp.PlateReady;
                DrawZPerkIcon(dl, d, new float2(at.X + r + x * (2 * r + gap), at.Y + r + 1), r, dim);
                x++;
            }
        }
        ImGui.End();
    }

    /// <summary>One perk badge: a dark disc with a ring and the perk's glyph in its colour (grey and faint when <paramref name="dim"/>).</summary>
    private void DrawZPerkIcon(ImDrawListPtr dl, ZombiePerkDef d, float2 c, float r, bool dim = false)
    {
        var col = dim ? new ImColor8(90, 90, 96, 255) : Col(d.Color);
        var dark = new ImColor8(18, 20, 26, 230);
        ImDrawListExtensions.AddCircleFilled(dl, in c, r, dark, 24);
        ImDrawListExtensions.AddCircle(dl, in c, r, col, 24, 2f * _scale);
        var t = 2f * _scale * MathF.Max(r / (16 * _scale), 0.7f);
        var u = r * 0.5f;
        float2 P(float x, float y) => c + new float2(x * u, y * u);
        switch (ZombiePerkIcons.Index(d.Icon))
        {
            case 0: // heart
                ImDrawListExtensions.AddCircleFilled(dl, P(-0.42f, -0.3f), u * 0.5f, col, 12);
                ImDrawListExtensions.AddCircleFilled(dl, P(0.42f, -0.3f), u * 0.5f, col, 12);
                Tri(dl, P(-0.9f, -0.12f), P(0.9f, -0.12f), P(0, 1.0f), col);
                break;
            case 1: // drum: a revolver cylinder
                ImDrawListExtensions.AddCircle(dl, in c, u * 1.05f, col, 18, t);
                for (var k = 0; k < 6; k++)
                {
                    var a = k * MathF.PI / 3;
                    ImDrawListExtensions.AddCircleFilled(dl, P(0.6f * MathF.Sin(a), -0.6f * MathF.Cos(a)), u * 0.2f, col, 8);
                }
                break;
            case 2: // rounds: two cartridges
                for (var s = -1; s <= 1; s += 2)
                {
                    var a = P(s * 0.45f - 0.22f, -0.35f);
                    var b = P(s * 0.45f + 0.22f, 0.95f);
                    ImDrawListExtensions.AddRectFilled(dl, in a, in b, col, 1 * _scale, ImDrawFlags.None);
                    Tri(dl, P(s * 0.45f - 0.22f, -0.42f), P(s * 0.45f + 0.22f, -0.42f), P(s * 0.45f, -1.05f), col);
                }
                break;
            case 3: // cross
            {
                var a = P(-0.28f, -0.95f);
                var b = P(0.28f, 0.95f);
                ImDrawListExtensions.AddRectFilled(dl, in a, in b, col, 1 * _scale, ImDrawFlags.None);
                a = P(-0.95f, -0.28f);
                b = P(0.95f, 0.28f);
                ImDrawListExtensions.AddRectFilled(dl, in a, in b, col, 1 * _scale, ImDrawFlags.None);
                break;
            }
            case 4: // wind: three gusts
                Line(dl, P(-1f, -0.55f), P(0.5f, -0.55f), col, t);
                Line(dl, P(-0.7f, 0.05f), P(0.9f, 0.05f), col, t);
                Line(dl, P(-1f, 0.65f), P(0.3f, 0.65f), col, t);
                ImDrawListExtensions.AddCircle(dl, P(0.55f, -0.8f), u * 0.25f, col, 10, t * 0.8f);
                break;
            case 5: // chevrons: two arrows forward
                for (var k = 0; k < 2; k++)
                {
                    var x = -0.75f + k * 0.75f;
                    Line(dl, P(x, -0.8f), P(x + 0.6f, 0), col, t * 1.2f);
                    Line(dl, P(x + 0.6f, 0), P(x, 0.8f), col, t * 1.2f);
                }
                break;
            case 6: // target: rings and a dot
                ImDrawListExtensions.AddCircle(dl, in c, u * 1.05f, col, 18, t * 0.8f);
                ImDrawListExtensions.AddCircle(dl, in c, u * 0.6f, col, 14, t * 0.8f);
                ImDrawListExtensions.AddCircleFilled(dl, in c, u * 0.22f, col, 8);
                break;
            case 7: // fang: a tooth with a drop
                Tri(dl, P(-0.75f, -0.9f), P(0.15f, -0.9f), P(-0.3f, 0.7f), col);
                ImDrawListExtensions.AddCircleFilled(dl, P(0.5f, 0.45f), u * 0.3f, col, 10);
                Tri(dl, P(0.25f, 0.35f), P(0.75f, 0.35f), P(0.5f, -0.25f), col);
                break;
            case 8: // star
            {
                Span<float2> star = stackalloc float2[10];
                for (var k = 0; k < 10; k++)
                {
                    var a = k * MathF.PI / 5;
                    var rr = k % 2 == 0 ? 1.05f : 0.45f;
                    star[k] = P(rr * MathF.Sin(a), -rr * MathF.Cos(a));
                }
                ImDrawListExtensions.AddConcavePolyFilled(dl, star, col);
                break;
            }
            case 9: // hammer
            {
                var a = P(-0.85f, -0.95f);
                var b = P(0.55f, -0.4f);
                ImDrawListExtensions.AddRectFilled(dl, in a, in b, col, 1 * _scale, ImDrawFlags.None);
                a = P(-0.18f, -0.4f);
                b = P(0.18f, 1.0f);
                ImDrawListExtensions.AddRectFilled(dl, in a, in b, col, 1 * _scale, ImDrawFlags.None);
                break;
            }
            case 10: // gear
                ImDrawListExtensions.AddCircle(dl, in c, u * 0.6f, col, 14, t * 1.4f);
                for (var k = 0; k < 8; k++)
                {
                    var a = k * MathF.PI / 4;
                    Line(dl, P(0.62f * MathF.Sin(a), -0.62f * MathF.Cos(a)), P(1.0f * MathF.Sin(a), -1.0f * MathF.Cos(a)), col, t * 1.3f);
                }
                break;
            case 11: // shield
            {
                Span<float2> shield = [P(-0.85f, -0.9f), P(0.85f, -0.9f), P(0.8f, 0.1f), P(0, 1.05f), P(-0.8f, 0.1f)];
                ImDrawListExtensions.AddConcavePolyFilled(dl, shield, col);
                Line(dl, P(0, -0.8f), P(0, 0.85f), new ImColor8(18, 20, 26, 255), t * 0.8f);
                break;
            }
            default: // the perk's initial
                if (d.Label.Length > 0)
                    SizedText(dl, P(0, -0.85f), r * 1.1f, col, d.Label[..1], 0.5f);
                break;
        }
    }

    /// <summary>Over every perk machine within 9 m: its name, in its colour (dim and "NEEDS POWER" while it waits for the power).</summary>
    private void DrawPerkLabels(Game game, Player me)
    {
        if (World.Map.Zombies is not { } zm)
            return;
        for (var i = 0; i < zm.PerkMachines.Count; i++)
        {
            var m = zm.PerkMachines[i];
            var k = game.ZRules.PerkIndex(m.Perk);
            if (k < 0)
                continue;
            var dx = m.At.X - me.Position.X;
            var dz = m.At.Z - me.Position.Z;
            if (dx * dx + dz * dz > 9 * 9)
                continue;
            var d = game.ZRules.Perks[k];
            var lit = !m.NeedsPower || game.ZPower;
            var top = m.At + new System.Numerics.Vector3(0, Program.PerkTopY(zm, i) + 0.35f, 0);
            Hud.WorldLabel(top, d.Label, lit ? d.Color : d.Color * 0.55f, lit ? 0.95f : 0.7f, 13f * _scale);
            if (!lit)
                Hud.WorldLabel(top - new System.Numerics.Vector3(0, 0.22f, 0), "NEEDS POWER", new System.Numerics.Vector3(0.75f), 0.7f, 11f * _scale);
        }
    }
}
