using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The big structural parts (Constructs/Parts/Capital): the same kit at the Medium, Large and Capital size classes, and the Town Block
/// blueprint. Was tools/constructs/capital_parts.py.
///
/// What it is for (docs/sandbox/CONSTRUCTS.md, "Size classes"): a mothership on the scale of a carrier (300 m, 100,000 t) or a traction
/// city: a heavy chassis on track units, overhanging storeys with terraces and balconies, building walls with windows and arches, riveted
/// armour, domes, spires, stacks and trusses; and a ship's hull, bows and keel. A small vehicle's parts bolt on through the adapters.
/// </summary>
public sealed class CapitalParts : IPartGenerator
{
    public string Name => "capital_parts";

    private const string Out = "Constructs/Parts/Capital";
    private static readonly Cls[] Big = [Medium, Large, Capital];

    private const string Steel = "#5f666e", Dark = "#3f444a", Rust = "#7a4a32", Iron = "#6a5f55";
    private const string Brick = "#8a5a44", Plaster = "#b79b7a", Copper = "#9c5a38", Soot = "#2f2b28", Brass = "#a58a4e";
    private const string Frames = "Frames", Plates = "Plates", Armour = "Armour", Hull = "Hull", Chassis = "Chassis";
    private const string Walls = "Walls", Decks = "Decks", Roofs = "Roofs", Stacks = "Stacks", Stairs = "Stairs", Shapes = "Shapes", Adapters = "Adapters";

    /// <summary>A part of class c; the name carries the class, so every name is unique across the classes.</summary>
    private static Part P(string name, Cls c, string section, string desc, string role = "frame", string color = Steel, string material = "steel")
    {
        var p = new Part($"{c.Name} {name}", c, section, desc, role: role, color: color, material: material);
        p.D["centreSockets"] = false; // (a big part's sockets are its class's lattice, nothing else)
        return p;
    }

    /// <summary>Mass and health from the plate area in it (m2), k times its class's plate.</summary>
    private static Part SteelStats(Part p, double area, double k = 1.0, int? armour = null, double? seconds = null)
    {
        var c = p.Cls;
        return p.Stats(area * c.KgM2 * k, Math.Min(16_000_000, 40 + area * c.HealthM2 * k), armour ?? c.Armour, seconds);
    }

    /// <summary>The block's sockets, at most limit x limit a face (the 64 a part can have go quickly).</summary>
    private static void Sides(Part p, int nx, int ny, int nz, int limit = 3) =>
        BlockSockets(p, nx, ny, nz, limit: new[] { "+x", "-x", "+y", "-y", "+z", "-z" }.ToDictionary(d => d, _ => (limit, limit)));

    private static string F(double v) => Fmt(v);

    private static double Sq(double v) => Math.Pow(v, 0.5);

    /// <summary>Rows of raised rivet heads along a plate's edges, as strips (detail): a rough riveted look for a few boxes.</summary>
    private static void Rivets(Part p, double x0, double x1, double z0, double z1, double y, string colour)
    {
        var s = (z1 - z0) / 40;
        var h = 0.012 * Sq(p.Cls.X);
        foreach (var z in new[] { z0 + s * 2, z1 - s * 2 })
            p.Add(Box([x0 + s, y, z - s / 2], [x1 - s, y + h, z + s / 2], colour, detail: true));
        s = (x1 - x0) / 40;
        foreach (var x in new[] { x0 + s * 2, x1 - s * 2 })
            p.Add(Box([x - s / 2, y, z0 + s * 3], [x + s / 2, y + h, z1 - s * 3], colour, detail: true));
    }

    private static PyDict Shape(params (string, object?)[] kv) => Kw(kv);

    // ---- Frames

    private static Part Node(Cls c)
    {
        var H = c.Half;
        var p = P("Frame Node", c, Frames, "The frame's node: sockets on all six faces.", color: Dark);
        p.Add(Box([-H, -H, -H], [H, H, H]));
        Sides(p, 1, 1, 1);
        return SteelStats(p, 6 * Math.Pow(c.Cell, 2) * 0.5);
    }

    private static Part Girder(Cls c, int n)
    {
        double H = c.Half, L = n * c.Cell;
        var p = P($"Girder {F(L)} m", c, Frames, $"Riveted I-girder, {F(c.Cell)} m deep.", color: Steel);
        double z0 = c.Lo(), z1 = c.Hi(n), f = c.Cell * 0.12, w = c.Cell * 0.05;
        p.Add(Box([-H, H - f, z0], [H, H, z1]));
        p.Add(Box([-H, -H, z0], [H, -H + f, z1]));
        p.Add(Box([-w, -H + f, z0], [w, H - f, z1], Dark));
        Sides(p, 1, 1, n);
        return SteelStats(p, L * c.Cell * 2.5 * 0.5);
    }

    private static Part BoxGirder(Cls c, int n)
    {
        double H = c.Half, L = n * c.Cell;
        var p = P($"Box Frame {F(L)} m", c, Frames, "Hollow box section: you can walk through it.", color: Iron);
        double z0 = c.Lo(), z1 = c.Hi(n), t = Math.Max(c.Plate, c.Cell * 0.05);
        p.Add(Box([-H, -H, z0], [H, -H + t, z1]));
        p.Add(Box([-H, H - t, z0], [H, H, z1]));
        p.Add(Box([-H, -H + t, z0], [-H + t, H - t, z1]));
        p.Add(Box([H - t, -H + t, z0], [H, H - t, z1]));
        Sides(p, 1, 1, n);
        return SteelStats(p, L * c.Cell * 4 * 0.6);
    }

    private static Part Truss(Cls c, int n)
    {
        double H = c.Half, L = n * c.Cell;
        var p = P($"Truss {F(L)} m", c, Frames, "Open truss: holds storeys up for little weight.", color: Steel);
        double z0 = c.Lo(), z1 = c.Hi(n), b = c.Cell * 0.1;
        foreach (var sy in new[] { -1, 1 })
            foreach (var sx in new[] { -1, 1 })
            {
                var (x0, x1) = sx < 0 ? (-H, -H + b) : (H - b, H);
                var (y0, y1) = sy < 0 ? (-H, -H + b) : (H - b, H);
                p.Add(Box([x0, y0, z0], [x1, y1, z1]));
            }
        // The lacing on the two side faces, a zigzag of a few steps a cell (drawn only: the chords are what collides).
        var m = Math.Min(4, 24 / (2 * n));
        var t = c.Cell * 0.035;
        foreach (var x in new[] { -H + t, H - t })
            for (var k = 0; k < n; k++)
            {
                var zc = k * c.Cell - H;
                for (var s = 0; s < m; s++)
                {
                    var f = (s + 0.5) / m;
                    var y = -H + (c.Cell - b) * (k % 2 == 0 ? f : 1 - f);
                    p.Add(Box([x - t, y, zc + c.Cell * s / m], [x + t, y + b, zc + c.Cell * (s + 1) / m], Dark, detail: true));
                }
            }
        Sides(p, 1, 1, n);
        return SteelStats(p, L * c.Cell * 0.9);
    }

    private static Part Brace(Cls c, int n)
    {
        var s = n * c.Cell;
        var p = P($"Strut {F(s)} m", c, Frames, "45 degree strut: props an overhanging storey.", color: Steel);
        var m = Math.Min(16, 6 + 2 * n);
        double y0 = c.Lo(), z0 = c.Lo(), d = s / m, t = c.Cell * 0.12;
        for (var k = 0; k < m; k++)
        {
            var y = y0 + d * k;
            p.Add(Box([-t, Math.Max(y0, y - d * 0.4), z0 + d * k], [t, Math.Min(c.Hi(n), y + d * 1.4), z0 + d * (k + 1)]));
        }
        p.Sock("bottom", [0, c.Lo(), 0], "-y");
        p.Sock("front", [0, 0, c.Lo()], "-z");
        p.Sock("top", [0, c.Hi(n), (n - 1) * c.Cell], "+y");
        p.Sock("back", [0, (n - 1) * c.Cell, c.Hi(n)], "+z");
        return SteelStats(p, s * c.Cell * 0.6);
    }

    private static Part Buttress(Cls c, int n)
    {
        double H = c.Half, s = n * c.Cell;
        var p = P($"Buttress {F(s)} m", c, Frames, "Stepped buttress: carries a tall wall's load.", color: Brick, material: "stone");
        const int M = 6;
        double y0 = c.Lo(), y1 = c.Hi(n), z0 = c.Lo(), z1 = c.Hi(n);
        var dz = (z1 - z0) / M;
        for (var k = 0; k < M; k++)
            p.Add(Box([-H, y0, z0 + dz * k], [H, y0 + (y1 - y0) * (k + 1) / M, z0 + dz * (k + 1)], hidden: true));
        p.Add(Box([-H, y0, z0], [H, y1, z1], detail: true, shape: Shape(("shape", "wedge"), ("taper", Py.L(1, 0)), ("shift", Py.L(0, -0.5)))));
        foreach (var k in Pick(n, 3))
        {
            p.Sock($"bottom{k}", [0, c.Lo(), k * c.Cell], "-y");
            p.Sock($"back{k}", [0, k * c.Cell, c.Hi(n)], "+z");
        }
        return SteelStats(p, s * s * 1.2, seconds: c.Seconds * 1.2);
    }

    private static Part Column(Cls c, int n)
    {
        double H = c.Half, L = n * c.Cell;
        var p = P($"Column {F(L)} m", c, Frames, $"Round column, {F(c.Cell)} m across.", color: Iron);
        double z0 = c.Lo(), z1 = c.Hi(n);
        p.Add(Box([-H, -H, z0], [H, H, z1], hidden: true));
        p.Add(Box([-H, -H, z0], [H, H, z1], detail: true, shape: Shape(("shape", "cylinder"), ("segments", 20))));
        foreach (var z in new[] { z0, z1 - c.Cell * 0.08 })
            p.Add(Box([-H, -H, z], [H, H, z + c.Cell * 0.08], Dark, detail: true, shape: Shape(("shape", "cylinder"), ("segments", 20))));
        Sides(p, 1, 1, n);
        return SteelStats(p, Math.PI * c.Cell * L * 0.6);
    }

    // ---- Plates, armour

    private static readonly (string Kind, string Label, string Section, double Tk, double Mk, string Colour, string Material, string What)[] PlateKinds =
    [
        ("hull", "Hull Plate", Plates, 1.0, 1.0, Steel, "steel", "A plain hull plate"),
        ("rust", "Riveted Plate", Plates, 1.0, 1.1, Rust, "steel", "Rough riveted plate"),
        ("armour", "Armour Plate", Armour, 1.6, 1.6, Iron, "armour", "Thick riveted armour"),
        ("belt", "Armour Belt", Armour, 2.6, 2.6, Dark, "armour", "Heavy armour belt, the thickest plate"),
        ("brick", "Brick Wall", Walls, 1.2, 0.8, Brick, "stone", "A rendered brick wall"),
        ("plaster", "Plaster Wall", Walls, 1.0, 0.7, Plaster, "stone", "A plastered wall"),
    ];

    private static void EdgeSockets(Part p, Cls c, int nx, int nz, double x0, double x1, double z0, double z1, double ey)
    {
        foreach (var (d, axis, along, pos) in new[] { ("-x", 0, nz, x0), ("+x", 0, nz, x1), ("-z", 2, nx, z0), ("+z", 2, nx, z1) })
            foreach (var j in Pick(along, 3))
            {
                var at = new[] { 0.0, ey, 0.0 };
                at[axis] = pos;
                at[2 - axis] = j * c.Cell;
                p.Sock($"{d}{j}", at, d);
            }
    }

    private static Part Plate((string Kind, string Label, string Section, double Tk, double Mk, string Colour, string Material, string What) pk, Cls c, int nx, int nz)
    {
        var H = c.Half;
        double w = nx * c.Cell, l = nz * c.Cell;
        var p = P($"{pk.Label} {F(w)}x{F(l)} m", c, pk.Section, $"{pk.What}, {F(c.Plate * pk.Tk)} m thick.", role: "panel", color: pk.Colour, material: pk.Material);
        var t = c.Plate * pk.Tk;
        double x0 = c.Lo(), x1 = c.Hi(nx), z0 = c.Lo(), z1 = c.Hi(nz), y0 = -H;
        p.Add(Box([x0, y0, z0], [x1, y0 + t, z1]));
        if (pk.Kind is "rust" or "armour" or "belt")
            Rivets(p, x0, x1, z0, z1, y0 + t, pk.Kind == "rust" ? Dark : Steel);
        foreach (var i in Pick(nx, 3))
            foreach (var k in Pick(nz, 3))
            {
                p.Sock($"back{i}.{k}", [i * c.Cell, y0, k * c.Cell], "-y");
                p.Sock($"front{i}.{k}", [i * c.Cell, y0 + t, k * c.Cell], "+y");
            }
        EdgeSockets(p, c, nx, nz, x0, x1, z0, z1, y0 + t / 2);
        var arm = pk.Kind switch
        {
            "hull" => c.Armour, "rust" => c.Armour + 4, "armour" => c.Armour * 2, "belt" => c.Armour * 3 + 10, "brick" => c.Armour / 2, _ => c.Armour / 3,
        };
        return SteelStats(p, w * l, pk.Mk, armour: arm);
    }

    private static Part Deck(Cls c, int nx, int nz, string kind)
    {
        var H = c.Half;
        double w = nx * c.Cell, l = nz * c.Cell;
        var (label, colour, what) = kind switch
        {
            "deck" => ("Deck", Iron, "A steel deck plate"), "timber" => ("Timber Deck", "#7d5a3a", "A planked deck on steel"), _ => ("Grate Deck", Dark, "An open grating"),
        };
        var p = P($"{label} {F(w)}x{F(l)} m", c, Decks, $"{what}.", role: "panel", color: colour, material: kind == "timber" ? "wood" : "steel");
        var t = c.Plate * 1.2;
        double x0 = c.Lo(), x1 = c.Hi(nx), z0 = c.Lo(), z1 = c.Hi(nz), y0 = -H;
        p.Add(Box([x0, y0, z0], [x1, y0 + t, z1]));
        // Stiffeners under it (drawn only): a deck reads as a deck from below too.
        for (var i = 1; i < nx; i++)
        {
            var x = c.Lo() + i * c.Cell;
            p.Add(Box([x - c.Cell * 0.03, y0 - c.Cell * 0.06, z0], [x + c.Cell * 0.03, y0, z1], Dark, detail: true));
        }
        foreach (var i in Pick(nx, 3))
            foreach (var k in Pick(nz, 3))
            {
                p.Sock($"back{i}.{k}", [i * c.Cell, y0, k * c.Cell], "-y");
                p.Sock($"front{i}.{k}", [i * c.Cell, y0 + t, k * c.Cell], "+y");
            }
        EdgeSockets(p, c, nx, nz, x0, x1, z0, z1, y0 + t / 2);
        p.D["walk"] = "floor";
        return SteelStats(p, w * l, 0.9);
    }

    // ---- Walls: windows, arches

    private static Part WindowWall(Cls c, int nx, int ny, string colour = Brick)
    {
        var H = c.Half;
        double w = nx * c.Cell, h = ny * c.Cell;
        var p = P($"Window Wall {F(w)}x{F(h)} m", c, Walls, $"Wall with {nx * ny} open windows: rounds and light pass.", role: "panel", color: colour, material: "stone");
        var t = c.Plate * 1.2;
        var z0 = -H;
        double x0 = c.Lo(), x1 = c.Hi(nx), y0 = c.Lo(), y1 = c.Hi(ny);
        var op = c.Cell * 0.5; // the opening
        // Lintels and sills right across, piers between the openings.
        for (var j = 0; j < ny; j++)
        {
            var yc = j * c.Cell;
            p.Add(Box([x0, yc - H, z0], [x1, yc - op / 2, z0 + t]));
            for (var i = 0; i <= nx; i++)
            {
                var xa = i == 0 ? x0 : (i - 1) * c.Cell + op / 2;
                var xb = i < nx ? i * c.Cell - op / 2 : x1;
                p.Add(Box([xa, yc - op / 2, z0], [xb, yc + op / 2, z0 + t]));
            }
        }
        p.Add(Box([x0, (ny - 1) * c.Cell + op / 2, z0], [x1, y1, z0 + t]));
        foreach (var i in Pick(nx, 3))
        {
            p.Sock($"bottom{i}", [i * c.Cell, y0, z0 + t / 2], "-y");
            p.Sock($"top{i}", [i * c.Cell, y1, z0 + t / 2], "+y");
        }
        foreach (var j in Pick(ny, 3))
        {
            p.Sock($"left{j}", [x0, j * c.Cell, z0 + t / 2], "-x");
            p.Sock($"right{j}", [x1, j * c.Cell, z0 + t / 2], "+x");
            p.Sock($"back{j}", [0, j * c.Cell, z0], "-z");
            p.Sock($"front{j}", [0, j * c.Cell, z0 + t], "+z");
        }
        return SteelStats(p, w * h * 0.75, 0.8, armour: c.Armour / 2);
    }

    /// <summary>n arches in a row, two cells tall: an arcade. The arc shape turns about X, so the wall's face is Y-Z and it is thick along X.</summary>
    private static Part ArchWall(Cls c, int n, string colour = Brick)
    {
        var H = c.Half;
        double span = c.Cell, tall = 2 * c.Cell;
        var p = P($"Arcade {F(n * span)}x{F(tall)} m", c, Walls, $"{n} arch{(n > 1 ? "es" : "")} in a row.", role: "panel", color: colour, material: "stone");
        var t = c.Plate * 2;
        double x0 = -H, x1 = -H + t, y0 = c.Lo(), y1 = c.Hi(2);
        var pier = c.Cell * 0.12;
        var rise = c.Cell * 0.5; // the arch's height
        var spring = y1 - rise - c.Cell * 0.1; // where the curve starts
        var z0 = c.Lo();
        for (var k = 0; k <= n; k++)
        {
            var zc = z0 + k * span;
            p.Add(Box([x0, y0, Math.Max(z0, zc - pier)], [x1, spring, Math.Min(c.Hi(n), zc + pier)]));
        }
        p.Add(Box([x0, y1 - c.Cell * 0.1, z0], [x1, y1, c.Hi(n)]));
        const int M = 6;
        for (var k = 0; k < n; k++)
        {
            var za = z0 + k * span + pier;
            var zb = z0 + (k + 1) * span - pier;
            for (var s = 0; s < M; s++)
            {
                // The spandrel over the opening, in columns that follow the ellipse.
                var u = (s + 0.5) / M * 2 - 1;
                var top = spring + rise * Math.Sqrt(Math.Max(0.0, 1 - u * u));
                p.Add(Box([x0, top, za + (zb - za) * s / M], [x1, y1 - c.Cell * 0.1, za + (zb - za) * (s + 1) / M]));
            }
            var ring = c.Cell * 0.05;
            p.Add(Box([x0 - 0.01 * c.X, spring - ring, za - ring], [x1 + 0.01 * c.X, spring + rise + ring, zb + ring], Dark, detail: true,
                shape: Shape(("shape", "arc"), ("inner", 0.86), ("angle", 180), ("dir", 180), ("segments", 16))));
        }
        for (var k = 0; k < n; k++)
        {
            p.Sock($"bottom{k}", [x0 + t / 2, y0, k * c.Cell], "-y");
            p.Sock($"top{k}", [x0 + t / 2, y1, k * c.Cell], "+y");
        }
        p.Sock("left", [x0 + t / 2, 0, z0], "-z");
        p.Sock("right", [x0 + t / 2, 0, c.Hi(n)], "+z");
        return SteelStats(p, n * span * tall * 0.6, 0.8, armour: c.Armour / 2);
    }

    // ---- Decks that hang out: balconies, terraces, corbels

    /// <summary>A rail along x at z: two bars and posts (drawn: thin things are cheaper not to collide with, a hidden kerb stops you).</summary>
    private static void Railing(Part p, double x0, double x1, double z, double y, string colour = Dark)
    {
        double h = 1.1, b = 0.05;
        p.Add(Box([x0, y, z - 0.1], [x1, y + 0.3, z + 0.1], colour, hidden: true));
        p.Add(Box([x0, y + h, z - b], [x1, y + h + 2 * b, z + b], colour, detail: true));
        p.Add(Box([x0, y + h * 0.55, z - b / 2], [x1, y + h * 0.55 + b, z + b / 2], colour, detail: true));
        var n = Math.Max(2, (int)((x1 - x0) / 2) + 1);
        var m = Math.Min(n, 7);
        for (var k = 0; k < m; k++)
        {
            var x = x0 + (x1 - x0) * (k + 0.5) / m;
            p.Add(Box([x - b, y, z - b], [x + b, y + h, z + b], colour, detail: true));
        }
    }

    private static Part Balcony(Cls c, int n)
    {
        var H = c.Half;
        double w = n * c.Cell, d = c.Cell;
        var p = P($"Balcony {F(w)} m", c, Decks, $"Railed balcony, {F(d)} m deep: hangs off a wall.", role: "panel", color: "#7d5a3a");
        var t = c.Plate * 1.2;
        double x0 = c.Lo(), x1 = c.Hi(n), z0 = c.Lo(), z1 = c.Hi(1), y0 = -H;
        p.Add(Box([x0, y0, z0], [x1, y0 + t, z1]));
        Railing(p, x0, x1, z0 + 0.06, y0 + t);
        // Brackets under it.
        foreach (var k in Pick(n, 3))
        {
            var x = k * c.Cell;
            p.Add(Box([x - c.Cell * 0.05, y0 - c.Cell * 0.2, z1 - c.Cell * 0.5], [x + c.Cell * 0.05, y0, z1], Dark, detail: true, shape: Shape(("shape", "wedge"), ("taper", Py.L(1, 0)), ("shift", Py.L(0, 0.5)))));
        }
        foreach (var k in Pick(n, 3))
        {
            p.Sock($"back{k}", [k * c.Cell, y0 + t / 2, z1], "+z");
            p.Sock($"bottom{k}", [k * c.Cell, y0, 0], "-y");
        }
        p.Sock("left", [x0, y0 + t / 2, 0], "-x");
        p.Sock("right", [x1, y0 + t / 2, 0], "+x");
        p.D["walk"] = "floor";
        return SteelStats(p, w * d, 0.7);
    }

    private static Part Terrace(Cls c, int nx, int nz)
    {
        var H = c.Half;
        double w = nx * c.Cell, l = nz * c.Cell;
        var p = P($"Terrace {F(w)}x{F(l)} m", c, Decks, "Flat roof with a parapet on three sides.", role: "panel", color: Plaster, material: "stone");
        double t = c.Plate * 1.2, ph = 1.1, pt = Math.Max(0.2, c.Plate);
        double x0 = c.Lo(), x1 = c.Hi(nx), z0 = c.Lo(), z1 = c.Hi(nz), y0 = -H;
        p.Add(Box([x0, y0, z0], [x1, y0 + t, z1]));
        p.Add(Box([x0, y0 + t, z0], [x1, y0 + t + ph, z0 + pt], Brick));
        p.Add(Box([x0, y0 + t, z0 + pt], [x0 + pt, y0 + t + ph, z1], Brick));
        p.Add(Box([x1 - pt, y0 + t, z0 + pt], [x1, y0 + t + ph, z1], Brick));
        foreach (var i in Pick(nx, 3))
            foreach (var k in Pick(nz, 3))
                p.Sock($"back{i}.{k}", [i * c.Cell, y0, k * c.Cell], "-y");
        foreach (var i in Pick(nx, 3))
            p.Sock($"+z{i}", [i * c.Cell, y0 + t / 2, z1], "+z");
        p.D["walk"] = "floor";
        return SteelStats(p, w * l, 0.9);
    }

    private static Part Corbel(Cls c, int n)
    {
        var w = n * c.Cell;
        var p = P($"Corbel {F(w)} m", c, Frames, "Stepped bracket: a storey overhangs by a cell.", color: Brick, material: "stone");
        const int M = 5;
        double x0 = c.Lo(), x1 = c.Hi(n), y0 = c.Lo(), y1 = c.Hi(1), z0 = c.Lo(), z1 = c.Hi(1);
        var dy = (y1 - y0) / M;
        for (var k = 0; k < M; k++)
            p.Add(Box([x0, y0 + dy * k, z0 + (z1 - z0) * (1 - (k + 1) / (double)M)], [x1, y0 + dy * (k + 1), z1], hidden: true));
        p.Add(Box([x0, y0, z0], [x1, y1, z1], detail: true, shape: Shape(("shape", "wedge"), ("taper", Py.L(1, 0)), ("shift", Py.L(0, 0.5)))));
        foreach (var k in Pick(n, 3))
        {
            p.Sock($"back{k}", [k * c.Cell, 0, z1], "+z");
            p.Sock($"top{k}", [k * c.Cell, y1, 0], "+y");
        }
        return SteelStats(p, w * c.Cell * 1.2);
    }

    // ---- Hull: sections, bows, keels

    private static Part HullSection(Cls c, int nx, int ny, int nz)
    {
        double w = nx * c.Cell, h = ny * c.Cell, l = nz * c.Cell;
        var p = P($"Hull Section {F(w)}x{F(h)}x{F(l)} m", c, Hull, "Hollow hull block, open at both ends.", color: Steel);
        var t = c.Plate * 1.5;
        double x0 = c.Lo(), x1 = c.Hi(nx), y0 = c.Lo(), y1 = c.Hi(ny), z0 = c.Lo(), z1 = c.Hi(nz);
        p.Add(Box([x0, y0, z0], [x1, y0 + t, z1]));
        p.Add(Box([x0, y1 - t, z0], [x1, y1, z1]));
        p.Add(Box([x0, y0 + t, z0], [x0 + t, y1 - t, z1]));
        p.Add(Box([x1 - t, y0 + t, z0], [x1, y1 - t, z1]));
        // Ring frames (drawn): the ribs that show it is a ship from inside.
        var r = c.Cell * 0.04;
        for (var k = 1; k < nz; k++)
        {
            var z = c.Lo() + k * c.Cell;
            p.Add(Box([x0 + t, y0 + t, z - r], [x1 - t, y0 + t + r * 2, z + r], Dark, detail: true));
            p.Add(Box([x0 + t, y1 - t - r * 2, z - r], [x1 - t, y1 - t, z + r], Dark, detail: true));
        }
        Sides(p, nx, ny, nz, limit: 2);
        return SteelStats(p, 2 * (w + h) * l, 1.5);
    }

    private static Part Bow(Cls c, int nx, int ny, int nz)
    {
        double w = nx * c.Cell, h = ny * c.Cell, l = nz * c.Cell;
        var p = P($"Bow {F(w)}x{F(h)}x{F(l)} m", c, Hull, "Hull's nose: fits on a hull section's end.", color: Steel);
        double x0 = c.Lo(), x1 = c.Hi(nx), y0 = c.Lo(), y1 = c.Hi(ny), z0 = c.Lo(), z1 = c.Hi(nz);
        const int M = 8;
        var dz = (z1 - z0) / M;
        var mid = (x0 + x1) / 2;
        for (var k = 0; k < M; k++)
        {
            var half = (x1 - x0) / 2 * Math.Sqrt((k + 1 - 0.25) / M);
            p.Add(Box([mid - half, y0, z0 + dz * k], [mid + half, y1, z0 + dz * (k + 1)], hidden: true));
        }
        p.Add(Box([x0, y0, z0], [x1, y1, z1], detail: true, shape: Shape(("shape", "wedge"), ("taper", Py.L(0.08, 1)), ("shift", Py.L(0, 0)))));
        foreach (var i in Pick(nx, 3))
            foreach (var j in Pick(ny, 3))
                p.Sock($"back{i}.{j}", [i * c.Cell, j * c.Cell, z1], "+z");
        if (nx % 2 != 0)
            p.Sock("top", [mid, y1, (nz - 1) * c.Cell], "+y");
        return SteelStats(p, 2 * (w + h) * l * 0.6, 1.5);
    }

    private static Part Keel(Cls c, int n)
    {
        double H = c.Half, L = n * c.Cell;
        var p = P($"Keel {F(L)} m", c, Hull, "Deep keel beam: the spine under a hull or chassis.", color: Dark);
        double z0 = c.Lo(), z1 = c.Hi(n);
        p.Add(Box([-H, -H, z0], [H, H, z1], hidden: true));
        p.Add(Box([-H, -H, z0], [H, H, z1], detail: true, shape: Shape(("shape", "bevel"), ("radius", 0.2), ("edges", "long"))));
        Sides(p, 1, 1, n, limit: 4);
        return SteelStats(p, 4 * L * c.Cell * 0.7, 1.5);
    }

    // ---- The undercarriage

    private static Part ChassisDeck(Cls c, int nx, int nz)
    {
        double w = nx * c.Cell, l = nz * c.Cell;
        var p = P($"Chassis Deck {F(w)}x{F(l)} m", c, Chassis, $"Heavy grillage, {F(c.Cell)} m deep: carries a town on tracks.", color: Dark);
        var t = c.Plate * 2;
        double x0 = c.Lo(), x1 = c.Hi(nx), y0 = c.Lo(), y1 = c.Hi(1), z0 = c.Lo(), z1 = c.Hi(nz);
        p.Add(Box([x0, y0, z0], [x1, y0 + t, z1]));
        p.Add(Box([x0, y1 - t, z0], [x1, y1, z1]));
        var b = c.Cell * 0.08;
        foreach (var i in Pick(nx + 1, 4))
        {
            var x = c.Lo() + i * c.Cell;
            p.Add(Box([Math.Max(x0, x - b), y0 + t, z0], [Math.Min(x1, x + b), y1 - t, z1], Iron));
        }
        foreach (var k in Pick(nz + 1, 2))
        {
            var z = c.Lo() + k * c.Cell;
            p.Add(Box([x0 + b * 2, y0 + t, Math.Max(z0, z - b)], [x1 - b * 2, y1 - t, Math.Min(z1, z + b)], Iron));
        }
        foreach (var i in Pick(nx, 4))
            foreach (var k in Pick(nz, 4))
            {
                p.Sock($"under{i}.{k}", [i * c.Cell, y0, k * c.Cell], "-y");
                p.Sock($"top{i}.{k}", [i * c.Cell, y1, k * c.Cell], "+y");
            }
        foreach (var i in Pick(nz, 3))
        {
            p.Sock($"-x{i}", [x0, 0, i * c.Cell], "-x");
            p.Sock($"+x{i}", [x1, 0, i * c.Cell], "+x");
        }
        foreach (var i in Pick(nx, 3))
        {
            p.Sock($"-z{i}", [i * c.Cell, 0, z0], "-z");
            p.Sock($"+z{i}", [i * c.Cell, 0, z1], "+z");
        }
        return SteelStats(p, w * l * 2 + 2 * (w + l) * c.Cell, 2.5, armour: c.Armour * 2);
    }

    private static Part Spreader(Cls c, int n)
    {
        double H = c.Half, L = n * c.Cell;
        var p = P($"Load Spreader {F(L)} m", c, Chassis, "Tapered bolster: spreads a column's load.", color: Dark);
        double z0 = c.Lo(), z1 = c.Hi(n);
        p.Add(Box([-H, -H, z0], [H, -H + c.Cell * 0.2, z1]));
        var mid = (z0 + z1) / 2;
        const int M = 4;
        for (var k = 0; k < M; k++)
        {
            var half = (z1 - z0) / 2 * (1 - k * 0.2);
            var y = -H + c.Cell * 0.2 + c.Cell * 0.8 * k / M;
            p.Add(Box([-H * 0.8, y, mid - half], [H * 0.8, y + c.Cell * 0.8 / M, mid + half], Iron));
        }
        foreach (var k in Pick(n, 4))
            p.Sock($"under{k}", [0, -H, k * c.Cell], "-y");
        if (n % 2 != 0)
            p.Sock("top", [0, H, mid], "+y");
        return SteelStats(p, L * c.Cell * 1.5, 2.0);
    }

    // ---- Roofs

    /// <summary>
    /// A round thing h high on a base n cells across. A shape's taper narrows its front (-Z), so the thing rises towards -Z from its base on
    /// +Z (cell 0's high face): turned over, the base's sockets face down. m slabs following radius(t), t 0 at the base; stepped hidden boxes
    /// round them are what collides.
    /// </summary>
    private static void Revolve(Part p, Cls c, int n, Func<double, double> profile, int m, string colour, double h)
    {
        var cx = (c.Lo() + c.Hi(n)) / 2;
        var R = n * c.Cell / 2;
        var z1 = c.Half;
        var dz = h / m;
        for (var k = 0; k < m; k++)
        {
            double r0 = R * profile(k / (double)m), r1 = R * profile((k + 1) / (double)m);
            var z = z1 - dz * (k + 1);
            var solid = Math.Max(0.02, (r0 + r1) / 2 * 0.92);
            p.Add(Box([cx - solid, cx - solid, z], [cx + solid, cx + solid, z + dz], hidden: true));
            var rd = Math.Max(r0, r1);
            if (r1 <= r0 && rd > 0.01)
            {
                var t = Py.Round(Math.Max(0.01, r1 / rd), 4);
                p.Add(Box([cx - rd, cx - rd, z], [cx + rd, cx + rd, z + dz], colour, detail: true, shape: Shape(("shape", "cylinder"), ("segments", 20), ("taper", Py.L(t, t)))));
            }
            else if (rd > 0.01)
            {
                var mean = (r0 + r1) / 2;
                p.Add(Box([cx - mean, cx - mean, z], [cx + mean, cx + mean, z + dz], colour, detail: true, shape: Shape(("shape", "cylinder"), ("segments", 20))));
            }
        }
    }

    private static void BaseSockets(Part p, Cls c, int n)
    {
        foreach (var i in Pick(n, 3))
            foreach (var j in Pick(n, 3))
                p.Sock($"base{i}.{j}", [i * c.Cell, j * c.Cell, c.Half], "+z");
    }

    private static Part Dome(Cls c, int n)
    {
        var s = n * c.Cell;
        var p = P($"Dome {F(s)} m", c, Roofs, "Copper dome.", color: Copper);
        Revolve(p, c, n, t => Math.Sqrt(Math.Max(0.0, 1 - t * t)), 7, Copper, s / 2);
        BaseSockets(p, c, n);
        return SteelStats(p, 2 * Math.PI * Math.Pow(s / 2, 2), 0.6, armour: c.Armour / 2);
    }

    private static Part Onion(Cls c, int n)
    {
        var s = n * c.Cell;
        var p = P($"Onion Dome {F(s)} m", c, Roofs, "Onion dome.", color: Copper);
        Revolve(p, c, n, t => Math.Max(0.02, (0.8 + 0.5 * Math.Sin(Math.PI * Math.Min(1.0, t * 1.6))) * Math.Pow(1 - t, 0.8) * 0.77), 8, Copper, s * 0.95);
        BaseSockets(p, c, n);
        return SteelStats(p, 2 * Math.PI * Math.Pow(s / 2, 2), 0.6, armour: c.Armour / 2);
    }

    private static Part Spire(Cls c, int n, int tall)
    {
        var s = n * c.Cell;
        var p = P($"Spire {F(s)}x{F(tall * c.Cell)} m", c, Roofs, "Tall spire.", color: Dark);
        Revolve(p, c, n, t => Math.Max(0.015, 1 - t), 6, Dark, tall * c.Cell);
        BaseSockets(p, c, n);
        return SteelStats(p, Math.PI * s * tall * c.Cell * 0.5, 0.4, armour: c.Armour / 2);
    }

    private static Part PyramidRoof(Cls c, int n)
    {
        var s = n * c.Cell;
        var p = P($"Hip Roof {F(s)} m", c, Roofs, "Four-sided roof rising to a point.", color: Rust);
        var h = s / 2;
        var z1 = c.Half;
        var cx = (c.Lo() + c.Hi(n)) / 2;
        const int M = 5;
        for (var k = 0; k < M; k++)
        {
            var e = s * (1 - (k + 0.3) / M) / 2;
            p.Add(Box([cx - e, cx - e, z1 - h * (k + 1) / M], [cx + e, cx + e, z1 - h * k / M], hidden: true));
        }
        p.Add(Box([c.Lo(), c.Lo(), z1 - h], [c.Hi(n), c.Hi(n), z1], detail: true, shape: Shape(("shape", "wedge"), ("taper", Py.L(0, 0)), ("shift", Py.L(0, 0)))));
        BaseSockets(p, c, n);
        return SteelStats(p, s * s * 1.2, 0.6, armour: c.Armour / 2);
    }

    /// <summary>The ridge runs along X, nx cells long; the span is nz cells of Y. It rises towards -Z from its base on +Z.</summary>
    private static Part GableRoof(Cls c, int nx, int nz)
    {
        double w = nz * c.Cell, l = nx * c.Cell;
        var p = P($"Gable Roof {F(w)}x{F(l)} m", c, Roofs, "Pitched roof with a ridge.", color: Rust);
        double x0 = c.Lo(), x1 = c.Hi(nx), y0 = c.Lo(), y1 = c.Hi(nz), z1 = c.Half;
        var h = w / 2;
        var mid = (y0 + y1) / 2;
        const int M = 6;
        for (var k = 0; k < M; k++)
        {
            var e = w * (1 - (k + 0.3) / M) / 2;
            p.Add(Box([x0, mid - e, z1 - h * (k + 1) / M], [x1, mid + e, z1 - h * k / M], hidden: true));
        }
        p.Add(Box([x0, y0, z1 - h], [x1, y1, z1], detail: true, shape: Shape(("shape", "wedge"), ("taper", Py.L(1, 0)), ("shift", Py.L(0, 0)))));
        foreach (var i in Pick(nx, 3))
            foreach (var j in Pick(nz, 3))
                p.Sock($"base{i}.{j}", [i * c.Cell, j * c.Cell, z1], "+z");
        return SteelStats(p, w * l * 1.3, 0.6, armour: c.Armour / 2);
    }

    private static Part BarrelRoof(Cls c, int nx, int nz)
    {
        double w = nx * c.Cell, l = nz * c.Cell;
        var p = P($"Barrel Roof {F(w)}x{F(l)} m", c, Roofs, "Curved roof: a hangar's vault.", color: Copper);
        var nl = nz;
        double x0 = c.Lo(), x1 = c.Hi(nl), y0 = c.Lo(), z0 = c.Lo(), z1 = c.Hi(nx);
        var R = w / 2;
        var t = c.Plate;
        var mid = (z0 + z1) / 2;
        const int M = 10;
        // The arc shape turns about X: this part's length runs along X, its span along Z.
        for (var k = 0; k < M; k++)
        {
            double a0 = Math.PI * k / M, a1 = Math.PI * (k + 1) / M;
            double[] ys = [y0 + R * Math.Sin(a0), y0 + R * Math.Sin(a1)];
            double[] zs = [mid - R * Math.Cos(a0), mid - R * Math.Cos(a1)];
            p.Add(Box([x0, ys.Min(), zs.Min()], [x1, ys.Max() + t, zs.Max()], hidden: true));
        }
        p.Add(Box([x0, y0, z0], [x1, y0 + R, z1], detail: true, shape: Shape(("shape", "arc"), ("inner", Math.Min(0.98, Py.Round(1 - t / R, 4))), ("angle", 180), ("dir", 180), ("segments", 20))));
        foreach (var i in Pick(nl, 3))
        {
            p.Sock($"left{i}", [i * c.Cell, y0, 0], "-y");
            p.Sock($"right{i}", [i * c.Cell, y0, (nx - 1) * c.Cell], "-y");
        }
        return SteelStats(p, Math.PI * R * l, 0.8);
    }

    private static Part CurvedPlate(Cls c, int n, int nx)
    {
        double s = n * c.Cell, w = nx * c.Cell;
        var p = P($"Curved Plate {F(s)}x{F(w)} m", c, Shapes, $"Quarter-pipe of plate, {F(s)} m radius.", role: "panel", color: Steel);
        var t = c.Plate;
        double y0 = c.Lo(), z1 = c.Hi(n);
        var m = Math.Min(14, 4 + 2 * n);
        for (var k = 0; k < m; k++)
        {
            double a0 = Math.PI / 2 * k / m, a1 = Math.PI / 2 * (k + 1) / m;
            var ys = new List<double>();
            var zs = new List<double>();
            foreach (var a in new[] { a0, a1 })
                foreach (var rr in new[] { s - t, s })
                {
                    ys.Add(y0 + rr * Math.Sin(a));
                    zs.Add(z1 - rr * Math.Cos(a));
                }
            p.Add(Box([c.Lo(), Math.Max(y0, ys.Min()), Math.Max(c.Lo(), zs.Min())], [c.Hi(nx), Math.Min(c.Hi(n), ys.Max()), Math.Min(z1, zs.Max())], hidden: true));
        }
        p.Add(Box([c.Lo(), c.Lo(), c.Lo()], [c.Hi(nx), c.Hi(n), c.Hi(n)], detail: true,
            shape: Shape(("shape", "arc"), ("inner", Math.Min(0.98, Py.Round(1 - t / s, 4))), ("angle", 90), ("dir", 135), ("segments", Math.Max(10, 3 * n)))));
        foreach (var i in Pick(nx, 4))
        {
            p.Sock($"bottom{i}", [i * c.Cell, y0, 0], "-y");
            p.Sock($"back{i}", [i * c.Cell, (n - 1) * c.Cell, z1], "+z");
        }
        return SteelStats(p, w * s * Math.PI / 2);
    }

    private static Part Wedge(Cls c, int ny, int nz)
    {
        double h = ny * c.Cell, l = nz * c.Cell;
        var p = P($"Wedge {F(h)}x{F(l)} m", c, Shapes, "Hollow slope: ramps, prows, roof ends.", color: Steel);
        var H = c.Half;
        double y0 = c.Lo(), y1 = c.Hi(ny), z0 = c.Lo(), z1 = c.Hi(nz);
        var m = Math.Min(10, 3 + 2 * Math.Max(ny, nz));
        var dz = (z1 - z0) / m;
        for (var k = 0; k < m; k++)
            p.Add(Box([-H, y0, z0 + dz * k], [H, y0 + (y1 - y0) * (k + 0.5) / m, z0 + dz * (k + 1)], hidden: true));
        p.Add(Box([-H, y0, z0], [H, y1, z1], detail: true, shape: Shape(("shape", "wedge"), ("taper", Py.L(1, 0)), ("shift", Py.L(0, -0.5)))));
        foreach (var k in Pick(nz, 3))
            p.Sock($"bottom{k}", [0, y0, k * c.Cell], "-y");
        foreach (var j in Pick(ny, 3))
            p.Sock($"back{j}", [0, j * c.Cell, z1], "+z");
        return SteelStats(p, (h + l) * c.Cell * 2);
    }

    // ---- Stacks

    private static Part Smokestack(Cls c, int n, int tall)
    {
        double s = n * c.Cell, h = tall * c.Cell;
        var p = P($"Smokestack {F(s)}x{F(h)} m", c, Stacks, "Round hollow smokestack. Tall ones need stays.", color: Soot);
        double z0 = c.Lo(), z1 = c.Hi(tall), a = c.Lo(), b = c.Hi(n);
        var t = Math.Max(c.Plate, s * 0.05);
        // A square of walls for collision (a stack's bore is not worth a tree of boxes): rounds find it hollow, players can't climb in.
        p.Add(Box([a, a, z0], [b, a + t, z1], hidden: true));
        p.Add(Box([a, b - t, z0], [b, b, z1], hidden: true));
        p.Add(Box([a, a + t, z0], [a + t, b - t, z1], hidden: true));
        p.Add(Box([b - t, a + t, z0], [b, b - t, z1], hidden: true));
        p.Add(Box([a, a, z0], [b, b, z1], detail: true, shape: Shape(("shape", "tube"), ("segments", 20), ("inner", 0.86))));
        var bands = Math.Min(4, tall + 1);
        for (var k = 0; k < bands; k++)
        {
            var z = z0 + (z1 - z0) * (k + 0.5) / bands;
            p.Add(Box([a - 0.01 * c.X, a - 0.01 * c.X, z], [b + 0.01 * c.X, b + 0.01 * c.X, z + h * 0.02], Iron, detail: true, shape: Shape(("shape", "tube"), ("segments", 20), ("inner", 0.9))));
        }
        foreach (var i in Pick(n, 3))
            foreach (var j in Pick(n, 3))
                p.Sock($"base{i}.{j}", [i * c.Cell, j * c.Cell, z0], "-z");
        foreach (var k in Pick(tall, 3))
        {
            p.Sock($"+x{k}", [b, n % 2 != 0 ? (a + b) / 2 : 0, k * c.Cell], "+x");
            p.Sock($"-x{k}", [a, n % 2 != 0 ? (a + b) / 2 : 0, k * c.Cell], "-x");
        }
        return SteelStats(p, Math.PI * s * h, 0.8);
    }

    private static Part Chimney(Cls c, int n, int tall)
    {
        double s = n * c.Cell, h = tall * c.Cell;
        var p = P($"Chimney {F(s)}x{F(h)} m", c, Stacks, "Square brick chimney.", color: Brick, material: "stone");
        double a = c.Lo(), b = c.Hi(n), z0 = c.Lo(), z1 = c.Hi(tall);
        p.Add(Box([a, a, z0], [b, b, z1]));
        var cap = s * 0.07;
        p.Add(Box([a - cap, a - cap, z1 - s * 0.3], [b + cap, b + cap, z1 + 0.01 * c.X], Soot, detail: true));
        Sides(p, n, n, tall, limit: 2);
        return SteelStats(p, 4 * s * h, 0.8);
    }

    // ---- Stairs

    private static Part Stair(Cls c)
    {
        var H = c.Half;
        var h = c.Cell;
        var steps = Math.Min(30, (int)Math.Ceiling(h / 0.5)); // (World.StepHeight is 0.55 m: every class's stair is walked)
        var rise = h / steps;
        const int Runs = 1;
        var p = P($"Stair {F(h)} m", c, Stairs, $"{steps} steps up one cell.", color: Iron);
        double y0 = c.Lo(), z0 = c.Lo(), z1 = c.Hi(Runs);
        var d = (z1 - z0) / steps;
        for (var k = 0; k < steps; k++)
            p.Add(Box([-H, y0 + rise * k, z0 + d * k], [H, y0 + rise * (k + 1), z1]));
        p.Sock("bottom", [0, y0, 0], "-y");
        p.Sock("back", [0, 0, z1], "+z");
        p.Sock("left", [-H, 0, 0], "-x");
        p.Sock("right", [H, 0, 0], "+x");
        p.Sock("top", [0, y0 + h, z1 - d / 2], "+y");
        p.D["walk"] = "stairs";
        return SteelStats(p, h * c.Cell * 2, 0.6);
    }

    private static Part Ladder(Cls c)
    {
        var h = c.Cell;
        var w = Math.Min(1.0, c.Cell);
        var steps = Math.Min(30, (int)Math.Ceiling(h / 0.375));
        double rise = h / steps, run = 0.25;
        var p = P($"Ship Ladder {F(h)} m", c, Stairs, $"Steep ladder, {steps} steps up one cell.", color: Iron);
        double y0 = c.Lo(), z1 = c.Hi(1);
        for (var k = 0; k < steps; k++)
            p.Add(Box([-w / 2, y0 + rise * k, z1 - run * (steps - k)], [w / 2, y0 + rise * (k + 1), z1]));
        p.Sock("bottom", [0, y0, z1 - run * steps / 2], "-y");
        p.Sock("back", [0, 0, z1], "+z");
        p.D["walk"] = "ladder";
        return SteelStats(p, h * 3, 0.3);
    }

    // ---- Adapters

    /// <summary>
    /// A pad with one big socket of class a on its back and a grid of class b on its front: what makes small things sit on big ones. Its
    /// back is on a whole centimetre; a small class's front on a half one, so what bolts on stays on the 1 cm grid.
    /// </summary>
    private static Part Adapter(Cls a, Cls b, int m = 4)
    {
        var step = b.Cell * Math.Max(1, m / 4);
        var size = step * 4 + b.Cell;
        var p = new Part($"Adapter {a.Name} to {(b.Name.Length > 0 ? b.Name : "Small")}{(m > 4 ? " Wide" : "")}", a, Adapters,
            $"Mounting pad: back on a {a.Key} socket, {b.Key} sockets on the front.", color: Brass);
        p.D["centreSockets"] = false;
        double back = -0.02, front = b.Key == "small" ? 0.025 : 0.03;
        var half = size / 2;
        p.Add(Box([-half, back, -half], [half, front, half]));
        p.Sock("back", [0, back, 0], "-y");
        for (var i = -2; i < 3; i++)
            for (var j = -2; j < 3; j++)
                p.Sock($"front{i + 2}.{j + 2}", [i * step, front, j * step], "+y", cls: b);
        p.Stats(size * size * b.KgM2, 40 + size * size * b.HealthM2, b.Armour, b.Seconds);
        return p;
    }

    // ---- The list

    private static List<(string, Part)> Everything()
    {
        var o = new List<(string, Part)>();
        void Add(Cls c, string folder, Part p) => o.Add(($"{c.Key}/{folder}", p));
        (int, int)[] five = [(1, 1), (2, 1), (2, 2), (4, 2), (4, 4)];
        foreach (var c in Big)
        {
            Add(c, "10-Frames", Node(c));
            foreach (var n in new[] { 1, 2, 3, 4, 6, 8 })
                Add(c, "10-Frames", Girder(c, n));
            foreach (var n in new[] { 1, 2, 4, 8 })
            {
                Add(c, "10-Frames", BoxGirder(c, n));
                Add(c, "10-Frames", Truss(c, n));
                Add(c, "10-Frames", Column(c, n));
            }
            foreach (var n in new[] { 1, 2, 4 })
            {
                Add(c, "10-Frames", Brace(c, n));
                Add(c, "10-Frames", Buttress(c, n));
                Add(c, "10-Frames", Corbel(c, n));
            }
            foreach (var kind in PlateKinds)
                foreach (var (nx, nz) in five)
                    Add(c, "20-Plates", Plate(kind, c, nx, nz));
            foreach (var kind in new[] { "deck", "timber", "grate" })
                foreach (var (nx, nz) in five)
                    Add(c, "30-Decks", Deck(c, nx, nz, kind));
            foreach (var (nx, ny) in five)
                Add(c, "40-Walls", WindowWall(c, nx, ny));
            foreach (var n in new[] { 1, 2, 3 })
            {
                Add(c, "40-Walls", ArchWall(c, n));
                Add(c, "30-Decks", Balcony(c, n));
            }
            foreach (var (nx, nz) in new[] { (2, 2), (4, 2), (4, 4) })
                Add(c, "30-Decks", Terrace(c, nx, nz));
            foreach (var n in new[] { 1, 2, 3, 4 })
                Add(c, "50-Roofs", Dome(c, n));
            foreach (var n in new[] { 1, 2 })
            {
                Add(c, "50-Roofs", Onion(c, n));
                Add(c, "50-Roofs", Spire(c, n, 2 * n));
                Add(c, "50-Roofs", Spire(c, n, 4 * n));
            }
            foreach (var n in new[] { 1, 2, 4 })
                Add(c, "50-Roofs", PyramidRoof(c, n));
            foreach (var (nx, nz) in new[] { (2, 1), (2, 2), (2, 4), (4, 4) })
            {
                Add(c, "50-Roofs", GableRoof(c, nx, nz));
                Add(c, "50-Roofs", BarrelRoof(c, nx, nz));
            }
            foreach (var (n, nx) in new[] { (1, 1), (1, 2), (2, 2), (2, 4), (4, 4) })
                Add(c, "60-Shapes", CurvedPlate(c, n, nx));
            foreach (var (ny, nz) in new[] { (1, 1), (1, 2), (2, 1), (2, 2), (2, 4) })
                Add(c, "60-Shapes", Wedge(c, ny, nz));
            foreach (var (n, tall) in new[] { (1, 2), (1, 4), (1, 8), (2, 4), (2, 8) })
            {
                Add(c, "70-Stacks", Smokestack(c, n, tall));
                Add(c, "70-Stacks", Chimney(c, n, tall));
            }
            Add(c, "80-Stairs", Stair(c));
            Add(c, "80-Stairs", Ladder(c));
        }
        foreach (var c in new[] { Large, Capital })
        {
            foreach (var (nx, ny, nz) in new[] { (1, 1, 2), (2, 1, 2), (2, 1, 4), (4, 1, 4), (4, 2, 4) })
                Add(c, "90-Hull", HullSection(c, nx, ny, nz));
            foreach (var (nx, ny, nz) in new[] { (2, 1, 2), (2, 1, 4), (4, 1, 4), (4, 2, 4) })
                Add(c, "90-Hull", Bow(c, nx, ny, nz));
            foreach (var n in new[] { 2, 4, 8 })
                Add(c, "90-Hull", Keel(c, n));
            foreach (var (nx, nz) in new[] { (1, 1), (2, 1), (2, 2), (4, 2), (4, 4), (8, 4) })
                Add(c, "95-Chassis", ChassisDeck(c, nx, nz));
            foreach (var n in new[] { 2, 4, 8 })
                Add(c, "95-Chassis", Spreader(c, n));
        }
        foreach (var (a, b) in new[] { (Medium, Small), (Large, Medium), (Large, Small), (Capital, Large), (Capital, Medium), (Capital, Small) })
            Add(a, "99-Adapters", Adapter(a, b));
        foreach (var (a, b) in new[] { (Large, Small), (Capital, Small), (Capital, Medium) })
            Add(a, "99-Adapters", Adapter(a, b, m: 8));
        return o;
    }

    /// <summary>A small example of the kit, for the parts menu: a chassis deck, columns, a storey, an arcade, a dome and a stack.</summary>
    private static PyDict TownBlock()
    {
        const string Deck = "Large Chassis Deck 16x16 m", Floor = "Large Deck 16x16 m", Col = "Large Column 8 m";
        var steps = Py.L(Kw(("part", Deck)));
        foreach (var socket in new[] { "top0.0", "top3.0", "top0.3", "top3.3" })
            steps.Add(Kw(("part", Col), ("on", 0), ("socket", socket), ("by", "-z")));
        steps.Add(Kw(("part", Floor), ("on", 1), ("socket", "+z"), ("by", "back0.0")));
        var floorStep = steps.Count - 1;
        steps.Add(Kw(("part", "Large Window Wall 8x8 m"), ("on", 0), ("socket", "top1.0"), ("by", "bottom0")));
        steps.Add(Kw(("part", "Large Window Wall 8x8 m"), ("on", 0), ("socket", "top1.3"), ("by", "bottom0")));
        steps.Add(Kw(("part", "Large Dome 8 m"), ("on", floorStep), ("socket", "front0.0"), ("by", "base0.0")));
        steps.Add(Kw(("part", "Large Smokestack 4x32 m"), ("on", floorStep), ("socket", "front3.3"), ("by", "base0.0")));
        return Kw(("format", "call-of-slop-construct-blueprint/1"), ("name", "Town Block"), ("description", "A block of a traction city, from Large parts."), ("steps", steps));
    }

    public void Make(PartOutput o)
    {
        WriteParts(o, Out, Everything());
        o.File("Constructs/Blueprints/TownBlock.json", PyJson.Dumps(TownBlock(), 2) + "\n");
    }
}
