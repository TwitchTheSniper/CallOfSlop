using System.Globalization;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>How one material takes a bullet passing through it (one entry of Materials/penetration.json).</summary>
/// <param name="ResistPerCm">Penetration power a round spends per centimetre of this material along its path.</param>
/// <param name="MaxCm">The thickest piece a bullet ever passes (cm along the path); 0 = never (rock, earth banks).</param>
/// <param name="DamageKept">The share of damage a round keeps after passing (scaled down further the harder it was).</param>
public readonly record struct PenMaterial(float ResistPerCm, float MaxCm, float DamageKept)
{
    /// <summary>Does this material stop every bullet?</summary>
    public bool Stops => MaxCm <= 0 || DamageKept <= 0;
}

/// <summary>
/// Bullet penetration data (Materials/penetration.json, loaded once): per <see cref="MaterialKey"/> a resistance, the
/// thickest piece a bullet passes and the damage kept after it; per weapon class a penetration power from the calibre
/// (<see cref="PowerOf"/>); which props are made of what (<see cref="PropMaterial"/>). The rules that use it are in
/// <see cref="PenetrationTrace"/>. Missing or broken file: the built-in table below (the same numbers as the shipped file).
/// </summary>
public static class MaterialPenetration
{
    public const string Format = "call-of-slop-penetration/1";

    /// <summary>The folder with penetration.json (copied next to the game).</summary>
    public static string Folder => Path.Combine(AppFolders.Data, "Materials");

    /// <summary>Most separate surfaces (walls, floors, a door...) one bullet passes before it must stop in the next.</summary>
    public static int MaxSurfaces { get; private set; } = 3;

    /// <summary>The most material (cm along the path) one surface of touching boxes may be, whatever it is made of.</summary>
    public static float MaxLayerCm { get; private set; } = 80;

    /// <summary>A shotgun's pellets (any weapon firing more than one pellet per shot): power of each.</summary>
    public static float PelletPower { get; private set; } = 2;

    /// <summary>Where the table came from (the file's path, or "built-in"), and the problem if the file was refused.</summary>
    public static string Source { get; private set; } = "built-in";

    public static string? LoadError { get; private set; }

    private static readonly PenMaterial[] Mats = new PenMaterial[(int)MaterialKey.Count];
    private static readonly (float Base, float PerCalibre)[] Powers = new (float, float)[(int)WeaponClass.Launcher + 1];
    private static (string Match, MaterialKey Key)[] _props = [];
    private static bool _loaded;

    private static void Defaults()
    {
        void M(MaterialKey k, float r, float max, float kept) => Mats[(int)k] = new PenMaterial(r, max, kept);
        M(MaterialKey.Default, 6.5f, 20, 0.55f);
        M(MaterialKey.Grid, 4.5f, 30, 0.6f);
        M(MaterialKey.Concrete, 6.5f, 25, 0.55f);
        M(MaterialKey.Brick, 4.5f, 35, 0.6f);
        M(MaterialKey.Wood, 1f, 45, 0.8f);
        M(MaterialKey.Metal, 8f, 6, 0.6f);
        M(MaterialKey.Tiles, 2.5f, 25, 0.7f);
        M(MaterialKey.Dirt, 6f, 20, 0.5f);
        M(MaterialKey.Asphalt, 0, 0, 0);
        M(MaterialKey.Plaster, 0.8f, 40, 0.85f);
        M(MaterialKey.Grass, 6f, 20, 0.5f);
        M(MaterialKey.Sand, 9f, 15, 0.45f);
        M(MaterialKey.Snow, 2f, 80, 0.7f);
        M(MaterialKey.Rock, 0, 0, 0);
        M(MaterialKey.Glass, 0.3f, 6, 0.9f);
        M(MaterialKey.Soft, 0.3f, 120, 0.95f);
        M(MaterialKey.Hedgerow, 0, 0, 0);
        Powers[(int)WeaponClass.Pistol] = (5, 12);
        Powers[(int)WeaponClass.SMG] = (5, 12);
        Powers[(int)WeaponClass.AssaultRifle] = (12, 20);
        Powers[(int)WeaponClass.LMG] = (16, 28);
        Powers[(int)WeaponClass.DMR] = (20, 30);
        Powers[(int)WeaponClass.Sniper] = (30, 70);
        Powers[(int)WeaponClass.Shotgun] = (10, 20);
        Powers[(int)WeaponClass.Special] = (6, 14);
        Powers[(int)WeaponClass.Launcher] = (0, 0);
        MaxSurfaces = 3;
        MaxLayerCm = 80;
        PelletPower = 2;
        _props =
        [
            ("Bocage Hedgerow", MaterialKey.Hedgerow), ("Sandbag", MaterialKey.Sand), ("Hedge", MaterialKey.Hedgerow),
            ("Boulder", MaterialKey.Rock), ("Rock", MaterialKey.Rock), ("Car", MaterialKey.Metal), ("Truck", MaterialKey.Metal),
            ("Van", MaterialKey.Metal), ("Crate", MaterialKey.Wood), ("Pallet", MaterialKey.Wood), ("Cardboard", MaterialKey.Soft),
        ];
    }

    /// <summary>Loads Materials/penetration.json once (the first time anything asks).</summary>
    public static void EnsureLoaded()
    {
        if (_loaded)
            return;
        _loaded = true;
        Load(Path.Combine(Folder, "penetration.json"));
    }

    /// <summary>(Re)loads the table from a file: the built-in numbers first, then whatever the file gives. Returns false (and logs) on a bad file.</summary>
    public static bool Load(string path)
    {
        _loaded = true;
        Defaults();
        Source = "built-in";
        LoadError = null;
        if (!File.Exists(path))
        {
            LoadError = $"{path} not found: using the built-in penetration table";
            Console.WriteLine(LoadError);
            return false;
        }
        try
        {
            Parse(File.ReadAllText(path), path);
            Source = path;
            return true;
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException or KeyNotFoundException)
        {
            Defaults();
            LoadError = $"{path}: {ex.Message} (using the built-in penetration table)";
            Console.WriteLine(LoadError);
            return false;
        }
    }

    /// <summary>Reads a penetration file's text over the current table. Throws <see cref="FormatException"/> with a readable message.</summary>
    public static void Parse(string json, string source)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new FormatException($"{source}: expected one JSON object");
        foreach (var p in root.EnumerateObject())
        {
            switch (p.Name.ToLowerInvariant())
            {
                case "format":
                    if (p.Value.ValueKind != JsonValueKind.String || !(p.Value.GetString() ?? "").StartsWith("call-of-slop-penetration/", StringComparison.Ordinal))
                        throw new FormatException($"{source}: \"format\" must be \"{Format}\"");
                    break;
                case "comment":
                    break;
                case "maxsurfaces":
                    MaxSurfaces = (int)Math.Clamp(Num(p.Value, source, p.Name), 0, 8);
                    break;
                case "maxlayercm":
                    MaxLayerCm = Math.Clamp(Num(p.Value, source, p.Name), 0, 500);
                    break;
                case "shotgunpellets":
                    PelletPower = Math.Clamp(Num(p.Value, source, p.Name), 0, 500);
                    break;
                case "materials":
                    foreach (var m in p.Value.EnumerateObject())
                    {
                        if (!MaterialKeys.TryParse(m.Name, out var key))
                            throw new FormatException($"{source}: unknown material \"{m.Name}\" (known: {string.Join(", ", MaterialKeys.Names)})");
                        var cur = Mats[(int)key];
                        float r = cur.ResistPerCm, max = cur.MaxCm, kept = cur.DamageKept;
                        foreach (var f in m.Value.EnumerateObject())
                        {
                            var v = Num(f.Value, source, m.Name + "." + f.Name);
                            switch (f.Name.ToLowerInvariant())
                            {
                                case "resistpercm": r = Math.Clamp(v, 0, 1000); break;
                                case "maxcm": max = Math.Clamp(v, 0, 1000); break;
                                case "damagekept": kept = Math.Clamp(v, 0, 1); break;
                                default: throw new FormatException($"{source}: unknown key \"{f.Name}\" in material \"{m.Name}\" (resistPerCm, maxCm, damageKept)");
                            }
                        }
                        Mats[(int)key] = new PenMaterial(r, max, kept);
                    }
                    break;
                case "weapons":
                    foreach (var w in p.Value.EnumerateObject())
                    {
                        if (!Enum.TryParse<WeaponClass>(w.Name, true, out var cls))
                            throw new FormatException($"{source}: unknown weapon class \"{w.Name}\" (known: {string.Join(", ", Enum.GetNames<WeaponClass>())})");
                        var (b, per) = Powers[(int)cls];
                        foreach (var f in w.Value.EnumerateObject())
                        {
                            var v = Math.Clamp(Num(f.Value, source, w.Name + "." + f.Name), 0, 1000);
                            switch (f.Name.ToLowerInvariant())
                            {
                                case "base": b = v; break;
                                case "percalibre" or "percaliber": per = v; break;
                                default: throw new FormatException($"{source}: unknown key \"{f.Name}\" in weapon class \"{w.Name}\" (base, perCalibre)");
                            }
                        }
                        Powers[(int)cls] = (b, per);
                    }
                    break;
                case "props":
                    var list = new List<(string, MaterialKey)>();
                    foreach (var e in p.Value.EnumerateArray())
                    {
                        string? match = null, mat = null;
                        if (e.ValueKind == JsonValueKind.Object)
                            foreach (var f in e.EnumerateObject())
                            {
                                if (f.Name == "match" && f.Value.ValueKind == JsonValueKind.String)
                                    match = f.Value.GetString();
                                else if (f.Name == "material" && f.Value.ValueKind == JsonValueKind.String)
                                    mat = f.Value.GetString();
                            }
                        if (string.IsNullOrWhiteSpace(match) || !MaterialKeys.TryParse(mat, out var key))
                            throw new FormatException($"{source}: each \"props\" entry is {{ \"match\": \"<words in the prop's name>\", \"material\": \"<material>\" }} (got {e})");
                        list.Add((match.Trim(), key));
                    }
                    _props = list.ToArray();
                    _propCache.Clear();
                    break;
                default:
                    throw new FormatException($"{source}: unknown key \"{p.Name}\" (format, maxSurfaces, maxLayerCm, materials, weapons, shotgunPellets, props)");
            }
        }
    }

    private static float Num(JsonElement e, string source, string name) =>
        e.ValueKind == JsonValueKind.Number ? e.GetSingle() : throw new FormatException($"{source}: \"{name}\" must be a number");

    /// <summary>The material's numbers.</summary>
    public static PenMaterial Get(MaterialKey k)
    {
        EnsureLoaded();
        return Mats[(uint)k < (uint)Mats.Length ? (int)k : 0];
    }

    /// <summary>
    /// A weapon's penetration power (the cost of material it can pass, see <see cref="PenMaterial.ResistPerCm"/>): its
    /// "penetration" if the weapon file gives one, else from its class and calibre (Materials/penetration.json "weapons");
    /// a shotgun's pellets get <see cref="PelletPower"/>. Times "penetrationMult" (AP Rounds raise it, hollow points lower it).
    /// </summary>
    public static float PowerOf(WeaponDef def)
    {
        EnsureLoaded();
        if (def.IsLauncher)
            return 0;
        float power;
        if (def.Penetration >= 0)
            power = def.Penetration;
        else if (def.Pellets > 1)
            power = PelletPower;
        else
        {
            var (b, per) = Powers[Math.Min((int)def.Class, Powers.Length - 1)];
            power = b + per * Math.Clamp(def.Caliber, 0f, 1f);
        }
        return MathF.Max(0, power * def.PenetrationMult);
    }

    /// <summary>Power spent passing <paramref name="cm"/> centimetres of a material.</summary>
    public static float Cost(MaterialKey k, float cm) => Get(k).ResistPerCm * MathF.Max(0, cm);

    /// <summary>The damage multiplier for passing a material with <paramref name="before"/> power, <paramref name="after"/> left.</summary>
    public static float Kept(in PenMaterial m, float before, float after) =>
        before <= 0 ? 0 : m.DamageKept * (0.5f + 0.5f * Math.Clamp(after / before, 0f, 1f));

    private static readonly Dictionary<PropDef, MaterialKey> _propCache = new();

    /// <summary>
    /// What a solid prop is made of, for bullets: the first "props" entry whose words appear in its name (the Bocage
    /// hedgerows are earth banks: <see cref="MaterialKey.Hedgerow"/>, never passed), else its biggest textured parts,
    /// else how it breaks (glass, soft, metal, wood), else <see cref="MaterialKey.Default"/>. Cached per prop definition.
    /// </summary>
    public static MaterialKey PropMaterial(PropDef def)
    {
        EnsureLoaded();
        if (_propCache.TryGetValue(def, out var cached))
            return cached;
        var key = PropMaterialUncached(def);
        _propCache[def] = key;
        return key;
    }

    private static MaterialKey PropMaterialUncached(PropDef def)
    {
        foreach (var (match, k) in _props)
            if (HasWords(def.Name, match))
                return k;
        Span<float> volume = stackalloc float[(int)Material.Count];
        foreach (var part in def.Parts)
            if (part.Layer >= 0 && part.Layer < (int)Material.Count)
                volume[part.Layer] += part.Size.X * part.Size.Y * part.Size.Z;
        var best = -1;
        for (var m = 0; m < volume.Length; m++)
            if (volume[m] > 0 && (best < 0 || volume[m] > volume[best]))
                best = m;
        if (best >= 0)
            return MaterialKeys.Of((Material)best);
        return def.Breakable?.Sound switch
        {
            PropBreakSound.Glass => MaterialKey.Glass,
            PropBreakSound.Soft => MaterialKey.Soft,
            PropBreakSound.Metal => MaterialKey.Metal,
            PropBreakSound.Wood => MaterialKey.Wood,
            _ => MaterialKey.Default,
        };
    }

    /// <summary>Does <paramref name="name"/> contain <paramref name="words"/> as whole words (case-insensitive)? "Car" matches "Old Car", not "Carousel".</summary>
    public static bool HasWords(string name, string words)
    {
        var at = 0;
        while ((at = name.IndexOf(words, at, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            var end = at + words.Length;
            if ((at == 0 || !char.IsLetter(name[at - 1])) && (end >= name.Length || !char.IsLetter(name[end])))
                return true;
            at++;
        }
        return false;
    }

    /// <summary>One line for the log and the self-test: where the table came from and a few of its numbers.</summary>
    public static string Describe()
    {
        EnsureLoaded();
        var inv = CultureInfo.InvariantCulture;
        return $"penetration table from {Source}: {MaxSurfaces} surfaces max, wood {Get(MaterialKey.Wood).ResistPerCm.ToString(inv)}/cm, "
               + $"brick {Get(MaterialKey.Brick).ResistPerCm.ToString(inv)}/cm, concrete {Get(MaterialKey.Concrete).ResistPerCm.ToString(inv)}/cm (max {Get(MaterialKey.Concrete).MaxCm.ToString(inv)} cm), {_props.Length} prop rules";
    }
}
