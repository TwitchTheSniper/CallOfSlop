using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace CallOfSlop.VehicleKit;

/// <summary>
/// The kit's quality tools (docs/vehiclekit/VEHICLEKIT.md, "Quality tools"): how alike a shape's seeds are, what never varies, what fails and what it costs, and the user's grades.
/// Headless, generation only (no drive or gun runs) unless asked: a seed costs 0.2 to 1 s here, so the defaults stay small on a 2-core box. Program.cs hands the arguments here.
/// </summary>
public static class VehicleQualityCli
{
    /// <summary>A generator's style and shape, for names and the grades file.</summary>
    public static (string Style, string Shape) Parts(IVehicleGenerator g) => (g.Style.Name, g is VehicleShape s ? s.Shape : g.Name[(g.Style.Name.Length + 1)..]);

    /// <summary>The shapes a style ships (its own extras too), or every one with <paramref name="extras"/>.</summary>
    public static IEnumerable<IVehicleGenerator> ShapesOf(FactionStyle st, bool extras) =>
        VehicleRegistry.Shapes.Select(f => f(st)).Concat(VehicleRegistry.Extras.Select(f => f(st)).Where(g => extras || g is VehicleShape v && st.Extra.Contains(v.Shape)));

    private static FactionStyle? StyleNamed(string name) => Styles.All.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase) || s.Group.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Seeds from --seed-list A,B,C, else --from S (default 1) and --seeds N.</summary>
    public static List<int> SeedsFrom(string[] args, int fallback)
    {
        if (Flag(args, "--seed-list") is { } list)
            return list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(int.Parse).ToList();
        var n = int.TryParse(Flag(args, "--seeds"), out var want) ? want : fallback;
        var from = int.TryParse(Flag(args, "--from"), out var f) ? f : 1;
        return Enumerable.Range(from, Math.Max(0, n)).ToList();
    }

    /// <summary>One sweep's results: what passed (features), what failed (seed, reason), timings.</summary>
    private sealed class Run
    {
        public readonly List<VehicleDiversity.Features> Ok = new();
        public readonly List<(int Seed, string Why)> Failed = new();
        public readonly List<VehicleVariety.Signature> Signatures = new();
        public readonly List<double> Ms = new();
        public readonly List<VehicleIdentity.Reading> Identity = new(); // (each passing seed against every style: VehicleIdentity)
    }

    private static Run Sweep(IVehicleGenerator g, IReadOnlyList<int> seeds, bool check, bool quiet)
    {
        var run = new Run();
        var clock = new Stopwatch();
        foreach (var seed in seeds)
        {
            try
            {
                clock.Restart();
                var (f, v, text, ms) = VehicleDiversity.Generate(g, seed);
                run.Ms.Add(ms);
                if (check && VehicleChecks.Check(text, $"{seed}.json", g.Checks, v.Rooms) is { Passed: false } r)
                {
                    run.Failed.Add((seed, "check: " + r.Failures[0]));
                    continue;
                }
                run.Ok.Add(f);
                run.Signatures.Add(VehicleVariety.Of(seed, v));
                run.Identity.Add(VehicleIdentity.Read(v));
            }
            catch (Exception ex) when (ex is VehicleKitException or ConstructPartException)
            {
                run.Ms.Add(clock.Elapsed.TotalMilliseconds);
                var own = $"{g.Name} {seed}: "; // (the kit names the vehicle first; the table already does)
                run.Failed.Add((seed, ex.Message.StartsWith(own, StringComparison.Ordinal) ? ex.Message[own.Length..] : ex.Message));
            }
            if (!quiet && run.Failed.Count > 0 && run.Failed[^1].Seed == seed)
                Console.WriteLine($"  seed {seed}: {run.Failed[^1].Why}");
        }
        return run;
    }

    /// <summary>One throwaway generation, so the first seed timed doesn't carry the JIT's start-up (a second or more).</summary>
    private static void Warm()
    {
        try
        {
            VehicleDiversity.Generate(VehicleRegistry.All[0], VehicleRegistry.All[0].Seeds[0]);
        }
        catch (Exception ex) when (ex is VehicleKitException or ConstructPartException)
        {
        }
    }

    /// <summary>A refusal with its numbers and quoted names taken out, so the same reason on two seeds counts once.</summary>
    public static string Reason(string why)
    {
        var sb = new StringBuilder();
        var quoted = false;
        foreach (var ch in why)
        {
            if (ch == '"')
            {
                quoted = !quoted;
                sb.Append(quoted ? "\"..." : "\"");
                continue;
            }
            if (quoted)
                continue;
            if (char.IsDigit(ch) || ch == '.' && sb.Length > 0 && sb[^1] == '#')
            {
                if (sb.Length == 0 || sb[^1] != '#')
                    sb.Append('#');
                continue;
            }
            sb.Append(ch);
        }
        var s = sb.ToString();
        return s.Length > 110 ? s[..110] + "..." : s;
    }

    /// <summary>
    /// <c>--diversity "Style Shape" [--seeds N] [--from S] [--seed-list A,B] [--alike D] [--checked]</c>: seeds (default 1 to 20) generated and measured by <see cref="VehicleDiversity"/>:
    /// the spread (mean pair, nearest neighbour), groups of seeds closer than D (default <see cref="VehicleDiversity.Alike"/>), the most different set (<see cref="VehicleVariety.Spread"/>),
    /// and every trait's spread, with the ones that never vary first. <c>--checked</c> also runs the full check and drops seeds that fail it (slow). Exit code: 0, or 1 for no generator.
    /// </summary>
    public static int Diversity(string name, string[] args)
    {
        if (VehicleRegistry.Find(name) is not { } g)
        {
            Console.WriteLine($"no vehicle generator named \"{name}\"");
            return 1;
        }
        var alike = float.TryParse(Flag(args, "--alike"), System.Globalization.CultureInfo.InvariantCulture, out var a) ? a : VehicleDiversity.Alike;
        var seeds = SeedsFrom(args, 20);
        Warm();
        var check = args.Any(x => x.Equals("--checked", StringComparison.OrdinalIgnoreCase));
        var run = Sweep(g, seeds, check, false);
        Console.WriteLine($"{g.Name}: {run.Ok.Count} of {seeds.Count} seeds {(check ? "pass the check" : "generate")}, {run.Ms.DefaultIfEmpty().Average():0} ms each");
        PrintSet(g, run, alike, true);
        return 0;
    }

    private static void PrintSet(IVehicleGenerator g, Run run, float alike, bool traits)
    {
        var s = VehicleDiversity.Summarise(run.Ok);
        if (s.Count < 2)
        {
            Console.WriteLine("  fewer than two seeds generated: nothing to compare");
            return;
        }
        Console.WriteLine($"  spread: mean pair {s.MeanPair:0.000}, nearest neighbour mean {s.MeanNearest:0.000}, least {s.LeastNearest:0.000} (seeds {s.Closest.A} and {s.Closest.B})");
        Console.WriteLine($"  mean pair by part: silhouette {s.MeanParts.Silhouette:0.000}, parts {s.MeanParts.Kinds:0.000}, layout {s.MeanParts.Layout:0.000}, size {s.MeanParts.Size:0.000}, front {s.MeanParts.Front:0.000}{(VehicleDiversity.SectionsOn ? "" : " (not counted: --no-sections)")}");
        var clusters = VehicleDiversity.Clusters(run.Ok, alike);
        if (clusters.Count == 0)
            Console.WriteLine($"  too alike (under {alike:0.000}): none");
        else
        {
            Console.WriteLine($"  too alike (under {alike:0.000}): {clusters.Count} group(s), {clusters.Sum(c => c.Count)} seeds");
            foreach (var c in clusters.Take(8))
                Console.WriteLine($"    {string.Join(" ", c.Select(f => f.Seed))}");
        }
        if (run.Identity.Count > 0)
        {
            var ids = run.Identity;
            Console.WriteLine($"  identity (VehicleIdentity): mean {ids.Average(x => x.Own.Total):0.00}, least {ids.Min(x => x.Own.Total):0.00} (seed {ids.MinBy(x => x.Own.Total)!.Seed}), weak points pass on {ids.Count(x => x.Own.WeakPassed)}/{ids.Count}{(ids.Any(x => x.Confused) ? $", read as another style: {string.Join(" ", ids.Where(x => x.Confused).Select(x => $"{x.Seed} ({x.BestOther.Style})"))}" : "")}");
            if (traits)
                foreach (var x in ids)
                    Console.WriteLine($"    seed {x.Seed}: {x.Own}");
        }
        var want = Math.Max(3, g.Seeds.Count);
        var spread = VehicleVariety.Spread(run.Signatures, want);
        Console.WriteLine($"  most different {spread.Count}: {string.Join(" ", spread.Select(x => x.Seed))}, closest pair {VehicleVariety.Closest(spread).D:0.00} (VehicleVariety), shipped: {string.Join(" ", g.Seeds)}");
        if (!traits)
            return;
        Console.WriteLine("  traits (values over the seeds; = derived from the body, else a style slot):");
        foreach (var t in VehicleDiversity.Traits(run.Ok).OrderBy(t => t.Values).ThenByDescending(t => t.Share))
        {
            var choices = t.Trait.StartsWith('=') || t.Trait == "gear" ? 0 : t.Trait.StartsWith("colour ", StringComparison.Ordinal)
                ? g.Style.Palette.GetValueOrDefault(t.Trait[7..])?.Distinct().Count() ?? 0
                : g.Style.Parts.GetValueOrDefault(t.Trait)?.Distinct().Count() ?? 0;
            var verdict = t.Values == 1 ? choices == 1 ? "fixed (the style lists one)" : "NEVER VARIES" : t.Share >= 0.85f ? "rarely varies" : "";
            Console.WriteLine($"    {t.Trait,-22} {t.Values,2} value(s), commonest {t.Share,4:P0} \"{t.Commonest}\"{(choices > 0 ? $" of {choices} listed" : "")}  {verdict}".TrimEnd());
        }
    }

    /// <summary>
    /// <c>--diversity-style Style [--seeds N] [--alike D] [--extras]</c>: <see cref="Diversity"/> for every shape a style ships (default 10 seeds each), one block a shape without the traits,
    /// then the shapes against each other: the pair of shapes that read closest.
    /// </summary>
    public static int DiversityStyle(string name, string[] args)
    {
        if (StyleNamed(name) is not { } st)
        {
            Console.WriteLine($"no style named \"{name}\". Known: {string.Join(", ", Styles.All.Select(s => s.Name))}");
            return 1;
        }
        var alike = float.TryParse(Flag(args, "--alike"), System.Globalization.CultureInfo.InvariantCulture, out var a) ? a : VehicleDiversity.Alike;
        var seeds = SeedsFrom(args, 10);
        Warm();
        var sets = new List<(IVehicleGenerator G, Run R)>();
        foreach (var g in ShapesOf(st, args.Any(x => x.Equals("--extras", StringComparison.OrdinalIgnoreCase))))
        {
            var run = Sweep(g, seeds, false, true);
            Console.WriteLine($"{g.Name}: {run.Ok.Count} of {seeds.Count} generate, {run.Ms.DefaultIfEmpty().Average():0} ms each");
            PrintSet(g, run, alike, false);
            sets.Add((g, run));
        }
        var all = sets.SelectMany(x => x.R.Ok).ToList();
        var whole = VehicleDiversity.Summarise(all);
        Console.WriteLine($"{st.Name}, every shape: {whole.Count} vehicles, mean pair {whole.MeanPair:0.000}, nearest neighbour mean {whole.MeanNearest:0.000}");
        var apart = new List<(string A, string B, float D)>();
        for (var i = 0; i < sets.Count; i++)
            for (var j = i + 1; j < sets.Count; j++)
                apart.Add((Parts(sets[i].G).Shape, Parts(sets[j].G).Shape, VehicleDiversity.Apart(sets[i].R.Ok, sets[j].R.Ok)));
        foreach (var p in apart.OrderBy(p => p.D).Take(3))
            Console.WriteLine($"  shapes closest to each other: {p.A} and {p.B}, {p.D:0.000} on average");
        var traits = VehicleDiversity.Traits(all).Where(t => !t.Trait.StartsWith('=') && t.Values == 1).Select(t => t.Trait).ToList();
        Console.WriteLine($"  style slots that never varied over every shape: {(traits.Count == 0 ? "none" : string.Join(", ", traits))}");
        return 0;
    }

    /// <summary>
    /// <c>--kit-report [--seeds N] [--style S] [--shape NAME] [--extras] [--check]</c>: every style and shape over N seeds (default 10): how often generation fails and on what,
    /// what a generation costs (mean, worst), part count and mass. <c>--check</c> also runs the full check on each seed and counts its failures (several seconds a seed).
    /// Exit code: 0.
    /// </summary>
    public static int KitReport(string[] args)
    {
        if (args.Any(x => x.Equals("--roles", StringComparison.OrdinalIgnoreCase)))
            return VehicleRolesCli.Matrix(args); // (kit 4's role and tier matrix: VehicleRoles.cs)
        var seeds = SeedsFrom(args, 10);
        var check = args.Any(x => x.Equals("--check", StringComparison.OrdinalIgnoreCase));
        var extras = args.Any(x => x.Equals("--extras", StringComparison.OrdinalIgnoreCase));
        var styleOnly = Flag(args, "--style");
        var shapeOnly = Flag(args, "--shape");
        var rows = new List<(string Style, string Shape, Run R)>();
        Warm();
        Console.WriteLine($"kit report: {seeds.Count} seed(s) a shape ({seeds.FirstOrDefault()} to {seeds.LastOrDefault()}){(check ? ", with the full check" : ", generation only")}");
        foreach (var st in Styles.All.Where(s => styleOnly == null || s.Name.Equals(styleOnly, StringComparison.OrdinalIgnoreCase)))
            foreach (var g in ShapesOf(st, extras))
            {
                var (style, shape) = Parts(g);
                if (shapeOnly != null && !shape.Equals(shapeOnly, StringComparison.OrdinalIgnoreCase))
                    continue;
                rows.Add((style, shape, Sweep(g, seeds, check, true)));
                var r = rows[^1].R;
                Console.WriteLine($"  {g.Name}: {r.Ok.Count}/{seeds.Count}");
            }
        Console.WriteLine();
        Console.WriteLine($"{"Style",-10} {"Shape",-14} {"ok",6} {"fail",5} {"ms mean",8} {"ms worst",8} {"parts",6} {"kg",6} {"ident",6} {"weak",5}  top refusal");
        foreach (var (style, shape, r) in rows)
        {
            var n = r.Ok.Count + r.Failed.Count;
            var top = r.Failed.GroupBy(f => Reason(f.Why)).OrderByDescending(x => x.Count()).FirstOrDefault();
            var ident = r.Identity.Count == 0 ? "-" : r.Identity.Average(x => x.Own.Total).ToString("0.00");
            var weak = r.Identity.Count == 0 ? "-" : (r.Identity.Count(x => x.Own.WeakPassed) / (float)r.Identity.Count).ToString("P0");
            Console.WriteLine($"{style,-10} {shape,-14} {$"{r.Ok.Count}/{n}",6} {(n == 0 ? 0 : r.Failed.Count / (float)n),5:P0} {r.Ms.DefaultIfEmpty().Average(),8:0} {r.Ms.DefaultIfEmpty().Max(),8:0} {r.Ok.Select(f => (double)f.Parts).DefaultIfEmpty().Average(),6:0} {r.Ok.Select(f => (double)f.Mass).DefaultIfEmpty().Average(),6:0} {ident,6} {weak,5}  {(top == null ? "" : $"{top.Count()}x {top.Key}")}");
        }
        foreach (var st in rows.GroupBy(r => r.Style))
        {
            var ids = st.SelectMany(r => r.R.Identity).ToList();
            if (ids.Count > 0)
                Console.WriteLine($"identity {st.Key}: mean {ids.Average(x => x.Own.Total):0.00}, weak points pass on {ids.Count(x => x.Own.WeakPassed)}/{ids.Count}, read as another style {ids.Count(x => x.Confused)}");
        }
        foreach (var (style, shape, r) in rows)
            foreach (var x in r.Identity.Where(x => x.Confused))
                Console.WriteLine($"  {style} {shape} {x.Seed} reads as {x.BestOther.Style}: {x.BestOther.Total:0.00} against {x.Own.Total:0.00} as its own");
        var reasons = rows.SelectMany(r => r.R.Failed.Select(f => (r.Style, r.Shape, f.Seed, Why: Reason(f.Why)))).GroupBy(f => f.Why).OrderByDescending(g => g.Count()).Take(10).ToList();
        if (reasons.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("top refusal reasons over everything:");
            foreach (var g in reasons)
                Console.WriteLine($"  {g.Count(),3}x {g.Key}  (e.g. {g.First().Style} {g.First().Shape} {g.First().Seed})");
        }
        var all = rows.SelectMany(r => r.R.Ms).ToList();
        Console.WriteLine($"{rows.Count} shapes, {rows.Sum(r => r.R.Ok.Count)} of {rows.Sum(r => r.R.Ok.Count + r.R.Failed.Count)} generated, {all.DefaultIfEmpty().Average():0} ms a generation, worst {all.DefaultIfEmpty().Max():0} ms");
        return 0;
    }

    // ---- Grades ----

    /// <summary>One grade the user gave: 1 (bin it) to 5 (ship it), with a note.</summary>
    public sealed record Grade(string Style, string Shape, int Seed, int Value, string Note, string Date);

    /// <summary>docs/vehiclekit/grades.json in the repository the game runs from; null outside a checkout.</summary>
    public static string? GradesPath()
    {
        for (var up = new DirectoryInfo(AppContext.BaseDirectory); up != null; up = up.Parent)
            if (File.Exists(Path.Combine(up.FullName, "CallOfSlop.csproj")) && Directory.Exists(Path.Combine(up.FullName, "docs", "vehiclekit")))
                return Path.Combine(up.FullName, "docs", "vehiclekit", "grades.json");
        return null;
    }

    public static List<Grade> LoadGrades(string path)
    {
        var list = new List<Grade>();
        if (!File.Exists(path))
            return list;
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        foreach (var e in doc.RootElement.GetProperty("grades").EnumerateArray())
            list.Add(new Grade(e.GetProperty("style").GetString()!, e.GetProperty("shape").GetString()!, e.GetProperty("seed").GetInt32(), e.GetProperty("grade").GetInt32(),
                e.TryGetProperty("note", out var n) ? n.GetString() ?? "" : "", e.TryGetProperty("date", out var d) ? d.GetString() ?? "" : ""));
        return list;
    }

    /// <summary>Sorted by style, shape and seed, one grade a line, LF, so a diff reads as the grades that changed.</summary>
    public static void SaveGrades(string path, IEnumerable<Grade> grades)
    {
        var sb = new StringBuilder("{\n  \"about\": \"The user's grades of generated vehicles, 1 (bin it) to 5 (ship it): --grade-vehicle adds one, --grade-report reads them (docs/vehiclekit/VEHICLEKIT.md, Quality tools).\",\n  \"grades\": [");
        var first = true;
        foreach (var g in grades.OrderBy(g => g.Style, StringComparer.Ordinal).ThenBy(g => g.Shape, StringComparer.Ordinal).ThenBy(g => g.Seed))
        {
            sb.Append(first ? "\n" : ",\n");
            first = false;
            sb.Append($"    {{ \"style\": {JsonSerializer.Serialize(g.Style)}, \"shape\": {JsonSerializer.Serialize(g.Shape)}, \"seed\": {g.Seed}, \"grade\": {g.Value}, \"note\": {JsonSerializer.Serialize(g.Note)}, \"date\": {JsonSerializer.Serialize(g.Date)} }}");
        }
        sb.Append(first ? "]\n}\n" : "\n  ]\n}\n");
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, sb.ToString());
        File.Move(tmp, path, true);
    }

    /// <summary><c>--grade-vehicle "Style Shape Seed" 1-5 ["note"]</c>: records (or replaces) a grade in docs/vehiclekit/grades.json. Exit code: 0 when written.</summary>
    public static int GradeVehicle(string[] args)
    {
        var rest = args.SkipWhile(a => !a.Equals("--grade-vehicle", StringComparison.OrdinalIgnoreCase)).Skip(1).ToArray();
        var at = rest.Length > 0 ? rest[0].LastIndexOf(' ') : -1;
        var seed = 0;
        var value = 0;
        if (rest.Length < 2 || at < 0 || !int.TryParse(rest[0][(at + 1)..], out seed) || !int.TryParse(rest[1], out value) || value is < 1 or > 5)
        {
            Console.WriteLine("usage: --grade-vehicle \"Style Shape Seed\" 1-5 [\"note\"]");
            return 1;
        }
        if (VehicleRegistry.Find(rest[0][..at]) is not { } g)
        {
            Console.WriteLine($"no vehicle generator named \"{rest[0][..at]}\"");
            return 1;
        }
        if (GradesPath() is not { } path)
        {
            Console.WriteLine("grades live in the repository's docs/vehiclekit/grades.json: run this from a checkout's build");
            return 1;
        }
        var note = rest.Length > 2 && !rest[2].StartsWith("--", StringComparison.Ordinal) ? rest[2] : "";
        var (style, shape) = Parts(g);
        var grades = LoadGrades(path);
        var old = grades.FindIndex(x => x.Style == style && x.Shape == shape && x.Seed == seed);
        var grade = new Grade(style, shape, seed, value, note, DateTime.UtcNow.ToString("yyyy-MM-dd"));
        if (old >= 0)
            grades[old] = grade;
        else
            grades.Add(grade);
        SaveGrades(path, grades);
        Console.WriteLine($"{(old >= 0 ? "replaced" : "added")}: {style} {shape} {seed} = {value}{(note.Length > 0 ? $" \"{note}\"" : "")} ({grades.Count} grade(s) in {path})");
        return 0;
    }

    /// <summary>
    /// <c>--grade-report [Style]</c>: the grades turned into suggestions, data only (nothing changes a weight): per shape the mean grade, seeds graded 4 or 5 that don't ship yet (commit them),
    /// shipped seeds graded 2 or less (drop them); then per style, trait values whose seeds grade a point or more under the style's mean (each graded seed is generated again to read its traits).
    /// </summary>
    public static int GradeReport(string[] args)
    {
        if (GradesPath() is not { } path || !File.Exists(path))
        {
            Console.WriteLine("no grades yet (docs/vehiclekit/grades.json): add some with --grade-vehicle");
            return 0;
        }
        var only = Flag(args, "--grade-report");
        var grades = LoadGrades(path).Where(g => only == null || g.Style.Equals(only, StringComparison.OrdinalIgnoreCase)).ToList();
        Console.WriteLine($"{grades.Count} grade(s){(only != null ? $" for {only}" : "")}");
        foreach (var byStyle in grades.GroupBy(g => g.Style).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var traits = new List<(Dictionary<string, string> T, int Grade)>();
            foreach (var byShape in byStyle.GroupBy(g => g.Shape).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                var gen = VehicleRegistry.Find($"{byStyle.Key} {byShape.Key}");
                var shipped = gen?.Seeds ?? [];
                var good = byShape.Where(g => g.Value >= 4 && !shipped.Contains(g.Seed)).OrderByDescending(g => g.Value).ThenBy(g => g.Seed).ToList();
                var bad = byShape.Where(g => g.Value <= 2 && shipped.Contains(g.Seed)).ToList();
                Console.WriteLine($"{byStyle.Key} {byShape.Key}: {byShape.Count()} graded, mean {byShape.Average(g => g.Value):0.0}; shipped {string.Join(" ", shipped)}");
                if (good.Count > 0)
                    Console.WriteLine($"  commit: {string.Join(", ", good.Select(g => $"{g.Seed} ({g.Value})"))}");
                if (bad.Count > 0)
                    Console.WriteLine($"  drop from the shipped seeds: {string.Join(", ", bad.Select(g => $"{g.Seed} ({g.Value}{(g.Note.Length > 0 ? $": {g.Note}" : "")})"))}");
                foreach (var low in byShape.Where(g => g.Value <= 2 && g.Note.Length > 0 && !bad.Contains(g)))
                    Console.WriteLine($"  low: {low.Seed} ({low.Value}): {low.Note}");
                if (gen == null)
                    continue;
                foreach (var g in byShape)
                    try
                    {
                        traits.Add((VehicleDiversity.Generate(gen, g.Seed).F.Traits, g.Value));
                    }
                    catch (Exception ex) when (ex is VehicleKitException or ConstructPartException)
                    {
                        Console.WriteLine($"  seed {g.Seed} doesn't generate any more: {ex.Message}");
                    }
            }
            if (traits.Count < 3)
                continue;
            var mean = traits.Average(t => t.Grade);
            var flagged = traits.SelectMany(t => t.T.Select(kv => (kv.Key, kv.Value, t.Grade)))
                .GroupBy(x => (x.Key, x.Value))
                .Where(g => g.Count() >= 2 && g.Average(x => x.Grade) <= mean - 1)
                .OrderBy(g => g.Average(x => x.Grade)).ThenByDescending(g => g.Count()).Take(12).ToList();
            Console.WriteLine($"{byStyle.Key}: traits whose seeds grade a point or more under the mean {mean:0.0} (two or more seeds each):");
            if (flagged.Count == 0)
                Console.WriteLine("  none");
            foreach (var f in flagged)
                Console.WriteLine($"  {f.Key.Key} = \"{f.Key.Value}\": mean {f.Average(x => x.Grade):0.0} over {f.Count()} seed(s)");
        }
        return 0;
    }

    private static string? Flag(string[] args, string flag) => args.SkipWhile(a => !a.Equals(flag, StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault(a => !a.StartsWith("--"));
}
