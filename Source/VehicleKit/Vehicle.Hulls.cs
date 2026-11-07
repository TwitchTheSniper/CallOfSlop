using System.Numerics;

namespace CallOfSlop.VehicleKit;

public sealed partial class Vehicle
{
    private const float BayHalf = 1.0f; // what furniture may reach, either side of the middle
    private const float DoorPlane = 0.025f, HingeWidth = 0.02f; // the door's middle out from the frame, and a hinge's width
    private const string HullDoor = "Steel Flush Door 1.25x2 m", HullHinge = "Long Door Hinge";

    /// <summary>
    /// A walk-in body from a run of faceted bays, front to back: <paramref name="bays"/> is their names less "Hull " ("Brow T", "Bay T-L", "Tail L") and "Door" for the Door Bay, which puts a flush door in the +x side.
    /// A bolt deck at <paramref name="under"/> runs under them. Bays' roofs, waists and ends change height and width, so a body built this way isn't a box: see VEHICLEKIT.md, "Silhouette".
    /// <paramref name="seatSpots"/> are points on the floor's 0.25 m grid. <see cref="Room.Bays"/> holds the bays, for roof turrets (a bay's "top") and side mounts ("+x0", "-x0").
    /// </summary>
    public Room Hull(string tag, float z0, float under, string[] bays, (float X, float Z)[]? seatSpots = null)
    {
        var floorTop = under + 0.05f;
        if (Look != null && WheelBand(z0, z0 + bays.Length + 0.5f, floorTop) is var band && band > Look.FlareFrom)
        {
            Modules.Add($"overhang from {band:0.00} m (the wheels)"); // (the plan judged its spots on the look's own: BaySpots; the wheels came higher)
            Look.FlareFrom = band;
        }
        var names = Look != null ? Look.Names(bays) : bays.Select(b => b == "Door" ? "Hull Door Bay T" : "Hull " + b).ToArray(); // (kit 5: lofted bays in the style's body language)
        if (Look != null && Look.Spoken(bays.Length) is var spoken && spoken != Look.Language.Name)
            Modules.Add($"sections {spoken}"); // (a mixing style's room: its stations' languages, front to back)
        var doorSide = Look?.DoorSide ?? 1;
        var lengths = new float[names.Length];
        for (var k = 0; k < names.Length; k++)
        {
            var d = Def(names[k]); // (a lofted bay is made the first time its key is asked for: a few milliseconds, so a spawn hands back to the tick after each)
            lengths[k] = MathF.Round(d.BoundsMax.Z - d.BoundsMin.Z, 2);
            VehicleGenJob.Yield();
        }
        var z1 = z0 + lengths.Sum();
        var r = new Rect(-BayHalf, BayHalf, z0, z1);
        var floorHalf = Look?.Scale.FloorHalf ?? 1.125f;
        var room = new Room { Tag = tag, R = r, DoorFace = new Vector3(doorSide, 0, 0), FloorTop = floorTop, Plan = bays.ToList() };
        Deck(tag + "floor", under, new Rect(-floorHalf, floorHalf, z0, z1), "floor");
        var walls = PanelList(tag + "walls");
        var z = z0;
        var ceiling = 0f;
        for (var k = 0; k < names.Length; k++)
        {
            // A bay stands on its feet (the Bolt Deck's quarter grid, from its floor's width: HullLoft.FootX); a left door bay is the right one turned round.
            var turned = bays[k] == "Door" && doorSide < 0 && Look != null;
            var foot = HullLoft.ShapeOf(names[k]) is { } sh ? HullLoft.FootX(sh) : 0.75f;
            var bay = turned
                ? Stand(names[k], new Vector3(-foot, room.FloorTop, z + 0.125f), new Orient(Vector3.UnitY, Vector3.UnitY, -Vector3.UnitZ, Vector3.UnitZ), null, "footR1", 0.06f)
                : Stand(names[k], new Vector3(-foot, room.FloorTop, z + 0.125f), Orient.Forward, null, "footL0", 0.06f);
            walls.Add(bay);
            room.Bays.Add(bay);
            ceiling = MathF.Max(ceiling, room.FloorTop + Def(names[k]).BoundsMax.Y); // (the tallest bay: a brow or a tail comes down)
            if (bays[k] == "Door")
                HullDoorIn(room, bay, z, tag, doorSide);
            z += lengths[k];
        }
        room.Walls.AddRange(walls);
        room.Ceiling = ceiling - 0.1f;
        room.FloorRest = ToRest(Vector3.Zero);
        if (Skirted)
            Skirts(new Rect(-floorHalf, floorHalf, z0, z1));
        foreach (var (x, sz) in seatSpots ?? [])
            room.Seats.Add(Put("Seat", new Vector3(x, room.FloorTop + 0.475f, sz), Orient.Forward, 0.2f, null, "bottom", onTop: true));
        Rooms.Add(room);
        return room;
    }

    /// <summary>
    /// How high above the room's floor an overhang must start to clear the wheels under the room's length (from <paramref name="z0"/> to <paramref name="z1"/>): the
    /// tyres' tops plus 0.12 m for their travel, where a tyre reaches in under the overhang (past 1.0 m of half width); 0 with no wheel there.
    /// </summary>
    private float WheelBand(float z0, float z1, float floorTop)
    {
        var band = 0f;
        for (var i = 0; i < C.PartCount; i++)
        {
            if (C.DefOf(i).Role is not PartRole.Wheel)
                continue;
            C.PartBounds(i, out var mn, out var mx);
            var (a, b) = (FromRest(mn), FromRest(mx));
            if (b.Z > z0 - 0.3f && a.Z < z1 + 0.3f && MathF.Max(MathF.Abs(a.X), MathF.Abs(b.X)) > 1.0f)
                band = MathF.Max(band, b.Y - floorTop + 0.12f);
        }
        return MathF.Round(band * 100) / 100;
    }

    /// <summary>The flush door in a Door Bay: its hinge welded to the jamb behind the opening, the door swung shut in it; on <paramref name="side"/> (+1 right, -1 left: the right one turned round).</summary>
    private void HullDoorIn(Room room, VPart bay, float z, string tag, float side = 1)
    {
        const float plane = 1.0f;
        var mid = z + 0.75f;
        room.DoorMid = new Vector3(side * plane, room.FloorTop, mid);
        var turn = side > 0 ? new Orient(Vector3.UnitX, -Vector3.UnitZ, Vector3.UnitZ, -Vector3.UnitX) : new Orient(Vector3.UnitX, Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitX);
        var def = Def(HullHinge);
        var at = new Vector3(side * (plane + DoorPlane), room.FloorTop + 1f, mid + side * (DoorWidth - HingeWidth) * 0.5f);
        room.Hinge = Weld(HullHinge, bay, at + Rot24.Apply(FirstTurn(turn), (def.BoundsMin + def.BoundsMax) * 0.5f), turn, 0, 0, 0);
        room.Door = Onto(room.Hinge, "leaf", HullDoor, "hang", 0);
        Paint(room.Door, Style.Colour(Dice.Fork("door " + tag), "rust"));
        room.Ladder = Boarding(new Vector3(side * 1.13f, room.FloorTop, mid), tag + "walls");
    }

    /// <summary>A gun on a side: a Gun Sponson on <paramref name="on"/>'s waist socket, a turntable and the gun on it. <paramref name="seat"/> works it from inside.</summary>
    public Vehicle SideGun(VPart on, float side, VPart? seat = null, WeaponOption? weapon = null)
    {
        var sponson = Onto(on, side > 0 ? "+x0" : "-x0", "Gun Sponson", "back", 0);
        PanelList("sponsons").Add(sponson);
        return Turret(sponson, weapon, seat: seat);
    }

    /// <summary>Faceted boxes hung on the free waist sockets of some bays: a broken flank.</summary>
    public Vehicle Panniers(IEnumerable<VPart> bays, Dice d, double chance = 0.5)
    {
        foreach (var bay in bays)
            foreach (var side in new[] { "-x0", "+x0" })
            {
                if (Look != null && !BaySpots.Pannier([bay.Def.Name], 0))
                    break; // (a lofted bay whose side stands out just over its waist: an eave, a ledge)
                var at = Array.FindIndex(bay.Def.Sockets, s => s.Name == side);
                if (at < 0 || C.SocketUsed(bay.Index, at) || !d.Chance(chance))
                    continue;
                try
                {
                    PanelList("panniers").Add(Onto(bay, side, "Hull Pannier 1 m", "back", 0));
                }
                catch (VehicleKitException e) when (e.Message.Contains("in the way", StringComparison.Ordinal))
                {
                    // (a ladder or a door there: that side stays flat)
                }
            }
        return this;
    }
}
