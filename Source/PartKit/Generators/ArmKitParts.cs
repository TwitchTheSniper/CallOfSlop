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
    public static readonly string[] PressedPieces = ["Arm Joint", "Arm Boom", "Arm Wrist", "Pad Clamp", "Arm Counterweight", "Arm Drive Pack"];

    private const string Out = "Constructs/Parts/Lifters/ArmKit";
    private const string Yellow = "#e8b81c", Yellow2 = "#c99c18", Dark = "#2b2f34", Steel = "#98a1a9", Pin = "#f07818", Weight = "#4c5157", Screen = "#10161c", Lamp = "#ff9a1a";

    // The preset blanks (cm): what each catalogue piece is pressed from on the Arm die.
    public const string JointBlank = "solid 50x50x50 steel", BoomBlank = "hollow 40x40x400 steel", WristBlank = "solid 30x30x40 steel",
        ClampBlank = "solid 60x30x30 steel", WeightBlank = "solid 200x100x10 steel", PackBlank = "solid 60x40x120 steel";

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
    private sealed record Preset(string Name, string Desc, int Pedestals, int JointCm, int UpperW, int UpperL, int ElbowCm, int ForeW, int ForeL, int WristCm, int ClampW, int Weights, int WeightCm, int Packs);

    private static readonly Preset[] Presets =
    [
        new("Bench Arm", "A small kit arm for a bench: about 4 m of reach, a light load.", 0, 30, 25, 150, 25, 20, 150, 20, 40, 0, 10, 0),
        new("Line Arm", "A kit arm for a production line: about 7 m of reach.", 0, 50, 40, 300, 45, 35, 300, 30, 60, 1, 10, 2),
        new("Heavy Arm", "A heavy kit arm on a pedestal: about 9 m of reach, big pieces.", 1, 80, 60, 400, 70, 55, 380, 50, 100, 2, 20, 4),
        new("Yard Arm", "A shipyard kit arm on two pedestals: about 16 m of reach for hull pieces.", 2, 120, 100, 700, 100, 90, 700, 80, 160, 4, 30, 6),
    ];

    private static string Key(string die, string pattern, string blank) => $"Pressed Part (arm.{pattern} {blank})";

    private static PyDict Blueprint(Preset s)
    {
        var steps = new List<object?>();
        int Step(PyDict d) { steps.Add(d); return steps.Count - 1; }
        int On(int on, string socket, string part, string by) => Step(Kw(("part", part), ("on", on), ("socket", socket), ("by", by)));
        var root = Step(Kw(("part", "Arm Pedestal")));
        var core = root;
        if (s.Pedestals == 0)
            steps.Clear();
        if (s.Pedestals == 0)
            core = Step(Kw(("part", "Arm Core")));
        else
        {
            var below = root;
            for (var k = 1; k < s.Pedestals; k++)
                below = On(below, "top", "Arm Pedestal", "bottom");
            core = On(below, "top", "Arm Core", "bottom");
        }
        var j1 = On(core, "chain", Key("arm", "joint", $"solid {s.JointCm}x{s.JointCm}x{s.JointCm} steel"), "base");
        var b1 = On(j1, "tip", Key("arm", "boom", $"hollow {s.UpperW}x{s.UpperW}x{s.UpperL} steel"), "base");
        var j2 = On(b1, "tip", Key("arm", "joint", $"solid {s.ElbowCm}x{s.ElbowCm}x{s.ElbowCm} steel"), "base");
        var b2 = On(j2, "tip", Key("arm", "boom", $"hollow {s.ForeW}x{s.ForeW}x{s.ForeL} steel"), "base");
        var w = On(b2, "tip", Key("arm", "wrist", $"solid {s.WristCm}x{s.WristCm}x{(s.WristCm * 4 / 3 + 2) / 5 * 5} steel"), "base");
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
            holders.Add(k); // (the pedestals are steps 0 .. Pedestals - 1)
        var packs = 0;
        foreach (var hold in holders)
            foreach (var side in new[] { "+z", "-z" })
                if (packs++ < s.Packs)
                    On(hold, side, Key("arm", "drivepack", PackBlank), "base");
        return Kw(("format", "call-of-slop-construct-blueprint/1"), ("name", s.Name), ("description", s.Desc + " Pressed pieces on an Arm Core: it stands as a mast and folds out once powered."), ("steps", steps));
    }

    public void Make(PartOutput o)
    {
        WriteParts(o, Out, [("", Core()), ("", Pedestal()), ("", Joint()), ("", Boom()), ("", WristPiece()), ("", Clamp()), ("", Counterweight()), ("", Pack())]);
        foreach (var s in Presets)
            o.File($"Constructs/Blueprints/{s.Name.Replace(" ", "")}.json", PyJson.Dumps(Blueprint(s), 2) + "\n");
    }
}
