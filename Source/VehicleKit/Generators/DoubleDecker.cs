using System.Numerics;

namespace CallOfSlop.VehicleKit.Generators;

/// <summary>
/// A double-decker bus: a long walk-in hull below (side door, loot, the seats that work the guns) and an open top deck on its roof, railed,
/// with the roof guns standing up through it. The top deck stops where the tail slopes down; a ladder hangs from its back edge, and the
/// rear platform, where the drum stands, is a step up to it. Only a style that names it in <see cref="FactionStyle.Extra"/> ships it (the British style).
/// </summary>
public sealed class DoubleDecker(FactionStyle style) : HeavyShape(style)
{
    public override string Shape => "Double Decker";
    public override IReadOnlyList<int> Seeds => [1, 2, 3];

    public override void Build(Vehicle v)
    {
        v.Description = "A double-decker bus. A long hull below with a side door and loot, an open top deck above with a rail and the guns, a ladder up from the rear platform.";
        var d = v.Dice.Fork("shape");
        var body = d.Pick((string[][])[["Brow T", "Door", "Bay T", "Bay T", "Bay T", "Tail T"], ["Brow L", "Door", "Bay T", "Bay T", "Bay T", "Tail L"], ["Brow T", "Bay T", "Door", "Bay T", "Bay T", "Tail T"]]);
        var length = 6.5f;
        var end = 0.2f + length;
        Chassis(v, length + 0.25f, 2.5f, "leaf", "hydraulic", "tandem", "six", "dually");
        v.Under(Underside(v, d), 0.2f, end);
        var bus = v.Hull("bus", 0.2f, v.FloorY, body, [(-0.5f, 0.575f), (-0.5f, 4.575f), (-0.5f, end - 0.875f)]);
        v.FedTurrets = true;
        // The roof guns, on the first and last tall bays: they stand up through the top deck, worked from the seats below.
        var tall = Enumerable.Range(0, body.Length).Where(k => body[k] is "Door" or "Bay T").ToArray();
        var (front, back) = (bus.Bays[tall[0]], bus.Bays[tall[^1]]);
        v.Turret(front, seat: bus.Seats[0]);
        v.Turret(back, seat: bus.Seats[2], aft: true);
        var waist = Enumerable.Range(0, body.Length).First(k => body[k] == "Bay T" && bus.Bays[k] != front && bus.Bays[k] != back);
        v.SideGun(bus.Bays[waist], -1, bus.Seats[1]);
        Magazine(v, bus, 0.2f);
        v.Furnish(bus);
        // The top deck: on the tall bays' roofs (their "top" sockets sit on its cells' centres), holes round the guns.
        var roof = bus.FloorTop + front.Def.BoundsMax.Y;
        var z0 = v.CentreOf(front).Z - (front.Def.BoundsMax.Z - front.Def.BoundsMin.Z) / 2 + 0.125f;
        var z1 = z0 + 0.25f * MathF.Floor((v.CentreOf(back).Z + (back.Def.BoundsMax.Z - back.Def.BoundsMin.Z) / 2 - z0) / 0.25f); // (it stops where the tail slopes down: a stepped roof)
        var holes = new[] { front, back }.Select(b => new Vehicle.Rect(-0.375f, 0.375f, v.CentreOf(b).Z - 0.375f, v.CentreOf(b).Z + 0.375f)).ToArray();
        v.Deck("topdeck", roof, new Vehicle.Rect(-Edge, Edge, z0, z1), "deck", holes);
        foreach (var s in new[] { -1f, 1f })
            v.TubeRails(s * Edge, z0 + 0.125f, z1, roof, s);
        v.Wall("toprails", -Vector3.UnitZ, z0, -Edge, Edge, roof - 0.12f, 0.5f, "sheet");
        if (!v.Boarding(new Vector3(-0.5f, roof, z1 + 0.1f), "toprails")) // (up from the tail's roof, else down the kerb side)
            v.Boarding(new Vector3(Edge + 0.1f, roof, z1 - 0.5f), "toprails");
        Porch(v, end, 1);
        v.Hood();
        v.Trim("buswalls", "toprails", "hoodsides");
    }
}
