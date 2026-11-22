using CallOfSlop.VehicleKit;

namespace CallOfSlop.PartKit.Generators;

public sealed partial class ZeroDayWorks
{
    private sealed partial class Script
    {
        // The armour lines (doc "Armour-forming lines"), each north to south: its own Sheet Extruder at the head (the inline extruder strip),
        // a roller run into its Panel Former's Piece in, the former's bed and Out Tables, a run onto a skin pad. Lines A to C then hand the skin
        // east into a Laser Cutter (a cutter takes its blank by conveyor only, and its bed lies on its Core's +x side, so the pad is turned to
        // send east); the cut piece waits at a short run's end, where the bay's Hall Crane picks it. From there the crane carries each piece to
        // the dress station's bay, the dressed group to the Bake Oven's bed and the baked part to the line's stores. Line D has no cutter: its
        // skins wait on their pad for the crane and its Building Station, which builds each end before the oven bakes it.

        private readonly List<VPart> _stations = new();

        public const float ExtruderCoreZ = -43f; // an Extruder Core's centre: its die's foot (outBed, 0.75 m south) at z -44, so 16 m of Run-out Tables end at -60
        public const float FormerZ = -69f; // the Panel Former Cores: their Piece in (-z, 1 m north) on the last row of the feed run

        private readonly List<(string Line, VPart Store)> _stores = new();

        private void LayArmourLines()
        {
            // Bay 1 (x 2 to 30, the 30 m span): lines A and B side by side, the same line twice (a Hull Section key carries its side letter).
            foreach (var (line, x0, paint, east) in new[] { ("A", 5f, LineA, 13f), ("B", 19f, LineB, 27f) })
            {
                ArmourLine(line, x0, paint, bedAcross: 3, bedAlong: 7, tables: 2, padAcross: 3, cutAlong: 7);
                // The dress station (a small Building Station: Core east of a 5 x 9 bay, Mast Blocks 3 high on its corners), its Bake Oven,
                // then two piece stores of 5 x 6 Pad Tiles with a run between them, and the transfer run south to the yard.
                Station(east, -119, east - 10, -117, 5, 9, 3, paint);
                Oven(east - 6, -137, east - 10, 5, 9, 3, paint);
                Stores(line, east - 10, -157, 5, 6, paint, second: true, transferRows: 8);
                // Four Industrial arms on the 4 m strip east of the station's and the oven's beds (west of the next line's or the columns).
                foreach (var z in new[] { -127f, -133f, -143f, -149f })
                    Arm("Industrial Loader Arm", east, z);
            }
            // Bay 2 west span (x 32 to 50): line C, the wide flat pieces.
            ArmourLine("C", 33, LineC, bedAcross: 6, bedAlong: 12, tables: 1, padAcross: 6, cutAlong: 12);
            Station(47, -137, 33, -135, 7, 13, 3, LineC);
            Oven(39, -163, 33, 7, 13, 3, LineC);
            Stores("C", 33, -191, 6, 4, LineC, second: false, transferRows: 0);
            foreach (var z in new[] { -143f, -151f, -159f })
                Arm("Industrial Loader Arm", 47, z); // (on the station's east side between the columns: a Plasma arm's 3.6 m base doesn't fit there)
            // Bay 2 east span (x 52 to 70): line D, the heavy ends: no cutter; its Building Station builds each end from skins, frames and fittings.
            ArmourLine("D", 53, LineD, bedAcross: 6, bedAlong: 8, tables: 1, padAcross: 6, cutAlong: 0);
            Station(69, -101, 53, -99, 8, 8, 6, LineD);
            Oven(61, -119, 53, 8, 8, 6, LineD);
            Stores("D", 53, -137, 8, 6, LineD, second: false, transferRows: 25);
            Arm("Plasma Loader Arm", 52, -118); // (two Plasma arms at the station's south corners, either side of the oven's Core)
            Arm("Plasma Loader Arm", 63, -118);
        }

        /// <summary>
        /// A Building Station: its Core at (<paramref name="coreX"/>, <paramref name="coreZ"/>) facing south with its bay west of it (the bay's
        /// north-west cell at <paramref name="bayX0"/>, <paramref name="bayZ0"/>), <paramref name="masts"/> Mast Blocks on each bay corner.
        /// </summary>
        private VPart Station(float coreX, float coreZ, float bayX0, float bayZ0, int across, int along, int masts, string paint)
        {
            var core = On("Building Station", coreX, coreZ, South, "-y.0.1", paint);
            var bay = Tiles("Bay Block", bayX0, bayZ0, across, along, South, paint);
            foreach (var c in Corners(bay))
                Stack(c, "top", masts);
            _stations.Add(core);
            return core;
        }

        /// <summary>A Loader Arm whose base's south-west corner is at (<paramref name="x"/>, <paramref name="z"/>), the base reaching north and east.</summary>
        private VPart Arm(string part, float x, float z) => On(part, x, z, Up, "-y.0.0", Crane);

        /// <summary>A Bake Oven: its Core at (x, z) facing south, its bed south of it from column <paramref name="bedX0"/>, masts on the bed's corners.</summary>
        private VPart Oven(float coreX, float coreZ, float bedX0, int across, int along, int masts, string paint)
        {
            var core = On("Bake Oven", coreX, coreZ, South, paint: paint);
            var bed = Tiles("Oven Bed Block", bedX0, coreZ - 2, across, along, South, paint);
            foreach (var c in Corners(bed))
                Stack(c, "top", masts);
            return core;
        }

        /// <summary>
        /// A line's piece store: a pad of Pad Tiles (a Mast Block on each corner, the stack 3 m), with <paramref name="second"/> a run of one
        /// row and a second pad below it; then a transfer run of <paramref name="transferRows"/> rows, three tiles wide, south from the last pad's middle.
        /// </summary>
        private void Stores(string line, float x0, float z0, int across, int along, string paint, bool second, int transferRows)
        {
            var pad = Tiles("Pad Tile", x0, z0, across, along, South, Stock);
            foreach (var c in Corners(pad))
                Stack(c, "top", 1);
            _stores.Add((line, pad[0, 0]));
            var z = z0 - 2 * along;
            if (second)
            {
                Tiles("Roller Tile", x0, z, across, 1, South, paint);
                pad = Tiles("Pad Tile", x0, z - 2, across, along, South, Stock);
                foreach (var c in Corners(pad))
                    Stack(c, "top", 1);
                _stores.Add((line, pad[0, 0]));
                z -= 2 + 2 * along;
            }
            if (transferRows > 0)
                Tiles("Roller Tile", x0 + 2 * ((across - 3) / 2), z, 3, transferRows, South, paint);
        }

        /// <summary>
        /// One armour line whose former's bed starts at column x <paramref name="x0"/> (its west column's centre, odd), its stages stacked
        /// south from the extruder strip. Bed sizes are in 2 m blocks, across (x) by along (z).
        /// </summary>
        private void ArmourLine(string line, float x0, string paint, int bedAcross, int bedAlong, int tables, int padAcross, int cutAlong)
        {
            var dieCells = bedAcross; // (the sheet as wide as the bed: a Die Block a 2 m column)

            // ---- The Sheet Extruder: its Core at the die's east end, Die Blocks welded west, Run-out Tables south of every cell. --------------
            // Everything after it is welded on in a chain from its tables (a table's far end is a quarter metre off the floor's 2 m grid).
            var tableEnds = SheetExtruder(x0 + 2 * dieCells, dieCells, tables, paint);
            VPart[,] run;
            if (bedAlong == 0)
            {
                // No former (bay 3's walls line): the sheet runs off the tables straight onto the pad's run.
                run = new VPart[bedAcross, 2];
                for (var i = 0; i < bedAcross; i++)
                    run[i, 0] = Paint(Onto(tableEnds[dieCells - 1 - i], "roller", "Roller Tile", "-z table"), paint);
                Chain(run, "Roller Tile", "+z", "-z", "-x", "+x", paint, rowZero: true);
            }
            else
                run = FormerStage(tableEnds, dieCells, bedAcross, bedAlong, tables, paint);
            if (cutAlong == 0)
            {
                // Line D: the skins wait on a pad two rows deep for the crane (it sends nowhere: the station's crane order takes them).
                var skins = new VPart[bedAcross, 2];
                for (var i = 0; i < bedAcross; i++)
                    skins[i, 0] = Paint(Onto(run[i, 1], "+z low", "Pad Tile", "-z"), Stock);
                Chain(skins, "Pad Tile", "+z", "-z", "-x", "+x", Stock, rowZero: false);
                Stack(skins[0, 0], "top", 1);
                return;
            }
            CutterStage(run, bedAcross, padAcross, cutAlong, paint);
        }

        /// <summary>The feed run from the extruder's tables, the Panel Former (bed, corner masts, Out Tables) and a two-row run off it.</summary>
        private VPart[,] FormerStage(VPart[] tableEnds, int dieCells, int bedAcross, int bedAlong, int tables, string paint)
        {
            // The feed run from the tables to the former: as wide as the die, rows down to the former's Piece in (its tiles butted on the tables).
            var feedRows = (int)MathF.Round((ExtruderCoreZ - 0.75f - 8 * tables - (FormerZ + 1)) / 2);
            var feed = new VPart[dieCells, feedRows];
            for (var i = 0; i < dieCells; i++)
                feed[i, 0] = Paint(Onto(tableEnds[dieCells - 1 - i], "roller", "Roller Tile", "-z table"), paint); // (west to east)
            Chain(feed, "Roller Tile", "+z", "-z", "-x", "+x", paint, rowZero: true);

            // ---- The Panel Former: Core, bed, a Mast Block on each bed corner (2 m more pin travel a level), Out Tables across the last row. --
            var mid = (bedAcross - 1) / 2; // (the Core over the middle column, the west one of two middles)
            var former = Paint(Onto(feed[Math.Min(mid, dieCells - 1), feedRows - 1], "+z", "Panel Former", "-z"), paint);
            OrdersOnly(former);
            var bed = new VPart[bedAcross, bedAlong];
            bed[mid, 0] = Paint(Onto(former, "+z", "Bed Block", "-z"), paint);
            for (var i = mid - 1; i >= 0; i--)
                bed[i, 0] = Paint(Onto(bed[i + 1, 0], "+x", "Bed Block", "-x"), paint); // (South: own +x is west)
            for (var i = mid + 1; i < bedAcross; i++)
                bed[i, 0] = Paint(Onto(bed[i - 1, 0], "-x", "Bed Block", "+x"), paint);
            Chain(bed, "Bed Block", "+z", "-z", "-x", "+x", paint, rowZero: false);
            foreach (var c in Corners(bed))
                Stack(c, "top", 1);
            var outs = new VPart[bedAcross, 1];
            for (var i = 0; i < bedAcross; i++)
                outs[i, 0] = Paint(Onto(bed[i, bedAlong - 1], "+z", "Out Table", "-z"), paint);

            // ---- A run to the skin pad, the pad turned to send east (its north edge takes the run). --------------------------------------------
            var run = new VPart[bedAcross, 2];
            for (var i = 0; i < bedAcross; i++)
                run[i, 0] = Paint(Onto(outs[i, 0], "+z", "Roller Tile", "-z"), paint);
            Chain(run, "Roller Tile", "+z", "-z", "-x", "+x", paint, rowZero: false);
            return run;
        }

        /// <summary>The skin pad turned east off a run, its feed tile and the Laser Cutter with its bed, Out Tables and pick runs.</summary>
        private void CutterStage(VPart[,] run, int bedAcross, int padAcross, int cutAlong, string paint)
        {
            // The pad: two tiles along its flow (east), padAcross rows south; turned East, its own +z is east and its own +x south.
            var pad = new VPart[2, padAcross];
            pad[0, 0] = Paint(Onto(run[0, 1], "+z low", "Pad Tile", "-x"), Stock);
            for (var j = 1; j < padAcross; j++)
                pad[0, j] = Paint(Onto(pad[0, j - 1], "+x", "Pad Tile", "-x"), Stock);
            for (var j = 0; j < padAcross; j++)
                pad[1, j] = Paint(Onto(pad[0, j], "+z", "Pad Tile", "-z"), Stock);
            Stack(pad[0, 0], "top", 1);


            // ---- The Laser Cutter: fed east from the pad by a one-tile run, its bed south of its Core, Out Tables on the far (south) edge. -----
            var feedEast = new VPart[padAcross];
            for (var j = 0; j < padAcross; j++)
                feedEast[j] = Paint(Onto(pad[1, j], "+z", "Roller Tile", "-z low"), paint);
            var cutter = Paint(Onto(feedEast[0], "+z", "Laser Cutter", "-z"), paint);
            OrdersOnly(cutter);
            var cut = new VPart[bedAcross, cutAlong];
            for (var j = 0; j < cutAlong; j++)
            {
                cut[0, j] = Paint(Onto(j == 0 ? cutter : cut[0, j - 1], "+x", "Laser Bed Block", "-x"), paint);
                for (var i = 1; i < bedAcross; i++)
                    cut[i, j] = Paint(Onto(cut[i - 1, j], "+z", "Laser Bed Block", "-z"), paint);
            }
            for (var i = 0; i < bedAcross; i++)
            {
                var t = Paint(Onto(cut[i, cutAlong - 1], "+x", "Out Table", "-z"), paint);
                // The pick run: two rows south of the table, no way on, so the cut piece waits at its end for the Hall Crane.
                var r1 = Paint(Onto(t, "+z", "Roller Tile", "-z"), paint);
                Paint(Onto(r1, "+z", "Roller Tile", "-z"), paint);
            }
        }

        /// <summary>
        /// Fills a grid of block parts by welding: each row's first cell south of the one above it (<paramref name="alongFrom"/> on the cell above,
        /// <paramref name="alongBy"/> on the new one), then the rest of the row east of its first (<paramref name="acrossFrom"/>, <paramref name="acrossBy"/>).
        /// <paramref name="rowZero"/>: row 0 is already laid whole; else only its first... (cells already set are kept).
        /// </summary>
        private void Chain(VPart[,] g, string part, string alongFrom, string alongBy, string acrossFrom, string acrossBy, string paint, bool rowZero)
        {
            for (var j = 0; j < g.GetLength(1); j++)
                for (var i = 0; i < g.GetLength(0); i++)
                {
                    if (g[i, j] != null)
                        continue;
                    g[i, j] = j > 0
                        ? Paint(Onto(g[i, j - 1], alongFrom, part, alongBy), paint)
                        : Paint(Onto(g[i - 1, j], acrossFrom, part, acrossBy), paint);
                }
        }

        /// <summary>
        /// A Sheet Extruder whose Core stands at x <paramref name="coreX"/> facing south, <paramref name="cells"/> - 1 Die Blocks welded west of
        /// it (a die <paramref name="cells"/> x 2 m wide, 2 m tall), <paramref name="tables"/> Run-out Tables south of each cell. Thin sheet, by order.
        /// </summary>
        private VPart[] SheetExtruder(float coreX, int cells, int tables, string paint)
        {
            var ends = new VPart[cells];
            var core = On("Extruder", coreX, ExtruderCoreZ, South, paint: paint);
            OrdersOnly(core);
            var cell = core;
            for (var k = 0; k < cells; k++)
            {
                cell = Paint(Onto(cell, "+x", "Die Block", "-x"), paint); // (the die: Die Blocks only; the Core's own small die stays bare)
                var t = Onto(cell, "outBed", "Run-out Table", "in");
                Paint(t, paint);
                for (var n = 1; n < tables; n++)
                {
                    t = Onto(t, "out", "Run-out Table", "in");
                    Paint(t, paint);
                }
                ends[k] = t;
            }
            Chute(_bins["steel"], core, 9f);
            return ends;
        }

        /// <summary>
        /// A rectangle of block parts standing on the floor: <paramref name="across"/> east from column x <paramref name="x0"/> and
        /// <paramref name="along"/> south from row z <paramref name="z0"/> (cell centres, 2 m apart), each turned as <paramref name="o"/>.
        /// </summary>
        private VPart[,] Tiles(string part, float x0, float z0, int across, int along, Orient o, string paint)
        {
            var g = new VPart[across, along];
            for (var j = 0; j < along; j++)
                for (var i = 0; i < across; i++)
                {
                    g[i, j] = On(part, x0 + 2 * i, z0 - 2 * j, o, paint: paint);
                }
            return g;
        }

        private static IEnumerable<VPart> Corners(VPart[,] g)
        {
            int a = g.GetLength(0) - 1, b = g.GetLength(1) - 1;
            yield return g[0, 0];
            if (a > 0)
                yield return g[a, 0];
            if (b > 0)
                yield return g[0, b];
            if (a > 0 && b > 0)
                yield return g[a, b];
        }

        /// <summary>A column of <paramref name="n"/> Mast Blocks on <paramref name="on"/>'s <paramref name="socket"/>.</summary>
        private VPart Stack(VPart on, string socket, int n)
        {
            var m = on;
            for (var k = 0; k < n; k++)
            {
                m = Onto(m, k == 0 ? socket : "top", "Mast Block", "bottom");
                Paint(m, Steel);
            }
            return m;
        }
    }
}
