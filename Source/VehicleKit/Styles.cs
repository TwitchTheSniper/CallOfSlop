namespace CallOfSlop.VehicleKit;

/// <summary>The factions' styles (docs/vehiclekit/VEHICLEKIT.md, "A faction style").</summary>
public static class Styles
{
    /// <summary>Every style a generator can be run in.</summary>
    public static IReadOnlyList<FactionStyle> All => [Bandit, It, Scam, Militia];

    /// <summary>The styles whose vehicles ship as blueprints.</summary>
    public static IReadOnlyList<FactionStyle> Shipped => [Bandit, It, Scam];

    /// <summary>Bandits: rust and bare metal, nothing matches, spikes, war paint and trophies.</summary>
    public static readonly FactionStyle Bandit = new()
    {
        Name = "Bandit",
        Group = "Bandit",
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
            ["fender"] = ["Arch Fender", "Wide Arch Fender", "none"],
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
        LootTable = "bandit",
        Limits = new() { ["crookedPlates"] = 4, ["tiltDegrees"] = 5, ["thirdSeat"] = 0.5, ["slits"] = 0.25 },
    };

    /// <summary>
    /// The IT Department: a parody of modern office work, and the first faction everything is tried with. Beige cases, carpet
    /// grey and corporate blue, a hazard stripe of lanyard yellow; monitors, dishes, switches, cable reels, plants and plaques where
    /// bandits hang skulls, router antennas and dishes down the flanks, server racks, computers and fax machines in the rooms (the
    /// Computer and the Fax Machine are the only line to the IT Department: docs/story/STORY.md), neat plates (it passed an audit).
    /// Guns are named for the job. It is the tech faction: what it carries and drops is electronics (Constructs/Loot.json "it").
    /// </summary>
    public static readonly FactionStyle It = new()
    {
        Name = "IT",
        Group = "IT Department",
        Palette = new()
        {
            ["rust"] = ["#d8d0b8", "#cfc6aa", "#d8d0b8", "#8e9296", "#a7abae"], // (the panels: old beige cases, carpet tile grey)
            ["body"] = ["#1f4e8c", "#2a64a8", "#1f4e8c", "#e6e2d6"],            // (corporate blue, letterhead white)
            ["frame"] = ["#3b3f44"],
            ["war"] = ["#f2c400", "#0f7bd8", "#e6e2d6", "#c0392b"],            // (lanyard yellow, link blue, paper, the red of a failed build)
        },
        Parts = new(Bandit.Parts)
        {
            ["engine"] = ["Engine", "V8 Engine"],
            ["wall"] = ["Sheet Steel", "Plate Steel"],
            ["deck"] = ["Plate Steel", "Tread Plate"],
            ["bed"] = ["Plate Steel", "Tread Plate"],
            ["sheet"] = ["Sheet Steel", "Sheet Steel", "Plate Steel"],
            ["patch"] = ["Sheet Steel 0.5 m", "Plate Steel 0.5 m"],
            ["hood"] = ["Snub Hood", "Slab Nose", "Long Hood"],
            ["nose"] = ["Slat Grille", "Bar Grille", "none"],
            ["fender"] = ["Arch Fender", "Wide Arch Fender"],
            ["cab"] = ["Windscreen Frame", "Chop Top Cab", "Roll Cage", "Roll Bar"],
            ["heavyCab"] = ["Cab Over Shell", "Tin Roof Cab", "Chop Top Cab"],
            ["spike"] = ["Router Antenna", "Router Antenna", "Wi-Fi Mast", "Satellite Dish"],                 // (Constructs/Parts/Office, tools/constructs/office_parts.py)
            ["trophy"] = ["CRT Monitor", "Satellite Dish", "Network Switch", "Cable Reel", "CRT Monitor", "Office Plant", "Desk Fan", "Employee of the Month"],
            ["bunk"] = ["Crew Bench"],                                                   // (nobody sleeps here, officially)
            ["workbench"] = ["Scrap Workbench"],                                         // (the hot desk)
            ["mast"] = ["Satellite Dish", "Wi-Fi Mast", "Satellite Dish", "Router Antenna"],          // (on the roofs: Vehicle.Kit)
            ["gear"] = ["Cable Reel", "Network Switch", "CRT Monitor", "Satellite Dish", "Cable Reel"], // (on open beds and porches)
            ["rack"] = ["Server Rack"],                                                  // (loot on its shelves)
            ["computer"] = ["Computer"],                                                 // (emails IT: GameSandboxOffice.cs)
            ["fax"] = ["Fax Machine"],                                                   // (IT answers on it)
            ["layout"] = ["open plan", "server room", "stationery cupboard", "break room"],
            ["layout open plan"] = ["computer", "computer", "fax", "rack"],
            ["layout server room"] = ["rack", "rack", "computer", "fax"],
            ["layout stationery cupboard"] = ["fax", "computer", "crates", "shelf"],
            ["layout break room"] = ["bench", "computer", "fax", "crates"],
        },
        LightGear = Bandit.LightGear,
        HeavyGear = Bandit.HeavyGear,
        // The weapon slot: named for the job.
        Weapons =
        [
            new("Ticket Closer", "Courier Carbine Receiver{barrel:Standard Barrel,feed:Box Mag}"),
            new("Ticket Closer", "Courier Carbine Receiver{barrel:Stub Barrel{muzzle:Flash Hider},feed:Box Mag}"),
            new("Reply All", "Workhorse Receiver{barrel:Standard Barrel,feed:Extended Mag,action:Full-Auto Sear}"),
            new("Hard Reset", "Farmhand Pump Receiver{barrel:Scattermouth Barrel,feed:Shell Box,action:Pump Slide}"),
        ],
        // (the shapes' own seeds fail in this style's dice; these are --sweep-vehicle's most different passing seeds)
        Seeds = new()
        {
            ["Technical"] = [2, 5, 6, 8, 11], ["Box Truck"] = [1, 20, 21], ["War Bus"] = [6, 15, 16], ["Flatbed Fort"] = [7, 17, 21],
            ["Gun Wagon"] = [1, 10, 14], ["Scrap Tanker"] = [1, 10, 14], ["Tanker Rig"] = [1, 20], ["Fuel Trailer"] = [1, 2],
        },
        LootTable = "it",
        Limits = new() { ["crookedPlates"] = 1, ["tiltDegrees"] = 1, ["thirdSeat"] = 1, ["slits"] = 0 },
    };

    /// <summary>
    /// The Call Centre: the Scammers (docs/story/STORY.md, "The Scammers") on wheels. A parody of the scam call centre: cubicle grey and
    /// corporate teal, with WINNER yellow, hot pink and dollar green where bandits use war paint; desk phones, headsets on stands, GIFT CARDS
    /// signs and a prize wheel where bandits hang skulls, loudhailers down the flanks, phone masts on the roofs, and rooms of cubicles
    /// (a drawer of loot in each) round the water cooler (Constructs/Parts/CallCentre, tools/constructs/callcentre_parts.py). They run on
    /// other people's batteries, so that is what they carry (Constructs/Loot.json "scam"). Guns are named for the job.
    /// </summary>
    public static readonly FactionStyle Scam = new()
    {
        Name = "Scam",
        Group = "The Call Centre",
        Palette = new()
        {
            ["rust"] = ["#9aa0a6", "#8a9096", "#b0b4b8", "#7d8a92"],            // (the panels: cubicle fabric greys)
            ["body"] = ["#1a8c8c", "#14706f", "#1a8c8c", "#f2f2ee"],            // (corporate teal, a brochure's white)
            ["frame"] = ["#2a2c30"],
            ["war"] = ["#ffd400", "#ff3d8b", "#21c45a", "#f2f2ee"],             // (WINNER yellow, hot pink, dollar green)
        },
        Parts = new(It.Parts)
        {
            ["engine"] = ["Engine", "V8 Engine", "Engine"],
            ["hood"] = ["Snub Hood", "Long Hood", "Slab Nose"],
            ["spike"] = ["Loudhailer", "Loudhailer", "Phone Mast"],                                       // (Constructs/Parts/CallCentre)
            ["trophy"] = ["Office Phone", "Call Headset", "Gift Card Sign", "Prize Wheel", "Office Phone", "Gift Card Sign", "Call Headset", "Loudhailer"],
            ["mast"] = ["Phone Mast", "Phone Mast", "Loudhailer", "Gift Card Sign"],                     // (on the roofs: Vehicle.Kit)
            ["gear"] = ["Prize Wheel", "Gift Card Sign", "Office Phone", "Loudhailer"],                  // (on open beds and porches)
            ["cubicle"] = ["Cubicle Desk"],                                                              // (loot in its drawer)
            ["cooler"] = ["Water Cooler"],
            ["layout"] = ["call floor", "call floor", "managers office", "break room"],
            ["layout call floor"] = ["cubicle", "cubicle", "cubicle", "cooler"],
            ["layout managers office"] = ["computer", "cubicle", "rack", "crates"],                     // (the computer: someone has to click the adverts)
            ["layout break room"] = ["cooler", "bench", "cubicle", "crates"],
        },
        LightGear = Bandit.LightGear,
        HeavyGear = Bandit.HeavyGear,
        Weapons =
        [
            new("Cold Caller", "Courier Carbine Receiver{barrel:Standard Barrel,feed:Box Mag}"),
            new("Robocall", "Workhorse Receiver{barrel:Standard Barrel,feed:Extended Mag,action:Full-Auto Sear}"),
            new("Final Notice", "Farmhand Pump Receiver{barrel:Scattermouth Barrel,feed:Shell Box,action:Pump Slide}"),
            new("Cold Caller", "Courier Carbine Receiver{barrel:Stub Barrel{muzzle:Flash Hider},feed:Box Mag}"),
        ],
        // (--sweep-vehicle's most different passing seeds)
        Seeds = new()
        {
            ["Technical"] = [1, 13, 16, 17, 21], ["Box Truck"] = [4, 11, 23], ["War Bus"] = [6, 10, 12], ["Flatbed Fort"] = [1, 22, 23],
            ["Gun Wagon"] = [1, 5, 11], ["Scrap Tanker"] = [1, 5, 23], ["Tanker Rig"] = [1, 2], ["Fuel Trailer"] = [1, 6],
        },
        LootTable = "scam",
        Limits = new() { ["crookedPlates"] = 2, ["tiltDegrees"] = 2, ["thirdSeat"] = 1, ["slits"] = 0 },
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
        Parts = new(Bandit.Parts)
        {
            ["wheel"] = ["Off-Road Wheel"],
            ["engine"] = ["V8 Engine"],
            ["deck"] = ["Plate Steel"],
            ["bed"] = ["Plate Steel", "Tread Plate"],
            ["sheet"] = ["Plate Steel"],
            ["wall"] = ["Plate Steel"],
            ["spike"] = ["Iron Spike"],
            ["trophy"] = ["Hubcap Plate"],
        },
        Weapons = [new("Service MG", "Reaper Battle Rifle Receiver{barrel:Standard Barrel,feed:Box Mag}")],
        LootTable = "bandit",
        Limits = new() { ["crookedPlates"] = 2, ["tiltDegrees"] = 1, ["thirdSeat"] = 1, ["slits"] = 0 },
    };
}
