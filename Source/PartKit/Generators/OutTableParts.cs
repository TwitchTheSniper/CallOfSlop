using static CallOfSlop.PartKit.PartKitMath;
using static CallOfSlop.PartKit.MachineLookShort;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The Out Table: Constructs/Parts/Production/OutTable.json (design page drawings 15 and 16). The far end of a bed machine on the block
/// system (MachineBlocks.cs, role "end"): a Panel Former's or a Laser Cutter's finished piece rides out over it, onto a conveyor from its
/// Piece out or onto whatever is butted on its far side. One table is a 2 x 2 m cell; weld them side by side for a wider bed. Its rollers'
/// tops are at <see cref="WorkY"/>, the height every bed machine's work passes at, so a former's table butts straight onto a cutter's bed.
/// What is on a side is drawn only while no cell of its machine is welded there ("seam"), so a row of tables reads as one.
/// </summary>
public sealed class OutTableParts : IPartGenerator
{
    public string Name => "out_table_parts";

    private const string Out = "Constructs/Parts/Production";
    public const double WorkY = 1.0; // the rollers' tops: the bed machines' work height (a Bed Block's lower bed, a cutter's slats)
    private const string Body = "#4a5058";
    private const double E = 0.997; // (the body stops 3 mm short of the cell, so two tables' faces never meet on one plane)

    private static PyDict Seam(PyDict? box, string face) => box!.Update(("seam", face));

    private static Part Table()
    {
        var p = new Part("Out Table", Small, "Extrusion",
            "The far end of a Panel Former's or a Laser Cutter's bed: weld it on the bed's last row, side by side for a wider bed. The finished piece rides out over its rollers, onto a conveyor from its Piece out or onto the next machine butted on.",
            group: "Production", color: Body);
        const double frameTop = 0.8, beamTop = 0.985, r = 0.07, rollY = WorkY - r, beam = 0.1;
        // The frame: a solid box body, the rollers over it between two side beams.
        p.Add(Box([-E, 0.003, -E], [E, frameTop, E]));
        p.Add(L([-E + beam + 0.004, frameTop, -E + 0.03], [E - beam - 0.004, frameTop + 0.014, E - 0.03], MachineLook.Steel, "tread plate"));
        foreach (var (sx, side) in new[] { (1, "+x"), (-1, "-x") })
        {
            double[] Xs(double a, double b) => sx > 0 ? [a, b] : [-b, -a];
            var bx = Xs(E - beam, E - 0.004);
            p.Add(Seam(L([bx[0], frameTop - 0.004, -E + 0.004], [bx[1], beamTop, E - 0.004], MachineLook.Base), side)); // (its side beam: the rollers' bearings)
            var face = sx * E;
            // Bolts along the beam's outer face, an orange band under it (the out side) and an arrow on the band along the way out.
            for (var z = -0.85; z < 0.9; z += 0.34)
            {
                var bt = Xs(E - 0.004, E + 0.012);
                p.Add(Seam(L([bt[0], 0.885, z - 0.02], [bt[1], 0.925, z + 0.02], MachineLook.Steel), side));
            }
            p.Add(Seam(L([sx > 0 ? E : -E - 0.012, 0.6, -E + 0.06], [sx > 0 ? E + 0.012 : -E, 0.72, E - 0.06], MachineLook.Out), side));
            MachineLook.Arrow(p, 0, face + sx * 0.012, sx, 2, 1, 0, 0.66, 0.5, 0.09, MachineLook.Ink);
            p.Boxes[^4].Update(("seam", side));
            p.Boxes[^3].Update(("seam", side));
            p.Boxes[^2].Update(("seam", side));
            p.Boxes[^1].Update(("seam", side));
            // The feet and kick plate.
            p.Add(Seam(L([sx > 0 ? E : -E - 0.014, 0.003, -E + 0.004], [sx > 0 ? E + 0.014 : -E, 0.13, E - 0.004], MachineLook.Base), side));
        }
        foreach (var z in new[] { -0.4, 0.4 })
            foreach (var x in new[] { -0.6, 0.6 })
                p.Add(L([x - 0.012, frameTop + 0.014, z - 0.012], [x + 0.012, frameTop + 0.026, z + 0.012], MachineLook.Base)); // (the deck plate's screws)
        // The rollers: eight across the table, turning while the machine it closes works (drawing 15).
        var cyl = Kw(("shape", "cylinder"), ("segments", 12), ("front", "+x"));
        for (var k = 0; k < 8; k++)
        {
            var z = -E + 0.125 + k * 0.2493;
            p.Add(MachineLook.Work(L([-E + beam + 0.008, rollY - r, z - r], [E - beam - 0.008, rollY + r, z + r], MachineLook.Steel, "steel", shape: new PyDict(cyl)), "spin", "+x"));
        }
        // The ends: a hazard edge along the far end's top, a dark kick plate under each end (drawn while nothing of its machine is welded there).
        p.Add(Seam(MachineLook.D([-E + 0.02, frameTop - 0.12, E], [E - 0.02, frameTop - 0.02, E + 0.012], MachineLook.Accent, "hazard paint"), "+z"));
        p.Add(Seam(L([-E + 0.004, 0.003, E], [E - 0.004, 0.13, E + 0.014], MachineLook.Base), "+z"));
        p.Add(Seam(L([-E + 0.004, 0.003, -E - 0.014], [E - 0.004, 0.13, -E], MachineLook.Base), "-z"));
        // The roller drive on its left side: a gear motor, its coupling turning, a cable run to a junction box, and a maker's plate.
        p.Add(Seam(L([-E - 0.16, 0.22, -0.45], [-E, 0.48, -0.1], MachineLook.Base), "-x"));
        p.Add(Seam(MachineLook.Work(L([-E - 0.12, 0.27, -0.1], [-E - 0.02, 0.43, 0.02], MachineLook.Accent, shape: Kw(("shape", "cylinder"), ("segments", 10), ("front", "+z"))), "spin", "+z")!, "-x"));
        p.Add(Seam(L([-E - 0.15, 0.3, -0.75], [-E - 0.006, 0.36, -0.45], MachineLook.Base), "-x"));
        p.Add(Seam(L([-E - 0.16, 0.2, -0.95], [-E, 0.46, -0.75], MachineLook.Base), "-x"));
        p.Add(Seam(L([-E - 0.014, 0.22, 0.25], [-E, 0.4, 0.55], MachineLook.In), "-x"));
        p.Add(Seam(L([-E - 0.027, 0.29, 0.28], [-E - 0.014, 0.33, 0.52], MachineLook.Ink), "-x"));
        // Its Piece out on the far end, the shared port look (MachineLook.Port, Sean 2026-11-21).
        const double portY = 0.42;
        var before = p.Boxes.Count;
        MachineLook.Port(p, [0, portY, E], 2, 1, 0.2, 0.12, false);
        foreach (var b in p.Boxes.Skip(before))
                b["seam"] = "+z"; // (butted on another machine's bed, the port is under it: not drawn)
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Sock("-z", [0, 0.5, -1], "-z");
        p.Sock("+z", [0, 0.5, 1], "+z");
        p.Sock("+x", [1, 0.5, 0], "+x");
        p.Sock("-x", [-1, 0.5, 0], "-x");
        p.Stats(1600, 900, 6, 10);
        p.Set(("ports", Py.L(Kw(("name", "Piece out"), ("type", "conveyor"), ("dir", "out"), ("at", Py.L(0, portY, E)), ("face", "+z"), ("opening", Py.L(1.8, 0.6))))),
            ("machineBlock", Kw(("role", "end"))), ("centreSockets", false), ("collider", "boxes"));
        return p;
    }

    public void Make(PartOutput o) => WriteParts(o, Out, [("", Table())]);
}
