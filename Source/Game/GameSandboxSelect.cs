using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>
/// The Selector Station and the Blueprint Desk on the wire (CActKind.Select; docs/sandbox/CONSTRUCTS.md, "Build stations"): a
/// selection goes up in chunks, then what to do with it; a stamp's blueprint goes up as compressed text. The server's checks
/// and its jobs are in ConstructEditsSelect.cs.
/// </summary>
public sealed partial class Game
{
    private enum SelOp : byte { Begin, Chunk, Do, Paste, ClipBegin, ClipChunk }

    /// <summary>The selector's HUD line (what is selected, the keys; empty: none). The tool's frame sets it.</summary>
    public string SandboxSelectHint = "";

    /// <summary>
    /// Why this aid isn't live for this player, or null: the one check every selector and desk action goes through, on the
    /// server and on the client (its text is the look line's). The station framework answers it.
    /// </summary>
    public string? SandboxAidWhy(byte player, BuildAid aid) => null;

    private void SelSend(PacketWriter w)
    {
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

    private PacketWriter SelBegin(SelOp op) => SbW.Begin(Msg.CAct).U8((byte)CActKind.Select).U8((byte)op);

    /// <summary>Client (or host): the selection, in bounded messages. The next <see cref="SandboxSelectDo"/> works on it.</summary>
    public void SandboxSelectSend(Construct c, ReadOnlySpan<ushort> items)
    {
        SelSend(SelBegin(SelOp.Begin).U16(c.Id).U16((ushort)items.Length).U32(ConstructEdits.SelectHash(c, items)));
        for (var from = 0; from < items.Length; from += ConstructEdits.MaxChunk)
        {
            var n = Math.Min(items.Length - from, ConstructEdits.MaxChunk);
            var w = SelBegin(SelOp.Chunk).U16((ushort)n);
            for (var k = 0; k < n; k++)
                w.U16(items[from + k]);
            SelSend(w);
        }
    }

    private static PacketWriter WritePlace(PacketWriter w, in ClipPlace p)
    {
        w.U8((byte)(p.Attach ? 1 : 0));
        if (p.Attach)
            return w.U16(p.Body).U16(p.Part).U8(p.Socket).U16(p.ClipPart).U8(p.ClipSocket).U8(p.Spin);
        return w.Vec(p.Pos).F32(p.Rot.X).F32(p.Rot.Y).F32(p.Rot.Z).F32(p.Rot.W);
    }

    private static ClipPlace ReadPlace(ref PacketReader r)
    {
        var p = new ClipPlace { Attach = r.U8() != 0 };
        if (p.Attach)
        {
            p.Body = r.U16();
            p.Part = r.U16();
            p.Socket = r.U8();
            p.ClipPart = r.U16();
            p.ClipSocket = r.U8();
            p.Spin = r.U8();
        }
        else
        {
            p.Pos = r.Vec();
            p.Rot = new System.Numerics.Quaternion(r.F32(), r.F32(), r.F32(), r.F32());
        }
        return p;
    }

    /// <summary>Client (or host): do this to the selection sent.</summary>
    public void SandboxSelectDo(SelAct act, PaintMode mode = PaintMode.Part, uint colour = 0, in ClipPlace place = default)
    {
        var w = SelBegin(SelOp.Do).U8((byte)act).U8((byte)mode).U8((byte)(colour >> 16)).U8((byte)(colour >> 8)).U8((byte)colour);
        SelSend(WritePlace(w, place));
    }

    /// <summary>Client (or host): place the clip the server holds (the last copy, or the blueprint sent).</summary>
    public void SandboxPaste(in ClipPlace place) => SelSend(WritePlace(SelBegin(SelOp.Paste), place));

    /// <summary>Client (or host): a blueprint's compressed text goes up, in bounded messages: the server's next clip.</summary>
    public void SandboxClipUpload(ReadOnlySpan<byte> data)
    {
        SelSend(SelBegin(SelOp.ClipBegin).U32((uint)data.Length));
        for (var from = 0; from < data.Length; from += ConstructEdits.UploadChunk)
        {
            var n = Math.Min(data.Length - from, ConstructEdits.UploadChunk);
            SelSend(SelBegin(SelOp.ClipChunk).U16((ushort)n).Bytes(data.Slice(from, n)));
        }
    }

    /// <summary>Server: one CActKind.Select.</summary>
    private string? SandboxSelectAct(Player p, System.Numerics.Vector3 eye, ref PacketReader r)
    {
        var hands = SandboxHands;
        switch ((SelOp)r.U8())
        {
            case SelOp.Begin:
            {
                var body = r.U16();
                int total = r.U16();
                return hands.SelectBegin(p.Id, body, total, r.U32());
            }
            case SelOp.Chunk:
                return hands.SelectChunk(p.Id, ref r);
            case SelOp.Do:
            {
                var act = (SelAct)r.U8();
                var mode = (PaintMode)r.U8();
                var colour = ConstructPaint.SetBit | (uint)(r.U8() << 16 | r.U8() << 8 | r.U8());
                var place = ReadPlace(ref r);
                return hands.SelectDo(Constructs, p.Id, eye, act, mode, colour, place);
            }
            case SelOp.Paste:
                return hands.Paste(Constructs, p.Id, eye, ReadPlace(ref r));
            case SelOp.ClipBegin:
                return hands.ClipBegin(p.Id, (int)Math.Min(r.U32(), int.MaxValue));
            case SelOp.ClipChunk:
            {
                int n = r.U16();
                return n > r.Remaining ? "bad blueprint" : hands.ClipChunk(p.Id, r.Take(n));
            }
        }
        return null;
    }
}
