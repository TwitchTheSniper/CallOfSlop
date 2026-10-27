using System.Numerics;

namespace CallOfSlop;

/// <summary>Why a bot was judged hopelessly stuck (for the <c>[bots] stuck:</c> log line, so map problems can be found).</summary>
public enum BotStuckCause : byte
{
    None,
    Blocked, // pushing into geometry on the way to the next nav node
    Ledge, // the path climbs a ledge here and the climb never happened
    Slope, // walking up a slope and sliding back down it
    NoRoute, // no path from where it stands to its goal (steering straight at the goal into a wall)
    Trap, // on nav nodes that can be dropped into but have no way back out (a pit; TerrainVoids)
    OffNav, // no nav node anywhere near (outside the walkable area: on top of something, in a void)
    Void, // fell out of the map (well below the lowest nav node)
}

/// <summary>The long-horizon stuck watch's state (see Game.BotStuckWatch in BotStuck.cs).</summary>
public sealed partial class BotBrain
{
    /// <summary>Seconds this bot has wanted to move without making progress (only counted while it could be stuck).</summary>
    public float StuckSecs;

    /// <summary>Recovery stage reached in the current stuck spell: 0 none, 1 backed off, 2 re-planned, 3 backed off again.</summary>
    public byte StuckStage;

    /// <summary>Seconds in a row this bot has been off the walkable nav area (a pit, a void, outside the graph).</summary>
    public float OffNavSecs;

    public BotStuckCause OffNavCause;
    internal Vector3 OffNavAnchor; // where the current off-nav spell started
    internal Vector3 StuckAnchor; // where the current stuck spell started (moving well away from it is progress)
    internal int StuckAnchorNode = -1; // the path node it was walking to then, and how far away it was
    internal float StuckAnchorNodeDist;
    internal uint StuckWatchTick = uint.MaxValue; // the server tick of the last watch (a gap = dead, stunned or a dummy: start over)
    internal uint StuckWatchLife = uint.MaxValue; // the life the watch's state belongs to
    internal double BackOffUntil; // stage 1/3: step back (and aside) from what we're pushing into until then
    internal Vector3 BackOffDir;
    internal double RescueUntil; // stage 2: walk straight at the nearest nav node until then
    internal Vector3 RescueAt;
    internal double NextOffNavCheck;
    internal double LastStuckLog = -1000; // the log line is written at most once a minute per bot

    internal StuckSpot? StuckSpellSpot; // the stuck-spot report's spot for the current spell (from recovery stage 1 on; StuckSpots.cs)
    internal BotStuckCause StuckSpellCause; // the cause as the spell reached stage 1

    /// <summary>What the last watch step made of this tick (self-test diagnostics; see <see cref="StuckWhy"/>).</summary>
    public StuckWhy StuckLastWhy;
}

/// <summary>What the stuck watch made of one tick (self-test diagnostics: why a stuck clock didn't run).</summary>
public enum StuckWhy : byte
{
    Counting, // the clock ran (or paused for a fight) this tick
    NotPlaying, // a phase other than Playing
    Idle, // not wanting to move (no wish, or stationary on purpose)
    Mover, // on a lift or another mover's sweep
    Holding, // holding a spot on purpose (BotHolding)
    Progress, // made progress: the spell started over
    PausedSight, // an enemy in sight: the clock paused
    PausedEngage, // fighting (Engage mode)
    PausedBlind, // flashed
    PausedMantle, // climbing a ledge
}

/// <summary>
/// Bots that can't move: the long-horizon stuck watch on top of BotUnstick's quick fixes (BotMove.cs). A bot that wants
/// to move (it has a goal and is pushing towards it) but hasn't got anywhere escalates:
///   - after 20% of <see cref="BotStuckSuicideSeconds"/> (3 s by default): jump and back off at an angle for a moment;
///   - after 40% (6 s): the edge it's on is avoided for 30 s, the path and goal are thrown away, and it walks straight
///     at the nearest nav node it can see before planning again;
///   - after 60% (9 s): another back-off, the other way;
///   - after <see cref="BotStuckSuicideSeconds"/> (15 s): it kills itself through the same path as the pause menu's
///     "Kill yourself" (<see cref="ServerSuicide"/>: a suicide, no kill credit for anyone, normal respawn).
/// A bot off the walkable nav area (fell into a pit with no way out, a void, or outside the nav graph) for
/// <see cref="BotOffNavSeconds"/> goes straight to the self-kill. Standing still on purpose never counts: holding a
/// spot (marksmen, anchors), the hill or another objective, squad holds and suppression, fights (an enemy in sight),
/// flashes and stuns, Zombies downs, a mover's sweep (lifts), and every phase but Playing.
/// Modes without respawns can't use a suicide: Last Man Standing teleports the bot to the nearest nav node of the main
/// area instead, and Zombies co-op bots (which don't use the nav graph) teleport to the nearest standing teammate, or to
/// the spot they started the life at (ZombieBotStuck, called from ZombieBots.cs).
/// Each self-kill is logged as <c>[bots] stuck: ...</c> (map, position, cause; at most once a minute per bot) and
/// counted per map; the end of every match logs the counts.
/// </summary>
public sealed partial class Game
{
    /// <summary>Seconds a bot may be stuck before it kills itself (the recovery stages scale with it). Tunable per game.</summary>
    public float BotStuckSuicideSeconds = 15f;

    /// <summary>Seconds a bot may be off the walkable nav area (pit, void, no nav node near) before it kills itself.</summary>
    public float BotOffNavSeconds = 4f;

    private const float StuckProgress = 0.6f; // moving this far from the stuck spot is progress...
    private const float StuckProgressRecovering = 4f; // ...or this far once a recovery started (backing off isn't progress)
    private const float StuckNodeProgress = 0.5f; // getting this much closer to the same path node is progress too
    private const double StuckLogEvery = 60; // seconds between log lines per bot

    /// <summary>Stuck self-kills (or rescues, in modes without respawns) this session (server; read by the self-test).</summary>
    public int BotStuckKills;

    /// <summary>Of those, how many were rescue teleports (Last Man Standing, Zombies) instead of a suicide.</summary>
    public int BotStuckRescues;

    /// <summary>Stuck self-kills since the last end-of-match line.</summary>
    public int BotStuckKillsThisMatch;

    /// <summary>The last <c>[bots] stuck:</c> line written (self-test).</summary>
    public string LastStuckLog = "";

    private readonly Dictionary<string, int> _stuckKillsByMap = new();

    /// <summary>Stuck self-kills on <paramref name="mapName"/> this session.</summary>
    public int StuckKillsOn(string mapName) => _stuckKillsByMap.GetValueOrDefault(mapName);

    /// <summary>
    /// Server, every bot tick after steering: the stuck watch and its recovery stages (may change the intent). Returns
    /// true when the bot just killed itself (the caller stops the tick there).
    /// </summary>
    private bool BotStuckWatch(Player p, BotBrain b, ref BotIntent s, bool visible, double now)
    {
        var pos = p.Position;
        if (b.StuckWatchLife != p.SpawnTick || b.StuckWatchTick + 1 != ServerTick || Phase != MatchPhase.Playing)
        {
            b.StuckWatchLife = p.SpawnTick;
            StuckReset(b, pos);
            b.OffNavSecs = 0;
            b.NextOffNavCheck = now + 0.5;
        }
        b.StuckWatchTick = ServerTick;
        if (Phase != MatchPhase.Playing)
        {
            b.StuckLastWhy = StuckWhy.NotPlaying;
            return false;
        }
        var nav = b.PathGraph ?? NavGraph.Current;
        var onMover = InMoverSweep(pos);

        // Standing still on purpose, or something else holds the bot: not stuck.
        var wants = s.Wish.LengthSquared() >= 0.05f && !s.Stationary;
        var holding = BotHolding(p, b, now);
        var idle = !wants || onMover || holding;
        b.StuckLastWhy = !wants ? StuckWhy.Idle : onMover ? StuckWhy.Mover : holding ? StuckWhy.Holding : StuckWhy.Counting;

        // Off the walkable area: checked twice a second, straight to the self-kill after a few seconds of wanting out
        // and staying within a few metres (a bot running about isn't in a pit; falling out of the map always counts).
        if (now >= b.NextOffNavCheck)
        {
            b.NextOffNavCheck = now + 0.5;
            var cause = onMover ? BotStuckCause.None : OffNavCause(nav, b, pos);
            if (cause == BotStuckCause.None || (idle && cause != BotStuckCause.Void))
                b.OffNavSecs = 0;
            else
            {
                if (b.OffNavSecs == 0 || (cause != BotStuckCause.Void && Vector3.DistanceSquared(pos, b.OffNavAnchor) > StuckProgressRecovering * StuckProgressRecovering))
                {
                    b.OffNavAnchor = pos;
                    b.OffNavSecs = 0;
                }
                b.OffNavSecs += 0.5f;
                b.OffNavCause = cause;
                if (b.OffNavSecs >= BotOffNavSeconds)
                    return StuckGiveUp(p, b, nav, cause, b.OffNavSecs, now);
            }
        }
        if (idle)
        {
            StuckReset(b, pos);
            return false;
        }

        // Progress: away from where the spell started, or clearly closer to the same path node. Once a recovery has
        // started only a real move away counts (backing off and walking back into the same corner isn't progress).
        var following = now - b.FollowTime < 0.05 && b.FollowNode >= 0;
        var node = following ? b.FollowNode : -1;
        var nodeDist = following ? Flat(b.FollowAim - pos) : 0f;
        // Across the ground only: a hop in place (BotUnstick jumps at a wall every couple of seconds) rises 1.5 m and lands
        // again, which isn't getting anywhere. Counting it reset the clock on every hop, so a boxed-in bot that happened to
        // hop more often than once a second never reached a recovery stage (the Windows "Bot stuck watch" failure).
        var moved = FlatVec(pos - b.StuckAnchor).LengthSquared();
        var progressed = b.StuckStage > 0
            ? moved >= StuckProgressRecovering * StuckProgressRecovering
            : node >= 0 && node == b.StuckAnchorNode
                ? b.StuckAnchorNodeDist - nodeDist >= StuckNodeProgress || moved >= 3f * 3f
                : moved >= StuckProgress * StuckProgress;
        if (progressed && now >= b.BackOffUntil)
        {
            b.StuckLastWhy = StuckWhy.Progress;
            StuckReset(b, pos);
            b.StuckAnchorNode = node;
            b.StuckAnchorNodeDist = nodeDist;
            return false;
        }
        if (b.StuckAnchorNode < 0 && node >= 0)
        {
            b.StuckAnchorNode = node;
            b.StuckAnchorNodeDist = nodeDist;
        }

        // In a fight, flashed or climbing: the clock pauses (strafing at an enemy isn't being stuck).
        var paused = visible || b.Mode == BotMode.Engage || BotBlinded(p, now) || p.Sim.MantleTicks > 0;
        if (!paused)
            b.StuckSecs += World.TickDt;
        else
            b.StuckLastWhy = visible ? StuckWhy.PausedSight : b.Mode == BotMode.Engage ? StuckWhy.PausedEngage : p.Sim.MantleTicks > 0 ? StuckWhy.PausedMantle : StuckWhy.PausedBlind;

        var limit = MathF.Max(1f, BotStuckSuicideSeconds);
        if (b.StuckSecs >= limit)
            return StuckGiveUp(p, b, nav, StuckCauseHere(b, nav, now - b.StuckSecs), b.StuckSecs, now);
        if (b.StuckStage == 0 && b.StuckSecs >= limit * 0.2f)
        {
            b.StuckStage = 1;
            StuckSpellStarts(b, nav, pos, StuckCauseHere(b, nav, now - b.StuckSecs));
            StuckBackOff(b, s.Wish, now);
        }
        else if (b.StuckStage == 1 && b.StuckSecs >= limit * 0.4f)
        {
            b.StuckStage = 2;
            StuckSpellStage(b);
            StuckReplan(p, b, nav, now);
        }
        else if (b.StuckStage == 2 && b.StuckSecs >= limit * 0.6f)
        {
            b.StuckStage = 3;
            StuckSpellStage(b);
            b.StrafeDir = -b.StrafeDir;
            StuckBackOff(b, s.Wish, now);
        }

        // The recovery moves override the steering while they last.
        if (now < b.BackOffUntil)
        {
            s.Wish = b.BackOffDir;
            s.Sprint = false;
            s.Mantle = false;
        }
        else if (now < b.RescueUntil)
        {
            var to = FlatVec(b.RescueAt - pos);
            var len = to.Length();
            if (len < 0.6f)
                b.RescueUntil = 0;
            else
            {
                s.Wish = to / len;
                s.Sprint = false;
            }
        }
        return false;
    }

    private static void StuckReset(BotBrain b, Vector3 pos)
    {
        b.StuckSecs = 0;
        b.StuckStage = 0;
        b.StuckAnchor = pos;
        b.StuckAnchorNode = -1;
        b.BackOffUntil = 0;
        b.RescueUntil = 0;
        b.StuckSpellSpot = null;
    }

    /// <summary>Spots stuck spells reached this match (Map contract: <c>maxStuckSpots</c>; reset by the end-of-match summary).</summary>
    public readonly List<StuckSpot> StuckSpotsThisMatch = new(32);

    /// <summary>Stuck spells (recovery stage 1 or a give-up) this match.</summary>
    public int StuckSpellsThisMatch;

    /// <summary>A stuck spell reached recovery stage 1 (or gave up at once): the stuck-spot report (StuckSpots.cs) counts it at its spot.</summary>
    private void StuckSpellStarts(BotBrain b, NavGraph nav, Vector3 pos, BotStuckCause cause)
    {
        if (!IsServer)
            return;
        var node = nav.Nodes.Length > 0 ? nav.Nearest(pos, needSight: false) : -1;
        var spot = StuckSpots.Session.Spell(World.Map.Name, Module.Name, pos, cause, node);
        b.StuckSpellSpot = spot;
        b.StuckSpellCause = cause;
        StuckSpellsThisMatch++;
        if (!StuckSpotsThisMatch.Contains(spot))
            StuckSpotsThisMatch.Add(spot);
    }

    private static void StuckSpellStage(BotBrain b)
    {
        if (b.StuckSpellSpot is { } spot)
            StuckSpots.Session.Stage(spot, b.StuckStage);
    }
    /// <summary>Holding a spot on purpose: a vantage point, a marksman's or anchor's perch, the objective (the hill), a squad hold or suppression.</summary>
    private static bool BotHolding(Player p, BotBrain b, double now)
    {
        var pos = p.Position;
        if (b.IsHoldingPosition && b.Mode == BotMode.Overwatch)
            return true; // holding a vantage point (BotVantage.cs)
        if (b.HoldUntil > now && Flat(b.HoldAnchor - pos) < 5f)
            return true;
        if (b.Mode == BotMode.Objective && b.Objective is { } o && Flat(o - pos) < 5f)
            return true;
        return b.Mode == BotMode.Squad && b.Order.Kind is SquadOrderKind.Hold or SquadOrderKind.Suppress && Flat(b.Order.Goal - pos) < 4f;
    }

    /// <summary>Stage 1 and 3: hop, and step back from the way we were pushing, angled to the strafe side, for 0.7 s.</summary>
    private static void StuckBackOff(BotBrain b, Vector3 wish, double now)
    {
        var w = FlatVec(wish);
        w = w.LengthSquared() > 1e-4f ? Vector3.Normalize(w) : GravityFrames.Curved ? FlatVec(Vector3.UnitZ + Vector3.UnitX * 0.3f) : Vector3.UnitZ;
        var side = Across(w) * b.StrafeDir;
        b.BackOffDir = Vector3.Normalize(-w + side);
        b.BackOffUntil = now + 0.7;
        b.JumpNext = true;
    }

    /// <summary>Stage 2: avoid the edge we're stuck on for 30 s, drop the path and the goal, head for the nearest nav node first.</summary>
    private void StuckReplan(Player p, BotBrain b, NavGraph nav, double now)
    {
        if (b.CurEdge >= 0)
        {
            b.BlockedEdge = b.CurEdge;
            b.BlockedUntil = now + 30;
        }
        b.Waypoint = Vector3.Zero;
        b.GoalUntil = 0;
        b.HoldUntil = 0;
        b.LastSeenTime = -100;
        b.HeardTime = -100;
        b.ClearPath();
        b.NextPathTime = now + 1.2; // plan again once it stands on the graph
        b.JumpNext = true;
        var n = nav.Nodes.Length > 0 ? nav.Nearest(p.Position) : -1;
        if (n >= 0 && Flat(nav.Nodes[n] - p.Position) > 0.6f)
        {
            b.RescueAt = nav.Nodes[n];
            b.RescueUntil = now + 1.2;
        }
    }

    /// <summary>
    /// Is the bot off the walkable area? None when it's fine (or the map's graph is still being built). Trap nodes only
    /// count while the bot has no route out (the nav's trap flags are a hint; a route A* found says otherwise).
    /// </summary>
    private static BotStuckCause OffNavCause(NavGraph nav, BotBrain b, Vector3 pos)
    {
        if (nav.Nodes.Length == 0)
            return BotStuckCause.None;
        if (pos.Y < nav.MinNodeY - 10f)
            return BotStuckCause.Void;
        var n = nav.Nearest(pos, needSight: false);
        if (n < 0)
            return BotStuckCause.OffNav;
        return nav.IsTrapped(n) && b.PathLen == 0 ? BotStuckCause.Trap : BotStuckCause.None;
    }

    /// <summary>The best guess at what's holding the bot, for the log.</summary>
    private static BotStuckCause StuckCauseHere(BotBrain b, NavGraph nav, double since)
    {
        if (b.CurEdge >= 0 && (nav.EdgeFlags[b.CurEdge] & NavGraph.FlagMantle) != 0)
            return BotStuckCause.Ledge;
        if (nav.Nodes.Length > 0 && b.PathLen == 0 && !b.NeedPath)
            return BotStuckCause.NoRoute;
        if (World.Map.HasTerrain && b.SlopeStallAt >= since) // a slide back down a slope during this stuck spell (not some earlier one)
            return BotStuckCause.Slope;
        return BotStuckCause.Blocked;
    }

    private static bool InMoverSweep(Vector3 pos)
    {
        foreach (ref readonly var box in MoverPhysics.Sweeps)
        {
            var lo = box.Min;
            var hi = box.Max;
            if (pos.X >= lo.X && pos.X <= hi.X && pos.Z >= lo.Z && pos.Z <= hi.Z && pos.Y >= lo.Y - 1f)
                return true;
        }
        return false;
    }

    /// <summary>
    /// The last resort: the pause menu's "Kill yourself" for the bot (a suicide; it respawns normally). In a mode with
    /// no respawns (Last Man Standing) it's teleported to the nearest main-area nav node instead. True if it died.
    /// </summary>
    private bool StuckGiveUp(Player p, BotBrain b, NavGraph nav, BotStuckCause cause, float secs, double now)
    {
        var at = p.Position;
        if (b.StuckSpellSpot == null)
            StuckSpellStarts(b, nav, at, cause); // off the nav area: straight to the last resort, no stages
        var spot = b.StuckSpellSpot;
        var spellCause = b.StuckSpellCause;
        StuckReset(b, at);
        b.OffNavSecs = 0;
        b.ClearPath();
        b.Waypoint = Vector3.Zero;
        if (!Module.CanRespawn(this, p, now))
        {
            if (NearestMainNode(nav, at) is not { } to)
                return false;
            StuckTeleport(p, to);
            if (spot != null)
                StuckSpots.Session.GaveUp(spot, cause, spellCause, rescue: true);
            StuckNote(p, b, cause, at, secs, "teleported to the nearest nav node (no respawns in this mode)", now, rescue: true);
            return false;
        }
        var died = ServerSuicide(p, now);
        if (died)
        {
            if (spot != null)
                StuckSpots.Session.GaveUp(spot, cause, spellCause, rescue: false);
            StuckNote(p, b, cause, at, secs, "self-kill", now, rescue: false);
        }
        return died;
    }

    private static Vector3? NearestMainNode(NavGraph nav, Vector3 from)
    {
        var best = -1;
        var bestD = float.MaxValue;
        foreach (var i in nav.MainNodes)
        {
            var d = Vector3.DistanceSquared(nav.Nodes[i], from);
            if (d > 2f * 2f && d < bestD)
                (best, bestD) = (i, d);
        }
        return best >= 0 ? nav.Nodes[best] : null;
    }

    private static void StuckTeleport(Player p, Vector3 feet)
    {
        p.Teleport(feet);
        for (var i = 0; i < Player.HistorySize; i++)
            p.History[i] = new HistoryEntry { Position = feet, Yaw = p.Yaw };
    }

    /// <summary>Counts a stuck self-kill or rescue and writes the log line (at most once a minute per bot).</summary>
    private void StuckNote(Player p, BotBrain b, BotStuckCause cause, Vector3 at, float secs, string action, double now, bool rescue)
    {
        BotStuckKills++;
        BotStuckKillsThisMatch++;
        if (rescue)
            BotStuckRescues++;
        var map = World.Map.Name;
        var count = _stuckKillsByMap.GetValueOrDefault(map) + 1;
        _stuckKillsByMap[map] = count;
        if (now - b.LastStuckLog < StuckLogEvery)
            return;
        b.LastStuckLog = now;
        LastStuckLog = $"[bots] stuck: {p.Name} on {map} ({Module.Name}) at ({at.X:0.0}, {at.Y:0.0}, {at.Z:0.0}): {StuckCauseText(cause)} for {secs:0.0} s -> {action} ({count} on this map this session)";
        Console.WriteLine(LastStuckLog);
    }

    internal static string StuckCauseText(BotStuckCause c) =>
        c switch
        {
            BotStuckCause.Ledge => "blocked at a ledge climb (mantle edge)",
            BotStuckCause.Slope => "sliding back down a slope",
            BotStuckCause.NoRoute => "no route to its goal from here",
            BotStuckCause.Trap => "in a pit with no way out (trap nav nodes)",
            BotStuckCause.OffNav => "off the nav graph (no walkable node near)",
            BotStuckCause.Void => "fell out of the map",
            _ => "blocked by geometry on its path",
        };

    /// <summary>End of every match (server): the stuck self-kills of this match and of this map so far, for the log.</summary>
    private void LogStuckMatchSummary()
    {
        if (!IsServer)
            return;
        var bots = 0;
        foreach (var p in Players.Values)
            if (p.Bot != null)
                bots++;
        if (bots == 0)
            return;
        var map = World.Map.Name;
        Console.WriteLine($"[bots] match over on {map}: {BotStuckKillsThisMatch} stuck self-kill(s) this match, {StuckKillsOn(map)} on this map this session; "
                          + $"{StuckSpellsThisMatch} stuck spell(s) at {StuckSpotsThisMatch.Count} spot(s) this match");
        // The map's stuck spots this session (the stuck-spot report, StuckSpots.cs), worst first.
        var spots = StuckSpots.Session.On(map);
        if (spots.Count > 0)
        {
            Console.WriteLine($"[bots] stuck spots on {map} this session: {spots.Count}");
            foreach (var spot in spots.OrderByDescending(x => x.Spells + x.SelfKills).Take(5))
                Console.WriteLine("[bots]   " + StuckSpots.Describe(spot));
        }
        StuckSpots.Session.WriteSessionInBackground();
        BotStuckKillsThisMatch = 0;
        StuckSpellsThisMatch = 0;
        StuckSpotsThisMatch.Clear();
    }

    // ---- Zombies co-op bots (ZombieBots.cs) ----------------------------------------------------------------

    /// <summary>
    /// Zombies co-op bots steer straight at their goals (no nav graph), and the mode has no respawn (a death is back next
    /// round), so a stuck one hops and strafes, and after <see cref="BotStuckSuicideSeconds"/> teleports to the nearest
    /// standing teammate (or back to where it started this life). <paramref name="buttons"/> gets the hop and strafe.
    /// </summary>
    internal void ZombieBotStuck(Player p, BotBrain b, bool wantsMove, ref Buttons buttons, double now)
    {
        var pos = p.Position;
        if (b.StuckWatchLife != p.SpawnTick || b.StuckWatchTick + 1 != ServerTick || Phase != MatchPhase.Playing)
        {
            b.StuckWatchLife = p.SpawnTick;
            StuckReset(b, pos);
        }
        b.StuckWatchTick = ServerTick;
        if (Phase != MatchPhase.Playing || !wantsMove || p.Sim.MantleTicks > 0 || InMoverSweep(pos))
        {
            StuckReset(b, pos);
            return;
        }
        var radius = b.StuckStage == 0 ? StuckProgress : StuckProgressRecovering;
        if (FlatVec(pos - b.StuckAnchor).LengthSquared() >= radius * radius && now >= b.BackOffUntil) // (across the ground: a hop isn't progress)
        {
            StuckReset(b, pos);
            return;
        }
        b.StuckSecs += World.TickDt;
        var limit = MathF.Max(1f, BotStuckSuicideSeconds);
        if (b.StuckSecs >= limit)
        {
            Player? best = null;
            var bestD = float.MaxValue;
            foreach (var o in Players.Values)
            {
                if (o == p || !IsStanding(o))
                    continue;
                var d = Vector3.DistanceSquared(o.Position, pos);
                if (d > 1f && d < bestD)
                    (best, bestD) = (o, d);
            }
            var to = best?.Position ?? b.HoldAnchor;
            var secs = b.StuckSecs;
            var zspot = b.StuckSpellSpot;
            StuckReset(b, pos);
            if (Vector3.DistanceSquared(to, pos) < 1f)
                return;
            StuckTeleport(p, to); // (players don't block each other: right where the teammate stands is fine)
            if (zspot != null)
                StuckSpots.Session.GaveUp(zspot, BotStuckCause.Blocked, BotStuckCause.Blocked, rescue: true);
            StuckNote(p, b, BotStuckCause.Blocked, pos, secs, best != null ? $"teleported to {best.Name}" : "teleported back to its start spot", now, rescue: true);
            return;
        }
        if (b.StuckStage < 3 && b.StuckSecs >= limit * (0.2f + 0.2f * b.StuckStage))
        {
            b.StuckStage++;
            if (b.StuckStage == 1)
                StuckSpellStarts(b, NavGraph.Current, pos, BotStuckCause.Blocked);
            else
                StuckSpellStage(b);
            b.StrafeDir = -b.StrafeDir;
            b.BackOffUntil = now + 0.7;
        }
        if (now < b.BackOffUntil)
        {
            buttons &= ~(Buttons.Forward | Buttons.Left | Buttons.Right);
            buttons |= Buttons.Back | (b.StrafeDir > 0 ? Buttons.Right : Buttons.Left);
            if (now + 0.65 >= b.BackOffUntil)
                buttons |= Buttons.Jump;
        }
    }

    // ---- Self-test hooks -------------------------------------------------------------------------

    /// <summary>Self-test: one stuck-watch step for a bot with the given intent (true = it killed itself).</summary>
    public bool SelfTestStuckWatch(Player p, Vector3 wish, bool stationary, bool visible, double now)
    {
        var s = new BotIntent { Wish = wish, Stationary = stationary };
        return BotStuckWatch(p, p.Bot!, ref s, visible, now);
    }

    /// <summary>Self-test: one Zombies co-op stuck-watch step (returns the buttons after the recovery moves).</summary>
    public Buttons SelfTestZombieStuck(Player p, bool wantsMove, Buttons buttons, double now)
    {
        ZombieBotStuck(p, p.Bot!, wantsMove, ref buttons, now);
        return buttons;
    }
}
