using System.Text.Json;

namespace CallOfSlop;

/// <summary>One line of a loot table: a part, how likely, how many.</summary>
public readonly record struct LootItem(ConstructPartDef Part, int Weight, int Min, int Max);

/// <summary>A faction's haul (Constructs/Loot.json): what a container of its can hold.</summary>
public sealed class LootTable
{
    public string Name = "";
    public int MaxTier, RollsMin, RollsMax, TotalWeight;
    public float Empty;
    public LootItem[] Items = [];
}

/// <summary>
/// Loot that rolls when a blueprint is spawned (docs/sandbox/CONSTRUCTS.md, "Loot"). A blueprint names containers and a table ("loot": [{"step": n, "table": "bandit"}]);
/// <see cref="ConstructBuild.BuildOnGround"/> fills them, on the server only, into the containers' ordinary slots. From then on it is stored parts like
/// any others: taken by hand, shot out with the container, saved with the world. Nothing rolls again, and a load doesn't roll.
/// </summary>
public static class ConstructLoot
{
    public const string Format = "call-of-slop-loot/1";

    private static readonly Lazy<(Dictionary<string, LootTable> Tables, List<string> Problems)> _tables = new(Load, LazyThreadSafetyMode.ExecutionAndPublication);

    public static string FilePath => Path.Combine(AppFolders.Data, "Constructs", "Loot.json");

    public static IReadOnlyDictionary<string, LootTable> Tables => _tables.Value.Tables;

    /// <summary>What's wrong with the file, each with its reason: such an item is left out.</summary>
    public static IReadOnlyList<string> Problems => _tables.Value.Problems;

    private static (Dictionary<string, LootTable>, List<string>) Load()
    {
        var tables = new Dictionary<string, LootTable>(StringComparer.OrdinalIgnoreCase);
        var problems = new List<string>();
        if (!File.Exists(FilePath))
            return (tables, problems);
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(FilePath), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            if (doc.RootElement.GetProperty("format").GetString() != Format)
                problems.Add($"Loot.json: \"format\" must be \"{Format}\"");
            foreach (var t in doc.RootElement.GetProperty("tables").EnumerateObject())
                tables[t.Name] = Read(t.Name, t.Value, problems);
        }
        catch (Exception ex) when (ex is JsonException or IOException or KeyNotFoundException or InvalidOperationException)
        {
            problems.Add($"Loot.json: {ex.Message}");
        }
        return (tables, problems);
    }

    private static LootTable Read(string name, JsonElement e, List<string> problems)
    {
        var table = new LootTable
        {
            Name = name,
            MaxTier = e.GetProperty("maxTier").GetInt32(),
            RollsMin = e.GetProperty("rolls")[0].GetInt32(),
            RollsMax = e.GetProperty("rolls")[1].GetInt32(),
            Empty = e.GetProperty("empty").GetSingle(),
        };
        var rarities = e.GetProperty("rarities").EnumerateArray().Select(r => r.GetString() ?? "").ToArray();
        var items = new List<LootItem>();
        foreach (var it in e.GetProperty("items").EnumerateArray())
        {
            var part = it.GetProperty("part").GetString() ?? "";
            var count = it.GetProperty("count");
            if (Refuse(ConstructParts.Find(part), part, table, rarities) is { } why)
            {
                problems.Add($"Loot.json: {name}: {part}: {why}");
                continue;
            }
            items.Add(new LootItem(ConstructParts.Find(part)!, it.GetProperty("weight").GetInt32(), count[0].GetInt32(), count[1].GetInt32()));
        }
        table.Items = items.ToArray();
        table.TotalWeight = items.Sum(i => i.Weight);
        return table;
    }

    private static int TierOfClass(ConstructPartDef d) => d.Class == ConstructParts.Small ? 1 : d.Class == ConstructParts.Medium ? 2 : d.Class == ConstructParts.Large ? 3 : 4;

    /// <summary>Why a part can't be in a table: it must fit a slot and belong to the table's tech tier.</summary>
    private static string? Refuse(ConstructPartDef? d, string name, LootTable t, string[] rarities)
    {
        if (d == null)
            return "no such part";
        if (d.PhysicsOnly)
            return "it is physics-only, and slots hold items";
        var wp = d.GunPartName.Length > 0 ? Parts.Find(d.GunPartName) : d.GunReceiver.Length > 0 ? Parts.Find(d.GunReceiver) : null;
        if (wp != null && !rarities.Contains(wp.Rarity.ToString(), StringComparer.OrdinalIgnoreCase))
            return $"a weapon part of rarity {wp.Rarity}: the table takes {string.Join(", ", rarities)}";
        if ((d.Surface != null || d.Rotor != null) && TierOfClass(d) > t.MaxTier)
            return $"it is tier {TierOfClass(d)} by its size: the table stops at tier {t.MaxTier}";
        var recipe = AssemblingTable.Current.Recipes.FirstOrDefault(r => r.Output == d);
        if (recipe != null && recipe.MinTier > t.MaxTier)
            return $"its recipe needs a tier {recipe.MinTier} Assembler: the table stops at tier {t.MaxTier}";
        return recipe == null && wp == null && d.Surface == null && d.Rotor == null ? $"it has no recipe to show its tier" : null; // (a wing or a rotor file is looted only: an Airframe or Fan Former makes its own, so it has none)
    }

    /// <summary>A step of a tiny generator: this file's rolls need no more, and it can't touch a seeded simulation.</summary>
    private static uint Next(ref uint s)
    {
        s ^= s << 13;
        s ^= s >> 17;
        s ^= s << 5;
        return s;
    }

    private static int Between(ref uint s, int lo, int hi) => lo + (int)((ulong)Next(ref s) * (ulong)(hi - lo + 1) >> 32);

    /// <summary>Fills a blueprint's loot spots on a spawned construct. Returns how many pieces went in. Only a server's spawn gets here.</summary>
    public static int Fill(ConstructWorld world, Construct c, ConstructBuild.Blueprint bp, ReadOnlySpan<int> partOf)
    {
        var total = 0;
        var s = world.NextLootSeed();
        foreach (var (step, name) in bp.Loot)
            if (Tables.TryGetValue(name, out var table) && table.TotalWeight > 0)
                total += FillOne(world, c, partOf[step], table, ref s);
        return total;
    }

    private static int FillOne(ConstructWorld world, Construct c, int part, LootTable t, ref uint s)
    {
        if (world.StoreOf(c, part) is not { } st || Next(ref s) / 4294967296f < t.Empty)
            return 0;
        var cd = c.DefOf(part);
        var put = 0;
        for (int k = 0, rolls = Between(ref s, t.RollsMin, t.RollsMax); k < rolls; k++)
        {
            var item = Pick(t, ref s);
            var want = Between(ref s, item.Min, item.Max);
            for (var slot = 0; slot < st.SlotCount && want > 0; slot++)
                while (want > 0 && ConstructStorage.Check(cd, st, slot, item.Part, out var rot) == SlotCheck.Ok)
                {
                    st.Def[slot] = item.Part.Id;
                    st.Rot[slot] = rot;
                    st.Count[slot]++;
                    st.Health[slot] = 1f;
                    st.Version++;
                    want--;
                    put++;
                }
        }
        if (put > 0)
            world.StoreDirty.Add(st.Handle);
        return put;
    }

    /// <summary>A blueprint's <c>"stored"</c> entry: <c>{"step": n, "slot": k, "part": "Box Mag", "count": 2}</c>, an item (never physics-only) for that container's slot.</summary>
    public static (int Step, int Slot, string Part, int Count) ParseStored(JsonElement e, string file, int k)
    {
        if (e.ValueKind != JsonValueKind.Object)
            throw new ConstructPartException($"{file}: stored[{k}] is {{\"step\", \"slot\", \"part\", \"count\"}}");
        int step = -1, slot = -1, count = 1;
        var part = "";
        foreach (var f in e.EnumerateObject())
            switch (f.Name)
            {
                case "step": step = f.Value.GetInt32(); break;
                case "slot": slot = f.Value.GetInt32(); break;
                case "part": part = f.Value.GetString() ?? ""; break;
                case "count": count = f.Value.GetInt32(); break;
                default: throw new ConstructPartException($"{file}: stored[{k}]: unknown key \"{f.Name}\" (step, slot, part, count)");
            }
        if (ConstructParts.Find(part) is not { } d)
            throw new ConstructPartException($"{file}: stored[{k}]: no part named \"{part}\"");
        if (d.PhysicsOnly)
            throw new ConstructPartException($"{file}: stored[{k}]: the {part} is physics-only, and slots hold items");
        if (step < 0 || slot < 0 || slot >= CStore.MaxSlots || count < 1 || count > 48)
            throw new ConstructPartException($"{file}: stored[{k}]: a step, a slot 0 to {CStore.MaxSlots - 1} and a count 1 to 48");
        return (step, slot, part, count);
    }

    /// <summary>
    /// A blueprint's stowed parts, each set in its container's slot as a player would set it there (<see cref="ConstructStorage.Check"/>: what doesn't fit, or a slot
    /// that holds another kind, is left out). Before <see cref="Fill"/>, so a loot roll takes what is left. Returns how many went in. Only a server's spawn gets here.
    /// </summary>
    public static int Stow(ConstructWorld world, Construct c, ConstructBuild.Blueprint bp, ReadOnlySpan<int> partOf)
    {
        var put = 0;
        foreach (var (step, slot, name, count) in bp.Stored)
        {
            if (step >= partOf.Length || world.StoreOf(c, partOf[step]) is not { } st || ConstructParts.Find(name) is not { } d)
                continue;
            var cd = c.DefOf(partOf[step]);
            var before = put;
            for (var n = 0; n < count && ConstructStorage.Check(cd, st, slot, d, out var rot) == SlotCheck.Ok; n++)
            {
                st.Def[slot] = d.Id;
                st.Rot[slot] = rot;
                st.Count[slot]++;
                st.Health[slot] = 1f;
                st.Version++;
                put++;
            }
            if (put > before)
                world.StoreDirty.Add(st.Handle);
        }
        return put;
    }

    private static LootItem Pick(LootTable t, ref uint s)
    {
        var w = Between(ref s, 1, t.TotalWeight);
        foreach (var i in t.Items)
            if ((w -= i.Weight) <= 0)
                return i;
        return t.Items[^1];
    }
}
