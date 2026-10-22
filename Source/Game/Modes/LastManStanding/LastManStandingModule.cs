using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Last Man Standing: the TEMPLATE mode (Source/Game/Modes/README.md walks through it). One life each: nobody respawns
/// while the match is on, the last player standing wins, and at the buzzer the most kills wins. Dev-only: it shows up in
/// the lobby for maps of a dev pack (Dev: Zombies Test), and the self-test plays a bot match in it.
///
/// It touches every kind of hook a mode can have, in the smallest way: a rule flag (no respawns), lifecycle (reset,
/// deaths), the win check, a bot objective (the living gather towards the middle so a round ends), a replicated state
/// tail (how many are standing), HUD lines, scoreboard columns and a self-test verification.
/// </summary>
public sealed class LastManStandingModule : GameModeModule
{
    public override GameMode Id => GameMode.LastManStanding;
    public override string Name => "Last Man Standing";
    public override string Short => "LMS";
    public override string Rules(int limit = 0) => "One life each. The last player standing wins; at the buzzer the most kills wins.";
    public override bool DevOnly => true;
    public override bool ShowsRespawnMessage => false; // the HUD line below says you're out

    // ---- State (server-owned, replicated in the mode-state tail) ---------------------------------------------

    /// <summary>Players still alive (replicated so clients can show it before their snapshot catches up).</summary>
    public int Standing;

    /// <summary>Order players went down in (server): the last one standing gets rank 1.</summary>
    private readonly List<byte> _eliminated = new();

    public override void OnReset(Game game)
    {
        Standing = game.Players.Count;
        _eliminated.Clear();
    }

    /// <summary>One life: nobody comes back while the match is on (everyone spawns again when the next match starts).</summary>
    public override bool CanRespawn(Game game, Player p, double now) => game.Phase != MatchPhase.Playing;

    public override void OnKill(Game game, Player attacker, Player victim, byte killId, double now)
    {
        if (!_eliminated.Contains(victim.Id))
            _eliminated.Add(victim.Id);
    }

    public override void OnPlayerLeave(Game game, Player p) => _eliminated.Remove(p.Id);

    public override void ServerTickPlaying(Game game, double now)
    {
        var alive = game.Players.Values.Where(p => p.Alive).ToList();
        Standing = alive.Count;
        if (game.SoloMatch)
            return; // alone: nothing to win (TestTools.cs)
        if (alive.Count == 1)
        {
            game.EndMatch(alive[0], $"{game.NameOf(alive[0].Id)} is the last one standing", now);
            return;
        }
        if (alive.Count == 0)
        {
            // Everyone went down in the same tick (grenades): the last to fall wins.
            var last = _eliminated.Count > 0 && game.Players.TryGetValue(_eliminated[^1], out var l) ? l : null;
            game.EndMatch(last, last != null ? $"{game.NameOf(last.Id)} fell last and wins" : "Draw: nobody left standing", now);
            return;
        }
        if (game.TimeLeft <= 0)
            FreeForAllWinCheck(game, now); // the buzzer: the kill leader wins, a tie is a draw
    }

    /// <summary>The living close in on the middle of the map, so a round ends instead of two bots hiding forever.</summary>
    public override Vector3? BotObjective(Game game, Player bot) => Standing <= 3 ? Vector3.Zero : null;

    // ---- Replicated state (1 byte) --------------------------------------------------------------------------

    public override void WriteState(Game game, PacketWriter w) => w.U8((byte)Math.Min(Standing, 255));

    public override void ReadState(Game game, ref PacketReader r)
    {
        if (r.Has(1))
            Standing = r.U8();
    }

    // ---- HUD ----------------------------------------------------------------------------------------------

    public override void HudLines(Game game, Player me, List<HudLine> lines, ref Vector3? pointAt)
    {
        lines.Add(new HudLine($"{Standing} STANDING", HudOrange));
        lines.Add(me.Alive ? new HudLine("One life - make it count", HudDim) : new HudLine("You're out - back next match", HudRed));
    }

    public override (string Big, string Sub)? ScoreBar(Game game) => ($"{Standing} LEFT", "Last Man Standing - one life each");

    public override (string First, string Second) ScoreboardColumns => ("Kills", "Status");

    public override (string First, string Second) ScoreboardColumnsShort => ("K", "St");

    public override (string First, string Second) ScoreboardCells(Game game, Player p) => ($"{p.Kills}", p.Alive ? "standing" : "out");

    public override string? SelfTestVerify(Game game)
    {
        var alive = game.Players.Values.Count(p => p.Alive);
        var deaths = game.Players.Values.Sum(p => (int)p.Deaths);
        if (deaths > 0 && game.Phase == MatchPhase.Playing && alive + deaths != game.Players.Count)
            return $"someone respawned: {alive} alive + {deaths} deaths for {game.Players.Count} players";
        return null;
    }
}
