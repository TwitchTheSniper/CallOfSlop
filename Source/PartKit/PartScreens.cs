namespace CallOfSlop.PartKit;

/// <summary>
/// A machine's built-in screen: a dark plate on the flattest free spot of its model, and the "panels" entry that draws on it. The port of
/// tools/constructs/screens.py (docs/sandbox/constructs/joints-and-logic.md, "Machine screens"), now deleted.
///
/// Tiers and sizes have different models, so a screen is placed by looking at each one, not by a table: <see cref="Mount"/> takes the drawn
/// boxes as seen from each side, slides a screen-sized window over them, and picks the spot that is flattest, near the height a player reads
/// at, and clear of every socket, port and die on that side. The plate fills what lies under the window and stands a little proud of the
/// highest thing there, so it never cuts into a detail and shares no face with it. With lamp (a machine of a production line) the plate
/// grows a band on top and a status lamp stands in it, the screen's width, proud of the plate: the game draws it in the line state's colour.
/// </summary>
public static class PartScreens
{
    private const string Plate = "#14181c";
    private const string Lamp = "#2b3036"; // (the status lamp unlit: the game colours it by the machine's state)
    private const double Aspect = 2.8; // width over height: four rows of 18 characters
    private const int StepsU = 13, StepsY = 7; // sample points across the window

    private static (int Axis, int Sign) Face(string f) => f switch { "-z" => (2, -1), "+z" => (2, 1), "+x" => (0, 1), _ => (0, -1) };

    // Python's min and max of two: the first when they are equal.
    private static double Min(double a, double b) => b < a ? b : a;
    private static double Max(double a, double b) => b > a ? b : a;

    private static double V(PyDict b, string k, int a) => Py.D(((List<object?>)b[k]!)[a]);

    /// <summary>How far out the model stands at (u, y) seen from this side, or null where there is nothing.</summary>
    private static double? Seen(List<PyDict> boxes, int axis, int sign, double u, double y)
    {
        var other = 2 - axis;
        double? best = null;
        foreach (var b in boxes)
        {
            if (Py.Truthy(b.Get("hidden")) || Py.Truthy(b.Get("glass")))
                continue;
            if (V(b, "min", other) <= u && u <= V(b, "max", other) && V(b, "min", 1) <= y && y <= V(b, "max", 1))
            {
                var at = sign > 0 ? V(b, "max", axis) : -V(b, "min", axis);
                best = best is { } bb ? Max(bb, at) : at;
            }
        }
        return best;
    }

    private static (double Lo, double Hi)? Window(List<PyDict> boxes, int axis, int sign, double cu, double cy, double w, double h)
    {
        double? lo = null, hi = null;
        for (var i = 0; i < StepsU; i++)
            for (var j = 0; j < StepsY; j++)
            {
                var at = Seen(boxes, axis, sign, cu + (i / (double)(StepsU - 1) - 0.5) * w, cy + (j / (double)(StepsY - 1) - 0.5) * h);
                if (at is not { } a)
                    return null;
                lo = lo is { } l ? Min(l, a) : a;
                hi = hi is { } hh ? Max(hh, a) : a;
            }
        return (lo!.Value, hi!.Value);
    }

    private static List<object?> R4(double[] v) => [.. v.Select(x => (object?)Py.Round(x, 4))];

    /// <summary>
    /// The plate box and the panel, and the status lamp's box with lamp (else null). keepClear: (at, face) of sockets, ports and dies; a
    /// window on that side stays off them.
    /// </summary>
    public static (PyDict Plate, PyDict Panel, PyDict? Status) Mount(List<PyDict> boxes, List<(double[] At, string Face)> keepClear, string[]? faces = null, int rows = 4, int rangeM = 30, double? width = null, bool lamp = false)
    {
        faces ??= ["-z", "-x", "+x", "+z"];
        var drawn = boxes.Where(b => !Py.Truthy(b.Get("hidden"))).ToList();
        double[] lo = [.. Enumerable.Range(0, 3).Select(a => drawn.Select(b => V(b, "min", a)).Aggregate(Min))];
        double[] hi = [.. Enumerable.Range(0, 3).Select(a => drawn.Select(b => V(b, "max", a)).Aggregate(Max))];
        var across = Max(hi[0] - lo[0], hi[2] - lo[2]);
        var w = width is { } wv && wv != 0 ? wv : Py.Round(Min(across * 0.38, 3.6), 2);
        var h = Py.Round(w / Aspect, 2);
        var border = Py.Round(h * 0.06, 3);
        var lampH = lamp ? Py.Round(Max(0.1, h * 0.24), 3) : 0; // (from 20 m a 0.1 m bar is a few pixels high: enough to read a colour and a blink)
        var band = lamp ? lampH + border : 0; // (the lamp's band over the screen, a border below it)
        double pw = w + 2 * border, ph = h + 2 * border + band; // (the plate)
        var target = Min(Max(1.6, (hi[1] - lo[1]) * 0.36), 3); // (on a capital machine too, it is read from the ground)
        (double Cost, string Face, double Cu, double Cy, (double Lo, double Hi) Seen)? best = null;
        for (var order = 0; order < faces.Length; order++)
        {
            var face = faces[order];
            var (axis, sign) = Face(face);
            var other = 2 - axis;
            var clear = keepClear.Where(k => k.Face == face).Select(k => k.At).ToList();
            var span = hi[other] - lo[other] - pw;
            for (var iu = -8; iu < 9; iu++)
            {
                var cu = (lo[other] + hi[other]) / 2 + iu / 8.0 * span / 2;
                for (var iy = 0; iy < 25; iy++)
                {
                    var cy = lo[1] + ph / 2 + 0.02 + iy / 24.0 * (hi[1] - lo[1] - ph - 0.04);
                    if (clear.Any(at => Math.Abs(at[other] - cu) < pw / 2 + 0.05 && Math.Abs(at[1] - cy) < ph / 2 + 0.05))
                        continue;
                    if (Window(boxes, axis, sign, cu, cy, pw, ph) is not { } seenAt)
                        continue;
                    var cost = (seenAt.Hi - seenAt.Lo) * 1.5 + Math.Abs(cy - target) * 1.0 + Math.Abs(cu - (lo[other] + hi[other]) / 2) * 0.1 + order * 0.1;
                    if (best is null || cost < best.Value.Cost)
                        best = (cost, face, cu, cy, seenAt);
                }
            }
        }
        if (best is not { } bst)
            throw new InvalidOperationException("no room for a screen");
        var (_, bf, bcu, bcy, (near, far)) = bst;
        var (ax, sg) = Face(bf);
        var oth = 2 - ax;
        var proud = Max(0.012, h * 0.03);
        var outer = far + proud;
        var inner = near - Max(0.02, h * 0.04);
        double[] mn = new double[3], mx = new double[3];
        (mn[oth], mx[oth]) = (bcu - pw / 2, bcu + pw / 2);
        (mn[1], mx[1]) = (bcy - ph / 2, bcy + ph / 2);
        (mn[ax], mx[ax]) = sg > 0 ? (inner, outer) : (-outer, -inner);
        var plate = new PyDict { ["min"] = R4(mn), ["max"] = R4(mx), ["color"] = Plate, ["detail"] = true };
        var atv = new object?[] { 0, 0, 0 };
        atv[oth] = Py.Round(bcu, 4);
        atv[1] = Py.Round(bcy - band / 2, 4);
        atv[ax] = Py.Round(sg * (outer + 0.004), 4);
        var panel = new PyDict
        {
            ["source"] = "machine", ["at"] = atv.ToList(), ["size"] = Py.L(Py.Round(w, 3), Py.Round(h, 3)), ["face"] = bf, ["up"] = "+y", ["rows"] = rows, ["range"] = rangeM,
        };
        if (!lamp)
            return (plate, panel, null);
        var deep = Max(0.02, h * 0.05); // (it stands this far proud of the plate; its back is inside the plate, so no face is shared)
        double[] lo2 = new double[3], hi2 = new double[3];
        (lo2[oth], hi2[oth]) = (bcu - w / 2, bcu + w / 2);
        (lo2[1], hi2[1]) = (bcy + ph / 2 - border - lampH, bcy + ph / 2 - border);
        (lo2[ax], hi2[ax]) = sg > 0 ? (outer - proud / 2, outer + deep) : (-outer - deep, -outer + proud / 2);
        var status = new PyDict { ["min"] = R4(lo2), ["max"] = R4(hi2), ["color"] = Lamp, ["detail"] = true, ["paintable"] = false, ["status"] = true };
        return (plate, panel, status);
    }

    /// <summary>Adds the plate to a part's boxes: what it hides goes, and if the part is at its limit of boxes (each a draw call), so does its smallest detail.</summary>
    public static void Fit(List<PyDict> boxes, PyDict plate, int most = 32)
    {
        bool Inside(PyDict b) => Enumerable.Range(0, 3).All(a => V(plate, "min", a) <= V(b, "min", a) && V(b, "max", a) <= V(plate, "max", a));
        double Size(PyDict b) => (V(b, "max", 0) - V(b, "min", 0)) * (V(b, "max", 1) - V(b, "min", 1)) * (V(b, "max", 2) - V(b, "min", 2));
        boxes.RemoveAll(b => Py.Truthy(b.Get("detail")) && Inside(b));
        while (boxes.Count >= most)
        {
            PyDict? small = null;
            foreach (var b in boxes)
                if (Py.Truthy(b.Get("detail")) && (small is null || Size(b) < Size(small)))
                    small = b;
            boxes.Remove(small!);
        }
        boxes.Add(plate);
    }
}
