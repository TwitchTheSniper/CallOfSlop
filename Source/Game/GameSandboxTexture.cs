using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>
/// Sandbox: the texture paint tool in the game (docs/sandbox/CONSTRUCTS.md, "The texture paint tool"). A client asks with
/// <see cref="SandboxTexture"/> (CActKind.Texture: a part, a mode and a material by name); the server checks the tool, the reach
/// and the contested-construct rule (ConstructEdits.Finish) and sends the change to everyone (CBodyKind.Finish).
/// </summary>
public sealed partial class Game
{
    /// <summary>Client (or the host itself): put material <paramref name="finish"/> (a row; Reset: none) on part <paramref name="index"/> of construct <paramref name="body"/>.</summary>
    public void SandboxTexture(ushort body, PaintMode mode, int index, byte finish)
    {
        var w = SbW;
        w.Begin(Msg.CAct).U8((byte)CActKind.Texture).U16(body).U8((byte)mode).U16((ushort)Math.Clamp(index, 0, 65535));
        ConstructFinish.WriteName(w, mode == PaintMode.Reset ? (byte)0 : finish);
        if (IsServer)
        {
            if (Local is { } me)
            {
                var r = new PacketReader(w.Span[1..]);
                if (SandboxActGuarded(me, ref r) is { } why)
                    SandboxSay(why);
            }
            return;
        }
        Net.SendToServer(w.Span, PacketReliability.ReliableOrdered);
    }
}
