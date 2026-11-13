namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The Sandbox's assembling data: Constructs/Assembling.json and the sub-components in Constructs/Parts/Components (docs/sandbox/CONSTRUCTS.md,
/// "Assembling"). Was tools/constructs/assembling.py.
///
/// <see cref="Hand"/> holds the recipes written by hand: every sub-component and the parts a player reaches for first, each drawing on
/// different production chains (components; drivetrain; power and control; weapons, flame, harpoon and artillery; machines and storage;
/// the factions' kit; air weapons and modular ordnance; robot shells; what a pilot wears; the build stations; the basic frame). The tool
/// upgrades' recipes come from <see cref="UpgradeParts.Recipes"/>, the Ordnance Racks' from <see cref="AirWeaponsParts.OrdnanceRacks"/>.
/// The long tail is generated from the part files by rule: a part costs half its mass in its class's material (<see cref="LongTail"/>),
/// its role adds what a part of that kind must have (a Circuit Board in anything electrical, a Gear Set in a gearbox, a Servo Motor in an
/// actuator), and the materials in it set the tier. Rules, not a list, so a new part gets a recipe by running this (after the generators
/// that write parts: it reads every part file).
///
/// The file is written one recipe a line (<see cref="Layout"/>), as the Python did.
/// </summary>
public sealed class Assembling : IPartGenerator
{
    public string Name => "assembling";

    private const string Format = "call-of-slop-assembling/1";
    private const string About = "Assembling (docs/sandbox/CONSTRUCTS.md, 'Assembling'): what an Assembler builds. Written by the part kit (--gen-parts assembling): fix it, not this file. "
        + "'output' names a part, 'count' how many a batch makes (default 1), 'materials' the bulk materials (Recycling.json) and 'parts' the item parts it takes, "
        + "'seconds' the work at speed 1, 'minTier' the lowest tier of Assembler (default 1), 'machine' the family that builds it, when not the plain Assembler. At most 6 inputs; a physics-only part can't be an input.";

    // Components: small item parts, each one step in a chain. (name, file, mass, colour, boxes, description); mass keeps its Python type.
    private static readonly (string Name, string File, object Mass, string Colour, (double[] Lo, double[] Hi)[] Shapes, string Desc)[] Components =
    [
        ("Gear Set", "GearSet", 6, "#7f8a94", [([-0.12, 0, -0.12], [0.12, 0.08, 0.12]), ([-0.07, 0.08, -0.07], [0.07, 0.15, 0.07])], "Steel gears for gearboxes and drives."),
        ("Bearing", "Bearing", 1.5, "#a8763a", [([-0.07, 0, -0.07], [0.07, 0.05, 0.07])], "A bronze bearing."),
        ("Piston Set", "PistonSet", 12, "#6f7d8c", [([-0.15, 0, -0.1], [0.15, 0.22, 0.1]), ([-0.11, 0.22, -0.07], [0.11, 0.3, 0.07])], "Pistons and rods for engines."),
        ("Copper Coil", "CopperCoil", 4, "#c9794a", [([-0.1, 0, -0.1], [0.1, 0.16, 0.1]), ([-0.13, 0.05, -0.13], [0.13, 0.11, 0.13])], "For motors and alternators."),
        ("Wiring Loom", "WiringLoom", 3, "#2c2c2e", [([-0.16, 0, -0.04], [0.16, 0.06, 0.04]), ([-0.04, 0.06, -0.12], [0.04, 0.1, 0.12])], "Copper wire in rubber."),
        ("Circuit Board", "CircuitBoard", 1, "#3f9b5c", [([-0.12, 0, -0.09], [0.12, 0.02, 0.09]), ([-0.05, 0.02, -0.04], [0.05, 0.05, 0.04])], "Circuits on a laminate board."),
        ("Control Unit", "ControlUnit", 4, "#30343a", [([-0.12, 0, -0.1], [0.12, 0.14, 0.1]), ([-0.05, 0.14, -0.05], [0.05, 0.17, 0.05])], "For powered machines."),
        ("Servo Motor", "ServoMotor", 8, "#55705a", [([-0.1, 0, -0.14], [0.1, 0.2, 0.14]), ([-0.04, 0.05, 0.14], [0.04, 0.13, 0.22])], "Moves an actuator."),
        ("Tyre Carcass", "TyreCarcass", 9, "#2c2c2e", [([-0.25, 0, -0.1], [0.25, 0.5, 0.1]), ([-0.1, 0.1, -0.11], [0.1, 0.4, 0.11])], "Tyre, ready for a rim."),
        ("Steel Plate", "SteelPlate", 8, "#6f7d8c", [([-0.25, 0, -0.25], [0.25, 0.03, 0.25])], "Rolled steel plate."),
        ("Lens", "Lens", 0.5, "#8fc5d9", [([-0.05, 0, -0.05], [0.05, 0.1, 0.05]), ([-0.04, 0.1, -0.04], [0.04, 0.12, 0.04])], "For scopes, sensors and lamps."),
        ("Turbine Stage", "TurbineStage", 6, "#9aa3ad", [([-0.14, 0, -0.14], [0.14, 0.1, 0.14]), ([-0.05, 0.1, -0.05], [0.05, 0.16, 0.05])], "A ring of hot blades for a jet."),
        ("Thrust Chamber", "ThrustChamber", 8, "#5b5048", [([-0.1, 0, -0.1], [0.1, 0.2, 0.1]), ([-0.15, 0.2, -0.15], [0.15, 0.32, 0.15])], "A combustion chamber and its throat, for a rocket."),
        ("Field Coil", "FieldCoil", 5, "#5c6fa8", [([-0.13, 0, -0.13], [0.13, 0.08, 0.13]), ([-0.07, 0.08, -0.07], [0.07, 0.14, 0.07])], "A tuned coil that pushes against the ground."),
        ("Levite Core", "LeviteCore", 3, "#a78fd0", [([-0.1, 0, -0.1], [0.1, 0.03, 0.1]), ([-0.07, 0.03, -0.07], [0.07, 0.18, 0.07])], "Levite in a cage of circuits: the heart of an Anti-Grav Generator."),
    ];

    private static PyDict? Dict((string, int)[]? kv) => kv is { Length: > 0 } ? new PyDict().Update([.. kv.Select(x => (x.Item1, (object?)x.Item2))]) : null;

    /// <summary>A recipe: output, count (if more than one), materials, parts, seconds, minTier (if above 1).</summary>
    private static PyDict Rec(string output, object seconds, int tier = 1, int count = 1, (string, int)[]? m = null, (string, int)[]? p = null) =>
        RecD(output, seconds, tier, count, Dict(m), Dict(p));

    private static PyDict RecD(string output, object seconds, int tier, int count, PyDict? m, PyDict? p)
    {
        var r = new PyDict { ["output"] = output };
        if (count > 1)
            r["count"] = count;
        if (m is { Count: > 0 })
            r["materials"] = m;
        if (p is { Count: > 0 })
            r["parts"] = p;
        r["seconds"] = seconds;
        if (tier > 1)
            r["minTier"] = tier;
        return r;
    }

    // Every one of these draws on at least two chains (ore to metal to steel; wood to planks to rubber and cloth; copper; sand to glass; gold).
    private static List<PyDict> Hand() =>
    [
        Rec("Gear Set", 4, 1, m: [("steel", 5)]),
        Rec("Bearing", 2, 2, count: 2, m: [("bronze", 2)]),
        Rec("Piston Set", 8, 2, m: [("steel", 4), ("bronze", 2)]),
        Rec("Copper Coil", 3, 2, m: [("copper", 3)]),
        Rec("Wiring Loom", 3, 2, m: [("copper", 2), ("rubber", 1)]),
        Rec("Circuit Board", 4, 3, m: [("circuits", 2), ("laminate", 1)]),
        Rec("Control Unit", 6, 3, m: [("steel", 1)], p: [("Circuit Board", 1), ("Wiring Loom", 1)]),
        Rec("Servo Motor", 6, 2, m: [("steel", 2)], p: [("Copper Coil", 1), ("Bearing", 2)]),
        Rec("Tyre Carcass", 5, 1, m: [("rubber", 4), ("cloth", 2)]),
        Rec("Steel Plate", 2, 1, count: 2, m: [("steel", 5)]),
        Rec("Lens", 3, 2, m: [("glass", 1), ("bronze", 1)]),
        Rec("Turbine Stage", 6, 2, m: [("steel", 4), ("bronze", 2)]),
        Rec("Thrust Chamber", 5, 1, m: [("steel", 5), ("concrete", 1)]),
        Rec("Field Coil", 6, 3, m: [("copper", 6), ("circuits", 1)]),
        Rec("Levite Core", 12, 4, m: [("levite", 4), ("circuits", 2)]),
        Rec("Engine", 20, 2, m: [("steel", 20)], p: [("Piston Set", 2), ("Wiring Loom", 1)]),
        Rec("Small Engine", 10, 2, m: [("steel", 6)], p: [("Piston Set", 1), ("Wiring Loom", 1)]),
        Rec("V8 Engine", 40, 3, m: [("steel", 45)], p: [("Piston Set", 4), ("Wiring Loom", 2), ("Bearing", 4)]),
        Rec("Diesel Engine", 50, 3, m: [("steel", 70)], p: [("Piston Set", 6), ("Wiring Loom", 2), ("Bearing", 6)]),
        Rec("Turbine", 60, 4, m: [("steel", 25), ("bronze", 10)], p: [("Bearing", 8), ("Control Unit", 1)]),
        Rec("Electric Motor", 15, 3, m: [("steel", 10)], p: [("Copper Coil", 4), ("Bearing", 2), ("Control Unit", 1)]),
        Rec("Transmission", 12, 1, m: [("steel", 8)], p: [("Gear Set", 2)]),
        Rec("Differential", 12, 2, m: [("steel", 10)], p: [("Gear Set", 2), ("Bearing", 2)]),
        Rec("Suspension", 5, 1, m: [("steel", 3), ("rubber", 1)]),
        Rec("Wheel", 5, 1, m: [("steel", 2)], p: [("Tyre Carcass", 1)]),
        Rec("Small Wheel", 3, 1, m: [("rubber", 3), ("steel", 1)]),
        Rec("Large Wheel", 8, 1, m: [("steel", 4)], p: [("Tyre Carcass", 2)]),
        Rec("Monster Wheel", 30, 3, m: [("steel", 20)], p: [("Tyre Carcass", 8)]),
        Rec("Fuel Tank", 6, 1, m: [("steel", 6), ("rubber", 1)]),
        Rec("Fuel Line", 2, 1, count: 2, m: [("rubber", 1), ("copper", 1)]),
        Rec("Alternator", 8, 2, m: [("steel", 1)], p: [("Copper Coil", 2), ("Bearing", 1)]),
        Rec("Battery", 8, 3, m: [("copper", 4), ("circuits", 2)]),
        Rec("Battery Pack", 40, 4, m: [("copper", 20), ("steel", 12)], p: [("Control Unit", 1)]),
        Rec("Seat", 5, 1, m: [("cloth", 3), ("steel", 3)]),
        Rec("Bunk", 6, 1, m: [("cloth", 4), ("steel", 3)]),
        Rec("Driving Module", 3, 1, m: [("steel", 3), ("rubber", 1)]),
        Rec("Turret Module", 3, 1, m: [("steel", 3), ("rubber", 1)]),
        Rec("Lamp", 2, 2, m: [("copper", 1)], p: [("Lens", 1)]),
        Rec("Proximity Sensor", 4, 3, p: [("Lens", 1), ("Circuit Board", 1)]),
        Rec("Gun Barrel", 6, 1, m: [("steel", 2)]),
        Rec("Ammo Box", 5, 2, m: [("steel", 3), ("bronze", 3)]),
        Rec("Rifle Receiver", 10, 2, m: [("steel", 6)], p: [("Gear Set", 1), ("Bearing", 2)]),
        Rec("Gun Scope", 4, 2, m: [("steel", 1)], p: [("Lens", 2)]),
        Rec("Mounted MG", 20, 3, m: [("steel", 10), ("bronze", 4)], p: [("Gear Set", 1), ("Control Unit", 1)]),
        Rec("Firebreather Receiver", 10, 2, m: [("steel", 6), ("bronze", 2)], p: [("Gear Set", 1)]),
        Rec("Flame Valve", 3, 1, m: [("steel", 1), ("bronze", 1)]),
        Rec("Flame Nozzle", 4, 1, m: [("steel", 2)]),
        Rec("Fuel Canister", 3, 1, m: [("steel", 2)]),
        Rec("Twin Fuel Tanks", 5, 2, m: [("steel", 3), ("bronze", 1)]),
        Rec("Fuel Keg", 7, 2, m: [("steel", 6), ("bronze", 2)]),
        Rec("Napalm Gel", 3, 2, m: [("rubber", 1), ("steel", 1)]),
        Rec("Whaler Receiver", 10, 2, m: [("steel", 7), ("bronze", 2)], p: [("Bearing", 2)]),
        Rec("Harpoon Trigger", 3, 1, m: [("steel", 1), ("bronze", 1)]),
        Rec("Harpoon Tube", 5, 1, m: [("steel", 3)]),
        Rec("Harpoon Breech", 3, 1, m: [("steel", 2)]),
        Rec("Harpoon Rack", 5, 2, m: [("steel", 3), ("bronze", 1)]),
        Rec("Harpoon Drum", 7, 2, m: [("steel", 5), ("bronze", 2)], p: [("Bearing", 1)]),
        Rec("Barbed Head", 3, 1, m: [("steel", 2)]),
        Rec("Field Gun Receiver", 20, 3, m: [("steel", 18), ("bronze", 4)], p: [("Gear Set", 2), ("Bearing", 2)]),
        Rec("Howitzer Receiver", 30, 3, m: [("steel", 28), ("bronze", 6)], p: [("Gear Set", 3), ("Bearing", 2), ("Control Unit", 1)]),
        Rec("Sliding Breech", 8, 2, m: [("steel", 4), ("bronze", 1)]),
        Rec("Interrupted Breech", 10, 2, m: [("steel", 5), ("bronze", 2)]),
        Rec("Howitzer Tube", 12, 2, m: [("steel", 12)]),
        Rec("Long Gun Tube", 16, 3, m: [("steel", 20)]),
        Rec("Loading Tray", 4, 1, m: [("steel", 3)]),
        Rec("Ready Rack", 8, 2, m: [("steel", 6), ("bronze", 1)]),
        Rec("Shell Hoist", 12, 3, m: [("steel", 10)], p: [("Gear Set", 1)]),
        Rec("High Explosive Shell", 4, 2, m: [("steel", 3), ("bronze", 1)]),
        Rec("Siege Shell", 7, 3, m: [("steel", 5), ("bronze", 2)]),
        Rec("Smoke Shell", 4, 2, m: [("steel", 2), ("rubber", 1)]),
        Rec("Incendiary Shell", 6, 3, m: [("steel", 3), ("rubber", 2)]),
        Rec("Materials Bin", 10, 1, m: [("steel", 8), ("concrete", 2)]),
        Rec("Chest", 6, 1, m: [("planks", 8), ("steel", 2)]),
        Rec("Recycler", 30, 2, m: [("steel", 45)], p: [("Gear Set", 4), ("Wiring Loom", 2)]),
        Rec("Inventory Beacon", 25, 3, m: [("steel", 16), ("copper", 6)], p: [("Control Unit", 2)]),
        Rec("Shelf Bay 0.5", 3, 1, m: [("planks", 2), ("steel", 1)]),
        Rec("Shelf Bay 1", 4, 1, m: [("planks", 4), ("steel", 1)]),
        Rec("Tall Shelf Bay 1", 5, 1, m: [("planks", 4), ("steel", 2)]),
        Rec("Medium Shelf Bay 1", 6, 1, m: [("planks", 8), ("steel", 3)]),
        Rec("Medium Shelf Bay 2", 8, 1, m: [("planks", 14), ("steel", 5)]),
        Rec("Medium Shelf Bay 4", 12, 1, m: [("planks", 30), ("steel", 12)]),
        Rec("Smart Terminal", 15, 2, m: [("steel", 12), ("glass", 2)], p: [("Control Unit", 1), ("Circuit Board", 2)]),
        Rec("Computer", 12, 3, m: [("steel", 4), ("glass", 2)], p: [("Circuit Board", 2), ("Control Unit", 1)]),
        Rec("Fax Machine", 10, 3, m: [("steel", 3), ("laminate", 1)], p: [("Circuit Board", 1), ("Copper Coil", 1)]),
        Rec("Server Rack", 15, 3, m: [("steel", 8)], p: [("Circuit Board", 3), ("Wiring Loom", 2)]),
        Rec("Network Switch", 4, 3, m: [("steel", 1), ("copper", 1)], p: [("Circuit Board", 1)]),
        Rec("Cable Reel", 3, 2, m: [("copper", 3), ("planks", 1)]),
        Rec("Satellite Dish", 4, 2, m: [("steel", 2), ("copper", 1)]),
        Rec("Cubicle Desk", 6, 1, m: [("planks", 4), ("steel", 2), ("laminate", 1)]),
        Rec("Water Cooler", 4, 1, m: [("laminate", 2), ("glass", 1)]),
        Rec("Office Phone", 3, 2, m: [("laminate", 1), ("copper", 1)]),
        Rec("Call Headset", 2, 2, m: [("laminate", 1), ("copper", 1)]),
        Rec("Gift Card Sign", 2, 1, m: [("planks", 1), ("laminate", 1)]),
        Rec("Prize Wheel", 3, 1, m: [("planks", 2), ("laminate", 1)]),
        Rec("Loudhailer", 3, 2, m: [("steel", 1), ("copper", 1)]),
        Rec("Phone Mast", 4, 2, m: [("steel", 2), ("copper", 1)]),
        Rec("Pipe Box", 8, 2, m: [("planks", 4), ("rubber", 2)], p: [("Bearing", 1)]),
        Rec("Whisky Cask", 5, 1, m: [("planks", 10), ("steel", 2)]),
        Rec("Pillar Box", 6, 1, m: [("steel", 12)]),
        Rec("Phone Box", 8, 1, m: [("steel", 14), ("glass", 3)], p: [("Circuit Board", 1)]),
        Rec("Tea Urn", 4, 1, m: [("steel", 3), ("planks", 1)]),
        Rec("Light Bomb", 3, 2, m: [("steel", 6), ("concrete", 2)]),
        Rec("AA Gun", 20, 3, m: [("steel", 40)], p: [("Gear Set", 1)]),
        Rec("Rocket Pod Mk2", 12, 3, m: [("steel", 18), ("copper", 3)]),
        Rec("Flare Dispenser", 6, 2, m: [("steel", 6), ("copper", 1)]),
        Rec("AA Sight", 8, 3, m: [("steel", 3), ("glass", 2)], p: [("Lens", 1), ("Circuit Board", 1)]),
        Rec("Seeker Head", 12, 4, m: [("steel", 3), ("glass", 2)], p: [("Lens", 2), ("Control Unit", 1)]),
        Rec("Ordnance Bench", 15, 2, m: [("steel", 30), ("planks", 6)]),
        Rec("Impact Fuse", 3, 2, m: [("steel", 2), ("copper", 1)]),
        Rec("Delay Fuse", 4, 2, m: [("steel", 2), ("copper", 2)]),
        Rec("Warhead HE", 6, 2, m: [("steel", 8), ("concrete", 2)]),
        Rec("Warhead Blast", 7, 2, m: [("steel", 6), ("concrete", 3)]),
        Rec("Warhead Bunker", 9, 3, m: [("steel", 16), ("concrete", 2)]),
        Rec("Bomb Tail Fins", 3, 2, m: [("steel", 3)]),
        Rec("Solid Motor", 8, 3, m: [("steel", 6), ("concrete", 3)]),
        Rec("Boost Motor", 9, 3, m: [("steel", 8), ("concrete", 4)]),
        Rec("Liquid Motor", 12, 4, m: [("steel", 8), ("copper", 3)], p: [("Gear Set", 1)]),
        Rec("Hybrid Motor", 12, 4, m: [("steel", 8), ("rubber", 3), ("copper", 2)]),
        Rec("Cold Gas Thruster", 6, 3, m: [("steel", 4), ("copper", 1)]),
        Rec("Ramjet", 15, 4, m: [("steel", 12), ("bronze", 4)]),
        Rec("Turbojet", 18, 4, m: [("steel", 10), ("bronze", 4)], p: [("Bearing", 2)]),
        Rec("Missile Fuel Tank", 4, 2, m: [("steel", 4), ("rubber", 1)]),
        Rec("Stage Separator", 4, 3, m: [("steel", 3), ("copper", 1)]),
        Rec("Body Tube", 3, 2, m: [("steel", 3)]),
        Rec("Tail Fins", 3, 2, m: [("steel", 3)]),
        Rec("Canards", 5, 3, m: [("steel", 3)], p: [("Servo Motor", 1)]),
        Rec("Gimbal Mount", 8, 4, m: [("steel", 4)], p: [("Servo Motor", 2)]),
        Rec("Jet Vanes", 5, 3, m: [("steel", 2), ("bronze", 2)]),
        Rec("Reaction Jets", 8, 4, m: [("steel", 4), ("copper", 2)], p: [("Control Unit", 1)]),
        Rec("Heat Seeker", 10, 4, m: [("steel", 2), ("glass", 2)], p: [("Lens", 1), ("Circuit Board", 1)]),
        Rec("Radar Seeker", 12, 4, m: [("steel", 3), ("copper", 2)], p: [("Circuit Board", 2)]),
        Rec("Command Link", 8, 3, m: [("steel", 2), ("copper", 1)], p: [("Circuit Board", 1)]),
        Rec("Laser Seeker", 10, 4, m: [("steel", 2), ("glass", 1)], p: [("Lens", 1), ("Circuit Board", 1)]),
        Rec("GPS Unit", 10, 4, m: [("steel", 2), ("copper", 1)], p: [("Control Unit", 1)]),
        Rec("Radio Homer", 8, 3, m: [("steel", 2), ("copper", 2)], p: [("Circuit Board", 1)]),
        Rec("Beacon Homer", 8, 3, m: [("steel", 2), ("copper", 2)], p: [("Circuit Board", 1)]),
        Rec("Proximity Fuse", 6, 3, m: [("steel", 2), ("copper", 1)], p: [("Circuit Board", 1)]),
        Rec("Altitude Fuse", 6, 3, m: [("steel", 2), ("copper", 1)], p: [("Circuit Board", 1)]),
        Rec("Timer Fuse", 4, 2, m: [("steel", 2), ("copper", 1)]),
        Rec("Airburst Fuse", 6, 3, m: [("steel", 2), ("copper", 1)], p: [("Circuit Board", 1)]),
        Rec("Remote Fuse", 6, 3, m: [("steel", 2), ("copper", 2)], p: [("Circuit Board", 1)]),
        Rec("Robot Shell", 30, 2, m: [("steel", 30), ("copper", 8), ("rubber", 4)], p: [("Circuit Board", 2)]),
        Rec("Shell Control Chair", 12, 2, m: [("steel", 14), ("copper", 6), ("rubber", 3)], p: [("Circuit Board", 1)]),
        Rec("Cluster Dispenser", 10, 3, m: [("steel", 10), ("concrete", 3)]),
        Rec("Fire Bomblets", 10, 3, m: [("steel", 8), ("concrete", 2), ("rubber", 2)]),
        Rec("Mine Dispenser", 12, 4, m: [("steel", 10), ("concrete", 3)], p: [("Circuit Board", 1)]),
        Rec("MIRV Bus", 18, 4, m: [("steel", 12), ("copper", 3)], p: [("Control Unit", 1)]),
        Rec("Rod Bundle", 10, 3, m: [("steel", 40)]),
        Rec("Kinetic Penetrator", 10, 3, m: [("steel", 40), ("bronze", 4)]),
        Rec("Fission Warhead", 120, 4, m: [("steel", 120), ("concrete", 40), ("gold", 25)], p: [("Control Unit", 2), ("Circuit Board", 4)]),
        Rec("Fusion Warhead", 240, 4, m: [("steel", 200), ("gold", 40), ("hydrogen", 120), ("helium", 60)], p: [("Control Unit", 3)]),
        Rec("Antimatter Warhead", 480, 4, m: [("steel", 160), ("gold", 60), ("levite", 80), ("helium", 40)], p: [("Levite Core", 2), ("Control Unit", 4)]),
        Rec("Tandem Spacer", 3, 3, m: [("steel", 2)]),
        Rec("Glide Wings", 8, 3, m: [("steel", 8), ("laminate", 2)]),
        Rec("Pop-out Wings", 8, 3, m: [("steel", 6), ("laminate", 1)], p: [("Servo Motor", 1)]),
        Rec("Cruise Wings", 8, 3, m: [("steel", 6), ("laminate", 3)]),
        Rec("Glide Guidance Kit", 10, 4, m: [("steel", 3)], p: [("Servo Motor", 1), ("Control Unit", 1)]),
        Rec("Rocket Assist", 8, 3, m: [("steel", 6), ("concrete", 3)]),
        Rec("Retard Chute", 4, 2, m: [("cloth", 3), ("steel", 1)]),
        Rec("Ballute", 4, 2, m: [("rubber", 3), ("steel", 1)]),
        Rec("Ring Tail", 3, 2, m: [("steel", 4)]),
        Rec("Parachute", 3, 1, m: [("rubber", 2), ("planks", 2)]),
        Rec("Backpack Glider", 4, 2, m: [("rubber", 3), ("steel", 2)]),
        Rec("Jetpack", 8, 4, m: [("steel", 6), ("copper", 3)], p: [("Control Unit", 1), ("Wiring Loom", 2)]),
        Rec("Ejection Seat", 5, 2, m: [("steel", 6), ("rubber", 2)], p: [("Wiring Loom", 1)]),
        Rec("Mirror Table", 30, 3, m: [("steel", 24), ("glass", 12)], p: [("Control Unit", 2), ("Lens", 2)]),
        Rec("Line Layer", 30, 3, m: [("steel", 30), ("copper", 6)], p: [("Control Unit", 2), ("Servo Motor", 2)]),
        Rec("Replacer", 30, 3, m: [("steel", 26), ("copper", 6)], p: [("Control Unit", 2), ("Servo Motor", 3)]),
        Rec("Build Gantry", 60, 3, m: [("steel", 70), ("copper", 12)], p: [("Control Unit", 4), ("Servo Motor", 4), ("Lens", 4)]),
        Rec("Selector", 30, 3, m: [("steel", 20), ("circuits", 6)], p: [("Control Unit", 3), ("Lens", 1)]),
        Rec("Blueprint Desk", 30, 3, m: [("steel", 20), ("laminate", 6)], p: [("Control Unit", 3), ("Circuit Board", 2)]),
        Rec("Foundation", 15, 1, m: [("concrete", 100), ("steel", 10)]),
        Rec("Frame Block", 4, 1, m: [("steel", 6)]),
        Rec("Frame Beam", 6, 1, m: [("steel", 14)]),
        Rec("Panel", 3, 1, m: [("steel", 4)]),
        Rec("Wood Panel", 2, 1, m: [("laminate", 4)])
    ];

    /// <summary>
    /// An Ordnance Rack preset: its cost goes with its stations and their size (the cube root of a station's volume against the old light
    /// one's, 25 cm by 1 m, as ModularRack.Cost does); a drum adds its gears, a big rack its wiring.
    /// </summary>
    private static PyDict RackRecipe(string name, string layout, int a, int b, double length, double diameter)
    {
        var n = a * b;
        var g = Math.Pow(diameter * diameter * length / 0.0625, 1 / 3.0);
        var steel = (int)Math.Min(2000, Py.RoundInt(3 + 3 * n * Math.Pow(g, 2.3)));
        PyDict m, extra = new();
        int tier;
        double seconds;
        if (g <= 1.6)
            (m, tier, seconds) = (new PyDict { ["steel"] = steel }, 2, 3 + n * g);
        else if (g <= 4)
        {
            (m, tier, seconds) = (new PyDict { ["steel"] = steel, ["copper"] = 2 }, 3, 6 + 2 * n * g);
            extra["Circuit Board"] = 1;
        }
        else
        {
            (m, tier, seconds) = (new PyDict { ["steel"] = steel, ["copper"] = 6 }, 4, 20 + 5 * n * g);
            extra["Control Unit"] = 1;
        }
        if (layout == "drum")
            extra["Gear Set"] = g <= 1.6 ? 1 : 2;
        var s = Py.Round(seconds, 1);
        return RecD(name, s > 600 ? 600 : s, tier, 1, m, extra.Count > 0 ? extra : null);
    }

    // What a role adds, beyond the mass in material. Parts per 50 kg, at least one.
    private static readonly Dictionary<string, string> RoleParts = new()
    {
        ["engine"] = "Piston Set", ["transmission"] = "Gear Set", ["differential"] = "Gear Set", ["transfer"] = "Gear Set", ["joint"] = "Servo Motor",
        ["battery"] = "Control Unit", ["alternator"] = "Copper Coil", ["logic"] = "Circuit Board", ["hose"] = "Wiring Loom",
        ["gun"] = "Gear Set", ["suspension"] = "Bearing", ["seat"] = "Tyre Carcass", ["hinge"] = "Bearing", ["door"] = "Bearing", ["wheel"] = "Tyre Carcass",
        ["enginePiece"] = "Piston Set", ["generator"] = "Copper Coil", ["thruster"] = "Gear Set",
    };

    // What a part's class costs: (material, share of the part's mass as units of it per kg, second material or null).
    private static readonly Dictionary<string, (string Material, double Share, string? Second)> Class = new()
    {
        ["steel"] = ("steel", 0.2, null), ["armour"] = ("steel", 0.25, "bronze"), ["wood"] = ("planks", 1.0, "laminate"), ["concrete"] = ("concrete", 0.17, "steel"),
        ["rubber"] = ("rubber", 0.3, "steel"), ["glass"] = ("glass", 0.2, "steel"), ["cloth"] = ("cloth", 1.0, "steel"), ["sand"] = ("sand", 0.2, "planks"),
    };

    private static readonly Dictionary<string, int> TierOf = new()
    {
        ["steel"] = 1, ["concrete"] = 1, ["planks"] = 1, ["laminate"] = 1, ["rubber"] = 1, ["cloth"] = 1, ["glass"] = 1, ["sand"] = 1, ["bronze"] = 2, ["copper"] = 2, ["circuits"] = 3,
    };

    private static readonly Dictionary<string, int> PartTier = new()
    {
        ["Thrust Chamber"] = 1, ["Turbine Stage"] = 2, ["Field Coil"] = 3, ["Levite Core"] = 4, ["Gear Set"] = 1, ["Tyre Carcass"] = 1, ["Bearing"] = 2, ["Piston Set"] = 2,
        ["Copper Coil"] = 2, ["Wiring Loom"] = 2, ["Servo Motor"] = 2, ["Circuit Board"] = 3, ["Control Unit"] = 3,
    };

    private static readonly string[] SkipFolders = ["Spawn", "Components", "Press/Dies", "Wings", "Lore"]; // (dies come from a Die Lathe, or the start kit; the colony's lore items are its own)
    // (the old ammo modules: a box's setting does it now) (a data centre's terminal is found, never made: docs/story/FRAGMENTS.md)
    private static readonly string[] SkipNames = ["Material Bundle", "Construct Core", "Data Centre Terminal", "Chest Lid", "Lid Hinge", "FMJ Rounds", "AP Rounds", "HE Rounds", "Incendiary Rounds"];
    private const int MaxUnits = 2000;
    // The lowest Assembler for a size class; a part's cost tops out at MaxUnits, its time at 600 s.
    private static readonly Dictionary<string, int> ClassTier = new() { ["small"] = 1, ["medium"] = 2, ["large"] = 3, ["capital"] = 4 };

    private static int RInt(double v) => (int)Py.RoundInt(v);

    private static int Int(object? v) => (int)Py.D(v);

    private static bool IsItem(PyDict p) => (p.Get("tool") as string ?? "wrench") != "welder" && Py.D(p.Get("mass") ?? 0) <= 40 && !Py.Truthy(p.Get("physicsOnly"));

    /// <summary>A part's recipe by rule.</summary>
    private static PyDict LongTail(PyDict p)
    {
        var mass = Py.D(p["mass"]);
        var role = p.Get("role") as string ?? "frame";
        var (material, share, second) = Class.TryGetValue(p.Get("material") as string ?? "steel", out var cl) ? cl : Class["steel"];
        var units = Math.Min(MaxUnits, Math.Max(1, RInt(mass * share)));
        var m = new PyDict { [material] = units };
        if (second is not null)
            m[second] = Math.Min(MaxUnits, Math.Max(1, units / 4));
        var extra = new PyDict();
        var joint = p.Get("joint") as PyDict;
        if (RoleParts.TryGetValue(role, out var rp))
        {
            var rail = joint?.Get("type") as string == "swivel" ? "free" : (joint?.Get("rail") as PyDict)?.Get("drive") as string;
            extra[rail == "free" ? "Bearing" : rail == "shaft" ? "Gear Set" : rp] = Math.Min(20, Math.Max(1, RInt(mass / 50)));
        }
        if (role is "logic" or "battery" or "alternator" or "hose" && !m.Has("copper"))
            m["copper"] = Math.Min(MaxUnits, Math.Max(1, RInt(mass / 3)));
        var ptier = p.Get("tier");
        if (role == "storage" && Py.Truthy(ptier))
        {
            // A machine of a tier is built from the tier below it in spirit: steel, a control unit from tier 3 up.
            var t = Int(ptier);
            m = new PyDict { ["steel"] = Math.Min(MaxUnits, RInt(mass / 8)), ["concrete"] = Math.Min(MaxUnits, RInt(mass / 16)) };
            extra = new PyDict { ["Gear Set"] = Math.Min(20, 2 * t) };
            if (t >= 2)
                extra["Wiring Loom"] = Math.Min(20, t);
            else
                m["rubber"] = Math.Min(MaxUnits, RInt(mass / 40)); // (a tier 1 machine asks nothing a tier 1 machine can't make, so survival can start)
            if (t >= 3)
                extra["Control Unit"] = Math.Min(20, t - 2);
        }
        if (role == "enginePiece" && (p.Get("enginePiece") as PyDict)?.Get("kind") as string == "cylinder" && Int(ptier ?? 1) >= 3)
        {
            // The Engine Kit's tier cylinders (Bored 3, Performance 4, Racing 5: Source/EngineKit/EngineKitParts.cs) climb the tech tree:
            // bronze bearing shells and Bearings from tier 3, copper and a Circuit Board from 4, gold and a Control Unit at 5 (six inputs at most).
            var t = Int(ptier);
            var u = Int(m[material]);
            var pistons = extra.Has("Piston Set") ? Int(extra["Piston Set"]) : 1;
            m["bronze"] = Math.Min(MaxUnits, Math.Max(1, u / 4));
            extra["Bearing"] = Math.Min(20, Math.Max(1, pistons / 2));
            if (t == 4)
            {
                m["copper"] = Math.Min(MaxUnits, Math.Max(1, u / 6));
                extra["Circuit Board"] = Math.Min(20, Math.Max(1, pistons / 4));
            }
            else if (t >= 5)
            {
                m["gold"] = Math.Min(MaxUnits, Math.Max(1, u / 10));
                extra["Control Unit"] = Math.Min(20, Math.Max(1, pistons / 4));
            }
        }
        if (m.Count + extra.Count < 2 && mass >= 3)
            // Two chains at least: anything of steel has a little rubber or concrete in its fixings (fixings stay fixings on a 240 t crank).
            m[mass < 12 ? "bronze" : role != "frame" ? "rubber" : "concrete"] = Math.Min(MaxUnits / 4, Math.Max(1, RInt(mass / 25)));
        var size = p.Get("sizeClass") as string ?? "small";
        var tier = m.Keys.Select(k => TierOf.GetValueOrDefault(k, 1)).Concat(extra.Keys.Select(k => PartTier.GetValueOrDefault(k, 1)))
            .Append(Math.Min(5, Int(ptier ?? 1))).Append(ClassTier[size]).Max();
        var seconds = Math.Min(size == "small" ? 180.0 : 600.0, Py.Round(2 + mass * 0.03, 1));
        return RecD((string)p["name"]!, seconds, tier, 1, m, extra.Count > 0 ? extra : null);
    }

    // Flight parts by machine (docs/sandbox/CONSTRUCTS.md "Flight in survival"). Wings and control surfaces have no recipe: an Airframe Former
    // cuts its own, and the part files are found on wrecks. These go to one family of Assembler, built from their own components.
    private static readonly string[] ThrusterFolders = ["Boost/Jets", "Boost/Rockets", "Boost/Repulsors", "BigDrive/Thrusters"];
    private static readonly string[] Turbines = ["Turbojet", "Jet", "Lift Fan"];
    private static readonly (string, string)[] Cores = [("Repulsor", "Field Coil"), ("Anti-Grav Generator", "Field Coil"), ("Rocket Engine", "Thrust Chamber"), ("Solid Booster", "Thrust Chamber")];

    /// <summary>Which family builds a part: "" is the plain Assembler.</summary>
    private static string MachineOf(PyDict p, string folder)
    {
        if (ThrusterFolders.Contains(folder))
            return "thruster";
        if (folder == "Air" && p.Get("role") as string == "fluidtank" && p.Get("material") as string == "cloth")
            return "tailor";
        return "";
    }

    /// <summary>A rule's recipe, moved to its machine: a thruster's bulk is its casing, and the parts that make it work come from the Assembler.</summary>
    private static PyDict FlightRecipe(PyDict p, string folder)
    {
        var r = LongTail(p);
        r["machine"] = MachineOf(p, folder);
        if ((string)r["machine"]! != "thruster")
            return r;
        var name = (string)p["name"]!;
        var mass = Py.D(p["mass"]);
        var core = Cores.Where(c => name.EndsWith(c.Item1, StringComparison.Ordinal)).Select(c => c.Item2).FirstOrDefault()
            ?? (Turbines.Any(t => name.EndsWith(t, StringComparison.Ordinal)) ? "Turbine Stage" : null);
        if (core is null)
            return r; // (a Decoupler: bolts)
        var n = Math.Min(20, Math.Max(1, RInt(mass / 30)));
        var extra = new PyDict { [core] = n };
        if (core == "Turbine Stage")
            extra["Bearing"] = n;
        if (!name.Contains("Solid Booster"))
            extra["Wiring Loom"] = Math.Min(20, Math.Max(1, n / 2));
        if (core == "Field Coil")
            extra["Control Unit"] = Math.Min(20, Math.Max(1, n / 4));
        if (name.Contains("Anti-Grav"))
            extra["Levite Core"] = n;
        r["parts"] = extra;
        var mats = (PyDict)r["materials"]!;
        r["materials"] = new PyDict { ["steel"] = mats.Has("steel") ? mats["steel"] : Math.Min(MaxUnits, Math.Max(1, RInt(mass * 0.2))) };
        r["minTier"] = new[] { r.Has("minTier") ? Int(r["minTier"]) : 1, extra.Keys.Max(k => PartTier.GetValueOrDefault(k, 1)), name.Contains("Anti-Grav") ? 4 : 1 }.Max();
        return r;
    }

    private static object? R4(object? v) => v is int or long ? v : Py.Round(Py.D(v), 4);

    private static PyDict Component((string Name, string File, object Mass, string Colour, (double[] Lo, double[] Hi)[] Shapes, string Desc) c)
    {
        // (a 0 in a corner stays an int, as Python's list(lo) of the table's tuples kept it)
        static List<object?> L(double[] v) => [.. v.Select(x => x == 0 && !double.IsNegative(x) ? (object?)0 : x)];
        var boxes = c.Shapes.Select(s => new PyDict { ["min"] = L(s.Lo), ["max"] = L(s.Hi) }).ToList();
        var sockets = new List<PyDict> { new() { ["name"] = "bottom", ["at"] = Py.L(0, 0, 0), ["dir"] = "-y" } };
        PartZFight.Resolve(boxes, sockets);
        return new PyDict
        {
            ["format"] = "call-of-slop-construct-part/1", ["name"] = c.Name, ["description"] = c.Desc, ["group"] = "Production", ["section"] = "Components",
            ["role"] = "panel", ["tool"] = "wrench", ["attachSeconds"] = 1.0, ["mass"] = c.Mass, ["health"] = 40, ["material"] = "steel", ["color"] = c.Colour,
            ["boxes"] = boxes.Select(b => (object?)new PyDict
            {
                ["min"] = ((List<object?>)b["min"]!).Select(R4).ToList(), ["max"] = ((List<object?>)b["max"]!).Select(R4).ToList(),
            }).ToList(),
            ["sockets"] = sockets.Cast<object?>().ToList(),
        };
    }

    /// <summary>The recipes file: the header, then one recipe a line (json.dumps with its default separators).</summary>
    public static string Layout(IEnumerable<PyDict> recipes) =>
        "{\n  \"format\": \"" + Format + "\",\n  \"about\": " + PyJson.Dumps(About) + ",\n  \"recipes\": [\n"
        + string.Join(",\n", recipes.Select(r => "    " + PyJson.Dumps(r))) + "\n  ]\n}\n";

    public void Make(PartOutput o)
    {
        foreach (var c in Components)
            o.File($"Constructs/Parts/Components/{c.File}.json", PyJson.Dumps(Component(c), 2) + "\n");
        var known = new List<(PyDict P, string Folder)>();
        var root = Path.Combine(o.Root, "Constructs", "Parts");
        foreach (var f in Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(o.Root, f).Replace('\\', '/')).Order(StringComparer.Ordinal))
        {
            var p = (PyDict)o.ReadJson(f)!;
            if (Py.Truthy(p.Get("toolOnly")) || p.Get("role") as string == "rotor")
                continue; // (a cylinder's head: the tool that makes it pays; a rotor comes off a Fan Former, not an Assembler)
            var dir = f[..f.LastIndexOf('/')];
            known.Add((p, dir == "Constructs/Parts" ? "." : dir["Constructs/Parts/".Length..]));
        }
        var recipes = Hand();
        recipes.AddRange(UpgradeParts.Recipes.Select(r => Rec(r.Output, r.Seconds, r.Tier, m: r.Materials, p: r.Parts)));
        recipes.AddRange(AirWeaponsParts.OrdnanceRacks.Select(r => RackRecipe(r.Name, r.Layout, r.A, r.B, r.Length, r.Diameter)));
        var hand = recipes.Select(r => (string)r["output"]!).ToHashSet();
        var tail = new List<PyDict>();
        foreach (var (p, folder) in known)
        {
            var name = (string)p["name"]!;
            if (hand.Contains(name) || Py.Truthy(p.Get("stage")) || SkipNames.Contains(name) || SkipFolders.Any(s => folder.StartsWith(s, StringComparison.Ordinal))
                || Py.Truthy(p.Get("legacy")) && (folder.StartsWith("AirWeapons", StringComparison.Ordinal) || folder.StartsWith("Ordnance", StringComparison.Ordinal)))
                continue;
            tail.Add(MachineOf(p, folder) != "" ? FlightRecipe(p, folder) : LongTail(p));
        }
        // (stable: a family's recipes are in file order, and the plain Assembler's keep their places)
        recipes.AddRange(tail.OrderBy(r => r.Get("machine") as string ?? "", StringComparer.Ordinal));
        var byName = new Dictionary<string, PyDict>();
        foreach (var (p, _) in known)
            byName[(string)p["name"]!] = p;
        var builder = BuilderNames(o.Root);
        foreach (var r in recipes)
        {
            var output = (string)r["output"]!;
            if (!byName.ContainsKey(output) && !builder.Contains(output))
                throw new InvalidOperationException($"assembling: recipe for {output}: no part named {output} (in Constructs/Parts or the weapon builder's Parts)");
            var n = (r.Get("materials") as PyDict)?.Count ?? 0;
            var parts = r.Get("parts") as PyDict;
            if (n + (parts?.Count ?? 0) > 6)
                throw new InvalidOperationException($"assembling: {r["output"]}: too many inputs");
            foreach (var q in parts?.Keys ?? [])
                if (!byName.TryGetValue(q, out var qp) || !IsItem(qp))
                    throw new InvalidOperationException($"assembling: {r["output"]}: {q} isn't an item part");
        }
        // Press phase 5: a single-piece part a die presses leaves the Assembler. Its row goes (phase 5b, the user's answer 4: an Assembler's
        // recipe setting is saved as 1 + its place in its family's list, so a saved Assembler past a removed row reads another recipe; accepted,
        // tools/MIGRATING.md). The picker, the Technologies page and a blueprint naming it find the die from the presets instead.
        var presets = PressParts.PresetParts();
        for (var k = recipes.Count - 1; k >= 0; k--)
        {
            var output = (string)recipes[k]["output"]!;
            if (!PressedNow(output, recipes[k]))
                continue;
            if (!presets.Contains(output))
                throw new InvalidOperationException($"assembling: {output} is to be pressed, but no die has a preset for it (PressParts.Presets)");
            recipes.RemoveAt(k);
        }
        o.File("Constructs/Assembling.json", Layout(recipes));
    }

    // ---- Press phase 5 (docs/sandbox/design/press-rework.md section 2, as the user's answers of section 12 cut it down)

    // The parts a die presses from one blank (a preset in PressParts) that leave the Assembler. Not here, and why:
    // Differential and Proximity Sensor take two kinds of part (an assembly by the sort's rule); the engine cylinders are engine pieces, sized in
    // their own phase (the user's answer 2, 2026-11-13). Suspension, Drive Shaft 1 m, U Joint, CV Joint, Stub Axle and Wheel Hub moved on
    // 2026-11-13, once their patterns took a Gear Set in bay 2 in place of Bearings (bronze is tier 2); the Frame Beam the same day as the
    // Beam die's girder (answer 3; the start kit carries a Beam Die). The other building pieces (Parts/Building, Parts/Capital) stay.
    private static readonly HashSet<string> Moved = new(StringComparer.Ordinal)
    {
        "L Bracket", "Corner Bracket", "Gusset 0.25 m", "Gusset 0.5 m",
        "Button", "Switch", "Gear Set", "Circuit Board", "Piston Set",
        "5-Speed Gearbox", "CVT", "Automatic Gearbox", "Reversing Gearbox", "Limited-Slip Differential", "Locking Differential",
        "Steel Plate", "Sheet Steel 1 m", "Plate Steel 1 m", "Tread Plate 1 m", "Mesh Grate 1 m", "Armour Plate 1 m",
        "Piston 1x1 Medium", "Motor Hinge", "Turntable", "Free Pivot 1x1", "Leaf Spring", "Rigid Mount", "Light Suspension",
        "Suspension", "Drive Shaft 1 m", "U Joint", "CV Joint", "Stub Axle", "Wheel Hub",
        "Wiring Loom", "Copper Coil", "Power Lead Reel", "Data Lead Reel", "Fluid Hose Reel", "Fuel Line",
        "AND Gate", "NOT Gate", "Timer", "Delay", "Compare", "Speed Sensor",
        "Fuel Tank", "Small Fuel Tank", "Fuel Drum", "Fluid Tank", "Small Fluid Tank",
        "Frame Beam",
    };

    /// <summary>A recipe that leaves the plain Assembler for a press: one of <see cref="Moved"/>, or a Housing (the Housing die presses all 21).</summary>
    private static bool PressedNow(string output, PyDict r) =>
        r.Get("machine") == null && (Moved.Contains(output) || output.EndsWith(" m", StringComparison.Ordinal) && output.Contains("Housing ", StringComparison.Ordinal));

    /// <summary>The weapon builder's parts (Parts/*/*.json outside _*, Builds and Tables): made into Sandbox items at load, so a hand
    /// recipe may name one with no file in Constructs/Parts.</summary>
    private static HashSet<string> BuilderNames(string root)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        var dir = Path.Combine(root, "Parts");
        if (!Directory.Exists(dir))
            return names;
        foreach (var sub in Directory.GetDirectories(dir))
        {
            var leaf = Path.GetFileName(sub);
            if (leaf.StartsWith('_') || leaf.StartsWith("Builds", StringComparison.Ordinal) || leaf.StartsWith("Tables", StringComparison.Ordinal))
                continue;
            foreach (var f in Directory.GetFiles(sub, "*.json"))
            {
                var m = System.Text.RegularExpressions.Regex.Match(System.IO.File.ReadAllText(f), "\"name\"\\s*:\\s*\"([^\"]+)\"");
                if (m.Success)
                    names.Add(m.Groups[1].Value);
            }
        }
        return names;
    }
}
