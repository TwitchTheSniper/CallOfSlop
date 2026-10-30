using System.Numerics;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>
/// Sandbox: the paint tool in the game (docs/sandbox/CONSTRUCTS.md, "The paint tool"). A client asks with
/// <see cref="SandboxPaint"/> (CActKind.Paint: a part or link, a mode and a colour); the server checks the tool, the reach and
/// the contested-construct rule (ConstructEdits.Paint) and sends the change to everyone (CBodyKind.Paint).
/// </summary>
public sealed partial class Game
{
    /// <summary>Client (or the host itself): paint part (or, with <paramref name="route"/>, link) <paramref name="index"/> of construct <paramref name="body"/>.</summary>
    public void SandboxPaint(ushort body, PaintMode mode, bool route, int index, uint colour)
    {
        var w = SbW;
        w.Begin(Msg.CAct).U8((byte)CActKind.Paint).U16(body).U8((byte)((byte)mode | (route ? 4 : 0))).U8((byte)Math.Clamp(index, 0, 255))
            .U8((byte)(colour >> 16)).U8((byte)(colour >> 8)).U8((byte)colour);
        if (IsServer)
        {
            if (Local is { } me)
            {
                var r = new PacketReader(w.Span[1..]);
                if (SandboxAct(me, ref r, SandboxNow) is { } why)
                    SandboxSay(why);
            }
            return;
        }
        Net.SendToServer(w.Span, PacketReliability.ReliableOrdered);
    }
}
