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

    public void Make(PartOutput o) => o.File(File, Script.Build().Text());

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
        private const string Copper = "#b8733a", PanelGreen = "#5d7f3c", CutterRed = "#a8365e", GlassBlue = "#6aa6b8", Wheels = "#8050a8", StationTeal = "#3f8f5a";

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
                Description = "One connected factory: raw ore, wood, stone and sand in at the west end, the Works Runabout built at the east end, every production machine working on the way. Read the signs, press Start on both consoles, then pick Works Runabout at the Building Station.",
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
            Paint(root, Floor);
            Finish(root, "concrete");
            _tiles.Add(((int)(RootX - 1) / 2, (int)(-RootZ - 1) / 2));
            Tiles(0, 55, 0, 7); // the main floor: x 0 to 110, z 0 to -14
            Tiles(21, 50, 7, 16); // the back floor: x 42 to 100, z -14 to -32
            PowerPlant();
            var r = Refinery();
            var asm = AssemblyHall(r);
            var ph = PressHall(asm, r);
            var pn = PanelHall(r);
            var st = StationLine(r, asm, ph, pn);
            var orders = OrdersConsole(pn.Sheet, pn.Former, pn.Glass, pn.Cutter, st.Wheel, asm.A, asm.B, ph.Blank, ph.Gear, ph.Tank, ph.Bracket, ph.Cylinder, ph.Engine, ph.CopperEx, ph.Cable, st.Station);
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
                    Paint(t, aisle ? Aisle : Floor);
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
            var mainPost = Sign(1.25f, Posts1, Power, "1 POWER. MAIN SWITCH: off = every machine No power");
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

        private sealed record Refined(VPart Steel, VPart Rubber, VPart Cloth, VPart Bronze, VPart Copper, VPart Glass, VPart Console);

        /// <summary>
        /// Eight refiners in two rows, 8 m apart (x 8 to 36), raw stock heaped beside each: row 1 Smelter (iron ore to metal), Foundry
        /// (metal and stone to steel), Sawmill (wood to planks), Foundry (planks and sand to rubber); row 2 Workshop Smelter (copper ore to
        /// copper), Workshop Foundry (copper and metal to bronze), Sawmill (planks to cloth), Smelter (sand to glass). Metal and planks chute
        /// straight from one refiner into the next; every finished material ends in a Materials Bin of the yard (x 39 to 43). One Line
        /// Console on all eight.
        /// </summary>
        private Refined Refinery()
        {
            var smelter = On("Smelter", 10.25f, Row1, Back, paint: Refining);
            var steelF = On("Foundry", 18.25f, Row1, Back, paint: Refining);
            var sawmill = On("Sawmill", 26.25f, Row1, Back, paint: Refining);
            var rubberF = On("Foundry", 34.25f, Row1, Back, paint: Refining);
            var copperS = On("Workshop Smelter", 10.25f, Row2, Back, paint: Refining);
            var bronzeF = On("Workshop Foundry", 18.25f, Row2, Back, paint: Refining);
            var clothS = On("Sawmill", 26.25f, Row2, Back, paint: Refining);
            var glassS = On("Smelter", 34.25f, Row2, Back, paint: Refining);

            Feed(Heap("iron ore", 6.25f, -4.25f, 3.0f), smelter);
            Feed(Heap("stone", 14.25f, -4.25f, 2.4f), steelF);
            Feed(Heap("wood", 22.25f, -4.25f, 2.4f), sawmill);
            Feed(Heap("sand", 30.25f, -4.25f, 2.4f), rubberF);
            Feed(Heap("copper ore", 6.25f, -10.75f, 2.4f), copperS);
            Feed(Heap("sand", 30.25f, -10.75f, 2.4f), glassS);
            Chute(smelter, steelF);
            Chute(smelter, bronzeF);
            Chute(copperS, bronzeF);
            Chute(sawmill, rubberF);
            Chute(sawmill, clothS);

            var steel = Bin(39.75f, -3.25f, Yard);
            var rubber = Bin(42.25f, -3.25f, Yard);
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
            Sign(9.75f, Posts1, Refining, "2 REFINERY: press Start on this console");
            foreach (var m in new[] { smelter, steelF, sawmill, rubberF })
                Run(console, m, V(8.25f, 0, -2.0f), V(FromRest(Live.Parts[m.Index].Pos).X, 0, -2.0f));
            foreach (var m in new[] { copperS, bronzeF, clothS, glassS })
                Run(console, m, V(8.25f, 0, -1.95f), V(8.0f, 0, -1.95f), V(8.0f, 0, -8.0f), V(FromRest(Live.Parts[m.Index].Pos).X, 0, -8.0f));
            Sign(14.25f, Posts1, Refining, "Ore to metal; metal and stone to steel");
            Sign(30.25f, Posts1, Refining, "Wood to planks; planks and sand to rubber");
            Sign(14.25f, Posts2, Refining, "Copper ore to copper; copper and metal to bronze");
            Sign(30.25f, Posts2, Refining, "Planks to cloth; sand to glass");
            Sign(41.25f, Posts1, Yard, "3 THE YARD: every refined material, in bins");
            return new Refined(steel, rubber, cloth, bronze, copper, glass, console);
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
            var b = On("Workshop Assembler", 46.25f, -23.25f, Flow, paint: Assembler);
            var a = On("Workshop Assembler", 52.25f, -17.75f, Flow, paint: Assembler);
            ChuteVia(r.Steel, a, -7.45f);
            ChuteVia(r.Cloth, a, -13.3f);
            ChuteVia(r.Rubber, a, -7.6f);
            ChuteVia(r.Bronze, b, -13.15f);
            ChuteVia(r.Copper, b, -13.0f);
            _steelB = Bin(44.25f, -27.75f, Yard); // (the press hall's steel: chuted on from the yard's)
            ChuteVia(r.Steel, _steelB, -7.75f);
            Feed(_steelB, b);
            var rb = On("Conveyor Router", 49.75f, -22.75f, Flow, "-y.0.0", Assembler);
            Convey(b, "Piece out", rb, "Piece in", Assembler);
            Convey(rb, "Piece out", a, "Piece in", Assembler);
            var ra = On("Conveyor Router", 55.75f, -17.25f, Flow, "-y.0.0", Assembler);
            Convey(a, "Piece out", ra, "Piece in", Assembler);
            Sign(48.75f, -15.25f, Assembler, "4 ASSEMBLY: B makes bearings and servos for A", false);
            return new Assembly(a, b, ra, rb);
        }

        private VPart? _steelB;

        // ---- 5. The press hall (the back floor) -----------------------------------------------------------------------------------

        private sealed record Presses(VPart Blank, VPart Gear, VPart Tank, VPart Bracket, VPart Cylinder, VPart Engine, VPart Cable, VPart CopperEx, VPart LineOut);

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
            foreach (var x in new[] { 59.75f, 65.75f, 71.75f, 77.75f, 83.75f })
            {
                var rt = On("Conveyor Router", x, lineZ + 0.5f, Flow, "-y.0.0", PressBlue);
                Convey(prev, prevPort, rt, "Piece in", PressBlue);
                routers.Add(rt);
                (prev, prevPort) = (rt, "Piece out");
            }
            var dies = new[] { "Gear Die", "Tank Die", "Bracket Die", "Cylinder Die", "Engine Die" };
            var presses = new VPart[5];
            for (var k = 0; k < 5; k++)
            {
                var at = FromRest(Live.Parts[routers[k].Index].Pos);
                var p = presses[k] = On("Press", at.X + 1.5f, pressZ, Back, "-y.0.0", PressBlue);
                Onto(p, "dieslot", dies[k], "mount");
                Convey(routers[k], "Piece out 3", p, "Piece in", PressBlue);
            }
            // The Cylinder's and Engine's pieces out north onto a Join, then east and up to the station line.
            var j1 = On("Conveyor Join", 80.75f, -15.25f, Flow, "-y.0.0", PressBlue);
            Convey(presses[3], "Piece out", j1, "Piece in", PressBlue);
            Convey(presses[4], "Piece out", j1, "Piece in 2", PressBlue);
            // The last Router's way ahead joins them too: a blank or a chassis bar the station ordered of this Extruder rides on to it,
            // and one nobody ordered goes to the station line's rack (the Routers' storage).
            var lo = PortAt(prev, prevPort);
            var li = PortAt(j1, "Piece in 3");
            Convey(prev, prevPort, j1, "Piece in 3", PressBlue, null, Along(new[] { V(lo.X + 1.2f, 0, lo.Z), V(lo.X + 1.2f, 0, li.Z + 0.9f), V(li.X, 0, li.Z + 0.9f) }).Select(n => n with { Y = lo.Y }).ToArray());
            // The Engine press's kit (a Bearing for a flywheel) from Assembler B's Router, way 3.
            ConveyVia(asm.RouterB, "Piece out 3", presses[4], "Piece in 2", Assembler, -24.6f);
            // Trays: the Gear press's Gear Sets to Assembler A; the Tank press's fuel tanks and the Bracket press's brackets on to the station.
            ChuteVia(presses[0], asm.A, -19.75f);
            _toStation.Add(presses[1]);
            _toStation.Add(presses[2]);
            // Copper: its own Extruder and the Cable press, the coils by chute to Assembler A (a Servo Motor's).
            var copperEx = On("Extruder", 44.25f, -18.25f, Back, paint: Copper);
            ChuteVia(r.Copper, copperEx, -13.45f);
            var cable = On("Press", 44.75f, -21.25f, Back, "-y.0.0", Copper);
            Onto(cable, "dieslot", "Cable Die", "mount");
            Convey(copperEx, "Piece out", cable, "Piece in", Copper);
            ChuteVia(cable, asm.A, -24.75f);
            Sign(62.75f, -15.25f, PressBlue, "5 PRESS HALL: blanks ride in from the south, parts out north", false);
            return new Presses(blank, presses[0], presses[1], presses[2], presses[3], presses[4], cable, copperEx, j1);
        }

        private readonly List<VPart> _toStation = new(); // (machines whose trays chute into the station's Chute in)

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
            Convey(former, "Piece out", rp, "Piece in", PanelGreen);
            var jc = On("Conveyor Join", 62.25f, -4.25f, Flow, "-y.0.0", CutterRed);
            Convey(rp, "Piece out", jc, "Piece in", PanelGreen);
            var cutter = On("Laser Cutter", 67.75f, Row1, Flow);
            Convey(jc, "Piece out", cutter, "Piece in", CutterRed);
            var offcut = Bin(70.75f, -1.25f, CutterRed);
            Link("chute", cutter, offcut, "Offcut", null, CutterRed);
            var glass = On("Panel Former", 54.25f, Row2, Flow, paint: GlassBlue);
            ChuteVia(r.Glass, glass, -13.85f);
            Corner(glass, "Piece out", jc, "Piece in 2", GlassBlue);
            Sign(50.25f, Posts1, PanelGreen, "6 PANELS: sheet in, formed panels out");
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
            var wheel = On("Wheel Former", 62.25f, Row2, Flow, paint: Wheels);
            ChuteVia(r.Rubber, wheel, -7.9f);
            var j1 = On("Conveyor Join", 68.75f, -10.75f, Flow, "-y.0.0", Wheels);
            Convey(wheel, "Piece out", j1, "Piece in", Wheels);
            ConveyVia(pn.RouterP, "Piece out 2", j1, "Piece in 3", PanelGreen, -7.6f);
            var j2 = On("Conveyor Join", 73.75f, -10.75f, Flow, "-y.0.0", CutterRed);
            Convey(j1, "Piece out", j2, "Piece in", Wheels);
            Corner(pn.Cutter, "Piece out", j2, "Piece in 3", CutterRed);
            var j3 = On("Conveyor Join", 78.75f, -10.75f, Flow, "-y.0.0", PressBlue);
            Convey(j2, "Piece out", j3, "Piece in", Wheels);
            Convey(ph.LineOut, "Piece out", j3, "Piece in 3", PressBlue);
            var rs = On("Conveyor Router", 83.75f, -10.75f, Flow, "-y.0.0", StationTeal);
            Convey(j3, "Piece out", rs, "Piece in", StationTeal);
            var station = On("Building Station", 92.25f, -9.25f, Front, "-y.0.0");
            Config(station, ("bayWidth", 6.0), ("bayDepth", 8.0), ("bayHeight", 3.5));
            Corner(rs, "Piece out", station, "Piece in", StationTeal);
            var ao = PortAt(asm.RouterA, "Piece out");
            var si = PortAt(station, "Piece in 2");
            Convey(asm.RouterA, "Piece out", station, "Piece in 2", Assembler, null, Along(new[] { V(ao.X + 1.0f, 0, ao.Z), V(ao.X + 1.0f, 0, -14.6f), V(90.75f, 0, -14.6f), V(90.75f, 0, si.Z) }).Select(n => n with { Y = ao.Y }).ToArray()); // (along the back floor's north edge, up beside the station)
            foreach (var t in _toStation)
                ChuteVia(t, station, -14.35f - 0.15f * _toStation.IndexOf(t));
            ChuteVia(r.Copper, station, -14.05f); // (link stock: a unit of copper a metre of wire)
            var rack = On("Long Piece Rack", 85.25f, -17.25f, Flow, "-y.0.0", StationTeal); // (on the back floor behind the Router: x 85 to 98)
            Convey(rs, "Piece out 2", rack, "Piece in", StationTeal);
            On("Loader Arm", 95.75f, -10.75f, Front, "-y.0.0");
            On("Loader Arm", 95.75f, -4.25f, Front, "-y.0.0");
            Sign(62.75f, Posts2, Wheels, "WHEELS: rubber in, a formed wheel out");
            Sign(80.25f, Posts2, StationTeal, "ROUTER: ordered pieces to the station, the rest to the rack");
            Sign(90.25f, Posts1, StationTeal, "7 BUILDING STATION: F, pick Works Runabout, Start");
            return (station, rs, wheel, rack);
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
                    Link("data", console, m, null, "Run", Control, true, null, Along(pts.ToArray()));
            }
            return console;
        }

        // ---- Helpers --------------------------------------------------------------------------------------------------------------

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
            var post = On("Frame Beam", x, z, Post, "end-", colour, "painted plating");
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
        private VPart Heap(string material, float x, float z, float size)
        {
            var v = On("Storage Volume", x, z, Front, "-y", Stock);
            // (the box from 5 cm over the floor, size square and 2 m high: 24 % of it is the raw stock, under every open side's lip)
            Config(v, ("sizeX", size), ("sizeY", 2.0), ("sizeZ", size), ("atY", 0.35 + 0.5), ("material", material), ("fill", 24));
            return v;
        }

        /// <summary>A Materials Bin (600 units of one material) on the floor at (x, z).</summary>
        private VPart Bin(float x, float z, string paint) => On("Materials Bin", x, z, Front, paint: paint);

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
            floor.Add(V(at.X, 0, z));
            var nodes = Along(floor.ToArray()).ToList();
            nodes.Add(V(at.X, at.Y, z));
            Link("data", _switch!, lamp, null, null, Board, true, null, nodes.ToArray());
        }
    }
}
