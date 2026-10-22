using System.Globalization;
using System.Numerics;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>
/// The "flag" block of a prop file: which parts are the flag's cloth (the swinging segments; each segment's "follow"
/// parts are its stripes in the prop's own look), and which design it flies by default.
/// </summary>
public sealed class PropFlagCloth
{
    /// <summary>The cloth's segments (0-based part numbers), in file order. Each one keeps its motion when a design replaces the cloth.</summary>
    public int[] Segments = [];

    /// <summary>"random" (a design from the map's pool, picked by position), "none" (the prop's own cloth) or a design id.</summary>
    public string Default = FlagPool.Random;
}

/// <summary>How a flag design piece is given.</summary>
public enum FlagShapeKind : byte
{
    /// <summary>An axis-aligned rectangle [u0, v0, u1, v1].</summary>
    Rect,

    /// <summary>A straight band from (u0, v0) to (u1, v1), <see cref="FlagShape.Width"/> of the flag's height wide (a saltire).</summary>
    Line,
}

/// <summary>One coloured piece of a flag design, in flag space: u from the hoist (0, at the pole) to the fly (1), v from the top (0) to the bottom (1).</summary>
public readonly record struct FlagShape(FlagShapeKind Kind, float U0, float V0, float U1, float V1, float Width, int Layer, Vector3 Color);

/// <summary>One flag from Props/Flags/flags.json: a field colour and coloured rectangles and bands on top, in drawing order.</summary>
public sealed class FlagDesign
{
    public required string Id;
    public string Name = "";

    /// <summary>The "aspect": [height, width] as the file gives it (US 10:19), and the ratio height / width.</summary>
    public float AspectHeight = 2, AspectWidth = 3;

    public float Aspect => AspectHeight / AspectWidth;

    /// <summary>The background colour (the cloth segments themselves are this colour).</summary>
    public Vector3 Field;

    /// <summary>Every piece after the field, in drawing order (repeats expanded).</summary>
    public readonly List<FlagShape> Shapes = new();

    /// <summary>How many layers the pieces stack in (1 = nothing overlaps; each overlapping layer is drawn a little thicker).</summary>
    public int Layers;
}

/// <summary>
/// The flag pool: national flag designs (Props/Flags/flags.json) that decorative flag props fly instead of their own
/// cloth (see Props/README.md "Flags"). A prop with a "flag" block gets one variant per design, built here from its
/// cloth segments: the segments keep their swing (so the wind still moves the whole flag) and the design's stripes,
/// crosses and stars are thin slabs that move with them. Each variant is its own prop type in the mesh library, so
/// flags stay instanced (one draw per design and chunk) with no new shader work.
///
/// Which design a placed flag flies is worked out when the map loads (<see cref="Map.ResolveFlags"/>): a placement's
/// own "flag", else the prop's default; "random" picks from the map's pool by the prop's position, so every PC (server
/// and clients) gets the same flags, and a map's flags never change between matches or reloads.
/// </summary>
public static class FlagPool
{
    public const string Format = "call-of-slop-flags/1";

    /// <summary>The folder in Props the flag file lives in (skipped by the prop loader).</summary>
    public const string FolderName = "Flags";

    public const string FileName = "flags.json";

    /// <summary>The "flag" value that picks from the map's pool by position.</summary>
    public const string Random = "random";

    /// <summary>The "flag" value that keeps the prop's own cloth.</summary>
    public const string None = "none";

    /// <summary>The most pieces a design may have (after repeats), to keep a flag a few hundred vertices.</summary>
    public const int MaxPieces = 80;

    public const int MaxLayers = 6;

    private static readonly object Lock = new();
    private static FlagDesign[]? _designs;
    private static readonly Dictionary<(PropDef, string), PropDef?> Variants = new();
    private static readonly Dictionary<PropDef[], PropDef[]> WithCache = new(ReferenceEqualityComparer.Instance);
    private static readonly HashSet<string> Warned = new();

    /// <summary>Problems found reading the flag file (written to the log; bad designs are skipped).</summary>
    public static readonly List<string> LoadErrors = new();

    /// <summary>Where the flag file is read from (Props/Flags/flags.json next to the game).</summary>
    public static string FilePath => Path.Combine(Props.Folder, FolderName, FileName);

    /// <summary>Every design, in file order. Loaded on first use.</summary>
    public static FlagDesign[] Designs
    {
        get
        {
            var d = _designs;
            if (d != null)
                return d;
            lock (Lock)
                return _designs ??= LoadFile(FilePath, LoadErrors);
        }
    }

    /// <summary>The design ids, in file order (the default pool of every map).</summary>
    public static string[] Ids => Designs.Select(d => d.Id).ToArray();

    public static FlagDesign? Find(string id)
    {
        foreach (var d in Designs)
            if (d.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
                return d;
        return null;
    }

    /// <summary>Re-reads the flag file and drops every built variant (called by <see cref="Props.Reload"/>).</summary>
    public static void Reload()
    {
        lock (Lock)
        {
            LoadErrors.Clear();
            Variants.Clear();
            WithCache.Clear();
            Warned.Clear();
            _designs = LoadFile(FilePath, LoadErrors);
        }
    }

    /// <summary>Is <paramref name="value"/> a valid "flag" for a placement or a prop default ("random", "none" or a design id)?</summary>
    public static bool IsChoice(string value) =>
        value.Equals(Random, StringComparison.OrdinalIgnoreCase) || value.Equals(None, StringComparison.OrdinalIgnoreCase) || Find(value) != null;

    /// <summary>A hint for an unknown flag id.</summary>
    public static string Known => $"\"{Random}\", \"{None}\" or one of {string.Join(", ", Designs.Select(d => $"\"{d.Id}\""))}";

    /// <summary>
    /// Which of <paramref name="count"/> pool entries a flag at <paramref name="pos"/> flies: a hash of its position
    /// (rounded to 1/8 m, like the animation seed), so it's the same on every PC and doesn't depend on other props. The
    /// hash uses the distance from the map's centre lines (|x|, |z|), so flags mirrored across a symmetric map fly the
    /// same design and both sides look alike.
    /// </summary>
    public static int Pick(Vector3 pos, int count)
    {
        if (count <= 1)
            return 0;
        var x = (int)MathF.Floor(MathF.Abs(pos.X) * 8f + 0.5f);
        var z = (int)MathF.Floor(MathF.Abs(pos.Z) * 8f + 0.5f);
        var h = Foliage.Hash(Foliage.Hash((uint)x * 73856093u ^ 0x5f1a6u) ^ (uint)z * 19349663u);
        return (int)((h >> 8) % (uint)count);
    }

    /// <summary>
    /// <paramref name="library"/> plus every flag variant of its flag props (one per design), for the renderer's mesh
    /// library. The same array back for the same library (cached until <see cref="Reload"/>), and the library itself
    /// when it has no flag props.
    /// </summary>
    public static PropDef[] WithVariants(PropDef[] library)
    {
        lock (Lock)
        {
            if (WithCache.TryGetValue(library, out var hit))
                return hit;
            var extra = new List<PropDef>();
            foreach (var def in library)
                if (def.FlagCloth != null)
                    foreach (var design in Designs)
                        if (VariantLocked(def, design.Id) is { } v)
                            extra.Add(v);
            var result = extra.Count == 0 ? library : library.Concat(extra).ToArray();
            WithCache[library] = result;
            return result;
        }
    }

    /// <summary>The prop <paramref name="def"/> flying design <paramref name="id"/> (cached), or null if it has no cloth or there is no such design.</summary>
    public static PropDef? Variant(PropDef def, string id)
    {
        lock (Lock)
            return VariantLocked(def, id);
    }

    private static PropDef? VariantLocked(PropDef def, string id)
    {
        if (def.FlagBase is { } b)
            def = b;
        if (def.FlagCloth == null)
            return null;
        var key = (def, id.ToLowerInvariant());
        if (Variants.TryGetValue(key, out var hit))
            return hit;
        PropDef? v = null;
        if ((_designs ??= LoadFile(FilePath, LoadErrors)).FirstOrDefault(d => d.Id == key.Item2) is { } design)
        {
            try
            {
                v = BuildVariant(def, design);
            }
            catch (Exception ex) when (ex is PropFileException or InvalidOperationException or ArgumentException)
            {
                Console.WriteLine($"[flags] couldn't put the {design.Id} flag on '{def.Name}': {ex.Message}");
            }
        }
        Variants[key] = v;
        return v;
    }

    /// <summary>Logs a warning once per text (unknown default ids and the like).</summary>
    internal static void WarnOnce(string text)
    {
        lock (Lock)
            if (Warned.Add(text))
                Console.WriteLine("[flags] " + text);
    }

    // ---- Building a variant ------------------------------------------------------------------------------

    /// <summary>
    /// The cloth's frame: flag space in metres has x from the hoist along <see cref="Along"/> and y down from the top;
    /// <see cref="Normal"/> is the thin axis (the cloth's depth).
    /// </summary>
    private readonly record struct ClothFrame(Vector3 Origin, Vector3 Along, Vector3 Normal, float Width, float Height, float Thickness, bool ThinIsZ);

    /// <summary>The members of a prop's cloth: its segments and their "follow" parts; for each member, the segment it belongs to.</summary>
    public static int[] ClothOwners(PropDef def)
    {
        var owner = new int[def.Parts.Count];
        Array.Fill(owner, -1);
        if (def.FlagCloth is not { } cloth)
            return owner;
        foreach (var s in cloth.Segments)
            owner[s] = s;
        for (var i = 0; i < def.Parts.Count; i++)
            if (owner[i] < 0 && def.Parts[i].Anim is { Type: PropAnimType.Follow, Parent: var p } && p >= 0 && cloth.Segments.Contains(p))
                owner[i] = p;
        return owner;
    }

    /// <summary>
    /// Checks a prop's "flag" block once its parts are read (called by <see cref="PropFile"/>): segments exist and move,
    /// the cloth is a flat sheet standing upright, and nothing outside the cloth moves with a part of it.
    /// </summary>
    public static void Validate(PropDef def, string source)
    {
        var cloth = def.FlagCloth!;
        var where = $"{source} (\"flag\")";
        foreach (var s in cloth.Segments)
        {
            if (s < 0 || s >= def.Parts.Count)
                throw new PropFileException($"{where}: \"cloth\" names part #{s + 1} but the prop has {def.Parts.Count} parts");
            if (def.Parts[s].Anim is not { } a || a.Type == PropAnimType.Follow || a.IsLight)
                throw new PropFileException($"{where}: cloth segment part #{s + 1} needs an \"anim\" block that moves it (a swing, like a flag in the wind)");
            if (def.Parts[s].Corners != null || def.Parts[s].RotationDeg != Vector3.Zero)
                throw new PropFileException($"{where}: cloth segment part #{s + 1} must not be rotated");
        }
        if (cloth.Segments.Distinct().Count() != cloth.Segments.Length)
            throw new PropFileException($"{where}: a part is listed twice in \"cloth\"");
        var owner = ClothOwners(def);
        for (var i = 0; i < def.Parts.Count; i++)
        {
            if (def.Parts[i].Anim is not { Parent: >= 0 } a)
                continue;
            var parentIsSegment = cloth.Segments.Contains(a.Parent);
            if (owner[i] < 0 && owner[a.Parent] >= 0)
                throw new PropFileException($"{where}: part #{i + 1} moves with cloth part #{a.Parent + 1} but isn't part of the cloth (only \"follow\" parts of a segment can be)");
            if (cloth.Segments.Contains(i) && owner[a.Parent] >= 0 && !parentIsSegment)
                throw new PropFileException($"{where}: cloth segment part #{i + 1} moves with part #{a.Parent + 1}, a stripe of another segment");
        }
        var frame = Frame(def, owner);
        if (frame.Width < 0.05f || frame.Height < 0.05f || frame.Thickness > 0.2f)
            throw new PropFileException($"{where}: the cloth is {frame.Width:0.##} x {frame.Height:0.##} m and {frame.Thickness:0.###} m thick; a flag is a flat upright sheet at least 0.05 m each way and at most 0.2 m thick");
        if (!cloth.Default.Equals(Random, StringComparison.OrdinalIgnoreCase) && !cloth.Default.Equals(None, StringComparison.OrdinalIgnoreCase)
            && !IsId(cloth.Default))
            throw new PropFileException($"{where}: \"default\" is \"{Random}\", \"{None}\" or a flag id like \"uk\", got \"{cloth.Default}\"");
    }

    /// <summary>Reads a prop file's "flag" block: { "cloth": [part numbers], "default": "random" }.</summary>
    public static PropFlagCloth ReadCloth(JsonElement e, string where)
    {
        if (e.ValueKind != JsonValueKind.Object)
            throw new PropFileException($"{where}: an object like {{ \"cloth\": [4, 7, 10], \"default\": \"random\" }}");
        var cloth = new PropFlagCloth();
        var hasCloth = false;
        foreach (var p in e.EnumerateObject())
        {
            var w = $"{where} (\"{p.Name}\")";
            switch (p.Name.ToLowerInvariant())
            {
                case "cloth":
                    if (p.Value.ValueKind != JsonValueKind.Array || p.Value.GetArrayLength() is 0 or > 8)
                        throw new PropFileException($"{w}: a list of 1 to 8 part numbers (1 = the first part): the cloth's swinging segments, from the pole out");
                    var list = new List<int>();
                    foreach (var n in p.Value.EnumerateArray())
                    {
                        var v = Num(n, w);
                        if (v != MathF.Floor(v) || v < 1 || v > PropFile.MaxParts)
                            throw new PropFileException($"{w}: part numbers are whole numbers from 1, got {n}");
                        list.Add((int)v - 1);
                    }
                    cloth.Segments = list.ToArray();
                    hasCloth = true;
                    break;
                case "default":
                    cloth.Default = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString()!.Trim().ToLowerInvariant() : throw new PropFileException($"{w}: expected text");
                    break;
                default:
                    throw new PropFileException($"{w}: unknown key. \"flag\" uses: cloth, default");
            }
        }
        if (!hasCloth)
            throw new PropFileException($"{where}: needs \"cloth\": the part numbers of the cloth's swinging segments");
        return cloth;
    }

    private static ClothFrame Frame(PropDef def, int[] owner)
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        for (var i = 0; i < def.Parts.Count; i++)
        {
            if (owner[i] < 0)
                continue;
            var b = def.Parts[i].Bounds;
            min = Vector3.Min(min, b.Min);
            max = Vector3.Max(max, b.Max);
        }
        var ext = max - min;
        var thinIsZ = ext.Z <= ext.X;
        var axis = thinIsZ ? Vector3.UnitX : Vector3.UnitZ;
        var normal = thinIsZ ? Vector3.UnitZ : Vector3.UnitX;
        float uMin = Vector3.Dot(min, axis), uMax = Vector3.Dot(max, axis);
        var thickness = float.MaxValue;
        foreach (var s in def.FlagCloth!.Segments)
        {
            var e = def.Parts[s].Bounds.Half * 2;
            thickness = MathF.Min(thickness, thinIsZ ? e.Z : e.X);
        }
        // The hoist is the cloth edge nearest the first segment's pivot (the pole); a cloth hung from its middle starts at -x / -z.
        var first = def.Parts[def.FlagCloth.Segments[0]];
        var pivot = Vector3.Dot(first.Anim?.Pivot ?? first.Offset, axis);
        var hoistAtMin = pivot - uMin <= uMax - pivot + 1e-4f;
        var along = hoistAtMin ? axis : -axis;
        var depth = Vector3.Dot((min + max) / 2, normal);
        var origin = axis * (hoistAtMin ? uMin : uMax) + Vector3.UnitY * max.Y + normal * depth;
        return new ClothFrame(origin, along, normal, uMax - uMin, max.Y - min.Y, thickness, thinIsZ);
    }

    /// <summary>A point in flag space (x metres from the hoist, y metres down from the top, d metres along the cloth's normal) in the prop's frame.</summary>
    private static Vector3 At(in ClothFrame f, float x, float y, float d) => f.Origin + f.Along * x - Vector3.UnitY * y + f.Normal * d;

    /// <summary>
    /// The thickness of a cloth layer: the field (layer 0) is the segments' own thickness, each layer on top a little
    /// thicker on both faces, so overlapping pieces never fight for the same pixels.
    /// </summary>
    public static float LayerThickness(float baseThickness, int layer) => baseThickness + layer * MathF.Max(0.004f, baseThickness * 0.25f);

    /// <summary>Builds <paramref name="def"/> flying <paramref name="design"/>. Public for the self-test.</summary>
    public static PropDef BuildVariant(PropDef def, FlagDesign design)
    {
        var cloth = def.FlagCloth ?? throw new InvalidOperationException($"'{def.Name}' has no flag cloth");
        var parts = def.Parts;
        var owner = ClothOwners(def);
        var f = Frame(def, owner);

        // Each segment's region in flag space (it and its stripes), then a clean split into strips along the axis the segments are spread on.
        var segs = cloth.Segments;
        var regions = new (float X0, float Y0, float X1, float Y1)[segs.Length];
        for (var k = 0; k < segs.Length; k++)
        {
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            for (var i = 0; i < parts.Count; i++)
            {
                if (owner[i] != segs[k])
                    continue;
                var b = parts[i].Bounds;
                for (var c = 0; c < 8; c++)
                {
                    var p = new Vector3((c & 1) == 0 ? b.Min.X : b.Max.X, (c & 2) == 0 ? b.Min.Y : b.Max.Y, (c & 4) == 0 ? b.Min.Z : b.Max.Z);
                    var x = Vector3.Dot(p - f.Origin, f.Along);
                    var y = f.Origin.Y - p.Y;
                    x0 = MathF.Min(x0, x);
                    x1 = MathF.Max(x1, x);
                    y0 = MathF.Min(y0, y);
                    y1 = MathF.Max(y1, y);
                }
            }
            regions[k] = (x0, y0, x1, y1);
        }
        float Spread(Func<(float X0, float Y0, float X1, float Y1), float> centre) =>
            regions.Length < 2 ? 0 : regions.Max(centre) - regions.Min(centre);
        var alongX = Spread(r => (r.X0 + r.X1) / 2) >= Spread(r => (r.Y0 + r.Y1) / 2);
        var order = Enumerable.Range(0, segs.Length).OrderBy(k => alongX ? regions[k].X0 + regions[k].X1 : regions[k].Y0 + regions[k].Y1).ToArray();
        var strips = new (float X0, float Y0, float X1, float Y1)[segs.Length];
        for (var n = 0; n < order.Length; n++)
        {
            var k = order[n];
            var r = regions[k];
            float lo = n == 0 ? 0 : alongX ? (regions[order[n - 1]].X1 + r.X0) / 2 : (regions[order[n - 1]].Y1 + r.Y0) / 2;
            float hi = n == order.Length - 1 ? (alongX ? f.Width : f.Height) : alongX ? (r.X1 + regions[order[n + 1]].X0) / 2 : (r.Y1 + regions[order[n + 1]].Y0) / 2;
            strips[k] = alongX ? (lo, 0, hi, f.Height) : (0, lo, f.Width, hi);
        }

        // Letterbox: the design keeps its own shape, as big as fits in the cloth, at the top of the hoist (where a flag is tied on).
        float w = f.Width, h = f.Width * design.Aspect;
        if (h > f.Height)
        {
            h = f.Height;
            w = f.Height / design.Aspect;
        }

        var result = new PropDef
        {
            Name = def.Name,
            Category = def.Category,
            Description = def.Description,
            Author = def.Author,
            SourceFile = def.SourceFile,
            Collision = def.Collision,
            Colliders = def.Colliders,
            RandomYaw = def.RandomYaw,
            YawStep = def.YawStep,
            YawJitter = def.YawJitter,
            ScaleMin = def.ScaleMin,
            ScaleMax = def.ScaleMax,
            GroundSnap = def.GroundSnap,
            Weight = def.Weight,
            FadeDistance = def.FadeDistance,
            Breakable = def.Breakable,
            Index = def.Index,
            Pack = def.Pack,
            FileHash = def.FileHash,
            FlagId = design.Id,
            FlagBase = def,
        };

        // Parts outside the cloth first (same order), then the segments, then the design's pieces, each moving with its segment.
        var newIndex = new int[parts.Count];
        Array.Fill(newIndex, -1);
        var keep = new List<int>();
        for (var i = 0; i < parts.Count; i++)
            if (owner[i] < 0)
            {
                newIndex[i] = keep.Count;
                keep.Add(i);
            }
        for (var k = 0; k < segs.Length; k++)
            newIndex[segs[k]] = keep.Count + k;
        foreach (var i in keep)
            result.Parts.Add(CopyPart(parts[i], newIndex));
        for (var k = 0; k < segs.Length; k++)
        {
            var seg = CopyPart(parts[segs[k]], newIndex);
            seg.Color = design.Field;
            seg.Layer = -1;
            var s = strips[k];
            float x0 = MathF.Max(s.X0, 0), y0 = MathF.Max(s.Y0, 0), x1 = MathF.Min(s.X1, w), y1 = MathF.Min(s.Y1, h);
            if (x1 - x0 < 1e-3f || y1 - y0 < 1e-3f)
            {
                // Nothing of the design falls on this segment (a short flag on a tall banner): a speck at its top corner keeps the chain.
                x0 = MathF.Max(s.X0, 0);
                y0 = MathF.Max(s.Y0, 0);
                x1 = x0 + 0.001f;
                y1 = y0 + 0.001f;
            }
            SetSlab(seg, f, [new Vector2(x0, y0), new Vector2(x1, y0), new Vector2(x1, y1), new Vector2(x0, y1)], f.Thickness);
            result.Parts.Add(seg);
        }
        for (var k = 0; k < segs.Length; k++)
        {
            var s = strips[k];
            var clip = (X0: MathF.Max(s.X0, 0), Y0: MathF.Max(s.Y0, 0), X1: MathF.Min(s.X1, w), Y1: MathF.Min(s.Y1, h));
            if (clip.X1 - clip.X0 < 1e-3f || clip.Y1 - clip.Y0 < 1e-3f)
                continue;
            foreach (var shape in design.Shapes)
            {
                var poly = ShapePolygon(shape, w, h);
                poly = ClipRect(poly, clip.X0, clip.Y0, clip.X1, clip.Y1);
                if (poly.Count < 3)
                    continue;
                foreach (var quad in Trapezoids(poly))
                {
                    var piece = new PropPart
                    {
                        Color = shape.Color,
                        Anim = new PropPartAnim { Type = PropAnimType.Follow, Parent = keep.Count + k },
                    };
                    SetSlab(piece, f, quad, LayerThickness(f.Thickness, shape.Layer));
                    piece.Anim!.Pivot = piece.Offset;
                    result.Parts.Add(piece);
                }
            }
        }

        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var part in result.Parts)
        {
            var b = part.Bounds;
            min = Vector3.Min(min, b.Min);
            max = Vector3.Max(max, b.Max);
        }
        result.Bounds = new Box((min + max) / 2, (max - min) / 2);
        PropAnimFile.Validate(result, $"{def.SourceFile} ({design.Id} flag)");
        return result;
    }

    private static PropPart CopyPart(PropPart p, int[] newIndex)
    {
        var c = new PropPart
        {
            Size = p.Size,
            Offset = p.Offset,
            RotationDeg = p.RotationDeg,
            Color = p.Color,
            Layer = p.Layer,
            TexScale = p.TexScale,
            Corners = p.Corners,
        };
        if (p.Anim is { } a)
        {
            c.Anim = new PropPartAnim
            {
                Type = a.Type, Axis = a.Axis, Pivot = a.Pivot, Rpm = a.Rpm, AngleDeg = a.AngleDeg, Distance = a.Distance, Period = a.Period, Phase = a.Phase,
                Jitter = a.Jitter, Wind = a.Wind, Rate = a.Rate, Duty = a.Duty, OneWay = a.OneWay, RandomPhase = a.RandomPhase,
                Parent = a.Parent >= 0 ? newIndex[a.Parent] : -1, OnColor = a.OnColor, OffColor = a.OffColor, Glow = a.Glow, Steps = a.Steps, OnMask = a.OnMask,
                FreezeDistance = a.FreezeDistance,
            };
        }
        return c;
    }

    /// <summary>
    /// Makes <paramref name="part"/> a slab over the flag-space quad <paramref name="q"/> (two corners at x0, then two
    /// at x1: (x0, top), (x1, top), (x1, bottom), (x0, bottom)), <paramref name="thickness"/> deep.
    /// </summary>
    private static void SetSlab(PropPart part, in ClothFrame f, Vector2[] q, float thickness)
    {
        var corners = new Vector3[8];
        var flip = Vector3.Dot(f.Along, f.ThinIsZ ? Vector3.UnitX : Vector3.UnitZ) < 0; // the hoist is on the + side
        for (var end = 0; end < 2; end++)
            for (var top = 0; top < 2; top++)
                for (var side = 0; side < 2; side++)
                {
                    // end 0: the x0 edge; top 1: the upper corner.
                    var p2 = end == 0 ? (top == 1 ? q[0] : q[3]) : (top == 1 ? q[1] : q[2]);
                    var pos = At(f, p2.X, p2.Y, (side == 0 ? -0.5f : 0.5f) * thickness);
                    var alongBit = (end == 1) != flip ? 1 : 0;
                    var idx = f.ThinIsZ ? alongBit | (top << 1) | (side << 2) : side | (top << 1) | (alongBit << 2);
                    corners[idx] = pos;
                }
        part.Corners = corners;
        var b = part.Bounds;
        part.Offset = b.Center;
        part.Size = b.Half * 2;
    }

    /// <summary>A design piece as a convex polygon in flag space (metres), for a flag <paramref name="w"/> x <paramref name="h"/>.</summary>
    private static List<Vector2> ShapePolygon(FlagShape s, float w, float h)
    {
        if (s.Kind == FlagShapeKind.Rect)
            return [new(s.U0 * w, s.V0 * h), new(s.U1 * w, s.V0 * h), new(s.U1 * w, s.V1 * h), new(s.U0 * w, s.V1 * h)];
        var a = new Vector2(s.U0 * w, s.V0 * h);
        var b = new Vector2(s.U1 * w, s.V1 * h);
        var d = Vector2.Normalize(b - a);
        var n = new Vector2(-d.Y, d.X) * (s.Width * h / 2);
        // Run the band past both ends; the clip to the flag cuts it square with the flag's edges.
        var ext = d * (s.Width * h * 2);
        return [a - ext + n, b + ext + n, b + ext - n, a - ext - n];
    }

    /// <summary>Sutherland-Hodgman: a convex polygon cut to a rectangle.</summary>
    private static List<Vector2> ClipRect(List<Vector2> poly, float x0, float y0, float x1, float y1)
    {
        poly = ClipEdge(poly, p => p.X - x0);
        poly = ClipEdge(poly, p => x1 - p.X);
        poly = ClipEdge(poly, p => p.Y - y0);
        return ClipEdge(poly, p => y1 - p.Y);
    }

    private static List<Vector2> ClipEdge(List<Vector2> poly, Func<Vector2, float> inside)
    {
        var result = new List<Vector2>(poly.Count + 2);
        for (var i = 0; i < poly.Count; i++)
        {
            var a = poly[i];
            var b = poly[(i + 1) % poly.Count];
            float da = inside(a), db = inside(b);
            if (da >= 0)
                result.Add(a);
            if ((da >= 0) != (db >= 0))
                result.Add(a + (b - a) * (da / (da - db)));
        }
        return result;
    }

    /// <summary>
    /// A convex polygon cut into vertical trapezoids (each [(x0, top), (x1, top), (x1, bottom), (x0, bottom)]), so every
    /// piece is a slab with straight edges: one for a rectangle, a few for a diagonal band cut by the flag's corners.
    /// </summary>
    private static IEnumerable<Vector2[]> Trapezoids(List<Vector2> poly)
    {
        var xs = poly.Select(p => p.X).Distinct().OrderBy(x => x).ToList();
        var merged = new List<float>();
        foreach (var x in xs)
            if (merged.Count == 0 || x - merged[^1] > 1e-4f)
                merged.Add(x);
        for (var i = 0; i + 1 < merged.Count; i++)
        {
            float xa = merged[i], xb = merged[i + 1];
            if (xb - xa < 1e-3f)
                continue;
            var mid = (xa + xb) / 2;
            var spans = new List<(float A, float B)>(2);
            for (var e = 0; e < poly.Count; e++)
            {
                var p = poly[e];
                var q = poly[(e + 1) % poly.Count];
                if (MathF.Abs(q.X - p.X) < 1e-6f || mid < MathF.Min(p.X, q.X) || mid > MathF.Max(p.X, q.X))
                    continue;
                spans.Add((p.Y + (q.Y - p.Y) * (xa - p.X) / (q.X - p.X), p.Y + (q.Y - p.Y) * (xb - p.X) / (q.X - p.X)));
            }
            if (spans.Count < 2)
                continue;
            var topEdge = spans.MinBy(t => t.A + t.B);
            var botEdge = spans.MaxBy(t => t.A + t.B);
            float topA = topEdge.A, topB = topEdge.B, botA = botEdge.A, botB = botEdge.B;
            if (botA - topA < 1e-4f && botB - topB < 1e-4f)
                continue;
            yield return [new(xa, topA), new(xb, topB), new(xb, botB), new(xa, botA)];
        }
    }

    // ---- The flag file -------------------------------------------------------------------------------------

    private static bool IsId(string s) => s.Length is > 0 and <= 24 && s.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');

    /// <summary>Reads a flag file (missing file: no designs, and every flag prop keeps its own cloth). Bad designs are skipped and described in <paramref name="errors"/>.</summary>
    public static FlagDesign[] LoadFile(string path, List<string> errors)
    {
        if (!File.Exists(path))
        {
            errors.Add($"{path}: not found (flag props keep their own cloth)");
            Console.WriteLine($"[flags] {path} not found: flag props keep their own cloth");
            return [];
        }
        try
        {
            var list = Parse(File.ReadAllText(path), $"Props/{FolderName}/{FileName}", errors);
            Console.WriteLine($"[flags] {list.Length} flag design(s) from {path}: {string.Join(", ", list.Select(d => d.Id))}"
                              + (errors.Count > 0 ? $", {errors.Count} problem(s)" : ""));
            return list;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            errors.Add($"{path}: {ex.Message}");
            return [];
        }
    }

    /// <summary>Parses a flag file's text. Public for the self-test.</summary>
    public static FlagDesign[] Parse(string json, string source, List<string> errors)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        }
        catch (JsonException ex)
        {
            errors.Add($"{source}: not valid JSON ({ex.Message})");
            return [];
        }
        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("flags", out var flags) || flags.ValueKind != JsonValueKind.Array)
            {
                errors.Add($"{source}: a flag file is {{ \"format\": \"{Format}\", \"flags\": [ ... ] }}");
                return [];
            }
            if (!root.TryGetProperty("format", out var fmt) || fmt.ValueKind != JsonValueKind.String || fmt.GetString() != Format)
            {
                errors.Add($"{source}: expected \"format\": \"{Format}\"");
                return [];
            }
            var list = new List<FlagDesign>();
            var index = 0;
            foreach (var e in flags.EnumerateArray())
            {
                index++;
                try
                {
                    var d = ReadDesign(e, $"{source}, flag #{index}");
                    if (list.Any(x => x.Id == d.Id))
                        throw new PropFileException($"{source}, flag #{index}: another flag is already \"{d.Id}\"");
                    list.Add(d);
                }
                catch (PropFileException ex)
                {
                    errors.Add(ex.Message);
                    Console.WriteLine($"[flags] skipped: {ex.Message}");
                }
            }
            return list.ToArray();
        }
    }

    private static FlagDesign ReadDesign(JsonElement e, string where)
    {
        if (e.ValueKind != JsonValueKind.Object)
            throw new PropFileException($"{where}: a flag is {{ \"id\": \"fr\", \"aspect\": [2, 3], \"field\": \"#ffffff\", \"shapes\": [...] }}");
        string? id = null;
        var design = new FlagDesign { Id = "" };
        JsonElement? shapes = null;
        bool hasAspect = false, hasField = false;
        foreach (var p in e.EnumerateObject())
        {
            var w = $"{where} (\"{p.Name}\")";
            switch (p.Name.ToLowerInvariant())
            {
                case "id":
                    id = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString()! : throw new PropFileException($"{w}: expected text");
                    if (!IsId(id) || id is Random or None)
                        throw new PropFileException($"{w}: an id is 1-24 lowercase letters, digits or '-' (not \"random\" or \"none\"), got \"{id}\"");
                    where = $"{where} (\"{id}\")";
                    break;
                case "name":
                    design.Name = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString()! : throw new PropFileException($"{w}: expected text");
                    if (design.Name.Any(c => c > 126 || c < 32))
                        throw new PropFileException($"{w}: names are plain ASCII");
                    break;
                case "aspect":
                    if (p.Value.ValueKind != JsonValueKind.Array || p.Value.GetArrayLength() != 2)
                        throw new PropFileException($"{w}: [height, width], e.g. [10, 19] for the US flag");
                    design.AspectHeight = Num(p.Value[0], w);
                    design.AspectWidth = Num(p.Value[1], w);
                    if (design.AspectHeight <= 0 || design.AspectWidth <= 0 || design.Aspect is < 0.3f or > 1.5f)
                        throw new PropFileException($"{w}: height / width is 0.3 to 1.5 (flags are wider than tall, or about square)");
                    hasAspect = true;
                    break;
                case "field":
                    design.Field = PropFile.Color(p.Value, w);
                    hasField = true;
                    break;
                case "shapes":
                    shapes = p.Value;
                    break;
                default:
                    throw new PropFileException($"{w}: unknown key. A flag uses: id, name, aspect, field, shapes");
            }
        }
        if (id == null)
            throw new PropFileException($"{where}: needs an \"id\"");
        design.Id = id;
        if (!hasAspect || !hasField)
            throw new PropFileException($"{where}: needs \"aspect\" [height, width] and \"field\" (the background colour)");
        if (string.IsNullOrWhiteSpace(design.Name))
            design.Name = id;
        if (shapes is { } list)
        {
            if (list.ValueKind != JsonValueKind.Array)
                throw new PropFileException($"{where} (\"shapes\"): a list of {{ \"color\": ..., \"rects\": [[u0, v0, u1, v1]] }} or {{ \"color\": ..., \"lines\": [...], \"width\": 0.2 }}");
            var n = 0;
            foreach (var s in list.EnumerateArray())
                ReadShape(s, $"{where}, shape #{++n}", design);
            design.Layers = design.Shapes.Count == 0 ? 0 : design.Shapes.Max(x => x.Layer);
            if (design.Shapes.Count > MaxPieces)
                throw new PropFileException($"{where}: {design.Shapes.Count} pieces is more than {MaxPieces} (keep flags cheap: simplify the pattern)");
            if (design.Layers > MaxLayers)
                throw new PropFileException($"{where}: pieces stack {design.Layers} deep; at most {MaxLayers}");
        }
        return design;
    }

    private static void ReadShape(JsonElement e, string where, FlagDesign design)
    {
        if (e.ValueKind != JsonValueKind.Object)
            throw new PropFileException($"{where}: a shape is {{ \"color\": ..., \"rects\": [[u0, v0, u1, v1]] }}");
        Vector3? color = null;
        JsonElement? rects = null, lines = null;
        float width = -1;
        foreach (var p in e.EnumerateObject())
        {
            var w = $"{where} (\"{p.Name}\")";
            switch (p.Name.ToLowerInvariant())
            {
                case "color":
                case "colour":
                    color = PropFile.Color(p.Value, w);
                    break;
                case "rects":
                    rects = p.Value;
                    break;
                case "lines":
                    lines = p.Value;
                    break;
                case "width":
                    width = Num(p.Value, w);
                    if (width is <= 0 or > 1)
                        throw new PropFileException($"{w}: a fraction of the flag's height, above 0 and at most 1");
                    break;
                default:
                    throw new PropFileException($"{w}: unknown key. A shape uses: color, rects, or lines and width");
            }
        }
        if (color == null)
            throw new PropFileException($"{where}: needs a \"color\"");
        if ((rects == null) == (lines == null))
            throw new PropFileException($"{where}: give \"rects\" or \"lines\" (one of them)");
        if (lines != null && width < 0)
            throw new PropFileException($"{where}: \"lines\" need a \"width\" (a fraction of the flag's height)");
        if (rects != null && width >= 0)
            throw new PropFileException($"{where}: \"width\" is only for \"lines\"");
        var pieces = new List<FlagShape>();
        var src = rects ?? lines!.Value;
        if (src.ValueKind != JsonValueKind.Array || src.GetArrayLength() == 0)
            throw new PropFileException($"{where}: a list of [u0, v0, u1, v1] (0 to 1: u from the pole, v from the top)");
        var k = 0;
        foreach (var r in src.EnumerateArray())
        {
            var w = $"{where}, item #{++k}";
            var len = r.ValueKind == JsonValueKind.Array ? r.GetArrayLength() : 0;
            if (len != 4 && !(rects != null && len == 8))
                throw new PropFileException($"{w}: [u0, v0, u1, v1]{(rects != null ? ", or [u0, v0, u1, v1, countU, countV, stepU, stepV] for a repeated grid" : "")}");
            float u0 = Num(r[0], w), v0 = Num(r[1], w), u1 = Num(r[2], w), v1 = Num(r[3], w);
            int cu = 1, cv = 1;
            float su = 0, sv = 0;
            if (len == 8)
            {
                cu = (int)Num(r[4], w);
                cv = (int)Num(r[5], w);
                su = Num(r[6], w);
                sv = Num(r[7], w);
                if (cu is < 1 or > 20 || cv is < 1 or > 20 || cu != Num(r[4], w) || cv != Num(r[5], w))
                    throw new PropFileException($"{w}: the counts are whole numbers 1 to 20");
            }
            for (var j = 0; j < cv; j++)
                for (var i = 0; i < cu; i++)
                {
                    float a0 = u0 + i * su, b0 = v0 + j * sv, a1 = u1 + i * su, b1 = v1 + j * sv;
                    const float Tol = 1e-3f;
                    var inside = a0 >= -Tol && b0 >= -Tol && a1 >= -Tol && b1 >= -Tol && a0 <= 1 + Tol && b0 <= 1 + Tol && a1 <= 1 + Tol && b1 <= 1 + Tol;
                    var shaped = rects != null ? a1 - a0 >= 1e-3f && b1 - b0 >= 1e-3f : MathF.Abs(a1 - a0) + MathF.Abs(b1 - b0) >= 1e-3f;
                    if (!inside || !shaped)
                        throw new PropFileException($"{w}: [{F(a0)}, {F(b0)}, {F(a1)}, {F(b1)}] isn't inside the flag (0 to 1, u0 < u1 and v0 < v1 for a rect)");
                    a0 = Math.Clamp(a0, 0, 1);
                    b0 = Math.Clamp(b0, 0, 1);
                    a1 = Math.Clamp(a1, 0, 1);
                    b1 = Math.Clamp(b1, 0, 1);
                    pieces.Add(new FlagShape(rects != null ? FlagShapeKind.Rect : FlagShapeKind.Line, a0, b0, a1, b1, MathF.Max(width, 0), 0, color.Value));
                }
        }
        // The layer: one above every earlier piece it overlaps (shapes side by side share a layer; a line counts as its whole box).
        var layer = 1;
        foreach (var b in design.Shapes)
            if (b.Layer >= layer && pieces.Any(a => Overlap(a, b)))
                layer = b.Layer + 1;
        foreach (var p in pieces)
            design.Shapes.Add(p with { Layer = layer });
    }

    private static bool Overlap(FlagShape a, FlagShape b)
    {
        float a0 = MathF.Min(a.U0, a.U1), a1 = MathF.Max(a.U0, a.U1), av0 = MathF.Min(a.V0, a.V1), av1 = MathF.Max(a.V0, a.V1);
        float b0 = MathF.Min(b.U0, b.U1), b1 = MathF.Max(b.U0, b.U1), bv0 = MathF.Min(b.V0, b.V1), bv1 = MathF.Max(b.V0, b.V1);
        return a0 < b1 - 1e-4f && a1 > b0 + 1e-4f && av0 < bv1 - 1e-4f && av1 > bv0 + 1e-4f;
    }

    private static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    private static float Num(JsonElement e, string where) =>
        e.ValueKind == JsonValueKind.Number && float.IsFinite(e.GetSingle()) ? e.GetSingle() : throw new PropFileException($"{where}: expected a number, got {e}");
}
