using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The Sandbox's building pieces (Constructs/Parts/Building) and the example buildings (Constructs/Blueprints). Was
/// tools/constructs/building_parts.py; docs/sandbox/CONSTRUCTS.md, "Building pieces", says how they fit together.
///
/// Large prefabricated structure parts for anchored builds (bases, bunkers, towers) on a common building module. They keep the
/// structural parts' 0.25 m cell rules (StructureParts): a part is made of 0.25 m cells, its origin the centre of its first cell (the
/// bottom, left, back one); every socket sits on a cell face (a face of one of the part's solid boxes, facing out of it), along it at
/// a cell centre. The module: walls 0.25 m thick, 2.75 m tall (11 cells); a floor slab 0.25 m thick, so a storey is 3 m. Lengths are
/// 2 m and 4 m; a room's corners are L pieces with 2 m legs, so four corners close a 4 by 4 m room. The blueprints' socket steps are
/// worked out by a mirror of the game's Rot24 turns and fitting order (<see cref="Blueprint"/>), which interior_parts and make_rv share.
/// </summary>
public sealed partial class BuildingParts : IPartGenerator
{
    public string Name => "building_parts";

    public const double C = 0.25; // one cell
    public const double H = C / 2; // a cell face from its centre
    private const string Out = "Constructs/Parts/Building";
    private const string BpOut = "Constructs/Blueprints";
    public const string Group = "Building";
    public const int WallH = 11; // cells: 2.75 m
    public const int Storey = 12; // cells: 3 m floor to floor

    /// <summary>f"{v:.2f}" with the trailing zeros and point stripped.</summary>
    public static string F(double v) => v.ToString("F2", System.Globalization.CultureInfo.InvariantCulture).TrimEnd('0').TrimEnd('.');

    /// <summary>A building piece (building_parts.Part): boxes in face metres, sockets checked against the solid faces.</summary>
    public sealed class BPart
    {
        public readonly PyDict D;
        public readonly string Material;
        public readonly string Color;
        public List<PyDict> Boxes = [];
        public List<PyDict> Sockets = [];
        public HashSet<string> Names = [];
        public readonly PyDict Extra = new();

        /// <summary>interior_parts' Panel: a piece longer than the 4 m limit, with up to 255 sockets (the same output otherwise).</summary>
        public bool Panel;

        public BPart(string name, string section, string desc, string role = "frame", string tool = "welder", string material = "steel", string color = "#7c838c")
        {
            D = new PyDict
            {
                ["format"] = "call-of-slop-construct-part/1",
                ["name"] = name,
                ["description"] = desc,
                ["group"] = Group,
                ["section"] = section,
                ["role"] = role,
                ["tool"] = tool,
            };
            Material = material;
            Color = color;
        }

        /// <summary>A box in face metres: from the part's min corner (cell 0's min corner is at -H on each axis).</summary>
        public BPart FBox(double x0, double y0, double z0, double x1, double y1, double z1, string? color = null, bool detail = false, bool hidden = false, bool glass = false, PyDict? shape = null) =>
            MBox([x0 - H, y0 - H, z0 - H], [x1 - H, y1 - H, z1 - H], color, detail, hidden, glass, shape);

        /// <summary>A box in cells (x0..x1 exclusive).</summary>
        public BPart CBox(double x0, double y0, double z0, double x1, double y1, double z1, string? color = null, bool detail = false, bool hidden = false, PyDict? shape = null) =>
            FBox(x0 * C, y0 * C, z0 * C, x1 * C, y1 * C, z1 * C, color, detail, hidden, false, shape);

        public BPart MBox(double[] mn, double[] mx, string? color = null, bool detail = false, bool hidden = false, bool glass = false, PyDict? shape = null)
        {
            if (Box(mn, mx, color, detail, hidden, glass, shape) is { } b)
                Boxes.Add(b);
            return this;
        }

        private IEnumerable<PyDict> Solids() => Boxes.Where(b => !Py.Truthy(b.Get("detail")));

        private static double At(PyDict b, string k, int a) => Py.D(((List<object?>)b[k]!)[a]);

        /// <summary>Is a socket at `at` facing `d` on a face of a solid box (and not covered by another solid box)?</summary>
        public bool OnSolid(double[] at, string d)
        {
            var axis = "xyz".IndexOf(d[1]);
            var sign = d[0] == '+' ? 1 : -1;
            var hit = false;
            foreach (var b in Solids())
            {
                var face = sign > 0 ? At(b, "max", axis) : At(b, "min", axis);
                if (Math.Abs(at[axis] - face) > 1e-4)
                    continue;
                var all = true;
                for (var a = 0; a < 3; a++)
                    if (a != axis && !(At(b, "min", a) - 1e-4 <= at[a] && at[a] <= At(b, "max", a) + 1e-4))
                        all = false;
                if (all)
                {
                    hit = true;
                    break;
                }
            }
            if (!hit)
                return false;
            var probe = (double[])at.Clone();
            probe[axis] += sign * 0.002;
            foreach (var b in Solids())
            {
                var inside = true;
                for (var a = 0; a < 3; a++)
                    if (!(At(b, "min", a) + 1e-4 < probe[a] && probe[a] < At(b, "max", a) - 1e-4))
                        inside = false;
                if (inside)
                    return false; // (an inside face: another box of the part is against it)
            }
            return true;
        }

        public bool Sock(string name, double[] at, string d, string? kind = null, bool must = true)
        {
            var v = Vec(at);
            var r = v.Select(Py.D).ToArray();
            if (!OnSolid(r, d))
            {
                if (must)
                    throw new InvalidOperationException($"building_parts: {D["name"]}: socket {name} at [{string.Join(", ", r)}] {d} is on no solid face");
                return false;
            }
            if (!Names.Add(name))
                throw new InvalidOperationException($"building_parts: {D["name"]}: socket {name} twice");
            var s = new PyDict { ["name"] = name, ["at"] = v, ["dir"] = d };
            if (!string.IsNullOrEmpty(kind))
                s["kind"] = kind;
            Sockets.Add(s);
            return true;
        }

        /// <summary>A socket on cell `cell`'s face `d` (only where that face is a solid face of the part).</summary>
        public bool CSock(string name, (double X, double Y, double Z) cell, string d, bool must = false, string? kind = null)
        {
            var axis = "xyz".IndexOf(d[1]);
            double[] at = [cell.X * C, cell.Y * C, cell.Z * C];
            at[axis] += d[0] == '+' ? H : -H;
            return Sock(name, at, d, kind, must);
        }

        /// <summary>Armour stays an int, seconds a float, as the Python wrote them.</summary>
        public BPart Stats(double mass, double health, int armour, double seconds)
        {
            D["mass"] = R(Math.Max(0.5, Py.Round(mass, 1)));
            D["health"] = (int)Py.RoundInt(health);
            D["armour"] = armour;
            D["material"] = Material;
            D["color"] = Color;
            D["attachSeconds"] = seconds;
            return this;
        }

        public PyDict Json()
        {
            var n = (string)D["name"]!;
            if (Boxes.Count is 0 or > 16 || Sockets.Count == 0 || Sockets.Count > (Panel ? 255 : 40) || n.Length > 32)
                throw new InvalidOperationException($"building_parts: {n}: {Boxes.Count} boxes, {Sockets.Count} sockets");
            foreach (var b in Panel ? new List<PyDict>() : Boxes)
                for (var a = 0; a < 3; a++)
                    if (Math.Max(Math.Abs(At(b, "min", a)), Math.Abs(At(b, "max", a))) > 4.0)
                        throw new InvalidOperationException($"building_parts: {n}: a box past 4 m");
            PartZFight.Resolve(Boxes, Sockets); // no two drawn faces on one plane (CLAUDE.md, "No coplanar faces")
            var d = new PyDict(D);
            d.Update(Extra);
            d["boxes"] = Boxes.Cast<object?>().ToList();
            d["sockets"] = Sockets.Cast<object?>().ToList();
            return d;
        }
    }

    /// <summary>The module points along n cells: both ends and each side of every 2 m line.</summary>
    public static List<int> Mods(int n)
    {
        var s = new SortedSet<int> { 0, n - 1 };
        for (var k = 8; k < n; k += 8)
        {
            s.Add(k - 1);
            s.Add(k);
        }
        return [.. s];
    }

    public static List<int> Thirds(int h) => [.. new SortedSet<int> { 0, h / 2, h - 1 }];

    private static List<int> Sorted(params int[] v) => [.. new SortedSet<int>(v)];

    // ---- Materials

    /// <summary>name, kg per m2 of wall, health per m2, armour, material, colour, dark colour, tool, seconds to fit a 4 m wall.</summary>
    public record Mat(string Name, int Kgm2, int Hpm2, int Arm, string Material, string Colour, string Dark, string Tool, double Secs);

    public static readonly Dictionary<string, Mat> Mats = new()
    {
        ["wood"] = new("Wood", 30, 45, 2, "wood", "#8a6a40", "#6e5332", "wrench", 3.0),
        ["tin"] = new("Corrugated", 18, 55, 4, "steel", "#8d959b", "#6f777d", "wrench", 3.0),
        ["concrete"] = new("Concrete", 300, 260, 25, "concrete", "#9a978f", "#7f7c75", "welder", 5.0),
        ["rc"] = new("Reinforced", 360, 420, 45, "concrete", "#7c827f", "#646a67", "welder", 6.0),
        ["steel"] = new("Steel", 160, 320, 35, "steel", "#5f6870", "#4c545b", "welder", 5.0),
    };

    public static readonly Dictionary<string, string> MatWords = new()
    {
        ["wood"] = "Cheap, light, burns.",
        ["tin"] = "Cheap, stops only weak pistol rounds.",
        ["concrete"] = "Heavy, stops pistols and SMGs.",
        ["rc"] = "Toughest, stops all but marksman rifles.",
        ["steel"] = "Stops pistols, SMGs and buckshot.",
    };

    public static BPart MatStats(BPart p, string key, double area, int @base = 60, double secondsScale = 1.0)
    {
        var m = Mats[key];
        return p.Stats(m.Kgm2 * area, @base + m.Hpm2 * area, m.Arm, Py.Round(Math.Max(1.5, m.Secs * secondsScale), 1));
    }

    public static BPart MatPart(string name, string key, string section, string desc, string role = "frame")
    {
        var m = Mats[key];
        return new BPart(name, section, $"{desc} {MatWords[key]}", role: role, tool: m.Tool, material: m.Material, color: m.Colour);
    }

    private static double SumD(IEnumerable<double> v) => Py.Sum(v);

    /// <summary>A little surface detail (drawn only): plank seams, corrugation ribs, a plinth, plate seams.</summary>
    public static void WallDetails(BPart p, string key, int n, int h, List<(double X0, double Y0, double X1, double Y1)> rects)
    {
        var dark = Mats[key].Dark;
        var L = n * C;
        if (key == "wood")
        {
            foreach (var y in new[] { 0.9, 1.8 })
                if (y < h * C - 0.1 && p.Boxes.Count < 15)
                    foreach (var zf in new[] { -0.006, C })
                    {
                        var seg = rects.Where(rc => rc.Y0 <= y - 0.03 && rc.Y1 >= y + 0.03).Take(1);
                        foreach (var rc in seg)
                            p.FBox(rc.X0, y - 0.02, Math.Min(zf, 0), rc.X1, y + 0.02, zf < 0 ? Math.Max(zf + 0.006, 0.006) : C + 0.006, dark, detail: true);
                    }
        }
        else if (key == "tin")
        {
            var ribs = Math.Max(2, (int)(L / 0.5));
            for (var k = 1; k < ribs; k++)
            {
                if (p.Boxes.Count >= 15)
                    break;
                var x = L * k / ribs;
                var col = rects.Where(rc => rc.X0 <= x - 0.02 && rc.X1 >= x + 0.02).ToList();
                if (col.Count > 0)
                {
                    var rc = col[0];
                    foreach (var q in col)
                        if (q.Y1 - q.Y0 > rc.Y1 - rc.Y0)
                            rc = q;
                    p.FBox(x - 0.02, rc.Y0, -0.012, x + 0.02, rc.Y1, 0.0, dark, detail: true);
                }
            }
        }
        else if (key is "concrete" or "rc")
        {
            foreach (var rc in rects.Where(rc => rc.Y0 == 0).Take(3))
                if (p.Boxes.Count < 16)
                    p.FBox(rc.X0, 0, -0.015, rc.X1, 0.15, C + 0.015, dark, detail: true);
        }
        else if (key == "steel")
        {
            foreach (var rc in rects.Take(3))
                if (p.Boxes.Count < 16 && rc.Y1 - rc.Y0 > 0.5)
                    p.FBox(rc.X0, rc.Y1 - 0.08, -0.01, rc.X1, rc.Y1 - 0.04, C + 0.01, dark, detail: true);
        }
    }

    public static void WallSockets(BPart p, int n, int h, bool top = true, bool ends = true, bool faces = true)
    {
        foreach (var x in Mods(n))
        {
            p.CSock($"b{x}", (x, 0, 0), "-y");
            if (top)
                p.CSock($"t{x}", (x, h - 1, 0), "+y");
        }
        if (ends)
            foreach (var y in Thirds(h))
            {
                p.CSock($"l{y}", (0, y, 0), "-x");
                p.CSock($"r{y}", (n - 1, y, 0), "+x");
            }
        if (faces)
            foreach (var x in Mods(n))
                foreach (var y in Thirds(h))
                {
                    p.CSock($"f{x}_{y}", (x, y, 0), "-z");
                    p.CSock($"k{x}_{y}", (x, y, 0), "+z");
                }
    }

    private static List<(double, double, double, double)> Rects(params (double, double, double, double)[] r) => [.. r];

    /// <summary>A straight wall of n x h cells from rectangles (face metres along the wall and up), one cell thick.</summary>
    private static double WallFrom(BPart p, string key, int n, int h, List<(double X0, double Y0, double X1, double Y1)> rects)
    {
        foreach (var (x0, y0, x1, y1) in rects)
            p.FBox(x0, y0, 0, x1, y1, C);
        WallDetails(p, key, n, h, rects);
        WallSockets(p, n, h);
        return SumD(rects.Select(r => (r.X1 - r.X0) * (r.Y1 - r.Y0)));
    }

    public const string SectionWalls = "Walls";
    private const string SectionOpen = "Wall openings";
    private const string SectionCorners = "Corners and ends";
    private const string SectionStages = "Damaged";

    private static BPart StraightWall(string key, int n, int h = WallH, string kind = "Wall")
    {
        double L = n * C, Hm = h * C;
        var name = $"{Mats[key].Name} {kind} {F(L)} m";
        var what = kind switch { "Wall" => "Storey wall.", "Half Wall" => "Crouch cover.", _ => "Flat roof's edge." };
        var p = MatPart(name, key, SectionWalls, what);
        var area = WallFrom(p, key, n, h, Rects((0, 0, L, Hm)));
        return MatStats(p, key, area, secondsScale: 0.6 + 0.4 * n / 16);
    }

    /// <summary>A wall's damage stage (it loses chunks before it breaks off): the same sockets, fewer boxes.</summary>
    private static BPart StageWall(string key, int n, int stage)
    {
        double L = n * C, Hm = WallH * C;
        var whole = $"{Mats[key].Name} Wall {F(L)} m";
        var name = $"{whole} {(stage == 1 ? "Damaged" : "Breached")}";
        var p = MatPart(name, key, SectionStages, stage == 1 ? "Top corner gone." : "Top corner gone, a crawl hole at the bottom.");
        (double X, double Y) chunk;
        (double X0, double X1, double H) hole;
        if (n == 16)
        {
            chunk = (12 * C, 7 * C); // the top corner from here (x) and from this height (y) is gone
            hole = (4 * C, 8 * C, 6 * C); // x0, x1, height of the hole at the bottom
        }
        else
        {
            chunk = (5 * C, 7 * C);
            hole = (1 * C, 5 * C, 6 * C);
        }
        var rects = stage == 1
            ? Rects((0, 0, chunk.X, Hm), (chunk.X, 0, L, chunk.Y))
            : Rects((0, 0, hole.X0, Hm), (hole.X0, hole.H, hole.X1, Hm), (hole.X1, 0, chunk.X, Hm), (chunk.X, 0, L, chunk.Y));
        foreach (var (x0, y0, x1, y1) in rects)
            p.FBox(x0, y0, 0, x1, y1, C);
        // Rubble edges at the break (drawn only).
        var dark = Mats[key].Dark;
        p.FBox(chunk.X - 0.08, chunk.Y - 0.06, -0.02, chunk.X + 0.12, chunk.Y + 0.05, C + 0.02, dark, detail: true);
        p.FBox(chunk.X + 0.2, chunk.Y - 0.04, 0.03, chunk.X + 0.42, chunk.Y + 0.07, C - 0.03, dark, detail: true);
        if (stage == 2)
            p.FBox(hole.X0 - 0.06, hole.H - 0.05, -0.02, hole.X1 + 0.06, hole.H + 0.04, C + 0.02, dark, detail: true);
        WallSockets(p, n, WallH);
        // The whole wall's sockets, exactly (a stage swaps in place: every link stays). Sockets that now hang in the air are kept.
        var full = new BPart("x", SectionStages, "");
        full.FBox(0, 0, 0, L, Hm, C);
        WallSockets(full, n, WallH);
        p.Sockets = [.. full.Sockets.Select(s => new PyDict(s))];
        p.Names = [.. p.Sockets.Select(s => (string)s["name"]!)];
        p.Extra["stage"] = true;
        var area = SumD(rects.Select(r => (r.Item3 - r.Item1) * (r.Item4 - r.Item2)));
        var wholeArea = L * Hm;
        var m = Mats[key];
        p.Stats(m.Kgm2 * area, 60 + m.Hpm2 * wholeArea, m.Arm, 1.5);
        return p;
    }

    /// <summary>The whole wall's "damaged" chain: below 60% of its health it loses a chunk, below 30% it is breached.</summary>
    private static void StageLinks(BPart p, string key, int n)
    {
        var whole = $"{Mats[key].Name} Wall {F(n * C)} m";
        if ((string)p.D["name"]! == whole)
            p.Extra["damaged"] = new PyDict { ["below"] = 0.6, ["part"] = $"{whole} Damaged" };
        else if ((string)p.D["name"]! == $"{whole} Damaged")
            p.Extra["damaged"] = new PyDict { ["below"] = 0.3, ["part"] = $"{whole} Breached" };
    }

    private static BPart WindowWall(string key, int n)
    {
        double L = n * C, Hm = WallH * C;
        var name = $"{Mats[key].Name} Window Wall {F(L)} m";
        (double, double)[] wins = n == 8 ? [(2 * C, 6 * C)] : [(2 * C, 6 * C), (10 * C, 14 * C)];
        double sill = 4 * C, head = 8 * C; // a 1 m window, its sill 1 m up
        var p = MatPart(name, key, SectionOpen, $"{(n == 8 ? "One" : "Two")} 1 m window{(n == 8 ? "" : "s")}.");
        var rects = Rects((0, 0, L, sill), (0, head, L, Hm));
        var xs = new List<double> { 0 };
        foreach (var w in wins)
        {
            xs.Add(w.Item1);
            xs.Add(w.Item2);
        }
        xs.Add(L);
        for (var k = 0; k < xs.Count; k += 2)
            rects.Add((xs[k], sill, xs[k + 1], head));
        foreach (var (x0, y0, x1, y1) in rects)
            p.FBox(x0, y0, 0, x1, y1, C);
        WallDetails(p, key, n, WallH, rects);
        WallSockets(p, n, WallH);
        for (var k = 0; k < wins.Length; k++)
            p.CSock(n > 8 ? $"sill{k}" : "sill", ((int)Py.RoundInt(wins[k].Item1 / C), 3, 0), "+y", must: true);
        var area = L * Hm - wins.Length * 1.0;
        return MatStats(p, key, area, secondsScale: 0.6 + 0.4 * n / 16);
    }

    private static BPart DoorWall(string key)
    {
        int n = 8;
        double L = 2.0, Hm = WallH * C;
        var p = MatPart($"{Mats[key].Name} Door Wall 2 m", key, SectionOpen, "Doorway: a Hinge on the jamb takes a door.");
        var rects = Rects((0, 0, C, Hm), (7 * C, 0, L, Hm), (C, 9 * C, 7 * C, Hm));
        foreach (var (x0, y0, x1, y1) in rects)
            p.FBox(x0, y0, 0, x1, y1, C);
        WallDetails(p, key, n, WallH, rects);
        WallSockets(p, n, WallH);
        p.CSock("hinge", (0, 4, 0), "+x", must: true);
        p.CSock("hinge2", (7, 4, 0), "-x", must: true);
        return MatStats(p, key, L * Hm - 1.5 * 2.25, secondsScale: 0.8);
    }

    private static BPart GarageWall(string key)
    {
        int n = 16;
        double L = 4.0, Hm = WallH * C;
        var p = MatPart($"{Mats[key].Name} Garage Wall 4 m", key, SectionOpen, "Vehicle opening: two Garage Door leaves close it.");
        var rects = Rects((0, 0, 2 * C, Hm), (14 * C, 0, L, Hm), (2 * C, 10 * C, 14 * C, Hm));
        foreach (var (x0, y0, x1, y1) in rects)
            p.FBox(x0, y0, 0, x1, y1, C);
        WallDetails(p, key, n, WallH, rects);
        WallSockets(p, n, WallH);
        p.CSock("hinge", (1, 4, 0), "+x", must: true);
        p.CSock("hinge2", (14, 4, 0), "-x", must: true);
        return MatStats(p, key, L * Hm - 3.0 * 2.5);
    }

    private static BPart SlitWall(string key)
    {
        int n = 8;
        double L = 2.0, Hm = WallH * C;
        double lo = 1.3, hi = 1.65; // eye height 1.6 standing
        var p = MatPart($"{Mats[key].Name} Slit Wall 2 m", key, SectionOpen, "Two firing slits.");
        var rects = Rects((0, 0, L, lo), (0, hi, L, Hm), (0, lo, C, hi), (3 * C, lo, 5 * C, hi), (7 * C, lo, L, hi));
        foreach (var (x0, y0, x1, y1) in rects)
            p.FBox(x0, y0, 0, x1, y1, C);
        WallDetails(p, key, n, WallH, rects);
        WallSockets(p, n, WallH);
        return MatStats(p, key, L * Hm - 2 * 0.5 * (hi - lo), secondsScale: 0.8);
    }

    /// <summary>An L with 2 m legs (8 cells each, sharing the corner cell): leg A along +x, leg B along +z.</summary>
    private static BPart Corner(string key, string kind = "corner", int h = WallH)
    {
        var Hm = h * C;
        var label = kind switch { "corner" => "Wall Corner", "door" => "Door Corner", "window" => "Window Corner", _ => "Parapet Corner" };
        var desc = kind switch
        {
            "corner" => "Room corner: four close a 4 m room.",
            "door" => "Room corner with a doorway.",
            "window" => "Room corner with a window.",
            _ => "Flat roof's corner.",
        };
        var p = MatPart($"{Mats[key].Name} {label}", key, SectionCorners, desc);
        p.FBox(0, 0, 0, C, Hm, 8 * C); // leg B (with the corner cell)
        List<(double, double, double, double)> rects;
        double area;
        if (kind is "corner" or "parapet")
        {
            rects = Rects((C, 0, 2.0, Hm));
            area = (2.0 + 1.75) * Hm;
        }
        else if (kind == "door")
        {
            rects = Rects((7 * C, 0, 2.0, Hm), (C, 9 * C, 7 * C, Hm));
            area = 3.75 * Hm - 1.5 * 2.25;
        }
        else
        {
            rects = Rects((C, 0, 2.0, 4 * C), (C, 8 * C, 2.0, Hm), (C, 4 * C, 2 * C, 8 * C), (6 * C, 4 * C, 2.0, 8 * C));
            area = 3.75 * Hm - 1.0;
        }
        foreach (var (x0, y0, x1, y1) in rects)
            p.FBox(x0, y0, 0, x1, y1, C);
        var dark = Mats[key].Dark;
        if (key is "concrete" or "rc" && kind != "parapet")
        {
            // (4 mm up: on an upper storey the band's underside is otherwise in the plane of a slab's that meets the wall outside)
            p.FBox(-0.015, 0.004, -0.015, 2.0, 0.15, 0, dark, detail: true);
            p.FBox(-0.015, 0.004, 0, 0, 0.15, 2.0, dark, detail: true);
        }
        else if (key == "tin")
        {
            foreach (var x in new[] { 0.5, 1.0, 1.5 })
            {
                p.FBox(x - 0.02, 0, -0.012, x + 0.02, Hm, 0, dark, detail: true);
                p.FBox(-0.012, 0, x - 0.02, 0, Hm, x + 0.02, dark, detail: true);
            }
        }
        else if (key == "steel")
            p.FBox(-0.01, Hm - 0.08, -0.01, 2.0, Hm - 0.04, 0, dark, detail: true);
        foreach (var y in Thirds(h))
        {
            foreach (var x in new[] { 0, 7 })
            {
                p.CSock($"fa{x}_{y}", (x, y, 0), "-z");
                p.CSock($"ka{x}_{y}", (x, y, 0), "+z");
                p.CSock($"fb{x}_{y}", (0, y, x), "-x");
                p.CSock($"kb{x}_{y}", (0, y, x), "+x");
            }
            p.CSock($"ra{y}", (7, y, 0), "+x");
            p.CSock($"rb{y}", (0, y, 7), "+z");
        }
        foreach (var (x, z) in new[] { (0, 0), (7, 0), (0, 7) })
        {
            p.CSock($"b{x}_{z}", (x, 0, z), "-y");
            p.CSock($"t{x}_{z}", (x, h - 1, z), "+y");
        }
        if (kind == "door")
            p.CSock("hinge", (7, 4, 0), "-x", must: true);
        if (kind == "window")
            p.CSock("sill", (2, 3, 0), "+y", must: true);
        // (sockets are kept to 40: the grid above already reaches it on the plain corner)
        p.Sockets = [.. p.Sockets.Take(40)];
        return MatStats(p, key, area, secondsScale: 0.9);
    }

    /// <summary>A T: a 2 m crossbar (8 cells along +x) with a 1.75 m stem from its last cell (+z).</summary>
    private static BPart Tee(string key)
    {
        var Hm = WallH * C;
        var p = MatPart($"{Mats[key].Name} Wall T", key, SectionCorners, "Where an inside wall meets an outside one.");
        p.FBox(0, 0, 0, 2.0, Hm, C);
        p.FBox(7 * C, 0, C, 2.0, Hm, 2.0);
        foreach (var y in Thirds(WallH))
        {
            p.CSock($"l{y}", (0, y, 0), "-x");
            p.CSock($"r{y}", (7, y, 0), "+x");
            p.CSock($"s{y}", (7, y, 7), "+z");
            foreach (var x in new[] { 0, 7 })
                p.CSock($"f{x}_{y}", (x, y, 0), "-z");
            p.CSock($"k0_{y}", (0, y, 0), "+z");
            p.CSock($"m{y}", (7, y, 7), "-x");
            p.CSock($"n{y}", (7, y, 7), "+x");
        }
        foreach (var (x, z) in new[] { (0, 0), (7, 0), (7, 7) })
        {
            p.CSock($"b{x}_{z}", (x, 0, z), "-y");
            p.CSock($"t{x}_{z}", (x, WallH - 1, z), "+y");
        }
        return MatStats(p, key, 3.75 * Hm, secondsScale: 0.9);
    }

    private static BPart EndCap(string key)
    {
        var Hm = WallH * C;
        var p = MatPart($"{Mats[key].Name} Wall End", key, SectionCorners, "A free wall's end.");
        p.FBox(0, 0, 0, C, Hm, C);
        p.FBox(-0.02, Hm - 0.1, -0.02, C + 0.02, Hm, C + 0.02, Mats[key].Dark, detail: true);
        foreach (var y in Thirds(WallH))
            foreach (var d in new[] { "-x", "+x", "-z", "+z" })
                p.CSock($"{d}{y}".Replace("-", "m").Replace("+", "p"), (0, y, 0), d);
        p.CSock("b", (0, 0, 0), "-y");
        p.CSock("t", (0, WallH - 1, 0), "+y");
        return MatStats(p, key, C * Hm * 2, secondsScale: 0.4);
    }

    // ---- Floors, foundations

    public const string SectionFloors = "Floors";
    private const string SectionFound = "Foundations";

    /// <summary>label, kg per m2, health per m2, armour, material, colour, dark, tool, seconds for 4x4.</summary>
    public static readonly Dictionary<string, Mat> FloorMats = new()
    {
        ["wood"] = new("Wood", 35, 50, 2, "wood", "#8a6a40", "#6e5332", "wrench", 4.0),
        ["steel"] = new("Steel", 90, 220, 20, "steel", "#8a9096", "#6c7278", "welder", 5.0),
        ["concrete"] = new("Concrete", 300, 260, 25, "concrete", "#9a978f", "#7f7c75", "welder", 6.0),
        ["rc"] = new("Reinforced", 360, 420, 45, "concrete", "#7c827f", "#646a67", "welder", 7.0),
    };

    public static readonly Dictionary<string, string> FloorWords = new()
    {
        ["wood"] = "Light, burns.",
        ["steel"] = "Tread plate.",
        ["concrete"] = "Concrete slab.",
        ["rc"] = "Stops all but marksman rifles.",
    };

    /// <summary>Module sockets on a slab's top and bottom (where walls stand and hang) and a few on its edges.</summary>
    public static void SlabSockets(BPart p, int nx, int nz, bool edges = true, bool top = true, bool bottom = true, int y = 0)
    {
        foreach (var x in Mods(nx))
            foreach (var z in Mods(nz))
            {
                if (top)
                    p.CSock($"t{x}_{z}", (x, y, z), "+y");
                if (bottom)
                    p.CSock($"b{x}_{z}", (x, 0, z), "-y");
            }
        if (edges)
        {
            foreach (var z in Sorted(0, nz - 1))
            {
                p.CSock($"l{z}", (0, 0, z), "-x");
                p.CSock($"r{z}", (nx - 1, 0, z), "+x");
            }
            foreach (var x in Sorted(0, nx - 1))
            {
                p.CSock($"f{x}", (x, 0, 0), "-z");
                p.CSock($"k{x}", (x, 0, nz - 1), "+z");
            }
        }
    }

    private static BPart Slab(string key, int nx, int nz, string kind = "plain")
    {
        var m = FloorMats[key];
        double W = nx * C, D = nz * C;
        var name = kind switch
        {
            "plain" => $"{m.Name} Floor {F(W)}x{F(D)} m",
            "hatch" => $"{m.Name} Hatch Floor {F(W)}x{F(D)} m",
            "stair" => $"{m.Name} Stair Floor {F(W)}x{F(D)} m",
            _ => $"{m.Name} Stairwell Floor {F(W)}x{F(D)} m",
        };
        var desc = kind switch
        {
            "plain" => "Floor on the walls' tops.",
            "hatch" => "Floor with a hatch for a Ship Ladder.",
            "stair" => "Floor with an opening over a Straight Stair.",
            _ => "Floor with a stairwell over a Half-Turn Stair.",
        };
        var p = new BPart(name, SectionFloors, $"{desc} {FloorWords[key]}", role: "frame", tool: m.Tool, material: m.Material, color: m.Colour);
        (int X0, int Z0, int X1, int Z1)[] holes = [], rects;
        if (kind == "plain")
            rects = [(0, 0, nx, nz)];
        else if (kind == "hatch")
        {
            holes = [(6, 6, 10, 14)];
            rects = [(0, 0, 16, 6), (0, 14, 16, 16), (0, 6, 6, 14), (10, 6, 16, 14)];
        }
        else if (kind == "stair")
        {
            holes = [(1, 4, 5, 16)];
            rects = [(0, 0, 16, 4), (0, 4, 1, 16), (5, 4, 16, 16)];
        }
        else
        {
            holes = [(1, 6, 9, 16)];
            rects = [(0, 0, 16, 6), (0, 6, 1, 16), (9, 6, 16, 16)];
        }
        foreach (var (x0, z0, x1, z1) in rects)
            p.CBox(x0, 0, z0, x1, 1, z1);
        if (key == "wood")
        {
            for (var k = 1; k < 4; k++)
            {
                var z = D * k / 4;
                foreach (var (x0, z0, x1, z1) in rects)
                    if (z0 * C < z - 0.02 && z1 * C > z + 0.02 && p.Boxes.Count < 16)
                    {
                        p.FBox(x0 * C, C, z - 0.01, x1 * C, C + 0.006, z + 0.01, m.Dark, detail: true);
                        break;
                    }
            }
        }
        else if (key == "steel")
            foreach (var (x0, z0, x1, z1) in rects.Take(2))
                p.FBox(x0 * C + 0.05, C, z0 * C + 0.05, x1 * C - 0.05, C + 0.008, z1 * C - 0.05, "#a3a8ac", detail: true);
        SlabSockets(p, nx, nz);
        var area = W * D - SumD(holes.Select(h => (h.X1 - h.X0) * (h.Z1 - h.Z0) * C * C));
        p.Extra["walk"] = "floor";
        return p.Stats(m.Kgm2 * area, 60 + m.Hpm2 * area, m.Arm, Py.Round(Math.Max(2.0, m.Secs * area / 16), 1));
    }

    private static BPart Foundation(string kind, string key = "concrete")
    {
        int nx = 16, nz = 16;
        var label = key == "concrete" ? "Concrete" : "Reinforced";
        var (hpM3, kgM3, arm, colour, dark) = key == "concrete" ? (500, 1800, 25, "#8e8a80", "#75716a") : (800, 2000, 45, "#787d79", "#626763");
        if (kind == "pad2")
            nx = nz = 8;
        var name = kind switch
        {
            "pad2" => $"{label} Foundation 2x2 m",
            "pad4" => $"{label} Foundation 4x4 m",
            "step" => $"{label} Stepped Foundation",
            _ => $"{label} Deep Foundation",
        };
        var desc = kind switch
        {
            "pad2" => "Anchors a build to the ground.",
            "pad4" => "Anchors a room: wall sockets on top.",
            "step" => "Deep side goes downhill on a slope.",
            _ => "Stands over rough or sloping ground.",
        };
        var p = new BPart(name, SectionFound, desc, role: "foundation", tool: "welder", material: "concrete", color: colour);
        double W = nx * C, D = nz * C;
        p.CBox(0, 0, 0, nx, 2, nz);
        var vol = W * D * 0.5;
        if (kind == "step")
        {
            p.CBox(0, -2, 8, nx, 0, nz);
            vol += W * D * 0.25;
        }
        else if (kind == "deep")
        {
            foreach (var (x, z) in new[] { (0, 0), (14, 0), (0, 14), (14, 14) })
                p.CBox(x, -4, z, x + 2, 0, z + 2);
            vol += 4 * 0.5 * 0.5 * 1.0;
        }
        p.FBox(-0.01, 2 * C - 0.04, -0.01, W + 0.01, 2 * C, D + 0.01, dark, detail: true);
        foreach (var x in Mods(nx))
            foreach (var z in Mods(nz))
                p.CSock($"t{x}_{z}", (x, 1, z), "+y");
        foreach (var v in Mods(nx))
        {
            p.CSock($"l{v}", (0, 1, v), "-x");
            p.CSock($"r{v}", (nx - 1, 1, v), "+x");
            p.CSock($"f{v}", (v, 1, 0), "-z");
            p.CSock($"k{v}", (v, 1, nz - 1), "+z");
        }
        p.Extra["walk"] = "floor";
        return p.Stats(kgM3 * vol, 200 + hpM3 * vol, arm, nx == 8 ? 4.0 : 6.0);
    }

    // ---- Roofs

    private const string SectionRoofs = "Roofs";

    private static readonly Dictionary<string, Mat> RoofMats = new()
    {
        ["wood"] = new("Wood", 25, 45, 2, "wood", "#7a5a38", "#5f462c", "wrench", 0),
        ["tin"] = new("Corrugated", 15, 55, 4, "steel", "#8d959b", "#6f777d", "wrench", 0),
        ["concrete"] = new("Concrete", 300, 260, 25, "concrete", "#9a978f", "#7f7c75", "welder", 0),
    };

    private static PyDict Wedge(int t0, int t1, object s0, object s1) => Kw(("shape", "wedge"), ("taper", Py.L(t0, t1)), ("shift", Py.L(s0, s1)));

    /// <summary>A mono-pitch roof (a solid wedge rising to +z), stepped collision under a smooth top: walkable.</summary>
    private static BPart RoofSlope(string key, int nx, int nz, int ny, string kind = "slope")
    {
        var m = RoofMats[key];
        double W = nx * C, D = nz * C, R = ny * C;
        var name = $"{m.Name} {(kind == "leanto" ? "Lean-to Roof" : "Roof Slope")} {F(W)}x{F(D)} m";
        var desc = kind == "leanto" ? "Lean-to roof against a wall." : "Two back to back make a ridge.";
        var p = new BPart(name, SectionRoofs, desc, role: "frame", tool: m.Tool, material: m.Material, color: m.Colour);
        var n = Math.Min(12, nz >= 8 ? nz / 2 : nz);
        var dz = D / n;
        for (var k = 0; k < n; k++)
        {
            var h = R * (k + 0.5) / n;
            p.FBox(0, 0, dz * k, W, Math.Max(0.02, h), dz * (k + 1), hidden: true);
        }
        p.FBox(0, 0, 0, W, R, D, detail: true, shape: Wedge(1, 0, 0, -0.5));
        if (key == "tin")
            for (var k = 1; k < (int)(W / 0.5); k++)
                if (p.Boxes.Count < 16)
                {
                    var x = 0.5 * k;
                    p.MBox([x - 0.02 - H, -H, -H], [x + 0.02 - H, R - H + 0.02, D - H], m.Dark, detail: true, shape: Wedge(1, 0, 0, -0.5));
                }
        foreach (var x in Mods(nx))
            foreach (var z in Mods(nz))
                p.CSock($"b{x}_{z}", (x, 0, z), "-y");
        foreach (var x in Mods(nx))
            for (var y = 0; y < ny; y++)
                p.CSock($"k{x}_{y}", (x, y, nz - 1), "+z");
        p.Extra["walk"] = "ramp";
        var area = W * D;
        return p.Stats(m.Kgm2 * area, 60 + m.Hpm2 * area, m.Arm, Py.Round(Math.Max(2.0, 4.0 * area / 16), 1));
    }

    /// <summary>A ridge (gable) roof: a triangular prism. In its own frame the ridge runs along y and the apex points to -z (up once
    /// fitted): its +z face is the base, which goes on the walls' tops.</summary>
    private static BPart RidgeRoof(string key, int nx = 16, int ny = 16, int nz = 4)
    {
        var m = RoofMats[key];
        double W = nx * C, L = ny * C, R = nz * C;
        var p = new BPart($"{m.Name} Ridge Roof {F(W)}x{F(L)} m", SectionRoofs, "Ridge roof with closed ends.", role: "frame", tool: m.Tool, material: m.Material, color: m.Colour);
        // Layers from the base (+z) up to the apex (-z), each narrower: stepped collision. k = 0 is the apex layer.
        for (var k = 0; k < nz; k++)
        {
            var half = (double)(k + 1) * nx / (2 * nz);
            double x0 = nx / 2.0 - half, x1 = nx / 2.0 + half;
            p.CBox(x0, 0, k, x1, ny, k + 1, hidden: true);
        }
        p.FBox(0, 0, 0, W, L, R, detail: true, shape: Wedge(0, 1, 0, 0));
        foreach (var x in Mods(nx))
            foreach (var y in Mods(ny))
                p.CSock($"b{x}_{y}", (x, y, nz - 1), "+z");
        foreach (var y in new[] { 0, ny - 1 })
            foreach (var x in new[] { nx / 2 - 2, nx / 2 + 1 })
                p.CSock($"e{x}_{y}", (x, y, nz - 1), y == 0 ? "-y" : "+y");
        p.Extra["walk"] = "ramp";
        var area = W * L * 1.12;
        return p.Stats(m.Kgm2 * area, 60 + m.Hpm2 * area, m.Arm, Py.Round(Math.Max(2.0, 5.0 * area / 16), 1));
    }

    /// <summary>A gable wall: the triangle under a ridge made of two Roof Slopes. Built like the ridge roof (apex to -z, thin in y).</summary>
    private static BPart Gable(string key, int nx = 16, int nz = 4)
    {
        var m = RoofMats.TryGetValue(key, out var rm) ? rm : Mats[key];
        double W = nx * C, R = nz * C;
        var p = new BPart($"{m.Name} Gable Wall {F(W)} m", SectionRoofs, "Closes a ridge's end, on top of a wall.", role: "frame", tool: m.Tool, material: m.Material, color: m.Colour);
        for (var k = 0; k < nz; k++)
        {
            var half = (double)(k + 1) * nx / (2 * nz);
            double x0 = nx / 2.0 - half, x1 = nx / 2.0 + half;
            p.CBox(x0, 0, k, x1, 1, k + 1, hidden: true);
        }
        p.FBox(0, 0, 0, W, C, R, detail: true, shape: Wedge(0, 1, 0, 0));
        foreach (var x in Mods(nx))
            p.CSock($"b{x}", (x, 0, nz - 1), "+z");
        p.CSock("l", (0, 0, nz - 1), "-x");
        p.CSock("r", (nx - 1, 0, nz - 1), "+x");
        var area = W * R / 2;
        return p.Stats(m.Kgm2 * area * 1, 60 + m.Hpm2 * area, m.Arm, 2.0);
    }

    // ---- Stairs and access

    private const string SectionStairs = "Stairs and access";

    private static readonly Dictionary<string, (string Nm, string Material, string Colour, string Dark, string Tool, int Arm, int Hp)> StairMats = new()
    {
        ["wood"] = ("Wood", "wood", "#8a6a40", "#6e5332", "wrench", 2, 60),
        ["steel"] = ("Steel", "steel", "#6f767f", "#4f565e", "welder", 20, 120),
        ["concrete"] = ("Concrete", "concrete", "#9a978f", "#7f7c75", "welder", 25, 160),
    };

    /// <summary>Steps as solid boxes from the floor: drawn as they are (concrete), or hidden under thin treads (wood, steel).</summary>
    private static void StepBoxes(BPart p, List<(double Z0, double Z1, double Top)> steps, double x0, double x1, bool solidVisible, string treadColour)
    {
        foreach (var (z0, z1, top) in steps)
            p.FBox(x0, 0, z0, x1, top, z1, hidden: !solidVisible);
        if (!solidVisible)
            foreach (var (z0, z1, top) in steps)
                if (p.Boxes.Count < 16)
                    p.FBox(x0, top - 0.05, z0, x1, top, z1, treadColour, detail: true);
    }

    private static BPart StraightStair(string key, int rise = Storey, int run = 12, int width = 4, string label = "Straight Stair")
    {
        var s = StairMats[key];
        var n = rise == Storey ? 8 : 4;
        var stepr = rise * C / n;
        var stepz = run * C / n;
        double Rm = rise * C, Dm = run * C, Wm = width * C;
        var p = new BPart($"{s.Nm} {label} {F(Rm)} m", SectionStairs, $"{n} steps{(rise == Storey ? ": a storey, under a Stair Floor" : ": half a storey, onto a Landing")}.",
            role: "frame", tool: s.Tool, material: s.Material, color: s.Colour);
        var steps = Enumerable.Range(0, n).Select(k => (stepz * k, stepz * (k + 1), stepr * (k + 1))).ToList();
        StepBoxes(p, steps, 0, Wm, key == "concrete", s.Dark);
        foreach (var x in Sorted(0, width - 1))
        {
            foreach (var z in Sorted(0, run - 1))
                p.CSock($"b{x}_{z}", (x, 0, z), "-y");
            p.CSock($"f{x}", (x, 0, 0), "-z");
            foreach (var y in Sorted(0, rise / 2, rise - 1))
                p.CSock($"k{x}_{y}", (x, y, run - 1), "+z");
            p.CSock($"t{x}", (x, rise - 1, run - 1), "+y");
        }
        foreach (var z in Sorted(0, run / 2 - 1, run / 2, run - 1))
        {
            p.CSock($"l{z}", (0, 0, z), "-x");
            p.CSock($"r{z}", (width - 1, 0, z), "+x");
        }
        p.Extra["walk"] = "stairs";
        var vol = Wm * Dm * Rm / 2;
        var kg = (key == "wood" ? 120 : key == "steel" ? 260 : 900) * Wm * Dm * Rm / 3;
        return p.Stats(kg, s.Hp + 60 * vol, s.Arm, rise == Storey ? 3.0 : 2.0);
    }

    private static BPart HalfTurn(string key)
    {
        var s = StairMats[key];
        var p = new BPart($"{s.Nm} Half-Turn Stair 3 m", SectionStairs, "A storey, turning halfway, under a Stairwell Floor.", role: "frame", tool: s.Tool, material: s.Material, color: s.Colour);
        double sr = 0.375, sz = 0.375;
        // Flight A (x 0..1 m) up along +z, the landing (z 1.5..2.5 m) at 1.5 m, flight B (x 1..2 m) back along -z to 3 m.
        for (var k = 0; k < 4; k++)
            p.FBox(0, 0, sz * k, 1.0, sr * (k + 1), sz * (k + 1));
        p.FBox(0, 0, 1.5, 2.0, 1.5, 2.5);
        for (var j = 0; j < 4; j++)
        {
            var z1 = 1.5 - sz * j;
            p.FBox(1.0, 0, z1 - sz, 2.0, 1.5 + sr * (j + 1), z1);
        }
        if (key == "steel")
            p.FBox(0.98, 0, 0, 1.02, 1.5, 1.5, s.Dark, detail: true); // the middle stringer
        foreach (var (x, z) in new[] { (0, 0), (3, 0), (4, 0), (7, 0), (0, 9), (7, 9) })
            p.CSock($"b{x}_{z}", (x, 0, z), "-y");
        foreach (var z in new[] { 0, 5, 9 })
        {
            p.CSock($"l{z}", (0, 0, z), "-x");
            p.CSock($"r{z}", (7, 0, z), "+x");
        }
        foreach (var x in new[] { 0, 7 })
            p.CSock($"k{x}", (x, 0, 9), "+z");
        p.CSock("t", (7, 11, 0), "+y");
        p.Extra["walk"] = "stairs";
        var kg = (key == "steel" ? 260 : 900) * 2.0 * 2.5 * 3 / 3;
        return p.Stats(kg, s.Hp * 1.5 + 200, s.Arm, 4.0);
    }

    private static BPart ShipLadder()
    {
        var p = new BPart("Ship Ladder 3 m", SectionStairs, "A storey up through a Hatch Floor.", role: "frame", tool: "welder", material: "steel", color: "#6f767f");
        var steps = Enumerable.Range(0, 8).Select(k => (0.25 * k, 0.25 * (k + 1), 0.375 * (k + 1))).ToList();
        StepBoxes(p, steps, 0, 0.75, false, "#9aa0a6");
        foreach (var x in new[] { 0, 2 })
        {
            foreach (var z in new[] { 0, 7 })
                p.CSock($"b{x}_{z}", (x, 0, z), "-y");
            foreach (var y in new[] { 0, 5, 11 })
                p.CSock($"k{x}_{y}", (x, y, 7), "+z");
            p.CSock($"t{x}", (x, 11, 7), "+y");
        }
        foreach (var z in new[] { 0, 7 })
        {
            p.CSock($"l{z}", (0, 0, z), "-x");
            p.CSock($"r{z}", (2, 0, z), "+x");
        }
        p.Extra["walk"] = "ladder";
        return p.Stats(90, 260, 20, 2.5);
    }

    private static BPart Landing(string key)
    {
        var s = StairMats[key];
        var p = new BPart($"{s.Nm} Landing 2x2 m", SectionStairs, "Half a storey up: two Stair Flights turn here.", role: "frame", tool: s.Tool, material: s.Material, color: s.Colour);
        p.CBox(0, 5, 0, 8, 6, 8);
        var corners = new[] { (0, 0), (7, 0), (0, 7), (7, 7) };
        foreach (var (x, z) in corners)
            p.CBox(x, 0, z, x + 1, 5, z + 1);
        foreach (var (x, z) in corners)
        {
            p.CSock($"b{x}_{z}", (x, 0, z), "-y");
            p.CSock($"t{x}_{z}", (x, 5, z), "+y");
        }
        foreach (var v in new[] { 0, 3, 4, 7 })
        {
            p.CSock($"l{v}", (0, 5, v), "-x");
            p.CSock($"r{v}", (7, 5, v), "+x");
            p.CSock($"f{v}", (v, 5, 0), "-z");
            p.CSock($"k{v}", (v, 5, 7), "+z");
        }
        p.Extra["walk"] = "floor";
        return p.Stats(key == "wood" ? 160 : 320, s.Hp * 2 + 100, s.Arm, 3.0);
    }

    private static BPart Ramp(string key, int nx, int nz, int ny)
    {
        var label = key switch { "concrete" => "Concrete", "steel" => "Steel", _ => "Wood" };
        var (colour, material, tool, kg, hp, arm) = key switch
        {
            "concrete" => ("#9a978f", "concrete", "welder", 900, 300, 25),
            "steel" => ("#7a8189", "steel", "welder", 200, 200, 20),
            _ => ("#8a6a40", "wood", "wrench", 80, 80, 2),
        };
        double W = nx * C, D = nz * C, R = ny * C;
        var p = new BPart($"{label} Ramp {F(W)}x{F(D)} m", SectionStairs, "Walk or drive up it.", role: "frame", tool: tool, material: material, color: colour);
        var m = Math.Min(12, (int)Py.RoundInt(R / 0.125));
        var dz = D / m;
        for (var k = 0; k < m; k++)
            p.FBox(0, 0, dz * k, W, R * (k + 1) / m, dz * (k + 1), hidden: true);
        p.FBox(0, 0, 0, W, R, D, detail: true, shape: Wedge(1, 0, 0, -0.5));
        var xs = new SortedSet<int>(Mods(nx)) { 0, nx - 1 };
        foreach (var x in xs)
        {
            foreach (var z in Sorted(0, nz - 1))
                p.CSock($"b{x}_{z}", (x, 0, z), "-y");
            for (var y = 0; y < ny; y++)
                p.CSock($"k{x}_{y}", (x, y, nz - 1), "+z");
        }
        p.Extra["walk"] = "ramp";
        return p.Stats(kg * W * D * R / 2, hp + 60 * W * D, arm, 3.0);
    }

    private static BPart Railing(string key, int n)
    {
        var (label, colour, material, tool, kg, hp, arm) = key == "steel"
            ? ("Steel", "#6f767f", "steel", "welder", 8, 60, 6)
            : ("Wood", "#8a6a40", "wood", "wrench", 6, 40, 1);
        var L = n * C;
        var p = new BPart($"{label} Railing {F(L)} m", SectionStairs, "Blocks people, not rounds.", role: "frame", tool: tool, material: material, color: colour);
        var t = key == "steel" ? 0.03 : 0.04;
        var posts = Enumerable.Range(0, n / 4 + 1).Select(k => k * 4).ToList(); // every metre
        foreach (var k in posts)
        {
            var x = k * C;
            var x0 = k < n ? x : x - 2 * t;
            p.FBox(x0, 0, H - t, x0 + 2 * t, 1.0, H + t);
        }
        p.FBox(0, 1.0 - 2 * t, H - t, L, 1.0, H + t);
        p.FBox(0, 0.5 - t, H - t * 0.7, L, 0.5 + t, H + t * 0.7);
        // Bottom sockets at the posts' feet (a solid face on a cell centre).
        foreach (var k in posts)
        {
            var cell = k < n ? k : n - 1;
            double[] at = [cell * C, -H, 0];
            if (p.OnSolid(Vec(at).Select(Py.D).ToArray(), "-y"))
                p.Sock($"b{cell}", at, "-y");
        }
        p.Sock("l", [-H, 0, 0], "-x", must: false);
        p.Sock("r", [n * C - H, 0, 0], "+x", must: false);
        return p.Stats(kg * L, hp + 20 * L, arm, 1.5);
    }

    // ---- Doors and gates

    private const string SectionDoors = "Doors and gates";

    /// <summary>A door or gate leaf (role door): its hang socket on its -x edge, hung on a Hinge's leaf.</summary>
    private static BPart Leaf(string name, string desc, double w, double h, string colour, string material, double mass, double health, int armour, int bars = 0, string? frame = null)
    {
        var p = new BPart(name, SectionDoors, desc, role: "door", tool: "wrench", material: material, color: colour);
        var t = bars == 0 ? 0.06 : 0.05;
        var use = Kw(("use", true));
        if (bars == 0)
        {
            p.MBox([0, -h / 2, -t / 2], [w, h / 2, t / 2]);
            p.MBox([w - 0.15, -0.05, -t / 2 - 0.04], [w - 0.1, 0.05, t / 2 + 0.04], "#c8c8c8", detail: true, shape: use);
        }
        else
        {
            var f = 0.05;
            p.MBox([0, -h / 2, -t / 2], [w, -h / 2 + f, t / 2], frame);
            p.MBox([0, h / 2 - f, -t / 2], [w, h / 2, t / 2], frame);
            p.MBox([0, -h / 2 + f, -t / 2], [f, h / 2 - f, t / 2], frame);
            p.MBox([w - f, -h / 2 + f, -t / 2], [w, h / 2 - f, t / 2], frame);
            for (var k = 1; k <= bars; k++)
            {
                var x = w * k / (bars + 1);
                p.MBox([x - 0.012, -h / 2 + f, -0.012], [x + 0.012, h / 2 - f, 0.012], frame);
            }
            p.MBox([0.004, -0.02, -0.02], [w, 0.02, 0.02], frame, shape: use); // (inset at the stile: the end faces would share a plane)
        }
        p.Sock("hang", [0, 0, 0], "-x", kind: "leaf");
        p.Sock("front", [w / 2, 0, bars == 0 ? t / 2 : 0.012], "+z", must: bars == 0);
        p.Sock("back", [w / 2, 0, bars == 0 ? -t / 2 : -0.012], "-z", must: bars == 0);
        return p.Stats(mass, health, armour, 2.0);
    }

    private static BPart WindowGlass()
    {
        var p = new BPart("Window Glass 1 m", SectionOpen, "Fits a window wall's window. Any round breaks it.", role: "panel", tool: "wrench", material: "glass", color: "#9fc6d6");
        p.MBox([-H, -H, -0.015], [4 * C - H, 4 * C - H, 0.015], glass: true);
        p.MBox([-H, 2 * C - H - 0.015, -0.02], [4 * C - H, 2 * C - H + 0.015, 0.02], "#5a6068", detail: true);
        p.Sock("bottom", [0, -H, 0], "-y");
        return p.Stats(9, 28, 0, 1.2);
    }

    // ---- Defences

    private const string SectionDef = "Defences";

    private static BPart Sandbags(int n, int h, string name, string desc, bool lShape = false)
    {
        var p = new BPart(name, SectionDef, desc, role: "frame", tool: "wrench", material: "sand", color: "#a8946a");
        double L = n * C, Hm = h * C;
        double vol;
        if (!lShape)
        {
            p.CBox(0, 0, 0, n, h, 2);
            for (var k = 0; k < h; k++)
                if (p.Boxes.Count < 16)
                {
                    var y = (k + 0.5) * C;
                    p.FBox(-0.02, y - 0.03, -0.02, L + 0.02, y + 0.03, 2 * C + 0.02, "#8f7c55", detail: true, shape: Kw(("shape", "rounded"), ("radius", 0.5)));
                }
            foreach (var x in Mods(n))
            {
                foreach (var z in new[] { 0, 1 })
                {
                    p.CSock($"b{x}_{z}", (x, 0, z), "-y");
                    p.CSock($"t{x}_{z}", (x, h - 1, z), "+y");
                }
                p.CSock($"f{x}", (x, 0, 0), "-z");
                p.CSock($"k{x}", (x, 0, 1), "+z");
            }
            foreach (var z in new[] { 0, 1 })
            {
                p.CSock($"l{z}", (0, 0, z), "-x");
                p.CSock($"r{z}", (n - 1, 0, z), "+x");
            }
            vol = L * Hm * 0.5;
        }
        else
        {
            p.CBox(0, 0, 0, 4, h, 2);
            p.CBox(0, 0, 2, 2, h, 4);
            foreach (var (x, z) in new[] { (0, 0), (3, 0), (0, 3), (3, 1), (1, 3) })
            {
                p.CSock($"b{x}_{z}", (x, 0, z), "-y");
                p.CSock($"t{x}_{z}", (x, h - 1, z), "+y");
            }
            foreach (var z in new[] { 0, 1 })
                p.CSock($"r{z}", (3, 0, z), "+x");
            foreach (var x in new[] { 0, 1 })
                p.CSock($"k{x}", (x, 0, 3), "+z");
            p.CSock("f0", (0, 0, 0), "-z");
            p.CSock("l0", (0, 0, 0), "-x");
            vol = (1.0 * 0.5 + 0.5 * 0.5) * Hm;
        }
        return p.Stats(1500 * vol, 300 + 900 * vol, 30, 2.5);
    }

    private static BPart Barrier()
    {
        var p = new BPart("Concrete Barrier 2 m", SectionDef, "Stops vehicles, crouch cover.", role: "frame", tool: "welder", material: "concrete", color: "#a3a097");
        p.CBox(0, 0, 0, 8, 1, 3);
        p.FBox(0, C, 0.2, 2.0, 0.5, 0.55);
        p.FBox(0, 0.5, 0.29, 2.0, 0.75, 0.46);
        p.FBox(-0.005, 0.12, -0.005, 2.005, 0.16, 0.755, "#c2a33a", detail: true);
        foreach (var x in new[] { 0, 7 })
        {
            foreach (var z in new[] { 0, 2 })
                p.CSock($"b{x}_{z}", (x, 0, z), "-y");
            if (x == 0)
                p.CSock($"l{x}", (0, 0, 1), "-x");
            else
                p.CSock($"r{x}", (7, 0, 1), "+x");
        }
        foreach (var x in new[] { 0, 7 })
        {
            p.CSock($"f{x}", (x, 0, 0), "-z");
            p.CSock($"k{x}", (x, 0, 2), "+z");
        }
        return p.Stats(1200, 1400, 30, 3.0);
    }

    private static BPart BlastWall()
    {
        var p = new BPart("Blast Wall 2 m", SectionDef, "Soaks up blasts, stops all but marksman rifles.", role: "frame", tool: "welder", material: "concrete", color: "#8a8e88");
        p.CBox(0, 0, 0, 8, 1, 3);
        p.CBox(0, 1, 1, 8, 12, 2);
        foreach (var x in Mods(8))
        {
            foreach (var z in new[] { 0, 1, 2 })
                p.CSock($"b{x}_{z}", (x, 0, z), "-y");
            p.CSock($"t{x}", (x, 11, 1), "+y");
            foreach (var y in new[] { 1, 6, 11 })
            {
                p.CSock($"f{x}_{y}", (x, y, 1), "-z");
                p.CSock($"k{x}_{y}", (x, y, 1), "+z");
            }
        }
        foreach (var y in new[] { 0, 6 })
        {
            p.CSock($"l{y}", (0, y, 1), "-x");
            p.CSock($"r{y}", (7, y, 1), "+x");
        }
        return p.Stats(4200, 4000, 45, 5.0);
    }

    private static BPart Fence(int n)
    {
        var L = n * C;
        var p = new BPart($"Wire Fence {F(L)} m", SectionDef, "See and shoot through it, can't climb it.", role: "frame", tool: "welder", material: "steel", color: "#7d848b");
        var t = 0.04;
        var Hm = 2.0;
        int[] posts = n == 8 ? [0, n] : [0, n / 2, n];
        foreach (var k in posts)
        {
            var x = k * C;
            var x0 = k == 0 ? x : k == n ? x - 2 * t : x - t;
            p.FBox(x0, 0, H - t, x0 + 2 * t, Hm, H + t);
        }
        p.FBox(0, Hm - 0.05, H - 0.02, L, Hm - 0.01, H + 0.02);
        p.FBox(0, 0.03, H - 0.02, L, 0.07, H + 0.02);
        var wires = 7;
        for (var k = 1; k <= wires; k++)
        {
            var x = L * k / (wires + 1);
            p.FBox(x - 0.01, 0.07, H - 0.01, x + 0.01, Hm - 0.05, H + 0.01, "#9aa1a8");
        }
        p.FBox(0, Hm, H - 0.06, L, Hm + 0.12, H + 0.06, "#5e6369", detail: true, shape: Kw(("shape", "cylinder"), ("segments", 6)));
        p.FBox(0, 1.0 - 0.01, H - 0.008, L, 1.0 + 0.01, H + 0.008, "#9aa1a8", detail: true);
        foreach (var k in posts)
        {
            var cell = k < n ? k : n - 1;
            double[] at = [cell * C, -H, 0];
            if (p.OnSolid(Vec(at).Select(Py.D).ToArray(), "-y"))
                p.Sock($"b{cell}", at, "-y");
        }
        foreach (var y in new[] { 0, 4, 7 })
        {
            p.Sock($"l{y}", [-H, y * C, 0], "-x", must: false);
            p.Sock($"r{y}", [n * C - H, y * C, 0], "+x", must: false);
        }
        return p.Stats(12 * L, 120 + 40 * L, 6, 2.5);
    }

    /// <summary>A 4 by 4 m reinforced bunker (a foundation: it anchors), 3 m tall: two slits each side and front, a door at the back.</summary>
    private static BPart Bunker()
    {
        var p = new BPart("Bunker Module", SectionDef, "Anchored bunker: firing slits, a doorway at the back.", role: "foundation", tool: "welder", material: "concrete", color: "#7c827f");
        var Hm = WallH * C;
        double lo = 1.3, hi = 1.65;
        // Front wall (z 0) with two slits.
        p.FBox(0, 0, 0, 4.0, lo, C);
        p.FBox(0, hi, 0, 4.0, Hm, C);
        foreach (var (x0, x1) in new[] { (0.0, 1.0), (1.5, 2.5), (3.0, 4.0) })
            p.FBox(x0, lo, 0, x1, hi, C);
        // Sides (solid) and the back with a 1.5 m doorway.
        p.FBox(0, 0, C, C, Hm, 4.0 - C);
        p.FBox(4.0 - C, 0, C, 4.0, Hm, 4.0 - C);
        p.FBox(0, 0, 4.0 - C, 1.25, Hm, 4.0);
        p.FBox(2.75, 0, 4.0 - C, 4.0, Hm, 4.0);
        p.FBox(1.25, 9 * C, 4.0 - C, 2.75, Hm, 4.0);
        p.CBox(0, WallH, 0, 16, WallH + 1, 16);
        p.FBox(-0.02, 0, -0.02, 4.02, 0.2, 0.0, "#646a67", detail: true);
        foreach (var x in Mods(16))
            foreach (var z in Mods(16))
                p.CSock($"t{x}_{z}", (x, WallH, z), "+y");
        foreach (var (x, z) in new[] { (0, 0), (15, 0), (0, 15), (15, 15) })
            p.CSock($"b{x}_{z}", (x, 0, z), "-y");
        foreach (var v in new[] { 0, 15 })
        {
            p.CSock($"l{v}", (0, 0, v), "-x");
            p.CSock($"r{v}", (15, 0, v), "+x");
            p.CSock($"f{v}", (v, 0, 0), "-z");
            p.CSock($"k{v}", (v, 0, 15), "+z");
        }
        p.CSock("hinge", (4, 4, 15), "+x", must: true);
        p.CSock("hinge2", (11, 4, 15), "-x", must: true);
        p.Extra["walk"] = "floor";
        var area = 4 * 4.0 * Hm + 16;
        return p.Stats(360 * area * 0.9, 60 + 420 * area * 0.6, 45, 8.0);
    }

    /// <summary>A 3 by 3 m reinforced pillbox (a foundation): slits on three sides, an open doorway at the back.</summary>
    private static BPart Pillbox()
    {
        var p = new BPart("Pillbox", SectionDef, "Anchored pillbox: firing slits, a doorway at the back.", role: "foundation", tool: "welder", material: "concrete", color: "#858a84");
        var Hm = 8 * C;
        var W = 3.0;
        double lo = 1.3, hi = 1.6;
        double z0 = 0, z1 = C;
        p.FBox(0, 0, z0, W, lo, z1);
        p.FBox(0, hi, z0, W, Hm, z1);
        p.FBox(0, lo, z0, 0.9, hi, z1);
        p.FBox(2.1, lo, z0, W, hi, z1);
        foreach (var (x0, x1) in new[] { (0.0, C), (W - C, W) })
        {
            p.FBox(x0, 0, C, x1, lo, W - C);
            p.FBox(x0, hi, C, x1, Hm, W - C);
            p.FBox(x0, lo, C, x1, hi, 1.1);
            p.FBox(x0, lo, 1.9, x1, hi, W - C);
        }
        p.FBox(0, 0, W - C, 0.75, Hm, W);
        p.FBox(2.25, 0, W - C, W, Hm, W);
        p.CBox(0, 8, 0, 12, 9, 12);
        foreach (var (x, z) in new[] { (0, 0), (11, 0), (0, 11), (11, 11), (5, 5), (6, 6), (0, 5), (11, 6) })
            p.CSock($"t{x}_{z}", (x, 8, z), "+y");
        foreach (var (x, z) in new[] { (0, 0), (11, 0), (0, 11), (11, 11) })
            p.CSock($"b{x}_{z}", (x, 0, z), "-y");
        foreach (var v in new[] { 0, 11 })
        {
            p.CSock($"l{v}", (0, 0, v), "-x");
            p.CSock($"r{v}", (11, 0, v), "+x");
            p.CSock($"f{v}", (v, 0, 0), "-z");
            p.CSock($"k{v}", (v, 0, 11), "+z");
        }
        p.Extra["walk"] = "floor";
        var area = 4 * 3.0 * 2.0 + 9;
        return p.Stats(360 * area * 0.9, 60 + 420 * area * 0.6, 45, 7.0);
    }

    // ---- Watchtower kit

    private const string SectionTower = "Watchtower";

    private static BPart TowerPlatform()
    {
        var p = new BPart("Tower Platform 2x2 m", SectionTower, "Lookout on four Wood Posts, open for a Ship Ladder.", role: "frame", tool: "wrench", material: "wood", color: "#7a5a38");
        p.CBox(0, 0, 0, 8, 1, 8);
        p.CBox(0, 1, 7, 8, 5, 8);
        p.CBox(0, 1, 0, 1, 5, 7);
        p.CBox(7, 1, 0, 8, 5, 7);
        p.FBox(0.25, C, 0.25, 1.75, C + 0.006, 1.75, "#6e5332", detail: true);
        foreach (var (x, z) in new[] { (0, 0), (7, 0), (0, 7), (7, 7) })
        {
            p.CSock($"b{x}_{z}", (x, 0, z), "-y");
            p.CSock($"t{x}_{z}", (x, 4, z), "+y");
        }
        foreach (var z in new[] { 0, 7 })
        {
            p.CSock($"l{z}", (0, 0, z), "-x");
            p.CSock($"r{z}", (7, 0, z), "+x");
        }
        foreach (var x in new[] { 0, 7 })
        {
            p.CSock($"k{x}", (x, 0, 7), "+z");
            p.CSock($"f{x}", (x, 0, 0), "-z");
        }
        foreach (var x in new[] { 3, 4 })
            p.CSock($"front{x}", (x, 0, 0), "-z");
        p.Extra["walk"] = "floor";
        return p.Stats(220, 420, 2, 4.0);
    }

    private static BPart TowerRoof()
    {
        var p = new BPart("Tower Roof 2x2 m", SectionTower, "Tower roof, on four Tower Posts.", role: "frame", tool: "wrench", material: "steel", color: "#8d959b");
        // In its own frame the apex points to -z (up once fitted); its base (+z face) goes on the posts.
        for (var k = 0; k < 3; k++)
        {
            var e = (k + 1) / 3.0 * 8;
            var c = 4;
            p.CBox(c - e / 2, c - e / 2, k, c + e / 2, c + e / 2, k + 1, hidden: true);
        }
        p.FBox(0, 0, 0, 2.0, 2.0, 0.75, detail: true, shape: Wedge(0, 0, 0, 0));
        foreach (var (x, y) in new[] { (0, 0), (7, 0), (0, 7), (7, 7) })
            p.CSock($"b{x}_{y}", (x, y, 2), "+z");
        return p.Stats(60, 220, 4, 3.0);
    }

    private static BPart TowerBrace()
    {
        var p = new BPart("Tower Brace", SectionTower, "Stiffens a tower's sides.", role: "frame", tool: "wrench", material: "wood", color: "#7a5a38");
        int w = 6, hcells = 11, m = 6;
        double W = w * C, Hm = hcells * C;
        var dy = Hm / m;
        foreach (var up in new[] { true, false })
            for (var k = 0; k < m; k++)
            {
                double t0 = (double)k / m, t1 = (double)(k + 1) / m;
                var xa = W * (up ? t0 : 1 - t1);
                var xb = W * (up ? t1 : 1 - t0);
                p.FBox(Math.Max(0, xa - 0.04), dy * k, H - 0.04, Math.Min(W, xb + 0.04), dy * (k + 1), H + 0.04);
            }
        foreach (var y in new[] { 0, 5, 10 })
        {
            p.Sock($"l{y}", [-H, y * C, 0], "-x", must: false);
            p.Sock($"r{y}", [W - H, y * C, 0], "+x", must: false);
        }
        return p.Stats(40, 160, 1, 2.0);
    }

    // ---- Columns and beams

    private const string SectionFrame = "Columns and beams";

    private static BPart Column(string key, int h = WallH, string? name = null, string section = SectionFrame)
    {
        var (label, colour, material, tool, kgm, hpm, arm, cells) = key switch
        {
            "concrete" => ("Concrete Column", "#9a978f", "concrete", "welder", 450, 600, 25, 2),
            "steel" => ("Steel Column", "#5f6670", "steel", "welder", 60, 400, 30, 1),
            _ => ("Wood Post", "#7a5a38", "wood", "wrench", 25, 120, 2, 1),
        };
        var Hm = h * C;
        var p = new BPart(name ?? $"{label} {F(Hm)} m", section, "Holds up a floor or a roof.", role: "frame", tool: tool, material: material, color: colour);
        if (key == "steel")
        {
            var f = 0.03;
            p.FBox(0, 0, 0, C, Hm, f);
            p.FBox(0, 0, C - f, C, Hm, C);
            p.FBox(H - 0.015, 0, f, H + 0.015, Hm, C - f, "#545a63");
        }
        else
            p.CBox(0, 0, 0, cells, h, cells);
        for (var x = 0; x < cells; x++)
            for (var z = 0; z < cells; z++)
            {
                p.CSock($"b{x}{z}", (x, 0, z), "-y");
                p.CSock($"t{x}{z}", (x, h - 1, z), "+y");
            }
        foreach (var y in Sorted(0, h / 2, h - 1))
            for (var v = 0; v < cells; v++)
            {
                p.CSock($"l{v}_{y}", (0, y, v), "-x");
                p.CSock($"r{v}_{y}", (cells - 1, y, v), "+x");
                p.CSock($"f{v}_{y}", (v, y, 0), "-z");
                p.CSock($"k{v}_{y}", (v, y, cells - 1), "+z");
            }
        return p.Stats(kgm * Hm, hpm * Hm * cells, arm, 3.0);
    }

    private static BPart Beam(string key, int n)
    {
        var (label, colour, material, tool, kgm, hpm, arm, depth) = key switch
        {
            "concrete" => ("Concrete Beam", "#9a978f", "concrete", "welder", 300, 500, 25, 2),
            "steel" => ("Steel Beam", "#5f6670", "steel", "welder", 40, 300, 30, 2),
            _ => ("Wood Beam", "#7a5a38", "wood", "wrench", 15, 90, 2, 1),
        };
        var L = n * C;
        var p = new BPart($"{label} {F(L)} m", SectionFrame, "Spans between columns or walls under a floor.", role: "frame", tool: tool, material: material, color: colour);
        if (key == "steel")
        {
            var f = 0.03;
            p.FBox(0, 0, 0, L, f, C);
            p.FBox(0, 2 * C - f, 0, L, 2 * C, C);
            p.FBox(0, f, H - 0.015, L, 2 * C - f, H + 0.015, "#545a63");
        }
        else
            p.CBox(0, 0, 0, n, depth, 1);
        foreach (var x in Mods(n))
        {
            p.CSock($"b{x}", (x, 0, 0), "-y");
            p.CSock($"t{x}", (x, depth - 1, 0), "+y");
            for (var y = 0; y < depth; y++)
            {
                p.CSock($"f{x}_{y}", (x, y, 0), "-z");
                p.CSock($"k{x}_{y}", (x, y, 0), "+z");
            }
        }
        for (var y = 0; y < depth; y++)
        {
            p.CSock($"l{y}", (0, y, 0), "-x");
            p.CSock($"r{y}", (n - 1, y, 0), "+x");
        }
        return p.Stats(kgm * L, hpm * L, arm, 3.0);
    }

    // ---- The list

    private static List<(string Folder, BPart P)> Everything()
    {
        var o = new List<(string, BPart)>();
        string[] wallKeys = ["wood", "tin", "concrete", "rc", "steel"];
        for (var k = 0; k < wallKeys.Length; k++)
        {
            var key = wallKeys[k];
            var folder = $"10-Walls/{k + 1}0-{Mats[key].Name}";
            foreach (var n in new[] { 8, 16 })
            {
                var w = StraightWall(key, n);
                StageLinks(w, key, n);
                o.Add((folder, w));
                foreach (var stage in new[] { 1, 2 })
                {
                    var s = StageWall(key, n, stage);
                    StageLinks(s, key, n);
                    o.Add(($"90-Damaged/{k + 1}0-{Mats[key].Name}", s));
                }
                o.Add((folder, StraightWall(key, n, 5, "Half Wall")));
                o.Add((folder, WindowWall(key, n)));
            }
            o.Add((folder, DoorWall(key)));
            if (key != "wood")
                o.Add((folder, GarageWall(key)));
            if (key is "concrete" or "rc" or "steel")
                o.Add((folder, SlitWall(key)));
            foreach (var kind in new[] { "corner", "door", "window" })
                o.Add((folder, Corner(key, kind)));
            o.Add((folder, Tee(key)));
            o.Add((folder, EndCap(key)));
            if (key is "wood" or "concrete")
            {
                o.Add((folder, StraightWall(key, 8, 3, "Parapet")));
                o.Add((folder, StraightWall(key, 16, 3, "Parapet")));
                o.Add((folder, Corner(key, "parapet", 3)));
            }
        }
        o.Add(("10-Walls/60-Glass", WindowGlass()));
        string[] floorKeys = ["wood", "steel", "concrete", "rc"];
        for (var k = 0; k < floorKeys.Length; k++)
        {
            var key = floorKeys[k];
            var folder = $"20-Floors/{k + 1}0-{FloorMats[key].Name}";
            foreach (var (nx, nz) in new[] { (8, 8), (8, 16), (16, 16) })
                o.Add((folder, Slab(key, nx, nz)));
            foreach (var kind in new[] { "hatch", "stair", "well" })
                o.Add((folder, Slab(key, 16, 16, kind)));
        }
        foreach (var kind in new[] { "pad2", "pad4", "step", "deep" })
            o.Add(("30-Foundations", Foundation(kind)));
        o.Add(("30-Foundations", Foundation("pad4", "rc")));
        o.Add(("40-Roofs", RoofSlope("wood", 16, 16, 4)));
        o.Add(("40-Roofs", RoofSlope("wood", 8, 16, 4)));
        o.Add(("40-Roofs", RoofSlope("tin", 16, 16, 4)));
        o.Add(("40-Roofs", RoofSlope("tin", 8, 16, 4)));
        o.Add(("40-Roofs", RoofSlope("tin", 16, 8, 2, "leanto")));
        o.Add(("40-Roofs", RoofSlope("tin", 8, 8, 2, "leanto")));
        o.Add(("40-Roofs", RidgeRoof("wood")));
        o.Add(("40-Roofs", RidgeRoof("tin")));
        o.Add(("40-Roofs", Gable("wood")));
        o.Add(("40-Roofs", Gable("concrete")));
        foreach (var key in new[] { "wood", "steel", "concrete" })
            o.Add(("50-Stairs", StraightStair(key)));
        foreach (var key in new[] { "steel", "concrete" })
        {
            o.Add(("50-Stairs", StraightStair(key, rise: 6, run: 6, label: "Stair Flight")));
            o.Add(("50-Stairs", HalfTurn(key)));
        }
        o.Add(("50-Stairs", ShipLadder()));
        foreach (var key in new[] { "wood", "steel" })
            o.Add(("50-Stairs", Landing(key)));
        o.Add(("50-Stairs", Ramp("concrete", 8, 16, 4)));
        o.Add(("50-Stairs", Ramp("concrete", 16, 16, 4)));
        o.Add(("50-Stairs", Ramp("steel", 8, 16, 4)));
        o.Add(("50-Stairs", Ramp("wood", 4, 12, 3)));
        foreach (var key in new[] { "steel", "wood" })
            foreach (var n in new[] { 8, 16 })
                o.Add(("50-Stairs/Railings", Railing(key, n)));
        o.Add(("60-Doors", Leaf("Wood Door 1.25 m", "For a Door Wall or Door Corner, on a Hinge.", 1.28, 2.2, "#6b5a3e", "wood", 35, 160, 2)));
        o.Add(("60-Doors", Leaf("Steel Door 1.25 m", "For a Door Wall, Door Corner or bunker, on a Hinge.", 1.28, 2.2, "#5a6168", "steel", 120, 600, 30)));
        o.Add(("60-Doors", Leaf("Garage Door Leaf", "Two on Hinges close a Garage Wall.", 1.28, 2.2, "#7d858b", "steel", 90, 380, 6)));
        o.Add(("60-Doors", Leaf("Fence Gate", "On a Hinge at a Wire Fence's end.", 1.78, 1.8, "#7d848b", "steel", 30, 200, 6, bars: 5, frame: "#6a7178")));
        o.Add(("70-Defences", Sandbags(8, 4, "Sandbag Wall 2 m", "Soaks up rounds and blasts.")));
        o.Add(("70-Defences", Sandbags(4, 4, "Sandbag Wall 1 m", "Soaks up rounds and blasts.")));
        o.Add(("70-Defences", Sandbags(8, 2, "Low Sandbag Wall 2 m", "Prone cover.")));
        o.Add(("70-Defences", Sandbags(4, 4, "Sandbag Corner", "Corner of a gun nest.", lShape: true)));
        o.Add(("70-Defences", Barrier()));
        o.Add(("70-Defences", BlastWall()));
        o.Add(("70-Defences", Fence(8)));
        o.Add(("70-Defences", Fence(16)));
        o.Add(("70-Defences", Bunker()));
        o.Add(("70-Defences", Pillbox()));
        o.Add(("80-Tower", TowerPlatform()));
        o.Add(("80-Tower", TowerRoof()));
        o.Add(("80-Tower", TowerBrace()));
        o.Add(("80-Tower", Column("wood", 5, "Tower Post 1.25 m", SectionTower)));
        foreach (var key in new[] { "concrete", "steel", "wood" })
        {
            o.Add(("85-Frame", Column(key)));
            foreach (var n in new[] { 8, 16 })
                o.Add(("85-Frame", Beam(key, n)));
        }
        o.AddRange(BrutalistParts()); // (fins, buttresses, canopies, fascias, slot and ribbon walls: BuildingParts.Brutalist.cs)
        return o;
    }

    // ---- Blueprints: a mirror of the game's fitting rules (ConstructBuild.Place, Rot24)

    public static readonly string[] Dirs = ["+x", "-x", "+y", "-y", "+z", "-z"];

    public static readonly int[][] Cols = MakeCols();

    private static int[][] MakeCols()
    {
        var o = new List<int[]>();
        int[][] perms = [[0, 1, 2], [0, 2, 1], [1, 0, 2], [1, 2, 0], [2, 0, 1], [2, 1, 0]];
        foreach (var p in perms)
            for (var signs = 0; signs < 8; signs++)
            {
                var m = new int[3, 3];
                for (var c = 0; c < 3; c++)
                    m[p[c], c] = (signs >> c & 1) == 0 ? 1 : -1;
                var det = m[0, 0] * (m[1, 1] * m[2, 2] - m[1, 2] * m[2, 1]) - m[0, 1] * (m[1, 0] * m[2, 2] - m[1, 2] * m[2, 0])
                    + m[0, 2] * (m[1, 0] * m[2, 1] - m[1, 1] * m[2, 0]);
                if (det < 0)
                    continue;
                o.Add([.. Enumerable.Range(0, 3).Select(c => p[c] * 2 + ((signs >> c & 1) == 0 ? 0 : 1))]);
            }
        return [.. o];
    }

    public static double[] Apply(int rot, double[] v)
    {
        var o = new double[3];
        for (var c = 0; c < 3; c++)
        {
            var d = Cols[rot][c];
            o[d >> 1] += (d & 1) == 0 ? v[c] : -v[c];
        }
        return o;
    }

    public static int ApplyDir(int rot, int d)
    {
        var c = Cols[rot][d >> 1];
        return (d & 1) == 0 ? c : c ^ 1;
    }

    /// <summary>The turn taking the part's +x and +y to these body directions.</summary>
    public static int Turn(string x = "+x", string y = "+y")
    {
        for (var rr = 0; rr < 24; rr++)
            if (ApplyDir(rr, 0) == Array.IndexOf(Dirs, x) && ApplyDir(rr, 2) == Array.IndexOf(Dirs, y))
                return rr;
        throw new ArgumentException($"no turn {x} {y}");
    }

    public static double Upright(int rr) => Apply(rr, [0, 1, 0])[1] * 2 + Apply(rr, [0, 0, -1])[2] * -1 + Apply(rr, [1, 0, 0])[0] * 0.1;

    public static double[] Quant(double[] v) => [.. v.Select(x => Math.Round(x * 100, MidpointRounding.ToEven) / 100)];

    public static double[] AtOf(PyDict s) => [.. ((List<object?>)s["at"]!).Select(Py.D)];

    public static List<PyDict> SocketsOf(PyDict d) => [.. ((List<object?>)d["sockets"]!).Cast<PyDict>()];

    public sealed class Placed(PyDict d, double[] pos, int rot)
    {
        public readonly PyDict D = d;
        public readonly List<PyDict> Socks = SocketsOf(d);
        public readonly double[] Pos = pos;
        public readonly int Rot = rot;
        public readonly HashSet<int> Used = [];

        public (double[] At, int Dir) Sock(int i)
        {
            var s = Socks[i];
            var a = Apply(Rot, AtOf(s));
            return ([Pos[0] + a[0], Pos[1] + a[1], Pos[2] + a[2]], ApplyDir(Rot, Array.IndexOf(Dirs, (string)s["dir"]!)));
        }
    }

    /// <summary>building_parts.Blueprint: steps worked out the way the game fits parts.</summary>
    public sealed class Blueprint(string name, string desc, Dictionary<string, PyDict> defs)
    {
        public readonly string Name = name;
        public readonly string Desc = desc;
        public readonly Dictionary<string, PyDict> Defs = defs;
        public readonly List<Placed> Parts = [];
        public readonly List<PyDict> Steps = [];

        /// <summary>Signal wires by step, from and to (look.md "The authoring fields"): a switch to its lamps.</summary>
        public readonly List<(int From, int To)> Wires = [];

        public int First(string part, int rot = 0)
        {
            Parts.Add(new Placed(Defs[part], [0.0, 0.0, 0.0], rot));
            Steps.Add(rot == 0 ? new PyDict { ["part"] = part } : new PyDict { ["part"] = part, ["spin"] = rot });
            return 0;
        }

        private (double[] Pos, int Rot) Place(int j, int t, List<PyDict> ds, int dsi, int spin)
        {
            var (at, dr) = Parts[j].Sock(t);
            var want = dr ^ 1;
            var dir = Array.IndexOf(Dirs, (string)ds[dsi]["dir"]!);
            var turns = Enumerable.Range(0, 24).Where(rr => ApplyDir(rr, dir) == want).ToList();
            for (var a = 0; a < turns.Count; a++)
                for (var b = a + 1; b < turns.Count; b++)
                    if (Upright(turns[b]) > Upright(turns[a]))
                        (turns[a], turns[b]) = (turns[b], turns[a]);
            var rot = turns[spin % turns.Count];
            var sa = Apply(rot, AtOf(ds[dsi]));
            return (Quant([at[0] - sa[0], at[1] - sa[1], at[2] - sa[2]]), rot);
        }

        private static string Kind(PyDict s) => s.Get("kind") as string ?? "struct";

        private int Commit(string part, int j, int t, int dsi, double[] pos, int rot, PyDict step)
        {
            var nw = new Placed(Defs[part], pos, rot);
            Parts[j].Used.Add(t);
            nw.Used.Add(dsi);
            Parts.Add(nw);
            Steps.Add(step);
            // AutoLink: the new part's free struct sockets that meet a free struct socket of an earlier part (first part found).
            var me = Parts.Count - 1;
            for (var s = 0; s < nw.Socks.Count; s++)
            {
                if (nw.Used.Contains(s) || Kind(nw.Socks[s]) != "struct")
                    continue;
                var (at, dr) = nw.Sock(s);
                for (var jj = 0; jj < Parts.Count - 1; jj++)
                {
                    var other = Parts[jj];
                    var done = false;
                    for (var tt = 0; tt < other.Socks.Count; tt++)
                    {
                        if (other.Used.Contains(tt) || Kind(other.Socks[tt]) != "struct")
                            continue;
                        var (oat, odr) = other.Sock(tt);
                        var d2 = Py.Sum(Enumerable.Range(0, 3).Select(k => Math.Pow(oat[k] - at[k], 2)));
                        if (odr == (dr ^ 1) && d2 < 0.0004)
                        {
                            other.Used.Add(tt);
                            nw.Used.Add(s);
                            done = true;
                            break;
                        }
                    }
                    if (done)
                        break;
                }
            }
            return me;
        }

        /// <summary>Fits `part` with its origin at `at` (metres, body frame) turned `rot`: finds an earlier part's free socket to go on.</summary>
        public int Put(string part, double[] at, int rot)
        {
            var d = SocketsOf(Defs[part]);
            at = Quant(at);
            for (var j = 0; j < Parts.Count; j++)
            {
                var tgt = Parts[j];
                if (tgt.D.Get("role") as string == "door")
                    continue;
                for (var t = 0; t < tgt.Socks.Count; t++)
                {
                    if (tgt.Used.Contains(t) || Kind(tgt.Socks[t]) != "struct")
                        continue;
                    for (var ds = 0; ds < d.Count; ds++)
                    {
                        if (Kind(d[ds]) != "struct")
                            continue;
                        for (var spin = 0; spin < 4; spin++)
                        {
                            var (pos, rr) = Place(j, t, d, ds, spin);
                            if (rr == rot && Math.Abs(pos[0] - at[0]) < 0.004 && Math.Abs(pos[1] - at[1]) < 0.004 && Math.Abs(pos[2] - at[2]) < 0.004)
                            {
                                var step = new PyDict { ["part"] = part, ["on"] = j, ["socket"] = tgt.Socks[t]["name"], ["by"] = d[ds]["name"] };
                                if (spin != 0)
                                    step["spin"] = spin;
                                return Commit(part, j, t, ds, pos, rr, step);
                            }
                        }
                    }
                }
            }
            throw new InvalidOperationException($"{Name}: no socket takes {part} at [{string.Join(", ", at)}] turned {rot}");
        }

        /// <summary>A step by socket names (a hinge on a jamb, a door on a hinge).</summary>
        public int Hang(string part, int j, string sock, string by, int spin = 0)
        {
            var tgt = Parts[j];
            var d = SocketsOf(Defs[part]);
            var t = tgt.Socks.FindIndex(s => (string)s["name"]! == sock);
            var ds = d.FindIndex(s => (string)s["name"]! == by);
            if (t < 0 || ds < 0)
                throw new InvalidOperationException($"{Name}: no socket {sock} or {by}");
            var (pos, rr) = Place(j, t, d, ds, spin);
            var step = new PyDict { ["part"] = part, ["on"] = j, ["socket"] = sock, ["by"] = by };
            if (spin != 0)
                step["spin"] = spin;
            return Commit(part, j, t, ds, pos, rr, step);
        }

        public PyDict Json()
        {
            if (Steps.Count > 128)
                throw new InvalidOperationException($"{Name}: {Steps.Count} steps");
            var d = new PyDict { ["format"] = "call-of-slop-construct-blueprint/1", ["name"] = Name, ["description"] = Desc, ["steps"] = Steps.Cast<object?>().ToList() };
            if (Wires.Count > 0)
                d["wires"] = Wires.Select(w => (object?)new List<object?> { w.From, w.To }).ToList();
            return d;
        }
    }

    /// <summary>A cell's centre in metres, the body origin being the centre of cell (0, 0, 0).</summary>
    public static double[] Cell(double x, double y, double z) => [x * C, y * C, z * C];

    public const int R0 = 0; // no turn
    public static readonly int RY90 = Turn("-z", "+y"); // +x to -z: a quarter turn left about the vertical (seen from above, x east and z south)
    public static readonly int RY180 = Turn("-x", "+y");
    public static readonly int RY270 = Turn("+z", "+y");

    /// <summary>Four corners round a room whose min cell is (x0, y, z0), nx by nz cells. door/window: which corner (0..3) gets one.</summary>
    private static List<int> RoomCorners(Blueprint bp, string prefix, int x0, int y, int z0, int nx, int nz, int door = -1, int window = -1)
    {
        // Corner 0 at (x0, z0) legs +x, +z; 1 at (x0+nx-1, z0) legs +z, -x; 2 at (x0+nx-1, z0+nz-1) legs -x, -z; 3 at (x0, z0+nz-1) legs -z, +x.
        int[] rots = [R0, RY270, RY180, RY90];
        (int, int)[] spots = [(x0, z0), (x0 + nx - 1, z0), (x0 + nx - 1, z0 + nz - 1), (x0, z0 + nz - 1)];
        var o = new List<int>();
        for (var k = 0; k < 4; k++)
        {
            var kind = door == k ? "Door Corner" : window == k ? "Window Corner" : "Wall Corner";
            o.Add(bp.Put($"{prefix} {kind}", Cell(spots[k].Item1, y, spots[k].Item2), rots[k]));
        }
        return o;
    }

    private static List<Blueprint> Blueprints(Dictionary<string, PyDict> defs)
    {
        var o = new List<Blueprint>();
        const string Pad = "Concrete Foundation 4x4 m";

        // Two-Storey House: two concrete pads, a 4 by 8 m concrete house, a straight stair, a wooden ridge roof, a front door.
        var bp = new Blueprint("Two-Storey House", "Concrete house with a stair and a ridge roof.", defs);
        bp.First(Pad);
        bp.Put(Pad, Cell(0, 0, 16), R0);
        var g = 2; // the ground floor's first cell row (the pads are 2 cells thick)
        RoomCorners(bp, "Concrete", 0, g, 0, 16, 32, door: 1);
        bp.Put("Concrete Window Wall 4 m", Cell(0, g, 23), RY90); // west, along +z from z 8 to 23 (turned: its +x along -z)
        bp.Put("Concrete Window Wall 4 m", Cell(15, g, 8), RY270); // east
        // The stair: along the west wall, rising to -z (from z 15 up to z 4), under the stair floor's opening.
        bp.Put("Concrete Straight Stair 3 m", Cell(4, g, 15), RY180);
        var f = g + WallH; // the first floor slab's row
        bp.Put("Concrete Stair Floor 4x4 m", Cell(0, f, 0), R0);
        bp.Put("Concrete Floor 4x4 m", Cell(0, f, 16), R0);
        var u = f + 1;
        RoomCorners(bp, "Concrete", 0, u, 0, 16, 32, window: 3);
        bp.Put("Concrete Window Wall 4 m", Cell(0, u, 23), RY90);
        bp.Put("Concrete Window Wall 4 m", Cell(15, u, 8), RY270);
        var rf = u + WallH;
        // The ridge roofs: base (+z) down, ridge along the house (z): local x to body x, local z to body -y.
        var rr = Turn("+x", "+z");
        bp.Put("Wood Ridge Roof 4x4 m", Cell(0, rf + 3, 0), rr);
        bp.Put("Wood Ridge Roof 4x4 m", Cell(0, rf + 3, 16), rr);
        o.Add(bp);
        // The front door: a hinge on the door corner's jamb and a wooden door on it.
        var doorCorner = 3; // (steps: 0, 1 pads, 2 corner 0, 3 corner 1: the door corner)
        var h = bp.Hang("Door Hinge", doorCorner, "hinge", "base");
        bp.Hang("Wood Flush Door 1.5x2.25 m", h, "leaf", "hang");

        // Bunker: a reinforced bunker on a pad, a sandbagged yard behind it, a steel door.
        bp = new Blueprint("Bunker", "Bunker with a sandbagged yard.", defs);
        bp.First(Pad);
        bp.Put(Pad, Cell(0, 0, 16), R0);
        var bk = bp.Put("Bunker Module", Cell(0, 2, 0), R0);
        bp.Put("Sandbag Wall 2 m", Cell(0, 2, 23), RY90);
        bp.Put("Sandbag Wall 2 m", Cell(0, 2, 31), RY90);
        bp.Put("Sandbag Wall 2 m", Cell(15, 2, 16), RY270);
        bp.Put("Sandbag Wall 2 m", Cell(15, 2, 24), RY270);
        bp.Put("Sandbag Wall 2 m", Cell(2, 2, 30), R0);
        bp.Put("Sandbag Wall 1 m", Cell(10, 2, 30), R0);
        var hb = bp.Hang("Door Hinge", bk, "hinge", "base");
        bp.Hang("Steel Flush Door 1.5x2.25 m", hb, "leaf", "hang");
        o.Add(bp);

        // Watchtower: a pad, four legs, braces, the platform, a ship ladder, posts and a roof.
        bp = new Blueprint("Watchtower", "Wooden lookout tower with a ladder and roof.", defs);
        bp.First(Pad);
        (int, int)[] legs = [(8, 8), (15, 8), (8, 15), (15, 15)]; // (module points of the pad)
        foreach (var (x, z) in legs)
            bp.Put("Wood Post 2.75 m", Cell(x, 2, z), R0);
        bp.Put("Tower Brace", Cell(9, 2, 15), R0); // back
        bp.Put("Tower Brace", Cell(8, 2, 14), RY90); // west side (along -z from 14 to 9)
        bp.Put("Tower Brace", Cell(15, 2, 9), RY270); // east side
        bp.Put("Tower Platform 2x2 m", Cell(8, 2 + WallH, 8), R0);
        bp.Put("Ship Ladder 3 m", Cell(10, 2, 0), R0);
        foreach (var (x, z) in legs)
            bp.Put("Tower Post 1.25 m", Cell(x, 2 + WallH + 5, z), R0);
        bp.Put("Tower Roof 2x2 m", Cell(8, 2 + WallH + 5 + 5 + 2, 8), Turn("+x", "+z"));
        o.Add(bp);

        // Small Outpost: a corrugated hut with a door, a fenced yard with a gate, a sandbag wall at the front.
        bp = new Blueprint("Small Outpost", "Corrugated hut with a fenced, sandbagged yard.", defs);
        bp.First(Pad);
        bp.Put(Pad, Cell(0, 0, 16), R0);
        var hut = RoomCorners(bp, "Corrugated", 0, 2, 16, 16, 16, door: 0);
        bp.Put("Corrugated Roof Slope 4x4 m", Cell(0, 2 + WallH, 16), R0);
        bp.Put("Wire Fence 4 m", Cell(0, 2, 15), RY90); // west side of the yard
        var fe = bp.Put("Wire Fence 2 m", Cell(15, 2, 15), RY90); // east side, from the hut back to z 8
        bp.Put("Sandbag Wall 2 m", Cell(1, 2, 0), R0);
        bp.Put("Sandbag Wall 1 m", Cell(9, 2, 0), R0);
        var hd = bp.Hang("Door Hinge", hut[0], "hinge", "base");
        bp.Hang("Wood Flush Door 1.5x2.25 m", hd, "leaf", "hang");
        var hg = bp.Hang("Hinge", fe, "r4", "base");
        bp.Hang("Fence Gate", hg, "leaf", "hang");
        o.Add(bp);

        // (The Penal Colony and the Colony Barracks are the Ministry's: BuildingParts.Ministry.cs.)

        // Remote Works: the Ministry's remote labour hall beside the penal colony (docs/story/DEPOT.md, Game/GameDepot.cs). A 4 by
        // 12 m concrete hall with windows down both long sides, an open doorway at the front (z 0, the door corner's side), four
        // Shell Control Chairs in a row down the middle facing the door, and a 12 by 8 m apron in front where the depot's landers
        // come down. The chairs and the apron are found by their places (DepotSpots in GameDepot.cs).
        bp = new Blueprint("Remote Works", "The Ministry of Slop's remote labour hall: four shell control chairs and a landing apron.", defs);
        bp.First(Pad);
        bp.Put(Pad, Cell(0, 0, 16), R0);
        bp.Put(Pad, Cell(0, 0, 32), R0);
        foreach (var j in new[] { 1, 2 })
            foreach (var i in new[] { 0, -1, 1 }) // (the apron, x -4 to 8 by z -8 to 0: each pad next to one already down)
                bp.Put(Pad, Cell(16 * i, 0, -16 * j), R0);
        RoomCorners(bp, "Concrete", 0, 2, 0, 16, 48, door: 1);
        foreach (var z in new[] { 23, 39 })
            bp.Put("Concrete Window Wall 4 m", Cell(0, 2, z), RY90);
        foreach (var z in new[] { 8, 24 })
            bp.Put("Concrete Window Wall 4 m", Cell(15, 2, z), RY270);
        foreach (var z in new[] { 0, 16, 32 })
            bp.Put("Concrete Floor 4x4 m", Cell(0, 2 + WallH, z), R0);
        foreach (var z in new[] { 2.0, 4.0, 6.0, 8.0 })
            bp.Put("Shell Control Chair", [1.75, 0.375, z], R0);
        o.Add(bp);

        // Sealed Data Centre: the first of the five data centres (docs/story/FRAGMENTS.md, Game/GameStoryFragments.cs), past the old
        // mine near the penal colony. A 4 by 8 m concrete block with blank walls and a flat roof, its doorway at the front (z 0), two
        // server racks down the middle and the Data Centre Terminal at the back facing the door. It has no battery: the player
        // brings power. The game finds the terminal by its part, not by its place.
        bp = new Blueprint("Sealed Data Centre", "A sealed data centre from before the records: server racks and a dead terminal.", defs);
        bp.First(Pad);
        bp.Put(Pad, Cell(0, 0, 16), R0);
        RoomCorners(bp, "Concrete", 0, 2, 0, 16, 32, door: 1);
        bp.Put("Concrete Wall 4 m", Cell(0, 2, 23), RY90);
        bp.Put("Concrete Wall 4 m", Cell(15, 2, 8), RY270);
        bp.Put("Concrete Floor 4x4 m", Cell(0, 2 + WallH, 0), R0);
        bp.Put("Concrete Floor 4x4 m", Cell(0, 2 + WallH, 16), R0);
        foreach (var z in new[] { 1.875, 4.125 }) // (two racks down the middle, on the pads' quarter sockets)
            bp.Put("Server Rack", [1.875, 0.375, z], R0);
        bp.Put("Data Centre Terminal", [2.0, 0.375, 6.0], RY180);
        o.Add(bp);

        // Orbital Depot: the Ministry's station high over the Remote Works, where a new inmate first wakes in a robot shell. A 16 by
        // 12 m deck: the shell bay (a corrugated hut, its door to the deck) at the back left, the stores tower at the back right (a
        // watchtower's frame and platform, climbed by a ship ladder: the Driving Module waits on top) and the open hangar deck in
        // front, with no rail at its edge. The game hangs it in the sky and puts the shells, the module and the landers on it.
        bp = new Blueprint("Orbital Depot", "The Ministry of Slop's orbital depot: a shell bay, a stores tower and a hangar deck.", defs);
        bp.First(Pad);
        for (var i = 0; i < 4; i++)
            for (var j = 0; j < 3; j++)
                if (i != 0 || j != 0)
                    bp.Put(Pad, Cell(16 * i, 0, 16 * j), R0);
        RoomCorners(bp, "Corrugated", 0, 2, 32, 16, 16, door: 1);
        bp.Put("Corrugated Roof Slope 4x4 m", Cell(0, 2 + WallH, 32), R0);
        int ox = 48, oz = 32;
        foreach (var (x, z) in legs)
            bp.Put("Wood Post 2.75 m", Cell(ox + x, 2, oz + z), R0);
        bp.Put("Tower Brace", Cell(ox + 9, 2, oz + 15), R0);
        bp.Put("Tower Brace", Cell(ox + 8, 2, oz + 14), RY90);
        bp.Put("Tower Brace", Cell(ox + 15, 2, oz + 9), RY270);
        bp.Put("Tower Platform 2x2 m", Cell(ox + 8, 2 + WallH, oz + 8), R0);
        bp.Put("Ship Ladder 3 m", Cell(ox + 10, 2, oz), R0);
        o.Add(bp);
        o.AddRange(MinistryBlueprints(defs)); // (the Ministry's buildings round the penal colony: BuildingParts.Ministry.cs)
        return o;
    }

    /// <summary>Decision 21 (the cargo volumes, cv01): light but big wrench pieces that are never items (no hands, backpack or storage
    /// slot): the game reads "physicsOnly". Welder pieces and pieces over 40 kg are physics-only by themselves.</summary>
    private static readonly HashSet<string> PhysicsOnly = ["Wood Door 1.25 m", "Fence Gate", "Wood Railing 2 m", "Wood Railing 4 m", "Wood Beam 2 m", "Tower Brace", "Window Glass 1 m"];

    /// <summary>Every part file under Constructs/Parts outside Building, by name (the first found keeps it): the hinges, doors and
    /// furniture the blueprints use. Sorted walk, so the pick doesn't depend on the file system's order.</summary>
    public static void AddPartDefs(PartOutput o, Dictionary<string, PyDict> defs, string skipFolder)
    {
        var root = Path.Combine(o.Root, "Constructs", "Parts");
        foreach (var f in Directory.GetFiles(root, "*.json", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var rel = Path.GetRelativePath(o.Root, f).Replace('\\', '/');
            if (rel.StartsWith(skipFolder + "/", StringComparison.Ordinal))
                continue;
            if (o.ReadJson(rel) is PyDict d && d.Get("name") is string n)
                defs.TryAdd(n, d);
        }
    }

    public void Make(PartOutput o)
    {
        o.Own(Out);
        var defs = new Dictionary<string, PyDict>();
        foreach (var (folder, p) in Everything())
        {
            var d = p.Json();
            var name = (string)d["name"]!;
            if (PhysicsOnly.Contains(name))
            {
                if ((string)d["tool"]! != "wrench" || Py.D(d["mass"]) > 40)
                    throw new InvalidOperationException($"building_parts: {name} is physics-only already");
                d["physicsOnly"] = true;
            }
            if (!defs.TryAdd(name, d))
                throw new InvalidOperationException($"building_parts: part {name} twice");
            o.File($"{Out}/{folder}/{FileName(name)}", PyJson.Dumps(d, 2) + "\n");
        }
        // Stages: the same sockets as the part they stand for.
        foreach (var d in defs.Values)
            if (d.Get("damaged") is PyDict dm)
            {
                static string Key(PyDict x) => string.Join("|", SocketsOf(x).Select(s => $"{s["name"]} {PyJson.Dumps(s["at"])} {s["dir"]}"));
                if (Key(defs[(string)dm["part"]!]) != Key(d))
                    throw new InvalidOperationException($"building_parts: {d["name"]}: its stage's sockets differ");
            }
        // The other parts the blueprints use (the hinge), from their files.
        AddPartDefs(o, defs, Out);
        foreach (var bp in Blueprints(defs))
            o.File($"{BpOut}/{FileName(bp.Name.Replace("-", " "))}", PyJson.Dumps(bp.Json(), 2) + "\n");
    }
}
