using System.Text.Json;

namespace CallOfSlop;

/// <summary>
/// A way to begin a new world (docs/story/STARTS.md; Story/Starts/*.json, format call-of-slop-start/1): the planetoid it
/// begins on (or any map), who the player is and their background, the opening they go through (Story/Opening), the
/// faction and standing they start with, and the start kit. The menus offer them in order for Survival and Creative, the
/// rust bucket first (Karl, 2026-11-14). A start belongs to its planetoid: a new world on another map plays the any-map
/// start, and a world saved without a start (one from before starts: an old penal colony world, the colony removed
/// 2026-11-17) plays <see cref="StoryStarts.Legacy"/>, which has no opening.
/// </summary>
public sealed class StoryStart
{
    public const string Format = "call-of-slop-start/1";

    public string Id = ""; // the file's name (Story/Starts/RustBucket.json: "RustBucket"); what a save and the settings keep
    public string Name = ""; // on the Story page's chooser
    public int Order; // the chooser's order (low first)
    public bool Default; // the start the menus fall back to (the rust bucket); a world saved without one plays StoryStarts.Legacy
    public bool Placeholder; // a rough start that only shows starts work (the chooser says so)
    public string Character = ""; // who you are ("Spacer")
    public string Background = ""; // a line on who you were
    public string About = ""; // the Story page's paragraph
    public string Map = ""; // the planetoid it begins on (a Sandbox map), or "any": the map picked on the menu
    public bool AnyMap => Map.Equals("any", StringComparison.OrdinalIgnoreCase);
    public string Opening = ""; // a file in Story/Opening ("" none: no card, no wake)
    public string[]? Kit; // the start kit by name, or null: the shipped one (Constructs/SurvivalStart.json)
    public string Problem = "";

    private OpeningScript? _script;

    /// <summary>The opening's script (loaded once). A start with none, or a bad file, gives a script whose Problem says so: no opening.</summary>
    public OpeningScript Script => _script ??= Opening.Length == 0
        ? new OpeningScript { Problem = $"the {Name} start has no opening" }
        : OpeningScript.Load(Path.Combine(AppFolders.Data, "Story", "Opening", Opening));

    /// <summary>Today's start, built in: what plays when Story/Starts has nothing usable (the rust bucket).</summary>
    public static StoryStart BuiltIn() => new()
    {
        Id = "RustBucket", Name = "Rust bucket", Default = true, Character = "Spacer", Map = "Glacier",
        Opening = "RustBucket.json",
        About = "You wake aboard the Rust Bucket, a patched two-deck freighter coasting round Glacier.",
    };

    public static StoryStart Load(string path)
    {
        var t = new StoryStart { Id = Path.GetFileNameWithoutExtension(path) };
        t.Name = t.Id;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("format", out var f) || f.GetString() != Format)
                throw new FormatException($"\"format\" must be \"{Format}\"");
            t.Name = Text(root, "name", t.Name);
            t.Character = Text(root, "character", "");
            t.Background = Text(root, "background", "");
            t.About = Text(root, "about", "");
            t.Map = Text(root, "map", "");
            if (t.Map.Length == 0)
                throw new FormatException("no \"map\" (the planetoid it begins on)");
            t.Opening = Text(root, "opening", "");
            if (t.Opening.Equals("none", StringComparison.OrdinalIgnoreCase))
                t.Opening = "";
            if (t.Opening.Length > 0 && (t.Opening.IndexOfAny(['/', '\\']) >= 0 || !File.Exists(Path.Combine(AppFolders.Data, "Story", "Opening", t.Opening))))
                throw new FormatException($"\"opening\": no file Story/Opening/{t.Opening}");
            if (!Text(root, "tutorial", "none").Equals("none", StringComparison.OrdinalIgnoreCase))
                throw new FormatException("\"tutorial\": \"none\" (the warden's induction went with the penal colony, 2026-11-17)");
            if (root.TryGetProperty("order", out var o))
                t.Order = o.GetInt32();
            t.Default = root.TryGetProperty("default", out var d) && d.GetBoolean();
            t.Placeholder = root.TryGetProperty("placeholder", out var ph) && ph.GetBoolean();
            if (root.TryGetProperty("kit", out var kit))
                t.Kit = [.. kit.EnumerateArray().Select(k => k.GetString() ?? "").Where(k => k.Length > 0)];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or FormatException or KeyNotFoundException or InvalidOperationException)
        {
            t.Problem = $"{Path.GetFileName(path)}: {ex.Message}";
        }
        return t;
    }

    private static string Text(JsonElement e, string key, string fallback) =>
        e.TryGetProperty(key, out var v) ? Ascii(v.GetString() ?? "", key) : fallback;

    private static string Ascii(string s, string where) =>
        s.All(c => c >= ' ' && c < 127) ? s : throw new FormatException($"{where}: player-visible text is ASCII (\"{s}\")");
}

/// <summary>Every start in Story/Starts (loaded once), the default first among equals.</summary>
public static class StoryStarts
{
    public static string Folder => Path.Combine(AppFolders.Data, "Story", "Starts");

    private static StoryStart[]? _all;
    private static StoryStart? _default;
    private static string[] _problems = [];

    /// <summary>The usable starts, in the chooser's order (never empty: the built-in one when the folder has none).</summary>
    public static StoryStart[] All
    {
        get
        {
            if (_all == null)
                Load();
            return _all!;
        }
    }

    /// <summary>The refused files and why.</summary>
    public static string[] Problems
    {
        get
        {
            if (_all == null)
                Load();
            return _problems;
        }
    }

    public static StoryStart Default
    {
        get
        {
            if (_all == null)
                Load();
            return _default!;
        }
    }

    private static void Load()
    {
        var files = Directory.Exists(Folder) ? Directory.GetFiles(Folder, "*.json").OrderBy(p => p, StringComparer.Ordinal).ToArray() : [];
        var loaded = files.Select(StoryStart.Load).ToArray();
        _problems = [.. loaded.Where(s => s.Problem.Length > 0).Select(s => s.Problem)];
        foreach (var p in _problems)
            Console.WriteLine($"[starts] refused {p}");
        var ok = loaded.Where(s => s.Problem.Length == 0).OrderBy(s => s.Order).ThenBy(s => s.Id, StringComparer.Ordinal).ToArray();
        if (ok.Length == 0)
            ok = [StoryStart.BuiltIn()];
        _default = ok.FirstOrDefault(s => s.Default) ?? ok[0];
        _all = ok;
    }

    /// <summary>A start by its id (any case), or null.</summary>
    public static StoryStart? Find(string id) =>
        id.Length == 0 ? null : Array.Find(All, s => s.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// What a world saved without a start plays (Id ""): one from before starts, which was a penal colony world (the
    /// colony and its induction were removed, 2026-11-17). It has no opening: the world's constructs stay as they were,
    /// and a late friend wakes where a new player spawns.
    /// </summary>
    public static readonly StoryStart Legacy = new() { Id = "", Name = "Old world", Map = "any" };

    /// <summary>
    /// The start a fresh world on <paramref name="map"/> plays when <paramref name="id"/> was asked for: that one on its own
    /// planetoid (or any map's), else the any-map start (a new world taken to another map, or a world saved with a start
    /// that is gone: the archived Castaway and the penal colony), else the default. Nothing asked (a tool's or a test's
    /// world) is the default on its planetoid, else the any-map start. A loaded world saved without a start plays
    /// <see cref="Legacy"/> instead (Game.StartLoad).
    /// </summary>
    public static StoryStart For(string id, string map) =>
        Find(id.Length == 0 ? Default.Id : id) is not { } s ? Array.Find(All, a => a.AnyMap) ?? Default
        : s.AnyMap || s.Map.Equals(map, StringComparison.OrdinalIgnoreCase) ? s
        : Array.Find(All, a => a.AnyMap) ?? Default;

    /// <summary>The starts the menus offer for a new world, in order.</summary>
    public static StoryStart[] Offered => All;

    /// <summary>The start the menus show picked for <paramref name="id"/>: that one when it is offered, else the first offered.</summary>
    public static StoryStart Picked(string id) => Find(id) ?? All[0];

    /// <summary><c>--check-starts</c>: every start, what it holds, and the refused files. Exit code: the problems found.</summary>
    public static int Check()
    {
        var bad = Problems.Length;
        foreach (var p in Problems)
            Console.WriteLine($"refused {p}");
        foreach (var s in All)
        {
            Console.WriteLine($"{s.Id}: \"{s.Name}\"{(s.Default ? " (default)" : "")}{(s.Placeholder ? " (placeholder)" : "")} on {s.Map}, {s.Character}; "
                              + $"opening {(s.Opening.Length > 0 ? s.Opening : "none")}"
                              + (s.Kit != null ? $", kit of {s.Kit.Length}" : ""));
            if (!s.AnyMap && Maps.Find(s.Map) is not { Sandbox: not null })
            {
                Console.WriteLine($"  problem: no Sandbox map named {s.Map}");
                bad++;
            }
            if (s.Opening.Length > 0 && s.Script.Problem.Length > 0)
            {
                Console.WriteLine($"  problem: {s.Script.Problem}");
                bad++;
            }
            foreach (var k in s.Kit ?? [])
                if (ConstructBuild.FindBlueprint(k) == null && ConstructParts.Find(k) == null)
                {
                    Console.WriteLine($"  problem: kit: no blueprint or part named {k}");
                    bad++;
                }
        }
        Console.WriteLine(bad == 0 ? "starts: ok" : $"starts: {bad} problem(s)");
        return bad;
    }
}
