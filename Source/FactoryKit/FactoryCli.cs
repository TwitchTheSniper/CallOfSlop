using System.Diagnostics;
using CallOfSlop.VehicleKit;

namespace CallOfSlop.FactoryKit;

/// <summary>
/// The factory kit's commands (factory-generator.md 6): <c>--gen-factory FUNCTION [--style S] [--size C] [--tier N] [--seed S] [--brief FILE] [--plan]
/// [--out DIR] [--check]</c>. Program.cs hands the arguments here.
/// </summary>
public static class FactoryCli
{
    private static string? Flag(string[] args, string name) => args.SkipWhile(a => !a.Equals(name, StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault();

    private static bool Has(string[] args, string name) => args.Any(a => a.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>The brief from the arguments (or <c>--brief FILE</c>), one per seed asked for (default: the style's shipped seeds of the function).</summary>
    private static List<FactoryBrief> Briefs(string function, string[] args)
    {
        var b = Flag(args, "--brief") is { } file ? FactoryData.ReadBrief(File.ReadAllText(file)) : new FactoryBrief { Function = function };
        if (Flag(args, "--style") is { } st)
            b = b with { Style = st };
        if (Flag(args, "--size") is { } sz)
            b = b with { Size = sz };
        if (int.TryParse(Flag(args, "--tier"), out var tier))
            b = b with { Tier = tier };
        if (int.TryParse(Flag(args, "--seed"), out var seed))
            return [b with { Seed = seed }];
        if (int.TryParse(Flag(args, "--seeds"), out var count))
            return Enumerable.Range(1, count).Select(k => b with { Seed = k }).ToList();
        if (Flag(args, "--brief") != null)
            return [b];
        var style = FactoryStyles.Find(b.Style);
        return style.Seeds.TryGetValue(b.Function, out var seeds) ? seeds.Select(s => b with { Seed = s }).ToList() : [b];
    }

    /// <summary>The vehicle check's options for a building: it stands, it doesn't drive; parts within the size class.</summary>
    public static VehicleCheckOptions CheckOptions(FactoryBrief b) => new()
    {
        Towed = true,
        MaxParts = FactoryData.SizeOf(b.Size ?? FactoryData.Function(b.Function).Size).Parts,
        MinMass = 0,
        MaxMass = 1e9f,
        MaxSideFill = 1,
        MaxTopFill = 1,
        MaxRestTilt = 2,
        MinStability = 0,
    };

    public static int GenFactory(string function, string[] args)
    {
        List<FactoryBrief> briefs;
        try
        {
            briefs = Briefs(function, args);
        }
        catch (FactoryKitException e)
        {
            Console.WriteLine($"--gen-factory: {e.Message}");
            return 1;
        }
        var outDir = Flag(args, "--out") ?? VehicleRegistry.RepoBlueprints();
        var compare = Has(args, "--check");
        var failed = 0;
        foreach (var brief in briefs)
        {
            try
            {
                if (Has(args, "--plan") || Has(args, "--factory-plan"))
                {
                    var clock = Stopwatch.StartNew();
                    var (p, plan, _, _) = FactoryScript.Planned(brief);
                    Console.WriteLine($"{FactoryScript.BlueprintName(brief)} ({clock.Elapsed.TotalMilliseconds:0} ms)");
                    Console.Write(p.Text());
                    Console.Write(plan.Ascii());
                    continue;
                }
                var t0 = Stopwatch.StartNew();
                var s = FactoryScript.Build(brief);
                var text = s.Text();
                var ms = t0.Elapsed.TotalMilliseconds;
                if (FactoryScript.Build(brief).Text() != text)
                {
                    Console.WriteLine($"{brief.Key}: FAIL two runs of one seed wrote different files");
                    failed++;
                    continue;
                }
                var file = FactoryScript.FileName(brief);
                var path = Path.Combine(outDir, file);
                if (compare)
                {
                    var verdict = !File.Exists(path) ? "new (no committed file)" : File.ReadAllText(path).Replace("\r\n", "\n") == text ? "identical" : "DIFFERS";
                    Console.WriteLine($"{file}: {verdict}");
                    failed += verdict == "identical" ? 0 : 1;
                }
                else
                {
                    Directory.CreateDirectory(outDir);
                    File.WriteAllText(path, text);
                    Console.WriteLine($"wrote {path} ({s.Count} steps, {ms:0} ms)");
                }
                var report = VehicleChecks.Check(text, file, CheckOptions(brief));
                Console.WriteLine(report.ToString().TrimEnd());
                failed += report.Passed ? 0 : 1;
            }
            catch (Exception ex) when (ex is VehicleKitException or FactoryKitException or ConstructPartException)
            {
                Console.WriteLine($"{brief.Key}: FAIL {ex.Message}");
                failed++;
            }
        }
        return failed;
    }
}
