namespace CallOfSlop;

/// <summary>
/// "True Random" and the random pools: what a random map is drawn from for a pack setting.
/// True Random is a virtual pack (no folder): every map of every pack that allows it (pack.json "trueRandom",
/// on by default; the Dev test pack turns it off). settings.txt stores it as map_pack=*all.
/// </summary>
public sealed partial class MapPack
{
    /// <summary>The map_pack value in settings.txt that means True Random. Can't clash with a folder name ('*' isn't allowed in one).</summary>
    public const string TrueRandomKey = "*all";

    /// <summary>What players see for True Random.</summary>
    public const string TrueRandomName = "True Random";

    /// <summary>Is this map_pack value True Random?</summary>
    public static bool IsTrueRandom(string? key) => string.Equals(key?.Trim(), TrueRandomKey, StringComparison.OrdinalIgnoreCase);

    /// <summary>The name to show for a map_pack value ("True Random" for <see cref="TrueRandomKey"/>).</summary>
    public static string DisplayName(string key) => IsTrueRandom(key) ? TrueRandomName : key;

    /// <summary>The packs True Random draws from, in list order.</summary>
    public static IEnumerable<MapPack> TrueRandomPacks(IReadOnlyList<MapPack> packs) => packs.Where(p => p.InTrueRandom && p.Maps.Count > 0);

    /// <summary>Every map True Random can pick: all maps of the packs that allow it (all maps, if no pack does).</summary>
    public static List<Map> TrueRandomPool(IReadOnlyList<MapPack> packs, IReadOnlyList<Map> all)
    {
        var pool = TrueRandomPacks(packs).SelectMany(p => p.Maps).ToList();
        return pool.Count > 0 ? pool : all.ToList();
    }

    /// <summary>
    /// The maps "random" picks from for a map_pack value: True Random's pool, the named pack's maps, or every map when
    /// the pack is gone (folder removed or renamed).
    /// </summary>
    public static IReadOnlyList<Map> RandomPool(string key, IReadOnlyList<MapPack> packs, IReadOnlyList<Map> all)
    {
        if (IsTrueRandom(key))
            return TrueRandomPool(packs, all);
        var pack = packs.FirstOrDefault(p => p.Name.Equals(key, StringComparison.OrdinalIgnoreCase) && p.Maps.Count > 0);
        return pack != null ? pack.Maps : all;
    }

    /// <summary>
    /// The packs in the order the map select screen lists them: by pack.json "order" (then name), except that packs left
    /// out of True Random (Dev) go last; True Random itself follows them (the screen adds it).
    /// </summary>
    public static List<MapPack> ListOrder(IReadOnlyList<MapPack> packs) =>
        packs.Where(p => p.InTrueRandom).Concat(packs.Where(p => !p.InTrueRandom)).Where(p => p.Maps.Count > 0).ToList();
}
