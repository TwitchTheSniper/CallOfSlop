using System.Numerics;
using System.Text;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>Reading and writing a map file's optional "boundary" section (see Maps/README.md, "Boundaries").</summary>
public static partial class MapFile
{
    private static readonly string[] BoundaryCommonKeys = ["shape", "noise", "edge", "groundMargin", "barrierHeight", "tolerance"];

    private static readonly Dictionary<BoundaryShape, string[]> BoundaryShapeKeys = new()
    {
        [BoundaryShape.Rect] = ["center", "halfX", "halfZ", "round"],
        [BoundaryShape.Circle] = ["center", "radius"],
        [BoundaryShape.Ellipse] = ["center", "radiusX", "radiusZ", "rotation"],
        [BoundaryShape.Polygon] = ["points"],
        [BoundaryShape.Smooth] = ["points"],
    };

    private static readonly string[] NoiseKeys = ["amplitude", "scale", "seed", "octaves"];
    private static readonly string[] EdgeKeys = ["style", "height", "thickness", "color", "material", "spacing", "width", "props", "seed"];

    // ---- Writing ----------------------------------------------------------------------------------

    /// <summary>The section as JSON (only the fields that differ from the defaults).</summary>
    public static string BoundaryToJson(BoundarySpec s, string indent = "  ")
    {
        var parts = ShapeFieldsToJson(s);
        var e = s.Edge;
        var ep = new List<string> { $"\"style\": {Str(e.Style.ToString().ToLowerInvariant())}" };
        if (e.Height > 0) ep.Add($"\"height\": {Num(e.Height)}");
        if (e.Thickness > 0) ep.Add($"\"thickness\": {Num(e.Thickness)}");
        if (e.Color is { } c) ep.Add($"\"color\": {Col(c)}");
        if (e.Material.Length > 0) ep.Add($"\"material\": {Str(e.Material)}");
        if (e.Spacing > 0) ep.Add($"\"spacing\": {Num(e.Spacing)}");
        if (e.Width > 0) ep.Add($"\"width\": {Num(e.Width)}");
        if (e.Props.Length > 0) ep.Add("\"props\": [" + string.Join(", ", e.Props.Select(Str)) + "]");
        if (e.Seed != 0) ep.Add($"\"seed\": {e.Seed}");
        parts.Add("\"edge\": { " + string.Join(", ", ep) + " }");
        if (s.GroundMargin != 6f) parts.Add($"\"groundMargin\": {Num(s.GroundMargin)}");
        if (s.BarrierHeight > 0) parts.Add($"\"barrierHeight\": {Num(s.BarrierHeight)}");
        if (s.Tolerance != 0.05f) parts.Add($"\"tolerance\": {Num(s.Tolerance)}");
        return "{\n" + string.Join(",\n", parts.Select(p => indent + "  " + p)) + "\n" + indent + "}";
    }

    /// <summary>The "shape" field, the shape's own fields and "noise" (shared by boundaries and foliage zones).</summary>
    private static List<string> ShapeFieldsToJson(BoundarySpec s)
    {
        var parts = new List<string> { $"\"shape\": {Str(s.Shape.ToString().ToLowerInvariant())}" };
        switch (s.Shape)
        {
            case BoundaryShape.Rect:
                if (s.Center != Vector2.Zero) parts.Add($"\"center\": {P2(s.Center)}");
                if (s.HalfX > 0) parts.Add($"\"halfX\": {Num(s.HalfX)}");
                if (s.HalfZ > 0) parts.Add($"\"halfZ\": {Num(s.HalfZ)}");
                if (s.Round > 0) parts.Add($"\"round\": {Num(s.Round)}");
                break;
            case BoundaryShape.Circle:
                if (s.Center != Vector2.Zero) parts.Add($"\"center\": {P2(s.Center)}");
                parts.Add($"\"radius\": {Num(s.Radius)}");
                break;
            case BoundaryShape.Ellipse:
                if (s.Center != Vector2.Zero) parts.Add($"\"center\": {P2(s.Center)}");
                parts.Add($"\"radiusX\": {Num(s.RadiusX)}");
                parts.Add($"\"radiusZ\": {Num(s.RadiusZ)}");
                if (s.Rotation != 0) parts.Add($"\"rotation\": {Num(s.Rotation)}");
                break;
            default:
                parts.Add("\"points\": [" + string.Join(", ", s.Points.Select(P2)) + "]");
                break;
        }
        if (s.Noise is { } n)
            parts.Add($"\"noise\": {{ \"amplitude\": {Num(n.Amplitude)}, \"scale\": {Num(n.Scale)}, \"seed\": {n.Seed}, \"octaves\": {n.Octaves} }}");
        return parts;
    }

    private static string P2(Vector2 p) => $"[{Num(p.X)}, {Num(p.Y)}]";

    // ---- Reading ----------------------------------------------------------------------------------

    /// <summary>Parses a "boundary" object. Throws <see cref="MapFileException"/> with a readable message.</summary>
    public static BoundarySpec ReadBoundary(JsonElement e, string source)
    {
        var where = $"{source}: boundary";
        if (e.ValueKind != JsonValueKind.Object)
            throw new MapFileException($"{where} must be an object, e.g. {{ \"shape\": \"circle\", \"radius\": 40 }}");
        var s = ReadShape(e, where, "boundary", BoundaryCommonKeys);

        float Opt(JsonElement obj, string name, float def, string w) => Prop(obj, name) is { } v ? Num(v, $"{w} \"{name}\"") : def;
        int OptInt(JsonElement obj, string name, int def, string w) =>
            Prop(obj, name) is { } v ? (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : throw new MapFileException($"{w}: \"{name}\" is a whole number")) : def;
        if (Prop(e, "edge") is { ValueKind: not JsonValueKind.Null } edge)
        {
            var w = $"{where} edge";
            var styleEl = edge.ValueKind == JsonValueKind.String ? edge : Prop(edge, "style");
            if (styleEl is not { ValueKind: JsonValueKind.String } st)
                throw new MapFileException($"{w}: needs \"style\" (none, wall, cliff, fence, hedge, rocks or props)");
            var styleName = st.GetString()!;
            if (!Enum.TryParse<BoundaryEdgeStyle>(styleName, true, out var style) || !Enum.IsDefined(style) || int.TryParse(styleName, out _))
                throw new MapFileException($"{w}: unknown style \"{styleName}\" (none, wall, cliff, fence, hedge, rocks or props)");
            s.Edge.Style = style;
            if (edge.ValueKind == JsonValueKind.Object)
            {
                CheckKeys(edge, EdgeKeys, w);
                s.Edge.Height = Opt(edge, "height", 0, w);
                s.Edge.Thickness = Opt(edge, "thickness", 0, w);
                s.Edge.Spacing = Opt(edge, "spacing", 0, w);
                s.Edge.Width = Opt(edge, "width", 0, w);
                s.Edge.Seed = OptInt(edge, "seed", 0, w);
                if (Prop(edge, "color") is { } col)
                    s.Edge.Color = ToColour(col, $"{w} \"color\"", "");
                if (Prop(edge, "material") is { } mat)
                {
                    s.Edge.Material = mat.ValueKind == JsonValueKind.String ? mat.GetString()! : throw new MapFileException($"{w}: \"material\" is a name like \"Concrete\"");
                    try
                    {
                        Map.ParseMeshMaterial(s.Edge.Material);
                    }
                    catch (InvalidOperationException ex)
                    {
                        throw new MapFileException($"{w}: {ex.Message}");
                    }
                }
                if (Prop(edge, "props") is { } props)
                    s.Edge.Props = (string[])Convert(props, typeof(string[]), $"{w} \"props\"")!;
                if (s.Edge.Height is < 0 or > 60 || s.Edge.Thickness is < 0 or > 10 || s.Edge.Spacing is < 0 or > 50 || s.Edge.Width is < 0 or > 20)
                    throw new MapFileException($"{w}: height is 0-60 m, thickness 0-10 m, spacing 0-50 m, width 0-20 m");
                if (s.Edge.Style == BoundaryEdgeStyle.Fence && s.Edge.Spacing is > 0 and < 0.5f || s.Edge.Style == BoundaryEdgeStyle.Rocks && s.Edge.Spacing is > 0 and < 0.5f)
                    throw new MapFileException($"{w}: spacing must be at least 0.5 m");
            }
            if (style == BoundaryEdgeStyle.Props && s.Edge.Props.Length == 0)
                throw new MapFileException($"{w}: a \"props\" edge needs \"props\": a list of prop or category names");
        }
        s.GroundMargin = Opt(e, "groundMargin", 6, where);
        s.BarrierHeight = Opt(e, "barrierHeight", 0, where);
        s.Tolerance = Opt(e, "tolerance", 0.05f, where);
        if (s.GroundMargin is < 0 or > 64 || s.BarrierHeight is < 0 or > 100 || s.Tolerance is < 0.005f or > 1)
            throw new MapFileException($"{where}: groundMargin is 0-64 m, barrierHeight 0-100 m, tolerance 0.005-1 m");
        return s;
    }

    /// <summary>
    /// Reads "shape", the shape's own fields and "noise" (boundaries and foliage zones). <paramref name="extraKeys"/> are the
    /// other fields the caller reads itself; anything else is an error.
    /// </summary>
    private static BoundarySpec ReadShape(JsonElement e, string where, string what, string[] extraKeys)
    {
        if (e.ValueKind != JsonValueKind.Object)
            throw new MapFileException($"{where} must be an object, e.g. {{ \"shape\": \"circle\", \"radius\": 40 }}");
        var s = new BoundarySpec();
        var shapeName = Prop(e, "shape") is { ValueKind: JsonValueKind.String } sh ? sh.GetString()! : throw new MapFileException($"{where}: missing \"shape\" (rect, circle, ellipse, polygon or smooth)");
        if (!Enum.TryParse(shapeName, true, out s.Shape) || !Enum.IsDefined(s.Shape) || int.TryParse(shapeName, out _))
            throw new MapFileException($"{where}: unknown shape \"{shapeName}\" (rect, circle, ellipse, polygon or smooth)");
        var allowed = new[] { "shape", "noise" }.Concat(extraKeys).Distinct(StringComparer.OrdinalIgnoreCase).Concat(BoundaryShapeKeys[s.Shape]).ToArray();
        foreach (var p in e.EnumerateObject())
            if (!allowed.Any(k => k.Equals(p.Name, StringComparison.OrdinalIgnoreCase)))
                throw new MapFileException($"{where}: a {shapeName} {what} has no \"{p.Name}\" (it takes: {string.Join(", ", allowed)})");

        float Opt(JsonElement obj, string name, float def, string w) => Prop(obj, name) is { } v ? Num(v, $"{w} \"{name}\"") : def;
        int OptInt(JsonElement obj, string name, int def, string w) =>
            Prop(obj, name) is { } v ? (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : throw new MapFileException($"{w}: \"{name}\" is a whole number")) : def;
        Vector2 Point(JsonElement p, string w) =>
            p.ValueKind == JsonValueKind.Array && p.GetArrayLength() == 2 ? new Vector2(Num(p[0], w), Num(p[1], w)) : throw new MapFileException($"{w}: a point is [x, z]");

        if (Prop(e, "center") is { } center)
            s.Center = Point(center, $"{where} \"center\"");
        s.HalfX = Opt(e, "halfX", 0, where);
        s.HalfZ = Opt(e, "halfZ", 0, where);
        s.Round = Opt(e, "round", 0, where);
        s.Radius = Opt(e, "radius", 0, where);
        s.RadiusX = Opt(e, "radiusX", 0, where);
        s.RadiusZ = Opt(e, "radiusZ", 0, where);
        s.Rotation = Opt(e, "rotation", 0, where);
        if (s.Shape == BoundaryShape.Circle && Prop(e, "radius") == null)
            throw new MapFileException($"{where}: a circle needs \"radius\"");
        if (s.Shape == BoundaryShape.Ellipse && (Prop(e, "radiusX") == null || Prop(e, "radiusZ") == null))
            throw new MapFileException($"{where}: an ellipse needs \"radiusX\" and \"radiusZ\"");
        if (s.Shape is BoundaryShape.Polygon or BoundaryShape.Smooth)
        {
            if (Prop(e, "points") is not { ValueKind: JsonValueKind.Array } pts || pts.GetArrayLength() < 3)
                throw new MapFileException($"{where}: a {shapeName} {what} needs \"points\": at least 3 [x, z] points in order round the outline");
            s.Points = pts.EnumerateArray().Select((p, i) => Point(p, $"{where} point {i + 1}")).ToArray();
        }
        if (Prop(e, "noise") is { ValueKind: not JsonValueKind.Null } noise)
        {
            var w = $"{where} noise";
            if (noise.ValueKind != JsonValueKind.Object)
                throw new MapFileException($"{w}: expected {{ \"amplitude\": 2, \"scale\": 12, \"seed\": 1 }}");
            CheckKeys(noise, NoiseKeys, w);
            s.Noise = new BoundaryNoise
            {
                Amplitude = Opt(noise, "amplitude", 0, w),
                Scale = Opt(noise, "scale", 12, w),
                Seed = OptInt(noise, "seed", 1, w),
                Octaves = OptInt(noise, "octaves", 2, w),
            };
            if (s.Noise.Amplitude is < 0 or > 30 || s.Noise.Scale is < 1 or > 200 || s.Noise.Octaves is < 1 or > 4)
                throw new MapFileException($"{w}: amplitude is 0-30 m, scale 1-200 m, octaves 1-4");
        }
        return s;
    }
}
