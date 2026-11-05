using System.Numerics;

namespace CallOfSlop.VehicleKit.Generators;

/// <summary>A bus war rig: one long room with a side door, three roof guns, drums strapped on at the tail.</summary>
public sealed class WarBus(FactionStyle style) : HeavyShape(style)
{
    public override string Shape => "War Bus";

    public override void Build(Vehicle v)
    {
        v.Description = "A bus war rig. A long room with a side door, three roof guns, drums at the tail.";
        Chassis(v, 5.75f, 1.75f, 0.325f, 1.325f, 2.325f, 3.325f, 4.825f, 5.825f);
        var bus = v.Room("bus", new Vehicle.Rect(-Edge, Edge, 0.2f, 5.2f), Under, Vector3.UnitX, 1.2f,
            seatSpots: [(-0.875f, 0.45f), (-0.875f, 2.2f), (-0.875f, 3.95f)], mounts: [(-0.375f, 0.7f), (-0.375f, 2.45f), (-0.375f, 3.95f)]);
        foreach (var (mount, seat) in v.Mounts["bus"].Zip(bus.Seats))
            v.Turret(mount, seat: seat);
        v.Stores(bus, "shelf", 4).Stores(bus, "chest", 2);
        var spot = new Vehicle.Rect(-Edge, -0.125f, 5.45f, 6.45f);
        v.Deck("porch", Under, new Vehicle.Rect(-Edge, Edge, 5.45f, 6.2f), "bed", whole: [spot]);
        v.FuelUnderFloor(v.TankOn(v.Wholes[("porch", spot)], new Vector3(-1, 0, 5.5f)), -1);
        v.Hood();
        v.Trim("buswalls", "hoodsides");
    }
}
