using System.Numerics;
using CallOfSlop.VehicleKit;

namespace CallOfSlop.FactoryKit;

/// <summary>
/// The builder (factory-generator.md 3.5 to 3.7): lays a plan as an ordinary blueprint, every part, link and setting a real step through the game's rules
/// (<see cref="BlueprintScript"/>), so a step the rules refuse fails here with their reason. The floor first (2 m Foundations grown tile by tile), then the
/// cells, the chutes on their lanes, power, the console and its cables, signs, lamps and the mast. Helpers taken from the Factory Showcase
/// (the removed FactoryShowcase.cs: Tiles, the signs, Heap, ChuteVia, Along, Lamp), its look rules kept: one steel for every link by kind, the floor carries the zone.
/// Places are metres: x along the flow (west to east), y up, z towards the road (z 0 is the road's front edge).
/// </summary>
public sealed class FactoryScript : BlueprintScript
{
    public const float FloorY = 0.3f; // the Foundations' tops

    public readonly FactoryBrief Brief;
    public readonly FactoryFunction Function;
    public readonly FactoryStyle Style;
    public readonly FactoryProgramme Programme;
    public readonly FactoryPlan Plan;
    private readonly Dice _paint, _dress;
    private readonly Dictionary<(int I, int J), VPart> _tiles = new();
    private readonly Dictionary<int, VPart> _of = new();
    private readonly List<(VPart Lamp, float X)> _lamps = new();
    private VPart? _switch;
    private bool _shellChair; // (--crew-check --crew-mine --crew-shell: a Shell Control Chair beside the Drill Rack, so a miner may wear a Mining Shell)
    private int _cable; // (cables side by side, never on one line)

    private static readonly Orient Front = new(Vector3.UnitY, Vector3.UnitY, Vector3.UnitZ, Vector3.UnitZ); // own +z to the road
    private static readonly Orient Back = new(Vector3.UnitY, Vector3.UnitY, -Vector3.UnitZ, Vector3.UnitZ); // own -z to the road (a refiner's screen; a drill's bit to the back)
    private static readonly Orient Post = new(Vector3.UnitZ, Vector3.UnitY, Vector3.UnitY, Vector3.UnitZ); // a Frame Beam stood up, its y+ sockets to the road
    private static readonly Orient West = new(Vector3.UnitZ, Vector3.UnitY, Vector3.UnitY, -Vector3.UnitX); // a post whose board faces west: read walking east
    private static readonly Orient Across = new(Vector3.UnitY, Vector3.UnitY, Vector3.UnitX, Vector3.UnitZ); // own +x across the spine

    public static string BlueprintName(FactoryBrief b) => $"{b.Style} {Title(b.Function)} {b.Seed}";

    public static string FileName(FactoryBrief b) => BlueprintName(b).Replace(" ", "") + ".json";

    private static string Title(string function) => function switch { "extraction" => "Extraction Post", "refinery" => "Refinery", _ => char.ToUpperInvariant(function[0]) + function[1..] };

    private FactoryScript(FactoryBrief brief, FactoryFunction f, FactoryStyle style, FactoryProgramme p, FactoryPlan plan) : base(BlueprintName(brief))
    {
        (Brief, Function, Style, Programme, Plan) = (brief, f, style, p, plan);
        var d = Dice.Of(brief.Seed);
        _paint = d.Fork("paint");
        _dress = d.Fork("dress");
        Group = "Factories";
    }

    /// <summary>Where the first part (the root Foundation, part 0) stands on the plan, its origin at the ground: a spawned body's plan origin is part 0's place less this.</summary>
    public Vector3 RootAt { get; private set; }

    /// <summary>The built part of a programme object (its index in the body as the blueprint builds it), or -1.</summary>
    public int IndexOf(FactoryObject o) => _of.TryGetValue(o.Id, out var v) ? v.Index : -1;

    /// <summary>
    /// The floor tiles people and vehicles cross, each with the clear height a link over it keeps (conveyor-inclines.md section 6: 2.4 m
    /// over the aisle, 4.5 m over the vehicle lane): --factory-check finds any chute or conveyor lower than that over one.
    /// </summary>
    public readonly List<(VPart Tile, float Clear)> Walkways = new();

    public const float AisleClear = 2.4f, LaneClear = 4.5f;

    /// <summary>Stages 1 to 4 (the programme and the plan), no parts: what <c>--gen-factory --plan</c> prints, under a second.</summary>
    public static (FactoryProgramme P, FactoryPlan Plan, FactoryFunction F, FactoryStyle S) Planned(FactoryBrief brief)
    {
        var f = FactoryData.Function(brief.Function);
        var style = FactoryStyles.Find(brief.Style);
        var size = FactoryData.SizeOf(brief.Size ?? f.Size);
        var d = Dice.Of(brief.Seed);
        var p = FactoryProgramme.Solve(brief, f, style, d.Fork("programme"));
        var plan = FactoryPlan.Make(p, f, size, style, d.Fork("plan"));
        return (p, plan, f, style);
    }

    public static FactoryScript Build(FactoryBrief brief, bool shellChair = false)
    {
        var (p, plan, f, style) = Planned(brief);
        var s = new FactoryScript(brief, f, style, p, plan) { _shellChair = shellChair };
        s.Lay();
        return s;
    }

    private static Vector3 V(float x, float y, float z) => new(x, y, z);

    private void Lay()
    {
        Description = Function.Name switch
        {
            "extraction" => $"A {Style.Name} extraction post: set it down at the edge of an ore deposit, its drills along the back edge. Each drill rides a rail ({(Plan.Follow == "gantry" ? "a gantry: a cross rail on a rail" : "on an arm")}) and moves itself on to fresh ground as a spot is worked out. Drills fill the ore buffer, the buffer the loading auger over the road; flip LOAD with a hauler under its spout. Generated by the factory kit ({Brief.Key}).",
            _ => $"A {Style.Name} refinery: a tipping grate by the road, raw stock in walled bins, refiners in banks along the aisle, refined stock and a loading auger at the far end, power fenced apart. It runs from the start. Generated by the factory kit ({Brief.Key}).",
        };
        Floor();
        PowerPlant();
        foreach (var c in Plan.Cells)
            _of[c.Obj.Id] = Cell(c);
        for (var k = 0; k < Programme.Edges.Count; k++)
            Edge(Programme.Edges[k], Plan.Lanes[k]);
        Console_();
        DrillRack();
        if (Plan.Shell == "shed")
            foreach (var (zone, r, _) in Plan.Zones.Where(z => z.Zone == 'P'))
                Shed(r);
        Signs();
        Mast();
        Road();
        foreach (var (lamp, x) in _lamps)
            Lamp(lamp, x);
    }

    // ---- Floor --------------------------------------------------------------------------------------------------------------------

    private void Floor()
    {
        var cols = (int)MathF.Ceiling(Plan.Length / FactoryPlan.Tile);
        var rows = (int)MathF.Ceiling(Plan.Depth / FactoryPlan.Tile);
        var (ri, rj) = (cols / 2, rows / 2);
        RootAt = V(1 + 2 * ri, 0, -1 - 2 * rj);
        var root = Root("Foundation", V(1 + 2 * ri, 0.15f, -1 - 2 * rj), Front);
        TilePaint(root, ri, rj);
        _tiles[(ri, rj)] = root;
        Tiles(0, cols, 0, rows);
        Deck();
    }

    /// <summary>
    /// The drill deck and its steps (<see cref="FactoryPlan.HasDeck"/>): Concrete Floor 2x2 m slabs stacked on the tiles, each by its b7_7 corner on the
    /// tile's top33 socket, so a slab stands 0.125 m west and back of its tile (over the back edge, never over a neighbour's top: no shared face but
    /// the zone's one paint). The top is <see cref="DeckTop"/>.
    /// </summary>
    private void Deck()
    {
        if (!Plan.HasDeck)
            return;
        var colour = Style.Floor(_paint.Fork("zoneI"), 'I');
        for (var j = Plan.DeckJ - 1; j <= Plan.DeckJ; j++)
            for (var i = Plan.DeckI0; i < Plan.DeckI1; i++)
            {
                var n = Plan.SlabsAt(i, j);
                if (n == 0 || !_tiles.TryGetValue((i, j), out var under))
                    continue;
                var (cx, cz) = (1f + 2 * i, -1f - 2 * j);
                for (var k = 0; k < n; k++)
                {
                    var slab = Stand("Concrete Floor 2x2 m", V(cx + 0.75f, FloorY + k * FactoryPlan.SlabThick, cz + 0.75f), Front, under, "b7_7");
                    Paint(slab, colour);
                    Finish(slab, "concrete");
                    under = slab;
                }
            }
    }

    public static float DeckTop => FloorY + FactoryPlan.DeckSlabs * FactoryPlan.SlabThick;

    /// <summary>The walking surface's height at (x, z): a slab tile's top (its slabs sit 0.125 m west and back of it), else the floor's.</summary>
    private float Top(float x, float z)
    {
        if (!Plan.HasDeck)
            return FloorY;
        var i = (int)MathF.Floor((x + 0.125f) / FactoryPlan.Tile);
        var j = (int)MathF.Floor((-z - 0.125f) / FactoryPlan.Tile);
        return FloorY + Plan.SlabsAt(i, j) * FactoryPlan.SlabThick;
    }

    /// <summary>
    /// Nodes laid on the floor (<see cref="Along"/>) lifted onto whatever they cross, <paramref name="dy"/> over its top: where the top changes, a node pair
    /// rises (or drops) 0.15 m on the low side of the step's edge, so nothing runs through a slab.
    /// </summary>
    private Vector3[] Lift(Vector3[] nodes, float dy)
    {
        if (!Plan.HasDeck)
            return nodes.Select(n => n with { Y = FloorY + dy }).ToArray();
        var outp = new List<Vector3>();
        for (var k = 0; k < nodes.Length; k++)
        {
            var q = nodes[k];
            var tq = Top(q.X, q.Z);
            if (k > 0)
            {
                var p = nodes[k - 1];
                var d = Vector3.Distance(p with { Y = 0 }, q with { Y = 0 });
                var u = d > 1e-4f ? (q - p) with { Y = 0 } / d : Vector3.Zero;
                var t = Top(p.X, p.Z);
                for (var s = 0.05f; s < d; s += 0.05f)
                {
                    var c = p + u * s;
                    var tc = Top(c.X, c.Z);
                    if (MathF.Abs(tc - t) < 1e-3f)
                        continue;
                    var at = tc > t ? c - u * 0.2f : c + u * 0.15f;
                    outp.Add(V(at.X, t + dy, at.Z));
                    outp.Add(V(at.X, tc + dy, at.Z));
                    t = tc;
                }
            }
            outp.Add(V(q.X, tq + dy, q.Z));
        }
        return outp.ToArray();
    }

    /// <summary>2 m Foundations over tiles [i0, i1) x [j0, j1), each beside one already down (a Foundation snaps to a neighbour's socket).</summary>
    private void Tiles(int i0, int i1, int j0, int j1)
    {
        var left = new List<(int I, int J)>();
        for (var j = j0; j < j1; j++)
            for (var i = i0; i < i1; i++)
                if (!_tiles.ContainsKey((i, j)) && Plan.Floored(1f + 2 * i, -1f - 2 * j))
                    left.Add((i, j));
        while (left.Count > 0)
        {
            var next = left.Where(t => _tiles.ContainsKey((t.I - 1, t.J)) || _tiles.ContainsKey((t.I + 1, t.J)) || _tiles.ContainsKey((t.I, t.J - 1)) || _tiles.ContainsKey((t.I, t.J + 1))).ToList();
            if (next.Count == 0)
                throw new VehicleKitException($"tiles {i0}-{i1} x {j0}-{j1} touch no floor laid before them");
            foreach (var (i, j) in next)
            {
                var t = Put("Foundation", V(1 + 2 * i, 0.15f, -1 - 2 * j), Front);
                _tiles[(i, j)] = t;
                left.Remove((i, j));
                TilePaint(t, i, j);
            }
        }
    }

    private void TilePaint(VPart t, int i, int j)
    {
        var (x, z) = (1f + 2 * i, -1f - 2 * j);
        var zone = ZoneAt(x, z);
        if (z > -FactoryPlan.RoadDepth)
        {
            Paint(t, Style.Colour(_paint.Fork("lane"), "lane"));
            Finish(t, "concrete");
            Walkways.Add((t, LaneClear));
        }
        else if (z > -(FactoryPlan.RoadDepth + FactoryPlan.AisleDepth) && zone is 'P' or 'S')
        {
            Paint(t, Style.Colour(_paint.Fork("aisle"), "aisle"));
            Finish(t, "painted deck plate");
            Walkways.Add((t, AisleClear));
        }
        else
        {
            Paint(t, zone == ' ' ? Style.Colour(_paint.Fork("floor"), "floor") : Style.Floor(_paint.Fork("zone" + zone), zone));
            Finish(t, "concrete");
        }
    }

    private char ZoneAt(float x, float z)
    {
        foreach (var (zone, r, _) in Plan.Zones)
            if (x > r.X0 && x < r.X1 && z < r.Z0 && z > r.Z1)
                return zone;
        return ' ';
    }

    // ---- Cells ----------------------------------------------------------------------------------------------------------------------

    private VPart Cell(PlanCell c)
    {
        var o = c.Obj;
        switch (o.Role)
        {
            case "drill":
                return DrillRun(c);
            case "process":
            {
                var m = On(o.Part, c.X, c.Z, Back, paint: Style.Colour(_paint.Fork("machine" + o.Bank), "machine"));
                if (o.Recipe.Length > 0)
                    Config(m, ("recipe", RecipeOf(m, o.Recipe))); // (set, not Auto: one input alone would otherwise wait for either fellow)
                return m;
            }
            case "heap":
                return WalledStore(o, c.X, c.Z, HeapPaint(o.Material));
            case "tipping":
                return On(o.Part, c.X, c.Z, Front, "bottom"); // (its own steel grid and hazard strips: a grate painted all over reads as a hazard block)
            case "loading":
                return Loading(o, c.X);
            case "buffer" or "bin":
                return WalledStore(o, c.X, c.Z, Style.Colour(_paint.Fork("stock"), "stock"));
            default:
                throw new VehicleKitException($"no cell for role {o.Role}");
        }
    }

    /// <summary>
    /// The loading point (the user, 2026-11-17: an output spigot like a combine's unloading auger): an Unloading Auger 4 m on a Turntable 1x1 Medium on
    /// a 4 m post at <see cref="FactoryPlan.LoadingPostZ"/>, its tube out over the road, its spout over the road's middle about 4.7 m up (over an Ore
    /// Hauler's bed rim). The stock's chute fills its hopper; the LOAD switch on the post runs it (a wire into its Run); the turntable holds it out over
    /// the road and swings it on a signal (a lever wired to it). The auger is the object's part.
    /// </summary>
    private VPart Loading(FactoryObject o, float x)
    {
        var z = FactoryPlan.LoadingPostZ;
        var frame = Style.Colour(_paint, "frame");
        var post = On("Frame Beam", x, z, Post, "end-", frame, "painted plating");
        var top = Onto(post, "end+", "Frame Beam", "end-");
        Paint(top, frame);
        Finish(top, "painted plating");
        var swing = Onto(top, "end+", "Turntable 1x1 Medium", "base");
        Paint(swing, frame);
        var auger = Stand(o.Part, V(x, FloorY + 4.3f, z), Front, swing, "mount");
        Paint(auger, Style.Colour(_paint.Fork("stock"), "stock"));
        var load = Onto(post, "x+1", "Switch", null);
        Paint(load, Style.Colour(_paint, "hazard"));
        Wire(load, auger);
        Sign(post, "y+2", "LOAD");
        return auger;
    }

    /// <summary>
    /// A drill run (the user, 2026-11-17: "extraction must follow the ore"): a 6 m Electric rail along the floor's back row (a 3x3 4 m and a 2 m
    /// section), unwired in Position mode with its Rest at 0, so the drill it carries moves itself on a spot at a time as each spot is worked out
    /// (ConstructDrillFollow). <see cref="FactoryPlan.Follow"/> "arm": a Frame Beam out over the back edge with the drill on its end; "gantry": a 4 m
    /// cross rail out over the ground, the drill on its carriage (a patch row by row). Either way the drill's foot is 1.24 m up, its bit just over
    /// the ground behind the floor. The drill is the object's part.
    /// </summary>
    private VPart DrillRun(PlanCell c)
    {
        var o = c.Obj;
        var frame = Style.Colour(_paint.Fork("rail"), "frame");
        var z = Plan.Back + 0.25f;
        var cx = c.Rect.X0 + 3.125f; // (the 4 m section's middle: its base.1 socket on the floor's x.25 lattice)
        var rail = Stand("Linear Actuator 3x3 4 m", V(cx + 0.125f, FloorY, z), RailX, null, "base.1");
        var rest = Onto(rail, "end+", "Linear Actuator 3x3 2 m", "end-");
        foreach (var r in new[] { rail, rest })
        {
            Paint(r, frame);
            Config(r, ("rest", 0.0));
        }
        var carriage = FloorY + RailRise;
        VPart drill;
        if (Plan.Follow == "gantry")
        {
            // The cross rail by its base socket furthest out (0.625 m along it), so it reaches 2.6 m past the carriage; its stop low at 25 % keeps the
            // drill's bit off the floor: it works 3 m of ground straight back from the edge, then the main rail steps it along.
            var cross = Stand("Linear Actuator 3x3 4 m", V(cx, carriage, z), Outward, rail, BaseAt(ConstructParts.Find("Linear Actuator 3x3 4 m")!, -0.625f));
            Paint(cross, frame);
            Config(cross, ("rest", 0.0), ("stopLow", 25.0));
            drill = Stand(o.Part, V(cx, carriage + RailRise, z - 0.625f), East, cross, "bottom"); // (its bit out to the side, clear of the cross rail under it)
        }
        else
        {
            var arm = Stand("Frame Beam", V(cx, carriage, z), Outward, rail, "y-0");
            Paint(arm, frame);
            Finish(arm, "painted plating");
            drill = Stand(o.Part, V(cx, carriage + 0.5f, z - 1.5f), Back, arm, "bottom");
        }
        Paint(drill, Style.Colour(_paint.Fork("machine"), "machine"));
        return drill;
    }

    /// <summary>
    /// An extraction post's Drill Rack (factory-generator-notes.md "Tool racks and crew miners"): three portable drills for its crew's miners, who
    /// carry them to ore past the drills' reach. It stands in the first drill zone's band at the aisle's edge, a metre in from the zone's west
    /// end, facing the aisle, so a miner works it from the walkway and the drill runs behind stay clear.
    /// </summary>
    private void DrillRack()
    {
        if (Function.Name != "extraction" || Plan.Cells.FirstOrDefault(c => c.Obj.Role == "drill") is not { } first)
            return;
        var x = FactoryPlan.Snap(first.Rect.X0 + 1.0f);
        var rack = On("Drill Rack", x, -(FactoryPlan.RoadDepth + FactoryPlan.AisleDepth) - 0.75f, Front);
        Paint(rack, Style.Colour(_paint.Fork("rack"), "frame"));
        if (_shellChair)
            On("Shell Control Chair", x + 1.5f, -(FactoryPlan.RoadDepth + FactoryPlan.AisleDepth) - 0.75f, Back);
    }

    /// <summary>A 3x3 rail's base to its carriage's top socket (metres).</summary>
    private const float RailRise = 0.47f;

    private static readonly Orient RailX = new(Vector3.UnitY, Vector3.UnitY, Vector3.UnitZ, Vector3.UnitX); // a rail's length (own z) along the spine
    private static readonly Orient East = new(Vector3.UnitY, Vector3.UnitY, Vector3.UnitZ, Vector3.UnitX); // own +z (a drill's bit) east, along the spine
    private static readonly Orient Outward = new(Vector3.UnitY, Vector3.UnitY, Vector3.UnitZ, -Vector3.UnitZ); // own z away from the road: out over the back edge

    /// <summary>The base socket of a rail section at <paramref name="along"/> metres along its own z.</summary>
    private static string BaseAt(ConstructPartDef d, float along) =>
        d.Sockets.Where(k => k.Name.StartsWith("base", StringComparison.Ordinal)).OrderBy(k => MathF.Abs(k.At.Z - along)).First().Name;

    /// <summary>
    /// A store (the user, 2026-11-17: walled cargo volumes, no hand-built walls): a Storage Volume with its Walls on, its box <see cref="FactoryObject.Size"/>
    /// square and <see cref="FactoryObject.Height"/> high centred at (x, z) on the floor, its hopper (the part, with the chute ports) just behind the box.
    /// </summary>
    private VPart WalledStore(FactoryObject o, float x, float z, string paint)
    {
        var side = o.Size;
        var v = On("Storage Volume", x, z - side * 0.5f - 0.5f, Front, "-y", paint); // (on the floor's lattice: the box half a module's side, the hopper 0.3 m off it)
        Config(v, ("sizeX", (double)side), ("sizeY", (double)o.Height), ("sizeZ", (double)side), ("atX", 0.0), ("atY", Math.Round(o.Height * 0.5 - 0.2 + 0.005, 3)), ("atZ", side * 0.5 + 0.5), ("walls", true));
        return v;
    }

    /// <summary>
    /// A shed over a zone's machine band (3.5, the shell <c>shed</c>): Portal Columns 8 m along the road's edge and the band's back, a bay of 4 m apart
    /// from the zone's west end, a Portal Rafter across each pair, a Roof Sheet over each bay. Open sided: the refiners (6.3 m) stand under 8 m eaves.
    /// </summary>
    private void Shed(PlanRect r)
    {
        var zf = -FactoryPlan.RoadDepth - 0.25f; // (over the aisle too: the front row stands at the road's edge, clear of the zone boards and the console)
        var zb = Plan.BandBack + 0.25f;
        var span = zf - zb;
        var frame = Style.Colour(_paint.Fork("shed"), "frame");
        var roof = Style.Colour(_paint.Fork("roof"), "roof");
        var bays = (int)MathF.Floor(r.W / FactoryPlan.Module + 1e-3f) - 1; // (a frame on each module line inside the zone, centred: the same 4 m bays everywhere)
        var x0 = FactoryPlan.Snap(r.X0 + (r.W - 4f * bays) * 0.5f);
        if (bays < 1)
            return;
        var rafters = new List<VPart>();
        for (var k = 0; k <= bays; k++)
        {
            var x = x0 + 4f * k;
            var front = On($"Portal Column 8 m", x, zf, Front, "foot", frame, "painted plating");
            On($"Portal Column 8 m", x, zb, Front, "foot", frame, "painted plating");
            var rafter = Stand($"Portal Rafter {span:0.0} m", V(x, FloorY + 8.002f, zf), new Orient(Vector3.UnitZ, -Vector3.UnitZ, Vector3.UnitY, Vector3.UnitY), front, "seatA");
            Paint(rafter, frame);
            rafters.Add(rafter);
        }
        for (var k = 1; k <= bays; k++)
        {
            var x = x0 + 4f * k;
            var sheet = Stand($"Roof Sheet 4x{span + 0.5f:0} m", V(x, FloorY + 8.002f + 0.35f, zf), new Orient(Vector3.UnitZ, -Vector3.UnitZ, Vector3.UnitY, Vector3.UnitY), rafters[k], "rest");
            Paint(sheet, roof);
        }
    }

    // ---- Chutes ---------------------------------------------------------------------------------------------------------------------

    private void Edge(FactoryEdge e, float lane)
    {
        var (from, to) = (_of[e.From], _of[e.To]);
        var a = ChutePortAt(from, "Chute out");
        var b = ChutePortAt(to, "Chute in");
        var near = Vector2.Distance(new Vector2(a.X, a.Z), new Vector2(b.X, b.Z)) < 5.5f;
        var fromObj = Programme.Objects[e.From];
        if (near && fromObj.Role is "tipping" or "drill")
        {
            Route("chute", from, to, OutPort(from), InPort(to)); // (short: the link's own route)
            return;
        }
        ChuteVia(from, to, lane);
    }

    /// <summary>A chute laid along the floor: out of <paramref name="from"/>'s port to the lane at z <paramref name="laneZ"/>, along it, and into <paramref name="to"/>.</summary>
    private void ChuteVia(VPart from, VPart to, float laneZ)
    {
        var a = ChutePortAt(from, "Chute out");
        var b = ChutePortAt(to, "Chute in");
        var pts = new List<Vector3>();
        if (MathF.Abs(a.Z - laneZ) > 5.5f)
            pts.Add(V(a.X, 0, a.Z + MathF.Sign(laneZ - a.Z)));
        pts.Add(V(a.X, 0, laneZ));
        pts.Add(V(b.X, 0, laneZ));
        if (MathF.Abs(b.Z - laneZ) > 5.5f)
            pts.Add(V(b.X, 0, b.Z - MathF.Sign(b.Z - laneZ)));
        var nodes = Lift(Along(pts.ToArray()), 0.2f);
        try
        {
            Route("chute", from, to, OutPort(from), InPort(to), true, nodes);
        }
        catch (VehicleKitException e)
        {
            throw new VehicleKitException($"{e.Message} ({from.Name} at {Show(a)}, {to.Name} at {Show(b)}, nodes {string.Join(" ", nodes.Select(Show))})");
        }
    }

    private Vector3 ChutePortAt(VPart p, string name)
    {
        ref var cp = ref Live.Parts[p.Index];
        foreach (var np in p.Def.NamedPorts)
            if (np.Type == RouteType.Chute && np.Name == name)
                return FromRest(cp.Pos + Rot24.Apply(cp.Rot, np.At));
        return FromRest(cp.Pos);
    }

    private static string? OutPort(VPart p) => p.Def.NamedPorts.Any(n => n.Type == RouteType.Chute && n.Name == "Chute out") ? "Chute out" : null;

    private static string? InPort(VPart p) => p.Def.NamedPorts.Any(n => n.Type == RouteType.Chute && n.Name == "Chute in") ? "Chute in" : null;

    // ---- Power ---------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The plant (3.7): gensets on fuel tanks, a battery, the main switch and its charge gauge on a post at the block's open side, sandbags round the
    /// rest (the style's fence). A wire into the only battery is the mains (ConstructPower.Mains): every machine on the body draws from it.
    /// </summary>
    private void PowerPlant()
    {
        var r = Plan.PowerRect;
        var colour = Style.Colour(_paint.Fork("power"), "power");
        var front = r.Z0 - 1.25f;
        var gx = FactoryPlan.Snap(r.X0 + 1.5f);
        var gensets = new List<VPart>();
        for (var k = 0; k < Programme.Gensets; k++)
        {
            var g = On(Programme.Genset, gx + 2.5f * k, FactoryPlan.Snap(r.Cz), Front, "-y", colour, "painted plating");
            var tank = Onto(g, "-z", "Fuel Tank", "+z");
            Paint(tank, colour);
            gensets.Add(g);
            _of[Programme.Of("power").ElementAt(k).Id] = g;
        }
        var battery = On("Battery", FactoryPlan.Snap(r.X1 - 1.25f), FactoryPlan.Snap(r.Cz), Front, "-y", colour);
        var post = Sign(FactoryPlan.Snap(r.X0 + 0.75f), FactoryPlan.Snap(front), "MAIN SWITCH", colour);
        _switch = Onto(post, "y+2", "Switch", "-z");
        Config(_switch, ("startOn", true));
        Paint(_switch, Style.Colour(_paint, "control"));
        Paint(Onto(post, "y+1", "Charge Gauge", "back"), Style.Colour(_paint, "board"));
        foreach (var g in gensets)
            Wire(_switch, g);
        Wire(_switch, battery);
        // Sandbags along the block's back and far side (its open side faces the aisle or the storage it stands behind).
        var bag = ConstructParts.Find(Style.Fence) != null ? Style.Fence : null;
        if (bag == null)
            return;
        for (var x = r.X0 + 0.25f; x + 2 <= r.X1 + 0.01f; x += 2)
            Stand(bag, V(x, FloorY, r.Z1 + 0.25f), Front, null, "b0_0");
        for (var z = r.Z1 + 2.25f; z + 0.5f < r.Z0; z += 2)
            Stand(bag, V(r.X1 - 0.25f, FloorY, z), Across, null, "b0_0");
    }

    // ---- Control -------------------------------------------------------------------------------------------------------------------

    /// <summary>The start console by the aisle's west end (the main door): its wires into every machine's Run are the line. It starts on.</summary>
    private void Console_()
    {
        var z = -(FactoryPlan.RoadDepth + FactoryPlan.AisleDepth) + 0.25f; // (the aisle's back edge)
        var console = On("Line Console", Plan.ConsoleX, z, Front, paint: Style.Colour(_paint, "control"));
        Config(console, ("startOn", true));
        _of[Programme.Of("control").First().Id] = console;
        foreach (var o in Programme.Objects.Where(o => o.Role is "process" or "drill"))
        {
            var m = _of[o.Id];
            var at = FromRest(Live.Parts[m.Index].Pos);
            var lane = z - 0.2f - 0.04f * _cable++;
            var side = FactoryPlan.Footprint(o).W * 0.5f + 0.4f; // (down the machine's west side to it, never over it)
            var pts = new List<Vector3> { V(Plan.ConsoleX, 0, lane), V(at.X - side, 0, lane) };
            if (MathF.Abs(at.Z - lane) > 6f)
                pts.Add(V(at.X - side, 0, at.Z));
            Route("data", console, m, null, "Run", true, Lift(Along(pts.ToArray()), 0.03f));
        }
    }

    // ---- Signs and dressing ----------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Signs only where they inform (the user's grading, 2026-11-17: fewer, smaller, never filler): the tipping grate's TIP HERE by the road, with a lamp
    /// on its post; the loading post's LOAD (Loading) and the plant's MAIN SWITCH (PowerPlant) are on the posts that carry their switches.
    /// </summary>
    private void Signs()
    {
        foreach (var c in Plan.Cells.Where(c => c.Obj.Role == "tipping"))
        {
            var post = Sign(FactoryPlan.Snap(c.Rect.X0 + 0.25f), -(FactoryPlan.RoadDepth + 0.75f), "TIP HERE", Style.Colour(_paint, "hazard"));
            var lamp = Onto(post, "end+", "Lamp", "-y");
            Config(lamp, ("colour", Style.LampColour), ("brightness", 160));
            _lamps.Add((lamp, c.Rect.X0));
        }
    }

    /// <summary>The signature (buildings.md rule 3): Militia's radio mast, a flagpole base and two masts with a lamp on top, the tallest thing on the site.</summary>
    private void Mast()
    {
        if (Style.Signature != "mast")
            return;
        var b = On("Flagpole Base", Plan.MastX, Plan.MastZ, Front, "b00", Style.Colour(_paint, "frame"));
        var m1 = Onto(b, "mast", "Flagpole Mast 3.75 m", "b");
        var m2 = Onto(m1, "t", "Flagpole Mast 3.75 m", "b");
        Paint(m1, Style.Colour(_paint, "frame"));
        Paint(m2, Style.Colour(_paint, "accent"));
        var lamp = Onto(m2, "t", "Lamp", "-y");
        Config(lamp, ("colour", "#ff4030"), ("brightness", 200));
        _lamps.Add((lamp, Plan.MastX));
    }

    /// <summary>The road: a lane marking and a way arrow every 6 m along its middle, the flow's way.</summary>
    private void Road()
    {
        for (var x = 1.75f; x + 4f < Plan.Length; x += 2 * FactoryPlan.Module) // (a marking and an arrow a module apart, on the module's middles)
        {
            On("Lane Marking", x, -1.75f, Front, "bottom", null, "hazard paint");
            On("Floor Arrow", x + FactoryPlan.Module, -1.75f, new Orient(Vector3.UnitY, Vector3.UnitY, Vector3.UnitX, Vector3.UnitX), "bottom", Style.Colour(_paint, "hazard"), "hazard paint");
        }
    }

    // ---- Helpers (from the Factory Showcase) ------------------------------------------------------------------------------------------

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

    private static string HeapPaint(string material) => material switch
    {
        "iron ore" => "#8a4a32", "copper ore" => "#3f8f7a", "stone" => "#9a9a94", "wood" => "#6b4a26", _ => "#d8c48a",
    };

    /// <summary>A refiner's Recipe setting for the recipe that makes <paramref name="output"/>: its option's number (0 is Auto).</summary>
    private static int RecipeOf(VPart refiner, string output)
    {
        var opts = refiner.Def.Config[refiner.Def.CfgSlot[(int)CfgKey.Recipe]].Options;
        var k = Array.FindIndex(opts, o => o.StartsWith(output + " ", StringComparison.Ordinal) || o.EndsWith(" to " + output, StringComparison.Ordinal));
        return k > 0 ? k : throw new VehicleKitException($"a {refiner.Name} has no recipe making {output} ({string.Join(", ", opts)})");
    }

    private const string ChuteSteel = "#555b62", CableDark = "#34383d", PostSteel = "#6a6f66";

    private void Route(string type, VPart from, VPart to, string? fromPort, string? toPort, bool laid = false, params Vector3[] nodes) =>
        base.Link(type, from, to, fromPort, toPort, type is "chute" or "pipe" ? ChuteSteel : CableDark, laid, null, nodes);

    /// <summary>A small sign on a post at (x, z): a Frame Beam stood up, a Text Sign facing the road.</summary>
    private VPart Sign(float x, float z, string text, string colour)
    {
        var post = On("Frame Beam", x, z, Post, "end-", PostSteel, "painted plating");
        var sign = Onto(post, "y+3", "Text Sign", "back");
        Paint(sign, colour);
        Text(sign, Fit(text));
        return post;
    }

    /// <summary>A small sign on a post already standing (its socket <paramref name="socket"/>, facing the road), in the board's colour.</summary>
    private void Sign(VPart post, string socket, string text)
    {
        var sign = Onto(post, socket, "Text Sign", "back");
        Paint(sign, Style.Colour(_paint, "board"));
        Text(sign, Fit(text));
    }

    /// <summary>A lamp wired from the main switch: a cable along the back strip's far edge, then up its post's back.</summary>
    private void Lamp(VPart lamp, float x)
    {
        var at = FromRest(Live.Parts[lamp.Index].Pos);
        var sw = FromRest(Live.Parts[_switch!.Index].Pos);
        var lane = Plan.Back + 0.15f + 0.04f * (_cable++ % 8);
        var rise = V(at.X + 0.3f, 0, at.Z - 0.3f);
        var floor = new List<Vector3> { V(sw.X + 0.3f, 0, sw.Z - 0.3f), V(sw.X + 0.3f, 0, lane), V(rise.X, 0, lane), rise };
        var nodes = Lift(Along(floor.ToArray()), 0.03f).ToList();
        for (var y = FloorY + 7f; y < at.Y - 0.5f; y += 7f)
            nodes.Add(V(rise.X, y, rise.Z)); // (up a mast: a node every 7 m, a segment reaches 8)
        nodes.Add(V(rise.X, at.Y, rise.Z));
        Route("data", _switch!, lamp, null, null, true, nodes.ToArray());
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
                if (d < 0.01f)
                    continue;
                for (var n = 1; n * 7.5f < d; n++)
                    nodes.Add(Vector3.Lerp(q, p, n * 7.5f / d));
            }
            nodes.Add(p);
        }
        return nodes.ToArray();
    }

    /// <summary>A sign's text, refused unless it fits its board whole: 3 lines of 10 characters, wrapped as the board wraps.</summary>
    private static string Fit(string text, int cols = 10, int rows = 3)
    {
        int lines = 0, column = 0;
        foreach (var word in text.Split(' '))
        {
            if (word.Length > cols)
                throw new VehicleKitException($"sign \"{text}\": \"{word}\" is longer than a line ({cols})");
            if (column > 0 && column + 1 + word.Length > cols)
                (lines, column) = (lines + 1, 0);
            column += (column > 0 ? 1 : 0) + word.Length;
        }
        if (lines + 1 > rows)
            throw new VehicleKitException($"sign \"{text}\" takes {lines + 1} lines of {cols}: its board shows {rows}");
        return text;
    }
}
