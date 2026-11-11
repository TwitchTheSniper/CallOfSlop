using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// Vehicle body parts: Constructs/Parts/Body (docs/vehiclekit/VEHICLEKIT.md, "Body parts"). Was tools/constructs/body_parts.py.
///
/// What gets a vehicle out of a box: hoods, cabs and roll cages, fenders and bed sides, sloped sheets, a chassis tub, tube frames in
/// straights and bends, bolt decks a floor can take furniture on, and the furniture. Each hood bolts on by one socket on top of an engine,
/// each cab by the rails' spacing, so the kit fits one part where it used to tile a dozen plates.
///
/// A slope is a "wedge" shape with its front end sheared (shift), which the game turns into a real convex collider; hidden boxes in steps
/// under it are what the build rules and a player's feet see. A big shell is hollow to the build rules: only its skin is solid, so an
/// engine or a seat fits inside it. Recipes come from the part kit's assembling (--gen-parts assembling); run it after this one.
/// </summary>
public sealed class BodyParts : IPartGenerator
{
    public string Name => "body_parts";

    private const string Out = "Constructs/Parts/Body";
    private const string Group = "Vehicle Body";
    private const string Steel = "#7a7f86", Dark = "#34383c", Tube = "#8a8f94", Mesh = "#55595c", Wood = "#6e5238", Cloth = "#6b6448";
    private const string Hoods = "Hoods and noses", Cabs = "Cabs and cages", Skins = "Fenders and skirts", Tubes = "Tube frames", Decks = "Decks and tubs", Furn = "Furniture";

    private static Part P(string name, string section, string desc, string color = Steel, string role = "panel", string tool = "welder") =>
        new(name, Small, section, desc, role: role, tool: tool, color: color, group: Group);

    /// <summary>A drawn solid box.</summary>
    private static void Slab(Part p, double[] mn, double[] mx, string? color = null) => p.Add(Box(mn, mx, color));

    /// <summary>Hidden solid boxes along a slope, front to back: what the build rules and feet use.</summary>
    private static void Steps(Part p, double x0, double x1, double zFront, double zBack, double yFront, double yBack, double thick, int n)
    {
        for (var k = 0; k < n; k++)
        {
            double a = k / (double)n, b = (k + 1) / (double)n;
            var y = yFront + (yBack - yFront) * (a + b) / 2;
            p.Add(Box([x0, y - thick / 2, zFront + (zBack - zFront) * a], [x1, y + thick / 2, zFront + (zBack - zFront) * b], hidden: true));
        }
    }

    private static PyDict Wedge(object taper0, object taper1, double shift) =>
        Kw(("shape", "wedge"), ("taper", Py.L(taper0, taper1)), ("shift", Py.L(0, shift)));

    /// <summary>A sheet from (zFront, yFront) up to (zBack, yBack): a sheared wedge, its back end the box and its front end moved.</summary>
    private static void Slope(Part p, double x0, double x1, double zFront, double zBack, double yFront, double yBack, double thick, string? color = null)
    {
        var h = thick / 2;
        p.Add(Box([x0, yBack - h, zFront], [x1, yBack + h, zBack], color, detail: true, shape: Wedge(1, 1, Py.Round((yFront - yBack) / thick, 4))));
    }

    /// <summary>A straight tube along an axis from a to b, drawn round on a hidden square (solid false: drawn only).</summary>
    private static void Bar(Part p, double[] a, double[] b, double r = 0.05, string color = Tube, bool solid = true)
    {
        double[] mn = [.. Enumerable.Range(0, 3).Select(k => Math.Min(a[k], b[k]))];
        double[] mx = [.. Enumerable.Range(0, 3).Select(k => Math.Max(a[k], b[k]))];
        var axis = 0;
        for (var k = 1; k < 3; k++)
            if (mx[k] - mn[k] > mx[axis] - mn[axis])
                axis = k;
        for (var k = 0; k < 3; k++)
            if (k != axis)
            {
                mn[k] -= r;
                mx[k] += r;
            }
        if (solid)
            p.Add(Box(mn, mx, hidden: true));
        var shape = Kw(("shape", "cylinder"), ("segments", 10));
        if (axis < 2)
            shape["front"] = axis == 0 ? "+x" : "+y";
        p.Add(Box(mn, mx, color, detail: true, shape: shape));
    }

    /// <summary>A tube leaning in the y-z plane, from a (low, front) to b (high, back): a sheared wedge, four-sided, on hidden steps.</summary>
    private static void Rake(Part p, double[] a, double[] b, double r = 0.05, string color = Tube, int n = 2)
    {
        var x = a[0];
        Steps(p, x - r, x + r, a[2], b[2], a[1], b[1], r * 2 + 0.03, n);
        var t = r * 2;
        p.Add(Box([x - r, b[1] - r, a[2]], [x + r, b[1] + r, b[2]], color, detail: true, shape: Wedge(1, 1, Py.Round((a[1] - b[1]) / t, 4))));
    }

    private static PyDict Arc(double inner, int angle, int dir, int segments) =>
        Kw(("shape", "arc"), ("inner", inner), ("angle", angle), ("dir", dir), ("segments", segments));

    /// <summary>A socket on a skin: the game wants a socket on a solid face, so a small hidden pad stands behind it.</summary>
    private static void Pad(Part p, string name, double[] at, string d, double size = 0.03)
    {
        int[] v = d switch
        {
            "+x" => [1, 0, 0], "-x" => [-1, 0, 0], "+y" => [0, 1, 0], "-y" => [0, -1, 0], "+z" => [0, 0, 1], _ => [0, 0, -1],
        };
        double[] mn = [.. Enumerable.Range(0, 3).Select(k => at[k] - size - (v[k] == 0 ? 0 : size) * v[k])];
        double[] mx = [.. Enumerable.Range(0, 3).Select(k => at[k] + size - (v[k] == 0 ? 0 : size) * v[k])];
        for (var k = 0; k < 3; k++)
            if (v[k] > 0)
                (mn[k], mx[k]) = (at[k] - 2 * size, at[k]);
            else if (v[k] < 0)
                (mn[k], mx[k]) = (at[k], at[k] + 2 * size);
        p.Add(Box(mn, mx, hidden: true));
        p.Sock(name, at, d);
    }

    private static void SideSockets(Part p, double x, double y, double[] zs)
    {
        for (var k = 0; k < zs.Length; k++)
        {
            Pad(p, $"-x{k}", [-x, y, zs[k]], "-x");
            Pad(p, $"+x{k}", [x, y, zs[k]], "+x");
        }
    }

    private static (double, double) Sorted(double a, double b) => a <= b ? (a, b) : (b, a);

    // ---- Hoods: on an engine's top socket, its origin there. Hollow: top and side skins are solid.

    private static (string, Part) Hood(string name, string desc, double w, double zf, double zb, double yTopF, double yTopB, double yLow, double mass, double health, int armour, string color = Steel, double taper = 0.86)
    {
        var p = P(name, Hoods, desc, color);
        var h = yTopB - yLow;
        p.Add(Box([-w, yLow, zf], [w, yTopB, zb], color, detail: true, shape: Wedge(taper, (yTopF - yLow) / h, Py.Round((yTopF + yLow - yTopB - yLow) / 2 / h, 4))));
        for (var k = 0; k < 5; k++) // (hidden steps under the skin, kept off the engine's top: a V8 is a metre long)
        {
            double a = zf + 0.02 + (zb - zf - 0.02) * k / 5, b = zf + 0.02 + (zb - zf - 0.02) * (k + 1) / 5;
            var y = yTopF - 0.05 + (yTopB - yTopF) * (k + 0.5) / 5;
            if (a < 0.5 && b > -0.5)
                y = Math.Max(y, 0.03);
            p.Add(Box([-w * 0.9, y - 0.025, a], [w * 0.9, y + 0.025, b], hidden: true));
        }
        foreach (var s in new[] { -1, 1 })
        {
            var (lo, hi) = Sorted(s * w * 0.9, s * w * 0.84);
            p.Add(Box([lo, yLow, zf + 0.25], [hi, yTopF - 0.08, zb], hidden: true));
        }
        p.Add(Box([-0.04, yLow + 0.04, zf - 0.02], [0.04, yTopF - 0.03, zf + 0.03], Dark, detail: true));
        Pad(p, "engine", [0, 0, 0], "-y");
        Pad(p, "front", [0, (yLow + yTopF) / 2, zf], "-z");
        SideSockets(p, w * 0.9, -0.27, [-0.25, 0.0, 0.25]);
        Pad(p, "top", [0, Math.Max(yTopB - (yTopB - yTopF) * zb / (zb - zf), 0.075), 0], "+y");
        p.Stats(mass, health, armour, 2.0);
        return ("", p);
    }

    private static (string, Part) BullHood()
    {
        var p = P("Bull Hood", Hoods, "A rounded hood with a raised spine. Old truck, bad intentions.", Steel);
        p.Add(Box([-0.56, -0.56, -0.95], [0.56, 0.16, 0.6], Steel, detail: true, shape: Kw(("shape", "rounded"), ("radius", 0.22), ("edges", "long"), ("segments", 4))));
        p.Add(Box([-0.06, 0.08, -0.9], [0.06, 0.2, 0.58], Dark, detail: true));
        p.Add(Box([-0.5, 0.05, -0.92], [0.5, 0.1, 0.58], hidden: true));
        foreach (var s in new[] { -1, 1 })
        {
            var (lo, hi) = Sorted(s * 0.5, s * 0.46);
            p.Add(Box([lo, -0.56, -0.7], [hi, 0.05, 0.58], hidden: true));
        }
        Pad(p, "engine", [0, 0, 0], "-y");
        Pad(p, "front", [0, -0.2, -0.95], "-z");
        SideSockets(p, 0.5, -0.27, [-0.25, 0.0, 0.25]);
        Pad(p, "top", [0, 0.1, 0], "+y");
        p.Stats(38, 180, 6, 2.0);
        return ("", p);
    }

    private static (string, Part) Grille(string name, string desc, int n, string color)
    {
        var p = P(name, Hoods, desc, color);
        var w = 0.5;
        Slab(p, [-w, -0.3, -0.04], [w, -0.26, 0], color);
        Slab(p, [-w, 0.26, -0.04], [w, 0.3, 0], color);
        for (var k = 0; k < n; k++)
        {
            var x = -w + 2 * w * (k + 0.5) / n;
            p.Add(Box([x - 0.018, -0.26, -0.035], [x + 0.018, 0.26, -0.005], color, detail: true));
        }
        Pad(p, "back", [0, 0, 0], "+z");
        p.Stats(8, 90, 4, 1.0);
        return ("", p);
    }

    private static (string, Part) Plow()
    {
        var p = P("Ram Plow", Hoods, "A wedge of plate on the bumper. Everything else moves first.", Dark);
        p.Add(Box([-0.6, -0.3, -0.42], [0.6, 0.3, 0], Dark, detail: true, shape: Wedge(0.3, 0.55, -0.1)));
        Steps(p, -0.45, 0.45, -0.3, 0, -0.1, 0.0, 0.5, 2);
        Pad(p, "back", [0, 0, 0], "+z");
        p.Stats(35, 400, 12, 2.0);
        return ("", p);
    }

    // ---- Cabs: stand across the rails (feet at y 0), z 0 the seat row. Bars in tube; skins hollow.

    /// <summary>A roll hoop across x at z: a foot bar, posts, bent corners, a top bar.</summary>
    private static void Hoop(Part p, double z, double w, double top)
    {
        var r = 0.14;
        Bar(p, [-w - 0.05, 0.05, z], [w + 0.05, 0.05, z]);
        foreach (var s in new[] { -1, 1 })
        {
            Bar(p, [s * w, 0.1, z], [s * w, top - r, z]);
            var (lo, hi) = Sorted(s * (w - r), s * (w + 0.05));
            p.Add(Box([lo, top - r, z - 0.05], [hi, top + 0.05, z + 0.05], Tube, detail: true, shape: Kw(("shape", "rounded"), ("radius", 0.05), ("edges", "all"), ("segments", 3))));
        }
        Bar(p, [-w + r, top, z], [w - r, top, z]);
    }

    private static (string, Part) RollBar(string name, string desc, double w, double top)
    {
        var p = P(name, Cabs, desc, Tube);
        var z = 0.25;
        Hoop(p, z, w, top);
        p.Sock("footL", [-0.25, 0, z], "-y");
        p.Sock("footR", [0.25, 0, z], "-y");
        Pad(p, "top", [0, top + 0.05, z], "+y");
        p.Stats(16, 220, 6, 2.0);
        return ("", p);
    }

    /// <summary>A hoop behind the seats, a low hoop at the dash, leaning A-pillars and top rails between them; a roof sheet or a mesh by the arguments.</summary>
    private static (string, Part) Cage(string name, string desc, double w, double top, double zf, double zb, string? roof = null, bool mesh = false, string color = Tube)
    {
        var p = P(name, Cabs, desc, color);
        Hoop(p, zb, w, top);
        Bar(p, [-w - 0.05, 0.05, zf], [w + 0.05, 0.05, zf]);
        var dash = top - 0.55;
        foreach (var s in new[] { -1, 1 })
        {
            var x = s * w;
            Bar(p, [x, 0.1, zf], [x, dash, zf]);
            Rake(p, [x, dash, zf], [x, top, zf + 0.42]);
            Bar(p, [x, top, zf + 0.45], [x, top, zb - 0.1], solid: false);
        }
        Bar(p, [-w + 0.05, top, zf + 0.42], [w - 0.05, top, zf + 0.42], solid: false);
        Bar(p, [-w + 0.05, dash, zf], [w - 0.05, dash, zf], 0.04, solid: false);
        if (!string.IsNullOrEmpty(roof))
        {
            Slope(p, -w - 0.08, w + 0.08, zf + 0.42, zb + 0.15, top + 0.09, top + 0.2, 0.03, roof);
            p.Add(Box([-w, top + 0.08, zf + 0.5], [w, top + 0.12, zb], hidden: true));
        }
        if (mesh)
            for (var k = 0; k < 5; k++)
            {
                var x = -w + 2 * w * (k + 0.5) / 5;
                p.Add(Box([x - 0.012, dash - 0.45, zf - 0.02], [x + 0.012, dash, zf + 0.0], Mesh, detail: true));
            }
        p.Sock("footL", [-0.25, 0, zb], "-y");
        p.Sock("footR", [0.25, 0, zb], "-y");
        Pad(p, "top", [0, !string.IsNullOrEmpty(roof) ? top + 0.12 : top + 0.05, (zf + zb) / 2], "+y");
        p.Stats(string.IsNullOrEmpty(roof) ? 32 : 44, 280, 6, 3.0);
        return ("", p);
    }

    /// <summary>Armoured shell: sloped front with a vision slit, tumbled-in sides, roof; the left side open to climb in.</summary>
    private static (string, Part) SlitCab()
    {
        var p = P("Slit Cab", Cabs, "An armoured cab with a slit to see through. Hot, dark, and a lot safer.", Steel);
        double w = 0.85, top = 1.42, zf = -0.62, zb = 0.325;
        Slope(p, -w, w, zf, zf + 0.3, 0.8, 1.05, 0.05, Steel); // the lower front
        Slope(p, -w, w, zf + 0.38, zf + 0.6, 1.17, top, 0.05, Steel); // the upper front, over the slit
        Steps(p, -w, w, zf, zf + 0.3, 0.8, 1.05, 0.05, 3);
        Steps(p, -w, w, zf + 0.38, zf + 0.6, 1.17, top, 0.05, 2);
        Slab(p, [-w, top - 0.03, zf + 0.6], [w, top + 0.03, zb]);
        Slab(p, [-w, 0.03, zb - 0.02], [w, 0.66, zb]); // (waist high: the back's open above it, a bar across and posts holding the roof)
        Bar(p, [0.0, 0.66, zb - 0.01], [0.0, top - 0.03, zb - 0.01], 0.008, Dark, solid: false);
        Bar(p, [w - 0.04, 0.66, zb - 0.01], [w - 0.04, top - 0.03, zb - 0.01], 0.008);
        Bar(p, [-w + 0.04, 0.66, zb - 0.01], [-w + 0.04, top - 0.03, zb - 0.01], 0.008);
        Slab(p, [w - 0.05, 0.27, zf + 0.1], [w, top - 0.03, zb - 0.02]);
        Bar(p, [-w, 0.05, zb - 0.02], [w, 0.05, zb - 0.02]);
        Bar(p, [-w + 0.02, 0.1, zf + 0.08], [-w + 0.02, 1.0, zf + 0.08]);
        Pad(p, "footL", [-0.25, 0, 0.25], "-y");
        Pad(p, "footR", [0.25, 0, 0.25], "-y");
        Pad(p, "top", [0, top + 0.03, 0], "+y");
        p.Stats(70, 500, 16, 3.0);
        return ("", p);
    }

    private static (string, Part) ScreenFrame()
    {
        var p = P("Windscreen Frame", Cabs, "A raked frame where the glass used to be.", Tube);
        double w = 0.8, h = 1.05;
        foreach (var s in new[] { -1, 1 })
            Rake(p, [s * w, 0, -0.25], [s * w, h, 0.25], 0.04);
        Bar(p, [-w, 0, -0.25], [w, 0, -0.25], 0.04);
        Bar(p, [-w, h, 0.25], [w, h, 0.25], 0.04);
        Pad(p, "footL", [-0.25, -0.04, -0.25], "-y");
        Pad(p, "footR", [0.25, -0.04, -0.25], "-y");
        p.Stats(10, 120, 4, 1.5);
        return ("", p);
    }

    // ---- Fenders, bed sides, slopes, tubs

    /// <summary>An arch over a wheel, from a socket on its inner lip: x 0 to w, the wheel's centre r - 0.03 below its top.</summary>
    private static (string, Part) Fender(string name, string desc, double w, double r, string color = Steel)
    {
        var p = P(name, Skins, desc, color);
        double cy = -r + 0.03, cz = 0;
        p.Add(Box([0, cy, cz - r], [w, cy + r, cz + r], color, detail: true, shape: Arc(0.92, 180, 180, 10))); // (dir 180: the crown up; dir 0 is a bowl)
        p.Add(Box([0, -0.04, -r * 0.55], [w, 0.03, r * 0.55], hidden: true));
        foreach (var s in new[] { -1, 1 })
        {
            var (lo, hi) = Sorted(s * r * 0.6, s * r);
            p.Add(Box([0, cy + r * 0.2, lo], [w, cy + r * 0.75, hi], hidden: true));
        }
        Pad(p, "mount", [0, 0, 0], "-x");
        p.Stats(9, 90, 2, 1.0);
        return ("", p);
    }

    /// <summary>A pickup's side, 2 m long with an arch in the middle for the wheel, standing on a deck's edge.</summary>
    private static (string, Part) BedSide(string name, string desc, string color = Steel)
    {
        var p = P(name, Skins, desc, color);
        double t = 0.05, low = -0.06, top = 0.42, arch = 0.6;
        Slab(p, [0, low, -1.0], [t, top, -arch]);
        Slab(p, [0, low, arch], [t, top, 1.0]);
        Slab(p, [0, 0.34, -arch], [t, top, arch]);
        p.Add(Box([0, -0.27, -arch - 0.04], [0.34, 0.93 - 0.27 - 0.33, arch + 0.04], color, detail: true, shape: Arc(0.9, 180, 180, 10)));
        p.Add(Box([0.03, 0.38, -0.5], [0.3, 0.42, 0.5], hidden: true));
        Bar(p, [0.03, top + 0.03, -1.0], [0.03, top + 0.03, 1.0], 0.03, Tube);
        double[] zs = [-0.75, -0.5, 0.5, 0.75];
        for (var k = 0; k < zs.Length; k++)
            Pad(p, $"edge{k}", [0, 0, zs[k]], "-x");
        p.Stats(22, 150, 4, 2.0);
        return ("", p);
    }

    /// <summary>A flat sheet leaning: length along x, run along z from its low edge (front) to its high edge (back at z 0, y 0).</summary>
    private static (string, Part) Sheet(string name, double run, double rise, double length, string color = Steel)
    {
        var p = P(name, Skins, "A slanted body sheet. Bolts on by its top or bottom edge.", color);
        var t = 0.05;
        Slope(p, 0, length, -run, 0, -rise, 0, t, color);
        Steps(p, 0, length, -run, 0, -rise, 0, t, 3);
        var n = Math.Max(1, (int)(length / 0.25));
        for (var k = 0; k < n; k += 2)
        {
            Pad(p, $"top{k}", [0.125 + k * 0.25, 0, 0], "+z");
            Pad(p, $"low{k}", [0.125 + k * 0.25, -rise, -run], "-z");
        }
        p.Stats(8 * length, 80 * length, 4, 1.0);
        return ("", p);
    }

    /// <summary>A sloped hull side: bolts up under a floor's edge and leans in and down to the frame.</summary>
    private static (string, Part) HullSkirt(string name, double length)
    {
        var p = P(name, Skins, "A sloped hull side under a floor's edge. No stilts showing.", Dark);
        double run = 0.34, rise = 0.45;
        Slope(p, 0, length, -run, 0, -rise, 0, 0.05, Dark);
        Steps(p, 0, length, -run, 0, -rise, -0.02, 0.05, 3);
        var n = (int)(length / 0.25);
        for (var k = 0; k < n; k += 2)
            Pad(p, $"top{k}", [0.125 + k * 0.25, 0.025, 0], "+y");
        p.Stats(10 * length, 120 * length, 6, 1.0);
        return ("", p);
    }

    /// <summary>A roof's shoulder: stands on a wall's top edge and leans in to a roof's edge.</summary>
    private static (string, Part) RoofSlant(string name, double length)
    {
        var p = P(name, Skins, "A sheet that leans in from a wall's top to a narrower roof.", Steel);
        double run = 0.5, rise = 0.375;
        Slope(p, 0, length, -run, 0, -rise, 0, 0.05, Steel);
        Steps(p, 0, length, -run, 0, -rise, 0, 0.05, 3);
        var n = (int)(length / 0.25);
        for (var k = 0; k < n; k += 2)
        {
            Pad(p, $"top{k}", [0.125 + k * 0.25, 0, 0], "+z");
            Pad(p, $"low{k}", [0.125 + k * 0.25, -rise, -run], "-y");
        }
        p.Stats(8 * length, 80 * length, 4, 1.0);
        return ("", p);
    }

    /// <summary>A thick slope to bolt outside a wall: solid wedge, its back the wall.</summary>
    private static (string, Part) ArmourSlope(string name, double h, double length)
    {
        var p = P(name, Skins, "A slab of sloped armour. Rounds skate off it.", Steel);
        p.Add(Box([0, 0, -0.3], [length, h, 0], Dark, detail: true, shape: Wedge(1, 0.15, -0.42)));
        Steps(p, 0, length, -0.28, 0, 0.05, h * 0.5, h * 0.6, 3);
        for (var k = 0; k < (int)(length / 0.25); k += 2)
            Pad(p, $"back{k}", [0.125 + k * 0.25, h / 2, 0], "+z");
        p.Stats(60 * length * h, 300 * length, 14, 2.0);
        return ("", p);
    }

    /// <summary>A solid under-body: rails' width and floor height. Floors bolt on top, sides close the gap to the rails.</summary>
    private static (string, Part) Tub(string name, int n)
    {
        var L = n * 0.25;
        var p = P(name, Decks, "A closed steel tub over the rails: a floor stands on it with no stilts showing.", Dark);
        double w = 0.56, top = 0.5;
        Slab(p, [-w, 0, -0.121], [w, top, L - 0.129], Dark); // (its ends never line up with a floor plate over it)
        double[] xs = [-0.25, 0, 0.25];
        for (var i = 0; i < n; i++)
        {
            var z = i * 0.25;
            if (i % 2 != 0)
                continue;
            p.Sock($"footL{i}", [-0.25, 0, z], "-y");
            p.Sock($"footR{i}", [0.25, 0, z], "-y");
            for (var j = 0; j < 3; j++)
                p.Sock($"top{j}{i}", [xs[j], top, z], "+y");
        }
        p.Sock("-z", [0, 0.25, -0.121], "-z");
        p.Sock("+z", [0, 0.25, L - 0.129], "+z");
        p.Stats(9 * L, 220 * L, 8, 2.0);
        return ("", p);
    }

    /// <summary>A floor plate with a socket every 0.25 m on its top, so furniture stands where it's wanted.</summary>
    private static (string, Part) BoltDeck(string name, int nx, int nz)
    {
        var p = P(name, Decks, "Floor plate with bolt holes all over: shelves, seats and crates stand on it.", "#6f6a60");
        const double C = 0.25;
        double x1 = -0.125 + nx * C, z1 = -0.125 + nz * C;
        Slab(p, [-0.125, -0.125, -0.125], [x1, -0.075, z1]);
        for (var i = 0; i < nx; i++)
            for (var k = 0; k < nz; k++)
            {
                if (nz < 8 || k % 2 == 0)
                    p.Sock($"back{i}{k}", [i * C, -0.125, k * C], "-y");
                p.Sock($"front{i}{k}", [i * C, -0.075, k * C], "+y");
            }
        for (var k = 0; k < nz; k += nz >= 8 ? 2 : 1)
        {
            p.Sock($"-x{k}", [-0.125, -0.12, k * C], "-x");
            p.Sock($"+x{k}", [x1, -0.12, k * C], "+x");
        }
        for (var i = 0; i < nx; i++)
        {
            p.Sock($"-z{i}", [i * C, -0.12, -0.125], "-z");
            p.Sock($"+z{i}", [i * C, -0.12, z1], "+z");
        }
        p.Stats(20 * nx * nz * C * C, 160, 12, 1.0);
        return ("", p);
    }

    // ---- Tubes

    private static (string, Part) TubeBend(string name, double r)
    {
        var p = P(name, Tubes, "A 90 degree bend in steel tube.", Tube, role: "frame");
        p.Add(Box([-0.05, 0, 0], [0.05, r * 2, r * 2], Tube, detail: true, shape: Arc(0.76, 90, 135, 8)));
        p.Add(Box([-0.05, r * 1.4, r * 0.6], [0.05, r * 2, r * 1.2], hidden: true));
        p.Add(Box([-0.05, r * 0.6, r * 1.4], [0.05, r * 1.2, r * 2], hidden: true));
        Pad(p, "a", [0, r * 2, r], "+y");
        Pad(p, "b", [0, r, r * 2], "+z");
        p.Stats(3, 80, 5, 1.0);
        return ("", p);
    }

    private static (string, Part) BedRail(string name, double L)
    {
        var p = P(name, Tubes, "Tube rail on posts round a bed: rope it down or hang on.", Tube, role: "frame");
        Bar(p, [0, 0.45, -L / 2], [0, 0.45, L / 2], 0.04);
        foreach (var z in new[] { -L / 2 + 0.08, 0, L / 2 - 0.08 })
            Bar(p, [0, 0, z], [0, 0.42, z], 0.03);
        double[] zs = [-0.25, 0.25];
        for (var k = 0; k < 2; k++)
            Pad(p, $"edge{k}", [-0.03, 0.05, zs[k]], "-x");
        p.Stats(5 * L, 100, 4, 1.0);
        return ("", p);
    }

    private static (string, Part) BullBar()
    {
        var p = P("Bull Bar", Tubes, "Bent tube in front of the grille. Ploughs through fences.", Tube);
        double w = 0.55, h = 0.5;
        Bar(p, [-w, 0, -0.14], [w, 0, -0.14], 0.045);
        foreach (var s in new[] { -1, 1 })
            Rake(p, [s * 0.3, 0.0, -0.14], [s * 0.3, h, -0.02], 0.04);
        Bar(p, [-0.34, h, -0.02], [0.34, h, -0.02], 0.04);
        p.Add(Box([-0.05, 0.15, -0.14], [0.05, 0.25, 0.04], hidden: true));
        p.Sock("back", [0, 0.2, 0.04], "+z");
        p.Stats(14, 300, 8, 1.5);
        return ("", p);
    }

    /// <summary>A long round tank lying along z on cradles.</summary>
    private static (string, Part) Barrel(string name, double L, int cap)
    {
        var p = P(name, Skins, $"Holds {cap} l in a round tank on cradles. A big target.", "#6e5a42", role: "fueltank", tool: "wrench");
        var r = 0.62;
        p.Add(Box([-r, 0.1, -L / 2], [r, 0.1 + 2 * r, L / 2], hidden: true));
        p.Add(Box([-r, 0.1, -L / 2], [r, 0.1 + 2 * r, L / 2], "#6e5a42", detail: true, shape: Kw(("shape", "cylinder"), ("segments", 16))));
        foreach (var z in new[] { -L / 2 + 0.3, L / 2 - 0.3 })
        {
            Slab(p, [-0.56, 0, z - 0.06], [0.56, 0.12, z + 0.06], Dark);
            p.Add(Box([-r - 0.012, 0.09, z - 0.03], [r + 0.012, 0.11 + 2 * r, z + 0.03], "#4e3a2a", detail: true, shape: Kw(("shape", "cylinder"), ("segments", 16))));
        }
        p.Add(Box([-0.12, 0.1 + 2 * r - 0.02, -0.12], [0.12, 0.1 + 2 * r + 0.07, 0.12], Dark, detail: true, shape: Kw(("shape", "cylinder"), ("segments", 10), ("front", "+y"))));
        double[] zs = [-1.0, 0.0, 1.0];
        for (var k = 0; k < (L > 2.5 ? 3 : 2); k++)
        {
            p.Sock($"footL{k}", [-0.25, 0, zs[k]], "-y");
            p.Sock($"footR{k}", [0.25, 0, zs[k]], "-y");
        }
        Pad(p, "top", [0, 0.1 + 2 * r, 0], "+y");
        p.Stats(70 + 30 * L, 180, 3, 3.0);
        p.Set(("ports", Py.L(
                Kw(("name", "Fuel out"), ("type", "pipe"), ("dir", "out"), ("fluid", "fuel"), ("at", Py.L(0.0, 0.1 + r, -L / 2)), ("face", "-z")),
                Kw(("name", "Fill"), ("type", "pipe"), ("dir", "in"), ("fluid", "fuel"), ("at", Py.L(0.0, 0.1 + 2 * r, 0.0)), ("face", "+y")))),
            ("fuelTank", Kw(("capacity", cap))));
        return ("", p);
    }

    // ---- Furniture: stands on its "-y" sockets on a bolt deck, its back to a wall (+z of the part is out into the room, -z the wall).

    private static void Feet(Part p, double[] xs, double[] zs, double y = 0)
    {
        for (var i = 0; i < xs.Length; i++)
            for (var k = 0; k < zs.Length; k++)
                p.Sock($"foot{i}{k}", [xs[i], y, zs[k]], "-y");
    }

    private static PyDict RackBox(double[] mn, double[] mx) =>
        Kw(("min", mn.Select(x => (object?)x).ToList()), ("max", mx.Select(x => (object?)x).ToList()), ("rack", true), ("label", "shelf"));

    private static PyDict Slots(params PyDict[] boxes) => Kw(("slots", boxes.Cast<object?>().ToList()));

    private static (string, Part) Workbench()
    {
        var p = P("Scrap Workbench", Furn, "A steel bench with a vice and a shelf under it. Somebody keeps tools here.", Wood, role: "storage", tool: "wrench");
        var t = 0.04;
        Slab(p, [-0.5, 0.82, -0.3], [0.5, 0.82 + t, 0.3], Wood);
        Slab(p, [-0.47, 0.3, -0.27], [0.47, 0.33, 0.27], Dark);
        foreach (var sx in new[] { -1, 1 })
            foreach (var sz in new[] { -1, 1 })
            {
                double x = sx * 0.45, z = sz * 0.25;
                Slab(p, [x - 0.025, 0, z - 0.025], [x + 0.025, 0.82, z + 0.025], Dark);
            }
        p.Add(Box([0.3, 0.86, 0.1], [0.42, 0.96, 0.26], Dark, detail: true));
        Slab(p, [-0.5, 0.86, 0.285], [0.5, 1.26, 0.3], Dark); // (a tool board at its back)
        Feet(p, [-0.25, 0.25], [0.0]);
        p.Stats(30, 160, 3, 2.0);
        p.Set(("storage", Slots(RackBox([-0.41, 0.33, -0.21], [0.41, 0.8, 0.21]), RackBox([-0.48, 0.86, -0.28], [0.25, 1.25, 0.27]))));
        return ("", p);
    }

    private static (string, Part) Crates()
    {
        var p = P("Crate Stack", Furn, "Lashed crates with the lids off. Rummage.", Wood, role: "storage", tool: "wrench");
        Slab(p, [-0.45, 0, -0.25], [0.0, 0.42, 0.25], Wood);
        Slab(p, [0.03, 0, -0.25], [0.45, 0.42, 0.25], "#5e4a34");
        Slab(p, [-0.25, 0.44, -0.22], [0.2, 0.8, 0.22], "#7a5e40");
        Slab(p, [-0.47, 0, 0.25], [-0.45, 1.16, 0.27], Dark); // (a strap post, up past the top crate)
        Feet(p, [-0.25, 0.25], [0.0]);
        p.Stats(22, 90, 1, 2.0);
        p.Material = "wood"; // (as the Python had it: set after the stats, so the file keeps steel)
        p.Set(("storage", Slots(RackBox([-0.44, 0.42, -0.24], [-0.26, 0.8, 0.24]), RackBox([0.21, 0.42, -0.24], [0.44, 0.8, 0.24]), RackBox([-0.24, 0.8, -0.21], [0.19, 1.15, 0.21]))));
        return ("", p);
    }

    private static (string, Part) Bunk()
    {
        var p = P("Scrap Bunk", Furn, "Two thin mattresses on a tube frame, and a locker under them.", Tube, tool: "wrench", role: "storage");
        foreach (var (y, c) in new[] { (0.42, Cloth), (1.15, "#5c5a42") })
            Slab(p, [-0.29, y, -0.95], [0.29, y + 0.09, 0.95], c);
        foreach (var sx in new[] { -1, 1 })
            foreach (var sz in new[] { -1, 1 })
            {
                double x = sx * 0.31, z = sz * 0.97;
                Slab(p, [x - 0.025, 0, z - 0.025], [x + 0.025, 1.45, z + 0.025], Tube);
            }
        Feet(p, [-0.25, 0.25], [-0.75, 0.75]);
        p.Stats(24, 120, 2, 2.0);
        p.Set(("storage", Slots(RackBox([-0.27, 0.02, -0.9], [0.27, 0.41, 0.9]))));
        return ("", p);
    }

    private static (string, Part) Bench()
    {
        var p = P("Crew Bench", Furn, "A plank bench with a locker in its belly.", Wood, tool: "wrench", role: "storage");
        Slab(p, [-0.24, 0.42, -0.74], [0.24, 0.47, 0.74], Wood);
        Slab(p, [-0.2, 0, -0.7], [0.2, 0.41, 0.7], "#4a3c2c");
        Slab(p, [-0.27, 0.47, -0.75], [-0.24, 0.86, 0.75], Wood); // (its back)
        Feet(p, [0.0], [-0.5, 0.0, 0.5]);
        p.Stats(18, 100, 2, 2.0);
        p.Set(("storage", Slots(RackBox([-0.23, 0.48, -0.72], [0.23, 0.85, 0.72]))));
        return ("", p);
    }

    public void Make(PartOutput o)
    {
        List<(string, Part)> parts =
        [
            Hood("Long Hood", "A long tapered hood. Bolts to the top of an engine, which it hides.", 0.56, -0.95, 0.6, -0.08, 0.14, -0.56, 34, 170, 5),
            Hood("Snub Hood", "A short blunt hood. Bolts to the top of an engine, which it hides.", 0.6, -0.72, 0.6, 0.0, 0.12, -0.56, 30, 170, 5, taper: 0.95),
            Hood("Slab Nose", "A wide armoured nose sloping to a blade. Bolts to the top of an engine.", 0.85, -1.05, 0.6, 0.02, 0.16, -0.3, 80, 420, 14, Dark, 0.7),
            BullHood(),
            Grille("Bar Grille", "Upright bars across a radiator.", 9, Dark),
            Grille("Slat Grille", "Three heavy slats across a radiator.", 3, Steel),
            Plow(),
            RollBar("Roll Bar", "A tube hoop behind the seats.", 0.8, 1.32),
            Cage("Roll Cage", "A full tube cage round the seats: leaning pillars, nothing in the gaps.", 0.82, 1.32, -0.62, 0.25),
            Cage("Tin Roof Cab", "A tube cage under a slanted tin roof.", 0.82, 1.32, -0.62, 0.25, roof: "#6b6d70"),
            Cage("Chop Top Cab", "A low cage with its roof cut down to the seats' tops.", 0.82, 1.29, -0.62, 0.25, roof: Dark),
            Cage("Mesh Guard Cage", "A tube cage with mesh across the dash.", 0.82, 1.32, -0.62, 0.25, mesh: true),
            SlitCab(),
            ScreenFrame(),
            Fender("Arch Fender", "A curved mudguard over a wheel.", 0.5, 0.55),
            Fender("Wide Arch Fender", "A broad curved mudguard over a wheel.", 0.7, 0.62),
            BedSide("Pickup Bed Side", "A truck bed's side with an arch for the wheel."),
            BedRail("Bed Rail 1 m", 1.0),
            BedRail("Bed Rail 2 m", 2.0),
            Sheet("Slant Sheet 1 m", 0.25, 0.45, 1.0),
            Sheet("Slant Sheet 2 m", 0.25, 0.45, 2.0),
            RoofSlant("Roof Slant 1 m", 1.0),
            HullSkirt("Hull Skirt 1 m", 1.0),
            Sheet("Skirt Sheet 1 m", 0.35, 0.5, 1.0, Dark),
            Sheet("Skirt Sheet 2 m", 0.35, 0.5, 2.0, Dark),
            ArmourSlope("Armour Slope 1 m", 0.75, 1.0),
            ArmourSlope("Armour Slope 2 m", 0.75, 2.0),
            Tub("Chassis Tub 1 m", 4),
            Tub("Chassis Tub 2 m", 8),
            BoltDeck("Bolt Deck 1 m", 4, 4),
            BoltDeck("Bolt Deck 0.5x1 m", 2, 4),
            BoltDeck("Bolt Deck 0.5 m", 2, 2),
            BoltDeck("Bolt Deck 0.25 m", 1, 1),
            BoltDeck("Bolt Deck 1x2 m", 4, 8),
            TubeBend("Tube Bend 0.25 m", 0.125),
            TubeBend("Tube Bend 0.5 m", 0.25),
            BullBar(),
            Barrel("Tanker Barrel 3 m", 3.0, 600),
            Barrel("Tanker Barrel 2 m", 2.0, 400),
            Workbench(),
            Crates(),
            Bunk(),
            Bench(),
        ];
        WriteParts(o, Out, parts);
    }
}
