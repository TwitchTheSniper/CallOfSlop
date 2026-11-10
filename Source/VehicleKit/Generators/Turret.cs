using System.Numerics;

namespace CallOfSlop.VehicleKit.Generators;

/// <summary>
/// The standalone turret generator (docs/sandbox/VEHICLE-WEAPONS.md, "Turrets"): a random complete turret from a seed, a mount (a steel base plate, its turntable and elevation
/// hinge), a gun and the gunner's seat with its Turret Module, through the kit's own <see cref="Vehicle.Turret"/>. The gun is the gunsmith's for a mount rated as if it stood on a
/// vehicle of 0.8 to 16 t (by the seed) at a tier of 1 to 4, or one of the style's own, so a seed gives anything from a pintle chain gun to a siege Howitzer. It drives nothing:
/// checked as a towed thing is (its build, its faces, how it stands). Every shipped style ships its seeds (<see cref="VehicleRegistry.Everywhere"/>): a creative spawn tile
/// ("New seed" makes another). Survival loot is a next step (VEHICLE-WEAPONS.md).
/// </summary>
public sealed class Turret(FactionStyle style) : VehicleShape(style)
{
    public override string Shape => "Turret";

    public override VehicleCheckOptions? Checks => new() { Towed = true, MinMass = 40, MaxMass = 6000, MaxParts = 60, MaxSideFill = 1, MaxTopFill = 1, MinStability = 0 };

    public override IReadOnlyList<int> Seeds => [1, 2];

    /// <summary>The share of seeds that take one of the style's own guns rather than the gunsmith's.</summary>
    public const double StyleGunShare = 0.25;

    public override void Build(Vehicle v)
    {
        var d = v.Dice.Fork("turret");
        // The mount's rating: as if it stood on a vehicle of this mass (log-even from 0.8 to 16 t), at this tier.
        var rated = (float)(800 * Math.Pow(20, d.Fork("rating").Next()));
        var tier = d.Fork("tier").Int(1, 4);
        var offer = VehicleGunsmith.On ? VehicleGunsmith.Offer(rated, tier) : [];
        var weapon = offer.Count == 0 || d.Fork("own gun").Chance(StyleGunShare) ? d.Fork("gun").Pick(Style.Weapons) : d.Fork("gun").Pick(offer);
        const string plate = "Plate Steel 1 m"; // (a 2 m plate has no socket at its middle for the turntable; a heavy gun on this one may tip it: see VEHICLE-WEAPONS.md, next steps)
        var root = v.Root(plate, new Vector3(0, 2f, 0), Orient.Facing(Vector3.UnitY)); // (where the gunsmith's test stand fits it: on the ground a 4x4 block's receivers didn't merge; it is set down on the ground when spawned)
        var paint = Style.Colour(d.Fork("paint"), "body");
        v.Paint(root, paint);
        // Outriggers: a plate out on each side, so a heavy gun's weight up on the arm doesn't tip a 1 m base past the 8 degree stand check.
        foreach (var (side, other) in ((string, string)[])[("-x1", "+x1"), ("+x1", "-x1"), ("-z1", "+z1"), ("+z1", "-z1")])
            try
            {
                v.Paint(v.Onto(root, side, plate, other, 0), paint);
            }
            catch (VehicleKitException)
            {
                // (that side won't take it: the base goes on without)
            }
        v.Turret(root, weapon);
        v.Description = $"A {Style.Name} turret: a {weapon.Name} on a turntable, its gunner's seat on the arm. Sit in it to aim and fire; weld or bolt its base to anything.";
    }
}
