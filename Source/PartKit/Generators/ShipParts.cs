using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The rust bucket (Karl, 2026-11-14: "a start in a space ship. You have a rust bucket, it has a full interior including small
/// bedroom, bathroom, hydroponics, engine room, cockpit, storage room"; his steer on the plan: less boxy, corners, a nose that makes
/// sense). Writes Constructs/Parts/Ship and the Rust Bucket blueprint (docs/sandbox/constructs/ship.md).
///
/// Two decks in a faceted hull about 10 by 17.5 m: a wedge nose (the canopy glass rakes back from the tip over the cockpit, the chin
/// under it is the cargo ramp), cut corners at the stern, sides that break below the lower deck and above the upper one, an engine
/// pod on each side of the stern and a radiator spine on the roof. Upper deck (living): cockpit across the bow, a corridor aft with
/// the bedroom and bathroom either side, then hydroponics, the stair hall and the galley. Lower deck (working): the storage hold
/// behind the ramp, the engine room aft. A Steel Wide Stair joins them.
///
/// The hull is three big parts, one a storey (keel, deck, roof), each holding its floor, its partitions, its share of the hull's
/// facets (drawn only, any angle) and the hidden boxes that stand for them, so the whole ship is a few merged meshes and a few dozen
/// steps. Every big part has its origin at the body's (the keel's cell 0): the generator works in ship metres (Sx: x from the port
/// side, y from the lower floor's underside, z from the bow back) and moves them by half a cell. The furniture, the doors and the
/// stair are the shipped parts, on sockets these parts carry where each one goes.
/// </summary>
public sealed class ShipParts : IPartGenerator
{
    public string Name => "ship_parts";

    private const string Out = "Constructs/Parts/Ship";
    private const string BpOut = "Constructs/Blueprints";
    private const string Group = "Ship";
    private const double H = 0.125; // half a cell: the body's origin is the keel's cell 0's middle
    private const double T = 0.04; // a facet's thickness
    private const double Cx = 5.0; // the middle line

    // The hull's colours: a rust bucket is patched with whatever was to hand.
    private const string HullPaint = "#7a6a58", Patch = "#5d7468", Patch2 = "#8c7b52", Rust = "#6e3f26", Belly = "#3b3530",
        RoofDark = "#4b4743", Stripe = "#b08a2e", Frame = "#2c2a28", Wall = "#9c9482", WallLow = "#5f665f", Tile = "#c9ccc4",
        Floor = "#5e6264", Deck = "#4a4e50", GlassTint = "#7fa6c0", Grow = "#4f8a3a", Leaf = "#6fb04a", LampGlow = "#f2e6c0";

    // Storey heights (Sx metres).
    private const double F0 = 0.25; // lower floor's top
    private const double F1 = 3.0, F1T = 3.25; // upper floor's underside and top
    private const double Ceil = 6.0; // the upper ceiling's underside

    /// <summary>A hull ring: half width, height and z of its five points: belly edge, chine (lower floor), deck line, eaves, roof edge.</summary>
    private static readonly (double W, double Y, double Z)[][] Rings =
    [
        [(0.9, -0.6, 0.9), (1.8, 0.0, -0.3), (2.0, 3.0, -1.5), (1.6, 6.0, 1.4), (1.1, 6.8, 2.3)], // the nose
        [(3.4, -0.6, 3.6), (4.65, 0.0, 3.0), (5.06, 3.0, 3.0), (4.75, 6.0, 3.2), (3.6, 6.8, 4.2)], // the shoulder
        [(3.4, -0.6, 13.7), (4.65, 0.0, 14.0), (5.06, 3.0, 14.0), (4.75, 6.0, 14.0), (3.6, 6.8, 13.7)], // the haunch
        [(2.3, -0.6, 15.4), (3.2, 0.0, 16.06), (3.5, 3.0, 16.06), (3.25, 6.0, 16.06), (2.4, 6.8, 15.6)], // the transom
    ];

    // Where people stand: the lower deck's walls are at the chine's outline (the sides flare out above it), the upper deck's at
    // the eaves' (they lean in above the deck line). A socket on the hull's inside is on those hidden walls' faces.
    private const double InLow = 4.63, InHigh = 4.73;

    private const int B = 0, L = 1, D = 2, U = 3, R = 4; // a ring's points

    private static double[] P(double x, double y, double z) => [x - H, y - H, z - H];

    private static double[] Pt(int ring, int k, int side)
    {
        var (w, y, z) = Rings[ring][k];
        return P(Cx + side * w, y, z);
    }

    // ---- geometry helpers

    private static double[] Sub(double[] a, double[] b) => [a[0] - b[0], a[1] - b[1], a[2] - b[2]];

    private static double[] Cross(double[] a, double[] b) => [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]];

    private static double Dot(double[] a, double[] b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];

    private static double[] Unit(double[] a)
    {
        var l = Math.Sqrt(Dot(a, a));
        return [a[0] / l, a[1] / l, a[2] / l];
    }

    /// <summary>A flat plate through the points (a convex polygon), T thick about them: a "hull" shape, drawn only.</summary>
    private static void Plate(Part p, double[][] pts, string color, string finish = "rusty paint", bool glass = false, bool paint = false, bool glow = false)
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
                both.Add([.. Enumerable.Range(0, 3).Select(k => Py.Round(q[k] + s * n[k] * T / 2, 4))]);
        double[] mn = [.. Enumerable.Range(0, 3).Select(k => both.Min(q => q[k]))];
        double[] mx = [.. Enumerable.Range(0, 3).Select(k => both.Max(q => q[k]))];
        var shape = Kw(("shape", "hull"), ("points", both.Select(q => (object?)q.Select(x => (object?)x).ToList()).ToList()));
        if (!paint)
            shape["paintable"] = false;
        if (!glass && !glow)
            shape["finish"] = finish;
        if (glow)
            shape["glow"] = true;
        p.Add(Box(mn, mx, color, detail: true, glass: glass, shape: shape));
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
    private static void Prism(Part p, double[][] a, double[][] b, string color, string finish)
    {
        var both = a.Concat(b).Select(q => q.Select(x => Py.Round(x, 4)).ToArray()).ToList();
        double[] mn = [.. Enumerable.Range(0, 3).Select(k => both.Min(q => q[k]))];
        double[] mx = [.. Enumerable.Range(0, 3).Select(k => both.Max(q => q[k]))];
        var shape = Kw(("shape", "hull"), ("points", both.Select(q => (object?)q.Select(x => (object?)x).ToList()).ToList()), ("paintable", false), ("finish", finish));
        p.Add(Box(mn, mx, color, detail: true, shape: shape));
    }

    /// <summary>A painted patch on the middle run's band from point k up to k+1 (t0..t1 of the way up, z0..z1 along), on side s, a
    /// little proud of the plate.</summary>
    private static void Daub(Part p, int k, int s, double t0, double t1, double z0, double z1, string color, string finish, double lift = 0.01, bool glow = false)
    {
        var (w0, y0, _) = Rings[1][k];
        var (w1, y1, _) = Rings[1][k + 1];
        double dw = w1 - w0, dy = y1 - y0, ln = Math.Sqrt(dw * dw + dy * dy);
        double nx = dy / ln, ny = -dw / ln; // outward on the +x side
        double[] At(double t, double z) => P(Cx + s * (w0 + dw * t + nx * lift), y0 + dy * t + ny * lift, z);
        Plate(p, [At(t0, z0), At(t1, z0), At(t1, z1), At(t0, z1)], color, finish, glow: glow);
    }

    /// <summary>What closes the gap between a partition across the hull at z (stopping at the hidden wall) and the band from point
    /// k to k+1 that leans away from it over y0..y1: a wedge each side, drawn only.</summary>
    private static void Fillers(Part p, int k, double z, double y0, double y1, string color, bool port = true, bool starboard = true)
    {
        var (w0, ya, _) = Rings[1][k];
        var (w1, yb, _) = Rings[1][k + 1];
        double Hull(double y) => w0 + (w1 - w0) * (y - ya) / (yb - ya) - 0.03;
        var inner = k == L ? InLow : InHigh;
        foreach (var s in new[] { -1, 1 })
        {
            if (s < 0 && !port || s > 0 && !starboard)
                continue;
            double[][] Tri(double zz) => [P(Cx + s * inner, y0, zz), P(Cx + s * Math.Max(inner, Hull(y0)), y0, zz), P(Cx + s * Math.Max(inner, Hull(y1)), y1, zz), P(Cx + s * inner, y1, zz)];
            Prism(p, Tri(z - 0.12), Tri(z + 0.12), color, "wall panelling");
        }
    }

    /// <summary>The band between points k and k+1 of rings r0 and r1, both sides.</summary>
    private static void Band(Part p, int r0, int r1, int k, string color, string finish = "rusty paint", bool glass = false, bool paint = true)
    {
        // The middle run's plates stop 3 mm short top and bottom, so no two plates meeting at a corner share an edge's plane.
        var trim = r0 == 1 ? 0.003 : 0.0;
        foreach (var s in new[] { -1, 1 })
            Quad(p, Up(Pt(r0, k, s), trim), Up(Pt(r0, k + 1, s), -trim), Up(Pt(r1, k + 1, s), -trim), Up(Pt(r1, k, s), trim), color, finish, glass, paint);
    }

    private static double[] Up(double[] q, double dy) => [q[0], q[1] + dy, q[2]];

    /// <summary>An end's band between points k and k+1 of ring r, across the middle.</summary>
    private static void End(Part p, int r, int k, string color, string finish = "rusty paint", bool glass = false, bool paint = true) =>
        Quad(p, Up(Pt(r, k, -1), 0.003), Up(Pt(r, k, 1), 0.003), Up(Pt(r, k + 1, 1), -0.003), Up(Pt(r, k + 1, -1), -0.003), color, finish, glass, paint);

    private static void Solid(Part p, double x0, double y0, double z0, double x1, double y1, double z1, string color, string? finish = null)
    {
        var shape = finish == null ? null : Kw(("finish", finish));
        p.Add(Box(P(x0, y0, z0), P(x1, y1, z1), color, shape: shape));
    }

    private static void Detail(Part p, double x0, double y0, double z0, double x1, double y1, double z1, string color, string? finish = null, bool glow = false)
    {
        var shape = finish == null && !glow ? null : new PyDict();
        if (finish != null)
            shape!["finish"] = finish;
        if (glow)
            shape!["glow"] = true;
        p.Add(Box(P(x0, y0, z0), P(x1, y1, z1), color, detail: true, shape: shape));
    }

    private static void Hidden(Part p, double x0, double y0, double z0, double x1, double y1, double z1) =>
        p.Add(Box(P(Math.Min(x0, x1), y0, Math.Min(z0, z1)), P(Math.Max(x0, x1), y1, Math.Max(z0, z1)), hidden: true));

    /// <summary>The half width of the outline of point k at z (ring to ring), or -1 off the hull.</summary>
    private static double HalfWidth(int k, double z)
    {
        for (var r = 0; r < Rings.Length - 1; r++)
        {
            var (w0, _, z0) = Rings[r][k];
            var (w1, _, z1) = Rings[r + 1][k];
            if (z >= z0 - 1e-6 && z <= z1 + 1e-6)
                return z1 - z0 < 1e-6 ? Math.Max(w0, w1) : w0 + (w1 - w0) * (z - z0) / (z1 - z0);
        }
        return -1;
    }

    /// <summary>Strips across outline k, front to back: half a metre long where it narrows, one piece where it is straight, cut at <paramref name="cuts"/>.
    /// Each strip's half width is its narrower end's (so a floor never pokes out of the hull) and its wider end's.</summary>
    private static List<(double Z0, double Z1, double Narrow, double Wide)> Strips(int k, params double[] cuts)
    {
        var zs = new SortedSet<double>();
        var front = Rings[0][k].Z;
        var back = Rings[^1][k].Z;
        for (var r = 0; r < Rings.Length; r++)
            zs.Add(Rings[r][k].Z);
        foreach (var c in cuts)
            if (c > front && c < back)
                zs.Add(c);
        for (var r = 0; r < Rings.Length - 1; r++)
        {
            var (w0, _, z0) = Rings[r][k];
            var (w1, _, z1) = Rings[r + 1][k];
            if (Math.Abs(w1 - w0) > 1e-6)
                for (var z = z0 + 0.5; z < z1 - 0.05; z += 0.5)
                    zs.Add(Math.Round(z, 3));
        }
        var l = zs.ToList();
        var o = new List<(double, double, double, double)>();
        for (var i = 0; i < l.Count - 1; i++)
        {
            double a = HalfWidth(k, l[i]), b = HalfWidth(k, l[i + 1]);
            o.Add((l[i], l[i + 1], Math.Min(a, b), Math.Max(a, b)));
        }
        return o;
    }

    /// <summary>The floor of outline k from y0 to y1, less the rectangles in <paramref name="holes"/> (x0, z0, x1, z1).</summary>
    private static void FloorOf(Part p, int k, double y0, double y1, string color, (double X0, double Z0, double X1, double Z1)[] holes, double from = double.MinValue)
    {
        var cuts = holes.SelectMany(h => new[] { h.Z0, h.Z1 }).Append(from).ToArray();
        foreach (var (z0, z1, w, _) in Strips(k, cuts))
        {
            if (z1 <= from + 1e-6)
                continue;
            double xa = Cx - w + 0.03, xb = Cx + w - 0.03;
            var xs = new List<(double, double)> { (xa, xb) };
            foreach (var h in holes)
                if (z0 >= h.Z0 - 1e-6 && z1 <= h.Z1 + 1e-6)
                    xs = [.. xs.SelectMany(r => new[] { (r.Item1, Math.Min(r.Item2, h.X0)), (Math.Max(r.Item1, h.X1), r.Item2) }).Where(r => r.Item2 - r.Item1 > 0.05)];
            foreach (var (a, b) in xs)
                Solid(p, a, y0, z0, b, y1, z1, color, "tread plate");
        }
    }

    /// <summary>The hidden walls standing for the hull round outline k from y0 to y1 (the sides, the stern, and the front unless it
    /// is left to another part), each strip reaching out past its wider end so a body can't slip between two.</summary>
    private static void HullWalls(Part p, int k, double y0, double y1, bool front)
    {
        var st = Strips(k);
        foreach (var (z0, z1, narrow, wide) in st)
            foreach (var s in new[] { -1, 1 })
                Hidden(p, Cx + s * (narrow - 0.02), y0, z0, Cx + s * (wide + 0.1), y1, z1);
        var (f0, _, _, fw) = st[0];
        var (_, b1, bw, _) = st[^1];
        if (front)
            Hidden(p, Cx - fw - 0.1, y0, f0 - 0.1, Cx + fw + 0.1, y1, f0 + 0.02);
        Hidden(p, Cx - bw - 0.1, y0, b1 - 0.02, Cx + bw + 0.1, y1, b1 + 0.1);
    }

    /// <summary>A plain socket on a face (Sx metres).</summary>
    private static void Sock(Part p, string name, double x, double y, double z, string dir) => p.Sock(name, P(x, y, z), dir);

    /// <summary>A partition from (x0, z0) to (x1, z1) (one of them equal: a run along an axis, 0.25 m thick about the line), from y0 to
    /// y1, with doorways (along the run: from, to, height over y0) left open.</summary>
    private static void Partition(Part p, double x0, double z0, double x1, double z1, double y0, double y1, string color, string finish, params (double A, double B, double Hgt)[] doors)
    {
        var alongX = Math.Abs(z1 - z0) < 1e-6;
        var a0 = alongX ? Math.Min(x0, x1) : Math.Min(z0, z1);
        var a1 = alongX ? Math.Max(x0, x1) : Math.Max(z0, z1);
        var c = alongX ? z0 : x0;
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
        // A dark kick strip along the bottom of each face (the low paint), drawn only.
        foreach (var s in new[] { -1, 1 })
        {
            var face = c + s * 0.13;
            at = a0;
            foreach (var (da, db, _) in doors.OrderBy(d => d.A).Append((a1, a1, 0)))
            {
                if (da - at > 0.05)
                {
                    if (alongX)
                        Detail(p, at, y0, Math.Min(face, face + s * 0.012), da, y0 + 0.9, Math.Max(face, face + s * 0.012), WallLow, "matte paint");
                    else
                        Detail(p, Math.Min(face, face + s * 0.012), y0, at, Math.Max(face, face + s * 0.012), y0 + 0.9, da, WallLow, "matte paint");
                }
                at = db;
            }
        }
    }

    private static Part Big(string name, string desc) => new(name, Small, "Rust bucket", desc, role: "frame", tool: "welder", material: "steel", color: HullPaint, group: Group);

    // ---- the keel: the lower deck

    private static Part Keel()
    {
        var p = Big("Rust Bucket Keel", "The rust bucket's lower deck: hold, engine room, belly and lower hull. The other big pieces go on it.");
        // The belly (flat), the bilge sides up to the chine, the lower sides up to the deck line, the stern's lower half.
        Plate(p, [Pt(0, B, -1), Pt(1, B, -1), Pt(2, B, -1), Pt(3, B, -1), Pt(3, B, 1), Pt(2, B, 1), Pt(1, B, 1), Pt(0, B, 1)], Belly, "dirty steel");
        for (var r = 0; r < 3; r++)
        {
            Band(p, r, r + 1, B, Belly, "dirty steel", paint: false);
            Band(p, r, r + 1, L, r == 1 ? HullPaint : Patch, paint: r == 1);
        }
        End(p, 0, B, Belly, "dirty steel", paint: false);
        End(p, 3, B, Belly, "dirty steel", paint: false);
        End(p, 3, L, HullPaint);
        // The chin's cheeks either side of the ramp (the ramp is the rest of the chin: Rust Bucket Ramp).
        foreach (var s in new[] { -1, 1 })
        {
            var lo = Pt(0, L, s);
            var hi = Pt(0, D, s);
            double[] loIn = [lo[0] - s * 0.1, lo[1], lo[2]], hiIn = [hi[0] - s * 0.1, hi[1], hi[2]];
            Quad(p, loIn, lo, hi, hiIn, Patch);
        }
        // Paint: a stripe along the waist, a few replacement panels, rust bleeding from the seams, on the flared sides.
        foreach (var s in new[] { -1, 1 })
        {
            Daub(p, L, s, 0.62, 0.7, 3.1, 13.9, Stripe, "worn paint");
            Daub(p, L, s, 0.12, 0.52, 5.2, 7.0, Patch2, "rusty paint");
            Daub(p, L, s, 0.08, 0.38, 10.4, 11.1, Rust, "rust");
            Daub(p, L, s, 0.76, 0.96, 8.6, 9.9, Patch, "rusty paint");
            foreach (var z in new[] { 6.0, 9.0, 12.0 }) // seams
                Daub(p, L, s, 0.02, 0.98, z - 0.02, z + 0.02, Frame, "dirty steel", 0.016);
        }
        // The floor, the hidden hull round it (the ramp shuts the front), the engine room's bulkhead with its doorway.
        FloorOf(p, L, 0.0, F0, Floor, []);
        HullWalls(p, L, F0, F1, front: false);
        Partition(p, Cx - InLow + 0.01, 12.0, Cx + InLow - 0.01, 12.0, F0, F1, Wall, "wall panelling", (4.25, 5.75, 2.25));
        Fillers(p, L, 12.0, F0, F1, Wall);
        // Under the upper deck: frames across the hold's ceiling, every 2 m (drawn only, a little under the deck).
        foreach (var z in new[] { 4.0, 6.0, 8.0, 10.0 })
            Detail(p, Cx - 4.95, F1 - 0.22, z - 0.08, z is 8.0 or 10.0 ? 8.2 : Cx + 4.95, F1 - 0.012, z + 0.08, Frame, "dirty steel");
        // Sockets: the deck (on the bulkhead's lintel), the ramp's hinge line, the stair's foot, the engine, the pods, the legs.
        Sock(p, "deck", 5.0, F1, 12.0, "+y");
        Sock(p, "ramp", Cx, 0.1, RampHingeZ, "-z");
        Hidden(p, Cx - 0.1, -0.1, RampHingeZ, Cx + 0.1, F0, -0.3); // (what the ramp's socket is on)
        Sock(p, "stair", 7.875, F0, 8.375, "+y");
        Sock(p, "engine", Cx, F0, 14.2, "+y");
        foreach (var (z, i) in new[] { (4.5, 0), (7.5, 1), (10.5, 2) })
            Sock(p, $"shelfL{i}", Cx - InLow, F0 + 0.125, z, "+x");
        Sock(p, "rackR", Cx + InLow, F0 + 0.9, 5.0, "-x");
        Sock(p, "lockerR", Cx + InLow, F0 + 0.125, 6.5, "-x");
        Sock(p, "crate0", 2.4, F0, 10.6, "+y");
        Sock(p, "crate1", 3.2, F0, 10.6, "+y");
        Sock(p, "panelEng", 1.2, F0 + 0.6, 12.125, "+z");
        Sock(p, "boardEng", 8.8, F0 + 0.9, 12.125, "+z");
        Sock(p, "pipesEngL", 2.4, F0 + 2.6, 12.125, "+z");
        Sock(p, "battery", 7.3, F0 + 2.2, 12.125, "+z");
        Sock(p, "switchHoldL", Cx - InLow, F0 + 1.3, 7.2, "+x");
        Sock(p, "switchHoldR", Cx + InLow, F0 + 1.3, 7.4, "-x");
        Sock(p, "switchEng", 6.3, F0 + 1.3, 12.125, "+z");
        foreach (var (z, i) in new[] { (3.6, 0), (7.6, 1) })
        {
            Sock(p, $"lampHoldL{i}", Cx - InLow, 2.5, z + 1.4, "+x");
            Sock(p, $"lampHoldR{i}", Cx + InLow, 2.5, z + 0.4, "-x");
        }
        Sock(p, "lampEngL", Cx - InLow, 2.5, 13.2, "+x");
        Sock(p, "lampEngR", Cx + InLow, 2.5, 13.2, "-x");
        foreach (var s in new[] { -1, 1 })
        {
            Sock(p, s < 0 ? "podL" : "podR", Cx + s * 4.75, 1.4, 14.5, s < 0 ? "-x" : "+x");
            Sock(p, s < 0 ? "saddleL" : "saddleR", Cx + s * 4.75, SadY, SadZ, s < 0 ? "-x" : "+x");
        }
        foreach (var (x, z, n) in new[] { (Cx - 2.6, 4.6, "legFL"), (Cx + 2.6, 4.6, "legFR"), (Cx - 2.6, 12.8, "legBL"), (Cx + 2.6, 12.8, "legBR") })
            Sock(p, n, x, -0.6, z, "-y");
        Hidden(p, Cx - 3.0, -0.6, 4.2, Cx + 3.0, -0.0, 13.2); // (the belly's keel, under the floor: the legs' sockets are on it)
        return p.Stats(42000, 40000, 30, 20.0);
    }

    // ---- the ramp: the chin, an event door that swings down to the ground

    private const double RampHingeZ = -0.4; // where the chin's plane crosses the lower floor's top

    private static Part Ramp()
    {
        // Shut it is the chin: a plate from the hinge (the lower floor's top, z RampHingeZ) up the chin's plane to the deck line,
        // between the cheeks. It turns a quarter about x through the hinge, forward and down, to lie 22 degrees below level with
        // its tip on the ground. Its hidden boxes are level treads in the open pose, turned back to the shut one.
        var p = new Part("Rust Bucket Ramp", Small, "Rust bucket", "The rust bucket's chin: F lowers it as a ramp to the ground and raises it again.", role: "door", tool: "welder", material: "steel", color: Patch, group: Group);
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

    // ---- the deck: the upper deck

    private static readonly (double X0, double Z0, double X1, double Z1) StairHole = (7.75, 8.25, 10.2, 11.75);

    private static Part DeckPart()
    {
        var p = Big("Rust Bucket Deck", "The rust bucket's upper deck: cockpit, cabins, hydroponics and galley, with the canopy.");
        // The upper sides: canopy glass from the nose to the shoulder and across the front, painted plates aft.
        Band(p, 0, 1, D, GlassTint, glass: true, paint: false);
        End(p, 0, D, GlassTint, glass: true, paint: false);
        Band(p, 1, 2, D, HullPaint);
        Band(p, 2, 3, D, Patch, paint: false);
        End(p, 3, D, HullPaint);
        // The canopy's frame: bars along the glass's edges and one up the middle of each pane.
        foreach (var s in new[] { -1, 1 })
        {
            double[] side = [s * 0.012, 0, -0.004], top = [s * 0.008, 0.01, 0];
            Bar(p, Pt(0, D, s), Pt(0, U, s), side);
            Bar(p, Pt(1, D, s), Pt(1, U, s), side);
            Bar(p, Mid(Pt(0, D, s), Pt(1, D, s)), Mid(Pt(0, U, s), Pt(1, U, s)), side);
            Bar(p, Pt(0, U, s), Pt(1, U, s), top);
        }
        Bar(p, Pt(0, U, -1), Pt(0, U, 1), [0, 0.018, -0.008]);
        Bar(p, Mid(Pt(0, D, -1), Pt(0, D, 1)), Mid(Pt(0, U, -1), Pt(0, U, 1)), [0, 0, -0.012]);
        // Paint on the sides aft of the shoulder: the stripe, patches, seams and a porthole in each cabin.
        foreach (var s in new[] { -1, 1 })
        {
            Daub(p, D, s, 0.1, 0.15, 3.4, 13.9, Stripe, "worn paint");
            Daub(p, D, s, 0.25, 0.68, 11.2, 12.9, Patch2, "rusty paint");
            Daub(p, D, s, 0.45, 0.8, 5.0, 6.1, Rust, "rust");
            foreach (var z in new[] { 6.6, 9.0, 12.0 })
                Daub(p, D, s, 0.02, 0.98, z - 0.02, z + 0.02, Frame, "dirty steel", 0.016);
            foreach (var z in new[] { 5.6, 10.5, 13.3 })
            {
                Daub(p, D, s, 0.36, 0.58, z - 0.32, z + 0.32, Frame, "dirty steel", 0.016);
                Daub(p, D, s, 0.39, 0.55, z - 0.24, z + 0.24, "#d8c890", "glass", 0.024, glow: true);
            }
        }
        // The floor (the stair's opening left out), the hidden hull from the eaves' outline (the canopy leans in over the cockpit).
        FloorOf(p, D, F1, F1T, Deck, [StairHole]);
        HullWalls(p, U, F1T, Ceil, front: true);
        // The cockpit's dashboard under the glass, in front of where anyone stands: a raked desk with dials and screens.
        Solid(p, 2.9, F1T, 0.55, 7.1, F1T + 0.7, 1.35, Frame, "dirty steel");
        Detail(p, 3.0, F1T + 0.7, 0.75, 7.0, F1T + 0.86, 1.3, "#3a3d3f", "plastic");
        foreach (var (x, c) in new[] { (3.5, "#5fd06a"), (4.6, "#e0a030"), (5.4, "#5fd06a"), (6.5, "#d04030") })
            Detail(p, x - 0.3, F1T + 0.87, 0.85, x + 0.3, F1T + 0.89, 1.2, c, glow: true);
        foreach (var x in new[] { 4.1, 5.9 })
            Detail(p, x - 0.05, F1T + 0.7, 1.3, x + 0.05, F1T + 1.25, 1.36, Frame, "dirty steel"); // (the yokes' columns)
        // Partitions: the cockpit's back wall, the corridor's walls, the cabins' ends, the galley's end; doorways 1.5 by 2.25 m.
        const double Dh = 2.25;
        Partition(p, Cx - InHigh + 0.01, 4.0, Cx + InHigh - 0.01, 4.0, F1T, Ceil, Wall, "wall panelling", (4.25, 5.75, Dh));
        Partition(p, 4.0, 4.125, 4.0, 16.04, F1T, Ceil, Wall, "wall panelling", (5.25, 6.75, Dh), (9.25, 10.75, Dh));
        Partition(p, 6.0, 4.125, 6.0, 7.875, F1T, Ceil, Tile, "ceramic tiles", (5.25, 6.75, Dh));
        Partition(p, 6.0, 12.125, 6.0, 16.04, F1T, Ceil, Wall, "wall panelling", (13.25, 14.75, Dh));
        Partition(p, Cx - InHigh + 0.01, 8.0, 3.875, 8.0, F1T, Ceil, Wall, "wall panelling");
        Partition(p, 6.125, 8.0, Cx + InHigh - 0.01, 8.0, F1T, Ceil, Tile, "ceramic tiles");
        Partition(p, 6.125, 12.0, Cx + InHigh - 0.01, 12.0, F1T, Ceil, Wall, "wall panelling");
        Fillers(p, D, 4.0, F1T, Ceil, Wall);
        Fillers(p, D, 8.0, F1T, Ceil, Wall, port: true, starboard: false);
        Fillers(p, D, 8.0, F1T, Ceil, Tile, port: false, starboard: true);
        Fillers(p, D, 12.0, F1T, Ceil, Wall, port: false, starboard: true);
        // The stair's rail along the opening's inboard side, open at its head (aft) to step off into the hall.
        Solid(p, 7.69, F1T, 8.25, 7.75, F1T + 1.0, 10.6, Frame, "steel");
        Detail(p, 7.65, F1T + 0.95, 8.25, 7.79, F1T + 1.05, 10.6, Stripe, "hazard paint");
        // Sockets: the keel (under the floor, over the bulkhead's lintel), the roof (on the corridor wall's top), the cabins'
        // furniture, the doors' jambs, the seats, the lamps.
        Sock(p, "keel", 5.0, F1, 12.0, "-y");
        Sock(p, "roof", 4.0, Ceil, 11.5, "+y");
        Sock(p, "seatL", 4.0, F1T, 2.45, "+y");
        Sock(p, "seatR", 6.0, F1T, 2.45, "+y");
        Sock(p, "module", Cx, F1T, 1.6, "+y"); // (on the floor in the footwell, under the dashboard's overhang)
        Sock(p, "chart", 7.4, F1T + 0.475, 3.875, "-z");
        Sock(p, "bed", Cx - InHigh, F1T + 0.125, 6.0, "+x");
        Sock(p, "locker", 3.2, F1T + 0.125, 4.125, "+z");
        Sock(p, "desk", 1.3, F1T + 0.125, 7.875, "-z");
        Sock(p, "shower", 9.2, F1T + 0.125, 4.125, "+z");
        Sock(p, "basin", Cx + InHigh, F1T + 0.125, 6.1, "-x");
        Sock(p, "toilet", Cx + InHigh, F1T + 0.125, 7.3, "-x");
        foreach (var (z, i) in new[] { (9.25, 0), (11.35, 1) })
            Sock(p, $"growL{i}", Cx - InHigh, F1T + 0.125, z, "+x");
        Sock(p, "growM0", 3.875, F1T + 0.125, 12.0, "-x");
        Sock(p, "growM1", 3.875, F1T + 0.125, 14.1, "-x");
        Sock(p, "stove", Cx + InHigh, F1T + 0.45, 12.7, "-x");
        Sock(p, "table", 8.0, F1T + 0.375, 12.125, "+z");
        Sock(p, "cabinet", Cx + InHigh, F1T + 0.85, 13.6, "-x");
        // Doors: each doorway's jamb, the hinge side, at the opening's mid height.
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
        return p.Stats(26000, 30000, 25, 20.0);
    }

    private static double[] Mid(double[] a, double[] b) => [(a[0] + b[0]) / 2, (a[1] + b[1]) / 2, (a[2] + b[2]) / 2];

    /// <summary>A frame bar from a to b: a thin plate 8 cm wide, moved by <paramref name="shift"/> to stand proud of the glass.</summary>
    private static void Bar(Part p, double[] a, double[] b, double[] shift)
    {
        a = [a[0] + shift[0], a[1] + shift[1], a[2] + shift[2]];
        b = [b[0] + shift[0], b[1] + shift[1], b[2] + shift[2]];
        var d = Unit(Sub(b, a));
        double[] up = Math.Abs(d[1]) < 0.9 ? [0, 1, 0] : [1, 0, 0];
        var side = Unit(Cross(d, up));
        double[] off(double[] q, double s) => [q[0] + side[0] * s, q[1] + side[1] * s, q[2] + side[2] * s];
        Plate(p, [off(a, -0.04), off(a, 0.04), off(b, 0.04), off(b, -0.04)], Frame, "dirty steel");
    }

    // ---- the roof

    private static Part Roof()
    {
        var p = Big("Rust Bucket Roof", "The rust bucket's roof: eaves, the radiator spine and the aerial, over the upper deck's ceiling.");
        Plate(p, [Pt(0, R, -1), Pt(1, R, -1), Pt(2, R, -1), Pt(3, R, -1), Pt(3, R, 1), Pt(2, R, 1), Pt(1, R, 1), Pt(0, R, 1)], RoofDark, "dirty steel");
        for (var r = 0; r < 3; r++)
            Band(p, r, r + 1, U, r == 1 ? HullPaint : Patch, paint: r == 1);
        End(p, 0, U, Patch);
        End(p, 3, U, HullPaint);
        // The ceiling under it, at the eaves' outline: what the partitions meet.
        foreach (var (z0, z1, w, _) in Strips(U))
            Solid(p, Cx - w + 0.03, Ceil, z0, Cx + w - 0.03, Ceil + 0.06, z1, "#8f8a7e", "wall panelling");
        // The radiator spine: a raked housing with fins, and the aerial mast aft of it.
        Quad(p, P(3.9, 6.8, 6.0), P(6.1, 6.8, 6.0), P(5.8, 7.35, 6.6), P(4.2, 7.35, 6.6), RoofDark, "dirty steel");
        Quad(p, P(4.2, 7.35, 11.4), P(5.8, 7.35, 11.4), P(6.1, 6.8, 12.0), P(3.9, 6.8, 12.0), RoofDark, "dirty steel");
        Solid(p, 4.2, 6.8, 6.6, 5.8, 7.35, 11.4, RoofDark, "dirty steel");
        foreach (var s in new[] { -1, 1 })
            for (var z = 6.8; z < 11.3; z += 0.4)
                Detail(p, Cx + s * 0.8, 6.85, z, Cx + s * 1.05, 7.25, z + 0.06, "#2d3033", "steel");
        Solid(p, 5.95, 6.8, 12.6, 6.05, 8.9, 12.7, Frame, "steel");
        Detail(p, 5.93, 8.9, 12.58, 6.07, 9.04, 12.72, "#d03020", glow: true);
        Solid(p, 3.6, 6.8, 13.0, 4.4, 7.0, 13.6, RoofDark, "dirty steel"); // (a hatch's coaming, shut)
        Sock(p, "deck", 4.0, Ceil, 11.5, "-y");
        return p.Stats(9000, 12000, 20, 12.0);
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

    // The stern pods' and the saddle tanks' middles and sizes (Sx metres), the main drives' and lift jets' pushes (N).
    private const double PodX = 6.1, PodY = 1.4, PodR = 0.9, SadX = 5.8, SadY = 1.0, SadR = 0.65, SadZ = 5.6, PodLiftZ = 14.2;
    private const double DriveForce = 600000, LiftForce = 800000, JetBurn = 3.0; // (l/s per MN, as the shipped jets)

    private static Part Pod(int side)
    {
        var p = new Part(side < 0 ? "Rust Bucket Pod L" : "Rust Bucket Pod R", Small, "Rust bucket", "One of the rust bucket's engine pods: a patched nacelle full of fuel, its main drive at the back and a lift jet under it.", role: "fueltank", tool: "welder", material: "steel", color: HullPaint, group: Group);
        double cx = Cx + side * PodX, cy = PodY, r = PodR;
        Nacelle(p, cx, cy, [(10.4, 0.35), (11.3, r), (16.6, r), (17.2, 0.75)], 1, "dirty steel", "soot");
        // The pylons to the hull, and the hidden box for the nacelle.
        double inner = Cx + side * 4.75, outer = cx - side * r * 0.9;
        foreach (var (z0, z1) in new[] { (12.2, 13.4), (15.0, 16.0) })
            Solid(p, Math.Min(inner, outer), 1.15, z0, Math.Max(inner, outer), 1.65, z1, Frame, "dirty steel");
        Hidden(p, cx - r * 0.9, cy - r * 0.9, 11.3, cx + r * 0.9, cy + r * 0.9, 17.2);
        Sock(p, "hull", inner, 1.4, 14.5, side < 0 ? "+x" : "-x");
        Sock(p, "drive", cx, cy, 17.2, "+z");
        Sock(p, "lift", cx, cy - r * 0.9, PodLiftZ, "-y");
        p.Set(("fuelTank", Kw(("capacity", 2500))), ("ports", Py.L(BigDriveParts.PipePort("Fuel out", "out", [.. P(cx + side * r * 0.9, cy, 13.8).Cast<object>()], side < 0 ? "-x" : "+x", "fuel"))));
        return p.Stats(3500, 6000, 25, 10.0);
    }

    /// <summary>A saddle tank on the hull's flank forward: fuel for the forward lift jet slung under it.</summary>
    private static Part Saddle(int side)
    {
        var p = new Part(side < 0 ? "Rust Bucket Saddle Tank L" : "Rust Bucket Saddle Tank R", Small, "Rust bucket", "A saddle tank on the rust bucket's flank: fuel for the forward lift jet under it.", role: "fueltank", tool: "welder", material: "steel", color: HullPaint, group: Group);
        double cx = Cx + side * SadX, cy = SadY, r = SadR;
        Nacelle(p, cx, cy, [(3.7, 0.22), (4.4, r), (7.3, r), (8.0, 0.3)], 1, "dirty steel", "dirty steel");
        // Two straps round it, and the pylon to the hull on the socket.
        foreach (var z in new[] { 4.9, 6.6 })
            Detail(p, cx - r * 0.95, cy - r * 0.95, z - 0.05, cx + r * 0.95, cy + r * 0.95, z + 0.05, Frame, "dirty steel");
        double inner = Cx + side * 4.75, outer = cx - side * r * 0.7;
        Solid(p, Math.Min(inner, outer), cy - 0.2, SadZ - 0.5, Math.Max(inner, outer), cy + 0.2, SadZ + 0.5, Frame, "dirty steel");
        Hidden(p, cx - r * 0.9, cy - r * 0.9, 4.4, cx + r * 0.9, cy + r * 0.9, 7.3);
        Sock(p, "hull", inner, cy, SadZ, side < 0 ? "+x" : "-x");
        Sock(p, "lift", cx, cy - r * 0.9, SadZ, "-y");
        p.Set(("fuelTank", Kw(("capacity", 1000))), ("ports", Py.L(BigDriveParts.PipePort("Fuel out", "out", [.. P(cx + side * r * 0.9, cy, 6.4).Cast<object>()], side < 0 ? "-x" : "+x", "fuel"))));
        return p.Stats(1200, 3000, 20, 8.0);
    }

    /// <summary>An octagon's corner (part-local, about the origin) at radius rr, height y in the xz plane (a ring across y).</summary>
    private static double[] RingY(double rr, double y, int i)
    {
        var a = Math.PI / 8 + i * Math.PI / 4;
        return [Py.Round(rr * Math.Cos(a), 4), y, Py.Round(rr * Math.Sin(a), 4)];
    }

    /// <summary>The same along z (a ring across z).</summary>
    private static double[] RingZ(double rr, double z, int i)
    {
        var a = Math.PI / 8 + i * Math.PI / 4;
        return [Py.Round(rr * Math.Cos(a), 4), Py.Round(rr * Math.Sin(a), 4), z];
    }

    /// <summary>A main drive: the bell on a pod's back, a glow deep inside. Origin on the pod's end, the bell aft along +z.</summary>
    private static Part Drive()
    {
        var p = new Part("Rust Bucket Drive", Small, "Rust bucket", $"The rust bucket's main drive: {DriveForce / 1000:0} kN from the pod's fuel. Pushes away from its bell.", role: "thruster", tool: "wrench", material: "steel", color: "#2a2624", group: Group);
        for (var i = 0; i < 8; i++)
            Quad(p, RingZ(0.55, 0.0, i), RingZ(0.55, 0.0, i + 1), RingZ(0.72, 0.7, i + 1), RingZ(0.72, 0.7, i), "#2a2624", "soot");
        Plate(p, [.. Enumerable.Range(0, 8).Select(i => RingZ(0.5, 0.1, i))], "#ff8a3a", glow: true);
        p.Add(Box([-0.5, -0.5, 0], [0.5, 0.5, 0.55], hidden: true));
        p.Sock("front", [0, 0, 0], "-z");
        p.Set(("thruster", Kw(("force", DriveForce), ("spool", 1.2), ("exhaust", "+z"), ("at", Py.L(0, 0, 0.7)), ("burnRate", Py.Round(DriveForce / 1e6 * JetBurn, 4)))), ("ports", Py.L(BigDriveParts.PipePort("Fuel in", "in", [0.5, 0, 0.3], "+x", "fuel"))));
        return p.Stats(1800, 4000, 12, 6.0);
    }

    /// <summary>A lift jet: a shroud under a tank, its nozzle pointing down. Origin on the tank's underside.</summary>
    private static Part LiftJet()
    {
        var p = new Part("Rust Bucket Lift Jet", Small, "Rust bucket", $"A lift jet: {LiftForce / 1000:0} kN straight down from the tank it hangs under.", role: "thruster", tool: "wrench", material: "steel", color: Frame, group: Group);
        // The shroud (its top a little up inside the tank), louvres down its sides, a hazard band.
        p.Add(Box([-0.46, -0.26, -0.5], [0.46, 0.0, 0.5], Frame, shape: Kw(("finish", "dirty steel"))));
        foreach (var x in new[] { -0.3, -0.1, 0.1, 0.3 })
            foreach (var s in new[] { -1, 1 })
                p.Add(Box([x - 0.04, -0.2, s * 0.5 - 0.012], [x + 0.04, -0.04, s * 0.5 + 0.012], "#1c1b1a", detail: true));
        p.Add(Box([-0.475, -0.25, -0.4], [0.475, -0.2, 0.4], Stripe, detail: true, shape: Kw(("finish", "hazard paint"))));
        // The nozzle, flaring down, sooted, with the glow up inside it.
        for (var i = 0; i < 8; i++)
            Quad(p, RingY(0.34, -0.26, i), RingY(0.34, -0.26, i + 1), RingY(0.44, -0.55, i + 1), RingY(0.44, -0.55, i), "#2a2624", "soot");
        Plate(p, [.. Enumerable.Range(0, 8).Select(i => RingY(0.3, -0.3, i))], "#ff8a3a", glow: true);
        p.Add(Box([-0.44, -0.55, -0.44], [0.44, -0.26, 0.44], hidden: true));
        p.Sock("top", [0, 0, 0], "+y");
        p.Set(("thruster", Kw(("force", LiftForce), ("spool", 1.0), ("exhaust", "-y"), ("at", Py.L(0, -0.55, 0)), ("burnRate", Py.Round(LiftForce / 1e6 * JetBurn, 4)))), ("ports", Py.L(BigDriveParts.PipePort("Fuel in", "in", [-0.46, -0.13, 0], "-x", "fuel"))));
        return p.Stats(2500, 4000, 12, 6.0);
    }

    // ---- the legs

    private static Part Leg()
    {
        var p = new Part("Rust Bucket Leg", Small, "Rust bucket", "A landing leg: a raked strut on a broad pad.", role: "frame", tool: "welder", material: "steel", color: Frame, group: Group);
        // Origin at the socket on the belly; the pad 0.45 m down, the strut raked out a little.
        Quad(p, [-0.12, 0, -0.12], [0.12, 0, -0.12], [0.18, -0.4, 0.0], [-0.06, -0.4, 0.0], Frame, "dirty steel");
        Quad(p, [-0.12, 0, 0.12], [0.12, 0, 0.12], [0.18, -0.4, 0.24], [-0.06, -0.4, 0.24], Frame, "dirty steel");
        p.Add(Box([-0.1, -0.4, -0.1], [0.1, 0.0, 0.1], "#3a3836", shape: Kw(("finish", "dirty steel"))));
        p.Add(Box([-0.4, -0.5, -0.4], [0.4, -0.4, 0.4], "#2c2a28", shape: Kw(("finish", "rubber"))));
        p.Add(Box([-0.12, -0.42, -0.12], [0.12, -0.38, 0.12], Stripe, detail: true, shape: Kw(("finish", "hazard paint"))));
        p.Sock("top", [0, 0, 0], "+y");
        return p.Stats(400, 1500, 20, 4.0);
    }

    // ---- fittings the ship needed that weren't in the game

    private static Part Fitting(string name, string desc, string role = "frame") =>
        new(name, Small, "Ship fittings", desc, role: role, tool: "wrench", material: "steel", color: "#c8ccc6", group: Group);

    /// <summary>Furniture sockets: on the floor, against a wall (its back at local -z, 0.125 m up, as the colony's furniture).</summary>
    private static void FurnitureSockets(Part p, double back, double top)
    {
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Sock("top", [0, top, 0], "+y");
        p.Sock("wall", [0, 0.125, -back], "-z");
    }

    private static Part Toilet()
    {
        var p = Fitting("Ship Toilet", "A steel toilet with a vacuum flush. Rinse before use.");
        p.Add(Box([-0.2, 0, -0.35], [0.2, 0.4, 0.15], "#c8ccc6", shape: Kw(("finish", "chrome"))));
        p.Add(Box([-0.22, 0.4, -0.3], [0.22, 0.44, 0.2], "#30302e", detail: true, shape: Kw(("finish", "plastic"))));
        p.Add(Box([-0.22, 0.4, -0.35], [0.22, 0.85, -0.2], "#c8ccc6", shape: Kw(("finish", "chrome"))));
        p.Add(Box([0.12, 0.7, -0.2], [0.2, 0.76, -0.12], "#d03020", detail: true, shape: Kw(("finish", "plastic"))));
        FurnitureSockets(p, 0.35, 0.85);
        return p.Stats(30, 120, 4, 2.0);
    }

    private static Part Basin()
    {
        var p = Fitting("Ship Basin", "A small steel basin with a mirror. The cold tap works.");
        p.Add(Box([-0.3, 0.75, -0.22], [0.3, 0.9, 0.18], "#c8ccc6", shape: Kw(("finish", "chrome"))));
        p.Add(Box([-0.05, 0, -0.22], [0.05, 0.75, -0.12], "#8a8e8a", shape: Kw(("finish", "steel"))));
        p.Add(Box([-0.25, 0.86, -0.17], [0.25, 0.91, 0.13], "#5c6a70", detail: true, shape: Kw(("finish", "steel"))));
        p.Add(Box([-0.03, 0.9, -0.2], [0.03, 1.05, -0.12], "#c8ccc6", detail: true, shape: Kw(("finish", "chrome"))));
        p.Add(Box([-0.28, 1.15, -0.22], [0.28, 1.7, -0.19], "#a8c0c8", detail: true, glass: true));
        p.Add(Box([-0.05, 0, -0.22], [0.05, 0.02, -0.12], "#8a8e8a", detail: true));
        FurnitureSockets(p, 0.22, 0.9);
        return p.Stats(20, 80, 3, 2.0);
    }

    private static Part Shower()
    {
        var p = Fitting("Shower Stall", "A one-person shower: a tray, a curtain rail and a head that spits.");
        p.Add(Box([-0.45, 0, -0.45], [0.45, 0.08, 0.45], "#c8ccc6", shape: Kw(("finish", "ceramic tiles"))));
        p.Add(Box([-0.45, 0.08, -0.45], [-0.42, 2.1, 0.45], "#c8ccc6", shape: Kw(("finish", "ceramic tiles"))));
        p.Add(Box([0.42, 0.08, -0.45], [0.45, 2.1, 0.45], "#c8ccc6", shape: Kw(("finish", "ceramic tiles"))));
        p.Add(Box([-0.42, 2.06, 0.4], [0.42, 2.1, 0.44], "#8a8e8a", detail: true, shape: Kw(("finish", "chrome"))));
        p.Add(Box([-0.42, 0.3, 0.41], [-0.1, 2.06, 0.43], "#9fb8a8", detail: true, shape: Kw(("finish", "fabric"))));
        p.Add(Box([-0.06, 1.9, -0.43], [0.06, 1.98, -0.3], "#c8ccc6", detail: true, shape: Kw(("finish", "chrome"))));
        p.Add(Box([-0.1, 0.081, -0.1], [0.1, 0.095, 0.1], "#30302e", detail: true));
        FurnitureSockets(p, 0.45, 2.1);
        return p.Stats(60, 150, 3, 3.0);
    }

    private static Part GrowRack()
    {
        var p = Fitting("Grow Rack 2 m", "Three trays of hydroponic greens under lamps, fed from a drip line. Lettuce, mostly.");
        p.Add(Box([-1.0, 0, -0.3], [-0.95, 2.0, 0.3], "#8a8e8a", shape: Kw(("finish", "steel"))));
        p.Add(Box([0.95, 0, -0.3], [1.0, 2.0, 0.3], "#8a8e8a", shape: Kw(("finish", "steel"))));
        foreach (var y in new[] { 0.3, 0.95, 1.6 })
        {
            p.Add(Box([-0.95, y, -0.28], [0.95, y + 0.1, 0.28], "#e0e0d8", shape: Kw(("finish", "plastic"))));
            for (var i = 0; i < 6; i++)
            {
                var x = -0.8 + i * 0.32;
                var h = 0.16 + 0.05 * ((i * 7 + (int)(y * 10)) % 3);
                p.Add(Box([x - 0.12, y + 0.1, -0.2], [x + 0.12, y + 0.1 + h, 0.2], i % 2 == 0 ? Leaf : Grow, detail: true, shape: Kw(("finish", "fabric"))));
            }
            p.Add(Box([-0.9, y + 0.5, -0.08], [0.9, y + 0.53, 0.08], "#f0b0f0", detail: true, shape: Kw(("glow", true))));
        }
        p.Add(Box([-0.95, 1.95, -0.3], [0.95, 1.98, -0.26], "#3a6080", detail: true, shape: Kw(("finish", "rubber"))));
        FurnitureSockets(p, 0.3, 2.0);
        return p.Stats(70, 200, 3, 3.0);
    }

    private static Part EngineBlock()
    {
        var p = Fitting("Ship Engine Block", "The rust bucket's main drive core: a fusion bottle older than its pilot, held together with clamps and hope.", role: "frame");
        p.Set(("tool", "welder"));
        p.Add(Box([-1.0, 0, -0.9], [1.0, 0.3, 0.9], "#2c2a28", shape: Kw(("finish", "dirty steel"))));
        // The bottle: an octagonal drum on its side, along z, banded.
        double[] O(double z, double r, int i)
        {
            var a = Math.PI / 8 + i * Math.PI / 4;
            return [r * Math.Cos(a), 1.15 + r * Math.Sin(a), z];
        }
        Prism(p, [.. Enumerable.Range(0, 8).Select(i => O(-0.85, 0.8, i))], [.. Enumerable.Range(0, 8).Select(i => O(0.85, 0.8, i))], "#6a5a48", "rusty paint");
        p.Add(Box([-0.55, 0.3, -0.55], [0.55, 1.95, 0.55], hidden: true));
        foreach (var z in new[] { -0.5, 0.0, 0.5 })
            Prism(p, [.. Enumerable.Range(0, 8).Select(i => O(z - 0.05, 0.84, i))], [.. Enumerable.Range(0, 8).Select(i => O(z + 0.05, 0.84, i))], Stripe, "hazard paint");
        p.Add(Box([-0.2, 1.9, -0.2], [0.2, 2.25, 0.2], "#3a3836", shape: Kw(("finish", "steel"))));
        p.Add(Box([-0.08, 2.25, -0.08], [0.08, 2.6, 0.08], "#8a8e8a", shape: Kw(("finish", "steel"))));
        p.Add(Box([-0.9, 0.6, 0.86], [-0.5, 1.0, 0.9], "#40ff90", detail: true, shape: Kw(("glow", true))));
        p.Add(Box([0.5, 0.6, 0.86], [0.9, 0.7, 0.9], "#ffb030", detail: true, shape: Kw(("glow", true))));
        p.Sock("bottom", [0, 0, 0], "-y");
        return p.Stats(3000, 2500, 30, 12.0);
    }

    // ---- the blueprint

    private static int Paint(BuildingParts.Blueprint bp, int step, string paint, string? finish = null)
    {
        bp.Steps[step]["paint"] = paint;
        if (finish != null)
            bp.Steps[step]["finish"] = finish;
        return step;
    }

    private static int Lamp(BuildingParts.Blueprint bp, int on, string sock, string colour = LampGlow)
    {
        var s = bp.Hang("Bulkhead Light", on, sock, "back");
        bp.Steps[s]["config"] = new PyDict { ["colour"] = colour };
        return s;
    }

    private static void Switch(BuildingParts.Blueprint bp, int on, string sock, params int[] lamps)
    {
        var s = bp.Hang("Bakelite Switch", on, sock, "back");
        bp.Steps[s]["config"] = new PyDict { ["startOn"] = true };
        foreach (var l in lamps)
            bp.Wires.Add((s, l));
    }

    private static BuildingParts.Blueprint Blueprint(Dictionary<string, PyDict> defs)
    {
        var bp = new BuildingParts.Blueprint("Rust Bucket", "Your ship: a patched two-deck tramp with a cockpit, two cabins, hydroponics, a galley, a hold and an engine room.", defs);
        var keel = bp.First("Rust Bucket Keel");
        double[] zero = [0, 0, 0];
        var deck = bp.Put("Rust Bucket Deck", zero, 0);
        bp.Put("Rust Bucket Roof", zero, 0);
        var pods = new[] { bp.Put("Rust Bucket Pod L", zero, 0), bp.Put("Rust Bucket Pod R", zero, 0) };
        var saddles = new[] { bp.Put("Rust Bucket Saddle Tank L", zero, 0), bp.Put("Rust Bucket Saddle Tank R", zero, 0) };
        foreach (var pod in pods)
        {
            bp.Hang("Rust Bucket Drive", pod, "drive", "front");
            bp.Hang("Rust Bucket Lift Jet", pod, "lift", "top");
        }
        var foreLift = saddles.Select(t => bp.Hang("Rust Bucket Lift Jet", t, "lift", "top")).ToArray();
        foreach (var n in new[] { "legFL", "legFR", "legBL", "legBR" })
            bp.Hang("Rust Bucket Leg", keel, n, "top");
        bp.Hang("Rust Bucket Ramp", keel, "ramp", "hang");
        bp.Hang("Steel Wide Stair 3 m", keel, "stair", "b0_0");
        // The hold: shelving down the port side, a tool rack and a locker opposite, two crates by the stair.
        for (var i = 0; i < 3; i++)
            Paint(bp, bp.Hang("Store Shelving 2 m", keel, $"shelfL{i}", "wall"), "#6b6f5a", "worn paint");
        bp.Hang("Tool Rack", keel, "rackR", "back");
        Paint(bp, bp.Hang("Locker", keel, "lockerR", "wall"), "#5d7468", "worn paint");
        bp.Hang("Requisition Crate", keel, "crate0", "-y");
        bp.Hang("Requisition Crate", keel, "crate1", "-y");
        // The engine room.
        bp.Hang("Ship Engine Block", keel, "engine", "bottom");
        bp.Hang("Engineer's Panel", keel, "panelEng", "back");
        bp.Hang("Power Board", keel, "boardEng", "back");
        bp.Hang("Pipe Bank", keel, "pipesEngL", "back");
        Paint(bp, bp.Hang("Battery", keel, "battery", "-z"), "#3a3d3f");
        Switch(bp, keel, "switchHoldL", Lamp(bp, keel, "lampHoldL0"), Lamp(bp, keel, "lampHoldL1"));
        Switch(bp, keel, "switchHoldR", Lamp(bp, keel, "lampHoldR0"), Lamp(bp, keel, "lampHoldR1"));
        Switch(bp, keel, "switchEng", Lamp(bp, keel, "lampEngL"), Lamp(bp, keel, "lampEngR"));
        // The cockpit: the pilot's seat drives through a Driving Module under the dashboard, as the Orbiter's does.
        var pilot = bp.Hang("Seat", deck, "seatL", "bottom");
        bp.Hang("Seat", deck, "seatR", "bottom");
        var module = bp.Hang("Driving Module", deck, "module", "-y");
        bp.Wires.Add((pilot, module));
        bp.Wires.Add((module, foreLift[0]));
        bp.Hang("Chart Table", deck, "chart", "back");
        // The bedroom, the bathroom.
        Paint(bp, bp.Hang("Iron Bed", deck, "bed", "wall"), "#6b4a2e", "worn paint");
        Paint(bp, bp.Hang("Locker", deck, "locker", "wall"), "#8c7b52", "worn paint");
        bp.Hang("Steel Desk", deck, "desk", "wall");
        bp.Hang("Shower Stall", deck, "shower", "wall");
        bp.Hang("Ship Basin", deck, "basin", "wall");
        bp.Hang("Ship Toilet", deck, "toilet", "wall");
        // Hydroponics, the galley.
        foreach (var g in new[] { "growL0", "growL1", "growM0", "growM1" })
            bp.Hang("Grow Rack 2 m", deck, g, "wall");
        bp.Hang("Galley Stove", deck, "stove", "back");
        bp.Hang("Mess Table", deck, "table", "back");
        bp.Hang("Medicine Cabinet", deck, "cabinet", "back");
        // Doors.
        foreach (var d in new[] { "doorCockpit", "doorBed", "doorBath", "doorHydro", "doorGalley" })
            Paint(bp, bp.Hang("Steel Swing Door 1.5x2.25 m", deck, d, "hang"), d == "doorBath" ? "#c9ccc4" : "#5d7468", "worn paint");
        // Lamps: forward and aft switches by the corridor's ends.
        Switch(bp, deck, "switchFwd", Lamp(bp, deck, "lampCockpit"), Lamp(bp, deck, "lampBed"), Lamp(bp, deck, "lampBath"), Lamp(bp, deck, "lampCorrA"));
        Switch(bp, deck, "switchAft", Lamp(bp, deck, "lampHydroA", "#f0c8f0"), Lamp(bp, deck, "lampHydroB", "#f0c8f0"), Lamp(bp, deck, "lampCorrB"), Lamp(bp, deck, "lampGalley"));
        return bp;
    }

    public void Make(PartOutput o)
    {
        List<Part> parts = [Keel(), Ramp(), DeckPart(), Roof(), Pod(-1), Pod(1), Saddle(-1), Saddle(1), Drive(), LiftJet(), Leg(), Toilet(), Basin(), Shower(), GrowRack(), EngineBlock()];
        var defs = new Dictionary<string, PyDict>();
        o.Own(Out);
        foreach (var p in parts)
        {
            var d = p.Json();
            var name = (string)d["name"]!;
            defs.Add(name, d);
            o.File($"{Out}/{FileName(name)}", PyJson.Dumps(d, 2) + "\n");
        }
        BuildingParts.AddPartDefs(o, defs, Out);
        var bp = Blueprint(defs);
        o.File($"{BpOut}/RustBucket.json", PyJson.Dumps(bp.Json(), 2) + "\n");
    }
}
