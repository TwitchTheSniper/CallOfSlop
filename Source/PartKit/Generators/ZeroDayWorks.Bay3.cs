using System.Numerics;
using CallOfSlop.VehicleKit;

namespace CallOfSlop.PartKit.Generators;

public sealed partial class ZeroDayWorks
{
    private sealed partial class Script
    {
        // Bay 3, the general line (doc "Bay 3"), under the 24 m span (rails at x 71 and 95; clear x 73 to 93), north to south:
        // the walls line (a 2-cell Sheet Extruder whose run goes straight onto a 2 x 9 Laser Cutter's bed), the module Building Station and its
        // stock pad, four Presses in two rows with their Die Racks and the Die Lathe between, the block Assembler with its Tool Bay and
        // Block Rack, then the walls and fittings stores.
        public const string General2 = "#3a9f99";

        private readonly List<VPart> _presses = new(); // (north row west, north row east, south row west, south row east)
        private VPart? _blockAssembler;

        /// <summary>
        /// The presses' conveyors (Sean, 2026-11-22: the presses were meant to send their many small parts on by conveyor, not have a crane
        /// lift each one). Each Press's Piece out goes on a conveyor; the south row's run straight into two Large Conveyor Joins in the aisle
        /// between the rows, the north row's rise from the aisle north of them, cross over their beds at 6.5 m and drop into the same Joins.
        /// The Joins feed a Large Conveyor Router, whose three ways go over the bays at 7.5 m: into the block Assembler's Piece in, into the
        /// yard station's Piece in (south over bay 3's east side, west along the shed's south end, down by the station's Core: its Piece in 2
        /// is where line D's transfer run ends) and into a Piece Rack in the north aisle, the storage the Router sends what nobody ordered. A press with a Piece out conveyor puts every part it stamps on it, tagged for its order,
        /// so its tray no longer waits for a crane. Laid after the yard (its station is one end).
        /// </summary>
        private void LayPressBelts()
        {
            const float low = 6.5f, high = 7.5f;
            var join = new[] { On("Large Conveyor Join", 75, -114, East, "-y.4.4", General2), On("Large Conveyor Join", 87, -114, East, "-y.4.4", General2) };
            var router = On("Large Conveyor Router", 91, -114, East, "-y.4.4", General2);
            var rack = On("Piece Rack", 77, -98, East, "-y.0.0", General2);
            for (var k = 0; k < 2; k++)
            {
                Belt(_presses[2 + k], "Piece out", join[k], "Piece in 2", null, General2); // (the south row: straight into the Join's south side)
                Belt(_presses[k], "Piece out", join[k], "Piece in 3", low, General2); // (the north row: up, over its own bed, down into the Join's north side)
            }
            Belt(join[0], "Piece out", join[1], "Piece in", low, General2, zFirst: false); // (over the Die Racks and the Lathe)
            Belt(join[1], "Piece out", router, "Piece in", null, General2);
            LaySmallPressBelts(join[0]); // (into the first Join's free west side: ZeroDayWorks.SmallPresses.cs)
            Belt(router, "Piece out 2", _blockAssembler!, "Piece in", high, General2, zFirst: true);
            Belt(router, "Piece out 3", rack, "Piece in", high, General2, zFirst: false);
            // (the yard station's Core is a rover dock with no hold since the rover docks, Sean 2026-11-24: pieces reach its bay by its pick
            // pads, so the router's last way, which ran into the old Core's Piece in, stays open)
        }

        /// <summary>
        /// A conveyor from a part's port to another's: straight between the ports with <paramref name="height"/> null, else out of the first
        /// port 1 m along its face, up to the height, along x then z (or z then x), and down 1 m off the second port.
        /// </summary>
        private void Belt(VPart from, string fromPort, VPart to, string toPort, float? height, string paint, bool zFirst = false)
        {
            var (a, fa) = PortOf(from, fromPort);
            var (b, fb) = PortOf(to, toPort);
            if (height is not { } h)
            {
                // (half a metre out of each port, square across between)
                var o = a + fa * 0.5f;
                var i = b + fb * 0.5f;
                var mid = Vector3.Dot(fa, Vector3.UnitZ) != 0 ? V(i.X, i.Y, o.Z) : V(o.X, o.Y, i.Z);
                Vector3[] pts = Vector3.Distance(mid, o) < 0.1f || Vector3.Distance(mid, i) < 0.1f ? [o, i] : [o, mid, i];
                Link("conveyor", from, to, fromPort, toPort, paint, false, null, pts);
                return;
            }
            var a1 = a + fa;
            var b1 = b + fb;
            var turn = zFirst ? V(a1.X, h, b1.Z) : V(b1.X, h, a1.Z);
            Link("conveyor", from, to, fromPort, toPort, paint, false, null, Cut(a, a1, V(a1.X, h, a1.Z), turn, V(b1.X, h, b1.Z), b1));
        }

        private void LayGeneralLine()
        {
            // ---- The walls line: dies at x 73 and 75, the Core at 77; three Run-out Tables; the cutter's bed at x 73 and 75, its Core at 77. --
            ArmourLine("W", 73, General, bedAcross: 2, bedAlong: 0, tables: 3, cutAlong: 9);

            // ---- The module Building Station (a 5 x 6 bay west of its Core, masts 3 high) and its stock pad (3 x 4) south of it. ----------
            Station(93, -71, 83, -69, 5, 6, 3, General);
            var pad = Tiles("Pad Tile", 83, -83, 3, 4, South, Stock);
            foreach (var c in Corners(pad))
                Stack(c, "top", 1);
            _stores.Add(("modules", pad[0, 0]));
            Arm("Plasma Loader Arm", 89, -87);
            Arm("Plasma Loader Arm", 89, -93);

            // ---- Four Presses (3 x 4 beds, the shells one block high: a 2 m swing strip north and south of each) in two rows. ---------
            // Each starts with a die (the yard's frigate wants ten families; four presses hold four: the leg pods' canopy plates, the
            // housings, the buttons and switches, the leg pods' brackets; the Circuit Boards and Gear Sets have their own small presses,
            // ZeroDayWorks.SmallPresses.cs). A press swaps its die from a Die Rack chuted into it (PressDieSwap); the works' Die Racks
            // aren't chuted to the presses yet, so the other families wait on that.
            var dies = new Queue<string>(["Plate Die", "Housing Die", "Button Die", "Bracket Die"]);
            foreach (var z in new[] { -103f, -119f })
                foreach (var x in new[] { 75f, 86f })
                {
                    var press = Press(x, z);
                    var die = dies.Dequeue();
                    Onto(press, "dieslot", die, "mount");
                    _pressDies.Add(die); // (in its own colours: the family's punch)
                    _makers.Add(press);
                    _presses.Add(press);
                }
            FeedPresses(); // (each press's own Extruder, straight into its bay 1: ZeroDayWorks.PressFeed.cs)
            LaySmallPresses(); // (the Circuit Boards' and Gear Sets' own small presses, south of the block Assembler)
            // Between the two columns of presses (x 82 to 85): the Die Racks and the Die Lathe, beside the beds, out of the swing strips.
            foreach (var z in new[] { -105f, -109f, -117f, -125f })
                On("Die Rack", 83, z, Up, "-y.0.0", General2);
            On("Die Lathe", 83, -114, Up, "-y.0.0", General2);

            // ---- The block Assembler: Core at x 75, a 4 x 4 bed east of it, 2-block masts outside the bed's corners, a Gantry Carriage
            // a bed row, the Thruster Jig docked on its west side (its Tool Bay), three upgrade modules; the Block Rack east of the bed. ----
            var asm = Assembler(75, -132); // (its north masts 1 m clear of the south presses' shell swing: the presses moved south 2 m for the belt aisle, 2026-11-23)
            _makers.Add(asm);
            _blockAssembler = asm;
            // Its stock: a chute from each bin its frigate orders draw on (Orders pull their inputs from the stores chuted in), at 9.5 to
            // 11.5 m over the bays, the Extruders' steel chutes at 9 m under them.
            var up = 9.5f;
            foreach (var m in new[] { "steel", "copper", "glass", "concrete", "rubber" })
            {
                Chute(_bins[m], asm, up);
                up += 0.5f;
            }
            // (the Block Rack is a medium part and the floor's sockets small: welded on freeform, its feet 3 mm over the slab). A chute from
            // it into the Core is the tool swap's way (AssemblerToolBay.Chute): a recipe that wants the rack's tool block takes it from there.
            var rack = Weld(Medium("Block Rack", 89, -138));
            Paint(rack, General2);
            var top = _welded[rack] + new Vector3((rack.Def.BoundsMin.X + rack.Def.BoundsMax.X) * 0.5f, rack.Def.BoundsMax.Y, (rack.Def.BoundsMin.Z + rack.Def.BoundsMax.Z) * 0.5f);
            var (chuteIn, _) = PortOf(asm, "Chute in");
            Link("chute", rack, asm, null, "Chute in", General2, false, null,
                [V(top.X, top.Y + 1, top.Z), V(top.X, 5f, top.Z), V(top.X - 7, 5f, top.Z), V(chuteIn.X, 5f, top.Z), V(chuteIn.X, 5f, chuteIn.Z), V(chuteIn.X, chuteIn.Y + 1, chuteIn.Z)]);
            On("Sewing Table Block", 91, -132, Up, paint: General2); // (the other tool block, on the floor: a player shelves it on the rack, which a blueprint can't)

            // ---- The walls store (5 x 8) and the fittings store (4 x 8), side by side; the fittings run south from it to the yard. -------
            var walls = Tiles("Pad Tile", 73, -147, 5, 8, South, Stock);
            foreach (var c in Corners(walls))
                Stack(c, "top", 1);
            _stores.Add(("walls", walls[0, 0]));
            var fittings = Tiles("Pad Tile", 85, -147, 4, 8, South, Stock);
            foreach (var c in Corners(fittings))
                Stack(c, "top", 1);
            _stores.Add(("fittings", fittings[0, 0]));
            Tiles("Roller Tile", 87, -163, 1, 17, South, General); // (the fittings run: south along the bay's east side, off the cranes' picks)
        }

        /// <summary>
        /// A Press: its Core by its "-y.4.4" at (x, z), a 3 x 4 bed east of it, Press Blocks under the housings and Press Bed Blocks between (3 along x, the flow, from the Core's +x;
        /// 4 rows south: the bed is the blocks beside the Core, not the Core). Its swing lid hinges on the south row and opens over a strip
        /// south of it (ConstructPress.SwingReach).
        /// </summary>
        private VPart Press(float x, float z)
        {
            var core = On("Press", x, z, Up, "-y.4.4", General);
            OrdersOnly(core);
            var bed = new VPart[3, 4];
            bed[0, 0] = Paint(Onto(core, "+x", "Press Block", "-x"), General);
            for (var i = 1; i < 3; i++)
                bed[i, 0] = Paint(Onto(bed[i - 1, 0], "+x", "Press Block", "-x"), General);
            for (var j = 1; j < 3; j++)
                for (var i = 0; i < 3; i++)
                    bed[i, j] = Paint(Onto(bed[i, j - 1], "-z", "Press Bed Block", "+z"), General); // (the low bed between the housings' rows, at the rollers' height: Sean 2026-11-23)
            Chain(bed, "Press Block", "-z", "+z", "+x", "-x", General, rowZero: true);
            return core; // (no wall blocks since the swing lid, 2026-11-25: the lid lies over the bed and opens over the south row's strip)
        }

        /// <summary>
        /// The block Assembler: its Core at (x, z), a 4 x 4 bed of Assembly Bed Blocks east of it (the Core's row the bed's north row),
        /// two Mast Blocks on the floor outside each bed corner (welded to the corner block's north or south side), a Gantry Carriage
        /// on the first block of each row (or the last: <paramref name="carriagesEast"/>), the Thruster Jig Block in its Tool Bay on the Core's west side, and three upgrade modules.
        /// </summary>
        private VPart Assembler(float x, float z, int across = 4, int along = 4, int masts = 2, bool jig = true, bool carriagesEast = false)
        {
            var core = On("Assembler", x, z, Up, paint: General);
            OrdersOnly(core);
            var bed = new VPart[across, along];
            bed[0, 0] = Paint(Onto(core, "+x", "Assembly Bed Block", "-x"), General);
            for (var i = 1; i < across; i++)
                bed[i, 0] = Paint(Onto(bed[i - 1, 0], "+x", "Assembly Bed Block", "-x"), General);
            Chain(bed, "Assembly Bed Block", "-z", "+z", "+x", "-x", General, rowZero: true);
            int a = across - 1, b = along - 1;
            foreach (var (c, side, by) in new[] { (bed[0, 0], "+z", "-z.low"), (bed[a, 0], "+z", "-z.low"), (bed[0, b], "-z", "+z.low"), (bed[a, b], "-z", "+z.low") })
                Stack(Paint(Onto(c, side, "Mast Block", by), Steel), "top", masts - 1);
            // (a carriage stands 1.4 m on its block's east edge, in the work zone: a part placed over it doesn't fit, so a big bed keeps
            // them on its last column, clear of what the bed packs from its first: the yard's decks are 5 cells of its 6)
            for (var j = 0; j < along; j++)
                Paint(Onto(bed[carriagesEast ? a : 0, j], "carriage", "Gantry Carriage", "bottom"), Crane);
            if (jig)
                Paint(Onto(core, "-x", "Thruster Jig Block", "dock"), General2);
            Onto(core, "module1", "Power Coupling", "mount");
            Onto(core, "module2", "Servo Rack", "mount");
            Onto(core, "module3", "Coolant Loop", "mount");
            return core;
        }
    }
}
