using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The Highland faction's parts: Constructs/Parts/Highland (docs/vehiclekit/VEHICLEKIT.md). The Pipe Box (a Music Player that strikes
/// up when what it's on is driven), and what the clan bolts on for show: a Saltire, a thistle, a wee caber, a coo's head, a haggis,
/// a keg; and the Whisky Cask, which holds loot. A trophy stands on one "base" socket and points up its own +y, so the kit turns it
/// outward, as the bandit parts do. Was tools/constructs/highland_parts.py.
/// </summary>
public sealed class HighlandParts : IPartGenerator
{
    public string Name => "highland_parts";

    private const string Out = "Constructs/Parts/Highland";
    private const string Section = "Body panels";
    private const string Group = "Lights and Details";
    private const string Wood = "#6b4527", WoodDark = "#4a2f1a", Bezel = "#14181c";
    private const string Tartan = "#9b1c22", TartanGreen = "#1f4a2e", Ebony = "#1b1714", Silver = "#c9cbc8";
    private const string Saltire = "#005eb8", White = "#f2f0e8";
    private const string Stem = "#3f6b34", Leaf = "#2f5a2a", ThistleColour = "#7a3a8c";
    private const string Ginger = "#b5632a", GingerDark = "#8a4a1e", HornColour = "#e3d6b4", Nose = "#2a201c";
    private const string HaggisColour = "#6e4a2e", Plate = "#d9d4c8";
    private const string Iron = "#3a3532";

    private static PyDict Volume() => new()
    {
        ["key"] = "volume", ["label"] = "Volume", ["group"] = "General", ["min"] = 0, ["max"] = 1, ["step"] = 0.05, ["default"] = 0.8,
        ["tooltip"] = "How loud it plays, before distance.",
    };

    private static PyDict Loop() => new()
    {
        ["key"] = "loop", ["label"] = "Loop the list", ["group"] = "General", ["type"] = "toggle", ["default"] = true,
        ["tooltip"] = "After the last tune it starts again from the top.",
    };

    private static PyDict NoPaint => new() { ["paintable"] = false };

    private static PyDict Round(string shape, int segments, string? front = null, double[]? taper = null, bool paintable = true)
    {
        var d = new PyDict { ["shape"] = shape, ["segments"] = segments };
        if (taper is not null)
            d["taper"] = Py.L(taper[0], taper[1]);
        if (front is not null)
            d["front"] = front;
        if (!paintable)
            d["paintable"] = false;
        return d;
    }

    private static void BaseSocket(Part p) => p.Sock("base", [0, 0, 0], "-y");

    /// <summary>A square post from the base up to top, under whatever it holds.</summary>
    private static void Post(Part p, double top, string colour = Iron) => p.Add(Box([-0.02, 0, -0.02], [0.02, top, 0.02], colour));

    private static (string, Part) PipeBox()
    {
        var desc = "The Clan MacSlop's pipes, in a box. It strikes up by itself when someone drives what it's on, so you hear them coming, "
            + "and plays the game's tracks and links like a Music Player. Heard to 150 m. Run a data cable to a speaker to carry it further.";
        var p = new Part("Pipe Box", Small, "Media", desc, role: "panel", tool: "wrench", group: "Logic", color: Wood);
        double w = 0.32, h = 0.32, t = 0.14;
        var rim = 0.008;
        var f = -t / 2; // (the case stands centred on its base socket, the screen's face at -z)
        p.Add(Box([-w / 2, 0, f], [w / 2, h, t / 2])); // (the case)
        p.Add(Box([-w / 2 + rim, rim, f - 0.004], [w / 2 - rim, h - rim, f + 0.002], Bezel, detail: true)); // (the screen's bezel, as a Music Player's)
        // The pipes on its lid: the bag, three drones up out of it and the blowpipe.
        var top = h + 0.004;
        p.Add(Box([-0.11, top, -0.06], [0.11, top + 0.12, 0.06], Tartan, detail: true, shape: Round("sphere", 10, paintable: false)));
        foreach (var (x, tall, z) in new[] { (-0.06, 0.32, 0.02), (0.0, 0.27, 0.03), (0.06, 0.27, 0.02) })
        {
            p.Add(Box([x - 0.013, top + 0.09, z - 0.013], [x + 0.013, top + 0.09 + tall, z + 0.013], Ebony, detail: true, shape: Round("cylinder", 8, "+y", paintable: false)));
            p.Add(Box([x - 0.02, top + 0.09 + tall - 0.03, z - 0.02], [x + 0.02, top + 0.09 + tall, z + 0.02], Silver, detail: true, shape: Round("cylinder", 8, "+y", paintable: false)));
        }
        p.Add(Box([0.09, top + 0.08, -0.01], [0.15, top + 0.095, 0.004], TartanGreen, detail: true, shape: NoPaint)); // (the blowpipe)
        BaseSocket(p);
        p.Sock("back", [0, h / 2, t / 2], "+z");
        p.Stats(8, 60, 1, 1);
        var pw = Py.Round(w - 2 * rim, 3);
        var bottom = 0.44; // (as the Music Player: YouTube's player stays in view above the controls)
        p.Set(("config", Py.L(Volume(), Loop())), ("centreSockets", false),
            ("media", new PyDict { ["hear"] = 150, ["near"] = 10, ["width"] = 356, ["height"] = 200, ["pipes"] = true }),
            ("panels", Py.L(new PyDict
            {
                ["source"] = "media", ["at"] = Py.L(0, h / 2, Py.Round(f - 0.007, 3)), ["size"] = Py.L(pw, pw), ["face"] = "-z", ["up"] = "+y", ["rows"] = 8, ["range"] = 12,
                ["picture"] = Py.L(0, bottom, 1, 1),
                ["touch"] = Py.L(
                    new PyDict { ["action"] = "media.previous", ["at"] = Py.L(0.02, 0.03, 0.32, 0.24), ["label"] = "Prev" },
                    new PyDict { ["action"] = "media.toggle", ["at"] = Py.L(0.35, 0.03, 0.65, 0.24), ["label"] = "Play" },
                    new PyDict { ["action"] = "media.next", ["at"] = Py.L(0.68, 0.03, 0.98, 0.24), ["label"] = "Next" }),
            })));
        return ("", p);
    }

    private static (string, Part) SaltireFlag()
    {
        var p = new Part("Saltire Flag", Small, Section, "A Saltire on a pole, white on blue. It flies the right way up whichever way you park.", color: Saltire, group: Group);
        double r = 0.016, tall = 1.0;
        p.Add(Box([-0.05, 0, -0.04], [0.05, 0.04, 0.04], Iron)); // (its foot)
        p.Add(Box([-r, 0.04, -r], [r, tall, r], hidden: true));
        p.Add(Box([-r, 0.04, -r], [r, tall, r], WoodDark, detail: true, shape: Round("cylinder", 8, "+y")));
        double x0 = r + 0.004, x1 = 0.52, y0 = tall - 0.36, y1 = tall - 0.02;
        p.Add(Box([x0, y0, -0.006], [x1, y1, 0.006], Saltire, detail: true)); // (the flag)
        var steps = 7;
        double sw = (x1 - x0) / steps, sh = (y1 - y0) / steps;
        for (var k = 0; k < steps; k++) // (the white cross, in steps: each square through the cloth, 3 mm proud of both faces)
        {
            var x = x0 + k * sw;
            foreach (var y in new[] { y0 + k * sh, y1 - (k + 1) * sh })
                p.Add(Box([x, y, -0.009], [Math.Min(x1, x + sw * 1.3), Math.Min(y1, y + sh * 1.3), 0.009], White, detail: true, shape: NoPaint));
        }
        BaseSocket(p);
        p.Stats(2, 25, 0, 0.5);
        return ("", p);
    }

    private static (string, Part) Thistle()
    {
        var p = new Part("Thistle", Small, Section, "A thistle on a stalk, bigger than they grow. Don't pat it.", color: Stem, group: Group);
        var top = 0.24;
        p.Add(Box([-0.018, 0, -0.018], [0.018, top, 0.018], Stem));
        foreach (var s in new[] { -1, 1 }) // (two spiky leaves)
            p.Add(Box([Math.Min(s * 0.02, s * 0.12), 0.08, -0.012], [Math.Max(s * 0.02, s * 0.12), 0.11, 0.012], Leaf, detail: true, shape: NoPaint));
        p.Add(Box([-0.07, top, -0.07], [0.07, top + 0.12, 0.07], Stem, detail: true, shape: Round("sphere", 10, paintable: false))); // (the bulb)
        p.Add(Box([-0.06, top + 0.1, -0.06], [0.06, top + 0.22, 0.06], ThistleColour, detail: true, shape: Round("cylinder", 10, "-y", [0.7, 0.7], false))); // (the purple tuft, flaring up)
        BaseSocket(p);
        p.Stats(1, 15, 0, 0.5);
        return ("", p);
    }

    private static (string, Part) Caber()
    {
        var p = new Part("Wee Caber", Small, Section, "A caber, cut down to fit. Still a log. Still thrown, now and then.", color: Wood, group: Group);
        double r = 0.05, tall = 0.7;
        p.Add(Box([-r * 1.5, 0.002, -r * 1.5], [r * 1.5, 0.024, r * 1.5], Iron, detail: true, shape: Round("cylinder", 8, "+y")));
        p.Add(Box([-r, 0, -r], [r, tall, r], hidden: true));
        p.Add(Box([-r, 0, -r], [r, tall, r], Wood, detail: true, shape: Round("cylinder", 8, "+y", [0.8, 0.8], false)));
        p.Add(Box([-r * 0.8 - 0.004, tall - 0.003, -r * 0.8 - 0.004], [r * 0.8 + 0.004, tall + 0.008, r * 0.8 + 0.004], "#c8a77a", detail: true, shape: Round("cylinder", 8, "+y", paintable: false))); // (its cut end)
        BaseSocket(p);
        p.Stats(6, 50, 2, 0.5);
        return ("", p);
    }

    private static (string, Part) Coo()
    {
        var p = new Part("Coo Heid", Small, Section, "A Highland coo's head, ginger fringe and all. Ceramic, the clan insists.", color: Ginger, group: Group);
        var top = 0.2;
        Post(p, top);
        p.Add(Box([-0.06, top, -0.05], [0.06, top + 0.14, 0.06], hidden: true));
        p.Add(Box([-0.08, top, -0.08], [0.08, top + 0.17, 0.08], Ginger, detail: true, shape: Round("sphere", 10, paintable: false))); // (the shaggy head)
        p.Add(Box([-0.07, top + 0.09, -0.094], [0.07, top + 0.16, -0.05], GingerDark, detail: true, shape: NoPaint)); // (the fringe, over its eyes)
        p.Add(Box([-0.045, top + 0.005, -0.112], [0.045, top + 0.06, -0.06], Nose, detail: true, shape: NoPaint)); // (the nose)
        foreach (var s in new[] { -1, 1 })
        {
            p.Add(Box([Math.Min(s * 0.07, s * 0.25), top + 0.12, -0.02], [Math.Max(s * 0.07, s * 0.25), top + 0.155, 0.015], HornColour, detail: true, shape: NoPaint));
            p.Add(Box([Math.Min(s * 0.22, s * 0.255), top + 0.15, -0.02], [Math.Max(s * 0.22, s * 0.255), top + 0.27, 0.015], HornColour, detail: true, shape: NoPaint));
        }
        BaseSocket(p);
        p.Stats(3, 25, 0, 0.5);
        return ("", p);
    }

    private static (string, Part) Haggis()
    {
        var p = new Part("Haggis on a Post", Small, Section, "A haggis on a plate on a post. A wild one: they run faster one way round the hill.", color: HaggisColour, group: Group);
        var top = 0.22;
        Post(p, top);
        p.Add(Box([-0.13, top, -0.13], [0.13, top + 0.02, 0.13], Plate, detail: true, shape: Round("cylinder", 12, "+y", paintable: false)));
        p.Add(Box([-0.1, top + 0.024, -0.075], [0.1, top + 0.15, 0.075], HaggisColour, detail: true, shape: Round("sphere", 10, paintable: false)));
        p.Add(Box([-0.016, top + 0.03, -0.08], [0.016, top + 0.15, 0.08], Tartan, detail: true, shape: NoPaint)); // (its tartan ribbon)
        BaseSocket(p);
        p.Stats(2, 15, 0, 0.5);
        return ("", p);
    }

    private static (string, Part) Keg()
    {
        var p = new Part("Wee Keg", Small, Section, "A wee keg on a stand, strapped down. Strictly for the radiator.", color: Wood, group: Group);
        double r = 0.15, tall = 0.36;
        p.Add(Box([-0.1, 0, -0.1], [0.1, 0.03, 0.1], Iron)); // (the stand)
        p.Add(Box([-r, 0.032, -r], [r, 0.032 + tall, r], hidden: true));
        p.Add(Box([-r, 0.032, -r], [r, 0.032 + tall, r], Wood, detail: true, shape: Round("cylinder", 12, "+y", [0.86, 0.86])));
        foreach (var y in new[] { 0.07, 0.032 + tall - 0.06 }) // (the hoops, 4 mm proud of the staves)
            p.Add(Box([-r - 0.004, y, -r - 0.004], [r + 0.004, y + 0.022, r + 0.004], Iron, detail: true, shape: Round("cylinder", 12, "+y", paintable: false)));
        BaseSocket(p);
        p.Stats(9, 40, 1, 0.5);
        return ("", p);
    }

    private static void Feet(Part p, double[] xs, double[] zs)
    {
        for (var i = 0; i < xs.Length; i++)
            for (var k = 0; k < zs.Length; k++)
                p.Sock($"foot{i}{k}", [xs[i], 0, zs[k]], "-y");
    }

    private static (string, Part) Cask()
    {
        var p = new Part("Whisky Cask", Small, "Furniture", "A cask stood on end, ageing. What's inside is loot, and older than you.", role: "storage", tool: "wrench", color: Wood, group: "Vehicle Body");
        double r = 0.26, tall = 0.95, wall = 0.04;
        p.Add(Box([-r, 0, -r], [r, tall, r], Wood, detail: true, shape: Round("cylinder", 12, "+y", [0.9, 0.9]))); // (the staves)
        foreach (var y in new[] { 0.08, tall / 2 - 0.012, tall - 0.11 }) // (hoops, 4 mm proud)
            p.Add(Box([-r - 0.004, y, -r - 0.004], [r + 0.004, y + 0.025, r + 0.004], Iron, detail: true, shape: Round("cylinder", 12, "+y", paintable: false)));
        p.Add(Box([-0.03, tall - 0.002, -0.02], [0.03, tall + 0.014, 0.04], WoodDark, detail: true, shape: NoPaint)); // (the bung)
        // Solid: its foot, its lid and four walls round the loot (a slot can't run into a solid box).
        var i = 0.9 * r;
        p.Add(Box([-i, 0, -i], [i, 0.05, i], hidden: true));
        p.Add(Box([-i, tall - 0.04, -i], [i, tall, i], hidden: true));
        foreach (var s in new[] { -1, 1 })
        {
            double lo = Math.Min(s * (i - wall), s * i), hi = Math.Max(s * (i - wall), s * i);
            p.Add(Box([lo, 0.052, -i], [hi, tall - 0.042, i], hidden: true));
            p.Add(Box([-i + wall + 0.002, 0.052, lo], [i - wall - 0.002, tall - 0.042, hi], hidden: true));
        }
        Feet(p, [-0.125, 0.125], [0.0]);
        p.Stats(30, 160, 2, 2.0);
        var k = i - wall - 0.006;
        p.Set(("storage", new PyDict
        {
            ["slots"] = Py.L(
                new PyDict { ["min"] = Py.L(-k, 0.056, -k), ["max"] = Py.L(k, 0.48, k), ["label"] = "cask" },
                new PyDict { ["min"] = Py.L(-k, 0.49, -k), ["max"] = Py.L(k, tall - 0.046, k), ["label"] = "cask" }),
        }));
        return ("", p);
    }

    public void Make(PartOutput o) => WriteParts(o, Out, [PipeBox(), SaltireFlag(), Thistle(), Caber(), Coo(), Haggis(), Keg(), Cask()]);
}
