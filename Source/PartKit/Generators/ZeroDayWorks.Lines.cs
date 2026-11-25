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
        private readonly List<VPart> _ovens = new(); // (each oven's Core)

        public const float ExtruderCoreZ = -43f; // an Extruder Core's centre: its die's foot (outBed, 0.75 m south) at z -44, so 16 m of Run-out Tables end at -60
        public const float FormerZ = -69f; // the row north of the Panel Formers' beds (their head row at z -71, their Cores beside it): the feed run's last row

        private readonly List<(string Line, VPart Store)> _stores = new();
        private readonly Dictionary<string, VPart[]> _lineCutOuts = new();
        private readonly Dictionary<string, VPart[,]> _skinPads = new(); // (line D's skins pad, its station's bay welded on it)

        private void LayArmourLines()
        {
            // Bay 1 (x 2 to 30, the 30 m span): lines A and B side by side, the same line twice (a Hull Section key carries its side letter).
            foreach (var (line, x0, paint, east) in new[] { ("A", 5f, LineA, 13f), ("B", 19f, LineB, 27f) })
            {
                ArmourLine(line, x0, paint, bedAcross: 3, bedAlong: 7, tables: 2, cutAlong: 7);
                // The dress station (a small Building Station: Core east of a 5 x 9 bay, Mast Blocks 3 high on its corners), its Bake Oven,
                // then two piece stores of 5 x 6 Pad Tiles with a run between them, and the transfer run south to the yard.
                // (the cutter's runs end at z -109: a row on, then the pad at -113 and -115, the bay from -117. The runs are a quarter metre off
                // the floor's 2 m grid (the extruder's tables), so the bay is welded on the pad's south edge, butted with no gap, and the
                // Core on the bay: Sean 2026-11-24)
                var pad = RollThrough(_lineCutOuts[line], 2, paint);
                var station = Station(east, -119, east - 10, -117, 5, 9, 3, paint, onPad: (pad[0, 1], "+z", 1));
                // The oven's Core east of its bed's first row, out of the two rows between the bay and the bed, where two Roller Tiles under
                // both middles roll a finished kit onto the bed (ConstructRollOut.cs); its console runs the line for the station's kits, and
                // the yard's orders reach the oven (Sean, 2026-11-23: the frigate's interior walls are made on lines A and B).
                // (the tunnel 9 rows long, 18 m: its hearth between the doors takes the longest wall's 17 m kit. Its way out is a Roller
                // Tile in the row after it, where the first store's pad began: the pad starts a row on, 5 rows long, so a baked wall rolls
                // straight out of the tunnel onto the tile and the pad)
                var oven = Oven(east, -139, east - 10, 5, 9, 6, paint, bedZ0: -139, facing: West);
                Tiles("Roller Tile", east - 6, -135, 1, 2, South, paint);
                Tiles("Roller Tile", east - 6, -157, 1, 1, South, paint);
                LineOrders(station, _lineExtruders[line], east + 2, -115, paint, _lineFormers[line]);
                _makers.Add(oven);
                Stores(line, east - 10, -159, 5, 5, paint, second: true, transferRows: 8);
                // On the 4 m strip east of the station's and the oven's beds (west of the next line's or the columns): two Long arms by the
                // station's bay (14 m, 6 t, on a 2 m plinth: the strip is 2 m wide), south of its Core: the first reaches the pick pad at the bay's head, so the plates the cutter's runs roll onto that pad go
                // straight into the bay (Sean 2026-11-24: the Industrial arms there reached neither the pad nor a 2.3 t wall plate), and two
                // Industrial arms by the oven's bed.
                foreach (var z in new[] { -124f, -132f })
                    Arm("Long Loader Arm", east, z, turn: z == -124f ? 1 : 0); // (the first turned a quarter: its drive packs met the station, --arm-swap-find)
                foreach (var z in new[] { -143f, -149f })
                    Arm("Industrial Loader Arm", east, z);
            }
            // Bay 2 west span (x 32 to 50): line C, the wide flat pieces.
            ArmourLine("C", 33, LineC, bedAcross: 6, bedAlong: 12, tables: 1, cutAlong: 12);
            // (the bay 20 m long, room for a deck's 17 m kit; the store 18 x 12 m, the kit station's pick pad and the yard station's: a deck's 10 m plates are
            // too long for a station's hold, so the bay crane lays them there off the extruder's run and fits them from it)
            // (the cutter's six runs end at z -129.75, 4.25 m short of the bay: two rows more of Roller Tiles, and the bay welded on the runs'
            // ends, so the runs touch the bay (Sean 2026-11-24, "the lines should touch"). No pick pad between: a deck's 10 m plates and the
            // skins' 18 m are longer than the strip would hold, so they wait at the runs' ends, where the bay's Hall Crane fits them from.)
            var runC = new VPart[_lineCutOuts["C"].Length, 2];
            for (var i = 0; i < runC.GetLength(0); i++)
                runC[i, 0] = Paint(Onto(_lineCutOuts["C"][i], "+z", "Roller Tile", "-z"), LineC);
            Chain(runC, "Roller Tile", "+z", "-z", "-x", "+x", LineC, rowZero: true);
            var stationC = Station(47, -137, 33, -135, 7, 10, 3, LineC, onPad: (runC[0, 1], "+z low", 0));
            // The oven's Core east of its bed's first row (it stood in the row between the bay and the bed): a finished kit rolls out of the
            // station's bay south along the Roller Tile in that row, straight onto the bed's middle (ConstructRollOut.cs), no crane.
            var ovenC = Oven(47, -157, 33, 7, 13, 7, LineC, bedZ0: -157, facing: West);
            Tiles("Roller Tile", 39, -155, 1, 1, South, LineC);
            Tiles("Roller Tile", 39, -183, 1, 1, South, LineC); // (its way out: the row between the tunnel and the store)
            Stores("C", 33, -185, 9, 6, LineC, second: false, transferRows: 0, eastWest: true, masts: 4); // (18 m across the bay, its hold's length east-west: a deck is 17.25 m long, and the gable wall is 12 m south; four masts a corner, an 8 m stack: the Stern's side skins stand 6.4 m)
            LineOrders(stationC, _lineExtruders["C"], 49, -133, LineC, _lineFormers["C"]); // (the former too: the keel's skins are its orders)
            _makers.Add(ovenC); // (the yard's station orders the frigate's decks of it: BakeKits.cs)
            foreach (var z in new[] { -143f, -148f, -153f })
                Arm("Long Loader Arm", 47, z, turn: z == -153f ? 2 : 0); // (the last turned a quarter: its counterweights met the store's masts, --arm-swap-find)
                // (on the station's east side between the columns: a Plasma arm's 3.6 m base doesn't fit there; a Long arm's 2 m plinth does, and it lifts the deck plates, Sean 2026-11-24)
            // Bay 2 east span (x 52 to 70): line D, the heavy ends: no cutter; its Building Station builds each end from skins, frames and fittings.
            ArmourLine("D", 53, LineD, bedAcross: 6, bedAlong: 8, tables: 1, cutAlong: 0);
            Station(69, -103, 53, -99, 8, 8, 6, LineD, onPad: (_skinPads["D"][0, 1], "+z", 0), coreRow: 2); // (welded on the skins' pad: no walkway between them)
            // (its Core east of the bed's first row, so the tunnel runs north to south: three rows of Roller Tiles from the station's bay
            // into it, and one out of it before the store; the bed 7 rows, so that row is free)
            Oven(69, -121, 53, 8, 7, 12, LineD, bedZ0: -121, facing: West);
            Tiles("Roller Tile", 59, -115, 2, 3, South, LineD);
            Tiles("Roller Tile", 59, -135, 2, 1, South, LineD);
            Stores("D", 53, -137, 8, 6, LineD, second: false, transferRows: 25);
            Arm("Heavy Loader Arm", 52, -118); // (two Heavy arms at the station's south corners, either side of the oven's Core: a Plasma's 10 m reached three of its steps, Sean 2026-11-24)
            Arm("Heavy Loader Arm", 63, -118, turn: 2, shoulder: new Vector2(62.96f, -115.96f), preset: "Kit Heavy Arm XL"); // (1.6 m west and half turned, a longer preset with no pedestal, so it still reaches every job: the Heavy preset's mast stood in the Ore Bunker's chute to the Assembler, --arm-swap-find)
        }

        /// <summary>
        /// A Building Station: its Core at (<paramref name="coreX"/>, <paramref name="coreZ"/>) facing south with its bay west of it (the bay's
        /// north-west cell at <paramref name="bayX0"/>, <paramref name="bayZ0"/>), <paramref name="masts"/> Mast Blocks on each bay corner.
        /// <paramref name="onPad"/>: a Pad Tile or a run's last Roller Tile on the bay's north edge, its south socket and the bay column south
        /// of it. The bay is welded on that socket and the Core on the bay (a pad or run laid off the floor's grid), not stood on the floor.
        /// </summary>
        private VPart Station(float coreX, float coreZ, float bayX0, float bayZ0, int across, int along, int masts, string paint, Orient? turned = null,
            (VPart Tile, string Socket, int Column)? onPad = null, int coreRow = 1)
        {
            VPart core;
            VPart[,] bay;
            if (onPad is var (tile, socket, col))
            {
                bay = new VPart[across, along];
                bay[col, 0] = Paint(Onto(tile, socket, "Bay Block", "-z"), paint);
                for (var i = col - 1; i >= 0; i--)
                    bay[i, 0] = Paint(Onto(bay[i + 1, 0], "+x", "Bay Block", "-x"), paint); // (South: a block's own +x is west)
                for (var i = col + 1; i < across; i++)
                    bay[i, 0] = Paint(Onto(bay[i - 1, 0], "-x", "Bay Block", "+x"), paint);
                Chain(bay, "Bay Block", "+z", "-z", "-x", "+x", paint, rowZero: true);
                // The Core (a dock pad, its mouth welded to the bay) east of the bay beside its row coreRow (the second unless that is taken), turned as one stood South would be.
                core = Paint(Onto(bay[across - 1, coreRow], "-x", "Building Station", "+x.0", spin: 0), paint);
                var want = V(coreX, 0, coreZ) - V(bayX0, 0, bayZ0);
                var got = SocketAt(core, "-y.0.0") - SocketAt(bay[0, 0], "bottom");
                if (MathF.Abs(got.X - want.X) > 0.01f || MathF.Abs(got.Z - want.Z) > 0.01f)
                    throw new VehicleKitException($"Zero Day Works: a station's Core welded on its bay sits at {got} from the bay, not {want}");
            }
            else
            {
                core = On("Building Station", coreX, coreZ, turned ?? South, "-y.0.0", paint);
                bay = Tiles("Bay Block", bayX0, bayZ0, across, along, South, paint);
            }
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
            var s = SocketAt(station, "-y.0.0");
            Link("data", console, station, null, "Orders", paint, false, null, Cut(at, V(at.X, up, at.Z), V(s.X, up, at.Z), V(s.X, up, s.Z)));
            if (former != null)
            {
                var f = SocketAt(former, former.Def.Sockets[0].Name);
                Link("data", console, former, null, "Run", paint, false, null, Cut(at, V(at.X, up + 0.5f, at.Z), V(at.X, up + 0.5f, f.Z), V(f.X, up + 0.5f, f.Z)));
            }
        }

        /// <summary>A Loader Arm whose base's south-west corner is at (<paramref name="x"/>, <paramref name="z"/>), the base reaching north and east.</summary>
        /// <summary>
        /// A loader arm where a fixed arm <paramref name="part"/> stood at (x, z) by its socket <paramref name="by"/>: that arm's kit preset
        /// (ArmKitMigrate.Map: at least its reach and load; <paramref name="preset"/> names another), its shoulder over the fixed arm's, or at
        /// <paramref name="shoulder"/> and turned <paramref name="turn"/> quarters (anticlockwise from above) where the preset meets something
        /// there (`--arm-swap-find`: the nearest place it fits and reaches every job the fixed arm did, Sean 2026-11-24). Hands back its Arm Core.
        /// </summary>
        private VPart Arm(string part, float x, float z, string by = "-y.0.0", int turn = 0, Vector2? shoulder = null, string? preset = null, bool keep = false)
        {
            if (keep)
                return On(part, x, z, Up, by, Crane); // (a fixed arm no kit preset fits in for now: Sean's to settle)
            preset ??= ArmKitMigrate.PresetFor(part) ?? throw new VehicleKitException($"no kit preset for a {part}");
            var t0 = FirstTurn(Up);
            // (where the fixed arm's shoulder stood, by its old numbers: its part file went, 2026-11-24; its socket "-y.0.0" is 0.125 m under its origin)
            var mid = V(x, FloorY, z) + Rot24.Apply(t0, ArmKitMigrate.OldBase.TryGetValue(part, out var ob) ? (by == "centre -y" ? Vector3.Zero : ob with { Y = 0.125f }) // ("centre -y": the old part stood by its footprint's middle)
                : (Def(part).BoundsMin + Def(part).BoundsMax) * 0.5f - Array.Find(Def(part).Sockets, s => s.Name == by).At);
            if (shoulder is { } at)
                mid = new Vector3(at.X, mid.Y, at.Y);
            var face = Vector3.Transform(Vector3.UnitZ, Quaternion.CreateFromAxisAngle(Vector3.UnitY, turn * MathF.PI / 2));
            var o = new Orient(Vector3.UnitY, Vector3.UnitY, Vector3.UnitZ, new Vector3(MathF.Round(face.X), 0, MathF.Round(face.Z)));
            var steps = ArmKitParts.PresetSteps(preset);
            var placed = new VPart[steps.Count];
            VPart? core = null;
            for (var k = 0; k < steps.Count; k++)
            {
                var (p, on, socket, own) = steps[k];
                if (on < 0)
                {
                    var d0 = Def(p);
                    var lift = Rot24.Apply(t0, (d0.BoundsMin + d0.BoundsMax) * 0.5f - Array.Find(d0.Sockets, s => s.Name == "bottom").At);
                    placed[k] = StandNear(p, mid with { Y = V(x, FloorY, z).Y + lift.Y }, o);
                }
                else
                    placed[k] = p is "Arm Core" or "Arm Pedestal" && turn != 0 ? Onto(placed[on], socket, p, own, spin: SpinTo(placed[on], socket, p, own, FirstTurn(o))) : Onto(placed[on], socket, p, own); // (the Core and pedestals turned alike)
                if (!p.StartsWith("Pressed Part"))
                    Paint(placed[k], Crane);
                if (p == "Arm Core")
                    core = placed[k];
            }
            return core!;
        }

        /// <summary>The spin that turns <paramref name="part"/> on <paramref name="on"/>'s <paramref name="socket"/> to <paramref name="rot"/>.</summary>
        private int SpinTo(VPart on, string socket, string part, string by, byte rot)
        {
            for (var sp = 0; sp < 4; sp++)
                if (TurnOf(on, socket, part, by, sp) == rot)
                    return sp;
            throw new VehicleKitException($"no spin turns a {part} on a {on.Name}'s \"{socket}\" to turn {rot}");
        }

        /// <summary>
        /// A part by its bottom on the floor, its middle on the whole metre nearest <paramref name="centre"/> that is free (the floor takes a part on
        /// whole metres; a kit arm's shoulder is at a fixed arm's, which wasn't): within 1.5 m, nearest first. Throws when none is.
        /// </summary>
        private VPart StandNear(string part, Vector3 centre, Orient o)
        {
            var spots = new List<Vector2>();
            for (var dx = -2; dx <= 2; dx++)
                for (var dz = -2; dz <= 2; dz++)
                    spots.Add(new Vector2(MathF.Round(centre.X) + dx, MathF.Round(centre.Z) + dz));
            spots.Sort((a, b) => Vector2.DistanceSquared(a, new Vector2(centre.X, centre.Z)).CompareTo(Vector2.DistanceSquared(b, new Vector2(centre.X, centre.Z))));
            foreach (var s in spots)
            {
                if (Vector2.Distance(s, new Vector2(centre.X, centre.Z)) > 1.5f)
                    break;
                try
                {
                    return Put(part, new Vector3(s.X, centre.Y, s.Y), o, 0.05f, Tile(s.X, s.Y), "bottom");
                }
                catch (VehicleKitException)
                {
                }
            }
            throw new VehicleKitException($"no free floor within 1.5 m of ({centre.X:0.00}, {centre.Z:0.00}) for a {part}");
        }

        /// <summary>
        /// A Bake Oven: its Core at (x, z) facing south, its bed south of it from column <paramref name="bedX0"/>, its roller hearth tunnel
        /// <paramref name="height"/> m clear over the rollers (the Core's Tunnel height). The tunnel runs along the bed's side the Core stands by.
        /// </summary>
        private VPart Oven(float coreX, float coreZ, float bedX0, int across, int along, float height, string paint, float? bedZ0 = null, Orient? facing = null)
        {
            var core = On("Bake Oven", coreX, coreZ, facing ?? South, paint: paint);
            Config(core, ("height", height));
            Tiles("Oven Bed Block", bedX0, bedZ0 ?? coreZ - 2, across, along, South, paint);
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
                _skinPads[line] = skins;
                return;
            }
            _lineCutOuts[line] = CutterStage(run, bedAcross, cutAlong, paint);
        }

        /// <summary>The feed run from the extruder's tables, the Panel Former (bed, Out Tables) and a two-row run off it.</summary>
        private VPart[,] FormerStage(VPart[] tableEnds, int dieCells, int bedAcross, int bedAlong, int tables, string paint)
        {
            // The feed run from the tables to the former's bed: as wide as the die, rows down to the bed's head (its tiles butted on the
            // tables), so each blank rides off the run straight onto the bed (RollerRuns.Butted: the Core's Bed in), never through the Core.
            var feedRows = (int)MathF.Round((ExtruderCoreZ - 0.75f - 8 * tables - (FormerZ - 1)) / 2);
            var feed = new VPart[dieCells, feedRows];
            for (var i = 0; i < dieCells; i++)
                feed[i, 0] = Paint(Onto(tableEnds[dieCells - 1 - i], "roller", "Roller Tile", "-z table"), paint); // (west to east)
            Chain(feed, "Roller Tile", "+z", "-z", "-x", "+x", paint, rowZero: true);

            // ---- The Panel Former: bed (welded on the run's last row), its Core beside the bed's head on the east (a stretch former: no
            // masts, its die sinks into a pocket), Out Tables across the last row (the bed's far end: MachineBlocks.FindOut). ---------------
            var bed = new VPart[bedAcross, bedAlong];
            for (var i = 0; i < bedAcross; i++)
                bed[i, 0] = Paint(Onto(feed[i, feedRows - 1], "+z", "Bed Block", "-z"), paint);
            Chain(bed, "Bed Block", "+z", "-z", "-x", "+x", paint, rowZero: false);
            var former = Paint(Onto(bed[bedAcross - 1, 0], "-x", "Panel Former", "+z"), paint); // (South: a block's own -x is east)
            OrdersOnly(former);
            _lastFormer = former;
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
        private VPart[] CutterStage(VPart[,] run, int bedAcross, int cutAlong, string paint)
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
            var ends = new VPart[bedAcross];
            for (var i = 0; i < bedAcross; i++)
            {
                var t = Paint(Onto(cut[i, cutAlong - 1], "+z", "Out Table", "-z"), paint);
                // The pick run: rows south of the table, so the cut piece waits at its end for the Hall Crane, or rolls on onto a pad.
                var r = Paint(Onto(t, "+z", "Roller Tile", "-z"), paint);
                ends[i] = Paint(Onto(r, "+z", "Roller Tile", "-z"), paint);
            }
            return ends;
        }

        /// <summary>
        /// Lines A and B's roll-through (Sean, 2026-11-23): the cutter's out runs on one row more onto a raised pad of Pad Tiles butted on the
        /// dress station's bay's north end, which the station takes as its pick pad. Each piece rolls from the Extruder through the Panel Former
        /// and the Laser Cutter (across a bed it has no work on) to that pad, where the station's crane picks it (ConstructStation pick pads).
        /// </summary>
        private VPart[,] RollThrough(VPart[] ends, int rows, string paint)
        {
            var pad = new VPart[ends.Length, rows];
            for (var i = 0; i < ends.Length; i++)
            {
                var r = Paint(Onto(ends[i], "+z", "Roller Tile", "-z"), paint);
                pad[i, 0] = Paint(Onto(r, "+z low", "Pad Tile", "-z"), Stock);
            }
            Chain(pad, "Pad Tile", "+z", "-z", "-x", "+x", Stock, rowZero: true);
            return pad;
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
