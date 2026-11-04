using System.Numerics;

namespace CallOfSlop;

/// <summary>Ropes and springs in the game (docs/sandbox/CONSTRUCTS.md, "Ropes and springs"): the requests (CActKind.Connector) both ways. The list goes out with the world links (GameSandboxWorldLinks.cs).</summary>
public sealed partial class Game
{
    private static ConnectorEnd ReadEnd(ref PacketReader r) => new() { Body = r.U16(), Part = r.U16(), Local = r.Vec() };

    private string? SandboxConnectorAct(Player p, ref PacketReader r)
    {
        switch (r.U8())
        {
            case 0:
                var kind = (ConnectorKind)r.U8();
                var size = r.U8();
                var a = ReadEnd(ref r);
                var b = ReadEnd(ref r);
                return SandboxHands.ConnectorAdd(Constructs, World.Query, p.Id, p.Eye, kind, size, a, b);
            case 1:
                return SandboxHands.ConnectorDelete(Constructs, p.Id, p.Eye, r.U16());
            case 2:
                var id = r.U16();
                return SandboxHands.ConnectorSet(Constructs, p.Id, p.Eye, id, r.F32(), r.F32(), r.F32(), r.F32());
            default:
                return "no such request";
        }
    }

    /// <summary>Client (or host): tie a rope or spring between two ends (the server checks it all again).</summary>
    public void SandboxSendConnector(ConnectorKind kind, byte size, in ConnectorEnd a, in ConnectorEnd b)
    {
        var w = SbW;
        w.Begin(Msg.CAct).U8((byte)CActKind.Connector).U8(0).U8((byte)kind).U8(size).U16(a.Body).U16(a.Part).Vec(a.Local).U16(b.Body).U16(b.Part).Vec(b.Local);
        SandboxSendBuilt(w);
    }

    public void SandboxSendConnectorDelete(ushort id)
    {
        var w = SbW;
        w.Begin(Msg.CAct).U8((byte)CActKind.Connector).U8(1).U16(id);
        SandboxSendBuilt(w);
    }

    public void SandboxSendConnectorSet(ushort id, float length, float rating, float stiffness, float damping)
    {
        var w = SbW;
        w.Begin(Msg.CAct).U8((byte)CActKind.Connector).U8(2).U16(id).F32(length).F32(rating).F32(stiffness).F32(damping);
        SandboxSendBuilt(w);
    }
}
