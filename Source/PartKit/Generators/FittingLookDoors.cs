using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// Bulkhead doors from the style sheet's high-tech company round (Karl, 2026-11-20: "Add the doors"): door B (Ceramic, approved) and
/// doors A (Graphite) and C (Armoured) from its Uncategorised shelf. Each is a frame with a 1 x 2.1 m opening and its leaf: weld the
/// frame, then hang the leaf on the frame's jamb socket (B has two leaves, one on each jamb, meeting in the middle). A leaf slides
/// sideways into the frame and on into the wall beside it, like the Bulkhead Door. The frame's light sits in shadow gaps, never on a
/// face; every face detail is on both faces. Frame: 1.7 x 2.6 x 0.4 m, front +z, sill on the floor. Written by FittingLookParts.
/// </summary>
internal static class FittingLookDoors
{
    private const string Group = "Building", Section = "Doors and gates";
    private const double W = 0.85, Ow = 0.5, Oh = 2.14, Sill = 0.04, D = 0.2, Gap = 0.02, T = 0.04, Top = 2.6;
    private const double Mid = (Sill + Oh) / 2; // (the opening's middle: the jamb sockets, the leaves' origin)

    /// <summary>A style: its name and palette (the sheet's takes A, B and C).</summary>
    private sealed record Style(string Name, string Body, string Finish, string Panel, string Trim, string TrimF, string Dark, string Light, string Dim, double Ch);

    private static readonly Style Graphite = new("Graphite", "#2f3338", "matte paint", "#3c4148", "#a3aab1", "aluminium", "#141619", "#ffdcae", "#5a4a36", 0.025);
    private static readonly Style Ceramic = new("Ceramic", "#d3d7da", "gloss paint", "#c0c5ca", "#5d646c", "aluminium", "#1e2226", "#eaf4ff", "#47525e", 0.055);
    private static readonly Style Armoured = new("Armoured", "#4b5054", "painted plating", "#5a5f63", "#2a2d30", "steel", "#131517", "#ffb867", "#5c4128", 0.07);

    /// <summary>A box with every edge chamfered by c: the hull of three points per corner.</summary>
    private static IEnumerable<double[]> Ch(double x0, double y0, double z0, double x1, double y1, double z1, double c)
    {
        (x0, x1) = (Math.Min(x0, x1), Math.Max(x0, x1));
        (y0, y1) = (Math.Min(y0, y1), Math.Max(y0, y1));
        (z0, z1) = (Math.Min(z0, z1), Math.Max(z0, z1));
        c = Math.Min(c, Math.Min(x1 - x0, Math.Min(y1 - y0, z1 - z0)) * 0.45);
        foreach (var (X, sx) in new[] { (x0, -1.0), (x1, 1.0) })
            foreach (var (Y, sy) in new[] { (y0, -1.0), (y1, 1.0) })
                foreach (var (Z, sz) in new[] { (z0, -1.0), (z1, 1.0) })
                {
                    yield return [X, Y - sy * c, Z - sz * c];
                    yield return [X - sx * c, Y, Z - sz * c];
                    yield return [X - sx * c, Y - sy * c, Z];
                }
    }

    private static void Shell(FittingLookParts.Kit k, double x0, double y0, double z0, double x1, double y1, double z1, string col, string finish, double c) =>
        k.Hull(Ch(x0, y0, z0, x1, y1, z1, c), col, finish);

    /// <summary>A flush bolt head on a face at z (fz: which way the face looks).</summary>
    private static void Bolt(FittingLookParts.Kit k, Style s, double x, double y, double z, double fz) =>
        k.Hull(FittingLookParts.Cyl(x, y, 0.012, 0, 1, 6).Select(q => new[] { q[0], q[2], z + fz * q[1] * 0.012 }), s.Trim, "steel");

    // ---- The frame.

    private static Part Frame(Style s, string desc, bool twoLeaves)
    {
        var p = new Part($"{s.Name} Bulkhead Frame", Medium, Section, desc, role: "frame", tool: "welder", material: "steel", color: s.Body, group: Group);
        p.Set(("finish", s.Finish), ("collider", "boxes")); // (the hidden jambs and head collide; the chamfered looks don't)
        p.Add(Box([-W, 0, -D], [-Ow, Top, D], s.Body, hidden: true));
        p.Add(Box([Ow, 0, -D], [W, Top, D], s.Body, hidden: true));
        p.Add(Box([-Ow, Oh, -D], [Ow, Top, D], s.Body, hidden: true));
        var k = new FittingLookParts.Kit(p);
        var c = s.Ch * 0.6;
        Shell(k, -W, 0, -D, -Ow, Top, D, s.Body, s.Finish, c);
        Shell(k, Ow, 0, -D, W, Top, D, s.Body, s.Finish, c);
        Shell(k, -Ow - 0.01, Oh, -D + 0.004, Ow + 0.01, Top - 0.004, D - 0.004, s.Body, s.Finish, s.Ch * 0.4);
        Shell(k, -Ow + 0.004, 0, -D + 0.02, Ow - 0.004, Sill, D - 0.02, s.Trim, s.TrimF, 0.004); // (the sill: a look, so nothing trips on it)
        // Cut top corners (A small, B wide with its bottom corners too; C square).
        var g = s == Ceramic ? 0.3 : s == Graphite ? 0.14 : 0.0;
        if (g > 0)
            foreach (var sx in new[] { -1.0, 1.0 })
                k.Face(-D + 0.006, D - 0.006, [(sx * (Ow + 0.004), Oh - g), (sx * (Ow + 0.004), Oh + 0.004), (sx * (Ow - g), Oh + 0.004)], s.Body, s.Finish);
        if (s == Ceramic)
            foreach (var sx in new[] { -1.0, 1.0 })
                k.Face(-D + 0.006, D - 0.006, [(sx * (Ow + 0.004), 0.12), (sx * (Ow + 0.004), Sill - 0.004), (sx * (Ow - 0.084), Sill - 0.004)], s.Body, s.Finish);
        foreach (var fz in new[] { 1.0, -1.0 })
        {
            double Z(double v) => fz * v;
            if (s == Graphite) // (a reveal: a trim bezel stood proud round the opening, the light in the gap behind it; a downlight in the head)
            {
                foreach (var sx in new[] { -1.0, 1.0 })
                {
                    Shell(k, sx * (Ow + 0.05), Sill, Z(D - 0.004), sx * (Ow + 0.07), Oh + 0.06, Z(D + 0.018), s.Trim, s.TrimF, 0.004);
                    k.Glow(Math.Min(sx * (Ow + 0.072), sx * (Ow + 0.084)), 0.06, Math.Min(Z(D - 0.008), Z(D + 0.004)), Math.Max(sx * (Ow + 0.072), sx * (Ow + 0.084)), Oh + 0.04, Math.Max(Z(D - 0.008), Z(D + 0.004)), s.Light);
                }
                Shell(k, -Ow - 0.07, Oh + 0.06, Z(D - 0.004), Ow + 0.07, Oh + 0.08, Z(D + 0.018), s.Trim, s.TrimF, 0.004);
                k.Glow(-Ow - 0.05, Oh + 0.082, Math.Min(Z(D - 0.008), Z(D + 0.004)), Ow + 0.05, Oh + 0.094, Math.Max(Z(D - 0.008), Z(D + 0.004)), s.Light);
                k.Glow(-0.3, Oh - 0.004, Math.Min(Z(0.08), Z(0.16)), 0.3, Oh + 0.008, Math.Max(Z(0.08), Z(0.16)), s.Light);
            }
            else if (s == Ceramic) // (a cool line round the portal's inner edge, set in the jambs and head)
            {
                k.Glow(-Ow + 0.03, Oh - 0.004, Math.Min(Z(0.14), Z(0.17)), Ow - 0.03, Oh + 0.008, Math.Max(Z(0.14), Z(0.17)), s.Light);
                foreach (var sx in new[] { -1.0, 1.0 })
                    k.Glow(sx > 0 ? Ow - 0.004 : -Ow - 0.008, 0.2, Math.Min(Z(0.14), Z(0.17)), sx > 0 ? Ow + 0.008 : -Ow + 0.004, Oh - 0.36, Math.Max(Z(0.14), Z(0.17)), s.Light);
            }
            else // (Armoured: bolted jamb plates, rubber corner bumpers, a ram over each side of the head)
                foreach (var sx in new[] { -1.0, 1.0 })
                {
                    Shell(k, sx * (Ow + 0.06), 0.3, Z(D - 0.004), sx * 0.8, 2.3, Z(D + 0.02), s.Panel, s.Finish, 0.012);
                    foreach (var y in new[] { 0.36, 1.3, 2.24 })
                        foreach (var bx in new[] { Ow + 0.1, 0.76 })
                            Bolt(k, s, sx * bx, y, Z(D + 0.016), fz);
                    Shell(k, sx * (W - 0.012), 0.02, Z(D - 0.012), sx * (W + 0.03), 2.4, Z(D + 0.03), "#1d2023", "rubber", 0.012);
                    k.Hull(FittingLookParts.Cyl(0, 0, 0.03, 0, 1, 8).Select(q => new[] { sx * (0.25 + q[1] * 0.35), Oh + 0.12 + q[0], Z(D + 0.05) + q[2] }), s.Trim, "steel");
                    Shell(k, sx * 0.58, Oh + 0.07, Z(D - 0.004), sx * 0.66, Oh + 0.17, Z(D + 0.05), s.Dark, "steel", 0.004);
                }
        }
        if (s == Armoured) // (amber light set into the sill)
            k.Glow(-Ow + 0.03, 0.03, -0.16, Ow - 0.03, 0.046, 0.16, s.Light);
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Sock("top", [0, Top, 0], "+y");
        p.Sock("-x", [-W, 1.3, 0], "-x");
        p.Sock("+x", [W, 1.3, 0], "+x");
        p.Sock("jamb", [-Ow, Mid, 0], "+x", Small); // (small sockets: the leaves are small parts)
        if (twoLeaves)
            p.Sock("jamb2", [Ow, Mid, 0], "-x", Small);
        p.Set(("centreSockets", false));
        return p.Stats(400, 1200, s == Armoured ? 20 : 10, 5.0);
    }

    // ---- The leaves. A leaf's origin is its hang edge at the opening's mid-height; it slides back over its hang side.

    private static Part Leaf(Style s, string name, double dw, int side, string desc)
    {
        var p = new Part(name, Small, Section, desc, role: "door", tool: "wrench", material: "steel", color: s.Panel, group: Group);
        p.Set(("finish", s.Finish), ("collider", "boxes"));
        var dh = Oh - Sill - 2 * Gap;
        double X(double v) => side * v; // (side +1: the leaf runs +x from its hang edge; -1: -x)
        double Lo(double a, double b) => Math.Min(X(a), X(b));
        double Hi(double a, double b) => Math.Max(X(a), X(b));
        p.Add(Box([Lo(0, dw), -dh / 2, -T], [Hi(0, dw), dh / 2, T], s.Panel));
        var k = new FittingLookParts.Kit(p);
        if (s != Ceramic)
            foreach (var y in new[] { -0.49, 0.41 }) // (pressed ribs across both faces)
                Shell(k, Lo(0.08, dw - 0.08), y, -T - 0.016, Hi(0.08, dw - 0.08), y + 0.1, T + 0.016, s.Panel, s.Finish, 0.01);
        if (s == Ceramic) // (the meeting edge lit at hand height, both faces)
            k.Glow(Lo(dw - 0.012, dw - 0.002), -0.09, -T - 0.006, Hi(dw - 0.012, dw - 0.002), 0.21, T + 0.006, s.Light);
        // The pull, on both faces (what F takes hold of), and a lock lamp over it.
        var gx = s == Ceramic ? dw - 0.1 : dw - 0.18;
        p.Add(Box([Lo(gx, gx + 0.1), -0.14, -T - 0.014], [Hi(gx, gx + 0.1), 0.06, T + 0.014], s.Dark, detail: true, shape: Kw(("use", true), ("paintable", false))));
        k.Glow(Lo(gx + 0.03, gx + 0.07), 0.21, -T - 0.012, Hi(gx + 0.03, gx + 0.07), 0.25, T + 0.012, s.Dim);
        p.Add(Box([Lo(-Gap, 0), -0.2, -0.01], [Hi(-Gap, 0), 0.2, 0.01], hidden: true)); // (the hang socket's strip)
        p.Sock("hang", [X(-Gap), 0, 0], side > 0 ? "-x" : "+x");
        p.Sock("front", [X(dw / 2), 0, T], "+z");
        p.Sock("back", [X(dw / 2), 0, -T], "-z");
        p.Set(("door", Kw(("kind", "slide"), ("axis", side > 0 ? "-x" : "+x"), ("travel", Py.Round(dw + Gap, 2)), ("seconds", 1.0), ("hang", "hang"))));
        var area = dw * dh;
        return p.Stats(Py.Round(65 * area, 1), Py.Round(320 * area, 0), s == Armoured ? 50 : 40, 2.5);
    }

    public static IEnumerable<Part> All()
    {
        const string hang = "Weld the frame into a wall, then hang its door on the frame's jamb socket.";
        var single = 2 * Ow - 2 * Gap;
        var half = Ow - Gap - 0.005;
        yield return Frame(Graphite, $"A graphite bulkhead frame for a 1 x 2.1 m doorway: cut top corners and warm light in a reveal round the opening. {hang}", false);
        yield return Leaf(Graphite, "Graphite Bulkhead Door", single, 1, "The Graphite Bulkhead Frame's door: slides into the frame and the wall beside it. Hang it on the frame's jamb socket.");
        yield return Frame(Ceramic, $"A white rounded bulkhead portal for a 1 x 2.1 m doorway with a cool light line round it and two doors meeting in the middle. {hang.Replace("its door", "a door on each jamb")}", true);
        yield return Leaf(Ceramic, "Ceramic Bulkhead Door Left", half, 1, "The Ceramic Bulkhead Frame's left door: hang it on the frame's left jamb socket; it slides away from the middle.");
        yield return Leaf(Ceramic, "Ceramic Bulkhead Door Right", half, -1, "The Ceramic Bulkhead Frame's right door: hang it on the frame's right jamb socket; it slides away from the middle.");
        yield return Frame(Armoured, $"A thick plated bulkhead frame for a 1 x 2.1 m doorway: bolted jamb plates, rubber bumpers, rams over the head, amber light in the sill. {hang}", false);
        yield return Leaf(Armoured, "Armoured Bulkhead Door", single, 1, "The Armoured Bulkhead Frame's door: slides into the frame and the wall beside it. Hang it on the frame's jamb socket.");
    }
}
