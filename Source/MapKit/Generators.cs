using System.Text;

namespace CallOfSlop.MapKit;

/// <summary>
/// A map generator: code that fills a <see cref="Kit"/> with a map. Register it in <see cref="GeneratorRegistry.All"/>
/// and run it with <c>CallOfSlop --gen-map &lt;Name&gt;</c> (docs/mapkit/MAPKIT.md).
/// </summary>
public interface IMapGenerator
{
    /// <summary>The map's name as the lobby shows it ("Twin Shard").</summary>
    string Name { get; }

    /// <summary>The pack folder under Maps ("Space").</summary>
    string Pack { get; }

    /// <summary>The map's number in its pack: the file is Maps/&lt;Pack&gt;/&lt;NN&gt;-&lt;NameWithoutSpaces&gt;.json.</summary>
    int Number { get; }

    void Build(Kit kit);
}

public static class GeneratorRegistry
{
    /// <summary>Every generator, by hand (no reflection: nothing runs at game start-up).</summary>
    public static IReadOnlyList<IMapGenerator> All => _all ??=
    [
        new Generators.TwinShard(),
        new Generators.HordeTest(),
        new Generators.LogicTest(),
    ];

    private static IMapGenerator[]? _all;

    public static IMapGenerator? Find(string name) =>
        All.FirstOrDefault(g => g.Name.Equals(name, StringComparison.OrdinalIgnoreCase) || Slug(g.Name).Equals(name, StringComparison.OrdinalIgnoreCase));

    public static string Slug(string name) => new(name.Where(char.IsLetterOrDigit).ToArray());

    /// <summary>The file a generator writes, relative to the Maps folder ("Space/04-TwinShard.json").</summary>
    // (A Number of 0 means no number, the Dev pack's habit: "Dev/HordeTest.json".)
    public static string RelativeFile(IMapGenerator g) => g.Number > 0 ? $"{g.Pack}/{g.Number:00}-{Slug(g.Name)}.json" : $"{g.Pack}/{Slug(g.Name)}.json";

    /// <summary>Runs a generator and returns its kit.</summary>
    public static Kit Run(IMapGenerator g)
    {
        var kit = new Kit(g.Name, "", 1000);
        g.Build(kit);
        return kit;
    }

    /// <summary>
    /// The repository's Maps folder when the game runs from its build output inside a checkout (bin/Release/net10.0),
    /// otherwise the Maps folder next to the exe.
    /// </summary>
    public static string RepoMapsFolder()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var up = dir; up != null; up = up.Parent)
            if (File.Exists(Path.Combine(up.FullName, "CallOfSlop.csproj")) && Directory.Exists(Path.Combine(up.FullName, "Maps")))
                return Path.Combine(up.FullName, "Maps");
        return Maps.Folder;
    }
}

/// <summary>The command line: --gen-map, --gen-maps and --gen-plan (Program.cs hands the arguments here).</summary>
public static class MapKitCli
{
    /// <summary>
    /// <c>--gen-map &lt;Name&gt; [--out DIR] [--plan]</c>: runs one generator, writes its map file under DIR (default: the
    /// repository's Maps folder), loads it back through the real loader, runs <see cref="MapChecks"/> and prints the report; --plan also draws a plan PNG next to the exe.
    /// Exit code: 0 when the map loads and passes, 1 otherwise.
    /// </summary>
    public static int GenMap(string name, string[] args)
    {
        var g = GeneratorRegistry.Find(name);
        if (g == null)
        {
            Console.WriteLine($"no generator named \"{name}\". Known: {string.Join(", ", GeneratorRegistry.All.Select(x => x.Name))}");
            return 1;
        }
        var outDir = Flag(args, "--out") ?? GeneratorRegistry.RepoMapsFolder();
        var rel = GeneratorRegistry.RelativeFile(g);
        var path = Path.Combine(outDir, rel);
        Kit kit;
        try
        {
            kit = GeneratorRegistry.Run(g);
        }
        catch (MapKitException ex)
        {
            Console.WriteLine($"{g.Name}: {ex.Message}");
            return 1;
        }
        var text = kit.Write();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        Console.WriteLine($"wrote {path}: {kit.Summary()}");
        Map m;
        try
        {
            m = MapFile.Load(text, rel.Replace('\\', '/'));
            m.Validate();
        }
        catch (MapFileException ex)
        {
            Console.WriteLine($"the written map doesn't load: {ex.Message}");
            return 1;
        }
        if (args.Any(a => a.Equals("--plan", StringComparison.OrdinalIgnoreCase)))
        {
            var png = Path.Combine(AppContext.BaseDirectory, Path.GetFileNameWithoutExtension(path) + ".plan.png"); // next to the exe, not in the Maps folder
            Plan.Draw(m, png);
            Console.WriteLine($"plan: {png}");
        }
        // A map's own test block may lower the main-area share (a Zombies map: the outside strip is cut off by the windows).
        var report = MapChecks.Check(m, m.TestSpec is { } t ? new CheckOptions { MainAreaShare = Math.Min(0.75f, t.MinMainNavPct / 100f) } : null);
        Console.WriteLine(report.ToString());
        return report.Failures.Count == 0 ? 0 : 1;
    }

    /// <summary>
    /// <c>--gen-maps [--out DIR]</c>: runs every generator and says which outputs differ from the committed files
    /// (byte for byte; when the bytes differ, whether the loaded maps are still the same). With --out the files are
    /// written there. Exit code: how many differ.
    /// </summary>
    public static int GenMaps(string[] args)
    {
        var outDir = Flag(args, "--out");
        var mapsDir = GeneratorRegistry.RepoMapsFolder();
        var differ = 0;
        foreach (var g in GeneratorRegistry.All)
        {
            var rel = GeneratorRegistry.RelativeFile(g);
            string text;
            try
            {
                text = GeneratorRegistry.Run(g).Write();
            }
            catch (MapKitException ex)
            {
                Console.WriteLine($"{rel}: generator failed: {ex.Message}");
                differ++;
                continue;
            }
            var committed = Path.Combine(mapsDir, rel);
            var verdict = Compare(text, committed, rel);
            Console.WriteLine($"{rel}: {verdict}");
            if (!verdict.StartsWith("identical"))
                differ++;
            if (outDir != null)
            {
                var path = Path.Combine(outDir, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, text);
            }
        }
        Console.WriteLine(differ == 0 ? $"{GeneratorRegistry.All.Count} generator(s), all identical to the committed files" : $"{differ} of {GeneratorRegistry.All.Count} differ");
        return differ;
    }

    /// <summary>How a generated map file relates to a committed one: identical, the same map in different text, or different.</summary>
    public static string Compare(string generated, string committedPath, string rel)
    {
        if (!File.Exists(committedPath))
            return "new (no committed file)";
        // Git may check the file out with CRLF (Windows, `* text=auto`); the kit writes LF, as git stores it.
        var committed = File.ReadAllText(committedPath).Replace("\r\n", "\n");
        if (committed == generated)
            return "identical";
        try
        {
            var a = MapFile.Load(committed, rel);
            var b = MapFile.Load(generated, rel);
            if (a.ContentHash == b.ContentHash && a.Ops.Count == b.Ops.Count && a.Spawns.Count == b.Spawns.Count)
                return $"differs in text only (same map: fingerprint {a.ContentHash:x8}, {a.Ops.Count} ops)";
            return $"DIFFERS: fingerprint {a.ContentHash:x8} -> {b.ContentHash:x8}, {a.Ops.Count} -> {b.Ops.Count} ops";
        }
        catch (MapFileException ex)
        {
            return $"DIFFERS and a version doesn't load: {ex.Message}";
        }
    }

    /// <summary><c>--gen-plan &lt;map file&gt; &lt;png&gt;</c>: a top-down plan of any map file.</summary>
    public static int GenPlan(string mapFile, string png)
    {
        try
        {
            var full = Path.GetFullPath(mapFile);
            var rel = Path.GetRelativePath(Maps.Folder, full);
            var inMaps = !rel.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(rel);
            if (!inMaps)
            {
                var repoRel = Path.GetRelativePath(GeneratorRegistry.RepoMapsFolder(), full);
                if (!repoRel.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(repoRel))
                    (rel, inMaps) = (repoRel, true);
            }
            var m = MapFile.Load(File.ReadAllText(full), inMaps ? rel.Replace('\\', '/') : Path.GetFileName(full), inMaps ? null : "");
            m.Validate();
            Plan.Draw(m, png);
            Console.WriteLine($"plan of '{m.Name}': {png}");
            return 0;
        }
        catch (MapFileException ex)
        {
            Console.WriteLine($"can't load {mapFile}: {ex.Message}");
            return 1;
        }
    }

    private static string? Flag(string[] args, string flag) => args.SkipWhile(a => !a.Equals(flag, StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault(a => !a.StartsWith("--"));
}
