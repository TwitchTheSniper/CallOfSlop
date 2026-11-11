namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The tool upgrades: Constructs/ToolUpgrades.json and the module parts in Constructs/Parts/Upgrades (docs/sandbox/TOOL-UPGRADES.md). Was
/// tools/constructs/upgrade_parts.py. <see cref="Assembling"/> takes the modules' recipes from <see cref="Recipes"/>: run it after this.
///
/// Each stat (ToolStat in Source/Constructs/ToolUpgrades.cs, in that order) has a base value, what every survivor starts with, and levels,
/// each made by fitting one module part (F on it lying loose). An Assembler builds the modules (<see cref="Recipes"/>). Some are taught by
/// factions ("teaches" in Story/Factions), which this doesn't write.
/// </summary>
public sealed class UpgradeParts : IPartGenerator
{
    public string Name => "upgrade_parts";

    private const string Format = "call-of-slop-tool-upgrades/1";
    private const string About = "Tool upgrades (docs/sandbox/TOOL-UPGRADES.md). Written by the part kit (--gen-parts upgrade_parts): fix it, not this file. "
        + "Each stat: 'base' is where every survivor starts, 'creative' what creative gives, 'levels' each made by fitting its "
        + "module 'part' (F on it, lying loose), in order of value; a higher Mk may be fitted over a lower. A function is base 0 "
        + "and one level of 1. The stats' order is ToolStat's (Source/Constructs/ToolUpgrades.cs).";

    // (stat, name, about, base, creative, colour, [(part, file, value, mass)]); value and mass keep their Python type.
    private static readonly (string Stat, string Name, string About, int Base, int Creative, string Colour, (string Part, string File, object Value, object Mass)[] Levels)[] Stats =
    [
        ("lift", "Lift", "the heaviest thing the physics gun takes, in kg; its pull rises with it (30 N a kg)", 5000, 20000000, "#5c6fa8",
            [("Lift Coil Mk 2", "LiftCoilMk2", 10000, 3), ("Lift Coil Mk 3", "LiftCoilMk3", 20000, 4), ("Lift Coil Mk 4", "LiftCoilMk4", 40000, 5), ("Lift Coil Mk 5", "LiftCoilMk5", 80000, 6)]),
        ("reach", "Reach", "how far the physics gun's beam reaches, in metres", 12, 30, "#8fc5d9",
            [("Beam Lens Mk 2", "BeamLensMk2", 15, 1), ("Beam Lens Mk 3", "BeamLensMk3", 18, 1.5), ("Beam Lens Mk 4", "BeamLensMk4", 22, 2), ("Beam Lens Mk 5", "BeamLensMk5", 26, 2.5)]),
        ("drillBit", "Drill bit", "the ground drill's scoop, times the digging table's radius", 1, 1, "#a8763a",
            [("Drill Bit Mk 2", "DrillBitMk2", 1.25, 4), ("Drill Bit Mk 3", "DrillBitMk3", 1.5, 5), ("Drill Bit Mk 4", "DrillBitMk4", 1.8, 6)]),
        ("drillMotor", "Drill motor", "how fast the ground drill scoops, times the digging table's pace", 1, 1, "#7f8a94",
            [("Drill Motor Mk 2", "DrillMotorMk2", 1.35, 4), ("Drill Motor Mk 3", "DrillMotorMk3", 1.75, 5), ("Drill Motor Mk 4", "DrillMotorMk4", 2.25, 6)]),
        ("fitter", "Fitting speed", "how fast timed fits, removals and welds go, times their usual pace", 1, 1, "#c9794a",
            [("Fitter Servo Mk 2", "FitterServoMk2", 1.5, 2), ("Fitter Servo Mk 3", "FitterServoMk3", 2, 3), ("Fitter Servo Mk 4", "FitterServoMk4", 3, 4)]),
        ("tractor", "Tractor beam", "the physics gun catches things out to twice its reach and reels them in", 0, 1, "#a78fd0",
            [("Tractor Emitter", "TractorEmitter", 1, 3)]),
        ("throw", "Throw", "with the physics gun holding something, Reload throws it along the look", 0, 1, "#d0a040",
            [("Kinetic Coil", "KineticCoil", 1, 4)]),
    ];

    /// <summary>What an Assembler takes for each module (Assembling.Rec: output, seconds, tier, materials, parts).</summary>
    public static readonly (string Output, int Seconds, int Tier, (string, int)[] Materials, (string, int)[]? Parts)[] Recipes =
    [
        ("Lift Coil Mk 2", 20, 2, [("copper", 6), ("steel", 4)], [("Copper Coil", 1)]),
        ("Lift Coil Mk 3", 40, 3, [("copper", 10), ("steel", 8)], [("Field Coil", 1), ("Control Unit", 1)]),
        ("Lift Coil Mk 4", 80, 4, [("copper", 16), ("gold", 4)], [("Field Coil", 2), ("Control Unit", 1)]),
        ("Lift Coil Mk 5", 160, 4, [("copper", 20), ("gold", 8), ("levite", 6)], [("Levite Core", 1), ("Field Coil", 2)]),
        ("Beam Lens Mk 2", 15, 2, [("glass", 4), ("bronze", 2)], [("Lens", 1)]),
        ("Beam Lens Mk 3", 30, 3, [("glass", 6), ("bronze", 4)], [("Lens", 2), ("Circuit Board", 1)]),
        ("Beam Lens Mk 4", 60, 3, [("glass", 10), ("copper", 4)], [("Lens", 3), ("Control Unit", 1)]),
        ("Beam Lens Mk 5", 120, 4, [("glass", 12), ("gold", 4)], [("Lens", 4), ("Control Unit", 1)]),
        ("Drill Bit Mk 2", 15, 1, [("steel", 10)], null),
        ("Drill Bit Mk 3", 30, 2, [("steel", 16), ("bronze", 4)], null),
        ("Drill Bit Mk 4", 60, 3, [("steel", 24), ("bronze", 6)], [("Bearing", 2)]),
        ("Drill Motor Mk 2", 15, 2, [("steel", 6), ("copper", 4)], [("Gear Set", 1)]),
        ("Drill Motor Mk 3", 30, 2, [("steel", 8), ("copper", 6)], [("Gear Set", 1), ("Servo Motor", 1)]),
        ("Drill Motor Mk 4", 60, 3, [("steel", 10), ("copper", 8)], [("Servo Motor", 2), ("Control Unit", 1)]),
        ("Fitter Servo Mk 2", 15, 2, [("steel", 4), ("copper", 2)], [("Servo Motor", 1)]),
        ("Fitter Servo Mk 3", 30, 3, [("steel", 6)], [("Servo Motor", 2), ("Control Unit", 1)]),
        ("Fitter Servo Mk 4", 60, 4, [("steel", 8), ("gold", 2)], [("Servo Motor", 3), ("Control Unit", 2)]),
        ("Tractor Emitter", 60, 3, [("copper", 10), ("glass", 6)], [("Field Coil", 1), ("Lens", 2), ("Control Unit", 1)]),
        ("Kinetic Coil", 45, 3, [("steel", 12), ("copper", 8)], [("Copper Coil", 2), ("Servo Motor", 1)]),
    ];

    private static object? R4(object? v) => v is int or long ? v : Py.Round(Py.D(v), 4);

    /// <summary>A palm-sized module: a base plate, a body and a cap, with one rib a Mk (a function: a lens on top).</summary>
    private static PyDict Module(string name, string colour, int mark, object mass, string about)
    {
        var boxes = new List<PyDict>
        {
            new() { ["min"] = Py.L(-0.1, 0, -0.07), ["max"] = Py.L(0.1, 0.03, 0.07) },
            new() { ["min"] = Py.L(-0.08, 0.03, -0.055), ["max"] = Py.L(0.08, 0.11, 0.055), ["color"] = colour },
        };
        if (mark == 0)
            boxes.Add(new() { ["min"] = Py.L(-0.035, 0.11, -0.035), ["max"] = Py.L(0.035, 0.15, 0.035), ["color"] = "#e8e8e0" });
        for (var k = 0; k < mark; k++)
        {
            var x = -0.06 + k * 0.03;
            boxes.Add(new() { ["min"] = Py.L(x, 0.11, -0.04), ["max"] = Py.L(x + 0.016, 0.13, 0.04), ["color"] = "#e8e8e0" });
        }
        var sockets = new List<PyDict> { new() { ["name"] = "bottom", ["at"] = Py.L(0, 0, 0), ["dir"] = "-y" } };
        PartZFight.Resolve(boxes, sockets);
        foreach (var b in boxes)
        {
            b["min"] = ((List<object?>)b["min"]!).Select(R4).ToList();
            b["max"] = ((List<object?>)b["max"]!).Select(R4).ToList();
        }
        return new PyDict
        {
            ["format"] = "call-of-slop-construct-part/1", ["name"] = name,
            ["description"] = $"A tool upgrade: {about}. F on it, lying loose, fits it to your tools.",
            ["group"] = "Production", ["section"] = "Tool upgrades", ["role"] = "panel", ["tool"] = "wrench", ["attachSeconds"] = 1.0,
            ["mass"] = mass, ["health"] = 40, ["material"] = "steel", ["color"] = "#30343a",
            ["boxes"] = boxes.Cast<object?>().ToList(), ["sockets"] = sockets.Cast<object?>().ToList(),
        };
    }

    public void Make(PartOutput o)
    {
        var stats = new List<string>();
        foreach (var s in Stats)
        {
            var function = s.Base == 0 && s.Levels.Length == 1 && s.Levels[0].Value is int v && v == 1;
            for (var k = 0; k < s.Levels.Length; k++)
            {
                var l = s.Levels[k];
                o.File($"Constructs/Parts/Upgrades/{l.File}.json", PyJson.Dumps(Module(l.Part, s.Colour, function ? 0 : k + 2, l.Mass, s.About), 2) + "\n");
            }
            var d = new PyDict
            {
                ["stat"] = s.Stat, ["name"] = s.Name, ["about"] = s.About, ["base"] = s.Base, ["creative"] = s.Creative,
                ["levels"] = s.Levels.Select(l => (object?)new PyDict { ["part"] = l.Part, ["value"] = l.Value }).ToList(),
            };
            stats.Add("    " + PyJson.Dumps(d));
        }
        var names = Stats.SelectMany(s => s.Levels.Select(l => l.Part)).ToHashSet();
        var made = Recipes.Select(r => r.Output).ToHashSet();
        if (!made.SetEquals(names))
            throw new InvalidOperationException("upgrade_parts: every module needs one recipe, and every recipe a module");
        o.File("Constructs/ToolUpgrades.json",
            "{\n  \"format\": \"" + Format + "\",\n  \"about\": " + PyJson.Dumps(About) + ",\n  \"stats\": [\n" + string.Join(",\n", stats) + "\n  ]\n}\n");
    }
}
