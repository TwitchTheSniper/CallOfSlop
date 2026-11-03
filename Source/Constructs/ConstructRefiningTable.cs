using System.Text.Json;

namespace CallOfSlop;

/// <summary>One recipe: a machine turns <see cref="UnitsIn"/> of a material into <see cref="UnitsOut"/> of another in <see cref="Seconds"/>.</summary>
public sealed record RefineRecipe(string Machine, string InputName, byte Input, int UnitsIn, string OutputName, byte Output, int UnitsOut, float Seconds)
{
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
    public const int MaxRecipes = 64, MaxUnits = 100;
    public const float MinSeconds = 0.1f, MaxSeconds = 600f;

    private static readonly (string Machine, string In, int UnitsIn, string Out, int UnitsOut, float Seconds)[] BuiltIn =
    [
        ("smelter", "iron ore", 2, "metal", 1, 0.5f), ("smelter", "copper ore", 2, "copper", 1, 0.6f), ("smelter", "gold ore", 4, "gold", 1, 1f),
        ("smelter", "sand", 2, "glass", 1, 0.5f), ("sawmill", "wood", 1, "planks", 2, 0.5f),
    ];

    public RefineRecipe[] Recipes = [];
    public string[] Machines = [];
    public string Problem = "";
    private readonly Dictionary<string, RefineRecipe?[]> _byInput = []; // by machine, then material id
    private readonly Dictionary<string, string> _takes = [];

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
            .Select(r => new RefineRecipe(r.Machine, r.In, names.IdOf(r.In), r.UnitsIn, r.Out, names.IdOf(r.Out), r.UnitsOut, r.Seconds)));
    }

    private RefiningTable Finish(IEnumerable<RefineRecipe> recipes)
    {
        Recipes = recipes.ToArray();
        Machines = Recipes.Select(r => r.Machine).Distinct().ToArray();
        foreach (var m in Machines)
        {
            var row = new RefineRecipe?[SandboxInventory.MaxMaterials];
            foreach (var r in Recipes.Where(r => r.Machine == m))
                row[r.Input] = r;
            _byInput[m] = row;
            _takes[m] = string.Join(", ", Recipes.Where(r => r.Machine == m).Select(r => r.InputName));
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
                    if (p.Name is not ("machine" or "input" or "unitsIn" or "output" or "unitsOut" or "seconds"))
                        throw new FormatException($"{at}: unknown key \"{p.Name}\"");
                var machine = Text(e, "machine", at);
                if (machine.Length is 0 or > 24 || machine.Any(c => c is < 'a' or > 'z'))
                    throw new FormatException($"{at}: \"machine\" is 1 to 24 lower-case letters");
                var input = Text(e, "input", at);
                var output = Text(e, "output", at);
                var inId = names.IdOf(input);
                var outId = names.IdOf(output);
                if (inId == 0)
                    throw new FormatException($"{at}: \"{input}\" isn't a bulk material (Recycling.json)");
                if (outId == 0)
                    throw new FormatException($"{at}: \"{output}\" isn't a bulk material (Recycling.json)");
                if (inId == outId)
                    throw new FormatException($"{at}: {input} refines into itself");
                if (recipes.Any(r => r.Machine == machine && r.Input == inId))
                    throw new FormatException($"{at}: the {machine} already has a recipe for {input}: the input picks the recipe");
                recipes.Add(new RefineRecipe(machine, input, inId, Units(e, "unitsIn", at), output, outId, Units(e, "unitsOut", at), Seconds(e, at)));
            }
            return new RefiningTable().Finish(recipes);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException)
        {
            return Refused(ex.Message, names);
        }
    }

    private static string Text(JsonElement e, string key, string at) =>
        e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : throw new FormatException($"{at} needs \"{key}\" (a name)");

    private static int Units(JsonElement e, string key, string at) =>
        e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) && n is >= 1 and <= MaxUnits ? n
            : throw new FormatException($"{at}: \"{key}\" is a whole number from 1 to {MaxUnits}");

    private static float Seconds(JsonElement e, string at) =>
        e.TryGetProperty("seconds", out var v) && v.ValueKind == JsonValueKind.Number && v.GetSingle() is >= MinSeconds and <= MaxSeconds ? v.GetSingle()
            : throw new FormatException($"{at}: \"seconds\" is a number from {MinSeconds} to {MaxSeconds}");

    /// <summary>The recipe <paramref name="machine"/> uses for <paramref name="material"/>, or null. Allocates nothing.</summary>
    public RefineRecipe? Find(string machine, byte material) =>
        material > 0 && material < SandboxInventory.MaxMaterials && _byInput.TryGetValue(machine, out var row) ? row[material] : null;

    /// <summary>What a machine takes, for the look line ("iron ore, sand"); "" for an unknown machine.</summary>
    public string Takes(string machine) => _takes.TryGetValue(machine, out var s) ? s : "";
}
