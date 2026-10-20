using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Bots using their earned kill streaks: a short "thinking" delay after earning one, then each streak where it makes
/// sense (mortars and strafe runs on the last enemy they saw or the biggest group of enemies, turrets facing the busiest
/// direction). Called from one line in BotTick, so the bot AI can change around it.
/// </summary>
public sealed partial class Game
{
    private readonly Dictionary<byte, double> _botStreakAt = new();

    private void BotUseStreaks(Player p, double now)
    {
        var s = p.Streak;
        if (!StreaksEnabled || !p.Alive || s.Ready == 0 || Phase != MatchPhase.Playing || p.Bot is not { } b)
            return;
        if (!_botStreakAt.TryGetValue(p.Id, out var at))
        {
            // Earned one: use it in a couple of seconds, not instantly.
            _botStreakAt[p.Id] = now + 1.5 + _rng.NextDouble() * 2.5;
            return;
        }
        if (now < at)
            return;
        _botStreakAt.Remove(p.Id);

        foreach (var id in s.Slots.Ids.Reverse()) // the biggest one first
        {
            if (!s.IsReady(id))
                continue;
            switch (id)
            {
                case StreakId.MortarBarrage:
                case StreakId.StrafeRun:
                {
                    if (BotStrikeTarget(p, b, now) is not { } target)
                        continue; // nobody to aim at yet: keep it
                    var to = target - p.Position;
                    var yaw = to.LengthSquared() > 1 ? MathF.Atan2(to.X, -to.Z) : p.Yaw;
                    ServerUseStreak(p, id, target, yaw, now);
                    return;
                }
                case StreakId.SentryTurret:
                    ServerUseStreak(p, id, p.Position, BotBusyYaw(p), now);
                    return;
                default:
                    ServerUseStreak(p, id, p.Position, p.Yaw, now);
                    return;
            }
        }
    }

    /// <summary>Where to drop a strike: the last enemy this bot saw (if recent), otherwise the middle of the biggest group of enemies.</summary>
    private Vector3? BotStrikeTarget(Player p, BotBrain b, double now)
    {
        if (now - b.LastSeenTime < 4 && Vector3.Distance(b.LastSeenAt, p.Position) > 8)
            return b.LastSeenAt;
        Vector3? best = null;
        var bestCount = 0;
        foreach (var e in Players.Values)
        {
            if (!e.Alive || !StreakIsEnemy(p, e) || Vector3.Distance(e.Position, p.Position) < 10)
                continue; // never on its own head
            var group = Players.Values.Where(o => o.Alive && StreakIsEnemy(p, o) && Vector3.Distance(o.Position, e.Position) < 8).ToList();
            if (group.Count > bestCount)
            {
                bestCount = group.Count;
                best = group.Aggregate(Vector3.Zero, (sum, o) => sum + o.Position) / group.Count;
            }
        }
        return best;
    }

    /// <summary>A turret faces where most enemies are (or where the bot last saw one).</summary>
    private float BotBusyYaw(Player p)
    {
        var sum = Vector3.Zero;
        foreach (var e in Players.Values)
            if (e.Alive && StreakIsEnemy(p, e))
            {
                var to = e.Position - p.Position;
                var d = to.Length();
                if (d > 1)
                    sum += to / d / MathF.Max(d, 5f); // nearer enemies count for more
            }
        if (p.Bot is { } b && b.LastSeenTime > -50)
        {
            var to = b.LastSeenAt - p.Position;
            if (to.LengthSquared() > 1)
                sum += Vector3.Normalize(to) * 0.5f;
        }
        return sum.LengthSquared() > 1e-6f ? MathF.Atan2(sum.X, -sum.Z) : p.Yaw;
    }
}
