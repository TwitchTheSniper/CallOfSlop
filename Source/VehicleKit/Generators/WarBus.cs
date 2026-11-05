using System.Numerics;

namespace CallOfSlop.VehicleKit.Generators;

/// <summary>A bus war rig: one long room with a side door, three roof guns, drums strapped on at the tail.</summary>
public sealed class WarBus(FactionStyle style) : HeavyShape(style)
{
    public override string Shape => "War Bus";
    public override IReadOnlyList<int> Seeds => [1, 10, 11];

    public override void Build(Vehicle v)
    {
        v.Description = "A bus war rig. A long room with a side door, three roof guns, drums at the tail.";
        var d = v.Dice.Fork("shape");
        var length = d.Chance(0.5) ? 6f : 5f;
        var end = 0.2f + length;
        Chassis(v, length + 0.75f, 1.75f);
        v.Under("skirted", 0.2f, end, end);
        var bus = v.Room("bus", new Vehicle.Rect(-Edge, Edge, 0.2f, end), Under, Vector3.UnitX, 1.2f,
            seatSpots: [(-0.75f, 0.575f), (-0.75f, 2.325f), (-0.75f, 4.075f)], mounts: [(-0.375f, 0.7f), (-0.375f, 2.45f), (-0.375f, 3.95f)], chamfer: true);
        foreach (var (mount, seat) in v.Mounts["bus"].Zip(bus.Seats))
            v.Turret(mount, seat: seat);
        v.Furnish(bus);
        Porch(v, end, -1);
        v.Hood();
        v.Trim("buswalls", "hoodsides");
    }
}
