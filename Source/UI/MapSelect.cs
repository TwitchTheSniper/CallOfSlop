namespace CallOfSlop;

/// <summary>One tile in the map select screen's pack column: a real pack, or the virtual True Random pack.</summary>
public sealed class MapSelectPack
{
    /// <summary>What settings.txt stores as map_pack: the pack's name, or <see cref="MapPack.TrueRandomKey"/>.</summary>
    public required string Key;

    /// <summary>The name on the tile ("True Random" for the virtual pack).</summary>
    public required string Name;

    /// <summary>The real pack (null for True Random).</summary>
    public MapPack? Pack;

    /// <summary>The maps listed under "Random", smallest first (empty for True Random: its only entry is Random).</summary>
    public required Map[] Maps;

    /// <summary>The maps "Random" draws from (the pack's maps, or every map of every pack in True Random).</summary>
    public required Map[] Pool;

    /// <summary>Up to four maps for the tile's picture, spread over the pack (True Random: one from each of its packs).</summary>
    public required Map[] Collage;

    /// <summary>Second line of the tile, e.g. "24 maps".</summary>
    public required string CountLine;

    /// <summary>Third line, e.g. "7 small, 9 medium, 8 large".</summary>
    public required string SizeLine;

    public bool IsTrueRandom => Pack == null;
}

/// <summary>
/// The logic behind the map select screen (see UiMapSelect.cs): the pack column (packs in pack.json order, Dev last, then
/// True Random), each pack's entries (entry 0 is always "Random", the rest are its maps), and turning a choice into
/// settings and back. Built once per map list (rebuilt after "Refresh maps"), so drawing allocates nothing.
/// </summary>
public static class MapSelect
{
    private static MapPack[]? _source;
    private static List<MapSelectPack> _cached = new();

    /// <summary>The pack column for the loaded maps (cached until the map list changes).</summary>
    public static IReadOnlyList<MapSelectPack> Current
    {
        get
        {
            var packs = CallOfSlop.Maps.Packs;
            if (!ReferenceEquals(packs, _source))
            {
                _cached = Build(packs, CallOfSlop.Maps.All);
                _source = packs;
            }
            return _cached;
        }
    }

    /// <summary>Builds the pack column for any set of packs and maps (the self-test uses its own).</summary>
    public static List<MapSelectPack> Build(IReadOnlyList<MapPack> packs, IReadOnlyList<Map> all)
    {
        var list = new List<MapSelectPack>();
        foreach (var p in MapPack.ListOrder(packs))
        {
            var maps = p.Maps.OrderBy(m => m.Size).ThenBy(m => m.Id).ToArray();
            list.Add(new MapSelectPack
            {
                Key = p.Name, Name = p.Name, Pack = p, Maps = maps, Pool = maps, Collage = Spread(maps, 4),
                CountLine = maps.Length == 1 ? "1 map" : $"{maps.Length} maps", SizeLine = p.SizeSummary,
            });
        }
        var included = MapPack.TrueRandomPacks(packs).ToList();
        var pool = MapPack.TrueRandomPool(packs, all).ToArray();
        list.Add(new MapSelectPack
        {
            Key = MapPack.TrueRandomKey, Name = MapPack.TrueRandomName, Pack = null, Maps = [], Pool = pool,
            Collage = included.Count >= 2 ? included.Take(4).Select(p => p.Maps.OrderBy(m => m.Size).ThenBy(m => m.Id).Skip(p.Maps.Count / 2).First()).ToArray() : Spread(pool, 4),
            CountLine = $"{pool.Length} maps from {included.Count} pack{(included.Count == 1 ? "" : "s")}",
            SizeLine = packs.Any(p => !p.InTrueRandom && p.Maps.Count > 0) ? $"Any pack but {string.Join(", ", packs.Where(p => !p.InTrueRandom && p.Maps.Count > 0).Select(p => p.Name))}" : "Any map of any pack",
        });
        return list;
    }

    /// <summary>Up to <paramref name="n"/> maps spread evenly over a list (so a pack's picture shows small and large maps).</summary>
    public static Map[] Spread(IReadOnlyList<Map> maps, int n)
    {
        var k = Math.Min(n, maps.Count);
        var r = new Map[k];
        for (var i = 0; i < k; i++)
            r[i] = maps[(int)((i + 0.5f) * maps.Count / k)];
        return r;
    }

    /// <summary>The tile index for a map_pack value (an unknown pack: the first tile).</summary>
    public static int IndexOf(IReadOnlyList<MapSelectPack> list, string key)
    {
        for (var i = 0; i < list.Count; i++)
            if (list[i].Key.Equals(key.Trim(), StringComparison.OrdinalIgnoreCase))
                return i;
        return 0;
    }

    /// <summary>
    /// Where the screen opens for the current settings: the saved pack with "Random" selected, or, when a map was chosen,
    /// the pack holding that map with the map selected.
    /// </summary>
    public static (int Pack, int Entry) FromSettings(Settings s, IReadOnlyList<MapSelectPack> list)
    {
        var pack = IndexOf(list, s.MapPack);
        if (s.MapRandom)
            return (pack, 0);
        for (var pi = 0; pi < list.Count; pi++)
        {
            // Prefer the saved pack (a map is only ever in one pack, but be safe after renames).
            var p = list[(pack + pi) % list.Count];
            var i = Array.FindIndex(p.Maps, m => m.Name.Equals(s.MapName, StringComparison.OrdinalIgnoreCase));
            if (i >= 0)
                return ((pack + pi) % list.Count, i + 1);
        }
        return (pack, 0);
    }

    /// <summary>Puts a choice into the settings: entry 0 = random from the pack (True Random: from every pack), else that map.</summary>
    public static void Apply(Settings s, MapSelectPack p, int entry)
    {
        s.MapPack = p.Key;
        if (entry <= 0 || entry > p.Maps.Length)
        {
            s.MapRandom = true;
            return;
        }
        s.MapRandom = false;
        s.MapName = p.Maps[entry - 1].Name;
    }

    /// <summary>The current choice in words, e.g. "Random from Legacy", "True Random" or "Quarry (Legacy)".</summary>
    public static string ChoiceText(Settings s)
    {
        if (!s.MapRandom && CallOfSlop.Maps.Find(s.MapName) is { } m)
            return m.Pack is { } p ? $"{m.Name} ({p.Name})" : m.Name;
        if (MapPack.IsTrueRandom(s.MapPack))
            return $"{MapPack.TrueRandomName} - any map from any pack";
        return CallOfSlop.Maps.FindPack(s.MapPack) is { } pack ? $"Random from {pack.Name}" : $"Random from all maps (pack '{s.MapPack}' is missing)";
    }
}

/// <summary>
/// Selection state of the map select screen: the open pack and the selected entry (0 = Random). Opening a pack always
/// selects Random, its top entry.
/// </summary>
public struct MapSelectState
{
    public int Pack;
    public int Entry;

    /// <summary>Opens a pack: Random (entry 0) is pre-selected.</summary>
    public void OpenPack(int pack)
    {
        Pack = pack;
        Entry = 0;
    }

    /// <summary>Moves the selection within the open pack (keyboard), clamped to Random..last map.</summary>
    public void Step(int delta, IReadOnlyList<MapSelectPack> list)
    {
        if (list.Count == 0)
            return;
        Pack = Math.Clamp(Pack, 0, list.Count - 1);
        Entry = Math.Clamp(Entry + delta, 0, list[Pack].Maps.Length);
    }

    /// <summary>Opens the previous or next pack (keyboard), which selects its Random.</summary>
    public void StepPack(int delta, IReadOnlyList<MapSelectPack> list)
    {
        if (list.Count == 0)
            return;
        OpenPack(Math.Clamp(Pack + delta, 0, list.Count - 1));
    }
}
