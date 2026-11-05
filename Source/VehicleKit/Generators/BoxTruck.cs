using System.Numerics;

namespace CallOfSlop.VehicleKit.Generators;

/// <summary>What the bigger shapes share: their chassis, a bigger limit in the check, one rear sill row. A shape is its proportions and where its room, turrets and tanks go.</summary>
public abstract class HeavyShape(FactionStyle style) : VehicleShape(style)
{
    protected const float Under = 0.625f, Edge = 1.125f; // a floor on two-layer sills, clear of the wheels; the odd eighth nearest 2.25 m wide
    public override VehicleCheckOptions? Checks => new() { MaxParts = 500, MaxMass = 16000, MinDistance = 15, MinTurn = 30, MinStability = 0.6f, MaxYawDrift = 15 };

    /// <summary>A plated porch behind a body at <paramref name="end"/> with the fuel drum on it, on one side, on a sill of its own.</summary>
    protected static void Porch(Vehicle v, float end, float side)
    {
        v.Sill(v.RailZ(end + 0.75f), layers: Vehicle.SillsFor(v.FloorY));
        var spot = side > 0 ? new Vehicle.Rect(0.125f, Edge, end + 0.25f, end + 1.25f) : new Vehicle.Rect(-Edge, -0.125f, end + 0.25f, end + 1.25f);
        var gap = side > 0 ? new Vehicle.Rect(0.125f, Edge, end, end + 0.25f) : new Vehicle.Rect(-Edge, -0.125f, end, end + 0.25f);
        v.Deck("porch", v.FloorY, new Vehicle.Rect(-Edge, Edge, end, end + 1f), "deck", [gap], whole: [spot]);
        v.FuelUnderFloor(v.TankOn(v.Wholes[("porch", spot)], new Vector3(side, 0, end + 0.3f)), side);
    }

    /// <summary>A body's magazine, on its floor by the front bay: two crates of different rounds, a switch and a lever, feeding its guns. The crates stay in the armour; the feeds cross the room.</summary>
    protected static void Magazine(Vehicle v, Room room, float z0)
    {
        var y = room.FloorTop + 0.2f;
        var crates = (new Vector3(0.5f, y, z0 + 0.625f), new Vector3(0f, y, z0 + 0.625f));
        var guns = v.Turrets.Select(t => t.Gun).ToArray();
        v.Feeds(crates.Item1, crates.Item2, "FMJ", v.Dice.Fork("rounds").Pick((string[])["Armour Piercing", "Explosive Tip", "Dragon's Breath"]), room.Bays[0], "lever", guns[0], guns[1..]);
    }

    /// <summary>The chassis on a rolled recipe: frame, axles, powertrain and cab. Rails and tail set the wheelbase; what the floor stands on is <see cref="Vehicle.Under"/>'s.</summary>
    protected static Gear Chassis(Vehicle v, float rails, float tail, params string[] only)
    {
        v.Heavy();
        var g = v.PickGear(only);
        v.FloorY = g.Floor;
        v.Ladder(-1.675f, rails, 0.625f, tail, g.Differential, frontWing: g.FrontWing).SteeredAxle(g.FrontWheel, g.FrontSpring).DrivenAxle(g.RearWheel, g.RearSpring).Powertrain(-1.425f).OpenCab();
        if (g.Tag != null)
            v.TagAxle(v.RearAxleZ + 1.25f, g.Tag, g.TagWheel ?? g.RearWheel);
        return g;
    }

    /// <summary>What a low floor stands on is always its sills; a high floor rolls a closed tub or a skirt.</summary>
    protected static string Underside(Vehicle v, Dice d) => v.FloorY < 0.5f ? "stilts" : d.Pick((string[])["tub", "skirted"]);
}

/// <summary>An armoured box on a truck: a walk-in box with a rear door, shelves, and two roof guns worked from seats inside. The drum stands on the rear porch.</summary>
public sealed class BoxTruck(FactionStyle style) : HeavyShape(style)
{
    public override string Shape => "Box Truck";
    public override IReadOnlyList<int> Seeds => [1, 14, 16];

    public override void Build(Vehicle v)
    {
        v.Description = "An armoured box truck. A stepped hull with loot, roof and side guns, a drum on the rear porch.";
        var d = v.Dice.Fork("shape");
        string[][] bodies =
        [
            ["Brow T", "Door", "Bay T-L", "Tail L"],      // tall ahead, stepped down at the back
            ["Brow L", "Bay L-T", "Door", "Tail T"],      // a low front rising to a tall back
            ["Brow N", "Bay N-T", "Door", "Tail T"],      // a narrow nose widening out
            ["Brow T", "Door", "Tail T"],                 // short
        ];
        var body = d.Pick(bodies);
        var length = body.Length == 3 ? 3.5f : 4.5f;
        var end = 0.2f + length;
        Chassis(v, length + 0.25f, 2.5f);
        v.Under(Underside(v, d), 0.2f, end);
        var box = v.Hull("box", 0.2f, v.FloorY, body, [(-0.5f, 0.575f), (-0.5f, end - 0.875f)]);
        v.Turret(box.Bays[0], seat: box.Seats[0]);
        v.SideGun(box.Bays[^2], -1, box.Seats[1]);
        Magazine(v, box, 0.2f);
        v.Furnish(box);
        v.Panniers(box.Bays, d.Fork("panniers"));
        Porch(v, end, 1);
        v.Hood();
        v.Trim("boxwalls", "hoodsides");
    }
}
