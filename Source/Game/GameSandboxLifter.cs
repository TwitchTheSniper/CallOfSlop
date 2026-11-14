using System.Numerics;

namespace CallOfSlop;

/// <summary>Sandbox: a lifter's hand orders (ConstructLifterOrders.cs; docs/sandbox/design/arms-and-cranes.md section 5 "By hand").</summary>
public sealed partial class Game
{
    /// <summary>Server: CActKind.Lifter.</summary>
    private string? SandboxLifterAct(ref PacketReader r, Player p, Vector3 eye)
    {
        var body = r.U16();
        var part = r.U16();
        var op = r.U8();
        var id = r.U8();
        var def = ConstructStorage.ReadDef(ref r);
        var count = r.U8();
        var from = new LiftPoint { Body = r.U16(), Part = r.U16(), Kind = (LiftPointKind)r.U8(), Bay = r.U8() };
        var to = new LiftPoint { Body = r.U16(), Part = r.U16(), Kind = (LiftPointKind)r.U8(), Bay = r.U8() };
        if (op == (byte)LiftOrderOp.Grab)
            return SandboxHands.CabGrab(Constructs, p.Id, body, part); // (phase 8: F in a Lifter Cab)
        if (op > (byte)LiftOrderOp.Grab || (byte)from.Kind > (byte)LiftPointKind.Tray || (byte)to.Kind > (byte)LiftPointKind.Tray)
            return "a bad action";
        return SandboxHands.LifterAct(Constructs, p.Id, eye, body, part, (LiftOrderOp)op, id, def, count, from, to, Constructs.Get(body) is { } c ? SandboxConfigRefusal(c, p.Id) : null);
    }

    /// <summary>Client (or the host itself): a hand order at a lifter. The server checks reach and who may, and says why not.</summary>
    public void SandboxLifter(ushort body, int part, LiftOrderOp op, byte id = 0, ushort def = 0, int count = 0, LiftPoint from = default, LiftPoint to = default)
    {
        var w = SbW;
        w.Begin(Msg.CAct).U8((byte)CActKind.Lifter).U16(body).U16((ushort)part).U8((byte)op).U8(id);
        ConstructStorage.WriteDef(w, def);
        w.U8((byte)Math.Clamp(count, 0, 255));
        w.U16(from.Body).U16(from.Part).U8((byte)from.Kind).U8(from.Bay);
        w.U16(to.Body).U16(to.Part).U8((byte)to.Kind).U8(to.Bay);
        SandboxCraftSend(w);
    }
}
