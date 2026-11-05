using System.Numerics;

namespace CallOfSlop.VehicleKit.Generators;

/// <summary>A gun wagon: a short crew room up front, an open bed behind it with two deck guns, the drum at the tail.</summary>
public sealed class GunWagon(FactionStyle style) : HeavyShape(style)
{
    public override string Shape => "Gun Wagon";

    public override void Build(Vehicle v)
    {
        v.Description = "A gun wagon. A short crew room, two deck guns on an open bed, the drum at the tail.";
        Chassis(v, 4.75f, 1.75f);
        v.Under("tub", 0.2f, 3.2f, 3.2f);
        v.Sill(v.RailZ(3.7f), layers: 2);
        var room = v.Room("room", new Vehicle.Rect(-Edge, Edge, 0.2f, 3.2f), Under, Vector3.UnitX, 1.2f, seatSpots: [(-0.75f, 0.825f)], mounts: [(-0.375f, 0.7f)], chamfer: true);
        v.Turret(v.Mounts["room"][0], seat: room.Seats[0]);
        v.Furnish(room);
        var spot = new Vehicle.Rect(0.375f, 1.375f, 4.45f, 5.45f);
        v.Deck("bed", Under, new Vehicle.Rect(-Edge, Edge, 3.45f, 5.2f), "bed", [new Vehicle.Rect(-0.375f, 0.375f, 4.45f, 5.2f)], whole: [spot]);
        v.FuelUnderFloor(v.TankOn(v.Wholes[("bed", spot)], new Vector3(1, 0, 4.5f)), 1);
        v.Turret(v.Sill(4.825f, layers: 2));
        foreach (var s in new[] { -1f, 1f })
            v.Wall("sides", new Vector3(s, 0, 0), s * Edge, 3.45f, 5.2f, Under - 0.12f, 0.5f, "sheet", 0.2);
        v.Hood();
        v.Trim("sides", "roomwalls", "hoodsides");
    }
}
