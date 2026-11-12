namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The Ministry of Slop's buildings round the penal colony (docs/story/OPENING.md "The Ministry's buildings";
/// Game/GameColonyBuildings.cs places them, Story/Opening/ColonyBuildings.json says where), in the Ministry's style:
/// Brutalism (docs/sandbox/constructs/buildings.md). Raw concrete, a heavy lid round every roof, small deep windows, buttresses
/// and fins, a canopy and steps at each door, amber lamps outside against the cold; inside, the last century's fittings: strip
/// lights on round brown switches, radiators, filing cabinets, a teleprinter, tape reels. Each building's shape comes from
/// its job, and no two have the same outline.
/// </summary>
public sealed partial class BuildingParts
{
    // The Ministry's palette (buildings.md "The Ministry of Slop"): concrete, weathered concrete, and one accent a building.
    private const string MinConcrete = "#a29e94", MinWeathered = "#858179", MinSlate = "#4b5157", MinGreen = "#2f5a3c", MinNavy = "#1f2b45",
        MinWhite = "#d6dad3", MinSoot = "#3a3836";

    private const string Sodium = "#ffb347"; // the lamps outside: warm against the snow
    private const string Tube = "#e4f2dc"; // the strip lights inside: cold and a little green

    private const int G = 2; // the ground floor's first row of cells (a pad is 2 cells thick)
    private const int Slab1 = G + WallH; // the slab over the ground floor

    /// <summary>A step's paint and finish (look.md "The authoring fields").</summary>
    private static int Look(Blueprint bp, int step, string paint, string? finish = null)
    {
        bp.Steps[step]["paint"] = paint;
        if (finish != null)
            bp.Steps[step]["finish"] = finish;
        return step;
    }

    private static void Concrete(Blueprint bp, IEnumerable<int> steps, string paint = MinConcrete)
    {
        foreach (var s in steps)
            Look(bp, s, paint, "concrete");
    }

    /// <summary>A Text Sign by its back on a face socket (a small socket: a bigger sign needs an adapter), saying <paramref name="text"/>.</summary>
    private static int Sign(Blueprint bp, int on, string sock, string text)
    {
        if (text.Length > 60 || text.Any(c => c < ' ' || c > '~'))
            throw new InvalidOperationException($"{bp.Name}: sign text \"{text}\" (60 ASCII characters at most)");
        var s = bp.Hang("Text Sign", on, sock, "back");
        bp.Steps[s]["text"] = new List<object?> { text };
        return Look(bp, s, MinNavy);
    }

    /// <summary>A mains lamp by its back on a socket, glowing <paramref name="colour"/> (lit while the building's battery reaches it).</summary>
    private static int Lamp(Blueprint bp, string part, int on, string sock, string colour)
    {
        var s = bp.Hang(part, on, sock, "back");
        bp.Steps[s]["config"] = new PyDict { ["colour"] = colour };
        return s;
    }

    private static int Outside(Blueprint bp, int on, string sock) => Lamp(bp, "Bulkhead Light", on, sock, Sodium);

    private static int Strip(Blueprint bp, int on, string sock) => Lamp(bp, "Strip Light", on, sock, Tube);

    /// <summary>The room's light switch, on (a wire to each of its lamps: a player can turn the lights off, with a clunk).</summary>
    private static int Switch(Blueprint bp, int on, string sock, params int[] lamps)
    {
        var s = bp.Hang("Bakelite Switch", on, sock, "back");
        bp.Steps[s]["config"] = new PyDict { ["startOn"] = true };
        foreach (var l in lamps)
            bp.Wires.Add((s, l));
        return s;
    }

    /// <summary>A part standing on a pad's top socket: pad <paramref name="i"/>, <paramref name="j"/> (4 m squares) at its local x, z.</summary>
    private static double[] OnPad(int i, int j, double x, double z, double up = 0.375) => [4.0 * i + x, up, 4.0 * j + z];

    /// <summary>The pads, the first at (0, 0) then the rest in order, each next to one already down.</summary>
    private static void Pads(Blueprint bp, IEnumerable<(int I, int J)> pads)
    {
        const string Pad = "Concrete Foundation 4x4 m";
        bp.First(Pad);
        foreach (var (i, j) in pads)
            bp.Put(Pad, Cell(16 * i, 0, 16 * j), R0);
    }

    /// <summary>A flat roof of 4 m slabs over cells x0.., z0.. (nx by nz), its slabs' row <paramref name="y"/>.</summary>
    private static List<int> Roof(Blueprint bp, int x0, int z0, int nx, int nz, int y)
    {
        var o = new List<int>();
        for (var z = z0; z < z0 + nz; z += 16)
            for (var x = x0; x < x0 + nx; x += 16)
                o.Add(Look(bp, bp.Put("Concrete Floor 4x4 m", Cell(x, y, z), R0), MinWeathered, "concrete"));
        return o;
    }

    /// <summary>The lid: fascias round a roof of 4 m slabs (cells x0.., z0.., nx by nz, the slabs' row y), and the corners where two
    /// sides meet. <paramref name="front"/> false leaves the -z side off (where a taller part of the building stands),
    /// <paramref name="back"/> false the +z side.</summary>
    private static List<int> Lid(Blueprint bp, int x0, int z0, int nx, int nz, int y, bool front = true, bool back = true)
    {
        const string Band = "Concrete Fascia 4 m", Corner = "Concrete Fascia Corner";
        var o = new List<int>();
        for (var x = x0; x < x0 + nx; x += 16)
        {
            if (front)
                o.Add(bp.Put(Band, Cell(x, y, z0 - 2), R0));
            if (back)
                o.Add(bp.Put(Band, Cell(x + 15, y, z0 + nz + 1), RY180));
        }
        for (var z = z0; z < z0 + nz; z += 16)
        {
            o.Add(bp.Put(Band, Cell(x0 - 2, y, z + 15), RY90));
            o.Add(bp.Put(Band, Cell(x0 + nx + 1, y, z), RY270));
        }
        if (front)
        {
            o.Add(bp.Put(Corner, Cell(x0 - 2, y, z0 - 2), R0));
            o.Add(bp.Put(Corner, Cell(x0 + nx + 1, y, z0 - 2), RY270));
        }
        if (back)
        {
            o.Add(bp.Put(Corner, Cell(x0 + nx + 1, y, z0 + nz + 1), RY180));
            o.Add(bp.Put(Corner, Cell(x0 - 2, y, z0 + nz + 1), RY90));
        }
        Concrete(bp, o, MinWeathered);
        return o;
    }

    /// <summary>A door on a doorway's hinge socket.</summary>
    private static void Door(Blueprint bp, int jamb, string leaf = "Wood Flush Door 1.5x2.25 m", string paint = MinGreen)
    {
        var h = bp.Hang("Door Hinge", jamb, "hinge", "base");
        Look(bp, bp.Hang(leaf, h, "leaf", "hang"), paint, leaf.StartsWith("Wood", StringComparison.Ordinal) ? "painted wood" : "worn paint");
    }

    /// <summary>The way in: a canopy over a door corner's doorway with a lamp under it and the building's name on its front.</summary>
    private static void Porch(Blueprint bp, int doorCorner, string name)
    {
        var c = Look(bp, bp.Hang("Concrete Canopy 2 m", doorCorner, "fa7_10", "back7"), MinWeathered, "concrete");
        Outside(bp, c, "lamp");
        Sign(bp, c, "sign", name);
    }

    private static int Steps(Blueprint bp, int x, int z) => Look(bp, bp.Put("Concrete Entrance Steps 2 m", Cell(x, 0, z - 4), R0), MinWeathered, "concrete");

    private static void Piers(Blueprint bp, string part, params (int On, string Sock)[] at)
    {
        foreach (var (on, sock) in at)
            Look(bp, bp.Hang(part, on, sock, "back5"), MinWeathered, "concrete");
    }

    private const string FinPart = "Concrete Fin 2.75 m", ButtressPart = "Concrete Buttress 2.75 m";

    private static List<Blueprint> MinistryBlueprints(Dictionary<string, PyDict> defs)
    {
        var o = new List<Blueprint>();
        const int U = Slab1 + 1; // an upper storey's first row
        var roof2 = U + WallH; // the roof slab's row over two storeys

        // Ministry Records Office: the street's monument. An 8 by 8 m hall with the archive over it: a blind concrete box 12 m
        // long that oversails the forecourt on three columns, so the queue stands under cover and the files stand over
        // everybody. Paper wants no daylight: the archive has slots, the hall a high ribbon each side. The door is in the
        // front wall under the overhang (x 2.25 to 3.75 m); inside, the counter crosses the hall at z 4 with the filing, the
        // teleprinter and the stair up (along the west wall, staff only) behind or beside it; upstairs the tape cabinets and
        // more filing. The west wall's inside is left bare for notices.
        var bp = new Blueprint("Ministry Records Office", "The Ministry of Slop's records office: a counter, a queue under the overhang, and the archive over both.", defs);
        Pads(bp, [(1, 0), (0, 1), (1, 1), (0, -1), (1, -1)]);
        var rc = RoomCorners(bp, "Concrete", 0, G, 0, 32, 32);
        var doorWall = bp.Put("Concrete Door Wall 2 m", Cell(8, G, 0), R0);
        var front = bp.Put("Concrete Slot Wall 2 m", Cell(16, G, 0), R0);
        var back = bp.Put("Concrete Wall 4 m", Cell(23, G, 31), RY180);
        var west = bp.Put("Concrete Ribbon Wall 4 m", Cell(0, G, 23), RY90);
        var east = bp.Put("Concrete Ribbon Wall 4 m", Cell(31, G, 8), RY270);
        Concrete(bp, rc.Concat([doorWall, front, back, west, east]));
        var cols = new[] { 0, 15, 30 }.Select(x => bp.Put("Concrete Column 2.75 m", Cell(x, G, -16), R0)).ToList();
        Concrete(bp, cols, MinWeathered);
        Look(bp, bp.Put("Concrete Stair Floor 4x4 m", Cell(0, Slab1, 0), R0), MinWeathered, "concrete");
        foreach (var (x, z) in new[] { (16, 0), (0, 16), (16, 16), (0, -16), (16, -16) })
            Look(bp, bp.Put("Concrete Floor 4x4 m", Cell(x, Slab1, z), R0), MinWeathered, "concrete");
        Look(bp, bp.Put("Concrete Straight Stair 3 m", Cell(4, G, 15), RY180), MinWeathered, "concrete");
        var uc = RoomCorners(bp, "Concrete", 0, U, -16, 32, 48);
        var uFront = bp.Put("Concrete Slot Wall 4 m", Cell(8, U, -16), R0);
        var uBack = bp.Put("Concrete Wall 4 m", Cell(23, U, 31), RY180);
        var uWest = new[] { 7, 23 }.Select(z => bp.Put("Concrete Slot Wall 4 m", Cell(0, U, z), RY90)).ToList();
        var uEast = new[] { -8, 8 }.Select(z => bp.Put("Concrete Slot Wall 4 m", Cell(31, U, z), RY270)).ToList();
        Concrete(bp, uc.Concat([uFront, uBack]).Concat(uWest).Concat(uEast));
        Roof(bp, 0, -16, 32, 48, roof2);
        Lid(bp, 0, -16, 32, 48, roof2);
        Door(bp, doorWall);
        Piers(bp, ButtressPart, (west, "f0_5"), (west, "f8_5"), (east, "f0_5"), (east, "f8_5"));
        Sign(bp, uFront, "f8_5", "MINISTRY OF SLOP");
        Sign(bp, rc[1], "fb7_5", "RECORDS OFFICE - QUEUE HERE");
        Outside(bp, doorWall, "f0_10");
        Outside(bp, front, "f7_10");
        // The hall: the counter, the clerk's things on it, the filing along the back wall, the teleprinter.
        var counter = new[] { 4.0, 6.0 }.Select(x => Look(bp, bp.Put("Mess Table", [x, 0.375, 4.0], RY180), MinSlate, "wood")).ToList();
        bp.Hang("Employee of the Month", counter[0], "top", "base");
        bp.Hang("Teapot", counter[1], "top", "base"); // (the clerk's)
        foreach (var s in new[] { "k0_0", "k7_0", "k15_0" })
            bp.Hang("Filing Cabinet", back, s, "wall");
        bp.Hang("Teleprinter", east, "k8_0", "wall");
        bp.Hang("Cast Iron Radiator", west, "k0_0", "back");
        bp.Hang("Wall Clock", doorWall, "k0_5", "back");
        bp.Put("Office Plant", OnPad(1, 0, 1.75, 1.75), R0);
        var hall = new[] { Strip(bp, west, "k8_10"), Strip(bp, east, "k8_10"), Strip(bp, back, "k8_10") };
        Switch(bp, doorWall, "k7_5", hall);
        foreach (var x in new[] { 1.75, 3.75, 4.0 }) // the queue posts along the forecourt, under the overhang
            bp.Put("Queue Post", OnPad(0, -1, x, 1.75), R0);
        bp.Put("Pillar Box", OnPad(1, -1, 2.125, 1.75), R0);
        // The archive: tape cabinets against the back wall, filing down the east side, strip lights and their switch.
        foreach (var s in new[] { "k7_0", "k15_0" })
            bp.Hang("Tape Cabinet", uBack, s, "wall");
        foreach (var w in uEast) // (the west side is the stairwell's)
            foreach (var s in new[] { "k0_0", "k8_0" })
                bp.Hang("Filing Cabinet", w, s, "wall");
        var archive = new[] { Strip(bp, uEast[0], "k8_10"), Strip(bp, uEast[1], "k8_10") };
        Switch(bp, uBack, "k0_5", archive);
        bp.Put("Union Flag", [6.0, (roof2 + 0.5) * C, 6.0], R0);
        Look(bp, bp.Hang("Battery", back, "k0_10", "-z"), MinSlate); // (the fuse box, high on the back wall)
        o.Add(bp);

        // Colony Canteen: a mess hall is one big room for everybody at once, so it is the tall one: an 8 by 8 m hall two
        // storeys high with no floor between, lit by a clerestory ribbon right round under the lid (steam and noise go up,
        // nobody sees out). Behind it the kitchen is a low block with its own back door for deliveries and a concrete stack
        // for the range, taller than everything in the street. Food comes through the servery, an opening in the wall
        // between them with a counter in it; the tables stand in rows across the hall.
        bp = new Blueprint("Colony Canteen", "The penal colony's canteen: a tall mess hall, a servery, and the kitchen and its stack behind.", defs);
        Pads(bp, [(1, 0), (0, 1), (1, 1), (0, 2), (1, 2)]);
        rc = RoomCorners(bp, "Concrete", 0, G, 0, 32, 32, door: 0);
        front = bp.Put("Concrete Slot Wall 4 m", Cell(8, G, 0), R0);
        var servery = bp.Put("Concrete Garage Wall 4 m", Cell(23, G, 31), RY180);
        west = bp.Put("Concrete Wall 4 m", Cell(0, G, 23), RY90);
        east = bp.Put("Concrete Wall 4 m", Cell(31, G, 8), RY270);
        Concrete(bp, rc.Concat([front, servery, west, east]));
        uc = RoomCorners(bp, "Concrete", 0, Slab1, 0, 32, 32); // the clerestory: walls on walls, no floor
        var clere = new[]
        {
            bp.Put("Concrete Ribbon Wall 4 m", Cell(8, Slab1, 0), R0), bp.Put("Concrete Ribbon Wall 4 m", Cell(23, Slab1, 31), RY180),
            bp.Put("Concrete Ribbon Wall 4 m", Cell(0, Slab1, 23), RY90), bp.Put("Concrete Ribbon Wall 4 m", Cell(31, Slab1, 8), RY270),
        };
        Concrete(bp, uc.Concat(clere));
        var hallRoof = Slab1 + WallH;
        Roof(bp, 0, 0, 32, 32, hallRoof);
        Lid(bp, 0, 0, 32, 32, hallRoof);
        // The kitchen (z 8 to 12 m): two corners at the back, the back door in the east one, 2 m of wall each side up to the hall.
        var kDoor = bp.Put("Concrete Door Corner", Cell(31, G, 47), RY180);
        var kCorner = bp.Put("Concrete Wall Corner", Cell(0, G, 47), RY90);
        var kBack = bp.Put("Concrete Wall 4 m", Cell(23, G, 47), RY180);
        var kWest = bp.Put("Concrete Wall 2 m", Cell(0, G, 39), RY90);
        var kEast = bp.Put("Concrete Wall 2 m", Cell(31, G, 32), RY270);
        Concrete(bp, [kDoor, kCorner, kBack, kWest, kEast]);
        Roof(bp, 0, 32, 32, 16, Slab1);
        Lid(bp, 0, 32, 32, 16, Slab1, front: false);
        var stack = Look(bp, bp.Put("Concrete Column 2.75 m", Cell(23, Slab1 + 1, 39), R0), MinWeathered, "concrete");
        Look(bp, bp.Put("Concrete Column 2.75 m", Cell(23, Slab1 + 1 + WallH, 39), R0), MinSoot, "soot");
        _ = stack;
        Door(bp, rc[0]);
        Door(bp, kDoor, "Steel Flush Door 1.5x2.25 m", MinSlate);
        Porch(bp, rc[0], "CANTEEN");
        Steps(bp, 0, 0);
        Piers(bp, ButtressPart, (west, "f0_5"), (west, "f8_5"), (east, "f0_5"), (east, "f8_5"));
        Piers(bp, FinPart, (front, "f0_5"), (front, "f8_5"), (front, "f15_5"));
        Outside(bp, kBack, "f8_10");
        foreach (var z in new[] { 1.75, 3.75, 5.75 })
        {
            foreach (var x in new[] { 2.0, 6.0 })
                Look(bp, bp.Put("Mess Table", [x, 0.375, z], R0), MinSlate, "wood");
            bp.Put("Seat", [4.0, 0.375, z], RY90);
        }
        Look(bp, bp.Put("Mess Table", [4.0, 0.375, 7.75], RY180), MinSlate, "steel"); // the servery's counter, in the opening
        bp.Put("Tea Urn", OnPad(0, 2, 1.875, 2.0), RY180); // (in the kitchen, at the end of the range)
        foreach (var x in new[] { 4.0, 6.0 })
            Look(bp, bp.Put("Galley Stove", [x, 0.375, 10.0], RY180), MinSlate);
        Sign(bp, servery, "k8_10", "GRUEL 12:00 TO 12:05");
        Sign(bp, west, "k8_5", "SECOND HELPINGS BY WRITTEN REQUEST ONLY");
        bp.Hang("Cast Iron Radiator", west, "k0_0", "back");
        bp.Hang("Cast Iron Radiator", east, "k8_0", "back");
        bp.Hang("Wall Clock", servery, "k0_5", "back");
        var lights = new[] { Strip(bp, clere[2], "k0_0"), Strip(bp, clere[2], "k15_0"), Strip(bp, clere[3], "k0_0"), Strip(bp, clere[3], "k15_0") };
        Switch(bp, front, "k8_5", lights);
        Switch(bp, kBack, "k15_5", Strip(bp, kBack, "k8_10"));
        Look(bp, bp.Hang("Battery", east, "k0_10", "-z"), MinSlate);
        o.Add(bp);

        // Colony Infirmary: long and low, 4 by 8 m, one ward down its length. The weather side (east) is a blank wall on
        // three buttresses; the west has a high ribbon over the beds, so the sick get daylight and no draught. Triage is at
        // the door (the medical station), the two beds beyond it, and the medicine cabinet and the patients' files on the
        // blank wall at the far end, furthest from the door.
        bp = new Blueprint("Colony Infirmary", "The penal colony's infirmary: triage at the door, a ward of two beds and the medicine cabinet.", defs);
        Pads(bp, [(0, 1)]);
        rc = RoomCorners(bp, "Concrete", 0, G, 0, 16, 32, door: 0);
        west = bp.Put("Concrete Ribbon Wall 4 m", Cell(0, G, 23), RY90);
        east = bp.Put("Concrete Wall 4 m", Cell(15, G, 8), RY270);
        Concrete(bp, rc.Concat([west, east]), MinWhite);
        Roof(bp, 0, 0, 16, 32, Slab1);
        Lid(bp, 0, 0, 16, 32, Slab1);
        Door(bp, rc[0]);
        Porch(bp, rc[0], "INFIRMARY");
        Steps(bp, 0, 0);
        Piers(bp, ButtressPart, (east, "f0_5"), (east, "f8_5"), (east, "f15_5"));
        Piers(bp, FinPart, (west, "f0_5"), (west, "f8_5"));
        Sign(bp, east, "k15_5", "FORM 27B: NOTICE OF INTENT TO BE ILL");
        Look(bp, bp.Put("Medical Station", OnPad(0, 0, 2.0, 2.0), RY90), MinWhite);
        bp.Put("Bunk", OnPad(0, 1, 2.0, 0.0), RY90);
        bp.Put("Bunk", OnPad(0, 1, 2.0, 1.75), RY90);
        Look(bp, bp.Hang("Medicine Cabinet", east, "k8_5", "back"), MinWhite);
        Look(bp, bp.Hang("Filing Cabinet", east, "k0_0", "wall"), MinWhite);
        bp.Hang("Cast Iron Radiator", west, "k8_0", "back");
        bp.Hang("Wall Clock", west, "k8_5", "back");
        Switch(bp, rc[0], "kb7_5", Strip(bp, west, "k0_10"), Strip(bp, west, "k15_10"));
        Look(bp, bp.Hang("Battery", east, "k0_10", "-z"), MinSlate);
        o.Add(bp);

        // Requisitions Store: a store is a dock and a counter. The roof runs 4 m out over a loading dock at lorry-bed
        // height (the pads), on two columns, with steps up one end; goods and people both stop at the counter in the wide
        // opening and nobody but the storekeeper goes in. The store behind has slots, not windows: light, and nothing
        // passed out through them. The form number is on the back wall.
        bp = new Blueprint("Requisitions Store", "The Ministry's requisitions store: a covered loading dock, a counter and the shelves behind it.", defs);
        Pads(bp, [(1, 0), (0, -1), (1, -1)]);
        rc = RoomCorners(bp, "Concrete", 0, G, 0, 32, 16);
        front = bp.Put("Concrete Garage Wall 4 m", Cell(8, G, 0), R0);
        back = bp.Put("Concrete Slot Wall 4 m", Cell(23, G, 15), RY180);
        Concrete(bp, rc.Concat([front, back]));
        cols = new[] { 0, 30 }.Select(x => bp.Put("Concrete Column 2.75 m", Cell(x, G, -16), R0)).ToList();
        Concrete(bp, cols, MinWeathered);
        Roof(bp, 0, -16, 32, 32, Slab1);
        var lid = Lid(bp, 0, -16, 32, 32, Slab1);
        Steps(bp, 8, -16);
        Piers(bp, ButtressPart, (back, "f0_5"), (back, "f8_5"), (back, "f15_5"));
        Sign(bp, lid[0], "f15", "REQUISITIONS"); // (on the lid's front, over the dock's middle)
        Sign(bp, back, "k8_5", "ALL ISSUES ON FORM R-12, IN TRIPLICATE");
        Outside(bp, front, "f0_10");
        Outside(bp, front, "f15_10");
        Look(bp, bp.Put("Mess Table", [4.0, 0.375, 0.0], R0), MinSlate, "steel"); // the counter, in the opening
        Look(bp, bp.Put("Parts Shelf", OnPad(0, 0, 2.0, 2.0), RY180), MinSlate);
        Look(bp, bp.Put("Parts Shelf", OnPad(1, 0, 2.0, 2.0), RY180), MinSlate);
        bp.Hang("Filing Cabinet", back, "k0_0", "wall");
        bp.Hang("Teleprinter", back, "k15_0", "wall");
        bp.Hang("Wall Clock", back, "k15_5", "back");
        var crate = bp.Hang("Requisition Crate", 3, "t8_8", "-y"); // (three crates in a huddle on the dock)
        bp.Hang("Requisition Crate", crate, "+x", "-x");
        bp.Hang("Requisition Crate", crate, "-z", "+z");
        Switch(bp, rc[0], "kb7_5", Strip(bp, back, "k8_10"));
        Look(bp, bp.Hang("Battery", back, "k0_10", "-z"), MinSlate);
        o.Add(bp);

        // Colony Gatehouse: a 4 by 4 m concrete post on a pad with its step in front, and its roof thrown 4 m out towards
        // the gate with nothing under it, so whoever is being checked stands under cover and the clerk never has to leave
        // the radiator. The gate clerk's terminal inside faces the front; a Belisha beacon and the phone box out front, and
        // the signs a visitor reads first.
        bp = new Blueprint("Colony Gatehouse", "The penal colony's gatehouse: the gate clerk's post, its roof thrown out over the way in.", defs);
        Pads(bp, [(0, -1)]);
        rc = RoomCorners(bp, "Concrete", 0, G, 0, 16, 16, door: 0, window: 3);
        Concrete(bp, rc);
        Roof(bp, 0, 0, 16, 16, Slab1);
        Roof(bp, -16, 0, 16, 16, Slab1); // (the cantilever, on the first slab's edge)
        Lid(bp, -16, 0, 32, 16, Slab1);
        Door(bp, rc[0]);
        Steps(bp, 0, -16);
        Sign(bp, rc[1], "fb7_5", "PENAL COLONY 7 - ALL VISITORS REPORT HERE");
        Sign(bp, rc[3], "fa7_5", "ESCAPEES ALSO REPORT HERE");
        Outside(bp, rc[0], "fa0_10");
        Outside(bp, rc[3], "fa0_10");
        bp.Put("Computer", [1.5, 0.375, 2.0], RY180);
        bp.Hang("Cast Iron Radiator", rc[2], "ka7_0", "back");
        bp.Hang("Wall Clock", rc[1], "ka7_5", "back");
        bp.Put("Belisha Beacon", OnPad(0, -1, 3.75, 1.75), R0);
        bp.Put("Phone Box", OnPad(0, -1, 2.125, 1.75), R0);
        Switch(bp, rc[0], "kb7_5", Strip(bp, rc[2], "ka7_10"));
        Look(bp, bp.Hang("Battery", rc[1], "kb7_10", "-z"), MinSlate);
        o.Add(bp);
        // Penal Colony: the Ministry's holding pen where a survival world's players wake (docs/story/OPENING.md), and a yard
        // is a place to be counted in. Nine pads (12 by 12 m) inside a wire fence a guard can see through, between four
        // concrete posts with a floodlight on each; the gateway (x 4 to 8 m) is in the front, z 0. Two masses stand in it.
        // The cell block (x 2 to 6, z 6 to 10 m) is the tall one: a blind box two storeys high with no floor between, one
        // window near the top where nobody can reach it, a steel door, and its light switch on the outside. The warden's
        // office (x 6 to 10, z 2 to 6 m) is low, with the one window at eye level the Ministry allows (he watches the gate
        // from his desk) and its roof thrown 4 m west over the muster, in front of the cell door: the inmates stand under it
        // to be counted and the warden counts from his wall. The lore's items and the wake spot keep their places.
        bp = new Blueprint("Penal Colony", "The Ministry of Slop's penal colony: a fenced yard with bunks, a cell block and the warden's office.", defs);
        Pads(bp, [(0, 1), (0, 2), (1, 0), (1, 1), (1, 2), (2, 0), (2, 1), (2, 2)]);
        var yard = RoomCorners(bp, "Concrete", 0, G, 0, 48, 48);
        Concrete(bp, yard, MinWeathered);
        foreach (var k in new[] { 1, 2, 3 }) // the east, back and west sides: from each post's leg A, two 4 m fences to the next
        {
            var fn = bp.Hang("Wire Fence 4 m", yard[k], "ra0", "l0");
            bp.Hang("Wire Fence 4 m", fn, "r0", "l0");
        }
        var gl = bp.Hang("Wire Fence 2 m", yard[0], "ra0", "l0"); // the front: x 2 to 4 m, the gateway, x 8 to 10 m
        bp.Hang("Wire Fence 2 m", yard[1], "rb0", "l0");
        var hg = bp.Hang("Hinge", gl, "r4", "base");
        bp.Hang("Fence Gate", hg, "leaf", "hang");
        foreach (var c in yard)
            Outside(bp, c, "ka7_10"); // the floodlights, into the yard
        // The cell block.
        var cells = RoomCorners(bp, "Concrete", 8, G, 24, 16, 16, door: 0);
        var cellsUp = RoomCorners(bp, "Concrete", 8, Slab1, 24, 16, 16, window: 1);
        Concrete(bp, cells.Concat(cellsUp));
        Roof(bp, 8, 24, 16, 16, Slab1 + WallH);
        Lid(bp, 8, 24, 16, 16, Slab1 + WallH);
        Door(bp, cells[0], "Steel Flush Door 1.5x2.25 m", MinSlate);
        Sign(bp, cellsUp[1], "fb7_5", "CELL BLOCK A - THERE IS NO CELL BLOCK B");
        Switch(bp, cells[1], "fb7_5", Strip(bp, cellsUp[3], "ka7_0")); // (the switch is outside: lights out is the guard's)
        // The warden's office and the muster's roof.
        var office = RoomCorners(bp, "Concrete", 24, G, 8, 16, 16, door: 1, window: 0);
        Concrete(bp, office);
        Roof(bp, 24, 8, 16, 16, Slab1);
        Roof(bp, 8, 8, 16, 16, Slab1); // (thrown out over the muster, on the first slab's edge, up to the cell block's wall)
        Lid(bp, 8, 8, 32, 16, Slab1, back: false);
        Concrete(bp, [bp.Put("Concrete Fascia 4 m", Cell(39, Slab1, 25), RY180), bp.Put("Concrete Fascia Corner", Cell(41, Slab1, 25), RY180)], MinWeathered);
        Door(bp, office[1]);
        Porch(bp, office[1], "WARDEN");
        Sign(bp, office[0], "fb7_5", "ROLL CALL 05:00. AND 05:10. AND 05:20.");
        Outside(bp, office[0], "fb0_10");
        Outside(bp, office[3], "fa7_10");
        bp.Put("Computer", [7.75, 0.375, 4.0], RY90); // the warden's desk, facing his door
        bp.Hang("Filing Cabinet", office[3], "kb7_0", "wall");
        bp.Hang("Cast Iron Radiator", office[0], "ka7_0", "back");
        bp.Hang("Wall Clock", office[0], "kb7_5", "back");
        Switch(bp, office[1], "kb7_5", Strip(bp, office[3], "ka7_10"));
        Look(bp, bp.Hang("Battery", office[2], "kb7_10", "-z"), MinSlate);
        foreach (var z in new[] { 1.75, 4.0 }) // the open-air dormitory: two Bunks down the yard's west side (the induction's bunk step)
            bp.Put("Bunk", [1.75, 0.375, z], R0);
        o.Add(bp);

        // Colony Barracks: the guards' quarters beside the penal colony, where the lockdown squad turns out from
        // (Game/GameColonyLockdown.cs). Guards sleep away from the door and leave by it fast, so it is a narrow slab two
        // storeys tall, 4 by 8 m: the guardroom below (the duty desk, the gun rack, a locker, an open doorway in the east
        // wall with no door, because the squad comes out of it without stopping, steps and a canopy outside it), the
        // dormitory above, up a stair along the east wall at the back, with a high ribbon each side over the bunks. Fins
        // down the east front, buttresses on the west. The lore's items keep the west and back walls downstairs.
        bp = new Blueprint("Colony Barracks", "The Ministry of Slop's guard barracks beside the penal colony.", defs);
        Pads(bp, [(0, 1)]);
        rc = RoomCorners(bp, "Concrete", 0, G, 0, 16, 32, door: 1);
        west = bp.Put("Concrete Wall 4 m", Cell(0, G, 23), RY90);
        east = bp.Put("Concrete Slot Wall 4 m", Cell(15, G, 8), RY270);
        Concrete(bp, rc.Concat([west, east]));
        Look(bp, bp.Put("Concrete Floor 4x4 m", Cell(0, Slab1, 0), R0), MinWeathered, "concrete");
        Look(bp, bp.Put("Concrete Stair Floor 4x4 m", Cell(15, Slab1, 31), RY180), MinWeathered, "concrete");
        Look(bp, bp.Put("Concrete Straight Stair 3 m", Cell(11, G, 16), R0), MinWeathered, "concrete");
        uc = RoomCorners(bp, "Concrete", 0, U, 0, 16, 32);
        var dormW = bp.Put("Concrete Ribbon Wall 4 m", Cell(0, U, 23), RY90);
        var dormE = bp.Put("Concrete Ribbon Wall 4 m", Cell(15, U, 8), RY270);
        Concrete(bp, uc.Concat([dormW, dormE]));
        Roof(bp, 0, 0, 16, 32, roof2);
        Lid(bp, 0, 0, 16, 32, roof2);
        Porch(bp, rc[1], "BARRACKS");
        Look(bp, bp.Put("Concrete Entrance Steps 2 m", Cell(19, 0, 0), RY270), MinWeathered, "concrete");
        Piers(bp, FinPart, (east, "f0_5"), (east, "f8_5"), (east, "f15_5"), (dormE, "f0_5"), (dormE, "f8_5"), (dormE, "f15_5"));
        Piers(bp, ButtressPart, (west, "f0_5"), (west, "f8_5"), (west, "f15_5"));
        Sign(bp, uc[1], "fb7_5", "GUARDS ONLY. GUARDS, THIS MEANS YOU");
        Outside(bp, rc[0], "fa7_10");
        Outside(bp, rc[2], "fa7_10");
        // The guardroom.
        bp.Put("Computer", [1.5, 0.375, 6.0], RY180); // the duty desk
        Look(bp, bp.Hang("Locker", west, "k15_0", "wall"), MinGreen, "worn paint");
        bp.Hang("Gun Rack", rc[3], "ka7_0", "wall");
        bp.Hang("Cast Iron Radiator", rc[0], "ka7_0", "back");
        bp.Hang("Wall Clock", east, "k0_5", "back");
        Switch(bp, rc[1], "kb7_5", Strip(bp, east, "k8_10"));
        Look(bp, bp.Hang("Battery", east, "k15_10", "-z"), MinSlate);
        // The dormitory: three bunks across the room, a locker at each end, the switch at the stair's head.
        foreach (var z in new[] { 1.75, 3.75, 5.75 })
            bp.Put("Bunk", [2.0, (Slab1 + 0.5) * C, z], RY90);
        Look(bp, bp.Hang("Locker", uc[0], "ka7_0", "wall"), MinGreen, "worn paint");
        Look(bp, bp.Hang("Locker", uc[3], "kb7_0", "wall"), MinGreen, "worn paint");
        bp.Hang("Cast Iron Radiator", dormW, "k15_0", "back");
        Switch(bp, uc[2], "kb7_5", Strip(bp, dormW, "k8_10"));
        o.Add(bp);
        return o;
    }
}
