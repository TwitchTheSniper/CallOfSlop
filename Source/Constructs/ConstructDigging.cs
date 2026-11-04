using System.Text.Json;

namespace CallOfSlop;

/// <summary>
/// The ground drill's numbers (Constructs/Digging.json; docs/sandbox/TERRAIN-DIGGING.md, "Sculpting"): reach, the scoop's
/// size and rate, what each terrain material gives, and the units of carried material a cubic metre of ground is worth, both
/// dug out and put back. A bad file is refused with a reason (<see cref="Problem"/>; <c>--check-construct-parts</c> prints it)
/// and the defaults are used.
/// </summary>
public sealed class DiggingTable
{
    public const string Format = "call-of-slop-digging/1";

    /// <summary>The material each <see cref="GroundKind"/> is carried as (RecyclingTable names).</summary>
    public static readonly string[] KindNames = ["dirt", "stone", "sand"];

    public float Reach = 5f, ScoopRadius = 0.8f, RepeatSeconds = 0.3f, StoneDepth = 3f, MaxAboveSurface = 16f, LogShare = 0.75f;

    /// <summary>Units of carried material per cubic metre, by <see cref="GroundKind"/>.</summary>
    public readonly float[] Units = [10f, 10f, 10f];

    /// <summary>How hard each <see cref="GroundKind"/> holds a scoop back, against dirt (docs/sandbox/CONSTRUCTS.md "Scoops").</summary>
    public readonly float[] ScoopHardness = [1f, 3f, 0.7f];

    /// <summary>Scoops: how much harder the ground holds one that can't cut (full, or pushed any way but edge first); the shortest time between one scoop's bites; bites a step (10 Hz) in the whole world; their share of the log.</summary>
    public float ScoopFullForce = 8f, ScoopBiteSeconds = 0.25f, ScoopLogShare = 0.75f;
    public int ScoopOpsPerStep = 4;

    /// <summary>What each <see cref="TerrainMaterial"/> gives (grass, dirt, rock, sand).</summary>
    public readonly GroundKind[] Ground = [GroundKind.Dirt, GroundKind.Dirt, GroundKind.Stone, GroundKind.Sand];

    public string Problem = "";

    public static string FilePath => Path.Combine(AppFolders.Data, "Constructs", "Digging.json");

    private static DiggingTable? _current;

    /// <summary>The table in use (loaded once; the defaults when the file is missing or refused).</summary>
    public static DiggingTable Current => _current ??= Load(FilePath);

    /// <summary>Sets the table in use (the self-test; null: load the file again).</summary>
    public static void Use(DiggingTable? t) => _current = t;

    /// <summary>The log's length at which the tool stops.</summary>
    public int LogCap => (int)(TerrainEdits.MaxOps * LogShare);

    /// <summary>The log's length at which scoops stop.</summary>
    public int ScoopLogCap => (int)(TerrainEdits.MaxOps * ScoopLogShare);

    public static DiggingTable Load(string path)
    {
        try
        {
            return Parse(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new DiggingTable { Problem = $"{Path.GetFileName(path)}: {ex.Message}" };
        }
    }

    public static DiggingTable Parse(string json)
    {
        var t = new DiggingTable();
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("format", out var f) || f.GetString() != Format)
                throw new FormatException($"\"format\" must be \"{Format}\"");
            foreach (var p in root.EnumerateObject())
                switch (p.Name)
                {
                    case "format" or "about": break;
                    case "reach": t.Reach = Num(p.Value, p.Name, 1, 16); break;
                    case "scoopRadius": t.ScoopRadius = Num(p.Value, p.Name, 0.3f, 3); break;
                    case "repeatSeconds": t.RepeatSeconds = Num(p.Value, p.Name, 0.05f, 5); break;
                    case "stoneDepth": t.StoneDepth = Num(p.Value, p.Name, 0, 100); break;
                    case "maxAboveSurface": t.MaxAboveSurface = Num(p.Value, p.Name, 0, 200); break;
                    case "logShare": t.LogShare = Num(p.Value, p.Name, 0, 1); break;
                    case "scoops":
                        foreach (var q in p.Value.EnumerateObject())
                            switch (q.Name)
                            {
                                case "fullForce": t.ScoopFullForce = Num(q.Value, "scoops.fullForce", 1, 100); break;
                                case "biteSeconds": t.ScoopBiteSeconds = Num(q.Value, "scoops.biteSeconds", 0.05f, 10); break;
                                case "logShare": t.ScoopLogShare = Num(q.Value, "scoops.logShare", 0, 1); break;
                                case "opsPerStep": t.ScoopOpsPerStep = (int)Num(q.Value, "scoops.opsPerStep", 1, 64); break;
                                case "hardness":
                                    foreach (var h in q.Value.EnumerateObject())
                                        t.ScoopHardness[Kind(h.Name, "scoops.hardness")] = Num(h.Value, $"scoops.hardness.{h.Name}", 0.1f, 100);
                                    break;
                                default: throw new FormatException($"unknown key \"scoops.{q.Name}\"");
                            }
                        break;
                    case "units":
                        foreach (var q in p.Value.EnumerateObject())
                            t.Units[Kind(q.Name, "units")] = Num(q.Value, $"units.{q.Name}", 0.1f, 1000);
                        break;
                    case "ground":
                        foreach (var q in p.Value.EnumerateObject())
                        {
                            if (!Enum.TryParse<TerrainMaterial>(q.Name, true, out var m) || !Enum.IsDefined(m))
                                throw new FormatException($"\"ground\" names \"{q.Name}\", not a terrain material (grass, dirt, rock, sand)");
                            t.Ground[(int)m] = (GroundKind)Kind(q.Value.GetString() ?? "", $"ground.{q.Name}");
                        }
                        break;
                    default: throw new FormatException($"unknown key \"{p.Name}\"");
                }
            return t;
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException)
        {
            return new DiggingTable { Problem = ex.Message };
        }
    }

    private static int Kind(string name, string what)
    {
        var i = Array.IndexOf(KindNames, name);
        return i >= 0 ? i : throw new FormatException($"\"{what}\" must be dirt, stone or sand, not \"{name}\"");
    }

    private static float Num(JsonElement e, string what, float min, float max)
    {
        if (e.ValueKind != JsonValueKind.Number || e.GetSingle() < min || e.GetSingle() > max)
            throw new FormatException($"\"{what}\" is a number from {min} to {max}");
        return e.GetSingle();
    }
}
