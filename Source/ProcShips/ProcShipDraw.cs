using CallOfSlop.PartKit;
using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.ProcShips;

/// <summary>
/// A procedural ship's welded pieces, drawn from its hull and plan the Zero Day's way (TorpedoFrigateParts): every piece has its
/// origin at the body's (ship metres moved by half a cell), the hull's facets are drawn only, hidden solid boxes stand for the void
/// between the skin and the rooms, and solid boxes of different pieces never overlap. The hull is cut into bands along its length,
/// each a keel, a spine and a side piece each side; each deck is floors, wall groups and ceilings; the airlocks' housings, the
/// stern and the pads under the module sockets are pieces too. Wherever two pieces' solid boxes touch they get a facing socket
/// pair (<see cref="Connect"/>): one pair places a piece, the game welds the rest.
/// </summary>
internal sealed class ProcShipDraw
{
    public const double H = 0.125; // half a cell
    private const double T = 0.04; // a drawn plate's thickness
    public const double FacetStagger = 0.006, FacetTrim = 0.0015;
    public const string Group = "Ship", Section = "Procedural ship";

    // The skeleton's paint: a pale skin over a graphite keel and spine, graphite chine rails, the ship's accent along the beam and on
    // its airlocks, hazard yellow at the doors and hatches; inside, pale walls on a dark deck under a light ceiling.
    public const string Skin = "#b4b8ba", Skin2 = "#a1a6a9", Skin3 = "#c3c6c8", Dark = "#3a4046", Dark2 = "#2c3136", Frame = "#1f2226",
        Hazard = "#e0b020", Wall = "#c4c8ca", WallTrim = "#2a2e33", Deck = "#4a4f55", Light = "#d8f0ff", Pad = "#2a2e33";

    private readonly ProcHullShape _h;
    private readonly ProcShipPlan _p;
    private readonly string _code;
    private readonly string _accent;
    private readonly Dictionary<string, Part> _pieces = new(StringComparer.Ordinal);
    private readonly List<string> _order = [];
    public readonly Dictionary<string, (string Role, int Band, int Side)> Roles = new(StringComparer.Ordinal);
    private readonly List<double[]> _voidHoles = [];
    private double[] _bands = [];
    private readonly Dictionary<Part, int> _joints = [];

    /// <summary>Sockets the blueprint hangs things on: (what, piece name, socket name).</summary>
    public readonly List<(string What, string Piece, string Sock, int Index)> Hangs = [];

    public ProcShipDraw(ProcHullShape h, ProcShipPlan p, string code)
    {
        _h = h;
        _p = p;
        _code = code;
        _accent = p.Accent;
    }

    public static string NameOf(string code, string label) => $"{ProcShipParts.Prefix}{code} {label}";

    private Part Piece(string label, string role, int band = -1, int side = 0, string color = Skin)
    {
        var name = NameOf(_code, label);
        if (_pieces.TryGetValue(name, out var have))
            return have;
        if (name.Length > 32)
            throw new ProcShipException($"piece name too long: {name}");
        var p = new Part(name, Small, Section, Describe(role, label), role: "frame", tool: "welder", material: "steel", color: color, group: Group);
        _pieces[name] = p;
        _order.Add(name);
        Roles[name] = (role, band, side);
        return p;
    }

    private string Describe(string role, string label) => role switch
    {
        "keel" => $"A length of procedural ship {_code}'s keel: its underside's plating and the void over it.",
        "spine" => $"A length of procedural ship {_code}'s spine: its top's plating and the void under it.",
        "hull" => $"A length of procedural ship {_code}'s side ({label}): the skin, the chine rail and the void between it and the rooms.",
        "stern" => $"Procedural ship {_code}'s transom and the pads its engine sockets stand on.",
        "floor" => $"A floor of procedural ship {_code} ({label}).",
        "ceiling" => $"A ceiling of procedural ship {_code} ({label}), where the deck over it is narrower.",
        "roof" => $"The roof over procedural ship {_code}'s top deck ({label}).",
        "walls" => $"Walls of procedural ship {_code} ({label}), with their door frames.",
        "lock" => $"An airlock housing of procedural ship {_code}: a passage out through the void and the skin to its outer door.",
        _ => $"A piece of procedural ship {_code} ({label}).",
    };

    // ---- primitives (ship metres; the part's frame is half a cell off)

    private static double[] P(double x, double y, double z) => [x - H, y - H, z - H];

    private static double[] Sh(double[] q) => P(q[0], q[1], q[2]);

    private static double[] Sub(double[] a, double[] b) => [a[0] - b[0], a[1] - b[1], a[2] - b[2]];

    private static double[] Cross(double[] a, double[] b) => [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]];

    private static double Dot(double[] a, double[] b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];

    private static double[] Unit(double[] a)
    {
        var l = Math.Sqrt(Dot(a, a));
        return l < 1e-12 ? [0, 1, 0] : [a[0] / l, a[1] / l, a[2] / l];
    }

    /// <summary>A flat plate through the points (part frame, a convex polygon), T thick about them: a "hull" shape, drawn only.</summary>
    private static void Plate(Part p, double[][] pts, string color, string finish = "painted plating", bool glass = false, bool glow = false, double thick = T)
    {
        double[]? n = null;
        for (var i = 2; i < pts.Length && n == null; i++)
        {
            var c = Cross(Sub(pts[1], pts[0]), Sub(pts[i], pts[0]));
            if (Dot(c, c) > 1e-8)
                n = Unit(c);
        }
        if (n == null)
            return;
        var both = new List<double[]>();
        foreach (var q in pts)
            foreach (var s in new[] { -1, 1 })
                both.Add([.. Enumerable.Range(0, 3).Select(k => Py.Round(q[k] + s * n[k] * thick / 2, 4))]);
        double[] mn = [.. Enumerable.Range(0, 3).Select(k => both.Min(q => q[k]))];
        double[] mx = [.. Enumerable.Range(0, 3).Select(k => both.Max(q => q[k]))];
        var shape = Kw(("shape", "hull"), ("points", both.Select(q => (object?)q.Select(x => (object?)x).ToList()).ToList()), ("paintable", false));
        if (!glass && !glow)
            shape["finish"] = finish;
        if (glow)
            shape["glow"] = true;
        p.Add(Box(mn, mx, color, detail: true, glass: glass, shape: shape));
    }

    private static void Quad(Part p, double[] a, double[] b, double[] c, double[] d, string color, string finish = "painted plating")
    {
        var n = Cross(Sub(b, a), Sub(c, a));
        if (Dot(n, n) < 1e-10)
        {
            Plate(p, [a, c, d], color, finish);
            return;
        }
        if (Math.Abs(Dot(Unit(n), Sub(d, a))) < 0.08)
            Plate(p, [a, b, c, d], color, finish);
        else
        {
            Plate(p, [a, b, c], color, finish);
            Plate(p, [a, c, d], color, finish);
        }
    }

    private static void Prism(Part p, double[][] a, double[][] b, string color, string finish)
    {
        var both = a.Concat(b).Select(q => q.Select(x => Py.Round(x, 4)).ToArray()).ToList();
        double[] mn = [.. Enumerable.Range(0, 3).Select(k => both.Min(q => q[k]))];
        double[] mx = [.. Enumerable.Range(0, 3).Select(k => both.Max(q => q[k]))];
        var shape = Kw(("shape", "hull"), ("points", both.Select(q => (object?)q.Select(x => (object?)x).ToList()).ToList()), ("paintable", false), ("finish", finish));
        p.Add(Box(mn, mx, color, detail: true, shape: shape));
    }

    private static void Solid(Part p, double x0, double y0, double z0, double x1, double y1, double z1, string color, string? finish = null)
    {
        if (Math.Abs(x1 - x0) < 0.011 || Math.Abs(y1 - y0) < 0.011 || Math.Abs(z1 - z0) < 0.011)
            return;
        var shape = finish == null ? null : Kw(("finish", finish));
        p.Add(Box(P(Math.Min(x0, x1), Math.Min(y0, y1), Math.Min(z0, z1)), P(Math.Max(x0, x1), Math.Max(y0, y1), Math.Max(z0, z1)), color, shape: shape));
    }

    private static void Detail(Part p, double x0, double y0, double z0, double x1, double y1, double z1, string color, string? finish = null, bool glow = false)
    {
        var shape = finish == null && !glow ? null : new PyDict();
        if (finish != null)
            shape!["finish"] = finish;
        if (glow)
            shape!["glow"] = true;
        p.Add(Box(P(Math.Min(x0, x1), Math.Min(y0, y1), Math.Min(z0, z1)), P(Math.Max(x0, x1), Math.Max(y0, y1), Math.Max(z0, z1)), color, detail: true, shape: shape));
    }

    private static void Hidden(Part p, double x0, double y0, double z0, double x1, double y1, double z1)
    {
        if (Math.Abs(x1 - x0) < 0.05 || Math.Abs(y1 - y0) < 0.05 || Math.Abs(z1 - z0) < 0.05)
            return;
        p.Add(Box(P(Math.Min(x0, x1), Math.Min(y0, y1), Math.Min(z0, z1)), P(Math.Max(x0, x1), Math.Max(y0, y1), Math.Max(z0, z1)), hidden: true));
    }

    private static void Sock(Part p, string name, double x, double y, double z, string dir) => p.Sock(name, P(x, y, z), dir);

    /// <summary>A hidden box less every hole in the void (housings, shafts, pads): the pieces round each hole.</summary>
    private void HiddenOut(Part p, double x0, double y0, double z0, double x1, double y1, double z1)
    {
        var pieces = new List<double[]> { new[] { Math.Min(x0, x1), Math.Min(y0, y1), Math.Min(z0, z1), Math.Max(x0, x1), Math.Max(y0, y1), Math.Max(z0, z1) } };
        foreach (var h in _voidHoles)
        {
            var next = new List<double[]>();
            foreach (var b in pieces)
            {
                if (h[0] >= b[3] || h[3] <= b[0] || h[1] >= b[4] || h[4] <= b[1] || h[2] >= b[5] || h[5] <= b[2])
                {
                    next.Add(b);
                    continue;
                }
                var r = (double[])b.Clone();
                for (var a = 0; a < 3; a++)
                {
                    if (h[a] > r[a] + 0.01)
                    {
                        var lo = (double[])r.Clone();
                        lo[a + 3] = h[a];
                        next.Add(lo);
                    }
                    if (h[a + 3] < r[a + 3] - 0.01)
                    {
                        var hi = (double[])r.Clone();
                        hi[a] = h[a + 3];
                        next.Add(hi);
                    }
                    r[a] = Math.Max(r[a], h[a]);
                    r[a + 3] = Math.Min(r[a + 3], h[a + 3]);
                }
            }
            pieces = next;
        }
        foreach (var b in pieces)
            Hidden(p, b[0], b[1], b[2], b[3], b[4], b[5]);
    }

    // ---- the hull's facets

    private double[] OnFacet(int k, int s, double za, double zb, double u, double v) => _h.OnFacet(k, s, za, zb, u, v);

    private double[] FacetNormal(int k, int s, double za, double zb)
    {
        var c = OnFacet(k, s, za, zb, 0.5, 0.5);
        var n = Unit(Cross(Sub(OnFacet(k, s, za, zb, 0.5, 1), OnFacet(k, s, za, zb, 0.5, 0)), Sub(OnFacet(k, s, za, zb, 1, 0.5), OnFacet(k, s, za, zb, 0, 0.5))));
        double[] axis = [0, (_h.KeelY + _h.TopY) / 2, c[2]];
        return Dot(n, Sub(c, axis)) < 0 ? [-n[0], -n[1], -n[2]] : n;
    }

    /// <summary>
    /// Facet k of side s from za to zb, with a hole from (hz0, v0) to (hz1, v1) when given (u along z, v across): the plates round it.
    /// The odd facets sit 6 mm aft of the even ones and each stops 1.5 mm short of its band's ends (the Zero Day's stagger).
    /// </summary>
    private void Facet(Part p, int k, int s, double za, double zb, string color, string finish, (double Z0, double Z1, double V0, double V1)? hole = null)
    {
        var dz = k % 2 == 1 ? FacetStagger : 0;
        double[] At(double u, double v)
        {
            var q = OnFacet(k, s, za, zb, u, v);
            return Sh([q[0], q[1], q[2] + dz + (u < 1e-9 ? FacetTrim : u > 1 - 1e-9 ? -FacetTrim : 0)]);
        }
        if (hole is not { } h)
        {
            Quad(p, At(0, 0), At(0, 1), At(1, 1), At(1, 0), color, finish);
            return;
        }
        double u0 = Math.Clamp((h.Z0 - za) / (zb - za), 0, 1), u1 = Math.Clamp((h.Z1 - za) / (zb - za), 0, 1);
        double v0 = Math.Clamp(h.V0, 0, 1), v1 = Math.Clamp(h.V1, 0, 1);
        if (u0 > 1e-6)
            Quad(p, At(0, 0), At(0, 1), At(u0, 1), At(u0, 0), color, finish);
        if (u1 < 1 - 1e-6)
            Quad(p, At(u1, 0), At(u1, 1), At(1, 1), At(1, 0), color, finish);
        if (v0 > 1e-6)
            Quad(p, At(u0, 0), At(u0, v0), At(u1, v0), At(u1, 0), color, finish);
        if (v1 < 1 - 1e-6)
            Quad(p, At(u0, v1), At(u0, 1), At(u1, 1), At(u1, v1), color, finish);
    }

    /// <summary>A flat strip on a facet (a stripe, a seam), d out from it.</summary>
    private void FStrip(Part p, int k, int s, double za, double zb, double u0, double u1, double v0, double v1, double d, string color, string finish = "painted plating")
    {
        var n = FacetNormal(k, s, za, zb);
        double[] Q(double u, double v)
        {
            var q = OnFacet(k, s, za, zb, u, v);
            return Sh([q[0] + n[0] * d, q[1] + n[1] * d, q[2] + n[2] * d]);
        }
        Plate(p, [Q(u0, v0), Q(u1, v0), Q(u1, v1), Q(u0, v1)], color, finish);
    }

    /// <summary>A chine rail: a faceted bar along profile point i (1..3) on side s from za to zb, w wide each way and h proud.</summary>
    private void Chine(Part p, int i, int s, double za, double zb, double w, double h, string color)
    {
        double[][] Ring(double z)
        {
            var q = _h.At(z);
            double[] c = [s * q[i].H, q[i].Y, z], a = [s * q[i - 1].H, q[i - 1].Y, z], b = [s * q[i + 1].H, q[i + 1].Y, z];
            var da = Unit(Sub(a, c));
            var db = Unit(Sub(b, c));
            var n = Unit([-(da[0] + db[0]), -(da[1] + db[1]), 0]);
            double[] Pt(double[] d1, double t1, double t2) => Sh([c[0] + d1[0] * t1 + n[0] * t2, c[1] + d1[1] * t1 + n[1] * t2, z]);
            return [Pt(da, w, 0.01), Pt(da, w * 0.45, h), Pt(db, w * 0.45, h), Pt(db, w, 0.01)];
        }
        var ra = Ring(za);
        var rb = Ring(zb);
        foreach (var q in ra)
            q[2] += FacetStagger / 2 + FacetTrim;
        foreach (var q in rb)
            q[2] += FacetStagger / 2 - FacetTrim;
        Prism(p, ra, rb, color, "plated steel");
    }

    private (double V0, double V1)? VSpan(int k, double y0, double y1)
    {
        var q = _h.Mid;
        if (k is < 1 or > 4)
            return null;
        double ya = q[k - 1].Y, yb = q[k].Y;
        if (y1 <= ya || y0 >= yb || yb - ya < 1e-6)
            return null;
        return ((y0 - ya) / (yb - ya), (y1 - ya) / (yb - ya));
    }

    // ---- making the pieces

    public List<Part> Make()
    {
        var h = _h;
        var p = _p;
        // The bands: the stations, then nothing longer than 8 m.
        var cuts = new List<double>();
        for (var i = 0; i < h.Stations.Length - 1; i++)
        {
            double za = h.Stations[i].Z, zb = h.Stations[i + 1].Z;
            var n = Math.Max(1, (int)Math.Ceiling((zb - za) / 8.0 - 1e-9));
            for (var k = 0; k < n; k++)
                cuts.Add(k == 0 ? za : Math.Round((za + (zb - za) * k / n) * 2) / 2); // (on the half-metre grid the runs' ends are on)
        }
        cuts.Add(h.Length);
        _bands = [.. cuts.Distinct().OrderBy(z => z)];
        // The holes in the void, first: the airlocks' housings, the hatches' shafts and slots, the pads.
        foreach (var l in p.Locks)
            _voidHoles.Add(l.Side < 0 ? [-l.XOut - 0.01, l.Y0 - 0.005, l.Z0 - 0.005, -l.XIn, l.Y1 + 0.005, l.Z1 + 0.005] : [l.XIn, l.Y0 - 0.005, l.Z0 - 0.005, l.XOut + 0.01, l.Y1 + 0.005, l.Z1 + 0.005]);
        foreach (var t in p.Hatches)
        {
            if (t.Kind == "belly")
            {
                _voidHoles.Add([t.X0 - 0.11, t.Y0 - 1, t.Z0 - 0.11, t.X1 + 0.11, 0.0, t.Z1 + 0.11]);
                _voidHoles.Add([t.X0 - 0.11, t.SillTop - 0.105, t.Z1, t.X1 + 0.11, t.SillTop + 0.085, t.Z1 + 1.67]);
            }
            else
            {
                var zd = t.Z0 - 0.22;
                _voidHoles.Add([t.X0 - 0.11, h.Roof, zd - 0.11, t.X1 + 0.11, h.TopY + 1, zd + 1.66]);
                _voidHoles.Add([t.X0 - 0.11, t.SillTop - 0.105, t.Z1, t.X1 + 0.11, t.SillTop + 0.085, t.Z1 + 1.67]);
            }
        }
        foreach (var s in p.Sockets)
            if (s.PadMin is { } a && s.PadMax is { } b)
                _voidHoles.Add([a[0], a[1], a[2], b[0], b[1], b[2]]); // (exactly: the void touches the pad, which is welded to it)

        for (var bi = 0; bi + 1 < _bands.Length; bi++)
            Band(bi, _bands[bi], _bands[bi + 1]);
        Caps();
        for (var d = 0; d < h.Decks; d++)
            DeckPieces(d);
        Slabs();
        foreach (var l in p.Locks)
            Lock(l);
        foreach (var t in p.Hatches)
            Hatch(t);
        Pads();
        var all = Settle();
        Connect(all);
        return all;
    }

    public int BandOf(double z)
    {
        var i = 0;
        while (i < _bands.Length - 2 && z >= _bands[i + 1])
            i++;
        return i;
    }

    private Part Keel(int bi) => Piece($"Keel {bi + 1}", "keel", bi, 0, Dark);

    private Part SpineP(int bi) => Piece($"Spine {bi + 1}", "spine", bi, 0, Dark);

    private Part Side(int bi, int s) => Piece($"Hull {(s < 0 ? "P" : "S")}{bi + 1}", "hull", bi, s);

    /// <summary>A band of the hull: its facets (with the holes the airlocks and hatches go through), the chine rails and the stripe, and the void behind the skin.</summary>
    private void Band(int bi, double za, double zb)
    {
        var h = _h;
        var p = _p;
        var constant = za >= h.MidZ0 - 1e-6 && zb <= h.MidZ1 + 1e-6;
        // The facets: the keel's flat and the lower flares to the keel piece, the sides to the side pieces, the upper flares and the spine's flat to the spine.
        foreach (var k in new[] { 0, 1, 2, 3, 4, 5 })
            foreach (var s in k is 0 or 5 ? new[] { 1 } : [-1, 1])
            {
                var piece = k <= 1 ? Keel(bi) : k >= 4 ? SpineP(bi) : Side(bi, s);
                var (color, finish) = k switch
                {
                    0 => (Dark, "plated steel"),
                    1 => (Skin2, "painted plating"),
                    2 => (Skin, "painted plating"),
                    3 => (Skin3, "painted plating"),
                    4 => (Skin2, "painted plating"),
                    _ => (Dark, "plated steel"),
                };
                (double, double, double, double)? hole = null;
                if (constant && k is >= 1 and <= 4 && p.Locks.FirstOrDefault(l => l.Side == s && l.Z0 < zb && l.Z1 > za) is { } lk && VSpan(k, lk.Y0, lk.Y1) is { } vs)
                    hole = (lk.Z0, lk.Z1, vs.V0, vs.V1);
                if (constant && k == 0 && p.Hatches.FirstOrDefault(t => t.Kind == "belly" && t.Z0 < zb && t.Z1 > za) is { } bt)
                {
                    var hw = h.Mid[0].H;
                    hole = (bt.Z0, bt.Z1, (bt.X0 + hw) / (2 * hw), (bt.X1 + hw) / (2 * hw));
                }
                if (constant && k == 5 && p.Hatches.FirstOrDefault(t => t.Kind == "dorsal" && t.Z0 < zb && t.Z1 > za) is { } dt)
                {
                    var hw = h.Mid[4].H;
                    hole = (dt.Z0, dt.Z1, (hw - dt.X1) / (2 * hw), (hw - dt.X0) / (2 * hw));
                }
                Facet(piece, k, s, za, zb, color, finish, hole);
            }
        // The chine rail along the beam and the accent stripe over it (not across an airlock's housing).
        foreach (var s in new[] { -1, 1 })
        {
            var lk = p.Locks.FirstOrDefault(l => l.Side == s && l.Z0 < zb && l.Z1 > za);
            var piece = Side(bi, s);
            if (lk == null)
            {
                Chine(piece, 2, s, za, zb, 0.09, 0.07, Dark);
                if (zb - za > 0.6)
                    FStrip(piece, 3, s, za, zb, 0.02, 0.98, 0.05, 0.12, 0.012, _accent);
            }
            else
            {
                if (lk.Z0 - za > 0.3)
                {
                    Chine(piece, 2, s, za, lk.Z0, 0.09, 0.07, Dark);
                    FStrip(piece, 3, s, za, zb, 0.02, (lk.Z0 - 0.06 - za) / (zb - za), 0.05, 0.12, 0.012, _accent);
                }
                if (zb - lk.Z1 > 0.3)
                {
                    Chine(piece, 2, s, lk.Z1, zb, 0.09, 0.07, Dark);
                    FStrip(piece, 3, s, za, zb, (lk.Z1 + 0.06 - za) / (zb - za), 0.98, 0.05, 0.12, 0.012, _accent);
                }
            }
        }
        // The void: in slices along the band (finer where the hull tapers), each layer from what fills it to the skin.
        var breaks = new SortedSet<double> { za, zb };
        for (var d = 0; d < h.Decks; d++)
            foreach (var r in p.Runs[d])
                foreach (var z in new[] { r.Z0, r.Z1 })
                    if (z > za + 0.05 && z < zb - 0.05)
                        breaks.Add(z);
        var taper = Math.Abs(h.SkinHalf(za, h.BeamY) - h.SkinHalf(zb, h.BeamY)) + Math.Abs(h.At(za)[0].Y - h.At(zb)[0].Y) + Math.Abs(h.At(za)[4].Y - h.At(zb)[4].Y);
        if (taper > 0.12)
        {
            var n = Math.Max(1, (int)Math.Ceiling((zb - za) / 1.0));
            for (var k = 1; k < n; k++)
                breaks.Add(Math.Round((za + (zb - za) * k / n) * 100) / 100);
        }
        var zs = breaks.ToList();
        for (var i = 0; i + 1 < zs.Count; i++)
            VoidSlice(bi, zs[i], zs[i + 1]);
    }

    /// <summary>The layers of the void in one slice: the keel's under the lowest floor, each slab's, each deck's rooms', the spine's over the roof.</summary>
    private void VoidSlice(int bi, double za, double zb)
    {
        var h = _h;
        var p = _p;
        var zm = (za + zb) / 2;
        double bottom = Math.Max(h.At(za)[0].Y, h.At(zb)[0].Y) + 0.03, ceiling = Math.Min(h.At(za)[4].Y, h.At(zb)[4].Y) - 0.1;
        var layers = new List<(double Y0, double Y1, double Occ, string Kind)>();
        void Split(double y0, double y1, double occ, string kind, double most)
        {
            y0 = Math.Max(y0, bottom);
            y1 = Math.Min(y1, ceiling);
            if (y1 - y0 < 0.05)
                return;
            var stops = new SortedSet<double> { y0, y1 };
            foreach (var q in h.Mid)
                if (q.Y > y0 + 0.2 && q.Y < y1 - 0.2)
                    stops.Add(q.Y);
            var list = stops.ToList();
            for (var i = 0; i + 1 < list.Count; i++)
            {
                var n = Math.Max(1, (int)Math.Ceiling((list[i + 1] - list[i]) / most - 1e-9));
                for (var k = 0; k < n; k++)
                    layers.Add((list[i] + (list[i + 1] - list[i]) * k / n, list[i] + (list[i + 1] - list[i]) * (k + 1) / n, occ, kind));
            }
        }
        Split(h.KeelY, 0, 0, "keel", 0.6);
        for (var j = 0; j <= h.Decks; j++)
        {
            var wUp = j < h.Decks ? p.W(j, zm) : 0;
            var wDn = j > 0 ? p.W(j - 1, zm) : 0;
            Split(ProcHullShape.Storey * j, ProcHullShape.Storey * j + ProcHullShape.Slab, Math.Max(wUp, wDn), "side", 1.0);
            if (j < h.Decks)
                Split(ProcHullShape.FloorTop(j), j == h.Decks - 1 ? ProcHullShape.Ceil(j) : ProcHullShape.Ceil(j), p.W(j, zm), "side", 1.1);
        }
        Split(h.Roof, h.TopY, 0, "spine", 0.6);
        foreach (var (y0, y1, occ, kind) in layers)
        {
            var xo = Math.Floor((new[] { h.SkinHalf(za, y0), h.SkinHalf(za, y1), h.SkinHalf(zb, y0), h.SkinHalf(zb, y1) }.Min() - 0.06) * 100) / 100;
            if (xo < occ + 0.08)
                continue;
            if (kind == "keel")
                HiddenOut(Keel(bi), -xo, y0, za, xo, y1, zb);
            else if (kind == "spine")
                HiddenOut(SpineP(bi), -xo, y0, za, xo, y1, zb);
            else if (occ <= 0)
            {
                HiddenOut(Side(bi, -1), -xo, y0, za, 0, y1, zb);
                HiddenOut(Side(bi, 1), 0, y0, za, xo, y1, zb);
            }
            else
            {
                HiddenOut(Side(bi, -1), -xo, y0, za, -occ, y1, zb);
                HiddenOut(Side(bi, 1), occ, y0, za, xo, y1, zb);
            }
        }
    }

    /// <summary>The bow's tip and the transom: flat plates over the first and last stations' sections.</summary>
    private void Caps()
    {
        double[][] Ring(ProcHullShape.Station st, double dz)
        {
            var q = st.P;
            var o = new List<double[]>();
            for (var i = 0; i < 5; i++)
                o.Add(Sh([q[i].H, q[i].Y, st.Z + dz]));
            for (var i = 4; i >= 0; i--)
                o.Add(Sh([-q[i].H, q[i].Y, st.Z + dz]));
            return [.. o];
        }
        Plate(Keel(0), Ring(_h.Stations[0], 0.0), Dark, "plated steel");
        Plate(Piece("Stern", "stern", _bands.Length - 2, 0, Dark), Ring(_h.Stations[^1], 0.0), Dark2, "plated steel");
    }

    // ---- the decks

    private static string GroupLabel(string group)
    {
        var dot = group.IndexOf('.');
        var deck = int.Parse(group[1..dot]) + 1;
        var zone = group[(dot + 1)..] switch { "fore" => "Fore", "lobby" => "Lobby", "aft" => "Aft", var z => z.ToUpperInvariant() };
        return $"D{deck} Walls {zone}";
    }

    private void DeckPieces(int d)
    {
        var p = _p;
        double y0 = ProcHullShape.FloorTop(d), y1 = ProcHullShape.Ceil(d);
        var doorIndex = 0;
        foreach (var w in p.Walls.Where(w => w.Deck == d))
        {
            var piece = Piece(GroupLabel(w.Group), "walls", -1, 0, Wall);
            WallRun(piece, w, y0, y1);
        }
        foreach (var door in p.Doors.Where(x => x.Deck == d))
        {
            var piece = Piece(GroupLabel(door.Group), "walls", -1, 0, Wall);
            var name = $"door{doorIndex++}";
            Sock(piece, name, door.AlongX ? door.A : door.C, door.Floor + 1.125, door.AlongX ? door.C : door.A, door.AlongX ? "+x" : "+z");
            Hangs.Add((door.Kind == "lock inner" ? "lock inner" : "door", piece.D["name"]!.ToString()!, name, -1));
        }
        foreach (var (pd, x0, z0, x1, z1) in p.Pillars.Where(q => q.Deck == d))
        {
            var piece = Piece($"D{d + 1} Walls Lobby", "walls", -1, 0, Wall);
            Solid(piece, x0, y0, z0, x1, y1, z1, Wall, "painted plating");
            Detail(piece, x0 - 0.005, y0, z1 - 0.02, x1 + 0.005, y0 + 0.12, z1 + 0.006, WallTrim, "painted steel");
        }
        // A light strip down the middle of each room under its ceiling.
        foreach (var r in p.Rooms.Where(q => q.Deck == d))
        {
            var piece = Piece(GroupLabel(ZoneGroup(d, r)), "walls", -1, 0, Wall);
            var along = r.Z1 - r.Z0 >= r.X1 - r.X0;
            double cx = (r.X0 + r.X1) / 2, cz = (r.Z0 + r.Z1) / 2;
            if (along)
                Detail(piece, cx - 0.06, y1 - 0.025, r.Z0 + 0.4, cx + 0.06, y1, r.Z1 - 0.4, Light, glow: true);
            else
                Detail(piece, r.X0 + 0.4, y1 - 0.025, cz - 0.06, r.X1 - 0.4, y1, cz + 0.06, Light, glow: true);
        }
    }

    private string ZoneGroup(int d, ProcRoom r) => r.Kind switch
    {
        "bridge" or "fore" => $"d{d}.fore",
        "engineering" or "hold" => $"d{d}.aft",
        "lobby" => $"d{d}.lobby",
        "corridor" => $"d{d}.{(r.Z1 <= _p.Zl + 1e-6 ? "f" : "a")}p",
        _ => $"d{d}.{(r.Z1 <= _p.Zl + 1e-6 ? "f" : "a")}{(r.X0 < 0 ? "p" : "s")}",
    };

    /// <summary>A wall run with its doorways: solid between them and over each, dark jambs and a hazard head on each.</summary>
    private static void WallRun(Part p, ProcWall w, double y0, double y1)
    {
        double th = ProcShipPlan.Half;
        void Run(double a, double b, double ya, double yb, string col, string fin, double t, bool detail)
        {
            if (b - a < 0.02 || yb - ya < 0.02)
                return;
            if (w.AlongX)
            {
                if (detail) Detail(p, a, ya, w.C - t, b, yb, w.C + t, col, fin);
                else Solid(p, a, ya, w.C - t, b, yb, w.C + t, col, fin);
            }
            else
            {
                if (detail) Detail(p, w.C - t, ya, a, w.C + t, yb, b, col, fin);
                else Solid(p, w.C - t, ya, a, w.C + t, yb, b, col, fin);
            }
        }
        var at = w.A0;
        foreach (var g in w.Gaps.OrderBy(g => g.A))
        {
            Run(at, g.A, y0, y1, Wall, "painted plating", th, false);
            Run(g.A, g.A + g.W, y0 + g.Head, y1, Wall, "painted plating", th, false);
            Run(g.A - 0.07, g.A, y0, y0 + g.Head + 0.07, WallTrim, "painted steel", th + 0.02, true);
            Run(g.A + g.W, g.A + g.W + 0.07, y0, y0 + g.Head + 0.07, WallTrim, "painted steel", th + 0.02, true);
            Run(g.A, g.A + g.W, y0 + g.Head, y0 + g.Head + 0.07, Hazard, "hazard paint", th + 0.02, true);
            at = g.A + g.W;
        }
        Run(at, w.A1, y0, y1, Wall, "painted plating", th, false);
        // A dark kick strip along its foot, both faces.
        Run(w.A0 + 0.01, w.A1 - 0.01, y0, y0 + 0.12, WallTrim, "painted steel", th + 0.006, true);
    }

    /// <summary>
    /// The slabs: each deck's floor (in pieces along its runs, cut at the lobby), the ceilings where a deck is wider than the one over
    /// it, and the roof over the top deck; each less its holes (ladders, hatches), with a rail round a hole on the deck it opens into.
    /// </summary>
    private void Slabs()
    {
        var h = _h;
        var p = _p;
        var floorN = new Dictionary<int, int>();
        for (var j = 0; j <= h.Decks; j++)
        {
            double y0 = ProcHullShape.Storey * j, y1 = y0 + ProcHullShape.Slab;
            var holes = p.Holes.Where(q => q.Slab == j).ToList();
            if (j < h.Decks)
                foreach (var r in p.Runs[j])
                    foreach (var (za, zb) in Split(r.Z0, r.Z1))
                    {
                        var label = j == h.Decks ? "Roof" : $"D{j + 1} Floor {(floorN[j] = floorN.GetValueOrDefault(j) + 1)}";
                        var piece = Piece(label, "floor", BandOf((za + zb) / 2), 0, Deck);
                        SlabBoxes(piece, -r.W, za, r.W, zb, y0, y1, holes, Deck, "deck plate");
                    }
            // The deck under: its ceiling where it is wider than this one (or this one isn't there); the roof over the top deck.
            if (j == 0)
                continue;
            var below = j - 1;
            var zsBreaks = new SortedSet<double>();
            foreach (var r in p.Runs[below])
            {
                zsBreaks.Add(r.Z0);
                zsBreaks.Add(r.Z1);
            }
            if (j < h.Decks)
                foreach (var r in p.Runs[j])
                {
                    zsBreaks.Add(r.Z0);
                    zsBreaks.Add(r.Z1);
                }
            foreach (var z in new[] { p.Zl, p.Zl + p.Ll })
                zsBreaks.Add(z);
            var zl = zsBreaks.ToList();
            var n = 0;
            for (var i = 0; i + 1 < zl.Count; i++)
            {
                double za = zl[i], zb = zl[i + 1], zm = (za + zb) / 2;
                var wd = p.W(below, zm);
                if (wd <= 0 || zb - za < 0.02)
                    continue;
                var wu = j < h.Decks ? p.W(j, zm) : 0;
                if (wu >= wd - 1e-9)
                    continue;
                var label = j == h.Decks ? $"Roof {BandOf(zm) + 1}" : $"D{below + 1} Ceiling {BandOf(zm) + 1}";
                var piece = Piece(label, j == h.Decks ? "roof" : "ceiling", BandOf(zm), 0, Deck);
                n++;
                if (wu <= 0)
                    SlabBoxes(piece, -wd, za, wd, zb, y0, y1, holes, Deck, "painted plating");
                else
                {
                    SlabBoxes(piece, wu, za, wd, zb, y0, y1, holes, Deck, "painted plating");
                    SlabBoxes(piece, -wd, za, -wu, zb, y0, y1, holes, Deck, "painted plating");
                }
            }
            // Rails round the holes on the deck they open into (open at the aft edge, where a ladder lands people).
            foreach (var hole in holes.Where(q => q.Rail))
            {
                var deck = j < h.Decks ? j : j - 1;
                var floor = FloorAt(deck, (hole.X0 + hole.X1) / 2, (hole.Z0 + hole.Z1) / 2) ?? FloorAt(deck, (hole.X0 + hole.X1) / 2, hole.Z1 + 0.3);
                if (floor != null)
                    Rail(floor, hole, y1, !p.Ladders.Any(l => l.Deck == deck - 1 && Math.Abs(l.X - (hole.X0 + hole.X1) / 2) < 0.1));
            }
        }
        // Deck 0's belly hole has its rail too (it opens in the lowest floor).
        foreach (var hole in p.Holes.Where(q => q.Slab == 0 && q.Rail))
            if ((FloorAt(0, (hole.X0 + hole.X1) / 2, (hole.Z0 + hole.Z1) / 2) ?? FloorAt(0, (hole.X0 + hole.X1) / 2, hole.Z1 + 0.3)) is { } floor)
                Rail(floor, hole, ProcHullShape.FloorTop(0), true);
    }

    /// <summary>A slab z range cut at the lobby's walls and into lengths of at most 10 m.</summary>
    private List<(double, double)> Split(double z0, double z1)
    {
        var stops = new SortedSet<double> { z0, z1 };
        foreach (var z in new[] { _p.Zl, _p.Zl + _p.Ll })
            if (z > z0 + 0.5 && z < z1 - 0.5)
                stops.Add(z);
        var o = new List<(double, double)>();
        var list = stops.ToList();
        for (var i = 0; i + 1 < list.Count; i++)
        {
            var n = Math.Max(1, (int)Math.Ceiling((list[i + 1] - list[i]) / 10.0 - 1e-9));
            for (var k = 0; k < n; k++)
                o.Add((Math.Round((list[i] + (list[i + 1] - list[i]) * k / n) * 2) / 2, k == n - 1 ? list[i + 1] : Math.Round((list[i] + (list[i + 1] - list[i]) * (k + 1) / n) * 2) / 2));
        }
        return o;
    }

    private readonly List<(Part P, int Deck, double X0, double Z0, double X1, double Z1)> _floors = [];

    private Part? FloorAt(int deck, double x, double z) =>
        _floors.FirstOrDefault(f => f.Deck == deck && x >= f.X0 - 1e-6 && x <= f.X1 + 1e-6 && z >= f.Z0 - 1e-6 && z <= f.Z1 + 1e-6).P;

    private void SlabBoxes(Part piece, double x0, double z0, double x1, double z1, double y0, double y1, List<ProcHole> holes, string color, string finish)
    {
        if (Roles[piece.D["name"]!.ToString()!].Role == "floor")
            _floors.Add((piece, (int)Math.Round(y0 / ProcHullShape.Storey), x0, z0, x1, z1));
        var rs = new List<(double X0, double Z0, double X1, double Z1)> { (x0, z0, x1, z1) };
        foreach (var hh in holes)
        {
            var next = new List<(double X0, double Z0, double X1, double Z1)>();
            foreach (var r in rs)
            {
                if (hh.X1 <= r.X0 || hh.X0 >= r.X1 || hh.Z1 <= r.Z0 || hh.Z0 >= r.Z1)
                {
                    next.Add(r);
                    continue;
                }
                if (hh.Z0 > r.Z0) next.Add(r with { Z1 = hh.Z0 });
                if (hh.Z1 < r.Z1) next.Add(r with { Z0 = hh.Z1 });
                double za = Math.Max(r.Z0, hh.Z0), zb = Math.Min(r.Z1, hh.Z1);
                if (hh.X0 > r.X0) next.Add((r.X0, za, hh.X0, zb));
                if (hh.X1 < r.X1) next.Add((hh.X1, za, r.X1, zb));
            }
            rs = next;
        }
        foreach (var r in rs.Where(r => r.X1 - r.X0 > 0.02 && r.Z1 - r.Z0 > 0.02))
            Solid(piece, r.X0, y0, r.Z0, r.X1, y1, r.Z1, color, finish);
    }

    /// <summary>
    /// A safety rail round a hole on its two sides (and its fore edge when <paramref name="fore"/>; else a wall stands there),
    /// open at its aft edge, where a ladder lands people or they step down, with hazard paint on the floor there.
    /// </summary>
    private static void Rail(Part p, ProcHole h, double y, bool fore)
    {
        if (fore)
        {
            Solid(p, h.X0 - 0.06, y, h.Z0 - 0.06, h.X1 + 0.06, y + 1.0, h.Z0, Frame, "painted steel");
            Detail(p, h.X0 - 0.075, y + 0.96, h.Z0 - 0.075, h.X1 + 0.075, y + 1.04, h.Z0 + 0.015, Hazard, "hazard paint");
        }
        Solid(p, h.X0 - 0.06, y, h.Z0, h.X0, y + 1.0, h.Z1, Frame, "painted steel");
        Solid(p, h.X1, y, h.Z0, h.X1 + 0.06, y + 1.0, h.Z1, Frame, "painted steel");
        Detail(p, h.X0 - 0.075, y + 0.96, h.Z0 + (fore ? 0.015 : 0.004), h.X0 + 0.015, y + 1.04, h.Z1 + 0.012, Hazard, "hazard paint");
        Detail(p, h.X1 - 0.015, y + 0.96, h.Z0 + (fore ? 0.015 : 0.004), h.X1 + 0.075, y + 1.04, h.Z1 + 0.012, Hazard, "hazard paint");
        Detail(p, h.X0 - 0.12, y, h.Z1, h.X1 + 0.12, y + 0.012, h.Z1 + 0.12, Hazard, "hazard paint");
    }

    // ---- the ways out

    /// <summary>A side airlock's housing: a passage 1.6 m wide and 2.4 m high out from the lobby's outer wall through the void and the skin, its outer door in its end wall.</summary>
    private void Lock(ProcLock l)
    {
        var p = Piece($"Lock {(l.Side < 0 ? "P" : "S")}", "lock", BandOf((l.Z0 + l.Z1) / 2), l.Side, Dark);
        var s = l.Side;
        double X(double v) => s * v;
        double ft = ProcHullShape.FloorTop(l.Deck), a = l.A, xi = l.XIn, xo = l.XOut;
        void Box(double x0, double y0, double z0, double x1, double y1, double z1, string color, string finish) => Solid(p, X(x0), y0, z0, X(x1), y1, z1, color, finish);
        var slab = Math.Max(_p.WMax(l.Deck, l.Z0, l.Z1), l.Deck > 0 ? _p.WMax(l.Deck - 1, l.Z0, l.Z1) : 0); // (a wider deck under it: its ceiling is the floor there)
        Box(Math.Max(xi, slab), l.Y0, l.Z0, xo, ft, l.Z1, Dark, "plated steel"); // the floor
        Box(xi, ft + 2.4, l.Z0, xo, l.Y1, l.Z1, Dark, "plated steel"); // the roof
        Box(xi, ft, l.Z0, xo, ft + 2.4, a - 0.05, Dark, "plated steel"); // fore, where the outer door slides
        Box(xi, ft, a + 1.55, xo, ft + 2.4, l.Z1, Dark, "plated steel"); // aft
        Box(xo - 0.25, ft + 2.25, a - 0.05, xo, ft + 2.4, a + 1.55, Dark, "plated steel"); // over the outer doorway
        Box(xo - 0.25, ft, a - 0.05, xo, ft + 2.25, a, Dark, "plated steel");
        Box(xo - 0.25, ft, a + 1.5, xo, ft + 2.25, a + 1.55, Dark, "plated steel");
        // Its look: a hazard frame round the outer doorway, the accent band round the housing's face, a deck plate inside.
        void D(double x0, double y0, double z0, double x1, double y1, double z1, string c, string f, bool glow = false) => Detail(p, X(x0), y0, z0, X(x1), y1, z1, c, f, glow);
        D(xo, ft - 0.08, a - 0.14, xo + 0.016, ft + 2.34, a - 0.05, Hazard, "hazard paint");
        D(xo, ft - 0.08, a + 1.55, xo + 0.016, ft + 2.34, a + 1.64, Hazard, "hazard paint");
        D(xo, ft + 2.25, a - 0.05, xo + 0.016, ft + 2.34, a + 1.55, Hazard, "hazard paint");
        D(xo, l.Y1 - 0.18, l.Z0 + 0.01, xo + 0.012, l.Y1 - 0.06, l.Z1 - 0.01, _accent, "painted plating");
        D(xo, l.Y0 + 0.06, l.Z0 + 0.01, xo + 0.012, l.Y0 + 0.18, l.Z1 - 0.01, _accent, "painted plating");
        D(xi + 0.05, ft + 2.38, a + 0.2, xo - 0.3, ft + 2.4, a + 1.3, Light, "painted plating", glow: true);
        Sock(p, "door", X(xo - 0.125), ft + 1.125, a, "+z");
        Hangs.Add(("lock outer", p.D["name"]!.ToString()!, "door", -1));
    }

    /// <summary>A docking hatch's shaft: the lining from the deck to the sill, the sill round the opening and under the slot its lid slides into, and the hatch's socket.</summary>
    private void Hatch(ProcHatch t)
    {
        var h = _h;
        var zc = (t.Z0 + t.Z1) / 2;
        var bi = BandOf(zc);
        var p = t.Kind == "belly" ? Keel(bi) : SpineP(bi);
        double st = t.SillTop, x0 = t.X0, x1 = t.X1;
        // The shaft's interior: the opening (belly), or the opening and the ladder's room fore of it (dorsal).
        double i0 = t.Kind == "belly" ? t.Z0 : t.Z0 - 0.22, i1 = t.Kind == "belly" ? t.Z1 : t.Z1 + 0.03;
        // The lining (0.1 m) round the interior, from the deck's side down to the slot over the sill (belly) or up to the sill's underside (dorsal).
        double ly0 = t.Kind == "belly" ? st + 0.08 : h.Roof, ly1 = t.Kind == "belly" ? 0.0 : st - 0.1;
        Solid(p, x0 - 0.1, ly0, i0 - 0.1, x0, ly1, i1 + 0.1, Frame, "dirty steel");
        Solid(p, x1, ly0, i0 - 0.1, x1 + 0.1, ly1, i1 + 0.1, Frame, "dirty steel");
        Solid(p, x0, ly0, i0 - 0.1, x1, ly1, i0, Frame, "dirty steel");
        Solid(p, x0, ly0, i1, x1, ly1, i1 + 0.1, Frame, "dirty steel");
        // The sill: round the opening, then on under the slot (1.6 m aft).
        Solid(p, x0 - 0.1, st - 0.1, i0 - 0.1, x1 + 0.1, st, t.Z0, Dark2, "plated steel");
        Solid(p, x0 - 0.1, st - 0.1, t.Z0, x0, st, t.Z1 + 1.66, Dark2, "plated steel");
        Solid(p, x1, st - 0.1, t.Z0, x1 + 0.1, st, t.Z1 + 1.66, Dark2, "plated steel");
        Solid(p, x0, st - 0.1, t.Z1, x1, st, t.Z1 + 1.66, Dark2, "plated steel");
        // Hazard paint round the opening (on the sill's top, inside the lid's reach) and a light in the shaft.
        Detail(p, x0 - 0.1, st - 0.012, i0 - 0.1, x0 + 0.004, st + 0.004, t.Z1 + 0.05, Hazard, "hazard paint");
        Detail(p, x1 - 0.004, st - 0.012, i0 - 0.1, x1 + 0.1, st + 0.004, t.Z1 + 0.05, Hazard, "hazard paint");
        if (ly1 - ly0 > 0.3)
            Detail(p, x0 + 0.1, (ly0 + ly1) / 2 - 0.06, i1 - 0.02, x1 - 0.1, (ly0 + ly1) / 2 + 0.06, i1, Light, "painted plating", glow: true);
        Sock(p, "hatch", 0, st, Math.Round((zc - 0.69) * 100) / 100, "+y");
        Hangs.Add((t.Kind + " hatch", p.D["name"]!.ToString()!, "hatch", -1));
    }

    /// <summary>The module sockets' pads (transom, spine, flanks) and the sockets their plates go on; a floor socket goes on its floor.</summary>
    private void Pads()
    {
        var i = 0;
        foreach (var s in _p.Sockets)
        {
            Part piece;
            if (s.PadMin is { } a && s.PadMax is { } b)
            {
                piece = s.Where switch
                {
                    "transom" => Piece("Stern", "stern", _bands.Length - 2, 0, Dark),
                    "spine" => SpineP(BandOf((a[2] + b[2]) / 2)),
                    _ => Side(BandOf((a[2] + b[2]) / 2), a[0] < 0 ? -1 : 1),
                };
                Solid(piece, a[0], a[1], a[2], b[0], b[1], b[2], Pad, "dirty steel");
            }
            else
                piece = FloorAt(s.Deck, s.Sock[0], s.Sock[2]) ?? throw new ProcShipException($"socket {s.Name}: no floor under it"); // (the piece under the socket itself: the plate may lie across two)
            var name = $"mod{i}";
            Sock(piece, name, s.Sock[0], s.Sock[1], s.Sock[2], s.Dir);
            Hangs.Add(("socket", piece.D["name"]!.ToString()!, name, i));
            i++;
        }
        var li = 0;
        foreach (var l in _p.Ladders)
        {
            var piece = FloorAt(l.Deck, l.X, l.Z) ?? throw new ProcShipException($"ladder ({l.Why}): no floor under it");
            var name = $"ladder{li}";
            Sock(piece, name, l.X, ProcHullShape.FloorTop(l.Deck), l.Z, "+y");
            Hangs.Add(("ladder", piece.D["name"]!.ToString()!, name, li));
            li++;
        }
    }

    // ---- welding

    private static bool Drawn(PyDict b) => b.Get("detail") is true;

    /// <summary>The pieces in order, each with something solid (one without joins its neighbour of the same kind).</summary>
    private List<Part> Settle()
    {
        var list = _order.Select(n => _pieces[n]).ToList();
        foreach (var piece in list.ToList())
        {
            if (piece.Boxes.Any(b => !Drawn(b)))
                continue;
            var name = piece.D["name"]!.ToString()!;
            var (role, band, side) = Roles[name];
            var to = list.Where(q => q != piece && q.Boxes.Any(b => !Drawn(b)) && Roles[q.D["name"]!.ToString()!] is var r && r.Role == role && r.Side == side)
                .OrderBy(q => Math.Abs(Roles[q.D["name"]!.ToString()!].Band - band)).FirstOrDefault()
                ?? list.Where(q => q != piece && q.Boxes.Any(b => !Drawn(b))).OrderBy(q => Math.Abs(Roles[q.D["name"]!.ToString()!].Band - band)).First();
            to.Boxes.AddRange(piece.Boxes);
            foreach (var sk in piece.Sockets)
                to.Sockets.Add(sk);
            for (var k = 0; k < Hangs.Count; k++)
                if (Hangs[k].Piece == name)
                    Hangs[k] = (Hangs[k].What, to.D["name"]!.ToString()!, Hangs[k].Sock, Hangs[k].Index);
            list.Remove(piece);
            Roles.Remove(name);
        }
        foreach (var q in list)
        {
            var (role, _, _) = Roles[q.D["name"]!.ToString()!];
            var solid = q.Boxes.Where(b => !Drawn(b)).Sum(b => Vol(b));
            var (mass, hp, armour) = role switch
            {
                "walls" => (Math.Clamp(solid * 900, 200, 8000), 3500.0, 25),
                "floor" or "ceiling" or "roof" => (Math.Clamp(solid * 1200, 200, 12000), 5000.0, 30),
                "lock" => (2400.0, 6000.0, 40),
                "stern" => (1800.0, 8000.0, 40),
                _ => (Math.Clamp(solid * 120, 300, 9000), 9000.0, 40),
            };
            q.Stats(mass, hp, armour, 6.0);
            q.Set(("stage", true));
        }
        return list;

        static double Vol(PyDict b)
        {
            var mn = (List<object?>)b["min"]!;
            var mx = (List<object?>)b["max"]!;
            return Enumerable.Range(0, 3).Aggregate(1.0, (acc, k) => acc * (Convert.ToDouble(mx[k]) - Convert.ToDouble(mn[k])));
        }
    }

    private static double[] Vd(PyDict b, string key) => [.. ((List<object?>)b[key]!).Select(v => Convert.ToDouble(v) + H)];

    /// <summary>The socket pairs <see cref="Connect"/> made: piece, its socket, the other piece, its socket.</summary>
    public readonly List<(string A, string SA, string B, string SB)> Joints = [];

    /// <summary>A facing socket pair wherever two pieces' solid boxes touch face to face (one pair a pair of pieces, on their biggest contact).</summary>
    private void Connect(List<Part> parts)
    {
        var solid = parts.Select(q => q.Boxes.Where(b => !Drawn(b)).Select(b => (Mn: Vd(b, "min"), Mx: Vd(b, "max"))).ToList()).ToList();
        var bounds = solid.Select(l => (Mn: Enumerable.Range(0, 3).Select(a => l.Min(b => b.Mn[a])).ToArray(), Mx: Enumerable.Range(0, 3).Select(a => l.Max(b => b.Mx[a])).ToArray())).ToList();
        var count = new int[parts.Count];
        for (var i = 0; i < parts.Count; i++)
            for (var j = i + 1; j < parts.Count; j++)
            {
                var (bi, bj) = (bounds[i], bounds[j]);
                if (Enumerable.Range(0, 3).Any(a => bi.Mn[a] > bj.Mx[a] + 0.002 || bj.Mn[a] > bi.Mx[a] + 0.002))
                    continue;
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
                string si = $"j{count[i]++}", sj = $"j{count[j]++}";
                Sock(parts[i], si, best.At[0], best.At[1], best.At[2], (best.IPlus ? "+" : "-") + axis);
                Sock(parts[j], sj, best.At[0], best.At[1], best.At[2], (best.IPlus ? "-" : "+") + axis);
                Joints.Add(((string)parts[i].D["name"]!, si, (string)parts[j].D["name"]!, sj));
            }
    }
}
