using System.Numerics;
using CallOfSlop.VehicleKit;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The compact extruder cells (Sean, 2026-11-28: "a new set of assembly cells with a bunch of small extruders ... a line of compact
/// extruders fed into a logistics conveyor can get all the parts they need to the construction station quickly"): most of the frigate's
/// parts are small extrusions, so a row of the smallest Extruder the works lays (a Core and one Die Block, as the electronics Extruder)
/// stands on the free floor north of the main station's bay, every one onto one shared pick pad past the bay's north end. Added beside
/// Sean's pieces; none of his move.
/// <list type="bullet">
/// <item>Five Extruders at layout x -9 to 7 (4 m apart: a die cell and its Core), z -48, flowing south: each die's Run-out Table ends on
/// one Roller Tile at z -45, and that run ends straight on the pad (a run ends on a pad's Piece in butted on its out end: RollerRuns),
/// so each cell's piece rides one conveyor from its die onto the pad and the station sees a way from every cell
/// (ConstructCrafting.RouteTo): its asks go to the cells first, shared over them by its Line split.</item>
/// <item>The pad: 9 x 2 Pad Tiles (x -10 to 8, z -40 to -44), wholly past the bay's north end (z -40) within its width: one of the main
/// station's pick pads (ConstructStationPads).</item>
/// <item>Steel by one chute a cell straight from the front refinery's first steel silo on its side; their Runs from the main station's
/// ordering console (<see cref="Script.Orders"/>), so they are its line's machines.</item>
/// </list>
/// Each cell costs the body three links (its run, its chute, its Run), and a body holds <see cref="ConstructRoutes.MaxRoutes"/>: Works
/// II stood at 237 with its runs before the cells, so five is what fits (2026-11-28: six cells on a conveyor of Large Conveyor Joins,
/// 30 more links, left the last runs and turntable crossings with no room, and the works fitted fewer parts).
/// </summary>
public sealed partial class ZeroDayWorks2
{
    /// <summary>The cells' die cells' layout x (odd metres, clear of the slabs' edges: nothing is welded freeform), west to east.</summary>
    private static readonly int[] CellX = [-9, -5, -1, 3, 7];

    /// <summary>The cells' Extruders' layout z (each die stands 6.75 m up the flow, at z -54.75) and their Roller Tiles' (the run's end, butting the pad).</summary>
    private const float CellZ = -48, CellTileZ = -45;

    /// <summary>The pad's Pad Tiles' layout x (from, to: odd metres) and z.</summary>
    private const int CellPadX0 = -9, CellPadX1 = 7;

    private static readonly int[] CellPadZ = [-41, -43];

    private static IEnumerable<string> CompactCellAdditions()
    {
        foreach (var x in CellX)
        {
            yield return $$"""{"t": "part", "name": "Roller Tile", "x": {{x}}, "z": {{CellTileZ}}, "rot": 0}""";
            yield return $$"""{"t": "lab", "id": "extruder", "w": 1, "l": 2, "x": {{x}}, "z": {{CellZ}}, "rot": 0, "mirror": false}""";
        }
        for (var x = CellPadX0; x <= CellPadX1; x += 2)
            foreach (var z in CellPadZ)
                yield return $$"""{"t": "part", "name": "Pad Tile", "x": {{x}}, "z": {{z}}, "rot": 0}""";
    }

    private sealed partial class Script
    {
        /// <summary>Is the Extruder at site <paramref name="at"/> one of the compact cells' (<see cref="CellX"/>)?</summary>
        private static bool IsCompactCell(Vector2 at) => MathF.Abs(at.Y + CellZ) < 0.5f && CellX.Any(x => MathF.Abs(at.X - x) < 0.5f);

        private readonly List<VPart> _cells = new(); // (the cells' Extruder Cores, west to east)

        /// <summary>The cells' steel (before <see cref="Orders"/>, which runs them from the main station's console).</summary>
        private void CompactCells()
        {
            var high = 14.75f;
            foreach (var x in CellX)
            {
                if (NearestLab("extruder", new Vector2(x, -CellZ), 1) is not { } core)
                {
                    Note($"compact cells: no Extruder at ({x}, {-CellZ:0})");
                    _linkMissed++;
                    continue;
                }
                _cells.Add(core);
                Chute(StockSilos(x < 0 ? -1 : 1)[1], "Chute out", core, "Chute in", high); // (over the silo's 11.5 m rim)
                high += 0.25f;
                if (Find("Roller Tile", new Vector2(x, -CellTileZ), 0.5f) is { } tile && !Butts(tile, "Pad Tile"))
                    Note($"compact cells: the Roller Tile at ({x}, {-CellTileZ:0}) is welded to no Pad Tile: its run won't end on the pad");
            }
            Note($"compact cells: {_cells.Count} Extruders at layout z {CellZ:0}, x {string.Join(", ", CellX)}, on steel, each run onto the pad at x {CellPadX0 - 1} to {CellPadX1 + 1}, z {CellPadZ[0] + 1} to {CellPadZ[^1] - 1}");
        }

        /// <summary>Is <paramref name="p"/> welded to a part named <paramref name="name"/>?</summary>
        private bool Butts(VPart p, string name)
        {
            for (var e = Live.FirstLink(p.Index); e >= 0; e = Live.NextLink(e))
                if (Live.DefOf(Live.OtherEnd(e)).Name == name)
                    return true;
            return false;
        }

        /// <summary>The cells' Runs, from the main station's ordering console: they join its line.</summary>
        private void CompactCellRuns(VPart console)
        {
            var h = 7.25f; // (a cable's drop onto its Run is one segment: at most 8 m)
            foreach (var core in _cells)
            {
                Cable(console, core, "Run", h);
                h += 0.1f;
            }
            Note($"compact cells: {_cells.Count} Runs from the main station's console");
        }
    }
}
