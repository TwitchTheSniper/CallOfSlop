namespace CallOfSlop.Platform.Steam;

/// <summary>
/// Auth sessions (Phase 2, ISteamUser): a client proves its Steam identity to the host with a session ticket made for
/// the host's Steam ID; the host begins a session with it and Steam answers with a verdict (owns the game, not banned,
/// not logged in elsewhere). The game side is GameSteamAuth.cs (Msg.SteamAuth carries the ticket). The host also keeps
/// a ban list by Steam ID (Settings "steam_bans"): a banned player is refused as they connect. Without Steam nothing
/// here does anything, and a host without Steam asks nobody for a ticket.
/// </summary>
public sealed class SteamAuth
{
    private ISteamBackend? _steam;
    private readonly Dictionary<uint, (ulong Server, byte[] Ticket)> _pending = new();
    private uint _ticketHandle;
    private readonly HashSet<ulong> _sessions = new();

    /// <summary>Client: a ticket for the host is ready to send (the ticket bytes).</summary>
    public event Action<ulong, byte[]>? TicketReady;
    /// <summary>Host: Steam's verdict on a client's session: the user, OK or not, and the reason text.</summary>
    public event Action<ulong, bool, string>? Verdict;

    internal void Attach(ISteamBackend steam)
    {
        _steam = steam;
        steam.AuthTicketReady += OnTicketReady;
        steam.AuthSessionResponse += OnResponse;
    }

    internal void Detach(ISteamBackend steam)
    {
        CancelTicket();
        foreach (var id in _sessions.ToArray())
            End(id);
        steam.AuthTicketReady -= OnTicketReady;
        steam.AuthSessionResponse -= OnResponse;
        _steam = null;
    }

    // ---- Client ----

    /// <summary>Asks Steam for a ticket proving this user to <paramref name="hostSteamId"/>; <see cref="TicketReady"/>
    /// follows. One at a time: a new request cancels the old ticket.</summary>
    public bool RequestTicket(ulong hostSteamId)
    {
        if (_steam == null || hostSteamId == 0)
            return false;
        CancelTicket();
        var handle = _steam.GetAuthSessionTicket(hostSteamId, out var ticket);
        if (handle == 0 || ticket.Length == 0)
            return false;
        _ticketHandle = handle;
        _pending[handle] = (hostSteamId, ticket);
        return true;
    }

    /// <summary>Client: done with the host (left the game): the ticket is cancelled so the host's session ends.</summary>
    public void CancelTicket()
    {
        if (_ticketHandle != 0)
        {
            _steam?.CancelAuthTicket(_ticketHandle);
            _pending.Remove(_ticketHandle);
            _ticketHandle = 0;
        }
    }

    private void OnTicketReady(uint handle, bool ok)
    {
        if (!_pending.Remove(handle, out var t))
            return;
        if (ok)
            TicketReady?.Invoke(t.Server, t.Ticket);
        else
            Console.WriteLine("[steam] the auth ticket could not be made");
    }

    // ---- Host ----

    /// <summary>Host: begins checking a client's ticket; false (with why) when Steam refuses it outright, else
    /// <see cref="Verdict"/> follows. A session already open for that user is replaced.</summary>
    public bool Begin(byte[] ticket, ulong steamId, out string error)
    {
        error = "";
        if (_steam == null)
        {
            error = "Steam is not running on the host";
            return false;
        }
        if (_sessions.Contains(steamId))
            End(steamId);
        var result = _steam.BeginAuthSession(ticket, steamId);
        if (result != 0)
        {
            error = result switch
            {
                1 => "invalid ticket",
                2 => "duplicate request",
                3 => "ticket version mismatch",
                4 => "ticket for another game",
                5 => "expired ticket",
                _ => $"ticket refused ({result})",
            };
            return false;
        }
        _sessions.Add(steamId);
        return true;
    }

    public void End(ulong steamId)
    {
        if (_sessions.Remove(steamId))
            _steam?.EndAuthSession(steamId);
    }

    public bool HasSession(ulong steamId) => _sessions.Contains(steamId);

    private void OnResponse(ulong steamId, int response, ulong owner)
    {
        if (!_sessions.Contains(steamId))
            return;
        var ok = response == 0;
        var why = response switch
        {
            0 => "",
            1 => "not connected to Steam",
            2 => "no licence for the game",
            3 => "VAC banned",
            4 => "logged in elsewhere",
            5 => "VAC check timed out",
            6 => "ticket cancelled",
            7 => "ticket already used",
            8 => "invalid ticket",
            9 => "banned by the publisher",
            _ => $"auth failed ({response})",
        };
        if (!ok)
            End(steamId);
        Verdict?.Invoke(steamId, ok, why);
    }
}

public static partial class SteamPlatform
{
    /// <summary>Auth tickets and sessions (Phase 2). Everything is a no-op without Steam.</summary>
    public static SteamAuth Auth { get; } = new();
}
