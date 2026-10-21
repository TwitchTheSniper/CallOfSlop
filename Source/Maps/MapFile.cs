using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>Marks a <see cref="Map"/> builder method that map files may call (see Maps/README.md).</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class MapOpAttribute : Attribute;

public sealed class MapFileException(string message) : Exception(message);

/// <summary>
/// Map files. A map is a self-contained JSON file in the Maps folder: a few properties (name, size,
/// sky colour...) and a list of "ops", each one a call to a builder like Building, Container, Hill
/// or Block with named arguments. The game loads every *.json in Maps at startup, so new maps
/// (including third-party ones) are added by dropping a file in; no code changes.
///
/// Builder calls record themselves (only the top-level ones; a Building's internal walls aren't
/// recorded), which is how a map built in code can be written back out as a file.
/// </summary>
public sealed partial class Map
{
    /// <summary>One recorded builder call: the method name, its arguments in parameter order, and its terrain anchor.</summary>
    public readonly record struct RecordedOp(string Name, object?[] Args, OpAnchor Anchor = OpAnchor.Auto);

    /// <summary>The terrain anchor for the next top-level op (the map file loader sets it from an op's "anchor" key).</summary>
    public OpAnchor NextAnchor;

    private readonly List<RecordedOp> _ops = new();
    private int _opDepth;

    public IReadOnlyList<RecordedOp> Ops => _ops;

    /// <summary>Who made the map (shown in the lobby for third-party maps).</summary>
    public string Author = "";

    /// <summary>Sort order in the map list (built-in maps use 1-99; anything else defaults to 1000, then by name).</summary>
    public int Order = 1000;

    /// <summary>Draw every surface with the plain prototype grid instead of picking textures.</summary>
    public bool PrototypeGrid;

    /// <summary>The file the map was loaded from (null for maps built in code).</summary>
    public string? SourceFile;

    public readonly struct OpScope(Map map, bool top, string name, int firstBox, OpAnchor anchor) : IDisposable
    {
        public void Dispose()
        {
            map._opDepth--;
            if (top)
                map.PlaceOnTerrain(name, firstBox, anchor); // terrain maps: lift what the op built onto the ground
        }
    }

    private OpScope Op(object?[] args, [CallerMemberName] string name = "")
    {
        var top = _opDepth == 0;
        var anchor = OpAnchor.Auto;
        if (top)
        {
            anchor = NextAnchor;
            NextAnchor = OpAnchor.Auto;
            _topAnchor = anchor;
            _topName = name;
            _ops.Add(new RecordedOp(name, args, anchor));
        }
        _opDepth++;
        return new OpScope(this, top, name, Solid.Count, anchor);
    }

    /// <summary>
    /// A fingerprint of the map's geometry and spawns. Host and clients compare it to make sure
    /// they're playing the same version of a map (collisions must match for prediction to work).
    /// </summary>
    public uint ContentHash
    {
        get
        {
            var h = 2166136261u;
            void Mix(float f)
            {
                var bits = BitConverter.SingleToUInt32Bits(MathF.Round(f * 1000f) / 1000f);
                for (var i = 0; i < 4; i++)
                {
                    h ^= (bits >> (i * 8)) & 0xFF;
                    h *= 16777619u;
                }
            }
            Mix(HalfX);
            Mix(HalfZ);
            for (var i = 0; i < Solid.Count; i++)
            {
                var b = HashBox(i); // a broken prop's collider counts as it was: breaking never changes the fingerprint
                Mix(b.Center.X); Mix(b.Center.Y); Mix(b.Center.Z);
                Mix(b.Half.X); Mix(b.Half.Y); Mix(b.Half.Z);
            }
            for (var i = 0; i < (AuthoredSpawns >= 0 ? AuthoredSpawns : Spawns.Count); i++) // generated spawns follow from these (MapPlayers.cs)
            {
                var s = Spawns[i];
                Mix(s.X); Mix(s.Y); Mix(s.Z);
            }
            MixObjectiveData(Mix); // waypoints and hand-placed objectives (game-mode objectives are built from them)
            if (FoliageHash != 0) // maps without a "foliage" section keep their old fingerprint
            {
                for (var i = 0; i < 4; i++)
                {
                    h ^= (FoliageHash >> (i * 8)) & 0xFF;
                    h *= 16777619u;
                }
            }
            if (MoverHash is var movers and not 0) // maps without a "movers" section keep their old fingerprint
            {
                for (var i = 0; i < 4; i++)
                {
                    h ^= (movers >> (i * 8)) & 0xFF;
                    h *= 16777619u;
                }
            }
            if (PropHash != 0) // maps without props keep their old fingerprint
            {
                for (var i = 0; i < 4; i++)
                {
                    h ^= (PropHash >> (i * 8)) & 0xFF;
                    h *= 16777619u;
                }
            }
            if (Terrain != null) // terrain maps only, so flat maps keep the fingerprint they always had
            {
                for (var i = 0; i < 4; i++)
                {
                    h ^= (Terrain.Hash >> (i * 8)) & 0xFF;
                    h *= 16777619u;
                }
            }
            return h;
        }
    }
}

public static partial class MapFile
{
    public const string Format = "call-of-slop-map/1";

    private static readonly Dictionary<string, MethodInfo> Builders = typeof(Map)
        .GetMethods(BindingFlags.Public | BindingFlags.Instance)
        .Where(m => m.GetCustomAttribute<MapOpAttribute>() != null)
        .ToDictionary(m => m.Name, StringComparer.OrdinalIgnoreCase);

    /// <summary>Every builder a map file can use, with its parameters (for the docs and error messages).</summary>
    public static IEnumerable<string> BuilderSignatures() =>
        Builders.Values.OrderBy(m => m.Name).Select(m =>
            $"{m.Name}({string.Join(", ", m.GetParameters().Select(p => $"{TypeName(p.ParameterType)} {p.Name}{(p.HasDefaultValue ? $" = {Fmt(p.DefaultValue)}" : p.IsDefined(typeof(ParamArrayAttribute)) ? " = []" : "")}"))})");

    private static string TypeName(Type t) => t == typeof(float) ? "number" : t == typeof(int) ? "integer" : t == typeof(bool) ? "true/false"
        : t == typeof(string) ? "text" : t == typeof(string[]) ? "list of text" : t == typeof(Vector3) ? "colour" : t == typeof(Map.Hole[]) ? "holes" : Nullable.GetUnderlyingType(t) != null ? "rect or null" : t.Name;

    // ---- Writing --------------------------------------------------------------------------

    public static string ToJson(Map m)
    {
        var sb = new StringBuilder();
        sb.Append("{\n");
        sb.Append($"  \"format\": {Str(Format)},\n");
        sb.Append($"  \"name\": {Str(m.Name)},\n");
        sb.Append($"  \"description\": {Str(m.Description)},\n");
        if (m.Author.Length > 0)
            sb.Append($"  \"author\": {Str(m.Author)},\n");
        sb.Append($"  \"order\": {m.Order},\n");
        if (m.PrototypeGrid)
            sb.Append("  \"prototypeGrid\": true,\n");
        sb.Append($"  \"halfX\": {Num(m.HalfX)},\n");
        sb.Append($"  \"halfZ\": {Num(m.HalfZ)},\n");
        if (m.MaxPlayersSetting > 0)
            sb.Append($"  \"maxPlayers\": {m.MaxPlayersSetting},\n");
        if (m.Skybox is { } skybox)
            sb.Append("  \"skybox\": ").Append(SkyFile.ToJson(skybox)).Append(",\n"); // the horizon colour is the map's sky colour
        else
            sb.Append($"  \"sky\": {Col(m.Sky)},\n");
        if (m.TerrainSpec != null)
            sb.Append("  \"terrain\": ").Append(m.TerrainSpec.ToJson("  ")).Append(",\n");
        MapObjectiveJson.Write(sb, m);
        sb.Append("  \"ops\": [\n");
        for (var i = 0; i < m.Ops.Count; i++)
        {
            var op = m.Ops[i];
            var method = Builders[op.Name];
            var parts = new List<string> { $"\"op\": {Str(op.Name)}" };
            var ps = method.GetParameters();
            for (var k = 0; k < ps.Length && k < op.Args.Length; k++)
            {
                var p = ps[k];
                var v = op.Args[k];
                if (p.HasDefaultValue && Equals(v, p.DefaultValue))
                    continue; // leave defaults out to keep files short
                if (v is Map.Hole[] { Length: 0 })
                    continue;
                parts.Add($"{Str(p.Name!)}: {Fmt(v)}");
            }
            if (op.Anchor != OpAnchor.Auto)
                parts.Add($"\"anchor\": {Str(op.Anchor.ToString().ToLowerInvariant())}");
            sb.Append("    { ").Append(string.Join(", ", parts)).Append(" }").Append(i < m.Ops.Count - 1 ? ",\n" : "\n");
        }
        sb.Append("  ]");
        if (m.FoliageSection is { } foliage)
            sb.Append(",\n  \"foliage\": ").Append(FoliageToJson(foliage));
        if (m.Movers.Count > 0)
            sb.Append(",\n  \"movers\": ").Append(MoversToJson(m.Movers));
        sb.Append("\n}\n");
        return sb.ToString();
    }

    private static string Str(string s) => JsonSerializer.Serialize(s);

    private static string Num(float f) => f.ToString("R", CultureInfo.InvariantCulture);

    private static string Col(Vector3 c) => $"[{Num(c.X)}, {Num(c.Y)}, {Num(c.Z)}]";

    private static string Fmt(object? v) => v switch
    {
        null => "null",
        float f => Num(f),
        int i => i.ToString(CultureInfo.InvariantCulture),
        bool b => b ? "true" : "false",
        string s => Str(s),
        string[] list => "[" + string.Join(", ", list.Select(Str)) + "]",
        Vector3 c => Col(c),
        Map.Hole[] holes => "[" + string.Join(", ", holes.Select(h => $"{{ \"center\": {Num(h.Center)}, \"width\": {Num(h.Width)}, \"bottom\": {Num(h.Bottom)}, \"top\": {Num(h.Top)} }}")) + "]",
        ValueTuple<float, float, float, float> r => $"{{ \"x0\": {Num(r.Item1)}, \"z0\": {Num(r.Item2)}, \"x1\": {Num(r.Item3)}, \"z1\": {Num(r.Item4)} }}",
        _ => throw new MapFileException($"can't write a {v.GetType().Name}"),
    };

    // ---- Reading --------------------------------------------------------------------------

    /// <summary>Builds a map from a map file. Throws <see cref="MapFileException"/> with a readable message if the file is wrong.</summary>
    public static Map Load(string json, string source)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        }
        catch (JsonException ex)
        {
            throw new MapFileException($"{source}: not valid JSON ({ex.Message})");
        }
        using (doc)
        {
            var root = doc.RootElement;
            string ReqStr(string name) => Prop(root, name) is { ValueKind: JsonValueKind.String } e ? e.GetString()! : throw new MapFileException($"{source}: missing \"{name}\"");
            float ReqNum(string name) => Prop(root, name) is { ValueKind: JsonValueKind.Number } e ? e.GetSingle() : throw new MapFileException($"{source}: missing number \"{name}\"");

            var map = new Map
            {
                Name = ReqStr("name"),
                Description = Prop(root, "description") is { ValueKind: JsonValueKind.String } d ? d.GetString()! : "",
                HalfX = Math.Clamp(ReqNum("halfX"), 5f, 500f),
                HalfZ = Math.Clamp(ReqNum("halfZ"), 5f, 500f),
                SourceFile = source,
            };
            if (Prop(root, "author") is { ValueKind: JsonValueKind.String } a)
                map.Author = a.GetString()!;
            if (Prop(root, "order") is { ValueKind: JsonValueKind.Number } o)
                map.Order = o.GetInt32();
            if (Prop(root, "maxPlayers") is { ValueKind: JsonValueKind.Number } mp)
                map.MaxPlayersSetting = mp.TryGetInt32(out var mpi) && mpi is >= 2 and <= Map.HardMaxPlayers
                    ? mpi : throw new MapFileException($"{source}: \"maxPlayers\" is a whole number from 2 to {Map.HardMaxPlayers}");
            if (Prop(root, "prototypeGrid") is { ValueKind: JsonValueKind.True })
                map.PrototypeGrid = true;
            if (Prop(root, "sky") is { } sky)
                map.Sky = ToColour(sky, "sky", source);
            if (Prop(root, "skybox") is { } skybox && SkyFile.Parse(skybox, source) is { } parsedSky)
            {
                map.Skybox = parsedSky;
                map.Sky = parsedSky.Horizon; // background, fog and UI colour follow the sky's horizon (Source/Render/Sky)
            }
            if (Prop(root, "terrain") is { ValueKind: not JsonValueKind.Null } terrain)
                map.SetTerrain(TerrainSpec.Parse(terrain, source)); // before the ops: they're placed on the ground as they run
            if (Prop(root, "ops") is not { ValueKind: JsonValueKind.Array } ops)
                throw new MapFileException($"{source}: missing \"ops\" list");

            var index = 0;
            foreach (var op in ops.EnumerateArray())
            {
                index++;
                var where = $"{source}, op #{index}";
                if (Prop(op, "op") is not { ValueKind: JsonValueKind.String } nameEl || !Builders.TryGetValue(nameEl.GetString()!, out var method))
                    throw new MapFileException($"{where}: unknown \"op\" {(Prop(op, "op") is { } n ? n.ToString() : "(missing)")}. Known: {string.Join(", ", Builders.Keys.OrderBy(k => k))}");
                if (method.Name == nameof(Map.Prop) && Prop(op, "name") != null)
                    method = Builders[nameof(Map.PlaceProp)]; // { "op": "Prop", "name": "Trash Can", ... } is a prop from the Props folder
                var ps = method.GetParameters();
                var args = new object?[ps.Length];
                for (var k = 0; k < ps.Length; k++)
                {
                    var p = ps[k];
                    if (Prop(op, p.Name!) is { } value)
                        args[k] = Convert(value, p.ParameterType, $"{where} ({method.Name}.{p.Name})");
                    else if (p.HasDefaultValue)
                        args[k] = p.DefaultValue;
                    else if (p.ParameterType == typeof(Map.Hole[]))
                        args[k] = Array.Empty<Map.Hole>();
                    else
                        throw new MapFileException($"{where}: {method.Name} needs \"{p.Name}\"");
                }
                map.NextAnchor = Prop(op, "anchor") switch
                {
                    null => OpAnchor.Auto,
                    { ValueKind: JsonValueKind.String } an when Enum.TryParse<OpAnchor>(an.GetString(), true, out var parsed) && Enum.IsDefined(parsed) => parsed,
                    var an => throw new MapFileException($"{where}: \"anchor\" is \"ground\", \"drape\", \"world\" or \"auto\", got {an}"),
                };
                foreach (var prop in op.EnumerateObject())
                    if (prop.Name != "op" && !prop.Name.Equals("anchor", StringComparison.OrdinalIgnoreCase) && !ps.Any(p => string.Equals(p.Name, prop.Name, StringComparison.OrdinalIgnoreCase)))
                        throw new MapFileException($"{where}: {method.Name} has no parameter \"{prop.Name}\" (it takes: {string.Join(", ", ps.Select(p => p.Name))})");
                try
                {
                    method.Invoke(map, args);
                }
                catch (TargetInvocationException ex)
                {
                    throw new MapFileException($"{where}: {method.Name} failed: {ex.InnerException?.Message}");
                }
            }
            if (Prop(root, "foliage") is { ValueKind: not JsonValueKind.Null } foliage)
            {
                map.FoliageSection = ReadFoliage(foliage, source);
                map.PlantFoliage(); // after the ops, so plants can keep out of what they built
            }
            if (map.Spawns.Count == 0)
                throw new MapFileException($"{source}: no spawn points (add Spawn or RingSpawns ops)");
            if (Prop(root, "movers") is { ValueKind: not JsonValueKind.Null } movers)
            {
                map.Movers.AddRange(ReadMovers(movers, source));
                CheckMoverSweeps(map, source); // before the objectives: automatic hills are picked from the waypoints
            }
            MapObjectiveJson.Read(root, map, source);
            map.FinishProps(); // last: Clutter keeps clear of the spawns, waypoints, objectives and movers above
            return map;
        }
    }

    private static JsonElement? Prop(JsonElement obj, string name)
    {
        if (obj.ValueKind != JsonValueKind.Object)
            return null;
        foreach (var p in obj.EnumerateObject())
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                return p.Value;
        return null;
    }

    private static float Num(JsonElement e, string where) =>
        e.ValueKind == JsonValueKind.Number ? e.GetSingle() : throw new MapFileException($"{where}: expected a number, got {e}");

    private static object? Convert(JsonElement e, Type t, string where)
    {
        if (t == typeof(float))
            return Num(e, where);
        if (t == typeof(int))
            return e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out var i) ? i : throw new MapFileException($"{where}: expected a whole number, got {e}");
        if (t == typeof(bool))
            return e.ValueKind is JsonValueKind.True or JsonValueKind.False ? e.GetBoolean() : throw new MapFileException($"{where}: expected true or false, got {e}");
        if (t == typeof(string))
            return e.ValueKind == JsonValueKind.String ? e.GetString() : throw new MapFileException($"{where}: expected text, got {e}");
        if (t == typeof(string[]))
            return e.ValueKind == JsonValueKind.Array ? e.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? x.GetString()! : throw new MapFileException($"{where}: expected a list of names, got {x}")).ToArray()
                : e.ValueKind == JsonValueKind.String ? new[] { e.GetString()! } : throw new MapFileException($"{where}: expected a list of names, got {e}");
        if (t == typeof(Vector3))
            return ToColour(e, where, "");
        if (t == typeof(Map.Hole[]))
        {
            if (e.ValueKind != JsonValueKind.Array)
                throw new MapFileException($"{where}: expected a list of holes");
            return e.EnumerateArray().Select(h => h.ValueKind == JsonValueKind.Array
                ? new Map.Hole(Num(h[0], where), Num(h[1], where), Num(h[2], where), Num(h[3], where))
                : new Map.Hole(Need(h, "center", where), Need(h, "width", where), Need(h, "bottom", where), Need(h, "top", where))).ToArray();
        }
        if (Nullable.GetUnderlyingType(t) == typeof(ValueTuple<float, float, float, float>))
        {
            if (e.ValueKind == JsonValueKind.Null)
                return null;
            return e.ValueKind == JsonValueKind.Array
                ? (Num(e[0], where), Num(e[1], where), Num(e[2], where), Num(e[3], where))
                : (Need(e, "x0", where), Need(e, "z0", where), Need(e, "x1", where), Need(e, "z1", where));
        }
        throw new MapFileException($"{where}: unsupported parameter type {t.Name}");
    }

    private static float Need(JsonElement obj, string name, string where) =>
        Prop(obj, name) is { } v ? Num(v, where) : throw new MapFileException($"{where}: missing \"{name}\"");

    /// <summary>Colours are [r, g, b] (0-1) or "#rrggbb".</summary>
    internal static Vector3 ToColour(JsonElement e, string where, string source)
    {
        if (e.ValueKind == JsonValueKind.Array && e.GetArrayLength() == 3)
            return new Vector3(Num(e[0], where), Num(e[1], where), Num(e[2], where));
        if (e.ValueKind == JsonValueKind.String && e.GetString() is { Length: 7 } hex && hex[0] == '#'
            && int.TryParse(hex[1..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
            return new Vector3(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f);
        throw new MapFileException($"{source}{where}: colours are [r, g, b] with values 0-1, or \"#rrggbb\"");
    }
}
