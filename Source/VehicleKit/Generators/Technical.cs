using System.Numerics;

namespace CallOfSlop.VehicleKit.Generators;

/// <summary>
/// The bandit technical: an armed pickup about 5 m long, a driver and a gunner (some seeds a third seat). Designed to be fought: an open driver's position, the
/// fuel drum feeding the engine stands exposed on the bed, nothing covers the tyres, and the crates in the bed are loose loot in a Cargo Volume.
/// Coordinates are <see cref="Vehicle"/>'s: y 0 is the rails' centre, about 0.5 m off the ground.
/// </summary>
public sealed class Technical(FactionStyle style) : VehicleShape(style)
{
    public override string Shape => "Technical";
    public override IReadOnlyList<int> Seeds => [7, 23, 41, 58];

    private const float BedTop = 0.625f, BedEdge = 0.875f, BedFront = 0.2f, BedBack = 2.45f;

    public override void Build(Vehicle v)
    {
        v.Description = "An armed pickup. Exposed fuel drum, open cab, loose crates.";
        v.Ladder(-1.675f, 2.75f, 0.625f, 1f).SteeredAxle().DrivenAxle().Powertrain(-1.425f);
        var d = v.Dice;
        v.OpenCab();

        // The bed on its sills, a hole for the turntable, one whole plate under the drum and the cargo.
        v.Sill(0.325f, layers: 2);
        var turret = v.Sill(1.075f, layers: 2);
        v.Sill(2.075f, layers: 2);
        var side = d.Fork("drum").Chance(0.5) ? -1f : 1f;
        var strong = new Vehicle.Rect(side < 0 ? -0.625f : -0.375f, side < 0 ? 0.375f : 0.625f, 1.45f, BedBack);
        var plate = v.Put("Plate Steel 1 m", new Vector3((strong.X0 + strong.X1) * 0.5f, BedTop + 0.031f, 1.95f), Orient.Facing(Vector3.UnitY), 0.03f);
        v.Deck("bed", BedTop, new Vehicle.Rect(-BedEdge, BedEdge, BedFront, BedBack), "bed", [new Vehicle.Rect(-0.375f, 0.375f, 0.7f, 1.45f), strong]);
        v.Panels["bed"].Add(plate);
        var drum = v.Put("Fuel Drum", new Vector3(side * 0.5f, BedTop + 0.5f, 2.325f), Orient.Forward, 0.1f, plate, "-y");
        v.FuelUnderFloor(drum, side);
        v.Turret(turret);
        // The loot rides over the strong plate's free half; the volume itself sits under the bed, out of the way of the rounds.
        var volume = v.Put("Cargo Volume", new Vector3(0, v.CentreOf(turret).Y, 1.3f), Orient.Forward, 0.1f, turret, "-z");
        var floor = BedTop + 0.07f;
        v.CargoBay(volume, new Vector3(side * -0.4f, BedTop + 0.5f, 1.95f), new Vector3(1, 1, 1), "loot", d.Fork("cargo").Int(2, 3),
            new Vector3(side * -0.65f, floor, 1.75f), new Vector3(side * -0.15f, floor, 2.1f), new Vector3(side * -0.65f, floor, 2.2f));

        // The bed's sides, its tailgate and the wall behind the cab.
        foreach (var s in new[] { -1f, 1f })
            v.Wall("sides", new Vector3(s, 0, 0), s * BedEdge, BedFront, BedBack, 0.505f, 0.5f, "sheet", 0.15);
        v.Wall("tail", Vector3.UnitZ, BedBack, side < 0 ? -0.125f : -BedEdge, side < 0 ? BedEdge : 0.125f, 0.505f, 0.5f, "sheet", 0.1); // (the drum stands open at the tail)
        v.Wall("headboard", -Vector3.UnitZ, BedFront, -BedEdge, BedEdge, 0.505f, 0.5f, "sheet");

        v.Hood();
        v.Trim("sides", "headboard", "hoodsides");
    }
}
