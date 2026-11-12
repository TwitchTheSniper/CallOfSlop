using System.Globalization;
using System.Numerics;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>One box a uniform adds to the torso (coat front, belt, buttons, badges, insignia), in the torso's reference frame.</summary>
public readonly record struct UniformBox(Matrix4x4 Local, Vector3 Color, bool Detail);

/// <summary>
/// A uniform from Cosmetics/uniforms.json: the colours it puts on the coat, sleeves, trousers, boots and gloves, the boxes it
/// lays over the torso in place of the plate carrier, and the hat that goes with it (a <see cref="HatDef"/>, drawn and
/// knocked off like a prestige hat). Who wears it: story people whose name starts with one of <see cref="WornBy"/> (the
/// colony's staff by their titles), and players whose standing with <see cref="Faction"/> is one of <see cref="Tiers"/>.
/// </summary>
public sealed class UniformDef
{
    /// <summary>Its number on the wire (1.., the file's order; 0 is no uniform).</summary>
    public byte Index;
    public string Key = "", Name = "", About = "", Faction = "", Rank = "";
    /// <summary>Story people whose name starts with one of these wear it ("Warden ", "Officer ").</summary>
    public string[] WornBy = [];
    /// <summary>Players whose standing tier with <see cref="Faction"/> is one of these are issued it ("Inmate", "Trusty").</summary>
    public string[] Tiers = [];
    public Vector3 Coat, CoatDark, Sleeve, Collar, Trim, Trousers, Hips, Boots, Gloves, Cuff;
    public IReadOnlyList<UniformBox> Torso = [];
    /// <summary>The hat that goes with it, or null (then the body's own headwear shows, as without a uniform).</summary>
    public HatDef? Hat;

    /// <summary>The body colours: the coat on the shirt and shoulders, the collar, the trim as the accent; skin and hair stay the person's.</summary>
    public BodyColors Colors(in BodyVariant v)
    {
        var c = BodyColors.For(Coat, v);
        c.Shirt = Coat;
        c.ShirtDark = Collar;
        c.Sleeve = Sleeve;
        c.Vest = CoatDark;
        c.Pouch = CoatDark;
        c.Accent = Trim;
        return c;
    }

    public LegColors Legs => new() { Belt = Hips, Trousers = Trousers, Knee = Trousers * 0.92f, Boot = Boots, Sole = new Vector3(0.06f, 0.06f, 0.06f) };

    public ArmColors Arms => new() { Sleeve = Sleeve, Cuff = Cuff, Glove = Gloves, Fingers = Gloves * 0.88f };
}

/// <summary>
/// The uniform catalogue (Cosmetics/uniforms.json, format <see cref="Format"/>; docs/sandbox/UNIFORMS.md). Cosmetic only:
/// a uniform changes how a body is drawn, never its hit boxes. The server picks who wears what (Game/GameUniforms.cs) and
/// the roster carries one byte a player. A broken file is logged and leaves the catalogue empty (nobody in uniform).
/// </summary>
public static class Uniforms
{
    public const string Format = "call-of-slop-uniforms/1";

    /// <summary>Most uniforms (the wire byte), and most torso boxes one may add (the body's part budget: <see cref="BodyParts.Capacity"/>).</summary>
    public const int MaxUniforms = 64, MaxTorsoBoxes = 30;

    public static string FilePath => Path.Combine(AppFolders.Data, "Cosmetics", "uniforms.json");

    private static IReadOnlyList<UniformDef>? _all;

    /// <summary>The error from loading uniforms.json, or null if it loaded.</summary>
    public static string? LoadError { get; private set; }

    /// <summary>Every uniform in file order (index 0 is uniform 1).</summary>
    public static IReadOnlyList<UniformDef> All => _all ??= LoadCatalog();

    /// <summary>The uniform with this wire number, or null for 0 or an unknown one.</summary>
    public static UniformDef? Get(byte index) => index == 0 || index > All.Count ? null : All[index - 1];

    /// <summary>The uniform with this key ("ministry_warden"), or null.</summary>
    public static UniformDef? Find(string key)
    {
        foreach (var u in All)
            if (string.Equals(u.Key, key, StringComparison.OrdinalIgnoreCase))
                return u;
        return null;
    }

    /// <summary>The uniform a story person of this name wears (the first whose <see cref="UniformDef.WornBy"/> starts the name), or 0. No allocations.</summary>
    public static byte ForName(string name)
    {
        foreach (var u in All)
            foreach (var w in u.WornBy)
                if (name.StartsWith(w, StringComparison.Ordinal))
                    return u.Index;
        return 0;
    }

    /// <summary>The uniform a player standing at <paramref name="tier"/> with <paramref name="faction"/> is issued, or 0. No allocations.</summary>
    public static byte ForTier(string faction, string tier)
    {
        if (tier.Length == 0)
            return 0;
        foreach (var u in All)
            if (string.Equals(u.Faction, faction, StringComparison.OrdinalIgnoreCase))
                foreach (var t in u.Tiers)
                    if (string.Equals(t, tier, StringComparison.OrdinalIgnoreCase))
                        return u.Index;
        return 0;
    }

    private static IReadOnlyList<UniformDef> LoadCatalog()
    {
        LoadError = null;
        try
        {
            if (!File.Exists(FilePath))
                throw new FormatException($"{FilePath} not found");
            var list = Parse(File.ReadAllText(FilePath), "uniforms.json");
            Console.WriteLine($"[uniforms] {list.Count} uniforms");
            return list;
        }
        catch (Exception ex) when (ex is FormatException or JsonException or IOException or InvalidOperationException)
        {
            LoadError = ex.Message;
            Console.WriteLine($"[uniforms] {ex.Message}; nobody wears a uniform");
            return [];
        }
    }

    /// <summary>Parses a uniforms file. Throws <see cref="FormatException"/> with a readable message if it's wrong.</summary>
    public static List<UniformDef> Parse(string json, string source)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("format"u8, out var fmt) || fmt.GetString() != Format)
            throw new FormatException($"{source}: expected one JSON object with \"format\": \"{Format}\"");
        if (!root.TryGetProperty("uniforms"u8, out var arr) || arr.ValueKind != JsonValueKind.Array)
            throw new FormatException($"{source}: needs \"uniforms\": [ ... ]");
        var list = new List<UniformDef>();
        foreach (var e in arr.EnumerateArray())
        {
            var where = $"{source}, uniform #{list.Count + 1}";
            if (list.Count == MaxUniforms)
                throw new FormatException($"{where}: more than {MaxUniforms} uniforms");
            var u = new UniformDef { Index = (byte)(list.Count + 1) };
            u.Key = Text(e, "id", where, true);
            if (u.Key.Length == 0 || list.Any(x => x.Key == u.Key))
                throw new FormatException($"{where}: \"id\" must be given once");
            u.Name = Text(e, "name", where, true);
            u.About = Text(e, "about", where, false);
            u.Faction = Text(e, "faction", where, false);
            u.Rank = Text(e, "rank", where, false);
            foreach (var t in new[] { u.Name, u.About, u.Rank })
                if (t.Length > 120 || t.Any(c => c < 32 || c > 126))
                    throw new FormatException($"{where}: names are plain ASCII, at most 120 characters");
            u.WornBy = Strings(e, "wornBy", where);
            u.Tiers = Strings(e, "tiers", where);
            if (u.Tiers.Length > 0 && u.Faction.Length == 0)
                throw new FormatException($"{where}: \"tiers\" needs a \"faction\"");
            if (!e.TryGetProperty("colors"u8, out var col) || col.ValueKind != JsonValueKind.Object)
                throw new FormatException($"{where}: needs \"colors\": {{ \"coat\": \"#rrggbb\", ... }}");
            u.Coat = Hex(col, "coat", where, null);
            u.CoatDark = Hex(col, "coatDark", where, u.Coat * 0.7f);
            u.Sleeve = Hex(col, "sleeve", where, u.Coat);
            u.Collar = Hex(col, "collar", where, u.CoatDark);
            u.Trim = Hex(col, "trim", where, u.CoatDark);
            u.Trousers = Hex(col, "trousers", where, u.CoatDark);
            u.Hips = Hex(col, "hips", where, u.Coat);
            u.Boots = Hex(col, "boots", where, new Vector3(0.1f, 0.09f, 0.08f));
            u.Gloves = Hex(col, "gloves", where, new Vector3(0.34f, 0.28f, 0.2f));
            u.Cuff = Hex(col, "cuff", where, u.CoatDark);
            var boxes = new List<UniformBox>();
            if (e.TryGetProperty("torso"u8, out var torso))
            {
                if (torso.ValueKind != JsonValueKind.Array)
                    throw new FormatException($"{where}: \"torso\" is a list of boxes");
                var k = 0;
                foreach (var p in torso.EnumerateArray())
                {
                    var pw = $"{where}, torso box #{++k}";
                    var at = Cosmetics.Vec(p, "at", pw, Vector3.Zero);
                    var size = Cosmetics.Vec(p, "size", pw, null);
                    if (size.X <= 0 || size.Y <= 0 || size.Z <= 0)
                        throw new FormatException($"{pw}: \"size\" must be positive");
                    if (MathF.Abs(at.X) > 0.6f || MathF.Abs(at.Y) > 0.45f || MathF.Abs(at.Z) > 0.45f)
                        throw new FormatException($"{pw}: \"at\" must stay within the body (|x| 0.6, |y| 0.45, |z| 0.45 m of the torso's centre)");
                    float Deg(string name) => p.TryGetProperty(name, out var v) ? v.GetSingle() * MathF.PI / 180f : 0f;
                    var tilt = Matrix4x4.CreateRotationY(Deg("tiltY")) * Matrix4x4.CreateRotationX(Deg("tiltX")) * Matrix4x4.CreateRotationZ(Deg("tiltZ"));
                    var detail = p.TryGetProperty("detail"u8, out var d) && d.ValueKind == JsonValueKind.True;
                    boxes.Add(new UniformBox(Matrix4x4.CreateScale(size) * tilt * Matrix4x4.CreateTranslation(at), Cosmetics.Color(p, pw), detail));
                }
                if (boxes.Count > MaxTorsoBoxes)
                    throw new FormatException($"{where}: {boxes.Count} torso boxes is more than the limit of {MaxTorsoBoxes}");
            }
            u.Torso = boxes;
            if (e.TryGetProperty("hat"u8, out var hat))
            {
                if (hat.ValueKind != JsonValueKind.Array || hat.GetArrayLength() == 0)
                    throw new FormatException($"{where}: \"hat\" is a list of hat parts (as in hats.json)");
                var hb = new List<HatBox>();
                var k = 0;
                foreach (var p in hat.EnumerateArray())
                    Cosmetics.ReadPart(p, $"{where}, hat part #{++k}", hb);
                if (hb.Count > 48)
                    throw new FormatException($"{where}: {hb.Count} hat boxes is more than the limit of 48");
                var def = new HatDef { Key = u.Key, Name = u.Name, Boxes = hb };
                Cosmetics.Measure(def);
                if (def.Max.Y > 1f || def.Min.Y < -0.3f || MathF.Max(def.Max.X, -def.Min.X) > 0.6f || MathF.Max(def.Max.Z, -def.Min.Z) > 0.6f)
                    throw new FormatException($"{where}: the hat must stay within 0.6 m of the head sideways, 0.3 m below its top and 1 m above it");
                u.Hat = def;
            }
            list.Add(u);
        }
        return list;
    }

    private static string Text(JsonElement e, string name, string where, bool required)
    {
        if (e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String)
            return v.GetString()!.Trim();
        return required ? throw new FormatException($"{where}: needs \"{name}\" (text)") : "";
    }

    private static string[] Strings(JsonElement e, string name, string where)
    {
        if (!e.TryGetProperty(name, out var v))
            return [];
        if (v.ValueKind != JsonValueKind.Array || v.EnumerateArray().Any(x => x.ValueKind != JsonValueKind.String || x.GetString()!.Length == 0))
            throw new FormatException($"{where}: \"{name}\" is a list of words");
        return v.EnumerateArray().Select(x => x.GetString()!).ToArray();
    }

    private static Vector3 Hex(JsonElement obj, string name, string where, Vector3? fallback)
    {
        if (!obj.TryGetProperty(name, out var v))
            return fallback ?? throw new FormatException($"{where}: \"colors\" needs \"{name}\"");
        var text = v.GetString() ?? "";
        if (text.Length != 7 || text[0] != '#' || !int.TryParse(text.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
            throw new FormatException($"{where}: colour \"{name}\" is \"#rrggbb\"");
        return new Vector3((rgb >> 16 & 255) / 255f, (rgb >> 8 & 255) / 255f, (rgb & 255) / 255f);
    }
}
