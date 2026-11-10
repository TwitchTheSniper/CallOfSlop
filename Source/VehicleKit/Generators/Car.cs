namespace CallOfSlop.VehicleKit.Generators;

/// <summary>
/// Body styles phase 8a (docs/vehiclekit/BODY-STYLES.md, section 16): a car in one of its style's archetypes (an early fastback, a coke-bottle, a seventies wedge, a
/// supercar, a compact hatch, an estate): a lofted cab in the archetype's language, its hood, and a short gun bed behind. Made by the shape grammar only (its
/// recipe, "Car"); an extra shape, shipped by the styles that name it.
/// </summary>
public sealed class Car(FactionStyle style) : VehicleShape(style)
{
    public override string Shape => "Car";
    public override VehicleCheckOptions? Checks => new() { MaxParts = 500, MaxMass = 16000, MinDistance = 15, MinTurn = 30, MinStability = 0.6f, MaxYawDrift = 15 };
    public override IReadOnlyList<int> Seeds => [1, 2, 3];

    public override void Build(Vehicle v) => throw new VehicleKitException("the Car is made by the shape grammar only (kit 2 and later)");
}
