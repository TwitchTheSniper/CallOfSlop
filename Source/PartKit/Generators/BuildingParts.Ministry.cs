namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The Ministry of Slop's buildings round the penal colony (docs/story/OPENING.md "The Ministry's buildings";
/// Game/GameColonyBuildings.cs places them, Story/Opening/ColonyBuildings.json says where). Built from the same walls,
/// pads and roofs as the colony, dressed in the Ministry's colours: civil-service cream walls, slate roofs and trim,
/// Ministry green doors, navy signs, and a battery in each so the mains lamps at the door and inside are lit.
/// </summary>
public sealed partial class BuildingParts
{
    // The Ministry's palette (docs/sandbox/constructs/look.md "Rules of thumb": a base, a second, an accent).
    private const string MinCream = "#c9bf9f", MinSlate = "#4b5157", MinGreen = "#2f5a3c", MinNavy = "#1f2b45", MinWhite = "#d6dad3",
        MinTin = "#5d7a5f", MinStores = "#7a6a4f";

    /// <summary>A step's paint and finish (look.md "The authoring fields").</summary>
    private static int Look(Blueprint bp, int step, string paint, string? finish = null)
    {
        bp.Steps[step]["paint"] = paint;
        if (finish != null)
            bp.Steps[step]["finish"] = finish;
        return step;
    }

    /// <summary>A Text Sign by its back on a wall's face socket (a small socket: a bigger sign needs an adapter), saying <paramref name="text"/>.</summary>
    private static int Sign(Blueprint bp, int wall, string sock, string text, string part = "Text Sign")
    {
        if (text.Length > 60 || text.Any(c => c < ' ' || c > '~'))
            throw new InvalidOperationException($"{bp.Name}: sign text \"{text}\" (60 ASCII characters at most)");
        var s = bp.Hang(part, wall, sock, "back");
        bp.Steps[s]["text"] = new List<object?> { text };
        return Look(bp, s, MinNavy);
    }

    /// <summary>A mains Wall Light on a wall's face socket (lit while the building's battery reaches it).</summary>
    private static int WallLamp(Blueprint bp, int wall, string sock) => bp.Hang("Wall Light", wall, sock, "back");

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

    private static List<Blueprint> MinistryBlueprints(Dictionary<string, PyDict> defs)
    {
        var o = new List<Blueprint>();
        var roofY = (2 + WallH) * C + 0.125; // a flat roof's top (its slab's top sockets)

        // Ministry Records Office: an 8 by 8 m concrete office with a 4 m forecourt in front (z -4 to 0) where the queue
        // forms. The door at the front left (corner 0's leg along +x, x 0.1 to 1.6 m), a fascia sign over the front wall,
        // windows down both sides; inside, a counter of three tables across the middle (z 4) with the filing behind it
        // (lockers and a parts shelf, z 6). The west wall's inside is left bare for notices.
        var bp = new Blueprint("Ministry Records Office", "The Ministry of Slop's records office: a counter, the filing and a queue outside.", defs);
        Pads(bp, [(1, 0), (0, 1), (1, 1), (0, -1), (1, -1)]);
        var rc = RoomCorners(bp, "Concrete", 0, 2, 0, 32, 32, door: 0);
        var front = bp.Put("Concrete Wall 4 m", Cell(8, 2, 0), R0);
        var back = bp.Put("Concrete Wall 4 m", Cell(23, 2, 31), RY180);
        var west = bp.Put("Concrete Window Wall 4 m", Cell(0, 2, 23), RY90);
        var east = bp.Put("Concrete Window Wall 4 m", Cell(31, 2, 8), RY270);
        foreach (var s in rc.Concat([front, back, west, east]))
            Look(bp, s, MinCream, "worn paint");
        var roof = new List<int>();
        foreach (var (x, z) in new[] { (0, 0), (16, 0), (0, 16), (16, 16) })
            roof.Add(Look(bp, bp.Put("Concrete Floor 4x4 m", Cell(x, 2 + WallH, z), R0), MinSlate, "concrete"));
        var hd = bp.Hang("Door Hinge", rc[0], "hinge", "base");
        Look(bp, bp.Hang("Wood Flush Door 1.5x2.25 m", hd, "leaf", "hang"), MinGreen, "painted wood");
        Sign(bp, front, "f8_10", "MINISTRY OF SLOP - RECORDS OFFICE");
        Sign(bp, front, "f15_5", "QUEUE HERE");
        var counter = new List<int>();
        foreach (var x in new[] { 2.0, 4.0, 6.0 }) // the counter: three tables end to end with a hand's gap
            counter.Add(Look(bp, bp.Put("Mess Table", [x, 0.375, 4.0], RY180), MinSlate, "wood"));
        bp.Hang("Employee of the Month", counter[1], "top", "base");
        bp.Hang("Teapot", counter[2], "top", "base"); // (the clerk's)
        Look(bp, bp.Put("Locker", OnPad(0, 1, 1.75, 2.0), RY180), MinGreen);
        Look(bp, bp.Put("Parts Shelf", OnPad(0, 1, 3.75, 2.0), RY180), MinSlate);
        Look(bp, bp.Put("Locker", OnPad(1, 1, 1.75, 2.0), RY180), MinGreen);
        bp.Put("Office Plant", OnPad(1, 0, 1.75, 1.75), R0);
        foreach (var x in new[] { 0.0, 1.75, 3.75, 4.0 }) // the queue posts along the forecourt, from the door
            bp.Put("Queue Post", OnPad(0, -1, x, 1.75), R0);
        bp.Put("Pillar Box", OnPad(1, -1, 2.125, 1.75), R0);
        bp.Put("Union Flag", [6.0, roofY, 6.0], R0);
        Look(bp, bp.Hang("Battery", back, "k0_10", "-z"), MinSlate); // (the fuse box, high on the back wall)
        WallLamp(bp, rc[0], "fa7_10"); // over the door
        WallLamp(bp, west, "k8_10");
        WallLamp(bp, east, "k8_10");
        o.Add(bp);

        // Colony Canteen: an 8 by 8 m corrugated hall in Ministry green with four lean-to slopes for a roof, an open
        // doorway at the front left, two rows of mess tables (z 1.75 and 3.75) with a seat between each pair, and the
        // serving line across the back (z 6: the tea urn and two stoves). The menu is over the front wall.
        bp = new Blueprint("Colony Canteen", "The penal colony's canteen: mess tables, a serving line and the tea urn.", defs);
        Pads(bp, [(1, 0), (0, 1), (1, 1)]);
        rc = RoomCorners(bp, "Corrugated", 0, 2, 0, 32, 32, door: 0);
        front = bp.Put("Corrugated Wall 4 m", Cell(8, 2, 0), R0);
        back = bp.Put("Corrugated Wall 4 m", Cell(23, 2, 31), RY180);
        west = bp.Put("Corrugated Window Wall 4 m", Cell(0, 2, 23), RY90);
        east = bp.Put("Corrugated Window Wall 4 m", Cell(31, 2, 8), RY270);
        foreach (var s in rc.Concat([front, back, west, east]))
            Look(bp, s, MinTin, "rusty paint");
        foreach (var (x, z) in new[] { (0, 0), (16, 0), (0, 16), (16, 16) })
            Look(bp, bp.Put("Corrugated Roof Slope 4x4 m", Cell(x, 2 + WallH, z), R0), MinSlate);
        Sign(bp, front, "f8_10", "CANTEEN - GRUEL 12:00 TO 12:05");
        Sign(bp, back, "k8_5", "SECOND HELPINGS BY WRITTEN REQUEST ONLY");
        foreach (var z in new[] { 1.75, 3.75 })
        {
            foreach (var x in new[] { 2.0, 6.0 })
                Look(bp, bp.Put("Mess Table", [x, 0.375, z], R0), MinSlate, "wood");
            bp.Put("Seat", [4.0, 0.375, z], RY90);
        }
        bp.Put("Tea Urn", OnPad(0, 1, 1.875, 2.0), RY180);
        Look(bp, bp.Put("Galley Stove", OnPad(1, 1, 0.0, 2.0), RY180), MinSlate);
        Look(bp, bp.Put("Galley Stove", OnPad(1, 1, 2.0, 2.0), RY180), MinSlate);
        Look(bp, bp.Hang("Battery", back, "k0_10", "-z"), MinSlate);
        WallLamp(bp, rc[0], "fa7_10");
        WallLamp(bp, west, "k8_10");
        WallLamp(bp, east, "k8_10");
        o.Add(bp);

        // Colony Infirmary: a 4 by 8 m concrete block in hospital white, its door at the front left with the sign over it;
        // inside, the medical station and two bunks across the room down its length and the medicine cabinet on the
        // east wall.
        bp = new Blueprint("Colony Infirmary", "The penal colony's infirmary: a medical station, two bunks and the medicine cabinet.", defs);
        Pads(bp, [(0, 1)]);
        rc = RoomCorners(bp, "Concrete", 0, 2, 0, 16, 32, door: 0);
        west = bp.Put("Concrete Window Wall 4 m", Cell(0, 2, 23), RY90);
        east = bp.Put("Concrete Wall 4 m", Cell(15, 2, 8), RY270);
        foreach (var s in rc.Concat([west, east]))
            Look(bp, s, MinWhite, "worn paint");
        foreach (var z in new[] { 0, 16 })
            Look(bp, bp.Put("Concrete Floor 4x4 m", Cell(0, 2 + WallH, z), R0), MinSlate, "concrete");
        hd = bp.Hang("Door Hinge", rc[0], "hinge", "base");
        Look(bp, bp.Hang("Wood Flush Door 1.5x2.25 m", hd, "leaf", "hang"), MinGreen, "painted wood");
        Sign(bp, rc[1], "fb7_10", "INFIRMARY - AILMENTS BY APPOINTMENT");
        Sign(bp, east, "k15_5", "FORM 27B: NOTICE OF INTENT TO BE ILL");
        Look(bp, bp.Put("Medical Station", OnPad(0, 0, 2.0, 2.0), RY90), MinWhite);
        bp.Put("Bunk", OnPad(0, 1, 2.0, 0.0), RY90);
        bp.Put("Bunk", OnPad(0, 1, 2.0, 1.75), RY90);
        Look(bp, bp.Hang("Medicine Cabinet", east, "k8_5", "back"), MinWhite);
        Look(bp, bp.Hang("Battery", east, "k0_10", "-z"), MinSlate);
        WallLamp(bp, rc[0], "fa0_10");
        WallLamp(bp, west, "k8_10");
        o.Add(bp);

        // Requisitions Store: an 8 by 4 m corrugated store in stores brown, an open bay (a garage wall with no door) across
        // the front, two shelves and the crates inside, the form number on the back wall.
        bp = new Blueprint("Requisitions Store", "The Ministry's requisitions store: shelves and crates behind an open bay.", defs);
        Pads(bp, [(1, 0)]);
        rc = RoomCorners(bp, "Corrugated", 0, 2, 0, 32, 16);
        front = bp.Put("Corrugated Garage Wall 4 m", Cell(8, 2, 0), R0);
        back = bp.Put("Corrugated Wall 4 m", Cell(23, 2, 15), RY180);
        foreach (var s in rc.Concat([front, back]))
            Look(bp, s, MinStores, "worn paint");
        foreach (var x in new[] { 0, 16 })
            Look(bp, bp.Put("Corrugated Roof Slope 4x4 m", Cell(x, 2 + WallH, 0), R0), MinSlate);
        Sign(bp, front, "f8_10", "REQUISITIONS");
        Sign(bp, back, "k8_5", "ALL ISSUES ON FORM R-12, IN TRIPLICATE");
        Look(bp, bp.Put("Parts Shelf", OnPad(0, 0, 2.0, 2.0), RY180), MinSlate);
        Look(bp, bp.Put("Parts Shelf", OnPad(1, 0, 2.0, 2.0), RY180), MinSlate);
        var crate = bp.Hang("Requisition Crate", 0, "t15_7", "-y"); // (three crates in a huddle mid-floor)
        bp.Hang("Requisition Crate", crate, "+x", "-x");
        bp.Hang("Requisition Crate", crate, "-z", "+z");
        Look(bp, bp.Hang("Battery", back, "k0_10", "-z"), MinSlate);
        WallLamp(bp, front, "k8_10");
        WallLamp(bp, rc[0], "fa7_10");
        o.Add(bp);

        // Colony Gatehouse: a 4 by 4 m concrete hut on a pad with a 4 m step in front, the gate clerk's terminal inside
        // facing the front, a Belisha beacon and the phone box out front, and the signs a visitor reads first.
        bp = new Blueprint("Colony Gatehouse", "The penal colony's gatehouse: the gate clerk's hut and the signs at the gate.", defs);
        Pads(bp, [(0, -1)]);
        rc = RoomCorners(bp, "Concrete", 0, 2, 0, 16, 16, door: 0, window: 2);
        foreach (var s in rc)
            Look(bp, s, MinCream, "worn paint");
        Look(bp, bp.Put("Concrete Floor 4x4 m", Cell(0, 2 + WallH, 0), R0), MinSlate, "concrete");
        hd = bp.Hang("Door Hinge", rc[0], "hinge", "base");
        Look(bp, bp.Hang("Wood Flush Door 1.5x2.25 m", hd, "leaf", "hang"), MinGreen, "painted wood");
        Sign(bp, rc[1], "fb7_10", "PENAL COLONY 7 - ALL VISITORS REPORT HERE");
        Sign(bp, rc[3], "fa7_5", "ESCAPEES ALSO REPORT HERE");
        bp.Put("Computer", [1.5, 0.375, 2.0], RY180);
        bp.Put("Belisha Beacon", OnPad(0, -1, 3.75, 1.75), R0);
        bp.Put("Phone Box", OnPad(0, -1, 2.125, 1.75), R0);
        Look(bp, bp.Hang("Battery", rc[1], "kb7_10", "-z"), MinSlate);
        WallLamp(bp, rc[0], "fa0_10");
        o.Add(bp);
        return o;
    }
}
