using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>
/// The packet safety net: other people play now, so a short, garbage or hostile packet must never take a client or
/// the host down. Every received packet goes through <see cref="OnPacketSafe"/>; if its handler throws, the packet is
/// dropped and <see cref="NetSafety.Drop"/> writes one rate-limited log line (with the stack, unless the packet was
/// just too short). Only packet handling is wrapped: the simulation's own ticks still fail loudly.
/// </summary>
public sealed partial class Game
{
    private void OnPacketSafe(RakNetGUID sender, ReadOnlySpan<byte> data, double now)
    {
        if (data.Length == 0)
            return;
        try
        {
            OnPacket(sender, data, now);
        }
        catch (Exception ex)
        {
            NetSafety.Drop(PacketSource(sender), data, ex);
        }
    }

    /// <summary>Who a packet came from, for the log.</summary>
    private string PacketSource(RakNetGUID sender)
    {
        if (!IsServer)
            return "the host";
        return _clients.TryGetValue(sender.G, out var c) ? $"{NameOf(c.Id)} (player {c.Id})" : $"connection {sender.G}";
    }

    // ---- Self-test hooks ----------------------------------------------------------------------------------

    /// <summary>Self-test: a packet straight into the handlers, without the safety net (so the test sees what they throw).</summary>
    public void SelfTestPacketRaw(RakNetGUID sender, ReadOnlySpan<byte> data, double now) => OnPacket(sender, data, now);

    /// <summary>Self-test: a packet through the safety net, exactly as <see cref="Update"/> delivers it.</summary>
    public void SelfTestPacket(RakNetGUID sender, ReadOnlySpan<byte> data, double now) => OnPacketSafe(sender, data, now);
}
