using System.Numerics;
using System.Text.Json;
using CallOfSlop.Space;

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
        if (type == "sum")
            return ReadSum(e, where);
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
            throw new MapFileException($"{where}: \"zones\" can't hold zones (a zone's field is \"uniform\", \"point\" or \"sum\")");
        if (type == "sum")
            return ReadSum(e, where);
        if (type is not ("uniform" or "point"))
            throw new MapFileException($"{where}: \"type\" is \"uniform\", \"point\", \"sum\" or \"zones\", got \"{type}\"");
        var allowed = type == "uniform" ? "type, direction, strength, axis" : "type, center, strength, axis, inside";
        foreach (var p in e.EnumerateObject())
        {
            var key = p.Name.ToLowerInvariant();
            var ok = key is "type" or "strength" or "axis" || (type == "uniform" ? key == "direction" : key is "center" or "centre" or "inside");
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
        var inside = Prop(e, "inside") is { } ins ? Num(ins, $"{where}: inside") : 0f; // (a hollow planetoid's cavity: the pull turns round in it)
        if (!(inside >= 0) || !float.IsFinite(inside))
            throw new MapFileException($"{where}: \"inside\" is a radius in metres (0: none)");
        return new PointGravity(center, strength, axis.LengthSquared() > 0 ? axis : -Vector3.UnitZ, inside);
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

    private const int MaxGravitySources = 16, MaxMascons = 4096, MaxTetras = 4096, MaxTetraVertices = 8192;

    /// <summary>
    /// A summed field (GravitySources.cs, docs/space/GRAVITY.md): <c>{ "type": "sum", "axis": [0, 0, -1], "far": 20,
    /// "sources": [ ... ] }</c>. Each source is <c>{ "type": "point", "center": [...], "radius": r, "surface": g }</c> (a round
    /// body: g at its radius, a uniform ball inside), <c>{ "type": "mascons", "points": [[x, y, z], [x, y, z, weight], ...],
    /// "soft": m, ... }</c>, <c>{ "type": "tetra", "vertices": [[x, y, z], ...], "tetras": [[a, b, c, d], [a, b, c, d,
    /// density], ...], ... }</c> or <c>{ "type": "uniform", "direction": [...], "strength": s }</c>. A body's pull is its
    /// <c>"mu"</c> (GM, m^3/s^2) or <c>"surface"</c> gravity at <c>"radius"</c> metres from its centre of mass.
    /// </summary>
    private static SummedGravity ReadSum(JsonElement e, string where)
    {
        var model = ReadSumModel(e, where, 1, out var axis);
        return new SummedGravity(model, axis);
    }

    /// <summary>
    /// A body's lumps in a star system file (docs/space/GRAVITY.md "Lumpy bodies"): a sum's sources, read as a map's are,
    /// in the body's body-fixed frame (metres from its centre, z its spin axis). The sources' masses are shares: the whole
    /// is scaled to the body's own GM (<paramref name="mu"/>). No uniform sources (a body pulls, it doesn't blow).
    /// </summary>
    internal static GravityModel ReadBodyLumps(JsonElement e, string where, double mu)
    {
        foreach (var p in e.EnumerateObject())
            if (p.Name.ToLowerInvariant() is not ("sources" or "far"))
                throw new MapFileException($"{where}: unknown key \"{p.Name}\" (lumps take: sources, far)");
        var first = ReadSumModel(e, where, 1, out _);
        if (first.Sources.Any(x => x is UniformSource))
            throw new MapFileException($"{where}: a body's lumps can't hold a \"uniform\" source");
        var total = first.Mu;
        if (!(total > 0) || !double.IsFinite(total))
            throw new MapFileException($"{where}: the lumps have no mass");
        return ReadSumModel(e, where, mu / total, out _); // (read again with every source's share of the body's GM)
    }

    private static GravityModel ReadSumModel(JsonElement e, string where, double muScale, out Vector3 axis)
    {
        foreach (var p in e.EnumerateObject())
            if (p.Name.ToLowerInvariant() is not ("type" or "sources" or "axis" or "far"))
                throw new MapFileException($"{where}: unknown key \"{p.Name}\" (a sum takes: type, sources, axis, far)");
        if (Prop(e, "sources") is not { ValueKind: JsonValueKind.Array } arr || arr.GetArrayLength() == 0)
            throw new MapFileException($"{where}: a sum needs \"sources\": a list of {{ \"type\": \"point\" | \"mascons\" | \"tetra\" | \"uniform\", ... }}");
        if (arr.GetArrayLength() > MaxGravitySources)
            throw new MapFileException($"{where}: at most {MaxGravitySources} sources");
        axis = Prop(e, "axis") is { } a ? Vec3(a, $"{where}: axis") : -Vector3.UnitZ;
        if (axis.LengthSquared() < 1e-6f)
            throw new MapFileException($"{where}: \"axis\" can't be [0, 0, 0]");
        var far = Prop(e, "far") is { } f ? Num(f, $"{where}: far") : 20f;
        if (far is < 4f or > 1000f || !float.IsFinite(far))
            throw new MapFileException($"{where}: \"far\" is 4 to 1000 (bounding radii past which a source is one point)");
        var sources = new List<GravitySource>();
        var i = 0;
        foreach (var s in arr.EnumerateArray())
            sources.Add(ReadSource(s, $"{where}: sources[{i++}]", muScale));
        if (sources.All(x => x is UniformSource))
            throw new MapFileException($"{where}: a sum needs at least one body (point, mascons or tetra); for plain gravity use \"uniform\"");
        return new GravityModel(sources.ToArray()) { FarFactor = far };
    }

    private static GravitySource ReadSource(JsonElement e, string where, double muScale = 1)
    {
        if (e.ValueKind != JsonValueKind.Object)
            throw new MapFileException($"{where}: expected {{ \"type\": \"point\" | \"mascons\" | \"tetra\" | \"uniform\", ... }}");
        var type = GravityType(e, where, "");
        var allowed = type switch
        {
            "point" => new[] { "type", "name", "center", "centre", "radius", "surface", "mu" },
            "mascons" => new[] { "type", "name", "points", "soft", "radius", "surface", "mu" },
            "tetra" => new[] { "type", "name", "vertices", "tetras", "radius", "surface", "mu" },
            "uniform" => new[] { "type", "name", "direction", "strength" },
            _ => throw new MapFileException($"{where}: \"type\" is \"point\", \"mascons\", \"tetra\" or \"uniform\", got \"{type}\""),
        };
        foreach (var p in e.EnumerateObject())
            if (!allowed.Contains(p.Name.ToLowerInvariant()))
                throw new MapFileException($"{where}: unknown key \"{p.Name}\" (a {type} source takes: {string.Join(", ", allowed)})");
        var name = Prop(e, "name") is { ValueKind: JsonValueKind.String } n ? n.GetString()!.Trim() : type;
        if (type == "uniform")
        {
            var dir = Prop(e, "direction") is { } d ? Vec3(d, $"{where}: direction") : -Vector3.UnitY;
            if (dir.LengthSquared() < 1e-6f)
                throw new MapFileException($"{where}: \"direction\" can't be [0, 0, 0]");
            var strength = Prop(e, "strength") is { } st ? Num(st, $"{where}: strength") : World.Gravity;
            if (strength is < 0f or > 60f || !float.IsFinite(strength))
                throw new MapFileException($"{where}: \"strength\" is 0 to 60 m/s^2");
            return new UniformSource(Vec3d.From(Vector3.Normalize(dir) * strength), name);
        }
        var radius = Prop(e, "radius") is { } r ? Num(r, $"{where}: radius") : 0f;
        if (radius < 0 || !float.IsFinite(radius) || radius > 1e6f)
            throw new MapFileException($"{where}: \"radius\" is 0 to 1,000,000 m");
        double mu;
        if (Prop(e, "mu") is { } m)
        {
            if (Prop(e, "surface") != null)
                throw new MapFileException($"{where}: give \"mu\" or \"surface\", not both");
            mu = Num(m, $"{where}: mu");
        }
        else if (Prop(e, "surface") is { } sg)
        {
            var g = Num(sg, $"{where}: surface");
            if (g is < 0.01f or > 60f || !float.IsFinite(g))
                throw new MapFileException($"{where}: \"surface\" is 0.01 to 60 m/s^2");
            if (radius <= 0)
                throw new MapFileException($"{where}: \"surface\" gravity needs the \"radius\" it's measured at");
            mu = (double)g * radius * radius;
        }
        else
            throw new MapFileException($"{where}: needs \"surface\" (m/s^2 at \"radius\") or \"mu\" (GM, m^3/s^2)");
        if (!(mu > 0) || !double.IsFinite(mu))
            throw new MapFileException($"{where}: \"mu\" must be above 0");
        mu *= muScale;
        try
        {
            switch (type)
            {
                case "point":
                    var c = (Prop(e, "center") ?? Prop(e, "centre")) is { } ce ? Vec3(ce, $"{where}: center") : Vector3.Zero;
                    return new PointSource(Vec3d.From(c), mu, radius, name);
                case "mascons":
                {
                    if (Prop(e, "points") is not { ValueKind: JsonValueKind.Array } pts || pts.GetArrayLength() == 0)
                        throw new MapFileException($"{where}: \"points\" is a list of [x, y, z] or [x, y, z, weight]");
                    if (pts.GetArrayLength() > MaxMascons)
                        throw new MapFileException($"{where}: at most {MaxMascons} points");
                    var list = new List<Vec3d>();
                    var w = new List<double>();
                    var k = 0;
                    foreach (var q in pts.EnumerateArray())
                    {
                        var v = Floats(q, $"{where}: points[{k++}]", 3, 4);
                        list.Add(new Vec3d(v[0], v[1], v[2]));
                        w.Add(v.Length > 3 ? v[3] : 1f);
                    }
                    var soft = Prop(e, "soft") is { } so ? Num(so, $"{where}: soft") : 1f;
                    if (soft is < 0f or > 10000f || !float.IsFinite(soft))
                        throw new MapFileException($"{where}: \"soft\" is 0 to 10,000 m");
                    return new MasconSource(list.ToArray(), w.ToArray(), mu, soft, name);
                }
                default:
                {
                    if (Prop(e, "vertices") is not { ValueKind: JsonValueKind.Array } vs || vs.GetArrayLength() < 4)
                        throw new MapFileException($"{where}: \"vertices\" is a list of at least four [x, y, z]");
                    if (vs.GetArrayLength() > MaxTetraVertices)
                        throw new MapFileException($"{where}: at most {MaxTetraVertices} vertices");
                    if (Prop(e, "tetras") is not { ValueKind: JsonValueKind.Array } ts || ts.GetArrayLength() == 0)
                        throw new MapFileException($"{where}: \"tetras\" is a list of [a, b, c, d] or [a, b, c, d, density] (vertex indices from 0)");
                    if (ts.GetArrayLength() > MaxTetras)
                        throw new MapFileException($"{where}: at most {MaxTetras} tetras");
                    var verts = new List<Vec3d>();
                    var k = 0;
                    foreach (var q in vs.EnumerateArray())
                    {
                        var v = Floats(q, $"{where}: vertices[{k++}]", 3, 3);
                        verts.Add(new Vec3d(v[0], v[1], v[2]));
                    }
                    var tets = new List<int[]>();
                    var dens = new List<double>();
                    k = 0;
                    foreach (var q in ts.EnumerateArray())
                    {
                        var v = Floats(q, $"{where}: tetras[{k}]", 4, 5);
                        var idx = new int[4];
                        for (var j = 0; j < 4; j++)
                        {
                            if (v[j] != MathF.Floor(v[j]) || v[j] < 0 || v[j] >= verts.Count)
                                throw new MapFileException($"{where}: tetras[{k}]: vertex {v[j]} isn't an index from 0 to {verts.Count - 1}");
                            idx[j] = (int)v[j];
                        }
                        tets.Add(idx);
                        dens.Add(v.Length > 4 ? v[4] : 1f);
                        k++;
                    }
                    return new TetraSource(verts.ToArray(), tets.ToArray(), dens.ToArray(), mu, name);
                }
            }
        }
        catch (ArgumentException ex)
        {
            throw new MapFileException($"{where}: {ex.Message}");
        }
    }

    private static float[] Floats(JsonElement e, string where, int min, int max)
    {
        if (e.ValueKind != JsonValueKind.Array || e.GetArrayLength() < min || e.GetArrayLength() > max)
            throw new MapFileException($"{where}: expected {min}{(max > min ? $" or {max}" : "")} numbers");
        var r = new float[e.GetArrayLength()];
        var i = 0;
        foreach (var x in e.EnumerateArray())
            r[i] = Num(x, $"{where}[{i++}]");
        return r;
    }

    private static string GravityToJson(IGravityField g) => g switch
    {
        PointGravity p => $"{{ \"type\": \"point\", \"center\": {Col(p.Center)}, \"strength\": {Num(p.Strength)}, \"axis\": {Col(p.Axis)}{(p.Inside > 0 ? $", \"inside\": {Num(p.Inside)}" : "")} }}",
        UniformGravity u => $"{{ \"type\": \"uniform\", \"direction\": {Col(u.Up * -1f)}, \"strength\": {Num(u.Strength)}, \"axis\": {Col(u.Axis)} }}",
        ZonedGravity z => "{ \"type\": \"zones\", \"default\": " + GravityToJson(z.Default) + ", \"zones\": [\n"
            + string.Join(",\n", z.Zones.Select(zone => "    " + (zone.Shape == GravityZoneShape.Box
                ? $"{{ \"shape\": \"box\", \"name\": \"{zone.Name}\", \"min\": {Col(zone.Min)}, \"max\": {Col(zone.Max)}, \"field\": {GravityToJson(zone.Field)} }}"
                : $"{{ \"shape\": \"sphere\", \"name\": \"{zone.Name}\", \"center\": {Col(zone.Center)}, \"radius\": {Num(zone.Radius)}, \"field\": {GravityToJson(zone.Field)} }}")))
            + "\n  ] }",
        SummedGravity s => SumToJson(s),
        _ => "{ \"type\": \"uniform\" }",
    };

    private static string SumToJson(SummedGravity s)
    {
        static string V(Vec3d v) => Col(v.ToVector3());
        var parts = s.Model.Sources.Select(src => src switch
        {
            PointSource p => $"{{ \"type\": \"point\", \"name\": \"{p.Name}\", \"center\": {V(p.Center)}, \"radius\": {Num((float)p.Radius)}, \"mu\": {p.Mu.ToString("R", System.Globalization.CultureInfo.InvariantCulture)} }}",
            UniformSource u => $"{{ \"type\": \"uniform\", \"name\": \"{u.Name}\", \"direction\": {V(u.G.Normalized())}, \"strength\": {Num((float)u.G.Length)} }}",
            MasconSource m => $"{{ \"type\": \"mascons\", \"name\": \"{m.Name}\", \"soft\": {Num((float)m.Soft)}, \"mu\": {m.Mu.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}, \"points\": ["
                + string.Join(", ", m.Points.Select((q, i) => $"[{Num((float)q.X)}, {Num((float)q.Y)}, {Num((float)q.Z)}, {(m.Mus[i] / m.Mu).ToString("R", System.Globalization.CultureInfo.InvariantCulture)}]")) + "] }",
            _ => $"{{ \"type\": \"uniform\", \"name\": \"{src.Name} (not exported)\", \"strength\": 0 }}",
        });
        return $"{{ \"type\": \"sum\", \"axis\": {Col(s.Axis)}, \"far\": {Num((float)s.Model.FarFactor)}, \"sources\": [\n    " + string.Join(",\n    ", parts) + "\n  ] }";
    }
}
