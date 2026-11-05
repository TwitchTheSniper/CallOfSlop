using System.Numerics;

namespace CallOfSlop.VehicleKit.Generators;

/// <summary>
/// The bandit technical: an armed pickup about 5 m long, a driver and a gunner (some seeds a third seat). Designed to be fought: an open driver's position, the
/// fuel drum feeding the engine stands exposed on the bed, nothing covers the tyres, and the crates in the bed are loose loot in a Cargo Volume.
/// Coordinates are <see cref="Vehicle"/>'s: y 0 is the rails' centre, about 0.5 m off the ground.
/// </summary>
public sealed class BanditTechnical : IVehicleGenerator
{
    public string Name => "Bandit Technical";
    public FactionStyle Style => Styles.Bandit;
    public IReadOnlyList<int> Seeds => [7, 23, 41, 58];
    public VehicleCheckOptions? Checks => null;

    private const float CabSill = 0.375f, BedTop = 0.625f, BedEdge = 0.875f, BedFront = 0.2f, BedBack = 2.45f;

    public void Build(Vehicle v)
    {
        v.Description = "A rusted pickup with a machine gun on the bed. Exposed fuel drum, open cab, loose crates.";
        v.Ladder(-1.675f, 2.75f, 0.625f, 1f).SteeredAxle().DrivenAxle().Powertrain(-1.425f);
        var d = v.Dice;

        // The cab: a seat sill, an open floor, the driver on the left and sometimes a passenger.
        v.Sill(-0.175f, 0.5f);
        v.Driver(new Vector3(-0.5f, CabSill, -0.175f));
        if (d.Fork("crew").Chance(v.Style.Limit("thirdSeat")))
            v.Passenger(new Vector3(0.5f, CabSill, -0.175f));
        v.Deck("cab", Vehicle.Half, new Vehicle.Rect(-BedEdge, BedEdge, -0.8f, -0.3f), "deck");

        // The bed on its sills, a hole for the turntable, one whole plate under the drum and the cargo.
        v.Sill(0.325f, layers: 2);
        var turret = v.Sill(1.075f, layers: 2);
        v.Sill(2.075f, layers: 2);
        var side = d.Fork("drum").Chance(0.5) ? -1f : 1f;
        var strong = new Vehicle.Rect(side < 0 ? -0.625f : -0.375f, side < 0 ? 0.375f : 0.625f, 1.45f, BedBack);
        var plate = v.Put("Plate Steel 1 m", new Vector3((strong.X0 + strong.X1) * 0.5f, BedTop + 0.031f, 1.95f), Orient.Facing(Vector3.UnitY), 0.03f);
        v.Deck("bed", BedTop, new Vehicle.Rect(-BedEdge, BedEdge, BedFront, BedBack), "bed", [new Vehicle.Rect(-0.375f, 0.375f, 0.7f, 1.45f), strong]);
        v.Panels["bed"].Add(plate);
        var drum = v.Put("Fuel Drum", new Vector3(side * 0.5f, BedTop + 0.5f, 2.325f), Orient.Forward, 0.1f, plate, "-y");
        // The drum's pipe: under the bed, along the rail below the cab, up to the engine's left side.
        var port = v.CentreOf(drum) + new Vector3(0, -0.3f, -0.45f); // (a little out of its Fuel out, through a hole in the bed)
        var lane = side * 0.42f;
        var route = new List<Vector3> { port, port with { Y = 0.55f }, new(lane, 0.55f, port.Z), new(lane, 0.55f, 0.7f), new(lane, 0.05f, 0.7f), new(lane, 0.05f, -0.92f) };
        if (side > 0)
            route.AddRange([new(lane, 0.25f, -0.92f), new(-0.42f, 0.25f, -0.92f)]); // (over the rails, under the gearbox, to the left)
        var fuelIn = v.CentreOf(v.Engine!) + new Vector3(-0.42f, 0, 0);
        route.AddRange([new(-0.42f, fuelIn.Y, -0.92f), new(-0.42f, fuelIn.Y, v.CentreOf(v.Engine!).Z)]);
        v.FuelFrom(drum, route.ToArray());
        v.Turret(turret);
        // The loot rides over the strong plate's free half; the volume itself sits under the bed, out of the way of the rounds.
        var volume = v.Put("Cargo Volume", new Vector3(0, v.CentreOf(turret).Y, 1.3f), Orient.Forward, 0.1f, turret, "-z");
        var floor = BedTop + 0.07f;
        v.CargoBay(volume, new Vector3(side * -0.4f, BedTop + 0.5f, 1.95f), new Vector3(1, 1, 1), "loot", d.Fork("cargo").Int(2, 3),
            new Vector3(side * -0.65f, floor, 1.75f), new Vector3(side * -0.15f, floor, 2.1f), new Vector3(side * -0.65f, floor, 2.2f));

        // The bed's sides, its tailgate and the wall behind the cab.
        foreach (var s in new[] { -1f, 1f })
            v.Wall("sides", new Vector3(s, 0, 0), s * BedEdge, BedFront, BedBack, 0.505f, 0.5f, "sheet", 0.15);
        v.Wall("tail", Vector3.UnitZ, BedBack, side < 0 ? -0.125f : -BedEdge, side < 0 ? BedEdge : 0.125f, 0.505f, 0.5f, "sheet", 0.1); // (the drum stands open at the tail)
        v.Wall("headboard", -Vector3.UnitZ, BedFront, -BedEdge, BedEdge, 0.505f, 0.5f, "sheet");

        // The hood: on the engine, sides hanging from it and a grille.
        var hoodY = v.SocketPoint(v.Engine!, "+y").Y;
        v.Deck("hood", hoodY, new Vehicle.Rect(-0.625f, 0.625f, -2.05f, -0.8f), "sheet", smallest: 2); // (no quarter plates: their bolts land on an engine's details, and a slot in the hood is a vent)
        foreach (var s in new[] { -1f, 1f })
            v.Wall("hoodsides", new Vector3(s, 0, 0), s * 0.625f, -2.05f, -0.8f, hoodY - 0.42f, 0.5f, "sheet", 0.3);
        v.Wall("grille", -Vector3.UnitZ, -2.05f, -0.625f, 0.625f, hoodY - 0.42f, 0.5f, "grille");

        v.RamBars(0.375f, -2.175f, d.Fork("ram"));
        Crooked(v, d.Fork("crooked"));
        Show(v, d.Fork("show"));
        Colours(v, d.Fork("paint"));
    }

    /// <summary>Crooked patches off the grid, welded over the walls.</summary>
    private static void Crooked(Vehicle v, Dice d)
    {
        var walls = v.Panels["sides"].Concat(v.Panels["headboard"]).Concat(v.Panels["hoodsides"]).ToList();
        var n = d.Int(2, (int)v.Style.Limit("crookedPlates"));
        foreach (var w in d.Shuffled(walls).Take(n))
        {
            var c = v.CentreOf(w);
            var face = MathF.Abs(c.X) > 0.6f ? new Vector3(MathF.Sign(c.X), 0, 0) : c.Z > 0 ? Vector3.UnitZ : -Vector3.UnitZ;
            v.Crooked("patches", v.Style.Part(d, "patch"), w, c + face * 0.06f + new Vector3(0, (float)d.Between(-0.08, 0.08), 0), face, d);
        }
    }

    /// <summary>Spikes along the bed, trophies on the hood and the headboard.</summary>
    private static void Show(Vehicle v, Dice d)
    {
        v.Dress("spike", v.Panels["sides"].Where(p => v.CentreOf(p).X < 0), -Vector3.UnitX, d.Int(1, 3), d);
        v.Dress("spike", v.Panels["sides"].Where(p => v.CentreOf(p).X > 0), Vector3.UnitX, d.Int(1, 3), d);
        v.Dress("trophy", v.Panels["hood"], Vector3.UnitY, d.Int(1, 2), d);
        v.Dress("trophy", v.Panels["headboard"], -Vector3.UnitZ, d.Int(1, 2), d);
    }

    private static void Colours(Vehicle v, Dice d)
    {
        var frame = v.Style.Colour(d, "frame"); // (one colour: stacked beams' bands line up, and paint in one look doesn't fight)
        foreach (var p in v.Parts.Where(p => p.Name.StartsWith(Vehicle.Beam, StringComparison.Ordinal)))
            v.Paint(p, frame);
        foreach (var (tag, list) in v.Panels)
            v.PaintAll(list, tag is "hood" or "hoodsides" or "headboard" or "tail" ? "body" : "rust", d);
        var war = v.Style.Colour(d, "war");
        foreach (var p in d.Shuffled(v.Panels["hood"].Concat(v.Panels["tail"]).Concat(v.Panels["hoodsides"])).Take(d.Int(2, 4)))
            v.Paint(p, war);
    }
}
