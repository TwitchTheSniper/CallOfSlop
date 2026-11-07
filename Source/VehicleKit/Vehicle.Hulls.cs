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
        var names = bays.Select(b => b == "Door" ? "Hull Door Bay T" : "Hull " + b).ToArray();
        var lengths = names.Select(n => MathF.Round(Def(n).BoundsMax.Z - Def(n).BoundsMin.Z, 2)).ToArray();
        var z1 = z0 + lengths.Sum();
        var r = new Rect(-BayHalf, BayHalf, z0, z1);
        var room = new Room { Tag = tag, R = r, DoorFace = Vector3.UnitX, FloorTop = under + 0.05f };
        Deck(tag + "floor", under, new Rect(-1.125f, 1.125f, z0, z1), "floor");
        var walls = PanelList(tag + "walls");
        var z = z0;
        var ceiling = 0f;
        for (var k = 0; k < names.Length; k++)
        {
            var bay = Stand(names[k], new Vector3(-0.75f, room.FloorTop, z + 0.125f), Orient.Forward, null, "footL0", 0.06f);
            walls.Add(bay);
            room.Bays.Add(bay);
            ceiling = MathF.Max(ceiling, room.FloorTop + Def(names[k]).BoundsMax.Y); // (the tallest bay: a brow or a tail comes down)
            if (bays[k] == "Door")
                HullDoorIn(room, bay, z, tag);
            z += lengths[k];
        }
        room.Walls.AddRange(walls);
        room.Ceiling = ceiling - 0.1f;
        room.FloorRest = ToRest(Vector3.Zero);
        if (Skirted)
            Skirts(new Rect(-1.125f, 1.125f, z0, z1));
        foreach (var (x, sz) in seatSpots ?? [])
            room.Seats.Add(Put("Seat", new Vector3(x, room.FloorTop + 0.475f, sz), Orient.Forward, 0.2f, null, "bottom", onTop: true));
        Rooms.Add(room);
        return room;
    }

    /// <summary>The flush door in a Door Bay: its hinge welded to the jamb behind the opening, the door swung shut in it.</summary>
    private void HullDoorIn(Room room, VPart bay, float z, string tag)
    {
        const float plane = 1.0f;
        var mid = z + 0.75f;
        room.DoorMid = new Vector3(plane, room.FloorTop, mid);
        var turn = new Orient(Vector3.UnitX, -Vector3.UnitZ, Vector3.UnitZ, -Vector3.UnitX);
        var def = Def(HullHinge);
        var at = new Vector3(plane + DoorPlane, room.FloorTop + 1f, mid + (DoorWidth - HingeWidth) * 0.5f);
        room.Hinge = Weld(HullHinge, bay, at + Rot24.Apply(FirstTurn(turn), (def.BoundsMin + def.BoundsMax) * 0.5f), turn, 0, 0, 0);
        room.Door = Onto(room.Hinge, "leaf", HullDoor, "hang", 0);
        Paint(room.Door, Style.Colour(Dice.Fork("door " + tag), "rust"));
        room.Ladder = Boarding(new Vector3(1.13f, room.FloorTop, mid), tag + "walls");
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
