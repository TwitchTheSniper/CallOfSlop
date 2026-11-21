namespace CallOfSlop.PartKit;

/// <summary>
/// Raised ribs and chunkier greebles (render bench phase 6, Karl 2026-11-21): a ship generator's thin drawn details are grown
/// outward a little before the coplanar-face pass, so frames, ribs and small fittings read at a distance as they do on the
/// Ship Render Bench page. Only drawn boxes change (never a solid one, a socket or a mass), so collision, building and the
/// blueprints are as before. The bench's rules and its defaults:
///   a dark box (sRGB luma under <see cref="DarkLuma"/>) thinner than <see cref="RibMax"/> grows <see cref="RibGrow"/> on every side;
///   any box thinner than <see cref="ThinMax"/> grows by <see cref="ThinShare"/> of its thickness, half on each side.
/// A box that moves, glows, shows, works or has its own outline (points) is left alone. <see cref="Part.Json"/> runs it for the
/// generators in <see cref="Generators"/>, then <see cref="PartZFight.Resolve"/> settles any face the growth made coplanar.
/// </summary>
public static class PartChunky
{
    public const double DarkLuma = 0.2, RibMax = 0.35, RibGrow = 0.02, ThinMax = 0.12, ThinShare = 0.2;

    /// <summary>The ship generators whose parts are grown (docs/sandbox/PARTKIT.md, "Chunky details").</summary>
    public static readonly HashSet<string> Generators = new(StringComparer.Ordinal)
    {
        "ship_parts", "roamer_parts", "miner_parts", "torpedo_frigate_parts", "hauler_parts", "freighter_parts",
        "haywain_parts", "courier_parts", "dibber_parts",
    };

    /// <summary>Set by <see cref="PartKit"/> while a generator in <see cref="Generators"/> makes its files.</summary>
    public static bool On;

    private static readonly HashSet<string> Plain = new(StringComparer.Ordinal) { "detail", "color", "min", "max", "finish", "paintable", "shape", "seam", "edges" };

    /// <summary>Grows the part's thin drawn boxes in place; returns how many changed.</summary>
    public static int Grow(List<PyDict> boxes)
    {
        var n = 0;
        foreach (var b in boxes)
        {
            if (!Py.Truthy(b.Get("detail")) || b.Get("min") is null || b.Get("max") is null || b.Keys.Any(k => !Plain.Contains(k)))
                continue;
            var lo = V3(b["min"]);
            var hi = V3(b["max"]);
            var th = Math.Min(hi[0] - lo[0], Math.Min(hi[1] - lo[1], hi[2] - lo[2]));
            if (th <= 0)
                continue;
            var d = 0.0;
            if (th < RibMax && b.Get("color") is string c && Luma(c) < DarkLuma)
                d += RibGrow;
            if (th < ThinMax)
                d += th * ThinShare / 2;
            if (d <= 0)
                continue;
            // A grown face that lands on another box's plane (a facet, a plate it stands beside) would z-fight, and the
            // coplanar pass doesn't move shaped boxes: grow less, or not at all.
            while (d > 0.001 && OnAPlane(boxes, b, lo, hi, d))
                d *= 0.6;
            if (d <= 0.001)
                continue;
            b["min"] = Py.L(Py.Round(lo[0] - d, 4), Py.Round(lo[1] - d, 4), Py.Round(lo[2] - d, 4));
            b["max"] = Py.L(Py.Round(hi[0] + d, 4), Py.Round(hi[1] + d, 4), Py.Round(hi[2] + d, 4));
            n++;
        }
        return n;
    }

    private static bool OnAPlane(List<PyDict> boxes, PyDict self, double[] lo, double[] hi, double d)
    {
        foreach (var o in boxes)
        {
            if (ReferenceEquals(o, self) || o.Get("min") is null || o.Get("max") is null)
                continue;
            var ol = V3(o["min"]);
            var oh = V3(o["max"]);
            for (var a = 0; a < 3; a++)
                foreach (var f in new[] { lo[a] - d, hi[a] + d })
                    if (Math.Abs(f - ol[a]) < 0.0015 || Math.Abs(f - oh[a]) < 0.0015)
                        return true;
        }
        return false;
    }

    private static double[] V3(object? list)
    {
        var l = (System.Collections.IList)list!;
        return [Py.D(l[0]), Py.D(l[1]), Py.D(l[2])];
    }

    /// <summary>A "#rrggbb" swatch's brightness as a person sees it (sRGB values, the bench's luma).</summary>
    public static double Luma(string hex)
    {
        if (hex.Length != 7 || hex[0] != '#')
            return 1;
        var v = Convert.ToInt32(hex[1..], 16);
        return (0.2126 * ((v >> 16) & 255) + 0.7152 * ((v >> 8) & 255) + 0.0722 * (v & 255)) / 255.0;
    }
}
