using System.Text.Json;

namespace CallOfSlop;

/// <summary>
/// A way to begin the story (docs/story/STARTS.md; Story/Starts/*.json, format call-of-slop-start/1): the planetoid it
/// begins on, who the player is and their background, the opening they go through (Story/Opening), whether the warden's
/// induction follows, the faction and standing they start with, and the start kit. The Ministry of Slop's penal colony on
/// Glacier is the first and default one (Karl, 2026-11-13: "allow it such that there may be different starts to the game
/// on different planets/characters/backgrounds"). A start belongs to its planetoid: a survival world on another map plays
/// the default start, as every survival world did before starts.
/// </summary>
public sealed class StoryStart
{
    public const string Format = "call-of-slop-start/1";

    public string Id = ""; // the file's name (Story/Starts/PenalColony.json: "PenalColony"); what a save and the settings keep
    public string Name = ""; // on the Story page's chooser
    public int Order; // the chooser's order (low first)
    public bool Default; // the start a world without one plays (old saves, other maps)
    public bool Placeholder; // a rough start that only shows starts work (the chooser says so)
    public string Character = ""; // who you are ("Convict")
    public string Background = ""; // a line on who you were
    public string About = ""; // the Story page's paragraph
    public string Map = ""; // the planetoid it begins on (a Sandbox map)
    public string Opening = ""; // a file in Story/Opening ("" none: no card, no wake, no colony)
    public bool Induction; // the warden's induction (Story/Tutorial/Induction.json) follows the opening
    public string Faction = ""; // a faction (Story/Factions, by name) whose standing...
    public int Standing; // ...each player starts with, when it isn't 0 (else as the faction rules leave it)
    public string[]? Kit; // the start kit by name, or null: the shipped one (Constructs/SurvivalStart.json)
    public string Problem = "";

    private OpeningScript? _script;

    /// <summary>The opening's script (loaded once). A start with none, or a bad file, gives a script whose Problem says so: no opening.</summary>
    public OpeningScript Script => _script ??= Opening.Length == 0
        ? new OpeningScript { Problem = $"the {Name} start has no opening" }
        : OpeningScript.Load(Path.Combine(AppFolders.Data, "Story", "Opening", Opening));

    /// <summary>Today's start, built in: what plays when Story/Starts has nothing usable.</summary>
    public static StoryStart BuiltIn() => new()
    {
        Id = "PenalColony", Name = "Ministry convict", Default = true, Character = "Convict", Map = "Glacier",
        Opening = "Opening.json", Induction = true, Faction = "Ministry of Slop",
        About = "You crash on Glacier and wake in the Ministry of Slop's penal colony.",
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
            var tut = Text(root, "tutorial", "none");
            t.Induction = tut.Equals("Induction", StringComparison.OrdinalIgnoreCase)
                ? true
                : tut.Equals("none", StringComparison.OrdinalIgnoreCase)
                    ? false
                    : throw new FormatException($"\"tutorial\": \"Induction\" or \"none\" (the induction is the one tutorial so far; another would have to reach clients too)");
            t.Faction = Text(root, "faction", "");
            if (root.TryGetProperty("standing", out var st))
                t.Standing = Math.Clamp(st.GetInt32(), -100, 100);
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

    /// <summary>The start a survival world on <paramref name="map"/> plays when <paramref name="id"/> was asked for: that one on its own planetoid, else the default.</summary>
    public static StoryStart For(string id, string map) =>
        Find(id) is { } s && s.Map.Equals(map, StringComparison.OrdinalIgnoreCase) ? s : Default;

    /// <summary><c>--check-starts</c>: every start, what it holds, and the refused files. Exit code: the problems found.</summary>
    public static int Check()
    {
        var bad = Problems.Length;
        foreach (var p in Problems)
            Console.WriteLine($"refused {p}");
        foreach (var s in All)
        {
            Console.WriteLine($"{s.Id}: \"{s.Name}\"{(s.Default ? " (default)" : "")}{(s.Placeholder ? " (placeholder)" : "")} on {s.Map}, {s.Character}; "
                              + $"opening {(s.Opening.Length > 0 ? s.Opening : "none")}, {(s.Induction ? "the induction" : "no tutorial")}"
                              + (s.Faction.Length > 0 ? $", {s.Faction} {s.Standing}" : "") + (s.Kit != null ? $", kit of {s.Kit.Length}" : ""));
            if (Maps.Find(s.Map) is not { Sandbox: not null })
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
