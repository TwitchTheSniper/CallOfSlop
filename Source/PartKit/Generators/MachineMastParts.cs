using static CallOfSlop.PartKit.PartKitMath;
using static CallOfSlop.PartKit.MachineLookShort;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The parts every block machine shares (Constructs/Parts/Machines; design page drawings 19 to 24, MachineBlocks.cs). The Mast Block is
/// the shared 2 m cube: a lattice column cell that any family's machine takes in ("machineBlock" role "shared"). Stacked on a bed cell it
/// is that cell's column: a pad's stacking height, an oven's or a station's corner mast, the column a Crane Rail Block sits on. Its cap and
/// foot are seams, so a stacked column shows one foot at the bottom and one cap at the top; its face bracing is a seam too, so masts
/// welded side by side read as one wall of lattice. A lamp near the top of each face glows while its machine works. Outside a machine (a
/// shed's pillar, a crane's column) the lattice is all "inMachine" and a mast draws a solid square column instead ("bare"): a 1.6 m shaft
/// with two steps at a column's foot and two under its head.
/// </summary>
public sealed class MachineMastParts : IPartGenerator
{
    public string Name => "machine_mast_parts";

    private const string Out = "Constructs/Parts/Machines";
    public const string Graphite = "#4a5058"; // the lattice's tone (takes paint)

    /// <summary>The "machineBlock" key of a shared cell (no family: it goes with every machine).</summary>
    public static PyDict SharedKey() => Kw(("role", "shared"), ("cell", 2));

    /// <summary>A strut on the face plane across axis <paramref name="n"/> at <paramref name="at"/>, from (a0, b0) to (a1, b1) on the other two axes (in order), <paramref name="w"/> wide in the plane, <paramref name="d"/> deep out of it (outwards by <paramref name="sg"/>).</summary>
    public static PyDict Strut(int n, double at, int sg, double a0, double b0, double a1, double b1, double w, double d, string color, bool paintable = true)
    {
        int u = (n + 1) % 3, v = (n + 2) % 3;
        if (u > v)
            (u, v) = (v, u);
        double du = a1 - a0, dv = b1 - b0, len = Math.Sqrt(du * du + dv * dv);
        double pu = -dv / len * w / 2, pv = du / len * w / 2;
        var pts = new List<double[]>();
        foreach (var depth in new[] { at, at + sg * d })
            foreach (var (cu, cv) in new[] { (a0 + pu, b0 + pv), (a0 - pu, b0 - pv), (a1 + pu, b1 + pv), (a1 - pu, b1 - pv) })
            {
                var q = new double[3];
                q[n] = depth;
                q[u] = cu;
                q[v] = cv;
                pts.Add(q);
            }
        double[] mn = [.. Enumerable.Range(0, 3).Select(i => pts.Min(a => a[i]))];
        double[] mx = [.. Enumerable.Range(0, 3).Select(i => pts.Max(a => a[i]))];
        var b = Box(mn, mx, color, detail: true, shape: Kw(("shape", "hull"), ("points", pts.Select(a => (object?)a.Select(x => (object?)R(x)).ToList()).ToList())))!;
        if (!paintable)
            b["paintable"] = false;
        return b;
    }

    private static PyDict Seam(PyDict? box, params string[] faces) => box!.Update(("seam", faces.Length == 1 ? faces[0] : Py.L([.. faces.Cast<object?>()])));

    /// <summary>Drawn only while the mast is a machine's cell: a bare column of masts (a shed's pillar under a crane rail) is its posts, girts, foot and cap alone.</summary>
    private static PyDict M(PyDict? box) => box!.Update(("inMachine", true));

    /// <summary>Drawn only while the mast is NOT a machine's cell: the solid column a bare stack of masts shows instead of its lattice.</summary>
    private static PyDict Bare(PyDict? box) => box!.Update(("bare", true));

    /// <summary>
    /// The Mast Block: 2 x 2 x 2 m, origin at its foot's middle. Four angle posts at its corners, a ring of girts at the top and the foot,
    /// X bracing on each face between them, gusset plates and bolts at the joints, a cable tray up one post, a metre mark on each post,
    /// a status lamp near the top of each face, a maker's plate. The hidden cube is its collider and its sockets' faces.
    /// </summary>
    private static Part Mast()
    {
        var p = new Part("Mast Block", Small, "Extrusion",
            "The shared 2 m cube of the block machines: stack it on a machine's bed cell or a pad's tile for a column (a pad's stacking height, an oven's or a station's mast, a crane rail's column). Masts stack on each other; welded side by side they make a wall.",
            group: "Production", color: Graphite);
        const double e = 1.0, h = 2.0, post = 0.16, fl = 0.012; // (half side, height, the angle posts' legs, their flange)
        p.Add(Box([-e, 0, -e], [e, h, e], hidden: true));
        // The angle posts: two flanges each, 3 mm inside the cell so masts side by side never share a face. Cap and foot plates where a
        // column starts or ends (seams up and down), a girt ring at the top and the foot of each face (seams on that face).
        const double i = 0.997;
        foreach (var (sx, sz) in new[] { (1, 1), (1, -1), (-1, 1), (-1, -1) })
        {
            double x0 = sx > 0 ? i - post : -i, x1 = sx > 0 ? i : -i + post, z0 = sz > 0 ? i - post : -i, z1 = sz > 0 ? i : -i + post;
            double fx0 = sx > 0 ? i - fl - 0.004 : -i, fx1 = sx > 0 ? i : -i + fl + 0.004, fz0 = sz > 0 ? i - fl - 0.004 : -i, fz1 = sz > 0 ? i : -i + fl + 0.004;
            p.Add(M(Box([x0, 0.003, fz0], [x1, h - 0.003, fz1], detail: true))); // (the flange on the z face)
            p.Add(M(Box([fx0, 0.003, z0], [fx1, h - 0.003, z1], detail: true))); // (the flange on the x face)
            // A dark mark at every metre: the old pad posts' marks (drawing 20), on the post's outer corner.
            foreach (var y in new[] { 1.0 })
                p.Add(M(L([Math.Min(x0, x1) - 0.004, y - 0.02, Math.Min(z0, z1) - 0.004], [Math.Max(x0, x1) + 0.004, y + 0.02, Math.Max(z0, z1) + 0.004], MachineLook.Ink)));
            // The foot plate under each post (a seam down: a stacked mast stands on the one below) and its two anchor bolts.
            double cx = sx * (i - post / 2), cz = sz * (i - post / 2);
            p.Add(M(Seam(L([cx - 0.13, 0.003, cz - 0.13], [cx + 0.13, 0.04, cz + 0.13], MachineLook.Base), "-y")));
            foreach (var (bx, bz) in new[] { (cx - sx * 0.09, cz), (cx, cz - sz * 0.09) })
                p.Add(M(Seam(L([bx - 0.022, 0.04, bz - 0.022], [bx + 0.022, 0.07, bz + 0.022], MachineLook.Steel, "steel", shape: Kw(("shape", "cylinder"), ("segments", 6), ("front", "+y"))), "-y")));
            // The amber cap on top of each post (a seam up: a column shows its caps once, at its top).
            p.Add(M(Seam(L([Math.Min(x0, x1) - 0.006, h - 0.04, Math.Min(z0, z1) - 0.006], [Math.Max(x0, x1) + 0.006, h - 0.003, Math.Max(z0, z1) + 0.006], MachineLook.Accent), "+y")));
        }
        // Each face: a top and a foot girt between the posts, X bracing between them, a gusset plate with four bolts at the crossing,
        // and the lamp under the top girt. All a seam on that face (masts welded side by side share one braced wall, drawn once).
        foreach (var (n, sg, face) in new[] { (0, 1, "+x"), (0, -1, "-x"), (2, 1, "+z"), (2, -1, "-z") })
        {
            var at = sg * (i - 0.006);
            double Lo(double d) => sg > 0 ? at - d : at;
            double Hi(double d) => sg > 0 ? at : at + d;
            PyDict? Face(double d, double y0, double y1, double a0, double a1, string color, string? finish = null, bool paint = false)
            {
                double[] mn = n == 0 ? [Lo(d), y0, a0] : [a0, y0, Lo(d)];
                double[] mx = n == 0 ? [Hi(d), y1, a1] : [a1, y1, Hi(d)];
                return L(mn, mx, color, finish, paint);
            }
            var w = i - post; // (the girts run between the posts' flanges)
            p.Add(M(Seam(Face(0.1, h - 0.17, h - 0.05, -w + 0.004, w - 0.004, Graphite, paint: true), face)));
            p.Add(M(Seam(Face(0.1, 0.05, 0.17, -w + 0.004, w - 0.004, Graphite, paint: true), face)));
            // X bracing, a flat bar each way (the second 6 mm further out, so the two never share a plane where they cross).
            double ya = 0.17, yb = h - 0.17, aa = -w + 0.02, ab = w - 0.02;
            PyDict X(double y0, double a0, double y1, double a1, double out_) // (Strut's other two axes, in order: x face (y, z), z face (x, y))
                => n == 0 ? Strut(0, at - sg * out_, -sg, y0, a0, y1, a1, 0.09, 0.022, Graphite) : Strut(2, at - sg * out_, -sg, a0, y0, a1, y1, 0.09, 0.022, Graphite);
            p.Add(M(Seam(X(ya, aa, yb, ab, 0.05), face)));
            p.Add(M(Seam(X(ya, ab, yb, aa, 0.028), face)));
            // The gusset at the crossing and its bolts.
            p.Add(M(Seam(Face(0.014, h / 2 - 0.13, h / 2 + 0.13, -0.13, 0.13, MachineLook.Base), face)));
            foreach (var (by, ba) in new[] { (0.08, 0.08), (0.08, -0.08), (-0.08, 0.08), (-0.08, -0.08) })
                p.Add(M(Seam(Face(0.03, h / 2 + by - 0.018, h / 2 + by + 0.018, ba - 0.018, ba + 0.018, MachineLook.Steel, "steel"), face)));
            // Gussets at the girts' ends, two bolts each.
            foreach (var ga in new[] { -1, 1 })
                foreach (var (gy0, gy1) in new[] { (h - 0.28, h - 0.04), (0.04, 0.28) })
                {
                    double a0 = ga > 0 ? w - 0.2 : -w + 0.004, a1 = ga > 0 ? w - 0.004 : -w + 0.2;
                    p.Add(M(Seam(Face(0.012, gy0, gy1, a0, a1, MachineLook.Base), face)));
                    p.Add(M(Seam(Face(0.028, (gy0 + gy1) / 2 - 0.018, (gy0 + gy1) / 2 + 0.018, (a0 + a1) / 2 - 0.018, (a0 + a1) / 2 + 0.018, MachineLook.Steel, "steel"), face)));
                }
            // The status lamp under the top girt: a dark housing, a lens that glows while its machine works (drawing 21: a cell shows its Core's work).
            p.Add(M(Seam(Face(0.05, h - 0.33, h - 0.19, -0.11, 0.11, MachineLook.Base), face)));
            p.Add(M(Seam(MachineLook.Work(Face(0.064, h - 0.31, h - 0.21, -0.08, 0.08, MachineLook.Accent), "glow"), face)));
        }
        // A cable tray up the +x/+z post's inner face, and a maker's plate on the -z face's foot girt.
        p.Add(M(Seam(L([i - post - 0.07, 0.17, i - fl - 0.06], [i - post - 0.004, h - 0.17, i - fl - 0.012], MachineLook.Base), "+z")));
        foreach (var y in new[] { 0.5, 1.0, 1.5 })
            p.Add(M(Seam(L([i - post - 0.08, y - 0.02, i - fl - 0.07], [i - post + 0.004, y + 0.02, i - fl - 0.012], MachineLook.Steel, "steel"), "+z")));
        p.Add(M(Seam(L([-0.3, 0.07, -i - 0.022], [0.3, 0.15, -i + 0.004], MachineLook.In), "-z")));
        p.Add(M(Seam(L([-0.25, 0.095, -i - 0.03], [0.25, 0.125, -i - 0.016], MachineLook.Ink), "-z")));
        // Outside a machine (a shed's pillar, a crane's column) the lattice gives way to a solid square column (Sean, 2026-11-22: see-through
        // pillars cluttered the works): a 1.6 m shaft, two steps at its foot and two under its top. The steps are seams, so a stacked column
        // shows one stepped foot and one stepped head; the shafts meet flush between cells. Each step overlaps the next 2 cm, so no two
        // faces share a plane; 5 boxes a cell, 3 between a column's ends.
        p.Add(Bare(L([-0.8, 0, -0.8], [0.8, h, 0.8], Graphite, paintable: true)));
        p.Add(Bare(Seam(L([-0.975, 0.003, -0.975], [0.975, 0.12, 0.975], MachineLook.Base), "-y")));
        p.Add(Bare(Seam(L([-0.885, 0.10, -0.885], [0.885, 0.26, 0.885], MachineLook.Base), "-y")));
        p.Add(Bare(Seam(L([-0.885, h - 0.26, -0.885], [0.885, h - 0.10, 0.885], MachineLook.Base), "+y")));
        p.Add(Bare(Seam(L([-0.975, h - 0.12, -0.975], [0.975, h - 0.003, 0.975], MachineLook.Base), "+y")));
        p.Sock("top", [0, h, 0], "+y");
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Sock("+x", [e, 1, 0], "+x");
        p.Sock("-x", [-e, 1, 0], "-x");
        p.Sock("+z", [0, 1, e], "+z");
        p.Sock("-z", [0, 1, -e], "-z");
        // Low on each face, at an Assembly Bed Block's side sockets: a mast stands on the floor beside an Assembler's bed, outside it (AssemblerParts).
        var low = AssemblerParts.BedTop / 2;
        p.Sock("+x.low", [e, low, 0], "+x");
        p.Sock("-x.low", [-e, low, 0], "-x");
        p.Sock("+z.low", [0, low, e], "+z");
        p.Sock("-z.low", [0, low, -e], "-z");
        p.Stats(900, 2400, 8, 12);
        p.Set(("machineBlock", SharedKey()), ("centreSockets", false), ("collider", "boxes"), ("seamSame", true)); // (seamSame: a bare column shows one foot and one cap)
        return p;
    }

    public void Make(PartOutput o) => WriteParts(o, Out, [("", Mast())]);
}
