using System.Text.Json;

namespace CallOfSlop;

/// <summary>
/// One recipe: a machine turns <see cref="UnitsIn"/> of a material into <see cref="UnitsOut"/> of another in <see cref="Seconds"/>.
/// A mixed recipe (a Foundry's) lists every input in <see cref="Mix"/>; <see cref="InputName"/> is then "metal + stone", and
/// <see cref="Input"/> and <see cref="UnitsIn"/> its first.
/// </summary>
public sealed record RefineRecipe(string Machine, string InputName, byte Input, int UnitsIn, string OutputName, byte Output, int UnitsOut, float Seconds, int MinTier = 1)
{
    /// <summary>A mixed recipe's inputs (empty: <see cref="Input"/> alone).</summary>
    public (byte Material, int Units)[] Mix { get; init; } = [];

    /// <summary>Does a mixed recipe take this material?</summary>
    public bool Uses(byte material)
    {
        foreach (var (m, _) in Mix)
            if (m == material)
                return true;
        return false;
    }

    /// <summary>"2 metal + 1 stone" (text made once).</summary>
    public string Batch { get; init; } = "";

    /// <summary>The ticks a batch takes (the step's whole 10 Hz steps add up to at least this).</summary>
    public int Ticks => (int)MathF.Ceiling(Seconds * World.TickRate - 0.001f);
}

/// <summary>
/// The refining table (Constructs/Refining.json; docs/sandbox/CONSTRUCTS.md "Refining"): what each production machine
/// makes, one recipe per machine and input material. Materials are <see cref="RecyclingTable"/>'s names, held as its ids. A
/// bad file gives the built-in recipes with <see cref="Problem"/>. Looking a recipe up allocates nothing.
/// </summary>
public sealed class RefiningTable
{
    public const string Format = "call-of-slop-refining/1";
    public const int MaxRecipes = 64, MaxUnits = 100, MaxMixInputs = 4;
    public const float MinSeconds = 0.1f, MaxSeconds = 600f;

    private static readonly (string Machine, string In, int UnitsIn, string Out, int UnitsOut, float Seconds, int MinTier)[] BuiltIn =
    [
        ("smelter", "iron ore", 2, "metal", 1, 0.5f, 1), ("smelter", "copper ore", 2, "copper", 1, 0.6f, 2), ("smelter", "gold ore", 4, "gold", 1, 1f, 3),
        ("smelter", "sand", 2, "glass", 1, 0.5f, 1), ("sawmill", "wood", 1, "planks", 2, 0.5f, 1),
    ];

    public RefineRecipe[] Recipes = [];
    public string[] Machines = [];
    public string Problem = "";
    private readonly Dictionary<string, RefineRecipe?[]> _byInput = []; // by machine, then material id
    private readonly Dictionary<string, RefineRecipe[]> _mixes = []; // a mixed machine's recipes
    private readonly Dictionary<string, string[]> _takes = [], _lines = []; // by machine, then tier (0: every recipe)

    public static string FilePath => Path.Combine(AppFolders.Data, "Constructs", "Refining.json");

    private static RefiningTable? _current;

    /// <summary>The table in use (loaded once; the built-in recipes when the file is missing or refused).</summary>
    public static RefiningTable Current => _current ??= Load(FilePath);

    /// <summary>Sets the table in use (the self-test; null: load the file again).</summary>
    public static void Use(RefiningTable? t) => _current = t;

    public static RefiningTable Load(string path)
    {
        try
        {
            return Parse(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Refused($"{Path.GetFileName(path)}: {ex.Message}", RecyclingTable.Current);
        }
    }

    private static RefiningTable Refused(string why, RecyclingTable names)
    {
        var t = new RefiningTable { Problem = why };
        return t.Finish(BuiltIn.Where(r => names.IdOf(r.In) != 0 && names.IdOf(r.Out) != 0)
            .Select(r => new RefineRecipe(r.Machine, r.In, names.IdOf(r.In), r.UnitsIn, r.Out, names.IdOf(r.Out), r.UnitsOut, r.Seconds, r.MinTier)));
    }

    private RefiningTable Finish(IEnumerable<RefineRecipe> recipes)
    {
        Recipes = recipes.ToArray();
        Machines = Recipes.Select(r => r.Machine).Distinct().ToArray();
        foreach (var m in Machines)
        {
            var row = new RefineRecipe?[SandboxInventory.MaxMaterials];
            foreach (var r in Recipes.Where(r => r.Machine == m && r.Mix.Length == 0))
                row[r.Input] = r;
            _byInput[m] = row;
            var (takes, lines) = (new string[ConstructParts.MaxTier + 1], new string[ConstructParts.MaxTier + 1]);
            var mine = Recipes.Where(r => r.Machine == m).ToArray();
            if (mine[0].Mix.Length > 0)
                _mixes[m] = mine;
            takes[0] = lines[0] = string.Join(", ", mine.Select(r => r.InputName));
            for (var tier = 1; tier <= ConstructParts.MaxTier; tier++)
            {
                var t = tier;
                takes[t] = string.Join(", ", mine.Where(r => r.MinTier <= t).Select(r => r.InputName));
                var gated = string.Join(", ", mine.Where(r => r.MinTier > t).Select(r => $"{r.InputName} needs tier {r.MinTier}"));
                lines[t] = (takes[t].Length > 0 ? takes[t] : "nothing yet") + (gated.Length > 0 ? $" ({gated})" : "");
            }
            _takes[m] = takes;
            _lines[m] = lines;
        }
        return this;
    }

    /// <summary>Reads the table; every material must be in <paramref name="names"/> (default: the recycling table in use).</summary>
    public static RefiningTable Parse(string json, RecyclingTable? names = null)
    {
        names ??= RecyclingTable.Current;
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("format", out var f) || f.ValueKind != JsonValueKind.String || f.GetString() != Format)
                throw new FormatException($"\"format\" must be \"{Format}\"");
            foreach (var p in root.EnumerateObject())
                if (p.Name is not ("format" or "about" or "recipes"))
                    throw new FormatException($"unknown key \"{p.Name}\"");
            if (!root.TryGetProperty("recipes", out var list) || list.ValueKind != JsonValueKind.Array || list.GetArrayLength() is 0 or > MaxRecipes)
                throw new FormatException($"\"recipes\" lists 1 to {MaxRecipes} recipes");
            var recipes = new List<RefineRecipe>();
            foreach (var e in list.EnumerateArray())
            {
                var at = $"recipe {recipes.Count + 1}";
                if (e.ValueKind != JsonValueKind.Object)
                    throw new FormatException($"{at} is an object (machine, input, unitsIn, output, unitsOut, seconds)");
                foreach (var p in e.EnumerateObject())
                    if (p.Name is not ("machine" or "input" or "unitsIn" or "inputs" or "output" or "unitsOut" or "seconds" or "minTier"))
                        throw new FormatException($"{at}: unknown key \"{p.Name}\"");
                var machine = Text(e, "machine", at);
                if (machine.Length is 0 or > 24 || machine.Any(c => c is < 'a' or > 'z'))
                    throw new FormatException($"{at}: \"machine\" is 1 to 24 lower-case letters");
                var output = Text(e, "output", at);
                var outId = names.IdOf(output);
                if (e.TryGetProperty("inputs", out var mixed))
                {
                    recipes.Add(ParseMix(e, mixed, machine, output, outId, at, names, recipes));
                    continue;
                }
                if (recipes.Any(r => r.Machine == machine && r.Mix.Length > 0))
                    throw new FormatException($"{at}: the {machine} has mixed recipes: all of a machine's recipes take \"input\" or all take \"inputs\"");
                var input = Text(e, "input", at);
                var inId = names.IdOf(input);
                if (inId == 0)
                    throw new FormatException($"{at}: \"{input}\" isn't a bulk material (Recycling.json)");
                if (outId == 0)
                    throw new FormatException($"{at}: \"{output}\" isn't a bulk material (Recycling.json)");
                if (inId == outId)
                    throw new FormatException($"{at}: {input} refines into itself");
                if (recipes.Any(r => r.Machine == machine && r.Input == inId))
                    throw new FormatException($"{at}: the {machine} already has a recipe for {input}: the input picks the recipe");
                recipes.Add(new RefineRecipe(machine, input, inId, Units(e, "unitsIn", at), output, outId, Units(e, "unitsOut", at), Seconds(e, at), MinTier(e, at)));
            }
            return new RefiningTable().Finish(recipes);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException)
        {
            return Refused(ex.Message, names);
        }
    }

    /// <summary>A mixed recipe: 2 to <see cref="MaxMixInputs"/> different materials; the set of them picks the recipe.</summary>
    private static RefineRecipe ParseMix(JsonElement e, JsonElement list, string machine, string output, byte outId, string at, RecyclingTable names, List<RefineRecipe> recipes)
    {
        if (e.TryGetProperty("input", out _) || e.TryGetProperty("unitsIn", out _))
            throw new FormatException($"{at}: \"inputs\" replaces \"input\" and \"unitsIn\"");
        if (list.ValueKind != JsonValueKind.Array || list.GetArrayLength() < 2 || list.GetArrayLength() > MaxMixInputs)
            throw new FormatException($"{at}: \"inputs\" lists 2 to {MaxMixInputs} materials ({{\"material\", \"units\"}})");
        if (recipes.Any(r => r.Machine == machine && r.Mix.Length == 0))
            throw new FormatException($"{at}: the {machine} has single-input recipes: all of a machine's recipes take \"input\" or all take \"inputs\"");
        if (outId == 0)
            throw new FormatException($"{at}: \"{output}\" isn't a bulk material (Recycling.json)");
        var mix = new List<(byte Material, int Units)>();
        foreach (var i in list.EnumerateArray())
        {
            if (i.ValueKind != JsonValueKind.Object)
                throw new FormatException($"{at}: an input is an object (material, units)");
            foreach (var p in i.EnumerateObject())
                if (p.Name is not ("material" or "units"))
                    throw new FormatException($"{at}: unknown key \"{p.Name}\"");
            var name = Text(i, "material", at);
            var id = names.IdOf(name);
            if (id == 0)
                throw new FormatException($"{at}: \"{name}\" isn't a bulk material (Recycling.json)");
            if (id == outId)
                throw new FormatException($"{at}: {name} refines into itself");
            if (mix.Any(m => m.Material == id))
                throw new FormatException($"{at}: {name} is listed twice");
            mix.Add((id, Units(i, "units", at)));
        }
        if (recipes.Any(r => r.Machine == machine && r.Mix.Length == mix.Count && mix.All(m => r.Uses(m.Material))))
            throw new FormatException($"{at}: the {machine} already has a recipe for those inputs: the inputs pick the recipe");
        return new RefineRecipe(machine, string.Join(" + ", mix.Select(m => names.NameOf(m.Material))), mix[0].Material, mix[0].Units, output, outId, Units(e, "unitsOut", at), Seconds(e, at), MinTier(e, at))
        {
            Mix = mix.ToArray(),
            Batch = string.Join(" + ", mix.Select(m => $"{m.Units} {names.NameOf(m.Material)}")),
        };
    }

    private static string Text(JsonElement e, string key, string at) =>
        e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : throw new FormatException($"{at} needs \"{key}\" (a name)");

    private static int Units(JsonElement e, string key, string at) =>
        e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) && n is >= 1 and <= MaxUnits ? n
            : throw new FormatException($"{at}: \"{key}\" is a whole number from 1 to {MaxUnits}");

    private static float Seconds(JsonElement e, string at) =>
        e.TryGetProperty("seconds", out var v) && v.ValueKind == JsonValueKind.Number && v.GetSingle() is >= MinSeconds and <= MaxSeconds ? v.GetSingle()
            : throw new FormatException($"{at}: \"seconds\" is a number from {MinSeconds} to {MaxSeconds}");

    private static int MinTier(JsonElement e, string at)
    {
        if (!e.TryGetProperty("minTier", out var v))
            return 1;
        return v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) && n is >= 1 and <= ConstructParts.MaxTier ? n
            : throw new FormatException($"{at}: \"minTier\" is a whole number from 1 to {ConstructParts.MaxTier}");
    }

    /// <summary>The recipe <paramref name="machine"/> uses for <paramref name="material"/>, or null. Allocates nothing.</summary>
    public RefineRecipe? Find(string machine, byte material) =>
        material > 0 && material < SandboxInventory.MaxMaterials && _byInput.TryGetValue(machine, out var row) ? row[material] : null;

    /// <summary>Do this machine's recipes take several inputs each? (Allocates nothing.)</summary>
    public bool IsMix(string machine) => _mixes.ContainsKey(machine);

    /// <summary>A mixed machine's recipes (empty for any other). Allocates nothing.</summary>
    public RefineRecipe[] MixesOf(string machine) => _mixes.TryGetValue(machine, out var r) ? r : [];

    /// <summary>What a machine of a tier takes ("iron ore, sand"; tier 0: of any tier); "" for an unknown machine.</summary>
    public string Takes(string machine, int tier = 0) => _takes.TryGetValue(machine, out var s) ? s[Math.Clamp(tier, 0, ConstructParts.MaxTier)] : "";

    /// <summary>For the look line: what a machine of a tier takes, and what asks for a higher one ("iron ore, sand (copper ore needs tier 2)").</summary>
    public string TakesLine(string machine, int tier) => _lines.TryGetValue(machine, out var s) ? s[Math.Clamp(tier, 0, ConstructParts.MaxTier)] : "";
}
