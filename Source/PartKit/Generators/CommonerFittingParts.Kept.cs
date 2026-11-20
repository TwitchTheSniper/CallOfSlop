using static CallOfSlop.PartKit.PartKitMath;
using K = CallOfSlop.PartKit.Generators.FittingLookParts;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The commoner fittings Karl kept from the style sheet (picked in round 2, polished in round 3), and the kit they are drawn with.
/// </summary>
public sealed partial class CommonerFittingParts
{
    private const string Copper = "#b06f3d", Brass = "#9a6a34", Steel = "#7d8085", Lagging = "#b9b39c", Blue = "#3f5a72", Signal = "#c23b2a";

    // ---- Round 2's kit

    /// <summary>A free-standing fitting.</summary>
    private static Part F(string name, string desc, double[] mn, double[] mx, Action<K.Kit> draw, bool wall = true, string role = "frame")
    {
        var k = Free(name, desc, mn, mx, role: role, wall: wall);
        draw(k);
        return k.P;
    }

    /// <summary>A module on the standard Module Plug; draw gets the half width inside the corner lugs.</summary>
    private static Part M(string name, string desc, int n, double height, string[] ports, Action<K.Kit, double> draw)
    {
        var k = K.Reactor("", n, desc, height, name, Section, ports, group: Group);
        draw(k, n / 2.0 - 0.124);
        return k.P;
    }

    /// <summary>A storage fitting: a hollow hidden shell t thick round the bounds (what a hand or a round meets) with one slot inside.</summary>
    private static Part Store(string name, string desc, double[] mn, double[] mx, Action<K.Kit> draw, bool wall = true, double t = 0.03)
    {
        var p = new Part(name, Small, Section, desc, role: "storage", tool: "wrench", material: "steel", color: Cream, group: Group);
        p.Set(("finish", "worn paint"), ("collider", "boxes"));
        double x0 = mn[0], y0 = mn[1], z0 = mn[2], x1 = mx[0], y1 = mx[1], z1 = mx[2];
        foreach (var (a, b) in new (double[], double[])[]
        {
            ([x0, y0, z0], [x1, y0 + t, z1]), ([x0, y1 - t, z0], [x1, y1, z1]),
            ([x0, y0 + t, z0], [x1, y1 - t, z0 + t]), ([x0, y0 + t, z1 - t], [x1, y1 - t, z1]),
            ([x0, y0 + t, z0 + t], [x0 + t, y1 - t, z1 - t]), ([x1 - t, y0 + t, z0 + t], [x1, y1 - t, z1 - t]),
        })
            p.Add(Box([.. a.Select(R)], [.. b.Select(R)], Cream, hidden: true));
        p.Sock("bottom", [R(0.5 * (x0 + x1)), y0, R(0.5 * (z0 + z1))], "-y");
        if (wall)
            p.Sock("wall", [R(0.5 * (x0 + x1)), R(y0 + Math.Min(y1 - y0, 0.25) / 2), z0], "-z");
        var g = t + 0.01;
        p.Set(("storage", new PyDict { ["slots"] = Py.L(K.Slot([R(x0 + g), R(y0 + g), R(z0 + g)], [R(x1 - g), R(y1 - g), R(z1 - g)])) }));
        p.Stats(40, 220, 2, 2.0);
        var k = new K.Kit(p);
        draw(k);
        return p;
    }

    /// <summary>Makes a seat of a piece whose cushion top is at y (a rider sits 0.55 m up by default).</summary>
    private static Part Seat(Part p, double cushion, bool bunk = false)
    {
        p.Set(("seat", bunk ? Kw(("at", Py.L(0, R(cushion - 0.55), 0)), ("bunk", true)) : Kw(("at", Py.L(0, R(cushion - 0.55), 0)))));
        return p;
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

    // ---- The fittings

    private static IEnumerable<Part> Kept()
    {
        yield return F("Water Tote", "A square water tote in a dented steel cage on a pallet, wired up where the cage split. A tap at the bottom, a jug under it, and a green streak of something growing inside.", [-0.5, 0, -0.5], [0.5, 1.2, 0.5], wall: false, draw: k =>
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
        yield return M("Oxygen Generator", "Keeps a ship's air the hard way: electrolysis cells in four big glass jars, bubbling tubes into a gas bottle and a battery charger feeding them through crocodile clips. Works as a Life Support Unit: aboard while it works and oxygen is left, anyone breathes without a suit, and suits refill. Everyone aboard uses it up. Holds 4 person-hours; Oxygen Tanks add more. F reads it.", 1, 1.25, ["Data 1"], (k, h) =>
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
            DialZ(k, 0.0, 1.0, 0.1, 0.03, 0.8);
            k.Box(-0.06, 0.85, 0.098, 0.06, 0.9, 0.106, Tape, "fabric");
        }).Set(("air", Kw(("kind", "unit"), ("holds", 240)))); // (a Life Support Unit in this look: ShipAir.cs)
        yield return F("Radio Set", "An old shortwave set on a wall shelf: a big tuning dial lit warm, chunky knobs, a handset on a hook by its curly cord, a frequency list taped to the side and the log book beside it.", [-0.35, 0, -0.25], [0.35, 1.0, 0.1], k =>
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
            Tube(k, 1, -0.28, -0.2, 0.82, 1.0, 0.006, Copper, "copper", 4); // (the aerial lead up the wall)
            k.Box(-0.06, 0.53, -0.2, 0.18, 0.55, -0.12, "#7a3a2a", "fabric"); // (the log book)
        });
        yield return Seat(F("Sling Jump Seat", "A fold-down jump seat on the wall, its canvas sling patched and the frame hinged on rusty pins, with a hook for a helmet. Sit in it with F.", [-0.25, 0, -0.25], [0.25, 1.2, 0.25], role: "seat", draw: k =>
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
            k.Hull(Frustum(1, 0.135, -0.12, 0.92, 0.09, 1.04, 0.1, 8), Olive, "worn paint"); // (an old helmet on the hook)
            k.Box(-0.18, 0.12, -0.25, -0.1, 0.35, -0.235, Steel, "rusty paint"); // (the stowed-strap bracket)
            k.Box(-0.19, 0.468, 0.1, 0.19, 0.481, 0.17, Tape, "fabric");
        }), 0.49);
        yield return F("Jacked Strut", "A steel I-beam post propping a sagging deck: gusset plates welded on crooked, a screw jack wound up under its foot, a block of wood shimming the gap and a tag that says DO NOT REMOVE.", [-0.25, 0, -0.25], [0.25, 2.4, 0.25], wall: false, draw: k =>
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
            k.Box(-0.104, 1.0, -0.104, 0.104, 1.08, 0.104, HazardYStr, "hazard paint"); // (hazard tape round the post)
            k.Box(0.012, 1.3, 0.1, 0.09, 1.42, 0.106, Ceramic, "plastic"); // (a tag: DO NOT REMOVE)
        });
        yield return Store("Old Fridge", "A rounded old fridge, cream gone yellow, with a chrome lever handle, rust along the bottom, magnets and notes on the door and a chain round it. It hasn't been cold in years, but it holds things.", [-0.32, 0, -0.32], [0.32, 1.5, 0.35], wall: false, draw: k =>
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
            k.Box(-0.26, 1.2, 0.282, -0.18, 1.3, 0.29, Ceramic, "plastic"); // (a shopping list)
            StreakX(k, 0.3, 1, 0.15, 0.06, 0.35, 0.04);
        });
        yield return Seat(F("Bucket Toilet", "A steel toilet bowl on a welded pedestal with a vacuum hose to the wall, a plywood seat, a hand-written sign about the lever, a mop bucket and a roll of paper on a bent coat-hanger. F sits on it.", [-0.4, 0, -0.35], [0.4, 1.4, 0.35], role: "seat", draw: k =>
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
            k.Box(-0.3, 1.18, -0.35, -0.08, 1.2, -0.336, Tape, "fabric"); // (the sign's tape)
            StreakZ(k, 0.06, 0.04, 0.28, 0.12, 0.03);
        }), 0.44);
        yield return Store("Parts Bins", "A louvred panel on the wall full of little plastic bins in four colours, labels on tape, one bin cracked and taped, nuts and washers spilled on the shelf under them. Holds small parts.", [-0.5, 0.5, -0.25], [0.5, 1.4, -0.05], t: 0.02, draw: k =>
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
            k.Box(-0.15, 0.82, -0.075, 0.02, 0.84, -0.068, Tape, "fabric"); // (the cracked bin's tape)
            StreakZ(k, 0.4, 0.62, 1.0, -0.22, 0.025);
        });
    }

    private const string HazardYStr = "#d9a72a";
}
