using System.Text;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>What completes a tutorial step (docs/sandbox/TUTORIAL.md). The server judges every cue; the client only reports the two it alone sees.</summary>
public enum TutorialCue : byte
{
    Wait, // the step's seconds pass
    Look, // the view turned this many radians
    Move, // walked this many metres
    Sprint, // sprinted this many seconds
    Jump,
    Crouch,
    Inventory, // the client: the inventory window opened
    ToolWheel, // the client: the tool wheel opened
    Hold, // a part in the hands
    Tool, // the step's tool out
    Place, // a part fitted on a socket or set down as a new construct
    Weld, // the attach tool joined two pieces (any weld)
    Link, // any routed link laid
    Power, // a power cable laid
    Drill, // the ground drill dug or filled
    Assembler, // an Assembler's recipe set, or something tipped into one
    Configure, // any part's settings set
    Stow, // a part stowed in the backpack
    Draw, // a part drawn from the backpack
    COUNT,
}

/// <summary>Where a player is in the induction. A byte on the wire and in the save: never renumber.</summary>
public enum TutorialState : byte
{
    None, // not started (a new player)
    Running,
    Finished,
    Skipped,
}

/// <summary>One step of the induction, as its file has it.</summary>
public sealed class TutorialStep
{
    public string Id = "", Title = "", Text = "", Warden = "";
    public TutorialCue Cue;
    public float Seconds, Amount;
    public ConstructTool Tool;
    public GameAction[] Keys = []; // the actions its text names, in order of first use
    public uint Gives; // the tools the warden hands over at this step ("gives"), a bit per ConstructTool
    public uint Issued; // every tool the player has by this step: this one's and all before it, and bare hands
    public bool GivesPack; // the warden hands over the backpack at this step ("gives": ["backpack"])
    public bool PackIssued; // the player has their backpack by this step (this one or one before it gave it)
}

/// <summary>
/// The tutorial's steps (Story/Tutorial/Induction.json, format call-of-slop-tutorial/1): loaded once, the same on every
/// peer. A bad file is refused with a reason and the game has no tutorial (Error says why). Text placeholders
/// <c>{Action}</c> are GameAction names, shown as the player's own key (<see cref="Fill"/>).
/// </summary>
public static class Tutorial
{
    public const string Format = "call-of-slop-tutorial/1";
    public const int MaxSteps = 64;

    public static TutorialStep[] Steps { get; private set; } = [];
    public static string Speaker { get; private set; } = "";
    public static string? Error { get; private set; }

    private static bool _loaded;

    /// <summary>The steps, loaded on first use.</summary>
    public static void EnsureLoaded()
    {
        if (_loaded)
            return;
        _loaded = true;
        var file = Path.Combine(AppFolders.Data, "Story", "Tutorial", "Induction.json");
        try
        {
            (Steps, Speaker) = Parse(File.ReadAllText(file));
            Error = null;
            Console.WriteLine($"[tutorial] {Steps.Length} steps");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or FormatException)
        {
            Steps = [];
            Error = $"Induction.json: {ex.Message}";
            Console.WriteLine($"[tutorial] refused: {Error}");
        }
    }

    /// <summary>A file's steps, or a FormatException with the reason.</summary>
    public static (TutorialStep[] Steps, string Speaker) Parse(string json)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var root = doc.RootElement;
        if (!root.TryGetProperty("format", out var f) || f.GetString() != Format)
            throw new FormatException($"format must be \"{Format}\"");
        var speaker = root.TryGetProperty("speaker", out var sp) ? Ascii(sp.GetString() ?? "", "speaker") : "";
        if (!root.TryGetProperty("steps", out var list) || list.ValueKind != JsonValueKind.Array)
            throw new FormatException("no \"steps\" list");
        var steps = new List<TutorialStep>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in list.EnumerateArray())
        {
            var n = steps.Count;
            string Str(string key, bool need)
            {
                if (e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String)
                    return Ascii(v.GetString() ?? "", $"steps[{n}].{key}");
                return need ? throw new FormatException($"steps[{n}] has no \"{key}\"") : "";
            }
            float Num(string key, float fallback) => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetSingle() : fallback;
            var s = new TutorialStep { Id = Str("id", true), Title = Str("title", true), Text = Str("text", true), Warden = Str("warden", false) };
            if (!ids.Add(s.Id))
                throw new FormatException($"steps[{n}]: the id \"{s.Id}\" is used twice");
            var cue = Str("cue", true);
            if (!Enum.TryParse<TutorialCue>(cue, true, out s.Cue) || s.Cue == TutorialCue.COUNT || int.TryParse(cue, out _))
                throw new FormatException($"steps[{n}] ({s.Id}): no such cue \"{cue}\"");
            s.Seconds = Num("seconds", 6);
            s.Amount = Num("amount", s.Cue switch { TutorialCue.Look => 3f, TutorialCue.Move => 6f, TutorialCue.Sprint => 1f, _ => 1f });
            if (s.Cue == TutorialCue.Wait && s.Seconds is not (> 0 and <= 120))
                throw new FormatException($"steps[{n}] ({s.Id}): seconds must be over 0 and at most 120");
            if (s.Amount is not (> 0 and <= 1000))
                throw new FormatException($"steps[{n}] ({s.Id}): amount must be over 0 and at most 1000");
            if (s.Cue == TutorialCue.Tool)
            {
                var tool = Str("tool", true);
                s.Tool = ToolNamed(tool) ?? throw new FormatException($"steps[{n}] ({s.Id}): no such tool \"{tool}\"");
            }
            if (e.TryGetProperty("gives", out var gives))
            {
                if (gives.ValueKind != JsonValueKind.Array)
                    throw new FormatException($"steps[{n}] ({s.Id}): \"gives\" is a list of tool names");
                foreach (var g in gives.EnumerateArray())
                {
                    var name = g.ValueKind == JsonValueKind.String ? g.GetString() ?? "" : "";
                    if (name.Equals("backpack", StringComparison.OrdinalIgnoreCase))
                    {
                        s.GivesPack = true; // (story mode starts without one: GameTutorial.cs TutorialPack)
                        continue;
                    }
                    s.Gives |= Bit(ToolNamed(name) ?? throw new FormatException($"steps[{n}] ({s.Id}): \"gives\": no such tool \"{name}\""));
                }
            }
            s.Issued = s.Gives | Bit(ConstructTool.Hands) | (steps.Count > 0 ? steps[^1].Issued : 0);
            s.PackIssued = s.GivesPack || steps.Count > 0 && steps[^1].PackIssued;
            if (s.Cue is TutorialCue.Stow or TutorialCue.Draw && !s.PackIssued)
                throw new FormatException($"steps[{n}] ({s.Id}): asks for the backpack, which no step up to it gives");
            if (s.Cue == TutorialCue.Tool && (s.Issued & Bit(s.Tool)) == 0)
                throw new FormatException($"steps[{n}] ({s.Id}): asks for the {ToolName(s.Tool)}, which no step up to it gives");
            s.Keys = KeysIn(s.Text, $"steps[{n}] ({s.Id})");
            steps.Add(s);
            if (steps.Count > MaxSteps)
                throw new FormatException($"more than {MaxSteps} steps");
        }
        if (steps.Count == 0)
            throw new FormatException("no steps");
        return (steps.ToArray(), speaker);
    }

    private static string Ascii(string s, string where)
    {
        foreach (var c in s)
            if (c is < ' ' or > '~')
                throw new FormatException($"{where}: player text is ASCII only");
        return s;
    }

    public static uint Bit(ConstructTool t) => 1u << (int)t;

    /// <summary>
    /// The tool a player in the induction gets when they ask for <paramref name="asked"/> (Karl, 2026-11-08: no gun and no
    /// tools until a step hands them over): it, bare hands for a gun or for putting a tool away, or null while it isn't
    /// issued yet. Outside a running induction every tool is theirs. Client and server ask the same.
    /// </summary>
    public static ConstructTool? Gate(TutorialState state, int step, ConstructTool asked)
    {
        if (state != TutorialState.Running || step >= Steps.Length)
            return asked;
        if (asked == ConstructTool.None)
            return ConstructTool.Hands;
        return (Steps[step].Issued & Bit(asked)) != 0 ? asked : null;
    }

    /// <summary>What a refused tool says.</summary>
    public static string NotIssued(ConstructTool t) => $"{(Speaker.Length > 0 ? Speaker : "The warden")} hasn't issued you the {ToolName(t)} yet";

    /// <summary>A tool as the warden calls it in running text.</summary>
    public static string ToolName(ConstructTool t) => t switch
    {
        ConstructTool.Welder or ConstructTool.Wrench => "attach tool",
        ConstructTool.Linker => "linking tool",
        ConstructTool.GroundDrill => "ground drill",
        ConstructTool.PhysicsGun => "physics gun",
        ConstructTool.NoCollide => "no-collide tool",
        ConstructTool.Texture => "texture paint tool",
        ConstructTool.Material => "material painter",
        ConstructTool.Tracks => "track tool",
        ConstructTool.Hands => "hands",
        _ => t.Label() + " tool",
    };

    private static ConstructTool? ToolNamed(string name) => name.ToLowerInvariant() switch
    {
        "attach" or "welder" => ConstructTool.Welder,
        "linker" or "linking" => ConstructTool.Linker,
        "configure" => ConstructTool.Configure,
        "grounddrill" or "drill" => ConstructTool.GroundDrill,
        "paint" => ConstructTool.Paint,
        "remover" => ConstructTool.Remover,
        "selector" => ConstructTool.Selector,
        "hands" => ConstructTool.Hands,
        "tracks" => ConstructTool.Tracks,
        "nudge" => ConstructTool.Nudge,
        "nocollide" => ConstructTool.NoCollide,
        "texture" => ConstructTool.Texture,
        "material" => ConstructTool.Material,
        "fuse" => ConstructTool.Fuse,
        "physicsgun" => ConstructTool.PhysicsGun,
        _ => null,
    };

    /// <summary>The actions a text's {Action} placeholders name; an unknown one or an unclosed brace refuses the file.</summary>
    private static GameAction[] KeysIn(string text, string where)
    {
        var keys = new List<GameAction>();
        for (var i = text.IndexOf('{'); i >= 0; i = text.IndexOf('{', i + 1))
        {
            var end = text.IndexOf('}', i);
            if (end < 0)
                throw new FormatException($"{where}: a '{{' with no '}}'");
            var name = text[(i + 1)..end];
            if (!Enum.TryParse<GameAction>(name, false, out var a) || a == GameAction.COUNT || int.TryParse(name, out _))
                throw new FormatException($"{where}: no such key action \"{name}\" (a GameAction name, Keybinds.cs)");
            if (!keys.Contains(a))
                keys.Add(a);
        }
        return keys.ToArray();
    }

    /// <summary>The cues only the client sees (the windows it opens); it reports them, and the server takes them only for the current step.</summary>
    public static bool ClientCue(TutorialCue c) => c is TutorialCue.Inventory or TutorialCue.ToolWheel;

    /// <summary>A text with each {Action} as the player's key ([Tab]); null settings: the defaults.</summary>
    public static string Fill(string text, Settings? s)
    {
        if (text.IndexOf('{') < 0)
            return text;
        var b = new StringBuilder(text.Length + 16);
        var at = 0;
        for (var i = text.IndexOf('{'); i >= 0; i = text.IndexOf('{', at))
        {
            var end = text.IndexOf('}', i);
            if (end < 0)
                break;
            b.Append(text, at, i - at);
            if (Enum.TryParse<GameAction>(text[(i + 1)..end], false, out var a))
                b.Append('[').Append(Keybinds.NameOf(s, a)).Append(']');
            at = end + 1;
        }
        b.Append(text, at, text.Length - at);
        return b.ToString();
    }

    /// <summary>A number that changes when any key a step names is rebound (the HUD fills its text again).</summary>
    public static int BindsStamp(TutorialStep step, Settings? s)
    {
        var h = 17;
        foreach (var a in step.Keys)
            h = h * 31 + (s != null ? s.Bind(a) : Keybinds.Defaults[(int)a]);
        return h;
    }
}
