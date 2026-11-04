using System.Text.Json;
using System.Text.Json.Nodes;
using CallOfSlop.Space;

namespace CallOfSlop;

/// <summary>A mod that can't be used, or a mod file that can't be applied, with the reason.</summary>
public sealed class ModException(string message) : Exception(message);

/// <summary>
/// Mods (phase 6 of the space plan, docs/mods/MODS.md: the architecture's "surgical patching"). A mod is a folder in
/// <c>Mods/</c> next to the game's data with a <c>mod.json</c> (name, description, order, enabled) and files laid out
/// like the game's own data: <c>Systems/*.json</c> (star systems), <c>Constructs/Parts/**/*.json</c> (construct parts),
/// <c>Constructs/Blueprints/*.json</c>. Each file in a mod either
/// <list type="bullet">
/// <item><b>adds</b> something new (a name no base file or earlier mod has),</item>
/// <item><b>replaces</b> something by name (<c>"override": true</c> and the whole file), or</item>
/// <item><b>patches</b> it (<c>"patch": "Name"</c> and only what changes: objects merge key by key, <c>null</c> removes a key,
/// lists of named objects merge by name, <c>{"name": "X", "remove": true}</c> takes one out, other lists are replaced).</item>
/// </list>
/// Mods apply in their order (then by folder name), each on what the ones before left. The result goes through the
/// data's own loader, so every check a base file meets, a modded one meets too, and a bad mod file is refused with the
/// mod's name and the reason, leaving the rest. Star systems are hashed after mods, so peers with different mods see that
/// their galaxies differ (Galaxy.Hash).
/// </summary>
public static class Mods
{
    public const string FolderName = "Mods", ManifestName = "mod.json";

    public sealed record Mod(string Name, string Folder, int Order, string Description, string Version);

    /// <summary>One data file after mods: the name it is known by (for messages), its folder relative to the data root's kind folder, its text, and which mod last touched it ("" for none).</summary>
    public readonly record struct DataFile(string File, string RelFolder, string Text, string Mod);

    private static List<Mod>? _active;
    private static readonly List<string> _errors = [];

    /// <summary>Where the game looks for mods (the data folder's <c>Mods</c>; the self-test and <c>--check-mods</c> point elsewhere).</summary>
    public static string Root = Path.Combine(AppFolders.Data, FolderName);

    /// <summary>The enabled mods in the order they apply.</summary>
    public static IReadOnlyList<Mod> Active
    {
        get
        {
            lock (_errors) // (the loaders' Lazy values can start on worker threads together)
                return _active ??= Discover(Root, _errors);
        }
    }

    /// <summary>Mods and mod files that were refused, with the reason.</summary>
    public static IReadOnlyList<string> Errors
    {
        get
        {
            _ = Active;
            return _errors;
        }
    }

    /// <summary>Forget what was found (tests, <c>--check-mods</c>): the next use looks again.</summary>
    public static void Reload(string? root = null)
    {
        lock (_errors)
        {
            if (root != null)
                Root = root;
            _active = null;
            _errors.Clear();
        }
    }

    /// <summary>
    /// <c>--check-mods [DIR]</c>: the mods under DIR (default <see cref="Root"/>), what each file of theirs does, then the
    /// star systems, construct parts and blueprints loaded with them; prints refusals; returns how many there were.
    /// </summary>
    public static int CheckCli(string? dir)
    {
        Reload(dir != null ? Path.GetFullPath(dir) : null);
        Console.WriteLine($"Mods in {Root}:");
        var mods = Active;
        foreach (var m in mods)
            Console.WriteLine($"OK  mod {m.Name}{(m.Version.Length > 0 ? " " + m.Version : "")} (order {m.Order}, {Path.GetFileName(m.Folder)}){(m.Description.Length > 0 ? ": " + m.Description : "")}");
        var problems = new List<string>(Errors);
        var data = AppFolders.Data;
        foreach (var (kind, folder, rec) in new[] { (StarSystemFile.FolderName, Path.Combine(data, StarSystemFile.FolderName), false),
                     ("Constructs/Parts", ConstructParts.Folder, true), ("Constructs/Blueprints", ConstructBuild.BlueprintFolder, false) })
        {
            var scratch = new List<string>(); // (the loaders below report these again, with their own checks)
            var baseCount = BaseFiles(folder, rec).Count;
            var files = Resolve(BaseFiles(folder, rec), mods, kind, rec, scratch);
            for (var i = 0; i < files.Count; i++)
                if (files[i].Mod.Length > 0)
                    Console.WriteLine($"OK  {kind}: {files[i].File} {(i < baseCount ? "changed" : "added")} by {files[i].Mod}");
        }
        var systems = new List<string>();
        foreach (var e in StarSystems.LoadFolder(Path.Combine(data, StarSystemFile.FolderName), systems, mods))
            Console.WriteLine($"OK  system {e.System.Name} at {e.System.GalaxyLy.X:0.##}, {e.System.GalaxyLy.Y:0.##}, {e.System.GalaxyLy.Z:0.##} ly ({e.System.File}, hash {e.Hash:x8})");
        problems.AddRange(systems);
        Console.WriteLine($"OK  {ConstructParts.All.Count} construct parts, {ConstructBuild.Blueprints.Count} blueprints");
        problems.AddRange(ConstructParts.Refused);
        problems.AddRange(ConstructBuild.RefusedBlueprints);
        foreach (var p in problems.Distinct())
            Console.WriteLine($"PROBLEM {p}");
        var n = problems.Distinct().Count();
        Console.WriteLine(n == 0 ? "No problems." : $"{n} problem(s).");
        return n;
    }

    private static readonly string[] ManifestKeys = ["name", "description", "order", "enabled", "version"];

    /// <summary>Every mod folder under a root with a good mod.json, enabled ones only, in order.</summary>
    public static List<Mod> Discover(string root, List<string> errors)
    {
        var list = new List<Mod>();
        if (!Directory.Exists(root))
            return list;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in Directory.GetDirectories(root).OrderBy(d => Path.GetFileName(d), StringComparer.Ordinal))
        {
            var folder = Path.GetFileName(dir);
            var manifest = Path.Combine(dir, ManifestName);
            try
            {
                if (!File.Exists(manifest))
                    throw new ModException($"{folder}: no {ManifestName}");
                var node = Parse(File.ReadAllText(manifest), $"{folder}/{ManifestName}") as JsonObject
                           ?? throw new ModException($"{folder}/{ManifestName}: should be an object");
                foreach (var (k, _) in node)
                    if (!ManifestKeys.Contains(k))
                        throw new ModException($"{folder}/{ManifestName}: unknown key \"{k}\" (known: {string.Join(", ", ManifestKeys)})");
                var name = Str(node, "name") ?? throw new ModException($"{folder}/{ManifestName}: needs a \"name\"");
                if (node["enabled"] is { } en && en.GetValueKind() is not (JsonValueKind.True or JsonValueKind.False))
                    throw new ModException($"{folder}/{ManifestName}: \"enabled\" is true or false");
                if (node["enabled"]?.GetValue<bool>() == false)
                    continue;
                var order = 0;
                if (node["order"] is { } o)
                {
                    if (o.GetValueKind() != JsonValueKind.Number || !o.AsValue().TryGetValue(out order) || order is < -1000 or > 1000)
                        throw new ModException($"{folder}/{ManifestName}: \"order\" is a whole number from -1000 to 1000");
                }
                if (!names.Add(name))
                    throw new ModException($"{folder}/{ManifestName}: another mod is already called \"{name}\"");
                list.Add(new Mod(name, dir, order, Str(node, "description") ?? "", Str(node, "version") ?? ""));
            }
            catch (Exception ex) when (ex is ModException or IOException)
            {
                errors.Add(ex.Message);
                Console.WriteLine($"[mods] refused: {ex.Message}");
            }
        }
        list.Sort((a, b) => a.Order != b.Order ? a.Order.CompareTo(b.Order) : string.CompareOrdinal(Path.GetFileName(a.Folder), Path.GetFileName(b.Folder)));
        foreach (var m in list)
            Console.WriteLine($"[mods] {m.Name}{(m.Version.Length > 0 ? " " + m.Version : "")} (order {m.Order}, {Path.GetFileName(m.Folder)})");
        return list;
    }

    /// <summary>
    /// The data files of one kind after mods. <paramref name="baseFiles"/> are the game's own (in its load order);
    /// <paramref name="kind"/> is the folder under each mod (e.g. "Systems", "Constructs/Parts"), <paramref name="recursive"/>
    /// whether its subfolders count. A file's name is its <c>"name"</c> (or, with none, its file name without .json).
    /// Overrides and patches keep the base file's place in the order; new files come after, mod by mod. Refusals go to
    /// <paramref name="errors"/>.
    /// </summary>
    /// <remarks>
    /// <paramref name="check"/> is the loader's own parse of one file (text, name), throwing when it refuses it: each mod's
    /// change is checked as it is made, and a refused change is dropped with the reason, leaving what was there before (the
    /// base file or an earlier mod's version), so a bad patch never takes a shipped part or system away.
    /// </remarks>
    public static List<DataFile> Resolve(List<DataFile> baseFiles, IReadOnlyList<Mod> mods, string kind, bool recursive, List<string> errors, Action<string, string>? check = null)
    {
        var files = new List<DataFile>(baseFiles);
        if (mods.Count == 0)
            return files;
        var byName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < files.Count; i++)
            if (NameOf(files[i].Text, files[i].File) is { } n)
                byName.TryAdd(n, i);
        foreach (var mod in mods)
        {
            var dir = Path.Combine(mod.Folder, kind.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(dir))
                continue;
            var found = Directory.GetFiles(dir, "*.json", recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
                .OrderBy(f => Path.GetRelativePath(dir, f).Replace('\\', '/'), StringComparer.Ordinal);
            foreach (var path in found)
            {
                var rel = Path.GetRelativePath(dir, path).Replace('\\', '/');
                var where = $"mod {mod.Name}: {kind}/{rel}";
                try
                {
                    var node = Parse(File.ReadAllText(path), where);
                    var relFolder = (Path.GetDirectoryName(rel) ?? "").Replace('\\', '/');
                    if (node is JsonObject obj && obj["patch"] is { } target)
                    {
                        if (target.GetValueKind() != JsonValueKind.String || string.IsNullOrWhiteSpace(target.GetValue<string>()))
                            throw new ModException($"{where}: \"patch\" names what it patches");
                        var name = target.GetValue<string>().Trim();
                        if (!byName.TryGetValue(name, out var at))
                            throw new ModException($"{where}: there is nothing called \"{name}\" to patch");
                        var into = Parse(files[at].Text, files[at].File) as JsonObject ?? throw new ModException($"{where}: \"{name}\" isn't an object");
                        obj.Remove("patch");
                        Merge(into, obj, where);
                        var newName = Str(into, "name");
                        if (newName != null && !newName.Equals(name, StringComparison.OrdinalIgnoreCase))
                            throw new ModException($"{where}: a patch can't rename \"{name}\" (add a new one instead)");
                        var patched = into.ToJsonString(Options);
                        Check(check, patched, files[at].File, where);
                        files[at] = files[at] with { Text = patched, Mod = mod.Name };
                        continue;
                    }
                    var own = NameOf(node, Path.GetFileNameWithoutExtension(path));
                    var replace = node is JsonObject o2 && o2["override"] is { } ov && ov.GetValueKind() == JsonValueKind.True;
                    if (node is JsonObject o3)
                        o3.Remove("override");
                    var text = node!.ToJsonString(Options);
                    if (own != null && byName.TryGetValue(own, out var old))
                    {
                        if (!replace)
                            throw new ModException($"{where}: \"{own}\" already exists ({files[old].File}): give \"override\": true to replace it, or \"patch\": \"{own}\" to change parts of it");
                        Check(check, text, files[old].File, where);
                        files[old] = files[old] with { Text = text, Mod = mod.Name };
                        continue;
                    }
                    if (replace)
                        throw new ModException($"{where}: \"override\" but there is no \"{own}\" to replace");
                    if (own != null)
                        byName[own] = files.Count;
                    Check(check, text, $"{mod.Name}/{rel}", where);
                    files.Add(new DataFile($"{mod.Name}/{rel}", relFolder, text, mod.Name));
                }
                catch (Exception ex) when (ex is ModException or IOException)
                {
                    errors.Add(ex.Message);
                    Console.WriteLine($"[mods] refused: {ex.Message}");
                }
            }
        }
        return files;
    }

    private static void Check(Action<string, string>? check, string text, string file, string where)
    {
        if (check == null)
            return;
        try
        {
            check(text, file);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            throw new ModException($"{where}: {ex.Message} (left as it was)");
        }
    }

    /// <summary>The game's own files of a kind as <see cref="DataFile"/>s (sorted as the loaders always have).</summary>
    public static List<DataFile> BaseFiles(string folder, bool recursive)
    {
        var list = new List<DataFile>();
        if (!Directory.Exists(folder))
            return list;
        foreach (var f in Directory.GetFiles(folder, "*.json", recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
                     .OrderBy(f => recursive ? Path.GetRelativePath(folder, f).Replace('\\', '/') : Path.GetFileName(f), recursive ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase))
        {
            var rel = Path.GetRelativePath(folder, f).Replace('\\', '/');
            list.Add(new DataFile(Path.GetFileName(f), (Path.GetDirectoryName(rel) ?? "").Replace('\\', '/'), File.ReadAllText(f), ""));
        }
        return list;
    }

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <summary>
    /// A merge patch: keys of <paramref name="patch"/> go into <paramref name="into"/>: null removes, objects merge, lists of
    /// named objects merge by name (a <c>"remove": true</c> entry takes one out, new names go on the end), anything else replaces.
    /// </summary>
    public static void Merge(JsonObject into, JsonObject patch, string where)
    {
        foreach (var (key, value) in patch.ToList())
        {
            if (value == null)
            {
                into.Remove(key);
                continue;
            }
            var old = into[key];
            if (value is JsonObject po && old is JsonObject oo)
            {
                Merge(oo, po, $"{where}: {key}");
                continue;
            }
            if (value is JsonArray pa && old is JsonArray oa && Named(pa) && Named(oa) && pa.Count > 0)
            {
                MergeList(oa, pa, $"{where}: {key}");
                continue;
            }
            into[key] = value.DeepClone();
        }
    }

    private static void MergeList(JsonArray into, JsonArray patch, string where)
    {
        foreach (var e in patch)
        {
            var po = (JsonObject)e!;
            var name = Str(po, "name")!;
            var at = -1;
            for (var i = 0; i < into.Count; i++)
                if (Str((JsonObject)into[i]!, "name") is { } n && n.Equals(name, StringComparison.OrdinalIgnoreCase))
                    at = i;
            var remove = po["remove"] is { } r && r.GetValueKind() == JsonValueKind.True;
            if (remove)
            {
                if (at < 0)
                    throw new ModException($"{where}: there is no \"{name}\" to remove");
                into.RemoveAt(at);
                continue;
            }
            if (at >= 0)
                Merge((JsonObject)into[at]!, po, $"{where}: {name}");
            else
                into.Add(po.DeepClone());
        }
    }

    /// <summary>Is every element an object with a "name" (a list that merges by name)?</summary>
    private static bool Named(JsonArray a)
    {
        foreach (var e in a)
            if (e is not JsonObject o || Str(o, "name") == null)
                return false;
        return true;
    }

    private static JsonNode? Parse(string text, string where)
    {
        try
        {
            return JsonNode.Parse(text, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        }
        catch (JsonException ex)
        {
            throw new ModException($"{where}: not valid JSON ({ex.Message})");
        }
    }

    private static string? NameOf(string text, string fallback)
    {
        try
        {
            return NameOf(Parse(text, fallback), Path.GetFileNameWithoutExtension(fallback));
        }
        catch (ModException)
        {
            return null; // (the loader refuses it with its own reason)
        }
    }

    private static string? NameOf(JsonNode? node, string fallback) => node is JsonObject o ? Str(o, "name") ?? fallback : fallback;

    private static string? Str(JsonObject o, string key) =>
        o[key] is { } v && v.GetValueKind() == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetValue<string>()) ? v.GetValue<string>().Trim() : null;
}
