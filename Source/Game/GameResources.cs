using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>
/// Raw resources on the wire (ResourceField.cs): deposits and trees are the same on every peer, so only felled trees are
/// sent, as they fall and to each joiner (<see cref="Msg.ResourceFell"/>). How much a deposit holds stays on the server.
/// </summary>
public sealed partial class Game
{
    /// <summary>Most trees in one <see cref="Msg.ResourceFell"/> packet.</summary>
    public const int FellPerPacket = 256;

    /// <summary>Server, each tick: tells everyone what fell.</summary>
    private void SendFelledTrees()
    {
        var felled = Constructs.FelledTrees;
        if (felled.Count == 0)
            return;
        for (var at = 0; at < felled.Count; at += FellPerPacket)
        {
            var n = Math.Min(FellPerPacket, felled.Count - at);
            _w.Begin(Msg.ResourceFell).U16((ushort)n);
            for (var k = 0; k < n; k++)
                _w.U32(felled[at + k]);
            Net.Broadcast(_w.Span, PacketReliability.ReliableOrdered);
        }
        felled.Clear();
    }

    /// <summary>Server: sends a joining player every tree felled this match.</summary>
    private void SendResourceState(RakNetGUID to)
    {
        if (World.Map.ResourcesIfMade is not { Felled.Count: > 0 } field)
            return;
        var keys = field.Felled.ToArray();
        for (var at = 0; at < keys.Length; at += FellPerPacket)
        {
            var n = Math.Min(FellPerPacket, keys.Length - at);
            _w.Begin(Msg.ResourceFell).U16((ushort)n);
            for (var k = 0; k < n; k++)
                _w.U32(keys[at + k]);
            Net.SendTo(to, _w.Span, PacketReliability.ReliableOrdered);
        }
    }

    /// <summary>Trees a client has read from <see cref="Msg.ResourceFell"/> packets (the self-test's loopback).</summary>
    public int FellRead { get; private set; }

    /// <summary>Client: a <see cref="Msg.ResourceFell"/> (after the id).</summary>
    internal void ReadResourceFell(ref PacketReader r)
    {
        if (IsServer || !r.Has(2) || World.Map.Resources is not { } field)
            return;
        var n = r.U16();
        for (var k = 0; k < n && r.Has(4); k++)
        {
            field.Fell(r.U32());
            FellRead++;
        }
    }
}
