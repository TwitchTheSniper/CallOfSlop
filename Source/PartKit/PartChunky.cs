namespace CallOfSlop.PartKit;

/// <summary>
/// Raised ribs and chunkier greebles (render bench phase 6, Karl 2026-11-21): a ship generator's thin drawn details are grown
/// outward a little before the coplanar-face pass, so frames, ribs and small fittings read at a distance as they do on the
/// Ship Render Bench page. Only drawn boxes change (never a solid one, a socket or a mass), so collision, building and the
/// blueprints are as before. The bench's rules and its defaults:
///   a dark box (sRGB luma under <see cref="DarkLuma"/>) thinner than <see cref="RibMax"/> grows <see cref="RibGrow"/>;
///   any box thinner than <see cref="ThinMax"/> grows by half of <see cref="ThinShare"/> of its thickness.
/// Only the faces that stand proud of the solid boxes a detail sits on move, outward (the bench grew every side; here that
/// pushed jambs into doorways and rails under floors). A shaped box (a shape collides) or one that moves, glows, shows or
/// works is left alone. <see cref="Part.Json"/> runs it for the
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

    private static readonly HashSet<string> Plain = new(StringComparer.Ordinal) { "detail", "color", "min", "max", "finish", "paintable", "edges" };

    /// <summary>Grows the part's thin drawn boxes in place; returns how many changed.</summary>
    public static int Grow(List<PyDict> boxes)
    {
        var solids = boxes.Where(b => !Py.Truthy(b.Get("detail")) && b.Get("min") is not null && b.Get("max") is not null)
            .Select(b => (Lo: V3(b["min"]), Hi: V3(b["max"]))).ToList();
        if (solids.Count == 0)
            return 0;
        var every = boxes.Where(b => b.Get("min") is not null && b.Get("max") is not null).Select(b => (Lo: V3(b["min"]), Hi: V3(b["max"]))).ToList();
        double[] allLo = [every.Min(q => q.Lo[0]), every.Min(q => q.Lo[1]), every.Min(q => q.Lo[2])]; // (the part's drawn ends, before growth)
        double[] allHi = [every.Max(q => q.Hi[0]), every.Max(q => q.Hi[1]), every.Max(q => q.Hi[2])];
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
            // Only the faces standing proud of the solid boxes the detail sits on move, outward: a rib rises off the skin, a
            // jamb off its wall, and nothing grows into a doorway, under a floor or past a part's back (merge review). A detail
            // touching no solid box stays as it is.
            var on = solids.Where(q => Overlaps(q.Lo, q.Hi, lo, hi)).ToList();
            if (on.Count == 0)
                continue;
            var grow = new bool[6];
            var any = false;
            for (var a = 0; a < 3; a++)
            {
                // (and never a face flush with the part's own end: it meets the next part there, the Haywain cab's corner rails)
                grow[a * 2] = lo[a] < on.Min(q => q.Lo[a]) - 0.0005 && Math.Abs(lo[a] - allLo[a]) > 0.0015;
                grow[a * 2 + 1] = hi[a] > on.Max(q => q.Hi[a]) + 0.0005 && Math.Abs(hi[a] - allHi[a]) > 0.0015;
                any |= grow[a * 2] || grow[a * 2 + 1];
            }
            if (!any)
                continue;
            // Each box grows by its own share (85-100%, from where it is): two parts' details that end on one plane once the
            // ship is built (the Haywain cab's corner rails) would grow onto one plane again, and the coplanar pass sees one part.
            var h = Math.Sin(lo[0] * 12.9898 + lo[1] * 78.233 + lo[2] * 37.719 + hi[0] * 4.1414 + hi[1] * 9.731 + hi[2] * 2.3197) * 43758.5453;
            d *= 0.85 + 0.15 * (h - Math.Floor(h));
            // A grown face that lands on another box's plane (a facet, a plate it stands beside) would z-fight, and the
            // coplanar pass doesn't move shaped boxes: grow less, or not at all.
            while (d > 0.001 && OnAPlane(boxes, b, lo, hi, grow, d))
                d *= 0.6;
            if (d <= 0.001)
                continue;
            for (var a = 0; a < 3; a++)
            {
                if (grow[a * 2])
                    lo[a] -= d;
                if (grow[a * 2 + 1])
                    hi[a] += d;
            }
            b["min"] = Py.L(Py.Round(lo[0], 4), Py.Round(lo[1], 4), Py.Round(lo[2], 4));
            b["max"] = Py.L(Py.Round(hi[0], 4), Py.Round(hi[1], 4), Py.Round(hi[2], 4));
            n++;
        }
        return n;
    }

    private static bool Overlaps(double[] alo, double[] ahi, double[] blo, double[] bhi)
    {
        for (var a = 0; a < 3; a++)
            if (alo[a] > bhi[a] + 0.001 || blo[a] > ahi[a] + 0.001)
                return false;
        return true;
    }

    /// <summary>Whether growing the box's faces in <paramref name="grow"/> by d leaves it sharing a face plane with another box it
    /// touches, where it didn't before (also a plane it already had, now reaching a box it passed by).</summary>
    private static bool OnAPlane(List<PyDict> boxes, PyDict self, double[] lo, double[] hi, bool[] grow, double d)
    {
        double[] nlo = [.. lo], nhi = [.. hi];
        for (var a = 0; a < 3; a++)
        {
            if (grow[a * 2])
                nlo[a] -= d;
            if (grow[a * 2 + 1])
                nhi[a] += d;
        }
        foreach (var o in boxes)
        {
            if (ReferenceEquals(o, self) || o.Get("min") is null || o.Get("max") is null)
                continue;
            var ol = V3(o["min"]);
            var oh = V3(o["max"]);
            if (Coplanar(nlo, nhi, ol, oh) && !Coplanar(lo, hi, ol, oh))
                return true;
        }
        return false;
    }

    /// <summary>Two boxes with a face plane in common (within 1.5 mm) and an overlap across it.</summary>
    private static bool Coplanar(double[] alo, double[] ahi, double[] blo, double[] bhi)
    {
        for (var a = 0; a < 3; a++)
        {
            if (!(Near(alo[a], blo[a]) || Near(alo[a], bhi[a]) || Near(ahi[a], blo[a]) || Near(ahi[a], bhi[a])))
                continue;
            var u = (a + 1) % 3;
            var v = (a + 2) % 3;
            if (Math.Min(ahi[u], bhi[u]) - Math.Max(alo[u], blo[u]) > 0.0005 && Math.Min(ahi[v], bhi[v]) - Math.Max(alo[v], blo[v]) > 0.0005)
                return true;
        }
        return false;
    }

    private static bool Near(double x, double y) => Math.Abs(x - y) < 0.0015;

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
