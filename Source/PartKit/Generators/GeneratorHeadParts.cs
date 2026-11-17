using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The Generator Heads (Constructs/Parts/EngineGenerators; docs/sandbox/constructs/power-and-links.md "Engine generators", design
/// notes docs/sandbox/design/engine-generators-notes.md): a power plant turned by an engine's shaft (role <c>generator</c> with
/// <c>"shaft"</c>, its efficiency). Four sizes, one for each Engine Kit preset (small 120 kW, medium 300, large 800, huge 3000): each
/// is rated a little over its preset, so the engine, not the head, sets what the set gives. All four take a small-class shaft on
/// their Drive in (a flywheel's output bolts straight to it; a bigger engine reaches one with a drive shaft) and stand on their feet.
/// </summary>
public sealed class GeneratorHeadParts : IPartGenerator
{
    public string Name => "generator_heads";

    private const string Out = "Constructs/Parts/EngineGenerators";
    private const string Section = "Power plants";
    private const string Body = "#4f6a52", Dark = "#2b2e2c", Copper = "#b8733e", Steel = "#9aa0a6", Plate = "#d9c27a";

    // (word, kW, efficiency, spool s, half width m, length m, kg, health)
    public static readonly (string Word, int Kw, double Eff, double Spool, double Half, double Length, int Kg, int Health)[] Heads =
    [
        ("Small", 150, 0.90, 2, 0.20, 0.60, 240, 500),
        ("Medium", 400, 0.92, 3, 0.28, 0.85, 560, 900),
        ("Large", 1000, 0.94, 4, 0.36, 1.20, 1300, 1800),
        ("Huge", 4000, 0.95, 6, 0.45, 1.90, 4200, 4500),
    ];

    /// <summary>The shaft's height over the feet, every size: a Small engine's flywheel ring (0.45 m) and crank clear the floor the head stands on.</summary>
    public const double Axis = 0.5;

    private static (string, Part) Head(string word, int kw, double eff, double spool, double h, double len, int kg, int health)
    {
        var name = $"{word} Generator Head";
        var p = new Part(name, Small, Section, $"Turned by an engine: bolt a flywheel's output to it, or run a drive shaft to it. Up to {Py.G(kw)} kW into the circuit, {Py.G(eff * 100)}% of the shaft's power. The engine runs as the circuit asks.",
            role: "generator", tool: "wrench", color: Body, group: "Drivetrain");
        var z0 = -len / 2;
        var z1 = len / 2;
        var s = h / 0.2;
        // The housing round the shaft (y 0), a flanged bell at the shaft end, an end cap, and a plinth down to the feet (y -Axis).
        p.Add(Box([-h, -h, z0 + 0.08 * len], [h, h, z1 - 0.06 * len]));
        p.Add(Box([-h * 0.82, -h * 0.82, z0 + 0.02 * len], [h * 0.82, h * 0.82, z0 + 0.08 * len - 0.003], Dark)); // (the bell)
        p.Add(Box([-h * 0.7, -h * 0.7, z1 - 0.06 * len + 0.003], [h * 0.7, h * 0.7, z1], Dark)); // (the end cap)
        p.Add(Box([-h * 0.18, -h * 0.18, z0], [h * 0.18, h * 0.18, z0 + 0.02 * len + 0.003], Steel)); // (the coupling: solid, the drive socket sits on its face)
        foreach (var x in new[] { -1, 1 })
        {
            var (lo, hi) = x < 0 ? (-h * 0.95, -h * 0.5) : (h * 0.5, h * 0.95);
            p.Add(Box([lo, -Axis, z0 + 0.1 * len], [hi, -h - 0.003, z1 - 0.08 * len], Dark)); // (a plinth rail under the housing)
        }
        // Cooling fins down each side, a few mm proud of the housing.
        var fins = Math.Max(3, (int)Math.Round(len / 0.12));
        var pitch = len * 0.8 / fins;
        for (var k = 0; k < fins; k++)
        {
            var z = z0 + 0.12 * len + pitch * (k + 0.5);
            foreach (var x in new[] { -1, 1 })
            {
                var (lo, hi) = x < 0 ? (-h - 0.03 * s, -h + 0.004) : (h - 0.004, h + 0.03 * s);
                p.Add(Box([lo, -h * 0.7, z - 0.012], [hi, h * 0.75, z + 0.012], Dark, detail: true));
            }
        }
        // The terminal box and its copper studs; a rating plate on the side.
        var t = h * 0.35;
        p.Add(Box([-t, h - 0.004, -t], [t, h + t * 0.9, t], Dark));
        foreach (var x in new[] { -0.5, 0, 0.5 })
            p.Add(Box([x * t - 0.02, h + t * 0.9 - 0.004, -0.02], [x * t + 0.02, h + t * 0.9 + 0.035, 0.02], Copper, detail: true));
        p.Add(Box([h + 0.002, h * 0.1, -len * 0.12], [h + 0.018, h * 0.5, len * 0.12], Plate, detail: true));
        // Sockets: the shaft at the bell (a flywheel's output bolts to it), the feet, the sides and the end to fix it by.
        p.Sock("drive", [0, 0, z0], "-z");
        p.Sock("bottom", [0, -Axis, 0], "-y");
        p.Sock("+x", [h, 0, 0], "+x");
        p.Sock("-x", [-h, 0, 0], "-x");
        p.Sock("+z", [0, 0, z1], "+z");
        p.Stats(kg, health, 8);
        p.Set(("collider", "boxes"),
            ("generator", Kw(("rate", kw), ("fuel", "none"), ("spool", spool), ("shaft", eff))),
            ("ports", Py.L(
                BigDriveParts.PowerPort("Power out", "out", [0, R(h + t * 0.9), 0], "+y"),
                Kw(("name", "Drive in"), ("type", "shaft"), ("dir", "in"), ("socket", "drive"), ("at", Py.L(0, 0, R(z0))), ("face", "-z")))));
        return ("", p);
    }

    public void Make(PartOutput o)
    {
        var parts = new List<(string, Part)>();
        foreach (var (word, kw, eff, spool, h, len, kg, health) in Heads)
            parts.Add(Head(word, kw, eff, spool, h, len, kg, health));
        WriteParts(o, Out, parts);
    }
}
