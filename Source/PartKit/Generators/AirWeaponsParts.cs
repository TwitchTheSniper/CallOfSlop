using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The air weapons: Constructs/Parts/AirWeapons, and the demo craft (docs/sandbox/CONSTRUCTS.md, "Air weapons"). Ported from
/// tools/constructs/air_weapons_parts.py. <see cref="Assembling"/> prices the racks from <see cref="OrdnanceRacks"/>: run it after this.
/// The blueprints are built on Constructs/Blueprints/LightPlane.json, GunnerTurret.json and SmallHelicopter.json: run this again if those change.
/// </summary>
public sealed class AirWeaponsParts : IPartGenerator
{
    public string Name => "air_weapons_parts";

    private const string Out = "Constructs/Parts/AirWeapons";
    private const string Olive = "#4a5530", Dark = "#2a2c26", Brass = "#c8a838", Glass = "#20303a";
    private const string Group = "Weapons";

    private static PyDict Cyl(int segments, double? taper = null)
    {
        var d = Kw(("shape", "cylinder"), ("segments", segments));
        if (taper is { } t)
            d["taper"] = Py.L(t, t);
        return d;
    }

    private static Part Bomb(string name, double radiusM, double length, int mass, int blast, int damage, double fuse, double crater, string desc)
    {
        var p = new Part(name, Small, "Bombs", desc, tool: "welder", color: Olive, group: Group);
        double r = radiusM, z = length / 2;
        p.Add(Box([-r, -r, -z], [r, r, z], hidden: true));
        p.Add(Box([-0.04, r - 0.02, -0.06], [0.04, r + 0.05, 0.06], hidden: true));
        p.Add(Box([-r * 0.97, -r * 0.97, -z * 0.55], [r * 0.97, r * 0.97, z * 0.55], Olive, detail: true, shape: Cyl(16)));
        p.Add(Box([-r * 0.94, -r * 0.94, -z * 0.97], [r * 0.94, r * 0.94, -z * 0.54], Olive, detail: true, shape: Cyl(16, 0.3)));
        p.Add(Box([-r, -r, -z * 0.4], [r, r, -z * 0.32], Brass, detail: true, shape: Cyl(16)));
        p.Add(Box([-r * 0.5, -r * 0.5, z * 0.54], [r * 0.5, r * 0.5, z * 0.92], Dark, detail: true, shape: Cyl(12)));
        p.Add(Box([-0.012, -r * 1.3, z * 0.62], [0.012, r * 1.3, z], Dark, detail: true));
        p.Add(Box([-r * 1.3, -0.012, z * 0.63], [r * 1.3, 0.012, z * 0.99], Dark, detail: true));
        p.Add(Box([-0.04, r * 0.96, -0.06], [0.04, r + 0.05, 0.06], Dark, detail: true));
        p.Sock("lug", [0, r + 0.05, 0], "+y");
        p.Stats(mass, 40, 1, 1.0);
        p.Set(("explosive", Kw(("radius", blast), ("damage", damage), ("fuse", fuse), ("knockback", 12), ("crater", crater))));
        return p;
    }

    /// <summary>Lugs below a hanger: each -y socket takes a bomb's lug. An old fixed mount: the Ordnance Rack does its job, so it loads and works but isn't offered.</summary>
    private static Part Rack(string name, int stations, string desc, int load = 300)
    {
        var x = 0.125 * stations;
        var p = new Part(name, Small, "Bomb racks", desc + $" Rated for {Py.G((double)load * stations)} kg.", role: "logic", tool: "welder", color: Dark, group: Group);
        p.Add(Box([-x, -0.04, -0.2], [x, 0.08, 0.2]));
        for (var k = 0; k < stations; k++)
        {
            var at = -x + 0.25 * k + 0.125;
            p.Add(Box([at - 0.05, -0.08, -0.07], [at + 0.05, -0.04, 0.07], Brass, detail: true));
            p.Sock($"lug{k}", [at, -0.04, 0], "-y");
        }
        p.Sock("top", [0, 0.08, 0], "+y");
        p.Sock("back", [0, 0, 0.2], "+z");
        p.Sock("front", [0, 0, -0.2], "-z");
        p.Stats(6 * stations, 80, 3, 1.5);
        p.Set(("logic", Kw(("kind", "bombrack"), ("load", load * stations))), ("legacy", true));
        return p;
    }

    /// <summary>
    /// An old preset of the Modular Bomb Rack: only its shape. It still loads (saves and blueprints name it) as the Ordnance Rack with the
    /// same stations in the same places, but isn't offered (Source/Constructs/ModularRack.cs, FromLegacy).
    /// </summary>
    private static PyDict ModularPreset(string name, int stations, string layout, string size, string desc) =>
        Kw(("format", "call-of-slop-construct-part/1"), ("name", name), ("description", desc), ("legacy", true),
            ("modularRack", Kw(("stations", stations), ("layout", layout), ("size", size))));

    /// <summary>
    /// A preset of the Ordnance Rack: only its shape. The game makes the whole part from it (Source/Constructs/ModularRack.cs), the same way
    /// it makes a shape the configure tool asks for, so a preset and the rack it is set to are one part. a, b: rows and columns (a drum's
    /// layers and segments); length and diameter: the largest ordnance a station takes, m.
    /// </summary>
    private static PyDict OrdnanceRack(string name, string layout, int a, int b, double length, double diameter, string desc)
    {
        var (ka, kb) = layout == "drum" ? ("layers", "segments") : ("rows", "columns");
        return Kw(("format", "call-of-slop-construct-part/1"), ("name", name), ("description", desc),
            ("modularRack", Kw(("layout", layout), (ka, a), (kb, b), ("length", length), ("diameter", diameter))));
    }

    // The Ordnance Rack's presets (docs/sandbox/CONSTRUCTS.md, "Ordnance racks"): every shape differs, and the configure tool turns any into any.
    // (name, layout, rows or layers, columns or segments, longest ordnance m, widest m, description)
    public static readonly (string Name, string Layout, int A, int B, double Length, double Diameter, string Desc)[] OrdnanceRacks =
    [
        ("Ordnance Rack", "grid", 1, 1, 2.5, 0.25, "One station for a bomb or a missile, set by the configure tool: a grid or a drum, its stations, and the largest ordnance it takes."),
        ("Twin Ordnance Rack", "grid", 1, 2, 1.0, 0.25, "Two small bombs or missiles side by side."),
        ("Triple Ordnance Rack", "grid", 1, 3, 2.5, 0.25, "Three side by side."),
        ("Ordnance Rack 2x2", "grid", 2, 2, 2.5, 0.25, "Four: two rows of two."),
        ("Ordnance Rack 2x4", "grid", 2, 4, 2.5, 0.25, "Eight: two rows of four."),
        ("Ordnance Rack 4x4", "grid", 4, 4, 1.0, 0.25, "Sixteen small bombs: a bay's worth."),
        ("Heavy Ordnance Rack", "grid", 1, 1, 2.5, 0.625, "One big station, 5 t: what a small rack won't take."),
        ("Giant Ordnance Rack", "grid", 1, 1, 8.0, 2.0, "For the biggest things a designer dares: 2000 t."),
        ("Launch Rack", "grid", 1, 1, 3.0, 0.25, "A long single station, for a missile under a wing."),
        ("Twin Launch Rack", "grid", 1, 2, 3.0, 0.25, "Two long stations side by side: a vehicle's missile launcher."),
        ("Launch Rack 2x2", "grid", 2, 2, 3.0, 0.25, "Four long stations, two rows of two: a missile battery."),
        ("Launch Rack 1x6", "grid", 1, 6, 3.0, 0.25, "Six long stations side by side: a heavy missile battery."),
        ("Missile Pod 2x2", "grid", 2, 2, 1.5, 0.2, "Four short missiles, two by two."),
        ("Light Ordnance Drum", "drum", 1, 6, 2.5, 0.25, "A drum for a bomb bay, six round it, mounted by its yoke or its ends."),
        ("Heavy Ordnance Drum", "drum", 1, 8, 2.5, 0.625, "A heavy drum for a bomb bay, eight round it, mounted by its yoke or its ends."),
        ("Double Ordnance Drum", "drum", 2, 6, 2.5, 0.25, "Two layers of six round one drum."),
    ];

    // The Modular Bomb Rack's old presets: saves and blueprints still name them.
    private static readonly (string Name, int Stations, string Layout, string Size, string Desc)[] Modular =
    [
        ("Modular Bomb Rack", 1, "row", "light", "A bomb rack set by the configure tool: 1 to 16 stations, in a row, in pairs or on a drum, light, heavy or giant."),
        ("Triple Bomb Rack", 3, "row", "light", "Three bombs side by side."),
        ("Quad Bomb Rack", 4, "row", "light", "Four bombs side by side."),
        ("5-Station Bomb Rack", 5, "row", "light", "Five bombs side by side."),
        ("6-Station Bomb Rack", 6, "pairs", "light", "Six bombs, three pairs."),
        ("7-Station Bomb Rack", 7, "row", "light", "Seven bombs side by side."),
        ("8-Station Bomb Rack", 8, "pairs", "light", "Eight bombs, four pairs."),
        ("Sixteen Bomb Rack", 16, "pairs", "light", "Sixteen bombs, eight pairs: a bay's worth."),
        ("Light Rotary Drum", 6, "drum", "light", "A small drum for a bomb bay, mounted by its spine or its ends."),
        ("Rotary Bomb Drum", 8, "drum", "heavy", "A heavy drum for a bomb bay, mounted by its spine or its ends."),
    ];

    /// <summary>A heavy gun on the shell pool: a breech block and a tube or a box of tubes.</summary>
    private static Part Gun(string name, string desc, double length, double half, int mass, PyDict block, string color = Dark, bool legacy = false)
    {
        var p = new Part(name, Small, "Guns", desc, role: "gun", tool: "welder", color: color, group: Group);
        p.Add(Box([-half, -half, -half], [half, half, half]));
        p.Add(Box([-half * 0.45, -half * 0.45, -length], [half * 0.45, half * 0.45, -half], "#3a3d40"));
        p.Sock("bottom", [0, -half, 0], "-y");
        p.Sock("top", [0, half, 0], "+y");
        p.Sock("left", [-half, 0, 0], "-x");
        p.Sock("right", [half, 0, 0], "+x");
        p.Sock("back", [0, 0, half], "+z");
        p.Stats(mass, 240, 6, 3.0);
        var g = new PyDict(block) { ["muzzle"] = Py.L(0, 0, -length) };
        p.Set(("gun", g), ("centreSockets", false));
        if (legacy)
            p.Set(("legacy", true)); // (an old mount: the Ordnance Rack launches designed missiles; it loads and works but isn't offered)
        return p;
    }

    private static Part Sight(string name, string kind, int reach, int cone, string desc, List<object?>? config = null)
    {
        var p = new Part(name, Small, "Sights", desc, tool: "wrench", color: Dark, group: Group);
        p.Add(Box([-0.08, 0, -0.14], [0.08, 0.12, 0.14]));
        p.Add(Box([-0.05, 0.03, -0.155], [0.05, 0.09, -0.135], Glass, detail: true));
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Stats(5, 50, 1, 1.0);
        p.Set(("sight", Kw(("kind", kind), ("reach", reach), ("cone", cone))));
        if (config is not null)
            p.Set(("config", config));
        return p;
    }

    private static List<(string, Part)> Parts()
    {
        var fuse = Kw(("key", "fuseRange"), ("label", "Fuse range"), ("group", "Gun"), ("unit", "m"), ("min", 0), ("max", 1500), ("step", 25), ("default", 0),
            ("tooltip", "With nothing locked, shells burst this far out. 0: only near what flies, or on what they hit."));
        const string so = "Leviathan .50";
        return
        [
            ("Bombs", Bomb("Light Bomb", 0.11, 1.0, 40, 7, 120, 0.5, 2.5, "40 kg: 7 m blast half a second after a hit. Hang it on a Bomb Rack.")),
            ("Bombs", Rack("Bomb Rack", 1, "Holds a bomb on its lug. While its input is on it lets go, three times a second, if its own construct leaves the way down clear: a closed bay door stops it.")),
            ("Bombs", Rack("Twin Bomb Rack", 2, "Two bombs side by side. While its input is on it lets them go in turn, if its own construct leaves the way down clear.")),
            ("Bombs", Rack("Heavy Bomb Rack", 1, "A strong single hook for what a small rack won't take. While its input is on it lets go, if its own construct leaves the way down clear.", load: 5000)),
            ("Bombs", Rack("Giant Bomb Rack", 1, "For the biggest things a designer dares. While its input is on it lets go, if its own construct leaves the way down clear.", load: 2000000)),
            ("Guns", Gun("AA Gun", "Flak: its shells burst near what flies. An AA Sight on it leads what the gunner looks at.", 1.3, 0.22, 90,
                Kw(("damage", 80), ("rpm", 90), ("spread", 0.4), ("range", 900), ("maxRange", 1000), ("mag", 8), ("reload", 3), ("kick", 200), ("fx", "cannon"), ("soundOf", so), ("shell", "flak"), ("speed", 340), ("prox", 4), ("blast", 12), ("blastDamage", 170)))),
            ("Guns", Gun("Rocket Pod Mk2", "Seven rockets that push themselves: 1.5 s of burn, straight at what the gun points at.", 0.5, 0.2, 45,
                Kw(("damage", 90), ("rpm", 180), ("spread", 1.2), ("range", 500), ("maxRange", 1000), ("mag", 7), ("reload", 8), ("kick", 30), ("fx", "rocket"), ("soundOf", so), ("shell", "rocket"), ("speed", 40), ("thrust", 220), ("burn", 1.5), ("blast", 5), ("blastDamage", 160), ("crater", 1.2)))),
            ("Guns", Gun("Missile Rail", "A guided missile: it locks what the gunner looks at, or what a Seeker Head sees, and turns after it. Flares draw it.", 0.7, 0.14, 60,
                Kw(("damage", 200), ("rpm", 30), ("spread", 0), ("range", 900), ("maxRange", 1000), ("mag", 2), ("reload", 10), ("kick", 20), ("fx", "rocket"), ("soundOf", so), ("shell", "missile"), ("speed", 30), ("thrust", 160), ("burn", 3), ("turn", 70), ("prox", 5), ("blast", 9), ("blastDamage", 320)), legacy: true)),
            ("Guns", Gun("Flare Dispenser", "Throws flares that burn five seconds, sinking. Missiles coming in may go for one instead.", 0.3, 0.12, 15,
                Kw(("damage", 1), ("rpm", 180), ("spread", 8), ("range", 30), ("maxRange", 60), ("mag", 12), ("reload", 6), ("kick", 2), ("fx", "cannon"), ("soundOf", so), ("shell", "flare"), ("speed", 30), ("burn", 5)))),
            ("Sights", Sight("AA Sight", "lead", 1200, 8, "On a gun: the gun leads the body its gunner looks at, and its shells burst on the way there.", Py.L(fuse))),
            ("Sights", Sight("Seeker Head", "seeker", 1000, 20, "On a Missile Rail: the missile locks the flying body nearest straight ahead, with or without a gunner.")),
        ];
    }

    private static PyDict Data(int a, int b, string? port = null)
    {
        var d = Kw(("type", "data"), ("from", a), ("to", b));
        if (port is not null)
            d["fromPort"] = port;
        return d;
    }

    private static int AddStep(List<object?> s, string part, int on, string socket, string by, params (string, object?)[] more)
    {
        s.Add(Kw([("part", part), ("on", on), ("socket", socket), ("by", by), .. more]));
        return s.Count - 1;
    }

    /// <summary>The Light Plane with a twin rack under its tail boom's root; the pilot's fire key lets go.</summary>
    private static PyDict LightBomber(PartOutput o)
    {
        var b = (PyDict)o.ReadJson("Constructs/Blueprints/LightPlane.json")!;
        var s = (List<object?>)b["steps"]!;
        var links = (List<object?>)b["links"]!;
        int boom = 5, stab = 17;
        var rackAt = AddStep(s, "Twin Ordnance Rack", boom, "-y2", "top");
        AddStep(s, "Light Bomb", rackAt, "lug0", "lug");
        AddStep(s, "Light Bomb", rackAt, "lug1", "lug");
        links.Add(Data(stab, rackAt, "fire"));
        return Kw(("format", b["format"]), ("name", "Light Bomber"), ("description", "The Light Plane with two Light Bombs under its boom. The fire key lets them go; a cross shows where the next lands."), ("steps", s), ("links", links));
    }

    private static PyDict FlakGun(PartOutput o)
    {
        var b = (PyDict)o.ReadJson("Constructs/Blueprints/GunnerTurret.json")!;
        var s = (List<object?>)b["steps"]!;
        s[7] = Kw(("part", "AA Gun"), ("on", 6), ("socket", "leaf"), ("by", "bottom"));
        s.Add(Kw(("part", "AA Sight"), ("on", 7), ("socket", "top"), ("by", "bottom")));
        return Kw(("format", b["format"]), ("name", "Flak Gun"), ("description", "A seated AA Gun with an AA Sight. Look at what flies: the gun leads it, and its shells burst beside it."), ("steps", s), ("wires", b["wires"]));
    }

    /// <summary>The Small Helicopter with a gunner on its right skid block and a stabilised gun under them. The gunner's Aim key throws flares.</summary>
    private static PyDict Gunship(PartOutput o)
    {
        var b = (PyDict)o.ReadJson("Constructs/Blueprints/SmallHelicopter.json")!;
        var s = (List<object?>)b["steps"]!;
        var links = (List<object?>)b["links"]!;
        var block = 9; // the Frame Block on the seat's +x side
        var seat = AddStep(s, "Seat", block, "+y", "bottom");
        var turret = AddStep(s, "Turret Module", block, "+z", "-x", ("config", Kw(("stabilised", 1))));
        var table = AddStep(s, "Turntable", block, "-y", "base");
        var arm = AddStep(s, "Frame Block", table, "top", "+y");
        var hinge = AddStep(s, "Motor Hinge", arm, "-y", "base");
        var mg = AddStep(s, "Mounted MG", hinge, "leaf", "top");
        AddStep(s, "Ammo Box", mg, "back", "front");
        var flares = AddStep(s, "Flare Dispenser", 10, "-y", "top");
        AddStep(s, "Ammo Box", flares, "back", "front");
        links.AddRange([Data(seat, turret), Data(turret, mg), Data(turret, flares, "aim")]);
        return Kw(("format", b["format"]), ("name", "Gunship"), ("description", "The Small Helicopter with a door gunner on a stabilised mount: the gun and the look stay on their point as she moves. The gunner's Aim key throws flares."), ("steps", s), ("links", links));
    }

    public void Make(PartOutput o)
    {
        WriteParts(o, Out, Parts());
        foreach (var m in Modular)
            o.File($"{Out}/Bombs/{FileName(m.Name)}", PyJson.Dumps(ModularPreset(m.Name, m.Stations, m.Layout, m.Size, m.Desc), 2) + "\n");
        foreach (var r in OrdnanceRacks)
            o.File($"{Out}/Racks/{FileName(r.Name)}", PyJson.Dumps(OrdnanceRack(r.Name, r.Layout, r.A, r.B, r.Length, r.Diameter, r.Desc), 2) + "\n");
        o.File("Constructs/Blueprints/LightBomber.json", PyJson.Dumps(LightBomber(o), 2) + "\n");
        o.File("Constructs/Blueprints/FlakGun.json", PyJson.Dumps(FlakGun(o), 2) + "\n");
        o.File("Constructs/Blueprints/Gunship.json", PyJson.Dumps(Gunship(o), 2) + "\n");
    }
}
