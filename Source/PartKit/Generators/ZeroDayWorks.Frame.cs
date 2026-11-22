using CallOfSlop.VehicleKit;

namespace CallOfSlop.PartKit.Generators;

public sealed partial class ZeroDayWorks
{
    private sealed partial class Script
    {
        // The shed's column rows (doc "Crane bays"): one run of Crane Rail Blocks on each, shared by the spans either side.
        // Bay 1 is one 30 m span (x 1 to 31), bay 2 two 20 m spans (31 to 51, 51 to 71), bay 3 one 24 m span (71 to 95).
        public static readonly float[] RailX = [1f, 31f, 51f, 71f, 95f];

        public const int ColumnMasts = 9; // 18 m: line D's 12 m oven and station masts stand under the hook (1.6 m under the rails' top)
        public const float RailNorth = ExtruderZ - 1f, RailSouth = -197f; // the runs' first and last block centres: 158 m, from the extruder strip to the south wall
        public const int ColumnEvery = 4; // a column every 4 rail blocks (8 m, the siding's pilaster lines)
        public static float RailFoot => FloorY + 2 * ColumnMasts;

        /// <summary>The rail blocks of each run, north to south (index 0 at <see cref="RailNorth"/>).</summary>
        private readonly List<VPart>[] _rails = RailX.Select(_ => new List<VPart>()).ToArray();

        /// <summary>The four bay cranes, west to east: bay 1, bay 2 west (line C), bay 2 east (line D), bay 3.</summary>
        private readonly List<VPart> _cranes = new();

        /// <summary>Each column's Mast Blocks, foot first, by (run, rail block index): the shell's wall sheets hang on them.</summary>
        private readonly Dictionary<(int Run, int K), List<VPart>> _columns = new();

        private int RailBlocks => (int)((RailNorth - RailSouth) / 2) + 1;

        /// <summary>The column rows and their rails, then a Crane Core at the north end of each span (outside every span: x - 2 of its near run).</summary>
        private void LayFrame()
        {
            for (var r = 0; r < RailX.Length; r++)
            {
                var x = RailX[r];
                VPart? prev = null;
                for (var k = 0; k < RailBlocks; k++)
                {
                    var z = RailNorth - 2 * k;
                    VPart rail;
                    if (k % ColumnEvery == 0 || k == RailBlocks - 1)
                    {
                        var column = Stand("Mast Block", V(x, FloorY, z), Up, Tile(x, z), "bottom");
                        Paint(column, Steel);
                        var masts = new List<VPart> { column };
                        for (var m = 1; m < ColumnMasts; m++)
                        {
                            column = Stand("Mast Block", V(x, FloorY + 2 * m, z), Up, column, "bottom");
                            Paint(column, Steel);
                            masts.Add(column);
                        }
                        _columns[(r, k)] = masts;
                        rail = Stand("Crane Rail Block", V(x, RailFoot, z), Up, column, "bottom");
                    }
                    else
                        rail = Stand("Crane Rail Block", V(x, RailFoot + 0.5f, z + 1), Up, prev, "+z");
                    Paint(rail, Rail);
                    _rails[r].Add(rail);
                    prev = rail;
                }
            }
            // Each span's Crane Core on its near (west) run's -x side, its +x onto the rail: its bridge spans east to the next run.
            for (var r = 0; r < RailX.Length - 1; r++)
            {
                var core = Stand("Crane Core", V(RailX[r] - 1, RailFoot + 0.5f, RailNorth - 2), Up, _rails[r][1], "+x");
                Paint(core, Crane);
                Config(core, ("carryHeight", 1.5));
                _cranes.Add(core);
            }
        }
    }
}
