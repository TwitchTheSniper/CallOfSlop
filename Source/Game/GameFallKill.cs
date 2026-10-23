using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Maps over a void (the Space pack's sky station): a map file's top-level <c>"fallKill"</c> is a world height, and
/// anyone whose feet drop below it has fallen off into the clouds. The server kills them through the same path as
/// the pause menu's "Kill yourself" (a death, no kill for anyone, "Suicide" in the feed, the usual respawn), so it
/// needs no new packet. Maps without "fallKill" are unaffected.
/// </summary>
public sealed partial class Game
{
    /// <summary>Players the void has killed since this game started (self-test).</summary>
    public int FallKills { get; private set; }

    /// <summary>Players put back on a spawn because they fell off while nobody can be hurt (self-test).</summary>
    public int FallRescues { get; private set; }

    /// <summary>
    /// Server, every tick: kills anyone below the map's fall-kill height. Outside play (warm-up, the countdown, the results
    /// screen: nobody can be hurt) they're put back on their nearest spawn instead, so nobody is left alive under the map
    /// (they used to hang at the arena's floor clamp, 5 m under the world, until the next match).
    /// </summary>
    private void ServerTickFallKill(double now)
    {
        if (World.Map.FallKillY is not { } killY || Phase == MatchPhase.Lobby)
            return;
        foreach (var p in Players.Values)
        {
            if (!p.Alive || p.Position.Y >= killY)
                continue;
            if (Phase != MatchPhase.Playing)
            {
                FallRescue(p);
                continue;
            }
            p.ImmuneUntil = 0;
            p.LastStandUsed = true;
            if (DamagePlayer(p, p, 100000, false, SuicideKillId, now))
                FallKills++;
        }
    }

    private void FallRescue(Player p)
    {
        var spawns = SpawnPointsFor(p);
        if (spawns.Count == 0)
            return;
        var best = spawns[0];
        var bestD = float.MaxValue;
        foreach (var sp in spawns)
        {
            var d = Vector3.DistanceSquared(sp, p.Position);
            if (d < bestD)
                (best, bestD) = (sp, d);
        }
        p.Teleport(best);
        for (var i = 0; i < Player.HistorySize; i++)
            p.History[i] = new HistoryEntry { Position = best, Yaw = p.Yaw };
        FallRescues++;
    }

    /// <summary>Self-test: runs the fall-kill check once.</summary>
    public void SelfTestTickFallKill(double now) => ServerTickFallKill(now);
}

public sealed partial class Map
{
    /// <summary>
    /// The map file's top-level "fallKill": players whose feet drop below this world height die (they fell off into the
    /// void). Null (the default): nobody ever falls out. A map with it is a void map: bot nav nodes need floor under
    /// a whole footprint (no nodes on railing tops).
    /// </summary>
    public float? FallKillY;
}
