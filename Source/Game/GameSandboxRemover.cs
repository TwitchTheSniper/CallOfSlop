using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>The remover tool in the game (docs/sandbox/CONSTRUCTS.md, "The remover tool"): the request that carries a confirmed selection to the server.</summary>
public sealed partial class Game
{
    /// <summary>The remover's question next to the crosshair ("Delete 3 parts? Right-click again to confirm"; empty: none). The tool's frame sets it.</summary>
    public string SandboxPrompt = "";

    /// <summary>The remover's HUD line (what is selected, the keys).</summary>
    public string SandboxRemoverHint = "";

    /// <summary>Client (or the host itself): delete these parts and links. The server checks the tool, the reach and who may (ConstructEdits.Remover); a refusal comes back as a notice.</summary>
    public void SandboxRemove(ReadOnlySpan<RemoveTarget> targets)
    {
        var n = Math.Min(targets.Length, ConstructEdits.MaxRemoveTargets);
        var w = SbW;
        w.Begin(Msg.CAct).U8((byte)CActKind.Remover).U16((ushort)n);
        for (var k = 0; k < n; k++)
            w.U16(targets[k].Body).U8((byte)(targets[k].Route ? 1 : 0)).U8(targets[k].Index);
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
