using System.Numerics;
using Brutal.RakNetApi;

namespace CallOfSlop;

public enum MatchPhase : byte
{
    Countdown, // 5 s before each match: pick a loadout, everyone is frozen and nobody can be hurt
    Playing,
    Ended, // results screen; nobody can be hurt; a new match starts automatically
    Warmup, // before the countdown: waiting for every person to ready up; nobody can be hurt (see ReadyUp.cs)
}

/// <summary>
/// Match rules (server-authoritative, same in single player and multiplayer):
///   - first to 11 kills wins (with 8 players; the Auto score limit scales it with the player count, ScoreLimits.cs),
///     but you must lead the next player by 2 ("win by two", like tennis),
///   - matches last at most 10 minutes; at the buzzer the kill leader wins, a tie at the top is a draw,
///   - then a results screen, and a fresh match starts on the same map,
///   - every match opens with a 5 second countdown (frozen, loadout menu open).
/// </summary>
public sealed partial class Game
{
    public const int WinKills = 11;
    public const int WinMargin = 2;
    public const double MatchSeconds = 10 * 60;
    public const double IntermissionSeconds = 12;
    public const double CountdownSeconds = 5;
    public const byte NoWinner = byte.MaxValue;

    public MatchPhase Phase = MatchPhase.Playing;
    public byte Winner = NoWinner; // NoWinner during play, or after a draw
    public string MatchResult = ""; // "Player X wins 11-9", "Draw at 7-7"...
    public int MatchNumber = 1;

    /// <summary>Seconds left in the match (Playing) or until the next match (Ended). Clients count it down locally.</summary>
    public double TimeLeft = MatchSeconds;

    private double _matchStart;
    private double _intermissionUntil;
    private double _countdownUntil;
    private bool _matchClockStarted;
    private int _lastBeep = -1;
    private bool _countdownStartedFlag, _playStartedFlag;

    public bool DamageEnabled => Phase == MatchPhase.Playing;

    /// <summary>Players can't move during the countdown (they can still look around and change stance).</summary>
    public bool Frozen => Phase == MatchPhase.Countdown;

    private const Buttons MoveButtons = Buttons.Forward | Buttons.Back | Buttons.Left | Buttons.Right | Buttons.Jump | Buttons.Sprint;

    /// <summary>Strips movement from a command while frozen. Used by prediction and the server alike.</summary>
    private InputCmd FreezeFilter(InputCmd cmd)
    {
        if (Frozen)
            cmd.Buttons &= ~MoveButtons;
        return cmd;
    }

    /// <summary>True once after a countdown begins (Program opens the loadout menu).</summary>
    public bool TakeCountdownStarted()
    {
        var f = _countdownStartedFlag;
        _countdownStartedFlag = false;
        return f;
    }

    /// <summary>True once when the countdown hits zero (Program deploys and closes the loadout menu).</summary>
    public bool TakePlayStarted()
    {
        var f = _playStartedFlag;
        _playStartedFlag = false;
        return f;
    }

    /// <summary>Is the local player in "deuce" (at or past 10 kills but not yet 2 clear)?</summary>
    public (Player? Leader, int LeaderKills, int SecondKills) Standings()
    {
        Player? leader = null;
        int best = -1, second = -1;
        foreach (var p in Players.Values)
        {
            if (p.Kills > best)
            {
                second = best;
                best = p.Kills;
                leader = p;
            }
            else if (p.Kills > second)
            {
                second = p.Kills;
            }
        }
        return (leader, Math.Max(best, 0), Math.Max(second, 0));
    }

    /// <summary>Server, every tick: timer and win check.</summary>
    private void ServerTickMatch(double now)
    {
        if (!_matchClockStarted)
        {
            _matchClockStarted = true;
            BeginPreMatch(now);
        }
        ServerTickMode(now);

        if (Phase is MatchPhase.Warmup or MatchPhase.Countdown)
            UpdateScoreLimit(); // before play the limit follows whoever is there

        if (Phase == MatchPhase.Warmup)
        {
            ServerTickWarmup(now);
        }
        else if (Phase == MatchPhase.Countdown)
        {
            TimeLeft = Math.Max(0, _countdownUntil - now);
            CountdownBeeps();
            if (now >= _countdownUntil)
            {
                Phase = MatchPhase.Playing;
                ScoreLimit = ScoreLimitFor(Players.Count); // everyone who made it to the start counts (ScoreLimits.cs)
                _matchStart = now;
                _soloElapsed = 0;
                TimeLeft = MatchSeconds;
                OnPlayStarted();
            }
        }
        else if (Phase == MatchPhase.Playing)
        {
            SoloHoldClock(now); // alone in the match (no bots, nobody joined): the clock stops (TestTools.cs)
            UpdateScoreLimit(); // people joining mid-match raise the limit, never lower it (ScoreLimits.cs)
            TimeLeft = Math.Max(0, MatchSeconds - (now - _matchStart));
            var (leader, best, second) = Standings();
            if (Mode != GameMode.FreeForAll)
                ServerTickModePlaying(now); // objectives and the mode's own win rules (GameModes.cs)
            else if (leader != null && best >= ScoreLimit && best - second >= WinMargin)
                EndMatch(leader, $"{NameOf(leader.Id)} wins {best}-{second}", now);
            else if (TimeLeft <= 0)
            {
                var tied = Players.Values.Count(p => p.Kills == best) > 1;
                if (leader == null || tied)
                    EndMatch(null, $"Time! Draw at {best}-{best}", now);
                else
                    EndMatch(leader, $"Time! {NameOf(leader.Id)} wins {best}-{second}", now);
            }
        }
        else
        {
            TimeLeft = Math.Max(0, _intermissionUntil - now);
            ServerTickVotes(now);
            if (now >= _intermissionUntil)
                StartNewMatch(now);
        }
    }

    private void EndMatch(Player? winner, string result, double now, byte? winnerCode = null)
    {
        Phase = MatchPhase.Ended;
        Winner = winnerCode ?? winner?.Id ?? NoWinner; // winnerCode: TeamWinBase + team for a team win
        MatchResult = result;
        _intermissionUntil = now + IntermissionSeconds;
        TimeLeft = IntermissionSeconds;

        var bytes = System.Text.Encoding.ASCII.GetBytes(result.Length > 60 ? result[..60] : result);
        _w.Begin(Msg.MatchEnd).U8(Winner).U8((byte)bytes.Length);
        foreach (var c in bytes)
            _w.U8(c);
        Net.Broadcast(_w.Span, PacketReliability.ReliableOrdered);
        OnMatchEnded(now);
        if (MapVoteEnabled && Mode != GameMode.Zombies) // a Zombies run restarts on its own map
            StartMapVote(now);
    }

    /// <summary>Smoke test only: end the current match right now (server).</summary>
    public void DebugEndMatch(double now)
    {
        if (IsServer && Phase == MatchPhase.Playing)
            EndMatch(null, "Smoke test: match ended early", now);
    }

    private void BeginCountdown(double now)
    {
        Phase = MatchPhase.Countdown;
        _countdownUntil = now + CountdownSeconds;
        TimeLeft = CountdownSeconds;
        _lastBeep = -1;
        _countdownStartedFlag = true;
    }

    private void StartNewMatch(double now)
    {
        if (MapVoteEnabled && ResolveVote() is { } next)
            ApplyMapChange(next);
        Winner = NoWinner;
        MatchResult = "";
        MatchNumber++;
        BeginPreMatch(now);
        ResetModeForMatch(respawn: false); // new teams, scores and objectives before everyone spawns

        Projectiles.Clear();
        Smokes.Clear();
        Sensors.Clear();
        foreach (var p in Players.Values)
        {
            p.Kills = 0;
            p.Deaths = 0;
            Spawn(p, ServerTick, now);
            if (p.Id == LocalId)
                OnLocalSpawn(p);
            if (p.Bot != null)
                p.Bot.Waypoint = Vector3.Zero;
        }

        _w.Begin(Msg.MatchStart).U16((ushort)MatchNumber).Str(World.Map.Name).U32(World.Map.ContentHash).U8((byte)Phase);
        Net.Broadcast(_w.Span, PacketReliability.ReliableOrdered);
        OnMatchStarted();
    }

    // ---- Both sides -----------------------------------------------------------------

    private void OnMatchEnded(double now)
    {
        var won = LocalWon;
        Log(MatchResult, true);
        // Match XP for whoever finished the match in it (account XP + a weapon XP bonus for the loadout carried).
        if (Progress != null && Local is { } me && Progress.AwardMatch(won, me.Primary, me.Secondary, Brutal.GlfwApi.Glfw.Time) != null)
            Audio.Effect(Sfx.LevelUp, 0.8f);
        if (Local != null)
            Audio.Effect(won ? Sfx.LevelUp : Winner == NoWinner ? Sfx.Respawn : Sfx.Death, 0.8f);
    }

    private void OnMatchStarted()
    {
        World.Map.RestoreProps(); // breakable props are whole again every match
        if (!IsServer)
            ResetZombiesWorld(); // Zombies: doors shut, boards up, no zombies (the server resets its run in ResetModeForMatch)
        PropDebris.Clear();
        ClearVote();
        Feed.Clear();
        Tracers.Clear();
        Projectiles.Clear();
        Smokes.Clear();
        Sensors.Clear();
        Blasts.Clear();
        Log($"Match {MatchNumber}: {ModeGoalText}", true);
        if (!IsServer)
            _localInitialized = false; // treat the next snapshot as a fresh spawn (full ammo, new position)
    }

    private void OnPlayStarted()
    {
        _playStartedFlag = true;
        Log("GO!", true);
        Audio.Effect(Sfx.Respawn, 1f);
    }

    /// <summary>A beep on each of the last seconds of the countdown.</summary>
    private void CountdownBeeps()
    {
        var sec = (int)Math.Ceiling(TimeLeft);
        if (sec != _lastBeep && sec >= 1 && sec <= 3)
            Audio.Effect(Sfx.ReconBeep, 0.7f);
        _lastBeep = sec;
    }

    /// <summary>Client: tick the countdown between snapshots.</summary>
    private void ClientTickMatch(double dt)
    {
        TimeLeft = Math.Max(0, TimeLeft - dt);
        if (Phase == MatchPhase.Countdown)
            CountdownBeeps();
    }

    private void OnMatchPacket(byte id, ref PacketReader r)
    {
        switch (id)
        {
            case (byte)Msg.MatchEnd when !IsServer && r.Has(2):
            {
                Phase = MatchPhase.Ended;
                Winner = r.U8();
                int len = r.U8();
                var chars = new char[len];
                for (var k = 0; k < len && r.Has(1); k++)
                    chars[k] = (char)r.U8();
                MatchResult = NetSafety.CleanText(new string(chars), 255);
                OnMatchEnded(Brutal.GlfwApi.Glfw.Time);
                break;
            }
            case (byte)Msg.MatchStart when !IsServer && r.Has(2):
                MatchNumber = r.U16();
                if (r.Has(1) && !UseHostMap(r.Str(), r.Has(4) ? r.U32() : 0, initial: false))
                    break;
                Winner = NoWinner;
                MatchResult = "";
                Phase = r.Has(1) ? (MatchPhase)r.U8() : MatchPhase.Countdown; // Warmup when the host waits for ready-up
                TimeLeft = Phase == MatchPhase.Countdown ? CountdownSeconds : 0;
                _lastBeep = -1;
                _countdownStartedFlag = true;
                OnMatchStarted();
                break;
        }
    }
}
