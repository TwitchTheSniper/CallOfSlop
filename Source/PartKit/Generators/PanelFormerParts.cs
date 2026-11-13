using static CallOfSlop.PartKit.PartKitMath;
using static CallOfSlop.PartKit.MachineLookShort;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The Panel Formers: Constructs/Parts/PanelFormer (docs/sandbox/constructs/production.md, "The Panel Former"). A wide flat bed with a
/// bridge over its middle: sheet comes in over a roller bank at the back (the white end), a forming head strokes under the bridge, and the
/// panel leaves over the out bed at the front (the orange end). Loose stock goes into a hopper tower on its right; the control face stands
/// on its left, the aisle side. Born in the Machine look (look.md): three tones, bare steel working parts, work boxes that move.
/// </summary>
public sealed class PanelFormerParts : IPartGenerator
{
    public string Name => "panel_former_parts";

    private const string Out = "Constructs/Parts/PanelFormer";
    private const string Green = "#5d7f3c"; // the Panel Formers' body colour
    private const string Trim = "#46602d";

    /// <summary>A former's numbers: its class, half width and length of the bed, the bed's and the bridge's heights, the largest panel (width, length), speed, bin, power, mass, health.</summary>
    private sealed record Row(Cls C, double Hw, double Len, double Bed, double Bridge, double MaxW, double MaxL, double Speed, int Units, int Power, int Mass, int Health);

    private static readonly Row[] Rows =
    [
        new(Small, 0.95, 3.0, 0.8, 2.3, 1.8, 2.5, 1, 600, 900, 520, 800),
        new(Medium, 2.5, 7.0, 0.9, 3.6, 4.5, 6.0, 2, 4000, 3600, 6400, 4200),
    ];

    private static List<object?> Buttons() => Py.L(
        Kw(("action", "orders.queue"), ("at", Py.L(0.02, 0.02, 0.2, 0.12)), ("label", "Queue")),
        Kw(("action", "orders.cancel"), ("at", Py.L(0.21, 0.02, 0.39, 0.12)), ("label", "Cancel")),
        Kw(("action", "orders.clear"), ("at", Py.L(0.4, 0.02, 0.56, 0.12)), ("label", "Clear")),
        Kw(("action", "machine.recipe"), ("at", Py.L(0.57, 0.02, 0.75, 0.12)), ("label", "Preset")), // (the preset its Queue orders, from the recipe picker)
        Kw(("action", "view.up"), ("at", Py.L(0.76, 0.02, 0.87, 0.12)), ("label", "Up")),
        Kw(("action", "view.down"), ("at", Py.L(0.88, 0.02, 0.98, 0.12)), ("label", "Down")));

    private static Part Former(Row r)
    {
        var c = r.C;
        var name = c.Name.Length > 0 ? $"{c.Name} Panel Former" : "Panel Former";
        var p = new Part(name, c, "Extrusion",
            $"Forms body panels from sheet, by order: bends, crowns and lofted hull parts up to {Py.G(r.MaxL)} x {Py.G(r.MaxW)} m. Sheet comes in by conveyor at the white end or loose into its hopper; the panel leaves at the orange end. Needs power.",
            role: "storage", group: "Production", color: Green);
        double hw = r.Hw, z0 = -r.Len / 2, z1 = r.Len / 2, bed = r.Bed, top = r.Bridge;
        var s = hw / 0.95; // (the look's scale)
        var col = 0.18 * s; // a bridge column's side
        var beam = 0.3 * Math.Sqrt(s);
        // Solid: the bed, the bridge (two columns and a beam) and the hopper tower on the right of the in end.
        p.Add(Box([-hw, 0, z0], [hw, bed, z1]));
        p.Add(Box([-hw, bed, -col], [-hw + col, top - beam, col], MachineLook.Base)!.Update(("paintable", false)));
        p.Add(Box([hw - col, bed, -col], [hw, top - beam, col], MachineLook.Base)!.Update(("paintable", false)));
        p.Add(Box([-hw, top - beam, -col * 1.3], [hw, top, col * 1.3], MachineLook.Base)!.Update(("paintable", false), ("finish", "tread plate")));
        double hx0 = hw, hx1 = hw + 0.5 * s, hz1 = z0 + 0.7 * s, hy = bed + 0.3 * s, rim = 0.045 * s;
        p.Add(Box([hx0, 0, z0], [hx1, hy, hz1]));
        p.Add(Box([hx0, hy, z0], [hx0 + rim, hy + 0.15 * s, hz1], Trim));
        p.Add(Box([hx1 - rim, hy, z0], [hx1, hy + 0.15 * s, hz1], Trim));
        p.Add(Box([hx0 + rim, hy, z0], [hx1 - rim, hy + 0.15 * s, z0 + rim], Trim));
        p.Add(Box([hx0 + rim, hy, hz1 - rim], [hx1 - rim, hy + 0.15 * s, hz1], Trim));
        var bulk = Kw(("material", "any"), ("capacity", r.Units),
            ("min", Py.L(R(hx0 + rim + 0.005), R(hy + 0.005), R(z0 + rim + 0.005))), ("max", Py.L(R(hx1 - rim - 0.005), R(hy + 0.15 * s - 0.005), R(hz1 - rim - 0.005))));
        // The control board on the left, the aisle side: it stands past the bridge towards the out end, the screen at a standing eye.
        double bz0 = col + 0.12, bz1 = bz0 + 1.0;
        p.Add(L([-hw - 0.06, 0.2, bz0 + 0.04], [-hw, 1.0, bz0 + 0.12], MachineLook.Base));
        p.Add(L([-hw - 0.06, 0.2, bz1 - 0.12], [-hw, 1.0, bz1 - 0.04], MachineLook.Base));
        p.Add(Box([-hw - 0.07, 1.0, bz0], [-hw, 1.92, bz1], MachineLook.Base)!.Update(("paintable", false))); // (solid, not a look box: the crosshair finds its screen's buttons on it)
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Sock("top", [0, R(top), 0], "+y");
        p.Stats(r.Mass, r.Health, c == Small ? 6 : c.Armour, c == Small ? 5 : null);
        p.Set(("power", r.Power), ("storage", Kw(("bulk", bulk),
                ("panel", Kw(("speed", r.Speed), ("at", Py.L(0, R(bed + 0.03), R(z1 - (z1 - col) / 2))), ("maxWidth", r.MaxW), ("maxLength", r.MaxL))))),
            ("config", Py.L(
                Kw(("key", "recipe"), ("label", "Preset"), ("group", "Orders"), ("options", Py.L([.. HullLoft.PanelPresets.Select(x => (object?)x.Label)])), ("default", 0),
                    ("tooltip", "The panel its Queue button orders, at the preset's own size, in what its bin holds. The Preset button on its screen opens a picker of them.")),
                ExtrusionParts.OrderCount,
                Kw(("key", "ink"), ("type", "colour"), ("label", "Screen colour"), ("group", "Display"), ("default", "#000000"), ("tooltip", "The colour of what its screen shows. Black: its own colours.")))),
            ("ports", Py.L(
                Kw(("name", "Piece in"), ("type", "conveyor"), ("dir", "in"), ("at", Py.L(0, 0.1, R(z0))), ("face", "-z"), ("opening", Py.L(r.MaxW, r.MaxW))))),
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
        // The bed's work top: bare steel between the rails, dark rails each side.
        p.Add(L([-hw + 0.1 * s, bed, z0 + 0.03], [hw - 0.1 * s, bed + 0.014, z1 - 0.03], MachineLook.Steel, "steel"));
        p.Add(L([-hw, bed, z0], [-hw + 0.085 * s, bed + 0.06 * s, -col - 0.01], MachineLook.Base));
        p.Add(L([-hw, bed, col + 0.01], [-hw + 0.085 * s, bed + 0.06 * s, z1], MachineLook.Base));
        p.Add(L([hw - 0.085 * s, bed, hz1 + 0.01], [hw, bed + 0.06 * s, -col - 0.01], MachineLook.Base));
        p.Add(L([hw - 0.085 * s, bed, col + 0.01], [hw, bed + 0.06 * s, z1], MachineLook.Base));
        // The roller bank at the in end: lower rollers in the bed and pinch rollers over them, in dark cheeks; they turn while it works.
        var rr = 0.055 * s;
        var n = c == Small ? 3 : 5;
        var pitch = (-col - 0.25 * s - (z0 + 0.2 * s)) / (n - 1);
        var cyl = Kw(("shape", "cylinder"), ("segments", 12), ("front", "+x"));
        for (var k = 0; k < n; k++)
        {
            var z = z0 + 0.2 * s + k * pitch;
            p.Add(MachineLook.Work(L([-hw + 0.12 * s, bed + 0.018, z - rr], [hw - 0.12 * s, bed + 0.018 + 2 * rr, z + rr], MachineLook.Steel, "steel", shape: new PyDict(cyl)), "spin", "+x"));
            if (k % 2 == 0)
            {
                p.Add(MachineLook.Work(L([-hw + 0.14 * s, bed + 0.03 + 2 * rr, z - rr * 0.8], [hw - 0.14 * s, bed + 0.03 + 3.6 * rr, z + rr * 0.8], MachineLook.Accent, shape: new PyDict(cyl)), "spin", "+x"));
                foreach (var sx in c == Small ? [] : new[] { -1, 1 }) // (the pinch rollers' cheeks: the small former does without, for its look budget)
                    p.Add(L([sx > 0 ? hw - 0.14 * s : -hw + 0.09 * s, bed + 0.06 * s + 0.004, z - rr * 1.3], [sx > 0 ? hw - 0.09 * s : -hw + 0.14 * s, bed + 0.05 + 4 * rr, z + rr * 1.3], MachineLook.Base));
            }
        }
        // The forming head under the bridge: a steel block on two rods, a hazard edge under it; it strokes while it works.
        double hy1 = top - beam - 0.12 * s, hy0 = hy1 - 0.3 * s, travel = Math.Max(0.1, hy0 - (bed + 0.16 * s + 0.2 * s));
        double headHw = hw - col - 0.1 * s, headD = col * 0.8;
        p.Add(MachineLook.Work(L([-headHw, hy0, -headD], [headHw, hy1, headD], MachineLook.Steel, "steel"), "stroke", "-y", travel));
        p.Add(MachineLook.Work(L([-headHw - 0.01, hy0 - 0.05 * s, -headD - 0.01], [headHw + 0.01, hy0 + 0.012, headD + 0.01], MachineLook.Accent, "hazard paint"), "stroke", "-y", travel));
        foreach (var sx in new[] { -1, 1 })
            p.Add(L([sx * headHw * 0.6 - 0.04 * s, hy1 - 0.02, -0.04 * s], [sx * headHw * 0.6 + 0.04 * s, top - beam + 0.01, 0.04 * s], MachineLook.Steel, "steel", shape: Kw(("shape", "cylinder"), ("segments", 10))));
        // The bridge: an accent cylinder cap each end of the beam (the rams' housings) and hazard paint on the columns' feet.
        foreach (var sx in new[] { -1, 1 })
        {
            p.Add(L([sx * headHw * 0.6 - 0.09 * s, top, -0.09 * s], [sx * headHw * 0.6 + 0.09 * s, top + 0.22 * s, 0.09 * s], MachineLook.Base, shape: Kw(("shape", "cylinder"), ("segments", 12))));
            var x0 = sx > 0 ? hw - col - 0.012 : -hw - 0.012;
            MachineLook.Hazard(p, [x0, bed + 0.065 * s, -col - 0.012], [x0 + col + 0.024, bed + 0.065 * s + 0.14 * s, col + 0.012]);
        }
        // The in end (white): a band across the bed's back face, and the hopper's rim and arrow down.
        p.Add(L([-hw, bed - 0.26 * s, z0 - 0.014], [hw, bed - 0.02, z0], MachineLook.In));
        p.Add(L([hx0 - 0.004, hy + 0.15 * s, z0 - 0.006], [hx1 + 0.006, hy + 0.15 * s + 0.05 * s, z0 + rim], MachineLook.In));
        p.Add(L([hx1 - rim, hy + 0.15 * s, z0 + rim], [hx1 + 0.006, hy + 0.15 * s + 0.05 * s, hz1 + 0.006], MachineLook.In));
        p.Add(L([hx0 - 0.004, hy + 0.15 * s, hz1 - rim], [hx1 - rim, hy + 0.15 * s + 0.05 * s, hz1 + 0.006], MachineLook.In));
        MachineLook.Arrow(p, 0, hx1, 1, 1, -1, hy - 0.3 * s, (z0 + hz1) / 2, 0.3 * s, 0.25 * s, MachineLook.In);
        MachineLook.Hazard(p, [hx0 + 0.02, 0.02, hz1], [hx1 - 0.02, 0.14 * s, hz1 + 0.014]);
        // The out end (orange): a band across the bed's front face, an arrow on the out bed, hazard paint under the lip.
        p.Add(L([-hw, bed - 0.26 * s, z1], [hw, bed - 0.02, z1 + 0.014], MachineLook.Out));
        MachineLook.Hazard(p, [-hw, 0.16 * Math.Sqrt(s), z1], [hw, 0.16 * Math.Sqrt(s) + 0.13 * s, z1 + 0.013]);
        MachineLook.Arrow(p, 1, bed + 0.014, 1, 2, 1, 0, z1 - 0.45 * s, 0.5 * s, 0.4 * s, MachineLook.Out);
        // The drive on the right, past the bridge: a dark gearbox and a pulley that turns.
        p.Add(L([hw, 0.2, col + 0.2 * s], [hw + 0.12 * s, bed - 0.12, col + 0.75 * s], MachineLook.Base));
        p.Add(MachineLook.Work(L([hw + 0.12 * s, bed * 0.3, col + 0.3 * s], [hw + 0.12 * s + 0.045 * s, bed * 0.3 + 0.34 * s, col + 0.64 * s], MachineLook.Accent, shape: new PyDict(cyl)), "spin", "+x"));
        // Cladding seams on the bed's long sides (the body's own tone: they take its paint).
        foreach (var z in c == Small ? [] : new[] { z0 + r.Len * 0.3, z1 - r.Len * 0.22 })
            p.Add(L([hw, 0.16 * Math.Sqrt(s) + 0.01, z - 0.012], [hw + 0.014, bed - 0.02, z + 0.012], Trim, paintable: true));
        if (c != Small)
            p.Add(L([-hw - 0.014, 0.16 * Math.Sqrt(s) + 0.01, z0 + r.Len * 0.25 - 0.012], [-hw, bed - 0.02, z0 + r.Len * 0.25 + 0.012], Trim, paintable: true));
        if (c != Small)
        {
            // A bigger former is built heavier: guard rails along the out bed and braces under the bridge.
            foreach (var sx in new[] { -1, 1 })
            {
                var x = sx * (hw - 0.04);
                p.Add(L([x - 0.03, bed + 0.06 * s + 0.004, col + 0.3], [x + 0.03, bed + 0.9, col + 0.36], MachineLook.Base));
                p.Add(L([x - 0.03, bed + 0.06 * s + 0.004, z1 - 0.36], [x + 0.03, bed + 0.9, z1 - 0.3], MachineLook.Base));
                p.Add(L([x - 0.025, bed + 0.9, col + 0.3], [x + 0.025, bed + 0.96, z1 - 0.3], MachineLook.Accent));
                p.Add(L([sx > 0 ? hw - col - 0.5 : -hw + col, top - beam - 0.5, -0.06], [sx > 0 ? hw - col : -hw + col + 0.5, top - beam - 0.012, 0.06], MachineLook.Base));
            }
        }
        return p;
    }

    public void Make(PartOutput o) => WriteParts(o, Out, [.. Rows.Select(r => ("", Former(r)))]);
}
