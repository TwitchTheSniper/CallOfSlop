using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>
/// Bake kits (Sean, 2026-11-23: "large deck parts should be made through the extrusion chain in the factory, put together, and then baked in
/// the oven, not made in an assembler"). A part listed in Constructs/BakeKits.json has a kit: one extrusion for each of its drawn boxes, the
/// piece's section and length the box's size, painted and finished as the box is, at the box's place and turn. A Building Station builds
/// the kit as a welded group (its blueprint <c>{part} kit</c>, made here at run time), and a Bake Oven bakes that group into the part
/// itself, its own name, sockets, mass and look (<see cref="BakedParts.Bake"/> asks <see cref="Target"/> before it makes a
/// <c>Baked (#hash)</c>), so a ship's blueprint, its saves and its fittings know the part as before. The kit's design is matched at any of
/// the 24 grid turns. Docs: production.md "Bake kits".
/// </summary>
public static class BakeKits
{
    public const string Format = "call-of-slop-bake-kits/1";
    public const string KitSuffix = " kit";

    /// <summary>
    /// One piece of a kit: the extrusion (or, with <paramref name="Name"/>, a run-time part by its key, such as a plain Hull Section a Panel
    /// Former forms), where its origin sits in the part's frame and its turn, its paint and its finish's name.
    /// </summary>
    public readonly record struct Piece(ExtrusionKey Key, Vector3 Pos, byte Rot, uint Paint, string Finish, string? Name = null, string? Dress = null)
    {
        public string PartName => Name ?? Extrusion.Name(Key);

        public ConstructPartDef? Def => Name != null ? ConstructParts.Find(Name) : Extrusion.Def(Key);
    }

    public const string ListFormat = "call-of-slop-bake-kit-list/1";

    /// <summary>A kit's design at one turn: the part it bakes into, the part's turn and its origin from the design's origin.</summary>
    public readonly record struct Hit(ConstructPartDef Part, byte Rot, Vector3 Offset);

    public static string FilePath => Path.Combine(AppFolders.Data, "Constructs", "BakeKits.json");

    /// <summary>Kits a generator lists piece by piece (Constructs/BakeKits/*.json, format <see cref="ListFormat"/>): the Zero Day's keel, formed skins.</summary>
    public static string ListFolder => Path.Combine(AppFolders.Data, "Constructs", "BakeKits");

    private static string[]? _names;
    private static string _material = "steel";
    private static readonly Dictionary<string, Piece[]?> Kits = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, ConstructBuild.Blueprint?> Prints = new(StringComparer.OrdinalIgnoreCase);
    private static Dictionary<string, List<Hit>>? _targets;
    private static readonly Dictionary<string, Piece[]> Listed = new(StringComparer.OrdinalIgnoreCase); // (by part: the kit's pieces as listed)
    private static readonly object Gate = new();

    private static int _bigBedCells;
    private static string[] _bigBed = [];

    /// <summary>
    /// The least bed (2 m cells) an oven needs to bake this part: BakeKits.json "bigBed" (Sean, 2026-11-23: the frigate's decks, keel and hull
    /// pieces on line C's big oven; its walls on lines A and B, whose smaller ovens a chain prefers). 0: any bed that holds it.
    /// </summary>
    public static int MinBedCells(ConstructPartDef d)
    {
        _ = Names; // (loaded)
        foreach (var p in _bigBed)
            if (d.Name.StartsWith(p, StringComparison.OrdinalIgnoreCase))
                return _bigBedCells;
        return 0;
    }

    /// <summary>The parts made from kits (Constructs/BakeKits.json "parts").</summary>
    public static string[] Names
    {
        get
        {
            if (_names != null)
                return _names;
            lock (Gate)
                return _names ??= [.. Load(FilePath).Concat(LoadLists(ListFolder)).Distinct(StringComparer.OrdinalIgnoreCase)];
        }
    }

    private static string[] Load(string path)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var root = doc.RootElement;
            if (!root.TryGetProperty("format", out var f) || f.GetString() != Format)
                throw new FormatException($"\"format\" must be \"{Format}\"");
            if (root.TryGetProperty("material", out var m) && m.GetString() is { Length: > 0 } mat)
                _material = mat;
            var list = new List<string>();
            if (root.TryGetProperty("bigBed", out var bb))
            {
                _bigBedCells = bb.TryGetProperty("cells", out var cells) ? cells.GetInt32() : 0;
                if (bb.TryGetProperty("prefixes", out var pre))
                    _bigBed = [.. pre.EnumerateArray().Select(x => x.GetString()).OfType<string>().Where(x => x.Length > 0)];
            }
            if (root.TryGetProperty("parts", out var ps))
                foreach (var p in ps.EnumerateArray())
                    if (p.GetString() is { Length: > 0 } n)
                        list.Add(n);
            return [.. list];
        }
        catch (Exception e) when (e is IOException or JsonException or FormatException or InvalidOperationException or UnauthorizedAccessException)
        {
            Console.WriteLine($"[bake kits] REFUSED {path}: {e.Message}");
            return [];
        }
    }

    /// <summary>Every kit list in the folder (none: no folder): each kit's part and pieces, a bad file or kit refused with a reason.</summary>
    private static List<string> LoadLists(string folder)
    {
        var names = new List<string>();
        if (!Directory.Exists(folder))
            return names;
        foreach (var path in Directory.GetFiles(folder, "*.json").Order(StringComparer.Ordinal))
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
                var root = doc.RootElement;
                if (!root.TryGetProperty("format", out var f) || f.GetString() != ListFormat)
                    throw new FormatException($"\"format\" must be \"{ListFormat}\"");
                foreach (var k in root.GetProperty("kits").EnumerateArray())
                {
                    var part = k.GetProperty("part").GetString() ?? throw new FormatException("a kit with no \"part\"");
                    var pieces = new List<Piece>();
                    foreach (var q in k.GetProperty("pieces").EnumerateArray())
                    {
                        var at = q.GetProperty("at");
                        var pos = new Vector3(MathF.Round(at[0].GetSingle() * 100), MathF.Round(at[1].GetSingle() * 100), MathF.Round(at[2].GetSingle() * 100)) / 100f; // (a part's place rounds to the centimetre)
                        var rot = q.TryGetProperty("rot", out var r) ? r.GetByte() : (byte)0;
                        var paint = q.TryGetProperty("paint", out var pa) && ConstructPaint.TryParse(pa.GetString(), out var pv) ? pv : 0u;
                        var finish = q.TryGetProperty("finish", out var fi) ? fi.GetString() ?? "" : "";
                        var dress = q.TryGetProperty("dress", out var dr) ? dr.GetString() : null; // ("STYLE SALT EXTRAS": a plain skin dressed at the station before the bake)
                        pieces.Add(new Piece(default, pos, rot, paint, finish, q.GetProperty("part").GetString() ?? throw new FormatException($"{part}: a piece with no \"part\""), dress));
                    }
                    if (rot24Bad(pieces) is { } bad)
                        throw new FormatException($"{part}: {bad}");
                    Listed[part] = [.. pieces];
                    names.Add(part);
                }
            }
            catch (Exception e) when (e is IOException or JsonException or FormatException or InvalidOperationException or KeyNotFoundException or UnauthorizedAccessException or IndexOutOfRangeException)
            {
                Console.WriteLine($"[bake kits] REFUSED {path}: {e.Message}");
            }
        return names;

        static string? rot24Bad(List<Piece> ps) => ps.Count is < 1 or > BakedParts.MaxSubparts ? $"{ps.Count} pieces (1 to {BakedParts.MaxSubparts}; 2 once dressed)"
            : ps.FindIndex(p => p.Rot >= Rot24.Count) is var i and >= 0 ? $"piece {i}: turn {ps[i].Rot} (0 to {Rot24.Count - 1})" : null;
    }

    /// <summary>
    /// Is this part made from a kit (baked, never assembled by a block Assembler)? A part BakeKits.json or Constructs/BakeKits/ names, or by
    /// the rule (Assembler retirement phase 3, Sean 2026-11-28): every placed part with an Assembler row, no press preset and no tool block
    /// whose own boxes make a kit (<see cref="ByRule"/>). Worked out once a part and kept by its id: it is asked in looks that must not allocate.
    /// </summary>
    public static bool Has(ConstructPartDef d)
    {
        if (d.IsNamedRuntime || d.IsExtrusion || Extrusion.IsRuntimeId((ushort)d.Id))
            return false; // (a run-time part, an Ord Rack or an ordnance design, AssembledParts: the Assembler's still)
        var id = d.Id;
        var defs = _hasDef;
        if ((uint)id < (uint)defs.Length && ReferenceEquals(defs[id], d) && ReferenceEquals(_hasTable, AssemblingTable.Current))
            return _has[id];
        return HasNow(d);
    }

    private static ConstructPartDef?[] _hasDef = [];
    private static bool[] _has = [];
    private static AssemblingTable? _hasTable;

    private static bool HasNow(ConstructPartDef d)
    {
        bool v;
        if (IsNamed(d.Name))
            v = !IsListed(d.Name) || KitOf(d, true) != null; // (a listed kit that can't be made leaves its part to the Assembler: else nothing makes it)
        else
            v = ByRule(d) && KitOf(d, true) != null;
        lock (Gate)
        {
            if (!ReferenceEquals(_hasTable, AssemblingTable.Current))
            {
                (_hasTable, _hasDef, _has) = (AssemblingTable.Current, [], []);
            }
            var n = Math.Max(ConstructParts.All.Count, d.Id + 1);
            if (_hasDef.Length < n)
            {
                Array.Resize(ref _hasDef, n);
                Array.Resize(ref _has, n);
            }
            if (d.Id >= 0 && d.Id < _hasDef.Length)
                (_hasDef[d.Id], _has[d.Id]) = (d, v);
        }
        return v;
    }

    private static bool IsNamed(string name)
    {
        foreach (var n in Names)
            if (n.Equals(name, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    /// <summary>
    /// Is this part baked by the rule (Assembler retirement phase 3): a placed part (welded on, never in the hands) with an Assembler row,
    /// no press preset (a die makes it) and no tool block (the Thruster Jig and Sewing Table rows are phase 4)? Its kit is worked out from
    /// its own boxes, its components and its rubber or concrete (<see cref="Make"/>).
    /// </summary>
    public static bool ByRule(ConstructPartDef d) =>
        d.PhysicsOnly && !d.IsNamedRuntime && !d.IsExtrusion && AssemblingTable.Current.Problem.Length == 0
        && AssemblingTable.Current.Of((ushort)d.Id) is { Tool.Length: 0 } && PressTable.Current.PresetKeyOf(d) == null;

    /// <summary>
    /// Does an Assembler refuse to build this part (it is a Bake Oven's)? One with a bed (<paramref name="bed"/>: Assembly Bed Blocks
    /// welded beside its Core) refuses every part <see cref="Has"/> names; a bare Core, the survival start's Starter Assembler, still builds
    /// the rule's parts until the start gets its own oven and station (phase 6), so a survival world can make its first Bake Oven, Building
    /// Station and Extruder. A listed kit's part every Assembler refuses, as before.
    /// </summary>
    public static bool AssemblerRefuses(ConstructPartDef made, bool bed) =>
        Has(made) && (bed || IsNamed(made.Name));

    /// <summary>
    /// A kit's split into sub-kits (Sean, 2026-11-28: "utilize the different oven sizes in the factory layout"): a node's pieces, its
    /// bounds' sides, the smallest oven that holds it (<see cref="MakerRoutes.Ovens"/>; their count: none) and its sub-kits. A leaf of two
    /// or more pieces bakes them on its oven; a node with sub-kits bakes the baked sub-kits (and nothing else) on its own; a leaf of one
    /// piece is the piece itself.
    /// </summary>
    public sealed class SplitNode
    {
        public List<int> Pieces = [];
        public Vector3 Size;
        public int Oven;
        public List<SplitNode> Kids = [];
    }

    /// <summary>
    /// A kit split along its boxes until every sub-kit fits the smallest oven, or is one piece (a piece no small bed holds, a railgun
    /// barrel's, needs its own bigger bed). Each cut is the gap between pieces along the bounds' longest side that leaves the smaller
    /// halves' ovens (then the more even halves). Null: no kit.
    /// </summary>
    public static SplitNode? Split(ConstructPartDef d)
    {
        if (KitOf(d) is not { } kit)
            return null;
        var all = new List<int>(kit.Length);
        for (var i = 0; i < kit.Length; i++)
            all.Add(i);
        return SplitOf(kit, all, 0);
    }

    private static SplitNode SplitOf(Piece[] kit, List<int> set, int depth)
    {
        var (lo, hi) = Bounds(kit, 0, 0, set);
        var node = new SplitNode { Pieces = set, Size = hi - lo, Oven = MakerRoutes.OvenFor(hi - lo) };
        if (node.Oven == 0 || set.Count < 2 || depth > 16)
            return node;
        // Along the bounds' longest side, the pieces by their middles; each gap between two is a cut.
        var size = hi - lo;
        var ax = size.X >= size.Y && size.X >= size.Z ? 0 : size.Y >= size.Z ? 1 : 2;
        var mids = new float[set.Count];
        for (var k = 0; k < set.Count; k++)
        {
            var (plo, phi) = Bounds(kit, 0, 0, [set[k]]);
            mids[k] = Axis((plo + phi) * 0.5f, ax);
        }
        var order = set.ToArray();
        Array.Sort(mids, order);
        int best = -1, bestOven = int.MaxValue, bestEven = int.MaxValue;
        for (var cut = 1; cut < order.Length; cut++)
        {
            if (mids[cut] - mids[cut - 1] < 1e-3f)
                continue;
            var (alo, ahi) = Bounds(kit, 0, 0, [.. order[..cut]]);
            var (blo, bhi) = Bounds(kit, 0, 0, [.. order[cut..]]);
            var ov = Math.Max(MakerRoutes.OvenFor(ahi - alo), MakerRoutes.OvenFor(bhi - blo));
            var even = Math.Abs(order.Length - 2 * cut);
            if (ov < bestOven || ov == bestOven && even < bestEven)
                (best, bestOven, bestEven) = (cut, ov, even);
        }
        if (best < 0)
            return node; // (every piece's middle in one place: it doesn't split)
        node.Kids.Add(SplitOf(kit, [.. order[..best]], depth + 1));
        node.Kids.Add(SplitOf(kit, [.. order[best..]], depth + 1));
        // (a halving that frees nothing, both halves on this node's own oven, is no help: kept whole)
        if (node.Kids.TrueForAll(k => k.Oven >= node.Oven && k.Kids.Count == 0))
            node.Kids.Clear();
        return node;
    }

    private static readonly HashSet<ExtrusionKey> OnePieceKeys = [];

    /// <summary>Is this extrusion the whole kit of a part worked out so far (a one-piece bake)? Allocates nothing.</summary>
    public static bool IsOnePieceKey(in ExtrusionKey k)
    {
        lock (Gate)
            return OnePieceKeys.Contains(k with { Growing = false });
    }

    /// <summary>Is this part's kit one extrusion (a one-piece bake: it rolls from an Extruder straight onto an oven's bed, no station)?</summary>
    public static bool OnePiece(ConstructPartDef d) => KitOf(d) is { Length: 1 } k && k[0].Name == null;

    /// <summary>A kit's bounds (m): its pieces' solid boxes at their places, components and sheets with them. Zero: no kit.</summary>
    public static Vector3 KitSize(ConstructPartDef d)
    {
        if (KitOf(d) is not { } kit)
            return Vector3.Zero;
        var (lo, hi) = Bounds(kit, 0, kit.Length, null);
        return hi - lo;
    }

    /// <summary>The solid bounds of pieces <paramref name="from"/> to <paramref name="to"/> (or those <paramref name="only"/> lists) in the part's frame.</summary>
    internal static (Vector3 Lo, Vector3 Hi) Bounds(Piece[] kit, int from, int to, List<int>? only)
    {
        Vector3 lo = new(float.MaxValue), hi = new(float.MinValue);
        var n = only?.Count ?? to - from;
        for (var k = 0; k < n; k++)
        {
            var p = kit[only != null ? only[k] : from + k];
            if (p.Def is not { } pd)
                continue;
            var c = p.Pos + Rot24.Apply(p.Rot, (pd.BoundsMin + pd.BoundsMax) * 0.5f);
            var h = Rot24.ApplyHalf(p.Rot, (pd.BoundsMax - pd.BoundsMin) * 0.5f);
            (lo, hi) = (Vector3.Min(lo, c - h), Vector3.Max(hi, c + h));
        }
        return lo.X > hi.X ? (Vector3.Zero, Vector3.Zero) : (lo, hi);
    }

    private static bool IsListed(string name)
    {
        lock (Gate)
            return Listed.ContainsKey(name);
    }

    /// <summary>A part's kit (null: it has none, or one of its boxes can't be made: the log says which).</summary>
    public static Piece[]? KitOf(ConstructPartDef d) => Has(d) ? KitOf(d, true) : null;

    private static Piece[]? KitOf(ConstructPartDef d, bool _)
    {
        lock (Gate)
        {
            if (Kits.TryGetValue(d.Name, out var have))
                return have;
            var kit = Listed.TryGetValue(d.Name, out var listed) ? Resolve(d, listed) : Make(d, IsNamed(d.Name) ? null : AssemblingTable.Current.Of((ushort)d.Id));
            Kits[d.Name] = kit;
            if (kit is { Length: 1 } one && one[0].Name == null)
                OnePieceKeys.Add(one[0].Key);
            return kit;
        }
    }

    /// <summary>The most pieces a kit takes: its root and the freeform welds one body holds (<see cref="Freeform.MaxPerConstruct"/>).</summary>
    public const int MaxPieces = Freeform.MaxPerConstruct + 1;

    /// <summary>
    /// One extrusion a box: its longest side the length (5 cm steps), the thinner of the others its height (a Sheet under 10 cm). A part by
    /// the rule (<paramref name="r"/>: its Assembler row, the bill) is extruded in its row's main material where an Extruder takes it,
    /// keeps a lone box as one piece (a one-piece bake), and adds its row's components as kit pieces and its rubber or concrete as a sheet
    /// piece (Sean, 2026-11-28), laid on the part's top face; the oven consumes them, so they still count.
    /// </summary>
    private static Piece[]? Make(ConstructPartDef d, AssembleRecipe? r = null)
    {
        var mat = RecyclingTable.Current.IdOf(_material);
        if (r != null && MainMaterial(r) is var main and not 0 && ExtrusionTable.Current.Find(main) != null)
            mat = main;
        var pieces = new List<Piece>();
        // (a part drawn only by detail and lathe shapes, a wheel or a bomb, has no drawn solid box: its kit is its hidden solid boxes, the
        // shape it collides as)
        var anyDrawn = false;
        for (var i = 0; i < d.SolidBoxes && !anyDrawn; i++)
            anyDrawn = !d.Boxes[i].Hidden;
        for (var i = 0; i < d.SolidBoxes; i++)
        {
            var b = d.Boxes[i];
            if (b.Hidden && (anyDrawn || r == null))
                continue; // (detail boxes, seams and markings, come with the bake: the kit is the part's solid plates and members)
            var size = b.Half * 2 * 100; // cm
            int la = size.X >= size.Y && size.X >= size.Z ? 0 : size.Y >= size.Z ? 1 : 2;
            int a1 = (la + 1) % 3, a2 = (la + 2) % 3;
            var (wa, ha) = Axis(size, a1) >= Axis(size, a2) ? (a1, a2) : (a2, a1);
            var len = Math.Max(Extrusion.StepCm, (int)MathF.Round(Axis(size, la) / Extrusion.StepCm) * Extrusion.StepCm);
            var w = (int)MathF.Round(Axis(size, wa));
            var h = (int)MathF.Round(Axis(size, ha));
            if (h < Extrusion.MinSectionCm)
            {
                // A Sheet (1 to 10 cm thick) when it is wider than thick and at least 10 cm wide; else a bar of the least section.
                h = Math.Max(Extrusion.MinSheetCm, h);
                if (w < Extrusion.MinSectionCm)
                    (w, h) = (Extrusion.MinSectionCm, Extrusion.MinSectionCm);
                else if (w <= h)
                    w = h + 1;
            }
            w = Math.Clamp(w, Extrusion.MinSectionCm, Extrusion.MaxSectionCm);
            h = Math.Min(h, Extrusion.MaxSectionCm);
            var key = new ExtrusionKey(ulong.MaxValue, (ushort)w, (ushort)h, (ushort)Math.Min(len, Extrusion.MaxLengthCm), mat, false);
            if (Extrusion.KeyRefusal(key) is { } why)
            {
                Console.WriteLine($"[bake kits] {(r != null ? "no kit by the rule (its Assembler row stays): " : "REFUSED ")}{d.Name}: box {i} ({size.X:0} x {size.Y:0} x {size.Z:0} cm) makes no piece: {why}");
                return null;
            }
            if (TurnFor(wa, ha, la) is not { } rot)
            {
                Console.WriteLine($"[bake kits] {(r != null ? "no kit by the rule (its Assembler row stays): " : "REFUSED ")}{d.Name}: box {i}: no grid turn lays a piece along it");
                return null;
            }
            // The piece's own frame: its width along x, height along y, length along z from 0 (ConstructExtrusion.BuildSection).
            var pos = b.Center - Rot24.Apply(rot, new Vector3(0, 0, key.L / 200f));
            pos = new Vector3(MathF.Round(pos.X * 100), MathF.Round(pos.Y * 100), MathF.Round(pos.Z * 100)) / 100f;
            var paint = ConstructPaint.FromVector(b.Color);
            pieces.Add(new Piece(key, pos, rot, paint, ConstructFinish.Offered(b.Surface) ? ConstructFinish.Name(b.Surface) : ""));
        }
        // The sections cored to weigh about what the part does (a solid kit of a 25 cm deck is three times its mass, past a hall crane's
        // 20 t): one profile for every piece thick enough, the one whose fill is nearest the part's mass over the solid kit's.
        float solid = 0;
        foreach (var p in pieces)
            solid += Extrusion.Def(p.Key)?.Mass ?? 0;
        if (solid > d.Mass * 1.25f)
        {
            var want = d.Mass / solid;
            var mask = ulong.MaxValue;
            foreach (var pr in Extrusion.Presets)
                if (pr.Id is "solid" or "wall" or "hollow" && MathF.Abs(Extrusion.Fill(pr.Mask) - want) < MathF.Abs(Extrusion.Fill(mask) - want))
                    mask = pr.Mask;
            for (var i = 0; i < pieces.Count; i++)
                if (!Extrusion.IsSheet(pieces[i].Key) && pieces[i].Key with { Mask = mask } is var cored && Extrusion.KeyRefusal(cored) == null && Extrusion.Def(cored) != null)
                    pieces[i] = pieces[i] with { Key = cored };
        }
        if (r != null)
        {
            var extras = Extras(d, r, mat, pieces);
            if (extras == null)
                return null;
            if (pieces.Count == 1 && extras.Count == 0)
            {
                // A one-piece bake (Sean, 2026-11-28: "Yes, skip the station"): the lone extrusion rolls off an Extruder onto the bed as it was
                // made, unpainted, so its design is the plain piece's; the part comes out in its own paint.
                pieces[0] = pieces[0] with { Paint = 0, Finish = "" };
                return [.. pieces];
            }
            pieces.AddRange(extras);
        }
        else if (pieces.Count == 1 && pieces[0].Key.L >= 2 * Extrusion.StepCm)
        {
            // One plate (a plain wall with no doorway): two lengths welded end to end, as a bake takes two or more subparts.
            var p = pieces[0];
            var l0 = p.Key.L / 2 / Extrusion.StepCm * Extrusion.StepCm;
            var second = p.Pos + Rot24.Apply(p.Rot, new Vector3(0, 0, l0 / 100f));
            second = new Vector3(MathF.Round(second.X * 100), MathF.Round(second.Y * 100), MathF.Round(second.Z * 100)) / 100f;
            pieces[0] = p with { Key = p.Key with { L = (ushort)l0 } };
            pieces.Add(p with { Key = p.Key with { L = (ushort)(p.Key.L - l0) }, Pos = second });
        }
        if (pieces.Count < 2 || pieces.Count > MaxPieces)
        {
            Console.WriteLine($"[bake kits] {(r != null ? "no kit by the rule (its Assembler row stays): " : "REFUSED ")}{d.Name}: {pieces.Count} pieces (2 to {MaxPieces})");
            return null;
        }
        var boxes = 0;
        foreach (var p in pieces)
            boxes += p.Def?.Boxes.Length ?? 0;
        if (boxes > ConstructParts.MaxBoxes)
        {
            Console.WriteLine($"[bake kits] {(r != null ? "no kit by the rule (its Assembler row stays): " : "REFUSED ")}{d.Name}: its kit is {boxes} boxes, at most {ConstructParts.MaxBoxes} bake into one part");
            return null;
        }
        Order(pieces, r != null);
        return [.. pieces];
    }

    /// <summary>A row's main material: the one it takes most units of (0: none).</summary>
    private static byte MainMaterial(AssembleRecipe r)
    {
        byte best = 0;
        var most = 0;
        foreach (var (m, u) in r.Materials)
            if (u > most)
                (best, most) = (m, u);
        return best;
    }

    /// <summary>
    /// A rule kit's extra pieces: a rubber or concrete sheet for each of those its row takes that isn't the kit's own material, then its
    /// component parts (each batch's count), laid in rows on the part's top face, a layer over the last when one is full. Null: one can't be
    /// made (the log says which).
    /// </summary>
    private static List<Piece>? Extras(ConstructPartDef d, AssembleRecipe r, byte mat, List<Piece> boxes)
    {
        var extras = new List<Piece>();
        var lo = d.BoundsMin;
        var hi = d.BoundsMax;
        if (boxes.Count == 0)
            return extras;
        var top = hi.Y;
        var size = hi - lo;
        // The sheets: as wide as the part's top face is (0.1 to 32 m), as long as it (5 cm steps), 1 cm thick, along its longer side.
        foreach (var (m, _) in r.Materials)
        {
            if (m == mat || RecyclingTable.Current.NameOf(m) is not ("rubber" or "concrete"))
                continue;
            var alongX = size.X >= size.Z;
            var w = Math.Clamp((int)MathF.Round((alongX ? size.Z : size.X) * 100), Extrusion.MinSectionCm, Extrusion.MaxSectionCm);
            var len = Math.Clamp((int)MathF.Round((alongX ? size.X : size.Z) * 100 / Extrusion.StepCm) * Extrusion.StepCm, Extrusion.StepCm, Extrusion.MaxLengthCm);
            var key = new ExtrusionKey(ulong.MaxValue, (ushort)w, (ushort)Extrusion.MinSheetCm, (ushort)len, m, false);
            if (Extrusion.KeyRefusal(key) is { } why)
            {
                Console.WriteLine($"[bake kits] {(r != null ? "no kit by the rule (its Assembler row stays): " : "REFUSED ")}{d.Name}: its {RecyclingTable.Current.NameOf(m)} sheet makes no piece: {why}");
                return null;
            }
            if (TurnFor(alongX ? 2 : 0, 1, alongX ? 0 : 2) is not { } rot)
                return null;
            // (the sheet's frame: width along x, thickness along y, length along z from 0; its middle over the top face's middle)
            var mid = new Vector3((lo.X + hi.X) * 0.5f, top + Extrusion.MinSheetCm / 200f, (lo.Z + hi.Z) * 0.5f);
            var pos = mid - Rot24.Apply(rot, new Vector3(0, 0, len / 200f));
            extras.Add(new Piece(key, Cm(pos), rot, 0, ""));
            top += Extrusion.MinSheetCm / 100f;
        }
        // The components, in rows over the top face.
        var batches = Math.Max(1, r.Count);
        foreach (var (part, count) in r.Parts)
        {
            var n = (count + batches - 1) / batches;
            var foot = part.BoundsMax - part.BoundsMin + new Vector3(0.01f);
            var cols = Math.Max(1, (int)(size.X / foot.X));
            var rows = Math.Max(1, (int)(size.Z / foot.Z));
            for (var i = 0; i < n; i++)
            {
                var layer = i / (cols * rows);
                var cell = i % (cols * rows);
                var at = new Vector3(lo.X + (cell % cols + 0.5f) * foot.X, top + layer * foot.Y, lo.Z + (cell / cols + 0.5f) * foot.Z)
                    - new Vector3((part.BoundsMin.X + part.BoundsMax.X) * 0.5f, part.BoundsMin.Y, (part.BoundsMin.Z + part.BoundsMax.Z) * 0.5f);
                extras.Add(new Piece(default, Cm(at), 0, 0, "", part.Name));
            }
            top += ((n - 1) / (cols * rows) + 1) * foot.Y;
        }
        return extras;
    }

    private static Vector3 Cm(Vector3 v) => new Vector3(MathF.Round(v.X * 100), MathF.Round(v.Y * 100), MathF.Round(v.Z * 100)) / 100f;

    /// <summary>A listed kit: every piece's part known (null: one isn't, the log says which), the biggest first, as <see cref="Make"/> lays them.</summary>
    private static Piece[]? Resolve(ConstructPartDef d, Piece[] listed)
    {
        // Dressed where its skins can be and the baked group stays within a part's boxes; else its skins plain (said once, in the log).
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var dressed = Expand(d, listed, true);
        if (sw.Elapsed.TotalMilliseconds > 4)
            Console.WriteLine($"[bake kits] {d.Name}: dressing its kit took {sw.Elapsed.TotalMilliseconds:0.0} ms");
        if (dressed != null)
            return dressed;
        Console.WriteLine($"[bake kits] {d.Name}: its kit is its skins plain, undressed (see the line above)");
        return Expand(d, listed, false);
    }

    private static Piece[]? Expand(ConstructPartDef d, Piece[] listed, bool dress)
    {
        var pieces = new List<Piece>(listed.Length * 8);
        foreach (var p in listed)
        {
            if (p.Def == null)
            {
                Console.WriteLine($"[bake kits] REFUSED {d.Name}: no such part as its piece {p.PartName}");
                return null;
            }
            if (p.Dress == null || !dress)
            {
                pieces.Add(p with { Dress = null });
                continue;
            }
            // A dressed skin: the skin and its dress subparts as the Building Station's dress blueprint lays them (HullLoft.Dress.cs), each
            // subpart at the skin's own place and turn, in the blueprint's paint and finish.
            var w = p.Dress.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var why = "STYLE SALT EXTRAS";
            if (w.Length != 3 || !int.TryParse(w[1], NumberStyles.None, CultureInfo.InvariantCulture, out var salt)
                || HullLoft.DressBlueprint(p.PartName, w[0], salt, w[2] == "-" ? "" : w[2], out why) is not { } bp)
            {
                Console.WriteLine($"[bake kits] {d.Name}: its piece {p.PartName} can't be dressed \"{p.Dress}\": {why}");
                return null;
            }
            for (var s = 0; s < bp.Steps.Count; s++)
            {
                var paint = s < bp.StepPaint.Count && ConstructPaint.IsSet(bp.StepPaint[s]) ? bp.StepPaint[s] : s == 0 ? p.Paint : 0;
                var finish = s < bp.StepFinish.Count && bp.StepFinish[s] != 0 ? ConstructFinish.Name(bp.StepFinish[s]) : s == 0 ? p.Finish : "";
                pieces.Add(new Piece(default, p.Pos, p.Rot, paint, finish, bp.Steps[s].Part));
            }
        }
        var boxes = 0;
        foreach (var p in pieces)
            boxes += p.Def?.Boxes.Length ?? 0;
        if (boxes > ConstructParts.MaxBoxes)
        {
            Console.WriteLine($"[bake kits] {(dress ? "" : "REFUSED ")}{d.Name}: {(dress ? "dressed, " : "")}its kit is {boxes} boxes, at most {ConstructParts.MaxBoxes} bake into one part");
            return null;
        }
        if (pieces.Count is < 2 or > BakedParts.MaxSubparts)
        {
            Console.WriteLine($"[bake kits] REFUSED {d.Name}: {pieces.Count} pieces dressed (2 to {BakedParts.MaxSubparts})");
            return null;
        }
        Order(pieces);
        return [.. pieces];
    }

    /// <summary>The biggest piece first: the kit's blueprint stands on it (step 0, unturned when it can be).</summary>
    private static void Order(List<Piece> pieces, bool rule = false)
    {
        pieces.Sort((p, q) => Volume(q).CompareTo(Volume(p)));
        // (a rule kit stands on its biggest extrusion, unturned if one is: never on a component it carries, a Bulkhead Door on a Bearing)
        var first = rule ? pieces.FindIndex(p => p.Rot == 0 && p.Name == null) : pieces.FindIndex(p => p.Rot == 0);
        if (first > 0)
            (pieces[0], pieces[first]) = (pieces[first], pieces[0]);
    }

    private static float Volume(in Piece p) => p.Name == null ? Volume(p.Key) : p.Def is { } d ? (d.BoundsMax.X - d.BoundsMin.X) * (d.BoundsMax.Y - d.BoundsMin.Y) * (d.BoundsMax.Z - d.BoundsMin.Z) * 1e6f : 0;

    private static float Axis(Vector3 v, int a) => a == 0 ? v.X : a == 1 ? v.Y : v.Z;
    private static float Volume(in ExtrusionKey k) => (float)k.W * k.H * k.L;

    private static Vector3 Unit(int a) => a == 0 ? Vector3.UnitX : a == 1 ? Vector3.UnitY : Vector3.UnitZ;

    /// <summary>The turn that lays a piece's x along axis <paramref name="wa"/>, its y along <paramref name="ha"/> and its z along <paramref name="la"/> (signs free).</summary>
    private static byte? TurnFor(int wa, int ha, int la)
    {
        for (byte r = 0; r < Rot24.Count; r++)
        {
            var x = Vector3.Abs(Rot24.Apply(r, Vector3.UnitX));
            var y = Vector3.Abs(Rot24.Apply(r, Vector3.UnitY));
            var z = Vector3.Abs(Rot24.Apply(r, Vector3.UnitZ));
            if ((x - Unit(wa)).LengthSquared() < 1e-4f && (y - Unit(ha)).LengthSquared() < 1e-4f && (z - Unit(la)).LengthSquared() < 1e-4f
                && Rot24.Apply(r, Vector3.UnitY).Y >= -0.5f && Rot24.Apply(r, Vector3.UnitZ) is var zd && zd.X + zd.Y + zd.Z > 0)
                return r; // (z along the axis's plus side, y not down: one turn a box)
        }
        return null;
    }

    /// <summary>
    /// A kit's blueprint, <c>{part} kit</c>: the first piece the root, every other one welded on it freeform at its place and turn, painted
    /// and finished (null: no kit). Made once a part.
    /// </summary>
    public static ConstructBuild.Blueprint? BlueprintOf(ConstructPartDef d)
    {
        if (KitOf(d) is not { } kit)
            return null;
        lock (Gate)
        {
            if (Prints.TryGetValue(d.Name, out var have))
                return have;
            var root = kit[0];
            var sb = new StringBuilder();
            sb.Append("{ \"format\": \"call-of-slop-construct-blueprint/1\", \"name\": ").Append(JsonSerializer.Serialize(d.Name + KitSuffix)).Append(", \"steps\": [\n");
            for (var i = 0; i < kit.Length; i++)
            {
                var p = kit[i];
                sb.Append("  { \"part\": ").Append(JsonSerializer.Serialize(p.PartName));
                if (ConstructPaint.IsSet(p.Paint))
                    sb.Append(", \"paint\": \"").Append(ConstructPaint.ToHex(p.Paint)).Append('"');
                if (p.Finish.Length > 0)
                    sb.Append(", \"finish\": ").Append(JsonSerializer.Serialize(p.Finish));
                if (i > 0)
                {
                    // (in the root's frame: the root is unturned, else every place turns back by it)
                    var inv = Rot24.Inverse(root.Rot);
                    var at = Rot24.Apply(inv, p.Pos - root.Pos);
                    var q = Rot24.Quat(Rot24.Compose(inv, p.Rot));
                    sb.Append(string.Create(CultureInfo.InvariantCulture,
                        $", \"weld\": {{\"on\": 0, \"pos\": [{at.X:0.###}, {at.Y:0.###}, {at.Z:0.###}], \"rot\": [{q.X:0.####}, {q.Y:0.####}, {q.Z:0.####}, {q.W:0.####}]}}"));
                }
                sb.Append(i + 1 < kit.Length ? " },\n" : " }\n");
            }
            sb.Append("] }\n");
            ConstructBuild.Blueprint? bp = null;
            try
            {
                bp = ConstructBuild.ParseBlueprint(sb.ToString(), d.Name + KitSuffix);
            }
            catch (ConstructPartException e)
            {
                Console.WriteLine($"[bake kits] REFUSED {d.Name}{KitSuffix}: {e.Message}");
            }
            Prints[d.Name] = bp;
            return bp;
        }
    }

    /// <summary>A kit's blueprint by its name, <c>{part} kit</c> (null: not a kit's name).</summary>
    public static ConstructBuild.Blueprint? FindPrint(string name) =>
        name.EndsWith(KitSuffix, StringComparison.OrdinalIgnoreCase) && Names.Length > 0 && ConstructParts.Find(name[..^KitSuffix.Length]) is { } d ? BlueprintOf(d) : null;

    /// <summary>
    /// The part whose kit a design's hash is, at the turn it was baked (null: none). Every kit at every turn, worked out once. Two parts can
    /// share a kit (the keel's bands where the hull runs straight): <paramref name="want"/>, the oven's order, picks between them.
    /// </summary>
    /// <summary>Every kit resolved and its hashes made now (a match's start), not on the tick of the first bake or order.</summary>
    public static void Warm() => Target("");

    public static Hit? Target(string hash, ushort want = 0, OrderList? queue = null)
    {
        lock (Gate)
        {
            if (_targets == null)
            {
                _targets = new Dictionary<string, List<Hit>>(StringComparer.Ordinal);
                foreach (var n in Names)
                {
                    if (ConstructParts.Find(n) is not { } d || KitOf(d) is not { } kit)
                    {
                        Console.WriteLine($"[bake kits] {n}: no such part, or no kit: never baked");
                        continue;
                    }
                    for (byte r = 0; r < Rot24.Count; r++)
                    {
                        var subs = new (ConstructPartDef Def, Vector3 Pos, byte Rot, uint Paint, string Finish)[kit.Length];
                        for (var i = 0; i < kit.Length; i++)
                            subs[i] = (kit[i].Def!, Rot24.Apply(r, kit[i].Pos), Rot24.Compose(r, kit[i].Rot), kit[i].Paint, kit[i].Finish);
                        var text = BakedParts.DesignText(subs, out var origin);
                        // The part's origin is the kit's frame's origin: turned by r, then measured from the design's origin.
                        var hash2 = BakedParts.HashOf(text);
                        if (!_targets.TryGetValue(hash2, out var hits))
                            _targets[hash2] = hits = [];
                        if (!hits.Exists(h => h.Part == d))
                            hits.Add(new Hit(d, r, -origin)); // (a kit alike at two turns: the first turn)
                    }
                }
            }
            if (_targets.TryGetValue(hash, out var list))
            {
                foreach (var h in list)
                    if (h.Part.Id == want)
                        return h;
                return list[0];
            }
            // A rule kit (every placed part with an Assembler row: too many to hash at a match's start): the oven's order's part, then the
            // rest of its queue, each part's 24 turns hashed once.
            if (ConstructParts.Get(want) is { } wd && RuleHit(wd, hash) is { } hw)
                return hw;
            if (queue is { Bill: false })
                for (var k = 0; k < queue.Count; k++)
                    if (queue.Items[k] is { IsPiece: false, Name: null } o && o.Part != want && ConstructParts.Get(o.Part) is { } qd && RuleHit(qd, hash) is { } hq)
                        return hq;
            return null;
        }
    }

    private static readonly Dictionary<ConstructPartDef, Dictionary<string, Hit>> RuleHashes = new(ReferenceEqualityComparer.Instance);

    /// <summary>A rule kit's part at the turn whose design hash is <paramref name="hash"/> (null: not its kit). Under <see cref="Gate"/>.</summary>
    private static Hit? RuleHit(ConstructPartDef d, string hash)
    {
        if (IsNamed(d.Name) || !Has(d) || KitOf(d, true) is not { } kit)
            return null;
        if (!RuleHashes.TryGetValue(d, out var turns))
        {
            RuleHashes[d] = turns = new Dictionary<string, Hit>(StringComparer.Ordinal);
            for (byte r = 0; r < Rot24.Count; r++)
            {
                var subs = new (ConstructPartDef Def, Vector3 Pos, byte Rot, uint Paint, string Finish)[kit.Length];
                for (var i = 0; i < kit.Length; i++)
                    subs[i] = (kit[i].Def!, Rot24.Apply(r, kit[i].Pos), Rot24.Compose(r, kit[i].Rot), kit[i].Paint, kit[i].Finish);
                var h = BakedParts.HashOf(BakedParts.DesignText(subs, out var origin));
                turns.TryAdd(h, new Hit(d, r, -origin)); // (a kit alike at two turns: the first turn)
            }
        }
        return turns.TryGetValue(hash, out var hit) ? hit : null;
    }
}

/// <summary>
/// <c>--bake-kit [NAME;NAME...|all]</c>, headless: each kit's blueprint built through the player's rules and baked (it must come out as its
/// part, where the kit stood), then the kit laid at four other turns and baked (the part at that turn). Docs: production.md "Bake kits".
/// </summary>
public static class BakeKitCheck
{
    public static int Cli(string[] args)
    {
        var at = Array.FindIndex(args, a => a.Equals("--bake-kit", StringComparison.OrdinalIgnoreCase));
        var list = at >= 0 && at + 1 < args.Length && !args[at + 1].StartsWith("--", StringComparison.Ordinal) ? args[at + 1] : "all";
        // ("rule:N": every Nth part the rule bakes, a sample: a check that loops over every part samples them)
        var names = list.Equals("all", StringComparison.OrdinalIgnoreCase) ? BakeKits.Names
            : list.StartsWith("rule:", StringComparison.OrdinalIgnoreCase) && int.TryParse(list.AsSpan(5), out var every) && every > 0
                ? [.. ConstructParts.All.Where(p => BakeKits.ByRule(p) && BakeKits.Has(p)).Where((_, i) => i % every == 0).Select(p => p.Name)]
                : list.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var fails = 0;
        void Fail(string why)
        {
            fails++;
            Console.WriteLine("FAIL " + why);
        }
        foreach (var n in names)
        {
            if (ConstructParts.Find(n) is not { } d || BakeKits.KitOf(d) is not { } kit || BakeKits.BlueprintOf(d) is not { } bp)
            {
                Fail($"{n}: no part, or no kit (the log says why)");
                continue;
            }
            Console.WriteLine($"{d.Name}: {kit.Length} pieces");
            foreach (var p in kit)
                Console.WriteLine(FormattableString.Invariant($"  {p.PartName} at {p.Pos.X:0.##},{p.Pos.Y:0.##},{p.Pos.Z:0.##} turn {p.Rot} {ConstructPaint.ToHex(p.Paint)} {p.Finish}"));
            // Through the blueprint, as a station builds it.
            var world = new ConstructWorld();
            if (ConstructBuild.Build(world, bp, new Vector3(0, 20, 0), Quaternion.Identity, out var why) is not { } c)
            {
                Fail($"{d.Name}: the kit's blueprint didn't build: {why}");
                continue;
            }
            Expect(world, c, d, Rot24.Inverse(kit[0].Rot), Rot24.Apply(Rot24.Inverse(kit[0].Rot), -kit[0].Pos), "built from its blueprint"); // (the body's frame is its first piece's)
            // Laid at other turns (an oven bakes a kit whichever way it lies).
            foreach (byte t in new byte[] { 1, 5, 10, 17, 23 })
            {
                var w2 = new ConstructWorld();
                var c2 = w2.Create(new Vector3(0, 20, 0), Quaternion.Identity)!;
                foreach (var p in kit)
                {
                    var i = w2.AddPart(c2, p.Def!, Rot24.Apply(t, p.Pos), Rot24.Compose(t, p.Rot));
                    c2.Parts[i].Paint = p.Paint;
                    c2.Parts[i].Finish = ConstructFinish.Of(p.Finish);
                }
                c2.Rebuild();
                Expect(w2, c2, d, t, Vector3.Zero, $"laid at turn {t}");
            }
        }
        Console.WriteLine(fails == 0 ? "bake kit check: passed" : $"bake kit check: {fails} failure(s)");
        return fails;

        void Expect(ConstructWorld world, Construct c, ConstructPartDef d, byte turn, Vector3 origin, string how)
        {
            // A box kit's solid bounds are its part's; a listed kit of formed skins isn't the part box for box (its plates are drawn shapes,
            // its solids the voids behind them), so the part's origin is held to the kit frame's origin instead (both in the body's frame).
            // (a rule kit too: its pieces are the boxes rounded to whole sections and 5 cm lengths, with components and sheets on top, so
            // its bounds aren't the part's; the part's origin is held to the kit frame's)
            var listed = c.DefOf(0).IsNamedRuntime || !BakeKits.Names.Contains(d.Name, StringComparer.OrdinalIgnoreCase);
            var (lo, hi) = Solid(c);
            if (BakedParts.Bake(world, c, (ushort)d.Id) is not { } b)
            {
                Fail($"{d.Name} {how}: bake refused: {BakedParts.LastRefusal}");
                return;
            }
            var i = Array.FindIndex(b.Parts, 0, b.PartCount, p => p.Def == d.Id);
            if (i < 0)
            {
                Fail($"{d.Name} {how}: baked into {b.DefOf(0).Name}, not the part");
                if (BakedParts.DesignOf(b.DefOf(0)) is { } got)
                    Console.Write(got.Text);
                return;
            }
            var (lo2, hi2) = Solid(b);
            var off = listed ? Vector3.Distance(b.Parts[i].Pos, origin) : MathF.Max(Vector3.Distance(lo, lo2), Vector3.Distance(hi, hi2));
            Console.WriteLine(FormattableString.Invariant($"  {how}: {d.Name} at turn {b.Parts[i].Rot}, its {(listed ? "origin" : "solid bounds")} {off * 100:0.#} cm from the kit's"));
            if (b.Parts[i].Rot != turn)
                Fail($"{d.Name} {how}: turn {b.Parts[i].Rot}, not {turn}");
            if (off > 0.06f)
                Fail($"{d.Name} {how}: the part sits {off:0.###} m from where its kit lay");
        }
    }

    /// <summary>A body's solid boxes' bounds in its own frame.</summary>
    private static (Vector3, Vector3) Solid(Construct c)
    {
        Vector3 lo = new(float.MaxValue), hi = new(float.MinValue);
        for (var i = 0; i < c.PartCount; i++)
        {
            var d = c.DefOf(i);
            c.RestPose(c.Parts[i].Seg, out var q, out var t);
            var pos = t + Vector3.Transform(c.Parts[i].Pos, q);
            var r = Rot24.Nearest(q * Rot24.Quat(c.Parts[i].Rot), out _);
            foreach (var bx in d.Boxes.AsSpan(0, d.SolidBoxes))
            {
                if (bx.Hidden)
                    continue;
                var centre = pos + Rot24.Apply(r, bx.Center);
                var half = Rot24.ApplyHalf(r, bx.Half);
                (lo, hi) = (Vector3.Min(lo, centre - half), Vector3.Max(hi, centre + half));
            }
        }
        return (lo, hi);
    }
}
