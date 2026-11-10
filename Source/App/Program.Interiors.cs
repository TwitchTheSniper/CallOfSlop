using CallOfSlop.Interiors;
using CallOfSlop.VehicleKit;

namespace CallOfSlop;

public static unsafe partial class Program
{
    /// <summary>
    /// <c>--gen-interior &lt;blueprint name or file&gt; [--seed S] [--out DIR] [--plan]</c>: the interior planner and builder (docs/sandbox/INTERIORS.md).
    /// Replays the blueprint through the build rules, plans an interior for its deck by its tonnage class, builds it and writes
    /// "&lt;Name&gt; Fitted" under DIR (default: the repository's Constructs/Blueprints), loads it back and prints the check. --plan prints the
    /// ASCII deck maps and the room list. Exit code: the check's failures.
    /// </summary>
    private static int GenInterior(string target, string[] args)
    {
        string? Flag(string f) => args.SkipWhile(a => !a.Equals(f, StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal));
        var seed = 1;
        if (Flag("--seed") is { } s && !int.TryParse(s, out seed))
        {
            Console.WriteLine($"--seed {s}: not a whole number");
            return 1;
        }
        InteriorData data;
        try
        {
            data = InteriorData.Load(InteriorData.Folder);
        }
        catch (InteriorDataException e)
        {
            Console.WriteLine($"interior data refused: {e.Message}");
            return 1;
        }
        if (data.UnknownParts() is { Count: > 0 } unknown)
            Console.WriteLine($"note: parts the data names that this build lacks: {string.Join(", ", unknown)}");
        foreach (var pr in ConstructLoot.Problems.Where(pr => pr.StartsWith("Loot.json: room ", StringComparison.Ordinal)))
            Console.WriteLine($"note: a room loot table item left out: {pr}");
        foreach (var hook in data.LootHooks.Values.OfType<string>().Distinct())
            if (!ConstructLoot.Tables.TryGetValue(hook, out var lt))
                Console.WriteLine($"note: loot hook table \"{hook}\" isn't in Loot.json");
            else
                Console.WriteLine($"loot table \"{hook}\": {lt.Items.Length} items, {lt.RollsMin}-{lt.RollsMax} rolls, {lt.Empty:0%} empty, tier {lt.MaxTier}");
        ConstructBuild.Blueprint bp;
        if (File.Exists(target))
            bp = ConstructBuild.ParseBlueprint(File.ReadAllText(target), Path.GetFileName(target));
        else if (ConstructBuild.FindBlueprint(target) is { } named)
            bp = named;
        else
        {
            Console.WriteLine($"no blueprint file or name \"{target}\"");
            return 1;
        }
        var clock = System.Diagnostics.Stopwatch.StartNew();
        InteriorBuilder b;
        InteriorPlan plan;
        try
        {
            b = InteriorBuilder.From(data, bp);
            plan = InteriorPlanner.Plan(data, b.Class!, b.Hull!, seed);
        }
        catch (VehicleKitException e)
        {
            Console.WriteLine($"{bp.Name}: FAIL {e.Message}");
            return 1;
        }
        var planMs = clock.Elapsed.TotalMilliseconds;
        Console.WriteLine($"{bp.Name}: {b.Tonnes:0} t, class {b.Class!.Name}{(data.ProvisionalClasses ? " (provisional bands)" : "")}, hull {b.Hull!.Size.X:0}x{b.Hull.Size.Z:0} m, {b.Hull.KeepOuts.Count} parts checked as keep-outs");
        if (args.Any(a => a.Equals("--plan", StringComparison.OrdinalIgnoreCase)))
            Console.Write(plan.Ascii());
        var lines = InteriorPlanner.Check(plan, data, out var failures);
        try
        {
            b.Build(plan, seed);
        }
        catch (VehicleKitException e)
        {
            Console.WriteLine($"build: FAIL {e.Message}");
            failures++;
        }
        var text = b.Text();
        var buildMs = clock.Elapsed.TotalMilliseconds - planMs;
        var outDir = Flag("--out") ?? VehicleRegistry.RepoBlueprints();
        Directory.CreateDirectory(outDir);
        var path = Path.Combine(outDir, VehicleRegistry.Slug(b.Name) + ".json");
        File.WriteAllText(path, text);
        Console.WriteLine($"wrote {path}");
        foreach (var l in lines)
            Console.WriteLine("  " + l);
        Console.WriteLine($"  parts: {b.Placed} placed ({b.Welded} welded), {b.Doors} door leaves hung, {b.LootSpotsSet} loot containers, {b.Refused.Count} refused; plan {planMs:0} ms, build {buildMs:0} ms");
        foreach (var r in b.Refused.Take(40))
            Console.WriteLine($"  refused: {r}");
        if (b.Refused.Count > 40)
            Console.WriteLine($"  ... and {b.Refused.Count - 40} more refused");
        foreach (var n in b.Notes)
            Console.WriteLine($"  note: {n}");
        foreach (var m in b.Missing.Distinct())
            Console.WriteLine($"  missing part: {m}");
        // the real loader on what was written
        var back = ConstructBuild.ParseBlueprint(text, Path.GetFileName(path));
        var c = ConstructBuild.BuildOnGround(new ConstructWorld(), back, System.Numerics.Vector3.Zero, 0, out var why);
        if (c == null)
        {
            Console.WriteLine($"  load back: FAIL it doesn't build: {why}");
            failures++;
        }
        else
            Console.WriteLine($"  load back: {c.PartCount} parts from {back.Steps.Count} steps{(c.PartCount == b.Live.PartCount ? "" : $" (the script had {b.Live.PartCount})")}");
        failures += b.Refused.Count > 0 ? 1 : 0;
        if (Flag("--seed") != null && plan.Ascii() != InteriorPlanner.Plan(data, b.Class, b.Hull, seed).Ascii())
        {
            Console.WriteLine("  FAIL two plans of one seed differ");
            failures++;
        }
        return failures;
    }
}
