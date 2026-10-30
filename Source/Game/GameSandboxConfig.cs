using System.Numerics;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>
/// Sandbox: the configure tool in the game (docs/sandbox/CONSTRUCTS.md, "The configure tool"). A client asks with
/// <see cref="SandboxConfigure"/> (CActKind.Configure: one or more parts of a kind, the slots and values); the server
/// checks the tool, the reach and who may (decision 19: <see cref="SandboxConfigRefusal"/>), clamps the values and sends
/// the construct's structure again (the settings ride in CBody).
/// </summary>
public sealed partial class Game
{
    /// <summary>The configure tool's HUD line (Program.SandboxConfig.cs sets it).</summary>
    public string SandboxConfigHint = "LMB on a part: its settings. Shift+LMB: select more of the same kind";

    private readonly byte[] _cfgParts = new byte[ConstructEdits.MaxConfigParts];
    private readonly byte[] _cfgSlots = new byte[ConstructConfig.MaxSlots];
    private readonly float[] _cfgValues = new float[ConstructConfig.MaxSlots];

    /// <summary>
    /// Why player <paramref name="player"/> may not configure construct <paramref name="c"/>, or null: their own, nobody's,
    /// a wreck or a loose part; someone else's is refused ("defended" while its owner is alive within
    /// <see cref="ConstructConfig.ContestRadius"/>). Factions don't exist yet: the owner is the faction.
    /// </summary>
    public string? SandboxConfigRefusal(Construct c, byte player)
    {
        if (c.Owner == player || c.Owner == 255 || c.Wreck || c.Loose)
            return null;
        var owner = Players.GetValueOrDefault(c.Owner);
        var near = owner is { Alive: true } && Vector3.Distance(owner.Position, c.WorldCom) <= ConstructConfig.ContestRadius + c.Radius;
        return ConstructConfig.Refusal(c, player, owner?.Name ?? $"player {c.Owner}", near);
    }

    private string? SandboxConfigureAct(Player p, ref PacketReader r, Vector3 eye)
    {
        var body = r.U16();
        int n = r.U8();
        if (n == 0 || n > _cfgParts.Length)
            return "nothing to set";
        for (var k = 0; k < n; k++)
            _cfgParts[k] = r.U8();
        int m = r.U8();
        if (m > _cfgSlots.Length)
            return "too many settings";
        for (var k = 0; k < m; k++)
        {
            _cfgSlots[k] = r.U8();
            _cfgValues[k] = r.F32();
        }
        var refusal = Constructs.Get(body) is { } c ? SandboxConfigRefusal(c, p.Id) : null;
        return SandboxHands.Configure(Constructs, p.Id, eye, body, _cfgParts.AsSpan(0, n), _cfgSlots.AsSpan(0, m), _cfgValues.AsSpan(0, m), refusal);
    }

    /// <summary>
    /// Client (or the host itself): set <paramref name="slots"/> to <paramref name="values"/> on <paramref name="parts"/>
    /// (all of one kind) of construct <paramref name="body"/>. The server decides; a refusal comes back as a notice.
    /// </summary>
    public void SandboxConfigure(ushort body, ReadOnlySpan<byte> parts, ReadOnlySpan<byte> slots, ReadOnlySpan<float> values)
    {
        var n = Math.Min(parts.Length, ConstructEdits.MaxConfigParts);
        var m = Math.Min(Math.Min(slots.Length, values.Length), ConstructConfig.MaxSlots);
        if (n == 0)
            return;
        var w = SbW;
        w.Begin(Msg.CAct).U8((byte)CActKind.Configure).U16(body).U8((byte)n);
        for (var k = 0; k < n; k++)
            w.U8(parts[k]);
        w.U8((byte)m);
        for (var k = 0; k < m; k++)
            w.U8(slots[k]).F32(values[k]);
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
