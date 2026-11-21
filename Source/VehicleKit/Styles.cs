namespace CallOfSlop.VehicleKit;

/// <summary>The factions' styles (docs/vehiclekit/VEHICLEKIT.md, "A faction style").</summary>
public static class Styles
{
    /// <summary>Every style a generator can be run in.</summary>
    public static IReadOnlyList<FactionStyle> All => [Raider, It, Salvager, Mercenary, Militia];

    /// <summary>A style's dice name by its name (<see cref="FactionStyle.DiceName"/>); a name no style has is its own.</summary>
    public static string DiceNameOf(string name) => All.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.DiceName ?? name;

    /// <summary>The styles whose vehicles ship as blueprints.</summary>
    public static IReadOnlyList<FactionStyle> Shipped => [Raider, It, Salvager, Mercenary];

    /// <summary>Raider: rust and bare metal, nothing matches, spikes, war paint and trophies (the Raiders fly it: Crew/Factions/Raiders.json).</summary>
    public static readonly FactionStyle Raider = new()
    {
        Name = "Raider",
        DiceName = "Bandit", // (its name before 2026-11-21: the committed vehicles keep their shapes)
        Extra = ["Car", "Buggy", "Monster Truck", "Wagon", "Giant Truck", "Drone", "Plane", "Helicopter"], // (phase 8a: its archetypes' cars; 8b: buggies, monster trucks, wagons, concepts, giants)
        Group = "Raider",
        Palette = new()
        {
            ["rust"] = ["#7a4326", "#8c5230", "#6b3a22", "#94603a", "#5e3320"],
            ["body"] = ["#8a8d8f", "#6f7477", "#a39a84", "#7a4326", "#5f6a55", "#8c5230"], // (bare metal, primer, old paint)
            ["frame"] = ["#3a3330", "#46392f", "#2f2c2a"],
            ["war"] = ["#a81c14", "#d8cdb0", "#c8a018", "#17181a"],
        },
        Parts = new()
        {
            ["wheel"] = ["Off-Road Wheel"],
            ["spring"] = ["Suspension"],
            ["differential"] = ["Differential"],
            ["engine"] = ["Engine", "Engine", "V8 Engine"],
            ["gearbox"] = ["Automatic Gearbox"],
            ["heavyWheel"] = ["Off-Road Wheel"], // (a Large Wheel's top reaches a wall hung under a floor on two-layer sills),
            ["heavySpring"] = ["Leaf Spring"],
            ["heavyDifferential"] = ["Wide Differential"],
            ["heavyEngine"] = ["V8 Engine"],
            ["heavyGearbox"] = ["Automatic Gearbox"],
            ["wall"] = ["Sheet Steel", "Sheet Steel", "Plate Steel", "Tread Plate"],
            ["armour"] = ["Plate Steel", "Armour Plate"],
            ["shelf"] = ["Shelf Bay 1", "Shelf Bay 1", "Tall Shelf Bay 1"],
            ["floor"] = ["Bolt Deck"],
            ["hood"] = ["Long Hood", "Snub Hood", "Slab Nose", "Bull Hood", "plates"],
            ["nose"] = ["Bar Grille", "Slat Grille", "Ram Plow", "Bull Bar", "none"],
            ["fender"] = ["Wide Arch Fender", "none"], // (phase 9a: wide welded arches or bare tyres)
            ["heavyCab"] = ["Roll Cage", "Tin Roof Cab", "Chop Top Cab", "Mesh Guard Cage", "Slit Cab", "Cab Over Shell"],
            ["cab"] = ["Roll Bar", "Roll Cage", "Tin Roof Cab", "Chop Top Cab", "Mesh Guard Cage", "Slit Cab", "Windscreen Frame"],
            ["bunk"] = ["Scrap Bunk"],
            ["bench"] = ["Crew Bench"],
            ["workbench"] = ["Scrap Workbench"],
            ["crates"] = ["Crate Stack"],
            ["layout"] = ["bunkroom", "workshop", "hold", "mess"],
            ["layout bunkroom"] = ["bunk", "bunk", "shelf"],
            ["layout workshop"] = ["workbench", "shelf", "shelf", "crates"],
            ["layout hold"] = ["crates", "crates", "crates", "shelf"],
            ["layout mess"] = ["bench", "bench", "workbench", "crates"],
            ["chest"] = ["Chest"],
            ["ladder"] = ["Scrap Ladder"],
            ["tank"] = ["Large Fuel Tank"],
            ["deck"] = ["Plate Steel", "Tread Plate", "Sheet Steel"],
            ["bed"] = ["Plate Steel", "Sheet Steel", "Tread Plate", "Wood Planks"],
            ["sheet"] = ["Sheet Steel", "Sheet Steel", "Plate Steel", "Tread Plate"],
            ["grille"] = ["Mesh Grate"],
            ["patch"] = ["Sheet Steel 0.5 m", "Plate Steel 0.5 m", "Tread Plate 0.5 m"],
            ["spike"] = ["Iron Spike", "Iron Spike", "Long Iron Spike"],
            ["trophy"] = ["Skull Trophy", "Horned Skull", "Hubcap Plate"],
            ["loot"] = ["Ammo Crate", "Ammo Crate", "Ammo Box", "Small Fuel Tank"],
        },
        LightGear =
        [
            new("coil", "Suspension", "Off-Road Wheel", "Suspension", "Off-Road Wheel"),
            new("long travel", "Long-Travel Suspension", "Large Wheel", "Long-Travel Suspension", "Large Wheel"),
            new("hot rod", "Stiff Suspension", "Slick Wheel", "Adjustable Suspension", "Large Wheel"),
            new("spiked", "Hydraulic Suspension", "Spiked Wheel", "Hydraulic Suspension", "Spiked Wheel"),
            new("junkyard", "Suspension", "Solid Wheel", "Adjustable Suspension", "Off-Road Wheel"),
        ],
        HeavyGear =
        [
            new("leaf", "Leaf Spring", "Off-Road Wheel", "Leaf Spring", "Off-Road Wheel"),
            new("hydraulic", "Hydraulic Suspension", "Large Wheel", "Hydraulic Suspension", "Off-Road Wheel"),
            new("swing", "Leaf Spring", "Off-Road Wheel", "Swing Arm", "Large Wheel", "Differential", Floor: 0.375f),
            new("wishbone", "Double Wishbone", "Large Wheel", "Swing Arm", "Large Wheel", "Differential", Floor: 0.375f),
            new("tandem", "Leaf Spring", "Large Wheel", "Leaf Spring", "Off-Road Wheel", Tag: "Leaf Spring", TagWheel: "Off-Road Wheel"),
            new("six", "Leaf Spring", "Off-Road Wheel", "Leaf Spring", "Off-Road Wheel", Tandem: 1.25f),
            new("stilt", "Heavy Pogo Strut", "Off-Road Wheel", "Heavy Pogo Strut", "Large Wheel"),
            new("dually", "Heavy Suspension", "Off-Road Wheel", "Leaf Spring", "Dually Wheel"),
            new("crawler", "Heavy Swing Arm", "Spiked Wheel", "Heavy Trailing Arm", "Spiked Wheel"),
            new("monster", "Heavy Pogo Strut", "Monster Wheel", "Heavy Pogo Strut", "Monster Wheel"), // (phase 8b: the Monster Truck's only gear; ShapeGrammar.SpecialGears)
        ],
        // The weapon slot: guns by their parts, in the builder's text. A name twice is twice as likely.
        Weapons =
        [
            new("Scrap MG", "Workhorse Receiver{barrel:Standard Barrel,feed:Extended Mag,action:Full-Auto Sear}"),
            new("Scrap MG", "Workhorse Receiver{barrel:Stub Barrel{muzzle:Flash Hider},feed:Box Mag,action:Full-Auto Sear}"),
            new("Scrap MG", "Courier Carbine Receiver{barrel:Standard Barrel,feed:Box Mag}"),
            new("Firebreather", "Firebreather Receiver{barrel:Flame Nozzle,feed:Fuel Canister,action:Flame Valve}", Box: null),
            new("Whaler", "Whaler Receiver{barrel:Harpoon Tube,feed:Harpoon Breech,action:Harpoon Trigger}"),
        ],
        Seeds = new() // (kit 5's, 2026-11-07, again after body styles phases 2 and 3: the most different passing seeds of a 10-seed sweep; the Technical and Tanker Rig keep their seed numbers, made by the grammar since phase 9b; the Fuel Trailer keeps its generator's)
        {
            ["Buggy"] = [1, 3, 4], ["Monster Truck"] = [1, 2, 8], ["Wagon"] = [1, 5, 6], ["Giant Truck"] = [1, 6, 2], ["Car"] = [1, 7, 5], ["Box Truck"] = [1, 4, 2], ["War Bus"] = [1, 6, 10], ["Flatbed Fort"] = [1, 7, 9], ["Gun Wagon"] = [1, 8, 2], ["Scrap Tanker"] = [1, 10, 5],
            ["Drone"] = [1, 12, 5], ["Plane"] = [1, 2, 7], ["Helicopter"] = [1, 2, 10], // (aircraft, docs/vehiclekit/AIRCRAFT.md "In the game": passing seeds of a 12-seed sweep, a different three per style)
        },
        LootTable = "raider",
        Limits = new() { ["crookedPlates"] = 4, ["tiltDegrees"] = 5, ["thirdSeat"] = 0.5, ["slits"] = 0.25 },
    };

    /// <summary>
    /// IT: office kit on wheels, and the first style everything is tried with (the Traders fly it: Crew/Factions/Traders.json). Beige
    /// cases, carpet grey and corporate blue, a hazard stripe of lanyard yellow; dishes, switches, plants and fans where raiders hang
    /// skulls, router antennas and dishes down the flanks, server racks, computers and fax machines in the rooms, neat plates. It is
    /// the tech style: what it carries and drops is electronics (Constructs/Loot.json "it"). (The IT Department's joke pieces, the
    /// CRT monitor, the cable reel and the plaque, went with the old factions, 2026-11-17.)
    /// </summary>
    public static readonly FactionStyle It = new()
    {
        Name = "IT",
        Extra = ["Car", "Buggy", "Concept", "Giant Truck", "Drone", "Plane", "Helicopter"], // (phase 8a: its archetypes' cars; 8b: buggies, monster trucks, wagons, concepts, giants)
        Group = "IT",
        Palette = new()
        {
            ["rust"] = ["#d8d0b8", "#cfc6aa", "#d8d0b8", "#8e9296", "#a7abae"], // (the panels: old beige cases, carpet tile grey)
            ["body"] = ["#1f4e8c", "#2a64a8", "#1f4e8c", "#e6e2d6"],            // (corporate blue, letterhead white)
            ["frame"] = ["#3b3f44"],
            ["war"] = ["#f2c400", "#0f7bd8", "#e6e2d6", "#c0392b"],            // (lanyard yellow, link blue, paper, the red of a failed build)
        },
        Parts = new(Raider.Parts)
        {
            ["engine"] = ["Engine", "V8 Engine"],
            ["wall"] = ["Sheet Steel", "Plate Steel"],
            ["deck"] = ["Plate Steel", "Tread Plate"],
            ["bed"] = ["Plate Steel", "Tread Plate"],
            ["sheet"] = ["Sheet Steel", "Sheet Steel", "Plate Steel"],
            ["patch"] = ["Sheet Steel 0.5 m", "Plate Steel 0.5 m"],
            ["hood"] = ["Snub Hood", "Slab Nose", "Long Hood"],
            ["nose"] = ["Slat Grille", "Bar Grille", "none"],
            ["fender"] = ["Arch Fender"], // (phase 9a: slim arches, the skirt fairing does the rest)
            ["cab"] = ["Windscreen Frame", "Chop Top Cab", "Roll Cage", "Roll Bar"],
            ["heavyCab"] = ["Cab Over Shell", "Tin Roof Cab", "Chop Top Cab"],
            ["spike"] = ["Router Antenna", "Router Antenna", "Wi-Fi Mast", "Satellite Dish"],                 // (Constructs/Parts/Office, Source/PartKit/Generators/OfficeParts.cs)
            ["trophy"] = ["Network Switch", "Satellite Dish", "Network Switch", "Office Plant", "Desk Fan", "Office Plant"],
            ["bunk"] = ["Crew Bench"],                                                   // (nobody sleeps here, officially)
            ["workbench"] = ["Scrap Workbench"],                                         // (the hot desk)
            ["mast"] = ["Satellite Dish", "Wi-Fi Mast", "Satellite Dish", "Router Antenna"],          // (on the roofs: Vehicle.Kit)
            ["gear"] = ["Network Switch", "Satellite Dish", "Office Plant", "Network Switch"], // (on open beds and porches)
            ["rack"] = ["Server Rack"],                                                  // (loot on its shelves)
            ["computer"] = ["Computer"],                                                 // (emails IT: GameSandboxOffice.cs)
            ["fax"] = ["Fax Machine"],                                                   // (the office desk answers on it: GameSandboxOffice.cs)
            ["layout"] = ["open plan", "server room", "stationery cupboard", "break room"],
            ["layout open plan"] = ["computer", "computer", "fax", "rack"],
            ["layout server room"] = ["rack", "rack", "computer", "fax"],
            ["layout stationery cupboard"] = ["fax", "computer", "crates", "shelf"],
            ["layout break room"] = ["bench", "computer", "fax", "crates"],
        },
        LightGear = Raider.LightGear,
        HeavyGear = Raider.HeavyGear,
        // The weapon slot: named for the trade.
        Weapons =
        [
            new("Bill of Sale", "Courier Carbine Receiver{barrel:Standard Barrel,feed:Box Mag}"),
            new("Bill of Sale", "Courier Carbine Receiver{barrel:Stub Barrel{muzzle:Flash Hider},feed:Box Mag}"),
            new("Bulk Order", "Workhorse Receiver{barrel:Standard Barrel,feed:Extended Mag,action:Full-Auto Sear}"),
            new("Final Offer", "Farmhand Pump Receiver{barrel:Scattermouth Barrel,feed:Shell Box,action:Pump Slide}"),
        ],
        // (the shapes' own seeds fail in this style's dice; these are --sweep-vehicle's most different passing seeds)
        Seeds = new()
        {
            ["Technical"] = [2, 5, 6, 8, 11], ["Buggy"] = [1, 3, 6], ["Concept"] = [1, 8, 3], ["Giant Truck"] = [3, 6, 7], ["Car"] = [1, 2, 8], ["Box Truck"] = [1, 5, 3], ["War Bus"] = [1, 5, 10], ["Flatbed Fort"] = [1, 2, 7],
            ["Gun Wagon"] = [1, 9, 7], ["Scrap Tanker"] = [1, 2, 5], ["Tanker Rig"] = [1, 20], ["Fuel Trailer"] = [1, 2],
            ["Drone"] = [3, 8, 11], ["Plane"] = [3, 6, 9], ["Helicopter"] = [3, 5, 8], // (aircraft, docs/vehiclekit/AIRCRAFT.md "In the game": passing seeds of a 12-seed sweep, a different three per style)
        },
        LootTable = "it",
        Limits = new() { ["crookedPlates"] = 1, ["tiltDegrees"] = 1, ["thirdSeat"] = 1, ["slits"] = 0 },
    };

    /// <summary>
    /// Salvager: worn grey haulers in olive and rust brown, picked out in hazard yellow and orange (the Salvagers fly it: Crew/Factions/Salvagers.json);
    /// hubcaps, cones and loudhailers where raiders hang skulls, loudhailers down the flanks, phone masts on the roofs, and rooms of crates and shelves
    /// round the water cooler. They strip other people's batteries, so that is what they carry (Constructs/Loot.json "salvager"). Guns are named for
    /// the salvage trade.
    /// </summary>
    public static readonly FactionStyle Salvager = new()
    {
        Name = "Salvager",
        DiceName = "Scam", // (its name before 2026-11-21: the committed vehicles keep their shapes)
        Extra = ["Car", "Buggy", "Concept", "Drone", "Plane", "Helicopter"], // (phase 8a: its archetypes' cars; 8b: buggies, monster trucks, wagons, concepts, giants)
        Group = "Salvager",
        Palette = new()
        {
            ["rust"] = ["#9aa0a6", "#8a9096", "#b0b4b8", "#7d8a92"],            // (the panels: weathered greys)
            ["body"] = ["#6b6f5e", "#8c5a2e", "#6b6f5e", "#d8d2c0"],            // (olive drab, rust brown, primer)
            ["frame"] = ["#2a2c30"],
            ["war"] = ["#e0b81c", "#d9822b", "#2b2b2b", "#d8d2c0"],             // (hazard yellow, salvage orange, black, primer)
        },
        Parts = new(It.Parts)
        {
            ["engine"] = ["Engine", "V8 Engine", "Engine"],
            ["hood"] = ["Snub Hood", "Long Hood", "Slab Nose"],
            ["fender"] = ["Arch Fender", "Wide Arch Fender"], // (phase 9a: as before: slim arches alone made two committed Technicals read as a box from above)
            ["spike"] = ["Loudhailer", "Loudhailer", "Phone Mast"],                                       // (Constructs/Parts/Signals)
            ["trophy"] = ["Hubcap Plate", "Loudhailer", "Traffic Cone", "Hubcap Plate", "Loudhailer"],
            ["mast"] = ["Phone Mast", "Phone Mast", "Loudhailer"],                                       // (on the roofs: Vehicle.Kit)
            ["gear"] = ["Traffic Cone", "Loudhailer", "Hubcap Plate"],                                   // (on open beds and porches)
            ["cooler"] = ["Water Cooler"],
            ["layout"] = ["scrap store", "scrap store", "office", "break room"],
            ["layout scrap store"] = ["crates", "crates", "shelf", "cooler"],
            ["layout office"] = ["computer", "crates", "rack", "shelf"],
            ["layout break room"] = ["cooler", "bench", "crates", "shelf"],
        },
        LightGear = Raider.LightGear,
        HeavyGear = Raider.HeavyGear,
        Weapons =
        [
            new("Scrapper", "Courier Carbine Receiver{barrel:Standard Barrel,feed:Box Mag}"),
            new("Strip Down", "Workhorse Receiver{barrel:Standard Barrel,feed:Extended Mag,action:Full-Auto Sear}"),
            new("Write-Off", "Farmhand Pump Receiver{barrel:Scattermouth Barrel,feed:Shell Box,action:Pump Slide}"),
            new("Scrapper", "Courier Carbine Receiver{barrel:Stub Barrel{muzzle:Flash Hider},feed:Box Mag}"),
        ],
        // (--sweep-vehicle's most different passing seeds)
        Seeds = new()
        {
            ["Technical"] = [1, 13, 16, 17, 21], ["Buggy"] = [1, 5, 6], ["Concept"] = [1, 8, 6], ["Car"] = [1, 4, 3], ["Box Truck"] = [1, 6, 9], ["War Bus"] = [1, 4, 6], ["Flatbed Fort"] = [1, 9, 6],
            ["Gun Wagon"] = [1, 6, 5], ["Scrap Tanker"] = [1, 6, 2], ["Tanker Rig"] = [1, 2], ["Fuel Trailer"] = [1, 6],
            ["Drone"] = [4, 7, 10], ["Plane"] = [5, 10, 12], ["Helicopter"] = [6, 9, 12], // (aircraft, docs/vehiclekit/AIRCRAFT.md "In the game": passing seeds of a 12-seed sweep, a different three per style)
        },
        LootTable = "salvager",
        Limits = new() { ["crookedPlates"] = 2, ["tiltDegrees"] = 2, ["thirdSeat"] = 1, ["slits"] = 0 },
    };

    /// <summary>
    /// Mercenary: red and dark green, mostly understated (the Mercenaries fly it: Crew/Factions/Mercenaries.json). Red on the panels with the odd
    /// dark green, cream or grey; dark green, cream and navy on the hoods; war paint in blue, red, white and yellow. Loudhailers and dishes on the
    /// roofs, jerry cans, traffic cones and garden gnomes where raiders hang skulls, iron spikes down the flanks, and rooms of benches, shelves and
    /// crates. Plates neat but not new. Only this style builds the Double Decker (<see cref="FactionStyle.Extra"/>). Guns are named for the contract.
    /// </summary>
    public static readonly FactionStyle Mercenary = new()
    {
        Name = "Mercenary",
        DiceName = "British", // (its name before 2026-11-21: the committed vehicles keep their shapes)
        Group = "Mercenary",
        Palette = new()
        {
            ["rust"] = ["#c8102e", "#b5121b", "#c8102e", "#be0f2a", "#c8102e", "#b5121b", "#1f4a32", "#e8dfc8", "#7d8186"], // (the panels: red, mostly; a dark green, cream, grey)
            ["body"] = ["#004225", "#e8dfc8", "#1d2b4f", "#004225"],                                // (dark green, cream, navy)
            ["frame"] = ["#1b1c1e", "#2b2a28"],
            ["war"] = ["#012169", "#c8102e", "#f2f0e8", "#f2c400"],                                // (blue, red, white, yellow)
        },
        Parts = new(Raider.Parts)
        {
            ["engine"] = ["Engine", "Engine", "V8 Engine"],
            ["wall"] = ["Sheet Steel", "Plate Steel", "Plate Steel"],
            ["deck"] = ["Plate Steel", "Tread Plate"],
            ["bed"] = ["Plate Steel", "Tread Plate", "Wood Planks"],
            ["sheet"] = ["Sheet Steel", "Plate Steel", "Plate Steel"],
            ["patch"] = ["Sheet Steel 0.5 m", "Plate Steel 0.5 m"],
            ["hood"] = ["Long Hood", "Snub Hood", "Long Hood", "Bull Hood"],
            ["nose"] = ["Slat Grille", "Bar Grille", "Slat Grille", "none"],
            ["fender"] = ["Arch Fender"], // (phase 9a: a bus's slim wheel arches)
            ["cab"] = ["Windscreen Frame", "Tin Roof Cab", "Chop Top Cab", "Roll Bar"],
            ["heavyCab"] = ["Tin Roof Cab", "Cab Over Shell", "Chop Top Cab"],
            ["spike"] = ["Long Iron Spike", "Iron Spike", "Long Iron Spike"],
            ["trophy"] = [HullLoft.JerryCan, "Traffic Cone", "Garden Gnome", HullLoft.JerryCan, "Traffic Cone"],
            ["mast"] = ["Loudhailer", "Satellite Dish", "Loudhailer"],                                         // (on the roofs: Vehicle.Kit)
            ["gear"] = [HullLoft.JerryCan, "Traffic Cone", HullLoft.JerryCan, "Garden Gnome"],                 // (on open beds and porches)
            ["layout"] = ["mess", "armoury", "store", "bunkroom"],
            ["layout mess"] = ["bench", "bench", "crates", "shelf"],
            ["layout armoury"] = ["shelf", "shelf", "crates", "crates"],
            ["layout store"] = ["crates", "shelf", "crates", "bench"],
            ["layout bunkroom"] = ["bunk", "bunk", "shelf", "crates"],
        },
        LightGear = Raider.LightGear,
        HeavyGear = Raider.HeavyGear,
        // The weapon slot: named for the contract.
        Weapons =
        [
            new("Hazard Pay", "Workhorse Receiver{barrel:Standard Barrel,feed:Extended Mag,action:Full-Auto Sear}"),
            new("Hazard Pay", "Workhorse Receiver{barrel:Stub Barrel{muzzle:Flash Hider},feed:Box Mag,action:Full-Auto Sear}"),
            new("Retainer", "Courier Carbine Receiver{barrel:Standard Barrel,feed:Box Mag}"),
            new("Severance", "Farmhand Pump Receiver{barrel:Scattermouth Barrel,feed:Shell Box,action:Pump Slide}"),
            new("Scorched Earth", "Firebreather Receiver{barrel:Flame Nozzle,feed:Fuel Canister,action:Flame Valve}", Box: null),
        ],
        Extra = ["Double Decker", "Car", "Wagon", "Concept", "Drone", "Plane", "Helicopter"],
        // (--sweep-vehicle's most different passing seeds in this style's dice)
        Seeds = new()
        {
            ["Technical"] = [2, 14, 4, 5, 18], ["Wagon"] = [1, 2, 7], ["Concept"] = [1, 7, 8], ["Car"] = [2, 4, 3], ["Box Truck"] = [1, 9, 8], ["War Bus"] = [1, 3, 5], ["Flatbed Fort"] = [1, 5, 7],
            ["Gun Wagon"] = [1, 4, 5], ["Scrap Tanker"] = [1, 8, 2], ["Tanker Rig"] = [1, 19], ["Fuel Trailer"] = [1, 3], ["Double Decker"] = [1, 4, 9],
            ["Drone"] = [2, 4, 11], ["Plane"] = [1, 3, 8], ["Helicopter"] = [2, 5, 9], // (aircraft, docs/vehiclekit/AIRCRAFT.md "In the game": passing seeds of a 12-seed sweep, a different three per style)
        },
        LootTable = "mercenary",
        Limits = new() { ["crookedPlates"] = 2, ["tiltDegrees"] = 2, ["thirdSeat"] = 0.8, ["slits"] = 0.1 },
    };

    /// <summary>A second look over the same shapes, to prove a style swaps without touching a generator: run <c>--gen-vehicle "Militia Box Truck" --out DIR</c>. Nothing of it ships.</summary>
    public static readonly FactionStyle Militia = new()
    {
        Name = "Militia",
        Group = "Militia",
        Palette = new()
        {
            ["rust"] = ["#4f5a3a", "#47523a", "#5c6644"],
            ["body"] = ["#4f5a3a", "#5c6644"],
            ["frame"] = ["#26282a"],
            ["war"] = ["#c8c4b0"],
        },
        Parts = new(Raider.Parts)
        {
            ["wheel"] = ["Off-Road Wheel"],
            ["engine"] = ["V8 Engine"],
            ["deck"] = ["Plate Steel"],
            ["bed"] = ["Plate Steel", "Tread Plate"],
            ["sheet"] = ["Plate Steel"],
            ["wall"] = ["Plate Steel"],
            ["spike"] = ["none"], // (body styles phase 4: nothing hung down the sides, where raiders hang spikes)
            ["fender"] = ["Wide Arch Fender"], // (phase 9a: always armoured over the wheels, never bare)
            ["trophy"] = [HullLoft.JerryCan], // (jerry cans where raiders hang skulls and hubcaps: HullLoft.Trim.cs)
        },
        LightGear = Raider.LightGear, // (a style needs gear recipes since PickGear: without them nothing of Militia's generated)
        HeavyGear = Raider.HeavyGear,
        Weapons = [new("Service MG", "Reaper Battle Rifle Receiver{barrel:Standard Barrel,feed:Box Mag}")],
        LootTable = "raider",
        Limits = new() { ["crookedPlates"] = 2, ["tiltDegrees"] = 1, ["thirdSeat"] = 1, ["slits"] = 0 },
        Seeds = new() { ["Ore Hauler"] = [1] }, // (the factory kit's ore loop spawns seed 1; the shape's other seeds were never committed)
        Committed = ["Ore Hauler", "Excavator"], // (it ships no vehicles, but the factory kit's ore loop spawns these by name: --gen-vehicles compares them)
    };
}
