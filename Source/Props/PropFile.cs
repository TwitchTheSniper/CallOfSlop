using System.Globalization;
using System.Numerics;
using System.Text.Json;

namespace CallOfSlop;

public sealed class PropFileException(string message) : Exception(message);

/// <summary>
/// Prop files: one JSON object per prop in the Props folder (see Props/README.md). Every key is checked: an unknown key,
/// a number out of range or a missing part is an error with a readable message, so typos never fail silently.
/// </summary>
public static class PropFile
{
    public const string Format = "call-of-slop-prop/1";

    public const int MaxParts = 128;
    public const int MaxColliders = 8;

    /// <summary>The largest a prop may be in any direction (metres, before an instance's scale). 24 m fits a parked transport plane (Props/Military/Vehicles).</summary>
    public const float MaxSize = 24f;

    /// <summary>How much of a <c>"glass": true</c> part is drawn (<see cref="PropPart.Glass"/>).</summary>
    public const float DefaultGlass = 0.4f;

    /// <summary>Named colours a part can use instead of [r, g, b] or "#rrggbb".</summary>
    public static readonly Dictionary<string, Vector3> Colors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["black"] = new(0.12f, 0.12f, 0.13f),
        ["rubber"] = new(0.1f, 0.1f, 0.11f),
        ["darkgrey"] = new(0.26f, 0.27f, 0.28f),
        ["grey"] = new(0.46f, 0.47f, 0.48f),
        ["lightgrey"] = new(0.68f, 0.68f, 0.68f),
        ["white"] = new(0.88f, 0.88f, 0.86f),
        ["paper"] = new(0.84f, 0.82f, 0.76f),
        ["newsprint"] = new(0.74f, 0.73f, 0.69f),
        ["concrete"] = new(0.6f, 0.59f, 0.56f),
        ["steel"] = new(0.55f, 0.57f, 0.6f),
        ["galvanized"] = new(0.64f, 0.66f, 0.66f),
        ["chrome"] = new(0.78f, 0.8f, 0.83f),
        ["rust"] = new(0.5f, 0.28f, 0.16f),
        ["brick"] = new(0.62f, 0.3f, 0.22f),
        ["wood"] = new(0.58f, 0.4f, 0.24f),
        ["darkwood"] = new(0.36f, 0.23f, 0.13f),
        ["lightwood"] = new(0.74f, 0.58f, 0.38f),
        ["pallet"] = new(0.66f, 0.54f, 0.36f),
        ["cardboard"] = new(0.66f, 0.5f, 0.32f),
        ["tape"] = new(0.78f, 0.66f, 0.44f),
        ["red"] = new(0.72f, 0.14f, 0.12f),
        ["orange"] = new(0.94f, 0.45f, 0.1f),
        ["yellow"] = new(0.92f, 0.76f, 0.16f),
        ["green"] = new(0.22f, 0.5f, 0.24f),
        ["darkgreen"] = new(0.15f, 0.3f, 0.17f),
        ["olive"] = new(0.36f, 0.38f, 0.24f),
        ["blue"] = new(0.2f, 0.36f, 0.68f),
        ["navy"] = new(0.13f, 0.18f, 0.34f),
        ["tan"] = new(0.68f, 0.6f, 0.44f),
        ["sand"] = new(0.72f, 0.64f, 0.46f),
        ["brown"] = new(0.4f, 0.28f, 0.18f),
        ["glass"] = new(0.3f, 0.5f, 0.36f),
        ["amber"] = new(0.55f, 0.32f, 0.1f),
    };

    /// <summary>Material names a part can use (the procedural surface textures), or "none" for a plain colour.</summary>
    public static readonly Dictionary<string, int> Materials = new(StringComparer.OrdinalIgnoreCase)
    {
        ["none"] = -1,
        ["grid"] = (int)Material.Grid,
        ["concrete"] = (int)Material.Concrete,
        ["brick"] = (int)Material.Brick,
        ["wood"] = (int)Material.Wood,
        ["metal"] = (int)Material.Metal,
        ["tiles"] = (int)Material.Tiles,
        ["dirt"] = (int)Material.Dirt,
    };

    /// <summary>Does a prop file compose other props (a part with "use")? A cheap look before the real load, for the two-pass library loader.</summary>
    public static bool UsesPrefabs(string json) => json.Contains("\"use\"", StringComparison.OrdinalIgnoreCase);

    /// <summary>Builds a prop from a prop file. Throws <see cref="PropFileException"/> with a readable message if the file is wrong.</summary>
    /// <param name="folderCategory">The category subfolder the file is in (used when the file gives no "category").</param>
    /// <param name="library">
    /// Finds a prop by name for parts that "use" another prop (a prefab, see Props/README.md); it throws
    /// <see cref="PropFileException"/> for a cycle and returns null for an unknown name. Without one, a prefab is refused.
    /// </param>
    public static PropDef Load(string json, string source, string folderCategory = "Misc", Func<string, PropDef?>? library = null)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        }
        catch (JsonException ex)
        {
            throw new PropFileException($"{source}: not valid JSON ({ex.Message})");
        }
        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new PropFileException($"{source}: a prop file is one JSON object {{ \"name\": ..., \"parts\": [...] }}");
            string? name = null;
            var def = new PropDef { Name = "", Category = folderCategory, SourceFile = source };
            JsonElement? parts = null, collider = null, scatter = null, breakable = null, flag = null, lights = null;
            float? fade = null;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in root.EnumerateObject())
            {
                var where = $"{source} (\"{p.Name}\")";
                if (!seen.Add(p.Name))
                    throw new PropFileException($"{where}: given twice");
                switch (p.Name.ToLowerInvariant())
                {
                    case "format":
                        var f = Text(p.Value, where);
                        if (!f.StartsWith("call-of-slop-prop/", StringComparison.Ordinal))
                            throw new PropFileException($"{where}: expected \"{Format}\", got \"{f}\"");
                        if (f != Format)
                            throw new PropFileException($"{where}: this game reads \"{Format}\" files; \"{f}\" is from a different version");
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
                    case "collision":
                        def.Collision = Text(p.Value, where).ToLowerInvariant() switch
                        {
                            "solid" => PropCollision.Solid,
                            "cosmetic" or "none" => PropCollision.Cosmetic,
                            var other => throw new PropFileException($"{where}: \"collision\" is \"solid\" or \"cosmetic\", got \"{other}\""),
                        };
                        break;
                    case "collider":
                        collider = p.Value;
                        break;
                    case "parts":
                        parts = p.Value;
                        break;
                    case "scatter":
                        scatter = p.Value;
                        break;
                    case "fadedistance":
                        fade = Range(Num(p.Value, where), 5, 400, where);
                        break;
                    case "breakable":
                        breakable = p.Value;
                        break;
                    case "flag":
                        flag = p.Value;
                        break;
                    case "concealment":
                        def.Concealment = Bool(p.Value, where);
                        break;
                    case "lights":
                        lights = p.Value;
                        break;
                    default:
                        throw new PropFileException($"{where}: unknown key. Prop files use: format, name, category, description, author, collision, collider, parts, scatter, fadeDistance, breakable, flag, concealment, lights");
                }
            }
            if (string.IsNullOrWhiteSpace(name))
                throw new PropFileException($"{source}: missing \"name\"");
            if (name.Length > 40)
                throw new PropFileException($"{source}: \"name\" is at most 40 characters");
            if (!IsPlain(name) || !IsPlain(def.Category) || !IsPlain(def.Description))
                throw new PropFileException($"{source}: name, category and description must be plain ASCII (the game's font has no curly quotes, dashes or accents)");
            def.Name = name;
            if (def.Category.Length == 0)
                def.Category = folderCategory;

            if (parts is not { ValueKind: JsonValueKind.Array } pl || pl.GetArrayLength() == 0)
                throw new PropFileException($"{source}: needs \"parts\": a list of boxes, e.g. [ {{ \"size\": [0.6, 1, 0.6], \"color\": \"grey\" }} ]");
            if (pl.GetArrayLength() > MaxParts)
                throw new PropFileException($"{source}: {pl.GetArrayLength()} parts is more than the limit of {MaxParts}");
            var index = 0;
            var prefabColliders = new List<Box>();
            var propLights = new List<MapLight>();
            foreach (var pe in pl.EnumerateArray())
            {
                var where = $"{source}, part #{++index}";
                if (pe.ValueKind == JsonValueKind.Object && pe.EnumerateObject().Any(q => q.Name.Equals("use", StringComparison.OrdinalIgnoreCase)))
                    ReadUse(pe, def, prefabColliders, propLights, where, library);
                else
                    def.Parts.Add(ReadPart(pe, where));
            }
            if (def.Parts.Count > MaxParts)
                throw new PropFileException($"{source}: the composed model has {def.Parts.Count} parts, more than the limit of {MaxParts}");

            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            foreach (var part in def.Parts)
            {
                var b = part.Bounds;
                min = Vector3.Min(min, b.Min);
                max = Vector3.Max(max, b.Max);
            }
            def.Bounds = new Box((min + max) / 2, (max - min) / 2);
            var size = max - min;
            if (size.X > MaxSize || size.Y > MaxSize || size.Z > MaxSize)
                throw new PropFileException($"{source}: the model is {size.X:0.##} x {size.Y:0.##} x {size.Z:0.##} m; props are at most {MaxSize} m in any direction");
            if (min.Y < -0.5f)
                throw new PropFileException($"{source}: parts reach {-min.Y:0.##} m below the ground (y = 0 is the ground; at most 0.5 m may sink into it)");
            if (max.Y <= 0.002f)
                throw new PropFileException($"{source}: the whole model is below the ground (y = 0)");

            PropAnimFile.Validate(def, source);
            if (flag is { ValueKind: not JsonValueKind.Null } fl)
            {
                def.FlagCloth = FlagPool.ReadCloth(fl, $"{source} (\"flag\")");
                FlagPool.Validate(def, source); // flag designs replace the cloth (FlagPool.cs)
            }
            def.Colliders = def.IsSolid ? collider == null && prefabColliders.Count > 0 ? PrefabColliders(prefabColliders, source) : ReadColliders(collider, def, source) : [];
            if (!def.IsSolid && collider != null)
                throw new PropFileException($"{source}: \"collider\" is only for solid props (this one is cosmetic)");
            def.FadeDistance = def.IsSolid ? 0 : fade ?? PropDef.DefaultFade(def.Bounds);
            if (def.IsSolid && def.Concealment)
                throw new PropFileException($"{source}: \"concealment\" is only for cosmetic props (solid ones already block sight)");
            if (def.IsSolid && fade != null)
                throw new PropFileException($"{source}: \"fadeDistance\" is only for cosmetic props (solid ones are always drawn)");
            if (scatter is { } sc)
                ReadScatter(sc, def, $"{source} (\"scatter\")");
            if (breakable is { ValueKind: not JsonValueKind.Null } br)
                def.Breakable = ReadBreakable(br, $"{source} (\"breakable\")");
            if (lights is { ValueKind: not JsonValueKind.Null } li)
                ReadLights(li, propLights, $"{source} (\"lights\")");
            if (propLights.Count > MaxLights)
                throw new PropFileException($"{source}: {propLights.Count} lights is more than the limit of {MaxLights}");
            def.Lights = propLights.ToArray();
            return def;
        }
    }

    /// <summary>
    /// A part that places another prop: { "use": "Landing Beacon", "pos": [x, y, z], "rotation": [pitch, yaw, roll], "scale": s }.
    /// The used prop's parts are copied in, moved, turned and scaled, so the loaded prop is flat (render and physics never see
    /// the composition); its colliders are collected for the prefab's default collider.
    /// </summary>
    private static void ReadUse(JsonElement e, PropDef def, List<Box> prefabColliders, List<MapLight> lights, string where, Func<string, PropDef?>? library)
    {
        string? use = null;
        var pos = Vector3.Zero;
        var rot = Vector3.Zero;
        var scale = 1f;
        foreach (var p in e.EnumerateObject())
        {
            var w = $"{where} (\"{p.Name}\")";
            switch (p.Name.ToLowerInvariant())
            {
                case "use":
                    use = Text(p.Value, w).Trim();
                    break;
                case "pos":
                case "offset":
                    pos = Vec(p.Value, w);
                    break;
                case "rotation":
                    rot = Vec(p.Value, w);
                    break;
                case "yaw":
                    rot.Y = Num(p.Value, w);
                    break;
                case "scale":
                    scale = Range(Num(p.Value, w), 0.1f, 4f, w);
                    break;
                default:
                    throw new PropFileException($"{w}: unknown key. A part that uses another prop takes: use, pos, rotation (or yaw), scale");
            }
        }
        if (string.IsNullOrEmpty(use))
            throw new PropFileException($"{where}: \"use\" needs a prop name");
        if (library == null)
            throw new PropFileException($"{where}: uses \"{use}\", but this prop was loaded on its own; prefabs load through the Props folder");
        var used = library(use) ?? throw new PropFileException($"{where}: uses unknown prop \"{use}\"");
        if (MathF.Abs(rot.X) > 360 || MathF.Abs(rot.Y) > 360 || MathF.Abs(rot.Z) > 360)
            throw new PropFileException($"{where}: rotation angles are degrees, -360 to 360");
        var tilted = rot.X != 0 || rot.Z != 0;
        var q = Quaternion.CreateFromYawPitchRoll(rot.Y * MathF.PI / 180f, rot.X * MathF.PI / 180f, rot.Z * MathF.PI / 180f);
        foreach (var part in used.Parts)
        {
            if (tilted && part.RotationDeg != Vector3.Zero)
                throw new PropFileException($"{where}: \"{use}\" has turned parts, so it can only be used with a yaw (pitch and roll would need a combined rotation)");
            var copy = new PropPart
            {
                Size = part.Size * scale,
                Offset = pos + Vector3.Transform(part.Offset * scale, q),
                RotationDeg = tilted ? rot : part.RotationDeg + new Vector3(0, rot.Y, 0),
                Color = part.Color,
                Layer = part.Layer,
                TexScale = part.TexScale,
                Glass = part.Glass,
                Glow = part.Glow,
                Anim = part.Anim,
                Corners = part.Corners?.Select(c => pos + Vector3.Transform(c * scale, q)).ToArray(),
            };
            def.Parts.Add(copy);
        }
        foreach (var l in used.Lights) // the used prop's lights come along, moved, turned and scaled with it
        {
            var dir = l.Dir == Vector3.Zero ? l.Dir : Vector3.Normalize(Vector3.Transform(l.Dir, q));
            lights.Add(l with { Pos = pos + Vector3.Transform(l.Pos * scale, q), Dir = dir, Range = MathF.Min(l.Range * scale, MapLight.MaxRange), Size = l.Size * scale });
        }
        if (used.IsSolid)
            foreach (var c in used.Colliders)
            {
                // The collider turned with the use: the axis-aligned box round its turned corners.
                var min = new Vector3(float.MaxValue);
                var max = new Vector3(float.MinValue);
                for (var i = 0; i < 8; i++)
                {
                    var corner = new Vector3((i & 1) == 0 ? -c.Half.X : c.Half.X, (i & 2) == 0 ? -c.Half.Y : c.Half.Y, (i & 4) == 0 ? -c.Half.Z : c.Half.Z);
                    var w = pos + Vector3.Transform((c.Center + corner) * scale, q);
                    min = Vector3.Min(min, w);
                    max = Vector3.Max(max, w);
                }
                prefabColliders.Add(new Box((min + max) / 2, (max - min) / 2));
            }
    }

    /// <summary>The most lights one prop carries.</summary>
    public const int MaxLights = 16;

    /// <summary>
    /// The "lights" list: [ { "pos": [x, y, z], "color": "amber", "intensity": 1, "range": 6, "flicker": true,
    /// "type": "point" | "spot" | "area", "yaw", "pitch", "cone", "w", "d" } ], in the prop's frame (Props/README.md "Lights").
    /// </summary>
    private static void ReadLights(JsonElement e, List<MapLight> into, string where)
    {
        if (e.ValueKind != JsonValueKind.Array)
            throw new PropFileException($"{where}: expected a list of lights [ {{ \"pos\": [x, y, z], \"color\": ..., \"range\": 6 }} ]");
        var n = 0;
        foreach (var le in e.EnumerateArray())
        {
            var w = $"{where} #{++n}";
            if (le.ValueKind != JsonValueKind.Object)
                throw new PropFileException($"{w}: a light is an object {{ \"pos\": [x, y, z], ... }}");
            Vector3? pos = null;
            var color = Vector3.One;
            float intensity = 1, range = 6, yaw = 0, pitch = 90, cone = 60, lw = 1, ld = 1;
            var type = "point";
            var flicker = false;
            foreach (var p in le.EnumerateObject())
            {
                var pw = $"{w} (\"{p.Name}\")";
                switch (p.Name.ToLowerInvariant())
                {
                    case "pos":
                    case "offset":
                        pos = Vec(p.Value, pw);
                        break;
                    case "color":
                    case "colour":
                        color = Color(p.Value, pw);
                        break;
                    case "intensity":
                        intensity = Num(p.Value, pw);
                        break;
                    case "range":
                        range = Num(p.Value, pw);
                        break;
                    case "type":
                        type = Text(p.Value, pw);
                        break;
                    case "yaw":
                        yaw = Num(p.Value, pw);
                        break;
                    case "pitch":
                        pitch = Num(p.Value, pw);
                        break;
                    case "cone":
                        cone = Num(p.Value, pw);
                        break;
                    case "w":
                        lw = Num(p.Value, pw);
                        break;
                    case "d":
                        ld = Num(p.Value, pw);
                        break;
                    case "flicker":
                        flicker = Bool(p.Value, pw);
                        break;
                    default:
                        throw new PropFileException($"{pw}: unknown key. A light takes: pos, color, intensity, range, type, yaw, pitch, cone, w, d, flicker");
                }
            }
            if (pos is not { } at)
                throw new PropFileException($"{w}: needs \"pos\": [x, y, z] (in the prop's frame)");
            try
            {
                into.Add(MapLight.Make(at, color, intensity, range, type, yaw, pitch, cone, lw, ld, flicker));
            }
            catch (InvalidOperationException ex)
            {
                throw new PropFileException($"{w}: {ex.Message}");
            }
        }
    }

    private static Box[] PrefabColliders(List<Box> boxes, string source)
    {
        if (boxes.Count > MaxColliders)
            throw new PropFileException($"{source}: the used props have {boxes.Count} collider boxes between them; at most {MaxColliders}. Give the prefab its own \"collider\" (\"bounds\" or a few boxes)");
        return boxes.Select(b =>
        {
            var min = b.Min;
            var max = b.Max;
            min.Y = MathF.Max(min.Y, 0f);
            if (max.Y - min.Y < 0.02f)
                max.Y = min.Y + 0.02f;
            return new Box((min + max) / 2, (max - min) / 2);
        }).ToArray();
    }

    private static PropPart ReadPart(JsonElement e, string where)
    {
        if (e.ValueKind != JsonValueKind.Object)
            throw new PropFileException($"{where}: a part is an object like {{ \"size\": [w, h, d], \"offset\": [x, y, z], \"color\": \"grey\" }}");
        var part = new PropPart { Color = Colors["grey"] };
        Vector3? offset = null;
        var hasSize = false;
        foreach (var p in e.EnumerateObject())
        {
            var w = $"{where} (\"{p.Name}\")";
            switch (p.Name.ToLowerInvariant())
            {
                case "size":
                    part.Size = Vec(p.Value, w);
                    if (part.Size.X < 0.001f || part.Size.Y < 0.001f || part.Size.Z < 0.001f || part.Size.X > MaxSize || part.Size.Y > MaxSize || part.Size.Z > MaxSize)
                        throw new PropFileException($"{w}: each size is 0.001 to {MaxSize} m");
                    hasSize = true;
                    break;
                case "offset":
                    offset = Vec(p.Value, w);
                    break;
                case "rotation":
                    part.RotationDeg = Vec(p.Value, w);
                    break;
                case "yaw":
                    part.RotationDeg.Y = Num(p.Value, w);
                    break;
                case "pitch":
                    part.RotationDeg.X = Num(p.Value, w);
                    break;
                case "roll":
                    part.RotationDeg.Z = Num(p.Value, w);
                    break;
                case "color":
                case "colour":
                    part.Color = Color(p.Value, w);
                    break;
                case "material":
                    var m = Text(p.Value, w);
                    if (!Materials.TryGetValue(m, out part.Layer))
                        throw new PropFileException($"{w}: unknown material \"{m}\". Known: {string.Join(", ", Materials.Keys)}");
                    break;
                case "texscale":
                    part.TexScale = Range(Num(p.Value, w), 0.1f, 10f, w);
                    break;
                case "anim":
                    part.Anim = PropAnimFile.Read(p.Value, where);
                    break;
                case "glass":
                    part.Glass = p.Value.ValueKind switch
                    {
                        JsonValueKind.True => DefaultGlass,
                        JsonValueKind.False => 0f,
                        _ => Range(Num(p.Value, w), 0.1f, 0.9f, w),
                    };
                    break;
                case "glow":
                    part.Glow = MathF.Round(Range(Num(p.Value, w), 0f, 4f, w) * 10f) / 10f;
                    break;
                default:
                    throw new PropFileException($"{w}: unknown key. A part uses: size, offset, rotation (or yaw / pitch / roll), color, material, texScale, glass, glow, anim");
            }
        }
        if (!hasSize)
            throw new PropFileException($"{where}: needs \"size\": [w, h, d] in metres");
        if ((part.Glass > 0 || part.Glow > 0) && part.Layer >= 0)
            throw new PropFileException($"{where}: a \"glass\" or \"glow\" part is a plain colour; leave out \"material\"");
        part.Offset = offset ?? new Vector3(0, part.Size.Y / 2, 0);
        if (part.RotationDeg != Vector3.Zero && (MathF.Abs(part.RotationDeg.X) > 360 || MathF.Abs(part.RotationDeg.Y) > 360 || MathF.Abs(part.RotationDeg.Z) > 360))
            throw new PropFileException($"{where}: rotation angles are degrees, -360 to 360");
        return part;
    }

    private static Box[] ReadColliders(JsonElement? e, PropDef def, string source)
    {
        var where = $"{source} (\"collider\")";
        switch (e)
        {
            case null:
            case { ValueKind: JsonValueKind.String } when e.Value.GetString()!.Equals("bounds", StringComparison.OrdinalIgnoreCase):
                return [Clamp(def.Bounds)];
            case { ValueKind: JsonValueKind.String } when e.Value.GetString()!.Equals("parts", StringComparison.OrdinalIgnoreCase):
                if (def.Parts.Count > MaxColliders)
                    throw new PropFileException($"{where}: \"parts\" would make {def.Parts.Count} collider boxes; at most {MaxColliders}. List a few simple boxes instead");
                return def.Parts.Select(p => Clamp(p.Bounds)).ToArray();
            case { ValueKind: JsonValueKind.Array } list:
            {
                var n = list.GetArrayLength();
                if (n == 0 || n > MaxColliders)
                    throw new PropFileException($"{where}: 1 to {MaxColliders} boxes");
                var boxes = new List<Box>();
                var i = 0;
                foreach (var b in list.EnumerateArray())
                {
                    var w = $"{where} box #{++i}";
                    if (b.ValueKind != JsonValueKind.Object)
                        throw new PropFileException($"{w}: a collider box is {{ \"size\": [w, h, d], \"offset\": [x, y, z] }}");
                    Vector3? size = null, offset = null;
                    foreach (var p in b.EnumerateObject())
                    {
                        if (p.Name.Equals("size", StringComparison.OrdinalIgnoreCase))
                            size = Vec(p.Value, w);
                        else if (p.Name.Equals("offset", StringComparison.OrdinalIgnoreCase))
                            offset = Vec(p.Value, w);
                        else
                            throw new PropFileException($"{w}: unknown key \"{p.Name}\" (a collider box has size and offset)");
                    }
                    if (size is not { } s || s.X < 0.02f || s.Y < 0.02f || s.Z < 0.02f || s.X > MaxSize || s.Y > MaxSize || s.Z > MaxSize)
                        throw new PropFileException($"{w}: needs \"size\" [w, h, d], each 0.02 to {MaxSize} m");
                    boxes.Add(Clamp(new Box(offset ?? new Vector3(0, s.Y / 2, 0), s / 2)));
                }
                return boxes.ToArray();
            }
            default:
                throw new PropFileException($"{where}: \"bounds\" (one box around the whole model, the default), \"parts\" (a box per part) or a list of boxes");
        }

        // A collider never reaches below the ground: the bit underground would only snag ray casts from below.
        static Box Clamp(Box b)
        {
            var min = b.Min;
            var max = b.Max;
            min.Y = MathF.Max(min.Y, 0f);
            if (max.Y - min.Y < 0.02f)
                max.Y = min.Y + 0.02f;
            return new Box((min + max) / 2, (max - min) / 2);
        }
    }

    private static void ReadScatter(JsonElement e, PropDef def, string where)
    {
        if (e.ValueKind != JsonValueKind.Object)
            throw new PropFileException($"{where}: an object like {{ \"randomYaw\": true, \"scale\": [0.9, 1.1] }}");
        foreach (var p in e.EnumerateObject())
        {
            var w = $"{where} (\"{p.Name}\")";
            switch (p.Name.ToLowerInvariant())
            {
                case "randomyaw":
                    def.RandomYaw = Bool(p.Value, w);
                    break;
                case "yawstep":
                    def.YawStep = Range(Num(p.Value, w), 0, 180, w);
                    break;
                case "yawjitter":
                    def.YawJitter = Range(Num(p.Value, w), 0, 180, w);
                    break;
                case "scale":
                    if (p.Value.ValueKind == JsonValueKind.Number)
                        def.ScaleMin = def.ScaleMax = Range(Num(p.Value, w), 0.25f, 4f, w);
                    else if (p.Value.ValueKind == JsonValueKind.Array && p.Value.GetArrayLength() == 2)
                    {
                        def.ScaleMin = Range(Num(p.Value[0], w), 0.25f, 4f, w);
                        def.ScaleMax = Range(Num(p.Value[1], w), 0.25f, 4f, w);
                        if (def.ScaleMax < def.ScaleMin)
                            throw new PropFileException($"{w}: [min, max] with min <= max");
                    }
                    else
                        throw new PropFileException($"{w}: a number or [min, max]");
                    break;
                case "groundsnap":
                    def.GroundSnap = Bool(p.Value, w);
                    break;
                case "weight":
                    def.Weight = Range(Num(p.Value, w), 0, 100, w);
                    break;
                default:
                    throw new PropFileException($"{w}: unknown key. \"scatter\" uses: randomYaw, yawStep, yawJitter, scale, groundSnap, weight");
            }
        }
    }

    private static PropBreakable ReadBreakable(JsonElement e, string where)
    {
        if (e.ValueKind != JsonValueKind.Object)
            throw new PropFileException($"{where}: an object like {{ \"health\": 40, \"breakBy\": \"all\" }}");
        var b = new PropBreakable();
        foreach (var p in e.EnumerateObject())
        {
            var w = $"{where} (\"{p.Name}\")";
            switch (p.Name.ToLowerInvariant())
            {
                case "health":
                    b.Health = (int)Range(Num(p.Value, w), 1, 10000, w);
                    break;
                case "breakby":
                    b.BreakBy = p.Value.ValueKind == JsonValueKind.String ? Damage(p.Value.GetString()!, w)
                        : p.Value.ValueKind == JsonValueKind.Array ? p.Value.EnumerateArray().Aggregate(PropDamage.None, (acc, x) => acc | Damage(Text(x, w), w))
                        : throw new PropFileException($"{w}: \"all\" or a list of \"bullets\", \"melee\", \"explosions\", \"streaks\"");
                    if (b.BreakBy == PropDamage.None)
                        throw new PropFileException($"{w}: give at least one kind of damage");
                    break;
                case "sound":
                    var s = Text(p.Value, w);
                    if (!Enum.TryParse(s, true, out b.Sound) || !Enum.IsDefined(b.Sound))
                        throw new PropFileException($"{w}: \"wood\", \"glass\", \"metal\", \"soft\" or \"meat\", got \"{s}\"");
                    break;
                case "debris":
                    ReadDebris(p.Value, b, w);
                    break;
                case "explode":
                    if (p.Value.ValueKind != JsonValueKind.Object)
                        throw new PropFileException($"{w}: an object like {{ \"radius\": 5, \"damage\": 100 }}");
                    foreach (var q in p.Value.EnumerateObject())
                    {
                        var wq = $"{w} (\"{q.Name}\")";
                        if (q.Name.Equals("radius", StringComparison.OrdinalIgnoreCase))
                            b.ExplodeRadius = Range(Num(q.Value, wq), 1, 15, wq);
                        else if (q.Name.Equals("damage", StringComparison.OrdinalIgnoreCase))
                            b.ExplodeDamage = Range(Num(q.Value, wq), 1, 300, wq);
                        else if (q.Name.Equals("fuse", StringComparison.OrdinalIgnoreCase))
                            b.ExplodeFuse = Range(Num(q.Value, wq), 0, 5, wq);
                        else if (q.Name.Equals("knockback", StringComparison.OrdinalIgnoreCase))
                            b.ExplodeKnockback = Range(Num(q.Value, wq), 0, 20, wq);
                        else if (q.Name.Equals("trigger", StringComparison.OrdinalIgnoreCase))
                            ReadTrigger(q.Value, b, wq);
                        else
                            throw new PropFileException($"{wq}: unknown key (\"explode\" has radius, damage, fuse, knockback and trigger)");
                    }
                    if (b.ExplodeRadius <= 0 || b.ExplodeDamage <= 0)
                        throw new PropFileException($"{w}: needs both \"radius\" and \"damage\"");
                    if (b.IsMine && b.ExplodeFuse <= 0)
                        b.ExplodeFuse = DefaultTriggerFuse; // a pressure plate always clicks first: the reaction window
                    break;
                case "gibs":
                    ReadGibs(p.Value, b, w);
                    break;
                default:
                    throw new PropFileException($"{w}: unknown key. \"breakable\" uses: health, breakBy, debris, sound, explode, gibs");
            }
        }
        if (b.GibCount > 0 && b.ExplodeRadius > 0)
            throw new PropFileException($"{where}: an animal (\"gibs\") can't also explode");
        if (b.GibCount > 0)
            b.Sound = PropBreakSound.Meat; // animals squelch
        return b;
    }

    /// <summary>The "gibs" block of an animal: <c>{ "count": 8, "style": "bird" }</c> (see <see cref="Gibs"/>).</summary>
    private static void ReadGibs(JsonElement e, PropBreakable b, string where)
    {
        if (e.ValueKind != JsonValueKind.Object)
            throw new PropFileException($"{where}: an object like {{ \"count\": 8, \"style\": \"bird\" }}");
        b.GibCount = 8;
        foreach (var p in e.EnumerateObject())
        {
            var w = $"{where} (\"{p.Name}\")";
            switch (p.Name.ToLowerInvariant())
            {
                case "count":
                    b.GibCount = (int)Range(Num(p.Value, w), 1, Gibs.MaxPerBurst, w);
                    break;
                case "style":
                    var s = Text(p.Value, w);
                    if (!Enum.TryParse(s, true, out b.GibStyle) || !Enum.IsDefined(b.GibStyle))
                        throw new PropFileException($"{w}: \"fur\", \"bird\" or \"wool\", got \"{s}\"");
                    break;
                default:
                    throw new PropFileException($"{w}: unknown key. \"gibs\" uses: count, style");
            }
        }
    }

    /// <summary>The click-to-blast delay (s) of a pressure plate whose "explode" block gives no "fuse".</summary>
    public const float DefaultTriggerFuse = 0.35f;

    /// <summary>The "trigger" block of "explode": a pressure plate, <c>{ "radius": 0.6, "height": 0.5 }</c>.</summary>
    private static void ReadTrigger(JsonElement e, PropBreakable b, string where)
    {
        if (e.ValueKind != JsonValueKind.Object)
            throw new PropFileException($"{where}: an object like {{ \"radius\": 0.6, \"height\": 0.5 }}");
        b.TriggerRadius = 0.6f;
        foreach (var p in e.EnumerateObject())
        {
            var w = $"{where} (\"{p.Name}\")";
            switch (p.Name.ToLowerInvariant())
            {
                case "radius":
                    b.TriggerRadius = Range(Num(p.Value, w), 0.1f, 3f, w);
                    break;
                case "height":
                    b.TriggerHeight = Range(Num(p.Value, w), 0.05f, 2f, w);
                    break;
                default:
                    throw new PropFileException($"{w}: unknown key (\"trigger\" has radius and height)");
            }
        }
    }

    private static void ReadDebris(JsonElement e, PropBreakable b, string where)
    {
        if (e.ValueKind != JsonValueKind.Object)
            throw new PropFileException($"{where}: an object like {{ \"count\": 8, \"size\": [0.05, 0.2] }}");
        foreach (var p in e.EnumerateObject())
        {
            var w = $"{where} (\"{p.Name}\")";
            switch (p.Name.ToLowerInvariant())
            {
                case "count":
                    b.DebrisCount = (int)Range(Num(p.Value, w), 0, 40, w);
                    break;
                case "size":
                    if (p.Value.ValueKind == JsonValueKind.Array && p.Value.GetArrayLength() == 2)
                    {
                        b.DebrisMin = Range(Num(p.Value[0], w), 0.01f, 1.5f, w);
                        b.DebrisMax = Range(Num(p.Value[1], w), 0.01f, 1.5f, w);
                        if (b.DebrisMax < b.DebrisMin)
                            throw new PropFileException($"{w}: [min, max] with min <= max");
                    }
                    else
                        b.DebrisMin = b.DebrisMax = Range(Num(p.Value, w), 0.01f, 1.5f, w);
                    break;
                case "speed":
                    b.DebrisSpeed = Range(Num(p.Value, w), 0, 20, w);
                    break;
                case "colors":
                case "colours":
                    if (p.Value.ValueKind != JsonValueKind.Array || p.Value.GetArrayLength() == 0)
                        throw new PropFileException($"{w}: a list of colours");
                    b.DebrisColors = p.Value.EnumerateArray().Select(c => Color(c, w)).ToArray();
                    break;
                default:
                    throw new PropFileException($"{w}: unknown key. \"debris\" uses: count, size, speed, colors");
            }
        }
    }

    private static PropDamage Damage(string s, string where) => s.ToLowerInvariant() switch
    {
        "all" => PropDamage.All,
        "bullet" or "bullets" => PropDamage.Bullet,
        "melee" => PropDamage.Melee,
        "explosion" or "explosions" => PropDamage.Explosion,
        "streak" or "streaks" => PropDamage.Streak,
        _ => throw new PropFileException($"{where}: unknown damage \"{s}\" (bullets, melee, explosions, streaks or all)"),
    };

    // ---- Value helpers ---------------------------------------------------------------------

    private static bool IsPlain(string s) => s.All(c => c >= 32 && c < 127);

    private static string Text(JsonElement e, string where) =>
        e.ValueKind == JsonValueKind.String ? e.GetString()! : throw new PropFileException($"{where}: expected text, got {e}");

    private static float Num(JsonElement e, string where) =>
        e.ValueKind == JsonValueKind.Number && float.IsFinite(e.GetSingle()) ? e.GetSingle() : throw new PropFileException($"{where}: expected a number, got {e}");

    private static bool Bool(JsonElement e, string where) =>
        e.ValueKind is JsonValueKind.True or JsonValueKind.False ? e.GetBoolean() : throw new PropFileException($"{where}: expected true or false, got {e}");

    private static float Range(float v, float lo, float hi, string where) =>
        v >= lo && v <= hi ? v : throw new PropFileException($"{where}: {v.ToString(CultureInfo.InvariantCulture)} is outside {lo.ToString(CultureInfo.InvariantCulture)} to {hi.ToString(CultureInfo.InvariantCulture)}");

    private static Vector3 Vec(JsonElement e, string where) =>
        e.ValueKind == JsonValueKind.Array && e.GetArrayLength() == 3
            ? new Vector3(Num(e[0], where), Num(e[1], where), Num(e[2], where))
            : throw new PropFileException($"{where}: expected [x, y, z], got {e}");

    /// <summary>A named colour, [r, g, b] (0-1) or "#rrggbb".</summary>
    public static Vector3 Color(JsonElement e, string where)
    {
        if (e.ValueKind == JsonValueKind.String && Colors.TryGetValue(e.GetString()!, out var named))
            return named;
        if (e.ValueKind == JsonValueKind.Array && e.GetArrayLength() == 3)
        {
            var c = Vec(e, where);
            if (c.X is < 0 or > 1 || c.Y is < 0 or > 1 || c.Z is < 0 or > 1)
                throw new PropFileException($"{where}: colour values are 0 to 1");
            return c;
        }
        if (e.ValueKind == JsonValueKind.String && e.GetString() is { Length: 7 } hex && hex[0] == '#'
            && int.TryParse(hex[1..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
            return new Vector3(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f);
        throw new PropFileException($"{where}: colours are a name ({string.Join(", ", Colors.Keys.Take(8))}...), [r, g, b] with values 0-1, or \"#rrggbb\"; got {e}");
    }
}
