using System.Text.Json;

namespace CallOfSlop;

/// <summary>
/// The building library: every Buildings/&lt;Name&gt;/building.json (with the folder's own Props), plus the furnishing
/// rules in Buildings/Furnishing. Loaded on first use, re-read by <see cref="Maps.Reload"/>. Folders starting with "_"
/// (the _Template) are skipped.
/// </summary>
public static class Buildings
{
    private static readonly object LoadLock = new();
    private static BuildingDef[]? _all;
    private static Dictionary<string, FurnishRule>? _rules;

    /// <summary>Problems found while loading (bad files are skipped and named in the log).</summary>
    public static readonly List<string> LoadErrors = new();

    /// <summary>The folder building folders are loaded from (next to the game).</summary>
    public static string Folder => Path.Combine(AppContext.BaseDirectory, "Buildings");

    /// <summary>The folder name of the furnishing rules (Buildings/Furnishing).</summary>
    public const string FurnishingFolder = "Furnishing";

    /// <summary>Goes up on every <see cref="Reload"/>.</summary>
    public static int Generation { get; private set; }

    /// <summary>Every building, sorted by name.</summary>
    public static BuildingDef[] All
    {
        get
        {
            var all = _all;
            if (all != null)
                return all;
            lock (LoadLock)
            {
                if (_all == null)
                    LoadFolder(Folder);
                return _all!;
            }
        }
    }

    /// <summary>Furnishing rules by room type.</summary>
    public static IReadOnlyDictionary<string, FurnishRule> Rules
    {
        get
        {
            _ = All;
            return _rules!;
        }
    }

    /// <summary>The building called <paramref name="name"/> (or in the folder of that name), or null.</summary>
    public static BuildingDef? Find(string name)
    {
        foreach (var b in All)
            if (b.Name.Equals(name, StringComparison.OrdinalIgnoreCase) || b.Folder.Equals(name, StringComparison.OrdinalIgnoreCase))
                return b;
        return null;
    }

    /// <summary>The rule for a room type, or null.</summary>
    public static FurnishRule? Rule(string type) => Rules.TryGetValue(type, out var r) ? r : null;

    /// <summary>A short hint for an unknown building name.</summary>
    public static string Suggest(string name)
    {
        var close = All.OrderBy(b => Props.Distance(b.Name.ToLowerInvariant(), name.ToLowerInvariant())).Take(3).Select(b => $"\"{b.Name}\"").ToList();
        return close.Count == 0 ? $"the Buildings folder ({Folder}) has no buildings" : $"did you mean {string.Join(", ", close)}?";
    }

    /// <summary>Re-reads the Buildings folder (before the maps that use it).</summary>
    public static void Reload()
    {
        lock (LoadLock)
        {
            LoadFolder(Folder);
            Generation++;
        }
    }

    /// <summary>Loads (or re-loads) the library from <paramref name="folder"/> (the self-test can point it elsewhere).</summary>
    public static void LoadFolder(string folder)
    {
        lock (LoadLock)
        {
            LoadErrors.Clear();
            var list = new List<BuildingDef>();
            var rules = new Dictionary<string, FurnishRule>(StringComparer.OrdinalIgnoreCase);
            if (Directory.Exists(folder))
            {
                var furn = Path.Combine(folder, FurnishingFolder);
                if (Directory.Exists(furn))
                    foreach (var file in Directory.GetFiles(furn, "*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                    {
                        var rel = $"Buildings/{FurnishingFolder}/{Path.GetFileName(file)}";
                        try
                        {
                            var text = File.ReadAllText(file);
                            var rule = BuildingFile.LoadRule(text, rel, Path.GetFileNameWithoutExtension(file));
                            rule.FileHash = Props.CanonicalHash(text);
                            if (rules.ContainsKey(rule.Type))
                                throw new BuildingFileException($"{rel}: another rule is already for room type \"{rule.Type}\"");
                            rules[rule.Type] = rule;
                        }
                        catch (Exception ex) when (ex is BuildingFileException or IOException or JsonException)
                        {
                            LoadErrors.Add(ex.Message);
                            Console.WriteLine($"[buildings] skipped: {ex.Message}");
                        }
                    }
                foreach (var dir in Directory.GetDirectories(folder).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
                {
                    var name = Path.GetFileName(dir);
                    if (name.StartsWith('_') || name.StartsWith('.') || name.Equals(FurnishingFolder, StringComparison.OrdinalIgnoreCase))
                        continue;
                    var file = Path.Combine(dir, "building.json");
                    if (!File.Exists(file))
                        continue;
                    var rel = $"Buildings/{name}/building.json";
                    try
                    {
                        var text = File.ReadAllText(file);
                        var def = BuildingFile.Load(text, rel, name);
                        var hash = Props.CanonicalHash(text);
                        var propDir = Path.Combine(dir, Props.PackFolderName);
                        if (Directory.Exists(propDir))
                        {
                            var errors = new List<string>();
                            var local = Props.LoadAll(propDir, errors);
                            foreach (var e in errors)
                                LoadErrors.Add($"Buildings/{name}/Props: {e}");
                            foreach (var p in local)
                            {
                                var prel = p.SourceFile ?? "";
                                p.Pack = $"Buildings/{name}";
                                p.SourceFile = $"Buildings/{name}/Props/{prel.Replace('\\', '/')}";
                                try
                                {
                                    p.FileHash = Props.CanonicalHash(File.ReadAllText(Path.Combine(propDir, prel)));
                                }
                                catch (Exception ex) when (ex is IOException or JsonException)
                                {
                                    p.FileHash = 1;
                                }
                                hash = hash * 16777619u ^ p.FileHash;
                            }
                            def.LocalProps = local;
                        }
                        var ruleDir = Path.Combine(dir, FurnishingFolder);
                        if (Directory.Exists(ruleDir))
                            foreach (var rf in Directory.GetFiles(ruleDir, "*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                            {
                                var rrel = $"Buildings/{name}/{FurnishingFolder}/{Path.GetFileName(rf)}";
                                var rtext = File.ReadAllText(rf);
                                var rule = BuildingFile.LoadRule(rtext, rrel, Path.GetFileNameWithoutExtension(rf));
                                rule.FileHash = Props.CanonicalHash(rtext);
                                if (!def.LocalRules.TryAdd(rule.Type, rule))
                                    throw new BuildingFileException($"{rrel}: another of this building's rules is already for room type \"{rule.Type}\"");
                                hash = hash * 16777619u ^ rule.FileHash;
                            }
                        def.FileHash = hash == 0 ? 1 : hash;
                        if (list.FirstOrDefault(b => b.Name.Equals(def.Name, StringComparison.OrdinalIgnoreCase)) is { } dup)
                            throw new BuildingFileException($"{rel}: another building is already called '{def.Name}' ({dup.SourceFile})");
                        list.Add(def);
                    }
                    catch (Exception ex) when (ex is BuildingFileException or IOException or JsonException)
                    {
                        LoadErrors.Add(ex.Message);
                        Console.WriteLine($"[buildings] skipped: {ex.Message}");
                    }
                }
            }
            _rules = rules;
            _all = list.OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase).ToArray();
            _libraries.Clear();
            Console.WriteLine($"[buildings] {_all.Length} building(s) and {rules.Count} furnishing rule(s) from {folder}"
                              + (LoadErrors.Count > 0 ? $", {LoadErrors.Count} file(s) skipped" : ""));
        }
    }

    private static readonly Dictionary<string, PropDef[]> _libraries = new();

    /// <summary>
    /// The prop renderer's library for a map that uses building-local props: <paramref name="baseLibrary"/> (the global and
    /// pack props) plus those props. Cached, so the same set gives the same array (the renderer compares references).
    /// </summary>
    public static PropDef[] CombinedLibrary(PropDef[] baseLibrary, IReadOnlyCollection<PropDef> local)
    {
        var extra = local.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase).ToArray();
        var key = $"{System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(baseLibrary)}|{Props.Generation}|{Generation}|{string.Join("|", extra.Select(p => p.Key))}";
        lock (LoadLock)
        {
            if (_libraries.TryGetValue(key, out var hit))
                return hit;
            var lib = baseLibrary.Concat(extra).ToArray();
            for (var i = baseLibrary.Length; i < lib.Length; i++)
                lib[i].Index = i;
            _libraries[key] = lib;
            return lib;
        }
    }
}
