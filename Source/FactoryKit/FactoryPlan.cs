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

    public PlanCell? CellOf(FactoryObject o) => Cells.FirstOrDefault(c => c.Obj == o);

    /// <summary>On the floor's socket lattice: the nearest x.25 or x.75.</summary>
    public static float Snap(float v) => MathF.Floor(v * 2f) / 2f + 0.25f;

    /// <summary>A part's footprint on the floor (x, z extents), standing as the builder stands it (machines turned with their back to the back).</summary>
    public static (float W, float D) Footprint(FactoryObject o)
    {
        if (o.Part == "Storage Volume")
            return (o.Size, o.Size);
        var def = ConstructParts.Find(o.Part) ?? throw new FactoryKitException($"no part {o.Part}");
        var s = def.BoundsMax - def.BoundsMin;
        return (s.X, s.Z);
    }

    public static FactoryPlan Make(FactoryProgramme p, FactoryFunction f, FactorySize size, FactoryStyle style, VehicleKit.Dice d)
    {
        var plan = new FactoryPlan { Arrangement = style.Pick(d.Fork("arrangement"), style.Arrangements), Shell = style.Pick(d.Fork("shell"), style.Shells) };
        if (plan.Arrangement != "rows")
            throw new FactoryKitException($"arrangement {plan.Arrangement}: phase 1 lays rows only");
        var dz = d.Fork("zones");
        var margin = (float)dz.Between(style.MarginMin, style.MarginMax);
        // The machine band's depth: the deepest machine with its margin, on whole tiles.
        var band = p.Objects.Where(o => o.Zone is "process" or "intake" && o.Role != "tipping").Select(o => Footprint(o).D).DefaultIfEmpty(2).Max();
        if (f.Name == "extraction")
            band = MathF.Max(band, 3f);
        var bandDepth = MathF.Ceiling((band + 2 * margin) / Tile) * Tile;
        plan.BandBack = -(RoadDepth + AisleDepth + bandDepth);
        plan.Back = plan.BandBack - StripDepth;
        plan.StripBack = plan.Back;
        plan.PowerBehind = f.Name != "extraction" && dz.Chance(0.5); // (an extraction's back edge is its drills')
        var x = 0f;
        var number = 1;

        void Block(char zone, string name, IReadOnlyList<FactoryObject> objs, Func<FactoryObject, int, float, (float X, float Z)> at, Func<FactoryObject, float> width)
        {
            if (objs.Count == 0)
                return;
            // A bank packs its cells at their own widths (a machine and its margin each side) and the block ends on a whole tile.
            var x0 = x;
            for (var k = 0; k < objs.Count; k++)
            {
                var w = width(objs[k]) + 2 * margin;
                var (cx, cz) = at(objs[k], k, x + w * 0.5f);
                plan.Cells.Add(new PlanCell(objs[k], new PlanRect(x, x + w, -(RoadDepth + AisleDepth), plan.BandBack), Snap(cx), Snap(cz), number++));
                x += w;
            }
            x = x0 + MathF.Ceiling((x - x0) / Tile) * Tile;
            plan.Zones.Add((zone, new PlanRect(x0, x, -RoadDepth, plan.Back), name));
        }

        var bandMid = (plan.BandBack - RoadDepth - AisleDepth) * 0.5f;
        if (f.Name == "extraction")
        {
            // Drills on the floor's back edge, their bits out over the ground (the deposit is behind the post); the buffer in front of them.
            var drills = p.Of("drill").ToList();
            Block('I', "DRILLS", drills, (o, k, cx) => (cx, plan.Back + 0.25f), o => MathF.Max(Footprint(o).W, 2.5f));
            var buffer = p.Of("buffer").ToList();
            Block('S', "ORE BUFFER", buffer, (o, k, cx) => (cx, bandMid), o => Footprint(o).W);
        }
        else
        {
            // The intake block (no aisle: the road runs up to it): slots two to a column, the tipping bay's hopper first by the road, the
            // hauler's ore heap straight behind it, the other heaps on in the seed's order.
            var heaps = p.Of("heap").ToList();
            var items = p.Of("tipping").Concat(heaps.Take(1)).Concat(heaps.Skip(1).OrderBy(_ => dz.Next())).ToList();
            var x0 = x;
            var w = MathF.Ceiling((items.Select(h => h.Size).DefaultIfEmpty(2).Max() + 2 * margin) / Tile) * Tile;
            var top = -RoadDepth;
            var rowH = (top - plan.BandBack) * 0.5f;
            for (var k = 0; k < items.Count; k++)
            {
                var cx = x + w * (k / 2 + 0.5f);
                var rect = new PlanRect(cx - w * 0.5f, cx + w * 0.5f, top - rowH * (k % 2), top - rowH * (k % 2 + 1));
                var cz = k == 0 && items[0].Role == "tipping" ? -(RoadDepth + 0.25f + items[0].Size * 0.5f) : rect.Cz;
                if (k % 2 == 1)
                    cx += 0.5f; // (the back slot sits half a metre over: the front one's chute to the back strip passes beside its anchor)
                plan.Cells.Add(new PlanCell(items[k], rect, Snap(cx), Snap(cz), number++));
            }
            x += w * Math.Max(1, (items.Count + 1) / 2);
            plan.Zones.Add(('I', new PlanRect(x0, x, -RoadDepth, plan.Back), "RAW STOCK"));
            // The banks in flow order, each bank's machines side by side, older or not.
            foreach (var bank in p.Of("process").GroupBy(o => o.Bank).OrderBy(g => g.Key))
                Block('P', bank.First().Material.ToUpperInvariant(), bank.ToList(), (o, k, cx) => (cx, bandMid), o => Footprint(o).W);
            Block('S', "REFINED STOCK", p.Of("bin").ToList(), (o, k, cx) => (cx, bandMid), o => Footprint(o).W);
        }
        plan.ConsoleX = Snap(plan.Zones.First(z => z.Zone != 'I').Rect.X0 + 1.0f);
        // The output: the loading hopper by the road at the end of the spine.
        var loading = p.Of("loading").ToList();
        if (loading.Count > 0)
        {
            var x0 = x;
            var w = MathF.Ceiling((loading.Max(l => l.Size) + 2 * margin) / Tile) * Tile;
            for (var k = 0; k < loading.Count; k++)
            {
                var cx = x + w * (k + 0.5f);
                plan.Cells.Add(new PlanCell(loading[k], new PlanRect(cx - w * 0.5f, cx + w * 0.5f, -RoadDepth, -(RoadDepth + AisleDepth + MathF.Ceiling(loading[k].Size / Tile) * Tile)), Snap(cx), Snap(-(RoadDepth + 0.25f + loading[k].Size * 0.5f)), number++));
            }
            x += w * loading.Count;
            plan.Zones.Add(('O', new PlanRect(x0, x, -RoadDepth, plan.Back), "LOADING BAY"));
        }
        // Power: apart and fenced, at the end of the spine or behind the storage (the seed's), never by the control corner.
        var power = p.Of("power").ToList();
        var pw = MathF.Max(6f, MathF.Ceiling((power.Count * 2.5f + 3f) / Tile) * Tile);
        if (plan.PowerBehind && plan.Zones.FirstOrDefault(z => z.Zone == 'S') is { Name: not null } storage && storage.Rect.W >= pw - 0.01f)
        {
            plan.PowerRect = new PlanRect(storage.Rect.X1 - pw, storage.Rect.X1, plan.Back, plan.Back - 6f);
            plan.Back -= 6f;
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
        // The mast: the signature, at the end the power isn't (asymmetry on purpose: rule 9).
        plan.MastX = dz.Chance(0.5) ? 0.75f : Snap(plan.PowerRect.X0 - 1.0f);
        plan.MastZ = plan.StripBack + 0.75f;
        if (plan.Length > size.Length || plan.Depth > size.Depth)
            throw new FactoryKitException($"plan: {FactoryData.N(plan.Length)} x {FactoryData.N(plan.Depth)} m is past the {size.Name} class's {FactoryData.N(size.Length)} x {FactoryData.N(size.Depth)} m");
        var machines = p.Objects.Count(o => o.Role is "process" or "drill");
        if (machines > size.MaxMachines)
            throw new FactoryKitException($"programme: {machines} machines is past the {size.Name} class's {size.MaxMachines}");
        // Lanes: every chute rides the back strip on a line of its own where it overlaps another along the spine (lines 0.15 m apart, the main line
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
        sb.Append($"plan: {Arrangement}, shell {Shell}, {FactoryData.N(Length)} x {FactoryData.N(Depth)} m (a character is a 2 m tile; road '=', aisle '.', back strip '-', zones by letter, cells numbered)\n");
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
