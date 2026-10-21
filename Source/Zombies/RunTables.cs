using System.Text.Json;

namespace CallOfSlop;

/// <summary>One row of a by-round table: from this round on, these weights per tier (or this chance).</summary>
public sealed record RoundRow(int FromRound, float[] Weights, float Chance);

/// <summary>
/// The roguelite drop tables (Parts/Tables/DropTables.json, format "call-of-slop-drops/1"): proposed data for the
/// Zombies mode, open for the user (docs/zombies/WEAPON-BUILDER.md, "Run progression"). A run starts with low-level gear;
/// later rounds roll rarer parts and builds, so one run can climb from a wall rifle to a sixteen-receiver monster. Nothing
/// in the game rolls these yet (the Zombies mode comes later); the self-test checks the curve's shape.
/// </summary>
public sealed class RunTables
{
    public const string Format = "call-of-slop-drops/1";

    /// <summary>Tier of a part a zombie drops, by round.</summary>
    public List<RoundRow> PartDrops = new();

    /// <summary>Chance a zombie drops a part at all, by round.</summary>
    public List<RoundRow> DropChance = new();

    /// <summary>Tier of the complete build the mystery box gives, by round.</summary>
    public List<RoundRow> BoxTiers = new();

    /// <summary>Points prices (proposed): the mystery box, the weapon-slot upgrade (2 -> 4 slots, once per run), a wall weapon.</summary>
    public int BoxCost = 950, SlotUpgradeCost = 4000, WallCost = 500;

    private static RunTables? _loaded;
    public static readonly List<string> LoadErrors = new();

    public static string PathName => Path.Combine(Parts.Folder, "Tables", "DropTables.json");

    /// <summary>The tables from Parts/Tables/DropTables.json (empty tables if it's missing or broken; the error is listed).</summary>
    public static RunTables Current
    {
        get
        {
            if (_loaded != null)
                return _loaded;
            try
            {
                _loaded = Load(File.ReadAllText(PathName), "Tables/DropTables.json");
            }
            catch (Exception ex) when (ex is WeaponFileException or IOException or UnauthorizedAccessException)
            {
                LoadErrors.Add(ex.Message);
                Console.WriteLine($"[drops] {ex.Message}");
                _loaded = new RunTables();
            }
            return _loaded;
        }
    }

    /// <summary>The row that applies in <paramref name="round"/> (the last one starting at or before it).</summary>
    public static RoundRow? RowFor(List<RoundRow> rows, int round) => rows.LastOrDefault(r => r.FromRound <= round) ?? rows.FirstOrDefault();

    /// <summary>
    /// A tier rolled from <paramref name="rows"/> for <paramref name="round"/>, deterministically from <paramref name="seed"/>
    /// (the same seed always gives the same tier, so co-op clients and the server agree).
    /// </summary>
    public static Rarity Roll(List<RoundRow> rows, int round, uint seed)
    {
        var row = RowFor(rows, round);
        if (row == null)
            return Rarity.Common;
        var total = row.Weights.Sum();
        if (total <= 0)
            return Rarity.Common;
        var h = seed * 2654435761u;
        h ^= h >> 15;
        var pick = (h % 1000000u) / 1000000f * total;
        for (var i = 0; i < row.Weights.Length; i++)
        {
            pick -= row.Weights[i];
            if (pick < 0)
                return (Rarity)i;
        }
        return (Rarity)Array.FindLastIndex(row.Weights, w => w > 0);
    }

    public static RunTables Load(string json, string source)
    {
        using var doc = WeaponFile.Parse(json, source);
        var root = doc.RootElement;
        var t = new RunTables();
        foreach (var p in root.EnumerateObject())
        {
            var at = $"{source}: \"{p.Name}\"";
            switch (p.Name)
            {
                case "format": WeaponFile.CheckFormat(p.Value, "call-of-slop-drops/", Format, at); break;
                case "note": break;
                case "partDrops": t.PartDrops = Rows(p.Value, at, weights: true); break;
                case "dropChance": t.DropChance = Rows(p.Value, at, weights: false); break;
                case "boxTiers": t.BoxTiers = Rows(p.Value, at, weights: true); break;
                case "costs":
                    foreach (var c in p.Value.EnumerateObject())
                    {
                        var v = WeaponFile.Int(c.Value, $"{at}.{c.Name}");
                        if (v is < 0 or > 100000)
                            throw new WeaponFileException($"{at}.{c.Name}: {v} isn't a sensible price");
                        switch (c.Name)
                        {
                            case "box": t.BoxCost = v; break;
                            case "slotUpgrade": t.SlotUpgradeCost = v; break;
                            case "wall": t.WallCost = v; break;
                            default: throw new WeaponFileException($"{at}: unknown price \"{c.Name}\" (box, slotUpgrade, wall)");
                        }
                    }
                    break;
                default: throw new WeaponFileException($"{source}: unknown key \"{p.Name}\" (format, note, partDrops, dropChance, boxTiers, costs)");
            }
        }
        return t;
    }

    private static List<RoundRow> Rows(JsonElement e, string where, bool weights)
    {
        if (e.ValueKind != JsonValueKind.Array)
            throw new WeaponFileException($"{where}: expected a list of rows");
        var list = new List<RoundRow>();
        foreach (var row in e.EnumerateArray())
        {
            var from = 0;
            var w = new float[Enum.GetValues<Rarity>().Length];
            var chance = 0f;
            foreach (var p in row.EnumerateObject())
            {
                if (p.Name == "fromRound")
                    from = WeaponFile.Int(p.Value, $"{where}.fromRound");
                else if (p.Name == "chance" && !weights)
                    chance = WeaponFile.Number(p.Value, $"{where}.chance");
                else if (weights && Enum.TryParse<Rarity>(p.Name, true, out var tier) && Enum.IsDefined(tier))
                    w[(int)tier] = WeaponFile.Number(p.Value, $"{where}.{p.Name}");
                else
                    throw new WeaponFileException($"{where}: unknown key \"{p.Name}\" in a row");
            }
            if (from < 1 || (list.Count > 0 && from <= list[^1].FromRound))
                throw new WeaponFileException($"{where}: rows need \"fromRound\" 1 or more, in increasing order");
            if (w.Any(x => x < 0) || (weights && w.Sum() <= 0) || chance is < 0 or > 1)
                throw new WeaponFileException($"{where}: row from round {from} has a negative or empty weight, or a chance outside 0-1");
            list.Add(new RoundRow(from, w, chance));
        }
        return list;
    }
}
