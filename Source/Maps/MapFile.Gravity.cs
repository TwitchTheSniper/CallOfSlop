using System.Numerics;
using System.Text.Json;

namespace CallOfSlop;

public static partial class MapFile
{
    private const int MaxGravityZones = 32;

    /// <summary>
    /// The top-level "gravity" field: <c>{ "type": "uniform" }</c> (the default: straight down, the same as leaving it out),
    /// <c>{ "type": "uniform", "strength": 6 }</c> (low gravity everywhere),
    /// <c>{ "type": "point", "center": [x, y, z], "strength": 18, "axis": [0, 0, -1] }</c> (a planet: everything falls
    /// towards the centre) or <c>{ "type": "zones", "default": {...}, "zones": [ { "shape": "box", "min": [...], "max": [...],
    /// "field": {...} }, { "shape": "sphere", "center": [...], "radius": r, "field": {...} } ] }</c> (several regions; the
    /// first zone containing a position wins, else the default; see GravityZones.cs). Returns null for plain uniform gravity.
    /// </summary>
    private static IGravityField? ReadGravity(JsonElement e, string source)
    {
        var where = $"{source}: gravity";
        if (e.ValueKind != JsonValueKind.Object)
            throw new MapFileException($"{where}: expected {{ \"type\": \"point\", \"center\": [x, y, z], \"strength\": 18 }} or {{ \"type\": \"zones\", ... }}");
        var type = GravityType(e, where, "point");
        if (type == "zones")
            return ReadZones(e, where);
        var field = ReadField(e, where, "point", topLevel: true);
        return field is UniformGravity { StraightDown: true } u && u.Strength == World.Gravity && u.Axis == -Vector3.UnitZ ? null : field;
    }

    private static string GravityType(JsonElement e, string where, string fallback) =>
        Prop(e, "type") is { ValueKind: JsonValueKind.String } t ? t.GetString()!.Trim().ToLowerInvariant() : fallback;

    /// <summary>One field (uniform or point). At the top level a uniform field must point straight down: sideways gravity goes in a zone.</summary>
    private static IGravityField ReadField(JsonElement e, string where, string fallbackType, bool topLevel)
    {
        if (e.ValueKind != JsonValueKind.Object)
            throw new MapFileException($"{where}: expected {{ \"type\": \"uniform\" | \"point\", ... }}");
        var type = GravityType(e, where, fallbackType);
        if (type == "zones")
            throw new MapFileException($"{where}: \"zones\" can't hold zones (a zone's field is \"uniform\" or \"point\")");
        if (type is not ("uniform" or "point"))
            throw new MapFileException($"{where}: \"type\" is \"uniform\", \"point\" or \"zones\", got \"{type}\"");
        var allowed = type == "uniform" ? "type, direction, strength, axis" : "type, center, strength, axis";
        foreach (var p in e.EnumerateObject())
        {
            var key = p.Name.ToLowerInvariant();
            var ok = key is "type" or "strength" or "axis" || (type == "uniform" ? key == "direction" : key is "center" or "centre");
            if (!ok)
                throw new MapFileException($"{where}: unknown key \"{p.Name}\" (a {type} field takes: {allowed})");
        }
        var strength = Prop(e, "strength") is { } s ? Num(s, $"{where}: strength") : World.Gravity;
        if (strength is < 1f or > 60f || !float.IsFinite(strength))
            throw new MapFileException($"{where}: \"strength\" is 1 to 60 (m/s^2; the usual is {World.Gravity})");
        var axis = Prop(e, "axis") is { } a ? Vec3(a, $"{where}: axis") : Vector3.Zero;
        if (Prop(e, "axis") != null && axis.LengthSquared() < 1e-6f)
            throw new MapFileException($"{where}: \"axis\" can't be [0, 0, 0]");
        if (type == "uniform")
        {
            var dir = Prop(e, "direction") is { } d ? Vec3(d, $"{where}: direction") : -Vector3.UnitY;
            if (dir.LengthSquared() < 1e-6f)
                throw new MapFileException($"{where}: \"direction\" can't be [0, 0, 0] (it's the way things fall, e.g. [0, -1, 0])");
            dir = Vector3.Normalize(dir);
            if (MathF.Abs(dir.X) < 1e-6f && MathF.Abs(dir.Z) < 1e-6f && dir.Y < 0)
                dir = -Vector3.UnitY; // exactly straight down (so the Y-up code paths apply)
            if (topLevel && dir != -Vector3.UnitY)
                throw new MapFileException($"{where}: a map-wide uniform field points straight down (\"direction\" [0, -1, 0]); sideways gravity goes in a zone (\"type\": \"zones\")");
            if (axis.LengthSquared() > 0 && MathF.Abs(Vector3.Dot(Vector3.Normalize(axis), dir)) > 0.999f)
                throw new MapFileException($"{where}: \"axis\" can't run along the \"direction\" (it's the frame's north, laid flat on the ground)");
            return new UniformGravity(dir * strength, axis);
        }
        var centerEl = Prop(e, "center") ?? Prop(e, "centre");
        var center = centerEl is { } c ? Vec3(c, $"{where}: center") : Vector3.Zero;
        return new PointGravity(center, strength, axis.LengthSquared() > 0 ? axis : -Vector3.UnitZ);
    }

    private static ZonedGravity ReadZones(JsonElement e, string where)
    {
        foreach (var p in e.EnumerateObject())
            if (p.Name.ToLowerInvariant() is not ("type" or "default" or "zones"))
                throw new MapFileException($"{where}: unknown key \"{p.Name}\" (zones take: type, default, zones)");
        var def = Prop(e, "default") is { ValueKind: not JsonValueKind.Null } d ? ReadField(d, $"{where}: default", "uniform", topLevel: true) : UniformGravity.Down;
        if (Prop(e, "zones") is not { ValueKind: JsonValueKind.Array } arr)
            throw new MapFileException($"{where}: \"zones\" is a list of {{ \"shape\": \"box\" | \"sphere\", ..., \"field\": {{...}} }}");
        var zones = new List<GravityZone>();
        var i = 0;
        foreach (var z in arr.EnumerateArray())
        {
            var zw = $"{where}: zones[{i}]";
            if (zones.Count >= MaxGravityZones)
                throw new MapFileException($"{where}: at most {MaxGravityZones} zones");
            if (z.ValueKind != JsonValueKind.Object)
                throw new MapFileException($"{zw}: expected {{ \"shape\": \"box\", \"min\": [x, y, z], \"max\": [x, y, z], \"field\": {{...}} }}");
            var shape = Prop(z, "shape") is { ValueKind: JsonValueKind.String } sh ? sh.GetString()!.Trim().ToLowerInvariant() : "";
            if (shape is not ("box" or "sphere"))
                throw new MapFileException($"{zw}: \"shape\" is \"box\" or \"sphere\", got \"{shape}\"");
            var allowed = shape == "box" ? "shape, min, max, field, name" : "shape, center, radius, field, name";
            foreach (var p in z.EnumerateObject())
            {
                var key = p.Name.ToLowerInvariant();
                var ok = key is "shape" or "field" or "name" || (shape == "box" ? key is "min" or "max" : key is "center" or "centre" or "radius");
                if (!ok)
                    throw new MapFileException($"{zw}: unknown key \"{p.Name}\" (a {shape} zone takes: {allowed})");
            }
            var name = Prop(z, "name") is { ValueKind: JsonValueKind.String } n ? n.GetString()!.Trim() : $"zone {i + 1}";
            if (Prop(z, "field") is not { ValueKind: JsonValueKind.Object } f)
                throw new MapFileException($"{zw}: needs a \"field\" ({{ \"type\": \"uniform\", \"direction\": [0, -1, 0], \"strength\": 6 }} or {{ \"type\": \"point\", \"center\": [...], \"strength\": 18 }})");
            var field = ReadField(f, $"{zw}: field", "uniform", topLevel: false);
            if (shape == "box")
            {
                if (Prop(z, "min") is not { } mn || Prop(z, "max") is not { } mx)
                    throw new MapFileException($"{zw}: a box zone needs \"min\" and \"max\" corners");
                var min = Vec3(mn, $"{zw}: min");
                var max = Vec3(mx, $"{zw}: max");
                if (!(max.X - min.X >= 0.5f && max.Y - min.Y >= 0.5f && max.Z - min.Z >= 0.5f))
                    throw new MapFileException($"{zw}: \"max\" must be at least 0.5 m beyond \"min\" on every axis");
                zones.Add(GravityZone.Box(min, max, field, name));
            }
            else
            {
                if ((Prop(z, "center") ?? Prop(z, "centre")) is not { } c)
                    throw new MapFileException($"{zw}: a sphere zone needs a \"center\" and a \"radius\"");
                var center = Vec3(c, $"{zw}: center");
                var radius = Prop(z, "radius") is { } r ? Num(r, $"{zw}: radius") : 0f;
                if (radius is < 1f or > 400f || !float.IsFinite(radius))
                    throw new MapFileException($"{zw}: \"radius\" is 1 to 400 m");
                zones.Add(GravityZone.Sphere(center, radius, field, name));
            }
            i++;
        }
        if (zones.Count == 0)
            throw new MapFileException($"{where}: \"zones\" needs at least one zone (or use \"type\": \"uniform\" / \"point\")");
        return new ZonedGravity(def, zones.ToArray());
    }

    private static string GravityToJson(IGravityField g) => g switch
    {
        PointGravity p => $"{{ \"type\": \"point\", \"center\": {Col(p.Center)}, \"strength\": {Num(p.Strength)}, \"axis\": {Col(p.Axis)} }}",
        UniformGravity u => $"{{ \"type\": \"uniform\", \"direction\": {Col(u.Up * -1f)}, \"strength\": {Num(u.Strength)}, \"axis\": {Col(u.Axis)} }}",
        ZonedGravity z => "{ \"type\": \"zones\", \"default\": " + GravityToJson(z.Default) + ", \"zones\": [\n"
            + string.Join(",\n", z.Zones.Select(zone => "    " + (zone.Shape == GravityZoneShape.Box
                ? $"{{ \"shape\": \"box\", \"name\": \"{zone.Name}\", \"min\": {Col(zone.Min)}, \"max\": {Col(zone.Max)}, \"field\": {GravityToJson(zone.Field)} }}"
                : $"{{ \"shape\": \"sphere\", \"name\": \"{zone.Name}\", \"center\": {Col(zone.Center)}, \"radius\": {Num(zone.Radius)}, \"field\": {GravityToJson(zone.Field)} }}")))
            + "\n  ] }",
        _ => "{ \"type\": \"uniform\" }",
    };
}
