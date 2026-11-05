using System.Numerics;

namespace CallOfSlop.VehicleKit.Generators;

/// <summary>
/// The technical: an armed pickup, a driver and a gunner. Three trucks of one family, by the seed: a stepside pickup on a low bed between wide-set wheels, a long flatbed ute on a closed tub
/// with tube rails, a short sand rail with nothing behind the cage but a gun and a drum. Hood, nose, fenders, cab and wheels vary on top of that.
/// Designed to be fought: an exposed drum feeds the engine, nothing covers the rear tyres on two of the three, the crew sit in the open.
/// </summary>
public sealed class Technical(FactionStyle style) : VehicleShape(style)
{
    public override string Shape => "Technical";
    public override VehicleCheckOptions? Checks => new() { MinStability = 0.72f }; // (a gun on a pedestal rides high, as a real one does)
    public override IReadOnlyList<int> Seeds => [1, 6, 10, 13, 23];

    public override void Build(Vehicle v)
    {
        v.Description = "An armed pickup. Exposed fuel drum, open cab, loose crates.";
        var body = v.Dice.Fork("body").Pick((string[])["stepside", "ute", "rail"]);
        var (rails, tail, wide) = body switch { "stepside" => (3.25f, 1f, true), "ute" => (3.75f, 1.25f, false), _ => (2.75f, 0.75f, false) };
        var gear = v.PickGear();
        v.Ladder(-1.675f, rails, 0.625f, tail, wide ? "Wide Differential" : null).SteeredAxle(gear.FrontWheel, gear.FrontSpring).DrivenAxle(gear.RearWheel, gear.RearSpring).Powertrain(-1.425f);
        v.OpenCab();
        var d = v.Dice;
        switch (body)
        {
            case "stepside": Stepside(v, d); break;
            case "ute": Ute(v, d); break;
            default: Rail(v, d); break;
        }
        v.Hood();
        v.Trim("sides", "headboard", "hoodsides");
    }

    /// <summary>A low bed on the rails between the wheels, pickup sides over them, a gun on a sill through the bed.</summary>
    private static void Stepside(Vehicle v, Dice d)
    {
        float z0 = 0.45f, z1 = v.TailZ, mid = v.RailZ(v.RearAxleZ - 0.25f), gun = v.RailZ(0.9f);
        var turret = v.Sill(gun);
        var side = d.Fork("drum").Chance(0.5) ? -1f : 1f;
        v.Deck("bed", Vehicle.Half, new Vehicle.Rect(-0.625f, 0.625f, z0, z1), "floor", [new Vehicle.Rect(-0.375f, 0.375f, gun - 0.325f, gun + 0.425f)]);
        foreach (var s in new[] { -1f, 1f })
            v.Sides(new Vector3(s * 0.65f, 0.3f, mid), s);
        v.FuelUnderFloor(v.DrumOn(new Vector3(-side * 0.3f, 0.19f, z1 - 0.4f)), -side);
        v.Turret(turret);
        v.Crates(turret, new Vector3(side * 0.3f, 0.19f, gun + 0.9f), new Vector3(0.6f, 1, 1.2f), 0.2f, d.Fork("cargo"));
        v.Wall("tail", Vector3.UnitZ, z1, -0.125f, 0.625f, 0.005f, 0.5f, "sheet", 0.1);
    }

    /// <summary>A long flat bed on a tub with tube rails along it.</summary>
    private static void Ute(Vehicle v, Dice d)
    {
        const float top = 0.625f, edge = 0.875f;
        float z1 = v.TailZ, gun = v.RailZ(1.075f);
        v.Under("tub", 0.2f, gun - 0.25f);
        var turret = v.Sill(gun, layers: 2);
        v.Under("tub", gun + 0.25f, v.RearAxleZ - 0.25f);
        v.Sill(v.RailZ(v.RearAxleZ + 0.625f), layers: 2);
        var side = d.Fork("drum").Chance(0.5) ? -1f : 1f;
        v.Deck("bed", top, new Vehicle.Rect(-edge, edge, 0.2f, z1), "floor", [new Vehicle.Rect(-0.375f, 0.375f, gun - 0.375f, gun + 0.375f)]);
        foreach (var s in new[] { -1f, 1f })
            v.TubeRails(s * edge, 0.45f, z1 - 0.1f, top - 0.05f, s);
        v.FuelUnderFloor(v.DrumOn(new Vector3(side * 0.5f, top + 0.05f, z1 - 0.4f)), side);
        v.Turret(turret);
        v.Crates(turret, new Vector3(-side * 0.4f, top + 0.05f, gun + 0.95f), new Vector3(1, 1, 1.2f), top + 0.07f, d.Fork("cargo"));
    }

    /// <summary>A short rail: no bed, a low deck for the gunner, the drum in plain sight.</summary>
    private static void Rail(Vehicle v, Dice d)
    {
        var gun = v.RailZ(0.825f);
        var turret = v.Sill(gun);
        v.Deck("bed", Vehicle.Half, new Vehicle.Rect(-0.375f, 0.375f, gun + 0.375f, v.TailZ), "floor");
        var side = d.Fork("drum").Chance(0.5) ? -1f : 1f;
        v.FuelUnderFloor(v.DrumOn(new Vector3(0, 0.19f, v.TailZ - 0.35f)), side);
        v.Turret(turret);
    }
}
