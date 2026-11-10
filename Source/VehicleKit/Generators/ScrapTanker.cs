using System.Numerics;

namespace CallOfSlop.VehicleKit.Generators;

/// <summary>A scrap tanker: a small cab, one big round tank up on cradles behind it, feeding the engine, and a gun between them. No room: a tanker is its tank.</summary>
public sealed class ScrapTanker(FactionStyle style) : HeavyShape(style)
{
    public override string Shape => "Scrap Tanker";
    public override IReadOnlyList<int> Seeds => [2, 4, 7];

    public override void Build(Vehicle v)
    {
        v.Description = "A scrap tanker. A small cab, a roof-high round tank of fuel out in the open, a gun behind the cab.";
        var d = v.Dice.Fork("shape");
        var barrel = d.Chance(0.5) ? 3f : 2f;
        Chassis(v, 4.25f, barrel > 2.5f ? 2.5f : 2.25f, "leaf", "hydraulic", "tandem", "dually", "six", "stilt");
        var front = v.RailZ(2.2f);
        var gun = v.Sill(v.RailZ(0.575f), layers: 2);
        for (var k = 0; k < barrel; k++) // (the tank rides on its own cradles)
            if (MathF.Abs(front + k - v.RearAxleZ) > 0.3f)
                v.Sill(v.RailZ(front + k), layers: 2);
        var tank = v.Stand(barrel > 2.5f ? "Tanker Barrel 3 m" : "Tanker Barrel 2 m", new Vector3(-0.25f, Vehicle.SillTop(2), v.RailZ(front)), Orient.Forward, null, "footL0", 0.03f);
        v.Link("pipe", tank, v.Engine!, laid: true);
        v.Tanks.Add(tank);
        v.Turret(gun);
        v.Hood();
        v.Trim("hoodsides");
    }
}

/// <summary>A tanker rig: the short cab and gun of a Scrap Tanker with no tank of its own, pulling a Fuel Trailer through a real hitch. Spawning one spawns both, coupled.</summary>
public sealed class TankerRig(FactionStyle style) : HeavyShape(style)
{
    public override string Shape => "Tanker Rig";
    public override IReadOnlyList<int> Seeds => [2, 10];

    public override void Build(Vehicle v)
    {
        v.Description = "A short rig pulling a Fuel Trailer on a tow hitch. A gun behind the cab, a drum on its back.";
        IVehicleGenerator trailer = new FuelTrailer(v.Style); // (as the interface: the style's shipped trailer seeds, so a committed rig tows a committed trailer)
        v.Tow = VehicleRegistry.BlueprintName(trailer, v.Dice.Fork("trailer").Pick(trailer.Seeds.ToArray()));
        Chassis(v, 3.25f, 1.25f, "leaf", "hydraulic", "dually", "swing");
        var gun = v.Sill(v.RailZ(0.575f), layers: 2);
        var seat = v.Sill(v.RailZ(1.5f), layers: 2);
        v.Deck("bed", 0.625f, new Vehicle.Rect(-0.875f, 0.875f, 0.2f, 2.2f), "floor", [new Vehicle.Rect(-0.375f, 0.375f, 0.2f, 0.95f)]);
        v.FuelUnderFloor(v.DrumOn(new Vector3(0.5f, 0.675f, 1.9f)), 1);
        v.Turret(gun);
        v.TowHitch();
        v.Hood();
        v.Trim("hoodsides");
    }
}

/// <summary>What a Tanker Rig pulls: a low frame on a free axle or a tandem, a round tank, a drawbar with a Tow Eye. It drives nothing and stands alone when unhitched.</summary>
public sealed class FuelTrailer(FactionStyle style) : HeavyShape(style)
{
    public override string Shape => "Fuel Trailer";
    public override IReadOnlyList<int> Seeds => [1, 2];
    public override VehicleCheckOptions? Checks => new() { Towed = true, MaxParts = 200, MaxMass = 8000 };

    public override void Build(Vehicle v)
    {
        v.Description = "A fuel trailer: a round tank on a sprung frame, with a drawbar's eye for a Tow Hitch.";
        var d = v.Dice.Fork("shape");
        var tandem = d.Chance(0.5);
        var (spring, wheel) = d.Pick(((string, string)[])[("Leaf Spring", "Off-Road Wheel"), ("Hydraulic Suspension", "Large Wheel"), ("Heavy Suspension", "Dually Wheel")]);
        v.Heavy().Ladder(0f, 3.25f, 0.625f, 1.25f, "Wide Differential").DrivenAxle(wheel, spring);
        if (tandem)
            v.TagAxle(v.RearAxleZ - 1.25f, spring, wheel);
        v.Drawbar();
        v.Sill(v.RailZ(1.075f), layers: 1);
        v.Sill(v.RailZ(2.075f), layers: 1);
        v.Stand("Tanker Barrel 2 m", new Vector3(-0.25f, Vehicle.SillTop(1), v.RailZ(1.075f)), Orient.Forward, null, "footL0", 0.03f);
        v.PaintAll(v.LeftRail.Concat(v.RightRail), "frame", d);
    }
}
