using CallOfSlop.VehicleKit;

namespace CallOfSlop.PartKit.Generators;

public sealed partial class ZeroDayWorks
{
    private sealed partial class Script
    {
        // The control room and the service rooms (doc "Control room and service rooms"), in the steel building kit (Constructs/Parts/
        // Building: 0.25 m cells, 2.75 m walls, 3 m storeys), laid as the Two Storey House is: Steel Floor panels on the slab, a Wall
        // Corner on each corner (2 m arms), walls between them, the next storey's floor (or a roof of floor panels) on the corners' tops.
        //  - Control room: 6 x 12 m on the floor between lines A and B's transfer runs (the 8 m between them: the doc's 12 x 9 m would
        //    stand over the rollers), at the foot of bay 1. Its ground storey is open, four corners and a Wide Stair; the room is the
        //    storey above (floor 3.5 m up), glazed all round, looking up bays 1 and 2 and out to the yard.
        //  - Repair shop (10 x 16 m), spare parts (10 x 8 m) and maintenance (10 x 8 m) at the foot of bay 3, west of the fittings run,
        //    doors onto the aisle beside it. The repair shop has no roof and an open north end, under bay 3's Hall Crane.
        public const string Office = "#8a9096", Glazing = "#5d6670";

        private void LayRooms()
        {
            // ---- The control room. ----
            var ground = Panels(On("Steel Floor 4x4 m", 11, -196, Up, "b0_0", Office), 6, 12);
            var legs = Corners(ground, 6, 12, "Steel Wall Corner", Office);
            Paint(Stand("Steel Wide Stair 3 m", SocketAt(ground[0, 0], "t7_15"), South, ground[0, 0], "b0_0"), Steel); // (the house's: up the west side, south)
            var upper = Panels(Paint(Stand("Steel Wide Stair Floor", SocketAt(legs[0], "t0_0"), Up, legs[0], "b0_0"), Office), 6, 12);
            var room = Corners(upper, 6, 12, "Steel Window Corner", Glazing);
            Walls(upper, 6, 12, Glazing, south: ["Steel Window Wall 2 m"], east: ["Steel Window Wall 4 m", "Steel Window Wall 4 m"],
                north: ["Steel Window Wall 2 m"], west: ["Steel Window Wall 4 m", "Steel Window Wall 4 m"]);
            Panels(Paint(Stand("Steel Floor 4x4 m", SocketAt(room[0], "t0_0"), Up, room[0], "b0_0"), Office), 6, 12);

            // ---- The repair shop, spare parts and maintenance rooms, north to south. ----
            foreach (var (z0, l, roof, open) in new[] { (-179f, 16, false, true), (-188f, 8, true, false), (-197f, 8, true, false) })
            {
                var floor = Panels(On("Steel Floor 4x4 m", 74, z0, Up, "b0_0", Office), 10, l);
                var c = Corners(floor, 10, l, "Steel Wall Corner", Office);
                string[] side = l == 16 ? ["Steel Wall 4 m", "Steel Door Wall 2 m", "Steel Wall 2 m", "Steel Wall 4 m"] : ["Steel Door Wall 2 m", "Steel Wall 2 m"];
                string[] blank = l == 16 ? ["Steel Wall 4 m", "Steel Wall 4 m", "Steel Wall 4 m"] : ["Steel Wall 4 m"];
                Walls(floor, 10, l, Office, south: ["Steel Wall 4 m", "Steel Wall 2 m"], east: side,
                    north: open ? [] : ["Steel Wall 4 m", "Steel Wall 2 m"], west: blank);
                if (roof)
                    Panels(Paint(Stand("Steel Floor 4x4 m", SocketAt(c[0], "t0_0"), Up, c[0], "b0_0"), Office), 10, l);
            }
        }

        /// <summary>
        /// Floor panels from <paramref name="first"/> (the south-west one, turned Up): <paramref name="w"/> by <paramref name="l"/> m, 4 m
        /// panels with a 2 x 4 m column on the east when the width wants it (l a multiple of 4). [column, row], rows north.
        /// </summary>
        private VPart[,] Panels(VPart first, int w, int l)
        {
            var cols = (w + 3) / 4;
            var rows = l / 4;
            var g = new VPart[cols, rows];
            for (var j = 0; j < rows; j++)
                for (var i = 0; i < cols; i++)
                {
                    if (i == 0 && j == 0)
                    {
                        g[0, 0] = first;
                        continue;
                    }
                    var part = 4 * (i + 1) > w ? "Steel Floor 2x4 m" : first.Name == "Steel Wide Stair Floor" ? "Steel Floor 4x4 m" : first.Name;
                    g[i, j] = Paint(j == 0
                        ? Stand(part, SocketAt(g[i - 1, 0], "r0"), Up, g[i - 1, 0], "l0")
                        : Stand(part, SocketAt(g[i, j - 1], "k0"), Up, g[i, j - 1], "f0"), Office);
                }
            return g;
        }

        /// <summary>A floor socket at cell (cx, cz) of a panel grid (0.25 m cells from the south-west corner's).</summary>
        private (VPart Panel, string Socket) Cell(VPart[,] g, int cx, int cz, bool top = true)
        {
            var p = g[cx / 16, cz / 16];
            return (p, $"{(top ? "t" : "b")}{cx % 16}_{cz % 16}");
        }

        /// <summary>A Wall Corner (or a window one) on each corner of a storey, its arms along the sides: SW, SE, NE, NW.</summary>
        private VPart[] Corners(VPart[,] g, int w, int l, string part, string paint)
        {
            int cw = 4 * w - 1, cl = 4 * l - 1;
            var r = new VPart[4];
            foreach (var (k, cx, cz, o) in new[] { (0, 0, 0, Up), (1, cw, 0, West), (2, cw, cl, South), (3, 0, cl, East) })
            {
                var (p, s) = Cell(g, cx, cz);
                r[k] = Paint(Stand(part, SocketAt(p, s), o, p, "b0_0"), paint);
            }
            return r;
        }

        /// <summary>
        /// Walls between a storey's corners, each side's pieces in order from its south (or west) end: the south and north sides run east,
        /// the east and west sides north. A door wall gets its hinge and a steel leaf.
        /// </summary>
        private void Walls(VPart[,] g, int w, int l, string paint, string[] south, string[] east, string[] north, string[] west)
        {
            int cw = 4 * w - 1, cl = 4 * l - 1;
            foreach (var (pieces, alongX, fixedCell, o) in new[] { (south, true, 0, Up), (north, true, cl, Up), (west, false, 0, West), (east, false, cw, West) })
            {
                var at = 8; // (past the corner's 2 m arm)
                foreach (var piece in pieces)
                {
                    var (p, s) = alongX ? Cell(g, at, fixedCell) : Cell(g, fixedCell, at);
                    var wall = Paint(Stand(piece, SocketAt(p, s), o, p, "b0"), paint);
                    if (piece.Contains("Door"))
                        Paint(Onto(Onto(wall, "hinge", "Door Hinge", "base"), "leaf", "Bulkhead Door 1.5x2.25 m", "hang"), Steel);
                    at += piece.Contains("4 m") ? 16 : 8;
                }
                if (pieces.Length > 0 && at != (alongX ? cw : cl) - 7)
                    throw new VehicleKitException($"a room side of {(alongX ? w : l)} m: its walls end at cell {at}, the corner's arm at {(alongX ? cw : cl) - 7}");
            }
        }
    }
}
