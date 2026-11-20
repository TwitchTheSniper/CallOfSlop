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
    public const string Glass = "#8fc8e8", Copper = "#b0703c";

    public const string Skin = "#b4b8ba", Skin2 = "#a1a6a9", Skin3 = "#c3c6c8", Dark = "#3a4046", Dark2 = "#2c3136", Frame = "#1f2226",
        Hazard = "#e0b020", Wall = "#c4c8ca", WallTrim = "#2a2e33", Deck = "#4a4f55", Light = "#d8f0ff", Pad = "#2a2e33";

    private readonly ProcHullShape _h;
    private readonly ProcShipPlan _p;
    private readonly string _code;
    private readonly string _accent;

    // The skin's paint for this ship (a stealth ship's is graphite, matt).
    private readonly string _skin, _skin2, _skin3, _dark, _dark2;
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
        (_skin, _skin2, _skin3, _dark, _dark2) = p.Role == ProcRole.Stealth
            ? ("#454b51", "#3c4247", "#4d5359", "#2a2e33", "#202428")
            : (Skin, Skin2, Skin3, Dark, Dark2);
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
        "nacelle" => $"An engine nacelle of procedural ship {_code} ({label}): its skin and the core its engine plate stands on, on a pylon from the hull.",
        "prong" => $"A prong of procedural ship {_code}'s forked bow ({label}): its skin and its core, welded to the bow's root face.",
        "barrel" => $"A length of procedural ship {_code}'s spinal railgun ({label}): the barrel and its rails.",
        "mouth" => $"The lined tunnel through procedural ship {_code}'s bow into the room its mouth opens.",
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

    /// <summary>
    /// A flat plate through the points (part frame, a convex polygon), T thick about them: a "hull" shape, drawn only. A
    /// <paramref name="square"/> plate (a hull facet, cut at its band's ends) is thickened across the ship's length only, so an end cut
    /// square to the length stays square: its end face never tilts into the plane of the transom or the next band's face.
    /// </summary>
    private static void Plate(Part p, double[][] pts, string color, string finish = "painted plating", bool glass = false, bool glow = false, double thick = T, bool square = false)
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
        // (square: o has no z and o.n = thick/2, so the faces stay thick/2 either side of the plane; a steep plate keeps n)
        var across = n[0] * n[0] + n[1] * n[1];
        double[] o = square && across >= 0.25 ? [n[0] / across * thick / 2, n[1] / across * thick / 2, 0] : [n[0] * thick / 2, n[1] * thick / 2, n[2] * thick / 2];
        var both = new List<double[]>();
        foreach (var q in pts)
            foreach (var s in new[] { -1, 1 })
                both.Add([.. Enumerable.Range(0, 3).Select(k => Py.Round(q[k] + s * o[k], 4))]);
        double[] mn = [.. Enumerable.Range(0, 3).Select(k => both.Min(q => q[k]))];
        double[] mx = [.. Enumerable.Range(0, 3).Select(k => both.Max(q => q[k]))];
        var shape = Kw(("shape", "hull"), ("points", both.Select(q => (object?)q.Select(x => (object?)x).ToList()).ToList()), ("paintable", false));
        if (!glass && !glow)
            shape["finish"] = finish;
        if (glow)
            shape["glow"] = true;
        p.Add(Box(mn, mx, color, detail: true, glass: glass, shape: shape));
    }

    private static void Quad(Part p, double[] a, double[] b, double[] c, double[] d, string color, string finish = "painted plating", bool square = false, bool glass = false)
    {
        var n = Cross(Sub(b, a), Sub(c, a));
        if (Dot(n, n) < 1e-10)
        {
            Plate(p, [a, c, d], color, finish, glass: glass, square: square);
            return;
        }
        if (Math.Abs(Dot(Unit(n), Sub(d, a))) < 0.08)
            Plate(p, [a, b, c, d], color, finish, glass: glass, square: square);
        else
        {
            Plate(p, [a, b, c], color, finish, glass: glass, square: square);
            Plate(p, [a, c, d], color, finish, glass: glass, square: square);
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
    private void Facet(Part p, int k, int s, double za, double zb, string color, string finish, (double Z0, double Z1, double V0, double V1)? hole = null, bool glass = false)
    {
        var dz = k % 2 == 1 ? FacetStagger : 0;
        double[] At(double u, double v)
        {
            var q = OnFacet(k, s, za, zb, u, v);
            return Sh([q[0], q[1], q[2] + dz + (u < 1e-9 ? FacetTrim : u > 1 - 1e-9 ? -FacetTrim : 0)]);
        }
        if (hole is not { } h)
        {
            Quad(p, At(0, 0), At(0, 1), At(1, 1), At(1, 0), color, finish, square: true, glass: glass);
            return;
        }
        double u0 = Math.Clamp((h.Z0 - za) / (zb - za), 0, 1), u1 = Math.Clamp((h.Z1 - za) / (zb - za), 0, 1);
        double v0 = Math.Clamp(h.V0, 0, 1), v1 = Math.Clamp(h.V1, 0, 1);
        if (u0 > 1e-6)
            Quad(p, At(0, 0), At(0, 1), At(u0, 1), At(u0, 0), color, finish, square: true);
        if (u1 < 1 - 1e-6)
            Quad(p, At(u1, 0), At(u1, 1), At(1, 1), At(1, 0), color, finish, square: true);
        if (v0 > 1e-6)
            Quad(p, At(u0, 0), At(u0, v0), At(u1, v0), At(u1, 0), color, finish, square: true);
        if (v1 < 1 - 1e-6)
            Quad(p, At(u0, v1), At(u0, 1), At(u1, 1), At(u1, v1), color, finish, square: true);
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
        if (h.HasWell)
            cuts.AddRange([h.WellZ0, h.WellZ1]); // (a band is in the well or out of it)
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
        if (p.Barrel is { } bar)
            _voidHoles.Add([-bar.HX, bar.Y - bar.HY, bar.Z0 - 1, bar.HX, bar.Y + bar.HY, p.Front[0]]); // (exactly: the void holds the barrel fore of the rooms)
        if (p.Mouth is { } mo)
            _voidHoles.Add([-mo.HalfW - MouthLining, mo.Y0, -1, mo.HalfW + MouthLining, mo.Y1 + MouthLining, mo.Z1]);

        for (var bi = 0; bi + 1 < _bands.Length; bi++)
            Band(bi, _bands[bi], _bands[bi + 1]);
        Caps();
        foreach (var n in h.Nacelles)
            Nacelle(n);
        for (var d = 0; d < h.Decks; d++)
            DeckPieces(d);
        Slabs();
        foreach (var pr in h.Prongs)
            Prong(pr);
        if (p.Barrel is { } barrel)
            Barrel(barrel);
        if (p.Mouth is { } mouth)
            MouthTunnel(mouth);
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

    private Part Keel(int bi) => Piece($"Keel {bi + 1}", "keel", bi, 0, _dark);

    private Part SpineP(int bi) => Piece($"Spine {bi + 1}", "spine", bi, 0, _dark);

    private Part Side(int bi, int s) => Piece($"Hull {(s < 0 ? "P" : "S")}{bi + 1}", "hull", bi, s, _skin);

    /// <summary>A band of the hull: its facets (with the holes the airlocks and hatches go through), the chine rails and the stripe, and the void behind the skin.</summary>
    private void Band(int bi, double za, double zb)
    {
        var h = _h;
        var p = _p;
        var constant = za >= h.MidZ0 - 1e-6 && zb <= h.MidZ1 + 1e-6;
        var inWell = h.HasWell && za >= h.WellZ0 - 1e-6 && zb <= h.WellZ1 + 1e-6;
        var cage = inWell && h.Cage; // (a cage's shell: frames over the chine, open between them)
        // A glazed dome: the bow's upper flares in glass (and its sides when the beam line is up on the top deck: never over a bridge).
        var dome = h.Bow == ProcBow.Dome && zb <= h.BowZ1 + 1e-6;
        var domeSides = dome && h.BeamY >= ProcHullShape.FloorLo(h.Decks - 1) - 0.1;
        // The facets: the keel's flat and the lower flares to the keel piece, the sides to the side pieces, the upper flares and the
        // spine's flat to the spine (over a barge's well, the bulwarks' flares and rims to the sides: the spine is open there).
        foreach (var k in new[] { 0, 1, 2, 3, 4, 5 })
            foreach (var s in k is 0 or 5 ? new[] { 1 } : [-1, 1])
            {
                var piece = k <= 1 ? Keel(bi) : k >= 4 && !inWell ? SpineP(bi) : Side(bi, s);
                var (color, finish) = k switch
                {
                    0 => (_dark, "plated steel"),
                    1 => (_skin2, "painted plating"),
                    2 => (_skin, "painted plating"),
                    3 => (_skin3, "painted plating"),
                    4 => (_skin2, "painted plating"),
                    _ => (_dark, "plated steel"),
                };
                if (cage && k >= 4)
                    continue; // (the cage's sides over the chine and its top are frames: CageFrames)
                if (inWell && k == 5)
                {
                    var hw = h.Mid[4].H;
                    double v0 = (hw - h.WellHalf) / (2 * hw), v1 = (hw + h.WellHalf) / (2 * hw);
                    Facet(Side(bi, 1), 5, 1, za, zb, color, finish, (za, zb, v0, 1.0));
                    Facet(Side(bi, -1), 5, 1, za, zb, color, finish, (za, zb, 0.0, v1));
                    continue;
                }
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
                var glass = dome && (k == 4 || k == 3 && domeSides);
                Facet(piece, k, s, za, zb, glass ? Glass : color, finish, hole, glass);
            }
        // The chine rail along the beam and the accent stripe over it (not across an airlock's housing).
        foreach (var s in new[] { -1, 1 })
        {
            var lk = p.Locks.FirstOrDefault(l => l.Side == s && l.Z0 < zb && l.Z1 > za);
            var piece = Side(bi, s);
            if (lk == null)
            {
                Chine(piece, 2, s, za, zb, 0.09, 0.07, _dark);
                if (zb - za > 0.6)
                    FStrip(piece, 3, s, za, zb, 0.02, 0.98, 0.05, 0.12, 0.012, _accent);
            }
            else
            {
                if (lk.Z0 - za > 0.3)
                {
                    Chine(piece, 2, s, za, lk.Z0, 0.09, 0.07, _dark);
                    FStrip(piece, 3, s, za, zb, 0.02, (lk.Z0 - 0.06 - za) / (zb - za), 0.05, 0.12, 0.012, _accent);
                }
                if (zb - lk.Z1 > 0.3)
                {
                    Chine(piece, 2, s, lk.Z1, zb, 0.09, 0.07, _dark);
                    FStrip(piece, 3, s, za, zb, (lk.Z1 + 0.06 - za) / (zb - za), 0.98, 0.05, 0.12, 0.012, _accent);
                }
            }
        }
        if (cage)
            CageFrames(bi, za, zb);
        else if (inWell)
            WellSides(bi, za, zb);
        if (h.HasWell && Math.Abs(zb - h.WellZ0) < 1e-6)
            WellEnd(SpineP(bi), h.WellZ0, 1);
        if (h.HasWell && Math.Abs(za - h.WellZ1) < 1e-6)
            WellEnd(SpineP(bi), h.WellZ1, -1);
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
        // The decks here, up to the highest with rooms at this z (a stepped hull's low part has the lowest alone), then the spine's
        // layer over the slab on it (over a barge's well, the bulwarks either side of the opening).
        var last = 0;
        for (var d = 0; d < h.Decks; d++)
            if (p.W(d, zm) > 0)
                last = d;
        var well = h.Well(zm);
        for (var j = 0; j <= last + 1; j++)
        {
            var wUp = j < h.Decks ? p.W(j, zm) : 0;
            var wDn = j > 0 ? p.W(j - 1, zm) : 0;
            Split(ProcHullShape.Storey * j, ProcHullShape.Storey * j + ProcHullShape.Slab, Math.Max(Math.Max(wUp, wDn), j == h.Decks ? well : 0), "side", 1.0);
            if (j <= last)
                Split(ProcHullShape.FloorTop(j), ProcHullShape.Ceil(j), p.W(j, zm), "side", 1.1);
        }
        if (!(well > 0 && h.Cage))
            Split(ProcHullShape.FloorTop(last + 1), h.TopY, well, well > 0 ? "side" : "spine", 0.6); // (a cage's shell is open over its roof)
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

    /// <summary>A barge's well in one band: a lining on each bulwark's inner face and a coaming in the accent along each rim.</summary>
    private void WellSides(int bi, double za, double zb)
    {
        var h = _h;
        var wh = h.WellHalf;
        foreach (var s in new[] { -1, 1 })
        {
            var piece = Side(bi, s);
            Detail(piece, s * (wh - 0.024), h.Roof + 0.004, za + 0.002, s * (wh + 0.006), h.TopY - 0.034, zb - 0.002, _skin2, "painted plating");
            Detail(piece, s * (wh - 0.07), h.TopY - 0.015, za + 0.003, s * (wh + 0.05), h.TopY + 0.05, zb - 0.003, _accent, "painted plating");
        }
    }

    /// <summary>A well's end bulkhead at z (on the spine's void that closes it) and the coaming across its top; <paramref name="wellAft"/> 1: the well is aft of z, -1: fore.</summary>
    private void WellEnd(Part piece, double z, int wellAft)
    {
        var h = _h;
        var wh = h.WellHalf;
        Detail(piece, -wh, h.Roof + 0.007, wellAft > 0 ? z - 0.03 : z - 0.004, wh, h.TopY - 0.03, wellAft > 0 ? z + 0.004 : z + 0.03, _skin2, "painted plating");
        Detail(piece, -wh - 0.054, h.TopY - 0.012, wellAft > 0 ? z - 0.05 : z - 0.07, wh + 0.054, h.TopY + 0.055, wellAft > 0 ? z + 0.07 : z + 0.05, _accent, "painted plating");
    }

    private Part NacelleP(ProcNacelle n) => Piece($"Nacelle {(n.Side < 0 ? "P" : "S")}", "nacelle", BandOf(Math.Min((n.Z0 + n.Z1) / 2, _h.Length - 0.1)), n.Side);

    /// <summary>
    /// An engine nacelle: its octagonal skin between its rings (drawn, the odd faces 6 mm aft as the hull's facets), a dark nose,
    /// caps on its nose and tail, an accent band behind the nose, the solid core its tail pad and pylon stand on, and its pylon
    /// from inside the hull's skin out to the core (each length in the hull side piece of its band).
    /// </summary>
    private void Nacelle(ProcNacelle n)
    {
        var piece = NacelleP(n);
        double cx = n.Side * n.X, cy = n.Y;
        (double X, double Y)[] Oct(double k)
        {
            double s = n.S * k, c = n.C * k;
            return [(s, -(s - c)), (s, s - c), (s - c, s), (-(s - c), s), (-s, s - c), (-s, -(s - c)), (-(s - c), -s), (s - c, -s)];
        }
        double[] At((double X, double Y) q, double z) => Sh([cx + q.X, cy + q.Y, z]);
        var rings = n.Rings;
        for (var i = 0; i + 1 < rings.Length; i++)
        {
            var a = Oct(rings[i].K);
            var b = Oct(rings[i + 1].K);
            for (var e = 0; e < 8; e++)
            {
                var f = (e + 1) % 8;
                var dz = e % 2 == 1 ? FacetStagger : 0;
                double za = rings[i].Z + FacetTrim + dz, zb = rings[i + 1].Z - FacetTrim + dz;
                var col = i == 0 ? _dark : e % 2 == 0 ? _skin : _skin2;
                Quad(piece, At(a[e], za), At(a[f], za), At(b[f], zb), At(b[e], zb), col, i == 0 ? "plated steel" : "painted plating", square: true);
            }
        }
        Plate(piece, [.. Oct(rings[0].K).Select(q => At(q, rings[0].Z + 0.002))], _dark2, "plated steel");
        Plate(piece, [.. Oct(rings[^1].K).Select(q => At(q, rings[^1].Z))], _dark2, "plated steel");
        // The accent band round the body just behind the nose, 1.2 cm proud (the odd faces' 4 mm aft).
        var o = Oct(1.0);
        for (var e = 0; e < 8; e++)
        {
            var f = (e + 1) % 8;
            double mx = (o[e].X + o[f].X) / 2, my = (o[e].Y + o[f].Y) / 2, l = Math.Sqrt(mx * mx + my * my);
            double ox = mx / l * 0.012, oy = my / l * 0.012, dz = e % 2 == 1 ? 0.004 : 0;
            double z0 = n.Body + 0.4 + dz, z1 = n.Body + 0.7 + dz;
            Plate(piece, [At((o[e].X + ox, o[e].Y + oy), z0), At((o[f].X + ox, o[f].Y + oy), z0), At((o[f].X + ox, o[f].Y + oy), z1), At((o[e].X + ox, o[e].Y + oy), z1)], _accent);
        }
        var core = n.Core;
        Hidden(piece, cx - core, cy - core, n.Body, cx + core, cy + core, n.Z1 - 0.45);
        for (var bi = 0; bi + 1 < _bands.Length; bi++)
        {
            double a = Math.Max(n.PZ0, _bands[bi]), b = Math.Min(n.PZ1, _bands[bi + 1]);
            if (b - a < 0.05)
                continue;
            Solid(Side(bi, n.Side), n.Side * n.PIn, n.Y - n.PT, a, n.Side * (n.X - core), n.Y + n.PT, b, _dark2, "plated steel");
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
        var bow = Ring(_h.Stations[0], 0.0);
        (double X0, double Y0, double X1, double Y1)? hole = _p.Barrel is { } b && b.Z0 <= _h.HullZ0 + 1e-6
            ? (-b.HX - 0.01, b.Y - b.HY - 0.01, b.HX + 0.01, b.Y + b.HY + 0.01)
            : _p.Mouth is { } m ? (-m.HalfW - MouthLining - 0.01, m.Y0 - 0.01, m.HalfW + MouthLining + 0.01, m.Y1 + MouthLining + 0.01) : null;
        // (a fork's root face is a big face: painted as the skin, the berth between the prongs dark)
        var face = _h.Prongs.Count > 0 ? _skin2 : _dark;
        if (hole is not { } hh)
            Plate(Keel(0), bow, face, "plated steel");
        else
            foreach (var piece in Around(bow, hh))
                Plate(Keel(0), piece, face, "plated steel");
        if (_h.Prongs.Count > 0)
        {
            // The berth's back between the prongs, 6 mm proud of the root face (cut round the muzzle): hazard edges on a salvage
            // berth, copper bars over and under the muzzle between a lancer's rails (each 6 to 8 mm proud of the back's face).
            var pr = _h.Prongs[0];
            double sx = pr.X - pr.SX - 0.03, y0 = pr.Y - pr.SY + 0.12, y1 = pr.Y + pr.SY - 0.12, z = _h.HullZ0 - 0.006;
            double[][] back = [Sh([-sx, y0, z]), Sh([sx, y0, z]), Sh([sx, y1, z]), Sh([-sx, y1, z])];
            foreach (var piece in hole is { } h2 ? Around(back, h2) : [back])
                Plate(Keel(0), piece, _dark, "plated steel");
            if (_p.Barrel is { } br)
            {
                foreach (var yb in new[] { br.Y - br.HY - 0.2, br.Y + br.HY + 0.12 })
                    if (yb > y0 + 0.05 && yb + 0.08 < y1 - 0.05)
                        Detail(Keel(0), -br.HX - 0.25, yb, z - T / 2 - 0.008, br.HX + 0.25, yb + 0.08, z - T / 2 + 0.006, Copper, "plated steel");
            }
            else
                foreach (var yb in new[] { y0 + 0.04, y1 - 0.14 })
                    Detail(Keel(0), -sx + 0.04, yb, z - T / 2 - 0.006, sx - 0.04, yb + 0.1, z - T / 2 + 0.006, Hazard, "hazard paint");
        }
        Plate(Piece("Stern", "stern", _bands.Length - 2, 0, _dark), Ring(_h.Stations[^1], 0.0), _dark2, "plated steel");

        // The plate round an opening: over it, under it, and either side of it (those 3 mm aft: no shared plane).
        IEnumerable<double[][]> Around(double[][] poly, (double X0, double Y0, double X1, double Y1) o)
        {
            var band = Clip(Clip(poly, 1, o.Y0 - H, true), 1, o.Y1 - H, false);
            foreach (var (piece, dz) in new[] { (Clip(poly, 1, o.Y1 - H, true), 0.0), (Clip(poly, 1, o.Y0 - H, false), 0.0), (Clip(band, 0, o.X0 - H, false), 0.003), (Clip(band, 0, o.X1 - H, true), 0.003) })
                if (piece.Length >= 3)
                    yield return [.. piece.Select(q => new[] { q[0], q[1], q[2] + dz })];
        }
    }

    /// <summary>A convex polygon (part frame) clipped to one side of a plane square to an axis: the points with v[axis] over c (above) or under it.</summary>
    private static double[][] Clip(double[][] poly, int axis, double c, bool above)
    {
        var o = new List<double[]>();
        bool In(double[] q) => above ? q[axis] >= c - 1e-9 : q[axis] <= c + 1e-9;
        for (var i = 0; i < poly.Length; i++)
        {
            double[] a = poly[i], b = poly[(i + 1) % poly.Length];
            if (In(a))
                o.Add(a);
            if (In(a) != In(b))
            {
                var t = (c - a[axis]) / (b[axis] - a[axis]);
                o.Add([a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t]);
            }
        }
        // (drop repeats: a polygon clipped at a corner)
        var keep = new List<double[]>();
        foreach (var q in o)
            if (keep.Count == 0 || Math.Abs(keep[^1][0] - q[0]) + Math.Abs(keep[^1][1] - q[1]) > 1e-4)
                keep.Add(q);
        if (keep.Count > 1 && Math.Abs(keep[0][0] - keep[^1][0]) + Math.Abs(keep[0][1] - keep[^1][1]) < 1e-4)
            keep.RemoveAt(keep.Count - 1);
        return Area(keep) < 0.01 ? [] : [.. keep];

        static double Area(List<double[]> q)
        {
            var a = 0.0;
            for (var i = 0; i < q.Count; i++)
                a += q[i][0] * q[(i + 1) % q.Count][1] - q[(i + 1) % q.Count][0] * q[i][1];
            return Math.Abs(a) / 2;
        }
    }

    // ---- the bows' own pieces

    /// <summary>The mouth tunnel's lining: its walls' and ceiling's thickness.</summary>
    public const double MouthLining = 0.15;

    private Part ProngP(int side) => Piece($"Prong {(side < 0 ? "P" : "S")}", "prong", 0, side);

    /// <summary>
    /// A fork's prong: a chamfered box pod from the bow's tip back to the root face (its skin between rings, the odd faces 6 mm aft,
    /// a dark nose and its cap, an accent band behind the nose), its solid core against the root face; a lancer's carry the
    /// railgun's rails down their inner faces.
    /// </summary>
    private void Prong(ProcProng pr)
    {
        var piece = ProngP(pr.Side);
        double cx = pr.Side * pr.X, cy = pr.Y;
        (double X, double Y)[] Oct(double k)
        {
            double sx = pr.SX * k, sy = pr.SY * k, c = pr.C * k;
            return [(sx, -(sy - c)), (sx, sy - c), (sx - c, sy), (-(sx - c), sy), (-sx, sy - c), (-sx, -(sy - c)), (-(sx - c), -sy), (sx - c, -sy)];
        }
        double[] At((double X, double Y) q, double z) => Sh([cx + q.X, cy + q.Y, z]);
        var rings = pr.Rings;
        for (var i = 0; i + 1 < rings.Length; i++)
        {
            var a = Oct(rings[i].K);
            var b = Oct(rings[i + 1].K);
            for (var e = 0; e < 8; e++)
            {
                var f = (e + 1) % 8;
                var dz = e % 2 == 1 ? FacetStagger : 0;
                double za = rings[i].Z + FacetTrim + dz, zb = rings[i + 1].Z - FacetTrim + (i + 2 == rings.Length ? -FacetStagger : dz);
                var col = i == 0 ? _dark : e % 2 == 0 ? _skin : _skin2;
                Quad(piece, At(a[e], za), At(a[f], za), At(b[f], zb), At(b[e], zb), col, i == 0 ? "plated steel" : "painted plating", square: true);
            }
        }
        Plate(piece, [.. Oct(rings[0].K).Select(q => At(q, rings[0].Z + 0.002))], _dark2, "plated steel");
        // The accent band round it behind the nose, 1.2 cm proud.
        var o = Oct(1.0);
        for (var e = 0; e < 8; e++)
        {
            var f = (e + 1) % 8;
            double mx = (o[e].X + o[f].X) / 2, my = (o[e].Y + o[f].Y) / 2, l = Math.Sqrt(mx * mx + my * my);
            double ox = mx / l * 0.012, oy = my / l * 0.012, dz = e % 2 == 1 ? 0.004 : 0;
            double z0 = pr.Body + 0.4 + dz, z1 = pr.Body + 0.7 + dz;
            Plate(piece, [At((o[e].X + ox, o[e].Y + oy), z0), At((o[f].X + ox, o[f].Y + oy), z0), At((o[f].X + ox, o[f].Y + oy), z1), At((o[e].X + ox, o[e].Y + oy), z1)], _accent);
        }
        Hidden(piece, cx - pr.SX + 0.1, cy - pr.SY + 0.1, pr.Body, cx + pr.SX - 0.1, cy + pr.SY - 0.1, pr.Z1);
        if (_p.Barrel != null)
        {
            // The rails: two copper strips down the inner face either side of the barrel's axis, from behind the nose to the root,
            // with dark mounts between (the inner face's straight part bounds them).
            var x = pr.Inner - pr.Side * 0.012;
            double lo = cy - (pr.SY - pr.C) + 0.1, hi = cy + (pr.SY - pr.C) - 0.1, by = _p.Barrel.Y, bh = _p.Barrel.HY;
            foreach (var yr in new[] { Math.Clamp(by - bh * 0.6, lo, hi), Math.Clamp(by + bh * 0.6, lo, hi) })
                Detail(piece, x - 0.012, yr - 0.09, pr.Body + 0.2, x + 0.012, yr + 0.09, pr.Z1 - 0.05, Copper, "plated steel");
            for (var z = pr.Body + 0.8; z < pr.Z1 - 0.5; z += 1.6)
                Detail(piece, x - 0.03, Math.Max(lo, by - bh * 0.35), z, x + 0.03, Math.Min(hi, by + bh * 0.35), z + 0.3, _dark2, "plated steel");
        }
    }

    /// <summary>
    /// A lancer's spinal railgun: the barrel in lengths (one per band) along the lowest deck's centreline with copper rails along its
    /// top and sides, saddles under it to the floor inside the rooms (the void holds it fore of them), the breech block in
    /// engineering, and a muzzle collar at the bow.
    /// </summary>
    private void Barrel(ProcBarrel b)
    {
        var p = _p;
        double ft = ProcHullShape.FloorTop(0), y0 = b.Y - b.HY, y1 = b.Y + b.HY;
        var walls = p.Walls.Where(w => w.Deck == 0 && w.AlongX).Select(w => w.C).ToList();
        for (var bi = 0; bi + 1 < _bands.Length; bi++)
        {
            double za = Math.Max(bi == 0 ? b.Z0 : _bands[bi], b.Z0), zb = Math.Min(_bands[bi + 1], b.BreechZ0);
            if (zb - za < 0.05)
                continue;
            var piece = Piece($"Barrel {bi + 1}", "barrel", bi, 0, _dark2);
            Solid(piece, -b.HX, y0, za, b.HX, y1, zb, _dark2, "plated steel");
            Detail(piece, -0.12, y1, za + 0.004, 0.12, y1 + 0.02, zb - 0.004, Copper, "plated steel");
            foreach (var sd in new[] { -1, 1 })
                Detail(piece, sd * b.HX, b.Y - 0.08, za + 0.004, sd * (b.HX + 0.02), b.Y + 0.08, zb - 0.004, Copper, "plated steel");
            // Saddles to the floor every 2.5 m inside the rooms, clear of the walls' arches.
            for (var z = Math.Ceiling((Math.Max(za, p.Front[0] + ProcShipPlan.Wall) + 0.4) / 2.5) * 2.5; z + 0.3 <= zb - 0.2; z += 2.5)
                if (!walls.Any(c => Math.Abs(c - (z + 0.15)) < 0.45))
                {
                    Solid(piece, -b.HX - 0.08, ft, z, b.HX + 0.08, y0, z + 0.3, _dark, "plated steel");
                    Detail(piece, -b.HX - 0.1, ft + 0.004, z - 0.02, b.HX + 0.1, ft + 0.06, z + 0.32, Hazard, "hazard paint");
                }
        }
        // The breech: a block standing on engineering's floor, the barrel's end in it.
        var breech = Piece("Breech", "barrel", BandOf((b.BreechZ0 + b.Z1) / 2), 0, _dark);
        Solid(breech, -ProcShipPlan.BreechHalf, ft, b.BreechZ0, ProcShipPlan.BreechHalf, b.Y + ProcShipPlan.BreechHalfH, b.Z1, _dark, "plated steel");
        Detail(breech, -ProcShipPlan.BreechHalf - 0.012, b.Y + 0.1, b.BreechZ0 + 0.2, ProcShipPlan.BreechHalf + 0.012, b.Y + 0.22, b.Z1 - 0.2, _accent, "painted plating");
        Detail(breech, -0.3, b.Y + ProcShipPlan.BreechHalfH, b.Z1 - 0.6, 0.3, b.Y + ProcShipPlan.BreechHalfH + 0.012, b.Z1 - 0.2, Light, "painted plating", glow: true);
        // The muzzle collar: a frame round the barrel's end (the lance's, proud of its tip; the fork's, at the root face between the rails).
        var piece0 = Piece($"Barrel {BandOf(Math.Max(0, b.Z0)) + 1}", "barrel", BandOf(Math.Max(0, b.Z0)), 0, _dark2);
        double c0 = b.Z0 <= _h.HullZ0 - 0.1 ? b.Z0 + 0.004 : b.Z0 - 0.25, c1 = b.Z0 <= _h.HullZ0 - 0.1 ? b.Z0 + 0.3 : b.Z0 - 0.025;
        const double r = 0.16, g = 0.004;
        Detail(piece0, -b.HX - r, y1 + g, c0, b.HX + r, y1 + r, c1, _dark, "plated steel");
        Detail(piece0, -b.HX - r, y0 - r, c0, b.HX + r, y0 - g, c1, _dark, "plated steel");
        Detail(piece0, -b.HX - r, y0 - g + 0.001, c0 + 0.003, -b.HX - g, y1 + g - 0.001, c1 - 0.003, _dark, "plated steel");
        Detail(piece0, b.HX + g, y0 - g + 0.001, c0 + 0.003, b.HX + r, y1 + g - 0.001, c1 - 0.003, _dark, "plated steel");
        Detail(piece0, -b.HX - r - 0.012, b.Y - 0.05, c0 + 0.05, -b.HX - r, b.Y + 0.05, c1 - 0.05, _accent, "painted plating");
        Detail(piece0, b.HX + r, b.Y - 0.05, c0 + 0.05, b.HX + r + 0.012, b.Y + 0.05, c1 - 0.05, _accent, "painted plating");
    }

    /// <summary>
    /// A mouth's tunnel from the bow to the room block's fore wall: its lined walls and ceiling, the lowest floor carried forward as
    /// its lip, a hazard frame round the opening and a light strip down the ceiling.
    /// </summary>
    private void MouthTunnel(ProcMouth m)
    {
        var piece = Piece("Mouth", "mouth", 0, 0, _dark2);
        double w = m.HalfW, l = MouthLining, z0 = -0.04, z1 = m.Z1, ft = ProcHullShape.FloorTop(m.D0);
        Solid(piece, -w - l, ft, z0, -w, m.Y1, z1, _dark2, "plated steel");
        Solid(piece, w, ft, z0, w + l, m.Y1, z1, _dark2, "plated steel");
        Solid(piece, -w - l, m.Y1, z0, w + l, m.Y1 + l, z1, _dark2, "plated steel");
        var floor = FloorAt(m.D0, 0, m.Z1 + 0.1) ?? throw new ProcShipException("the mouth: no floor behind it");
        Solid(floor, -w - l, m.Y0, z0, w + l, ft, z1, Deck, "deck plate");
        // The hazard frame on the opening's face and the light.
        Detail(piece, -w - l - 0.004, ft + 0.004, z0 - 0.016, -w + 0.06, m.Y1 + l + 0.004, z0 - 0.002, Hazard, "hazard paint");
        Detail(piece, w - 0.06, ft + 0.004, z0 - 0.016, w + l + 0.004, m.Y1 + l + 0.004, z0 - 0.002, Hazard, "hazard paint");
        Detail(piece, -w + 0.065, m.Y1 - 0.06, z0 - 0.016, w - 0.065, m.Y1 + l + 0.004, z0 - 0.002, Hazard, "hazard paint");
        Detail(piece, -0.08, m.Y1 - 0.025, z0 + 0.3, 0.08, m.Y1, z1 - 0.3, Light, glow: true);
        Detail(floor, -w + 0.2, ft, z0 + 0.1, -w + 0.32, ft + 0.008, z1 - 0.1, Hazard, "hazard paint");
        Detail(floor, w - 0.32, ft, z0 + 0.1, w - 0.2, ft + 0.008, z1 - 0.1, Hazard, "hazard paint");
    }

    /// <summary>
    /// A cage hull's shell over its well, in one band: posts at the skin from the roof to the top, a mid rail and a top rail (in the
    /// accent, the coaming) between them, open between: the hollow shell an ice hauler's chunks ride in.
    /// </summary>
    private void CageFrames(int bi, double za, double zb)
    {
        var h = _h;
        double y0 = h.Roof, y1 = h.TopY - 0.06, xo = Math.Min(h.At(za)[4].H, h.At(zb)[4].H) - 0.08, xi = xo - 0.22;
        var n = Math.Max(1, (int)Math.Ceiling((zb - za - 0.4) / 2.5));
        foreach (var s in new[] { -1, 1 })
        {
            var piece = Side(bi, s);
            for (var k = 0; k <= n; k++)
            {
                var z = za + 0.2 + (zb - za - 0.62) * k / n;
                Solid(piece, s * xi, y0, z, s * xo, y1, z + 0.22, _dark, "plated steel");
            }
            Solid(piece, s * (xi + 0.02), y1 - 0.2, za + 0.005, s * (xo - 0.02), y1 - 0.02, zb - 0.005, _accent, "painted plating");
            var ym = (y0 + y1) / 2;
            Solid(piece, s * (xi + 0.03), ym - 0.07, za + 0.006, s * (xo - 0.03), ym + 0.07, zb - 0.006, _dark2, "plated steel");
        }
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
        // A light strip down the middle of each room under its ceiling (a tall room's, under the ceiling of its top deck).
        foreach (var r in p.Rooms.Where(q => q.Deck == d))
        {
            var piece = Piece(GroupLabel(ZoneGroup(r.Top, r)), "walls", -1, 0, Wall);
            var yl = ProcHullShape.Ceil(r.Top);
            var along = r.Z1 - r.Z0 >= r.X1 - r.X0;
            double cx = (r.X0 + r.X1) / 2, cz = (r.Z0 + r.Z1) / 2;
            if (along)
                Detail(piece, cx - 0.06, yl - 0.025, r.Z0 + 0.4, cx + 0.06, yl, r.Z1 - 0.4, Light, glow: true);
            else
                Detail(piece, r.X0 + 0.4, yl - 0.025, cz - 0.06, r.X1 - 0.4, yl, cz + 0.06, Light, glow: true);
        }
    }

    /// <summary>Which wall piece a room's light goes in on deck d: the zone it is in, by where it is (the fore room, the lobby, the aft room, a side of a middle part).</summary>
    private string ZoneGroup(int d, ProcRoom r)
    {
        if (r.Kind == "lobby")
            return $"d{d}.lobby";
        if (r.Z1 <= _p.ForeEnd(d) + 1e-6)
            return $"d{d}.fore";
        if (r.Z0 >= _p.AftStart(d) - 1e-6)
            return $"d{d}.aft";
        return $"d{d}.{(r.Z1 <= _p.Zl + 1e-6 ? "f" : "a")}{(r.X0 < -1e-6 ? "p" : "s")}";
    }

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
            var full = g.Head >= y1 - y0 - 1e-6;
            var jamb = full ? y1 - 0.004 : y0 + g.Head + 0.07;
            Run(at, g.A, y0, y1, Wall, "painted plating", th, false);
            Run(g.A, g.A + g.W, y0 + g.Head, y1, Wall, "painted plating", th, false);
            Run(g.A - 0.07, g.A, y0, jamb, WallTrim, "painted steel", th + 0.02, true);
            Run(g.A + g.W, g.A + g.W + 0.07, y0, jamb, WallTrim, "painted steel", th + 0.02, true);
            if (!full)
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
            if (j == h.Decks && h.HasWell)
                zsBreaks.UnionWith([h.WellZ0, h.WellZ1]);
            var zl = zsBreaks.ToList();
            var n = 0;
            for (var i = 0; i + 1 < zl.Count; i++)
            {
                double za = zl[i], zb = zl[i + 1], zm = (za + zb) / 2;
                var wd = p.W(below, zm);
                if (wd <= 0 || zb - za < 0.02)
                    continue;
                if (j == h.Decks)
                    wd = Math.Max(wd, h.Well(zm)); // (the roof is the well's floor, bulwark to bulwark)
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
                var deck = j; // (the roof: the well's floor; nothing stands at the fore edge of a hole in it)
                var floor = FloorAt(deck, (hole.X0 + hole.X1) / 2, (hole.Z0 + hole.Z1) / 2) ?? FloorAt(deck, (hole.X0 + hole.X1) / 2, hole.Z1 + 0.3);
                if (floor != null)
                    Rail(floor, hole, y1, j == h.Decks || !p.Ladders.Any(l => l.Deck == deck - 1 && Math.Abs(l.X - (hole.X0 + hole.X1) / 2) < 0.1));
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
        if (Roles[piece.D["name"]!.ToString()!].Role is "floor" or "roof") // (the roof, as the floor of a barge's well)
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
        var p = Piece($"Lock {(l.Side < 0 ? "P" : "S")}", "lock", BandOf((l.Z0 + l.Z1) / 2), l.Side, _dark);
        var s = l.Side;
        double X(double v) => s * v;
        double ft = ProcHullShape.FloorTop(l.Deck), a = l.A, xi = l.XIn, xo = l.XOut;
        void Box(double x0, double y0, double z0, double x1, double y1, double z1, string color, string finish) => Solid(p, X(x0), y0, z0, X(x1), y1, z1, color, finish);
        var slab = Math.Max(_p.WMax(l.Deck, l.Z0, l.Z1), l.Deck > 0 ? _p.WMax(l.Deck - 1, l.Z0, l.Z1) : 0); // (a wider deck under it: its ceiling is the floor there)
        Box(Math.Max(xi, slab), l.Y0, l.Z0, xo, ft, l.Z1, _dark, "plated steel"); // the floor
        Box(xi, ft + 2.4, l.Z0, xo, l.Y1, l.Z1, _dark, "plated steel"); // the roof
        Box(xi, ft, l.Z0, xo, ft + 2.4, a - 0.05, _dark, "plated steel"); // fore, where the outer door slides
        Box(xi, ft, a + 1.55, xo, ft + 2.4, l.Z1, _dark, "plated steel"); // aft
        Box(xo - 0.25, ft + 2.25, a - 0.05, xo, ft + 2.4, a + 1.55, _dark, "plated steel"); // over the outer doorway
        Box(xo - 0.25, ft, a - 0.05, xo, ft + 2.25, a, _dark, "plated steel");
        Box(xo - 0.25, ft, a + 1.5, xo, ft + 2.25, a + 1.55, _dark, "plated steel");
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
        Solid(p, x0 - 0.1, st - 0.1, i0 - 0.1, x1 + 0.1, st, t.Z0, _dark2, "plated steel");
        Solid(p, x0 - 0.1, st - 0.1, t.Z0, x0, st, t.Z1 + 1.66, _dark2, "plated steel");
        Solid(p, x1, st - 0.1, t.Z0, x1 + 0.1, st, t.Z1 + 1.66, _dark2, "plated steel");
        Solid(p, x0, st - 0.1, t.Z1, x1, st, t.Z1 + 1.66, _dark2, "plated steel");
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
                    "transom" => Piece("Stern", "stern", _bands.Length - 2, 0, _dark),
                    "spine" => SpineP(BandOf((a[2] + b[2]) / 2)),
                    "keel" => Keel(BandOf((a[2] + b[2]) / 2)),
                    "bow" => Keel(0),
                    "prong" => ProngP(a[0] < 0 ? -1 : 1),
                    "mast" => SpineP(BandOf((a[2] + b[2]) / 2)),
                    "nacelle" => NacelleP(_h.Nacelles.First(n => n.Side == (a[0] < 0 ? -1 : 1))),
                    _ => Side(BandOf((a[2] + b[2]) / 2), a[0] < 0 ? -1 : 1),
                };
                Solid(piece, a[0], a[1], a[2], b[0], b[1], b[2], Pad, "dirty steel");
                if (s.Where == "mast")
                {
                    // Its post from inside the spine up to the head, with a lamp near the top.
                    double zc = (a[2] + b[2]) / 2;
                    Solid(piece, -0.12, _h.TopY - 0.3, zc - 0.12, 0.12, a[1], zc + 0.12, _dark2, "plated steel");
                    Detail(piece, -0.135, a[1] - 0.35, zc - 0.135, 0.135, a[1] - 0.25, zc + 0.135, _accent, "painted plating", glow: true);
                }
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
