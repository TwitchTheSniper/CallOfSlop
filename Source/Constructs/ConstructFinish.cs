namespace CallOfSlop;

/// <summary>
/// The texture paint tool's data (docs/sandbox/CONSTRUCTS.md, "The texture paint tool"). A part's finish
/// (<see cref="CPart.Finish"/>) is a shared material (a row of Materials/Surfaces.json, docs/render/MATERIALS.md) that every
/// box of the part is drawn with in place of its own, glass and glowing boxes aside; 0 is "its own materials". Texture and
/// colour are separate: a finish keeps the part's colour or paint, and the colour paint tool keeps the finish. The row is
/// this build's own number, so it is never saved or sent: saves, blueprints and the wire carry the material's name, and a
/// name this build doesn't have reads as "its own". Per part for now; a per-box finish would be a short list of
/// (box, row) beside the part, with the same names on the wire and in saves, and the tool would aim at the box the
/// crosshair's ray hit (ConstructHit's box). Nothing here allocates on a steady frame.
/// </summary>
public static class ConstructFinish
{
    /// <summary>The longest name a finish may have on the wire (a material's name is at most 32 ASCII characters).</summary>
    public const int MaxName = 32;

    /// <summary>Whether a row may be painted on: a shaded material the file offers (not the old texture layers, not glass).</summary>
    public static bool Offered(int row)
    {
        var rows = Surfaces.File.Rows;
        return row >= Surfaces.FirstShaded && row < rows.Length && rows[row].Offer;
    }

    /// <summary>Whether a box takes a part's finish: every box but glass and the glowing ones.</summary>
    public static bool Takes(in CPartBox box) => !box.IsGlass && !box.Glow;

    /// <summary>Whether any box of the part takes a finish.</summary>
    public static bool Paintable(ConstructPartDef d)
    {
        foreach (var b in d.Boxes)
            if (Takes(in b) && !b.Hidden)
                return true;
        return false;
    }

    /// <summary>The material a box of a part is drawn with: its finish, else its own (under paint, its painted row).</summary>
    public static byte BoxRow(in CPartBox box, uint paint, byte finish) =>
        finish != 0 && Takes(in box) ? finish : Surfaces.OnPaint(box.Surface, paint, box.Paintable);

    /// <summary>A finish's name ("" for none, or a row this build no longer has).</summary>
    public static string Name(byte finish) => finish != 0 && finish < Surfaces.File.Rows.Length ? Surfaces.Row(finish).Name : "";

    /// <summary>A name as a finish: the row, or 0 when it is empty or not an offered material here.</summary>
    public static byte Of(string? name)
    {
        if (string.IsNullOrEmpty(name))
            return 0;
        var id = Surfaces.Id(name);
        return Offered(id) ? id : (byte)0;
    }

    /// <summary>How many parts of a construct carry a finish.</summary>
    public static int Finished(Construct c)
    {
        var n = 0;
        for (var i = 0; i < c.PartCount; i++)
            n += c.Parts[i].Finish != 0 ? 1 : 0;
        return n;
    }

    // ---- The wire (protocol 191) -----------------------------------------------------------------------------------------

    /// <summary>A name as a length byte then its ASCII (no string is made on either side).</summary>
    public static void WriteName(PacketWriter w, byte finish)
    {
        var name = finish != 0 && finish < Surfaces.File.Rows.Length ? Surfaces.Row(finish).Name : "";
        var n = Math.Min(name.Length, MaxName);
        w.U8((byte)n);
        for (var k = 0; k < n; k++)
            w.U8((byte)name[k]);
    }

    /// <summary>Reads <see cref="WriteName"/>: the row here, 0 for none or a name this build doesn't offer.</summary>
    public static byte ReadName(ref PacketReader r)
    {
        int n = r.U8();
        if (n > MaxName)
            throw new MalformedPacketException("a texture name that is too long");
        Span<byte> name = stackalloc byte[MaxName];
        for (var k = 0; k < n; k++)
            name[k] = r.U8();
        if (n == 0)
            return 0;
        var id = Surfaces.Id(name[..n]);
        return Offered(id) ? id : (byte)0;
    }

    /// <summary>A Full message's finish block (its tail): the finished parts, each by name.</summary>
    public static void WriteFull(PacketWriter w, Construct c)
    {
        var at = w.Length;
        w.U16(0);
        var n = 0;
        for (var i = 0; i < c.PartCount; i++)
            if (c.Parts[i].Finish != 0)
            {
                w.U16((ushort)i);
                WriteName(w, c.Parts[i].Finish);
                n++;
            }
        w.PatchU16(at, (ushort)n);
    }

    /// <summary>Reads <see cref="WriteFull"/>'s block (after the parts: every index is checked against them).</summary>
    public static void ReadFull(ref PacketReader r, Construct c)
    {
        int n = r.U16();
        for (var k = 0; k < n; k++)
        {
            int part = r.U16();
            var row = ReadName(ref r);
            if (part >= c.PartCount)
                throw new MalformedPacketException("a texture on a part that isn't there");
            c.Parts[part].Finish = row;
        }
    }

    /// <summary>A batch of finish changes (CBodyKind.Finish): per change the construct, the part and the name ("" for its own). At most 100 a message.</summary>
    public static int WriteChanges(PacketWriter w, ConstructWorld world, List<(ushort Body, ushort Part)> changes, int from)
    {
        w.Begin(Msg.CBody).U8((byte)CBodyKind.Finish);
        var countAt = w.Length;
        w.U8(0);
        var n = 0;
        var i = from;
        for (; i < changes.Count && n < 100; i++)
        {
            var (id, part) = changes[i];
            if (world.Get(id) is not { } c || part >= c.PartCount)
                continue;
            w.U16(id).U16(part);
            WriteName(w, c.Parts[part].Finish);
            n++;
        }
        w.PatchU8(countAt, (byte)n);
        return i;
    }

    /// <summary>Client: a CBodyKind.Finish message (after the kind byte).</summary>
    public static void ReadChanges(ref PacketReader r, ConstructWorld world)
    {
        int n = r.U8();
        for (var k = 0; k < n; k++)
        {
            var id = r.U16();
            int part = r.U16();
            var row = ReadName(ref r);
            if (world.Get(id) is { } c && part < c.PartCount)
                c.Parts[part].Finish = row;
        }
    }
}

public sealed partial class ConstructWorld
{
    /// <summary>Finish changes the network must carry: (construct, part).</summary>
    public readonly List<(ushort Body, ushort Part)> FinishDirty = new(256);

    public void NoteFinish(Construct c, int part)
    {
        var k = (c.Id, (ushort)part);
        var n = FinishDirty.Count;
        if (n > 0 && FinishDirty[n - 1] == k)
            return;
        if (n < 2048)
            FinishDirty.Add(k);
    }
}
