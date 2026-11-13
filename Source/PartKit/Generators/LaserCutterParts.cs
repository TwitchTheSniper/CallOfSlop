using static CallOfSlop.PartKit.PartKitMath;
using static CallOfSlop.PartKit.MachineLookShort;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The Laser Cutters: Constructs/Parts/LaserCutter (docs/sandbox/constructs/production.md, "The Laser Cutter"; design
/// docs/sandbox/design/laser-cutter.md section 5). A flat slatted bed under a gantry: the blank comes in over the white end at the back,
/// the head runs along the gantry with its lens glowing while it cuts, and the cut part leaves at the orange end. The offcut falls into a
/// hopper on the right of the out end, emptied by chute from its Offcut port; the control face stands on the left, the aisle side. Two sizes,
/// matching the two Panel Formers' panels, and no tiers. Born in the Machine look (look.md).
/// </summary>
public sealed class LaserCutterParts : IPartGenerator
{
    public string Name => "laser_cutter_parts";

    private const string Out = "Constructs/Parts/LaserCutter";
    private const string Body = "#a8365e"; // the Laser Cutters' body colour
    private const string Trim = "#7f2846";

    /// <summary>A cutter's numbers: class, half width and length of the bed, the bed's height, the largest blank (width, length, height), cutting kW, offcut bin, power (W), mass, health.</summary>
    private sealed record Row(Cls C, double Hw, double Len, double Bed, double MaxW, double MaxL, double MaxH, double Kw, int Units, int Power, int Mass, int Health);

    private static readonly Row[] Rows =
    [
        new(Small, 1.1, 3.2, 0.8, 1.8, 2.5, 0.6, 2.4, 200, 2400, 480, 700),
        new(Medium, 2.7, 7.0, 0.9, 4.5, 6.0, 2.0, 9.6, 1200, 9600, 5600, 3800),
    ];

    private static List<object?> Buttons() => Py.L(
        Kw(("action", "orders.queue"), ("at", Py.L(0.02, 0.02, 0.24, 0.12)), ("label", "Queue")),
        Kw(("action", "orders.cancel"), ("at", Py.L(0.25, 0.02, 0.47, 0.12)), ("label", "Cancel")),
        Kw(("action", "orders.clear"), ("at", Py.L(0.48, 0.02, 0.7, 0.12)), ("label", "Clear")),
        Kw(("action", "view.up"), ("at", Py.L(0.72, 0.02, 0.85, 0.12)), ("label", "Up")),
        Kw(("action", "view.down"), ("at", Py.L(0.86, 0.02, 0.98, 0.12)), ("label", "Down")));

    private static Part Cutter(Row r)
    {
        var c = r.C;
        var name = c.Name.Length > 0 ? $"{c.Name} Laser Cutter" : "Laser Cutter";
        var p = new Part(name, c, "Extrusion",
            $"Cuts openings, notches and corners out of a formed panel or a flat extruded piece, by order: blanks up to {Py.G(r.MaxL)} x {Py.G(r.MaxW)} m and {Py.G(r.MaxH)} m tall. The blank comes in by conveyor at the white end; the cut part leaves at the orange end, the offcut drops into its bin. Thicker stock cuts slower. Needs power.",
            role: "storage", group: "Production", color: Body);
        double hw = r.Hw, z0 = -r.Len / 2, z1 = r.Len / 2, bed = r.Bed;
        var s = hw / 1.1; // (the look's scale)
        var col = 0.16 * s; // a gantry column's side
        var beam = 0.2 * Math.Sqrt(s);
        var top = bed + r.MaxH + 0.15 + beam; // (the beam clears the tallest blank)
        // Solid: the bed, the gantry (two columns and a beam) and the offcut hopper on the right of the out end.
        p.Add(Box([-hw, 0, z0], [hw, bed, z1]));
        p.Add(Box([-hw, bed, -col], [-hw + col, top - beam, col], MachineLook.Base)!.Update(("paintable", false)));
        p.Add(Box([hw - col, bed, -col], [hw, top - beam, col], MachineLook.Base)!.Update(("paintable", false)));
        p.Add(Box([-hw, top - beam, -col * 1.2], [hw, top, col * 1.2], MachineLook.Base)!.Update(("paintable", false), ("finish", "tread plate")));
        double hx0 = hw, hx1 = hw + 0.55 * s, hz0 = z1 - 0.8 * s, hy = bed * 0.7, rim = 0.045 * s;
        p.Add(Box([hx0, 0, hz0], [hx1, hy, z1]));
        p.Add(Box([hx0, hy, hz0], [hx0 + rim, hy + 0.12 * s, z1], Trim));
        p.Add(Box([hx1 - rim, hy, hz0], [hx1, hy + 0.12 * s, z1], Trim));
        p.Add(Box([hx0 + rim, hy, hz0], [hx1 - rim, hy + 0.12 * s, hz0 + rim], Trim));
        p.Add(Box([hx0 + rim, hy, z1 - rim], [hx1 - rim, hy + 0.12 * s, z1], Trim));
        var bulk = Kw(("material", "any"), ("capacity", r.Units),
            ("min", Py.L(R(hx0 + rim + 0.005), R(hy + 0.005), R(hz0 + rim + 0.005))), ("max", Py.L(R(hx1 - rim - 0.005), R(hy + 0.12 * s - 0.005), R(z1 - rim - 0.005))));
        // The control board on the left, the aisle side, past the gantry towards the out end.
        double bz0 = col + 0.12, bz1 = bz0 + 1.0;
        p.Add(L([-hw - 0.06, 0.2, bz0 + 0.04], [-hw, 1.0, bz0 + 0.12], MachineLook.Base));
        p.Add(L([-hw - 0.06, 0.2, bz1 - 0.12], [-hw, 1.0, bz1 - 0.04], MachineLook.Base));
        p.Add(Box([-hw - 0.07, 1.0, bz0], [-hw, 1.92, bz1], MachineLook.Base)!.Update(("paintable", false)));
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Sock("top", [0, R(top), 0], "+y");
        p.Stats(r.Mass, r.Health, c == Small ? 6 : c.Armour, c == Small ? 5 : null);
        p.Set(("power", r.Power), ("storage", Kw(("bulk", bulk),
                ("cut", Kw(("kw", r.Kw), ("at", Py.L(0, R(bed + 0.05), R(z1 - (z1 - col) / 2))), ("maxWidth", r.MaxW), ("maxLength", r.MaxL), ("maxHeight", r.MaxH))))),
            ("config", Py.L(
                ExtrusionParts.OrderCount,
                Kw(("key", "ink"), ("type", "colour"), ("label", "Screen colour"), ("group", "Display"), ("default", "#000000"), ("tooltip", "The colour of what its screen shows. Black: its own colours.")))),
            ("ports", Py.L(
                Kw(("name", "Piece in"), ("type", "conveyor"), ("dir", "in"), ("at", Py.L(0, 0.1, R(z0))), ("face", "-z"), ("opening", Py.L(r.MaxW, r.MaxW))),
                Kw(("name", "Offcut"), ("type", "chute"), ("dir", "out"), ("at", Py.L(R(hx1), R(hy * 0.4), R((hz0 + z1) / 2))), ("face", "+x")))),
            ("collider", "boxes"));
        // The screen, its lamp and the button shelf on the board.
        var keepClear = p.Sockets.Select(k => (new[] { Py.D(((List<object?>)k["at"]!)[0]), Py.D(((List<object?>)k["at"]!)[1]), Py.D(((List<object?>)k["at"]!)[2]) }, (string)k["dir"]!)).ToList();
        var (plate, panel, lamp) = PartScreens.Mount(p.Boxes, keepClear, faces: ["-x"], rows: 8, width: 0.84, lamp: true);
        PartScreens.Fit(p.Boxes, plate, most: 200);
        MachineLook.Buttons(p, plate, (string)panel["face"]!);
        if (lamp is not null)
            p.Boxes.Add(lamp);
        panel["touch"] = Buttons();
        p.Set(("panels", Py.L(panel)));
        // ---- The look ----
        MachineLook.Skirt(p, -hw, hw, z0, z1, 0, 0.14 * Math.Sqrt(s), 0.012);
        // The slatted bed: steel slats along it between dark rails; the blank lies on them.
        var slats = c == Small ? 7 : 11;
        var span = 2 * (hw - col - 0.06 * s);
        for (var k = 0; k < slats; k++)
        {
            var x = -span / 2 + span * k / (slats - 1);
            p.Add(L([x - 0.015 * s, bed, z0 + 0.06], [x + 0.015 * s, bed + 0.045, -col - 0.02], MachineLook.Steel, "steel"));
            p.Add(L([x - 0.015 * s, bed, col + 0.02], [x + 0.015 * s, bed + 0.045, z1 - 0.06], MachineLook.Steel, "steel"));
        }
        p.Add(L([-hw, bed, z0], [-hw + 0.07 * s, bed + 0.06, -col - 0.01], MachineLook.Base));
        p.Add(L([-hw, bed, col + 0.01], [-hw + 0.07 * s, bed + 0.06, z1], MachineLook.Base));
        p.Add(L([hw - 0.07 * s, bed, z0], [hw, bed + 0.06, -col - 0.01], MachineLook.Base));
        p.Add(L([hw - 0.07 * s, bed, col + 0.01], [hw, bed + 0.06, hz0 - 0.01], MachineLook.Base));
        // The head on the gantry: a carriage under the beam, a bare steel nozzle and a lens that glows while it cuts; the carriage runs along the beam.
        var travel = 2 * (hw - col) - 0.5 * s;
        double cw = 0.2 * s, cy1 = top - beam - 0.01, cy0 = cy1 - 0.22 * s, cx = -travel / 2;
        p.Add(MachineLook.Work(L([cx - cw, cy0, -col * 0.9], [cx + cw, cy1, col * 0.9], MachineLook.Accent), "stroke", "+x", travel));
        p.Add(MachineLook.Work(L([cx - 0.06 * s, cy0 - 0.2 * s, -0.06 * s], [cx + 0.06 * s, cy0 + 0.004, 0.06 * s], MachineLook.Steel, "steel", shape: Kw(("shape", "cylinder"), ("segments", 10))), "stroke", "+x", travel));
        p.Add(MachineLook.Work(L([cx - 0.035 * s, cy0 - 0.26 * s, -0.035 * s], [cx + 0.035 * s, cy0 - 0.2 * s + 0.004, 0.035 * s], MachineLook.Hot, shape: Kw(("shape", "cylinder"), ("segments", 8))), "stroke", "+x", travel));
        // The beam's line across the bed under the gantry: dull at rest, glowing while it cuts.
        p.Add(MachineLook.Work(L([-hw + col + 0.02, bed, -0.02 * s], [hw - col - 0.02, bed + 0.014, 0.02 * s], MachineLook.Hot), "glow"));
        // The fume hood: a duct along the top of the beam to a stack over the right column.
        p.Add(L([-hw + 0.1 * s, top, -0.1 * s], [hw - 0.1 * s, top + 0.16 * s, 0.1 * s], MachineLook.Base, shape: Kw(("shape", "cylinder"), ("segments", 12), ("front", "+x"))));
        p.Add(L([hw - col + 0.01, top, -col * 0.8], [hw - 0.01, top + 0.5 * s, col * 0.8], Trim, paintable: true));
        // Hazard paint on the columns' feet.
        foreach (var sx in new[] { -1, 1 })
        {
            var x0 = sx > 0 ? hw - col - 0.012 : -hw - 0.012;
            MachineLook.Hazard(p, [x0, bed + 0.065, -col - 0.012], [x0 + col + 0.024, bed + 0.065 + 0.14 * s, col + 0.012]);
        }
        // The in end (white): a band across the bed's back face and an arrow on the bed.
        p.Add(L([-hw, bed - 0.26 * s, z0 - 0.014], [hw, bed - 0.02, z0], MachineLook.In));
        MachineLook.Arrow(p, 1, bed + 0.045, 1, 2, 1, 0, z0 + 0.45 * s, 0.4 * s, 0.35 * s, MachineLook.In);
        // The out end (orange): a band across the bed's front face, an arrow on the out bed, hazard paint under the lip.
        p.Add(L([-hw, bed - 0.26 * s, z1], [hw, bed - 0.02, z1 + 0.014], MachineLook.Out));
        MachineLook.Hazard(p, [-hw, 0.16 * Math.Sqrt(s), z1], [hw, 0.16 * Math.Sqrt(s) + 0.13 * s, z1 + 0.013]);
        MachineLook.Arrow(p, 1, bed + 0.045, 1, 2, 1, 0, z1 - 0.45 * s, 0.5 * s, 0.4 * s, MachineLook.Out);
        // The offcut hopper: a hazard edge on its rim's out face and its chute's mouth.
        MachineLook.Hazard(p, [hx1, hy + 0.02, hz0 + 0.02], [hx1 + 0.013, hy + 0.1 * s, z1 - 0.02]);
        foreach (var m in MachineLook.Mouth([hx1, hy * 0.4, (hz0 + z1) / 2], 0, 1, 0.1 * s, 0.1, false))
            p.Add(m);
        if (c != Small)
        {
            // A bigger cutter is built heavier: braces under the beam and cladding seams on the bed's long sides.
            foreach (var sx in new[] { -1, 1 })
                p.Add(L([sx > 0 ? hw - col - 0.5 : -hw + col, top - beam - 0.5, col * 0.9 + 0.012], [sx > 0 ? hw - col : -hw + col + 0.5, top - beam - 0.012, col * 1.2 - 0.012], MachineLook.Base));
            foreach (var z in new[] { z0 + r.Len * 0.3, z0 + r.Len * 0.55 })
                p.Add(L([hw, 0.16 * Math.Sqrt(s) + 0.01, z - 0.012], [hw + 0.014, bed - 0.02, z + 0.012], Trim, paintable: true));
        }
        return p;
    }

    public void Make(PartOutput o) => WriteParts(o, Out, [.. Rows.Select(r => ("", Cutter(r)))]);
}
