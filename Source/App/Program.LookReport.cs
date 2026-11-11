using System.Text;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>
/// <c>--look-report [FILE|DIR]</c>: how finished a set of blueprints looks (docs/sandbox/constructs/look.md). For each blueprint: its steps, the
/// share of steps with paint, the distinct paint colours, the share of paint that is dark or grey, the finishes (and finish names this build doesn't
/// offer, which load as the part's own), the lamps, and a colour histogram of every step (its paint, else the part's own main colour). Then a summary
/// over all, the most used colours and the ten pairs of blueprints most alike by histogram (and ten more between different families, names with their
/// trailing digits taken off). The terminal gets a short table; the whole table goes to <c>look-report.txt</c> next to the DLL. It reads the part files
/// and the blueprints' JSON, never builds them and never loads a map.
/// </summary>
public static partial class Program
{
    /// <summary>Dark: luma (Rec. 601, 0..1) below this. Grey: saturation ((max - min) / max) below <see cref="LookGreySat"/>.</summary>
    private const float LookDarkLuma = 0.25f, LookGreySat = 0.15f;

    private const int LookHues = 12, LookValues = 3, LookBins = LookHues * LookValues + 3; // (3 grey bins: dark, mid, light)

    private sealed class LookRow
    {
        public string Name = "", File = "";
        public int Steps, Painted, PaintUses, DarkGrey, Finished, BadFinish, Lamps, BayLamps, Unknown, LinkPaint;
        public readonly HashSet<uint> Colours = new();
        public readonly float[] Hist = new float[LookBins];
    }

    public static int LookReport(string[] args)
    {
        using var stdout = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true }; // (Console goes to callofslop.log)
        void Say(string line = "")
        {
            stdout.WriteLine(line);
            Console.WriteLine(line);
        }
        var target = args.SkipWhile(a => !a.Equals("--look-report", StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault(a => !a.StartsWith("--"))
                     ?? ConstructBuild.BlueprintFolder;
        string[] files;
        if (Directory.Exists(target))
            files = Directory.GetFiles(target, "*.json", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal).ToArray();
        else if (File.Exists(target))
            files = [target];
        else
        {
            Say($"--look-report: no file or folder \"{target}\"");
            return 1;
        }

        var rows = new List<LookRow>();
        var uses = new Dictionary<uint, int>();
        var errors = 0;
        foreach (var file in files)
        {
            try
            {
                rows.Add(LookRead(file, uses));
            }
            catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
            {
                Say($"SKIPPED {file}: {e.Message}");
                errors++;
            }
        }
        if (rows.Count == 0)
        {
            Say("--look-report: no blueprints read");
            return 1;
        }

        var full = new StringBuilder();
        full.AppendLine($"Look report, {DateTime.Now:yyyy-MM-dd HH:mm}, {target}");
        full.AppendLine($"Dark: luma < {LookDarkLuma:0.00}; grey: saturation < {LookGreySat:0.00} (paint uses only; a step's own colours are not paint).");
        full.AppendLine("Colour histogram: every step's paint, else its part's own main colour, in 12 hues x 3 values + 3 greys; likeness is the histograms' overlap.");
        full.AppendLine();
        const string head = "Blueprint                            steps painted% colours darkgrey% finish lamps baylamps";
        full.AppendLine(head);
        foreach (var r in rows.OrderBy(r => r.Name, StringComparer.Ordinal))
            full.AppendLine(LookLine(r));

        // The summary.
        var steps = rows.Sum(r => r.Steps);
        var painted = rows.Sum(r => r.Painted);
        var paintUses = rows.Sum(r => r.PaintUses);
        var darkGrey = rows.Sum(r => r.DarkGrey);
        var summary = new StringBuilder();
        summary.AppendLine($"{rows.Count} blueprints, {steps} steps. No paint at all: {rows.Count(r => r.PaintUses == 0)}. " +
                           $"Painted steps: {Pct(painted, steps)}% ({paintUses} step paint uses, {rows.Sum(r => r.LinkPaint)} link paint uses).");
        summary.AppendLine($"Dark or grey paint: {Pct(darkGrey, paintUses)}% of paint uses. Distinct colours per painted blueprint: median " +
                           $"{Median(rows.Where(r => r.PaintUses > 0).Select(r => r.Colours.Count))}, one colour only: {rows.Count(r => r.Colours.Count == 1)}.");
        summary.AppendLine($"Finish: {rows.Count(r => r.Finished > 0)} blueprints, {rows.Sum(r => r.Finished)} steps ({rows.Sum(r => r.BadFinish)} names not offered). " +
                           $"Lamps: {rows.Count(r => r.Lamps > 0)} blueprints have a lamp part ({rows.Sum(r => r.Lamps)} in all); " +
                           $"{rows.Count(r => r.Lamps + r.BayLamps > 0)} have a lamp or a lit bay. Unknown parts: {rows.Sum(r => r.Unknown)}.");
        summary.Append("Most used paint: ");
        summary.AppendLine(string.Join(", ", uses.OrderByDescending(kv => kv.Value).Take(10)
            .Select(kv => $"{ConstructPaint.ToHex(kv.Key)} {Pct(kv.Value, paintUses)}%{(LookDarkGrey(kv.Key) ? " (dk/gr)" : "")}")));

        // The pairs most alike.
        var pairs = new List<(float Same, int A, int B)>();
        for (var a = 0; a < rows.Count; a++)
            for (var b = a + 1; b < rows.Count; b++)
            {
                var same = 0f;
                for (var k = 0; k < LookBins; k++)
                    same += MathF.Min(rows[a].Hist[k], rows[b].Hist[k]);
                pairs.Add((same, a, b));
            }
        pairs.Sort((x, y) => y.Same.CompareTo(x.Same));
        var alike = new StringBuilder();
        alike.AppendLine("Most alike by colour (overlap 1.00: the same mix):");
        foreach (var p in pairs.Take(10))
            alike.AppendLine($"  {p.Same:0.00}  {rows[p.A].Name} / {rows[p.B].Name}");
        alike.AppendLine("Most alike between families (names without trailing digits differ):");
        foreach (var p in pairs.Where(p => LookFamily(rows[p.A].Name) != LookFamily(rows[p.B].Name)).Take(10))
            alike.AppendLine($"  {p.Same:0.00}  {rows[p.A].Name} / {rows[p.B].Name}");
        alike.AppendLine($"Pairs at 0.90 or more: {pairs.Count(p => p.Same >= 0.9f)} of {pairs.Count}.");

        full.AppendLine();
        full.Append(summary);
        full.AppendLine();
        full.Append(alike);
        var outFile = Path.Combine(AppContext.BaseDirectory, "look-report.txt");
        File.WriteAllText(outFile, full.ToString());

        // The terminal: the summary, the ten least finished and the pairs.
        Say(summary.ToString().TrimEnd());
        Say();
        Say("Least finished (painted share, then colours):");
        Say(head);
        foreach (var r in rows.OrderBy(r => r.Painted / (float)Math.Max(1, r.Steps)).ThenBy(r => r.Colours.Count).ThenByDescending(r => r.Steps).Take(10))
            Say(LookLine(r));
        Say();
        Say(alike.ToString().TrimEnd());
        Say($"Full table: {outFile}");
        return errors;
    }

    private static LookRow LookRead(string file, Dictionary<uint, int> uses)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(file), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var root = doc.RootElement;
        var r = new LookRow { File = Path.GetFileName(file), Name = root.TryGetProperty("name", out var n) ? n.GetString() ?? "" : Path.GetFileNameWithoutExtension(file) };
        foreach (var s in root.GetProperty("steps").EnumerateArray())
        {
            r.Steps++;
            var def = s.TryGetProperty("part", out var pn) ? ConstructParts.Find(pn.GetString() ?? "") : null;
            if (def == null)
                r.Unknown++;
            else
            {
                if (def.Logic == CLogic.Lamp)
                    r.Lamps++;
                if (def.Lamps.Length > 0)
                    r.BayLamps++;
            }
            var colour = 0u;
            if (s.TryGetProperty("paint", out var pt) && ConstructPaint.TryParse(pt.GetString(), out var paint) && ConstructPaint.IsSet(paint))
            {
                colour = paint;
                r.Painted++;
                r.PaintUses++;
                r.Colours.Add(paint);
                uses[paint] = uses.GetValueOrDefault(paint) + 1;
                if (LookDarkGrey(paint))
                    r.DarkGrey++;
            }
            else if (def != null && def.Boxes.Length > 0)
                colour = ConstructPaint.MainColour(def);
            if (s.TryGetProperty("finish", out var fn))
            {
                r.Finished++;
                if (ConstructFinish.Of(fn.GetString()) == 0)
                    r.BadFinish++;
            }
            if (colour != 0)
                r.Hist[LookBin(colour)]++;
        }
        if (root.TryGetProperty("links", out var links))
            foreach (var l in links.EnumerateArray())
                if (l.TryGetProperty("paint", out _))
                    r.LinkPaint++;
        var total = r.Hist.Sum();
        if (total > 0)
            for (var k = 0; k < LookBins; k++)
                r.Hist[k] /= total;
        return r;
    }

    private static string LookLine(LookRow r) =>
        $"{Trim(r.Name, 36),-36} {r.Steps,5} {Pct(r.Painted, r.Steps),8} {r.Colours.Count,7} {(r.PaintUses == 0 ? "-" : Pct(r.DarkGrey, r.PaintUses).ToString()),9} {r.Finished,6} {r.Lamps,5} {r.BayLamps,8}";

    private static (float Luma, float Sat, float Hue) LookHsv(uint paint)
    {
        var v = ConstructPaint.ToVector(paint);
        var max = MathF.Max(v.X, MathF.Max(v.Y, v.Z));
        var min = MathF.Min(v.X, MathF.Min(v.Y, v.Z));
        var luma = 0.299f * v.X + 0.587f * v.Y + 0.114f * v.Z;
        var sat = max <= 0f ? 0f : (max - min) / max;
        var hue = 0f;
        if (max - min > 1e-5f)
        {
            hue = max == v.X ? (v.Y - v.Z) / (max - min) : max == v.Y ? 2f + (v.Z - v.X) / (max - min) : 4f + (v.X - v.Y) / (max - min);
            hue = (hue / 6f + 1f) % 1f;
        }
        return (luma, sat, hue);
    }

    private static bool LookDarkGrey(uint paint)
    {
        var (luma, sat, _) = LookHsv(paint);
        return luma < LookDarkLuma || sat < LookGreySat;
    }

    private static int LookBin(uint paint)
    {
        var (luma, sat, hue) = LookHsv(paint);
        var value = luma < 0.25f ? 0 : luma < 0.55f ? 1 : 2;
        if (sat < LookGreySat)
            return LookHues * LookValues + value;
        return Math.Min(LookHues - 1, (int)(hue * LookHues)) * LookValues + value;
    }

    private static string LookFamily(string name) => name.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9', ' ');

    private static int Pct(int part, int whole) => whole <= 0 ? 0 : (int)MathF.Round(100f * part / whole);

    private static int Median(IEnumerable<int> values)
    {
        var v = values.OrderBy(x => x).ToArray();
        return v.Length == 0 ? 0 : v[v.Length / 2];
    }

    private static string Trim(string s, int n) => s.Length <= n ? s : s[..(n - 1)] + "~";
}
