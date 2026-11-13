using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>
/// The moment in the middle of a player's screen when they learn or reach something (drawn by Ui.DrawCelebration,
/// UiTechnologies.cs): a title ("TECHNOLOGY LEARNT", "PROMOTED", "FIRST FLIGHT"), a name in big letters and a line under it.
/// Technologies and tool upgrades are noticed by every peer on its own (UiTechnologies.cs); anything else the server
/// decides calls <see cref="Celebrate"/> for one player, and code on a peer that knows for itself calls
/// <see cref="CelebrateHere"/>. They queue, one at a time, and wait out cinematics. Nothing is sent but the words, once.
/// </summary>
public sealed partial class Game
{
    /// <summary>This peer's moments still to be shown (Ui takes them each frame).</summary>
    public readonly Queue<(string Title, string Name, string Line)> Celebrations = new(4);

    /// <summary>A moment on this peer's own screen (for its local player).</summary>
    public void CelebrateHere(string title, string name, string line = "")
    {
        if (Celebrations.Count < 16)
            Celebrations.Enqueue((title, name, line));
    }

    /// <summary>Server: a moment on one player's screen: the host's own here, a client's sent to them.</summary>
    public void Celebrate(byte player, string title, string name, string line = "")
    {
        if (player == LocalId)
            CelebrateHere(title, name, line);
        else if (IsServer && !Net.IsOffline && Players.TryGetValue(player, out var p) && p.Bot == null && GuidOf(p) is { } g)
        {
            SbW.Begin(Msg.CAct).U8((byte)CActKind.Celebrate).Str(title).Str(name).Str(line);
            Net.SendTo(g, SbW.Span, PacketReliability.ReliableOrdered);
        }
    }

    /// <summary>Client: a <see cref="CActKind.Celebrate"/> after its kind.</summary>
    private void CelebrateRead(ref PacketReader r)
    {
        var title = r.Text(48);
        var name = r.Text(96);
        var line = r.Has(1) ? r.Text(200) : "";
        CelebrateHere(title, name, line);
    }
}
