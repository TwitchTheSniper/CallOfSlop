using System.Globalization;
using System.Numerics;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>The prestige hats. The byte value is what goes over the network and must never change for a shipped hat.</summary>
public enum HatId : byte { None = 0, Beanie = 1, BaseballCap = 2, Fedora = 3, TopHat = 4, TallTopHat = 5 }

/// <summary>One box of a hat model: a unit cube transformed into head space (y = 0 the top of the head, -Z the face).</summary>
public readonly record struct HatBox(Matrix4x4 Local, Vector3 Color);

/// <summary>A hat from Cosmetics/hats.json: its id, save key, display name, the prestige that unlocks it and its model.</summary>
public sealed class HatDef
{
    public HatId Id;
    /// <summary>The save / file key ("beanie", "tall_top_hat").</summary>
    public string Key = "";
    /// <summary>Display name ("Ridiculous Top Hat"), plain ASCII.</summary>
    public string Name = "";
    /// <summary>The prestige level that unlocks it (1..5).</summary>
    public int Prestige;
    /// <summary>The model: boxes in head space, built from the file's parts (a "round" part becomes several boxes).</summary>
    public IReadOnlyList<HatBox> Boxes = [];
    /// <summary>Bounds of the model in head space (metres, before scaling to the head).</summary>
    public Vector3 Min, Max;
    /// <summary>How much the hat sways with movement (0 = rigid; the ridiculous top hat wobbles).</summary>
    public float Wobble;

    /// <summary>Height of the model above the top of the head.</summary>
    public float Height => Max.Y;
}

/// <summary>
/// The cosmetics catalog (Cosmetics/hats.json, format <see cref="Format"/>). Hats are cosmetic only: no collision, no
/// hitbox change, nothing but the hat id on the network. A hat is unlocked by reaching its prestige
/// (<see cref="Progress.Prestige"/>, Progression.cs). If the file is missing or broken the built-in catalog is used
/// (same ids, names and prestige, plain block models) and the error is logged.
/// </summary>
public static class Cosmetics
{
    public const string Format = "call-of-slop-cosmetics/1";

    /// <summary>The highest prestige (one hat per prestige).</summary>
    public const int MaxPrestige = 5;

    /// <summary>Where the catalog is read from.</summary>
    public static string FilePath => Path.Combine(AppFolders.Data, "Cosmetics", "hats.json");

    private static IReadOnlyList<HatDef>? _hats;

    /// <summary>The error from loading hats.json, or null if it loaded (self-test).</summary>
    public static string? LoadError { get; private set; }

    /// <summary>All hats in prestige order, from Cosmetics/hats.json.</summary>
    public static IReadOnlyList<HatDef> Hats => _hats ??= LoadCatalog();

    /// <summary>The hat with this id, or null for <see cref="HatId.None"/> or an unknown id.</summary>
    public static HatDef? Get(HatId id)
    {
        if (id == HatId.None)
            return null;
        foreach (var h in Hats)
            if (h.Id == id)
                return h;
        return null;
    }

    /// <summary>True if a player with this prestige may wear the hat (None is always allowed; unknown ids never are).</summary>
    public static bool IsUnlocked(HatId id, int prestige) => id == HatId.None || (Get(id) is { } def && prestige >= def.Prestige);

    /// <summary>The hat with this save key ("beanie"), case-insensitive; None for "", "none" or an unknown key.</summary>
    public static HatId FromKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return HatId.None;
        foreach (var h in Hats)
            if (string.Equals(h.Key, key.Trim(), StringComparison.OrdinalIgnoreCase))
                return h.Id;
        return HatId.None;
    }

    /// <summary>The save key of a hat ("none" for None).</summary>
    public static string KeyOf(HatId id) => Get(id)?.Key ?? "none";

    /// <summary>The hat a prestige level unlocks, or None.</summary>
    public static HatId HatForPrestige(int prestige)
    {
        foreach (var h in Hats)
            if (h.Prestige == prestige)
                return h.Id;
        return HatId.None;
    }

    /// <summary>Reloads the catalog from disk (self-test).</summary>
    public static void Reload() => _hats = null;

    private static readonly (HatId Id, string Key, string Name)[] BuiltIn =
    [
        (HatId.Beanie, "beanie", "Beanie"), (HatId.BaseballCap, "baseball_cap", "Baseball Cap"), (HatId.Fedora, "fedora", "Fedora"),
        (HatId.TopHat, "top_hat", "Top Hat"), (HatId.TallTopHat, "tall_top_hat", "Ridiculous Top Hat"),
    ];

    private static IReadOnlyList<HatDef> LoadCatalog()
    {
        LoadError = null;
        try
        {
            if (!File.Exists(FilePath))
                throw new FormatException($"{FilePath} not found");
            var list = Parse(File.ReadAllText(FilePath), "hats.json");
            foreach (var (id, _, _) in BuiltIn)
                if (list.All(h => h.Id != id))
                    throw new FormatException($"hats.json: hat {id} is missing");
            return list;
        }
        catch (Exception ex)
        {
            LoadError = ex.Message;
            Console.WriteLine($"[cosmetics] {ex.Message}; using the built-in hats");
            return BuiltInCatalog();
        }
    }

    /// <summary>The fallback catalog: the contract's ids, names and prestige with a plain block model each.</summary>
    public static List<HatDef> BuiltInCatalog()
    {
        var list = new List<HatDef>();
        for (var i = 0; i < BuiltIn.Length; i++)
        {
            var (id, key, name) = BuiltIn[i];
            var h = i switch { 3 => 0.4f, 4 => 1.4f, _ => 0.15f };
            var def = new HatDef { Id = id, Key = key, Name = name, Prestige = i + 1, Wobble = i == 4 ? 1 : 0 };
            def.Boxes = [new HatBox(Matrix4x4.CreateScale(0.47f, h, 0.47f) * Matrix4x4.CreateTranslation(0, h / 2 - 0.02f, 0), new Vector3(0.12f + 0.1f * i, 0.12f, 0.14f))];
            Measure(def);
            list.Add(def);
        }
        return list;
    }

    /// <summary>Parses a cosmetics file. Throws <see cref="FormatException"/> with a readable message if it's wrong.</summary>
    public static List<HatDef> Parse(string json, string source)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new FormatException($"{source}: expected one JSON object");
        if (!root.TryGetProperty("format"u8, out var fmt) || fmt.GetString() != Format)
            throw new FormatException($"{source}: expected \"format\": \"{Format}\"");
        if (!root.TryGetProperty("hats"u8, out var hats) || hats.ValueKind != JsonValueKind.Array)
            throw new FormatException($"{source}: needs \"hats\": [ ... ]");
        var list = new List<HatDef>();
        var n = 0;
        foreach (var e in hats.EnumerateArray())
        {
            var where = $"{source}, hat #{++n}";
            var key = Str(e, "id", where);
            var id = BuiltIn.FirstOrDefault(b => b.Key == key).Id;
            if (id == HatId.None)
                throw new FormatException($"{where}: unknown hat id \"{key}\" (known: {string.Join(", ", BuiltIn.Select(b => b.Key))})");
            if (list.Any(h => h.Id == id))
                throw new FormatException($"{where}: \"{key}\" is listed twice");
            var name = Str(e, "name", where);
            if (name.Length is 0 or > 32 || name.Any(c => c < 32 || c > 126))
                throw new FormatException($"{where}: \"name\" must be 1-32 plain ASCII characters");
            var prestige = (int)Num(e, "prestige", where);
            if (prestige < 1 || prestige > MaxPrestige)
                throw new FormatException($"{where}: \"prestige\" is 1..{MaxPrestige}");
            var def = new HatDef
            {
                Id = id, Key = key, Name = name, Prestige = prestige,
                Wobble = e.TryGetProperty("wobble"u8, out var wb) ? Math.Clamp(wb.GetSingle(), 0, 2) : 0,
            };
            if (!e.TryGetProperty("parts"u8, out var parts) || parts.ValueKind != JsonValueKind.Array || parts.GetArrayLength() == 0)
                throw new FormatException($"{where}: needs \"parts\"");
            var boxes = new List<HatBox>();
            var k = 0;
            foreach (var p in parts.EnumerateArray())
                ReadPart(p, $"{where}, part #{++k}", boxes);
            if (boxes.Count > 48)
                throw new FormatException($"{where}: {boxes.Count} boxes is more than the limit of 48 (hats must stay cheap)");
            def.Boxes = boxes;
            Measure(def);
            if (def.Max.Y > 2f || def.Min.Y < -0.25f || MathF.Max(def.Max.X, -def.Min.X) > 0.6f || MathF.Max(def.Max.Z, -def.Min.Z) > 0.6f)
                throw new FormatException($"{where}: the model must stay within 0.6 m of the head sideways, 0.25 m below its top and 2 m above it");
            list.Add(def);
        }
        list.Sort((a, b) => a.Prestige.CompareTo(b.Prestige));
        return list;
    }

    private static void ReadPart(JsonElement p, string where, List<HatBox> boxes)
    {
        var shape = p.TryGetProperty("shape"u8, out var s) ? s.GetString() ?? "box" : "box";
        var at = Vec(p, "at", where, Vector3.Zero);
        var size = Vec(p, "size", where, null);
        if (size.X <= 0 || size.Y <= 0 || size.Z <= 0)
            throw new FormatException($"{where}: \"size\" must be positive");
        var color = Color(p, where);
        float Deg(string name) => p.TryGetProperty(name, out var v) ? v.GetSingle() * MathF.PI / 180f : 0f;
        var tilt = Matrix4x4.CreateRotationY(Deg("tiltY")) * Matrix4x4.CreateRotationX(Deg("tiltX")) * Matrix4x4.CreateRotationZ(Deg("tiltZ"));
        var place = tilt * Matrix4x4.CreateTranslation(at);
        switch (shape)
        {
            case "box":
                boxes.Add(new HatBox(Matrix4x4.CreateScale(size) * place, color));
                break;
            case "round":
                // A union of squares turned around Y: always covers the head's own square, reads as round from any side.
                var sides = p.TryGetProperty("sides"u8, out var sd) ? sd.GetInt32() : 4;
                if (sides < 1 || sides > 8)
                    throw new FormatException($"{where}: \"sides\" is 1..8");
                for (var i = 0; i < sides; i++)
                    boxes.Add(new HatBox(Matrix4x4.CreateScale(size) * Matrix4x4.CreateRotationY(i * MathF.PI / 2 / sides) * place, color));
                break;
            default:
                throw new FormatException($"{where}: \"shape\" is \"box\" or \"round\"");
        }
    }

    private static void Measure(HatDef def)
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var b in def.Boxes)
            for (var c = 0; c < 8; c++)
            {
                var v = Vector3.Transform(new Vector3((c & 1) - 0.5f, ((c >> 1) & 1) - 0.5f, ((c >> 2) & 1) - 0.5f), b.Local);
                min = Vector3.Min(min, v);
                max = Vector3.Max(max, v);
            }
        def.Min = min;
        def.Max = max;
    }

    private static string Str(JsonElement e, string name, string where) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : throw new FormatException($"{where}: needs \"{name}\" (text)");

    private static float Num(JsonElement e, string name, string where) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetSingle() : throw new FormatException($"{where}: needs \"{name}\" (a number)");

    private static Vector3 Vec(JsonElement e, string name, string where, Vector3? fallback)
    {
        if (!e.TryGetProperty(name, out var v))
            return fallback ?? throw new FormatException($"{where}: needs \"{name}\": [x, y, z]");
        if (v.ValueKind != JsonValueKind.Array || v.GetArrayLength() != 3)
            throw new FormatException($"{where}: \"{name}\" is [x, y, z]");
        return new Vector3(v[0].GetSingle(), v[1].GetSingle(), v[2].GetSingle());
    }

    private static Vector3 Color(JsonElement e, string where)
    {
        var text = e.TryGetProperty("color"u8, out var v) ? v.GetString() ?? "" : "";
        if (text.Length != 7 || text[0] != '#' || !int.TryParse(text.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
            throw new FormatException($"{where}: \"color\" is \"#rrggbb\"");
        return new Vector3((rgb >> 16 & 255) / 255f, (rgb >> 8 & 255) / 255f, (rgb & 255) / 255f);
    }
}
