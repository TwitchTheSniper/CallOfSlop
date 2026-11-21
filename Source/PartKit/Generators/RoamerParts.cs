using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The Roamer (Karl, 2026-11-20: "another Rust Bucket style ship? Glass, sort of like a winnebago styling similar to the panels it
/// has"): the rust bucket's sister, built the same way (a lofted hull of five facets a side through seven rings, the rooms a box
/// standing inside it, the structure welded pieces) but dressed as an old motorhome: cream paint with brown, orange and mustard
/// stripes sweeping up the nose, a wraparound windscreen under a cab-over brow, a band of big framed windows down both sides that
/// look into the rooms through deep reveals, skylights, a chrome bumper and round lamps, a rooftop air unit, a roof rack and a rear
/// ladder. Its rooms are the rust bucket's (the same sockets, so the same fittings hang in them). docs/sandbox/constructs/roamer.md.
///
/// The generator works in ship metres (Sx: x from the port side, y from the lower floor's underside, z from the bow back; the bow
/// is -z) and moves them by half a cell; every structural piece has its origin at the body's (the keel's cell 0).
/// </summary>
public sealed class RoamerParts : IPartGenerator
{
    public string Name => "roamer_parts";

    private const string Out = "Constructs/Parts/Roamer";
    private const string BpOut = "Constructs/Blueprints";
    private const string Group = "Ship";
    private const double H = 0.125; // half a cell: the body's origin is the keel's cell 0's middle
    private const double T = 0.04; // a facet's thickness
    private const double Cx = 5.0; // the middle line

    // The hull's colours: an old motorhome's. Base: faded cream paint; trim: the camper's brown, orange and mustard stripes, the
    // dark plate and chrome; accent (under a tenth): the lamps, primer red, rust.
    private const string HullPaint = "#d3c59f", Patch = "#7f9a8e", Patch2 = "#b9a87c", Rust = "#7a4528", Belly = "#4a423a",
        RoofDark = "#a8a08a", Stripe = "#c8962e", Frame = "#3a3530", Armour = "#8a8070", Armour2 = "#9c917a", Primer = "#8a3a26",
        Wall = "#cbbd98", WallLow = "#7a5434", BathWall = "#d8dcd2", Ceiling = "#bfb59a", Floor = "#6a5e50", Deck = "#5e5448",
        GlassTint = "#8fb4c8", Grow = "#4f8a3a", Leaf = "#6fb04a", LampGlow = "#f2e0b0",
        Brown = "#6b4226", Orange = "#b8602e", Mustard = "#c8962e", Chrome = "#b8bcb6", Skirt = "#8c7a5c";

    // Storey heights (Sx metres).
    private const double F0 = 0.25; // lower floor's top
    private const double F1 = 3.0, F1T = 3.25; // upper floor's underside and top
    private const double Ceil = 6.0, CeilT = 6.06; // the upper ceiling's underside and top

    /// <summary>The hull's rings: half width, height and z of its five points a side (belly edge, chine, beam at the deck line, upper
    /// chine, spine edge). Each point has its own z, so a facet can rake. Ring 0 is the bridge's blunt front (Karl, 2026-11-15: the chisel had to go), over the cargo ramp:
    /// its belly and chine points are the overhang's underside, so the lower facets start at ring 1.</summary>
    private static readonly (double W, double Y, double Z)[][] Rings =
    [
        [(3.2, 2.7, -2.3), (3.2, 2.7, -2.3), (3.55, 3.45, -2.62), (3.35, 5.55, -1.75), (2.75, 6.55, -2.05)], // 0 the front: bumper line, the windscreen raked back, the cab-over brow jutting over it
        [(2.6, -0.6, 0.9), (3.5, 0.0, -0.3), (3.85, 3.0, -1.5), (4.05, 6.3, 0.7), (3.4, 7.05, 1.3)], // 1 the nose's corners: the ramp is its chin
        [(3.8, -0.6, 3.6), (4.6, 0.0, 3.0), (4.72, 3.0, 3.0), (4.65, 6.4, 3.0), (4.05, 7.2, 3.3)], // 2 the shoulder (frame)
        [(3.8, -0.6, 8.5), (4.6, 0.0, 8.5), (4.72, 3.0, 8.5), (4.65, 6.4, 8.5), (4.05, 7.25, 8.5)], // 3 midship (frame)
        [(3.8, -0.6, 13.8), (4.6, 0.0, 14.0), (4.72, 3.0, 14.0), (4.65, 6.4, 14.0), (4.05, 7.25, 13.8)], // 4 the haunch (frame)
        [(3.4, -0.4, 16.06), (4.35, 0.1, 16.06), (4.5, 3.0, 16.06), (4.42, 6.4, 16.06), (3.85, 7.15, 16.06)], // 5 where the rear cap starts
        [(2.7, 0.0, 17.1), (3.85, 0.5, 17.4), (4.15, 3.0, 17.6), (4.05, 5.9, 17.5), (3.3, 6.7, 17.25)], // 6 the rear cap (Karl, 2026-11-20: pulled in and filled, no empty space behind the rooms)
    ];

    private const int B = 0, L = 1, D = 2, U = 3, R = 4; // a ring's points
    private static readonly double[] FrameZ = [3.0, 8.5, 14.0]; // the hoop frames (rings 2, 3, 4)
    private static readonly string[] FrameNames = ["Fore", "Mid", "Aft"];

    // Where people stand (Karl, 2026-11-16: the rooms are what you'd expect inside a ship, a box standing in its own hull as the
    // Zero Day's do, not rooms filling the shell). Aft of the cockpit the rooms have their own walls at these half widths (Lw the
    // hold and the engine room, Uw the upper deck), with a gap of a metre or more out to the skin; hidden voids fill the gap so a
    // round still stops at the skin. The cockpit keeps the whole nose (InHigh), so the canopy's windows are its walls, and the hold
    // has a straight bay at its front, as wide as the ramp lets it be (no walls stepping in with the nose). A socket on the hull's inside is on those walls' faces.
    private const double InLow = 4.63, InHigh = 4.73;
    private const double Lw = 3.75, Uw = 3.5, WallT = 0.06;
    private const double CockpitBack = 4.125; // (the cockpit's back partition's aft face: the upper rooms' walls start here)

    private static double[] P(double x, double y, double z) => [x - H, y - H, z - H];

    private static double[] Pt(int ring, int k, int side)
    {
        var (w, y, z) = Rings[ring][k];
        return P(Cx + side * w, y, z);
    }

    // ---- geometry helpers

    private static double[] Sub(double[] a, double[] b) => [a[0] - b[0], a[1] - b[1], a[2] - b[2]];

    private static double[] Add(double[] a, double[] b, double s = 1) => [a[0] + b[0] * s, a[1] + b[1] * s, a[2] + b[2] * s];

    private static double[] Cross(double[] a, double[] b) => [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]];

    private static double Dot(double[] a, double[] b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];

    private static double[] Unit(double[] a)
    {
        var l = Math.Sqrt(Dot(a, a));
        return [a[0] / l, a[1] / l, a[2] / l];
    }

    private static double[] Lerp(double[] a, double[] b, double t) => [a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t];

    private static double[] Mid(double[] a, double[] b) => Lerp(a, b, 0.5);

    // ---- what piece a box goes to: drawing tags each box with the piece it belongs to (Tag), "~hull" for a hull run that the
    // splitter places by where it stands, nothing for the storey's own piece (its floor, or the roof).

    private const string HullTag = "~hull";
    private static string? Tag;
    private static readonly Dictionary<PyDict, string> Tags = new(ReferenceEqualityComparer.Instance);

    private static void With(string? tag, Action a)
    {
        var was = Tag;
        Tag = tag;
        a();
        Tag = was;
    }

    private static void Put(Part p, PyDict b)
    {
        if (Tag != null)
            Tags[b] = Tag;
        p.Add(b);
    }

    /// <summary>A flat plate through the points (a convex polygon), T thick about them: a "hull" shape, drawn only.</summary>
    private static void Plate(Part p, double[][] pts, string color, string finish = "rusty paint", bool glass = false, bool paint = false, bool glow = false, double thick = T)
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
        var shape = Kw(("shape", "hull"), ("points", both.Select(q => (object?)q.Select(x => (object?)x).ToList()).ToList()));
        if (!paint)
            shape["paintable"] = false;
        if (!glass && !glow)
            shape["finish"] = finish;
        if (glow)
            shape["glow"] = true;
        Put(p, Box(mn, mx, color, detail: true, glass: glass, shape: shape));
    }

    /// <summary>A quad as one plate when flat, else as two triangles.</summary>
    private static void Quad(Part p, double[] a, double[] b, double[] c, double[] d, string color, string finish = "rusty paint", bool glass = false, bool paint = false)
    {
        var n = Cross(Sub(b, a), Sub(c, a));
        var flat = Dot(n, n) < 1e-10 || Math.Abs(Dot(Unit(n), Sub(d, a))) < 0.002;
        if (flat)
            Plate(p, [a, b, c, d], color, finish, glass, paint);
        else
        {
            Plate(p, [a, b, c], color, finish, glass, paint);
            Plate(p, [a, c, d], color, finish, glass, paint);
        }
    }

    /// <summary>A convex solid between two outlines (drawn only).</summary>
    private static void Prism(Part p, double[][] a, double[][] b, string color, string finish, bool glow = false, bool paint = false)
    {
        var both = a.Concat(b).Select(q => q.Select(x => Py.Round(x, 4)).ToArray()).ToList();
        double[] mn = [.. Enumerable.Range(0, 3).Select(k => both.Min(q => q[k]))];
        double[] mx = [.. Enumerable.Range(0, 3).Select(k => both.Max(q => q[k]))];
        var shape = Kw(("shape", "hull"), ("points", both.Select(q => (object?)q.Select(x => (object?)x).ToList()).ToList()));
        if (!paint)
            shape["paintable"] = false;
        if (glow)
            shape["glow"] = true;
        else
            shape["finish"] = finish;
        Put(p, Box(mn, mx, color, detail: true, shape: shape));
    }

    private static void Solid(Part p, double x0, double y0, double z0, double x1, double y1, double z1, string color, string? finish = null)
    {
        var shape = finish == null ? null : Kw(("finish", finish));
        Put(p, Box(P(Math.Min(x0, x1), Math.Min(y0, y1), Math.Min(z0, z1)), P(Math.Max(x0, x1), Math.Max(y0, y1), Math.Max(z0, z1)), color, shape: shape));
    }

    private static void Detail(Part p, double x0, double y0, double z0, double x1, double y1, double z1, string color, string? finish = null, bool glow = false)
    {
        var shape = finish == null && !glow ? null : new PyDict();
        if (finish != null)
            shape!["finish"] = finish;
        if (glow)
            shape!["glow"] = true;
        Put(p, Box(P(Math.Min(x0, x1), Math.Min(y0, y1), Math.Min(z0, z1)), P(Math.Max(x0, x1), Math.Max(y0, y1), Math.Max(z0, z1)), color, detail: true, shape: shape));
    }

    private static void Hidden(Part p, double x0, double y0, double z0, double x1, double y1, double z1) =>
        Put(p, Box(P(Math.Min(x0, x1), y0, Math.Min(z0, z1)), P(Math.Max(x0, x1), y1, Math.Max(z0, z1)), hidden: true));

    /// <summary>A plain socket on a face (Sx metres).</summary>
    private static void Sock(Part p, string name, double x, double y, double z, string dir) => p.Sock(name, P(x, y, z), dir);

    private static double Cm(double v) => Math.Floor(v * 100 + 1e-6) / 100;

    // ---- the skin as numbers: where it is at a height and a z (for the rooms, the voids and what hangs on the outside)

    /// <summary>Point k's half width and height at z, along its own rings (the belly and chine start at the nose ring).</summary>
    private static (double W, double Y) PointAt(int k, double z)
    {
        var r0 = k <= L ? 1 : 0;
        if (z <= Rings[r0][k].Z)
            return (Rings[r0][k].W, Rings[r0][k].Y);
        for (var r = r0; r < Rings.Length - 1; r++)
        {
            var (w0, y0, z0) = Rings[r][k];
            var (w1, y1, z1) = Rings[r + 1][k];
            if (z <= z1)
            {
                var t = z1 - z0 < 1e-9 ? 1 : (z - z0) / (z1 - z0);
                return (w0 + (w1 - w0) * t, y0 + (y1 - y0) * t);
            }
        }
        var (w, y, _) = Rings[^1][k];
        return (w, y);
    }

    /// <summary>The skin's half width at height y and z (0 above or below it).</summary>
    private static double SkinHalf(double z, double y)
    {
        var q = Enumerable.Range(0, 5).Select(k => PointAt(k, z)).ToArray();
        if (y < q[0].Y - 1e-6 || y > q[4].Y + 1e-6)
            return 0;
        for (var i = 0; i < 4; i++)
            if (y <= q[i + 1].Y)
                return q[i + 1].Y - q[i].Y < 1e-9 ? Math.Max(q[i].W, q[i + 1].W) : q[i].W + (q[i + 1].W - q[i].W) * (y - q[i].Y) / (q[i + 1].Y - q[i].Y);
        return q[4].W;
    }

    /// <summary>The front's z at height y (the bumper line's face, the windscreen, the brow), on the middle line.</summary>
    private static double FrontZ(double y) => AlongEnd(0, y);

    /// <summary>The rear face's z at height y, on the middle line.</summary>
    private static double RearZ(double y) => AlongEnd(6, y);

    private static double AlongEnd(int ring, double y)
    {
        var q = Rings[ring];
        for (var k = L; k < R; k++)
            if (y <= q[k + 1].Y || k == R - 1)
            {
                var t = Math.Clamp((y - q[k].Y) / Math.Max(1e-6, q[k + 1].Y - q[k].Y), 0, 1);
                return q[k].Z + (q[k + 1].Z - q[k].Z) * t;
            }
        return q[R].Z;
    }

    /// <summary>The upper side's half width at height y amidships (beam to the upper chine).</summary>
    private static double UpSide(double y) => Rings[3][D].W + (Rings[3][U].W - Rings[3][D].W) * (y - 3.0) / (Rings[3][U].Y - 3.0);

    // ---- the windows down the sides: each a hole in the skin with glass, a frame and a sill, a reveal back to the room's own
    // wall, and glass in that wall too, with curtains inside (a camper's). They sit between the hoop frames and clear of the
    // partitions and of what hangs on the walls (the bathroom has none; the galley range and the cabinet keep their wall).

    private const double Wy0 = 4.15, Wy1 = 5.55; // (sill and head heights)

    private static readonly (int Side, double Z0, double Z1)[] Windows =
    [
        (-1, 4.6, 7.6), (-1, 9.0, 11.3), (-1, 11.6, 13.6), (-1, 14.4, 15.7), // the bedroom, hydroponics (three)
        (1, 6.5, 7.05), (1, 9.0, 11.7), (1, 12.4, 13.4), // the bathroom (frosted), the stair hall, the galley over its table
    ];

    private const string Frosted = "#c4d4d6";

    private static string GlassOf(int side, double z0) => side > 0 && z0 < 8.0 ? Frosted : GlassTint; // (the bathroom's is frosted)

    private static double WinV(double y) => (y - 3.0) / 3.4; // (facet D from the beam at 3 m to the upper chine at 6.4 m, rings 2 to 5)

    private static double WinU(int r, double z) => (z - Rings[r][D].Z) / (Rings[r + 1][D].Z - Rings[r][D].Z);

    /// <summary>Facet D of band r, side s, drawn round the windows' holes, with glass in them.</summary>
    private static void FacetHoles(Part p, int r, int s, List<(int Side, double Z0, double Z1)> holes)
    {
        double[] Q(double u, double v)
        {
            var q = OnFacet(r, D, s, u, v);
            return [q[0], q[1], q[2] + (u < 0.5 ? 0.0015 : -0.0015)];
        }
        void Piece(double u0, double u1, double v0, double v1, bool glass = false, string tint = GlassTint) =>
            Plate(p, [Q(u0, v0), Q(u0, v1), Q(u1, v1), Q(u1, v0)], glass ? tint : HullPaint, "rusty paint", glass: glass, paint: !glass);
        if (holes.Count == 0)
        {
            Piece(0, 1, 0, 1);
            return;
        }
        double vb = WinV(Wy0), vt = WinV(Wy1);
        Piece(0, 1, 0, vb);
        Piece(0, 1, vt, 1);
        vb += 0.001; // (the pieces between: a hair short of the rows, so no two end caps meet in a plane)
        vt -= 0.001;
        var at = 0.0;
        foreach (var w in holes.OrderBy(h => h.Z0))
        {
            double u0 = WinU(r, w.Z0), u1 = WinU(r, w.Z1);
            Piece(at, u0, vb, vt);
            Piece(u0, u1, vb, vt, glass: true, tint: GlassOf(s, w.Z0));
            at = u1;
        }
        Piece(at, 1, vb, vt);
    }

    /// <summary>A window's dressing: its rubber frame and a chrome sill outside, the reveal back to the room's wall, a valance
    /// and two curtains inside.</summary>
    private static void WindowDress(Part p, int r, int s, double z0, double z1)
    {
        double u0 = WinU(r, z0), u1 = WinU(r, z1), vb = WinV(Wy0), vt = WinV(Wy1), du = 0.05 / (Rings[r + 1][D].Z - Rings[r][D].Z);
        // (the gasket laps 12 mm onto the glass all round: its edges never in the plane of a skin piece's)
        FStrip(p, r, D, s, u0 - du, u1 + du, vb - 0.015, vb + 0.012, 0.02, Frame, "rubber");
        FStrip(p, r, D, s, u0 - du, u1 + du, vt - 0.012, vt + 0.015, 0.02, Frame, "rubber");
        FStrip(p, r, D, s, u0 - du, u0 + du * 0.3, vb + 0.012, vt - 0.012, 0.024, Frame, "rubber");
        FStrip(p, r, D, s, u1 - du * 0.3, u1 + du, vb + 0.012, vt - 0.012, 0.024, Frame, "rubber");
        var n = FacetNormal(r, D, s);
        Bar(p, OnFacet(r, D, s, u0 - du, vb - 0.045), OnFacet(r, D, s, u1 + du, vb - 0.045), [n[0] * 0.05, n[1] * 0.05, n[2] * 0.05]);
        // The reveal: four plates from the skin's hole back to the wall's outer face.
        var xw = Cx + s * (Uw + WallT);
        double[] Sk(double y, double z) => OnFacet(r, D, s, WinU(r, z), WinV(y));
        double[] Wl(double y, double z) => P(xw, y, z);
        double[] Wi(double y, double z) => P(xw - s * 0.004, y, z); // (the ends' plates run 4 mm into the wall: no cap in the sill's plane)
        const double E = 0.003;
        Plate(p, [Sk(Wy0 + E, z0), Sk(Wy0 + E, z1), Wl(Wy0 + E, z1), Wl(Wy0 + E, z0)], Wall, "painted plating", paint: true);
        Plate(p, [Sk(Wy1 - E, z0), Sk(Wy1 - E, z1), Wl(Wy1 - E, z1), Wl(Wy1 - E, z0)], Wall, "painted plating", paint: true);
        Plate(p, [Sk(Wy0, z0 + E), Sk(Wy1, z0 + E), Wi(Wy1, z0 + E), Wi(Wy0, z0 + E)], Wall, "painted plating", paint: true);
        Plate(p, [Sk(Wy0, z1 - E), Sk(Wy1, z1 - E), Wi(Wy1, z1 - E), Wi(Wy0, z1 - E)], Wall, "painted plating", paint: true);
        // Inside: a wooden valance over it and a curtain drawn back at each end, in the camper's brown and orange.
        var xi = Cx + s * Uw;
        Detail(p, xi - s * 0.012, Wy1 + 0.04, z0 - 0.12, xi - s * 0.09, Wy1 + 0.19, z1 + 0.12, WallLow, "wood");
        Detail(p, xi - s * 0.02, Wy0 - 0.12, z0 - 0.1, xi - s * 0.05, Wy1 + 0.04, z0 + 0.22, Orange, "fabric");
        Detail(p, xi - s * 0.02, Wy0 - 0.12, z1 - 0.22, xi - s * 0.05, Wy1 + 0.04, z1 + 0.1, Orange, "fabric");
        foreach (var z in new[] { z0 + 0.05, z1 - 0.05 })
            Detail(p, xi - s * 0.017, Wy0 + 0.35, z - 0.06, xi - s * 0.053, Wy0 + 0.39, z + 0.06, Brown, "fabric");
    }

    /// <summary>The camper's three stripes (brown, orange, mustard) along facet D of band r, side s, from u0 to u1, rising by rise0
    /// at u0 and rise1 at u1 (a sweep).</summary>
    private static void Stripes(Part p, int r, int s, double u0, double u1, double rise0, double rise1)
    {
        var n = FacetNormal(r, D, s);
        double[] Q(double u, double v, double d) => Add(OnFacet(r, D, s, u, v), n, d);
        foreach (var (v0, v1, c, d) in new[] { (0.1, 0.15, Brown, 0.012), (0.16, 0.21, Orange, 0.016), (0.22, 0.27, Mustard, 0.012) })
        {
            double a = Math.Max(u0, 0.004), b = Math.Min(u1, 0.996);
            Plate(p, [Q(a, v0 + rise0, d), Q(b, v0 + rise1, d), Q(b, v1 + rise1, d), Q(a, v1 + rise0, d)], c, "worn paint");
        }
    }

    /// <summary>The chin's (and the shut ramp's) z at height y: the plane from the hinge up to the nose's deck-line corner.</summary>
    private static double ChinZ(double y) => Rings[1][L].Z + (Rings[1][D].Z - Rings[1][L].Z) * y / 3.0;

    /// <summary>The lower side's half width at height y amidships (chine to beam).</summary>
    private static double LowSide(double y) => Rings[3][L].W + (Rings[3][D].W - Rings[3][L].W) * y / 3.0;

    /// <summary>The least skin half width over a box's z and y range (sampled at its corners, its middle and the profile's kinks).</summary>
    private static double SkinMin(double z0, double z1, double y0, double y1)
    {
        var m = double.MaxValue;
        foreach (var z in new[] { z0, (z0 + z1) / 2, z1 })
        {
            var ys = new List<double> { y0, y1 };
            for (var k = 0; k < 5; k++)
            {
                var y = PointAt(k, z).Y;
                if (y > y0 && y < y1)
                    ys.Add(y);
            }
            foreach (var y in ys)
                m = Math.Min(m, SkinHalf(z, y));
        }
        return m;
    }

    private static readonly (double Y0, double Y1, double In)[] Levels = [(0.0, F1, InLow), (F1T, CeilT, InHigh)];

    /// <summary>A storey's room half width over z0..z1: its wall line, less where the skin comes in.</summary>
    /// <summary>The hold's front (Karl, 2026-11-16: no floor or walls stepping in with the nose) is a straight bay as wide as the
    /// ramp's mouth lets it be, from the ramp to <see cref="HoldWallFront"/>, where a flat wall steps out to the hold's own.</summary>
    private static double RoomHalf(int level, double z0, double z1)
    {
        var raw = SkinHalfIn(level, z0, z1);
        return level == 0 && raw < Lw - 1e-6 ? BayHalf : raw;
    }

    /// <summary>Where the rear cap starts (ring 5): aft of it the rooms' side walls taper in to the back wall.</summary>
    private static double TailZ => Rings[5][D].Z;

    /// <summary>The rooms' back wall's half width on a storey (inside the skin).</summary>
    private static double BackHalf(int level) => Cm(SkinMin(Back, Back + 0.1, Levels[level].Y0, Levels[level].Y1) - 0.06);

    /// <summary>The tapered side wall's inner face (its half width) at z in the rear cap: the rooms' wall at <see cref="TailZ"/>,
    /// straight in to a hair inside the back wall's ends at <see cref="Back"/>.</summary>
    private static double TaperIn(int level, double z)
    {
        double a = RoomHalf(level, TailZ - 0.3, TailZ), b = Math.Min(a, BackHalf(level) - WallT - 0.01);
        return a + (b - a) * (z - TailZ) / (Back - TailZ);
    }

    private static double SkinHalfIn(int level, double z0, double z1)
    {
        var (y0, y1, _) = Levels[level];
        var inner = level == 0 ? Lw : z1 <= CockpitBack + 1e-6 ? InHigh : Uw;
        return Cm(Math.Min(inner, SkinMin(z0, z1, y0, y1) - 0.08));
    }

    /// <summary>The ramp bay's half width: the narrowest the nose allows at the ramp, less room for the wall's void.</summary>
    private static double BayHalf => Cm(Strips(LowFront, Back, RoomCuts(0)).Min(t => SkinHalfIn(0, t.Z0, t.Z1)) - 0.04);

    /// <summary>Whether a strip of a storey has the rooms' own wall (a room box in the hull), not the skin, at its sides: the whole
    /// lower deck (the hold, its ramp bay and the engine room), and the upper deck aft of the cockpit.</summary>
    private static bool BoxWalled(int level, double z0, double z1) => level == 0 || z0 >= CockpitBack - 1e-6;

    /// <summary>Where the hold's own walls start: the first strip aft of the nose wide enough for them.</summary>
    private static double HoldWallFront => Strips(LowFront, Back, RoomCuts(0)).First(t => SkinHalfIn(0, t.Z0, t.Z1) >= Lw - 1e-6).Z0;

    /// <summary>A storey's strip cuts: the stair's opening, and upstairs the partitions across the rooms (where a wall's paint changes).</summary>
    private static double[] RoomCuts(int level) => level == 0 ? [.. StairCuts, 12.0] : [.. StairCuts, CockpitBack, 8.0, 12.0];

    /// <summary>Strips along z from a to b: cut at every ring, at each frame's edges, at <paramref name="cuts"/>, and every half metre
    /// where the hull narrows (fore of the shoulder, aft of the haunch).</summary>
    private static List<(double Z0, double Z1)> Strips(double a, double b, params double[] cuts)
    {
        var zs = new SortedSet<double> { a, b };
        void AddZ(double z)
        {
            z = Math.Round(z, 3);
            if (z > a + 0.02 && z < b - 0.02)
                zs.Add(z);
        }
        foreach (var ring in Rings)
            foreach (var q in ring)
                AddZ(q.Z);
        foreach (var f in FrameZ)
        {
            AddZ(f - 0.1);
            AddZ(f + 0.1);
        }
        foreach (var c in cuts)
            AddZ(c);
        for (var z = Math.Ceiling(a * 2) / 2; z < 3.0; z += 0.5)
            AddZ(z);
        for (var z = 14.5; z < b; z += 0.5)
            AddZ(z);
        // (no sliver strips: a cut within 5 cm of another is dropped, the frames' edges kept)
        var l = zs.ToList();
        var o = new List<double> { l[0] };
        for (var i = 1; i < l.Count; i++)
        {
            var keep = FrameZ.Any(f => Math.Abs(l[i] - f + 0.1) < 1e-6 || Math.Abs(l[i] - f - 0.1) < 1e-6) || i == l.Count - 1;
            if (l[i] - o[^1] < 0.05 && !keep)
                continue;
            if (l[i] - o[^1] < 0.05 && keep && o.Count > 1)
                o.RemoveAt(o.Count - 1);
            o.Add(l[i]);
        }
        return [.. Enumerable.Range(0, o.Count - 1).Select(i => (o[i], o[i + 1]))];
    }

    private const double LowFront = -0.3, UpFront = 1.4, Back = 16.8; // the rooms' ends (Karl, 2026-11-20: aft into the rear cap, which the tail had filled; the walls step in with its skin)

    private static readonly double[] StairCuts = [8.15, 11.0]; // (StairHole's ends: declared here, before it, for the initialisation order)

    /// <summary>A storey's floor (the lower one to its walls' inner faces, the upper one out to the deck line), less the holes.</summary>
    private static void FloorOf(Part p, int level, double y0, double y1, string color, (double X0, double Z0, double X1, double Z1)[] holes)
    {
        var front = level == 0 ? LowFront : Rings[1][D].Z;
        foreach (var (z0, z1) in Strips(front, Back, [.. holes.SelectMany(h => new[] { h.Z0, h.Z1 }), .. RoomCuts(level)]))
        {
            // (where the rooms are a box, the floor stops under their walls, upstairs over the hold's, and the gap to the skin is open;
            // the cockpit's runs out to the skin, and the ramp bay to its walls)
            var w = z0 >= TailZ - 1e-6 ? Math.Min(TaperIn(level, z0) + WallT, Cm(SkinMin(z0, z1, y0, y1) - 0.03)) // (under the rear cap's tapered walls)
                : BoxWalled(level, z0, z1) ? (level == 0 ? RoomHalf(0, z0, z1) : Lw) + WallT : level == 0 ? RoomHalf(0, z0, z1) : Cm(SkinMin(z0, z1, y0, y1) - 0.03);
            double xa = Cx - w, xb = Cx + w;
            var xs = new List<(double, double)> { (xa, xb) };
            foreach (var h in holes)
                if (z0 >= h.Z0 - 1e-6 && z1 <= h.Z1 + 1e-6)
                    xs = [.. xs.SelectMany(r => new[] { (r.Item1, Math.Min(r.Item2, h.X0)), (Math.Max(r.Item1, h.X1), r.Item2) }).Where(r => r.Item2 - r.Item1 > 0.05)];
            foreach (var (a, b) in xs)
                Solid(p, a, y0, z0, b, y1, z1, color, "tread plate");
        }
    }

    /// <summary>The walls round a storey. Where the rooms are a box in the hull, each side gets the room's own wall (painted plate,
    /// a dark kick strip along its foot), one run per hull piece, and hidden voids from its back out to the skin; where they hug the
    /// skin (the cockpit), only the hidden wall from the room's face out to the skin. A cap at the back (and the
    /// front, upstairs). They are hull runs: the splitter deals them to the hull pieces and the frames.</summary>
    private static void HullWalls(Part p, int level)
    {
        var (y0, y1, _) = Levels[level];
        var front = level == 0 ? LowFront : UpFront;
        var wallTop = level == 0 ? F1 : CeilT;
        var runs = new List<(double Z0, double Z1, double In, string Color, string Finish, int Side)>();
        With(HullTag, () =>
        {
            foreach (var (z0, z1) in Strips(front, Back, RoomCuts(level)))
            {
                if (z0 >= TailZ - 1e-6)
                {
                    // (in the rear cap the side walls run in a straight taper to the back wall, drawn below: only the voids out to the skin here)
                    var tb = TaperIn(level, z0) + WallT;
                    var to = Math.Max(tb + 0.04, Cm(SkinMin(z0, z1, y0, y1) - 0.06));
                    foreach (var s in new[] { -1, 1 })
                        Hidden(p, Cx + s * tb, y0, z0, Cx + s * to, y1, z1);
                    continue;
                }
                var inner = RoomHalf(level, z0, z1);
                var boxed = BoxWalled(level, z0, z1);
                var back = boxed ? inner + WallT : inner;
                var outer = Math.Max(back + 0.04, Cm(SkinMin(z0, z1, y0, y1) - 0.06));
                foreach (var s in new[] { -1, 1 })
                {
                    if (boxed && Zone(z0, z1) >= 10) // (at a hoop frame the gap holds the frame's rib, wall to skin, seen in a cutaway)
                        Solid(p, Cx + s * back, y0, z0, Cx + s * outer, y1, z1, Frame, "dirty steel");
                    else
                        Hidden(p, Cx + s * back, y0, z0, Cx + s * outer, y1, z1);
                    if (boxed && level == 1 && outer > Lw + WallT + 0.04) // (the floor's edge, over the hold's walls, out to the skin)
                        Hidden(p, Cx + s * (Lw + WallT), F1, z0, Cx + s * outer, F1T, z1);
                    if (!boxed)
                        continue;
                    var bath = level == 1 && s > 0 && z0 >= CockpitBack - 1e-6 && z1 <= 8.0 + 1e-6;
                    var (c, f) = bath ? (BathWall, "gloss paint") : (Wall, "painted plating");
                    if (runs.Count > 0 && runs[^1] is var r && r.Side == s && Math.Abs(r.Z1 - z0) < 1e-6 && Math.Abs(r.In - inner) < 1e-6 && r.Color == c
                        && Zone(r.Z0, r.Z1) == Zone(z0, z1))
                        runs[^1] = r with { Z1 = z1 };
                    else
                        runs.Add((z0, z1, inner, c, f, s));
                }
            }
            foreach (var (z0, z1, inner, c, f, s) in runs)
            {
                var face = Cx + s * inner;
                var foot = level == 0 ? F0 : y0;
                // (upstairs a window in the skin has one in the wall behind it: the wall is cut round it and glazed)
                var at = z0;
                foreach (var w in Windows.Where(w => level == 1 && w.Side == s && w.Z0 < z1 && w.Z1 > z0).OrderBy(w => w.Z0))
                {
                    double a = Math.Max(w.Z0, z0), b = Math.Min(w.Z1, z1);
                    if (a - at > 0.01)
                        Solid(p, face, foot, at, face + s * WallT, wallTop, a, c, f);
                    Solid(p, face, foot, a, face + s * WallT, Wy0, b, c, f);
                    Solid(p, face, Wy1, a, face + s * WallT, wallTop, b, c, f);
                    Put(p, Box(P(Math.Min(face + s * 0.002, face + s * (WallT - 0.002)), Wy0 + 0.003, a + 0.003), P(Math.Max(face + s * 0.002, face + s * (WallT - 0.002)), Wy1 - 0.003, b - 0.003), GlassOf(s, w.Z0), glass: true));
                    at = b;
                }
                if (z1 - at > 0.01)
                    Solid(p, face, foot, at, face + s * WallT, wallTop, z1, c, f);
                // (the kick strip 4 to 16 mm proud of the wall, its ends 4 mm short of the strip's: no face in a neighbour's plane)
                Detail(p, face - s * 0.004, (level == 0 ? F0 : y0) + 0.006, z0 + 0.004, face - s * 0.016, (level == 0 ? F0 : y0) + 0.896, z1 - 0.004, WallLow, "matte paint");
            }
            var bw = BackHalf(level);
            Solid(p, Cx - bw, y0, Back, Cx + bw, y1, Back + 0.1, Wall, "painted plating"); // (the rooms' back wall, drawn: the end of the galley and the engine room)
            // The tapered side walls in the rear cap (Karl, 2026-11-20: the walls joined, no steps): from the rooms' walls at the cap's
            // start straight to the back wall, their ends 2 cm into it.
            {
                var foot = level == 0 ? F0 : y0;
                double za = TailZ, zb = Back + 0.02;
                foreach (var s in new[] { -1, 1 })
                {
                    double[][] Out(double y) => [P(Cx + s * TaperIn(level, za), y, za), P(Cx + s * (TaperIn(level, za) + WallT), y, za), P(Cx + s * (TaperIn(level, zb) + WallT), y, zb), P(Cx + s * TaperIn(level, zb), y, zb)];
                    Prism(p, Out(foot + 0.003), Out(wallTop - 0.003), Wall, "painted plating"); // (3 mm inside the back wall's top and foot)
                }
            }
            if (level == 1)
            {
                var fw = Cm(SkinMin(UpFront - 0.04, UpFront, y0, y1) - 0.06);
                Hidden(p, Cx - fw, y0, UpFront - 0.04, Cx + fw, y1, UpFront);
            }
            else
            {
                // The ramp bay's aft end: where the hold's own walls start, a flat plate each side from the bay's wall (3 mm into it,
                // no shared face) out to the skin.
                var z = HoldWallFront;
                var bi = BayHalf + 0.003;
                foreach (var s in new[] { -1, 1 })
                {
                    double OutAt(double y) => Math.Max(Lw + WallT, SkinHalf(z, y) - 0.03);
                    // (4 mm inside the bay wall's top and foot, which it overlaps)
                    double ya = F0 + 0.004, yb = F1 - 0.004;
                    double[][] Tri(double zz) => [P(Cx + s * bi, ya, zz), P(Cx + s * OutAt(ya), ya, zz), P(Cx + s * OutAt(yb), yb, zz), P(Cx + s * bi, yb, zz)];
                    Prism(p, Tri(z - 0.06), Tri(z - 0.004), Wall, "painted plating");
                }
            }
        });
    }

    /// <summary>The piece a stretch of hull wall at z0..z1 goes to (as Where deals them): a frame's band, else a hull run.</summary>
    private static int Zone(double z0, double z1)
    {
        for (var i = 0; i < FrameZ.Length; i++)
            if (z0 >= FrameZ[i] - 0.105 && z1 <= FrameZ[i] + 0.105)
                return 10 + i;
        var zc = (z0 + z1) / 2;
        return zc < 2.95 ? 0 : zc > 14.05 ? 3 : zc < 8.5 ? 1 : 2;
    }

    /// <summary>Hidden boxes filling the skin between z0 and z1 over a band of heights, in strips (no room in it: the nose, the roof,
    /// the belly, the tail).</summary>
    private static void Void(Part p, double z0, double z1, double y0, double y1, double minHalf = 0.25)
    {
        foreach (var (a, b) in Strips(z0, z1))
        {
            var w = Cm(SkinMin(a, b, y0, y1) - 0.06);
            if (w >= minHalf)
                Hidden(p, Cx - w, y0, a, Cx + w, y1, b);
        }
    }

    // ---- the skin as plates: facets between rings, chine rails along the edges, slabs and strips on the facets

    /// <summary>The point at (u along the band from ring r to r+1, v across facet k from point k to k+1) on side s.</summary>
    private static double[] OnFacet(int r, int k, int s, double u, double v) =>
        Lerp(Lerp(Pt(r, k, s), Pt(r, k + 1, s), v), Lerp(Pt(r + 1, k, s), Pt(r + 1, k + 1, s), v), u);

    private static double[] FacetNormal(int r, int k, int s)
    {
        var n = Unit(Cross(Sub(OnFacet(r, k, s, 0.5, 1), OnFacet(r, k, s, 0.5, 0)), Sub(OnFacet(r, k, s, 1, 0.5), OnFacet(r, k, s, 0, 0.5))));
        var c = OnFacet(r, k, s, 0.5, 0.5);
        double[] axis = [Cx - H, 3.0 - H, c[2]];
        return Dot(n, Sub(c, axis)) < 0 ? [-n[0], -n[1], -n[2]] : n;
    }

    /// <summary>Facet k of the band from ring r to r+1, side s. The odd facets sit 6 mm aft of the even ones and each stops 1.5 mm
    /// short of its band's ends, so where neighbours' plates cross their ends never share a plane.</summary>
    private static void Facet(Part p, int r, int k, int s, string color, string finish = "rusty paint", bool glass = false, bool paint = true, double u0 = 0, double u1 = 1)
    {
        var dz = k % 2 == 1 ? 0.006 : 0;
        double[] Q(double u, double v)
        {
            var q = OnFacet(r, k, s, u, v);
            return [q[0], q[1], q[2] + dz + (u < 0.5 ? 0.0015 : -0.0015)];
        }
        Quad(p, Q(u0, 0), Q(u0, 1), Q(u1, 1), Q(u1, 0), color, finish, glass, paint);
    }

    /// <summary>A bevelled slab on facet k (band r, side s) over (u0..u1, v0..v1), standing d0..d1 out, its face drawn in by bev.</summary>
    private static void Slab(Part p, int r, int k, int s, double u0, double u1, double v0, double v1, double d0, double d1, double bev, string color, string finish = "plated steel", bool paint = false)
    {
        var n = FacetNormal(r, k, s);
        var lu = Math.Sqrt(Dot(Sub(OnFacet(r, k, s, 1, 0.5), OnFacet(r, k, s, 0, 0.5)), Sub(OnFacet(r, k, s, 1, 0.5), OnFacet(r, k, s, 0, 0.5))));
        var lv = Math.Sqrt(Dot(Sub(OnFacet(r, k, s, 0.5, 1), OnFacet(r, k, s, 0.5, 0)), Sub(OnFacet(r, k, s, 0.5, 1), OnFacet(r, k, s, 0.5, 0))));
        double bu = bev / Math.Max(lu, 0.1), bv = bev / Math.Max(lv, 0.1);
        double[][] inner = [.. new[] { (u0, v0), (u1, v0), (u1, v1), (u0, v1) }.Select(t => Add(OnFacet(r, k, s, t.Item1, t.Item2), n, d0))];
        double[][] outer = [.. new[] { (u0 + bu, v0 + bv), (u1 - bu, v0 + bv), (u1 - bu, v1 - bv), (u0 + bu, v1 - bv) }.Select(t => Add(OnFacet(r, k, s, t.Item1, t.Item2), n, d1))];
        Prism(p, inner, outer, color, finish, paint: paint);
    }

    /// <summary>A flat strip on a facet, d out from it: a seam, a stripe, a streak of rust, a lamp's lens.</summary>
    private static void FStrip(Part p, int r, int k, int s, double u0, double u1, double v0, double v1, double d, string color, string finish = "worn paint", bool glow = false)
    {
        var n = FacetNormal(r, k, s);
        double[] Q(double u, double v) => Add(OnFacet(r, k, s, u, v), n, d);
        Plate(p, [Q(u0, v0), Q(u1, v0), Q(u1, v1), Q(u0, v1)], color, finish, glow: glow);
    }

    /// <summary>A chine rail along point k (1..4) on side s from ring r to r+1: a faceted bar w wide each way and h proud, its ends
    /// a few mm inside the band so the next band's rail never shares their plane.</summary>
    private static void Chine(Part p, int r, int k, int s, double w, double h, string color)
    {
        double[][] Section(int ring, double inset)
        {
            var z = Rings[ring][k].Z + inset;
            var (cw, cy) = PointAt(k, z);
            var (aw, ay) = PointAt(k - 1, z);
            double[] c = [Cx + s * cw, cy], a = [Cx + s * aw, ay];
            double[] b2;
            if (k == R)
                b2 = [Cx - s * cw, cy];
            else
            {
                var (bw, by) = PointAt(k + 1, z);
                b2 = [Cx + s * bw, by];
            }
            double[] da = U2(a[0] - c[0], a[1] - c[1]), db = U2(b2[0] - c[0], b2[1] - c[1]);
            var n = U2(-(da[0] + db[0]), -(da[1] + db[1]));
            double[] At(double[] d1, double t1, double t2) => P(c[0] + d1[0] * t1 + n[0] * t2, c[1] + d1[1] * t1 + n[1] * t2, z);
            return [At(da, w, 0.01), At(da, w * 0.45, h), At(db, w * 0.45, h), At(db, w, 0.01)];
        }
        Prism(p, Section(r, 0.006), Section(r + 1, -0.006), color, "plated steel");
    }

    private static double[] U2(double x, double y)
    {
        var l = Math.Sqrt(x * x + y * y);
        return l < 1e-9 ? [0, 1] : [x / l, y / l];
    }

    /// <summary>The panels on facet k of band r, side s (v0..v1 across it): about 2.7 m long each, each one of a rust bucket's
    /// repairs: a dark armour slab, a stepped double slab, a mismatched patch plate, two half slabs at different heights, or bare
    /// paint with a seam, a rust streak and a primer scuff. salt varies the pattern band to band and side to side.</summary>
    private static void Plates(Part p, int r, int k, int s, int salt, double v0 = 0.07, double v1 = 0.93, double clearTo = -1)
    {
        var lu = Math.Sqrt(Dot(Sub(OnFacet(r, k, s, 1, 0.5), OnFacet(r, k, s, 0, 0.5)), Sub(OnFacet(r, k, s, 1, 0.5), OnFacet(r, k, s, 0, 0.5))));
        var n = Math.Max(1, (int)Math.Round(lu / 2.7));
        var gap = 0.05 / lu;
        for (var i = 0; i < n; i++)
        {
            double u0 = (double)i / n + gap, u1 = (double)(i + 1) / n - gap;
            if (u0 < clearTo)
                u0 = clearTo;
            if (u1 - u0 < 0.08)
                continue;
            var pick = ((k * 7 + i * 3 + salt * 5 + r * 2 + (s > 0 ? 1 : 0)) % 7 + 7) % 7;
            switch (pick)
            {
                case 0:
                case 3:
                    Slab(p, r, k, s, u0, u1, v0, v1, 0.02, 0.12, 0.06, Armour);
                    break;
                case 1:
                    Slab(p, r, k, s, u0, u1, v0, v1, 0.02, 0.1, 0.06, Armour2);
                    Slab(p, r, k, s, u0 + (u1 - u0) * 0.15, u1 - (u1 - u0) * 0.1, v0 + (v1 - v0) * 0.2, v1 - (v1 - v0) * 0.15, 0.1, 0.18, 0.04, Armour);
                    break;
                case 2:
                    Slab(p, r, k, s, u0 + 0.02, u1 - 0.06, v0 + 0.04, v1 - 0.1, 0.02, 0.06, 0.03, (i + salt) % 2 == 0 ? Patch : Patch2, "rusty paint", paint: true);
                    FStrip(p, r, k, s, u0 + 0.1, u0 + 0.16, v0, v0 + (v1 - v0) * 0.4, 0.025, Rust, "rust");
                    break;
                case 4:
                    Slab(p, r, k, s, u0, (u0 + u1) / 2 - 0.01, v0, v1, 0.02, 0.14, 0.07, Armour);
                    Slab(p, r, k, s, (u0 + u1) / 2 + 0.01, u1, v0 + (v1 - v0) * 0.25, v1, 0.02, 0.09, 0.05, Armour2);
                    break;
                case 5:
                    Slab(p, r, k, s, u0, u1, v0, (v0 + v1) / 2 - 0.02, 0.02, 0.11, 0.05, Armour2);
                    FStrip(p, r, k, s, u0 + (u1 - u0) * 0.6, u0 + (u1 - u0) * 0.75, (v0 + v1) / 2 + 0.05, v1 - 0.05, 0.022, Primer, "worn paint");
                    break;
                default:
                    FStrip(p, r, k, s, (u0 + u1) / 2 - 0.004, (u0 + u1) / 2 + 0.004, v0, v1, 0.02, Frame, "dirty steel");
                    FStrip(p, r, k, s, u0 + 0.05, u0 + 0.09, v0, v0 + (v1 - v0) * 0.55, 0.021, Rust, "rust");
                    break;
            }
        }
    }

    /// <summary>A hoop frame's rib round ring r: a dark girder standing 0.1 m proud of the skin all the way round (belly, sides,
    /// spine), drawn only; its hidden part is the slices of the walls at the frame (the splitter gives them to the frame).</summary>
    private static void Rib(Part p, int r)
    {
        const double Proud = 0.11;
        double[] Normal2(double[] a, double[] b, int s)
        {
            var d = U2(b[0] - a[0], b[1] - a[1]);
            double[] n = [d[1], -d[0]];
            var c = new[] { (a[0] + b[0]) / 2 - Cx, (a[1] + b[1]) / 2 - 3.0 };
            return n[0] * c[0] + n[1] * c[1] < 0 ? [-n[0], -n[1]] : n;
        }
        // Neighbouring segments overlap at their corners, so every other one is 8 mm thinner (its fore and aft faces stay inside its neighbour).
        void Seg(double[] a, double[] b, int s, bool thick)
        {
            var Half = thick ? 0.1 : 0.096;
            var n = Normal2(a, b, s);
            // (each end runs 15 mm past the ring's point, so a rib's ends never share a plane with the facets' edges there)
            var d = U2(b[0] - a[0], b[1] - a[1]);
            a = [a[0] - d[0] * 0.015, a[1] - d[1] * 0.015, a[2]];
            b = [b[0] + d[0] * 0.015, b[1] + d[1] * 0.015, b[2]];
            double[][] Ring(double dz) => [P(a[0], a[1], a[2] + dz), P(b[0], b[1], b[2] + dz), P(b[0] + n[0] * Proud, b[1] + n[1] * Proud, b[2] + dz), P(a[0] + n[0] * Proud, a[1] + n[1] * Proud, a[2] + dz)];
            Prism(p, Ring(-Half), Ring(Half), Frame, "dirty steel");
        }
        double[] V(int k, int s) => [Cx + s * Rings[r][k].W, Rings[r][k].Y, Rings[r][k].Z];
        foreach (var s in new[] { -1, 1 })
            for (var k = 0; k < 4; k++)
                Seg(V(k, s), V(k + 1, s), s, k % 2 == 1);
        Seg(V(B, -1), V(B, 1), 1, true);
        Seg(V(R, 1), V(R, -1), 1, false);
    }

    /// <summary>A box run along z split at the frames (the frame's rib covers the joint), so each length goes to its own hull piece.</summary>
    private static void RunZ(Part p, double x0, double y0, double z0, double x1, double y1, double z1, string color, string finish)
    {
        var cuts = new List<double> { z0 };
        foreach (var f in FrameZ)
            if (f > z0 + 0.2 && f < z1 - 0.2)
            {
                cuts.Add(f - 0.04);
                cuts.Add(f + 0.04);
            }
        cuts.Add(z1);
        for (var i = 0; i + 1 < cuts.Count; i += 2)
            Detail(p, x0, y0, cuts[i], x1, y1, cuts[i + 1], color, finish);
    }

    /// <summary>A partition from (x0, z0) to (x1, z1) (one of them equal: a run along an axis, 0.25 m thick about the line), from y0 to
    /// y1, with doorways (along the run: from, to, height over y0) left open. Its own piece.</summary>
    private static void Partition(Part p, double x0, double z0, double x1, double z1, double y0, double y1, string color, string finish, params (double A, double B, double Hgt)[] doors)
    {
        var alongX = Math.Abs(z1 - z0) < 1e-6;
        var a0 = alongX ? Math.Min(x0, x1) : Math.Min(z0, z1);
        var a1 = alongX ? Math.Max(x0, x1) : Math.Max(z0, z1);
        var c = alongX ? z0 : x0;
        var was = Tag;
        Tag = $"Roamer Partition {++PartitionCount}";
        void Run(double a, double b, double ya, double yb)
        {
            if (b - a < 0.02)
                return;
            if (alongX)
                Solid(p, a, ya, c - 0.125, b, yb, c + 0.125, color, finish);
            else
                Solid(p, c - 0.125, ya, a, c + 0.125, yb, b, color, finish);
        }
        var at = a0;
        foreach (var (da, db, hgt) in doors.OrderBy(d => d.A))
        {
            Run(at, da, y0, y1);
            Run(da, db, y0 + hgt, y1); // the lintel
            at = db;
        }
        Run(at, a1, y0, y1);
        // A dark kick strip along the bottom of each face (the low paint), drawn only, 4 mm proud so it never shares the floor's plane.
        foreach (var s in new[] { -1, 1 })
        {
            var face = c + s * 0.13;
            at = a0;
            foreach (var (da, db, _) in doors.OrderBy(d => d.A).Append((a1, a1, 0)))
            {
                if (da - at > 0.05)
                {
                    if (alongX)
                        Detail(p, at, y0 + 0.004, Math.Min(face, face + s * 0.012), da, y0 + 0.9, Math.Max(face, face + s * 0.012), WallLow, "matte paint");
                    else
                        Detail(p, Math.Min(face, face + s * 0.012), y0 + 0.004, at, Math.Max(face, face + s * 0.012), y0 + 0.9, da, WallLow, "matte paint");
                }
                at = db;
            }
        }
        Tag = was;
        LastPartition = $"Roamer Partition {PartitionCount}";
    }

    private static int PartitionCount;
    private static string LastPartition = "";

    /// <summary>What closes the gap between a partition across the hull at z (stopping at the room's wall) and the skin, over y0..y1:
    /// a wedge each side, drawn only, with the last partition.</summary>
    private static void Fillers(Part p, int level, double z, double y0, double y1, string color, string finish, bool port = true, bool starboard = true)
    {
        var inner = RoomHalf(level, z - 0.12, z + 0.12);
        With(LastPartition, () =>
        {
            foreach (var s in new[] { -1, 1 })
            {
                if (s < 0 && !port || s > 0 && !starboard)
                    continue;
                double OutAt(double y) => Math.Max(inner, SkinHalf(z, y) - 0.03);
                double[][] Tri(double zz) => [P(Cx + s * inner, y0, zz), P(Cx + s * OutAt(y0), y0, zz), P(Cx + s * OutAt(y1), y1, zz), P(Cx + s * inner, y1, zz)];
                Prism(p, Tri(z - 0.12), Tri(z + 0.12), color, finish);
            }
        });
    }

    private static Part Big(string name, string desc) => new(name, Small, "Roamer", desc, role: "frame", tool: "welder", material: "steel", color: HullPaint, group: Group);

    // ---- the keel: the lower deck, the belly, the lower hull, the chin's cheeks, the tail's lower half

    private static Part Keel()
    {
        var p = Big("Roamer Keel", "The Roamer's lower deck: the hold's and the engine room's floor, the belly and the legs' mounts.");
        // The belly (flat between the belly edges), and the lower facets a side: the bilge (belly edge to chine) and the flared side
        // (chine to deck line), from the nose ring back to the tail.
        for (var r = 1; r < Rings.Length - 1; r++)
            With(r == 5 ? "Roamer Tail" : null, () => End2(p, r, B, Belly, "dirty steel"));
        // (ring 5, the tail's, is drawn below under the tail's own tag: these loops stop at ring 4, or its facets came twice)
        With(HullTag, () =>
        {
            for (var r = 1; r < Rings.Length - 2; r++)
                foreach (var s in new[] { -1, 1 })
                {
                    Facet(p, r, B, s, Belly, "dirty steel", paint: false);
                    var c = r == 1 ? Patch : r == 5 ? RoofDark : (r + (s > 0 ? 1 : 0)) % 3 == 0 ? Patch2 : HullPaint;
                    Facet(p, r, L, s, c, "rusty paint", paint: c == HullPaint);
                }
            // The chine and the beam's rails, the beam's the heavier (the rub strake).
            for (var r = 1; r < Rings.Length - 2; r++)
                foreach (var s in new[] { -1, 1 })
                {
                    Chine(p, r, L, s, 0.16, 0.1, Armour);
                    Chine(p, r, D, s, 0.24, 0.17, Frame);
                }
            // Armour and patches on the flared sides, below and above the fuel lines; the bilge gets a few slabs.
            foreach (var s in new[] { -1, 1 })
                for (var r = 2; r <= 4; r++)
                {
                    var door = r == 2 && s < 0; // (no panels over the entry door)
                    Plates(p, r, L, s, r * 3 + 1, 0.06, 0.44, door ? 0.3 : -1);
                    Plates(p, r, L, s, r * 3 + 2, 0.6, 0.94, door ? 0.3 : -1);
                    Plates(p, r, B, s, r + 5, 0.15, 0.85);
                }
            foreach (var s in new[] { -1, 1 })
            {
                Plates(p, 1, L, s, 11, 0.3, 0.9);
                Plates(p, 5, L, s, 12, 0.1, 0.9);
                // The fuel lines: two pipes from the saddle tank aft to the pod's pylon, in the gap between the rows of plates,
                // on brackets.
                foreach (var (v, hy) in new[] { (0.47, 0.08), (0.56, 0.07) })
                {
                    var y = 3.0 * v;
                    var x = LowSide(y) + 0.03;
                    RunZ(p, Cx + s * x, y - hy / 2, 6.9, Cx + s * (x + hy), y + hy / 2, 12.25, v < 0.5 ? "#6b6a60" : Primer, "dirty steel");
                }
                foreach (var z in new[] { 7.5, 9.6, 11.6 })
                    Detail(p, Cx + s * (LowSide(1.3) + 0.004), 1.3, z - 0.05, Cx + s * (LowSide(1.82) + 0.22), 1.82, z + 0.05, Frame, "dirty steel");
            }
            // The entry door on the port bow quarter, a camper's (sealed long ago: the ramp is the way in): a frame, the door in
            // cream with a brown stripe, a little window high up, a chrome grab handle, a fold-down step under it.
            Slab(p, 2, L, -1, 0.09, 0.27, 0.2, 0.9, 0.02, 0.06, 0.02, Frame, "dirty steel");
            Slab(p, 2, L, -1, 0.1, 0.26, 0.23, 0.87, 0.06, 0.09, 0.015, HullPaint, "worn paint", paint: true);
            FStrip(p, 2, L, -1, 0.1, 0.26, 0.47, 0.53, 0.095, Brown, "worn paint");
            FStrip(p, 2, L, -1, 0.13, 0.23, 0.68, 0.83, 0.096, "#3a4a58", "plastic");
            FStrip(p, 2, L, -1, 0.125, 0.235, 0.675, 0.69, 0.1, Chrome, "chrome");
            Slab(p, 2, L, -1, 0.215, 0.235, 0.5, 0.6, 0.09, 0.15, 0.008, Chrome, "chrome");
            Slab(p, 2, L, -1, 0.11, 0.25, 0.12, 0.19, 0.02, 0.32, 0.02, "#5a5650", "tread plate");
        });
        // The tail's lower half: its facets, and the end plate's lower bands with the heat exchanger's slats across it.
        With("Roamer Tail", () =>
        {
            foreach (var s in new[] { -1, 1 })
            {
                Facet(p, 5, B, s, Belly, "dirty steel", paint: false);
                Facet(p, 5, L, s, RoofDark, "rusty paint", paint: false);
                Chine(p, 5, L, s, 0.16, 0.1, Armour);
                Chine(p, 5, D, s, 0.24, 0.17, Frame);
            }
            End(p, 6, B, Belly, "dirty steel", paint: false);
            End(p, 6, L, RoofDark, "soot", paint: false);
            for (var y = 0.8; y < 2.75; y += 0.18)
            {
                var t = (y - 0.5) / 2.5;
                var zf = Rings[6][L].Z + (Rings[6][D].Z - Rings[6][L].Z) * t; // (the end plate leans from the chine to the beam)
                var w = Rings[6][L].W + (Rings[6][D].W - Rings[6][L].W) * t - 0.55;
                Detail(p, Cx - w, y, zf + 0.03, Cx + w, y + 0.07, zf + 0.1, "#1e1d1c", "dirty steel");
            }
            // Tail lights: a camper's tall red lamps at the corners, an amber one over each, in chrome bezels.
            foreach (var s in new[] { -1, 1 })
            {
                double zt = Rings[6][D].Z + 0.012, xo = Rings[6][D].W - 0.12;
                Detail(p, Cx + s * (xo - 0.3), 1.5, zt - 0.02, Cx + s * xo, 2.95, zt + 0.06, Chrome, "chrome");
                Detail(p, Cx + s * (xo - 0.27), 1.53, zt + 0.06, Cx + s * (xo - 0.03), 2.45, zt + 0.08, "#d82a1c", glow: true);
                Detail(p, Cx + s * (xo - 0.27), 2.5, zt + 0.06, Cx + s * (xo - 0.03), 2.92, zt + 0.08, "#f0a020", glow: true);
            }
        });
        // The chin's cheeks either side of the ramp (the ramp is the rest of the chin: Rust Bucket Ramp).
        With("Roamer Chin", () =>
        {
            foreach (var s in new[] { -1, 1 })
            {
                // (the nose is wide and the ramp narrow: the cheek is the chin's plane from the ramp's edge out to the corner)
                var lo = Pt(1, L, s);
                var hi = Pt(1, D, s);
                double[] loIn = [Cx - H + s * 1.9, lo[1], lo[2]], hiIn = [Cx - H + s * 1.9, hi[1], hi[2]];
                Quad(p, loIn, lo, hi, hiIn, HullPaint, "rusty paint", paint: true);
                // Its collision: level slices behind the slope, fore of the hold's own boxes.
                for (var y = F0; y < 3.0 - 0.01; y += 0.45)
                {
                    var y1 = Math.Min(3.0, y + 0.45);
                    double zf = ChinZ(y1) + 0.03, zb = Math.Min(LowFront - 0.005, ChinZ(y) + 0.2);
                    if (zb - zf < 0.05)
                        continue;
                    var w = Cm(Math.Min(SkinHalf(zf, y), SkinHalf(zf, y1)) - 0.05);
                    if (w > 2.0)
                        Hidden(p, Cx + s * 1.92, y, zf, Cx + s * w, y1, zb);
                }
                // The cheek's stripes, the body's three running down the corner to the ramp's edge, and a round lamp low down.
                var n = Unit(Cross(Sub(hi, lo), Sub(loIn, lo)));
                if (n[2] > 0)
                    n = [-n[0], -n[1], -n[2]];
                double[] C(double t, double v, double d) => Add(Lerp(Lerp(loIn, lo, t), Lerp(hiIn, hi, t), v), n, d);
                foreach (var (v0, v1, c, d) in new[] { (0.72, 0.77, Brown, 0.012), (0.78, 0.83, Orange, 0.016), (0.84, 0.89, Mustard, 0.012) })
                    Plate(p, [C(0.02, v0, d), C(0.98, v0, d), C(0.98, v1, d), C(0.02, v1, d)], c, "worn paint");
                Plate(p, [C(0.7, 0.3, 0.01), C(0.9, 0.3, 0.01), C(0.9, 0.42, 0.01), C(0.7, 0.42, 0.01)], Chrome, "chrome");
                Plate(p, [C(0.72, 0.31, 0.025), C(0.88, 0.31, 0.025), C(0.88, 0.41, 0.025), C(0.72, 0.41, 0.025)], "#fff2c8", glow: true);
                Plate(p, [C(0.006, 0.01, 0.02), C(0.046, 0.01, 0.02), C(0.046, 0.99, 0.02), C(0.006, 0.99, 0.02)], Chrome, "chrome"); // (the ramp's edge)
            }
        });
        // The floor; the hidden walls round the hold and the engine room; the engine room's bulkhead; the belly void (the legs' mounts).
        FloorOf(p, 0, 0.0, F0, Floor, []);
        HullWalls(p, 0);
        Partition(p, Cx - Lw, 12.0, Cx + Lw, 12.0, F0, F1, Wall, "painted plating", (4.25, 5.75, 2.25));
        foreach (var (a, b) in Strips(Rings[1][B].Z, Back))
        {
            var y0 = Math.Max(PointAt(B, a).Y, PointAt(B, b).Y);
            var w = Cm(Math.Min(SkinHalf(a, y0 + 0.001), SkinHalf(b, y0 + 0.001)) - 0.06);
            if (w > 0.3 && y0 < -0.05)
                Hidden(p, Cx - w, y0, a, Cx + w, 0.0, b);
        }
        // Under the upper deck: frames across the hold's ceiling, every 2 m (drawn only, a little under the deck), and a cable tray.
        With("Roamer Deck", () =>
        {
            foreach (var z in new[] { 4.0, 6.0, 8.0, 10.0 })
                Detail(p, Cx - Lw + 0.004, F1 - 0.22, z - 0.08, z is 8.0 or 10.0 ? StairHole.X0 - 0.1 : Cx + Lw - 0.004, F1 - 0.012, z + 0.08, Frame, "dirty steel");
            Detail(p, 1.45, F1 - 0.3, 3.4, 1.85, F1 - 0.24, 11.8, "#6b6a60", "dirty steel");
        });
        // Sockets: the ramp's hinge line, the stair's foot, the engine, the pods and saddle tanks (on the skin), the legs, the fittings.
        Sock(p, "ramp", Cx, 0.1, RampHingeZ, "-z");
        Hidden(p, Cx - 0.1, -0.1, RampHingeZ, Cx + 0.1, F0, LowFront); // (what the ramp's socket is on)
        Sock(p, "stair", StairHole.X0 + 0.175, F0, StairHole.Z0 - StairLead + 0.125, "+y"); // (its head, StairRun on, at the opening's aft end)
        Sock(p, "engine", Cx, F0, 14.2, "+y");
        foreach (var (z, i) in new[] { (4.5, 0), (7.5, 1), (10.5, 2) })
            Sock(p, $"shelfL{i}", Cx - Lw, F0 + 0.125, z, "+x");
        Sock(p, "rackR", Cx + Lw, F0 + 0.9, 5.0, "-x");
        Sock(p, "lockerR", Cx + Lw, F0 + 0.125, 6.05, "-x");
        // The spacewalk gear by the ramp, the way out: the MMU Dock and a Space Suit Locker to starboard, a second locker to port.
        Sock(p, "mmuDock", Cx + Lw, F0 + 0.125, 2.75, "-x");
        Sock(p, "suitR", Cx + Lw, F0 + 0.125, 3.8, "-x");
        Sock(p, "suitL", Cx - Lw, F0 + 0.125, 2.8, "+x");
        Sock(p, "lifeSupport", Cx - Lw, F0 + 0.125, 5.97, "+x"); // (the ship's air: the unit and a tank on the port wall, in the gaps between the shelves)
        Sock(p, "o2Tank0", Cx - Lw, F0 + 0.125, 8.93, "+x");
        Sock(p, "shellDock", Cx + Lw, F0 + 0.125, 7.0, "-x"); // (the one Robot Shell Dock, aft of the locker: Karl, 2026-11-17)
        Sock(p, "crate0", 2.4, F0, 10.6, "+y");
        Sock(p, "crate1", 3.2, F0, 10.6, "+y");
        Sock(p, "crate2", StairHole.X0 + 0.6, F0, 10.25, "+y"); // (under the open stair's high end: the junk that lives there)
        Sock(p, "crate3", StairHole.X0 + 1.2, F0, 10.5, "+y");
        Sock(p, "panelEng", 1.9, F0 + 0.6, 12.125, "+z");
        Sock(p, "boardEng", 8.0, F0 + 0.9, 12.125, "+z");
        Sock(p, "pipesEngL", 3.1, F0 + 2.6, 12.125, "+z");
        Sock(p, "battery", 6.85, F0 + 0.125, 12.125, "+z"); // (its Ship Battery S stands on the floor against this wall: Karl, 2026-11-18)
        Sock(p, "switchHoldL", Cx - Lw, F0 + 1.3, 7.2, "+x");
        Sock(p, "switchHoldR", Cx + Lw, F0 + 1.3, 7.55, "-x"); // (aft of the Robot Shell Dock)
        Sock(p, "switchEng", 6.3, F0 + 1.3, 12.125, "+z");
        foreach (var (z, i) in new[] { (3.6, 0), (7.6, 1) })
        {
            Sock(p, $"lampHoldL{i}", Cx - Lw, 2.5, z + 1.4, "+x");
            Sock(p, $"lampHoldR{i}", Cx + Lw, 2.5, z + 0.4, "-x");
        }
        Sock(p, "lampEngL", Cx - Lw, 2.5, 13.2, "+x");
        Sock(p, "lampEngR", Cx + Lw, 2.5, 13.2, "-x");
        // (each on a small hidden mount just inside the skin, outside the hull run's wall: the pod and the tank hang from the keel)
        foreach (var s in new[] { -1, 1 })
        {
            Sock(p, s < 0 ? "podL" : "podR", Cx + s * PodInner, PodY, PodZ, s < 0 ? "-x" : "+x");
            Hidden(p, Cx + s * Cm(SkinMin(PodZ - 0.1, PodZ + 0.1, PodY - 0.1, PodY + 0.1) - 0.05), PodY - 0.1, PodZ - 0.1, Cx + s * PodInner, PodY + 0.1, PodZ + 0.1);
            Sock(p, s < 0 ? "saddleL" : "saddleR", Cx + s * SadInner, SadY, SadZ, s < 0 ? "-x" : "+x");
            Hidden(p, Cx + s * Cm(SkinMin(SadZ - 0.1, SadZ + 0.1, SadY - 0.1, SadY + 0.1) - 0.05), SadY - 0.1, SadZ - 0.1, Cx + s * SadInner, SadY + 0.1, SadZ + 0.1);
        }
        foreach (var (x, z, n) in new[] { (Cx - 2.4, 4.6, "legFL"), (Cx + 2.4, 4.6, "legFR"), (Cx - 2.4, 12.8, "legBL"), (Cx + 2.4, 12.8, "legBR") })
            Sock(p, n, x, -0.6, z, "-y");
        return p;
    }

    /// <summary>An end's band between points k and k+1 of ring r, across the middle.</summary>
    private static void End(Part p, int r, int k, string color, string finish = "rusty paint", bool glass = false, bool paint = true) =>
        Quad(p, Up(Pt(r, k, -1), 0.003), Up(Pt(r, k, 1), 0.003), Up(Pt(r, k + 1, 1), -0.003), Up(Pt(r, k + 1, -1), -0.003), color, finish, glass, paint);

    /// <summary>The flat across the middle between points k of rings r and r+1 (the belly, the spine).</summary>
    private static void End2(Part p, int r, int k, string color, string finish, bool glass = false) =>
        Quad(p, Pt(r, k, -1), Pt(r, k, 1), Pt(r + 1, k, 1), Pt(r + 1, k, -1), color, finish, glass);

    private static double[] Up(double[] q, double dy) => [q[0], q[1] + dy, q[2]];

    // ---- the ramp: the chin, an event door that swings down to the ground

    private const double RampHingeZ = -0.4; // where the chin's plane crosses the lower floor's top

    private static Part Ramp()
    {
        // Shut it is the chin: a plate from the hinge (the lower floor's top, z RampHingeZ) up the chin's plane to the deck line,
        // between the cheeks. It turns a quarter about x through the hinge, forward and down, to lie 22 degrees below level with
        // its tip on the ground. Its hidden boxes are level treads in the open pose, turned back to the shut one.
        var p = new Part("Roamer Ramp", Small, "Roamer", "The Roamer's chin: F lowers it as a ramp to the ground and raises it again.", role: "door", tool: "welder", material: "steel", color: Patch, group: Group);
        double x0 = Cx - 1.88, x1 = Cx + 1.88, hy = F0, hz = RampHingeZ;
        var ln = Math.Sqrt(3.0 * 3.0 + 1.2 * 1.2);
        double uy = 3.0 / ln, uz = -1.2 / ln; // up the plate, shut
        double ny = uz, nz = -uy; // its outer face's normal, shut (forward and down)
        var len = (3.0 - F0) / uy;
        double[] Shut(double x, double a, double o) => P(x, hy + a * uy + o * ny, hz + a * uz + o * nz);
        void Strip(double a0, double a1, double o, double xa, double xb, string c, string f) =>
            Plate(p, [Shut(xa, a0, o), Shut(xb, a0, o), Shut(xb, a1, o), Shut(xa, a1, o)], c, f, paint: c == Patch);
        Strip(0, len, -0.02, x0, x1, Patch, "rusty paint");
        foreach (var a in new[] { 0.7, 1.5, 2.3 })
            Strip(a - 0.05, a + 0.05, -0.07, x0 + 0.1, x1 - 0.1, Frame, "dirty steel"); // (ribs inside)
        Strip(0.03, 0.3, 0.015, x0 + 0.05, x1 - 0.05, Stripe, "hazard paint"); // (the foot's edge, outside)
        // Treads, open: step i runs from i/n to (i+1)/n of the way out, its top level with the slope at its hinge end. Open is
        // (y, z) -> (z, -y) about the hinge from shut, so shut is (dy, dz) -> (-dz, dy).
        const int N = 10;
        for (var i = 0; i < N; i++)
        {
            double a0 = len * i / N, a1 = len * (i + 1) / N;
            // Open pose (relative to the hinge): forward is -z, the slope's direction is n's turn of u, i.e. (ny, nz).
            double zA = a0 * nz, zB = a1 * nz, top = a0 * ny, bot = a1 * ny - 0.08;
            double oz0 = Math.Min(zA, zB), oz1 = Math.Max(zA, zB), oy0 = bot, oy1 = top;
            // Turn back: (dy, dz) -> (-dz, dy).
            double sy0 = -oz1, sy1 = -oz0, sz0 = oy0, sz1 = oy1;
            Hidden(p, x0, hy + sy0, hz + sz0, x1, hy + sy1, hz + sz1);
        }
        Hidden(p, Cx - 0.1, 0.0, hz - 0.1, Cx + 0.1, 0.2, hz);
        p.Sock("hang", P(Cx, 0.1, hz), "+z");
        p.Set(("door", Kw(("kind", "swing"), ("axis", "-x"), ("pivot", Py.L(R(Cx - H), R(hy - H), R(hz - H))), ("angle", 90), ("seconds", 4.0), ("hang", "hang"))));
        return p.Stats(1900, 3000, 30, 8.0);
    }

    // ---- the deck: the upper deck, the canopy and the bridge front, the upper hull, the tail's upper half

    // The stair hall, inside the rooms' starboard wall: the opening runs from just aft of the bathroom's wall to the Wide Stair's head,
    // and the floor is whole from there to the galley's wall, a landing level with the deck to step off onto (Karl, 2026-11-17: the
    // flight ran on to the far wall past where the floor was open beside it). The stair's foot is StairLead fore of the opening, under
    // the deck, low enough there that a standing head clears the opening's edge.
    private static readonly (double X0, double Z0, double X1, double Z1) StairHole = (6.55, 8.15, Cx + Uw, 11.0);
    private const double StairLead = 0.05; // (on this steep flight a standing head that steps up near the opening's fore edge meets it: the foot is almost under it)
    // The Roamer Stair is steeper than the Wide Stair (Karl, 2026-11-20: its head where the hall's rail ends, the deck whole beyond):
    // 10 steps rise 3 m over 2.9 m, 8.15 - 0.05 + 2.9 = 11.0, and the landing runs 1 m to the galley's wall.
    private const double StairRun = 2.9;
    private const double StairRailEnd = 11.0;

    /// <summary>A frame bar from a to b: a thin plate 8 cm wide, moved by <paramref name="shift"/> to stand proud of the glass.</summary>
    private static void Bar(Part p, double[] a, double[] b, double[] shift)
    {
        a = Add(a, shift);
        b = Add(b, shift);
        var d = Unit(Sub(b, a));
        double[] up = Math.Abs(d[1]) < 0.9 ? [0, 1, 0] : [1, 0, 0];
        var side = Unit(Cross(d, up));
        Plate(p, [Add(a, side, -0.04), Add(a, side, 0.04), Add(b, side, 0.04), Add(b, side, -0.04)], Frame, "dirty steel");
    }

    private static Part DeckPart()
    {
        var p = Big("Roamer Deck", "The Roamer's upper deck: the floor of the cockpit, the cabins, hydroponics and the galley.");
        // The nose: the bridge front over the ramp, its underside, its plated sides, a windscreen raked over the cockpit, the
        // side windows, the bridge's windows under a visor, a bumper and two searchlights.
        With("Roamer Canopy", () =>
        {
            Quad(p, Pt(0, L, -1), Pt(0, L, 1), Pt(1, D, 1), Pt(1, D, -1), Belly, "dirty steel");
            const double Glass = 0.12; // (the cab's glass roof starts this far back along the brow's sides)
            foreach (var s in new[] { -1, 1 })
            {
                Plate(p, [Pt(0, L, s), Pt(0, D, s), Pt(1, D, s)], HullPaint, "rusty paint", paint: true);
                // The wraparound: the windscreen runs round the corners into the cheeks, glass from just over the bumper line.
                const double Cv0 = 0.1;
                Facet(p, 0, D, s, GlassTint, glass: true, paint: false);
                FStrip(p, 0, D, s, 0.01, 0.99, 0.01, Cv0, 0.012, HullPaint, "rusty paint");
                var nc = FacetNormal(0, D, s);
                Bar(p, OnFacet(0, D, s, 0.55, Cv0), OnFacet(0, D, s, 0.55, 0.97), [nc[0] * 0.035, nc[1] * 0.035, nc[2] * 0.035]);
                Bar(p, OnFacet(0, D, s, 0.03, Cv0 + 0.01), OnFacet(0, D, s, 0.97, Cv0 + 0.01), [nc[0] * 0.04, nc[1] * 0.04, nc[2] * 0.04]);
                Facet(p, 0, U, s, HullPaint, u1: Glass);
                Facet(p, 0, U, s, GlassTint, glass: true, paint: false, u0: Glass);
                // The cockpit's side windows: big, from the stripes nearly to the eaves, one post in the middle.
                const double Wu0 = 0.08, Wu1 = 0.93, Wv0 = 0.32, Wv1 = 0.92;
                void Pane(double u0, double u1, double v0, double v1, bool glass) =>
                    Plate(p, [OnFacet(1, D, s, u0, v0), OnFacet(1, D, s, u0, v1), OnFacet(1, D, s, u1, v1), OnFacet(1, D, s, u1, v0)], glass ? GlassTint : HullPaint, "rusty paint", glass: glass, paint: !glass);
                Pane(Wu0, Wu1, Wv0 + 0.001, Wv1 - 0.001, true);
                Pane(0.0015, 0.9985, 0, Wv0, false);
                Pane(0.0015, 0.9985, Wv1, 1, false);
                Pane(0.0015, Wu0, Wv0 + 0.001, Wv1 - 0.001, false);
                Pane(Wu1, 0.9985, Wv0 + 0.001, Wv1 - 0.001, false);
                Stripes(p, 1, s, 0, 1, 0, 0);
                Chine(p, 0, D, s, 0.2, 0.14, Chrome);
                Chine(p, 0, U, s, 0.12, 0.08, HullPaint);
                Chine(p, 0, R, s, 0.1, 0.07, HullPaint);
                Chine(p, 1, U, s, 0.14, 0.09, Chrome);
                var n1 = FacetNormal(1, D, s);
                Bar(p, OnFacet(1, D, s, 0.5, Wv0), OnFacet(1, D, s, 0.5, Wv1), [n1[0] * 0.025, n1[1] * 0.025, n1[2] * 0.025]);
                Bar(p, OnFacet(1, D, s, Wu0, Wv0 - 0.01), OnFacet(1, D, s, Wu1, Wv0 - 0.01), [n1[0] * 0.03, n1[1] * 0.03, n1[2] * 0.03]);
                Bar(p, OnFacet(1, D, s, Wu0, Wv1 + 0.01), OnFacet(1, D, s, Wu1, Wv1 + 0.01), [n1[0] * 0.03, n1[1] * 0.03, n1[2] * 0.03]);
                var n0 = FacetNormal(0, U, s);
                Bar(p, OnFacet(0, U, s, Glass, 0.03), OnFacet(0, U, s, Glass, 0.97), [n0[0] * 0.025, n0[1] * 0.025, n0[2] * 0.025]);
                Bar(p, OnFacet(0, U, s, 0.6, 0.03), OnFacet(0, U, s, 0.6, 0.97), [n0[0] * 0.025, n0[1] * 0.025, n0[2] * 0.025]);
                // A chrome mirror on a stalk off each front corner.
                var mc = Pt(1, D, s);
                Detail(p, mc[0] + H + s * 0.02, mc[1] + H + 0.6, mc[2] + H - 0.04, mc[0] + H + s * 0.42, mc[1] + H + 0.64, mc[2] + H, Chrome, "chrome");
                Detail(p, mc[0] + H + s * 0.36, mc[1] + H + 0.45, mc[2] + H - 0.06, mc[0] + H + s * 0.46, mc[1] + H + 0.95, mc[2] + H + 0.02, Frame, "plastic");
            }
            // The spine across the cab: plated forward, a glass roof aft of the brow, a bar up the middle.
            double[] SpineAt(int side, double u) => Lerp(Pt(0, R, side), Pt(1, R, side), u);
            Quad(p, Pt(0, R, -1), Pt(0, R, 1), SpineAt(1, Glass), SpineAt(-1, Glass), HullPaint, paint: true);
            Quad(p, SpineAt(-1, Glass), SpineAt(1, Glass), Pt(1, R, 1), Pt(1, R, -1), GlassTint, glass: true);
            Bar(p, Mid(SpineAt(-1, Glass), SpineAt(1, Glass)), Mid(Pt(1, R, -1), Pt(1, R, 1)), [0, 0.025, 0]);
            // The front: a cream nose panel with the three stripes across it and four round lamps either side of a grille, a chrome
            // bumper along the bumper line, the windscreen over it with no post in front of the pilot, and the cab-over brow above
            // with its little window.
            double[] F(int k, double t) => Lerp(Pt(0, k, -1), Pt(0, k, 1), t);
            double[] Band(int k0, int k1, double t, double v) => Lerp(F(k0, t), F(k1, t), v);
            Quad(p, F(L, 0), F(L, 1), F(D, 1), F(D, 0), HullPaint, paint: true);
            var ln = Unit(Cross(Sub(F(L, 1), F(L, 0)), Sub(F(D, 0), F(L, 0))));
            if (ln[2] > 0)
                ln = [-ln[0], -ln[1], -ln[2]];
            double[] LF(double t, double v, double d) => Add(Band(L, D, t, v), ln, d);
            void LPlate(double t0, double t1, double v0, double v1, double d, string c, string f, bool glow = false) =>
                Plate(p, [LF(t0, v0, d), LF(t1, v0, d), LF(t1, v1, d), LF(t0, v1, d)], c, f, glow: glow);
            foreach (var (v0, v1, c, d) in new[] { (0.44, 0.54, Brown, 0.012), (0.56, 0.66, Orange, 0.016), (0.68, 0.78, Mustard, 0.012) })
                LPlate(0.01, 0.99, v0, v1, d, c, "worn paint");
            foreach (var t in new[] { 0.1, 0.2, 0.8, 0.9 })
            {
                LPlate(t - 0.045, t + 0.045, 0.06, 0.4, 0.02, Chrome, "chrome");
                LPlate(t - 0.035, t + 0.035, 0.1, 0.36, 0.035, "#fff2c8", "", glow: true);
            }
            LPlate(0.3, 0.7, 0.06, 0.4, 0.02, Frame, "dirty steel");
            for (var v = 0.1; v < 0.37; v += 0.075)
                LPlate(0.31, 0.69, v, v + 0.035, 0.03, Chrome, "chrome");
            const double Fv0 = 0.04, Fv1 = 0.96, Ft0 = 0.015, Ft1 = 0.985;
            Quad(p, Band(D, U, 0, 0), Band(D, U, 1, 0), Band(D, U, 1, Fv0), Band(D, U, 0, Fv0), HullPaint, paint: true);
            Quad(p, Band(D, U, 0, Fv1), Band(D, U, 1, Fv1), Band(D, U, 1, 1), Band(D, U, 0, 1), HullPaint, paint: true);
            Quad(p, Band(D, U, 0, Fv0), Band(D, U, Ft0, Fv0), Band(D, U, Ft0, Fv1), Band(D, U, 0, Fv1), HullPaint, paint: true);
            Quad(p, Band(D, U, Ft1, Fv0), Band(D, U, 1, Fv0), Band(D, U, 1, Fv1), Band(D, U, Ft1, Fv1), HullPaint, paint: true);
            Quad(p, Band(D, U, Ft0, Fv0), Band(D, U, Ft1, Fv0), Band(D, U, Ft1, Fv1), Band(D, U, Ft0, Fv1), GlassTint, glass: true);
            var fn = Unit(Cross(Sub(F(D, 1), F(D, 0)), Sub(F(U, 0), F(D, 0))));
            if (fn[2] > 0)
                fn = [-fn[0], -fn[1], -fn[2]];
            Bar(p, Band(D, U, Ft0, Fv0 + 0.005), Band(D, U, Ft1, Fv0 + 0.005), [fn[0] * 0.035, fn[1] * 0.035, fn[2] * 0.035]);
            // Two wipers parked along the screen's foot.
            foreach (var t in new[] { 0.22, 0.62 })
                Bar(p, Band(D, U, t, 0.06), Band(D, U, t + 0.2, 0.1), [fn[0] * 0.05, fn[1] * 0.05, fn[2] * 0.05]);
            // The cab-over brow: cream, a stripe, a dark little window for the bunk behind it, two amber clearance lamps.
            Quad(p, F(U, 0), F(U, 1), F(R, 1), F(R, 0), HullPaint, "rusty paint", paint: true);
            var bn = Unit(Cross(Sub(F(U, 1), F(U, 0)), Sub(F(R, 0), F(U, 0))));
            if (bn[2] > 0)
                bn = [-bn[0], -bn[1], -bn[2]];
            double[] BF(double t, double v, double d) => Add(Lerp(F(U, t), F(R, t), v), bn, d);
            void BPlate(double t0, double t1, double v0, double v1, double d, string c, string f, bool glow = false) =>
                Plate(p, [BF(t0, v0, d), BF(t1, v0, d), BF(t1, v1, d), BF(t0, v1, d)], c, f, glow: glow);
            BPlate(0.02, 0.98, 0.12, 0.22, 0.012, Brown, "worn paint");
            BPlate(0.3, 0.7, 0.36, 0.76, 0.012, Frame, "rubber");
            BPlate(0.31, 0.69, 0.39, 0.73, 0.02, "#3a4a58", "plastic");
            foreach (var t in new[] { 0.12, 0.88 })
                BPlate(t - 0.04, t + 0.04, 0.84, 0.92, 0.02, "#f0a020", "", glow: true);
            // The bumper: a chrome beam across the face at the bumper line, dark rubber ends.
            double[] Out(double[] q, double dy, double dz) => [q[0], q[1] + dy, q[2] + dz];
            double[][] Beam(double dy) => [Out(F(D, -0.03), dy, 0.05), Out(F(D, 1.03), dy, 0.05), Out(F(D, 1.03), dy, -0.22), Out(F(D, -0.03), dy, -0.22)];
            Prism(p, Beam(-0.14), Beam(0.08), Chrome, "chrome");
            foreach (var t in new[] { -0.03, 1.03 })
            {
                var c = Out(F(D, t), 0, 0);
                Detail(p, c[0] + H - 0.1, c[1] + H - 0.15, c[2] + H - 0.23, c[0] + H + 0.1, c[1] + H + 0.09, c[2] + H + 0.04, Frame, "rubber");
            }
            // What a round hits in the nose: the void behind the windscreen, ahead of the dashboard.
            foreach (var (y0, y1) in new[] { (3.08, 3.6), (3.6, 4.2), (4.2, 4.8), (4.8, 5.4) })
                Void(p, Math.Max(FrontZ(y0), FrontZ(y1)) + 0.05, Rings[1][D].Z, y0, y1, 0.2);
            foreach (var (y0, y1) in new[] { (F1T, 3.9), (3.9, 4.6), (4.6, 5.3), (5.3, 5.9) })
                Void(p, Rings[1][D].Z, 0.5, y0, y1, 0.2);
        });
        // The upper sides aft of the cab: cream with the three stripes under a band of big framed windows that look into the rooms
        // through deep reveals, a row of panels over them, the drip rail along the eaves, an awning rolled up over the entry door.
        With(HullTag, () =>
        {
            for (var r = 2; r <= 4; r++)
                foreach (var s in new[] { -1, 1 })
                {
                    var holes = Windows.Where(w => w.Side == s && w.Z0 >= Rings[r][D].Z && w.Z1 <= Rings[r + 1][D].Z).ToList();
                    FacetHoles(p, r, s, holes);
                    Chine(p, r, U, s, 0.14, 0.09, Chrome);
                    Stripes(p, r, s, 0, 1, 0, 0);
                    Plates(p, r, D, s, r * 2 + 4, 0.785, 0.87);
                    foreach (var w in holes)
                        WindowDress(p, r, s, w.Z0, w.Z1);
                }
            // The awning over the entry door: a long cream housing on three brackets, the striped canvas showing at its lip.
            RunZ(p, Cx - (UpSide(6.0) + 0.02), 6.0, 3.3, Cx - (UpSide(6.0) + 0.2), 6.2, 8.2, HullPaint, "worn paint");
            RunZ(p, Cx - (UpSide(6.0) + 0.12), 5.975, 3.32, Cx - (UpSide(6.0) + 0.21), 6.0, 8.18, Orange, "fabric");
            foreach (var z in new[] { 3.5, 5.75, 8.0 })
                Detail(p, Cx - (UpSide(6.05) + 0.004), 6.03, z - 0.04, Cx - (UpSide(6.05) + 0.05), 6.17, z + 0.04, Chrome, "chrome");
        });
        With("Roamer Tail", () =>
        {
            foreach (var s in new[] { -1, 1 })
            {
                Facet(p, 5, D, s, HullPaint, paint: true);
                Chine(p, 5, U, s, 0.14, 0.09, Chrome);
                Stripes(p, 5, s, 0, 1, 0, 0.34); // (the stripes sweep up round the rear corner)
            }
            // The rear face: cream, a wide dark rear window, the ladder up to the roof on the starboard side.
            double[] RF(double t, double v) => Lerp(Lerp(Pt(6, D, -1), Pt(6, D, 1), t), Lerp(Pt(6, U, -1), Pt(6, U, 1), t), v);
            void RQ(double t0, double t1, double v0, double v1, string c, bool glass = false) =>
                Plate(p, [RF(t0, v0), RF(t1, v0), RF(t1, v1), RF(t0, v1)], c, "rusty paint", glass: glass, paint: !glass);
            const double Rt0 = 0.22, Rt1 = 0.7, Rv0 = 0.4, Rv1 = 0.82;
            RQ(0, 1, 0.003, Rv0, HullPaint);
            RQ(0, 1, Rv1, 0.997, HullPaint);
            RQ(0, Rt0, Rv0 + 0.001, Rv1 - 0.001, HullPaint);
            RQ(Rt1, 1, Rv0 + 0.001, Rv1 - 0.001, HullPaint);
            RQ(Rt0, Rt1, Rv0 + 0.001, Rv1 - 0.001, "#3a4a58", glass: true);
            foreach (var (v0, v1, c, d) in new[] { (0.1, 0.13, Brown, 0.012), (0.14, 0.17, Orange, 0.016), (0.18, 0.21, Mustard, 0.012) })
            {
                double[] RO(double t, double v) => Add(RF(t, v), [0, 0, 1], d);
                Plate(p, [RO(0.02, v0), RO(0.98, v0), RO(0.98, v1), RO(0.02, v1)], c, "worn paint");
            }
            foreach (var x in new[] { 2.35, 2.8 })
                Detail(p, Cx + x, 2.9, Rings[6][D].Z + 0.09, Cx + x + 0.05, 6.95, Rings[6][D].Z + 0.13, Chrome, "chrome");
            for (var y = 3.2; y < 6.9; y += 0.32)
                Detail(p, Cx + 2.4, y, Rings[6][D].Z + 0.095, Cx + 2.8, y + 0.035, Rings[6][D].Z + 0.125, Chrome, "chrome");
            foreach (var y in new[] { 3.1, 6.6 })
                Detail(p, Cx + 2.5, y, RearZ(y) + 0.004, Cx + 2.65, y + 0.06, Rings[6][D].Z + 0.09, Frame, "dirty steel");
        });
        // The floor (the stair's opening left out); the hidden walls round the rooms (the front's is the canopy's).
        FloorOf(p, 1, F1, F1T, Deck, [StairHole]);
        HullWalls(p, 1);
        // The cockpit's dashboard under the windscreen, in front of where anyone stands: a raked desk with dials and screens.
        // (kept low, its top well under a seated pilot's eye, and the yokes short stubs off the sight line: Karl, 2026-11-16)
        Solid(p, 2.9, F1T, 0.55, 7.1, F1T + 0.55, 1.35, Frame, "dirty steel");
        Detail(p, 3.0, F1T + 0.55, 0.75, 7.0, F1T + 0.69, 1.3, "#3a3d3f", "plastic");
        foreach (var (x, c) in new[] { (3.5, "#5fd06a"), (4.6, "#e0a030"), (5.4, "#5fd06a"), (6.5, "#d04030") })
            Detail(p, x - 0.3, F1T + 0.7, 0.85, x + 0.3, F1T + 0.72, 1.2, c, glow: true);
        foreach (var x in new[] { 4.3, 5.7 })
        {
            Detail(p, x - 0.03, F1T + 0.555, 1.305, x + 0.03, F1T + 0.8, 1.34, Frame, "dirty steel"); // (the yokes' columns)
            Detail(p, x - 0.14, F1T + 0.8, 1.29, x + 0.14, F1T + 0.84, 1.35, Frame, "rubber");
        }
        // Partitions: the cockpit's back wall, the corridor's walls, the cabins' ends, the galley's end; doorways 1.5 by 2.25 m. Ship
        // walls are painted plate; the bathroom's gloss white.
        const double Dh = 2.25;
        var cw = RoomHalf(1, 3.875, 4.125); // (the cab is narrower than the rust bucket's: the wall stops at the room's side, the fillers close the rest)
        Partition(p, Cx - cw + 0.01, 4.0, Cx + cw - 0.01, 4.0, F1T, Ceil, Wall, "painted plating", (4.25, 5.75, Dh));
        Fillers(p, 1, 4.0, F1T, Ceil, Wall, "painted plating");
        Partition(p, 4.0, 4.125, 4.0, Back - 0.02, F1T, Ceil, Wall, "painted plating", (5.25, 6.75, Dh), (9.25, 10.75, Dh));
        var corridor = LastPartition;
        Partition(p, 6.0, 4.125, 6.0, 7.875, F1T, Ceil, BathWall, "gloss paint", (5.25, 6.75, Dh));
        Partition(p, 6.0, 12.125, 6.0, Back - 0.02, F1T, Ceil, Wall, "painted plating", (13.25, 14.75, Dh));
        Partition(p, Cx - Uw, 8.0, 3.875, 8.0, F1T, Ceil, Wall, "painted plating");
        Partition(p, 6.125, 8.0, Cx + Uw, 8.0, F1T, Ceil, BathWall, "gloss paint");
        Partition(p, 6.125, 12.0, Cx + Uw, 12.0, F1T, Ceil, Wall, "painted plating");
        // Pipes along the corridor's port wall under the ceiling, on brackets: the ship's plumbing, out in the open.
        With(corridor, () =>
        {
            Detail(p, 4.13, 5.62, 4.3, 4.21, 5.7, Back - 0.16, "#6b6a60", "dirty steel");
            Detail(p, 4.13, 5.76, 4.3, 4.23, 5.86, Back - 0.16, Primer, "dirty steel");
            for (var z = 5.0; z < Back - 0.16; z += 1.5)
                Detail(p, 4.13, 5.58, z - 0.03, 4.27, 5.9, z + 0.03, Frame, "dirty steel");
        });
        // The stair's rail along the opening's inboard side; at its head (aft) the landing, then the hall.
        Solid(p, StairHole.X0 - 0.06, F1T, StairHole.Z0, StairHole.X0, F1T + 1.0, StairRailEnd, Frame, "steel");
        Detail(p, StairHole.X0 - 0.1, F1T + 0.95, StairHole.Z0, StairHole.X0 + 0.04, F1T + 1.05, StairRailEnd, Stripe, "hazard paint");
        // Sockets: the seats, the cabins' furniture, the doors' jambs, the lamps.
        Sock(p, "seatL", 4.0, F1T, 2.45, "+y");
        Sock(p, "seatR", 6.0, F1T, 2.45, "+y");
        Sock(p, "module", 4.0, F1T, 1.6, "+y"); // (on the floor in the pilot's footwell, so the seat's link runs along the floor, not up past the windscreen)
        Sock(p, "chart", 7.4, F1T + 0.475, 3.875, "-z");
        Sock(p, "linkChair", 2.6, F1T, 3.5, "+y"); // (the one Shell Control Chair, port of the cockpit's door, its back to the partition)
        Sock(p, "bed", Cx - Uw, F1T + 0.125, 6.0, "+x");
        Sock(p, "locker", 3.2, F1T + 0.125, 4.125, "+z");
        Sock(p, "desk", 2.9, F1T + 0.125, 7.875, "-z");
        Sock(p, "shower", 7.9, F1T + 0.125, 4.125, "+z");
        Sock(p, "basin", Cx + Uw, F1T + 0.125, 6.1, "-x");
        Sock(p, "toilet", Cx + Uw, F1T + 0.125, 7.3, "-x");
        foreach (var (z, i) in new[] { (9.25, 0), (11.35, 1) })
            Sock(p, $"growL{i}", Cx - Uw, F1T + 0.125, z, "+x");
        Sock(p, "growM0", 3.875, F1T + 0.125, 12.0, "-x");
        Sock(p, "growM1", 3.875, F1T + 0.125, 14.1, "-x");
        Sock(p, "stove", Cx + Uw, F1T + 0.45, 15.2, "-x");
        Sock(p, "table", 7.15, F1T + 0.375, 12.125, "+z");
        Sock(p, "cabinet", Cx + Uw, F1T + 0.85, 13.8, "-x");
        Sock(p, "coffee", 6.125, F1T + 0.125, 15.35, "+x");
        Sock(p, "doorCockpit", 4.25, F1T + 1.125, 4.0, "+x");
        Sock(p, "doorBed", 4.0, F1T + 1.125, 5.25, "+z");
        Sock(p, "doorBath", 6.0, F1T + 1.125, 5.25, "+z");
        Sock(p, "doorHydro", 4.0, F1T + 1.125, 9.25, "+z");
        Sock(p, "doorGalley", 6.0, F1T + 1.125, 13.25, "+z");
        foreach (var (n, x, z, d) in new[]
        {
            ("lampCockpit", 9.3, 3.875, "-z"), ("lampBed", 2.0, 4.125, "+z"), ("lampBath", 8.0, 7.875, "-z"), ("lampHydroA", 3.875, 9.0, "-x"),
            ("lampHydroB", 3.875, 15.0, "-x"), ("lampCorrA", 4.125, 7.0, "+x"), ("lampCorrB", 4.125, 12.5, "+x"), ("lampGalley", 8.0, 12.125, "+z"),
        })
            Sock(p, n, x, F1T + 2.3, z, d);
        Sock(p, "switchFwd", 4.125, F1T + 1.3, 4.6, "+x");
        Sock(p, "switchAft", 5.875, F1T + 1.3, 12.5, "-x");
        return p;
    }

    // ---- the roof: the ceiling, the eaves, the spine, the frames' ribs, and the mounts for what stands on it

    private const double RoofDeck = 7.2, RoofLow = 6.6; // the roof void's top (what the roof gear stands on) and its step

    private static Part Roof()
    {
        var p = Big("Roamer Roof", "The Roamer's roof: the upper deck's ceiling, the eaves and the spine, and the mounts for the gear on it.");
        for (var r = 1; r <= 4; r++)
        {
            foreach (var s in new[] { -1, 1 })
            {
                Facet(p, r, U, s, (r + (s > 0 ? 1 : 0)) % 3 == 0 ? Patch2 : HullPaint, paint: true);
                Chine(p, r, R, s, 0.14, 0.1, HullPaint);
                if (r >= 2)
                    Plates(p, r, U, s, r * 5 + 2, 0.12, 0.88);
            }
            End2(p, r, R, RoofDark, "dirty steel");
        }
        // Skylights: a raised cream frame and a domed glass lid over the bedroom, the hydroponics and the galley.
        foreach (var (x, z) in new[] { (Cx + 1.4, 5.4), (Cx - 1.6, 11.0), (Cx - 1.2, 15.0) })
        {
            Detail(p, x - 0.45, 7.17, z - 0.45, x + 0.45, 7.36, z + 0.45, HullPaint, "worn paint");
            Put(p, Box(P(x - 0.38, 7.36, z - 0.38), P(x + 0.38, 7.44, z + 0.38), GlassTint, detail: true, glass: true));
            Detail(p, x - 0.3, 7.44, z - 0.3, x + 0.3, 7.48, z + 0.3, "#9ab8c8", "plastic");
        }
        With("Roamer Tail", () =>
        {
            foreach (var s in new[] { -1, 1 })
            {
                Facet(p, 5, U, s, HullPaint, paint: true);
                Chine(p, 5, R, s, 0.14, 0.1, HullPaint);
            }
            End2(p, 5, R, RoofDark, "dirty steel");
            End(p, 6, U, HullPaint);
            // Three red clearance lamps along the top of the rear.
            foreach (var t in new[] { -0.3, 0.0, 0.3 })
                Detail(p, Cx + t - 0.07, 6.45, RearZ(6.5) + 0.03, Cx + t + 0.07, 6.53, RearZ(6.5) + 0.07, "#e02818", glow: true);
        });
        // The hoop frames: dark girders right round the hull at the shoulder, midship and the haunch.
        for (var i = 0; i < FrameZ.Length; i++)
            With($"Roamer Frame {FrameNames[i]}", () => Rib(p, i + 2));
        // The ceiling (what the partitions meet), and the void over it to the skin, stepped: what the roof gear stands on.
        foreach (var (z0, z1) in Strips(UpFront, Back, RoomCuts(1)))
        {
            var w = RoomHalf(1, z0, z1);
            Solid(p, Cx - w, Ceil, z0, Cx + w, CeilT, z1, Ceiling, "painted plating");
        }
        Void(p, UpFront, Back, CeilT, RoofLow, 0.5);
        Void(p, 4.0, 13.8, RoofLow, RoofDeck, 0.5);
        // A hatch's coaming on the spine aft, shut, with grab rails.
        Solid(p, 3.6, RoofDeck, 13.0, 4.4, 7.42, 13.6, RoofDark, "dirty steel");
        Detail(p, 3.66, 7.42, 13.06, 4.34, 7.47, 13.54, Patch2, "rusty paint");
        foreach (var x in new[] { 3.45, 4.55 })
            Detail(p, x - 0.03, 7.3, 13.0, x + 0.03, 7.62, 13.6, Chrome, "chrome");
        // Running lights at the beam's ends: red to port, green to starboard, on the shoulder frame.
        var rl = Rings[2][D].W + 0.13;
        Detail(p, Cx - rl - 0.1, 2.9, 2.94, Cx - rl, 3.1, 3.06, "#e02018", glow: true);
        Detail(p, Cx + rl, 2.9, 2.94, Cx + rl + 0.1, 3.1, 3.06, "#20d040", glow: true);
        // Mounts for the gear: the air unit, the roof rack, the mast (on the void's top) and the radiator wings (on its sides).
        Sock(p, "airUnit", Cx - 0.3, RoofDeck, 9.6, "+y");
        Sock(p, "rack", Cx + 0.9, RoofDeck, 12.5, "+y");
        Sock(p, "mast", Cx - 1.6, RoofDeck, 5.2, "+y");
        Sock(p, "sensor", Cx - 0.4, RoofDeck, 5.2, "+y"); // (the Long Range Sensor beside the mast, the radar's scope: on a 1x1 module socket, its plate clear of the mast)
        Sock(p, "radL", Cx - RadX, 6.3, RadZ, "-x");
        Sock(p, "radR", Cx + RadX, 6.3, RadZ, "+x");
        return p;
    }

    private static readonly double RadX = Cm(Math.Min(SkinHalf(RadZ - 2, RoofLow), SkinHalf(RadZ + 2, RoofLow)) - 0.06);
    private const double RadZ = 11.5;

    // ---- the gear on the roof

    /// <summary>The roof's air unit, a camper's: a cream shroud with a bevelled top over a dark base, louvres down its sides, a fan
    /// grille on top and a rust run from a bolt. Origin on the roof's mount.</summary>
    private static Part AirUnit()
    {
        var p = new Part("Roamer Air Unit", Small, "Roamer", "The Roamer's rooftop air unit. It rattles, it drips, and on a hot world it is the most loved thing aboard.", role: "frame", tool: "welder", material: "steel", color: HullPaint, group: Group);
        LBox(p, -0.55, 0, -0.7, 0.55, 0.06, 0.7, Frame, "dirty steel");
        double[][] Rim(double y, double hx, double hz) => [[-hx, y, -hz], [hx, y, -hz], [hx, y, hz], [-hx, y, hz]];
        Prism(p, Rim(0.06, 0.53, 0.68), Rim(0.32, 0.53, 0.68), HullPaint, "worn paint", paint: true);
        Prism(p, Rim(0.32, 0.53, 0.68), Rim(0.44, 0.43, 0.58), HullPaint, "worn paint", paint: true);
        foreach (var sx in new[] { -1, 1 })
            for (var z = -0.5; z <= 0.51; z += 0.125)
                LBox(p, sx * 0.53, 0.1, z - 0.035, sx * 0.548, 0.28, z + 0.035, "#4a443c", "dirty steel", detail: true);
        LBox(p, -0.3, 0.44, -0.3, 0.3, 0.452, 0.3, "#2a2826", "dirty steel", detail: true);
        for (var x = -0.24; x <= 0.25; x += 0.08)
            LBox(p, x - 0.012, 0.452, -0.28, x + 0.012, 0.47, 0.28, "#8a8e8a", "steel", detail: true);
        LBox(p, 0.3, 0.1, 0.682, 0.34, 0.3, 0.688, Rust, "rust", detail: true);
        LBox(p, -0.4, 0.2, 0.682, -0.05, 0.25, 0.69, Brown, "worn paint", detail: true);
        LBox(p, -0.5, 0.06, -0.65, 0.5, 0.4, 0.65, null, hidden: true);
        p.Sock("bottom", [0, 0, 0], "-y");
        return p.Stats(180, 900, 10, 3.0);
    }

    /// <summary>A roof rack: chrome rails on feet, a cream cargo pod strapped down, two jerry cans, a rolled orange tarp and a
    /// folded deck chair in the camper's stripes. Origin on the roof's mount.</summary>
    private static Part RoofRack()
    {
        var p = new Part("Roamer Roof Rack", Small, "Roamer", "The Roamer's roof rack: a cargo pod, two jerry cans, a tarp and a deck chair. Holidays, once.", role: "frame", tool: "welder", material: "steel", color: Chrome, group: Group);
        LBox(p, -0.15, 0, -0.15, 0.15, 0.25, 0.15, Frame, "dirty steel"); // (the middle mount)
        foreach (var x in new[] { -1.05, 1.05 })
            foreach (var z in new[] { -1.25, 1.25 })
                LBox(p, x - 0.04, 0, z - 0.04, x + 0.04, 0.25, z + 0.04, Frame, "dirty steel");
        // The rails: two long ones, two ends, three cross bars under the load.
        foreach (var x in new[] { -1.05, 1.05 })
            LBox(p, x - 0.03, 0.25, -1.3, x + 0.03, 0.3, 1.3, Chrome, "chrome");
        foreach (var z in new[] { -1.27, 1.27 })
            LBox(p, -1.02, 0.252, z - 0.025, 1.02, 0.298, z + 0.025, Chrome, "chrome");
        foreach (var z in new[] { -0.6, 0.0, 0.6 })
            LBox(p, -1.02, 0.254, z - 0.02, 1.02, 0.29, z + 0.02, "#8a8e8a", "steel", detail: true);
        // The cargo pod, its lid bevelled, a strap over it.
        double[][] Rim(double y, double x0, double x1, double z0, double z1) => [[x0, y, z0], [x1, y, z0], [x1, y, z1], [x0, y, z1]];
        Prism(p, Rim(0.29, -0.98, 0.25, -1.2, 0.95), Rim(0.62, -0.98, 0.25, -1.2, 0.95), HullPaint, "worn paint", paint: true);
        Prism(p, Rim(0.62, -0.98, 0.25, -1.2, 0.95), Rim(0.72, -0.9, 0.17, -1.1, 0.85), HullPaint, "worn paint", paint: true);
        LBox(p, -0.985, 0.48, -1.205, 0.255, 0.52, 0.955, Brown, "worn paint", detail: true);
        foreach (var z in new[] { -0.6, 0.4 })
            LBox(p, -0.99, 0.296, z - 0.04, 0.26, 0.735, z + 0.04, "#2a2826", "fabric", detail: true);
        // Two jerry cans, red and olive.
        foreach (var (z, c) in new[] { (-1.0, "#a83224"), (-0.6, "#5a6a3a") })
        {
            LBox(p, 0.4, 0.3, z - 0.17, 0.72, 0.75, z + 0.17, c, "worn paint");
            LBox(p, 0.5, 0.75, z - 0.05, 0.62, 0.8, z + 0.05, "#2a2826", "plastic", detail: true);
        }
        // The tarp, rolled, along the starboard rail.
        double[][] Oct(double z, double r) => [.. Enumerable.Range(0, 8).Select(i => { var a = Math.PI / 8 + i * Math.PI / 4; return new[] { Py.Round(0.75 + r * Math.Cos(a), 4), Py.Round(0.46 + r * Math.Sin(a), 4), z }; })];
        Prism(p, Oct(-0.2, 0.16), Oct(1.1, 0.16), Orange, "fabric");
        // A folded deck chair lying on the pod's lid: its frame and the striped canvas.
        LBox(p, -0.8, 0.725, 0.0, -0.1, 0.75, 0.75, Chrome, "steel", detail: true);
        foreach (var (z0, z1, c) in new[] { (0.05, 0.25, Brown), (0.25, 0.45, Orange), (0.45, 0.7, Mustard) })
            LBox(p, -0.76, 0.75, z0, -0.14, 0.765, z1, c, "fabric", detail: true);
        LBox(p, -1.0, 0.25, -1.25, 1.0, 0.75, 1.25, null, hidden: true);
        p.Sock("bottom", [0, 0, 0], "-y");
        return p.Stats(300, 1200, 12, 4.0);
    }

    /// <summary>The Roamer's stair (Karl, 2026-11-20): the Wide Stair's width, 10 steps over a shorter run (StairRun), open, a ship's companionway
    /// of steel treads with hazard-striped nosings on two sloped stringers, hand rails on posts, and nothing under the flight, so the
    /// space beneath is somewhere to shove things. The treads are the solid boxes a person walks up (0.25 m a step, World.StepHeight).</summary>
    private static Part OpenStair()
    {
        var p = new Part("Roamer Stair", Small, "Roamer", "The Roamer's open stair: 10 striped steel treads up a storey, and the space under it for whatever doesn't fit anywhere else.", role: "frame", tool: "welder", material: "steel", color: "#6f767f", group: Group);
        const int n = 10; // (0.3 m a step over 0.29 m treads: as deep as the Wide Stair's, which a standing body climbs; 12 shallower ones stuck it)
        const double run = StairRun / n, rise = 3.0 / n, z0 = -0.125, foot = -0.125;
        const double slope = rise / run; // (along the nosings)
        const string treadC = "#7d848c", stringerC = "#7a8088", railC = "#a4aab0", yellow = "#d8b62a", black = "#1e1e1c";
        double Nose(double z) => foot + rise + slope * (z - z0); // (the nosings' line: tread k's front edge is on it)
        // The treads (solid: what is walked on), a dark angle under each one's nose.
        for (var k = 0; k < n; k++)
        {
            double a = z0 + run * k, b = a + run - 0.03, top = foot + rise * (k + 1);
            LBox(p, -0.05, top - 0.05, a, 1.55, top, b, treadC, "steel");
            LBox(p, -0.05, top - rise + 0.003, a + 0.002, 1.55, top - 0.05, a + run - 0.002, null, hidden: true); // (a step's depth for the feet: the open riser isn't stepped into)
            LBox(p, -0.015, top - 0.085, a + 0.004, 1.515, top - 0.045, a + 0.045, stringerC, "dirty steel", detail: true);
            // The nosing: a yellow strip, black slashes across it.
            double s0 = a + 0.012, s1 = a + 0.092;
            LBox(p, 0.0, top - 0.002, s0, 1.5, top + 0.004, s1, yellow, "hazard paint", detail: true);
            for (var x = 0.02; x + 0.1 <= 1.49; x += 0.1)
            {
                double[][] Slash(double y) => [[x, y, s0 + 0.004], [x + 0.04, y, s0 + 0.004], [x + 0.09, y, s1 - 0.004], [x + 0.05, y, s1 - 0.004]];
                Prism(p, Slash(top + 0.002), Slash(top + 0.007), black, "hazard paint");
            }
        }
        // The stringers: slim sloped steel channels either side, level with the nosings, the treads' ends let into them; cut level at the head.
        var zh = z0 + StairRun - 0.005;
        double Top(double z) => Nose(z) + 0.015;
        var cap = foot + rise * n + 0.01;
        var zc = z0 + (cap - Top(z0)) / slope;
        foreach (var (x0, x1) in new[] { (-0.09, -0.03), (1.53, 1.59) })
        {
            double zs = z0 + 0.015, ys = foot + 0.02; // (standing on the kick plate, just aft of its front)
            double[][] Side(double x) => [[x, ys, zs], [x, Top(zs), zs], [x, cap, zc], [x, cap, zh], [x, Top(zh) - (Top(zs) - ys), zh]];
            Prism(p, Side(x0), Side(x1), stringerC, "dirty steel");
        }
        // The hand rails: square tube 0.9 m over the nosings on three posts a side, stopping short of the deck's edge.
        double Rail(double z) => Nose(z) + 0.9;
        const double ra = 0.05, rb = z0 + StairRun - 0.42;
        foreach (var x in new[] { -0.06, 1.56 })
        {
            double[][] End(double z) => [[x - 0.022, Rail(z) - 0.022, z], [x + 0.022, Rail(z) - 0.022, z], [x + 0.022, Rail(z) + 0.022, z], [x - 0.022, Rail(z) + 0.022, z]];
            Prism(p, End(ra), End(rb), railC, "steel");
            foreach (var z in new[] { 0.1, (0.1 + rb) / 2, rb - 0.05 })
                LBox(p, x - 0.016, Top(z) - 0.03, z - 0.016, x + 0.016, Rail(z) - 0.01, z + 0.016, railC, "steel", detail: true);
        }
        // Steel foot plates under the stringers, bolted to the floor: what the stair stands on. The sockets: its foot either side, its head.
        foreach (var (x0, x1) in new[] { (-0.11, -0.01), (1.51, 1.61) })
            LBox(p, x0, foot, z0 + 0.008, x1, foot + 0.02, 0.15, stringerC, "dirty steel");
        // The header angle under the top tread's back edge, and on it the "head" socket: a Steel Catwalk's open end meets it level
        // with the top tread (Sean, 2026-11-21: CatwalkParts).
        LBox(p, -0.05, foot + rise * n - 0.11, z0 + StairRun - 0.075, 1.55, foot + rise * n - 0.05, z0 + StairRun - 0.005, stringerC, "dirty steel");
        p.Sock("head", [0.75, foot + rise * n - 0.075, z0 + StairRun - 0.005], "+z");
        foreach (var x in new[] { 0.0, 1.5 })
        {
            p.Sock($"b{x * 4:0}_0", [x, foot, 0], "-y");
            p.Sock($"t{x * 4:0}", [x, foot + rise * n, z0 + StairRun - 0.125], "+y");
        }
        p.Set(("walk", "stairs"), ("collider", "boxes")); // (the stringers and rails are looks: a hull round them would fill the space under the flight)
        return p.Stats(420, 671, 20, 3.0);
    }

    private static double[] Lo(double x0, double y0, double z0, double x1, double y1, double z1) => [Math.Min(x0, x1), Math.Min(y0, y1), Math.Min(z0, z1)];

    private static double[] Hi(double x0, double y0, double z0, double x1, double y1, double z1) => [Math.Max(x0, x1), Math.Max(y0, y1), Math.Max(z0, z1)];

    private static void LBox(Part p, double x0, double y0, double z0, double x1, double y1, double z1, string? color, string? finish = null, bool detail = false, bool hidden = false, bool glow = false)
    {
        PyDict? shape = null;
        if (finish != null || glow)
        {
            shape = new PyDict();
            if (finish != null)
                shape["finish"] = finish;
            if (glow)
                shape["glow"] = true;
        }
        p.Add(hidden ? Box(Lo(x0, y0, z0, x1, y1, z1), Hi(x0, y0, z0, x1, y1, z1), hidden: true) : Box(Lo(x0, y0, z0, x1, y1, z1), Hi(x0, y0, z0, x1, y1, z1), color!, detail: detail, shape: shape));
    }

    // ---- the engine pods, the saddle tanks, the drives and the lift jets

    /// <summary>An octagonal nacelle along z round (cx, cy) through the stations (z, radius), banded and patched, its ends capped.</summary>
    private static void Nacelle(Part p, double cx, double cy, (double Z, double Rr)[] st, int band, string capFront, string capBack)
    {
        double[] Ring(double z, double rr, int i)
        {
            var a = Math.PI / 8 + i * Math.PI / 4;
            return P(cx + rr * Math.Cos(a), cy + rr * Math.Sin(a), z);
        }
        for (var k = 0; k < st.Length - 1; k++)
            for (var i = 0; i < 8; i++)
                Quad(p, Ring(st[k].Z, st[k].Rr, i), Ring(st[k].Z, st[k].Rr, i + 1), Ring(st[k + 1].Z, st[k + 1].Rr, i + 1), Ring(st[k + 1].Z, st[k + 1].Rr, i),
                    k == band ? (i % 3 == 0 ? Patch2 : HullPaint) : Patch, paint: k == band && i % 3 != 0);
        Plate(p, [.. Enumerable.Range(0, 8).Select(i => Ring(st[0].Z, st[0].Rr, i))], Frame, capFront);
        Plate(p, [.. Enumerable.Range(0, 8).Select(i => Ring(st[^1].Z, st[^1].Rr, i))], "#2a2624", capBack);
    }

    /// <summary>A ring of armour round a nacelle: an octagonal band from z0 to z1, r0 to r1 out (a cowl, a collar, a fin ring).</summary>
    private static void Collar(Part p, double cx, double cy, double z0, double z1, double r0, double r1, string color, string finish)
    {
        double[][] Ring(double z, double rr) => [.. Enumerable.Range(0, 8).Select(i => { var a = Math.PI / 8 + i * Math.PI / 4; return P(cx + rr * Math.Cos(a), cy + rr * Math.Sin(a), z); })];
        Prism(p, Ring(z0, r0), Ring(z1, r1), color, finish);
    }

    // The stern pods' and the saddle tanks' middles and sizes (Sx metres), the main drives' and lift jets' pushes (N).
    private const double PodX = 5.75, PodY = 1.4, PodR = 0.9, SadX = 5.5, SadY = 1.0, SadR = 0.65, SadZ = 5.6, PodLiftZ = 14.2;
    private const double DriveForce = 600000, LiftForce = 800000, JetBurn = 3.0; // (l/s per MN, as the shipped jets)
    private const double PodZ = 14.5, PodEng = 14.85; // the pod's middle (its socket), the bulkhead between its tank and its engine housing
    private const double RcsForce = 80000, HeavyRcsForce = 250000; // N: the attitude jets (Karl, 2026-11-15: a ship needs thrusters pointing every way to turn every way)
    private const double PodRcsZ = 15.4, PodRcsDownZ = 15.9, SadRcsZ = 6.1, SadRcsDownZ = 6.95;
    private static readonly double PodInner = Cm(SkinHalf(PodZ, PodY)), SadInner = Cm(SkinHalf(SadZ, SadY));

    private static Part Pod(int side)
    {
        var p = new Part(side < 0 ? "Roamer Pod L" : "Roamer Pod R", Small, "Roamer", "One of the Roamer's engine pods: a patched nacelle full of fuel, a lift jet under it, and at its back the engine housing: the main drive on the tail plate outside, its Drive Core inside behind it, a service hatch in the outboard flank.", role: "fueltank", tool: "welder", material: "steel", color: HullPaint, group: Group);
        double cx = Cx + side * PodX, cy = PodY, r = PodR;
        Nacelle(p, cx, cy, [(10.4, 0.35), (11.3, r), (PodEng, r)], 1, "dirty steel", "soot");
        // The engine housing (ship-works.md 15.4, Karl 2026-11-16: "the nacelles don't have any thrust components in them"): the
        // tank stops at a bulkhead and the pod's tail is hollow round the drive's own Drive Core S, reached through a hatch.
        EngineHousing.Draw(p, P, cx, cy, Flat(r), EngineHousing.HalfFlat(Flat(r)), PodEng, 17.2, side, Patch, HullPaint, Frame, Stripe, "rusty paint", ShipWorksParts.CoreR[0], ShipWorksParts.CoreD[0], ShipWorksParts.CoreD[0]);
        Collar(p, cx, cy, PodEng - 0.08, PodEng + 0.12, r * 1.02, r * 1.08, Armour, "plated steel"); // (the band over the bulkhead)
        // A stepped intake cowl at the front, three cooling rings, a fairing along its top.
        Collar(p, cx, cy, 10.95, 11.45, r * 0.98, r * 1.1, Armour, "plated steel");
        Collar(p, cx, cy, 11.45, 11.75, r * 1.1, r * 1.04, Frame, "dirty steel");
        foreach (var z in new[] { 13.2, 13.5, 13.8 })
            Collar(p, cx, cy, z, z + 0.08, r * 1.01, r * 1.08, "#2d3033", "steel");
        Prism(p, [P(cx - 0.18, cy + r * 0.97, 11.8), P(cx + 0.18, cy + r * 0.97, 11.8), P(cx + 0.1, cy + r * 1.12, 12.3), P(cx - 0.1, cy + r * 1.12, 12.3)],
            [P(cx - 0.18, cy + r * 0.97, PodEng - 0.1), P(cx + 0.18, cy + r * 0.97, PodEng - 0.1), P(cx + 0.1, cy + r * 1.12, PodEng - 0.4), P(cx - 0.1, cy + r * 1.12, PodEng - 0.4)], Armour2, "plated steel");
        Detail(p, cx - side * 0.2, cy + 0.3, 13.6, cx - side * 0.83, cy + 0.42, 14.5, Stripe, "hazard paint");
        // Its engine is mended outside, through the hatch (ship works, Karl 2026-11-16): a grab rail along its outboard flank and a step under it, for a space walk or from the ground.
        foreach (var z in new[] { 14.1, 15.0 })
            Detail(p, cx + side * 0.74, cy + 0.43, z - 0.03, cx + side * 1.06, cy + 0.47, z + 0.03, "#9a9c98", "steel");
        Detail(p, cx + side * 1.03, cy + 0.41, 14.0, cx + side * 1.09, cy + 0.49, 15.1, Stripe, "worn paint");
        Detail(p, cx + side * 0.62, cy - 0.62, 14.75, cx + side * 0.98, cy - 0.58, 15.15, "#3a3836", "tread plate");
        // The pylons to the hull (each meets the skin), and the hidden box for the nacelle.
        var outer = cx - side * r * 0.9;
        foreach (var (z0, z1) in new[] { (12.2, 13.4), (15.0, 16.0) })
        {
            var inner = Cx + side * Cm(Math.Min(SkinHalf(z0, PodY), SkinHalf(z1, PodY)));
            Solid(p, inner, 1.15, z0, outer, 1.65, z1, Frame, "dirty steel");
        }
        Hidden(p, cx - r * 0.9, cy - r * 0.9, 11.3, cx + r * 0.9, cy + r * 0.9, PodEng - 0.004);
        Hidden(p, Cx + side * PodInner, PodY - 0.1, PodZ - 0.1, outer, PodY + 0.1, PodZ + 0.1); // (what the socket is on)
        Sock(p, "hull", Cx + side * PodInner, PodY, PodZ, side < 0 ? "+x" : "-x");
        Works.Mount(p, outside: true); // (the housing's tail plate: the drive bolts to it outside, its Drive Core inside)
        Sock(p, "lift", cx, cy - r * 0.9, PodLiftZ, "-y");
        // The attitude jets: up on the fairing, down under the pod behind the lift jet, out on the outboard flat.
        RcsMount(p, "rcsUp", cx, cy + r * 1.12, PodEng - 0.75, "+y", 0.36, Frame); // (on the fairing, ahead of the housing)
        RcsMount(p, "rcsDown", cx, cy - Flat(r), PodRcsDownZ, "-y", 0.36, Frame);
        RcsMount(p, "rcsOut", cx + side * Flat(r), cy, PodRcsZ, side < 0 ? "-x" : "+x", 0.36, Frame);
        p.Set(("fuelTank", Kw(("capacity", 2500))), ("ports", Py.L(BigDriveParts.PipePort("Fuel out", "out", [.. P(cx + side * r * 0.9, cy, 13.0).Cast<object>()], side < 0 ? "-x" : "+x", "fuel"))));
        return p.Stats(3500, 6000, 25, 10.0);
    }

    /// <summary>A saddle tank on the hull's flank forward: fuel for the forward lift jet slung under it.</summary>
    private static Part Saddle(int side)
    {
        var p = new Part(side < 0 ? "Roamer Saddle Tank L" : "Roamer Saddle Tank R", Small, "Roamer", "A saddle tank on the Roamer's flank: fuel for the forward lift jet under it.", role: "fueltank", tool: "welder", material: "steel", color: HullPaint, group: Group);
        double cx = Cx + side * SadX, cy = SadY, r = SadR;
        Nacelle(p, cx, cy, [(3.7, 0.22), (4.4, r), (7.3, r), (8.0, 0.3)], 1, "dirty steel", "dirty steel");
        // Two straps round it, a filler cap, and the pylon to the hull on the socket.
        foreach (var z in new[] { 4.9, 6.6 })
            Collar(p, cx, cy, z - 0.05, z + 0.05, r * 1.0, r * 1.06, Frame, "dirty steel");
        Detail(p, cx - 0.08, cy + r * 0.95, 5.6, cx + 0.08, cy + r * 1.12, 5.76, Stripe, "hazard paint");
        var inner = Cx + side * SadInner;
        Solid(p, inner, cy - 0.2, SadZ - 0.5, cx - side * r * 0.7, cy + 0.2, SadZ + 0.5, Frame, "dirty steel");
        Hidden(p, cx - r * 0.9, cy - r * 0.9, 4.4, cx + r * 0.9, cy + r * 0.9, 7.3);
        Sock(p, "hull", inner, cy, SadZ, side < 0 ? "+x" : "-x");
        Sock(p, "lift", cx, cy - r * 0.9, SadZ, "-y");
        // The attitude jets: up between the filler and the aft strap, down behind the lift jet, out on the outboard flat, and a retro on its nose.
        RcsMount(p, "rcsUp", cx, cy + Flat(r), SadRcsZ, "+y", 0.36, Frame);
        RcsMount(p, "rcsDown", cx, cy - Flat(r), SadRcsDownZ, "-y", 0.36, Frame);
        RcsMount(p, "rcsOut", cx + side * Flat(r), cy, SadRcsZ, side < 0 ? "-x" : "+x", 0.36, Frame);
        RcsMount(p, "rcsFore", cx, cy, 3.7, "-z", 0.54, Frame);
        p.Set(("fuelTank", Kw(("capacity", 1000))), ("ports", Py.L(BigDriveParts.PipePort("Fuel out", "out", [.. P(cx + side * r * 0.9, cy, 6.4).Cast<object>()], side < 0 ? "-x" : "+x", "fuel"))));
        return p.Stats(1200, 3000, 20, 8.0);
    }

    /// <summary>
    /// A mount for an attitude jet (ShipParts.Rcs): a square pad standing <c>0.04</c> m proud of the surface point, its outer face the socket (a socket sits on a solid box's face).
    /// <paramref name="w"/>: the pad's width, a little over the jet's housing.
    /// </summary>
    private static void RcsMount(Part p, string name, double x, double y, double z, string dir, double w, string color)
    {
        var s = dir[0] == '-' ? -1 : 1;
        double[] at = [x, y, z];
        var axis = dir[1] - 'x';
        double[] lo = [x - w / 2, y - w / 2, z - w / 2], hi = [x + w / 2, y + w / 2, z + w / 2];
        lo[axis] = Math.Min(at[axis] - s * 0.11, at[axis] + s * 0.04);
        hi[axis] = Math.Max(at[axis] - s * 0.11, at[axis] + s * 0.04);
        at[axis] += s * 0.04;
        Solid(p, lo[0], lo[1], lo[2], hi[0], hi[1], hi[2], color, "dirty steel");
        Sock(p, name, at[0], at[1], at[2], dir);
    }

    /// <summary>How far an octagon's flat side is from its middle, for corner radius r (the corners at 22.5 degrees off the axes, so the flats face along them).</summary>
    private static double Flat(double r) => Math.Round(r * Math.Cos(Math.PI / 8), 3);

    // ---- the storeys dealt out into pieces (Karl, 2026-11-15: a hit takes a panel, not a deck). Each box carries the piece it was
    // drawn for (or "~hull": placed by where it stands, or nothing: the storey's own piece). Each socket goes to the piece whose box
    // face it is on, and wherever two pieces' solid boxes touch they get a facing socket pair: one pair places a piece, the game
    // welds the rest (ConstructBuild.AutoLink), so a piece knocked off leaves the rest welded to each other.

    private static readonly Dictionary<string, Part> Pieces = [];
    private static readonly Dictionary<(string Part, string Sock), string> SockOwner = []; // (a storey's socket: the piece that holds it now)

    private static double[] Vd(PyDict b, string key) => [.. ((List<object?>)b[key]!).Select(v => Convert.ToDouble(v) + H)];

    private static bool Drawn(PyDict b) => b.Get("detail") is true;

    private static Part Piece(string name)
    {
        if (Pieces.TryGetValue(name, out var p))
            return p;
        var what = name["Roamer ".Length..];
        var desc = what switch
        {
            "Keel" => "The Roamer's lower deck: the hold's and the engine room's floor, the belly and the legs' mounts.",
            "Deck" => "The Roamer's upper deck: the floor of the cockpit, the cabins, hydroponics and the galley.",
            "Roof" => "The Roamer's roof: the upper deck's ceiling, the eaves and the spine, and the mounts for the gear on it.",
            "Chin" => "The Roamer's chin: the hull either side of the cargo ramp, round the front of the hold.",
            "Canopy" => "The Roamer's nose: the bridge front over the ramp, the windscreen, the side windows and the cockpit's front.",
            "Tail" => "The Roamer's tail behind the rooms: the heat exchanger, the fins and the tail lights.",
            _ when what.StartsWith("Frame") => $"One of the Roamer's hoop frames ({what[6..].ToLowerInvariant()}): a girder right round the hull. Tough.",
            _ when what.StartsWith("Partition") => "A partition of the Roamer's rooms, with its doorways.",
            _ => $"A run of the Roamer's hull ({what.ToLowerInvariant()}): the skin, its plates and rails, and the wall behind them.",
        };
        p = new Part(name, Small, "Roamer", desc, role: "frame", tool: "welder", material: "steel", color: what.StartsWith("Partition") ? Wall : HullPaint, group: Group);
        Pieces[name] = p;
        return p;
    }

    /// <summary>The piece a "~hull" box of storey level goes to, by where it stands.</summary>
    private static string Where(PyDict b, int level)
    {
        double[] mn = Vd(b, "min"), mx = Vd(b, "max");
        double xc = (mn[0] + mx[0]) / 2, zc = (mn[2] + mx[2]) / 2;
        if (!Drawn(b))
            for (var i = 0; i < FrameZ.Length; i++)
                if (mn[2] >= FrameZ[i] - 0.105 && mx[2] <= FrameZ[i] + 0.105)
                    return $"Roamer Frame {FrameNames[i]}";
        var deck = level == 0 ? "Lower" : "Upper";
        if (zc < 2.95)
            return level == 0 ? "Roamer Chin" : "Roamer Canopy";
        if (zc > Back + 0.04)
            return "Roamer Tail";
        if (zc > 14.05)
            return $"Roamer {deck} Stern";
        return $"Roamer {deck} {(xc < Cx ? "Port" : "Stbd")} {(zc < 8.5 ? "Fore" : "Aft")}";
    }

    /// <summary>Deals a storey's boxes out to the pieces and its sockets to the piece whose face each is on.</summary>
    private static void Split(Part storey, int level)
    {
        var own = (string)storey.D["name"]!;
        foreach (var b in storey.Boxes)
        {
            var to = Tags.GetValueOrDefault(b);
            var name = to == null ? own : to == HullTag ? Where(b, level) : to;
            Piece(name).Boxes.Add(b);
        }
        foreach (var sk in storey.Sockets)
        {
            var at = ((List<object?>)sk["at"]!).Select(v => Convert.ToDouble(v) + H).ToArray();
            var dir = (string)sk["dir"]!;
            var ax = dir[1] - 'x';
            var plus = dir[0] == '+';
            Part? owner = null, drawnOwner = null;
            foreach (var q in Pieces.Values)
                foreach (var b in q.Boxes)
                {
                    double[] mn = Vd(b, "min"), mx = Vd(b, "max");
                    if (Math.Abs((plus ? mx[ax] : mn[ax]) - at[ax]) > 0.005)
                        continue;
                    var inside = true;
                    for (var a = 0; a < 3; a++)
                        if (a != ax && (at[a] < mn[a] - 0.005 || at[a] > mx[a] + 0.005))
                            inside = false;
                    if (!inside)
                        continue;
                    if (Drawn(b))
                        drawnOwner ??= q;
                    else
                        owner ??= q;
                }
            owner ??= drawnOwner ?? Piece(own);
            owner.Sockets.Add(sk);
            SockOwner[(own, (string)sk["name"]!)] = (string)owner.D["name"]!;
        }
    }

    /// <summary>The pieces' stats: the frames toughest, then the floors, the hull runs, the partitions; the mass near the first hull's.</summary>
    private static void PieceStats()
    {
        foreach (var (name, p) in Pieces)
        {
            var what = name["Roamer ".Length..];
            _ = what switch
            {
                "Keel" => p.Stats(26000, 14000, 30, 20.0),
                "Deck" => p.Stats(12000, 9000, 25, 20.0),
                "Roof" => p.Stats(5000, 5000, 20, 12.0),
                "Canopy" => p.Stats(2500, 2500, 20, 8.0),
                "Chin" => p.Stats(1500, 2500, 25, 8.0),
                "Tail" => p.Stats(3500, 4000, 25, 8.0),
                _ when what.StartsWith("Frame") => p.Stats(2200, 30000, 40, 8.0),
                _ when what.StartsWith("Partition") => p.Stats(400, 1200, 15, 4.0),
                _ when what.EndsWith("Stern") => p.Stats(1100, 3000, 25, 6.0),
                _ when what.StartsWith("Lower") => p.Stats(1800, 3500, 30, 6.0),
                _ => p.Stats(1600, 3500, 25, 6.0),
            };
        }
    }

    /// <summary>A facing socket pair wherever two of the parts' solid boxes touch face to face (one pair a pair of parts, on their
    /// biggest contact), unless they already meet at one: what welds the pieces to each other.</summary>
    private static void Connect(List<Part> parts)
    {
        var solid = parts.Select(q => q.Boxes.Where(b => !Drawn(b)).Select(b => (Mn: Vd(b, "min"), Mx: Vd(b, "max"))).ToList()).ToList();
        var count = parts.Select(q => q.Sockets.Count(s => ((string)s["name"]!).StartsWith('j'))).ToArray();
        for (var i = 0; i < parts.Count; i++)
            for (var j = i + 1; j < parts.Count; j++)
            {
                if (Meet(parts[i], parts[j]))
                    continue;
                (double Area, double[] At, int Axis, bool IPlus) best = (0, [], 0, false);
                foreach (var A in solid[i])
                    foreach (var Bx in solid[j])
                        for (var ax = 0; ax < 3; ax++)
                        {
                            bool iPlus = Math.Abs(A.Mx[ax] - Bx.Mn[ax]) < 0.001, iMinus = Math.Abs(A.Mn[ax] - Bx.Mx[ax]) < 0.001;
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
                                double lo = Math.Max(A.Mn[a], Bx.Mn[a]), hi = Math.Min(A.Mx[a], Bx.Mx[a]);
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
                Sock(parts[i], $"j{count[i]++}", best.At[0], best.At[1], best.At[2], (best.IPlus ? "+" : "-") + axis);
                Sock(parts[j], $"j{count[j]++}", best.At[0], best.At[1], best.At[2], (best.IPlus ? "-" : "+") + axis);
            }
    }

    /// <summary>Whether two parts (at one origin) already have a socket of each at one point, facing each other.</summary>
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

    /// <summary>The order to place the structure in: out from the first part over the socket pairs they meet at.</summary>
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
            throw new InvalidOperationException("Rust bucket: pieces joined to nothing: " + string.Join(", ", names.Where((n, i) => !seen.Contains(i))));
        return order;
    }

    // ---- the blueprint

    private static List<string> Structure = [];
    private static Dictionary<string, int> PieceSteps = [];

    /// <summary>The step that holds socket sock of step on: a storey's piece that took it when the storey was split, else on itself.</summary>
    private static int Holder(BuildingParts.Blueprint bp, int on, string sock) =>
        bp.Steps[on]["part"] is string part && SockOwner.TryGetValue((part, sock), out var piece) ? PieceSteps[piece] : on;

    private static int Hang(BuildingParts.Blueprint bp, string part, int on, string sock, string by, int spin = 0) => bp.Hang(part, Holder(bp, on, sock), sock, by, spin);

    /// <summary>A ship's flight stabilizer settings: Ship keys (FlightPattern.Ship), the nose on the mouse.</summary>
    private static PyDict ShipFlight() => ShipParts.ShipFlight();

    private static int Paint(BuildingParts.Blueprint bp, int step, string paint, string? finish = null)
    {
        bp.Steps[step]["paint"] = paint;
        if (finish != null)
            bp.Steps[step]["finish"] = finish;
        return step;
    }

    private static int Lamp(BuildingParts.Blueprint bp, int on, string sock, string colour = LampGlow)
    {
        var s = Hang(bp, "Bulkhead Light", on, sock, "back");
        bp.Steps[s]["config"] = new PyDict { ["colour"] = colour };
        return s;
    }

    private static void Switch(BuildingParts.Blueprint bp, int on, string sock, params int[] lamps)
    {
        var s = Hang(bp, "Bakelite Switch", on, sock, "back");
        bp.Steps[s]["config"] = new PyDict { ["startOn"] = true };
        foreach (var l in lamps)
            bp.Wires.Add((s, l));
    }

    /// <summary>A Module Socket W x L centred on step <paramref name="on"/>'s socket, with its kind (the table's order) and keying. Its step.</summary>
    private static int ModuleSocket(BuildingParts.Blueprint bp, int on, string sock, int w, int l, int kind = 0, bool keyed = false, double above = 0)
    {
        var s = bp.Hang($"Module Socket {w}x{l}", on, sock, "bc");
        var cfg = new PyDict();
        if (kind != 0)
            cfg["modKind"] = kind;
        if (keyed)
            cfg["modKeyed"] = 1;
        if (above > 0)
            cfg["modAbove"] = above;
        if (cfg.Count > 0)
            bp.Steps[s]["config"] = cfg;
        return s;
    }

    /// <summary>A module fitted to a plate: its plug, then <paramref name="part"/> by <paramref name="by"/> on the plug's middle, unturned in the ship as it stood before the retrofit. The part's step.</summary>
    private static int FittedModule(BuildingParts.Blueprint bp, int plate, int w, int l, string part, string by)
    {
        var plug = bp.Hang($"Module Plug {w}x{l}", plate, "module", "module");
        return bp.HangTurned(part, plug, ModuleSocketParts.CentreTop(w, l), by, BuildingParts.R0);
    }

    private static BuildingParts.Blueprint Blueprint(Dictionary<string, PyDict> defs)
    {
        var bp = new BuildingParts.Blueprint("Roamer", "An old motorhome of a ship, the rust bucket's sister: cream paint and stripes, a wraparound windscreen, windows down both sides, a cockpit, two cabins, hydroponics, a galley, a hold and an engine room.", defs, maxSteps: 200);
        bp.Conduit = true; // (its lamp wires run along the walls and over the ceilings, not across the rooms)
        double[] zero = [0, 0, 0];
        var steps = new Dictionary<string, int>();
        foreach (var name in Structure)
            steps[name] = steps.Count == 0 ? bp.First(name) : bp.Put(name, zero, 0);
        PieceSteps = steps;
        int keel = steps["Roamer Keel"], deck = steps["Roamer Deck"], roof = steps["Roamer Roof"];
        var pods = new[] { Hang(bp, "Roamer Pod L", keel, "podL", "hull"), Hang(bp, "Roamer Pod R", keel, "podR", "hull") };
        var saddles = new[] { Hang(bp, "Roamer Saddle Tank L", keel, "saddleL", "hull"), Hang(bp, "Roamer Saddle Tank R", keel, "saddleR", "hull") };
        foreach (var pod in pods)
        {
            // The main drive is a module (module-sockets.md 11): a keyed 2x2 engine socket on the tail plate, the drive on its plug; the
            // Drive Core stays inside behind the plate, the pod being the drive's mount (ConstructWorks.MountUnder).
            FittedModule(bp, ModuleSocket(bp, pod, "drive", 2, 2, kind: 1, keyed: true), 2, 2, "Rust Bucket Drive", "front");
            bp.Hang("Drive Core S", pod, "core", "front"); // (its engine, inside the housing on the drive's axis, bolted to the tail plate)
            Works.HangHatch(bp, pod, pod == pods[0] ? -1 : 1);
            bp.Hang("Rust Bucket Lift Jet", pod, "lift", "top");
        }
        var foreLift = saddles.Select(t => bp.Hang("Rust Bucket Lift Jet", t, "lift", "top")).ToArray();
        // Attitude jets pointing every way at both ends, so it turns about any axis and stops any way; heavy retros on the saddle tanks' noses to brake.
        foreach (var t in pods.Concat(saddles))
            foreach (var sock in new[] { "rcsUp", "rcsDown", "rcsOut" })
                bp.Hang("RCS Thruster", t, sock, "base");
        foreach (var t in saddles)
            bp.Hang("Heavy RCS Thruster", t, "rcsFore", "base");
        foreach (var n in new[] { "legFL", "legFR", "legBL", "legBR" })
            Hang(bp, "Rust Bucket Leg", keel, n, "top");
        Hang(bp, "Roamer Ramp", keel, "ramp", "hang");
        Hang(bp, "Roamer Stair", keel, "stair", "b0_0");
        // The gear on the roof.
        Hang(bp, "Roamer Air Unit", roof, "airUnit", "bottom");
        Hang(bp, "Roamer Roof Rack", roof, "rack", "bottom");
        Hang(bp, "Rust Bucket Mast", roof, "mast", "bottom");
        FittedModule(bp, ModuleSocket(bp, Holder(bp, roof, "sensor"), "sensor", 1, 1), 1, 1, "Long Range Sensor", "-y"); // (the radar's scope in every seat; no missile rack, so no lock: ConstructRadar.CanLock)
        Hang(bp, "Rust Bucket Radiator L", roof, "radL", "root");
        Hang(bp, "Rust Bucket Radiator R", roof, "radR", "root");
        // The hold: shelving down the port side, a tool rack and a locker opposite, two crates by the stair.
        for (var i = 0; i < 3; i++)
            Paint(bp, Hang(bp, "Store Shelving 2 m", keel, $"shelfL{i}", "wall"), "#6b6f5a", "worn paint");
        Hang(bp, "Tool Rack", keel, "rackR", "back");
        Paint(bp, Hang(bp, "Locker", keel, "lockerR", "wall"), "#5d7468", "worn paint");
        Hang(bp, "Robot Shell", Hang(bp, "Robot Shell Dock", keel, "shellDock", "wall"), "dock", "back");
        Hang(bp, EvaParts.LockerName, keel, "suitR", "wall");
        Hang(bp, EvaParts.LockerName, keel, "suitL", "wall");
        Hang(bp, EvaParts.LifeSupportName, keel, "lifeSupport", "wall"); // (the ship's air: 16 person-hours with its tank)
        Hang(bp, EvaParts.OxygenTankName, keel, "o2Tank0", "wall");
        EvaParts.FitDock(bp, Holder(bp, keel, "mmuDock"), "mmuDock", saddles[1]); // (its pack fed from the starboard saddle tank, outside the wall)
        Hang(bp, "Requisition Crate", keel, "crate0", "-y");
        Hang(bp, "Requisition Crate", keel, "crate1", "-y");
        Hang(bp, "Requisition Crate", keel, "crate2", "-y");
        Hang(bp, "Requisition Crate", keel, "crate3", "-y");
        // The engine room. (The Engineer's Panel is where the breakdowns are seen to: GameSandboxBreakdowns reads its name.)
        FittedModule(bp, ModuleSocket(bp, Holder(bp, keel, "engine"), "engine", 3, 3, above: 2.5), 3, 3, "Ship Engine Block", "bottom"); // (a generic 3x3 socket, its room 2.5 m up under the deck: the block is 2 m wide, past a 2x2 plug's lugs; it makes no power, so a reactor module may take its place)
        Hang(bp, "Engineer's Panel", keel, "panelEng", "back");
        Hang(bp, "Power Board", keel, "boardEng", "back");
        Hang(bp, "Pipe Bank", keel, "pipesEngL", "back");
        Hang(bp, "Ship Battery S", keel, "battery", "wall");
        Switch(bp, keel, "switchHoldL", Lamp(bp, keel, "lampHoldL0"), Lamp(bp, keel, "lampHoldL1"));
        Switch(bp, keel, "switchHoldR", Lamp(bp, keel, "lampHoldR0"), Lamp(bp, keel, "lampHoldR1"));
        Switch(bp, keel, "switchEng", Lamp(bp, keel, "lampEngL"), Lamp(bp, keel, "lampEngR"));
        // The cockpit: the pilot's seat flies it through a flight stabilizer under the dashboard, on Ship keys with the mouse on the nose.
        var pilot = Hang(bp, "Worn Flight Couch", deck, "seatL", "bottom");
        Hang(bp, "Worn Flight Couch", deck, "seatR", "bottom");
        var stab = Hang(bp, "Flight Stabilizer", deck, "module", "-y"); // (a small gyro: the attitude jets turn it)
        bp.Steps[stab]["config"] = ShipFlight();
        bp.Wires.Add((pilot, stab));
        bp.HangTurned("Shell Control Chair", Holder(bp, deck, "linkChair"), "linkChair", "bottom", BuildingParts.R0);
        // The bedroom, the bathroom.
        Paint(bp, Hang(bp, "Iron Bed", deck, "bed", "wall"), "#6b4a2e", "worn paint");
        Paint(bp, Hang(bp, "Locker", deck, "locker", "wall"), "#8c7b52", "worn paint");
        Hang(bp, "Steel Desk", deck, "desk", "wall");
        Hang(bp, "Shower Stall", deck, "shower", "wall");
        Hang(bp, "Ship Basin", deck, "basin", "wall");
        Hang(bp, "Ship Toilet", deck, "toilet", "wall");
        // Hydroponics, the galley.
        foreach (var g in new[] { "growL0", "growL1", "growM0", "growM1" })
            Hang(bp, "Grow Rack 2 m", deck, g, "wall");
        Hang(bp, "Ship Galley Range", deck, "stove", "back");
        Hang(bp, "Ship Mess Table", deck, "table", "back");
        Hang(bp, "Medicine Cabinet", deck, "cabinet", "back");
        Hang(bp, "Galley Coffee Station", deck, "coffee", "wall");
        // Doors.
        foreach (var d in new[] { "doorCockpit", "doorBed", "doorBath", "doorHydro", "doorGalley" })
            Paint(bp, Hang(bp, "Steel Swing Door 1.5x2.25 m", deck, d, "hang"), d == "doorBath" ? "#d8dcd2" : "#8a6a48", "worn paint");
        // Lamps: forward and aft switches by the corridor's ends.
        Switch(bp, deck, "switchFwd", Lamp(bp, deck, "lampCockpit"), Lamp(bp, deck, "lampBed"), Lamp(bp, deck, "lampBath"), Lamp(bp, deck, "lampCorrA"));
        Switch(bp, deck, "switchAft", Lamp(bp, deck, "lampHydroA", "#f0c8f0"), Lamp(bp, deck, "lampHydroB", "#f0c8f0"), Lamp(bp, deck, "lampCorrB"), Lamp(bp, deck, "lampGalley"));
        return bp;
    }

    public void Make(PartOutput o)
    {
        Pieces.Clear();
        SockOwner.Clear();
        Tags.Clear();
        PartitionCount = 0;
        Part[] storeys = [Keel(), DeckPart(), Roof()];
        for (var i = 0; i < storeys.Length; i++)
            Split(storeys[i], i);
        PieceStats();
        var structure = Pieces.Values.ToList();
        Connect(structure);
        Structure = PlaceOrder(structure, "Roamer Keel");
        List<Part> parts =
        [
            .. structure, Ramp(), Pod(-1), Pod(1), Saddle(-1), Saddle(1), AirUnit(), RoofRack(), OpenStair(),
        ];
        var defs = new Dictionary<string, PyDict>();
        o.Own(Out);
        foreach (var p in parts)
        {
            if (p.Boxes.Count > 256 || p.Sockets.Count > 64)
                throw new InvalidOperationException($"Rust bucket: {p.D["name"]} has {p.Boxes.Count} boxes and {p.Sockets.Count} sockets");
            var d = p.Json();
            var name = (string)d["name"]!;
            defs.Add(name, d);
            o.File($"{Out}/{FileName(name)}", PyJson.Dumps(d, 2) + "\n");
        }
        BuildingParts.AddPartDefs(o, defs, Out);
        var bp = Blueprint(defs);
        o.File($"{BpOut}/Roamer.json", PyJson.Dumps(bp.Json(), 2) + "\n");
    }
}
