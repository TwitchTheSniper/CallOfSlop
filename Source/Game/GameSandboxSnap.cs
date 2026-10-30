namespace CallOfSlop;

/// <summary>Snappy building on the wire (CActKind.Snap; docs/sandbox/CONSTRUCTS.md, "Snappy building").</summary>
public sealed partial class Game
{
    public const byte SnapUndo = 0, SnapQuickOff = 1, SnapQuickOn = 2, SnapPick = 3, SnapTake = 4;

    /// <summary>Server: one snappy-building request. Returns why it was refused, or null.</summary>
    private string? SandboxSnapAct(Player p, ref PacketReader r, System.Numerics.Vector3 eye)
    {
        var op = r.U8();
        switch (op)
        {
            case SnapUndo:
                return SandboxHands.UndoPlaced(Constructs, p.Id, eye);
            case SnapQuickOff:
            case SnapQuickOn:
                SandboxHands.SetQuick(p.Id, op == SnapQuickOn);
                return null;
            case SnapPick:
            {
                var body = r.U16();
                var part = r.U8();
                return SandboxHands.PickPart(Constructs, p.Id, eye, body, part);
            }
            case SnapTake:
                return ConstructParts.Get(r.U16()) is { } def ? SandboxHands.TakeKind(p.Id, def) : "no such part";
        }
        return "bad snap request";
    }

    /// <summary>Client (or host): a snappy-building request (undo, quick building on or off, pick the part at <paramref name="part"/> of construct <paramref name="body"/>).</summary>
    public void SandboxSendSnap(byte op, ushort body = 0, byte part = 0)
    {
        var w = SbW;
        w.Begin(Msg.CAct).U8((byte)CActKind.Snap).U8(op);
        if (op == SnapPick)
            w.U16(body).U8(part);
        else if (op == SnapTake)
            w.U16(body); // (the part kind)
        SandboxSendBuilt(w);
    }

    /// <summary>The quick-building choice last sent in this match (-1: none yet), so it is sent once and again after a new match.</summary>
    public int SnapQuickSent = -1;
}
