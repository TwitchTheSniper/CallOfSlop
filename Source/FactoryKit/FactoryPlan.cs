using System.Text;

namespace CallOfSlop.FactoryKit;

/// <summary>A rectangle on the floor plan, metres: x along the spine (the flow, west to east), z across it (0 is the road's front edge, the back is negative).</summary>
public readonly record struct PlanRect(float X0, float X1, float Z0, float Z1)
{
    public float W => X1 - X0;
    public float D => Z0 - Z1; // (Z0 is the front, nearer the road)
    public float Cx => (X0 + X1) * 0.5f;
    public float Cz => (Z0 + Z1) * 0.5f;
}

/// <summary>A cell (3.2): a machine or store and the margin round it; the unit the plan moves.</summary>
public sealed record PlanCell(FactoryObject Obj, PlanRect Rect, float X, float Z, int Number);

/// <summary>
/// The plan (factory-generator.md 3.3, 3.4): zones as rectangles along a spine in flow order, cells in each, the bands across (the road, the aisle, the
/// machine band and the back strip), and a lane in the back strip for every chute. Phase 1's arrangement is <c>rows</c>: one straight spine, cells on the
/// back side of the aisle, the road in front. Everything lands on the floor's 2 m tiles and its 0.5 m socket lattice (a centre on .25 or .75).
/// </summary>
public sealed class FactoryPlan
{
    public static readonly Dictionary<string, char> ZoneLetters = new()
    {
        ["intake"] = 'I', ["process"] = 'P', ["storage"] = 'S', ["output"] = 'O', ["power"] = 'W', ["control"] = 'C', ["defence"] = 'D', ["docks"] = 'K',
    };

    public const float Tile = 2f, RoadDepth = 4f, AisleDepth = 2f, StripDepth = 2f;

    public string Arrangement = "rows", Shell = "yard";
    public float Length, Depth;

    /// <summary>The bands across (z): road z 0 to -4, aisle -4 to -6, the machine band to <see cref="BandBack"/>, the back strip behind it.</summary>
    public float BandBack, Back;

    public readonly List<PlanCell> Cells = new();
    public readonly List<(char Zone, PlanRect Rect, string Name)> Zones = new();

    /// <summary>Each chute's lane in the back strip (z), by edge index; routes that stay in a block have none (NaN).</summary>
    public readonly List<float> Lanes = new();

    /// <summary>Where the console, the mast and the power block's fence stand.</summary>
    public float ConsoleX, MastX, MastZ;

    public PlanRect PowerRect;

    /// <summary>The back strip's back edge: past it only a power block behind the storage has floor (an extraction's drills stand on this edge).</summary>
    public float StripBack;

    /// <summary>Whether the tile centred at (x, z) has floor.</summary>
    public bool Floored(float x, float z) => z > StripBack || PowerBehind && x > PowerRect.X0 && x < PowerRect.X1;
    public bool PowerBehind;

    /// <summary>
    /// An extraction's drill deck (Open 1): the back row's tiles under the drills carry <see cref="DeckSlabs"/> concrete slabs (0.25 m each, the top at 1.30 m) so a
    /// Drill Module's tower, 1.2 m under its foot, hangs to the ground past the back edge while the floor stays on it; three step tiles in the row in front climb to
    /// it at one end (the seed's) (0.25 m rises, a person walks them). Tiles are columns <see cref="DeckI0"/> to <see cref="DeckI1"/> (exclusive), row <see cref="DeckJ"/>.
    /// </summary>
    public bool HasDeck, DeckStepsWest;
    public int DeckI0, DeckI1, DeckJ;
    public const int DeckSlabs = 4;
    public const float SlabThick = 0.25f;

    /// <summary>How many slabs tile (i, j) carries: the deck's four, a step's one to three, else none.</summary>
    public int SlabsAt(int i, int j)
    {
        if (!HasDeck || i < DeckI0 || i >= DeckI1)
            return 0;
        if (j == DeckJ)
            return DeckSlabs;
        if (j != DeckJ - 1)
            return 0;
        if (DeckStepsWest)
            return i < DeckI0 + 3 ? DeckI0 + 3 - i : 0;
        return i >= DeckI1 - 3 ? i - (DeckI1 - 3) + 1 : 0;
    }

    public PlanCell? CellOf(FactoryObject o) => Cells.FirstOrDefault(c => c.Obj == o);

    /// <summary>On the floor's socket lattice: the nearest x.25 or x.75.</summary>
    public static float Snap(float v) => MathF.Floor(v * 2f) / 2f + 0.25f;

    /// <summary>On the drill deck's top sockets (a slab's corners: x = 2k or 2k + 1.75, FactoryScript.Deck), the nearest.</summary>
    public static float DeckX(float v)
    {
        var a = MathF.Round(v / Tile) * Tile;
        var b = MathF.Floor(v / Tile) * Tile + 1.75f;
        return MathF.Abs(a - v) <= MathF.Abs(b - v) ? a : b;
    }

    /// <summary>A part's footprint on the floor (x, z extents), standing as the builder stands it (machines turned with their back to the back).</summary>
    public static (float W, float D) Footprint(FactoryObject o)
    {
        if (o.Part == "Storage Volume" || o.Part.Contains("Bunker") || o.Part.Contains("Grate") || o.Role == "loading")
            return (o.Size, o.Size);
        var def = ConstructParts.Find(o.Part) ?? throw new FactoryKitException($"no part {o.Part}");
        if (o.Square > 0) // (a squares Core: its cell, the n x n square of blocks on its side and a Casting Bed past it; n deep: FactoryScript.Squares)
            return ((o.Square + 2) * def.BlockCell, o.Square * def.BlockCell);
        var s = def.BoundsMax - def.BoundsMin;
        return (s.X, s.Z);
    }

    // ---- The spacing system (the user's grading, 2026-11-17: "one consistent spacing system, so a building reads as planned") ------------
    //
    // Everything stands on a 4 m module (two floor tiles). Across the spine the bands are fixed: the vehicle lane (the road, 4 m), the walkway
    // (the aisle, 2 m), the machine band (whole modules), the service strip behind it (2 m, the chutes' lanes). Along the spine every cell is a
    // whole number of modules: its object's footprint plus a clearance by purpose each side (a machine's service room, a store's), the object
    // centred in it. Zones are runs of cells, so they start and end on the module too; nothing between them but their cells. The seed changes
    // what stands where, never a gap.

    public const float Module = 4f;
    public const float Service = 1f; // clear round a machine (buildings.md "Laying out a building": 1 m before its screen)
    public const float StoreClear = 0.5f; // clear round a store or a grate (walked past, not worked at)

    /// <summary>The clearance an object keeps each side, by what it is for.</summary>
    public static float Clearance(FactoryObject o) => o.Role is "process" or "drill" ? Service : StoreClear;

    /// <summary>A cell's width along the spine: the footprint and its clearance, up to whole modules.</summary>
    public static float CellWidth(FactoryObject o) => MathF.Ceiling((Footprint(o).W + 2 * Clearance(o)) / Module - 1e-4f) * Module;

    /// <summary>An extraction's drill run (FactoryScript.DrillRun): the rail along the back edge is 6 m (a 4 m and a 2 m section), its cell two modules.</summary>
    public const float RunLength = 6f, RunCell = 2 * Module;

    /// <summary>How an extraction post moves its drills over the ground (FactoryScript.DrillRun): an arm on a rail, or a gantry (a cross rail on the rail).</summary>
    public string Follow = "arm";

    public static FactoryPlan Make(FactoryProgramme p, FactoryFunction f, FactorySize size, FactoryStyle style, VehicleKit.Dice d)
    {
        var plan = new FactoryPlan { Arrangement = style.Pick(d.Fork("arrangement"), style.Arrangements), Shell = style.Pick(d.Fork("shell"), style.Shells) };
        if (plan.Arrangement != "rows")
            throw new FactoryKitException($"arrangement {plan.Arrangement}: phase 1 lays rows only");
        var dz = d.Fork("zones");
        // The machine band: the deepest object in it and its clearance, on whole modules.
        var band = p.Objects.Where(o => o.Zone is "process" or "intake" or "storage" && o.Role is not ("tipping" or "drill")).Select(o => Footprint(o).D + 2 * Clearance(o)).DefaultIfEmpty(Module).Max();
        var bandDepth = MathF.Ceiling(band / Module - 1e-4f) * Module;
        plan.BandBack = -(RoadDepth + AisleDepth + bandDepth);
        plan.Back = plan.BandBack - StripDepth;
        plan.StripBack = plan.Back;
        plan.PowerBehind = f.Name != "extraction" && dz.Chance(0.5); // (an extraction's back edge is its drills')
        if (f.Name == "extraction")
            plan.Follow = style.Pick(d.Fork("follow"), style.Follow);
        var x = 0f;
        var number = 1;
        var bandMid = (plan.BandBack - RoadDepth - AisleDepth) * 0.5f;

        void Block(char zone, string name, IReadOnlyList<FactoryObject> objs, Func<FactoryObject, float, float> zOf, Func<FactoryObject, float>? width = null)
        {
            if (objs.Count == 0)
                return;
            var x0 = x;
            foreach (var o in objs)
            {
                var w = width?.Invoke(o) ?? CellWidth(o);
                var cx = x + w * 0.5f;
                plan.Cells.Add(new PlanCell(o, new PlanRect(x, x + w, -(RoadDepth + AisleDepth), plan.BandBack), Snap(cx), zOf(o, cx), number++));
                x += w;
            }
            plan.Zones.Add((zone, new PlanRect(x0, x, -RoadDepth, plan.Back), name));
        }

        var power = p.Of("power").ToList();
        var pw = MathF.Max(2 * Module, MathF.Ceiling((power.Count * 2.5f + 3f) / Module) * Module);
        var powerFirst = f.Name == "extraction" && dz.Chance(0.4); // (a post's plant at the west end, before the drills, or at the east end: the seed's)
        if (powerFirst)
        {
            plan.PowerRect = new PlanRect(x, x + pw, -(RoadDepth + AisleDepth), plan.Back);
            x += pw;
        }
        if (f.Name == "extraction")
        {
            // A drill run a cell (two modules) along the back edge: its rail on the floor's last row, the drill carried out over the ground behind.
            Block('I', "DRILLS", p.Of("drill").ToList(), (o, cx) => plan.Back + 0.25f, o => RunCell);
            Block('S', "ORE BUFFER", p.Of("buffer").ToList(), (o, cx) => Snap(bandMid));
        }
        else
        {
            // The intake: the tipping grate flush with the road's back edge (no walkway: the hauler tips onto it), then the raw stores in the band.
            Block('I', "TIPPING", p.Of("tipping").ToList(), (o, cx) => -(RoadDepth + 0.25f + o.Size * 0.5f));
            var heaps = p.Of("heap").ToList();
            Block('I', "RAW STOCK", heaps.Take(1).Concat(heaps.Skip(1).OrderBy(_ => dz.Next())).ToList(), (o, cx) => Snap(bandMid));
            foreach (var bank in p.Of("process").GroupBy(o => o.Bank).OrderBy(g => g.Key))
                Block('P', bank.First().Material.ToUpperInvariant(), bank.ToList(), (o, cx) => Snap(bandMid));
            Block('S', "REFINED STOCK", p.Of("bin").ToList(), (o, cx) => Snap(bandMid));
        }
        plan.ConsoleX = Snap(plan.Zones.First(z => z.Zone is not ('I' or 'W')).Rect.X0 + 1.0f);
        // The output: the loading auger's post, a module's cell at the end of the spine, its spout over the road's middle.
        Block('O', "LOADING", p.Of("loading").ToList(), (o, cx) => LoadingPostZ, _ => Module);
        // Power: apart and fenced, at the end of the spine or behind the storage (the seed's), never by the control corner.
        if (powerFirst)
        {
        }
        else if (plan.PowerBehind && plan.Zones.FirstOrDefault(z => z.Zone == 'S') is { Name: not null } storage && storage.Rect.W >= pw - 0.01f)
        {
            plan.PowerRect = new PlanRect(storage.Rect.X1 - pw, storage.Rect.X1, plan.Back, plan.Back - 2 * Module);
            plan.Back -= 2 * Module;
        }
        else
        {
            plan.PowerBehind = false;
            plan.PowerRect = new PlanRect(x, x + pw, -(RoadDepth + AisleDepth), plan.Back);
            x += pw;
        }
        plan.Zones.Add(('W', plan.PowerRect, "POWER"));
        plan.Length = x;
        plan.Depth = -plan.Back;
        // The mast: the signature, at the end the power isn't (asymmetry on purpose: rule 9), in the service strip.
        plan.MastX = powerFirst ? Snap(plan.Length - 1.25f) : dz.Chance(0.5) ? 0.75f : Snap(plan.PowerRect.X0 - 1.0f);
        plan.MastZ = plan.BandBack - 0.75f;
        if (f.Name == "extraction" && plan.Cells.Any(c => c.Obj.Role == "drill" && plan.MastX > c.Rect.X0 - 0.5f && plan.MastX < c.Rect.X1 + 0.5f))
            plan.MastZ = plan.BandBack + 0.75f; // (off the drill runs' row)
        if (plan.Length > size.Length || plan.Depth > size.Depth)
            throw new FactoryKitException($"plan: {FactoryData.N(plan.Length)} x {FactoryData.N(plan.Depth)} m is past the {size.Name} class's {FactoryData.N(size.Length)} x {FactoryData.N(size.Depth)} m");
        var machines = p.Objects.Count(o => o.Role is "process" or "drill");
        if (machines > size.MaxMachines)
            throw new FactoryKitException($"programme: {machines} machines is past the {size.Name} class's {size.MaxMachines}");
        // Lanes: every chute rides the service strip on a line of its own where it overlaps another along the spine (lines 0.15 m apart, the main line
        // nearest the machines); chutes whose spans don't overlap share a line (interval colouring, spans from the cells' centres, 3 m either side).
        var spans = new List<(float X0, float X1)>[11];
        for (var k = 0; k < spans.Length; k++)
            spans[k] = new();
        foreach (var e in p.Edges.Select((e, i) => (e, i)).OrderBy(t => t.e.Main ? 0 : 1).ThenBy(t => t.i))
        {
            while (plan.Lanes.Count <= e.i)
                plan.Lanes.Add(float.NaN);
            var (a, b2) = (plan.Cells.First(c => c.Obj.Id == e.e.From).X, plan.Cells.First(c => c.Obj.Id == e.e.To).X);
            var span = (MathF.Min(a, b2) - 3f, MathF.Max(a, b2) + 3f);
            var line = Array.FindIndex(spans, l => l.All(o => o.X1 < span.Item1 || o.X0 > span.Item2));
            if (line < 0)
                throw new FactoryKitException($"plan: the chutes don't fit the back strip's {spans.Length} lines");
            spans[line].Add(span);
            plan.Lanes[e.i] = plan.BandBack - 0.4f - 0.15f * line;
        }
        return plan;
    }

    /// <summary>The loading auger's post (FactoryScript.Loading): 3.85 m behind its spout, which hangs over the road's middle (z -2).</summary>
    public const float LoadingPostZ = -5.75f, SpoutZ = -1.9f;

    /// <summary>The plan as ASCII, a character a 2 m tile (3.4): zones as letters, cells as numbers, the aisle '.', the road '=', the back strip '-'.</summary>
    public string Ascii()
    {
        var cols = (int)MathF.Ceiling(Length / Tile);
        var rows = (int)MathF.Ceiling(Depth / Tile);
        var g = new char[rows, cols];
        for (var j = 0; j < rows; j++)
            for (var i = 0; i < cols; i++)
            {
                var (x, z) = (i * Tile + 1, -j * Tile - 1);
                g[j, i] = z > -RoadDepth ? '=' : ' ';
                foreach (var (zone, r, _) in Zones)
                    if (x > r.X0 && x < r.X1 && z < r.Z0 && z > r.Z1)
                        g[j, i] = z > -(RoadDepth + AisleDepth) && zone is 'P' or 'S' ? '.' : z < BandBack && z > BandBack - StripDepth && zone != 'W' ? '-' : char.ToLowerInvariant(zone);
                if (SlabsAt(i, j) > 0)
                    g[j, i] = j == DeckJ ? '#' : "_~^"[SlabsAt(i, j) - 1];
            }
        foreach (var c in Cells)
        {
            var i = (int)(c.X / Tile);
            var j = (int)(-c.Z / Tile);
            if (j >= 0 && j < rows && i >= 0 && i < cols)
            {
                var s = c.Number.ToString();
                for (var k = 0; k < s.Length && i + k < cols; k++)
                    g[j, i + k] = s[k];
            }
        }
        var sb = new StringBuilder();
        sb.Append($"plan: {Arrangement}, shell {Shell}, {FactoryData.N(Length)} x {FactoryData.N(Depth)} m (a character is a 2 m tile; road '=', aisle '.', back strip '-', drill deck '#', its steps '_~^', zones by letter, cells numbered)\n");
        for (var j = 0; j < rows; j++)
        {
            sb.Append("  ");
            for (var i = 0; i < cols; i++)
                sb.Append(g[j, i]);
            sb.Append('\n');
        }
        foreach (var (zone, r, name) in Zones)
            sb.Append($"  {zone} {name}: x {FactoryData.N(r.X0)} to {FactoryData.N(r.X1)}\n");
        foreach (var c in Cells)
            sb.Append($"  {c.Number,2} {c.Obj.Part}{(c.Obj.Material.Length > 0 ? $" ({c.Obj.Material})" : "")} at {FactoryData.N(c.X)}, {FactoryData.N(c.Z)}\n");
        return sb.ToString();
    }
}
