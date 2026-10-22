using System.Numerics;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>What a mode needs from a map, so the lobby and the map vote can tell which modes a map plays.</summary>
[Flags]
public enum MapNeeds : byte
{
    None = 0,
    TeamSpawns = 1, // two spawn groups (every map has them: generated when not hand-placed)
    Hills = 2, // hill zones (generated when not hand-placed)
    Flags = 4, // two flag stands (generated when not hand-placed)
    ZombiesSection = 8, // a "zombies" section in the map file (only maps made for Zombies)
}

/// <summary>
/// One game mode: its identity, its rules (capability flags), and the hooks the game, the bots, the network and the HUD
/// call while it is being played. One instance per <see cref="Game"/> per registered mode (<see cref="GameModeRegistry"/>
/// creates them), so a module keeps its own match state in plain fields (the hill, the flags...) and writes it to the
/// mode-state packet itself (<see cref="WriteState"/> / <see cref="ReadState"/>).
///
/// Everything a mode does NOT override keeps the default: free-for-all rules with loadouts, kill streaks, perks and a
/// score limit, no teams, no objectives. Source/Game/Modes/README.md walks through adding a mode; LastManStandingModule
/// is the smallest complete example.
///
/// Which hooks run where:
///   - the "reset" hooks (<see cref="OnReset"/>, <see cref="OnMapChanged"/>, <see cref="OnClientMatchStart"/>) run for
///     EVERY module at every new match, active or not, so stale state never leaks into the next match in another mode;
///   - every other hook runs for the active module only (<see cref="Game.Module"/>).
/// </summary>
public abstract class GameModeModule
{
    // ---- Identity ---------------------------------------------------------------------------------------

    /// <summary>The wire id (one byte in packets, the server info and settings.txt). Never renumber a shipped mode.</summary>
    public abstract GameMode Id { get; }

    /// <summary>Full name for menus and the scoreboard ("Capture the Flag"). ASCII.</summary>
    public abstract string Name { get; }

    /// <summary>Short tag for tight spaces, 3-4 capitals ("CTF").</summary>
    public abstract string Short { get; }

    /// <summary>One line of rules for the lobby and the countdown. <paramref name="limit"/>: this match's score limit (0: the classic one).</summary>
    public abstract string Rules(int limit = 0);

    /// <summary>A short description for the README-style listings (the rules by default).</summary>
    public virtual string Description => Rules();

    // ---- Capabilities -------------------------------------------------------------------------------------

    /// <summary>Two teams (Blue and Red): team spawns, team colours, autobalance, the TEAMS window, team scores.</summary>
    public virtual bool TeamBased => false;

    /// <summary>Everyone is on one side (co-op): no friendly damage, friendly colours for all, no kill notices, "GAME OVER" instead of a winner.</summary>
    public virtual bool CoOp => false;

    /// <summary>The loadout screen applies (false: players spawn straight in with what the mode hands out in <see cref="OnSpawn"/>).</summary>
    public virtual bool UsesLoadout => true;

    /// <summary>Kill streaks can be earned.</summary>
    public virtual bool AllowsStreaks => true;

    /// <summary>Everyone carries this perk set instead of their own (null: the loadout's perks).</summary>
    public virtual PerkSet? FixedPerks => null;

    /// <summary>Grenades and equipment from the loadout apply (false: none are handed out, on the server and the local client).</summary>
    public virtual bool UsesThrowables => true;

    /// <summary>The server decides everyone's weapons (<see cref="OnSpawn"/>): a loadout request never swaps the guns in hand.</summary>
    public virtual bool ServerPicksWeapons => false;

    /// <summary>After a match, players vote on the next map (false: the match restarts on the same map).</summary>
    public virtual bool UsesMapVote => true;

    /// <summary>The 10-minute clock applies ("... minutes max" in the goal text).</summary>
    public virtual bool HasTimeLimit => true;

    /// <summary>How the score limit scales with the player count (ScoreLimits.cs), or null for a mode without a score limit.</summary>
    public virtual ScoreLimits.Rule? ScoreRule => null;

    /// <summary>What the score counts ("kills", "points", "captures").</summary>
    public virtual string ScoreUnit => "kills";

    /// <summary>What the mode needs from a map (<see cref="Fits"/> checks it).</summary>
    public virtual MapNeeds Needs => MapNeeds.None;

    /// <summary>Listed in the lobby only for maps of a dev pack (a pack kept out of True Random): work-in-progress and template modes.</summary>
    public virtual bool DevOnly => false;

    /// <summary>Dead players see "Respawning in..." / "Press L to pick your loadout" (false: the mode says it itself).</summary>
    public virtual bool ShowsRespawnMessage => true;

    /// <summary>The local player's kill notices ("You killed X") are shown.</summary>
    public virtual bool ShowsKillNotices => true;

    /// <summary>Can <paramref name="map"/> play this mode? (null: a random map, so only modes every map plays.)</summary>
    public virtual bool Fits(Map? map)
    {
        if ((Needs & MapNeeds.ZombiesSection) != 0 && map?.Zombies == null)
            return false;
        return true; // team spawns, hills and flags are generated for every map
    }

    /// <summary>A line for the loadout screen's footer when the mode makes the loadout moot (null: the normal hints).</summary>
    public virtual string? LoadoutHint => null;

    /// <summary>The lobby's warning when the picked map can't play this mode (it falls back to free-for-all).</summary>
    public virtual string MapWarning => $"{Name} needs a map made for it, or it plays Free-for-all.";

    /// <summary>The most players (people + bots) on this map in this mode.</summary>
    public virtual int MaxPlayers(Map map) => map.MaxPlayers;

    // ---- Lifecycle (server) -------------------------------------------------------------------------------

    /// <summary>A new match (or a mode change) begins: clear this mode's state. Runs for every module, active or not.</summary>
    public virtual void OnReset(Game game)
    {
    }

    /// <summary>The map changed under a running game (hot swap): objectives come from the new map. Runs for every module.</summary>
    public virtual void OnMapChanged(Game game)
    {
    }

    /// <summary>Client: a match starts (the server's MatchStart arrived). Runs for every module.</summary>
    public virtual void OnClientMatchStart(Game game)
    {
    }

    /// <summary>Someone (re)spawns: hand out the mode's weapons, perks, position (after the normal spawn and loadout).</summary>
    public virtual void OnSpawn(Game game, Player p)
    {
    }

    /// <summary>A player left the match (server).</summary>
    public virtual void OnPlayerLeave(Game game, Player p)
    {
    }

    /// <summary>
    /// A kill was counted and announced (attacker == victim: their own grenade). <paramref name="killId"/>: the weapon id or
    /// <see cref="Game.MeleeKillId"/>. Score it for the mode.
    /// </summary>
    public virtual void OnKill(Game game, Player attacker, Player victim, byte killId, double now)
    {
    }

    /// <summary>May a dead player (or bot) respawn now? (false: they wait; the mode decides when the match ends.)</summary>
    public virtual bool CanRespawn(Game game, Player p, double now) => true;

    /// <summary>
    /// Every server tick while playing: run the objectives, then the win check and the time limit. The default is the
    /// free-for-all rule (<see cref="FreeForAllWinCheck"/>); team modes call <see cref="TeamWinCheck"/>.
    /// </summary>
    public virtual void ServerTickPlaying(Game game, double now) => FreeForAllWinCheck(game, now);

    /// <summary>A packet with an id this mode owns (dispatched by Game.OnPacket). True: handled.</summary>
    public virtual bool HandlePacket(Game game, byte id, ref PacketReader r, RakNetGUID sender, double now) => false;

    // ---- Bots -------------------------------------------------------------------------------------------

    /// <summary>Take over this bot's whole tick (true), or let the normal deathmatch brain run (false).</summary>
    public virtual bool BotTick(Game game, Player bot, BotBrain brain, double now) => false;

    /// <summary>Where a bot with nobody in sight should head for the objective (null: roam). Routing is done for you.</summary>
    public virtual Vector3? BotObjective(Game game, Player bot) => null;

    // ---- Replicated state ---------------------------------------------------------------------------------

    /// <summary>
    /// The version of this mode's tail of the mode-state packet. Bump it when <see cref="WriteState"/> changes; a client
    /// with another version skips the tail instead of misreading it.
    /// </summary>
    public virtual byte StateVersion => 1;

    /// <summary>Server, ~10 times a second: this mode's state after the shared part (teams, scores, ready flags, gun levels).</summary>
    public virtual void WriteState(Game game, PacketWriter w)
    {
    }

    /// <summary>Client: exactly what <see cref="WriteState"/> wrote (the reader holds just this mode's tail).</summary>
    public virtual void ReadState(Game game, ref PacketReader r)
    {
    }

    // ---- HUD (client) -------------------------------------------------------------------------------------

    /// <summary>The goal of the mode in one line for the match log and the warmup bar ("First to 11 kills...").</summary>
    public virtual string GoalText(Game game) =>
        $"{Name}: {Rules(game.ScoreLimit)}{(HasTimeLimit ? $" {Game.MatchSeconds / 60:0} minutes max." : "")}";

    /// <summary>
    /// Objective lines under the match bar while playing (coloured text), and a world position to point an arrow at
    /// (null: no arrow). Nothing added: no panel.
    /// </summary>
    public virtual void HudLines(Game game, Player me, List<HudLine> lines, ref Vector3? pointAt)
    {
    }

    /// <summary>For modes without teams: the big text and the line under it in the match bar (null: the plain free-for-all bar).</summary>
    public virtual (string Big, string Sub)? ScoreBar(Game game) => null;

    /// <summary>The two number columns of the scoreboard.</summary>
    public virtual (string First, string Second) ScoreboardColumns => ("Kills", "Deaths");

    /// <summary>The same two columns on the compact 32-player scoreboard (a letter or two).</summary>
    public virtual (string First, string Second) ScoreboardColumnsShort => ("K", "D");

    /// <summary>A player's two numbers.</summary>
    public virtual (string First, string Second) ScoreboardCells(Game game, Player p) => ($"{p.Kills}", $"{p.Deaths}");

    /// <summary>What ranks players on the scoreboard (ties: kills, then fewer deaths).</summary>
    public virtual int RankScore(Player p) => p.Kills;

    /// <summary>A tag after a name on the scoreboard (" [FLAG]").</summary>
    public virtual string ScoreboardTag(Game game, Player p) => "";

    /// <summary>A line under the result on the end-of-match screen for modes without teams (null: none).</summary>
    public virtual string? ResultsLine(Game game) => null;

    /// <summary>Objectives drawn in the world (inside the blended, no-depth-write pass). Nothing by default.</summary>
    public virtual void DrawObjectives(Renderer gfx, Game game, double now)
    {
    }

    // ---- Self-test ----------------------------------------------------------------------------------------

    /// <summary>
    /// After the self-test's short bot match in this mode: anything wrong with how it went (null: fine). The game is still
    /// up, so look at scores, objectives and state.
    /// </summary>
    public virtual string? SelfTestVerify(Game game) => null;

    // ---- Shared rule pieces for modules -----------------------------------------------------------------------

    /// <summary>The classic free-for-all end: first to the limit and 2 clear, or the kill leader at the buzzer.</summary>
    protected static void FreeForAllWinCheck(Game game, double now)
    {
        var (leader, best, second) = game.Standings();
        if (leader != null && best >= game.ScoreLimit && best - second >= Game.WinMargin)
            game.EndMatch(leader, $"{game.NameOf(leader.Id)} wins {best}-{second}", now);
        else if (game.TimeLeft <= 0)
        {
            var tied = game.Players.Values.Count(p => p.Kills == best) > 1;
            if (leader == null || tied)
                game.EndMatch(null, $"Time! Draw at {best}-{best}", now);
            else
                game.EndMatch(leader, $"Time! {game.NameOf(leader.Id)} wins {best}-{second}", now);
        }
    }

    /// <summary>A team at the score limit wins; at the buzzer the team ahead wins, equal is a draw. Solo: the score is capped (TestTools.cs).</summary>
    protected static void TeamWinCheck(Game game, double now)
    {
        var limit = game.ScoreLimit > 0 ? game.ScoreLimit : int.MaxValue; // ScoreLimits.cs
        if (game.SoloMatch)
            game.SoloCapScores(limit); // alone: objectives still score, but never win (TestTools.cs)
        for (var t = 0; t < 2; t++)
        {
            if (game.TeamScore[t] >= limit)
            {
                game.EndMatch(null, $"{GameModes.TeamNames[t]} team wins {game.TeamScore[t]}-{game.TeamScore[1 - t]}", now, (byte)(Game.TeamWinBase + t));
                return;
            }
        }
        if (game.TimeLeft > 0)
            return;
        if (game.TeamScore[0] == game.TeamScore[1])
            game.EndMatch(null, $"Time! Draw at {game.TeamScore[0]}-{game.TeamScore[1]}", now);
        else
        {
            var t = game.TeamScore[0] > game.TeamScore[1] ? 0 : 1;
            game.EndMatch(null, $"Time! {GameModes.TeamNames[t]} team wins {game.TeamScore[t]}-{game.TeamScore[1 - t]}", now, (byte)(Game.TeamWinBase + t));
        }
    }

    // HUD colours (the same values as Ui's), so modules don't depend on ImGui types.
    protected static readonly Vector4 HudWhite = new(1, 1, 1, 1);
    protected static readonly Vector4 HudDim = new(0.65f, 0.65f, 0.7f, 1f);
    protected static readonly Vector4 HudOrange = new(0.95f, 0.55f, 0.18f, 1f);
    protected static readonly Vector4 HudGreen = new(0.4f, 1f, 0.5f, 1f);
    protected static readonly Vector4 HudRed = new(1f, 0.35f, 0.35f, 1f);

    /// <summary>A team's colour (Blue, Red; grey for no team) as a HUD colour.</summary>
    protected static Vector4 HudTeam(byte team)
    {
        var c = GameModes.TeamColor(team);
        return new Vector4(c.X, c.Y, c.Z, 1f);
    }
}

/// <summary>One line of a mode's HUD panel: ASCII text in a colour.</summary>
public readonly record struct HudLine(string Text, Vector4 Color);
