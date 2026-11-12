using System.Text;

namespace CallOfSlop.PartKit;

/// <summary>
/// One part generator: the C# port of a script that was in tools/constructs/ (docs/sandbox/PARTKIT.md). It adds every file it
/// makes to the <see cref="PartOutput"/>, by its path from the repo root with '/' separators, and claims the folders it owns
/// whole (any .json in an owned folder that it didn't make is stale and is removed on a write).
/// </summary>
public interface IPartGenerator
{
    /// <summary>The name on the command line: the old script's, without ".py" (free_parts).</summary>
    string Name { get; }

    void Make(PartOutput o);
}

/// <summary>What a generator makes: the files' text by path, the folders it owns, and where it reads its inputs.</summary>
public sealed class PartOutput(string root)
{
    /// <summary>The repo root inputs are read from (Constructs/Parts/Storage/StorageVolume.json...).</summary>
    public readonly string Root = root;
    public readonly Dictionary<string, string> Files = new(StringComparer.Ordinal);
    public readonly List<string> Owned = [];

    public void Own(string folder)
    {
        if (!Owned.Contains(folder))
            Owned.Add(folder);
    }

    public void File(string path, string text)
    {
        if (!Files.TryAdd(path, text))
            throw new InvalidOperationException($"{path} made twice");
    }

    /// <summary>A file the generator reads, from the repo root.</summary>
    public string Read(string path) => System.IO.File.ReadAllText(Path.Combine(Root, path), Encoding.UTF8);

    /// <summary>A committed JSON file, parsed the way Python's json.load does.</summary>
    public object? ReadJson(string path) => PyJson.Loads(Read(path));
}

/// <summary>The registry and the command line: <c>--gen-parts NAME|all [--out DIR] [--check]</c>.</summary>
public static class PartKit
{
    /// <summary>Every ported generator. A new port goes here (docs/sandbox/PARTKIT.md).</summary>
    public static readonly IPartGenerator[] All =
    [
        new Generators.FreeParts(),
        new Generators.BanditParts(),
        new Generators.MediaParts(),
        new Generators.AmmoParts(),
        new Generators.HighlandParts(),
        new Generators.ShellParts(),
        new Generators.PilotParts(),
        new Generators.CallCentreParts(),
        new Generators.OfficeParts(),
        new Generators.LoreParts(),
        new Generators.BritishParts(),
        new Generators.DoorParts(),
        new Generators.StationParts(),
        new Generators.ShelfParts(),
        new Generators.LinearParts(),
        new Generators.ScoopParts(),
        new Generators.BigDriveParts(),
        new Generators.FluidTankParts(),
        new Generators.AirParts(),
        new Generators.BoostParts(),
        new Generators.SuspensionParts(),
        new Generators.FlightParts(),
        new Generators.DroneParts(),
        new Generators.AirCargo(),
        new Generators.AirWeaponsParts(),
        new Generators.OrdnanceParts(),
        new Generators.HullParts(),
        new Generators.BodyParts(),
        new Generators.WingParts(),
        new Generators.ActuatorParts(),
        new Generators.MakeJointSides(),
        new Generators.ControlParts(),
        new Generators.HydraulicsParts(),
        new Generators.PressParts(),
        new Generators.ExtrusionParts(),
        new Generators.PieceRackParts(),
        new Generators.RecyclerParts(),
        new Generators.PanelFormerParts(),
        new Generators.ConveyorJunctionParts(),
        new Generators.CraftingTerminalParts(),
        new Generators.StructureParts(),
        new Generators.CapitalParts(),
        new Generators.LargeSystems(),
        new Generators.StabiliserParts(),
        new Generators.LegParts(),
        new Generators.UpgradeParts(),
        new Generators.EngineParts(),
        new Generators.BuildingParts(),
        new Generators.InteriorParts(),
        new Generators.MakeRv(),
        new Generators.MachineTiers(),
        new Generators.Assembling(),
        new Generators.ProductionShowcase(), // (a blueprint built through the game's rules from the part files: last)
    ];

    public static IPartGenerator? Find(string name) =>
        All.FirstOrDefault(g => g.Name.Equals(name, StringComparison.OrdinalIgnoreCase) || (g.Name + ".py").Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>The folder above the exe or the working directory that holds Constructs/Parts and tools/: the repo.</summary>
    public static string FindRoot()
    {
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
            for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
                if (Directory.Exists(Path.Combine(dir.FullName, "Constructs", "Parts")) && Directory.Exists(Path.Combine(dir.FullName, "tools")))
                    return dir.FullName;
        return Environment.CurrentDirectory;
    }

    private static StreamWriter? _tty;

    /// <summary>A line to the terminal as well as to callofslop.log (the game sends Console there).</summary>
    private static void Say(string line)
    {
        Console.WriteLine(line);
        _tty ??= new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
        _tty.WriteLine(line);
    }

    public static int Cli(string[] args)
    {
        string? Arg(string name) => args.SkipWhile(a => !a.Equals(name, StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault();
        var check = args.Any(a => a.Equals("--check", StringComparison.OrdinalIgnoreCase));
        var root = FindRoot();
        if (Arg("--roundtrip") is { } rt)
            return RoundTrip(Path.Combine(root, rt));
        if (Arg("--settle-props") is { } propFile)
            return SettleProps(propFile);
        var name = Arg("--gen-parts");
        if (name is null || name.StartsWith("--"))
        {
            Say("--gen-parts NAME|all [--out DIR] [--check]; ported: " + string.Join(", ", All.Select(g => g.Name)));
            return 2;
        }
        var gens = name.Equals("all", StringComparison.OrdinalIgnoreCase) ? All : Find(name) is { } g ? [g] : [];
        if (gens.Length == 0)
        {
            Say($"--gen-parts: no generator {name}; ported: " + string.Join(", ", All.Select(x => x.Name)));
            return 2;
        }
        var outRoot = Arg("--out") is { } od ? Path.GetFullPath(od) : root;
        var bad = 0;
        var firstShown = false;
        foreach (var gen in gens)
        {
            var o = new PartOutput(root);
            gen.Make(o);
            var have = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var folder in o.Owned)
            {
                var full = Path.Combine(outRoot, folder);
                if (!Directory.Exists(full))
                    continue;
                foreach (var f in Directory.EnumerateFiles(full, "*.json", SearchOption.AllDirectories))
                    have[Path.GetRelativePath(outRoot, f).Replace('\\', '/')] = System.IO.File.ReadAllText(f, Encoding.UTF8);
            }
            foreach (var path in o.Files.Keys)
                if (!have.ContainsKey(path) && System.IO.File.Exists(Path.Combine(outRoot, path)))
                    have[path] = System.IO.File.ReadAllText(Path.Combine(outRoot, path), Encoding.UTF8);
            var stale = have.Keys.Where(p => !o.Files.ContainsKey(p)).Order(StringComparer.Ordinal).ToList();
            var changed = o.Files.Where(kv => !have.TryGetValue(kv.Key, out var t) || t != kv.Value).Select(kv => kv.Key).Order(StringComparer.Ordinal).ToList();
            if (check)
            {
                foreach (var p in stale)
                    Say($"stale: {p}");
                foreach (var p in changed)
                {
                    Say($"out of date: {p}");
                    if (!firstShown)
                    {
                        firstShown = true;
                        Say(FirstDifference(have.GetValueOrDefault(p), o.Files[p]));
                    }
                }
                Say($"{gen.Name}: {o.Files.Count} files; {changed.Count} out of date, {stale.Count} stale");
                bad += changed.Count + stale.Count;
                continue;
            }
            foreach (var p in stale)
                System.IO.File.Delete(Path.Combine(outRoot, p));
            foreach (var p in changed)
            {
                var full = Path.Combine(outRoot, p);
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                System.IO.File.WriteAllBytes(full, new UTF8Encoding(false).GetBytes(o.Files[p]));
            }
            Say($"{gen.Name}: {o.Files.Count} files written ({changed.Count} changed, {stale.Count} removed) in {outRoot}");
        }
        return bad;
    }

    /// <summary>"line N" with the committed line and the made one, for the first line that differs.</summary>
    public static string FirstDifference(string? have, string want)
    {
        if (have is null)
            return "  (no such file)";
        var a = have.Split('\n');
        var b = want.Split('\n');
        for (var i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            var x = i < a.Length ? a[i] : "(end of file)";
            var y = i < b.Length ? b[i] : "(end of file)";
            if (x != y)
                return $"  line {i + 1}:\n    committed: {x}\n    made:      {y}";
        }
        return "  (same lines, different bytes)";
    }

    /// <summary>
    /// --gen-parts --settle-props FILE: a prop file's parts (or each prop's "parts" in a list) through <see cref="PartZFight.SettleProps"/>,
    /// printed as json.dumps(indent=2); the same as zfight.settle_props in Python, for a port of the prop generators to check against.
    /// </summary>
    private static int SettleProps(string file)
    {
        var v = PyJson.Loads(System.IO.File.ReadAllText(file, Encoding.UTF8));
        int changed = 0, left = 0;
        void One(PyDict d)
        {
            if (d.Get("parts") is List<object?> parts)
            {
                var (c, l) = PartZFight.SettleProps([.. parts.OfType<PyDict>()]);
                changed += c;
                left += l;
            }
        }
        if (v is PyDict o)
        {
            One(o);
            foreach (var kv in o)
                if (kv.Value is List<object?> list)
                    foreach (var item in list.OfType<PyDict>())
                        One(item);
        }
        Say(PyJson.Dumps(v, 2));
        Say($"settled: {changed} parts changed, {left} hits left");
        return left;
    }

    /// <summary>
    /// --gen-parts --roundtrip DIR: every .json under DIR parsed and written back with indent 2, 1 and none; the writer's own
    /// check (a Python-written file comes back byte for byte in one of them). Prints the files none matches.
    /// </summary>
    private static int RoundTrip(string dir)
    {
        int same = 0, other = 0;
        foreach (var f in Directory.EnumerateFiles(dir, "*.json", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var text = System.IO.File.ReadAllText(f, Encoding.UTF8);
            object? v;
            try
            {
                v = PyJson.Loads(text);
            }
            catch (FormatException)
            {
                continue;
            }
            var body = text.EndsWith('\n') ? text[..^1] : text;
            if (PyJson.Dumps(v, 2) == body || PyJson.Dumps(v, 1) == body || PyJson.Dumps(v) == body || PyJson.Dumps(v, 2, ensureAscii: false) == body
                || v is PyDict pd && (Generators.MachineTiers.Layout(pd) == text
                    || (pd.Get("parts") ?? pd.Get("ops")) is List<object?> && (PropWriter.Lines(pd) == text || PropWriter.Head(pd) == text)))
                same++;
            else
            {
                other++;
                Say($"differs: {Path.GetRelativePath(dir, f)}");
                if (other <= 3)
                    Say(FirstDifference(body, PyJson.Dumps(v, 2)));
            }
        }
        Say($"{same} files written back byte for byte, {other} not (hand-written or another layout)");
        return 0;
    }
}
