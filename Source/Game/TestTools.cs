using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>
/// Testing helpers players can use in a real match:
///   - Solo matches ("No bots (test mode)"): when the server has nobody but you, the match never ends on its own.
///     The match clock stops while you're alone and score limits are held one short of the win, so you can try
///     weapons, attachments, perks, streak screens and mode objectives for as long as you like. The moment
///     anyone else is in the match (a person joins), the normal rules apply again.
///   - Kill yourself (pause menu): a suicide through the normal server damage path (no kill credit, the usual
///     death screen and respawn). Clients ask the server with <see cref="Msg.Suicide"/>; the server decides.
/// </summary>
public sealed partial class Game
{
    /// <summary>Kill-feed id for a requested suicide ("Suicide" in the feed; no weapon involved).</summary>
    public const byte SuicideKillId = 255;

    /// <summary>The server allows one suicide request per player per this many seconds.</summary>
    public const double SuicideCooldown = 1.0;

    /// <summary>True on the server when nobody but one player (or nobody) is in the match: it never ends on its own.</summary>
    public bool SoloMatch => IsServer && (Players.Count <= 1 || BuilderRange); // the Workbench Range's practice dummies don't count

    private double _soloElapsed; // match seconds played so far, kept while the clock is stopped for a solo match
    private readonly Dictionary<byte, double> _suicideAt = new(); // server: when each player last killed themselves
    private double _localSuicideAt = -10; // client: when we last asked (so a held click doesn't spam the server)

    /// <summary>Server, every playing tick before the time check: stops the match clock while the match is solo.</summary>
    private void SoloHoldClock(double now)
    {
        if (SoloMatch)
            _matchStart = now - _soloElapsed;
        else
            _soloElapsed = now - _matchStart;
    }

    /// <summary>Server, solo match: team scores stay one short of the limit, so objectives score but never win.</summary>
    internal void SoloCapScores(int limit)
    {
        for (var t = 0; t < 2; t++)
            if (TeamScore[t] >= limit)
                TeamScore[t] = limit - 1;
    }

    /// <summary>Can the local player use "Kill yourself" right now (alive, match live, not on cooldown)?</summary>
    public bool CanRequestSuicide =>
        Phase == MatchPhase.Playing && Local is { Alive: true } && Brutal.GlfwApi.Glfw.Time - _localSuicideAt >= SuicideCooldown;

    /// <summary>The pause menu's "Kill yourself": the host kills itself directly, a client asks the server.</summary>
    public void RequestSuicide() => RequestSuicide(Brutal.GlfwApi.Glfw.Time);

    /// <summary><see cref="RequestSuicide()"/> with an explicit clock (self-test).</summary>
    public void RequestSuicide(double now)
    {
        if (Phase != MatchPhase.Playing || Local is not { Alive: true } me)
            return;
        _localSuicideAt = now;
        if (IsServer)
            ServerSuicide(me, now);
        else
        {
            _w.Begin(Msg.Suicide);
            Net.SendToServer(_w.Span, PacketReliability.ReliableOrdered);
        }
    }

    /// <summary>
    /// Server: <paramref name="p"/> kills themselves through the normal damage path (spawn protection and Last Stand
    /// don't save you). It counts as a suicide: a death, no kill for anyone, the streak resets. Returns true if it killed.
    /// </summary>
    private bool ServerSuicide(Player p, double now)
    {
        if (!p.Alive || Phase != MatchPhase.Playing)
            return false;
        if (_suicideAt.TryGetValue(p.Id, out var last) && now - last < SuicideCooldown)
            return false; // rate limit
        _suicideAt[p.Id] = now;
        p.ImmuneUntil = 0;
        p.LastStandUsed = true;
        return DamagePlayer(p, p, 100000, false, SuicideKillId, now);
    }

    private void OnSuicidePacket(RakNetGUID sender, double now)
    {
        if (IsServer && _clients.TryGetValue(sender.G, out var c) && Players.TryGetValue(c.Id, out var p))
            ServerSuicide(p, now);
    }

    // ---- Self-test hooks -------------------------------------------------------------------------

    /// <summary>Self-test: the server handling a client's suicide request for <paramref name="p"/>.</summary>
    public bool SelfTestSuicideRequest(Player p, double now) => ServerSuicide(p, now);
}
