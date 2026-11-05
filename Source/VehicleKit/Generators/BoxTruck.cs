using System.Numerics;

namespace CallOfSlop.VehicleKit.Generators;

/// <summary>What the bigger shapes share: their chassis, a bigger limit in the check, one rear sill row. A shape is its proportions and where its room, turrets and tanks go.</summary>
public abstract class HeavyShape(FactionStyle style) : VehicleShape(style)
{
    protected const float Under = 0.625f, Edge = 1.125f; // a floor on two-layer sills, clear of the wheels; the odd eighth nearest 2.25 m wide
    public override VehicleCheckOptions? Checks => new() { MaxParts = 500, MaxMass = 16000, MinDistance = 15, MinTurn = 30, MinStability = 0.6f, MaxYawDrift = 15 };

    /// <summary>A plated porch behind a room at <paramref name="end"/> with the fuel drum on it, on one side, on a sill of its own.</summary>
    protected static void Porch(Vehicle v, float end, float side)
    {
        v.Sill(v.RailZ(end + 0.75f), layers: 2);
        var spot = side > 0 ? new Vehicle.Rect(0.125f, Edge, end + 0.25f, end + 1.25f) : new Vehicle.Rect(-Edge, -0.125f, end + 0.25f, end + 1.25f);
        var gap = side > 0 ? new Vehicle.Rect(0.125f, Edge, end, end + 0.25f) : new Vehicle.Rect(-Edge, -0.125f, end, end + 0.25f);
        v.Deck("porch", Under, new Vehicle.Rect(-Edge, Edge, end, end + 1f), "deck", [gap], whole: [spot]);
        v.FuelUnderFloor(v.TankOn(v.Wholes[("porch", spot)], new Vector3(side, 0, end + 0.3f)), side);
    }

    /// <summary>The chassis, cab and hood's engine. What the floor stands on is <see cref="Vehicle.Under"/>'s: each shape picks its own.</summary>
    protected static void Chassis(Vehicle v, float rails, float tail) =>
        v.Heavy().Ladder(-1.675f, rails, 0.625f, tail, frontWing: 0.5f).SteeredAxle().DrivenAxle().Powertrain(-1.425f).OpenCab();
}

/// <summary>An armoured box on a truck: a walk-in box with a rear door, shelves, and two roof guns worked from seats inside. The drum stands on the rear porch.</summary>
public sealed class BoxTruck(FactionStyle style) : HeavyShape(style)
{
    public override string Shape => "Box Truck";
    public override IReadOnlyList<int> Seeds => [1, 14, 16];

    public override void Build(Vehicle v)
    {
        v.Description = "An armoured box truck. Walk-in box with loot, two roof guns, a drum on the rear porch.";
        var d = v.Dice.Fork("shape");
        var length = d.Chance(0.5) ? 4f : 3f;
        var end = 0.2f + length;
        Chassis(v, length + 0.75f, 1.75f);
        v.Under(d.Pick((string[])["tub", "skirted"]), 0.2f, end, end);
        var chamfer = d.Chance(0.6); // (a shouldered roof wants its door in a side: an end door's corner stands up through the shoulder)
        var box = v.Room("box", new Vehicle.Rect(-Edge, Edge, 0.2f, end), Under, chamfer ? Vector3.UnitX : Vector3.UnitZ, chamfer ? 1.2f : -Edge,
            seatSpots: [(-0.75f, 0.575f), (-0.75f, length > 3.5f ? 3.325f : 2.575f)], mounts: [(-0.375f, 0.7f), (-0.375f, length > 3.5f ? 2.95f : 2.2f)], chamfer: chamfer);
        foreach (var (mount, seat) in v.Mounts["box"].Zip(box.Seats))
            v.Turret(mount, seat: seat);
        v.Furnish(box);
        Porch(v, end, 1);
        v.Hood();
        v.Trim("boxwalls", "hoodsides");
    }
}
