using System.Numerics;
using CallOfSlop.VehicleKit;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The works' control row (Sean, 2026-11-26: "all of the line consoles for this entire factory are sitting next to each other in a line,
/// so I can just walk through them all and see the status of the entire factory", on the open floor by the west gensets he showed). Every
/// Line Console stands in one row there instead of where the layout puts it, screens east to a walk between the row and the gensets; its
/// cables (the Extruder's Run, a station's Orders, the presses' and ovens' Runs) are worked out from where the layout puts it, as before,
/// and run from the row. After the Line Consoles, one Station Console a Building Station, its watch link to that station
/// (ConstructStationConsole.cs), in the order the consoles number the stations (Station 1 first).
/// </summary>
public sealed partial class ZeroDayWorks2
{
    private sealed partial class Script
    {
        /// <summary>The row: along z at x <see cref="RowX"/> from <see cref="RowZ0"/> southward, a desk every <see cref="RowPitch"/>.</summary>
        public const float RowX = -45f, RowZ0 = -38.5f, RowPitch = 1.5f;

        /// <summary>A gap between the Line Consoles and the Station Consoles, in slots.</summary>
        private const int RowGap = 1;

        /// <summary>The way the row's screens face: east, to the walk by the gensets.</summary>
        private static readonly Vector2 RowFaces = new(1, 0);

        private readonly Dictionary<(string, float, float), int> _rowSlot = new();

        private static Vector2 RowSpot(int slot) => new(RowX, RowZ0 - slot * RowPitch);

        /// <summary>A Station Console for each Building Station, after the Line Consoles in the row, its watch link to the station over the works.</summary>
        private void StationConsoles()
        {
            var slot = _rowSlot.Count + RowGap;
            var height = 10f;
            foreach (var (at, station) in _stations.OrderBy(s => s.Core.Index)) // (in part order: the order the consoles number them, ConstructStationConsole.Number)
            {
                var spot = RowSpot(slot++);
                if (Piece("Station Console", spot, Facing(RowFaces), Station) is not { } console)
                    continue;
                // (over the works at its own height, then down onto where a watch link meets the station: its bounds' middle, which on a
                // Core as big as a station's stands far off its origin)
                var from = _free.TryGetValue(console, out var cw) ? FromRest(cw.Pos) : FromRest(Live.Parts[console.Index].Pos); // (a desk welded freeform: its pose)
                var to = FromRest(ConstructRoutes.PortPoint(Live, station.Index, ConstructRoutes.WatchPort, RouteType.Data));
                try
                {
                    Link("data", console, station, ConstructRoutes.WatchName, ConstructRoutes.WatchName, Station, false, null,
                        Cut(from, V(from.X, height, from.Z), V(to.X, height, from.Z), V(to.X, height, to.Z), V(to.X, to.Y + 1, to.Z)));
                    _links++;
                }
                catch (VehicleKitException ex)
                {
                    Note($"control row: the Station Console's watch link to the Building Station at ({at.X:0}, {at.Y:0}) not laid: {ex.Message}");
                    _linkMissed++;
                }
                Note($"control row: a Station Console at ({spot.X:0.#}, {spot.Y:0.#}) watches the Building Station at ({at.X:0}, {at.Y:0})");
                height += 0.5f;
            }
        }
    }
}
