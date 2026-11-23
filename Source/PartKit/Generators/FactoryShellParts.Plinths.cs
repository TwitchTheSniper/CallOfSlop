using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// Plinths that lift an Extruder level with the conveyors after it (Sean, 2026-11-22: "conveyor height differences"). A Run-out Table's
/// top is level with its Extruder's foot, a Roller Tile's rollers are 1 m up, and a Panel Former's or a Laser Cutter's bed takes a piece at
/// 1 m: an Extruder standing on the floor put its tables 0.7 m under the run that took its pieces on. On an Extruder Plinth (1 m) its
/// tables' tops are level with the rollers and the beds after them, and a Run-out Table Stand carries each table on legs to the floor.
/// </summary>
public sealed partial class FactoryShellParts
{
    public const double PlinthHigh = 1.0; // a Roller Tile's rollers' top: an Extruder on a plinth puts its Run-out Tables level with a run

    /// <summary>The plinth parts' names, in their Assembling rows' order.</summary>
    public static readonly string[] PlinthNames = ["Extruder Plinth", "Run-out Table Stand", "Pad Tile Stand"];

    private static IEnumerable<(string, Part)> Plinths()
    {
        yield return ("Plinths", ExtruderPlinth());
        yield return ("Plinths", TableStand());
        yield return ("Plinths", PadStand());
    }

    /// <summary>
    /// The Extruder Plinth: a 2 m wide concrete block 1 m high under an Extruder Core or a Die Block, their whole length (a Die Block's die
    /// stands 4 m deep behind its face). Its front stops 1 cm short of the die's face, where a Run-out Table's bed begins.
    /// </summary>
    private static Part ExtruderPlinth()
    {
        const double h = PlinthHigh, back = -3.3, front = 0.74, e = 0.985;
        var p = new Part("Extruder Plinth", Small, "Factory",
            "A 1 m concrete plinth for an Extruder Core or a Die Block: the machine on its top puts its Run-out Tables level with a Roller Tile run and the beds after it. Lay one under every cell of the die.",
            material: "concrete", color: Concrete, group: "Production");
        p.Add(Box([-e, 0, back], [e, h - 0.04, front])); // the block
        p.Add(Box([-e - 0.012, h - 0.04, back - 0.012], [e + 0.012, h, front + 0.01], Rim)); // a steel cap the machine stands on
        p.Add(Box([-e - 0.006, 0.12, front + 0.002], [e + 0.006, 0.2, front + 0.008], Hazard, detail: true)); // a hazard band on the front's foot
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Sock("top", [0, h, 0], "+y");
        p.Sock("+x", [1, h * 0.5, 0], "+x");
        p.Sock("-x", [-1, h * 0.5, 0], "-x");
        p.Stats(2 * (front - back) * h * 2400 * 0.5, 4000, 20);
        p.Set(("collider", "boxes"));
        return p;
    }

    /// <summary>
    /// The Run-out Table Stand: legs and rails under a Run-out Table on a raised Extruder, 2 x 8 m like the table, its top 4 mm under the
    /// table's bed. It runs along +z from z 0 (under the table's in end); its socket is under its middle.
    /// </summary>
    private static Part TableStand()
    {
        const double h = PlinthHigh - 0.3 - 0.004, len = 8, e = 0.94, leg = 0.1;
        var p = new Part("Run-out Table Stand", Small, "Factory",
            "Steel legs for a Run-out Table on a raised Extruder: stand it under the table, its rails carry the table's bed level with the rollers after it.",
            color: StandSteel, group: "Production");
        foreach (var z in new[] { 0.3, 4.0, 7.7 })
            foreach (var x in new[] { -e + leg, e - leg })
            {
                p.Add(Box([x - leg, 0.06, z - leg], [x + leg, h - 0.08, z + leg])); // a leg
                p.Add(Box([x - leg - 0.06, 0, z - leg - 0.06], [x + leg + 0.06, 0.06, z + leg + 0.06], Rim)); // its foot plate
            }
        foreach (var x in new[] { -e, e - 2 * leg })
            p.Add(Box([x, h - 0.08, 0.1], [x + 2 * leg, h, len - 0.1])); // the rails the table lies on
        foreach (var z in new[] { 0.3, 4.0, 7.7 })
            p.Add(Box([-e + 2 * leg, h - 0.2, z - 0.04], [e - 2 * leg, h - 0.1, z + 0.04])); // a cross beam
        p.Add(Box([-e + 2 * leg, 0, 3.95], [e - 2 * leg, 0.05, 4.05])); // a floor tie between the middle legs, the socket's seat
        p.Sock("bottom", [0, 0, 4.0], "-y");
        p.Stats(420, 900, 6);
        p.Set(("collider", "boxes"));
        return p;
    }

    /// <summary>
    /// The Pad Tile Stand (Sean, 2026-11-23: a pick pad beside a Building Station raised to the rollers' height): a 2 m steel stool under one
    /// Pad Tile, its top 0.77 m up so the pad's hold floor is level with a Roller Tile's rollers (1 m) and a run rolls its pieces straight on.
    /// Stands weld side by side as their pad's tiles do; a tile welds on its top.
    /// </summary>
    private static Part PadStand()
    {
        const double h = PlinthHigh - 0.23, e = 0.985, leg = 0.09; // (0.23: the Pad Tile's hold floor over its foot, PieceRackParts.HoldLo)
        var p = new Part("Pad Tile Stand", Small, "Factory",
            "A steel stand under a Pad Tile: the pad's deck level with a Roller Tile run's rollers, so a run rolls its pieces straight onto it. Lay one under every tile of the pad.",
            color: StandSteel, group: "Production");
        const double f = 0.997, lx = e - leg - 0.04; // (f: the top frame's side, the stand's faces; lx: a leg's middle, its foot plate inside the frame's line)
        foreach (var x in new[] { -lx, lx })
            foreach (var z in new[] { -lx, lx })
            {
                p.Add(Box([x - leg, 0.046, z - leg], [x + leg, h - 0.116, z + leg])); // a leg (4 mm into its foot and the frame: no shared face)
                p.Add(Box([x - leg - 0.04, 0, z - leg - 0.04], [x + leg + 0.04, 0.05, z + leg + 0.04], Rim)); // its foot plate
            }
        p.Add(Box([-f, h - 0.12, -f], [f, h - 0.004, f])); // the top frame the tile stands on (4 mm under its foot)
        foreach (var x in new[] { -0.5, 0.0, 0.5 })
            p.Add(Box([x - 0.04, h - 0.008, -e + 0.02], [x + 0.04, h, e - 0.02], Rim, detail: true)); // the bearers under the tile
        foreach (var z in new[] { -lx, lx })
            p.Add(Box([-lx + leg, 0.3, z - 0.03], [lx - leg, 0.38, z + 0.03])); // a brace between the legs
        p.Add(Box([-f + 0.02, h - 0.1, -f - 0.004], [f - 0.02, h - 0.04, -f + 0.002], Hazard, detail: true)); // a hazard band on its -z face
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Sock("top", [0, h, 0], "+y");
        p.Sock("+x", [1, h * 0.5, 0], "+x");
        p.Sock("-x", [-1, h * 0.5, 0], "-x");
        p.Sock("+z", [0, h * 0.5, 1], "+z");
        p.Sock("-z", [0, h * 0.5, -1], "-z");
        p.Stats(180, 900, 6);
        p.Set(("collider", "boxes"));
        return p;
    }

    private const string StandSteel = "#3d4247";
}
