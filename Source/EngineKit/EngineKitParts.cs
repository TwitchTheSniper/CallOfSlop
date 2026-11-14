using System.Text.Json;
using System.Text.Json.Nodes;

namespace CallOfSlop.EngineKit;

/// <summary>
/// The Engine Kit's own pieces (docs/vehiclekit/ENGINEKIT.md, "The parts"), written by C# rather than Python: the higher tiers' cylinders, more power for
/// their size, in each of the modular engine's four sizes (Constructs/Parts/EngineKit). <c>--gen-engine-parts [--check]</c> writes them, or with --check only
/// says which are out of date. Run it from the repo root.
/// <para>
/// A tier's cylinder is the plain cylinder (Constructs/Parts/ModularEngine, the part kit's engine_parts) with the same boxes and sockets, so the same
/// geometry the z-fight corrector already settled, a head of the tier's colour (the head already differs in colour from every box it meets, so recolouring
/// it adds no coplanar pair), its own power and thirst (<c>enginePiece.power</c>, <c>thirst</c>: <see cref="ConstructEngine"/> multiplies a cylinder's kW by
/// them), a little more mass and toughness, and its <c>tier</c>.
/// </para>
/// </summary>
public static class EngineKitParts
{
    public const string Folder = "Constructs/Parts/EngineKit";
    public const string PlainFolder = "Constructs/Parts/ModularEngine";

    /// <summary>A tier's cylinder: its name's word, tier, power and thirst a kW (times the plain one's), mass and health factors, its head's colour. Since the tier cylinders were retired (sized parts phase 6) the Engine Kit plans a plain cylinder with the thinner wall whose wider bore gives the grade's power (bored cylinders, 2026-11-14).</summary>
    public readonly record struct Grade(string Word, int Tier, float Power, float Thirst, float Mass, float Health, string Head, string What);

    /// <summary>The plain cylinder is tiers 1 and 2; these are 3, 4 and 5.</summary>
    public static readonly Grade[] Grades =
    [
        new("Bored", 3, 1.3f, 1.05f, 1.1f, 1.0f, "#4d6a7a", "A bored-out cylinder: +30% power, a little thirstier. Tier 3."),
        new("Performance", 4, 1.6f, 1.1f, 1.2f, 1.1f, "#2f5f9e", "A performance cylinder: +60% power for its size. Tier 4."),
        new("Racing", 5, 2.0f, 1.2f, 1.3f, 1.2f, "#a32a2a", "A racing cylinder: twice the power for its size, thirsty. Tier 5."),
    ];

    public static readonly string[] Sizes = ["Small", "Medium", "Large", "Capital"];

    /// <summary>The colour of the plain cylinder's head (the part kit's EngineParts.Rust), the box recoloured.</summary>
    private const string PlainHead = "#7c4a2e";

    /// <summary>The part's name for a size and grade ("Small Racing Cylinder"); the plain one for grade null.</summary>
    public static string CylinderName(string size, Grade? g) => g is { } x ? $"{size} {x.Word} Cylinder" : $"{size} Cylinder";

    public static int Cli(string[] args)
    {
        var check = args.Any(a => a.Equals("--check", StringComparison.OrdinalIgnoreCase));
        var bad = 0;
        var files = 0;
        if (!check)
            Directory.CreateDirectory(Folder);
        foreach (var size in Sizes)
            foreach (var g in Grades)
            {
                var path = Path.Combine(Folder, $"{size}{g.Word}Cylinder.json");
                var text = Make(size, g);
                files++;
                if (check)
                {
                    if (!File.Exists(path) || File.ReadAllText(path).Replace("\r\n", "\n") != text)
                    {
                        Console.WriteLine($"out of date: {path}");
                        bad++;
                    }
                }
                else
                    File.WriteAllText(path, text);
            }
        Console.WriteLine($"{files} files" + (check ? $", {bad} out of date" : " written"));
        return bad;
    }

    /// <summary>One tier's cylinder file, from the plain cylinder of its size.</summary>
    public static string Make(string size, Grade g)
    {
        var plain = Path.Combine(PlainFolder, $"{size}Cylinder.json");
        var root = JsonNode.Parse(File.ReadAllText(plain))!.AsObject();
        var o = new JsonObject
        {
            ["format"] = root["format"]!.GetValue<string>(),
            ["name"] = CylinderName(size, g),
            ["description"] = g.What,
            ["group"] = root["group"]!.GetValue<string>(),
            ["section"] = root["section"]!.GetValue<string>(),
            ["tool"] = root["tool"]!.GetValue<string>(),
            ["attachSeconds"] = root["attachSeconds"]!.GetValue<double>(),
            ["mass"] = Math.Round(root["mass"]!.GetValue<double>() * g.Mass, 1),
            ["health"] = Math.Round(root["health"]!.GetValue<double>() * g.Health),
            ["armour"] = root["armour"]!.GetValue<double>(),
            ["material"] = root["material"]!.GetValue<string>(),
            ["color"] = root["color"]!.GetValue<string>(),
            ["tier"] = g.Tier,
            ["retired"] = true, // (bore replaces them: sized-parts answer 8; old blueprints, saves and shipped vehicles keep working)
            ["centreSockets"] = false,
        };
        var boxes = root["boxes"]!.DeepClone().AsArray();
        var heads = 0;
        foreach (var b in boxes)
            if (b!["color"]!.GetValue<string>() == PlainHead)
            {
                b["color"] = g.Head;
                heads++;
            }
        if (heads != 1)
            throw new InvalidOperationException($"{plain}: expected one head box of {PlainHead}, found {heads} (EngineParts changed: update EngineKitParts.PlainHead)");
        o["boxes"] = boxes;
        o["sockets"] = root["sockets"]!.DeepClone();
        if (root["sizeClass"] is { } sc)
            o["sizeClass"] = sc.GetValue<string>();
        o["role"] = "enginePiece";
        var piece = root["enginePiece"]!.DeepClone().AsObject();
        piece["power"] = g.Power;
        piece["thirst"] = g.Thirst;
        o["enginePiece"] = piece;
        return o.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }).Replace("\r\n", "\n") + "\n";
    }
}
