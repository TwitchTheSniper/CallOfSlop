using System.Text.Json;

namespace CallOfSlop;

/// <summary>
/// The default start kit of a survival world (Constructs/SurvivalStart.json; docs/sandbox/CONSTRUCTS.md, "Creative or
/// Survival"): blueprint or part names, placed near the rack when the host picks Survival in the lobby. A map's
/// <c>"survivalStart"</c> replaces it. A bad file is refused with a reason (<see cref="Problem"/>;
/// <c>--check-construct-parts</c> prints it) and the world starts with nothing.
/// </summary>
public sealed class SurvivalStart
{
    public const string Format = "call-of-slop-survival-start/1";

    public readonly List<string> Items = new();
    public string Problem = "";

    public static string FilePath => Path.Combine(AppFolders.Data, "Constructs", "SurvivalStart.json");

    private static SurvivalStart? _current;

    /// <summary>The kit in use (loaded once).</summary>
    public static SurvivalStart Current => _current ??= Load(FilePath);

    public static SurvivalStart Load(string path)
    {
        var t = new SurvivalStart();
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("format", out var f) || f.GetString() != Format)
                throw new FormatException($"\"format\" must be \"{Format}\"");
            foreach (var e in root.GetProperty("items").EnumerateArray())
            {
                var name = e.GetString() ?? "";
                if (ConstructBuild.FindBlueprint(name) == null && ConstructParts.Find(name) == null)
                    throw new FormatException($"no blueprint or construct part named \"{name}\"");
                t.Items.Add(name);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or FormatException or KeyNotFoundException or InvalidOperationException)
        {
            t.Items.Clear();
            t.Problem = $"{Path.GetFileName(path)}: {ex.Message}";
        }
        return t;
    }
}
