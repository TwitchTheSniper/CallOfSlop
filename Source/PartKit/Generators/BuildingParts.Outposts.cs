namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The factions' outposts out in the world beyond the colony (docs/story/OUTPOSTS.md; Game/GameOutposts.cs builds them,
/// Story/Outposts says where): each small, each in its faction's own style (docs/sandbox/constructs/buildings.md). The
/// Ministry's are concrete as ever; Clan MacSlop builds in drystone and tin, the IT Department in beige steel cabinets,
/// The Call Centre in purple portacabins. A blueprint's front (-z) faces the colony when the outpost is placed.
/// </summary>
public sealed partial class BuildingParts
{
    // Clan MacSlop: drystone walls, a rusty tin roof, saltire blue and firelight.
    private const string ClanStone = "#8a8072", ClanTin = "#6e3324", ClanTimber = "#3b2a1c", ClanBlue = "#1f4f9a", ClanFire = "#ff8a3c";

    // The IT Department: beige steel (the colour of every computer it ever owned), a grey lid, teal, and a cold blue light.
    private const string ItBeige = "#c8c2ae", ItGrey = "#5a5f63", ItTeal = "#1c8c99", ItLight = "#cfe8ff";

    // The Call Centre: gloss purple portacabins, gift-card gold, and a pink glow nobody asked for.
    private const string CcPurple = "#7a2f6e", CcGold = "#e0b030", CcPink = "#ffd6ef";

    private static int Board(Blueprint bp, int on, string sock, string text, string paint) => Look(bp, Sign(bp, on, sock, text), paint);

    /// <summary>
    /// A lattice mast stood on a pad's socket: lengths of medium truss, end on end (8 m, then 4 m pieces), with a light at the
    /// top. The small dressing parts (a phone mast, a dish) are a vehicle's size; at 50 m in the snow an outpost is read by
    /// what stands over it, so the mast is the outpost's outline. Returns the top piece.
    /// </summary>
    private static int LatticeMast(Blueprint bp, int pad, string sock, string paint, int eights, int fours, string lamp)
    {
        var plate = Look(bp, bp.Hang("Adapter Medium to Small", pad, sock, "front2.2"), paint, "painted steel"); // (a pad's sockets are small, a truss's medium)
        var top = Look(bp, bp.Hang("Medium Truss 8 m", plate, "back", "-z"), paint, "painted steel");
        for (var k = 1; k < eights; k++)
            top = Look(bp, bp.Hang("Medium Truss 8 m", top, "+z", "-z"), paint, "painted steel");
        for (var k = 0; k < fours; k++)
            top = Look(bp, bp.Hang("Medium Truss 4 m", top, "+z", "-z"), paint, "painted steel");
        Look(bp, bp.Hang("Floodlight Medium", top, "+z", "-y"), lamp);
        return top;
    }

    /// <summary>A flagpole of `lengths` masts (3.75 m each) under its top, on a pad's socket.</summary>
    private static int TallPole(Blueprint bp, int pad, string sock, int lengths, string paint)
    {
        var foot = Look(bp, bp.Hang("Flagpole Base", pad, sock, "b00"), MinWeathered, "concrete");
        var mast = foot;
        for (var k = 0; k < lengths; k++)
            mast = Look(bp, bp.Hang("Flagpole Mast 3.75 m", mast, k == 0 ? "mast" : "t", "b"), paint, "worn paint");
        return Look(bp, bp.Hang("Flagpole Top 3.75 m", mast, "t", "b"), paint, "worn paint");
    }

    private static List<Blueprint> OutpostBlueprints(Dictionary<string, PyDict> defs)
    {
        var o = new List<Blueprint>();
        const string Pad = "Concrete Foundation 4x4 m";

        // Ministry Checkpoint: a road check on the way out from the colony. The job is to stop people and look at their
        // papers, so the plan is a road (4 m, pad 0 and its forecourt) between a booth and a barrier line: the booth stands
        // beside the road with its door and window on it, the barriers close the forecourt either side and leave the road
        // open for whoever is waved through. The booth is the Ministry's 4 m concrete box with its lid; its one bold move is
        // the flag on its tall pole over the barrier, lit, so the checkpoint is seen from a long way off in the snow. Queue
        // posts stand on the forecourt where nobody will ever queue.
        var bp = new Blueprint("Ministry Checkpoint", "A Ministry of Slop road check: a concrete booth, a barrier line and a flag.", defs);
        Pads(bp, [(1, 0), (0, -1), (1, -1), (-1, -1)]); // 0 the road, 1 the booth, 2 the road's forecourt, 3 the booth's, 4 the barrier's
        var rc = RoomCorners(bp, "Concrete", 16, G, 0, 16, 16, door: 0, window: 3);
        Concrete(bp, rc);
        Roof(bp, 16, 0, 16, 16, Slab1);
        Lid(bp, 16, 0, 16, 16, Slab1);
        Door(bp, rc[0]);
        Porch(bp, rc[0], "CHECKPOINT FOUR");
        Sign(bp, rc[1], "fb7_5", "HALT. PAPERS READY. NO PAPERS, NO PASSAGE");
        Sign(bp, rc[3], "fa7_5", "LOST YOUR PAPERS? APPLY IN PERSON, WITH PAPERS");
        Outside(bp, rc[3], "fa0_10");
        bp.Put("Computer", [5.5, 0.375, 2.0], RY180); // the clerk's terminal, facing the road
        bp.Hang("Cast Iron Radiator", rc[2], "ka7_0", "back");
        bp.Hang("Wall Clock", rc[1], "ka7_5", "back");
        Switch(bp, rc[0], "kb7_5", Strip(bp, rc[2], "ka7_10"));
        Look(bp, bp.Hang("Battery", rc[1], "kb7_10", "-z"), MinSlate);
        foreach (var (pad, sock) in new[] { (4, "t0_0"), (4, "t8_0"), (3, "t8_0") }) // the barrier line, the road left open
            Look(bp, bp.Hang("Concrete Barrier 2 m", pad, sock, "b0_0"), MinWeathered, "concrete");
        TallPole(bp, 4, "t4_4", 2, "#d9dcd6");
        // A crossing is lit like a stage: a floodlight mast over the barrier, so the road is seen from the colony at night.
        LatticeMast(bp, 3, "t7_7", MinSlate, 1, 0, "#ffb347");
        var board = Look(bp, bp.Hang("Concrete Notice Board 2 m", 4, "t8_15", "b0"), MinWeathered, "concrete");
        Sign(bp, board, "n0", "CHECKPOINT HOURS: ALWAYS. EXCEPT LUNCH.");
        Sign(bp, board, "n1", "SMILING AT THE OFFICER IS AN OFFENCE");
        foreach (var sock in new[] { "t4_11", "t11_11" })
            bp.Hang("Queue Post", 3, sock, "base");
        bp.Hang("Belisha Beacon", 2, "t0_7", "base");
        o.Add(bp);

        // Ministry Sentry Post: where the perimeter walk starts and ends. Two men on foot need a hard place to stand in when
        // something comes over the ice: a pillbox, its slit to the front, sandbags across half the forecourt and the walk's
        // way out through the other half. A lamp, a flag over it so the walk can find its way back, and the orders.
        bp = new Blueprint("Ministry Sentry Post", "A Ministry of Slop sentry post: a pillbox behind sandbags, where the foot patrol starts.", defs);
        Pads(bp, [(0, -1), (1, 0), (1, -1)]); // 0 the pillbox's, 1 the forecourt, 2 the flag's, 3 the walk's way out
        var box = Look(bp, bp.Hang("Pillbox", 0, "t4_4", "b0_0"), MinConcrete, "concrete");
        foreach (var sock in new[] { "t0_0", "t8_0" })
            Look(bp, bp.Hang("Sandbag Wall 2 m", 1, sock, "b0_0"), "#9a8c6c", "fabric");
        TallPole(bp, 2, "t4_4", 2, "#d9dcd6");
        // The walk is watched from above: a searchlight on a lattice tower over the forecourt, the outline seen first.
        LatticeMast(bp, 1, "t7_7", MinSlate, 1, 1, "#ffb347");
        board = Look(bp, bp.Hang("Concrete Notice Board 2 m", 3, "t0_15", "b0"), MinWeathered, "concrete");
        Sign(bp, board, "n0", "SENTRY POST NINE. THE WALK BEGINS HERE.");
        Sign(bp, board, "n1", "THE WALK ALSO ENDS HERE. DO NOT WANDER.");
        bp.Hang("Traffic Cone", 3, "t11_4", "base");
        bp.Hang("Tea Urn", 2, "t11_11", "foot00");
        o.Add(bp);

        // Clan MacSlop Bothy: a camp the clan keeps within sight of the Ministry's colony, to watch it and annoy it. A bothy
        // is a shelter for whoever is passing, so it is one stone room under a tin ridge with a door that doesn't lock; the
        // clan's own touches are outside: a sandbagged front towards the colony (half of it: the way in is the other half),
        // the saltire on its pole, the casks, a keg and the pipes. Firelight at the door, nothing inside but a bunk.
        bp = new Blueprint("Clan MacSlop Bothy", "Clan MacSlop's watch on the Ministry: a stone bothy under a tin roof, casks and the saltire.", defs);
        Pads(bp, [(0, -1), (1, -1)]); // 0 the bothy, 1 the yard, 2 the casks'
        foreach (var s in new[] { 0, 1, 2 })
            Look(bp, s, ClanStone, "stone");
        rc = RoomCorners(bp, "Concrete", 0, G, 0, 16, 16, door: 0, window: 2);
        foreach (var s in rc)
            Look(bp, s, ClanStone, "stone");
        Look(bp, bp.Put("Corrugated Ridge Roof 4x4 m", Cell(0, G + WallH + 3, 0), Turn("+x", "+z")), ClanTin, "rusty paint");
        Door(bp, rc[0], "Wood Flush Door 1.5x2.25 m", ClanTimber);
        Lamp(bp, "Bulkhead Light", rc[0], "fa0_10", ClanFire);
        Board(bp, rc[1], "fb7_5", "MACSLOP BOTHY. SASSENACHS KNOCK TWICE.", ClanBlue);
        bp.Put("Bunk", [1.75, 0.375, 2.0], R0);
        Look(bp, bp.Hang("Sandbag Wall 2 m", 1, "t0_0", "b0_0"), "#8c7a5a", "fabric");
        bp.Hang("Saltire Flag", 1, "t15_7", "base");
        bp.Hang("Whisky Cask", 2, "t4_4", "foot00");
        bp.Hang("Whisky Cask", 2, "t11_4", "foot00");
        bp.Hang("Wee Keg", 2, "t11_11", "base");
        bp.Hang("Pipe Box", 2, "t4_11", "base");
        bp.Hang("Thistle", 1, "t11_11", "base");
        // The fire inside needs a stack: stone, against the gable on the yard side, standing over the ridge and smoking
        // (Constructs/Ambience.json smokes a Concrete Chimney's top), so the bothy is found by its smoke.
        var stack = Look(bp, bp.Hang("Concrete Chimney 2.75 m", 1, "t0_7", "b00"), ClanStone, "stone");
        Look(bp, bp.Hang("Concrete Chimney 2.75 m", stack, "t00", "b00"), ClanStone, "stone");
        o.Add(bp);

        // IT Field Cabinet: the network has to reach the colony across the ice, so somebody put a box in the middle of it.
        // The job is to keep a rack dry and running and to be found by the engineer sent to reboot it: a beige steel cabinet
        // with one door and no windows (nothing in it wants to look out), the rack in the middle, and outside on its pad the
        // things that make it findable from a kilometre: the mast, the dish and the drum of cable nobody has ever rewound.
        bp = new Blueprint("IT Field Cabinet", "An IT Department network cabinet on the ice: a beige steel box, a rack, a mast and a dish.", defs);
        Pads(bp, [(1, 0)]); // 0 the cabinet, 1 the mast's
        rc = RoomCorners(bp, "Steel", 0, G, 0, 16, 16, door: 0);
        foreach (var s in rc)
            Look(bp, s, ItBeige, "painted steel");
        Look(bp, bp.Put("Steel Floor 4x4 m", Cell(0, Slab1, 0), R0), ItGrey, "painted steel");
        Door(bp, rc[0], "Steel Flush Door 1.5x2.25 m", ItTeal);
        Lamp(bp, "Bulkhead Light", rc[0], "fa0_10", ItLight);
        Board(bp, rc[1], "fb7_5", "NC-14. HAVE YOU TRIED TURNING IT OFF AND ON?", ItTeal);
        bp.Put("Server Rack", [1.875, 0.375, 1.875], R0);
        Look(bp, bp.Hang("Battery", rc[1], "kb7_10", "-z"), ItGrey);
        bp.Hang("Wi-Fi Mast", 1, "t4_4", "base");
        LatticeMast(bp, 1, "t8_8", ItGrey, 1, 1, ItLight);
        bp.Hang("Satellite Dish", 1, "t11_11", "base");
        bp.Hang("Cable Reel", 1, "t11_4", "base");
        o.Add(bp);

        // Call Centre Relay: a phone mast on the ice and the portacabin that keeps it calling. The job is to be heard, so
        // the mast is the tall thing and the loudhailer faces the colony; the cabin is one room with a window on the road
        // (they watch for anyone walking past, then ring them). Gift-card gold on the door and the sign, a prize wheel that
        // has never paid out, and a pink lamp.
        bp = new Blueprint("Call Centre Relay", "The Call Centre's relay on the ice: a purple portacabin, a phone mast, a loudhailer and a prize wheel.", defs);
        Pads(bp, [(1, 0), (0, -1)]); // 0 the cabin, 1 the mast's, 2 the front
        rc = RoomCorners(bp, "Corrugated", 0, G, 0, 16, 16, door: 0, window: 3);
        foreach (var s in rc)
            Look(bp, s, CcPurple, "gloss paint");
        Look(bp, bp.Put("Corrugated Roof Slope 4x4 m", Cell(0, G + WallH, 0), R0), CcPurple, "gloss paint");
        Door(bp, rc[0], "Wood Flush Door 1.5x2.25 m", CcGold);
        Lamp(bp, "Bulkhead Light", rc[0], "fa0_10", CcPink);
        Board(bp, rc[1], "fb7_5", "RELAY 0800. YOUR CALL IS IMPORTANT TO US.", CcGold);
        bp.Hang("Water Cooler", 0, "t11_11", "foot00");
        bp.Hang("Phone Mast", 1, "t4_4", "base");
        LatticeMast(bp, 1, "t8_8", "#d8d8d8", 2, 0, CcPink);
        bp.Hang("Loudhailer", 1, "t11_4", "base");
        bp.Hang("Prize Wheel", 2, "t4_4", "base");
        bp.Hang("Gift Card Sign", 2, "t11_4", "base");
        o.Add(bp);
        return o;
    }
}
