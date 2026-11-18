using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// Ship batteries (Karl, 2026-11-18: "change the batteries into ship batteries, in various sizes", sci-fi like the Space Suit Locker):
/// the circuit's store for a ship, in four sizes. Ship Battery XS is a thick block for a pod's wall or floor (the Shuttle, the Limpet),
/// S and M are wall cabinets, and the Ship Battery Bank is a row of three tall cells on one plinth, as grid storage stands (the Zero Day).
/// Each is a steel carcass with a window onto its cell modules (their tops lit blue), an orange stripe over the window as the locker
/// has, a charge bar of lamps down its edge, a hazard band at its foot and two terminals on its lid. Writes Constructs/Parts/ShipPower.
/// Frame: the back on +z (the "wall" socket, as the locker's), the front -z, the foot at y 0.
/// </summary>
public sealed class ShipBatteryParts : IPartGenerator
{
    public string Name => "ship_batteries";

    private const string Out = "Constructs/Parts/ShipPower";
    private const string Section = "Ship fittings", Group = "Ship";
    private const string Steel = "#5c6166", Frame = "#2c3034", Dark = "#1f2226", Orange = "#d8642a", Hazard = "#d8a628", CellGlow = "#5ac8ff",
        Charge = "#40e070", ChargeOff = "#24402c", Copper = "#b87333", Red = "#c03028";

    /// <summary>A size: its name, width, height, depth, kJ held, mass, health, and the cells behind each window (columns, rows).</summary>
    private sealed record Size(string Name, double W, double H, double D, double Kj, double Mass, double Health, int Cols, int Rows, int Modules = 1);

    private static readonly Size[] Sizes =
    [
        new("Ship Battery XS", 0.5, 0.6, 0.32, 600, 45, 150, 2, 2),
        new("Ship Battery S", 0.8, 1.2, 0.36, 2000, 130, 300, 3, 3),
        new("Ship Battery M", 0.8, 2.0, 0.5, 6000, 320, 600, 3, 5),
        new("Ship Battery Bank", 2.4, 2.0, 0.8, 24000, 600, 2000, 3, 5, Modules: 3),
    ];

    private static PyDict F(string finish) => Kw(("finish", finish));
    private static PyDict Keep(string finish) => Kw(("finish", finish), ("paintable", false));
    private static PyDict Lit() => Kw(("glow", true), ("paintable", false));

    private static void D(Part p, double x0, double y0, double z0, double x1, double y1, double z1, string col, PyDict shape) =>
        p.Add(Box([x0, y0, z0], [x1, y1, z1], col, detail: true, shape: shape));

    private static Part Battery(Size s)
    {
        var (hw, h, hd) = (s.W / 2, s.H, s.D / 2);
        var desc = s.Modules > 1
            ? $"A ship's battery bank: three tall cells on one plinth, {Py.G(s.Kj / 1000)} MJ for the circuit it is on (the lamps, the logic, the starter, and what the capacitors refill from). Weld it to a deck by its foot."
            : $"A ship battery: {Py.G(s.Kj / 1000)} MJ for the circuit it is on (the lamps, the logic, the starter, and what the capacitors refill from). Hangs on a wall by its back or stands on a deck.";
        var p = new Part(s.Name, Small, Section, desc, role: "battery", tool: "wrench", material: "steel", color: Steel, group: Group);
        var plinth = Math.Min(0.12, h * 0.15);
        var lid = Math.Min(0.1, h * 0.12);
        const double Wall = 0.05, Back = 0.06;
        // The carcass, solid: the plinth, the lid, the back and the two sides (the locker's build).
        p.Add(Box([-hw, 0, -hd], [hw, plinth, hd - Back], Frame, shape: F("dirty steel")));
        p.Add(Box([-hw, h - lid, -hd], [hw, h, hd - Back], Steel, shape: F("painted steel")));
        p.Add(Box([-hw, 0, hd - Back], [hw, h, hd], Steel, shape: F("painted steel")));
        foreach (var sx in new[] { -1.0, 1.0 })
            p.Add(Box([sx > 0 ? hw - Wall : -hw, plinth, -hd], [sx > 0 ? hw : -hw + Wall, h - lid, hd - Back], Steel, shape: F("painted steel")));
        // Each module: a louvred panel low down with its charge bar, a window over its cells above it, a mullion between modules.
        var inner = s.W - 2 * Wall;
        var mullion = s.Modules > 1 ? 0.06 : 0;
        var mw = (inner - mullion * (s.Modules - 1)) / s.Modules;
        var panel = s.H >= 1 ? Math.Round(h * 0.2, 3) : 0; // (the XS has no panel: its charge bar is on its lid)
        for (var m = 0; m < s.Modules; m++)
        {
            var x0 = -hw + Wall + m * (mw + mullion);
            var x1 = x0 + mw;
            if (m > 0)
                p.Add(Box([x0 - mullion, plinth, -hd], [x0, h - lid, hd - Back], Steel, shape: F("painted steel")));
            var wy0 = plinth + panel;
            if (panel > 0)
            {
                p.Add(Box([x0, plinth, -hd + 0.02], [x1, wy0, hd - Back], Frame, shape: F("painted plating")));
                for (var k = 0; k < 3; k++)
                    D(p, x0 + 0.05, plinth + 0.04 + k * 0.05, -hd + 0.004, x1 - 0.05, plinth + 0.065 + k * 0.05, -hd + 0.02, Dark, Keep("steel")); // (louvres)
                var lw = (mw - 0.1) / 5;
                var ly = wy0 - 0.07;
                for (var k = 0; k < 5; k++)
                    D(p, x0 + 0.05 + k * lw + 0.008, ly, -hd + 0.002, x0 + 0.05 + (k + 1) * lw - 0.008, ly + 0.035, -hd + 0.02, k < 4 ? Charge : ChargeOff, k < 4 ? Lit() : Keep("plastic")); // (the charge bar: four of five lit)
            }
            // The cells: dark blocks standing in rows behind the glass, each lit across its top.
            var cy0 = wy0 + 0.03;
            var cy1 = h - lid - 0.03;
            var pitchX = (mw - 0.04) / s.Cols;
            var pitchY = (cy1 - cy0) / s.Rows;
            for (var i = 0; i < s.Cols; i++)
                for (var j = 0; j < s.Rows; j++)
                {
                    var cx0 = x0 + 0.02 + pitchX * i + 0.012;
                    var cx1 = cx0 + pitchX - 0.024;
                    var y0 = cy0 + pitchY * j + 0.008;
                    var y1 = y0 + pitchY - 0.016;
                    D(p, cx0, y0, -hd + 0.07, cx1, y1 - 0.035, hd - Back - 0.006, "#39424c", Keep("plastic"));
                    D(p, cx0 + 0.006, y1 - 0.035, -hd + 0.076, cx1 - 0.006, y1, hd - Back - 0.012, CellGlow, Lit());
                    D(p, cx0 + pitchX * 0.3, y0 + 0.02, -hd + 0.054, cx0 + pitchX * 0.42, y0 + 0.05, -hd + 0.07, Orange, Keep("plastic")); // (its tag)
                }
            // The window, recessed a little behind the frame's front.
            p.Add(Box([x0, wy0, -hd + 0.012], [x1, h - lid, -hd + 0.03], "#9fc8d8", glass: true));
        }
        // The lid's front: a lit read-out on the left, the orange stripe to its right (the locker's).
        var rw = Math.Min(0.3, s.W * 0.35);
        D(p, -hw + 0.04, h - lid * 0.8, -hd - 0.018, -hw + 0.04 + rw, h - lid * 0.2, -hd, Dark, Keep("plastic"));
        D(p, -hw + 0.055, h - lid * 0.7, -hd - 0.026, -hw + 0.025 + rw, h - lid * 0.3, -hd - 0.01, CellGlow, Lit());
        D(p, -hw + 0.06 + rw, h - lid * 0.65, -hd - 0.016, hw - 0.04, h - lid * 0.35, -hd, Orange, Keep("worn paint"));
        if (panel == 0)
            for (var k = 0; k < 5; k++)
                D(p, hw - 0.04 - (5 - k) * 0.03, h + 0.003, -hd + 0.02, hw - 0.04 - (4 - k) * 0.03 - 0.006, h + 0.02, -hd + 0.05, k < 4 ? Charge : ChargeOff, k < 4 ? Lit() : Keep("plastic")); // (the XS's charge bar, on its lid)
        // A blue edge light down each front corner.
        foreach (var sx in new[] { -1.0, 1.0 })
            D(p, sx > 0 ? hw - 0.034 : -hw + 0.012, plinth + 0.03, -hd - 0.016, sx > 0 ? hw - 0.012 : -hw + 0.034, h - lid - 0.03, -hd, "#3a8fe0", Lit());
        // The hazard band at its foot.
        var bands = Math.Max(2, (int)(s.W / 0.12));
        var bw = (s.W - 0.08) / bands;
        for (var i = 0; i < bands; i += 2)
            D(p, -hw + 0.04 + i * bw, plinth * 0.25, -hd - 0.016, -hw + 0.04 + (i + 1) * bw, plinth * 0.75, -hd, Hazard, Keep("hazard paint"));
        // The terminals on its lid: red and black posts with copper caps, and a cable trunk into the wall behind.
        var k1 = Math.Min(1, s.W / 0.8); // (smaller posts and trunk on the XS)
        foreach (var (tx, col) in new[] { (-hw * 0.55, Red), (hw * 0.2, Dark) })
        {
            D(p, tx - 0.03 * k1, h, -0.03 * k1, tx + 0.03 * k1, h + 0.04 * k1, 0.03 * k1, col, Keep("plastic"));
            D(p, tx - 0.018 * k1, h + 0.04 * k1, -0.018 * k1, tx + 0.018 * k1, h + 0.06 * k1, 0.018 * k1, Copper, Keep("copper"));
        }
        D(p, hw * 0.6 - 0.05 * k1, h, 0.0, hw * 0.6 + 0.05 * k1, h + 0.07 * k1, hd, Frame, Keep("dirty steel"));
        // Pressed ribs and vent slats up each side.
        foreach (var sx in new[] { -1.0, 1.0 })
        {
            D(p, sx > 0 ? hw : -hw - 0.016, plinth + 0.05, -hd + 0.04, sx > 0 ? hw + 0.016 : -hw, h - lid - 0.05, -hd + 0.07, Steel, Keep("painted steel"));
            if (s.H >= 1)
                for (var k = 0; k < 4; k++)
                {
                    var y = h * 0.55 + k * 0.06;
                    D(p, sx > 0 ? hw : -hw - 0.016, y, -hd * 0.3, sx > 0 ? hw + 0.016 : -hw, y + 0.025, hd * 0.5, Frame, Keep("dirty steel"));
                }
        }
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Sock("wall", [0, Math.Round(Math.Min(0.125, h / 2), 3), hd], "+z");
        p.Sock("top", [0, h, 0], "+y");
        p.Sock("+x", [hw, Math.Round(h / 2, 2), 0], "+x");
        p.Sock("-x", [-hw, Math.Round(h / 2, 2), 0], "-x");
        p.Set(("battery", Kw(("capacity", s.Kj))),
            ("ports", Py.L(
                Kw(("name", "Power out"), ("type", "power"), ("dir", "out"), ("at", new object[] { hw, Math.Round(h / 2, 2), 0 }), ("face", "+x")),
                Kw(("name", "Power in"), ("type", "power"), ("dir", "in"), ("noEffect", true), ("at", new object[] { -hw, Math.Round(h / 2, 2), 0 }), ("face", "-x")))));
        return p.Stats(s.Mass, s.Health, 4, s.Modules > 1 ? 6.0 : 2.0);
    }

    public void Make(PartOutput o)
    {
        o.Own(Out);
        foreach (var s in Sizes)
        {
            var d = Battery(s).Json();
            o.File($"{Out}/{FileName((string)d["name"]!)}", PyJson.Dumps(d, 2) + "\n");
        }
    }
}
