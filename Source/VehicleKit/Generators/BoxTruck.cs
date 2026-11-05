using System.Numerics;

namespace CallOfSlop.VehicleKit.Generators;

/// <summary>What the bigger shapes share: their chassis, a bigger limit in the check, one rear sill row. A shape is its proportions and where its room, turrets and tanks go.</summary>
public abstract class HeavyShape(FactionStyle style) : VehicleShape(style)
{
    protected const float Under = 0.625f, Edge = 1.125f; // a floor on two-layer sills, clear of the wheels; the odd eighth nearest 2.25 m wide
    public override IReadOnlyList<int> Seeds => [3, 11, 19];

    public override VehicleCheckOptions? Checks => new() { MaxParts = 500, MaxMass = 16000, MinDistance = 15, MinTurn = 30, MinStability = 0.6f, MaxYawDrift = 15 };

    /// <summary>The chassis, cab and hood, with sills at each of <paramref name="sills"/> (the rails' grid: -1.425 and quarter metres; behind the differential 0.125 past).</summary>
    protected static void Chassis(Vehicle v, float rails, float tail, params float[] sills)
    {
        v.Heavy().Ladder(-1.675f, rails, 0.625f, tail, frontWing: 0.5f).SteeredAxle().DrivenAxle().Powertrain(-1.425f).OpenCab();
        foreach (var z in sills)
            v.Sill(z, layers: 2);
    }
}

/// <summary>An armoured box on a truck: a walk-in box with a rear door, shelves, and two roof guns worked from seats inside. The drum stands on the rear porch.</summary>
public sealed class BoxTruck(FactionStyle style) : HeavyShape(style)
{
    public override string Shape => "Box Truck";

    public override void Build(Vehicle v)
    {
        v.Description = "An armoured box truck. Walk-in box with loot, two roof guns, a drum on the rear porch.";
        Chassis(v, 4.75f, 1.75f, 0.325f, 1.325f, 2.325f, 4.075f, 4.825f);
        var box = v.Room("box", new Vehicle.Rect(-Edge, Edge, 0.2f, 4.2f), Under, Vector3.UnitZ, -Edge,
            seatSpots: [(-0.875f, 0.95f), (-0.875f, 2.45f)], mounts: [(-0.375f, 1.2f), (-0.375f, 2.95f)]);
        foreach (var (mount, seat) in v.Mounts["box"].Zip(box.Seats))
            v.Turret(mount, seat: seat);
        v.Stores(box, "shelf", 3).Stores(box, "chest", 1);
        var spot = new Vehicle.Rect(0.125f, Edge, 4.45f, 5.45f);
        v.Deck("porch", Under, new Vehicle.Rect(-Edge, Edge, 4.2f, 5.2f), "bed", [new Vehicle.Rect(0.125f, Edge, 4.2f, 4.45f)], whole: [spot]);
        v.FuelUnderFloor(v.TankOn(v.Wholes[("porch", spot)], new Vector3(1, 0, 4.5f)), 1);
        v.Hood();
        v.Trim("boxwalls", "hoodsides");
    }
}
