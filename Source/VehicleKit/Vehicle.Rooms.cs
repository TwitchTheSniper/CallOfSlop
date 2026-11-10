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
    public List<string> Plan = new(); // the bays as planned ("Brow T", "Door", "Bay T-L"): a lofted bay's part name is its key, not its plan (VehicleBodyLanguage.cs)
    public List<VPart> Bays = new();

    public Vector3 FloorRest; // vehicle coordinates to the construct's rest frame
    public bool Ladder;

    /// <summary>Phase 5: the room is a frame of bars (seen and shot through: the check holds a player in it rather than shooting rays for holes).</summary>
    public bool Caged;

    /// <summary>Phase 5: the storey over this room (its floor the cut roofs, reached by <see cref="Stair"/>), and for an upper storey, whether it is an open deck.</summary>
    public Room? Upper;

    /// <summary>Phase 6: a balcony outside the door (Vehicle.Balcony.cs): the part, its deck's middle (vehicle coordinates, at the room's floor), the way out from the
    /// wall, along it toward the closed end, its depth and half length; null: none.</summary>
    public (VPart Part, Vector3 Mid, Vector3 Out, Vector3 Closed, float Depth, float Half)? Balcony;
    public VPart? Stair;
    public bool Open;
    public Vector3 StairFoot; // where a player stands to walk up the stair, at floor height (vehicle coordinates), and the way up it
    public Vector3 StairWay;

    /// <summary>Phase 5: floor kept clear of furniture (the way onto a stair), as rectangles in vehicle coordinates.</summary>
    public List<Vehicle.Rect> Keep = new();

    /// <summary>Whether a footprint centred at (x, z), <paramref name="dx"/> by <paramref name="dz"/>, stands on floor kept clear.</summary>
    public bool Kept(float x, float z, float dx, float dz) => Keep.Any(k => x + dx / 2 > k.X0 && x - dx / 2 < k.X1 && z + dz / 2 > k.Z0 && z - dz / 2 < k.Z1);


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
        var inside = Look != null && !BodyLook.NoInteriors ? Rule : null; // (phase 7a: the faction's density, role gear and props, kit 5's rooms)
        var repeat = inside == null ? 4 : (int)MathF.Round(4 * inside.Fill);
        if (length >= 3) // (a bigger room fills up: the list again, then once more for a bus)
            slots.AddRange(d.Shuffled(slots.Take(repeat)));
        if (length >= 5)
            slots.AddRange(d.Shuffled(slots.Take(repeat)));
        var props = new List<string>();
        var lateGear = 0;
        if (inside != null)
            (slots, props, lateGear) = Interior(room, inside, d.Fork("interior"), length, slots);
        const float Aisle = 0.9f; // (a player is 0.8 m across)
        var from = room.DoorMid.Z > room.Middle.Z ? room.R.Z0 : room.R.Z1; // (from the end away from the door)
        var way = room.DoorMid.Z > room.Middle.Z ? 1f : -1f;
        var taken = new List<(float Side, float Z, float Run, float Deep)>();
        var first = d.Chance(0.5) ? -1f : 1f;
        var index = -1;
        var firstProp = slots.Count + 1 + lateGear; // (role gear with no slots, an ammo rack, after the last-resort chest: it mustn't take a container's wall)
        foreach (var slot in slots.Append("chest").Concat(props)) // (a room with nothing to loot gets a chest: lazily, so this reads what the list above placed; phase 7a's props after it)
        {
            if (slot == "chest" && room.Containers.Count > 0)
                continue;
            index++;
            var named = slot.StartsWith('=');
            var prop = index >= firstProp;
            var part = named ? slot[1..] : Style.Part(d, slot); // ("=Name": a part by its name, phase 7a's gear and props)
            // (phase 7a's gear and props: put no further than 0.1 m from where asked, kept 0.17 m from what stands on the wall already and 0.9 m from the room's
            // ends, where an end chest stands: two backs on one wall line meet in a plane wherever they overlap)
            var (reach, gap, endKeep) = named ? (0.1f, 0.17f, prop ? 0.9f : 0f) : (0.2f, 0.05f, 0f); // (role gear may stand by an end: it is a container, as a chest is)
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
                for (var at = MathF.Max(0.1f, endKeep) + run / 2; placed == null && at + run / 2 < length - MathF.Max(0.1f, endKeep); at += 0.25f)
                {
                    var z = from + way * at;
                    if (named && NearFurniture(room, x, z, alongWall ? size.X : size.Z, alongWall ? size.Z : size.X, prop ? 0.17f + reach : 0.12f, seats: prop)) // (phase 7a: an end chest or a seat stands there)
                        continue;
                    if (InDoorway(room, x, z, deep, run) || room.Kept(x, z, deep + 0.4f, run + 0.4f) /* (Put may move it up to 0.2 m to a socket) */ || (slot != "chest" || clearMiddle) && MathF.Abs(z - room.Middle.Z) < run / 2 + 0.55f && MathF.Abs(x) - deep / 2 - 0.13f < CallOfSlop.World.PlayerRadius + 0.05f ||
                        taken.Any(t => MathF.Abs(t.Z - z) < (t.Run + run) / 2 + gap && (t.Side == side || t.Deep + deep + 0.2f > width - Aisle)))
                        continue;
                    try
                    {
                        placed = Put(part, new Vector3(x, room.FloorTop + size.Y / 2, z), o, reach, onTop: true);
                        taken.Add((side, named ? CentreOf(placed).Z : z, run, deep)); // (phase 7a: a named piece where it stands, not where it was asked for)
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
            if (placed == null && prop)
                placed = OnFurniture(room, part, size, d); // (phase 7c: no floor left for it: on a shelf's, a rack's or a chest's top)
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
    /// Phase 7c: a faction prop stood on what furniture the room has (a shelf's, a rack's, a chest's free top socket), when the walls have no floor left for it:
    /// the furniture in a shuffled order, its top no higher than 2 m (a lamp hangs there, a window's sill is 1.2 m up and more: it stands back against the wall
    /// with its furniture) and 0.3 m under the ceiling. Null: nothing takes it.
    /// </summary>
    private VPart? OnFurniture(Room room, string part, Vector3 size, Dice d)
    {
        foreach (var f in d.Fork("on top").Shuffled(room.Furniture.Where(f => !room.Seats.Contains(f))).ToList())
        {
            C.PartBounds(f.Index, out var mn, out var mx);
            var (lo, hi) = (FromRest(mn), FromRest(mx));
            var top = MathF.Max(lo.Y, hi.Y);
            if (top + size.Y > MathF.Min(room.FloorTop + 2.0f, room.Ceiling - 0.3f) || top - room.FloorTop < 0.3f)
                continue;
            var c = (lo + hi) * 0.5f;
            // (only a top it stands on: a free socket facing up at the piece's drawn top, and room on it for the prop and a centimetre all round)
            if (MathF.Max(size.X, size.Z) > MathF.Min(MathF.Abs(hi.X - lo.X), MathF.Abs(hi.Z - lo.Z)) - 0.02f)
                continue;
            Vector3? spot = null;
            var def = C.DefOf(f.Index);
            var drawn = float.MinValue; // (the highest drawn box, in the rest frame: a knob on a lid stands over the lid's socket)
            for (var k = 0; k < def.Boxes.Length; k++)
            {
                C.BoxOf(f.Index, k, out var bc, out var bh);
                drawn = MathF.Max(drawn, bc.Y + bh.Y);
            }
            for (var k = 0; k < def.Sockets.Length; k++)
            {
                C.SocketAt(f.Index, k, out var at, out var dir);
                var w = FromRest(at);
                if (!C.SocketUsed(f.Index, k) && ConstructParts.DirVector(dir).Y > 0.9f && at.Y >= drawn - 0.003f && (spot is not { } sp || Vector2.Distance(new(w.X, w.Z), new(c.X, c.Z)) < Vector2.Distance(new(sp.X, sp.Z), new(c.X, c.Z))))
                    spot = w;
            }
            if (spot is not { } on)
                continue;
            try
            {
                // (asked for right over that socket, within 12 cm: a socket lower down, a base's say, is further off than that by the piece's height; the
                // British Box Truck 2's flag stood on an Ammo Container's foot socket, in its foot)
                return Put(part, new Vector3(on.X, top + size.Y / 2, on.Z), Orient.Upright, 0.12f, f, onTop: true);
            }
            catch (VehicleKitException)
            {
                // (no free top there, or it's in the way: the next piece)
            }
        }
        return null;
    }

    /// <summary>Whether a footprint (x by z) centred at (x, z) comes within <paramref name="margin"/> of what already stands in the room (its furniture and seats, by their bounds' widest).</summary>
    private bool NearFurniture(Room room, float x, float z, float dx, float dz, float margin, bool seats = true)
    {
        foreach (var f in seats ? room.Furniture.Concat(room.Seats) : room.Furniture)
        {
            var def = C.DefOf(f.Index);
            var size = def.BoundsMax - def.BoundsMin;
            var r = MathF.Max(size.X, size.Z) / 2; // (turned any way: its widest each way)
            var c = CentreOf(f);
            if (MathF.Abs(c.X - x) < r + dx / 2 + margin && MathF.Abs(c.Z - z) < r + dz / 2 + margin)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Phase 7a (docs/vehiclekit/BODY-STYLES.md, section 13): a kit-5 room's furnishing by its faction and its role. Role gear first (it has the walls' first pick; an ammo rack, which holds rounds and no slots, after the last-resort chest):
    /// ammo racks (Ammo Containers) where gunners sit or for a fighting role, bunks in a carrier or a long room with no guns, racks for spares (a Parts Shelf, a Wheel
    /// Rack) in a hauler; then the layout (and the last-resort chest); then the faction's props, <see cref="BodyStyleRule.Clutter"/> a metre. Everything goes down the side walls by
    /// <see cref="Furnish"/>'s rules (never in the doorway, the middle or a stair's way, the aisle kept), so what doesn't fit is left out.
    /// </summary>
    private (List<string> Slots, List<string> Props, int LateGear) Interior(Room room, BodyStyleRule rule, Dice d, float length, List<string> layout)
    {
        var gunners = Turrets.Any(t => room.Seats.Contains(t.Seat));
        var role = Role?.Role.Name ?? "";
        var gear = new List<string>();
        var n = Math.Max(0, rule.Gear);
        if (gunners || role is "gun platform" or "boss" or "raider" or "command")
            gear.AddRange(Enumerable.Repeat("=Ammo Container", n));
        else if (role == "carrier" || length >= 5)
            gear.AddRange(Enumerable.Repeat("bunk", n));
        else
            gear.AddRange(Enumerable.Range(0, n).Select(_ => d.Pick((string[])["=Wheel Rack", "shelf"])));
        var props = new List<string>();
        if (rule.Props.Length > 0)
            for (var i = (int)MathF.Round(rule.Clutter * length); i > 0; i--)
                props.Add("=" + d.Pick(rule.Props));
        var late = gear.Where(g => g.StartsWith('=') && Def(g[1..]).Slots.Length == 0).ToList(); // (an Ammo Container holds rounds, not slots)
        gear.RemoveAll(late.Contains);
        return ([.. gear, .. layout], [.. late, .. props], late.Count);
    }

    /// <summary>
    /// The style's chest against the room's far end wall, in the middle of it, when no side wall took a container (the shape grammar's last resort: a short room's
    /// walls go to seats, the door and the magazine). Tried a quarter metre further in at a time, never into the room's middle; false when nothing takes it.
    /// </summary>
    public bool EndChest(Room room) => EndChest(room, "chest", true);

    /// <summary>As <see cref="EndChest(Room)"/>, the style's <paramref name="slot"/> part (a shelf for the cargo pass's spares: VehicleCargo.cs), filling from the loot table only when <paramref name="loot"/>.</summary>
    public bool EndChest(Room room, string slot, bool loot)
    {
        var part = Style.Part(Dice.Fork((loot ? "end chest " : "end chest spares ") + room.Tag), slot);
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
                if (room.Kept(x, wall - away * (deep / 2 + In), MathF.Max(size.X, size.Z), MathF.Max(size.X, size.Z)))
                    continue;
                if (Look != null && !BodyLook.NoInteriors && NearFurniture(room, x, wall - away * (deep / 2 + In), MathF.Max(size.X, size.Z), MathF.Max(size.X, size.Z), 0.05f, seats: false))
                    continue; // (phase 7a: the role gear and props stand down the walls further toward the ends)
                var placed = Put(part, new Vector3(x, room.FloorTop + size.Y / 2, wall - away * (deep / 2 + In)), o, 0.1f, onTop: true);
                PanelList("furniture").Add(placed);
                room.Furniture.Add(placed);
                room.Containers.Add(placed);
                if (loot)
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
