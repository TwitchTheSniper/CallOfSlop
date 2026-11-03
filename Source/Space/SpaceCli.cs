using System.Diagnostics;
using System.Globalization;

namespace CallOfSlop.Space;

/// <summary>
/// <c>--space-sim [FILE] [--days N] [--every H]</c>: loads a star system (default Systems/Dev.json), prints its bodies,
/// then where each one is every H hours for N days (distance from its parent and from the star, latitude and longitude
/// of the star overhead on each spinning body). <c>--check-system FILE</c> only loads it. Headless, for system makers.
/// </summary>
public static class SpaceCli
{
    public static int Run(string[] args)
    {
        string? file = null;
        double days = 1, every = 6;
        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i].ToLowerInvariant();
            var v = i + 1 < args.Length ? args[i + 1] : null;
            if (a == "--space-sim" && v != null && !v.StartsWith("--"))
                file = v;
            else if (a == "--days" && v != null)
                days = double.Parse(v, CultureInfo.InvariantCulture);
            else if (a == "--every" && v != null)
                every = double.Parse(v, CultureInfo.InvariantCulture);
        }
        var sys = LoadOrPrint(file ?? Path.Combine(AppContext.BaseDirectory, StarSystemFile.FolderName, "Dev.json"));
        if (sys == null)
            return 1;
        Console.WriteLine(StarSystemFile.Describe(sys));
        var sw = Stopwatch.StartNew();
        var steps = (int)Math.Max(1, Math.Round(days * 24 / every));
        for (var n = 0; n <= steps; n++)
        {
            var t = SpaceTime.FromSeconds(n * every * 3600);
            Console.WriteLine($"[space] {t}");
            for (var b = 1; b < sys.Count; b++)
            {
                var body = sys.Bodies[b];
                var p = sys.PositionOf(b, t);
                var fromParent = (p - sys.PositionOf(body.Parent, t)).Length;
                var fromStar = p.ToMetres().Length;
                var line = FormattableString.Invariant($"  {body.Name,-14} {Dist(fromParent),12} from {sys.Bodies[body.Parent].Name}, {fromStar / StarSystemFile.Au:0.000000} AU from the star");
                if (body.RotationPeriod > 0)
                {
                    // Where the star is overhead: its direction in the body-fixed frame.
                    var fixedPose = sys.PoseOf(new FrameRef(FrameKind.BodyFixed, b), t);
                    var sun = fixedPose.Rot.InverseRotate(SpacePos.Origin - p).Normalized();
                    line += FormattableString.Invariant($", sun overhead at lat {Math.Asin(sun.Z) * 180 / Math.PI:0.0}, lon {Math.Atan2(sun.Y, sun.X) * 180 / Math.PI:0.0}");
                }
                Console.WriteLine(line);
            }
        }
        Console.WriteLine($"[space] {steps + 1} samples in {sw.Elapsed.TotalMilliseconds:0.0} ms");
        return 0;
    }

    /// <summary><c>--check-system FILE</c>: does this system file load? Prints it or the reason; the exit code is 0 or 1.</summary>
    public static int Check(string file)
    {
        var sys = LoadOrPrint(file);
        if (sys == null)
            return 1;
        Console.WriteLine(StarSystemFile.Describe(sys));
        Console.WriteLine("[space] OK");
        return 0;
    }

    private static StarSystem? LoadOrPrint(string file)
    {
        try
        {
            return StarSystemFile.Load(File.ReadAllText(file), Path.GetFileName(file));
        }
        catch (Exception ex) when (ex is StarSystemException or IOException)
        {
            Console.WriteLine($"[space] refused: {ex.Message}");
            return null;
        }
    }

    private static string Dist(double m) => m >= 1e9 ? FormattableString.Invariant($"{m / StarSystemFile.Au:0.000000} AU") : FormattableString.Invariant($"{m / 1000:0.000} km");
}
