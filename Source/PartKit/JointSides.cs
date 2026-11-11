namespace CallOfSlop.PartKit;

/// <summary>
/// The two sides of a joint part, drawn (board item dev-joint-side-indicators-js01): the port of tools/constructs/make_joint_sides.py,
/// and its custom layout (the part file as json.dumps(indent=2), but its "boxes" one box a line, separators ", " and ": ").
///
/// A part's "boxes" are rebuilt: its own solid box gets "side": "base" and a dark fixed-looking colour; a base mounting flange (dark, at
/// the base socket's end) with four bolts is "base"; a moving-side collar (safety orange, over the leaf half) and a direction cue on it (a
/// stepped arrow: one head with a tail tick for a rotating joint, a head at each end for a piston) are "leaf". Collar, flange, bolts and cue
/// are detail boxes, not paintable, so painting a joint never hides which side is which. Faces never share a plane: the collar and flange
/// stand 8 and 5 mm proud on the sides and 4 mm inside the end faces, the cue steps differ by 2.5 mm, and the steps overlap by 2 mm.
/// Idempotent: boxes with a "side" key and the old gold leaf strips (#c7a23a) are dropped before rebuilding; other detail boxes stay.
/// </summary>
public static class JointSides
{
    private const string BaseBody = "#3b4046", Flange = "#202327", Bolt = "#8d939a", Collar = "#f08a12", Cue = "#1c1c1c";

    // Python's min and max of two: the first when they are equal (so 0.0 and -0.0 keep their order).
    private static double Min(double a, double b) => b < a ? b : a;
    private static double Max(double a, double b) => b > a ? b : a;

    private static (int Axis, int Sign) DirOf(string s) => ("xyz".IndexOf(s[1]), s[0] == '+' ? 1 : -1);

    private static PyDict Mk(double[] lo, double[] hi, string colour, string side) => new()
    {
        ["size"] = Enumerable.Range(0, 3).Select(i => (object?)Py.Round(hi[i] - lo[i], 4)).ToList(),
        ["at"] = Enumerable.Range(0, 3).Select(i => (object?)Py.Round((hi[i] + lo[i]) / 2, 4)).ToList(),
        ["color"] = colour,
        ["detail"] = true,
        ["side"] = side,
        ["paintable"] = false,
    };

    private static (double[], double[]) Span(Dictionary<int, (double L, double H)> ranges)
    {
        var lo = new double[3];
        var hi = new double[3];
        foreach (var (ax, (l, hh)) in ranges)
            (lo[ax], hi[ax]) = (Min(l, hh), Max(l, hh));
        return (lo, hi);
    }

    /// <summary>
    /// Whether the pass draws this part's sides: a hinge or joint of the small class that isn't a rail or a swivel, and whose own box is a
    /// "size" box. Parts whose generator draws their sides itself are left alone: a size class's joints (large_systems), rails and free
    /// rotation parts (BigDriveParts, FreeParts, LinearParts) and the door hinges (DoorParts: min/max boxes with their own "side" knuckles,
    /// which made the Python pass fail with KeyError 'size' once door_parts wrote them).
    /// </summary>
    public static bool Applies(PyDict d)
    {
        if (d.Get("role") is not ("joint" or "hinge") || Py.Truthy(d.Get("sizeClass")))
            return false;
        if (d.Get("joint") is PyDict j && (j.Has("rail") || j.Get("type") as string == "swivel"))
            return false;
        return Main(d) is { } m && m.Has("size");
    }

    private static List<PyDict> Kept(PyDict d) =>
        [.. ((List<object?>)d["boxes"]!).Cast<PyDict>().Where(b => !(Py.Truthy(b.Get("detail")) && b.Has("side")) && b.Get("color") as string != "#c7a23a")];

    private static PyDict? Main(PyDict d) => Kept(d).FirstOrDefault(b => !Py.Truthy(b.Get("detail")));

    /// <summary>make_joint_sides.build: the part's new boxes.</summary>
    public static List<PyDict> Build(PyDict d)
    {
        var jb = Py.Truthy(d.Get("joint")) ? (PyDict)d["joint"]! : (PyDict)d["hinge"]!;
        var keep = Kept(d);
        var main = keep.First(b => !Py.Truthy(b.Get("detail")));
        var others = keep.Where(b => !ReferenceEquals(b, main)).ToList();
        double[] c = main.Get("at") is List<object?> at ? [.. at.Select(Py.D)] : [0, 0, 0];
        double[] h = [.. ((List<object?>)main["size"]!).Select(v => Py.D(v) / 2)];
        var socks = new Dictionary<string, PyDict>();
        foreach (var s in ((List<object?>)d["sockets"]!).Cast<PyDict>())
            socks[(string)s["name"]!] = s;
        var (a, sa) = DirOf((string)socks[(string)jb["leaf"]!]["dir"]!);
        var (b_, sb) = DirOf((string)socks["base"]["dir"]!);
        var mainOut = new PyDict(main) { ["color"] = BaseBody, ["side"] = "base" };
        var o = new List<PyDict> { mainOut };
        o.AddRange(others);

        var ina = Min(0.008, h[a] * 0.25); // (the collar and flange end this far inside their faces)
        var inb = Min(0.008, h[b_] * 0.25);
        // Moving-side collar over the leaf half.
        var cr = new Dictionary<int, (double L, double H)>();
        for (var ax = 0; ax < 3; ax++)
            cr[ax] = ax == a ? (c[a] + sa * 0.1 * h[a], c[a] + sa * (h[a] - ina)) : (c[ax] - h[ax] - 0.008, c[ax] + h[ax] + 0.008);
        var (clo, chi) = Span(cr);
        o.Add(Mk(clo, chi, Collar, "leaf"));
        // Base flange and its bolts.
        var tb = Min(0.04, h[b_] * 0.8);
        var fr = new Dictionary<int, (double L, double H)>();
        for (var ax = 0; ax < 3; ax++)
            fr[ax] = ax == b_ ? (c[b_] + sb * (h[b_] - inb - tb), c[b_] + sb * (h[b_] - inb)) : (c[ax] - h[ax] - 0.005, c[ax] + h[ax] + 0.005);
        var (flo, fhi) = Span(fr);
        o.Add(Mk(flo, fhi, Flange, "base"));
        var mid = c[b_] + sb * (h[b_] - inb - tb / 2);
        var ps = Enumerable.Range(0, 3).Where(ax => ax != b_).ToArray();
        int p1 = ps[0], p2 = ps[1];
        foreach (var s1 in new[] { -1, 1 })
            foreach (var s2 in new[] { -1, 1 })
            {
                var ctr = new double[3];
                ctr[b_] = mid;
                ctr[p1] = c[p1] + s1 * (h[p1] + 0.005);
                ctr[p2] = c[p2] + s2 * (h[p2] + 0.005);
                var (blo, bhi) = Span(Enumerable.Range(0, 3).ToDictionary(ax => ax, ax => (ctr[ax] - 0.01, ctr[ax] + 0.01)));
                o.Add(Mk(blo, bhi, Bolt, "base"));
            }
        // Direction cue on the collar's +n face.
        var axisA = jb.Get("axis") is string axs ? "xyz".IndexOf(axs[1]) : a;
        var sliding = jb.Get("type") as string == "piston";
        var n = axisA != a ? Enumerable.Range(0, 3).First(ax => ax != axisA && ax != a) : (a != 0 ? 0 : 1);
        var t = sliding ? axisA : 3 - n - axisA;
        var w = 3 - n - t;
        var extT = Math.Abs(cr[t].H - cr[t].L);
        var extW = Math.Abs(cr[w].H - cr[w].L);
        var lg = Min(extT * 0.8, 0.6);
        if (lg >= 0.05)
        {
            var u = Max(0.011, Min(Min(extW, extT) / 9, 0.03));
            while (5 * u > extW * 0.9 && u > 0.011)
                u -= 0.002;
            if (5 * u <= extW * 0.95)
            {
                var f = cr[n].H;
                var wc = (cr[w].L + cr[w].H) / 2;
                var tc = (cr[t].L + cr[t].H) / 2;
                var T = lg / 2;
                PyDict Glyph(double t0, double t1, double across, double up)
                {
                    var lo = new double[3];
                    var hi = new double[3];
                    (lo[t], hi[t]) = (tc + Min(t0, t1), tc + Max(t0, t1));
                    (lo[w], hi[w]) = (wc - across / 2, wc + across / 2);
                    (lo[n], hi[n]) = (f - 0.0045, f + up);
                    return Mk(lo, hi, Cue, "leaf");
                }
                int[] ends = sliding ? [1, -1] : [1];
                o.Add(Glyph(-T + (!sliding ? 0.5 * u : -(T - 1.5 * u)), T - 1.5 * u, u, 0.0065));
                foreach (var e in ends)
                {
                    o.Add(Glyph(e * (T - 2 * u), e * (T - u), 5 * u, 0.009));
                    o.Add(Glyph(e * (T - u - 0.002), e * T, 3 * u, 0.0115));
                }
                if (!sliding)
                    o.Add(Glyph(-T, -T + u, 3 * u, 0.009));
            }
        }
        return o;
    }

    /// <summary>make_joint_sides.fmt: the boxes list, one box a line.</summary>
    public static string Fmt(List<PyDict> boxes) =>
        "[\n" + string.Join(",\n", boxes.Select(b => "    " + PyJson.Dumps(b, itemSep: ", ", keySep: ": "))) + "\n  ]";

    /// <summary>A part file's text with its "boxes" list rebuilt (the text around it kept as it is).</summary>
    public static string Apply(string txt)
    {
        var d = (PyDict)PyJson.Loads(txt)!;
        var boxes = Build(d);
        var i = txt.IndexOf("\"boxes\"", StringComparison.Ordinal);
        var j = txt.IndexOf('[', i);
        int depth = 0, k = j;
        while (true)
        {
            depth += txt[k] == '[' ? 1 : 0;
            depth -= txt[k] == ']' ? 1 : 0;
            if (depth == 0)
                break;
            k++;
        }
        return txt[..j] + Fmt(boxes) + txt[(k + 1)..];
    }
}
