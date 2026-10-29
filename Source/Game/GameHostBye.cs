using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>
/// Why a connection ended, said plainly on both sides (board item dev-back-to-lobby-again-bl02, protocol 74):
///   - <see cref="Msg.HostBye"/>: the host closing the server (CLOSE SERVER, quitting) tells every client why before the
///     connections close, so a client sees "The host closed the server" and not a bare "Disconnected from host"; a
///     host refusing a different build says so too.
///   - A lost connection (no reply for RakNet's timeout) reads as a timeout, not as the host leaving.
///   - Both ends send <see cref="ServerInfo.ProtocolVersion"/> (the end of Msg.Welcome and Msg.Hello): two different
///     builds are refused at the door with the two numbers, instead of joining and misreading packets later.
/// </summary>
public sealed partial class Game
{
    /// <summary>Host: what clients are told when this game closes (Program sets it from the button pressed).</summary>
    public string CloseReason = "The host closed the server (CLOSE SERVER or quit).";

    private string _hostBye = "";

    /// <summary>The host's last word before it closed our connection ("" if none came).</summary>
    public string HostByeReason => _hostBye;

    /// <summary>Host, closing (Dispose): tell every connected client why, before RakNet's shutdown notice.</summary>
    private void SayHostBye()
    {
        if (!IsServer || Net.IsOffline || Net.ConnectionCount == 0)
            return;
        _w.Begin(Msg.HostBye).Str(CloseReason.Length > 200 ? CloseReason[..200] : CloseReason);
        Net.Broadcast(_w.Span, PacketReliability.ReliableOrdered); // NetPeer.Dispose's Shutdown waits for it to go out
    }

    private void ReadHostBye(ref PacketReader r)
    {
        if (r.Has(1))
            _hostBye = r.Text(200);
    }

    /// <summary>Client: the line shown when the connection to the host ends.</summary>
    private string DisconnectReason(bool timedOut)
    {
        if (_hostBye.Length > 0)
            return _hostBye;
        return timedOut
            ? "Lost the connection to the host: nothing came back for several seconds (their game closed or froze, or the network dropped)."
            : "The host closed the connection.";
    }

    /// <summary>A different build at the other end (<paramref name="theirs"/>: their protocol; 0 = an older build that doesn't send it), or null.</summary>
    internal static string? VersionProblem(int theirs, string who)
    {
        if (theirs == ServerInfo.ProtocolVersion)
            return null;
        var them = theirs == 0 ? "an older build" : $"protocol {theirs}";
        return $"The {who} has a different version of the game ({them}; this one is protocol {ServerInfo.ProtocolVersion}, build {BuildInfo.Label}). "
               + "Both need the same build: update the older one.";
    }

    /// <summary>Host: a joiner with a different build. Say why, then close their connection.</summary>
    private void RefuseVersion(RakNetGUID who, Player p, string problem)
    {
        Log($"Refused {p.Name}: {problem}", true);
        var toThem = $"The host has a different version of the game (protocol {ServerInfo.ProtocolVersion}, build {BuildInfo.Label}). Both need the same build: update the older one.";
        _w.Begin(Msg.HostBye).Str(toThem.Length > 200 ? toThem[..200] : toThem);
        Net.SendTo(who, _w.Span, PacketReliability.ReliableOrdered);
        Net.Kick(who); // (the disconnection removes them from the game as usual)
    }
}
