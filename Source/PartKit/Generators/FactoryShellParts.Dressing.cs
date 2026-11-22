using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The mill's dressing (the Zero Day works' layout doc, "Dressing: steel-mill details", Sean 2026-11-22): the things a steel mill's shed
/// and offices have that make it read as one. High-bay pendants under the trusses, an amber beacon for the cranes, a lagged steam main
/// with its elbows and risers, gauge clusters, heat shields for the ovens, chain racks, a gas bottle cage, a ladle stand and a slag pot,
/// and for the offices a notice board and a framed picture. docs/sandbox/constructs/zeroday-works.md "The mill's dressing parts".
///
/// Each part's own frame follows the interior dressing's (InteriorParts "Dressing"): a wall piece hangs by its "back" socket (the origin,
/// facing -z; the piece stands in +z), a floor piece stands on its "bottom" (-y), and the pendant hangs from its "top" (+y) under a Roof
/// Truss Half's bottom chord ("hang" sockets every 2 m). The lamps are mains lamps: lit while the power circuit reaches them, and a wire
/// into one switches it. The beacon's mirror turns while it is lit (a "spin" work box: ConstructRender draws a lamp's work while it is lit).
/// </summary>
public sealed partial class FactoryShellParts
{
    private const string Enamel = "#2f4a3c", EnamelIn = "#e9e6dc", GlowWarm = "#fff3b0", Amber = "#e08a12", Lag = "#c4bda8", Band = "#a4abb0",
        Iron = "#3a3c3e", Brass = "#b08d3c", Copper = "#b5734a", Valve = "#a12a22", Shield = "#9aa0a2", Black = "#262626", Wood = "#6b4a2b",
        Cork = "#b38a5a", Paper = "#e8e6dc", Ladle = "#4a4440", Slag = "#5a4a3c", Ember = "#c8552a", Galv2 = "#8e959a";

    /// <summary>The lagged main's radius and how far its axis stands off the wall it hangs on.</summary>
    public const double MainR = 0.3, MainOff = 0.55;

    /// <summary>A drawn look box with its own finish and an optional shape (detail unless said).</summary>
    private static PyDict? L(double[] mn, double[] mx, string color, string? finish = null, PyDict? shape = null, bool detail = true, bool glass = false)
    {
        var b = Box(mn, mx, color, detail: detail, glass: glass, shape: shape);
        if (b != null && finish != null)
            b["finish"] = finish;
        return b;
    }

    private static PyDict Cyl(string front, int segments = 12, double taper = 1, double inner = -1)
    {
        var s = Kw(("shape", inner > 0 ? "tube" : "cylinder"), ("segments", segments), ("front", front));
        if (taper != 1)
            s["taper"] = Py.L(Py.Round(taper, 4), Py.Round(taper, 4));
        if (inner > 0)
            s["inner"] = Py.Round(inner, 4);
        return s;
    }

    /// <summary>A lamp's glowing face: drawn bright in the lamp's colour while it is lit (an unpaintable detail).</summary>
    private static PyDict? Glow(double[] mn, double[] mx, PyDict? shape = null)
    {
        var b = Box(mn, mx, GlowWarm, detail: true, shape: shape);
        if (b != null)
            b["paintable"] = false;
        return b;
    }

    private static List<object?> LightConfig(string colour, int brightness) =>
    [
        new PyDict { ["key"] = "colour", ["type"] = "colour", ["label"] = "Colour", ["group"] = "Light", ["default"] = colour, ["tooltip"] = "The colour it glows when lit." },
        new PyDict { ["key"] = "brightness", ["label"] = "Brightness", ["group"] = "Light", ["unit"] = "%", ["min"] = 10, ["max"] = 200, ["step"] = 1, ["default"] = brightness, ["tooltip"] = "How bright it glows." },
        new PyDict { ["key"] = "blink", ["label"] = "Blink", ["group"] = "Light", ["unit"] = "Hz", ["min"] = 0, ["max"] = 10, ["step"] = 0.1, ["default"] = 0, ["tooltip"] = "Flashes this many times a second while lit (0: steady)." },
    ];

    private static Part Mains(Part p, int watts, string colour, int brightness = 100) =>
        p.Set(("logic", new PyDict { ["kind"] = "lamp", ["mains"] = true }), ("power", watts), ("config", LightConfig(colour, brightness)));

    private static Part Dressing(string name, string desc, string color, string finish, string role = "frame") =>
        new Part(name, Small, "Dressing", desc, role: role, tool: "wrench", color: color, group: "Interior").Set(("finish", finish));

    /// <summary>
    /// A High-Bay Pendant: an enamel shade on a short drop rod, hung from its "top" socket under a Roof Truss Half's "hang" sockets.
    /// 0.7 m from socket to bulb, so under a truss 4 m over the crane rail's socket it stays over a Hall Crane's bridge (drawn 3.12 m up).
    /// </summary>
    private static Part HighBayPendant()
    {
        var p = Dressing("High-Bay Pendant", "An enamel lamp shade on a drop rod, for under a shed's roof trusses. Lights while the power circuit reaches it and has charge; a wire into it switches it.", Enamel, "gloss paint", role: "logic");
        p.Add(L([-0.08, -0.03, -0.08], [0.08, 0, 0.08], Iron, "steel", detail: false)); // the hanger plate (its top on the socket)
        p.Add(L([-0.02, -0.22, -0.02], [0.02, -0.026, 0.02], Iron, "steel", Cyl("+y", 8))); // the drop rod
        p.Add(L([-0.12, -0.34, -0.12], [0.12, -0.21, 0.12], Enamel, "gloss paint", Cyl("+y", 12))); // the gear housing
        p.Add(L([-0.38, -0.62, -0.38], [0.38, -0.33, 0.38], Enamel, "gloss paint", Cyl("+y", 16, taper: 0.34))); // the shade, narrow at the top
        p.Add(L([-0.36, -0.629, -0.36], [0.36, -0.617, 0.36], EnamelIn, "gloss paint", Cyl("+y", 16))); // the white enamel inside's rim
        p.Add(Glow([-0.3, -0.643, -0.3], [0.3, -0.631, 0.3], Cyl("+y", 16))); // the lit underside
        p.Add(Glow([-0.09, -0.7, -0.09], [0.09, -0.634, 0.09], Kw(("shape", "sphere"), ("segments", 10)))); // the bulb
        p.Sock("top", [0, 0, 0], "+y");
        p.Stats(14, 60, 1, 1.0);
        return Mains(p, 250, "#fff1d0", 140);
    }

    /// <summary>
    /// An Amber Beacon: a rotating warning light for a crane or a door, stood on its "bottom". While it is lit its mirror turns inside the
    /// amber dome; wire it to what moves (a crane's controls, a door's switch) and it turns only while that does.
    /// </summary>
    private static Part AmberBeacon()
    {
        var p = Dressing("Amber Beacon", "A rotating amber warning light for a crane, a door or a crossing. Its mirror turns while it is lit; a wire into it switches it.", Black, "matte paint", role: "logic");
        p.Add(L([-0.14, 0, -0.14], [0.14, 0.06, 0.14], Black, "matte paint", detail: false)); // the base plate
        p.Add(L([-0.12, 0.056, -0.12], [0.12, 0.124, 0.12], Black, "matte paint", Cyl("+y", 16))); // the collar
        p.Add(L([-0.11, 0.12, -0.11], [0.11, 0.36, 0.11], Amber, "glass", Cyl("+y", 16, taper: 0.9), glass: true)); // the dome
        p.Add(L([-0.115, 0.357, -0.115], [0.115, 0.39, 0.115], Black, "matte paint", Cyl("+y", 16))); // the cap
        var mirror = Glow([-0.085, 0.152, -0.014], [0.085, 0.33, 0.014]);
        if (mirror != null)
        {
            mirror["work"] = "spin"; // (turns about +y through its own middle while the lamp is lit)
            mirror["axis"] = "+y";
        }
        p.Add(mirror);
        p.Add(L([-0.02, 0.122, -0.02], [0.02, 0.15, 0.02], Iron, "steel", Cyl("+y", 8))); // the spindle
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Stats(3, 30, 1, 1.0);
        return Mains(p, 40, "#ffa31a", 160);
    }

    /// <summary>Aluminium cladding bands round a lagged pipe along <paramref name="axis"/> ("+x" or "+y") at the given places.</summary>
    private static void Bands(Part p, string axis, IEnumerable<double> at, double z)
    {
        const double r = MainR + 0.008, w = 0.03;
        foreach (var a in at)
            p.Add(axis == "+x" ? L([a - w, -r, z - r], [a + w, r, z + r], Band, "aluminium", Cyl("+x", 16))
                : L([-r, a - w, z - r], [r, a + w, z + r], Band, "aluminium", Cyl("+y", 16)));
    }

    /// <summary>A flange round a lagged pipe's end: <paramref name="a"/> to <paramref name="b"/> along the axis.</summary>
    private static PyDict? Flange(string axis, double a, double b, double x, double z)
    {
        const double r = MainR + 0.06;
        return axis == "+x" ? L([Math.Min(a, b), -r, z - r], [Math.Max(a, b), r, z + r], Iron, "steel", Cyl("+x", 16))
            : L([x - r, Math.Min(a, b), z - r], [x + r, Math.Max(a, b), z + r], Iron, "steel", Cyl("+y", 16));
    }

    /// <summary>A wall bracket: a plate on the wall at x and a clamp ring round the pipe (solid: the one under the "back" socket).</summary>
    private static void Bracket(Part p, string axis, double at, bool solid)
    {
        const double z = MainOff, r = MainR;
        if (axis == "+x")
        {
            p.Add(L([at - 0.15, -0.4, 0], [at + 0.15, 0.4, 0.03], Iron, "steel", detail: !solid)); // the wall plate
            p.Add(L([at - 0.05, -0.38, 0.027], [at + 0.05, -0.3, z], Iron, "steel")); // the arm, out under the pipe
            p.Add(L([at - 0.06, -r - 0.04, z - r - 0.04], [at + 0.06, r + 0.04, z + r + 0.04], Iron, "steel", Cyl("+x", 16, inner: 0.9))); // the clamp
        }
        else
        {
            p.Add(L([-0.4, at - 0.15, 0], [0.4, at + 0.15, 0.03], Iron, "steel", detail: !solid));
            foreach (var s in new[] { -1, 1 })
                p.Add(L([Math.Min(s * 0.3, s * 0.38), at - 0.05, 0.027], [Math.Max(s * 0.3, s * 0.38), at + 0.05, z], Iron, "steel")); // an arm either side
            p.Add(L([-r - 0.04, at - 0.06, z - r - 0.04], [r + 0.04, at + 0.06, z + r + 0.04], Iron, "steel", Cyl("+y", 16, inner: 0.9)));
        }
    }

    /// <summary>
    /// A Lagged Main <paramref name="l"/> m: a 0.6 m insulated steam main along +x, its axis 0.55 m off the wall it hangs on by its "back"
    /// socket, flanged at each end ("-x" and "+x" sockets on the axis, so pieces chain end to end and a Lagged Main Elbow turns it).
    /// </summary>
    private static Part LaggedMain(double l)
    {
        var h = l / 2;
        var p = Dressing($"Lagged Main {M(l)} m", "A big insulated steam main on wall brackets, flanged at its ends: chain them end to end, turn corners and drops with a Lagged Main Elbow.", Lag, "lagging");
        p.Add(Box([-h, -MainR, MainOff - MainR], [h, MainR, MainOff + MainR], hidden: true)); // (the solid the end sockets sit on)
        p.Add(L([-h + 0.012, -MainR, MainOff - MainR], [h - 0.012, MainR, MainOff + MainR], Lag, "lagging", Cyl("+x", 16))); // the lagged pipe
        p.Add(Flange("+x", -h + 0.004, -h + 0.12, 0, MainOff));
        p.Add(Flange("+x", h - 0.12, h - 0.004, 0, MainOff));
        Bands(p, "+x", l >= 6 ? [-1.7, -0.5, 0.5, 1.6] : [-0.5, 0.5], MainOff);
        Bracket(p, "+x", 0, true);
        if (l >= 6)
        {
            foreach (var x in new[] { -2.2, 2.2 })
                Bracket(p, "+x", x, false);
            // A gate valve on top with its red hand wheel, and a yellow ID band.
            const double vx = 1.0, top = MainR;
            p.Add(L([vx - 0.12, top - 0.04, MainOff - 0.12], [vx + 0.12, top + 0.22, MainOff + 0.12], Iron, "steel", Cyl("+y", 12)));
            p.Add(L([vx - 0.02, top + 0.21, MainOff - 0.02], [vx + 0.02, top + 0.4, MainOff + 0.02], Iron, "steel", Cyl("+y", 8)));
            p.Add(L([vx - 0.22, top + 0.37, MainOff - 0.22], [vx + 0.22, top + 0.41, MainOff + 0.22], Valve, "gloss paint", Cyl("+y", 16, inner: 0.82)));
            p.Add(L([vx - 0.2, top + 0.38, MainOff - 0.012], [vx + 0.2, top + 0.4, MainOff + 0.012], Valve, "gloss paint")); // the spokes
            p.Add(L([vx - 0.012, top + 0.383, MainOff - 0.2], [vx + 0.012, top + 0.397, MainOff + 0.2], Valve, "gloss paint"));
            p.Add(L([-1.25, -MainR - 0.006, MainOff - MainR - 0.006], [-0.95, MainR + 0.006, MainOff + MainR + 0.006], Hazard, "hazard paint", Cyl("+x", 16)));
        }
        p.Sock("back", [0, 0, 0], "-z");
        p.Sock("-x", [-h, 0, MainOff], "-x");
        p.Sock("+x", [h, 0, MainOff], "+x");
        p.Stats(l * 95, 300 + l * 60, 4, 1.5);
        return p;
    }

    /// <summary>
    /// A Lagged Main Elbow: a lagged bend with a clad knuckle, from its "a" socket (the origin, on the pipe's axis, facing -x) to its "b"
    /// socket 0.6 m on and 0.6 m down (facing -y). Fit "a" to a Lagged Main's end and turn it to drop, rise or round a corner.
    /// </summary>
    private static Part LaggedElbow()
    {
        const double k = 0.6, r = MainR;
        var p = Dressing("Lagged Main Elbow", "A lagged bend for a Lagged Main: fit it to a main's end and turn it to drop to a Lagged Main Riser, rise or round a corner.", Lag, "lagging");
        p.Add(Box([0, -r, -r], [k, r, r], hidden: true)); // (the solids the sockets sit on)
        p.Add(Box([k - r, -k, -r], [k + r, 0, r], hidden: true));
        p.Add(L([0.012, -r, -r], [k, r, r], Lag, "lagging", Cyl("+x", 16)));
        p.Add(L([k - r, -k + 0.012, -r], [k + r, 0, r], Lag, "lagging", Cyl("+y", 16)));
        p.Add(L([k - r - 0.03, -r - 0.03, -r - 0.03], [k + r + 0.03, r + 0.03, r + 0.03], Lag, "lagging", Kw(("shape", "sphere"), ("segments", 16)))); // the clad knuckle
        p.Add(Flange("+x", 0.004, 0.12, 0, 0));
        p.Add(Flange("+y", -k + 0.004, -k + 0.12, k, 0));
        p.Add(L([k - r - 0.036, -0.03, -r - 0.036], [k + r + 0.036, 0.03, r + 0.036], Band, "aluminium", Cyl("+y", 16))); // a band round the knuckle
        p.Sock("a", [0, 0, 0], "-x");
        p.Sock("b", [k, -k, 0], "-y");
        p.Stats(140, 400, 4, 1.5);
        return p;
    }

    /// <summary>A Lagged Main Riser: 4 m of lagged main standing up a wall or column ("back", axis 0.55 m out), its "-y" and "+y" ends flanged.</summary>
    private static Part LaggedRiser()
    {
        const double h = 2.0;
        var p = Dressing("Lagged Main Riser", "A 4 m upright of lagged steam main on a wall bracket, for the drops to the furnaces: join it under a Lagged Main Elbow.", Lag, "lagging");
        p.Add(Box([-MainR, -h, MainOff - MainR], [MainR, h, MainOff + MainR], hidden: true));
        p.Add(L([-MainR, -h + 0.012, MainOff - MainR], [MainR, h - 0.012, MainOff + MainR], Lag, "lagging", Cyl("+y", 16)));
        p.Add(Flange("+y", -h + 0.004, -h + 0.12, 0, MainOff));
        p.Add(Flange("+y", h - 0.12, h - 0.004, 0, MainOff));
        Bands(p, "+y", [-1.2, -0.6, 0.6, 1.2], MainOff);
        Bracket(p, "+y", 0, true);
        p.Sock("back", [0, 0, 0], "-z");
        p.Sock("-y", [0, -h, MainOff], "-y");
        p.Sock("+y", [0, h, MainOff], "+y");
        p.Stats(380, 540, 4, 1.5);
        return p;
    }

    /// <summary>A Gauge Cluster: three brass-bezelled pressure dials on a plate, fed from a copper manifold under them. For an oven's Core or a plant room.</summary>
    private static Part GaugeCluster()
    {
        var p = Dressing("Gauge Cluster", "Three pressure gauges on a plate with their copper manifold, for a furnace, an oven or a plant room wall. Dressing.", "#3d4a40", "painted steel");
        p.Add(L([-0.4, -0.3, 0], [0.4, 0.3, 0.03], "#3d4a40", "painted steel", detail: false)); // the plate
        var dials = new[] { (-0.25, "+y"), (0.0, "+x"), (0.25, "-x") };
        foreach (var (x, needle) in dials)
        {
            const double y = 0.1;
            p.Add(L([x - 0.12, y - 0.12, 0.027], [x + 0.12, y + 0.12, 0.07], Brass, "brass", Cyl("+z", 16))); // the bezel
            p.Add(L([x - 0.1, y - 0.1, 0.066], [x + 0.1, y + 0.1, 0.074], Paper, "plastic", Cyl("+z", 16))); // the dial face
            p.Add(L([x + 0.04, y + 0.04, 0.072], [x + 0.08, y + 0.08, 0.077], Valve, "gloss paint")); // the red line's mark
            p.Add(needle switch
            {
                "+y" => L([x - 0.006, y - 0.01, 0.076], [x + 0.006, y + 0.08, 0.088], Black, "matte paint"),
                "+x" => L([x - 0.01, y - 0.006, 0.076], [x + 0.08, y + 0.006, 0.088], Black, "matte paint"),
                _ => L([x - 0.08, y - 0.006, 0.076], [x + 0.01, y + 0.006, 0.088], Black, "matte paint"),
            });
            p.Add(L([x - 0.015, -0.19, 0.075], [x + 0.015, -0.024, 0.105], Copper, "copper", Cyl("+y", 8))); // its feed
        }
        p.Add(L([-0.36, -0.24, 0.05], [0.36, -0.16, 0.13], Copper, "copper", Cyl("+x", 10))); // the manifold
        p.Add(L([0.3, -0.27, 0.12], [0.34, -0.13, 0.16], Valve, "gloss paint")); // its cock's handle
        p.Add(L([-0.3, 0.25, 0.027], [0.3, 0.285, 0.036], Paper, "painted steel")); // the label strip
        p.Sock("back", [0, 0, 0], "-z");
        p.Stats(12, 60, 1, 1.0);
        return p;
    }

    /// <summary>A Heat Shield: a 2 x 2 m ribbed aluminised sheet on stand-offs, for an oven's or a furnace's side facing a walkway.</summary>
    private static Part HeatShield()
    {
        var p = Dressing("Heat Shield", "A ribbed aluminised sheet on stand-offs, 2 x 2 m: hang it on an oven's or a furnace's side where people walk past. Dressing.", Shield, "aluminium");
        p.Add(L([-0.06, -0.06, 0], [0.06, 0.06, 0.154], Iron, "steel", detail: false)); // the middle stand-off (on the socket)
        foreach (var (x, y) in new[] { (-0.8, -0.75), (0.8, -0.75), (-0.8, 0.75), (0.8, 0.75) })
            p.Add(L([x - 0.04, y - 0.04, 0.004], [x + 0.04, y + 0.04, 0.154], Iron, "steel")); // a stand-off
        p.Add(L([-1.0, -0.97, 0.15], [1.0, 1.0, 0.19], Shield, "aluminium", detail: false)); // the sheet
        for (var y = -0.82; y < 0.75; y += 0.2)
            p.Add(L([-0.97, y - 0.035, 0.186], [0.97, y + 0.035, 0.212], y < -0.3 ? "#8f8466" : Shield, "aluminium")); // a rib (the low ones heat-browned)
        for (var k = 0; k < 10; k++)
        {
            var x = -0.95 + k * 0.19;
            var o = (k % 2) * 0.003; // (the black stripes a little wider and prouder: no shared side faces)
            p.Add(L([x - o, 0.84, 0.19 + o], [x + 0.19 + o, 0.95, 0.198 + o], k % 2 == 0 ? Hazard : Black, "hazard paint")); // the hazard band
        }
        p.Sock("back", [0, 0, 0], "-z");
        p.Stats(55, 400, 6, 1.5);
        return p;
    }

    /// <summary>A Hook and Chain Rack: a wall beam with four hooks, two chains with hook blocks and two lifting slings on them.</summary>
    private static Part ChainRack()
    {
        var p = Dressing("Hook and Chain Rack", "A wall beam with hooks, hung with lifting chains and slings, for a crane bay's wall or column. Dressing.", Iron, "dirty steel");
        p.Add(L([-0.9, -0.08, 0], [0.9, 0.08, 0.12], Iron, "dirty steel", detail: false)); // the beam
        for (var k = 0; k < 6; k++)
            p.Add(L([-0.85 + k * 0.3, 0.02, 0.123], [-0.7 + k * 0.3, 0.06, 0.129], k % 2 == 0 ? Hazard : Black, "hazard paint")); // the beam's stripes
        foreach (var (x, kind, drop) in new[] { (-0.6, 0, 1.1), (-0.2, 1, 0.9), (0.2, 0, 0.75), (0.6, 1, 1.0) })
        {
            p.Add(L([x - 0.02, -0.06, 0.117], [x + 0.02, -0.02, 0.32], Iron, "steel")); // the hook's arm
            p.Add(L([x - 0.02, -0.024, 0.28], [x + 0.02, 0.05, 0.32], Iron, "steel")); // its tip
            if (kind == 0)
            {
                p.Add(L([x - 0.012, -drop, 0.236], [x + 0.012, -0.056, 0.264], Black, "steel")); // a chain
                for (var y = -0.15; y > -drop + 0.1; y -= 0.15)
                    p.Add(L([x - 0.03, y - 0.04, 0.23], [x + 0.03, y + 0.04, 0.27], Black, "steel")); // a link across
                p.Add(L([x - 0.07, -drop - 0.16, 0.2], [x + 0.07, -drop + 0.004, 0.3], Yellow, "hazard paint")); // the hook block
                p.Add(L([x - 0.015, -drop - 0.3, 0.235], [x + 0.015, -drop - 0.156, 0.265], Iron, "steel")); // its hook
            }
            else
            {
                var col = x < 0 ? "#d0661f" : "#3a7a3a";
                foreach (var s in new[] { -1, 1 })
                    p.Add(L([x + s * 0.03 - 0.015, -drop, 0.24], [x + s * 0.03 + 0.015, -0.04, 0.255], col, "fabric")); // a sling's leg
                p.Add(L([x - 0.06, -drop - 0.05, 0.236], [x + 0.06, -drop + 0.004, 0.26], col, "fabric")); // its eye
            }
        }
        p.Sock("back", [0, 0, 0], "-z");
        p.Stats(70, 300, 4, 1.0);
        return p;
    }

    private const string Yellow = "#d9b23a";

    /// <summary>A Gas Bottle Cage: a galvanised mesh cage with a roof, six oxygen, acetylene and argon bottles chained in it.</summary>
    private static Part GasBottleCage()
    {
        const double x1 = 0.8, z1 = 0.4, top = 1.9;
        var p = Dressing("Gas Bottle Cage", "A galvanised cage of welding gas bottles, chained in, for beside an oven or a repair shop. Dressing.", Band, "steel");
        p.Add(L([-x1, 0, -z1], [x1, 0.05, z1], Galv2, "steel", detail: false)); // the base plate
        p.Add(Box([-x1, 0.05, -z1], [x1, top, z1], hidden: true)); // (what bumps into it)
        foreach (var x in new[] { -x1 + 0.003, x1 - 0.053 })
            foreach (var z in new[] { -z1 + 0.003, z1 - 0.053 })
                p.Add(L([x, 0.046, z], [x + 0.05, top + 0.004, z + 0.05], Band, "steel")); // a corner post
        p.Add(L([-x1 - 0.02, top, -z1 - 0.02], [x1 + 0.02, top + 0.04, z1 + 0.02], Galv2, "steel")); // the roof
        for (var x = -0.675; x < 0.7; x += 0.15)
            p.Add(L([x - 0.008, 0.046, z1 - 0.026], [x + 0.008, top, z1 - 0.01], Band, "steel")); // a front bar
        foreach (var s in new[] { -1, 1 })
            for (var z = -0.25; z < 0.3; z += 0.15)
                p.Add(L([Math.Min(s * (x1 - 0.026), s * (x1 - 0.01)), 0.046, z - 0.008], [Math.Max(s * (x1 - 0.026), s * (x1 - 0.01)), top, z + 0.008], Band, "steel")); // a side bar
        foreach (var y in new[] { 0.6, 1.25 })
            p.Add(L([-x1 + 0.05, y, z1 - 0.034], [x1 - 0.05, y + 0.04, z1 - 0.004], Band, "steel")); // a front rail
        var bottles = new[] { ("#1f2224", "#e6e6e0"), ("#1f2224", "#e6e6e0"), ("#6e1f24", "#6e1f24"), ("#6e1f24", "#6e1f24"), ("#2e5a3c", "#2e5a3c"), ("#1f2224", "#e6e6e0") };
        for (var k = 0; k < bottles.Length; k++)
        {
            var x = -0.6 + k * 0.24;
            var (body, shoulder) = bottles[k];
            p.Add(L([x - 0.11, 0.052, -0.11], [x + 0.11, 1.35, 0.11], body, "gloss paint", Cyl("+y", 14))); // the bottle
            p.Add(L([x - 0.11, 1.346, -0.11], [x + 0.11, 1.47, 0.11], shoulder, "gloss paint", Cyl("+y", 14, taper: 0.45))); // its shoulder
            p.Add(L([x - 0.03, 1.465, -0.03], [x + 0.03, 1.56, 0.03], Brass, "brass", Cyl("+y", 8))); // its valve
        }
        p.Add(L([-0.72, 1.0, 0.112], [0.72, 1.024, 0.13], Black, "steel")); // the restraint chain across the bottles
        p.Add(L([-0.25, 1.5, z1 - 0.004], [0.25, 1.7, z1 + 0.008], "#f2b01e", "painted steel")); // the warning sign
        p.Add(L([-0.2, 1.62, z1 + 0.004], [0.2, 1.67, z1 + 0.01], Black, "painted steel"));
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Stats(380, 500, 4, 1.5);
        return p;
    }

    /// <summary>A Ladle Stand: a cold refractory-lined ladle resting by its trunnions on two trestles, its bail up for the crane's hook.</summary>
    private static Part LadleStand()
    {
        var p = Dressing("Ladle Stand", "A steel ladle resting on its trestle stand, bail up for a crane's hook, for beside a foundry or a furnace. Dressing.", Ladle, "dirty steel");
        p.Add(L([-1.2, 0, -1.0], [1.2, 0.04, 1.0], Iron, "dirty steel", detail: false)); // the base plate
        p.Add(Box([-0.8, 0.25, -0.8], [0.8, 1.86, 0.8], hidden: true)); // (the ladle's bulk)
        foreach (var x in new[] { -1.05, 1.05 })
        {
            foreach (var z in new[] { -0.4, 0.4 })
                p.Add(L([x - 0.12, 0.036, z - 0.1], [x + 0.12, 1.304, z + 0.1], Iron, "dirty steel")); // a trestle's leg
            p.Add(L([x - 0.15, 1.3, -0.5], [x + 0.15, 1.4, 0.5], Iron, "dirty steel")); // its saddle
            p.Add(L([x - 0.15, 0.036, -0.55], [x + 0.15, 0.2, 0.55], Iron, "dirty steel")); // its foot
        }
        p.Add(L([-0.8, 0.25, -0.8], [0.8, 1.85, 0.8], Ladle, "dirty steel", Cyl("-y", 18, taper: 0.8))); // the ladle, narrow at the foot
        p.Add(L([-0.84, 1.75, -0.84], [0.84, 1.86, 0.84], Iron, "steel", Cyl("+y", 18))); // its rim band
        p.Add(L([-0.7, 1.856, -0.7], [0.7, 1.87, 0.7], "#2a1f1a", "soot", Cyl("+y", 18))); // the cold skull inside
        p.Add(L([-0.15, 1.75, 0.8], [0.15, 1.85, 1.0], Iron, "steel")); // the pouring lip
        foreach (var s in new[] { -1, 1 })
        {
            p.Add(L([Math.Min(s * 0.7, s * 0.98), 1.25, -0.1], [Math.Max(s * 0.7, s * 0.98), 1.45, 0.1], Iron, "steel", Cyl("+x", 12))); // a trunnion
            p.Add(L([s * 0.9 - 0.05, 1.44, -0.05], [s * 0.9 + 0.05, 2.604, 0.05], Yellow, "hazard paint")); // the bail's arm
        }
        p.Add(L([-0.95, 2.6, -0.06], [0.95, 2.72, 0.06], Yellow, "hazard paint")); // the bail's bar
        foreach (var (y, z) in new[] { (0.9, -0.74), (1.3, -0.79), (0.6, -0.7) })
            p.Add(L([-0.2 + y * 0.2, y, z - 0.04], [0.05 + y * 0.2, y + 0.18, z + 0.004], Slag, "rust")); // slag dribbles down its side
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Stats(2600, 2400, 12, 3.0);
        return p;
    }

    /// <summary>A Slag Pot: a bell-shaped pot of cooling slag on a four-wheeled bogie, a few embers still glowing in its crust.</summary>
    private static Part SlagPot()
    {
        var p = Dressing("Slag Pot", "A pot of cooling slag on its bogie, for beside a furnace or a foundry. Dressing.", Ladle, "dirty steel");
        p.Add(Box([-0.1, 0, -0.1], [0.1, 0.254, 0.1], hidden: true)); // (the pad its socket sits on)
        p.Add(L([-0.9, 0.25, -0.7], [0.9, 0.45, 0.7], Iron, "dirty steel", detail: false)); // the bogie's frame
        foreach (var x in new[] { -0.6, 0.6 })
            foreach (var s in new[] { -1, 1 })
                p.Add(L([x - 0.25, 0.004, Math.Min(s * 0.705, s * 0.82)], [x + 0.25, 0.504, Math.Max(s * 0.705, s * 0.82)], Black, "steel", Cyl("+z", 14))); // a wheel
        p.Add(L([-0.75, 0.446, -0.75], [0.75, 1.5, 0.75], Ladle, "dirty steel", Cyl("-y", 18, taper: 0.55))); // the pot
        p.Add(L([-0.8, 1.45, -0.8], [0.8, 1.53, 0.8], Iron, "steel", Cyl("+y", 18))); // its rim
        p.Add(L([-0.68, 1.526, -0.68], [0.68, 1.545, 0.68], Slag, "rust", Cyl("+y", 18))); // the crust
        foreach (var (x, z) in new[] { (-0.25, 0.1), (0.2, -0.3), (0.35, 0.25) })
            p.Add(L([x - 0.07, 1.541, z - 0.05], [x + 0.07, 1.56, z + 0.05], Ember, "rust")); // an ember
        foreach (var s in new[] { -1, 1 })
            p.Add(L([Math.Min(s * 0.62, s * 0.92), 1.15, -0.1], [Math.Max(s * 0.62, s * 0.92), 1.35, 0.1], Iron, "steel", Cyl("+x", 12))); // a lug
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Stats(1800, 1800, 10, 2.5);
        return p;
    }

    /// <summary>A Cork Notice Board: a cork board in a wooden frame with notices pinned up, for an office or a crew room wall.</summary>
    private static Part NoticeBoard()
    {
        var p = Dressing("Cork Notice Board", "A cork board in a wooden frame with notices pinned up, for an office, a control room or a crew room. Dressing.", Wood, "wood");
        p.Add(L([-0.62, -0.45, 0], [0.62, 0.45, 0.03], Wood, "wood", detail: false)); // the backing
        p.Add(L([-0.573, -0.403, 0.026], [0.573, 0.403, 0.038], Cork, "fabric")); // the cork
        p.Add(L([-0.62, 0.405, 0.026], [0.62, 0.45, 0.05], Wood, "wood")); // the frame
        p.Add(L([-0.62, -0.45, 0.026], [0.62, -0.405, 0.05], Wood, "wood"));
        foreach (var s in new[] { -1, 1 })
            p.Add(L([Math.Min(s * 0.575, s * 0.62), -0.403, 0.026], [Math.Max(s * 0.575, s * 0.62), 0.403, 0.048], Wood, "wood"));
        var notes = new[] { (-0.45, 0.05, 0.21, 0.297, Paper), (-0.2, 0.12, 0.21, 0.24, "#efe39a"), (0.05, -0.05, 0.297, 0.21, Paper),
            (0.32, 0.1, 0.18, 0.22, "#e9b8c2"), (-0.38, -0.32, 0.25, 0.12, "#b8d0e6"), (0.28, -0.3, 0.21, 0.16, Paper) };
        for (var k = 0; k < notes.Length; k++)
        {
            var (x, y, w, h, col) = notes[k];
            var z0 = 0.034 + (k % 2) * 0.002;
            p.Add(L([x, y - h / 2, z0], [x + w, y + h / 2, z0 + 0.012], col, "plastic")); // a notice
            p.Add(L([x + w / 2 - 0.01, y + h / 2 - 0.03, z0 + 0.008], [x + w / 2 + 0.01, y + h / 2 - 0.01, z0 + 0.02], k % 3 == 0 ? Valve : "#2c5aa0", "plastic")); // its pin
        }
        p.Add(L([0.06, 0.025, 0.042], [0.17, 0.045, 0.054], Valve, "plastic")); // the red heading on the safety notice
        p.Sock("back", [0, 0, 0], "-z");
        p.Stats(6, 30, 1, 1.0);
        return p;
    }

    /// <summary>A Wall Frame: a framed picture of the Zero Day under way, for an office or the control room.</summary>
    private static Part WallFrame()
    {
        var p = Dressing("Wall Frame", "A framed picture of the Zero Day under way, for an office or a control room wall. Dressing.", "#2a2420", "wood");
        p.Add(L([-0.45, -0.32, 0], [0.45, 0.32, 0.03], "#2a2420", "wood", detail: false)); // the backing
        p.Add(L([-0.45, 0.27, 0.026], [0.45, 0.32, 0.055], "#2a2420", "wood")); // the frame
        p.Add(L([-0.45, -0.32, 0.026], [0.45, -0.27, 0.055], "#2a2420", "wood"));
        foreach (var s in new[] { -1, 1 })
            p.Add(L([Math.Min(s * 0.4, s * 0.45), -0.268, 0.026], [Math.Max(s * 0.4, s * 0.45), 0.268, 0.053], "#2a2420", "wood"));
        p.Add(L([-0.398, -0.268, 0.026], [0.398, 0.268, 0.037], "#e8e0c8", "painted wood")); // the mount
        p.Add(L([-0.33, -0.2, 0.032], [0.33, 0.2, 0.043], "#7a96b0", "matte paint")); // the sky
        p.Add(L([-0.328, -0.198, 0.034], [0.328, -0.12, 0.046], "#3f4a55", "matte paint")); // the planet's limb
        p.Add(L([-0.24, -0.06, 0.038], [0.2, 0.0, 0.049], "#5d6670", "matte paint")); // the hull
        p.Add(L([0.198, -0.05, 0.035], [0.27, -0.012, 0.0485], "#5d6670", "matte paint")); // the bow
        p.Add(L([-0.06, -0.002, 0.039], [0.05, 0.06, 0.0505], "#4d555e", "matte paint")); // the bridge
        p.Add(L([-0.29, -0.055, 0.04], [-0.238, -0.005, 0.0515], "#f0a040", "matte paint")); // the drives
        p.Add(L([-0.1, -0.25, 0.034], [0.1, -0.225, 0.046], Brass, "brass")); // the plaque
        p.Sock("back", [0, 0, 0], "-z");
        p.Stats(3, 20, 1, 1.0);
        return p;
    }

    /// <summary>The dressing's part names in the order written: Assembling's Tail and AfterCloche list them so (saved recipe numbers keep).</summary>
    public static IReadOnlyList<string> MillDressingNames => MillDressingParts().Select(e => (string)e.Item2.D["name"]!).ToList();

    private static IEnumerable<(string, Part)> MillDressing() =>
        MillDressingParts().Select(e => (e.Item1, e.Item2.Set(("collider", "boxes")))); // (the round looks are drawn only)

    private static IEnumerable<(string, Part)> MillDressingParts()
    {
        yield return ("Dressing", HighBayPendant());
        yield return ("Dressing", AmberBeacon());
        yield return ("Dressing", LaggedMain(6));
        yield return ("Dressing", LaggedMain(2));
        yield return ("Dressing", LaggedElbow());
        yield return ("Dressing", LaggedRiser());
        yield return ("Dressing", GaugeCluster());
        yield return ("Dressing", HeatShield());
        yield return ("Dressing", ChainRack());
        yield return ("Dressing", GasBottleCage());
        yield return ("Dressing", LadleStand());
        yield return ("Dressing", SlagPot());
        yield return ("Dressing", NoticeBoard());
        yield return ("Dressing", WallFrame());
    }
}
