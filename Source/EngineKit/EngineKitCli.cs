using System.Globalization;
using System.Numerics;

namespace CallOfSlop.EngineKit;

/// <summary>
/// <c>--gen-engine [--preset small|medium|large|huge] [--power KW] [--torque NM] [--rpm R] [--fit LxWxH] [--mass KG] [--fuel F] [--layout inline|v|flat|w|radial]
/// [--boost none|supercharger|turbo] [--size small|medium|large|capital] [--tier 1-5] [--seed S] [--plans N] [--out FILE]</c>: the Engine Kit on the command line
/// (docs/vehiclekit/ENGINEKIT.md). Prints the plan it chose, the numbers the game works out for what it built, and why it falls short when it does; --out writes
/// the blueprint; --plans N lists the N best plans too.
/// </summary>
public static class EngineKitCli
{
    /// <summary>The presets a player picks from (and the command line's --preset): kW at tier 1's pieces; a higher tier makes the same power smaller.</summary>
    public static readonly (string Name, float Kw)[] Presets = [("small", 120), ("medium", 300), ("large", 800), ("huge", 3000)];

    public static int Cli(string[] args)
    {
        string? Arg(string name) => args.SkipWhile(a => !a.Equals(name, StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault();
        float F(string name) => Arg(name) is { } v && float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : 0;
        var power = F("--power");
        if (power <= 0 && Arg("--preset") is { } preset)
            power = Presets.FirstOrDefault(p => p.Name.Equals(preset, StringComparison.OrdinalIgnoreCase)).Kw;
        var fit = Vector3.Zero;
        if (Arg("--fit") is { } fs)
        {
            var parts = fs.Split('x', 'X');
            if (parts.Length == 3)
                fit = new Vector3(float.Parse(parts[0], CultureInfo.InvariantCulture), float.Parse(parts[1], CultureInfo.InvariantCulture), float.Parse(parts[2], CultureInfo.InvariantCulture));
        }
        var r = new EngineRequest
        {
            PowerKw = power,
            TorqueNm = F("--torque"),
            PeakRpm = F("--rpm"),
            Fit = fit,
            MaxMass = F("--mass"),
            Fuel = Arg("--fuel") ?? "petrol",
            Tier = Arg("--tier") is { } t ? int.Parse(t, CultureInfo.InvariantCulture) : 1,
            Layout = Arg("--layout") is { } l ? Enum.Parse<EngineLayout>(l, true) : EngineLayout.Auto,
            Boost = Arg("--boost") is { } b ? Enum.Parse<EngineBoost>(b, true) : EngineBoost.Auto,
            Size = Arg("--size") is { } s ? Array.FindIndex(EngineKit.SizeWords, w => w.Equals(s, StringComparison.OrdinalIgnoreCase)) : -1,
            Seed = Arg("--seed") is { } sd ? int.Parse(sd, CultureInfo.InvariantCulture) : 0,
        };
        if (r.PowerKw <= 0 && r.TorqueNm <= 0)
        {
            Console.WriteLine("--gen-engine needs --power KW, --torque NM or --preset small|medium|large|huge");
            return 2;
        }
        Console.WriteLine($"request: {r}");
        var start = System.Diagnostics.Stopwatch.GetTimestamp();
        if (F("--plans") is > 0 and var np)
            foreach (var p in EngineKit.Plans(r).Take((int)np))
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"  plan {EngineKit.SizeWords[p.Size]} {EngineKit.LayoutName(p.Layout, p.Cylinders)} x{p.Cranks}: {p.Kw:0} kW, {p.TorqueNm:0} N m at {p.PeakRpm:0} rpm (stroke {p.Stroke:0.:0.00}x{p.Dims.Y:0.00}x{p.Dims.Z:0.00} m, {p.Cylinder} / {p.FuelPart} / {p.BoostPart ?? "-"}, score {p.Score:0.000}{(p.Feasible ? "" : " (over: " + p.Why + ")")}"));
        var res = EngineKit.Request(r);
        Console.WriteLine(res.ToString());
        Console.WriteLine($"  {System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds:0} ms");
        if (args.Any(a => a.Equals("--again", StringComparison.OrdinalIgnoreCase)))
        {
            start = System.Diagnostics.Stopwatch.GetTimestamp();
            var plans = EngineKit.Plans(r).Count;
            var tp = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            var again = EngineKit.Request(r);
            Console.WriteLine($"  again (warm): {plans} plans in {tp:0.0} ms, request {System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds:0.0} ms, {(again.Text == res.Text ? "same blueprint" : "A DIFFERENT BLUEPRINT")}");
        }
        if (res.Built && Arg("--out") is { } outFile)
        {
            File.WriteAllText(outFile, res.Text);
            Console.WriteLine($"  wrote {outFile}");
        }
        return res.Built ? 0 : 1;
    }
}
