using System.Numerics;

namespace CallOfSlop.VehicleKit.Generators;

/// <summary>A gun wagon: a short crew room up front, an open bed behind it with two deck guns, the drum at the tail.</summary>
public sealed class GunWagon(FactionStyle style) : HeavyShape(style)
{
    public override string Shape => "Gun Wagon";
    public override IReadOnlyList<int> Seeds => [1, 5, 9];

    public override void Build(Vehicle v)
    {
        v.Description = "A gun wagon. A cab, an armoured ammo hutch and a stern gun on a flat deck, the drum at the tail.";
        var d = v.Dice.Fork("shape");
        Chassis(v, 3.75f, 1.75f);
        v.Under(Underside(v, d), 0.2f, 1.2f);
        var deckY = v.FloorY;
        var gunZ = v.RailZ(2.825f);
        var turret = v.Sill(gunZ, layers: Vehicle.SillsFor(deckY));
        v.Sill(v.RailZ(1.325f), layers: Vehicle.SillsFor(deckY));
        var spot = new Vehicle.Rect(0.375f, 1.375f, 3.45f, 4.45f);
        v.Deck("bed", deckY, new Vehicle.Rect(-Edge, Edge, 0.2f, 4.2f), "floor", [new Vehicle.Rect(-0.375f, 0.375f, gunZ - 0.375f, gunZ + 0.375f)], whole: [spot]);
        v.FuelUnderFloor(v.TankOn(v.Wholes[("bed", spot)], new Vector3(1, 0, 3.5f)), 1);
        v.Turret(turret);
        v.Stand("Ammo Hutch", new Vector3(-0.5f, deckY + 0.05f, 0.95f), Orient.Forward, null, "foot00", 0.2f);
        v.Hood();
        v.Trim("hoodsides");
    }
}
