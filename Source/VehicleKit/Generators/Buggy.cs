namespace CallOfSlop.VehicleKit.Generators;

/// <summary>
/// Body styles phase 8b (docs/vehiclekit/BODY-STYLES.md, section 18): an open-top 4x4 gun buggy: a caged two-seat tub on chunky long-travel wheels, the gun on a ring behind the cage, the drum on a rear porch. Made by the shape grammar only (its recipe, "Buggy"), in its style's
/// archetype (<see cref="BodyArchetype"/>); an extra shape, shipped by the styles that name it.
/// </summary>
public sealed class Buggy(FactionStyle style) : VehicleShape(style)
{
    public override string Shape => "Buggy";
    public override VehicleCheckOptions? Checks => new() { MaxParts = 500, MaxMass = 16000, MinDistance = 15, MinTurn = 30, MinStability = 0.6f, MaxYawDrift = 15 };
    public override IReadOnlyList<int> Seeds => [1, 2, 3];

    public override void Build(Vehicle v) => throw new VehicleKitException("the Buggy is made by the shape grammar only (kit 2 and later)");
}
