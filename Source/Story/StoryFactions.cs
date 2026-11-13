using System.Text.Json;

namespace CallOfSlop.Story;

/// <summary>Something done to a faction that it remembers (docs/story/STORY.md "Factions"): a verb against something of its, how much it counts.</summary>
public sealed class FactionDeed
{
    public string Name = "", About = "";
    public int Id; // its place in the faction's list
    public int Verb;
    public StoryRole Theirs = StoryRole.Target; // the role that must be the faction's (itself, a member, something it claims)
    public string[] Words = []; // an email's: one of these in it (any case)
    public int Remember = -1; // a relation the faction holds towards the doer, +1 each time (-1: none)
    public int Standing; // added to the faction's standing with the doer and their group
    public (ushort Quality, int Delta)[] Wants = [];
    public double Every; // s: counted once in this long per doer at most (0: every time)
    public string Text = ""; // what it asks, as words (the window)
    public string WhoseName = ""; // "whose": the deed is against another faction's things, not this one's...
    public int Whose = -1; // ...that faction (-1: this one)
}

/// <summary>
/// A technology a faction teaches (docs/story/STORY.md "Technologies"): the parts an Assembler won't build for a player or
/// group until the faction has taught them it, which it does when its standing with them first comes to <see cref="Tier"/>.
/// </summary>
public sealed class FactionTech
{
    public string Name = "", About = "";
    public int Id; // its place in the faction's list
    public int Tier; // the faction's tier it is taught at
    public string[] Parts = []; // construct parts by name (the game checks them against its parts)
}

/// <summary>Something a faction wants (a quality of the faction from 0 to 100) and how it drifts by the hour.</summary>
public sealed class FactionWant
{
    public string Name = "", About = "";
    public ushort Quality;
    public int Start;
    public double PerHour;
}

/// <summary>A faction's word for a party whose standing is below <see cref="Below"/> (the last tier has no top).</summary>
public readonly record struct FactionTier(string Name, int Below);

/// <summary>A faction member, a story person (in F1 a name with traits; their bodies come with visits).</summary>
public readonly record struct FactionMember(string Name, string About, (ushort Quality, int Value)[] Qualities);

/// <summary>A faction from Story/Factions/*.json: who they are, where they live, what is theirs, what they want and what they remember.</summary>
/// <summary>
/// A faction's home on a planetoid (<c>"settlement"</c>, docs/planets/WORLDS.md): buildings round a place given by latitude and
/// longitude, each a blueprint so many metres east and north of it.
/// </summary>
public sealed class FactionSettlement
{
    public const int MaxBuildings = 12;
    public const float Reach = 60f; // (m from the place a building may stand)
    public string Map = "";
    public float Lat, Lon;
    public (string Blueprint, float East, float North, float Yaw)[] Buildings = [];
}

public sealed class FactionDef
{
    public string Name = "", About = "", File = "", Home = "";
    public int Id;
    public string[] Claims = []; // a construct named starting with one of these is the faction's ("IT ": the vehicle kit's IT style and blueprints)
    public FactionMember[] Members = [];
    public FactionWant[] Wants = [];
    public FactionTier[] Tiers = [];
    public FactionDeed[] Deeds = [];
    public FactionTech[] Teaches = [];
    public FactionJob[] Jobs = []; // what it offers work for (StoryFactionJobs.cs)
    public string Voice = ""; // the tag its lines go out under ("MINISTRY MEMO": "[MINISTRY MEMO] ...")
    public string Stranger = ""; // what its people call someone whose name they don't know yet ("inmate"; "" the people's own default)
    public FactionSettlement? Settlement; // where its home stands on a world (Game/GameStorySettlements.cs)
    public string Courier = ""; // its line when its courier collects a crate
    public string[] Staff = []; // people from a cast (by name) who are its members whenever they are in the story
    public (string Name, int Faction, int Percent)[] Rivals = []; // a rise in its standing takes this share of the rise off the rival's

    /// <summary>The tier a standing falls in (-1: the faction has none).</summary>
    public int TierOf(int standing)
    {
        for (var i = 0; i < Tiers.Length; i++)
            if (standing < Tiers[i].Below)
                return i;
        return Tiers.Length - 1;
    }

    public string TierName(int standing) => TierOf(standing) is var t and >= 0 ? Tiers[t].Name : "";

    /// <summary>What a notice names: a deed, a tier, a technology it taught, or "Notorious" (an atrocity: every faction hunts them).</summary>
    public string NoticeName(int deed, int tier, int tech = -1, int job = -1) => deed >= 0 ? Deeds[deed].Name : tier >= 0 ? Tiers[tier].Name : tech >= 0 ? Teaches[tech].Name : job >= 0 ? Jobs[job].Name : "Notorious";
}

/// <summary>The factions from Story/Factions/*.json. A bad file is refused whole with its reason (<see cref="StoryRules.Errors"/>).</summary>
public sealed partial class FactionRules
{
    public const int MaxFactions = 32, MaxDeeds = 64, MaxMembers = 64, MaxWants = 8, MaxTiers = 8, MaxStanding = 100;
    public const int MaxTeaches = 8, MaxTechParts = 32, MaxRivals = 8; // (8: a faction's learnt technologies are a byte on the wire)
    public const string Format = "call-of-slop-faction/1";

    public FactionDef[] Factions = [];
    public int MemberOf = -1, Claims = -1, Home = -1, Likes = -1, Grudge = -1; // relations
    public int Email = -1, Fax = -1, Keep = -1, Rate = -1, Atrocity = -1, Teach = -1; // verbs
    public int Learnt = -1; // the relation a player or group holds towards a technology (a Thing) it was taught
    public int Notorious = -1; // the quality on a player or group every faction hunts

    public FactionDef? Find(string name) => Array.Find(Factions, f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    private static readonly string[] FileKeys = ["format", "name", "about", "home", "claims", "members", "staff", "wants", "tiers", "deeds", "teaches", "rivals", "jobs", "voice", "stranger", "courier", "settlement"];
    private static readonly string[] SettlementKeys = ["map", "lat", "lon", "buildings"];
    private static readonly string[] BuildingKeys = ["blueprint", "east", "north", "yaw"];
    private static readonly string[] TechKeys = ["name", "about", "tier", "parts"];
    private static readonly string[] MemberKeys = ["name", "about", "qualities"];
    private static readonly string[] WantKeys = ["name", "about", "start", "perHour"];
    private static readonly string[] TierKeys = ["name", "below"];
    private static readonly string[] DeedKeys = ["name", "about", "verb", "theirs", "whose", "words", "remember", "standing", "wants", "every"];

    public static FactionRules Load(StoryRules rules, string folder)
    {
        var fr = new FactionRules
        {
            MemberOf = rules.Relation("MemberOf")?.Id ?? -1, Claims = rules.Relation("Claims")?.Id ?? -1, Home = rules.Relation("Home")?.Id ?? -1,
            Likes = rules.Relation("Likes")?.Id ?? -1, Grudge = rules.Relation("Grudge")?.Id ?? -1,
            Email = rules.VerbId("Email"), Fax = rules.VerbId("Fax"), Keep = rules.VerbId("Keep"), Rate = rules.VerbId("Rate"), Atrocity = rules.VerbId("Atrocity"),
            Teach = rules.VerbId("Teach"), Learnt = rules.Relation("Learnt")?.Id ?? -1,
            Notorious = rules.Quality("Notorious")?.Id ?? -1,
        };
        fr.JobIds(rules);
        var all = new List<FactionDef>();
        foreach (var f in StoryRules.Files(Path.Combine(folder, "Factions")))
        {
            var name = $"Factions/{Path.GetFileName(f)}";
            try
            {
                if (fr.MemberOf < 0 || fr.Claims < 0 || fr.Likes < 0 || fr.Grudge < 0)
                    throw new StoryException($"{name}: factions need the relations MemberOf, Claims, Likes and Grudge (Story/Relations)");
                using var doc = StoryRules.Parse(StoryRules.ReadFile(f), name);
                var def = Read(fr, rules, doc.RootElement, name, all.Count);
                if (all.Any(o => o.Name.Equals(def.Name, StringComparison.OrdinalIgnoreCase)))
                    throw new StoryException($"{name}: a faction called {def.Name} already exists");
                all.Add(def);
                if (all.Count > MaxFactions)
                    throw new StoryException($"{name}: too many factions ({MaxFactions} at most)");
            }
            catch (StoryException ex)
            {
                rules.Errors.Add(ex.Message);
            }
        }
        // What names another faction: rivals and "whose" deeds (a faction's file may name one that comes later).
        foreach (var def in all.ToArray())
        {
            try
            {
                def.Rivals = def.Rivals.Select(r => (r.Name, all.FindIndex(o => o.Name.Equals(r.Name, StringComparison.OrdinalIgnoreCase)) is var k and >= 0 && k != def.Id ? k
                    : throw new StoryException($"{def.File}: {def.Name}: \"rivals\": no other faction called {r.Name}"), r.Percent)).ToArray();
                foreach (var d in def.Deeds)
                    if (d.WhoseName.Length > 0)
                        d.Whose = all.FindIndex(o => o.Name.Equals(d.WhoseName, StringComparison.OrdinalIgnoreCase)) is var k and >= 0 && k != def.Id ? k
                            : throw new StoryException($"{def.File}: {def.Name}: {d.Name}: \"whose\" is another faction (no {d.WhoseName})");
                if (def.Teaches.Length > 0 && (fr.Learnt < 0 || fr.Teach < 0))
                    throw new StoryException($"{def.File}: {def.Name}: \"teaches\" needs the relation Learnt and the verb Teach (Story/Relations, Story/Verbs)");
            }
            catch (StoryException ex)
            {
                rules.Errors.Add(ex.Message);
                all.Remove(def);
            }
        }
        for (var k = 0; k < all.Count; k++)
            all[k].Id = k;
        foreach (var def in all) // (ids again, after a refusal)
        {
            def.Rivals = def.Rivals.Select(r => (r.Name, all.FindIndex(o => o.Name.Equals(r.Name, StringComparison.OrdinalIgnoreCase)), r.Percent)).Where(r => r.Item2 >= 0).ToArray();
            foreach (var d in def.Deeds)
                if (d.WhoseName.Length > 0)
                    d.Whose = all.FindIndex(o => o.Name.Equals(d.WhoseName, StringComparison.OrdinalIgnoreCase));
        }
        fr.Factions = all.ToArray();
        return fr;
    }

    private static FactionDef Read(FactionRules fr, StoryRules rules, JsonElement root, string file, int id)
    {
        StoryRules.Keys(root, FileKeys, file);
        if (StoryRules.Str(root, "format", file, true) != Format)
            throw new StoryException($"{file}: \"format\" is \"{Format}\"");
        var def = new FactionDef { Name = StoryRules.Str(root, "name", file, true), About = StoryRules.Str(root, "about", file), File = file, Id = id };
        var where = $"{file}: {def.Name}";
        Plain(def.Name, where, 40);
        def.Home = StoryRules.Str(root, "home", where);
        if (def.Home.Length > 0)
            Plain(def.Home, $"{where}: home", 40);
        if (root.TryGetProperty("settlement", out var se))
            def.Settlement = ReadSettlement(se, $"{where}: settlement");
        def.Claims = StoryRules.List(root, "claims", where).Select(c => c.ValueKind == JsonValueKind.String && c.GetString() is { Length: >= 2 and <= 40 } s ? s
            : throw new StoryException($"{where}: each of \"claims\" is the start of a construct's name, 2 to 40 characters (\"IT \")")).ToArray();

        var members = new List<FactionMember>();
        foreach (var m in StoryRules.List(root, "members", where))
        {
            var at = $"{where}: member {members.Count + 1}";
            if (m.ValueKind != JsonValueKind.Object)
                throw new StoryException($"{at}: like {{\"name\": \"Gary\", \"about\": \"has a van\"}}");
            StoryRules.Keys(m, MemberKeys, at);
            var name = StoryRules.Str(m, "name", at, true);
            Plain(name, at, 40);
            var quals = new List<(ushort, int)>();
            if (m.TryGetProperty("qualities", out var qs))
            {
                if (qs.ValueKind != JsonValueKind.Object)
                    throw new StoryException($"{at}: \"qualities\" is like {{\"Brave\": 60}}");
                foreach (var q in qs.EnumerateObject())
                    quals.Add((rules.Quality(q.Name)?.Id ?? throw new StoryException($"{at}: no quality {q.Name}"),
                        q.Value.ValueKind == JsonValueKind.Number && q.Value.TryGetInt32(out var v) ? v : throw new StoryException($"{at}: {q.Name} is a whole number")));
            }
            members.Add(new FactionMember(name, StoryRules.Str(m, "about", at), quals.ToArray()));
        }
        def.Staff = StoryRules.List(root, "staff", where).Select(x => x.ValueKind == JsonValueKind.String && x.GetString() is { Length: > 0 and <= 40 } s ? s
            : throw new StoryException($"{where}: each of \"staff\" is the name of a cast's person (Story/Casts)")).ToArray();
        if (members.Count + def.Staff.Length > MaxMembers)
            throw new StoryException($"{where}: {MaxMembers} members and staff at most");
        def.Members = members.ToArray();

        var wants = new List<FactionWant>();
        foreach (var w in StoryRules.List(root, "wants", where))
        {
            var at = $"{where}: want {wants.Count + 1}";
            if (w.ValueKind != JsonValueKind.Object)
                throw new StoryException($"{at}: like {{\"name\": \"Respect\", \"start\": 50, \"perHour\": -2}}");
            StoryRules.Keys(w, WantKeys, at);
            var want = new FactionWant { Name = StoryRules.Str(w, "name", at, true), About = StoryRules.Str(w, "about", at) };
            want.Quality = rules.Quality(want.Name)?.Id ?? throw new StoryException($"{at}: a want is a quality of the faction: declare {want.Name} (Story/Relations)");
            want.Start = Int(w, "start", at, 0, 100, 50);
            want.PerHour = w.TryGetProperty("perHour", out var ph) && ph.ValueKind == JsonValueKind.Number && ph.TryGetDouble(out var d) && d is >= -100 and <= 100 ? d
                : w.TryGetProperty("perHour", out _) ? throw new StoryException($"{at}: \"perHour\" is a number from -100 to 100") : 0;
            wants.Add(want);
        }
        if (wants.Count > MaxWants)
            throw new StoryException($"{where}: {MaxWants} wants at most");
        def.Wants = wants.ToArray();

        var tiers = new List<FactionTier>();
        var tierList = StoryRules.List(root, "tiers", where).ToArray();
        foreach (var t in tierList)
        {
            var at = $"{where}: tier {tiers.Count + 1}";
            if (t.ValueKind != JsonValueKind.Object)
                throw new StoryException($"{at}: like {{\"name\": \"Flagged\", \"below\": -15}}");
            StoryRules.Keys(t, TierKeys, at);
            var name = StoryRules.Str(t, "name", at, true);
            Plain(name, at, 24);
            var last = tiers.Count == tierList.Length - 1;
            if (last && t.TryGetProperty("below", out _))
                throw new StoryException($"{at}: the last tier has no \"below\" (it is everything above)");
            var below = last ? int.MaxValue : Int(t, "below", at, -MaxStanding + 1, MaxStanding, null);
            if (tiers.Count > 0 && below <= tiers[^1].Below)
                throw new StoryException($"{at}: tiers go from the worst up (\"below\" grows)");
            if (tiers.Any(o => o.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                throw new StoryException($"{at}: a tier called {name} already exists");
            tiers.Add(new FactionTier(name, below));
        }
        if (tiers.Count > MaxTiers)
            throw new StoryException($"{where}: {MaxTiers} tiers at most");
        def.Tiers = tiers.ToArray();

        var deeds = new List<FactionDeed>();
        foreach (var e in StoryRules.List(root, "deeds", where))
        {
            var at = $"{where}: deed {deeds.Count + 1}";
            if (e.ValueKind != JsonValueKind.Object)
                throw new StoryException($"{at}: like {{\"name\": \"Wrecked our hardware\", \"verb\": \"Wreck\", \"remember\": \"WreckedOurs\", \"standing\": -20}}");
            StoryRules.Keys(e, DeedKeys, at);
            var deed = new FactionDeed { Name = StoryRules.Str(e, "name", at, true), About = StoryRules.Str(e, "about", at), Id = deeds.Count };
            at = $"{where}: {deed.Name}";
            Plain(deed.Name, at, 40);
            if (deeds.Any(o => o.Name.Equals(deed.Name, StringComparison.OrdinalIgnoreCase)) || tiers.Any(o => o.Name.Equals(deed.Name, StringComparison.OrdinalIgnoreCase)) || deed.Name.Equals("Notorious", StringComparison.OrdinalIgnoreCase))
                throw new StoryException($"{at}: deeds and tiers share names (a fax's notices name them)");
            var verb = StoryRules.Str(e, "verb", at, true);
            deed.Verb = rules.VerbId(verb) is var vid and >= 0 ? vid : throw new StoryException($"{at}: no verb {verb}");
            var theirs = StoryRules.Str(e, "theirs", at);
            if (theirs.Length > 0)
                deed.Theirs = Array.IndexOf(StoryRules.RoleNames, theirs) is var r and >= 0 && r != (int)StoryRole.Actor ? (StoryRole)r
                    : throw new StoryException($"{at}: \"theirs\" is the role that is the faction's: target, owner, place or item");
            deed.WhoseName = StoryRules.Str(e, "whose", at);
            deed.Words = StoryRules.List(e, "words", at).Select(x => x.ValueKind == JsonValueKind.String && x.GetString() is { Length: > 0 } s ? s
                : throw new StoryException($"{at}: each of \"words\" is text")).ToArray();
            if (deed.Words.Length > 0 && verb != "Email")
                throw new StoryException($"{at}: \"words\" are for an Email");
            var remember = StoryRules.Str(e, "remember", at);
            if (remember.Length > 0)
                deed.Remember = rules.Relation(remember)?.Id ?? throw new StoryException($"{at}: \"remember\" is a relation the faction holds towards the doer: declare {remember} (Story/Relations)");
            deed.Standing = Int(e, "standing", at, -MaxStanding, MaxStanding, 0);
            if (e.TryGetProperty("wants", out var ws))
            {
                if (ws.ValueKind != JsonValueKind.Object)
                    throw new StoryException($"{at}: \"wants\" is like {{\"Hardware\": -10}}");
                deed.Wants = ws.EnumerateObject().Select(p => def.Wants.FirstOrDefault(w => w.Name.Equals(p.Name, StringComparison.OrdinalIgnoreCase)) is { } w
                    && p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetInt32(out var n) && n is >= -100 and <= 100 ? (w.Quality, n)
                    : throw new StoryException($"{at}: \"wants\": {p.Name} is one of the faction's wants, by a whole number from -100 to 100")).ToArray();
            }
            var every = StoryRules.Str(e, "every", at);
            if (every.Length > 0)
                deed.Every = StoryRules.Duration(every) is { } dd and > 0 and <= 86400 ? dd : throw new StoryException($"{at}: \"every\" is a time like \"10m\", up to 1d");
            if (deed.Remember < 0 && deed.Standing == 0 && deed.Wants.Length == 0)
                throw new StoryException($"{at}: a deed changes something (\"remember\", \"standing\" or \"wants\")");
            deed.Text = $"{verb} {theirs.DefaultIfEmpty("target")}{(deed.Words.Length > 0 ? $" with \"{string.Join("\", \"", deed.Words)}\"" : "")}: "
                + string.Join(", ", new[] { remember.Length > 0 ? $"{remember} +1" : "", deed.Standing != 0 ? $"standing {deed.Standing:+0;-0}" : "" }
                    .Concat(deed.Wants.Select(w => $"{rules.Qualities[w.Quality].Name} {w.Delta:+0;-0}")).Where(x => x.Length > 0))
                + (deed.Every > 0 ? $", once in {StoryRules.Span(deed.Every)}" : "");
            deeds.Add(deed);
        }
        if (deeds.Count > MaxDeeds)
            throw new StoryException($"{where}: {MaxDeeds} deeds at most");
        def.Deeds = deeds.ToArray();

        var techs = new List<FactionTech>();
        foreach (var e in StoryRules.List(root, "teaches", where))
        {
            var at = $"{where}: technology {techs.Count + 1}";
            if (e.ValueKind != JsonValueKind.Object)
                throw new StoryException($"{at}: like {{\"name\": \"Remote Control\", \"tier\": \"Registered\", \"parts\": [\"Remote Transmitter\"]}}");
            StoryRules.Keys(e, TechKeys, at);
            var tech = new FactionTech { Name = StoryRules.Str(e, "name", at, true), About = StoryRules.Str(e, "about", at), Id = techs.Count };
            at = $"{where}: {tech.Name}";
            Plain(tech.Name, at, 32);
            if (techs.Any(o => o.Name.Equals(tech.Name, StringComparison.OrdinalIgnoreCase)) || deeds.Any(o => o.Name.Equals(tech.Name, StringComparison.OrdinalIgnoreCase))
                || tiers.Any(o => o.Name.Equals(tech.Name, StringComparison.OrdinalIgnoreCase)) || tech.Name.Equals("Notorious", StringComparison.OrdinalIgnoreCase))
                throw new StoryException($"{at}: technologies, deeds and tiers share names (a fax's notices name them)");
            var tier = StoryRules.Str(e, "tier", at, true);
            tech.Tier = tiers.FindIndex(t => t.Name.Equals(tier, StringComparison.OrdinalIgnoreCase)) is var ti and >= 0 ? ti
                : throw new StoryException($"{at}: \"tier\" is one of the faction's tiers (no {tier})");
            tech.Parts = StoryRules.List(e, "parts", at).Select(x => x.ValueKind == JsonValueKind.String && x.GetString() is { Length: > 0 and <= 64 } s ? s
                : throw new StoryException($"{at}: each of \"parts\" is a construct part's name")).ToArray();
            if (tech.Parts.Length is 0 or > MaxTechParts)
                throw new StoryException($"{at}: \"parts\" names 1 to {MaxTechParts} parts");
            techs.Add(tech);
        }
        if (techs.Count > MaxTeaches)
            throw new StoryException($"{where}: {MaxTeaches} technologies at most");
        def.Teaches = techs.ToArray();

        def.Jobs = ReadJobs(fr, rules, root, def, where);
        def.Voice = Voice(root, where);
        def.Stranger = StoryRules.Str(root, "stranger", where);
        if (def.Stranger.Length > 20 || def.Stranger.Any(c => c != ' ' && (c < 'a' || c > 'z')))
            throw new StoryException($"{where}: \"stranger\" is a few lower-case words, like \"inmate\"");
        def.Courier = Line(root, "courier", where);

        if (root.TryGetProperty("rivals", out var rv))
        {
            if (rv.ValueKind != JsonValueKind.Object)
                throw new StoryException($"{where}: \"rivals\" is like {{\"Clan MacSlop\": 50}} (the share of a rise in its standing taken off theirs, in percent)");
            def.Rivals = rv.EnumerateObject().Select(p => p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetInt32(out var n) && n is >= 1 and <= 100 ? (p.Name, -1, n)
                : throw new StoryException($"{where}: \"rivals\": {p.Name} is a whole number from 1 to 100 (percent)")).ToArray();
            if (def.Rivals.Length > MaxRivals)
                throw new StoryException($"{where}: {MaxRivals} rivals at most");
        }
        return def;
    }

    private static FactionSettlement ReadSettlement(JsonElement e, string where)
    {
        if (e.ValueKind != JsonValueKind.Object)
            throw new StoryException($"{where}: an object (\"map\", \"lat\", \"lon\", \"buildings\")");
        StoryRules.Keys(e, SettlementKeys, where);
        var st = new FactionSettlement { Map = StoryRules.Str(e, "map", where, true), Lat = Real(e, "lat", where, -89, 89), Lon = Real(e, "lon", where, -360, 360) };
        var list = new List<(string, float, float, float)>();
        foreach (var b in StoryRules.List(e, "buildings", where, true))
        {
            StoryRules.Keys(b, BuildingKeys, where);
            list.Add((StoryRules.Str(b, "blueprint", where, true), Real(b, "east", where, -FactionSettlement.Reach, FactionSettlement.Reach),
                Real(b, "north", where, -FactionSettlement.Reach, FactionSettlement.Reach), Real(b, "yaw", where, -360, 360)));
        }
        if (list.Count is 0 or > FactionSettlement.MaxBuildings)
            throw new StoryException($"{where}: 1 to {FactionSettlement.MaxBuildings} buildings");
        st.Buildings = [.. list];
        return st;
    }

    private static float Real(JsonElement e, string key, string where, float min, float max)
    {
        if (!e.TryGetProperty(key, out var v))
            return 0;
        return v.ValueKind == JsonValueKind.Number && v.TryGetSingle(out var n) && n >= min && n <= max ? n : throw new StoryException($"{where}: \"{key}\" is a number from {min} to {max}");
    }

    private static int Int(JsonElement e, string key, string where, int min, int max, int? fallback)
    {
        if (!e.TryGetProperty(key, out var v))
            return fallback ?? throw new StoryException($"{where}: \"{key}\" is missing");
        return v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) && n >= min && n <= max ? n : throw new StoryException($"{where}: \"{key}\" is a whole number from {min} to {max}");
    }

    private static void Plain(string s, string where, int max)
    {
        if (s.Length == 0 || s.Length > max || s.Any(c => c < ' ' || c > '~'))
            throw new StoryException($"{where}: a name is 1 to {max} printable ASCII characters");
    }
}

file static class StringExt
{
    public static string DefaultIfEmpty(this string s, string or) => s.Length > 0 ? s : or;
}

/// <summary>Something a faction would say about: a deed (its index), a tier its standing with a party came to (<see cref="Deed"/> -1), a technology it taught them (both -1), or, all -1, the party became Notorious.</summary>
public readonly record struct FactionNotice(int Faction, int Deed, int Tier, int Actor, int Party, int Tech = -1, int Job = -1);

/// <summary>
/// The factions in the story world (docs/story/STORY.md "Factions"): each is a Faction entity with a home place, members
/// (people who are <c>MemberOf</c> it) and what it claims (constructs, by name). It wants things (its qualities, which
/// drift by the hour), and it remembers what is done to it: a deed is an event against something of its, which adds to a
/// relation it holds towards the doer (<c>WreckedOurs</c>) and moves its standing with them and their group. Standing runs
/// from -100 to 100 and is held as <c>Likes</c> (above 0) or <c>Grudge</c> (below), so queries, patterns, the drama manager
/// and the fax's pages read it like any feeling. A group is a player faction (a Faction entity the game keeps players
/// <c>MemberOf</c>); a player in none is their own group. All of it is world state, so it is saved with the story.
/// </summary>
public sealed partial class StoryFactions
{
    private readonly StoryEngine _e;
    private bool _ready;
    private int[] _ids = []; // per faction, its entity
    private bool[] _verbs = []; // per verb: a deed listens for it
    private double _driftAt;
    private double[] _drift = []; // per faction and want, the part of a point not yet applied
    private readonly Dictionary<(int Faction, int Deed, int Party), double> _deedAt = new();
    private readonly List<(int Faction, int Party, int Tier, int Actor)> _rated = new(8);
    private string? _words; // the email being recorded
    private int[][] _techIds = []; // per faction and technology, its Thing (what Learnt points at)
    private bool[][] _staffIn = []; // per faction and staff name: found in the story and made a member

    /// <summary>What the factions would say about, oldest first, for the game to send (a fax); it empties them.</summary>
    public readonly List<FactionNotice> Notices = new(16);

    /// <summary>Technologies taught since the game last looked (faction, technology, the party taught, who brought it about), oldest first; the game empties it.</summary>
    public readonly List<(int Faction, int Tech, int Party, int Actor)> Taught = new(8);

    public long Deeds;

    public StoryFactions(StoryEngine engine) => _e = engine;

    private FactionRules R => _e.Rules.Factions;
    private StoryWorld W => _e.World;

    public int Count => R.Factions.Length;
    public FactionDef Def(int f) => R.Factions[f];

    public void Clear()
    {
        _ready = false;
        _ids = [];
        _deedAt.Clear();
        _rated.Clear();
        Notices.Clear();
        Taught.Clear();
        _techIds = [];
        _staffIn = [];
        _jobIds = [];
        Finished.Clear();
        _finishing = (-1, -1, -1);
        _driftAt = 0;
        Deeds = 0;
    }

    /// <summary>A stable key from a name (as the game keys places: FNV-1a over the lower case).</summary>
    public static ulong Key(string name)
    {
        var h = 14695981039346656037UL;
        foreach (var ch in name)
            h = (h ^ char.ToLowerInvariant(ch)) * 1099511628211UL;
        return h == 0 ? 1 : h;
    }

    /// <summary>
    /// The factions are in the world: each one's entity, home and members, made when missing (a new story, an old save, a
    /// faction added to the data), found again by key otherwise. Cheap after the first call.
    /// </summary>
    public void Ensure()
    {
        if (_ready)
            return;
        _ready = true;
        var r = R;
        var now = _e.Time;
        _ids = new int[r.Factions.Length];
        _drift = new double[r.Factions.Length * FactionRules.MaxWants];
        _verbs = new bool[_e.Rules.Verbs.Length];
        _techIds = new int[r.Factions.Length][];
        _staffIn = new bool[r.Factions.Length][];
        for (var f = 0; f < r.Factions.Length; f++)
        {
            var def = r.Factions[f];
            _techIds[f] = new int[def.Teaches.Length];
            _staffIn[f] = new bool[def.Staff.Length];
            for (var t = 0; t < def.Teaches.Length; t++)
                _techIds[f][t] = W.Get(StoryKind.Thing, Key($"tech:{def.Name}:{def.Teaches[t].Name}"), def.Teaches[t].Name, now);
            foreach (var d in def.Deeds)
                _verbs[d.Verb] = true;
            var key = Key("faction:" + def.Name);
            var id = W.Find(StoryKind.Faction, key);
            var fresh = id < 0;
            if (fresh)
                id = W.Create(StoryKind.Faction, def.Name, key, now);
            _ids[f] = id;
            if (id < 0)
                continue;
            if (fresh)
                foreach (var w in def.Wants)
                    W.SetQuality(id, w.Quality, w.Start);
            var home = -1;
            if (def.Home.Length > 0)
            {
                home = W.Get(StoryKind.Place, Key("place:" + def.Home), def.Home, now);
                if (r.Home >= 0)
                    W.Set(id, r.Home, home, 1);
            }
            foreach (var m in def.Members)
            {
                var mk = Key($"member:{def.Name}:{m.Name}");
                var mid = W.Find(StoryKind.Person, mk);
                if (mid < 0)
                {
                    mid = W.Create(StoryKind.Person, m.Name, mk, now);
                    if (mid < 0)
                        continue;
                    foreach (var (q, v) in m.Qualities)
                        W.SetQuality(mid, q, v);
                    if (home >= 0 && r.Home >= 0)
                        W.Set(mid, r.Home, home, 1);
                }
                W.Set(mid, r.MemberOf, id, 1);
            }
        }
        EnsureJobs();
        BindStaff();
    }

    /// <summary>People of a cast who are a faction's staff (the colony's warden and guards are the Ministry's): members once they are in the story.</summary>
    private void BindStaff()
    {
        var r = R;
        for (var f = 0; f < _staffIn.Length; f++)
        {
            var staff = r.Factions[f].Staff;
            for (var k = 0; k < staff.Length; k++)
            {
                if (_staffIn[f][k] || _ids[f] < 0)
                    continue;
                var who = W.Find(staff[k]);
                if (who < 0 || W[who].Kind != StoryKind.Person)
                    continue;
                _staffIn[f][k] = true;
                W.Set(who, r.MemberOf, _ids[f], 1);
            }
        }
    }

    // ---- Technologies -------------------------------------------------------------------------------------------

    /// <summary>Has this party (a player or group) been taught a faction's technology? Learnt for good, whatever the standing does after.</summary>
    public bool Learnt(int party, int f, int tech)
    {
        Ensure();
        if (R.Learnt < 0 || !W.Valid(party) || (uint)f >= (uint)_techIds.Length || (uint)tech >= (uint)_techIds[f].Length)
            return false;
        return W.Get(party, R.Learnt, _techIds[f][tech]) > 0;
    }

    /// <summary>Has this player been taught it, themselves or through their group?</summary>
    public bool Knows(int player, int f, int tech) => Learnt(player, f, tech) || (Party(player) is var g && g != player && Learnt(g, f, tech));

    /// <summary>A faction's technologies this party has learnt, a bit each (bit 0: its first).</summary>
    public byte LearntMask(int party, int f)
    {
        var m = 0;
        if ((uint)f < (uint)R.Factions.Length)
            for (var t = 0; t < R.Factions[f].Teaches.Length; t++)
                if (Learnt(party, f, t))
                    m |= 1 << t;
        return (byte)m;
    }

    /// <summary>
    /// A party's standing came to a tier: the faction teaches them every technology of that tier or below they don't know yet.
    /// A Teach event each (the radio can tell of it), a notice (a fax can print it), and <see cref="Taught"/> for the game.
    /// </summary>
    private void TeachUpTo(int f, int party, int tier, int actor)
    {
        var def = R.Factions[f];
        if (def.Teaches.Length == 0 || R.Learnt < 0 || Notorious(party))
            return;
        for (var t = 0; t < def.Teaches.Length; t++)
        {
            if (def.Teaches[t].Tier > tier || Learnt(party, f, t))
                continue;
            W.Set(party, R.Learnt, _techIds[f][t], 1);
            if (R.Teach >= 0)
                _e.Record(R.Teach, _ids[f], party, item: _techIds[f][t], value: Standing(f, party));
            if (Notices.Count < 64)
                Notices.Add(new FactionNotice(f, -1, -1, actor, party, t));
            if (Taught.Count < 64)
                Taught.Add((f, t, party, actor));
        }
    }

    /// <summary>
    /// Teaches a party what their standing already earns (a group a player has just joined, an old save from before the
    /// technologies): every faction, every tier up to the one they are in. Nothing happens when they know it all.
    /// </summary>
    public void TeachEarned(int party, int actor)
    {
        Ensure();
        if (!W.Valid(party))
            return;
        for (var f = 0; f < R.Factions.Length; f++)
            if (R.Factions[f].Teaches.Length > 0 && _ids[f] >= 0)
                TeachUpTo(f, party, R.Factions[f].TierOf(Standing(f, party)), actor);
    }

    /// <summary>A faction's entity (-1: none).</summary>
    public int EntityOf(int f)
    {
        Ensure();
        return (uint)f < (uint)_ids.Length ? _ids[f] : -1;
    }

    /// <summary>The faction called that (any case), or -1.</summary>
    public int Find(string name)
    {
        var fs = R.Factions;
        for (var f = 0; f < fs.Length; f++)
            if (fs[f].Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return f;
        return -1;
    }

    /// <summary>The faction whose entity this is, or -1.</summary>
    public int FactionOf(int entity)
    {
        for (var f = 0; f < _ids.Length; f++)
            if (_ids[f] == entity && entity >= 0)
                return f;
        return -1;
    }

    /// <summary>The faction this is theirs: the faction itself, one of its members, or a construct it claims. -1: nobody's.</summary>
    public int OwnerOf(int entity)
    {
        if (!W.Valid(entity))
            return -1;
        if (FactionOf(entity) is var f and >= 0)
            return f;
        var r = R;
        switch (W[entity].Kind)
        {
            case StoryKind.Person:
            case StoryKind.Player:
                var (a, b) = W.Range(entity, r.MemberOf);
                return b > a ? FactionOf(W[entity].Pairs[a].Target) : -1;
            case StoryKind.Construct:
                for (f = 0; f < _ids.Length; f++)
                    if (_ids[f] >= 0 && W.Get(_ids[f], r.Claims, entity) > 0)
                        return f;
                return -1;
        }
        return -1;
    }

    /// <summary>Who a faction's standing with someone is kept for: their group (the player faction they are <c>MemberOf</c>), else themselves.</summary>
    public int Party(int who)
    {
        if (!W.Valid(who) || R.MemberOf < 0)
            return who;
        var (a, b) = W.Range(who, R.MemberOf);
        if (b <= a)
            return who;
        var g = W[who].Pairs[a].Target;
        return FactionOf(g) < 0 ? g : who;
    }

    /// <summary>A player faction's entity, made the first time (found again by its name).</summary>
    public int Group(string name) => W.Get(StoryKind.Faction, Key("group:" + name), name, _e.Time);

    /// <summary>Puts a player in a group (a player faction's entity), or in none (-1). Nothing happens when it is already so.</summary>
    public void SetGroup(int player, int group)
    {
        var rel = R.MemberOf;
        if (rel < 0 || !W.Valid(player))
            return;
        var (a, b) = W.Range(player, rel);
        var now = b > a ? W[player].Pairs[a].Target : -1;
        if (now == group)
            return;
        if (group >= 0)
            W.Set(player, rel, group, 1);
        else
            W.Set(player, rel, now, 0);
    }

    /// <summary>A person who works for a faction (an outpost's people: Game/GameOutposts.cs): its member from now on.</summary>
    public void Enlist(int person, int f)
    {
        if (EntityOf(f) is var id and >= 0 && W.Valid(person) && R.MemberOf >= 0)
            W.Set(person, R.MemberOf, id, 1);
    }

    /// <summary>A construct a faction built itself (a building of its settlement) is its own.</summary>
    public void ClaimFor(int f, int construct)
    {
        if (EntityOf(f) is var id and >= 0 && W.Valid(construct) && R.Claims >= 0)
            W.Set(id, R.Claims, construct, 1);
    }

    /// <summary>A construct the story has just met, by its name: a faction whose claim it starts with takes it as its own.</summary>
    public void Claim(int construct, string name)
    {
        Ensure();
        var fs = R.Factions;
        for (var f = 0; f < fs.Length; f++)
            foreach (var c in fs[f].Claims)
                if (name.StartsWith(c, StringComparison.OrdinalIgnoreCase) && _ids[f] >= 0)
                {
                    W.Set(_ids[f], R.Claims, construct, 1);
                    return;
                }
    }

    /// <summary>A faction's standing with a party, from -100 (its grudge) to 100 (its liking).</summary>
    public int Standing(int f, int party)
    {
        var id = EntityOf(f);
        if (id < 0)
            return 0;
        if (Notorious(party))
            return -FactionRules.MaxStanding; // (every faction hunts them, whatever it thought before)
        return W.Get(id, R.Likes, party) - W.Get(id, R.Grudge, party);
    }

    /// <summary>A party every faction hunts: it has done something none forgives (an Atrocity: the world-wide state on top of each faction's own).</summary>
    public bool Notorious(int party) => R.Notorious >= 0 && W.Valid(party) && W.Quality(party, R.Notorious) > 0;

    /// <summary>
    /// A story start's standing (docs/story/STARTS.md): the player (or their group) begins on <paramref name="s"/> with the
    /// faction, when it has made nothing of them yet. True: set.
    /// </summary>
    public bool StartStanding(int f, int who, int s)
    {
        var id = EntityOf(f);
        var party = Party(who);
        if (id < 0 || !W.Valid(party) || Standing(f, party) != 0)
            return false;
        SetStanding(id, party, s);
        return true;
    }

    /// <summary>A hand check's shortcut (--outpost-check): a faction's standing with a party set outright.</summary>
    public void SetStandingForCheck(int f, int party, int s)
    {
        if (EntityOf(f) is var id and >= 0 && W.Valid(party))
            SetStanding(id, party, s);
    }

    private void SetStanding(int id, int party, int s)
    {
        s = Math.Clamp(s, -FactionRules.MaxStanding, FactionRules.MaxStanding);
        W.Set(id, R.Likes, party, Math.Max(0, s));
        W.Set(id, R.Grudge, party, Math.Max(0, -s));
    }

    /// <summary>
    /// A player emails a faction (the game's computers): an Email event, and the deeds that listen for one read its words.
    /// Returns the event's id (-1: no such faction or verb).
    /// </summary>
    public int Email(int f, int actor, string text, int place = -1)
    {
        var id = EntityOf(f);
        if (id < 0 || R.Email < 0)
            return -1;
        _words = text;
        try
        {
            return _e.Record(R.Email, actor, id, place: place);
        }
        finally
        {
            _words = null;
        }
    }

    /// <summary>The engine, after each event: the deeds it is, and what the factions make of them.</summary>
    public void OnEvent(in StoryEvent ev)
    {
        if (ev.Verb == R.Atrocity && R.Atrocity >= 0 && W.Valid(ev.Actor))
            Atrocity(ev.Actor);
        if (ev.Verb == R.Job && R.Job >= 0)
            PayJob(ev);
        if (_ids.Length == 0 || (uint)ev.Verb >= (uint)_verbs.Length || !_verbs[ev.Verb] || !W.Valid(ev.Actor) || W[ev.Actor].Kind != StoryKind.Player)
            return;
        var fs = R.Factions;
        for (var f = 0; f < fs.Length; f++)
        {
            foreach (var d in fs[f].Deeds)
            {
                if (d.Verb != ev.Verb || OwnerOf(ev.Role(d.Theirs)) != (d.Whose >= 0 ? d.Whose : f) || !Said(d))
                    continue;
                var party = Party(ev.Actor);
                if (d.Every > 0)
                {
                    if (_deedAt.TryGetValue((f, d.Id, party), out var at) && _e.Time - at < d.Every)
                        continue;
                    _deedAt[(f, d.Id, party)] = _e.Time;
                }
                Done(f, d, ev.Actor, party);
            }
        }
    }

    /// <summary>An atrocity: the doer's group is Notorious (the doer too), and every faction has something to say about it.</summary>
    private void Atrocity(int actor)
    {
        var party = Party(actor);
        if (R.Notorious < 0)
            return;
        if (party != actor)
            W.SetQuality(party, R.Notorious, W.Quality(party, R.Notorious) + 1); // (the verb's own effect counts the doer)
        Ensure();
        for (var f = 0; f < _ids.Length; f++)
            if (_ids[f] >= 0 && Notices.Count < 64)
                Notices.Add(new FactionNotice(f, -1, -1, actor, party));
    }

    private bool Said(FactionDeed d)
    {
        if (d.Words.Length == 0)
            return true;
        if (_words == null)
            return false;
        var text = " " + _words + " ";
        foreach (var w in d.Words)
            if (text.Contains(w, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    private void Done(int f, FactionDeed d, int actor, int party)
    {
        var id = _ids[f];
        Deeds++;
        Touch(f, d, actor, actor == party, actor);
        if (party != actor)
            Touch(f, d, party, true, actor); // (their group remembers it too, and its standing is the one that counts)
        foreach (var (q, delta) in d.Wants)
            W.SetQuality(id, q, Math.Clamp(W.Quality(id, q) + delta, 0, 100));
        if (Notices.Count < 64)
            Notices.Add(new FactionNotice(f, d.Id, -1, actor, party));
    }

    private void Touch(int f, FactionDeed d, int who, bool rate, int actor)
    {
        var id = _ids[f];
        if (d.Remember >= 0)
            W.Add(id, d.Remember, who, 1);
        if (d.Standing == 0)
            return;
        Move(f, who, d.Standing, rate, actor);
        if (d.Standing <= 0)
            return;
        foreach (var (_, g, percent) in R.Factions[f].Rivals) // (rising with one cools its rivals: a share of the rise off theirs, a point at least)
            if (_ids[g] >= 0)
                Move(g, who, -Math.Max(1, d.Standing * percent / 100), rate, actor);
    }

    /// <summary>Moves a faction's standing with someone; a party's that comes to a new tier is rated after the event (<see cref="Flush"/>).</summary>
    private void Move(int f, int who, int delta, bool rate, int actor)
    {
        var def = R.Factions[f];
        var was = Standing(f, who);
        SetStanding(_ids[f], who, was + delta);
        if (rate && def.TierOf(Standing(f, who)) is var t1 and >= 0 && t1 != def.TierOf(was))
            _rated.Add((f, who, t1, actor));
    }

    /// <summary>The engine, once an event and all it led to are recorded: a party's standing that came to a new tier is a Rate event ("IT Department now rates Karl: Flagged").</summary>
    public void Flush()
    {
        while (_rated.Count > 0)
        {
            var (f, party, tier, actor) = _rated[0];
            _rated.RemoveAt(0);
            var def = R.Factions[f];
            if (R.Rate >= 0)
            {
                var thing = W.Get(StoryKind.Thing, Key($"tier:{def.Name}:{def.Tiers[tier].Name}"), def.Tiers[tier].Name, _e.Time);
                _e.Record(R.Rate, _ids[f], party, item: thing, value: Standing(f, party));
            }
            if (Notices.Count < 64)
                Notices.Add(new FactionNotice(f, -1, tier, actor, party));
            TeachUpTo(f, party, tier, actor);
        }
    }

    /// <summary>The clock (once a second from the engine): every minute each faction's wants drift by their "perHour".</summary>
    public void Update(double now)
    {
        if (now < _driftAt)
            return;
        BindStaff(); // (a cast added since: once a minute)
        var first = _driftAt == 0;
        _driftAt = now + 60;
        if (first)
            return;
        var fs = R.Factions;
        for (var f = 0; f < fs.Length; f++)
        {
            var id = _ids[f];
            if (id < 0)
                continue;
            for (var k = 0; k < fs[f].Wants.Length; k++)
            {
                var w = fs[f].Wants[k];
                if (w.PerHour == 0)
                    continue;
                ref var acc = ref _drift[f * FactionRules.MaxWants + k];
                acc += w.PerHour / 60;
                var whole = (int)acc;
                if (whole == 0)
                    continue;
                acc -= whole;
                W.SetQuality(id, w.Quality, Math.Clamp(W.Quality(id, w.Quality) + whole, 0, 100));
            }
        }
    }
}
