using System.Numerics;

namespace CallOfSlop.VehicleKit;

/// <summary>A room a player walks into: what <see cref="Vehicle.Room"/> built, for the furniture and for the check.</summary>
public sealed class Room
{
    public string Tag = "";
    public Vehicle.Rect R;
    public float FloorTop, Ceiling;
    public Vector3 DoorFace; // the way out
    public Vector3 DoorMid; // the doorway's middle, at floor height
    public VPart? Door, Hinge;
    public List<VPart> Walls = new();
    public List<VPart> Roof = new();
    public List<VPart> Containers = new();
    public List<VPart> Seats = new();
    public List<VPart> Furniture = new();
    public List<VPart> Bays = new();

    public Vector3 FloorRest; // vehicle coordinates to the construct's rest frame
    public bool Ladder;

    public Vector3 Middle => new((R.X0 + R.X1) * 0.5f, FloorTop, (R.Z0 + R.Z1) * 0.5f);
}

public sealed partial class Vehicle
{
    public const float DoorWidth = 1.25f, DoorHeight = 2.35f; // the opening: floor to the wall's top (tools/constructs/door_parts.py names its door for this)
    private const string DoorPart = "Steel Flush Door 1.25x2.35 m", HingePart = "Long Door Hinge";
    private const float DoorPlane = 0.025f; // the door's middle, out from the room's edge: clear of the floor's and the roof's edges, a little inside a plate's outer face
    private const float HingeWidth = 0.02f;

    /// <summary>The rooms built, for the check.</summary>
    public List<Room> Rooms { get; } = new();

    /// <summary>
    /// A room: a floor at <paramref name="under"/> (sills below it), 2.5 m walls of the style's "wall" plates round <paramref name="r"/>, a roof, and a flush door on a Long Door Hinge in the wall facing <paramref name="doorFace"/>,
    /// its doorway from <paramref name="doorFrom"/> along that wall, with a Boarding Ladder under it. <paramref name="seatSpots"/> are 1 m floor plates (x0, z0), a seat on the corner nearest x0; <paramref name="mounts"/> 1 m roof plates for turrets (<see cref="Mounts"/>).
    /// Walls have no socket for a floor or a roof to lean on but their inner faces' rows, so the sizes are the grid's: keep a room's sides whole metres.
    /// </summary>
    public Room Room(string tag, Rect r, float under, Vector3 doorFace, float doorFrom, (float X, float Z)[]? seatSpots = null, (float X, float Z)[]? mounts = null, bool chamfer = false)
    {
        var room = new Room { Tag = tag, R = r, DoorFace = doorFace };
        var roofPlates = (mounts ?? []).Select(s => new Rect(s.X, s.X + 1, s.Z, s.Z + 1)).ToArray();
        Deck(tag + "floor", under, r, "floor");
        room.FloorTop = under + 0.05f;

        var sideways = MathF.Abs(doorFace.X) > 0.5f;
        var skip = (doorFrom, doorFrom + DoorWidth);
        var bottom = under - 0.12f;
        foreach (var (face, at, from, to) in new[] { (Vector3.UnitX, r.X1, r.Z0, r.Z1), (-Vector3.UnitX, r.X0, r.Z0, r.Z1), (Vector3.UnitZ, r.Z1, r.X0, r.X1), (-Vector3.UnitZ, r.Z0, r.X0, r.X1) })
        {
            var open = Vector3.Dot(face, doorFace) > 0.5f;
            Wall(tag + "walls", face, at, from, to, bottom, 2f, "wall", 0, open ? skip : null, strict: true);
            if (!chamfer || MathF.Abs(face.Z) > 0.5f || open)
                Wall(tag + "upper", face, at, from, to, bottom + 2f, 0.5f, "wall", Style.Limit("slits"), open ? skip : null);
        }
        room.Walls.AddRange(Panels[tag + "walls"]);
        room.Walls.AddRange(Panels[tag + "upper"]);

        // The roof hangs on the upper plates' inner sockets.
        var roofY = RoofLine(Panels[tag + "upper"], under + 2.1f);
        if (chamfer)
            RoofSlants(tag, r, doorFace);
        Deck(tag + "roof", roofY, chamfer ? r with { X0 = r.X0 + (doorFace.X < -0.5f ? 0 : 0.5f), X1 = r.X1 - (doorFace.X > 0.5f ? 0 : 0.5f) } : r, "sheet", smallest: 2, whole: roofPlates);
        room.Roof = Panels[tag + "roof"];
        room.Ceiling = roofY;
        Mounts[tag] = roofPlates.Select(h => Wholes[(tag + "roof", h)]).ToList();

        // The door: in the opening, in the plane of the wall. The hinge is welded to the jamb where the door needs it (a plate's edge
        // sockets sit at its back and on its own rows), and fits in the gap; its -z, where the pivot is, faces out, so the door swings out past the jamb.
        var plane = sideways ? (doorFace.X > 0 ? r.X1 : r.X0) : (doorFace.Z > 0 ? r.Z1 : r.Z0);
        var mid = doorFrom + DoorWidth * 0.5f;
        Vector3 Point(float along, float y, float out_) => sideways ? new Vector3(plane + doorFace.X * out_, y, along) : new Vector3(along, y, plane + doorFace.Z * out_);
        room.DoorMid = Point(mid, room.FloorTop, 0);
        var neighbour = Panels[tag + "walls"].Where(p => Vector3.Dot(CentreOf(p) - room.DoorMid, doorFace) > -0.2f).OrderBy(p => Vector3.Distance(CentreOf(p), room.DoorMid)).FirstOrDefault()
            ?? throw new VehicleKitException($"{Name}: no wall beside the door of the {tag}");
        var along = sideways ? CentreOf(neighbour).Z : CentreOf(neighbour).X;
        var hingeSide = along > mid ? 1f : -1f;
        var toDoor = sideways ? new Vector3(0, 0, -hingeSide) : new Vector3(-hingeSide, 0, 0);
        var hingeDef = Def(HingePart);
        var hingeTurn = new Orient(Vector3.UnitX, toDoor, Vector3.UnitZ, -doorFace);
        var midY = room.FloorTop + DoorHeight * 0.5f;
        var hingeAt = Point(mid + hingeSide * (DoorWidth - HingeWidth) * 0.5f, midY, DoorPlane);
        room.Hinge = Weld(HingePart, neighbour, hingeAt + Rot24.Apply(FirstTurn(hingeTurn), (hingeDef.BoundsMin + hingeDef.BoundsMax) * 0.5f), hingeTurn, 0, 0, 0);
        room.Door = Onto(room.Hinge, "leaf", DoorPart, "hang", 0); // (by its socket: a welded part's own frame isn't the body's)
        Paint(room.Door, Style.Colour(Dice.Fork("door " + tag), "rust"));
        room.Ladder = Boarding(Point(mid, room.FloorTop, 0.1f), tag + "walls");
        room.FloorRest = ToRest(Vector3.Zero);
        Belt(room, tag);

        if (Skirted)
            Skirts(r);
        foreach (var (x, z) in seatSpots ?? [])
            room.Seats.Add(Put("Seat", new Vector3(x, room.FloorTop + 0.475f, z), Orient.Forward, 0.2f, null, "bottom", onTop: true));
        Rooms.Add(room);
        return room;
    }

    /// <summary>The style's ladder hanging from whatever edge is nearest <paramref name="at"/> (its top), if something takes it; false when nothing does.</summary>
    public bool Boarding(Vector3 at, string tag)
    {
        var part = Def(Style.Part(Dice.Fork("ladder"), "ladder")); // (a missing part is a mistake; no edge to hang it on isn't)
        try
        {
            PanelList(tag).Add(Put(part.Name, at + new Vector3(0, (part.BoundsMin.Y + part.BoundsMax.Y) * 0.5f, 0), Orient.Upright, 0.3f));
            return true;
        }
        catch (VehicleKitException)
        {
            return false;
        }
    }

    /// <summary>The 1 m plates for each roof mount, by room tag.</summary>
    public Dictionary<string, List<VPart>> Mounts { get; } = new();

    /// <summary>A plate's free upward socket nearest a place.</summary>
    private Vector3 NearestUp(VPart plate, Vector3 near) => NearestFacing(plate, Vector3.UnitY, near);

    /// <summary>A plate's free socket facing <paramref name="way"/> nearest a place.</summary>
    private Vector3 NearestFacing(VPart plate, Vector3 way, Vector3 near)
    {
        var best = Vector3.Zero;
        var bestD = float.MaxValue;
        var def = C.DefOf(plate.Index);
        for (var s = 0; s < def.Sockets.Length; s++)
        {
            C.SocketAt(plate.Index, s, out var at, out var dir);
            var p = FromRest(at);
            var d = Vector3.Distance(p, near);
            if (Vector3.Dot(ConstructParts.DirVector(dir), way) > 0.9f && !C.SocketUsed(plate.Index, s) && d < bestD)
                (best, bestD) = (p, d);
        }
        return best;
    }

    /// <summary>The first socket row at or above <paramref name="y"/> on the inner faces of <paramref name="upper"/>: what a roof's edge can bolt to. The plate's edge socket sits 5 mm over its underside.</summary>
    private float RoofLine(List<VPart> upper, float y)
    {
        var best = float.MaxValue;
        foreach (var p in upper)
        {
            var def = C.DefOf(p.Index);
            for (var s = 0; s < def.Sockets.Length; s++)
            {
                if (!def.Sockets[s].Name.StartsWith("back", StringComparison.Ordinal))
                    continue;
                C.SocketAt(p.Index, s, out var at, out _);
                var row = FromRest(at).Y;
                if (row >= y && row < best)
                    best = row;
            }
        }
        return best == float.MaxValue ? throw new VehicleKitException($"{Name}: no socket row for a roof over {y:0.00}") : best - 0.005f;
    }

    /// <summary>The side walls' tops lean in to a narrower roof, one sheet on each plate: every side but a door's, which keeps its full wall over the door.</summary>
    private void RoofSlants(string tag, Rect r, Vector3 doorFace)
    {
        foreach (var w in Panels[tag + "walls"].ToList())
        {
            var c = CentreOf(w);
            var side = MathF.Abs(c.X - r.X0) < 0.1f ? -1f : MathF.Abs(c.X - r.X1) < 0.1f ? 1f : 0f;
            if (side == 0 || doorFace.X * side > 0.5f)
                continue;
            var o = new Orient(Vector3.UnitZ, new Vector3(-side, 0, 0), Vector3.UnitY, Vector3.UnitY);
            try
            {
                PanelList(tag + "slants").Add(Put("Roof Slant 1 m", new Vector3(c.X - side * 0.25f, c.Y + 1.19f, c.Z), o, 0.2f, w, "low0"));
            }
            catch (VehicleKitException)
            {
                // (a half-metre plate beside a door takes no sheet: that bit of the shoulder stays open)
            }
        }
    }

    /// <summary>
    /// A belt of sloped armour round the walls, so a room's side is a contour and not a sheet: the style's "belt" part (or "none") on each wall plate at a height the dice pick, clear of the door's swing.
    /// </summary>
    private void Belt(Room room, string tag)
    {
        var d = Dice.Fork("belt " + tag);
        var part = Style.Part(d, "belt");
        if (part == "none")
            return;
        var y = room.FloorTop + d.Pick((float[])[0.7f, 1.2f, 1.5f]);
        var gappy = d.Chance(0.3);
        foreach (var wall in Panels[tag + "walls"].ToList())
        {
            var c = CentreOf(wall);
            var face = MathF.Abs(c.X - room.R.X0) < 0.1f ? -Vector3.UnitX : MathF.Abs(c.X - room.R.X1) < 0.1f ? Vector3.UnitX : c.Z < room.Middle.Z ? -Vector3.UnitZ : Vector3.UnitZ;
            if (Vector3.Distance(c with { Y = 0 }, room.DoorMid with { Y = 0 }) < 1.4f || wall.Def.Name.Contains("0.5", StringComparison.Ordinal) || face.Z > 0.5f || gappy && d.Chance(0.3))
                continue;
            try
            {
                PanelList("belts").Add(Stand(part, NearestFacing(wall, face, new Vector3(c.X, y, c.Z)), new Orient(Vector3.UnitZ, -face, Vector3.UnitY, Vector3.UnitY), wall, "back0", 0.06f));
            }
            catch (VehicleKitException)
            {
                // (a ladder or a cab's roof there: a gap in the belt)
            }
        }
    }

    /// <summary>Whether a footprint stands in the way in through the door: the door's width, a metre into the room.</summary>
    private static bool InDoorway(Room room, float x, float z, float deep, float run)
    {
        var mid = room.DoorMid - room.DoorFace * 0.5f;
        var (halfX, halfZ) = MathF.Abs(room.DoorFace.X) > 0.5f ? (0.55f, DoorWidth / 2 + 0.1f) : (DoorWidth / 2 + 0.1f, 0.55f);
        return MathF.Abs(x - mid.X) < halfX + deep / 2 && MathF.Abs(z - mid.Z) < halfZ + run / 2;
    }

    /// <summary>
    /// The room's furniture, standing on its floor: a layout from the style ("layout" picks one, "layout NAME" lists its slots), down the side walls from the end away from the door.
    /// What has slots rolls from the loot table at each spawn. What won't fit (a seat, a door) is skipped.
    /// </summary>
    public Vehicle Furnish(Room room)
    {
        var d = Dice.Fork("furnish " + room.Tag);
        var layout = Style.Part(d, "layout");
        var (length, width) = (room.R.Z1 - room.R.Z0, room.R.X1 - room.R.X0);
        var slots = d.Shuffled(Style.Choices("layout " + layout)).ToList();
        if (length >= 3) // (a bigger room fills up: the list again, then once more for a bus)
            slots.AddRange(d.Shuffled(slots.Take(4)));
        if (length >= 5)
            slots.AddRange(d.Shuffled(slots.Take(4)));
        const float Aisle = 0.9f; // (a player is 0.8 m across)
        var from = room.DoorMid.Z > room.Middle.Z ? room.R.Z0 : room.R.Z1; // (from the end away from the door)
        var way = room.DoorMid.Z > room.Middle.Z ? 1f : -1f;
        var taken = new List<(float Side, float Z, float Run, float Deep)>();
        var first = d.Chance(0.5) ? -1f : 1f;
        foreach (var slot in slots.Append("chest")) // (a room with nothing to loot gets a chest: lazily, so this reads what the list above placed)
        {
            if (slot == "chest" && room.Containers.Count > 0)
                continue;
            var part = Style.Part(d, slot);
            var def = Def(part);
            var size = def.BoundsMax - def.BoundsMin;
            var alongWall = size.X >= size.Z;
            var (run, deep) = alongWall ? (size.X, size.Z) : (size.Z, size.X);
            VPart? placed = null;
            foreach (var side in new[] { first, -first })
            {
                var inward = new Vector3(-side, 0, 0);
                var o = alongWall ? new Orient(Vector3.UnitZ, inward, Vector3.UnitY, Vector3.UnitY) : new Orient(Vector3.UnitX, inward, Vector3.UnitY, Vector3.UnitY);
                var x = (side < 0 ? room.R.X0 : room.R.X1) + inward.X * (deep / 2 + 0.03f); // (the floor's sockets may move it in by an eighth)
                for (var at = 0.1f + run / 2; placed == null && at + run / 2 < length - 0.1f; at += 0.25f)
                {
                    var z = from + way * at;
                    if (InDoorway(room, x, z, deep, run) || MathF.Abs(z - room.Middle.Z) < run / 2 + 0.55f && MathF.Abs(x) - deep / 2 - 0.13f < CallOfSlop.World.PlayerRadius + 0.05f ||
                        taken.Any(t => MathF.Abs(t.Z - z) < (t.Run + run) / 2 + 0.05f && (t.Side == side || t.Deep + deep + 0.2f > width - Aisle)))
                        continue;
                    try
                    {
                        placed = Put(part, new Vector3(x, room.FloorTop + size.Y / 2, z), o, 0.2f, onTop: true);
                        taken.Add((side, z, run, deep));
                    }
                    catch (VehicleKitException)
                    {
                        // (a seat or a turret's module there: on down the wall)
                    }
                }
                if (placed != null)
                    break;
            }
            first = -first;
            if (placed == null)
                continue;
            PanelList("furniture").Add(placed);
            room.Furniture.Add(placed);
            if (def.Slots.Length > 0)
            {
                room.Containers.Add(placed);
                Loot(placed, Style.LootTable);
            }
        }
        return this;
    }

    /// <summary>An exposed fuel tank on a plate: a whole 1 m plate with the deck's hole round it is the caller's. Stands on the plate's corner nearest <paramref name="near"/>.</summary>
    public VPart TankOn(VPart plate, Vector3 near, string part = "Fuel Drum") =>
        Stand(part, NearestUp(plate, near), Orient.Forward, plate, "-y", 0.1f);

    /// <summary>A fuel pipe that runs under a bed at sill height, along the rail below the cab and up to the engine's left side. <paramref name="side"/>: the rail side it runs down.</summary>
    public Vehicle FuelUnderFloor(VPart tank, float side)
    {
        var port = CentreOf(tank) + new Vector3(0, -0.3f, -0.45f); // (a little out of a drum's Fuel out, through a hole in the bed)
        var lane = side * 0.42f;
        var route = new List<Vector3> { port, port with { Y = 0.55f }, new(lane, 0.55f, port.Z), new(lane, 0.55f, 0.7f), new(lane, 0.05f, 0.7f), new(lane, 0.05f, -0.92f) };
        if (side > 0)
            route.AddRange([new(lane, 0.25f, -0.92f), new(-0.42f, 0.25f, -0.92f)]); // (over the rails, under the gearbox, to the left)
        var fuelIn = CentreOf(Engine!) + new Vector3(-0.42f, 0, 0);
        route.AddRange([new(-0.42f, fuelIn.Y, -0.92f), new(-0.42f, fuelIn.Y, CentreOf(Engine!).Z)]);
        return FuelFrom(tank, route.ToArray());
    }
}
