using System.Numerics;
using CallOfSlop.VehicleKit;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The Factory Showcase (docs/sandbox/constructs/production.md, "Factory Showcase"; notes docs/sandbox/design/showcase-new-notes.md): one
/// connected factory on one body. Raw ore, wood, stone and sand go into the refinery at the west end; refined material rides chutes east
/// to the extruders, formers, presses and the Assembler; their pieces ride conveyors, Routers and arms to the Building Station at the east
/// end, which builds the Works Runabout from them. Every production machine does work in that one flow. Built step by step through the
/// game's own rules (<see cref="BlueprintScript"/>), so a step the rules refuse fails here with their reason. Reads the part files as the
/// game loads them: run after the part generators.
/// </summary>
public sealed class FactoryShowcase : IPartGenerator
{
    public string Name => "factory_showcase";

    public const string File = "Constructs/Blueprints/FactoryShowcase.json";

    public const string RunaboutFile = "Constructs/Blueprints/WorksRunabout.json";

    public void Make(PartOutput o)
    {
        o.File(RunaboutFile, Runabout.Build().Text());
        o.File(File, Script.Build().Text());
    }

    /// <summary>
    /// The Works Runabout: the vehicle the showcase's station builds, the Swing Arm Buggy's layout with every part one the factory makes.
    /// Its rails and cross bars are extruded steel bars, its wheels the Wheel Former's, its engine the Engine Kit's pressed pieces (a
    /// cylinder off the Cylinder die, a crank, carburettor and flywheel off the Engine die), its fuel tank and brackets pressed, its
    /// drivetrain, seat and controls the Assembler's, its hood a formed panel, its windscreen a cut glass pane and its deck a cut panel.
    /// </summary>
    private sealed class Runabout : BlueprintScript
    {
        private Runabout() : base("Works Runabout")
        {
        }

        public const string Rail = "Extrusion solid 25x25x200 steel", Cross = "Extrusion solid 25x25x150 steel", Stub = "Extrusion solid 25x25x50 steel";
        public const string Wheel = "Extrusion wheel 25x60x0 rubber";
        public const string FuelTank = "Pressed Part (tank.fuel hollow 45x45x45 steel)";
        private const string Body = "#c4472b", Chassis = "#3b4046", Trim = "#e0b020"; // (look.md: one body colour, a dark chassis, a trim accent)
        public const string Cylinder = "Pressed Part (cylinder.cylinder pipe10 45x45x120 steel)", Crank = "Pressed Part (engine.crank solid 45x45x60 steel)",
            Carb = "Small Carburettor"; // (the kit's 0.50 m carburettor is past a small Press's 0.45 m: its preset, a 0.40 m blank)

        public static Runabout Build()
        {
            var s = new Runabout
            {
                Description = "Built at the Factory Showcase's Building Station from parts its machines make: extruded rails, formed wheels, a pressed one-cylinder engine, assembled drivetrain, a formed hood and cut panels.",
                Group = "Production",
            };
            s.Lay();
            return s;
        }

        /// <summary>Onto, and on a refusal both parts' sockets, so a step can be put right from the message.</summary>
        private VPart Fit(VPart on, string socket, string part, string by, int? spin = null)
        {
            try
            {
                return Onto(on, socket, part, by, spin);
            }
            catch (VehicleKitException e)
            {
                throw new VehicleKitException($"{e.Message} [{on.Name}: {string.Join(" ", on.Def.Sockets.Select(x => x.Name))}] [{part}: {string.Join(" ", Def(part).Sockets.Select(x => x.Name))}]");
            }
        }

        private void Lay()
        {
            var railL = Root(Rail, Vector3.Zero);
            var front = Fit(railL, "base", Cross, "+x0", 3);
            var railR = Fit(front, "+x1", Rail, "base");
            var stubL = Fit(railL, "tip", Stub, "base");
            var stubR = Fit(railR, "tip", Stub, "base");
            foreach (var bar in new[] { railL, front, railR, stubL, stubR })
                Paint(bar, Chassis);
            var diff = Fit(stubL, "tip", "Differential", "-z0");
            var armL = Fit(diff, "-x", "Swing Arm", "mount");
            var armR = Fit(diff, "+x", "Swing Arm", "mount");
            var pivL = Fit(front, "base", "Steering Pivot", "base");
            var pivR = Fit(front, "tip", "Steering Pivot", "base");
            var armFL = Fit(pivL, "leaf", "Swing Arm", "mount");
            var armFR = Fit(pivR, "leaf", "Swing Arm", "mount");
            foreach (var arm in new[] { armL, armR, armFL, armFR })
                Fit(arm, "axle", Wheel, "axle");
            var seat = Fit(railL, "+y1", "Seat", "bottom");
            var gearbox = Fit(diff, "top1", "Transmission", "-y");
            var fly = Fit(gearbox, "-z", "Small Flywheel", "+z");
            var crank = Fit(fly, "crank", Crank, "front");
            var cyl = Fit(crank, "+y", Cylinder, "base");
            Fit(cyl, "head", Carb, "base");
            var tank = Fit(railR, "+y0", FuelTank, "-y"); // (a pressed tank sized for a small Press: the Fuel Tank preset's blank is 0.50 m, past its 0.45 m)
            var dm = Fit(seat, "-x", "Driving Module", "+x");
            Wire(seat, dm);
            Wire(dm, fly);
            Link("pipe", tank, fly, "Fuel out", "Fuel in");

            // The body, welded on: a formed hood over the front, a cut windscreen frame before the seat with its glass pane in the
            // opening, pressed brackets at the front corners and a formed spoiler over the back axle.
            var at = (VPart p) => FromRest(Live.Parts[p.Index].Pos);
            var s = at(seat);
            var f = at(front);
            var d = at(diff);
            Paint(Flat(Hood, railL, V3((f.X + at(railR).X) * 0.5f, f.Y + 0.55f, f.Z + 0.35f), up: true), Body);
            var frame = Flat(Frame, railL, V3((s.X + at(railR).X) * 0.5f, s.Y + 0.75f, s.Z - 0.55f), up: false);
            Paint(frame, Body);
            Fit(frame, "o0", Pane, "back");
            Paint(Weld("L Bracket", front, V3(f.X - 0.05f, f.Y + 0.2f, f.Z + 0.01f), default, 0, 0, 0), Trim); // (10 mm off the bar's faces: no coplanar faces)
            Paint(Weld("L Bracket", front, V3(at(railR).X + 0.05f, f.Y + 0.2f, f.Z + 0.01f), default, 0, 0, 0), Trim);
            Paint(Weld("Short Wing Segment", diff, V3(d.X, d.Y + 1.0f, d.Z + 0.3f), default, 0, 90, 0), Body);
        }

        public const string Hood = "Formed Panel (curve 130x80 c10 r0 t20 steel)";
        public const string Frame = "Cut Panel (curve 130x70 c0 r0 t20 steel | r65,35,90,40,5)";
        public const string Pane = "Cut Pane (r90,40,5 t12 glass)";

        private static Vector3 V3(float x, float y, float z) => new(x, y, z);

        /// <summary>A panel welded with its thinnest side up (<paramref name="up"/>) or facing along z, its centre at <paramref name="centre"/>.</summary>
        private VPart Flat(string part, VPart on, Vector3 centre, bool up)
        {
            var size = Def(part).BoundsMax - Def(part).BoundsMin;
            var thinY = size.Y <= size.X && size.Y <= size.Z; // (else the thin side is z)
            var pitch = up == thinY ? 0 : 90; // (a quarter turn about x swaps y and z)
            return Weld(part, on, centre, default, pitch, 0, 0);
        }
    }

    /// <summary>The script: places in metres, x along the flow (west to east), y up, z towards the front walkway (z 0 is the front edge).</summary>
    private sealed class Script : BlueprintScript
    {
        private Script() : base("Factory Showcase")
        {
        }

        private const float FloorY = 0.3f; // the Foundations' tops
        private const float RootX = 55, RootZ = -13; // the root tile's centre: the middle of the whole floor
        private const float Row1 = -4.75f, Row2 = -11.25f; // the main floor's two rows; the front walkway is z 0 to -2, the middle aisle z -7 to -8.5
        private const float Posts1 = -2.25f, Posts2 = -8.75f; // sign posts and consoles of each row, on its aisle's edge

        // Paint by section (look.md): a warm concrete floor, steel-blue walkways, one accent a stage, yellow controls.
        private const string Floor = "#8c8576", Aisle = "#4f6578", Hazard = "#d8a020", Board = "#23272b", Control = "#e0b020";
        private const string Power = "#b5452b", Stock = "#c9a24a", Refining = "#d17a22", Yard = "#9a7b3a", Assembler = "#2f8f8a", PressBlue = "#2f5f8f";
        private const string ToolGrey = "#6f7378", Copper = "#b8733a", PanelGreen = "#5d7f3c", CutterRed = "#a8365e", GlassBlue = "#6aa6b8", Wheels = "#8050a8", StationTeal = "#3f8f5a";

        private static readonly Orient Front = new(Vector3.UnitY, Vector3.UnitY, Vector3.UnitZ, Vector3.UnitZ); // own +z to the front walkway
        private static readonly Orient Back = new(Vector3.UnitY, Vector3.UnitY, -Vector3.UnitZ, Vector3.UnitZ); // own -z to the front (a refiner's screen)
        private static readonly Orient Flow = new(Vector3.UnitY, Vector3.UnitY, Vector3.UnitZ, Vector3.UnitX); // own +z along the flow (an Extruder's die)
        private static readonly Orient Toward = new(Vector3.UnitY, Vector3.UnitY, Vector3.UnitZ, -Vector3.UnitX); // own +z against the flow
        private static readonly Orient Post = new(Vector3.UnitZ, Vector3.UnitY, Vector3.UnitY, Vector3.UnitZ); // a Frame Beam stood up, its y+ sockets to the front

        private readonly List<VPart> _lamps = new();
        private VPart? _switch;
        private readonly HashSet<(int I, int J)> _tiles = new(); // the floor's 2 m tiles laid so far, by (i, j): x 1 + 2i, z -1 - 2j

        public static Script Build()
        {
            var s = new Script
            {
                Description = "One connected factory: raw ore, wood, stone and sand in at the west end, the Works Runabout built at the east end, every production machine working on the way. The refinery runs from the start; read the signs, press Start on the orders console, then pick Works Runabout at the Building Station.",
                Group = "Production",
            };
            s.Lay();
            return s;
        }

        private static Vector3 V(float x, float y, float z) => new(x, y, z);

        private void Lay()
        {
            // The root: the main floor's middle tile, so the blueprint's origin is its middle and a spawn in front of the player sets it
            // down as near as its size allows (SandboxBuildInFront: the origin goes its widest reach from its origin, plus 1.5 m, ahead).
            var root = Root("Foundation", V(RootX, 0.15f, RootZ), Front);
            Paint(root, AreaFloor(RootX, RootZ));
            Finish(root, "concrete");
            _tiles.Add(((int)(RootX - 1) / 2, (int)(-RootZ - 1) / 2));
            Tiles(0, 55, 0, 7); // the main floor: x 0 to 110, z 0 to -14
            Tiles(-4, 0, 0, 7); // the raw strip: x -8 to 0
            Tiles(21, 55, 7, 16); // the back floor: x 42 to 110, z -14 to -32
            PowerPlant();
            var r = Refinery();
            var asm = AssemblyHall(r);
            var ph = PressHall(asm, r);
            AssemblyFeeds();
            var pn = PanelHall(r);
            var st = StationLine(r, asm, ph, pn);
            ToolRoom(ph);
            var hand = HandOrders(st.RouterS);
            var wing = WingCorner(r);
            ScrapAndSpares(r);
            var orders = OrdersConsole(pn.Sheet, pn.Former, pn.Glass, pn.Cutter, st.Wheel, asm.A, ph.Blank, ph.Gear, ph.Tank, ph.Piston, ph.Bracket, ph.Cylinder, ph.Engine, ph.CopperEx, ph.Cable, st.Station, hand.Terminal, hand.Designer, wing);
            Monitors(r.Console, orders, st.RouterS, st.Station);
            Walkway();
            // The areas, numbered as the walkway passes them west to east, then back along the back floor east to west.
            AreaSign(-4.75f, Posts1, "1 RAW STOCK AND POWER", Stock);
            AreaSign(6.75f, Posts1, "2 REFINERY", Refining);
            AreaSign(37.75f, Posts1, "3 THE YARD", Yard);
            AreaSign(52.75f, Posts1, "4 PANEL LINE", PanelGreen);
            AreaSign(71.75f, Posts1, "5 WHEELS AND THE STATION LINE", Wheels);
            AreaSign(83.25f, Posts1, "6 BUILDING STATION", StationTeal);
            AreaSign(109.25f, Posts1, "7 THE WING", Wheels); // (east of the Lay-down Pad: the pad and the bay stay clear)
            AreaSign(77.75f, Posts1, "8 PRESS HALL: BEHIND THIS ROW, ON THE BACK FLOOR", PressBlue); // (the aisle's board for a hall it can't see)
            Arrow(76.75f, -3.25f, -Vector3.UnitZ); // (its arrow, on the floor under the board: south, to the back floor)
            AreaSign(75.25f, -15.25f, "8 PRESS HALL", PressBlue);
            AreaSign(45.25f, -15.25f, "9 ASSEMBLY: WHERE THE LINES MEET", Assembler);
            AreaSign(56.75f, -31.25f, "10 TOOL ROOM", ToolGrey);
            // Each press named by what it makes, on a medium board on its north side, big enough to read from the strip in front.
            foreach (var (press, what) in new[] { (ph.Gear, "GEAR PRESS"), (ph.Tank, "TANK PRESS"), (ph.Piston, "PISTON PRESS"),
                         (ph.Cylinder, "CYLINDER PRESS"), (ph.Engine, "ENGINE PRESS"), (ph.Bracket, "BRACKET PRESS") })
            {
                var at = FromRest(Live.Parts[press.Index].Pos);
                MediumBoard(at.X - 1.0f, -20.75f, what, PressBlue, Post, tall: false);
            }
            FlowArrows();
            foreach (var lamp in _lamps)
                Lamp(lamp);
        }

        // ---- Floors ---------------------------------------------------------------------------------------------------------------

        /// <summary>2 m Foundations over tiles [i0, i1) x [j0, j1): concrete, the first row (j 0) and any row in <paramref name="aisles"/> in deck plate.</summary>
        private void Tiles(int i0, int i1, int j0, int j1, params int[] aisles)
        {
            // Each tile goes on beside one already down (a Foundation snaps to a neighbour's socket), so the rectangle grows out from
            // the tiles it touches: rounds over what is left, taking every tile with a neighbour laid.
            var left = new List<(int I, int J)>();
            for (var j = j0; j < j1; j++)
                for (var i = i0; i < i1; i++)
                    if (!_tiles.Contains((i, j)))
                        left.Add((i, j));
            while (left.Count > 0)
            {
                var next = left.Where(t => _tiles.Contains((t.I - 1, t.J)) || _tiles.Contains((t.I + 1, t.J)) || _tiles.Contains((t.I, t.J - 1)) || _tiles.Contains((t.I, t.J + 1))).ToList();
                if (next.Count == 0)
                    throw new VehicleKitException($"tiles {i0}-{i1} x {j0}-{j1} touch no floor laid before them");
                foreach (var (i, j) in next)
                {
                    var t = Put("Foundation", V(1 + 2 * i, 0.15f, -1 - 2 * j), Front);
                    _tiles.Add((i, j));
                    left.Remove((i, j));
                    var aisle = j == 0 || aisles.Contains(j);
                    Paint(t, aisle ? Aisle : AreaFloor(1 + 2 * i, -1 - 2 * j));
                    Finish(t, aisle ? "painted deck plate" : "concrete");
                }
            }
        }

        // ---- 1. Power -------------------------------------------------------------------------------------------------------------

        /// <summary>A Small Genset on two fuel tanks, a battery, the main switch and its charge gauge (x 0 to 5).</summary>
        private void PowerPlant()
        {
            var genset = On("Small Genset", 2.25f, Row1, Front, "-y", Power, "painted plating");
            var tank = Onto(genset, "-x", "Fuel Tank", "+x");
            Paint(tank, Power);
            var tank2 = Onto(tank, "-z", "Fuel Tank", "+z");
            Paint(tank2, Power);
            _fuel = tank2;
            var battery = On("Battery", 3.75f, Row1, Front, "-y", Power);
            var mainPost = Sign(1.25f, Posts1, Power, "MAIN SWITCH: off = every machine No power");
            _switch = Onto(mainPost, "y+2", "Switch", "-z");
            Config(_switch, ("startOn", true));
            Paint(_switch, Control);
            var gauge = Onto(mainPost, "y+1", "Charge Gauge", "back");
            Paint(gauge, Board);
            Wire(_switch, genset);
            Wire(_switch, battery); // (a wire into the only battery is the mains: ConstructPower.Mains)
        }

        private VPart? _fuel;

        // ---- 2. The refinery ------------------------------------------------------------------------------------------------------

        private sealed record Refined(VPart Steel, VPart Rubber, VPart Cloth, VPart Bronze, VPart Copper, VPart Glass, VPart Console, VPart SteelFoundry);

        /// <summary>
        /// Eight refiners in two rows, 8 m apart (x 8 to 36), raw stock heaped beside each: row 1 Smelter (iron ore to metal), Foundry
        /// (metal and stone to steel), Sawmill (wood to planks), Foundry (planks and sand to rubber); row 2 Workshop Smelter (copper ore to
        /// copper), Workshop Foundry (copper and metal to bronze), Sawmill (planks to cloth), Smelter (sand to glass). Metal and planks chute
        /// straight from one refiner into the next; every finished material ends in a Materials Bin of the yard (x 39 to 43). One Line
        /// Console on all eight.
        /// </summary>
        private Refined Refinery()
        {
            var smelter = On("Workshop Smelter", 10.25f, Row1, Back, paint: Refining); // (the steel line is tier 2: a chute between two tier 2 machines moves 20 units a second, not 10)
            var steelF = On("Workshop Foundry", 18.25f, Row1, Back, paint: Refining);
            var sawmill = On("Sawmill", 26.25f, Row1, Back, paint: Refining);
            var rubberF = On("Foundry", 34.25f, Row1, Back, paint: Refining);
            var copperS = On("Workshop Smelter", 10.25f, Row2, Back, paint: Refining);
            var bronzeF = On("Workshop Foundry", 18.25f, Row2, Back, paint: Refining);
            Config(steelF, ("recipe", RecipeOf(steelF, "steel"))); // (set, not Auto: metal alone in the input would otherwise wait for either fellow)
            Config(bronzeF, ("recipe", RecipeOf(bronzeF, "bronze")));
            var clothS = On("Sawmill", 26.25f, Row2, Back, paint: Refining);
            var glassS = On("Smelter", 34.25f, Row2, Back, paint: Refining);

            var ore = Heap("iron ore", -3.75f, -4.25f, 5.0f, 8.0f); // (two heaps on the raw strip west of the power plant: the whole factory's metal)
            var ore2 = Heap("iron ore", -3.75f, -10.25f, 5.0f, 8.0f);
            ChuteVia(ore2, smelter, -9.6f);
            ChuteVia(ore, smelter, -9.5f);
            // (8 m boxes, a quarter full: the refinery runs from the start and fills every machine's hopper, about 2,600 steel, before the build ends)
            Feed(Heap("stone", 14.25f, -4.25f, 2.4f, 8.0f), steelF);
            var wood = Heap("wood", 22.25f, -4.25f, 2.4f, 8.0f);
            Feed(wood, sawmill);
            Feed(Heap("sand", 30.25f, -4.25f, 2.4f, 8.0f), rubberF);
            Feed(Heap("copper ore", 6.25f, -10.75f, 3.2f, 4.0f), copperS); // (3.2 m square: copper for wire stock, bronze and coils)
            Sign(-7.25f, Posts1, Stock, "RAW STOCK: filled in creative, empty in survival");
            Feed(Heap("sand", 30.25f, -10.75f, 2.4f, 8.0f), glassS);
            // Each heap named on a post beside it (look review 6).
            Sign(-7.25f, -7.25f, Stock, "IRON ORE (both heaps)", false);
            Sign(13.25f, Posts1, Stock, "STONE", false);
            Sign(21.25f, Posts1, Stock, "WOOD", false);
            Sign(29.25f, Posts1, Stock, "SAND", false);
            Sign(6.25f, -8.75f, Stock, "COPPER ORE", false);
            Sign(28.25f, -8.75f, Stock, "SAND", false);
            ChuteVia(smelter, steelF, -7.35f);
            ChuteVia(smelter, bronzeF, -8.2f);
            Chute(copperS, bronzeF);
            Chute(sawmill, rubberF);
            Chute(sawmill, clothS);

            var steel = Store(40.25f, -4.25f, 2.0f); // (a Storage Volume, not a bin: its chutes move what the far end takes, up to 160 a second)
            var rubber = Bin(42.75f, -3.25f, Yard);
            var cloth = Bin(39.75f, -10.25f, Yard);
            var bronze = Bin(42.25f, -10.25f, Yard);
            var copper = Bin(39.75f, -12.75f, Yard);
            var glass = Bin(42.25f, -12.75f, Yard);
            ChuteVia(steelF, steel, -7.15f); // (laid along the middle aisle's edges and the back edge: the walkways stay clear)
            ChuteVia(rubberF, rubber, -7.3f);
            ChuteVia(clothS, cloth, -13.55f);
            ChuteVia(bronzeF, bronze, -13.7f);
            ChuteVia(copperS, copper, -13.85f);
            ChuteVia(glassS, glass, -13.4f);

            var console = On("Line Console", 8.25f, Posts1, Front, paint: Control);
            Config(console, ("startOn", true)); // (the refinery runs from the moment the factory is down: stacks smoke, chutes move; the order lines wait for Start)
            Sign(9.75f, Posts1, Refining, "REFINERY CONSOLE: runs from the start; Stop holds it");
            foreach (var m in new[] { smelter, steelF, sawmill, rubberF })
                Run(console, m, V(8.25f, 0, -2.0f), V(FromRest(Live.Parts[m.Index].Pos).X, 0, -2.0f));
            foreach (var m in new[] { copperS, bronzeF, clothS, glassS })
                Run(console, m, V(8.25f, 0, -1.95f), V(8.0f, 0, -1.95f), V(8.0f, 0, -8.0f), V(FromRest(Live.Parts[m.Index].Pos).X, 0, -8.0f));
            Sign(14.25f, Posts1, Refining, "Ore to metal; metal and stone to steel");
            Sign(30.25f, Posts1, Refining, "Wood to planks; planks and sand to rubber");
            Sign(14.25f, Posts2, Refining, "Copper ore to copper; copper and metal to bronze");
            Sign(30.25f, Posts2, Refining, "Planks to cloth; sand to glass");
            Sign(41.25f, Posts1, Yard, "Every refined material waits here in bins");
            return new Refined(steel, rubber, cloth, bronze, copper, glass, console, steelF);
        }

        // ---- 4. Assembly (the back floor's west end) ------------------------------------------------------------------------------

        private sealed record Assembly(VPart A, VPart B, VPart RouterA, VPart RouterB);

        /// <summary>
        /// Two Workshop Assemblers on the back floor (x 44 to 56): B makes the small parts (Bearings, Servo Motors) and sends them on a
        /// Router, ahead into A's Piece in, its way 3 to the Engine press's kit; A makes the vehicle's catalogue parts (Seat, Driving
        /// Module, Transmission, Differential, Swing Arms, Steering Pivots) from the yard's steel, cloth and rubber and B's parts, out on a
        /// Router whose way ahead runs to the station.
        /// </summary>
        private Assembly AssemblyHall(Refined r)
        {
            var a = On("Workshop Assembler", 52.25f, -17.75f, Flow, paint: Assembler);
            ChuteVia(r.Steel, a, -7.45f);
            ChuteVia(r.Cloth, a, -13.3f);
            ChuteVia(r.Rubber, a, -7.6f);
            ChuteVia(r.Bronze, a, -13.45f); // (it makes its own Bearings: its Router's way 2 loops back into it)
            _steelB = Bin(44.25f, -27.75f, Yard); // (the press hall's steel, chuted on from the yard's: a bin, so its chute moves 10 a second and the yard's store isn't emptied into it)
            ChuteVia(r.Steel, _steelB, -7.75f);

            var ra = On("Large Conveyor Router", 55.75f, -17.25f, Flow, "-y.0.0", Assembler); // (a Large one: a Differential is 1.18 m across, a small junction opens 1 m)
            Convey(a, "Piece out", ra, "Piece in", Assembler, Wide);
            // A's Router way 2 loops back into A's own Piece in: a part A makes for its own next recipe (a Servo for a Steering Pivot).
            _loop = (ra, a);
            // (One Assembler: its inputs from the presses ride in by conveyor, AssemblyFeeds.)
            // (One Assembler: a second, set to Bearings on its own, ate the copper and bronze with 298 spare Bearings.)


            Sign(48.75f, -15.25f, Assembler, "Every catalogue part, its own bearings too", false);
            return new Assembly(a, a, ra, ra);
        }

        private VPart? _steelB, _gear, _cable;
        private (VPart Router, VPart Asm) _loop;

        // ---- 5. The press hall (the back floor) -----------------------------------------------------------------------------------

        private sealed record Presses(VPart Blank, VPart Gear, VPart Tank, VPart Bracket, VPart Cylinder, VPart Engine, VPart Cable, VPart CopperEx, VPart LineOut, VPart Piston);

        private static readonly Vector2 Wide = new(1.25f, 1.25f);
        private static readonly Vector2 Small = new(1.0f, 1.0f); // (a small Join's or Router's whole opening: left to the narrower end, a conveyor after a Join refused a 0.6 m wheel) // (the Assemblers' conveyors and the press line: a Differential or a gearbox is wider than a narrow conveyor)

        /// <summary>
        /// The blank line along the back floor's south side: an Extruder makes each press's ordered blank (pipe, round, hollow, angle) and
        /// a row of Routers sends it north (way 3) into the press that ordered it. Five presses, one die each: Gear, Tank, Bracket,
        /// Cylinder and Engine; the Gear, Tank and Bracket presses' parts go out of their trays by chute, the Cylinder's and Engine's
        /// (too big for a tray) ride a Join along the floor's north edge to the station line. A copper Extruder feeds a sixth press,
        /// the Cable die's, whose coils go by chute to Assembler B.
        /// </summary>
        private Presses PressHall(Assembly asm, Refined r)
        {
            const float lineZ = -27.25f, pressZ = -21.75f;
            var blank = On("Extruder", 56.25f, lineZ, Flow, paint: PressBlue);
            ChuteVia(_steelB!, blank, -29.75f);
            var routers = new List<VPart>();
            VPart prev = blank;
            var prevPort = "Piece out";
            foreach (var x in new[] { 59.75f, 65.25f, 70.75f, 76.25f, 81.75f, 87.25f })
            {
                var rt = On("Conveyor Router", x, lineZ + 0.5f, Flow, "-y.0.0", PressBlue);
                Convey(prev, prevPort, rt, "Piece in", PressBlue);
                routers.Add(rt);
                (prev, prevPort) = (rt, "Piece out");
            }
            var dies = new[] { "Gear Die", "Tank Die", "Piston Die", "Cylinder Die", "Engine Die", "Bracket Die" };
            var presses = new VPart[6];
            for (var k = 0; k < 6; k++)
            {
                var at = FromRest(Live.Parts[routers[k].Index].Pos);
                var p = presses[k] = On("Press", at.X + 1.5f, pressZ, Back, "-y.0.0", PressBlue);
                if (dies[k] is { } die)
                    Onto(p, "dieslot", die, "mount");
                Convey(routers[k], "Piece out 3", p, "Piece in", PressBlue);
            }
            // The press line: three Joins in a row along the back floor's north strip, nothing crossing. Assembler A's parts come in
            // from the west, the Cylinder press's and the Engine press's pieces from the south, and last the blank line's way ahead (a
            // chassis bar the station ordered of this Extruder); the line runs on into the station's back port (StationLine).
            var ja = On("Large Conveyor Join", 80.25f, -17.25f, Flow, "-y.0.0", PressBlue);
            Corner(asm.RouterA, "Piece out", ja, "Piece in", Assembler, Wide);
            Convey(presses[3], "Piece out", ja, "Piece in 2", PressBlue);
            // The Piston press: a steel round in bay 1, bronze by chute in its kit hopper; its Piston Sets ride a conveyor into the Cylinder press's hopper.
            ChuteVia(r.Bronze, presses[2], -23.9f);
            Convey(presses[2], "Piece out", presses[3], "Piece in 2", PressBlue); // (a Piston Set rides out of its Piece out, too big for the tray; a kit part riding in at any bay goes to the hopper)
            var jb = On("Large Conveyor Join", 85.75f, -17.25f, Flow, "-y.0.0", PressBlue);
            Convey(ja, "Piece out", jb, "Piece in", PressBlue, Wide);
            Convey(presses[4], "Piece out", jb, "Piece in 2", PressBlue);
            var j1 = On("Large Conveyor Join", 90.75f, -17.25f, Flow, "-y.0.0", PressBlue);
            Convey(jb, "Piece out", j1, "Piece in", PressBlue, Wide);
            // The Tank press's pressed fuel tanks and the Bracket press's brackets are too big for a tray: with nothing on their Piece out
            // they dropped loose in front of the press, where no order ever saw them. Each rides a conveyor along the strip between the
            // presses and the Routers into a Join on the blank line's end, and on with the blank line's own pieces to the station.
            var jt1 = On("Conveyor Join", 91.25f, lineZ + 0.5f, Flow, "-y.0.0", PressBlue);
            Convey(prev, prevPort, jt1, "Piece in", PressBlue, Small);
            var jt2 = On("Conveyor Join", 95.75f, lineZ + 0.5f, Flow, "-y.0.0", PressBlue); // (3 m on: a 2 m rail rides between them)
            Convey(jt1, "Piece out", jt2, "Piece in", PressBlue, Small);
            foreach (var (press, join, lane) in new[] { (presses[5], jt1, -24.95f), (presses[1], jt2, -25.2f) })
            {
                var po = PortAt(press, "Piece out");
                var jn = PortAt(join, "Piece in 3");
                Convey(press, "Piece out", join, "Piece in 3", PressBlue, Small, Along(new[] { V(po.X, 0, lane), V(jn.X, 0, lane) }).Select(n => n with { Y = po.Y }).ToArray());
            }
            var lo = PortAt(jt2, "Piece out");
            var li = PortAt(j1, "Piece in 2");
            Convey(jt2, "Piece out", j1, "Piece in 2", PressBlue, Small, Along(new[] { V(lo.X + 0.8f, 0, lo.Z), V(lo.X + 0.8f, 0, li.Z - 1.0f), V(li.X, 0, li.Z - 1.0f) }).Select(n => n with { Y = lo.Y }).ToArray());
            // (No rack of its own: a rack here had no road on, so a piece that went to it as storage waited there for ever. Every blank on
            // this line is ordered, by a press or the station; a stray one is held at its Router, which says why.)
            // The Engine press's kit (a Bearing for a flywheel) from Assembler B's Router, way 3.
            ConveyVia(asm.RouterA, "Piece out 2", presses[4], "Piece in 2", Assembler, -24.6f); // (the Engine press's kit, a Bearing for a flywheel or a crank, from the Assembler's Router, way 2)
            // Trays: the Gear press's Gear Sets to Assembler A; the Tank press's fuel tanks and the Bracket press's brackets on to the station.
            _gear = presses[0]; // (its Gear Sets ride a conveyor into the Assembler: AssemblyFeeds)
            // Copper: its own Extruder and the Cable press, the coils by chute to Assembler A (a Servo Motor's).
            var copperEx = On("Extruder", 44.25f, -18.25f, Back, paint: Copper);
            ChuteVia(r.Copper, copperEx, -13.45f);
            var cable = On("Press", 44.75f, -21.25f, Back, "-y.0.0", Copper);
            Onto(cable, "dieslot", "Cable Die", "mount");
            Convey(copperEx, "Piece out", cable, "Piece in", Copper);
            _cable = cable;
            Sign(62.75f, -14.75f, PressBlue, "Blanks in from the south, parts out north", false);
            return new Presses(blank, presses[0], presses[1], presses[5], presses[3], presses[4], cable, copperEx, j1, presses[2]);
        }

        private VPart? _terminal;
        private readonly List<VPart> _toStation = new(); // (machines whose trays chute into the station's Chute in)

        /// <summary>
        /// The Assembler's one Piece in, through a Join in front of it: the Gear press's Gear Sets and the Cable press's Copper Coils ride
        /// in on conveyors, and the Assembler's own Router loops back (way 2) with a part it made for its next recipe. Each piece rides
        /// tagged for the order that asked for it, so a chain counts it in. (A tray chuted into a store hid them from the chain: it asked
        /// again and again, 197 Copper Coils for two.)
        /// </summary>
        private void AssemblyFeeds()
        {
            var (ra, a) = _loop;
            var ja = On("Conveyor Join", 47.75f, -17.25f, Flow, "-y.0.0", Assembler);
            Convey(ja, "Piece out", a, "Piece in", Assembler, Small);
            var ji = PortAt(ja, "Piece in 2");
            // The loop: its Router's way 3 back round the north side into the Join, a part it makes for its own next order (a Servo Motor
            // for a Steering Pivot, a Bearing for a Swing Arm). (A rack on the loop broke its road to itself: a rack isn't a junction.)
            var a3 = PortAt(ra, "Piece out 3");
            var jn = PortAt(ja, "Piece in 3");
            Convey(ra, "Piece out 3", ja, "Piece in 3", Assembler, Small, Along(new[] { V(a3.X, 0, -15.6f), V(jn.X, 0, -15.6f) }).Select(n => n with { Y = a3.Y }).ToArray());
            var g = PortAt(_gear!, "Piece out");
            Convey(_gear!, "Piece out", ja, "Piece in 2", PressBlue, Small, Along(new[] { V(g.X, 0, -20.0f), V(ji.X, 0, -20.0f) }).Select(n => n with { Y = g.Y }).ToArray());
            Convey(_cable!, "Piece out", ja, "Piece in", Copper, Small);
        }

        // ---- 6. Panels and the cutter (main floor east, row 1) ----------------------------------------------------------------------

        private sealed record Panels(VPart Sheet, VPart Former, VPart Glass, VPart Cutter, VPart RouterP);

        /// <summary>
        /// The panel line: an Extruder with a Die Frame (a 1 m die) makes steel sheet into the Panel Former; a Router after it sends a panel
        /// the cutter ordered ahead through a Join into the Laser Cutter, the rest (way 2) south to the station line. A second Panel Former
        /// fed glass by chute forms glass sheet for the panes, into the Join's side. The cutter's parts go on to the station line.
        /// </summary>
        private Panels PanelHall(Refined r)
        {
            var sheet = On("Extruder", 48.25f, Row1, Flow, paint: PanelGreen);
            Paint(Onto(sheet, "+x", "Die Frame", "-x"), PanelGreen); // (a frame joined to it widens the die across its bounds: ConstructExtruder.Reach)
            ChuteVia(r.Steel, sheet, -7.0f);
            var former = On("Panel Former", 54.25f, Row1, Flow, paint: PanelGreen);
            Convey(sheet, "Piece out", former, "Piece in", PanelGreen);
            var rp = On("Conveyor Router", 58.75f, -4.25f, Flow, "-y.0.0", PanelGreen);
            Convey(former, "Piece out", rp, "Piece in", PanelGreen, Small);
            var jc = On("Conveyor Join", 62.25f, -4.25f, Flow, "-y.0.0", CutterRed);
            Convey(rp, "Piece out", jc, "Piece in", PanelGreen, Small);
            var cutter = On("Laser Cutter", 67.75f, Row1, Flow);
            Convey(jc, "Piece out", cutter, "Piece in", CutterRed, Small);
            var offcut = Bin(70.75f, -7.75f, CutterRed); // (off the walkway)
            Link("chute", cutter, offcut, "Offcut", null, CutterRed);
            var glass = On("Panel Former", 54.25f, Row2, Flow, paint: GlassBlue);
            ChuteVia(r.Glass, glass, -13.85f);
            Corner(glass, "Piece out", jc, "Piece in 2", GlassBlue, Small);
            Sign(50.25f, Posts1, PanelGreen, "Sheet in, formed panels out");
            Sign(64.75f, Posts1, CutterRed, "LASER CUTTER: a panel in, a cut part out");
            Sign(56.25f, Posts2, GlassBlue, "GLASS: formed glass sheet for the panes");
            return new Panels(sheet, former, glass, cutter, rp);
        }

        // ---- 7. Wheels, the station line and the station ----------------------------------------------------------------------------

        /// <summary>
        /// Every piece bound for the station meets on one line (main floor, row 2, x 62 to 90): the Wheel Former's wheels, the panels the
        /// cutter didn't order (Router P's way 2), the cutter's parts and the press hall's pieces, through three Joins into Router S, whose
        /// way ahead runs into the station's Piece in and way 2 to a Long Piece Rack (storage). Assembler A's Router runs into the
        /// station's Piece in 2; the Tank and Bracket presses' trays chute into its Chute in.
        /// </summary>
        private (VPart Station, VPart RouterS, VPart Wheel, VPart Rack) StationLine(Refined r, Assembly asm, Presses ph, Panels pn)
        {
            var wheel = On("Wheel Former", 64.75f, Row2, Flow, paint: Wheels);
            ChuteVia(r.Rubber, wheel, -7.9f);
            var j1 = On("Conveyor Join", 68.75f, -10.75f, Flow, "-y.0.0", Wheels);
            Convey(wheel, "Piece out", j1, "Piece in", Wheels, Small);
            ConveyVia(pn.RouterP, "Piece out 2", j1, "Piece in 3", PanelGreen, -7.6f, Small);
            var j2 = On("Conveyor Join", 73.75f, -10.75f, Flow, "-y.0.0", CutterRed);
            Convey(j1, "Piece out", j2, "Piece in", Wheels, Small);
            Corner(pn.Cutter, "Piece out", j2, "Piece in 3", CutterRed, Small);
            var rs = On("Conveyor Router", 78.75f, -10.75f, Flow, "-y.0.0", StationTeal);
            Convey(j2, "Piece out", rs, "Piece in", StationTeal, Small);
            var station = On("Building Station", 92.25f, -9.25f, Front, "-y.0.0");
            Config(station, ("bayWidth", 6.0), ("bayDepth", 8.0), ("bayHeight", 3.5));
            // Router S ahead through Join S into the station's side; S's storage rack (way 2) runs back into Join S's south side, so a piece
            // stored there is fetched on to the station (a rack with no way on left a Wheel at "Collect" for good).
            var js = On("Conveyor Join", 87.25f, -10.75f, Flow, "-y.0.0", StationTeal);
            Convey(rs, "Piece out", js, "Piece in", StationTeal, Small);
            var ro = PortAt(js, "Piece out");
            var ji = PortAt(ph.LineOut, "Piece in 3");
            Convey(js, "Piece out", ph.LineOut, "Piece in 3", StationTeal, Small, V(89.25f, ro.Y, ro.Z), V(89.25f, ro.Y, ji.Z + 1.0f), V(ji.X, ro.Y, ji.Z + 1.0f)); // (into the press line's last Join: the station's side port takes only 0.5 m, so everything enters by its back)
            // The last Router before the station (a Large one: a Differential is 1.18 m across): ahead into the station's back port, way 2
            // south into the Crafting Terminal's tray, so a part ordered there of any maker on the floor rides to it.
            var rq = On("Large Conveyor Router", 97.25f, -17.25f, Flow, "-y.0.0", StationTeal); // (4.5 m after the last Join: a 2 m rail rides between them)
            Convey(ph.LineOut, "Piece out", rq, "Piece in", PressBlue, Wide);
            var po = PortAt(rq, "Piece out");
            var sb = PortAt(station, "Piece in");
            Convey(rq, "Piece out", station, "Piece in", PressBlue, Wide, V(po.X + 1.0f, po.Y, po.Z), V(po.X + 1.0f, po.Y, sb.Z - 3.0f - (po.Y - sb.Y)), V(sb.X, po.Y, sb.Z - 3.0f - (po.Y - sb.Y)), V(sb.X, po.Y, sb.Z - 2.0f - (po.Y - sb.Y)), V(sb.X, sb.Y, sb.Z - 2.0f)); // (round east and up into the station's back, a 45 degree step down to its port)
            _terminal = On("Crafting Terminal", 99.25f, -23.75f, Front, "-y.0.0");
            Convey(rq, "Piece out 2", _terminal, "Piece in", StationTeal, Small);
            Sign(102.75f, -21.75f, StationTeal, "CRAFTING TERMINAL: F, order a part, it rides here", false);
            foreach (var t in _toStation)
                ChuteVia(t, station, -14.35f - 0.15f * _toStation.IndexOf(t));
            ChuteVia(r.Copper, station, -14.05f); // (link stock: a unit of copper a metre of wire)
            var rack = On("Piece Rack", 79.75f, -13.25f, Flow, "-y.0.0", StationTeal); // (the Router's storage, along the main floor's back edge)
            Convey(rs, "Piece out 2", rack, "Piece in", StationTeal, Small);
            Convey(rack, "Piece out", js, "Piece in 2", StationTeal, Small);
            On("Loader Arm", 95.75f, -10.75f, Front, "-y.0.0");
            On("Loader Arm", 95.75f, -4.25f, Front, "-y.0.0");
            Sign(66.25f, Posts2, Wheels, "WHEELS: rubber in, a formed wheel out");
            Sign(76.25f, Posts2, StationTeal, "ROUTER: ordered pieces to the station, the rest to the rack");
            Sign(90.25f, Posts1, StationTeal, "F on the station: pick Works Runabout, Start");
            return (station, rs, wheel, rack);
        }

        // ---- 9. The tool room: dies made on the floor ----------------------------------------------------------------------------

        /// <summary>
        /// The tool room, on the back floor's south strip (x 58 to 76), cuts a spare die when it is asked: its Extruder makes nothing but
        /// orders (a Die Lathe takes none, so the line can't ask it for a die), and its own console's bill is one 0.25 m steel bar. Bill,
        /// then Send: one bar is extruded, the Die Lathe cuts it into a Bracket Die, which rides into the die stock rack and on by chute to a
        /// Die Rack, where a player takes it to fit a press. (It ran on its own settings at first and piled dies on the floor.)
        /// </summary>
        private void ToolRoom(Presses ph)
        {
            var bar = On("Extruder", 60.25f, -30.75f, Flow, paint: ToolGrey);
            ChuteVia(_steelB!, bar, -31.6f);
            var lathe = On("Die Lathe", 64.25f, -31.25f, Flow, "-y.0.0", ToolGrey);
            Config(lathe, ("recipe", LatheRecipe(lathe, "Bracket Die")));
            Convey(bar, "Piece out", lathe, "Piece in", ToolGrey);
            var dies = On("Piece Rack", 66.75f, -30.75f, Flow, "-y.0.0", ToolGrey); // (a die is too big for the lathe's tray: it rides out of its Piece out)
            Convey(lathe, "Piece out", dies, "Piece in", ToolGrey);
            var rack = On("Die Rack", 74.75f, -31.25f, Flow, "-y.0.0", ToolGrey);
            Link("chute", dies, rack, null, null, ToolGrey);
            var console = On("Line Console", 58.25f, -28.75f, Front, paint: Control);
            Run(console, bar);
            Run(console, lathe);
            OrdersOnly(bar);
            Config(console, ("startOn", true));
            Bill(console, "Extrusion solid 25x25x25 steel", 1);
            Sign(56.75f, -28.75f, ToolGrey, "TOOL ROOM: Bill, Send: one bar, one spare Bracket Die", false);
        }

        /// <summary>An Assembler's Recipe setting for <paramref name="part"/>: its option's number (0 is None).</summary>
        private static int AsmRecipe(VPart asm, string part) => LatheRecipe(asm, part);

        /// <summary>A Die Lathe's Recipe setting for <paramref name="die"/>: its option's number (0 is None).</summary>
        private static int LatheRecipe(VPart lathe, string die)
        {
            var opts = lathe.Def.Config[lathe.Def.CfgSlot[(int)CfgKey.Recipe]].Options;
            var k = Array.FindIndex(opts, o => o.StartsWith(die, StringComparison.Ordinal));
            return k > 0 ? k : throw new VehicleKitException($"a {lathe.Name} cuts no {die} ({string.Join(", ", opts)})");
        }

        // ---- 10. Scrap in, spares out ------------------------------------------------------------------------------------------------

        /// <summary>
        /// The Recycler at the west end (scrap in: a wreck dropped in its mouth becomes metal for the steel Foundry); the Smart Terminal
        /// and Inventory Beacon on the Parts Shelf's chutes (a player pulls a spare Gear Set or Coil there; the beacon lists the shelf to
        /// players near it in survival).
        /// </summary>
        private void ScrapAndSpares(Refined r)
        {
            var rec = On("Recycler", 2.25f, -11.25f, Front, paint: Refining);
            ChuteVia(rec, r.SteelFoundry, -8.1f);
            Sign(2.25f, Posts2, Refining, "RECYCLER: drop a wreck in; its metal goes to steel", false);
            var term = On("Smart Terminal", 43.75f, -8.25f, Front, paint: Assembler); // (by the yard, on its chute network: a player pulls steel here)
            Link("chute", term, r.Steel, "Network", null, Assembler);
            var beacon = On("Inventory Beacon", 38.25f, -7.75f, Front, paint: Assembler);
            Link("chute", beacon, r.Steel, null, null, Assembler);
        }

        // ---- 11. Hand orders: the Crafting Terminal and the Panel Designer -------------------------------------------------------

        /// <summary>
        /// Router S's way 3 runs north to Router T (main floor, row 1, x 78 to 88): ahead into the Crafting Terminal's tray (order a part:
        /// it is made on the line and rides here), way 2 into the Panel Designer's (design a panel: the Panel Former forms it). Both are on
        /// the orders console's wire, so they see every maker.
        /// </summary>
        private (VPart Terminal, VPart Designer) HandOrders(VPart rs)
        {
            var rt = On("Conveyor Router", 78.25f, -7.25f, Front, "-y.0.0", StationTeal);
            Convey(rs, "Piece out 3", rt, "Piece in", StationTeal, Small);
            var term = _terminal!;

            var desk = On("Panel Designer", 86.25f, -6.25f, Back, "-y.0.0");
            Convey(rt, "Piece out", desk, "Piece in", PanelGreen, Small);

            Sign(86.25f, Posts1, PanelGreen, "PANEL DESIGNER: design a panel; it is formed for you");
            return (term, desk);
        }

        // ---- 13. The wing: a former, a pad and an arm ---------------------------------------------------------------------------

        /// <summary>
        /// East of the station's bay (main floor, x 102 to 110): a wing has no conveyor, so the Airframe Former's wing grows west over a
        /// Lay-down Pad and drops on it, and a Military Loader Arm south of the pad (8 m of reach: the pad, the former's out and the whole
        /// bay) fits it from the pad straight onto the build. The former is fed steel by chute from the yard and is on the orders line.
        /// </summary>
        private VPart WingCorner(Refined r)
        {
            On("Lay-down Pad", 102.25f, -10.75f, Front, "-y.0.0", Hazard);
            var former = On("Airframe Former", 107.25f, -6.75f, Toward, paint: Wheels); // (its die over the pad's east end: the wing grows west over the pad)
            ChuteVia(r.Steel, former, -13.95f); // (along the main floor's back edge, off the walkway)
            On("Military Loader Arm", 102.25f, -14.75f, Front, "-y.0.0");
            Sign(107.25f, Posts1, Wheels, "AIRFRAME FORMER: the wing drops on the pad; the arm fits it");
            return former;
        }

        // ---- 12. Monitors and the station's fuel -----------------------------------------------------------------------------------

        /// <summary>
        /// A Number Readout on the refinery console's Data out and a Text Sign on the orders console's, each on a post by its console,
        /// and a Medium Text Sign over Router S as the network board (its Data out: where each piece goes). A Fuel Tank by the station,
        /// on the body's fuel pool (the genset's tanks), piped into its Fluid in, fills the Runabout's tank in survival.
        /// </summary>
        private void Monitors(VPart refine, VPart orders, VPart rs, VPart station)
        {
            var p1 = Sign(11.25f, Posts1, Refining, "MONITOR: the refinery console's Data out");
            var small = Onto(p1, "y+1", "Number Readout", "back");
            Paint(small, Board);
            BehindPost(refine, small, 8.25f, 11.25f);
            var p2 = Sign(43.25f, Posts1, Control, "MONITOR: every maker on the orders line");
            var medium = Onto(p2, "y+1", "Text Sign", "back");
            Paint(medium, Board);
            BehindPost(orders, medium, 44.75f, 43.25f);
            var post = On("Frame Beam", 76.25f, -12.75f, Post, "end-", StationTeal, "painted plating");
            var adapter = Onto(post, "y+3", "Adapter Medium to Small", "front0.0"); // (a medium sign on a small post)
            Paint(adapter, StationTeal);
            var board = Onto(adapter, "back", "Medium Text Sign", "back");
            Paint(board, Board);
            Text(board, "NETWORK BOARD");
            Link("data", rs, board, "Data out", null, StationTeal, true, Along(new[] { V(FromRest(Live.Parts[rs.Index].Pos).X, 0, -12.6f), V(76.25f, 0, -12.6f) }));
            var fuel = On("Fuel Tank", 90.75f, -3.25f, Front, "-y", Power);
            Link("pipe", fuel, station, "Fuel out", "Fluid in", Power);
        }

        /// <summary>
        /// A console's Data out to a screen on a post (front walkway's edge): laid along the floor behind the console and the post and up the
        /// post's back, so no cable crosses a screen (look review 7: a straight link ran in front of the line's status sign).
        /// </summary>
        private void BehindPost(VPart console, VPart screen, float consoleX, float postX)
        {
            var at = FromRest(Live.Parts[screen.Index].Pos);
            var z = Posts1 - 0.4f;
            var nodes = Along(new[] { V(consoleX, 0, z), V(postX + 0.3f, 0, z) }).ToList();
            nodes.Add(V(postX + 0.3f, at.Y, z));
            Link("data", console, screen, "Data out", null, CableDark, true, null, nodes.ToArray());
        }

        // ---- 8. The orders console ----------------------------------------------------------------------------------------------------

        /// <summary>
        /// The making line's console (front walkway, x 44.75): its wires into every maker's Run are the line, and into the station's Orders
        /// port, so each part the station lacks becomes an order at the machine that makes it. Cables run along the walkway's edge, down
        /// the gap at x 43.6 between the yard and the halls, then along the middle aisle or the back floor's north edge.
        /// </summary>
        private VPart OrdersConsole(params VPart[] makers)
        {
            const float cx = 44.75f, gapX = 43.6f;
            var console = On("Line Console", cx, Posts1, Front, paint: Control);
            Sign(46.25f, Posts1, Control, "ORDERS: Start runs every maker; the station orders here");
            var k = 0;
            foreach (var m in makers)
            {
                var at = FromRest(Live.Parts[m.Index].Pos);
                var off = 0.04f * k++; // (cables side by side, never on one line)
                var edge = -2.0f + off;
                var pts = new List<Vector3> { V(cx, 0, edge) };
                if (at.Z > -7.5f)
                    pts.Add(V(at.X, 0, edge)); // (row 1: along the walkway's edge to it)
                else
                {
                    var lane = at.Z > -14f ? -8.0f - off : -14.2f - off; // (row 2: the middle aisle; the back floor: its north edge)
                    pts.Add(V(gapX - off, 0, edge));
                    pts.Add(V(gapX - off, 0, lane));
                    pts.Add(V(at.X, 0, lane));
                    if (MathF.Abs(at.Z - lane) > 5.5f)
                        pts.Add(V(at.X, 0, at.Z + 1.5f * MathF.Sign(lane - at.Z)));
                }
                if (m.Def.IsBuildStation || m.Def.IsCraftTerminal)
                    Link("data", console, m, null, "Orders", Control, true, null, Along(pts.ToArray()));
                else
                {
                    Link("data", console, m, null, "Run", Control, true, null, Along(pts.ToArray()));
                    OrdersOnly(m); // (an empty queue: it waits for an order, never runs its own settings while the line is on)
                }
            }
            return console;
        }

        // ---- Helpers --------------------------------------------------------------------------------------------------------------

        // ---- The look (the look review, 2026-11-14): one steel for every link and junction, a floor colour an area, numbered signs ----

        private const string ConveyorSteel = "#6b7177", ChuteSteel = "#555b62", CableDark = "#34383d";

        /// <summary>Every routed link in one steel by kind, never a stage's colour: the machines carry the colour, the floor the area's.</summary>
        private static string LinkPaint(string type) => type switch { "conveyor" => ConveyorSteel, "chute" => ChuteSteel, "pipe" => ChuteSteel, _ => CableDark };

        private new void Link(string type, VPart from, VPart to, string? fromPort = null, string? toPort = null, string? paint = null, bool laid = false, params Vector3[] nodes) =>
            base.Link(type, from, to, fromPort, toPort, LinkPaint(type), laid, null, nodes);

        private new void Link(string type, VPart from, VPart to, string? fromPort, string? toPort, string? paint, bool laid, Vector2? size, params Vector3[] nodes) =>
            base.Link(type, from, to, fromPort, toPort, LinkPaint(type), laid, size, nodes);

        /// <summary>An area's floor colour (concrete tinted, look.md "Machine look": the floor carries the area, the machines their own colours).</summary>
        private static string AreaFloor(float x, float z) => z > -14
            ? x < 8 ? "#7b7163" : x < 38 ? "#877866" : x < 44 ? "#7f786c" : x < 76 ? "#6f8f55" : "#3f8a86" // (the panel line green, the station line teal)
            : x < 46 ? "#7a7682" : x < 57 && z > -24 ? "#8c7f58" : z < -28 && x < 80 ? "#79766f" : "#5468a8"; // (the Assembler's tiles ochre: where the lines meet; the press hall blue)

        /// <summary>
        /// An area's name, numbered in the order the walkway passes it: a Medium Text Sign 3.5 m up on two posts, facing the walkway (or
        /// the way <paramref name="o"/> says), a lamp on top. Seen from the overview.
        /// </summary>
        private void AreaSign(float x, float z, string text, string colour) => MediumBoard(x, z, text, colour, West, tall: true);

        /// <summary>
        /// A Medium Text Sign on a steel post (two Frame Beams, 3.5 m up, when <paramref name="tall"/>), a lamp on top; the board's face in
        /// the area's colour (look review 2: every post and frame one steel, the board carries the area).
        /// </summary>
        private void MediumBoard(float x, float z, string text, string colour, Orient o, bool tall)
        {
            var post = On("Frame Beam", x, z, o, "end-", PostSteel, "painted plating");
            var top = post;
            if (tall)
            {
                top = Onto(post, "end+", "Frame Beam", "end-");
                Paint(top, PostSteel);
                Finish(top, "painted plating");
            }
            var adapter = Onto(top, tall ? "y+1" : "y+3", "Adapter Medium to Small", "front0.0");
            Paint(adapter, PostSteel);
            var board = Onto(adapter, "back", "Medium Text Sign", "back");
            Paint(board, colour);
            Config(board, ("ink", Lighter(colour))); // (its face is a screen: the area's colour is its writing, lightened to read on the dark face)
            Text(board, text);
            var lamp = Onto(top, "end+", "Lamp", "-y");
            Config(lamp, ("colour", "#ffd27a"), ("brightness", 160));
            _lamps.Add(lamp);
        }

        /// <summary>A painted Floor Arrow flat on the floor at (x, z), pointing along <paramref name="way"/>.</summary>
        private void Arrow(float x, float z, Vector3 way) =>
            On("Floor Arrow", x, z, new Orient(Vector3.UnitY, Vector3.UnitY, Vector3.UnitX, way), "bottom", Hazard, "hazard paint");

        /// <summary>The walkway: a Lane Marking, then an arrow pointing east, the whole length west to east (look review 3: the flow's way).</summary>
        private void Walkway()
        {
            for (var x = -6.25f; x < 106f; x += 6.0f) // (3.6 m lanes on the 0.5 m lattice, a 1.8 m arrow in each gap)
            {
                On("Lane Marking", x, -1.25f, Front, "bottom", null, "hazard paint");
                if (x + 3.0f < 108f)
                    Arrow(x + 3.0f, -1.25f, Vector3.UnitX);
            }
        }

        /// <summary>Arrows beside the main lines, east with the flow: the blank line south of its Routers, the press line's north strip.</summary>
        private void FlowArrows()
        {
            foreach (var x in new[] { 62.75f, 68.25f, 73.75f, 79.25f, 84.75f })
                Arrow(x, -28.75f, Vector3.UnitX);
            foreach (var x in new[] { 77.25f, 83.25f })
                Arrow(x, -15.75f, Vector3.UnitX);
        }

        private const string PostSteel = "#7a8086";

        /// <summary>A colour halfway to white: an area's colour as writing on a dark screen.</summary>
        private static string Lighter(string hex)
        {
            var v = Convert.ToInt32(hex[1..], 16);
            int r = v >> 16 & 255, g = v >> 8 & 255, b = v & 255;
            return $"#{(r + 255) / 2:x2}{(g + 255) / 2:x2}{(b + 255) / 2:x2}";
        }
        private static readonly Orient West = new(Vector3.UnitZ, Vector3.UnitY, Vector3.UnitY, -Vector3.UnitX); // a post whose board faces west: read by someone walking east

        /// <summary>A part standing on the floor by its own socket <paramref name="by"/> at (x, z).</summary>
        private VPart On(string part, float x, float z, Orient o, string by = "bottom", string? paint = null, string? finish = null)
        {
            if (part.Contains("Conveyor ", StringComparison.Ordinal) && paint != null)
                paint = ConveyorSteel; // (Joins and Routers: the conveyors' own steel)
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

        /// <summary>A part's socket's place (vehicle coordinates).</summary>
        private Vector3 SocketAt(VPart p, string name)
        {
            var s = p.Def.Sockets.First(x => x.Name == name);
            ref var cp = ref Live.Parts[p.Index];
            return FromRest(cp.Pos + Rot24.Apply(cp.Rot, s.At));
        }

        /// <summary>A sign post at (x, z): a Frame Beam stood up in the stage's colour, a Text Sign facing the aisle, a lamp on top.</summary>
        private VPart Sign(float x, float z, string colour, string text, bool lit = true)
        {
            var post = On("Frame Beam", x, z, Post, "end-", PostSteel, "painted plating"); // (every post one steel, look review 2)
            var sign = Onto(post, "y+3", "Text Sign", "back");
            Paint(sign, Board);
            Text(sign, text);
            if (!lit)
                return post;
            var lamp = Onto(post, "end+", "Lamp", "-y");
            Config(lamp, ("colour", "#ffb347"), ("brightness", 120));
            _lamps.Add(lamp);
            return post;
        }

        /// <summary>A Lane Marking in hazard paint flat on the floor at (x, z), across a conveyor that runs along x (<paramref name="acrossX"/>) or along z.</summary>
        private void Stripe(float x, float z, bool acrossX) =>
            On("Lane Marking", x, z, new Orient(Vector3.UnitY, Vector3.UnitY, Vector3.UnitX, acrossX ? Vector3.UnitZ : Vector3.UnitX), "bottom", Hazard, "hazard paint");

        /// <summary>
        /// Raw stock: a Storage Volume at (x, z), a box on the floor that a creative world fills as the blueprint builds (to 24 %, under an
        /// open side's lip: Sealed volumes); a survival world builds it empty.
        /// </summary>
        private VPart Heap(string material, float x, float z, float size, float height = 2.0f)
        {
            var v = On("Storage Volume", x, z, Front, "-y", HeapPaint(material)); // (each heap's box its own colour: look review 6)
            // (the box from 5 cm over the floor, size square and 2 m high: 24 % of it is the raw stock, under every open side's lip)
            Config(v, ("sizeX", size), ("sizeY", (double)height), ("sizeZ", size), ("atY", height / 2.0 - 0.15), ("material", material), ("fill", 24));
            return v;
        }

        private static string HeapPaint(string material) => material switch
        {
            "iron ore" => "#8a4a32", "copper ore" => "#3f8f7a", "stone" => "#9a9a94", "wood" => "#6b4a26", _ => "#d8c48a", // (sand)
        };

        /// <summary>
        /// An empty store at (x, z): a Storage Volume's box <paramref name="size"/> square and 1.5 m high, holding up to a quarter of it before an
        /// open side pours (600 units at 2 m). Its chutes move what the other end takes (a Storage Volume's chute load is 160 a second).
        /// </summary>
        private VPart Store(float x, float z, float size)
        {
            var v = On("Storage Volume", x, z, Front, "-y", Yard);
            Config(v, ("sizeX", size), ("sizeY", 1.5), ("sizeZ", size), ("atY", 0.6));
            return v;
        }

        /// <summary>A Materials Bin (600 units of one material) on the floor at (x, z).</summary>
        private VPart Bin(float x, float z, string paint) => On("Materials Bin", x, z, Front, paint: paint);

        /// <summary>A refiner's Recipe setting for the recipe that makes <paramref name="output"/>: its option's number (0 is Auto).</summary>
        private static int RecipeOf(VPart refiner, string output)
        {
            var opts = refiner.Def.Config[refiner.Def.CfgSlot[(int)CfgKey.Recipe]].Options;
            var k = Array.FindIndex(opts, o => o.StartsWith(output + " ", StringComparison.Ordinal) || o.EndsWith(" to " + output, StringComparison.Ordinal));
            return k > 0 ? k : throw new VehicleKitException($"a {refiner.Name} has no recipe making {output} ({string.Join(", ", opts)})");
        }

        private void Feed(VPart from, VPart machine) => Link("chute", from, machine, OutPort(from), InPort(machine), Stock);

        private static string? OutPort(VPart p) => p.Def.NamedPorts.Any(n => n.Type == RouteType.Chute && n.Name == "Chute out") ? "Chute out" : null;

        private static string? InPort(VPart p) => p.Def.NamedPorts.Any(n => n.Type == RouteType.Chute && n.Name == "Chute in") ? "Chute in" : null;

        /// <summary>A chute from a refiner's output (or a bin) into the next store: a refiner's Chute in, a bin.</summary>
        private void Chute(VPart from, VPart to, params Vector3[] nodes) =>
            Link("chute", from, to, OutPort(from), InPort(to), Refining, false, nodes);

        /// <summary>A chute laid along the floor: out of <paramref name="from"/>'s port to the lane at z <paramref name="laneZ"/>, along it, and into <paramref name="to"/>.</summary>
        private void ChuteVia(VPart from, VPart to, float laneZ)
        {
            var a = ChutePortAt(from, "Chute out");
            var b = ChutePortAt(to, "Chute in");
            // (from the port straight to the lane, along it, and straight to the other port: Along adds a node every 7.5 m, the last one
            // within a metre of the port, since a segment reaches 8 m)
            var pts = new List<Vector3>();
            if (MathF.Abs(a.Z - laneZ) > 5.5f)
                pts.Add(V(a.X, 0, a.Z + MathF.Sign(laneZ - a.Z)));
            pts.Add(V(a.X, 0, laneZ));
            pts.Add(V(b.X, 0, laneZ));
            if (MathF.Abs(b.Z - laneZ) > 5.5f)
                pts.Add(V(b.X, 0, b.Z - MathF.Sign(b.Z - laneZ)));
            var nodes = Along(pts.ToArray()).Select(n => n with { Y = FloorY + 0.2f }).ToArray();
            try
            {
                Link("chute", from, to, OutPort(from), InPort(to), Refining, true, nodes);
            }
            catch (VehicleKitException e)
            {
                throw new VehicleKitException($"{e.Message} ({from.Name} at {Show(a)}, {to.Name} at {Show(b)}, nodes {string.Join(" ", nodes.Select(Show))})");
            }
        }

        /// <summary>A part's named chute port (vehicle coordinates), else its centre.</summary>
        private Vector3 ChutePortAt(VPart p, string name)
        {
            ref var cp = ref Live.Parts[p.Index];
            foreach (var np in p.Def.NamedPorts)
                if (np.Type == RouteType.Chute && np.Name == name)
                    return FromRest(cp.Pos + Rot24.Apply(cp.Rot, np.At));
            return FromRest(cp.Pos);
        }

        /// <summary>A conveyor laid out of one port to the lane at z <paramref name="laneZ"/>, along it at the out port's height, and into the other.</summary>
        private void ConveyVia(VPart from, string fromPort, VPart to, string toPort, string paint, float laneZ, Vector2? size = null)
        {
            var a = PortAt(from, fromPort);
            var b = PortAt(to, toPort);
            var pts = new List<Vector3>();
            if (MathF.Abs(a.Z - laneZ) > 5.5f)
                pts.Add(V(a.X, 0, a.Z + MathF.Sign(laneZ - a.Z)));
            pts.Add(V(a.X, 0, laneZ));
            pts.Add(V(b.X, 0, laneZ));
            if (MathF.Abs(b.Z - laneZ) > 5.5f)
                pts.Add(V(b.X, 0, b.Z - MathF.Sign(b.Z - laneZ)));
            Convey(from, fromPort, to, toPort, paint, size, Along(pts.ToArray()).Select(n => n with { Y = a.Y }).ToArray());
        }

        /// <summary>A conveyor with one bend: along x out of the first port (at its height) to the second's x, then along z into it.</summary>
        private void Corner(VPart from, string fromPort, VPart to, string toPort, string paint, Vector2? size = null)
        {
            var a = PortAt(from, fromPort);
            var b = PortAt(to, toPort);
            var nodes = Along(new[] { V(a.X, 0, a.Z), V(b.X, 0, a.Z) }).Skip(1).Select(n => n with { Y = a.Y }).ToArray(); // (a node every 7.5 m along the run)
            if (MathF.Abs(b.Z - a.Z) < 0.5f)
                nodes = nodes[..^1]; // (straight: the corner would be the port itself)
            Convey(from, fromPort, to, toPort, paint, size, nodes);
        }

        /// <summary>A conveyor between two ports; a refusal names both ports' places, so a layout fault reads at once.</summary>
        private void Convey(VPart from, string fromPort, VPart to, string toPort, string paint, Vector2? size = null, params Vector3[] nodes)
        {
            try
            {
                Link("conveyor", from, to, fromPort, toPort, paint, false, size, nodes);
            }
            catch (VehicleKitException e)
            {
                throw new VehicleKitException($"{e.Message} ({from.Name} {fromPort} at {Show(PortAt(from, fromPort))}, {to.Name} {toPort} at {Show(PortAt(to, toPort))})");
            }
        }

        /// <summary>A console's wire into a machine's Run: straight when near, else a cable laid along the floor through (x, z) points.</summary>
        private void Run(VPart console, VPart machine, params Vector3[] floor)
        {
            if (floor.Length == 0)
            {
                Link("data", console, machine, null, "Run");
                return;
            }
            Link("data", console, machine, null, "Run", Control, true, null, Along(floor));
        }

        /// <summary>Floor points (x, z) as cable nodes on the floor, with a node at least every 7.5 m (a segment reaches 8).</summary>
        private static Vector3[] Along(Vector3[] floor)
        {
            var nodes = new List<Vector3>();
            for (var k = 0; k < floor.Length; k++)
            {
                var p = V(floor[k].X, FloorY + 0.03f, floor[k].Z);
                if (k > 0)
                {
                    var q = nodes[^1];
                    var d = Vector3.Distance(p, q);
                    for (var n = 1; n * 7.5f < d; n++)
                        nodes.Add(Vector3.Lerp(q, p, n * 7.5f / d));
                }
                nodes.Add(p);
            }
            return nodes.ToArray();
        }

        /// <summary>A lamp wired from the main switch: a cable along the front walkway's edge (and across to the middle aisle), up its post.</summary>
        private void Lamp(VPart lamp)
        {
            var at = FromRest(Live.Parts[lamp.Index].Pos);
            var back = at.Z < -6;
            var z = back ? -8.35f : -1.85f;
            var floor = new List<Vector3> { V(1.25f, 0, -1.85f) };
            if (back)
            {
                floor.Add(V(4.75f, 0, -1.85f));
                floor.Add(V(4.75f, 0, z));
            }
            // (up the post's back corner, never in front of its sign: look review 7, a cable rising 0.4 m in front read as a post before the boards)
            var rise = V(at.X + 0.35f, 0, at.Z - 0.35f);
            floor.Add(V(rise.X, 0, z));
            floor.Add(rise);
            var nodes = Along(floor.ToArray()).ToList();
            nodes.Add(V(rise.X, at.Y, rise.Z));
            Link("data", _switch!, lamp, null, null, Board, true, null, nodes.ToArray());
        }
    }
}
