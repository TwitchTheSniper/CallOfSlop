using System.Numerics;

namespace CallOfSlop.VehicleKit.Generators;

/// <summary>A scrap tanker: a small cab, one big round tank up on cradles behind it, feeding the engine, and a gun between them. No room: a tanker is its tank.</summary>
public sealed class ScrapTanker(FactionStyle style) : HeavyShape(style)
{
    public override string Shape => "Scrap Tanker";
    public override IReadOnlyList<int> Seeds => [1, 4, 22];

    public override void Build(Vehicle v)
    {
        v.Description = "A scrap tanker. A small cab, a roof-high round tank of fuel out in the open, a gun behind the cab.";
        var d = v.Dice.Fork("shape");
        var barrel = d.Chance(0.5) ? 3f : 2f;
        Chassis(v, 4.25f, barrel > 2.5f ? 2.5f : 1.5f);
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
