using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The factory kit's own parts (docs/sandbox/design/factory-generator.md 3.5, part kit generator <c>factory_shell</c>), in Constructs/Parts/Factory.
/// Phase 1: the ore bunkers, open-topped concrete bins with chute ports that a generated works keeps its raw and refined stock in. A sized Storage
/// Volume standing in the open spills over its open sides as soon as it is a quarter full (ConstructSeal: an open face's lowest corner under the
/// level pours), so a heap fed by a hauler or a smelter ran out onto the floor as Material Bundles; a bunker is a bin, so it holds what it is rated.
/// The Tipping Bunker is low (its top 1 m up) so a side-tipping Ore Hauler's lip pours over it from the road.
/// Run the part kit's assembling (--gen-parts assembling) after it for the recipes.
/// </summary>
public sealed class FactoryShellParts : IPartGenerator
{
    public string Name => "factory_shell";

    private const string Out = "Constructs/Parts/Factory";
    private const string Concrete = "#8e8a80", Rim = "#5c5a55", Hazard = "#f2b01e";
    private const int UnitsPerM3 = 400; // (Recycling.json binUnitsPerCubicMetre: a bunker holds what a Storage Volume of its inside would)

    /// <summary>An open-topped bin: walls <paramref name="t"/> thick round a floor, chute in and out on its rim, a socket under the middle.</summary>
    private static Part Bunker(string name, double w, double h, double t, string desc, bool hazard)
    {
        var c = Small;
        var p = new Part(name, c, "Factory", desc, role: "storage", material: "concrete", color: Concrete, group: "Production");
        double x0 = -w / 2, x1 = w / 2;
        p.Add(Box([x0, 0, x0], [x1, t, x1])); // the floor
        foreach (var (a, b) in new[] { (x0, x0 + t), (x1 - t, x1) })
            p.Add(Box([a, t, x0], [b, h, x1])); // the walls along z
        foreach (var (a, b) in new[] { (x0, x0 + t), (x1 - t, x1) })
            p.Add(Box([x0 + t, t, a], [x1 - t, h - 0.004, b])); // the walls along x, a few mm under the others' tops
        // A steel rim round the top (drawn), and on a tipping bunker a hazard band on the road side's face.
        foreach (var (a, b) in new[] { (x0 - 0.012, x0 + t + 0.012), (x1 - t - 0.012, x1 + 0.012) })
            p.Add(Box([a, h - 0.003, x0 - 0.012], [b, h + 0.03, x1 + 0.012], Rim, detail: true));
        foreach (var (a, b) in new[] { (x0 - 0.009, x0 + t + 0.009), (x1 - t - 0.009, x1 + 0.009) })
            p.Add(Box([x0 + t + 0.013, h - 0.003, a], [x1 - t - 0.013, h + 0.027, b], Rim, detail: true));
        if (hazard)
            p.Add(Box([x0 + 0.05, h * 0.35, x1 + 0.003], [x1 - 0.05, h * 0.65, x1 + 0.009], Hazard, detail: true));
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Sock("+x", [x1, h * 0.5, 0], "+x");
        p.Sock("-x", [x0, h * 0.5, 0], "-x");
        double i0 = x0 + t + 0.005, i1 = x1 - t - 0.005;
        var volume = (i1 - i0) * (i1 - i0) * (h - 0.05 - t - 0.005);
        p.Set(("storage", Kw(("bulk", Kw(("material", "any"), ("capacity", (int)Math.Round(volume * UnitsPerM3)), ("chuteLoad", 160),
                ("min", Vec(i0, t + 0.005, i0)), ("max", Vec(i1, h - 0.05, i1)))))),
            ("ports", Py.L(
                Kw(("name", "Chute in"), ("type", "chute"), ("dir", "in"), ("at", Vec(x0 + t * 0.5, h, 0)), ("face", "+y")),
                Kw(("name", "Chute out"), ("type", "chute"), ("dir", "out"), ("at", Vec(x1 - t * 0.5, h, 0)), ("face", "+y")))));
        var area = w * w + 4 * w * h;
        p.Stats(area * t * 2400 * 0.5, area * 900, 20);
        return p;
    }

    public void Make(PartOutput o)
    {
        WriteParts(o, Out,
        [
            ("", Bunker("Ore Bunker 2 m", 2.0, 1.5, 0.12, "An open concrete bin for bulk stock: chute in and out on its rim, or tip a load in from above.", false)),
            ("", Bunker("Ore Bunker 3 m", 3.0, 2.0, 0.15, "A big open concrete bin for bulk stock: chute in and out on its rim, or tip a load in from above.", false)),
            ("", Bunker("Tipping Bunker 3 m", 3.0, 1.0, 0.15, "A low open bin a tipper pours into from the road beside it: its top is 1 m up. Chute it on to the stock.", true)),
        ]);
    }
}
