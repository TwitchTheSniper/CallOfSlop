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
    public const float DoorWidth = 1.25f; // a Door Bay's opening

    /// <summary>The rooms built, for the check.</summary>
    public List<Room> Rooms { get; } = new();

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
    /// <paramref name="clearMiddle"/>: the last-resort chest keeps out of the room's middle too, where a player stands (the shape grammar's rooms; kit 1's
    /// rooms let it stand anywhere, and a short room could put it in the way).
    /// </summary>
    public Vehicle Furnish(Room room, bool clearMiddle = false)
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
                    if (InDoorway(room, x, z, deep, run) || (slot != "chest" || clearMiddle) && MathF.Abs(z - room.Middle.Z) < run / 2 + 0.55f && MathF.Abs(x) - deep / 2 - 0.13f < CallOfSlop.World.PlayerRadius + 0.05f ||
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

    /// <summary>
    /// The style's chest against the room's far end wall, in the middle of it, when no side wall took a container (the shape grammar's last resort: a short room's
    /// walls go to seats, the door and the magazine). Tried a quarter metre further in at a time, never into the room's middle; false when nothing takes it.
    /// </summary>
    public bool EndChest(Room room)
    {
        var part = Style.Part(Dice.Fork("end chest " + room.Tag), "chest");
        var def = Def(part);
        var size = def.BoundsMax - def.BoundsMin;
        var alongWall = size.X >= size.Z;
        var deep = alongWall ? size.Z : size.X;
        var away = room.DoorMid.Z > room.Middle.Z ? -1f : 1f; // (the end away from the door's half)
        var wall = away < 0 ? room.R.Z0 : room.R.Z1;
        var inward = new Vector3(0, 0, -away);
        var o = alongWall ? new Orient(Vector3.UnitZ, inward, Vector3.UnitY, Vector3.UnitY) : new Orient(Vector3.UnitX, inward, Vector3.UnitY, Vector3.UnitY);
        for (var In = 0.15f; MathF.Abs(wall - away * (deep / 2 + In) - room.Middle.Z) >= deep / 2 + CallOfSlop.World.PlayerRadius + 0.1f; In += 0.25f) // (never into the middle, where a player stands)
        foreach (var x in (float[])[-0.25f, 0.25f])
            try
            {
                var placed = Put(part, new Vector3(x, room.FloorTop + size.Y / 2, wall - away * (deep / 2 + In)), o, 0.1f, onTop: true);
                PanelList("furniture").Add(placed);
                room.Furniture.Add(placed);
                room.Containers.Add(placed);
                Loot(placed, Style.LootTable);
                return true;
            }
            catch (VehicleKitException)
            {
                // (the end wall rakes in, or a seat's there: further in)
            }
        return false;
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
        for (var k = 1; k < route.Count; k++) // (a pipe's segment reaches 8 m: a tank further back than that gets a node half way; nothing shorter changes)
            if (Vector3.Distance(route[k - 1], route[k]) > 8f)
                route.Insert(k, (route[k - 1] + route[k]) * 0.5f);
        return FuelFrom(tank, route.ToArray());
    }
}
