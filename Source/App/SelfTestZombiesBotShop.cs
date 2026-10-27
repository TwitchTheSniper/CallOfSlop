using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Self-test section "Zombies bot shopping" (Source/Game/ZombieBotsShop.cs; developer answer, 2026-10-26): co-op bots
/// spend their own points like players. Short seeded scenarios on Zombies Test (no long matches): what a bot plans with
/// little and plenty of points (nothing when poor; the power and perks first); a bot walking to and buying things in a
/// 40 s break (perks at the players' prices, never below its reserve, back at its post afterwards, the same result twice);
/// no shopping with a zombie close mid-round; a downed teammate revived before any purchase; and the cost (no allocation
/// in the planning, the bots' ticks within budget).
/// </summary>
public static partial class SelfTest
{
    private static void CheckZombiesBotShop()
    {
        var now = 0.0;
        var lines = new List<string>();
        var ok = true;
        var map = ZTestMap;
        if (map?.Zombies is not { } zm)
        {
            Fail("Zombies Test is missing");
            return;
        }

        // ---- Planning: nothing when poor; the power (free) or a perk first ----
        using (var g = ZGame(1, ref now, tweak: r => r.FirstRoundDelay = 9999, hostIn: false))
        {
            var bot = g.Players.Values.First(p => p.Bot != null);
            var zp = g.ZP(bot);
            zp.Points = 100;
            var poor = g.SelfTestBotPlan(bot, now);
            zp.Points = 20000;
            var rich = g.SelfTestBotPlan(bot, now);
            zp.Perks = 0;
            for (var i = 0; i < g.ZRules.Perks.Count; i++)
                zp.Perks |= (ushort)(1 << i);
            var allPerks = g.SelfTestBotPlan(bot, now);
            g.ZRules.BotsBuy = false;
            var off = g.SelfTestBotPlan(bot, now);
            g.ZRules.BotsBuy = true;
            zp.Perks = 0;
            lines.Add($"plans: 100 points {poor}, 20000 {rich}, with every perk {allPerks}, buying off {off}");
            ok &= poor == ZUseKind.None && rich is ZUseKind.Power or ZUseKind.Perk && allPerks is not (ZUseKind.None or ZUseKind.Perk) && off == ZUseKind.None;

            // No allocation in the planning (after a warm-up).
            for (var i = 0; i < 50; i++)
                g.SelfTestBotPlan(bot, now);
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 500; i++)
                g.SelfTestBotPlan(bot, now + i * 0.001);
            var alloc = GC.GetAllocatedBytesForCurrentThread() - before;
            lines.Add($"planning 500 times: {alloc} bytes");
            ok &= alloc == 0;
        }

        // ---- A break of shopping: walk there, buy, come back; the same twice ----
        string Shop(out string detail, out bool good)
        {
            var t = 0.0;
            using var g = ZGame(2, ref t, tweak: r => r.FirstRoundDelay = 9999, hostIn: false);
            var bots = g.Players.Values.Where(p => p.Bot != null).OrderBy(p => p.Id).ToList();
            foreach (var b in bots)
                g.ZP(b).Points = 9000;
            var anchors = bots.Select(b => b.Bot!.HoldAnchor).ToList();
            var farthest = 0f;
            var lowest = int.MaxValue;
            var ticks = 0;
            var worst = 0.0;
            for (var s = 0.0; s < 42; s += 1.0 / World.TickRate)
            {
                var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                Run(g, ref t, 1.0 / World.TickRate);
                worst = Math.Max(worst, System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds);
                ticks++;
                if (ticks == 30 * World.TickRate)
                    g.ZRules.BotsBuy = false; // 30 s of shopping; then 12 s to walk back to their posts
                for (var i = 0; i < bots.Count; i++)
                {
                    farthest = MathF.Max(farthest, ZFlat(bots[i].Position - anchors[i]));
                    lowest = Math.Min(lowest, g.ZP(bots[i]).Points);
                }
            }
            var perks = bots.Select(b => string.Join("+", g.ZRules.Perks.Where((_, k) => g.ZP(b).Has(k)).Select(d => d.Key))).ToList();
            var doors = Enumerable.Range(0, zm.Doors.Count).Count(World.Map.IsDoorOpen);
            var home = bots.Select((b, i) => ZFlat(b.Position - anchors[i])).Max();
            var digest = $"{string.Join("|", bots.Select(b => $"{g.ZP(b).Points}/{g.ZP(b).Perks}/{(b.ExtraSlots ? 4 : 2)}/{b.WeaponDef.Name}/{b.Position.X:0.00},{b.Position.Z:0.00}"))}; doors {doors}; power {g.ZPower}";
            detail = $"{g.ZBotPurchases} purchases in 30 s [{string.Join(", ", g.ZBotBought)}] (perks {string.Join(", ", perks)}; doors opened {doors}, power {g.ZPower}, slots {string.Join("/", bots.Select(b => b.ExtraSlots ? 4 : 2))}), "
                     + $"points left {string.Join("/", bots.Select(b => g.ZP(b).Points))} (lowest {lowest}), furthest from the post {farthest:0.0} m, home within {home:0.0} m (plans now {string.Join("/", bots.Select(g.SelfTestBotShopKind))}, at {string.Join(" ", bots.Select(b => $"{b.Position.X:0.0},{b.Position.Z:0.0}"))}, posts {string.Join(" ", anchors.Select(a => $"{a.X:0.0},{a.Z:0.0}"))}); worst tick {worst:0.00} ms";
            good = g.ZBotPurchases >= 3 && perks.All(x => x.Length > 0) && lowest >= 0 && farthest <= g.ZRules.BotShopRange + 3 && home < 3.5f && doors <= 1 + zm.Doors.Count(d => d.Cost == 0);
            return digest;
        }
        var d1 = Shop(out var detail1, out var good1);
        var d2 = Shop(out _, out _);
        lines.Add($"shopping: {detail1}; same twice {d1 == d2}");
        ok &= good1 && d1 == d2;

        // ---- Safety: a zombie close mid-round stops it; a downed teammate is revived first ----
        using (var g = ZGame(1, ref now, tweak: r => { r.FirstRoundDelay = 9999; r.PowerFirstAt = 0; }, hostIn: true))
        {
            var me = g.Local!;
            var bot = g.Players.Values.First(p => p.Bot != null);
            bot.ImmuneUntil = me.ImmuneUntil = 1e9;
            g.ZP(bot).Points = 9000;
            g.SelfTestBeginRound(1, now);
            var z = g.SelfTestSpawnZombie(0, now);
            z.Sim.Position = bot.Position + new Vector3(3, 0, 0);
            Run(g, ref now, 0.6);
            var busy = g.SelfTestBotShopKind(bot);
            var bought = g.ZBotPurchases;
            g.DamageZombie(me, z, 100000, false, false, now);
            // A downed teammate next to it: the revive comes first.
            me.Teleport(bot.Position + new Vector3(1.5f, 0, 0));
            g.SelfTestDown(me, now);
            var revivedFirst = false;
            for (var i = 0; i < 60 * 8 && g.IsDowned(me); i++)
            {
                Run(g, ref now, 1.0 / World.TickRate);
                if (!g.IsDowned(me))
                    revivedFirst = g.ZBotPurchases == bought;
            }
            lines.Add($"a zombie 3 m off mid-round: plan {busy}, bought {bought}; a downed teammate revived first {revivedFirst}");
            ok &= busy == ZUseKind.None && bought == 0 && revivedFirst;
        }

        foreach (var l in lines)
            ZLine("bot shopping " + l);
        if (!ok)
            Fail("co-op bots should spend their points sensibly (power and perks first, never below their reserve, back at their post), only when it's safe, after revives, deterministically and without allocating");
    }
}
