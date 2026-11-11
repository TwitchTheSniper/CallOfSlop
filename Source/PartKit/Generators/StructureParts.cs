using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The Sandbox's structural construct parts (Constructs/Parts/Structure) at the agreed scale (the developer's chart of 2026-10-29,
/// "Realistic": a frame step of 0.25 m, beams and plates 0.25 to 2 m). Was tools/constructs/structure_parts.py; docs/sandbox/CONSTRUCTS.md,
/// "Structural parts", says how they fit together.
///
/// The rules every part here keeps (the "Structural parts" self-test checks them in every turn): a part is made of 0.25 m cells, its origin
/// the centre of one cell, so cell centres are whole centimetres and cell faces 12.5 cm off them; every socket sits on a cell face, at the
/// middle of the cell's face (the panels' edge sockets 1 cm inside the plate), so two mating sockets put the new part's origin on a whole
/// centimetre and the 0.25 m lattice carries on through every part; a thin plate lies on the face of its cells, so a plate on a beam sits
/// flush on it.
/// </summary>
public sealed class StructureParts : IPartGenerator
{
    public string Name => "structure_parts";

    private const double C = 0.25; // the frame step (one cell)
    private const double H = C / 2; // half a cell: a cell face from its centre
    private const string Out = "Constructs/Parts/Structure";

    private static Part P(string name, string group, string section, string desc, string role = "frame", string tool = "welder", string material = "steel", string color = "#7c838c") =>
        new(name, Small, section, desc, role: role, tool: tool, material: material, color: color, group: group);

    private static double CellsMax(int n) => n * C - H;

    /// <summary>f"{v:.2f}" with the trailing zeros and point stripped.</summary>
    private static string F(double v) => v.ToString("F2", System.Globalization.CultureInfo.InvariantCulture).TrimEnd('0').TrimEnd('.');

    private static readonly (string D, int Axis, int Sign)[] Dirs = [("+x", 0, 1), ("-x", 0, -1), ("+y", 1, 1), ("-y", 1, -1), ("+z", 2, 1), ("-z", 2, -1)];

    /// <summary>Sockets on the faces of an nx x ny x nz block of cells (origin: the centre of cell 0,0,0), one per cell face.</summary>
    private static void FacesOfBlock(Part p, int nx, int ny, int nz, string faces = "xyz", string[]? skip = null)
    {
        int[] dims = [nx, ny, nz];
        for (var axis = 0; axis < 3; axis++)
        {
            var a = "xyz"[axis];
            if (!faces.Contains(a))
                continue;
            var others = Enumerable.Range(0, 3).Where(k => k != axis).ToArray();
            foreach (var (sign, sname) in new[] { (-1, "-"), (1, "+") })
            {
                var d = sname + a;
                if (skip is not null && skip.Contains(d))
                    continue;
                int u = others[0], v = others[1];
                foreach (var i in Pick(dims[u], 99))
                    foreach (var j in Pick(dims[v], 99))
                    {
                        var at = new double[3];
                        at[axis] = sign > 0 ? CellsMax(dims[axis]) : -H;
                        at[u] = i * C;
                        at[v] = j * C;
                        var nm = dims[u] * dims[v] == 1 ? d : dims[u] > 1 && dims[v] > 1 ? $"{d}{i}{j}" : $"{d}{(dims[u] > 1 ? i : j)}";
                        p.Sock(nm, at, d);
                    }
            }
        }
    }

    // ---- Beams

    private const string Beams = "Beams";

    /// <summary>Darker rings at each 0.25 m along a beam (detail): its length reads at a glance.</summary>
    private static void Seams(Part p, int n, double half, string color)
    {
        for (var k = 1; k < n; k++)
        {
            var z = k * C - H;
            p.Add(Box([-half - 0.003, -half - 0.003, z - 0.008], [half + 0.003, half + 0.003, z + 0.008], color, detail: true));
        }
    }

    private static Part SquareBeam(int n)
    {
        var L = n * C;
        var p = P($"Square Beam {F(L)} m", Beams, "Square beams", n > 1 ? "Square beam: frames, rails, posts." : "The frame's node: sockets on all six faces.", color: "#6f767f");
        p.Add(Box([-H, -H, -H], [H, H, CellsMax(n)]));
        Seams(p, n, H, "#5d636b");
        FacesOfBlock(p, 1, 1, n);
        return p.Stats(2.5 * n, 50 + 100 * L, 8, n <= 2 ? 1.5 : 2.5);
    }

    private static Part IBeam(int n)
    {
        var L = n * C;
        var p = P($"I-Beam {F(L)} m", Beams, "I-beams", "Tougher than a square beam for the same weight.", color: "#5f6670");
        double z0 = -H, z1 = CellsMax(n), f = 0.03;
        p.Add(Box([-H, H - f, z0], [H, H, z1]));
        p.Add(Box([-H, -H, z0], [H, -H + f, z1]));
        p.Add(Box([-0.015, -H + f, z0], [0.015, H - f, z1], "#545a63"));
        FacesOfBlock(p, 1, 1, n);
        return p.Stats(3.2 * n, 60 + 140 * L, 10, 2.5);
    }

    private static Part Tube(int n, string kind)
    {
        var L = n * C;
        double half, mass;
        int seg, hp0, hp1, arm;
        string colour, name, desc;
        if (kind == "tube")
            (half, seg, mass, hp0, hp1, arm, colour, name, desc) = (0.055, 12, 1.0, 30, 60, 5, "#8a9098", $"Steel Tube {F(L)} m", "11 cm tube: roll cages, rails, light frames.");
        else
            (half, seg, mass, hp0, hp1, arm, colour, name, desc) = (0.025, 8, 0.4, 15, 30, 3, "#9aa0a6", $"Steel Rod {F(L)} m", "5 cm rod: railings, bars, grilles.");
        var p = P(name, Beams, "Tubes and rods", desc, color: colour);
        double z0 = -H, z1 = CellsMax(n);
        p.Add(Box([-half, -half, z0], [half, half, z1], hidden: true));
        p.Add(Box([-half, -half, z0], [half, half, z1], detail: true, shape: Kw(("shape", "cylinder"), ("segments", seg))));
        p.Sock("end-", [0, 0, z0], "-z");
        p.Sock("end+", [0, 0, z1], "+z");
        for (var k = 0; k < n; k++)
            foreach (var (d, at) in new (string, double[])[] { ("+x", [half, 0, k * C]), ("-x", [-half, 0, k * C]), ("+y", [0, half, k * C]), ("-y", [0, -half, k * C]) })
                p.Sock(n > 1 ? $"{d}{k}" : d, at, d);
        return p.Stats(mass * n, hp0 + hp1 * L, arm, 1.5);
    }

    private static Part AngleIron(int n)
    {
        var L = n * C;
        var p = P($"Angle Iron {F(L)} m", Beams, "Angle and channel", "L-section: edges, ledges, light bracing.", color: "#7a7f86");
        double z0 = -H, z1 = CellsMax(n), t = 0.03;
        p.Add(Box([-H, -H, z0], [H, -H + t, z1]));
        p.Add(Box([-H, -H + t, z0], [-H + t, H, z1]));
        FacesOfBlock(p, 1, 1, n, skip: ["+x", "+y"]);
        return p.Stats(1.0 * n, 30 + 70 * L, 6, 2.0);
    }

    private static Part Channel(int n)
    {
        var L = n * C;
        var p = P($"Channel {F(L)} m", Beams, "Angle and channel", "U-section: rails, runners, gutters.", color: "#727980");
        double z0 = -H, z1 = CellsMax(n), t = 0.03;
        p.Add(Box([-H, -H, z0], [H, -H + t, z1]));
        p.Add(Box([-H, -H + t, z0], [-H + t, H, z1]));
        p.Add(Box([H - t, -H + t, z0], [H, H, z1]));
        FacesOfBlock(p, 1, 1, n, skip: ["+y"]);
        return p.Stats(1.5 * n, 40 + 90 * L, 7, 2.0);
    }

    /// <summary>A bar from the bottom-front corner to the top-back corner of an ny x nz block of cells (Y-Z plane), as m steps.</summary>
    private static void StepsDiagonal(Part p, int ny, int nz, int m, double t, string? colour = null, bool yUp = true)
    {
        double y0 = -H, y1 = CellsMax(ny), z0 = -H, z1 = CellsMax(nz);
        double dy = (y1 - y0) / m, dz = (z1 - z0) / m;
        for (var k = 0; k < m; k++)
        {
            var ya = yUp ? y0 + dy * k : y1 - dy * (k + 1);
            p.Add(Box([-t, Math.Max(y0, ya - dy * 0.35), z0 + dz * k], [t, Math.Min(y1, ya + dy * 1.35), z0 + dz * (k + 1)], colour));
        }
    }

    private static Part Brace(int ny, int nz)
    {
        double hy = ny * C, hz = nz * C;
        var length = Math.Sqrt(hy * hy + hz * hz);
        var (name, sec) = ny == nz ? ($"Brace 45 {F(hy)} m", "45 degree brace") : ($"Brace 1:2 {F(hy)}x{F(hz)} m", "Shallow 1:2 brace");
        var p = P(name, Beams, "Braces", sec + ": stiffens a frame's corner.", color: "#7f868e");
        var m = Math.Min(16, Math.Max(4, 2 * Math.Max(ny, nz)));
        StepsDiagonal(p, ny, nz, m, 0.025);
        p.Sock("bottom", [0, -H, 0], "-y");
        p.Sock("front", [0, 0, -H], "-z");
        p.Sock("top", [0, CellsMax(ny), (nz - 1) * C], "+y");
        p.Sock("back", [0, (ny - 1) * C, CellsMax(nz)], "+z");
        return p.Stats(3 * length, 30 + 60 * length, 5, 1.8);
    }

    private static Part CrossBrace(int n)
    {
        var s = n * C;
        var p = P($"Cross Brace {F(s)} m", Beams, "Braces", "X brace: keeps a frame panel square.", color: "#7f868e");
        var m = Math.Min(8, 2 * n);
        StepsDiagonal(p, n, n, m, 0.025);
        StepsDiagonal(p, n, n, m, 0.025, yUp: false);
        double top = CellsMax(n), back = CellsMax(n);
        foreach (var (d, at, dir) in new (string, double[], string)[]
        {
            ("bottom0", [0, -H, 0], "-y"), ("bottom1", [0, -H, (n - 1) * C], "-y"),
            ("top0", [0, top, 0], "+y"), ("top1", [0, top, (n - 1) * C], "+y"),
            ("front0", [0, 0, -H], "-z"), ("front1", [0, (n - 1) * C, -H], "-z"),
            ("back0", [0, 0, back], "+z"), ("back1", [0, (n - 1) * C, back], "+z"),
        })
            p.Sock(d, at, dir);
        return p.Stats(6 * s * 1.414, 40 + 100 * s, 5, 2.2);
    }

    /// <summary>Solid boxes for a set of cells (merged along X where they run) and a socket on every cell face on the bounds.</summary>
    private static void Voxel(Part p, (int X, int Y, int Z)[] cellList, string? colour = null)
    {
        var cells = cellList.ToHashSet();
        int[] lo = [cells.Min(c => c.X), cells.Min(c => c.Y), cells.Min(c => c.Z)];
        int[] hi = [cells.Max(c => c.X), cells.Max(c => c.Y), cells.Max(c => c.Z)];
        var done = new HashSet<(int, int, int)>();
        foreach (var c in cells.OrderBy(c => c.Y).ThenBy(c => c.Z).ThenBy(c => c.X))
        {
            if (done.Contains(c))
                continue;
            var x1 = c.X;
            while (cells.Contains((x1 + 1, c.Y, c.Z)) && !done.Contains((x1 + 1, c.Y, c.Z)))
                x1++;
            for (var x = c.X; x <= x1; x++)
                done.Add((x, c.Y, c.Z));
            p.Add(Box([c.X * C - H, c.Y * C - H, c.Z * C - H], [x1 * C + H, c.Y * C + H, c.Z * C + H], colour));
        }
        foreach (var c in cells.OrderBy(c => c.X).ThenBy(c => c.Y).ThenBy(c => c.Z))
            foreach (var (d, axis, sign) in Dirs)
            {
                int[] cv = [c.X, c.Y, c.Z];
                var nb = (int[])cv.Clone();
                nb[axis] += sign;
                if (cells.Contains((nb[0], nb[1], nb[2])))
                    continue;
                if ((sign > 0 && cv[axis] != hi[axis]) || (sign < 0 && cv[axis] != lo[axis]))
                    continue; // (an inside face: not on the part's bounds)
                double[] at = [cv[0] * C, cv[1] * C, cv[2] * C];
                at[axis] += sign * H;
                p.Sock($"{d}{cv[0] - lo[0]}{cv[1] - lo[1]}{cv[2] - lo[2]}".Replace("-", "m").Replace("+", "p"), at, d);
            }
    }

    private static Part Junction(string name, (int, int, int)[] cells, string desc)
    {
        var p = P(name, Beams, "Junctions", desc, color: "#6a7078");
        Voxel(p, cells);
        p.Add(Box([-H - 0.006, -H - 0.006, -H - 0.006], [H + 0.006, H + 0.006, H + 0.006], "#596068", detail: true));
        var n = cells.Length;
        return p.Stats(2.5 * n, 60 + 30 * n, 8, 2.0);
    }

    private static Part EndCap(string kind)
    {
        Part p;
        if (kind == "beam")
        {
            p = P("Beam End Cap", Beams, "Caps", "Caps the open end of a beam.", color: "#7d848c");
            var t = 0.04;
            p.Add(Box([-H, -H, -H], [H, H, -H + t]));
            p.Add(Box([-H + 0.02, -H + 0.02, -H + t], [H - 0.02, H - 0.02, -H + t + 0.015], "#6a7078", detail: true, shape: Kw(("shape", "bevel"), ("radius", 0.4))));
            p.Sock("back", [0, 0, -H], "-z");
            p.Sock("front", [0, 0, -H + t], "+z");
            return p.Stats(1.2, 60, 8, 1.2);
        }
        p = P("Tube End Cap", Beams, "Caps", "Caps the end of a tube.", color: "#8a9098");
        double tt = 0.03, half = 0.055;
        p.Add(Box([-half, -half, -H], [half, half, -H + tt], hidden: true));
        p.Add(Box([-half, -half, -H], [half, half, -H + tt], detail: true, shape: Kw(("shape", "cylinder"), ("segments", 12))));
        p.Sock("back", [0, 0, -H], "-z");
        p.Sock("front", [0, 0, -H + tt], "+z");
        return p.Stats(0.5, 30, 5, 1.0);
    }

    private static Part Bracket(string kind)
    {
        var t = 0.02;
        if (kind == "L")
        {
            var pl = P("L Bracket", Beams, "Brackets and gussets", "Inside corner between a floor and a wall.", color: "#8c9298");
            pl.Add(Box([-H, -H, -H], [H, -H + t, H]));
            pl.Add(Box([-H, -H + t, -H], [-H + t, H, H]));
            pl.Sock("-y", [0, -H, 0], "-y");
            pl.Sock("-x", [-H, 0, 0], "-x");
            pl.Sock("-z", [0, 0, -H], "-z");
            pl.Sock("+z", [0, 0, H], "+z");
            return pl.Stats(0.8, 40, 5, 1.2);
        }
        var p = P("Corner Bracket", Beams, "Brackets and gussets", "Inside corner of a floor and two walls.", color: "#8c9298");
        p.Add(Box([-H, -H, -H], [H, -H + t, H]));
        p.Add(Box([-H, -H + t, -H], [-H + t, H, H]));
        p.Add(Box([-H + t, -H + t, -H], [H, H, -H + t]));
        p.Sock("-y", [0, -H, 0], "-y");
        p.Sock("-x", [-H, 0, 0], "-x");
        p.Sock("-z", [0, 0, -H], "-z");
        return p.Stats(1.2, 50, 5, 1.2);
    }

    private static Part Gusset(int n)
    {
        var s = n * C;
        var p = P($"Gusset {F(s)} m", Beams, "Brackets and gussets", "Stiffens a corner.", color: "#858b92");
        var t = 0.015;
        var m = n == 1 ? 3 * n : 2 * n;
        double y0 = -H, top = CellsMax(n), z0 = -H, z1 = CellsMax(n);
        var dz = (z1 - z0) / m;
        for (var k = 0; k < m; k++)
        {
            // Columns from the front: shortest at the front, full height at the back.
            var h = (top - y0) * (k + 1) / m;
            p.Add(Box([-t, y0, z0 + dz * k], [t, y0 + h, z0 + dz * (k + 1)]));
        }
        for (var k = 0; k < n; k++)
        {
            p.Sock(n > 1 ? $"bottom{k}" : "bottom", [0, -H, k * C], "-y");
            p.Sock(n > 1 ? $"back{k}" : "back", [0, k * C, z1], "+z");
        }
        return p.Stats(4 * s * s + 0.4, 30 + 80 * s, 6, 1.2);
    }

    // ---- Panels

    private const string Panels = "Panels", Armour = "Armour";

    private sealed record Mat(string Sec, string Group, double T, int KgM2, int Hb, int Hm, int Arm, string Tool, double Secs, string Colour, string Material, string Words);

    private static readonly Dictionary<string, Mat> Mats = new()
    {
        ["sheet"] = new("Sheet steel", Panels, 0.02, 8, 20, 50, 6, "welder", 1.5, "#a3a9ae", "steel", "2 cm sheet: stops shotgun pellets, not rifles."),
        ["plate"] = new("Plate steel", Panels, 0.05, 20, 40, 120, 20, "welder", 2.5, "#8a9096", "steel", "5 cm plate: stops pistols, shotgun pellets, weak SMG rounds."),
        ["wood"] = new("Wood", Panels, 0.05, 7, 25, 65, 1, "wrench", 1.2, "#8a6a40", "wood", "5 cm planks: light cover that burns."),
        ["glass"] = new("Glass", Panels, 0.03, 9, 8, 20, 0, "wrench", 1.2, "#9fc6d6", "glass", "Glass: any bullet breaks it."),
        ["mesh"] = new("Mesh and grate", Panels, 0.03, 5, 30, 50, 3, "welder", 1.5, "#6e7378", "steel", "Grate: walk on it, see and shoot through."),
        ["tread"] = new("Tread plate", Panels, 0.03, 12, 40, 100, 10, "welder", 2.0, "#9a9ea2", "steel", "3 cm tread plate: floors, decks, steps."),
        ["armour"] = new("Armour plate", Armour, 0.08, 45, 80, 240, 40, "welder", 3.5, "#5d6650", "armour", "8 cm armour: stops SMGs, rifles, LMGs, shotguns, most pistols."),
        ["heavy"] = new("Heavy armour", Armour, 0.12, 80, 150, 400, 68, "welder", 5.0, "#4a5240", "armour", "12 cm armour: only snipers and heavy guns get through."),
    };

    private static readonly Dictionary<string, string> MatName = new()
    {
        ["sheet"] = "Sheet Steel", ["plate"] = "Plate Steel", ["wood"] = "Wood Planks", ["glass"] = "Glass Pane", ["mesh"] = "Mesh Grate",
        ["tread"] = "Tread Plate", ["armour"] = "Armour Plate", ["heavy"] = "Heavy Armour",
    };

    /// <summary>A plate's sockets: its back (on the cell face) and front at cell centres, its four edges 1 cm into the plate.</summary>
    private static void PanelSockets(Part p, int nx, int nz, double t, bool front = true)
    {
        var backY = -H;
        foreach (var i in Pick(nx, 4))
            foreach (var k in Pick(nz, 4))
                p.Sock(nx * nz > 1 ? $"back{i}{k}" : "back", [i * C, backY, k * C], "-y");
        var edgeY = -0.12; // (1 cm in from the back: a whole centimetre, inside the thinnest plate)
        foreach (var (d, axis, nAlong, pos) in new[] { ("-x", 0, nz, -H), ("+x", 0, nz, CellsMax(nx)), ("-z", 2, nx, -H), ("+z", 2, nx, CellsMax(nz)) })
            foreach (var j in Pick(nAlong, 4))
            {
                var at = new[] { 0.0, edgeY, 0.0 };
                at[axis] = pos;
                at[2 - axis] = j * C;
                p.Sock(nAlong > 1 ? $"{d}{j}" : d, at, d);
            }
        if (front)
        {
            var left = 40 - p.Sockets.Count;
            var per = Math.Max(1, (int)Math.Sqrt(left));
            foreach (var i in Pick(nx, Math.Min(per, 3)))
                foreach (var k in Pick(nz, Math.Min(per, 3)))
                    p.Sock(nx * nz > 1 ? $"front{i}{k}" : "front", [i * C, backY + t, k * C], "+y");
        }
    }

    private static string SizeName(int nx, int nz)
    {
        double a = nx * C, b = nz * C;
        return nx == nz ? $"{F(a)} m" : $"{F(a)}x{F(b)} m";
    }

    private static void Bolts(Part p, int nx, int nz, double t, string colour)
    {
        double x1 = CellsMax(nx), z1 = CellsMax(nz);
        foreach (var x in new[] { -H + 0.04, x1 - 0.04 })
            foreach (var z in new[] { -H + 0.04, z1 - 0.04 })
                p.Add(Box([x - 0.015, -H + t, z - 0.015], [x + 0.015, -H + t + 0.012, z + 0.015], colour, detail: true));
    }

    private static Part Panel(string mat, int nx, int nz)
    {
        var m = Mats[mat];
        var area = nx * nz * C * C;
        var p = P($"{MatName[mat]} {SizeName(nx, nz)}", m.Group, m.Sec, m.Words, role: "panel", tool: m.Tool, material: m.Material, color: m.Colour);
        double x0 = -H, x1 = CellsMax(nx), z0 = -H, z1 = CellsMax(nz), y0 = -H, y1 = -H + m.T;
        if (mat == "wood")
        {
            // Planks along Z, 0.25 m wide, in two shades.
            for (var i = 0; i < nx; i++)
                p.Add(Box([i * C - H, y0, z0], [i * C + H, y1, z1], i % 2 == 0 ? "#8a6a40" : "#7b5e38"));
            if (nx * 1 + 2 <= 16)
                foreach (var z in new[] { z0 + 0.06, z1 - 0.06 })
                    p.Add(Box([x0 + 0.01, y1, z - 0.02], [x1 - 0.01, y1 + 0.01, z + 0.02], "#6a5030", detail: true));
        }
        else if (mat == "glass")
        {
            var f = 0.03;
            p.Add(Box([x0, y0, z0], [x1, y1, z0 + f], "#5a6068"));
            p.Add(Box([x0, y0, z1 - f], [x1, y1, z1], "#5a6068"));
            p.Add(Box([x0, y0, z0 + f], [x0 + f, y1, z1 - f], "#5a6068"));
            p.Add(Box([x1 - f, y0, z0 + f], [x1, y1, z1 - f], "#5a6068"));
            p.Add(Box([x0 + f, y0 + 0.01, z0 + f], [x1 - f, y1 - 0.01, z1 - f], glass: true)); // (drawn see-through, sorted)
        }
        else if (mat == "mesh")
        {
            var f = 0.03;
            p.Add(Box([x0, y0, z0], [x1, y1, z0 + f]));
            p.Add(Box([x0, y0, z1 - f], [x1, y1, z1]));
            p.Add(Box([x0, y0, z0 + f], [x0 + f, y1, z1 - f]));
            p.Add(Box([x1 - f, y0, z0 + f], [x1, y1, z1 - f]));
            // Bars along Z, every 0.125 m (0.25 m on the 2 m grate), and two cross ties drawn over them.
            var step = nx <= 4 ? C / 2 : C;
            var bars = (int)Py.RoundInt((x1 - x0) / step) - 1;
            for (var b = 0; b < bars; b++)
            {
                var x = x0 + step * (b + 1);
                p.Add(Box([x - 0.008, y0, z0 + f], [x + 0.008, y1, z1 - f], "#7d8288"));
            }
            foreach (var z in new[] { z0 + (z1 - z0) / 3, z0 + 2 * (z1 - z0) / 3 })
                if (p.Boxes.Count < 16)
                    p.Add(Box([x0 + f, y1 - 0.012, z - 0.006], [x1 - f, y1 + 0.004, z + 0.006], "#5f6469", detail: true));
        }
        else
        {
            p.Add(Box([x0, y0, z0], [x1, y1, z1]));
            if (mat == "tread")
            {
                // A raised pattern: short ridges in rows (detail).
                var rows = Math.Min(6, Math.Max(2, nz * 2 / 2));
                for (var k = 0; k < rows; k++)
                {
                    var z = z0 + (z1 - z0) * (k + 0.5) / rows;
                    var xa = x0 + 0.03 + (k % 2 != 0 ? 0.04 : 0);
                    p.Add(Box([xa, y1, z - 0.008], [x1 - 0.03, y1 + 0.006, z + 0.008], "#b1b5b9", detail: true));
                }
            }
            else if (mat is "plate" or "armour" or "heavy")
            {
                Bolts(p, nx, nz, m.T, mat != "plate" ? "#3f4636" : "#6a7076");
                if (mat != "plate")
                    p.Add(Box([x0 + 0.03, y1, z0 + 0.03], [x1 - 0.03, y1 + 0.01, z1 - 0.03], mat == "armour" ? "#545c47" : "#40472f", detail: true));
            }
        }
        PanelSockets(p, nx, nz, m.T);
        return p.Stats(m.KgM2 * area, m.Hb + m.Hm * area, m.Arm, area >= 0.5 ? m.Secs : Math.Max(1.0, m.Secs * 0.7));
    }

    /// <summary>A plate with a hole in it: four solid boxes round the hole (x0, z0, x1, z1 in metres from the plate's corner).</summary>
    private static Part Opening(string name, string mat, int nx, int nz, double[] hole, string desc, string section = "Openings", string group = Panels)
    {
        var m = Mats[mat];
        var p = P(name, group, section, desc, role: "panel", tool: m.Tool, material: m.Material, color: m.Colour);
        double x0 = -H, x1 = CellsMax(nx), z0 = -H, z1 = CellsMax(nz), y0 = -H, y1 = -H + m.T;
        double hx0 = x0 + hole[0], hz0 = z0 + hole[1], hx1 = x0 + hole[2], hz1 = z0 + hole[3];
        p.Add(Box([x0, y0, z0], [x1, y1, hz0]));
        p.Add(Box([x0, y0, hz1], [x1, y1, z1]));
        p.Add(Box([x0, y0, hz0], [hx0, y1, hz1]));
        p.Add(Box([hx1, y0, hz0], [x1, y1, hz1]));
        var lip = m.Material == "steel" ? "#4a5058" : "#363c2c";
        foreach (var (a, b) in new[] { ((hx0 - 0.02, hz0 - 0.02), (hx1 + 0.02, hz0)), ((hx0 - 0.02, hz1), (hx1 + 0.02, hz1 + 0.02)), ((hx0 - 0.02, hz0), (hx0, hz1)), ((hx1, hz0), (hx1 + 0.02, hz1)) })
            p.Add(Box([a.Item1, y1, a.Item2], [b.Item1, y1 + 0.012, b.Item2], lip, detail: true));
        if (group == Armour)
            p.Add(Box([x0 + 0.03, y1, z0 + 0.03], [hx0 - 0.03, y1 + 0.01, z1 - 0.03], mat == "armour" ? "#545c47" : "#40472f", detail: true));
        PanelSockets(p, nx, nz, m.T);
        var area = nx * nz * C * C - (hole[2] - hole[0]) * (hole[3] - hole[1]);
        return p.Stats(m.KgM2 * area, m.Hb + m.Hm * area, m.Arm, m.Secs);
    }

    private static Part Vent(string name, int nx, int nz)
    {
        var m = Mats["plate"];
        var p = P(name, Panels, "Openings", "Plate with three louvred slots.", role: "panel", tool: m.Tool, material: m.Material, color: m.Colour);
        double x0 = -H, x1 = CellsMax(nx), z0 = -H, z1 = CellsMax(nz), y0 = -H, y1 = -H + m.T;
        double slot = 0.06, bar = (z1 - z0 - 0.2 - 3 * 0.06) / 2;
        var edges = new List<double> { z0 + 0.1 };
        for (var k = 0; k < 3; k++)
        {
            edges.Add(edges[^1] + slot);
            if (k < 2)
                edges.Add(edges[^1] + bar);
        }
        // Bottom and top bands, side posts, and the bars between the slots.
        p.Add(Box([x0, y0, z0], [x1, y1, edges[0]]));
        p.Add(Box([x0, y0, edges[^1]], [x1, y1, z1]));
        p.Add(Box([x0, y0, edges[0]], [x0 + 0.15, y1, edges[^1]]));
        p.Add(Box([x1 - 0.15, y0, edges[0]], [x1, y1, edges[^1]]));
        for (var k = 0; k < 2; k++)
            p.Add(Box([x0 + 0.15, y0, edges[1 + 2 * k]], [x1 - 0.15, y1, edges[2 + 2 * k]]));
        for (var k = 0; k < 3; k++)
        {
            var a = edges[2 * k];
            p.Add(Box([x0 + 0.15, y1, a - 0.005], [x1 - 0.15, y1 + 0.02, a + 0.012], "#6a7076", detail: true));
        }
        PanelSockets(p, nx, nz, m.T);
        var area = nx * nz * C * C - 3 * slot * (x1 - x0 - 0.3);
        return p.Stats(m.KgM2 * area, m.Hb + m.Hm * area, m.Arm, m.Secs);
    }

    private static Part Triangle(string mat, int n)
    {
        var m = Mats[mat];
        var s = n * C;
        var p = P($"Triangle Plate {F(s)} m", Panels, "Triangles", "Plate triangle: noses, roofs, odd gaps.", role: "panel", tool: m.Tool, material: m.Material, color: m.Colour);
        double x0 = -H, z0 = -H, x1 = CellsMax(n), z1 = CellsMax(n), y0 = -H, y1 = -H + m.T;
        var mm = Math.Min(16, n > 1 ? 2 * n : 3);
        var dx = (x1 - x0) / mm;
        for (var k = 0; k < mm; k++)
        {
            // Columns from the -x side: full length there, short at the far corner.
            var length = (z1 - z0) * (mm - k) / mm;
            p.Add(Box([x0 + dx * k, y0, z0], [x0 + dx * (k + 1), y1, z0 + length]));
        }
        for (var i = 0; i < n; i++)
            for (var k = 0; k < n - i; k++)
                if (i + k < n - 1 || n == 1)
                    p.Sock($"back{i}{k}", [i * C, -H, k * C], "-y");
        foreach (var j in Pick(n, 4))
        {
            p.Sock($"-x{j}", [-H, -0.12, j * C], "-x");
            p.Sock($"-z{j}", [j * C, -0.12, -H], "-z");
        }
        var area = s * s / 2;
        return p.Stats(m.KgM2 * area, m.Hb + m.Hm * area, m.Arm, m.Secs);
    }

    // ---- Shapes

    private const string Shapes = "Shapes";

    /// <summary>Collision for a profile in the Y-Z plane: m slabs along Z, each as tall as height(t) at its middle (t: 0 front, 1 back).</summary>
    private static void Stepped(Part p, int ny, int nz, int nx, Func<double, double> height, int m, string? colour = null, bool hidden = true)
    {
        double y0 = -H, y1 = CellsMax(ny), z0 = -H, z1 = CellsMax(nz);
        var dz = (z1 - z0) / m;
        for (var k = 0; k < m; k++)
        {
            var h = height((k + 0.5) / m) * (y1 - y0);
            if (h >= 0.01)
                p.Add(Box([-H, y0, z0 + dz * k], [CellsMax(nx), y0 + h, z0 + dz * (k + 1)], colour, hidden: hidden));
        }
    }

    private static void ShapeSockets(Part p, int nx, int ny, int nz, bool sidesRow = true)
    {
        foreach (var i in Pick(nx, 4))
            foreach (var k in Pick(nz, 4))
                p.Sock(nx * nz > 1 ? $"bottom{i}{k}" : "bottom", [i * C, -H, k * C], "-y");
        foreach (var i in Pick(nx, 4))
            foreach (var j in Pick(ny, 4))
                p.Sock(nx * ny > 1 ? $"back{i}{j}" : "back", [i * C, j * C, CellsMax(nz)], "+z");
        if (sidesRow)
            foreach (var k in Pick(nz, 4))
                if (k >= nz / 2 || nz == 1)
                {
                    p.Sock(nz > 1 ? $"-x{k}" : "-x", [-H, 0, k * C], "-x");
                    p.Sock(nz > 1 ? $"+x{k}" : "+x", [CellsMax(nx), 0, k * C], "+x");
                }
    }

    private static double Round(double t) => Math.Sqrt(Math.Max(0.0, 1 - Math.Pow(1 - t, 2)));

    private static Part Wedge(int ny, int nz, int nx, string label)
    {
        double hy = ny * C, hz = nz * C;
        var name = ny != nz ? $"Wedge {label} {F(hy)}x{F(hz)} m" : $"Wedge {label} {F(hy)} m";
        var p = P(name, Shapes, "Wedges", "Solid slope: noses, ramps, roof edges.", color: "#80878f");
        var m = Math.Min(12, Math.Max(4, 2 * nz));
        Stepped(p, ny, nz, nx, t => t, m);
        p.Add(Box([-H, -H, -H], [CellsMax(nx), CellsMax(ny), CellsMax(nz)], detail: true, shape: Kw(("shape", "wedge"), ("taper", Py.L(1, 0)), ("shift", Py.L(0, -0.5)))));
        ShapeSockets(p, nx, ny, nz);
        var vol = nx * ny * nz * 0.5;
        return p.Stats(2.5 * vol, 40 + 20 * vol, 8, 2.0);
    }

    private static Part SlopeCorner(int n)
    {
        var s = n * C;
        var p = P($"Slope Corner {F(s)} m", Shapes, "Corners", "Outer corner where two 1:1 slopes meet.", color: "#80878f");
        var m = n > 1 ? Math.Max(3, 2 * n) : 3;
        double z0 = -H, z1 = CellsMax(n);
        var dz = (z1 - z0) / m;
        for (var k = 0; k < m; k++)
        {
            var f = (k + 0.5) / m; // 0 at the front (the point), 1 at the back (the base)
            var e = s * f;
            p.Add(Box([-H, -H, z0 + dz * k], [-H + e, -H + e, z0 + dz * (k + 1)], hidden: true));
        }
        p.Add(Box([-H, -H, -H], [CellsMax(n), CellsMax(n), CellsMax(n)], detail: true, shape: Kw(("shape", "wedge"), ("taper", Py.L(0, 0)), ("shift", Py.L(-0.5, -0.5)))));
        for (var i = 0; i < n; i++)
            for (var j = 0; j < n; j++)
                p.Sock(n > 1 ? $"back{i}{j}" : "back", [i * C, j * C, z1], "+z");
        // The two triangular faces: a socket each at their back row, where they are widest.
        p.Sock("bottom", [0, -H, (n - 1) * C], "-y");
        p.Sock("-x", [-H, 0, (n - 1) * C], "-x");
        var vol = n * n * n / 3.0;
        return p.Stats(2.5 * vol, 40 + 20 * vol, 8, 2.0);
    }

    private static Part InnerCorner(int n)
    {
        var s = n * C;
        var p = P($"Inner Corner {F(s)} m", Shapes, "Corners", "Inner corner where two 1:1 slopes meet.", color: "#80878f");
        // Height h(x, z) = max of the two slopes: layers of L shapes (two boxes each), from the bottom up.
        var m = n == 2 ? 4 : 6;
        var dy = s / m;
        double x1 = CellsMax(n), z1 = CellsMax(n);
        for (var k = 0; k < m; k++)
        {
            var y = -H + dy * k;
            var cut = s * (k + 0.5) / m; // the layer covers the back strip and the +x strip
            p.Add(Box([-H, y, -H + cut], [x1, y + dy, z1]));
            p.Add(Box([-H + cut, y, -H], [x1, y + dy, -H + cut]));
        }
        for (var i = 0; i < n; i++)
        {
            for (var k = 0; k < n; k++)
                p.Sock($"bottom{i}{k}", [i * C, -H, k * C], "-y");
            p.Sock($"back{i}", [i * C, 0, z1], "+z");
            p.Sock($"+x{i}", [x1, 0, i * C], "+x");
        }
        var vol = n * n * n * 2 / 3.0;
        return p.Stats(2.5 * vol, 40 + 20 * vol, 8, 2.0);
    }

    private static Part Pyramid(int n)
    {
        var s = n * C;
        var hgt = n / 2;
        var p = P($"Pyramid Cap {F(s)} m", Shapes, "Caps and trims", "Caps posts, towers and noses.", color: "#7d848c");
        double z0 = -H, z1 = CellsMax(hgt);
        var m = n == 2 ? 3 : 4;
        var dz = (z1 - z0) / m;
        for (var k = 0; k < m; k++)
        {
            var f = (k + 0.5) / m;
            var e = s * f;
            var c = (CellsMax(n) + -H) / 2;
            p.Add(Box([c - e / 2, c - e / 2, z0 + dz * k], [c + e / 2, c + e / 2, z0 + dz * (k + 1)], hidden: true));
        }
        p.Add(Box([-H, -H, z0], [CellsMax(n), CellsMax(n), z1], detail: true, shape: Kw(("shape", "wedge"), ("taper", Py.L(0, 0)), ("shift", Py.L(0, 0)))));
        for (var i = 0; i < n; i++)
            for (var j = 0; j < n; j++)
                p.Sock($"base{i}{j}", [i * C, j * C, z1], "+z");
        var vol = n * n * hgt / 3.0;
        return p.Stats(2.5 * vol + 0.5, 40 + 20 * vol, 8, 1.5);
    }

    private static Part QuarterRound(int n, int nx)
    {
        var s = n * C;
        var name = nx == n ? $"Quarter Round {F(s)} m" : $"Quarter Round {F(s)}x{F(nx * C)} m";
        var p = P(name, Shapes, "Rounded", "Rounded corners and edges.", color: "#80878f");
        var m = Math.Min(12, Math.Max(4, 3 * n));
        // Height at distance d from the back: sqrt(R^2 - d^2) (the centre is the bottom back edge).
        Stepped(p, n, n, nx, Round, m);
        p.Add(Box([-H, -H, -H], [CellsMax(nx), CellsMax(n), CellsMax(n)], detail: true, shape: Kw(("shape", "arc"), ("inner", 0), ("angle", 90), ("dir", 135), ("segments", Math.Max(6, 4 * n)))));
        ShapeSockets(p, nx, n, n);
        var vol = nx * n * n * Math.PI / 4;
        return p.Stats(2.5 * vol, 40 + 20 * vol, 8, 2.0);
    }

    private static Part CurvedPanel(int n, int nx)
    {
        var s = n * C;
        var t = 0.06;
        var p = P($"Curved Plate {F(s)} m", Shapes, "Rounded", "Quarter-pipe: a wall's top round onto a roof.", role: "panel", color: "#8a9096");
        var inner = 1 - t / s;
        // Collision: thin boxes round the arc (centre at the bottom back edge, from the bottom front to the top back).
        var m = Math.Min(12, 4 * n / 2 + 4);
        double y0 = -H, z1 = CellsMax(n);
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
            p.Add(Box([-H, Math.Max(y0, ys.Min()), Math.Max(-H, zs.Min())], [CellsMax(nx), Math.Min(CellsMax(n), ys.Max()), Math.Min(z1, zs.Max())], hidden: true));
        }
        p.Add(Box([-H, -H, -H], [CellsMax(nx), CellsMax(n), CellsMax(n)], detail: true, shape: Kw(("shape", "arc"), ("inner", R(inner)), ("angle", 90), ("dir", 135), ("segments", Math.Max(8, 4 * n)))));
        foreach (var i in Pick(nx, 4))
        {
            p.Sock(nx > 1 ? $"bottom{i}" : "bottom", [i * C, -H, 0], "-y");
            p.Sock(nx > 1 ? $"back{i}" : "back", [i * C, (n - 1) * C, z1], "+z");
        }
        var area = nx * C * s * Math.PI / 2;
        return p.Stats(20 * area, 40 + 120 * area, 20, 2.5);
    }

    private static Part Trim(string kind, int n)
    {
        var L = n * C;
        var round = kind == "round";
        var p = round
            ? P($"Round Trim {F(L)} m", Shapes, "Caps and trims", "Softens a box's edge.", color: "#858c94")
            : P($"Chamfer Trim {F(L)} m", Shapes, "Caps and trims", "Bevels a box's edge.", color: "#858c94");
        // Along X (the length), profile in Y-Z.
        Stepped(p, 1, 1, n, round ? Round : t => t, 4);
        if (round)
            p.Add(Box([-H, -H, -H], [CellsMax(n), H, H], detail: true, shape: Kw(("shape", "arc"), ("inner", 0), ("angle", 90), ("dir", 135), ("segments", 6))));
        else
            p.Add(Box([-H, -H, -H], [CellsMax(n), H, H], detail: true, shape: Kw(("shape", "wedge"), ("taper", Py.L(1, 0)), ("shift", Py.L(0, -0.5)))));
        for (var i = 0; i < n; i++)
        {
            p.Sock(n > 1 ? $"bottom{i}" : "bottom", [i * C, -H, 0], "-y");
            p.Sock(n > 1 ? $"back{i}" : "back", [i * C, 0, H], "+z");
        }
        p.Sock("-x", [-H, 0, 0], "-x");
        p.Sock("+x", [CellsMax(n), 0, 0], "+x");
        var vol = n * (round ? 0.785 : 0.5);
        return p.Stats(2.5 * vol, 30 + 20 * vol, 8, 1.5);
    }

    // ---- The list

    private static List<(string, Part)> Everything()
    {
        var o = new List<(string, Part)>();
        foreach (var n in new[] { 1, 2, 3, 4, 6, 8 })
            o.Add(("10-Beams/10-Square", SquareBeam(n)));
        foreach (var n in new[] { 2, 4, 6, 8 })
            o.Add(("10-Beams/20-IBeam", IBeam(n)));
        foreach (var n in new[] { 2, 4, 8 })
            o.Add(("10-Beams/30-Tube", Tube(n, "tube")));
        foreach (var n in new[] { 2, 4, 8 })
            o.Add(("10-Beams/30-Tube", Tube(n, "rod")));
        foreach (var n in new[] { 2, 4, 8 })
            o.Add(("10-Beams/40-Angle", AngleIron(n)));
        foreach (var n in new[] { 2, 4, 8 })
            o.Add(("10-Beams/40-Angle", Channel(n)));
        foreach (var (ny, nz) in new[] { (2, 2), (4, 4), (8, 8), (2, 4), (4, 8) })
            o.Add(("10-Beams/50-Brace", Brace(ny, nz)));
        foreach (var n in new[] { 2, 4 })
            o.Add(("10-Beams/50-Brace", CrossBrace(n)));
        o.Add(("10-Beams/60-Junction", Junction("Corner Junction", [(0, 0, 0), (1, 0, 0), (0, 0, 1)], "Two beams at a right angle.")));
        o.Add(("10-Beams/60-Junction", Junction("T Junction", [(-1, 0, 0), (0, 0, 0), (1, 0, 0), (0, 0, 1)], "A beam meets the middle of another.")));
        o.Add(("10-Beams/60-Junction", Junction("Cross Junction", [(0, 0, 0), (1, 0, 0), (-1, 0, 0), (0, 0, 1), (0, 0, -1)], "Two beams cross.")));
        o.Add(("10-Beams/60-Junction", Junction("3-Way Corner", [(0, 0, 0), (1, 0, 0), (0, 1, 0), (0, 0, 1)], "The corner of a box frame.")));
        o.Add(("10-Beams/60-Junction", Junction("6-Way Hub", [(0, 0, 0), (1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1)], "Six beams meet.")));
        o.Add(("10-Beams/70-Bracket", Bracket("L")));
        o.Add(("10-Beams/70-Bracket", Bracket("corner")));
        foreach (var n in new[] { 1, 2 })
            o.Add(("10-Beams/70-Bracket", Gusset(n)));
        o.Add(("10-Beams/80-Cap", EndCap("beam")));
        o.Add(("10-Beams/80-Cap", EndCap("tube")));

        (string, (int, int)[])[] sizes =
        [
            ("sheet", [(1, 1), (2, 2), (2, 4), (4, 4), (4, 8), (8, 8)]),
            ("plate", [(1, 1), (2, 2), (2, 4), (4, 4), (4, 8), (8, 8)]),
            ("wood", [(2, 2), (2, 4), (4, 8), (8, 8)]),
            ("glass", [(2, 2), (2, 4), (4, 4), (4, 8)]),
            ("mesh", [(2, 2), (4, 4), (4, 8), (8, 8)]),
            ("tread", [(2, 2), (4, 4), (4, 8), (8, 8)]),
        ];
        for (var k = 0; k < sizes.Length; k++)
        {
            var (mat, ss) = sizes[k];
            foreach (var (nx, nz) in ss)
                o.Add(($"20-Panels/{k + 1}0-{Mats[mat].Sec.Replace(" ", "")}", Panel(mat, nx, nz)));
        }
        o.Add(("20-Panels/70-Openings", Opening("Window Frame 1 m", "plate", 4, 4, [0.25, 0.25, 0.75, 0.75], "Plate with a 0.5 m window.")));
        o.Add(("20-Panels/70-Openings", Opening("Window Frame 1x0.5 m", "plate", 4, 2, [0.15, 0.15, 0.85, 0.35], "Plate with a low window slot.")));
        o.Add(("20-Panels/70-Openings", Opening("Gun Port Panel 1 m", "plate", 4, 4, [0.3, 0.45, 0.7, 0.6], "Plate with a firing slit.")));
        o.Add(("20-Panels/70-Openings", Vent("Vent Panel 1 m", 4, 4)));
        o.Add(("20-Panels/70-Openings", Opening("Hatch Frame 1 m", "plate", 4, 4, [0.15, 0.15, 0.85, 0.85], "Deck plate with a 0.7 m hole.")));
        o.Add(("20-Panels/70-Openings", Opening("Door Frame 1x2 m", "plate", 4, 8, [0.15, 0.0, 0.85, 1.8], "Wall plate with a doorway.")));
        foreach (var n in new[] { 2, 4, 8 })
            o.Add(("20-Panels/80-Triangles", Triangle("plate", n)));

        foreach (var (nx, nz) in new[] { (2, 2), (2, 4), (4, 4), (4, 8), (8, 8) })
            o.Add(("30-Armour/10-Armour", Panel("armour", nx, nz)));
        foreach (var (nx, nz) in new[] { (2, 2), (4, 4), (4, 8), (8, 8) })
            o.Add(("30-Armour/20-Heavy", Panel("heavy", nx, nz)));
        o.Add(("30-Armour/30-Ports", Opening("Armour Gun Port 1 m", "armour", 4, 4, [0.3, 0.45, 0.7, 0.6], "Armour plate with a firing slit.", "Gun ports", Armour)));
        o.Add(("30-Armour/30-Ports", Opening("Heavy Armour Port 1 m", "heavy", 4, 4, [0.35, 0.47, 0.65, 0.58], "Heavy armour plate with a narrow slit.", "Gun ports", Armour)));

        o.Add(("40-Shapes/10-Wedges", Wedge(1, 1, 1, "1:1")));
        o.Add(("40-Shapes/10-Wedges", Wedge(2, 2, 2, "1:1")));
        o.Add(("40-Shapes/10-Wedges", Wedge(4, 4, 4, "1:1")));
        o.Add(("40-Shapes/10-Wedges", Wedge(1, 2, 2, "1:2")));
        o.Add(("40-Shapes/10-Wedges", Wedge(2, 4, 4, "1:2")));
        o.Add(("40-Shapes/10-Wedges", Wedge(2, 1, 2, "2:1")));
        o.Add(("40-Shapes/10-Wedges", Wedge(4, 2, 4, "2:1")));
        foreach (var n in new[] { 2, 4 })
            o.Add(("40-Shapes/20-Corners", SlopeCorner(n)));
        foreach (var n in new[] { 2, 4 })
            o.Add(("40-Shapes/20-Corners", InnerCorner(n)));
        o.Add(("40-Shapes/30-Rounded", QuarterRound(1, 1)));
        o.Add(("40-Shapes/30-Rounded", QuarterRound(2, 2)));
        o.Add(("40-Shapes/30-Rounded", QuarterRound(4, 4)));
        o.Add(("40-Shapes/30-Rounded", CurvedPanel(4, 4)));
        o.Add(("40-Shapes/30-Rounded", CurvedPanel(8, 4)));
        o.Add(("40-Shapes/40-Caps", Pyramid(2)));
        o.Add(("40-Shapes/40-Caps", Pyramid(4)));
        foreach (var n in new[] { 4, 8 })
            o.Add(("40-Shapes/40-Caps", Trim("round", n)));
        foreach (var n in new[] { 4, 8 })
            o.Add(("40-Shapes/40-Caps", Trim("chamfer", n)));
        return o;
    }

    public void Make(PartOutput o) => WriteParts(o, Out, Everything());
}
