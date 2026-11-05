using System.Numerics;

namespace CallOfSlop.VehicleKit.Generators;

/// <summary>A flatbed fort: an open deck with two guns on it and loose crates, a small bunker at the back of the deck.</summary>
public sealed class FlatbedFort(FactionStyle style) : HeavyShape(style)
{
    public override string Shape => "Flatbed Fort";
    public override IReadOnlyList<int> Seeds => [1, 8, 9];

    public override void Build(Vehicle v)
    {
        v.Description = "A flatbed fort. Two deck guns, walled sides, a small bunker at the back of the deck.";
        var d = v.Dice.Fork("shape");
        Chassis(v, 4.75f, 1.75f, "leaf", "hydraulic", "tandem");
        v.Under("stilts", 0.2f, 5.2f); // (a fort stands tall on purpose: the deck is the point)
        var deck = new Vehicle.Rect(-Edge, Edge, 0.2f, 2.2f);
        var gun = v.Sill(0.825f, layers: 2);
        var spot = new Vehicle.Rect(0.375f, 1.375f, 0.95f, 1.95f);
        v.Deck("deck", Under, deck, "bed", [new Vehicle.Rect(-0.375f, 0.375f, 0.45f, 1.2f), new Vehicle.Rect(0.125f, Edge, 1.95f, 2.2f)], whole: [spot]);
        var bunker = v.Hull("bunker", 2.2f, Under, d.Pick((string[][])[["Bay L-T", "Door", "Tail T"], ["Brow L", "Door", "Bay T-L"]]) , [(-0.5f, 3.325f)]);
        v.Turret(gun);
        v.Turret(bunker.Bays[^1], seat: bunker.Seats[0]);
        v.Furnish(bunker);
        v.FuelUnderFloor(v.TankOn(v.Wholes[("deck", spot)], new Vector3(1, 0, 1)), 1);
        foreach (var s in new[] { -1f, 1f })
            v.Wall("sides", new Vector3(s, 0, 0), s * Edge, 0.2f, 2.2f, Under - 0.12f, 0.5f, "sheet", 0.2);
        v.Boarding(new Vector3(-Edge - 0.1f, Under, 1.45f), "sides");
        v.Hood();
        v.Trim("sides", "bunkerwalls");
    }
}
