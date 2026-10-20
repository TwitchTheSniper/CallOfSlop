using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// What bots do for the game mode (kept apart from the movement and aim code in Bots.cs). BotTick asks two things:
/// who is an enemy (<see cref="IsEnemy"/>), and, when nobody is in sight, where to head (<see cref="ObjectiveGoal"/>).
///   - King of the Hill: everyone heads for the hill, each to their own spot inside it.
///   - Capture the Flag: a carrier runs home; your dropped flag gets picked up; an enemy carrier gets chased;
///     otherwise even ids attack the enemy flag and odd ids guard their own (or escort a teammate carrying).
///   - Free-for-all, Team Deathmatch, Gun Game: no objective, normal roaming.
/// A goal behind walls is reached through the waypoint nearest to it that the bot can walk to in a straight line.
/// </summary>
public sealed partial class Game
{
    private readonly Dictionary<byte, (Vector3 Goal, Vector3 Via, double Until)> _botRoute = new();

    /// <summary>Should this bot treat player o as an enemy? (teams/friendly fire)</summary>
    private bool IsEnemy(Player bot, Player other) => AreEnemies(bot, other);

    /// <summary>Where this bot should be heading for the current mode's objective (hill, flag, carrier...), or null for normal roaming/fighting.</summary>
    private Vector3? ObjectiveGoal(Player bot, double now)
    {
        if (Phase != MatchPhase.Playing)
            return null;
        Vector3? goal = Mode switch
        {
            GameMode.KingOfTheHill => HillGoal(bot),
            GameMode.CaptureTheFlag => FlagGoal(bot),
            _ => null,
        };
        return goal is { } g ? Route(bot, g, now) : null;
    }

    private Vector3? HillGoal(Player bot)
    {
        if (ActiveHill is not { } hill)
            return null;
        // Spread out inside the zone instead of stacking on the centre.
        var a = bot.Id * 2.4f;
        return hill.Center + new Vector3(MathF.Cos(a), 0, MathF.Sin(a)) * hill.Radius * 0.45f;
    }

    private Vector3? FlagGoal(Player bot)
    {
        if (bot.Team > 1)
            return null;
        var own = bot.Team;
        var enemy = 1 - own;
        var home = Objectives.Flags[own];
        if (CarriedFlagOf(bot) >= 0)
            return home; // run it home (it scores once our flag is back)
        var mine = Flags[own];
        if (mine.Dropped)
            return mine.Position; // return it
        if (mine.Carried && Players.TryGetValue(mine.Carrier, out var thief))
            return thief.Position; // chase the thief
        var theirs = Flags[enemy];
        if (theirs.Carried && Players.TryGetValue(theirs.Carrier, out var buddy) && buddy != bot)
            return bot.Id % 2 == 0 ? buddy.Position : home; // escort or guard
        return bot.Id % 2 == 0 ? theirs.Position : home + new Vector3(MathF.Cos(bot.Id), 0, MathF.Sin(bot.Id)) * 3f;
    }

    /// <summary>
    /// Straight there when the way is clear; otherwise via the waypoint closest to the goal that can be walked to in a
    /// straight line (re-picked about once a second). Good enough for open routes; a proper path finder can replace it.
    /// </summary>
    private Vector3 Route(Player bot, Vector3 goal, double now)
    {
        var eye = new Vector3(0, 1.0f, 0);
        if (Clear(bot.Position + eye, goal + eye))
            return goal;
        if (_botRoute.TryGetValue(bot.Id, out var cached) && now < cached.Until && Vector3.DistanceSquared(cached.Goal, goal) < 9f
            && Vector3.DistanceSquared(bot.Position, cached.Via) > 4f)
            return cached.Via;
        var via = goal;
        var best = float.MaxValue;
        var checks = 0;
        foreach (var w in World.Map.Waypoints.OrderBy(w => Vector3.DistanceSquared(w, goal)))
        {
            if (checks++ > 24)
                break;
            var d = Vector3.Distance(w, goal);
            if (d >= best || Vector3.DistanceSquared(w, bot.Position) < 4f)
                continue;
            if (Clear(bot.Position + eye, w + eye))
            {
                best = d;
                via = w;
                break; // sorted by distance to the goal: the first clear one is the best
            }
        }
        if (best == float.MaxValue)
            via = World.Map.Waypoints.Count > 0 ? NearestClearWaypoint(bot.Position, goal) : goal;
        _botRoute[bot.Id] = (goal, via, now + 1.0);
        return via;
    }

    /// <summary>No waypoint near the goal is reachable: step to a reachable one that's at least closer to the goal.</summary>
    private Vector3 NearestClearWaypoint(Vector3 from, Vector3 goal)
    {
        var eye = new Vector3(0, 1.0f, 0);
        var here = Vector3.Distance(from, goal);
        var bestVia = goal;
        var best = float.MaxValue;
        var checks = 0;
        foreach (var w in World.Map.Waypoints.OrderBy(w => Vector3.DistanceSquared(w, from)))
        {
            if (checks++ > 24)
                break;
            var d = Vector3.Distance(w, goal);
            if (d < here && d < best && Vector3.DistanceSquared(w, from) > 4f && Clear(from + eye, w + eye))
            {
                best = d;
                bestVia = w;
            }
        }
        return bestVia;
    }

    private static bool Clear(Vector3 from, Vector3 to)
    {
        var d = to - from;
        var dist = d.Length();
        return dist < 0.01f || World.RaycastWorld(from, d / dist, dist) >= dist - 0.05f;
    }
}
