using System.Text.Json;

namespace CallOfSlop;

/// <summary>How a faction kind sees someone: fights on sight, fights only who shot first, or never starts a fight.</summary>
public enum CrewStance : byte
{
    Neutral,
    Wary,
    Hostile,
}

/// <summary>The boarding actions a captain weighs (bot-brain.md section 8), in the faction file's <c>boarding.weights</c> order.</summary>
public enum BoardAct : byte
{
    Hold,
    Repel,
    LockDown,
    CallForHelp,
    Dump,
    Flee,
    Abandon,
    Surrender,
    Scuttle,
}

/// <summary>When a crew scuttles its ship.</summary>
public enum ScuttleWhen : byte
{
    Never,
    Desperate,
    Always,
}

/// <summary>One NPC faction kind (Crew/Factions/NAME.json, "call-of-slop-npc-faction/1"; docs/sandbox/design/bot-brain.md section 7).</summary>
public sealed class NpcFactionDef
{
    public byte Id; // 1-based (Construct.NpcFaction; 0: none)
    public string Name = "", About = "";
    public string[] Styles = [];
    public CrewStance ToPlayers;
    public CrewStance[] ToFactions = []; // by Id (0 unused): filled once every file is read; itself neutral
    public float Sight = 2500, Range; // m; Range 0: the brief's Engage, or the drivers' own
    public bool Chase, OnFoot;
    public int SparesMin, SparesMax;
    public byte SkillMin = 100, SkillMax = 200;
    public float MoraleStart = 0.8f, MoraleCrewLost = 0.1f, MoraleHullLost = 0.5f, MoraleOutOfAmmo = 0.2f, MoraleKill = 0.1f, MoraleAllies = 0.1f;
    public float FleeHull, FleeMorale; // the captain's Flee wins below these (hull share, morale)
    public readonly float[] Board = new float[9]; // weights by BoardAct
    public float SurrenderMorale, ScuttleDesperation;
    public bool SurrenderAtOnce; // boarding.atOnce (Karl, 2026-11-18: the Traders): anyone who comes aboard uninvited is a boarder, and the crew puts its hands up at once
    public ScuttleWhen Scuttle;
    public float ScuttleArm = 15, ScuttleCountdown = 90, ScuttleDisarm = 5; // s
    public string[] ScuttlePoints = [];
    public bool CanRelease, CanDrop, CanHire, CanBrig;
    public int HireCost, Wage; // credits
    /// <summary>False ("controller": false): a power made from it keeps its relations as they start (Player Crew, Test Hostile: faction-controllers.md section 2).</summary>
    public bool Controller = true;
    /// <summary>Its admirals' doctrine in Crew/Fleet.json (empty: the one named as the kind, else Standard: CrewFleetRules.cs).</summary>
    public string Doctrine = "";
    public ModuleWants? Modules; // "modules": what it wants of a ship's modules (module-sockets.md 9.1; ModuleChoice.cs), or null: it chooses none
    /// <summary>Its standing to start from with a player who hasn't met it (-100 to 100).</summary>
    public int StartStanding => ToPlayers switch { CrewStance.Hostile => -50, CrewStance.Wary => 0, _ => 20 };
}

/// <summary>The NPC faction kinds, read once from Crew/Factions (a bad file is refused with a reason and left out). Kinds are numbered in name order.</summary>
public static class CrewFactions
{
    public const string Format = "call-of-slop-npc-faction/1";
    public const int Max = FactionTable.MaxStory; // (their standings sit in the factions table's NPC rows: 16)

    private static NpcFactionDef[]? _all;

    /// <summary>Index 0 is null (no faction); 1..Count the kinds.</summary>
    public static NpcFactionDef?[] All => _all ??= Load(Path.Combine(AppFolders.Data, "Crew", "Factions"));

    public static int Count => All.Length - 1;

    public static NpcFactionDef? Of(byte id) => id > 0 && id < All.Length ? All[id] : null;

    public static byte Find(string name)
    {
        for (var i = 1; i < All.Length; i++)
            if (All[i]!.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return (byte)i;
        return 0;
    }

    /// <summary>The kind whose craft are made in vehicle kit style <paramref name="style"/> (0: none).</summary>
    public static byte ForStyle(string style)
    {
        for (var i = 1; i < All.Length; i++)
            if (Array.Exists(All[i]!.Styles, s => s.Equals(style, StringComparison.OrdinalIgnoreCase)))
                return (byte)i;
        return 0;
    }

    public static NpcFactionDef[] Load(string dir)
    {
        var list = new List<NpcFactionDef>();
        var raw = new List<Dictionary<string, string>>();
        if (Directory.Exists(dir))
            foreach (var path in Directory.GetFiles(dir, "*.json").OrderBy(p => p, StringComparer.Ordinal))
            {
                try
                {
                    var (def, rel) = Read(path);
                    if (list.Exists(d => d.Name.Equals(def.Name, StringComparison.OrdinalIgnoreCase)))
                        throw new InvalidDataException($"a faction named \"{def.Name}\" is already read");
                    if (list.Count >= Max)
                        throw new InvalidDataException($"there are {Max} factions already");
                    list.Add(def);
                    raw.Add(rel);
                }
                catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or FormatException)
                {
                    Console.WriteLine($"[crew] REFUSED {path}: {ex.Message}");
                }
            }
        else
            Console.WriteLine($"[crew] no faction folder {dir}: crews have no factions");
        var all = new NpcFactionDef[list.Count + 1];
        for (var i = 0; i < list.Count; i++)
        {
            list[i].Id = (byte)(i + 1);
            all[i + 1] = list[i];
        }
        for (var i = 0; i < list.Count; i++)
        {
            var d = list[i];
            d.ToFactions = new CrewStance[all.Length];
            for (var j = 1; j < all.Length; j++)
                d.ToFactions[j] = raw[i].TryGetValue(all[j].Name, out var s) ? Stance(s) : all[j] == d ? CrewStance.Neutral : d.ToPlayers == CrewStance.Hostile && all[j].Name == "Test Hostile" ? CrewStance.Hostile : CrewStance.Neutral;
            foreach (var name in raw[i].Keys)
                if (Array.FindIndex(all, 1, a => a!.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) < 0)
                    Console.WriteLine($"[crew] {d.Name}: its stance to \"{name}\" names no faction: left out");
        }
        Console.WriteLine($"[crew] {list.Count} factions: {string.Join(", ", list.Select(d => d.Name))}");
        return all!;
    }

    private static CrewStance Stance(string s) => s.ToLowerInvariant() switch
    {
        "hostile" => CrewStance.Hostile,
        "wary" => CrewStance.Wary,
        "neutral" => CrewStance.Neutral,
        _ => throw new InvalidDataException($"stance \"{s}\" isn't hostile, wary or neutral"),
    };

    private static (NpcFactionDef, Dictionary<string, string>) Read(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var root = doc.RootElement;
        if (!root.TryGetProperty("format", out var f) || f.GetString() != Format)
            throw new InvalidDataException($"its format isn't \"{Format}\"");
        var d = new NpcFactionDef
        {
            Name = Text(root, "name", 32) is { Length: > 0 } n ? n : throw new InvalidDataException("it has no name"),
            About = Text(root, "about", 200),
            Controller = !root.TryGetProperty("controller", out var ctl) || ctl.GetBoolean(),
            Doctrine = Text(root, "doctrine", 32),
        };
        if (root.TryGetProperty("modules", out var mods))
            d.Modules = ModuleWants.Parse(mods);
        if (root.TryGetProperty("styles", out var st))
            d.Styles = st.EnumerateArray().Select(e => e.GetString() ?? "").Where(s => s.Length > 0).ToArray();
        var rel = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (root.TryGetProperty("stance", out var stance))
        {
            d.ToPlayers = stance.TryGetProperty("players", out var p) ? Stance(p.GetString() ?? "") : CrewStance.Neutral;
            if (stance.TryGetProperty("factions", out var fs))
                foreach (var kv in fs.EnumerateObject())
                {
                    Stance(kv.Value.GetString() ?? "");
                    rel[kv.Name] = kv.Value.GetString()!;
                }
        }
        if (root.TryGetProperty("engage", out var en))
        {
            d.Sight = Num(en, "sight", d.Sight, 100, 10000);
            d.Range = Num(en, "range", 0, 0, 5000);
            d.Chase = en.TryGetProperty("chase", out var c) && c.GetBoolean();
            d.OnFoot = en.TryGetProperty("onFoot", out var o) && o.GetBoolean();
        }
        if (root.TryGetProperty("crew", out var cr))
        {
            if (cr.TryGetProperty("spares", out var sp) && sp.GetArrayLength() == 2)
                (d.SparesMin, d.SparesMax) = (Math.Clamp(sp[0].GetInt32(), 0, 16), Math.Clamp(sp[1].GetInt32(), 0, 16));
            if (cr.TryGetProperty("skill", out var sk) && sk.GetArrayLength() == 2)
                (d.SkillMin, d.SkillMax) = ((byte)Math.Clamp(sk[0].GetInt32(), 0, 255), (byte)Math.Clamp(sk[1].GetInt32(), 0, 255));
            if (d.SparesMax < d.SparesMin || d.SkillMax < d.SkillMin)
                throw new InvalidDataException("crew: a range's top is under its bottom");
        }
        if (root.TryGetProperty("morale", out var mo))
        {
            d.MoraleStart = Num(mo, "start", d.MoraleStart, 0, 1);
            d.MoraleCrewLost = Num(mo, "crewLost", d.MoraleCrewLost, 0, 1);
            d.MoraleHullLost = Num(mo, "hullLost", d.MoraleHullLost, 0, 2);
            d.MoraleOutOfAmmo = Num(mo, "outOfAmmo", d.MoraleOutOfAmmo, 0, 1);
            d.MoraleKill = Num(mo, "kill", d.MoraleKill, 0, 1);
            d.MoraleAllies = Num(mo, "alliesNear", d.MoraleAllies, 0, 1);
        }
        if (root.TryGetProperty("flee", out var fl))
        {
            d.FleeHull = Num(fl, "hull", 0, 0, 1);
            d.FleeMorale = Num(fl, "morale", 0, 0, 1);
        }
        if (root.TryGetProperty("boarding", out var bo))
        {
            if (bo.TryGetProperty("weights", out var w))
                foreach (var kv in w.EnumerateObject())
                    d.Board[(int)Enum.Parse<BoardAct>(kv.Name, ignoreCase: true)] = Math.Clamp(kv.Value.GetSingle(), 0, 10);
            d.SurrenderMorale = Num(bo, "surrenderMorale", 0, 0, 1);
            d.ScuttleDesperation = Num(bo, "scuttleDesperation", 1, 0, 2);
            d.SurrenderAtOnce = bo.TryGetProperty("atOnce", out var ao) && ao.GetBoolean();
        }
        if (root.TryGetProperty("scuttle", out var sc))
        {
            d.Scuttle = (Text(sc, "when", 16) is { Length: > 0 } wh ? wh : "never").ToLowerInvariant() switch
            {
                "never" => ScuttleWhen.Never,
                "desperate" => ScuttleWhen.Desperate,
                "always" => ScuttleWhen.Always,
                var x => throw new InvalidDataException($"scuttle \"{x}\" isn't never, desperate or always"),
            };
            d.ScuttleArm = Num(sc, "arm", 15, 1, 120);
            d.ScuttleCountdown = Num(sc, "countdown", 90, 10, 600);
            d.ScuttleDisarm = Num(sc, "disarm", 5, 1, 60);
            if (sc.TryGetProperty("points", out var pts))
                d.ScuttlePoints = pts.EnumerateArray().Select(e => (e.GetString() ?? "").ToLowerInvariant()).Where(s => s is "magazine" or "fuel" or "reactor" or "drive").ToArray();
        }
        if (root.TryGetProperty("capture", out var ca))
            foreach (var e in ca.EnumerateArray())
                switch (e.GetString())
                {
                    case "release": d.CanRelease = true; break;
                    case "drop": d.CanDrop = true; break;
                    case "hire": d.CanHire = true; break;
                    case "brig": d.CanBrig = true; break;
                    default: throw new InvalidDataException($"capture \"{e.GetString()}\" isn't release, drop, hire or brig");
                }
        if (root.TryGetProperty("hire", out var hi))
        {
            d.HireCost = (int)Num(hi, "cost", 0, 0, 1_000_000);
            d.Wage = (int)Num(hi, "wage", 0, 0, 100_000);
        }
        return (d, rel);
    }

    private static string Text(JsonElement e, string key, int max)
    {
        if (!e.TryGetProperty(key, out var v))
            return "";
        var s = v.GetString() ?? "";
        if (s.Length > max || !s.All(ch => ch is >= ' ' and <= '~'))
            throw new InvalidDataException($"\"{key}\" is over {max} letters or not ASCII");
        return s;
    }

    private static float Num(JsonElement e, string key, float def, float min, float max)
    {
        if (!e.TryGetProperty(key, out var v))
            return def;
        var x = v.GetSingle();
        if (!float.IsFinite(x) || x < min || x > max)
            throw new InvalidDataException($"\"{key}\" {x} is outside {min} to {max}");
        return x;
    }
}
