using System.Numerics;

namespace CallOfSlop.VehicleKit.Generators;

/// <summary>A scrap tanker: a crew cabin behind the cab and big fuel tanks in the open along the rails, all feeding the engine.</summary>
public sealed class ScrapTanker(FactionStyle style) : HeavyShape(style)
{
    public override string Shape => "Scrap Tanker";

    public override void Build(Vehicle v)
    {
        v.Description = "A scrap tanker. A crew cabin, a roof gun and big tanks of fuel out in the open.";
        Chassis(v, 4.75f, 1.75f, 0.325f, 1.325f, 2.825f, 3.825f, 4.825f);
        var cabin = v.Room("cabin", new Vehicle.Rect(-Edge, Edge, 0.2f, 2.2f), Under, Vector3.UnitZ, -0.125f, seatSpots: [(-0.875f, 0.45f)], mounts: [(-0.375f, 0.95f)]);
        v.Turret(v.Mounts["cabin"][0], seat: cabin.Seats[0]);
        v.Stores(cabin, "shelf", 2).Stores(cabin, "chest", 1);
        var spot = new Vehicle.Rect(0.125f, Edge, 4.45f, 5.45f);
        v.Deck("walkway", Under, new Vehicle.Rect(-Edge, Edge, 2.2f, 5.2f), "bed", [new Vehicle.Rect(-0.625f, 0.125f, 2.2f, 5.2f), new Vehicle.Rect(0.125f, Edge, 2.2f, 2.45f)], whole: [spot]);
        v.FuelUnderFloor(v.TankOn(v.Wholes[("walkway", spot)], new Vector3(1, 0, 4.5f)), 1);
        foreach (var z in new[] { 2.825f, 3.825f })
            v.Link("pipe", v.Stand(v.Style.Part(v.Dice.Fork("tanks"), "tank"), new Vector3(-0.25f, Vehicle.SillTop(2), z), Orient.Forward, null, "-y"), v.Engine!, laid: true);
        v.Hood();
        v.Trim("cabinwalls", "hoodsides");
    }
}
