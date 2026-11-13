namespace CallOfSlop;

/// <summary>
/// "A part, catalogue or run-time" for the wire, and the name a player reads (docs/sandbox/design/inventory-run-time-parts.md). A
/// catalogue part travels as its id (a U16); a run-time part (an extrusion, wheel, pressed, lofted or cut part, an ordnance design,
/// a rack, a part of another material: <see cref="ConstructPartDef.RuntimeMade"/>) as <see cref="Extrusion.WireMark"/> and its key,
/// because its id is this machine's own (CLAUDE.md "Extrusions"). In a save every part already goes by name (<see cref="WorldNames"/>),
/// and a run-time part's name is its key. Stores, conveyors and orders use <see cref="Write"/>/<see cref="Read"/> (protocol 244); the
/// hands use the cached form (<see cref="WriteCached"/>, protocol 292), sent several times a second, which allocates nothing while
/// a hand holds the same part.
/// </summary>
public static class PartRef
{
    /// <summary>A part's id, or a run-time part's key.</summary>
    public static void Write(PacketWriter w, ushort def)
    {
        if (def != ConstructParts.None && Extrusion.IsRuntimeId(def) && ConstructParts.Get(def) is { } d)
            Extrusion.Write(w, d);
        else
            w.U16(def);
    }

    /// <summary><see cref="Write"/>'s bytes as this machine's part id (an unknown key: a stand-in, as a body's part is).</summary>
    public static ushort Read(ref PacketReader r)
    {
        var def = r.U16();
        return def == Extrusion.WireMark ? Extrusion.Read(ref r) : def;
    }

    /// <summary>One holder's last key (a hand): its bytes, so the same part is written and read again without building or parsing its key.</summary>
    public sealed class Cache
    {
        internal readonly byte[] Bytes = new byte[256];
        internal int Length = -1;
        internal ushort Id = ConstructParts.None;
        internal ConstructPartDef? Def;

        public void Clear()
        {
            Length = -1;
            Id = ConstructParts.None;
            Def = null;
        }
    }

    /// <summary>
    /// A part as <see cref="Write"/> writes it, but a run-time part's key as a length (a byte) and its bytes, kept in
    /// <paramref name="cache"/> while the part is the same (the server's side: one cache a hand).
    /// </summary>
    public static void WriteCached(PacketWriter w, ushort def, Cache cache)
    {
        if (def == ConstructParts.None || !Extrusion.IsRuntimeId(def) || ConstructParts.Get(def) is not { } d)
        {
            w.U16(def);
            return;
        }
        if (cache.Id != def || !ReferenceEquals(cache.Def, d) || cache.Length < 0)
        {
            var scratch = new PacketWriter(512); // (only when the hand's part changes)
            Extrusion.Write(scratch, d);
            var key = scratch.Span;
            if (key.Length > cache.Bytes.Length - 1)
            {
                w.U16(ConstructParts.None); // (no key is this long: MaxRuntimeName; nothing rather than a wrong part)
                return;
            }
            key.CopyTo(cache.Bytes);
            cache.Length = key.Length;
            cache.Id = def;
            cache.Def = d;
        }
        w.U16(Extrusion.WireMark).U8((byte)cache.Length).Bytes(cache.Bytes.AsSpan(0, cache.Length));
    }

    /// <summary><see cref="WriteCached"/>'s bytes as this machine's part id; the same key as last time (the client's side: one cache a hand) is not parsed again.</summary>
    public static ushort ReadCached(ref PacketReader r, Cache cache)
    {
        var def = r.U16();
        if (def != Extrusion.WireMark)
            return def;
        int n = r.U8();
        var key = r.Take(n);
        if (cache.Length == n && key.SequenceEqual(cache.Bytes.AsSpan(0, n)) && cache.Def is { Reclaimed: false } same && ConstructParts.Get(cache.Id) == same)
            return cache.Id;
        var sub = new PacketReader(key);
        if (sub.U16() != Extrusion.WireMark)
            throw new MalformedPacketException("a part's key without its mark");
        var id = Extrusion.Read(ref sub);
        key.CopyTo(cache.Bytes);
        cache.Length = n;
        cache.Id = id;
        cache.Def = ConstructParts.Get(id);
        return id;
    }

    /// <summary>The name a player reads: a piece's label ("Steel I-beam 0.1 x 0.1 m"), an ordnance design's description, else the part's name.</summary>
    public static string Shown(ConstructPartDef d) =>
        d.IsOrdnance && d.Description.Length > 0 ? d.Description
        : d.IsExtrusion ? Extrusion.Label(d.ExKey)
        : Pressed.IsName(d.Name) ? Pressed.Label(d.Name)
        : d.RuntimeMade ? HullLoft.PanelLabel(d.Name) // (a formed or cut panel's label; any other made name as it is, cut short)
        : d.Name;

    /// <summary>
    /// A made part's line in a list (the Tab inventory's "Made parts", phase 2 of inventory-run-time-parts.md): <see cref="Shown"/>, with a
    /// straight piece's length, since pieces of one section differ only by it.
    /// </summary>
    public static string Listed(ConstructPartDef d) =>
        d.IsExtrusion && d.ExKey.Shape is ExShape.Section or ExShape.Round or ExShape.Pipe ? $"{Extrusion.Label(d.ExKey)}, {d.ExKey.L / 100f:0.##} m long" : Shown(d);

    /// <summary>A made part's kind, for sorting a list by kind then size: the extrusion's shape, else what made it.</summary>
    public static int KindOrder(ConstructPartDef d) =>
        d.IsExtrusion ? (int)d.ExKey.Shape
        : Pressed.IsName(d.Name) ? 10
        : d.IsOrdnance ? 30
        : d.IsRackRuntime ? 40
        : d.MatBase != null ? 50
        : 20; // (formed, lofted and cut panels)

    /// <summary>The name of <see cref="KindOrder"/>'s kind, for a list's subheading.</summary>
    public static string KindName(ConstructPartDef d) => d.IsExtrusion
        ? d.ExKey.Shape switch
        {
            ExShape.Round => "Rounds",
            ExShape.Pipe => "Pipes",
            ExShape.Wheel => "Wheels",
            ExShape.Fan => "Fans and rotors",
            ExShape.Blade => "Blades",
            ExShape.Wing => "Wings",
            _ => "Beams and sections",
        }
        : Pressed.IsName(d.Name) ? "Pressed parts"
        : d.IsOrdnance ? "Ordnance"
        : d.IsRackRuntime ? "Racks"
        : d.MatBase != null ? "Parts of another material"
        : "Panels and lofted parts";

    /// <summary>A made part's size, for sorting (its bounds' volume, m3).</summary>
    public static float SizeOf(ConstructPartDef d)
    {
        var e = d.BoundsMax - d.BoundsMin;
        return e.X * e.Y * e.Z;
    }

    /// <summary><see cref="Shown"/> by id (null: no such part).</summary>
    public static string? Shown(ushort def) => ConstructParts.Get(def) is { } d ? Shown(d) : null;
}
