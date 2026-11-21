using System.Numerics;
using CallOfSlop.VehicleKit;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The Hall Bay (design page drawings 20, 23 and 24): a concrete floor with a Hall Crane over it, two runs of Crane Rail Blocks on Mast
/// Block columns and a Crane Core beside the near run. Built through the game's own rules (<see cref="BlueprintScript"/>), so a step
/// they refuse fails here with their reason. Reads the part files as the game loads them: run after the part generators.
/// </summary>
public sealed class HallShowcase : IPartGenerator
{
    public string Name => "hall_showcase";

    public const string File = "Constructs/Blueprints/HallBay.json";

    public void Make(PartOutput o) => o.File(File, Script.Build().Text());

    private sealed class Script : BlueprintScript
    {
        private Script() : base("Hall Bay")
        {
        }

        public const float FloorY = 0.3f; // the Foundations' tops
        public const int TilesX = 12, TilesZ = 10; // 2 m Foundations: 24 by 20 m
        public const int Masts = 3; // a column's height in Mast Blocks: the rails' foot 6 m over the floor
        public const float NearX = 3f, Span = 16f; // the near run's line and the gap to the far one (8 cells)
        private const string Floor = "#8c8576", Mast = "#4a5058", Rail = "#22262b", Crane = "#f0c21a";

        private static readonly Orient Up = new(Vector3.UnitY, Vector3.UnitY, Vector3.UnitZ, Vector3.UnitZ);
        private static readonly Orient Toward = new(Vector3.UnitY, Vector3.UnitY, Vector3.UnitZ, -Vector3.UnitZ); // (own +z down the bay, away from the floor's front)
        private static readonly Orient East = new(Vector3.UnitY, Vector3.UnitY, Vector3.UnitZ, Vector3.UnitX); // (own +z along the bay's +x)
        private const string Line = "#3d7ab8", Stock = "#c9a24a", Hazard = "#d8a020";

        public static Script Build()
        {
            var s = new Script { Description = "A Hall Crane over a 24 x 20 m floor: two runs of Crane Rail Blocks on Mast Block columns, 16 m apart, and a Crane Core. F on the Core's screen for orders." };
            s.Group = "Production";
            s.Lay();
            return s;
        }

        private static Vector3 V(float x, float y, float z) => new(x, y, z);

        private void Lay()
        {
            var tiles = new VPart[TilesX, TilesZ];
            for (var j = 0; j < TilesZ; j++)
                for (var i = 0; i < TilesX; i++)
                {
                    var at = V(1 + 2 * i, 0.15f, -1 - 2 * j);
                    var t = i == 0 && j == 0 ? Root("Foundation", at, Up) : Put("Foundation", at, Up);
                    tiles[i, j] = t;
                    Paint(t, Floor);
                    Finish(t, "concrete");
                }
            // The two runs: a column of masts at each end and in the middle, rail blocks end to end along -z over the whole floor.
            var foot = FloorY + 2 * Masts;
            VPart? near = null;
            for (var side = 0; side < 2; side++)
            {
                var x = NearX + side * Span;
                VPart? prev = null;
                for (var k = 0; k < TilesZ; k++)
                {
                    var z = -1 - 2 * k;
                    VPart? column = null;
                    if (k is 0 or TilesZ / 2 or TilesZ - 1)
                    {
                        column = Stand("Mast Block", V(x, FloorY, z), Up, tiles[(int)(x / 2), k], "bottom");
                        Paint(column, Mast);
                        for (var m = 1; m < Masts; m++)
                        {
                            column = Stand("Mast Block", V(x, FloorY + 2 * m, z), Up, column, "bottom");
                            Paint(column, Mast);
                        }
                    }
                    var rail = column != null ? Stand("Crane Rail Block", V(x, foot, z), Up, column, "bottom") : Stand("Crane Rail Block", V(x, foot + 0.5f, z + 1), Up, prev, "+z");
                    Paint(rail, Rail);
                    prev = rail;
                    if (side == 0 && k == 2)
                        near = rail;
                }
            }
            // The Crane Core on the near run's outer (-x) side, its +x onto the rail's -x: its frame's +x across the span.
            var core = Stand("Crane Core", V(NearX - 1, foot + 0.5f, -5), Up, near, "+x");
            Paint(core, Crane);
            Config(core, ("carryHeight", 1.5));
            // Under it, a line with no conveyor at all: an Extruder making angles onto its Run-out Table (unwired, so it runs while
            // its stock lasts), and a Lay-down Pad across the bay. The crane carries each angle from the table to the pad.
            var ex = Stand("Extruder", V(8.25f, FloorY, -3.25f), Toward, null, "bottom");
            Paint(ex, Line);
            Config(ex, ("profile", "L-angle"), ("width", 0.2), ("height", 0.2), ("cutLength", 1.5));
            Paint(Onto(ex, "outBed", "Run-out Table", "in"), Line);
            var stock = Stand("Storage Volume", V(5, FloorY, -3), Up, null, "-y");
            Paint(stock, Stock);
            Config(stock, ("sizeX", 1.2), ("sizeY", 1.0), ("sizeZ", 1.2), ("atY", 0.35), ("material", "metal"), ("fill", 24));
            Link("chute", stock, ex, "Chute out", null, Stock);
            // The pad the crane lands them on (drawing 23): Pad Tiles welded 3 by 4 into one store, a Mast Block on each corner tile
            // (the stack 3 m high), Rack Sides along its far edge.
            var pad = new VPart[3, 4];
            for (var j = 0; j < 4; j++)
                for (var i = 0; i < 3; i++)
                {
                    var t = pad[i, j] = Stand("Pad Tile", V(11 + 2 * i, FloorY, -9 - 2 * j), Up, tiles[5 + i, 4 + j], "bottom");
                    Paint(t, Hazard);
                    if (i is 0 or 2 && j is 0 or 3)
                        Paint(Stand("Mast Block", V(11 + 2 * i, FloorY + 0.2f, -9 - 2 * j), Up, t, "bottom"), Mast);
                    if (i == 2)
                        Paint(Stand("Rack Side", V(16, FloorY + 0.1f, -9 - 2 * j), Up, t, "in"), Hazard);
                }
            // A Roller Tile line into the pad from the bay's back corner (drawing 23): a Pad Tile it starts at, a run of two tiles east,
            // a Turntable Tile, and one tile north butted on the pad's -z edge. Each run is one conveyor the server lays from its tiles.
            var feed = Stand("Pad Tile", V(5, FloorY, -19), East, tiles[2, 9], "bottom");
            Paint(feed, Hazard);
            foreach (var x in new[] { 7, 9 })
                Paint(Stand("Roller Tile", V(x, FloorY, -19), East, tiles[x / 2, 9], "bottom"), Line);
            Paint(Stand("Turntable Tile", V(11, FloorY, -19), Up, tiles[5, 9], "bottom"), Line);
            Paint(Stand("Roller Tile", V(11, FloorY, -17), Up, tiles[5, 8], "bottom"), Line);
        }
    }
}
