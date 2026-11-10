namespace CallOfSlop.VehicleKit.Generators;

/// <summary>
/// Body styles phase 8b (docs/vehiclekit/BODY-STYLES.md, section 18): a one-off concept vehicle: a bubble-canopy car, a single-seat streamliner; a cabin, its hood and a boot over the tank, the gun on a pedestal. Made by the shape grammar only (its recipe, "Concept"), in its style's
/// archetype (<see cref="BodyArchetype"/>); an extra shape, shipped by the styles that name it.
/// </summary>
public sealed class Concept(FactionStyle style) : VehicleShape(style)
{
    public override string Shape => "Concept";
    public override VehicleCheckOptions? Checks => new() { MaxParts = 500, MaxMass = 16000, MinDistance = 15, MinTurn = 30, MinStability = 0.6f, MaxYawDrift = 15 };
    public override IReadOnlyList<int> Seeds => [1, 2, 3];

    public override void Build(Vehicle v) => throw new VehicleKitException("the Concept is made by the shape grammar only (kit 2 and later)");
}
