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
    public Room Room(string tag, Rect r, float under, Vector3 doorFace, float doorFrom, (float X, float Z)[]? seatSpots = null, (float X, float Z)[]? mounts = null)
    {
        var room = new Room { Tag = tag, R = r, DoorFace = doorFace };
        var seatPlates = (seatSpots ?? []).Select(s => new Rect(s.X, s.X + 1, s.Z, s.Z + 1)).ToArray();
        var roofPlates = (mounts ?? []).Select(s => new Rect(s.X, s.X + 1, s.Z, s.Z + 1)).ToArray();
        Deck(tag + "floor", under, r, "deck", whole: seatPlates);
        room.FloorTop = under + 0.05f;

        var sideways = MathF.Abs(doorFace.X) > 0.5f;
        var skip = (doorFrom, doorFrom + DoorWidth);
        var bottom = under - 0.12f;
        foreach (var (face, at, from, to) in new[] { (Vector3.UnitX, r.X1, r.Z0, r.Z1), (-Vector3.UnitX, r.X0, r.Z0, r.Z1), (Vector3.UnitZ, r.Z1, r.X0, r.X1), (-Vector3.UnitZ, r.Z0, r.X0, r.X1) })
        {
            var open = Vector3.Dot(face, doorFace) > 0.5f;
            Wall(tag + "walls", face, at, from, to, bottom, 2f, "wall", 0, open ? skip : null, strict: true);
            Wall(tag + "upper", face, at, from, to, bottom + 2f, 0.5f, "wall", Style.Limit("slits"), open ? skip : null);
        }
        room.Walls.AddRange(Panels[tag + "walls"]);
        room.Walls.AddRange(Panels[tag + "upper"]);

        // The roof hangs on the upper plates' inner sockets.
        var roofY = RoofLine(Panels[tag + "upper"], under + 2.1f);
        Deck(tag + "roof", roofY, r, "sheet", smallest: 2, whole: roofPlates);
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

        // Seats on their plates.
        foreach (var h in seatPlates)
        {
            var plate = Wholes[(tag + "floor", h)];
            room.Seats.Add(Stand("Seat", NearestUp(plate, new Vector3(h.X0 + 0.1f, 0, h.Z0 + 0.5f)), Orient.Forward, plate, "bottom", 0.1f));
        }
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
    private Vector3 NearestUp(VPart plate, Vector3 near)
    {
        var best = Vector3.Zero;
        var bestD = float.MaxValue;
        var def = C.DefOf(plate.Index);
        for (var s = 0; s < def.Sockets.Length; s++)
        {
            C.SocketAt(plate.Index, s, out var at, out var dir);
            var p = FromRest(at);
            var d = Vector2.Distance(new Vector2(p.X, p.Z), new Vector2(near.X, near.Z));
            if (Vector3.Dot(ConstructParts.DirVector(dir), Vector3.UnitY) > 0.9f && !C.SocketUsed(plate.Index, s) && d < bestD)
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

    /// <summary>Containers along the room's side walls, filling from the style's loot table each time the vehicle spawns. <paramref name="slot"/>: "shelf", or a part name's slot; they hang on the walls' inner faces, 0.5 m and more off the floor.</summary>
    public Vehicle Stores(Room room, string slot, int count)
    {
        var d = Dice.Fork("stores " + room.Tag);
        var spots = new List<(VPart Wall, Vector3 Inward)>();
        foreach (var w in Panels[room.Tag + "walls"])
        {
            if (w.Name.StartsWith("Boarding", StringComparison.Ordinal))
                continue;
            var c = CentreOf(w);
            var inward = MathF.Abs(c.X - room.R.X0) < 0.1f ? Vector3.UnitX : MathF.Abs(c.X - room.R.X1) < 0.1f ? -Vector3.UnitX : Vector3.Zero;
            if (inward != Vector3.Zero && Vector2.Distance(new Vector2(c.X, c.Z), new Vector2(room.DoorMid.X, room.DoorMid.Z)) > 1.3f) // (nothing in front of the door)
                spots.Add((w, inward));
        }
        var left = count;
        foreach (var (w, inward) in d.Shuffled(spots))
        {
            if (left <= 0)
                break;
            var part = Style.Part(d, slot);
            var def = Def(part);
            var c = CentreOf(w);
            var o = new Orient(Vector3.UnitZ, inward, Vector3.UnitY, Vector3.UnitY);
            var depth = MathF.Abs(Rot24.ApplyHalf(FirstTurn(o), (def.BoundsMax - def.BoundsMin) * 0.5f).X);
            try
            {
                var shelf = Put(part, new Vector3(c.X + inward.X * (depth + 0.06f), room.FloorTop + 0.7f, c.Z), o, 0.45f, w);
                room.Containers.Add(shelf);
                Loot(shelf, Style.LootTable);
                left--;
            }
            catch (VehicleKitException)
            {
                // (a door or a seat in the way: the next wall)
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
