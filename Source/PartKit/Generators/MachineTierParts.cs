using static CallOfSlop.PartKit.PartKitMath;
using static CallOfSlop.PartKit.MachineLookShort;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The block machines' tier pieces (design page drawing 21; MachineBlocks.cs, role "tier"): four small pieces bolted on a Core's tier
/// slots, each raising the machine one tier when every lower one is there: the Power Coupling (2 Workshop), the Coolant Loop (3 Industrial),
/// the Servo Rack (4 Military) and the Plasma Conditioner (5 Plasma). The tier multiplies the Core's rate, draw and bin by the machine
/// tiers' Curve (<see cref="MachineTiers.Multipliers"/>). Each wears its tier's colours (<see cref="TierStyle.Palette"/>) and as many pips
/// as its tier on its face, so a player reads a machine's tier from across a hall; each shows its Core's work (a glow, a spin, a stroke).
///
/// A piece is 0.38 m across, 0.5 m high and 0.3 m deep, its mount on its +z face (the Core's tier slot); the hidden box is its collider.
/// </summary>
public sealed class MachineTierParts : IPartGenerator
{
    public string Name => "machine_tier_parts";

    private const string Out = "Constructs/Parts/TierPieces";
    private const double W = 0.19, H = 0.5, Deep = 0.3; // half across, height, depth
    private const string Copper = "#c07a3a";
    private const string Coolant = "#2f62b5";

    /// <summary>A tier slot's height on a Core's face (its mount's middle over the piece's foot): a Core's "tier" socket sits this far over where the piece's foot goes.</summary>
    public const double MountY = 0.25;

    private static Dictionary<string, string> Pal(int tier) => TierStyle.Palette[tier];

    /// <summary>What every piece has: the hidden collider, the mount plate and its bolts, the tier plate on its face with a pip a tier, the socket and keys.</summary>
    private static Part Piece(string name, int tier, string desc, double mass, double health)
    {
        var pal = Pal(tier);
        var p = new Part(name, Small, "Extrusion", desc, group: "Production", color: pal["body"]);
        p.Add(Box([-W, 0, -Deep], [W, H, 0], hidden: true));
        // The mount plate, 3 mm off the Core's face, and a bolt at each corner.
        p.Add(L([-W, 0.003, -0.022], [W, H - 0.003, -0.003], MachineLook.Base));
        foreach (var (x, y) in new[] { (-W + 0.035, 0.035), (W - 0.035, 0.035), (-W + 0.035, H - 0.035), (W - 0.035, H - 0.035) })
            p.Add(L([x - 0.016, y - 0.016, -0.04], [x + 0.016, y + 0.016, -0.022], MachineLook.Steel, "steel", shape: Kw(("shape", "cylinder"), ("segments", 6), ("front", "-z"))));
        p.Sock("mount", [0, MountY, 0], "+z", kind: "tier");
        p.Stats(mass, health, 6, 8);
        p.Set(("machineBlock", Kw(("role", "tier"), ("tier", tier))), ("centreSockets", false), ("collider", "boxes"));
        return p;
    }

    /// <summary>The tier plate on a piece's face at height <paramref name="y"/> (its front at <paramref name="z"/>): a dark plate and a pip a tier in the tier's accent.</summary>
    private static void TierPlate(Part p, int tier, double y, double z)
    {
        var pal = Pal(tier);
        double half = 0.03 + 0.025 * tier;
        p.Add(L([-half, y - 0.028, z - 0.014], [half, y + 0.028, z + 0.004], MachineLook.Ink));
        for (var k = 0; k < tier; k++)
        {
            var x = (k - (tier - 1) / 2.0) * 0.05;
            p.Add(L([x - 0.014, y - 0.014, z - 0.028], [x + 0.014, y + 0.014, z - 0.014], pal["accent"]));
        }
    }

    /// <summary>
    /// The Power Coupling (tier 2): a heavy feeder box in the Workshop's blue, copper bus bars out of its top, finned sides, a cable gland on
    /// its face ringed by a lamp that glows while its machine works.
    /// </summary>
    private static Part PowerCoupling()
    {
        const int t = 2;
        var pal = Pal(t);
        var p = Piece("Power Coupling", t,
            "A tier piece: bolt it on a machine Core's tier slot for tier 2 (Workshop). Its machine works twice as fast, holds twice as much and draws 1.8 times the power.",
            40, 300);
        p.Add(L([-0.16, 0.04, -0.24], [0.16, 0.4, -0.022], pal["body"], paintable: true));
        // Fins down each side.
        foreach (var sx in new[] { -1, 1 })
            foreach (var y in new[] { 0.1, 0.17, 0.24, 0.31 })
                p.Add(L([sx > 0 ? 0.16 - 0.004 : -0.176, y - 0.012, -0.22], [sx > 0 ? 0.176 : -0.16 + 0.004, y + 0.012, -0.04], MachineLook.Base));
        // The bus bars: two copper bars out of the top on insulators.
        foreach (var x in new[] { -0.07, 0.07 })
        {
            p.Add(L([x - 0.03, 0.396, -0.17], [x + 0.03, 0.43, -0.09], "#e8e6df", shape: Kw(("shape", "cylinder"), ("segments", 8), ("front", "+y"))));
            p.Add(L([x - 0.018, 0.43, -0.2], [x + 0.018, 0.47, -0.06], Copper, "copper"));
        }
        // The cable gland on the face, its lamp ring round it (it glows while the machine works), and the hazard band over it.
        p.Add(L([-0.07, 0.11, -0.3], [0.07, 0.25, -0.236], MachineLook.Base, shape: Kw(("shape", "cylinder"), ("segments", 12), ("front", "-z"))));
        p.Add(L([-0.035, 0.145, -0.3], [0.035, 0.215, -0.286], MachineLook.Ink, shape: Kw(("shape", "cylinder"), ("segments", 8), ("front", "-z"))));
        p.Add(MachineLook.Work(L([-0.095, 0.085, -0.256], [0.095, 0.275, -0.238], pal["light"], shape: Kw(("shape", "tube"), ("segments", 16), ("inner", 0.78), ("front", "-z"))), "glow"));
        MachineLook.Hazard(p, [-0.15, 0.3, -0.252], [0.15, 0.322, -0.238]);
        TierPlate(p, t, 0.36, -0.238);
        return p;
    }

    /// <summary>
    /// The Coolant Loop (tier 3): a pump on a bracket under a finned radiator in the Industrial's grey and yellow, blue coolant pipes in a
    /// loop up its face. The pump's impeller turns, its sight glass glows and the radiator vents while its machine works.
    /// </summary>
    private static Part CoolantLoop()
    {
        const int t = 3;
        var pal = Pal(t);
        var p = Piece("Coolant Loop", t,
            "A tier piece: bolt it on a machine Core's tier slot, beside a Power Coupling, for tier 3 (Industrial). Its machine works four times as fast as tier 1, holds 3.5 times as much and draws 3.2 times the power.",
            55, 340);
        // The pump: a drum across the foot, its impeller cap on the +x end turning, a sight glass on its face.
        p.Add(L([-0.15, 0.03, -0.25], [0.15, 0.2, -0.05], pal["body"], paintable: true, shape: Kw(("shape", "cylinder"), ("segments", 16), ("front", "+x"))));
        p.Add(MachineLook.Work(L([0.148, 0.065, -0.2], [0.182, 0.165, -0.1], pal["accent"], shape: Kw(("shape", "cylinder"), ("segments", 8), ("front", "+x"))), "spin", "+x"));
        p.Add(L([-0.182, 0.07, -0.19], [-0.148, 0.16, -0.11], MachineLook.Base, shape: Kw(("shape", "cylinder"), ("segments", 8), ("front", "-x"))));
        p.Add(MachineLook.Work(L([-0.05, 0.08, -0.262], [0.05, 0.14, -0.24], "#35d0ff"), "glow"));
        // The radiator: a dark block, fins across its face, a vent on its top that steams while the machine works.
        p.Add(L([-0.17, 0.22, -0.21], [0.17, 0.46, -0.024], MachineLook.Base));
        foreach (var y in new[] { 0.25, 0.29, 0.33, 0.37, 0.41 })
            p.Add(L([-0.16, y - 0.008, -0.222], [0.16, y + 0.008, -0.21], pal["frame"]));
        p.Add(L([-0.08, 0.46, -0.16], [0.08, 0.485, -0.07], pal["frame"]));
        p.Add(MachineLook.Work(L([-0.06, 0.485, -0.15], [0.06, 0.5, -0.08], MachineLook.Ink), "smoke"));
        // The coolant pipes: up each side of the face from the pump to the radiator's top, and across its top.
        foreach (var x in new[] { -0.13, 0.13 })
            p.Add(L([x - 0.018, 0.13, -0.27], [x + 0.018, 0.45, -0.234], Coolant, shape: Kw(("shape", "cylinder"), ("segments", 8), ("front", "+y"))));
        p.Add(L([-0.148, 0.432, -0.272], [0.148, 0.466, -0.236], Coolant, shape: Kw(("shape", "cylinder"), ("segments", 8), ("front", "+x"))));
        foreach (var x in new[] { -0.13, 0.13 })
            p.Add(L([x - 0.026, 0.19, -0.276], [x + 0.026, 0.21, -0.228], pal["accent"])); // (pipe clamps)
        TierPlate(p, t, 0.36, -0.236);
        return p;
    }

    /// <summary>
    /// The Servo Rack (tier 4): three servo rams standing in a Military olive frame under a cross head, a drive motor on its face and a red
    /// lamp. The rams and the cross head stroke and the motor turns while its machine works.
    /// </summary>
    private static Part ServoRack()
    {
        const int t = 4;
        var pal = Pal(t);
        var p = Piece("Servo Rack", t,
            "A tier piece: bolt it on a machine Core's tier slot, with a Power Coupling and a Coolant Loop, for tier 4 (Military). Its machine works eight times as fast as tier 1, holds eight times as much and draws 5.6 times the power.",
            70, 420);
        // The frame: a foot, two side posts, all in the military's frame tone.
        p.Add(L([-0.18, 0.003, -0.28], [0.18, 0.05, -0.022], pal["frame"]));
        foreach (var sx in new[] { -1, 1 })
            p.Add(L([sx > 0 ? 0.14 : -0.18, 0.05, -0.27], [sx > 0 ? 0.18 : -0.14, 0.47, -0.03], pal["body"], paintable: true));
        // The rams: barrels standing on the foot, rods out of their tops, a cross head over the rods; rods and head stroke together.
        foreach (var x in new[] { -0.075, 0.0, 0.075 })
        {
            p.Add(L([x - 0.03, 0.05, -0.18], [x + 0.03, 0.3, -0.12], pal["trim"], shape: Kw(("shape", "cylinder"), ("segments", 10), ("front", "+y"))));
            p.Add(L([x - 0.036, 0.28, -0.186], [x + 0.036, 0.3, -0.114], MachineLook.Base, shape: Kw(("shape", "cylinder"), ("segments", 10), ("front", "+y")))); // (its gland)
            p.Add(MachineLook.Work(L([x - 0.013, 0.3, -0.163], [x + 0.013, 0.39, -0.137], MachineLook.Steel, "steel", shape: Kw(("shape", "cylinder"), ("segments", 6), ("front", "+y"))), "stroke", "+y", 0.05));
        }
        p.Add(MachineLook.Work(L([-0.13, 0.39, -0.2], [0.13, 0.43, -0.1], pal["accent"]), "stroke", "+y", 0.05));
        // The drive motor on the face, its fan cap turning, its cable up the left post; the lamp in the right post.
        p.Add(L([-0.06, 0.08, -0.3], [0.06, 0.2, -0.18], MachineLook.Base, shape: Kw(("shape", "cylinder"), ("segments", 12), ("front", "-z"))));
        p.Add(MachineLook.Work(L([-0.04, 0.1, -0.3], [0.04, 0.18, -0.288], pal["accent"], shape: Kw(("shape", "cylinder"), ("segments", 6), ("front", "-z"))), "spin", "-z"));
        p.Add(L([-0.17, 0.1, -0.29], [-0.15, 0.44, -0.27], MachineLook.Ink));
        p.Add(MachineLook.Work(L([0.145, 0.38, -0.29], [0.175, 0.44, -0.268], pal["light"]), "glow"));
        p.Add(L([-0.14, 0.43, -0.28], [0.14, 0.49, -0.24], pal["frame"])); // (the front lintel between the posts, clear of the cross head's stroke)
        TierPlate(p, t, 0.46, -0.28);
        return p;
    }

    /// <summary>
    /// The Plasma Conditioner (tier 5): a violet plasma column on a dark base inside three field rings, a cap with a cyan emitter and a
    /// conduit down its side. The column and the emitter glow and the rings turn while its machine works.
    /// </summary>
    private static Part PlasmaConditioner()
    {
        const int t = 5;
        var pal = Pal(t);
        var p = Piece("Plasma Conditioner", t,
            "A tier piece: bolt it on a machine Core's tier slot, with the three lower tier pieces, for tier 5 (Plasma). Its machine works sixteen times as fast as tier 1, holds sixteen times as much and draws 9.6 times the power.",
            60, 480);
        p.Add(L([-0.17, 0.003, -0.28], [0.17, 0.1, -0.022], pal["body"], paintable: true));
        p.Add(L([-0.13, 0.1, -0.26], [0.13, 0.13, -0.04], pal["frame"]));
        p.Add(MachineLook.Work(L([-0.1, 0.13, -0.25], [0.1, 0.4, -0.05], pal["heat"], shape: Kw(("shape", "cylinder"), ("segments", 16), ("front", "+y"))), "glow"));
        foreach (var y in new[] { 0.18, 0.265, 0.35 })
            p.Add(MachineLook.Work(L([-0.132, y - 0.018, -0.282], [0.132, y + 0.018, -0.018], pal["plate2"], shape: Kw(("shape", "tube"), ("segments", 16), ("inner", 0.8), ("front", "+y"))), "spin", "+y"));
        // Three posts hold the cap over the column.
        foreach (var (x, z) in new[] { (-0.15, -0.26), (0.15, -0.26), (0.0, -0.04) })
            p.Add(L([x - 0.014, 0.1, z - 0.014], [x + 0.014, 0.42, z + 0.014], pal["trim"]));
        p.Add(L([-0.16, 0.4, -0.28], [0.16, 0.46, -0.024], pal["body"], paintable: true));
        p.Add(MachineLook.Work(L([-0.05, 0.46, -0.2], [0.05, 0.484, -0.1], pal["accent"], shape: Kw(("shape", "cylinder"), ("segments", 10), ("front", "+y"))), "glow"));
        // The conduit down the right from the cap to the base, glowing with the column.
        p.Add(MachineLook.Work(L([0.152, 0.1, -0.14], [0.176, 0.4, -0.116], pal["accent"]), "glow"));
        TierPlate(p, t, 0.055, -0.28);
        return p;
    }

    public void Make(PartOutput o) => WriteParts(o, Out, [("", PowerCoupling()), ("", CoolantLoop()), ("", ServoRack()), ("", PlasmaConditioner())]);
}
