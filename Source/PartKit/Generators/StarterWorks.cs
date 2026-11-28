using System.Numerics;
using CallOfSlop.VehicleKit;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The Starter Works (retiring the Assembler, phase 6, 2026-11-28): a survival world's first kit line on one floor, so a fresh Survival
/// game can make the placed parts an Assembler no longer builds. An Extruder with a Die Block (a 2 x 2 m die) fed by two Materials Bins (steel, and a kit's rubber, concrete or cloth), its pieces on a conveyor onto a Pad
/// Tile pick pad beside a Building Station's 4 x 3 bay (8 x 6 m), a Roller Tile run from the bay's far end into a Bake Oven's 4 x 3 bed,
/// and a Small Genset on its fuel tanks with a Battery. An order at the oven (its screen, or a terminal) asks the station for the part's
/// kit, the station asks the Extruder for its pieces, the rover fits them, the kit rolls out into the oven and bakes. Built through the
/// game's own rules (<see cref="BlueprintScript"/>), so a step they refuse fails here with their reason. In Constructs/SurvivalStart.json.
/// </summary>
public sealed class StarterWorks : IPartGenerator
{
    public string Name => "starter_works";

    public const string File = "Constructs/Blueprints/StarterWorks.json";

    public void Make(PartOutput o) => o.File(File, Script.Build().Text());

    private sealed class Script : BlueprintScript
    {
        private Script() : base("Starter Works")
        {
        }

        private const float FloorY = 0.3f; // the Foundations' tops
        private const int TilesX = 15, TilesZ = 7; // 2 m Foundations: 30 by 14 m
        private const float Line = -7.25f; // the kit line's middle (z): the station's dock, its bay's middle row, the run, the oven's bed
        private const string Floor = "#8c8576", Power = "#b5452b", Stock = "#c9a24a", Kit = "#3d7ab8", Hazard = "#d8a020";

        private static readonly Orient Front = new(Vector3.UnitY, Vector3.UnitY, Vector3.UnitZ, Vector3.UnitZ);
        private static readonly Orient Flow = new(Vector3.UnitY, Vector3.UnitY, Vector3.UnitZ, Vector3.UnitX); // own +z along +x
        private static readonly Orient Toward = new(Vector3.UnitY, Vector3.UnitY, Vector3.UnitZ, -Vector3.UnitX); // own +z along -x

        public static Script Build()
        {
            var s = new Script
            {
                Description = "A survival world's first kit line, 30 x 14 m: an Extruder fed by a bin, its pieces onto a pick pad beside a Building Station's 8 x 6 m bay, a roller run from the bay into a Bake Oven's 8 x 6 m bed, and a Small Genset with a Battery. Order a part at the oven: the station fits its kit from the Extruder's pieces and rolls it into the oven, which bakes it into the part. Tip steel into one bin and rubber into the other.",
            };
            s.Group = "Production";
            s.Lay();
            return s;
        }

        private static Vector3 V(float x, float y, float z) => new(x, y, z);

        private VPart On(string part, float x, float z, Orient o, string by = "bottom", string? paint = null)
        {
            var p = Stand(part, V(x, FloorY, z), o, null, by);
            if (paint != null)
                Paint(p, paint);
            return p;
        }

        private void Lay()
        {
            for (var j = 0; j < TilesZ; j++)
                for (var i = 0; i < TilesX; i++)
                {
                    var at = V(1 + 2 * i, 0.15f, -1 - 2 * j);
                    var t = i == 0 && j == 0 ? Root("Foundation", at, Front) : Put("Foundation", at, Front);
                    Paint(t, Floor);
                    Finish(t, "concrete");
                }

            // Power: a Small Genset on two fuel tanks and a Battery, in the near corner.
            var genset = On("Small Genset", 2.25f, -1.25f, Front, "-y", Power);
            var tank = Onto(genset, "+x", "Fuel Tank", "-x");
            Paint(tank, Power);
            Paint(Onto(tank, "+x", "Fuel Tank", "-x"), Power);
            On("Battery", 6.25f, -1.25f, Front, "-y", Power);

            // The Building Station: its dock pad x 1 to 5, its 4 x 3 bay of Bay Blocks out of its mouth (x 5 to 13, z -10 to -4).
            var station = On("Building Station", 4.25f, Line, Front, "-y.0.0");
            var row = new VPart[4];
            row[0] = Onto(station, "+x.0", "Bay Block", "-x");
            for (var k = 1; k < 4; k++)
                row[k] = Onto(row[k - 1], "+x", "Bay Block", "-x");
            foreach (var b in row)
            {
                Onto(b, "+z", "Bay Block", "-z");
                Onto(b, "-z", "Bay Block", "+z");
            }

            // Its pick pad: two Pad Tiles past the bay's -z side (z -12 to -10), within its span along x.
            var pad = new VPart[2];
            for (var k = 0; k < 2; k++)
                pad[k] = On("Pad Tile", 8.25f + 2 * k, -11.25f, Front, "bottom", Hazard);

            // The Extruder, fed steel from a bin, its pieces east along z -11 onto the pad's -x side.
            var ex = On("Extruder", 3.25f, -11.25f, Flow, "bottom", Kit);
            Paint(Onto(ex, "+x", "Die Block", "-x"), Kit); // (a 2 x 2 m die beside the Core's own 0.5 m one: a kit's plates and sheets)
            var bin = On("Materials Bin", 0.75f, -11.25f, Front, "bottom", Stock);
            Link("chute", bin, ex, null, null, Stock);
            var bin2 = On("Materials Bin", 0.75f, -9.25f, Front, "bottom", Stock); // (a second material: a kit's rubber, concrete or cloth sheet)
            Link("chute", bin2, ex, null, null, Stock);
            Link("conveyor", ex, pad[0], "Piece out", "Piece in -x", Kit, false);
            // A Line Console with the Extruder on its line, wired into the station's Orders: the station's kit pieces are the line's orders.
            var console = On("Line Console", 6.25f, -13.25f, Front);
            Link("data", console, ex, null, "Run");
            Link("data", console, station, null, "Orders");

            // The way out of the bay and into the oven: two Roller Tiles along the line (x 13 to 17).
            On("Roller Tile", 14.25f, Line, Flow, "bottom", Kit);
            On("Roller Tile", 16.25f, Line, Flow, "bottom", Kit);

            // The Bake Oven: its Core at the bed's far end (x 25 to 27), its 4 x 3 bed of Oven Bed Blocks x 17 to 25.
            var oven = On("Bake Oven", 26.25f, Line, Front, "bottom");
            var bed = new VPart[4];
            bed[0] = Onto(oven, "-x", "Oven Bed Block", "+x");
            for (var k = 1; k < 4; k++)
                bed[k] = Onto(bed[k - 1], "-x", "Oven Bed Block", "+x");
            foreach (var b in bed)
            {
                Onto(b, "+z", "Oven Bed Block", "-z");
                Onto(b, "-z", "Oven Bed Block", "+z");
            }
        }
    }
}
