using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The Insane squad planner. Planning is amortised: at most one squad is planned per server tick (round robin), and
/// each squad about twice a second, so the cost doesn't grow with the bot count. A plan reads only the squad's
/// blackboard (what members saw and heard), the map and the members themselves, and gives each member an order:
///   - Move: the squad heads for a shared goal (the mode's objective, the last contact, or a sweep of the map) in a
///     wedge around the leader; near trouble it bounds: one element moves while the other holds and watches ahead.
///     Too spread out (half the squad 25+ m from the leader): regroup, the leader waits and the rest close up.
///   - Assault (a fresh contact): bases of fire (suppressor, leader) take spots with a line to the target's last known
///     position and hold their distance; flankers go round to a spot 85-155 degrees off the suppressor's angle, on a
///     route that avoids what the target can see, wait at a staging point and push together when everyone is in
///     place (timed arrival); then the bases of fire put suppressive fire on the spot so the target looks their way.
///     A fourth member makes a crossfire from a second angle, or a pincer (a second flanker on the other side).
///     A squadmate killed in view of their killer calls them out, and the nearest member goes to trade the kill.
///   - Retreat: down to the last member of a 3-4 squad, fall back towards the team's spawns (under smoke) and regroup.
/// </summary>
public sealed partial class Game
{
    private const double SquadPlanInterval = 0.45;
    private const double SquadFreshContact = 6; // seconds a seen contact stays worth assaulting
    private const double SquadFreshHeard = 3;
    private static readonly SquadRole[] ActingOrder = [SquadRole.Suppressor, SquadRole.Support, SquadRole.Flanker];
    private Func<byte, bool>? _squadAliveFn;

    /// <summary>Once per server tick (BotTickShared): the roster, then the next squad due for a plan.</summary>
    private void SquadTickShared(double now)
    {
        SquadRoster(now);
        if (_squads.Count == 0)
            return;
        var t0 = Stopwatch.GetTimestamp();
        for (var k = 0; k < _squads.Count; k++)
        {
            var i = (_squadPlanCursor + k) % _squads.Count;
            if (now < _squads[i].NextPlan)
                continue;
            SquadPlan(_squads[i], now);
            _squadPlanCursor = (i + 1) % _squads.Count;
            break;
        }
        SquadStats.PlanMs += Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
    }

    private static Vector3 Rot(Vector3 v, float a)
    {
        var (sin, cos) = MathF.SinCos(a);
        return new Vector3(v.X * cos - v.Z * sin, 0, v.X * sin + v.Z * cos);
    }

    private static Vector3 FlatDir(Vector3 v, Vector3 fallback)
    {
        v.Y = 0;
        var l = v.Length();
        return l > 0.01f ? v / l : fallback;
    }

    private void SetTactic(BotSquad s, SquadTactic t, double now)
    {
        if (s.Tactic == t)
            return;
        s.Tactic = t;
        s.TacticSince = now;
        s.Bounding = false;
        if (t != SquadTactic.Assault)
        {
            s.Target = byte.MaxValue;
            s.Executing = false;
        }
        switch (t)
        {
            case SquadTactic.Assault: SquadStats.Assaults++; break;
            case SquadTactic.Regroup: SquadStats.Regroups++; break;
            case SquadTactic.Retreat: SquadStats.Retreats++; break;
        }
    }

    private void SquadPlan(BotSquad s, double now)
    {
        s.NextPlan = now + SquadPlanInterval;
        SquadStats.Plans++;
        var nav = NavGraph.Current;
        if (nav.Nodes.Length == 0)
            return;
        s.EnsureScratch(nav);
        s.Board.Decay(now, _squadAliveFn ??= id => Players.TryGetValue(id, out var e) && e.Alive);

        // Who leads right now: the leader, else the next one alive (base of fire first); a flag carrier always does.
        s.Acting = s.Leader is { Alive: true } ? s.Leader : null;
        foreach (var role in ActingOrder)
            foreach (var m in s.Members)
                if (s.Acting == null && m.Alive && m.Bot!.Role == role)
                    s.Acting = m;
        foreach (var m in s.Members)
            if (m.Alive && CarriedFlagOf(m) >= 0)
                s.Acting = m;
        if (s.Acting == null)
        {
            SetTactic(s, SquadTactic.Move, now);
            return;
        }
        var centroid = Vector3.Zero;
        var alive = 0;
        foreach (var m in s.Members)
            if (m.Alive)
            {
                centroid += m.Position;
                alive++;
            }
        centroid /= alive;

        if (s.Tactic == SquadTactic.Retreat)
        {
            var spread = 0f;
            foreach (var m in s.Members)
                if (m.Alive)
                    spread = MathF.Max(spread, Flat(m.Position - centroid));
            var held = now - s.TacticSince;
            if (held < 12 && !(alive >= 2 && spread < 12 && held > 4))
            {
                PlanRetreat(s, centroid, nav, now);
                return;
            }
            SetTactic(s, SquadTactic.Regroup, now);
        }
        else if (s.Members.Count >= 3 && alive == 1 && s.Tactic == SquadTactic.Assault)
        {
            SetTactic(s, SquadTactic.Retreat, now);
            s.Rally = RallyPoint(s, centroid, nav);
            PlanRetreat(s, centroid, nav, now);
            return;
        }

        if (BestContact(s, centroid, now) is { } contact)
        {
            PlanAssault(s, contact, centroid, nav, now);
            return;
        }
        if (s.Tactic == SquadTactic.Assault)
            SetTactic(s, SquadTactic.Move, now);
        PlanMove(s, centroid, nav, now);
    }

    /// <summary>The contact to go after: fresh sightings first (nearest, newest; the current target a little preferred), else a fresh sound.</summary>
    private static SquadContact? BestContact(BotSquad s, Vector3 centroid, double now)
    {
        var best = -1;
        var bestScore = float.MaxValue;
        for (var i = 0; i < s.Board.Count; i++)
        {
            var c = s.Board[i];
            var age = (float)(now - c.Time);
            if (age > (c.Seen ? SquadFreshContact : SquadFreshHeard))
                continue;
            var score = age * 3f + Flat(c.Pos - centroid) * 0.1f + (c.Seen ? 0f : 12f) - (c.Enemy == s.Target && c.Enemy != byte.MaxValue ? 4f : 0f) - (c.Callout ? 2f : 0f);
            if (score < bestScore)
            {
                bestScore = score;
                best = i;
            }
        }
        return best < 0 ? null : s.Board[best];
    }

    /// <summary>The nav node nearest <paramref name="want"/> in the main walkable area, at about the same height, or null.</summary>
    private static Vector3? SnapNode(NavGraph nav, Vector3 want, float maxOff = 4.5f)
    {
        var n = nav.Nearest(want, needSight: false);
        if (n < 0 || nav.Component[n] != nav.MainComponent)
            return null;
        var q = nav.Nodes[n];
        return Flat(q - want) <= maxOff && MathF.Abs(q.Y - want.Y) < 4f ? q : null;
    }

    // ---- Assault ---------------------------------------------------------------------------------------------------

    private void PlanAssault(BotSquad s, SquadContact c, Vector3 centroid, NavGraph nav, double now)
    {
        var target = c.Pos;
        var aim = target + new Vector3(0, 1.1f, 0);
        var newTarget = s.Tactic != SquadTactic.Assault || (c.Enemy != byte.MaxValue && c.Enemy != s.Target);
        SetTactic(s, SquadTactic.Assault, now);
        var replan = newTarget || Flat(target - s.TargetAt) > 6f || now - s.AssaultPlannedAt > 8;
        s.Target = c.Enemy;
        s.LastContactTime = c.Time;
        s.LastContactPos = target;

        Player? baseOfFire = null;
        foreach (var m in s.Members)
            if (m.Alive && m.Bot!.Role == SquadRole.Suppressor)
                baseOfFire = m;
        baseOfFire ??= s.Acting!;

        if (replan)
        {
            s.TargetAt = target;
            s.AssaultPlannedAt = now;
            if (newTarget)
            {
                s.Executing = false;
                s.ThrewFrag = false;
            }
            s.ExposureGen++;
            s.ClaimGen++;
            s.ExposureEye = target + new Vector3(0, 1.5f, 0);
            var axis = FlatDir(baseOfFire.Position - target, FlatDir(centroid - target, Vector3.UnitX));
            // Flank on the side that's more behind them (their facing when seen; unseen: assume they face us).
            var facing = c.Seen ? World.FlatForward(c.Yaw) : axis;
            var side = Vector3.Dot(Rot(axis, 1.92f), facing) <= Vector3.Dot(Rot(axis, -1.92f), facing) ? 1f : -1f;
            var flankers = 0;
            for (var i = 0; i < s.Members.Count; i++)
            {
                var m = s.Members[i];
                var b = m.Bot!;
                switch (b.Role)
                {
                    case SquadRole.Flanker:
                    case SquadRole.Support when PincerSupport(b):
                    {
                        var mySide = flankers++ == 0 ? side : -side;
                        var range = Math.Clamp(PreferredRange(m.WeaponDef) * 0.9f, 7f, 15f);
                        PickFlankSpot(nav, target, aim, axis, mySide, range, out s.FlankGoal[i], out s.FlankStage[i]);
                        b.Order.FlankFrom = m.Position;
                        break;
                    }
                    case SquadRole.Support:
                        s.FireSpot[i] = PickFireSpot(nav, m, target, aim, axis, -side * 1.15f); // crossfire from the other side
                        break;
                    case SquadRole.Leader when baseOfFire != m:
                        s.FireSpot[i] = PickFireSpot(nav, m, target, aim, axis, -side * 0.6f);
                        break;
                    default:
                        s.FireSpot[i] = PickFireSpot(nav, m, target, aim, axis, 0f);
                        break;
                }
            }
        }

        // Orders, and whether the flankers are in place for the push.
        var fresh = now - c.Time < 3.5;
        var flankersReady = true;
        for (var i = 0; i < s.Members.Count; i++)
        {
            var m = s.Members[i];
            if (!m.Alive)
                continue;
            var b = m.Bot!;
            var flankFrom = b.Order.FlankFrom;
            var o = new SquadOrder { Look = aim, HasLook = true, FlankFrom = flankFrom, FlankTo = target };
            if (b.Role == SquadRole.Flanker || PincerSupport(b))
            {
                var stage = s.FlankStage[i];
                var atStage = Flat(m.Position - stage) < 3f;
                var goal = s.Executing ? s.FlankGoal[i] : stage;
                var toGoal = Flat(goal - m.Position);
                var eta = (b.PathLen > 0 && Vector3.DistanceSquared(b.PathGoal, goal) < 9f ? PathLeft(b, nav, m.Position) : toGoal * 1.4f) / (toGoal > 10 ? 9f : 6f);
                s.FlankEta[i] = eta;
                if (!s.Executing && (atStage || eta < 1f))
                {
                    o.Kind = SquadOrderKind.Hold; // in place: wait for the others, out of sight
                    o.Goal = atStage ? m.Position : stage;
                }
                else
                {
                    if (!s.Executing)
                        flankersReady = false;
                    o.Kind = SquadOrderKind.Flank;
                    o.Goal = goal;
                    o.FlankRoute = true;
                    o.Sprint = toGoal > 10f && Flat(target - m.Position) > 16f;
                    o.HasLook = Flat(target - m.Position) < 24f;
                }
            }
            else
            {
                o.Kind = SquadOrderKind.Suppress;
                o.Goal = s.FireSpot[i];
                o.HoldBack = true;
                o.Fire = fresh && (s.Executing || now - c.Time < 1.5);
            }
            b.Order = o;
        }
        if (!s.Executing && (flankersReady || now - s.AssaultPlannedAt > 14))
        {
            s.Executing = true;
            s.ExecuteAt = now;
            SquadStats.Executes++;
            s.NextPlan = now + 0.05; // release the flankers right away
        }

        // The push: a frag on their spot from someone who can't see them (flush them towards the flankers).
        if (s.Executing && !s.ThrewFrag && now - c.Time is > 0.5 and < 5)
            foreach (var m in s.Members)
            {
                var b = m.Bot!;
                if (!m.Alive || b.Role is SquadRole.Flanker || PincerSupport(b) || m.Grenade is not (ThrowableKind.Frag or ThrowableKind.Impact)
                    || m.GrenadesLeft <= 0 || now < m.NextThrow)
                    continue;
                var d = Flat(target - m.Position);
                if (d is > 8f and < 24f && !HasLineOfSight(m.Eye, aim))
                {
                    BotThrowAt(m, b, target, now);
                    s.ThrewFrag = true;
                    SquadStats.Frags++;
                    break;
                }
            }
        ApplyTrade(s, nav, now);
    }

    /// <summary>Metres left along the bot's planned path from where it stands.</summary>
    private static float PathLeft(BotBrain b, NavGraph nav, Vector3 pos)
    {
        var total = 0f;
        var prev = pos;
        for (var k = b.PathIdx; k < b.PathLen; k++)
        {
            var q = nav.Nodes[b.Path[k]];
            total += Flat(q - prev);
            prev = q;
        }
        return total + Flat(b.PathGoal - prev);
    }

    /// <summary>
    /// The flank spot: <paramref name="range"/> metres from the target at 85-155 degrees off the base of fire's angle
    /// (110 first), preferring one with a line to the target; the staging point is 8 m further out on the same bearing.
    /// </summary>
    private void PickFlankSpot(NavGraph nav, Vector3 target, Vector3 aim, Vector3 axis, float side, float range, out Vector3 goal, out Vector3 stage)
    {
        ReadOnlySpan<float> angles = [1.92f, 2.36f, 1.48f, 2.7f];
        Vector3? fallback = null;
        var fallbackDir = Rot(axis, side * angles[0]);
        foreach (var a in angles)
        {
            var dir = Rot(axis, side * a);
            if (SnapNode(nav, target + dir * range) is not { } q)
                continue;
            if (HasLineOfSight(q + new Vector3(0, 1.5f, 0), aim))
            {
                goal = q;
                stage = SnapNode(nav, target + dir * (range + 8f), 5f) ?? q;
                return;
            }
            if (fallback == null)
            {
                fallback = q;
                fallbackDir = dir;
            }
        }
        goal = fallback ?? SnapNode(nav, target + fallbackDir * range * 0.6f, 6f) ?? target;
        stage = SnapNode(nav, target + fallbackDir * (range + 8f), 5f) ?? goal;
    }

    /// <summary>
    /// A firing spot for a base of fire: where it stands if it already has a line to the target at a sane range and about
    /// the wanted angle, else a node 12-26 m out at <paramref name="angle"/> (+-15/30 degrees) with a line to it.
    /// </summary>
    private Vector3 PickFireSpot(NavGraph nav, Player m, Vector3 target, Vector3 aim, Vector3 axis, float angle)
    {
        var dist = Flat(m.Position - target);
        var mine = FlatDir(m.Position - target, axis);
        var want = Rot(axis, angle);
        if (dist is > 7f and < 40f && Vector3.Dot(mine, want) > 0.8f && HasLineOfSight(m.Eye, aim))
            return m.Position;
        var d = Math.Clamp(dist, 12f, 26f);
        ReadOnlySpan<float> offsets = [0f, 0.26f, -0.26f, 0.52f, -0.52f];
        Vector3? first = null;
        foreach (var k in offsets)
        {
            if (SnapNode(nav, target + Rot(axis, angle + k) * d) is not { } q)
                continue;
            first ??= q;
            if (HasLineOfSight(q + new Vector3(0, 1.5f, 0), aim))
                return q;
        }
        return first ?? m.Position;
    }

    // ---- Trades -------------------------------------------------------------------------------------------------

    /// <summary>The trader (a squadmate saw their killer's spot) runs at it with the spot pre-aimed.</summary>
    private void ApplyTrade(BotSquad s, NavGraph nav, double now)
    {
        if (s.Trader is not { Alive: true } t || now >= s.TradeUntil)
            return;
        var look = s.Board.TryGet(s.TradeTarget, out var killer) ? killer.Pos : s.TradeAt;
        t.Bot!.Order = new SquadOrder
        {
            Kind = SquadOrderKind.Trade, Goal = s.TradeAt, Look = look + new Vector3(0, 1.1f, 0), HasLook = Flat(s.TradeAt - t.Position) < 20f,
            Sprint = Flat(s.TradeAt - t.Position) > 12f,
        };
    }

    /// <summary>A member died: if they saw their killer it's a callout, and the nearest squadmate goes to trade.</summary>
    private void SquadNoteKill(Player killer, Player victim, double now)
    {
        if (victim.Bot?.Squad is { } s && killer != victim && AreEnemies(victim, killer) && HasLineOfSight(victim.Eye, killer.Chest))
        {
            s.Board.Report(killer.Id, killer.Position, killer.Yaw, now, victim.Id, seen: true, callout: true);
            Player? best = null;
            var bestD = 35f;
            foreach (var m in s.Members)
            {
                if (m == victim || !m.Alive || (s.Executing && m.Bot!.Role == SquadRole.Flanker))
                    continue;
                var d = Flat(m.Position - victim.Position);
                if (d < bestD)
                {
                    bestD = d;
                    best = m;
                }
            }
            if (best != null)
            {
                s.Trader = best;
                s.TradeAt = victim.Position;
                s.TradeTarget = killer.Id;
                s.TradeUntil = now + 6;
                s.NextPlan = Math.Min(s.NextPlan, now + 0.05);
                SquadStats.Trades++;
            }
        }
        if (killer.Bot?.Squad is { } ks && killer != victim)
        {
            ks.Board.Forget(victim.Id);
            if (victim.Id == ks.TradeTarget && now < ks.TradeUntil)
            {
                SquadStats.TradedKills++;
                ks.TradeUntil = 0;
            }
            if (ks.Tactic == SquadTactic.Assault && (killer.Bot.Role == SquadRole.Flanker || PincerSupport(killer.Bot)))
                SquadStats.FlankKills++;
        }
    }

    // ---- Move, bounding overwatch and regroup ----------------------------------------------------------------------

    private void PlanMove(BotSquad s, Vector3 centroid, NavGraph nav, double now)
    {
        var lead = s.Acting!;
        if (ObjectiveSpot(lead) is { } objective)
        {
            if (!s.GoalIsObjective || Flat(objective - s.Goal) > 3f)
                s.ClaimGen++;
            s.Goal = objective;
            s.GoalIsObjective = true;
        }
        else if (now - s.LastContactTime < 30 && Flat(s.LastContactPos - centroid) > 6f)
        {
            s.Goal = s.LastContactPos; // sweep where the last contact was
            s.GoalIsObjective = false;
            s.GoalUntil = now + 10;
        }
        else
        {
            if (now - s.LastContactTime < 30)
                s.LastContactTime = -100; // got there: nothing
            if (s.GoalIsObjective || now >= s.GoalUntil || Flat(s.Goal - lead.Position) < 5f)
            {
                s.Goal = SweepGoal(nav, s, lead);
                s.GoalUntil = now + 25;
                s.GoalIsObjective = false;
            }
        }

        // Regroup when half the squad is 25+ m from the leader (respawned members 70+ m away just catch up).
        int others = 0, far = 0;
        var maxD = 0f;
        foreach (var m in s.Members)
        {
            if (m == lead || !m.Alive)
                continue;
            others++;
            var d = Flat(m.Position - lead.Position);
            maxD = MathF.Max(maxD, d);
            if (d is > 25f and < 70f)
                far++;
        }
        if (s.Tactic == SquadTactic.Regroup)
        {
            if (maxD < 10f || now - s.TacticSince > 15)
                SetTactic(s, SquadTactic.Move, now);
        }
        else if (far > 0 && far * 2 >= others)
            SetTactic(s, SquadTactic.Regroup, now);

        if (s.Tactic == SquadTactic.Regroup)
        {
            foreach (var m in s.Members)
            {
                if (!m.Alive)
                    continue;
                m.Bot!.Order = m == lead
                    ? new SquadOrder { Kind = SquadOrderKind.Hold, Goal = lead.Position, Look = s.Goal + new Vector3(0, 1.2f, 0), HasLook = true }
                    : new SquadOrder { Kind = SquadOrderKind.Follow, Goal = lead.Position, Sprint = true };
            }
            ApplyTrade(s, nav, now);
            return;
        }

        var toGoal = Flat(s.Goal - centroid);
        var heading = FlatDir(s.Goal - lead.Position, World.FlatForward(lead.Yaw));
        var bounding = others > 0 && toGoal > 22f && (now - s.LastContactTime < 15 || s.GoalIsObjective);
        if (bounding)
            PlanBounds(s, centroid, heading, nav, now);
        else
        {
            s.Bounding = false;
            // Wedge around the leader: two members out to the sides behind, a fourth at the back.
            ReadOnlySpan<Vector2> wedge = [new(-3.5f, -3f), new(3.5f, -3f), new(0f, -6f)];
            var right = new Vector3(-heading.Z, 0, heading.X);
            var k = 0;
            foreach (var m in s.Members)
            {
                if (!m.Alive)
                    continue;
                if (m == lead)
                {
                    m.Bot!.Order = new SquadOrder { Kind = SquadOrderKind.Follow, Goal = s.Goal, Sprint = toGoal > 10f && maxD < 10f }; // walk while they catch up
                    continue;
                }
                var off = wedge[Math.Min(k++, wedge.Length - 1)];
                var want = lead.Position + right * off.X + heading * off.Y;
                var slot = SnapNode(nav, want, 3f) ?? lead.Position;
                m.Bot!.Order = new SquadOrder { Kind = SquadOrderKind.Follow, Goal = slot, Sprint = Flat(slot - m.Position) > 6f };
            }
        }
        ApplyTrade(s, nav, now);
    }

    /// <summary>
    /// Bounding overwatch: element 0 (leader and suppressor) and element 1 (the rest) take turns. The moving element
    /// goes to the next bound point 14 m on towards the goal; the other holds, crouched, watching ahead. They swap when
    /// the movers are there (or after 7 s).
    /// </summary>
    private void PlanBounds(BotSquad s, Vector3 centroid, Vector3 heading, NavGraph nav, double now)
    {
        static int Element(BotBrain mb) => mb.Role is SquadRole.Leader or SquadRole.Suppressor ? 0 : 1;
        Vector3 Next(Vector3 start)
        {
            var step = MathF.Min(14f, Flat(s.Goal - start));
            return SnapNode(nav, start + FlatDir(s.Goal - start, heading) * step, 5f) ?? s.Goal;
        }
        if (!s.Bounding)
        {
            s.Bounding = true;
            s.BoundGroup = 0;
            s.BoundSince = now;
            s.BoundPoint = Next(centroid);
        }
        bool moversAlive = false, arrived = true;
        foreach (var m in s.Members)
            if (m.Alive && Element(m.Bot!) == s.BoundGroup)
            {
                moversAlive = true;
                if (Flat(m.Position - s.BoundPoint) > 4.5f)
                    arrived = false;
            }
        if (!moversAlive || arrived || now - s.BoundSince > 7)
        {
            s.BoundGroup ^= 1;
            s.BoundSince = now;
            s.BoundPoint = Next(s.BoundPoint);
        }
        var right = new Vector3(-heading.Z, 0, heading.X);
        var look = s.Goal + new Vector3(0, 1.2f, 0);
        var k = 0;
        foreach (var m in s.Members)
        {
            if (!m.Alive)
                continue;
            var b = m.Bot!;
            if (Element(b) == s.BoundGroup)
            {
                var slot = SnapNode(nav, s.BoundPoint + right * ((k++ & 1) == 0 ? -2.5f : 2.5f), 3f) ?? s.BoundPoint;
                b.Order = new SquadOrder { Kind = SquadOrderKind.Follow, Goal = slot, Sprint = Flat(slot - m.Position) > 10f };
            }
            else
                b.Order = new SquadOrder { Kind = SquadOrderKind.Hold, Goal = m.Position, Look = look, HasLook = true };
        }
    }

    /// <summary>Somewhere worth sweeping: a far node, leaning towards the middle of the map and the enemy's side of it.</summary>
    private Vector3 SweepGoal(NavGraph nav, BotSquad s, Player lead)
    {
        if (nav.MainNodes.Length == 0)
            return RandomNode(nav, lead);
        var enemySpawns = Objectives.TeamSpawns[1 - Math.Min((int)s.Team, 1)];
        var toward = Vector3.Zero;
        foreach (var sp in enemySpawns)
            toward += sp;
        toward = enemySpawns.Count > 0 ? toward / enemySpawns.Count * 0.5f : Vector3.Zero; // halfway from the middle to their spawns
        var best = nav.Nodes[nav.MainNodes[0]];
        var bestScore = float.MinValue;
        for (var i = 0; i < 12; i++)
        {
            var q = nav.Nodes[nav.MainNodes[_rng.Next(nav.MainNodes.Length)]];
            var d = Flat(q - lead.Position);
            var score = (float)_rng.NextDouble() * 30f - Flat(q - toward) * 0.5f + (d < 20f ? -40f : 0f);
            if (score > bestScore)
            {
                bestScore = score;
                best = q;
            }
        }
        return best;
    }

    // ---- Retreat ---------------------------------------------------------------------------------------------------

    /// <summary>Where to fall back to: the team's spawn area, else 20 m directly away from the last contact.</summary>
    private Vector3 RallyPoint(BotSquad s, Vector3 centroid, NavGraph nav)
    {
        var spawns = Objectives.TeamSpawns[Math.Min((int)s.Team, 1)];
        if (spawns.Count > 0)
        {
            var sum = Vector3.Zero;
            foreach (var sp in spawns)
                sum += sp;
            if (SnapNode(nav, sum / spawns.Count, 8f) is { } q)
                return q;
            return spawns[0];
        }
        return SnapNode(nav, centroid + FlatDir(centroid - s.LastContactPos, Vector3.UnitX) * 20f, 8f) ?? centroid;
    }

    private void PlanRetreat(BotSquad s, Vector3 centroid, NavGraph nav, double now)
    {
        var threat = s.LastContactPos + new Vector3(0, 1.1f, 0);
        foreach (var m in s.Members)
        {
            if (!m.Alive)
                continue;
            var b = m.Bot!;
            var d = Flat(s.Rally - m.Position);
            b.Order = new SquadOrder { Kind = SquadOrderKind.Fallback, Goal = s.Rally, Sprint = d > 6f, Look = threat, HasLook = d < 6f };
            // Cover the withdrawal: smoke between us and them, once.
            if (now - s.TacticSince < 1 && m.Grenade == ThrowableKind.Smoke && m.GrenadesLeft > 0 && now >= m.NextThrow)
            {
                var toThreat = s.LastContactPos - m.Position;
                var dist = Flat(toThreat);
                if (dist > 6f)
                {
                    BotThrowAt(m, b, m.Position + FlatDir(toThreat, Vector3.UnitX) * MathF.Min(8f, dist * 0.5f), now);
                    SquadStats.Smokes++;
                }
            }
        }
    }
}
