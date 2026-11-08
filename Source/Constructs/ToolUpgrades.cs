using System.Text.Json;

namespace CallOfSlop;

/// <summary>
/// What a tool upgrade raises (docs/sandbox/TOOL-UPGRADES.md). The byte is a column of a player's levels on the wire and in a
/// saved world (<see cref="ToolUpgrades.Slots"/> of them): a new stat goes at the end and none is ever renumbered.
/// </summary>
public enum ToolStat : byte
{
    Lift, // the physics gun: the heaviest thing it takes (kg); its pull goes with it (ToolUpgrades.PullPerKg)
    Reach, // the physics gun: how far the beam reaches (m)
    DrillBit, // the ground drill: the scoop's radius, times the digging table's
    DrillMotor, // the ground drill: scoops a second, times the digging table's
    Fitter, // the attach tool: timed fits, removals and welds this many times as fast
    Tractor, // the physics gun (a function, 0 or 1): the beam catches things out to twice its reach and reels them in
    Throw, // the physics gun (a function, 0 or 1): Reload while holding throws what it holds along the look
    COUNT,
}

/// <summary>
/// Per-player tool upgrades (docs/sandbox/TOOL-UPGRADES.md; data: Constructs/ToolUpgrades.json, written by
/// tools/constructs/upgrade_parts.py with the module parts and their recipes). Each <see cref="ToolStat"/> has a base value
/// (what every survivor starts with) and levels, each made by fitting one module part (F on it): an Assembler builds the
/// modules, some only once a faction has taught them. Creative gives every stat its <c>creative</c> value. A file that is
/// refused leaves the built-in table below (the same numbers) and says why in <see cref="Problem"/>.
/// </summary>
public sealed class ToolUpgrades
{
    public const string Format = "call-of-slop-tool-upgrades/1";

    /// <summary>The columns a player's levels have on the wire and in a save (room for stats still to come).</summary>
    public const int Slots = 16;

    /// <summary>The most levels a stat has above its base.</summary>
    public const int MaxLevels = 8;

    /// <summary>The physics gun's pull for each kilogram of its lift (N/kg): what it takes at its limit it still moves at 30 m/s2.</summary>
    public const float PullPerKg = 30f;

    public sealed class Stat
    {
        public ToolStat Id;
        public ConstructTool Tool;
        public string Name = "", Unit = "", About = "";
        public float Base, Creative;
        public bool Function; // on or off (base 0, one level of 1)
        public Level[] Levels = [];
    }

    public sealed class Level
    {
        public string Part = "";
        public float Value;
        public ushort PartId = ConstructParts.None; // (resolved against the part library when first asked)
    }

    public readonly Stat[] Stats = new Stat[(int)ToolStat.COUNT];
    public string Problem = "";
    private Dictionary<ushort, (ToolStat Stat, byte Level)>? _byPart;

    public static string FilePath => Path.Combine(AppFolders.Data, "Constructs", "ToolUpgrades.json");

    private static ToolUpgrades? _current;

    /// <summary>The table in use (loaded once; the built-in one when the file is missing or refused).</summary>
    public static ToolUpgrades Current => _current ??= Load(FilePath);

    /// <summary>Sets the table in use (null: load the file again).</summary>
    public static void Use(ToolUpgrades? t) => _current = t;

    public ToolUpgrades()
    {
        // The built-in table: what the file says, should it go missing.
        Put(ToolStat.Lift, ConstructTool.PhysicsGun, "Lift", "kg", ConstructGrab.SurvivalMaxMass, ConstructGrab.MaxMass, 3000, 6000, 12000, 25000);
        Put(ToolStat.Reach, ConstructTool.PhysicsGun, "Reach", "m", ConstructGrab.SurvivalRange, ConstructGrab.Range, 15, 18, 22, 26);
        Put(ToolStat.DrillBit, ConstructTool.GroundDrill, "Drill bit", "x", 1, 1.8f, 1.25f, 1.5f, 1.8f);
        Put(ToolStat.DrillMotor, ConstructTool.GroundDrill, "Drill motor", "x", 1, 2.25f, 1.35f, 1.75f, 2.25f);
        Put(ToolStat.Fitter, ConstructTool.Welder, "Fitting speed", "x", 1, 1, 1.5f, 2, 3);
        Put(ToolStat.Tractor, ConstructTool.PhysicsGun, "Tractor beam", "", 0, 1, 1);
        Put(ToolStat.Throw, ConstructTool.PhysicsGun, "Throw", "", 0, 1, 1);
    }

    private void Put(ToolStat id, ConstructTool tool, string name, string unit, float b, float creative, params float[] levels) =>
        Stats[(int)id] = new Stat
        {
            Id = id, Tool = tool, Name = name, Unit = unit, Base = b, Creative = creative, Function = unit.Length == 0,
            Levels = levels.Select(v => new Level { Value = v }).ToArray(),
        };

    public Stat Of(ToolStat s) => Stats[(int)s];

    /// <summary>A stat's value at a level (0: the base; past its top: the top).</summary>
    public float Value(ToolStat s, int level, bool creative = false)
    {
        var st = Stats[(int)s];
        if (creative)
            return st.Creative;
        if (level <= 0 || st.Levels.Length == 0)
            return st.Base;
        return st.Levels[Math.Min(level, st.Levels.Length) - 1].Value;
    }

    /// <summary>The module a part is, or null: which stat it raises and to which level (1 up).</summary>
    public (ToolStat Stat, byte Level)? ModuleOf(ushort partId)
    {
        if (_byPart == null)
        {
            var map = new Dictionary<ushort, (ToolStat, byte)>();
            foreach (var st in Stats)
                for (var k = 0; k < st.Levels.Length; k++)
                    if (ConstructParts.Find(st.Levels[k].Part) is { } d)
                    {
                        st.Levels[k].PartId = d.Id;
                        map[d.Id] = (st.Id, (byte)(k + 1));
                    }
                    else if (st.Levels[k].Part.Length > 0 && ConstructParts.All.Count > 0)
                        Console.WriteLine($"[upgrades] {st.Name} level {k + 1}: no part called \"{st.Levels[k].Part}\" (Constructs/ToolUpgrades.json)");
            if (ConstructParts.All.Count == 0)
                return null; // (the parts aren't loaded yet: asked again later)
            _byPart = map;
        }
        return _byPart.TryGetValue(partId, out var m) ? m : null;
    }

    /// <summary>A value as a player reads it: "6,000 kg", "18 m", "x1.5", "on".</summary>
    public string Show(ToolStat s, float v)
    {
        var st = Stats[(int)s];
        return st.Function ? v >= 0.5f ? "on" : "off" : st.Unit == "x" ? $"x{v:0.##}" : $"{v:#,0.##} {st.Unit}";
    }

    /// <summary>A tool as running text names it ("physics gun").</summary>
    public static string ToolName(ConstructTool t) => t switch
    {
        ConstructTool.PhysicsGun => "physics gun",
        ConstructTool.GroundDrill => "ground drill",
        _ => t.Label(),
    };

    /// <summary>What fitting a module gives, for its look line and the notice: "physics gun lift 6,000 kg".</summary>
    public string Gives(ToolStat s, int level)
    {
        var st = Stats[(int)s];
        return st.Function ? $"{ToolName(st.Tool)}: {st.Name.ToLowerInvariant()}" : $"{ToolName(st.Tool)} {st.Name.ToLowerInvariant()} {Show(s, Value(s, level))}";
    }

    public static ToolUpgrades Load(string path)
    {
        try
        {
            return Parse(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new ToolUpgrades { Problem = $"{Path.GetFileName(path)}: {ex.Message}" };
        }
    }

    private static readonly string[] StatNames = ["lift", "reach", "drillBit", "drillMotor", "fitter", "tractor", "throw"];

    public static ToolUpgrades Parse(string json)
    {
        var t = new ToolUpgrades();
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
                    case "stats":
                        foreach (var e in p.Value.EnumerateArray())
                            ParseStat(t, e);
                        break;
                    default: throw new FormatException($"unknown key \"{p.Name}\"");
                }
            return t;
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException)
        {
            return new ToolUpgrades { Problem = ex.Message };
        }
    }

    private static void ParseStat(ToolUpgrades t, JsonElement e)
    {
        var id = e.TryGetProperty("stat", out var s) ? Array.IndexOf(StatNames, s.GetString()) : -1;
        if (id < 0)
            throw new FormatException($"a stat's \"stat\" is one of {string.Join(", ", StatNames)}");
        var st = t.Stats[id];
        var what = StatNames[id];
        foreach (var p in e.EnumerateObject())
            switch (p.Name)
            {
                case "stat": break;
                case "name": st.Name = Text(p.Value, $"{what}.name"); break;
                case "about": st.About = Text(p.Value, $"{what}.about"); break;
                case "base": st.Base = Num(p.Value, $"{what}.base"); break;
                case "creative": st.Creative = Num(p.Value, $"{what}.creative"); break;
                case "levels":
                {
                    var levels = new List<Level>();
                    foreach (var l in p.Value.EnumerateArray())
                    {
                        var lv = new Level();
                        foreach (var q in l.EnumerateObject())
                            switch (q.Name)
                            {
                                case "part": lv.Part = Text(q.Value, $"{what}.levels.part"); break;
                                case "value": lv.Value = Num(q.Value, $"{what}.levels.value"); break;
                                default: throw new FormatException($"unknown key \"{what}.levels.{q.Name}\"");
                            }
                        if (lv.Part.Length == 0)
                            throw new FormatException($"each of {what}'s levels names its module \"part\"");
                        if (levels.Count > 0 ? lv.Value <= levels[^1].Value : lv.Value <= st.Base)
                            throw new FormatException($"{what}'s levels must each be more than the one before ({lv.Value} after {(levels.Count > 0 ? levels[^1].Value : st.Base)})");
                        levels.Add(lv);
                    }
                    if (levels.Count > MaxLevels)
                        throw new FormatException($"{what} has {levels.Count} levels: at most {MaxLevels}");
                    st.Levels = levels.ToArray();
                    break;
                }
                default: throw new FormatException($"unknown key \"{what}.{p.Name}\"");
            }
        if (st.Function && (st.Base != 0 || st.Levels.Length != 1 || st.Levels[0].Value != 1))
            throw new FormatException($"{what} is a function: base 0 and one level of value 1");
        // The limits the code holds to whatever the file says (the beam's mass and reach, the drill's size and pace).
        var (lo, hi) = (ToolStat)id switch
        {
            ToolStat.Lift => (50f, ConstructGrab.MaxMass),
            ToolStat.Reach => (2f, ConstructGrab.Range),
            ToolStat.DrillBit => (0.5f, 3f),
            ToolStat.DrillMotor => (0.5f, 5f),
            ToolStat.Fitter => (0.5f, 10f),
            _ => (0f, 1f),
        };
        foreach (var v in st.Levels.Select(l => l.Value).Append(st.Base).Append(st.Creative))
            if (v < lo || v > hi)
                throw new FormatException($"{what}: {v} is outside {lo} to {hi}");
    }

    private static string Text(JsonElement e, string what) =>
        e.ValueKind == JsonValueKind.String && e.GetString() is { Length: > 0 and <= 120 } s && s.All(ch => ch is >= ' ' and <= '~') ? s : throw new FormatException($"\"{what}\" is ASCII text, at most 120 characters");

    private static float Num(JsonElement e, string what) =>
        e.ValueKind == JsonValueKind.Number && float.IsFinite(e.GetSingle()) ? e.GetSingle() : throw new FormatException($"\"{what}\" is a number");
}
