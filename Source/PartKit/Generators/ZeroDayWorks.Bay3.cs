using System.Numerics;
using CallOfSlop.VehicleKit;

namespace CallOfSlop.PartKit.Generators;

public sealed partial class ZeroDayWorks
{
    private sealed partial class Script
    {
        // Bay 3, the general line (doc "Bay 3"), under the 24 m span (rails at x 71 and 95; clear x 73 to 93), north to south:
        // the walls line (a 2-cell Sheet Extruder straight onto a pad that feeds a 2 x 9 Laser Cutter), the module Building Station and its
        // stock pad, four Presses in two rows with their Die Racks and the Die Lathe between, the block Assembler with its Tool Bay and
        // Block Rack, then the walls and fittings stores.
        public const string General2 = "#3a9f99";

        private void LayGeneralLine()
        {
            // ---- The walls line: dies at x 73 and 75, the Core at 77; three Run-out Tables; the pad, the cutter at x 79, its bed to 81. ----
            ArmourLine("W", 73, General, bedAcross: 2, bedAlong: 0, tables: 3, padAcross: 2, cutAlong: 9);

            // ---- The module Building Station (a 5 x 6 bay west of its Core, masts 3 high) and its stock pad (3 x 4) south of it. ----------
            Station(93, -71, 83, -69, 5, 6, 3, General);
            var pad = Tiles("Pad Tile", 83, -83, 3, 4, South, Stock);
            foreach (var c in Corners(pad))
                Stack(c, "top", 1);
            _stores.Add(("modules", pad[0, 0]));
            Arm("Plasma Loader Arm", 89, -87);
            Arm("Plasma Loader Arm", 89, -93);

            // ---- Four Presses (3 x 4 beds, the shells one block high: a 2 m swing strip north and south of each) in two rows. ---------
            // Each starts with a die (the yard's frigate wants ten families; four presses hold four: the Gear Set kits, the Circuit Boards,
            // the buttons and switches, the leg pods' brackets). A press swaps its die from a Die Rack chuted into it (PressDieSwap); the works' Die Racks
            // aren't chuted to the presses yet, so the other families wait on that.
            var dies = new Queue<string>(["Gear Die", "Board Die", "Button Die", "Bracket Die"]);
            foreach (var z in new[] { -103f, -117f })
                foreach (var x in new[] { 75f, 86f })
                {
                    var press = Press(x, z);
                    Onto(press, "dieslot", dies.Dequeue(), "mount"); // (in its own colours: the family's punch)
                    _makers.Add(press);
                }
            // Between the two columns of presses (x 82 to 85): the Die Racks and the Die Lathe, beside the beds, out of the swing strips.
            foreach (var z in new[] { -105f, -109f, -118f, -122f })
                On("Die Rack", 83, z, Up, "-y.0.0", General2);
            On("Die Lathe", 83, -113, Up, "-y.0.0", General2);

            // ---- The block Assembler: Core at x 75, a 4 x 4 bed east of it, 2-block masts outside the bed's corners, a Gantry Carriage
            // a bed row, the Thruster Jig docked on its west side (its Tool Bay), three upgrade modules; the Block Rack east of the bed. ----
            var asm = Assembler(75, -131);
            _makers.Add(asm);
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
            On("Sewing Table Block", 91, -131, Up, paint: General2); // (the other tool block, on the floor: a player shelves it on the rack, which a blueprint can't)

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
        /// A Press: its Core by its "-y.4.4" at (x, z), a 3 x 4 bed of Press Blocks east of it (3 along x, the flow, from the Core's +x;
        /// 4 rows south: the bed is the blocks beside the Core, not the Core), and one block on each cell of the north and south rows for
        /// the shells (a 2 m swing strip north and south).
        /// </summary>
        private VPart Press(float x, float z)
        {
            var core = On("Press", x, z, Up, "-y.4.4", General);
            OrdersOnly(core);
            var bed = new VPart[3, 4];
            bed[0, 0] = Paint(Onto(core, "+x", "Press Block", "-x"), General);
            for (var i = 1; i < 3; i++)
                bed[i, 0] = Paint(Onto(bed[i - 1, 0], "+x", "Press Block", "-x"), General);
            Chain(bed, "Press Block", "-z", "+z", "+x", "-x", General, rowZero: true);
            foreach (var j in new[] { 0, 3 })
                for (var i = 0; i < 3; i++)
                    Paint(Onto(bed[i, j], "top", "Press Block", "bottom"), General);
            return core;
        }

        /// <summary>
        /// The block Assembler: its Core at (x, z), a 4 x 4 bed of Assembly Bed Blocks east of it (the Core's row the bed's north row),
        /// two Mast Blocks on the floor outside each bed corner (welded to the corner block's north or south side), a Gantry Carriage
        /// on the first block of each row, the Thruster Jig Block in its Tool Bay on the Core's west side, and three upgrade modules.
        /// </summary>
        private VPart Assembler(float x, float z, int across = 4, int along = 4, int masts = 2, bool jig = true)
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
            for (var j = 0; j < along; j++)
                Paint(Onto(bed[0, j], "carriage", "Gantry Carriage", "bottom"), Crane);
            if (jig)
                Paint(Onto(core, "-x", "Thruster Jig Block", "dock"), General2);
            Onto(core, "module1", "Power Coupling", "mount");
            Onto(core, "module2", "Servo Rack", "mount");
            Onto(core, "module3", "Coolant Loop", "mount");
            return core;
        }
    }
}
