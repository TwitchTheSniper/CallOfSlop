using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The arm kit (Sean 2026-11-24, docs/sandbox/constructs/production.md "Arm kit"; ArmKit.cs): a modular loader arm built from pieces.
/// An Arm Core (the 2 m block with the slewing bearing, its screen and power) stands on Arm Pedestals; its chain stands on its top as a
/// mast while it is built (shoulder Joint, upper Boom, elbow Joint, fore Boom, Wrist, Pad Clamp) and folds out into the arm once it
/// has power. Counterweights chain on its back, Drive Packs bolt on its sides. Every piece but the Core and the Pedestal is pressed
/// to order on the Arm die (PressParts: its patterns are "like" bodies of the catalogue pieces written here), so its size, and with it
/// its rating, is the order's. The presets (Bench, Line, Heavy, Yard) are blueprints of pressed pieces.
/// Each like part stands with its origin on its base socket, along +y (ArmKitRender poses them so).
/// </summary>
public sealed class ArmKitParts : IPartGenerator
{
    public string Name => "arm_kit";

    /// <summary>The chain pieces: pressed on the Arm die, never an Assembler's row (Assembling.PressedNow).</summary>
    public static readonly string[] PressedPieces = ["Arm Joint", "Arm Boom", "Arm Wrist", "Pad Clamp", "Arm Counterweight", "Arm Drive Pack",
        "Spreader", "Vacuum Head", "Magnet Head", "Fork", "Fitter Head", "Arm Knuckle"];

    private const string Out = "Constructs/Parts/Lifters/ArmKit";
    private const string Yellow = "#e8b81c", Yellow2 = "#c99c18", Dark = "#2b2f34", Steel = "#98a1a9", Pin = "#f07818", Weight = "#4c5157", Screen = "#10161c", Lamp = "#ff9a1a";

    // The preset blanks (cm): what each catalogue piece is pressed from on the Arm die.
    public const string JointBlank = "solid 50x50x50 steel", BoomBlank = "hollow 40x40x400 steel", WristBlank = "solid 30x30x40 steel",
        ClampBlank = "solid 60x30x30 steel", WeightBlank = "solid 200x100x10 steel", PackBlank = "solid 60x40x120 steel",
        SpreaderBlank = "solid 150x30x30 steel", VacuumBlank = "solid 80x20x80 steel", MagnetBlank = "solid 60x25x60 steel", ForkBlank = "solid 80x60x100 steel",
        FitterBlank = "solid 60x40x40 steel", KnuckleBlank = "solid 40x40x40 steel";

    private static Part Core()
    {
        var p = new Part("Arm Core", Small, "Arm kit",
            "The heart of a modular loader arm: a 2 m block with the slewing bearing on its top, its screen and its power. Build the chain on its top (a Joint, a Boom, a Joint, a Boom, a Wrist and a Pad Clamp, pressed to any size on the Arm die): it stands as a mast and folds out into the arm once it has power. Counterweights go on its back, Drive Packs on its sides, Arm Pedestals under it. Its reach, load chart and speed come from the pieces.",
            group: "Production", color: Yellow);
        const double e = 1.0, h = 2.0;
        p.Add(Box([-e, 0, -e], [e, h, e], Dark));
        MachineLook.Hazard(p, [-e - 0.006, 0.12, -e + 0.1], [-e + 0.006, 0.3, e - 0.1]);
        MachineLook.Hazard(p, [-e + 0.1, 0.12, e - 0.006], [e - 0.1, 0.3, e + 0.006]);
        MachineLook.Hazard(p, [-e + 0.1, 0.12, -e - 0.006], [e - 0.1, 0.3, -e + 0.006]);
        // The yellow cladding round its upper half, the slewing bearing's ring and gear on top, its bolts.
        p.Add(MachineLook.D([-e - 0.008, 0.9, -e - 0.008], [e + 0.008, h - 0.15, e + 0.008], Yellow, paintable: true));
        p.Add(MachineLook.D([-0.82, h - 0.004, -0.82], [0.82, h + 0.06, 0.82], Steel, "steel", shape: Kw(("shape", "cylinder"), ("segments", 24), ("front", "+y"))));
        p.Add(MachineLook.D([-0.7, h + 0.056, -0.7], [0.7, h + 0.1, 0.7], Dark, shape: Kw(("shape", "cylinder"), ("segments", 24), ("front", "+y"))));
        foreach (var (bx, bz) in new[] { (0.88, 0.0), (-0.88, 0.0), (0.0, 0.88), (0.0, -0.88), (0.62, 0.62), (-0.62, 0.62), (0.62, -0.62), (-0.62, -0.62) })
            p.Add(MachineLook.D([bx - 0.03, h - 0.004, bz - 0.03], [bx + 0.03, h + 0.03, bz + 0.03], Steel, "steel"));
        // Its screen on the +x face, a cabinet door below it, a power gland and an amber lamp at a top corner.
        p.Add(MachineLook.D([e - 0.004, 1.05, -0.45], [e + 0.05, 1.7, 0.45], Dark, "steel"));
        p.Add(MachineLook.D([e + 0.046, 1.22, -0.4], [e + 0.058, 1.6, 0.4], Screen));
        p.Add(MachineLook.D([e - 0.004, 0.36, -0.5], [e + 0.02, 0.86, 0.5], Weight));
        p.Add(MachineLook.D([e + 0.016, 0.58, 0.36], [e + 0.04, 0.66, 0.44], Steel, "steel"));
        p.Add(MachineLook.D([-0.3, 0.05, e - 0.004], [-0.1, 0.25, e + 0.08], Steel, "steel", shape: Kw(("shape", "cylinder"), ("segments", 12), ("front", "+z"))));
        p.Add(MachineLook.Work(MachineLook.D([e - 0.16, h - 0.004, e - 0.16], [e - 0.04, h + 0.12, e - 0.04], Lamp, shape: Kw(("shape", "cylinder"), ("segments", 12), ("front", "+y")))!, "glow"));
        p.Set(("panels", Py.L(Kw(("source", "lifter"), ("at", Py.L(e + 0.064, 1.41, 0.0)), ("size", Py.L(0.74, 0.32)),
            ("face", "+x"), ("up", "+y"), ("rows", 5), ("range", 10)))));
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Sock("chain", [0, h, 0], "+y");
        p.Sock("weight", [-e, 1, 0], "-x");
        p.Sock("+z", [0, 1, e], "+z");
        p.Sock("-z", [0, 1, -e], "-z");
        p.Stats(2600, 3000, 10, seconds: 10.0);
        p.Set(("tier", 3), ("power", 2000), ("onBreak", "disable"), ("collider", "boxes"));
        p.Set(("lifter", Kw(("kind", "arm"), ("kit", true), ("base", Py.L(0.0, h, 0.0)), ("ring", h - 0.4), ("reach", Py.L(1.0, 4.0)), ("up", 3.0), ("down", 2.3),
            ("payload", 500), ("piece", Py.L(1.0, 4.0)), ("speed", 3.0), ("handle", 0.5), ("arm", Py.L(2.0, 2.0)))));
        p.Set(("config", Py.L(
            Kw(("key", "liftMode"), ("type", "choice"), ("label", "Works"), ("group", "Lifter"), ("options", Py.L("Auto", "Station only", "Carry only", "Off")), ("default", 0),
                ("tooltip", "Auto: every job in its reach. Station only: fits parts at Building Stations. Carry only: carries between machines. Off: stands still.")),
            Kw(("key", "carryHeight"), ("label", "Carry height"), ("group", "Lifter"), ("unit", "m"), ("min", 0.0), ("max", 8.0), ("step", 0.1), ("default", 0.4),
                ("tooltip", "How high it lifts a piece over the higher end of its move before it turns. Never past what its arm reaches.")))));
        return p;
    }

    private static Part Pedestal()
    {
        var p = new Part("Arm Pedestal", Small, "Arm kit",
            "A 2 m block an Arm Core stands on: up to four raise an arm over a bay's machines. Drive Packs bolt on its sides as on the Core's.",
            group: "Production", color: Dark);
        const double e = 1.0, h = 2.0;
        p.Add(Box([-e, 0, -e], [e, h, e], Dark));
        foreach (var y in new[] { 0.004, h - 0.12 })
            p.Add(MachineLook.D([-e - 0.008, y, -e - 0.008], [e + 0.008, y + 0.116, e + 0.008], Weight));
        foreach (var (sx, sz) in new[] { (1, 1), (1, -1), (-1, 1), (-1, -1) })
            p.Add(MachineLook.D([sx * e - (sx > 0 ? 0.14 : -0.006) - 0.006, 0.12, sz * e - (sz > 0 ? 0.14 : -0.006) - 0.006], [sx * e + (sx > 0 ? 0.006 : 0.14) , h - 0.12, sz * e + (sz > 0 ? 0.006 : 0.14)], Yellow2, paintable: true));
        MachineLook.Hazard(p, [e - 0.004, 0.9, -0.6], [e + 0.008, 1.1, 0.6]);
        MachineLook.Hazard(p, [-e - 0.008, 0.9, -0.6], [-e + 0.004, 1.1, 0.6]);
        p.Sock("top", [0, h, 0], "+y");
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Sock("+z", [0, 1, e], "+z");
        p.Sock("-z", [0, 1, -e], "-z");
        p.Sock("+x", [e, 1, 0], "+x");
        p.Sock("-x", [-e, 1, 0], "-x");
        p.Stats(1900, 2600, 10, seconds: 8.0);
        p.Set(("armPiece", "pedestal"), ("collider", "boxes"));
        return p;
    }

    /// <summary>A chain piece's base and tip sockets (origin on the base, the piece along +y).</summary>
    private static void Chain(Part p, double len, bool tip = true)
    {
        p.Sock("base", [0, 0, 0], "-y");
        if (tip)
            p.Sock("tip", [0, len, 0], "+y");
    }

    private static Part Joint()
    {
        var p = new Part("Arm Joint", Small, "Arm kit",
            "A shoulder or an elbow of a modular arm: a steel hub with its pin across. Pressed to any size on the Arm die: its moment grows with its side cubed, its top speed falls as it grows.",
            group: "Production", color: Dark);
        const double a = 0.5, r = a / 2;
        p.Add(Box([-r, 0, -r], [r, a, r], Dark));
        p.Add(MachineLook.D([-r - 0.008, a * 0.2, -r * 0.7], [r + 0.008, a * 0.8, r * 0.7], Yellow, paintable: true)); // (the cheeks' cladding)
        p.Add(MachineLook.D([-a * 0.3, a * 0.2, -r - 0.04], [a * 0.3, a * 0.8, r + 0.04], Steel, "steel", shape: Kw(("shape", "cylinder"), ("segments", 16), ("front", "+z")))); // the pin
        p.Add(MachineLook.D([-a * 0.16, a * 0.34, -r - 0.06], [a * 0.16, a * 0.66, r + 0.06], Pin, shape: Kw(("shape", "cylinder"), ("segments", 12), ("front", "+z")))); // its caps
        p.Add(MachineLook.D([-r * 0.8, -0.004, -r * 0.8], [r * 0.8, 0.04, r * 0.8], Steel, "steel")); // (the base flange)
        p.Add(MachineLook.D([-r * 0.8, a - 0.04, -r * 0.8], [r * 0.8, a + 0.004, r * 0.8], Steel, "steel")); // (the tip flange)
        Chain(p, a);
        p.Stats(1100 * a * a, 900, 12, seconds: 6.0);
        p.Set(("armPiece", "joint"), ("collider", "boxes"));
        return p;
    }

    /// <summary>A Knuckle (part 3): a joint after the forearm whose drop Boom hangs plumb, so the hand reaches down into a bay over a wall.</summary>
    private static Part Knuckle()
    {
        var p = new Part("Arm Knuckle", Small, "Arm kit",
            "A modular arm's third bend: fit it on the forearm, then a drop Boom and the Wrist. The drop hangs plumb whatever the arm does, so the hand reaches down into a bay over its wall without losing reach. Pressed to any size on the Arm die.",
            group: "Production", color: Dark);
        const double a = 0.4, r = a / 2;
        p.Add(Box([-r, 0, -r], [r, a, r], Dark));
        p.Add(MachineLook.D([-r - 0.008, a * 0.2, -r * 0.7], [r + 0.008, a * 0.8, r * 0.7], Pin)); // (orange cheeks: the third bend, told from the joints)
        p.Add(MachineLook.D([-r - 0.04, a * 0.25, -a * 0.25], [r + 0.04, a * 0.75, a * 0.25], Steel, "steel", shape: Kw(("shape", "cylinder"), ("segments", 16), ("front", "+x")))); // its pin, across the other way
        p.Add(MachineLook.D([-r * 0.8, -0.004, -r * 0.8], [r * 0.8, 0.04, r * 0.8], Steel, "steel"));
        p.Add(MachineLook.D([-r * 0.8, a - 0.04, -r * 0.8], [r * 0.8, a + 0.004, r * 0.8], Steel, "steel"));
        MachineLook.Hazard(p, [-r * 0.6, a * 0.1, r - 0.004], [r * 0.6, a * 0.2, r + 0.008]);
        Chain(p, a);
        p.Stats(1100 * a * a, 900, 12, seconds: 6.0);
        p.Set(("armPiece", "knuckle"), ("collider", "boxes"));
        return p;
    }

    /// <summary>A Rail Carriage (part 3): under an Arm Core or its Pedestals, its four wheels on a crane rail along its z.</summary>
    private static Part Carriage()
    {
        var p = new Part("Rail Carriage", Small, "Arm kit",
            "A wheeled bogie an Arm Core (or its Pedestals) stands on: set it on a Small crane rail (Crane Rail Small 1 m sections laid end to end on the floor) along its length and the whole arm runs along the rail to each job, as far as the rail runs.",
            group: "Production", color: Dark);
        const double h = 0.6, l = 1.0, w = 0.8;
        p.Add(Box([-0.12, 0, -l], [0.12, 0.15, l], Dark, hidden: true)); // (its footing on the rail's top: drawn by its wheels and guide)
        p.Add(Box([-w, 0.15, -l], [w, h, l], Dark));
        foreach (var sx in new[] { -1.0, 1.0 })
        {
            p.Add(MachineLook.D([sx * w - (sx > 0 ? 0 : 0.008) - (sx > 0 ? 0.004 : 0), 0.22, -l + 0.1], [sx * w + (sx > 0 ? 0.008 : 0.004), h - 0.08, l - 0.1], Yellow, paintable: true)); // (its side skirts)
            foreach (var z in new[] { -0.7, 0.7 })
            {
                var wheel = MachineLook.D([sx * 0.09 - 0.025, 0.003, z - 0.2], [sx * 0.09 + 0.025, 0.403, z + 0.2], Steel, "steel", shape: Kw(("shape", "cylinder"), ("segments", 16), ("front", "+x")))!;
                wheel["roll"] = true;
                p.Add(wheel);
            }
        }
        foreach (var sz in new[] { -1.0, 1.0 })
        {
            MachineLook.Hazard(p, sz > 0 ? [-w + 0.1, 0.25, l - 0.004] : [-w + 0.1, 0.25, -l - 0.008], sz > 0 ? [w - 0.1, 0.4, l + 0.008] : [w - 0.1, 0.4, -l + 0.004]);
            p.Add(MachineLook.D([-0.3, 0.05, sz > 0 ? l - 0.004 : -l - 0.1], [0.3, 0.14, sz > 0 ? l + 0.1 : -l + 0.004], Weight)); // (its rail sweeps)
        }
        p.Add(MachineLook.Work(MachineLook.D([w - 0.12, h - 0.004, l - 0.12], [w - 0.04, h + 0.06, l - 0.04], Lamp)!, "glow"));
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Sock("top", [0, h, 0], "+y");
        p.Stats(900, 1500, 10, seconds: 6.0);
        p.Set(("armPiece", "railCarriage"), ("collider", "boxes"));
        return p;
    }

    private static Part Boom()
    {
        var p = new Part("Arm Boom", Small, "Arm kit",
            "A link of a modular arm: a square steel tube with lugs at its ends. Pressed to any width and length on the Arm die: its moment grows with its section, its weight with both.",
            group: "Production", color: Yellow);
        const double w = 0.4, r = w / 2, len = 4;
        p.Add(Box([-r, 0, -r], [r, len, r], Yellow));
        foreach (var (y0, y1) in new[] { (0.0, 0.3), (len - 0.3, len) })
            p.Add(MachineLook.D([-r - 0.01, y0 + 0.004, -r - 0.01], [r + 0.01, y1 - 0.004, r + 0.01], Dark));
        // A seam down two faces, a lighter band (the lacing's plate) at its middle, hazard near its tip.
        p.Add(MachineLook.D([-r - 0.005, 0.32, -0.02], [-r + 0.004, len - 0.32, 0.02], Yellow2));
        p.Add(MachineLook.D([r - 0.004, 0.32, -0.02], [r + 0.005, len - 0.32, 0.02], Yellow2));
        p.Add(MachineLook.D([-r - 0.006, len * 0.45, -r - 0.006], [r + 0.006, len * 0.55, r + 0.006], Yellow2, paintable: true));
        MachineLook.Hazard(p, [-r - 0.008, len - 0.6, -r - 0.008], [r + 0.008, len - 0.4, r + 0.008]);
        p.Add(MachineLook.D([-r * 0.6, len * 0.3, r + 0.004], [r * 0.6, len * 0.36, r + 0.05], Steel, "steel")); // (the ram's lug)
        Chain(p, len);
        p.Stats(500, 1200, 10, seconds: 6.0);
        p.Set(("armPiece", "boom"), ("collider", "boxes"));
        return p;
    }

    private static Part WristPiece()
    {
        var p = new Part("Arm Wrist", Small, "Arm kit",
            "A modular arm's wrist: a rotator that keeps its head level under the forearm. Pressed to size on the Arm die; it limits the elbow's moment as a joint of its side does.",
            group: "Production", color: Dark);
        const double a = 0.3, r = a / 2, len = 0.4;
        p.Add(Box([-r, 0, -r], [r, len, r], Dark));
        p.Add(MachineLook.D([-r - 0.01, len * 0.3, -r - 0.01], [r + 0.01, len * 0.7, r + 0.01], Steel, "steel", shape: Kw(("shape", "cylinder"), ("segments", 16), ("front", "+y"))));
        p.Add(MachineLook.D([-r * 0.5, len * 0.42, r + 0.006], [r * 0.5, len * 0.58, r + 0.03], Pin));
        Chain(p, len);
        p.Stats(150, 500, 10, seconds: 4.0);
        p.Set(("armPiece", "wrist"), ("collider", "boxes"));
        return p;
    }

    private static Part Clamp()
    {
        var p = new Part("Pad Clamp", Small, "Arm kit",
            "A modular arm's head: a bar with two pads that close on a piece. Pressed to size on the Arm die: its width sets what it holds, up to 120 t.",
            group: "Production", color: Pin);
        const double w = 0.6, h = 0.3, d = 0.3;
        p.Add(Box([-w / 2, 0, -d / 2], [w / 2, h * 0.5, d / 2], Dark));
        foreach (var s in new[] { -1, 1 })
            p.Add(Box([s > 0 ? w / 2 - 0.08 : -w / 2, h * 0.5 - 0.004, -d / 2 + 0.02], [s > 0 ? w / 2 : -w / 2 + 0.08, h, d / 2 - 0.02], Pin));
        p.Add(MachineLook.D([-w * 0.3, -0.004, -d * 0.3], [w * 0.3, h * 0.2, d * 0.3], Steel, "steel"));
        MachineLook.Hazard(p, [-w / 2 + 0.1, h * 0.2, d / 2 - 0.004], [w / 2 - 0.1, h * 0.4, d / 2 + 0.008]);
        Chain(p, h, tip: false);
        p.Stats(120, 400, 8, seconds: 4.0);
        p.Set(("armPiece", "padClamp"), ("collider", "boxes"));
        return p;
    }


    // ---- Heads (part 2): each hangs on a Wrist's tip by its "base" socket, its width along x (what it holds: ArmKit.HeadRating). A spare sits on
    // a Head Stand's slot by the same socket; the arm swaps by itself when the next piece needs another.
    private static Part Head(string name, string desc, string kind, double w, double h, double d, Action<Part> look, double kg)
    {
        var p = new Part(name, Small, "Arm kit", desc, group: "Production", color: Pin);
        p.Add(Box([-0.1, 0, -0.1], [0.1, 0.06, 0.1], Steel)); // (its quick-change coupler)
        p.Add(Box([-w / 2, 0.056, -d / 2], [w / 2, 0.056 + h * 0.4, d / 2], Dark));
        look(p);
        Chain(p, h, tip: false);
        p.Stats(kg, 400, 8, seconds: 4.0);
        p.Set(("armPiece", kind), ("collider", "boxes"));
        return p;
    }

    private static Part Spreader() => Head("Spreader", "A modular arm's head for long pieces: a beam with a twist-lock hook at each end. Takes beams, hull sections and anything 2 m long or more, up to three widths across; holds a fifth more than a clamp of its width.",
        "spreader", 1.5, 0.3, 0.3, p =>
        {
            foreach (var s in new[] { -1, 1 })
            {
                p.Add(Box([s * 0.75 - (s > 0 ? 0.1 : 0), 0.18, -0.1], [s * 0.75 + (s > 0 ? 0 : 0.1), 0.3, 0.1], Yellow)); // (its end blocks, above)
                p.Add(MachineLook.D([s * 0.68 - 0.03, 0.3 - 0.004, -0.03], [s * 0.68 + 0.03, 0.36, 0.03], Pin)); // (its twist locks)
            }
            MachineLook.Hazard(p, [-0.5, 0.06, 0.15 - 0.004], [0.5, 0.15, 0.15 + 0.008]);
        }, 160);

    private static Part Vacuum() => Head("Vacuum Head", "A modular arm's head for plates and sheets: a frame of suction cups. Takes a piece whose thin side is a tenth of its long side or less; holds less than a clamp (four tenths).",
        "vacuum", 0.8, 0.2, 0.8, p =>
        {
            foreach (var x in new[] { -0.28, 0.28 })
                foreach (var z in new[] { -0.28, 0.28 })
                    p.Add(MachineLook.D([x - 0.09, 0.136, z - 0.09], [x + 0.09, 0.2, z + 0.09], Screen, shape: Kw(("shape", "cylinder"), ("segments", 12), ("front", "+y")))); // (its cups)
            p.Add(MachineLook.D([0.2, 0.06, -0.38], [0.36, 0.13, -0.3], Steel, "steel")); // (its vacuum pump)
        }, 90);

    private static Part Magnet() => Head("Magnet Head", "A modular arm's head for steel: an electromagnet disc. Takes any steel or iron piece up to three widths across, whatever its shape; holds eight tenths of a clamp.",
        "magnet", 0.6, 0.25, 0.6, p =>
        {
            p.Add(MachineLook.D([-0.3, 0.15, -0.3], [0.3, 0.25, 0.3], Weight, shape: Kw(("shape", "cylinder"), ("segments", 24), ("front", "+y"))));
            p.Add(MachineLook.Work(MachineLook.D([0.2, 0.056 + 0.1 - 0.004, 0.2], [0.26, 0.16, 0.26], Lamp)!, "glow"));
            MachineLook.Hazard(p, [-0.25, 0.07, 0.3 - 0.004], [0.25, 0.13, 0.3 + 0.008]);
        }, 140);

    private static Part ForkHead() => Head("Fork", "A modular arm's head of two tines: lifts crates, pallets and blocks from under. Takes anything whose long side is within three widths; holds half again what a clamp does.",
        "fork", 0.8, 0.6, 0.76, p =>
        {
            foreach (var x in new[] { -0.3, 0.3 })
            {
                p.Add(Box([x - 0.06, 0.24 - 0.004, -0.38], [x + 0.06, 0.6, -0.26], Steel)); // (its upright)
                p.Add(Box([x - 0.06, 0.48, -0.26], [x + 0.06, 0.54, 0.37], Steel)); // (a tine)
            }
            p.Add(MachineLook.D([-0.4, 0.3, -0.38 - 0.004], [0.4, 0.4, -0.3], Yellow, paintable: true));
        }, 180);

    private static Part Fitter() => Head("Fitter Head", "A modular arm's head that fits parts at a Building Station: a clamp with a turning wrist and a torque driver. What a clamp of its width takes; the arm then fits as a Module Fitter does.",
        "fitterHead", 0.6, 0.4, 0.4, p =>
        {
            p.Add(MachineLook.D([-0.2, 0.216, -0.2], [0.2, 0.3, 0.2], Steel, "steel", shape: Kw(("shape", "cylinder"), ("segments", 16), ("front", "+y"))));
            foreach (var s in new[] { -1, 1 })
                p.Add(Box([s > 0 ? 0.22 : -0.3, 0.296, -0.12], [s > 0 ? 0.3 : -0.22, 0.4, 0.12], Pin));
            p.Add(MachineLook.D([-0.03, 0.296, 0.14], [0.03, 0.38, 0.2], Yellow, paintable: true)); // (its driver)
        }, 130);

    /// <summary>A stand of three spare heads in reach of an arm kit (ArmKit.Heads): each slot takes a head by its base socket.</summary>
    private static Part HeadStand()
    {
        var p = new Part("Head Stand", Small, "Arm kit",
            "A rack of three spare heads beside a modular arm: put a head on each slot. An Arm Core in reach swaps to the head its next piece needs by itself (5 s), or by its Head setting.",
            group: "Production", color: Dark);
        const double e = 1.5, h = 1.0;
        p.Add(Box([-e, 0, -0.4], [e, 0.1, 0.4], Weight));
        foreach (var x in new[] { -1.4, 1.4 })
            p.Add(Box([x - 0.06, 0.096, -0.06], [x + 0.06, h - 0.1, 0.06], Yellow2));
        p.Add(Box([-e, h - 0.1, -0.3], [e, h, 0.3], Dark));
        double[] slots = [-0.95, 0.3, 1.15]; // (room for a 1.5 m Spreader, a Vacuum Head and a Fork side by side)
        for (var k = 0; k < 3; k++)
        {
            var x = slots[k];
            foreach (var (a, b) in new[] { (-0.14, -0.106), (0.106, 0.14) })
                p.Add(MachineLook.D([x + a, h - 0.004, -0.14], [x + b, h + 0.03, 0.14], Steel, "steel")); // (its slot's seat cheeks, either side of the coupler)
            p.Sock($"slot{k}", [x, h, 0], "+y"); // (a head's coupler sits through its seat ring)
        }
        MachineLook.Hazard(p, [-1.2, 0.2, 0.06 - 0.004 + 0.0], [1.2, 0.3, 0.06 + 0.008]);
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Sock("mount", [0, 1, -0.4], "-z"); // (bolted on an Arm Core's or Pedestal's side, its floor level with theirs)
        p.Stats(260, 600, 8, seconds: 6.0);
        p.Set(("armPiece", "headStand"), ("collider", "boxes"));
        return p;
    }

    private static Part Counterweight()
    {
        var p = new Part("Arm Counterweight", Small, "Arm kit",
            "Steel plates on an Arm Core's back: each holds its slewing bearing against the arm's load (3.25 m of lever). Up to four chain one behind the other; pressed thicker on the Arm die, it weighs more.",
            group: "Production", color: Weight);
        // (pressed along x: its thickness; 2 m tall, 1 m wide; its origin on its base socket's face)
        const double t = 0.1, h = 2.0, w = 1.0;
        p.Add(Box([-t, 0, -w / 2], [0, h, w / 2], Weight));
        MachineLook.Hazard(p, [-t + 0.01, h - 0.3, w / 2 - 0.004], [-0.01, h - 0.1, w / 2 + 0.008]);
        MachineLook.Hazard(p, [-t + 0.01, h - 0.3, -w / 2 - 0.008], [-0.01, h - 0.1, -w / 2 + 0.004]);
        p.Add(MachineLook.D([-t * 0.7, h - 0.004, -0.1], [-t * 0.3, h + 0.08, 0.1], Steel, "steel")); // (its lifting eye)
        p.Sock("base", [0, 1, 0], "+x");
        p.Sock("back", [-t, 1, 0], "-x");
        p.Stats(1570, 2000, 14, seconds: 5.0);
        p.Set(("armPiece", "counterweight"), ("collider", "boxes"));
        return p;
    }

    private static Part Pack()
    {
        var p = new Part("Arm Drive Pack", Small, "Arm kit",
            "A hydraulic power pack bolted on an Arm Core's or Pedestal's side: 3 kW a metre of its height, so a heavy arm keeps its speed. Up to eight; pressed taller on the Arm die, it gives more.",
            group: "Production", color: Dark);
        const double w = 0.6, d = 0.4, h = 1.2;
        p.Add(Box([-w / 2, 0, 0], [w / 2, h, d], Dark));
        for (var k = 0; k < 4; k++)
        {
            var y = 0.2 + k * 0.22;
            p.Add(MachineLook.D([-w / 2 + 0.04, y, d - 0.004], [w / 2 - 0.04, y + 0.06, d + 0.02], Steel, "steel")); // (cooling fins)
        }
        p.Add(MachineLook.D([-w / 2 - 0.006, h - 0.25, 0.04], [w / 2 + 0.006, h - 0.04, d - 0.04], Yellow, paintable: true));
        p.Add(MachineLook.Work(MachineLook.D([w / 2 - 0.12, h - 0.004, d - 0.12], [w / 2 - 0.04, h + 0.06, d - 0.04], Lamp)!, "glow"));
        p.Add(MachineLook.D([-0.08, -0.004, d * 0.3], [0.08, 0.1, d * 0.7], Steel, "steel", shape: Kw(("shape", "cylinder"), ("segments", 12), ("front", "+y")))); // (its hose gland)
        p.Sock("base", [0, 1, 0], "-z");
        p.Stats(380, 700, 8, seconds: 5.0);
        p.Set(("armPiece", "drivePack"), ("collider", "boxes"));
        return p;
    }

    // ---- The presets: blueprints of pressed pieces on a Core (Sean's four: Bench, Line, Heavy, Yard).
    private sealed record Preset(string Name, string Desc, int Pedestals, int JointCm, int UpperW, int UpperL, int ElbowCm, int ForeW, int ForeL, int WristCm, int ClampW, int Weights, int WeightCm, int Packs, bool Stand = false, int KnuckleCm = 0, int DropW = 0, int DropL = 0, int Rails = 0, bool Fitter = false);

    private static readonly Preset[] Presets =
    [
        new("Bench Arm", "A small kit arm for a bench: about 4 m of reach, a light load.", 0, 30, 25, 150, 25, 20, 150, 20, 40, 0, 10, 0),
        new("Line Arm", "A kit arm for a production line: about 7 m of reach.", 0, 50, 40, 300, 45, 35, 300, 30, 60, 1, 10, 2),
        new("Line Arm Heads", "A Line Arm with a Head Stand of three spares (a Spreader, a Vacuum Head and a Fork): it swaps to what each piece needs.", 0, 50, 40, 300, 45, 35, 300, 30, 60, 1, 10, 1, Stand: true),
        new("Knuckle Arm", "A kit arm on a pedestal with a Knuckle: its last 2 m hang plumb, to load machine beds over their guards.", 1, 75, 55, 400, 65, 45, 400, 35, 60, 1, 10, 2, KnuckleCm: 45, DropW: 30, DropL: 150),
        new("Rail Arm", "A Line Arm on a Rail Carriage: it runs along 24 m of crane rail to each job.", 0, 50, 40, 300, 45, 35, 300, 30, 60, 1, 10, 2, Rails: 24),
        new("Heavy Arm", "A heavy kit arm on a pedestal: about 9 m of reach, big pieces.", 1, 80, 60, 400, 70, 55, 380, 50, 100, 2, 20, 4),
        new("Yard Arm", "A shipyard kit arm on two pedestals: about 16 m of reach for hull pieces.", 2, 120, 100, 700, 100, 90, 700, 80, 160, 4, 30, 6),
        // (part 4: one for each fixed arm it replaces, at least its reach and its load at full reach: tools/MIGRATING.md, ArmKitMigrate)
        new("Kit Loader Arm", "The kit arm a Loader Arm became: 4 m of reach, 500 kg.", 0, 30, 25, 200, 25, 20, 190, 20, 40, 0, 10, 0),
        new("Kit Workshop Arm", "The kit arm a Workshop Loader Arm became: 5 m of reach, 1 t.", 0, 40, 30, 250, 40, 30, 240, 35, 50, 0, 10, 1),
        new("Kit Industrial Arm", "The kit arm an Industrial Loader Arm became: 6 m of reach, 2 t.", 0, 60, 45, 290, 55, 40, 280, 35, 70, 2, 20, 2),
        new("Kit Military Arm", "The kit arm a Military Loader Arm became: 8 m of reach, 4 t.", 0, 85, 60, 390, 80, 55, 370, 70, 90, 3, 30, 4),
        new("Kit Plasma Arm", "The kit arm a Plasma Loader Arm became: 10 m of reach, 8 t.", 0, 110, 80, 480, 105, 75, 460, 90, 120, 4, 40, 6),
        new("Kit Long Arm", "The kit arm a Long Loader Arm became: 14 m of reach, 6 t, on a pedestal.", 1, 120, 90, 690, 115, 95, 680, 95, 120, 4, 40, 6),
        new("Kit Heavy Arm", "The kit arm a Heavy Loader Arm became: 14 m of reach, 16 t, on a pedestal.", 1, 155, 115, 660, 145, 110, 640, 125, 160, 4, 70, 8),
        new("Kit Shipyard Arm", "The kit arm a Shipyard Loader Arm became: 18 m of reach, 24 t, on two pedestals.", 2, 200, 150, 850, 190, 150, 840, 150, 190, 4, 240, 8),
        new("Kit Module Fitter", "The kit arm a Module Fitter S became: a Fitter Head, 8 m of reach, 20 t.", 0, 130, 100, 380, 120, 100, 360, 110, 140, 4, 60, 6, Fitter: true),
    ];

    private static string Key(string die, string pattern, string blank) => $"Pressed Part (arm.{pattern} {blank})";

    private static PyDict Blueprint(Preset s)
    {
        var steps = new List<object?>();
        int Step(PyDict d) { steps.Add(d); return steps.Count - 1; }
        int On(int on, string socket, string part, string by) => Step(Kw(("part", part), ("on", on), ("socket", socket), ("by", by)));
        int core;
        var firstPedestal = 0;
        if (s.Rails > 0)
        {
            // A run of Small crane rail (1 m sections end to end along z), the carriage on its middle section, the arm on the carriage.
            var rail = Step(Kw(("part", "Crane Rail Small 1 m")));
            var mid = rail;
            for (var k = 1; k < s.Rails; k++)
            {
                rail = On(rail, "+z", "Crane Rail Small 1 m", "-z");
                if (k == s.Rails / 2)
                    mid = rail;
            }
            var under = On(mid, "+y.0.0", "Rail Carriage", "bottom");
            firstPedestal = steps.Count;
            for (var k = 0; k < s.Pedestals; k++)
                under = On(under, "top", "Arm Pedestal", "bottom");
            core = On(under, "top", "Arm Core", "bottom");
        }
        else if (s.Pedestals == 0)
            core = Step(Kw(("part", "Arm Core")));
        else
        {
            var below = Step(Kw(("part", "Arm Pedestal")));
            for (var k = 1; k < s.Pedestals; k++)
                below = On(below, "top", "Arm Pedestal", "bottom");
            core = On(below, "top", "Arm Core", "bottom");
        }
        var j1 = On(core, "chain", Key("arm", "joint", $"solid {s.JointCm}x{s.JointCm}x{s.JointCm} steel"), "base");
        var b1 = On(j1, "tip", Key("arm", "boom", $"hollow {s.UpperW}x{s.UpperW}x{s.UpperL} steel"), "base");
        var j2 = On(b1, "tip", Key("arm", "joint", $"solid {s.ElbowCm}x{s.ElbowCm}x{s.ElbowCm} steel"), "base");
        var b2 = On(j2, "tip", Key("arm", "boom", $"hollow {s.ForeW}x{s.ForeW}x{s.ForeL} steel"), "base");
        var wristOn = b2;
        if (s.KnuckleCm > 0)
        {
            var kn = On(b2, "tip", Key("arm", "knuckle", $"solid {s.KnuckleCm}x{s.KnuckleCm}x{s.KnuckleCm} steel"), "base");
            wristOn = On(kn, "tip", Key("arm", "boom", $"hollow {s.DropW}x{s.DropW}x{s.DropL} steel"), "base");
        }
        var w = On(wristOn, "tip", Key("arm", "wrist", $"solid {s.WristCm}x{s.WristCm}x{(s.WristCm * 4 / 3 + 2) / 5 * 5} steel"), "base");
        if (s.Fitter)
            On(w, "tip", Key("arm", "fitter", $"solid {s.ClampW}x{s.ClampW * 2 / 3 / 5 * 5}x{s.ClampW * 2 / 3 / 5 * 5} steel"), "base");
        else
            On(w, "tip", Key("arm", "padclamp", $"solid {s.ClampW}x{s.ClampW / 2}x{s.ClampW / 2} steel"), "base");
        var back = core;
        string sock = "weight";
        for (var k = 0; k < s.Weights; k++)
        {
            back = On(back, sock, Key("arm", "counterweight", $"solid 200x100x{s.WeightCm} steel"), "base");
            sock = "back";
        }
        var holders = new List<int> { core };
        for (var k = 0; k < s.Pedestals; k++)
            holders.Add(firstPedestal + k); // (the pedestals are the steps from firstPedestal on)
        var packs = 0;
        foreach (var hold in holders)
            foreach (var side in new[] { "+z", "-z" })
                if (packs++ < s.Packs)
                    On(hold, side, Key("arm", "drivepack", PackBlank), "base");
        if (s.Stand)
        {
            var stand = On(core, "-z", "Head Stand", "mount");
            On(stand, "slot0", Key("arm", "spreader", SpreaderBlank), "base");
            On(stand, "slot1", Key("arm", "vacuum", VacuumBlank), "base");
            On(stand, "slot2", Key("arm", "fork", ForkBlank), "base");
        }
        return Kw(("format", "call-of-slop-construct-blueprint/1"), ("name", s.Name), ("description", s.Desc + " Pressed pieces on an Arm Core: it stands as a mast and folds out once powered."), ("steps", steps));
    }

    public void Make(PartOutput o)
    {
        WriteParts(o, Out, [("", Core()), ("", Pedestal()), ("", Joint()), ("", Boom()), ("", WristPiece()), ("", Clamp()), ("", Counterweight()), ("", Pack()),
            ("", Spreader()), ("", Vacuum()), ("", Magnet()), ("", ForkHead()), ("", Fitter()), ("", HeadStand()),
            ("", Knuckle()), ("", Carriage())]);
        foreach (var s in Presets)
            o.File($"Constructs/Blueprints/{s.Name.Replace(" ", "")}.json", PyJson.Dumps(Blueprint(s), 2) + "\n");
    }
}
