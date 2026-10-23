using System.Numerics;

namespace CallOfSlop;

/// <summary>Bot decisions: what to do right now, where to roam (by personality), and where to take cover.</summary>
public sealed partial class Game
{
    private readonly List<int> _botNear = new(64); // reused for node queries (no allocations per tick)

    /// <summary>Chooses the bot's mode for this tick: fight, take cover, investigate, play the objective or roam.</summary>
    private void BotDecide(Player p, BotBrain b, Player? target, bool visible, WeaponDef def, double now)
    {
        var prof = b.Profile;
        if (SquadDecide(p, b, target, visible, now))
            return; // Insane: the squad's order (SquadMember.cs)
        if (now >= b.NextObjectiveCheck)
        {
            b.NextObjectiveCheck = now + 1 + _rng.NextDouble() * 0.5;
            // With a nav graph, A* finds the way to the objective itself; while it's still building, use the waypoint routing.
            b.Objective = NavGraph.Current.Nodes.Length > 0 ? ObjectiveSpot(p) : ObjectiveGoal(p, now);
        }
        if (VantageDecide(p, b, target, visible, now))
            return; // a long gun: to a vantage point, or holding one (Source/Game/Bots/Vantage)

        var low = p.Health <= World.HealthAt(prof.RetreatHealth);
        if (visible)
        {
            var wantCover = (low && prof.Aggression < 0.9f) || (b.CoverReload && b.ReloadUntil > 0);
            if (b.Mode == BotMode.Cover)
            {
                // Got to the spot but they can still see us: the cover didn't work, fight.
                if (Flat(p.Position - b.CoverSpot) < 0.8f || now >= b.CoverUntil)
                {
                    b.Mode = BotMode.Engage;
                    b.NextCoverSearch = now + 2;
                }
                return;
            }
            if (wantCover && now >= b.NextCoverSearch)
            {
                b.NextCoverSearch = now + 0.7;
                if (BotFindCover(p, b, target!.Eye))
                {
                    b.Mode = BotMode.Cover;
                    b.CoverUntil = now + (b.ReloadUntil > 0 ? b.ReloadUntil - now + 0.4 : 2.5 + _rng.NextDouble() * 2);
                    return;
                }
            }
            b.Mode = BotMode.Engage;
            return;
        }

        if (b.Mode == BotMode.Cover && now < b.CoverUntil)
            return;
        if (b.Mode == BotMode.Cover && now - b.LastSeenTime < 8)
        {
            // Out of cover: peek back towards where they were.
            b.Mode = BotMode.Investigate;
            b.InvestigateAt = b.LastSeenAt;
            return;
        }

        var lost = now - b.LastSeenTime;
        var chaseFor = 2 + 6 * prof.Aggression;
        if (lost < chaseFor && (!prof.Holds || Flat(b.LastSeenAt - p.Position) < 18))
        {
            b.Mode = BotMode.Investigate;
            b.InvestigateAt = b.LastSeenAt;
            return;
        }
        if (now - b.HeardTime < 5 && (!prof.Holds || Flat(b.HeardAt - p.Position) < 20))
        {
            b.Mode = BotMode.Investigate;
            b.InvestigateAt = b.HeardAt;
            return;
        }
        if (b.Objective != null && b.ObjectiveMinded)
        {
            b.Mode = BotMode.Objective;
            return;
        }
        b.Mode = BotMode.Roam;
    }

    private static float Flat(Vector3 v)
    {
        if (!GravityFrames.Curved)
            return MathF.Sqrt(v.X * v.X + v.Z * v.Z);
        // A planet: across the bot's ground plane, or the straight distance for points well round the curve (below the horizon).
        var rise = Vector3.Dot(v, _botUp);
        return rise < -3f ? v.Length() : MathF.Sqrt(MathF.Max(0, v.LengthSquared() - rise * rise));
    }

    /// <summary>
    /// "Up" for the bot being thought about (set at the start of each bot's tick): on a planet the bots' flat-ground maths
    /// (<see cref="Flat"/>, <see cref="FlatVec"/>, <see cref="Rise"/>, <see cref="Across"/>) works in its ground plane.
    /// </summary>
    private static Vector3 _botUp = Vector3.UnitY;

    /// <summary>The part of a vector across the bot's ground plane (y = 0 on ordinary maps).</summary>
    private static Vector3 FlatVec(Vector3 v) => GravityFrames.Curved ? v - _botUp * Vector3.Dot(v, _botUp) : new Vector3(v.X, 0, v.Z);

    /// <summary>How far a vector goes up (its y on ordinary maps).</summary>
    private static float Rise(Vector3 v) => GravityFrames.Curved ? Vector3.Dot(v, _botUp) : v.Y;

    /// <summary>The ground direction a quarter turn to the right of <paramref name="dir"/> (a flat unit vector).</summary>
    private static Vector3 Across(Vector3 dir) => GravityFrames.Curved ? Vector3.Cross(dir, _botUp) : new Vector3(-dir.Z, 0, dir.X);

    /// <summary>
    /// The roaming goal for the bot's personality: rushers hunt the nearest enemy, flankers aim behind one (on a
    /// route that avoids the straight line), marksmen pick an open perch, anchors hold an open spot near the middle
    /// (or the objective), tacticians drift towards the enemy, chaotic bots do whatever.
    /// </summary>
    private Vector3 BotRoamGoal(Player p, BotBrain b, NavGraph nav, double now)
    {
        if (b.ForcedGoal is { } forced)
            return forced;
        // Holding a spot: small shuffles around it, then move on when the time is up.
        if (b.HoldUntil > 0)
        {
            if (now < b.HoldUntil)
            {
                if (now >= b.ShuffleAt)
                {
                    b.ShuffleAt = now + 4 + _rng.NextDouble() * 3;
                    b.Waypoint = b.HoldAnchor + Noise(3.5f);
                }
                return b.Waypoint;
            }
            b.HoldUntil = 0;
            b.GoalUntil = 0;
        }
        var arrived = b.Waypoint != Vector3.Zero && Flat(b.Waypoint - p.Position) < 2.5f;
        if (arrived && (b.Profile.Holds || b.Personality == BotPersonality.Marksman) && b.GoalUntil > now)
        {
            b.HoldUntil = now + (b.Personality == BotPersonality.Marksman ? 18 + _rng.NextDouble() * 14 : 25 + _rng.NextDouble() * 20);
            b.HoldAnchor = b.Waypoint;
            b.ShuffleAt = now + 3 + _rng.NextDouble() * 3;
            return b.Waypoint;
        }
        if (b.Waypoint != Vector3.Zero && now < b.GoalUntil && !arrived)
            return b.Waypoint;

        b.FlankWidth = 0;
        var enemy = NearestEnemy(p);
        var wander = b.Skill == BotDifficulty.Easy && _rng.NextDouble() < 0.5; // easy bots don't hunt as well
        Vector3 goal;
        switch (b.Personality)
        {
            case BotPersonality.Rusher when enemy != null && !wander:
                goal = enemy.Position + Noise(4);
                b.GoalUntil = now + 3;
                break;
            case BotPersonality.Flanker when enemy != null && !wander:
                // Get behind them, along a route that bends away from the straight line.
                goal = enemy.Position - GravityFrames.FlatForward(enemy.Position, enemy.Yaw) * 8f + Noise(3);
                b.FlankFrom = p.Position;
                b.FlankTo = enemy.Position;
                b.FlankWidth = b.Profile.FlankOffset;
                b.GoalUntil = now + 5;
                break;
            case BotPersonality.Marksman:
                goal = PickSpot(nav, p, n => nav.Openness[n] * 3f + Math.Clamp(Rise(nav.Nodes[n] - p.Position), 0, 8) * 1.5f
                                               - MathF.Abs(Flat(nav.Nodes[n] - p.Position) - 30f) * 0.15f);
                b.GoalUntil = now + 40;
                break;
            case BotPersonality.Anchor:
                goal = b.Objective ?? PickSpot(nav, p, n => nav.Openness[n] * 2f - Flat(nav.Nodes[n]) * 0.08f);
                b.GoalUntil = now + 40;
                break;
            case BotPersonality.Tactician when enemy != null && !wander:
                goal = AnyEnemy(p)!.Position + Noise(8);
                b.GoalUntil = now + 6;
                break;
            case BotPersonality.Chaotic when enemy != null && _rng.NextDouble() < 0.5:
                goal = enemy.Position + Noise(10);
                b.GoalUntil = now + 2 + _rng.NextDouble() * 8;
                break;
            default:
                goal = RandomNode(nav, p);
                b.GoalUntil = now + 12;
                break;
        }
        b.Waypoint = goal == Vector3.Zero ? new Vector3(0.01f, 0, 0) : goal;
        return b.Waypoint;
    }

    private Vector3 Noise(float r) => FlatVec(new(((float)_rng.NextDouble() * 2 - 1) * r, 0, ((float)_rng.NextDouble() * 2 - 1) * r)); // (across the ground: a planet's too)

    private Player? NearestEnemy(Player p)
    {
        Player? best = null;
        var bestD = float.MaxValue;
        foreach (var o in Players.Values)
        {
            if (o == p || !o.Alive || !IsEnemy(p, o))
                continue;
            var d = Vector3.DistanceSquared(o.Position, p.Position);
            if (d < bestD)
            {
                bestD = d;
                best = o;
            }
        }
        return best;
    }

    private Player? AnyEnemy(Player p)
    {
        var count = 0;
        foreach (var o in Players.Values)
            if (o != p && o.Alive && IsEnemy(p, o))
                count++;
        if (count == 0)
            return null;
        var pick = _rng.Next(count);
        foreach (var o in Players.Values)
            if (o != p && o.Alive && IsEnemy(p, o) && pick-- == 0)
                return o;
        return null;
    }

    /// <summary>A random node in the main walkable area (or a map waypoint if the graph is empty).</summary>
    private Vector3 RandomNode(NavGraph nav, Player p)
    {
        if (nav.MainNodes.Length == 0)
        {
            var w = World.Map.Waypoints;
            return w.Count > 0 ? w[_rng.Next(w.Count)] : p.Position + Noise(10);
        }
        // Prefer somewhere not right next to us.
        for (var attempt = 0; attempt < 6; attempt++)
        {
            var n = nav.Nodes[nav.MainNodes[_rng.Next(nav.MainNodes.Length)]];
            if (Flat(n - p.Position) > 10)
                return n;
        }
        return nav.Nodes[nav.MainNodes[_rng.Next(nav.MainNodes.Length)]];
    }

    /// <summary>Best-scoring of a couple of dozen random main-area nodes (cheap, and a little different every time).</summary>
    private Vector3 PickSpot(NavGraph nav, Player p, Func<int, float> score)
    {
        if (nav.MainNodes.Length == 0)
            return RandomNode(nav, p);
        var best = -1;
        var bestScore = float.MinValue;
        for (var i = 0; i < 24; i++)
        {
            var n = nav.MainNodes[_rng.Next(nav.MainNodes.Length)];
            var s = score(n) + (float)_rng.NextDouble() * 2f;
            if (s > bestScore)
            {
                bestScore = s;
                best = n;
            }
        }
        return nav.Nodes[best];
    }

    /// <summary>
    /// A nearby spot where <paramref name="threatEye"/> can't see a crouching bot, preferring close ones that aren't
    /// towards the threat. At most a handful of ray casts.
    /// </summary>
    private bool BotFindCover(Player p, BotBrain b, Vector3 threatEye)
    {
        var nav = b.PathGraph;
        if (nav == null || nav.Nodes.Length == 0)
            return false;
        _botNear.Clear();
        nav.Within(p.Position, 14f, _botNear);
        var best = -1;
        var bestScore = float.MaxValue;
        var myThreatDist = Flat(threatEye - p.Position);
        var rays = 0;
        foreach (var i in _botNear)
        {
            var q = nav.Nodes[i];
            if (MathF.Abs(Rise(q - p.Position)) > 2f || Flat(threatEye - q) < myThreatDist - 4f)
                continue;
            var score = Flat(q - p.Position);
            if (score >= bestScore)
                continue;
            if (rays++ >= 14)
                break;
            if (!HasLineOfSight(threatEye, q + new Vector3(0, 1.0f, 0)))
            {
                best = i;
                bestScore = score;
            }
        }
        if (best < 0)
            return false;
        b.CoverSpot = nav.Nodes[best];
        return true;
    }
}
