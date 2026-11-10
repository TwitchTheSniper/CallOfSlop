namespace CallOfSlop.VehicleKit.Generators;

/// <summary>
/// Body styles phase 8b (docs/vehiclekit/BODY-STYLES.md, section 18): a fantasy wagon: a war wagon under a hooped canvas top, a carriage with a bowed body, a totem cart; a room with a crew (and a bed behind it now and then). Made by the shape grammar only (its recipe, "Wagon"), in its style's
/// archetype (<see cref="BodyArchetype"/>); an extra shape, shipped by the styles that name it.
/// </summary>
public sealed class Wagon(FactionStyle style) : VehicleShape(style)
{
    public override string Shape => "Wagon";
    public override VehicleCheckOptions? Checks => new() { MaxParts = 500, MaxMass = 16000, MinDistance = 15, MinTurn = 30, MinStability = 0.6f, MaxYawDrift = 15 };
    public override IReadOnlyList<int> Seeds => [1, 2, 3];

    public override void Build(Vehicle v) => throw new VehicleKitException("the Wagon is made by the shape grammar only (kit 2 and later)");
}
