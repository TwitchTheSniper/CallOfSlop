using System.Text.Json;

namespace CallOfSlop;

/// <summary>Crew members' names (Crew/Names.json, "call-of-slop-crew-names/1"): a bad file is refused with a reason and members are called "Crew".</summary>
public static class CrewNames
{
    public const string Format = "call-of-slop-crew-names/1";

    private static string[]? _names;

    public static string[] All => _names ??= Load(Path.Combine(AppFolders.Data, "Crew", "Names.json"));

    /// <summary>A member's name by its number.</summary>
    public static string Of(ushort number) => All.Length == 0 ? "Crew" : All[number % All.Length];

    public static string[] Load(string path)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var root = doc.RootElement;
            if (!root.TryGetProperty("format", out var f) || f.GetString() != Format)
                throw new InvalidDataException($"its format isn't \"{Format}\"");
            if (!root.TryGetProperty("names", out var list) || list.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("it has no \"names\" list");
            var names = new List<string>();
            foreach (var e in list.EnumerateArray())
                if (e.GetString() is { Length: > 0 and <= 24 } n && n.All(ch => ch is >= ' ' and <= '~'))
                    names.Add(n);
                else
                    Console.WriteLine($"[crew] REFUSED a name in {path}: empty, over 24 letters or not ASCII");
            return names.ToArray();
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException or UnauthorizedAccessException)
        {
            Console.WriteLine($"[crew] REFUSED {path}: {ex.Message}; crew are called \"Crew\"");
            return [];
        }
    }
}
