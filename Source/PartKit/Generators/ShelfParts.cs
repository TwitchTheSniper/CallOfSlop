using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The modular shelves and the Smart Terminal: Constructs/Parts/Shelves, and the Smart Depot blueprint (docs/sandbox/CONSTRUCTS.md,
/// "Modular shelves" and "The Smart Terminal"). Was tools/constructs/shelf_parts.py.
///
/// A Shelf Bay is a cabinet (two sides, a back, a top board) with one or two tiers of boards, open at the front, on its class's lattice,
/// so bays snap on each other up, along and back to back. Its room is "rack" slots: a slot takes as many of one part as really fit, in
/// rows and layers, and holds them in place on anything that moves. Items are parts a hand can carry (40 kg at most,
/// ConstructParts.HandCarryLimit), so shelves stop at the medium class: a Large bay (8 m) would stand a person's parts on a floor no one
/// can reach, and physics-only pieces don't go on shelves.
/// </summary>
public sealed class ShelfParts : IPartGenerator
{
    public string Name => "shelf_parts";

    private const string Out = "Constructs/Parts/Shelves";
    private const string BoardC = "#8a6b45", PostC = "#46525e", Brace = "#5a6672", Lip = "#c9a21c";
    private const string Case = "#3f4c5a", Dark = "#2a3036", Screen = "#14323a", Lamp = "#39465a";
    private const double Gap = 0.015; // slots stand this clear of the boxes round them

    /// <summary>
    /// One bay: nx x ny x nz cells of its class, a cabinet of two sides, a back and a top, with tiers boards inside. Each tier has slots
    /// split evenly across it; the bottom tier's slots come first, so a save from before the tiers keeps its stacks where they were.
    /// </summary>
    private static Part Bay(string name, Cls cls, int nx, int ny, int nz, int slots, string desc, int tiers = 1)
    {
        var p = new Part(name, cls, "Shelves", desc, role: "storage", tool: "welder", color: PostC, group: "Storage");
        double x0 = cls.Lo(), x1 = cls.Hi(nx), y0 = cls.Lo(), y1 = cls.Hi(ny), z0 = cls.Lo(), z1 = cls.Hi(nz);
        var small = cls == Small;
        var board = small ? 0.03 : 0.05;
        var side = small ? 0.03 : 0.05; // the side panels
        var back = small ? 0.015 : 0.025; // the back panel
        var inset = small ? 0.01 : 0.02; // boards stand this far behind the sides' front edge, so the sides frame them
        // The carcass: two sides the full height and depth, a back between them, open only at the front.
        p.Add(Box([x0, y0, z0], [x0 + side, y1, z1]));
        p.Add(Box([x1 - side, y0, z0], [x1, y1, z1]));
        p.Add(Box([x0 + side, y0, z0], [x1 - side, y1, z0 + back], Brace));
        double bx0 = x0 + side, bx1 = x1 - side, bz0 = z0 + back, bz1 = z1 - inset;
        // Boards: one at the bottom, one under the top, and one between each pair of tiers.
        var floors = new List<(double Lo, double Hi)>(); // (top of the board under a tier, bottom of the board over it)
        for (var k = 0; k <= tiers; k++)
        {
            var y = y0 + (y1 - y0) * k / tiers;
            var lo0 = k == 0 ? y : (k == tiers ? y - board : y - board / 2);
            var hi0 = lo0 + board;
            p.Add(Box([bx0, lo0, bz0], [bx1, hi0, bz1], BoardC));
            if (k < tiers) // a lip along the board's front that a stack sits behind
                p.Add(Box([bx0, hi0, bz1 - 0.02], [bx1, hi0 + 0.03, bz1 - 0.005], Lip, detail: true));
            floors.Add((lo0, hi0));
        }
        BlockSockets(p, nx, ny, nz, skip: ["+z"], limit: new() { ["-y"] = (4, 2), ["+y"] = (4, 2), ["-x"] = (2, 2), ["+x"] = (2, 2), ["-z"] = (4, 2) });
        double lo = bx0 + Gap, hi = bx1 - Gap;
        var each = (hi - lo) / slots;
        var room = new List<object?>();
        for (var t = 0; t < tiers; t++)
        {
            double floor = floors[t].Hi, roof = floors[t + 1].Lo;
            for (var k = 0; k < slots; k++)
                room.Add(new PyDict
                {
                    ["min"] = Vec(lo + k * each + (k != 0 ? Gap : 0), floor + Gap, bz0 + Gap),
                    ["max"] = Vec(lo + (k + 1) * each - (k < slots - 1 ? Gap : 0), roof - Gap, bz1 - 0.025),
                    ["rack"] = true,
                    ["label"] = "shelf",
                });
        }
        double wide = nx * cls.Cell, deep = nz * cls.Cell, high = ny * cls.Cell;
        var mass = (wide * deep * (small ? 8 : 16)) * (tiers + 1) / 2 + high * (deep + wide) * (small ? 6 : 14);
        p.Stats(mass, 80 + 60 * wide * deep * (small ? 1 : 0.5), 3);
        p.Set(("storage", Kw(("slots", room))));
        return p;
    }

    /// <summary>A cabinet with a screen over a tray: chutes read through Network, and what is pulled lands on the tray.</summary>
    private static Part Terminal()
    {
        var p = new Part("Smart Terminal", Small, "Beacons and recycling", "Lists its chute network's stores. Pulls an item to its tray, a chute or your hands. Needs power.",
            role: "storage", tool: "welder", color: Case, group: "Storage");
        double w = 0.45, d = 0.35;
        p.Add(Box([-w - 0.04, 0, -d - 0.04], [w + 0.04, 0.07, d + 0.04], Dark));
        p.Add(Box([-w, 0.06, -d], [w, 0.3, d])); // the plinth; the tray's floor is its top
        p.Add(Box([-w, 0.3, -d], [w, 1.3, -0.02])); // the cabinet
        p.Add(Box([-w, 0.3, -0.02], [-w + 0.03, 0.76, d])); // the tray's cheeks
        p.Add(Box([w - 0.03, 0.3, -0.02], [w, 0.76, d]));
        p.Add(Box([-w, 0.76, -0.02], [w, 1.3, 0.1])); // the hood the screen stands in
        p.Add(Box([-0.4, 0.82, 0.094], [0.4, 1.26, 0.106], Screen, detail: true)); // what its panel stands on
        p.Add(Box([-0.34, 0.31, 0.32], [0.34, 0.34, 0.345], "#c9a21c", detail: true));
        p.Add(Box([-0.05, 1.3, -0.1], [0.05, 1.38, 0.0], Lamp, detail: true)); // the lamp: the last box, lit while it works
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Sock("back", [0, 0.6, -d], "-z");
        p.Sock("+x", [w, 0.6, -0.15], "+x");
        p.Sock("-x", [-w, 0.6, -0.15], "-x");
        p.Stats(70, 200, 3, seconds: 4.0);
        p.Set(("power", 60), ("onBreak", "disable"));
        p.Set(("panels", Py.L(Kw(("source", "terminal"), ("at", Py.L(0, 1.04, 0.112)), ("size", Py.L(0.78, 0.42)), ("face", "+z"), ("up", "+y"), ("rows", 6), ("range", 16)))));
        p.Set(("ports", Py.L(
            Kw(("name", "Network"), ("type", "chute"), ("dir", "both"), ("at", Py.L(-0.25, 1.3, -0.15)), ("face", "+y")),
            Kw(("name", "Chute out"), ("type", "chute"), ("dir", "out"), ("at", Py.L(w, 0.5, 0.15)), ("face", "+x")))));
        p.Set(("storage", Kw(("slots", Py.L(Kw(("min", Py.L(-0.41, 0.315, 0.0)), ("max", Py.L(0.41, 0.745, 0.315)), ("rack", true), ("label", "tray")))), ("terminal", true))));
        return p;
    }

    private static string Say(int slots, int tiers)
    {
        var n = slots * tiers;
        return $"{n} shelf slot{(n > 1 ? "s" : "")}" + (tiers > 1 ? $" on {tiers} tiers" : "");
    }

    private static List<(string, Part)> Everything()
    {
        (string, int, int, int, int, int)[] small =
        [
            ("Shelf Bay 0.5", 2, 2, 2, 1, 1),
            ("Shelf Bay 1", 4, 2, 2, 2, 1),
            ("Tall Shelf Bay 1", 4, 4, 2, 2, 2),
        ];
        (string, int, int, int, int, int)[] medium =
        [
            ("Medium Shelf Bay 1", 1, 1, 1, 1, 1),
            ("Medium Shelf Bay 2", 2, 1, 1, 2, 1),
            ("Medium Shelf Bay 4", 4, 2, 1, 4, 2),
        ];
        var parts = new List<(string, Part)>();
        foreach (var (name, nx, ny, nz, slots, tiers) in small)
            parts.Add(("Small", Bay(name, Small, nx, ny, nz, slots, $"Snaps to more bays. {Say(slots, tiers)}: parts stack as they fit.", tiers)));
        foreach (var (name, nx, ny, nz, slots, tiers) in medium)
            parts.Add(("Medium", Bay(name, Medium, nx, ny, nz, slots, $"Rack for a store room. {Say(slots, tiers)}: parts stack as they fit.", tiers)));
        parts.Add(("", Terminal()));
        return parts;
    }

    /// <summary>A terminal reading two shelves and a bin, and a Chest its Chute out feeds.</summary>
    private static PyDict Depot() => new()
    {
        ["format"] = "call-of-slop-construct-blueprint/1",
        ["name"] = "Smart Depot",
        ["description"] = "A Smart Terminal reading stacked shelves and a bin, feeding a Chest.",
        ["steps"] = Py.L(
            Kw(("part", "Foundation")),
            Kw(("part", "Smart Terminal"), ("on", 0), ("socket", "top03"), ("by", "bottom")), // at the front edge: nothing stands or runs before its screen
            Kw(("part", "Battery"), ("on", 0), ("socket", "top30"), ("by", "-y")),
            Kw(("part", "Shelf Bay 1"), ("on", 0), ("socket", "top00"), ("by", "-y.0.0")),
            Kw(("part", "Shelf Bay 1"), ("on", 3), ("socket", "+y.0.0"), ("by", "-y.0.0")),
            Kw(("part", "Materials Bin"), ("on", 0), ("socket", "top31"), ("by", "bottom")),
            Kw(("part", "Chest"), ("on", 0), ("socket", "top33"), ("by", "bottom"))),
        ["links"] = Py.L(
            Kw(("type", "chute"), ("from", 1), ("to", 3)),
            Kw(("type", "chute"), ("from", 1), ("to", 4)),
            Kw(("type", "chute"), ("from", 1), ("to", 5)),
            Kw(("type", "chute"), ("from", 1), ("fromPort", "Chute out"), ("to", 6))),
    };

    public void Make(PartOutput o)
    {
        WriteParts(o, Out, Everything());
        o.File("Constructs/Blueprints/SmartDepot.json", PyJson.Dumps(Depot(), 2) + "\n");
    }
}
