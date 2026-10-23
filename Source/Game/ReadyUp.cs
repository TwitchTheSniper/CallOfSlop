using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>
/// Ready-up (server-authoritative). Before every match (the first of a session and each one after the map vote) there's
/// a warmup: people pick loadouts and can walk around (nobody can be hurt), and the HUD lists who's ready. The 5 second
/// countdown starts as soon as every person is ready; bots are always ready. The host can force the start, and can set
/// it to start on its own N seconds after the first person readies up. In single player, Ready or Deploy starts it.
/// Disconnects never stall it (only the players still here count), and people who join during the match never block.
/// </summary>
public sealed partial class Game
{
    /// <summary>Hold each match in a warmup until everyone is ready. Program turns this on; off, matches go straight to the countdown.</summary>
    public bool WarmupEnabled;

    /// <summary>Host setting: once someone is ready, start anyway after this many seconds (0: wait for everyone).</summary>
    public int AutoStartSeconds;

    private double _autoStartAt;
    private bool _forceStart;
    private double _readyToggledAt = -10;

    public bool IsWarmup => Phase == MatchPhase.Warmup;

    /// <summary>How many people (not bots) are ready, out of how many.</summary>
    public (int Ready, int Humans) ReadyCount
    {
        get
        {
            int ready = 0, humans = 0;
            foreach (var p in Players.Values)
            {
                if (p.Bot != null)
                    continue;
                humans++;
                if (p.Ready)
                    ready++;
            }
            return (ready, humans);
        }
    }

    /// <summary>Seconds until the auto-start kicks in (0 when it isn't counting).</summary>
    public double AutoStartIn => IsWarmup && TimeLeft > 0 ? TimeLeft : 0;

    /// <summary>The Ready key / button.</summary>
    public void ToggleReady()
    {
        if (ZToggleRoundReady())
            return; // a Zombies break: ready for the next round (ZombiesFlow.cs)
        SetReady(!(Local?.Ready ?? false));
    }

    public void SetReady(bool ready)
    {
        if (Phase is not (MatchPhase.Warmup or MatchPhase.Lobby) || Local is not { } me || me.Ready == ready) // (the lobby room too, PreMatchLobby.cs)
            return;
        me.Ready = ready; // shown straight away; the server's copy follows
        _readyToggledAt = Brutal.GlfwApi.Glfw.Time;
        Audio.Effect(Sfx.HitTick, 0.5f);
        if (IsServer)
            ServerSetReady(me, ready);
        else
        {
            _w.Begin(Msg.Ready).U8(ready ? (byte)1 : (byte)0);
            Net.SendToServer(_w.Span, PacketReliability.ReliableOrdered);
        }
    }

    /// <summary>Host only: start the countdown now, ready or not.</summary>
    public void ForceStart()
    {
        if (IsServer && Phase == MatchPhase.Warmup)
            _forceStart = true;
    }

    private void ServerSetReady(Player p, bool ready)
    {
        if (Phase == MatchPhase.Playing && IsZombies)
        {
            ServerSetRoundReady(p, ready); // a Zombies break: ready for the next round (ZombiesFlow.cs)
            return;
        }
        if (Phase is not (MatchPhase.Warmup or MatchPhase.Lobby) || p.Ready == ready)
            return;
        p.Ready = ready;
        var (r, n) = ReadyCount;
        if (!Net.IsOffline)
            Log($"{p.Name} is {(ready ? "ready" : "not ready")} ({r}/{n})");
    }

    /// <summary>Warmup or straight to the countdown, depending on <see cref="WarmupEnabled"/>.</summary>
    private void BeginPreMatch(double now)
    {
        if (WarmupEnabled)
            BeginWarmup(now);
        else
            BeginCountdown(now);
    }

    private void BeginWarmup(double now)
    {
        Phase = MatchPhase.Warmup;
        TimeLeft = 0;
        _autoStartAt = 0;
        _forceStart = false;
        foreach (var p in Players.Values)
            p.Ready = false;
        _countdownStartedFlag = true; // Program opens the loadout screen
    }

    /// <summary>Server, every warmup tick: start the countdown once every person is ready (or forced, or the auto-start ran out).</summary>
    private void ServerTickWarmup(double now)
    {
        var (ready, humans) = ReadyCount;
        var start = _forceStart || ready == humans;
        if (!start && AutoStartSeconds > 0 && ready > 0)
        {
            if (_autoStartAt <= 0)
                _autoStartAt = now + AutoStartSeconds;
            start = now >= _autoStartAt;
        }
        else if (ready == 0)
            _autoStartAt = 0;
        TimeLeft = _autoStartAt > 0 ? Math.Max(0, _autoStartAt - now) : 0;
        if (!start)
            return;
        if (_forceStart && ready < humans)
            Log("The host started the match", true);
        StartCountdownFromWarmup(now);
    }

    /// <summary>Everyone goes back to a spawn point (warmup wanderers too), frozen for the countdown.</summary>
    private void StartCountdownFromWarmup(double now)
    {
        BeginCountdown(now);
        _forceStart = false;
        foreach (var p in Players.Values)
        {
            p.Kills = 0;
            p.Deaths = 0;
            Spawn(p, ServerTick, now);
            if (p.Id == LocalId)
                OnLocalSpawn(p);
        }
    }
}
