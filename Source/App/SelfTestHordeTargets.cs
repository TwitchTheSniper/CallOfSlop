using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Self-test section "Horde targets" (board bug dev-horde-wrong-target-hz01, docs/zombies/ZOMBIES-MODE.md, "Horde
/// controller"): every so often the whole horde turned together towards a wrong spot. The cause was the blocked-group
/// check: a player kiting the horde round the map kept the group's mean distance from shrinking, so the group "regrouped"
/// every few seconds, and regrouping walked every member straight at the group's centre, which for a horde spread round
/// its target is somewhere else entirely (on Horde Test the dais in the middle: the origin). A short seeded scenario on
/// Horde Test: 40 zombies in the arena plus round 10's spawns from the windows, the host running a loop round the dais
/// (crossing grid cells, the target's nav node changing and its flow fields rebuilt), the flow fields all dropped once
/// mid-run. Every tick, the chasing zombies that move
/// away from the host (more than 4 m off) are counted: never a sizeable share of the horde at once, never one for long,
/// and never a run of them heading for the origin; the kited group never regroups; a regrouping member only closes up
/// from behind the group's centre.
/// </summary>
public static partial class SelfTest
{
    private static void CheckHordeTargets()
    {
        var now = 0.0;
        if (Maps.Find("Horde Test") is not { Zombies: { } zm } map)
        {
            Fail("no Horde Test map");
            return;
        }
        using var g = ZGame(0, ref now, tweak: r => r.FirstRoundDelay = 9999, onMap: map);
        var me = g.Local!;
        me.ImmuneUntil = 1e9;
        var arena = zm.AreaIndex("Arena");
        Vector3 PathAt(double t)
        {
            var a = (float)(t * 0.36); // about 6.5 m/s round an 18 m ring: faster than the horde, so it trails behind
            var p = new Vector3(MathF.Sin(a) * 18f, 0, MathF.Cos(a) * 18f);
            p.Y = map.GroundHeight(p.X, p.Z);
            return p;
        }
        me.Teleport(PathAt(0));
        HordePlace(g, map, zm, 40, now, 777u, _ => arena);
        g.SelfTestBeginRound(10, now);
        var ai = g.HordeAI;
        var prev = new Dictionary<ushort, Vector3>();
        var awayRun = new Dictionary<ushort, int>();
        var t0 = now;
        int worstAway = 0, worstAwayOf = 0, worstOrigin = 0, longestAway = 0, ticks = 0, badRegroup = 0, sumMoving = 0;
        var worstAt = 0;
        var built0 = ai.Flow.Built;
        var nodes = new HashSet<int>();
        const int Ticks = 60 * 26;
        var minStep = g.ZRules.WalkSpeed * World.TickDt * 0.6f;
        for (var k = 0; k < Ticks; k++)
        {
            me.Teleport(PathAt(now - t0));
            if (k == 300)
                ai.Flow.Clear(); // every field dropped at once: rebuilt one a tick, members steer straight meanwhile
            now += World.TickDt;
            g.SelfTestZombiesTick(now);
            ticks++;
            var slot = ai.SlotOf(me.Id);
            if (slot >= 0)
                nodes.Add(ai.Targets[slot].Node);
            int away = 0, origin = 0, moving = 0;
            foreach (var z in g.Horde)
            {
                var had = prev.TryGetValue(z.Id, out var p0);
                prev[z.Id] = z.Position;
                if (!had || z.State != ZState.Chase)
                {
                    awayRun[z.Id] = 0;
                    continue;
                }
                var d = z.Position - p0;
                d.Y = 0;
                var to = me.Position - z.Position;
                to.Y = 0;
                if (d.Length() < minStep || to.Length() < 4f || d.Length() > 1f) // (barely moving: grinding on a wall, not heading anywhere)
                {
                    awayRun[z.Id] = 0;
                    continue;
                }
                moving++;
                var c = Vector3.Dot(Vector3.Normalize(d), Vector3.Normalize(to));
                var run = c < -0.2f ? awayRun.GetValueOrDefault(z.Id) + 1 : 0;
                awayRun[z.Id] = run;
                longestAway = Math.Max(longestAway, run);
                if (c >= -0.2f)
                    continue;
                away++;
                var o = -z.Position;
                o.Y = 0;
                if (o.Length() > 3f && Vector3.Dot(Vector3.Normalize(d), Vector3.Normalize(o)) > 0.95f)
                    origin++;
            }
            sumMoving += moving;
            if (away > worstAway)
            {
                worstAway = away;
                worstAwayOf = moving;
                worstAt = k;
            }
            worstOrigin = Math.Max(worstOrigin, origin);
            // A regrouping member that closes up (doesn't hold) is behind the centre: it never walks away from the goal.
            foreach (var z in g.Horde)
                if (z.State == ZState.Chase && ai.Regrouping(z, out var hold, out var centre) && !hold)
                {
                    var goal = ai.GoalOf(z.Hm.Group);
                    if (Vector3.Dot(centre - z.Position, goal - z.Position) < 0)
                        badRegroup++;
                }
        }
        var gi = ai.TargetGroup(Math.Max(0, ai.SlotOf(me.Id)));
        var regroups = ai.Groups[gi].Regroups;
        var meanMoving = sumMoving / Math.Max(1, ticks);
        Line($"horde targets: {g.Horde.Count(z => z.Alive)} zombies chasing the host round the dais for {Ticks / 60} s ({nodes.Count} target nodes, {ai.Flow.Built - built0} flow fields built, "
             + $"all dropped once): at worst {worstAway} of {worstAwayOf} moving away from the host at once (tick {worstAt}; {meanMoving} moving on average), "
             + $"the longest any one moved away {longestAway} ticks, at worst {worstOrigin} heading away and for the origin; the kited group regrouped {regroups} time(s); "
             + $"{badRegroup} member-ticks closing up from ahead of the centre");
        if (nodes.Count < 10 || ai.Flow.Built - built0 < 10)
            Fail("the scenario should cross many nav nodes and rebuild flow fields");
        if (worstAway > Math.Max(4, worstAwayOf / 6))
            Fail($"the horde should never turn away from its target together ({worstAway} of {worstAwayOf} at once)");
        if (worstOrigin > 3)
            Fail($"zombies should never head for the origin away from their target ({worstOrigin} at once)");
        if (longestAway > 60)
            Fail($"a zombie should not walk away from its target for long ({longestAway} ticks)");
        if (regroups > 0)
            Fail("a group chasing a player who keeps moving is not blocked and should not regroup");
        if (badRegroup > 0)
            Fail("a regrouping member should only close up from behind the group's centre");
    }
}
