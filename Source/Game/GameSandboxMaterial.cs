using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>
/// Sandbox: the material painter in the game (docs/sandbox/CONSTRUCTS.md, "The material painter"). A client asks with
/// <see cref="SandboxMaterial"/> (CActKind.Material: a part, a mode and a material by name); the server checks and swaps the
/// part's type (ConstructEdits.Rematerial) and sends the body again, whose remade parts go by their key (PartMaterials.Write).
/// </summary>
public sealed partial class Game
{
    /// <summary>Client (or the host itself): part <paramref name="index"/> of construct <paramref name="body"/> made of offered material <paramref name="material"/> (Reset: its own).</summary>
    public void SandboxMaterial(ushort body, PaintMode mode, int index, int material)
    {
        var w = SbW;
        w.Begin(Msg.CAct).U8((byte)CActKind.Material).U16(body).U8((byte)mode).U16((ushort)Math.Clamp(index, 0, 65535));
        var all = PartMaterials.Offered;
        PartMaterials.WriteName(w, mode != PaintMode.Reset && (uint)material < (uint)all.Length ? all[material] : "");
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
