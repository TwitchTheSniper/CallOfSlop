using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Drawing the Zombies slot machines (Source/Game/ZombiesSlots.cs; docs/zombies/ZOMBIES-MODE.md, "Slot machines"): on
/// the Reel Cabinet prop (or a plain cabinet without machine props) the three reels, which spin and stop one by one on
/// the symbols the server rolled (a click as each one lands), the heat meter under them (it fills towards the next
/// rarity step, then flashes red at the top), and the topper's chasing lights (faster as the machine warms up; gold and
/// pink in a jackpot round; dark without the power). A part won floats over the machine in its rarity colour for a
/// moment. Client-side only and allocation-free.
/// </summary>
public static unsafe partial class Program
{
    /// <summary>When reel <paramref name="reel"/> (0-2) stops, as a fraction of the spin.</summary>
    public static float SlotReelStop(int reel) => 0.55f + 0.225f * reel;

    private static readonly Vector3 SlotGold = new(1f, 0.78f, 0.2f), SlotPink = new(1f, 0.35f, 0.7f);

    private static void DrawSlotMachines(ZombiesMap zm, bool live, double now)
    {
        var t = (float)now;
        for (var i = 0; i < zm.SlotMachines.Count && i < _game.ZSlots.Length; i++)
        {
            var spot = zm.SlotMachines[i];
            var m = _game.ZSlots[i];
            var rot = Matrix4x4.CreateRotationY(-spot.Yaw);
            var place = rot * Matrix4x4.CreateTranslation(spot.At);
            void Part(float sx, float sy, float sz, float x, float y, float z, Vector3 c, float alpha = 1f, int mode = 0) =>
                Gfx.Draw(Matrix4x4.CreateScale(sx, sy, sz) * Matrix4x4.CreateTranslation(x, y, z) * place, c, alpha, mode);
            if (!zm.MachineProps)
            {
                // No props (Zombies Test): a plain cabinet with its topper.
                Part(0.8f, 1.6f, 0.6f, 0, 0.8f, 0, new Vector3(0.42f, 0.1f, 0.12f));
                Part(0.86f, 0.28f, 0.52f, 0, 1.8f, 0.02f, new Vector3(0.78f, 0.58f, 0.12f));
                Part(0.62f, 0.3f, 0.02f, 0, 1.3f, -0.305f, new Vector3(0.02f));
            }
            var lit = !spot.NeedsPower || _game.ZPower;
            if (!live)
                continue;

            // A new spin: the reels start again (the clock is worked back from the seconds left, so a joiner mid-spin lines up).
            var spin = _game.ZRules.SlotSpinSeconds;
            if (m.SpinId != m.SeenSpin)
            {
                m.SeenSpin = m.SpinId;
                if (m.State == ZSlotState.Spinning)
                {
                    m.SpinStart = now - Math.Clamp(spin - m.Left, 0, spin);
                    m.ReelsStopped = 0;
                }
                else
                    m.ReelsStopped = 3;
            }
            var k = (float)((now - m.SpinStart) / Math.Max(0.1f, spin));
            var front = -0.29f;

            // The reels.
            for (var r = 0; r < 3; r++)
            {
                var x = (r - 1) * 0.2f;
                var stopped = m.State != ZSlotState.Spinning || k >= SlotReelStop(r);
                if (stopped && r >= m.ReelsStopped && m.State == ZSlotState.Spinning)
                {
                    m.ReelsStopped = r + 1;
                    AudioSystem.Effect3D(Sfx.HitTick, Vector3.Transform(new Vector3(x, 1.3f, front), place), 0.7f, MixBus.Zombies); // a reel lands
                }
                var face = lit ? new Vector3(0.92f, 0.9f, 0.82f) : new Vector3(0.35f);
                Part(0.17f, 0.26f, 0.01f, x, 1.3f, front, face, 1f, lit ? 2 : 0);
                if (!stopped)
                {
                    // Spinning: symbols roll past (a blur strip and a symbol stepping down the reel).
                    var roll = k * 18f + r * 0.37f;
                    var sym = (byte)((int)roll % 8);
                    var dy = (roll - MathF.Floor(roll) - 0.5f) * 0.22f;
                    DrawSlotSymbol(place, x, 1.3f - dy, front - 0.012f, sym, lit, 0.7f);
                    Part(0.16f, 0.03f, 0.005f, x, 1.3f + dy * 0.5f, front - 0.02f, new Vector3(1f), 0.35f, 2);
                }
                else if (m.State != ZSlotState.Idle || m.SpinId != 0)
                    DrawSlotSymbol(place, x, 1.3f, front - 0.012f, m.Symbols[r], lit, 1f);
            }
            var winning = m.State == ZSlotState.Showing && m.Result != ZSlotResult.Nothing;
            if (winning && ((int)(t * 6) & 1) == 0)
                Part(0.64f, 0.32f, 0.005f, 0, 1.3f, front - 0.025f, m.Result is ZSlotResult.Part or ZSlotResult.Melee ? PartDef.RarityColor(m.Tier) : SlotGold, 0.35f, 2);

            // The heat meter: segments towards the next rarity step; flashing red at the top step.
            var step = Math.Max(1, _game.ZRules.SlotHeatStep);
            var steps = Math.Min(_game.ZRules.SlotHeatMaxSteps, m.Heat / step);
            var maxed = steps >= _game.ZRules.SlotHeatMaxSteps;
            var fill = maxed ? step : m.Heat % step;
            var heatCol = steps switch { 0 => new Vector3(1f, 0.85f, 0.2f), 1 => new Vector3(1f, 0.55f, 0.1f), 2 => new Vector3(1f, 0.3f, 0.08f), _ => new Vector3(1f, 0.1f, 0.05f) };
            var segW = 0.46f / step;
            for (var s = 0; s < step; s++)
            {
                var on = lit && s < fill && (!maxed || ((int)(t * 4) & 1) == 0);
                Part(segW * 0.8f, 0.06f, 0.006f, -0.23f + segW * (s + 0.5f), 0.76f, -0.31f, on ? heatCol : new Vector3(0.12f, 0.08f, 0.06f), 1f, on ? 2 : 0);
            }

            // The bet: a lamp per level under the heat meter, lit up to the machine's bet (green, gold, then red for the top bet).
            var bets = Math.Clamp(_game.ZRules.SlotBets.Length, 1, 5);
            if (bets > 1)
                for (var bl = 0; bl < bets; bl++)
                {
                    var on = lit && bl <= m.Bet;
                    var c = bl == 0 ? new Vector3(0.3f, 1f, 0.35f) : bl == bets - 1 ? new Vector3(1f, 0.25f, 0.15f) : SlotGold;
                    Part(0.07f, 0.05f, 0.006f, -0.2f + 0.4f * bl / (bets - 1), 0.66f, -0.31f, on ? c : c * 0.2f, 1f, on ? 2 : 0);
                }

            // The topper's chasing lights: faster as it warms up; gold and pink in a jackpot round.
            var jackpot = _game.ZRules.SlotJackpot(_game.ZRound);
            var speed = 2f + 2.5f * steps + (jackpot ? 4f : 0f) + (m.State == ZSlotState.Spinning ? 6f : 0f);
            for (var b = 0; b < 7; b++)
            {
                var on = lit && ((int)(t * speed) + b) % 3 == 0;
                var c = jackpot ? (b % 2 == 0 ? SlotGold : SlotPink) : steps > 0 ? heatCol : SlotGold;
                Part(0.06f, 0.06f, 0.01f, -0.3f + b * 0.1f, 1.8f, -0.258f, on ? c : c * 0.25f, 1f, on ? 2 : 0);
            }

            // A part won floats over the machine for a moment.
            if (m.State == ZSlotState.Showing && m.Result == ZSlotResult.Part && !m.AutoScrapped)
            {
                var up = 2.1f + 0.3f * Math.Clamp(1f - m.Left / Math.Max(0.1f, _game.ZRules.SlotShowSeconds), 0, 1);
                Gfx.Draw(Matrix4x4.CreateScale(0.22f) * Matrix4x4.CreateRotationY(t * 3) * Matrix4x4.CreateTranslation(0, up, -0.1f) * place, PartDef.RarityColor(m.Tier), 1f, mode: 2);
            }
            else if (m.State == ZSlotState.Showing && m.Result == ZSlotResult.Melee)
            {
                // A melee build won: a glowing blade in its rarity colour, turning over the machine.
                var up = 2.1f + 0.3f * Math.Clamp(1f - m.Left / Math.Max(0.1f, _game.ZRules.SlotShowSeconds), 0, 1);
                var wonAt = Matrix4x4.CreateRotationY(t * 3) * Matrix4x4.CreateTranslation(0, up, -0.1f) * place;
                Gfx.Draw(Matrix4x4.CreateScale(0.06f, 0.5f, 0.02f) * Matrix4x4.CreateTranslation(0, 0.12f, 0) * wonAt, PartDef.RarityColor(m.Tier), 1f, mode: 2);
                Gfx.Draw(Matrix4x4.CreateScale(0.2f, 0.04f, 0.04f) * Matrix4x4.CreateTranslation(0, -0.15f, 0) * wonAt, new Vector3(0.3f, 0.2f, 0.1f));
                Gfx.Draw(Matrix4x4.CreateScale(0.04f, 0.16f, 0.04f) * Matrix4x4.CreateTranslation(0, -0.25f, 0) * wonAt, new Vector3(0.3f, 0.2f, 0.1f));
            }
        }
    }

    /// <summary>One reel symbol on the reel face at (<paramref name="x"/>, <paramref name="y"/>, <paramref name="z"/>) in the machine's frame.</summary>
    private static void DrawSlotSymbol(Matrix4x4 place, float x, float y, float z, byte sym, bool lit, float alpha)
    {
        var mode = lit ? 2 : 0;
        var dim = lit ? 1f : 0.4f;
        void Box(float sx, float sy, float ox, float oy, Vector3 c, float roll = 0) =>
            Gfx.Draw(Matrix4x4.CreateScale(sx, sy, 0.006f) * Matrix4x4.CreateRotationZ(roll) * Matrix4x4.CreateTranslation(x + ox, y + oy, z) * place, c * dim, alpha, mode);
        switch (sym)
        {
            case Game.SlotSymSkull:
                Box(0.1f, 0.09f, 0, 0.015f, new Vector3(0.95f, 0.93f, 0.88f));
                Box(0.06f, 0.04f, 0, -0.045f, new Vector3(0.95f, 0.93f, 0.88f));
                Box(0.025f, 0.03f, -0.025f, 0.02f, new Vector3(0.05f));
                Box(0.025f, 0.03f, 0.025f, 0.02f, new Vector3(0.05f));
                break;
            case Game.SlotSymScrap:
                Box(0.1f, 0.03f, 0, 0, new Vector3(0.55f, 0.57f, 0.6f));
                Box(0.1f, 0.03f, 0, 0, new Vector3(0.55f, 0.57f, 0.6f), MathF.PI / 2);
                Box(0.1f, 0.03f, 0, 0, new Vector3(0.45f, 0.47f, 0.5f), MathF.PI / 4);
                Box(0.1f, 0.03f, 0, 0, new Vector3(0.45f, 0.47f, 0.5f), -MathF.PI / 4);
                Box(0.04f, 0.04f, 0, 0, new Vector3(0.2f));
                break;
            case Game.SlotSymBlade:
                // A sword: blade, guard and grip (two blades and a gem: a melee build of the gem's rarity).
                Box(0.025f, 0.11f, 0, 0.03f, new Vector3(0.85f, 0.88f, 0.92f), -MathF.PI / 4);
                Box(0.07f, 0.018f, -0.03f, -0.03f, new Vector3(0.75f, 0.6f, 0.2f), -MathF.PI / 4);
                Box(0.02f, 0.04f, -0.045f, -0.045f, new Vector3(0.4f, 0.25f, 0.12f), -MathF.PI / 4);
                break;
            case Game.SlotSymCoin:
                Box(0.09f, 0.09f, 0, 0, new Vector3(0.95f, 0.72f, 0.15f), MathF.PI / 4);
                Box(0.05f, 0.05f, 0, 0, new Vector3(1f, 0.85f, 0.35f), MathF.PI / 4);
                break;
            default:
            {
                // A gem in its rarity colour (a wonder gem gets a second, turning facet).
                var tier = (Rarity)Math.Min(sym - Game.SlotSymGem, (int)Rarity.Wonder);
                var c = PartDef.RarityColor(tier);
                Box(0.08f, 0.08f, 0, 0, c, MathF.PI / 4);
                Box(0.035f, 0.035f, -0.012f, 0.012f, Vector3.Lerp(c, Vector3.One, 0.6f), MathF.PI / 4);
                if (tier == Rarity.Wonder)
                    Box(0.1f, 0.02f, 0, 0, Vector3.One, (float)Brutal.GlfwApi.Glfw.Time * 2f);
                break;
            }
        }
    }
}
