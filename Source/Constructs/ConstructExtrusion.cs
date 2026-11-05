using System.Globalization;
using System.Numerics;

namespace CallOfSlop;

/// <summary>What a run-time piece is: a section off the 8 by 8 grid, a solid round, a pipe, or a wheel off a Wheel Former. Wire byte: add at the end.</summary>
public enum ExShape : byte
{
    Section,
    Round,
    Pipe,
    Wheel,
}

/// <summary>
/// One extrusion: its section (<see cref="Mask"/>: bit y * 8 + x of the 8 by 8 cell grid, y up), its width and height in cm,
/// its length in cm, its material (a RecyclingTable id), and whether it is still on its machine. <see cref="Shape"/>: a Round
/// or a Pipe has no grid: <see cref="W"/> and <see cref="H"/> are both its diameter, and <see cref="Wall"/> a Pipe's wall (cm).
/// A Wheel has <see cref="W"/> its width along the axle, <see cref="H"/> its diameter and no length. This is all a piece
/// is, on the wire and in a blueprint (a part id only means something on the machine that made it), and what a later
/// machine reads to know a piece's section and length.
/// </summary>
public readonly record struct ExtrusionKey(ulong Mask, ushort W, ushort H, ushort L, byte Material, bool Growing, ExShape Shape = ExShape.Section, ushort Wall = 0)
{
    public uint Stable()
    {
        var h = 2166136261u;
        void Mix(uint v) => h = (h ^ v) * 16777619u;
        Mix((uint)Mask);
        Mix((uint)(Mask >> 32));
        Mix((uint)W | (uint)H << 16);
        Mix((uint)L | (uint)Material << 16 | (Growing ? 1u << 24 : 0));
        if (Shape != ExShape.Section)
            Mix((uint)Shape | (uint)Wall << 8);
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
    public const int MaxLengthCm = 19200; // 192 m: no piece is longer (the Capital Extruder's; ConstructParts.MaxPartReach is 200 m)
    public const int MinSectionCm = 10, MaxSectionCm = 3200; // 0.1 to 32 m a side
    public const int MinBoreCm = 5; // a pipe's wall always leaves this much hole
    public const int MaxWallCm = 200; // and is at most this thick
    public const int MinWheelWidthCm = 5, MaxWheelWidthCm = 2000; // a wheel: 5 cm to 20 m wide...
    public const int MinWheelCm = 20, MaxWheelCm = 4800; // ...and 0.2 to 48 m across
    public const int MaxDefs = 48000; // distinct pieces in one run of the game (a length step of one line is one): past this no new shape can be made
    public const ushort RuntimeBase = 0x4000; // well above ConstructParts.MaxTypes
    public const ushort WireMark = 0xFFFE; // in a Full's part list: a key follows instead of a part id
    private const byte CustomWire = 255, RoundWire = 254, PipeWire = 253, WheelWire = 252;
    private const int RoundSides = 14; // a Round's sides: with the caps' middles, 30 points of ConvexHull.MaxCorners' 32
    private const int PipeBoxes = 32, PipeSides = 24; // a Pipe's wall: this many boxes round its bore, and sides drawn
    private const int WheelSides = 24;

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

    /// <summary>The Extruder's Profile choices after the grid's: Solid bar...Cross, Custom, Round, Pipe.</summary>
    public static int RoundProfile => Presets.Length + 1;

    public static int PipeProfile => Presets.Length + 2;

    /// <summary>The shape a Profile choice makes.</summary>
    public static ExShape ShapeOf(int profile) => profile == RoundProfile ? ExShape.Round : profile == PipeProfile ? ExShape.Pipe : ExShape.Section;

    /// <summary>What a key is, in words: "I-beam 0.25 x 0.25 m", "Round 0.5 m", "Pipe 0.5 m, wall 3 cm".</summary>
    public static string Label(in ExtrusionKey k) => k.Shape switch
    {
        ExShape.Round => $"Round {k.W / 100f:0.##} m",
        ExShape.Pipe => $"Pipe {k.W / 100f:0.##} m, wall {k.Wall} cm",
        ExShape.Wheel => $"Wheel {k.H / 100f:0.##} m across, {k.W / 100f:0.##} m wide",
        _ => $"{ProfileLabel(k.Mask)} {k.W / 100f:0.##} x {k.H / 100f:0.##} m",
    };

    /// <summary>A round section on the 8 by 8 grid, for showing it (the panel, the die's preview): <paramref name="inner"/> is the bore over the diameter. The grid is never what is made.</summary>
    public static ulong RoundMask(float inner)
    {
        var mask = 0ul;
        var hole = inner * Grid * 0.5f - 0.5f; // (half a cell into the bore: a thin wall still shows)
        for (var y = 0; y < Grid; y++)
            for (var x = 0; x < Grid; x++)
            {
                var r = MathF.Sqrt(MathF.Pow(x + 0.5f - Grid * 0.5f, 2) + MathF.Pow(y + 0.5f - Grid * 0.5f, 2));
                if (r <= Grid * 0.5f && r >= hole)
                    mask |= 1ul << (y * Grid + x);
            }
        return mask;
    }

    /// <summary>A key's section in m2.</summary>
    public static float AreaM2(in ExtrusionKey k)
    {
        var r = k.W * 0.005f;
        var bore = r - k.Wall * 0.01f;
        return k.Shape switch
        {
            ExShape.Round => MathF.PI * r * r,
            ExShape.Pipe => MathF.PI * (r * r - bore * bore),
            _ => Fill(k.Mask) * k.W * k.H / 10000f,
        };
    }

    /// <summary>A wheel's volume in m3: a cylinder of its width and diameter.</summary>
    public static float WheelVolume(in ExtrusionKey k)
    {
        var r = k.H * 0.005f;
        return MathF.PI * r * r * k.W * 0.01f;
    }

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

    private static bool Valid(in ExtrusionKey k)
    {
        if (k.Shape == ExShape.Wheel)
            return k.W is >= MinWheelWidthCm and <= MaxWheelWidthCm && k.H is >= MinWheelCm and <= MaxWheelCm && k.W % StepCm == 0 && k.H % StepCm == 0
                && k.L == 0 && k.Mask == 0 && k.Wall == 0 && ExtrusionTable.Current.FindWheel(k.Material) != null;
        if (k.W is < MinSectionCm or > MaxSectionCm || k.H is < MinSectionCm or > MaxSectionCm || k.L is < StepCm or > MaxLengthCm || k.L % StepCm != 0
            || ExtrusionTable.Current.Find(k.Material) == null)
            return false;
        return k.Shape switch
        {
            ExShape.Section => k.Wall == 0 && Refusal(k.Mask) == null,
            ExShape.Round => k.W == k.H && k.Mask == 0 && k.Wall == 0,
            ExShape.Pipe => k.W == k.H && k.Mask == 0 && k.Wall is >= 1 and <= MaxWallCm && k.Wall * 2 + MinBoreCm <= k.W,
            _ => false,
        };
    }

    private static ConstructPartDef Build(in ExtrusionKey k)
    {
        if (k.Shape == ExShape.Wheel)
            return BuildWheel(k);
        var d = k.Shape == ExShape.Section ? BuildSection(k) : BuildRound(k);
        d.Sockets = k.Growing ? [new CSocket("base", new Vector3((d.BoundsMin.X + d.BoundsMax.X) * 0.5f, (d.BoundsMin.Y + d.BoundsMax.Y) * 0.5f, 0), 5, SocketKind.Die)]
            : Sockets(k, k.W / 100f, k.H / 100f, k.L / 100f, d.BoundsMin, d.BoundsMax);
        return d;
    }

    /// <summary>The numbers every piece has; the caller adds its boxes and bounds.</summary>
    private static ConstructPartDef Stats(in ExtrusionKey k, ExtrusionMaterial mat, float area, float volume, float thinnest, string description)
    {
        var names = RecyclingTable.Current;
        return new ConstructPartDef
        {
            Name = Name(k),
            Description = description,
            Group = "Production",
            Section = "Extrusion",
            Role = PartRole.Frame,
            Tool = ConstructTool.Welder,
            AttachSeconds = Math.Clamp(2 + volume * 2, 2, 8),
            Mass = Math.Clamp(volume * mat.UnitsPerM3 * names.WeightOf(k.Material), 0.1f, ConstructParts.MaxPartMass),
            Health = Math.Clamp(volume * mat.HealthPerM3, 10, ConstructParts.MaxPartHealth),
            Armour = MathF.Min(mat.MaxArmour, mat.ArmourPerCm * thinnest * 100),
            Material = mat.Class,
            PhysicsOnly = true,
            PhysicsWhy = "extruded",
            Stage = true, // (never offered: a piece comes out of a machine)
            OnBreak = PartBreak.Detach,
            CentreOptOut = true,
            Friction = mat.Class == "rubber" ? 1f : 0.6f,
            IsExtrusion = true,
            ExKey = k,
            ExHash = k.Stable(),
            ExArea = area,
        };
    }

    private static ConstructPartDef BuildSection(in ExtrusionKey k)
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
        var d = Stats(k, mat, area, area * len, thinnest, $"An extrusion: a {ProfileLabel(k.Mask)} section {w:0.##} by {h:0.##} m, {len:0.##} m of {names.NameOf(k.Material)}.");
        d.Boxes = boxes;
        d.SolidBoxes = n;
        d.BoundsMin = min;
        d.BoundsMax = max;
        return d;
    }

    /// <summary>
    /// A Round or a Pipe. Drawn as a shape (one mesh for every length, its size the box's). What collides: a finished Round is
    /// a convex hull of the same shape over three hidden boxes (a growing one is the boxes, since a hull is made once and
    /// a piece on a die changes length four times a second); a Pipe's wall is <see cref="PipeBoxes"/> hidden boxes round the
    /// bore, so the bore is open: whatever fits walks, rolls or is shot through it. Boxes don't turn, so the ring is stepped:
    /// true on the axes, about 9 % of the radius proud (and into the bore) on the diagonals.
    /// </summary>
    private static ConstructPartDef BuildRound(in ExtrusionKey k)
    {
        var mat = ExtrusionTable.Current.Find(k.Material)!;
        var names = RecyclingTable.Current;
        var colour = names.ColourOf(k.Material);
        float across = k.W / 100f, r = across * 0.5f, len = k.L / 100f, wall = k.Wall / 100f;
        var pipe = k.Shape == ExShape.Pipe;
        var boxes = new CPartBox[(pipe ? PipeBoxes : 3) + 1];
        var n = 0;
        if (pipe)
        {
            var bore = r - wall;
            var slice = MathF.Tau / PipeBoxes;
            for (var i = 0; i < PipeBoxes; i++)
            {
                // The box round one slice of the ring.
                float a0 = (i - 0.5f) * slice, a1 = a0 + slice;
                var lo = new Vector2(float.MaxValue);
                var hi = new Vector2(float.MinValue);
                void Take(float a, float at)
                {
                    var p = new Vector2(MathF.Cos(a), MathF.Sin(a)) * at;
                    lo = Vector2.Min(lo, p);
                    hi = Vector2.Max(hi, p);
                }
                Take(a0, bore);
                Take(a0, r);
                Take(a1, bore);
                Take(a1, r);
                if (i % (PipeBoxes / 4) == 0)
                    Take(i * slice, r); // (the slice over an axis: its middle stands furthest out)
                var c = (lo + hi) * 0.5f;
                var h = (hi - lo) * 0.5f;
                boxes[n++] = new CPartBox(new Vector3(c, len * 0.5f), new Vector3(h, len * 0.5f), colour, Hidden: true);
            }
        }
        else
        {
            // An octagon's three boxes, inside the circle.
            float wide = r * 0.92f, narrow = r * 0.38f, square = r * 0.70f;
            var half = len * 0.5f;
            boxes[n++] = new CPartBox(new Vector3(0, 0, half), new Vector3(wide, narrow, half), colour, Hidden: true);
            boxes[n++] = new CPartBox(new Vector3(0, 0, half), new Vector3(narrow, wide, half), colour, Hidden: true);
            boxes[n++] = new CPartBox(new Vector3(0, 0, half), new Vector3(square, square, half), colour, Hidden: true);
        }
        var inner = pipe ? MathF.Round((across - 2 * wall) / across * 128) / 128 : 0;
        var shape = PartShapes.For(new PartBox { Shape = pipe ? "tube" : "cylinder", Size = Vector3.One, Segments = pipe ? PipeSides : RoundSides, Inner = pipe ? inner : -1, Ends = pipe ? "open" : "closed" });
        boxes[n] = new CPartBox(new Vector3(0, 0, len * 0.5f), new Vector3(r, r, len * 0.5f), colour, Detail: true, Shape: shape);
        var area = AreaM2(k);
        var what = pipe ? $"A pipe {across:0.##} m across with a {wall:0.##} m wall" : $"A round bar {across:0.##} m across";
        var d = Stats(k, mat, area, area * len, pipe ? wall : across, $"An extrusion: {what}, {len:0.##} m of {names.NameOf(k.Material)}.");
        d.Boxes = boxes;
        d.SolidBoxes = n;
        d.BoundsMin = new Vector3(-r, -r, 0);
        d.BoundsMax = new Vector3(r, r, len);
        d.ColliderBoxes = pipe || k.Growing;
        return d;
    }

    private static readonly ConstructPartDef?[] WheelConfigs = new ConstructPartDef?[256];

    /// <summary>
    /// A wheel off a Wheel Former: a real wheel, with its axle socket on one face and its origin there too (so width grows away
    /// from the hub's plate and the part never moves). It rolls on a suspension as any wheel does (a raycast wheel and a cylinder
    /// from its bounds and radius, ConstructDrive.cs); loose, its three hidden boxes collide. Its numbers come from the
    /// table's "wheels" rows, and it has the tyre settings of every wheel, from the first of its material on.
    /// </summary>
    private static ConstructPartDef BuildWheel(in ExtrusionKey k)
    {
        var row = ExtrusionTable.Current.FindWheel(k.Material)!;
        var names = RecyclingTable.Current;
        float wide = k.W / 100f, across = k.H / 100f, r = across * 0.5f;
        var tyre = names.ColourOf(k.Material);
        var hubColour = new Vector3(0.6f, 0.62f, 0.65f);
        var middle = wide * 0.5f;
        var solid = new Vector3(middle, 0, 0);
        var lathe = PartShapes.For(new PartBox { Shape = "cylinder", Size = Vector3.One, Segments = WheelSides, Inner = -1 });
        var volume = WheelVolume(k);
        var d = Stats(k, new ExtrusionMaterial(row.Material, row.UnitsPerM3, row.HealthPerM3, row.ArmourPerCm, row.MaxArmour, row.Class), 0, volume, across * 0.1f,
            $"A wheel off a Wheel Former: {across:0.##} m across, {wide:0.##} m wide, of {names.NameOf(k.Material)}.");
        d.Boxes =
        [
            new CPartBox(solid, new Vector3(middle, r * 0.92f, r * 0.38f), tyre, Hidden: true, Paintable: false),
            new CPartBox(solid, new Vector3(middle, r * 0.38f, r * 0.92f), tyre, Hidden: true, Paintable: false),
            new CPartBox(solid, new Vector3(middle, r * 0.7f, r * 0.7f), tyre, Hidden: true, Paintable: false),
            new CPartBox(solid, new Vector3(middle, r, r), tyre, Detail: true, Shape: lathe, Paintable: false) { Front = 1 },
            new CPartBox(solid, new Vector3(middle + 0.012f, r * 0.34f, r * 0.34f), hubColour, Detail: true, Shape: lathe) { Front = 1 },
            new CPartBox(solid, new Vector3(middle + 0.008f, r * 0.72f, r * 0.08f), hubColour, Detail: true),
            new CPartBox(solid, new Vector3(middle + 0.006f, r * 0.08f, r * 0.72f), hubColour, Detail: true),
        ];
        d.SolidBoxes = 3;
        d.BoundsMin = new Vector3(0, -r, -r);
        d.BoundsMax = new Vector3(wide, r, r);
        d.Role = PartRole.Wheel;
        d.Group = "Production";
        d.Section = "Wheels";
        d.Friction = 1;
        d.Grip = row.Grip;
        d.OffroadGrip = row.Offroad;
        d.SideGrip = row.Side;
        d.RollResist = row.Roll;
        d.FlatTyre = row.Flat;
        d.OnBreak = row.Flat ? PartBreak.Disable : PartBreak.Detach;
        d.WheelRadius = r;
        d.Class = k.H < 200 ? ConstructParts.Small : k.H < 700 ? ConstructParts.Medium : k.H < 2400 ? ConstructParts.Large : ConstructParts.Capital;
        d.Sockets = [new CSocket("axle", Vector3.Zero, 1, k.Growing ? SocketKind.Die : SocketKind.Axle, d.Class)];
        // (the same settings for every size of a material: made once)
        if (WheelConfigs[k.Material] is { } made)
        {
            d.Config = made.Config;
            d.CfgSlot = made.CfgSlot;
        }
        else
        {
            ConstructConfig.ForWheel(d);
            WheelConfigs[k.Material] = d;
        }
        return d;
    }

    /// <summary>Most stations along a piece: 4 sockets each, two ends (every part stays within 40 sockets).</summary>
    private const int MaxStations = 9;

    /// <summary>
    /// A finished piece's sockets: one on each end, and four round each station along it (about 1 m apart, further on a long
    /// piece: <see cref="MaxStations"/>), each on the outermost cell of its side nearest the middle, so an I-beam's flanges
    /// have them on their faces and its web has none in the air. Point and freeform welds reach everywhere else.
    /// </summary>
    private static CSocket[] Sockets(in ExtrusionKey k, float w, float h, float len, Vector3 min, Vector3 max)
    {
        var mask = k.Mask;
        var round = k.Shape != ExShape.Section;
        var stations = Math.Clamp((int)MathF.Round(len), 1, MaxStations);
        var list = new CSocket[2 + stations * 4];
        var mid = new Vector3((min.X + max.X) * 0.5f, (min.Y + max.Y) * 0.5f, 0);
        list[0] = new CSocket("base", mid, 5, SocketKind.Struct);
        list[1] = new CSocket("tip", mid with { Z = len }, 4, SocketKind.Struct);
        float cw = w / Grid, ch = h / Grid;
        Span<Vector2> spots = stackalloc Vector2[4];
        if (round)
        {
            // (on the outside of the round, at the four compass points)
            spots[0] = new Vector2(max.X, 0);
            spots[1] = new Vector2(min.X, 0);
            spots[2] = new Vector2(0, max.Y);
            spots[3] = new Vector2(0, min.Y);
        }
        else
        {
            spots[0] = new Vector2(max.X, CentreCell(mask, Grid - 1, true) is var yp ? -h * 0.5f + (yp + 0.5f) * ch : 0);
            spots[1] = new Vector2(min.X, CentreCell(mask, 0, true) is var yn ? -h * 0.5f + (yn + 0.5f) * ch : 0);
            spots[2] = new Vector2(CentreCell(mask, Grid - 1, false) is var xp ? -w * 0.5f + (xp + 0.5f) * cw : 0, max.Y);
            spots[3] = new Vector2(CentreCell(mask, 0, false) is var xn ? -w * 0.5f + (xn + 0.5f) * cw : 0, min.Y);
        }
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

    /// <summary>
    /// "Extrusion i-beam 25x25x400 metal": a piece's whole key as text (blueprints and saves name pieces like this, <see cref="Parse"/>).
    /// "Extrusion round 50x50x400 metal", "Extrusion pipe3 50x50x400 metal" (a 3 cm wall), "Extrusion wheel 30x100x0 rubber".
    /// </summary>
    public static string Name(in ExtrusionKey k)
    {
        var profile = k.Shape switch
        {
            ExShape.Round => RoundWord,
            ExShape.Pipe => PipeWord + k.Wall.ToString(CultureInfo.InvariantCulture),
            ExShape.Wheel => WheelWord,
            _ => PresetOf(k.Mask) is var p and >= 0 ? Presets[p].Id : k.Mask.ToString("x16", CultureInfo.InvariantCulture),
        };
        return $"{Prefix}{profile} {k.W}x{k.H}x{k.L} {RecyclingTable.Current.NameOf(k.Material)}{(k.Growing ? GrowingSuffix : "")}";
    }

    public const string Prefix = "Extrusion ";
    private const string GrowingSuffix = " growing";
    private const string RoundWord = "round", PipeWord = "pipe", WheelWord = "wheel";

    /// <summary>The piece a name gives (<see cref="Name"/>), or null when it isn't one.</summary>
    public static ConstructPartDef? Parse(string name)
    {
        if (!name.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            return null;
        var words = name[Prefix.Length..].Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length != 3)
            return null;
        var mask = 0ul;
        var shape = ExShape.Section;
        ushort wall = 0;
        var preset = Array.FindIndex(Presets, p => p.Id.Equals(words[0], StringComparison.OrdinalIgnoreCase));
        if (preset >= 0)
            mask = Presets[preset].Mask;
        else if (words[0].Equals(RoundWord, StringComparison.OrdinalIgnoreCase))
            shape = ExShape.Round;
        else if (words[0].Equals(WheelWord, StringComparison.OrdinalIgnoreCase))
            shape = ExShape.Wheel;
        else if (words[0].StartsWith(PipeWord, StringComparison.OrdinalIgnoreCase))
        {
            if (!ushort.TryParse(words[0][PipeWord.Length..], out wall))
                return null;
            shape = ExShape.Pipe;
        }
        else if (!ulong.TryParse(words[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out mask))
            return null;
        var sizes = words[1].Split('x');
        if (sizes.Length != 3 || !ushort.TryParse(sizes[0], out var w) || !ushort.TryParse(sizes[1], out var h) || !ushort.TryParse(sizes[2], out var l))
            return null;
        var material = words[2];
        var growing = material.EndsWith(GrowingSuffix, StringComparison.OrdinalIgnoreCase);
        if (growing)
            material = material[..^GrowingSuffix.Length];
        return RecyclingTable.Current.IdOf(material) is var id and not 0 ? Def(new ExtrusionKey(mask, w, h, l, id, growing, shape, wall)) : null;
    }

    // ---- The wire ----------------------------------------------------------------------------------------------------------

    /// <summary>A piece in a Full's part list: a mark, then its key (8 bytes with a preset section, 16 with a custom one, 8 with a round or a wheel, 10 with a pipe).</summary>
    public static void Write(PacketWriter w, ConstructPartDef d)
    {
        ref readonly var k = ref d.ExKey;
        w.U16(WireMark);
        switch (k.Shape)
        {
            case ExShape.Round:
                w.U8(RoundWire);
                break;
            case ExShape.Pipe:
                w.U8(PipeWire).U16(k.Wall);
                break;
            case ExShape.Wheel:
                w.U8(WheelWire);
                break;
            default:
                var preset = PresetOf(k.Mask);
                w.U8(preset >= 0 ? (byte)preset : CustomWire);
                if (preset < 0)
                    w.U32((uint)k.Mask).U32((uint)(k.Mask >> 32));
                break;
        }
        w.U16(k.W).U16(k.H).U16(k.L).U8((byte)(k.Material | (k.Growing ? 0x80 : 0)));
    }

    /// <summary>The key after a <see cref="WireMark"/>, as this machine's part id. A key no machine would send is malformed.</summary>
    public static ushort Read(ref PacketReader r)
    {
        var profile = r.U8();
        var mask = 0ul;
        var shape = ExShape.Section;
        ushort wall = 0;
        if (profile is RoundWire)
            shape = ExShape.Round;
        else if (profile is PipeWire)
        {
            shape = ExShape.Pipe;
            wall = r.U16();
        }
        else if (profile is WheelWire)
            shape = ExShape.Wheel;
        else if (profile == CustomWire)
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
        var key = new ExtrusionKey(mask, w, h, l, (byte)(m & 0x7F), m >= 0x80, shape, wall);
        if (Def(key) is { } d)
            return d.Id;
        if (DefCount >= MaxDefs)
            return ConstructParts.Need("Frame Beam").Id; // (a client that has seen more pieces than the table holds: a stand-in, never a dropped packet)
        throw new MalformedPacketException($"an extrusion of {w} by {h} by {l} cm, material {m & 0x7F}");
    }
}
