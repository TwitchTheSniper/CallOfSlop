namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The Ministry of Slop's spaceport on Glacier, 2 km from the penal colony (docs/story/SPACEPORT.md; Game/GameSpaceport.cs
/// places the pieces, Story/Opening/Spaceport.json says where), in the Ministry's style (docs/sandbox/constructs/buildings.md).
/// A spaceport is a terminal, a tower and an apron, so it is three kinds of piece: the arrivals and customs hall, low and
/// wide, its roof a landing deck reached by a stair inside; the control tower, a blind concrete shaft seven storeys high
/// with a glass cab on top (the one place the Ministry looks out) and a pad for small craft on its roof; and the landing
/// pads on the apron, laid flush with the ground. Three heights to land at: the ground, the hall's roof (3.5 m) and the
/// tower's (21.5 m), with red lamps on the tower's corners and amber ones round every deck. The Ministry Courier, the small
/// ship parked on the first pad, is the Orbiter in the Ministry's colours.
/// </summary>
public sealed partial class BuildingParts
{
    private const string Beacon = "#ff2a1a"; // the tower's obstruction lamps
    private const string PadYellow = "#b8902c"; // the pads' touchdown squares, worn

    /// <summary>A room's four corners, each a wall or a door corner (<paramref name="doors"/>: which of 0..3), as <see cref="RoomCorners"/> numbers them.</summary>
    private static List<int> Corners(Blueprint bp, int x0, int y, int z0, int nx, int nz, params int[] doors)
    {
        int[] rots = [R0, RY270, RY180, RY90];
        (int, int)[] spots = [(x0, z0), (x0 + nx - 1, z0), (x0 + nx - 1, z0 + nz - 1), (x0, z0 + nz - 1)];
        var o = new List<int>();
        for (var k = 0; k < 4; k++)
            o.Add(bp.Put(doors.Contains(k) ? "Concrete Door Corner" : "Concrete Wall Corner", Cell(spots[k].Item1, y, spots[k].Item2), rots[k]));
        return o;
    }

    /// <summary>The 4 m walls between a room's corners (x0, z0 its min cell, nx by nz cells, each side a whole number of
    /// 4 m walls plus the corners' 2 m legs), each side's part by <paramref name="part"/> (0 front -z, 1 east +x, 2 back +z, 3 west -x).</summary>
    private static List<int>[] Sides(Blueprint bp, int x0, int y, int z0, int nx, int nz, Func<int, int, string> part)
    {
        var o = new[] { new List<int>(), new List<int>(), new List<int>(), new List<int>() };
        for (int x = x0 + 8, k = 0; x < x0 + nx - 8; x += 16, k++)
        {
            o[0].Add(bp.Put(part(0, k), Cell(x, y, z0), R0));
            o[2].Add(bp.Put(part(2, k), Cell(x + 15, y, z0 + nz - 1), RY180));
        }
        for (int z = z0 + 8, k = 0; z < z0 + nz - 8; z += 16, k++)
        {
            o[3].Add(bp.Put(part(3, k), Cell(x0, y, z + 15), RY90));
            o[1].Add(bp.Put(part(1, k), Cell(x0 + nx - 1, y, z), RY270));
        }
        return o;
    }

    private static List<Blueprint> SpaceportBlueprints(Dictionary<string, PyDict> defs)
    {
        var o = new List<Blueprint>();
        const string Wall = "Concrete Wall 4 m", Slot = "Concrete Slot Wall 4 m", Ribbon = "Concrete Ribbon Wall 4 m";
        const string Floor = "Concrete Floor 4x4 m", StairFloor = "Concrete Wide Stair Floor", Stair = "Concrete Wide Stair 3 m";

        // Spaceport Terminal: arrivals and departures are one hall, 16 by 12 m, split across the middle by the customs
        // counter: the landside half at the front (the door from the road under its canopy, the queue, the benches, the
        // phone), the airside half behind (the departures desk's filing and teleprinter, the cargo, the door out to the
        // apron). Nobody sees out (slots in front, a high ribbon down each side over the queue); the roof is the second
        // landing deck, flat inside its lid, reached by the stair up the west wall from the airside half.
        var bp = new Blueprint("Spaceport Terminal", "The Ministry of Slop spaceport's arrivals and customs hall; its roof is a landing deck.", defs);
        Pads(bp, [(1, 0), (2, 0), (3, 0), (0, 1), (1, 1), (2, 1), (3, 1), (0, 2), (1, 2), (2, 2), (3, 2)]);
        var rc = Corners(bp, 0, G, 0, 64, 48, 0, 2);
        var sides = Sides(bp, 0, G, 0, 64, 48, (s, k) => s switch { 0 => Slot, 1 or 3 => Ribbon, _ => Wall });
        Concrete(bp, rc.Concat(sides.SelectMany(s => s)));
        // The roof: the stair's opening over the west side's middle (the stair rises to -z along the west wall, from the
        // airside half up over the landside one), slabs on the rest.
        Look(bp, bp.Put(StairFloor, Cell(0, Slab1, 16), R0), MinWeathered, "concrete");
        for (var z = 0; z < 48; z += 16)
            for (var x = 0; x < 64; x += 16)
                if (x != 0 || z != 16)
                    Look(bp, bp.Put(Floor, Cell(x, Slab1, z), R0), MinWeathered, "concrete");
        Look(bp, bp.Put(Stair, Cell(7, G, 31), RY180), MinWeathered, "concrete"); // (x 0.25 to 2 m, foot at z 8 m)
        var lid = Lid(bp, 0, 0, 64, 48, Slab1);
        Door(bp, rc[0], "Steel Flush Door 1.5x2.25 m", MinGreen);
        Door(bp, rc[2], "Steel Flush Door 1.5x2.25 m", MinSlate);
        Porch(bp, rc[0], "ARRIVALS AND DEPARTURES");
        Porch(bp, rc[2], "AIRSIDE");
        Steps(bp, 0, 0);
        Look(bp, bp.Put("Concrete Entrance Steps 2 m", Cell(63, 0, 51), RY180), MinWeathered, "concrete");
        Piers(bp, FinPart, sides[0].SelectMany(w => new[] { (w, "f0_5"), (w, "f8_5") }).ToArray());
        Piers(bp, ButtressPart, sides[1].SelectMany(w => new[] { (w, "f0_5"), (w, "f8_5") }).ToArray());
        // The deck's edge: amber lamps on the lid's outside faces, one each side, for a pilot coming in out of the snow.
        foreach (var k in new[] { 2, 5, 10, 11 }) // (front, back, west, east)
            Outside(bp, lid[k], "f7");
        // The landside half: the queue posts to the counter, benches along the east wall, the phone and the clock.
        foreach (var x in new[] { 5.75, 7.75, 9.75 })
            bp.Put("Queue Post", OnPad(x < 8 ? 1 : 2, 0, x < 8 ? x - 4 : x - 8, 3.75), R0);
        foreach (var sock in new[] { "k0_0", "k15_0" }) // (facing west, at the queue, the radiator between them)
            bp.Hang("Waiting Bench 2 m", sides[1][0], sock, "wall");
        bp.Put("Phone Box", OnPad(0, 0, 2.125, 3.75), R0);
        // The customs counter across the middle (z 6 m), x 6.75 to 10.75 m, its closed side to the queue, with a way past
        // each end for those cleared.
        var counter = Look(bp, bp.Put("Service Counter 2 m", OnPad(1, 1, 3.75, 2.0), R0), "#6b4a2e", "worn paint");
        var counter2 = Look(bp, bp.Hang("Service Counter 2 m", counter, "+x", "-x"), "#6b4a2e", "worn paint");
        bp.Hang("Employee of the Month", counter, "top", "base");
        bp.Hang("Teapot", counter2, "top", "base"); // (the customs officer's)
        // The airside half: the departures desk's paper on the back wall, the cargo by the door out.
        foreach (var s in new[] { "k0_0", "k15_0" })
            Look(bp, bp.Hang("Filing Cabinet", sides[2][1], s, "wall"), MinGreen, "worn paint");
        bp.Hang("Teleprinter", sides[2][0], "k8_0", "wall");
        var crate = bp.Hang("Requisition Crate", 10, "t8_8", "-y"); // (pad 2, 2: on the way to the airside door)
        bp.Hang("Requisition Crate", crate, "-x", "+x");
        bp.Hang("Requisition Crate", crate, "-z", "+z");
        bp.Hang("Cast Iron Radiator", sides[3][1], "k0_0", "back"); // (behind the stair's foot)
        bp.Hang("Cast Iron Radiator", sides[1][0], "k8_0", "back");
        bp.Hang("Wall Clock", sides[0][1], "k8_5", "back");
        Sign(bp, sides[0][0], "k8_5", "CUSTOMS: DECLARE EVERYTHING. THEN DECLARE IT AGAIN");
        Warning(bp, sides[2][2], "k8_5", "DEPARTURES: NONE WITHOUT FORM 88");
        Sign(bp, sides[0][2], "k8_5", "THIS QUEUE IS NOT MOVING. NEITHER ARE YOU");
        // The strip lights down each side, each side's switch on its own wall by the front (a wire reaches 8 m).
        Switch(bp, sides[3][0], "k15_5", Strip(bp, sides[3][0], "k8_10"), Strip(bp, sides[3][1], "k0_10"));
        Switch(bp, sides[1][0], "k0_5", Strip(bp, sides[1][0], "k8_10"), Strip(bp, sides[1][1], "k0_10"));
        Look(bp, bp.Hang("Battery", sides[2][1], "k8_10", "-z"), MinSlate);
        o.Add(bp);

        // Spaceport Control Tower: the controllers must see every pad and be seen from far off in the weather, so it is
        // the tall one, a blind shaft 8 by 8 m and seven storeys high (slots to light the stair, nothing else) with the
        // cab on top glazed all round, and a pad for small craft on its roof inside the lid, red lamps on the lid's
        // corners. The stair goes up the shaft in alternate flights, east then west, each under its own floor's opening,
        // so every flight has a storey of headroom; the last flight goes up from the cab through the roof's hatch.
        bp = new Blueprint("Spaceport Control Tower", "The Ministry of Slop spaceport's control tower: a concrete shaft, a glass cab and a pad for small craft on its roof.", defs);
        Pads(bp, [(1, 0), (0, 1), (1, 1)]);
        const int Floors = 7;
        List<int> first = [], tc = [];
        for (var k = 0; k < Floors; k++)
        {
            var y = G + k * Storey;
            var top = k == Floors - 1;
            tc = Corners(bp, 0, y, 0, 32, 32, k == 0 ? [0] : []);
            var walls = Sides(bp, 0, y, 0, 32, 32, (s, _) => top ? "Concrete Window Wall 4 m" : s is 0 or 3 ? Slot : Wall);
            Concrete(bp, tc.Concat(walls.SelectMany(w => w)));
            if (k == 0)
                first = tc;
            if (k == 3)
                NameBoard(bp, walls[0][0], "f8_5", "MINISTRY OF SLOP - SPACEPORT");
            // This storey's flight: east, rising to +z under an opening in the floor over it at the back-east slab (even
            // storeys: the ground floor's, so the door in the front's west half opens onto the floor, not a flight's head),
            // or west, rising to -z under one at the front-west slab (odd ones).
            var west = k % 2 == 1;
            var slab = y + WallH;
            Look(bp, west ? bp.Put(StairFloor, Cell(0, slab, 0), R0) : bp.Put(StairFloor, Cell(31, slab, 31), RY180), MinWeathered, "concrete");
            foreach (var (x, z) in new[] { (0, 0), (16, 0), (0, 16), (16, 16) })
                if (west ? x != 0 || z != 0 : x != 16 || z != 16)
                    Look(bp, bp.Put(Floor, Cell(x, slab, z), R0), MinWeathered, "concrete");
            Look(bp, west ? bp.Put(Stair, Cell(7, y, 15), RY180) : bp.Put(Stair, Cell(24, y, 16), R0), MinWeathered, "concrete");
        }
        var roofRow = G + (Floors - 1) * Storey + WallH;
        var tlid = Lid(bp, 0, 0, 32, 32, roofRow);
        foreach (var k in new[] { 8, 9, 10, 11 }) // (the lid's four corners: red on top, for anything flying)
            Lamp(bp, "Bulkhead Light", tlid[k], "t", Beacon);
        Door(bp, first[0], "Steel Flush Door 1.5x2.25 m", MinSlate);
        Porch(bp, first[0], "CONTROL TOWER - NO ADMITTANCE");
        Steps(bp, 0, 0);
        // The cab: the controller's desk at the front glass (the seat behind it, facing out), the clock, the strip lights
        // and their switch, and the fuse box that also feeds the beacons.
        var cabFloor = (G + (Floors - 2) * Storey + WallH + 0.5) * C;
        bp.Put("Computer", [4.0, cabFloor, 2.0], R0); // (in the middle, clear of the stair's head and of the last flight's foot)
        bp.Put("Seat", [4.0, cabFloor, 3.75], R0);
        bp.Hang("Wall Clock", tc[3], "ka7_5", "back");
        Switch(bp, tc[0], "ka7_5", Strip(bp, tc[1], "ka7_10"), Strip(bp, tc[3], "ka7_10"));
        Look(bp, bp.Hang("Battery", tc[1], "kb7_10", "-z"), MinSlate);
        o.Add(bp);

        // Spaceport Radar Mast: the tower's blueprint is at its step limit, so what gives the port its Cold War outline
        // stands beside it as its own piece: a 4 m lattice tower 32 m high with the approach radar's white cabin on its
        // head and a red obstruction lamp, the tallest thing on the field, seen from the colony road before anything else.
        bp = new Blueprint("Spaceport Radar Mast", "The Ministry of Slop spaceport's approach radar: a 32 m lattice tower with the radar cabin on its head.", defs);
        Pads(bp, []);
        Look(bp, 0, MinWeathered, "concrete");
        var plate = Look(bp, bp.Hang("Adapter Large to Small", 0, "t8_8", "front2.2"), MinSlate, "painted steel"); // (a pad's sockets are small, the tower's large)
        var leg = Look(bp, bp.Hang("Large Truss 16 m", plate, "back", "-z"), MinSlate, "painted steel");
        leg = Look(bp, bp.Hang("Large Truss 16 m", leg, "+z", "-z"), MinSlate, "painted steel");
        Look(bp, bp.Hang("Floodlight Large", leg, "+z", "-y"), "#e2e4e0", "painted plating");
        Look(bp, bp.Hang("Battery", 0, "l8", "-z"), MinSlate);
        o.Add(bp);

        // Spaceport Landing Pad: 16 by 16 m of foundation laid flush with the ground (the site sinks it), a worn yellow
        // touchdown square of painted bays in the middle, an amber lamp on each corner and the fuse box under the slab.
        bp = new Blueprint("Spaceport Landing Pad", "A Ministry of Slop landing pad: 16 by 16 m of concrete, a touchdown square and corner lamps.", defs);
        bp.First("Concrete Foundation 4x4 m");
        for (var j = 0; j < 4; j++)
            for (var i = 0; i < 4; i++)
                if (i != 0 || j != 0)
                    bp.Put("Concrete Foundation 4x4 m", Cell(16 * i, 0, 16 * j), R0);
        for (var k = 0; k < 16; k++)
            Look(bp, k, MinWeathered, "concrete");
        // The touchdown square: four painted bays on the middle slabs, each pulled a cell towards the pad's centre (Bay
        // Marking stands 4 mm off the slab, so no shared plane).
        foreach (var (pad, sock) in new[] { (5, "t8_8"), (6, "t7_8"), (9, "t8_7"), (10, "t7_7") })
            Look(bp, bp.Hang("Bay Marking", pad, sock, "bottom"), PadYellow, "hazard paint");
        foreach (var (pad, sock) in new[] { (0, "t0_0"), (3, "t15_0"), (12, "t0_15"), (15, "t15_15") })
            Outside(bp, pad, sock);
        Look(bp, bp.Hang("Battery", 0, "l8", "-z"), MinSlate);
        o.Add(bp);
        return o;
    }
}
