using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// Conveyor junctions: Constructs/Parts/ConveyorJunctions (docs/sandbox/constructs/production.md, "Conveyors"). A low steel table
/// with a roller deck that pieces cross whole from one conveyor to the next. A Splitter takes one conveyor in at -z and sends
/// its pieces out at +z, +x and -x in turn (round robin, past a cut, too small or full one); a Join takes three in (-z, +x,
/// -x) and sends them out at +z fairly. The ports sit at its edges' middles on the deck's line, so a piece runs on through
/// its middle (ConstructConveyor.LeadIn). Amber posts at the corners; a pale mark under each way out, an amber one under each
/// way in.
/// </summary>
public sealed class ConveyorJunctionParts : IPartGenerator
{
    public string Name => "conveyor_junction_parts";

    private const string Out = "Constructs/Parts/ConveyorJunctions";
    private const string Frame = "#35556b", Plate = "#2c2e30", Roller = "#9aa0a6", Post = "#d9a21c", Mark = "#e8e2d0";

    private static readonly (string Face, double Dx, double Dz)[] Sides = [("-z", 0, -1), ("+z", 0, 1), ("+x", 1, 0), ("-x", -1, 0)];

    /// <summary>A junction n by n small cells (0.25 m), two high, passing pieces up to section by section; <paramref name="split"/>: one in, three out (else three in, one out).</summary>
    private static Part Junction(string name, int n, double section, bool split, string desc)
    {
        var c = Small;
        var p = new Part(name, c, "Extrusion", desc, tool: "welder", color: Frame, group: "Production");
        double x0 = c.Lo(), x1 = c.Hi(n), y0 = c.Lo(), y1 = c.Hi(2), z0 = c.Lo(), z1 = c.Hi(n);
        var top = y1 - 0.12; // the frame's top; the conveyors' line is 5 mm over it (their rollers' tops 6 cm over the line, as the deck's are)
        double cx = (x0 + x1) / 2, cz = (z0 + z1) / 2;
        p.Add(Box([x0, y0, z0], [x1, top, z1]));
        // The deck: a dark plate, and light rollers across it every 0.25 m (their tops 6 cm over the line, a piece's underside 5 mm over them).
        p.Add(Box([x0 + 0.06, top - 0.005, z0 + 0.06], [x1 - 0.06, top + 0.02, z1 - 0.06], Plate, detail: true));
        for (var z = z0 + 0.15; z + 0.05 < z1 - 0.1; z += 0.25)
            p.Add(Box([x0 + 0.13, top + 0.015, z], [x1 - 0.13, top + 0.06, z + 0.05], Roller, detail: true));
        // Amber posts at the corners (clear of the widest conveyor at each edge's middle).
        foreach (var (px, pz) in new[] { (x0, z0), (x1 - 0.12, z0), (x0, z1 - 0.12), (x1 - 0.12, z1 - 0.12) })
            p.Add(Box([px, top - 0.005, pz], [px + 0.12, y1, pz + 0.12], Post, detail: true));
        // The ways in and out: a mark on each side face under its port (pale out, amber in), 4 mm proud.
        var ports = new List<object?>();
        var outs = 0;
        var ins = 0;
        foreach (var (face, dx, dz) in Sides)
        {
            var isIn = split ? face == "-z" : face != "+z";
            double ax = cx + dx * (x1 - x0) / 2, az = cz + dz * (z1 - z0) / 2;
            double[] mn = dz != 0 ? [cx - 0.2, top - 0.09, az - (dz > 0 ? 0.01 : 0.004)] : [ax - (dx > 0 ? 0.01 : 0.004), top - 0.09, cz - 0.2];
            double[] mx = dz != 0 ? [cx + 0.2, top - 0.03, az + (dz > 0 ? 0.004 : 0.01)] : [ax + (dx > 0 ? 0.004 : 0.01), top - 0.03, cz + 0.2];
            p.Add(Box(mn, mx, isIn ? Post : Mark, detail: true));
            var portName = isIn ? (++ins == 1 ? "Piece in" : $"Piece in {ins}") : (++outs == 1 ? "Piece out" : $"Piece out {outs}");
            ports.Add(Kw(("name", portName), ("type", "conveyor"), ("dir", isIn ? "in" : "out"), ("at", Py.L(Py.Round(ax, 3), Py.Round(top + 0.005, 3), Py.Round(az, 3))), ("face", face),
                ("opening", Py.L(section, section))));
        }
        BlockSockets(p, n, 2, n, faces: "y", skip: ["+y"], limit: new() { ["-y"] = (3, 6) });
        var wide = n * c.Cell;
        p.Stats(wide * wide * 40, 300 + 40 * wide * wide, 4);
        p.Set(("ports", ports));
        return p;
    }

    public void Make(PartOutput o) => WriteParts(o, Out,
    [
        ("", Junction("Conveyor Splitter", 6, 1.0, split: true, "Takes pieces off one conveyor and sends them on to up to three, in turn, past any that is cut, too small or full. Pieces up to 1 x 1 m.")),
        ("", Junction("Conveyor Join", 6, 1.0, split: false, "Takes pieces off up to three conveyors, each in its turn, and sends them on along one. Pieces up to 1 x 1 m.")),
        ("", Junction("Large Conveyor Splitter", 10, 2.0, split: true, "Takes pieces off one conveyor and sends them on to up to three, in turn, past any that is cut, too small or full. Pieces up to 2 x 2 m.")),
        ("", Junction("Large Conveyor Join", 10, 2.0, split: false, "Takes pieces off up to three conveyors, each in its turn, and sends them on along one. Pieces up to 2 x 2 m.")),
    ]);
}
