using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>
/// Sandbox: the fuse tool in the game (docs/sandbox/CONSTRUCTS.md, "Fusing"). A client asks with <see cref="SandboxFuse"/>
/// (CActKind.Fuse: a construct, the aimed part, a mode and for <see cref="FuseMode.Parts"/> the selection's parts); the server
/// checks it (ConstructEdits.Fuse), sets the tags and sends the construct to everyone as a structure change.
/// </summary>
public sealed partial class Game
{
    /// <summary>Client (or the host itself): the fuse tool on construct <paramref name="body"/>, aimed at <paramref name="index"/>.</summary>
    public void SandboxFuse(ushort body, FuseMode mode, int index, ReadOnlySpan<ushort> parts)
    {
        var w = SbW;
        var n = Math.Min(parts.Length, ConstructEdits.MaxFuseParts);
        w.Reserve(16 + n * 2);
        w.Begin(Msg.CAct).U8((byte)CActKind.Fuse).U16(body).U8((byte)mode).U16((ushort)Math.Clamp(index, 0, 65535)).U16((ushort)n);
        for (var k = 0; k < n; k++)
            w.U16(parts[k]);
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
