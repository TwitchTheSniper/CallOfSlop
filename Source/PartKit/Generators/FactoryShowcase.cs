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
        private const float RootX = 49, RootZ = -7; // the root tile's centre
        private const float Row1 = -4.75f, Row2 = -11.25f; // the main floor's two rows; the front walkway is z 0 to -2, the middle aisle z -7 to -8.5
        private const float Posts1 = -2.25f, Posts2 = -8.75f; // sign posts and consoles of each row, on its aisle's edge

        // Paint by section (look.md): a warm concrete floor, steel-blue walkways, one accent a stage, yellow controls.
        private const string Floor = "#8c8576", Aisle = "#4f6578", Hazard = "#d8a020", Board = "#23272b", Control = "#e0b020";
        private const string Power = "#b5452b", Stock = "#c9a24a", Refining = "#d17a22", Yard = "#9a7b3a";

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
            Tiles(0, 50, 0, 7); // the main floor: x 0 to 100, z 0 to -14
            PowerPlant();
            var r = Refinery();
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

        private void Feed(VPart from, VPart machine) => Link("chute", from, machine, "Chute out", machine.Def.IsRefiner ? "Chute in" : null, Stock);

        /// <summary>A chute from a refiner's output (or a bin) into the next store: a refiner's Chute in, a bin.</summary>
        private void Chute(VPart from, VPart to, params Vector3[] nodes) =>
            Link("chute", from, to, from.Def.IsRefiner ? "Chute out" : null, to.Def.IsRefiner ? "Chute in" : null, Refining, false, nodes);

        /// <summary>A chute laid along the floor: out of <paramref name="from"/>'s port to the lane at z <paramref name="laneZ"/>, along it, and into <paramref name="to"/>.</summary>
        private void ChuteVia(VPart from, VPart to, float laneZ)
        {
            var a = ChutePortAt(from, "Chute out");
            var b = ChutePortAt(to, "Chute in");
            var nodes = Along(new[] { V(a.X, 0, laneZ), V(b.X, 0, laneZ) }).Select(n => n with { Y = FloorY + 0.2f }).ToArray();
            Link("chute", from, to, from.Def.IsRefiner ? "Chute out" : null, to.Def.IsRefiner ? "Chute in" : null, Refining, true, nodes);
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
