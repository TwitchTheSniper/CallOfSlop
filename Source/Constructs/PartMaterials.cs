using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Parts of another material (the material painter; docs/sandbox/CONSTRUCTS.md, "The material painter"). A part file says what
/// its part is made of (<see cref="ConstructPartDef.Material"/>: steel, wood, stone, concrete...). The painter remakes a part in
/// another bulk material of Extrusion.json's "painter" list: the same shape, sockets and links, a <see cref="ConstructPartDef"/>
/// made at run time (as an extrusion is) whose mass, health and armour scale by the two materials' rows, which salvages as the
/// new row's class and is drawn in its colour and surface. Remade in its own material it is the file's part again.
/// <para>
/// Its key is its name, "Plate Steel 0.5 m (wood)": saves and blueprints keep that, the wire the file part's id and the
/// material's name (never a row number), and its id is this machine's own (<see cref="Extrusion.Adopt"/>). A remade part is
/// always physics-only (never an item: an inventory or a store would have to carry its key). It counts as plain structure
/// when its file part does (journalled rebuilds, fused units).
/// </para>
/// </summary>
public static class PartMaterials
{
    public const int MaxName = 24, MaxMaterials = 32;
    internal const byte WireKind = 246; // (after Extrusion.WireMark: the file part's id, then the material's name)

    private static readonly Dictionary<(ushort Base, string Material), ConstructPartDef> Interned = new();
    private static readonly object Gate = new();

    /// <summary>The materials the painter offers, in the file's order (names of Recycling.json materials the game has).</summary>
    public static string[] Offered
    {
        get
        {
            var t = ExtrusionTable.Current;
            if (!ReferenceEquals(_offeredOf, t))
            {
                var names = RecyclingTable.Current;
                _offered = t.PainterMaterials.Where(m => names.IdOf(m) != 0).ToArray();
                _offeredOf = t;
            }
            return _offered;
        }
    }

    private static string[] _offered = [];
    private static ExtrusionTable? _offeredOf;

    /// <summary>Where a material sits in <see cref="Offered"/> (-1: not offered).</summary>
    public static int IndexOf(string material) => Array.IndexOf(Offered, material);

    /// <summary>The same for a name read off the wire (ASCII bytes), with nothing made.</summary>
    public static int IndexOf(ReadOnlySpan<byte> name)
    {
        var all = Offered;
        for (var k = 0; k < all.Length; k++)
        {
            var m = all[k];
            if (m.Length != name.Length)
                continue;
            var same = true;
            for (var i = 0; i < m.Length && same; i++)
                same = m[i] == name[i];
            if (same)
                return k;
        }
        return -1;
    }

    /// <summary>The file part a part was remade from (itself for a file part).</summary>
    public static ConstructPartDef BaseOf(ConstructPartDef d) => d.MatBase ?? d;

    /// <summary>The bulk material a part is made of: a remade part's, else its file's own "material".</summary>
    public static string MaterialOf(ConstructPartDef d) => d.MatBase != null ? d.MatName : d.Material;

    private enum Why : byte { Ok, Machine, Kind, Material }

    private static Why Check(ConstructPartDef d)
    {
        var b = BaseOf(d);
        var t = ExtrusionTable.Current;
        if (b.RuntimeMade || b.Id >= ConstructParts.MaxTypes)
            return Why.Machine;
        if (Array.IndexOf(t.PainterRoles, b.Role) < 0 || b.HasGlass || b.Explosive != null || b.Yield > 0 || b.Core || b.HasJoint)
            return Why.Kind;
        if (t.FindByName(b.Material) == null || IndexOf(b.Material) < 0)
            return Why.Material;
        return Why.Ok;
    }

    /// <summary>Why the painter can't remake this kind of part, or null.</summary>
    public static string? Refusal(ConstructPartDef d) => Check(d) switch
    {
        Why.Machine => $"a {BaseOf(d).Name} is made by its machine, of its own material",
        Why.Kind => $"a {BaseOf(d).Name} can't be made of anything else",
        Why.Material => $"a {BaseOf(d).Name} is made of {BaseOf(d).Material}, which the material painter doesn't work",
        _ => null,
    };

    /// <summary>Can the painter remake this part at all (the aim's highlight; nothing is made)?</summary>
    public static bool Paintable(ConstructPartDef d) => Check(d) == Why.Ok;

    /// <summary>
    /// Part <paramref name="d"/> made of offered material <paramref name="k"/>: the file part itself in its own material, else
    /// the remade one (made the first time it is asked for). Null: it can't be (<see cref="Refusal"/>), or the run-time table is full.
    /// </summary>
    public static ConstructPartDef? Variant(ConstructPartDef d, int k)
    {
        var all = Offered;
        if ((uint)k >= (uint)all.Length || !Paintable(d))
            return null;
        var b = BaseOf(d);
        var material = all[k];
        if (material == b.Material)
            return b;
        lock (Gate)
        {
            if (Interned.TryGetValue((b.Id, material), out var have) && ReferenceEquals(have.MatBase, b))
            {
                have.Touched = Environment.TickCount64;
                return have;
            }
            var v = Make(b, material);
            if (v == null || !Extrusion.Adopt(v))
                return null;
            Interned[(b.Id, material)] = v;
            return v;
        }
    }

    /// <summary>(RuntimeParts) Drops the doomed parts from the key table.</summary>
    internal static void Forget(Func<ConstructPartDef, bool> doomed)
    {
        lock (Gate)
            RuntimeParts.Drop(Interned, doomed);
    }

    /// <summary>The key a remade part goes by: its file part's name and its material.</summary>
    public static string KeyOf(ConstructPartDef b, string material) => $"{b.Name} ({material})";

    /// <summary>The remade part a key names ("Plate Steel 0.5 m (wood)"), or null when it isn't one.</summary>
    public static ConstructPartDef? Parse(string name)
    {
        if (name.Length < 5 || name[^1] != ')')
            return null;
        var open = name.LastIndexOf(" (", StringComparison.Ordinal);
        if (open <= 0)
            return null;
        var k = IndexOf(name[(open + 2)..^1]);
        if (k < 0 || ConstructParts.Find(name[..open]) is not { } b || b.MatBase != null)
            return null;
        return Variant(b, k);
    }

    private static ConstructPartDef? Make(ConstructPartDef b, string material)
    {
        var t = ExtrusionTable.Current;
        var names = RecyclingTable.Current;
        if (t.FindByName(b.Material) is not { } from || t.FindByName(material) is not { } to)
            return null;
        var kgFrom = from.UnitsPerM3 * names.WeightOf(names.IdOf(from.Material));
        var kgTo = to.UnitsPerM3 * names.WeightOf(names.IdOf(material));
        var v = b.ShallowCopy();
        v.MatBase = b;
        v.MatName = material;
        v.Name = KeyOf(b, material);
        v.Description = $"{b.Description} Made of {material}.";
        v.Material = to.Class; // (salvage, sounds, burning: by the row's class)
        v.Mass = Math.Clamp(b.Mass * kgTo / MathF.Max(kgFrom, 0.001f), 0.05f, ConstructParts.MaxPartMass);
        v.Health = Math.Clamp(b.Health * to.HealthPerM3 / MathF.Max(from.HealthPerM3, 1f), 1f, ConstructParts.MaxPartHealth);
        v.Armour = from.ArmourPerCm > 0 ? MathF.Round(MathF.Min(to.MaxArmour, b.Armour * to.ArmourPerCm / from.ArmourPerCm), 1) : MathF.Min(to.MaxArmour, b.Armour);
        v.PhysicsOnly = true;
        v.PhysicsWhy = "made of another material";
        v.SurfaceOverride = 0;
        v.Hulls = null;
        v.HullCovers = null;
        v.ExHash = Fnv(v.Name);
        // The look: its plain boxes in the material's colour and surface (bolts and trim keep theirs); glass never changes.
        var colour = names.ColourOf(names.IdOf(material));
        var surface = SurfaceRules.OfBulk(material);
        var boxes = (CPartBox[])b.Boxes.Clone();
        for (var i = 0; i < boxes.Length; i++)
        {
            ref readonly var x = ref boxes[i];
            if (x.Detail || x.IsGlass || x.Hidden || !x.Paintable)
                continue;
            boxes[i] = x with { Color = colour, Surface = surface };
        }
        v.Boxes = boxes;
        SurfaceRules.Apply(v); // (a material with no surface of its own: the rules pick one by its class)
        return v;
    }

    private static uint Fnv(string s)
    {
        var h = 2166136261u;
        foreach (var ch in s)
            h = (h ^ ch) * 16777619u;
        return h;
    }

    // ---- Survival: what remaking a part costs -------------------------------------------------------------------------

    /// <summary>The units of its bulk material a part takes to make: its mass over the material's kg a unit, rounded up.</summary>
    public static int UnitsFor(ConstructPartDef d)
    {
        var names = RecyclingTable.Current;
        var id = names.IdOf(MaterialOf(d));
        return id == 0 ? 0 : Math.Max(1, (int)MathF.Ceiling(d.Mass / MathF.Max(names.WeightOf(id), 0.001f) - 1e-3f));
    }

    // ---- The wire --------------------------------------------------------------------------------------------------

    /// <summary>After <see cref="Extrusion.WireMark"/> and <see cref="WireKind"/>: the file part's id and the material's name (ASCII).</summary>
    public static void Write(PacketWriter w, ConstructPartDef d)
    {
        w.U16(d.MatBase!.Id);
        WriteName(w, d.MatName);
    }

    public static void WriteName(PacketWriter w, string name)
    {
        var n = Math.Min(name.Length, MaxName);
        w.U8((byte)n);
        for (var i = 0; i < n; i++)
            w.U8((byte)name[i]);
    }

    /// <summary>A material's name off the wire, as its place in <see cref="Offered"/> (-1: one this build doesn't offer). Nothing is made.</summary>
    public static int ReadName(ref PacketReader r)
    {
        int n = r.U8();
        if (n > MaxName)
            throw new MalformedPacketException("a material name that is too long");
        Span<byte> name = stackalloc byte[MaxName];
        for (var i = 0; i < n; i++)
            name[i] = r.U8();
        return IndexOf(name[..n]);
    }

    /// <summary>Reads <see cref="Write"/> as this machine's part id.</summary>
    public static ushort Read(ref PacketReader r)
    {
        var id = r.U16();
        var k = ReadName(ref r);
        if (ConstructParts.Get(id) is not { } b || b.Id >= ConstructParts.MaxTypes || b.MatBase != null)
            throw new MalformedPacketException($"a part of another material from part {id}");
        if (k >= 0 && Variant(b, k) is { } v)
            return v.Id;
        return b.Id; // (a material this build doesn't have, or the run-time table full: the file part, never a dropped packet)
    }
}
