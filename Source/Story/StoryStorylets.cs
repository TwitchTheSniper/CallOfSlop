using System.Text;
using System.Text.Json;

namespace CallOfSlop.Story;

/// <summary>
/// Which queue a storylet waits in: the A plot and the B plot take turns; an urgent one jumps the queue. A side road
/// (<see cref="Side"/>: the convicts' break-out) runs in a lane of its own beside them, one at a time, so it can be taken
/// while a duty or the main quest holds the player's other lane (Karl, 2026-11-13: "branch off the main quest at any point").
/// </summary>
public enum StoryletBucket : byte
{
    A,
    B,
    Urgent,
    Side,
}

/// <summary>A quality the storylets keep on a player (Rank, Suspicion): progress or menace, its range, and how it cools off.</summary>
public sealed class StoryletQuality
{
    public string Name = "", About = "", File = "";
    public ushort Id; // the story quality (StoryRules.Qualities)
    public bool Menace;
    public int Min, Max = 100;
    public int Decay; // taken off each DecayEvery with no rise in between (0: none)
    public double DecayEvery;
}

/// <summary>A spot the game marks a player as having visited (<c>?me Visited 'Kettle'</c> counts the visits): near a person's body.</summary>
public sealed class StoryletSpot
{
    public string Name = "", Near = "";
    public float Reach = 5;
}

/// <summary>An event an end waits for: a verb, and for each role a variable that must match, a name, or anyone.</summary>
public sealed class StoryletWhen
{
    public ushort Verb;
    public readonly sbyte[] Roles = [-1, -1, -1, -1, -1]; // per StoryRole: a storylet variable, -1 any
    public readonly string?[] Names = new string?[5]; // per StoryRole: an entity by name (when Roles is -1)
    public string Text = "";
}

/// <summary>One way a storylet ends: an event (<c>when</c>), a time (<c>after</c>), or a player's choice; with what it changes.</summary>
public sealed class StoryletEnd
{
    public string Id = "", Text = "";
    public int Index;
    public StoryletWhen? When;
    public double Within; // s: a "when" end counts only this long after the storylet began waiting (0: any time)
    public double After; // s: a time end, after the storylet began waiting
    public string Choice = ""; // a player's answer, offered in the talk panel of the one who asks (By)
    public StoryQuery? If; // checked when the end would happen
    public StoryEffect[] Then = [];
    public string[] Say = [];
    public int By = -1; // the variable who says it (and asks the choice), -1 the lead
    public string Hook = "";
    public int Learn = -1, LearnFrom = -1; // a fact the player learns (a variable, usually a named one) and from whom (-1 nobody)
    public string[] World = []; // the author's plain words for what physically happens (the story map shows them; the game does nothing with them)
    public string Next = ""; // a storylet this one leads to (only for the builder and the checks: the next one still needs its needs)
}

/// <summary>
/// A storylet (docs/story/STORY.md "Storylets"): an authored beat that waits for the world to be ready. Its needs are
/// queries over the player (<c>?me</c>); its cast is filled from the living people; its beats are a play; it may hand
/// out a faction job or start a game hook; and it ends by an event, a time or a choice, each changing the world.
/// </summary>
public sealed class StoryletDef
{
    public string Id = "", Title = "", About = "", File = "";
    public int Index;
    public StoryletBucket Bucket;
    public bool Group; // its ends change every player in the starter's group
    public double Again; // s before a player may have it again (0: once)
    public int Priority;
    public StoryQuery[] Needs = []; // any one holds
    public StoryQuery? Cast;
    public int PreferVar = -1, PreferRel = -1, PreferObj = -1; // "prefer": the cast's choice by a relation's strength (or a quality's)
    public bool PreferQuality;
    public string PreferText = "";
    public string[] Vars = []; // "me", the cast's roles, then the named things ("#0"...)
    public int CastVars; // me and the cast's roles
    public string[] Named = []; // the named things' names, from CastVars on
    public int Play = -1; // its beats as a play (PlayRules), -1 none
    public int[] PlayRole = []; // per play role: the storylet variable
    public int JobFaction = -1, Job = -1;
    public string JobText = "", Hook = "";
    public StoryletEnd[] Ends = [];
    public bool Final; // nothing is meant to follow (the checks don't call it a dead end)
    public bool HasChoice;
}

/// <summary>The storylets from Story/Storylets/*.json (format <see cref="Format"/>). A bad file is refused whole with a reason.</summary>
public sealed class StoryletRules
{
    public const string Format = "call-of-slop-storylets/1";
    public const int MaxStorylets = 500, MaxEnds = 8, MaxNeeds = 4;
    public static readonly string[] Hooks = ["lockdown", "depot", "breakout", "pcvirus"]; // what the game knows how to start (Game/GameStoryStorylets.cs)
    public const string ErrandHook = "errand:"; // and "errand:ID": an errand in Story/Errands (Game/GameErrands.cs; --check-story checks the id)

    public StoryletDef[] Storylets = [];
    public StoryletQuality[] Qualities = [];
    public StoryletSpot[] Spots = [];
    public int OnStorylet = -1, StoryletAt = -1, Played = -1, Lead = -1, Visited = -1, In = -1; // relations
    public int Ended = -1, EndedWith = -1; // relations: which ends a player has had (a bit each), and who led it last (entity + 1): the Logbook's Paths
    public int VerbStorylet = -1, VerbLearn = -1, VerbChoose = -1; // verbs

    public StoryletDef? Find(string id) => Array.Find(Storylets, s => s.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    private static readonly string[] FileKeys = ["format", "about", "qualities", "spots", "storylets"];
    private static readonly string[] QualityKeys = ["name", "about", "kind", "min", "max", "decay"];
    private static readonly string[] SpotKeys = ["name", "near", "reach"];
    private static readonly string[] StoryletKeys = ["id", "title", "about", "bucket", "scope", "again", "priority", "needs", "cast", "prefer", "beats", "job", "hook", "ends", "final"];
    private static readonly string[] EndKeys = ["id", "when", "within", "after", "choice", "if", "then", "say", "by", "hook", "learn", "from", "next", "world"];
    private static readonly string[] WhenKeys = ["verb", "actor", "target", "owner", "place", "item"];

    /// <summary>
    /// First pass (before anything that compiles a query): the qualities the storylet files declare become story
    /// qualities, so patterns, lines and goals can read Rank and Suspicion too.
    /// </summary>
    public static List<StoryletQuality> ReadQualities(StoryRules rules, string folder)
    {
        var all = new List<StoryletQuality>();
        foreach (var f in StoryRules.Files(Path.Combine(folder, "Storylets")))
        {
            var file = $"Storylets/{Path.GetFileName(f)}";
            try
            {
                using var doc = StoryRules.Parse(StoryRules.ReadFile(f), file);
                var mine = new List<StoryletQuality>();
                foreach (var q in StoryRules.List(doc.RootElement, "qualities", file))
                {
                    if (q.ValueKind != JsonValueKind.Object)
                        throw new StoryException($"{file}: a quality is like {{\"name\": \"Rank\", \"kind\": \"progress\", \"max\": 5}}");
                    var name = StoryRules.Str(q, "name", $"{file}: a quality", true);
                    var where = $"{file}: quality {name}";
                    StoryRules.Keys(q, QualityKeys, where);
                    var kind = StoryRules.Str(q, "kind", where);
                    var sq = new StoryletQuality
                    {
                        Name = name, About = StoryRules.Str(q, "about", where), File = file,
                        Menace = kind switch
                        {
                            "" or "progress" => false,
                            "menace" => true,
                            _ => throw new StoryException($"{where}: \"kind\" is progress or menace"),
                        },
                        Min = Int(q, "min", where, 0, -1000, 1000),
                        Max = Int(q, "max", where, 100, -1000, 100_000),
                    };
                    if (sq.Max < sq.Min)
                        throw new StoryException($"{where}: \"max\" is below \"min\"");
                    var decay = StoryRules.Str(q, "decay", where);
                    if (decay.Length > 0)
                    {
                        // "-1 per 30m"
                        var t = decay.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (t.Length != 3 || t[1] != "per" || !int.TryParse(t[0], out var d) || d >= 0 || d < -1000
                            || StoryRules.Duration(t[2]) is not { } every || every < 60)
                            throw new StoryException($"{where}: \"decay\" is like \"-1 per 30m\" (at least a minute)");
                        (sq.Decay, sq.DecayEvery) = (-d, every);
                    }
                    if (rules.Quality(name) is { } existing)
                    {
                        if (all.Concat(mine).Any(o => o.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                            throw new StoryException($"{where}: declared twice");
                        sq.Id = existing.Id; // (Relations.json has it already: the storylets give it a range)
                    }
                    else if (rules.Relation(name) != null || !StoryRules.Word(name))
                        throw new StoryException($"{where}: a name is one word that isn't a relation's");
                    mine.Add(sq);
                }
                foreach (var sq in mine)
                    if (rules.Quality(sq.Name) == null)
                        sq.Id = rules.AddQuality(sq.Name, sq.About);
                all.AddRange(mine);
            }
            catch (StoryException ex)
            {
                rules.Errors.Add(ex.Message);
                Console.WriteLine($"[story] storylet qualities skipped: {ex.Message}");
            }
        }
        return all;
    }

    public static StoryletRules Load(StoryRules rules, string folder, List<StoryletQuality> qualities)
    {
        var sr = new StoryletRules { Qualities = qualities.ToArray() };
        int Rel(string n) => rules.Relation(n)?.Id ?? -1;
        (sr.OnStorylet, sr.StoryletAt, sr.Played, sr.Lead, sr.Visited, sr.In) = (Rel("OnStorylet"), Rel("StoryletAt"), Rel("Played"), Rel("Lead"), Rel("Visited"), Rel("In"));
        (sr.Ended, sr.EndedWith) = (Rel("Ended"), Rel("EndedWith"));
        (sr.VerbStorylet, sr.VerbLearn, sr.VerbChoose) = (rules.VerbId("Storylet"), rules.VerbId("Learn"), rules.VerbId("Choose"));
        var files = StoryRules.Files(Path.Combine(folder, "Storylets"));
        if (files.Length == 0)
            return sr;
        if (sr.OnStorylet < 0 || sr.StoryletAt < 0 || sr.Played < 0 || sr.VerbStorylet < 0)
        {
            rules.Errors.Add("Storylets: the relations OnStorylet, StoryletAt and Played and the verb Storylet are missing (Story/Relations/Storylets.json, Story/Verbs/Storylets.json): no storylets");
            return sr;
        }
        var all = new List<StoryletDef>();
        var spots = new List<StoryletSpot>();
        var plays = rules.Plays.Plays.ToList();
        foreach (var f in files)
        {
            var file = $"Storylets/{Path.GetFileName(f)}";
            var playsBefore = plays.Count;
            try
            {
                using var doc = StoryRules.Parse(StoryRules.ReadFile(f), file);
                var root = doc.RootElement;
                StoryRules.Keys(root, FileKeys, file);
                var format = StoryRules.Str(root, "format", file, true);
                if (format != Format)
                    throw new StoryException($"{file}: \"format\" is \"{format}\": this build reads \"{Format}\"");
                var mineSpots = new List<StoryletSpot>();
                foreach (var s in StoryRules.List(root, "spots", file))
                {
                    if (s.ValueKind != JsonValueKind.Object)
                        throw new StoryException($"{file}: a spot is like {{\"name\": \"Kettle\", \"near\": \"Officer Crumb\"}}");
                    var name = StoryRules.Str(s, "name", $"{file}: a spot", true);
                    var where = $"{file}: spot {name}";
                    StoryRules.Keys(s, SpotKeys, where);
                    var reach = s.TryGetProperty("reach", out var rv) ? rv.ValueKind == JsonValueKind.Number && rv.TryGetSingle(out var r) && r is >= 1 and <= 50 ? r : throw new StoryException($"{where}: \"reach\" is metres from 1 to 50") : 5f;
                    mineSpots.Add(new StoryletSpot { Name = name, Near = StoryRules.Str(s, "near", where, true), Reach = reach });
                }
                var mine = new List<StoryletDef>();
                foreach (var e in StoryRules.List(root, "storylets", file, true))
                    mine.Add(Read(rules, sr, e, file, all.Count + mine.Count, all.Concat(mine), plays));
                all.AddRange(mine);
                spots.AddRange(mineSpots);
            }
            catch (StoryException ex)
            {
                plays.RemoveRange(playsBefore, plays.Count - playsBefore);
                rules.Errors.Add(ex.Message);
                Console.WriteLine($"[story] storylets skipped: {ex.Message}");
            }
        }
        if (all.Count > MaxStorylets)
        {
            rules.Errors.Add($"Storylets: {all.Count} storylets, {MaxStorylets} at most: none loaded");
            return sr;
        }
        rules.Plays.Plays = plays.ToArray();
        sr.Storylets = all.ToArray();
        sr.Spots = spots.ToArray();
        return sr;
    }

    private static StoryletDef Read(StoryRules rules, StoryletRules sr, JsonElement e, string file, int index, IEnumerable<StoryletDef> before, List<PlayDef> plays)
    {
        if (e.ValueKind != JsonValueKind.Object)
            throw new StoryException($"{file}: a storylet is like {{\"id\": \"work-detail\", \"bucket\": \"A\", \"needs\": \"?me Inducted\", \"ends\": [...]}}");
        var id = StoryRules.Str(e, "id", $"{file}: storylet {index + 1}", true);
        var where = $"{file}: {id}";
        StoryRules.Keys(e, StoryletKeys, where);
        if (id.Length == 0 || id.Length > 48 || id.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_')))
            throw new StoryException($"{where}: an id is letters, digits, - and _ (48 at most)");
        if (before.Any(o => o.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
            throw new StoryException($"{where}: a storylet with that id already exists");
        var d = new StoryletDef { Id = id, Title = StoryRules.Str(e, "title", where) is { Length: > 0 } title ? title : id, About = StoryRules.Str(e, "about", where), File = file, Index = index };
        CheckText(d.Title, where, "title");
        d.Bucket = StoryRules.Str(e, "bucket", where, true) switch
        {
            "A" or "a" => StoryletBucket.A,
            "B" or "b" => StoryletBucket.B,
            "urgent" => StoryletBucket.Urgent,
            "side" => StoryletBucket.Side,
            _ => throw new StoryException($"{where}: \"bucket\" is A, B, urgent or side"),
        };
        d.Group = StoryRules.Str(e, "scope", where) switch
        {
            "" or "player" => false,
            "group" => true,
            _ => throw new StoryException($"{where}: \"scope\" is player or group"),
        };
        var again = StoryRules.Str(e, "again", where);
        if (again.Length > 0)
            d.Again = StoryRules.Duration(again) is { } a and >= 60 and <= 30 * 86400 ? a : throw new StoryException($"{where}: \"again\" is a time from 1m to 30d, like \"2h\"");
        d.Priority = Int(e, "priority", where, 0, -100, 100);
        d.Final = StoryRules.Bool(e, "final", where);

        // Needs: one query, or a list of them (any one holds). ?me is the player.
        var needs = new List<StoryQuery>();
        if (e.TryGetProperty("needs", out var nv))
        {
            var texts = nv.ValueKind switch
            {
                JsonValueKind.String => [nv.GetString() ?? ""],
                JsonValueKind.Array => nv.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() ?? "" : throw new StoryException($"{where}: each of \"needs\" is a query")).ToArray(),
                _ => throw new StoryException($"{where}: \"needs\" is a query, or a list of them (any one holds)"),
            };
            if (texts.Length is 0 or > MaxNeeds)
                throw new StoryException($"{where}: \"needs\" has 1 to {MaxNeeds} queries");
            foreach (var t in texts)
                needs.Add(Compile(t, rules, ["me"], where, "needs"));
        }
        d.Needs = needs.ToArray();

        // The cast: ?me bound, its other variables the roles (the first leads: a living person).
        var vars = new List<string> { "me" };
        var cast = StoryRules.Str(e, "cast", where);
        if (cast.Length > 0)
        {
            d.Cast = Compile(cast, rules, vars, where, "cast");
            if (vars.Count < 2)
                throw new StoryException($"{where}: \"cast\" names no roles (its first variable after ?me is the person who leads)");
        }
        d.CastVars = vars.Count;
        var prefer = StoryRules.Str(e, "prefer", where);
        if (prefer.Length > 0)
        {
            var t = prefer.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            int V(string s) => s.StartsWith('?') ? vars.IndexOf(s[1..]) : -1;
            if (t.Length is < 2 or > 3 || V(t[0]) < 0)
                throw new StoryException($"{where}: \"prefer\" is like \"?g Likes ?me\" or \"?g Brave\" (the strongest is cast)");
            d.PreferVar = V(t[0]);
            if (t.Length == 2 && rules.Quality(t[1]) is { } pq)
                (d.PreferRel, d.PreferQuality) = (pq.Id, true);
            else if (t.Length == 3 && rules.Relation(t[1]) is { } pr && V(t[2]) >= 0)
                (d.PreferRel, d.PreferObj) = (pr.Id, V(t[2]));
            else
                throw new StoryException($"{where}: \"prefer\": \"{prefer}\": a relation between two of the cast, or a quality");
            if (d.PreferVar != 1 || !d.PreferQuality && d.PreferObj != 0)
                throw new StoryException($"{where}: \"prefer\" ranks the lead (the cast's first role) by a quality of theirs or how they stand to ?me, like \"?g Likes ?me\"");
            d.PreferText = prefer;
        }

        // Named things in effects ('Old Mine'): slots after the cast's.
        var named = new List<string>();
        int Slot(string s)
        {
            if (s.StartsWith('?'))
                return vars.IndexOf(s[1..]) is var i and >= 0 && i < d.CastVars ? i : -1;
            if (s.StartsWith('#') && int.TryParse(s.AsSpan(1), out var n))
                return d.CastVars + n;
            return vars.IndexOf(s) is var j and >= 0 && j < d.CastVars ? j : -1;
        }
        StoryEffect[] Effects(JsonElement x, string at) => StoryRules.List(x, "then", at).Select(v => v.ValueKind == JsonValueKind.String
            ? ParseNamed(rules, v.GetString() ?? "", at, named, d.CastVars, Slot)
            : throw new StoryException($"{at}: each \"then\" is text, like \"?me Rank +1\"")).ToArray();
        int VarOf(string s, string at, string key)
        {
            var i = s.StartsWith('?') ? vars.IndexOf(s[1..]) : -1;
            return i >= 0 && i < d.CastVars ? i : throw new StoryException($"{at}: \"{key}\": {s} isn't ?me or one of the cast ({string.Join(", ", vars.Take(d.CastVars).Select(v => "?" + v))})");
        }

        // The beats: a play of the cast, the lead first (where it happens), ?me among them.
        if (e.TryGetProperty("beats", out _))
        {
            if (d.Cast == null)
                throw new StoryException($"{where}: \"beats\" need a \"cast\" (someone to say them)");
            var order = Enumerable.Range(1, d.CastVars - 1).Append(0).ToArray(); // play roles: the cast's, then me
            var playVars = order.Select(i => vars[i]).ToList();
            var roles = playVars.ToArray();
            var beats = PlayRules.ReadBeats(rules, e, where, playVars, roles, true);
            if (beats.Count == 0)
                throw new StoryException($"{where}: \"beats\" is empty");
            if (playVars.Count != roles.Length)
                throw new StoryException($"{where}: a beat's \"if\" names a variable the cast doesn't");
            var play = new PlayDef
            {
                Name = $"storylet: {d.Title}", About = d.About, File = file, Id = plays.Count, Roles = roles, CastVars = roles.Length,
                Cast = d.Cast, Beats = beats.ToArray(), External = true,
            };
            plays.Add(play);
            d.Play = play.Id;
            d.PlayRole = order;
        }

        var job = StoryRules.Str(e, "job", where);
        if (job.Length > 0)
        {
            var colon = job.IndexOf(':');
            var fac = colon > 0 ? rules.Factions.Find(job[..colon].Trim()) : null;
            var jn = colon > 0 ? job[(colon + 1)..].Trim() : "";
            var j = fac == null ? -1 : Array.FindIndex(fac.Jobs, x => x.Name.Equals(jn, StringComparison.OrdinalIgnoreCase));
            if (fac == null || j < 0)
                throw new StoryException($"{where}: \"job\": \"{job}\" is \"Faction: Job\" with a faction and one of its jobs (Story/Factions)");
            (d.JobFaction, d.Job, d.JobText) = (fac.Id, j, $"{fac.Name}: {fac.Jobs[j].Name}");
        }
        d.Hook = HookName(StoryRules.Str(e, "hook", where), where);

        var ends = new List<StoryletEnd>();
        foreach (var x in StoryRules.List(e, "ends", where))
        {
            var at = $"{where}: end {ends.Count + 1}";
            if (x.ValueKind != JsonValueKind.Object)
                throw new StoryException($"{at}: an end is like {{\"id\": \"done\", \"when\": {{\"verb\": \"Job\", \"actor\": \"?me\"}}, \"then\": [\"?me Rank +1\"]}}");
            StoryRules.Keys(x, EndKeys, at);
            var end = new StoryletEnd { Id = StoryRules.Str(x, "id", at, true), Index = ends.Count };
            at = $"{where}: end {end.Id}";
            if (!StoryRules.Word(end.Id.Replace('-', '_')) || ends.Any(o => o.Id == end.Id))
                throw new StoryException($"{at}: an end's id is a word, once per storylet");
            var kinds = 0;
            if (x.TryGetProperty("when", out var wv))
            {
                kinds++;
                end.When = When(rules, wv, at, s => VarOf(s, at, "when"));
                var within = StoryRules.Str(x, "within", at);
                if (within.Length > 0)
                    end.Within = StoryRules.Duration(within) is { } w and >= 1 and <= 30 * 86400 ? w : throw new StoryException($"{at}: \"within\" is a time, like \"30m\"");
            }
            else if (x.TryGetProperty("within", out _))
                throw new StoryException($"{at}: \"within\" goes with \"when\"");
            var after = StoryRules.Str(x, "after", at);
            if (after.Length > 0)
            {
                kinds++;
                end.After = StoryRules.Duration(after) is { } af and >= 1 and <= 30 * 86400 ? af : throw new StoryException($"{at}: \"after\" is a time, like \"30m\"");
            }
            end.Choice = StoryRules.Str(x, "choice", at);
            if (end.Choice.Length > 0)
            {
                kinds++;
                CheckText(end.Choice, at, "choice");
                if (end.Choice.Length > 60)
                    throw new StoryException($"{at}: \"choice\" is 60 characters at most (a button)");
                d.HasChoice = true;
            }
            if (kinds != 1)
                throw new StoryException($"{at}: an end has one of \"when\" (an event), \"after\" (a time) or \"choice\" (the player's answer)");
            var cond = StoryRules.Str(x, "if", at);
            if (cond.Length > 0)
                end.If = Compile(cond, rules, vars.Take(d.CastVars).ToList(), at, "if", d.CastVars);
            end.Then = Effects(x, at);
            end.Say = StoryRules.List(x, "say", at).Select(v => v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : throw new StoryException($"{at}: each of \"say\" is a line")).ToArray();
            if (end.Say.Length > LineRules.MaxLines)
                throw new StoryException($"{at}: \"say\" has {LineRules.MaxLines} lines at most");
            foreach (var line in end.Say)
                CheckLine(line, vars, d.CastVars, at);
            var by = StoryRules.Str(x, "by", at);
            end.By = by.Length > 0 ? VarOf(by, at, "by") : d.CastVars > 1 ? 1 : -1;
            if (end.By == 0)
                throw new StoryException($"{at}: \"by\" is one of the cast (the player says nothing)");
            if (end.Choice.Length > 0 && end.By < 0)
                throw new StoryException($"{at}: a choice is asked by one of the cast: give the storylet a \"cast\"");
            end.Hook = HookName(StoryRules.Str(x, "hook", at), at);
            var learn = StoryRules.Str(x, "learn", at);
            if (learn.Length > 0)
            {
                if (sr.Lead < 0)
                    throw new StoryException($"{at}: \"learn\" needs the relation Lead (Story/Relations/Storylets.json)");
                CheckText(learn, at, "learn");
                end.Learn = NamedSlot(learn.Trim('\''), named, d.CastVars);
                var from = StoryRules.Str(x, "from", at);
                end.LearnFrom = from.Length > 0 ? VarOf(from, at, "from") : end.By;
            }
            else if (x.TryGetProperty("from", out _))
                throw new StoryException($"{at}: \"from\" goes with \"learn\"");
            end.Next = StoryRules.Str(x, "next", at);
            end.World = StoryRules.List(x, "world", at).Select(v => v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : throw new StoryException($"{at}: each of \"world\" is a sentence")).ToArray();
            end.Text = end.When != null ? $"when {end.When.Text}{(end.Within > 0 ? $" within {StoryRules.Span(end.Within)}" : "")}"
                : end.After > 0 ? $"after {StoryRules.Span(end.After)}" : $"choice \"{end.Choice}\"";
            if (cond.Length > 0)
                end.Text += $" if {cond}";
            ends.Add(end);
            if (ends.Count > MaxEnds)
                throw new StoryException($"{where}: {MaxEnds} ends at most");
        }
        d.Ends = ends.ToArray();
        if (d.CastVars + named.Count > StoryRules.MaxVars)
            throw new StoryException($"{where}: ?me, the cast and the named things in its effects are {StoryRules.MaxVars} at most");
        d.Named = named.ToArray();
        d.Vars = [.. vars.Take(d.CastVars), .. named.Select((_, i) => $"#{i}")];
        return d;
    }

    private static string HookName(string hook, string where) =>
        hook.Length == 0 || Array.IndexOf(Hooks, hook) >= 0 || hook.StartsWith(ErrandHook, StringComparison.Ordinal) && hook.Length > ErrandHook.Length ? hook
            : throw new StoryException($"{where}: \"hook\": \"{hook}\" isn't one the game knows ({string.Join(", ", Hooks)}, {ErrandHook}ID)");

    internal static int NamedSlot(string name, List<string> named, int castVars)
    {
        var i = named.FindIndex(n => n.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (i < 0)
        {
            named.Add(name);
            i = named.Count - 1;
        }
        return castVars + i;
    }

    /// <summary>An effect that may name things in quotes ("?me Lead 'Old Mine'"): each name becomes a slot of its own.</summary>
    internal static StoryEffect ParseNamed(StoryRules rules, string text, string where, List<string> named, int castVars, Func<string, int> slot)
    {
        var sb = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '\'')
            {
                sb.Append(text[i]);
                continue;
            }
            var end = text.IndexOf('\'', i + 1);
            if (end < 0)
                throw new StoryException($"{where}: effect \"{text}\": a name in quotes isn't closed");
            var name = text[(i + 1)..end];
            CheckText(name, where, "a name");
            sb.Append('#').Append(NamedSlot(name, named, castVars) - castVars);
            i = end;
        }
        return rules.ParseEffect(sb.ToString(), where, slot);
    }

    private static StoryletWhen When(StoryRules rules, JsonElement e, string where, Func<string, int> var)
    {
        if (e.ValueKind != JsonValueKind.Object)
            throw new StoryException($"{where}: \"when\" is like {{\"verb\": \"Job\", \"actor\": \"?me\"}}");
        StoryRules.Keys(e, WhenKeys, where);
        var verb = StoryRules.Str(e, "verb", where, true);
        var v = rules.VerbId(verb);
        if (v < 0)
            throw new StoryException($"{where}: \"when\": no verb {verb}");
        var w = new StoryletWhen { Verb = (ushort)v };
        var text = new StringBuilder(verb);
        for (var r = 0; r < StoryRules.RoleNames.Length; r++)
        {
            var s = StoryRules.Str(e, StoryRules.RoleNames[r], where);
            if (s.Length == 0)
                continue;
            if (s.StartsWith('?'))
                w.Roles[r] = (sbyte)var(s);
            else
                w.Names[r] = s.Trim('\'');
            text.Append(' ').Append(StoryRules.RoleNames[r]).Append(' ').Append(s);
        }
        w.Text = text.ToString();
        return w;
    }

    private static StoryQuery Compile(string text, StoryRules rules, List<string> vars, string where, string key, int maxVars = StoryRules.MaxVars)
    {
        try
        {
            return StoryQuery.Compile(text, rules, vars, maxVars);
        }
        catch (StoryException ex)
        {
            throw new StoryException($"{where}: \"{key}\": \"{text}\": {ex.Message}");
        }
    }

    private static int Int(JsonElement e, string key, string where, int otherwise, int min, int max)
    {
        if (!e.TryGetProperty(key, out var v))
            return otherwise;
        return v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) && n >= min && n <= max ? n : throw new StoryException($"{where}: \"{key}\" is a whole number from {min} to {max}");
    }

    private static void CheckText(string s, string where, string key)
    {
        if (s.Length == 0 || s.Length > LineRules.MaxLength || s.Any(c => c < ' ' || c > '~'))
            throw new StoryException($"{where}: {key} \"{s}\": printable ASCII, 1 to {LineRules.MaxLength} characters");
    }

    /// <summary>An end's line: printable ASCII, short, filling only {me}, the cast's roles and {here}.</summary>
    private static void CheckLine(string line, List<string> vars, int castVars, string where)
    {
        CheckText(line, where, "line");
        for (var i = line.IndexOf('{'); i >= 0; i = line.IndexOf('{', i + 1))
        {
            var end = line.IndexOf('}', i);
            var name = end > i ? line[(i + 1)..end] : "";
            var bare = name.EndsWith(".first", StringComparison.Ordinal) ? name[..^6] : name;
            if (bare != "here" && vars.IndexOf(bare) is var k && (k < 0 || k >= castVars))
                throw new StoryException($"{where}: \"{line}\": {{{name}}} isn't ?me or one of the cast ({string.Join(", ", vars.Take(castVars).Select(r => "{" + r + "}"))}, or {{here}})");
        }
    }
}

/// <summary>A storylet under way for a player: who is cast, and where it is at.</summary>
public struct StoryletRun
{
    public int Def, Player, Serial;
    public Ints8 Binds; // per storylet variable (StoryletDef.Vars): me, the cast, the named things
    public StoryletPhase Phase;
    public double StartedAt, WaitFrom, PausedAt, Paused; // Paused: s spent paused (time ends count without it)
}

public enum StoryletPhase : byte
{
    Beats, // its play is under way
    Waiting, // for one of its ends
    Paused, // an urgent one came first: its events still count, its times stand still
}

/// <summary>What became of a storylet lately (the window): which end, or why it was given up.</summary>
public readonly record struct StoryletDone(int Def, int Player, int End, string? Dropped, double StartedAt, double EndedAt);

/// <summary>
/// The storylets (docs/story/STORY.md "Storylets"): per player, once a second, the next storylet that can play is
/// started: an urgent one first (pausing whatever was under way), else the A and B plots in turn, with a rest between.
/// A storylet casts its roles from the living people (free, where the player is), plays its beats, then waits for one
/// of its ends: an event, a time or the player's choice. What it changes is the story world's own relations and
/// qualities, so it saves with the world; which storylets are under way is kept as relations too (OnStorylet,
/// StoryletAt, Played), and one under way when the world was saved waits for its ends again after a load.
/// </summary>
public sealed class StoryStorylets
{
    public const double Rest = 120, DropRest = 60, Look = 1; // s
    public const int MaxRuns = 64, HistorySize = 32, MaxCandidates = 32;

    private readonly StoryEngine _e;
    public bool On = true;

    public readonly List<StoryletRun> Runs = new(MaxRuns);
    public readonly StoryletDone[] History = new StoryletDone[HistorySize];
    public int HistoryNext, HistoryCount;
    public long Started, Ended, Dropped;

    /// <summary>The game: start a hook ("lockdown"...) for a player, with the storylet's cast (entities).</summary>
    public Action<int, string, Ints8>? Hook;

    /// <summary>A line with nobody to say it (no cast), for the player alone: the game sends it as a notice.</summary>
    public Action<int, string>? Notice;

    /// <summary>A storylet began or ended, as words (the CLI).</summary>
    public Action<string>? Note;

    private readonly Dictionary<int, (StoryletBucket Last, double RestUntil)> _player = new();
    private readonly Dictionary<long, (int Value, double At)> _decay = new(); // (player, quality): the value seen, and since when it hasn't risen
    private readonly int[] _cand = new int[MaxCandidates];
    private readonly int[] _score = new int[MaxCandidates];
    private int[] _things = [];
    private int _serial;
    private double _lookAt;
    private bool _wired;
    private readonly StringBuilder _sb = new(160);

    public StoryStorylets(StoryEngine engine) => _e = engine;

    private StoryletRules R => _e.Rules.Storylets;
    private StoryWorld W => _e.World;
    private StoryAgents Ag => _e.Agents;

    public void Clear()
    {
        Runs.Clear();
        HistoryNext = HistoryCount = 0;
        Started = Ended = Dropped = 0;
        _player.Clear();
        _decay.Clear();
        _things = [];
        _lookAt = 0;
    }

    private void Wire()
    {
        if (_wired)
            return;
        _wired = true;
        _e.Plays.Ended += OnPlayEnded;
    }

    /// <summary>The storylet's Thing (what OnStorylet, StoryletAt and Played point at), made the first time.</summary>
    public int Thing(int def)
    {
        if (_things.Length != R.Storylets.Length)
            _things = Enumerable.Repeat(-1, R.Storylets.Length).ToArray();
        var t = _things[def];
        var d = R.Storylets[def];
        if (!W.Valid(t) || W[t].Kind != StoryKind.Thing || W[t].Name != d.Title)
            _things[def] = t = W.Get(StoryKind.Thing, StoryFactions.Key("storylet:" + d.Id), d.Title, _e.Time);
        return t;
    }

    /// <summary>How many times a player has had this storylet to its end.</summary>
    public int PlayedCount(int player, int def) => W.Get(player, R.Played, Thing(def));

    public int RunOf(int player, bool paused = false, bool side = false)
    {
        for (var i = 0; i < Runs.Count; i++)
            if (Runs[i].Player == player && (Runs[i].Phase == StoryletPhase.Paused) == paused && IsSide(Runs[i].Def) == side)
                return i;
        return -1;
    }

    /// <summary>A side road's storylet: its own lane (<see cref="StoryletBucket.Side"/>).</summary>
    private bool IsSide(int def) => R.Storylets[def].Bucket == StoryletBucket.Side;

    // ---- Each second --------------------------------------------------------------------------------------------------

    public void Update(double now)
    {
        if (R.Storylets.Length == 0 || now < _lookAt)
            return;
        _lookAt = now + Look;
        Wire();
        Decay(now);
        // Time ends.
        for (var i = 0; i < Runs.Count; i++)
        {
            var r = Runs[i];
            if (r.Phase != StoryletPhase.Waiting)
                continue;
            var d = R.Storylets[r.Def];
            foreach (var end in d.Ends)
                if (end.After > 0 && now - r.WaitFrom - r.Paused >= end.After && Holds(end, r))
                {
                    Finish(r.Serial, end.Index, now);
                    i = -1; // (the list changed: start again; each pass ends one at most)
                    break;
                }
        }
        if (!On)
            return;
        var all = W.All;
        for (var p = 0; p < all.Count; p++)
        {
            var e = all[p];
            if (e.Kind != StoryKind.Player || e.Gone)
                continue;
            Consider(p, now);
        }
    }

    /// <summary>One player: an urgent storylet that can play pauses theirs; with none under way, a paused one goes on, or the next starts.</summary>
    private void Consider(int player, double now)
    {
        // The side lane first, on its own: one side road's storylet at a time, whatever the main lane is doing.
        if (RunOf(player, side: true) < 0 && !Restore(player, now, side: true))
        {
            var side = Next(player, StoryletBucket.Side, now, out var sb);
            if (side >= 0)
                Start(side, sb, now);
        }
        var active = RunOf(player);
        if (active < 0 && Restore(player, now))
            active = RunOf(player);
        if (active >= 0 && R.Storylets[Runs[active].Def].Bucket == StoryletBucket.Urgent)
            return;
        var st = _player.GetValueOrDefault(player, (StoryletBucket.B, 0));
        if (active < 0 && now < st.RestUntil && RunOf(player, true) < 0)
            return;
        // Urgent first, then the bucket that didn't go last, then the other.
        var urgent = Next(player, StoryletBucket.Urgent, now, out var ub);
        if (urgent >= 0)
        {
            if (active >= 0)
            {
                if (RunOf(player, true) >= 0)
                    return; // (one paused at a time)
                var r = Runs[active];
                r.Phase = StoryletPhase.Paused;
                r.PausedAt = now;
                Runs[active] = r;
                W.Set(player, R.OnStorylet, Thing(r.Def), 2);
                Note?.Invoke($"{W.NameOf(player)}: {R.Storylets[r.Def].Title} waits for {R.Storylets[urgent].Title}");
            }
            Start(urgent, ub, now);
            return;
        }
        if (active >= 0)
            return;
        var paused = RunOf(player, true);
        if (paused >= 0)
        {
            var r = Runs[paused];
            r.Phase = StoryletPhase.Waiting;
            r.Paused += now - r.PausedAt;
            Runs[paused] = r;
            W.Set(player, R.OnStorylet, Thing(r.Def), 1);
            return;
        }
        var first = st.Last == StoryletBucket.A ? StoryletBucket.B : StoryletBucket.A;
        var next = Next(player, first, now, out var b1);
        if (next >= 0)
        {
            Start(next, b1, now);
            return;
        }
        next = Next(player, first == StoryletBucket.A ? StoryletBucket.B : StoryletBucket.A, now, out var b2);
        if (next >= 0)
            Start(next, b2, now);
    }

    /// <summary>The ready storylet of a bucket with the highest priority (the first written among equals), cast. -1: none.</summary>
    private int Next(int player, StoryletBucket bucket, double now, out Ints8 binds)
    {
        binds = Ints8.Empty();
        var best = -1;
        var bestPri = int.MinValue;
        foreach (var d in R.Storylets)
        {
            if (d.Bucket != bucket || d.Priority <= bestPri || !Available(d, player, now) || !NeedsHold(d, player))
                continue;
            if (!CastFor(d, player, out var b))
                continue;
            (best, bestPri, binds) = (d.Index, d.Priority, b);
        }
        return best;
    }

    /// <summary>Not under way for them, and not had (or had long enough ago, for one that comes "again").</summary>
    private bool Available(StoryletDef d, int player, double now)
    {
        var t = Thing(d.Index);
        if (W.Get(player, R.OnStorylet, t) > 0)
            return false;
        if (W.Get(player, R.Played, t) <= 0)
            return true;
        return d.Again > 0 && now - W.Get(player, R.StoryletAt, t) >= d.Again;
    }

    private bool NeedsHold(StoryletDef d, int player)
    {
        if (d.Needs.Length == 0)
            return true;
        Span<int> b = stackalloc int[StoryRules.MaxVars];
        foreach (var q in d.Needs)
        {
            b.Fill(-1);
            b[0] = player;
            if (q.Any(W, b))
                return true;
        }
        return false;
    }

    /// <summary>The player's place (In), -1 none.</summary>
    public int PlaceOf(int player)
    {
        if (R.In < 0)
            return -1;
        var (a, b) = W.Range(player, R.In);
        return b > a ? W[player].Pairs[a].Target : -1;
    }

    /// <summary>
    /// The cast: the lead (the first role: a free person where the player is), the strongest first when it "prefer"s,
    /// then the rest of the query; everyone else cast who is a living person must be free and there too.
    /// </summary>
    private bool CastFor(StoryletDef d, int player, out Ints8 binds)
    {
        binds = Ints8.Empty();
        binds[0] = player;
        if (d.Cast == null)
            return true;
        var place = PlaceOf(player);
        var n = d.Cast.Collect(W, binds, 1, _cand);
        if (n == 0)
            return false;
        if (d.PreferVar == 1)
        {
            for (var i = 0; i < n; i++)
                _score[i] = d.PreferQuality ? W.Quality(_cand[i], d.PreferRel) : W.Get(_cand[i], d.PreferRel, d.PreferObj == 0 ? player : -1);
            for (var i = 1; i < n; i++) // (insertion sort: a handful)
                for (var j = i; j > 0 && _score[j] > _score[j - 1]; j--)
                    ((_cand[j], _cand[j - 1]), (_score[j], _score[j - 1])) = ((_cand[j - 1], _cand[j]), (_score[j - 1], _score[j]));
        }
        for (var k = 0; k < n; k++)
        {
            var lead = _cand[k];
            if (Ag.Of(lead) is not { Dead: false } || !_e.Plays.Free(lead) || Busy(lead) || place >= 0 && Ag.Where(lead) != place)
                continue;
            var b = Ints8.Empty();
            b[0] = player;
            b[1] = lead;
            if (!d.Cast.Any(W, b))
                continue;
            var ok = true;
            for (var v = 2; v < d.CastVars && ok; v++)
                if (b[v] >= 0 && Ag.Of(b[v]) is { Dead: false })
                    ok = _e.Plays.Free(b[v]) && !Busy(b[v]) && Ag.Where(b[v]) == Ag.Where(lead);
            if (!ok)
                continue;
            binds = b;
            return true;
        }
        return false;
    }

    /// <summary>Someone already cast in another player's storylet that is under way.</summary>
    private bool Busy(int person)
    {
        foreach (var r in Runs)
            for (var v = 1; v < R.Storylets[r.Def].CastVars; v++)
                if (r.Binds[v] == person && r.Phase != StoryletPhase.Paused)
                    return true;
        return false;
    }

    private void Start(int def, Ints8 binds, double now)
    {
        var d = R.Storylets[def];
        var player = binds[0];
        for (var i = 0; i < d.Named.Length; i++)
            binds[d.CastVars + i] = NamedThing(d.Named[i]);
        if (d.Play >= 0)
        {
            var cast = Ints8.Empty();
            for (var k = 0; k < d.PlayRole.Length; k++)
                cast[k] = binds[d.PlayRole[k]];
            if (!_e.Plays.BeginExternal(d.Play, cast, now))
                return; // (no room for another play just now: tried again next look)
        }
        var t = Thing(def);
        W.Set(player, R.OnStorylet, t, 1);
        W.Set(player, R.StoryletAt, t, (int)now);
        var run = new StoryletRun { Def = def, Player = player, Serial = ++_serial, Binds = binds, StartedAt = now, WaitFrom = now, Phase = d.Play >= 0 ? StoryletPhase.Beats : StoryletPhase.Waiting };
        Runs.Add(run);
        Started++;
        if (d.Bucket != StoryletBucket.Side) // (the side lane neither takes a turn from A and B nor rests them)
            _player[player] = (d.Bucket == StoryletBucket.Urgent ? _player.GetValueOrDefault(player).Last : d.Bucket, 0);
        Note?.Invoke($"{W.NameOf(player)}: the storylet {d.Title} begins{(d.CastVars > 1 ? $" ({CastText(d, binds)})" : "")}");
        if (d.Job >= 0)
        {
            var why = _e.Factions.TakeJob(player, d.JobFaction, d.Job);
            if (why != null)
                Note?.Invoke($"{W.NameOf(player)}: {d.JobText} wasn't given: {why}");
        }
        if (d.Hook.Length > 0)
            Hook?.Invoke(player, d.Hook, binds);
        if (run.Phase == StoryletPhase.Waiting && d.Ends.Length == 0)
            Finish(run.Serial, -1, now);
    }

    /// <summary>A name in an effect: whoever has it, else a Thing of that name (a fact, a lead).</summary>
    private int NamedThing(string name)
    {
        var id = W.Find(name);
        return id >= 0 ? id : W.Get(StoryKind.Thing, StoryFactions.Key("fact:" + name), name, _e.Time);
    }

    /// <summary>A world that was saved with a storylet under way for this player: it waits for its ends again, cast afresh.</summary>
    private bool Restore(int player, double now, bool side = false)
    {
        if (W[player].PairCount == 0)
            return false;
        var (a, b) = W.Range(player, R.OnStorylet);
        for (var i = a; i < b; i++)
        {
            var pair = W[player].Pairs[i];
            if (pair.Value <= 0)
                continue;
            var def = -1;
            for (var k = 0; k < R.Storylets.Length && def < 0; k++)
                if (Thing(k) == pair.Target)
                    def = k;
            if (def >= 0 && IsSide(def) != side)
                continue; // (the other lane's: restored with it)
            if (def < 0 || RunOf(player, side: side) >= 0 || RunOf(player, true, side) >= 0)
            {
                W.Set(player, R.OnStorylet, pair.Target, 0); // (a storylet this build no longer has, or one too many)
                return Restore(player, now, side);
            }
            var d = R.Storylets[def];
            if (!CastFor(d, player, out var binds))
            {
                // Nobody to play it now (the cast is busy, or dead): it is dropped, and may come again.
                W.Set(player, R.OnStorylet, pair.Target, 0);
                Note?.Invoke($"{W.NameOf(player)}: the storylet {d.Title} was under way, and can't be cast again: dropped");
                return Restore(player, now, side);
            }
            for (var n = 0; n < d.Named.Length; n++)
                binds[d.CastVars + n] = NamedThing(d.Named[n]);
            var paused = pair.Value == 2 && !side; // (a side storylet is never paused: one saved paused, from before the lane, just waits)
            if (side && pair.Value == 2)
                W.Set(player, R.OnStorylet, pair.Target, 1);
            Runs.Add(new StoryletRun
            {
                Def = def, Player = player, Serial = ++_serial, Binds = binds, StartedAt = W.Get(player, R.StoryletAt, pair.Target), WaitFrom = now,
                Phase = paused ? StoryletPhase.Paused : StoryletPhase.Waiting, PausedAt = now,
            });
            return true;
        }
        return false;
    }

    private void OnPlayEnded(int play, Ints8 cast, string? broken)
    {
        for (var i = 0; i < Runs.Count; i++)
        {
            var r = Runs[i];
            var d = R.Storylets[r.Def];
            if (r.Phase != StoryletPhase.Beats || d.Play != play || r.Binds[d.PlayRole[0]] != cast[0])
                continue;
            if (broken != null)
            {
                Drop(r.Serial, $"its beats were broken off: {broken}", _e.Time);
                return;
            }
            r.Phase = StoryletPhase.Waiting;
            r.WaitFrom = _e.Time;
            Runs[i] = r;
            if (d.Ends.Length == 0)
                Finish(r.Serial, -1, _e.Time);
            return;
        }
    }

    // ---- Ends ---------------------------------------------------------------------------------------------------------

    /// <summary>An event: the ends that wait for it (of storylets waiting or paused), the first that holds.</summary>
    public void OnEvent(in StoryEvent ev)
    {
        if (Runs.Count == 0)
            return;
        Span<int> roles = [ev.Actor, ev.Target, ev.Owner, ev.Place, ev.Item];
        for (var i = 0; i < Runs.Count; i++)
        {
            var r = Runs[i];
            if (r.Phase == StoryletPhase.Beats)
                continue;
            foreach (var end in R.Storylets[r.Def].Ends)
            {
                if (end.When is not { } w || w.Verb != ev.Verb || end.Within > 0 && ev.At - r.WaitFrom > end.Within)
                    continue;
                var match = true;
                for (var k = 0; k < 5 && match; k++)
                    if (w.Roles[k] >= 0)
                        match = r.Binds[w.Roles[k]] == roles[k];
                    else if (w.Names[k] is { } name)
                        match = roles[k] >= 0 && W.NameOf(roles[k]).Equals(name, StringComparison.OrdinalIgnoreCase);
                if (match && Holds(end, r))
                {
                    Finish(r.Serial, end.Index, ev.At);
                    return; // (one end per event: what it records may end others in turn)
                }
            }
        }
    }

    private bool Holds(StoryletEnd end, in StoryletRun r)
    {
        if (end.If == null)
            return true;
        Span<int> b = stackalloc int[StoryRules.MaxVars];
        ((ReadOnlySpan<int>)r.Binds).CopyTo(b);
        return end.If.Any(W, b);
    }

    /// <summary>The choices a player has now from this person (the talk panel): (storylet run serial, end index, text).</summary>
    public int ChoicesFor(int player, int person, Span<(int Serial, int End)> into)
    {
        var n = 0;
        foreach (var r in Runs)
        {
            if (r.Player != player || r.Phase != StoryletPhase.Waiting)
                continue;
            foreach (var end in R.Storylets[r.Def].Ends)
                if (end.Choice.Length > 0 && end.By >= 0 && r.Binds[end.By] == person && n < into.Length && Holds(end, r))
                    into[n++] = (r.Serial, end.Index);
        }
        return n;
    }

    public string ChoiceText(int serial, int end) => Find(serial) is var i and >= 0 && (uint)end < (uint)R.Storylets[Runs[i].Def].Ends.Length ? R.Storylets[Runs[i].Def].Ends[end].Choice : "";

    /// <summary>A player chose an answer (the talk panel): the end happens, recorded as a Choose. False: it can't now.</summary>
    public bool Choose(int player, int serial, int end, double now)
    {
        var i = Find(serial);
        if (i < 0 || Runs[i].Player != player || Runs[i].Phase != StoryletPhase.Waiting)
            return false;
        var r = Runs[i];
        var d = R.Storylets[r.Def];
        if ((uint)end >= (uint)d.Ends.Length || d.Ends[end].Choice.Length == 0 || !Holds(d.Ends[end], r))
            return false;
        Finish(serial, end, now, chose: true);
        return true;
    }

    private int Find(int serial)
    {
        for (var i = 0; i < Runs.Count; i++)
            if (Runs[i].Serial == serial)
                return i;
        return -1;
    }

    /// <summary>The host's button (the window): this end now, as if it happened. False: not under way.</summary>
    public bool Force(int player, int def, int end, double now)
    {
        for (var i = 0; i < Runs.Count; i++)
            if (Runs[i].Player == player && Runs[i].Def == def && (end < 0 || (uint)end < (uint)R.Storylets[def].Ends.Length))
            {
                if (Runs[i].Phase == StoryletPhase.Beats)
                {
                    var r = Runs[i];
                    r.Phase = StoryletPhase.Waiting;
                    Runs[i] = r;
                }
                Finish(Runs[i].Serial, end, now);
                return true;
            }
        return false;
    }

    /// <summary>The host's button: starts a storylet for a player now if it can be cast (its needs aside). Why not, or null.</summary>
    public string? StartNow(int player, int def, double now)
    {
        Wire();
        if ((uint)def >= (uint)R.Storylets.Length || !W.Valid(player))
            return "no such storylet";
        var d = R.Storylets[def];
        if (RunOf(player, side: IsSide(def)) >= 0)
            return "another is under way for them";
        if (!CastFor(d, player, out var b))
            return "nobody can be cast";
        var was = Started;
        Start(def, b, now);
        return Started > was ? null : "no room for its beats just now";
    }

    /// <summary>The host's button: forgets what a player has had, so every storylet can come again.</summary>
    public void Reset(int player)
    {
        for (var i = Runs.Count - 1; i >= 0; i--)
            if (Runs[i].Player == player)
                Runs.RemoveAt(i);
        for (var k = 0; k < R.Storylets.Length; k++)
        {
            var t = Thing(k);
            W.Set(player, R.OnStorylet, t, 0);
            W.Set(player, R.Played, t, 0);
            W.Set(player, R.StoryletAt, t, 0);
            if (R.Ended >= 0)
                W.Set(player, R.Ended, t, 0);
            if (R.EndedWith >= 0)
                W.Set(player, R.EndedWith, t, 0);
        }
        _player.Remove(player);
    }

    private void Drop(int serial, string why, double now)
    {
        var i = Find(serial);
        if (i < 0)
            return;
        var r = Runs[i];
        Runs.RemoveAt(i);
        W.Set(r.Player, R.OnStorylet, Thing(r.Def), 0);
        if (!IsSide(r.Def)) // (the side lane never rests the A and B plots)
            _player[r.Player] = (_player.GetValueOrDefault(r.Player).Last, now + DropRest);
        Dropped++;
        Keep(new StoryletDone(r.Def, r.Player, -1, why, r.StartedAt, now));
        Note?.Invoke($"{W.NameOf(r.Player)}: the storylet {R.Storylets[r.Def].Title} was given up: {why}");
    }

    /// <summary>A storylet ends (end -1: it has none): what it changes, for the player or their whole group; its lines; its hook.</summary>
    private void Finish(int serial, int endIndex, double now, bool chose = false)
    {
        var i = Find(serial);
        if (i < 0)
            return;
        var r = Runs[i];
        Runs.RemoveAt(i); // (first: what it records below may look at the runs)
        var d = R.Storylets[r.Def];
        var end = endIndex >= 0 ? d.Ends[endIndex] : null;
        var t = Thing(r.Def);
        Span<int> group = stackalloc int[16];
        var members = Members(r.Player, d.Group, group);
        for (var m = 0; m < members; m++)
        {
            var b = r.Binds;
            b[0] = group[m];
            if (end != null)
            {
                W.Apply(end.Then, b);
                if (end.Learn >= 0 && R.Lead >= 0 && b[end.Learn] >= 0)
                {
                    W.Add(group[m], R.Lead, b[end.Learn], 1);
                    if (R.VerbLearn >= 0)
                        _e.Record(R.VerbLearn, group[m], end.LearnFrom >= 0 ? b[end.LearnFrom] : -1, -1, PlaceOf(group[m]), b[end.Learn]);
                }
            }
            Clamp(group[m]);
            W.Set(group[m], R.OnStorylet, t, 0);
            W.Add(group[m], R.Played, t, 1);
            W.Set(group[m], R.StoryletAt, t, (int)now);
            if (end != null && R.Ended >= 0)
                W.Set(group[m], R.Ended, t, W.Get(group[m], R.Ended, t) | 1 << end.Index);
            if (R.EndedWith >= 0)
                W.Set(group[m], R.EndedWith, t, d.CastVars > 1 && r.Binds[1] >= 0 ? r.Binds[1] + 1 : 0);
        }
        if (end != null && chose && R.VerbChoose >= 0)
            _e.Record(R.VerbChoose, r.Player, end.By >= 0 ? r.Binds[end.By] : -1, -1, PlaceOf(r.Player), t, end.Index + 1);
        _e.Record(R.VerbStorylet, r.Player, end?.By >= 0 ? r.Binds[end.By] : -1, -1, PlaceOf(r.Player), t, (end?.Index ?? -1) + 1);
        if (end is { Say.Length: > 0 })
        {
            var line = Fill(end.Say[(int)(now * 7.31) % end.Say.Length], d, r.Binds);
            var by = end.By >= 0 ? r.Binds[end.By] : -1;
            if (by < 0 || !_e.Talk.SayFor(by, r.Player, line, now, d.Play, -1))
                Notice?.Invoke(r.Player, line);
        }
        if (end is { Hook.Length: > 0 })
            Hook?.Invoke(r.Player, end.Hook, r.Binds);
        if (d.Bucket != StoryletBucket.Side && (d.Bucket != StoryletBucket.Urgent || RunOf(r.Player, true) < 0))
            _player[r.Player] = (_player.GetValueOrDefault(r.Player).Last, now + Rest);
        Ended++;
        Keep(new StoryletDone(r.Def, r.Player, endIndex, null, r.StartedAt, now));
        Note?.Invoke($"{W.NameOf(r.Player)}: the storylet {d.Title} ended{(end != null ? $": {end.Id}" : "")}");
    }

    /// <summary>The player, or (a group storylet) every player in their group: into <paramref name="into"/>, how many.</summary>
    private int Members(int player, bool group, Span<int> into)
    {
        into[0] = player;
        if (!group)
            return 1;
        var party = _e.Factions.Party(player);
        if (party == player)
            return 1;
        var n = 1;
        var all = W.All;
        for (var p = 0; p < all.Count && n < into.Length; p++)
            if (p != player && all[p].Kind == StoryKind.Player && !all[p].Gone && _e.Factions.Party(p) == party)
                into[n++] = p;
        return n;
    }

    private void Keep(in StoryletDone done)
    {
        History[HistoryNext] = done;
        HistoryNext = (HistoryNext + 1) % HistorySize;
        HistoryCount = Math.Min(HistoryCount + 1, HistorySize);
    }

    // ---- Qualities ----------------------------------------------------------------------------------------------------

    /// <summary>The storylet qualities kept in their range.</summary>
    private void Clamp(int player)
    {
        foreach (var q in R.Qualities)
        {
            var v = W.Quality(player, q.Id);
            var c = Math.Clamp(v, q.Min, q.Max);
            if (c != v)
                W.SetQuality(player, q.Id, c);
        }
    }

    /// <summary>A menace cools off: a quality with "decay" drops by its amount each interval it didn't rise.</summary>
    private void Decay(double now)
    {
        foreach (var q in R.Qualities)
        {
            if (q.Decay <= 0)
                continue;
            var all = W.All;
            for (var p = 0; p < all.Count; p++)
            {
                if (all[p].Kind != StoryKind.Player)
                    continue;
                var v = W.Quality(p, q.Id);
                var key = (long)p << 16 | q.Id;
                if (!_decay.TryGetValue(key, out var seen) || v > seen.Value || v <= q.Min)
                    _decay[key] = (v, now); // (new, risen, or nothing to cool: the clock starts again)
                else if (now - seen.At >= q.DecayEvery)
                {
                    var nv = Math.Max(q.Min, v - q.Decay);
                    W.SetQuality(p, q.Id, nv);
                    _decay[key] = (nv, now);
                }
                else if (v < seen.Value)
                    _decay[key] = (v, seen.At);
            }
        }
    }

    // ---- Words --------------------------------------------------------------------------------------------------------

    /// <summary>A storylet's line with {me}, the cast ({g}, {g.first}) and {here} filled in.</summary>
    public string Fill(string line, StoryletDef d, in Ints8 binds)
    {
        var sb = _sb.Clear();
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            var end = c == '{' ? line.IndexOf('}', i) : -1;
            if (end < 0)
            {
                sb.Append(c);
                continue;
            }
            var name = line.AsSpan(i + 1, end - i - 1);
            var first = name.EndsWith(".first");
            if (first)
                name = name[..^6];
            var who = -1;
            if (name.SequenceEqual("here"))
                who = PlaceOf(binds[0]);
            else
                for (var k = 0; k < d.CastVars; k++)
                    if (name.SequenceEqual(d.Vars[k]))
                        who = binds[k];
            var text = W.NameOf(who);
            sb.Append(first && text.IndexOf(' ') is var sp and > 0 ? text[..sp] : text);
            i = end;
        }
        return sb.Length > StoryTalk.MaxText ? sb.ToString(0, StoryTalk.MaxText - 3) + "..." : sb.ToString();
    }

    public string CastText(StoryletDef d, in Ints8 binds)
    {
        var parts = new List<string>(d.CastVars);
        for (var k = 1; k < d.CastVars; k++)
            if (binds[k] >= 0)
                parts.Add($"{d.Vars[k]} = {W.NameOf(binds[k])}");
        return string.Join(", ", parts);
    }

    /// <summary>
    /// Why a storylet isn't playing for a player now (the Story tab, --check-story): under way, had, resting, the
    /// first clause of its needs that fails (of the alternative that got furthest), or who can't be cast. Null: ready.
    /// </summary>
    public string? WhyNot(int player, int def, double now)
    {
        var d = R.Storylets[def];
        var t = Thing(def);
        var on = W.Get(player, R.OnStorylet, t);
        if (on == 1)
            return "under way";
        if (on == 2)
            return "paused for an urgent one";
        var played = W.Get(player, R.Played, t);
        if (played > 0 && d.Again <= 0)
            return played == 1 ? "had" : $"had {played} times";
        if (played > 0 && now - W.Get(player, R.StoryletAt, t) < d.Again)
            return $"had; again in {StoryRules.Span(d.Again - (now - W.Get(player, R.StoryletAt, t)))}";
        if (d.Needs.Length > 0 && !NeedsHold(d, player))
        {
            string? why = null;
            var furthest = -1;
            Span<int> b = stackalloc int[StoryRules.MaxVars];
            foreach (var q in d.Needs)
            {
                b.Fill(-1);
                b[0] = player;
                var k = q.FirstFailing(W, b);
                if (k > furthest)
                    (furthest, why) = (k, $"needs {q.ClauseTexts[k]}");
            }
            return why ?? "needs";
        }
        if (d.Cast != null)
        {
            if (!CastFor(d, player, out _))
            {
                Span<int> b = stackalloc int[StoryRules.MaxVars];
                b.Fill(-1);
                b[0] = player;
                var k = d.Cast.FirstFailing(W, b);
                return k >= 0 ? $"nobody to cast: {d.Cast.ClauseTexts[k]}" : PlaceOf(player) < 0 ? "they are in no place" : "the cast is busy or elsewhere";
            }
        }
        return null;
    }
}
