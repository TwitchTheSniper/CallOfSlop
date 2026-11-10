namespace CallOfSlop.VehicleKit;

/// <summary>The factions' styles (docs/vehiclekit/VEHICLEKIT.md, "A faction style").</summary>
public static class Styles
{
    /// <summary>Every style a generator can be run in.</summary>
    public static IReadOnlyList<FactionStyle> All => [Bandit, It, Highland, Scam, British, Militia];

    /// <summary>The styles whose vehicles ship as blueprints.</summary>
    public static IReadOnlyList<FactionStyle> Shipped => [Bandit, It, Highland, Scam, British];

    /// <summary>Bandits: rust and bare metal, nothing matches, spikes, war paint and trophies.</summary>
    public static readonly FactionStyle Bandit = new()
    {
        Name = "Bandit",
        Extra = ["Car"], // (phase 8a: its archetypes' cars)
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
        Seeds = new() // (kit 5's, 2026-11-07, again after body styles phases 2 and 3: the most different passing seeds of a 10-seed sweep; the Technical, Tanker Rig and Fuel Trailer keep their generators')
        {
            ["Box Truck"] = [1, 4, 2], ["War Bus"] = [1, 6, 10], ["Flatbed Fort"] = [1, 7, 9], ["Gun Wagon"] = [1, 8, 2], ["Scrap Tanker"] = [1, 10, 5],
        },
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
        Extra = ["Car"], // (phase 8a: its archetypes' cars)
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
            ["Technical"] = [2, 5, 6, 8, 11], ["Box Truck"] = [1, 5, 3], ["War Bus"] = [1, 5, 10], ["Flatbed Fort"] = [1, 2, 7],
            ["Gun Wagon"] = [1, 9, 7], ["Scrap Tanker"] = [1, 2, 5], ["Tanker Rig"] = [1, 20], ["Fuel Trailer"] = [1, 2],
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
        Extra = ["Car"], // (phase 8a: its archetypes' cars)
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
            ["Technical"] = [1, 13, 16, 17, 21], ["Box Truck"] = [1, 6, 9], ["War Bus"] = [1, 4, 6], ["Flatbed Fort"] = [1, 9, 6],
            ["Gun Wagon"] = [1, 6, 5], ["Scrap Tanker"] = [1, 6, 2], ["Tanker Rig"] = [1, 2], ["Fuel Trailer"] = [1, 6],
        },
        LootTable = "scam",
        Limits = new() { ["crookedPlates"] = 2, ["tiltDegrees"] = 2, ["thirdSeat"] = 1, ["slits"] = 0 },
    };

    /// <summary>
    /// The Clan MacSlop: a Highland clan on wheels, and the loudest faction on the map. Tartan reds and hunting greens on the panels, Saltire
    /// blue and white on the hoods, war paint in pillar-box red and lion-rampant yellow; Saltires on the roofs, thistles, haggis and a Highland
    /// coo's head where bandits hang skulls, wee cabers down the flanks, kegs on the beds and whisky casks in the rooms (loot inside). Every
    /// vehicle carries a Pipe Box (the "piper" slot: Vehicle.Kit), which strikes up the pipes while someone drives it, so you hear them
    /// coming from 150 m (docs/sandbox/MEDIA.md, "Pipe Boxes"; the tunes are PipeTunes). Plates a little crooked (well loved, not neglected).
    /// Guns are named for the glen: the Claymore, the Sgian Dubh, the Highland Charge and the Caber Toss.
    /// </summary>
    public static readonly FactionStyle Highland = new()
    {
        Name = "Highland",
        Extra = ["Car"], // (phase 8a: its archetypes' cars)
        Group = "Clan MacSlop",
        Palette = new()
        {
            ["rust"] = ["#7b1e22", "#1f4a2e", "#23395d", "#5b4a2f", "#8c2a2a", "#2b5a3a"], // (the panels: clan tartan reds, hunting green, navy, tweed)
            ["body"] = ["#005eb8", "#0b4f9c", "#e8e4d8", "#005eb8"],                     // (Saltire blue and white)
            ["frame"] = ["#2a2622", "#33302b"],
            ["war"] = ["#c8102e", "#f2c400", "#e8e4d8", "#1b1714"],                      // (pillar-box red, lion-rampant yellow, white, black)
        },
        Parts = new(Bandit.Parts)
        {
            ["engine"] = ["Engine", "V8 Engine", "V8 Engine"],
            ["wall"] = ["Sheet Steel", "Plate Steel", "Tread Plate", "Plate Steel"],
            ["deck"] = ["Plate Steel", "Tread Plate", "Wood Planks"],
            ["bed"] = ["Wood Planks", "Plate Steel", "Tread Plate"],
            ["hood"] = ["Long Hood", "Bull Hood", "Snub Hood", "Long Hood"],
            ["nose"] = ["Bull Bar", "Bar Grille", "Ram Plow", "none"],
            ["cab"] = ["Roll Cage", "Tin Roof Cab", "Chop Top Cab", "Windscreen Frame", "Roll Bar"],
            ["spike"] = ["Wee Caber", "Wee Caber", "Iron Spike"],                                        // (Constructs/Parts/Highland, tools/constructs/highland_parts.py)
            ["trophy"] = ["Thistle", "Coo Heid", "Haggis on a Post", "Thistle", "Wee Keg", "Coo Heid"],
            ["mast"] = ["Saltire Flag", "Saltire Flag", "Thistle"],                                      // (on the roofs: Vehicle.Kit)
            ["gear"] = ["Wee Keg", "Wee Keg", "Coo Heid", "Saltire Flag"],                               // (on open beds and porches)
            ["piper"] = ["Pipe Box"],                                                                    // (one on every vehicle: Vehicle.Kit)
            ["cask"] = ["Whisky Cask"],                                                                  // (loot inside)
            ["layout"] = ["bothy", "distillery", "ceilidh hall", "kit room"],
            ["layout bothy"] = ["bunk", "bunk", "shelf", "cask"],
            ["layout distillery"] = ["cask", "cask", "cask", "crates"],
            ["layout ceilidh hall"] = ["bench", "bench", "cask", "crates"],
            ["layout kit room"] = ["workbench", "shelf", "cask", "crates"],
        },
        LightGear = Bandit.LightGear,
        HeavyGear = Bandit.HeavyGear,
        // The weapon slot: named for the glen.
        Weapons =
        [
            new("Claymore", "Farmhand Pump Receiver{barrel:Scattermouth Barrel,feed:Shell Box,action:Pump Slide}"),
            new("Sgian Dubh", "Courier Carbine Receiver{barrel:Stub Barrel{muzzle:Flash Hider},feed:Box Mag}"),
            new("Highland Charge", "Workhorse Receiver{barrel:Standard Barrel,feed:Extended Mag,action:Full-Auto Sear}"),
            new("Highland Charge", "Workhorse Receiver{barrel:Standard Barrel,feed:Extended Mag,action:Full-Auto Sear}"),
            new("Caber Toss", "Whaler Receiver{barrel:Harpoon Tube,feed:Harpoon Breech,action:Harpoon Trigger}"),
        ],
        // (--sweep-vehicle's most different passing seeds in this style's dice)
        Seeds = new()
        {
            ["Technical"] = [1, 14, 10, 11, 5], ["Box Truck"] = [1, 3, 7], ["War Bus"] = [1, 7, 6], ["Flatbed Fort"] = [1, 8, 9],
            ["Gun Wagon"] = [1, 6, 2], ["Scrap Tanker"] = [1, 2, 7], ["Tanker Rig"] = [1, 13], ["Fuel Trailer"] = [1, 2],
        },
        LootTable = "highland",
        Limits = new() { ["crookedPlates"] = 3, ["tiltDegrees"] = 3, ["thirdSeat"] = 0.7, ["slits"] = 0.2 },
    };

    /// <summary>
    /// The Ministry of Slop: a very British faction on wheels, affectionate and mostly understated. Bus red on the panels with the odd
    /// racing green, cream or rain-washed grey; racing green, cream and navy on the hoods; war paint in the flag's blue, red and white and
    /// double-yellow-line yellow. Union Flags, Belisha beacons and rain gauges on the roofs, teapots in cosies, traffic cones nobody will
    /// move and garden gnomes where bandits hang skulls, furled brollies down the flanks (it's that sort of day), queue posts on the beds,
    /// and rooms of pillar boxes and red telephone boxes (loot in each) round a tea urn (Constructs/Parts/British,
    /// tools/constructs/british_parts.py). Plates neat but not new. Only this style builds the Double Decker (<see cref="FactionStyle.Extra"/>).
    /// Guns are named the way the British complain: Terribly Sorry, Bit of Bother, a Strongly Worded Letter and Builder's Tea.
    /// </summary>
    public static readonly FactionStyle British = new()
    {
        Name = "British",
        Group = "Ministry of Slop",
        Palette = new()
        {
            ["rust"] = ["#c8102e", "#b5121b", "#c8102e", "#be0f2a", "#c8102e", "#b5121b", "#1f4a32", "#e8dfc8", "#7d8186"], // (the panels: bus red, mostly; a racing green, cream, the grey of a wet Tuesday)
            ["body"] = ["#004225", "#e8dfc8", "#1d2b4f", "#004225"],                                // (racing green, cream, navy)
            ["frame"] = ["#1b1c1e", "#2b2a28"],
            ["war"] = ["#012169", "#c8102e", "#f2f0e8", "#f2c400"],                                // (the flag's blue, red and white; double-yellow-line yellow)
        },
        Parts = new(Bandit.Parts)
        {
            ["engine"] = ["Engine", "Engine", "V8 Engine"],
            ["wall"] = ["Sheet Steel", "Plate Steel", "Plate Steel"],
            ["deck"] = ["Plate Steel", "Tread Plate"],
            ["bed"] = ["Plate Steel", "Tread Plate", "Wood Planks"],
            ["sheet"] = ["Sheet Steel", "Plate Steel", "Plate Steel"],
            ["patch"] = ["Sheet Steel 0.5 m", "Plate Steel 0.5 m"],
            ["hood"] = ["Long Hood", "Snub Hood", "Long Hood", "Bull Hood"],
            ["nose"] = ["Slat Grille", "Bar Grille", "Slat Grille", "none"],
            ["fender"] = ["Arch Fender", "Wide Arch Fender"],
            ["cab"] = ["Windscreen Frame", "Tin Roof Cab", "Chop Top Cab", "Roll Bar"],
            ["heavyCab"] = ["Tin Roof Cab", "Cab Over Shell", "Chop Top Cab"],
            ["spike"] = ["Furled Brolly", "Furled Brolly", "Belisha Beacon"],                                  // (Constructs/Parts/British, tools/constructs/british_parts.py)
            ["trophy"] = ["Teapot", "Traffic Cone", "Garden Gnome", "Teapot", "Rain Gauge", "Traffic Cone"],
            ["mast"] = ["Union Flag", "Belisha Beacon", "Rain Gauge", "Union Flag"],                           // (on the roofs: Vehicle.Kit)
            ["gear"] = ["Queue Post", "Queue Post", "Traffic Cone", "Garden Gnome"],                           // (on open beds and porches: a queue forms)
            ["pillar"] = ["Pillar Box"],                                                                       // (loot inside)
            ["phonebox"] = ["Phone Box"],                                                                      // (loot under the shelf)
            ["urn"] = ["Tea Urn"],
            ["layout"] = ["tea room", "post office", "lost property", "snug"],
            ["layout tea room"] = ["urn", "bench", "bench", "pillar"],
            ["layout post office"] = ["pillar", "pillar", "phonebox", "shelf"],
            ["layout lost property"] = ["phonebox", "shelf", "crates", "pillar"],
            ["layout snug"] = ["bench", "urn", "phonebox", "crates"],
        },
        LightGear = Bandit.LightGear,
        HeavyGear = Bandit.HeavyGear,
        // The weapon slot: named the way they complain.
        Weapons =
        [
            new("Terribly Sorry", "Workhorse Receiver{barrel:Standard Barrel,feed:Extended Mag,action:Full-Auto Sear}"),
            new("Terribly Sorry", "Workhorse Receiver{barrel:Stub Barrel{muzzle:Flash Hider},feed:Box Mag,action:Full-Auto Sear}"),
            new("Bit of Bother", "Courier Carbine Receiver{barrel:Standard Barrel,feed:Box Mag}"),
            new("Strongly Worded Letter", "Farmhand Pump Receiver{barrel:Scattermouth Barrel,feed:Shell Box,action:Pump Slide}"),
            new("Builder's Tea", "Firebreather Receiver{barrel:Flame Nozzle,feed:Fuel Canister,action:Flame Valve}", Box: null),
        ],
        Extra = ["Double Decker", "Car"],
        // (--sweep-vehicle's most different passing seeds in this style's dice)
        Seeds = new()
        {
            ["Technical"] = [2, 14, 4, 5, 18], ["Box Truck"] = [1, 9, 8], ["War Bus"] = [1, 3, 5], ["Flatbed Fort"] = [1, 5, 7],
            ["Gun Wagon"] = [1, 4, 5], ["Scrap Tanker"] = [1, 8, 2], ["Tanker Rig"] = [1, 19], ["Fuel Trailer"] = [1, 3], ["Double Decker"] = [1, 4, 9],
        },
        LootTable = "british",
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
        Parts = new(Bandit.Parts)
        {
            ["wheel"] = ["Off-Road Wheel"],
            ["engine"] = ["V8 Engine"],
            ["deck"] = ["Plate Steel"],
            ["bed"] = ["Plate Steel", "Tread Plate"],
            ["sheet"] = ["Plate Steel"],
            ["wall"] = ["Plate Steel"],
            ["spike"] = ["none"], // (body styles phase 4: nothing hung down the sides, where bandits hang spikes)
            ["trophy"] = [HullLoft.JerryCan], // (jerry cans where bandits hang skulls and hubcaps: HullLoft.Trim.cs)
        },
        LightGear = Bandit.LightGear, // (a style needs gear recipes since PickGear: without them nothing of Militia's generated)
        HeavyGear = Bandit.HeavyGear,
        Weapons = [new("Service MG", "Reaper Battle Rifle Receiver{barrel:Standard Barrel,feed:Box Mag}")],
        LootTable = "bandit",
        Limits = new() { ["crookedPlates"] = 2, ["tiltDegrees"] = 1, ["thirdSeat"] = 1, ["slits"] = 0 },
    };
}
