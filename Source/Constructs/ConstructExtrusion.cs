using System.Globalization;
using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// One extrusion: its section (<see cref="Mask"/>: bit y * 8 + x of the 8 by 8 cell grid, y up), its width and height in cm,
/// its length in cm, its material (a RecyclingTable id), and whether it is still on an Extruder's die. This is all a piece
/// is, on the wire and in a blueprint (a part id only means something on the machine that made it).
/// </summary>
public readonly record struct ExtrusionKey(ulong Mask, ushort W, ushort H, ushort L, byte Material, bool Growing)
{
    public uint Stable()
    {
        var h = 2166136261u;
        void Mix(uint v) => h = (h ^ v) * 16777619u;
        Mix((uint)Mask);
        Mix((uint)(Mask >> 32));
        Mix((uint)W | (uint)H << 16);
        Mix((uint)L | (uint)Material << 16 | (Growing ? 1u << 24 : 0));
        return h;
    }
}

/// <summary>
/// Extrusions (docs/sandbox/CONSTRUCTS.md, "Extrusion"): parametric pieces, a cross-section swept along a length, which an
/// Extruder makes and which then build like any other part. A piece is a real <see cref="ConstructPartDef"/> made on demand
/// and kept in a table of its own: the same key always gives the same def, ids are <see cref="RuntimeBase"/> and up (never
/// sent: a key goes on the wire), and every system that reads a part's boxes, sockets, mass and health needs nothing new.
/// <see cref="ConstructParts.All"/> doesn't list them, so menus, racks and part sweeps see only the part files.
/// </summary>
public static class Extrusion
{
    public const int Grid = 8; // cells a side of a section
    public const int MaxRects = 16; // solid boxes a section may mesh into (each is a draw and a collision box)
    public const int StepCm = 5; // lengths go in steps of this, so the 0.1 m and 0.25 m lattices are both reachable
    public const int MaxLengthCm = 3200; // 32 m: no piece is longer
    public const int MinSectionCm = 10, MaxSectionCm = 400; // 0.1 to 4 m a side
    public const int MaxDefs = 12000; // distinct pieces in one run of the game (a length step of one line is one): past this no new shape can be made
    public const ushort RuntimeBase = 0x4000; // well above ConstructParts.MaxTypes
    public const ushort WireMark = 0xFFFE; // in a Full's part list: a key follows instead of a part id
    private const byte CustomWire = 255;

    /// <summary>A rectangle of cells.</summary>
    private readonly record struct Rect(int X, int Y, int W, int H);

    public readonly record struct Preset(string Id, ulong Mask);

    private static ulong Cells(params string[] rows)
    {
        // Written as drawn: the first row is the top.
        var m = 0ul;
        for (var r = 0; r < Grid; r++)
            for (var x = 0; x < Grid; x++)
                if (rows[r][x] == '#')
                    m |= 1ul << ((Grid - 1 - r) * Grid + x);
        return m;
    }

    /// <summary>The shipped sections, in the order of the Extruder's Profile choice (its last option is Custom).</summary>
    public static readonly Preset[] Presets =
    [
        new("solid", ulong.MaxValue),
        new("plate", Cells("........", "........", "........", "########", "########", "........", "........", "........")),
        new("wall", Cells("########", "#..##..#", "#..##..#", "#..##..#", "#..##..#", "#..##..#", "#..##..#", "########")),
        new("hollow", Cells("########", "#......#", "#......#", "#......#", "#......#", "#......#", "#......#", "########")),
        new("i-beam", Cells("########", "...##...", "...##...", "...##...", "...##...", "...##...", "...##...", "########")),
        new("c-channel", Cells("########", "##......", "##......", "##......", "##......", "##......", "##......", "########")),
        new("l-angle", Cells("##......", "##......", "##......", "##......", "##......", "##......", "########", "########")),
        new("t-bar", Cells("########", "########", "...##...", "...##...", "...##...", "...##...", "...##...", "...##...")),
        new("cross", Cells("...##...", "...##...", "...##...", "########", "########", "...##...", "...##...", "...##...")),
    ];

    private static readonly string[] PresetLabels = ["Solid bar", "Plate", "Wall", "Hollow box", "I-beam", "C-channel", "L-angle", "T-bar", "Cross"];

    private static readonly Dictionary<ExtrusionKey, ConstructPartDef> Interned = new(1024);
    private static readonly ConstructPartDef?[] ById = new ConstructPartDef?[MaxDefs];
    private static int _count;

    /// <summary>How many pieces the table holds.</summary>
    public static int DefCount => Volatile.Read(ref _count);

    public static bool IsRuntimeId(ushort id) => id >= RuntimeBase && id != ConstructParts.None;

    /// <summary>The def behind an id this table gave (null: no such piece).</summary>
    public static ConstructPartDef? Get(ushort id)
    {
        var k = id - RuntimeBase;
        return k >= 0 && k < MaxDefs ? ById[k] : null;
    }

    /// <summary>The preset's index for a mask, or -1.</summary>
    public static int PresetOf(ulong mask)
    {
        for (var k = 0; k < Presets.Length; k++)
            if (Presets[k].Mask == mask)
                return k;
        return -1;
    }

    /// <summary>What a section is called on a look line.</summary>
    public static string ProfileLabel(ulong mask) => PresetOf(mask) is var k and >= 0 ? PresetLabels[k] : "Custom";

    /// <summary>Why a section can't be made, or null: it needs cells, in one piece, that mesh into <see cref="MaxRects"/> boxes.</summary>
    public static string? Refusal(ulong mask)
    {
        if (mask == 0)
            return "the section has no cells";
        // One piece: a flood from the first cell reaches them all.
        var reach = 1ul << System.Numerics.BitOperations.TrailingZeroCount(mask);
        for (;;)
        {
            var next = reach | ((reach << 1) & 0xFEFEFEFEFEFEFEFEul) | ((reach >> 1) & 0x7F7F7F7F7F7F7F7Ful) | (reach << Grid) | (reach >> Grid);
            next &= mask;
            if (next == reach)
                break;
            reach = next;
        }
        if (reach != mask)
            return "the section is in separate pieces: every cell must join the rest";
        Span<Rect> rects = stackalloc Rect[Grid * Grid];
        return Mesh(mask, rects) > MaxRects ? $"the section is too broken up: it would take more than {MaxRects} boxes" : null;
    }

    /// <summary>The share of the grid a section fills.</summary>
    public static float Fill(ulong mask) => System.Numerics.BitOperations.PopCount(mask) / (float)(Grid * Grid);

    /// <summary>The section as rectangles: greedy, widest run first, the same on every machine. Returns how many.</summary>
    private static int Mesh(ulong mask, Span<Rect> into)
    {
        var left = mask;
        var n = 0;
        while (left != 0)
        {
            var bit = System.Numerics.BitOperations.TrailingZeroCount(left);
            int x0 = bit % Grid, y0 = bit / Grid;
            var w = 1;
            while (x0 + w < Grid && (left >> (y0 * Grid + x0 + w) & 1) != 0)
                w++;
            var h = 1;
            for (; y0 + h < Grid; h++)
            {
                var row = ((1ul << w) - 1) << ((y0 + h) * Grid + x0);
                if ((left & row) != row)
                    break;
            }
            for (var y = 0; y < h; y++)
                left &= ~(((1ul << w) - 1) << ((y0 + y) * Grid + x0));
            into[n++] = new Rect(x0, y0, w, h);
        }
        return n;
    }

    /// <summary>The piece for a key, made the first time it is asked for. Null: a bad key, or the table is full.</summary>
    public static ConstructPartDef? Def(in ExtrusionKey key)
    {
        lock (Interned)
        {
            if (Interned.TryGetValue(key, out var have))
                return have;
            if (_count >= MaxDefs || !Valid(key))
                return null;
            var d = Build(key);
            d.Id = (ushort)(RuntimeBase + _count);
            Interned[key] = d;
            ById[_count] = d;
            Volatile.Write(ref _count, _count + 1);
            return d;
        }
    }

    private static bool Valid(in ExtrusionKey k) =>
        k.W is >= MinSectionCm and <= MaxSectionCm && k.H is >= MinSectionCm and <= MaxSectionCm && k.L is >= StepCm and <= MaxLengthCm && k.L % StepCm == 0
        && ExtrusionTable.Current.Find(k.Material) != null && Refusal(k.Mask) == null;

    private static ConstructPartDef Build(in ExtrusionKey k)
    {
        var mat = ExtrusionTable.Current.Find(k.Material)!;
        var names = RecyclingTable.Current;
        float w = k.W / 100f, h = k.H / 100f, len = k.L / 100f;
        float cw = w / Grid, ch = h / Grid;
        Span<Rect> rects = stackalloc Rect[Grid * Grid];
        var n = Mesh(k.Mask, rects);
        var colour = names.ColourOf(k.Material);
        var boxes = new CPartBox[n];
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        var thinnest = float.MaxValue;
        for (var i = 0; i < n; i++)
        {
            var r = rects[i];
            var lo = new Vector3(-w * 0.5f + r.X * cw, -h * 0.5f + r.Y * ch, 0);
            var hi = new Vector3(-w * 0.5f + (r.X + r.W) * cw, -h * 0.5f + (r.Y + r.H) * ch, len);
            boxes[i] = new CPartBox((lo + hi) * 0.5f, (hi - lo) * 0.5f, colour);
            min = Vector3.Min(min, lo);
            max = Vector3.Max(max, hi);
            thinnest = MathF.Min(thinnest, MathF.Min(r.W * cw, r.H * ch));
        }
        var area = Fill(k.Mask) * w * h;
        var volume = area * len;
        var d = new ConstructPartDef
        {
            Name = Name(k),
            Description = $"An extrusion: a {ProfileLabel(k.Mask)} section {w:0.##} by {h:0.##} m, {len:0.##} m of {names.NameOf(k.Material)}.",
            Group = "Production",
            Section = "Extrusion",
            Role = PartRole.Frame,
            Tool = ConstructTool.Welder,
            AttachSeconds = Math.Clamp(2 + volume * 2, 2, 8),
            Mass = MathF.Max(0.1f, volume * mat.UnitsPerM3 * names.WeightOf(k.Material)),
            Health = MathF.Max(10, volume * mat.HealthPerM3),
            Armour = MathF.Min(mat.MaxArmour, mat.ArmourPerCm * thinnest * 100),
            Material = mat.Class,
            PhysicsOnly = true,
            PhysicsWhy = "extruded",
            Stage = true, // (never offered: a piece comes out of a machine)
            OnBreak = PartBreak.Detach,
            Boxes = boxes,
            SolidBoxes = n,
            BoundsMin = min,
            BoundsMax = max,
            CentreOptOut = true,
            Friction = mat.Class == "rubber" ? 1f : 0.6f,
            IsExtrusion = true,
            ExKey = k,
            ExHash = k.Stable(),
            ExArea = area,
        };
        d.Sockets = k.Growing ? [new CSocket("base", new Vector3((min.X + max.X) * 0.5f, (min.Y + max.Y) * 0.5f, 0), 5, SocketKind.Die)] : Sockets(k.Mask, w, h, len, min, max);
        return d;
    }

    /// <summary>Most stations along a piece: 4 sockets each, two ends (every part stays within 40 sockets).</summary>
    private const int MaxStations = 9;

    /// <summary>
    /// A finished piece's sockets: one on each end, and four round each station along it (about 1 m apart, further on a long
    /// piece: <see cref="MaxStations"/>), each on the outermost cell of its side nearest the middle, so an I-beam's flanges
    /// have them on their faces and its web has none in the air. Point and freeform welds reach everywhere else.
    /// </summary>
    private static CSocket[] Sockets(ulong mask, float w, float h, float len, Vector3 min, Vector3 max)
    {
        var stations = Math.Clamp((int)MathF.Round(len), 1, MaxStations);
        var list = new CSocket[2 + stations * 4];
        var mid = new Vector3((min.X + max.X) * 0.5f, (min.Y + max.Y) * 0.5f, 0);
        list[0] = new CSocket("base", mid, 5, SocketKind.Struct);
        list[1] = new CSocket("tip", mid with { Z = len }, 4, SocketKind.Struct);
        float cw = w / Grid, ch = h / Grid;
        Span<Vector2> spots = stackalloc Vector2[4];
        spots[0] = new Vector2(max.X, CentreCell(mask, Grid - 1, true) is var yp ? -h * 0.5f + (yp + 0.5f) * ch : 0);
        spots[1] = new Vector2(min.X, CentreCell(mask, 0, true) is var yn ? -h * 0.5f + (yn + 0.5f) * ch : 0);
        spots[2] = new Vector2(CentreCell(mask, Grid - 1, false) is var xp ? -w * 0.5f + (xp + 0.5f) * cw : 0, max.Y);
        spots[3] = new Vector2(CentreCell(mask, 0, false) is var xn ? -w * 0.5f + (xn + 0.5f) * cw : 0, min.Y);
        var at = 2;
        for (var s = 0; s < stations; s++)
        {
            var z = (s + 0.5f) * len / stations;
            for (var f = 0; f < 4; f++)
                list[at++] = new CSocket(FaceNames[f] + s.ToString(CultureInfo.InvariantCulture), new Vector3(spots[f].X, spots[f].Y, z), (byte)f, SocketKind.Struct);
        }
        return list;
    }

    private static readonly string[] FaceNames = ["+x", "-x", "+y", "-y"];

    /// <summary>
    /// The cell on a side nearest the side's middle. <paramref name="columns"/>: walking in from column <paramref name="from"/>
    /// (0 or 7) the first column that has cells, and its row nearest the middle; otherwise the same for rows, giving a column.
    /// </summary>
    private static float CentreCell(ulong mask, int from, bool columns)
    {
        var step = from == 0 ? 1 : -1;
        for (var a = from; a is >= 0 and < Grid; a += step)
        {
            var best = -1;
            var bestD = float.MaxValue;
            for (var b = 0; b < Grid; b++)
            {
                var bit = columns ? b * Grid + a : a * Grid + b;
                if ((mask >> bit & 1) == 0)
                    continue;
                var dist = MathF.Abs(b - (Grid - 1) * 0.5f);
                if (dist < bestD)
                {
                    bestD = dist;
                    best = b;
                }
            }
            if (best >= 0)
                return best;
        }
        return (Grid - 1) * 0.5f;
    }

    // ---- Names: a piece in a blueprint or a log ----------------------------------------------------------------------------

    /// <summary>"Extrusion i-beam 25x25x400 metal": a piece's whole key as text (blueprints name pieces like this, <see cref="Parse"/>).</summary>
    public static string Name(in ExtrusionKey k)
    {
        var profile = PresetOf(k.Mask) is var p and >= 0 ? Presets[p].Id : k.Mask.ToString("x16", CultureInfo.InvariantCulture);
        return $"{Prefix}{profile} {k.W}x{k.H}x{k.L} {RecyclingTable.Current.NameOf(k.Material)}{(k.Growing ? GrowingSuffix : "")}";
    }

    public const string Prefix = "Extrusion ";
    private const string GrowingSuffix = " growing";

    /// <summary>The piece a name gives (<see cref="Name"/>), or null when it isn't one.</summary>
    public static ConstructPartDef? Parse(string name)
    {
        if (!name.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            return null;
        var words = name[Prefix.Length..].Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length != 3)
            return null;
        var mask = 0ul;
        var preset = Array.FindIndex(Presets, p => p.Id.Equals(words[0], StringComparison.OrdinalIgnoreCase));
        if (preset >= 0)
            mask = Presets[preset].Mask;
        else if (!ulong.TryParse(words[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out mask))
            return null;
        var sizes = words[1].Split('x');
        if (sizes.Length != 3 || !ushort.TryParse(sizes[0], out var w) || !ushort.TryParse(sizes[1], out var h) || !ushort.TryParse(sizes[2], out var l))
            return null;
        var material = words[2];
        var growing = material.EndsWith(GrowingSuffix, StringComparison.OrdinalIgnoreCase);
        if (growing)
            material = material[..^GrowingSuffix.Length];
        return RecyclingTable.Current.IdOf(material) is var id and not 0 ? Def(new ExtrusionKey(mask, w, h, l, id, growing)) : null;
    }

    // ---- The wire ----------------------------------------------------------------------------------------------------------

    /// <summary>A piece in a Full's part list: a mark, then its key (8 bytes with a preset section, 16 with a custom one).</summary>
    public static void Write(PacketWriter w, ConstructPartDef d)
    {
        ref readonly var k = ref d.ExKey;
        w.U16(WireMark);
        var preset = PresetOf(k.Mask);
        w.U8(preset >= 0 ? (byte)preset : CustomWire);
        if (preset < 0)
            w.U32((uint)k.Mask).U32((uint)(k.Mask >> 32));
        w.U16(k.W).U16(k.H).U16(k.L).U8((byte)(k.Material | (k.Growing ? 0x80 : 0)));
    }

    /// <summary>The key after a <see cref="WireMark"/>, as this machine's part id. A key no machine would send is malformed.</summary>
    public static ushort Read(ref PacketReader r)
    {
        var profile = r.U8();
        ulong mask;
        if (profile == CustomWire)
        {
            mask = r.U32();
            mask |= (ulong)r.U32() << 32;
        }
        else if (profile < Presets.Length)
            mask = Presets[profile].Mask;
        else
            throw new MalformedPacketException($"an extrusion of profile {profile}");
        var w = r.U16();
        var h = r.U16();
        var l = r.U16();
        var m = r.U8();
        var key = new ExtrusionKey(mask, w, h, l, (byte)(m & 0x7F), m >= 0x80);
        if (Def(key) is { } d)
            return d.Id;
        if (DefCount >= MaxDefs)
            return ConstructParts.Need("Frame Beam").Id; // (a client that has seen more pieces than the table holds: a stand-in, never a dropped packet)
        throw new MalformedPacketException($"an extrusion of {w} by {h} by {l} cm, material {m & 0x7F}");
    }
}
