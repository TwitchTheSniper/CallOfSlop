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
    Fan, // a whole propeller, rotor or ducted fan off a Fan Former
    Blade, // a blade thrown off a rotor: scrap
    Wing, // a wing or a control surface off an Airframe Former
}

/// <summary>
/// One extrusion: its section (<see cref="Mask"/>: bit y * 8 + x of the 8 by 8 cell grid, y up), its width and height in cm,
/// its length in cm, its material (a RecyclingTable id), and whether it is still on its machine. <see cref="Shape"/>: a Round
/// or a Pipe has no grid: <see cref="W"/> and <see cref="H"/> are both its diameter, and <see cref="Wall"/> a Pipe's wall (cm).
/// A Wheel has <see cref="W"/> its width along the axle, <see cref="H"/> its diameter and no length. A Fan has <see cref="H"/>
/// its diameter, no width or length, and <see cref="Wall"/> its kind, blade style and blades (<see cref="Extrusion.FanWall"/>). A Blade has <see cref="W"/> its chord,
/// <see cref="H"/> its length and <see cref="Wall"/> its style. A Wing has <see cref="W"/> its chord, <see cref="L"/> its span and
/// <see cref="Wall"/> its row of "airframes". This is all a piece
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
    public const int MinFanCm = 40, MaxFanCm = 9600; // a fan: 0.4 to 96 m across
    public const int MinChordCm = 10, MaxChordCm = 3200; // a wing: 0.1 to 32 m of chord
    private const float WingSkin = 0.0225f; // m of skin and ribs over its area, before chord's root
    public const int MaxBladeStyles = 8; // (three bits of a Fan's Wall)
    private const byte CustomWire = 255, RoundWire = 254, PipeWire = 253, WheelWire = 252, FanWire = 251, BladeWire = 250, WingWire = 249;
    internal const byte OrdnanceWire = 248; // (a modular bomb or missile, not an extrusion: Ordnance.Write)
    private const int RoundSides = 14; // a Round's sides: with the caps' middles, 30 points of ConvexHull.MaxCorners' 32
    private const int PipeBoxes = 32, PipeSides = 24; // a Pipe's wall: this many boxes round its bore, and sides drawn
    private const int WheelSides = 24;

    /// <summary>A rectangle of cells.</summary>
    private readonly record struct Rect(int X, int Y, int W, int H);

    public readonly record struct Preset(string Id, ulong Mask);

    internal static ulong Cells(params string[] rows)
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
    internal static int Limit = MaxDefs; // (--bench-parts lowers it, so freed slots are taken again within a short run)
    private static readonly Queue<int> FreeSlots = new(); // slots RuntimeParts freed, oldest first: taken again only once every slot has been used, so a stale id lasts

    /// <summary>How many slots have been used (the high-water mark, freed ones included).</summary>
    public static int DefCount => Volatile.Read(ref _count);

    /// <summary>Run-time parts held now: the slots used less the freed ones waiting.</summary>
    public static int Live
    {
        get
        {
            lock (Interned)
                return _count - FreeSlots.Count;
        }
    }

    /// <summary>No new run-time part can be made (every slot used and none freed).</summary>
    public static bool Full
    {
        get
        {
            lock (Interned)
                return _count >= Limit && FreeSlots.Count == 0;
        }
    }

    /// <summary>The lock every run-time part is made and freed under.</summary>
    internal static object Gate => Interned;

    // (under Interned) A slot for a new part: a never-used one first, then the oldest freed one. False: the table is full.
    private static bool Place(ConstructPartDef d)
    {
        int slot;
        if (_count < Limit)
            slot = _count;
        else if (!FreeSlots.TryDequeue(out slot))
            return false;
        d.Id = (ushort)(RuntimeBase + slot);
        d.Touched = Environment.TickCount64;
        ById[slot] = d;
        if (slot == _count)
            Volatile.Write(ref _count, _count + 1);
        RuntimeParts.NoteFill(_count - FreeSlots.Count, Limit, "run-time parts");
        return true;
    }

    /// <summary>(RuntimeParts, under <see cref="Gate"/>) Frees a run-time part's slot. Its def stays readable by its old id until the slot is taken again.</summary>
    internal static void FreeSlot(ConstructPartDef d)
    {
        var slot = d.Id - RuntimeBase;
        if (slot < 0 || slot >= MaxDefs || !ReferenceEquals(ById[slot], d))
            return;
        FreeSlots.Enqueue(slot);
    }

    /// <summary>(RuntimeParts, under <see cref="Gate"/>) Run-time part <paramref name="slot"/>, or null.</summary>
    internal static ConstructPartDef? AtSlot(int slot) => slot < _count ? ById[slot] : null;

    /// <summary>(RuntimeParts, under <see cref="Gate"/>) Drops the doomed pieces from the key table.</summary>
    internal static void Forget(Func<ConstructPartDef, bool> doomed) => RuntimeParts.Drop(Interned, doomed);

    public static bool IsRuntimeId(ushort id) => id >= RuntimeBase && id != ConstructParts.None;

    /// <summary>The def behind an id this table gave (null: no such piece).</summary>
    public static ConstructPartDef? Get(ushort id)
    {
        var k = id - RuntimeBase;
        return k >= 0 && k < MaxDefs ? ById[k] : null;
    }

    /// <summary>The preset's index for a mask, or -1.</summary>
    /// <summary>The Extruder's Profile choice for airfoil <paramref name="k"/> (rows of Extrusion.json's "airfoils"): they come after Pipe.</summary>
    public static int AirfoilProfile(int k) => Presets.Length + 3 + k;

    /// <summary>The airfoil row a section is, or null.</summary>
    public static AirfoilRow? AirfoilOf(ulong mask)
    {
        var rows = ExtrusionTable.Current.Airfoils;
        for (var k = 0; k < rows.Length; k++)
            if (rows[k].Mask == mask)
                return rows[k];
        return null;
    }

    public static int PresetOf(ulong mask)
    {
        for (var k = 0; k < Presets.Length; k++)
            if (Presets[k].Mask == mask)
                return k;
        return -1;
    }

    /// <summary>What a section is called on a look line.</summary>
    public static string ProfileLabel(ulong mask) => PresetOf(mask) is var k and >= 0 ? PresetLabels[k] : AirfoilOf(mask)?.Label ?? "Custom";

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
        ExShape.Fan => $"{FanKindOf(k.Wall)?.Label ?? "Fan"} {k.H / 100f:0.##} m across, {FanBladesOf(k.Wall)} {StyleRow(FanStyleOf(k.Wall)).Label.ToLowerInvariant()} blades",
        ExShape.Blade => $"{StyleRow(k.Wall).Label} blade {k.H / 100f:0.##} m long",
        ExShape.Wing => $"{AirframeOf(k.Wall)?.Label ?? "Wing"} {k.W / 100f:0.##} m chord, {k.L / 100f:0.##} m span",
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

    /// <summary>Gives a run-time part made elsewhere (a modular bomb or missile: Ordnance.cs) an id in this table. False when it is full.</summary>
    internal static bool Adopt(ConstructPartDef d)
    {
        lock (Interned)
            return Place(d);
    }

    /// <summary>The piece for a key, made the first time it is asked for. Null: a bad key, or the table is full.</summary>
    public static ConstructPartDef? Def(in ExtrusionKey key)
    {
        lock (Interned)
        {
            if (Interned.TryGetValue(key, out var have))
            {
                have.Touched = Environment.TickCount64;
                return have;
            }
            if (!Valid(key) || _count >= Limit && FreeSlots.Count == 0)
                return null;
            var d = Build(key); // (its shapes are unit lathes and sections, few and kept for good)
            if (!Place(d))
                return null;
            Interned[key] = d;
            return d;
        }
    }

    /// <summary>A Fan's <see cref="ExtrusionKey.Wall"/>: its kind (a row of "fanKinds"), its blade style (a row of Air.json's "bladeStyles") and how many blades.</summary>
    public static ushort FanWall(int kind, int style, int blades) => (ushort)(kind << 8 | style << 5 | blades);

    public static int FanBladesOf(ushort wall) => wall & 31;

    public static int FanStyleOf(ushort wall) => wall >> 5 & 7;

    public static FanKind? FanKindOf(ushort wall) => wall >> 8 is var k && k < ExtrusionTable.Current.FanKinds.Length ? ExtrusionTable.Current.FanKinds[k] : null;

    private static BladeStyleRow StyleRow(int style)
    {
        var rows = AirTable.Current.BladeStyles;
        return rows[Math.Clamp(style, 0, rows.Length - 1)];
    }

    public static AirframeKind? AirframeOf(ushort wall) => wall < ExtrusionTable.Current.Airframes.Length ? ExtrusionTable.Current.Airframes[wall] : null;

    /// <summary>A wing's skin and ribs, m3. Skin thickens with chord's root, as a big part file's does: 27 kg for a 1.5 by 2 m metal wing.</summary>
    public static float WingVolume(in ExtrusionKey k)
    {
        var chord = k.W / 100f;
        return chord * (k.L / 100f) * WingSkin * MathF.Sqrt(MathF.Max(1, chord / 1.5f));
    }

    /// <summary>A blade to throw off a rotor of this radius, chord and style: sizes to the 5 cm step, so a rotor's blades are one piece each.</summary>
    public static ConstructPartDef? Blade(float radius, float chord, int style, byte material)
    {
        static ushort Step(float metres) => (ushort)(Math.Clamp((int)MathF.Round(metres * 20), 2, 2000) * StepCm);
        return material == 0 ? null : Def(new ExtrusionKey(0, Step(chord), Step(radius), 0, material, false, ExShape.Blade, (ushort)style));
    }

    /// <summary>A fan's solid material, m3: its blades and its hub.</summary>
    public static float FanVolume(in ExtrusionKey k)
    {
        var r = k.H * 0.005f;
        var kind = FanKindOf(k.Wall);
        var chord = r * (kind?.ChordShare ?? 0.1f);
        return FanBladesOf(k.Wall) * r * chord * chord * 0.12f * StyleRow(FanStyleOf(k.Wall)).Area + r * r * MathF.Min(r, 4) * 0.02f; // (a hub past 4 m of radius grows with its disc, not its volume)
    }

    private static bool Valid(in ExtrusionKey k)
    {
        if (k.Shape == ExShape.Fan)
            return k.H is >= MinFanCm and <= MaxFanCm && k.H % StepCm == 0 && k.W == 0 && k.L == 0 && k.Mask == 0 && FanBladesOf(k.Wall) is >= 2 and <= ConstructRotor.MaxBlades
                && FanStyleOf(k.Wall) < AirTable.Current.BladeStyles.Length && FanKindOf(k.Wall) != null && ExtrusionTable.Current.FindFan(k.Material) != null;
        if (k.Shape == ExShape.Blade)
            return k.H is >= 10 and <= MaxFanCm && k.W is >= 10 and <= MaxFanCm && k.W % StepCm == 0 && k.H % StepCm == 0 && k.L == 0 && k.Mask == 0 && k.Wall < AirTable.Current.BladeStyles.Length && k.Material != 0;
        if (k.Shape == ExShape.Wing)
            return k.W is >= MinChordCm and <= MaxChordCm && k.W % StepCm == 0 && k.L is >= StepCm and <= MaxLengthCm && k.L % StepCm == 0 && k.H == 0 && k.Mask == 0
                && AirframeOf(k.Wall) != null && ExtrusionTable.Current.FindWing(k.Material) != null;
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
        if (k.Shape == ExShape.Fan)
            return BuildFan(k);
        if (k.Shape == ExShape.Blade)
            return BuildBlade(k);
        if (k.Shape == ExShape.Wing)
            return BuildWing(k);
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
        d.SurfaceOverride = SurfaceRules.OfBulk(names.NameOf(k.Material));
        SurfaceRules.Apply(d);
        d.SolidBoxes = n;
        d.BoundsMin = min;
        d.BoundsMax = max;
        if (!k.Growing && AirfoilOf(k.Mask) is { } foil)
        {
            // An airfoil flies: its chord across x with its leading edge at -x, its span the length (ConstructAero.cs).
            d.Surface = new SurfaceDef { Area = w * len, At = new Vector3(0, 0, len * 0.5f), Normal = Vector3.UnitY, Forward = -Vector3.UnitX, Cl0 = foil.Cl0, Stall = foil.StallDeg * MathF.PI / 180f, Cd0 = foil.Cd0 };
            d.Surface.Shape(MathF.Max(1, 2 * len / w), 0);
            d.Description = $"An extruded airfoil: {w:0.##} m of chord, {len:0.##} m of span, of {names.NameOf(k.Material)}. It lifts.";
        }
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
        d.SurfaceOverride = SurfaceRules.OfBulk(names.NameOf(k.Material));
        SurfaceRules.Apply(d);
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
        d.SurfaceOverride = SurfaceRules.OfBulk(names.NameOf(k.Material));
        SurfaceRules.Apply(d);
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

    private static readonly ConstructPartDef?[,] FanConfigs = new ConstructPartDef?[ConstructRotor.MaxBlades + 1, MaxBladeStyles];

    /// <summary>A blade off a rotor, as scrap: one hidden box to collide with, and the style's four stations drawn root to tip along its +x.</summary>
    private static ConstructPartDef BuildBlade(in ExtrusionKey k)
    {
        var names = RecyclingTable.Current;
        var style = StyleRow(k.Wall);
        float len = k.H / 100f, chord = k.W / 100f, thick = MathF.Max(0.012f, chord * 0.06f);
        var row = ExtrusionTable.Current.FindFan(k.Material) ?? ExtrusionTable.Current.FanRows.FirstOrDefault() ?? ExtrusionTable.Current.Rows[0];
        var d = Stats(k, row, 0, len * chord * 0.12f * chord * style.Area, thick, $"A {style.Label.ToLowerInvariant()} rotor blade, thrown off its hub. Scrap.");
        var colour = names.ColourOf(k.Material);
        var boxes = new CPartBox[5];
        var lo = new Vector3(float.MaxValue);
        var hi = new Vector3(float.MinValue);
        for (var s = 0; s < 4; s++)
        {
            var st = style.Stations[s];
            var half = new Vector3(len / 8, chord * st.X * 0.5f, thick);
            var at = new Vector3(len * (s + 0.5f) / 4, st.Y * len, 0);
            boxes[s + 1] = new CPartBox(at, half + new Vector3(0, 0, 0.001f * s), colour, Detail: true);
            lo = Vector3.Min(lo, at - half);
            hi = Vector3.Max(hi, at + half);
        }
        boxes[0] = new CPartBox((lo + hi) * 0.5f, (hi - lo) * 0.5f, colour, Hidden: true);
        d.Boxes = boxes;
        d.SolidBoxes = 1;
        d.BoundsMin = lo;
        d.BoundsMax = hi;
        d.Section = "Rotors";
        d.Sockets = [new CSocket("root", new Vector3(lo.X, (lo.Y + hi.Y) * 0.5f, 0), 1, SocketKind.Struct)];
        return d;
    }

    /// <summary>
    /// A whole rotor off a Fan Former. Its origin is the back of its hub, where it mounts, and it pushes along its +z. What it can
    /// take and how fast it turns follow from its size by its kind's row; its blades, drawn turning, are one box the renderer
    /// repeats round the hub; its two hidden arms are what it collides with (ConstructRotor.Ray: rays meet the blades themselves).
    /// </summary>
    /// <summary>
    /// A wing or a control surface off an Airframe Former: its span along +z from the die, its chord across x with the leading edge
    /// at -x, as an extruded airfoil; its numbers are its row of "airframes", its skin the "wings" row of its material. A wing is the
    /// same from either end, so a left wing welds by its base and a right one by its tip. A swept kind steps back along its span, so a
    /// left and a right are two rows. Cloth tears. A control surface's whole body is its leaf, hinged along its leading edge.
    /// </summary>
    private static ConstructPartDef BuildWing(in ExtrusionKey k)
    {
        var row = ExtrusionTable.Current.FindWing(k.Material)!;
        var kind = AirframeOf(k.Wall)!;
        var names = RecyclingTable.Current;
        var cloth = row.Class == "cloth";
        float chord = k.W / 100f, span = k.L / 100f, half = chord * 0.5f, thick = MathF.Max(0.04f, chord * 0.08f);
        var skin = names.ColourOf(k.Material);
        var d = Stats(k, row, 0, WingVolume(k), thick, $"{kind.Label} off an Airframe Former: {chord:0.##} m of chord, {span:0.##} m of span, of {names.NameOf(k.Material)}.");
        var list = new List<CPartBox>();
        var bound = new Vector3(half, thick * 0.5f, 0);
        var min = -bound;
        var max = bound with { Z = span };
        var sweep = MathF.Tan(MathF.Abs(kind.Sweep) * MathF.PI / 180f) * span; // (how far back one end sits; a row of plus sweep: its far end, of minus: its near end)
        if (sweep == 0)
        {
            list.Add(new CPartBox(new Vector3(0, 0, span * 0.5f), new Vector3(half, thick * 0.5f, span * 0.5f), skin, Hidden: true, Paintable: false));
            var nose = PartShapes.For(new PartBox { Shape = "rounded", Size = Vector3.One, Radius = 0.5f, Edges = "long" });
            var leaf = kind.Control > 0;
            byte side = leaf ? JointSides.Leaf : (byte)0;
            list.Add(new CPartBox(new Vector3(chord * 0.08f, 0, span * 0.5f), new Vector3(half * 0.8f, thick * 0.42f, span * 0.495f), skin, Detail: true, Side: side));
            list.Add(new CPartBox(new Vector3(-half * 0.8f, 0, span * 0.5f), new Vector3(half * 0.2f, thick * 0.38f, span * 0.495f), skin, Detail: true, Shape: nose, Side: side));
        }
        else
        {
            const int steps = 4;
            for (var s = 0; s < steps; s++)
            {
                var along = (s + 0.5f) / steps;
                var off = sweep * (kind.Sweep > 0 ? along : 1 - along);
                var inset = 0.002f * s; // (no two steps share a face)
                list.Add(new CPartBox(new Vector3(off, 0, span * (s + 0.5f) / steps), new Vector3(half, thick * 0.5f - inset, span * 0.5f / steps - inset), skin));
                min = Vector3.Min(min, new Vector3(off - half, -thick * 0.5f, 0));
                max = Vector3.Max(max, new Vector3(off + half, thick * 0.5f, span));
            }
        }
        d.Boxes = list.ToArray();
        d.SolidBoxes = sweep == 0 ? 1 : list.Count;
        d.BoundsMin = min;
        d.BoundsMax = max;
        d.Section = kind.Control > 0 ? "Control surfaces" : "Wings";
        d.Class = k.W < 400 ? ConstructParts.Small : k.W < 1600 ? ConstructParts.Medium : k.W < 3200 ? ConstructParts.Large : ConstructParts.Capital;
        d.OnBreak = cloth ? PartBreak.Disable : PartBreak.Detach;
        var sized = d.Class;
        if (k.Growing)
        {
            d.Sockets = [new CSocket("base", Vector3.Zero, 5, SocketKind.Die, sized)];
            return d;
        }
        d.Sockets = Sockets(k, chord, thick, span, min, max);
        if (sweep != 0)
        {
            d.Sockets[0] = d.Sockets[0] with { At = new Vector3(kind.Sweep < 0 ? sweep : 0, 0, 0) };
            d.Sockets[1] = d.Sockets[1] with { At = new Vector3(kind.Sweep < 0 ? 0 : sweep, 0, span) };
        }
        for (var i = 0; i < d.Sockets.Length; i++)
            d.Sockets[i] = d.Sockets[i] with { Class = sized };
        d.Surface = new SurfaceDef
        {
            Area = chord * span,
            At = kind.Control > 0 ? new Vector3(0, 0, span * 0.5f) : new Vector3(sweep * 0.5f - chord * 0.25f, 0, span * 0.5f),
            Normal = Vector3.UnitY,
            Forward = -Vector3.UnitX,
            Cl0 = kind.Cl0,
            Stall = kind.StallDeg * MathF.PI / 180f,
            Cd0 = kind.Cd0 * (cloth ? 1.6f : 1),
            Tears = cloth,
        };
        d.Surface.Shape(kind.Aspect, MathF.Abs(kind.Sweep));
        if (kind.Control > 0)
        {
            d.Surface.Control = kind.Control * MathF.PI / 180f;
            d.Surface.ControlRate = 90 * MathF.PI / 180f;
            d.Surface.Effect = kind.Effect;
            d.Surface.Sign = kind.Sign;
            d.Surface.Hinge = new Vector3(-half, 0, span * 0.5f);
            d.DataIn = ["angle"];
            ConstructConfig.ForSurface(d);
        }
        return d;
    }

    private static ConstructPartDef BuildFan(in ExtrusionKey k)
    {
        var row = ExtrusionTable.Current.FindFan(k.Material)!;
        var kind = FanKindOf(k.Wall)!;
        var names = RecyclingTable.Current;
        var blades = FanBladesOf(k.Wall);
        float across = k.H / 100f, r = across * 0.5f, chord = r * kind.ChordShare, hub = MathF.Max(0.08f, r * 0.12f), deep = MathF.Max(0.12f, r * 0.14f);
        var disc = MathF.PI * r * r;
        var style = FanStyleOf(k.Wall);
        var bladeVolume = r * chord * chord * 0.12f * StyleRow(style).Area;
        var d = Stats(k, row, 0, FanVolume(k), chord * 0.12f, $"A {kind.Label.ToLowerInvariant()} off a Fan Former: {across:0.##} m across, {blades} blades of {names.NameOf(k.Material)}.");
        var colour = names.ColourOf(k.Material);
        var lathe = PartShapes.For(new PartBox { Shape = "cylinder", Size = Vector3.One, Segments = WheelSides, Inner = -1 });
        var mid = new Vector3(0, 0, deep * 0.5f);
        var armHalf = MathF.Max(0.02f, chord * 0.3f);
        var bladeHalf = new Vector3((r - hub) * 0.5f, chord * 0.5f, MathF.Max(0.012f, chord * 0.06f));
        var list = new List<CPartBox>
        {
            new(mid, new Vector3(hub, hub, deep * 0.5f), new Vector3(0.3f, 0.32f, 0.35f)),
            new(mid, new Vector3(r, armHalf, armHalf), colour, Hidden: true, Paintable: false),
            new(mid, new Vector3(armHalf, r, armHalf), colour, Hidden: true, Paintable: false),
            new(mid + new Vector3(hub + bladeHalf.X, 0, 0), bladeHalf, colour, Detail: true, Spin: true),
        };
        if (kind.Duct > 1)
        {
            var ring = PartShapes.For(new PartBox { Shape = "tube", Size = Vector3.One, Segments = PipeSides, Inner = MathF.Round(r / (r + 0.06f) * 128) / 128, Ends = "open" });
            list.Add(new CPartBox(mid, new Vector3(r + 0.06f, r + 0.06f, deep), new Vector3(0.28f, 0.3f, 0.33f), Detail: true, Shape: ring));
        }
        d.Boxes = list.ToArray();
        d.SolidBoxes = 3;
        d.BoundsMin = new Vector3(-r, -r, 0);
        d.BoundsMax = new Vector3(r, r, deep);
        d.Role = PartRole.Rotor;
        d.Section = "Rotors";
        d.Class = k.H < 400 ? ConstructParts.Small : k.H < 1600 ? ConstructParts.Medium : k.H < 4800 ? ConstructParts.Large : ConstructParts.Capital;
        d.Sockets = [new CSocket("base", Vector3.Zero, 5, k.Growing ? SocketKind.Die : SocketKind.Struct, d.Class)];
        d.OnBreak = PartBreak.Detach;
        d.Friction = 0.6f;
        d.Rotor = new RotorDef
        {
            Radius = r,
            RatedKw = kind.KwPerM2 * disc,
            MotorKw = kind.MotorPerM2 * disc,
            RatedOmega = kind.TipSpeed / r,
            Blades = blades,
            BaseBlades = 3,
            Style = style,
            Chord = chord,
            Spool = kind.Spool * MathF.Max(1, MathF.Sqrt(r)),
            TopSpeed = kind.TopSpeed,
            Duct = kind.Duct,
            Heli = kind.Heli,
            At = mid,
            Axis = Vector3.UnitZ,
            Forward = Vector3.UnitY,
            BladeHealth = MathF.Max(5, bladeVolume * row.HealthPerM3),
        };
        d.DataIn = kind.Heli ? ["collective", "cyclic pitch", "cyclic roll", ConstructModules.Bus] : ["throttle", ConstructModules.Bus];
        d.NamedPorts = [new NamedPort { Name = "Drive in", Type = RouteType.Shaft, In = true, Socket = 0, Byte = 0, At = Vector3.Zero, Face = 5 }];
        d.DriveInSocket = 0;
        if (FanConfigs[blades, style] is { } made)
        {
            d.Config = made.Config;
            d.CfgSlot = made.CfgSlot;
        }
        else
        {
            ConstructConfig.ForRotor(d); // (its blades and style are the settings' defaults, so a made prop carries them)
            FanConfigs[blades, style] = d;
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
            ExShape.Fan => $"{FanWord}{k.Wall >> 8}.{StyleRow(FanStyleOf(k.Wall)).Id}.{FanBladesOf(k.Wall)}",
            ExShape.Blade => BladeWord + StyleRow(k.Wall).Id,
            ExShape.Wing => WingWord + (AirframeOf(k.Wall)?.Id ?? ""),
            _ => PresetOf(k.Mask) is var p and >= 0 ? Presets[p].Id : AirfoilOf(k.Mask)?.Id ?? k.Mask.ToString("x16", CultureInfo.InvariantCulture),
        };
        return $"{Prefix}{profile} {k.W}x{k.H}x{k.L} {RecyclingTable.Current.NameOf(k.Material)}{(k.Growing ? GrowingSuffix : "")}";
    }

    public const string Prefix = "Extrusion ";
    private const string GrowingSuffix = " growing";
    private const string RoundWord = "round", PipeWord = "pipe", WheelWord = "wheel", FanWord = "fan", BladeWord = "blade-", WingWord = "wing-";

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
        else if (Array.Find(ExtrusionTable.Current.Airfoils, f => f.Id.Equals(words[0], StringComparison.OrdinalIgnoreCase)) is { } foil)
            mask = foil.Mask;
        else if (words[0].StartsWith(FanWord, StringComparison.OrdinalIgnoreCase))
        {
            // "fan3.scimitar.8": its kind, style and blades, by name (a style's row may move, a name doesn't). "fan51": before styles, kind times 16 plus blades.
            var bits = words[0][FanWord.Length..].Split('.');
            if (bits.Length == 3)
            {
                var row = Array.FindIndex(AirTable.Current.BladeStyles, x => x.Id == bits[1]);
                if (row < 0 || !int.TryParse(bits[0], out var kind) || !int.TryParse(bits[2], out var count))
                    return null;
                wall = FanWall(kind, row, count);
            }
            else if (ushort.TryParse(bits[0], out var old))
                wall = FanWall(old >> 4, 0, old & 15);
            else
                return null;
            shape = ExShape.Fan;
        }
        else if (words[0].StartsWith(BladeWord, StringComparison.OrdinalIgnoreCase))
        {
            var row = Array.FindIndex(AirTable.Current.BladeStyles, x => x.Id == words[0][BladeWord.Length..]);
            if (row < 0)
                return null;
            wall = (ushort)row;
            shape = ExShape.Blade;
        }
        else if (words[0].StartsWith(WingWord, StringComparison.OrdinalIgnoreCase))
        {
            var row = Array.FindIndex(ExtrusionTable.Current.Airframes, x => x.Id == words[0][WingWord.Length..]);
            if (row < 0)
                return null;
            wall = (ushort)row;
            shape = ExShape.Wing;
        }
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
        if (d.IsOrdnance)
        {
            w.U8(OrdnanceWire);
            Ordnance.Write(w, d);
            return;
        }
        if (d.IsNamedRuntime)
        {
            w.U8(HullLoft.WireKind);
            HullLoft.Write(w, d);
            return;
        }
        if (d.IsRackRuntime)
        {
            w.U8(ModularRack.WireKind);
            ModularRack.Write(w, d);
            return;
        }
        if (d.MatBase != null)
        {
            w.U8(PartMaterials.WireKind);
            PartMaterials.Write(w, d); // (protocol 194: a part of another material, by its file part and the material's name)
            return;
        }
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
            case ExShape.Fan:
                w.U8(FanWire).U16(k.Wall);
                break;
            case ExShape.Blade:
                w.U8(BladeWire).U16(k.Wall);
                break;
            case ExShape.Wing:
                w.U8(WingWire).U16(k.Wall);
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
        if (profile == OrdnanceWire)
            return Ordnance.Read(ref r);
        if (profile == ModularRack.WireKind)
            return ModularRack.Read(ref r);
        if (profile == PartMaterials.WireKind)
            return PartMaterials.Read(ref r);
        if (profile == HullLoft.WireKind)
            return HullLoft.Read(ref r);
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
        else if (profile is BladeWire)
        {
            shape = ExShape.Blade;
            wall = r.U16();
        }
        else if (profile is FanWire)
        {
            shape = ExShape.Fan;
            wall = r.U16();
        }
        else if (profile is WingWire)
        {
            shape = ExShape.Wing;
            wall = r.U16();
        }
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
