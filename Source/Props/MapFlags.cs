using System.Text.Json;

namespace CallOfSlop;

/// <summary>
/// Which flag design each flag prop on a map flies (see <see cref="FlagPool"/> and Props/README.md "Flags"): the
/// placement's own "flag", else the prop's default ("random" for a decorative flag); "random" picks from the map's pool
/// (its "flags" list, or every design) by position. Worked out once when the map has loaded, the same on every PC.
/// Cosmetic only: the colliders, the prop's name and so the map's fingerprint stay exactly as they were.
/// </summary>
public sealed partial class Map
{
    /// <summary>The map's "flags": the designs random flags pick from (null: every design in Props/Flags/flags.json; empty: none, flags keep their own cloth).</summary>
    public string[]? FlagPoolIds;

    private readonly Dictionary<int, string> _flagRequests = new();

    /// <summary>How many flag props fly a design from the pool (worked out by <see cref="ResolveFlags"/>).</summary>
    public int FlagsFlying { get; private set; }

    /// <summary>The designs random flags on this map pick from, in pool order (only ones that exist).</summary>
    public string[] FlagPoolInUse => FlagPoolIds == null ? FlagPool.Ids : FlagPoolIds.Where(id => FlagPool.Find(id) != null).ToArray();

    /// <summary>The "flag" a PlaceProp op gave prop <paramref name="i"/>, or null.</summary>
    public string? FlagRequest(int i) => _flagRequests.TryGetValue(i, out var f) ? f : null;

    /// <summary>Records a PlaceProp op's "flag" for the prop it just placed (checked here, so a typo is a map error).</summary>
    private void RequestFlag(PropDef def, string flag)
    {
        if (string.IsNullOrWhiteSpace(flag))
            return;
        flag = flag.Trim().ToLowerInvariant();
        if (def.FlagCloth == null)
            throw new InvalidOperationException($"\"flag\" is only for flag props (a prop file with a \"flag\" block); \"{def.Name}\" has none");
        if (!FlagPool.IsChoice(flag))
            throw new InvalidOperationException($"unknown flag \"{flag}\": {FlagPool.Known}");
        _flagRequests[PropInstances.Count - 1] = flag;
    }

    /// <summary>
    /// Swaps every flag prop for the variant flying its design (called by <see cref="FinishProps"/>). Deterministic: the
    /// placement's "flag", else the prop's default; "random" hashes the prop's position into the map's pool.
    /// </summary>
    public void ResolveFlags()
    {
        FlagsFlying = 0;
        string[]? pool = null;
        for (var i = 0; i < PropInstances.Count; i++)
        {
            var p = PropInstances[i];
            var def = p.Def.FlagBase ?? p.Def;
            if (def.FlagCloth is not { } cloth)
                continue;
            var want = FlagRequest(i) ?? cloth.Default;
            string? id;
            if (want == FlagPool.None)
                id = null;
            else if (want == FlagPool.Random)
            {
                pool ??= FlagPoolInUse;
                id = pool.Length == 0 ? null : pool[FlagPool.Pick(p.Position, pool.Length)];
            }
            else
                id = want;
            var shown = def;
            if (id != null)
            {
                if (FlagPool.Variant(def, id) is { } v)
                    shown = v;
                else
                    FlagPool.WarnOnce($"'{def.Name}' on {Name}: no flag design \"{id}\" (Props/{FlagPool.FolderName}/{FlagPool.FileName}), it keeps its own cloth");
            }
            if (shown != def)
                FlagsFlying++;
            if (shown != p.Def)
                PropInstances[i] = p with { Def = shown, Bounds = WorldBounds(shown.Bounds, p.Position, p.Yaw, p.Scale) };
        }
    }

    /// <summary>Reads a map file's "flags": a list of design ids, "none" (flags keep their own cloth) or "all" (the default: every design).</summary>
    public static string[]? ReadFlagPool(JsonElement e, string source)
    {
        var where = $"{source} (\"flags\")";
        if (e.ValueKind == JsonValueKind.String)
            return e.GetString()!.Trim().ToLowerInvariant() switch
            {
                "none" => [],
                "all" or "random" => null,
                var s => throw new MapFileException($"{where}: a list of flag ids like [\"uk\", \"us\"], \"none\" or \"all\", got \"{s}\""),
            };
        if (e.ValueKind != JsonValueKind.Array)
            throw new MapFileException($"{where}: a list of flag ids like [\"uk\", \"us\"], \"none\" or \"all\"");
        var ids = new List<string>();
        foreach (var x in e.EnumerateArray())
        {
            var id = x.ValueKind == JsonValueKind.String ? x.GetString()!.Trim().ToLowerInvariant() : throw new MapFileException($"{where}: expected flag ids (text), got {x}");
            if (FlagPool.Find(id) == null)
                throw new MapFileException($"{where}: unknown flag \"{id}\"; Props/{FlagPool.FolderName}/{FlagPool.FileName} has {string.Join(", ", FlagPool.Ids.Select(i => $"\"{i}\""))}");
            if (!ids.Contains(id))
                ids.Add(id);
        }
        return ids.ToArray();
    }

    /// <summary>The "flags" value as map-file JSON (for <see cref="MapFile.ToJson"/>), or null when it's the default.</summary>
    public string? FlagPoolJson => FlagPoolIds == null ? null
        : FlagPoolIds.Length == 0 ? "\"none\"" : "[" + string.Join(", ", FlagPoolIds.Select(id => JsonSerializer.Serialize(id))) + "]";
}
