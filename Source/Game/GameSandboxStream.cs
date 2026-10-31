using System.Numerics;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>
/// Big Sandbox worlds on the wire (board item cl01; docs/sandbox/CONSTRUCTS.md "Scale"). Once a world holds
/// <see cref="ConstructWorld.LinearBelow"/> bodies or more:
/// <list type="bullet">
/// <item>poses go to each client on its own (<see cref="ConstructNet.WritePosesFor"/>): the bodies that move near it every
/// pose tick, far ones less often, sleeping ones refreshed a few at a time;</item>
/// <item>a joiner gets the constructs streamed, <see cref="ConstructNet.StreamBytesPerTick"/> a tick, instead of one
/// burst; what refers to them (stores, links, its inventory) follows the last one.</item>
/// </list>
/// Smaller worlds keep the broadcast and the burst exactly as before.
/// </summary>
public sealed partial class Game
{
    private sealed class SbStream
    {
        public RakNetGUID To;
        public ushort[] Ids = Array.Empty<ushort>();
        public int Next;
    }

    private readonly List<SbStream> _sbStreams = new(8);

    /// <summary>Joiners still being streamed the world (the self-test).</summary>
    public int SandboxStreaming => _sbStreams.Count;

    private void SandboxStreamStart(RakNetGUID to)
    {
        _sbStreams.RemoveAll(s => s.To.G == to.G);
        var ids = new ushort[Constructs.Bodies.Count]; // (once a join: the bodies as they are now; new ones reach it as everyone's structure)
        for (var i = 0; i < ids.Length; i++)
            ids[i] = Constructs.Bodies[i].Id;
        var s = new SbStream { To = to, Ids = ids };
        _sbStreams.Add(s);
        StreamSlice(s); // (the first slice at once)
    }

    /// <summary>Server, every tick: each joiner's next slice of the world; the tail when its last construct went.</summary>
    private void SandboxStreamTick()
    {
        for (var k = _sbStreams.Count - 1; k >= 0; k--)
            if (StreamSlice(_sbStreams[k]))
                _sbStreams.RemoveAt(k);
    }

    /// <summary>Sends up to a tick's bytes of structure; true when the joiner has the whole world (the tail sent too).</summary>
    private bool StreamSlice(SbStream s)
    {
        var w = SbW;
        var bytes = 0;
        while (s.Next < s.Ids.Length && bytes < ConstructNet.StreamBytesPerTick)
        {
            if (Constructs.Get(s.Ids[s.Next++]) is not { } c)
                continue; // (gone since: its Gone went to everyone)
            if (c.Dirty)
                c.Rebuild();
            ConstructNet.WriteFull(w, c);
            bytes += w.Length;
            Net.SendTo(s.To, w.Span, PacketReliability.ReliableOrdered);
        }
        if (s.Next < s.Ids.Length)
            return false;
        SendSandboxTail(s.To);
        return true;
    }

    private uint _sbPoseCount;

    /// <summary>Server, each pose tick: the poses to the clients (a broadcast in a small world; each client its own in a big one).</summary>
    private void SandboxPoses(PacketWriter w)
    {
        if (!Constructs.UseGrid)
        {
            if (ConstructNet.WritePoses(w, Constructs, ServerTick, ref _sbSleepCursor))
                Net.Broadcast(w.Span, PacketReliability.UnreliableSequenced);
            return;
        }
        var poseTick = ++_sbPoseCount;
        ConstructNet.PoseMovers(Constructs, poseTick);
        if (!Net.IsOffline)
            foreach (var (guid, id) in _clients.Values)
            {
                if (guid.G == 0 || !Players.TryGetValue(id, out var p))
                    continue;
                _sbPoseTo = guid;
                ConstructNet.WritePosesFor(w, Constructs, ServerTick, poseTick, p.Position, _sbSleepCursor, _sbPoseSend ??= SendPoseTo);
            }
        ConstructNet.PoseSent();
        if (Constructs.Bodies.Count > 0)
            _sbSleepCursor = (_sbSleepCursor + ConstructNet.SleepingPerPacket) % Constructs.Bodies.Count;
    }

    /// <summary>The host's Sandbox safety caps (the lobby room; Settings.SandboxMaxBodies and SandboxMaxPerPlayer): the server refuses past them.</summary>
    public void SetSandboxLimits(int bodies, int perPlayer) => Constructs.SetCaps(bodies, perPlayer);

    private RakNetGUID _sbPoseTo;
    private Action<PacketWriter>? _sbPoseSend;

    private void SendPoseTo(PacketWriter w) => Net.SendTo(_sbPoseTo, w.Span, PacketReliability.Unreliable);
}
