using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>
/// Surrendered crew put their hands up (Karl, 2026-11-18: boarding the Traders' ship shows them with their hands in the air,
/// shaking): the host finds every crew body whose member is surrendered (a crew that gave up, or prisoners on a taken ship), a few
/// times a second, and tells everyone which bodies changed (<see cref="Msg.HandsUp"/>, and all of them to a joiner). Each peer
/// keeps <see cref="HandsUp"/> by player id; GameSandbox copies it onto <see cref="Player.RenderHandsUp"/> each frame, and
/// PlayerRender draws the pose (no gun, both hands over the head, trembling).
/// </summary>
public sealed partial class Game
{
    /// <summary>Every peer: player ids whose body has its hands up (the host's own word, or the last <see cref="Msg.HandsUp"/>). Never read by the simulation.</summary>
    public readonly bool[] HandsUp = new bool[256];

    private int _handsUpAt;

    /// <summary>Host, in the crew tick: the bodies whose hands went up or came down since the last look (every quarter second), to everyone.</summary>
    private void HandsUpTick()
    {
        if (++_handsUpAt % 15 != 0)
            return;
        Span<byte> changed = stackalloc byte[256];
        var n = 0;
        for (var id = 0; id < 255; id++)
        {
            var up = Players.TryGetValue((byte)id, out var p) && p.Alive && p.CrewSlot >= 0 && p.CrewSlot < Crew.High
                && Crew.State[p.CrewSlot] == CrewState.Surrendered;
            if (up == HandsUp[id])
                continue;
            HandsUp[id] = up;
            changed[n++] = (byte)id;
        }
        if (n == 0 || Net.IsOffline)
            return;
        var w = _w;
        w.Begin(Msg.HandsUp).U8((byte)n);
        for (var k = 0; k < n; k++)
            w.U8(changed[k]).U8((byte)(HandsUp[changed[k]] ? 1 : 0));
        Net.Broadcast(w.Span, PacketReliability.ReliableOrdered);
    }

    /// <summary>A joiner hears every body with its hands up now.</summary>
    private void SendHandsUp(RakNetGUID to)
    {
        var n = 0;
        for (var id = 0; id < 255; id++)
            if (HandsUp[id])
                n++;
        if (n == 0)
            return;
        _w.Begin(Msg.HandsUp).U8((byte)n);
        for (var id = 0; id < 255; id++)
            if (HandsUp[id])
                _w.U8((byte)id).U8(1);
        Net.SendTo(to, _w.Span, PacketReliability.ReliableOrdered);
    }

    /// <summary>Client: Msg.HandsUp (u8 n, n x (u8 player, u8 up)).</summary>
    private void ReadHandsUp(ref PacketReader r)
    {
        int n = r.U8();
        if (!r.Has(n * 2))
        {
            Console.WriteLine($"[crew] a hands-up list of {n} didn't read: dropped");
            return;
        }
        for (var k = 0; k < n; k++)
        {
            var id = r.U8();
            HandsUp[id] = r.U8() != 0;
        }
    }
}
