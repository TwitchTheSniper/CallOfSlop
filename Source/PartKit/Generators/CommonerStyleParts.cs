using static CallOfSlop.PartKit.PartKitMath;
using K = CallOfSlop.PartKit.Generators.FittingLookParts;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The commoners' style, drafts for a copy of the Part Style Sheet (Karl, 2026-11-20: "winnebago vibe. Think space commoner,
/// they're just trying to get by, it looks as though it's way past its expiry date and from an older generation"). One draft
/// of each of 25 kinds of ship equipment (the sheet's step 4, "the set"), for feedback one by one. Not merged; nothing here is in
/// the Parts tab (group StyleCommoner is unlisted, Constructs/Categories.json). Shown by --preview-gallery DIR --group StyleCommoner.
///
/// The look: an old camper's cream and tan with faded brown, orange and mustard stripes, wood-grain laminate, dull chrome,
/// tired vinyl, grey tape over the cracks, patches riveted on in another colour, rust where water sat. Lamps are warm
/// incandescent and few. Modules keep the standard Module Plug (FittingLookParts.Reactor, module-sockets.md): 1x1, the
/// magazine 2x2. Free-standing pieces: front +z, the wall behind -z, the floor at y 0.
/// </summary>
public sealed class CommonerStyleParts : IPartGenerator
{
    public string Name => "style_commoner";

    private const string Out = "Constructs/Parts/StylesCommoner";
    internal const string Group = "StyleCommoner";
    private const string Section = "Commoner style";

    // The camper palette, all faded.
    private const string Cream = "#d6c9a8", Beige = "#b5a37f", Tan = "#8c7654", Brown = "#5b3f28", Orange = "#b25a2c", Mustard = "#c2962f";
    private const string Avocado = "#6d763c", Rust = "#7a4a2c", Wood = "#7b5634", WoodDark = "#5a3d24", Tape = "#8b8c88", Chrome = "#a3a199";
    private const string Rubber = "#2a2826", Grime = "#3b3631", Vinyl = "#6b4a33", Olive = "#565b3a", Teal = "#4f7c76", BottleGreen = "#3e5a3a";
    private const string Bulb = "#ffc978", PilotRed = "#d9533b", PilotGreen = "#9fd46a", Phosphor = "#8fd88a", Ceramic = "#e4ddcc";

    // ---- Shapes

    /// <summary>A free-standing piece: a hidden solid (what collides), a floor socket and a wall socket.</summary>
    private static K.Kit Free(string name, string desc, double[] mn, double[] mx, string role = "frame", bool wall = true, string material = "steel")
    {
        var p = new Part(name, Small, Section, desc, role: role, tool: "wrench", material: material, color: Cream, group: Group);
        p.Set(("finish", "worn paint"), ("collider", "boxes"));
        p.Add(Box(mn, mx, Cream, hidden: true));
        p.Sock("bottom", [R(0.5 * (mn[0] + mx[0])), mn[1], R(0.5 * (mn[2] + mx[2]))], "-y");
        if (wall)
            p.Sock("wall", [R(0.5 * (mn[0] + mx[0])), R(Math.Min(mx[1], 0.25) / 2), mn[2]], "-z");
        p.Stats(40, 200, 2, 2.0);
        return new K.Kit(p);
    }

    private static double R(double v) => Py.Round(v, 4);

    /// <summary>A frustum along an axis (0 x, 1 y, 2 z) from t0 (radius r0) to t1 (radius r1), centred on (a, b) in the other two axes.</summary>
    private static IEnumerable<double[]> Frustum(int axis, double a, double b, double t0, double r0, double t1, double r1, int sides = 10, double turn = 0.5)
    {
        foreach (var (t, r) in new[] { (t0, r0), (t1, r1) })
            for (var i = 0; i < sides; i++)
            {
                var ang = (i + turn) * 2 * Math.PI / sides;
                double u = a + r * Math.Cos(ang), v = b + r * Math.Sin(ang);
                yield return axis switch { 0 => [t, u, v], 1 => [u, t, v], _ => [u, v, t] };
            }
    }

    private static void Tube(K.Kit k, int axis, double a, double b, double t0, double t1, double r, string col, string finish, int sides = 8) =>
        k.Hull(Frustum(axis, a, b, t0, r, t1, r, sides), col, finish);

    /// <summary>A strip of grey tape over a crack: flat on a face whose normal is +z at z, from (x0, y0) to (x1, y1), 45 mm wide.</summary>
    private static void TapeZ(K.Kit k, double z, double x0, double y0, double x1, double y1, double w = 0.045)
    {
        double dx = x1 - x0, dy = y1 - y0, l = Math.Sqrt(dx * dx + dy * dy), nx = -dy / l * w / 2, ny = dx / l * w / 2;
        k.Hull([[x0 + nx, y0 + ny, z], [x0 - nx, y0 - ny, z], [x1 + nx, y1 + ny, z], [x1 - nx, y1 - ny, z],
                [x0 + nx, y0 + ny, z + 0.011], [x0 - nx, y0 - ny, z + 0.011], [x1 + nx, y1 + ny, z + 0.011], [x1 - nx, y1 - ny, z + 0.011]], Tape, "fabric");
    }

    /// <summary>A row of rivet heads along x at height y on the +z face at z.</summary>
    private static void RivetsX(K.Kit k, double x0, double x1, double y, double z, int n)
    {
        for (var i = 0; i < n; i++)
        {
            var x = x0 + (x1 - x0) * i / Math.Max(1, n - 1);
            k.Box(x - 0.008, y - 0.008, z, x + 0.008, y + 0.008, z + 0.011, Chrome, "steel");
        }
    }

    /// <summary>A riveted patch plate on the +z face at z, in another colour than what it covers.</summary>
    private static void PatchZ(K.Kit k, double x0, double y0, double x1, double y1, double z, string col)
    {
        k.Box(x0, y0, z, x1, y1, z + 0.012, col, "rusty paint");
        RivetsX(k, x0 + 0.02, x1 - 0.02, y0 + 0.02, z + 0.012, 3);
        RivetsX(k, x0 + 0.02, x1 - 0.02, y1 - 0.02, z + 0.012, 3);
    }

    /// <summary>A round dial gauge facing +z: rim, face and a needle.</summary>
    private static void DialZ(K.Kit k, double x, double y, double z, double r, double needle = 0.6)
    {
        k.Hull(Frustum(2, x, y, z, r, z + 0.03, r, 10), Chrome, "chrome");
        k.Hull(Frustum(2, x, y, z + 0.03, r * 0.82, z + 0.036, r * 0.82, 10), Ceramic, "plastic");
        var a = Math.PI * (1.25 - 1.5 * needle);
        double ex = x + Math.Cos(a) * r * 0.7, ey = y + Math.Sin(a) * r * 0.7;
        k.Hull([[x - 0.004, y, z + 0.036], [x + 0.004, y, z + 0.036], [ex, ey, z + 0.036], [x - 0.004, y, z + 0.048], [x + 0.004, y, z + 0.048], [ex, ey, z + 0.048]], Brown, "plastic");
    }

    /// <summary>A small warm pilot lamp in a chrome bezel facing +z.</summary>
    private static void PilotZ(K.Kit k, double x, double y, double z, string col, double r = 0.016)
    {
        k.Hull(Frustum(2, x, y, z, r * 1.5, z + 0.014, r * 1.5, 8), Chrome, "chrome");
        k.Hull(Frustum(2, x, y, z + 0.014, r, z + 0.026, r * 0.8, 8), col, "", glow: true);
    }

    /// <summary>A toggle switch facing +z: a plate and a lever thrown up or down.</summary>
    private static void ToggleZ(K.Kit k, double x, double y, double z, bool up)
    {
        k.Box(x - 0.016, y - 0.024, z, x + 0.016, y + 0.024, z + 0.011, Chrome, "steel");
        var t = up ? 0.035 : -0.035;
        k.Hull([[x - 0.005, y, z + 0.011], [x + 0.005, y, z + 0.011], [x - 0.005, y + 0.01 * Math.Sign(t), z + 0.011], [x + 0.005, y + 0.01 * Math.Sign(t), z + 0.011],
                [x - 0.004, y + t, z + 0.045], [x + 0.004, y + t, z + 0.045], [x - 0.004, y + t * 0.8, z + 0.05], [x + 0.004, y + t * 0.8, z + 0.05]], Chrome, "chrome");
    }

    /// <summary>The camper's stripes: three bands across a +z face at z, brown, orange and mustard, from y upward.</summary>
    private static void StripesZ(K.Kit k, double x0, double x1, double y, double z, double band = 0.035)
    {
        string[] cols = [Brown, Orange, Mustard];
        for (var i = 0; i < 3; i++)
            k.Box(x0, y + i * (band + 0.006), z, x1, y + i * (band + 0.006) + band, z + 0.011, cols[i], "worn paint");
    }

    private static void StripesX(K.Kit k, double x, double z0, double z1, double y, double sx, double band = 0.035)
    {
        string[] cols = [Brown, Orange, Mustard];
        for (var i = 0; i < 3; i++)
            k.Box(Math.Min(x, x + sx * 0.011), y + i * (band + 0.006), z0, Math.Max(x, x + sx * 0.011), y + i * (band + 0.006) + band, z1, cols[i], "worn paint");
    }

    /// <summary>A rust run under a seam: a thin streak down a +z face.</summary>
    private static void StreakZ(K.Kit k, double x, double y0, double y1, double z, double w = 0.03) =>
        k.Hull([[x - w / 2, y1, z], [x + w / 2, y1, z], [x - w * 0.15, y0, z], [x + w * 0.15, y0, z],
                [x - w / 2, y1, z + 0.011], [x + w / 2, y1, z + 0.011], [x - w * 0.15, y0, z + 0.011], [x + w * 0.15, y0, z + 0.011]], Rust, "rust");

    // ---- Modules on the Module Plug (1x1; the magazine 2x2)

    /// <summary>1. The reactor: an old fission pile in a cream drum, a crank for the rods, a porthole on the glow, a copper bypass loop.</summary>
    private static Part Reactor()
    {
        const double H = 1.35;
        var k = K.Reactor("", 1, "Commoner reactor (draft): an old fission pile in a cream drum with faded stripes and rust runs, a hand crank on top for the control rods, a porthole on the warm glow, a dial, and a copper bypass loop someone added later.", H, "Commoner Reactor", Section, ["Data 1", "Fuel A"], group: Group);
        k.Hull(Frustum(1, 0, 0, 0.08, 0.36, 0.2, 0.37, 12), Grime, "dirty steel"); // (the foot ring)
        k.Hull(Frustum(1, 0, 0, 0.2, 0.34, H - 0.16, 0.34, 12), Cream, "worn paint");
        for (var i = 0; i < 3; i++) // (stripes round the drum, slightly proud)
            k.Hull(Frustum(1, 0, 0, 0.72 + i * 0.045, 0.346 + i * 0.002, 0.755 + i * 0.045, 0.346 + i * 0.002, 12), i == 0 ? Brown : i == 1 ? Orange : Mustard, "worn paint");
        k.Hull(Frustum(1, 0, 0, H - 0.16, 0.36, H - 0.1, 0.33, 12), Tan, "rusty paint"); // (the lid's rim)
        k.Hull(Frustum(1, 0, 0, H - 0.1, 0.3, H - 0.06, 0.2, 12), Beige, "worn paint");
        // The rod crank: a spindle and a spoked wheel.
        Tube(k, 1, 0, 0, H - 0.07, H + 0.06, 0.025, Chrome, "steel");
        k.Hull(Frustum(1, 0, 0, H + 0.05, 0.15, H + 0.075, 0.15, 12), Grime, "steel");
        k.Hull(Frustum(1, 0, 0, H + 0.04, 0.12, H + 0.085, 0.12, 12), Rust, "rust");
        Tube(k, 1, 0.11, 0, H + 0.08, H + 0.17, 0.016, Brown, "plastic"); // (its handle)
        // Porthole on the glow (+z face).
        k.Hull(Frustum(2, 0, 0.6, 0.33, 0.09, 0.375, 0.09, 10), Chrome, "chrome");
        k.Hull(Frustum(2, 0, 0.6, 0.37, 0.065, 0.382, 0.065, 10), Bulb, "", glow: true);
        DialZ(k, 0.17, 0.98, 0.3, 0.05, 0.75);
        // A rust run from the lid and a riveted patch on the side.
        k.Box(-0.2, 0.32, 0.31, -0.07, 0.5, 0.343, Avocado, "rusty paint");
        k.Box(-0.16, 0.95, 0.318, -0.13, H - 0.17, 0.344, Rust, "rust");
        // The copper bypass: up the -x side from the fuel gland, over the lid.
        Tube(k, 1, -0.4, -0.3, 0.12, 1.05, 0.022, "#b06f3d", "copper");
        Tube(k, 0, 1.05, -0.3, -0.4, -0.2, 0.017, "#b06f3d", "copper");
        k.Box(-0.43, 0.6, -0.33, -0.37, 0.64, -0.27, Tape, "fabric"); // (taped where it leaked)
        return k.P;
    }

    /// <summary>2. The battery: mismatched old cells in a tube rack under a ratchet strap, jumper leads and a voltmeter.</summary>
    private static Part Battery()
    {
        var k = K.Reactor("", 1, "Commoner battery (draft): a rack of mismatched old cells, none the same make, held by a ratchet strap in a bent tube frame, jumper leads between the terminals and a voltmeter clamped to the frame.", 1.1, "Commoner Battery", Section, ["Data 1"], role: "frame", group: Group);
        // The tube rack: four posts and two shelves.
        foreach (var sx in new[] { -0.33, 0.33 })
            foreach (var sz in new[] { -0.3, 0.3 })
                Tube(k, 1, sx, sz, 0.08, 1.02, 0.018, Teal, "worn paint", 6);
        k.Box(-0.35, 0.1, -0.32, 0.35, 0.13, 0.32, Teal, "rusty paint");
        k.Box(-0.35, 0.56, -0.32, 0.35, 0.59, 0.32, Teal, "rusty paint");
        // Cells: three on each shelf, all different.
        (double W, double Hh, string Col)[] cells = [(0.2, 0.34, Grime), (0.16, 0.3, "#4a5a6a"), (0.22, 0.38, Cream), (0.18, 0.36, Rubber), (0.22, 0.32, Mustard), (0.18, 0.38, Grime)];
        for (var i = 0; i < 6; i++)
        {
            var y0 = i < 3 ? 0.13 : 0.59;
            var x0 = -0.31 + (i % 3) * 0.215;
            var (w, hh, col) = cells[i];
            k.Box(x0, y0, -0.24, x0 + w, y0 + hh, 0.24, col, i == 2 ? "worn paint" : "plastic");
            k.Box(x0 + 0.03, y0 + hh, -0.04, x0 + 0.06, y0 + hh + 0.03, 0.0, "#9a6a34", "brass"); // (terminals)
            k.Box(x0 + w - 0.06, y0 + hh, -0.04, x0 + w - 0.03, y0 + hh + 0.03, 0.0, PilotRed, "plastic");
            k.Box(x0 + 0.02, y0 + 0.1, 0.24, x0 + w - 0.02, y0 + 0.15, 0.252, i % 2 == 0 ? Ceramic : Mustard, "plastic"); // (a label)
        }
        // Jumper leads between the top cells.
        Tube(k, 0, 0.986, -0.02, -0.25, 0.05, 0.012, PilotRed, "rubber", 6);
        Tube(k, 0, 0.996, -0.02, 0.12, 0.3, 0.012, Rubber, "rubber", 6);
        // The ratchet strap round the top shelf.
        k.Box(-0.345, 0.78, 0.25, 0.345, 0.82, 0.262, Mustard, "fabric");
        k.Box(-0.06, 0.77, 0.262, 0.06, 0.83, 0.285, Chrome, "steel");
        // A voltmeter clamped to the front left post.
        k.Box(-0.4, 0.62, 0.28, -0.24, 0.76, 0.32, Grime, "plastic");
        DialZ(k, -0.32, 0.69, 0.32, 0.05, 0.35);
        return k.P;
    }

    /// <summary>3. The capacitor bank: old oil-filled cans on a channel frame, porcelain bushings, a hand-painted warning board.</summary>
    private static Part Capacitor()
    {
        var k = K.Reactor("", 1, "Commoner capacitor bank (draft): four old oil-filled cans like a power company's, porcelain bushings and a copper bus on top, an oil stain down one can and a hand-painted warning board.", 1.2, "Commoner Capacitor", Section, ["Data 1"], group: Group);
        k.Box(-0.38, 0.08, -0.38, 0.38, 0.16, 0.38, Olive, "rusty paint"); // (the channel frame)
        foreach (var (x, z) in new[] { (-0.19, -0.19), (0.19, -0.19), (-0.19, 0.19), (0.19, 0.19) })
        {
            k.Hull(Frustum(1, x, z, 0.16, 0.15, 0.92, 0.15, 10), "#7f8a78", "worn paint");
            k.Hull(Frustum(1, x, z, 0.92, 0.155, 0.96, 0.13, 10), Grime, "dirty steel");
            for (var s = 0; s < 4; s++) // (a porcelain bushing's sheds)
                k.Hull(Frustum(1, x, z, 0.96 + s * 0.045, 0.045, 0.99 + s * 0.045, 0.03, 8), Ceramic, "gloss paint");
            k.Hull(Frustum(1, x, z, 1.13, 0.014, 1.16, 0.014, 6), "#b06f3d", "copper");
        }
        k.Box(-0.21, 1.15, -0.2, 0.21, 1.18, -0.17, "#b06f3d", "copper"); // (the bus)
        k.Box(-0.21, 1.15, 0.17, 0.21, 1.18, 0.2, "#b06f3d", "copper");
        k.Box(-0.2, 1.155, -0.19, -0.17, 1.185, 0.19, "#b06f3d", "copper");
        StreakZ(k, 0.24, 0.3, 0.88, 0.335, 0.05); // (oil down the front right can)
        // The warning board, wired to the frame: white with a red bar, a bit crooked.
        k.Hull([[-0.3, 0.3, 0.385], [0.02, 0.33, 0.385], [0.02, 0.55, 0.385], [-0.3, 0.52, 0.385], [-0.3, 0.3, 0.4], [0.02, 0.33, 0.4], [0.02, 0.55, 0.4], [-0.3, 0.52, 0.4]], Ceramic, "painted wood");
        k.Hull([[-0.28, 0.46, 0.4], [0.0, 0.485, 0.4], [0.0, 0.525, 0.4], [-0.28, 0.5, 0.4], [-0.28, 0.46, 0.411], [0.0, 0.485, 0.411], [0.0, 0.525, 0.411], [-0.28, 0.5, 0.411]], PilotRed, "worn paint");
        return k.P;
    }

    /// <summary>4. The fuel tank: a lying propane-style tank on saddles, dented, a float gauge, a hand valve.</summary>
    private static Part FuelTank()
    {
        var k = K.Reactor("", 1, "Commoner fuel tank (draft): a lying tank like a camper's gas bottle, faded mustard and dented, on two welded saddles with a strap. A float gauge, a hand valve and a rubber hose to the plug's fuel gland.", 1.0, "Commoner Fuel Tank", Section, ["Fuel A"], group: Group);
        const double cy = 0.52, r = 0.3;
        k.Hull(Frustum(0, cy, 0, -0.3, r, 0.3, r, 12), Mustard, "worn paint");
        k.Hull(Frustum(0, cy, 0, -0.36, r * 0.55, -0.3, r * 0.92, 12), Mustard, "worn paint"); // (the domed ends)
        k.Hull(Frustum(0, cy, 0, 0.3, r * 0.92, 0.36, r * 0.55, 12), Mustard, "worn paint");
        k.Hull([[0.05, cy + 0.12, r - 0.03], [0.2, cy + 0.08, r - 0.03], [0.17, cy - 0.06, r - 0.03], [0.07, cy - 0.03, r - 0.03], [0.12, cy + 0.03, r + 0.002]], Rust, "rust"); // (a dent gone rusty)
        foreach (var x in new[] { -0.22, 0.22 }) // (saddles and straps)
        {
            k.Profile(x - 0.035, x + 0.035, [(0.08, -0.3), (0.08, 0.3), (0.32, 0.22), (0.32, -0.22)], Grime, "dirty steel");
            k.Hull(Frustum(0, cy, 0, x - 0.022, r + 0.012, x + 0.022, r + 0.012, 12), Rubber, "rubber");
        }
        // The float gauge and the valve on top.
        k.Hull(Frustum(1, 0.0, 0.0, cy + r - 0.02, 0.06, cy + r + 0.04, 0.06, 10), Chrome, "steel");
        k.Hull(Frustum(1, 0.0, 0.0, cy + r + 0.04, 0.045, cy + r + 0.046, 0.045, 10), Ceramic, "plastic");
        Tube(k, 1, 0.18, 0.08, cy + r - 0.03, cy + r + 0.08, 0.02, "#9a6a34", "brass");
        k.Hull(Frustum(1, 0.18, 0.08, cy + r + 0.08, 0.055, cy + r + 0.1, 0.055, 8), PilotRed, "worn paint");
        // The hose down to the fuel gland (0.35, -0.35).
        Tube(k, 0, cy + 0.12, -0.2, 0.33, 0.4, 0.018, Rubber, "rubber", 6);
        Tube(k, 1, 0.4, -0.2, 0.12, cy + 0.12, 0.018, Rubber, "rubber", 6);
        TapeZ(k, r + 0.001, -0.2, cy - 0.1, -0.05, cy + 0.06);
        return k.P;
    }

    /// <summary>5. Life support: a camper's rooftop cooler box with louvres, a fan grille on top, a filter taped to its side, a drip can.</summary>
    private static Part LifeSupport()
    {
        const double H = 1.1;
        var k = K.Reactor("", 1, "Commoner life support (draft): a scrubber in a box like an old camper's rooftop cooler, wrap-round louvres, a fan grille on top, a spare filter taped to its side and a can under the drip spout.", H, "Commoner Life Support", Section, ["Data 1"], group: Group);
        k.Profile(-0.36, 0.36, [(0.08, -0.36), (0.08, 0.36), (H - 0.1, 0.36), (H, 0.28), (H, -0.28), (H - 0.1, -0.36)], Cream, "worn paint");
        for (var i = 0; i < 7; i++) // (louvres on the front and both sides)
        {
            var y = 0.32 + i * 0.07;
            k.Box(-0.3, y, 0.36, 0.3, y + 0.03, 0.39, Beige, "worn paint");
            foreach (var sx in new[] { -1.0, 1.0 })
                k.Box(Math.Min(sx * 0.36, sx * 0.39), y, -0.3, Math.Max(sx * 0.36, sx * 0.39), y + 0.03, 0.3, Beige, "worn paint");
        }
        StripesZ(k, -0.36, 0.36, 0.14, 0.36);
        foreach (var sx in new[] { -1.0, 1.0 })
            StripesX(k, sx * 0.36, -0.36, 0.36, 0.14, sx);
        // The fan grille on top: a ring and bars.
        k.Hull(Frustum(1, 0, 0, H - 0.004, 0.24, H + 0.03, 0.24, 12), Grime, "dirty steel");
        for (var i = 0; i < 5; i++)
            k.Box(-0.22 + i * 0.11 - 0.006, H + 0.03, -0.22, -0.22 + i * 0.11 + 0.006, H + 0.045, 0.22, Chrome, "steel");
        // A spare filter cartridge taped to the +x side.
        k.Box(0.395, 0.6, -0.2, 0.47, 0.9, 0.12, "#c9c0a0", "fabric");
        k.Box(0.39, 0.7, -0.22, 0.48, 0.75, 0.14, Tape, "fabric");
        // The drip spout and its can.
        Tube(k, 2, -0.22, 0.24, 0.36, 0.44, 0.015, Chrome, "steel", 6);
        k.Hull(Frustum(1, -0.22, 0.42, 0.08, 0.06, 0.2, 0.06, 8), "#8f8a7a", "rusty paint");
        PilotZ(k, 0.26, H - 0.2, 0.36, PilotGreen);
        return k.P;
    }

    /// <summary>6. The sensor mast: a short lattice mast with an old dish and a fishbone aerial, its cable taped down.</summary>
    private static Part SensorMast()
    {
        var k = K.Reactor("", 1, "Commoner sensor mast (draft): a short lattice mast on the plug with an old dish, its paint flaking, and a fishbone aerial clamped on beside it. A junction box at the foot and the cable taped down the leg.", 2.1, "Commoner Sensor Mast", Section, ["Data 1"], role: "frame", group: Group);
        const double c = 0.16, top = 1.7;
        foreach (var (x, z) in new[] { (-c, -c), (c, -c), (-c, c), (c, c) })
            Tube(k, 1, x, z, 0.08, top, 0.014, Cream, "worn paint", 6);
        for (var i = 0; i < 5; i++) // (cross bracing: rungs on each face)
        {
            var y = 0.3 + i * 0.3;
            k.Box(-c, y, c - 0.01, c, y + 0.02, c + 0.01, Beige, "worn paint");
            k.Box(-c, y + 0.15, -c - 0.01, c, y + 0.17, -c + 0.01, Beige, "worn paint");
            k.Box(-c - 0.01, y + 0.07, -c, -c + 0.01, y + 0.09, c, Beige, "worn paint");
            k.Box(c - 0.01, y + 0.22, -c, c + 0.01, y + 0.24, c, Beige, "worn paint");
        }
        k.Box(-c - 0.02, top, -c - 0.02, c + 0.02, top + 0.05, c + 0.02, Grime, "dirty steel");
        // The dish facing +z, tilted up a little, on a yoke.
        Tube(k, 1, 0, 0, top + 0.05, top + 0.2, 0.03, Chrome, "steel");
        k.Hull(Frustum(2, 0, top + 0.28, 0.02, 0.06, 0.1, 0.3, 12), Ceramic, "worn paint");
        k.Hull(Frustum(2, 0, top + 0.28, 0.1, 0.3, 0.115, 0.3, 12), Tan, "rusty paint"); // (its rim)
        Tube(k, 2, 0, top + 0.28, 0.1, 0.32, 0.01, Chrome, "steel", 6); // (the feed arm)
        k.Box(-0.03, top + 0.25, 0.31, 0.03, top + 0.31, 0.36, Grime, "plastic");
        // The fishbone aerial on the +x leg.
        Tube(k, 1, 0.3, 0, 0.9, 1.9, 0.012, Chrome, "steel", 6);
        k.Box(c, 1.2, -0.012, 0.3, 1.23, 0.012, Grime, "steel");
        for (var i = 0; i < 6; i++)
        {
            var w = 0.18 - i * 0.022;
            k.Box(0.3 - 0.006, 1.3 + i * 0.1, -w, 0.3 + 0.006, 1.312 + i * 0.1, w, Chrome, "steel");
        }
        // The junction box and taped cable.
        k.Box(c + 0.01, 0.12, c - 0.08, c + 0.12, 0.3, c + 0.06, Grime, "plastic");
        Tube(k, 1, c + 0.03, c + 0.03, 0.3, 1.5, 0.01, Rubber, "rubber", 6);
        for (var i = 0; i < 4; i++)
            k.Box(c - 0.005, 0.45 + i * 0.3, c - 0.005, c + 0.05, 0.49 + i * 0.3, c + 0.05, Tape, "fabric");
        return k.P;
    }

    /// <summary>7. The magazine (2x2): ammo cans stacked in a welded cage, a hand crank on the feed and a chute down to the ammo gland.</summary>
    private static Part Magazine()
    {
        const double H = 1.3;
        var k = K.Reactor("", 2, "Commoner magazine (draft, 2x2): surplus ammo cans of three colours stacked in a welded angle-iron cage, a hand crank on the feed head and a sheet chute down to the plug's ammo port. One can is a jerry can.", H, "Commoner Magazine 2x2", Section, ["Data 1", "Ammo"], group: Group);
        const double c = 0.82;
        foreach (var x in new[] { -c, c }) // (the cage)
            foreach (var z in new[] { -c, c })
                k.Box(x - 0.03, 0.08, z - 0.03, x + 0.03, H, z + 0.03, Olive, "rusty paint");
        foreach (var y in new[] { 0.5, H - 0.04 })
        {
            k.Box(-c, y, -c - 0.025, c, y + 0.04, -c + 0.025, Olive, "rusty paint");
            k.Box(-c, y + 0.003, c - 0.025, c, y + 0.043, c + 0.025, Olive, "rusty paint");
            k.Box(-c - 0.025, y + 0.006, -c, -c + 0.025, y + 0.046, c, Olive, "rusty paint");
            k.Box(c - 0.025, y + 0.009, -c, c + 0.025, y + 0.049, c, Olive, "rusty paint");
        }
        // The cans: two rows of three, two high, of three colours.
        string[] cols = [Avocado, Olive, Tan, Avocado, "#6b5a3a", Olive];
        for (var row = 0; row < 2; row++)
            for (var i = 0; i < 3; i++)
                for (var lv = 0; lv < 2; lv++)
                {
                    var x0 = -0.74 + i * 0.5;
                    var z0 = -0.7 + row * 0.75;
                    var y0 = 0.09 + lv * 0.42;
                    var col = cols[(row * 3 + i + lv) % 6];
                    if (row == 1 && i == 2 && lv == 1) // (the jerry can)
                    {
                        k.Box(x0, y0, z0, x0 + 0.44, y0 + 0.38, z0 + 0.18, PilotRed, "rusty paint");
                        k.Box(x0 + 0.1, y0 + 0.38, z0 + 0.06, x0 + 0.3, y0 + 0.42, z0 + 0.12, PilotRed, "rusty paint");
                        continue;
                    }
                    k.Box(x0, y0, z0, x0 + 0.44, y0 + 0.36, z0 + 0.6, col, "worn paint");
                    k.Box(x0 + 0.16, y0 + 0.36, z0 + 0.2, x0 + 0.28, y0 + 0.39, z0 + 0.4, Grime, "steel"); // (the handle)
                    k.Box(x0 + 0.05, y0 + 0.12, z0 + 0.6, x0 + 0.3, y0 + 0.2, z0 + 0.612, Mustard, "worn paint"); // (stencil band)
                }
        // The feed head over the middle front and its crank.
        k.Box(-0.2, H, 0.2, 0.2, H + 0.18, 0.7, Grime, "dirty steel");
        Tube(k, 0, H + 0.09, 0.45, 0.2, 0.3, 0.025, Chrome, "steel");
        k.Box(0.3, H + 0.06, 0.43, 0.33, H + 0.26, 0.47, Chrome, "steel");
        Tube(k, 0, H + 0.24, 0.45, 0.33, 0.42, 0.018, Brown, "plastic", 6);
        // The chute down the front to the ammo port (0, 0.7).
        k.Box(-0.12, 0.12, 0.84, 0.12, H, 0.9, Beige, "rusty paint");
        k.Box(-0.12, 0.12, 0.62, 0.12, 0.2, 0.84, Beige, "rusty paint");
        TapeZ(k, 0.9, -0.1, 0.6, 0.1, 0.8);
        return k.P;
    }

    // ---- Free-standing equipment

    /// <summary>8. Oxygen bottles: three tall green bottles chained to a wall bracket, a regulator with a gauge.</summary>
    private static Part OxygenBottles()
    {
        var k = Free("Commoner Oxygen Bottles", "Commoner oxygen (draft): three tall bottles of different ages in faded green, chained to a wall bracket, their shoulders chipped, one regulator with a gauge shared between them by a tangle of hose.", [-0.36, 0, -0.25], [0.36, 1.6, 0.13]);
        k.Box(-0.36, 0.0, -0.25, 0.36, 0.06, 0.13, Grime, "dirty steel"); // (the floor tray)
        k.Box(-0.36, 0.06, -0.25, 0.36, 1.45, -0.21, Beige, "worn paint"); // (the back board)
        double[] hs = [1.35, 1.28, 1.38];
        for (var i = 0; i < 3; i++)
        {
            var x = -0.23 + i * 0.23;
            k.Hull(Frustum(1, x, -0.07, 0.06, 0.1, hs[i] - 0.14, 0.1, 10), i == 1 ? "#4d6b47" : BottleGreen, "worn paint");
            k.Hull(Frustum(1, x, -0.07, hs[i] - 0.14, 0.1, hs[i] - 0.04, 0.05, 10), Ceramic, "worn paint"); // (the shoulder, white)
            Tube(k, 1, x, -0.07, hs[i] - 0.04, hs[i] + 0.04, 0.022, "#9a6a34", "brass", 6);
        }
        foreach (var y in new[] { 0.45, 1.0 }) // (the chains: a bar each)
            k.Box(-0.35, y, 0.032, 0.35, y + 0.025, 0.05, Chrome, "steel");
        // The regulator on the middle bottle, its gauge and the hoses.
        k.Box(-0.04, hs[1] + 0.04, -0.11, 0.04, hs[1] + 0.12, -0.03, "#9a6a34", "brass");
        DialZ(k, 0.0, hs[1] + 0.2, -0.05, 0.045, 0.3);
        Tube(k, 0, hs[1] + 0.06, -0.07, -0.23, -0.04, 0.012, Rubber, "rubber", 6);
        Tube(k, 0, hs[1] + 0.09, -0.07, 0.04, 0.23, 0.012, Rubber, "rubber", 6);
        k.Box(0.18, 0.75, 0.025, 0.3, 0.83, 0.033, Ceramic, "plastic"); // (a paper tag)
        return k.P;
    }

    /// <summary>9. The radiator: cast sections like an old house radiator on the wall, cream paint chipped to rust, a bleed valve.</summary>
    private static Part Radiator()
    {
        var k = Free("Commoner Radiator", "Commoner radiator (draft): cast-iron sections like a house radiator from long ago, bolted to the wall, cream paint chipped to rust at the edges, a bleed valve, a hand-written tag and a sock drying on it.", [-0.45, 0, -0.25], [0.45, 0.9, -0.05]);
        k.Box(-0.42, 0.06, -0.2, 0.42, 0.11, -0.1, Grime, "rusty paint"); // (bottom header)
        k.Box(-0.42, 0.78, -0.2, 0.42, 0.83, -0.1, Grime, "rusty paint"); // (top header)
        for (var i = 0; i < 10; i++)
        {
            var x = -0.4 + i * 0.0889;
            k.Profile(x, x + 0.066, [(0.04, -0.21), (0.04, -0.09), (0.12, -0.07), (0.77, -0.07), (0.85, -0.09), (0.85, -0.21)], i == 6 ? Rust : Cream, i == 6 ? "rust" : "worn paint");
        }
        foreach (var sx in new[] { -1.0, 1.0 }) // (feet and wall brackets)
        {
            k.Box(sx * 0.36 - 0.03, 0, -0.18, sx * 0.36 + 0.03, 0.05, -0.1, Grime, "steel");
            k.Box(sx * 0.3 - 0.02, 0.7, -0.25, sx * 0.3 + 0.02, 0.74, -0.2, Grime, "steel");
        }
        Tube(k, 0, 0.8, -0.14, 0.42, 0.48, 0.02, "#9a6a34", "brass", 6); // (bleed valve)
        Tube(k, 1, -0.47, -0.14, 0.0, 0.12, 0.022, "#b06f3d", "copper", 6); // (the feed pipe from the floor)
        Tube(k, 0, 0.1, -0.14, -0.47, -0.42, 0.022, "#b06f3d", "copper", 6);
        // A sock over the top and a tag.
        k.Box(-0.08, 0.6, -0.07, 0.02, 0.86, -0.04, Orange, "fabric");
        k.Box(-0.08, 0.83, -0.205, 0.02, 0.87, -0.06, Orange, "fabric");
        k.Box(0.2, 0.5, -0.066, 0.3, 0.6, -0.055, Ceramic, "plastic");
        return k.P;
    }

    /// <summary>10. The RCS block: four little nozzles on a dented, sooty box, a hand-labelled number plate.</summary>
    private static Part RcsBlock()
    {
        var k = Free("Commoner RCS Block", "Commoner RCS block (draft): four small nozzles on a dented box bolted to the hull, soot round each throat, faded orange paint, one nozzle a different make, and a number painted on by hand.", [-0.25, 0, -0.25], [0.25, 0.4, 0.15]);
        k.Profile(-0.22, 0.22, [(0.004, -0.246), (0.004, 0.12), (0.28, 0.12), (0.36, 0.04), (0.36, -0.246)], Orange, "worn paint");
        k.Box(-0.23, 0.0, -0.25, 0.23, 0.04, 0.13, Grime, "dirty steel");
        k.Hull([[0.05, 0.12, 0.121], [0.17, 0.1, 0.121], [0.15, 0.24, 0.121], [0.06, 0.22, 0.121], [0.1, 0.17, 0.1]], Rust, "rust"); // (a dent)
        // Nozzles: up (+y), front (+z) and both sides.
        k.Hull(Frustum(1, -0.1, -0.08, 0.36, 0.045, 0.46, 0.07, 8), Grime, "soot");
        k.Hull(Frustum(2, -0.08, 0.2, 0.12, 0.04, 0.22, 0.065, 8), Grime, "soot");
        k.Hull(Frustum(0, 0.18, -0.08, 0.22, 0.04, 0.31, 0.065, 8), "#6f6a60", "steel"); // (the odd one)
        k.Hull(Frustum(0, 0.18, -0.08, -0.22, 0.04, -0.31, 0.065, 8), Grime, "soot");
        // The hand-painted number.
        k.Box(0.02, 0.18, 0.12, 0.17, 0.3, 0.131, Cream, "worn paint");
        k.Box(0.07, 0.2, 0.131, 0.12, 0.28, 0.142, Brown, "worn paint");
        RivetsX(k, -0.2, 0.2, 0.06, 0.12, 6);
        return k.P;
    }

    /// <summary>11. The drive nozzle: an old engine bell with heat-blued bands, a welded patch and soot inside.</summary>
    private static Part DriveNozzle()
    {
        var k = Free("Commoner Drive Nozzle", "Commoner drive nozzle (draft): an old engine bell, faceted and heat-blued in bands, a welded patch on one side, soot inside, on a mount ring with mismatched bolts. Points aft (-z).", [-0.5, 0, -1.0], [0.5, 1.0, 0.3], wall: false);
        const double cy = 0.5;
        k.Hull(Frustum(2, 0, cy, 0.3, 0.36, 0.2, 0.38, 12), Grime, "dirty steel"); // (the mount ring)
        k.Hull(Frustum(2, 0, cy, 0.2, 0.22, 0.0, 0.2, 12), Tan, "rusty paint"); // (the throat)
        string[] bands = ["#5a6070", "#7d6a52", "#4e5668", "#6d5d4a"];
        for (var i = 0; i < 4; i++)
        {
            var z0 = -i * 0.24;
            k.Hull(Frustum(2, 0, cy, z0, 0.2 + i * 0.07, z0 - 0.24, 0.27 + i * 0.07, 12), bands[i], "dirty steel");
        }
        k.Hull(Frustum(2, 0, cy, -0.9, 0.42, -0.97, 0.47, 12), Rust, "rust"); // (the lip)
        k.Hull(Frustum(2, 0, cy, -0.96, 0.4, -0.975, 0.4, 12), Rubber, "soot"); // (soot inside, seen down the bell)
        // A welded patch and its bead.
        k.Hull([[0.3, cy + 0.1, -0.45], [0.36, cy + 0.06, -0.62], [0.36, cy - 0.12, -0.62], [0.3, cy - 0.14, -0.45], [0.34, cy + 0.1, -0.45], [0.4, cy + 0.06, -0.62], [0.4, cy - 0.12, -0.62], [0.34, cy - 0.14, -0.45]], "#8a8a80", "steel");
        // Mismatched bolts round the ring.
        for (var i = 0; i < 8; i++)
        {
            var a = i * Math.PI / 4;
            var col = i == 3 ? "#9a6a34" : Chrome;
            k.Box(0.33 * Math.Cos(a) - 0.015, cy + 0.33 * Math.Sin(a) - 0.015, 0.3, 0.33 * Math.Cos(a) + 0.015, cy + 0.33 * Math.Sin(a) + 0.015, 0.32, col, "steel");
        }
        Tube(k, 1, 0.26, 0.1, 0.0, cy, 0.03, Grime, "steel"); // (a support leg to the deck)
        Tube(k, 1, -0.26, 0.1, 0.0, cy, 0.03, Grime, "steel");
        return k.P;
    }

    /// <summary>12. The pilot seat: a captain's chair from an old camper, brown vinyl with a taped split, swivel base, lap belt.</summary>
    private static Part PilotSeat()
    {
        var k = Free("Commoner Pilot Seat", "Commoner pilot seat (draft): a camper's captain's chair in tired brown vinyl with piped seams, a split taped over, padded fold-up arms, a swivel base and a lap belt with a chrome buckle. A cushion someone brought from home.", [-0.32, 0, -0.35], [0.32, 1.25, 0.35], wall: false);
        Tube(k, 1, 0, 0, 0.0, 0.05, 0.28, Grime, "dirty steel", 10); // (the swivel foot)
        Tube(k, 1, 0, 0, 0.05, 0.38, 0.05, Chrome, "chrome");
        k.Box(-0.26, 0.38, -0.26, 0.26, 0.42, 0.28, Grime, "steel");
        // Seat cushion and back, rounded at the front.
        k.Profile(-0.27, 0.27, [(0.42, -0.26), (0.42, 0.3), (0.5, 0.33), (0.56, 0.3), (0.58, -0.22)], Vinyl, "rubber");
        k.Profile(-0.28, 0.28, [(0.5, -0.34), (0.5, -0.2), (1.12, -0.24), (1.2, -0.28), (1.2, -0.36), (1.1, -0.38)], Vinyl, "rubber");
        // Piping: lighter bands down the back.
        foreach (var x in new[] { -0.1, 0.1 })
            k.Box(x - 0.008, 0.6, -0.211, x + 0.008, 1.1, -0.2, Tan, "rubber");
        TapeZ(k, -0.205, 0.12, 0.75, 0.2, 0.95); // (a split, taped)
        // Fold-up arms.
        foreach (var sx in new[] { -1.0, 1.0 })
        {
            k.Box(sx * 0.27, 0.68, -0.22, sx * 0.33, 0.74, 0.22, Vinyl, "rubber");
            k.Box(sx * 0.28, 0.5, -0.24, sx * 0.31, 0.68, -0.2, Chrome, "chrome");
        }
        // The cushion from home: a mustard pillow on the seat.
        k.Box(-0.18, 0.56, -0.18, 0.18, 0.62, 0.12, Mustard, "fabric");
        // The lap belt and buckle.
        k.Box(-0.27, 0.6, 0.0, 0.27, 0.63, 0.04, Grime, "fabric");
        k.Box(-0.04, 0.595, 0.035, 0.04, 0.64, 0.05, Chrome, "chrome");
        return k.P;
    }

    /// <summary>13. The bunk: a fold-down bunk with a plaid blanket, a curtain on a rail, a reading lamp and a shelf with a mug.</summary>
    private static Part Bunk()
    {
        var k = Free("Commoner Bunk", "Commoner bunk (draft): a fold-down bunk on chains with a thin mattress and a plaid blanket, a curtain on a rail pulled half across, a little warm reading lamp, a wood-grain shelf with a mug and a photo stuck to the wall.", [-1.0, 0, -0.42], [1.0, 1.4, 0.42]);
        k.Box(-1.0, 0.0, -0.42, 1.0, 1.4, -0.38, Wood, "wood"); // (wood-grain wall panel)
        k.Box(-0.98, 0.42, -0.38, 0.98, 0.48, 0.38, Grime, "steel"); // (the frame)
        k.Box(-0.96, 0.48, -0.36, 0.96, 0.58, 0.36, Ceramic, "fabric"); // (the mattress)
        for (var i = 0; i < 5; i++) // (the blanket's plaid: bands of orange and brown)
            k.Box(-0.3 + i * 0.25, 0.581, -0.35, -0.2 + i * 0.25, 0.6, 0.37, i % 2 == 0 ? Orange : Brown, "fabric");
        k.Box(-0.92, 0.58, -0.3, -0.62, 0.66, 0.2, Cream, "fabric"); // (the pillow)
        foreach (var sx in new[] { -1.0, 1.0 }) // (chains to the wall)
            k.Hull([[sx * 0.95, 0.48, 0.34], [sx * 0.93, 0.48, 0.34], [sx * 0.95, 1.2, -0.37], [sx * 0.93, 1.2, -0.37], [sx * 0.95, 0.5, 0.36], [sx * 0.93, 0.5, 0.36]], Chrome, "steel");
        k.Box(-0.99, 0.0, 0.3, -0.94, 0.42, 0.36, Grime, "steel"); // (a fold leg)
        k.Box(0.94, 0.0, 0.3, 0.99, 0.42, 0.36, Grime, "steel");
        // The curtain rail and the curtain, half drawn, in mustard.
        k.Box(-1.0, 1.36, 0.36, 1.0, 1.38, 0.4, Chrome, "chrome");
        k.Box(0.2, 0.62, 0.37, 0.98, 1.35, 0.39, Mustard, "fabric");
        for (var i = 0; i < 4; i++)
            k.Box(0.25 + i * 0.2, 0.62, 0.385, 0.33 + i * 0.2, 1.35, 0.4, "#a87e26", "fabric"); // (its folds)
        // The shelf, a mug, a photo and a reading lamp.
        k.Box(-0.9, 1.05, -0.38, -0.3, 1.08, -0.2, WoodDark, "wood");
        k.Hull(Frustum(1, -0.75, -0.29, 1.08, 0.035, 1.17, 0.035, 8), Orange, "gloss paint");
        k.Box(-0.15, 0.85, -0.38, 0.0, 0.97, -0.369, Ceramic, "plastic");
        k.Box(0.3, 1.0, -0.38, 0.4, 1.06, -0.33, Chrome, "chrome");
        k.Hull(Frustum(1, 0.35, -0.27, 0.94, 0.07, 1.0, 0.035, 8), Mustard, "gloss paint");
        k.Hull(Frustum(1, 0.35, -0.27, 0.93, 0.03, 0.945, 0.03, 8), Bulb, "", glow: true);
        k.Box(0.33, 1.0, -0.33, 0.37, 1.02, -0.28, Chrome, "chrome");
        return k.P;
    }

    /// <summary>14. The galley: a kitchenette cabinet in wood-grain laminate, a two-ring stove with a kettle, a little sink.</summary>
    private static Part Galley()
    {
        var k = Free("Commoner Galley", "Commoner galley (draft): a camper kitchenette, wood-grain laminate doors with chrome pulls, a cream counter with a chipped edge, a two-ring stove with a whistling kettle on it, a little steel sink and a tea towel on the rail.", [-0.6, 0, -0.3], [0.6, 1.4, 0.3]);
        k.Box(-0.6, 0.0, -0.3, 0.6, 0.08, 0.26, Grime, "dirty steel"); // (kick)
        k.Box(-0.6, 0.08, -0.3, 0.6, 0.86, 0.27, Wood, "wood");
        foreach (var x0 in new[] { -0.57, 0.01 }) // (two doors)
        {
            k.Box(x0, 0.12, 0.27, x0 + 0.56, 0.82, 0.282, WoodDark, "wood");
            k.Box(x0 + 0.04, 0.16, 0.282, x0 + 0.52, 0.78, 0.293, Wood, "wood");
            var hx = x0 < 0 ? x0 + 0.5 : x0 + 0.06;
            k.Box(hx - 0.01, 0.62, 0.293, hx + 0.01, 0.74, 0.31, Chrome, "chrome");
        }
        k.Box(-0.62, 0.86, -0.3, 0.62, 0.9, 0.31, Cream, "plastic"); // (the counter)
        k.Box(-0.62, 0.86, 0.306, 0.62, 0.885, 0.32, Chrome, "chrome"); // (its edge strip)
        k.Box(0.3, 0.87, 0.28, 0.4, 0.9, 0.321, Brown, "plastic"); // (where it chipped)
        k.Box(-0.6, 0.9, -0.3, 0.6, 1.4, -0.27, "#c8b88f", "wall panelling"); // (the splashback)
        // The stove on the left: two rings and knobs.
        k.Box(-0.56, 0.9, -0.24, -0.06, 0.93, 0.22, Grime, "steel");
        foreach (var x in new[] { -0.44, -0.18 })
        {
            k.Hull(Frustum(1, x, -0.02, 0.93, 0.08, 0.945, 0.08, 10), Rubber, "soot");
            k.Box(x - 0.01, 0.94, -0.1, x + 0.01, 0.96, 0.06, Grime, "steel");
            k.Hull(Frustum(2, x, 0.88, 0.32, 0.022, 0.35, 0.02, 8), Ceramic, "plastic"); // (the knob, on the counter's lip)
        }
        // The kettle on the back ring.
        k.Hull(Frustum(1, -0.44, -0.02, 0.96, 0.1, 1.1, 0.07, 10), Orange, "gloss paint");
        k.Hull(Frustum(1, -0.44, -0.02, 1.1, 0.04, 1.13, 0.03, 8), Grime, "plastic");
        k.Hull([[-0.355, 1.02, -0.03], [-0.355, 1.06, -0.03], [-0.28, 1.1, -0.03], [-0.355, 1.02, -0.01], [-0.355, 1.06, -0.01], [-0.28, 1.1, -0.01]], Orange, "gloss paint"); // (spout)
        k.Box(-0.5, 1.13, -0.03, -0.38, 1.16, -0.01, Grime, "plastic");
        // The sink on the right and its tap.
        k.Box(0.08, 0.86, -0.22, 0.52, 0.905, 0.2, Chrome, "steel");
        k.Box(0.12, 0.84, -0.18, 0.48, 0.91, 0.16, "#6f716d", "steel");
        Tube(k, 1, 0.3, -0.25, 0.9, 1.12, 0.014, Chrome, "chrome", 6);
        Tube(k, 2, 0.3, 1.12, -0.25, -0.12, 0.012, Chrome, "chrome", 6);
        // The tea towel on the door rail.
        k.Box(-0.15, 0.55, 0.31, 0.15, 0.84, 0.322, Ceramic, "fabric");
        k.Box(-0.15, 0.6, 0.322, 0.15, 0.63, 0.33, PilotRed, "fabric");
        return k.P;
    }

    /// <summary>15. The head: a wet-bath toilet with a fold-down seat, a tiny basin and a mirror cabinet with a crack taped over.</summary>
    private static Part Head()
    {
        var k = Free("Commoner Head", "Commoner head (draft): a camper's wet-bath corner in cream plastic gone yellow: a toilet with a wood-effect seat folded up, a tiny basin with a push tap, a mirror cabinet with its crack taped over and a roll of paper on a bent wire holder.", [-0.5, 0, -0.35], [0.5, 1.7, 0.35]);
        k.Box(-0.5, 0.0, -0.35, 0.5, 0.03, 0.35, "#a0a48f", "rubber matting"); // (the shower tray floor)
        k.Box(-0.5, 0.03, -0.35, 0.5, 1.7, -0.32, "#d9cfaa", "plastic"); // (the wall moulding)
        // The toilet: pedestal, bowl, cistern, seat up.
        k.Hull(Frustum(1, -0.15, 0.0, 0.03, 0.14, 0.32, 0.17, 10), Ceramic, "gloss paint");
        k.Hull(Frustum(1, -0.15, 0.0, 0.32, 0.19, 0.4, 0.2, 10), Ceramic, "gloss paint");
        k.Box(-0.33, 0.4, -0.32, 0.03, 0.75, -0.14, Ceramic, "gloss paint"); // (the cistern)
        k.Box(-0.2, 0.75, -0.24, -0.1, 0.77, -0.2, Chrome, "chrome");
        k.Profile(-0.33, 0.03, [(0.4, -0.14), (0.42, -0.14), (0.78, -0.1), (0.78, -0.08), (0.42, -0.12)], Wood, "wood"); // (the seat, folded up)
        // The basin on the right, the tap.
        k.Box(0.12, 0.78, -0.32, 0.48, 0.86, -0.02, Ceramic, "gloss paint");
        k.Box(0.16, 0.81, -0.28, 0.44, 0.861, -0.06, "#9aa39b", "steel");
        Tube(k, 1, 0.3, -0.28, 0.86, 0.95, 0.012, Chrome, "chrome", 6);
        k.Box(0.25, 0.6, -0.31, 0.35, 0.78, -0.12, "#b7ac86", "plastic"); // (its bracket)
        // The mirror cabinet with a crack taped.
        k.Box(0.08, 1.05, -0.32, 0.5, 1.55, -0.22, "#d9cfaa", "plastic");
        k.Box(0.1, 1.08, -0.22, 0.48, 1.52, -0.21, "#a9b4b0", "chrome");
        TapeZ(k, -0.21, 0.15, 1.15, 0.38, 1.45, 0.035);
        // The paper roll.
        Tube(k, 0, 0.5, -0.24, -0.46, -0.34, 0.006, Chrome, "steel", 6);
        k.Hull(Frustum(0, 0.5, -0.24, -0.45, 0.055, -0.35, 0.055, 10), Ceramic, "fabric");
        return k.P;
    }

    /// <summary>16. The locker: a tall steel locker with one door repainted another colour, stickers, a dent, a padlock hasp.</summary>
    private static Part Locker()
    {
        var k = Free("Commoner Locker", "Commoner locker (draft): a tall steel locker, the door replaced from another one so it is a different colour, stickers half peeled, a boot-high dent, slotted vents, a padlock on the hasp and a name strip written on tape.", [-0.3, 0, -0.25], [0.3, 1.9, 0.2]);
        k.Box(-0.3, 0.0, -0.25, 0.3, 1.88, 0.2, Beige, "rusty paint"); // (the carcass)
        k.Box(-0.29, 1.88, -0.24, 0.29, 1.9, 0.19, Tan, "rusty paint");
        k.Paint(-0.28, 0.08, 0.2, 0.28, 1.84, 0.214, Avocado, "worn paint"); // (the odd door)
        for (var i = 0; i < 5; i++) // (vents top and bottom)
        {
            k.Box(-0.16, 1.6 + i * 0.04, 0.214, 0.16, 1.618 + i * 0.04, 0.224, Grime, "dirty steel");
            k.Box(-0.16, 0.2 + i * 0.04, 0.214, 0.16, 0.218 + i * 0.04, 0.224, Grime, "dirty steel");
        }
        k.Hull([[-0.2, 0.35, 0.218], [-0.05, 0.32, 0.218], [-0.06, 0.48, 0.218], [-0.18, 0.5, 0.218], [-0.12, 0.42, 0.204]], "#58603a", "rusty paint"); // (the dent)
        // Hasp and padlock.
        k.Box(0.18, 0.95, 0.214, 0.26, 1.02, 0.226, Chrome, "steel");
        k.Box(0.2, 0.86, 0.226, 0.25, 0.94, 0.246, "#9a6a34", "brass");
        k.Box(0.21, 0.94, 0.232, 0.24, 0.98, 0.24, Chrome, "chrome");
        // Stickers and a name strip on tape.
        k.Box(-0.22, 1.25, 0.214, -0.08, 1.37, 0.222, Orange, "plastic");
        k.Hull([[-0.02, 1.15, 0.214], [0.12, 1.17, 0.214], [0.1, 1.29, 0.214], [-0.04, 1.27, 0.214], [-0.02, 1.15, 0.222], [0.12, 1.17, 0.222], [0.1, 1.29, 0.222], [-0.04, 1.27, 0.222]], Mustard, "plastic");
        k.Box(-0.18, 1.48, 0.214, 0.18, 1.52, 0.225, Tape, "fabric");
        StreakZ(k, -0.25, 0.05, 0.4, 0.2);
        return k.P;
    }

    /// <summary>17. The console: a flight desk with a deep old screen, rows of toggles and dials, a mug and a note taped on.</summary>
    private static Part Console()
    {
        var k = Free("Commoner Console", "Commoner flight console (draft): a sloped desk in tan with wood-grain sides, a deep old tube screen glowing dim green, rows of chunky toggles, three dials, warm pilot lamps, a mug in a cup ring and a note taped to the screen's hood.", [-0.6, 0, -0.4], [0.6, 1.35, 0.35]);
        k.Profile(-0.6, 0.6, [(0.0, -0.4), (0.0, 0.3), (0.72, 0.35), (0.8, 0.28), (0.95, -0.15), (0.95, -0.4)], Tan, "worn paint");
        foreach (var sx in new[] { -1.0, 1.0 }) // (wood-grain end panels)
            k.Profile(sx > 0 ? 0.6 : -0.612, sx > 0 ? 0.612 : -0.6, [(0.02, -0.38), (0.02, 0.28), (0.7, 0.33), (0.78, 0.26), (0.93, -0.15), (0.93, -0.38)], Wood, "wood");
        k.Box(-0.58, 0.003, 0.0, 0.58, 0.08, 0.3, Grime, "dirty steel"); // (kick)
        StripesZ(k, -0.596, 0.596, 0.2, 0.3);
        // The sloped deck's switches, dials and lamps sit on a plate laid on the slope (front edge 0.8 high at z 0.28).
        double Sy(double z) => 0.8 + (0.28 - z) / 0.43 * 0.15; // (the slope from (0.8, 0.28) to (0.95, -0.15))
        for (var i = 0; i < 8; i++)
        {
            var x = -0.5 + i * 0.075;
            var z = 0.15;
            k.Box(x - 0.016, Sy(z) - 0.006, z - 0.03, x + 0.016, Sy(z) + 0.012, z + 0.03, Chrome, "steel");
            k.Box(x - 0.005, Sy(z) + 0.012, z - (i % 3 == 0 ? -0.01 : 0.02), x + 0.005, Sy(z) + 0.05, z - (i % 3 == 0 ? -0.02 : 0.01), Chrome, "chrome");
        }
        foreach (var (x, needle) in new[] { (0.15, 0.3), (0.3, 0.6), (0.45, 0.85) })
        {
            k.Hull(Frustum(1, x, 0.05, Sy(0.05) - 0.01, 0.055, Sy(0.05) + 0.02, 0.055, 10), Chrome, "chrome");
            k.Hull(Frustum(1, x, 0.05, Sy(0.05) + 0.02, 0.045, Sy(0.05) + 0.026, 0.045, 10), Ceramic, "plastic");
            k.Box(x - 0.004, Sy(0.05) + 0.026, 0.05 - 0.035 * needle, x + 0.004, Sy(0.05) + 0.034, 0.05, Brown, "plastic");
        }
        foreach (var (x, col) in new[] { (-0.48, Bulb), (-0.4, PilotRed), (-0.32, PilotGreen), (-0.24, Bulb) })
            k.Hull(Frustum(1, x, 0.0, Sy(0.0) - 0.006, 0.016, Sy(0.0) + 0.02, 0.012, 8), col, "", glow: true);
        // The tube screen: a deep box with a dim green face, a hood over it.
        k.Box(-0.3, 0.95, -0.4, 0.3, 1.3, -0.08, Beige, "worn paint");
        k.Box(-0.32, 1.3, -0.38, 0.32, 1.34, -0.04, Tan, "worn paint");
        k.Box(-0.24, 1.0, -0.08, 0.24, 1.25, -0.066, Grime, "plastic");
        k.Box(-0.2, 1.03, -0.07, 0.2, 1.22, -0.058, "#2f4a32", "plastic");
        for (var i = 0; i < 4; i++)
            k.Glow(-0.17, 1.06 + i * 0.04, -0.06, -0.17 + 0.08 + (i * 7 % 5) * 0.05, 1.075 + i * 0.04, -0.052, Phosphor);
        k.Box(0.12, 1.24, -0.04, 0.24, 1.33, -0.028, Mustard, "plastic"); // (the note)
        k.Box(0.14, 1.31, -0.028, 0.22, 1.34, -0.02, Tape, "fabric");
        // A mug in a cup ring at the right end.
        k.Hull(Frustum(1, 0.52, -0.25, 0.95, 0.04, 1.05, 0.04, 8), Ceramic, "gloss paint");
        k.Hull(Frustum(1, 0.52, -0.25, 0.94, 0.055, 0.955, 0.055, 8), Chrome, "chrome");
        return k.P;
    }

    /// <summary>18. The wall switch: a chunky cast box with a big lever switch, a conduit pipe up the wall and a red-and-green lamp pair.</summary>
    private static Part WallSwitch()
    {
        var k = Free("Commoner Wall Switch", "Commoner wall switch (draft): a chunky cast box painted cream long ago, a big lever thrown up, a red and a green lamp in chrome bezels, steel conduit running up the wall from it and a strip of tape with what it does written on.", [-0.12, 0.0, -0.25], [0.12, 0.5, -0.14]);
        k.Box(-0.11, 0.12, -0.25, 0.11, 0.4, -0.17, Cream, "worn paint");
        k.Box(-0.1, 0.13, -0.17, 0.1, 0.39, -0.16, Beige, "worn paint"); // (the cover plate)
        foreach (var (x, y) in new[] { (-0.08, 0.15), (0.08, 0.15), (-0.08, 0.37), (0.08, 0.37) })
            k.Box(x - 0.008, y - 0.008, -0.16, x + 0.008, y + 0.008, -0.15, Chrome, "steel");
        // The lever: a pivot block and a long handle with a black grip.
        k.Box(-0.04, 0.24, -0.16, 0.04, 0.3, -0.12, Grime, "steel");
        k.Hull([[-0.012, 0.26, -0.13], [0.012, 0.26, -0.13], [-0.012, 0.28, -0.13], [0.012, 0.28, -0.13], [-0.01, 0.36, -0.07], [0.01, 0.36, -0.07], [-0.01, 0.375, -0.075], [0.01, 0.375, -0.075]], Chrome, "chrome");
        k.Hull(Frustum(2, 0, 0.37, -0.085, 0.018, -0.045, 0.018, 8), Rubber, "plastic");
        PilotZ(k, -0.055, 0.19, -0.16, PilotRed, 0.012);
        PilotZ(k, 0.055, 0.19, -0.16, PilotGreen, 0.012);
        Tube(k, 1, 0, -0.21, 0.4, 0.5, 0.018, Chrome, "steel", 6); // (conduit up)
        k.Box(-0.03, 0.43, -0.235, 0.03, 0.45, -0.185, Grime, "steel"); // (its saddle)
        k.Box(-0.09, 0.1, -0.175, 0.09, 0.122, -0.165, Tape, "fabric"); // (the label)
        return k.P;
    }

    /// <summary>19. The lamp: a caged bulb in a dented enamel shade on a wall arm, a pull cord.</summary>
    private static Part Lamp()
    {
        var k = Free("Commoner Lamp", "Commoner lamp (draft): a bare warm bulb in a wire cage under a dented green enamel shade on a wall arm, the arm's paint flaking, a pull cord with a bead and its flex taped along the wall.", [-0.15, 0.0, -0.25], [0.15, 0.4, 0.1]);
        k.Box(-0.06, 0.2, -0.25, 0.06, 0.34, -0.22, Grime, "steel"); // (wall plate)
        Tube(k, 2, 0, 0.27, -0.22, 0.0, 0.012, Chrome, "steel", 6); // (the arm)
        k.Hull(Frustum(1, 0, 0.0, 0.26, 0.03, 0.29, 0.03, 8), Grime, "steel");
        k.Hull(Frustum(1, 0, 0.0, 0.18, 0.13, 0.27, 0.05, 10), "#4d6650", "gloss paint"); // (the shade)
        k.Hull([[0.06, 0.2, 0.09], [0.1, 0.2, 0.06], [0.09, 0.24, 0.03], [0.05, 0.23, 0.06], [0.075, 0.21, 0.05]], Rust, "rust"); // (a chip)
        k.Hull(Frustum(1, 0, 0.0, 0.11, 0.035, 0.19, 0.045, 8), Bulb, "", glow: true);
        foreach (var a in new[] { 0.0, Math.PI / 2 }) // (the cage's hoops)
            k.Box(-Math.Cos(a) * 0.06 - 0.004 * Math.Sin(a) - 0.004, 0.08, -Math.Sin(a) * 0.06 - 0.004, Math.Cos(a) * 0.06 + 0.004, 0.185, Math.Sin(a) * 0.06 + 0.004, Chrome, "steel");
        Tube(k, 1, 0.09, 0.03, 0.06, 0.2, 0.003, Ceramic, "fabric", 4); // (pull cord)
        k.Hull(Frustum(1, 0.09, 0.03, 0.04, 0.012, 0.065, 0.012, 6), Brown, "wood");
        k.Box(-0.01, 0.0, -0.25, 0.01, 0.2, -0.235, Rubber, "rubber"); // (the flex down the wall)
        k.Box(-0.02, 0.06, -0.25, 0.02, 0.09, -0.23, Tape, "fabric");
        return k.P;
    }

    /// <summary>20. The ladder: a steel ladder, its rungs worn bright, one replaced in another colour, grip tape, hooks at the top.</summary>
    private static Part Ladder()
    {
        var k = Free("Commoner Ladder", "Commoner ladder (draft): a steel ladder off something else, olive paint worn to bare metal where hands and boots go, one rung replaced with a length of pipe, grip tape wound round the stiles and two hooks over the hatch rim at the top.", [-0.25, 0, -0.25], [0.25, 2.5, -0.05]);
        foreach (var sx in new[] { -0.22, 0.22 })
        {
            k.Box(sx - 0.025, 0.0, -0.17, sx + 0.025, 2.45, -0.1, Olive, "worn paint");
            k.Box(sx - 0.03, 1.0, -0.175, sx + 0.03, 1.3, -0.095, Rubber, "fabric"); // (grip tape)
            k.Box(sx - 0.025, 0.15, -0.25, sx + 0.025, 0.2, -0.17, Grime, "steel"); // (stand-off brackets)
            k.Box(sx - 0.025, 2.0, -0.25, sx + 0.025, 2.05, -0.17, Grime, "steel");
            k.Hull([[sx - 0.02, 2.45, -0.17], [sx + 0.02, 2.45, -0.17], [sx - 0.02, 2.45, -0.1], [sx + 0.02, 2.45, -0.1], [sx - 0.02, 2.5, -0.22], [sx + 0.02, 2.5, -0.22], [sx - 0.02, 2.53, -0.18], [sx + 0.02, 2.53, -0.18]], Grime, "steel"); // (the hook)
        }
        for (var i = 0; i < 8; i++)
        {
            var y = 0.25 + i * 0.29;
            if (i == 3)
                Tube(k, 0, y, -0.135, -0.198, 0.198, 0.017, "#8a8f94", "steel", 6); // (the replaced rung: a pipe)
            else
            {
                k.Box(-0.196, y - 0.016, -0.16, 0.196, y + 0.016, -0.11, Olive, "worn paint");
                k.Box(-0.12, y + 0.016, -0.155, 0.12, y + 0.027, -0.115, "#8f9086", "steel"); // (worn bright on top)
            }
        }
        return k.P;
    }

    /// <summary>21. The door: a camper-style interior door, aluminium frame, wood-grain panel, a louvred lower half, a lever, a scuffed kick plate.</summary>
    private static Part Door()
    {
        var k = Free("Commoner Door", "Commoner door (draft): an interior door like a camper's, a dull aluminium frame round a wood-grain laminate panel, a louvred lower half, a chrome lever handle and a scuffed kick plate. A hook with a coat on the back.", [-0.55, 0, -0.08], [0.55, 2.15, 0.08], wall: false);
        k.Box(-0.55, 0.0, -0.06, -0.48, 2.15, 0.06, Chrome, "aluminium"); // (the frame)
        k.Box(0.48, 0.0, -0.06, 0.55, 2.15, 0.06, Chrome, "aluminium");
        k.Box(-0.48, 2.08, -0.06, 0.48, 2.15, 0.06, Chrome, "aluminium");
        k.Box(-0.47, 0.01, -0.025, 0.47, 2.07, 0.025, Wood, "wood"); // (the leaf)
        k.Box(-0.4, 1.05, 0.025, 0.4, 1.95, 0.036, WoodDark, "wood"); // (the upper panel's moulding)
        k.Box(-0.36, 1.09, 0.036, 0.36, 1.91, 0.047, Wood, "wood");
        for (var i = 0; i < 9; i++) // (louvres below)
            k.Box(-0.38, 0.32 + i * 0.07, 0.025, 0.38, 0.35 + i * 0.07, 0.045, WoodDark, "wood");
        k.Box(-0.44, 0.02, 0.025, 0.44, 0.24, 0.036, "#8f8d85", "steel"); // (kick plate)
        k.Box(-0.2, 0.08, 0.036, 0.05, 0.14, 0.042, Grime, "dirty steel"); // (scuffs)
        // The lever, both sides.
        foreach (var s in new[] { -1.0, 1.0 })
        {
            k.Box(0.33, 0.95, s * 0.025, 0.38, 1.1, s * 0.035, Chrome, "chrome");
            k.Box(0.22, 1.02, s * 0.035, 0.37, 1.045, s * 0.06, Chrome, "chrome");
        }
        // A hook and a coat on the back.
        k.Box(-0.03, 1.75, -0.04, 0.03, 1.8, -0.025, Chrome, "chrome");
        k.Profile(-0.18, 0.18, [(1.0, -0.06), (1.0, -0.03), (1.75, -0.03), (1.75, -0.07), (1.3, -0.1)], Mustard, "fabric");
        return k.P;
    }

    /// <summary>22. The airlock hatch: an octagonal pressure hatch with a hand wheel, dogs, a gasket and faded hazard paint, a patch welded on.</summary>
    private static Part AirlockHatch()
    {
        var k = Free("Commoner Airlock Hatch", "Commoner airlock hatch (draft): a heavy eight-sided pressure hatch in a thick frame, a spoked hand wheel in the middle, six dogs round the rim, a black gasket, the hazard paint faded to tan, a patch welded on and a stencilled word half gone.", [-0.65, 0, -0.15], [0.65, 1.3, 0.15], wall: false);
        const double cy = 0.65;
        (double, double)[] Oct(double r) => [.. Enumerable.Range(0, 8).Select(i => (r * Math.Cos((i + 0.5) * Math.PI / 4), cy + r * Math.Sin((i + 0.5) * Math.PI / 4)))];
        k.Face(-0.12, 0.08, Oct(0.68), Grime, "dirty steel"); // (the frame)
        k.Face(0.08, 0.095, Oct(0.62), Rubber, "rubber"); // (the gasket)
        k.Face(0.095, 0.16, Oct(0.56), Beige, "rusty paint"); // (the hatch)
        for (var i = 0; i < 8; i++) // (faded hazard chevrons round the hatch face)
            if (i % 2 == 0)
            {
                var a = (i + 0.5) * Math.PI / 4;
                double x = 0.48 * Math.Cos(a), y = cy + 0.48 * Math.Sin(a);
                k.Box(x - 0.05, y - 0.05, 0.16, x + 0.05, y + 0.05, 0.175, "#b39552", "hazard paint");
            }
        for (var i = 0; i < 6; i++) // (dogs on the frame)
        {
            var a = i * Math.PI / 3;
            double x = 0.64 * Math.Cos(a), y = cy + 0.64 * Math.Sin(a);
            k.Box(x - 0.04, y - 0.04, 0.08, x + 0.04, y + 0.04, 0.13, Chrome, "steel");
        }
        // The hand wheel: hub, rim, four spokes.
        Tube(k, 2, 0, cy, 0.16, 0.28, 0.04, Grime, "steel");
        k.Hull(Frustum(2, 0, cy, 0.24, 0.22, 0.27, 0.22, 12), "#9b4a30", "worn paint");
        k.Box(-0.2, cy - 0.015, 0.235, 0.2, cy + 0.015, 0.26, "#9b4a30", "worn paint");
        k.Box(-0.015, cy - 0.2, 0.236, 0.015, cy + 0.2, 0.259, "#9b4a30", "worn paint");
        // A patch welded on, and a word half gone.
        k.Box(-0.4, 0.25, 0.16, -0.18, 0.42, 0.174, "#8a8a80", "steel");
        k.Box(0.12, 0.92, 0.16, 0.42, 0.98, 0.168, Grime, "worn paint");
        k.Box(0.12, 0.92, 0.168, 0.24, 0.98, 0.176, Brown, "worn paint");
        return k.P;
    }

    /// <summary>23. The cargo crate: a plywood crate with steel corners, a stencil, a ratchet strap and a sticker from a port long closed.</summary>
    private static Part CargoCrate()
    {
        var k = Free("Commoner Cargo Crate", "Commoner cargo crate (draft): a plywood crate with bent steel corners, a stencilled number, a ratchet strap round it and old freight stickers. The lid nailed shut, one board replaced in a paler wood.", [-0.4, 0, -0.4], [0.4, 0.8, 0.4], wall: false, material: "wood");
        k.Box(-0.4, 0.0, -0.4, 0.4, 0.8, 0.4, "#9c7a4e", "wood");
        for (var i = 0; i < 4; i++) // (boards on the front)
            k.Box(-0.37, 0.04 + i * 0.19, 0.4, 0.37, 0.2 + i * 0.19, 0.411, i == 2 ? "#c4a273" : "#8f6e44", "wood");
        foreach (var sx in new[] { -1.0, 1.0 }) // (steel corners)
            foreach (var sz in new[] { -1.0, 1.0 })
                k.Box(sx * 0.42 - (sx > 0 ? 0.06 : 0), 0.0, sz * 0.42 - (sz > 0 ? 0.06 : 0), sx * 0.42 + (sx < 0 ? 0.06 : 0), 0.8, sz * 0.42 + (sz < 0 ? 0.06 : 0), Grime, "rusty paint");
        k.Box(-0.42, 0.8, -0.42, 0.42, 0.83, 0.42, "#8f6e44", "wood"); // (the lid)
        // Ratchet strap round it.
        k.Box(-0.06, 0.0, -0.425, 0.06, 0.84, 0.425, Orange, "fabric");
        k.Box(-0.05, 0.4, 0.425, 0.05, 0.5, 0.445, Chrome, "steel");
        // Stencil and stickers.
        k.Box(0.12, 0.45, 0.411, 0.34, 0.6, 0.42, Grime, "worn paint");
        k.Box(-0.34, 0.25, 0.411, -0.14, 0.35, 0.419, Ceramic, "plastic");
        k.Box(-0.34, 0.25, 0.419, -0.24, 0.35, 0.427, PilotRed, "plastic");
        return k.P;
    }

    /// <summary>24. The workbench: a wood-top bench with a vice, a pegboard of tools, a drawer unit and a clamp-on lamp.</summary>
    private static Part Workbench()
    {
        var k = Free("Commoner Workbench", "Commoner workbench (draft): a thick wooden top worn dark and gouged on a welded frame, a big vice on the corner, a pegboard behind with a spanner, a hammer and a saw on it, a drawer unit with one drawer missing and a clamp-on lamp.", [-0.8, 0, -0.35], [0.8, 1.7, 0.35]);
        foreach (var sx in new[] { -0.74, 0.74 }) // (the frame)
            foreach (var sz in new[] { -0.29, 0.29 })
                k.Box(sx - 0.03, 0.0, sz - 0.03, sx + 0.03, 0.86, sz + 0.03, Teal, "rusty paint");
        k.Box(-0.74, 0.18, -0.31, 0.74, 0.22, -0.27, Teal, "rusty paint");
        k.Box(-0.74, 0.18, 0.27, 0.74, 0.22, 0.31, Teal, "rusty paint");
        k.Box(-0.8, 0.86, -0.35, 0.8, 0.94, 0.35, Wood, "wood"); // (the top)
        k.Box(-0.4, 0.94, 0.0, 0.1, 0.946, 0.2, WoodDark, "wood"); // (worn patch)
        // The drawer unit under the right.
        k.Box(0.2, 0.25, -0.28, 0.7, 0.84, 0.28, Mustard, "worn paint");
        for (var i = 0; i < 3; i++)
        {
            var y = 0.3 + i * 0.18;
            if (i == 1)
                k.Box(0.24, y, 0.2, 0.66, y + 0.15, 0.28, Rubber, "dirty steel"); // (the missing drawer's hole)
            else
            {
                k.Box(0.24, y, 0.28, 0.66, y + 0.15, 0.292, Mustard, "worn paint");
                k.Box(0.4, y + 0.06, 0.292, 0.5, y + 0.09, 0.305, Chrome, "chrome");
            }
        }
        // The vice on the front left corner.
        k.Box(-0.75, 0.94, 0.15, -0.55, 1.02, 0.33, "#3a5a78", "worn paint");
        k.Box(-0.72, 1.02, 0.2, -0.58, 1.1, 0.24, "#3a5a78", "worn paint");
        k.Box(-0.72, 1.02, 0.28, -0.58, 1.1, 0.32, "#3a5a78", "worn paint");
        Tube(k, 2, -0.65, 1.06, 0.32, 0.44, 0.012, Chrome, "steel", 6);
        k.Box(-0.73, 1.05, 0.43, -0.57, 1.07, 0.45, Chrome, "steel");
        // The pegboard and tools.
        k.Box(-0.8, 0.94, -0.35, 0.8, 1.7, -0.32, "#b39a72", "painted wood");
        k.Box(-0.6, 1.2, -0.32, -0.58, 1.5, -0.3, Chrome, "steel"); // (a spanner)
        k.Box(-0.62, 1.47, -0.32, -0.56, 1.52, -0.3, Chrome, "steel");
        k.Box(-0.4, 1.15, -0.32, -0.38, 1.45, -0.3, Wood, "wood"); // (a hammer)
        k.Box(-0.44, 1.45, -0.32, -0.34, 1.49, -0.29, Grime, "steel");
        k.Hull([[-0.1, 1.2, -0.32], [0.3, 1.25, -0.32], [0.3, 1.33, -0.32], [-0.1, 1.42, -0.32], [-0.1, 1.2, -0.305], [0.3, 1.25, -0.305], [0.3, 1.33, -0.305], [-0.1, 1.42, -0.305]], Chrome, "steel"); // (a saw)
        k.Box(-0.18, 1.2, -0.32, -0.1, 1.42, -0.3, Wood, "wood");
        // The clamp-on lamp.
        k.Box(0.6, 0.94, -0.33, 0.66, 1.0, -0.28, Grime, "steel");
        Tube(k, 1, 0.63, -0.3, 1.0, 1.45, 0.01, Grime, "steel", 6);
        k.Hull(Frustum(2, 0.63, 1.45, -0.3, 0.04, -0.15, 0.08, 8), Mustard, "gloss paint");
        k.Hull(Frustum(2, 0.63, 1.45, -0.17, 0.05, -0.155, 0.05, 8), Bulb, "", glow: true);
        return k.P;
    }

    /// <summary>25. The point defence gun: an old twin autocannon on a pedestal, hand wheels for traverse and elevation, an ammo can on its side.</summary>
    private static Part PointDefence()
    {
        var k = Free("Commoner Point Defence", "Commoner point defence (draft): an old twin autocannon on a pedestal like a deck gun from a war long ago, faded olive with stencils, hand wheels for traverse and elevation, a bicycle seat for the gunner, an ammo can clipped to its side and tape round one barrel's cooling jacket.", [-0.6, 0, -0.7], [0.6, 1.5, 1.4], wall: false);
        Tube(k, 1, 0, 0, 0.0, 0.08, 0.45, Grime, "dirty steel", 10); // (the base)
        Tube(k, 1, 0, 0, 0.08, 0.9, 0.12, Olive, "worn paint", 10); // (the pedestal)
        k.Hull(Frustum(1, 0, 0, 0.9, 0.2, 0.98, 0.2, 10), Grime, "steel"); // (the turntable)
        // The cradle: two cheeks and the twin barrels.
        foreach (var sx in new[] { -0.2, 0.2 })
            k.Box(sx - 0.03, 0.98, -0.2, sx + 0.03, 1.3, 0.2, Olive, "worn paint");
        k.Box(-0.17, 1.02, -0.45, 0.17, 1.3, 0.35, Olive, "rusty paint"); // (the receiver)
        foreach (var sx in new[] { -0.08, 0.08 })
        {
            Tube(k, 2, sx, 1.18, 0.35, 0.8, 0.05, Grime, "dirty steel"); // (the cooling jacket)
            Tube(k, 2, sx, 1.18, 0.8, 1.38, 0.025, Grime, "steel");
            k.Hull(Frustum(2, sx, 1.18, 1.32, 0.035, 1.4, 0.04, 8), Rubber, "soot"); // (muzzle)
        }
        k.Hull(Frustum(2, -0.08, 1.18, 0.5, 0.054, 0.62, 0.054, 8), Tape, "fabric"); // (taped jacket)
        // Hand wheels for traverse and elevation.
        Tube(k, 0, 1.1, -0.1, 0.23, 0.3, 0.015, Chrome, "steel", 6);
        k.Hull(Frustum(0, 1.1, -0.1, 0.3, 0.1, 0.32, 0.1, 10), "#9b4a30", "worn paint");
        Tube(k, 2, -0.3, 0.75, -0.12, -0.2, 0.015, Chrome, "steel", 6);
        k.Hull(Frustum(2, -0.3, 0.75, -0.22, 0.1, -0.2, 0.1, 10), "#9b4a30", "worn paint");
        k.Box(-0.33, 0.73, -0.12, -0.27, 0.78, -0.02, Grime, "steel");
        // The gunner's bicycle seat on an arm behind.
        k.Box(-0.02, 0.6, -0.12, 0.02, 0.64, -0.55, Grime, "steel");
        Tube(k, 1, 0, -0.55, 0.64, 0.78, 0.015, Chrome, "steel", 6);
        k.Hull([[-0.1, 0.78, -0.62], [0.1, 0.78, -0.62], [-0.03, 0.78, -0.4], [0.03, 0.78, -0.4], [-0.1, 0.83, -0.62], [0.1, 0.83, -0.62], [-0.03, 0.82, -0.4], [0.03, 0.82, -0.4]], Vinyl, "rubber");
        // The ammo can on the +x side, its feed.
        k.Box(0.24, 1.0, -0.25, 0.44, 1.26, 0.15, Avocado, "worn paint");
        k.Box(0.3, 1.26, -0.12, 0.38, 1.29, 0.02, Grime, "steel");
        k.Box(0.17, 1.15, -0.05, 0.24, 1.2, 0.1, "#9a6a34", "brass"); // (the belt in)
        k.Box(-0.12, 1.3, -0.3, 0.12, 1.31, 0.0, Mustard, "worn paint"); // (stencil)
        return k.P;
    }

    public void Make(PartOutput o)
    {
        o.Own(Out);
        var all = new List<Part>
        {
            Reactor(), Battery(), Capacitor(), FuelTank(), LifeSupport(), SensorMast(), Magazine(),
            OxygenBottles(), Radiator(), RcsBlock(), DriveNozzle(), PilotSeat(), Bunk(), Galley(), Head(), Locker(), Console(),
            WallSwitch(), Lamp(), Ladder(), Door(), AirlockHatch(), CargoCrate(), Workbench(), PointDefence(),
        };
        foreach (var p in all)
        {
            var d = p.Json();
            o.File($"{Out}/{FileName((string)d["name"]!)}", PyJson.Dumps(d, 2) + "\n");
        }
    }
}
