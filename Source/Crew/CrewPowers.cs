using System.Text.Json;

namespace CallOfSlop;

/// <summary>
/// One power: a named faction made from a kind (Crew/Powers/NAME.json, "call-of-slop-npc-power/1"; docs/sandbox/design/faction-controllers.md
/// section 2). The kind (Crew/Factions) gives its crews' rules; the power its name, colour, operating parameters and starting relations. With no
/// Powers folder each kind is one power of the kind's name, so a world without one plays as before.
/// </summary>
public sealed class NpcPowerDef
{
    public byte Id; // 1-based (CrewShip.Faction, Construct.NpcFaction, the factions table's NPC row + 1)
    public string Name = "", Tag = "", About = "";
    public uint Colour = 0xA0A0A0;
    public NpcFactionDef Kind = null!;
    /// <summary>False: its relations never move by themselves (no decay, no pacts) and it takes no third party's part: Player Crew, Test Hostile.</summary>
    public bool Controller = true;
    public string Doctrine = ""; // its admirals' doctrine (Crew/Fleet.json; empty: its kind's)
    public string PlanText = ""; // phase 3: its controller's plan ("plan": budget, fleet, missions, places), raw (ControllerRules.PlanOf reads it; empty: its kind's in Crew/Controllers.json)
    // Operating parameters (section 3.1): 0.5 is "as the kind's numbers say" (an event's amount is scaled by 0.5 + parameter).
    public float Aggression = 0.5f, Greed = 0.5f, Loyalty = 0.5f, Forgiveness = 0.5f, Pride = 0.5f, Caution = 1f;
    public float ValueCrew = 0.5f, ValueShips = 0.5f, ValueCargo = 0.5f, ValuePlanets = 0.5f;
    /// <summary>How it sees each power at the start (by power id; [0] unused) and the values its relations drift back to.</summary>
    public sbyte[] StartTo = [], RestTo = [];
    /// <summary>How it sees players it hasn't met, and the value its standing with them drifts back to.</summary>
    public sbyte StartPlayers, RestPlayers;

    /// <summary>A parameter by its name in Crew/Relations.json ("pride", "values.crew", ...); -1: no such name.</summary>
    public float Param(string name) => name switch
    {
        "aggression" => Aggression,
        "greed" => Greed,
        "loyalty" => Loyalty,
        "forgiveness" => Forgiveness,
        "pride" => Pride,
        "values.crew" => ValueCrew,
        "values.ships" => ValueShips,
        "values.cargo" => ValueCargo,
        "values.planets" => ValuePlanets,
        _ => -1,
    };
}

/// <summary>The powers, read once (a bad file is refused with a reason and left out). At most <see cref="Max"/>: they take the factions table's NPC rows.</summary>
public static class CrewPowers
{
    public const string Format = "call-of-slop-npc-power/1";
    public const int Max = FactionTable.MaxStory;

    private static NpcPowerDef?[]? _all;

    /// <summary>Index 0 is null (none); 1..Count the powers.</summary>
    public static NpcPowerDef?[] All => _all ??= Load(Path.Combine(AppFolders.Data, "Crew", "Powers"));

    public static int Count => All.Length - 1;

    /// <summary>A tool (--crew-relations): powers from another folder, from now on in this run.</summary>
    public static void Use(string dir) => _all = Load(dir);

    public static NpcPowerDef? Of(byte id) => id > 0 && id < All.Length ? All[id] : null;

    /// <summary>The kind power <paramref name="id"/> is made from (its crews' rules), or null.</summary>
    public static NpcFactionDef? KindOf(byte id) => Of(id)?.Kind;

    /// <summary>A power by name; else the first power of the kind of that name (a save from before a Powers folder came); 0: none.</summary>
    public static byte Find(string name)
    {
        for (var i = 1; i < All.Length; i++)
            if (All[i]!.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return (byte)i;
        return ForKind(CrewFactions.Find(name));
    }

    /// <summary>The first power made from kind <paramref name="kind"/> (0: none).</summary>
    public static byte ForKind(byte kind)
    {
        if (kind == 0)
            return 0;
        for (var i = 1; i < All.Length; i++)
            if (All[i]!.Kind.Id == kind)
                return (byte)i;
        return 0;
    }

    /// <summary>The first power of the kind named <paramref name="kindName"/> (0: none).</summary>
    public static byte ForKindName(string kindName) => ForKind(CrewFactions.Find(kindName));

    /// <summary>The power whose craft are made in vehicle kit style <paramref name="style"/> (its kind's; 0: none).</summary>
    public static byte ForStyle(string style) => ForKind(CrewFactions.ForStyle(style));

    public static int StartFrom(CrewStance s) => s switch { CrewStance.Hostile => -50, CrewStance.Wary => 0, _ => 20 };

    public static NpcPowerDef?[] Load(string dir)
    {
        var kinds = CrewFactions.All;
        var list = new List<NpcPowerDef>();
        var raw = new List<(Dictionary<string, int> Start, Dictionary<string, int> Rests)>();
        if (Directory.Exists(dir) && Directory.GetFiles(dir, "*.json").Length > 0)
        {
            foreach (var path in Directory.GetFiles(dir, "*.json").OrderBy(p => p, StringComparer.Ordinal))
                try
                {
                    var (def, start, rest) = Read(path);
                    if (list.Exists(d => d.Name.Equals(def.Name, StringComparison.OrdinalIgnoreCase)))
                        throw new InvalidDataException($"a power named \"{def.Name}\" is already read");
                    if (list.Count >= Max)
                        throw new InvalidDataException($"there are {Max} powers already (the factions table's NPC rows)");
                    list.Add(def);
                    raw.Add((start, rest));
                }
                catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or FormatException)
                {
                    Console.WriteLine($"[crew] REFUSED {path}: {ex.Message}");
                }
        }
        else
            for (var k = 1; k < kinds.Length; k++)
            {
                var kind = kinds[k]!;
                list.Add(new NpcPowerDef { Name = kind.Name, Tag = TagOf(kind.Name), About = kind.About, Kind = kind, Controller = kind.Controller, Colour = ColourOf(kind.Name) });
                raw.Add((new(StringComparer.OrdinalIgnoreCase), new(StringComparer.OrdinalIgnoreCase)));
            }
        var all = new NpcPowerDef?[list.Count + 1];
        for (var i = 0; i < list.Count; i++)
        {
            list[i].Id = (byte)(i + 1);
            all[i + 1] = list[i];
        }
        for (var i = 0; i < list.Count; i++)
        {
            var d = list[i];
            d.StartTo = new sbyte[all.Length];
            d.RestTo = new sbyte[all.Length];
            for (var j = 1; j < all.Length; j++)
            {
                var o = all[j]!;
                // By name in the power file; else its kind's stance to the other's kind (a power and itself, or two of one kind: the kind's word for itself, neutral).
                var v = raw[i].Start.TryGetValue(o.Name, out var n) ? n : o == d ? 100 : StartFrom(d.Kind.ToFactions.Length > o.Kind.Id ? d.Kind.ToFactions[o.Kind.Id] : CrewStance.Neutral);
                d.StartTo[j] = (sbyte)v;
                d.RestTo[j] = (sbyte)(raw[i].Rests.TryGetValue(o.Name, out var rn) ? rn : v);
            }
            d.StartPlayers = (sbyte)(raw[i].Start.TryGetValue("players", out var p) ? p : d.Kind.StartStanding);
            d.RestPlayers = (sbyte)(raw[i].Rests.TryGetValue("players", out var rp) ? rp : d.StartPlayers);
            foreach (var name in raw[i].Start.Keys.Concat(raw[i].Rests.Keys))
                if (name != "players" && Array.FindIndex(all, 1, a => a!.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) < 0)
                    Console.WriteLine($"[crew] power {d.Name}: its relation to \"{name}\" names no power: left out");
        }
        Console.WriteLine($"[crew] {list.Count} powers{(list.Count > 0 && Directory.Exists(dir) ? $" from {Path.GetFileName(dir)}" : " (one a kind)")}: {string.Join(", ", list.Select(d => d.Name == d.Kind.Name ? d.Name : $"{d.Name} ({d.Kind.Name})"))}");
        return all;
    }

    private static string TagOf(string name)
    {
        var letters = new string(name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(w => char.ToUpperInvariant(w[0])).ToArray());
        return letters.Length >= 2 ? letters[..Math.Min(4, letters.Length)] : name[..Math.Min(4, name.Length)].ToUpperInvariant();
    }

    private static uint ColourOf(string name)
    {
        var h = 2166136261u;
        foreach (var ch in name)
            h = (h ^ ch) * 16777619u;
        // A light colour: each channel 96-255.
        return (96u + (h & 0x9F)) << 16 | (96u + (h >> 8 & 0x9F)) << 8 | 96u + (h >> 16 & 0x9F);
    }

    private static (NpcPowerDef, Dictionary<string, int>, Dictionary<string, int>) Read(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var root = doc.RootElement;
        if (!root.TryGetProperty("format", out var f) || f.GetString() != Format)
            throw new InvalidDataException($"its format isn't \"{Format}\"");
        var name = Text(root, "name", 32);
        if (name.Length == 0)
            throw new InvalidDataException("it has no name");
        var kindName = Text(root, "kind", 32);
        var kind = CrewFactions.Of(CrewFactions.Find(kindName)) ?? throw new InvalidDataException($"its kind \"{kindName}\" is no kind in Crew/Factions");
        var d = new NpcPowerDef
        {
            Name = name,
            Kind = kind,
            About = Text(root, "about", 200),
            Tag = Text(root, "tag", FactionTable.TagMax) is { Length: > 0 } t ? t : TagOf(name),
            Controller = root.TryGetProperty("controller", out var c) ? c.GetBoolean() : kind.Controller,
            Colour = ColourOf(name),
            Doctrine = Text(root, "doctrine", 32),
            PlanText = root.TryGetProperty("plan", out var plan) ? plan.GetRawText() : "",
        };
        if (root.TryGetProperty("colour", out var col))
        {
            var s = (col.GetString() ?? "").TrimStart('#');
            d.Colour = s.Length == 6 && uint.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out var u) ? u : throw new InvalidDataException($"colour \"{col.GetString()}\" isn't #RRGGBB");
        }
        if (root.TryGetProperty("parameters", out var p))
        {
            d.Aggression = Num(p, "aggression", d.Aggression, 0, 1);
            d.Greed = Num(p, "greed", d.Greed, 0, 1);
            d.Loyalty = Num(p, "loyalty", d.Loyalty, 0, 1);
            d.Forgiveness = Num(p, "forgiveness", d.Forgiveness, 0, 1);
            d.Pride = Num(p, "pride", d.Pride, 0, 1);
            d.Caution = Num(p, "caution", d.Caution, 0.5f, 2);
            if (p.TryGetProperty("values", out var v))
            {
                d.ValueCrew = Num(v, "crew", d.ValueCrew, 0, 1);
                d.ValueShips = Num(v, "ships", d.ValueShips, 0, 1);
                d.ValueCargo = Num(v, "cargo", d.ValueCargo, 0, 1);
                d.ValuePlanets = Num(v, "planets", d.ValuePlanets, 0, 1);
            }
        }
        return (d, Relations(root, "relations"), Relations(root, "rest"));
    }

    /// <summary>"relations": {"Other Power": -70, "players": 0} (a number -100 to 100, or hostile, wary, neutral).</summary>
    private static Dictionary<string, int> Relations(JsonElement root, string key)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (!root.TryGetProperty(key, out var rel))
            return map;
        foreach (var kv in rel.EnumerateObject())
            map[kv.Name] = kv.Value.ValueKind == JsonValueKind.Number
                ? Math.Abs(kv.Value.GetInt32()) <= 100 ? kv.Value.GetInt32() : throw new InvalidDataException($"{key}: \"{kv.Name}\" {kv.Value.GetInt32()} is outside -100 to 100")
                : (kv.Value.GetString() ?? "").ToLowerInvariant() switch
                {
                    "hostile" => -50,
                    "wary" => 0,
                    "neutral" => 20,
                    var w => throw new InvalidDataException($"{key}: \"{kv.Name}\" is \"{w}\", not a number or hostile, wary or neutral"),
                };
        return map;
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
