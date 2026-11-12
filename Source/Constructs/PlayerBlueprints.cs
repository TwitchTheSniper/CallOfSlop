using System.Numerics;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>
/// The player's own blueprints (docs/sandbox/CONSTRUCTS.md, "Build stations"): one shipped-format file each in the save folder's
/// <see cref="FolderName"/> folder (<see cref="SavePaths.SaveFolders"/>), named after the blueprint, written atomically with a
/// .bak like every save. They go by part name, so a file works in any world that has those parts.
/// </summary>
public static class PlayerBlueprints
{
    public const string FolderName = "ConstructBlueprints";
    public const int MaxName = 32;

    public static string Folder => Path.Combine(SavePaths.Folder, FolderName);

    /// <summary>One saved blueprint as the Tab inventory lists it.</summary>
    public sealed record Entry(string Name, string File, int Parts, string Search);

    private static readonly string[] Reserved = ["con", "prn", "aux", "nul", "com1", "com2", "com3", "com4", "com5", "com6", "com7", "com8", "com9", "lpt1", "lpt2", "lpt3", "lpt4", "lpt5", "lpt6", "lpt7", "lpt8", "lpt9"];

    /// <summary>Why a name can't be a blueprint's (it is a file name on every system), or null.</summary>
    public static string? NameRefusal(string name)
    {
        if (name.Length == 0)
            return "give it a name";
        if (name.Length > MaxName)
            return $"at most {MaxName} characters";
        if (name[0] == ' ' || name[^1] == ' ' || name[^1] == '.')
            return "a name can't start or end with a space, or end with a dot";
        foreach (var ch in name)
            if (!(char.IsAsciiLetterOrDigit(ch) || ch is ' ' or '-' or '_' or '(' or ')' or '.' or '\''))
                return "letters, digits, spaces and - _ ( ) . ' only";
        if (Reserved.Contains(name.ToLowerInvariant()))
            return "that name is reserved by the system";
        return null;
    }

    public static string PathOf(string name) => Path.Combine(Folder, name + ".json");

    public static bool Exists(string name) => File.Exists(PathOf(name));

    /// <summary>Writes one (call <see cref="NameRefusal"/> first). Null, or why not.</summary>
    public static string? Write(string name, string text) => WriteFile(PathOf(name), text);

    /// <summary>Writes a file of the library's folder as every save is (the panel designs' list: PanelDesigns.cs). Null, or why not.</summary>
    public static string? WriteFile(string path, string text)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            return SavePaths.WriteAtomic(path, text) ? null : "the file couldn't be written";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ex.Message;
        }
    }

    public static bool Delete(string name)
    {
        try
        {
            File.Delete(PathOf(name));
            File.Delete(SavePaths.BackupOf(PathOf(name)));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Every readable one, by name: a look at each file's name and step count, for when the list opens. A bad file is left out.</summary>
    public static List<Entry> List()
    {
        var list = new List<Entry>();
        if (!Directory.Exists(Folder))
            return list;
        foreach (var file in Directory.GetFiles(Folder, "*.json"))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
                var root = doc.RootElement;
                var steps = root.TryGetProperty("steps", out var s) && s.ValueKind == JsonValueKind.Array ? s.GetArrayLength() : 0;
                var name = Path.GetFileNameWithoutExtension(file);
                var label = root.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() ?? name : name;
                list.Add(new Entry(name, file, steps, label.ToLowerInvariant()));
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                Console.WriteLine($"[sandbox] blueprint {Path.GetFileName(file)} skipped: {ex.Message}");
            }
        }
        list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return list;
    }
}

/// <summary>
/// A blueprint's text built into a clip on this PC, a few steps a frame: to check a save builds and to draw a stamp's ghost. The
/// text is read away from the game thread; the steps run on it, inside a time budget.
/// </summary>
public sealed class BlueprintLoad
{
    public const double BudgetMs = 3;

    private readonly string _text;
    private Task<ConstructBuild.Blueprint>? _parse;
    private ConstructWorld? _world;
    private IEnumerator<int>? _steps;
    private ConstructBuild.BuildRun? _run;
    private string _name = "";

    public ConstructClip? Clip { get; private set; }
    public string Why { get; private set; } = "";
    public bool Done { get; private set; }

    public BlueprintLoad(string text, string file)
    {
        _text = text;
        _parse = Task.Run(() => ConstructBuild.ParseBlueprint(text, file));
    }

    public string Text => _text;

    /// <summary>One frame's work. True when it is done (see <see cref="Clip"/> and <see cref="Why"/>).</summary>
    public bool Step()
    {
        if (Done)
            return true;
        if (_parse is { } parse)
        {
            if (!parse.IsCompleted)
                return false;
            _parse = null;
            if (parse.Exception is { } ex)
            {
                Why = ex.InnerException?.Message ?? ex.Message;
                return Done = true;
            }
            var bp = parse.Result;
            _name = bp.Name;
            if (bp.Steps.Count > ConstructClip.MaxParts)
            {
                Why = "it is too big";
                return Done = true;
            }
            _world = new ConstructWorld();
            _run = new ConstructBuild.BuildRun();
            _steps = ConstructBuild.BuildSteps(_world, bp, Vector3.Zero, Quaternion.Identity, _run).GetEnumerator();
        }
        var until = System.Diagnostics.Stopwatch.GetTimestamp() + (long)(BudgetMs * System.Diagnostics.Stopwatch.Frequency / 1000);
        while (System.Diagnostics.Stopwatch.GetTimestamp() < until)
            if (!_steps!.MoveNext())
            {
                if (_run!.Built is { } built)
                    Clip = ConstructClip.Of(built, _name, _world);
                else
                    Why = _run.Why;
                _world = null;
                return Done = true;
            }
        return false;
    }
}
