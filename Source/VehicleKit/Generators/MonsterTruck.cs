namespace CallOfSlop.VehicleKit.Generators;

/// <summary>
/// Body styles phase 8b (docs/vehiclekit/BODY-STYLES.md, section 18): a monster truck: a short body high on stilts over four huge wheels, an open bed (or a gun bed) behind the cab. Made by the shape grammar only (its recipe, "Monster Truck"), in its style's
/// archetype (<see cref="BodyArchetype"/>); an extra shape, shipped by the styles that name it.
/// </summary>
public sealed class MonsterTruck(FactionStyle style) : VehicleShape(style)
{
    public override string Shape => "Monster Truck";
    public override VehicleCheckOptions? Checks => new() { MaxParts = 500, MaxMass = 16000, MinDistance = 15, MinTurn = 30, MinStability = 0.55f, MaxYawDrift = 15 };
    public override IReadOnlyList<int> Seeds => [1, 2, 3];

    public override void Build(Vehicle v) => throw new VehicleKitException("the Monster Truck is made by the shape grammar only (kit 2 and later)");
}
