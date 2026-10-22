using System.Text.Json;

namespace CallOfSlop;

/// <summary>A stat a frenzy buff changes (the "stat" key of an effect in Weapons/Frenzy/classes.json).</summary>
public enum FrenzyStat : byte
{
    /// <summary>"equipTime": multiplier on the time to draw the weapon (swap to it).</summary>
    EquipTime,

    /// <summary>"moveSpread": multiplier on the extra spread from moving (and from firing on the run out of a sprint).</summary>
    MoveSpread,

    /// <summary>"recoil": multiplier on the camera kick per shot.</summary>
    Recoil,

    /// <summary>"adsTime": multiplier on the time to aim down sights.</summary>
    AdsTime,

    /// <summary>"aimedSpread": multiplier on the whole cone while aimed (blended by how far in the aim is).</summary>
    AimedSpread,

    /// <summary>"spread": multiplier on the whole cone (hip and aimed, every pellet).</summary>
    Spread,

    /// <summary>"reload": multiplier on the reload time.</summary>
    Reload,

    /// <summary>"movePenalty": multiplier on how much the weapon slows you (mobility 0.8 = a 0.2 penalty).</summary>
    MovePenalty,

    /// <summary>"killHeal": hit points back for each kill with the weapon (additive, 0 = none).</summary>
    KillHeal,
}

/// <summary>One effect of a frenzy buff: its value at the buff's first level and at frenzy level 6 (linear between).</summary>
public readonly record struct FrenzyEffect(FrenzyStat Stat, float AtMin, float AtMax)
{
    /// <summary>The value meaning "no change": 0 for <see cref="FrenzyStat.KillHeal"/>, 1 for the multipliers.</summary>
    public float Identity => Stat == FrenzyStat.KillHeal ? 0f : 1f;

    /// <summary>Safety limits: multipliers stay between 0.5 and 1 (buffs only, never more than halving), heals 0-50 HP.</summary>
    public float Clamp(float v) => Stat == FrenzyStat.KillHeal ? Math.Clamp(v, 0f, 50f) : Math.Clamp(v, 0.5f, 1f);

    /// <summary>The value at frenzy <paramref name="level"/> for a buff that starts at <paramref name="minLevel"/>.</summary>
    public float At(int level, int minLevel)
    {
        if (level < minLevel || level <= 0)
            return Identity;
        var span = FrenzyMeterCore.MaxLevel - minLevel;
        var t = span <= 0 ? 1f : Math.Clamp((level - minLevel) / (float)span, 0f, 1f);
        return Clamp(float.Lerp(AtMin, AtMax, t));
    }
}

/// <summary>A class's gameplay buff ("buffs" in classes.json).</summary>
public sealed class FrenzyBuffDef
{
    public string Name = "", Blurb = "";

    /// <summary>The first frenzy level the buff works at (1-6).</summary>
    public int MinLevel = 3;

    public FrenzyEffect[] Effects = [];

    /// <summary>The value of <paramref name="stat"/> at frenzy <paramref name="level"/> (the identity if this buff doesn't touch it).</summary>
    public float Value(FrenzyStat stat, int level)
    {
        foreach (var e in Effects)
            if (e.Stat == stat)
                return e.At(level, MinLevel);
        return stat == FrenzyStat.KillHeal ? 0f : 1f;
    }
}

/// <summary>
/// One weapon class's frenzy character from Weapons/Frenzy/classes.json. <see cref="Music"/>, <see cref="Visual"/> and
/// <see cref="Sound"/> are that file's sections as raw JSON (each feature reads its own); <see cref="Buffs"/> is parsed here.
/// </summary>
public sealed class FrenzyClassEntry
{
    public WeaponClass Class;
    public string Character = "";
    public JsonElement Music, Visual, Sound;
    public FrenzyBuffDef Buffs = new();
}

/// <summary>
/// The per-class frenzy data file, Weapons/Frenzy/classes.json (format <see cref="Format"/>): one entry per
/// <see cref="WeaponClass"/>, with sections owned by different features ("music", "visual", "sound", "buffs").
/// Loaded on first use; a missing or broken file (or class) falls back to the built-in buffs and empty sections, and
/// the problems go to <see cref="LoadErrors"/>. The weapon loader skips this folder (<see cref="IsFrenzyFile"/>).
/// </summary>
public static class FrenzyClasses
{
    public const string Format = "call-of-slop-frenzy-classes/1";

    /// <summary>The folder under Weapons that holds frenzy data rather than weapons.</summary>
    public const string SubFolder = "Frenzy";

    public static string FilePath => Path.Combine(Weapons.Folder, SubFolder, "classes.json");

    /// <summary>True for a path (relative to the Weapons folder) inside <see cref="SubFolder"/>.</summary>
    public static bool IsFrenzyFile(string relative) => relative.Replace('\\', '/').StartsWith(SubFolder + "/", StringComparison.OrdinalIgnoreCase);

    /// <summary>Problems found the last time the file was read (empty when it loaded cleanly).</summary>
    public static readonly List<string> LoadErrors = new();

    private static FrenzyClassEntry[]? _all;
    private static readonly object Lock = new();

    /// <summary>Every class's entry, indexed by <see cref="WeaponClass"/>.</summary>
    public static IReadOnlyList<FrenzyClassEntry> All
    {
        get
        {
            lock (Lock)
                return _all ??= Load();
        }
    }

    /// <summary>The entry for <paramref name="c"/> (an unknown value gets the Special entry).</summary>
    public static FrenzyClassEntry Get(WeaponClass c)
    {
        var all = All;
        var i = (int)c;
        return i >= 0 && i < all.Count ? all[i] : all[^1];
    }

    /// <summary>Reads the file again (next use).</summary>
    public static void Reload()
    {
        lock (Lock)
            _all = null;
    }

    private static FrenzyClassEntry[] Load()
    {
        LoadErrors.Clear();
        string? json = null;
        try
        {
            if (File.Exists(FilePath))
                json = File.ReadAllText(FilePath);
            else
                LoadErrors.Add($"{FilePath} is missing; using the built-in frenzy buffs");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LoadErrors.Add($"{FilePath}: {ex.Message}");
        }
        var list = Parse(json, LoadErrors);
        foreach (var e in LoadErrors)
            Console.WriteLine($"[frenzy] {e}");
        return list;
    }

    /// <summary>Parses a classes.json text (null: built-in defaults only). Problems are added to <paramref name="errors"/>.</summary>
    public static FrenzyClassEntry[] Parse(string? json, List<string> errors)
    {
        var classes = Enum.GetValues<WeaponClass>();
        var list = new FrenzyClassEntry[classes.Length];
        for (var i = 0; i < classes.Length; i++)
            list[i] = new FrenzyClassEntry { Class = classes[i], Buffs = DefaultBuffs(classes[i]), Music = Empty, Visual = Empty, Sound = Empty };
        if (json == null)
            return list;
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var root = doc.RootElement;
            if (!root.TryGetProperty("format"u8, out var fmt) || fmt.GetString() != Format)
                errors.Add($"classes.json: format should be \"{Format}\"");
            if (!root.TryGetProperty("classes"u8, out var cls) || cls.ValueKind != JsonValueKind.Object)
            {
                errors.Add("classes.json: no \"classes\" object");
                return list;
            }
            var seen = new bool[classes.Length];
            foreach (var prop in cls.EnumerateObject())
            {
                if (!Enum.TryParse<WeaponClass>(prop.Name.Replace(" ", ""), true, out var c) || !Enum.IsDefined(c))
                {
                    errors.Add($"classes.json: unknown weapon class \"{prop.Name}\"");
                    continue;
                }
                var e = list[(int)c];
                seen[(int)c] = true;
                var v = prop.Value;
                if (v.TryGetProperty("character"u8, out var ch) && ch.ValueKind == JsonValueKind.String)
                    e.Character = ch.GetString() ?? "";
                e.Music = Section(v, "music");
                e.Visual = Section(v, "visual");
                e.Sound = Section(v, "sound");
                if (v.TryGetProperty("buffs"u8, out var b) && b.ValueKind == JsonValueKind.Object)
                    e.Buffs = ParseBuffs(b, prop.Name, errors) ?? e.Buffs;
            }
            for (var i = 0; i < classes.Length; i++)
                if (!seen[i])
                    errors.Add($"classes.json: no entry for {classes[i]} (built-in buffs used)");
        }
        catch (JsonException ex)
        {
            errors.Add($"classes.json: {ex.Message}");
        }
        return list;
    }

    private static readonly JsonElement Empty = JsonDocument.Parse("{}").RootElement.Clone();

    private static JsonElement Section(JsonElement v, string key) =>
        v.TryGetProperty(key, out var s) && s.ValueKind == JsonValueKind.Object ? s.Clone() : Empty;

    private static readonly Dictionary<string, FrenzyStat> StatKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["equipTime"] = FrenzyStat.EquipTime, ["moveSpread"] = FrenzyStat.MoveSpread, ["recoil"] = FrenzyStat.Recoil,
        ["adsTime"] = FrenzyStat.AdsTime, ["aimedSpread"] = FrenzyStat.AimedSpread, ["spread"] = FrenzyStat.Spread,
        ["reload"] = FrenzyStat.Reload, ["movePenalty"] = FrenzyStat.MovePenalty, ["killHeal"] = FrenzyStat.KillHeal,
    };

    private static FrenzyBuffDef? ParseBuffs(JsonElement b, string cls, List<string> errors)
    {
        var d = new FrenzyBuffDef();
        if (b.TryGetProperty("name"u8, out var n) && n.ValueKind == JsonValueKind.String)
            d.Name = n.GetString() ?? "";
        if (b.TryGetProperty("blurb"u8, out var bl) && bl.ValueKind == JsonValueKind.String)
            d.Blurb = bl.GetString() ?? "";
        if (b.TryGetProperty("minLevel"u8, out var ml) && ml.TryGetInt32(out var min))
            d.MinLevel = Math.Clamp(min, 1, FrenzyMeterCore.MaxLevel);
        var effects = new List<FrenzyEffect>();
        if (b.TryGetProperty("effects"u8, out var ef) && ef.ValueKind == JsonValueKind.Array)
            foreach (var x in ef.EnumerateArray())
            {
                if (!x.TryGetProperty("stat"u8, out var st) || st.GetString() is not { } key || !StatKeys.TryGetValue(key, out var stat))
                {
                    errors.Add($"classes.json: {cls}: an effect has no known \"stat\" ({string.Join(", ", StatKeys.Keys)})");
                    continue;
                }
                if (!x.TryGetProperty("atMin"u8, out var a) || !a.TryGetSingle(out var atMin) || !x.TryGetProperty("atMax"u8, out var z) || !z.TryGetSingle(out var atMax)
                    || !float.IsFinite(atMin) || !float.IsFinite(atMax))
                {
                    errors.Add($"classes.json: {cls}: effect \"{key}\" needs numbers atMin and atMax");
                    continue;
                }
                var e = new FrenzyEffect(stat, atMin, atMax);
                if (e.Clamp(atMin) != atMin || e.Clamp(atMax) != atMax)
                    errors.Add($"classes.json: {cls}: effect \"{key}\" clamped to {e.Clamp(atMin)}..{e.Clamp(atMax)} (buffs stay modest)");
                effects.Add(e with { AtMin = e.Clamp(atMin), AtMax = e.Clamp(atMax) });
            }
        d.Effects = effects.ToArray();
        return d;
    }

    /// <summary>The built-in buffs (the same numbers as the shipped classes.json; used when the file or a class is missing).</summary>
    public static FrenzyBuffDef DefaultBuffs(WeaponClass c) => c switch
    {
        WeaponClass.Pistol => Buff("Quick draw", "Pistols come out faster", new FrenzyEffect(FrenzyStat.EquipTime, 0.9f, 0.7f)),
        WeaponClass.SMG => Buff("Run and gun", "SMGs lose less accuracy while moving, so firing out of a sprint hits", new FrenzyEffect(FrenzyStat.MoveSpread, 0.85f, 0.6f)),
        WeaponClass.AssaultRifle => Buff("Steady hands", "Assault rifles kick less", new FrenzyEffect(FrenzyStat.Recoil, 0.94f, 0.85f)),
        WeaponClass.DMR => Buff("Snap aim", "Marksman rifles aim down sights faster", new FrenzyEffect(FrenzyStat.AdsTime, 0.92f, 0.8f)),
        WeaponClass.Sniper => Buff("Held breath", "Sniper rifles hold a steadier, tighter aim through the scope", new FrenzyEffect(FrenzyStat.AimedSpread, 0.85f, 0.65f)),
        WeaponClass.Shotgun => Buff("Choke", "Shotguns fire a tighter spread", new FrenzyEffect(FrenzyStat.Spread, 0.94f, 0.85f)),
        WeaponClass.LMG => Buff("Belt fed", "LMGs reload faster and slow you down less", new FrenzyEffect(FrenzyStat.Reload, 0.94f, 0.85f), new FrenzyEffect(FrenzyStat.MovePenalty, 0.85f, 0.6f)),
        WeaponClass.Launcher => Buff("Hot loader", "Launchers reload faster", new FrenzyEffect(FrenzyStat.Reload, 0.9f, 0.7f)),
        _ => Buff("Vampire rounds", "Kills with a Special weapon give you some health back", new FrenzyEffect(FrenzyStat.KillHeal, 10f, 25f)),
    };

    private static FrenzyBuffDef Buff(string name, string blurb, params FrenzyEffect[] effects) => new() { Name = name, Blurb = blurb, MinLevel = 3, Effects = effects };
}
