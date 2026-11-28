namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// Retiring the Assembler, phase 2 (Sean, 2026-11-28; docs/sandbox/constructs/production.md "Retiring the Assembler"): components and
/// items move off the Assembler to press presets. Three new dies, the Ring (Bearings and Turbine Stages from a bronze or steel round),
/// the Bell (a tube pressed into a thruster bell: the Thrust Chamber and the item thrusters, an ordnance motor among them) and the Shell
/// (ordnance sections and shells), and new patterns on the dies there were: the Lens on the Plate die (a glass blank), the Field Coil on
/// the Cable die (as the Copper Coil is), the Levite Core on the Board die, and the Zero Day frigate's small fittings on the Housing,
/// Control and Button dies. A press adds the function to a blank: each pattern presses its catalogue part from one standard blank, the
/// part's bounds on the 5 cm lattice held to its die's ranges, with the rest of its old Assembler row in bay 2 as a kit (one kind: a
/// part, or the row's second material). "presetsOnly": a stretched copy would keep its like's thrust, payload or sensing.
///
/// What a press can't make stays on the Assembler: a row with two kinds of part input (bay 2 takes one kit), and assemblies of unlike
/// parts (a Server Rack's Boards and Looms, a Robot Shell, an MMU). A new kind is a row appended here (a Press saves its Pattern
/// setting as 1 + the pattern's place on its die: never insert).
/// </summary>
public sealed partial class PressParts
{
    /// <summary>A new die's range: blanks, materials, across and length in cm, aspect, seconds.</summary>
    private sealed record DieSpec(string[] Blanks, string[] Materials, int[] Across, int[] Length, double[] Aspect, double SecBase, double PerKg);

    /// <summary>The new dies (appended to <see cref="Families"/> too, so every older die and lathe row keeps its place).</summary>
    private static readonly (string Family, DieSpec Spec)[] Phase2Dies =
    [
        ("Ring", new(["round", "pipe"], ["bronze", "steel"], [10, 180], [5, 100], [0.1, 4], 2, 0.05)),
        ("Bell", new(["pipe", "round"], ["steel", "metal"], [20, 360], [10, 800], [0.25, 16], 3, 0.02)),
        ("Shell", new(["round", "pipe", "solid"], ["steel", "metal", "bronze"], [10, 180], [10, 400], [0.25, 16], 2, 0.02)),
    ];

    /// <summary>The ranges of the older dies phase 2 adds patterns to (as their SizedDie calls give them).</summary>
    private static readonly Dictionary<string, DieSpec> OlderDieRanges = new(StringComparer.Ordinal)
    {
        ["housing"] = new(["hollow", "solid"], [], [15, 2880], [15, 3200], [0.5, 8], 0, 0),
        ["control"] = new(["plate", "hollow", "solid"], [], [10, 720], [10, 800], [0.1, 8], 0, 0),
        ["button"] = new(["plate", "solid", "round"], [], [10, 180], [10, 800], [0.25, 16], 0, 0),
        ["plate"] = new(["plate"], [], [10, 2880], [25, 6400], [1, 64], 0, 0),
        ["cable"] = new(["round", "pipe"], [], [10, 180], [10, 1600], [0.5, 64], 0, 0),
        ["board"] = new(["plate"], [], [20, 45], [20, 100], [0.5, 4], 0, 0),
    };

    /// <summary>
    /// The parts moved: catalogue part, die, pattern id, blank section and material, and its bay 2 kit (a part or a material and how many;
    /// null: none). Grouped by die; within a die, the order is the patterns' order (append only).
    /// </summary>
    private static readonly (string Part, string Die, string Id, string Section, string Material, string? Kit, bool KitPart, int N)[] Phase2Moves =
    [
        // The Ring die: a Bearing from a bronze round (counts unchanged: a recipe still takes as many), a Turbine Stage's blades round a steel one.
        ("Bearing", "ring", "bearing", "round", "bronze", null, false, 0),
        ("Turbine Stage", "ring", "turbine", "round", "steel", "bronze", false, 2),
        // The Bell die: a tube pressed on it is a thruster bell (Sean). The Thrust Chamber, and the item thrusters (ordnance motors).
        ("Thrust Chamber", "bell", "chamber", "pipe", "steel", "concrete", false, 1),
        ("Solid Motor", "bell", "solid", "pipe", "steel", "concrete", false, 3),
        ("Boost Motor", "bell", "boost", "pipe", "steel", "concrete", false, 4),
        ("Liquid Motor", "bell", "liquid", "pipe", "steel", "Gear Set", true, 1),
        ("Hybrid Motor", "bell", "hybrid", "pipe", "steel", "rubber", false, 3),
        ("Cold Gas Thruster", "bell", "coldgas", "pipe", "steel", "copper", false, 1),
        ("Ramjet", "bell", "ramjet", "pipe", "steel", "bronze", false, 4),
        ("Turbojet", "bell", "turbojet", "pipe", "steel", "Bearing", true, 2),
        ("Rocket Assist", "bell", "assist", "pipe", "steel", "concrete", false, 3),
        ("Jet Vanes", "bell", "vanes", "pipe", "steel", "bronze", false, 2),
        // The Shell die: shells, and the ordnance sections a magazine's rounds are built of.
        ("High Explosive Shell", "shell", "he", "round", "steel", "bronze", false, 1),
        ("Siege Shell", "shell", "siege", "round", "steel", "bronze", false, 2),
        ("Smoke Shell", "shell", "smoke", "round", "steel", "rubber", false, 1),
        ("Incendiary Shell", "shell", "incendiary", "round", "steel", "rubber", false, 2),
        ("Warhead HE", "shell", "warheadhe", "round", "steel", "concrete", false, 2),
        ("Warhead Blast", "shell", "blast", "round", "steel", "concrete", false, 3),
        ("Impact Fuse", "shell", "impactfuse", "round", "steel", "copper", false, 1),
        ("Delay Fuse", "shell", "delayfuse", "round", "steel", "copper", false, 2),
        ("Timer Fuse", "shell", "timerfuse", "round", "steel", "copper", false, 1),
        ("Proximity Fuse", "shell", "proxfuse", "round", "steel", "Circuit Board", true, 1),
        ("Altitude Fuse", "shell", "altfuse", "round", "steel", "Circuit Board", true, 1),
        ("Airburst Fuse", "shell", "airburst", "round", "steel", "Circuit Board", true, 1),
        ("Remote Fuse", "shell", "remotefuse", "round", "steel", "Circuit Board", true, 1),
        ("Bomb Tail Fins", "shell", "bombfins", "round", "steel", null, false, 0),
        ("Missile Fuel Tank", "shell", "fueltank", "round", "steel", "rubber", false, 1),
        ("Stage Separator", "shell", "separator", "round", "steel", "copper", false, 1),
        ("Body Tube", "shell", "bodytube", "round", "steel", null, false, 0),
        ("Tail Fins", "shell", "tailfins", "round", "steel", null, false, 0),
        ("Tandem Spacer", "shell", "tandem", "round", "steel", null, false, 0),
        ("Ring Tail", "shell", "ringtail", "round", "steel", null, false, 0),
        ("Canards", "shell", "canards", "round", "steel", "Servo Motor", true, 1),
        ("Gimbal Mount", "shell", "gimbal", "round", "steel", "Servo Motor", true, 2),
        ("Reaction Jets", "shell", "reactionjets", "round", "steel", "Circuit Board", true, 2),
        ("Radar Seeker", "shell", "radarseeker", "round", "steel", "Circuit Board", true, 2),
        ("Command Link", "shell", "commandlink", "round", "steel", "Circuit Board", true, 1),
        ("GPS Unit", "shell", "gps", "round", "steel", "Circuit Board", true, 2),
        ("Radio Homer", "shell", "radiohomer", "round", "steel", "Circuit Board", true, 1),
        ("Beacon Homer", "shell", "beaconhomer", "round", "steel", "Circuit Board", true, 1),
        ("Cluster Dispenser", "shell", "cluster", "round", "steel", "concrete", false, 3),
        ("Fire Bomblets", "shell", "bomblets", "round", "steel", "concrete", false, 2),
        ("Mine Dispenser", "shell", "mines", "round", "steel", "Circuit Board", true, 1),
        ("MIRV Bus", "shell", "mirv", "round", "steel", "Circuit Board", true, 2),
        ("Glide Wings", "shell", "glidewings", "round", "steel", "laminate", false, 2),
        ("Cruise Wings", "shell", "cruisewings", "round", "steel", "laminate", false, 3),
        ("Pop-out Wings", "shell", "popoutwings", "round", "steel", "Servo Motor", true, 1),
        // New patterns on the older dies (Sean's plan): the Lens on the Plate die, the Field Coil on the Cable die, the Levite Core on the Board die.
        ("Lens", "plate", "lens", "plate", "glass", "bronze", false, 1),
        ("Field Coil", "cable", "fieldcoil", "round", "copper", "circuits", false, 1),
        ("Levite Core", "board", "levitecore", "plate", "laminate", "levite", false, 4),
        // The Zero Day frigate's small fittings that were Assembler-made: lamps and boxes on the Housing die, its sensors and computer on
        // the Control die, the turret and driving modules on the Button die (Driving Module beside its twin).
        ("Bulkhead Light", "housing", "bulkheadlight", "hollow", "steel", "Circuit Board", true, 1),
        ("Ammo Container", "housing", "ammocontainer", "hollow", "steel", "rubber", false, 1),
        ("Ammo Box", "housing", "ammobox", "hollow", "steel", "bronze", false, 3),
        ("Requisition Crate", "housing", "reqcrate", "hollow", "steel", "rubber", false, 1),
        ("Medicine Cabinet", "housing", "medcabinet", "hollow", "steel", "rubber", false, 1),
        ("Ship Toilet", "housing", "shiptoilet", "hollow", "steel", "concrete", false, 1),
        ("Ship Basin", "housing", "shipbasin", "hollow", "steel", "concrete", false, 1),
        ("Fire Director", "control", "firedirector", "hollow", "steel", "Circuit Board", true, 1),
        ("Long Range Sensor", "control", "rangesensor", "hollow", "steel", "Circuit Board", true, 1),
        ("Computer", "control", "computer", "hollow", "steel", "Circuit Board", true, 4),
        ("Turret Module", "button", "turretmodule", "plate", "steel", "rubber", false, 1),
        ("Driving Module", "button", "drivingmodule", "plate", "steel", "rubber", false, 1),
        // Phase 4 (2026-11-28): the Thruster Jig's one item, the Decoupler, a bolted plate with its charge's board: on the Button die. Its
        // placed rows (jets, lift fans, rockets, repulsors) and the Sewing Table's Lift Cells bake from kits instead (BakeKits.ByRule).
        ("Decoupler", "button", "decoupler", "plate", "steel", "Circuit Board", true, 1),
    ];

    private static DieSpec RangeOf(string die) =>
        Phase2Dies.FirstOrDefault(d => d.Family.ToLowerInvariant() == die).Spec ?? OlderDieRanges.GetValueOrDefault(die)
        ?? throw new InvalidOperationException($"press_parts: phase 2 has no ranges for the {die} die");

    /// <summary>A part's standard blank: its bounds on the 5 cm lattice (a round bar along a long part, or a slice of one for a disc; a plate's
    /// width its smaller side, its length the larger; a box its bounds, hollow for the big fittings), held to its die's across, length and aspect.</summary>
    private static string Phase2Blank(string part, string die, string section, string material)
    {
        var d = CallOfSlop.ConstructParts.Find(part) ?? throw new InvalidOperationException($"press_parts: phase 2 names {part}, which isn't a part");
        var r = RangeOf(die);
        if (!r.Blanks.Contains(section))
            throw new InvalidOperationException($"press_parts: the {die} die takes no {section} blank ({part})");
        var size = d.BoundsMax - d.BoundsMin;
        static int Up(double cm) => (int)Math.Ceiling(cm / 5 - 1e-6) * 5;
        static int Down(double cm) => (int)Math.Floor(cm / 5 + 1e-6) * 5;
        static int Lat(double m) => Math.Max(5, (int)Math.Round(m * 100 / 5, MidpointRounding.AwayFromZero) * 5);
        int w, h, len;
        switch (section)
        {
            case "round" or "pipe":
            {
                // (a long part, a motor or a fuse, is a bar as long as it; a disc or a block, a Bearing or a coil, a slice of a bar as wide as
                // it and as thick as its thinnest side, so the blank costs about what the part weighs)
                var sides = new[] { size.X, size.Y, size.Z }.OrderBy(v => v).ToArray();
                var lengthwise = sides[2] > sides[1] * 1.2f;
                w = h = Math.Max(Math.Max(r.Across[0], section == "pipe" ? 20 : 10), Lat(lengthwise ? sides[1] : sides[2]));
                len = Lat(lengthwise ? sides[2] : sides[0]);
                break;
            }
            case "plate":
                w = Math.Max(r.Across[0], Math.Max(10, Lat(Math.Min(size.X, size.Z))));
                h = 10;
                len = Lat(Math.Max(size.X, size.Z));
                break;
            default:
                w = Math.Max(r.Across[0], Math.Max(10, Lat(size.X)));
                h = Math.Max(r.Across[0], Math.Max(10, Lat(size.Y)));
                len = Lat(size.Z);
                break;
        }
        var across = section == "plate" ? w : Math.Min(w, h);
        if (across > r.Across[1])
            throw new InvalidOperationException($"press_parts: {part} is {across} cm across, past the {die} die's {r.Across[1]}");
        len = Math.Max(len, Math.Max(r.Length[0], Up(r.Aspect[0] * across)));
        len = Math.Min(len, Math.Min(r.Length[1], Down(r.Aspect[1] * across)));
        return section switch
        {
            "round" => $"round {w}x{w}x{len} {material}",
            "pipe" => $"pipe5 {w}x{w}x{len} {material}",
            _ => $"{section} {w}x{h}x{len} {material}",
        };
    }

    /// <summary>A moved part's pattern (on a new die, Auto's; on an older one appended, never Auto's: a press on Auto makes what it made before).</summary>
    private static PyDict Phase2Pattern((string Part, string Die, string Id, string Section, string Material, string? Kit, bool KitPart, int N) m)
    {
        var fresh = Phase2Dies.Any(d => d.Family.ToLowerInvariant() == m.Die);
        var label = m.Part.ToLowerInvariant();
        if (label.Length > 24)
            label = label[..24];
        var kit = m.Kit == null ? null : Kit(m.Kit, m.KitPart, m.N, 0, "mass", m.N);
        var p = Pattern(m.Id, m.Part, label, Phase2Blank(m.Part, m.Die, m.Section, m.Material), blanks: [m.Section], bay2: kit, auto: fresh);
        p["presetsOnly"] = true;
        return p;
    }

    /// <summary>The phase 2 patterns of a die, to append to its list.</summary>
    private static IEnumerable<object?> Phase2(string die) => Phase2Moves.Where(m => m.Die == die).Select(m => (object?)Phase2Pattern(m));

    /// <summary>The new dies (after every older one: a die is found by its family, never its place).</summary>
    private static IEnumerable<object?> Phase2NewDies()
    {
        foreach (var (family, s) in Phase2Dies)
            yield return SizedDie(family, s.Blanks, s.Materials, s.Across, s.Length, s.Aspect, s.SecBase, s.PerKg, Py.L(), Phase2(family.ToLowerInvariant()).ToList());
    }

    /// <summary>Each moved part's preset: its own pattern's blank.</summary>
    private static IEnumerable<(string Part, string Die, string Pattern, string Blank)> Phase2Presets() =>
        Phase2Moves.Select(m => (m.Part, m.Die, m.Id, Phase2Blank(m.Part, m.Die, m.Section, m.Material)));
}
