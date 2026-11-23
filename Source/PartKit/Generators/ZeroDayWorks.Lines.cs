using System.Numerics;
using CallOfSlop.VehicleKit;

namespace CallOfSlop.PartKit.Generators;

public sealed partial class ZeroDayWorks
{
    private sealed partial class Script
    {
        // The armour lines (doc "Armour-forming lines"), each north to south: its own Sheet Extruder at the head (the inline extruder strip),
        // a roller run onto its Panel Former's bed (the former's Core beside the bed's head), the bed and Out Tables, a short run. Lines A to C
        // then run the skin straight on south onto a Laser Cutter's bed (a cutter takes its blank by conveyor only; its Core stands on the
        // bed's long east side, so the gantry's legs stand along the long sides); the cut piece waits at a short run's end, where the bay's Hall Crane picks it. From there the crane carries each piece to
        // the dress station's bay, the dressed group to the Bake Oven's bed and the baked part to the line's stores. Line D has no cutter: its
        // skins wait on their pad for the crane and its Building Station, which builds each end before the oven bakes it.

        private readonly List<VPart> _stations = new();
        private readonly List<VPart> _ovens = new(), _ovenMasts = new(); // (each oven's Core; the foot Mast Block on each bed corner)

        public const float ExtruderCoreZ = -43f; // an Extruder Core's centre: its die's foot (outBed, 0.75 m south) at z -44, so 16 m of Run-out Tables end at -60
        public const float FormerZ = -69f; // the row north of the Panel Formers' beds (their head row at z -71, their Cores beside it): the feed run's last row

        private readonly List<(string Line, VPart Store)> _stores = new();

        private void LayArmourLines()
        {
            // Bay 1 (x 2 to 30, the 30 m span): lines A and B side by side, the same line twice (a Hull Section key carries its side letter).
            foreach (var (line, x0, paint, east) in new[] { ("A", 5f, LineA, 13f), ("B", 19f, LineB, 27f) })
            {
                ArmourLine(line, x0, paint, bedAcross: 3, bedAlong: 7, tables: 2, cutAlong: 7);
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
            ArmourLine("C", 33, LineC, bedAcross: 6, bedAlong: 12, tables: 1, cutAlong: 12);
            // (the bay 20 m long, room for a deck's 17 m kit; the store 18 x 12 m, the kit station's pick pad and the yard station's: a deck's 10 m plates are
            // too long for a station's hold, so the bay crane lays them there off the extruder's run and fits them from it)
            var stationC = Station(47, -137, 33, -135, 7, 10, 3, LineC);
            // The oven's Core east of its bed's first row (it stood in the row between the bay and the bed): a finished kit rolls out of the
            // station's bay south along the Roller Tile in that row, straight onto the bed's middle (ConstructRollOut.cs), no crane.
            var ovenC = Oven(47, -157, 33, 7, 13, 3, LineC, bedZ0: -157, facing: West);
            Tiles("Roller Tile", 39, -155, 1, 1, South, LineC);
            Stores("C", 33, -185, 9, 6, LineC, second: false, transferRows: 0, eastWest: true, masts: 4); // (18 m across the bay, its hold's length east-west: a deck is 17.25 m long, and the gable wall is 12 m south; four masts a corner, an 8 m stack: the Stern's side skins stand 6.4 m)
            LineOrders(stationC, _lineExtruders["C"], 49, -133, LineC, _lineFormers["C"]); // (the former too: the keel's skins are its orders)
            _makers.Add(ovenC); // (the yard's station orders the frigate's decks of it: BakeKits.cs)
            foreach (var z in new[] { -143f, -148f, -153f })
                Arm("Industrial Loader Arm", 47, z); // (on the station's east side between the columns: a Plasma arm's 3.6 m base doesn't fit there)
            // Bay 2 east span (x 52 to 70): line D, the heavy ends: no cutter; its Building Station builds each end from skins, frames and fittings.
            ArmourLine("D", 53, LineD, bedAcross: 6, bedAlong: 8, tables: 1, cutAlong: 0);
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
        private VPart Station(float coreX, float coreZ, float bayX0, float bayZ0, int across, int along, int masts, string paint, Orient? turned = null)
        {
            var core = On("Building Station", coreX, coreZ, turned ?? South, "-y.0.1", paint);
            var bay = Tiles("Bay Block", bayX0, bayZ0, across, along, South, paint);
            foreach (var c in Corners(bay))
                Stack(c, "top", masts);
            _stations.Add(core);
            return core;
        }

        private readonly Dictionary<string, VPart> _lineExtruders = new(); // (each armour line's Sheet Extruder Core)
        private VPart? _lastSheet;
        private readonly Dictionary<string, VPart> _lineFormers = new(); // (each armour line's Panel Former Core)
        private VPart? _lastFormer;

        /// <summary>
        /// What a line's own station orders through: a Line Console (started) at (<paramref name="x"/>, <paramref name="z"/>), its Run wired to
        /// the line's Sheet Extruder (and its Panel Former, at 6.5 m) and its wire into the station's Orders port, carried at 6 m.
        /// </summary>
        private void LineOrders(VPart station, VPart extruder, float x, float z, string paint, VPart? former = null)
        {
            const float up = 6f;
            var console = Paint(On("Line Console", x, z, West, paint: paint), paint);
            Config(console, ("startOn", 1));
            var at = SocketAt(console, "bottom");
            var to = SocketAt(extruder, extruder.Def.Sockets[0].Name);
            Link("data", console, extruder, null, "Run", paint, false, null, Cut(at, V(at.X, up, at.Z), V(at.X, up, to.Z), V(to.X, up, to.Z)));
            var s = SocketAt(station, "-y.0.1");
            Link("data", console, station, null, "Orders", paint, false, null, Cut(at, V(at.X, up, at.Z), V(s.X, up, at.Z), V(s.X, up, s.Z)));
            if (former != null)
            {
                var f = SocketAt(former, former.Def.Sockets[0].Name);
                Link("data", console, former, null, "Run", paint, false, null, Cut(at, V(at.X, up + 0.5f, at.Z), V(at.X, up + 0.5f, f.Z), V(f.X, up + 0.5f, f.Z)));
            }
        }

        /// <summary>A Loader Arm whose base's south-west corner is at (<paramref name="x"/>, <paramref name="z"/>), the base reaching north and east.</summary>
        private VPart Arm(string part, float x, float z) => On(part, x, z, Up, "-y.0.0", Crane);

        /// <summary>A Bake Oven: its Core at (x, z) facing south, its bed south of it from column <paramref name="bedX0"/>, masts on the bed's corners.</summary>
        private VPart Oven(float coreX, float coreZ, float bedX0, int across, int along, int masts, string paint, float? bedZ0 = null, Orient? facing = null)
        {
            var core = On("Bake Oven", coreX, coreZ, facing ?? South, paint: paint);
            var bed = Tiles("Oven Bed Block", bedX0, bedZ0 ?? coreZ - 2, across, along, South, paint);
            foreach (var c in Corners(bed))
            {
                var first = Stack(c, "top", 1);
                _ovenMasts.Add(first); // (the heat shields and gauges hang on it: Dressing)
                Stack(first, "top", masts - 1);
            }
            _ovens.Add(core);
            return core;
        }

        /// <summary>
        /// A line's piece store: a pad of Pad Tiles (a Mast Block on each corner, the stack 3 m), with <paramref name="second"/> a run of one
        /// row and a second pad below it; then a transfer run of <paramref name="transferRows"/> rows, three tiles wide, south from the last pad's middle.
        /// </summary>
        private void Stores(string line, float x0, float z0, int across, int along, string paint, bool second, int transferRows, bool eastWest = false, int masts = 1)
        {
            var pad = Tiles("Pad Tile", x0, z0, across, along, eastWest ? East : South, Stock); // (East: the hold's length runs across the bay)
            foreach (var c in Corners(pad))
                Stack(c, "top", masts); // (each mast raises the hold's stack: a lower deck is 3.25 m tall)
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
        private void ArmourLine(string line, float x0, string paint, int bedAcross, int bedAlong, int tables, int cutAlong)
        {
            var dieCells = bedAcross; // (the sheet as wide as the bed: a Die Block a 2 m column)

            // ---- The Sheet Extruder: its Core at the die's east end, Die Blocks welded west, Run-out Tables south of every cell. --------------
            // Everything after it is welded on in a chain from its tables (a table's far end is a quarter metre off the floor's 2 m grid).
            var tableEnds = SheetExtruder(x0 + 2 * dieCells, dieCells, tables, paint);
            _lineExtruders[line] = _lastSheet!;
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
            {
                run = FormerStage(tableEnds, dieCells, bedAcross, bedAlong, tables, paint);
                _lineFormers[line] = _lastFormer!;
            }
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
            CutterStage(run, bedAcross, cutAlong, paint);
        }

        /// <summary>The feed run from the extruder's tables, the Panel Former (bed, corner masts, Out Tables) and a two-row run off it.</summary>
        private VPart[,] FormerStage(VPart[] tableEnds, int dieCells, int bedAcross, int bedAlong, int tables, string paint)
        {
            // The feed run from the tables to the former's bed: as wide as the die, rows down to the bed's head (its tiles butted on the
            // tables), so each blank rides off the run straight onto the bed (RollerRuns.Butted: the Core's Bed in), never through the Core.
            var feedRows = (int)MathF.Round((ExtruderCoreZ - 0.75f - 8 * tables - (FormerZ - 1)) / 2);
            var feed = new VPart[dieCells, feedRows];
            for (var i = 0; i < dieCells; i++)
                feed[i, 0] = Paint(Onto(tableEnds[dieCells - 1 - i], "roller", "Roller Tile", "-z table"), paint); // (west to east)
            Chain(feed, "Roller Tile", "+z", "-z", "-x", "+x", paint, rowZero: true);

            // ---- The Panel Former: bed (welded on the run's last row), its Core beside the bed's head on the east, a Mast Block on each bed
            // corner (2 m more pin travel a level), Out Tables across the last row (the bed's far end: MachineBlocks.FindOut). ---------------
            var bed = new VPart[bedAcross, bedAlong];
            for (var i = 0; i < bedAcross; i++)
                bed[i, 0] = Paint(Onto(feed[i, feedRows - 1], "+z", "Bed Block", "-z"), paint);
            Chain(bed, "Bed Block", "+z", "-z", "-x", "+x", paint, rowZero: false);
            var former = Paint(Onto(bed[bedAcross - 1, 0], "-x", "Panel Former", "+z"), paint); // (South: a block's own -x is east)
            OrdersOnly(former);
            _lastFormer = former;
            foreach (var c in Corners(bed))
                Stack(c, "top", 1);
            var outs = new VPart[bedAcross, 1];
            for (var i = 0; i < bedAcross; i++)
                outs[i, 0] = Paint(Onto(bed[i, bedAlong - 1], "+z", "Out Table", "-z"), paint);

            // ---- A run on south: onto the Laser Cutter's bed (lines A to C), or line D's skin pad. ------------------------------------------------------
            var run = new VPart[bedAcross, 2];
            for (var i = 0; i < bedAcross; i++)
                run[i, 0] = Paint(Onto(outs[i, 0], "+z", "Roller Tile", "-z"), paint);
            Chain(run, "Roller Tile", "+z", "-z", "-x", "+x", paint, rowZero: false);
            return run;
        }

        /// <summary>The Laser Cutter straight on a run's end: its bed, its Core on the bed's east side, Out Tables and pick runs.</summary>
        private void CutterStage(VPart[,] run, int bedAcross, int cutAlong, string paint)
        {
            // ---- The Laser Cutter (Sean, 2026-11-23: the gantry's legs on the long sides, the run feeding straight in): its bed welded on the
            // run's last row, as wide as the run and cutAlong blocks south, so each blank rides off the rollers straight onto the bed's north
            // (short) end (RollerRuns.Butted: the Core's Bed in). Its Core stands on the bed's long east side at the head row, so the bridge's
            // rails run along the long sides, the bridge spans the bed's width with one head a column, parks over the in end and runs south.
            var cut = new VPart[bedAcross, cutAlong];
            for (var i = 0; i < bedAcross; i++)
                cut[i, 0] = Paint(Onto(run[i, run.GetLength(1) - 1], "+z", "Laser Bed Block", "-z"), paint);
            Chain(cut, "Laser Bed Block", "+z", "-z", "-x", "+x", paint, rowZero: false);
            var cutter = Paint(Onto(cut[bedAcross - 1, 0], "-x", "Laser Cutter", "+x"), paint); // (a block's own -x is east)
            OrdersOnly(cutter);
            for (var i = 0; i < bedAcross; i++)
            {
                var t = Paint(Onto(cut[i, cutAlong - 1], "+z", "Out Table", "-z"), paint);
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
            // The machine stands on Extruder Plinths, 1 m up (Sean, 2026-11-22: "conveyor height differences"): a Run-out Table's top is level
            // with its Extruder's foot, so on the floor its tables lay 0.7 m under the Roller Tiles' rollers and the beds the run feeds. Raised,
            // the die's foot, the tables, the run and the next machine's bed are one level, and each table stands on legs to the floor.
            var plinth = Finish2(On("Extruder Plinth", coreX, ExtruderCoreZ, South), "concrete");
            var core = Paint(Stand("Extruder", SocketAt(plinth, "top"), South, plinth, "bottom"), paint);
            OrdersOnly(core);
            _lastSheet = core;
            var cell = core;
            for (var k = 0; k < cells; k++)
            {
                cell = Paint(Onto(cell, "+x", "Die Block", "-x"), paint); // (the die: Die Blocks only; the Core's own small die stays bare)
                Finish2(On("Extruder Plinth", coreX - 2 * (k + 1), ExtruderCoreZ, South), "concrete");
                var t = Onto(cell, "outBed", "Run-out Table", "in");
                Paint(t, paint);
                TableStand(t);
                for (var n = 1; n < tables; n++)
                {
                    t = Onto(t, "out", "Run-out Table", "in");
                    Paint(t, paint);
                    TableStand(t);
                }
                ends[k] = t;
            }
            Chute(_bins["steel"], core, 9f);
            return ends;
        }

        private VPart Finish2(VPart p, string finish)
        {
            Finish(p, finish);
            return p;
        }

        /// <summary>A Run-out Table Stand under a raised Run-out Table, welded on the floor slab under its middle (the table's foot is off the 2 m grid).</summary>
        private void TableStand(VPart table)
        {
            var (lo, hi) = BoundsOf(table);
            var d = Def("Run-out Table Stand");
            var mid = (lo + hi) * 0.5f;
            var centre = V(mid.X, FloorY + 0.003f + (d.BoundsMax.Y - d.BoundsMin.Y) * 0.5f, mid.Z);
            Paint(Weld("Run-out Table Stand", Tile(mid.X, mid.Z), centre, South, 0, 0, 0), Steel);
        }

        /// <summary>A part's bounds in site coordinates (a part on the grid, not a freeform weld).</summary>
        private (Vector3 Lo, Vector3 Hi) BoundsOf(VPart p)
        {
            ref var cp = ref Live.Parts[p.Index];
            var a = FromRest(cp.Pos + Rot24.Apply(cp.Rot, p.Def.BoundsMin));
            var b = FromRest(cp.Pos + Rot24.Apply(cp.Rot, p.Def.BoundsMax));
            return (Vector3.Min(a, b), Vector3.Max(a, b));
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
