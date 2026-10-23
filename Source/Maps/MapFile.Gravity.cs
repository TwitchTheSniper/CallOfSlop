using System.Numerics;
using System.Text.Json;

namespace CallOfSlop;

public static partial class MapFile
{
    /// <summary>
    /// The top-level "gravity" field: <c>{ "type": "uniform" }</c> (the default: straight down, the same as leaving it out)
    /// or <c>{ "type": "point", "center": [x, y, z], "strength": 18, "axis": [0, 0, -1] }</c> (a planet: everything falls
    /// towards the centre). Returns null for uniform gravity.
    /// </summary>
    private static PointGravity? ReadGravity(JsonElement e, string source)
    {
        var where = $"{source}: gravity";
        if (e.ValueKind != JsonValueKind.Object)
            throw new MapFileException($"{where}: expected {{ \"type\": \"point\", \"center\": [x, y, z], \"strength\": 18 }}");
        var type = Prop(e, "type") is { ValueKind: JsonValueKind.String } t ? t.GetString()!.Trim().ToLowerInvariant() : "point";
        foreach (var p in e.EnumerateObject())
            if (p.Name.ToLowerInvariant() is not ("type" or "center" or "centre" or "strength" or "axis"))
                throw new MapFileException($"{where}: unknown key \"{p.Name}\" (it takes: type, center, strength, axis)");
        if (type == "uniform")
            return null;
        if (type != "point")
            throw new MapFileException($"{where}: \"type\" is \"uniform\" or \"point\", got \"{type}\"");
        var centerEl = Prop(e, "center") ?? Prop(e, "centre");
        var center = centerEl is { } c ? Vec3(c, $"{where}: center") : Vector3.Zero;
        var strength = Prop(e, "strength") is { } s ? Num(s, $"{where}: strength") : World.Gravity;
        if (strength is < 1f or > 60f)
            throw new MapFileException($"{where}: \"strength\" is 1 to 60 (m/s^2; the usual is {World.Gravity})");
        var axis = Prop(e, "axis") is { } a ? Vec3(a, $"{where}: axis") : -Vector3.UnitZ;
        if (axis.LengthSquared() < 1e-6f)
            throw new MapFileException($"{where}: \"axis\" can't be [0, 0, 0]");
        return new PointGravity(center, strength, axis);
    }

    private static string GravityToJson(PointGravity g) =>
        $"{{ \"type\": \"point\", \"center\": {Col(g.Center)}, \"strength\": {Num(g.Strength)}, \"axis\": {Col(g.Axis)} }}";
}
