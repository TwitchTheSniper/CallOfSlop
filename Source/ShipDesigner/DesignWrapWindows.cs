using System.Text;
using System.Text.Json.Nodes;

namespace CallOfSlop.ShipDesigner;

/// <summary>
/// A wraparound window (Karl, 2026-11-27: "mainly useful for cockpits"): a band of glass that wraps round a body the way a nacelle's
/// connection goes round its skin, from a ring on the skin at <see cref="Z"/>, <see cref="Span"/> degrees round the body's axis about
/// <see cref="Turn"/> (0 the top). The glass leans back from upright by <see cref="Rake"/> (0: upright, looking straight ahead), is cut into
/// <see cref="Panels"/> panes with beading between them, and can stand forward in the middle (<see cref="Bow"/>, curving in from
/// <see cref="BowStart"/>). Its frame fits itself to the skin: a sill runs from the glass's foot back to the skin and a header from its top,
/// so upright glass on a raked nose has a roof back to the skin over it, and glass standing forward a deep sill under it, and the skin behind the glass is cut away (DesignScene.WrapCuts).
/// </summary>
public sealed class WrapWindow
{
    public const int MaxPanels = 12;
    public const double MinSpan = 20, MaxSpan = 330, MaxRake = 85, MaxTurn = 180, MaxBow = 2, MinHeight = 0.2, MaxHeight = 3;
    public const double MinBead = 0.02, MaxBead = 0.3, MinFrame = 0.02, MaxFrame = 0.3;
    /// <summary>How far back along the body a header looks for the skin before it drops straight in onto it instead (m).</summary>
    public const double Reach = 4;

    public int Id, Hull;
    public double Z;              // hull-local z of the ring its foot stands on
    public double Turn;           // degrees round the body of its middle: 0 the top, 90 the +x side, 180 the keel
    public double Span = 150;     // degrees round the body it wraps
    public double Rake = 40;      // degrees the glass leans back from upright
    public double Height = 0.9;   // the glass's height along its face (m)
    public int Panels = 5;
    public double Bead = 0.08;    // the beading between panes (m across)
    public double Frame = 0.06;   // the frame round the glass's top and foot and its two ends (m)
    public double Bow;            // how far the middle stands forward of the ends (m)
    public double BowStart = 0.5; // 0..1 of the half span from the middle: inside it the glass curves, outside it runs straight to the ends
    public bool Flat = true;      // each pane flat between its beads (as a real windscreen's), or the glass one smooth curve

    public JsonObject ToJson(ShipDesign d) => new()
    {
        ["id"] = Id, ["hull"] = d.Hulls.FindIndex(h => h.Id == Hull), ["z"] = ShipDesign.R(Z), ["turn"] = ShipDesign.R(Turn), ["span"] = ShipDesign.R(Span),
        ["rake"] = ShipDesign.R(Rake), ["height"] = ShipDesign.R(Height), ["panels"] = Panels, ["bead"] = ShipDesign.R(Bead), ["frame"] = ShipDesign.R(Frame),
        ["bow"] = ShipDesign.R(Bow), ["bowStart"] = ShipDesign.R(BowStart), ["flat"] = Flat,
    };

    /// <summary>Takes the settings in <paramref name="o"/> that are there, each clamped (a saved window and the editor's set both come here).</summary>
    public void Set(JsonObject o)
    {
        static double F(JsonNode? n, double or) => n is null ? or : (double)n;
        Z = F(o["z"], Z);
        Turn = Math.Clamp(F(o["turn"], Turn), -MaxTurn, MaxTurn);
        Span = Math.Clamp(F(o["span"], Span), MinSpan, MaxSpan);
        Rake = Math.Clamp(F(o["rake"], Rake), 0, MaxRake);
        Height = Math.Clamp(F(o["height"], Height), MinHeight, MaxHeight);
        Panels = Math.Clamp((int?)o["panels"] ?? Panels, 1, MaxPanels);
        Bead = Math.Clamp(F(o["bead"], Bead), MinBead, MaxBead);
        Frame = Math.Clamp(F(o["frame"], Frame), MinFrame, MaxFrame);
        Bow = Math.Clamp(F(o["bow"], Bow), 0, MaxBow);
        BowStart = Math.Clamp(F(o["bowStart"], BowStart), 0.02, 1);
        Flat = (bool?)o["flat"] ?? Flat;
    }

    public string Key => $"{Id}:{Hull}:{Z:R}:{Turn:R}:{Span:R}:{Rake:R}:{Height:R}:{Panels}:{Bead:R}:{Frame:R}:{Bow:R}:{BowStart:R}:{Flat}";

    /// <summary>How far forward the glass stands at <paramref name="s"/> (-1..1 across it), as a share of <see cref="Bow"/>: straight from the
    /// ends in to <see cref="BowStart"/>, then a curve over the middle, so it is thickest in the middle and has no kink.</summary>
    public double Bump(double s)
    {
        var a = Math.Abs(s);
        var st = Math.Clamp(BowStart, 0.02, 1);
        var v = a >= st ? 1 - a : 1 - (a * a / st + st) / 2;
        return v / (1 - st / 2);
    }
}

/// <summary>
/// The wraparound windows' shape (<see cref="WrapWindow"/>): where the glass stands, the frame it fits to the skin, the skin it cuts away and
/// the openings it makes in the walls of the rooms behind it.
/// </summary>
public static partial class DesignScene
{
    /// <summary>A wraparound window laid on its body: N points across, each the glass's foot (B) and top (T), where its sill (Sc) and header (Hc)
    /// meet the skin, its way out from the axis (R) and the glass's outward normal (Nm); Edge, each pane's first point and the last one.</summary>
    public sealed class WrapLay
    {
        public int N;
        public double[][] B = [], T = [], Sc = [], Hc = [], R = [], Nm = [];
        public int[] Edge = [];
        public double Ax, Ay;
        public bool Ok;
        public string Why = "";
    }

    private static readonly Dictionary<int, (string Key, WrapLay L)> WrapLaid = [];

    /// <summary><see cref="LayWrap"/>, kept under the window's settings and its body's shape.</summary>
    public static WrapLay WrapOnce(ShipDesign d, WrapWindow w)
    {
        var key = w.Key + "|" + (d.Hull(w.Hull) is { } h ? MeshJson(h) : "");
        if (WrapLaid.TryGetValue(w.Id, out var hit) && hit.Key == key)
            return hit.L;
        var l = LayWrap(d, w);
        WrapLaid[w.Id] = (key, l);
        return l;
    }

    private static double[] Add(double[] a, double[] b, double s = 1) => [a[0] + b[0] * s, a[1] + b[1] * s, a[2] + b[2] * s];
    private static double[] Lerp(double[] a, double[] b, double t) => [a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t];
    private static double Dot3(double[] a, double[] b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
    private static double[] Cross3(double[] a, double[] b) => [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]];
    private static (double, double, double) Tp(double[] v) => (v[0], v[1], v[2]);

    private static bool In(HullBody h, double[] p) => h.Depth(p[0], p[1], p[2]) > 0;

    /// <summary>Where the line from <paramref name="p"/> along <paramref name="dir"/> first goes into the body within <paramref name="max"/> m
    /// (to a millimetre), or null; a start already inside is its own answer.</summary>
    private static double[]? Into(HullBody h, double[] p, double[] dir, double max)
    {
        if (In(h, p))
            return p;
        const double step = 0.05;
        for (var t = step; t <= max + 1e-9; t += step)
        {
            if (!In(h, Add(p, dir, t)))
                continue;
            double lo = t - step, hi = t;
            for (var it = 0; it < 12; it++)
            {
                var m = (lo + hi) / 2;
                if (In(h, Add(p, dir, m))) hi = m; else lo = m;
            }
            return Add(p, dir, hi);
        }
        return null;
    }

    /// <summary>The skin's distance from the axis point <paramref name="c"/> going out along <paramref name="dir"/> (-1: the point is outside the body).</summary>
    private static double SkinOut(HullBody h, double[] c, double[] dir)
    {
        if (!In(h, c))
            return -1;
        const double step = 0.05;
        for (var t = step; t < 80; t += step)
        {
            if (In(h, Add(c, dir, t)))
                continue;
            double lo = t - step, hi = t;
            for (var it = 0; it < 12; it++)
            {
                var m = (lo + hi) / 2;
                if (In(h, Add(c, dir, m))) lo = m; else hi = m;
            }
            return lo;
        }
        return -1;
    }

    /// <summary>
    /// Lays a wraparound window on its body: the ring of skin points at its z round its span (from the body's axis there), the glass from each
    /// leaning back by its rake (pushed forward by its bow; flat panes pushed out until each clears the skin), clipped where it goes into
    /// the skin, and a sill and a header back from its foot and top to the skin (straight in onto it when the skin is not met within
    /// <see cref="WrapWindow.Reach"/>); the sill straight in under the foot.
    /// </summary>
    public static WrapLay LayWrap(ShipDesign d, WrapWindow w)
    {
        var l = new WrapLay();
        if (d.Hull(w.Hull) is not { } h || h.Stations.Count < 2)
            return Fail("its body is gone");
        if (w.Z < h.Front + 0.05 || w.Z > h.Back - 0.05)
            return Fail("its foot is off the end of its body: move it along");
        Span<double> hs = stackalloc double[HullBody.MaxLoft];
        Span<double> ys = stackalloc double[HullBody.MaxLoft];
        var pn = h.At(w.Z, hs, ys);
        if (pn < 2)
            return Fail("its body has no skin there");
        (l.Ax, l.Ay) = (h.X, h.Y + (ys[0] + ys[pn - 1]) / 2);
        var wz = h.Z + w.Z;
        var axis = new[] { l.Ax, l.Ay, wz };
        int panels = Math.Clamp(w.Panels, 1, WrapWindow.MaxPanels), per = Math.Max(1, (int)Math.Ceiling(w.Span / panels / 3)), n = panels * per + 1;
        l.N = n;
        l.Edge = [.. Enumerable.Range(0, panels + 1).Select(k => k * per)];
        double[] Dir(double s)
        {
            var phi = (w.Turn + s * w.Span / 2) * Math.PI / 180;
            return [Math.Sin(phi), Math.Cos(phi), 0];
        }
        double S(int i) => -1 + 2.0 * i / (n - 1);
        // The foot of the glass: on the skin's ring, then forward by its bow.
        var foot = new double[n][];
        for (var i = 0; i < n; i++)
        {
            var r = Dir(S(i));
            var rho = SkinOut(h, axis, r);
            if (rho < 0)
                return Fail("its body has no skin there");
            // Forward by its bow at the ring's own height (its sill then runs back to the ring), or out onto the skin there where the body
            // grows towards the bow.
            var fz = wz - w.Bow * w.Bump(S(i));
            if (fz < wz - 1e-6 && SkinOut(h, [l.Ax, l.Ay, fz], r) is var rf && rf > rho)
                rho = rf;
            foot[i] = [l.Ax + r[0] * rho, l.Ay + r[1] * rho, fz];
        }
        double[] Out(double[] p) => Unit([p[0] - l.Ax, p[1] - l.Ay, 0]);
        double[] Glass(double[] p) { var r = Out(p); var a = w.Rake * Math.PI / 180; return Unit([r[0] * Math.Cos(a), r[1] * Math.Cos(a), Math.Sin(a)]); }
        double[] Top(double[] b)
        {
            var g = Glass(b);
            var hit = Into(h, Add(b, g, 0.03), g, w.Height - 0.03);
            return hit ?? Add(b, g, w.Height);
        }
        var B = new double[n][];
        var T = new double[n][];
        if (w.Flat)
        {
            // Each pane a flat quad between its edges: the edges pushed out from the axis until every chord between them clears the skin.
            var e = l.Edge.Select(k => (double[])foot[k].Clone()).ToArray();
            for (var it = 0; it < 4; it++)
            {
                var grow = new double[e.Length];
                Array.Fill(grow, 1.0);
                for (var p = 0; p < panels; p++)
                    for (var i = 1; i < per; i++)
                    {
                        var c = Lerp(e[p], e[p + 1], (double)i / per);
                        var u = Out(c);
                        var rc = Math.Sqrt((c[0] - l.Ax) * (c[0] - l.Ax) + (c[1] - l.Ay) * (c[1] - l.Ay));
                        var rs = SkinOut(h, [l.Ax, l.Ay, c[2]], u);
                        if (rc > 1e-6 && rs > 0 && rs + 0.01 > rc)
                        {
                            var k = (rs + 0.01) / rc;
                            (grow[p], grow[p + 1]) = (Math.Max(grow[p], k), Math.Max(grow[p + 1], k));
                        }
                    }
                for (var k = 0; k < e.Length; k++)
                    e[k] = [l.Ax + (e[k][0] - l.Ax) * grow[k], l.Ay + (e[k][1] - l.Ay) * grow[k], e[k][2]];
            }
            var te = e.Select(Top).ToArray();
            for (var p = 0; p < panels; p++)
                for (var i = 0; i <= per; i++)
                {
                    var k = p * per + i;
                    (B[k], T[k]) = (Lerp(e[p], e[p + 1], (double)i / per), Lerp(te[p], te[p + 1], (double)i / per));
                }
        }
        else
            for (var i = 0; i < n; i++)
                (B[i], T[i]) = (foot[i], Top(foot[i]));
        // The sill: straight in from the glass's foot onto the skin under it (deeper where the glass stands forward). The header: back along
        // the body from its top to where the skin rises to meet it (a roof over the cockpit), else straight in onto the skin.
        double[] Down(double[] p) => Into(h, p, Unit([-(p[0] - l.Ax), -(p[1] - l.Ay), 0]), 6) ?? p;
        (l.B, l.T) = (B, T);
        l.Sc = [.. B.Select(Down)];
        l.Hc = [.. T.Select(p => Into(h, p, [0, 0, 1], WrapWindow.Reach) ?? Down(p))];
        l.R = [.. B.Select(Out)];
        l.Nm = new double[n][];
        var lean = w.Rake * Math.PI / 180;
        for (var i = 0; i < n; i++)
        {
            var along = Unit(Add(B[Math.Min(n - 1, i + 1)], B[Math.Max(0, i - 1)], -1));
            var up = Unit(Add(T[i], B[i], -1));
            var nm = Unit(Cross3(along, up));
            double[] want = [l.R[i][0] * Math.Sin(lean), l.R[i][1] * Math.Sin(lean), -Math.Cos(lean)]; // (out of the glass: forward, and out as it leans back)
            l.Nm[i] = Dot3(nm, want) < 0 ? [-nm[0], -nm[1], -nm[2]] : nm;
        }
        l.Ok = true;
        for (var i = 0; i < n && l.Ok; i++)
            if (Math.Sqrt(Dot3(Add(T[i], B[i], -1), Add(T[i], B[i], -1))) < 0.1)
                (l.Ok, l.Why) = (false, "its glass lies in the skin: tilt it up or move it forward");
        return l;

        WrapLay Fail(string why) { l.Ok = false; l.Why = why; return l; }
    }

    /// <summary>
    /// A wraparound window's mesh in the ship's frame: its glass (panes, <see cref="DesignMesh.Panes"/>), the sill and header in the body's own
    /// colour, the cheeks closing each end down to the skin, and the frame and beading standing proud of the glass.
    /// </summary>
    public static DesignMesh WrapMesh(ShipDesign d, WrapWindow w)
    {
        var m = new DesignMesh();
        m.World();
        var l = WrapOnce(d, w);
        if (l.N < 2 || l.B.Length < l.N || d.Hull(w.Hull) is not { } h)
            return m;
        var n = l.N;
        var glass = DesignMesh.Hex(PieceKinds.Glow);
        var skin = DesignMesh.Hex(h.Skin);
        var frame = DesignMesh.Hex(PieceKinds.Armour);
        m.Pane(() =>
        {
            for (var i = 0; i < n - 1; i++)
                m.Quad(Tp(l.B[i]), Tp(l.B[i + 1]), Tp(l.T[i + 1]), Tp(l.T[i]), glass, false);
        });
        static double Len(double[] a, double[] b) => Math.Sqrt((a[0] - b[0]) * (a[0] - b[0]) + (a[1] - b[1]) * (a[1] - b[1]) + (a[2] - b[2]) * (a[2] - b[2]));
        // Sill and header: from the glass's foot and top back to where they meet the skin.
        for (var i = 0; i < n - 1; i++)
        {
            if (Len(l.B[i], l.Sc[i]) > 0.005 || Len(l.B[i + 1], l.Sc[i + 1]) > 0.005)
                m.Quad(Tp(l.Sc[i]), Tp(l.Sc[i + 1]), Tp(l.B[i + 1]), Tp(l.B[i]), skin, false);
            if (Len(l.T[i], l.Hc[i]) > 0.005 || Len(l.T[i + 1], l.Hc[i + 1]) > 0.005)
                m.Quad(Tp(l.T[i]), Tp(l.T[i + 1]), Tp(l.Hc[i + 1]), Tp(l.Hc[i]), skin, false);
        }
        // Cheeks: each end closed from the glass's edge, its sill and its header down to the skin between them.
        foreach (var i in new[] { 0, n - 1 })
        {
            var poly = new List<double[]> { l.B[i], l.T[i], l.Hc[i] };
            for (var k = 1; k < 8; k++)
            {
                var q = Lerp(l.Hc[i], l.Sc[i], k / 8.0);
                var u = Unit([q[0] - l.Ax, q[1] - l.Ay, 0]);
                var rs = SkinOut(h, [l.Ax, l.Ay, q[2]], u);
                poly.Add(rs > 0 ? [l.Ax + u[0] * rs, l.Ay + u[1] * rs, q[2]] : q);
            }
            poly.Add(l.Sc[i]);
            var c = new double[3];
            foreach (var p in poly)
                (c[0], c[1], c[2]) = (c[0] + p[0] / poly.Count, c[1] + p[1] / poly.Count, c[2] + p[2] / poly.Count);
            for (var k = 0; k < poly.Count; k++)
                if (Len(poly[k], poly[(k + 1) % poly.Count]) > 1e-4)
                    m.Tri(Tp(c), Tp(poly[k]), Tp(poly[(k + 1) % poly.Count]), skin, false);
        }
        // A bar standing proud of the glass, between two lines along it (a0..a1 and b0..b1), raised along each point's glass normal.
        void Bar(double[] a0, double[] a1, double[] b0, double[] b1, double[] na, double[] nb, double rise)
        {
            double[] A0 = Add(a0, na, rise), A1 = Add(a1, nb, rise), B0 = Add(b0, na, rise), B1 = Add(b1, nb, rise);
            m.Quad(Tp(A0), Tp(A1), Tp(B1), Tp(B0), frame, false);
            m.Quad(Tp(a0), Tp(a1), Tp(A1), Tp(A0), frame, false);
            m.Quad(Tp(b0), Tp(b1), Tp(B1), Tp(B0), frame, false);
            m.Quad(Tp(a0), Tp(b0), Tp(B0), Tp(A0), frame, false);
            m.Quad(Tp(a1), Tp(b1), Tp(B1), Tp(A1), frame, false);
        }
        const double rise = 0.025;
        // The frame along the glass's foot and its top, pane by pane.
        for (var i = 0; i < n - 1; i++)
        {
            double[] Up(int k) => Unit(Add(l.T[k], l.B[k], -1));
            double fa = Math.Min(w.Frame, Len(l.T[i], l.B[i]) / 3), fb = Math.Min(w.Frame, Len(l.T[i + 1], l.B[i + 1]) / 3);
            Bar(l.B[i], l.B[i + 1], Add(l.B[i], Up(i), fa), Add(l.B[i + 1], Up(i + 1), fb), l.Nm[i], l.Nm[i + 1], rise);
            Bar(Add(l.T[i], Up(i), -fa), Add(l.T[i + 1], Up(i + 1), -fb), l.T[i], l.T[i + 1], l.Nm[i], l.Nm[i + 1], rise);
        }
        // The posts: the frame's width at each end, the beading's between panes, centred on the line between them (inside it at the ends).
        for (var e = 0; e < l.Edge.Length; e++)
        {
            var k = l.Edge[e];
            var end = e == 0 || e == l.Edge.Length - 1;
            var along = Unit(Add(l.B[Math.Min(n - 1, k + 1)], l.B[Math.Max(0, k - 1)], -1));
            var alongT = Unit(Add(l.T[Math.Min(n - 1, k + 1)], l.T[Math.Max(0, k - 1)], -1));
            var wd = end ? w.Frame : w.Bead;
            double s0 = end ? (e == 0 ? 0 : -wd) : -wd / 2, s1 = end ? (e == 0 ? wd : 0) : wd / 2;
            Bar(Add(l.B[k], along, s0), Add(l.T[k], alongT, s0), Add(l.B[k], along, s1), Add(l.T[k], alongT, s1), l.Nm[k], l.Nm[k], rise + 0.005);
        }
        return m;
    }

    /// <summary>A mesh's panes and the rest apart (the page's glass and frame).</summary>
    public static (DesignMesh Solid, DesignMesh Glass) SplitPanes(DesignMesh m)
    {
        var (a, b) = (new DesignMesh(), new DesignMesh());
        for (var t = 0; t < m.Tris; t++)
        {
            var o = m.InPane(t) ? b : a;
            o.P.AddRange(m.P.GetRange(t * 9, 9));
            o.C.AddRange(m.C.GetRange(t * 9, 9));
        }
        return (a, b);
    }

    /// <summary>The cuts the wraparound windows on body <paramref name="h"/> make in its skin: the skin between each window's sill and header,
    /// a few degrees of it at a time, each cut along its own way out from the axis.</summary>
    private static IEnumerable<Cutout> WrapCuts(ShipDesign d, HullBody h)
    {
        foreach (var w in d.WrapWindows)
        {
            if (w.Hull != h.Id)
                continue;
            var l = WrapOnce(d, w);
            if (!l.Ok)
                continue;
            for (var a = 0; a < l.N - 1; a += 4)
            {
                var b = Math.Min(l.N - 1, a + 4);
                var pts = new List<double[]>();
                for (var i = a; i <= b; i++) pts.Add(l.Sc[i]);
                for (var i = b; i >= a; i--) pts.Add(l.Hc[i]);
                var o = new double[3];
                foreach (var p in pts)
                    (o[0], o[1], o[2]) = (o[0] + p[0] / pts.Count, o[1] + p[1] / pts.Count, o[2] + p[2] / pts.Count);
                var nrm = Unit(Add(l.R[a], l.R[b]));
                var u = Unit(Add(l.B[b], l.B[a], -1));
                u = Unit(Add(u, nrm, -Dot3(u, nrm)));
                var v = Cross3(nrm, u);
                if (Dot3(v, v) < 0.5)
                    continue;
                var poly = new List<(double U, double V)>();
                double lo = double.MaxValue, hi = double.MinValue;
                foreach (var p in pts)
                {
                    var q = Add(p, o, -1);
                    poly.Add((Dot3(q, u), Dot3(q, v)));
                    (lo, hi) = (Math.Min(lo, Dot3(q, nrm)), Math.Max(hi, Dot3(q, nrm)));
                }
                yield return new Cutout(o, u, nrm, v, poly, 0, (lo - 0.15, hi + 0.1)) { GapSet = 0.04 };
            }
        }
    }

    /// <summary>The text of every wraparound window's cuts on body h, for its key.</summary>
    private static string WrapCutKey(ShipDesign d, HullBody h)
    {
        var k = new StringBuilder();
        foreach (var w in d.WrapWindows)
            if (w.Hull == h.Id)
                k.Append('^').Append(w.Key);
        return k.ToString();
    }

    /// <summary>The openings the wraparound windows make in the walls of the rooms behind them: a few degrees of glass at a time looking
    /// in, each room wall's share joined into one opening.</summary>
    private static List<double[]> WrapWays(ShipDesign d)
    {
        var o = new List<double[]>();
        int up = d.Stacked ? 2 : 1;
        foreach (var w in d.WrapWindows)
        {
            var l = WrapOnce(d, w);
            if (!l.Ok)
                continue;
            var merged = new Dictionary<(int Room, int Face), double[]>();
            for (var a = 0; a < l.N - 1; a += 2)
            {
                var b = Math.Min(l.N - 1, a + 2);
                var corners = new List<double[]> { l.B[a], l.B[b], l.T[b], l.T[a] };
                var inward = Unit(Add(l.Nm[a], l.Nm[b]));
                inward = [-inward[0], -inward[1], -inward[2]];
                if (PieceWindow(d, corners, inward) is not { Q: { } q, Room: { } r })
                    continue;
                var face = (int)q[6];
                if (!merged.TryGetValue((r.Id, face), out var had))
                {
                    merged[(r.Id, face)] = q;
                    continue;
                }
                // Joined: the span along the wall and the height over both.
                var al = face == 0 ? (d.Stacked ? 1 : 2) : 0;
                double s0 = Math.Min(had[al] - had[3], q[al] - q[3]), s1 = Math.Max(had[al] + had[3], q[al] + q[3]);
                double u0 = d.Stacked ? Math.Max(had[up], q[up]) : Math.Min(had[up], q[up]);
                double u1 = d.Stacked ? Math.Min(had[up] - had[4], q[up] - q[4]) : Math.Max(had[up] + had[4], q[up] + q[4]);
                had[al] = (s0 + s1) / 2;
                had[3] = (s1 - s0) / 2;
                had[up] = u0;
                had[4] = Math.Abs(u1 - u0);
                had[7] = Math.Max(had[7], q[7]);
            }
            o.AddRange(merged.Values);
        }
        return o;
    }

    /// <summary>Checks: a wraparound window that lies on no skin says why.</summary>
    private static void WrapChecks(ShipDesign d, Check c)
    {
        foreach (var w in d.WrapWindows)
            if (WrapOnce(d, w) is { Ok: false } l)
                c.Issues.Add(new("warn", $"A wraparound window on {d.Hull(w.Hull)?.Name ?? "a body"}: {l.Why}", $"hull:{w.Hull}"));
    }

    /// <summary>The scene's entry for a wraparound window: its settings, whether it lies on the skin, its frame and its glass.</summary>
    private static string WrapJson(ShipDesign d, WrapWindow w)
    {
        var l = WrapOnce(d, w);
        var (solid, glass) = SplitPanes(WrapMesh(d, w));
        var t = new StringBuilder();
        var j = w.ToJson(d);
        j["hull"] = w.Hull; // (the scene names bodies by id)
        j["ok"] = l.Ok;
        j["why"] = l.Why;
        var hb = d.Hull(w.Hull);
        j["front"] = hb is null ? 0 : ShipDesign.R(hb.Front);
        j["back"] = hb is null ? 0 : ShipDesign.R(hb.Back);
        var s = j.ToJsonString();
        t.Append(s, 0, s.Length - 1).Append(",\"mesh\":");
        solid.Write(t, Made("ww" + w.Id));
        t.Append(",\"glass\":");
        glass.Write(t, Made("wg" + w.Id));
        return t.Append('}').ToString();
    }
}
