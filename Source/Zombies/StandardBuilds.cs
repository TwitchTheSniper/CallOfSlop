using System.Text.Json;

namespace CallOfSlop;

/// <summary>
/// A standard complete configuration (Parts/Builds/*.json, format "call-of-slop-build/1"): a whole, legal build of a
/// given tier. The mystery box only ever gives these (the user's call: complete builds, never single parts), wall
/// weapons are the plain ones, runs start with the "start" ones, and the workbench loads any of them as a preset.
/// </summary>
public sealed class StandardBuild
{
    public required string Name;
    public Rarity Tier;

    /// <summary>Where it shows up: "box" (mystery box), "wall" (wall weapon), "start" (starting gear of a run).</summary>
    public string[] Sources = [];

    public string Flavor = "";

    /// <summary>The build in its save form (<c>Grip{socket:Part{...}}</c>, or a hilt for a melee build).</summary>
    public required string Tree;

    public string? SourceFile;

    /// <summary>"chainFire" in the file: true = chain fire, false = volley, absent = the grip's default.</summary>
    public bool? ChainFire;

    /// <summary>A fresh copy of the build (parts that no longer exist are dropped and listed).</summary>
    public WeaponBuild Build(Func<string, PartDef?>? find = null, List<string>? dropped = null) =>
        WeaponBuild.Parse($"{Name}|{(ChainFire is { } c ? c ? "rotate" : "volley" : "grip")}|{Tree}", find ?? Parts.Find, dropped);

    public bool IsMelee => Build().IsMelee;
}

/// <summary>Loads and checks the standard builds (Parts/Builds). Every one must be complete and legal with today's parts.</summary>
public static class StandardBuilds
{
    public const string Format = "call-of-slop-build/1";

    /// <summary>Where a standard build can show up.</summary>
    public static readonly string[] SourceNames = ["box", "wall", "start"];

    private static readonly object LoadLock = new();
    private static StandardBuild[]? _all;

    /// <summary>Problems found while loading (bad files are skipped).</summary>
    public static readonly List<string> LoadErrors = new();

    public static string Folder => Path.Combine(Parts.Folder, "Builds");

    /// <summary>Every standard build, by tier then name.</summary>
    public static StandardBuild[] All
    {
        get
        {
            var all = _all;
            if (all != null)
                return all;
            lock (LoadLock)
                return _all ??= LoadFrom(Folder, Parts.Find, LoadErrors);
        }
    }

    /// <summary>Forgets the loaded builds so they're read again on next use (after a part hot reload).</summary>
    public static void Reload()
    {
        lock (LoadLock)
        {
            _all = null;
            LoadErrors.Clear();
        }
    }

    public static StandardBuild? Find(string name) => All.FirstOrDefault(b => b.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>The builds that can come from <paramref name="source"/> ("box", "wall", "start").</summary>
    public static IEnumerable<StandardBuild> From(string source) => All.Where(b => b.Sources.Contains(source));

    /// <summary>Reads one standard build file and checks it assembles into a complete, legal weapon.</summary>
    public static StandardBuild Load(string json, string source, Func<string, PartDef?> find)
    {
        using var doc = WeaponFile.Parse(json, source);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new WeaponFileException($"{source}: a build file is one JSON object {{ \"name\", \"tier\", \"build\", ... }}");
        string? name = null, tree = null, flavor = "";
        Rarity? tier = null;
        bool? chain = null;
        var sources = Array.Empty<string>();
        foreach (var p in root.EnumerateObject())
        {
            var at = $"{source}: \"{p.Name}\"";
            switch (p.Name)
            {
                case "format": WeaponFile.CheckFormat(p.Value, "call-of-slop-build/", Format, at); break;
                case "name": name = WeaponBuild.CleanName(WeaponFile.Text(p.Value, at)); break;
                case "tier": tier = WeaponFile.EnumValue<Rarity>(p.Value, at); break;
                case "source":
                    sources = WeaponFile.TextList(p.Value, at).Select(x => x.ToLowerInvariant()).ToArray();
                    foreach (var s in sources)
                        if (!SourceNames.Contains(s))
                            throw new WeaponFileException($"{at}: \"{s}\" isn't one of {string.Join(", ", SourceNames)}");
                    break;
                case "flavor": flavor = WeaponFile.Text(p.Value, at); break;
                case "build": tree = WeaponFile.Text(p.Value, at).Trim(); break;
                case "chainFire":
                    chain = p.Value.ValueKind switch
                    {
                        JsonValueKind.True => true,
                        JsonValueKind.False => false,
                        _ => throw new WeaponFileException($"{at}: true (receivers fire in turn) or false (all together)"),
                    };
                    break;
                default: throw new WeaponFileException($"{source}: unknown key \"{p.Name}\". Build files know: format, name, tier, source, flavor, build, chainFire");
            }
        }
        if (name == null || tree == null || tier == null)
            throw new WeaponFileException($"{source}: a build file needs \"name\", \"tier\" and \"build\"");
        var b = new StandardBuild { Name = name, Tier = tier.Value, Sources = sources, Flavor = flavor, Tree = tree, SourceFile = source, ChainFire = chain };
        var dropped = new List<string>();
        WeaponBuild build;
        try
        {
            build = b.Build(find, dropped);
        }
        catch (FormatException ex)
        {
            throw new WeaponFileException($"{source} ('{name}'): \"build\" isn't a build: {ex.Message}");
        }
        if (dropped.Count > 0)
            throw new WeaponFileException($"{source} ('{name}'): {string.Join("; ", dropped)}");
        var asm = WeaponBuilder.Assemble(build);
        if (!asm.Ok)
            throw new WeaponFileException($"{source} ('{name}'): not a complete, legal build: {string.Join("; ", asm.Problems.Select(x => x.Message))}");
        return b;
    }

    /// <summary>Loads every *.json in <paramref name="folder"/>; bad files go to <paramref name="errors"/>.</summary>
    public static StandardBuild[] LoadFrom(string folder, Func<string, PartDef?> find, List<string> errors)
    {
        var list = new List<StandardBuild>();
        if (!Directory.Exists(folder))
        {
            errors.Add($"no standard builds folder at {folder}");
            return [];
        }
        foreach (var file in Directory.GetFiles(folder, "*.json", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            var rel = Path.GetRelativePath(folder, file);
            try
            {
                var b = Load(File.ReadAllText(file), rel, find);
                if (list.Any(x => x.Name.Equals(b.Name, StringComparison.OrdinalIgnoreCase)))
                    throw new WeaponFileException($"{rel}: another standard build is already called '{b.Name}'");
                list.Add(b);
            }
            catch (Exception ex) when (ex is WeaponFileException or IOException or UnauthorizedAccessException)
            {
                errors.Add(ex.Message);
                Console.WriteLine($"[builds] skipped: {ex.Message}");
            }
        }
        Console.WriteLine($"[builds] {list.Count} standard build(s) from {folder}");
        return list.OrderBy(b => b.Tier).ThenBy(b => b.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
