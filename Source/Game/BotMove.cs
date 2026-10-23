using System.Numerics;

namespace CallOfSlop;

/// <summary>Bot movement: path planning (rationed A*), path following, combat footwork, separation and getting unstuck.</summary>
public sealed partial class Game
{
    private const int BotSearchesPerTick = 3; // A* searches allowed per server tick, shared by all bots
    private const double BotNavBuildMs = 3; // per server tick, while a new map's nav graph is being built

    private readonly NavSearch _botSearch = new();

    /// <summary>
    /// Enemy scans (a sight line to every enemy) allowed per server tick, shared by all bots: with 31 bots the
    /// 0.2-0.3 s scans can bunch up on one tick; the extras wait a tick or two, which nobody can notice.
    /// </summary>
    private const int BotScansPerTick = 4;

    private int _botScanBudget;

    /// <summary>Takes one of this tick's enemy scans (false: none left, try again next tick).</summary>
    private bool TakeBotScan()
    {
        if (_botScanBudget <= 0)
            return false;
        _botScanBudget--;
        return true;
    }
    private int _botPathBudget;
    private uint _botBudgetTick = uint.MaxValue;

    /// <summary>Once per server tick (from the first bot's tick): refill the search budget, advance a nav graph build, plan an Insane squad.</summary>
    private void BotTickShared(double now)
    {
        if (_botBudgetTick == ServerTick)
            return;
        _botBudgetTick = ServerTick;
        _botPathBudget = BotSearchesPerTick;
        _botScanBudget = Math.Max(BotScansPerTick, Players.Count / 5); // see TakeBotScan
        NavGraph.Pump(BotNavBuildMs);
        VantageMap.Ensure(World.Map, NavGraph.Current); // the map's vantage points, on a worker thread (once per nav graph)
        World.Map.PrepareFoliageCover(); // bot sight through foliage: built once per map on a worker thread (FoliageCover.cs)
        SquadTickShared(now); // Insane squads: roster and one squad's plan (SquadPlanner.cs)
    }

    /// <summary>Where the bot wants to move this tick, and what to look at, for its current mode.</summary>
    private BotIntent BotSteer(Player p, BotBrain b, Player? target, bool visible, WeaponDef def, double now)
    {
        var nav = b.PathGraph!;
        var prof = b.Profile;
        var s = new BotIntent();
        switch (b.Mode)
        {
            case BotMode.Engage when target != null:
            {
                var to = target.Position - p.Position;
                to.Y = 0;
                var dist = to.Length();
                var dir = dist > 0.01f ? to / dist : Vector3.Zero;
                var pref = MathF.Max(3f, PreferredRange(def) * prof.RangeMult);
                var hurt = p.Health <= World.HealthAt(prof.RetreatHealth) && prof.Aggression < 0.9f;
                var holdBack = (prof.Holds ? dist < pref * 2f : prof.Kind == BotPersonality.Marksman && dist < 60f) || b.Order.HoldBack;
                if (dist > pref + 3 && !hurt && !holdBack)
                    s.Wish += BotFollow(p, b, nav, target.Position, now, ref s); // close in along the path (around cover)
                else if (dist < pref - 3 || hurt)
                    s.Wish -= dir;
                // Strafe: rhythm and commitment depend on personality.
                if (now >= b.StrafeUntil)
                {
                    b.StrafeDir = prof.Kind == BotPersonality.Flanker && _rng.NextDouble() < 0.75 ? b.StrafeDir : _rng.NextDouble() < 0.5 ? -1 : 1;
                    b.StrafeUntil = now + prof.StrafeMin + _rng.NextDouble() * (prof.StrafeMax - prof.StrafeMin);
                }
                s.Wish += new Vector3(-dir.Z, 0, dir.X) * b.StrafeDir * prof.StrafeStrength * (b.Skill == BotDifficulty.Easy ? 0.5f : 1f);
                break;
            }
            case BotMode.Cover:
            {
                s.Wish = BotFollow(p, b, nav, b.CoverSpot, now, ref s);
                s.Stationary = s.Wish == Vector3.Zero;
                s.HasLook = true;
                s.Look = b.LastSeenAt + new Vector3(0, 1.2f, 0);
                break;
            }
            case BotMode.Investigate:
            {
                var d = Flat(b.InvestigateAt - p.Position);
                if (d < 2f)
                {
                    // Nothing here: forget it.
                    b.LastSeenTime = -100;
                    b.HeardTime = -100;
                }
                s.Wish = BotFollow(p, b, nav, b.InvestigateAt, now, ref s);
                s.Sprint = d > 15f && prof.Aggression >= 0.5f;
                if (d < 30f && !s.Mantle)
                {
                    s.HasLook = true; // pre-aim the spot at chest height
                    s.Look = b.InvestigateAt + new Vector3(0, 1.2f, 0);
                }
                break;
            }
            case BotMode.Squad:
                SquadSteer(p, b, nav, now, ref s);
                break;
            case BotMode.Overwatch:
                VantageSteer(p, b, nav, visible, now, ref s); // BotVantage.cs
                break;
            case BotMode.Objective when b.Objective is { } goal:
            {
                var spot = goal;
                if (Flat(goal - p.Position) < 3f && prof.Holds)
                    spot = goal + new Vector3(MathF.Sin((float)now * 0.7f + b.Seed), 0, MathF.Cos((float)now * 0.5f + b.Seed)) * 2.5f; // pace the area
                s.Wish = BotFollow(p, b, nav, spot, now, ref s);
                s.Sprint = Flat(goal - p.Position) > 8f;
                break;
            }
            default:
            {
                var goal = BotRoamGoal(p, b, nav, now);
                s.Wish = BotFollow(p, b, nav, goal, now, ref s);
                var far = Flat(goal - p.Position);
                s.Sprint = far > 8f && b.HoldUntil == 0;
                s.Stationary = s.Wish == Vector3.Zero || (b.HoldUntil > 0 && far < 1.5f);
                break;
            }
        }

        b.IsHoldingPosition = BotHoldingPosition(b, s, now); // stuck detection leaves these alone (BotVantage.cs)

        // Check corners: glance aside now and then while moving around with nobody in sight.
        if (!visible && !s.HasLook && !s.Mantle)
        {
            if (now >= b.NextGlance)
            {
                b.NextGlance = now + 1.5 + _rng.NextDouble() * 2.5;
                b.GlanceUntil = now + 0.45 + _rng.NextDouble() * 0.3;
                var baseYaw = s.Wish.LengthSquared() > 0.01f ? MathF.Atan2(s.Wish.X, -s.Wish.Z) : p.Yaw;
                b.GlanceYaw = baseYaw + (_rng.NextDouble() < 0.5 ? -1 : 1) * (0.6f + (float)_rng.NextDouble() * 0.7f);
                if (now - b.HurtTime < 2 || now - b.HeardTime < 2)
                    b.GlanceYaw = MathF.Atan2(b.HeardAt.X - p.Position.X, -(b.HeardAt.Z - p.Position.Z)); // look where it came from
            }
            if (now < b.GlanceUntil)
            {
                s.HasLook = true;
                s.Look = p.Eye + World.FlatForward(b.GlanceYaw) * 10f;
            }
        }

        if (b.SelfTestHold)
        {
            s.Wish = Vector3.Zero; // self-test: stay on the spot (BotFoliage.cs)
            s.Sprint = false;
            s.Stationary = true;
        }
        BotSeparate(p, ref s.Wish);
        BotAvoidMines(p, ref s.Wish); // step round landmines (GameMines.cs)
        BotUnstick(p, b, ref s, now);
        return s;
    }

    /// <summary>
    /// Direction to walk to follow the path towards <paramref name="goal"/> (re-planning when the goal moves), with
    /// look-ahead shortcuts and corner smoothing. Zero when there. Sets <see cref="BotIntent.Mantle"/> at ledges.
    /// </summary>
    private Vector3 BotFollow(Player p, BotBrain b, NavGraph nav, Vector3 goal, double now, ref BotIntent s)
    {
        var pos = p.Position;
        if (Vector3.DistanceSquared(goal, b.PathGoal) > 9f)
            b.NeedPath = true;
        if (b.NeedPath && now >= b.NextPathTime && _botPathBudget > 0)
            BotPlan(p, b, nav, goal, now);

        // Advance past nodes we've reached.
        while (b.PathIdx < b.PathLen)
        {
            var node = nav.Nodes[b.Path[b.PathIdx]];
            var dy = node.Y - pos.Y;
            if (Flat(node - pos) < 0.8f && dy < 1.0f && dy > -2.5f)
                BotAdvance(b, nav);
            else
                break;
        }

        Vector3 aimAt;
        if (b.PathIdx >= b.PathLen)
        {
            aimAt = goal;
            if (Flat(goal - pos) < 0.7f)
                return Vector3.Zero;
        }
        else
        {
            // Look-ahead: skip a node when the one after it is in plain, level, walkable view.
            if (now >= b.NextShortcut && b.PathIdx + 1 < b.PathLen && !EdgeIs(nav, b.CurEdge, NavGraph.FlagMantle | NavGraph.FlagDrop))
            {
                b.NextShortcut = now + 0.25;
                var next = nav.Nodes[b.Path[b.PathIdx + 1]];
                var e = EdgeBetween(nav, b.Path[b.PathIdx], b.Path[b.PathIdx + 1]);
                if (!EdgeIs(nav, e, NavGraph.FlagMantle | NavGraph.FlagDrop) && CanShortcut(pos, next))
                    BotAdvance(b, nav);
            }
            aimAt = nav.Nodes[b.Path[b.PathIdx]];
            var dh = Flat(aimAt - pos);
            if (b.PathIdx + 1 < b.PathLen && dh < 1.6f && !EdgeIs(nav, b.CurEdge, NavGraph.FlagMantle))
                aimAt = Vector3.Lerp(aimAt, nav.Nodes[b.Path[b.PathIdx + 1]], 0.5f * (1f - dh / 1.6f)); // round the corner
            if (b.CurEdge >= 0 && (nav.EdgeFlags[b.CurEdge] & NavGraph.FlagMantle) != 0)
            {
                var ledge = nav.EdgeLedge[b.CurEdge];
                if (Flat(ledge - pos) < 1.4f && ledge.Y > pos.Y + World.StepHeight)
                {
                    s.Mantle = true;
                    s.HasLook = true;
                    s.Look = new Vector3(ledge.X, p.Eye.Y, ledge.Z);
                }
            }
        }
        b.FollowAim = aimAt;
        b.FollowNode = b.PathIdx < b.PathLen ? b.Path[b.PathIdx] : -1;
        b.FollowTime = now;
        var d = aimAt - pos;
        d.Y = 0;
        var len = d.Length();
        return len > 0.01f ? d / len : Vector3.Zero;
    }

    /// <summary>
    /// Can a bot head straight for a node further along its path? Level and in plain view, or, on terrain, along a line the
    /// slope rules let a player walk (<see cref="World.CanWalkStraight"/>): path smoothing across hillsides, so bots don't
    /// zigzag from grid node to grid node up a slope.
    /// </summary>
    private static bool CanShortcut(Vector3 pos, Vector3 next)
    {
        if (!NavGraph.Clear(pos, next))
            return false;
        if (MathF.Abs(next.Y - pos.Y) < 0.4f)
            return true;
        return World.Map.TerrainCollision?.Layer(pos.Y) is { } t && pos.Y - t.HeightAt(pos.X, pos.Z) < 0.3f && next.Y - t.HeightAt(next.X, next.Z) < 0.3f
            && World.CanWalkStraight(pos, next);
    }

    private static bool EdgeIs(NavGraph nav, int edge, byte flags) => edge >= 0 && (nav.EdgeFlags[edge] & flags) != 0;

    private static int EdgeBetween(NavGraph nav, int from, int to)
    {
        for (var e = nav.EdgeStart[from]; e < nav.EdgeStart[from + 1]; e++)
            if (nav.EdgeTo[e] == to)
                return e;
        for (var e = nav.ExtraHead.Length != 0 ? nav.ExtraHead[from] : -1; e >= 0; e = nav.ExtraNext[e - nav.BuiltEdges])
            if (nav.EdgeTo[e] == to)
                return e; // a way destruction opened (BotNavPatch.cs)
        return -1;
    }

    private static void BotAdvance(BotBrain b, NavGraph nav)
    {
        b.PathIdx++;
        b.CurEdge = b.PathIdx < b.PathLen ? EdgeBetween(nav, b.Path[b.PathIdx - 1], b.Path[b.PathIdx]) : -1;
    }

    /// <summary>Runs one A* search (rationed: a few per tick for all bots together).</summary>
    private void BotPlan(Player p, BotBrain b, NavGraph nav, Vector3 goal, double now)
    {
        _botPathBudget--;
        b.NextPathTime = now + 0.6 + _rng.NextDouble() * 0.3;
        b.NeedPath = false;
        b.PathGoal = goal;
        b.PathLen = 0;
        b.PathIdx = 0;
        b.CurEdge = -1;
        var start = nav.Nearest(p.Position);
        var end = nav.Nearest(goal, needSight: false);
        if (start < 0 || end < 0)
            return;
        b.SearchGraph = nav;
        b.SearchNow = now;
        b.Hazard = MineHazard(nav); // nodes next to a landmine cost extra (GameMines.cs)
        if (b.Mode is not (BotMode.Roam or BotMode.Squad))
            b.FlankWidth = 0; // flank routes only while sneaking around, not when chasing or retreating
        if (b.Squad != null)
            SquadBeforeSearch(b);
        b.PathLen = _botSearch.Find(nav, start, end, b.Path, b.EdgePenaltyFn);
        if (b.Squad != null)
            SquadAfterSearch(b);
        // Already past the first node (it's behind us on the way)? Start with the second.
        if (b.PathLen >= 2)
        {
            var n0 = nav.Nodes[b.Path[0]];
            var n1 = nav.Nodes[b.Path[1]];
            if (Flat(n1 - p.Position) < Flat(n1 - n0) && CanShortcut(p.Position, n1))
                BotAdvance(b, nav);
        }
    }

    /// <summary>Simple separation: step away from bots closer than 1.2 m so doorways don't jam.</summary>
    private void BotSeparate(Player p, ref Vector3 wish)
    {
        foreach (var o in Players.Values)
        {
            if (o == p || !o.Alive || o.Bot == null)
                continue;
            var d = p.Position - o.Position;
            d.Y = 0;
            var dist = d.Length();
            if (dist is < 1.2f and > 0.001f)
                wish += d / dist * (1.2f - dist) * 1.5f;
        }
    }

    /// <summary>
    /// Not getting anywhere? First hop/mantle, then avoid the edge we're stuck on and re-plan, then give up on the goal.
    /// "Not getting anywhere" is moving less than 0.45 m in 0.8 s, or, on terrain while following a path, getting no closer to
    /// the next node (walking up a slope and sliding back down it counts as stuck, so the bot re-plans another way up).
    /// </summary>
    private void BotUnstick(Player p, BotBrain b, ref BotIntent s, double now)
    {
        var following = now - b.FollowTime < 0.05 && b.FollowNode >= 0 && b.Mode != BotMode.Engage;
        var aimDist = Flat(b.FollowAim - p.Position);
        if (s.Wish.LengthSquared() < 0.05f || s.Stationary || p.Sim.MantleTicks > 0)
        {
            b.StuckCheckPos = p.Position;
            b.StuckCheckTime = now;
            b.StuckCheckNode = -1;
            return;
        }
        if (now - b.StuckCheckTime < 0.8)
            return;
        var moved = Vector3.Distance(p.Position, b.StuckCheckPos);
        // No closer to the same path node (on terrain): sliding back and forth on a slope.
        var noProgress = following && World.Map.HasTerrain && b.FollowNode == b.StuckCheckNode && aimDist > 2f && b.StuckCheckAim - aimDist < 0.3f;
        b.StuckCheckPos = p.Position;
        b.StuckCheckTime = now;
        b.StuckCheckNode = following ? b.FollowNode : -1;
        b.StuckCheckAim = aimDist;
        if (moved >= 0.45f && !noProgress)
        {
            b.StuckCount = 0;
            return;
        }
        if (noProgress && moved >= 0.45f)
        {
            b.SlopeStalls++;
            b.SlopeStallAt = now;
        }
        b.StuckCount++;
        b.StrafeDir = -b.StrafeDir;
        switch (b.StuckCount)
        {
            case 1:
                b.JumpNext = true; // a ledge we didn't know about? try to climb it
                break;
            case 2:
                if (b.CurEdge >= 0)
                {
                    b.BlockedEdge = b.CurEdge;
                    b.BlockedUntil = now + 10;
                }
                b.ClearPath();
                b.NextPathTime = now;
                b.JumpNext = true;
                break;
            default:
                b.Waypoint = Vector3.Zero;
                b.GoalUntil = 0;
                b.HoldUntil = 0;
                b.LastSeenTime = -100;
                b.HeardTime = -100;
                b.ClearPath();
                b.NextPathTime = now;
                b.StuckCount = 0;
                break;
        }
    }
}
