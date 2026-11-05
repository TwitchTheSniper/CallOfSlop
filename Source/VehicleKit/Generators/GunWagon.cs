using System.Numerics;

namespace CallOfSlop.VehicleKit.Generators;

/// <summary>A gun wagon: a short crew room up front, an open bed behind it with two deck guns, the drum at the tail.</summary>
public sealed class GunWagon(FactionStyle style) : HeavyShape(style)
{
    public override string Shape => "Gun Wagon";
    public override IReadOnlyList<int> Seeds => [2, 3, 19];

    public override void Build(Vehicle v)
    {
        v.Description = "A gun wagon. A cab, an armoured ammo hutch and a stern gun on a flat deck, the drum at the tail.";
        var d = v.Dice.Fork("shape");
        Chassis(v, 3.75f, 2.25f);
        v.Under(Underside(v, d), 0.2f, 1.2f);
        var deckY = v.FloorY;
        var gunZ = v.RailZ(2.825f);
        var turret = v.Sill(gunZ, layers: Vehicle.SillsFor(deckY));
        v.Sill(v.RailZ(1.325f), layers: Vehicle.SillsFor(deckY));
        var spot = new Vehicle.Rect(0.375f, 1.375f, 3.45f, 4.45f);
        v.Deck("bed", deckY, new Vehicle.Rect(-Edge, Edge, 0.2f, 4.2f), "floor", [new Vehicle.Rect(-0.375f, 0.375f, gunZ - 0.375f, gunZ + 0.375f)], whole: [spot]);
        v.FuelUnderFloor(v.TankOn(v.Wholes[("bed", spot)], new Vector3(1, 0, 3.5f)), 1);
        v.FedTurrets = true;
        v.Turret(turret, aft: d.Chance(0.5));
        var hutch = v.Stand("Ammo Hutch", new Vector3(-0.5f, deckY + 0.05f, 1.075f), Orient.Forward, null, "foot00", 0.2f);
        v.Feeds(new Vector3(-0.25f, deckY + 0.25f, 1.325f), new Vector3(0.25f, deckY + 0.25f, 1.325f), "FMJ", v.Dice.Fork("rounds").Pick((string[])["Armour Piercing", "Explosive Tip", "Dragon's Breath"]), hutch, "top", new Vector3(0, deckY + 0.25f, 1.85f), v.Gun!);
        v.Hood();
        v.Trim("hoodsides");
    }
}
