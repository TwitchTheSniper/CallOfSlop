using System.Text;

namespace CallOfSlop.FactoryKit;

/// <summary>One object the building needs (factory-generator.md 3.1): a role, a part, what it handles and its rate (units a second), its bank and zone.</summary>
public sealed class FactoryObject
{
    public int Id;
    public string Role = "", Part = "", Zone = "process";
    public string Material = ""; // (a heap's, a bin's, a drill's: what it holds; a machine's: what it makes)
    public string Recipe = ""; // (a mixed machine's recipe, by its output)
    public double Rate; // units a second through it
    public int Bank = -1; // (machines of one recipe stand together facing one feed)
    public float Size; // (a Storage Volume's box: metres square)
    public override string ToString() => $"{Id,2} {Zone,-8} {Role,-8} {Part}{(Material.Length > 0 ? $" ({Material})" : "")} {FactoryData.N(Rate * 3600)}/h";
}

/// <summary>A material flow between two objects (3.2): a carrier and a rate; main-line flows are spine traffic.</summary>
public sealed record FactoryEdge(int From, int To, string Material, string Carrier, double Rate, bool Main);

/// <summary>
/// The programme solver (3.1): target rates to machine counts with the game's own tables, never its own numbers. For each wanted output it walks
/// Constructs/Refining.json back to raw input, picks each step's machine at the brief's tier (the style may keep an older machine in a bank), and counts
/// machines as ceil(rate / machine rate * (1 + spare)). Power is the machines' draw plus the margin, met by gensets.
/// </summary>
public sealed class FactoryProgramme
{
    public readonly List<FactoryObject> Objects = new();
    public readonly List<FactoryEdge> Edges = new();
    public double PowerKw;
    public int Gensets;
    public string Genset = "Small Genset";
    public double Spare;
    public int Tier;

    public IEnumerable<FactoryObject> Of(string role) => Objects.Where(o => o.Role == role);

    private FactoryObject Add(string role, string part, string zone, string material = "", double rate = 0, int bank = -1)
    {
        var o = new FactoryObject { Id = Objects.Count, Role = role, Part = part, Zone = zone, Material = material, Rate = rate, Bank = bank };
        Objects.Add(o);
        return o;
    }

    /// <summary>Raw materials: what the hauler, the drills or the heaps bring, never refined here.</summary>
    public static bool Raw(string material) => material.EndsWith(" ore", StringComparison.Ordinal) || material is "stone" or "sand" or "wood" or "dirt";

    public static FactoryProgramme Solve(FactoryBrief brief, FactoryFunction f, FactoryStyle style, VehicleKit.Dice d)
    {
        var p = new FactoryProgramme { Tier = brief.Tier > 0 ? brief.Tier : style.Tier };
        p.Spare = brief.Spare ?? Math.Round(d.Between(style.SpareMin, style.SpareMax) * 20) / 20;
        var target = brief.Target ?? f.Target;
        switch (f.Name)
        {
            case "extraction":
                p.Extraction(target, f);
                break;
            case "refinery":
                p.Refinery(target, f, style, d);
                break;
            default:
                throw new FactoryKitException($"function {f.Name}: phase 1 solves extraction and refinery only");
        }
        // Power: every machine's draw plus the margin, in gensets of the style's plant.
        var draw = p.Objects.Sum(o => (ConstructParts.Find(o.Part)?.PowerDraw ?? 0)) / 1000.0;
        p.PowerKw = draw * (1 + f.PowerMargin);
        var plant = ConstructParts.Find(p.Genset) ?? throw new FactoryKitException($"no {p.Genset}");
        p.Gensets = Math.Max(1, (int)Math.Ceiling(p.PowerKw / Math.Max(1, plant.PlantRate)));
        for (var k = 0; k < p.Gensets; k++)
            p.Add("power", p.Genset, "power");
        p.Add("control", "Line Console", "control");
        return p;
    }

    private void Extraction(Dictionary<string, double> target, FactoryFunction f)
    {
        var drill = MachinePart(def => def.IsGatherer && !def.GatherSaw, Tier, "drill");
        var (ore, perHour) = target.First();
        var rate = perHour / 3600.0;
        var n = Math.Max(1, (int)Math.Ceiling(rate / drill.GatherRate * (1 + Spare)));
        var drills = Enumerable.Range(0, n).Select(_ => Add("drill", drill.Name, "intake", ore, rate / n, 0)).ToList();
        var buffer = Add("buffer", "Storage Volume", "storage", ore, rate);
        buffer.Size = 2.5f;
        var load = Add("loading", "Storage Volume", "output", ore, rate);
        load.Size = 2.0f;
        foreach (var dr in drills)
            Edges.Add(new FactoryEdge(dr.Id, buffer.Id, ore, "chute", dr.Rate, true));
        Edges.Add(new FactoryEdge(buffer.Id, load.Id, ore, "chute", rate, true));
    }

    private void Refinery(Dictionary<string, double> target, FactoryFunction f, FactoryStyle style, VehicleKit.Dice d)
    {
        var table = RefiningTable.Current;
        // Walk each wanted output back to raw: (material, units a second) needed, in the order they are made (raw last).
        var banks = new List<(string Output, RefineRecipe Recipe, ConstructPartDef Part, double Rate)>();
        var raw = new Dictionary<string, double>();
        void Need(string material, double rate, int depth)
        {
            if (depth > 6)
                throw new FactoryKitException($"the chain to {material} is deeper than 6 steps");
            if (Raw(material))
            {
                raw[material] = raw.GetValueOrDefault(material) + rate;
                return;
            }
            var recipe = table.Recipes.FirstOrDefault(r => r.OutputName == material && r.MinTier <= Tier && MachineOf(r.Machine, Tier) != null)
                         ?? throw new FactoryKitException($"no recipe at tier {Tier} makes {material} (Constructs/Refining.json)");
            var part = MachineOf(recipe.Machine, Tier)!;
            var k = banks.FindIndex(b => b.Output == material);
            if (k >= 0)
                banks[k] = banks[k] with { Rate = banks[k].Rate + rate };
            else
                banks.Add((material, recipe, part, rate));
            var batches = rate / recipe.UnitsOut;
            if (recipe.Mix.Length > 0)
                foreach (var (m, units) in recipe.Mix)
                    Need(RecyclingTable.Current.NameOf(m), batches * units, depth + 1);
            else
                Need(recipe.InputName, batches * recipe.UnitsIn, depth + 1);
        }
        foreach (var (m, perHour) in target)
            Need(m, perHour / 3600.0, 0);
        banks.Reverse(); // (raw end first: the order of the spine)

        // Raw heaps (the tipping bay's ore first), then each bank's machines, then a bin per output and the loading bin.
        var tipping = raw.Keys.FirstOrDefault(m => m.EndsWith(" ore", StringComparison.Ordinal)) ?? raw.Keys.First();
        var bay = Add("tipping", "Storage Volume", "intake", tipping, raw[tipping]);
        bay.Size = 3.0f;
        var heaps = new Dictionary<string, FactoryObject>();
        foreach (var (m, rate) in raw.OrderBy(r => r.Key == tipping ? 0 : 1).ThenBy(r => r.Key, StringComparer.Ordinal))
        {
            var h = heaps[m] = Add("heap", "Storage Volume", "intake", m, rate);
            h.Size = Math.Clamp((float)Math.Ceiling(Math.Sqrt(rate * f.IntakeMinutes * 60 / 400.0 / 2.0) * 2) / 2, 2.0f, 3.5f); // (400 units a cubic metre, 2 m high)
        }
        Edges.Add(new FactoryEdge(bay.Id, heaps[tipping].Id, tipping, "chute", raw[tipping], true));
        var made = new Dictionary<string, List<FactoryObject>>();
        for (var b = 0; b < banks.Count; b++)
        {
            var (output, recipe, part, rate) = banks[b];
            var per = recipe.UnitsOut / Math.Max(0.1, recipe.Seconds) * (part.RefineWork / (double)ConstructRefining.BaseWork);
            var n = Math.Max(1, (int)Math.Ceiling(rate / per * (1 + Spare) - 1e-9));
            var list = made[output] = new List<FactoryObject>();
            for (var k = 0; k < n; k++)
            {
                var use = part;
                if (k > 0 && d.Chance(style.OlderMachine) && MachineOf(recipe.Machine, part.Tier - 1) is { } older && older.Tier >= recipe.MinTier)
                    use = older; // (an older machine kept running in the bank: the style's habit)
                var o = Add("process", use.Name, "process", output, rate / n, b);
                o.Recipe = recipe.Mix.Length > 0 ? output : "";
                list.Add(o);
            }
            var inputs = recipe.Mix.Length > 0 ? recipe.Mix.Select(x => (RecyclingTable.Current.NameOf(x.Material), (double)x.Units)).ToArray() : [(recipe.InputName, (double)recipe.UnitsIn)];
            foreach (var (m, units) in inputs)
            {
                var share = rate / recipe.UnitsOut * units / n;
                if (heaps.TryGetValue(m, out var heap))
                    foreach (var o in list)
                        Edges.Add(new FactoryEdge(heap.Id, o.Id, m, "chute", share, m == tipping));
                else if (made.TryGetValue(m, out var from))
                    for (var k = 0; k < n; k++)
                        Edges.Add(new FactoryEdge(from[k % from.Count].Id, list[k].Id, m, "chute", share, true));
            }
        }
        foreach (var (m, perHour) in target)
        {
            var bin = Add("bin", "Storage Volume", "storage", m, perHour / 3600.0);
            bin.Size = 2.0f;
            foreach (var o in made[m])
                Edges.Add(new FactoryEdge(o.Id, bin.Id, m, "chute", o.Rate, true));
            var load = Add("loading", "Storage Volume", "output", m, perHour / 3600.0);
            load.Size = 2.0f;
            Edges.Add(new FactoryEdge(bin.Id, load.Id, m, "chute", bin.Rate, true));
        }
    }

    /// <summary>The machine part of a refining machine at a tier, else the highest below it (null: none at or under it).</summary>
    public static ConstructPartDef? MachineOf(string machine, int tier)
    {
        ConstructPartDef? best = null;
        foreach (var def in ConstructParts.All)
            if (def.RefineMachine == machine && def.Tier <= tier && !def.Name.Contains("Capital", StringComparison.Ordinal) && (best == null || def.Tier > best.Tier || def.Tier == best.Tier && string.CompareOrdinal(def.Name, best.Name) < 0))
                best = def;
        return best;
    }

    private static ConstructPartDef MachinePart(Func<ConstructPartDef, bool> what, int tier, string name)
    {
        ConstructPartDef? best = null;
        foreach (var def in ConstructParts.All)
            if (what(def) && def.Tier <= tier && (best == null || def.Tier > best.Tier || def.Tier == best.Tier && string.CompareOrdinal(def.Name, best.Name) < 0))
                best = def;
        return best ?? throw new FactoryKitException($"no {name} part at tier {tier} or under");
    }

    public string Text()
    {
        var sb = new StringBuilder();
        sb.Append($"programme: tier {Tier}, spare {FactoryData.N(Spare * 100)} %, power {FactoryData.N(PowerKw)} kW in {Gensets} {Genset}(s)\n");
        foreach (var o in Objects)
            sb.Append("  ").Append(o).Append('\n');
        sb.Append("edges:\n");
        foreach (var e in Edges)
            sb.Append($"  {e.From,2} -> {e.To,2} {e.Material} by {e.Carrier}, {FactoryData.N(e.Rate * 3600)}/h{(e.Main ? ", main line" : "")}\n");
        return sb.ToString();
    }
}
