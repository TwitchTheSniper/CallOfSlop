using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CallOfSlop;

/// <summary>
/// The data part files are checked against (Parts/_Registry.json, format "call-of-slop-part-registry/1"): the socket
/// types (what a part's "mount" and a socket's "type" may be), the calibre classes ("bores"), and for every category
/// its default mount and display name. Adding a socket type or a bore is a data change: list it here and use it in part
/// files. The socket types the rules in code rely on (<see cref="CoreSocketTypes"/>) can't be removed, and the category
/// set itself is fixed in code (<see cref="PartCategory"/>). No registry file: <see cref="BuiltIn"/>, the same lists the
/// game always had.
/// </summary>
public sealed class PartRegistry
{
    public const string Format = "call-of-slop-part-registry/1";

    /// <summary>Socket types the code itself refers to (rules, stats, model sockets): every registry must keep them.</summary>
    public static readonly string[] CoreSocketTypes = ["receiver", "barrel", "feed", "ammo", "stock", "foregrip", "optic", "muzzle", "hopper", "head", "action"];

    /// <summary>Every socket type, in registry order.</summary>
    public required string[] SocketTypes;

    /// <summary>Every calibre class, in registry order.</summary>
    public required string[] Bores;

    /// <summary>
    /// How each calibre class adapts a barrel fitted under a receiver of that bore (barrels fit every receiver; the
    /// receiver decides the size and how much the barrel's own modifiers count). Bores the file says nothing about get
    /// <see cref="BoreClass.Default"/>.
    /// </summary>
    public readonly Dictionary<string, BoreClass> Classes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The barrel adaptation of <paramref name="bore"/> (the built-in table's, else <see cref="BoreClass.Default"/>).</summary>
    public BoreClass ClassOf(string bore) =>
        Classes.TryGetValue(bore, out var c) ? c : BuiltInClasses.TryGetValue(bore, out var b) ? b : BoreClass.Default;

    /// <summary>
    /// The bore table the game ships (also in Parts/_Registry.json): how each bore adapts a barrel (pistol smaller, heavy
    /// bigger) and the calibre it fires (base damage, range, pellets, fall-off and headshot multiplier: what the receiver
    /// gives a round before the ammo part's own modifiers; docs/zombies/WEAPON-BUILDER.md, "Universal ammo").
    /// </summary>
    public static readonly Dictionary<string, BoreClass> BuiltInClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        ["pistol"] = new(new Vector3(0.8f, 0.8f, 0.7f), 0.85f, false, new(20, 15f, 1, 0.6f, 2f)),
        ["rifle"] = new(Vector3.One, 1f, false, new(26, 28f, 1, 0.6f, 2f)),
        ["heavy"] = new(new Vector3(1.35f, 1.35f, 1.3f), 1.15f, false, new(85, 120f, 1, 0.8f, 2f)),
        ["shell"] = new(new Vector3(1.5f, 1.5f, 1f), 1f, true, new(14, 9f, 8, 0.2f, 2f)),
        ["launcher"] = new(new Vector3(2.7f, 2.7f, 1.3f), 0.5f, false, new(120, 40f, 1, 0.6f, 1f)),
        ["energy"] = new(new Vector3(1f, 1f, 1.2f), 1f, false, new(30, 40f, 1, 0.6f, 2f)),
    };

    /// <summary>A line about each socket type and bore (for --check-parts, the docs and tools); may be empty.</summary>
    public readonly Dictionary<string, string> About = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<PartCategory, (string Mount, string Display)> _categories = new();

    /// <summary>The registry file it came from (relative to the Parts folder), or null for <see cref="BuiltIn"/>.</summary>
    public string? Source;

    /// <summary>The mount a category gets when a part file doesn't say.</summary>
    public string MountOf(PartCategory c) => _categories.TryGetValue(c, out var v) ? v.Mount : PartFile.BuiltInMount(c);

    /// <summary>A category's display name ("Receiver", "Damage Multiplier"...).</summary>
    public string DisplayOf(PartCategory c) => _categories.TryGetValue(c, out var v) ? v.Display : PartDef.BuiltInCategoryName(c);

    /// <summary>The registry the game had before Parts/_Registry.json (used when the file is missing).</summary>
    public static PartRegistry BuiltIn { get; } = MakeBuiltIn();

    private static PartRegistry MakeBuiltIn()
    {
        var r = new PartRegistry
        {
            SocketTypes = ["receiver", "barrel", "feed", "ammo", "stock", "foregrip", "optic", "muzzle", "utility", "upgrade", "amp", "hopper", "head", "mod", "action", "fuse", "payload", "gmod"],
            Bores = ["pistol", "rifle", "heavy", "shell", "launcher", "energy"],
        };
        foreach (var c in Enum.GetValues<PartCategory>())
            r._categories[c] = (PartFile.BuiltInMount(c), PartDef.BuiltInCategoryName(c));
        foreach (var (k, v) in BuiltInClasses)
            r.Classes[k] = v;
        return r;
    }

    /// <summary>Reads a registry file. Throws <see cref="WeaponFileException"/> with a readable message if anything is wrong.</summary>
    public static PartRegistry Load(string json, string source)
    {
        using var doc = WeaponFile.Parse(json, source);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new WeaponFileException($"{source}: the registry is one JSON object {{ \"socketTypes\": [...], \"bores\": [...], \"categories\": [...] }}");
        string[]? sockets = null, bores = null;
        var about = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var classes = new Dictionary<string, BoreClass>(StringComparer.OrdinalIgnoreCase);
        var cats = new Dictionary<PartCategory, (string Mount, string Display)>();
        JsonElement? catList = null;
        foreach (var p in root.EnumerateObject())
            switch (p.Name.ToLowerInvariant())
            {
                case "format": WeaponFile.CheckFormat(p.Value, "call-of-slop-part-registry/", Format, $"{source}: \"format\""); break;
                case "about": WeaponFile.Text(p.Value, $"{source}: \"about\""); break;
                case "sockettypes": sockets = Names(p.Value, $"{source}: \"socketTypes\"", about); break;
                case "bores": bores = Names(p.Value, $"{source}: \"bores\"", about, classes); break;
                case "categories": catList = p.Value.Clone(); break;
                default: throw new WeaponFileException($"{source}: unknown key \"{p.Name}\" (a registry has format, about, socketTypes, bores, categories)");
            }
        if (sockets == null || sockets.Length == 0)
            throw new WeaponFileException($"{source}: missing \"socketTypes\"");
        if (bores == null || bores.Length == 0)
            throw new WeaponFileException($"{source}: missing \"bores\"");
        foreach (var core in CoreSocketTypes)
            if (!sockets.Contains(core))
                throw new WeaponFileException($"{source}: \"socketTypes\" must keep \"{core}\" (the rules in code use it); the core types are {string.Join(", ", CoreSocketTypes)}");
        if (catList is not { ValueKind: JsonValueKind.Array } list)
            throw new WeaponFileException($"{source}: missing \"categories\" (a list of {{ \"name\", \"mount\", \"display\" }}, one for each of {string.Join(", ", Enum.GetNames<PartCategory>())})");
        var i = 0;
        foreach (var e in list.EnumerateArray())
        {
            var where = $"{source}: category #{++i}";
            if (e.ValueKind != JsonValueKind.Object)
                throw new WeaponFileException($"{where}: expected {{ \"name\": \"barrel\", \"mount\": \"barrel\", \"display\": \"Barrel\" }}");
            PartCategory? cat = null;
            string? mount = null, display = null;
            foreach (var p in e.EnumerateObject())
                switch (p.Name.ToLowerInvariant())
                {
                    case "name": cat = WeaponFile.EnumValue<PartCategory>(p.Value, $"{where}.name (categories are fixed in code)"); break;
                    case "mount": mount = WeaponFile.Text(p.Value, $"{where}.mount").ToLowerInvariant(); break;
                    case "display": display = WeaponFile.Text(p.Value, $"{where}.display").Trim(); break;
                    case "about": WeaponFile.Text(p.Value, $"{where}.about"); break;
                    default: throw new WeaponFileException($"{where}: unknown key \"{p.Name}\" (known: name, mount, display, about)");
                }
            if (cat is not { } c)
                throw new WeaponFileException($"{where}: missing \"name\"");
            where = $"{source}: category '{c}'";
            if (cats.ContainsKey(c))
                throw new WeaponFileException($"{where} is listed twice");
            mount ??= PartFile.BuiltInMount(c);
            if (mount != "" && !sockets.Contains(mount))
                throw new WeaponFileException($"{where}: \"mount\" \"{mount}\" isn't in \"socketTypes\"");
            if (c is PartCategory.Grip or PartCategory.Hilt or PartCategory.Receiver or PartCategory.Action or PartCategory.Casing && mount != PartFile.BuiltInMount(c))
                throw new WeaponFileException($"{where}: \"mount\" must stay \"{PartFile.BuiltInMount(c)}\" (the rules in code build weapons on it)");
            if (display is { Length: 0 or > 24 })
                throw new WeaponFileException($"{where}: \"display\" must be 1-24 characters");
            cats[c] = (mount, display ?? PartDef.BuiltInCategoryName(c));
        }
        foreach (var c in Enum.GetValues<PartCategory>())
            if (!cats.ContainsKey(c))
                throw new WeaponFileException($"{source}: \"categories\" is missing '{c.ToString().ToLowerInvariant()}' (every category in code needs an entry: {string.Join(", ", Enum.GetNames<PartCategory>()).ToLowerInvariant()})");
        var r = new PartRegistry { SocketTypes = sockets, Bores = bores, Source = source };
        foreach (var (k, v) in about)
            r.About[k] = v;
        foreach (var (k, v) in classes)
            r.Classes[k] = v;
        foreach (var (k, v) in cats)
            r._categories[k] = v;
        return r;
    }

    /// <summary>
    /// A list of names: "name" strings or {"name", "about"} objects (lower-case letters, digits and _). Bores
    /// (<paramref name="classes"/> given) may also say how they adapt barrels: "barrelScale" [width, height, length],
    /// "barrelStrength" (how much a barrel's modifiers count) and "barrelPellets" (whether barrels may change pellets),
    /// and the calibre they fire: "calibre" {damage, range, pellets, minDamageMult, headMult}.
    /// </summary>
    private static string[] Names(JsonElement e, string where, Dictionary<string, string> about, Dictionary<string, BoreClass>? classes = null)
    {
        if (e.ValueKind != JsonValueKind.Array)
            throw new WeaponFileException($"{where}: expected a list like [\"barrel\", {{ \"name\": \"bayonet\", \"about\": \"...\" }}]");
        var names = new List<string>();
        foreach (var x in e.EnumerateArray())
        {
            string name;
            if (x.ValueKind == JsonValueKind.Object)
            {
                string? n = null, a = "";
                Vector3? scale = null;
                float? strength = null;
                bool? pellets = null;
                CalibreInfo? calibre = null;
                foreach (var p in x.EnumerateObject())
                    switch (p.Name.ToLowerInvariant())
                    {
                        case "name": n = WeaponFile.Text(p.Value, where); break;
                        case "about": a = WeaponFile.Text(p.Value, where); break;
                        case "barrelscale" when classes != null: scale = BoreClass.ReadScale(p.Value, $"{where}: \"barrelScale\""); break;
                        case "barrelstrength" when classes != null:
                            strength = WeaponFile.Number(p.Value, $"{where}: \"barrelStrength\"");
                            if (strength is < 0 or > 3)
                                throw new WeaponFileException($"{where}: \"barrelStrength\" must be between 0 and 3 (1 = the barrel's modifiers as written)");
                            break;
                        case "barrelpellets" when classes != null:
                            pellets = p.Value.ValueKind switch
                            {
                                JsonValueKind.True => true,
                                JsonValueKind.False => false,
                                _ => throw new WeaponFileException($"{where}: \"barrelPellets\" is true or false"),
                            };
                            break;
                        case "calibre" when classes != null: calibre = CalibreInfo.Read(p.Value, $"{where}: \"calibre\""); break;
                        default:
                            throw new WeaponFileException($"{where}: unknown key \"{p.Name}\" (known: name, about{(classes != null ? ", barrelScale, barrelStrength, barrelPellets, calibre" : "")})");
                    }
                name = n ?? throw new WeaponFileException($"{where}: an entry needs a \"name\"");
                about[name] = a;
                if (classes != null && (scale != null || strength != null || pellets != null || calibre != null))
                {
                    var d = PartRegistry.BuiltInClasses.TryGetValue(name, out var bi) ? bi : BoreClass.Default;
                    classes[name] = new BoreClass(scale ?? d.Scale, strength ?? d.Strength, pellets ?? d.Pellets, calibre ?? d.Calibre);
                }
            }
            else
                name = WeaponFile.Text(x, where);
            if (name.Length is 0 or > 24 || !name.All(ch => ch is >= 'a' and <= 'z' or >= '0' and <= '9' or '_') || !char.IsAsciiLetterLower(name[0]))
                throw new WeaponFileException($"{where}: \"{name}\" should be a short lower-case word (letters, digits, _)");
            if (names.Contains(name))
                throw new WeaponFileException($"{where}: \"{name}\" is listed twice");
            names.Add(name);
        }
        return names.ToArray();
    }
}

/// <summary>
/// How a calibre class adapts a barrel hung under a receiver of that bore (docs/zombies/WEAPON-BUILDER.md, "Barrels and
/// receivers"): <see cref="Scale"/> sizes the barrel's model (width, height, length; 1 = as drawn for a rifle),
/// <see cref="Strength"/> scales the barrel's own stat modifiers (a x1.4 range barrel at strength 0.5 gives x1.2), and
/// <see cref="Pellets"/> says whether barrels may change the pellet count (only shotguns: a choke on a rifle shouldn't
/// double its bullets), and <see cref="Calibre"/> is the round a receiver of that bore fires before its ammo part's
/// modifiers (ammo is universal: it picks the round type, the receiver the calibre).
/// </summary>
public readonly record struct BoreClass(Vector3 Scale, float Strength, bool Pellets, CalibreInfo Calibre)
{
    public static readonly BoreClass Default = new(Vector3.One, 1f, false, CalibreInfo.Default);

    /// <summary>Reads a [width, height, length] barrel scale (each 0.2 to 4).</summary>
    public static Vector3 ReadScale(JsonElement e, string where)
    {
        if (e.ValueKind != JsonValueKind.Array || e.GetArrayLength() != 3)
            throw new WeaponFileException($"{where}: expected [width, height, length], e.g. [0.8, 0.8, 0.7]");
        var v = new Vector3(WeaponFile.Number(e[0], where), WeaponFile.Number(e[1], where), WeaponFile.Number(e[2], where));
        if (v.X is < 0.2f or > 4f || v.Y is < 0.2f or > 4f || v.Z is < 0.2f or > 4f)
            throw new WeaponFileException($"{where}: each scale must be between 0.2 and 4");
        return v;
    }
}

/// <summary>
/// What a bore's rounds do before the ammo part changes them (docs/zombies/WEAPON-BUILDER.md, "Universal ammo"): the
/// damage per projectile, full-damage range, projectiles per round (shells fire 8 pellets), damage left at long range
/// and the headshot multiplier. A receiver's own stats apply on top (a revolver's headMult), then the ammo's relative
/// modifiers.
/// </summary>
public readonly record struct CalibreInfo(int Damage, float RangeM, int Pellets, float MinDamageMult, float HeadMult)
{
    public static readonly CalibreInfo Default = new(20, 25f, 1, 0.6f, 2f);

    /// <summary>Reads {"damage": 26, "range": 28, "pellets": 1, "minDamageMult": 0.6, "headMult": 2} (every key optional, defaults as <see cref="Default"/>).</summary>
    public static CalibreInfo Read(JsonElement e, string where)
    {
        if (e.ValueKind != JsonValueKind.Object)
            throw new WeaponFileException($"{where}: expected {{ \"damage\": 26, \"range\": 28, \"pellets\": 1, \"minDamageMult\": 0.6, \"headMult\": 2 }}");
        var c = Default;
        foreach (var p in e.EnumerateObject())
        {
            var at = $"{where}.{p.Name}";
            switch (p.Name.ToLowerInvariant())
            {
                case "damage": c = c with { Damage = (int)Check(WeaponFile.Int(p.Value, at), 1, 1000, at) }; break;
                case "range": c = c with { RangeM = Check(WeaponFile.Number(p.Value, at), 0.5f, 1000, at) }; break;
                case "pellets": c = c with { Pellets = (int)Check(WeaponFile.Int(p.Value, at), 1, 30, at) }; break;
                case "mindamagemult": c = c with { MinDamageMult = Check(WeaponFile.Number(p.Value, at), 0, 1, at) }; break;
                case "headmult": c = c with { HeadMult = Check(WeaponFile.Number(p.Value, at), 0.5f, 10, at) }; break;
                default: throw new WeaponFileException($"{where}: unknown key \"{p.Name}\" (known: damage, range, pellets, minDamageMult, headMult)");
            }
        }
        return c;
    }

    private static float Check(float v, float min, float max, string where) =>
        v < min || v > max ? throw new WeaponFileException($"{where}: must be between {WeaponFile.Num(min)} and {WeaponFile.Num(max)}") : v;
}

/// <summary>A reusable model piece from Parts/_Fragments (format "call-of-slop-fragment/1"): a name and a list of boxes.</summary>
public sealed class PartFragment
{
    public const string Format = "call-of-slop-fragment/1";

    public required string Name;

    /// <summary>The file it came from, relative to the Parts folder.</summary>
    public required string Source;

    public string About = "";

    /// <summary>The fragment's "model" list, unread (read where it's used, so nested fragments resolve in the user's library).</summary>
    public required JsonElement Model;
}

/// <summary>
/// What part files can build on, read from the Parts folder before the parts themselves (Zombies weapon builder, docs
/// "Making parts"):
/// <list type="bullet">
/// <item>the registry (<see cref="PartRegistry"/>, Parts/_Registry.json);</item>
/// <item>model fragments (Parts/_Fragments/*.json): a model entry <c>{ "use": "Picatinny Rail", "pos": [...], "rot":
/// [...], "scale": 1.2, "color": "accent" }</c> brings the fragment's boxes, scaled (in the fragment's own axes), turned,
/// then moved. "color", "alpha" and "tag" recolour or retag every box it brings. Fragments may use other fragments;</item>
/// <item>templates: <c>"extends": "Base Barrel"</c> starts the part from another part or a base (Parts/_Bases/*.json,
/// never in the game themselves, and allowed to be incomplete). The part's own keys replace the base's; objects
/// (receiver, frame, split...) merge key by key; null removes a key; "+model", "+sockets", "+stats", "+muzzles",
/// "+flags" and "+bores" append to the base's list ("+sockets" replaces a base socket of the same name); "-sockets",
/// "-stats", "-flags" and "-bores" remove entries by name.</item>
/// </list>
/// Everything resolves at load time into ordinary <see cref="PartDef"/>s, so the game, the network (which sends build
/// text) and the fingerprint see no difference between a part written out in full and one built from pieces.
/// </summary>
public sealed class PartLibrary
{
    /// <summary>Folder (under Parts) of model fragments.</summary>
    public const string FragmentFolder = "_Fragments";

    /// <summary>Folder (under Parts) of base templates for "extends".</summary>
    public const string BaseFolder = "_Bases";

    /// <summary>Folder (under Parts) of the commented template and examples (checked by the self-test, never loaded as parts).</summary>
    public const string TemplateFolder = "_Template";

    /// <summary>The registry file (under Parts).</summary>
    public const string RegistryFile = "_Registry.json";

    /// <summary>How deep fragments may use fragments, and bases extend bases.</summary>
    public const int MaxDepth = 8;

    public PartRegistry Registry = PartRegistry.BuiltIn;

    public readonly Dictionary<string, PartFragment> Fragments = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Base templates by name: (file, text).</summary>
    public readonly Dictionary<string, (string Source, string Text)> Bases = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Every part file, (relative path, text), in path order (what <see cref="Parts.LoadDetailed"/> loads).</summary>
    public readonly List<(string Source, string Text)> PartTexts = new();

    /// <summary>Part files by part name (for "extends" on a real part).</summary>
    private readonly Dictionary<string, (string Source, string Text)> _partsByName = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The Parts folder it was read from (null for an empty library).</summary>
    public string? Folder;

    /// <summary>A library with only the built-in registry (no fragments or bases).</summary>
    public static PartLibrary Empty => new();

    /// <summary>
    /// Reads the registry, fragments, bases and part file texts under <paramref name="folder"/>. Problems go to
    /// <paramref name="errors"/> (a broken fragment or base is skipped; a broken registry falls back to the built-in one).
    /// </summary>
    public static PartLibrary FromFolder(string folder, List<string> errors)
    {
        var lib = new PartLibrary { Folder = folder };
        if (!Directory.Exists(folder))
            return lib;
        var reg = Path.Combine(folder, RegistryFile);
        if (File.Exists(reg))
            try
            {
                lib.Registry = PartRegistry.Load(File.ReadAllText(reg), RegistryFile);
            }
            catch (Exception ex) when (ex is WeaponFileException or IOException or UnauthorizedAccessException)
            {
                errors.Add(ex.Message + " (using the built-in socket types and bores)");
                Console.WriteLine($"[parts] registry: {ex.Message}");
            }
        foreach (var file in Directory.GetFiles(folder, "*.json", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            var rel = Path.GetRelativePath(folder, file).Replace('\\', '/');
            var top = rel.Split('/')[0];
            var isFragment = top.Equals(FragmentFolder, StringComparison.OrdinalIgnoreCase);
            var isBase = top.Equals(BaseFolder, StringComparison.OrdinalIgnoreCase);
            if (!isFragment && !isBase && Parts.IsDataFile(rel))
                continue; // Builds, Tables, the registry, templates
            string text;
            try
            {
                text = File.ReadAllText(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                errors.Add($"{rel}: couldn't read the file ({ex.Message})");
                continue;
            }
            try
            {
                if (isFragment)
                    lib.AddFragment(text, rel);
                else if (isBase)
                    lib.AddBase(text, rel);
                else
                    lib.AddPartText(text, rel);
            }
            catch (WeaponFileException ex)
            {
                errors.Add(ex.Message);
                Console.WriteLine($"[parts] skipped: {ex.Message}");
            }
        }
        return lib;
    }

    /// <summary>Adds a part file's text (its name is indexed for "extends" when it parses; errors show up when it loads).</summary>
    public void AddPartText(string text, string source)
    {
        PartTexts.Add((source, text));
        try
        {
            using var doc = WeaponFile.Parse(text, source);
            if (doc.RootElement.ValueKind == JsonValueKind.Object && Get(doc.RootElement, "name") is { ValueKind: JsonValueKind.String } n)
                _partsByName.TryAdd(n.GetString()!.Trim(), (source, text));
        }
        catch (WeaponFileException)
        {
            // reported when the part itself loads
        }
    }

    /// <summary>Adds a fragment file. Throws <see cref="WeaponFileException"/> if it's broken or its name is taken.</summary>
    public PartFragment AddFragment(string text, string source)
    {
        using var doc = WeaponFile.Parse(text, source);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new WeaponFileException($"{source}: a fragment file is one JSON object {{ \"name\": ..., \"model\": [...] }}");
        string? name = null, about = "";
        JsonElement? model = null;
        foreach (var p in root.EnumerateObject())
            switch (p.Name.ToLowerInvariant())
            {
                case "format": WeaponFile.CheckFormat(p.Value, "call-of-slop-fragment/", PartFragment.Format, $"{source}: \"format\""); break;
                case "name": name = WeaponFile.Text(p.Value, $"{source}: \"name\"").Trim(); break;
                case "about": about = WeaponFile.Text(p.Value, $"{source}: \"about\""); break;
                case "model": model = p.Value.Clone(); break;
                default: throw new WeaponFileException($"{source}: unknown key \"{p.Name}\" (a fragment has format, name, about, model)");
            }
        if (string.IsNullOrEmpty(name) || name.Length > 40)
            throw new WeaponFileException($"{source}: a fragment needs a \"name\" (1-40 characters)");
        if (model is not { ValueKind: JsonValueKind.Array } m || m.GetArrayLength() == 0)
            throw new WeaponFileException($"{source} ('{name}'): a fragment needs a \"model\": a list of boxes (or other fragments' \"use\")");
        if (Fragments.TryGetValue(name, out var dup))
            throw new WeaponFileException($"{source}: another fragment is already called '{name}' ({dup.Source})");
        var f = new PartFragment { Name = name, Source = source, About = about, Model = m };
        Fragments[name] = f;
        return f;
    }

    /// <summary>Adds a base template. Throws <see cref="WeaponFileException"/> if it isn't a JSON object with a name, or the name is taken.</summary>
    public void AddBase(string text, string source)
    {
        using var doc = WeaponFile.Parse(text, source);
        if (doc.RootElement.ValueKind != JsonValueKind.Object || Get(doc.RootElement, "name") is not { ValueKind: JsonValueKind.String } n || n.GetString()!.Trim().Length == 0)
            throw new WeaponFileException($"{source}: a base is a part file (a JSON object) with a \"name\" that parts can \"extends\"");
        var name = n.GetString()!.Trim();
        if (Bases.TryGetValue(name, out var dup))
            throw new WeaponFileException($"{source}: another base is already called '{name}' ({dup.Source})");
        Bases[name] = (source, text);
    }

    /// <summary>A property of a JSON object by name, ignoring case (null if missing).</summary>
    public static JsonElement? Get(JsonElement obj, string key)
    {
        foreach (var p in obj.EnumerateObject())
            if (p.Name.Equals(key, StringComparison.OrdinalIgnoreCase))
                return p.Value;
        return null;
    }

    // ---- Fragments -----------------------------------------------------------------------------------------------

    /// <summary>The boxes of one <c>{ "use": ... }</c> model entry, placed in the part's space.</summary>
    internal IEnumerable<PartBox> Use(JsonElement e, string where, int depth)
    {
        string? name = null;
        Vector3 pos = default, rotDeg = default, scale = Vector3.One;
        var turned = false;
        GunColor? color = null;
        float? alpha = null;
        string? tag = null;
        foreach (var p in e.EnumerateObject())
        {
            var at = $"{where}.{p.Name}";
            switch (p.Name.ToLowerInvariant())
            {
                case "use": name = WeaponFile.Text(p.Value, at).Trim(); break;
                case "pos": pos = PartFile.ReadVec(p.Value, at); break;
                case "rot": rotDeg = PartFile.ReadAngles(p.Value, at); turned = rotDeg != Vector3.Zero; break;
                case "scale":
                    scale = p.Value.ValueKind == JsonValueKind.Number
                        ? new Vector3(PartFile.ReadRange(p.Value, 0.05f, 20, at))
                        : p.Value is { ValueKind: JsonValueKind.Array } sv && sv.GetArrayLength() == 3
                            ? new Vector3(PartFile.ReadRange(sv[0], 0.05f, 20, at), PartFile.ReadRange(sv[1], 0.05f, 20, at), PartFile.ReadRange(sv[2], 0.05f, 20, at))
                            : throw new WeaponFileException($"{at}: expected a number like 1.5 or [x, y, z]");
                    break;
                case "color": color = PartFile.ReadColor(p.Value, at); break;
                case "alpha": alpha = PartFile.ReadRange(p.Value, 0.05f, 1, at); break;
                case "tag": tag = PartFile.ReadTag(p.Value, at); break;
                default: throw new WeaponFileException($"{where}: unknown key \"{p.Name}\" (a fragment use knows: use, pos, rot, scale, color, alpha, tag)");
            }
        }
        if (string.IsNullOrEmpty(name))
            throw new WeaponFileException($"{where}: \"use\" names a fragment from Parts/{FragmentFolder}");
        if (!Fragments.TryGetValue(name, out var f))
            throw new WeaponFileException($"{where}: no model fragment called '{name}'"
                                          + (Fragments.Count == 0 ? $" (there are none: fragments live in Parts/{FragmentFolder})" : $" (fragments: {string.Join(", ", Fragments.Keys.Order().Take(30))})"));
        if (depth >= MaxDepth)
            throw new WeaponFileException($"{where}: fragments nest more than {MaxDepth} deep (does '{name}' use itself?)");
        var boxes = PartFile.ReadModel(f.Model, $"{where} -> fragment '{f.Name}' ({f.Source})", depth + 1);
        var q = PartDef.Rot(rotDeg);
        foreach (var b in boxes)
        {
            b.Size = Vector3.Abs(b.Size * scale);
            b.Pos = Vector3.Transform(b.Pos * scale, q) + pos;
            if (turned)
                b.Orient = Quaternion.Normalize(q * b.Rotation); // the box's own turn first, then the use's
            if (color is { } c)
                b.Color = c;
            if (alpha is { } a)
                b.Alpha = a;
            if (tag != null)
                b.Tag = tag;
            if (b.Size.X is < 0.001f or > 1.5f || b.Size.Y is < 0.001f or > 1.5f || b.Size.Z is < 0.001f or > 1.5f)
                throw new WeaponFileException($"{where} ('{name}'): with \"scale\" a box is {Fmt(b.Size)} m; each size must be between 0.001 and 1.5 m");
            if (MathF.Abs(b.Pos.X) > 2 || MathF.Abs(b.Pos.Y) > 2 || MathF.Abs(b.Pos.Z) > 2)
                throw new WeaponFileException($"{where} ('{name}'): a box lands at {Fmt(b.Pos)}, more than 2 m from the part's origin");
        }
        return boxes;
    }

    private static string Fmt(Vector3 v) => $"[{WeaponFile.Num(v.X)}, {WeaponFile.Num(v.Y)}, {WeaponFile.Num(v.Z)}]";

    // ---- extends -------------------------------------------------------------------------------------------------

    /// <summary>List keys a part that extends another can append to ("+model") or remove from ("-sockets").</summary>
    public static readonly string[] AppendKeys = ["model", "sockets", "stats", "muzzles", "flags", "bores"];

    public static readonly string[] RemoveKeys = ["sockets", "stats", "flags", "bores"];

    /// <summary>A file without "extends" can't use "+model" or "-sockets" (there's nothing to add to).</summary>
    internal static void CheckNoMergeKeys(JsonElement root, string source)
    {
        if (root.ValueKind != JsonValueKind.Object)
            return;
        foreach (var p in root.EnumerateObject())
            if (p.Name.Length > 1 && p.Name[0] is '+' or '-')
                throw new WeaponFileException($"{source}: \"{p.Name}\" only works in a part that \"extends\" another (it adds to or removes from the base's \"{p.Name[1..]}\")");
    }

    /// <summary>
    /// The part file <paramref name="root"/> with its "extends" chain merged in, as JSON text for <see cref="PartFile"/>
    /// (errors name <paramref name="source"/> and the chain).
    /// </summary>
    internal string ResolveExtends(JsonElement root, string source) => Resolve(root, source, []).ToJsonString();

    private JsonObject Resolve(JsonElement root, string source, List<string> chain)
    {
        var obj = ToObject(root, source);
        if (!obj.TryGetPropertyValue("extends", out var ext))
        {
            CheckNoMergeKeys(root, source);
            obj.Remove("abstract");
            return obj;
        }
        if (ext is not JsonValue ev || !ev.TryGetValue<string>(out var baseName) || baseName.Trim().Length == 0)
            throw new WeaponFileException($"{source}: \"extends\" names a part or a base (Parts/{BaseFolder}), like \"extends\": \"Standard Barrel\"");
        baseName = baseName.Trim();
        var me = obj.TryGetPropertyValue("name", out var nm) && nm is JsonValue nv && nv.TryGetValue<string>(out var ns) ? ns.Trim() : null;
        if (chain.Count == 0 && me == null)
            throw new WeaponFileException($"{source}: a part that \"extends\" '{baseName}' still needs its own \"name\"");
        var link = me ?? source;
        if (chain.Contains(baseName, StringComparer.OrdinalIgnoreCase) || baseName.Equals(link, StringComparison.OrdinalIgnoreCase))
            throw new WeaponFileException($"{source}: \"extends\" goes round in a circle ({string.Join(" -> ", chain.Append(link).Append(baseName))})");
        if (chain.Count >= MaxDepth)
            throw new WeaponFileException($"{source}: \"extends\" chains more than {MaxDepth} deep ({string.Join(" -> ", chain.Append(link))})");
        (string Source, string Text) found;
        if (Bases.TryGetValue(baseName, out var b))
            found = b;
        else if (_partsByName.TryGetValue(baseName, out var pt))
            found = pt;
        else
            throw new WeaponFileException($"{source}: \"extends\": no part or base called '{baseName}'"
                                          + (Bases.Count > 0 ? $" (bases: {string.Join(", ", Bases.Keys.Order())})" : $" (bases live in Parts/{BaseFolder})"));
        using var baseDoc = WeaponFile.Parse(found.Text, found.Source);
        if (baseDoc.RootElement.ValueKind != JsonValueKind.Object)
            throw new WeaponFileException($"{found.Source}: not a JSON object (extended by {source})");
        var parent = Resolve(baseDoc.RootElement, found.Source, [.. chain, link]);
        return Merge(parent, obj, $"{source} (extends '{baseName}')");
    }

    /// <summary>JSON element to a mutable object (keys compared ignoring case; a key given twice is an error).</summary>
    private static JsonObject ToObject(JsonElement root, string source)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in root.EnumerateObject())
            if (!seen.Add(p.Name))
                throw new WeaponFileException($"{source}: \"{p.Name}\" is given twice");
        try
        {
            return JsonNode.Parse(root.GetRawText(), new JsonNodeOptions { PropertyNameCaseInsensitive = true },
                       new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) as JsonObject
                   ?? throw new WeaponFileException($"{source}: expected a JSON object");
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            throw new WeaponFileException($"{source}: {ex.Message}");
        }
    }

    private static JsonObject Merge(JsonObject parent, JsonObject child, string where)
    {
        var result = (JsonObject)parent.DeepClone();
        result.Remove("abstract");
        // 1. Removals ("-sockets": ["utility"]) from the base's lists.
        foreach (var (k, v) in child)
        {
            if (!k.StartsWith('-'))
                continue;
            var list = k[1..];
            if (!RemoveKeys.Contains(list, StringComparer.OrdinalIgnoreCase))
                throw new WeaponFileException($"{where}: \"{k}\" isn't a list you can remove from (use {string.Join(", ", RemoveKeys.Select(x => $"-{x}"))})");
            var names = v is JsonArray va ? va.Select(x => x is JsonValue xv && xv.TryGetValue<string>(out var s) ? s : null).ToList()
                : v is JsonValue sv && sv.TryGetValue<string>(out var one) ? [one] : null;
            if (names == null || names.Any(n => n == null))
                throw new WeaponFileException($"{where}: \"{k}\" is a name or a list of names");
            if (result[list] is not JsonArray arr)
                throw new WeaponFileException($"{where}: \"{k}\": the base has no \"{list}\"");
            foreach (var n in names.Select(x => x!))
            {
                var idx = IndexOf(arr, list, n);
                if (idx < 0)
                    throw new WeaponFileException($"{where}: \"{k}\": the base has no {list[..^1]} \"{n}\" (it has: {string.Join(", ", arr.Select(x => KeyOf(x, list)))})");
                arr.RemoveAt(idx);
            }
        }
        // 2. Replacements: the part's own keys win; objects merge key by key; null removes.
        foreach (var (k, v) in child)
        {
            if (k.StartsWith('+') || k.StartsWith('-') || k.Equals("extends", StringComparison.OrdinalIgnoreCase) || k.Equals("abstract", StringComparison.OrdinalIgnoreCase))
                continue;
            if (v == null)
                result.Remove(k);
            else if (v is JsonObject vo && result[k] is JsonObject ro)
                result[k] = MergeObjects(ro, vo);
            else
                result[k] = v.DeepClone();
        }
        // 3. Appends ("+model": [...]); "+sockets" replaces a base socket with the same name.
        foreach (var (k, v) in child)
        {
            if (!k.StartsWith('+'))
                continue;
            var list = k[1..];
            if (!AppendKeys.Contains(list, StringComparer.OrdinalIgnoreCase))
                throw new WeaponFileException($"{where}: \"{k}\" isn't a list you can add to (use {string.Join(", ", AppendKeys.Select(x => $"+{x}"))})");
            if (v is not JsonArray add)
                throw new WeaponFileException($"{where}: \"{k}\" is a list of entries to add");
            if (result[list] is not JsonArray arr)
                result[list] = arr = new JsonArray();
            foreach (var item in add)
            {
                var copy = item?.DeepClone();
                var idx = list.Equals("sockets", StringComparison.OrdinalIgnoreCase) && copy != null ? IndexOf(arr, list, KeyOf(copy, list)) : -1;
                if (idx >= 0)
                    arr[idx] = copy;
                else
                    arr.Add(copy);
            }
        }
        return result;
    }

    private static JsonObject MergeObjects(JsonObject a, JsonObject b)
    {
        var r = (JsonObject)a.DeepClone();
        foreach (var (k, v) in b)
            if (v == null)
                r.Remove(k);
            else if (v is JsonObject vo && r[k] is JsonObject ro)
                r[k] = MergeObjects(ro, vo);
            else
                r[k] = v.DeepClone();
        return r;
    }

    /// <summary>The name an entry of a list goes by: a socket's "name", a stat change's "stat", a flag or bore itself.</summary>
    private static string KeyOf(JsonNode? item, string list) => item switch
    {
        JsonObject o when list.Equals("sockets", StringComparison.OrdinalIgnoreCase) => o["name"]?.ToString() ?? "",
        JsonObject o when list.Equals("stats", StringComparison.OrdinalIgnoreCase) => o["stat"]?.ToString() ?? "",
        JsonValue v when v.TryGetValue<string>(out var s) => s,
        _ => item?.ToJsonString() ?? "",
    };

    private static int IndexOf(JsonArray arr, string list, string name)
    {
        for (var i = 0; i < arr.Count; i++)
            if (KeyOf(arr[i], list).Equals(name, StringComparison.OrdinalIgnoreCase))
                return i;
        return -1;
    }
}
