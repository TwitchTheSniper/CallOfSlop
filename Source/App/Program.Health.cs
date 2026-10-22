using System.Numerics;

namespace CallOfSlop;

/// <summary>Health feedback drawn in GL: the regen glow on the health bar, streak armour, Last Stand shield pips and the shield shimmer.</summary>
public static unsafe partial class Program
{
    private static readonly Vector3 ShieldColor = new(0.35f, 0.85f, 1f);

    /// <summary>The health bar's fill: red to green by health, pulsing brighter while it regenerates.</summary>
    private static Vector3 HealthBarColor(Player local, float hp, double now)
    {
        var c = Vector3.Lerp(new Vector3(1, 0.15f, 0.15f), new Vector3(0.25f, 0.95f, 0.35f), hp);
        if (!local.Regenerating)
            return c;
        var pulse = 0.5f + 0.5f * MathF.Sin((float)now * 5f);
        return Vector3.Lerp(c, new Vector3(0.8f, 1f, 0.85f), 0.45f * pulse);
    }

    /// <summary>
    /// Next to the health bar: a soft glow around it while health regenerates, and three pips for Last Stand's shield
    /// (lit = charges left) while it's up. The pips flash white when one is used.
    /// </summary>
    private static void DrawHealthExtras(Player local, float left, float m, float bw, float bh, float k, double now)
    {
        if (!local.Alive)
            return;
        if (local.Regenerating)
        {
            var pulse = 0.5f + 0.5f * MathF.Sin((float)now * 5f);
            var g = 3 * k;
            Gfx.Rect(left - g, m - g, bw + 2 * g, bh + 2 * g, new Vector3(0.4f, 1f, 0.5f), 0.06f + 0.1f * pulse);
        }
        if (local.Streak.Armor > 0) // Ballistic Vests / the Juggernaut suit: a thin bar on top of the health bar
        {
            var heavy = local.Streak.ArmorHeavy;
            var max = heavy ? KillStreaks.JuggernautArmor : KillStreaks.VestArmor;
            var f = Math.Clamp(local.Streak.Armor / (float)max, 0f, 1f);
            var ah = 7 * k;
            Gfx.Rect(left, m + bh + 3 * k, bw, ah, new Vector3(0, 0, 0), 0.5f);
            Gfx.Rect(left + 2 * k, m + bh + 5 * k, (bw - 4 * k) * f, ah - 4 * k, KillStreaks.Get(heavy ? StreakId.Juggernaut : StreakId.BallisticVests).Color, 0.95f);
        }
        if (local.ShieldCharges == 0)
            return;
        var flash = Math.Clamp((float)(1 - (now - local.ShieldFlashTime) / 0.35), 0f, 1f);
        float size = bh, gap = 6 * k, x = left + bw + 10 * k;
        for (var i = 0; i < Perks.LastStandCharges; i++)
        {
            var px = x + i * (size + gap);
            Gfx.Rect(px, m, size, size, new Vector3(0, 0, 0), 0.5f);
            var lit = i < local.ShieldCharges;
            var c = lit ? Vector3.Lerp(ShieldColor, Vector3.One, flash * 0.7f) : new Vector3(0.25f, 0.3f, 0.35f);
            var inset = 3 * k;
            Gfx.Rect(px + inset, m + inset, size - 2 * inset, size - 2 * inset, c, lit ? 0.95f : 0.6f);
        }
    }

    /// <summary>
    /// Other players with a Last Stand shield up: a faint flickering shell around them, flaring bright for a moment
    /// each time it blocks a hit. Drawn with blending on and depth writes off.
    /// </summary>
    private static void DrawShieldShimmer(double now)
    {
        foreach (var p in _game.Players.Values)
        {
            if (!p.Alive || p.Id == _game.LocalId) // your own shield shows as the HUD pips
                continue;
            var flash = Math.Clamp((float)(1 - (now - p.ShieldFlashTime) / 0.35), 0f, 1f);
            if (p.ShieldCharges == 0 && flash <= 0)
                continue;
            var hb = World.Hitbox(p.RenderPosition, p.RenderStance, p.RenderYaw);
            var flicker = 0.5f + 0.5f * MathF.Sin((float)now * 9f + p.Id);
            var alpha = (p.ShieldCharges > 0 ? 0.07f + 0.05f * flicker : 0f) + 0.45f * flash;
            var grow = 1.15f + 0.12f * flash;
            Gfx.Draw(Matrix4x4.CreateScale(hb.Half * 2 * grow) * Matrix4x4.CreateTranslation(hb.Center),
                Vector3.Lerp(ShieldColor, Vector3.One, 0.5f * flash), alpha, mode: 2);
        }
    }
}
