namespace CallOfSlop.VehicleKit.Generators;

/// <summary>
/// Body styles phase 8b (docs/vehiclekit/BODY-STYLES.md, section 18): a giant truck: the giant scale class (a land train's or a mobile base's room), wider and taller than a heavy truck, on two or three axles. Made by the shape grammar only (its recipe, "Giant Truck"), in its style's
/// archetype (<see cref="BodyArchetype"/>); an extra shape, shipped by the styles that name it.
/// </summary>
public sealed class GiantTruck(FactionStyle style) : VehicleShape(style)
{
    public override string Shape => "Giant Truck";
    public override VehicleCheckOptions? Checks => new() { MaxParts = 500, MaxMass = 30000, MinDistance = 12, MinTurn = 20, MinStability = 0.6f, MaxYawDrift = 15 };
    public override IReadOnlyList<int> Seeds => [1, 2, 3];

    public override void Build(Vehicle v) => throw new VehicleKitException("the Giant Truck is made by the shape grammar only (kit 2 and later)");
}
