using System.Globalization;
using System.Numerics;
using System.Text.Json;

namespace CallOfSlop;

public sealed class DecalFileException(string message) : Exception(message);

/// <summary>
/// Decal files: one JSON object per decal in the Decals folder (see Decals/README.md), format "call-of-slop-decal/1".
/// Every key is checked: an unknown key, a number out of range or a bad shape is an error with a readable message.
/// </summary>
public static class DecalFile
{
    public const string Format = "call-of-slop-decal/1";

    /// <summary>The most layers one decal may have.</summary>
    public const int MaxLayers = 48;

    /// <summary>The largest side of a decal in metres.</summary>
    public const float MaxSize = 16f;

    /// <summary>The largest PNG a decal may use (pixels per side); it is resampled to <see cref="DecalAtlas.LayerSize"/>.</summary>
    public const int MaxImageSize = 512;

    /// <summary>Material names an "impact" may list (the box surface textures, the terrain, and Default for anything else).</summary>
    public static readonly string[] ImpactMaterialNames = ["Concrete", "Brick", "Wood", "Metal", "Tiles", "Dirt", "Grid", "Terrain", "Default",
        "Asphalt", "Plaster", "Grass", "Sand", "Rock", "Snow", "Explosive", "Gore"];

    /// <summary>
    /// Where a material with no impact decal of its own borrows them from (the chain ends at Default; Explosive and Gore have none:
    /// a blast with no scorch decal leaves nothing rather than a bullet hole).
    /// </summary>
    public static string? ImpactFallback(string material) => material switch
    {
        "Asphalt" or "Plaster" or "Rock" or "Tiles" or "Grid" => "Concrete",
        "Grass" or "Sand" or "Snow" => "Terrain",
        "Terrain" => "Dirt",
        "Explosive" or "Gore" or "Default" => null, // (Gore: the gibs' blood splats, Decals/Gore; never a bullet hole)
        _ => "Default",
    };

    private const string Keys = "format, name, category, description, author, size, sizeVariation, rotation, tintVariation, blend, surfaces, opacity, fadeDistance, image, layers, impact";

    private const string ShapeKeys = "shape, at, to, radius, width, size, rotation, count, length, jag, seed, period, duty, color, alpha, softness, noise, noiseScale, erase";

    /// <summary>Builds a decal from a decal file. Throws <see cref="DecalFileException"/> with a readable message if the file is wrong.</summary>
    /// <param name="folderCategory">The category subfolder the file is in (used when the file gives no "category").</param>
    /// <param name="directory">The folder the file is in (a relative "image" is looked up there); null: images aren't allowed.</param>
    public static DecalDef Load(string json, string source, string folderCategory = "Misc", string? directory = null)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        }
        catch (JsonException ex)
        {
            throw new DecalFileException($"{source}: not valid JSON ({ex.Message})");
        }
        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new DecalFileException($"{source}: a decal file is one JSON object {{ \"format\": \"{Format}\", \"name\": ..., \"layers\": [...] }}");
            var def = new DecalDef { Category = folderCategory, SourceFile = source };
            string? name = null;
            JsonElement? layers = null;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in root.EnumerateObject())
            {
                var where = $"{source} (\"{p.Name}\")";
                if (!seen.Add(p.Name))
                    throw new DecalFileException($"{where}: given twice");
                switch (p.Name.ToLowerInvariant())
                {
                    case "format":
                        var f = Text(p.Value, where);
                        if (!f.StartsWith("call-of-slop-decal/", StringComparison.Ordinal))
                            throw new DecalFileException($"{where}: expected \"{Format}\", got \"{f}\"");
                        if (f != Format)
                            throw new DecalFileException($"{where}: this game reads \"{Format}\" files; \"{f}\" is from a different version");
                        break;
                    case "name":
                        name = Text(p.Value, where).Trim();
                        break;
                    case "category":
                        def.Category = Text(p.Value, where).Trim();
                        break;
                    case "description":
                        def.Description = Text(p.Value, where);
                        break;
                    case "author":
                        def.Author = Text(p.Value, where);
                        break;
                    case "size":
                        def.Size = p.Value.ValueKind == JsonValueKind.Number
                            ? new Vector2(Range(Num(p.Value, where), 0.02f, MaxSize, where))
                            : Vec2(p.Value, where, 0.02f, MaxSize);
                        break;
                    case "sizevariation":
                        def.SizeVariation = Range(Num(p.Value, where), 0f, 0.9f, where);
                        break;
                    case "rotation":
                        def.Rotation = p.Value.ValueKind == JsonValueKind.String && p.Value.GetString()!.Equals("random", StringComparison.OrdinalIgnoreCase)
                            ? float.NaN
                            : p.Value.ValueKind == JsonValueKind.Number ? Range(Num(p.Value, where), -360f, 360f, where)
                            : throw new DecalFileException($"{where}: \"rotation\" is \"random\" or a number of degrees");
                        break;
                    case "tintvariation":
                        def.TintVariation = Range(Num(p.Value, where), 0f, 0.5f, where);
                        break;
                    case "blend":
                        def.Blend = Text(p.Value, where).ToLowerInvariant() switch
                        {
                            "alpha" or "normal" => DecalBlend.Alpha,
                            "multiply" => DecalBlend.Multiply,
                            var other => throw new DecalFileException($"{where}: \"blend\" is \"alpha\" or \"multiply\", got \"{other}\""),
                        };
                        break;
                    case "surfaces":
                        def.Surfaces = ParseSurfaces(p.Value, where);
                        break;
                    case "opacity":
                        def.Opacity = Range(Num(p.Value, where), 0.02f, 1f, where);
                        break;
                    case "fadedistance":
                        def.FadeDistance = Range(Num(p.Value, where), 5f, 400f, where);
                        break;
                    case "image":
                        def.ImagePath = ResolveImage(Text(p.Value, where), directory, where);
                        break;
                    case "layers":
                        layers = p.Value;
                        break;
                    case "impact":
                        ReadImpact(p.Value, def, where);
                        break;
                    default:
                        throw new DecalFileException($"{where}: unknown key. Decal files use: {Keys}");
                }
            }
            if (string.IsNullOrWhiteSpace(name))
                throw new DecalFileException($"{source}: missing \"name\"");
            if (name.Length > 40)
                throw new DecalFileException($"{source}: \"name\" is at most 40 characters");
            if (!IsPlain(name) || !IsPlain(def.Category) || !IsPlain(def.Description))
                throw new DecalFileException($"{source}: name, category and description must be plain ASCII (the game's font has no curly quotes, dashes or accents)");
            def.Name = name;
            if (def.Category.Length == 0)
                def.Category = folderCategory;
            if (layers is { } ls)
            {
                if (ls.ValueKind != JsonValueKind.Array)
                    throw new DecalFileException($"{source} (\"layers\"): expected a list of shapes");
                if (ls.GetArrayLength() > MaxLayers)
                    throw new DecalFileException($"{source}: {ls.GetArrayLength()} layers is more than the limit of {MaxLayers}");
                var i = 0;
                foreach (var le in ls.EnumerateArray())
                    def.Layers.Add(ReadShape(le, $"{source}, layer #{++i}"));
            }
            if (def.Layers.Count == 0 && def.ImagePath == null)
                throw new DecalFileException($"{source}: needs \"layers\" (shapes, e.g. [ {{ \"shape\": \"circle\", \"radius\": 0.3, \"color\": \"black\" }} ]) or an \"image\"");
            if (def.IsImpact && def.Blend != DecalBlend.Alpha)
                throw new DecalFileException($"{source}: impact decals use \"blend\": \"alpha\" (they are drawn in one instanced pass)");
            if (def.IsImpact && MathF.Max(def.Size.X, def.Size.Y) > 1f)
                throw new DecalFileException($"{source}: impact decals are at most 1 m across (this one is {MathF.Max(def.Size.X, def.Size.Y):0.##} m)");
            return def;
        }
    }

    private static DecalSurfaces ParseSurfaces(JsonElement e, string where)
    {
        string[] names = e.ValueKind == JsonValueKind.String ? [e.GetString()!]
            : e.ValueKind == JsonValueKind.Array ? e.EnumerateArray().Select(x => Text(x, where)).ToArray()
            : throw new DecalFileException($"{where}: \"surfaces\" is a list of \"floor\", \"wall\", \"ceiling\" (or \"all\")");
        var s = DecalSurfaces.None;
        foreach (var n in names)
            s |= n.Trim().ToLowerInvariant() switch
            {
                "floor" or "floors" or "ground" => DecalSurfaces.Floor,
                "wall" or "walls" => DecalSurfaces.Wall,
                "ceiling" or "ceilings" => DecalSurfaces.Ceiling,
                "all" or "any" => DecalSurfaces.All,
                var other => throw new DecalFileException($"{where}: unknown surface \"{other}\" (floor, wall, ceiling or all)"),
            };
        return s == DecalSurfaces.None ? throw new DecalFileException($"{where}: \"surfaces\" lists none") : s;
    }

    private static void ReadImpact(JsonElement e, DecalDef def, string where)
    {
        if (e.ValueKind != JsonValueKind.Object)
            throw new DecalFileException($"{where}: \"impact\" is {{ \"materials\": [\"Concrete\", ...], \"weight\": 1 }}");
        foreach (var p in e.EnumerateObject())
        {
            var w = $"{where}.{p.Name}";
            switch (p.Name.ToLowerInvariant())
            {
                case "materials":
                    string[] list = p.Value.ValueKind == JsonValueKind.Array ? p.Value.EnumerateArray().Select(x => Text(x, w)).ToArray()
                        : p.Value.ValueKind == JsonValueKind.String ? [p.Value.GetString()!] : throw new DecalFileException($"{w}: expected a list of materials");
                    var mats = new List<string>();
                    foreach (var m in list)
                    {
                        var known = ImpactMaterialNames.FirstOrDefault(n => n.Equals(m.Trim(), StringComparison.OrdinalIgnoreCase))
                                    ?? throw new DecalFileException($"{w}: unknown material \"{m}\" (use {string.Join(", ", ImpactMaterialNames)})");
                        if (!mats.Contains(known))
                            mats.Add(known);
                    }
                    if (mats.Count == 0)
                        throw new DecalFileException($"{w}: lists no materials");
                    def.ImpactMaterials = mats.ToArray();
                    break;
                case "weight":
                    def.ImpactWeight = Range(Num(p.Value, w), 0.05f, 20f, w);
                    break;
                default:
                    throw new DecalFileException($"{w}: unknown key (impact takes materials, weight)");
            }
        }
        if (def.ImpactMaterials.Length == 0)
            throw new DecalFileException($"{where}: \"impact\" needs \"materials\"");
    }

    private static DecalShape ReadShape(JsonElement e, string where)
    {
        if (e.ValueKind != JsonValueKind.Object)
            throw new DecalFileException($"{where}: a layer is an object {{ \"shape\": \"circle\", ... }}");
        var s = new DecalShape();
        var hasShape = false;
        var alpha = 1f;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in e.EnumerateObject())
        {
            var w = $"{where} (\"{p.Name}\")";
            if (!seen.Add(p.Name))
                throw new DecalFileException($"{w}: given twice");
            switch (p.Name.ToLowerInvariant())
            {
                case "shape":
                    var k = Text(p.Value, w);
                    if (!Enum.TryParse<DecalShapeKind>(k, true, out var kind) || !Enum.IsDefined(kind) || int.TryParse(k, out _))
                        throw new DecalFileException($"{w}: unknown shape \"{k}\" (use {string.Join(", ", Enum.GetNames<DecalShapeKind>().Select(n => n.ToLowerInvariant()))})");
                    s.Kind = kind;
                    hasShape = true;
                    break;
                case "at": s.At = Vec2(p.Value, w, -0.5f, 1.5f); break;
                case "to": s.To = Vec2(p.Value, w, -0.5f, 1.5f); break;
                case "radius": s.Radius = Range(Num(p.Value, w), 0.002f, 1f, w); break;
                case "width": s.Width = Range(Num(p.Value, w), 0.002f, 1f, w); break;
                case "size": s.Size = p.Value.ValueKind == JsonValueKind.Number ? new Vector2(Range(Num(p.Value, w), 0.002f, 2f, w)) : Vec2(p.Value, w, 0.002f, 2f); break;
                case "rotation": s.Rotation = Range(Num(p.Value, w), -360f, 360f, w); break;
                case "count": s.Count = (int)Range(Whole(p.Value, w), 0, 64, w); break;
                case "length": s.Length = Range(Num(p.Value, w), 0.01f, 1.5f, w); break;
                case "jag": s.Jag = Range(Num(p.Value, w), 0f, 1f, w); break;
                case "seed": s.Seed = Whole(p.Value, w); break;
                case "period": s.Period = Range(Num(p.Value, w), 0.01f, 1f, w); break;
                case "duty": s.Duty = Range(Num(p.Value, w), 0.05f, 0.95f, w); break;
                case "color":
                    try
                    {
                        var c = PropFile.Color(p.Value, w);
                        s.Color = new Vector4(c, s.Color.W);
                    }
                    catch (PropFileException ex)
                    {
                        throw new DecalFileException(ex.Message);
                    }
                    break;
                case "alpha": alpha = Range(Num(p.Value, w), 0f, 1f, w); break;
                case "softness": s.Softness = Range(Num(p.Value, w), 0f, 0.5f, w); break;
                case "noise": s.Noise = Range(Num(p.Value, w), 0f, 1f, w); break;
                case "noisescale": s.NoiseScale = Range(Num(p.Value, w), 1f, 64f, w); break;
                case "erase":
                    s.Erase = p.Value.ValueKind is JsonValueKind.True or JsonValueKind.False ? p.Value.GetBoolean() : throw new DecalFileException($"{w}: expected true or false");
                    break;
                default:
                    throw new DecalFileException($"{w}: unknown key. Layers use: {ShapeKeys}");
            }
        }
        if (!hasShape)
            throw new DecalFileException($"{where}: needs \"shape\" ({string.Join(", ", Enum.GetNames<DecalShapeKind>().Select(n => n.ToLowerInvariant()))})");
        s.Color.W = alpha;
        return s;
    }

    private static string ResolveImage(string rel, string? directory, string where)
    {
        if (directory == null)
            throw new DecalFileException($"{where}: images need the decal to be loaded from a file");
        if (rel.Length == 0 || Path.IsPathRooted(rel) || rel.Contains("..", StringComparison.Ordinal) || !rel.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            throw new DecalFileException($"{where}: \"image\" is a .png file next to the decal file (e.g. \"oil.png\")");
        var path = Path.Combine(directory, rel);
        if (!File.Exists(path))
            throw new DecalFileException($"{where}: image \"{rel}\" not found next to the decal file");
        return path;
    }

    // ---- Value helpers ---------------------------------------------------------------------

    private static bool IsPlain(string s) => s.All(c => c >= 32 && c < 127);

    private static string Text(JsonElement e, string where) =>
        e.ValueKind == JsonValueKind.String ? e.GetString()! : throw new DecalFileException($"{where}: expected text, got {e}");

    private static float Num(JsonElement e, string where) =>
        e.ValueKind == JsonValueKind.Number && float.IsFinite(e.GetSingle()) ? e.GetSingle() : throw new DecalFileException($"{where}: expected a number, got {e}");

    private static int Whole(JsonElement e, string where) =>
        e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out var i) ? i : throw new DecalFileException($"{where}: expected a whole number, got {e}");

    private static float Range(float v, float lo, float hi, string where) =>
        v >= lo && v <= hi ? v : throw new DecalFileException($"{where}: {v.ToString(CultureInfo.InvariantCulture)} is outside {lo.ToString(CultureInfo.InvariantCulture)} to {hi.ToString(CultureInfo.InvariantCulture)}");

    private static Vector2 Vec2(JsonElement e, string where, float lo, float hi) =>
        e.ValueKind == JsonValueKind.Array && e.GetArrayLength() == 2
            ? new Vector2(Range(Num(e[0], where), lo, hi, where), Range(Num(e[1], where), lo, hi, where))
            : throw new DecalFileException($"{where}: expected [x, y], got {e}");
}
