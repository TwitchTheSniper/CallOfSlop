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

    /// <summary>A notice in the colony's style (cream board, black letters, three lines), or a one-line cream strip where a
    /// notice board's height doesn't fit (<paramref name="size"/> Narrow), by its back on a face socket.</summary>
    private static int Sign(Blueprint bp, int on, string sock, string text, BoardSize size = BoardSize.Notice) => Board(size, bp, on, sock, text, BoardCream);

    /// <summary>A lit Text Sign by its back on a face socket (the other factions' signs: screens, not boards), saying <paramref name="text"/>.</summary>
    private static int TextSign(Blueprint bp, int on, string sock, string text)
    {
        if (text.Length > 60 || text.Any(c => c < ' ' || c > '~'))
            throw new InvalidOperationException($"{bp.Name}: sign text \"{text}\" (60 ASCII characters at most)");
        var s = bp.Hang("Text Sign", on, sock, "back");
        bp.Steps[s]["text"] = new List<object?> { text };
        return Look(bp, s, MinNavy);
    }

    // The penal colony's signs (buildings.md "Signs"): signwritten boards, not screens. A block's name in cream on Ministry
    // green; the rules in black on cream; a warning in cream on red. Every block has a number.
    private const string BoardGreen = "#2f5a3c", BoardCream = "#d8d1b4", BoardRed = "#8e2a22";

    private static int Board(BoardSize bl, Blueprint bp, int on, string sock, string text, string paint)
    {
        if (text.Length > 60 || text.Any(c => c < ' ' || c > '~'))
            throw new InvalidOperationException($"{bp.Name}: sign text \"{text}\" (60 ASCII characters at most)");
        var s = bp.Hang(bl switch { BoardSize.Wide => "Sign Board 3 m", BoardSize.Narrow => "Sign Board 2 m", _ => "Notice Board" }, on, sock, "back");
        bp.Steps[s]["text"] = new List<object?> { text };
        return Look(bp, s, paint);
    }

    private enum BoardSize { Wide, Narrow, Notice }

    /// <summary>A block's name board, 3 m (or 2 m where it hangs on a canopy): "BLOCK 4 - REQUISITIONS".</summary>
    private static int NameBoard(Blueprint bp, int on, string sock, string text, bool narrow = false) => Board(narrow ? BoardSize.Narrow : BoardSize.Wide, bp, on, sock, text, BoardGreen);

    /// <summary>A notice of rules, black on cream, three lines.</summary>
    private static int Notice(Blueprint bp, int on, string sock, string text) => Board(BoardSize.Notice, bp, on, sock, text, BoardCream);

    /// <summary>A warning, cream on red, one line on a 2 m board.</summary>
    private static int Warning(Blueprint bp, int on, string sock, string text) => Board(BoardSize.Narrow, bp, on, sock, text, BoardRed);

    /// <summary>A slogan over the street: a Roof Sign Frame by its middle foot on a lid's top socket, a 3 m board on it in cream on
    /// Ministry red, and two sodium lamps under its hood to light it at night.</summary>
    private static int Slogan(Blueprint bp, int lid, string sock, string text)
    {
        var f = bp.Hang("Roof Sign Frame 3 m", lid, sock, "b");
        Board(BoardSize.Wide, bp, f, "sign", text, BoardRed);
        Outside(bp, f, "lamp0");
        Outside(bp, f, "lamp1");
        return f;
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

    /// <summary>The pads, the first at (0, 0) then the rest in order, each next to one already down. <paramref name="floor"/>: the
    /// finish of the pads that are indoors (terrazzo or lino on top, plain concrete at their edges: Materials/Surfaces.json), those
    /// <paramref name="indoors"/> picks (all when null); the rest keep the foundation's concrete.</summary>
    private static void Pads(Blueprint bp, IEnumerable<(int I, int J)> pads, string? floor = null, Func<int, int, bool>? indoors = null)
    {
        const string Pad = "Concrete Foundation 4x4 m";
        var first = bp.First(Pad);
        if (floor != null && (indoors?.Invoke(0, 0) ?? true))
            bp.Steps[first]["finish"] = floor;
        foreach (var (i, j) in pads)
        {
            var s = bp.Put(Pad, Cell(16 * i, 0, 16 * j), R0);
            if (floor != null && (indoors?.Invoke(i, j) ?? true))
                bp.Steps[s]["finish"] = floor;
        }
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
        NameBoard(bp, c, "sign", name, narrow: true);
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

        // Ministry Records Office (Block 2): the street's monument. An 8 by 8 m hall with the archive over it: a blind concrete
        // box 12 m long that oversails the forecourt on three columns, so the queue stands under cover and the files stand over
        // everybody. Paper wants no daylight: the archive has slots, the hall a high ribbon each side. The hall is split down its
        // length by the counter (x 3.5 to 4 m): the public's side (east) is the door's (x 4 to 6 m in the front wall), with the
        // benches along the east wall and the clock over them; the clerks' side (west) has the filing along the back, the
        // teleprinter, and the Wide Stair up the west wall to the archive, its foot behind the counter. The way between them is
        // the 1.1 m gap at the counter's back end, marked staff only. Upstairs the tape cabinets and more filing.
        var bp = new Blueprint("Ministry Records Office", "The Ministry of Slop's records office: a counter, a queue under the overhang, and the archive over both.", defs);
        Pads(bp, [(1, 0), (0, 1), (1, 1), (0, -1), (1, -1)], "lino", (_, j) => j >= 0); // (the queue under the overhang stands on concrete)
        var rc = RoomCorners(bp, "Concrete", 0, G, 0, 32, 32);
        var front = bp.Put("Concrete Slot Wall 2 m", Cell(8, G, 0), R0);
        var doorWall = bp.Put("Concrete Door Wall 2 m", Cell(16, G, 0), R0);
        var back = bp.Put("Concrete Wall 4 m", Cell(23, G, 31), RY180);
        var west = bp.Put("Concrete Ribbon Wall 4 m", Cell(0, G, 23), RY90);
        var east = bp.Put("Concrete Ribbon Wall 4 m", Cell(31, G, 8), RY270);
        Concrete(bp, rc.Concat([doorWall, front, back, west, east]));
        var cols = new[] { 0, 15, 30 }.Select(x => bp.Put("Concrete Column 2.75 m", Cell(x, G, -16), R0)).ToList();
        Concrete(bp, cols, MinWeathered);
        Look(bp, bp.Put("Concrete Wide Stair Floor", Cell(0, Slab1, 0), R0), MinWeathered, "lino");
        foreach (var (x, z) in new[] { (16, 0), (0, 16), (16, 16), (0, -16), (16, -16) })
            Look(bp, bp.Put("Concrete Floor 4x4 m", Cell(x, Slab1, z), R0), MinWeathered, "lino"); // the archive's floor
        Look(bp, bp.Put("Concrete Wide Stair 3 m", Cell(7, G, 15), RY180), MinWeathered, "concrete"); // (foot at z 4 m, top at the front)
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
        NameBoard(bp, uFront, "f8_5", "BLOCK 2 - MINISTRY RECORDS");
        Notice(bp, rc[0], "fa7_5", "QUEUE HERE. QUEUE QUIETLY. QUEUE.");
        Outside(bp, doorWall, "f0_10");
        Outside(bp, doorWall, "f7_10");
        // The counter: three lengths down the hall from the front, their closed side to the public (east).
        var counter = Look(bp, bp.Put("Service Counter 2 m", OnPad(0, 0, 3.75, 1.75), RY270), "#6b4a2e", "worn paint");
        var c2 = bp.Hang("Service Counter 2 m", counter, "+x", "-x");
        bp.Hang("Service Counter 2 m", c2, "+x", "-x");
        bp.Hang("Employee of the Month", counter, "top", "base");
        bp.Hang("Teapot", c2, "top", "base"); // (the clerk's)
        Warning(bp, back, "k15_5", "STAFF ONLY BEYOND THIS POINT");
        // The public's side: a bench along the east wall, the clock over it, the forms on the front wall.
        bp.Hang("Waiting Bench 2 m", east, "k8_0", "wall");
        bp.Hang("Wall Clock", east, "k8_5", "back");
        bp.Hang("Cast Iron Radiator", rc[1], "ka7_0", "back");
        Notice(bp, east, "k0_5", "FORMS ARE AVAILABLE ON REQUEST. REQUESTS ON FORM 1.");
        // The clerks' side: the filing down the back wall, the teleprinter by the stair, a desk and its lamp.
        bp.Hang("Teleprinter", back, "k15_0", "wall");
        bp.Hang("Filing Cabinet", rc[3], "ka7_0", "wall");
        bp.Hang("Cast Iron Radiator", back, "k0_0", "back"); // (the public's end of the back wall)
        var hall = new[] { Strip(bp, west, "k8_10"), Strip(bp, east, "k0_10"), Strip(bp, back, "k8_10") };
        Switch(bp, doorWall, "k7_5", hall);
        foreach (var x in new[] { 4.0, 4.0 + 1.75 }) // the queue posts along the forecourt, under the overhang, to the door
            bp.Put("Queue Post", OnPad(1, -1, x - 4.0, 1.75), R0);
        bp.Put("Pillar Box", OnPad(0, -1, 2.125, 1.75), R0);
        // The archive: tape cabinets against the back wall, filing down the east side, strip lights and their switch.
        foreach (var sock in new[] { "k7_0", "k15_0" })
            bp.Hang("Tape Cabinet", uBack, sock, "wall");
        foreach (var w in uEast) // (the west side is the stairwell's)
            foreach (var sock in new[] { "k0_0", "k8_0" })
                bp.Hang("Filing Cabinet", w, sock, "wall");
        var archive = new[] { Strip(bp, uEast[0], "k8_10"), Strip(bp, uEast[1], "k8_10") };
        Switch(bp, uBack, "k0_5", archive);
        bp.Put("Union Flag", [6.0, (roof2 + 0.5) * C, 6.0], R0);
        bp.Put("Tannoy Mast", [2.0, (roof2 + 1) * C, -2.0], R0); // (the Ministry's voice, over the queue: every announcement starts here)
        Look(bp, bp.Hang("Battery", back, "k0_10", "-z"), MinSlate); // (the fuse box, high on the back wall)
        o.Add(bp);

        // Colony Canteen: a mess hall is one big room for everybody at once, so it is the tall one: an 8 by 8 m hall two
        // storeys high with no floor between, lit by a clerestory ribbon right round under the lid (steam and noise go up,
        // nobody sees out). Behind it the kitchen is a low block with its own back door for deliveries and a concrete stack
        // for the range, taller than everything in the street. Food comes through the servery, an opening in the wall
        // between them with a counter in it; the tables stand in rows across the hall.
        bp = new Blueprint("Colony Canteen", "The penal colony's canteen: a tall mess hall, a servery, and the kitchen and its stack behind.", defs);
        Pads(bp, [(1, 0), (0, 1), (1, 1), (0, 2), (1, 2)], "terrazzo");
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
        var hallLid = Lid(bp, 0, 0, 32, 32, hallRoof);
        Slogan(bp, hallLid[2], "t0", "EAT WHAT YOU ARE GIVEN"); // (on the tall hall's front, over the street)
        // The kitchen (z 8 to 12 m): two corners at the back, the back door in the east one, 2 m of wall each side up to the hall.
        var kDoor = bp.Put("Concrete Door Corner", Cell(31, G, 47), RY180);
        var kCorner = bp.Put("Concrete Wall Corner", Cell(0, G, 47), RY90);
        var kBack = bp.Put("Concrete Wall 4 m", Cell(23, G, 47), RY180);
        var kWest = bp.Put("Concrete Wall 2 m", Cell(0, G, 39), RY90);
        var kEast = bp.Put("Concrete Wall 2 m", Cell(31, G, 32), RY270);
        Concrete(bp, [kDoor, kCorner, kBack, kWest, kEast]);
        Roof(bp, 0, 32, 32, 16, Slab1);
        Lid(bp, 0, 32, 32, 16, Slab1, front: false);
        var stack = Look(bp, bp.Put("Concrete Chimney 2.75 m", Cell(23, Slab1 + 1, 39), R0), MinWeathered, "concrete"); // (the range's stack: it smokes)
        Look(bp, bp.Hang("Concrete Chimney 2.75 m", stack, "t00", "b00"), MinSoot, "soot");
        Door(bp, rc[0]);
        Door(bp, kDoor, "Steel Flush Door 1.5x2.25 m", MinSlate);
        Porch(bp, rc[0], "BLOCK 1 - CANTEEN");
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

        // Colony Infirmary (Block 3): long and low, 4 by 8 m, one ward down its length. The weather side (east) is a blank wall
        // on three buttresses; the west has a high ribbon over the beds, so the sick get daylight and no draught. The door is in
        // the front's west half: triage is the medical station just inside it, on the east side, so the first thing anyone sees
        // is the nurse's couch. The beds stand along the west wall under the ribbon, the aisle runs down the east side of them,
        // and the medicine cabinet and the patients' files are on the blank wall at the far end, furthest from the door.
        bp = new Blueprint("Colony Infirmary", "The penal colony's infirmary: triage at the door, a ward of iron beds and the medicine cabinet.", defs);
        Pads(bp, [(0, 1)], "terrazzo");
        rc = RoomCorners(bp, "Concrete", 0, G, 0, 16, 32, door: 0);
        west = bp.Put("Concrete Ribbon Wall 4 m", Cell(0, G, 23), RY90);
        east = bp.Put("Concrete Wall 4 m", Cell(15, G, 8), RY270);
        Concrete(bp, rc.Concat([west, east]), MinWhite);
        Roof(bp, 0, 0, 16, 32, Slab1);
        Lid(bp, 0, 0, 16, 32, Slab1);
        Look(bp, bp.Put("Roof Water Tank", [1.75, (Slab1 + 1) * C, 5.75], R0), MinWhite, "worn paint"); // (the ward's water, on legs over the far end)
        Door(bp, rc[0]);
        Porch(bp, rc[0], "BLOCK 3 - INFIRMARY");
        Steps(bp, 0, 0);
        Piers(bp, ButtressPart, (east, "f0_5"), (east, "f8_5"), (east, "f15_5"));
        Piers(bp, FinPart, (west, "f0_5"), (west, "f8_5"));
        Notice(bp, west, "k15_5", "FORM 27B: NOTICE OF INTENT TO BE ILL");
        Warning(bp, rc[1], "kb7_5", "NO MALINGERING");
        Look(bp, bp.Put("Medical Station", OnPad(0, 0, 2.75, 2.75), R0), MinWhite); // (triage: by the door, its foot to the ward)
        foreach (var sock in new[] { "k8_0", "k0_0" }) // (the ward: z 3 to 7 m, under the ribbon)
            Look(bp, bp.Hang("Iron Bed", west, sock, "wall"), MinWhite, "worn paint");
        Look(bp, bp.Hang("Medicine Cabinet", east, "k8_5", "back"), MinWhite);
        Look(bp, bp.Hang("Filing Cabinet", east, "k15_0", "wall"), MinWhite);
        bp.Hang("Cast Iron Radiator", rc[2], "ka7_0", "back");
        bp.Hang("Wall Clock", west, "k8_5", "back");
        Switch(bp, rc[0], "kb7_5", Strip(bp, west, "k0_10"), Strip(bp, west, "k15_10"));
        Look(bp, bp.Hang("Battery", east, "k0_10", "-z"), MinSlate);
        o.Add(bp);

        // Requisitions Store (Block 4): a store is a dock and a counter. The roof runs 4 m out over a loading dock at lorry-bed
        // height (the pads), on two columns, with steps up one end and the crates waiting on the other. The goods door (the
        // garage opening, x 2 to 6 m) stands open with nothing in it: inside, the issue counter runs across the store 1.3 m in
        // from the front (z 1.45 to 2.05 m), so whoever comes in for a form stands in the strip in front of it; the storekeeper's
        // way round is the gap at its east end, marked staff only. Behind it the shelving stands against the back wall with an
        // aisle in front of it, the files against the west wall. Slots, not windows: light, and nothing passed out through them.
        bp = new Blueprint("Requisitions Store", "The Ministry's requisitions store: a covered loading dock, an issue counter across the store and the shelving behind it.", defs);
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
        NameBoard(bp, lid[0], "f15", "BLOCK 4 - REQUISITIONS"); // (on the lid's front, over the dock's middle)
        bp.Hang("Loading Hoist", lid[2], "f8", "back"); // (out over the dock's east end, where a lorry backs up)
        Outside(bp, front, "f0_10");
        Outside(bp, front, "f15_10");
        // The counter: three lengths from the west wall, their closed side to the door.
        var ctr = Look(bp, bp.Put("Service Counter 2 m", OnPad(0, 0, 1.75, 1.75), R0), "#6b4a2e", "worn paint");
        var ctr2 = bp.Hang("Service Counter 2 m", ctr, "+x", "-x");
        bp.Hang("Service Counter 1.5 m", ctr2, "+x", "-x"); // (the storekeeper's way round: 1.4 m at the east end)
        bp.Hang("Teapot", ctr, "top", "base");
        Notice(bp, rc[0], "ka7_5", "ALL ISSUES ON FORM R-12, IN TRIPLICATE");
        Warning(bp, rc[1], "ka7_5", "STAFF ONLY");
        // The store: shelving against the back wall, the files on the west wall, the clock between the shelves.
        foreach (var sock in new[] { "k0_0", "k15_0" })
            Look(bp, bp.Hang("Store Shelving 2 m", back, sock, "wall"), MinSlate);
        bp.Hang("Filing Cabinet", rc[3], "ka7_0", "wall");
        bp.Hang("Wall Clock", back, "k8_5", "back");
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

        // Colony Barracks (Block 5): the guards' quarters beside the penal colony, where the lockdown squad turns out from
        // (Game/GameColonyLockdown.cs). Guards sleep away from the door and leave by it fast, so it is a narrow slab two
        // storeys tall, 4 by 8 m: the guardroom below, the dormitory above. The open doorway is in the east wall at the front,
        // with no door, because the squad comes out of it without stopping, and the Wide Stair runs up the east wall from
        // just inside it to the back, so a guard comes down and is out in four strides. The guardroom's front half is the
        // duty desk (facing the doorway), the tea urn and the gun rack on the west wall; its back half, beside the stair, is the
        // guards' own: a locker, and the lore's noticeboard and crate (Story/Lore). Upstairs the iron beds stand along the
        // walls of the front half and beside the stairwell, the aisle down the middle. Fins down the east front, buttresses on
        // the west, a high ribbon each side over the beds.
        bp = new Blueprint("Colony Barracks", "The Ministry of Slop's guard barracks beside the penal colony: the guardroom, and the dormitory up a wide stair.", defs);
        Pads(bp, [(0, 1)], "lino");
        rc = RoomCorners(bp, "Concrete", 0, G, 0, 16, 32, door: 1);
        west = bp.Put("Concrete Wall 4 m", Cell(0, G, 23), RY90);
        east = bp.Put("Concrete Slot Wall 4 m", Cell(15, G, 8), RY270);
        Concrete(bp, rc.Concat([west, east]));
        Look(bp, bp.Put("Concrete Floor 4x4 m", Cell(0, Slab1, 0), R0), MinWeathered, "lino"); // the dormitory's floor
        Look(bp, bp.Put("Concrete Wide Stair Floor", Cell(15, Slab1, 31), RY180), MinWeathered, "lino");
        Look(bp, bp.Put("Concrete Wide Stair 3 m", Cell(8, G, 16), R0), MinWeathered, "concrete"); // (x 2 to 3.75 m, foot at z 4 m, head at the back)
        uc = RoomCorners(bp, "Concrete", 0, U, 0, 16, 32);
        var dormW = bp.Put("Concrete Ribbon Wall 4 m", Cell(0, U, 23), RY90);
        var dormE = bp.Put("Concrete Ribbon Wall 4 m", Cell(15, U, 8), RY270);
        Concrete(bp, uc.Concat([dormW, dormE]));
        Roof(bp, 0, 0, 16, 32, roof2);
        Lid(bp, 0, 0, 16, 32, roof2);
        bp.Put("Air Raid Siren", [2.0, (roof2 + 1) * C, 2.0], R0); // (the lockdown's call to turn out)
        Porch(bp, rc[1], "BLOCK 5 - BARRACKS");
        Look(bp, bp.Put("Concrete Entrance Steps 2 m", Cell(19, 0, 0), RY270), MinWeathered, "concrete");
        Piers(bp, FinPart, (east, "f0_5"), (east, "f8_5"), (east, "f15_5"), (dormE, "f0_5"), (dormE, "f8_5"), (dormE, "f15_5"));
        Piers(bp, ButtressPart, (west, "f0_5"), (west, "f8_5"), (west, "f15_5"));
        Warning(bp, uc[1], "fb7_5", "GUARDS ONLY. GUARDS, THIS MEANS YOU");
        Outside(bp, rc[0], "fa7_10");
        Outside(bp, rc[2], "fa7_10");
        // The guardroom. The duty desk where a guard sits doing the admin (the staff's posts: Story/Posts/PenalColony.json,
        // Game/GameStoryPosts.cs): a bolt deck as its floor (top sockets every 0.25 m, which the pad hasn't) in the front
        // west corner, the computer against the front wall and the seat behind it, the gun rack on the wall at the guard's
        // back. The tea urn stands at the desk's end with its tap to the back of the room, so whoever is at it stands out of
        // the way to the stair.
        bp.Put("Bolt Deck 1x2 m", OnPad(0, 0, 1.0, 1.0, 0.5), R0);
        bp.Put("Computer", OnPad(0, 0, 1.25, 1.25, 0.425), R0);
        bp.Put("Seat", OnPad(0, 0, 1.25, 2.0, 0.425), R0);
        bp.Put("Tea Urn", OnPad(0, 0, 1.625, 3.75), RY180);
        bp.Hang("Gun Rack", rc[0], "kb7_0", "wall");
        bp.Hang("Cast Iron Radiator", rc[0], "ka7_0", "back");
        Look(bp, bp.Hang("Locker", west, "k0_0", "wall"), MinGreen, "worn paint");
        bp.Hang("Wall Clock", east, "k0_5", "back");
        Notice(bp, west, "k8_5", "KIT CLEAN. BOOTS CLEAN. CONSCIENCE OPTIONAL.");
        Switch(bp, rc[1], "kb7_5", Strip(bp, east, "k8_10"));
        Look(bp, bp.Hang("Battery", west, "k15_10", "-z"), MinSlate);
        // The dormitory: two iron beds down the west wall and one on the east in the front half, a locker on the front wall,
        // the radiator and the light switch on the west wall beside the stair's head.
        foreach (var (on, sock) in new[] { (uc[0], "kb7_0"), (dormW, "k8_0"), (uc[1], "ka7_0") })
            Look(bp, bp.Hang("Iron Bed", on, sock, "wall"), MinGreen, "worn paint");
        Look(bp, bp.Hang("Locker", uc[0], "ka7_0", "wall"), MinGreen, "worn paint");
        bp.Hang("Cast Iron Radiator", dormW, "k0_0", "back");
        Switch(bp, dormW, "k0_5", Strip(bp, dormW, "k8_10"), Strip(bp, dormE, "k8_10"));
        o.Add(bp);
        return o;
    }
}
