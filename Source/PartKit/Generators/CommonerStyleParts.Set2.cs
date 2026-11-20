using static CallOfSlop.PartKit.PartKitMath;
using K = CallOfSlop.PartKit.Generators.FittingLookParts;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The commoners' style, round 2 (Karl, 2026-11-20: "Feedback is in! Now make another 100 parts based around the feedback").
/// What round 1's grades said: the reactor, the battery and the radiator scored 5 (exposed, mismatched, patched machinery that
/// still runs); the ladder was too nice for these people; the head "looks too nice", the RCS block "not scrappy enough", the
/// workbench "doesn't read as rusty space ship stuff", the crate "doesn't look interesting", the door and the fuel tank and the
/// capacitor low; avoid weapons for now. So round 2 leans on working machinery: pipes run on the outside, dials, valve wheels,
/// tube frames, bands and patches in other colours, rust runs, tape, cables loose, and fewer clean flat panels or domestic
/// wood. Redos of the low ones are here too (scrappier crate, head, RCS, workbench, door, fuel tank, capacitor, ladder).
/// </summary>
public sealed partial class CommonerStyleParts
{
    private const string Copper = "#b06f3d", Brass = "#9a6a34", Steel = "#7d8085", Lagging = "#b9b39c", Blue = "#3f5a72", Signal = "#c23b2a";

    // ---- Round 2's kit

    /// <summary>A free-standing piece for round 2: "Commoner NAME", its description after "): " is the sheet's idea line.</summary>
    private static Part F(string name, string desc, double[] mn, double[] mx, Action<K.Kit> draw, bool wall = true)
    {
        var k = Free("Commoner " + name, $"Commoner {name.ToLowerInvariant()} (draft, set 2): {desc}", mn, mx, wall: wall);
        draw(k);
        return k.P;
    }

    /// <summary>A module for round 2 on the standard Module Plug; draw gets the half width inside the corner lugs.</summary>
    private static Part M(string name, string desc, int n, double height, string[] ports, Action<K.Kit, double> draw)
    {
        var k = K.Reactor("", n, $"Commoner {name.ToLowerInvariant()} (draft, set 2): {desc}", height, "Commoner " + name, Section, ports, group: Group);
        draw(k, n / 2.0 - 0.124);
        return k.P;
    }

    /// <summary>A pipe through points that differ along one axis at a time, with a joint lump at each bend.</summary>
    private static void Pipe(K.Kit k, string col, double r, string finish, params (double X, double Y, double Z)[] pts)
    {
        for (var i = 0; i + 1 < pts.Length; i++)
        {
            var (a, b) = (pts[i], pts[i + 1]);
            if (a.X != b.X)
                Tube(k, 0, a.Y, a.Z, Math.Min(a.X, b.X), Math.Max(a.X, b.X), r, col, finish, 6);
            else if (a.Y != b.Y)
                Tube(k, 1, a.X, a.Z, Math.Min(a.Y, b.Y), Math.Max(a.Y, b.Y), r, col, finish, 6);
            else
                Tube(k, 2, a.X, a.Y, Math.Min(a.Z, b.Z), Math.Max(a.Z, b.Z), r, col, finish, 6);
            if (i > 0)
                k.Box(a.X - r * 1.35, a.Y - r * 1.35, a.Z - r * 1.35, a.X + r * 1.35, a.Y + r * 1.35, a.Z + r * 1.35, col, finish);
        }
    }

    /// <summary>A ring of straight bars (a hand wheel's rim, a hoop) about an axis: 0 x, 1 y, 2 z, centred on (a, b) at t.</summary>
    private static void Ring(K.Kit k, int axis, double a, double b, double t, double r, double thick, string col, string finish, int sides = 6)
    {
        double[] P(double ang, double rr, double tt)
        {
            double u = a + rr * Math.Cos(ang), v = b + rr * Math.Sin(ang);
            return axis switch { 0 => [tt, u, v], 1 => [u, tt, v], _ => [u, v, tt] };
        }
        for (var i = 0; i < sides; i++)
        {
            double a0 = i * 2 * Math.PI / sides, a1 = (i + 1) * 2 * Math.PI / sides;
            k.Hull([P(a0, r - thick / 2, t - thick / 2), P(a0, r + thick / 2, t - thick / 2), P(a0, r - thick / 2, t + thick / 2), P(a0, r + thick / 2, t + thick / 2),
                    P(a1, r - thick / 2, t - thick / 2), P(a1, r + thick / 2, t - thick / 2), P(a1, r - thick / 2, t + thick / 2), P(a1, r + thick / 2, t + thick / 2)], col, finish);
        }
    }

    /// <summary>A hand wheel facing +z (axis 2) or up (axis 1): a six-bar rim, a hub and two spokes.</summary>
    private static void Wheel(K.Kit k, int axis, double a, double b, double t, double r, string col = "#9b4a30")
    {
        Ring(k, axis, a, b, t, r, Math.Max(0.012, r * 0.16), col, "worn paint");
        k.Hull(Frustum(axis, a, b, t - 0.02, r * 0.22, t + 0.02, r * 0.22, 6), Grime, "steel");
        if (axis == 2)
        {
            Tube(k, 0, b, t, a - r, a + r, 0.006, col, "worn paint", 4);
            Tube(k, 1, a, t, b - r, b + r, 0.006, col, "worn paint", 4);
        }
        else
        {
            Tube(k, 0, t, b, a - r, a + r, 0.006, col, "worn paint", 4);
            Tube(k, 2, a, t, b - r, b + r, 0.006, col, "worn paint", 4);
        }
    }

    /// <summary>An upright tank: body, domed ends, two straps a shade darker.</summary>
    private static void TankUp(K.Kit k, double x, double z, double y0, double y1, double r, string col, string finish = "worn paint", int sides = 10)
    {
        var dome = Math.Min(r * 0.45, (y1 - y0) * 0.2);
        k.Hull(Frustum(1, x, z, y0, r * 0.75, y0 + dome, r, sides), col, finish);
        k.Hull(Frustum(1, x, z, y0 + dome, r, y1 - dome, r, sides), col, finish);
        k.Hull(Frustum(1, x, z, y1 - dome, r, y1, r * 0.6, sides), col, finish);
        foreach (var f in new[] { 0.3, 0.72 })
        {
            var y = y0 + (y1 - y0) * f;
            k.Hull(Frustum(1, x, z, y, r + 0.008, y + 0.035, r + 0.008, sides), Grime, "rusty paint");
        }
    }

    /// <summary>A tank lying along x: body, domed ends, a strap.</summary>
    private static void TankLie(K.Kit k, double y, double z, double x0, double x1, double r, string col, string finish = "worn paint")
    {
        var dome = r * 0.35;
        k.Hull(Frustum(0, y, z, x0, r * 0.6, x0 + dome, r, 10), col, finish);
        k.Hull(Frustum(0, y, z, x0 + dome, r, x1 - dome, r, 10), col, finish);
        k.Hull(Frustum(0, y, z, x1 - dome, r, x1, r * 0.6, 10), col, finish);
    }

    /// <summary>A frame of square tube along a box's twelve edges.</summary>
    private static void Cage(K.Kit k, double x0, double y0, double z0, double x1, double y1, double z1, string col, double w = 0.025, string finish = "rusty paint")
    {
        foreach (var x in new[] { x0, x1 - w })
            foreach (var z in new[] { z0, z1 - w })
                k.Box(x, y0, z, x + w, y1, z + w, col, finish);
        foreach (var y in new[] { y0 + 0.002, y1 - w - 0.002 })
        {
            k.Box(x0 + w, y, z0 + 0.002, x1 - w, y + w, z0 + w - 0.002, col, finish);
            k.Box(x0 + w, y, z1 - w + 0.002, x1 - w, y + w, z1 - 0.002, col, finish);
            k.Box(x0 + 0.002, y + 0.003, z0 + w, x0 + w - 0.002, y + w - 0.003, z1 - w, col, finish);
            k.Box(x1 - w + 0.002, y + 0.003, z0 + w, x1 - 0.002, y + w - 0.003, z1 - w, col, finish);
        }
    }

    /// <summary>A bolted access panel on a +z face at z, a bolt in each corner.</summary>
    private static void PanelZ(K.Kit k, double x0, double y0, double x1, double y1, double z, string col, string finish = "worn paint")
    {
        k.Box(x0, y0, z, x1, y1, z + 0.012, col, finish);
        foreach (var x in new[] { x0 + 0.025, x1 - 0.025 })
            foreach (var y in new[] { y0 + 0.025, y1 - 0.025 })
                k.Box(x - 0.009, y - 0.009, z + 0.012, x + 0.009, y + 0.009, z + 0.023, Chrome, "steel");
    }

    /// <summary>Louvres across a +z face at z.</summary>
    private static void LouvreZ(K.Kit k, double x0, double x1, double y0, double y1, double z, int n, string col)
    {
        k.Box(x0, y0, z - 0.004, x1, y1, z + 0.006, Grime, "dirty steel");
        for (var i = 0; i < n; i++)
        {
            var y = y0 + 0.01 + (y1 - y0 - 0.03) * i / Math.Max(1, n - 1);
            k.Box(x0 + 0.01, y, z + 0.006, x1 - 0.01, y + 0.018, z + 0.026, col, "worn paint");
        }
    }

    /// <summary>A rust run down a side face (normal +x or -x by sx).</summary>
    private static void StreakX(K.Kit k, double x, double sx, double z, double y0, double y1, double w = 0.03) =>
        k.Hull([[x, y1, z - w / 2], [x, y1, z + w / 2], [x, y0, z - w * 0.15], [x, y0, z + w * 0.15],
                [x + sx * 0.011, y1, z - w / 2], [x + sx * 0.011, y1, z + w / 2], [x + sx * 0.011, y0, z - w * 0.15], [x + sx * 0.011, y0, z + w * 0.15]], Rust, "rust");

    /// <summary>A painted box body with a dark plinth and a rusty top lip: the default cabinet.</summary>
    private static void Cab(K.Kit k, double x0, double y0, double z0, double x1, double y1, double z1, string col, string finish = "worn paint")
    {
        k.Box(x0 + 0.01, y0, z0 + 0.01, x1 - 0.01, y0 + 0.05, z1 - 0.01, Grime, "dirty steel");
        k.Box(x0, y0 + 0.05, z0, x1, y1 - 0.025, z1, col, finish);
        k.Box(x0 - 0.006, y1 - 0.025, z0 - 0.006, x1 + 0.006, y1, z1 + 0.006, Tan, "rusty paint");
    }

    /// <summary>A loose cable: a sagging run of three straight pieces from (x0, y0) to (x1, y1) at depth z.</summary>
    private static void CableZ(K.Kit k, double z, double x0, double y0, double x1, double y1, double sag, string col = Rubber)
    {
        var xm = (x0 + x1) / 2;
        var ym = Math.Min(y0, y1) - sag;
        void Seg(double ax, double ay, double bx, double by)
        {
            const double r = 0.009;
            k.Hull([[ax, ay - r, z - r], [ax, ay + r, z - r], [ax, ay - r, z + r], [ax, ay + r, z + r], [bx, by - r, z - r], [bx, by + r, z - r], [bx, by - r, z + r], [bx, by + r, z + r]], col, "rubber");
        }
        Seg(x0, y0, x0 + (xm - x0) * 0.6, ym);
        Seg(x0 + (xm - x0) * 0.6, ym, x1 - (x1 - xm) * 0.6, ym);
        Seg(x1 - (x1 - xm) * 0.6, ym, x1, y1);
    }

    /// <summary>A hand-painted stencil block on a +z face.</summary>
    private static void Stencil(K.Kit k, double x0, double y0, double x1, double y1, double z, string col = Mustard) =>
        k.Box(x0, y0, z, x1, y1, z + 0.011, col, "worn paint");

    /// <summary>A warm bulb in a little cage facing +z.</summary>
    private static void CagedBulbZ(K.Kit k, double x, double y, double z)
    {
        k.Hull(Frustum(2, x, y, z, 0.035, z + 0.02, 0.035, 8), Grime, "steel");
        k.Hull(Frustum(2, x, y, z + 0.02, 0.022, z + 0.07, 0.026, 8), Bulb, "", glow: true);
        Tube(k, 0, y + 0.032, z + 0.05, x - 0.03, x + 0.03, 0.004, Chrome, "steel", 4);
        Tube(k, 1, x + 0.032, z + 0.05, y - 0.03, y + 0.03, 0.004, Chrome, "steel", 4);
    }

    // ---- The 100

    private static IEnumerable<Part> Set2()
    {
        // ===== Power =====
        yield return M("Reactor 2x2", "two old reactor drums of different makes side by side, joined by a hand-made manifold, one big rod-crank wheel between them, copper bypass loops, a riveted shroud patch and a dial board bolted on the front.", 2, 1.6, ["Data 1", "Fuel A", "Coolant A"], (k, h) =>
        {
            TankUp(k, -0.42, -0.05, 0.08, 1.4, 0.36, Cream);
            TankUp(k, 0.42, -0.05, 0.08, 1.25, 0.32, Beige, "rusty paint", 8);
            for (var i = 0; i < 3; i++)
                k.Hull(Frustum(1, -0.42, -0.05, 0.8 + i * 0.045, 0.37, 0.835 + i * 0.045, 0.37, 10), i == 0 ? Brown : i == 1 ? Orange : Mustard, "worn paint");
            Pipe(k, Copper, 0.035, "copper", (-0.42, 1.38, -0.05), (-0.42, 1.5, -0.05), (0.42, 1.5, -0.05), (0.42, 1.23, -0.05));
            Pipe(k, Copper, 0.022, "copper", (-0.8, 0.2, 0.3), (-0.8, 1.0, 0.3), (-0.75, 1.0, 0.3));
            Pipe(k, Grime, 0.05, "dirty steel", (0.0, 0.1, -0.5), (0.0, 0.6, -0.5), (0.0, 0.6, -0.05));
            Wheel(k, 2, 0.0, 1.05, 0.38, 0.17);
            Tube(k, 2, 0.0, 1.05, -0.05, 0.37, 0.025, Chrome, "steel");
            k.Box(-0.3, 0.3, 0.42, 0.3, 0.62, 0.45, Grime, "dirty steel");
            DialZ(k, -0.17, 0.46, 0.45, 0.06, 0.8);
            DialZ(k, 0.0, 0.46, 0.45, 0.06, 0.4);
            DialZ(k, 0.17, 0.46, 0.45, 0.06, 0.55);
            k.Box(0.3, 0.5, 0.2, 0.5, 0.72, 0.27, Avocado, "rusty paint");
            StreakZ(k, -0.3, 0.3, 1.1, 0.305, 0.04);
            k.Hull(Frustum(2, -0.42, 0.7, 0.3, 0.07, 0.4, 0.07, 10), Chrome, "chrome");
            k.Hull(Frustum(2, -0.42, 0.7, 0.39, 0.05, 0.405, 0.05, 10), Bulb, "", glow: true);
        });
        yield return M("Genset", "an old engine generator: a finned cylinder block, a flywheel on the side, an exhaust stack with a flapping rain cap, a dented muffler, an oil drip tray and a pull cord.", 1, 1.15, ["Data 1", "Fuel A"], (k, h) =>
        {
            k.Box(-0.36, 0.08, -0.36, 0.36, 0.12, 0.36, Grime, "soot"); // (the drip tray)
            Cab(k, -0.3, 0.12, -0.25, 0.15, 0.72, 0.25, Mustard, "rusty paint");
            for (var i = 0; i < 6; i++)
                k.Box(-0.28, 0.74 + i * 0.035, -0.22, 0.12, 0.755 + i * 0.035, 0.22, Steel, "dirty steel"); // (head fins)
            k.Hull(Frustum(0, 0.4, 0.0, 0.15, 0.2, 0.2, 0.2, 12), Grime, "steel"); // (flywheel)
            k.Hull(Frustum(0, 0.4, 0.0, 0.2, 0.06, 0.24, 0.06, 8), Chrome, "steel");
            TankLie(k, 0.95, 0.15, -0.15, 0.25, 0.08, "#5e5a52", "rust"); // (the muffler)
            Pipe(k, Rust, 0.025, "rust", (0.1, 0.95, 0.15), (0.32, 0.95, 0.15), (0.32, 1.12, 0.15));
            k.Hull([[0.27, 1.12, 0.1], [0.37, 1.12, 0.1], [0.27, 1.12, 0.2], [0.37, 1.12, 0.2], [0.27, 1.15, 0.1], [0.37, 1.18, 0.1], [0.27, 1.15, 0.2], [0.37, 1.18, 0.2]], Grime, "soot");
            PanelZ(k, -0.25, 0.25, 0.1, 0.6, 0.25, Tan, "rusty paint");
            k.Box(-0.33, 0.45, 0.0, -0.3, 0.5, 0.06, Signal, "plastic"); // (pull cord handle)
            Pipe(k, Rubber, 0.012, "rubber", (0.3, 0.12, -0.3), (0.3, 0.3, -0.3), (0.1, 0.3, -0.3));
        });
        yield return F("Solar Wing", "a folding solar panel on a hinged wall arm, its cells blue-grey with two replaced by mismatched ones and one cracked cell taped over, a junction box and a loose lead.", [-0.6, 0, -0.25], [0.6, 1.6, 0.1], k =>
        {
            k.Box(-0.08, 0.2, -0.25, 0.08, 0.4, -0.2, Grime, "steel");
            Tube(k, 1, 0, -0.18, 0.3, 0.75, 0.02, Steel, "steel");
            k.Box(-0.58, 0.75, -0.18, 0.58, 1.55, -0.14, Chrome, "aluminium"); // (frame)
            for (var i = 0; i < 4; i++)
                for (var j = 0; j < 3; j++)
                {
                    var odd = (i == 1 && j == 2) || (i == 3 && j == 0);
                    k.Box(-0.55 + i * 0.275, 0.78 + j * 0.255, -0.14, -0.29 + i * 0.275, 1.02 + j * 0.255, -0.128, odd ? "#4b4f6a" : "#2e3d52", "plastic");
                }
            TapeZ(k, -0.127, -0.5, 0.8, -0.32, 1.0);
            k.Box(-0.06, 0.6, -0.17, 0.06, 0.72, -0.1, Grime, "plastic");
            CableZ(k, -0.12, 0.0, 0.62, 0.3, 0.1, 0.1);
        });
        yield return F("Battery Shelf", "four old car-style batteries on a sagging steel wall shelf, jumper leads chained between them, a chalk tally of charge scratched on a board and a rag over one.", [-0.55, 0, -0.25], [0.55, 1.1, 0.05], k =>
        {
            k.Box(-0.55, 0.4, -0.25, 0.55, 0.43, 0.04, Steel, "rusty paint");
            foreach (var sx in new[] { -0.45, 0.45 })
                k.Hull([[sx - 0.015, 0.4, -0.25], [sx + 0.015, 0.4, -0.25], [sx - 0.015, 0.4, 0.0], [sx + 0.015, 0.4, 0.0], [sx - 0.015, 0.15, -0.25], [sx + 0.015, 0.15, -0.25]], Steel, "rusty paint");
            string[] cols = [Rubber, "#4a5a6a", Grime, Cream];
            for (var i = 0; i < 4; i++)
            {
                var x = -0.52 + i * 0.265;
                k.Box(x, 0.43, -0.2, x + 0.23, 0.63 + (i % 2) * 0.03, 0.02, cols[i], i == 3 ? "worn paint" : "plastic");
                k.Box(x + 0.03, 0.63 + (i % 2) * 0.03, -0.1, x + 0.06, 0.66 + (i % 2) * 0.03, -0.07, Brass, "brass");
                k.Box(x + 0.17, 0.63 + (i % 2) * 0.03, -0.1, x + 0.2, 0.66 + (i % 2) * 0.03, -0.07, Signal, "plastic");
            }
            for (var i = 0; i < 3; i++)
                CableZ(k, -0.085, -0.34 + i * 0.265, 0.67, -0.29 + (i + 1) * 0.265 - 0.2, 0.67, -0.06, i == 1 ? Signal : Rubber);
            k.Box(-0.4, 0.75, -0.25, 0.1, 1.05, -0.235, "#2c3a30", "matte paint"); // (the chalk board)
            for (var i = 0; i < 5; i++)
                k.Box(-0.36 + i * 0.05, 0.85, -0.235, -0.35 + i * 0.05, 0.97, -0.228, Ceramic, "plastic");
            k.Box(0.27, 0.62, -0.21, 0.5, 0.67, 0.03, Orange, "fabric"); // (a rag)
        });
        yield return M("Battery 2x2", "a pallet of mismatched batteries in a welded angle crate, copper strap bussing them together, a charge controller box with a dial and a cooling fan clamped on with a hose clip.", 2, 1.1, ["Data 1"], (k, h) =>
        {
            Cage(k, -0.82, 0.08, -0.82, 0.82, 0.95, 0.82, Olive);
            string[] cols = [Rubber, Grime, "#4a5a6a", Cream, Mustard, Rubber, "#5b4a3a", Grime, Rubber];
            for (var i = 0; i < 3; i++)
                for (var j = 0; j < 3; j++)
                {
                    double x = -0.76 + i * 0.51, z = -0.76 + j * 0.51, hh = 0.5 + ((i * 3 + j) % 3) * 0.06;
                    k.Box(x, 0.1, z, x + 0.47, 0.1 + hh, z + 0.44, cols[i * 3 + j], (i + j) % 2 == 0 ? "plastic" : "worn paint");
                }
            foreach (var z in new[] { -0.55, -0.04, 0.47 })
                k.Box(-0.74, 0.72, z, 0.74, 0.735, z + 0.05, Copper, "copper");
            Cab(k, -0.3, 0.95, -0.2, 0.3, 1.1, 0.2, Cream);
            DialZ(k, 0.0, 1.03, 0.2, 0.045, 0.6);
            k.Hull(Frustum(2, 0.6, 0.5, 0.82, 0.15, 0.86, 0.15, 10), Grime, "dirty steel"); // (the fan)
            Tube(k, 1, 0.6, 0.865, 0.37, 0.63, 0.006, Chrome, "steel", 4);
            Tube(k, 0, 0.5, 0.869, 0.47, 0.73, 0.006, Chrome, "steel", 4);
        });
        yield return M("Capacitor Rack", "a rack of salvaged capacitor cans, no two alike, held by a steel band with a turnbuckle, scorch marks round one, a red paint smear for HIGH VOLTAGE and cables spilling out of the top.", 1, 1.15, ["Data 1"], (k, h) =>
        {
            k.Box(-0.36, 0.08, -0.36, 0.36, 0.13, 0.36, Steel, "rusty paint");
            (double X, double Z, double R, double H, string C)[] cans = [(-0.2, -0.2, 0.14, 0.85, "#7f8a78"), (0.18, -0.2, 0.12, 0.7, Cream), (-0.2, 0.18, 0.12, 0.78, Steel), (0.19, 0.18, 0.15, 0.9, "#5d6670")];
            foreach (var c in cans)
            {
                k.Hull(Frustum(1, c.X, c.Z, 0.13, c.R, 0.13 + c.H, c.R, 8), c.C, "worn paint");
                k.Hull(Frustum(1, c.X, c.Z, 0.13 + c.H, c.R * 0.5, 0.17 + c.H, c.R * 0.4, 6), Ceramic, "gloss paint");
            }
            k.Hull(Frustum(1, 0, 0, 0.55, 0.37, 0.58, 0.37, 8), Grime, "steel"); // (the band)
            k.Box(-0.05, 0.53, 0.35, 0.05, 0.6, 0.4, Chrome, "steel");
            k.Hull([[0.05, 0.4, 0.33], [0.3, 0.45, 0.24], [0.3, 0.85, 0.24], [0.12, 0.8, 0.33], [0.18, 0.6, 0.34]], "#2b2622", "soot"); // (scorch)
            Stencil(k, -0.3, 0.25, -0.05, 0.33, 0.255, Signal);
            CableZ(k, 0.18, -0.2, 1.02, 0.4, 0.7, -0.15, Signal);
            CableZ(k, -0.18, 0.19, 1.1, -0.42, 0.6, -0.12);
        });
        yield return F("Junction Box", "a cast junction box with conduit in and out, its cover held by three screws of four, a fat bundle of cables spilling out of a knock-out and tape round the worst one.", [-0.2, 0, -0.25], [0.2, 0.6, -0.1], k =>
        {
            k.Box(-0.15, 0.15, -0.25, 0.15, 0.45, -0.17, Steel, "rusty paint");
            k.Box(-0.14, 0.16, -0.17, 0.14, 0.44, -0.16, "#8a8d82", "worn paint");
            foreach (var (x, y) in new[] { (-0.12, 0.18), (0.12, 0.18), (-0.12, 0.42) })
                k.Box(x - 0.008, y - 0.008, -0.16, x + 0.008, y + 0.008, -0.15, Chrome, "steel");
            Tube(k, 1, -0.07, -0.21, 0.45, 0.6, 0.02, Steel, "steel");
            Tube(k, 1, 0.07, -0.21, 0.0, 0.15, 0.02, Steel, "steel");
            foreach (var (dz, c) in new[] { (0.0, Rubber), (0.02, Signal), (0.04, Blue) })
                CableZ(k, -0.2 + dz, 0.15, 0.3, 0.2, 0.05, -0.05 + dz, c);
            k.Box(0.14, 0.26, -0.215, 0.18, 0.31, -0.155, Tape, "fabric");
        });
        yield return F("Breaker Panel", "a wall breaker panel with its door hanging open, rows of old breakers with hand-written tape labels, one gap blanked with a square of tape and a burn mark above a tripped one.", [-0.3, 0, -0.25], [0.3, 1.0, 0.15], k =>
        {
            k.Box(-0.25, 0.3, -0.25, 0.25, 0.95, -0.15, "#8a8d82", "rusty paint");
            k.Box(-0.23, 0.32, -0.15, 0.23, 0.93, -0.145, Grime, "dirty steel");
            for (var r = 0; r < 4; r++)
                for (var c = 0; c < 6; c++)
                {
                    if (r == 2 && c == 3)
                    {
                        k.Box(-0.2 + c * 0.068, 0.38 + r * 0.13, -0.145, -0.15 + c * 0.068, 0.45 + r * 0.13, -0.137, Tape, "fabric");
                        continue;
                    }
                    k.Box(-0.2 + c * 0.068, 0.38 + r * 0.13, -0.145, -0.15 + c * 0.068, 0.45 + r * 0.13, -0.125, Rubber, "plastic");
                    k.Box(-0.185 + c * 0.068, (r * 6 + c) % 7 == 0 ? 0.39 + r * 0.13 : 0.42 + r * 0.13, -0.125, -0.165 + c * 0.068, (r * 6 + c) % 7 == 0 ? 0.41 + r * 0.13 : 0.44 + r * 0.13, -0.11, Ceramic, "plastic");
                }
            for (var r = 0; r < 4; r++)
                k.Box(-0.21, 0.355 + r * 0.13, -0.145, 0.21, 0.37 + r * 0.13, -0.138, Tape, "fabric");
            k.Hull([[-0.08, 0.6, -0.144], [0.02, 0.6, -0.144], [-0.03, 0.72, -0.144], [-0.08, 0.6, -0.136], [0.02, 0.6, -0.136], [-0.03, 0.72, -0.136]], "#2b2622", "soot");
            k.Box(0.25, 0.3, -0.15, 0.27, 0.95, 0.15, "#8a8d82", "rusty paint"); // (the open door)
            k.Box(0.27, 0.6, 0.08, 0.29, 0.68, 0.12, Chrome, "chrome");
        });
        yield return F("Transformer", "an oil-filled transformer like a pole can, fin radiators down two sides, porcelain bushings on the lid, an oil stain down the front and a ring of rust round its base.", [-0.35, 0, -0.35], [0.35, 1.1, 0.35], k =>
        {
            k.Box(-0.3, 0.0, -0.3, 0.3, 0.06, 0.3, Grime, "rust");
            k.Hull(Frustum(1, 0, 0, 0.06, 0.24, 0.85, 0.24, 10), "#6f7a6c", "rusty paint");
            k.Hull(Frustum(1, 0, 0, 0.85, 0.26, 0.9, 0.2, 10), Grime, "dirty steel");
            foreach (var sx in new[] { -1.0, 1.0 })
                for (var i = 0; i < 4; i++)
                    k.Box(sx * 0.22, 0.15, -0.16 + i * 0.1, sx * 0.34, 0.75, -0.14 + i * 0.1, "#6f7a6c", "rusty paint");
            foreach (var x in new[] { -0.1, 0.1 })
                for (var s = 0; s < 3; s++)
                    k.Hull(Frustum(1, x, 0, 0.9 + s * 0.05, 0.05, 0.93 + s * 0.05, 0.032, 8), Ceramic, "gloss paint");
            StreakZ(k, 0.05, 0.1, 0.85, 0.235, 0.06);
            Stencil(k, -0.12, 0.6, 0.0, 0.7, 0.235, Ceramic);
        });
        yield return F("Cable Reel", "a big wooden-flanged cable drum on a bent steel stand, a few turns of heavy cable left on it, the end taped and a crank handle.", [-0.45, 0, -0.35], [0.45, 0.9, 0.35], wall: false, draw: k =>
        {
            foreach (var sx in new[] { -1.0, 1.0 })
            {
                k.Hull([[sx * 0.4, 0, -0.3], [sx * 0.43, 0, -0.3], [sx * 0.4, 0, 0.3], [sx * 0.43, 0, 0.3], [sx * 0.4, 0.5, -0.03], [sx * 0.43, 0.5, -0.03], [sx * 0.4, 0.5, 0.03], [sx * 0.43, 0.5, 0.03]], Steel, "rusty paint");
                k.Hull(Frustum(0, 0.45, 0, sx * 0.3, 0.38, sx * 0.34, 0.38, 12), Wood, "wood");
            }
            k.Hull(Frustum(0, 0.45, 0, -0.3, 0.2, 0.3, 0.2, 12), Grime, "dirty steel");
            k.Hull(Frustum(0, 0.45, 0, -0.29, 0.27, 0.29, 0.27, 12), Rubber, "rubber");
            Tube(k, 0, 0.45, 0.0, -0.45, 0.45, 0.02, Chrome, "steel");
            k.Box(0.45, 0.35, -0.02, 0.48, 0.48, 0.02, Chrome, "steel");
            Tube(k, 0, 0.36, 0.0, 0.48, 0.56, 0.015, Brown, "plastic", 6);
            Pipe(k, Rubber, 0.025, "rubber", (0.1, 0.18, 0.25), (0.1, 0.02, 0.25), (0.1, 0.02, 0.33));
            k.Box(0.07, 0.0, 0.3, 0.13, 0.05, 0.35, Tape, "fabric");
        });
        yield return F("Fuse Board", "an old board of ceramic fuse carriers on a wooden backing gone dark, a knife switch with a red handle, spare fuse wire wound round a nail and a candle stub on the ledge.", [-0.3, 0, -0.25], [0.3, 0.9, -0.08], k =>
        {
            k.Box(-0.28, 0.25, -0.25, 0.28, 0.85, -0.22, WoodDark, "wood");
            for (var i = 0; i < 5; i++)
            {
                k.Box(-0.22 + i * 0.09, 0.58, -0.22, -0.16 + i * 0.09, 0.75, -0.18, Ceramic, "gloss paint");
                k.Box(-0.21 + i * 0.09, 0.62, -0.18, -0.17 + i * 0.09, 0.71, -0.165, Brass, "brass");
            }
            k.Box(-0.15, 0.3, -0.22, 0.15, 0.36, -0.19, Ceramic, "gloss paint");
            k.Hull([[-0.012, 0.33, -0.19], [0.012, 0.33, -0.19], [-0.012, 0.345, -0.19], [0.012, 0.345, -0.19], [-0.01, 0.5, -0.13], [0.01, 0.5, -0.13], [-0.01, 0.51, -0.14], [0.01, 0.51, -0.14]], Brass, "brass");
            k.Hull(Frustum(2, 0, 0.5, -0.15, 0.018, -0.11, 0.018, 6), Signal, "plastic");
            Tube(k, 2, 0.22, 0.42, -0.22, -0.16, 0.005, Steel, "steel", 4);
            k.Hull(Frustum(2, 0.22, 0.42, -0.2, 0.02, -0.18, 0.02, 6), Copper, "copper");
            k.Box(-0.28, 0.22, -0.22, 0.28, 0.25, -0.1, WoodDark, "wood");
            k.Hull(Frustum(1, -0.2, -0.15, 0.25, 0.02, 0.29, 0.018, 6), Ceramic, "plastic");
        });
        yield return M("Power Conditioner", "a stack of three boxes of different makes wired in a chain, a mains filter with a dial, a fan grille on the side and every lead held down with tape.", 1, 1.1, ["Data 1"], (k, h) =>
        {
            Cab(k, -0.34, 0.08, -0.3, 0.34, 0.45, 0.3, "#5d6670");
            Cab(k, -0.3, 0.45, -0.26, 0.26, 0.78, 0.26, Cream);
            Cab(k, -0.22, 0.78, -0.2, 0.22, 1.02, 0.2, Avocado, "rusty paint");
            DialZ(k, -0.1, 0.62, 0.26, 0.05, 0.6);
            PilotZ(k, 0.12, 0.62, 0.26, PilotGreen);
            LouvreZ(k, -0.28, 0.28, 0.15, 0.38, 0.3, 5, "#5d6670");
            CableZ(k, 0.31, 0.2, 0.92, 0.3, 0.55, 0.05);
            CableZ(k, 0.33, 0.25, 0.6, 0.32, 0.3, 0.05, Signal);
            k.Box(0.18, 0.4, 0.3, 0.3, 0.45, 0.335, Tape, "fabric");
        });

        // ===== Fuel and fluids =====
        yield return M("Fuel Tank Banded", "a dented upright fuel tank welded from rings of different steel, a sight glass tube up the side, a hand pump on top, a drip pan and a chalked level line.", 1, 1.35, ["Fuel A"], (k, h) =>
        {
            string[] rings = [Mustard, "#8a7a52", Mustard, Tan, Mustard];
            for (var i = 0; i < 5; i++)
                k.Hull(Frustum(1, 0, 0, 0.14 + i * 0.2, 0.3 - (i % 2) * 0.006, 0.34 + i * 0.2, 0.3 - (i % 2) * 0.006, 10), rings[i], i == 3 ? "rust" : "worn paint");
            k.Hull(Frustum(1, 0, 0, 1.14, 0.3, 1.24, 0.18, 10), Mustard, "worn paint");
            k.Box(-0.36, 0.08, -0.36, 0.36, 0.14, 0.36, Grime, "soot");
            Tube(k, 1, 0.31, 0.12, 0.2, 1.05, 0.016, "#a9c2b0", "plastic", 6); // (sight glass)
            k.Box(0.29, 0.62, 0.1, 0.33, 0.64, 0.14, Ceramic, "plastic");
            Tube(k, 1, -0.1, 0.0, 1.24, 1.35, 0.025, Steel, "steel");
            k.Hull([[-0.1, 1.33, -0.012], [-0.1, 1.35, -0.012], [-0.1, 1.33, 0.012], [-0.1, 1.35, 0.012], [0.15, 1.38, -0.012], [0.15, 1.4, -0.012], [0.15, 1.38, 0.012], [0.15, 1.4, 0.012]], Signal, "worn paint");
            k.Hull([[0.1, 0.45, 0.28], [0.22, 0.4, 0.2], [0.2, 0.55, 0.2], [0.08, 0.57, 0.28], [0.15, 0.49, 0.25]], Rust, "rust");
            Pipe(k, Rubber, 0.018, "rubber", (0.28, 0.2, -0.15), (0.35, 0.2, -0.15), (0.35, 0.12, -0.15));
        });
        yield return M("Fuel Tank 2x2", "two lying tanks of different sizes stacked in a welded saddle frame, joined by a cross-pipe manifold with three valve wheels, a rotary hand pump and a drip can under the drain.", 2, 1.5, ["Fuel A", "Coolant A"], (k, h) =>
        {
            TankLie(k, 0.45, -0.1, -0.8, 0.8, 0.34, Mustard);
            TankLie(k, 1.08, -0.1, -0.7, 0.55, 0.28, Avocado, "rusty paint");
            foreach (var x in new[] { -0.55, 0.0, 0.5 })
            {
                k.Box(x - 0.04, 0.08, -0.5, x + 0.04, 0.2, 0.3, Grime, "rusty paint");
                k.Box(x - 0.03, 0.75, -0.42, x + 0.03, 0.82, 0.22, Grime, "rusty paint");
            }
            Pipe(k, Steel, 0.03, "steel", (0.7, 0.45, 0.24), (0.7, 1.08, 0.24), (0.4, 1.08, 0.24));
            Pipe(k, Steel, 0.03, "steel", (0.7, 0.45, 0.24), (0.7, 0.25, 0.24), (0.7, 0.25, 0.5), (0.7, 0.1, 0.5));
            foreach (var y in new[] { 0.35, 0.7, 1.0 })
                Wheel(k, 2, 0.7, y, 0.3, 0.07);
            k.Box(-0.7, 0.08, 0.35, -0.4, 0.3, 0.6, Signal, "rusty paint"); // (the pump body)
            Tube(k, 1, -0.55, 0.48, 0.3, 0.6, 0.02, Chrome, "steel");
            k.Box(-0.56, 0.58, 0.46, -0.3, 0.61, 0.5, Chrome, "steel");
            k.Hull(Frustum(1, 0.0, 0.5, 0.08, 0.08, 0.22, 0.08, 8), "#8f8a7a", "rusty paint");
        });
        yield return F("Fuel Pump", "a small electric pump and motor bolted to a skid of angle iron, an inlet and outlet pipe with unions, a pressure dial with its glass cracked, oil weeping from the seal.", [-0.45, 0, -0.3], [0.45, 0.6, 0.3], wall: false, draw: k =>
        {
            k.Box(-0.42, 0.0, -0.25, 0.42, 0.05, -0.2, Steel, "rusty paint");
            k.Box(-0.42, 0.003, 0.2, 0.42, 0.053, 0.25, Steel, "rusty paint");
            k.Box(-0.38, 0.05, -0.22, 0.38, 0.065, 0.22, Grime, "soot");
            k.Hull(Frustum(0, 0.24, 0.0, -0.36, 0.15, 0.0, 0.15, 10), "#4f6a7a", "worn paint"); // (motor)
            for (var i = 0; i < 5; i++)
                k.Hull(Frustum(0, 0.24, 0.0, -0.33 + i * 0.07, 0.16, -0.31 + i * 0.07, 0.16, 10), "#4f6a7a", "worn paint");
            k.Hull(Frustum(0, 0.24, 0.0, 0.0, 0.11, 0.06, 0.11, 8), Grime, "steel");
            k.Hull(Frustum(0, 0.24, 0.0, 0.06, 0.13, 0.26, 0.13, 10), Signal, "rusty paint"); // (pump)
            Pipe(k, Steel, 0.03, "steel", (0.26, 0.24, 0.0), (0.42, 0.24, 0.0));
            Pipe(k, Steel, 0.025, "steel", (0.16, 0.37, 0.0), (0.16, 0.52, 0.0), (0.16, 0.52, -0.28));
            DialZ(k, 0.16, 0.52, 0.03, 0.045, 0.85);
            k.Hull([[0.03, 0.12, 0.13], [0.09, 0.12, 0.13], [0.06, 0.07, 0.15], [0.03, 0.12, 0.145], [0.09, 0.12, 0.145]], "#2b2622", "soot");
        });
        yield return F("Fuel Filter", "two canister filters on a wall bracket, a bypass loop with a lever valve, a catch jar half full of brown sludge and a date scrawled on the newer filter in marker.", [-0.3, 0, -0.25], [0.3, 0.9, -0.05], k =>
        {
            k.Box(-0.28, 0.6, -0.25, 0.28, 0.66, -0.2, Steel, "rusty paint");
            foreach (var (x, c) in new[] { (-0.12, Steel), (0.12, Cream) })
            {
                k.Box(x - 0.07, 0.6, -0.2, x + 0.07, 0.68, -0.08, Grime, "steel");
                k.Hull(Frustum(1, x, -0.14, 0.32, 0.065, 0.6, 0.065, 10), c, c == Cream ? "plastic" : "rusty paint");
            }
            Stencil(k, 0.08, 0.42, 0.16, 0.48, -0.075, Rubber);
            Pipe(k, Copper, 0.015, "copper", (-0.28, 0.74, -0.14), (0.28, 0.74, -0.14));
            Pipe(k, Copper, 0.012, "copper", (-0.2, 0.74, -0.14), (-0.2, 0.82, -0.14), (0.2, 0.82, -0.14), (0.2, 0.74, -0.14));
            k.Box(-0.015, 0.82, -0.14, 0.015, 0.88, -0.08, Signal, "plastic");
            k.Hull(Frustum(1, -0.12, -0.14, 0.12, 0.05, 0.28, 0.05, 8), "#a9b4aa", "plastic"); // (catch jar)
            k.Hull(Frustum(1, -0.12, -0.14, 0.125, 0.045, 0.2, 0.045, 8), "#5a4128", "rubber");
        });
        yield return F("Water Tote", "a square cream water tote in a steel cage frame on a pallet, a tap at the bottom with a jug under it, green algae streak inside the plastic and the cage dented and wired up.", [-0.5, 0, -0.5], [0.5, 1.2, 0.5], wall: false, draw: k =>
        {
            for (var i = 0; i < 3; i++)
                k.Box(-0.5, 0.0, -0.45 + i * 0.4, 0.5, 0.12, -0.35 + i * 0.4, Wood, "wood");
            k.Box(-0.48, 0.12, -0.48, 0.48, 0.15, 0.48, Wood, "wood");
            k.Box(-0.44, 0.15, -0.44, 0.44, 1.08, 0.44, "#ddd6bf", "plastic");
            k.Box(-0.3, 0.6, 0.44, -0.1, 0.9, 0.446, "#7b8a52", "plastic"); // (algae)
            for (var i = 0; i < 4; i++)
                k.Box(-0.46, 0.15 + i * 0.3, 0.452, 0.46, 0.17 + i * 0.3, 0.47, Steel, "steel");
            for (var i = 0; i < 4; i++)
                k.Box(-0.4 + i * 0.27, 0.15, 0.454, -0.38 + i * 0.27, 1.12, 0.47, Steel, "steel");
            Cage(k, -0.47, 0.15, -0.47, 0.47, 1.15, 0.47, Steel, 0.02, "steel");
            k.Hull(Frustum(1, 0, 0, 1.08, 0.12, 1.16, 0.12, 8), Grime, "plastic");
            Pipe(k, Grime, 0.03, "plastic", (0.0, 0.25, 0.44), (0.0, 0.25, 0.56));
            k.Box(-0.015, 0.27, 0.5, 0.015, 0.32, 0.53, Mustard, "plastic");
        });
        yield return M("Water Recycler", "a home-made still: a boiler pot with a coiled copper condenser, a glass collection jar on a shelf, a thermometer stuck through the lid with tape and a drip tray.", 1, 1.4, ["Data 1"], (k, h) =>
        {
            TankUp(k, -0.12, -0.05, 0.08, 0.7, 0.24, Steel, "dirty steel");
            Pipe(k, Copper, 0.018, "copper", (-0.12, 0.7, -0.05), (-0.12, 1.2, -0.05), (0.2, 1.2, -0.05));
            for (var i = 0; i < 5; i++)
                Ring(k, 1, 0.2, -0.05, 1.12 - i * 0.1, 0.1, 0.02, Copper, "copper");
            Pipe(k, Copper, 0.012, "copper", (0.2, 0.68, -0.05), (0.2, 0.55, -0.05), (0.2, 0.55, 0.2));
            k.Box(0.05, 0.4, 0.1, 0.36, 0.43, 0.36, Steel, "rusty paint");
            k.Hull(Frustum(1, 0.2, 0.25, 0.43, 0.07, 0.6, 0.06, 8), "#b8c8c2", "plastic");
            Tube(k, 1, -0.2, 0.05, 0.7, 0.95, 0.008, Ceramic, "plastic", 4);
            k.Box(-0.22, 0.7, 0.03, -0.18, 0.74, 0.07, Tape, "fabric");
            k.Box(-0.36, 0.08, -0.36, 0.36, 0.12, 0.36, Grime, "soot");
        });
        yield return F("Valve Manifold", "a run of five pipes along the wall, each with a valve wheel of a different colour and a paper tag on string, one wheel replaced with a spanner jammed on the spindle.", [-0.6, 0, -0.25], [0.6, 1.2, 0.05], k =>
        {
            Pipe(k, Steel, 0.04, "rusty paint", (-0.6, 0.5, -0.18), (0.6, 0.5, -0.18));
            string[] cols = ["#9b4a30", Blue, Avocado, Mustard, Signal];
            for (var i = 0; i < 5; i++)
            {
                var x = -0.44 + i * 0.22;
                Pipe(k, Steel, 0.025, "steel", (x, 0.5, -0.18), (x, 1.05, -0.18));
                k.Box(x - 0.045, 0.7, -0.22, x + 0.045, 0.8, -0.14, Brass, "brass");
                if (i == 3)
                {
                    k.Box(x - 0.008, 0.73, -0.14, x + 0.008, 0.77, -0.06, Chrome, "steel");
                    k.Box(x - 0.012, 0.75, -0.08, x + 0.11, 0.77, -0.06, Chrome, "steel"); // (the spanner)
                }
                else
                {
                    Tube(k, 2, x, 0.75, -0.14, -0.09, 0.008, Chrome, "steel", 4);
                    Wheel(k, 2, x, 0.75, -0.08, 0.07, cols[i]);
                }
                k.Box(x + 0.03, 0.58, -0.141, x + 0.08, 0.65, -0.13, Ceramic, "plastic");
            }
            foreach (var x in new[] { -0.5, 0.5 })
                k.Box(x - 0.03, 0.45, -0.25, x + 0.03, 0.55, -0.22, Grime, "steel");
        });
        yield return F("Pressure Regulator", "a cast regulator body on a pipe stub, two dials of different sizes, a vent with a scrap of rag tied over it and a lead seal hanging off a wire.", [-0.25, 0, -0.25], [0.25, 0.7, 0.0], k =>
        {
            Pipe(k, Steel, 0.03, "steel", (0.0, 0.0, -0.18), (0.0, 0.3, -0.18), (0.0, 0.3, -0.25));
            k.Hull(Frustum(1, 0, -0.14, 0.3, 0.08, 0.42, 0.09, 8), "#6f7a6c", "rusty paint");
            k.Hull(Frustum(1, 0, -0.14, 0.42, 0.05, 0.58, 0.035, 8), "#6f7a6c", "rusty paint");
            DialZ(k, -0.13, 0.42, -0.08, 0.05, 0.7);
            DialZ(k, 0.12, 0.4, -0.08, 0.035, 0.2);
            Pipe(k, Steel, 0.012, "steel", (-0.08, 0.38, -0.14), (-0.13, 0.38, -0.14), (-0.13, 0.42, -0.14), (-0.13, 0.42, -0.08));
            k.Box(0.05, 0.55, -0.16, 0.12, 0.62, -0.1, Orange, "fabric");
            k.Hull(Frustum(2, -0.05, 0.25, -0.1, 0.012, -0.088, 0.012, 6), "#77787a", "steel");
        });
        yield return F("Coolant Pump", "a belt-driven pump: motor and pump side by side on a plate, a fan belt with no guard, a coolant hose clamped on with wire, green coolant crusted round the gland.", [-0.45, 0, -0.3], [0.45, 0.65, 0.3], wall: false, draw: k =>
        {
            k.Box(-0.42, 0.0, -0.28, 0.42, 0.05, 0.28, Steel, "rusty paint");
            k.Hull(Frustum(0, 0.22, -0.1, -0.38, 0.13, -0.06, 0.13, 10), Avocado, "rusty paint");
            k.Hull(Frustum(2, 0.15, 0.25, 0.1, 0.12, 0.2, 0.12, 10), Blue, "worn paint");
            k.Hull(Frustum(0, 0.22, -0.1, -0.06, 0.05, 0.02, 0.05, 8), Grime, "steel");
            k.Hull(Frustum(0, 0.25, 0.15, -0.04, 0.07, 0.0, 0.07, 8), Grime, "steel");
            k.Hull([[0.004, 0.18, -0.1], [0.024, 0.18, -0.1], [0.004, 0.26, -0.1], [0.024, 0.26, -0.1], [0.004, 0.2, 0.15], [0.024, 0.2, 0.15], [0.004, 0.31, 0.15], [0.024, 0.31, 0.15]], Rubber, "rubber");
            Pipe(k, Rubber, 0.03, "rubber", (0.15, 0.25, 0.2), (0.15, 0.25, 0.3));
            Pipe(k, Rubber, 0.03, "rubber", (0.15, 0.37, 0.15), (0.15, 0.55, 0.15), (0.42, 0.55, 0.15));
            k.Hull(Frustum(2, 0.15, 0.25, 0.2, 0.06, 0.215, 0.06, 8), "#7fa86a", "plastic");
        });
        yield return M("Heat Exchanger", "a shell-and-tube exchanger lying on two saddles, flanged heads bolted on with mismatched bolts, lagging half torn off and taped, and a thermometer pocket on top.", 1, 1.0, ["Data 1", "Fuel A"], (k, h) =>
        {
            TankLie(k, 0.45, 0.0, -0.36, 0.36, 0.25, "#6b7682", "rusty paint");
            k.Hull(Frustum(0, 0.45, 0.0, -0.3, 0.26, 0.06, 0.26, 10), Lagging, "lagging");
            k.Box(-0.2, 0.68, -0.05, -0.1, 0.72, 0.05, Tape, "fabric");
            foreach (var x in new[] { -0.36, 0.36 })
            {
                k.Hull(Frustum(0, 0.45, 0.0, x - 0.02, 0.28, x + 0.02, 0.28, 10), Grime, "steel");
                for (var i = 0; i < 6; i++)
                {
                    var a = i * Math.PI / 3;
                    k.Box(x - 0.03, 0.45 + 0.25 * Math.Sin(a) - 0.013, 0.25 * Math.Cos(a) - 0.013, x + 0.03, 0.45 + 0.25 * Math.Sin(a) + 0.013, 0.25 * Math.Cos(a) + 0.013, i == 4 ? Brass : Chrome, "steel");
                }
            }
            foreach (var x in new[] { -0.2, 0.2 })
                k.Box(x - 0.04, 0.08, -0.22, x + 0.04, 0.25, 0.22, Grime, "rusty paint");
            Tube(k, 1, 0.15, 0.0, 0.68, 0.82, 0.015, Brass, "brass");
            Pipe(k, Steel, 0.03, "steel", (-0.25, 0.68, 0.0), (-0.25, 0.85, 0.0), (-0.25, 0.85, 0.3));
        });
        yield return F("Expansion Tank", "an upright coolant expansion tank on legs with a sight tube, a pressure cap with a warning in faded red, and a drip of green dried down one leg.", [-0.25, 0, -0.25], [0.25, 1.3, 0.25], wall: false, draw: k =>
        {
            foreach (var (x, z) in new[] { (-0.15, -0.15), (0.15, -0.15), (-0.15, 0.15), (0.15, 0.15) })
                k.Box(x - 0.02, 0.0, z - 0.02, x + 0.02, 0.4, z + 0.02, Steel, "rusty paint");
            TankUp(k, 0, 0, 0.4, 1.15, 0.2, Teal);
            k.Hull(Frustum(1, 0, 0, 1.15, 0.06, 1.22, 0.06, 8), Signal, "worn paint");
            Tube(k, 1, 0.21, 0.08, 0.5, 1.05, 0.012, "#a9c2b0", "plastic", 6);
            k.Box(0.13, 0.02, 0.13, 0.17, 0.35, 0.17, "#7fa86a", "plastic");
            Pipe(k, Steel, 0.025, "steel", (0.0, 0.4, 0.0), (0.0, 0.1, 0.0), (0.25, 0.1, 0.0));
        });
        yield return F("Pipe Tee", "a big flanged tee where three pipes meet, the gasket weeping, a bucket under it catching the drips and a rag wrapped round the joint with wire.", [-0.5, 0, -0.3], [0.5, 0.9, 0.3], wall: false, draw: k =>
        {
            Tube(k, 0, 0.55, 0.0, -0.5, 0.5, 0.07, Steel, "rusty paint", 8);
            Tube(k, 1, 0.0, 0.0, 0.55, 0.9, 0.07, Steel, "rusty paint", 8);
            foreach (var x in new[] { -0.2, 0.2 })
                k.Hull(Frustum(0, 0.55, 0.0, x - 0.02, 0.11, x + 0.02, 0.11, 8), Grime, "steel");
            k.Hull(Frustum(1, 0.0, 0.0, 0.7, 0.11, 0.74, 0.11, 8), Grime, "steel");
            k.Hull(Frustum(0, 0.55, 0.0, -0.08, 0.085, 0.08, 0.085, 8), Orange, "fabric");
            k.Hull(Frustum(1, 0, 0.05, 0.0, 0.12, 0.25, 0.14, 8), "#8f8a7a", "rusty paint");
            foreach (var x in new[] { -0.45, 0.45 })
                k.Box(x - 0.04, 0.0, -0.1, x + 0.04, 0.48, 0.1, Grime, "rusty paint");
        });

        // ===== Air =====
        yield return M("Life Support 2x2", "a big scrubber box from two cooler units welded together, two fan grilles on top, a duct elbow taped at the seam, a rack of spare filters on the side and a condensate hose to a bucket.", 2, 1.3, ["Data 1", "Coolant A"], (k, h) =>
        {
            Cab(k, -0.8, 0.08, -0.6, 0.0, 1.1, 0.6, Cream);
            Cab(k, 0.0, 0.08, -0.6, 0.78, 1.0, 0.6, Beige, "rusty paint");
            k.Box(-0.02, 0.15, 0.6, 0.02, 1.0, 0.62, Tape, "fabric");
            LouvreZ(k, -0.7, -0.1, 0.3, 0.9, 0.6, 7, Beige);
            LouvreZ(k, 0.1, 0.68, 0.3, 0.8, 0.6, 6, Cream);
            StripesZ(k, -0.8, 0.0, 0.16, 0.606);
            foreach (var x in new[] { -0.4, 0.39 })
            {
                var y = x < 0 ? 1.1 : 1.0;
                k.Hull(Frustum(1, x, 0, y, 0.25, y + 0.04, 0.25, 12), Grime, "dirty steel");
                for (var i = 0; i < 4; i++)
                    k.Box(x - 0.2 + i * 0.13, y + 0.04, -0.2, x - 0.19 + i * 0.13, y + 0.055, 0.2, Chrome, "steel");
            }
            Pipe(k, Steel, 0.12, "dirty steel", (-0.4, 1.15, -0.4), (-0.4, 1.3, -0.4), (0.4, 1.3, -0.4));
            for (var i = 0; i < 4; i++)
                k.Box(0.78, 0.2 + i * 0.18, -0.4, 0.84, 0.35 + i * 0.18, 0.3, "#c9c0a0", "fabric");
            Pipe(k, Rubber, 0.015, "rubber", (0.6, 0.2, 0.6), (0.6, 0.2, 0.75));
            k.Hull(Frustum(1, 0.6, 0.78, 0.08, 0.08, 0.28, 0.09, 8), "#8f8a7a", "rusty paint");
        });
        yield return F("CO2 Scrubber", "a wall rack of five lime canisters, one spent and chalked with an X, a little fan blowing through a duct that ties them together, and a hand-drawn schedule taped beside it.", [-0.45, 0, -0.25], [0.45, 1.2, 0.0], k =>
        {
            k.Box(-0.42, 0.3, -0.25, 0.42, 0.35, -0.05, Steel, "rusty paint");
            k.Box(-0.42, 0.95, -0.25, 0.42, 1.0, -0.05, Steel, "rusty paint");
            for (var i = 0; i < 5; i++)
            {
                var x = -0.34 + i * 0.17;
                k.Hull(Frustum(1, x, -0.15, 0.35, 0.065, 0.95, 0.065, 8), i == 2 ? "#8a8a80" : Cream, "worn paint");
                if (i == 2)
                {
                    k.Hull([[x - 0.04, 0.6, -0.085], [x - 0.03, 0.6, -0.085], [x + 0.04, 0.7, -0.085], [x + 0.03, 0.7, -0.085], [x - 0.04, 0.6, -0.078], [x + 0.04, 0.7, -0.078]], Ceramic, "plastic");
                    k.Hull([[x + 0.04, 0.6, -0.088], [x + 0.03, 0.6, -0.088], [x - 0.04, 0.7, -0.088], [x - 0.03, 0.7, -0.088], [x + 0.04, 0.6, -0.075], [x - 0.04, 0.7, -0.075]], Ceramic, "plastic");
                }
            }
            Pipe(k, Steel, 0.05, "dirty steel", (-0.42, 1.08, -0.15), (0.42, 1.08, -0.15));
            k.Hull(Frustum(0, 1.08, -0.15, 0.42, 0.08, 0.47, 0.08, 10), Grime, "steel");
            k.Box(-0.42, 0.45, -0.25, -0.44, 0.75, -0.24, Ceramic, "plastic");
            k.Box(-0.25, 0.12, -0.25, 0.05, 0.28, -0.235, Ceramic, "plastic");
        });
        yield return F("Air Vent", "a wall vent grille in a duct collar, its paint gone, a strip of ribbon tied to the grille to show it is blowing, and dust streaked down the wall below it.", [-0.25, 0, -0.25], [0.25, 0.6, -0.1], k =>
        {
            k.Box(-0.22, 0.25, -0.25, 0.22, 0.55, -0.16, Steel, "dirty steel");
            for (var i = 0; i < 6; i++)
                k.Box(-0.2, 0.27 + i * 0.045, -0.16, 0.2, 0.29 + i * 0.045, -0.13, Steel, "steel");
            k.Hull([[0.05, 0.48, -0.13], [0.08, 0.48, -0.13], [0.15, 0.38, -0.1], [0.18, 0.38, -0.1], [0.05, 0.48, -0.12], [0.08, 0.48, -0.12], [0.15, 0.38, -0.09], [0.18, 0.38, -0.09]], Signal, "fabric");
            k.Hull([[-0.18, 0.25, -0.16], [0.18, 0.25, -0.16], [-0.1, 0.02, -0.16], [0.1, 0.02, -0.16], [-0.18, 0.25, -0.155], [0.18, 0.25, -0.155], [-0.1, 0.02, -0.155], [0.1, 0.02, -0.155]], "#4a4540", "dirty steel");
        });
        yield return F("Wall Fan", "a caged desk fan bolted to a wall bracket, its guard bent in at one side, a blade missing so it wobbles, and the flex looped up on a hook.", [-0.25, 0, -0.25], [0.25, 0.6, 0.2], k =>
        {
            k.Box(-0.06, 0.25, -0.25, 0.06, 0.37, -0.22, Grime, "steel");
            Tube(k, 2, 0.0, 0.31, -0.22, -0.05, 0.015, Steel, "steel");
            k.Hull(Frustum(2, 0.0, 0.31, -0.08, 0.06, 0.02, 0.07, 10), Teal, "worn paint");
            Ring(k, 2, 0.0, 0.31, 0.08, 0.17, 0.012, Chrome, "steel", 8);
            Ring(k, 2, 0.0, 0.31, 0.15, 0.12, 0.01, Chrome, "steel", 8);
            for (var i = 0; i < 3; i++)
            {
                var a = i * 2 * Math.PI / 3;
                k.Hull([[0, 0.31, 0.06], [0.14 * Math.Cos(a) - 0.04 * Math.Sin(a), 0.31 + 0.14 * Math.Sin(a) + 0.04 * Math.Cos(a), 0.09], [0.14 * Math.Cos(a) + 0.04 * Math.Sin(a), 0.31 + 0.14 * Math.Sin(a) - 0.04 * Math.Cos(a), 0.08], [0, 0.31, 0.08]], Cream, "plastic");
            }
            k.Hull(Frustum(2, 0.0, 0.31, 0.04, 0.025, 0.1, 0.02, 8), Grime, "plastic");
            CableZ(k, -0.22, 0.0, 0.25, 0.15, 0.4, 0.15);
        });
        yield return M("Oxygen Generator", "electrolysis cells in four big glass jars on a shelf, bubbling tubes into a gas bottle, a car battery charger feeding them and a crocodile-clip lead for each jar.", 1, 1.25, ["Data 1"], (k, h) =>
        {
            k.Box(-0.36, 0.08, -0.36, 0.36, 0.12, 0.36, Steel, "rusty paint");
            foreach (var (x, z) in new[] { (-0.18, -0.18), (0.18, -0.18), (-0.18, 0.18), (0.18, 0.18) })
            {
                k.Hull(Frustum(1, x, z, 0.12, 0.13, 0.55, 0.13, 8), "#b8c8c2", "plastic");
                k.Hull(Frustum(1, x, z, 0.55, 0.09, 0.6, 0.09, 8), Grime, "rubber");
                Pipe(k, Ceramic, 0.008, "plastic", (x, 0.6, z), (x, 0.8, z), (0.0, 0.8, z));
            }
            TankUp(k, 0.0, 0.0, 0.62, 1.2, 0.1, BottleGreen);
            k.Box(-0.34, 0.6, 0.28, -0.12, 0.75, 0.36, Signal, "worn paint");
            DialZ(k, -0.23, 0.68, 0.36, 0.035, 0.6);
            CableZ(k, 0.32, -0.3, 0.6, 0.2, 0.58, 0.08, Signal);
        });
        yield return F("Air Filter Box", "a wall box of stacked filter frames with its front left off, one filter clean and the rest grey, a pressure tell-tale on a stick and the screws kept in a tin on top.", [-0.35, 0, -0.25], [0.35, 1.1, 0.05], k =>
        {
            k.Box(-0.33, 0.4, -0.25, 0.33, 1.05, -0.02, "#8a8d82", "rusty paint");
            for (var i = 0; i < 4; i++)
                k.Box(-0.3, 0.44 + i * 0.15, -0.22, 0.3, 0.56 + i * 0.15, 0.0, i == 1 ? "#e3dcc8" : "#77756e", "fabric");
            Tube(k, 1, 0.25, 0.02, 1.05, 1.18, 0.006, Steel, "steel", 4);
            k.Box(0.2, 1.17, 0.0, 0.3, 1.2, 0.04, Signal, "plastic");
            k.Hull(Frustum(1, -0.15, -0.12, 1.05, 0.05, 1.1, 0.05, 8), Steel, "rusty paint");
        });
        yield return F("Dehumidifier", "a boxy old dehumidifier on castors, its plastic yellowed, a bucket drawer pulled half out and full, a sticker of somewhere sunny and a dial knob gone missing replaced by a bolt.", [-0.25, 0, -0.2], [0.25, 0.75, 0.25], wall: false, draw: k =>
        {
            foreach (var (x, z) in new[] { (-0.18, -0.14), (0.18, -0.14), (-0.18, 0.14), (0.18, 0.14) })
                k.Hull(Frustum(0, 0.03, z, x - 0.015, 0.03, x + 0.015, 0.03, 6), Rubber, "rubber");
            k.Box(-0.23, 0.06, -0.18, 0.23, 0.72, 0.18, "#d8cfa8", "plastic");
            LouvreZ(k, -0.18, 0.18, 0.5, 0.68, 0.18, 4, "#c9bf95");
            k.Box(-0.2, 0.1, 0.13, 0.2, 0.35, 0.24, "#b5bcc2", "plastic");
            k.Box(-0.18, 0.12, 0.15, 0.18, 0.3, 0.239, "#7f9eb0", "plastic");
            k.Box(0.08, 0.42, 0.18, 0.12, 0.46, 0.2, Chrome, "steel");
            k.Box(-0.18, 0.4, 0.18, -0.06, 0.47, 0.19, "#4fa0b8", "plastic");
        });
        yield return F("Bar Heater", "a two-bar electric heater with a dented chrome reflector, one bar glowing dull orange and the other dead, a wire guard pushed in and socks drying on top.", [-0.3, 0, -0.15], [0.3, 0.5, 0.2], wall: false, draw: k =>
        {
            k.Box(-0.28, 0.0, -0.12, 0.28, 0.04, 0.15, Grime, "steel");
            k.Profile(-0.25, 0.25, [(0.04, -0.12), (0.04, 0.05), (0.42, 0.0), (0.45, -0.12)], "#a0705a", "worn paint");
            k.Profile(-0.22, 0.22, [(0.07, 0.03), (0.4, -0.01), (0.4, 0.02), (0.07, 0.06)], Chrome, "chrome");
            k.Hull(Frustum(0, 0.17, 0.08, -0.2, 0.012, 0.2, 0.012, 6), "#ff8a3c", "", glow: true);
            Tube(k, 0, 0.3, 0.07, -0.2, 0.2, 0.012, "#6a5a50", "steel", 6);
            for (var i = 0; i < 5; i++)
                Tube(k, 1, -0.2 + i * 0.1, 0.12, 0.07, 0.42, 0.003, Chrome, "steel", 4);
            k.Box(-0.15, 0.45, -0.1, -0.02, 0.47, 0.0, Orange, "fabric");
            k.Box(0.03, 0.45, -0.1, 0.16, 0.47, 0.0, Blue, "fabric");
        });
        yield return F("Airlock Panel", "an airlock control panel: two big mushroom buttons, a cycle lamp, a pressure needle, a laminated instruction card held with tape and a crack across the casing glued.", [-0.22, 0, -0.25], [0.22, 0.6, -0.1], k =>
        {
            k.Box(-0.2, 0.1, -0.25, 0.2, 0.55, -0.17, Cream, "worn paint");
            k.Box(-0.2, 0.1, -0.17, 0.2, 0.14, -0.16, "#8a7a52", "rusty paint");
            k.Hull(Frustum(2, -0.09, 0.3, -0.17, 0.045, -0.12, 0.05, 8), "#4f8a4a", "plastic");
            k.Hull(Frustum(2, 0.09, 0.3, -0.17, 0.045, -0.12, 0.05, 8), Signal, "plastic");
            PilotZ(k, 0.0, 0.47, -0.17, Bulb, 0.02);
            DialZ(k, -0.12, 0.46, -0.17, 0.04, 0.3);
            k.Box(0.06, 0.4, -0.17, 0.18, 0.53, -0.162, Ceramic, "plastic");
            k.Box(0.08, 0.52, -0.162, 0.16, 0.54, -0.155, Tape, "fabric");
            k.Hull([[-0.2, 0.2, -0.17], [-0.19, 0.2, -0.17], [-0.05, 0.13, -0.17], [-0.04, 0.13, -0.17], [-0.2, 0.2, -0.166], [-0.05, 0.13, -0.166]], Rubber, "plastic");
        });
        yield return F("Gauge Board", "a plank board on the wall with four dials of different makes plumbed in by thin copper tubes, two with their glass gone and a pencil line marking the needle that matters.", [-0.4, 0, -0.25], [0.4, 1.0, -0.1], k =>
        {
            k.Box(-0.38, 0.4, -0.25, 0.38, 0.9, -0.22, "#6b5a42", "painted wood");
            (double X, double Y, double R, double N)[] dials = [(-0.25, 0.75, 0.07, 0.3), (-0.05, 0.76, 0.05, 0.7), (0.15, 0.74, 0.065, 0.5), (0.3, 0.55, 0.045, 0.9)];
            foreach (var d in dials)
            {
                DialZ(k, d.X, d.Y, -0.22, d.R, d.N);
                Pipe(k, Copper, 0.006, "copper", (d.X, d.Y - d.R, -0.2), (d.X, 0.45, -0.2), (d.X, 0.45, -0.245));
            }
            k.Box(0.14, 0.81, -0.183, 0.16, 0.83, -0.178, Rubber, "plastic");
        });

        // ===== Propulsion =====
        yield return M("Drive Core 2x2", "an old drive's guts on a 2x2 plate: a combustion chamber wrapped in lagging, a turbopump plumbed with three different pipe sizes, a bell sticking out of the side, a patched flange and soot over everything.", 2, 1.6, ["Fuel A", "Coolant A"], (k, h) =>
        {
            k.Hull(Frustum(1, -0.25, 0.0, 0.1, 0.38, 1.1, 0.38, 12), Lagging, "lagging");
            for (var i = 0; i < 3; i++)
                k.Hull(Frustum(1, -0.25, 0.0, 0.3 + i * 0.3, 0.39, 0.33 + i * 0.3, 0.39, 12), Tape, "fabric");
            k.Hull(Frustum(1, -0.25, 0.0, 1.1, 0.3, 1.3, 0.15, 12), Grime, "soot");
            k.Hull(Frustum(2, -0.25, 0.6, -0.4, 0.2, -0.85, 0.42, 12), "#4e5668", "dirty steel");
            k.Hull(Frustum(2, -0.25, 0.6, -0.84, 0.4, -0.86, 0.4, 12), Rubber, "soot");
            k.Hull(Frustum(1, 0.45, 0.35, 0.1, 0.18, 0.5, 0.18, 10), Signal, "rusty paint");
            k.Hull(Frustum(2, 0.45, 0.3, 0.17, 0.2, 0.3, 0.2, 10), Grime, "steel");
            Pipe(k, Steel, 0.05, "steel", (0.45, 0.5, 0.35), (0.45, 0.9, 0.35), (0.12, 0.9, 0.35));
            Pipe(k, Copper, 0.025, "copper", (0.62, 0.2, 0.0), (0.62, 1.2, 0.0), (-0.25, 1.2, 0.0), (-0.25, 1.32, 0.0));
            Pipe(k, Grime, 0.035, "dirty steel", (0.45, 0.3, 0.55), (0.45, 0.3, 0.8));
            k.Box(-0.65, 0.5, -0.25, -0.6, 0.8, 0.2, "#8a8a80", "steel");
        });
        yield return F("RCS Cluster", "four mismatched thruster nozzles on a mount welded up from angle iron, fuel lines looping between them and taped at the joins, one nozzle bent, soot fans on the hull plate under each.", [-0.35, 0, -0.35], [0.35, 0.5, 0.35], k =>
        {
            k.Box(-0.33, 0.0, -0.33, 0.33, 0.02, 0.33, "#8a8a80", "rusty paint");
            foreach (var (x, z) in new[] { (-0.25, 0.0), (0.25, 0.0), (0.0, -0.25), (0.0, 0.25) })
                k.Hull([[x * 0.4 - 0.03, 0.02, z * 0.4 - 0.03], [x * 0.4 + 0.03, 0.02, z * 0.4 - 0.03], [x * 0.4 - 0.03, 0.02, z * 0.4 + 0.03], [x * 0.4 + 0.03, 0.02, z * 0.4 + 0.03], [x - 0.02, 0.22, z - 0.02], [x + 0.02, 0.22, z - 0.02], [x - 0.02, 0.22, z + 0.02], [x + 0.02, 0.22, z + 0.02]], Steel, "rusty paint");
            k.Box(-0.08, 0.02, -0.08, 0.08, 0.3, 0.08, Orange, "rusty paint");
            k.Hull(Frustum(0, 0.22, 0.0, 0.25, 0.04, 0.35, 0.07, 8), Grime, "soot");
            k.Hull(Frustum(0, 0.22, 0.0, -0.25, 0.035, -0.35, 0.06, 8), "#6f6a60", "steel");
            k.Hull(Frustum(2, 0.0, 0.22, 0.25, 0.04, 0.35, 0.065, 8), Grime, "soot");
            k.Hull([[-0.03, 0.2, -0.25], [0.03, 0.2, -0.25], [-0.03, 0.24, -0.25], [0.03, 0.24, -0.25], [0.02, 0.15, -0.35], [0.1, 0.15, -0.35], [0.02, 0.22, -0.35], [0.1, 0.22, -0.35]], Grime, "soot"); // (the bent one)
            Pipe(k, Copper, 0.01, "copper", (0.0, 0.3, 0.0), (0.0, 0.4, 0.0), (0.2, 0.4, 0.0), (0.2, 0.25, 0.0));
            Pipe(k, Copper, 0.01, "copper", (0.0, 0.4, 0.0), (0.0, 0.4, 0.2), (0.0, 0.26, 0.2));
            k.Box(0.08, 0.38, -0.03, 0.12, 0.42, 0.03, Tape, "fabric");
            foreach (var (x, z) in new[] { (0.31, 0.0), (-0.31, 0.0), (0.0, 0.31) })
                k.Box(x - 0.03, 0.02, z - 0.03, x + 0.03, 0.033, z + 0.03, Rubber, "soot");
        });
        yield return F("RCS Puffer", "a single small thruster nozzle on a bracket bolted through a patch plate, a fuel line taped to the hull, a soot halo round it and an arrow painted on by hand.", [-0.2, 0, -0.2], [0.2, 0.3, 0.2], k =>
        {
            k.Box(-0.18, 0.0, -0.18, 0.18, 0.015, 0.18, "#8a8a80", "rusty paint");
            foreach (var (x, z) in new[] { (-0.15, -0.15), (0.15, -0.15), (-0.15, 0.15), (0.15, 0.15) })
                k.Box(x - 0.01, 0.015, z - 0.01, x + 0.01, 0.027, z + 0.01, Chrome, "steel");
            k.Box(-0.06, 0.015, -0.06, 0.06, 0.12, 0.06, Steel, "rusty paint");
            k.Hull(Frustum(2, 0.0, 0.08, 0.06, 0.025, 0.16, 0.045, 8), Grime, "soot");
            k.Hull(Frustum(1, 0, 0.18, 0.012, 0.09, 0.016, 0.09, 8), Rubber, "soot");
            Pipe(k, Copper, 0.008, "copper", (0.0, 0.12, 0.0), (0.0, 0.14, 0.0), (0.0, 0.14, -0.2));
            k.Box(-0.02, 0.125, -0.16, 0.02, 0.155, -0.12, Tape, "fabric");
            k.Hull([[-0.1, 0.015, 0.1], [-0.04, 0.015, 0.1], [-0.07, 0.015, 0.16], [-0.1, 0.026, 0.1], [-0.04, 0.026, 0.1], [-0.07, 0.026, 0.16]], Mustard, "worn paint");
        });
        yield return F("Gimbal Engine", "a small engine hung in a gimbal ring, two hydraulic rams to steer it (one leaking, a rag round it), a heat-blued bell and a mount frame of bent tube.", [-0.5, 0, -0.5], [0.5, 1.2, 0.5], wall: false, draw: k =>
        {
            foreach (var (x, z) in new[] { (-0.42, -0.42), (0.42, -0.42), (-0.42, 0.42), (0.42, 0.42) })
                k.Hull([[x - 0.02, 0.0, z - 0.02], [x + 0.02, 0.0, z - 0.02], [x - 0.02, 0.0, z + 0.02], [x + 0.02, 0.0, z + 0.02], [x * 0.7 - 0.02, 1.0, z * 0.7 - 0.02], [x * 0.7 + 0.02, 1.0, z * 0.7 - 0.02], [x * 0.7 - 0.02, 1.0, z * 0.7 + 0.02], [x * 0.7 + 0.02, 1.0, z * 0.7 + 0.02]], Steel, "rusty paint");
            Ring(k, 1, 0, 0, 1.0, 0.3, 0.04, Grime, "steel", 8);
            k.Hull(Frustum(1, 0, 0, 0.85, 0.14, 1.15, 0.16, 10), Steel, "dirty steel");
            k.Hull(Frustum(1, 0, 0, 0.85, 0.14, 0.6, 0.18, 10), "#5a6070", "dirty steel");
            k.Hull(Frustum(1, 0, 0, 0.6, 0.18, 0.25, 0.3, 10), "#7d6a52", "dirty steel");
            k.Hull(Frustum(1, 0, 0, 0.26, 0.28, 0.24, 0.28, 10), Rubber, "soot");
            foreach (var (x, c) in new[] { (0.22, Chrome), (-0.22, Chrome) })
                k.Hull([[x - 0.02, 0.98, 0.25], [x + 0.02, 0.98, 0.25], [x - 0.02, 1.0, 0.29], [x + 0.02, 1.0, 0.29], [x * 0.6 - 0.015, 0.7, 0.13], [x * 0.6 + 0.015, 0.7, 0.13], [x * 0.6 - 0.015, 0.72, 0.17], [x * 0.6 + 0.015, 0.72, 0.17]], c, "chrome");
            k.Hull(Frustum(1, 0.18, 0.23, 0.85, 0.03, 0.92, 0.03, 6), Orange, "fabric");
        });
        yield return F("Turbopump", "a turbopump off something bigger: a snail-shell volute, an inlet pipe bigger than the outlet, a hand-turned priming crank and bolts round every flange, two of them different.", [-0.4, 0, -0.35], [0.4, 0.8, 0.35], wall: false, draw: k =>
        {
            k.Box(-0.35, 0.0, -0.3, 0.35, 0.05, 0.3, Steel, "rusty paint");
            k.Hull(Frustum(2, 0.0, 0.38, -0.12, 0.28, 0.12, 0.28, 12), "#6f7a6c", "rusty paint");
            k.Hull(Frustum(2, 0.0, 0.38, 0.12, 0.18, 0.18, 0.15, 10), Grime, "steel");
            k.Hull([[0.15, 0.55, -0.1], [0.38, 0.55, -0.1], [0.15, 0.66, -0.1], [0.38, 0.66, -0.1], [0.15, 0.55, 0.1], [0.38, 0.55, 0.1], [0.15, 0.66, 0.1], [0.38, 0.66, 0.1]], "#6f7a6c", "rusty paint");
            Tube(k, 2, 0.0, 0.38, -0.35, -0.12, 0.11, Steel, "steel", 10);
            for (var i = 0; i < 8; i++)
            {
                var a = i * Math.PI / 4;
                k.Box(0.3 * Math.Cos(a) - 0.015, 0.38 + 0.3 * Math.Sin(a) - 0.015, -0.13, 0.3 * Math.Cos(a) + 0.015, 0.38 + 0.3 * Math.Sin(a) + 0.015, 0.13, i % 5 == 0 ? Brass : Chrome, "steel");
            }
            Tube(k, 2, 0.0, 0.38, 0.18, 0.28, 0.015, Chrome, "steel");
            k.Box(-0.015, 0.38, 0.27, 0.015, 0.5, 0.3, Chrome, "steel");
            Tube(k, 2, 0.0, 0.49, 0.3, 0.38, 0.012, Brown, "plastic", 6);
            k.Box(-0.06, 0.05, -0.06, 0.06, 0.12, 0.06, Grime, "steel");
        });
        yield return F("Lagged Feed Line", "a length of propellant line wrapped in lagging, the lagging held on with tape and wire, one section bare and frosted, and pipe clamps on two short posts.", [-0.7, 0, -0.15], [0.7, 0.45, 0.15], wall: false, draw: k =>
        {
            Tube(k, 0, 0.3, 0.0, -0.7, 0.7, 0.05, Steel, "steel", 8);
            k.Hull(Frustum(0, 0.3, 0.0, -0.65, 0.085, -0.15, 0.085, 8), Lagging, "lagging");
            k.Hull(Frustum(0, 0.3, 0.0, 0.1, 0.08, 0.6, 0.08, 8), "#a39f8a", "lagging");
            k.Hull(Frustum(0, 0.3, 0.0, -0.12, 0.056, 0.07, 0.056, 8), "#dfe8ea", "plastic");
            foreach (var x in new[] { -0.5, -0.3, 0.3, 0.45 })
                k.Hull(Frustum(0, 0.3, 0.0, x, 0.09, x + 0.04, 0.09, 8), Tape, "fabric");
            foreach (var x in new[] { -0.55, 0.55 })
            {
                k.Box(x - 0.03, 0.0, -0.06, x + 0.03, 0.22, 0.06, Grime, "rusty paint");
                k.Box(x - 0.035, 0.2, -0.1, x + 0.035, 0.24, 0.1, Steel, "steel");
            }
        });
        yield return F("Engine Relay Box", "a wall box with its mesh front, old relays and contactors visible inside, two big levers on the side with a padlock through one, and a smell-of-burning note taped on.", [-0.3, 0, -0.25], [0.35, 1.0, 0.0], k =>
        {
            k.Box(-0.28, 0.35, -0.25, 0.28, 0.95, -0.06, "#6b7682", "rusty paint");
            for (var r = 0; r < 3; r++)
                for (var c = 0; c < 4; c++)
                    k.Box(-0.22 + c * 0.11, 0.42 + r * 0.17, -0.2, -0.14 + c * 0.11, 0.54 + r * 0.17, -0.09, (r + c) % 3 == 0 ? Cream : Grime, "plastic");
            for (var i = 0; i < 7; i++)
                k.Box(-0.27, 0.37 + i * 0.085, -0.075, 0.27, 0.38 + i * 0.085, -0.06, Steel, "steel");
            for (var i = 0; i < 6; i++)
                k.Box(-0.25 + i * 0.1, 0.36, -0.074, -0.24 + i * 0.1, 0.94, -0.061, Steel, "steel");
            foreach (var y in new[] { 0.55, 0.75 })
            {
                k.Box(0.28, y - 0.03, -0.17, 0.31, y + 0.03, -0.11, Grime, "steel");
                k.Hull([[0.31, y - 0.01, -0.15], [0.31, y + 0.01, -0.15], [0.31, y - 0.01, -0.13], [0.31, y + 0.01, -0.13], [0.33, y + 0.13, -0.15], [0.33, y + 0.15, -0.15], [0.33, y + 0.13, -0.13], [0.33, y + 0.15, -0.13]], Signal, "worn paint");
            }
            k.Box(0.3, 0.62, -0.13, 0.34, 0.67, -0.12, Brass, "brass");
            k.Box(-0.2, 0.86, -0.06, -0.04, 0.93, -0.052, Mustard, "plastic");
        });
        yield return F("Blast Deflector", "an angled deflector plate on the hull, heat-blued in rainbow bands, bolted with odd bolts, one corner curled from heat and a soot fan up its face.", [-0.45, 0, -0.3], [0.45, 0.5, 0.3], k =>
        {
            k.Hull([[-0.42, 0.0, -0.28], [0.42, 0.0, -0.28], [-0.42, 0.0, 0.28], [0.42, 0.0, 0.28], [-0.42, 0.45, -0.28], [0.42, 0.45, -0.28], [-0.42, 0.42, -0.24], [0.42, 0.42, -0.24]], "#5a6070", "dirty steel");
            k.Hull([[-0.4, 0.01, 0.26], [0.4, 0.01, 0.26], [-0.4, 0.42, -0.23], [0.4, 0.42, -0.23], [-0.4, 0.025, 0.27], [0.4, 0.025, 0.27], [-0.4, 0.435, -0.22], [0.4, 0.435, -0.22]], "#7d6a52", "dirty steel");
            k.Hull([[-0.2, 0.15, 0.1], [0.2, 0.15, 0.1], [-0.05, 0.4, -0.18], [0.05, 0.4, -0.18], [-0.2, 0.165, 0.11], [0.2, 0.165, 0.11], [-0.05, 0.415, -0.17], [0.05, 0.415, -0.17]], Rubber, "soot");
            foreach (var x in new[] { -0.36, 0.0, 0.36 })
                k.Box(x - 0.015, 0.003, 0.26, x + 0.015, 0.04, 0.29, x == 0 ? Brass : Chrome, "steel");
            k.Hull([[0.36, 0.42, -0.24], [0.43, 0.42, -0.24], [0.43, 0.5, -0.2], [0.38, 0.47, -0.21], [0.4, 0.44, -0.22]], "#5a6070", "dirty steel");
        });

        // ===== Sensors and comms =====
        yield return M("Radar Dish", "a mesh radar dish rebuilt with mismatched panels on a turntable, a motor housing with its cover held on by a bungee, the feed horn on bent struts and a cable loop that wraps as it turns.", 1, 1.8, ["Data 1"], (k, h) =>
        {
            Cab(k, -0.28, 0.08, -0.28, 0.28, 0.5, 0.28, "#6b7682", "rusty paint");
            k.Box(-0.3, 0.3, 0.28, 0.3, 0.32, 0.3, Orange, "rubber"); // (bungee)
            k.Hull(Frustum(1, 0, 0, 0.5, 0.22, 0.56, 0.22, 10), Grime, "steel");
            Tube(k, 1, 0, 0, 0.56, 0.9, 0.05, Steel, "steel");
            string[] panels = [Ceramic, "#9aa0a2", Ceramic, Tan, Ceramic, "#9aa0a2"];
            for (var i = 0; i < 6; i++)
            {
                double a0 = i * Math.PI / 3, a1 = (i + 1) * Math.PI / 3;
                k.Hull([[0.0, 1.3, -0.02], [0.4 * Math.Cos(a0), 1.3 + 0.4 * Math.Sin(a0), 0.1], [0.4 * Math.Cos(a1), 1.3 + 0.4 * Math.Sin(a1), 0.1], [0.0, 1.3, 0.0], [0.4 * Math.Cos(a0), 1.3 + 0.4 * Math.Sin(a0), 0.115], [0.4 * Math.Cos(a1), 1.3 + 0.4 * Math.Sin(a1), 0.115]], panels[i], "worn paint");
            }
            k.Box(-0.06, 0.88, -0.06, 0.06, 1.3, 0.0, Grime, "steel");
            foreach (var x in new[] { -0.25, 0.25 })
                k.Hull([[x - 0.008, 1.3, 0.1], [x + 0.008, 1.3, 0.1], [x - 0.008, 1.312, 0.1], [x + 0.008, 1.312, 0.1], [-0.008, 1.3, 0.38], [0.008, 1.3, 0.38], [-0.008, 1.312, 0.38], [0.008, 1.312, 0.38]], Steel, "steel");
            k.Hull(Frustum(2, 0.0, 1.3, 0.36, 0.04, 0.42, 0.02, 8), Grime, "plastic");
            CableZ(k, 0.1, 0.1, 0.9, 0.22, 0.4, 0.15);
        });
        yield return F("Whip Antenna", "a long whip antenna on a heavy spring base, a guy cord tied off to a bolt, a bit of coloured tape at the top so you can see it, and the coax run along the hull under tape.", [-0.2, 0, -0.2], [0.2, 2.3, 0.2], k =>
        {
            k.Box(-0.08, 0.0, -0.08, 0.08, 0.03, 0.08, Grime, "rusty paint");
            k.Hull(Frustum(1, 0, 0, 0.03, 0.04, 0.12, 0.04, 8), Brass, "brass");
            for (var i = 0; i < 4; i++)
                k.Hull(Frustum(1, 0, 0, 0.12 + i * 0.03, 0.03, 0.135 + i * 0.03, 0.03, 8), Chrome, "steel");
            Tube(k, 1, 0, 0, 0.24, 2.25, 0.006, Chrome, "steel", 4);
            k.Hull(Frustum(1, 0, 0, 2.15, 0.012, 2.22, 0.012, 6), Signal, "fabric");
            k.Hull([[0.0, 1.2, 0.0], [0.005, 1.2, 0.0], [0.15, 0.03, 0.12], [0.155, 0.03, 0.12], [0.0, 1.21, 0.005], [0.15, 0.04, 0.125]], Ceramic, "fabric");
            k.Box(0.13, 0.0, 0.1, 0.17, 0.04, 0.14, Chrome, "steel");
            k.Box(-0.015, 0.0, -0.2, 0.015, 0.02, -0.08, Rubber, "rubber");
            k.Box(-0.025, 0.0, -0.17, 0.025, 0.025, -0.13, Tape, "fabric");
        });
        yield return F("Radio Set", "an old shortwave set on a wall shelf, big tuning dial lit warm, chunky knobs, a handset hanging on a hook by its curly cord and a frequency list taped to the side.", [-0.35, 0, -0.25], [0.35, 1.0, 0.1], k =>
        {
            k.Box(-0.35, 0.5, -0.25, 0.35, 0.53, 0.05, WoodDark, "wood");
            foreach (var sx in new[] { -0.3, 0.3 })
                k.Hull([[sx - 0.015, 0.5, -0.25], [sx + 0.015, 0.5, -0.25], [sx - 0.015, 0.5, -0.05], [sx + 0.015, 0.5, -0.05], [sx - 0.015, 0.32, -0.25], [sx + 0.015, 0.32, -0.25]], Steel, "steel");
            Cab(k, -0.3, 0.53, -0.22, 0.24, 0.82, 0.02, "#4f5a4c");
            k.Box(-0.26, 0.66, 0.02, -0.02, 0.77, 0.03, Grime, "plastic");
            k.Glow(-0.24, 0.68, 0.03, -0.04, 0.75, 0.036, Bulb);
            k.Box(-0.15, 0.68, 0.036, -0.14, 0.75, 0.042, Signal, "plastic");
            foreach (var x in new[] { 0.04, 0.13 })
                k.Hull(Frustum(2, x, 0.71, 0.02, 0.03, 0.055, 0.028, 8), Rubber, "plastic");
            LouvreZ(k, -0.24, 0.2, 0.57, 0.64, 0.02, 3, "#4f5a4c");
            k.Box(0.25, 0.6, -0.12, 0.29, 0.66, -0.08, Chrome, "steel");
            k.Hull([[0.27, 0.42, -0.13], [0.33, 0.42, -0.13], [0.27, 0.6, -0.08], [0.33, 0.6, -0.08], [0.27, 0.42, -0.09], [0.33, 0.42, -0.09], [0.27, 0.6, -0.05], [0.33, 0.6, -0.05]], Rubber, "plastic");
            k.Box(0.241, 0.58, -0.2, 0.25, 0.8, -0.05, Ceramic, "plastic");
        });
        yield return F("Transponder", "a sealed grey box with a blinking amber lamp, its official seal broken and re-stuck with tape, an aerial lead screwed on, and the serial plate scratched off.", [-0.15, 0, -0.25], [0.15, 0.45, -0.1], k =>
        {
            k.Box(-0.13, 0.15, -0.25, 0.13, 0.4, -0.15, "#7c8186", "worn paint");
            k.Box(-0.12, 0.16, -0.15, 0.12, 0.39, -0.145, "#6a6f74", "worn paint");
            PilotZ(k, 0.07, 0.33, -0.145, Bulb, 0.014);
            k.Box(-0.06, 0.2, -0.145, 0.06, 0.24, -0.135, Tape, "fabric");
            k.Box(-0.1, 0.3, -0.145, 0.0, 0.36, -0.14, "#9a9a8e", "steel");
            Tube(k, 1, -0.08, -0.2, 0.4, 0.45, 0.012, Brass, "brass", 6);
            CableZ(k, -0.2, -0.08, 0.45, 0.15, 0.42, -0.02);
        });
        yield return F("Brass Telescope", "an old brass telescope clamped to a tripod with a hose clip, its leather grip worn through, a lens cap on a string and a star chart pinned under the leg.", [-0.4, 0, -0.4], [0.4, 1.5, 0.6], wall: false, draw: k =>
        {
            foreach (var a in new[] { Math.PI / 2, Math.PI / 2 + 2.1, Math.PI / 2 + 4.2 })
                k.Hull([[0.0, 1.1, 0.0], [0.02, 1.1, 0.0], [0.0, 1.1, 0.02], [0.35 * Math.Cos(a), 0.0, 0.35 * Math.Sin(a)], [0.35 * Math.Cos(a) + 0.02, 0.0, 0.35 * Math.Sin(a)], [0.35 * Math.Cos(a), 0.0, 0.35 * Math.Sin(a) + 0.02]], Wood, "wood");
            k.Box(-0.04, 1.08, -0.04, 0.04, 1.16, 0.04, Grime, "steel");
            k.Hull([[-0.045, 1.2, -0.3], [0.045, 1.2, -0.3], [-0.045, 1.29, -0.3], [0.045, 1.29, -0.3], [-0.06, 1.33, 0.5], [0.06, 1.33, 0.5], [-0.06, 1.45, 0.5], [0.06, 1.45, 0.5]], Brass, "brass");
            k.Hull([[-0.05, 1.2, -0.05], [0.05, 1.2, -0.05], [-0.05, 1.3, -0.05], [0.05, 1.3, -0.05], [-0.055, 1.23, 0.15], [0.055, 1.23, 0.15], [-0.055, 1.34, 0.15], [0.055, 1.34, 0.15]], Brown, "rubber");
            k.Box(-0.02, 1.15, -0.02, 0.02, 1.22, 0.02, Chrome, "steel");
            k.Box(0.1, 0.0, 0.15, 0.3, 0.012, 0.35, "#2c3446", "plastic");
        });
        yield return F("Nav Beacon", "a rotating amber beacon in a wire cage on a stubby post, the dome cloudy, one cage bar snapped, and its base bolted through a patch of rubber matting.", [-0.15, 0, -0.15], [0.15, 0.45, 0.15], k =>
        {
            k.Box(-0.14, 0.0, -0.14, 0.14, 0.012, 0.14, Rubber, "rubber matting");
            k.Hull(Frustum(1, 0, 0, 0.012, 0.06, 0.18, 0.05, 8), Steel, "rusty paint");
            k.Hull(Frustum(1, 0, 0, 0.18, 0.08, 0.21, 0.08, 10), Grime, "steel");
            k.Hull(Frustum(1, 0, 0, 0.21, 0.065, 0.36, 0.05, 10), "#ffae3c", "", glow: true);
            foreach (var a in new[] { 0.0, 1.57, 3.14 })
                Tube(k, 1, 0.085 * Math.Cos(a), 0.085 * Math.Sin(a), 0.21, 0.38, 0.004, Chrome, "steel", 4);
            Tube(k, 1, 0.0, -0.085, 0.21, 0.3, 0.004, Chrome, "steel", 4);
            k.Hull(Frustum(1, 0, 0, 0.37, 0.09, 0.39, 0.09, 8), Chrome, "steel");
        });
        yield return F("Searchlight", "a big drum searchlight on a yoke, the lens yellowed, a dent in the drum knocked out with a hammer, a handle to aim it by hand and the cable stapled down.", [-0.35, 0, -0.35], [0.35, 0.8, 0.45], wall: false, draw: k =>
        {
            k.Hull(Frustum(1, 0, 0, 0.0, 0.14, 0.05, 0.14, 10), Grime, "rusty paint");
            Tube(k, 1, 0, 0, 0.05, 0.25, 0.03, Steel, "steel");
            k.Box(-0.26, 0.25, -0.04, 0.26, 0.28, 0.04, Grime, "rusty paint");
            foreach (var sx in new[] { -1.0, 1.0 })
                k.Box(sx * 0.24 - 0.02, 0.25, -0.04, sx * 0.24 + 0.02, 0.5, 0.04, Grime, "rusty paint");
            k.Hull(Frustum(2, 0.0, 0.5, -0.25, 0.16, 0.25, 0.21, 12), "#5d6670", "worn paint");
            k.Hull(Frustum(2, 0.0, 0.5, 0.25, 0.22, 0.29, 0.22, 12), Chrome, "chrome");
            k.Hull(Frustum(2, 0.0, 0.5, 0.28, 0.19, 0.295, 0.19, 12), "#ffe2a8", "", glow: true);
            k.Hull([[0.1, 0.66, 0.0], [0.2, 0.6, 0.05], [0.2, 0.62, -0.08], [0.12, 0.68, -0.05], [0.15, 0.62, -0.02]], "#4a535c", "rusty paint");
            Tube(k, 2, 0.0, 0.5, -0.4, -0.25, 0.015, Chrome, "steel", 6);
            k.Box(-0.06, 0.48, -0.42, 0.06, 0.52, -0.38, Rubber, "plastic");
        });
        yield return F("CCTV Camera", "an old camera in a weatherproof housing on a wall bracket, the sun hood bent, a red tally lamp that doesn't light and a cobweb of cables taped to the bracket.", [-0.15, 0, -0.25], [0.15, 0.55, 0.25], k =>
        {
            k.Box(-0.05, 0.25, -0.25, 0.05, 0.38, -0.22, Grime, "steel");
            Tube(k, 2, 0.0, 0.32, -0.22, -0.05, 0.015, Steel, "steel");
            k.Box(-0.06, 0.32, -0.08, 0.06, 0.45, 0.2, Cream, "worn paint");
            k.Hull([[-0.075, 0.45, -0.1], [0.075, 0.45, -0.1], [-0.075, 0.47, -0.1], [0.075, 0.47, -0.1], [-0.075, 0.44, 0.24], [0.075, 0.46, 0.24], [-0.075, 0.455, 0.24], [0.075, 0.475, 0.24]], Cream, "worn paint");
            k.Hull(Frustum(2, 0.0, 0.385, 0.2, 0.04, 0.22, 0.04, 8), Rubber, "plastic");
            k.Box(0.03, 0.335, 0.2, 0.05, 0.355, 0.21, Signal, "plastic");
            CableZ(k, -0.2, -0.04, 0.33, 0.1, 0.1, 0.03);
            k.Box(-0.03, 0.27, -0.22, 0.03, 0.31, -0.18, Tape, "fabric");
        });

        // ===== Control and cockpit =====
        yield return F("Dinette Bench", "a camper dinette bench in split brown vinyl, foam showing through one tear and two strips of tape, a storage lid under the seat with a finger hole and a lap belt bolted on afterwards.", [-0.6, 0, -0.3], [0.6, 1.0, 0.3], k =>
        {
            k.Box(-0.6, 0.0, -0.25, 0.6, 0.4, 0.3, Wood, "wood");
            k.Box(-0.58, 0.02, 0.3, 0.58, 0.38, 0.312, WoodDark, "wood");
            k.Hull(Frustum(2, 0.0, 0.3, 0.31, 0.02, 0.315, 0.02, 8), Rubber, "plastic");
            k.Profile(-0.58, 0.58, [(0.4, -0.22), (0.4, 0.28), (0.46, 0.3), (0.5, 0.26), (0.5, -0.2)], Vinyl, "rubber");
            k.Profile(-0.575, 0.575, [(0.45, -0.3), (0.45, -0.18), (0.95, -0.22), (0.98, -0.3)], Vinyl, "rubber");
            k.Hull([[-0.2, 0.49, -0.05], [0.0, 0.49, 0.05], [0.05, 0.49, 0.0], [-0.15, 0.49, -0.1], [-0.12, 0.505, -0.03]], "#e0c98e", "fabric");
            k.Box(-0.3, 0.495, -0.12, -0.27, 0.51, 0.15, Tape, "fabric");
            k.Box(0.2, 0.495, 0.02, 0.4, 0.51, 0.06, Tape, "fabric");
            k.Box(-0.4, 0.45, 0.0, -0.37, 0.52, 0.04, Grime, "fabric");
            k.Box(0.37, 0.45, 0.0, 0.4, 0.52, 0.04, Grime, "fabric");
            k.Box(0.33, 0.46, 0.04, 0.42, 0.52, 0.055, Chrome, "chrome");
        });
        yield return F("Jump Seat", "a fold-down jump seat on the wall, its canvas sling patched, the frame hinged on rusty pins, a strap to hold it up and a hook for a helmet.", [-0.25, 0, -0.25], [0.25, 1.2, 0.25], k =>
        {
            k.Box(-0.22, 0.35, -0.25, 0.22, 1.15, -0.22, Steel, "rusty paint");
            foreach (var sx in new[] { -0.2, 0.2 })
                Tube(k, 2, sx, 0.48, -0.22, 0.22, 0.015, Steel, "rusty paint", 6);
            Tube(k, 0, 0.48, 0.22, -0.2, 0.2, 0.015, Steel, "rusty paint", 6);
            k.Box(-0.19, 0.46, -0.21, 0.19, 0.48, 0.21, Olive, "fabric");
            k.Box(-0.08, 0.48, -0.05, 0.06, 0.49, 0.08, Mustard, "fabric");
            k.Profile(-0.19, 0.19, [(0.55, -0.22), (0.55, -0.2), (1.0, -0.2), (1.0, -0.22)], Olive, "fabric");
            foreach (var sx in new[] { -0.2, 0.2 })
                k.Hull([[sx - 0.01, 0.48, 0.2], [sx + 0.01, 0.48, 0.2], [sx - 0.01, 1.1, -0.21], [sx + 0.01, 1.1, -0.21], [sx - 0.01, 0.5, 0.2], [sx + 0.01, 1.12, -0.21]], Chrome, "steel");
            k.Box(0.12, 1.05, -0.22, 0.15, 1.08, -0.15, Chrome, "steel");
        });
        yield return F("Overhead Switches", "a long row of chunky toggle switches and push buttons screwed to a plank on the wall above head height, labels on tape, three lamps, two of them blown, and a spare switch hanging on its wires.", [-0.5, 1.6, -0.25], [0.5, 2.0, -0.05], k =>
        {
            k.Box(-0.48, 1.65, -0.25, 0.48, 1.95, -0.2, "#6b5a42", "painted wood");
            for (var i = 0; i < 8; i++)
                ToggleZ(k, -0.4 + i * 0.1, 1.82, -0.2, i % 3 != 1);
            for (var i = 0; i < 8; i++)
                k.Box(-0.43 + i * 0.1, 1.73, -0.2, -0.37 + i * 0.1, 1.76, -0.192, Tape, "fabric");
            PilotZ(k, -0.3, 1.9, -0.2, Bulb, 0.012);
            k.Hull(Frustum(2, 0.0, 1.9, -0.2, 0.018, -0.186, 0.018, 8), Chrome, "chrome");
            k.Hull(Frustum(2, 0.3, 1.9, -0.2, 0.018, -0.186, 0.018, 8), Chrome, "chrome");
            k.Box(0.38, 1.6, -0.19, 0.42, 1.65, -0.16, Chrome, "steel");
            Tube(k, 1, 0.4, -0.2, 1.646, 1.7, 0.004, Signal, "rubber", 4);
        }, wall: true);
        yield return F("Throttle Stand", "a floor pedestal with two big throttle levers with worn black knobs, a trim wheel on the side, a friction screw, a cup holder made from a tin can and gaffer tape round the base.", [-0.2, 0, -0.25], [0.25, 1.1, 0.25], wall: false, draw: k =>
        {
            k.Box(-0.18, 0.0, -0.2, 0.18, 0.04, 0.2, Grime, "steel");
            k.Profile(-0.12, 0.12, [(0.04, -0.15), (0.04, 0.15), (0.85, 0.1), (0.95, 0.0), (0.95, -0.12)], "#5d6670", "worn paint");
            k.Box(-0.125, 0.06, -0.15, 0.125, 0.12, 0.15, Tape, "fabric");
            foreach (var x in new[] { -0.05, 0.05 })
            {
                k.Hull([[x - 0.01, 0.95, -0.02], [x + 0.01, 0.95, -0.02], [x - 0.01, 0.95, 0.02], [x + 0.01, 0.95, 0.02], [x - 0.008, 1.05, 0.06], [x + 0.008, 1.05, 0.06], [x - 0.008, 1.05, 0.09], [x + 0.008, 1.05, 0.09]], Chrome, "chrome");
                k.Hull(Frustum(1, x, 0.075, 1.04, 0.025, 1.09, 0.025, 8), Rubber, "plastic");
            }
            Wheel(k, 2, 0.0, 0.6, 0.12, 0.08, Grime);
            k.Hull(Frustum(1, 0.19, -0.05, 0.75, 0.04, 0.87, 0.04, 8), "#9a9a90", "steel");
            k.Box(0.12, 0.78, -0.07, 0.15, 0.8, -0.03, Steel, "steel");
        });
        yield return F("Computer Rack", "a rack of old computer boxes of different ages stacked on angle shelves, two tape reels, a mess of ribbon cables, a fan blowing on it from below and lamps blinking amber and green.", [-0.35, 0, -0.35], [0.35, 1.9, 0.35], k =>
        {
            Cage(k, -0.33, 0.0, -0.33, 0.33, 1.85, 0.33, Steel, 0.03);
            foreach (var y in new[] { 0.35, 0.8, 1.25 })
                k.Box(-0.3, y, -0.3, 0.3, y + 0.025, 0.3, Steel, "rusty paint");
            Cab(k, -0.28, 0.375, -0.28, 0.28, 0.78, 0.28, Cream);
            Cab(k, -0.28, 0.825, -0.28, 0.2, 1.1, 0.25, "#6b7682");
            Cab(k, -0.26, 1.275, -0.26, 0.26, 1.75, 0.26, Beige);
            foreach (var x in new[] { -0.12, 0.12 })
            {
                Ring(k, 2, x, 1.55, 0.265, 0.09, 0.02, Rubber, "plastic", 8);
                k.Hull(Frustum(2, x, 1.55, 0.26, 0.025, 0.28, 0.025, 6), Chrome, "steel");
            }
            for (var i = 0; i < 6; i++)
                PilotZ(k, -0.2 + i * 0.07, 0.68, 0.28, i % 2 == 0 ? Bulb : PilotGreen, 0.01);
            CableZ(k, 0.29, -0.2, 1.05, 0.15, 0.85, 0.12, Ceramic);
            CableZ(k, 0.3, -0.1, 1.3, 0.2, 1.08, 0.08, Ceramic);
            k.Hull(Frustum(1, 0, 0, 0.03, 0.2, 0.08, 0.2, 10), Grime, "dirty steel");
        });
        yield return F("CRT Monitor", "a deep old tube monitor on a scissor arm off the wall, its screen glowing dim green with a crack across the corner, a knob missing and a sticky note stuck on the bezel.", [-0.25, 0, -0.25], [0.25, 1.4, 0.35], k =>
        {
            k.Box(-0.06, 0.9, -0.25, 0.06, 1.1, -0.22, Grime, "steel");
            k.Hull([[-0.015, 1.0, -0.22], [0.015, 1.0, -0.22], [-0.015, 1.02, -0.22], [0.015, 1.02, -0.22], [-0.015, 0.95, 0.0], [0.015, 0.95, 0.0], [-0.015, 0.97, 0.0], [0.015, 0.97, 0.0]], Steel, "steel");
            k.Hull([[-0.2, 0.85, -0.02], [0.2, 0.85, -0.02], [-0.2, 1.2, -0.02], [0.2, 1.2, -0.02], [-0.22, 0.83, 0.3], [0.22, 0.83, 0.3], [-0.22, 1.22, 0.3], [0.22, 1.22, 0.3]], Beige, "worn paint");
            k.Box(-0.18, 0.87, 0.3, 0.18, 1.18, 0.31, Grime, "plastic");
            k.Box(-0.15, 0.9, 0.31, 0.12, 1.15, 0.315, "#2f4a32", "plastic");
            for (var i = 0; i < 4; i++)
                k.Glow(-0.13, 0.93 + i * 0.05, 0.315, -0.13 + 0.06 + (i * 5 % 4) * 0.05, 0.945 + i * 0.05, 0.32, Phosphor);
            k.Hull([[0.05, 1.15, 0.315], [0.06, 1.15, 0.315], [0.12, 1.08, 0.315], [0.12, 1.07, 0.315], [0.05, 1.15, 0.319], [0.12, 1.08, 0.319]], Ceramic, "plastic");
            k.Hull(Frustum(2, 0.16, 0.92, 0.31, 0.012, 0.325, 0.012, 6), Rubber, "plastic");
            k.Box(-0.2, 1.12, 0.31, -0.12, 1.2, 0.314, Mustard, "plastic");
        });
        yield return F("Intercom", "a cream intercom box with a round speaker grille, a talk bar worn shiny, a little channel dial and someone's name scratched into the casing.", [-0.12, 0, -0.25], [0.12, 0.5, -0.12], k =>
        {
            k.Box(-0.1, 0.15, -0.25, 0.1, 0.45, -0.18, Cream, "worn paint");
            k.Hull(Frustum(2, 0.0, 0.35, -0.18, 0.065, -0.17, 0.065, 10), Grime, "dirty steel");
            for (var i = 0; i < 4; i++)
                k.Box(-0.05, 0.31 + i * 0.022, -0.17, 0.05, 0.318 + i * 0.022, -0.164, Steel, "steel");
            k.Box(-0.07, 0.19, -0.18, 0.07, 0.24, -0.16, "#d8d2c0", "chrome");
            k.Hull(Frustum(2, 0.065, 0.42, -0.18, 0.012, -0.165, 0.012, 6), Rubber, "plastic");
            k.Box(-0.08, 0.26, -0.18, -0.02, 0.268, -0.175, "#8a8070", "worn paint");
        });
        yield return F("Klaxon", "a big old alarm horn on a bracket, its bell dented, a cracked red lens beside it and a wire guard over the whole thing, painted over at some point in a hurry.", [-0.2, 0, -0.25], [0.2, 0.5, 0.15], k =>
        {
            k.Box(-0.15, 0.2, -0.25, 0.15, 0.4, -0.22, Grime, "steel");
            k.Box(-0.08, 0.22, -0.22, 0.08, 0.38, -0.12, "#8a8d82", "rusty paint");
            k.Hull(Frustum(2, 0.0, 0.3, -0.12, 0.04, 0.1, 0.12, 10), "#8a8d82", "rusty paint");
            k.Hull([[0.06, 0.38, 0.05], [0.11, 0.33, 0.08], [0.1, 0.36, 0.1], [0.07, 0.4, 0.07], [0.08, 0.36, 0.06]], Rust, "rust");
            k.Hull(Frustum(2, -0.11, 0.42, -0.22, 0.03, -0.18, 0.03, 8), "#c23b2a", "plastic");
            k.Hull([[-0.13, 0.42, -0.179], [-0.09, 0.44, -0.179], [-0.11, 0.4, -0.179], [-0.13, 0.42, -0.175], [-0.09, 0.44, -0.175]], Rubber, "plastic");
            Ring(k, 2, 0.0, 0.3, 0.12, 0.13, 0.008, Chrome, "steel", 6);
            k.Hull(Frustum(2, 0.0, 0.3, 0.05, 0.08, 0.062, 0.08, 8), Grime, "dirty steel");
        });
        yield return F("Warning Beacon", "a rotating amber warning lamp on the wall in a battered housing, its bracket bent, the lens scratched and a hand-lettered sign under it that says what it means.", [-0.12, 0, -0.25], [0.12, 0.6, -0.05], k =>
        {
            k.Box(-0.08, 0.3, -0.25, 0.08, 0.36, -0.12, Grime, "rusty paint");
            k.Hull(Frustum(1, 0.0, -0.17, 0.36, 0.065, 0.4, 0.065, 8), "#5d6670", "worn paint");
            k.Hull(Frustum(1, 0.0, -0.17, 0.4, 0.055, 0.52, 0.045, 8), "#ffae3c", "", glow: true);
            k.Hull(Frustum(1, 0.0, -0.17, 0.52, 0.03, 0.54, 0.02, 8), "#5d6670", "worn paint");
            k.Box(-0.11, 0.15, -0.25, 0.11, 0.25, -0.24, Ceramic, "painted wood");
            k.Box(-0.09, 0.19, -0.24, 0.07, 0.21, -0.235, Signal, "worn paint");
        });
        yield return F("Bucket Seat", "a salvaged racing bucket seat bolted to rails welded on a box frame, its side bolster split and stitched with wire, a four-point harness with one strap knotted where it broke and a sheepskin over the back.", [-0.3, 0, -0.35], [0.3, 1.3, 0.35], wall: false, draw: k =>
        {
            Cage(k, -0.24, 0.0, -0.28, 0.24, 0.3, 0.28, Steel, 0.03);
            foreach (var x in new[] { -0.17, 0.17 })
                k.Box(x - 0.025, 0.3, -0.3, x + 0.025, 0.33, 0.3, Chrome, "steel");
            k.Profile(-0.22, 0.22, [(0.33, -0.25), (0.33, 0.28), (0.42, 0.3), (0.46, 0.28), (0.45, -0.22)], Rubber, "rubber");
            k.Profile(-0.24, 0.24, [(0.36, -0.32), (0.4, -0.2), (1.2, -0.27), (1.26, -0.34), (1.2, -0.4)], Rubber, "rubber");
            foreach (var sx in new[] { -1.0, 1.0 })
            {
                k.Hull([[sx * 0.22, 0.35, -0.25], [sx * 0.29, 0.35, -0.25], [sx * 0.22, 0.42, 0.25], [sx * 0.29, 0.48, 0.25], [sx * 0.22, 0.46, -0.25], [sx * 0.29, 0.52, -0.25]], Rubber, "rubber");
                k.Hull([[sx * 0.24, 0.5, -0.3], [sx * 0.3, 0.5, -0.3], [sx * 0.24, 1.0, -0.33], [sx * 0.3, 1.0, -0.33], [sx * 0.24, 0.5, -0.2], [sx * 0.3, 0.5, -0.2], [sx * 0.24, 1.0, -0.26], [sx * 0.3, 1.0, -0.26]], Rubber, "rubber");
            }
            k.Box(0.285, 0.55, -0.28, 0.305, 0.85, -0.25, Steel, "steel"); // (the wire stitching)
            k.Hull([[-0.2, 0.7, -0.24], [0.2, 0.7, -0.24], [-0.2, 1.22, -0.28], [0.2, 1.22, -0.28], [-0.2, 0.7, -0.2], [0.2, 0.7, -0.2], [-0.2, 1.22, -0.25], [0.2, 1.22, -0.25]], "#e6dcc4", "fabric");
            foreach (var x in new[] { -0.1, 0.1 })
                k.Hull([[x - 0.025, 1.215, -0.27], [x + 0.025, 1.215, -0.27], [x - 0.025, 0.55, -0.12], [x + 0.025, 0.55, -0.12], [x - 0.025, 1.215, -0.25], [x + 0.025, 1.215, -0.25], [x - 0.025, 0.55, -0.1], [x + 0.025, 0.55, -0.1]], Signal, "fabric");
            k.Box(0.07, 0.8, -0.18, 0.13, 0.84, -0.14, Signal, "fabric");
            k.Box(-0.05, 0.5, -0.08, 0.05, 0.56, -0.02, Chrome, "chrome");
        });

        // ===== Structure =====
        yield return F("Bulkhead Door", "a hinged steel bulkhead door, dented at boot height, rust running down from the top hinge, a small hand wheel instead of a handle, a hand-painted number and a strip of hazard tape half peeled off.", [-0.55, 0, -0.1], [0.55, 2.1, 0.1], wall: false, draw: k =>
        {
            k.Box(-0.55, 0.0, -0.08, -0.45, 2.1, 0.08, Steel, "rusty paint");
            k.Box(0.45, 0.0, -0.08, 0.55, 2.1, 0.08, Steel, "rusty paint");
            k.Box(-0.45, 2.0, -0.08, 0.45, 2.1, 0.08, Steel, "rusty paint");
            k.Box(-0.44, 0.01, -0.035, 0.44, 1.99, 0.035, "#8a8d82", "worn paint");
            PanelZ(k, -0.35, 1.1, 0.35, 1.85, 0.035, "#7a7d72");
            PanelZ(k, -0.35, 0.15, 0.35, 0.95, 0.035, "#9a8a6a", "rusty paint");
            k.Hull([[-0.2, 0.25, 0.048], [0.05, 0.22, 0.048], [0.02, 0.42, 0.048], [-0.18, 0.44, 0.048], [-0.08, 0.33, 0.035]], "#6e6a5e", "rusty paint");
            foreach (var y in new[] { 0.3, 1.7 })
                k.Box(-0.47, y, 0.035, -0.4, y + 0.15, 0.06, Grime, "steel");
            StreakZ(k, -0.42, 1.0, 1.7, 0.049, 0.05);
            Tube(k, 2, 0.3, 1.0, 0.035, 0.08, 0.012, Chrome, "steel");
            Wheel(k, 2, 0.3, 1.0, 0.085, 0.07);
            Stencil(k, -0.12, 1.9, 0.12, 1.97, 0.035, Ceramic);
            k.Box(-0.44, 0.95, 0.036, 0.0, 1.0, 0.044, HazardYStr, "hazard paint");
        });
        yield return F("Floor Hatch", "a square floor hatch with a lifting ring folded flat, a strip of tread plate worn smooth, a rubber seal squashed out at one corner and a chalked arrow showing which way it opens.", [-0.45, 0, -0.45], [0.45, 0.08, 0.45], wall: false, draw: k =>
        {
            k.Box(-0.45, 0.0, -0.45, 0.45, 0.04, 0.45, Steel, "rusty paint");
            k.Box(-0.38, 0.04, -0.38, 0.38, 0.055, 0.38, "#8a8d82", "tread plate");
            k.Box(-0.4, 0.04, -0.4, -0.3, 0.05, 0.4, Rubber, "rubber");
            k.Box(0.32, 0.055, -0.15, 0.36, 0.065, 0.15, Grime, "steel");
            Ring(k, 1, 0.0, 0.25, 0.065, 0.06, 0.012, Chrome, "steel", 6);
            k.Hull([[-0.1, 0.055, -0.2], [0.1, 0.055, -0.2], [0.0, 0.055, -0.3], [-0.1, 0.06, -0.2], [0.1, 0.06, -0.2], [0.0, 0.06, -0.3]], Ceramic, "plastic");
        });
        yield return F("Outer Airlock Door", "a heavy outer airlock door with a thick frame, faded hazard stripes, a small dark viewport with its glass crazed and taped, four dogs round the edge and one replaced with a G-clamp.", [-0.6, 0, -0.15], [0.6, 2.1, 0.15], wall: false, draw: k =>
        {
            k.Box(-0.6, 0.0, -0.12, -0.48, 2.1, 0.12, Grime, "dirty steel");
            k.Box(0.48, 0.0, -0.12, 0.6, 2.1, 0.12, Grime, "dirty steel");
            k.Box(-0.48, 1.98, -0.12, 0.48, 2.1, 0.12, Grime, "dirty steel");
            k.Box(-0.48, 0.0, -0.12, 0.48, 0.12, 0.12, Grime, "dirty steel");
            k.Box(-0.46, 0.13, -0.06, 0.46, 1.96, 0.06, Beige, "rusty paint");
            for (var i = 0; i < 6; i++)
                k.Hull([[-0.46 + i * 0.16, 0.14, 0.06], [-0.38 + i * 0.16, 0.14, 0.06], [-0.3 + i * 0.16, 0.3, 0.06], [-0.38 + i * 0.16, 0.3, 0.06], [-0.46 + i * 0.16, 0.14, 0.07], [-0.38 + i * 0.16, 0.14, 0.07], [-0.3 + i * 0.16, 0.3, 0.07], [-0.38 + i * 0.16, 0.3, 0.07]], "#b39552", "hazard paint");
            k.Hull(Frustum(2, 0.0, 1.5, 0.06, 0.14, 0.09, 0.14, 8), Grime, "steel");
            k.Hull(Frustum(2, 0.0, 1.5, 0.08, 0.1, 0.095, 0.1, 8), "#24303a", "plastic");
            TapeZ(k, 0.095, -0.08, 1.42, 0.08, 1.58, 0.03);
            foreach (var (x, y) in new[] { (-0.5, 0.6), (-0.5, 1.6), (0.5, 0.6) })
                k.Box(x - 0.04, y - 0.05, 0.12, x + 0.04, y + 0.05, 0.17, Chrome, "steel");
            k.Box(0.46, 1.52, 0.06, 0.56, 1.58, 0.2, Blue, "worn paint"); // (the G-clamp)
            k.Box(0.46, 1.52, 0.17, 0.56, 1.68, 0.2, Blue, "worn paint");
        });
        yield return F("Pipe Ladder", "a ladder welded up from scrap pipe, every rung a different length and one bent, the welds lumpy, rust where hands don't go and bright where they do, wired to its brackets.", [-0.25, 0, -0.25], [0.25, 2.5, -0.05], k =>
        {
            foreach (var sx in new[] { -0.21, 0.2 })
                Tube(k, 1, sx, -0.13, 0.0, 2.45, 0.022, sx < 0 ? "#6f6a60" : "#7f6a50", "rust", 6);
            double[] widths = [0.21, 0.24, 0.2, 0.22, 0.25, 0.21, 0.23, 0.2];
            for (var i = 0; i < 8; i++)
            {
                var y = 0.25 + i * 0.29 + (i % 3) * 0.01;
                if (i == 4)
                    k.Hull([[-0.21, y, -0.14], [-0.21, y + 0.03, -0.14], [0.0, y - 0.04, -0.1], [0.0, y - 0.01, -0.1], [0.2, y, -0.14], [0.2, y + 0.03, -0.14]], Steel, "steel");
                else
                    Tube(k, 0, y, -0.13, -widths[i], widths[i], 0.014, i % 2 == 0 ? "#8a8f94" : "#7d7466", "steel", 6);
                k.Box(-0.234, y - 0.02, -0.15, -0.19, y + 0.02, -0.11, "#5a5650", "rust");
            }
            foreach (var y in new[] { 0.2, 2.0 })
                foreach (var sx in new[] { -0.21, 0.2 })
                    k.Box(sx - 0.015, y, -0.25, sx + 0.015, y + 0.04, -0.15, Grime, "steel");
        });
        yield return F("Scrap Steps", "three steps welded from bent plate on a frame of angle, grip tape on the treads worn through in the middle, and a tin can wired on as a nosing.", [-0.35, 0, -0.45], [0.35, 0.6, 0.45], wall: false, draw: k =>
        {
            for (var i = 0; i < 3; i++)
            {
                var z0 = 0.45 - (i + 1) * 0.3;
                k.Box(-0.33, i * 0.2, z0, 0.33, (i + 1) * 0.2, 0.45 - i * 0.3, i == 1 ? "#7f7a6a" : Steel, "rusty paint");
                k.Box(-0.28, (i + 1) * 0.2, z0 + 0.03, -0.04, (i + 1) * 0.2 + 0.006, 0.42 - i * 0.3, Rubber, "rubber matting");
                k.Box(0.04, (i + 1) * 0.2, z0 + 0.03, 0.28, (i + 1) * 0.2 + 0.006, 0.42 - i * 0.3, Rubber, "rubber matting");
            }
            k.Hull(Frustum(0, 0.2, 0.45, -0.3, 0.025, 0.3, 0.025, 6), "#a0a6a8", "steel");
        });
        yield return F("Grab Rail", "a pipe grab rail on three different brackets, its paint worn to bare steel along the top, grip tape wound round the middle and a rag knotted on the end.", [-0.6, 0, -0.25], [0.6, 1.2, -0.05], k =>
        {
            Tube(k, 0, 1.0, -0.12, -0.6, 0.6, 0.02, Olive, "worn paint", 6);
            k.Hull(Frustum(0, 1.0, -0.12, -0.15, 0.024, 0.15, 0.024, 6), Rubber, "fabric");
            foreach (var (x, c) in new[] { (-0.5, Grime), (0.0, Steel), (0.5, Brass) })
            {
                k.Box(x - 0.02, 0.95, -0.25, x + 0.02, 1.05, -0.23, c, "steel");
                Tube(k, 2, x, 1.0, -0.23, -0.14, 0.01, c, "steel", 4);
            }
            k.Box(0.56, 0.9, -0.14, 0.62, 1.02, -0.1, Orange, "fabric");
        });
        yield return F("Deck Grating", "a square of open steel grating, some bars bent, a patch welded in from a different grate, cable ties holding a loose corner and dirt caught in the holes.", [-0.5, 0, -0.5], [0.5, 0.06, 0.5], wall: false, draw: k =>
        {
            k.Box(-0.5, 0.0, -0.5, 0.5, 0.012, 0.5, "#2c2a26", "dirty steel");
            for (var i = 0; i < 9; i++)
                k.Box(-0.48, 0.012, -0.46 + i * 0.115, 0.48, 0.05, -0.44 + i * 0.115, Steel, "rusty paint");
            for (var i = 0; i < 6; i++)
                k.Box(-0.42 + i * 0.17, 0.012, -0.48, -0.41 + i * 0.17, 0.04, 0.48, Steel, "rusty paint");
            k.Box(0.1, 0.05, 0.1, 0.4, 0.056, 0.4, "#7d7466", "rust");
            k.Box(-0.48, 0.05, 0.42, -0.4, 0.06, 0.45, Ceramic, "plastic");
        });
        yield return F("Hull Patch", "a patch plate welded over a hole in the hull wall, lumpy weld bead round it, the old paint around blistered, a second smaller patch overlapping the first, and chalk circles round a crack still growing.", [-0.45, 0, -0.25], [0.45, 1.2, -0.15], k =>
        {
            k.Box(-0.43, 0.2, -0.25, 0.43, 1.1, -0.22, "#8a8d82", "worn paint");
            k.Box(-0.3, 0.35, -0.22, 0.2, 0.85, -0.205, "#7f7a6a", "rusty paint");
            k.Box(-0.32, 0.33, -0.22, 0.22, 0.35, -0.2, Grime, "soot");
            k.Box(-0.32, 0.85, -0.22, 0.22, 0.87, -0.2, Grime, "soot");
            k.Box(-0.32, 0.35, -0.22, -0.3, 0.85, -0.2, Grime, "soot");
            k.Box(0.2, 0.35, -0.22, 0.22, 0.85, -0.2, Grime, "soot");
            k.Box(0.05, 0.7, -0.205, 0.32, 0.98, -0.19, Avocado, "rusty paint");
            Ring(k, 2, 0.3, 0.45, -0.218, 0.06, 0.008, Ceramic, "plastic", 6);
            k.Hull([[0.26, 0.47, -0.22], [0.27, 0.47, -0.22], [0.34, 0.42, -0.22], [0.26, 0.47, -0.215], [0.34, 0.42, -0.215], [0.33, 0.42, -0.22]], Rubber, "soot");
        });
        yield return F("Jacked Strut", "a steel I-beam post propping a sagging deck, gusset plates welded on crooked, a screw jack wound up under its foot and a block of wood shimming the gap.", [-0.25, 0, -0.25], [0.25, 2.4, 0.25], wall: false, draw: k =>
        {
            k.Box(-0.2, 0.0, -0.2, 0.2, 0.05, 0.2, Steel, "rusty paint");
            k.Hull(Frustum(1, 0, 0, 0.05, 0.12, 0.25, 0.06, 8), Signal, "worn paint");
            Tube(k, 1, 0, 0, 0.25, 0.42, 0.035, Chrome, "steel");
            k.Box(-0.14, 0.42, -0.14, 0.14, 0.5, 0.14, Wood, "wood");
            k.Box(-0.12, 0.5, -0.12, 0.12, 0.53, 0.12, Steel, "rusty paint");
            k.Box(-0.1, 0.53, -0.012, 0.1, 2.3, 0.012, Steel, "rusty paint");
            k.Box(-0.1, 0.53, -0.1, 0.1, 2.3, -0.08, Steel, "rusty paint");
            k.Box(-0.1, 0.53, 0.08, 0.1, 2.3, 0.1, Steel, "rusty paint");
            k.Hull([[0.012, 2.1, -0.08], [0.012, 2.3, -0.08], [0.18, 2.3, -0.08], [0.012, 2.1, -0.068], [0.012, 2.3, -0.068], [0.18, 2.3, -0.068]], "#7f7a6a", "rusty paint");
            k.Box(-0.15, 2.3, -0.15, 0.15, 2.36, 0.15, Steel, "rusty paint");
            StreakZ(k, 0.05, 1.2, 2.2, 0.1, 0.03);
        });
        yield return F("Porthole", "a round porthole in a thick bolted frame, the glass dark and scratched, a bit of curtain on a wire across the top, a crack round the frame filled with sealant and a drip stain under it.", [-0.3, 0, -0.25], [0.3, 1.6, -0.12], k =>
        {
            k.Box(-0.28, 0.8, -0.25, 0.28, 1.5, -0.23, "#8a8d82", "worn paint");
            k.Hull(Frustum(2, 0.0, 1.15, -0.23, 0.24, -0.19, 0.24, 12), Grime, "dirty steel");
            k.Hull(Frustum(2, 0.0, 1.15, -0.19, 0.17, -0.185, 0.17, 12), "#24303a", "plastic");
            for (var i = 0; i < 8; i++)
            {
                var a = i * Math.PI / 4;
                k.Box(0.21 * Math.Cos(a) - 0.012, 1.15 + 0.21 * Math.Sin(a) - 0.012, -0.19, 0.21 * Math.Cos(a) + 0.012, 1.15 + 0.21 * Math.Sin(a) + 0.012, -0.178, Chrome, "steel");
            }
            Tube(k, 0, 1.42, -0.17, -0.28, 0.28, 0.004, Steel, "steel", 4);
            k.Box(-0.26, 1.28, -0.18, -0.05, 1.42, -0.165, Orange, "fabric");
            StreakZ(k, 0.05, 0.85, 0.92, -0.23, 0.03);
        });

        // ===== Living =====
        yield return F("Hammock", "a canvas hammock slung between two hooks, patched with a different cloth, a blanket balled up in it, a string bag of belongings hanging off one end.", [-1.0, 0, -0.35], [1.0, 1.6, 0.2], k =>
        {
            foreach (var sx in new[] { -0.95, 0.95 })
            {
                k.Box(sx - 0.04, 1.3, -0.35, sx + 0.04, 1.4, -0.32, Grime, "steel");
                k.Box(sx - 0.012, 1.32, -0.32, sx + 0.012, 1.36, -0.2, Chrome, "steel");
            }
            k.Hull([[-0.95, 1.32, -0.21], [-0.95, 1.34, -0.19], [-0.5, 1.0, -0.3], [-0.5, 1.0, -0.05], [-0.5, 0.97, -0.17], [0.5, 1.0, -0.3], [0.5, 1.0, -0.05], [0.5, 0.97, -0.17], [0.95, 1.32, -0.21], [0.95, 1.34, -0.19]], Olive, "fabric");
            k.Box(0.1, 1.0, -0.24, 0.35, 1.03, -0.1, "#7f6a50", "fabric");
            k.Hull([[-0.3, 1.0, -0.25], [-0.05, 1.0, -0.25], [-0.3, 1.0, -0.08], [-0.05, 1.0, -0.08], [-0.17, 1.09, -0.16]], Blue, "fabric");
            k.Hull([[0.9, 1.3, -0.2], [0.95, 1.3, -0.2], [0.85, 1.0, -0.15], [1.0, 1.0, -0.15], [0.85, 1.0, -0.25], [1.0, 1.0, -0.25], [0.92, 0.9, -0.2]], Mustard, "fabric");
        });
        yield return F("Bunk Stack", "two bunks in a tube frame, mismatched thin mattresses, a curtain made from a tablecloth on the bottom one, clothes hung on the frame, a reading torch taped to a post and boots under the bottom bunk.", [-1.0, 0, -0.42], [1.0, 1.9, 0.42], k =>
        {
            foreach (var x in new[] { -0.97, 0.97 })
                foreach (var z in new[] { -0.38, 0.38 })
                    Tube(k, 1, x, z, 0.0, 1.85, 0.02, Steel, "rusty paint", 6);
            foreach (var y in new[] { 0.35, 1.2 })
            {
                k.Box(-0.97, y, -0.4, 0.97, y + 0.04, 0.4, Steel, "rusty paint");
                k.Box(-0.95, y + 0.04, -0.37, 0.95, y + 0.13, 0.37, y < 1 ? "#a8a090" : "#8f9a9a", "fabric");
            }
            k.Box(-0.95, 1.33, -0.3, 0.2, 1.36, 0.36, Orange, "fabric");
            k.Box(-0.1, 0.5, 0.39, 0.95, 1.18, 0.41, "#c9b48a", "fabric");
            for (var i = 0; i < 4; i++)
                k.Box(-0.05 + i * 0.25, 0.5, 0.41, 0.0 + i * 0.25, 1.18, 0.42, Signal, "fabric");
            k.Hull([[-0.99, 1.6, 0.4], [-0.95, 1.6, 0.4], [-0.99, 1.1, 0.44], [-0.95, 1.1, 0.44], [-0.99, 1.6, 0.36], [-0.95, 1.6, 0.36]], Blue, "fabric");
            k.Hull(Frustum(1, 0.95, 0.38, 1.5, 0.025, 1.62, 0.025, 6), Grime, "plastic");
            k.Box(0.92, 1.53, 0.35, 0.98, 1.57, 0.41, Tape, "fabric");
            foreach (var x in new[] { -0.6, -0.45 })
                k.Box(x, 0.0, -0.1, x + 0.11, 0.18, 0.2, "#4a3626", "rubber");
        });
        yield return F("Camp Stove", "a single gas ring on a steel shelf, a blackened pot on it with a dent, the gas bottle on the floor under it with a hose taped at the joint, a box of matches and a sooty patch on the wall.", [-0.35, 0, -0.25], [0.35, 1.2, 0.1], k =>
        {
            k.Box(-0.33, 0.8, -0.25, 0.33, 0.83, 0.05, Steel, "rusty paint");
            foreach (var sx in new[] { -0.28, 0.28 })
                k.Hull([[sx - 0.015, 0.8, -0.25], [sx + 0.015, 0.8, -0.25], [sx - 0.015, 0.8, 0.0], [sx + 0.015, 0.8, 0.0], [sx - 0.015, 0.6, -0.25], [sx + 0.015, 0.6, -0.25]], Steel, "steel");
            k.Hull(Frustum(1, -0.08, -0.08, 0.83, 0.1, 0.88, 0.09, 10), Grime, "soot");
            k.Hull(Frustum(1, -0.08, -0.08, 0.88, 0.11, 1.02, 0.1, 10), "#3a3633", "soot");
            k.Hull([[-0.12, 0.93, 0.0], [-0.05, 0.93, 0.02], [-0.06, 0.98, 0.02], [-0.11, 0.97, 0.01], [-0.08, 0.95, 0.025]], "#4a4540", "soot");
            k.Box(-0.2, 1.01, -0.09, -0.17, 1.03, -0.07, Grime, "plastic");
            k.Box(-0.3, 0.86, -0.25, 0.15, 1.2, -0.245, "#2b2622", "soot");
            TankUp(k, 0.12, -0.08, 0.0, 0.45, 0.13, Orange);
            Pipe(k, Rubber, 0.01, "rubber", (0.12, 0.45, -0.08), (0.12, 0.6, -0.08), (-0.08, 0.6, -0.08), (-0.08, 0.82, -0.08));
            k.Box(0.06, 0.58, -0.1, 0.12, 0.62, -0.06, Tape, "fabric");
            k.Box(0.15, 0.83, -0.05, 0.22, 0.85, 0.0, "#d9a72a", "plastic");
        });
        yield return F("Old Fridge", "a rounded old fridge, cream gone yellow, a big chrome lever handle, rust blooming along the bottom edge, magnets and notes on the door and a chain with a padlock round it.", [-0.32, 0, -0.32], [0.32, 1.5, 0.35], wall: false, draw: k =>
        {
            k.Box(-0.3, 0.0, -0.3, 0.3, 0.06, 0.28, Grime, "dirty steel");
            k.Profile(-0.3, 0.3, [(0.06, -0.3), (0.06, 0.27), (1.38, 0.27), (1.45, 0.2), (1.47, 0.0), (1.47, -0.3)], "#d6ccaa", "gloss paint");
            k.Box(-0.29, 0.07, 0.27, 0.29, 0.2, 0.285, Rust, "rust");
            k.Box(-0.28, 0.25, 0.266, 0.28, 1.35, 0.282, "#cfc5a2", "gloss paint");
            k.Box(0.18, 0.85, 0.282, 0.22, 1.1, 0.33, Chrome, "chrome");
            foreach (var (x, y, c) in new[] { (-0.15, 1.1, Signal), (-0.05, 0.95, Blue), (-0.2, 0.8, Mustard) })
                k.Box(x, y, 0.282, x + 0.07, y + 0.06, 0.294, c, "plastic");
            k.Box(-0.12, 0.6, 0.282, 0.0, 0.75, 0.288, Ceramic, "plastic");
            k.Box(-0.31, 0.95, 0.27, 0.31, 0.97, 0.3, Chrome, "steel");
            k.Box(0.0, 0.88, 0.3, 0.05, 0.95, 0.32, Brass, "brass");
        });
        yield return F("Steel Sink", "a dented steel sink on four bent legs, a bucket under it instead of a drain, a pump tap, a scrubbing brush and a crusty bar of soap on the rim.", [-0.35, 0, -0.3], [0.35, 1.1, 0.25], k =>
        {
            foreach (var (x, z) in new[] { (-0.3, -0.25), (0.3, -0.25), (-0.3, 0.2), (0.3, 0.2) })
                Tube(k, 1, x, z, 0.0, 0.8, 0.015, Steel, "rusty paint", 6);
            k.Box(-0.33, 0.8, -0.28, 0.33, 0.85, 0.23, Chrome, "steel");
            k.Box(-0.28, 0.72, -0.22, 0.28, 0.851, 0.18, "#6f716d", "steel");
            k.Hull(Frustum(1, 0.0, -0.02, 0.0, 0.13, 0.28, 0.15, 8), Blue, "plastic");
            Tube(k, 1, -0.2, -0.24, 0.85, 1.05, 0.02, Steel, "rusty paint", 6);
            k.Hull([[-0.2, 1.03, -0.24], [-0.2, 1.06, -0.24], [-0.2, 1.03, -0.1], [-0.2, 1.06, -0.1], [-0.18, 1.03, -0.24], [-0.18, 1.06, -0.24], [-0.18, 1.03, -0.1], [-0.18, 1.06, -0.1]], Steel, "steel");
            k.Hull([[-0.2, 1.06, -0.24], [-0.18, 1.06, -0.24], [-0.2, 1.18, -0.4], [-0.18, 1.18, -0.4], [-0.2, 1.08, -0.24], [-0.18, 1.08, -0.24]], Signal, "worn paint");
            k.Box(0.15, 0.85, -0.25, 0.27, 0.88, -0.2, "#7f6a50", "wood");
            k.Box(0.05, 0.85, -0.26, 0.11, 0.87, -0.22, "#e0d3a8", "plastic");
        });
        yield return F("Bucket Head", "a steel toilet bowl on a welded pedestal with a vacuum hose to the wall, a seat cut from plywood, a hand-written sign about the lever, a mop bucket and a roll of paper on a bent coat-hanger.", [-0.4, 0, -0.35], [0.4, 1.4, 0.35], k =>
        {
            k.Box(-0.35, 0.0, -0.3, 0.35, 0.02, 0.35, "#7f8a7a", "rubber matting");
            k.Hull(Frustum(1, 0.0, 0.05, 0.02, 0.1, 0.3, 0.13, 8), Steel, "rusty paint");
            k.Hull(Frustum(1, 0.0, 0.05, 0.3, 0.17, 0.42, 0.19, 10), Chrome, "steel");
            k.Hull(Frustum(1, 0.0, 0.05, 0.42, 0.19, 0.44, 0.19, 10), "#a08060", "wood");
            Pipe(k, Rubber, 0.04, "rubber", (0.0, 0.35, -0.12), (0.0, 0.35, -0.2), (0.0, 0.8, -0.2), (0.0, 0.8, -0.35));
            k.Box(0.1, 0.7, -0.35, 0.2, 0.82, -0.3, Grime, "steel");
            k.Box(0.17, 0.74, -0.3, 0.19, 0.76, -0.2, Signal, "worn paint");
            k.Box(-0.3, 0.9, -0.35, -0.08, 1.15, -0.34, Ceramic, "plastic");
            k.Hull(Frustum(1, 0.28, 0.2, 0.02, 0.1, 0.28, 0.12, 8), Mustard, "plastic");
            Tube(k, 1, 0.28, 0.2, 0.28, 0.75, 0.01, "#7f6a50", "wood", 4);
            Tube(k, 0, 0.6, -0.3, -0.38, -0.26, 0.004, Steel, "steel", 4);
            k.Hull(Frustum(0, 0.6, -0.27, -0.37, 0.05, -0.27, 0.05, 8), Ceramic, "fabric");
        });
        yield return F("Shower Stall", "a shower made from a tube frame and a plastic curtain gone mouldy at the hem, a watering-can rose on a bent pipe, a slatted duckboard on the tray and a bottle wedged on the frame.", [-0.45, 0, -0.45], [0.45, 2.1, 0.45], wall: false, draw: k =>
        {
            k.Box(-0.45, 0.0, -0.45, 0.45, 0.06, 0.45, "#9aa39b", "plastic");
            for (var i = 0; i < 6; i++)
                k.Box(-0.4, 0.06, -0.38 + i * 0.13, 0.4, 0.09, -0.3 + i * 0.13, Wood, "wood");
            foreach (var (x, z) in new[] { (-0.43, -0.43), (0.43, -0.43), (-0.43, 0.43), (0.43, 0.43) })
                Tube(k, 1, x, z, 0.06, 2.05, 0.015, Chrome, "steel", 6);
            k.Box(-0.42, 0.25, 0.42, 0.2, 2.0, 0.435, "#c9d2c8", "plastic");
            k.Box(-0.42, 0.25, 0.42, 0.2, 0.38, 0.436, "#7f8a6a", "plastic");
            Pipe(k, Steel, 0.012, "steel", (0.0, 0.06, -0.43), (0.0, 1.95, -0.43), (0.0, 1.95, -0.2));
            k.Hull(Frustum(1, 0.0, -0.2, 1.88, 0.08, 1.95, 0.03, 10), "#5d8a5a", "plastic");
            k.Hull(Frustum(1, 0.38, 0.0, 1.0, 0.03, 1.2, 0.03, 6), Orange, "plastic");
        });
        yield return F("Fold Table", "a fold-down table on a wall hinge, its laminate top worn through to the board in a ring, two mugs and a plate, a chain to hold it level and a deck of cards held with a rubber band.", [-0.5, 0, -0.25], [0.5, 1.0, 0.45], k =>
        {
            k.Box(-0.5, 0.68, -0.25, 0.5, 0.72, -0.22, Steel, "steel");
            k.Box(-0.48, 0.72, -0.22, 0.48, 0.75, 0.42, "#c7b48a", "plastic");
            k.Hull(Frustum(1, 0.1, 0.1, 0.75, 0.12, 0.752, 0.12, 10), Wood, "wood");
            k.Hull(Frustum(1, -0.25, 0.05, 0.752, 0.035, 0.84, 0.035, 8), Orange, "gloss paint");
            k.Hull(Frustum(1, 0.32, 0.25, 0.752, 0.035, 0.83, 0.033, 8), Ceramic, "gloss paint");
            k.Hull(Frustum(1, 0.0, 0.25, 0.752, 0.1, 0.765, 0.11, 10), Ceramic, "gloss paint");
            k.Box(-0.4, 0.752, 0.25, -0.33, 0.775, 0.35, Signal, "plastic");
            foreach (var sx in new[] { -0.46, 0.46 })
                k.Hull([[sx - 0.006, 0.74, 0.4], [sx + 0.006, 0.74, 0.4], [sx - 0.006, 1.0, -0.22], [sx + 0.006, 1.0, -0.22], [sx - 0.006, 0.75, 0.42], [sx + 0.006, 1.0, -0.2]], Chrome, "steel");
        });
        yield return F("Plank Bench", "a scaffold plank on two chains off the wall, a folded blanket for a cushion, initials carved in the plank and a nail sticking up that someone hammered flat.", [-0.6, 0, -0.25], [0.6, 1.2, 0.15], k =>
        {
            k.Box(-0.6, 0.42, -0.24, 0.6, 0.47, 0.12, "#9c7a4e", "wood");
            foreach (var sx in new[] { -0.55, 0.55 })
            {
                k.Box(sx - 0.03, 1.05, -0.25, sx + 0.03, 1.1, -0.22, Grime, "steel");
                k.Hull([[sx - 0.006, 0.47, 0.1], [sx + 0.006, 0.47, 0.1], [sx - 0.006, 1.06, -0.22], [sx + 0.006, 1.06, -0.22], [sx - 0.006, 0.48, 0.12], [sx + 0.006, 1.07, -0.22]], Chrome, "steel");
            }
            k.Box(-0.4, 0.47, -0.2, 0.05, 0.52, 0.1, "#6b7a8a", "fabric");
            k.Box(0.25, 0.47, -0.1, 0.35, 0.474, -0.06, WoodDark, "wood");
            k.Box(0.45, 0.47, 0.0, 0.47, 0.475, 0.02, Chrome, "steel");
        });
        yield return F("Percolator", "a dented aluminium coffee percolator on a hot plate on a shelf, the hot plate's cord mended with tape, a jar of instant with no label and three mugs on hooks, one chipped.", [-0.3, 0, -0.25], [0.3, 1.2, 0.05], k =>
        {
            k.Box(-0.3, 0.8, -0.25, 0.3, 0.83, 0.03, WoodDark, "wood");
            k.Box(-0.2, 0.83, -0.18, 0.04, 0.87, 0.0, Grime, "steel");
            k.Hull(Frustum(1, -0.08, -0.09, 0.87, 0.07, 1.05, 0.055, 8), "#b4b6b2", "aluminium");
            k.Hull(Frustum(1, -0.08, -0.09, 1.05, 0.03, 1.09, 0.015, 8), "#b4b6b2", "aluminium");
            k.Hull([[-0.02, 1.0, -0.09], [-0.02, 1.02, -0.09], [0.03, 0.9, -0.09], [0.03, 0.92, -0.09], [-0.02, 1.0, -0.075], [0.03, 0.9, -0.075]], Grime, "plastic");
            k.Hull(Frustum(1, 0.18, -0.12, 0.83, 0.045, 0.95, 0.045, 8), "#7f6a50", "plastic");
            for (var i = 0; i < 3; i++)
            {
                var x = -0.2 + i * 0.18;
                k.Box(x - 0.005, 0.66, -0.25, x + 0.005, 0.7, -0.2, Chrome, "steel");
                k.Hull(Frustum(1, x, -0.18, 0.56, 0.035, 0.65, 0.035, 8), i == 1 ? Blue : i == 2 ? Mustard : Ceramic, "gloss paint");
            }
            CableZ(k, -0.17, -0.2, 0.84, -0.28, 0.4, 0.05);
            k.Box(-0.27, 0.6, -0.2, -0.24, 0.66, -0.14, Tape, "fabric");
        });
        yield return F("Box TV", "a fat old television on a wall bracket, rabbit-ear aerial with foil on the tips, a dial for the channel, the picture a blur of warm colour and a cloth doily on top with a plastic plant.", [-0.3, 0, -0.25], [0.3, 1.8, 0.25], k =>
        {
            k.Box(-0.12, 1.1, -0.25, 0.12, 1.2, -0.22, Grime, "steel");
            k.Box(-0.2, 1.12, -0.22, 0.2, 1.16, 0.15, Steel, "steel");
            k.Profile(-0.27, 0.27, [(1.16, -0.18), (1.16, 0.2), (1.55, 0.2), (1.55, -0.1), (1.48, -0.18)], WoodDark, "wood");
            k.Box(-0.24, 1.2, 0.2, 0.12, 1.51, 0.21, Grime, "plastic");
            k.Glow(-0.21, 1.23, 0.21, 0.09, 1.48, 0.215, "#d9b47a");
            foreach (var y in new[] { 1.42, 1.3 })
                k.Hull(Frustum(2, 0.19, y, 0.2, 0.025, 0.23, 0.022, 8), Ceramic, "plastic");
            k.Box(-0.2, 1.55, -0.12, 0.15, 1.556, 0.15, Ceramic, "fabric");
            foreach (var sx in new[] { -1.0, 1.0 })
                k.Hull([[sx * 0.008 - 0.005, 1.555, 0.0], [sx * 0.008 + 0.005, 1.555, 0.0], [sx * 0.2 - 0.005, 1.78, -0.05], [sx * 0.2 + 0.005, 1.78, -0.05], [sx * 0.008, 1.555, 0.01], [sx * 0.2, 1.78, -0.04]], Chrome, "steel");
            k.Hull(Frustum(1, 0.08, 0.05, 1.556, 0.035, 1.6, 0.03, 6), Signal, "plastic");
            k.Hull([[0.08, 1.6, 0.05], [0.0, 1.7, 0.0], [0.14, 1.72, 0.1], [0.1, 1.68, -0.03]], "#5d8a5a", "plastic");
        });
        yield return F("Curtain Wardrobe", "a steel cabinet with its doors long gone, a flowered curtain hung across on a wire instead, clothes on a pipe rail, boots on the floor of it and a dent in the side where something hit it.", [-0.45, 0, -0.3], [0.45, 1.9, 0.3], k =>
        {
            k.Box(-0.45, 0.0, -0.3, 0.45, 1.9, -0.27, "#7a7d72", "rusty paint");
            foreach (var sx in new[] { -1.0, 1.0 })
                k.Box(sx * 0.45 - (sx > 0 ? 0.025 : 0), 0.0, -0.27, sx * 0.45 + (sx < 0 ? 0.025 : 0), 1.9, 0.28, "#7a7d72", "rusty paint");
            k.Box(-0.425, 1.86, -0.27, 0.425, 1.9, 0.28, "#7a7d72", "rusty paint");
            Tube(k, 0, 1.65, 0.0, -0.425, 0.425, 0.012, Chrome, "steel", 6);
            foreach (var (x, c) in new[] { (-0.3, Blue), (-0.15, Olive), (0.05, "#8a5a3a"), (0.25, Ceramic) })
                k.Hull([[x - 0.06, 1.62, -0.12], [x + 0.06, 1.62, -0.12], [x - 0.06, 1.62, 0.12], [x + 0.06, 1.62, 0.12], [x - 0.04, 1.0, -0.1], [x + 0.04, 1.0, -0.1], [x - 0.04, 1.0, 0.1], [x + 0.04, 1.0, 0.1]], c, "fabric");
            Tube(k, 0, 1.82, 0.27, -0.44, 0.44, 0.004, Steel, "steel", 4);
            k.Box(-0.42, 0.05, 0.265, 0.05, 1.8, 0.275, "#c98a5a", "fabric");
            for (var i = 0; i < 4; i++)
                k.Box(-0.38 + i * 0.12, 0.3 + (i % 2) * 0.5, 0.275, -0.33 + i * 0.12, 0.36 + (i % 2) * 0.5, 0.28, Mustard, "fabric");
            k.Box(0.12, 0.0, -0.1, 0.35, 0.2, 0.15, "#4a3626", "rubber");
            k.Hull([[0.454, 0.8, 0.0], [0.454, 1.0, 0.05], [0.454, 0.95, -0.12], [0.434, 0.9, -0.03]], "#6e6a5e", "rusty paint");
        });

        // ===== Storage =====
        yield return F("Salvage Crate", "a crate pieced together from three different crates' panels, rope handles, one corner reinforced with a bent road sign, stencils from three owners overlapping, a padlock hasp screwed on and a ratchet strap with a frayed tail.", [-0.45, 0, -0.4], [0.45, 0.8, 0.4], wall: false, draw: k =>
        {
            k.Box(-0.42, 0.0, -0.38, 0.42, 0.75, 0.38, "#9c7a4e", "wood");
            k.Box(-0.4, 0.02, 0.38, 0.0, 0.73, 0.392, "#6f7a5a", "worn paint");
            k.Box(0.0, 0.02, 0.38, 0.4, 0.4, 0.392, "#a86a3a", "painted wood");
            k.Box(0.0, 0.4, 0.38, 0.4, 0.73, 0.392, "#8f6e44", "wood");
            k.Hull([[0.3, 0.5, 0.38], [0.45, 0.5, 0.38], [0.45, 0.78, 0.38], [0.45, 0.78, 0.2], [0.42, 0.5, 0.2], [0.3, 0.5, 0.395], [0.45, 0.5, 0.395], [0.45, 0.78, 0.395]], "#3f6a8a", "worn paint");
            Stencil(k, -0.35, 0.45, -0.1, 0.6, 0.392, Ceramic);
            Stencil(k, 0.05, 0.15, 0.3, 0.25, 0.392, Grime);
            Stencil(k, -0.25, 0.2, -0.05, 0.3, 0.392, "#d9a72a");
            foreach (var sx in new[] { -1.0, 1.0 })
                k.Hull(Frustum(2, sx * 0.43, 0.55, -0.12, 0.02, 0.12, 0.02, 6), "#b39a6a", "fabric");
            k.Box(-0.44, 0.75, -0.4, 0.44, 0.8, 0.4, "#8f6e44", "wood");
            k.Box(-0.05, 0.6, 0.392, 0.05, 0.72, 0.404, Chrome, "steel");
            k.Box(-0.03, 0.57, 0.404, 0.03, 0.62, 0.42, Brass, "brass");
            k.Box(-0.44, 0.0, 0.1, 0.44, 0.82, 0.16, Orange, "fabric");
            k.Hull([[0.44, 0.3, 0.12], [0.46, 0.3, 0.12], [0.44, 0.3, 0.14], [0.46, 0.3, 0.14], [0.5, 0.05, 0.13]], Orange, "fabric");
        });
        yield return F("Oil Drum", "a dented 200-litre drum, its paint half gone to rust, a hand pump screwed in the bung with a hose over the rim, a stencilled warning and a puddle around the base.", [-0.35, 0, -0.35], [0.35, 1.15, 0.35], wall: false, draw: k =>
        {
            k.Box(-0.34, 0.0, -0.34, 0.34, 0.006, 0.34, "#2b2622", "soot");
            k.Hull(Frustum(1, 0, 0, 0.006, 0.28, 0.88, 0.28, 12), "#3f5a72", "rusty paint");
            foreach (var y in new[] { 0.29, 0.58 })
                k.Hull(Frustum(1, 0, 0, y, 0.29, y + 0.025, 0.29, 12), "#3f5a72", "rusty paint");
            k.Hull([[0.1, 0.4, 0.26], [0.25, 0.35, 0.14], [0.22, 0.55, 0.14], [0.08, 0.6, 0.26], [0.17, 0.48, 0.22]], Rust, "rust");
            k.Box(-0.15, 0.55, 0.275, 0.15, 0.7, 0.285, Mustard, "worn paint");
            Tube(k, 1, 0.12, 0.0, 0.88, 1.12, 0.02, Steel, "steel", 6);
            k.Box(0.08, 1.1, -0.015, 0.25, 1.13, 0.015, Signal, "worn paint");
            Pipe(k, Rubber, 0.012, "rubber", (0.12, 1.0, 0.0), (0.12, 1.0, 0.3), (0.12, 0.8, 0.3));
        });
        yield return F("Loaded Shelves", "steel angle shelving bolted to the wall and loaded: tins, a coil of rope, a box of mixed bolts, spare filters, a jerry can and a torch, the top shelf sagging under it all.", [-0.6, 0, -0.25], [0.6, 1.9, 0.15], k =>
        {
            foreach (var x in new[] { -0.58, 0.55 })
                foreach (var z in new[] { -0.23, 0.1 })
                    k.Box(x, 0.0, z, x + 0.03, 1.85, z + 0.03, Steel, "rusty paint");
            foreach (var y in new[] { 0.3, 0.8, 1.3, 1.78 })
                k.Box(-0.58, y, -0.23, 0.58, y + 0.025, 0.13, Steel, "rusty paint");
            for (var i = 0; i < 5; i++)
                k.Hull(Frustum(1, -0.48 + i * 0.09, -0.05, 0.325, 0.04, 0.44, 0.04, 8), i % 2 == 0 ? Signal : "#b4b6b2", "worn paint");
            k.Hull(Frustum(1, 0.3, -0.05, 0.325, 0.15, 0.4, 0.15, 10), "#b39a6a", "fabric");
            k.Box(-0.5, 0.825, -0.2, -0.15, 1.0, 0.1, "#9c7a4e", "wood");
            for (var i = 0; i < 3; i++)
                k.Box(0.0 + i * 0.16, 0.825, -0.15, 0.14 + i * 0.16, 1.1, 0.05, "#c9c0a0", "fabric");
            k.Box(-0.4, 1.325, -0.2, -0.1, 1.65, -0.05, Signal, "rusty paint");
            k.Hull(Frustum(0, 1.37, 0.0, 0.1, 0.035, 0.35, 0.035, 8), Grime, "plastic");
            k.Hull([[-0.55, 1.807, -0.2], [0.55, 1.807, -0.2], [-0.55, 1.807, 0.1], [0.55, 1.807, 0.1], [-0.4, 2.0, -0.1], [0.3, 1.95, 0.0]], "#7f6a50", "fabric");
        });
        yield return F("Tool Chest", "a rolling tool chest with drawers that don't quite shut, stickers from places long gone, a missing castor replaced by a block of wood, a vice clamped to its lid and a coffee ring on top.", [-0.4, 0, -0.3], [0.4, 1.1, 0.3], wall: false, draw: k =>
        {
            foreach (var (x, z) in new[] { (-0.33, -0.22), (0.33, -0.22), (-0.33, 0.22) })
                k.Hull(Frustum(0, 0.04, z, x - 0.015, 0.04, x + 0.015, 0.04, 8), Rubber, "rubber");
            k.Box(0.29, 0.0, 0.18, 0.37, 0.08, 0.26, Wood, "wood");
            Cab(k, -0.38, 0.08, -0.26, 0.38, 0.95, 0.26, Signal, "rusty paint");
            for (var i = 0; i < 6; i++)
            {
                var y = 0.18 + i * 0.12;
                k.Box(-0.34, y, 0.26 + (i == 2 ? 0.04 : 0.0), 0.34, y + 0.1, 0.27 + (i == 2 ? 0.04 : 0.0), "#a8392a", "rusty paint");
                k.Box(-0.1, y + 0.04, 0.27 + (i == 2 ? 0.04 : 0.0), 0.1, y + 0.06, 0.285 + (i == 2 ? 0.04 : 0.0), Chrome, "chrome");
            }
            foreach (var (x, y, c) in new[] { (0.2, 0.4, Mustard), (-0.3, 0.7, Blue), (0.12, 0.82, Ceramic) })
                k.Box(x, y, 0.271, x + 0.09, y + 0.06, 0.278, c, "plastic");
            k.Box(-0.3, 0.95, 0.05, -0.1, 1.03, 0.22, "#3a5a78", "worn paint");
            k.Hull(Frustum(1, 0.15, -0.05, 0.95, 0.05, 0.952, 0.05, 10), "#6a4a2a", "rubber");
        });
        yield return F("Cargo Net", "a cargo net hooked to the wall over a heap of soft bags and boxes, its webbing knotted where it tore, and a carabiner holding one corner instead of the proper hook.", [-0.6, 0, -0.25], [0.6, 1.5, 0.2], k =>
        {
            k.Box(-0.55, 0.0, -0.22, -0.1, 0.5, 0.1, "#9c7a4e", "wood");
            k.Hull([[-0.1, 0.0, -0.22], [0.5, 0.0, -0.22], [-0.1, 0.0, 0.12], [0.5, 0.0, 0.12], [0.0, 0.6, -0.15], [0.4, 0.55, -0.1], [0.2, 0.7, 0.0]], Olive, "fabric");
            k.Box(-0.4, 0.5, -0.2, 0.0, 0.8, 0.05, Blue, "fabric");
            for (var i = 0; i < 6; i++)
                k.Hull([[-0.58 + i * 0.23, 1.4, -0.2], [-0.56 + i * 0.23, 1.4, -0.2], [-0.58 + i * 0.23, 0.0, 0.14], [-0.56 + i * 0.23, 0.0, 0.14], [-0.58 + i * 0.23, 1.4, -0.19], [-0.58 + i * 0.23, 0.0, 0.15]], "#3a3a30", "fabric");
            for (var i = 0; i < 5; i++)
            {
                var y = 0.15 + i * 0.28;
                var z = 0.14 - (y / 1.4) * 0.34 + 0.02;
                k.Box(-0.58, y, z - 0.006, 0.58, y + 0.015, z + 0.006, "#3a3a30", "fabric");
            }
            foreach (var x in new[] { -0.58, 0.0, 0.57 })
                k.Box(x - 0.025, 1.38, -0.25, x + 0.025, 1.44, -0.2, x > 0.5 ? Signal : Grime, "steel");
        });
        yield return F("Parts Bins", "a louvred panel on the wall full of little plastic bins in four colours, labels written on tape, one bin cracked and taped, nuts and washers spilled on the shelf under them.", [-0.5, 0, -0.25], [0.5, 1.4, -0.05], k =>
        {
            k.Box(-0.48, 0.6, -0.25, 0.48, 1.35, -0.22, Steel, "dirty steel");
            string[] cols = [Blue, Mustard, Signal, "#4f7a4a"];
            for (var r = 0; r < 4; r++)
                for (var c = 0; c < 6; c++)
                {
                    double x = -0.44 + c * 0.148, y = 0.65 + r * 0.17;
                    k.Hull([[x, y, -0.22], [x + 0.13, y, -0.22], [x, y + 0.14, -0.22], [x + 0.13, y + 0.14, -0.22], [x, y, -0.08], [x + 0.13, y, -0.08], [x, y + 0.08, -0.08], [x + 0.13, y + 0.08, -0.08]], cols[(r + c) % 4], "plastic");
                    k.Box(x + 0.02, y + 0.03, -0.08, x + 0.11, y + 0.06, -0.072, Tape, "fabric");
                }
            k.Box(-0.48, 0.5, -0.25, 0.48, 0.53, -0.08, WoodDark, "wood");
            for (var i = 0; i < 5; i++)
                k.Box(-0.3 + i * 0.07, 0.53, -0.15 + (i % 2) * 0.03, -0.285 + i * 0.07, 0.545, -0.135 + (i % 2) * 0.03, Chrome, "steel");
        });
        yield return F("Bottle Cage", "a mesh cage of mixed gas bottles chained in, one on its side, their colours faded, a padlock rusted shut and a hand-painted NO SMOKING board wired to the front.", [-0.5, 0, -0.35], [0.5, 1.5, 0.35], wall: false, draw: k =>
        {
            Cage(k, -0.48, 0.0, -0.33, 0.48, 1.45, 0.33, Steel, 0.025);
            for (var i = 0; i < 8; i++)
                Tube(k, 1, -0.4 + i * 0.115, 0.33, 0.03, 1.42, 0.004, Steel, "steel", 4);
            string[] cols = [BottleGreen, "#5d6670", Orange, BottleGreen];
            for (var i = 0; i < 4; i++)
                TankUp(k, -0.33 + i * 0.22, -0.05, 0.02, 1.15 - (i % 2) * 0.12, 0.1, cols[i], "worn paint", 8);
            TankLie(k, 0.12, 0.2, -0.3, 0.3, 0.09, "#8a8a80", "rusty paint");
            k.Box(-0.48, 0.65, 0.33, 0.48, 0.67, 0.35, Chrome, "steel");
            k.Box(-0.25, 0.9, 0.355, 0.25, 1.1, 0.365, Ceramic, "painted wood");
            k.Box(-0.2, 0.97, 0.365, 0.2, 1.03, 0.37, Signal, "worn paint");
            k.Box(0.4, 0.55, 0.35, 0.45, 0.62, 0.37, Rust, "rust");
        });
        yield return F("Kit Bags", "a heap of soft kit bags and a duffel against the wall, one bag patched with a different cloth, a pair of boots tied to a strap and a sleeping roll on top.", [-0.6, 0, -0.35], [0.6, 0.9, 0.35], k =>
        {
            k.Hull([[-0.55, 0.0, -0.3], [0.0, 0.0, -0.3], [-0.55, 0.0, 0.25], [0.0, 0.0, 0.25], [-0.5, 0.35, -0.25], [-0.05, 0.35, -0.25], [-0.5, 0.35, 0.2], [-0.05, 0.38, 0.2]], Olive, "fabric");
            k.Hull(Frustum(0, 0.18, 0.0, 0.0, 0.18, 0.55, 0.18, 8), Blue, "fabric");
            k.Box(0.2, 0.25, 0.15, 0.35, 0.33, 0.18, Mustard, "fabric");
            k.Hull(Frustum(0, 0.47, -0.05, -0.45, 0.12, 0.3, 0.12, 8), "#7f3a2a", "fabric");
            k.Hull(Frustum(0, 0.67, -0.05, -0.4, 0.07, 0.2, 0.07, 8), "#6b7a6a", "fabric");
            foreach (var x in new[] { 0.35, 0.47 })
                k.Box(x, 0.4, 0.1, x + 0.1, 0.62, 0.3, "#4a3626", "rubber");
        });

        // ===== Maintenance =====
        yield return F("Door Workbench", "a workbench made from an old steel door laid on two oil drums, a vice bolted through it, a bench grinder with no guard, a coffee can of bolts, a welding scar across the top and rust everywhere the paint gave up.", [-0.85, 0, -0.4], [0.85, 1.3, 0.4], k =>
        {
            foreach (var x in new[] { -0.55, 0.55 })
            {
                k.Hull(Frustum(1, x, 0.0, 0.0, 0.27, 0.82, 0.27, 12), x < 0 ? "#3f5a72" : Signal, "rusty paint");
                k.Hull(Frustum(1, x, 0.0, 0.4, 0.28, 0.425, 0.28, 12), x < 0 ? "#3f5a72" : Signal, "rusty paint");
            }
            k.Box(-0.85, 0.82, -0.4, 0.85, 0.87, 0.4, "#8a8d82", "rusty paint");
            PanelZ(k, -0.7, 0.871, 0.7, 0.88, 0.3, "#8a8d82");
            k.Box(-0.4, 0.87, -0.2, 0.3, 0.875, -0.17, Grime, "soot");
            k.Box(0.2, 0.86, 0.2, 0.3, 0.88, 0.3, Steel, "steel");
            k.Box(-0.8, 0.87, 0.2, -0.6, 0.95, 0.38, "#3a5a78", "rusty paint");
            k.Box(-0.77, 0.95, 0.24, -0.63, 1.03, 0.28, "#3a5a78", "rusty paint");
            k.Box(-0.77, 0.95, 0.32, -0.63, 1.03, 0.36, "#3a5a78", "rusty paint");
            k.Box(0.4, 0.87, -0.15, 0.6, 1.02, 0.05, "#4f6a7a", "worn paint");
            foreach (var x in new[] { 0.37, 0.63 })
                k.Hull(Frustum(0, 0.98, -0.05, x - 0.02, 0.08, x + 0.02, 0.08, 10), "#9a9a90", "steel");
            k.Hull(Frustum(1, -0.15, 0.1, 0.87, 0.06, 1.0, 0.06, 8), "#b4b6b2", "rusty paint");
            StreakZ(k, 0.6, 0.0, 0.7, 0.27, 0.04);
        });
        yield return F("Welding Cart", "a welding set on a home-made cart, two bottles chained on the back, the leads coiled over the handle, a mask hanging off it with its glass scratched and a tin of rods taped to the side.", [-0.4, 0, -0.4], [0.4, 1.4, 0.4], wall: false, draw: k =>
        {
            foreach (var x in new[] { -0.33, 0.33 })
                k.Hull(Frustum(0, 0.1, -0.25, x - 0.03, 0.1, x + 0.03, 0.1, 10), Rubber, "rubber");
            k.Box(-0.33, 0.12, -0.35, 0.33, 0.16, 0.35, Steel, "rusty paint");
            Cab(k, -0.3, 0.16, 0.0, 0.3, 0.6, 0.33, "#2e5a8a", "rusty paint");
            DialZ(k, -0.12, 0.45, 0.33, 0.05, 0.6);
            k.Hull(Frustum(2, 0.12, 0.45, 0.33, 0.03, 0.36, 0.03, 8), Rubber, "plastic");
            TankUp(k, -0.15, -0.2, 0.16, 1.25, 0.11, BottleGreen);
            TankUp(k, 0.15, -0.2, 0.16, 1.05, 0.09, "#5d6670");
            k.Box(-0.33, 0.8, -0.34, 0.33, 0.82, -0.31, Chrome, "steel");
            foreach (var sx in new[] { -0.32, 0.32 })
                Tube(k, 1, sx, -0.33, 0.16, 1.3, 0.015, Steel, "steel", 6);
            Tube(k, 0, 1.3, -0.33, -0.32, 0.32, 0.015, Steel, "steel", 6);
            Ring(k, 2, 0.0, 1.15, -0.33, 0.13, 0.03, Rubber, "rubber", 6);
            k.Hull([[0.2, 0.95, -0.36], [0.33, 0.95, -0.36], [0.2, 1.22, -0.36], [0.33, 1.22, -0.36], [0.22, 0.98, -0.42], [0.31, 0.98, -0.42], [0.22, 1.2, -0.42], [0.31, 1.2, -0.42]], Grime, "plastic");
            k.Box(0.3, 0.2, 0.15, 0.33, 0.45, 0.25, "#8a8a80", "steel");
            k.Box(0.29, 0.3, 0.14, 0.34, 0.34, 0.26, Tape, "fabric");
        });
        yield return F("Air Compressor", "a compressor tank on two wheels with a motor bolted on top, the belt guard missing, a pressure dial, a coiled air line on the handle and oil sprayed round the drain.", [-0.5, 0, -0.3], [0.5, 0.9, 0.3], wall: false, draw: k =>
        {
            TankLie(k, 0.3, 0.0, -0.45, 0.4, 0.22, Signal, "rusty paint");
            foreach (var x in new[] { 0.35 })
                foreach (var z in new[] { -0.22, 0.22 })
                    k.Hull(Frustum(2, x, 0.11, z - 0.03, 0.11, z + 0.03, 0.11, 10), Rubber, "rubber");
            k.Box(-0.4, 0.0, -0.05, -0.35, 0.1, 0.05, Grime, "steel");
            k.Box(-0.3, 0.5, -0.15, 0.3, 0.54, 0.15, Grime, "steel");
            k.Hull(Frustum(0, 0.64, 0.0, -0.28, 0.1, 0.0, 0.1, 10), "#4f6a7a", "worn paint");
            k.Box(0.05, 0.54, -0.1, 0.25, 0.75, 0.1, Steel, "dirty steel");
            for (var i = 0; i < 4; i++)
                k.Box(0.03, 0.62 + i * 0.03, -0.12, 0.27, 0.635 + i * 0.03, 0.12, Steel, "dirty steel");
            k.Hull([[0.0, 0.58, 0.12], [0.02, 0.58, 0.12], [0.0, 0.72, 0.12], [0.02, 0.72, 0.12], [0.0, 0.58, 0.14], [0.02, 0.58, 0.14], [0.0, 0.72, 0.14], [0.02, 0.72, 0.14]], Rubber, "rubber");
            DialZ(k, -0.3, 0.6, 0.15, 0.04, 0.7);
            Tube(k, 1, -0.48, 0.0, 0.3, 0.85, 0.015, Chrome, "steel", 6);
            Ring(k, 0, 0.7, 0.0, -0.49, 0.1, 0.025, Blue, "rubber", 6);
            k.Box(-0.2, 0.0, -0.2, 0.2, 0.004, 0.2, "#2b2622", "soot");
        });
        yield return F("Bottle Jack Press", "an H-frame press welded from channel, a bottle jack doing the pressing, a bent piece still in it, scrap blocks of wood to set it up on and a hammer resting on the bed.", [-0.4, 0, -0.3], [0.4, 1.6, 0.3], wall: false, draw: k =>
        {
            foreach (var sx in new[] { -0.35, 0.35 })
            {
                k.Box(sx - 0.04, 0.0, -0.06, sx + 0.04, 1.55, 0.06, "#2e5a8a", "rusty paint");
                k.Box(sx - 0.05, 0.0, -0.25, sx + 0.05, 0.05, 0.25, "#2e5a8a", "rusty paint");
            }
            k.Box(-0.31, 1.45, -0.06, 0.31, 1.55, 0.06, "#2e5a8a", "rusty paint");
            k.Box(-0.31, 0.55, -0.08, 0.31, 0.62, 0.08, "#2e5a8a", "rusty paint");
            k.Hull(Frustum(1, 0, 0, 1.0, 0.07, 1.28, 0.07, 8), Signal, "worn paint");
            Tube(k, 1, 0, 0, 1.28, 1.45, 0.025, Chrome, "steel");
            k.Box(-0.08, 0.92, -0.08, 0.08, 1.0, 0.08, Steel, "steel");
            k.Hull([[-0.25, 0.62, -0.03], [0.25, 0.62, -0.03], [-0.25, 0.65, -0.03], [0.25, 0.65, -0.03], [-0.05, 0.7, -0.03], [0.05, 0.7, -0.03], [-0.25, 0.62, 0.03], [0.25, 0.62, 0.03], [-0.05, 0.7, 0.03], [0.05, 0.7, 0.03]], "#9a9a90", "steel");
            k.Box(-0.2, 0.62, 0.1, 0.0, 0.7, 0.2, Wood, "wood");
            k.Box(0.05, 0.62, 0.1, 0.3, 0.64, 0.14, Wood, "wood");
            k.Box(0.25, 0.62, 0.1, 0.32, 0.67, 0.18, Grime, "steel");
        });
        yield return F("Fire Extinguisher", "a red extinguisher on a wall bracket, its paint chipped, the pin held with a twist of wire, the gauge in the red, an inspection tag years out of date and a strip of tape over a dent.", [-0.15, 0, -0.25], [0.15, 0.9, -0.05], k =>
        {
            k.Box(-0.08, 0.35, -0.25, 0.08, 0.45, -0.22, Grime, "steel");
            TankUp(k, 0.0, -0.14, 0.2, 0.72, 0.075, Signal, "worn paint", 10);
            k.Box(-0.03, 0.72, -0.17, 0.03, 0.8, -0.11, Grime, "steel");
            k.Hull([[-0.01, 0.78, -0.166], [0.01, 0.78, -0.166], [-0.01, 0.8, -0.166], [0.01, 0.8, -0.166], [-0.01, 0.8, -0.03], [0.01, 0.8, -0.03], [-0.01, 0.82, -0.03], [0.01, 0.82, -0.03]], Grime, "steel");
            DialZ(k, 0.04, 0.76, -0.11, 0.015, 0.1);
            Pipe(k, Rubber, 0.01, "rubber", (-0.03, 0.75, -0.14), (-0.09, 0.75, -0.14), (-0.09, 0.3, -0.14));
            k.Box(0.06, 0.5, -0.08, 0.11, 0.58, -0.07, Ceramic, "plastic");
            k.Box(-0.07, 0.4, -0.074, 0.03, 0.44, -0.063, Tape, "fabric");
        });
        yield return F("First Aid Box", "a steel first aid cabinet with a faded cross, its door hanging on one hinge, a roll of tape and a bottle visible inside, and a hand-written list of what was taken stuck to the side.", [-0.25, 0, -0.25], [0.25, 0.9, 0.1], k =>
        {
            k.Box(-0.22, 0.35, -0.25, 0.22, 0.8, -0.1, "#d0c8b0", "rusty paint");
            k.Box(-0.2, 0.37, -0.1, 0.2, 0.78, -0.098, "#4a4540", "dirty steel");
            k.Box(-0.18, 0.55, -0.24, 0.18, 0.565, -0.1, "#b8b0a0", "steel");
            k.Hull(Frustum(1, -0.1, -0.16, 0.565, 0.035, 0.6, 0.035, 8), Ceramic, "fabric");
            k.Hull(Frustum(1, 0.08, -0.17, 0.565, 0.03, 0.66, 0.025, 8), "#7a4a2c", "plastic");
            k.Hull([[-0.22, 0.37, -0.1], [-0.2, 0.37, -0.1], [-0.22, 0.78, -0.1], [-0.2, 0.78, -0.1], [-0.06, 0.35, 0.08], [-0.04, 0.35, 0.08], [-0.06, 0.76, 0.08], [-0.04, 0.76, 0.08]], "#d0c8b0", "rusty paint");
            k.Hull([[-0.18, 0.5, -0.07], [-0.16, 0.5, -0.07], [-0.18, 0.66, -0.07], [-0.16, 0.66, -0.07], [-0.1, 0.5, 0.01], [-0.08, 0.5, 0.01], [-0.1, 0.66, 0.01], [-0.08, 0.66, 0.01]], "#b8443a", "worn paint");
            k.Box(0.22, 0.45, -0.22, 0.23, 0.65, -0.12, Ceramic, "plastic");
        });
        yield return F("Spares Board", "a pegboard on the wall hung with salvaged spares: a fan belt, two gears, a coil of wire, a bearing, an old pump impeller, a spark plug and outlines painted round the gaps where things are missing.", [-0.5, 0, -0.25], [0.5, 1.5, -0.1], k =>
        {
            k.Box(-0.48, 0.6, -0.25, 0.48, 1.45, -0.23, "#b39a72", "painted wood");
            k.Hull(Frustum(2, -0.3, 1.2, -0.22, 0.11, -0.2, 0.11, 10), Rubber, "rubber");
            k.Hull(Frustum(2, -0.3, 1.2, -0.225, 0.09, -0.195, 0.09, 10), "#a08a6a", "painted wood");
            foreach (var (x, y, r) in new[] { (0.0, 1.25, 0.08), (0.15, 1.12, 0.05) })
            {
                k.Hull(Frustum(2, x, y, -0.23, r, -0.21, r, 8, 0.0), "#7d8085", "steel");
                k.Hull(Frustum(2, x, y, -0.23, r * 1.15, -0.215, r * 1.15, 8, 0.5), "#7d8085", "steel");
            }
            Ring(k, 2, 0.32, 1.25, -0.215, 0.07, 0.03, Copper, "copper", 8);
            k.Hull(Frustum(2, -0.25, 0.85, -0.23, 0.06, -0.19, 0.06, 10), Chrome, "chrome");
            k.Hull(Frustum(2, 0.05, 0.85, -0.23, 0.09, -0.2, 0.09, 6), Brass, "brass");
            k.Hull(Frustum(1, 0.3, -0.21, 0.78, 0.012, 0.9, 0.012, 6), Ceramic, "gloss paint");
            Ring(k, 2, 0.32, 0.95, -0.229, 0.05, 0.008, Ceramic, "worn paint", 6);
            Ring(k, 2, -0.3, 0.65, -0.229, 0.05, 0.008, Ceramic, "worn paint", 6);
        });
        yield return F("Trash Compactor", "a box compactor with a hand crank on the side, a lid that has been kicked shut so often it's bent, a bin bag hanging out and a sign about what not to put in it.", [-0.35, 0, -0.35], [0.45, 1.1, 0.35], wall: false, draw: k =>
        {
            Cab(k, -0.33, 0.0, -0.33, 0.33, 0.95, 0.33, Avocado, "rusty paint");
            k.Hull([[-0.34, 0.95, -0.34], [0.34, 0.95, -0.34], [-0.34, 0.95, 0.34], [0.34, 0.95, 0.34], [-0.34, 0.99, -0.34], [0.34, 0.99, -0.34], [-0.34, 0.97, 0.34], [0.34, 1.0, 0.34], [0.0, 0.96, 0.1]], Avocado, "rusty paint");
            k.Hull([[-0.2, 0.9, 0.33], [0.1, 0.9, 0.33], [-0.2, 0.6, 0.36], [0.05, 0.55, 0.38], [-0.1, 0.75, 0.4]], Rubber, "plastic");
            Tube(k, 0, 0.6, 0.0, 0.33, 0.4, 0.025, Chrome, "steel");
            k.Box(0.4, 0.45, -0.02, 0.43, 0.75, 0.02, Chrome, "steel");
            Tube(k, 0, 0.47, 0.0, 0.43, 0.52, 0.015, Brown, "plastic", 6);
            k.Box(-0.2, 0.3, 0.33, 0.2, 0.45, 0.34, Ceramic, "painted wood");
        });
        yield return F("Med Cot", "a folding stretcher on a welded stand, canvas stained and patched, a drip bag hung from a coat-hanger hook on a broom handle, a blanket folded at one end and a torch on a string.", [-1.0, 0, -0.35], [1.0, 1.7, 0.35], wall: false, draw: k =>
        {
            foreach (var x in new[] { -0.85, 0.85 })
                k.Box(x - 0.03, 0.0, -0.3, x + 0.03, 0.6, 0.3, Steel, "rusty paint");
            foreach (var z in new[] { -0.28, 0.28 })
                Tube(k, 0, 0.62, z, -1.0, 1.0, 0.018, Wood, "wood", 6);
            k.Box(-0.95, 0.6, -0.27, 0.95, 0.625, 0.27, Olive, "fabric");
            k.Box(-0.2, 0.625, -0.1, 0.1, 0.63, 0.12, "#5a4128", "fabric");
            k.Box(0.6, 0.625, -0.25, 0.9, 0.7, 0.25, "#8f5a3a", "fabric");
            Tube(k, 1, -0.95, 0.32, 0.02, 1.6, 0.012, "#9c7a4e", "wood", 6);
            k.Box(-0.98, 0.0, 0.25, -0.92, 0.04, 0.4, Grime, "steel");
            k.Hull([[-0.95, 1.6, 0.32], [-0.945, 1.6, 0.32], [-0.95, 1.6, 0.2], [-0.95, 1.62, 0.2], [-0.945, 1.62, 0.2]], Chrome, "steel");
            k.Hull([[-1.0, 1.55, 0.18], [-0.9, 1.55, 0.18], [-1.0, 1.55, 0.22], [-0.9, 1.55, 0.22], [-0.98, 1.35, 0.19], [-0.92, 1.35, 0.19], [-0.98, 1.35, 0.21], [-0.92, 1.35, 0.21]], "#d6e8e4", "plastic");
            Tube(k, 1, -0.95, 0.2, 0.7, 1.35, 0.003, Ceramic, "plastic", 4);
        });
        yield return F("Grow Tray", "a hydroponic tray in a frame of angle and plywood, leafy plants in cut-down bottles, a warm grow lamp on a chain, a pump in a bucket and a sign naming each plant in marker.", [-0.6, 0, -0.35], [0.6, 1.6, 0.35], wall: false, draw: k =>
        {
            foreach (var x in new[] { -0.55, 0.52 })
                foreach (var z in new[] { -0.32, 0.29 })
                    k.Box(x, 0.0, z, x + 0.03, 1.55, z + 0.03, Steel, "rusty paint");
            k.Box(-0.55, 0.6, -0.32, 0.55, 0.75, 0.32, "#7f6a50", "painted wood");
            k.Box(-0.5, 0.75, -0.27, 0.5, 0.76, 0.27, "#3a4a42", "plastic");
            for (var i = 0; i < 5; i++)
                for (var j = 0; j < 2; j++)
                {
                    double x = -0.4 + i * 0.2, z = -0.12 + j * 0.24;
                    k.Hull(Frustum(1, x, z, 0.76, 0.05, 0.86, 0.05, 6), "#b8c8c2", "plastic");
                    k.Hull([[x - 0.08, 0.9, z], [x + 0.08, 0.9, z], [x, 0.9, z - 0.08], [x, 0.9, z + 0.08], [x, 0.86, z], [x + 0.02 * (i % 2), 1.05 + 0.04 * ((i + j) % 3), z]], (i + j) % 2 == 0 ? "#4f7a3a" : "#6a8a3a", "plastic");
                }
            k.Box(-0.55, 1.52, -0.32, 0.55, 1.55, 0.32, Steel, "rusty paint");
            Tube(k, 1, 0.0, 0.0, 1.35, 1.52, 0.004, Chrome, "steel", 4);
            k.Box(-0.3, 1.3, -0.08, 0.3, 1.35, 0.08, Grime, "steel");
            k.Glow(-0.27, 1.29, -0.05, 0.27, 1.3, 0.05, Bulb);
            k.Hull(Frustum(1, 0.35, 0.0, 0.0, 0.1, 0.28, 0.11, 8), Blue, "plastic");
            Pipe(k, "#4a4a4a", 0.008, "rubber", (0.35, 0.28, 0.0), (0.35, 0.4, 0.0), (0.45, 0.4, 0.0), (0.45, 0.745, 0.0));
            k.Box(-0.4, 0.62, 0.32, 0.1, 0.7, 0.33, Ceramic, "plastic");
        });

        // ===== More fittings (to round out the hundred) =====
        yield return F("Coat Hooks", "a row of hooks screwed to a strip of wood, coats and a hi-vis vest hung three deep, a helmet on one hook and a towel fallen half off.", [-0.5, 0, -0.25], [0.5, 1.9, 0.0], k =>
        {
            k.Box(-0.48, 1.6, -0.25, 0.48, 1.68, -0.22, WoodDark, "wood");
            for (var i = 0; i < 5; i++)
                k.Box(-0.4 + i * 0.2 - 0.008, 1.6, -0.22, -0.4 + i * 0.2 + 0.008, 1.63, -0.17, Chrome, "steel");
            foreach (var (x, c, d) in new[] { (-0.4, Olive, 0.0), (-0.2, "#c9d23a", 0.012), (0.2, Blue, 0.006) })
                k.Hull([[x - 0.12, 1.62 - d, -0.2 + d], [x + 0.12, 1.62 - d, -0.2 + d], [x - 0.15, 0.9 + d, -0.22 + d], [x + 0.15, 0.9 + d, -0.22 + d], [x - 0.12, 1.6 - d, -0.08 + d], [x + 0.12, 1.6 - d, -0.08 + d], [x - 0.14, 0.95 + d, -0.06 + d], [x + 0.14, 0.95 + d, -0.06 + d]], c, "fabric");
            k.Hull(Frustum(1, 0.0, -0.12, 1.5, 0.12, 1.62, 0.09, 8), Mustard, "gloss paint");
            k.Hull([[0.335, 1.62, -0.196], [0.47, 1.62, -0.196], [0.335, 1.2, -0.205], [0.47, 1.0, -0.205], [0.335, 1.62, -0.176], [0.47, 1.62, -0.176], [0.335, 1.2, -0.185], [0.47, 1.0, -0.185]], Orange, "fabric");
        });
        yield return F("Wall Clock", "a round wall clock with a cracked face, stuck at twenty past something, a nail through its hanger and a calendar from years ago pinned under it.", [-0.2, 0, -0.25], [0.2, 1.8, -0.15], k =>
        {
            k.Hull(Frustum(2, 0.0, 1.55, -0.25, 0.16, -0.21, 0.16, 12), Cream, "worn paint");
            k.Hull(Frustum(2, 0.0, 1.55, -0.21, 0.13, -0.205, 0.13, 12), Ceramic, "plastic");
            k.Box(-0.004, 1.55, -0.205, 0.004, 1.65, -0.198, Grime, "plastic");
            k.Hull([[0.0, 1.548, -0.205], [0.0, 1.556, -0.205], [0.06, 1.52, -0.205], [0.0, 1.548, -0.198], [0.06, 1.52, -0.198]], Grime, "plastic");
            k.Hull([[-0.08, 1.62, -0.204], [-0.07, 1.62, -0.204], [0.02, 1.5, -0.204], [-0.08, 1.62, -0.2], [0.02, 1.5, -0.2]], "#8a8070", "plastic");
            k.Box(-0.13, 1.0, -0.25, 0.13, 1.32, -0.245, Ceramic, "plastic");
            k.Box(-0.12, 1.2, -0.245, 0.12, 1.31, -0.242, "#6a8aa0", "plastic");
        });
        yield return F("Mirror Shard", "a broken mirror screwed to the wall with three of its corners left, a razor and a comb on a tin ledge below and a photo tucked in the frame.", [-0.2, 0, -0.25], [0.2, 1.8, -0.15], k =>
        {
            k.Box(-0.18, 1.2, -0.25, 0.18, 1.7, -0.235, Steel, "rusty paint");
            k.Hull([[-0.16, 1.22, -0.235], [0.15, 1.22, -0.235], [0.16, 1.5, -0.235], [-0.05, 1.68, -0.235], [-0.16, 1.6, -0.235], [-0.16, 1.22, -0.228], [0.15, 1.22, -0.228], [0.16, 1.5, -0.228], [-0.05, 1.68, -0.228], [-0.16, 1.6, -0.228]], "#aab6b8", "chrome");
            k.Box(-0.17, 1.15, -0.25, 0.17, 1.17, -0.17, "#a0a6a8", "steel");
            k.Box(-0.1, 1.17, -0.22, 0.0, 1.18, -0.2, Grime, "plastic");
            k.Box(0.04, 1.17, -0.21, 0.12, 1.18, -0.19, Brown, "plastic");
            k.Box(0.08, 1.55, -0.228, 0.16, 1.66, -0.222, Ceramic, "plastic");
        });
        yield return F("Mop Bucket", "a mop bucket on wonky wheels with a wringer, a mop with half its strings gone, grey water in it and a wet floor sign with the leg taped.", [-0.35, 0, -0.3], [0.45, 1.3, 0.3], wall: false, draw: k =>
        {
            k.Box(-0.25, 0.04, -0.2, 0.25, 0.4, 0.2, Mustard, "plastic");
            k.Box(-0.22, 0.38, -0.17, 0.22, 0.385, 0.17, "#6a6a5e", "plastic");
            foreach (var (x, z) in new[] { (-0.2, -0.15), (0.2, -0.15), (-0.2, 0.15), (0.2, 0.15) })
                k.Hull(Frustum(1, x, z, 0.0, 0.025, 0.04, 0.025, 6), Grime, "rubber");
            k.Box(0.05, 0.4, -0.15, 0.25, 0.55, 0.15, Grime, "plastic");
            Tube(k, 1, -0.1, 0.0, 0.2, 1.25, 0.012, "#9c7a4e", "wood", 6);
            k.Hull(Frustum(1, -0.1, 0.0, 0.3, 0.06, 0.405, 0.04, 6), "#c9c0a0", "fabric");
            k.Hull([[0.3, 0.0, -0.15], [0.45, 0.0, -0.15], [0.3, 0.0, 0.15], [0.45, 0.0, 0.15], [0.37, 0.65, -0.12], [0.39, 0.65, -0.12], [0.37, 0.65, 0.12], [0.39, 0.65, 0.12]], Mustard, "plastic");
            k.Box(0.36, 0.2, -0.05, 0.42, 0.25, 0.05, Tape, "fabric");
        });
        yield return F("Laundry Line", "a washing line strung between two eye bolts across a corner, socks, a shirt and overalls pegged on it with mismatched pegs, and a drip stain on the deck under them.", [-0.9, 0, -0.25], [0.9, 2.0, 0.1], k =>
        {
            foreach (var x in new[] { -0.88, 0.88 })
                k.Box(x - 0.02, 1.78, -0.25, x + 0.02, 1.82, -0.18, Chrome, "steel");
            Tube(k, 0, 1.79, -0.2, -0.88, 0.88, 0.004, Ceramic, "fabric", 4);
            foreach (var (x0, x1, y, c) in new[] { (-0.7, -0.6, 1.55, Blue), (-0.55, -0.47, 1.6, Signal), (-0.35, 0.05, 1.25, "#6b7a8a"), (0.15, 0.55, 1.1, Orange) })
            {
                k.Box(x0, y, -0.21, x1, 1.79, -0.19, c, "fabric");
                k.Box(x0 + 0.01, 1.77, -0.215, x0 + 0.03, 1.83, -0.185, x0 < 0 ? Wood : Mustard, "plastic");
            }
            k.Box(0.15, 0.0, -0.25, 0.55, 0.002, 0.05, "#3a3632", "soot");
        });
        yield return F("Plant Pot", "a leafy plant in a dented paint tin, its leaves tied up with string to a wire, a watering can made from a juice bottle beside it and a hand-written name stuck in the soil.", [-0.25, 0, -0.25], [0.3, 1.0, 0.25], wall: false, draw: k =>
        {
            k.Hull(Frustum(1, 0.0, 0.0, 0.0, 0.14, 0.28, 0.14, 10), "#6b7a8a", "rusty paint");
            k.Hull(Frustum(1, 0.0, 0.0, 0.27, 0.13, 0.285, 0.13, 10), "#4a3626", "dirt");
            for (var i = 0; i < 6; i++)
            {
                var a = i * Math.PI / 3;
                k.Hull([[0.0, 0.3, 0.0], [0.03, 0.3, 0.0], [0.25 * Math.Cos(a), 0.55 + 0.08 * (i % 3), 0.25 * Math.Sin(a)], [0.25 * Math.Cos(a + 0.4), 0.6 + 0.08 * (i % 3), 0.25 * Math.Sin(a + 0.4)], [0.12 * Math.Cos(a + 0.2), 0.7 + 0.08 * (i % 3), 0.12 * Math.Sin(a + 0.2)]], i % 2 == 0 ? "#4f7a3a" : "#6a8a3a", "plastic");
            }
            Tube(k, 1, -0.04, 0.03, 0.28, 0.95, 0.004, Steel, "steel", 4);
            k.Box(0.05, 0.28, 0.06, 0.1, 0.38, 0.065, Ceramic, "plastic");
            k.Hull(Frustum(1, 0.22, 0.12, 0.0, 0.05, 0.22, 0.05, 8), "#e0a040", "plastic");
        });
        yield return F("Dart Board", "a dart board on a scrap of cork, darts stuck in everywhere but the middle, a chalk scoreboard beside it with two names and a long tally, and holes in the wall around where it missed.", [-0.35, 0, -0.25], [0.35, 1.9, -0.15], k =>
        {
            k.Box(-0.3, 1.25, -0.25, 0.3, 1.75, -0.235, "#9a7a5a", "fabric");
            k.Hull(Frustum(2, -0.08, 1.5, -0.235, 0.18, -0.205, 0.18, 12), Grime, "fabric");
            k.Hull(Frustum(2, -0.08, 1.5, -0.205, 0.12, -0.2, 0.12, 12), "#c9b48a", "fabric");
            k.Hull(Frustum(2, -0.08, 1.5, -0.2, 0.025, -0.196, 0.025, 8), Signal, "fabric");
            foreach (var (x, y) in new[] { (-0.15, 1.58), (0.02, 1.4), (-0.18, 1.38) })
                Tube(k, 2, x, y, -0.205, -0.13, 0.004, Chrome, "steel", 4);
            k.Box(0.14, 1.3, -0.235, 0.29, 1.7, -0.228, "#2c3a30", "matte paint");
            for (var i = 0; i < 6; i++)
                k.Box(0.17 + (i % 3) * 0.035, 1.4 + (i / 3) * 0.15, -0.228, 0.18 + (i % 3) * 0.035, 1.5 + (i / 3) * 0.15, -0.224, Ceramic, "plastic");
        });
        yield return F("Bike Trainer", "an exercise bike bodged from a bicycle frame on a stand, its wheel turning a little generator, a seat with tape on it, a rag on the bars and a lamp it lights when you pedal.", [-0.3, 0, -0.7], [0.3, 1.3, 0.7], wall: false, draw: k =>
        {
            k.Box(-0.25, 0.0, -0.6, 0.25, 0.04, 0.6, Steel, "rusty paint");
            k.Hull(Frustum(0, 0.38, 0.35, -0.02, 0.33, 0.02, 0.33, 12), Rubber, "rubber");
            k.Hull(Frustum(0, 0.38, 0.35, -0.025, 0.06, 0.025, 0.06, 8), Chrome, "steel");
            k.Hull([[-0.019, 0.4, -0.3], [0.019, 0.4, -0.3], [-0.019, 0.43, -0.3], [0.019, 0.43, -0.3], [-0.019, 0.9, 0.3], [0.019, 0.9, 0.3], [-0.019, 0.93, 0.3], [0.019, 0.93, 0.3]], "#8a3a3a", "worn paint");
            k.Hull([[-0.016, 0.38, 0.35], [0.016, 0.38, 0.35], [-0.016, 0.38, 0.38], [0.016, 0.38, 0.38], [-0.016, 0.95, -0.25], [0.016, 0.95, -0.25], [-0.016, 0.95, -0.22], [0.016, 0.95, -0.22]], "#8a3a3a", "worn paint");
            k.Hull([[-0.013, 0.04, -0.4], [0.013, 0.04, -0.4], [-0.013, 0.04, -0.2], [0.013, 0.04, -0.2], [-0.013, 0.42, -0.3], [0.013, 0.42, -0.3]], Steel, "rusty paint");
            k.Hull([[-0.1, 0.95, -0.32], [0.1, 0.95, -0.32], [-0.05, 0.95, -0.12], [0.05, 0.95, -0.12], [-0.1, 1.0, -0.32], [0.1, 1.0, -0.32], [-0.05, 0.99, -0.12], [0.05, 0.99, -0.12]], Rubber, "rubber");
            Tube(k, 0, 0.95, 0.3, -0.25, 0.25, 0.012, Chrome, "steel", 6);
            k.Box(0.05, 0.9, 0.28, 0.2, 0.95, 0.32, Orange, "fabric");
            k.Hull(Frustum(0, 0.38, 0.35, 0.03, 0.05, 0.12, 0.05, 8), "#4f6a7a", "worn paint");
            CagedBulbZ(k, 0.2, 0.05, 0.55);
        });
        yield return F("Tea Chest", "a plywood tea chest used as a table, a lamp made from a bottle on it, a cracked mug, a paperback face down and a cloth thrown over half of it.", [-0.3, 0, -0.3], [0.3, 1.0, 0.3], wall: false, draw: k =>
        {
            k.Box(-0.28, 0.0, -0.28, 0.28, 0.55, 0.28, "#b48a5a", "wood");
            foreach (var sx in new[] { -1.0, 1.0 })
                k.Box(sx * 0.28 - (sx > 0 ? 0.03 : 0), 0.0, -0.285, sx * 0.28 + (sx < 0 ? 0.03 : 0), 0.55, 0.285, Steel, "steel");
            k.Hull([[-0.29, 0.55, -0.29], [0.1, 0.55, -0.29], [-0.29, 0.55, 0.29], [0.1, 0.55, 0.29], [-0.29, 0.56, -0.29], [0.1, 0.56, -0.29], [-0.29, 0.56, 0.29], [0.1, 0.56, 0.29], [-0.3, 0.3, 0.0], [-0.3, 0.4, 0.2]], "#7a5a8a", "fabric");
            k.Hull(Frustum(1, 0.15, -0.1, 0.55, 0.045, 0.75, 0.04, 8), "#4a6a4a", "plastic");
            k.Hull(Frustum(1, 0.15, -0.1, 0.75, 0.1, 0.9, 0.06, 8), "#e0c98e", "fabric");
            k.Hull(Frustum(1, 0.15, -0.1, 0.76, 0.03, 0.85, 0.03, 6), Bulb, "", glow: true);
            k.Hull(Frustum(1, -0.1, 0.12, 0.56, 0.035, 0.65, 0.035, 8), Ceramic, "gloss paint");
            k.Box(0.0, 0.55, 0.05, 0.18, 0.58, 0.2, Signal, "plastic");
        });
        yield return F("Fuse Spares Tin", "a biscuit tin screwed to the wall as a box for spare fuses and bulbs, its lid on a string, a picture of a cottage half rubbed off, and a torch on a hook beside it.", [-0.2, 0, -0.25], [0.2, 1.2, -0.12], k =>
        {
            k.Box(-0.15, 0.9, -0.25, 0.15, 1.1, -0.17, "#3f6a8a", "worn paint");
            k.Box(-0.13, 0.95, -0.17, 0.05, 1.05, -0.165, "#c9b48a", "worn paint");
            k.Box(-0.16, 1.1, -0.255, 0.16, 1.12, -0.165, "#3f6a8a", "worn paint");
            k.Box(0.17, 0.95, -0.25, 0.19, 0.98, -0.2, Chrome, "steel");
            k.Hull(Frustum(1, 0.18, -0.18, 0.75, 0.025, 0.95, 0.025, 8), Signal, "plastic");
            k.Hull(Frustum(1, 0.18, -0.18, 0.72, 0.03, 0.75, 0.03, 8), Chrome, "chrome");
        });
    }

    private const string HazardYStr = "#d9a72a";
}
