using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The Sandbox's free rotation parts (Constructs/Parts/Free): joints that turn with no motor, spring or power
/// (docs/sandbox/CONSTRUCTS.md, "Free rotation"; Source/Constructs/ConstructLinear.cs). A Free Swivel turns about the line through
/// its leaf socket (a lazy Susan), a Free Pivot about an axis across it (a pendulum). A Lock signal holds it; limits are a setting,
/// off to begin with. Was tools/constructs/free_parts.py. Run the part kit's assembling (--gen-parts assembling) after it for the recipes.
/// </summary>
public sealed class FreeParts : IPartGenerator
{
    public string Name => "free_parts";

    private const string Out = "Constructs/Parts/Free";
    private const string Base = "#3b4046", Collar = "#2f9e8f", Cue = "#1c1c1c";
    private static readonly (Cls C, int G)[] Sizes = [(Small, 1), (Small, 2), (Small, 3), (Medium, 1), (Large, 1), (Capital, 1)];

    private static PyDict Num(string key, string label, int lo, int hi, int step, string tip, int def) => new()
    {
        ["key"] = key, ["label"] = label, ["group"] = "Limits", ["unit"] = "deg", ["min"] = lo, ["max"] = hi, ["step"] = step, ["tooltip"] = tip, ["default"] = def,
    };

    private static string Word(Cls c, int g) => c == Small ? $"{g}x{g}" : c.Name;

    private static Part Make(string kind, Cls c, int g)
    {
        var w = g * c.Cell;
        var pivot = kind == "pivot";
        var h = pivot || c == Small && g == 1 ? w : w * 0.5; // (a 1x1 is square: a small part welds to its side)
        var y = h / 2;
        var lip = 0.008 * c.X;
        var head = pivot ? "Free Pivot" : "Free Swivel";
        var p = new Part($"{head} {Word(c, g)}", c, "Free rotation", pivot
            ? "Swings freely across its leaf, no motor. Lock signal holds it. Limits in its settings."
            : "Turns freely about its leaf, no motor. Lock signal holds it. Limits in its settings.", role: "joint", tool: "wrench", color: Base, group: "Joints");
        p.Add(Box([-w / 2, -y, -w / 2], [w / 2, y, w / 2], Base, shape: new() { ["side"] = "base" }));
        // The moving side: a plate over the top and a bar across it, for how far it has turned.
        p.Add(Box([-w * 0.42, y, -w * 0.42], [w * 0.42, y + lip * 3, w * 0.42], Collar, detail: true, shape: new() { ["side"] = "leaf", ["paintable"] = false }));
        p.Add(Box([-w * 0.06, y + lip * 3, -w * 0.4], [w * 0.06, y + lip * 4, w * 0.4], Cue, detail: true, shape: new() { ["side"] = "leaf", ["paintable"] = false }));
        if (pivot)
            foreach (var sx in new[] { -1, 1 }) // the pin's ends, on the axis
            {
                double a = sx * (w / 2), b = sx * (w / 2 + lip * 2);
                p.Add(Box([Math.Min(a, b), -w * 0.12, -w * 0.12], [Math.Max(a, b), w * 0.12, w * 0.12], "#4a4f55", detail: true, shape: new() { ["side"] = "base", ["paintable"] = false })); // (base side: not the moving side's colour)
            }
        p.Sock("base", [0, -y, 0], "-y");
        p.Sock("top", [0, y, 0], "+y");
        p.Sock("+z", [0, 0, w / 2], "+z");
        p.Sock("-z", [0, 0, -w / 2], "-z");
        p.Sock("+x", [w / 2, 0, 0], "+x");
        p.Sock("-x", [-w / 2, 0, 0], "-x");
        if (c == Small)
        {
            var k = g * g * 2;
            p.Stats(1.5 * k * (pivot ? 1.3 : 1), 80 + 15 * k, 3 + g, 1.0 + 0.5 * g);
        }
        else
        {
            var area = 6 * w * w;
            p.Stats(area * c.KgM2, area * c.HealthM2, c.Armour + 8, c.Seconds * 1.5);
        }
        p.Set(("power", 0), ("joint", new PyDict { ["type"] = "swivel", ["axis"] = pivot ? "+x" : "+y", ["pivot"] = pivot ? Py.L(0.0, 0.0, 0.0) : Py.L(0.0, y, 0.0), ["leaf"] = "top" }), ("config", Py.L(
            new PyDict { ["key"] = "limited", ["type"] = "toggle", ["label"] = "Use limits", ["group"] = "Limits", ["default"] = false, ["tooltip"] = "Off: it turns forever." },
            Num("min", "Back to", -180, 180, 1, "How far it turns one way, with limits on.", -90),
            Num("max", "Forward to", -180, 180, 1, "How far it turns the other way, with limits on.", 90))), ("centreSockets", false));
        return p;
    }

    public void Make(PartOutput o) =>
        WriteParts(o, Out, from kind in new[] { "swivel", "pivot" } from s in Sizes select (char.ToUpperInvariant(kind[0]) + kind[1..], Make(kind, s.C, s.G)));
}
