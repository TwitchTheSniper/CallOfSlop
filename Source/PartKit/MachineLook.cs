using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit;

/// <summary>
/// The look every production machine shares (docs/sandbox/constructs/look.md, "Machine look"): three tones (a dark structural base, the
/// family's body colour, a bright accent only on what a hand works or what moves), bare steel on working parts, the in side marked white
/// and the out side orange with an arrow each, hazard paint at moving ports, and one control face (screen, lamp over it, a button shelf
/// under it). Everything here is a detail box: drawn only, so a machine's collision, bounds and sockets stay its solid boxes'. The base,
/// steel, accent and markings don't take paint, so a painted machine keeps its three tones.
/// Faces sit a few millimetres apart by construction (CLAUDE.md, "No coplanar faces"); Part.Json's corrector settles the rest.
/// </summary>
public static class MachineLook
{
    public const string Base = "#22262b"; // the structural tone: skirts, frames, guards, housings
    public const string Steel = "#98a1a9"; // bare steel: dies, rams, rollers, hubs, beds
    public const string Accent = "#f2b01e"; // what a hand works and what moves
    public const string In = "#ecebe4"; // the in side's band and arrow
    public const string Out = "#f07818"; // the out side's band and arrow
    public const string Ink = "#15171a"; // an arrow on a band
    public const string Stop = "#d02a1e"; // the stop button
    public const string Hot = "#5a2414"; // a die or a spout: dull until its machine works ("work": "glow")

    // The control face of a machine a player stands at (metres above its floor).
    public const double EyeY = 1.5; // the screen's middle
    public const double HandY = 1.15; // the button shelf's top

    /// <summary>A look box. It doesn't take paint unless <paramref name="paintable"/> (a body-tone box).</summary>
    public static PyDict? D(double[] mn, double[] mx, string color, string? finish = null, bool paintable = false, PyDict? shape = null)
    {
        if (Math.Min(mx[0] - mn[0], Math.Min(mx[1] - mn[1], mx[2] - mn[2])) is > 0.0099 and < 0.0115)
            throw new InvalidOperationException($"a look box {mx[0] - mn[0]:0.###} x {mx[1] - mn[1]:0.###} x {mx[2] - mn[2]:0.###} m: the game refuses under 1 cm, so keep 12 mm");
        var b = Box(mn, mx, color, detail: true, shape: shape);
        if (b is null)
            return null;
        if (!paintable)
            b["paintable"] = false;
        if (finish is not null)
            b["finish"] = finish;
        return b;
    }

    /// <summary>
    /// A port's fitting, the same on every machine: a dark spout that reaches back <paramref name="depth"/> from the port's
    /// point into the body, and a rim round its mouth in the side's colour (white in, orange out). The port is at the middle
    /// of the mouth's plane; nothing comes within 2.5 cm of it, so the link's end stays in view (the "Machine tier looks"
    /// rule: no look box within 2 cm of a port). <paramref name="n"/>: the axis the port faces along, <paramref name="sg"/> its
    /// sign; <paramref name="half"/>: half the spout's width. Looks only.
    /// </summary>
    public static List<PyDict> Mouth(double[] at, int n, int sg, double half, double depth, bool input)
    {
        const double clear = 0.026, lip = 0.012;
        int u = (n + 1) % 3, v = (n + 2) % 3;
        var list = new List<PyDict>();
        void Add(double n0, double n1, double u0, double u1, double v0, double v1, string color)
        {
            double[] mn = new double[3], mx = new double[3];
            (mn[n], mx[n]) = sg > 0 ? (at[n] - n1, at[n] - n0) : (at[n] + n0, at[n] + n1); // (n0, n1: how far behind the port's plane)
            (mn[u], mx[u], mn[v], mx[v]) = (at[u] + u0, at[u] + u1, at[v] + v0, at[v] + v1);
            if (D(mn, mx, color) is { } box)
                list.Add(box);
        }
        if (depth >= clear + 0.014)
            Add(clear, depth, -half, half, -half, half, Base);
        var o = half + lip;
        var rim = input ? In : Out;
        Add(0.003, clear + 0.008, -o, o, clear, o, rim);
        Add(0.003, clear + 0.008, -o, o, -o, -clear, rim);
        Add(0.003, clear + 0.008, clear, o, -clear, clear, rim);
        Add(0.003, clear + 0.008, -o, -clear, -clear, clear, rim);
        return list;
    }

    /// <summary>A box that shows work (the game's "work" tag): "spin" about <paramref name="axis"/>, "stroke" along it by <paramref name="travel"/>, "glow" or "smoke".</summary>
    public static PyDict? Work(PyDict? b, string kind, string? axis = null, double travel = 0)
    {
        if (b is null)
            return null;
        b["work"] = kind;
        if (axis is not null)
            b["axis"] = axis;
        if (travel != 0)
            b["travel"] = R(travel);
        return b;
    }

    /// <summary>A dark skirt round a body's foot, <paramref name="proud"/> of its faces.</summary>
    public static void Skirt(Part p, double x0, double x1, double z0, double z1, double y0, double y1, double proud, string color = Base)
    {
        var t = Math.Max(0.012, proud);
        p.Add(D([x0 - proud, y0, z0 - proud], [x1 + proud, y1, z0 - proud + t], color));
        p.Add(D([x0 - proud, y0, z1 + proud - t], [x1 + proud, y1, z1 + proud], color));
        p.Add(D([x0 - proud, y0, z0 - proud + t], [x0 - proud + t, y1, z1 + proud - t], color));
        p.Add(D([x1 + proud - t, y0, z0 - proud + t], [x1 + proud, y1, z1 + proud - t], color));
    }

    /// <summary>
    /// An arrow drawn flat on a face: on the plane across axis <paramref name="n"/> at <paramref name="at"/> (standing out the way of
    /// <paramref name="sn"/>), pointing along axis <paramref name="a"/> the way of <paramref name="sa"/>, its middle at (ca, cb) on the
    /// other two axes. Four boxes: a shaft and a stepped head.
    /// </summary>
    public static void Arrow(Part p, int n, double at, int sn, int a, int sa, double ca, double cb, double len, double wid, string color, double thick = 0.012)
    {
        var b = 3 - n - a;
        void Bar(double a0, double a1, double w)
        {
            double[] mn = new double[3], mx = new double[3];
            (mn[n], mx[n]) = sn > 0 ? (at, at + thick) : (at - thick, at);
            var (lo, hi) = sa > 0 ? (ca + a0 * len, ca + a1 * len) : (ca - a1 * len, ca - a0 * len);
            (mn[a], mx[a]) = (lo, hi);
            (mn[b], mx[b]) = (cb - w * wid / 2, cb + w * wid / 2);
            p.Add(D(mn, mx, color));
        }
        Bar(-0.5, 0, 0.36);
        Bar(0, 0.17, 1);
        Bar(0.17, 0.34, 0.66);
        Bar(0.34, 0.5, 0.32);
    }

    /// <summary>A strip of hazard paint (one box: the finish draws the stripes).</summary>
    public static void Hazard(Part p, double[] mn, double[] mx) => p.Add(D(mn, mx, Accent, "hazard paint"));

    /// <summary>
    /// The button shelf of a control face, under a screen's plate: a dark shelf, three accent buttons and a red stop, on the screen's side
    /// of the machine. <paramref name="plate"/> is PartScreens.Mount's plate box, <paramref name="face"/> the panel's face.
    /// </summary>
    public static void Buttons(Part p, PyDict plate, string face) => Buttons(p.Boxes, plate, face);

    public static void Buttons(List<PyDict> boxes, PyDict plate, string face)
    {
        double V(string k, int i) => Py.D(((List<object?>)plate[k]!)[i]);
        var ax = face[1] == 'x' ? 0 : 2;
        var sg = face[0] == '+' ? 1 : -1;
        var u = 2 - ax;
        double u0 = V("min", u), u1 = V("max", u), w = u1 - u0;
        var top = V("min", 1) - Math.Max(0.02, w * 0.03); // (a gap under the plate)
        var deep = Math.Max(0.05, w * 0.1);
        var tall = Math.Max(0.03, w * 0.05);
        var outer = sg > 0 ? V("max", ax) : V("min", ax);
        void Add(double a0, double a1, double y0, double y1, double d0, double d1, string color)
        {
            double[] mn = new double[3], mx = new double[3];
            (mn[u], mx[u]) = (a0, a1);
            (mn[1], mx[1]) = (y0, y1);
            (mn[ax], mx[ax]) = sg > 0 ? (outer + d0, outer + d1) : (outer - d1, outer - d0);
            if (D(mn, mx, color) is { } b)
                boxes.Add(b);
        }
        Add(u0, u1, top - tall, top, -0.02, deep, Base);
        var cap = Math.Max(0.012, tall * 0.4);
        var bw = w * 0.12;
        for (var i = 0; i < 3; i++)
        {
            var c = u0 + w * (0.14 + 0.2 * i);
            Add(c - bw / 2, c + bw / 2, top, top + cap, deep * 0.2, deep * 0.8, Accent);
        }
        var s = u0 + w * 0.86; // (the stop: bigger, red, at the end)
        Add(s - bw * 0.65, s + bw * 0.65, top, top + cap * 1.6, deep * 0.1, deep * 0.9, Stop);
    }

    /// <summary>How many drawn-only boxes a part has (the look budget).</summary>
    public static int Details(Part p) => p.Boxes.Count(b => Py.Truthy(b.Get("detail")));
}

/// <summary>MachineLook.D as L, for generators that write many look boxes (using static).</summary>
public static class MachineLookShort
{
    public static PyDict? L(double[] mn, double[] mx, string color, string? finish = null, bool paintable = false, PyDict? shape = null) => MachineLook.D(mn, mx, color, finish, paintable, shape);
}
