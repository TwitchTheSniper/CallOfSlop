using System.Numerics;
using System.Text;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>Reading and writing a map file's optional "foliage" section (see Maps/README.md, "Foliage").</summary>
public static partial class MapFile
{
    private static readonly string[] FoliageKeys = ["seed", "paths", "regions", "items"];
    private static readonly string[] RegionKeys = ["type", "density", "rect", "circle", "polygon", "seed", "scale", "height", "color", "patchiness", "avoidSolid", "avoidPaths"];
    private static readonly string[] ItemKeys = ["type", "x", "z", "scale", "height", "color"];
    private static readonly string[] PathKeys = ["points", "width"];

    // ---- Writing ----------------------------------------------------------------------------------

    private static string FoliageToJson(FoliageSpec f)
    {
        var sb = new StringBuilder();
        sb.Append("{\n");
        sb.Append($"    \"seed\": {f.Seed}");
        if (f.Paths.Count > 0)
        {
            sb.Append(",\n    \"paths\": [\n");
            for (var i = 0; i < f.Paths.Count; i++)
            {
                var p = f.Paths[i];
                sb.Append($"      {{ \"points\": {PointList(p.Points)}, \"width\": {Num(p.Width)} }}").Append(i < f.Paths.Count - 1 ? ",\n" : "\n");
            }
            sb.Append("    ]");
        }
        if (f.Regions.Count > 0)
        {
            sb.Append(",\n    \"regions\": [\n");
            for (var i = 0; i < f.Regions.Count; i++)
                sb.Append("      ").Append(RegionToJson(f.Regions[i])).Append(i < f.Regions.Count - 1 ? ",\n" : "\n");
            sb.Append("    ]");
        }
        if (f.Items.Count > 0)
        {
            sb.Append(",\n    \"items\": [\n");
            for (var i = 0; i < f.Items.Count; i++)
            {
                var it = f.Items[i];
                var parts = new List<string> { $"\"type\": {Str(TypeName(it.Type))}", $"\"x\": {Num(it.X)}", $"\"z\": {Num(it.Z)}" };
                if (it.Size != DefaultItemSize(it.Type))
                    parts.Add($"\"{SizeKey(it.Type)}\": {Num(it.Size)}");
                if (it.Color is { } c)
                    parts.Add($"\"color\": {Col(c)}");
                sb.Append("      { ").Append(string.Join(", ", parts)).Append(" }").Append(i < f.Items.Count - 1 ? ",\n" : "\n");
            }
            sb.Append("    ]");
        }
        sb.Append("\n  }");
        return sb.ToString();
    }

    private static string RegionToJson(FoliageRegion r)
    {
        var parts = new List<string> { $"\"type\": {Str(TypeName(r.Type))}", $"\"density\": {Num(r.Density)}" };
        var v = r.Area.Values;
        parts.Add(r.Area.Kind switch
        {
            FoliageAreaKind.Rect => $"\"rect\": [{Num(v[0])}, {Num(v[1])}, {Num(v[2])}, {Num(v[3])}]",
            FoliageAreaKind.Circle => $"\"circle\": [{Num(v[0])}, {Num(v[1])}, {Num(v[2])}]",
            _ => $"\"polygon\": {PointList(v)}",
        });
        parts.Add($"\"seed\": {r.Seed}");
        if ((r.SizeMin, r.SizeMax) != Foliage.DefaultSize(r.Type))
            parts.Add($"\"{SizeKey(r.Type)}\": " + (r.SizeMin == r.SizeMax ? Num(r.SizeMin) : $"[{Num(r.SizeMin)}, {Num(r.SizeMax)}]"));
        if (r.Color is { } c)
            parts.Add($"\"color\": {Col(c)}");
        if (r.Patchiness != Foliage.DefaultPatchiness(r.Type))
            parts.Add($"\"patchiness\": {Num(r.Patchiness)}");
        if (!r.AvoidSolid)
            parts.Add("\"avoidSolid\": false");
        if (!r.AvoidPaths)
            parts.Add("\"avoidPaths\": false");
        return "{ " + string.Join(", ", parts) + " }";
    }

    private static string PointList(float[] v)
    {
        var pts = new List<string>();
        for (var i = 0; i + 1 < v.Length; i += 2)
            pts.Add($"[{Num(v[i])}, {Num(v[i + 1])}]");
        return "[" + string.Join(", ", pts) + "]";
    }

    private static string TypeName(FoliageType t) => t.ToString().ToLowerInvariant();

    /// <summary>Trees are sized by "height" (metres), everything else by "scale".</summary>
    private static string SizeKey(FoliageType t) => Foliage.IsTree(t) ? "height" : "scale";

    private static float DefaultItemSize(FoliageType t) => t switch { FoliageType.Tree => 6.5f, FoliageType.Pine => 8f, _ => 1f };

    // ---- Reading ----------------------------------------------------------------------------------

    private static FoliageSpec ReadFoliage(JsonElement e, string source)
    {
        var where = $"{source}, foliage";
        if (e.ValueKind != JsonValueKind.Object)
            throw new MapFileException($"{where}: expected an object {{ \"regions\": [...], ... }}");
        CheckKeys(e, FoliageKeys, where);
        var spec = new FoliageSpec();
        if (Prop(e, "seed") is { } seed)
            spec.Seed = ReadInt(seed, $"{where} (seed)");

        if (Prop(e, "paths") is { } paths)
        {
            var i = 0;
            foreach (var p in JsonList(paths, $"{where} (paths)"))
            {
                var w = $"{where}, path #{++i}";
                CheckKeys(p, PathKeys, w);
                var pts = Prop(p, "points") is { } pe ? Points(pe, w) : throw new MapFileException($"{w}: needs \"points\"");
                if (pts.Length < 2)
                    throw new MapFileException($"{w}: needs at least one point");
                var width = Prop(p, "width") is { } we ? Num(we, w) : 3f;
                spec.Paths.Add(new FoliagePath(pts, MathF.Max(0f, width)));
            }
        }

        if (Prop(e, "regions") is { } regions)
        {
            var i = 0;
            foreach (var r in JsonList(regions, $"{where} (regions)"))
            {
                var w = $"{where}, region #{++i}";
                CheckKeys(r, RegionKeys, w);
                var type = ReadType(r, w);
                var areas = new[] { "rect", "circle", "polygon" }.Where(k => Prop(r, k) != null).ToArray();
                if (areas.Length != 1)
                    throw new MapFileException($"{w}: give exactly one area: \"rect\": [x0, z0, x1, z1], \"circle\": [x, z, radius] or \"polygon\": [[x, z], ...]");
                var areaEl = Prop(r, areas[0])!.Value;
                FoliageArea area = areas[0] switch
                {
                    "rect" => new FoliageArea(FoliageAreaKind.Rect, Floats(areaEl, 4, $"{w} (rect)")),
                    "circle" => new FoliageArea(FoliageAreaKind.Circle, Floats(areaEl, 3, $"{w} (circle)")),
                    _ => new FoliageArea(FoliageAreaKind.Polygon, Points(areaEl, $"{w} (polygon)")),
                };
                if (area.Kind == FoliageAreaKind.Polygon && area.Values.Length < 6)
                    throw new MapFileException($"{w}: a polygon needs at least 3 points");
                var density = Prop(r, "density") is { } de ? Num(de, $"{w} (density)") : throw new MapFileException($"{w}: needs \"density\" (plants per square metre)");
                if (density < 0 || density > Foliage.MaxDensity)
                    throw new MapFileException($"{w}: density {density} is outside 0..{Foliage.MaxDensity} plants per square metre");
                var (min, max) = ReadSize(r, type, Foliage.DefaultSize(type), w);
                var reg = new FoliageRegion
                {
                    Type = type,
                    Density = density,
                    Area = area,
                    Seed = Prop(r, "seed") is { } se ? ReadInt(se, $"{w} (seed)") : i,
                    SizeMin = min,
                    SizeMax = max,
                    Color = Prop(r, "color") is { } ce ? ToColour(ce, $"{w} (color)", "") : null,
                    Patchiness = Prop(r, "patchiness") is { } pe ? Math.Clamp(Num(pe, $"{w} (patchiness)"), 0f, 1f) : Foliage.DefaultPatchiness(type),
                    AvoidSolid = Prop(r, "avoidSolid") is not { } ase || ReadBool(ase, $"{w} (avoidSolid)"),
                    AvoidPaths = Prop(r, "avoidPaths") is not { } ape || ReadBool(ape, $"{w} (avoidPaths)"),
                };
                spec.Regions.Add(reg);
            }
        }

        if (Prop(e, "items") is { } items)
        {
            var i = 0;
            foreach (var it in JsonList(items, $"{where} (items)"))
            {
                var w = $"{where}, item #{++i}";
                CheckKeys(it, ItemKeys, w);
                var type = ReadType(it, w);
                var x = Prop(it, "x") is { } xe ? Num(xe, w) : throw new MapFileException($"{w}: needs \"x\"");
                var z = Prop(it, "z") is { } ze ? Num(ze, w) : throw new MapFileException($"{w}: needs \"z\"");
                var d = DefaultItemSize(type);
                var (size, _) = ReadSize(it, type, (d, d), w);
                var color = Prop(it, "color") is { } ce ? ToColour(ce, $"{w} (color)", "") : (Vector3?)null;
                spec.Items.Add(new FoliageItem(type, x, z, size, color));
            }
        }
        return spec;
    }

    private static void CheckKeys(JsonElement obj, string[] known, string where)
    {
        if (obj.ValueKind != JsonValueKind.Object)
            throw new MapFileException($"{where}: expected an object");
        foreach (var p in obj.EnumerateObject())
            if (!known.Any(k => string.Equals(k, p.Name, StringComparison.OrdinalIgnoreCase)))
                throw new MapFileException($"{where}: unknown field \"{p.Name}\" (it takes: {string.Join(", ", known)})");
    }

    private static IEnumerable<JsonElement> JsonList(JsonElement e, string where) =>
        e.ValueKind == JsonValueKind.Array ? e.EnumerateArray() : throw new MapFileException($"{where}: expected a list [...]");

    private static FoliageType ReadType(JsonElement obj, string where)
    {
        var names = string.Join(", ", Enum.GetNames<FoliageType>().Select(n => n.ToLowerInvariant()));
        if (Prop(obj, "type") is not { ValueKind: JsonValueKind.String } t)
            throw new MapFileException($"{where}: needs \"type\" ({names})");
        if (!Enum.TryParse<FoliageType>(t.GetString(), true, out var type) || !Enum.IsDefined(type) || int.TryParse(t.GetString(), out _))
            throw new MapFileException($"{where}: unknown type \"{t.GetString()}\" (known: {names})");
        return type;
    }

    /// <summary>"scale" (small plants) or "height" (trees): a number or [min, max].</summary>
    private static (float Min, float Max) ReadSize(JsonElement obj, FoliageType type, (float, float) fallback, string where)
    {
        var key = SizeKey(type);
        var other = key == "height" ? "scale" : "height";
        if (Prop(obj, other) != null)
            throw new MapFileException($"{where}: a {TypeName(type)} is sized with \"{key}\", not \"{other}\"");
        if (Prop(obj, key) is not { } e)
            return fallback;
        float min, max;
        if (e.ValueKind == JsonValueKind.Array)
        {
            var v = Floats(e, 2, $"{where} ({key})");
            (min, max) = (MathF.Min(v[0], v[1]), MathF.Max(v[0], v[1]));
        }
        else
            min = max = Num(e, $"{where} ({key})");
        var (lo, hi) = Foliage.IsTree(type) ? (2f, 30f) : (0.1f, 5f);
        if (min < lo || max > hi)
            throw new MapFileException($"{where}: {key} must be between {lo} and {hi}");
        return (min, max);
    }

    private static float[] Floats(JsonElement e, int count, string where)
    {
        if (e.ValueKind != JsonValueKind.Array || e.GetArrayLength() != count)
            throw new MapFileException($"{where}: expected a list of {count} numbers");
        var v = new float[count];
        for (var i = 0; i < count; i++)
            v[i] = Num(e[i], where);
        return v;
    }

    /// <summary>[[x, z], [x, z], ...] flattened to x, z, x, z...</summary>
    private static float[] Points(JsonElement e, string where)
    {
        if (e.ValueKind != JsonValueKind.Array)
            throw new MapFileException($"{where}: expected a list of [x, z] points");
        var v = new List<float>();
        foreach (var p in e.EnumerateArray())
        {
            var xz = Floats(p, 2, $"{where} (each point is [x, z])");
            v.Add(xz[0]);
            v.Add(xz[1]);
        }
        return v.ToArray();
    }

    private static int ReadInt(JsonElement e, string where) =>
        e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out var i) ? i : throw new MapFileException($"{where}: expected a whole number, got {e}");

    private static bool ReadBool(JsonElement e, string where) =>
        e.ValueKind is JsonValueKind.True or JsonValueKind.False ? e.GetBoolean() : throw new MapFileException($"{where}: expected true or false, got {e}");
}
