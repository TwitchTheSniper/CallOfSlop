using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The Wireless pair (docs/sandbox/CONSTRUCTS.md, "Wireless and the Data Bus"): a sender puts its input on a channel, and a
/// receiver puts out the nearest sender on its channel that reaches it. It works between constructs. The senders are gathered once a
/// tick, before the logic, into a fixed list, so a signal takes one tick more than a wire and nothing allocates.
/// </summary>
public sealed partial class ConstructWorld
{
    private const int MaxSenders = 256;

    private struct RadioSender
    {
        public Vector3 At;
        public float Value, Reach;
        public int Channel;
    }

    private readonly RadioSender[] _senders = new RadioSender[MaxSenders];
    private int _senderCount;

    private static Vector3 PartAt(Construct c, in CPart p)
    {
        c.WorldFrame(p.Seg, out var q, out var t);
        return t + Vector3.Transform(p.Pos, q);
    }

    private void RadioGather()
    {
        _senderCount = 0;
        foreach (var c in Bodies)
            for (var k = 0; k < c.RadioParts.Length && _senderCount < MaxSenders; k++)
            {
                int part = c.RadioParts[k];
                if (part >= c.PartCount)
                    continue;
                ref var p = ref c.Parts[part];
                if (p.Broken || !ConstructPower.On(c, part))
                    continue;
                _senders[_senderCount++] = new RadioSender { At = PartAt(c, p), Value = p.Mem, Reach = ConstructConfig.Get(p, p.D, CfgKey.Radius, 30), Channel = (int)ConstructConfig.Get(p, p.D, CfgKey.Channel, 1) };
            }
    }

    /// <summary>What a receiver hears (0: nothing in range on its channel).</summary>
    public float RadioHeard(Construct c, int part, ref CPart p, ConstructPartDef d)
    {
        var channel = (int)ConstructConfig.Get(p, d, CfgKey.Channel, 1);
        var at = PartAt(c, p);
        var best = float.MaxValue;
        var heard = 0f;
        for (var k = 0; k < _senderCount; k++)
        {
            ref var s = ref _senders[k];
            var dist = Vector3.DistanceSquared(s.At, at);
            if (s.Channel == channel && dist <= s.Reach * s.Reach && dist < best)
            {
                best = dist;
                heard = s.Value;
            }
        }
        return heard;
    }
}
