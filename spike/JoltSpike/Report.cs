using System.Diagnostics;
using System.Globalization;

namespace JoltSpike;

/// <summary>
/// Output for CI. GitHub shows at most 10 notices and 10 errors per step, so each section prints one summary
/// notice of its checks, a few metric notices and an error per failed check. Everything also goes to
/// results.txt (key=value, compared across Windows and Linux by the workflow) and spike.log.
/// </summary>
public static class Report
{
    static readonly List<string> Passed = new();
    static readonly List<string> Failed = new();
    static readonly List<string> Metrics = new();
    public static int Failures;
    static string _section = "";
    static StreamWriter? _results, _log;

    public static void Open(string section)
    {
        _section = section;
        _results = new StreamWriter(File.Open($"results-{section}.txt", FileMode.Create)) { AutoFlush = true };
        _log = new StreamWriter(File.Open($"spike-{section}.log", FileMode.Create)) { AutoFlush = true };
    }

    public static string F(double v, int digits = 3) => v.ToString("F" + digits, CultureInfo.InvariantCulture);

    public static void Log(string line)
    {
        Console.WriteLine(line);
        _log?.WriteLine(line);
    }

    /// <summary>A pass/fail check; failures become error annotations right away.</summary>
    public static void Check(string name, bool ok, string detail)
    {
        Log($"{(ok ? "PASS" : "FAIL")} {name}: {detail}");
        if (ok)
            Passed.Add($"{name} ({detail})");
        else
        {
            Failures++;
            Failed.Add($"{name} ({detail})");
        }
    }

    /// <summary>A measured value, collected into the section's metric notices.</summary>
    public static void Metric(string name, string value)
    {
        Log($"METRIC {name} = {value}");
        Metrics.Add($"{name}={value}");
    }

    /// <summary>A value that must match across machines (written to results.txt for the compare job).</summary>
    public static void Result(string key, string value)
    {
        Log($"RESULT {key}={value}");
        _results?.WriteLine($"{key}={value}");
    }

    public static void Notice(string title, string msg) => Console.WriteLine($"::notice title={_section} {title}::{Clean(msg)}");

    public static void Close()
    {
        if (Phys.BindingNotes.Count > 0 && _section == "a")
            Notice("binding issues", string.Join(" | ", Phys.BindingNotes));
        if (Passed.Count > 0)
            Notice($"checks passed {Passed.Count}/{Passed.Count + Failures}", string.Join(" | ", Passed));
        if (Failed.Count > 0)
            Console.WriteLine($"::error title={_section} checks FAILED {Failures}/{Passed.Count + Failures}::{Clean(string.Join(" | ", Failed))}");
        // Metrics in chunks so no single annotation gets huge.
        for (var i = 0; i < Metrics.Count; i += 8)
            Notice($"metrics {i / 8 + 1}", string.Join(" | ", Metrics.Skip(i).Take(8)));
        _results?.Dispose();
        _log?.Dispose();
    }

    static string Clean(string s) => s.Replace("%", "%25").Replace("\r", "").Replace("\n", "%0A");
}

/// <summary>Microsecond timing helpers.</summary>
public static class Clock
{
    public static long Now => Stopwatch.GetTimestamp();
    public static double Us(long from, long to) => (to - from) * 1e6 / Stopwatch.Frequency;
    public static double UsSince(long from) => Us(from, Now);
    public static double MsSince(long from) => Us(from, Now) / 1000.0;

    public static double Percentile(List<double> xs, double p)
    {
        if (xs.Count == 0)
            return 0;
        var s = xs.OrderBy(x => x).ToList();
        return s[Math.Clamp((int)Math.Round(p * (s.Count - 1)), 0, s.Count - 1)];
    }
}
