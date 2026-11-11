using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The bandit vehicles' parts: Constructs/Parts/Bandit (docs/vehiclekit/VEHICLEKIT.md). The fuel drum a technical carries on its bed,
/// and what bandits bolt on for show: spikes, skulls and a hubcap. A spike and a trophy stand on one "base" socket and point up
/// their own +y, so the kit turns them outward. A drum and a spike are round: a hidden solid box for the collider and the build
/// rules, a cylinder drawn over it, as Steel Tube does. Bone isn't paintable; the rest takes war paint.
/// Was tools/constructs/bandit_parts.py. Recipes come from the part kit's assembling (--gen-parts assembling); run it after this one.
/// </summary>
public sealed class BanditParts : IPartGenerator
{
    public string Name => "bandit_parts";

    private const string Out = "Constructs/Parts/Bandit";
    private const string Iron = "#5a5048", Dark = "#2c2a28", Bone = "#d8cdb0", Horn = "#3a3026", Drum = "#6e3f26", Hoop = "#4e3a2a";
    private const string Section = "Body panels";
    private const string Group = "Lights and Details";

    private static void BaseSocket(Part p) => p.Sock("base", [0, 0, 0], "-y");

    private static (string, Part) Spike(string name, double length, double radius, double mass)
    {
        var p = new Part(name, Small, Section, "A sharpened steel stake. It looks worse than it hits.", color: Iron, group: Group);
        p.Add(Box([-radius, 0, -radius], [radius, length, radius], hidden: true));
        p.Add(Box([-radius, 0, -radius], [radius, length, radius], Iron, detail: true, shape: new() { ["shape"] = "cylinder", ["segments"] = 8, ["taper"] = Py.L(0.96, 0.96), ["front"] = "+y" }));
        p.Add(Box([-radius * 1.5, 0.002, -radius * 1.5], [radius * 1.5, 0.022, radius * 1.5], Dark, detail: true, shape: new() { ["shape"] = "cylinder", ["segments"] = 8, ["front"] = "+y" }));
        BaseSocket(p);
        p.Stats(mass, 45, 4, 0.5);
        return ("", p);
    }

    private static PyDict NoPaint => new() { ["paintable"] = false };

    /// <summary>A skull's dome, jaw and sockets, its face towards -z, sitting at height y.</summary>
    private static void SkullAt(Part p, double y)
    {
        p.Add(Box([-0.075, y + 0.03, -0.09], [0.075, y + 0.17, 0.09], Bone, detail: true, shape: new() { ["shape"] = "sphere", ["segments"] = 10, ["paintable"] = false }));
        p.Add(Box([-0.05, y, -0.085], [0.05, y + 0.06, -0.01], Bone, detail: true, shape: NoPaint));
        foreach (var x in new[] { -0.034, 0.034 })
            p.Add(Box([x - 0.018, y + 0.082, -0.096], [x + 0.018, y + 0.112, -0.05], Dark, detail: true, shape: NoPaint));
        p.Add(Box([-0.01, y + 0.056, -0.094], [0.01, y + 0.076, -0.06], Dark, detail: true, shape: NoPaint));
    }

    private static (string, Part) Skull(string name, string desc, bool horns, double mass)
    {
        var p = new Part(name, Small, Section, desc, color: Bone, group: Group);
        var top = 0.2;
        p.Add(Box([-0.02, 0, -0.02], [0.02, top, 0.02], Iron));
        p.Add(Box([-0.05, top, -0.04], [0.05, top + 0.14, 0.05], hidden: true));
        SkullAt(p, top);
        if (horns)
            foreach (var s in new[] { -1, 1 })
            {
                double lo = Math.Min(s * 0.07, s * 0.2), hi = Math.Max(s * 0.07, s * 0.2);
                p.Add(Box([lo, top + 0.09, -0.025], [hi, top + 0.135, 0.02], Horn, detail: true, shape: NoPaint));
                double lo2 = Math.Min(s * 0.17, s * 0.215), hi2 = Math.Max(s * 0.17, s * 0.215);
                p.Add(Box([lo2, top + 0.13, -0.025], [hi2, top + 0.27, 0.02], Horn, detail: true, shape: NoPaint));
            }
        BaseSocket(p);
        p.Stats(mass, 30, 1, 0.5);
        return ("", p);
    }

    private static (string, Part) Hubcap()
    {
        var p = new Part("Hubcap Plate", Small, Section, "A stolen hubcap. Keeps the rain off nothing.", color: "#9aa0a6", group: Group);
        var r = 0.2;
        p.Add(Box([-r, 0, -r], [r, 0.03, r], hidden: true));
        p.Add(Box([-r, 0, -r], [r, 0.03, r], "#9aa0a6", detail: true, shape: new() { ["shape"] = "cylinder", ["segments"] = 16, ["front"] = "+y" }));
        p.Add(Box([-0.08, 0.03, -0.08], [0.08, 0.05, 0.08], Dark, detail: true, shape: new() { ["shape"] = "cylinder", ["segments"] = 12, ["front"] = "+y" }));
        BaseSocket(p);
        p.Stats(2, 25, 1, 0.5);
        return ("", p);
    }

    /// <summary>A short ladder for a floor over small wheels: a Boarding Ladder's rig, 0.8 m long so it clears the ground.</summary>
    private static (string, Part) Ladder()
    {
        var p = new Part("Scrap Ladder", Small, "Boarding", "A short ladder of welded rebar. Climb aboard from outside.", role: "panel", tool: "wrench", color: "#6a5a48", group: "Panels");
        foreach (var s in new[] { -1, 1 })
            p.Add(Box([Math.Min(s * 0.21, s * 0.25), -0.8, -0.08], [Math.Max(s * 0.21, s * 0.25), 0, -0.04]));
        foreach (var y in new[] { -0.15, -0.45, -0.72 })
            p.Add(Box([-0.21, y - 0.015, -0.075], [0.21, y + 0.015, -0.045]));
        foreach (var s in new[] { -1, 1 })
            p.Add(Box([s * 0.23 - 0.02, -0.04, -0.04], [s * 0.23 + 0.02, 0, 0])); // (its brackets: the socket's on their face)
        p.Sock("back", [0, 0, 0], "+z");
        p.Stats(6, 150, 3, 1.5);
        p.Set(("boarding", true));
        return ("", p);
    }

    private static (string, Part) FuelDrum()
    {
        var p = new Part("Fuel Drum", Small, "Fuel tanks", "Holds 60 l of fuel in thin steel, out in the open. Feeds an engine through a pipe. Burns well.",
            role: "fueltank", tool: "wrench", color: Drum, group: "Power and Fuel");
        double r = 0.29, h = 0.44;
        p.Add(Box([-r, -h, -r], [r, h, r], hidden: true));
        p.Add(Box([-r, -h, -r], [r, h, r], Drum, detail: true, shape: new() { ["shape"] = "cylinder", ["segments"] = 14, ["front"] = "+y" })); // (14 sides: 16 are too many corners for a hull, and a drum collides as its round shape)
        foreach (var y in new[] { -0.16, 0.16 })
            p.Add(Box([-r - 0.008, y - 0.015, -r - 0.008], [r + 0.008, y + 0.015, r + 0.008], Hoop, detail: true, shape: new() { ["shape"] = "cylinder", ["segments"] = 16, ["front"] = "+y" }));
        p.Add(Box([0.1, h, -0.04], [0.18, h + 0.03, 0.04], Dark, detail: true));
        p.Add(Box([-0.04, -0.34, -r - 0.03], [0.04, -0.26, -r + 0.01], Dark, detail: true));
        foreach (var (name, at) in new (string, double[])[] { ("+x", [r, 0, 0]), ("-x", [-r, 0, 0]), ("+y", [0, h, 0]), ("-y", [0, -h, 0]), ("+z", [0, 0, r]), ("-z", [0, 0, -r]) })
            p.Sock(name, at, name);
        p.Stats(38, 55, 1, 2.0);
        p.Set(("ports", Py.L(
            new PyDict { ["name"] = "Fuel out", ["type"] = "pipe", ["dir"] = "out", ["fluid"] = "fuel", ["at"] = Py.L(0.0, -0.3, -r), ["face"] = "-z" },
            new PyDict { ["name"] = "Fill", ["type"] = "pipe", ["dir"] = "in", ["fluid"] = "fuel", ["at"] = Py.L(0.14, h, 0.0), ["face"] = "+y" })),
            ("fuelTank", new PyDict { ["capacity"] = 60 }));
        return ("", p);
    }

    public void Make(PartOutput o) => WriteParts(o, Out,
    [
        FuelDrum(),
        Ladder(),
        Spike("Iron Spike", 0.35, 0.045, 1.5),
        Spike("Long Iron Spike", 0.6, 0.05, 2.5),
        Skull("Skull Trophy", "A skull on a stake. A warning, or a boast.", false, 2),
        Skull("Horned Skull", "A horned skull on a stake. Somebody's bull, probably.", true, 3),
        Hubcap(),
    ]);
}
