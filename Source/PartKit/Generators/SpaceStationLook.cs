using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The space station kit's shared look (docs/sandbox/constructs/station.md): industrial science fiction, worn and working. Pale
/// grey plate over a dark frame, a blue-grey livery band, hazard yellow where things move or dock, amber work lamps, blue-white
/// windows, rust-red tanks. Every station module draws through these helpers, so any station the kit builds reads as one family.
/// Station metres: x east, y up, z south (north is -z, the game's bow); <see cref="P"/> moves them half a small cell into a part's
/// frame, as the ships' generators do, so every piece of a station shares one frame and is placed at the origin.
/// </summary>
internal static class StationLook
{
    public const double H = 0.125; // half a small cell

    // ---- the paint
    public const string Plate = "#9ba0a3", Plate2 = "#868c90", Plate3 = "#aeb2b2", Frame = "#2b2f33", Truss = "#4c5359", Band = "#3c5a6c",
        Hazard = "#d9a441", HazardDark = "#25272a", LampCol = "#ffd8a0", Window = "#a8d8ea", NavRed = "#ff3b2f", NavGreen = "#3bff74",
        Tank = "#9a5436", Pipe = "#7a6448", Water = "#3b6f8a", Oxygen = "#6f8190", Power = "#b8922e", RadiatorCol = "#c6cbcf", Glow = "#ff7a3a",
        Wall = "#b7bbb3", Dado = "#3d5569", DeckCol = "#4b4f51", Ceiling = "#8f969a", Core = "#33383d", Container1 = "#2f5d80",
        Container2 = "#a8402e", Container3 = "#c9952e", Container4 = "#4f7a3c";

    public const string Plated = "plated steel", Dirty = "dirty steel", Painted = "painted plating", HazardFin = "hazard paint", Worn = "worn paint",
        Tread = "tread plate", Grating = "painted grating", DeckFin = "painted deck plate", Rusty = "rusty paint", Steel = "steel", Soot = "soot";

    public static double[] P(double x, double y, double z) => [x - H, y - H, z - H];

    private static double[] Sub(double[] a, double[] b) => [a[0] - b[0], a[1] - b[1], a[2] - b[2]];
    private static double[] Cross(double[] a, double[] b) => [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]];
    private static double[] Unit(double[] a)
    {
        var l = Math.Sqrt(a[0] * a[0] + a[1] * a[1] + a[2] * a[2]);
        return [a[0] / l, a[1] / l, a[2] / l];
    }

    private static (double[] Mn, double[] Mx) Bounds(double x0, double y0, double z0, double x1, double y1, double z1) =>
        (P(Math.Min(x0, x1), Math.Min(y0, y1), Math.Min(z0, z1)), P(Math.Max(x0, x1), Math.Max(y0, y1), Math.Max(z0, z1)));

    /// <summary>A solid box: drawn, it collides, it welds (pieces meet face to face on these).</summary>
    public static void Solid(Part p, double x0, double y0, double z0, double x1, double y1, double z1, string color, string finish)
    {
        var (mn, mx) = Bounds(x0, y0, z0, x1, y1, z1);
        p.Add(Box(mn, mx, color, shape: Kw(("finish", finish))));
    }

    /// <summary>A drawn-only box (no collision, no mass): plates, seams, ribs, lamps.</summary>
    public static void Detail(Part p, double x0, double y0, double z0, double x1, double y1, double z1, string color, string? finish = null, bool glow = false)
    {
        var (mn, mx) = Bounds(x0, y0, z0, x1, y1, z1);
        var shape = new PyDict();
        if (glow)
        {
            shape["glow"] = true;
            shape["paintable"] = false;
        }
        else if (finish != null)
            shape["finish"] = finish;
        p.Add(Box(mn, mx, color, detail: true, shape: shape.Count == 0 ? null : shape));
    }

    public static void Lamp(Part p, double x0, double y0, double z0, double x1, double y1, double z1, string color = LampCol) => Detail(p, x0, y0, z0, x1, y1, z1, color, glow: true);

    /// <summary>A solid box never drawn: collision under a shaped look.</summary>
    public static void Hidden(Part p, double x0, double y0, double z0, double x1, double y1, double z1)
    {
        var (mn, mx) = Bounds(x0, y0, z0, x1, y1, z1);
        p.Add(Box(mn, mx, hidden: true));
    }

    /// <summary>A shaped detail (cylinder, sphere, tube, wedge...) filling the box; front: the axis its ends face.</summary>
    public static void Shaped(Part p, double x0, double y0, double z0, double x1, double y1, double z1, string color, string finish, string shape, string? front = null, params (string Key, object? Value)[] more)
    {
        var (mn, mx) = Bounds(x0, y0, z0, x1, y1, z1);
        var d = Kw(("shape", shape), ("finish", finish));
        if (front != null)
            d["front"] = front;
        d.Update(more);
        p.Add(Box(mn, mx, color, detail: true, shape: d));
    }

    /// <summary>A cylinder of radius r along an axis (x, y or z) from a0 to a1, its middle at the other two coordinates.</summary>
    public static void Cyl(Part p, char axis, double cx, double cy, double cz, double r, double a0, double a1, string color, string finish)
    {
        switch (axis)
        {
            case 'x': Shaped(p, a0, cy - r, cz - r, a1, cy + r, cz + r, color, finish, "cylinder", "+x"); break;
            case 'y': Shaped(p, cx - r, a0, cz - r, cx + r, a1, cz + r, color, finish, "cylinder", "+y"); break;
            default: Shaped(p, cx - r, cy - r, a0, cx + r, cy + r, a1, color, finish, "cylinder", "+z"); break;
        }
    }

    public static void Sphere(Part p, double cx, double cy, double cz, double r, string color, string finish) =>
        Shaped(p, cx - r, cy - r, cz - r, cx + r, cy + r, cz + r, color, finish, "sphere");

    /// <summary>A square bar from a to b, t across (a truss's diagonal): a hull of its eight corners, drawn only.</summary>
    public static void Bar(Part p, double[] a, double[] b, double t, string color, string finish)
    {
        var d = Unit(Sub(b, a));
        var helper = Math.Abs(d[1]) < 0.9 ? new double[] { 0, 1, 0 } : [1, 0, 0];
        var u = Unit(Cross(d, helper));
        var v = Unit(Cross(d, u));
        var pts = new List<double[]>();
        foreach (var e in new[] { a, b })
            foreach (var su in new[] { -1, 1 })
                foreach (var sv in new[] { -1, 1 })
                {
                    var q = P(e[0] + (u[0] * su + v[0] * sv) * t / 2, e[1] + (u[1] * su + v[1] * sv) * t / 2, e[2] + (u[2] * su + v[2] * sv) * t / 2);
                    pts.Add([R(q[0]), R(q[1]), R(q[2])]);
                }
        double[] mn = [.. Enumerable.Range(0, 3).Select(k => pts.Min(q => q[k]))], mx = [.. Enumerable.Range(0, 3).Select(k => pts.Max(q => q[k]))];
        var shape = Kw(("shape", "hull"), ("points", pts.Select(q => (object?)q.Select(x => (object?)x).ToList()).ToList()), ("finish", finish));
        p.Add(Box(mn, mx, color, detail: true, shape: shape));
    }

    public static void Sock(Part p, string name, double x, double y, double z, string dir, Cls? cls = null) => p.Sock(name, P(x, y, z), dir, cls);

    /// <summary>A small fixed hash: the same look on every machine (no Random).</summary>
    public static double Rnd(int seed, int k)
    {
        unchecked
        {
            var h = (uint)(seed * 73856093) ^ (uint)(k * 19349663) ^ 0x9e3779b9u;
            h ^= h >> 16;
            h *= 0x7feb352du;
            h ^= h >> 15;
            h *= 0x846ca68bu;
            h ^= h >> 16;
            return (h & 0xffffff) / (double)0x1000000;
        }
    }

    /// <summary>
    /// The outside of a plated hull wall: a livery band under the roof line, seams at every deck and every few metres, ribs, a strip
    /// of windows on each deck (windows), and tone patches where plates were replaced. axis: the wall's normal ('x' or 'z'), sign its
    /// way out; face: the outer face's coordinate; u along the wall (x for a 'z' wall, z for an 'x' wall).
    /// </summary>
    public static void WallLook(Part p, char axis, int sign, double face, double u0, double u1, double y0, double y1, IReadOnlyList<double> decks, bool windows, int seed)
    {
        void D(double ua, double ub, double ya, double yb, double oa, double ob, string col, string? fin = null, bool glow = false)
        {
            if (axis == 'z')
                Detail(p, ua, ya, face + sign * oa, ub, yb, face + sign * ob, col, fin, glow);
            else
                Detail(p, face + sign * oa, ya, ua, face + sign * ob, yb, ub, col, fin, glow);
        }
        var len = u1 - u0;
        // the band
        D(u0 + 0.3, u1 - 0.3, y1 - 3.1, y1 - 1.9, 0, 0.022, Band, Worn);
        // deck seams and windows
        foreach (var f in decks)
        {
            if (f > y0 + 0.5 && f < y1 - 0.5)
                D(u0 + 0.2, u1 - 0.2, f - 0.07, f + 0.07, 0, 0.032, Frame, Dirty);
            if (windows && f + 2.1 < y1 - 3.2)
                D(u0 + 1.6, u1 - 1.6, f + 1.35, f + 2.05, 0, 0.026, Window, glow: true);
        }
        // vertical seams and ribs
        var bays = Math.Max(2, (int)Math.Round(len / 6.0));
        for (var i = 1; i < bays; i++)
        {
            var u = u0 + len * i / bays;
            if (i % 2 == 0)
                D(u - 0.37, u + 0.37, y0 + 0.3, y1 - 0.2, 0, 0.42, Plate2, Dirty); // a rib
            else
                D(u - 0.05, u + 0.05, y0 + 0.3, y1 - 0.2, 0, 0.036, Frame, Dirty); // a seam
        }
        // replaced plates
        for (var k = 0; k < Math.Min(10, bays * 2); k++)
        {
            var bu = u0 + 1 + Rnd(seed, k) * (len - 5);
            var by = y0 + 1 + Rnd(seed, k + 50) * (y1 - y0 - 6);
            D(bu, bu + 1.8 + Rnd(seed, k + 90) * 2.2, by, by + 1.2 + Rnd(seed, k + 130) * 1.6, 0, 0.015, Rnd(seed, k + 170) < 0.5 ? Plate2 : Plate3, Plated);
        }
    }

    /// <summary>Roof clutter: vent stacks, plant boxes and a pipe run, kept out of a middle square (keep) where something stands on it.</summary>
    public static void Greebles(Part p, double x0, double x1, double z0, double z1, double y, double keep, int n, int seed)
    {
        var placed = 0;
        for (var k = 0; placed < n && k < n * 6; k++)
        {
            var x = x0 + 1.5 + Rnd(seed, k) * (x1 - x0 - 5);
            var z = z0 + 1.5 + Rnd(seed, k + 300) * (z1 - z0 - 5);
            var w = 1.2 + Rnd(seed, k + 600) * 2.4;
            var d = 1.0 + Rnd(seed, k + 900) * 2.0;
            if (x + w > -keep && x < keep && z + d > -keep && z < keep)
                continue;
            var kind = Rnd(seed, k + 1200);
            if (kind < 0.45)
            {
                var h = 0.7 + Rnd(seed, k + 1500) * 1.2;
                Detail(p, x, y, z, x + w, y + h, z + d, kind < 0.2 ? Plate2 : Truss, Dirty);
                Detail(p, x + 0.2, y + h, z + 0.2, x + w - 0.2, y + h + 0.06, z + d - 0.2, Frame, Grating);
            }
            else if (kind < 0.8)
            {
                var r = 0.35 + Rnd(seed, k + 1800) * 0.4;
                Cyl(p, 'y', x + r, 0, z + r, r, y, y + 0.8 + Rnd(seed, k + 2100) * 1.6, Plate2, Dirty);
            }
            else
                {
                if (Rnd(seed, k + 2400) < 0.5)
                    Cyl(p, 'x', 0, y + 0.45, z + 0.3, 0.3, x, Math.Min(x1 - 0.5, x + w * 2.5), Pipe, Dirty);
                else
                    Cyl(p, 'z', x + 0.3, y + 0.45, 0, 0.3, z, Math.Min(z1 - 0.5, z + d * 2.5), Pipe, Dirty);
            }
            placed++;
        }
    }

    /// <summary>A point-defence turret's look on a surface at (x, y, z), facing up: a ring, a domed body and twin barrels.</summary>
    public static void Pdc(Part p, double x, double y, double z)
    {
        Cyl(p, 'y', x, 0, z, 1.3, y, y + 0.5, Frame, Dirty);
        Shaped(p, x - 1.1, y + 0.5, z - 1.1, x + 1.1, y + 2.0, z + 1.1, Plate3, Painted, "sphere");
        Cyl(p, 'z', x - 0.25, y + 1.3, 0, 0.12, z - 3.2, z - 0.6, Frame, Steel);
        Cyl(p, 'z', x + 0.25, y + 1.3, 0, 0.12, z - 3.2, z - 0.6, Frame, Steel);
        Lamp(p, x - 0.15, y + 2.0, z - 0.15, x + 0.15, y + 2.15, z + 0.15, NavRed);
    }
}
