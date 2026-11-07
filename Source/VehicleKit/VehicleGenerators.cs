namespace CallOfSlop.VehicleKit;

/// <summary>
/// A vehicle generator: code that fills a <see cref="Vehicle"/> for a seed. It is a shape: everything about the look comes from its <see cref="Style"/>, so
/// one shape works for any faction with the slots it asks for. Register it in <see cref="VehicleRegistry.Shapes"/> and run it with <c>CallOfSlop --gen-vehicle &lt;Name&gt;</c>
/// (docs/vehiclekit/VEHICLEKIT.md). The game loads the committed blueprint files in Constructs/Blueprints, and a server also runs a generator at spawn time for a new seed
/// (<see cref="VehicleGenJob"/>, "Dynamic spawn"); either way a vehicle is an ordinary construct, salvaged part by part like any other.
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

    /// <summary>What ships: the style's own seeds for this shape when it names some (<see cref="FactionStyle.Seeds"/>), else the shape's.</summary>
    IReadOnlyList<int> IVehicleGenerator.Seeds => Style.Seeds.TryGetValue(Shape, out var own) ? own : Seeds;

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
        s => new Generators.FuelTrailer(s),
        s => new Generators.TankerRig(s),
    ];

    /// <summary>Shapes a style ships only when it names them in <see cref="FactionStyle.Extra"/> (the British style's Double Decker); any style can still run one by name.</summary>
    public static readonly Func<FactionStyle, IVehicleGenerator>[] Extras =
    [
        s => new Generators.DoubleDecker(s),
    ];

    private static IEnumerable<IVehicleGenerator> For(FactionStyle st, bool all) =>
        Shapes.Select(f => f(st)).Concat(Extras.Select(f => f(st)).Where(g => all || g is VehicleShape v && st.Extra.Contains(v.Shape)));

    /// <summary>What ships: every shape in every shipped style.</summary>
    public static IReadOnlyList<IVehicleGenerator> All => _all ??= Styles.Shipped.SelectMany(st => For(st, false)).ToArray();

    private static IVehicleGenerator[]? _all;

    /// <summary>A generator by name; any style's, so "Militia Box Truck" works though only shipped styles' files are committed.</summary>
    public static IVehicleGenerator? Find(string name) =>
        Styles.All.SelectMany(st => For(st, true)).FirstOrDefault(g => g.Name.Equals(name, StringComparison.OrdinalIgnoreCase) || Slug(g.Name).Equals(name, StringComparison.OrdinalIgnoreCase));

    public static string Slug(string name) => new(name.Where(char.IsLetterOrDigit).ToArray());

    public static string BlueprintName(IVehicleGenerator g, int seed) => $"{g.Name} {seed}";

    /// <summary>The file a seed writes, in the Blueprints folder.</summary>
    public static string FileName(IVehicleGenerator g, int seed) => $"{Slug(g.Name)}{seed}.json";

    /// <summary>Runs a generator for a seed: the blueprint's text.</summary>
    public static string Run(IVehicleGenerator g, int seed, VehicleLayers? layers = null) => Make(g, seed, layers: layers).Text();

    /// <summary>
    /// Runs a generator for a seed: the built vehicle, with what the check needs to know about it. <paramref name="kit"/> null: the seed's own
    /// (<see cref="VehicleKitVersion.For"/>: the committed seeds as they were, everything else the shape grammar where the shape has a recipe).
    /// </summary>
    /// <remarks>
    /// <paramref name="layers"/>: kit 3's per-layer seeds (<see cref="VehicleLayers"/>; null: none, or the command line's <c>--layers</c>). With overrides a seed is made by
    /// kit 3 even when its own file is kit 1's (that file is the seed without them); a shape with no recipe has no layers and throws. The command line's <c>--vary LAYER</c>
    /// turns <paramref name="seed"/> into that layer's seed on its base seed, so every tool that loops over seeds loops over one layer.
    /// </remarks>
    public static Vehicle Make(IVehicleGenerator g, int seed, int? kit = null, VehicleLayers? layers = null, RoleAsk? role = null, SpawnContext? context = null)
    {
        if (context == null || ReferenceEquals(context, SpawnContext.Current))
            return MakeHere(g, seed, kit, layers, role);
        using (SpawnContext.Use(context, kitOnly: role?.Model != null)) // (a family's model keeps its body and chassis: only the context's kit changes)
        {
            var v = MakeHere(g, seed, kit, layers, role);
            if (v.Context is { Matters: true } c)
                v.Name += c.Suffix; // (never a committed seed's name, nor the neutral vehicle's)
            return v;
        }
    }

    private static Vehicle MakeHere(IVehicleGenerator g, int seed, int? kit, VehicleLayers? layers, RoleAsk? role)
    {
        var lay = layers ?? VehicleLayers.Force ?? default;
        if (VehicleLayers.Vary is { } vary && layers == null)
            (lay, seed) = (lay.With(vary.Layer, seed), vary.Base);
        var k = kit ?? VehicleKitVersion.For(g, seed);
        if (lay.Any && kit == null)
            k = Math.Max(k, VehicleKitVersion.Layered);
        if (role != null && kit == null && VehicleKitVersion.Force is null or >= VehicleKitVersion.Roles)
            k = VehicleKitVersion.Roles; // (kit 4: a role's vehicle, VehicleRoles.cs; --kit 3 makes the seed without its role)
        if (k >= VehicleKitVersion.Grammar && ShapeGrammar.RecipeOf(g) is { } recipe)
        {
            if (lay.Any && k < VehicleKitVersion.Layered)
                throw new VehicleKitException($"kit {k} has no layers (kit 3 has)");
            return ShapeGrammar.Make(g, seed, recipe, k, lay, role);
        }
        if (lay.Any)
            throw new VehicleKitException($"the {g.Name} has no layers: only the shape grammar's shapes do (kit 3)");
        if (role != null && k >= VehicleKitVersion.Roles)
            return VehicleRoleMake.Kit1(g, seed, role); // (a shape the grammar doesn't make: its generator, with the role's part weights, measured and tried again)
        var v = new Vehicle(BlueprintName(g, seed), g.Style, seed);
        g.Build(v);
        var shipsNowhere = ShapeGrammar.RecipeOf(g) == null && (kit ?? VehicleKitVersion.Force ?? (g.Seeds.Contains(seed) ? VehicleKitVersion.Legacy : VehicleKitVersion.Current)) >= VehicleKitVersion.Grammar;
        if (shipsNowhere)
            v.GuaranteeMarks(); // (a shape the grammar doesn't make is kit 1, but its seeds that ship nowhere get kit 2's marks: VehicleMarks.cs)
        if (shipsNowhere || v.Context is { Matters: true })
            v.DriveGate(); // (and the spawn's drive check, as any vehicle no file holds: a seed that fails it rerolls in the spawner)
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
                var file = VehicleRegistry.Slug(v.Name) + ".json"; // (FileName's, or with kit 3's overridden layers in it: never a committed seed's file)
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
    /// a shape meant to spawn with any seed has to pass here. It also compares seeds (<see cref="VehicleVariety"/>): the most different ones it found, and whether the shipped ones are far enough apart. Exit code: how many failed.
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
        var passed = new List<VehicleVariety.Signature>();
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
                else
                    passed.Add(VehicleVariety.Of(seed, v));
            }
            catch (Exception ex) when (ex is VehicleKitException or ConstructPartException)
            {
                failed++;
                Console.WriteLine($"seed {seed}: {ex.Message}");
            }
        }
        Console.WriteLine($"{g.Name}: {n - failed} of {n} seeds pass, {parts / Math.Max(1, n - failed):0} parts, {make / n:0} ms a generation");
        var spread = VehicleVariety.Spread(passed, g.Seeds.Count);
        Console.WriteLine($"most different {spread.Count}: {string.Join(" ", spread.Select(s => s.Seed))}, closest pair {VehicleVariety.Closest(spread).D:0.00}");
        try
        {
            var shipped = VehicleVariety.Closest(g.Seeds.Select(s => VehicleVariety.Of(s, VehicleRegistry.Make(g, s))).ToList());
            Console.WriteLine($"shipped seeds: closest pair {shipped.A} and {shipped.B}, {shipped.D:0.00} (at least {VehicleVariety.Least:0.00})");
            if (shipped.D < VehicleVariety.Least)
                failed++;
        }
        catch (Exception ex) when (ex is VehicleKitException or ConstructPartException)
        {
            failed++;
            Console.WriteLine($"a shipped seed doesn't build: {ex.Message}");
        }
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
