using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The Cloche, the farmers' greenhouse ship (Karl, 2026-11-21: a picture of a long red farm ship, "all new parts, new hydroponics, new
/// nacelles, new everything", the exterior like for like; he picked the farmers). A long boxy red hull with chamfered top edges and dark
/// ribs wrapped round it every 3.2 m, a dark chassis band and skids under it; two glass barrel-vault greenhouses side by side on its roof,
/// full of grow beds; a cream bus-fronted cab low on the bow with a railed walk on its roof and a balcony across the bow; a railed step
/// each side of the cab with a ladder down; two cream and red water tanks strapped low on each flank; a taller aft block with a louvred
/// vent, a grille, two stacks, masts and a radome on a yellow tower; and a red nacelle each side of it with a cream intake ring.
///
/// Inside, one deck the length of the hull: the entry room behind the cab, the grow hall (three rows of rack towers), the engine room
/// under the aft block. A ladder up through the roof into each greenhouse. Built of welded pieces as the Dibber is: the keel in five
/// lengths, each side in four, the roof in four, the bow, the cab, the balcony, the steps, two bulkheads, the aft block's sides, roof and
/// stern, each greenhouse in three bays, the four tanks and the two nacelles. Ship metres: x across, y up from the ground under the
/// skids, z along (forward is -z); P() moves them by half a cell into a part's frame. Every part is its own (nothing from another ship
/// or the stock fittings). Notes: docs/sandbox/constructs/cloche.md.
/// </summary>
public sealed class ClocheParts : IPartGenerator
{
    public string Name => "cloche_parts";

    private const string Out = "Constructs/Parts/Cloche";
    private const string BpOut = "Constructs/Blueprints";
    private const string Group = "Ship";
    private const string Section = "Cloche greenhouse ship";
    public const string ShipName = "Cloche Greenhouse Ship";
    private const double H = 0.125; // half a cell
    private const double T = 0.04; // a drawn plate's thickness

    // The paint (the farmers' red and cream, the picture's): brick red plating and a darker red roof, dark ribs and chassis, cream trim,
    // yellow rails, grey pipes, glass. Inside: cream walls over an olive dado, a dark deck.
    private const string Red = "#a63a30", Red2 = "#8e3029", Rib = "#2e302e", Chassis = "#242624", Cream = "#e6dcc0", Cream2 = "#d4c7a3",
        GlassC = "#a8d2da", Yellow = "#e3b021", Hazard = "#e8b317", HazDark = "#1f1f1d", Pipe = "#9ea2a0", Grey = "#7d817e",
        Leaf = "#4f9a45", Leaf2 = "#3c7a36", Leaf3 = "#7cba5c", Tray = "#ebe7da", Soil = "#4a3a2c", Wall = "#d6cfb8", WallLow = "#56654a",
        Deck = "#3e403c", CeilC = "#c4bfac", Amber = "#f0a830", RedLamp = "#e0402c", Glow = "#fff1c8", Console = "#262a2d", Fabric = "#6b4a30";

    // ---- the plan (y up from the ground under the skids)

    private const double LineY = 1.325; // (the linings start over the deck plate)
    private const double Hw = 5.0, In = 4.7, Ch = 0.7, Y0 = 0.5, FloorY = 1.3, PlinthY = 1.1, Eave = 5.9, CeilY = 6.0, RoofY = 6.6;
    private const double BowZ = -16.8, BowIn = -16.5, RoofEnd = 9.2, AftZ = 9.5, SternIn = 18.5, SternZ = 18.8;
    private const double AftEave = 8.6, AftCeil = 8.7, AftTop = 9.3;
    private static readonly double[] Seg = [-16.5, -10.8, -4.4, 2.0, 9.5];
    private static readonly double[] Ribs = [-15.6, -12.4, -9.2, -6.0, -2.8, 0.4, 3.6, 6.8];

    // The cab: a bus front on the bow, its floor level with the deck, a walkable roof behind a visor.
    private const double CabX = 2.9, CabZ0 = -22.0, CabY0 = 0.6, CabRoof0 = 4.45, CabRoof1 = 4.6, BalZ = -17.7;
    private const double StepZ0 = -20.6, StepY0 = 1.15;
    private const double DoorX0 = -4.1, DoorX1 = -3.05, DoorTop = 3.4, CabDoor = 0.6;
    private const double FbZ0 = -11.15, FbZ1 = -10.85, PassX = 0.7, PassTop = 3.5;

    // The greenhouses: barrel vaults on the roof, a walkway down the middle of each between two rows of beds.
    private const double GX0 = 0.35, GX1 = 4.05, GWall = 8.0, GRise = 1.3;
    private static readonly double[] Bays = [-14.2, -6.6, 1.0, 8.6];
    private const double HoleX0 = 1.6, HoleX1 = 2.8, HoleZ0 = -8.0, HoleZ1 = -6.9; // (each greenhouse's hatch through the roof: x mirrored)

    // Flying it: eight lift jets under the belly, a drive in each nacelle's tail, each under the 500 kN that needs works.
    private const double LiftForce = 400000, DriveForce = 450000, Burn = 2.5;
    private const string LiftJetName = "Cloche Lift Jet", DriveName = "Cloche Drive";
    private const double NacR = 1.35, NacY = 3.4, NacX = 6.9, NacZ0 = 10.85, NacTail = 17.7;

    // ---- geometry helpers

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

    private static PyDict HullShape(List<double[]> pts, string? finish, bool glow)
    {
        var shape = Kw(("shape", "hull"), ("points", pts.Select(q => (object?)q.Select(x => (object?)x).ToList()).ToList()), ("paintable", false));
        if (glow)
            shape["glow"] = true;
        else if (finish != null)
            shape["finish"] = finish;
        return shape;
    }

    private static double[] Mn(List<double[]> pts) => [.. Enumerable.Range(0, 3).Select(k => pts.Min(q => q[k]))];

    private static double[] Mx(List<double[]> pts) => [.. Enumerable.Range(0, 3).Select(k => pts.Max(q => q[k]))];

    /// <summary>A flat plate through the points (a convex polygon), thick about them: a "hull" shape, drawn only. raw: part-frame points.</summary>
    private static void Plate(Part p, double[][] pts, string color, string finish = "painted plating", bool glass = false, bool glow = false, double thick = T, bool raw = false)
    {
        if (!raw)
            pts = [.. pts.Select(Sh)];
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
        p.Add(Box(Mn(both), Mx(both), color, detail: true, glass: glass, shape: HullShape(both, glass ? null : finish, glow)));
    }

    /// <summary>A convex solid between two outlines (drawn only). raw: part-frame points.</summary>
    private static void Prism(Part p, double[][] a, double[][] b, string color, string finish, bool glow = false, bool raw = false)
    {
        var both = a.Concat(b).Select(q => (raw ? q : Sh(q)).Select(x => Py.Round(x, 4)).ToArray()).ToList();
        p.Add(Box(Mn(both), Mx(both), color, detail: true, shape: HullShape(both, finish, glow)));
    }

    /// <summary>An n-gon about a line along z (flat faces at 0, 90, 180 and 270 degrees for n = 8).</summary>
    private static double[][] RingZ(double cx, double cy, double z, double r, int n = 8) =>
        [.. Enumerable.Range(0, n).Select(i => { var t = (i + 0.5) * 2 * Math.PI / n; return new[] { cx + r * Math.Cos(t), cy + r * Math.Sin(t), z }; })];

    /// <summary>An n-gon about a vertical line.</summary>
    private static double[][] RingY(double cx, double y, double cz, double r, int n = 8) =>
        [.. Enumerable.Range(0, n).Select(i => { var t = (i + 0.5) * 2 * Math.PI / n; return new[] { cx + r * Math.Cos(t), y, cz + r * Math.Sin(t) }; })];

    private static void CylZ(Part p, double cx, double cy, double z0, double z1, double r0, double r1, string col, string fin, int n = 8, bool glow = false, bool raw = false) =>
        Prism(p, RingZ(cx, cy, z0, r0, n), RingZ(cx, cy, z1, r1, n), col, fin, glow, raw);

    private static void CylY(Part p, double cx, double cz, double y0, double y1, double r0, double r1, string col, string fin, int n = 8, bool glow = false, bool raw = false) =>
        Prism(p, RingY(cx, y0, cz, r0, n), RingY(cx, y1, cz, r1, n), col, fin, glow, raw);

    private static double[] Lo(double x0, double y0, double z0, double x1, double y1, double z1, bool raw) =>
        raw ? [Math.Min(x0, x1), Math.Min(y0, y1), Math.Min(z0, z1)] : P(Math.Min(x0, x1), Math.Min(y0, y1), Math.Min(z0, z1));

    private static double[] Hi(double x0, double y0, double z0, double x1, double y1, double z1, bool raw) =>
        raw ? [Math.Max(x0, x1), Math.Max(y0, y1), Math.Max(z0, z1)] : P(Math.Max(x0, x1), Math.Max(y0, y1), Math.Max(z0, z1));

    /// <summary>A box that is drawn and collides.</summary>
    private static void Solid(Part p, double x0, double y0, double z0, double x1, double y1, double z1, string color, string? finish = null, bool raw = false) =>
        p.Add(Box(Lo(x0, y0, z0, x1, y1, z1, raw), Hi(x0, y0, z0, x1, y1, z1, raw), color, shape: finish == null ? null : Kw(("finish", finish))));

    /// <summary>A box that is drawn only.</summary>
    private static void Detail(Part p, double x0, double y0, double z0, double x1, double y1, double z1, string color, string? finish = null, bool glow = false, bool glass = false, bool raw = false)
    {
        PyDict? shape = null;
        if (glow)
            shape = Kw(("glow", true), ("paintable", false));
        else if (finish != null && !glass)
            shape = Kw(("finish", finish));
        p.Add(Box(Lo(x0, y0, z0, x1, y1, z1, raw), Hi(x0, y0, z0, x1, y1, z1, raw), color, detail: true, glass: glass, shape: shape));
    }

    /// <summary>A box that collides only.</summary>
    private static void Hidden(Part p, double x0, double y0, double z0, double x1, double y1, double z1, bool raw = false)
    {
        if (Math.Abs(x1 - x0) < 0.02 || Math.Abs(y1 - y0) < 0.02 || Math.Abs(z1 - z0) < 0.02)
            return;
        p.Add(Box(Lo(x0, y0, z0, x1, y1, z1, raw), Hi(x0, y0, z0, x1, y1, z1, raw), hidden: true));
    }

    private static void Sock(Part p, string name, double x, double y, double z, string dir) => p.Sock(name, P(x, y, z), dir);

    private static double[] Vd(PyDict b, string key) => [.. ((List<object?>)b[key]!).Select(v => Convert.ToDouble(v) + H)];

    private static bool Drawn(PyDict b) => b.Get("detail") is true;

    /// <summary>
    /// A rectangle in the plane of two axes less rectangular holes, as rectangles: cut into bands across the first axis at the holes'
    /// edges, each band then cut along the second axis round the holes in it. Each piece goes to emit (a0, b0, a1, b1).
    /// </summary>
    private static void Less(double a0, double b0, double a1, double b1, (double A0, double B0, double A1, double B1)[] holes, Action<double, double, double, double> emit)
    {
        var cuts = new SortedSet<double> { a0, a1 };
        foreach (var h in holes)
        {
            if (h.A0 > a0 && h.A0 < a1) cuts.Add(h.A0);
            if (h.A1 > a0 && h.A1 < a1) cuts.Add(h.A1);
        }
        var cs = cuts.ToList();
        for (var i = 0; i + 1 < cs.Count; i++)
        {
            double u0 = cs[i], u1 = cs[i + 1], mid = (u0 + u1) / 2;
            var inBand = holes.Where(h => h.A0 < mid && h.A1 > mid).OrderBy(h => h.B0).ToList();
            var v = b0;
            foreach (var h in inBand)
            {
                if (h.B0 > v + 0.001)
                    emit(u0, v, u1, Math.Min(h.B0, b1));
                v = Math.Max(v, h.B1);
            }
            if (b1 > v + 0.001)
                emit(u0, v, u1, b1);
        }
    }

    /// <summary>A railing along an axis-aligned line at deck height y: posts about a metre apart, a top and a middle rail, a hidden
    /// barrier behind it; a gap (in metres along the line from its start) left open for a ladder.</summary>
    private static void Railing(Part p, double xa, double za, double xb, double zb, double y, double gap0 = -1, double gap1 = -1)
    {
        var alongX = Math.Abs(xb - xa) > Math.Abs(zb - za);
        var len = alongX ? Math.Abs(xb - xa) : Math.Abs(zb - za);
        double At(double t, int k) => k == 0 ? (alongX ? xa + Math.Sign(xb - xa) * t : xa) : (alongX ? za : za + Math.Sign(zb - za) * t);
        bool InGap(double t) => gap1 > gap0 && t > gap0 - 0.01 && t < gap1 + 0.01;
        var n = Math.Max(1, (int)Math.Ceiling(len / 1.05));
        for (var i = 0; i <= n; i++)
        {
            var t = len * i / n;
            if (InGap(t))
                continue;
            double x = At(t, 0), z = At(t, 1);
            Detail(p, x - 0.025, y, z - 0.025, x + 0.025, y + 1.05, z + 0.025, Yellow, "painted steel");
        }
        var runs = gap1 > gap0 ? new[] { (0.0, gap0), (gap1, len) } : [(0.0, len)];
        foreach (var (t0, t1) in runs)
        {
            if (t1 - t0 < 0.05)
                continue;
            double x0 = At(t0, 0), z0 = At(t0, 1), x1 = At(t1, 0), z1 = At(t1, 1);
            foreach (var (h, w) in new[] { (y + 1.0, 0.05), (y + 0.5, 0.04) })
                Detail(p, Math.Min(x0, x1) - (alongX ? 0 : w / 2), h, Math.Min(z0, z1) - (alongX ? w / 2 : 0), Math.Max(x0, x1) + (alongX ? 0 : w / 2), h + w, Math.Max(z0, z1) + (alongX ? w / 2 : 0), Yellow, "painted steel");
            Hidden(p, Math.Min(x0, x1) - (alongX ? 0 : 0.02), y, Math.Min(z0, z1) - (alongX ? 0.02 : 0), Math.Max(x0, x1) + (alongX ? 0 : 0.02), y + 1.05, Math.Max(z0, z1) + (alongX ? 0.02 : 0));
        }
    }

    private static Part Piece(string name, string desc, string color = Red, string role = "frame") =>
        new(name, Small, Section, desc, role: role, tool: "welder", material: "steel", color: color, group: Group);

    private static string SideName(int s) => s < 0 ? "Port" : "Stbd";

    // ---- the hull

    /// <summary>A length of the keel: the deck's slab and plate, the dark chassis band round its foot, the belly, two skids and the fuel the lift jets under it burn.</summary>
    private static Part Keel(int i, double z0, double z1)
    {
        var p = Piece($"Cloche Keel {i + 1}", "A length of the Cloche's keel: the deck slab, the dark chassis band under the red hull, the belly plate, two landing skids and a tank in the slab for the lift jets hung under it.", Chassis, "fueltank");
        var aft = z0 >= AftZ - 0.01;
        Hidden(p, -In, Y0, z0, In, FloorY, z1);
        Detail(p, -In + 0.004, FloorY, z0 + 0.003, In - 0.004, FloorY + 0.02, z1 - 0.003, Deck, "painted deck plate");
        foreach (var s in new[] { -1, 1 })
        {
            Detail(p, s * 4.6, Y0, z0, s * 4.86, PlinthY + 0.004, z1, Chassis, "dirty steel"); // (the chassis band, set in under the red hull)
            Solid(p, s * 2.9, 0, z0 + 0.35, s * 3.5, Y0, z1 - 0.35, Chassis, "dirty steel"); // (a skid)
            Detail(p, s * 2.85, 0.06, z0 + 0.5, s * 3.55, 0.14, z1 - 0.5, Hazard, "hazard paint"); // (its foot rail)
            for (var z = z0 + 1.2; z < z1 - 1.0; z += 2.2)
                Detail(p, s * 3.5, 0.2, z - 0.12, s * 4.4, Y0 - 0.004, z + 0.12, Rib, "dirty steel"); // (outrigger brackets)
        }
        Detail(p, -4.84, Y0 - 0.025, z0 + 0.004, 4.84, Y0 - 0.003, z1 - 0.004, Rib, "dirty steel"); // (the belly)
        for (var z = z0 + 1.6; z < z1 - 1.2; z += 3.2)
            Detail(p, -2.9, 0.22, z - 0.1, 2.9, 0.36, z + 0.1, Rib, "dirty steel"); // (cross braces between the skids)
        p.Set(("fuelTank", Kw(("capacity", 900))));
        return p.Stats(aft ? 16000 : 12000, 9000, 30, 8.0);
    }

    /// <summary>A length of one side: the red skin, the chamfer up to the roof, dark ribs wrapped over both, the lining inside, labels and hazard marks.</summary>
    private static Part Side(int s, int i, double z0, double z1)
    {
        var p = Piece($"Cloche Side {SideName(s)} {i + 1}", "A length of the Cloche's side: red plating up to a chamfer under the roof, dark ribs wrapped over it, cream walls over an olive dado inside.");
        Hidden(p, s * In, Y0, z0, s * Hw, CeilY, z1);
        var za = Math.Max(z0, BowZ + Ch);
        var zb = Math.Min(z1, RoofEnd);
        Detail(p, s * (Hw - 0.02), PlinthY, za, s * Hw, Eave, zb, Red, "painted plating");
        Plate(p, [[s * Hw, Eave, za], [s * Hw, Eave, zb], [s * (Hw - Ch), RoofY, zb], [s * (Hw - Ch), RoofY, za]], Red);
        foreach (var rz in Ribs.Where(r => r > z0 + 0.2 && r < z1 - 0.2))
        {
            Detail(p, s * (Hw - 0.03), 0.75, rz - 0.18, s * (Hw + 0.13), Eave + 0.06, rz + 0.18, Rib, "worn paint");
            const double o = 0.065;
            Plate(p, [[s * (Hw + o), Eave + o, rz - 0.18], [s * (Hw + o), Eave + o, rz + 0.18], [s * (Hw - Ch + o), RoofY + o, rz + 0.18], [s * (Hw - Ch + o), RoofY + o, rz - 0.18]], Rib, "worn paint", thick: 0.13);
            // (a hazard mark high on the bay after the rib, and a cream plate on every other bay)
            for (var k = 0; k < 4; k++)
                Detail(p, s * (Hw - 0.004), 5.32, rz + 0.36 + k * 0.16, s * (Hw + 0.012), 5.46, rz + 0.44 + k * 0.16, k % 2 == 0 ? Hazard : HazDark, "hazard paint");
            if (((int)Math.Round(rz * 10) / 32) % 2 == 0)
            {
                Detail(p, s * (Hw - 0.004), 4.35, rz + 1.1, s * (Hw + 0.014), 4.62, rz + 2.0, Cream, "painted plating");
                Detail(p, s * (Hw + 0.006), 4.44, rz + 1.2, s * (Hw + 0.022), 4.5, rz + 1.85, HazDark, "matte paint");
            }
        }
        Detail(p, s * (In - 0.02), LineY, z0 + 0.003, s * In, 2.3, z1 - 0.003, WallLow, "painted plating");
        Detail(p, s * (In - 0.015), 2.3, z0 + 0.003, s * In, CeilY - 0.025, z1 - 0.003, Wall, "wall panelling");
        Detail(p, s * (In - 0.035), 2.27, z0 + 0.003, s * In, 2.34, z1 - 0.003, Rib, "painted steel");
        return p.Stats(5000, 5000, 20, 6.0);
    }

    /// <summary>A length of the roof: dark red deck plate between the chamfers, the ceiling under it, and in one length a hatch up into each greenhouse.</summary>
    private static Part Roof(int i, double z0, double z1)
    {
        var p = Piece($"Cloche Roof {i + 1}", "A length of the Cloche's roof: dark red deck plate the greenhouses stand on, the cream ceiling of the grow hall under it.", Red2);
        var holes = HoleZ0 > z0 && HoleZ1 < z1 ? new[] { (HoleZ0, -HoleX1, HoleZ1, -HoleX0), (HoleZ0, HoleX0, HoleZ1, HoleX1) } : [];
        Less(z0, -Hw, z1, Hw, holes, (a0, b0, a1, b1) => Hidden(p, b0, CeilY, a0, b1, RoofY, a1));
        var za = Math.Max(z0, BowZ + Ch);
        Less(za, -(Hw - Ch), z1, Hw - Ch, holes, (a0, b0, a1, b1) => Detail(p, b0, RoofY - 0.02, a0 + 0.002, b1, RoofY, a1 - 0.002, Red2, "painted deck plate"));
        Less(z0, -In, z1, In, holes, (a0, b0, a1, b1) => Detail(p, b0, CeilY - 0.02, a0 + 0.002, b1, CeilY, a1 - 0.002, CeilC, "painted plating"));
        foreach (var (hz0, hx0, hz1, hx1) in holes)
        {
            // (the hatch's shaft and its hazard rim)
            Detail(p, hx0 - 0.02, CeilY - 0.01, hz0 - 0.02, hx0, RoofY + 0.004, hz1 + 0.02, Rib, "dirty steel");
            Detail(p, hx1, CeilY - 0.01, hz0 - 0.02, hx1 + 0.02, RoofY + 0.004, hz1 + 0.02, Rib, "dirty steel");
            Detail(p, hx0, CeilY - 0.01, hz0 - 0.02, hx1, RoofY + 0.004, hz0, Rib, "dirty steel");
            Detail(p, hx0, CeilY - 0.01, hz1, hx1, RoofY + 0.004, hz1 + 0.02, Rib, "dirty steel");
            Detail(p, hx0 - 0.1, RoofY, hz0 - 0.1, hx1 + 0.1, RoofY + 0.012, hz0 - 0.02, Hazard, "hazard paint");
            Detail(p, hx0 - 0.1, RoofY, hz1 + 0.02, hx1 + 0.1, RoofY + 0.012, hz1 + 0.1, Hazard, "hazard paint");
        }
        return p.Stats(5000, 5000, 20, 6.0);
    }

    /// <summary>The bow: the front wall with the way into the cab and a side door, its dark-framed chamfers, an electrical cabinet.</summary>
    private static Part Bow()
    {
        var p = Piece("Cloche Bow", "The Cloche's bow: the front wall behind the cab, a dark frame round its chamfered edges, the way through to the cab and the side door off the port step, an electrical cabinet on the starboard side.");
        var holes = new[] { (-CabDoor, FloorY, CabDoor, DoorTop), (DoorX0, FloorY, DoorX1, DoorTop) };
        Less(-Hw, Y0, Hw, RoofY, holes, (a0, b0, a1, b1) => Hidden(p, a0, b0, BowZ, a1, b1, BowIn));
        // Outside: the red face, the dark corner and top chamfers meeting at a triangle each side.
        Less(-(Hw - Ch), PlinthY, Hw - Ch, Eave, [.. holes.Select(h => (h.Item1, PlinthY - 0.1, h.Item3, h.Item4))], (a0, b0, a1, b1) => Detail(p, a0, b0, BowZ - 0.02, a1, b1, BowZ, Red, "painted plating"));
        Plate(p, [[-(Hw - Ch), Eave, BowZ], [Hw - Ch, Eave, BowZ], [Hw - Ch, RoofY, BowZ + Ch], [-(Hw - Ch), RoofY, BowZ + Ch]], Rib, "worn paint");
        foreach (var s in new[] { -1, 1 })
        {
            Plate(p, [[s * (Hw - Ch), PlinthY, BowZ], [s * Hw, PlinthY, BowZ + Ch], [s * Hw, Eave, BowZ + Ch], [s * (Hw - Ch), Eave, BowZ]], Rib, "worn paint");
            Plate(p, [[s * (Hw - Ch), Eave, BowZ], [s * Hw, Eave, BowZ + Ch], [s * (Hw - Ch), RoofY, BowZ + Ch]], Rib, "worn paint");
        }
        Detail(p, -4.86, Y0, BowZ + 0.14, 4.86, PlinthY + 0.004, BowIn, Chassis, "dirty steel");
        // The door's hazard frame and a step plate; the cab's way through framed dark.
        Detail(p, DoorX0 - 0.12, FloorY, BowZ - 0.035, DoorX0, DoorTop + 0.12, BowZ - 0.015, Hazard, "hazard paint");
        Detail(p, DoorX1, FloorY, BowZ - 0.035, DoorX1 + 0.12, DoorTop + 0.12, BowZ - 0.015, Hazard, "hazard paint");
        Detail(p, DoorX0, DoorTop, BowZ - 0.035, DoorX1, DoorTop + 0.12, BowZ - 0.015, Hazard, "hazard paint");
        for (var k = 0; k < 5; k++)
            Detail(p, DoorX0 + 0.05 + k * 0.21, DoorTop + 0.02, BowZ - 0.045, DoorX0 + 0.15 + k * 0.21, DoorTop + 0.1, BowZ - 0.03, HazDark, "hazard paint");
        // The electrical cabinet (starboard): a grey box, its door seam, a gauge and a conduit up the wall.
        Detail(p, 3.35, 1.55, BowZ - 0.36, 4.3, 3.15, BowZ - 0.004, Grey, "painted steel");
        Detail(p, 3.82, 1.6, BowZ - 0.376, 3.84, 3.1, BowZ - 0.356, Rib, "painted steel");
        Detail(p, 3.45, 2.6, BowZ - 0.39, 3.7, 2.85, BowZ - 0.36, Cream, "plastic");
        Detail(p, 3.95, 2.2, BowZ - 0.39, 4.05, 2.5, BowZ - 0.36, Rib, "painted steel");
        Detail(p, 3.75, 3.15, BowZ - 0.2, 3.9, 4.4, BowZ - 0.06, Pipe, "painted steel");
        Detail(p, 3.3, 1.45, BowZ - 0.4, 4.35, 1.55, BowZ - 0.004, Rib, "dirty steel");
        // Inside: the lining.
        Less(-In + 0.04, FloorY, In - 0.04, CeilY - 0.025, holes, (a0, b0, a1, b1) => Detail(p, a0, Math.Max(b0, LineY), BowIn, a1, b1, BowIn + 0.02, b1 <= 2.31 ? WallLow : Wall, "wall panelling"));
        return p.Stats(6000, 7000, 25, 6.0);
    }

    /// <summary>The cab: a cream bus front, a big split windscreen and glass doors, a bumper with lamps, side windows, a visor roof you can walk on.</summary>
    private static Part Cab()
    {
        var p = Piece("Cloche Cab", "The Cloche's cab: a cream bus front low on the bow, its big windscreen and glass double door, a dark bumper and lamps, side windows, and a roof with a beacon and a railing that you walk on from the balcony.", Cream);
        // Collision: the floor slab, the sides, the front, the roof.
        Hidden(p, -CabX, CabY0, CabZ0, CabX, FloorY, BowZ);
        foreach (var s in new[] { -1, 1 })
            Hidden(p, s * (CabX - 0.1), FloorY, CabZ0, s * CabX, CabRoof0, BowZ);
        Hidden(p, -(CabX - 0.1), FloorY, CabZ0, CabX - 0.1, CabRoof0, CabZ0 + 0.1);
        Hidden(p, -CabX, CabRoof0, CabZ0, CabX, CabRoof1, BalZ);
        const double F = CabZ0; // the front face
        // The front: the lower panel (cut at the door), the bumper, grille and lamps.
        Detail(p, -CabX - 0.02, CabY0, F - 0.03, 0.97, 1.62, F, Cream, "painted plating");
        Detail(p, -CabX - 0.05, CabY0 - 0.02, F - 0.18, CabX + 0.05, 0.95, F - 0.004, Chassis, "rubber");
        Detail(p, -0.7, 1.0, F - 0.05, 0.4, 1.45, F - 0.025, Rib, "dirty steel");
        for (var k = 0; k < 4; k++)
            Detail(p, -0.66, 1.05 + k * 0.1, F - 0.065, 0.36, 1.09 + k * 0.1, F - 0.045, Grey, "painted steel");
        Detail(p, -0.45, 0.68, F - 0.2, 0.15, 0.86, F - 0.17, Tray, "plastic"); // (the plate)
        foreach (var s in new[] { -1, 1 })
        {
            Detail(p, s * 1.85, 1.05, F - 0.06, s * 2.15, 1.32, F - 0.025, Glow, glow: true);
            Detail(p, s * 2.22, 1.05, F - 0.06, s * 2.52, 1.32, F - 0.025, Glow, glow: true);
            Detail(p, s * 2.58, 1.05, F - 0.06, s * 2.8, 1.26, F - 0.025, Amber, glow: true);
        }
        // The windscreen and the double door, framed dark.
        Detail(p, -CabX - 0.02, 1.58, F - 0.05, CabX + 0.02, 1.66, F + 0.06, Rib, "painted steel");
        Detail(p, -CabX - 0.02, 3.98, F - 0.05, CabX + 0.02, 4.06, F + 0.06, Rib, "painted steel");
        foreach (var x in new[] { -CabX + 0.04, 0.91, CabX - 0.04 })
            Detail(p, x - 0.07, 0.96, F - 0.05, x + 0.07, 4.0, F + 0.06, Rib, "painted steel");
        Detail(p, -CabX + 0.11, 1.66, F - 0.02, 0.84, 3.98, F - 0.004, GlassC, glass: true);
        foreach (var (x0, x1) in new[] { (0.98, 1.86), (1.9, CabX - 0.11) })
        {
            Detail(p, x0, 0.98, F - 0.02, x1, 3.98, F - 0.004, GlassC, glass: true);
            Detail(p, x0, 0.98, F - 0.045, x0 + 0.05, 3.98, F - 0.02, Rib, "painted steel");
            Detail(p, x1 - 0.05, 0.98, F - 0.045, x1, 3.98, F - 0.02, Rib, "painted steel");
            Detail(p, x0, 2.55, F - 0.045, x1, 2.62, F - 0.02, Rib, "painted steel");
            Detail(p, x0, 0.98, F - 0.045, x1, 1.06, F - 0.02, Rib, "painted steel");
        }
        Detail(p, 0.98, 0.62, F - 0.32, CabX - 0.11, 0.7, F - 0.004, Rib, "tread plate"); // (the door's step)
        // Above the windscreen: the cream band and the destination board.
        Detail(p, -CabX - 0.02, 4.06, F - 0.03, CabX + 0.02, CabRoof0, F, Cream, "painted plating");
        Detail(p, -1.3, 4.12, F - 0.05, 1.3, 4.38, F - 0.03, Console, "plastic");
        Detail(p, -1.1, 4.2, F - 0.062, 0.6, 4.3, F - 0.048, "#f0c060", glow: true);
        // The visor roof: the roof plate, a lip over the windscreen, a hatch, the beacon.
        Detail(p, -CabX - 0.06, CabRoof0, F - 0.4, CabX + 0.06, CabRoof1, BalZ - 0.03, Cream2, "painted plating");
        Detail(p, -CabX - 0.1, 4.36, F - 0.46, CabX + 0.1, CabRoof1 + 0.04, F - 0.3, Cream, "painted plating");
        Detail(p, -0.55, CabRoof1, -20.6, 0.45, CabRoof1 + 0.05, -19.6, "#bdb8a8", "plated steel");
        Detail(p, -0.5, CabRoof1 + 0.05, -20.55, 0.4, CabRoof1 + 0.08, -19.65, "#a8a394", "plated steel");
        Detail(p, -0.14, CabRoof1, -21.35, 0.14, CabRoof1 + 0.12, -21.07, HazDark, "rubber");
        CylY(p, 0, -21.21, CabRoof1 + 0.12, CabRoof1 + 0.3, 0.1, 0.07, Amber, "", glow: true);
        // The sides: the lower panel, the windows and pillars, the band above, the solid panel under the balcony.
        foreach (var s in new[] { -1, 1 })
        {
            Detail(p, s * CabX, CabY0, F + 0.004, s * (CabX + 0.02), 1.62, BowZ - 0.004, Cream, "painted plating");
            Detail(p, s * CabX, 1.58, F + 0.06, s * (CabX + 0.045), 1.66, -17.85, Rib, "painted steel");
            Detail(p, s * CabX, 3.98, F + 0.06, s * (CabX + 0.045), 4.06, -17.85, Rib, "painted steel");
            Detail(p, s * (CabX - 0.004), 1.66, F + 0.1, s * (CabX + 0.012), 3.98, -17.95, GlassC, glass: true);
            foreach (var z in new[] { -19.9, -17.9 })
                Detail(p, s * CabX, 1.62, z - 0.06, s * (CabX + 0.045), 4.02, z + 0.06, Rib, "painted steel");
            Detail(p, s * CabX, 4.06, F + 0.004, s * (CabX + 0.02), CabRoof0, BowZ - 0.004, Cream, "painted plating");
            Detail(p, s * CabX, 1.62, -17.84, s * (CabX + 0.02), 4.06, BowZ - 0.004, Cream2, "painted plating");
            Detail(p, s * (CabX + 0.02), 0.62, -21.6, s * (CabX + 0.05), 0.8, -20.2, Amber, glow: true); // (a side marker)
        }
        // Inside: the floor and the cream lower walls.
        Detail(p, -CabX + 0.1, FloorY, CabZ0 + 0.1, CabX - 0.1, FloorY + 0.02, BowZ - 0.003, Deck, "rubber matting");
        foreach (var s in new[] { -1, 1 })
            Detail(p, s * (CabX - 0.12), FloorY, CabZ0 + 0.1, s * (CabX - 0.1), 1.62, BowZ - 0.003, Cream2, "painted plating");
        Detail(p, -CabX + 0.1, FloorY, CabZ0 + 0.1, 0.97, 1.62, CabZ0 + 0.12, Cream2, "painted plating");
        return p.Stats(4000, 4000, 15, 6.0);
    }

    /// <summary>The balcony across the bow at the cab roof's height: grating, a dark fascia and railings (a gap for the ladder up from the port step).</summary>
    private static Part Balcony()
    {
        var p = Piece("Cloche Balcony", "The balcony across the Cloche's bow, level with the cab roof: a grating walk with yellow railings, the ladder up from the port step at its port end.", Rib);
        Hidden(p, -Hw, CabRoof0, BalZ, Hw, CabRoof1, BowZ);
        Detail(p, -Hw + 0.01, CabRoof0 + 0.004, BalZ + 0.004, Hw - 0.01, CabRoof1, BowZ - 0.004, "#4a4c48", "painted grating");
        foreach (var s in new[] { -1, 1 })
        {
            Detail(p, s * (CabX + 0.03), CabRoof0 - 0.08, BalZ - 0.025, s * Hw, CabRoof1 + 0.02, BalZ - 0.004, Rib, "worn paint");
            Detail(p, s * (CabX + 0.4), CabRoof0 - 0.7, BalZ + 0.05, s * (CabX + 0.5), CabRoof0, BowZ - 0.01, Rib, "dirty steel"); // (a bracket)
            Detail(p, s * (Hw - 0.4), CabRoof0 - 0.7, BalZ + 0.05, s * (Hw - 0.3), CabRoof0, BowZ - 0.01, Rib, "dirty steel");
            // The front rail outside the cab (port: a gap for the ladder) and the end rail.
            if (s < 0)
                Railing(p, -Hw + 0.05, BalZ + 0.05, -CabX, BalZ + 0.05, CabRoof1, 0.05, 0.8);
            else
                Railing(p, CabX, BalZ + 0.05, Hw - 0.05, BalZ + 0.05, CabRoof1);
            Railing(p, s * (Hw - 0.05), BalZ + 0.05, s * (Hw - 0.05), BowZ - 0.08, CabRoof1);
            // The cab roof's own railings, along its sides.
            Railing(p, s * (CabX - 0.05), BalZ - 0.02, s * (CabX - 0.05), CabZ0 + 0.6, CabRoof1);
        }
        return p.Stats(900, 1500, 8, 4.0);
    }

    /// <summary>A step each side of the cab at the deck's height: a grating with a railing, brackets to the bow, the ladder up from the ground at its front.</summary>
    private static Part Step(int s)
    {
        var p = Piece($"Cloche Step {SideName(s)}", "A step beside the Cloche's cab at the deck's height: grating, a yellow railing, the ladder up from the ground at its front." + (s < 0 ? " The side door opens off it." : ""), Rib);
        Hidden(p, s * CabX, StepY0, StepZ0, s * Hw, FloorY, BowZ);
        Detail(p, s * (CabX + 0.004), StepY0 + 0.004, StepZ0 + 0.004, s * (Hw - 0.01), FloorY, BowZ - 0.004, "#4a4c48", "painted grating");
        Detail(p, s * CabX, StepY0 - 0.12, StepZ0 - 0.025, s * Hw, FloorY + 0.02, StepZ0 - 0.004, Hazard, "hazard paint");
        foreach (var z in new[] { StepZ0 + 0.3, -18.3, BowZ - 0.3 })
            Detail(p, s * (Hw - 0.3), CabY0 + 0.1, z - 0.06, s * (Hw - 0.18), StepY0, z + 0.06, Rib, "dirty steel"); // (legs to the chassis band's height)
        Railing(p, s * (Hw - 0.05), StepZ0 + 0.05, s * (Hw - 0.05), BowZ - 0.1, FloorY);
        Railing(p, s * (CabX + 0.05), StepZ0 + 0.05, s * (Hw - 0.05), StepZ0 + 0.05, FloorY, 0.75, 1.45);
        return p.Stats(600, 1200, 8, 4.0);
    }

    /// <summary>The bulkhead between the entry room and the grow hall: a wall each face, a framed way through.</summary>
    private static Part ForeBulkhead()
    {
        var p = Piece("Cloche Fore Bulkhead", "The bulkhead between the Cloche's entry room and its grow hall: cream walls over the dado, a dark-framed way through.", Wall);
        var hole = new[] { (-PassX, FloorY, PassX, PassTop) };
        Less(-In, FloorY, In, CeilY, hole, (a0, b0, a1, b1) => Hidden(p, a0, b0, FbZ0, a1, b1, FbZ1));
        foreach (var (z0, z1) in new[] { (FbZ0 - 0.02, FbZ0), (FbZ1, FbZ1 + 0.02) })
        {
            Less(-In + 0.03, FloorY, In - 0.03, CeilY - 0.01, hole, (a0, b0, a1, b1) => Detail(p, a0, Math.Max(b0, LineY), z0, a1, b1, z1, b1 <= 2.31 ? WallLow : Wall, "wall panelling"));
            var zo = z0 < FbZ0 ? z0 - 0.03 : z1 + 0.03;
            Detail(p, -PassX - 0.14, LineY, Math.Min(z0, zo), -PassX, PassTop + 0.14, Math.Max(z1, zo), Rib, "painted steel");
            Detail(p, PassX, LineY, Math.Min(z0, zo), PassX + 0.14, PassTop + 0.14, Math.Max(z1, zo), Rib, "painted steel");
            Detail(p, -PassX, PassTop, Math.Min(z0, zo), PassX, PassTop + 0.14, Math.Max(z1, zo), Rib, "painted steel");
        }
        return p.Stats(1500, 2500, 10, 4.0);
    }

    /// <summary>The aft block's front: the bulkhead between the grow hall and the engine room, and outside, the block's red face above the roof.</summary>
    private static Part AftBulkhead()
    {
        var p = Piece("Cloche Aft Bulkhead", "The front of the Cloche's aft block: the bulkhead between the grow hall and the engine room with a framed way through, and outside, the block's red face standing over the roof.");
        var hole = new[] { (-PassX, FloorY, PassX, PassTop) };
        Less(-In, FloorY, In, AftCeil, hole, (a0, b0, a1, b1) => Hidden(p, a0, b0, RoofEnd, a1, b1, AftZ));
        Less(-In + 0.03, FloorY, In - 0.03, CeilY - 0.01, hole, (a0, b0, a1, b1) => Detail(p, a0, Math.Max(b0, LineY), RoofEnd - 0.02, a1, b1, RoofEnd, b1 <= 2.31 ? WallLow : Wall, "wall panelling"));
        Less(-In + 0.03, FloorY, In - 0.03, AftCeil - 0.01, hole, (a0, b0, a1, b1) => Detail(p, a0, Math.Max(b0, LineY), AftZ, a1, b1, AftZ + 0.02, b1 <= 2.31 ? WallLow : Wall, "wall panelling"));
        foreach (var (zl, zh) in new[] { (RoofEnd - 0.05, RoofEnd), (AftZ, AftZ + 0.05) })
        {
            Detail(p, -PassX - 0.14, LineY, zl, -PassX, PassTop + 0.14, zh, Rib, "painted steel");
            Detail(p, PassX, LineY, zl, PassX + 0.14, PassTop + 0.14, zh, Rib, "painted steel");
            Detail(p, -PassX, PassTop, zl, PassX, PassTop + 0.14, zh, Rib, "painted steel");
        }
        // Outside: the block's face over the roof and its hazard band.
        Detail(p, -Hw, Eave, RoofEnd - 0.02, Hw, AftEave, RoofEnd, Red, "painted plating");
        Detail(p, -Hw, 7.85, RoofEnd - 0.05, Hw, 8.15, RoofEnd - 0.015, Rib, "worn paint");
        for (var k = 0; k < 6; k++)
            Detail(p, -2.4 + k * 0.42, 7.2, RoofEnd - 0.04, -2.2 + k * 0.42, 7.5, RoofEnd - 0.015, k % 2 == 0 ? Hazard : HazDark, "hazard paint");
        Detail(p, 1.2, 6.95, RoofEnd - 0.04, 2.8, 7.55, RoofEnd - 0.015, Cream, "painted plating");
        return p.Stats(2500, 4000, 15, 5.0);
    }

    /// <summary>One side of the aft block: the tall red side, the chamfer to its roof, the stern corner, ribs and a band, the louvred vent and the grille.</summary>
    private static Part AftSide(int s)
    {
        var p = Piece($"Cloche Aft Side {SideName(s)}", "A side of the Cloche's aft block over the engine room: red plating standing above the roof, dark ribs and a band, a cream louvred vent and a dark grille.");
        Hidden(p, s * In, Y0, AftZ, s * Hw, AftCeil, SternIn);
        const double z0 = RoofEnd, zr = SternZ - Ch;
        Detail(p, s * (Hw - 0.016), PlinthY, z0, s * (Hw + 0.004), AftEave, zr, Red, "painted plating");
        Plate(p, [[s * Hw, AftEave, z0], [s * Hw, AftEave, zr], [s * (Hw - Ch), AftTop, zr], [s * (Hw - Ch), AftTop, z0 + Ch]], Red);
        Plate(p, [[s * (Hw - Ch), PlinthY, SternZ], [s * Hw, PlinthY, zr], [s * Hw, AftEave, zr], [s * (Hw - Ch), AftEave, SternZ]], Red);
        Plate(p, [[s * (Hw - Ch), AftEave, SternZ], [s * Hw, AftEave, zr], [s * (Hw - Ch), AftTop, zr]], Red);
        foreach (var rz in new[] { z0 + 0.2, zr - 0.3 })
        {
            Detail(p, s * (Hw - 0.03), 0.75, rz - 0.18, s * (Hw + 0.13), AftEave + 0.06, rz + 0.18, Rib, "worn paint");
            const double o = 0.065;
            if (rz - 0.18 > z0 + Ch)
                Plate(p, [[s * (Hw + o), AftEave + o, rz - 0.18], [s * (Hw + o), AftEave + o, rz + 0.18], [s * (Hw - Ch + o), AftTop + o, rz + 0.18], [s * (Hw - Ch + o), AftTop + o, rz - 0.18]], Rib, "worn paint", thick: 0.13);
        }
        Detail(p, s * (Hw - 0.02), 5.95, z0 + 0.38, s * (Hw + 0.1), 6.3, zr - 0.48, Rib, "worn paint"); // (the band at the hull's eave)
        // The louvred vent (cream, dark slats) and the grille (dark, lighter bars).
        Detail(p, s * (Hw - 0.01), 6.7, 10.4, s * (Hw + 0.07), 8.15, 12.7, Cream, "painted plating");
        for (var k = 0; k < 6; k++)
            Detail(p, s * (Hw + 0.06), 6.85 + k * 0.21, 10.55, s * (Hw + 0.12), 6.97 + k * 0.21, 12.55, Rib, "painted steel");
        Detail(p, s * (Hw - 0.01), 6.6, 15.4, s * (Hw + 0.05), 8.1, 17.2, Rib, "dirty steel");
        for (var k = 0; k < 7; k++)
            Detail(p, s * (Hw + 0.04), 6.68 + k * 0.2, 15.5, s * (Hw + 0.08), 6.76 + k * 0.2, 17.1, "#4d504c", "dirty steel");
        for (var k = 0; k < 4; k++)
            Detail(p, s * (Hw - 0.004), 8.25, 10.7 + k * 0.16, s * (Hw + 0.014), 8.4, 10.78 + k * 0.16, k % 2 == 0 ? Hazard : HazDark, "hazard paint");
        Detail(p, s * (In - 0.02), LineY, AftZ + 0.003, s * In, 2.3, SternIn - 0.003, WallLow, "painted plating");
        Detail(p, s * (In - 0.015), 2.3, AftZ + 0.003, s * In, AftCeil - 0.025, SternIn - 0.003, Wall, "wall panelling");
        return p.Stats(7000, 7000, 22, 6.0);
    }

    /// <summary>The aft block's roof: the top, the chamfers fore and aft, two stacks, two masts, the radome on its yellow tower and a grille.</summary>
    private static Part AftRoof()
    {
        var p = Piece("Cloche Aft Roof", "The roof of the Cloche's aft block: two dark stacks with caps, masts with lamps, a radome on a yellow lattice tower and a dark grille over the engine room.");
        Hidden(p, -Hw, AftCeil, AftZ, Hw, AftTop, SternIn);
        const double z0 = RoofEnd, zr = SternZ - Ch;
        Detail(p, -(Hw - Ch), AftTop - 0.02, z0 + Ch, Hw - Ch, AftTop, zr, Red, "painted plating");
        Plate(p, [[-Hw, AftEave, z0], [Hw, AftEave, z0], [Hw - Ch, AftTop, z0 + Ch], [-(Hw - Ch), AftTop, z0 + Ch]], Rib, "worn paint");
        Plate(p, [[-(Hw - Ch), AftEave, SternZ], [Hw - Ch, AftEave, SternZ], [Hw - Ch, AftTop, zr], [-(Hw - Ch), AftTop, zr]], Red);
        Detail(p, -(Hw - Ch), AftTop, zr - 0.48, Hw - Ch, AftTop + 0.03, zr - 0.12, Rib, "worn paint");
        Detail(p, -(In - 0.03), AftCeil - 0.02, AftZ + 0.003, In - 0.03, AftCeil, SternIn - 0.003, CeilC, "painted plating");
        // Two stacks: dark, a grey band, a wider cap.
        foreach (var (x, z, h) in new[] { (-1.3, 11.4, 1.7), (3.0, 16.7, 1.5) })
        {
            Detail(p, x - 0.42, AftTop, z - 0.42, x + 0.42, AftTop + 0.16, z + 0.42, Rib, "dirty steel");
            CylY(p, x, z, AftTop + 0.16, AftTop + h, 0.28, 0.28, Rib, "soot", 10);
            CylY(p, x, z, AftTop + h * 0.55, AftTop + h * 0.55 + 0.14, 0.31, 0.31, Grey, "painted steel", 10);
            CylY(p, x, z, AftTop + h - 0.05, AftTop + h + 0.14, 0.37, 0.37, Rib, "soot", 10);
        }
        // Masts with lamps at their tips.
        foreach (var (x, z, h, lamp) in new[] { (0.6, 12.4, 2.6, Amber), (1.5, 13.3, 1.9, RedLamp), (-0.4, 16.9, 1.4, Amber) })
        {
            Detail(p, x - 0.15, AftTop, z - 0.15, x + 0.15, AftTop + 0.1, z + 0.15, Rib, "dirty steel");
            Detail(p, x - 0.03, AftTop + 0.1, z - 0.03, x + 0.03, AftTop + h, z + 0.03, Rib, "painted steel");
            Detail(p, x - 0.06, AftTop + h, z - 0.06, x + 0.06, AftTop + h + 0.12, z + 0.06, lamp, glow: true);
            Detail(p, x - 0.22, AftTop + h * 0.7, z - 0.02, x + 0.22, AftTop + h * 0.7 + 0.04, z + 0.02, Rib, "painted steel");
        }
        // The radome on its yellow lattice tower, at the block's fore port corner.
        const double rx = -3.25, rz = 10.95, th = 1.3;
        foreach (var (dx, dz) in new[] { (-0.33, -0.33), (0.33, -0.33), (-0.33, 0.33), (0.33, 0.33) })
            Detail(p, rx + dx - 0.03, AftTop, rz + dz - 0.03, rx + dx + 0.03, AftTop + th, rz + dz + 0.03, Yellow, "painted steel");
        foreach (var y in new[] { AftTop + 0.45, AftTop + 0.9 })
        {
            Detail(p, rx - 0.33, y, rz - 0.36, rx + 0.33, y + 0.04, rz - 0.31, Yellow, "painted steel");
            Detail(p, rx - 0.33, y + 0.005, rz + 0.31, rx + 0.33, y + 0.045, rz + 0.36, Yellow, "painted steel");
            Detail(p, rx - 0.36, y + 0.01, rz - 0.33, rx - 0.31, y + 0.05, rz + 0.33, Yellow, "painted steel");
            Detail(p, rx + 0.31, y + 0.015, rz - 0.33, rx + 0.36, y + 0.055, rz + 0.33, Yellow, "painted steel");
        }
        Detail(p, rx - 0.42, AftTop + th, rz - 0.42, rx + 0.42, AftTop + th + 0.06, rz + 0.42, Yellow, "painted steel");
        CylY(p, rx, rz, AftTop + th + 0.06, AftTop + th + 0.16, 0.66, 0.66, Cream, "plastic", 16);
        CylY(p, rx, rz, AftTop + th + 0.16, AftTop + th + 0.42, 0.42, 0.2, Cream2, "plastic", 12);
        // The grille over the engine room.
        Detail(p, -1.0, AftTop, 14.9, 1.1, AftTop + 0.03, 16.3, Rib, "dirty steel");
        for (var k = 0; k < 6; k++)
            Detail(p, -0.9, AftTop + 0.03, 15.0 + k * 0.22, 1.0, AftTop + 0.06, 15.1 + k * 0.22, "#4d504c", "dirty steel");
        return p.Stats(6000, 6000, 20, 6.0);
    }

    /// <summary>The stern: the red face, two ribs down it, tail lamps, a maintenance panel.</summary>
    private static Part Stern()
    {
        var p = Piece("Cloche Stern", "The Cloche's stern: a red face with two dark ribs down it, red tail lamps and a cream service panel.");
        Hidden(p, -Hw, Y0, SternIn, Hw, AftTop, SternZ);
        Detail(p, -(Hw - Ch), PlinthY, SternZ, Hw - Ch, AftEave, SternZ + 0.02, Red, "painted plating");
        Detail(p, -4.86, Y0, SternIn, 4.86, PlinthY + 0.004, SternZ - 0.14, Chassis, "dirty steel");
        foreach (var x in new[] { -2.2, 2.2 })
            Detail(p, x - 0.18, 0.75, SternZ - 0.02, x + 0.18, AftEave + 0.06, SternZ + 0.15, Rib, "worn paint");
        foreach (var s in new[] { -1, 1 })
        {
            Detail(p, s * 3.3, 1.5, SternZ + 0.015, s * 3.9, 1.9, SternZ + 0.05, RedLamp, glow: true);
            Detail(p, s * 3.3, 1.95, SternZ + 0.015, s * 3.9, 2.1, SternZ + 0.05, Amber, glow: true);
        }
        Detail(p, -1.4, 2.2, SternZ + 0.015, 1.4, 4.4, SternZ + 0.04, Cream, "painted plating");
        Detail(p, -1.3, 2.3, SternZ + 0.035, 1.3, 2.36, SternZ + 0.055, Rib, "painted steel");
        Detail(p, -1.3, 4.24, SternZ + 0.035, 1.3, 4.3, SternZ + 0.055, Rib, "painted steel");
        for (var k = 0; k < 6; k++)
            Detail(p, -1.2 + k * 0.42, 5.0, SternZ + 0.015, -1.0 + k * 0.42, 5.3, SternZ + 0.04, k % 2 == 0 ? Hazard : HazDark, "hazard paint");
        Detail(p, -(In - 0.03), LineY, SternIn - 0.02, In - 0.03, AftCeil - 0.01, SternIn, Wall, "wall panelling");
        return p.Stats(5000, 6000, 20, 6.0);
    }

    // ---- the greenhouses

    /// <summary>A point on a greenhouse's vault at step k of 6 (0 the outboard eave, 6 the inboard one... either way round).</summary>
    private static (double X, double Y) Vault(double xc, int k)
    {
        var t = Math.PI * k / 6;
        return (xc + (GX1 - GX0) / 2 * Math.Cos(t), GWall + GRise * Math.Sin(t));
    }

    /// <summary>One bay of a greenhouse: cream sills and frames, glass walls and a six-faceted glass vault, purlins, gable ends at the
    /// first and last bays, yellow frames and rails inside, the walkway's grating down the middle.</summary>
    private static Part Glasshouse(int s, int b)
    {
        double za = Bays[b], zb = Bays[b + 1];
        bool front = b == 0, rear = b == Bays.Length - 2;
        var p = Piece($"Cloche Glasshouse {(s < 0 ? "P" : "S")}{b + 1}", "A bay of one of the Cloche's greenhouses: a glass barrel vault on cream frames over two rows of grow beds and a walkway, yellow frames inside.", Cream);
        double x0 = Math.Min(s * GX0, s * GX1), x1 = Math.Max(s * GX0, s * GX1), xc = s * (GX0 + GX1) / 2;
        // Collision: the two walls, a ceiling under the glass, the gable ends.
        Hidden(p, x0, RoofY, za, x0 + 0.15, GWall, zb);
        Hidden(p, x1 - 0.15, RoofY, za, x1, GWall, zb);
        Hidden(p, xc - 1.2, 8.85, za, xc + 1.2, 8.95, zb);
        if (front)
            Hidden(p, x0 + 0.15, RoofY, za, x1 - 0.15, GWall, za + 0.12);
        if (rear)
            Hidden(p, x0 + 0.15, RoofY, zb - 0.12, x1 - 0.15, GWall, zb);
        double ga = za + (front ? 0.004 : 0.002), gb = zb - (rear ? 0.004 : 0.002);
        // The sills, the wall glass and the eave rails.
        foreach (var (xa, xb) in new[] { (x0, x0 + 0.15), (x1 - 0.15, x1) })
        {
            Detail(p, xa, RoofY, ga, xb, 6.92, gb, Cream, "painted steel");
            var xg = (xa + xb) / 2;
            Detail(p, xg - 0.01, 6.92, ga, xg + 0.01, GWall, gb, GlassC, glass: true);
            Detail(p, xa, GWall - 0.04, ga, xb, GWall + 0.05, gb, Cream, "painted steel");
        }
        // The vault: six glass facets, purlins on the joints between them.
        for (var k = 0; k < 6; k++)
        {
            var (ax, ay) = Vault(xc, k);
            var (bx, by) = Vault(xc, k + 1);
            Plate(p, [[ax, ay, ga], [bx, by, ga], [bx, by, gb], [ax, ay, gb]], GlassC, glass: true, thick: 0.02);
            if (k > 0)
                Detail(p, ax - 0.045, ay - 0.045, ga, ax + 0.045, ay + 0.045, gb, Cream, "painted steel");
        }
        Detail(p, xc - 0.06, GWall + GRise - 0.03, ga, xc + 0.06, GWall + GRise + 0.07, gb, Cream, "painted steel"); // (the ridge)
        // The arched frames across it.
        var frames = new List<double>();
        for (var z = za; z < zb - 0.5; z += (zb - za) / 4)
            frames.Add(z + (z == za && !front ? 0 : 0) + (z == za ? 0.06 : 0));
        if (rear)
            frames.Add(zb - 0.06);
        foreach (var zf in frames)
        {
            foreach (var (xa, xb) in new[] { (x0 + 0.02, x0 + 0.13), (x1 - 0.13, x1 - 0.02) })
                Detail(p, xa, 6.92, zf - 0.05, xb, GWall, zf + 0.05, Cream, "painted steel");
            for (var k = 0; k < 6; k++)
            {
                var (ax, ay) = Vault(xc, k);
                var (bx, by) = Vault(xc, k + 1);
                Plate(p, [[ax, ay, zf - 0.05], [bx, by, zf - 0.05], [bx, by, zf + 0.05], [ax, ay, zf + 0.05]], Cream, "painted steel", thick: 0.1);
            }
        }
        // The gable ends: glass in the arch, mullions and a transom.
        foreach (var (on, zg) in new[] { (front, za + 0.07), (rear, zb - 0.07) })
        {
            if (!on)
                continue;
            var outline = new List<double[]> { new[] { x1 - 0.08, 6.92, zg }, new[] { x0 + 0.08, 6.92, zg } };
            var arch = Enumerable.Range(0, 7).Select(k => Vault(xc, k)).OrderBy(q => q.X).ToList();
            outline.AddRange(arch.Select(q => new[] { Math.Clamp(q.X, x0 + 0.08, x1 - 0.08), q.Y - 0.04, zg }));
            Plate(p, [.. outline.OrderBy(q => Math.Atan2(q[1] - 7.9, q[0] - xc))], GlassC, glass: true, thick: 0.02);
            Detail(p, x0 + 0.15, RoofY, zg - 0.07, x1 - 0.15, 6.92, zg + 0.07, Cream, "painted steel");
            Detail(p, x0 + 0.13, GWall - 0.04, zg - 0.035, x1 - 0.13, GWall + 0.04, zg + 0.035, Cream, "painted steel");
            foreach (var dx in new[] { -0.62, 0.0, 0.62 })
            {
                var top = GWall + GRise * Math.Sqrt(Math.Max(0, 1 - Math.Pow(dx / ((GX1 - GX0) / 2), 2))) - 0.06;
                Detail(p, xc + dx - 0.04, 6.92, zg - 0.035, xc + dx + 0.04, top, zg + 0.035, Cream, "painted steel");
            }
        }
        // Inside: the walkway's grating (round the hatch in the first bay), yellow rails along it, a yellow frame overhead.
        var hole = b == 0 ? new[] { (HoleZ0 - 0.05, Math.Min(s * HoleX0, s * HoleX1) - 0.05, HoleZ1 + 0.05, Math.Max(s * HoleX0, s * HoleX1) + 0.05) } : [];
        Less(ga + 0.12, xc - 0.68, gb - 0.12, xc + 0.68, hole, (a0, b0, a1, b1) => Detail(p, b0, RoofY, a0, b1, RoofY + 0.015, a1, "#55574f", "painted grating"));
        foreach (var dx in new[] { -0.74, 0.74 })
        {
            Detail(p, xc + dx - 0.025, 7.55, ga + 0.15, xc + dx + 0.025, 7.6, gb - 0.15, Yellow, "painted steel");
            for (var z = za + 0.95; z < zb - 0.3; z += 1.9)
                Detail(p, xc + dx - 0.025, RoofY + 0.015, z - 0.025, xc + dx + 0.025, 7.6, z + 0.025, Yellow, "painted steel");
        }
        var zm = (za + zb) / 2;
        foreach (var dx in new[] { -0.9, 0.9 })
            Detail(p, xc + dx - 0.05, RoofY + 0.015, zm - 0.05, xc + dx + 0.05, 8.7, zm + 0.05, Yellow, "painted steel");
        Detail(p, xc - 0.95, 8.7, zm - 0.06, xc + 0.95, 8.8, zm + 0.06, Yellow, "painted steel");
        Detail(p, xc - 0.03, 8.0, zm - 0.6, xc + 0.03, 8.7, zm + 0.6, Yellow, "painted steel"); // (a hanger for the misting line)
        Detail(p, xc - 0.04, 8.0, ga + 0.2, xc + 0.04, 8.06, gb - 0.2, Pipe, "painted steel");
        return p.Stats(2000, 1500, 5, 5.0);
    }

    // ---- the tanks and the nacelles

    /// <summary>A water tank strapped low on a flank: an eight-sided drum with chamfered ends, cream ends and band, a red body, dark straps on saddles, a pipe up into the hull.</summary>
    private static Part Tank(int s, bool fore)
    {
        double r = fore ? 1.45 : 1.3, z0 = fore ? -8.6 : 0.2, z1 = fore ? -3.0 : 5.4, yc = PlinthY + r + 0.1, xc = s * (Hw + 0.25 + r), zm = (z0 + z1) / 2;
        var p = Piece($"Cloche Tank {(fore ? "Fore" : "Aft")} {SideName(s)}", "One of the Cloche's water tanks: an eight-sided drum strapped low on its flank, cream ends and a band, a red body, a pipe up into the hull for the grow beds.");
        Hidden(p, xc - r * 0.88, yc - r * 0.88, z0 + 0.1, xc + r * 0.88, yc + r * 0.88, z1 - 0.1);
        CylZ(p, xc, yc, z0 + 0.45, z1 - 0.45, r, r, Red, "painted plating");
        CylZ(p, xc, yc, z0, z0 + 0.46, r * 0.72, r - 0.004, Cream, "painted plating");
        CylZ(p, xc, yc, z1 - 0.46, z1, r - 0.004, r * 0.72, Cream, "painted plating");
        CylZ(p, xc, yc, z0 + 0.44, z0 + 1.3, r + 0.01, r + 0.01, Cream, "painted plating");
        CylZ(p, xc, yc, z1 - 1.3, z1 - 0.44, r + 0.01, r + 0.01, Cream, "painted plating");
        CylZ(p, xc, yc, zm - 0.32, zm + 0.32, r + 0.012, r + 0.012, Cream, "painted plating");
        foreach (var zs in new[] { z0 + 1.55, z1 - 1.8 })
        {
            CylZ(p, xc, yc, zs, zs + 0.25, r + 0.06, r + 0.06, Rib, "dirty steel");
            Solid(p, s * Hw, yc - 0.45, zs, s * (Hw + 0.4), yc + 0.45, zs + 0.25, Rib, "dirty steel"); // (a saddle to the hull)
        }
        // A dark label on the band, and the pipe: out of the aft end, up the hull, in under the eave.
        var face = Math.Abs(xc) + (r + 0.012) * Math.Cos(Math.PI / 8);
        Detail(p, s * (face - 0.004), yc - 0.22, zm - 0.24, s * (face + 0.012), yc + 0.08, zm + 0.24, Rib, "matte paint");
        Detail(p, s * (face + 0.006), yc - 0.14, zm - 0.18, s * (face + 0.02), yc - 0.08, zm + 0.12, Cream, "matte paint");
        var px = s * (Hw + 0.28);
        Detail(p, px - 0.09, yc + 0.2, z1 - 0.1, px + 0.09, yc + 0.38, z1 + 0.42, Pipe, "painted steel");
        Detail(p, px - 0.09, yc + 0.2, z1 + 0.24, px + 0.09, 4.85, z1 + 0.42, Pipe, "painted steel");
        Detail(p, s * (Hw - 0.01), 4.67, z1 + 0.24, px + s * 0.09, 4.85, z1 + 0.42, Pipe, "painted steel");
        Detail(p, px - 0.13, 3.5, z1 + 0.2, px + 0.13, 3.72, z1 + 0.46, Rib, "painted steel"); // (a valve)
        Detail(p, px - 0.03, 3.72, z1 + 0.3, px + 0.03, 3.9, z1 + 0.36, RedLamp, "painted steel");
        return p.Stats(fore ? 5500 : 4500, 2500, 10, 6.0);
    }

    /// <summary>A nacelle on the aft block's flank: an eight-sided red pod on a dark pylon, a cream intake ring round a dark grille, dark rings, a cream panel, a pipe from the block, the tail plate the drive hangs on.</summary>
    private static Part Nacelle(int s)
    {
        double xc = s * NacX, yc = NacY;
        var p = Piece($"Cloche Nacelle {SideName(s)}", "A nacelle on the Cloche's aft block: a red eight-sided pod on a dark pylon, a cream intake ring round a dark grille, a pipe in from the block and the drive in its tail. Its own fuel.", Red, "fueltank");
        Hidden(p, xc - 1.15, yc - 1.15, 11.1, xc + 1.15, yc + 1.15, 17.0);
        Hidden(p, xc - 0.6, yc - 0.6, 17.0, xc + 0.6, yc + 0.6, NacTail);
        CylZ(p, xc, yc, 11.55, 17.35, NacR, NacR, Red, "painted plating");
        CylZ(p, xc, yc, NacZ0, 11.6, 1.52, 1.5, Cream, "painted plating");
        CylZ(p, xc, yc, NacZ0 - 0.012, NacZ0 + 0.03, 1.1, 1.1, Rib, "dirty steel");
        for (var k = 0; k < 6; k++)
            Detail(p, xc - 0.82, yc - 0.62 + k * 0.25, NacZ0 - 0.04, xc + 0.82, yc - 0.53 + k * 0.25, NacZ0 - 0.01, "#5a5d59", "painted steel");
        foreach (var z in new[] { 12.45, 16.05 })
            CylZ(p, xc, yc, z, z + 0.22, NacR + 0.03, NacR + 0.03, Rib, "worn paint");
        CylZ(p, xc, yc, 17.3, 17.6, NacR + 0.06, NacR + 0.04, Rib, "worn paint");
        CylZ(p, xc, yc, 17.55, NacTail, 1.2, 1.18, Chassis, "dirty steel");
        var face = NacX + NacR * Math.Cos(Math.PI / 8);
        Detail(p, s * (face - 0.004), yc - 0.4, 14.6, s * (face + 0.014), yc + 0.4, 15.7, Cream, "painted plating");
        Detail(p, s * (face + 0.006), yc - 0.1, 14.75, s * (face + 0.022), yc + 0.05, 15.4, Rib, "matte paint");
        // The pylon and the pipe from the block (out under the eave band, down, into the pod's top).
        Solid(p, s * Hw, yc - 0.45, 12.7, s * (NacX - 1.0), yc + 0.45, 15.9, Rib, "dirty steel");
        Plate(p, [[s * Hw, yc + 0.45, 12.7], [s * Hw, yc + 0.45, 15.9], [s * (NacX - 1.0), yc + 0.45, 15.6], [s * (NacX - 1.0), yc + 0.45, 13.0]], Rib, "worn paint", thick: 0.06);
        double pz0 = 11.85, pz1 = 12.1, pxa = s * (Hw + 0.6), pxb = s * (Hw + 0.85);
        Detail(p, s * (Hw - 0.01), 6.45, pz0, pxb, 6.7, pz1, Pipe, "painted steel");
        Detail(p, pxa, yc + 1.05, pz0, pxb, 6.7, pz1, Pipe, "painted steel");
        Detail(p, pxa, yc + 1.05, pz0, s * (NacX - 0.2), yc + 1.3, pz1, Pipe, "painted steel");
        Detail(p, pxa - 0.05, 5.4, pz0 - 0.05, pxb + 0.05, 5.6, pz1 + 0.05, Rib, "painted steel");
        p.Set(("fuelTank", Kw(("capacity", 1500))));
        return p.Stats(6000, 4000, 15, 6.0);
    }

    // ---- fittings (part frame, raw)

    private static Part Fitting(string name, string desc, string role, string color, string tool = "wrench") =>
        new(name, Small, Section, desc, role: role, tool: tool, material: "steel", color: color, group: Group);

    /// <summary>The drive in a nacelle's tail: a dark collar and a flared bell, glowing inside.</summary>
    private static Part Drive()
    {
        var p = Fitting(DriveName, $"The drive in each of the Cloche's nacelle tails: {DriveForce / 1e3:0} kN out of a flared dark bell, pushing it along. It burns the nacelle's own fuel.", "thruster", Chassis);
        CylZ(p, 0, 0, 0, 0.25, 1.12, 1.12, Rib, "dirty steel", raw: true);
        CylZ(p, 0, 0, 0.24, 1.15, 0.92, 1.1, "#3a3c3a", "soot", 12, raw: true);
        CylZ(p, 0, 0, 1.12, 1.16, 1.12, 1.12, Grey, "painted steel", 12, raw: true);
        CylZ(p, 0, 0, 0.8, 0.84, 0.72, 0.72, "#ffb060", "", 12, glow: true, raw: true);
        Hidden(p, -0.75, -0.75, 0, 0.75, 0.75, 0.8, raw: true);
        p.Sock("front", [0, 0, 0], "-z");
        p.Set(("thruster", Kw(("force", DriveForce), ("spool", 0.5), ("exhaust", "+z"), ("at", Py.L(0, 0, 1.15)), ("burnRate", Py.Round(DriveForce / 1e6 * Burn, 4)))),
            ("ports", Py.L(BigDriveParts.PipePort("Fuel in", "in", [0, 0.75, 0.4], "+y", "fuel"))));
        p.Set(("flame", Py.L(Kw(("at", Py.L(0, 0, 1.15)), ("dir", "+z"), ("radius", 0.85), ("length", 4.5), ("kind", "Rocket")))));
        return p.Stats(900, 3000, 20, 5.0);
    }

    /// <summary>A lift jet under the belly: a cream collar, a dark louvred skirt, a nozzle glowing inside.</summary>
    private static Part LiftJet()
    {
        var p = Fitting(LiftJetName, $"A lift jet: {LiftForce / 1e3:0} kN straight down from under the Cloche's belly. Eight of them hold it up; the flight computer tips it on them.", "thruster", Chassis);
        CylY(p, 0, 0, -0.12, -0.003, 0.5, 0.5, Cream, "painted plating", raw: true);
        CylY(p, 0, 0, -0.42, -0.12, 0.44, 0.4, Rib, "dirty steel", raw: true);
        for (var k = 0; k < 3; k++)
            CylY(p, 0, 0, -0.36 + k * 0.08, -0.33 + k * 0.08, 0.452 - k * 0.012, 0.452 - k * 0.012, "#4d504c", "dirty steel", raw: true);
        CylY(p, 0, 0, -0.48, -0.42, 0.34, 0.36, "#2a2c2e", "soot", raw: true);
        CylY(p, 0, 0, -0.46, -0.45, 0.24, 0.24, "#ffb060", "", glow: true, raw: true);
        Hidden(p, -0.35, -0.45, -0.35, 0.35, 0, 0.35, raw: true);
        p.Sock("top", [0, 0, 0], "+y");
        p.Set(("thruster", Kw(("force", LiftForce), ("spool", 0.5), ("exhaust", "-y"), ("at", Py.L(0, -0.48, 0)), ("burnRate", Py.Round(LiftForce / 1e6 * Burn, 4)))),
            ("ports", Py.L(BigDriveParts.PipePort("Fuel in", "in", [0.35, -0.2, 0], "+x", "fuel"))));
        p.Set(("flame", Py.L(Kw(("at", Py.L(0, -0.48, 0)), ("dir", "-y"), ("radius", 0.34), ("length", 2.2), ("kind", "Rocket")))));
        return p.Stats(420, 3000, 25, 5.0);
    }

    /// <summary>The cab's dashboard: a bus dash the width of the driver's side, its sloped instrument panel, a steering wheel; the flight computer.</summary>
    private static Part Dashboard()
    {
        var p = Fitting("Cloche Dashboard", "The Cloche's dashboard: a bus driver's dash with a sloped panel of dials and screens and a steering wheel, the flight computer in it. Fit it level, facing the seat.", "logic", Console);
        Solid(p, -1.1, 0, -0.32, 1.1, 0.78, 0.05, Console, "plastic", raw: true);
        Plate(p, [[-1.1, 0.78, 0.05], [1.1, 0.78, 0.05], [1.1, 0.95, -0.32], [-1.1, 0.95, -0.32]], Console, "plastic", thick: 0.04, raw: true);
        // The panel's screens and dials (green and yellow, as the picture's), and switches.
        Plate(p, [[-0.7, 0.8, 0.02], [-0.2, 0.8, 0.02], [-0.2, 0.92, -0.24], [-0.7, 0.92, -0.24]], "#5fc07a", glow: true, thick: 0.05, raw: true);
        Plate(p, [[-0.1, 0.8, 0.02], [0.3, 0.8, 0.02], [0.3, 0.92, -0.24], [-0.1, 0.92, -0.24]], "#e8c050", glow: true, thick: 0.05, raw: true);
        Plate(p, [[0.45, 0.8, 0.02], [0.95, 0.8, 0.02], [0.95, 0.92, -0.24], [0.45, 0.92, -0.24]], "#78c8e8", glow: true, thick: 0.05, raw: true);
        for (var k = 0; k < 6; k++)
            Detail(p, -0.95 + k * 0.34, 0.55, 0.05, -0.85 + k * 0.34, 0.62, 0.07, k % 2 == 0 ? Amber : "#d8d4c8", "plastic", raw: true);
        // The steering wheel on its column, raked towards the seat.
        Detail(p, -0.6, 0.55, 0.05, -0.5, 0.75, 0.3, Rib, "plastic", raw: true);
        double[][] W(double r, double d)
        {
            const double tilt = 0.6; // radians from upright
            return [.. Enumerable.Range(0, 12).Select(i =>
            {
                var t = (i + 0.5) * Math.PI / 6;
                double u = r * Math.Cos(t), v = r * Math.Sin(t);
                return new[] { -0.55 + u, 0.85 + v * Math.Cos(tilt) - d * Math.Sin(tilt), 0.33 + v * Math.Sin(tilt) + d * Math.Cos(tilt) };
            })];
        }
        Prism(p, W(0.24, 0), W(0.24, 0.03), Rib, "rubber", raw: true);
        Prism(p, W(0.07, -0.01), W(0.07, 0.045), Grey, "painted steel", raw: true);
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Set(("logic", Kw(("kind", "flightstabilizer"), ("gyro", 40000000))), ("config", FlightParts.Config), ("faces", "forward"), ("faceAxis", "-z"));
        return p.Stats(260, 800, 8, 3.0);
    }

    /// <summary>The driver's seat: a sprung pedestal, a brown cushion and a tall back, armrests.</summary>
    private static Part DriverSeat()
    {
        var p = Fitting("Cloche Driver Seat", "The Cloche's driver's seat: a sprung pedestal, a brown cushioned seat with a tall back and armrests. Wire it to the dashboard.", "seat", Fabric);
        Solid(p, -0.18, 0, -0.18, 0.18, 0.32, 0.18, Rib, "painted steel", raw: true);
        Detail(p, -0.24, 0.0, -0.24, 0.24, 0.05, 0.24, Rib, "painted steel", raw: true);
        Detail(p, -0.28, 0.32, -0.28, 0.28, 0.48, 0.24, Fabric, "fabric", raw: true);
        Plate(p, [[-0.27, 0.5, 0.2], [0.27, 0.5, 0.2], [0.25, 1.3, 0.34], [-0.25, 1.3, 0.34]], Fabric, "fabric", thick: 0.14, raw: true);
        Detail(p, -0.17, 1.3, 0.25, 0.17, 1.48, 0.43, Fabric, "fabric", raw: true);
        foreach (var s in new[] { -1, 1 })
            Detail(p, s * 0.27, 0.62, -0.2, s * 0.33, 0.68, 0.18, Rib, "plastic", raw: true);
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Set(("seat", Kw(("at", Py.L(0, -0.05, 0)))));
        return p.Stats(60, 200, 4, 2.0);
    }

    /// <summary>The power plant in the engine room: a cream cabinet on a dark plinth with red bands, a vent, coolant risers to the ceiling.</summary>
    private static Part PowerPlant()
    {
        var p = Fitting("Cloche Power Plant", "The Cloche's power plant: a cream cabinet on a dark plinth in the engine room, 400 kW for the grow lights, the air and the flight computer. No fuel.", "generator", Cream, "welder");
        Solid(p, -1.3, 0, -1.0, 1.3, 0.2, 1.0, Rib, "dirty steel", raw: true);
        Solid(p, -1.15, 0.2, -0.85, 1.15, 2.2, 0.85, Cream, "worn paint", raw: true);
        foreach (var y in new[] { 0.6, 1.6 })
            Detail(p, -1.165, y, -0.865, 1.165, y + 0.12, 0.865, Red, "worn paint", raw: true);
        Detail(p, -0.6, 0.9, 0.85, 0.6, 1.4, 0.87, Rib, "dirty steel", raw: true);
        for (var k = 0; k < 4; k++)
            Detail(p, -0.55, 0.95 + k * 0.12, 0.865, 0.55, 1.0 + k * 0.12, 0.885, "#5a5d59", "painted steel", raw: true);
        Detail(p, 0.7, 1.7, 0.85, 1.0, 2.0, 0.87, "#80d0a0", glow: true, raw: true);
        foreach (var x in new[] { -0.75, 0.75 })
            CylY(p, x, 0, 2.2, 7.39, 0.12, 0.12, Pipe, "painted steel", raw: true);
        Detail(p, -0.9, 2.2, -0.2, 0.9, 2.35, 0.2, Grey, "painted steel", raw: true);
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Set(("generator", Kw(("rate", 400), ("spool", 6), ("fuel", "none"))), ("ports", Py.L(Kw(("name", "Power out"), ("type", "power"), ("dir", "out"), ("at", Py.L(0, 0.1, 1.0)), ("face", "+z")))));
        return p.Stats(2200, 3000, 20, 8.0);
    }

    /// <summary>A caged lamp: a cream back plate, a dark cage round a warm glass.</summary>
    private static Part Lamp()
    {
        var p = Fitting("Cloche Cage Lamp", "A caged lamp on the Cloche's walls: a cream back plate, a dark cage round a warm glass. Lights while the power circuit reaches it.", "logic", Cream);
        Solid(p, -0.12, -0.16, 0, 0.12, 0.16, 0.04, Cream, "painted steel", raw: true);
        Detail(p, -0.07, -0.1, 0.04, 0.07, 0.1, 0.13, Glow, glow: true, raw: true);
        foreach (var x in new[] { -0.085, 0.085 })
            Detail(p, x - 0.01, -0.12, 0.04, x + 0.01, 0.12, 0.15, Rib, "painted steel", raw: true);
        foreach (var y in new[] { -0.05, 0.05 })
            Detail(p, -0.095, y - 0.01, 0.135, 0.095, y + 0.01, 0.155, Rib, "painted steel", raw: true);
        p.Sock("back", [0, 0, 0], "-z");
        p.Set(("logic", new PyDict { ["kind"] = "lamp", ["mains"] = true }), ("power", 30), ("config", LampConfig()));
        return p.Stats(5, 60, 2, 1.0);
    }

    private static List<object?> LampConfig() =>
    [
        new PyDict { ["key"] = "colour", ["type"] = "colour", ["label"] = "Colour", ["group"] = "Light", ["default"] = "#ffe6b0", ["tooltip"] = "The colour it glows when lit." },
        new PyDict { ["key"] = "brightness", ["label"] = "Brightness", ["group"] = "Light", ["unit"] = "%", ["min"] = 10, ["max"] = 200, ["step"] = 1, ["default"] = 100, ["tooltip"] = "How bright it glows." },
        new PyDict { ["key"] = "blink", ["label"] = "Blink", ["group"] = "Light", ["unit"] = "Hz", ["min"] = 0, ["max"] = 10, ["step"] = 0.1, ["default"] = 0, ["tooltip"] = "Flashes this many times a second while lit (0: steady)." },
    ];

    /// <summary>Plants on a tray: rows of leafy clumps, sizes and greens varied by a fixed pattern (salt), from (x0, z0) to (x1, z1) at height y.</summary>
    private static void Plants(Part p, double x0, double z0, double x1, double z1, double y, int rows, int cols, double size, double height, int salt, bool fruit = false)
    {
        string[] greens = [Leaf, Leaf2, Leaf3];
        for (var r = 0; r < rows; r++)
            for (var c = 0; c < cols; c++)
            {
                var h = (r * 7 + c * 13 + salt * 5) % 11;
                double cx = x0 + (x1 - x0) * (r + 0.5) / rows, cz = z0 + (z1 - z0) * (c + 0.5) / cols;
                double w = size * (0.8 + 0.04 * (h % 5)), ht = height * (0.75 + 0.05 * (h % 6));
                Detail(p, cx - w / 2, y, cz - w / 2, cx + w / 2, y + ht, cz + w / 2, greens[h % 3], "matte paint", raw: true);
                Detail(p, cx - w * 0.3, y + ht - 0.004, cz - w * 0.3, cx + w * 0.3, y + ht + height * 0.25, cz + w * 0.3, greens[(h + 1) % 3], "matte paint", raw: true);
                if (fruit && h % 3 == 0)
                    Detail(p, cx + w * 0.2, y + ht * 0.5, cz - w / 2 - 0.012, cx + w * 0.42, y + ht * 0.5 + 0.06, cz - w / 2 + 0.03, RedLamp, "plastic", raw: true);
            }
    }

    /// <summary>A grow rack for the hall: a dark frame, three cream trays of plants, a warm grow light under each shelf; reached from either side. Long along z.</summary>
    private static Part GrowRack()
    {
        var p = Fitting("Cloche Grow Rack", "A grow rack in the Cloche's hall: a dark steel frame of three shelves, each a tray of plants in nutrient film under a warm grow light; tended from either side.", "frame", Rib, "welder");
        const double hx = 0.5, hz = 1.2;
        Hidden(p, -hx, 0, -hz, hx, 2.75, hz, raw: true);
        foreach (var (x, z) in new[] { (-hx, -hz), (hx, -hz), (-hx, hz), (hx, hz) })
            Detail(p, x - Math.Sign(x) * 0.0 - 0.03, 0, z - 0.03, x + 0.03, 2.75, z + 0.03, Rib, "painted steel", raw: true);
        var tiers = new[] { 0.25, 1.05, 1.85 };
        for (var t = 0; t < tiers.Length; t++)
        {
            var y = tiers[t];
            Detail(p, -hx + 0.03, y, -hz + 0.03, hx - 0.03, y + 0.1, hz - 0.03, Tray, "plastic", raw: true);
            Detail(p, -hx + 0.06, y + 0.1, -hz + 0.06, hx - 0.06, y + 0.115, hz - 0.06, "#3d5a6a", "plastic", raw: true); // (the nutrient film)
            Plants(p, -hx + 0.06, -hz + 0.06, hx - 0.06, hz - 0.06, y + 0.115, 2, 6, t == 1 ? 0.24 : 0.3, t == 0 ? 0.28 : 0.18, t + 3, fruit: t == 0);
            Detail(p, -hx + 0.04, y + 0.66, -hz + 0.1, hx - 0.04, y + 0.7, hz - 0.1, Rib, "painted steel", raw: true);
            Detail(p, -0.08, y + 0.64, -hz + 0.15, 0.08, y + 0.66, hz - 0.15, Glow, glow: true, raw: true);
        }
        Detail(p, -hx + 0.04, 2.7, -hz + 0.04, hx - 0.04, 2.75, hz - 0.04, Rib, "painted steel", raw: true);
        CylZ(p, hx - 0.1, 0.18, -hz, hz, 0.04, 0.04, Pipe, "painted steel", 6, raw: true);
        p.Sock("bottom", [0, 0, 0], "-y");
        return p.Stats(450, 400, 3, 3.0);
    }

    /// <summary>A grow bed for the greenhouses: a waist-high cream trough of soil on dark legs, rows of plants, a misting line over it. Long along z.</summary>
    private static Part GrowBed(bool seed)
    {
        var p = Fitting(seed ? "Cloche Seed Bed" : "Cloche Grow Bed", seed
            ? "A seed bed in the Cloche's greenhouses: a cream trough on dark legs of seedling trays, many small plants in rows."
            : "A grow bed in the Cloche's greenhouses: a cream trough of soil on dark legs, rows of full-grown plants and a misting line over them.", "frame", Tray, "welder");
        const double hx = 0.5, hz = 0.9;
        Hidden(p, -hx, 0, -hz, hx, 0.95, hz, raw: true);
        foreach (var (x, z) in new[] { (-hx + 0.08, -hz + 0.1), (hx - 0.08, -hz + 0.1), (-hx + 0.08, hz - 0.1), (hx - 0.08, hz - 0.1) })
            Detail(p, x - 0.035, 0, z - 0.035, x + 0.035, 0.62, z + 0.035, Rib, "painted steel", raw: true);
        Detail(p, -hx + 0.1, 0.25, -hz + 0.1, hx - 0.1, 0.29, hz - 0.1, Rib, "painted steel", raw: true);
        Solid(p, -hx, 0.62, -hz, hx, 0.88, hz, Tray, "plastic", raw: true);
        Detail(p, -hx + 0.05, 0.88, -hz + 0.05, hx - 0.05, 0.9, hz - 0.05, seed ? "#2d3a33" : Soil, seed ? "plastic" : "dirt", raw: true);
        if (seed)
        {
            for (var k = 0; k < 3; k++)
                Detail(p, -hx + 0.06, 0.9, -hz + 0.06 + k * 0.6, hx - 0.06, 0.93, -hz + 0.6 + k * 0.6, "#2a2c2a", "plastic", raw: true);
            Plants(p, -hx + 0.08, -hz + 0.08, hx - 0.08, hz - 0.08, 0.93, 4, 8, 0.09, 0.07, 1);
        }
        else
        {
            Plants(p, -hx + 0.06, -hz + 0.06, hx - 0.06, hz - 0.06, 0.9, 2, 4, 0.3, 0.26, 2, fruit: true);
            Detail(p, -0.015, 0.9, -hz + 0.02, 0.015, 1.5, -hz + 0.05, Pipe, "painted steel", raw: true);
            Detail(p, -0.02, 1.5, -hz + 0.02, 0.02, 1.53, hz - 0.02, Pipe, "painted steel", raw: true);
        }
        Detail(p, -hx - 0.012, 0.66, -0.25, -hx + 0.004, 0.82, 0.25, Leaf2, "matte paint", raw: true); // (a tag)
        p.Sock("bottom", [0, 0, 0], "-y");
        return p.Stats(300, 300, 2, 3.0);
    }

    /// <summary>A ladder: dark rails, rungs, a yellow hoop over its top; its back socket on the face it stands against.</summary>
    private static Part Ladder(string name, double rise, double over, double sockY, string desc)
    {
        var p = Fitting(name, desc, "frame", Rib, "welder");
        var top = rise + over;
        Hidden(p, -0.3, 0, -0.19, 0.3, top, -0.005, raw: true);
        foreach (var x in new[] { -0.25, 0.25 })
            Detail(p, x - 0.03, 0, -0.18, x + 0.03, top, -0.12, Rib, "painted steel", raw: true);
        for (var y = 0.28; y < rise + 0.1; y += 0.28)
            Detail(p, -0.22, y - 0.02, -0.165, 0.22, y + 0.02, -0.135, Grey, "painted steel", raw: true);
        Detail(p, -0.3, top - 0.05, -0.19, 0.3, top, -0.11, Yellow, "painted steel", raw: true);
        p.Sock("back", [0, Py.Round(sockY, 3), -0.19], "-z");
        p.Set(("ladder", Py.Round(rise, 3)));
        return p.Stats(50 + rise * 12, 400, 6, 3.0);
    }

    /// <summary>The side door: a red leaf with a window that slides up inside the bow.</summary>
    private static Part SideDoor()
    {
        var p = Fitting("Cloche Side Door", "The Cloche's side door off the port step: a red leaf with a small window that slides up inside the bow. F opens it.", "door", Red, "welder");
        const double sy = DoorTop + 0.06; // (the socket: the leaf's top edge, on the bow's inner face)
        double y0 = FloorY - sy + 0.01, y1 = 0, hw = (DoorX1 - DoorX0) / 2 + 0.06;
        Hidden(p, -hw, -0.1, 0, hw, 0, 0.03, raw: true);
        Solid(p, -hw, y0, 0.03, hw, y1, 0.095, Red, "painted plating", raw: true);
        Detail(p, -0.22, y1 - 0.75, 0.095, 0.22, y1 - 0.3, 0.11, GlassC, glass: true, raw: true);
        Detail(p, -0.26, y1 - 0.79, 0.09, 0.26, y1 - 0.26, 0.104, Rib, "painted steel", raw: true);
        Detail(p, hw - 0.2, y0 + 1.0, 0.095, hw - 0.12, y0 + 1.15, 0.15, Grey, "painted steel", raw: true);
        p.Sock("hang", [0, 0, 0], "-z");
        p.Set(("door", Kw(("kind", "slide"), ("axis", "+y"), ("travel", 2.15), ("seconds", 3.0), ("hang", "hang"))));
        return p.Stats(250, 2500, 20, 4.0);
    }

    // ---- welding the pieces

    private static readonly Dictionary<string, string> Owner = [];

    /// <summary>A socket on whichever of the parts has a solid or hidden box face there (facing dir): throws when none does.</summary>
    private static void SockOn(List<Part> parts, string name, double x, double y, double z, string dir)
    {
        var ax = dir[1] - 'x';
        var plus = dir[0] == '+';
        double[] at = [x, y, z];
        foreach (var q in parts)
            foreach (var b in q.Boxes)
            {
                if (Drawn(b))
                    continue;
                double[] mn = Vd(b, "min"), mx = Vd(b, "max");
                if (Math.Abs((plus ? mx[ax] : mn[ax]) - at[ax]) > 0.005)
                    continue;
                if (Enumerable.Range(0, 3).All(a => a == ax || (at[a] >= mn[a] - 0.005 && at[a] <= mx[a] + 0.005)))
                {
                    Sock(q, name, x, y, z, dir);
                    Owner[name] = (string)q.D["name"]!;
                    return;
                }
            }
        throw new InvalidOperationException($"cloche_parts: no box face for socket {name} at ({x}, {y}, {z}) {dir}");
    }

    /// <summary>A facing socket pair wherever two of the parts' solid boxes touch face to face (one pair a pair, on their biggest contact).</summary>
    private static void Connect(List<Part> parts)
    {
        var solid = parts.Select(q => q.Boxes.Where(b => !Drawn(b)).Select(b => (Mn: Vd(b, "min"), Mx: Vd(b, "max"))).ToList()).ToList();
        var count = new int[parts.Count];
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
                Sock(parts[i], $"j{count[i]++}", best.At[0], best.At[1], best.At[2], (best.IPlus ? "+" : "-") + axis);
                Sock(parts[j], $"j{count[j]++}", best.At[0], best.At[1], best.At[2], (best.IPlus ? "-" : "+") + axis);
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
                    if (da[1] == db[1] && da[0] != db[0] && ((string)s["name"]!).StartsWith('j') && ((string)t["name"]!).StartsWith('j'))
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
            throw new InvalidOperationException("cloche_parts: pieces joined to nothing: " + string.Join(", ", names.Where((n, i) => !seen.Contains(i))));
        return order;
    }

    // ---- where the fittings go

    private static readonly double[] RackZ = [-9.4, -6.8, -4.2, -1.6, 1.0, 3.6, 6.2];
    private static readonly double[] MidRackZ = [-5.4, -2.8, -0.2, 2.4, 5.0];
    private static readonly (double X, double Z)[] Jets = [(-1.6, -12.6), (1.6, -12.6), (-1.6, -5.4), (1.6, -5.4), (-1.6, 4.6), (1.6, 4.6), (-1.6, 12.6), (1.6, 12.6)];
    private static readonly (string Name, double X, double Y, double Z, string Dir, bool Turned)[] Lamps =
    [
        ("lampDoor", (DoorX0 + DoorX1) / 2, DoorTop + 0.45, BowZ, "-z", true),
        ("lampEntryL", -2.2, 4.6, BowIn, "+z", false), ("lampEntryR", 2.2, 4.6, BowIn, "+z", false),
        ("lampHallL", -2.4, 4.6, FbZ1, "+z", false), ("lampHallR", 2.4, 4.6, FbZ1, "+z", false),
        ("lampHallAftL", -2.4, 4.6, RoofEnd, "-z", true), ("lampHallAftR", 2.4, 4.6, RoofEnd, "-z", true),
        ("lampEngL", -2.4, 5.6, AftZ, "+z", false), ("lampEngR", 2.4, 5.6, AftZ, "+z", false),
    ];

    private static void Sockets(List<Part> st)
    {
        foreach (var s in new[] { -1, 1 })
            foreach (var z in RackZ)
                SockOn(st, $"rack{(s < 0 ? "P" : "S")}{z:0.0}", s * 4.15, FloorY, z, "+y");
        foreach (var z in MidRackZ)
            SockOn(st, $"rackM{z:0.0}", 0, FloorY, z, "+y");
        foreach (var s in new[] { -1, 1 })
            for (var row = 0; row < 2; row++)
                for (var j = 0; j < 11; j++)
                    SockOn(st, $"bed{(s < 0 ? "P" : "S")}{row}{j}", s * (row == 0 ? 1.0 : 3.4), RoofY, -13.0 + 2 * j, "+y");
        for (var k = 0; k < Jets.Length; k++)
            SockOn(st, $"jet{k}", Jets[k].X, Y0, Jets[k].Z, "-y");
        foreach (var l in Lamps)
            SockOn(st, l.Name, l.X, l.Y, l.Z, l.Dir);
        SockOn(st, "seat", -1.4, FloorY, -20.0, "+y");
        SockOn(st, "dash", -1.4, FloorY, -21.45, "+y");
        SockOn(st, "plant", 0, FloorY, 14.6, "+y");
        SockOn(st, "door", (DoorX0 + DoorX1) / 2, DoorTop + 0.06, BowIn, "+z");
        foreach (var s in new[] { -1, 1 })
            SockOn(st, $"stepLadder{(s < 0 ? "P" : "S")}", s * 4.1, 1.22, StepZ0, "-z");
        SockOn(st, "bowLadder", -4.55, 4.52, BalZ, "-z");
        foreach (var s in new[] { -1, 1 })
            SockOn(st, $"hatch{(s < 0 ? "P" : "S")}", s * (HoleX0 + HoleX1) / 2, 6.3, HoleZ1, "-z");
        foreach (var s in new[] { -1, 1 })
            SockOn(st, $"drive{(s < 0 ? "P" : "S")}", s * NacX, NacY, NacTail, "+z");
    }

    // ---- the blueprint

    private static int Paint(BuildingParts.Blueprint bp, int step, string paint, string? finish = null)
    {
        bp.Steps[step]["paint"] = paint;
        if (finish != null)
            bp.Steps[step]["finish"] = finish;
        return step;
    }

    private static PyDict Blueprint(Dictionary<string, PyDict> defs, List<string> order)
    {
        var bp = new BuildingParts.Blueprint(ShipName, "The farmers' greenhouse ship: a long red hull with dark ribs, two glass barrel-vault greenhouses full of grow beds on its roof, a cream bus-fronted cab on the bow with a railed walk on its roof, cream and red water tanks on its flanks and a nacelle each side of the taller aft block. Inside, the entry room behind the cab, a grow hall of rack towers, the engine room aft, and a ladder up into each greenhouse. Eight lift jets under the belly hold it up and the two nacelle drives push it along.", defs, maxSteps: 220);
        bp.Conduit = true;
        double[] zero = [0, 0, 0];
        var st = new Dictionary<string, int>();
        foreach (var name in order)
            st[name] = st.Count == 0 ? bp.First(name) : bp.Put(name, zero, 0);
        int On(string sock) => st[Owner[sock]];
        int Hang(string part, string sock, string by) => bp.Hang(part, On(sock), sock, by);
        int Turned(string part, string sock, string by) => bp.HangTurned(part, On(sock), sock, by, BuildingParts.RY180);
        for (var k = 0; k < Jets.Length; k++)
            Hang(LiftJetName, $"jet{k}", "top");
        foreach (var s in new[] { "P", "S" })
            Hang(DriveName, $"drive{s}", "front");
        var pilot = Hang("Cloche Driver Seat", "seat", "bottom");
        var dash = Hang("Cloche Dashboard", "dash", "bottom");
        bp.Steps[dash]["config"] = ShipParts.ShipFlight();
        bp.Wires.Add((pilot, dash));
        Hang("Cloche Power Plant", "plant", "bottom");
        Hang("Cloche Side Door", "door", "hang");
        foreach (var s in new[] { "P", "S" })
            Turned("Cloche Step Ladder", $"stepLadder{s}", "back");
        Turned("Cloche Bow Ladder", "bowLadder", "back");
        foreach (var s in new[] { "P", "S" })
            Turned("Cloche Hatch Ladder", $"hatch{s}", "back");
        foreach (var l in Lamps)
            if (l.Turned)
                Turned("Cloche Cage Lamp", l.Name, "back");
            else
                Hang("Cloche Cage Lamp", l.Name, "back");
        foreach (var s in new[] { "P", "S" })
            foreach (var z in RackZ)
                Hang("Cloche Grow Rack", $"rack{s}{z:0.0}", "bottom");
        foreach (var z in MidRackZ)
            Hang("Cloche Grow Rack", $"rackM{z:0.0}", "bottom");
        foreach (var s in new[] { "P", "S" })
            for (var row = 0; row < 2; row++)
                for (var j = 0; j < 11; j++)
                    Hang((j + row) % 2 == 0 ? "Cloche Grow Bed" : "Cloche Seed Bed", $"bed{s}{row}{j}", "bottom");
        return bp.Json();
    }

    public void Make(PartOutput o)
    {
        Owner.Clear();
        var structure = new List<Part>();
        for (var i = 0; i + 1 < Seg.Length; i++)
            structure.Add(Keel(i, Seg[i], Seg[i + 1]));
        structure.Add(Keel(Seg.Length - 1, AftZ, SternIn));
        foreach (var s in new[] { -1, 1 })
            for (var i = 0; i + 1 < Seg.Length; i++)
                structure.Add(Side(s, i, Seg[i], Seg[i + 1]));
        for (var i = 0; i + 1 < Seg.Length; i++)
            structure.Add(Roof(i, Seg[i], i == Seg.Length - 2 ? RoofEnd : Seg[i + 1]));
        structure.Add(Bow());
        structure.Add(Cab());
        structure.Add(Balcony());
        structure.Add(Step(-1));
        structure.Add(Step(1));
        structure.Add(ForeBulkhead());
        structure.Add(AftBulkhead());
        structure.Add(AftSide(-1));
        structure.Add(AftSide(1));
        structure.Add(AftRoof());
        structure.Add(Stern());
        foreach (var s in new[] { -1, 1 })
            for (var b = 0; b + 1 < Bays.Length; b++)
                structure.Add(Glasshouse(s, b));
        foreach (var s in new[] { -1, 1 })
        {
            structure.Add(Tank(s, true));
            structure.Add(Tank(s, false));
            structure.Add(Nacelle(s));
        }
        Sockets(structure);
        Connect(structure);
        var order = PlaceOrder(structure, "Cloche Keel 3");
        List<Part> parts =
        [
            .. structure,
            Drive(), LiftJet(), Dashboard(), DriverSeat(), PowerPlant(), Lamp(), GrowRack(), GrowBed(false), GrowBed(true), SideDoor(),
            Ladder("Cloche Step Ladder", FloorY, 0.9, 1.22, "The ladder up to a step beside the Cloche's cab from the ground. F climbs it."),
            Ladder("Cloche Bow Ladder", CabRoof1 - FloorY, 0.9, 4.52 - FloorY, "The ladder from the Cloche's port step up to the balcony across its bow. F climbs it."),
            Ladder("Cloche Hatch Ladder", RoofY - FloorY, 0.9, 6.3 - FloorY, "The ladder from the Cloche's grow hall up through the roof into a greenhouse. F climbs it."),
        ];
        var defs = new Dictionary<string, PyDict>();
        o.Own(Out);
        foreach (var p in parts)
        {
            var d = p.Json();
            var name = (string)d["name"]!;
            if (name.Length > 32)
                throw new InvalidOperationException($"cloche_parts: {name} is longer than 32 characters");
            if (p.Boxes.Count > 256)
                throw new InvalidOperationException($"cloche_parts: {name} has {p.Boxes.Count} boxes (256 at most)");
            defs.Add(name, d);
            o.File($"{Out}/{FileName(name)}", PyJson.Dumps(d, 2) + "\n");
        }
        BuildingParts.AddPartDefs(o, defs, Out);
        o.File($"{BpOut}/ClocheGreenhouseShip.json", PyJson.Dumps(Blueprint(defs, order), 2) + "\n");
    }
}
