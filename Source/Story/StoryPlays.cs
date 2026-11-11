using System.Text;
using System.Text.Json;

namespace CallOfSlop.Story;

/// <summary>One beat of a play: who says or does what to whom, if what holds, how long after the beat before.</summary>
public sealed class PlayBeat
{
    public int By, To = -1; // roles (the play's variables), -1 none
    public string[] Lines = []; // said (one at random), filled with the roles' names
    public int Action = -1; // a people's action done (StoryPeopleRules), -1 none
    public StoryQuery? If;
    public double After = PlayRules.DefaultAfter; // s after the beat before (the first: after the cast is together)
    public float Chance = 1;
    public bool Must; // the play is broken off when its action can't be done (else the beat is skipped)
    public StoryEffect[] Then = []; // what it changes when it happens (the cast's roles as slots)
    public string Text = ""; // as written (the window)
}

/// <summary>
/// A play (docs/story/STORY.md "Plays"): a short scene whose roles are cast from the world by a query,
/// acted out in beats of lines and actions. Its first role leads (a person); the play happens where they are.
/// </summary>
public sealed class PlayDef
{
    public string Name = "", About = "", File = "";
    public int Id;
    public string[] Roles = []; // the variables, cast's first, then extra's
    public int CastVars; // how many of them the cast query binds (the rest are extras: may be nobody)
    public StoryQuery Cast = null!;
    public StoryQuery? Extra;
    public PlayBeat[] Beats = [];
    public double Every = PlayRules.DefaultEvery; // s between two of it, anywhere
    public bool Once; // once per cast (this session)
    public int OnceRoles; // the roles "once" counts (a bit each): all of them, or those it names
    public float Weight = 1, Chance = 1;
    public bool External; // a storylet's beats (StoryStorylets): never drawn by the director, started by its storylet
}

/// <summary>The plays from Story/Plays/*.json. A bad file is refused whole with a reason (in <see cref="StoryRules.Errors"/>).</summary>
public sealed class PlayRules
{
    public const int MaxPlays = 200, MaxBeats = 16;
    public const double DefaultAfter = 4, DefaultEvery = 3600;

    public PlayDef[] Plays = [];

    public PlayDef? Play(string name) => Plays.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    private static readonly string[] FileKeys = ["about", "plays"];
    private static readonly string[] PlayKeys = ["name", "about", "cast", "extra", "beats", "every", "once", "weight", "chance"];
    private static readonly string[] BeatKeys = ["by", "to", "say", "do", "if", "after", "chance", "must", "then"];

    public static PlayRules Load(StoryRules rules, string folder)
    {
        var pr = new PlayRules();
        var all = new List<PlayDef>();
        foreach (var f in StoryRules.Files(Path.Combine(folder, "Plays")))
        {
            var name = $"Plays/{Path.GetFileName(f)}";
            try
            {
                using var doc = StoryRules.Parse(StoryRules.ReadFile(f), name);
                all.AddRange(Read(rules, doc.RootElement, name, all));
            }
            catch (StoryException ex)
            {
                rules.Errors.Add(ex.Message);
            }
        }
        pr.Plays = all.ToArray();
        return pr;
    }

    private static List<PlayDef> Read(StoryRules rules, JsonElement root, string file, List<PlayDef> before)
    {
        StoryRules.Keys(root, FileKeys, file);
        var list = new List<PlayDef>();
        var people = rules.People;
        foreach (var e in StoryRules.List(root, "plays", file, true))
        {
            var where = $"{file}: play {list.Count + 1}";
            if (e.ValueKind != JsonValueKind.Object)
                throw new StoryException($"{where}: a play is like {{\"name\": \"...\", \"cast\": \"?a In ?p, ?b In ?p\", \"beats\": [...]}}");
            StoryRules.Keys(e, PlayKeys, where);
            var p = new PlayDef { Name = StoryRules.Str(e, "name", where, true), About = StoryRules.Str(e, "about", where), File = file, Id = before.Count + list.Count };
            where = $"{file}: {p.Name}";
            if (before.Concat(list).Any(o => o.Name.Equals(p.Name, StringComparison.OrdinalIgnoreCase)))
                throw new StoryException($"{where}: a play of that name already exists");
            var vars = new List<string>();
            var cast = StoryRules.Str(e, "cast", where, true);
            p.Cast = Compile(cast, rules, vars, where, "cast");
            if (vars.Count == 0)
                throw new StoryException($"{where}: \"cast\" names no roles (its first variable is the person who leads)");
            p.CastVars = vars.Count;
            var extra = StoryRules.Str(e, "extra", where);
            if (extra.Length > 0)
                p.Extra = Compile(extra, rules, vars, where, "extra");
            p.Roles = vars.ToArray();
            if (e.TryGetProperty("every", out _))
                p.Every = StoryRules.Duration(StoryRules.Str(e, "every", where)) is { } d and >= 60 and <= 7 * 86400 ? d : throw new StoryException($"{where}: \"every\" is a time from 1m to 7d, like \"2h\"");
            if (e.TryGetProperty("once", out var once) && once.ValueKind == JsonValueKind.String)
            {
                // "once": "?a ?d": once per those roles (whoever else is cast)
                foreach (var part in (once.GetString() ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    p.OnceRoles |= 1 << Role(part, vars, where, "once");
                p.Once = p.OnceRoles != 0;
            }
            else if (p.Once = StoryRules.Bool(e, "once", where))
                p.OnceRoles = (1 << vars.Count) - 1;
            p.Weight = Number(e, "weight", where, 1, 0.01f, 100);
            p.Chance = Number(e, "chance", where, 1, 0.01f, 1);
            var beats = ReadBeats(rules, e, where, vars, p.Roles, true);
            if (beats.Count == 0)
                throw new StoryException($"{where}: \"beats\" is empty");
            p.Beats = beats.ToArray();
            list.Add(p);
            if (before.Count + list.Count > MaxPlays)
                throw new StoryException($"{file}: too many plays ({MaxPlays} at most in all)");
        }
        return list;
    }

    /// <summary>A play's (or a storylet's) "beats", over the roles <paramref name="vars"/> (<paramref name="roles"/> as they are named).</summary>
    internal static List<PlayBeat> ReadBeats(StoryRules rules, JsonElement e, string where, List<string> vars, string[] roles, bool required)
    {
        var people = rules.People;
        var beats = new List<PlayBeat>();
        foreach (var b in StoryRules.List(e, "beats", where, required))
        {
            var at = $"{where}: beat {beats.Count + 1}";
            if (b.ValueKind != JsonValueKind.Object)
                throw new StoryException($"{at}: a beat is like {{\"by\": \"?a\", \"to\": \"?b\", \"say\": [\"...\"]}}");
            StoryRules.Keys(b, BeatKeys, at);
            var beat = new PlayBeat { By = Role(StoryRules.Str(b, "by", at, true), vars, at, "by") };
            var to = StoryRules.Str(b, "to", at);
            if (to.Length > 0)
                beat.To = Role(to, vars, at, "to");
            if (beat.To == beat.By)
                throw new StoryException($"{at}: \"to\" is the one saying it");
            beat.Lines = StoryRules.List(b, "say", at).Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() ?? "" : throw new StoryException($"{at}: each of \"say\" is a line")).ToArray();
            if (beat.Lines.Length > LineRules.MaxLines)
                throw new StoryException($"{at}: \"say\" has {LineRules.MaxLines} lines at most");
            foreach (var line in beat.Lines)
                Check(line, roles, at);
            var verb = StoryRules.Str(b, "do", at);
            if (verb.Length > 0)
            {
                var v = rules.VerbId(verb);
                if (v < 0)
                    throw new StoryException($"{at}: no verb {verb}");
                beat.Action = v < people.ActionOfVerb.Length ? people.ActionOfVerb[v] : -1;
                if (beat.Action < 0)
                    throw new StoryException($"{at}: {verb} isn't something people do (Story/People actions; nothing physical is ever a play's)");
                var act = people.Actions[beat.Action];
                if (act.Road || act.News)
                    throw new StoryException($"{at}: {verb} can't be a play's (trips and news are the people's own steps)");
            }
            int Var(string s) => s.StartsWith('?') ? vars.IndexOf(s[1..]) : -1;
            beat.Then = StoryRules.List(b, "then", at).Select(x => x.ValueKind == JsonValueKind.String
                ? rules.ParseEffect(x.GetString() ?? "", at, Var)
                : throw new StoryException($"{at}: each \"then\" is text, like \"?a Knows ?b +1\"")).ToArray();
            if (beat.Lines.Length == 0 && beat.Action < 0 && beat.Then.Length == 0)
                throw new StoryException($"{at}: a beat says something (\"say\"), does something (\"do\") or changes something (\"then\")");
            var cond = StoryRules.Str(b, "if", at);
            if (cond.Length > 0)
                beat.If = Compile(cond, rules, [.. vars], at, "if");
            if (b.TryGetProperty("after", out _))
                beat.After = StoryRules.Duration(StoryRules.Str(b, "after", at)) is { } a and <= 600 ? a : throw new StoryException($"{at}: \"after\" is a time up to 10m, like \"5s\"");
            beat.Chance = Number(b, "chance", at, 1, 0.01f, 1);
            beat.Must = StoryRules.Bool(b, "must", at);
            beat.Text = $"{roles[beat.By]}{(beat.To >= 0 ? $" to {roles[beat.To]}" : "")}: {(beat.Action >= 0 ? $"[{verb}] " : "")}{(beat.Lines.Length > 0 ? $"\"{beat.Lines[0]}\"{(beat.Lines.Length > 1 ? $" (+{beat.Lines.Length - 1})" : "")}" : "")}{(cond.Length > 0 ? $" if {cond}" : "")}";
            beats.Add(beat);
            if (beats.Count > MaxBeats)
                throw new StoryException($"{where}: {MaxBeats} beats at most");
        }
        return beats;
    }

    private static StoryQuery Compile(string text, StoryRules rules, List<string> vars, string where, string key)
    {
        try
        {
            return StoryQuery.Compile(text, rules, vars);
        }
        catch (StoryException ex)
        {
            throw new StoryException($"{where}: \"{key}\": \"{text}\": {ex.Message}");
        }
    }

    private static int Role(string s, List<string> vars, string where, string key)
    {
        var name = s.StartsWith('?') ? s[1..] : s;
        var i = vars.IndexOf(name);
        return i >= 0 ? i : throw new StoryException($"{where}: \"{key}\": {s} isn't one of the play's roles ({string.Join(", ", vars.Select(v => "?" + v))})");
    }

    private static float Number(JsonElement e, string key, string where, float otherwise, float min, float max)
    {
        if (!e.TryGetProperty(key, out var v))
            return otherwise;
        return v.ValueKind == JsonValueKind.Number && v.TryGetSingle(out var f) && f >= min && f <= max ? f : throw new StoryException($"{where}: \"{key}\" is a number from {min} to {max}");
    }

    /// <summary>A play's line: printable ASCII, short enough for the chat, and only its roles' names and {here}.</summary>
    private static void Check(string line, string[] roles, string where)
    {
        if (line.Length == 0 || line.Any(c => c < ' ' || c > '~'))
            throw new StoryException($"{where}: \"{line}\": a line is printable ASCII");
        if (line.Length > LineRules.MaxLength)
            throw new StoryException($"{where}: \"{line}\": {LineRules.MaxLength} characters at most");
        for (var i = line.IndexOf('{'); i >= 0; i = line.IndexOf('{', i + 1))
        {
            var end = line.IndexOf('}', i);
            var name = end > i ? line[(i + 1)..end] : "";
            var bare = name.EndsWith(".first", StringComparison.Ordinal) ? name[..^6] : name;
            if (bare != "here" && Array.IndexOf(roles, bare) < 0)
                throw new StoryException($"{where}: \"{line}\": {{{name}}} isn't one of the play's roles ({string.Join(", ", roles.Select(r => "{" + r + "}"))}, or {{here}}; add .first for a first name)");
        }
    }
}

/// <summary>A play under way: its cast, where, the beat it is at and when that is due.</summary>
public struct PlayRun
{
    public int Play;
    public Ints8 Cast;
    public int Place;
    public int Beat;
    public double StartedAt, NextAt;
    public double WaitSince; // the beat's two haven't met yet (the game's bodies): since when (-1 not waiting)
    public int Said;
    public int Present; // the roles who are people there (a bit each): living, and where it happens (the rest are what it is about)
}

/// <summary>A play that ended: done, or broken off (and why).</summary>
public struct PlayDone
{
    public int Play;
    public Ints8 Cast;
    public int Place, Beats, Said;
    public double StartedAt, EndedAt;
    public string? Broken;
}

/// <summary>
/// The plays (docs/story/STORY.md "Plays"): every <see cref="LookEvery"/> s of the story's clock the director
/// looks for a play it can cast (the people free, together in one place, its query holding) and starts one; a play under
/// way says its beats in turn, holding its people (they finish the step they were on but start no other) until it is
/// over. With bodies (<see cref="StoryAgents.Meet"/>) a beat between two waits until they are together, as a step does.
/// What a play does is the people's own actions (the same verbs, recorded the same way); what it says changes nothing.
/// Not saved: a loaded story has none under way.
/// </summary>
public sealed class StoryPlays
{
    public const int MaxRunning = 4, HistorySize = 24, MaxLeads = 16;
    public const double LookEvery = 15, MeetWait = 40, PlaceGap = 120; // s; a place has no new play this soon after one ended there, or something big happened
    public const double HoldCheck = 2; // s: someone held by a play thinks again this often
    public const int BigNews = 50; // an event this interesting as news keeps plays from its place for PlaceGap

    private readonly StoryEngine _e;

    /// <summary>The host's switch (the Story window): off, no play starts (one under way finishes).</summary>
    public bool On = true;

    public readonly PlayRun[] Running = new PlayRun[MaxRunning];
    public int RunningCount;
    public readonly PlayDone[] History = new PlayDone[HistorySize];
    public int HistoryNext, HistoryCount;
    public long Started, Finished, BrokenOff, Looks;

    /// <summary>A play ended: its id, its cast, and why it was broken off (null: played out). Storylets watch their own beats here.</summary>
    public Action<int, Ints8, string?>? Ended;

    /// <summary>
    /// Starts a storylet's beats (<see cref="PlayDef.External"/>) with the cast its storylet chose: the lead (role 0) is a
    /// living person, and the play happens where they are. False: no room (<see cref="MaxRunning"/>) or the lead can't.
    /// </summary>
    public bool BeginExternal(int play, in Ints8 cast, double now)
    {
        if ((uint)play >= (uint)R.Plays.Length || RunningCount >= MaxRunning || Ag.Of(cast[0]) is not { Dead: false } || Ag.Where(cast[0]) < 0)
            return false;
        var was = Started;
        Begin(R.Plays[play], cast, now);
        return Started > was;
    }

    /// <summary>A play began or ended, as words (the CLI's --lines).</summary>
    public Action<string>? Note;

    private double _lookAt;
    private double[] _lastAt = []; // per play: when it last started
    private readonly HashSet<ulong> _once = new();
    private readonly Dictionary<int, double> _placeAt = new(); // place -> when its last play ended
    private readonly int[] _leads = new int[MaxLeads];
    private readonly StringBuilder _sb = new(160);
    private ulong _seed = 0x6A09E667F3BCC909UL;

    public StoryPlays(StoryEngine engine) => _e = engine;

    private PlayRules R => _e.Rules.Plays;
    private StoryAgents Ag => _e.Agents;

    public void Clear()
    {
        RunningCount = 0;
        HistoryNext = HistoryCount = 0;
        Started = Finished = BrokenOff = Looks = 0;
        _lookAt = 0;
        _lastAt = [];
        _once.Clear();
        _placeAt.Clear();
    }

    private double Random01()
    {
        var x = _seed;
        x ^= x >> 12;
        x ^= x << 25;
        x ^= x >> 27;
        _seed = x;
        return ((x * 2685821657736338717UL) >> 11) * (1.0 / (1UL << 53));
    }

    // ---- Who is in one ------------------------------------------------------------------------------------------------

    /// <summary>The play under way that holds this person (an index into <see cref="Running"/>), or -1.</summary>
    public int HeldBy(int entity)
    {
        if (entity < 0)
            return -1;
        for (var i = 0; i < RunningCount; i++)
        {
            ref readonly var r = ref Running[i];
            var roles = R.Plays[r.Play].Roles.Length;
            for (var k = 0; k < roles; k++)
                if (r.Cast[k] == entity && (r.Present & 1 << k) != 0)
                    return i;
        }
        return -1;
    }

    public bool Holds(int entity) => HeldBy(entity) >= 0;

    /// <summary>
    /// Whom a person in a play is with now (their body goes beside them): the other one of the beat under way, else the
    /// lead, else (the lead) the next one cast who is a person or a player. -1: in no play.
    /// </summary>
    public int PartnerOf(int entity)
    {
        var i = HeldBy(entity);
        if (i < 0)
            return -1;
        ref readonly var r = ref Running[i];
        var p = R.Plays[r.Play];
        if (r.Beat < p.Beats.Length)
        {
            var b = p.Beats[r.Beat];
            var (by, to) = (r.Cast[b.By], b.To >= 0 ? r.Cast[b.To] : -1);
            if (entity == by && to >= 0)
                return to;
            if (entity == to)
                return by;
        }
        if (r.Cast[0] != entity)
            return r.Cast[0];
        for (var k = 1; k < p.Roles.Length; k++)
            if (r.Cast[k] >= 0 && r.Cast[k] != entity && ((r.Present & 1 << k) != 0 || _e.World.Valid(r.Cast[k]) && _e.World[r.Cast[k]].Kind == StoryKind.Player))
                return r.Cast[k];
        return -1;
    }

    // ---- The director -------------------------------------------------------------------------------------------------

    /// <summary>The clock moved on (<see cref="StoryEngine.Advance"/>): the beats that are due, and now and then a look for a new play.</summary>
    public void Update(double now)
    {
        for (var i = 0; i < RunningCount;)
            if (Step(ref Running[i], now) is { } ended)
                End(i, now, ended.Length == 0 ? null : ended);
            else
                i++;
        if (now < _lookAt)
            return;
        _lookAt = now + LookEvery;
        if (On && Ag.Count > 0 && R.Plays.Length > 0 && RunningCount < MaxRunning)
            Look(now);
    }

    /// <summary>Every play that can be cast now is a candidate; one is drawn by weight and starts (its chance allowing).</summary>
    public void Look(double now, int only = -1)
    {
        Looks++;
        var plays = R.Plays;
        if (_lastAt.Length != plays.Length)
            _lastAt = Enumerable.Repeat(double.MinValue, plays.Length).ToArray();
        var best = -1;
        var bestCast = Ints8.Empty();
        var total = 0f;
        var start = (int)(Random01() * plays.Length) % plays.Length;
        for (var k = 0; k < plays.Length; k++)
        {
            var p = plays[(start + k) % plays.Length];
            if (p.External || (only >= 0 ? p.Id != only : now - _lastAt[p.Id] < p.Every))
                continue;
            if (!CastFor(p, now, out var cast, only >= 0))
                continue;
            total += p.Weight;
            if (Random01() * total < p.Weight)
                (best, bestCast) = (p.Id, cast);
        }
        if (best < 0)
            return;
        var play = plays[best];
        _lastAt[best] = now; // (counted from here, started or not: a missed chance isn't tried every look)
        if (only < 0 && play.Chance < 1 && Random01() >= play.Chance)
            return;
        Begin(play, bestCast, now);
    }

    /// <summary>Starts a play now, cast as it can be (the window's button). False: nobody fits.</summary>
    public bool TryNow(int play, double now)
    {
        var was = Started;
        if ((uint)play < (uint)R.Plays.Length && RunningCount < MaxRunning)
            Look(now, play);
        return Started > was;
    }

    /// <summary>
    /// A cast for the play: a lead (a free person), then its query for the rest, then its extras; everyone who is a person
    /// must be free and with the lead; a place that had a play lately has none.
    /// </summary>
    private bool CastFor(PlayDef p, double now, out Ints8 cast, bool anyTime)
    {
        cast = Ints8.Empty();
        var w = _e.World;
        var none = Ints8.Empty();
        var n = p.Cast.Collect(w, none, 0, _leads);
        if (n == 0)
            return false;
        var first = (int)(Random01() * n) % n;
        for (var k = 0; k < n; k++)
        {
            var lead = _leads[(first + k) % n];
            if (!Free(lead))
                continue;
            var place = Ag.Where(lead);
            if (!anyTime && _placeAt.TryGetValue(place, out var ended) && now - ended < PlaceGap || PlaceBusy(place))
                continue;
            var b = Ints8.Empty();
            b[0] = lead;
            if (!p.Cast.Any(w, b))
                continue;
            if (!Together(p, b, 1, p.CastVars, place))
                continue;
            if (p.Extra != null)
            {
                var more = b;
                if (p.Extra.Any(w, more) && Together(p, more, p.CastVars, p.Roles.Length, place))
                    b = more;
            }
            if (p.Once && _once.Contains(OnceKey(p, b)))
                continue;
            cast = b;
            return true;
        }
        return false;
    }

    /// <summary>The roles from <paramref name="from"/> to <paramref name="to"/> who are people are free and in the place (players and places may be anywhere the query says).</summary>
    private bool Together(PlayDef p, in Ints8 b, int from, int to, int place)
    {
        for (var k = from; k < to; k++)
        {
            var e = b[k];
            if (e < 0 || Ag.Of(e) is not { Dead: false })
                continue; // (a player, a place, or the dead: what the play is about, not in it)
            if (!Free(e) || Ag.Where(e) != place)
                return false;
            for (var j = 0; j < k; j++)
                if (b[j] == e)
                    return false; // (one person, two roles)
        }
        return true;
    }

    /// <summary>A person who could be in a play: alive, in a place, in no other, and idle or at a step with nobody else in it.</summary>
    public bool Free(int entity)
    {
        if (Ag.Of(entity) is not { Dead: false } a || Ag.Where(entity) < 0 || Holds(entity))
            return false;
        return !a.Busy || !Ag.P.Actions[a.Doing.Action].Road && a.Doing.Target < 0;
    }

    private bool PlaceBusy(int place)
    {
        for (var i = 0; i < RunningCount; i++)
            if (Running[i].Place == place)
                return true;
        return false;
    }

    private static ulong OnceKey(PlayDef p, in Ints8 b)
    {
        var h = (ulong)p.Id * 0x9E3779B97F4A7C15UL;
        for (var k = 0; k < p.Roles.Length; k++)
            if ((p.OnceRoles & 1 << k) != 0)
                h = (h ^ (ulong)(uint)b[k]) * 0x100000001B3UL;
        return h;
    }

    private void Begin(PlayDef p, in Ints8 cast, double now)
    {
        if (RunningCount >= MaxRunning)
            return;
        var place = Ag.Where(cast[0]);
        var present = 0;
        for (var k = 0; k < p.Roles.Length; k++)
            if (cast[k] >= 0 && Ag.Of(cast[k]) is { Dead: false } && Ag.Where(cast[k]) == place)
                present |= 1 << k;
        Running[RunningCount++] = new PlayRun
        {
            Play = p.Id, Cast = cast, Place = place, Beat = 0, StartedAt = now, NextAt = now + Math.Min(p.Beats[0].After, 1), WaitSince = -1, Present = present,
        };
        if (p.Once)
            _once.Add(OnceKey(p, cast));
        Started++;
        Ag.Trace?.Invoke(Ag.Of(cast[0])!, $"leads the play {p.Name}: {CastText(p, cast)}");
        Note?.Invoke($"the play {p.Name} begins at {_e.World.NameOf(Running[RunningCount - 1].Place)}: {CastText(p, cast)}");
    }

    /// <summary>The beats that are due. Null: still under way; "": done; else why it was broken off.</summary>
    private string? Step(ref PlayRun r, double now)
    {
        var p = R.Plays[r.Play];
        var w = _e.World;
        while (r.Beat < p.Beats.Length && now >= r.NextAt)
        {
            // Everyone who is a person still here and alive.
            for (var k = 0; k < p.Roles.Length; k++)
                if ((r.Present & 1 << k) != 0 && Ag.Of(r.Cast[k]) is { } a && (a.Dead || Ag.Where(a.Entity) != r.Place))
                    return a.Dead ? $"{w.NameOf(a.Entity)} died" : $"{w.NameOf(a.Entity)} left";
            var beat = p.Beats[r.Beat];
            var by = r.Cast[beat.By];
            var to = beat.To >= 0 ? r.Cast[beat.To] : -1;
            var skip = by < 0 || beat.To >= 0 && to < 0 || (r.Present & 1 << beat.By) == 0
                || beat.If != null && !beat.If.Any(w, r.Cast) || beat.Chance < 1 && Random01() >= beat.Chance;
            if (!skip && Ag.Meet != null && to >= 0 && !Ag.Meet(by, to))
            {
                if (r.WaitSince < 0)
                    r.WaitSince = now;
                if (now - r.WaitSince > MeetWait)
                    return $"{w.NameOf(by)} never got to {w.NameOf(to)}";
                return null; // (their bodies are still on their way: the beat waits)
            }
            r.WaitSince = -1;
            if (!skip && beat.Action >= 0 && !Ag.PlayStep(Ag.Of(by)!, beat.Action, to, r.Place, now))
            {
                if (beat.Must)
                    return $"{w.NameOf(by)} couldn't {Ag.P.Actions[beat.Action].Name}";
                skip = true;
            }
            if (!skip && beat.Then.Length > 0)
                w.Apply(beat.Then, r.Cast);
            if (!skip && beat.Lines.Length > 0)
            {
                var line = beat.Lines[(int)(Random01() * beat.Lines.Length) % beat.Lines.Length];
                if (_e.Talk.SayFor(by, to, Fill(line, p, r.Cast, r.Place), now, r.Play, r.Beat))
                    r.Said++;
            }
            r.Beat++;
            if (r.Beat < p.Beats.Length)
                r.NextAt = now + p.Beats[r.Beat].After;
        }
        return r.Beat >= p.Beats.Length ? "" : null;
    }

    /// <summary>A play is over: kept in the history, and its people think again.</summary>
    private void End(int i, double now, string? broken)
    {
        var r = Running[i];
        var p = R.Plays[r.Play];
        Running[i] = Running[--RunningCount];
        History[HistoryNext] = new PlayDone { Play = r.Play, Cast = r.Cast, Place = r.Place, Beats = r.Beat, Said = r.Said, StartedAt = r.StartedAt, EndedAt = now, Broken = broken };
        HistoryNext = (HistoryNext + 1) % HistorySize;
        HistoryCount = Math.Min(HistoryCount + 1, HistorySize);
        _placeAt[r.Place] = now;
        if (broken == null)
            Finished++;
        else
            BrokenOff++;
        for (var k = 0; k < p.Roles.Length; k++)
            if ((r.Present & 1 << k) != 0 && Ag.Of(r.Cast[k]) is { Dead: false, Busy: false } a && !Holds(a.Entity))
                a.WakeAt = Math.Min(a.WakeAt, now); // (they go back to their own lives)
        if (Ag.Of(r.Cast[0]) is { } lead)
            Ag.Trace?.Invoke(lead, $"the play {p.Name} {(broken == null ? "is over" : $"was broken off: {broken}")}");
        Note?.Invoke($"the play {p.Name} {(broken == null ? "is over" : $"was broken off: {broken}")} ({r.Said} lines)");
        Ended?.Invoke(r.Play, r.Cast, broken);
    }

    /// <summary>An event was recorded: something big where it happened (news of interest 50 and up, a killing) keeps plays away from there a while.</summary>
    public void OnEvent(in StoryEvent ev)
    {
        var interest = Ag.P.NewsInterest;
        if (ev.Place >= 0 && (uint)ev.Verb < (uint)interest.Length && interest[ev.Verb] >= BigNews)
            _placeAt[ev.Place] = Math.Max(_placeAt.GetValueOrDefault(ev.Place, double.MinValue), ev.At);
    }

    /// <summary>Stops every play under way (the window), as broken off.</summary>
    public void StopAll(double now)
    {
        while (RunningCount > 0)
            End(RunningCount - 1, now, "stopped");
    }

    // ---- Words --------------------------------------------------------------------------------------------------------

    /// <summary>A play's line with its roles' names ({a}, {a.first}) and {here} filled in, cut to <see cref="StoryTalk.MaxText"/>.</summary>
    public string Fill(string line, PlayDef p, in Ints8 cast, int place)
    {
        var sb = _sb.Clear();
        var w = _e.World;
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
                who = place;
            else
                for (var k = 0; k < p.Roles.Length; k++)
                    if (name.SequenceEqual(p.Roles[k]))
                        who = cast[k];
            var text = w.NameOf(who);
            sb.Append(first && text.IndexOf(' ') is var sp and > 0 ? text[..sp] : text);
            i = end;
        }
        if (sb.Length > StoryTalk.MaxText)
        {
            var cut = sb.ToString(0, StoryTalk.MaxText - 3);
            return (cut.LastIndexOf(' ') is var sp and > 40 ? cut[..sp] : cut) + "...";
        }
        return sb.ToString();
    }

    /// <summary>Who plays whom: "a = Rhea Lund, b = Karl, p = Landing".</summary>
    public string CastText(PlayDef p, in Ints8 cast)
    {
        var parts = new List<string>(p.Roles.Length);
        for (var k = 0; k < p.Roles.Length; k++)
            if (cast[k] >= 0)
                parts.Add($"{p.Roles[k]} = {_e.World.NameOf(cast[k])}");
        return string.Join(", ", parts);
    }
}
