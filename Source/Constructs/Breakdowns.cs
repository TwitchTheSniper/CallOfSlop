using System.Text.Json;

namespace CallOfSlop;

/// <summary>
/// The rust bucket's breakdowns, as data (Constructs/Breakdowns.json; the game's side is Game/GameSandboxBreakdowns.cs): how
/// often a part starts to go, how long it warns first and what a repair costs. A bad file is refused with its reason, and
/// then no breakdowns happen.
/// </summary>
public sealed class Breakdowns
{
    public const string Format = "call-of-slop-breakdowns/1";

    public float MinMinutes = 8, MaxMinutes = 14, FirstMinutes = 4, WarnSeconds = 90;
    public string RepairMaterial = "metal";
    public int RepairCost = 3, MaxFailed = 2;
    public string Problem = "";

    public static string FilePath => Path.Combine(AppFolders.Data, "Constructs", "Breakdowns.json");

    private static Breakdowns? _current;

    /// <summary>The table in use (loaded once).</summary>
    public static Breakdowns Current => _current ??= Load(FilePath);

    public static Breakdowns Load(string path)
    {
        var t = new Breakdowns();
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("format", out var f) || f.GetString() != Format)
                throw new FormatException($"\"format\" must be \"{Format}\"");
            t.MinMinutes = Num(root, "minMinutes", 0.5f, 600);
            t.MaxMinutes = Num(root, "maxMinutes", t.MinMinutes, 600);
            t.FirstMinutes = Num(root, "firstMinutes", 0.1f, 600);
            t.WarnSeconds = Num(root, "warnSeconds", 10, 600);
            t.RepairMaterial = root.GetProperty("repairMaterial").GetString() ?? "";
            if (RecyclingTable.Current.IdOf(t.RepairMaterial) == 0)
                throw new FormatException($"\"repairMaterial\": no material named \"{t.RepairMaterial}\" in Recycling.json");
            t.RepairCost = (int)Num(root, "repairCost", 0, 1000);
            t.MaxFailed = (int)Num(root, "maxFailed", 1, 64);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or FormatException or KeyNotFoundException or InvalidOperationException)
        {
            t.Problem = $"{Path.GetFileName(path)}: {ex.Message}";
            Console.WriteLine($"[breakdowns] REFUSED {t.Problem}: no breakdowns");
        }
        return t;
    }

    private static float Num(JsonElement root, string key, float min, float max)
    {
        var v = root.GetProperty(key).GetSingle();
        if (!float.IsFinite(v) || v < min || v > max)
            throw new FormatException($"\"{key}\" is {v}, not between {min} and {max}");
        return v;
    }
}
