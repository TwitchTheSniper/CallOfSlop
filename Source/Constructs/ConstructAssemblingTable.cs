using System.Text.Json;

namespace CallOfSlop;

/// <summary>One recipe of the Assembling table: <see cref="Count"/> of a part from materials and other parts, in <see cref="Seconds"/>.</summary>
public sealed class AssembleRecipe
{
    public string Name = ""; // the output part's name: what the configure tool lists and a blueprint names
    public ConstructPartDef Output = null!;
    public int Count = 1;
    public (byte Material, int Units)[] Materials = [];
    public (ConstructPartDef Part, int Count)[] Parts = [];
    public float Seconds = 1;
    public int MinTier = 1;
    public byte Machine; // which family of Assembler builds it: an index of AssemblingTable.Machines
    public int Choice; // its value of the recipe setting: 1 + its index in its family's list (AssemblingTable.ByMachine)
    public string Batch = ""; // "4 steel + 2 copper + 1 Gear Set" (made once)
    public bool Pressed; // Press phase 5: a part a die presses now; the row only keeps its place (and a saved Assembler's choice), it builds nothing

    /// <summary>What an Assembler set to a pressed row says (design section 6): "the Gear Set is pressed now: a Gear Die and a steel round 15x15x15".</summary>
    public string PressedText() => _pressedText ??= PressedSpec() is { } s
        ? $"the {Name} is pressed now: a {s.Die.Family} Die and a {Extrusion.Label(s.Blank)}{(s.Pattern.Bay2 is { } kit ? $", with {kit.What} in bay 2" : "")}"
        : $"the {Name} is pressed now: no die here makes it";

    /// <summary>The die that presses a pressed row's part ("Gear Die"), for a screen line.</summary>
    public string PressedDie() => _pressedDie ??= PressedSpec() is { } s ? $"{s.Die.Family} Die" : "no die";

    private string? _pressedText, _pressedDie; // (made once; a table is read again whole when its files change)

    private CallOfSlop.Pressed.OrderSpec? PressedSpec() =>
        PressTable.Current.PresetKeyOf(Output) is { } key ? CallOfSlop.Pressed.SpecOf(key) : null;

    /// <summary>The ticks a batch takes at speed 1.</summary>
    public int Ticks => (int)MathF.Ceiling(Seconds * World.TickRate - 0.001f);

    public int UnitsOf(byte material)
    {
        foreach (var (m, u) in Materials)
            if (m == material)
                return u;
        return 0;
    }

    public int CountOf(ushort part)
    {
        foreach (var (p, n) in Parts)
            if (p.Id == part)
                return n;
        return 0;
    }
}

/// <summary>
/// The Assembling table (Constructs/Assembling.json, format call-of-slop-assembling/1; docs/sandbox/CONSTRUCTS.md
/// "Assembling"): what an Assembler can build, one recipe per output part. A file that is refused gives no recipes and
/// <see cref="Problem"/>. A part's recipe choice is filled from the table when the parts load (<see cref="Attach"/>);
/// 0 means none, so a recipe's value is 1 + its index in its machine's own list (<see cref="ByMachine"/>): a recipe with a
/// <c>"machine"</c> is built by that family of Assembler (a part's <c>storage.assemble.machine</c>) and no other.
/// </summary>
public sealed class AssemblingTable
{
    public const string Format = "call-of-slop-assembling/1";
    public const int MaxRecipes = 2000, MaxInputs = 6, MaxUnits = 2000, MaxPartCount = 20, MaxOutput = 12;
    public const float MinSeconds = 0.5f, MaxSeconds = 600f;
    public const string NoRecipe = "(no recipe)";

    /// <summary>The Assembler families: "" the plain Assembler, then the machines that took flight parts over (docs/sandbox/CONSTRUCTS.md "Flight in survival").</summary>
    public static readonly string[] Machines = ["", "thruster", "tailor"];

    public AssembleRecipe[] Recipes = [];
    public string[] Choices = [NoRecipe];
    public AssembleRecipe[][] ByMachine = [[], [], []]; // per family: its recipes, in the file's order
    public string[][] ChoicesBy = [[NoRecipe], [NoRecipe], [NoRecipe]]; // ...and its recipe choice
    public string Problem = "";
    public Dictionary<ushort, AssembleRecipe> ByOutput = new(); // the recipe that makes a part (one recipe a part): work orders find theirs here

    /// <summary>The recipe that makes a part, or null.</summary>
    public AssembleRecipe? Of(ushort part) => ByOutput.TryGetValue(part, out var r) ? r : null;

    public static string FilePath => Path.Combine(AppFolders.Data, "Constructs", "Assembling.json");

    private static AssemblingTable? _current;

    /// <summary>The table in use. Loading the parts loads it (<see cref="Attach"/>).</summary>
    public static AssemblingTable Current
    {
        get
        {
            if (_current == null)
                _ = ConstructParts.All;
            return _current ?? new AssemblingTable { Problem = "the parts haven't loaded" };
        }
    }

    /// <summary>Sets the table in use (the self-test; null: read the file again, against the parts as loaded).</summary>
    public static void Use(AssemblingTable? t)
    {
        _current = t ?? Load(FilePath, ConstructParts.All);
        Fill(ConstructParts.All, _current);
    }

    /// <summary>Called as the parts load: reads the table against them and fills every Assembler's recipe choice.</summary>
    public static ConstructPartDef[] Attach(ConstructPartDef[] parts)
    {
        _current = Load(FilePath, parts);
        Fill(parts, _current);
        return parts;
    }

    private static void Fill(IReadOnlyList<ConstructPartDef> parts, AssemblingTable t)
    {
        foreach (var d in parts)
            if (d.CfgSlot[(int)CfgKey.Recipe] >= 0 && !d.IsPanelFormer) // (a Panel Former's recipe setting is its preset list, from its file)
                d.Config[d.CfgSlot[(int)CfgKey.Recipe]].Options = t.ChoicesBy[d.AsmMachine];
    }

    public static AssemblingTable Load(string path, IReadOnlyList<ConstructPartDef> parts)
    {
        try
        {
            return Parse(File.ReadAllText(path), parts);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new AssemblingTable { Problem = $"{Path.GetFileName(path)}: {ex.Message}" };
        }
    }

    /// <summary>Reads the table against <paramref name="parts"/>; a bad file gives no recipes and the reason.</summary>
    public static AssemblingTable Parse(string json, IReadOnlyList<ConstructPartDef>? parts = null, RecyclingTable? names = null)
    {
        parts ??= ConstructParts.All;
        names ??= RecyclingTable.Current;
        try
        {
            var byName = new Dictionary<string, ConstructPartDef>(StringComparer.OrdinalIgnoreCase);
            foreach (var d in parts)
                byName[d.Name] = d;
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("format", out var f) || f.ValueKind != JsonValueKind.String || f.GetString() != Format)
                throw new FormatException($"\"format\" must be \"{Format}\"");
            foreach (var p in root.EnumerateObject())
                if (p.Name is not ("format" or "about" or "recipes"))
                    throw new FormatException($"unknown key \"{p.Name}\"");
            if (!root.TryGetProperty("recipes", out var list) || list.ValueKind != JsonValueKind.Array || list.GetArrayLength() is 0 or > MaxRecipes)
                throw new FormatException($"\"recipes\" lists 1 to {MaxRecipes} recipes");
            var recipes = new List<AssembleRecipe>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in list.EnumerateArray())
            {
                var r = Recipe(e, $"recipe {recipes.Count + 1}", byName, names);
                if (!seen.Add(r.Name))
                    throw new FormatException($"recipe {recipes.Count + 1}: {r.Name} already has a recipe: one recipe a part");
                recipes.Add(r);
            }
            var t = new AssemblingTable { Recipes = recipes.Where(r => !r.Pressed).ToArray() };
            for (var m = 0; m < Machines.Length; m++)
            {
                t.ByMachine[m] = recipes.Where(r => r.Machine == m).ToArray();
                for (var k = 0; k < t.ByMachine[m].Length; k++)
                    t.ByMachine[m][k].Choice = k + 1;
                t.ChoicesBy[m] = [NoRecipe, .. t.ByMachine[m].Select(r => r.Name)]; // (a pressed row keeps its plain name: a blueprint or mod naming it still resolves, and the machine says it is pressed now)
            }
            t.Choices = t.ChoicesBy[0];
            foreach (var r in t.Recipes)
                t.ByOutput[(ushort)r.Output.Id] = r;
            return t;
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException)
        {
            return new AssemblingTable { Problem = ex.Message };
        }
    }

    private static AssembleRecipe Recipe(JsonElement e, string at, Dictionary<string, ConstructPartDef> byName, RecyclingTable names)
    {
        if (e.ValueKind != JsonValueKind.Object)
            throw new FormatException($"{at} is an object (output, materials, parts, seconds)");
        foreach (var p in e.EnumerateObject())
            if (p.Name is not ("output" or "count" or "materials" or "parts" or "seconds" or "minTier" or "machine" or "pressed"))
                throw new FormatException($"{at}: unknown key \"{p.Name}\"");
        var output = e.TryGetProperty("output", out var o) && o.ValueKind == JsonValueKind.String ? o.GetString()! : throw new FormatException($"{at} needs \"output\" (a part's name)");
        if (!byName.TryGetValue(output, out var def))
            throw new FormatException($"{at}: no part named \"{output}\"");
        at = $"{at} ({def.Name})";
        var r = new AssembleRecipe { Name = def.Name, Output = def };
        if (e.TryGetProperty("pressed", out var pr) && pr.ValueKind == JsonValueKind.True)
        {
            // (a part a die presses now: the row keeps its place in its family's list, takes nothing and builds nothing; Press phase 5)
            if (e.TryGetProperty("machine", out var pm))
                r.Machine = MachineOf(pm.GetString() ?? "", at);
            r.Pressed = true;
            return r;
        }
        if (e.TryGetProperty("machine", out var mc))
            r.Machine = MachineOf(mc.GetString() ?? "", at);
        if (e.TryGetProperty("count", out var c))
            r.Count = Whole(c, "count", 1, MaxOutput, at);
        if (r.Count > 1 && def.PhysicsOnly)
            throw new FormatException($"{at}: a physics-only part leaves as one body: \"count\" is 1");
        var mats = new List<(byte, int)>();
        var bits = new List<string>();
        if (e.TryGetProperty("materials", out var ml))
        {
            if (ml.ValueKind != JsonValueKind.Object)
                throw new FormatException($"{at}: \"materials\" is an object of material: units");
            foreach (var p in ml.EnumerateObject())
            {
                var id = names.IdOf(p.Name);
                if (id == 0)
                    throw new FormatException($"{at}: \"{p.Name}\" isn't a bulk material (Recycling.json)");
                mats.Add((id, Whole(p.Value, p.Name, 1, MaxUnits, at)));
                bits.Add($"{mats[^1].Item2} {p.Name}");
            }
        }
        var parts = new List<(ConstructPartDef, int)>();
        if (e.TryGetProperty("parts", out var pl))
        {
            if (pl.ValueKind != JsonValueKind.Object)
                throw new FormatException($"{at}: \"parts\" is an object of part name: count");
            foreach (var p in pl.EnumerateObject())
            {
                if (!byName.TryGetValue(p.Name, out var d))
                    throw new FormatException($"{at}: no part named \"{p.Name}\"");
                if (d.PhysicsOnly)
                    throw new FormatException($"{at}: the {d.Name} is physics-only ({d.PhysicsWhy}): no chute or hands can bring it, so it can't be an input");
                if (d.Id == def.Id)
                    throw new FormatException($"{at}: it can't be built from itself");
                parts.Add((d, Whole(p.Value, p.Name, 1, MaxPartCount, at)));
                bits.Add($"{parts[^1].Item2} {d.Name}");
            }
        }
        if (mats.Count + parts.Count == 0)
            throw new FormatException($"{at}: a recipe takes something (\"materials\", \"parts\")");
        if (mats.Count + parts.Count > MaxInputs)
            throw new FormatException($"{at}: at most {MaxInputs} inputs");
        r.Materials = mats.ToArray();
        r.Parts = parts.ToArray();
        r.Batch = string.Join(" + ", bits);
        r.Seconds = e.TryGetProperty("seconds", out var s) && s.ValueKind == JsonValueKind.Number && s.GetSingle() is >= MinSeconds and <= MaxSeconds ? s.GetSingle()
            : throw new FormatException($"{at}: \"seconds\" is a number from {MinSeconds} to {MaxSeconds}");
        if (e.TryGetProperty("minTier", out var t))
            r.MinTier = Whole(t, "minTier", 1, ConstructParts.MaxTier, at);
        return r;
    }

    /// <summary>The family a <c>"machine"</c> names.</summary>
    internal static byte MachineOf(string name, string at)
    {
        var k = Array.IndexOf(Machines, name);
        return k >= 0 ? (byte)k : throw new FormatException($"{at}: \"machine\" is one of {string.Join(", ", Machines.Skip(1))}, not \"{name}\"");
    }

    private static int Whole(JsonElement e, string key, int min, int max, string at) =>
        e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out var n) && n >= min && n <= max ? n : throw new FormatException($"{at}: \"{key}\" is a whole number from {min} to {max}");
}
