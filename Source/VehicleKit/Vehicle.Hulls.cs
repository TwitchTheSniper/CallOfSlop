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
        var rear = Look?.RearDoor == true; // (phase 2: the door in the tail's back)
        var lengths = new float[names.Length];
        for (var k = 0; k < names.Length; k++)
        {
            var d = Def(names[k]); // (a lofted bay is made the first time its key is asked for: a few milliseconds, so a spawn hands back to the tick after each)
            lengths[k] = HullLoft.ShapeOf(names[k])?.Length ?? MathF.Round(d.BoundsMax.Z - d.BoundsMin.Z, 2); // (a lofted bay's own length: its caps and door ends stand a little past it)
            VehicleGenJob.Yield();
        }
        var z1 = z0 + lengths.Sum();
        var r = new Rect(-BayHalf, BayHalf, z0, z1);
        var floorHalf = Look?.Scale.FloorHalf ?? 1.125f;
        var room = new Room { Tag = tag, R = r, DoorFace = rear ? Vector3.UnitZ : new Vector3(doorSide, 0, 0), FloorTop = floorTop, Plan = bays.ToList(), Caged = Look?.Cage is > '\0' };
        // The floor: plates on a half-metre lattice where the bays' feet stand (a quarter-metre plate's centre is half a centimetre off the grid, so a foot on one
        // stood its bay 5 mm off, and bays of different lengths then met in each other: phase 2); an odd column at the +x edge in plates of any size.
        var cols = (int)MathF.Round(2 * floorHalf / Cell);
        var main = cols % 2 == 0 ? floorHalf : floorHalf - Cell;
        Deck(tag + "floor", under, new Rect(-floorHalf, main, z0, z1), "floor", aligned: true);
        if (main < floorHalf)
            Deck(tag + "floor", under, new Rect(main, floorHalf, z0, z1), "floor");
        var walls = PanelList(tag + "walls");
        var balcony = Look != null && (bays.Contains("Door") || rear) ? BalconyPlan(tag, rear) : null; // (phase 6: Vehicle.Balcony.cs; its ladder at its open end)
        var z = z0;
        var ceiling = 0f;
        for (var k = 0; k < names.Length; k++)
        {
            // A bay stands on its feet (the Bolt Deck's quarter grid, from its floor's width: HullLoft.FootX); a left door bay is the right one turned round.
            var turned = bays[k] == "Door" && doorSide < 0 && Look != null && !rear;
            var foot = HullLoft.ShapeOf(names[k]) is { } sh ? HullLoft.FootX(sh) : 0.75f;
            var bay = turned
                ? Stand(names[k], new Vector3(-foot, room.FloorTop, z + 0.125f), new Orient(Vector3.UnitY, Vector3.UnitY, -Vector3.UnitZ, Vector3.UnitZ), null, "footR1", 0.06f)
                : Stand(names[k], new Vector3(-foot, room.FloorTop, z + 0.125f), Orient.Forward, null, "footL0", 0.06f);
            walls.Add(bay);
            room.Bays.Add(bay);
            Glaze(bay, names[k]);
            ceiling = MathF.Max(ceiling, room.FloorTop + Def(names[k]).BoundsMax.Y); // (the tallest bay: a brow or a tail comes down)
            if (bays[k] == "Door" && !rear)
                HullDoorIn(room, bay, z, tag, doorSide, balcony == null);
            else if (rear && k == names.Length - 1)
                RearDoorIn(room, bay, z + lengths[k], tag, balcony == null);
            z += lengths[k];
        }
        room.Walls.AddRange(walls);
        room.Ceiling = ceiling - 0.1f;
        room.FloorRest = ToRest(Vector3.Zero);
        if (balcony != null && !Balcony(room, balcony))
            room.Ladder = rear ? Boarding(new Vector3(0, room.FloorTop, room.DoorMid.Z + 0.13f), tag + "walls") : Boarding(new Vector3(doorSide * 1.13f, room.FloorTop, room.DoorMid.Z), tag + "walls"); // (no room for it: the ladder under the door, as without)
        if (Skirted)
            Skirts(new Rect(-floorHalf, floorHalf, z0, z1));
        foreach (var (x, sz) in seatSpots ?? [])
            room.Seats.Add(Put("Seat", new Vector3(x, room.FloorTop + 0.475f, sz), Orient.Forward, 0.2f, null, "bottom", onTop: true));
        Rooms.Add(room);
        return room;
    }

    /// <summary>Phase 7b: a lined bay's windows glazed, a pane on each window's sill (HullLoft.RoomPanesOf), cut in the bay's frame as a cab's panes are.</summary>
    private void Glaze(VPart bay, string name)
    {
        if (HullLoft.ShapeOf(name) is not { Window: > '\0' } sh)
            return;
        foreach (var (socket, pane) in HullLoft.RoomPanesOf(sh))
            PanelList("roomglass").Add(AsIs(bay, socket, pane.Key(), "frame")); // (not "glass": that is the cab's, which the cab measure reads)
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
    private void HullDoorIn(Room room, VPart bay, float z, string tag, float side = 1, bool ladder = true)
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
        if (ladder)
            room.Ladder = Boarding(new Vector3(side * 1.13f, room.FloorTop, mid), tag + "walls");
    }

    /// <summary>
    /// The flush door in a tail's back doorway (phase 2, HullLoft's "b" flag): the side door's hinge and leaf turned a quarter round, the hinge on the left jamb, the
    /// door swung shut across the opening, a ladder under it.
    /// </summary>
    private void RearDoorIn(Room room, VPart bay, float zBack, string tag, bool ladder = true)
    {
        room.DoorMid = new Vector3(0, room.FloorTop, zBack);
        var turn = new Orient(Vector3.UnitX, Vector3.UnitX, Vector3.UnitZ, -Vector3.UnitZ);
        var def = Def(HullHinge);
        var at = new Vector3(-(DoorWidth - HingeWidth) * 0.5f, room.FloorTop + 1f, zBack + 0.025f + (DoorPlane - 0.02f));
        room.Hinge = Weld(HullHinge, bay, at + Rot24.Apply(FirstTurn(turn), (def.BoundsMin + def.BoundsMax) * 0.5f), turn, 0, 0, 0);
        room.Door = Onto(room.Hinge, "leaf", HullDoor, "hang", 0);
        Paint(room.Door, Style.Colour(Dice.Fork("door " + tag), "rust"));
        if (ladder)
            room.Ladder = Boarding(new Vector3(0, room.FloorTop, zBack + 0.13f), tag + "walls");
    }

    /// <summary>A gun on a side: a Gun Sponson on <paramref name="on"/>'s waist socket, a turntable and the gun on it. <paramref name="seat"/> works it from inside.</summary>
    public Vehicle SideGun(VPart on, float side, VPart? seat = null, WeaponOption? weapon = null)
    {
        var sponson = Onto(on, side > 0 ? "+x0" : "-x0", "Gun Sponson", "back", 0);
        PanelList("sponsons").Add(sponson);
        return Turret(sponson, weapon, seat: seat);
    }

    private const string PannierBeside = "a pannier beside it";

    /// <summary>
    /// A pannier mustn't overlap one already on: a 1 m pannier on a bay under a metre long reached into its neighbour's, and their tops shared a plane once wear
    /// painted them apart (Highland War Bus 16, 2026-11-10: the placing check took them as one look).
    /// </summary>
    private string? PannierClear(ConstructPartDef def, ConstructGhost g)
    {
        var (mn, mx) = Drawn(def, g.Pos, g.Rot);
        foreach (var p in PanelList("panniers"))
        {
            var (pn, px) = Drawn(C.DefOf(p.Index), C.Parts[p.Index].Pos, C.Parts[p.Index].Rot);
            if (pn.X < mx.X - 0.005f && px.X > mn.X + 0.005f && pn.Y < mx.Y - 0.005f && px.Y > mn.Y + 0.005f && pn.Z < mx.Z - 0.005f && px.Z > mn.Z + 0.005f)
                return PannierBeside;
        }
        return null;
    }

    /// <summary>A part's drawn boxes' bounds (its detail boxes too: a pannier's faceted shell is a metre long round a 0.76 m solid box) where it stands, rest frame.</summary>
    private static (Vector3 Min, Vector3 Max) Drawn(ConstructPartDef def, Vector3 pos, byte rot)
    {
        var (mn, mx) = (new Vector3(float.MaxValue), new Vector3(float.MinValue));
        foreach (var b in def.Boxes)
        {
            if (b.Hidden)
                continue;
            var c = pos + Rot24.Apply(rot, b.Center);
            var h = Rot24.ApplyHalf(rot, b.Half);
            (mn, mx) = (Vector3.Min(mn, c - h), Vector3.Max(mx, c + h));
        }
        return (mn, mx);
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
                    PanelList("panniers").Add(Onto(bay, side, "Hull Pannier 1 m", "back", 0, veto: PannierClear));
                }
                catch (VehicleKitException e) when (e.Message.Contains("in the way", StringComparison.Ordinal) || e.Message.Contains(PannierBeside, StringComparison.Ordinal))
                {
                    // (a ladder or a door there, or the last bay's pannier: that side stays flat)
                }
            }
        return this;
    }
}
