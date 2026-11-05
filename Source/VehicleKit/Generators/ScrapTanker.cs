using System.Numerics;

namespace CallOfSlop.VehicleKit.Generators;

/// <summary>A scrap tanker: a crew cabin behind the cab and big fuel tanks in the open along the rails, all feeding the engine.</summary>
public sealed class ScrapTanker(FactionStyle style) : HeavyShape(style)
{
    public override string Shape => "Scrap Tanker";
    public override IReadOnlyList<int> Seeds => [1, 4, 22];

    public override void Build(Vehicle v)
    {
        v.Description = "A scrap tanker. A crew cabin, a roof gun and a big round tank of fuel out in the open.";
        var d = v.Dice.Fork("shape");
        var barrel = d.Chance(0.5) ? 3f : 2f;
        Chassis(v, 4.75f, barrel > 2.5f ? 3.25f : 2.75f);
        v.Under(d.Pick((string[])["tub", "skirted"]), 0.2f, 3.2f, 3.2f);
        var cabin = v.Room("cabin", new Vehicle.Rect(-Edge, Edge, 0.2f, 3.2f), Under, Vector3.UnitX, 1.2f, seatSpots: [(-0.75f, 0.575f)], mounts: [(-0.375f, 0.95f)], chamfer: true);
        v.Turret(v.Mounts["cabin"][0], seat: cabin.Seats[0]);
        v.Furnish(cabin);
        var front = v.RailZ(barrel > 2.5f ? 3.95f : 3.7f);
        for (var k = 0; k < barrel; k++) // (the tank sits up on its own cradles, clear of the rear wheels: the stilts are what a tanker looks like)
            v.Sill(front + k, layers: 2);
        var tank = v.Stand(barrel > 2.5f ? "Tanker Barrel 3 m" : "Tanker Barrel 2 m", new Vector3(-0.25f, Vehicle.SillTop(2), front), Orient.Forward, null, "footL0", 0.03f);
        v.Link("pipe", tank, v.Engine!, laid: true);
        v.Hood();
        v.Trim("cabinwalls", "hoodsides");
    }
}
