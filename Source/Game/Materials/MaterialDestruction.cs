using System.Globalization;
using System.Numerics;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>What kind of hit damages a destructible chunk (each material scales it by its own multiplier).</summary>
public enum StructureHit : byte
{
    Bullet,
    Melee,
    Grenade,
    Launcher,
    Streak,
    PropBlast,
    Landmine,
    Count,
}

/// <summary>How one building material breaks (one entry of Materials/destruction.json, keyed by surface name).</summary>
public sealed class DestructionMaterial
{
    public required string Key;

    /// <summary>Health of a reference wall panel (1 x 1.25 m, 0.2 m thick); a chunk gets more or less by the square root of its volume (0.5x to 2x).</summary>
    public float Hp = 400;

    /// <summary>Damage multiplier per <see cref="StructureHit"/> (0: that kind of hit does nothing).</summary>
    public readonly float[] Mult = new float[(int)StructureHit.Count];

    /// <summary>How far (m) a chunk may reach sideways or hang from what holds it up before it falls.</summary>
    public float Span = 4f;

    /// <summary>Tonnes per cubic metre: how hard a falling piece lands on someone.</summary>
    public float Density = 1.8f;

    public float DebrisMin = 0.06f, DebrisMax = 0.3f;
    public int DebrisCount = 6;
    public Vector3 Dust = new(0.65f);
    public Sfx Sound = Sfx.ImpactConcrete;

    /// <summary>Can bullets hurt it at all?</summary>
    public bool BulletsChip => Mult[(int)StructureHit.Bullet] > 0;
}

/// <summary>The chunking and collapse settings of Materials/destruction.json ("chunks", "rules").</summary>
public sealed class DestructionRules
{
    /// <summary>Wall panels: width along the wall and height (m). Floors and roofs: slab cells (m).</summary>
    public float WallCell = 1.0f, WallRow = 1.25f, SlabCell = 2.0f;

    /// <summary>No piece narrower than this (m): a cut that would leave a sliver at a box's end is skipped.</summary>
    public float MinPiece = 0.3f;

    public int MaxPerBox = 48, MaxPerBuilding = 4000, MaxPerMap = 40000;

    /// <summary>Ground-floor chunks within this distance (m) of a footprint corner are load-bearing (never break). 0: none.</summary>
    public float CornerPillar = 0.8f;

    /// <summary>Extra reach (m) every chunk gets over what it needed as built (so a building always stands as built).</summary>
    public float SpanMargin = 1.0f;

    /// <summary>Damage of a falling piece: <see cref="FallDamage"/> x tonnes x sqrt(drop in m), at most <see cref="FallDamageMax"/>.</summary>
    public float FallDamage = 70f, FallDamageMax = 240f;

    /// <summary>Falling pieces the server tracks for landing damage at once (more just fall without hurting anyone).</summary>
    public int MaxFalling = 400;
}

/// <summary>
/// Materials/destruction.json: how each building material breaks (see Buildings/README.md, "Destruction"). Loaded once
/// (or again with <see cref="Reload"/>); a missing or broken file falls back to built-in values so the game always runs.
/// Materials are looked up by the surface names <see cref="ImpactDecals.SurfaceOf"/> gives (Grass, Sand and Snow use
/// Dirt; anything unknown uses Default).
/// </summary>
public sealed class MaterialDestruction
{
    public const string FileName = "destruction.json";
    public const string Format = "call-of-slop-destruction/1";

    /// <summary>The Materials folder next to the game.</summary>
    public static string Folder => Path.Combine(AppFolders.Data, "Materials");

    private static MaterialDestruction? _current;

    /// <summary>The table in use (loaded on first use).</summary>
    public static MaterialDestruction Current => _current ??= LoadFile(Path.Combine(Folder, FileName));

    /// <summary>Reads the file again (maps loaded after this use the new values).</summary>
    public static MaterialDestruction Reload() => _current = LoadFile(Path.Combine(Folder, FileName));

    public readonly List<DestructionMaterial> Materials = new();
    public DestructionRules Rules = new();
    public readonly List<string> Errors = new();

    /// <summary>Where the values came from ("built-in" when the file was missing or unreadable).</summary>
    public string Source = "built-in";

    /// <summary>A fingerprint of every value (mixed into the fingerprint of maps with destruction: they must agree).</summary>
    public uint Hash { get; private set; }

    private readonly Dictionary<string, byte> _byKey = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The material id (index into <see cref="Materials"/>) for a surface name.</summary>
    public byte IdOf(string surface)
    {
        if (_byKey.TryGetValue(surface, out var id))
            return id;
        if (surface is "Grass" or "Sand" or "Snow" && _byKey.TryGetValue("Dirt", out id))
            return id;
        return _byKey.TryGetValue("Default", out id) ? id : (byte)0;
    }

    public DestructionMaterial this[int id] => Materials[(uint)id < (uint)Materials.Count ? id : 0];

    /// <summary>Is <paramref name="key"/> a material in the table?</summary>
    public bool Has(string key) => _byKey.ContainsKey(key);

    /// <summary>Loads a destruction table from a file (built-in values if it's missing or bad; problems go in <see cref="Errors"/>).</summary>
    public static MaterialDestruction LoadFile(string path)
    {
        if (!File.Exists(path))
        {
            var d = BuiltIn();
            d.Errors.Add($"{path} not found: built-in destruction values");
            Console.WriteLine($"[destruction] {path} not found: using built-in values");
            return d;
        }
        try
        {
            var t = Parse(File.ReadAllText(path));
            t.Source = path;
            foreach (var e in t.Errors)
                Console.WriteLine($"[destruction] {FileName}: {e}");
            return t;
        }
        catch (Exception ex) when (ex is IOException or JsonException or FormatException or InvalidOperationException)
        {
            var d = BuiltIn();
            d.Errors.Add($"{FileName}: {ex.Message} (built-in values used)");
            Console.WriteLine($"[destruction] {FileName}: {ex.Message} (using built-in values)");
            return d;
        }
    }

    /// <summary>Parses the JSON text (throws on invalid JSON; unknown or bad fields are reported in <see cref="Errors"/>).</summary>
    public static MaterialDestruction Parse(string json)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new FormatException("the file is a JSON object { \"materials\": { ... } }");
        var t = new MaterialDestruction();
        foreach (var p in root.EnumerateObject())
        {
            switch (p.Name)
            {
                case "format" or "about":
                    break;
                case "chunks" when p.Value.ValueKind == JsonValueKind.Object:
                    foreach (var c in p.Value.EnumerateObject())
                    {
                        var r = t.Rules;
                        switch (c.Name)
                        {
                            case "wallCell": r.WallCell = Num(t, c, 0.4f, 4f, r.WallCell); break;
                            case "wallRow": r.WallRow = Num(t, c, 0.4f, 4f, r.WallRow); break;
                            case "slabCell": r.SlabCell = Num(t, c, 0.5f, 8f, r.SlabCell); break;
                            case "minPiece": r.MinPiece = Num(t, c, 0.05f, 1f, r.MinPiece); break;
                            case "maxPerBox": r.MaxPerBox = (int)Num(t, c, 1, 512, r.MaxPerBox); break;
                            case "maxPerBuilding": r.MaxPerBuilding = (int)Num(t, c, 50, 20000, r.MaxPerBuilding); break;
                            case "maxPerMap": r.MaxPerMap = (int)Num(t, c, 100, 200000, r.MaxPerMap); break;
                            default: t.Errors.Add($"chunks: unknown \"{c.Name}\""); break;
                        }
                    }
                    break;
                case "rules" when p.Value.ValueKind == JsonValueKind.Object:
                    foreach (var c in p.Value.EnumerateObject())
                    {
                        var r = t.Rules;
                        switch (c.Name)
                        {
                            case "cornerPillar": r.CornerPillar = Num(t, c, 0, 3, r.CornerPillar); break;
                            case "spanMargin": r.SpanMargin = Num(t, c, 0, 10, r.SpanMargin); break;
                            case "fallDamage": r.FallDamage = Num(t, c, 0, 1000, r.FallDamage); break;
                            case "fallDamageMax": r.FallDamageMax = Num(t, c, 0, 1000, r.FallDamageMax); break;
                            case "maxFalling": r.MaxFalling = (int)Num(t, c, 0, 2048, r.MaxFalling); break;
                            default: t.Errors.Add($"rules: unknown \"{c.Name}\""); break;
                        }
                    }
                    break;
                case "materials" when p.Value.ValueKind == JsonValueKind.Object:
                    foreach (var m in p.Value.EnumerateObject())
                        t.Add(ReadMaterial(t, m.Name, m.Value));
                    break;
                default:
                    t.Errors.Add($"unknown field \"{p.Name}\"");
                    break;
            }
        }
        if (!t._byKey.ContainsKey("Default"))
            t.Add(Fallback("Default"));
        t.Finish();
        return t;
    }

    private static float Num(MaterialDestruction t, JsonProperty p, float lo, float hi, float fallback)
    {
        if (p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetSingle(out var v) && float.IsFinite(v))
            return Math.Clamp(v, lo, hi);
        t.Errors.Add($"\"{p.Name}\" is a number ({lo}-{hi})");
        return fallback;
    }

    private static readonly string[] HitKeys = ["bullet", "melee", "grenade", "launcher", "streak", "propBlast", "landmine"];

    private static DestructionMaterial ReadMaterial(MaterialDestruction t, string key, JsonElement e)
    {
        var m = Fallback(key);
        if (e.ValueKind != JsonValueKind.Object)
        {
            t.Errors.Add($"material \"{key}\" is an object {{ \"hp\": ..., ... }}");
            return m;
        }
        foreach (var p in e.EnumerateObject())
        {
            var hit = Array.IndexOf(HitKeys, p.Name);
            if (hit >= 0)
            {
                m.Mult[hit] = Num(t, p, 0, 100, m.Mult[hit]);
                continue;
            }
            switch (p.Name)
            {
                case "hp": m.Hp = Num(t, p, 1, 100000, m.Hp); break;
                case "span": m.Span = Num(t, p, 0.2f, 50, m.Span); break;
                case "density": m.Density = Num(t, p, 0.05f, 20, m.Density); break;
                case "debrisCount": m.DebrisCount = (int)Num(t, p, 0, 32, m.DebrisCount); break;
                case "debrisSize" when p.Value.ValueKind == JsonValueKind.Array && p.Value.GetArrayLength() == 2
                                       && p.Value[0].TryGetSingle(out var lo) && p.Value[1].TryGetSingle(out var hi):
                    m.DebrisMin = Math.Clamp(MathF.Min(lo, hi), 0.01f, 2f);
                    m.DebrisMax = Math.Clamp(MathF.Max(lo, hi), 0.01f, 2f);
                    break;
                case "dust" when p.Value.ValueKind == JsonValueKind.String && Hex(p.Value.GetString()!) is { } c:
                    m.Dust = c;
                    break;
                case "sound" when p.Value.ValueKind == JsonValueKind.String && Enum.TryParse<Sfx>(p.Value.GetString(), true, out var s) && Enum.IsDefined(s) && s != Sfx.COUNT:
                    m.Sound = s;
                    break;
                default:
                    t.Errors.Add($"material \"{key}\": bad \"{p.Name}\": {p.Value}");
                    break;
            }
        }
        return m;
    }

    private static Vector3? Hex(string s) =>
        s.Length == 7 && s[0] == '#' && int.TryParse(s.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb)
            ? new Vector3(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f)
            : null;

    private void Add(DestructionMaterial m)
    {
        if (_byKey.TryGetValue(m.Key, out var old))
        {
            Materials[old] = m;
            return;
        }
        if (Materials.Count >= 255)
        {
            Errors.Add($"too many materials (\"{m.Key}\" left out)");
            return;
        }
        _byKey[m.Key] = (byte)Materials.Count;
        Materials.Add(m);
    }

    private static DestructionMaterial Fallback(string key)
    {
        var m = new DestructionMaterial { Key = key };
        float[] mult = [0, 0, 1f, 5f, 8f, 3f, 2f];
        mult.CopyTo(m.Mult, 0);
        return m;
    }

    /// <summary>Built-in values (the shipped file's main materials), for a missing or unreadable file.</summary>
    public static MaterialDestruction BuiltIn()
    {
        var t = new MaterialDestruction();
        void M(string key, float hp, float span, float density, Sfx sound, params float[] mult)
        {
            var m = new DestructionMaterial { Key = key, Hp = hp, Span = span, Density = density, Sound = sound };
            mult.CopyTo(m.Mult, 0);
            t.Add(m);
        }
        M("Concrete", 1200, 4.5f, 2.4f, Sfx.ImpactConcrete, 0, 0, 0.3f, 3, 10, 2.5f, 1);
        M("Brick", 400, 3.5f, 1.9f, Sfx.ImpactConcrete, 0, 0, 1, 5, 8, 3, 2);
        M("Plaster", 200, 2.5f, 1.1f, Sfx.BreakSoft, 0.35f, 0.8f, 1.3f, 3, 5, 2.5f, 2);
        M("Wood", 180, 3, 0.6f, Sfx.BreakWood, 0.4f, 0.8f, 1.2f, 3, 5, 2.5f, 2);
        M("Tiles", 220, 3, 1.4f, Sfx.BreakGlass, 0.1f, 0.3f, 1, 3, 5, 2.5f, 1.5f);
        M("Metal", 700, 5, 1.2f, Sfx.BreakMetal, 0, 0, 0.4f, 2.5f, 5, 2, 1);
        M("Default", 400, 3.5f, 1.9f, Sfx.ImpactConcrete, 0, 0, 1, 5, 8, 3, 2);
        t.Finish();
        return t;
    }

    private void Finish()
    {
        var h = 2166136261u;
        void Mix(float f)
        {
            var bits = BitConverter.SingleToUInt32Bits(f);
            for (var i = 0; i < 4; i++)
            {
                h ^= (bits >> (i * 8)) & 0xFF;
                h *= 16777619u;
            }
        }
        var r = Rules;
        foreach (var f in new[] { r.WallCell, r.WallRow, r.SlabCell, r.MinPiece, r.MaxPerBox, r.MaxPerBuilding, r.MaxPerMap, r.CornerPillar, r.SpanMargin, r.FallDamage, r.FallDamageMax, r.MaxFalling })
            Mix(f);
        foreach (var m in Materials)
        {
            foreach (var ch in m.Key)
                Mix(ch);
            Mix(m.Hp);
            Mix(m.Span);
            Mix(m.Density);
            foreach (var x in m.Mult)
                Mix(x);
        }
        Hash = h == 0 ? 1 : h;
    }
}
