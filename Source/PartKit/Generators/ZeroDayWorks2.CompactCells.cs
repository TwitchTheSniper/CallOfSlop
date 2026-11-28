using System.Numerics;
using CallOfSlop.VehicleKit;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The compact extruder cells (Sean, 2026-11-28: "a new set of assembly cells with a bunch of small extruders ... a line of compact
/// extruders fed into a logistics conveyor can get all the parts they need to the construction station quickly"): most of the frigate's
/// parts are small extrusions, so a row of the smallest Extruder the works lays (a Core and one Die Block, as the electronics Extruder)
/// stands on the free floor north of the main station's bay, every one onto one shared conveyor that ends on a pick pad of its own past
/// the bay's north end. Added beside Sean's pieces; none of his move.
/// <list type="bullet">
/// <item>Six Extruders at layout x -11 to 9 (4 m apart: a die cell and its Core), z -58, flowing south: each die's Run-out Table ends on
/// one Roller Tile at z -55. Five on steel from two Materials Bins north of them (x -5 and 5, z -79), each chuted from the front
/// refinery's first steel silo on its side; the one at x 1 on laminate and copper, straight from the east back row's bins as the
/// electronics Extruders are (it hands back what it holds when the next order wants the other).</item>
/// <item>Three Large Conveyor Joins (z about -49): the west three runs into one, the east three into another, both into a middle Join whose
/// Piece out goes onto a 4 x 3 Pad Tile pick pad (x -4 to 4, z -40 to -46, past the bay's north end at z -40). Every conveyor is at least
/// <see cref="CellRide"/> long, rising over the Joins as needed: a piece rides a conveyor whole, and a cell's run takes pieces up to its
/// table's 8 m.</item>
/// <item>Their Runs come from the main station's ordering console (<see cref="Script.Orders"/>), so they are machines of its line: its
/// orders share over them, and a chain asks the maker with a conveyor way to the station first (ConstructLogisticsPlan.FindMaker).</item>
/// </list>
/// </summary>
public sealed partial class ZeroDayWorks2
{
    /// <summary>The cells' die cells' layout x (odd metres, clear of the slabs' edges: nothing is welded freeform), west to east.</summary>
    private static readonly int[] CellX = [-11, -7, -3, 1, 5, 9];

    /// <summary>The cells' Extruders' layout z (each die stands 6.75 m up the flow, at z -64.75) and their Roller Tiles' (the run's end).</summary>
    private const float CellZ = -58, CellTileZ = -55;

    /// <summary>The cell on laminate and copper (its die's layout x); the rest are on steel.</summary>
    private const int CellLam = 1;

    /// <summary>The cells' steel bins (layout x; z <see cref="CellBinZ"/>): behind the Extruders' platens, each feeding its side's cells.</summary>
    private const float CellBinX = 5, CellBinZ = -79;

    /// <summary>The least length of every conveyor between a cell's run and the pad, m: a cell's piece may be its Run-out Table's 8 m, and it rides whole.</summary>
    private const float CellRide = 8.5f;

    private static IEnumerable<string> CompactCellAdditions()
    {
        foreach (var x in CellX)
        {
            yield return $$"""{"t": "part", "name": "Roller Tile", "x": {{x}}, "z": {{CellTileZ}}, "rot": 0}""";
            yield return $$"""{"t": "lab", "id": "extruder", "w": 1, "l": 2, "x": {{x}}, "z": {{CellZ}}, "rot": 0, "mirror": false}""";
        }
        foreach (var s in new[] { -1, 1 })
            yield return $$"""{"t": "part", "name": "Materials Bin", "x": {{s * CellBinX}}, "z": {{CellBinZ}}, "rot": 0}""";
        foreach (var x in new[] { -3, -1, 1, 3 })
            foreach (var z in new[] { -41, -43, -45 })
                yield return $$"""{"t": "part", "name": "Pad Tile", "x": {{x}}, "z": {{z}}, "rot": 0}""";
    }

    private sealed partial class Script
    {
        /// <summary>Is the Extruder at site <paramref name="at"/> one of the compact cells' (<see cref="CellX"/>)?</summary>
        private static bool IsCompactCell(Vector2 at) => MathF.Abs(at.Y + CellZ) < 0.5f && CellX.Any(x => MathF.Abs(at.X - x) < 0.5f);

        private readonly List<VPart> _cells = new(); // (the cells' Extruder Cores, west to east)

        /// <summary>The cells' feeds and their conveyor to the pad (before <see cref="Orders"/>, which runs them from the main station's console).</summary>
        private void CompactCells()
        {
            var cores = new List<(int X, VPart Core)>();
            foreach (var x in CellX)
                if (NearestLab("extruder", new Vector2(x, -CellZ), 1) is { } core)
                    cores.Add((x, core));
                else
                {
                    Note($"compact cells: no Extruder at ({x}, {-CellZ:0})");
                    _linkMissed++;
                }
            _cells.AddRange(cores.Select(c => c.Core));
            // The feeds: steel from each side's bin, itself from that side's first steel silo; the laminate cell from the back row's bins.
            var high = 14.75f;
            foreach (var s in new[] { -1f, 1f })
            {
                var bin = Find("Materials Bin", new Vector2(s * CellBinX, -CellBinZ));
                Chute(StockSilos(s)[1], "Chute out", bin, null, high);
                high += 0.5f;
                var low = 4.5f;
                foreach (var (x, core) in cores.Where(c => MathF.Sign(c.X) == (int)s && c.X != CellLam))
                {
                    Chute(bin, null, core, "Chute in", low);
                    low += 0.5f;
                }
            }
            if (cores.FirstOrDefault(c => c.X == CellLam).Core is { } lam)
            {
                foreach (var from in new[] { LamBin, 26f })
                {
                    Chute(Find("Materials Bin", P(1, from, 114)), null, lam, "Chute in", high);
                    high += 0.5f;
                }
            }
            // The conveyor: each run's tile into a Join, the side Joins into the middle one, the middle one onto the pad.
            var west = Piece("Large Conveyor Join", new Vector2(-6, 49), Facing(new Vector2(1, 0)), Line, "-y.4.4", new Vector2(1, 1));
            var mid = Piece("Large Conveyor Join", new Vector2(1, 49), Facing(new Vector2(0, -1)), Line, "-y.4.4", new Vector2(1, 1));
            var east = Piece("Large Conveyor Join", new Vector2(7, 49), Facing(new Vector2(-1, 0)), Line, "-y.4.4", new Vector2(1, 1));
            var pad = Find("Pad Tile", new Vector2(1, 45), 0.5f);
            if (west == null || mid == null || east == null || pad == null)
            {
                Note("compact cells: a Join or the pad isn't there: no conveyor");
                _linkMissed++;
                return;
            }
            var rise = 2.5f;
            // (west to east: the west Join's west, north and south sides, the east Join's south, north and east sides)
            string[] side = ["Piece in", "Piece in 3", "Piece in 2", "Piece in 3", "Piece in 2", "Piece in"];
            for (var k = 0; k < cores.Count; k++)
            {
                var x = cores[k].X;
                var k6 = Array.IndexOf(CellX, x);
                if (Find("Roller Tile", new Vector2(x, -CellTileZ), 0.5f) is not { } tile)
                {
                    _linkMissed++;
                    continue;
                }
                Ride(tile, "Piece out", x < 0 ? west : east, side[k6], rise);
                rise += 0.5f;
            }
            Ride(west, "Piece out", mid, "Piece in 2", rise);
            Ride(east, "Piece out", mid, "Piece in 3", rise + 0.5f);
            Ride(mid, "Piece out", pad, "Piece in", rise + 1f);
            Note($"compact cells: {cores.Count} Extruders at layout z {CellZ:0}, x {string.Join(", ", cores.Select(c => c.X))} ({CellLam} on laminate and copper), three Large Conveyor Joins, onto the pad at x -4 to 4, z -40 to -46");
        }

        /// <summary>
        /// A conveyor from a port to another, square: flat (out of each port 1 m, across) when that is at least <see cref="CellRide"/> long,
        /// else up to <paramref name="height"/> or more until it is; refused: a note.
        /// </summary>
        private void Ride(VPart from, string fromPort, VPart to, string toPort, float height)
        {
            try
            {
                var (a, fa) = PortOf(from, fromPort);
                var (b, fb) = PortOf(to, toPort);
                Vector3 a1 = a + fa, b1 = b + fb;
                static float Len(Vector3[] p)
                {
                    var l = 0f;
                    for (var i = 1; i < p.Length; i++)
                        l += Vector3.Distance(p[i - 1], p[i]);
                    return l;
                }
                // (across along the first port's face first, then the other way: out of a port straight, into the next straight)
                var xFirst = MathF.Abs(fa.X) > MathF.Abs(fa.Z);
                Vector3 Turn(float h) => xFirst ? V(b1.X, h, a1.Z) : V(a1.X, h, b1.Z);
                var flat = new[] { a, a1, Turn(a1.Y), V(b1.X, a1.Y, b1.Z), b1, b };
                Vector3[] nodes;
                if (Len(flat) >= CellRide)
                    nodes = Cut(a, a1, Turn(a1.Y), V(b1.X, a1.Y, b1.Z), b1);
                else
                {
                    var h = MathF.Max(height, MathF.Max(a1.Y, b1.Y) + 1f);
                    while (Len([a, a1, V(a1.X, h, a1.Z), Turn(h), V(b1.X, h, b1.Z), b1, b]) < CellRide)
                        h += 0.5f;
                    nodes = Cut(a, a1, V(a1.X, h, a1.Z), Turn(h), V(b1.X, h, b1.Z), b1);
                }
                Link("conveyor", from, to, fromPort, toPort, Line, false, null, nodes);
                _links++;
                var ride = ConstructConveyor.Length(Live, Live.RouteCount - 1);
                if (ride < CellRide - 0.05f)
                    Note($"compact cells: the conveyor {from.Name} -> {to.Name} ({toPort}) is only {ride:0.0} m: a piece longer waits at its start");
            }
            catch (VehicleKitException e)
            {
                Note($"compact cells: the conveyor {from.Name} -> {to.Name} ({toPort}) not laid: {e.Message}");
                _linkMissed++;
            }
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
