using static CallOfSlop.PartKit.PartKitMath;
using BP = CallOfSlop.PartKit.Generators.BuildingParts;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The interior system's own parts (Constructs/Parts/Interior). Was tools/constructs/interior_parts.py; docs/sandbox/INTERIORS.md.
///
/// The Freight Lift Car (phase 2): hung on the carriage of a Linear Actuator 3x3 4 m (linear_parts) stood on end against a shaft wall.
/// Frame: the yoke's back face is x = 0 with the "mount" socket on it (facing -x, onto the carriage's "top"); the car runs along +x;
/// up is +y. The floor's top is FloorDrop below the mount, so the builder puts the floor 2 cm above a deck's floor at the actuator's
/// low stop (12% of 4 m: 0.48 m) and 2 cm above the next deck's floor at the high stop (87%).
///
/// The hollow chassis (phase 4): the Capital Chassis Deck 128x64 m's outside (footprint, 16 m deep, its under, top and side sockets
/// by the same names and places; its mass and strength) with a hollow the interior builder can build in: the beams stand on the
/// interior's 4 m grid, the cross beams have a full-height portal where the corridor runs through them, the top plate has hatches over
/// the planner's stairwells and lift shaft, and each storey has capital floor sockets for the adapter pads in the corridor (one a bay).
/// The armoured hull is the same frame with heavier plate.
///
/// The panel walls (phase 5): the Steel Wall 4 m's section and plate in long runs with few links (a bottom and a top socket at each end,
/// one end socket each side at mid height; face sockets every 2 m link to nothing unless something is fitted). Furniture and floor
/// panels (phase 8), dressing (phase 11) and trim (phase 12) follow. Boxes go through zfight (CLAUDE.md, "No coplanar faces").
/// </summary>
public sealed partial class InteriorParts : IPartGenerator
{
    public string Name => "interior_parts";

    private const string Out = "Constructs/Parts/Interior";
    private const string Plate = "#5d6670", Frame = "#3b4046", Rail = "#c9a21c", Stripe = "#f08a12";

    private const double Length = 3.2; // along +x, away from the rail: the 4 m shaft block less the rail, the carriage and clearance
    private const double Width = 3.5; // across (z): the block less a quarter metre each side
    private const double FloorDrop = 0.46; // the floor's top below the mount: low stop 0.48 m less 2 cm
    private const double PlateT = 0.1;
    private const double YokeT = 0.1;
    private const double YokeTop = 0.6;
    private const double RailH = 1.0; // side rails' top over the floor
    private const double Bar = 0.06;

    private static PyDict? B(double[] mn, double[] mx, string? color = null, bool detail = false, params (string, object?)[] shape) =>
        Box(mn, mx, color, detail: detail, shape: shape.Length > 0 ? Kw(shape) : null);

    private static Part Car()
    {
        var p = new Part("Freight Lift Car", Small, "Lifts", "Hangs on a vertical Linear Actuator's carriage. Freight-sized: a push cart or a stack of crates.",
            role: "frame", tool: "welder", color: Plate, group: "Joints");
        var y0 = -FloorDrop - PlateT;
        var y1 = -FloorDrop;
        var hw = Width / 2;
        p.Add(B([0, y0, -hw], [Length, y1, hw], Plate, false, ("finish", "deck plate")));
        p.Add(B([0, y1, -0.6], [YokeT, YokeTop, 0.6], Frame)); // the yoke, bolted to the carriage
        // side rails: a corner post at the far end, a top bar and a mid bar each side (the near end is the yoke's)
        foreach (var s in new[] { -1, 1 })
        {
            double zo = s * hw, zi = s * (hw - Bar);
            double zlo = Math.Min(zo, zi), zhi = Math.Max(zo, zi);
            p.Add(B([YokeT, y1, zlo], [YokeT + Bar, y1 + RailH, zhi], Frame));
            p.Add(B([Length - Bar, y1, zlo], [Length, y1 + RailH, zhi], Frame));
            p.Add(B([YokeT + Bar, y1 + RailH - Bar, zlo], [Length - Bar, y1 + RailH, zhi], Rail, false, ("finish", "hazard paint")));
            p.Add(B([YokeT + Bar, y1 + RailH * 0.5 - Bar / 2, zlo], [Length - Bar, y1 + RailH * 0.5 + Bar / 2, zhi], Frame));
        }
        // a hazard stripe along the open edge (detail: drawn just proud of the floor)
        p.Add(B([Length - 0.15, y1, -hw + Bar], [Length - 0.005, y1 + 0.006, hw - Bar], Stripe, true, ("paintable", false), ("finish", "hazard paint")));
        p.Sock("mount", [0, 0, 0], "-x");
        p.Sock("button", [YokeT, 0.3, 0.4], "+x"); // the car's send button, on the yoke's face at hand height
        double[] xs = [0.8, 1.8, 2.8], zs = [-1.0, 0.0, 1.0];
        for (var i = 0; i < 3; i++)
            for (var j = 0; j < 3; j++)
                p.Sock($"t{i}_{j}", [xs[i], y1, zs[j]], "+y"); // floor sockets for cargo or a cart's clamp
        var area = Length * Width;
        p.Stats(area * 40 + 120, area * 120, 12, 6);
        p.Set(("walk", "floor"));
        return p;
    }

    // ---- The hollow chassis

    private const string Dark = "#3f444a", Iron = "#6a5f55", Armour = "#4a4f45";
    private const int NX = 8, NZ = 4; // capital cells: 128 x 64 m, as the Capital Chassis Deck 128x64 m
    private const double Grid = -4.125; // the interior grid's first line (InteriorBuilder.Measure on the deck's top sockets with a one-cell margin)
    private const double CellM = 4.0;
    private const double Clear = 0.05; // beams and hatch edges stand this far off a grid line (walls poke 1 cm past their block)
    private const double StoreyM = 3.0;
    private const int Storeys = 4;
    private static readonly (int From, int To) Spine = (6, 8); // the planner's corridor rows on a 14-cell-deep grid
    private static readonly int[] BeamCols = [11, 19]; // the grid columns the two inner cross beams fill
    // the planner's shafts on this grid (north inner band, 1x3 stairs at a quarter and three quarters, the 2x3 lift in the middle)
    private static readonly (int X, int Z, int W, int D)[] Hatches = [(7, 3, 1, 3), (16, 3, 2, 3), (22, 3, 1, 3)];
    private static readonly int[] FloorBlocks = [4, 14, 25]; // corridor blocks (row Spine.From) holding each storey's floor sockets, one a bay

    /// <summary>Grid line i (m).</summary>
    private static double G(int i) => Grid + i * CellM;

    private static Part HollowChassis(bool armoured = false)
    {
        var c = Capital;
        double w = NX * c.Cell, l = NZ * c.Cell;
        var name = armoured ? $"Armoured Hull {Fmt(w)}x{Fmt(l)} m" : $"Hollow Chassis {Fmt(w)}x{Fmt(l)} m";
        var desc = armoured
            ? "Heavy armoured hull, 16 m deep, hollow inside: four storeys of interior under the deck."
            : "Heavy grillage, 16 m deep, hollow inside: four storeys of interior under the deck.";
        var p = new Part($"{c.Name} {name}", c, "Chassis", desc, role: "frame", color: armoured ? Armour : Dark);
        p.D["centreSockets"] = false; // (a big part's sockets are its lattice, nothing else)
        var t = c.Plate * 2;
        double x0 = c.Lo(), x1 = c.Hi(NX), y0 = c.Lo(), y1 = c.Hi(1), z0 = c.Lo(), z1 = c.Hi(NZ);
        double lo = y0 + t, hi = y1 - t; // the hollow's floor and roof
        p.Add(B([x0, y0, z0], [x1, lo, z1]));
        // the top plate round its hatches: a strip north and south of them, the band between cut by the hatches
        double hz0 = G(Hatches[0].Z) - Clear, hz1 = G(Hatches[0].Z + Hatches[0].D) + Clear;
        p.Add(B([x0, hi, z0], [x1, y1, hz0]));
        p.Add(B([x0, hi, hz1], [x1, y1, z1]));
        var at = x0;
        foreach (var (hx, _, hw, _) in Hatches)
        {
            p.Add(B([at, hi, hz0], [G(hx) - Clear, y1, hz1]));
            at = G(hx + hw) + Clear;
        }
        p.Add(B([at, hi, hz0], [x1, y1, hz1]));
        var beam = Iron;
        // end beams and edge beams round the hollow, just outside the grid
        double ex0 = G(0) - Clear, ex1 = G(30) + Clear, ez0 = G(0) - Clear, ez1 = G(14) + Clear;
        p.Add(B([x0, lo, z0], [ex0, hi, z1], beam));
        p.Add(B([ex1, lo, z0], [x1, hi, z1], beam));
        p.Add(B([ex0, lo, z0], [ex1, hi, ez0], beam));
        p.Add(B([ex0, lo, ez1], [ex1, hi, z1], beam));
        // the inner cross beams, each filling a grid column, with a full-height portal where the corridor runs through
        foreach (var col in BeamCols)
        {
            double bx0 = G(col) + Clear, bx1 = G(col + 1) - Clear;
            p.Add(B([bx0, lo, ez0], [bx1, hi, G(Spine.From) - Clear], beam));
            p.Add(B([bx0, lo, G(Spine.To) + Clear], [bx1, hi, ez1], beam));
        }
        if (armoured)
        {
            // belt armour on the long sides (drawn proud of the edge beams, so it reads as armour)
            var a = 0.6;
            foreach (var (zf, zb) in new[] { (z0 - a, z0), (z1, z1 + a) })
                p.Add(B([x0 + 2, y0 + 2, zf], [x1 - 2, y1 - 2, zb], Armour, true));
        }
        // the deck's own sockets, by the same names and in the same places (ChassisDeck in CapitalParts.cs)
        foreach (var i in Pick(NX, 4))
            foreach (var k in Pick(NZ, 4))
            {
                p.Sock($"under{i}.{k}", [i * c.Cell, y0, k * c.Cell], "-y");
                p.Sock($"top{i}.{k}", [i * c.Cell, y1, k * c.Cell], "+y");
            }
        foreach (var i in Pick(NZ, 3))
        {
            p.Sock($"-x{i}", [x0, 0, i * c.Cell], "-x");
            p.Sock($"+x{i}", [x1, 0, i * c.Cell], "+x");
        }
        foreach (var i in Pick(NX, 3))
        {
            p.Sock($"-z{i}", [i * c.Cell, 0, z0], "-z");
            p.Sock($"+z{i}", [i * c.Cell, 0, z1], "+z");
        }
        // each storey's floor sockets (an adapter pad's back), in the middle of a corridor block in each bay; above the bottom plate
        // each sits on a bracket (a socket stands on a solid face), just under the slab, so the corridor below keeps its headroom
        for (var k = 0; k < Storeys; k++)
            for (var b = 0; b < FloorBlocks.Length; b++)
            {
                double x = G(FloorBlocks[b]) + 2.125, y = lo + k * StoreyM, z = G(Spine.From) + 2.125;
                if (k > 0)
                    p.Add(B([x - 0.25, y - 0.15, z - 0.25], [x + 0.25, y, z + 0.25], beam));
                p.Sock($"floor{k}.{b}", [x, y, z], "+y");
            }
        var area = w * l * 2 + 2 * (w + l) * c.Cell;
        if (armoured)
            return p.Stats(area * c.KgM2 * 3.5, Math.Min(16_000_000, 40 + area * c.HealthM2 * 3.5), c.Armour * 4);
        return p.Stats(area * c.KgM2 * 2.5, Math.Min(16_000_000, 40 + area * c.HealthM2 * 2.5), c.Armour * 2);
    }

    /// <summary>A steel storey wall `metres` long (a multiple of 4) with links only where they hold it.</summary>
    private static BP.BPart PanelWall(int metres)
    {
        var n = (int)(metres / BP.C);
        var h = BP.WallH;
        var L = n * BP.C;
        var m = BP.Mats["steel"];
        var p = new BP.BPart($"Steel Panel Wall {metres} m", BP.SectionWalls, "Long storey wall with few links (interiors). " + BP.MatWords["steel"],
            role: "frame", tool: m.Tool, material: m.Material, color: m.Colour) { Panel = true };
        p.FBox(0, 0, 0, L, h * BP.C, BP.C);
        BP.WallDetails(p, "steel", n, h, [(0, 0, L, h * BP.C)]);
        foreach (var x in new[] { 0, n - 1 })
        {
            p.CSock($"b{x}", (x, 0, 0), "-y");
            p.CSock($"t{x}", (x, h - 1, 0), "+y");
        }
        p.CSock($"l{h / 2}", (0, h / 2, 0), "-x");
        p.CSock($"r{h / 2}", (n - 1, h / 2, 0), "+x");
        var xs = new SortedSet<int>(Enumerable.Range(0, (n + 7) / 8).Select(k => k * 8)) { n - 1 };
        foreach (var x in xs)
            foreach (var y in BP.Thirds(h))
            {
                p.CSock($"f{x}_{y}", (x, y, 0), "-z");
                p.CSock($"k{x}_{y}", (x, y, 0), "+z");
            }
        p.D["finish"] = "wall panelling"; // (phase 12: a wall reads as a wall; the builder gives each its faction's own by a step's finish)
        var area = L * h * BP.C;
        return BP.MatStats(p, "steel", area, secondsScale: 0.6 + 0.4 * n / 16);
    }

    // ---- Room furniture (phase 8): small-class pieces the builder stands on a floor slab's top socket by their "bottom" socket (the
    // part's origin, at its floor face). Front is +z. Containers are ordinary storage (slots), the boards read the circuit's charge as
    // the Charge Gauge does, the medical station is a seat that heals its sitter.

    private const string Steel = "#6a737b", Darks = "#3a3f45", White = "#d8dcd8", Red = "#b8352c", Wood = "#7a5a38", Brass = "#a58a4e", Glow = "#9fd18c";

    private static Part Furniture(string name, string desc, string role, double w, double h, double d, string color, string tool = "wrench",
        double mass = 40, double health = 200, int armour = 4, double seconds = 2.0, string finish = "painted steel")
    {
        var p = new Part(name, Small, "Furniture", desc, role: role, tool: tool, color: color, group: "Interior");
        p.Set(("finish", finish)); // (phase 12: the part's material; a box naming its own keeps it)
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Sock("top", [0, h, 0], "+y");
        p.Sock("back", [0, h / 2, -d / 2], "-z");
        p.Sock("+x", [w / 2, h / 2, 0], "+x");
        p.Sock("-x", [-w / 2, h / 2, 0], "-x");
        p.Stats(mass, health, armour, seconds);
        return p;
    }

    private static PyDict Slot(double[] mn, double[] mx, string? label = null, int stack = 0)
    {
        var s = new PyDict { ["min"] = mn.Select(v => (object?)Py.Round(v, 3)).ToList(), ["max"] = mx.Select(v => (object?)Py.Round(v, 3)).ToList() };
        if (!string.IsNullOrEmpty(label))
            s["label"] = label;
        if (stack != 0)
            s["stack"] = stack;
        return s;
    }

    private static PyDict Storage(params PyDict[] slots) => new() { ["slots"] = slots.Cast<object?>().ToList() };

    /// <summary>A steel cabinet open at the front: back, sides, top, bottom, the shelves between; door: a detail panel colour drawn on the
    /// lower front.</summary>
    private static void Cabinet(Part p, double w, double h, double d, string color, double[] shelves, string? door = null, double t = 0.03)
    {
        double hw = w / 2, hd = d / 2;
        p.Add(B([-hw, 0, -hd], [hw, t, hd], color));
        p.Add(B([-hw, h - t, -hd], [hw, h, hd], color));
        p.Add(B([-hw, t, -hd], [hw, h - t, -hd + t], color));
        foreach (var sx in new[] { -1, 1 })
        {
            double a = sx * hw, b = sx * (hw - t);
            p.Add(B([Math.Min(a, b), t, -hd + t], [Math.Max(a, b), h - t, hd], color));
        }
        foreach (var y in shelves)
            p.Add(B([-hw + t, y - t / 2, -hd + t], [hw - t, y + t / 2, hd], color));
        if (!string.IsNullOrEmpty(door))
            p.Add(B([-hw + t, t, hd - 0.004], [hw - t, h - t, hd - 0.001], door, true));
    }

    private static Part Locker()
    {
        double w = 0.6, h = 1.9, d = 0.5;
        var p = Furniture("Locker", "A crew locker: two shelves for a person's kit.", "storage", w, h, d, Steel, mass: 45, health: 240);
        Cabinet(p, w, h, d, Steel, [0.95]);
        p.Add(B([-0.2, 1.45, d / 2 - 0.003], [0.2, 1.5, d / 2 + 0.003], Darks, true)); // name plate
        p.Set(("storage", Storage(Slot([-0.26, 0.04, -0.21], [0.26, 0.92, 0.24]), Slot([-0.26, 0.98, -0.21], [0.26, 1.86, 0.24]))));
        return p;
    }

    private static Part GunRack()
    {
        double w = 1.2, h = 1.6, d = 0.4;
        var p = Furniture("Gun Rack", "Three long guns or barrels stood upright, and a shelf of small parts.", "storage", w, h, d, Darks, mass: 50, health: 260);
        var t = 0.04;
        p.Add(B([-w / 2, 0, -d / 2], [w / 2, 0.08, d / 2], Darks)); // foot
        p.Add(B([-w / 2, 0.08, -d / 2], [w / 2, h, -d / 2 + t], Wood, false, ("finish", "wood"))); // back board
        foreach (var x in new[] { -w / 2, -0.21, 0.19, w / 2 - t })
            p.Add(B([x, 0.08, -d / 2 + t], [x + t, h - 0.3, d / 2], Darks)); // dividers
        p.Add(B([-w / 2, h - 0.3, -d / 2 + t], [w / 2, h - 0.3 + t, d / 2], Darks)); // the top shelf
        p.Set(("storage", Storage(Slot([-0.555, 0.09, -0.15], [-0.215, 1.29, 0.19], "long gun"),
            Slot([-0.165, 0.09, -0.15], [0.185, 1.29, 0.19], "long gun"),
            Slot([0.235, 0.09, -0.15], [0.555, 1.29, 0.19], "long gun"),
            Slot([-0.55, h - 0.25, -0.15], [0.55, h - 0.02, 0.19], "parts", 6))));
        return p;
    }

    private static Part MedicineCabinet()
    {
        double w = 0.8, h = 1.7, d = 0.35;
        var p = Furniture("Medicine Cabinet", "Shelves of medical stores behind glass.", "storage", w, h, d, White, mass: 35, health: 160, armour: 2, finish: "gloss paint");
        Cabinet(p, w, h, d, White, [0.6, 1.15]);
        p.Add(B([-0.05, h - 0.2, d / 2 - 0.003], [0.05, h - 0.06, d / 2 + 0.003], Red, true)); // the cross
        p.Add(B([-0.12, h - 0.15, d / 2 - 0.0035], [0.12, h - 0.11, d / 2 + 0.0025], Red, true));
        p.Set(("storage", Storage(Slot([-0.37, 0.04, -0.14], [0.37, 0.58, 0.16], "medical"), Slot([-0.37, 0.62, -0.14], [0.37, 1.13, 0.16], "medical"),
            Slot([-0.37, 1.17, -0.14], [0.37, 1.66, 0.16], "medical"))));
        return p;
    }

    private static Part ToolRack()
    {
        double w = 1.4, h = 1.8, d = 0.4;
        var p = Furniture("Tool Rack", "A pegboard and bins for tools and spares.", "storage", w, h, d, Steel, mass: 55, health: 240);
        var t = 0.04;
        p.Add(B([-w / 2, 0, -d / 2], [w / 2, 0.85, d / 2], Steel)); // the bench cabinet
        p.Add(B([-w / 2, 0.85, -d / 2], [w / 2, h, -d / 2 + t], "#8a6a40", false, ("finish", "wood"))); // the pegboard
        p.Add(B([-w / 2, 1.25, -d / 2 + t], [w / 2, 1.25 + t, d / 2 - 0.1], Steel)); // the bin shelf
        p.Set(("storage", Storage(Slot([-0.66, 0.86, -0.15], [-0.01, 1.24, 0.19], "tools"), Slot([0.01, 0.86, -0.15], [0.66, 1.24, 0.19], "tools"),
            Slot([-0.66, 1.3, -0.15], [-0.23, 1.75, 0.09], "parts bin", 6), Slot([-0.21, 1.3, -0.15], [0.21, 1.75, 0.09], "parts bin", 6),
            Slot([0.23, 1.3, -0.15], [0.66, 1.75, 0.09], "parts bin", 6))));
        return p;
    }

    private static Part Table(string name, string desc, double w, double h, double d, string top, string legs, PyDict[] slots, double mass)
    {
        var p = Furniture(name, desc, "storage", w, h, d, top, mass: mass, health: 180, armour: 2);
        double t = 0.05, leg = 0.06;
        p.Add(B([-w / 2, h - t, -d / 2], [w / 2, h, d / 2], top, false, ("finish", "plastic")));
        foreach (var sx in new[] { -1, 1 })
            foreach (var sz in new[] { -1, 1 })
            {
                double x = sx * (w / 2 - 0.05), z = sz * (d / 2 - 0.05);
                p.Add(B([Math.Min(x, x - sx * leg), 0, Math.Min(z, z - sz * leg)], [Math.Max(x, x - sx * leg), h - t, Math.Max(z, z - sz * leg)], legs));
            }
        p.Add(B([-w / 2 + 0.11, 0.12, -d / 2 + 0.11], [w / 2 - 0.11, 0.15, d / 2 - 0.11], legs)); // the shelf under the top
        p.Set(("storage", Storage(slots)));
        return p;
    }

    private static Part MessTable()
    {
        double w = 1.8, h = 0.75, d = 0.9;
        return Table("Mess Table", "A crew table: what is set on it stays on it.", w, h, d, "#8a8f94", Darks,
            [Slot([-0.78, 0.16, -0.33], [-0.01, h - 0.06, 0.33], "shelf"), Slot([0.01, 0.16, -0.33], [0.78, h - 0.06, 0.33], "shelf")], 60);
    }

    private static Part ChartTable()
    {
        double w = 1.4, h = 0.95, d = 1.0;
        var p = Furniture("Chart Table", "The navigator's table, with a chart drawer.", "storage", w, h, d, Wood, mass: 70, health: 200, armour: 2, finish: "wood");
        var t = 0.04;
        p.Add(B([-w / 2, 0, -d / 2], [w / 2, h - 0.25, d / 2], Wood)); // the cabinet
        p.Add(B([-w / 2, h - 0.25, -d / 2], [w / 2, h - 0.25 + t, d / 2], Wood)); // the drawer floor
        foreach (var sx in new[] { -1, 1 })
        {
            double a = sx * w / 2, b = sx * (w / 2 - t);
            p.Add(B([Math.Min(a, b), h - 0.25 + t, -d / 2], [Math.Max(a, b), h - t, d / 2], Wood));
        }
        p.Add(B([-w / 2 + t, h - 0.25 + t, -d / 2], [w / 2 - t, h - t, -d / 2 + t], Wood));
        p.Add(B([-w / 2, h - t, -d / 2], [w / 2, h, d / 2], "#c9c2a8", false, ("finish", "matte paint"))); // the chart's face
        p.Set(("storage", Storage(Slot([-0.65, h - 0.2, -0.45], [0.65, h - 0.045, 0.49], "charts"))));
        return p;
    }

    private static Part GalleyStove()
    {
        double w = 1.0, h = 0.9, d = 0.7;
        var p = Furniture("Galley Stove", "A galley range: hobs over an oven (cooking comes with food).", "storage", w, h, d, Steel, mass: 90, health: 260, finish: "steel");
        Cabinet(p, w, h, d, Steel, []);
        foreach (var x in new[] { -0.25, 0.25 })
            foreach (var z in new[] { -0.15, 0.15 })
                p.Add(B([x - 0.09, h, z - 0.09], [x + 0.09, h + 0.02, z + 0.09], Darks, true)); // hobs
        p.Add(B([-0.3, h - 0.15, d / 2 + 0.001], [0.3, h - 0.11, d / 2 + 0.03], Darks, true)); // the oven's handle
        p.Set(("storage", Storage(Slot([-0.46, 0.04, -0.31], [0.46, h - 0.04, 0.34], "oven"))));
        return p;
    }

    /// <summary>A panel that reads the circuit's charge (the Charge Gauge's logic): a cabinet, or a console with its dial on a desk.</summary>
    private static Part Board(string name, string desc, double w, double h, double d, bool desk)
    {
        var p = Furniture(name, desc, "logic", w, h, d, Darks, mass: 60, health: 180, armour: 3, finish: "matte paint");
        if (desk)
        {
            p.Add(B([-w / 2, 0, -d / 2], [w / 2, 0.8, d / 2], Darks));
            p.Add(B([-w / 2, 0.8, -d / 2], [w / 2, h, -d / 2 + 0.2], Darks));
            p.Add(B([-0.35, 0.95, -d / 2 + 0.2], [0.35, 1.1, -d / 2 + 0.203], Glow, true)); // the screen
            p.D["dial"] = new PyDict { ["at"] = Py.L(0, 0.82, 0.0), ["face"] = "+y", ["max"] = 1 };
        }
        else
        {
            p.Add(B([-w / 2, 0, -d / 2], [w / 2, h, d / 2], Darks));
            for (var i = 0; i < 4; i++)
            {
                var y = 0.4 + i * 0.3;
                p.Add(B([-0.45, y, d / 2], [0.45, y + 0.04, d / 2 + 0.004], Brass, true, ("finish", "brass"))); // breaker rows
            }
            p.D["dial"] = new PyDict { ["at"] = Py.L(0, h - 0.25, d / 2 + 0.01), ["face"] = "+z", ["max"] = 1 };
        }
        p.D["logic"] = new PyDict { ["kind"] = "chargegauge" };
        return p;
    }

    private static Part MedicalStation()
    {
        double w = 0.8, h = 1.6, d = 2.0;
        var p = Furniture("Medical Station", "A treatment couch with its monitor: whoever sits in it heals, 4 health a second.", "seat", w, h, d, White,
            mass: 120, health: 240, armour: 3, seconds: 3.0, finish: "gloss paint");
        p.Add(B([-w / 2, 0, -d / 2], [w / 2, 0.5, d / 2], White)); // the couch
        p.Add(B([-w / 2 + 0.05, 0.5, -d / 2 + 0.05], [w / 2 - 0.05, 0.58, d / 2 - 0.05], "#5a8fa0", false, ("finish", "plastic"))); // its pad
        p.Add(B([-0.06, 0.58, -d / 2 + 0.05], [0.06, h, -d / 2 + 0.17], Steel)); // the monitor's post
        p.Add(B([-0.25, h - 0.35, -d / 2 + 0.17], [0.25, h, -d / 2 + 0.22], Darks)); // the monitor
        p.Add(B([-0.2, h - 0.3, -d / 2 + 0.22], [0.2, h - 0.05, -d / 2 + 0.223], Glow, true));
        p.D["seat"] = new PyDict { ["at"] = Py.L(0, 0.15, 0.2), ["heal"] = 4 };
        return p;
    }

    private static Part RepairStation()
    {
        double w = 1.6, h = 1.5, d = 0.8;
        var p = Furniture("Repair Station", "A welding bench with stock for repairs at hand.", "storage", w, h, d, "#5d6670", mass: 150, health: 320, armour: 6, finish: "dirty steel");
        p.Add(B([-w / 2, 0, -d / 2], [w / 2, 0.85, d / 2], "#5d6670")); // the bench
        p.Add(B([-w / 2, 0.85, -d / 2], [w / 2, h, -d / 2 + 0.05], Steel)); // the back
        p.Add(B([0.45, 0.85, -0.2], [0.7, 1.25, 0.05], "#c9a21c", false, ("finish", "matte paint"))); // the welding set
        p.Set(("storage", Storage(Slot([-0.75, 0.86, -0.34], [0.4, 1.3, 0.38], "stock"), Slot([-0.75, 1.32, -0.34], [0.75, h - 0.02, -0.0], "parts bin", 6))));
        return p;
    }

    private static List<Part> FurnitureParts() =>
    [
        Locker(), GunRack(), MedicineCabinet(), ToolRack(), MessTable(), ChartTable(), GalleyStove(),
        Board("Power Board", "The plant room's breaker board: its dial reads the circuit's charge, 0 to 1.", 1.2, 1.8, 0.3, false),
        Board("Engineer's Panel", "The bridge's power board: its dial reads the circuit's charge, 0 to 1.", 1.0, 1.2, 0.6, true),
        MedicalStation(), RepairStation(),
    ];

    // ---- Floor panels (phase 8): the Steel Floor 4x4 m's plate and section in big squares. Top sockets wherever a wall or a corner may
    // stand (each 4 m block's edge lines, at the 2 m module points), bottom sockets where a wall below may hold it, edge sockets at each
    // block's ends. A part has at most 255 sockets: the 16 m panel keeps its edge lines' tops and only its rim's bottoms.

    private static BP.BPart FloorPanel(int metres)
    {
        var m = BP.FloorMats["steel"];
        var n = (int)(metres / BP.C); // cells a side
        var blocks = n / 16;
        var p = new BP.BPart($"Steel Floor Panel {metres}x{metres} m", BP.SectionFloors, $"Big floor with few links (interiors). {BP.FloorWords["steel"]}",
            role: "frame", tool: m.Tool, material: m.Material, color: m.Colour) { Panel = true };
        p.CBox(0, 0, 0, n, 1, n);
        p.FBox(0.05, BP.C, 0.05, n * BP.C - 0.05, BP.C + 0.008, n * BP.C - 0.05, "#a3a8ac", detail: true);
        var mods = BP.Mods(n);
        var edge = new SortedSet<int>();
        for (var b = 0; b < blocks; b++)
        {
            edge.Add(b * 16);
            edge.Add(b * 16 + 15);
        }
        var full = blocks <= 2;
        foreach (var x in mods)
            foreach (var z in mods)
                if (full || edge.Contains(x) || edge.Contains(z))
                    p.CSock($"t{x}_{z}", (x, 0, z), "+y");
        foreach (var x in mods)
            foreach (var z in mods)
                if (full || edge.Contains(x) && (z == 0 || z == n - 1) || edge.Contains(z) && (x == 0 || x == n - 1))
                    p.CSock($"b{x}_{z}", (x, 0, z), "-y");
        foreach (var e in edge)
        {
            p.CSock($"l{e}", (0, 0, e), "-x");
            p.CSock($"r{e}", (n - 1, 0, e), "+x");
            p.CSock($"f{e}", (e, 0, 0), "-z");
            p.CSock($"k{e}", (e, 0, n - 1), "+z");
        }
        p.D["finish"] = "deck plate";
        p.Extra["walk"] = "floor";
        var area = metres * metres;
        return p.Stats(m.Kgm2 * area, 60 + m.Hpm2 * area, m.Arm, Py.Round(Math.Min(40.0, Math.Max(2.0, m.Secs * area / 16 * 0.5)), 1));
    }

    // ---- Dressing (phase 11): things only looked at, hung on a wall's face socket by their "back" socket (the part's origin, facing -z;
    // the part stands in +z, into the room) or stood on a floor socket by their "bottom". Up is +y. A wall's face sockets are 0.125,
    // 1.375 and 2.625 m over the floor and the ceiling is 2.75 m up, so a top-band piece keeps under y = +0.11 and hangs below its
    // socket. The mains lamps light from the power circuit (logic "mains": ConstructLamps.Lit), so a room's lights need no switch.

    private const string GlowPale = "#fff3b0", Yellow = "#d9b23a", Hazard = "#262626", Rust = "#7d6b4f", PipeGreen = "#5f7d6a", Galv = "#a4abb0", Cable = "#2b2d30", Orange = "#c8692c";
    private const string Lagged = "#c4bda8";

    private static List<object?> LampConfig() =>
    [
        new PyDict { ["key"] = "colour", ["type"] = "colour", ["label"] = "Colour", ["group"] = "Light", ["default"] = "#ffed99", ["tooltip"] = "The colour it glows when lit." },
        new PyDict { ["key"] = "brightness", ["label"] = "Brightness", ["group"] = "Light", ["unit"] = "%", ["min"] = 10, ["max"] = 200, ["step"] = 1, ["default"] = 100, ["tooltip"] = "How bright it glows." },
        new PyDict { ["key"] = "blink", ["label"] = "Blink", ["group"] = "Light", ["unit"] = "Hz", ["min"] = 0, ["max"] = 10, ["step"] = 0.1, ["default"] = 0, ["tooltip"] = "Flashes this many times a second while lit (0: steady)." },
    ];

    private static Part Hung(string name, string desc, string role = "frame", string color = Steel, double mass = 10, double health = 80, int armour = 2, double seconds = 1.0, string tool = "wrench", string? finish = null)
    {
        var p = new Part(name, Small, "Dressing", desc, role: role, tool: tool, color: color, group: "Interior");
        if (!string.IsNullOrEmpty(finish))
            p.Set(("finish", finish)); // (phase 12: the part's material; a box naming its own keeps it, a lamp's glow names none)
        p.Sock("back", [0, 0, 0], "-z");
        p.Stats(mass, health, armour, seconds);
        return p;
    }

    /// <summary>A lamp's glowing face: lit while the lamp is (the game draws a lit lamp's unpaintable detail bright).</summary>
    private static PyDict? GlowBox(double[] mn, double[] mx) => B(mn, mx, GlowPale, true, ("paintable", false));

    private static Part MainsLamp(Part p, int watts) =>
        p.Set(("logic", new PyDict { ["kind"] = "lamp", ["mains"] = true }), ("power", watts), ("config", LampConfig()));

    private static Part WallLight()
    {
        var p = Hung("Wall Light", "A lamp for a room's wall. Lights while the power circuit reaches it and has charge (no wire needed); a wire into it switches it.",
            role: "logic", color: Darks, mass: 4, health: 40, armour: 1);
        p.Add(B([-0.3, -0.07, 0.0], [0.3, 0.05, 0.12], Darks, false, ("finish", "matte paint")));
        p.Add(GlowBox([-0.27, -0.09, 0.02], [0.27, -0.07, 0.105]));
        return MainsLamp(p, 20);
    }

    private static Part HallFloodlight()
    {
        var p = Hung("Hall Floodlight", "A big lamp on an arm for a hall's wall. Lights while the power circuit reaches it and has charge; a wire into it switches it.",
            role: "logic", color: Darks, mass: 18, health: 80, armour: 2);
        p.Add(B([-0.15, -0.3, 0.0], [0.15, 0.08, 0.04], Darks, false, ("finish", "matte paint"))); // wall plate
        p.Add(B([-0.05, -0.12, 0.04], [0.05, 0.0, 0.42], Steel, false, ("finish", "steel"))); // arm
        p.Add(B([-0.45, -0.3, 0.3], [0.45, -0.1, 0.62], Darks, false, ("finish", "matte paint"))); // head
        p.Add(GlowBox([-0.42, -0.32, 0.33], [0.42, -0.3, 0.59]));
        return MainsLamp(p, 120);
    }

    private static Part PipeBank()
    {
        var p = Hung("Pipe Bank", "Three pipes, 3.6 m, along a wall under the ceiling, on brackets. Dressing.", color: Rust, mass: 60, health: 200, armour: 3, finish: "steel");
        foreach (var x in new[] { -1.4, 0.0, 1.4 })
        {
            p.Add(B([x - 0.04, -0.52, 0.0], [x + 0.04, 0.06, 0.05], Darks)); // bracket plate
            p.Add(B([x - 0.025, -0.5, 0.05], [x + 0.025, -0.47, 0.26], Darks)); // bracket arm
        }
        // (phase 12) the top pipe lagged, the middle one painted, the bottom one bare; the lagged pipe's flanges are bare steel bands
        foreach (var (y, col, fin) in new[] { (-0.12, Lagged, "lagging"), (-0.27, PipeGreen, "painted steel"), (-0.42, Steel, "steel") })
        {
            p.Add(B([-1.78, y - 0.06, 0.08], [1.78, y + 0.06, 0.2], col, false, ("finish", fin)));
            foreach (var x in new[] { -1.0, 1.0 })
                p.Add(B([x - 0.03, y - 0.08, 0.06], [x + 0.03, y + 0.08, 0.22], fin == "lagging" ? Steel : col, false, ("finish", fin == "lagging" ? "steel" : fin))); // flange
        }
        return p;
    }

    private static Part DuctRun()
    {
        var p = Hung("Duct Run", "A square air duct, 3.6 m, along a wall under the ceiling. Dressing.", color: Galv, mass: 45, health: 150, armour: 2, finish: "aluminium");
        p.Add(B([-1.78, -0.5, 0.06], [1.78, -0.05, 0.56], Galv));
        foreach (var x in new[] { -1.2, -0.4, 0.4, 1.2 })
            p.Add(B([x - 0.03, -0.52, 0.045], [x + 0.03, -0.03, 0.575], "#8e959a")); // seams
        foreach (var x in new[] { -1.0, 1.0 })
            p.Add(B([x - 0.03, -0.53, 0.0], [x + 0.03, 0.06, 0.04], Darks)); // hanger plates
        return p;
    }

    private static Part CableTray()
    {
        var p = Hung("Cable Tray Run", "A ladder tray of cables, 3.6 m, along a wall under the ceiling. Dressing.", color: Steel, mass: 25, health: 120, armour: 2, finish: "steel");
        foreach (var z0 in new[] { 0.03, 0.38 })
            p.Add(B([-1.78, -0.16, z0], [1.78, -0.08, z0 + 0.02], Steel)); // side rails
        for (var i = 0; i < 8; i++)
        {
            var x = -1.575 + i * 0.45;
            p.Add(B([x - 0.015, -0.15, 0.05], [x + 0.015, -0.13, 0.38], Steel)); // rungs
        }
        foreach (var x in new[] { -1.1, 1.1 })
            p.Add(B([x - 0.03, -0.2, 0.0], [x + 0.03, 0.06, 0.03], Darks)); // brackets
        foreach (var (z0, z1, col) in new[] { (0.07, 0.15, Cable), (0.17, 0.24, Orange), (0.26, 0.35, Cable) })
            p.Add(B([-1.77, -0.13, z0], [1.77, -0.07, z1], col, false, ("finish", "rubber")));
        return p;
    }

    private static Part WallRib()
    {
        var p = Hung("Wall Rib", "A frame of the hull's structure standing along a wall, floor to ceiling. Dressing.", color: Darks, mass: 40, health: 200, armour: 4, finish: "dirty steel");
        p.Add(B([-0.15, -1.355, 0.0], [0.15, 1.355, 0.03], Darks));
        p.Add(B([-0.03, -1.35, 0.03], [0.03, 1.35, 0.2], Darks));
        p.Add(B([-0.11, -1.345, 0.2], [0.11, 1.345, 0.23], Darks));
        p.Add(B([-0.02, 0.9, 0.23], [0.02, 1.33, 0.45], Darks)); // knee under the ceiling
        return p;
    }

    private static Part WallSign()
    {
        var p = Hung("Deck Sign", "A stencilled section board with a hazard band. Dressing.", color: "#2f4f6f", mass: 3, health: 30, armour: 1, finish: "painted steel");
        p.Add(B([-0.45, -0.18, 0.0], [0.45, 0.18, 0.03], "#2f4f6f"));
        p.Add(B([-0.4, 0.04, 0.03], [0.4, 0.13, 0.036], "#e8e8e0", true)); // the stencil band
        for (var i = 0; i < 6; i++)
        {
            var x = -0.4 + i * 0.14;
            p.Add(B([x, -0.13, 0.03], [x + 0.07, -0.03, 0.036], i % 2 == 0 ? Yellow : Hazard, true, ("finish", "hazard paint")));
        }
        return p;
    }

    private static Part ToolBoard()
    {
        var p = Hung("Tool Board", "A pegboard of hand tools. Dressing.", color: "#8c7a5a", mass: 12, health: 60, armour: 1);
        p.Add(B([-0.6, -0.4, 0.0], [0.6, 0.4, 0.03], "#8c7a5a", false, ("finish", "wood")));
        foreach (var (x, y0, y1, w, col) in new[] { (-0.45, -0.25, 0.2, 0.04, Darks), (-0.3, -0.3, 0.25, 0.03, "#b8352c"), (-0.12, -0.2, 0.28, 0.05, Darks),
                     (0.08, -0.32, 0.1, 0.06, "#c9a21c"), (0.3, -0.25, 0.25, 0.04, Darks), (0.46, -0.15, 0.3, 0.03, Steel) })
            p.Add(B([x - w / 2, y0, 0.03], [x + w / 2, y1, 0.07], col, true));
        return p;
    }

    private static Part FirePoint()
    {
        var p = Hung("Fire Point", "A red cabinet with an extinguisher and a hose reel. Dressing.", color: Red, mass: 20, health: 60, armour: 1, finish: "gloss paint");
        p.Add(B([-0.22, -0.4, 0.0], [0.22, 0.4, 0.2], Red));
        p.Add(B([-0.18, -0.36, 0.2], [0.18, 0.1, 0.206], "#e8e8e0", true)); // the window
        p.Add(B([-0.06, -0.3, 0.206], [0.06, 0.05, 0.26], "#9a1f1a", true)); // the extinguisher behind it
        return p;
    }

    private static Part JibCrane()
    {
        var p = Hung("Jib Crane", "A wall jib with a trolley and a chain hoist, for a hall's upper wall. Dressing.", color: Yellow, mass: 420, health: 900, armour: 6, seconds: 3.0, finish: "hazard paint");
        p.Add(B([-0.25, -1.0, 0.0], [0.25, 0.08, 0.08], Darks)); // wall plate
        p.Add(B([-0.06, -0.25, 0.08], [0.06, -0.02, 3.6], Yellow)); // the jib's web
        p.Add(B([-0.14, -0.29, 0.1], [0.14, -0.25, 3.58], Yellow)); // its bottom flange
        p.Add(B([-0.14, -0.02, 0.1], [0.14, 0.02, 3.58], Yellow)); // its top flange
        p.Add(B([-0.05, -0.95, 0.08], [0.05, -0.29, 0.75], Yellow)); // the knee brace
        p.Add(B([-0.18, -0.42, 2.3], [0.18, -0.3, 2.7], Darks)); // trolley
        p.Add(B([-0.12, -0.75, 2.38], [0.12, -0.42, 2.62], "#c9a21c")); // hoist
        p.Add(B([-0.015, -1.4, 2.485], [0.015, -0.75, 2.515], Cable, false, ("finish", "steel"))); // chain
        p.Add(B([-0.07, -1.55, 2.45], [0.07, -1.4, 2.55], Darks)); // hook block
        foreach (var x in new[] { -0.2, 0.2 })
            p.Add(B([x - 0.02, -0.4, 3.5], [x + 0.02, -0.29, 3.6], Hazard)); // end stops
        return p;
    }

    private static Part BayMarking()
    {
        var p = new Part("Bay Marking", Small, "Dressing", "A painted floor bay: four yellow lines round a 3.4 m square. Dressing.", role: "frame", tool: "wrench", color: Yellow, group: "Interior");
        p.Set(("finish", "hazard paint"));
        p.Sock("bottom", [0, 0, 0], "-y");
        double h0 = 0.004, h1 = 0.015, a = 1.7, w = 0.1;
        p.Add(B([-a, h0, -a], [a, h1, -a + w], Yellow));
        p.Add(B([-a, h0, a - w], [a, h1, a], Yellow));
        p.Add(B([-a, h0, -a + w + 0.005], [-a + w, h1, a - w - 0.005], Yellow));
        p.Add(B([a - w, h0, -a + w + 0.005], [a, h1, a - w - 0.005], Yellow));
        p.Stats(4, 40, 1, 1.0);
        return p;
    }

    // ---- Trim (phase 12): cheap pieces that make a room read as floor, wall and trim. The builder paints them from the faction's
    // palette (docs/sandbox/INTERIORS.md, Palettes.json) and gives them its wear's material.

    /// <summary>Skirting: a 3.6 m strip of deck plate along a wall's foot, on the wall's low face socket (0.125 m over the floor).</summary>
    private static Part KickPlate()
    {
        var p = Hung("Kick Plate", "A strip of plate along a wall's foot, where boots and carts scuff it. Trim.", color: "#4a4f55", mass: 8, health: 60, armour: 2, finish: "deck plate");
        p.Add(B([-1.78, -0.113, 0.0], [1.78, 0.17, 0.012], "#4a4f55")); // (1.2 cm over the floor's top: no shared plane)
        p.Add(B([-1.78, 0.17, 0.0], [1.78, 0.19, 0.02], "#3a3f45", false, ("finish", "steel"))); // the top lip
        return p;
    }

    /// <summary>A walkway 3.6 m long between two painted edge lines, for a hall's floor: the middle worn where feet go.</summary>
    private static Part LaneMarking()
    {
        var p = new Part("Lane Marking", Small, "Dressing", "Painted walkway lines 3.6 m long, with the scuffed lane between them. Trim.", role: "frame", tool: "wrench", color: Yellow, group: "Interior");
        p.Set(("finish", "hazard paint"));
        p.Sock("bottom", [0, 0, 0], "-y");
        double a = 1.8, w = 0.08, half = 0.6;
        foreach (var s in new[] { -1, 1 })
        {
            double u = s * half, v = s * (half + w);
            p.Add(B([-a, 0.004, Math.Min(u, v)], [a, 0.014, Math.Max(u, v)], Yellow));
        }
        p.Add(B([-a + 0.01, 0.002, -half + 0.006], [a - 0.01, 0.0125, half - 0.006], "#6a6e72", false, ("finish", "worn paint"))); // the scuffed lane
        p.Stats(3, 30, 1, 1.0);
        return p;
    }

    private static List<Part> DressingParts() =>
    [
        WallLight(), HallFloodlight(), PipeBank(), DuctRun(), CableTray(), WallRib(), WallSign(), ToolBoard(), FirePoint(), JibCrane(), BayMarking(),
        KickPlate(), LaneMarking(),
    ];

    public void Make(PartOutput o)
    {
        var all = new List<(string Folder, Func<PyDict> Json)>
        {
            ("", Car().Json),
            ("Capital", HollowChassis().Json),
            ("Capital", HollowChassis(armoured: true).Json),
        };
        foreach (var m in new[] { 4, 8, 12, 16 })
            all.Add(("Walls", PanelWall(m).Json));
        foreach (var f in FurnitureParts().Concat(ColdWarFurniture()))
            all.Add(("Furniture", f.Json));
        foreach (var m in new[] { 8, 16 })
            all.Add(("Floors", FloorPanel(m).Json));
        foreach (var d in DressingParts().Concat(ColdWarDressing())) // (the Ministry's old fittings: InteriorParts.ColdWar.cs)
            all.Add(("Dressing", d.Json));
        // scale.write_parts over both kinds of part (a scale Part and a Building piece)
        o.Own(Out);
        var names = new HashSet<string>();
        foreach (var (folder, json) in all)
        {
            var d = json();
            var name = (string)d["name"]!;
            if (!names.Add(name))
                throw new InvalidOperationException($"interior_parts: part {name} twice");
            o.File(folder.Length > 0 ? $"{Out}/{folder}/{FileName(name)}" : $"{Out}/{FileName(name)}", PyJson.Dumps(d, 2) + "\n");
        }
    }
}
