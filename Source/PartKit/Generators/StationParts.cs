using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// Freeport One, the first space station (Karl, 2026-11-17; the layout agreed in the project's floor plan artifact, version 3): a
/// harbour in orbit. A 6 m walkway spine runs north to south with everything hung off it, and each side takes one kind of traffic:
/// the open cargo pier to the west under a gantry crane, off the warehouse's big door, for a hauler such as the Dray; two frigate
/// fingers to the east, each with a repair arm (Karl: repairs take time and you see an arm mend the ship from outside); a trader
/// marina at the north end beside the market hall and customs; the command hub at the bow; the reactor on a boom at the stern,
/// behind its radiators, with engineering at the boom's root. A fuel farm of six spheres hangs under catwalks between the fingers,
/// and the habitat drum lies across the spine with a roof pad for a small ship on top (the station's second level).
///
/// Karl (2026-11-17, "keep it simple"): every building is a sealed shell with doors out and nothing inside; the station's services
/// come from a docking screen, not from walking its rooms. So the doors here are drawn on the walls, not hung.
///
/// Built as the Dray and the Zero Day are, of welded pieces placed at one origin (Connect, PlaceOrder): spine lengths, piers, each
/// building's floor, walls and roof, the drum's thirds, the boom, the reactor and its radiators. Station metres: e east, u up from the
/// deck's top, n north; the game's x is e, y is u and z is -n (the bow, north, is -z). docs/sandbox/constructs/station.md.
/// </summary>
public sealed class StationParts : IPartGenerator
{
    public string Name => "station_parts";

    private const string Out = "Constructs/Parts/Freeport";
    private const string BpOut = "Constructs/Blueprints";
    private const string Group = "Ship";
    private const string Section = "Freeport station";
    public const string StationName = "Freeport One Station";
    private const double H = 0.125; // half a cell

    // The paint: a neutral free port's. Pale weathered panels over a dark frame, deck plate underfoot, a teal livery stripe, hazard
    // yellow at every edge a ship or a person can meet, warm lamps, red beacons on the tall things.
    private const string Hull = "#c3c5be", Hull2 = "#a9ada6", Hull3 = "#8e938f", Frame = "#2a2e31", Deck = "#55595b", Grate = "#3d4143",
        Hazard = "#e0a020", Orange = "#dd6a1e", Teal = "#2e7c84", Glow = "#ffe0a0", Cyan = "#9fe8ff", Red = "#e0402c", Rad = "#3a3f43",
        RadHot = "#7a3a24", Tank = "#d9d5c8", Door = "#4f5a60", Green = "#8dffb0";

    private static int _slabs;
    private const double DeckT = 1.0; // the deck slabs' thickness (top at u 0)

    // ---- the plan (station metres)

    private static readonly (double N0, double N1)[] SpineCuts = [(-90, -62), (-62, -34), (-34, -6), (-6, 12), (32, 52), (52, 76), (76, 100)];
    private const double DrumN0 = 12, DrumN1 = 32, DrumR = 10, DrumAxisU = 4, DrumE = 26;
    private static readonly (double N0, double N1) FingerA = (-3, 1), FingerB = (-70, -66);
    private const double FingerE = 104, FingerCut = 53;
    private static readonly (double N0, double N1) Pier = (-26, -4);
    private const double PierE0 = -104, PierCut = -67, PierE1 = -30;
    private static readonly double[] MarinaN = [64, 80];
    private const double MarinaE = 24;
    private static readonly double[] FuelN = [-20, -46];
    private static readonly double[] FuelE = [22, 40, 58];
    private const double FuelR = 7.2, FuelU = -9.5;

    private static double[] P(double e, double u, double n) => [e - H, u - H, -n - H];

    // ---- box helpers

    private static void Bx(Part p, double e0, double u0, double n0, double e1, double u1, double n1, string? color, bool detail = false, bool hidden = false, string? finish = null, bool glow = false, PyDict? shape = null)
    {
        double ea = Math.Min(e0, e1), eb = Math.Max(e0, e1), ua = Math.Min(u0, u1), ub = Math.Max(u0, u1), na = Math.Min(n0, n1), nb = Math.Max(n0, n1);
        if (hidden && (eb - ea < 0.05 || ub - ua < 0.05 || nb - na < 0.05))
            return;
        var s = shape;
        if (finish != null || glow)
        {
            s = s == null ? new PyDict() : new PyDict(s);
            if (glow)
                s["glow"] = true;
            else
                s["finish"] = finish;
        }
        p.Add(Box(P(ea, ua, nb), P(eb, ub, na), hidden ? null : color, detail: detail, hidden: hidden, shape: s));
    }

    private static void Solid(Part p, double e0, double u0, double n0, double e1, double u1, double n1, string color, string? finish = null) =>
        Bx(p, e0, u0, n0, e1, u1, n1, color, finish: finish);

    private static void Det(Part p, double e0, double u0, double n0, double e1, double u1, double n1, string color, string? finish = null, bool glow = false) =>
        Bx(p, e0, u0, n0, e1, u1, n1, color, detail: true, finish: finish, glow: glow);

    private static void Hid(Part p, double e0, double u0, double n0, double e1, double u1, double n1) =>
        Bx(p, e0, u0, n0, e1, u1, n1, null, hidden: true);

    /// <summary>A shaped detail (cylinder, sphere, tube...) filling the box; front: the axis a cylinder runs along ("+x", "+y", "-z"...).</summary>
    private static void Shp(Part p, string shape, double e0, double u0, double n0, double e1, double u1, double n1, string color, string? front = null, int segments = 12, double[]? taper = null, string? finish = "painted plating", bool glow = false, double? inner = null)
    {
        var s = Kw(("shape", shape));
        if (segments != 12)
            s["segments"] = segments;
        if (inner != null)
            s["inner"] = inner.Value;
        if (taper != null)
            s["taper"] = Py.L(taper[0], taper[1]);
        if (front != null)
            s["front"] = front;
        Bx(p, e0, u0, n0, e1, u1, n1, color, detail: true, finish: glow ? null : finish, glow: glow, shape: s);
    }

    /// <summary>A cylinder along an axis through (e, u, n): "e", "u" or "n", from a to b along it, radius r.</summary>
    private static void Cyl(Part p, char axis, double e, double u, double n, double a, double b, double r, string color, int segments = 12, string? finish = "painted plating", bool glow = false, double[]? taper = null)
    {
        switch (axis)
        {
            case 'e': Shp(p, "cylinder", a, u - r, n - r, b, u + r, n + r, color, "+x", segments, taper, finish, glow); break;
            case 'u': Shp(p, "cylinder", e - r, a, n - r, e + r, b, n + r, color, "+y", segments, taper, finish, glow); break;
            default: Shp(p, "cylinder", e - r, u - r, a, e + r, u + r, b, color, "-z", segments, taper, finish, glow); break;
        }
    }

    private static void Sphere(Part p, double e, double u, double n, double r, string color, int segments = 12, string? finish = "painted plating", bool glow = false) =>
        Shp(p, "sphere", e - r, u - r, n - r, e + r, u + r, n + r, color, null, segments, null, finish, glow);

    /// <summary>A square bar w thick from a to b (station metres), drawn only: a truss diagonal, a mast leg, an arm.</summary>
    private static void Bar(Part p, double[] a, double[] b, double w, string color, string finish = "painted steel")
    {
        double[] d = [b[0] - a[0], b[1] - a[1], b[2] - a[2]];
        var len = Math.Sqrt(d[0] * d[0] + d[1] * d[1] + d[2] * d[2]);
        double[] t = [d[0] / len, d[1] / len, d[2] / len];
        double[] up = Math.Abs(t[1]) > 0.9 ? [1, 0, 0] : [0, 1, 0];
        var s1 = Unit(Cross(t, up));
        var s2 = Unit(Cross(t, s1));
        var pts = new List<double[]>();
        foreach (var q in new[] { a, b })
            foreach (var (i, j) in new[] { (-1, -1), (-1, 1), (1, 1), (1, -1) })
            {
                var e = q[0] + (s1[0] * i + s2[0] * j) * w / 2;
                var u = q[1] + (s1[1] * i + s2[1] * j) * w / 2;
                var n = q[2] + (s1[2] * i + s2[2] * j) * w / 2;
                pts.Add([.. P(e, u, n).Select(v => Py.Round(v, 4))]);
            }
        HullPts(p, pts, color, finish);
    }

    /// <summary>A convex solid round the points (game frame, already shifted), drawn only.</summary>
    private static void HullPts(Part p, List<double[]> pts, string color, string? finish, bool glow = false)
    {
        double[] mn = [.. Enumerable.Range(0, 3).Select(k => pts.Min(q => q[k]))];
        double[] mx = [.. Enumerable.Range(0, 3).Select(k => pts.Max(q => q[k]))];
        var shape = Kw(("shape", "hull"), ("points", pts.Select(q => (object?)q.Select(x => (object?)x).ToList()).ToList()), ("paintable", false));
        if (glow)
            shape["glow"] = true;
        else if (finish != null)
            shape["finish"] = finish;
        p.Add(Box(mn, mx, color, detail: true, shape: shape));
    }

    /// <summary>A convex solid round station-metre points.</summary>
    private static void HullS(Part p, IEnumerable<double[]> pts, string color, string? finish = "painted plating", bool glow = false) =>
        HullPts(p, [.. pts.Select(q => P(q[0], q[1], q[2]).Select(v => Py.Round(v, 4)).ToArray())], color, finish, glow);

    private static double[] Cross(double[] a, double[] b) => [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]];

    private static double[] Unit(double[] a)
    {
        var l = Math.Sqrt(a[0] * a[0] + a[1] * a[1] + a[2] * a[2]);
        return [a[0] / l, a[1] / l, a[2] / l];
    }

    private static void Sock(Part p, string name, double e, double u, double n, string dir, Cls? cls = null) => p.Sock(name, P(e, u, n), dir, cls);

    private static Part New(string name, string desc, string color = Hull) =>
        new Part(name, Small, Section, desc, role: "frame", tool: "welder", material: "steel", color: color, group: Group).Set(("collider", "boxes")); // (its round looks are drawn only: collision is its boxes)

    // ---- furniture of the decks: railings, lamps, edges

    /// <summary>Gaps along a run: (from, to) to leave open.</summary>
    private static IEnumerable<(double A, double B)> Runs(double a0, double a1, (double, double)[] gaps)
    {
        var at = a0;
        foreach (var (g0, g1) in gaps.OrderBy(g => g.Item1))
        {
            if (g1 <= at || g0 >= a1)
                continue;
            if (g0 - at > 1.0)
                yield return (at, g0);
            at = Math.Max(at, g1);
        }
        if (a1 - at > 1.0)
            yield return (at, a1);
    }

    /// <summary>A railing on a deck edge: posts every 2 m, a top rail and a mid rail, along n (alongN) or e, at c (the edge, inset 15 cm).</summary>
    private static void Railing(Part p, bool alongN, double a0, double a1, double c, params (double, double)[] gaps)
    {
        foreach (var (a, b) in Runs(a0, a1, gaps))
        {
            void R(double s0, double s1, double u0, double u1, double w, string col) =>
                (alongN ? (Action)(() => Det(p, c - w, u0, s0, c + w, u1, s1, col, "painted steel")) : () => Det(p, s0, u0, c - w, s1, u1, c + w, col, "painted steel"))();
            var n = Math.Max(1, (int)Math.Round((b - a) / 2.0));
            for (var k = 0; k <= n; k++)
            {
                var s = a + (b - a) * k / n;
                R(s - 0.04, s + 0.04, 0.004, 1.06, 0.04, Frame);
            }
            R(a, b, 1.06, 1.12, 0.05, Hazard);
            R(a + 0.02, b - 0.02, 0.52, 0.56, 0.03, Frame);
        }
    }

    /// <summary>A lamp post: a post up from the deck at (e, n), a head with a warm glow.</summary>
    private static void LampPost(Part p, double e, double n, double h = 3.2)
    {
        Det(p, e - 0.07, 0.004, n - 0.07, e + 0.07, h, n + 0.07, Frame, "painted steel");
        Det(p, e - 0.22, h, n - 0.22, e + 0.22, h + 0.14, n + 0.22, Frame, "painted steel");
        Det(p, e - 0.17, h - 0.06, n - 0.17, e + 0.17, h - 0.005, n + 0.17, Glow, glow: true);
    }

    /// <summary>A deck slab with its plate seams and a hazard edge on the sides a ship meets (edges: "n", "s", "e", "w").</summary>
    private static void DeckSlab(Part p, double e0, double n0, double e1, double n1, string edges = "", double seam = 4)
    {
        Solid(p, e0, -DeckT, n0, e1, 0, n1, Deck, "deck plate");
        // seams across the long way
        var alongN = n1 - n0 > e1 - e0;
        if (alongN)
            for (var s = n0 + seam; s < n1 - 0.5; s += seam)
                Det(p, e0 + 0.3, 0.003, s - 0.03, e1 - 0.3, 0.012, s + 0.03, Grate, "dirty steel");
        else
            for (var s = e0 + seam; s < e1 - 0.5; s += seam)
                Det(p, s - 0.03, 0.003, n0 + 0.3, s + 0.03, 0.012, n1 - 0.3, Grate, "dirty steel");
        foreach (var c in edges)
            switch (c)
            {
                case 'n': Det(p, e0 + 0.02, 0.004, n1 - 0.32, e1 - 0.02, 0.022, n1 - 0.02, Hazard, "hazard paint"); break;
                case 's': Det(p, e0 + 0.02, 0.004, n0 + 0.02, e1 - 0.02, 0.022, n0 + 0.32, Hazard, "hazard paint"); break;
                case 'e': Det(p, e1 - 0.32, 0.004, n0 + 0.02, e1 - 0.02, 0.022, n1 - 0.02, Hazard, "hazard paint"); break;
                case 'w': Det(p, e0 + 0.02, 0.004, n0 + 0.02, e0 + 0.32, 0.022, n1 - 0.02, Hazard, "hazard paint"); break;
            }
        // the slab's edge band, under the deck line
        var o = 0.004 + 0.004 * (_slabs++ % 3); // (neighbouring slabs' bands overlap at their ends: never on one plane)
        Det(p, e0 - o, -0.55, n0 - o, e1 + o, -0.25, n1 + o, Teal, "worn paint");
    }

    /// <summary>A truss under a deck strip along n: bottom chords, posts and diagonals, the two service pipes.</summary>
    private static void TrussN(Part p, double eC, double half, double n0, double n1, double depth, bool pipes = true)
    {
        var bot = -DeckT - depth;
        foreach (var s in new[] { -1, 1 })
        {
            var e = eC + s * (half - 0.3);
            Det(p, e - 0.2, bot, n0 + 0.01, e + 0.2, bot + 0.4, n1 - 0.01, Frame, "painted steel");
            var k = Math.Max(1, (int)Math.Round((n1 - n0) / 4.0));
            for (var i = 0; i <= k; i++)
            {
                var n = n0 + (n1 - n0) * i / k;
                n = Math.Clamp(n, n0 + 0.2, n1 - 0.2);
                Det(p, e - 0.15, bot + 0.4, n - 0.15, e + 0.15, -DeckT - 0.004, n + 0.15, Frame, "painted steel");
                if (i < k)
                {
                    var nb = Math.Clamp(n0 + (n1 - n0) * (i + 1) / k, n0 + 0.2, n1 - 0.2);
                    Bar(p, i % 2 == 0 ? [e, bot + 0.4, n] : [e, -DeckT - 0.05, n], i % 2 == 0 ? [e, -DeckT - 0.05, nb] : [e, bot + 0.4, nb], 0.18, Hull3);
                }
            }
        }
        if (pipes)
        {
            Cyl(p, 'n', eC - 0.7, -DeckT - 0.6, 0, n0 + 0.05, n1 - 0.05, 0.28, Orange, 10, "worn paint");
            Cyl(p, 'n', eC + 0.7, -DeckT - 0.6, 0, n0 + 0.05, n1 - 0.05, 0.22, Teal, 10, "worn paint");
        }
    }

    /// <summary>The same along e.</summary>
    private static void TrussE(Part p, double nC, double half, double e0, double e1, double depth)
    {
        var bot = -DeckT - depth;
        foreach (var s in new[] { -1, 1 })
        {
            var n = nC + s * (half - 0.3);
            Det(p, e0 + 0.01, bot, n - 0.2, e1 - 0.01, bot + 0.4, n + 0.2, Frame, "painted steel");
            var k = Math.Max(1, (int)Math.Round((e1 - e0) / 4.0));
            for (var i = 0; i <= k; i++)
            {
                var e = Math.Clamp(e0 + (e1 - e0) * i / k, e0 + 0.2, e1 - 0.2);
                Det(p, e - 0.15, bot + 0.4, n - 0.15, e + 0.15, -DeckT - 0.004, n + 0.15, Frame, "painted steel");
                if (i < k)
                {
                    var eb = Math.Clamp(e0 + (e1 - e0) * (i + 1) / k, e0 + 0.2, e1 - 0.2);
                    Bar(p, i % 2 == 0 ? [e, bot + 0.4, n] : [e, -DeckT - 0.05, n], i % 2 == 0 ? [eb, -DeckT - 0.05, n] : [eb, bot + 0.4, n], 0.18, Hull3);
                }
            }
        }
    }

    /// <summary>A door drawn on a wall's outer face (sealed for now: Karl, 2026-11-17): the leaf, a hazard frame, a lamp over it.
    /// face: the way the wall faces ("+e", "-e", "+n", "-n"); at: its middle along the wall; w, h its size.</summary>
    private static void WallDoor(Part p, string face, double wallOut, double at, double w, double h, bool big = false)
    {
        var s = face[0] == '+' ? 1 : -1;
        double O(double d) => wallOut + s * d;
        void B(double a0, double u0, double a1, double u1, double d0, double d1, string col, string? fin, bool glow = false)
        {
            if (face[1] == 'e')
                Det(p, O(d0), u0, a0, O(d1), u1, a1, col, fin, glow);
            else
                Det(p, a0, u0, O(d0), a1, u1, O(d1), col, fin, glow);
        }
        B(at - w / 2, 0.004, at + w / 2, h, 0.004, 0.06, Door, "worn paint");
        B(at - w / 2 - 0.25, 0.004, at - w / 2, h + 0.25, 0.004, 0.12, Hazard, "hazard paint");
        B(at + w / 2, 0.004, at + w / 2 + 0.25, h + 0.25, 0.004, 0.12, Hazard, "hazard paint");
        B(at - w / 2 - 0.25, h, at + w / 2 + 0.25, h + 0.25, 0.008, 0.124, Hazard, "hazard paint");
        if (big)
            for (var k = 1; k < 8; k++)
                B(at - w / 2 + 0.1, h * k / 8 - 0.04, at + w / 2 - 0.1, h * k / 8 + 0.04, 0.06, 0.1, Frame, "painted steel");
        else
            B(at - 0.06, h * 0.45, at + 0.3, h * 0.5, 0.06, 0.12, Frame, "painted steel");
        B(at - 0.35, h + 0.45, at + 0.35, h + 0.65, 0.004, 0.25, Glow, null, true);
    }

    // ---- the spine

    private static Part Spine(int i)
    {
        var (n0, n1) = SpineCuts[i];
        var p = New($"Freeport Spine {i + 1}", "A length of Freeport One's spine: the 6 m deck the station hangs off, a truss under it carrying the fuel and coolant mains, railings and lamps.", Deck);
        DeckSlab(p, -3, n0, 3, n1);
        TrussN(p, 0, 3, n0, n1, 5);
        // the railings, open where a pier, a finger, a catwalk or a building meets the spine
        (double, double)[] east = [(FingerA.N0, FingerA.N1), (FingerB.N0, FingerB.N1), (FuelN[0] - 1, FuelN[0] + 1), (FuelN[1] - 1, FuelN[1] + 1), (42, 56), (MarinaN[0] - 1.5, MarinaN[0] + 1.5), (MarinaN[1] - 1.5, MarinaN[1] + 1.5)];
        (double, double)[] west = [(-38, -2), (44, 56), (MarinaN[0] - 1.5, MarinaN[0] + 1.5), (MarinaN[1] - 1.5, MarinaN[1] + 1.5)];
        Railing(p, true, n0 + 0.2, n1 - 0.2, 2.85, east);
        Railing(p, true, n0 + 0.2, n1 - 0.2, -2.85, west);
        // a teal walking line down the middle, lamps every 12 m on alternate sides
        Det(p, -0.12, 0.004, n0 + 0.4, 0.12, 0.012, n1 - 0.4, Teal, "worn paint");
        for (var n = Math.Ceiling(n0 / 12) * 12 + 6; n < n1 - 1; n += 12)
            LampPost(p, (int)Math.Round(n / 12) % 2 == 0 ? 2.4 : -2.4, n);
        return p.Stats(30000 * (n1 - n0) / 28, 60000, 30, 15.0);
    }

    // ---- piers and fingers

    private static Part Deckway(string name, string desc, double e0, double n0, double e1, double n1, string edges, bool railS, bool railN, bool railE, bool railW, bool truss = true)
    {
        var p = New(name, desc, Deck);
        DeckSlab(p, e0, n0, e1, n1, edges);
        var alongN = n1 - n0 > e1 - e0;
        if (truss)
        {
            if (alongN)
                TrussN(p, (e0 + e1) / 2, (e1 - e0) / 2, n0, n1, 3, false);
            else
                TrussE(p, (n0 + n1) / 2, (n1 - n0) / 2, e0, e1, 3);
        }
        if (railS)
            Railing(p, false, e0 + 0.2, e1 - 0.2, n0 + 0.15);
        if (railN)
            Railing(p, false, e0 + 0.2, e1 - 0.2, n1 - 0.15);
        if (railE)
            Railing(p, true, n0 + 0.2, n1 - 0.2, e1 - 0.15);
        if (railW)
            Railing(p, true, n0 + 0.2, n1 - 0.2, e0 + 0.15);
        return p;
    }

    /// <summary>A frigate finger's half: 4 m wide, railed on the side away from the ship, fenders and a hazard edge on the ship's side.</summary>
    private static Part Finger(string tag, (double N0, double N1) f, bool outer, int shipSide)
    {
        var e0 = outer ? FingerCut : 3;
        var e1 = outer ? FingerE : FingerCut;
        var p = Deckway($"Freeport Finger {tag}", "Half of a frigate finger at Freeport One: a 4 m deck a frigate lies alongside, held by a Docking Clamp Large.",
            e0, f.N0, e1, f.N1, shipSide > 0 ? "n" : "s", railS: shipSide > 0, railN: shipSide < 0, railE: outer, railW: false);
        // fenders on the ship's side: black blocks hung off the edge every 6 m
        var edge = shipSide > 0 ? f.N1 : f.N0;
        for (var e = e0 + 3; e < e1 - 1; e += 6)
            Det(p, e - 0.6, -0.9, shipSide > 0 ? edge + 0.004 : edge - 0.45, e + 0.6, -0.1, shipSide > 0 ? edge + 0.45 : edge - 0.004, Frame, "rubber");
        for (var e = e0 + 6; e < e1 - 1; e += 12)
            LampPost(p, e, shipSide > 0 ? f.N0 + 0.5 : f.N1 - 0.5);
        // berth guide lights along the ship's edge: green, a pilot lines up on them
        for (var e = e0 + 2; e < e1 - 1; e += 4)
            Det(p, e - 0.2, 0.004, shipSide > 0 ? edge - 0.5 : edge + 0.34, e + 0.2, 0.1, shipSide > 0 ? edge - 0.34 : edge + 0.5, Green, glow: true);
        if (!outer)
        {
            // a floodlight tower at the finger's root, its lamps turned over the berth
            var tn = shipSide > 0 ? f.N0 + 0.7 : f.N1 - 0.7;
            Det(p, 7.6, 0.004, tn - 0.4, 8.4, 16, tn + 0.4, Frame, "painted steel");
            Det(p, 7.7, 0.004, tn - 0.5, 8.3, 0.9, tn + 0.5, Hazard, "hazard paint");
            Det(p, 5.5, 16, tn - 0.3, 10.5, 16.4, tn + 0.3, Frame, "painted steel");
            foreach (var e in new[] { 6.0, 8.0, 10.0 })
            {
                Det(p, e - 0.45, 15.3, tn - 0.35, e + 0.45, 16, tn + 0.35, Hull3, "painted steel");
                Det(p, e - 0.38, 15.37, shipSide > 0 ? tn + 0.35 : tn - 0.39, e + 0.38, 15.93, shipSide > 0 ? tn + 0.39 : tn - 0.35, Glow, glow: true);
            }
            Sphere(p, 8, 16.9, tn, 0.4, Red, 10, glow: true);
        }
        if (outer)
        {
            // the clamp's place (socket below) and a boarding gangway stub out over the edge toward the ship's side
            Sock(p, "clamp", 60, 0, (f.N0 + f.N1) / 2, "+y", Large);
            var gn = shipSide > 0 ? f.N1 : f.N0;
            Det(p, 69, 0.004, shipSide > 0 ? gn - 1.2 : gn - 1.6, 71.5, 0.25, shipSide > 0 ? gn + 1.6 : gn + 1.2, Hull2, "plated steel");
            Shp(p, "tube", 69.2, 0.25, shipSide > 0 ? gn - 1.0 : gn - 1.6, 71.3, 2.6, shipSide > 0 ? gn + 1.6 : gn + 1.0, Hull, "-z", 10, null, "painted plating", inner: 0.86);
            Det(p, 69.9, 2.62, shipSide > 0 ? gn + 0.2 : gn - 0.6, 70.6, 2.8, shipSide > 0 ? gn + 0.6 : gn - 0.2, Glow, glow: true);
            // an end light tower
            Det(p, e1 - 1.4, 0.004, f.N0 + 0.6, e1 - 0.6, 5, f.N0 + 1.4, Frame, "painted steel");
            Sphere(p, e1 - 1, 5.4, f.N0 + 1, 0.45, Red, 10, glow: true);
        }
        else
            Sock(p, "spare", 10, 0, (f.N0 + f.N1) / 2, "+y");
        return p.Stats(30000, 50000, 25, 15.0);
    }

    private static Part MarinaFinger(int i, int side)
    {
        var n = MarinaN[i];
        var (e0, e1) = side > 0 ? (3.0, MarinaE) : (-MarinaE, -3.0);
        var p = Deckway($"Freeport Marina {i + 1}{(side > 0 ? "E" : "W")}", "A trader's berth at Freeport One's north marina: a 3 m finger with a Docking Clamp Medium at its end, small craft either side.",
            e0, n - 1.5, e1, n + 1.5, "ns", false, false, side > 0, side < 0, truss: false);
        var end = side > 0 ? e1 : e0;
        Sock(p, "clamp", end - side * 1.5, 0, n, "+y", Medium);
        // bollards and a berth number post
        foreach (var e in new[] { (e0 + e1) / 2 - 4, (e0 + e1) / 2 + 4 })
            foreach (var s in new[] { -1, 1 })
                Cyl(p, 'u', e, 0, n + s * 1.05, 0.004, 0.55, 0.2, Frame, 10, "painted steel");
        LampPost(p, (e0 + e1) / 2, n);
        Det(p, end - side * 0.3 - 0.06, 0.004, n - 0.06, end - side * 0.3 + 0.06, 2.2, n + 0.06, Frame, "painted steel");
        Det(p, end - side * 0.3 - 0.05, 1.6, n - 0.5, end - side * 0.3 + 0.05, 2.2, n + 0.5, Teal, "gloss paint");
        // under the finger: a box girder
        Det(p, Math.Min(e0, e1) + 0.3, -DeckT - 1.2, n - 0.8, Math.Max(e0, e1) - 0.3, -DeckT - 0.004, n + 0.8, Frame, "painted steel");
        return p.Stats(8000, 20000, 20, 10.0);
    }

    private static Part CargoPier(bool west)
    {
        var (e0, e1) = west ? (PierE0, PierCut) : (PierCut, PierE1);
        var p = Deckway($"Freeport Cargo Pier {(west ? "W" : "E")}", "Half of Freeport One's open cargo pier: a 22 m deck a hauler sets down on, under the gantry crane, off the warehouse's big door.",
            e0, Pier.N0, e1, Pier.N1, "", true, true, false, west);
        if (!west)
        {
            // the Cargo Dock's pad under the hauler's well, and the clamp in it (a hauler stands over it on its legs)
            double ce = -56, cn = -15;
            foreach (var (a0, b0, a1, b1) in new[] { (ce - 4, cn - 4, ce + 4, cn - 3.7), (ce - 4, cn + 3.7, ce + 4, cn + 4), (ce - 4, cn - 3.7, ce - 3.7, cn + 3.7), (ce + 3.7, cn - 3.7, ce + 4, cn + 3.7) })
                Det(p, a0, 0.004, b0, a1, 0.022, b1, Hazard, "hazard paint");
            Det(p, ce - 3.6, 0.004, cn - 3.6, ce + 3.6, 0.01, cn + 3.6, Grate, "grating");
            Sock(p, "clamp", ce, 0, cn, "+y", Large);
            // container lay-down slots by the warehouse door: painted outlines
            for (var k = 0; k < 3; k++)
            {
                var n = -21 + k * 3.2;
                Det(p, -42, 0.004, n - 1.3, -35.8, 0.012, n - 1.18, Orange, "worn paint");
                Det(p, -42, 0.004, n + 1.18, -35.8, 0.012, n + 1.3, Orange, "worn paint");
            }
        }
        // the dolly's trench rails from the pad to the lay-down
        if (!west)
            foreach (var s in new[] { -0.75, 0.75 })
                Det(p, -60, 0.004, -15 + s - 0.06, -35.8, 0.03, -15 + s + 0.06, Hull3, "steel");
        else
            Det(p, PierCut - 1, 0.004, -15.75 - 0.06, PierCut - 0.02, 0.03, -15.75 + 0.06, Hull3, "steel");
        return p.Stats(45000, 70000, 30, 15.0);
    }

    /// <summary>The gantry over the cargo pier: legs on both edges every 11 m, girders along them, a crane bridge across with its trolley and cab.</summary>
    private static Part Gantry()
    {
        var p = New("Freeport Gantry", "The gantry crane over Freeport One's cargo pier: legs along both edges, a girder on each, and a bridge across with a trolley and the operator's cab, to lift containers off a hauler.", Hazard);
        const double top = 14;
        foreach (var n in new[] { Pier.N0 + 0.85, Pier.N1 - 0.85 })
        {
            for (var e = PierE0 + 4; e <= PierE1 - 4; e += 11)
            {
                Solid(p, e - 0.5, 0, n - 0.5, e + 0.5, top, n + 0.5, Frame, "painted steel");
                Det(p, e - 0.7, 0.007, n - 0.7, e + 0.7, 0.5, n + 0.7, Hazard, "hazard paint");
                if (e + 11 <= PierE1 - 4)
                    Bar(p, [e + 0.5, 1.2, n], [e + 10.5, top - 1, n], 0.25, Frame);
            }
            Solid(p, PierE0 + 3.4, top, n - 0.7, PierE1 - 3.4, top + 1.6, n + 0.7, Hazard, "painted steel");
            Det(p, PierE0 + 3.4, top + 1.6, n - 0.2, PierE1 - 3.4, top + 1.75, n + 0.2, Frame, "steel");
        }
        // the bridge across, the trolley on it, the hook block hanging over the pad, and the cab under the bridge's west end
        const double be = -56;
        Det(p, be - 0.9, top + 1.75, Pier.N0 + 0.3, be + 0.9, top + 3.2, Pier.N1 - 0.3, Hazard, "hazard paint");
        Det(p, be - 0.95, top + 2.2, Pier.N0 + 0.25, be + 0.95, top + 2.6, Pier.N1 - 0.25, Frame, "painted steel");
        Det(p, be - 1.5, top + 0.6, -16.4, be + 1.5, top + 1.75, -13.6, Frame, "dirty steel");
        foreach (var s in new[] { -0.5, 0.5 })
            Det(p, be + s - 0.025, 9.2, -15 - 0.025, be + s + 0.025, top + 0.6, -15 + 0.025, Hull3, "steel");
        Det(p, be - 1.2, 8.4, -16, be + 1.2, 9.2, -14, Orange, "worn paint");
        Det(p, be - 1.4, top - 1.4, Pier.N0 + 1.6, be + 1.4, top + 1.75, Pier.N0 + 4.2, Hull, "painted plating");
        Det(p, be - 1.2, top - 0.8, Pier.N0 + 4.2, be + 1.2, top + 0.4, Pier.N0 + 4.25, Cyan, glow: true);
        Sphere(p, PierE0 + 4, top + 2.2, Pier.N0 + 0.8, 0.4, Red, 10, glow: true);
        Sphere(p, PierE0 + 4, top + 2.2, Pier.N1 - 0.8, 0.4, Red, 10, glow: true);
        return p.Stats(60000, 80000, 30, 20.0);
    }

    /// <summary>A repair arm on a frigate finger (Karl, 2026-11-17: repairs take time, and an arm mends the ship from outside): a
    /// turret base, a column, a long boom out over the berth and a forearm down to a welding head. Drawn at rest over the ship's beam.</summary>
    private static Part RepairArm(int i, (double N0, double N1) f, int shipSide)
    {
        var p = New($"Freeport Repair Arm {i + 1}", "A repair arm on a frigate finger at Freeport One: it reaches over a docked ship and mends it, piece by piece, while the crew wait.", Orange);
        const double e = 86;
        var nb = (f.N0 + f.N1) / 2;
        Solid(p, e - 1.5, 0, nb - 1.5, e + 1.5, 1.2, nb + 1.5, Frame, "painted steel");
        Cyl(p, 'u', e, 0, nb, 1.204, 2.2, 1.35, Hazard, 16, "hazard paint");
        Solid(p, e - 0.6, 1.2, nb - 0.6, e + 0.6, 8.5, nb + 0.6, Orange, "worn paint");
        Det(p, e - 0.65, 2.4, nb - 0.65, e + 0.65, 2.6, nb + 0.65, Frame, "painted steel");
        Sphere(p, e, 9.1, nb, 1.0, Frame, 12, "painted steel");
        double[] shoulder = [e, 9.1, nb], elbow = [e - 2, 15.5, nb + shipSide * 10.5], wrist = [e - 3, 9.5, nb + shipSide * 15];
        Bar(p, shoulder, elbow, 0.9, Orange, "worn paint");
        Bar(p, [shoulder[0] + 0.5, shoulder[1] + 0.6, shoulder[2]], [elbow[0] + 0.5, elbow[1] + 0.6, elbow[2]], 0.2, Frame);
        Sphere(p, elbow[0], elbow[1], elbow[2], 0.75, Frame, 12, "painted steel");
        Bar(p, elbow, wrist, 0.65, Orange, "worn paint");
        Sphere(p, wrist[0], wrist[1], wrist[2], 0.5, Frame, 10, "painted steel");
        Cyl(p, 'u', wrist[0], 0, wrist[2], wrist[1] - 1.6, wrist[1] - 0.45, 0.32, Hull3, 10, "steel", taper: [0.45, 0.45]);
        Sphere(p, wrist[0], wrist[1] - 1.75, wrist[2], 0.2, Cyan, 8, glow: true);
        Det(p, e - 0.3, 6.8, nb - 0.65, e + 0.3, 7.6, nb - 0.604, Glow, glow: true);
        return p.Stats(6000, 15000, 20, 15.0);
    }

    // ---- the fuel farm

    private static Part FuelFarm(int i)
    {
        var n = FuelN[i];
        var p = New($"Freeport Fuel Farm {(i == 0 ? "N" : "S")}", "Three of Freeport One's fuel spheres hung under a catwalk between the frigate fingers; mains run under the decks to every clamp, which refuels docked ships.", Tank);
        DeckSlab(p, 3, n - 1, 66, n + 1);
        Railing(p, false, 3.2, 65.8, n - 0.85);
        Railing(p, false, 3.2, 65.8, n + 0.85);
        Railing(p, true, n - 0.8, n + 0.8, 65.85);
        foreach (var e in FuelE)
        {
            Sphere(p, e, FuelU, n, FuelR, Tank, 20, "painted plating");
            Cyl(p, 'u', e, 0, n, FuelU - 0.35, FuelU + 0.35, FuelR + 0.04, Orange, 20, "worn paint");
            Cyl(p, 'u', e, 0, n, FuelU + FuelR - 0.9, -DeckT - 0.004, 1.3, Frame, 12, "painted steel");
            Hid(p, e - 5, FuelU - 5, n - 5, e + 5, -DeckT - 0.6, n + 5);
            Hid(p, e - 1.2, -DeckT - 0.6, n - 0.9, e + 1.2, -DeckT, n + 0.9);
            // a valve wheel and a gauge on the catwalk over each sphere
            Cyl(p, 'u', e + 1.6, 0, n, 0.004, 0.9, 0.12, Frame, 8, "painted steel");
            Cyl(p, 'u', e + 1.6, 0, n, 0.9, 0.96, 0.32, Red, 12, "gloss paint");
            Det(p, e - 0.4, 0.004, n - 0.12, e + 0.4, 1.2, n + 0.12, Hull3, "painted steel");
            Det(p, e - 0.25, 0.85, n + 0.12, e + 0.25, 1.1, n + 0.16, Cyan, glow: true);
        }
        Cyl(p, 'e', 0, -DeckT - 0.5, n, 3.05, 64, 0.3, Orange, 10, "worn paint");
        return p.Stats(90000, 40000, 15, 15.0);
    }

    // ---- sealed buildings: floor, four walls, roof, each its own piece

    private sealed record Bldg(string Name, string Short, double E0, double N0, double E1, double N1, double Height, string Color, string Accent);

    /// <summary>A sealed shell's pieces: the floor slab (fe0..fe1 may reach the spine), walls 0.3 m thick on it, a roof slab on them.
    /// dress(wallPieces, roof) adds each building's own doors, ribs and roof furniture.</summary>
    private static List<Part> Shell(Bldg b, double fe0, double fe1, double fn0, double fn1, Action<Dictionary<char, Part>, Part, Part> dress)
    {
        const double t = 0.3;
        var floor = New($"{b.Name} Floor", $"The floor of {b.Short}, a sealed building at Freeport One.", Deck);
        DeckSlab(floor, fe0, fn0, fe1, fn1);
        floor.Stats(20000, 50000, 25, 15.0);
        var walls = new Dictionary<char, Part>();
        foreach (var side in "nsew")
        {
            var w = New($"{b.Name} Wall {char.ToUpperInvariant(side)}", $"A wall of {b.Short}, a sealed building at Freeport One: plated, ribbed, with its doors out.", b.Color);
            double e0, e1, n0, n1;
            switch (side)
            {
                case 'n': (e0, e1, n0, n1) = (b.E0, b.E1, b.N1 - t, b.N1); break;
                case 's': (e0, e1, n0, n1) = (b.E0, b.E1, b.N0, b.N0 + t); break;
                case 'e': (e0, e1, n0, n1) = (b.E1 - t, b.E1, b.N0 + t, b.N1 - t); break;
                default: (e0, e1, n0, n1) = (b.E0, b.E0 + t, b.N0 + t, b.N1 - t); break;
            }
            Solid(w, e0, 0, n0, e1, b.Height, n1, b.Color, "painted plating");
            // ribs every 3 m on the outer face, a dark plinth, a teal band and a coping
            var alongE = side is 'n' or 's';
            var o = side switch { 'n' => n1, 's' => n0, 'e' => e1, _ => e0 };
            var sg = side is 'n' or 'e' ? 1 : -1;
            var (a0, a1) = alongE ? (e0, e1) : (n0 - t, n1 + t);
            void Face(double s0, double u0, double s1, double u1, double d0, double d1, string col, string fin)
            {
                if (alongE)
                    Det(w, s0, u0, o + sg * d0, s1, u1, o + sg * d1, col, fin);
                else
                    Det(w, o + sg * d0, u0, s0, o + sg * d1, u1, s1, col, fin);
            }
            Face(a0 + 0.02, 0.004, a1 - 0.02, 0.9, 0.004, 0.08, Frame, "dirty steel");
            Face(a0 + 0.02, b.Height * 0.62, a1 - 0.02, b.Height * 0.62 + 0.5, 0.004, 0.05, b.Accent, "worn paint");
            Face(a0 + 0.02, b.Height - 0.35, a1 - 0.02, b.Height - 0.004, 0.004, 0.12, Hull3, "painted steel");
            var k = Math.Max(1, (int)Math.Round((a1 - a0) / 3));
            for (var i = 1; i < k; i++)
            {
                var s = a0 + (a1 - a0) * i / k;
                Face(s - 0.12, 0.9, s + 0.12, b.Height - 0.35, 0.004, 0.18, Hull2, "painted plating");
            }
            walls[side] = w;
        }
        var roof = New($"{b.Name} Roof", $"The roof of {b.Short}, a sealed building at Freeport One.", Hull2);
        Solid(roof, b.E0, b.Height, b.N0, b.E1, b.Height + 0.4, b.N1, Hull2, "painted plating");
        Det(roof, b.E0 - 0.1, b.Height + 0.05, b.N0 - 0.1, b.E1 + 0.1, b.Height + 0.35, b.N1 + 0.1, Frame, "painted steel");
        dress(walls, roof, floor);
        foreach (var w in walls.Values)
            w.Stats(8000, 30000, 25, 15.0);
        roof.Stats(9000, 25000, 20, 15.0);
        return [floor, .. walls.Values, roof];
    }

    /// <summary>Roof furniture: an air handler box with a fan.</summary>
    private static void Handler(Part r, double e, double n, double top)
    {
        Det(r, e - 1.4, top + 0.004, n - 1, e + 1.4, top + 1.3, n + 1, Hull3, "painted plating");
        Cyl(r, 'u', e - 0.5, 0, n, top + 1.3, top + 1.45, 0.6, Frame, 12, "painted steel");
        Det(r, e + 0.4, top + 1.3, n - 0.6, e + 1.1, top + 1.5, n + 0.6, Frame, "grating");
    }

    private static List<Part> Warehouse()
    {
        var b = new Bldg("Freeport Warehouse", "the warehouse", -30, -38, -3, -2, 12, Hull, Teal);
        return Shell(b, -30, -3, -38, -2, (w, r, f) =>
        {
            WallDoor(w['w'], "-e", -30, -15, 14, 10, big: true);
            WallDoor(w['e'], "+e", -3, -20, 1.6, 2.3);
            WallDoor(w['n'], "+n", -2, -16, 1.6, 2.3);
            // a low gable down its length, with a ridge vent and roof lights
            HullS(r, [[-29.6, 12.4, -37.6], [-3.4, 12.4, -37.6], [-29.6, 12.4, -2.4], [-3.4, 12.4, -2.4], [-17.5, 14.4, -37.6], [-15.5, 14.4, -37.6], [-17.5, 14.4, -2.4], [-15.5, 14.4, -2.4]], Hull2, "painted plating");
            Det(r, -17.3, 14.4, -36, -15.7, 15.0, -4, Frame, "grating");
            for (var n = -34; n <= -6; n += 7)
            {
                Det(r, -25, 13.0, n - 1.2, -21, 13.4, n + 1.2, Cyan, glow: true);
                Det(r, -12, 13.0, n - 1.2, -8, 13.4, n + 1.2, Cyan, glow: true);
            }
            // big letters' worth of sign: a teal board over the cargo door
            Det(w['w'], -30.35, 10.6, -22, -30.05, 11.7, -8, Teal, "gloss paint");
            Det(w['w'], -30.4, 10.9, -21, -30.34, 11.4, -9, Glow, glow: true);
        });
    }

    private static List<Part> Market()
    {
        var b = new Bldg("Freeport Market", "the market hall", 6, 42, 30, 56, 8, Hull, Orange);
        return Shell(b, 3, 30, 42, 56, (w, r, f) =>
        {
            WallDoor(w['w'], "-e", 6, 49, 4, 3.2, big: true);
            WallDoor(w['n'], "+n", 56, 14, 1.6, 2.3);
            // a striped awning over the market's front, on posts on the apron
            for (var k = 0; k < 6; k++)
            {
                var n0 = 43.5 + k * 1.8333;
                HullS(r, [[5.9, 7.6, n0], [5.9, 7.6, n0 + 1.83], [3.3, 6.2, n0], [3.3, 6.2, n0 + 1.83], [5.9, 7.7, n0], [5.9, 7.7, n0 + 1.83], [3.3, 6.3, n0], [3.3, 6.3, n0 + 1.83]], k % 2 == 0 ? Teal : Tank, "fabric");
            }
            foreach (var n in new[] { 43.7, 54.3 })
                Det(f, 3.4, 0.004, n - 0.08, 3.56, 6.2, n + 0.08, Frame, "painted steel");
            // a sawtooth roof: three teeth across it, each with a lit north light
            for (var k = 0; k < 3; k++)
            {
                double e0 = 6.4 + k * 7.8, e1 = e0 + 7.6;
                HullS(r, [[e0, 8.4, 42.4], [e1, 8.4, 42.4], [e0, 8.4, 55.6], [e1, 8.4, 55.6], [e1, 11, 42.4], [e1, 11, 55.6], [e1 - 0.01, 8.4, 55.6], [e1 - 0.01, 8.4, 42.4]], Hull2, "painted plating");
                Det(r, e1 - 0.1, 8.6, 43, e1 + 0.004, 10.6, 55, Glow, glow: true);
            }
            Det(w['w'], 5.65, 4.2, 45, 5.95, 5.4, 53, Orange, "gloss paint");
            Det(w['w'], 5.6, 4.5, 46, 5.66, 5.1, 52, Glow, glow: true);
        });
    }

    private static List<Part> Customs()
    {
        var b = new Bldg("Freeport Customs", "customs and berthing", -22, 44, -6, 56, 6, Hull, Teal);
        return Shell(b, -22, -3, 44, 56, (w, r, f) =>
        {
            WallDoor(w['e'], "+e", -6, 50, 2.4, 2.6);
            WallDoor(w['n'], "+n", 56, -14, 1.6, 2.3);
            Handler(r, -16, 49, 6.4);
            // a berth board on a frame over the apron, facing the spine
            Det(f, -5.4, 0.004, 45, -5.2, 5.5, 45.2, Frame, "painted steel");
            Det(f, -5.4, 0.004, 54.8, -5.2, 5.5, 55, Frame, "painted steel");
            Det(f, -5.5, 3.6, 45.2, -5.1, 5.4, 54.8, Frame, "painted steel");
            Det(f, -5.1, 3.8, 45.6, -5.04, 5.2, 54.4, Cyan, glow: true);
            Det(r, -9, 6.404, 54, -8.6, 10, 54.4, Frame, "painted steel");
            Sphere(r, -8.8, 10.3, 54.2, 0.35, Red, 8, glow: true);
        });
    }

    private static List<Part> Engineering()
    {
        var b = new Bldg("Freeport Engineering", "engineering", -12, -104, 12, -90, 7, Hull2, Orange);
        return Shell(b, -12, 12, -104, -90, (w, r, f) =>
        {
            WallDoor(w['n'], "+n", -90, 0, 2.4, 2.6);
            WallDoor(w['s'], "-n", -104, 0, 1.6, 2.3);
            WallDoor(w['e'], "+e", 12, -97, 1.6, 2.3);
            // two stacks with hot mouths, a pipe rack to the boom, two gun domes on the back corners
            foreach (var e in new[] { -6.0, 6.0 })
            {
                Cyl(r, 'u', e, 0, -96, 7.404, 13, 1.2, Hull3, 14, "dirty steel");
                Cyl(r, 'u', e, 0, -96, 13, 13.6, 1.35, Frame, 14, "soot");
                Cyl(r, 'u', e, 0, -96, 13.2, 13.62, 0.95, RadHot, 14, glow: true);
            }
            Handler(r, 0, -93, 7.4);
            foreach (var e in new[] { -10.5, 10.5 })
                GunDome(r, e, 7.4, -102.5, -1);
        });
    }

    /// <summary>A point-defence dome on a roof (guns fitted later): a drum, a dome, twin barrels toward dir (n +1 or -1).</summary>
    private static void GunDome(Part r, double e, double top, double n, int dir)
    {
        Cyl(r, 'u', e, 0, n, top + 0.004, top + 0.9, 1.3, Frame, 14, "painted steel");
        Shp(r, "sphere", e - 1.2, top + 0.4, n - 1.2, e + 1.2, top + 2.4, n + 1.2, Hull3, null, 14, null, "painted plating");
        foreach (var s in new[] { -0.25, 0.25 })
            Cyl(r, 'n', e + s, top + 1.6, 0, dir > 0 ? n + 0.8 : n - 3.6, dir > 0 ? n + 3.6 : n - 0.8, 0.1, Frame, 8, "steel");
    }

    // ---- the command hub at the bow

    private static List<Part> Hub()
    {
        const double cn = 112, r = 13;
        var b = New("Freeport Hub Base", "The lower storey of Freeport One's command hub: a hexagonal block at the bow, sealed, with its door onto the spine.", Hull);
        Solid(b, -11, -DeckT, 100, 11, 0, 124, Deck, "deck plate");
        Hid(b, -11, 0, 101, 11, 6, 123);
        Shp(b, "cylinder", -r, 0.006, cn - r, r, 6, cn + r, Hull, "+y", 6, null, "painted plating");
        Shp(b, "cylinder", -r - 0.25, 0.006, cn - r - 0.25, r + 0.25, 0.9, cn + r + 0.25, Frame, "+y", 6, null, "dirty steel");
        Shp(b, "cylinder", -r - 0.06, 3.3, cn - r - 0.06, r + 0.06, 3.7, cn + r + 0.06, Teal, "+y", 6, null, "worn paint");
        Shp(b, "cylinder", -r + 0.4, -DeckT - 3, cn - r + 0.4, r - 0.4, -DeckT - 0.004, cn + r - 0.4, Hull3, "+y", 6, [0.7, 0.7], "painted plating");
        WallDoor(b, "-n", 100.75, 0, 2.4, 2.6);
        b.Stats(40000, 80000, 35, 20.0);

        var o = New("Freeport Hub Ops", "The upper storey of Freeport One's command hub: traffic control, armoured with screens, not windows; a lit band of sensor slits all round.", Hull2);
        Hid(o, -8.5, 6, 104, 8.5, 11.5, 120);
        Shp(o, "cylinder", -r + 0.2, 6.004, cn - r + 0.2, r - 0.2, 6.5, cn + r - 0.2, Hull3, "+y", 6, null, "painted plating");
        Shp(o, "cylinder", -10.5, 6.5, cn - 10.5, 10.5, 11, cn + 10.5, Hull2, "+y", 6, [0.86, 0.86], "painted plating");
        Shp(o, "cylinder", -10.2, 8.2, cn - 10.2, 10.2, 8.7, cn + 10.2, Cyan, "+y", 6, [0.97, 0.97], glow: true);
        Shp(o, "cylinder", -9.2, 11.004, cn - 9.2, 9.2, 11.5, cn + 9.2, Frame, "+y", 6, null, "painted steel");
        foreach (var (e, n) in new[] { (-6.0, 106.0), (6.0, 106.0), (-6.0, 118.0), (6.0, 118.0) })
            GunDome(o, e, 11.5, n, n > cn ? 1 : -1);
        o.Stats(20000, 60000, 45, 20.0);

        var m = New("Freeport Hub Mast", "The mast on Freeport One's command hub: a lattice tower with the traffic radar dish, the long-range array and the red beacons.", Frame);
        Solid(m, -1.2, 11.5, cn - 1.2, 1.2, 12.5, cn + 1.2, Frame, "painted steel");
        double[][] legs = [[-1, 12.5, cn - 1], [1, 12.5, cn - 1], [1, 12.5, cn + 1], [-1, 12.5, cn + 1]];
        foreach (var l in legs)
            Bar(m, l, [l[0] * 0.25, 34, cn + (l[2] - cn) * 0.25], 0.2, Frame);
        for (var u = 15.0; u < 33; u += 3.5)
        {
            var k = 1 - (u - 12.5) / 21.5 * 0.75;
            Det(m, -k - 0.08, u, cn - k - 0.08, k + 0.08, u + 0.14, cn + k + 0.08, Hazard, "hazard paint");
        }
        // the radar dish facing north, the array of rods, beacons
        Cyl(m, 'n', 0, 26, 0, cn - 3.8, cn - 1.4, 3.4, Hull, 20, "painted plating", taper: [1.0, 0.25]);
        Cyl(m, 'n', 0, 26, 0, cn - 4.4, cn - 3.8, 0.25, Frame, 8, "steel");
        Det(m, -3.5, 30.5, cn - 0.12, 3.5, 30.75, cn + 0.12, Frame, "steel");
        foreach (var e in new[] { -3.0, -1.5, 1.5, 3.0 })
            Det(m, e - 0.05, 30.75, cn - 0.05, e + 0.05, 32.5, cn + 0.05, Hull3, "steel");
        Sphere(m, 0, 34.5, cn, 0.5, Red, 10, glow: true);
        Sphere(m, 0, 22, cn, 0.35, Red, 8, glow: true);
        m.Stats(5000, 15000, 15, 15.0);
        return [b, o, m];
    }

    // ---- the habitat drum across the spine, and the roof pad on it

    private static Part Drum(int part)
    {
        var (e0, e1) = part switch { 0 => (-DrumE, -3.0), 1 => (-3.0, 3.0), _ => (3.0, DrumE) };
        var name = part switch { 0 => "Freeport Habitat W", 1 => "Freeport Habitat Mid", _ => "Freeport Habitat E" };
        var p = New(name, "A third of Freeport One's habitat drum, a 20 m cylinder lying across the spine: quarters, mess and medbay (sealed for now), lit window strips round it.", Hull);
        double u0 = DrumAxisU - DrumR, u1 = DrumAxisU + DrumR, n0 = DrumN0, n1 = DrumN1;
        Shp(p, "cylinder", e0 + (part == 0 ? 0 : 0.004), u0, n0, e1 - (part == 2 ? 0 : 0.004), u1, n1, Hull, "+x", 24, null, "painted plating");
        Hid(p, e0, u0 + 2, n0 + 2, e1, u1 - 2, n1 - 2);
        if (part == 1)
        {
            Solid(p, -3, -DeckT, n0, 3, 0, n1, Deck, "deck plate");
            WallDoor(p, "-n", n0 + 0.6, 0, 2.4, 2.6);
            WallDoor(p, "+n", n1 - 0.6, 0, 2.4, 2.6);
            Shp(p, "cylinder", -2.9, u0 - 0.15, n0 - 0.15, 2.9, u1 + 0.15, n1 + 0.15, Teal, "+x", 24, null, "worn paint");
        }
        else
        {
            var end = part == 0 ? e0 : e1;
            var s = part == 0 ? -1 : 1;
            // ring frames, the end cap with its hub, and window strips along both sides and the top quarter
            for (var e = e0 + 4; e < e1 - 1; e += 6)
                Shp(p, "cylinder", e - 0.3, u0 - 0.2, n0 - 0.2, e + 0.3, u1 + 0.2, n1 + 0.2, Frame, "+x", 24, null, "painted steel");
            Shp(p, "cylinder", end - (s < 0 ? 1.2 : -0.004), u0 + 0.6, n0 + 0.6, end + (s > 0 ? 1.2 : -0.004), u1 - 0.6, n1 - 0.6, Hull2, "+x", 24, [0.8, 0.8], "painted plating");
            Cyl(p, 'e', 0, DrumAxisU, (n0 + n1) / 2, s < 0 ? end - 2.6 : end + 1.2, s < 0 ? end - 1.2 : end + 2.6, 2.4, Frame, 12, "painted steel");
            Sphere(p, s < 0 ? end - 2.6 : end + 2.6, DrumAxisU, (n0 + n1) / 2, 0.45, Red, 8, glow: true);
            var a = Math.Min(e0, e1) + 1.5;
            var b = Math.Max(e0, e1) - 1.5;
            foreach (var (u, n, dn) in new[] { (DrumAxisU + 1.2, n0, -1), (DrumAxisU + 1.2, n1, 1), (DrumAxisU - 1.6, n0, -1), (DrumAxisU - 1.6, n1, 1) })
                for (var e = a; e < b - 0.5; e += 6)
                    Det(p, e + 0.6, u - 0.25, n + dn * 0.02, e + 4.4, u + 0.25, n + dn * 0.12, Glow, glow: true);
        }
        // the pad's pylons stand on the drum's top
        return p.Stats(part == 1 ? 12000 : 40000, 60000, 30, 20.0);
    }

    private static Part RoofPad()
    {
        var p = New("Freeport Roof Pad", "The roof pad on Freeport One's habitat drum, the station's second level: a landing deck on four pylons for one small ship, a Docking Clamp Large in its middle.", Deck);
        const double top = 16.0;
        double cn = (DrumN0 + DrumN1) / 2;
        foreach (var e in new[] { -12.0, 12.0 })
            foreach (var n in new[] { cn - 6.0, cn + 6.0 })
            {
                Solid(p, e - 0.6, DrumAxisU + DrumR - 2, n - 0.6, e + 0.6, top - 0.6, n + 0.6, Frame, "painted steel");
            }
        Solid(p, -16, top - 0.6, cn - 9, 16, top, cn + 9, Deck, "deck plate");
        Det(p, -16.004, top - 0.6, cn - 9.004, 16.004, top - 0.3, cn + 9.004, Hazard, "hazard paint");
        Shp(p, "tube", -6, top + 0.004, cn - 6, 6, top + 0.02, cn + 6, Hazard, "+y", 32, null, "hazard paint", inner: 0.92);
        foreach (var (e, n) in new[] { (-15.4, cn - 8.4), (15.4, cn - 8.4), (-15.4, cn + 8.4), (15.4, cn + 8.4) })
            Sphere(p, e, top + 0.25, n, 0.25, Glow, 8, glow: true);
        Sock(p, "clamp", 0, top, cn, "+y", Large);
        // a ladder tower from the spine's west side up to the pad
        Det(p, -4.2, 0.004, DrumN0 - 2.2, -3.4, top - 0.6, DrumN0 - 1.4, Frame, "painted steel");
        for (var u = 0.4; u < top - 0.8; u += 0.4)
            Det(p, -4.25, u, DrumN0 - 1.4, -3.35, u + 0.04, DrumN0 - 1.35, Hull3, "steel");
        return p.Stats(12000, 30000, 20, 15.0);
    }

    // ---- the reactor boom, the reactor and its radiators

    private static Part Boom()
    {
        var p = New("Freeport Reactor Boom", "The 46 m boom from Freeport One's engineering to its reactor: a walkway on a deep truss, the coolant mains under it.", Frame);
        Solid(p, -1.5, -DeckT, -150, 1.5, 0, -104, Grate, "grating");
        Railing(p, true, -149.8, -104.2, 1.35, (-142, -139));
        Railing(p, true, -149.8, -104.2, -1.35, (-142, -139));
        TrussN(p, 0, 2.5, -150, -104, 4, true);
        for (var n = -146.0; n < -105; n += 12)
            LampPost(p, 1.0, n, 2.6);
        return p.Stats(15000, 30000, 20, 15.0);
    }

    private static Part Reactor()
    {
        var p = New("Freeport Reactor", "Freeport One's reactor at the end of its boom, well away from the people: a heavy station plant (more than a ship can carry), its cooling rings lit.", Hull3);
        const double cn = -156;
        Hid(p, -5, -7, -161, 5, 7, -150);
        Cyl(p, 'u', 0, 0, cn, -7, 7, 6, Hull3, 16, "plated steel");
        foreach (var u in new[] { -4.0, 0.5, 5.0 })
            Cyl(p, 'u', 0, 0, cn, u - 0.35, u + 0.35, 6.25, Frame, 16, "painted steel");
        Cyl(p, 'u', 0, 0, cn, -1.9, -1.3, 6.12, Cyan, 16, glow: true);
        Cyl(p, 'u', 0, 0, cn, 2.7, 3.3, 6.12, Cyan, 16, glow: true);
        Shp(p, "sphere", -5.5, 4.2, cn - 5.5, 5.5, 10.2, cn + 5.5, Hull2, null, 16, null, "painted plating");
        Cyl(p, 'u', 0, 0, cn, -10, -7, 4.5, Frame, 16, "dirty steel", taper: [0.6, 0.6]);
        Det(p, -1.4, -DeckT + 0.01, -150.6, 1.4, 0.004, -149.996, Hazard, "hazard paint");
        Sphere(p, 0, 10.6, cn, 0.4, Red, 8, glow: true);
        foreach (var s in new[] { -1, 1 })
            Det(p, s * 5.9 - 0.3, -6, cn - 0.3, s * 5.9 + 0.3, 6, cn + 0.3, Hazard, "hazard paint");
        return p.Stats(400000, 120000, 60, 30.0);
    }

    private static Part Radiator(int side)
    {
        var p = New($"Freeport Radiator {(side < 0 ? "W" : "E")}", "One of Freeport One's radiator wings off the reactor boom: dark fins that shed the reactor's heat, the hot ones glowing.", Rad);
        double e0 = side * 1.5, e1 = side * 46;
        const double n0 = -141.2, n1 = -139.8;
        Hid(p, Math.Min(e0, e1), -14, n0, Math.Max(e0, e1), 14, n1);
        Det(p, Math.Min(e0, e1), -14, n0 + 0.5, Math.Max(e0, e1), 14, n1 - 0.5, Rad, "dirty steel");
        var a = Math.Min(e0, e1);
        var b = Math.Max(e0, e1);
        for (var e = a + 1.5; e < b - 0.5; e += 1.5)
        {
            var hot = Math.Abs(e) < 18;
            Det(p, e - 0.1, -13.6, n0, e + 0.1, 13.6, n1, hot ? RadHot : Rad, "dirty steel");
        }
        Det(p, a, 13.6, n0 - 0.1, b, 14.3, n1 + 0.1, Frame, "painted steel");
        Det(p, a, -14.3, n0 - 0.1, b, -13.6, n1 + 0.1, Frame, "painted steel");
        Det(p, side < 0 ? a : b - 0.7, -14.3, n0 - 0.1, side < 0 ? a + 0.7 : b, 14.3, n1 + 0.1, Frame, "painted steel");
        Sphere(p, side * 46.3, 14.8, -140.5, 0.4, Red, 8, glow: true);
        return p.Stats(12000, 15000, 5, 15.0);
    }

    // ---- welding the pieces (the Dray's and the Zero Day's way)

    private static double[] Vd(PyDict b, string key) => [.. ((List<object?>)b[key]!).Select(v => Convert.ToDouble(v) + H)];

    private static bool Drawn(PyDict b) => b.Get("detail") is true;

    private static void JSock(Part p, string name, double[] at, string dir) => p.Sock(name, [at[0] - H, at[1] - H, at[2] - H], dir);

    /// <summary>A facing socket pair wherever two of the parts' solid boxes touch face to face (one pair a pair of parts).</summary>
    private static void Connect(List<Part> parts)
    {
        var solid = parts.Select(q => q.Boxes.Where(b => !Drawn(b)).Select(b => (Mn: Vd(b, "min"), Mx: Vd(b, "max"))).ToList()).ToList();
        var count = parts.Select(q => q.Sockets.Count(s => ((string)s["name"]!).StartsWith('j'))).ToArray();
        for (var i = 0; i < parts.Count; i++)
            for (var j = i + 1; j < parts.Count; j++)
            {
                (double Area, double[] At, int Axis, bool IPlus) best = (0, [], 0, false);
                foreach (var A in solid[i])
                    foreach (var B in solid[j])
                        for (var ax = 0; ax < 3; ax++)
                        {
                            bool iPlus = Math.Abs(A.Mx[ax] - B.Mn[ax]) < 0.001, iMinus = Math.Abs(A.Mn[ax] - B.Mx[ax]) < 0.001;
                            if (!iPlus && !iMinus)
                                continue;
                            var at = new double[3];
                            var area = 1.0;
                            var ok = true;
                            for (var a = 0; a < 3 && ok; a++)
                            {
                                if (a == ax)
                                {
                                    at[a] = iPlus ? A.Mx[a] : A.Mn[a];
                                    continue;
                                }
                                double lo = Math.Max(A.Mn[a], B.Mn[a]), hi = Math.Min(A.Mx[a], B.Mx[a]);
                                if (hi - lo < 0.03)
                                    ok = false;
                                at[a] = Math.Round((lo + hi) / 2, 2);
                                area *= hi - lo;
                            }
                            if (ok && area > best.Area)
                                best = (area, at, ax, iPlus);
                        }
                if (best.Area <= 0)
                    continue;
                var axis = "xyz"[best.Axis];
                JSock(parts[i], $"j{count[i]++}", best.At, (best.IPlus ? "+" : "-") + axis);
                JSock(parts[j], $"j{count[j]++}", best.At, (best.IPlus ? "-" : "+") + axis);
            }
    }

    private static bool Meet(Part a, Part b)
    {
        foreach (var s in a.Sockets)
            foreach (var t in b.Sockets)
            {
                var (pa, pb) = ((List<object?>)s["at"]!, (List<object?>)t["at"]!);
                if (Enumerable.Range(0, 3).All(i => Math.Abs(Convert.ToDouble(pa[i]) - Convert.ToDouble(pb[i])) < 0.004))
                {
                    var (da, db) = ((string)s["dir"]!, (string)t["dir"]!);
                    if (da[1] == db[1] && da[0] != db[0])
                        return true;
                }
            }
        return false;
    }

    private static List<string> PlaceOrder(List<Part> parts, string first)
    {
        var names = parts.Select(q => (string)q.D["name"]!).ToList();
        var seen = new HashSet<int> { names.IndexOf(first) };
        var order = new List<string> { first };
        var queue = new Queue<int>(seen);
        while (queue.Count > 0)
        {
            var i = queue.Dequeue();
            for (var j = 0; j < parts.Count; j++)
                if (!seen.Contains(j) && Meet(parts[i], parts[j]))
                {
                    seen.Add(j);
                    order.Add(names[j]);
                    queue.Enqueue(j);
                }
        }
        if (order.Count != parts.Count)
            throw new InvalidOperationException("station_parts: pieces joined to nothing: " + string.Join(", ", names.Where((n, i) => !seen.Contains(i))));
        return order;
    }

    // ---- the blueprint

    private static PyDict Blueprint(Dictionary<string, PyDict> defs, List<string> order)
    {
        var bp = new BuildingParts.Blueprint(StationName, "Freeport One, a free port in orbit: a long spine with an open cargo pier and gantry crane to the west for haulers, two frigate fingers with repair arms to the east, a trader marina, market and customs at the north end, the command hub at the bow, a habitat drum with a roof pad, a fuel farm under the decks and the reactor on a boom at the stern. Its buildings are sealed shells for now: the station's services come from the docking screen.", defs, maxSteps: 200);
        double[] zero = [0, 0, 0];
        var placed = new Dictionary<string, int>();
        foreach (var name in order)
            placed[name] = placed.Count == 0 ? bp.First(name) : bp.Put(name, zero, 0);
        foreach (var f in new[] { "Freeport Finger A2", "Freeport Finger B2", "Freeport Cargo Pier E", "Freeport Roof Pad" })
            bp.Steps[bp.Hang("Docking Clamp Large", placed[f], "clamp", "-y")]["paint"] = Hazard;
        foreach (var f in new[] { "Freeport Marina 1E", "Freeport Marina 1W", "Freeport Marina 2E", "Freeport Marina 2W" })
            bp.Steps[bp.Hang("Docking Clamp Medium", placed[f], "clamp", "-y")]["paint"] = Hazard;
        return bp.Json();
    }

    public void Make(PartOutput o)
    {
        _slabs = 0;
        var parts = new List<Part>();
        for (var i = 0; i < SpineCuts.Length; i++)
            parts.Add(Spine(i));
        parts.AddRange([Drum(0), Drum(1), Drum(2), RoofPad()]);
        parts.AddRange(Hub());
        parts.AddRange([CargoPier(true), CargoPier(false), Gantry()]);
        parts.AddRange([Finger("A1", FingerA, false, 1), Finger("A2", FingerA, true, 1), Finger("B1", FingerB, false, -1), Finger("B2", FingerB, true, -1)]);
        parts.AddRange([RepairArm(0, FingerA, 1), RepairArm(1, FingerB, -1)]);
        for (var i = 0; i < MarinaN.Length; i++)
            parts.AddRange([MarinaFinger(i, 1), MarinaFinger(i, -1)]);
        parts.AddRange([FuelFarm(0), FuelFarm(1)]);
        parts.AddRange(Warehouse());
        parts.AddRange(Market());
        parts.AddRange(Customs());
        parts.AddRange(Engineering());
        parts.AddRange([Boom(), Reactor(), Radiator(-1), Radiator(1)]);
        Connect(parts);
        var order = PlaceOrder(parts, "Freeport Spine 4");
        var defs = new Dictionary<string, PyDict>();
        o.Own(Out);
        foreach (var p in parts)
        {
            var d = p.Json();
            var name = (string)d["name"]!;
            if (name.Length > 32)
                throw new InvalidOperationException($"station_parts: {name} is longer than 32 characters");
            defs.Add(name, d);
            o.File($"{Out}/{FileName(name)}", PyJson.Dumps(d, 2) + "\n");
        }
        BuildingParts.AddPartDefs(o, defs, Out);
        o.File($"{BpOut}/FreeportOneStation.json", PyJson.Dumps(Blueprint(defs, order), 2) + "\n");
    }
}
