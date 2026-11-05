using System.Numerics;

namespace CallOfSlop.VehicleKit.Generators;

/// <summary>A bus war rig: one long room with a side door, three roof guns, drums strapped on at the tail.</summary>
public sealed class WarBus(FactionStyle style) : HeavyShape(style)
{
    public override string Shape => "War Bus";
    public override IReadOnlyList<int> Seeds => [1, 10, 11];

    public override void Build(Vehicle v)
    {
        v.Description = "A bus war rig. A long stepped hull with a side door, guns on the roof and the flanks, drums at the tail.";
        var d = v.Dice.Fork("shape");
        var body = d.Pick((string[][])[["Brow T", "Door", "Bay T", "Bay T-L", "Bay L", "Tail L"], ["Brow N", "Bay N-T", "Door", "Bay T", "Bay T-N", "Tail N"], ["Bay L-T", "Door", "Bay T-L", "Bay L-T", "Bay T", "Tail T"]]);
        var length = 6.5f;
        var end = 0.2f + length;
        Chassis(v, length + 0.25f, 2.5f);
        v.Under(Underside(v, d), 0.2f, end);
        var bus = v.Hull("bus", 0.2f, v.FloorY, body, [(-0.5f, 0.575f), (-0.5f, 4.575f), (-0.5f, end - 0.875f)]);
        v.Turret(bus.Bays[0], seat: bus.Seats[0]);
        v.Turret(bus.Bays[^2], seat: bus.Seats[2]);
        v.SideGun(bus.Bays[3], -1, bus.Seats[1]);
        v.Furnish(bus);
        v.Panniers(bus.Bays, d.Fork("panniers"), 0.4);
        Porch(v, end, -1);
        v.Hood();
        v.Trim("buswalls", "hoodsides");
    }
}
