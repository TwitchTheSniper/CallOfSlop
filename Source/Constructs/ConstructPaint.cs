using System.Numerics;

namespace CallOfSlop;

/// <summary>What one stroke of the paint tool does (the tool wheel's modes, <see cref="SandboxPaintWheel"/>).</summary>
public enum PaintMode : byte
{
    Part, // the aimed part (or link)
    Whole, // every part and link of the aimed construct
    Reset, // back to the part's (or link's) own colour
}

/// <summary>
/// The paint tool's data (board item dev-sandbox-paint-tool-pp01, decision 29; docs/sandbox/CONSTRUCTS.md, "The paint
/// tool"). A colour is a <c>uint</c>: 0 is "not painted" (the part draws its own colours), else <see cref="SetBit"/> |
/// 0xRRGGBB. <see cref="CPart.Paint"/> and <see cref="CRoute.Paint"/> hold it; it moves with the part or link (compaction,
/// splits, joins copy the structs). Painting replaces the colour of a part's paintable boxes (<see cref="CPartBox.Paintable"/>:
/// everything but glass and a wheel's tyre unless its file says <c>"paintable": false</c> / <c>true</c>), keeping each
/// box's tone relative to the part's main colour (<see cref="CPartBox.Tone"/>), so a hinge's dark pin stays darker than
/// its plate. Nothing here allocates.
/// </summary>
public static class ConstructPaint
{
    public const uint SetBit = 0x01000000;
    public const uint RgbMask = 0x00FFFFFF;

    /// <summary>The colour of a swatch on the paint tool's ring.</summary>
    public static uint Rgb(byte r, byte g, byte b) => SetBit | (uint)(r << 16 | g << 8 | b);

    public static bool IsSet(uint paint) => (paint & SetBit) != 0;

    /// <summary>A colour as read off the wire or a file: the set bit, no other high bits.</summary>
    public static bool Valid(uint paint) => paint == 0 || (paint & ~(SetBit | RgbMask)) == 0 && (paint & SetBit) != 0;

    public static Vector3 ToVector(uint paint) =>
        new(((paint >> 16) & 255) / 255f, ((paint >> 8) & 255) / 255f, (paint & 255) / 255f);

    public static uint FromVector(Vector3 v) =>
        Rgb((byte)Math.Clamp((int)MathF.Round(v.X * 255f), 0, 255), (byte)Math.Clamp((int)MathF.Round(v.Y * 255f), 0, 255), (byte)Math.Clamp((int)MathF.Round(v.Z * 255f), 0, 255));

    /// <summary>"#rrggbb" (a blueprint's "paint"); an unpainted colour gives "".</summary>
    public static string ToHex(uint paint) => IsSet(paint) ? $"#{paint & RgbMask:x6}" : "";

    /// <summary>A "#rrggbb" text as a colour (false when it isn't one).</summary>
    public static bool TryParse(string? text, out uint paint)
    {
        paint = 0;
        if (text is not { Length: 7 } || text[0] != '#' || !uint.TryParse(text.AsSpan(1), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var rgb))
            return false;
        paint = SetBit | rgb;
        return true;
    }

    private static float Luma(Vector3 c) => 0.299f * c.X + 0.587f * c.Y + 0.114f * c.Z;

    /// <summary>A box's tone against the part's main colour (how much lighter or darker its own colour is), kept in a modest range.</summary>
    public static float ToneOf(Vector3 box, Vector3 main) =>
        Math.Clamp(Luma(box) / MathF.Max(Luma(main), 0.04f), 0.65f, 1.3f);

    /// <summary>The colour a box of a part is drawn in: the paint (in its tone) on a paintable box of a painted part, else the box's own.</summary>
    public static Vector3 BoxColour(uint paint, in CPartBox box)
    {
        if (paint == 0 || !box.Paintable)
            return box.Color;
        var v = ToVector(paint) * box.Tone;
        return new Vector3(MathF.Min(v.X, 1f), MathF.Min(v.Y, 1f), MathF.Min(v.Z, 1f));
    }

    /// <summary>Whether any box of the part takes paint (a pane of glass, a tyre alone, doesn't).</summary>
    public static bool Paintable(ConstructPartDef d)
    {
        foreach (var b in d.Boxes)
            if (b.Paintable && !b.Hidden)
                return true;
        return false;
    }

    /// <summary>The part's own main colour (its first paintable solid box): what the eyedropper picks off an unpainted part.</summary>
    public static uint MainColour(ConstructPartDef d)
    {
        foreach (var b in d.Boxes)
            if (b.Paintable && !b.Hidden)
                return FromVector(b.Color);
        return FromVector(d.Boxes[0].Color);
    }

    /// <summary>What the eyedropper takes off a part: its paint, else its own main colour.</summary>
    public static uint Sample(in CPart p) => IsSet(p.Paint) ? p.Paint : MainColour(p.D);

    /// <summary>The swatches on the paint tool's ring: orange, black, white, grey, rust, olive, sand, red, blue and yellow.</summary>
    public static readonly uint[] Swatches =
    {
        Rgb(240, 150, 40), Rgb(26, 26, 28), Rgb(232, 232, 226), Rgb(122, 126, 132), Rgb(158, 80, 44),
        Rgb(92, 102, 58), Rgb(200, 176, 126), Rgb(190, 40, 36), Rgb(52, 98, 170), Rgb(232, 196, 44),
    };

    public static readonly string[] SwatchNames =
        { "Orange", "Black", "White", "Grey", "Rust", "Olive", "Sand", "Red", "Blue", "Yellow" };

    /// <summary>How many parts and links of a construct carry paint (the self-test, the HUD).</summary>
    public static int Painted(Construct c)
    {
        var n = 0;
        for (var i = 0; i < c.PartCount; i++)
            n += c.Parts[i].Paint != 0 ? 1 : 0;
        for (var k = 0; k < c.RouteCount; k++)
            n += c.Routes[k].Paint != 0 ? 1 : 0;
        return n;
    }

    // ---- The wire (protocol 98) --------------------------------------------------------------------------------------------

    /// <summary>A Full message's paint block (its tail): the painted parts, then the painted links.</summary>
    public static void WriteFull(PacketWriter w, Construct c)
    {
        var at = w.Length;
        w.U8(0);
        var n = 0;
        for (var i = 0; i < c.PartCount; i++)
            if (c.Parts[i].Paint != 0)
            {
                w.U8((byte)i).U8((byte)(c.Parts[i].Paint >> 16)).U8((byte)(c.Parts[i].Paint >> 8)).U8((byte)c.Parts[i].Paint);
                n++;
            }
        w.PatchU8(at, (byte)n);
        at = w.Length;
        w.U8(0);
        n = 0;
        for (var k = 0; k < c.RouteCount; k++)
            if (c.Routes[k].Paint != 0)
            {
                w.U8((byte)k).U8((byte)(c.Routes[k].Paint >> 16)).U8((byte)(c.Routes[k].Paint >> 8)).U8((byte)c.Routes[k].Paint);
                n++;
            }
        w.PatchU8(at, (byte)n);
    }

    /// <summary>Reads <see cref="WriteFull"/>'s block (after the parts and links: every index is checked against them).</summary>
    public static void ReadFull(ref PacketReader r, Construct c)
    {
        int n = r.U8();
        for (var k = 0; k < n; k++)
        {
            int part = r.U8();
            var rgb = (uint)(r.U8() << 16 | r.U8() << 8 | r.U8());
            if (part >= c.PartCount)
                throw new MalformedPacketException("paint on a part that isn't there");
            c.Parts[part].Paint = SetBit | rgb;
        }
        n = r.U8();
        for (var k = 0; k < n; k++)
        {
            int route = r.U8();
            var rgb = (uint)(r.U8() << 16 | r.U8() << 8 | r.U8());
            if (route >= c.RouteCount)
                throw new MalformedPacketException("paint on a link that isn't there");
            c.Routes[route].Paint = SetBit | rgb;
        }
    }

    /// <summary>A batch of paint changes (CBodyKind.Paint): per change the construct, a kind byte (1 a link, 2 painted) and the index, then the colour when painted. At most 150 a message.</summary>
    public static int WriteChanges(PacketWriter w, ConstructWorld world, List<(ushort Body, ushort Key)> changes, int from)
    {
        w.Begin(Msg.CBody).U8((byte)CBodyKind.Paint);
        var countAt = w.Length;
        w.U8(0);
        var n = 0;
        var i = from;
        for (; i < changes.Count && n < 150; i++)
        {
            var (id, key) = changes[i];
            var route = (key & RouteKey) != 0;
            var index = key & 0xFF;
            if (world.Get(id) is not { } c || index >= (route ? c.RouteCount : c.PartCount))
                continue;
            var paint = route ? c.Routes[index].Paint : c.Parts[index].Paint;
            w.U16(id).U8((byte)((route ? 1 : 0) | (paint != 0 ? 2 : 0))).U8((byte)index);
            if (paint != 0)
                w.U8((byte)(paint >> 16)).U8((byte)(paint >> 8)).U8((byte)paint);
            n++;
        }
        w.PatchU8(countAt, (byte)n);
        return i;
    }

    /// <summary>A change's key for a link (a part's key is its index).</summary>
    public const ushort RouteKey = 0x100;

    /// <summary>Client: a CBodyKind.Paint message (after the kind byte).</summary>
    public static void ReadChanges(ref PacketReader r, ConstructWorld world)
    {
        int n = r.U8();
        for (var k = 0; k < n; k++)
        {
            var id = r.U16();
            var flags = r.U8();
            int index = r.U8();
            var paint = 0u;
            if ((flags & 2) != 0)
                paint = SetBit | (uint)(r.U8() << 16 | r.U8() << 8 | r.U8());
            if (flags > 3)
                throw new MalformedPacketException("a paint change that isn't one");
            if (world.Get(id) is not { } c)
                continue;
            if ((flags & 1) != 0)
            {
                if (index < c.RouteCount)
                    c.Routes[index].Paint = paint;
            }
            else if (index < c.PartCount)
                c.Parts[index].Paint = paint;
        }
    }
}

public sealed partial class ConstructWorld
{
    /// <summary>Paint changes the network must carry: (construct, part index, or <see cref="ConstructPaint.RouteKey"/> | link index).</summary>
    public readonly List<(ushort Body, ushort Key)> PaintDirty = new(256);

    public void NotePaint(Construct c, int key)
    {
        var k = (c.Id, (ushort)key);
        var n = PaintDirty.Count;
        if (n > 0 && PaintDirty[n - 1] == k)
            return;
        if (n < 2048)
            PaintDirty.Add(k);
    }
}
