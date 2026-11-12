using System.Numerics;
using CallOfSlop.VehicleKit;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The Production Showcase (docs/sandbox/constructs/production.md, "Production Showcase"): one floor slab with every machine that
/// carries the production systems of 2026-11-11 (status lamps, the Line Console, conveyors for large pieces, junctions, racks, the
/// Press's Piece in, work orders and a bill), stocked and wired so it works the moment it is spawned, with a sign at each test.
/// Built step by step through the game's own rules (<see cref="BlueprintScript"/>), so a step the rules refuse fails here with
/// their reason. Reads the part files as the game loads them: run after the part generators.
/// </summary>
public sealed class ProductionShowcase : IPartGenerator
{
    public string Name => "production_showcase";

    public const string File = "Constructs/Blueprints/ProductionShowcase.json";

    public void Make(PartOutput o) => o.File(File, Script.Build().Text());

    /// <summary>The script: places are in metres, x along the floor (the way material flows), y up, z towards the front aisle.</summary>
    private sealed class Script : BlueprintScript
    {
        private Script() : base("Production Showcase")
        {
        }

        private const float FloorY = 0.3f; // the Foundations' tops
        private const int TilesX = 32, TilesZ = 7; // 2 m Foundations: 64 by 14 m
        private const float Row1 = -4.75f, Row2 = -11.25f; // the two rows of machines; the front aisle is z 0 to -2, the back one z -7 to -8.5
        private const float Posts1 = -2.25f, Posts2 = -8.75f; // the sign posts and consoles of each row, at its aisle

        // Paint by section (look.md): a warm concrete floor, steel-blue walkways, one accent a station, yellow controls.
        private const string Floor = "#8c8576", Aisle = "#4f6578", Hazard = "#d8a020", Board = "#23272b", Control = "#e0b020";
        private const string Power = "#b5452b", Stock = "#c9a24a", Refining = "#d17a22", LineA = "#3d7ab8", PressBlue = "#2f5f8f", LineB = "#4f8f45", Spur = "#b83a3a", Wheels = "#8050a8", Assembly = "#2f8f8a";

        private static readonly Orient Front = new(Vector3.UnitY, Vector3.UnitY, Vector3.UnitZ, Vector3.UnitZ); // own +z to the front aisle
        private static readonly Orient Back = new(Vector3.UnitY, Vector3.UnitY, -Vector3.UnitZ, Vector3.UnitZ); // own -z to the front (a refiner's screen)
        private static readonly Orient Flow = new(Vector3.UnitY, Vector3.UnitY, Vector3.UnitZ, Vector3.UnitX); // own +z along the flow (an Extruder's die; its screen, own -x, then faces the front)
        private static readonly Orient Toward = new(Vector3.UnitY, Vector3.UnitY, Vector3.UnitZ, -Vector3.UnitX); // own +z against the flow (a rack whose Piece in faces +x)
        private static readonly Orient Post = new(Vector3.UnitZ, Vector3.UnitY, Vector3.UnitY, Vector3.UnitZ); // a Frame Beam stood up, its y+ sockets to the front

        private readonly List<VPart> _lamps = new();
        private VPart? _switch;

        public static Script Build()
        {
            var s = new Script { Description = "Every new production machine on one floor, stocked and wired: read the signs, press Start, send the bill." };
            s.Group = "Production";
            s.Lay();
            return s;
        }

        private static Vector3 V(float x, float y, float z) => new(x, y, z);

        /// <summary>A part standing on the floor by its own socket <paramref name="by"/> at (x, z).</summary>
        private VPart On(string part, float x, float z, Orient o, string by = "bottom", string? paint = null, string? finish = null)
        {
            var p = Stand(part, V(x, FloorY, z), o, null, by);
            if (paint != null)
                Paint(p, paint);
            if (finish != null)
                Finish(p, finish);
            return p;
        }

        /// <summary>A conveyor port's place (vehicle coordinates).</summary>
        private Vector3 PortAt(VPart p, string name)
        {
            var np = p.Def.NamedPorts.First(x => x.Type == RouteType.Conveyor && x.Name == name);
            ref var cp = ref Live.Parts[p.Index];
            return FromRest(cp.Pos + Rot24.Apply(cp.Rot, np.At));
        }

        private void Lay()
        {
            // ---- The floor: 2 m Foundations, concrete, the two aisles in deck plate. --------------------------------------
            var tiles = new VPart[TilesX, TilesZ];
            for (var j = 0; j < TilesZ; j++)
                for (var i = 0; i < TilesX; i++)
                {
                    var at = V(1 + 2 * i, 0.15f, -1 - 2 * j);
                    var t = i == 0 && j == 0 ? Root("Foundation", at, Front) : Put("Foundation", at, Front);
                    tiles[i, j] = t;
                    var aisle = j == 0 || j == 3 && i >= 11;
                    Paint(t, aisle ? Aisle : Floor);
                    Finish(t, aisle ? "painted deck plate" : "concrete");
                }

            // ---- 1. Power: a Small Genset on two fuel tanks, a battery, the main switch and its charge gauge. ------------------
            var genset = On("Small Genset", 2.25f, Row1, Front, "-y", Power, "painted plating");
            var tank = Onto(genset, "-x", "Fuel Tank", "+x");
            Paint(tank, Power);
            var tank2 = Onto(tank, "-z", "Fuel Tank", "+z");
            Paint(tank2, Power);
            var battery = On("Battery", 3.75f, Row1, Front, "-y", Power);
            var mainPost = Sign(1.25f, Posts1, Power, "MAIN SWITCH: off = machines No power, genset and lamps off");
            _switch = Onto(mainPost, "y+2", "Switch", "-z");
            Config(_switch, ("startOn", true));
            Paint(_switch, Control);
            var gauge = Onto(mainPost, "y+1", "Charge Gauge", "back");
            Paint(gauge, Board);
            Wire(_switch, genset);
            Wire(_switch, battery); // (a wire into the only battery is the mains: off, every machine has No power, creative too; ConstructPower.Mains)

            // ---- 2. Stock: Storage Volumes the creative world fills as the blueprint builds (their Material and Fill are creative-only). --
            Sign(5.25f, Posts1, Stock, "STOCK: creative fills these bins; survival builds them empty");

            // ---- 3. Refining: Smelter, Sawmill and Foundry, chuted from stock into Materials Bins, one Line Console on their Runs. -----
            var smelter = On("Smelter", 9.25f, Row1, Back, paint: Refining);
            var sawmill = On("Sawmill", 15.25f, Row1, Back, paint: Refining);
            var foundry = On("Foundry", 20.25f, Row1, Flow, paint: Refining);
            Feed(StockOf("iron ore", 6.25f, -3.75f), smelter);
            Feed(StockOf("wood", 12.25f, -3.75f), sawmill);
            Feed(StockOf("metal", 18.25f, -8.25f), foundry);
            Feed(StockOf("stone", 19.75f, -8.25f), foundry);
            Output(smelter, 6.75f, -6.25f);
            Output(sawmill, 12.75f, -6.25f);
            Output(foundry, 21.75f, -7.75f);
            var refine = On("Line Console", 17.75f, Posts1, Front, paint: Control);
            Sign(16.25f, Posts1, Refining, "Press Start on the console: all three refiners run");
            Run(refine, smelter, V(17.75f, 0, -2.0f), V(13.5f, 0, -2.0f), V(9.25f, 0, -2.0f));
            Run(refine, sawmill);
            Run(refine, foundry);

            // ---- 4. Extrusion line A (metal): Extruder, conveyor, splitter; one branch to the Press, one through the join to the rack. --
            var exA = On("Extruder", 25.25f, Row1, Flow, paint: LineA);
            Config(exA, ("profile", "L-angle"), ("width", 0.2), ("height", 0.2), ("cutLength", 1.5));
            Feed(StockOf("metal", 23.25f, Row1), exA);
            var split = On("Conveyor Splitter", 28.75f, -4.25f, Flow, "-y.0.0", LineA);
            Convey(exA, "Piece out", split, "Piece in", LineA);
            Sign(27.75f, Posts1, LineA, "Cut this conveyor to see pieces drop");
            var press = On("Press", 30.25f, -9.25f, Back, "-y.0.0", PressBlue); // (its first bay under the splitter; a one-bay die works either bay, so "Piece in 2" is free for a second feed)
            Onto(press, "dieslot", "Bracket Die", "mount");
            Convey(split, "Piece out 2", press, "Piece in", LineA);
            Sign(28.25f, Posts2, PressBlue, "Line A's angles go in here: the press stamps brackets");
            var bracketRack = On("Piece Rack", 27.75f, -12.75f, Toward, "-y.0.0", PressBlue);
            Convey(press, "Piece out", bracketRack, "Piece in", PressBlue);
            var join = On("Conveyor Join", 33.25f, -4.25f, Flow, "-y.0.0", LineA);
            Convey(split, "Piece out", join, "Piece in", LineA);
            var longRack = On("Long Piece Rack", 37.75f, -3.25f, Flow, "-y.0.0", LineA);
            Convey(join, "Piece out", longRack, "Piece in", LineA);
            Sign(40.25f, Posts1, LineA, "Take a piece with F (aim at the one you want)");

            // ---- 5. Extrusion line B (planks): another material is another line; it meets line A at the join. --------------------
            var exB = On("Extruder", 31.75f, Row2, Flow, paint: LineB);
            Config(exB, ("profile", "Solid bar"), ("width", 0.2), ("height", 0.1), ("cutLength", 2.0));
            Feed(StockOf("planks", 30.75f, -12.75f), exB);
            var bOut = PortAt(exB, "Piece out");
            var bIn = PortAt(join, "Piece in 2");
            Convey(exB, "Piece out", join, "Piece in 2", LineB, null, V(bIn.X, bOut.Y, bOut.Z));
            Sign(33.25f, Posts2, LineB, "Line B (planks) meets line A at the join: one rack");

            // ---- The spur: a conveyor set too narrow on purpose, so its Extruder refuses the piece with the reason. ----------------
            var exS = On("Extruder", 38.25f, Row2, Flow, paint: Spur);
            Config(exS, ("profile", "Solid bar"), ("width", 0.3), ("height", 0.3), ("cutLength", 1.0));
            Feed(StockOf("metal", 36.25f, Row2), exS);
            var spurRack = On("Piece Rack", 42.25f, -10.75f, Flow, "-y.0.0", Spur);
            Convey(exS, "Piece out", spurRack, "Piece in", Spur, new Vector2(0.1f, 0.1f));
            Sign(39.75f, Posts2, Spur, "Too narrow on purpose: aim the linking tool, wheel to widen");

            // ---- 6. The wheel line: a Wheel Former, a conveyor, a Piece Rack. ----------------------------------------------------
            var former = On("Wheel Former", 52.25f, Row1, Flow, paint: Wheels);
            Config(former, ("width", 0.25), ("height", 0.6));
            Feed(StockOf("rubber", 52.25f, -7.25f), former);
            var wheelRack = On("Piece Rack", 56.25f, -4.25f, Flow, "-y.0.0", Wheels);
            Convey(former, "Piece out", wheelRack, "Piece in", Wheels);
            Sign(54.75f, Posts1, Wheels, "Narrow this conveyor (linking tool, wheel): a refusal");

            // ---- 6b. The assembling line: an Assembler fed steel, its Piece out onto a conveyor to a Piece Rack (catalogue parts). --
            var asm = On("Assembler", 52.25f, Row2, Flow, paint: Assembly);
            Feed(StockOf("steel", 52.25f, -13.25f), asm);
            var partRack = On("Piece Rack", 55.25f, -10.75f, Flow, "-y.0.0", Assembly);
            Convey(asm, "Piece out", partRack, "Piece in", Assembly);
            Sign(50.25f, Posts2, Assembly, "Send the bill: the Assembler makes these parts");

            // ---- 7. Orders: a second Line Console on both Extruders and the Wheel Former, its bill already on it. ------------------
            var orders = On("Line Console", 24.25f, Posts1, Front, paint: Control);
            Sign(23.25f, Posts1, Control, "Bill, then Send: the orders go out. Then Line, Start");
            Run(orders, exA);
            Run(orders, exB, V(24.25f, 0, -2.0f), V(26.75f, 0, -2.0f), V(26.75f, 0, -8.6f), V(31.75f, 0, -8.6f));
            Run(orders, former, V(24.25f, 0, -2.0f), V(31.75f, 0, -2.0f), V(39.25f, 0, -2.0f), V(46.75f, 0, -2.0f), V(52.25f, 0, -2.0f));
            foreach (var (piece, n) in new[]
                     {
                         ("Extrusion l-angle 10x10x45 metal", 2), ("Extrusion l-angle 15x15x60 metal", 2), ("Extrusion l-angle 20x20x75 metal", 2),
                         ("Extrusion l-angle 25x25x100 metal", 2), ("Extrusion l-angle 30x30x125 metal", 1), ("Extrusion plate 30x30x100 metal", 1),
                         ("Extrusion plate 45x45x150 metal", 1),
                         ("Extrusion solid 20x10x200 planks", 2), ("Extrusion i-beam 25x25x150 planks", 2),
                         ("Extrusion wheel 25x60x0 rubber", 2), ("Extrusion wheel 30x90x0 rubber", 2),
                     })
                Bill(orders, piece, n);
            // (and the Assembler's catalogue parts: a Send deals them to it, the pieces to the Extruders and the Former)
            Run(orders, asm, V(24.25f, 0, -2.0f), V(26.75f, 0, -2.0f), V(26.75f, 0, -8.6f), V(34.25f, 0, -8.6f), V(41.75f, 0, -8.6f), V(49.25f, 0, -8.6f), V(52.25f, 0, -8.6f));
            foreach (var (part, n) in new[] { ("Gear Set", 4), ("Steel Plate", 4), ("Gun Barrel", 2) }) // (steel only, tier 1: what its stock makes)
                BillPart(orders, part, n);

            Monitors(refine, orders);

            // ---- Hazard stripes where pieces start and end their ride. ---------------------------------------------------------
            Stripe(26.75f, Row1, true);
            Stripe(36.75f, Row1, true);
            Stripe(29.75f, -7.75f, false);
            Stripe(39.75f, Row2, true);
            Stripe(53.75f, Row1, true);

            // ---- The lamps: one on every sign post, wired from the main switch along the aisles' edges. -------------------------
            foreach (var lamp in _lamps)
                Lamp(lamp);
        }

        /// <summary>
        /// Production monitors (production.md "Production monitors"), all on the front aisle's post line, x 19 to 22 and 57 to 63.5:
        /// a small one by the refining console (a Number Readout on its Data out), a medium one by the orders console (a Text Sign),
        /// and the orders line's board at the right-hand end (a Text Sign at Text size 50 %: no adapter takes a medium display onto
        /// the small floor), a red lamp on a mast beside it on the console's Alarm output.
        /// </summary>
        private void Monitors(VPart refine, VPart orders)
        {
            // Small: a row a machine, by the refining console.
            var smallPost = Sign(19.25f, Posts1, Refining, "MONITOR: the console's Data out wired to a display");
            var small = Onto(smallPost, "y+1", "Number Readout", "back");
            Paint(small, Board);
            Link("data", refine, small, "Data out", null);

            // Medium: two lines a machine, by the orders console.
            var mediumPost = Sign(21.75f, Posts1, Control, "MONITOR: any display works; it lays itself out");
            var medium = Onto(mediumPost, "y+1", "Text Sign", "back");
            Paint(medium, Board);
            Link("data", orders, medium, "Data out", null);

            // Large: the orders line's board at the right-hand end, its Alarm on a red lamp on a mast beside it.
            Sign(57.25f, Posts1, Control, "ORDERS BOARD: red lamp = Alarm (a machine waits or is out)");
            var boardPost = On("Frame Beam", 60.75f, Posts1, Post, "end-", Control, "painted plating");
            var board = Onto(boardPost, "y+3", "Text Sign", "back");
            Paint(board, Board);
            Config(board, ("textSize", 50));
            var mast = On("Frame Beam", 62.75f, Posts1, Post, "end-", Control, "painted plating");
            var mast2 = Onto(mast, "end+", "Frame Beam", "end-");
            Paint(mast2, Control);
            var alarm = Onto(mast2, "end+", "Lamp", "-y");
            Config(alarm, ("colour", "#ff2a1a"), ("brightness", 160));
            var cable = new List<Vector3>();
            for (var x = 24.25f; x < 60.75f; x += 7.5f)
                cable.Add(V(x, FloorY + 0.03f, -1.95f));
            cable.Add(V(60.75f, FloorY + 0.03f, -1.95f));
            Link("data", orders, board, "Data out", null, Control, true, cable.ToArray());
            for (var k = 0; k < cable.Count; k++)
                cable[k] = cable[k] with { Z = -1.9f };
            cable.Add(V(62.75f, FloorY + 0.03f, -1.9f));
            cable.Add(V(62.75f, FromRest(Live.Parts[alarm.Index].Pos).Y, -1.9f)); // (up the mast)
            Link("data", orders, alarm, "Alarm", null, Spur, true, cable.ToArray());
        }

        /// <summary>A sign post at (x, z): a Frame Beam stood up in the station's colour, a Text Sign facing the aisle, a lamp on top.</summary>
        private VPart Sign(float x, float z, string colour, string text)
        {
            var post = On("Frame Beam", x, z, Post, "end-", colour, "painted plating");
            var sign = Onto(post, "y+3", "Text Sign", "back");
            Paint(sign, Board);
            Text(sign, text);
            var lamp = Onto(post, "end+", "Lamp", "-y");
            Config(lamp, ("colour", "#ffb347"), ("brightness", 120));
            _lamps.Add(lamp);
            return post;
        }

        /// <summary>A Lane Marking in hazard paint flat on the floor at (x, z), across a conveyor that runs along x (<paramref name="acrossX"/>) or along z.</summary>
        private void Stripe(float x, float z, bool acrossX) =>
            On("Lane Marking", x, z, new Orient(Vector3.UnitY, Vector3.UnitY, Vector3.UnitX, acrossX ? Vector3.UnitZ : Vector3.UnitX), "bottom", Hazard, "hazard paint");

        /// <summary>A Storage Volume of stock at (x, z): a low box on the floor, a quarter full in a creative world (no walls needed below a quarter: Sealed volumes).</summary>
        private VPart StockOf(string material, float x, float z)
        {
            var v = On("Storage Volume", x, z, Front, "-y", Stock);
            // (the box from 5 cm over the floor, 1.2 m square and 1 m high; 24% full stays under every open side's lip)
            Config(v, ("sizeX", 1.2), ("sizeY", 1.0), ("sizeZ", 1.2), ("atY", 0.35), ("material", material), ("fill", 24));
            return v;
        }

        private void Feed(VPart from, VPart machine) => Link("chute", from, machine, "Chute out", machine.Def.IsRefiner ? "Chute in" : null, Stock);

        private void Output(VPart machine, float x, float z)
        {
            var bin = On("Materials Bin", x, z, Front, paint: Refining);
            Link("chute", machine, bin, "Chute out", null, Refining);
        }

        private void Convey(VPart from, string fromPort, VPart to, string toPort, string paint, Vector2? size = null, params Vector3[] nodes) =>
            Link("conveyor", from, to, fromPort, toPort, paint, false, size, nodes);

        /// <summary>A console's wire into a machine's Run: straight when near, else a cable laid along the floor through (x, z) points.</summary>
        private void Run(VPart console, VPart machine, params Vector3[] floor)
        {
            if (floor.Length == 0)
            {
                Link("data", console, machine, null, "Run");
                return;
            }
            Link("data", console, machine, null, "Run", Control, true, null, floor.Select(p => V(p.X, FloorY + 0.03f, p.Z)).ToArray());
        }

        /// <summary>A lamp wired from the main switch: a cable along the front aisle's edge (and across to the back row), up its post.</summary>
        private void Lamp(VPart lamp)
        {
            var at = FromRest(Live.Parts[lamp.Index].Pos);
            var nodes = new List<Vector3> { V(1.25f, FloorY + 0.03f, -1.85f) };
            var x = 1.25f;
            var crossAt = 26.75f; // (the back row's cables cross between Extruder A's die and its post)
            var back = at.Z < -6;
            var toX = back ? crossAt : at.X;
            while (MathF.Abs(toX - x) > 7.5f)
            {
                x += MathF.Sign(toX - x) * 7.5f;
                nodes.Add(V(x, FloorY + 0.03f, -1.85f));
            }
            if (MathF.Abs(toX - x) > 0.1f)
                nodes.Add(V(toX, FloorY + 0.03f, -1.85f));
            if (back)
            {
                nodes.Add(V(crossAt, FloorY + 0.03f, -8.35f));
                x = crossAt;
                while (MathF.Abs(at.X - x) > 7.5f)
                {
                    x += MathF.Sign(at.X - x) * 7.5f;
                    nodes.Add(V(x, FloorY + 0.03f, -8.35f));
                }
            }
            var z = back ? -8.35f : -1.85f;
            if (MathF.Abs(at.X - x) > 0.1f && back)
                nodes.Add(V(at.X, FloorY + 0.03f, z));
            nodes.Add(V(at.X, at.Y, z));
            Link("data", _switch!, lamp, null, null, Board, true, null, nodes.ToArray());
        }
    }
}
