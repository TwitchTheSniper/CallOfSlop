namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// Assembler parts to the press (Sean, 2026-11-22: "Move assembler parts to the press where they make sense"). The press sweep
/// (--press-sweep) listed 389 Assembler rows a die could press; these are the ones that are pressed parts in truth: sized families of a
/// kind a die already makes (more hinges, pistons, turntables, pivots, suspensions, plates, sheets, shafts, tanks, beams), and kinds of
/// the same make as a die's (logic blocks and sensors on the Control die, switches on the Button die, braces and frame nodes on the
/// Bracket die, gearboxes on the Drive die). Left on the Assembler: assemblies of unlike parts (hub motors, receivers, belt and chain
/// drives, clutches, speakers, turbine stages, coils, cores), cut panels (frames, vents, triangles: the Laser Cutter's), glass and wood,
/// the building and capital frames past a 2 m girder or column (trusses, buttresses, corbels, nodes, 4 m sections), the ship generators'
/// hull and wall pieces, and the odd matches by size alone (a Bearing or a Lens on the Gear die, a Driving Module on the Button die).
///
/// A new kind is a pattern appended to its die's list (a Press saves its Pattern setting as 1 + the pattern's place: never insert), never
/// Auto's ("auto": false, as the generated receivers: a press on Auto makes of a blank what it made before; the Pattern setting or an order reaches it),
/// its like part the kind's first size, whose preset is the pattern's own blank. The other sizes are presets on that pattern, each blank
/// the part's bounds on the 5 cm lattice, held to the die's ranges (at least 10 cm across; a Medium Girder 1 m is 50 cm across, the
/// Beam die pressing at least twice as long as across). Grades of one size (kart to truck suspension arms) take a thicker blank each,
/// as a heavier arm is cut from heavier stock. Their Assembler rows go (Assembling.PressedNow); as in press phase 5b, a saved
/// Assembler set to a recipe past a removed row reads another one (tools/MIGRATING.md).
/// </summary>
public sealed partial class PressParts
{
    /// <summary>The new kinds: die, pattern id, like part (its preset is the blank given), label, blank, the blanks the pattern takes (null: the die's).</summary>
    private static readonly (string Die, string Id, string Like, string Label, string Blank, string[]? Only)[] MorePatterns =
    [
        ("bracket", "brace12", "Brace 1:2 0.5x1 m", "brace 1:2", "plate 50x10x100 steel", ["plate"]),
        ("bracket", "brace45", "Brace 45 1 m", "brace 45", "plate 100x10x100 steel", ["plate"]),
        ("bracket", "cross", "Cross Brace 1 m", "cross brace", "plate 100x10x100 steel", ["plate"]),
        ("bracket", "corner3", "3-Way Corner", "3-way corner", "solid 50x50x50 steel", ["solid"]),
        ("bracket", "hub6", "6-Way Hub", "6-way hub", "solid 75x75x75 steel", ["solid"]),
        ("bracket", "cornerjn", "Corner Junction", "corner junction", "solid 25x50x50 steel", ["solid"]),
        ("bracket", "crossjn", "Cross Junction", "cross junction", "solid 25x75x75 steel", ["solid"]),
        ("bracket", "tjunction", "T Junction", "T junction", "solid 25x50x75 steel", ["solid"]),
        ("bracket", "endcap", "Beam End Cap", "beam end cap", "plate 25x10x25 steel", ["plate"]),
        ("button", "keyswitch", "Key Switch", "key switch", "plate 20x10x20 laminate", null),
        ("button", "mushroom", "Mushroom Button", "mushroom button", "plate 20x10x20 laminate", null),
        ("button", "rocker", "Rocker Switch", "rocker switch", "plate 20x10x20 laminate", null),
        ("button", "knob", "Rotary Knob", "rotary knob", "plate 20x10x20 laminate", null),
        ("button", "slider", "Slider", "slider", "plate 20x10x20 laminate", null),
        ("button", "toggleswitch", "Toggle Switch", "toggle switch", "plate 20x10x20 laminate", null),
        ("button", "selector", "Gear Selector", "gear selector", "plate 20x10x20 laminate", null),
        ("button", "reader", "Input Reader", "input reader", "plate 20x10x20 laminate", null),
        ("button", "bakelite", "Bakelite Switch", "bakelite switch", "plate 15x10x15 laminate", null),
        ("button", "lever", "Lever", "lever", "plate 20x10x20 laminate", null),
        ("control", "altitude", "Altitude Sensor", "altitude sensor", "solid 20x20x25 laminate", null),
        ("control", "damage", "Damage Sensor", "damage sensor", "solid 20x20x25 laminate", null),
        ("control", "heading", "Heading Sensor", "heading sensor", "solid 20x20x25 laminate", null),
        ("control", "level", "Level Sensor", "level sensor", "solid 20x20x25 laminate", null),
        ("control", "occupancy", "Occupancy Sensor", "occupancy sensor", "solid 20x20x25 laminate", null),
        ("control", "tilt", "Tilt Sensor", "tilt sensor", "solid 20x20x25 laminate", null),
        ("control", "filter", "Filter", "filter", "solid 20x20x25 laminate", null),
        ("control", "math", "Math Block", "math block", "solid 20x20x25 laminate", null),
        ("control", "mux", "Multiplexer", "multiplexer", "solid 20x20x25 laminate", null),
        ("control", "nand", "NAND Gate", "NAND gate", "solid 20x20x25 laminate", null),
        ("control", "or", "OR Gate", "OR gate", "solid 20x20x25 laminate", null),
        ("control", "xor", "XOR Gate", "XOR gate", "solid 20x20x25 laminate", null),
        ("control", "random", "Random", "random", "solid 20x20x25 laminate", null),
        ("control", "scale", "Scale", "scale", "solid 20x20x25 laminate", null),
        ("control", "toggle", "Toggle", "toggle", "solid 20x20x25 laminate", null),
        ("actuator", "swivel", "Free Swivel 1x1", "free swivel", "solid 25x25x25 steel", ["solid"]),
        ("spring", "adjustable", "Adjustable Suspension", "adjustable suspension", "solid 10x30x30 steel", null),
        ("spring", "hydraulic", "Hydraulic Suspension", "hydraulic suspension", "solid 10x30x30 steel", null),
        ("spring", "longtravel", "Long-Travel Suspension", "long-travel suspension", "solid 10x30x30 steel", null),
        ("spring", "stiff", "Stiff Suspension", "stiff suspension", "solid 10x30x30 steel", null),
        ("spring", "wishbone", "Double Wishbone", "double wishbone", "solid 20x75x75 steel", null),
        ("spring", "swingarm", "Swing Arm", "swing arm", "solid 20x50x75 steel", null),
        ("spring", "trailing", "Trailing Arm", "trailing arm", "solid 20x25x75 steel", null),
        ("spring", "pogo", "Pogo Strut", "pogo strut", "solid 20x25x125 steel", null),
        ("cable", "clamp", "Cable Clamp x1", "cable clamp", "round 10x10x20 steel", ["round"]),
        ("beam", "rod", "Steel Rod 1 m", "rod", "round 10x10x100 steel", ["round"]),
        ("beam", "tube", "Steel Tube 1 m", "tube", "pipe5 15x15x100 steel", ["pipe"]),
        ("beam", "angle", "Angle Iron 1 m", "angle", "l-angle 25x25x100 steel", ["l-angle"]),
        ("beam", "channel", "Channel 1 m", "channel", "c-channel 25x25x100 steel", ["c-channel"]),
        // Leg pods (Sean, 2026-11-22: pressed without the Bearings and concrete their Assembler rows took): one pattern a piece, its S size
        // the like part, a like body (the piece's own boxes and leaves) so a sized one still opens as its door does.
        ("bracket", "podbrace", "Leg Pod Brace S", "leg pod brace", "solid 10x10x10 steel", ["solid"]),
        ("bracket", "podfoot", "Leg Pod Foot S", "leg pod foot", "solid 20x40x50 steel", ["solid"]),
        ("bracket", "podstrut", "Leg Pod Strut S", "leg pod strut", "solid 10x10x15 steel", ["solid"]),
        ("plate", "canopyport", "Leg Pod Canopy Port S", "pod canopy port", "plate 30x10x180 steel", null),
        ("plate", "canopystbd", "Leg Pod Canopy Stbd S", "pod canopy stbd", "plate 30x10x180 steel", null),
        ("beam", "podcarriage", "Leg Pod Carriage S", "leg pod carriage", "solid 10x10x55 steel", ["solid"]),
        ("housing", "podhousing", "Leg Pod Housing S", "leg pod housing", "hollow 50x70x225 steel", ["hollow"]),
        // Ship works (the Zero Day works' yard station found no maker for them, 2026-11-22; Karl, 2026-11-16: no Assembler recipes, the new
        // production system makes them): a drive's cylinder sections and the capacitor are pressed housings, a turret ring a big turntable.
        // Each pattern presses its catalogue sizes only (PressPattern.PresetsOnly: a stretched copy would keep its like's thrust, charge or race).
        ("housing", "drivecore", "Drive Core XS", "drive core", "hollow 85x80x110 steel", ["hollow"]),
        ("housing", "fuelpump", "Fuel Pump M", "fuel pump", "hollow 220x190x100 steel", ["hollow"]),
        ("housing", "exchanger", "Coolant Exchanger L", "coolant exchanger", "hollow 230x220x115 steel", ["hollow"]),
        ("housing", "capacitor", "Capacitor Bank S", "capacitor bank", "solid 80x110x70 steel", ["solid"]),
        ("actuator", "turretring", "Turret Ring S", "turret ring", "solid 100x100x95 steel", ["solid"]),
        // Engine trains (Sean, 2026-11-24, "Ship engines with interior parts"): the minimum viable engine's three pieces, each a pressed housing
        // at any length and diameter (EngineTrainPattern: what it serves scales with the blank, so these are not presets-only).
        ("housing", "reactionchamber", "Reaction Chamber XS", "reaction chamber", "hollow 70x70x110 steel", ["hollow"]),
        ("housing", "fuelinjector", "Fuel Injector XS", "fuel injector", "hollow 70x70x40 steel", ["hollow"]),
        ("housing", "drivenozzle", "Drive Nozzle XS", "drive nozzle", "hollow 85x85x100 steel", ["hollow"]),
        // ...and its function pieces (phase 3): a Spacer, a Fuel Buffer and a Cooling Jacket, at any size too.
        ("housing", "trainspacer", "Train Spacer XS", "train spacer", "hollow 70x70x40 steel", ["hollow"]),
        ("housing", "fuelbuffer", "Fuel Buffer XS", "fuel buffer", "hollow 75x75x50 steel", ["hollow"]),
        ("housing", "coolingjacket", "Cooling Jacket XS", "cooling jacket", "hollow 75x75x60 steel", ["hollow"]),
        ("housing", "igniter", "Igniter XS", "igniter", "hollow 70x70x35 steel", ["hollow"]),
        ("housing", "sensorring", "Sensor Ring XS", "sensor ring", "hollow 70x70x35 steel", ["hollow"]),
        ("housing", "throttlevalve", "Throttle Valve XS", "throttle valve", "hollow 70x70x40 steel", ["hollow"]),
        ("housing", "mixture", "Mixture Controller XS", "mixture controller", "hollow 70x70x40 steel", ["hollow"]),
    ];

    /// <summary>The new gearboxes and axles, appended to <see cref="DrivePatterns"/> (each its like part's preset).</summary>
    private static readonly (string Id, string Like, string Label, string Blank)[] MoreDrivePatterns =
    [
        ("lowrange", "Low-Range Gearbox", "low-range gearbox", "solid 50x50x60 steel"),
        ("reduction", "Reduction Gearbox", "reduction gearbox", "solid 40x40x50 steel"),
        ("rightangle", "Right-Angle Gearbox", "right-angle gearbox", "solid 50x50x50 steel"),
        ("sequential", "Sequential Gearbox", "sequential gearbox", "solid 50x50x60 steel"),
        ("tgearbox", "T Gearbox", "T gearbox", "solid 50x50x50 steel"),
        ("transmission", "Transmission", "transmission", "solid 50x50x50 steel"),
        ("liveaxle", "Live Axle", "live axle", "hollow 50x50x160 steel"),
    ];

    /// <summary>The other sizes and grades: part, die, pattern, blank.</summary>
    private static readonly (string Part, string Die, string Pattern, string Blank)[] MorePresets =
    [
        ("Brace 1:2 1x2 m", "bracket", "brace12", "plate 100x10x200 steel"),
        ("Brace 45 0.5 m", "bracket", "brace45", "plate 50x10x50 steel"),
        ("Brace 45 2 m", "bracket", "brace45", "plate 200x10x200 steel"),
        ("Cross Brace 0.5 m", "bracket", "cross", "plate 50x10x50 steel"),
        ("Motor Hinge 1x1 Short", "actuator", "hinge", "solid 20x25x30 steel"),
        ("Motor Hinge 1x1 Long", "actuator", "hinge", "solid 20x25x80 steel"),
        ("Motor Hinge 2x2 Short", "actuator", "hinge", "solid 45x50x30 steel"),
        ("Motor Hinge 2x2 Medium", "actuator", "hinge", "solid 45x50x55 steel"),
        ("Motor Hinge 2x2 Long", "actuator", "hinge", "solid 45x50x80 steel"),
        ("Motor Hinge 3x3 Short", "actuator", "hinge", "solid 70x75x30 steel"),
        ("Motor Hinge 3x3 Medium", "actuator", "hinge", "solid 70x75x55 steel"),
        ("Motor Hinge 3x3 Long", "actuator", "hinge", "solid 70x75x80 steel"),
        ("Motor Hinge Medium", "actuator", "hinge", "solid 95x100x105 steel"),
        ("Piston", "actuator", "piston", "round 40x40x30 steel"),
        ("Piston 1x1 Short", "actuator", "piston", "round 25x25x25 steel"),
        ("Piston 1x1 Long", "actuator", "piston", "round 25x25x75 steel"),
        ("Piston 2x2 Short", "actuator", "piston", "round 50x50x25 steel"),
        ("Piston 2x2 Medium", "actuator", "piston", "round 50x50x50 steel"),
        ("Piston 3x3 Short", "actuator", "piston", "round 75x75x25 steel"),
        ("Piston 3x3 Medium", "actuator", "piston", "round 75x75x50 steel"),
        ("Piston 3x3 Long", "actuator", "piston", "round 75x75x75 steel"),
        ("Turntable 1x1 Short", "actuator", "turntable", "solid 25x25x20 steel"),
        ("Turntable 1x1 Medium", "actuator", "turntable", "solid 25x25x30 steel"),
        ("Turntable 1x1 Long", "actuator", "turntable", "solid 25x25x40 steel"),
        ("Turntable 2x2 Medium", "actuator", "turntable", "solid 50x50x30 steel"),
        ("Turntable 2x2 Long", "actuator", "turntable", "solid 50x50x40 steel"),
        ("Turntable 3x3 Short", "actuator", "turntable", "solid 75x75x20 steel"),
        ("Turntable 3x3 Medium", "actuator", "turntable", "solid 75x75x30 steel"),
        ("Turntable 3x3 Long", "actuator", "turntable", "solid 75x75x40 steel"),
        ("Turntable Medium", "actuator", "turntable", "solid 100x100x100 steel"),
        ("Free Pivot 2x2", "actuator", "pivot", "solid 50x50x50 steel"),
        ("Free Pivot 3x3", "actuator", "pivot", "solid 75x75x75 steel"),
        ("Free Pivot Medium", "actuator", "pivot", "solid 100x100x100 steel"),
        ("Free Swivel 2x2", "actuator", "swivel", "solid 50x50x25 steel"),
        ("Free Swivel 3x3", "actuator", "swivel", "solid 75x75x40 steel"),
        ("Free Swivel Medium", "actuator", "swivel", "solid 100x100x50 steel"),
        ("Kart Suspension", "spring", "suspension", "solid 10x25x25 steel"),
        ("Heavy Suspension", "spring", "suspension", "solid 20x25x25 steel"),
        ("Truck Suspension", "spring", "suspension", "solid 25x25x30 steel"),
        ("Medium Suspension", "spring", "suspension", "solid 45x100x100 steel"),
        ("Kart Double Wishbone", "spring", "wishbone", "solid 10x50x75 steel"),
        ("Light Double Wishbone", "spring", "wishbone", "solid 15x75x75 steel"),
        ("Heavy Double Wishbone", "spring", "wishbone", "solid 25x75x100 steel"),
        ("Truck Double Wishbone", "spring", "wishbone", "solid 30x75x150 steel"),
        ("Kart Swing Arm", "spring", "swingarm", "solid 10x50x50 steel"),
        ("Light Swing Arm", "spring", "swingarm", "solid 15x50x75 steel"),
        ("Heavy Swing Arm", "spring", "swingarm", "solid 25x50x100 steel"),
        ("Truck Swing Arm", "spring", "swingarm", "solid 30x50x150 steel"),
        ("Kart Trailing Arm", "spring", "trailing", "solid 10x25x50 steel"),
        ("Light Trailing Arm", "spring", "trailing", "solid 15x25x75 steel"),
        ("Heavy Trailing Arm", "spring", "trailing", "solid 25x25x100 steel"),
        ("Truck Trailing Arm", "spring", "trailing", "solid 30x25x150 steel"),
        ("Kart Pogo Strut", "spring", "pogo", "solid 10x25x75 steel"),
        ("Light Pogo Strut", "spring", "pogo", "solid 15x25x100 steel"),
        ("Heavy Pogo Strut", "spring", "pogo", "solid 25x25x150 steel"),
        ("Cable Clamp x2", "cable", "clamp", "round 10x10x35 steel"),
        ("Cable Clamp x3", "cable", "clamp", "round 10x10x45 steel"),
        ("Drive Shaft 0.5 m", "shaft", "shaft", "round 20x20x50 steel"),
        ("Drive Shaft 2 m", "shaft", "shaft", "round 20x20x200 steel"),
        ("Wide Differential", "drive", "diff", "hollow 50x50x160 steel"),
        ("Large Fuel Tank", "tank", "fuel", "hollow 50x50x100 steel"),
        ("Large Fluid Tank", "tank", "fluid", "hollow 50x50x100 steel"),
        ("Sheet Steel 0.25 m", "plate", "sheet", "plate 25x10x25 steel"),
        ("Sheet Steel 0.5 m", "plate", "sheet", "plate 50x10x50 steel"),
        ("Sheet Steel 0.5x1 m", "plate", "sheet", "plate 50x10x100 steel"),
        ("Sheet Steel 1x2 m", "plate", "sheet", "plate 100x10x200 steel"),
        ("Sheet Steel 2 m", "plate", "sheet", "plate 200x10x200 steel"),
        ("Plate Steel 0.25 m", "plate", "plate", "plate 25x10x25 steel"),
        ("Plate Steel 0.5 m", "plate", "plate", "plate 50x10x50 steel"),
        ("Plate Steel 0.5x1 m", "plate", "plate", "plate 50x10x100 steel"),
        ("Plate Steel 1x2 m", "plate", "plate", "plate 100x10x200 steel"),
        ("Plate Steel 2 m", "plate", "plate", "plate 200x10x200 steel"),
        ("Tread Plate 0.5 m", "plate", "tread", "plate 50x10x50 steel"),
        ("Tread Plate 1x2 m", "plate", "tread", "plate 100x10x200 steel"),
        ("Tread Plate 2 m", "plate", "tread", "plate 200x10x200 steel"),
        ("Mesh Grate 0.5 m", "plate", "grate", "plate 50x10x50 steel"),
        ("Mesh Grate 1x2 m", "plate", "grate", "plate 100x10x200 steel"),
        ("Mesh Grate 2 m", "plate", "grate", "plate 200x10x200 steel"),
        ("Armour Plate 0.5 m", "plate", "armour", "plate 50x20x50 steel"),
        ("Armour Plate 0.5x1 m", "plate", "armour", "plate 50x20x100 steel"),
        ("Armour Plate 1x2 m", "plate", "armour", "plate 100x20x200 steel"),
        ("Armour Plate 2 m", "plate", "armour", "plate 200x20x200 steel"),
        ("Medium Armour Plate 1x1 m", "plate", "armour", "plate 100x40x100 steel"),
        ("Medium Armour Plate 2x1 m", "plate", "armour", "plate 100x40x200 steel"),
        ("Medium Armour Plate 2x2 m", "plate", "armour", "plate 200x40x200 steel"),
        ("Medium Armour Belt 1x1 m", "plate", "armour", "plate 100x65x100 steel"),
        ("Medium Armour Belt 2x1 m", "plate", "armour", "plate 100x65x200 steel"),
        ("Medium Armour Belt 2x2 m", "plate", "armour", "plate 200x65x200 steel"),
        ("I-Beam 1 m", "beam", "girder", "i-beam 25x25x100 steel"),
        ("I-Beam 1.5 m", "beam", "girder", "i-beam 25x25x150 steel"),
        ("I-Beam 2 m", "beam", "girder", "i-beam 25x25x200 steel"),
        ("Medium Girder 1 m", "beam", "girder", "i-beam 50x50x100 steel"),
        ("Medium Girder 2 m", "beam", "girder", "i-beam 100x100x200 steel"),
        ("Medium Girder 3 m", "beam", "girder", "i-beam 100x100x300 steel"),
        ("Medium Girder 4 m", "beam", "girder", "i-beam 100x100x400 steel"),
        ("Square Beam 1 m", "beam", "strut", "solid 25x25x100 steel"),
        ("Square Beam 1.5 m", "beam", "strut", "solid 25x25x150 steel"),
        ("Square Beam 2 m", "beam", "strut", "solid 25x25x200 steel"),
        ("Medium Column 1 m", "beam", "column", "hollow 50x50x100 steel"),
        ("Medium Column 2 m", "beam", "column", "hollow 100x100x200 steel"),
        ("Medium Column 4 m", "beam", "column", "hollow 100x100x400 steel"),
        ("Steel Rod 2 m", "beam", "rod", "round 10x10x200 steel"),
        ("Steel Tube 2 m", "beam", "tube", "pipe5 15x15x200 steel"),
        ("Angle Iron 2 m", "beam", "angle", "l-angle 25x25x200 steel"),
        ("Channel 2 m", "beam", "channel", "c-channel 25x25x200 steel"),
        ("Leg Pod Brace M", "bracket", "podbrace", "solid 10x10x15 steel"),
        ("Leg Pod Brace L", "bracket", "podbrace", "solid 15x15x15 steel"),
        ("Leg Pod Brace XL", "bracket", "podbrace", "solid 15x15x20 steel"),
        ("Leg Pod Foot M", "bracket", "podfoot", "solid 25x65x75 steel"),
        ("Leg Pod Foot L", "bracket", "podfoot", "solid 35x85x100 steel"),
        ("Leg Pod Foot XL", "bracket", "podfoot", "solid 45x105x125 steel"),
        ("Leg Pod Strut M", "bracket", "podstrut", "solid 15x15x25 steel"),
        ("Leg Pod Strut L", "bracket", "podstrut", "solid 20x20x30 steel"),
        ("Leg Pod Strut XL", "bracket", "podstrut", "solid 25x25x40 steel"),
        ("Leg Pod Canopy Port M", "plate", "canopyport", "plate 45x10x270 steel"),
        ("Leg Pod Canopy Port L", "plate", "canopyport", "plate 55x10x360 steel"),
        ("Leg Pod Canopy Port XL", "plate", "canopyport", "plate 70x10x450 steel"),
        ("Leg Pod Canopy Stbd M", "plate", "canopystbd", "plate 45x10x270 steel"),
        ("Leg Pod Canopy Stbd L", "plate", "canopystbd", "plate 55x10x360 steel"),
        ("Leg Pod Canopy Stbd XL", "plate", "canopystbd", "plate 70x10x450 steel"),
        ("Leg Pod Carriage M", "beam", "podcarriage", "solid 10x20x85 steel"),
        ("Leg Pod Carriage L", "beam", "podcarriage", "solid 15x25x110 steel"),
        ("Leg Pod Carriage XL", "beam", "podcarriage", "solid 15x30x140 steel"),
        ("Leg Pod Housing M", "housing", "podhousing", "hollow 75x110x340 steel"),
        ("Leg Pod Housing L", "housing", "podhousing", "hollow 100x145x450 steel"),
        ("Leg Pod Housing XL", "housing", "podhousing", "hollow 125x180x565 steel"),
        ("Drive Core S", "housing", "drivecore", "hollow 145x145x180 steel"),
        ("Drive Core M", "housing", "drivecore", "hollow 195x200x260 steel"),
        ("Drive Core L", "housing", "drivecore", "hollow 245x250x320 steel"),
        ("Fuel Pump L", "housing", "fuelpump", "hollow 285x245x130 steel"),
        ("Capacitor Bank M", "housing", "capacitor", "solid 140x170x110 steel"),
        ("Capacitor Bank L", "housing", "capacitor", "solid 200x230x150 steel"),
        ("Turret Ring M", "actuator", "turretring", "solid 160x160x140 steel"),
        ("Turret Ring L", "actuator", "turretring", "solid 240x240x200 steel"),
    ];

    /// <summary>A die's new kinds as patterns, to append to its list (a kit, numbers and a like body as the die's own patterns take).</summary>
    private static IEnumerable<object?> More(string die, PyDict? bay2 = null, List<object?>? scale = null, string? along = null) =>
        MorePatterns.Where(m => m.Die == die).Select(m => EngineTrainScale(m.Id) is { } train ? (object?)EngineTrainPattern(m, train)
            : Pattern(m.Id, m.Like, m.Label, m.Blank, blanks: m.Only, bay2: bay2, scale: scale,
                along: m.Like.StartsWith("Leg Pod ", StringComparison.Ordinal) ? "" : along, auto: false));

    /// <summary>
    /// An engine train piece's numbers by its blank (Sean, 2026-11-24: any length and diameter; what it serves goes with length times
    /// diameter squared, the blank's volume): the bell's thrust and burn, a chamber's or an injector's serves. Null: not a train piece.
    /// </summary>
    private static List<object?>? EngineTrainScale(string id) => id switch
    {
        "drivenozzle" => Py.L(Scale("thrustForce", "volume", 1, false), Scale("thrustBurn", "volume", 1, false)),
        "reactionchamber" or "fuelinjector" => Py.L(Scale("worksServes", "volume", 1, false)),
        "trainspacer" or "fuelbuffer" or "coolingjacket" or "igniter" or "sensorring" or "throttlevalve" or "mixture" => Py.L(), // (a function piece: what it adds is the same at any size)
        _ => null,
    };

    /// <summary>A train piece's pattern: its own round look stretched to the blank along its axis (z), none of the housing die's corner bosses.</summary>
    private static PyDict EngineTrainPattern((string Die, string Id, string Like, string Label, string Blank, string[]? Only) m, List<object?> scale)
    {
        var p = Pattern(m.Id, m.Like, m.Label, m.Blank, blanks: m.Only, scale: scale.Count > 0 ? scale : null, along: "z", auto: false);
        p["dieFeatures"] = false;
        return p;
    }

    /// <summary>The presets this adds (Presets calls it, after the ones before it).</summary>
    private static IEnumerable<(string Part, string Die, string Pattern, string Blank)> MoreToPress() =>
        MorePatterns.Select(m => (m.Like, m.Die, m.Id, m.Blank)).Concat(MorePresets);

    /// <summary>The parts whose Assembler rows go with this (Assembling.PressedNow).</summary>
    internal static IEnumerable<string> MovedToPress() =>
        MorePatterns.Select(m => m.Like).Concat(MoreDrivePatterns.Select(d => d.Like)).Concat(MorePresets.Select(p => p.Part));
}

internal static class PatternListExtensions
{
    /// <summary>A die's pattern list with more appended (its new kinds, at the end).</summary>
    public static List<object?> And(this List<object?> list, IEnumerable<object?> more)
    {
        list.AddRange(more);
        return list;
    }
}
