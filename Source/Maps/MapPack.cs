using System.Text.Json;

namespace CallOfSlop;

public sealed partial class Map
{
    /// <summary>The pack (top-level folder in Maps) the map was loaded from. Local presentation only: the network never sees it.</summary>
    public MapPack? Pack;
}

/// <summary>
/// A map pack: one top-level folder in the Maps folder, with every map file inside it (subfolders included).
/// An optional pack.json in the folder gives the pack a name, description, author and sort order; without one
/// the folder name is the pack name. Map files lying loose in Maps itself form the "Other" pack.
/// Packs only decide how maps are grouped in the lobby, what "random" picks from and what the map vote offers;
/// servers and clients still identify maps by name + fingerprint.
/// </summary>
public sealed partial class MapPack
{
    /// <summary>The "format" value a pack.json should carry.</summary>
    public const string Format = "call-of-slop-pack/1";

    /// <summary>The metadata file's name. Never loaded as a map.</summary>
    public const string FileName = "pack.json";

    /// <summary>The pack holding map files that sit directly in the Maps folder.</summary>
    public const string LooseName = "Other";

    public required string Name;
    public string Description = "";
    public string Author = "";

    /// <summary>Sort order in the pack list (then by name). Default 1000; the loose-files pack sorts last.</summary>
    public int Order = 1000;

    /// <summary>
    /// Whether "True Random" (random from every pack) may pick this pack's maps. pack.json "trueRandom": false
    /// leaves a pack out (the Dev test maps); its maps can still be chosen directly or played as random from that pack.
    /// </summary>
    public bool InTrueRandom = true;

    /// <summary>The pack's folder, relative to the Maps folder (null for the loose-files pack).</summary>
    public string? Folder;

    /// <summary>The pack's maps, in the same order as <see cref="Maps.All"/>.</summary>
    public readonly List<Map> Maps = new();

    /// <summary>Map count by size, e.g. "7 small, 9 medium, 8 large" (sizes with no maps are left out).</summary>
    public string SizeSummary
    {
        get
        {
            var parts = new[] { (MapSize.Small, "small"), (MapSize.Medium, "medium"), (MapSize.Large, "large") }
                .Select(s => (Count: Maps.Count(m => m.Size == s.Item1), Label: s.Item2))
                .Where(s => s.Count > 0)
                .Select(s => $"{s.Count} {s.Label}");
            var text = string.Join(", ", parts);
            return text.Length > 0 ? text : "no maps";
        }
    }

    /// <summary>A random map from the pack.</summary>
    public Map PickRandom(Random rng) => Maps[rng.Next(Maps.Count)];

    /// <summary>
    /// Reads a pack.json. A broken file throws <see cref="MapFileException"/>; the loader then still uses the
    /// folder as a pack, named after the folder.
    /// </summary>
    public static MapPack Load(string json, string folder, string source)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        }
        catch (JsonException ex)
        {
            throw new MapFileException($"{source}: not valid JSON ({ex.Message})");
        }
        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new MapFileException($"{source}: should be a JSON object like {{ \"name\": \"My pack\" }}");
            JsonElement? Get(string name)
            {
                foreach (var p in root.EnumerateObject())
                    if (p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                        return p.Value;
                return null;
            }
            string Text(string name) => Get(name) is { ValueKind: JsonValueKind.String } e ? e.GetString()!.Trim() : "";
            var pack = new MapPack
            {
                Name = Text("name") is { Length: > 0 } n ? n : folder,
                Description = Text("description"),
                Author = Text("author"),
                Folder = folder,
            };
            if (Get("order") is { ValueKind: JsonValueKind.Number } o && o.TryGetInt32(out var order))
                pack.Order = order;
            if (Get("trueRandom") is { ValueKind: JsonValueKind.False })
                pack.InTrueRandom = false;
            return pack;
        }
    }
}
