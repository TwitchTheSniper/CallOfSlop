using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The sections of modular bombs and missiles, and the Ordnance Bench: Constructs/Parts/Ordnance (docs/sandbox/CONSTRUCTS.md, "Modular
/// ordnance"). Was tools/constructs/ordnance_parts.py. A section is a real part (made in survival, stored, carried) whose "ordnance" block is
/// what it adds to a design. Its boxes run along Z, nose to -z, at the reference diameter of 25 cm; the bench scales them. Recipes come from
/// the part kit's assembling (--gen-parts assembling). The demo planes are built on Constructs/Blueprints/LightPlane.json: run this again if it changes.
/// A section's ordnance numbers are given as (key, value) pairs in order, each value keeping its Python type (an int stays an int).
/// </summary>
public sealed class OrdnanceParts : IPartGenerator
{
    public string Name => "ordnance_parts";

    private const string Out = "Constructs/Parts/Ordnance";
    private const double R0 = 0.125; // m: the reference radius
    private const string Group = "Weapons";
    private const string Olive = "#4a5530", Dark = "#2a2c26", Steel = "#7c838c", Brass = "#c8a838", Red = "#a83428", White = "#d8d8d0", Grey = "#565a5e";

    private static (string, object?)[] O(params (string, object?)[] kv) => kv;

    private static PyDict Cyl(int segments, double? taper = null)
    {
        var d = Kw(("shape", "cylinder"), ("segments", segments));
        if (taper is { } t)
            d["taper"] = Py.L(t, t);
        return d;
    }

    /// <summary>
    /// A round section, with a coloured band, fins, wings, canards, a nozzle, a chute pack, a glass eye, a ring tail or a tapered nose.
    /// lengths: (shortest, longest) as shares of its own length, when the bench may set its length (Sizes, below).
    /// </summary>
    private static (string, Part) Section(string name, string slot, double length, int mass, string color, string desc, (string, object?)[] ord, string? band = null, bool fins = false,
        bool nose = false, double wings = 0.0, bool canards = false, bool nozzle = false, bool pack = false, bool glass = false, bool ring = false, (double Lo, double Hi, double[]? Presets)? lengths = null)
    {
        var p = new Part(name, Small, "Ordnance sections", desc, tool: "wrench", color: color, group: Group);
        var z = length / 2;
        const double r = R0;
        p.Add(Box([-r, -r, -z], [r, r, z], hidden: true));
        p.Add(Box([-r, -r, -z], [r, r, z], color, detail: true, shape: nose ? Cyl(16, 0.2) : Cyl(16)));
        if (band is not null)
            p.Add(Box([-r * 1.01, -r * 1.01, -z * 0.5], [r * 1.01, r * 1.01, -z * 0.3], band, detail: true, shape: Cyl(16)));
        if (fins)
        {
            p.Add(Box([-0.012, -r * 1.9, -z * 0.8], [0.012, r * 1.9, z * 0.8], Dark, detail: true));
            p.Add(Box([-r * 1.9, -0.012, -z * 0.79], [r * 1.9, 0.012, z * 0.79], Dark, detail: true));
        }
        if (canards)
        {
            p.Add(Box([-0.01, -r * 1.5, -z * 0.5], [0.01, r * 1.5, z * 0.2], Dark, detail: true));
            p.Add(Box([-r * 1.5, -0.01, -z * 0.49], [r * 1.5, 0.01, z * 0.19], Dark, detail: true));
        }
        if (wings != 0)
            p.Add(Box([-r - wings, -0.015, -z * 0.7], [r + wings, 0.015, z * 0.7], color != Dark ? color : Grey, detail: true));
        if (nozzle)
            p.Add(Box([-r * 0.85, -r * 0.85, z * 0.6], [r * 0.85, r * 0.85, z * 1.02], Dark, detail: true, shape: Cyl(12, 0.6)));
        if (pack)
            p.Add(Box([-r * 0.8, -r * 0.8, z * 0.4], [r * 0.8, r * 0.8, z * 1.02], "#c0b080", detail: true, shape: Cyl(12, 0.6)));
        if (glass)
            p.Add(Box([-r * 0.6, -r * 0.6, -z * 1.02], [r * 0.6, r * 0.6, -z * 0.7], "#20303a", detail: true, glass: true, shape: Cyl(12, 0.4)));
        if (ring)
            p.Add(Box([-r * 1.6, -r * 1.6, -z * 0.2], [r * 1.6, r * 1.6, z * 0.8], Dark, detail: true, shape: Cyl(16)));
        p.Sock("-z", [0, 0, -z], "-z");
        p.Sock("+z", [0, 0, z], "+z");
        p.Stats(mass, 40, 1, 1.0);
        var kind = ord.Where(kv => kv.Item1 == "kind").Select(kv => (string?)kv.Item2).FirstOrDefault();
        var group = GroupOf(name, slot, kind);
        var block = Kw(("slot", slot), ("length", length), ("group", group), ("groupOrder", Array.IndexOf(Groups, group) + 1));
        if (lengths is { } l)
            block["lengths"] = Sizes(length, l.Lo, l.Hi, l.Presets);
        var numbers = Kw([.. ord.Where(kv => !Top.Contains(kv.Item1))]);
        block.Update([.. ord.Where(kv => Top.Contains(kv.Item1))]);
        if (numbers.Count > 0)
            block["numbers"] = numbers;
        p.Set(("ordnance", block));
        return ("Sections", p);
    }

    // The Ordnance Bench's picker, nose to tail in the order a weapon is built (a design's sections are stacked the same way). Every section goes
    // in every design: a design with a motor that lights as it leaves flies as a missile, one without falls as a bomb (OrdnanceDesign.Powered).
    private static readonly string[] Groups = ["Fuses", "Seekers", "Forward control", "Warheads", "Sub-payloads", "Body and fuel", "Wings", "Propulsion", "Tail control", "Stabilizers and brakes"];
    private static readonly string[] Forward = ["Canards", "Reaction Jets"];
    private static readonly string[] Subs = ["cluster", "incendiary", "mines", "mirv", "rods"];

    /// <summary>A section's group in the bench's picker, from its slot.</summary>
    private static string GroupOf(string name, string slot, string? kind)
    {
        if (slot == "control")
            return Forward.Contains(name) ? "Forward control" : "Tail control";
        if (slot == "payload")
            return kind is not null && Subs.Contains(kind) ? "Sub-payloads" : "Warheads";
        return slot switch
        {
            "fuse" => "Fuses", "seeker" => "Seekers", "spacer" => "Warheads", "body" => "Body and fuel", "fuel" => "Body and fuel", "wings" => "Wings",
            "motor" => "Propulsion", "stage" => "Propulsion", "stabilizer" => "Stabilizers and brakes", _ => throw new KeyNotFoundException(slot),
        };
    }

    private static readonly string[] Top = ["payload", "blast", "damage", "crater", "fuseDelay", "settings", "kind", "massFixed"]; // (the rest are the flight's numbers)
    private const double Step = 0.05; // m at 25 cm: a length's step on the bench

    private static double Snap(double x) => Py.Round(Py.RoundInt(x / Step) * Step, 3);

    /// <summary>
    /// A lengthable section's lengths (m at 25 cm): its shortest and longest as shares of its own length, and the bench picker's presets
    /// (its own length, then twice and three times it, unless given). Its mass, filling, built-in fuel, propellant and the per-length
    /// settings go with the length; a section's own length is still the one its numbers are written at.
    /// </summary>
    private static PyDict Sizes(double length, double lo0, double hi0, double[]? presets = null)
    {
        var s = Snap(length * lo0);
        var lo = s > Step ? s : Step;
        var hi = Snap(length * hi0);
        presets ??= [length, length * 2, length * 3];
        return Kw(("min", lo), ("max", hi), ("step", Step), ("presets", presets.Where(p => lo <= p && p <= hi).Select(p => (object?)Py.Round(p, 3)).ToList()));
    }

    private static readonly (double, double, double[]?) Warhead = (0.5, 6, null), Motor = (0.5, 4, null), BodyL = (0.2, 8, null); // a section's shortest and longest, as shares of its own length
    private static readonly PyDict Delay = Kw(("key", "delay"), ("label", "Delay"), ("unit", "s"), ("min", 0.05), ("max", 30), ("step", 0.05), ("default", 1));

    /// <summary>A setting; perLength: its least, most and default are for the section's own length and go with its length (a tank's fuel, a motor's burn).</summary>
    private static PyDict Setting(string key, string label, string unit, object lo, object hi, object step, object def, bool perLength = false)
    {
        var s = Kw(("key", key), ("label", label), ("unit", unit), ("min", lo), ("max", hi), ("step", step), ("default", def));
        if (perLength)
            s["perLength"] = true;
        return s;
    }

    private static readonly PyDict Class = Setting("class", "Thrust class", "", 1, 5, 1, 1);
    private static readonly PyDict Channel = Setting("channel", "Channel", "", 1, 99, 1, 1);
    private const string White2 = "#c8ccc8", Orange = "#b86a28", Blue = "#3a4c68";

    private static List<object?> L(params object?[] v) => Py.L(v);

    private static IEnumerable<(string, Part)> Stage1() =>
    [
        Section("Impact Fuse", "fuse", 0.2, 3, Grey, "Goes off the moment it hits.", O(("fuseDelay", 0.05)), nose: true),
        Section("Delay Fuse", "fuse", 0.25, 4, Grey, "Goes off a set time after it hits: through a deck first.", O(("settings", L(Delay))), band: Brass, nose: true),
        Section("Warhead HE", "payload", 0.5, 25, Olive, "25 kg of high explosive for its 0.5 m. Set its length for a bigger charge.", O(("payload", 20)), band: Brass, lengths: Warhead),
        Section("Warhead Blast", "payload", 0.6, 28, "#5a4a30", "Thin-cased: a wider blast for its weight, and less damage.", O(("payload", 22), ("blast", 1.5), ("damage", 0.6)), band: Red, lengths: Warhead),
        Section("Warhead Bunker", "payload", 0.6, 45, "#3a3d40", "A thick steel case: a deep crater.", O(("payload", 20), ("damage", 1.2), ("crater", 2.2)), band: White, lengths: Warhead),
        Section("Bomb Tail Fins", "stabilizer", 0.4, 6, Dark, "Keeps a bomb nose down.", O(("drag", 0.2)), fins: true),
    ];

    /// <summary>Flight: motors and their fuels, tanks, separators, steering and seekers (docs/sandbox/CONSTRUCTS.md, "Modular ordnance: flight").</summary>
    private static IEnumerable<(string, Part)> Stage3() =>
    [
        // Motors: thrust in N at 25 cm (with the cube of the scale), fuel use kg/s, built-in fuel kg, "air": breathes air.
        Section("Solid Motor", "motor", 0.8, 20, White2, "A solid rocket: strong and simple. Set its burn; it can't be stopped.",
            O(("kind", "solid"), ("settings", L(Setting("burn", "Burn time", "s", 0.5, 12, 0.5, 3, true), Class)), ("thrust", 9000), ("mass_burn", 4), ("mass_class", 6)), nozzle: true, lengths: Motor),
        Section("Boost Motor", "motor", 0.7, 24, Orange, "A short, hard kick off the rail: put it behind a Stage Separator.",
            O(("kind", "solid"), ("settings", L(Setting("burn", "Burn time", "s", 0.3, 4, 0.1, 1.2, true), Class)), ("thrust", 22000), ("mass_burn", 6), ("mass_class", 8)), nozzle: true, lengths: Motor),
        Section("Liquid Motor", "motor", 0.6, 18, Steel, "Burns what its stage's tanks hold, and gimbals while it burns.",
            O(("kind", "liquid"), ("settings", L(Class)), ("thrust", 6000), ("use", 1.0), ("gimbal", 30), ("mass_class", 4)), nozzle: true),
        Section("Hybrid Motor", "motor", 0.7, 22, Grey, "A solid grain and a liquid oxidiser: six seconds on its own, longer with a tank.",
            O(("kind", "hybrid"), ("settings", L(Class)), ("thrust", 7000), ("use", 1.2), ("fuel", 7), ("mass_class", 5)), nozzle: true, lengths: Motor),
        Section("Cold Gas Thruster", "motor", 0.4, 8, Steel, "A bottle of gas: a gentle push that works anywhere, air or none, and no flame.",
            O(("kind", "cold gas"), ("thrust", 1500), ("use", 0.5), ("fuel", 3)), nozzle: true, lengths: Motor),
        Section("Ramjet", "motor", 1.0, 25, Grey, "Breathes air, and only works at 150 m/s or more: boost it first. Nothing in thin air.",
            O(("kind", "air"), ("settings", L(Class)), ("thrust", 12000), ("use", 1.2), ("air", 1), ("minSpeed", 150), ("mass_class", 5)), band: Orange, nozzle: true),
        Section("Turbojet", "motor", 0.9, 30, Dark, "A small jet: weak, but it sips fuel. Breathes air: nothing in thin air. With wings, a cruise missile.",
            O(("kind", "air"), ("settings", L(Class)), ("thrust", 2500), ("use", 0.2), ("air", 1), ("mass_class", 6)), band: Steel, nozzle: true),
        // One tank of any length (the Long Missile Fuel Tank was this at 1 m: OrdnanceDesign.Folded). 3 kg of it are its ends, the same at any length.
        Section("Missile Fuel Tank", "fuel", 0.5, 6, Steel, "Fuel for the liquid, hybrid and air motors of its stage. Set its length for more.",
            O(("settings", L(Setting("fuel", "Fuel load", "kg", 2, 75, 1, 15, true))), ("mass_fuel", 1), ("massFixed", 3)), band: Blue, lengths: (0.2, 8, [0.5, 1.0, 2.0])),
        Section("Stage Separator", "stage", 0.12, 4, Red, "Splits a missile into stages: what is behind it burns first and drops when it burns out.", O(), band: Dark),
        Section("Body Tube", "body", 0.5, 5, White2, "An empty length of airframe: room, and weight.", O(), lengths: BodyL),
        // Steering: degrees a second of turn. Fins and canards need air and speed; a gimbal and vanes need a burning motor; reaction jets work anywhere.
        Section("Tail Fins", "control", 0.4, 6, Dark, "Steer in the air at speed. Nothing in thin air.", O(("turnAir", 60), ("drag", 0.03)), fins: true),
        Section("Canards", "control", 0.3, 5, Dark, "Small fins near the nose: a sharp turn in the air, and some drag.", O(("turnAir", 90), ("drag", 0.05)), canards: true),
        Section("Gimbal Mount", "control", 0.2, 7, Steel, "Swings the motor: turns while it burns, in air or out of it.", O(("turnBurn", 80)), band: Brass),
        Section("Jet Vanes", "control", 0.15, 3, Grey, "Vanes in the exhaust: a little turn while it burns.", O(("turnBurn", 45))),
        Section("Reaction Jets", "control", 0.25, 10, Grey, "Puffs of gas round the body: a slow turn at any speed, anywhere.", O(("turnAny", 35)), band: Red),
        // Seekers: how it finds what it steers for (the game's own pieces: flares, Cameras, Wireless Senders, Flight Stabilizers, beacons).
        Section("Heat Seeker", "seeker", 0.35, 6, White2, "Locks the moving thing nearest straight ahead as it goes. Flares draw it.", O(("kind", "heat"), ("reach", 1200), ("cone", 25)), nose: true, glass: true),
        Section("Radar Seeker", "seeker", 0.4, 9, Grey, "Locks the vehicle nearest straight ahead, moving or not. Flares don't fool it.", O(("kind", "radar"), ("reach", 2000), ("cone", 15)), nose: true),
        Section("Command Link", "seeker", 0.3, 5, Grey, "Goes for what the gunner looks at as it leaves: a vehicle, a base or a spot on the ground.", O(("kind", "command"), ("reach", 2500), ("cone", 10)), nose: true, band: Brass),
        Section("Laser Seeker", "seeker", 0.35, 7, Dark, "Rides the spot the gunner keeps looking at, or the spot a Camera on its channel looks at (a drone's).",
            O(("kind", "laser"), ("reach", 3000), ("cone", 30), ("settings", L(Channel))), nose: true, glass: true),
        Section("GPS Unit", "seeker", 0.3, 5, Olive, "Flies to its Flight Stabilizer's waypoint, read as it flies: wire the waypoint from logic.", O(("kind", "gps"), ("reach", 50000), ("cone", 180)), nose: true, band: Blue),
        Section("Radio Homer", "seeker", 0.3, 5, Olive, "Homes on the Wireless Sender on its channel.", O(("kind", "radio"), ("reach", 3000), ("cone", 180), ("settings", L(Channel))), nose: true, band: Red),
        Section("Beacon Homer", "seeker", 0.3, 5, Olive, "Homes on the beacon nearest straight ahead as it goes.", O(("kind", "beacon"), ("reach", 2000), ("cone", 30)), nose: true, band: White),
    ];

    private static PyDict Count(int lo, int hi, int d) => Setting("count", "Count", "", lo, hi, 1, d, true); // (a longer bay holds more)

    /// <summary>Bombs and payloads: fuses, sub-payloads, series charges, glide and cruise kits, rocket assist, stabilizers.</summary>
    private static IEnumerable<(string, Part)> Stage4() =>
    [
        // Fuses: any together; impact always works.
        Section("Proximity Fuse", "fuse", 0.2, 4, Grey, "Goes off when something flying comes within its range.", O(("kind", "prox"), ("settings", L(Setting("range", "Range", "m", 1, 30, 1, 6)))), band: Red, nose: true),
        Section("Altitude Fuse", "fuse", 0.2, 4, Grey, "Goes off coming down to its height over the ground: for bomblets and air bursts.", O(("kind", "altitude"), ("settings", L(Setting("height", "Height", "m", 2, 500, 1, 30)))), band: Blue, nose: true),
        Section("Timer Fuse", "fuse", 0.2, 3, Grey, "Goes off a set time after it leaves its rack.", O(("kind", "timer"), ("settings", L(Setting("time", "Time", "s", 0.5, 300, 0.5, 10)))), band: Brass, nose: true),
        Section("Airburst Fuse", "fuse", 0.2, 4, Grey, "Goes off a set distance short of what it steers for.", O(("kind", "airburst"), ("settings", L(Setting("distance", "Distance", "m", 2, 200, 1, 15)))), band: White, nose: true),
        Section("Remote Fuse", "fuse", 0.2, 4, Grey, "Goes off when a Wireless Sender on its channel turns on, at any range.", O(("kind", "remote"), ("settings", L(Channel))), band: Olive, nose: true),
        // Sub-payloads: the charge's filling shared among what it lets go, on the game's own shells and blasts.
        Section("Cluster Dispenser", "payload", 0.8, 30, Olive, "Opens and scatters bomblets that go off where they land. Give it an Altitude Fuse.",
            O(("kind", "cluster"), ("payload", 18), ("blast", 1.1), ("damage", 0.9), ("settings", L(Count(4, 400, 24)))), band: Brass, lengths: Warhead),
        Section("Fire Bomblets", "payload", 0.8, 28, "#6a3a20", "Scatters bomblets that set fire to what is near where they land.",
            O(("kind", "incendiary"), ("payload", 15), ("settings", L(Count(4, 200, 12)))), band: Red, lengths: Warhead),
        Section("Mine Dispenser", "payload", 0.9, 34, Olive, "Sows mines that lie until something moves near them, or their life runs out.",
            O(("kind", "mines"), ("payload", 16), ("prox", 4), ("settings", L(Count(2, 120, 8), Setting("life", "Mine life", "s", 10, 900, 10, 180)))), band: White, lengths: Warhead),
        Section("MIRV Bus", "payload", 0.9, 40, White2, "Splits into small guided warheads that fan out round what it steers for.",
            O(("kind", "mirv"), ("payload", 24), ("settings", L(Count(2, 24, 4)))), band: Red, lengths: Warhead),
        Section("Rod Bundle", "payload", 1.0, 60, Steel, "Steel rods thrown down fast: what they hit takes their speed. Drop them from high up.",
            O(("kind", "rods"), ("payload", 25), ("blast", 0.4), ("damage", 2.5), ("crater", 1.5), ("settings", L(Count(2, 60, 8)))), lengths: Warhead),
        Section("Kinetic Penetrator", "payload", 0.8, 70, "#3a3d40", "A dense rod with a little filling: the faster it hits, the harder.",
            O(("kind", "kinetic"), ("payload", 12), ("blast", 0.5), ("damage", 3), ("crater", 2.5)), band: Steel, nose: true, lengths: Warhead),
        Section("Tandem Spacer", "spacer", 0.15, 3, Dark, "Splits the payload ahead of it from the one behind: they go off one after the other, the later one deeper.", O(), band: Brass),
        // Glide, cruise and rocket assist (bombs).
        Section("Glide Wings", "wings", 0.8, 30, Olive, "Wings: it glides a long way from high up. Add a Glide Guidance Kit and a seeker to steer it.", O(("lift", 1.0), ("drag", 0.04)), wings: 0.9),
        Section("Pop-out Wings", "wings", 0.6, 20, Olive, "Folded wings that spring out a moment after it drops: a shorter glide.", O(("lift", 0.8), ("drag", 0.03), ("deploy", 0.8)), wings: 0.15),
        Section("Cruise Wings", "wings", 0.9, 26, Steel, "Long thin wings that hold it up at speed: with a Turbojet, a cruise missile.", O(("lift", 1.3), ("drag", 0.025)), wings: 1.3),
        Section("Glide Guidance Kit", "control", 0.35, 8, Dark, "Moving tail fins and a computer: it steers for what its seeker finds.", O(("turnAir", 40), ("drag", 0.02)), fins: true),
        Section("Rocket Assist", "motor", 0.6, 22, Orange, "A rocket that lights a second after the drop and drives the bomb on: further, and harder into what it hits.",
            O(("kind", "solid"), ("settings", L(Setting("burn", "Burn time", "s", 0.5, 8, 0.5, 2, true))), ("thrust", 7000), ("wait", 1), ("mass_burn", 4)), nozzle: true, lengths: Motor),
        // Stabilizers.
        Section("Retard Chute", "stabilizer", 0.3, 6, Dark, "A chute that opens off the rack: it falls slow and steep, and the plane is long gone.", O(("drag", 1.4), ("deploy", 0.3)), pack: true),
        Section("Ballute", "stabilizer", 0.3, 5, Dark, "A balloon brake: slower and steeper than fins alone.", O(("drag", 0.5), ("deploy", 0.2)), pack: true),
        Section("Ring Tail", "stabilizer", 0.35, 7, Dark, "A ring round the tail: steady, with a little steering in the air.", O(("drag", 0.05), ("turnAir", 8)), ring: true),
    ];

    /// <summary>
    /// Massive warheads (docs/sandbox/CONSTRUCTS.md, "Massive blasts"): arcade game items with made-up numbers, nothing modelled on anything
    /// real. A tiny filling with a huge blast weight puts them on the same curve as every charge, hundreds of metres across; they are heavy
    /// for their size, so only big rack stations and big airframes lift them. "kind" picks the blast's look (MegaBlasts.LookOf).
    /// </summary>
    private static IEnumerable<(string, Part)> Massive() =>
    [
        Section("Fission Warhead", "payload", 1.2, 1400, "#c8a020", "The big one. A 166 m blast at its own length: everything near it is gone, everything far off hears it. Needs a heavy rack and a long runway.",
            O(("kind", "fission"), ("payload", 50), ("blast", 4000), ("damage", 22000), ("crater", 1.0)), band: Dark, lengths: Warhead),
        Section("Fusion Warhead", "payload", 1.6, 3200, White2, "Bigger than the big one. A 380 m blast at its own length that flattens a valley and leaves a lake-sized hole. Strictly for capital bombers.",
            O(("kind", "fusion"), ("payload", 60), ("blast", 40000), ("damage", 30000), ("crater", 0.8)), band: Blue, ring: true, lengths: Warhead),
        Section("Antimatter Warhead", "payload", 0.9, 2200, "#2c2440", "A few grams of the wrong kind of matter in a very nervous bottle. A 612 m white-violet blast at its own length. Do not drop.",
            O(("kind", "antimatter"), ("payload", 4), ("blast", 2500000), ("damage", 1200000), ("crater", 0.45)), band: "#9a5cd8", glass: true, lengths: Warhead),
    ];

    /// <summary>A launch rail: a beam with a slide under it whose lug takes a designed weapon. What it lets go leaves forward along it; a missile lights on it.</summary>
    private static (string, Part) Rail(string name, int load, double length, int mass, string desc)
    {
        var p = new Part(name, Small, "Bombs", desc + $" Rated for {Py.G(load)} kg.", role: "logic", tool: "welder", color: Dark, group: Group);
        var z = length / 2;
        p.Add(Box([-0.06, -0.02, -z], [0.06, 0.08, z]));
        p.Add(Box([-0.035, -0.06, -z * 0.85], [0.035, -0.02, z * 0.85], Brass));
        p.Add(Box([-0.07, 0.0, z * 0.7], [0.07, 0.1, z], Grey, detail: true));
        p.Sock("lug0", [0, -0.06, 0], "-y");
        p.Sock("top", [0, 0.08, 0], "+y");
        p.Sock("back", [0, 0.03, z], "+z");
        p.Sock("front", [0, 0.03, -z], "-z");
        p.Stats(mass, 120, 3, 2.0);
        p.Set(("logic", Kw(("kind", "bombrack"), ("load", load), ("rail", true))), ("legacy", true)); // (an old mount: the Ordnance Rack launches missiles; it loads and works but isn't offered)
        return ("Rails", p);
    }

    private static (string, Part) Bench()
    {
        var p = new Part("Ordnance Bench", Small, "Workbenches", "F opens it: design a bomb or a missile from sections, and build it here.", role: "storage", tool: "welder", color: "#4a4f45", group: "Weapons");
        p.Add(Box([-1, 0, -0.5], [1, 0.9, 0.5]));
        p.Add(Box([-0.95, 0.9, -0.45], [0.95, 0.96, 0.45], Olive, detail: true));
        p.Add(Box([-1, 0.9, 0.44], [1, 1.6, 0.5], "#3a3f38"));
        for (var k = 0; k < 3; k++)
        {
            var x = -0.6 + 0.6 * k;
            p.Add(Box([x - 0.1, 0.96, -0.3], [x + 0.1, 1.02, 0.3], Brass, detail: true));
        }
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Stats(180, 900, 3, 4.0);
        var slots = Enumerable.Range(0, 3).Select(k => (object?)Kw(("min", L(-0.9 + 0.6 * k, 1.0, -0.4)), ("max", L(-0.4 + 0.6 * k, 1.5, 0.4)), ("stack", 12), ("label", $"Bay {k + 1}"))).ToList();
        p.Set(("storage", Kw(("ordnanceBench", true), ("slots", slots))));
        return ("", p);
    }

    /// <summary>The Light Plane with a rack or rail under each outer wing, each holding a designed weapon by its key; the pilot's fire key lets go.</summary>
    private static PyDict Plane(PartOutput o, string name, string desc, string rack, string weapon)
    {
        var b = (PyDict)o.ReadJson("Constructs/Blueprints/LightPlane.json")!;
        var s = (List<object?>)b["steps"]!;
        var links = (List<object?>)b["links"]!;
        var stab = 17;
        foreach (var wing in new[] { 11, 12 })
        {
            s.Add(Kw(("part", rack), ("on", wing), ("socket", "-y"), ("by", "top")));
            var at = s.Count - 1;
            s.Add(Kw(("part", weapon), ("on", at), ("socket", "lug0"), ("by", "lug")));
            links.Add(Kw(("type", "data"), ("from", stab), ("to", at), ("fromPort", "fire")));
        }
        return Kw(("format", b["format"]), ("name", name), ("description", desc), ("steps", s), ("links", links));
    }

    public void Make(PartOutput o)
    {
        WriteParts(o, Out, [Bench(), .. Stage1(), .. Stage3(), .. Stage4(), .. Massive(),
            Rail("Launch Rail", 600, 1.4, 12, "Fires the designed missile on it forward along the rail, under its own thrust: one too heavy for its motor stays put. A bomb on it just drops."),
            Rail("Heavy Launch Rail", 8000, 3.0, 60, "A long rail for a big missile. Fires it forward under its own thrust; one too heavy for its motor stays put.")]);
        o.File("Constructs/Blueprints/StrikePlane.json", PyJson.Dumps(Plane(o, "Strike Plane", "The Light Plane with a heat-seeking missile on a Launch Rack under each wing. Point at what flies and fire: each locks what is nearest straight ahead.",
            "Launch Rack", "Ordnance:M25:Heat Seeker+Proximity Fuse+Warhead HE+Tail Fins+Solid Motor"), 2) + "\n");
        o.File("Constructs/Blueprints/GlideBomber.json", PyJson.Dumps(Plane(o, "Glide Bomber", "The Light Plane with a GPS glide bomb under each wing: set the Flight Stabilizer's waypoint, let go from high up, and they glide there.",
            "Launch Rack", "Ordnance:B25:GPS Unit+Impact Fuse+Warhead HE+Pop-out Wings+Glide Guidance Kit"), 2) + "\n");
    }
}
