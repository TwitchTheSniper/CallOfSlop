namespace CallOfSlop.VehicleKit;

/// <summary>The factions' styles (docs/vehiclekit/VEHICLEKIT.md, "A faction style").</summary>
public static class Styles
{
    /// <summary>Every style a generator can be run in.</summary>
    public static IReadOnlyList<FactionStyle> All => [It, Militia];


    /// <summary>The styles whose vehicles ship as blueprints.</summary>
    public static IReadOnlyList<FactionStyle> Shipped => [It];

    /// <summary>
    /// The parts and running gear every style starts from (never generated itself): a style copies these and changes what is its own.
    /// (It was the Bandits' style until the old factions went, 2026-11-21.)
    /// </summary>
    private static readonly FactionStyle Base = new()
    {
        Name = "Base",
        Group = "Base",
        Palette = [],
        Weapons = [],
        Limits = [],
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
        Parts = new(Base.Parts)
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
        LightGear = Base.LightGear,
        HeavyGear = Base.HeavyGear,
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
        Parts = new(Base.Parts)
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
        LightGear = Base.LightGear, // (a style needs gear recipes since PickGear: without them nothing of Militia's generated)
        HeavyGear = Base.HeavyGear,
        Weapons = [new("Service MG", "Reaper Battle Rifle Receiver{barrel:Standard Barrel,feed:Box Mag}")],
        LootTable = "militia",
        Limits = new() { ["crookedPlates"] = 2, ["tiltDegrees"] = 1, ["thirdSeat"] = 1, ["slits"] = 0 },
        Seeds = new() { ["Ore Hauler"] = [1] }, // (the factory kit's ore loop spawns seed 1; the shape's other seeds were never committed)
        Committed = ["Ore Hauler", "Excavator"], // (it ships no vehicles, but the factory kit's ore loop spawns these by name: --gen-vehicles compares them)
    };
}
