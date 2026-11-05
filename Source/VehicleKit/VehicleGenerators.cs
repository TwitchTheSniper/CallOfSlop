namespace CallOfSlop.VehicleKit;

/// <summary>
/// A vehicle generator: code that fills a <see cref="Vehicle"/> for a seed. It is a shape: everything about the look comes from its <see cref="Style"/>, so
/// one shape works for any faction with the slots it asks for. Register it in <see cref="VehicleRegistry.Shapes"/> and run it with <c>CallOfSlop --gen-vehicle &lt;Name&gt;</c>
/// (docs/vehiclekit/VEHICLEKIT.md). The game never runs a generator: it loads the blueprint files in Constructs/Blueprints, so a generated vehicle is salvaged part by part like any other.
/// </summary>
public interface IVehicleGenerator
{
    /// <summary>The vehicle's name, "Style Shape"; a seed's blueprint is "Name seed".</summary>
    string Name { get; }

    /// <summary>The faction's rules: palette, part preferences, what a seed may vary.</summary>
    FactionStyle Style { get; }

    /// <summary>The seeds that ship as blueprints.</summary>
    IReadOnlyList<int> Seeds { get; }

    /// <summary>What the check holds it to; null: the defaults.</summary>
    VehicleCheckOptions? Checks { get; }

    void Build(Vehicle v);
}

/// <summary>The usual generator: a shape's name, a style given by whoever makes it.</summary>
public abstract class VehicleShape(FactionStyle style) : IVehicleGenerator
{
    public abstract string Shape { get; }
    public FactionStyle Style { get; } = style;
    public string Name => $"{Style.Name} {Shape}";
    public abstract IReadOnlyList<int> Seeds { get; }
    public virtual VehicleCheckOptions? Checks => null;
    public abstract void Build(Vehicle v);
}

public static class VehicleRegistry
{
    /// <summary>Every shape, by hand (no reflection: nothing runs at game start-up).</summary>
    public static readonly Func<FactionStyle, IVehicleGenerator>[] Shapes =
    [
        s => new Generators.Technical(s),
        s => new Generators.BoxTruck(s),
        s => new Generators.WarBus(s),
        s => new Generators.FlatbedFort(s),
        s => new Generators.ScrapTanker(s),
        s => new Generators.GunWagon(s),
    ];

    /// <summary>What ships: every shape in every shipped style.</summary>
    public static IReadOnlyList<IVehicleGenerator> All => _all ??= Styles.Shipped.SelectMany(st => Shapes.Select(f => f(st))).ToArray();

    private static IVehicleGenerator[]? _all;

    /// <summary>A generator by name; any style's, so "Militia Box Truck" works though only shipped styles' files are committed.</summary>
    public static IVehicleGenerator? Find(string name) =>
        Styles.All.SelectMany(st => Shapes.Select(f => f(st))).FirstOrDefault(g => g.Name.Equals(name, StringComparison.OrdinalIgnoreCase) || Slug(g.Name).Equals(name, StringComparison.OrdinalIgnoreCase));

    public static string Slug(string name) => new(name.Where(char.IsLetterOrDigit).ToArray());

    public static string BlueprintName(IVehicleGenerator g, int seed) => $"{g.Name} {seed}";

    /// <summary>The file a seed writes, in the Blueprints folder.</summary>
    public static string FileName(IVehicleGenerator g, int seed) => $"{Slug(g.Name)}{seed}.json";

    /// <summary>Runs a generator for a seed: the blueprint's text.</summary>
    public static string Run(IVehicleGenerator g, int seed) => Make(g, seed).Text();

    /// <summary>Runs a generator for a seed: the built vehicle, with what the check needs to know about it.</summary>
    public static Vehicle Make(IVehicleGenerator g, int seed)
    {
        var v = new Vehicle(BlueprintName(g, seed), g.Style, seed);
        g.Build(v);
        return v;
    }

    /// <summary>The repository's Constructs/Blueprints when the game runs from its build output inside a checkout, else the one next to the exe.</summary>
    public static string RepoBlueprints()
    {
        for (var up = new DirectoryInfo(AppContext.BaseDirectory); up != null; up = up.Parent)
            if (File.Exists(Path.Combine(up.FullName, "CallOfSlop.csproj")) && Directory.Exists(Path.Combine(up.FullName, "Constructs", "Blueprints")))
                return Path.Combine(up.FullName, "Constructs", "Blueprints");
        return ConstructBuild.BlueprintFolder;
    }
}

/// <summary>The command line: --gen-vehicle and --gen-vehicles (Program.cs hands the arguments here).</summary>
public static class VehicleKitCli
{
    /// <summary>
    /// <c>--gen-vehicle &lt;Name&gt; [--seed S] [--out DIR] [--plan]</c>: runs a generator (one seed, or all it ships), writes each blueprint under DIR (default: the
    /// repository's Constructs/Blueprints), loads it back, runs <see cref="VehicleChecks"/> and prints the report; --plan also draws a plan PNG next to the exe.
    /// Exit code: how many seeds failed.
    /// </summary>
    public static int GenVehicle(string name, string[] args)
    {
        var g = VehicleRegistry.Find(name);
        if (g == null)
        {
            Console.WriteLine($"no vehicle generator named \"{name}\". Known: {string.Join(", ", VehicleRegistry.All.Select(x => x.Name))}");
            return 1;
        }
        var outDir = Flag(args, "--out") ?? VehicleRegistry.RepoBlueprints();
        IReadOnlyList<int> seeds = g.Seeds;
        if (Flag(args, "--seed") is { } s)
        {
            if (!int.TryParse(s, out var one))
            {
                Console.WriteLine($"--seed {s}: not a whole number");
                return 1;
            }
            seeds = [one];
        }
        var failed = 0;
        foreach (var seed in seeds)
        {
            try
            {
                var v = VehicleRegistry.Make(g, seed);
                var text = v.Text();
                if (text != VehicleRegistry.Run(g, seed))
                {
                    Console.WriteLine($"{g.Name} {seed}: FAIL two runs of one seed wrote different files");
                    failed++;
                    continue;
                }
                var file = VehicleRegistry.FileName(g, seed);
                var path = Path.Combine(outDir, file);
                Directory.CreateDirectory(outDir);
                File.WriteAllText(path, text);
                var report = VehicleChecks.Check(text, file, g.Checks, v.Rooms);
                Console.WriteLine($"wrote {path}");
                Console.WriteLine(report.ToString());
                if (args.Any(a => a.Equals("--plan", StringComparison.OrdinalIgnoreCase)))
                {
                    var png = Path.Combine(AppContext.BaseDirectory, Path.GetFileNameWithoutExtension(file) + ".plan.png");
                    VehiclePlan.Draw(text, file, png);
                    Console.WriteLine($"plan: {png}");
                }
                failed += report.Passed ? 0 : 1;
            }
            catch (Exception ex) when (ex is VehicleKitException or ConstructPartException)
            {
                Console.WriteLine($"{g.Name} {seed}: FAIL {ex.Message}");
                failed++;
            }
        }
        return failed;
    }

    /// <summary>
    /// <c>--sweep-vehicle &lt;Name&gt; [--seeds N]</c>: seeds 1 to N (default 30) built and checked, nothing written: how often a shape fails, and on what. A shipped seed proves itself;
    /// a shape meant to spawn with any seed has to pass here. Exit code: how many failed.
    /// </summary>
    public static int SweepVehicle(string name, string[] args)
    {
        if (VehicleRegistry.Find(name) is not { } g)
        {
            Console.WriteLine($"no vehicle generator named \"{name}\"");
            return 1;
        }
        var n = int.TryParse(Flag(args, "--seeds"), out var want) ? want : 30;
        var failed = 0;
        double make = 0, parts = 0;
        var clock = new System.Diagnostics.Stopwatch();
        for (var seed = 1; seed <= n; seed++)
        {
            try
            {
                clock.Restart();
                var v = VehicleRegistry.Make(g, seed);
                var text = v.Text();
                make += clock.Elapsed.TotalMilliseconds;
                var r = VehicleChecks.Check(text, $"{seed}.json", g.Checks, v.Rooms);
                parts += r.Parts;
                if (!r.Passed)
                {
                    failed++;
                    Console.WriteLine($"seed {seed}: {r.Failures[0]}");
                }
            }
            catch (Exception ex) when (ex is VehicleKitException or ConstructPartException)
            {
                failed++;
                Console.WriteLine($"seed {seed}: {ex.Message}");
            }
        }
        Console.WriteLine($"{g.Name}: {n - failed} of {n} seeds pass, {parts / Math.Max(1, n - failed):0} parts, {make / n:0} ms a generation");
        return failed;
    }

    /// <summary><c>--check-vehicle &lt;blueprint file&gt;</c>: the report for any blueprint, hand-made or generated. A generator's own seed also gets its rooms walked. Exit code: 0 when it passes.</summary>
    public static int CheckVehicle(string file)
    {
        if (!File.Exists(file))
        {
            Console.WriteLine($"no file {file}");
            return 1;
        }
        var text = File.ReadAllText(file);
        foreach (var g in VehicleRegistry.All)
            foreach (var seed in g.Seeds)
                if (Path.GetFileName(file).Equals(VehicleRegistry.FileName(g, seed), StringComparison.OrdinalIgnoreCase))
                {
                    var own = VehicleChecks.Check(text, Path.GetFileName(file), g.Checks, VehicleRegistry.Make(g, seed).Rooms);
                    Console.WriteLine(own.ToString());
                    return own.Passed ? 0 : 1;
                }
        var report = VehicleChecks.Check(text, Path.GetFileName(file));
        Console.WriteLine(report.ToString());
        return report.Passed ? 0 : 1;
    }

    /// <summary><c>--gen-vehicles [--out DIR]</c>: runs every generator's every seed and says which differ from the committed files (with --out, writes them there). Exit code: how many differ.</summary>
    public static int GenVehicles(string[] args)
    {
        var outDir = Flag(args, "--out");
        var folder = VehicleRegistry.RepoBlueprints();
        int total = 0, differ = 0;
        foreach (var g in VehicleRegistry.All)
        foreach (var seed in g.Seeds)
        {
            total++;
            var file = VehicleRegistry.FileName(g, seed);
            try
            {
                var text = VehicleRegistry.Run(g, seed);
                var committed = Path.Combine(folder, file);
                // (git may check a file out with CRLF on Windows; the kit writes LF, as git stores it)
                var verdict = !File.Exists(committed) ? "new (no committed file)" : File.ReadAllText(committed).Replace("\r\n", "\n") == text ? "identical" : "DIFFERS";
                Console.WriteLine($"{file}: {verdict}");
                differ += verdict == "identical" ? 0 : 1;
                if (outDir != null)
                {
                    Directory.CreateDirectory(outDir);
                    File.WriteAllText(Path.Combine(outDir, file), text);
                }
            }
            catch (Exception ex) when (ex is VehicleKitException or ConstructPartException)
            {
                Console.WriteLine($"{file}: generator failed: {ex.Message}");
                differ++;
            }
        }
        Console.WriteLine(differ == 0 ? $"{total} vehicle(s), all identical to the committed files" : $"{differ} of {total} differ");
        return differ;
    }

    private static string? Flag(string[] args, string flag) => args.SkipWhile(a => !a.Equals(flag, StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault(a => !a.StartsWith("--"));
}
