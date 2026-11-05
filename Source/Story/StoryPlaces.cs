using System.Text.Json;

namespace CallOfSlop.Story;

/// <summary>What a place's state counts: events of one verb there lately (at least so many within so long).</summary>
public readonly record struct PlaceRecent(ushort Verb, double Within, int Count);

/// <summary>
/// A state a place can be in (docs/story/STORY.md "Places"; Kenshi's towns that change with the world): what must hold
/// for it, and the words when a place comes to it and leaves it. A place in it carries the quality of its name.
/// </summary>
public sealed class PlaceStateDef
{
    public string Name = "", About = "", File = "", Sentence = "", Ends = "";
    public int Id;
    public ushort Quality;
    public StoryQuery? If; // over ?place
    public PlaceRecent[] Recent = [];
    public StoryQuery.Cmp PeopleOp = StoryQuery.Cmp.NonZero, PlayersOp = StoryQuery.Cmp.NonZero; // NonZero: not asked
    public int People, Players;
    public double Hold = PlaceRules.DefaultHold; // s: at least this long in it (and out of the state before)
    public string Text = ""; // what it asks, as words (the window)
}

/// <summary>The place states from Story/Places/*.json, in order (the first that holds is the one). A bad file is refused whole.</summary>
public sealed class PlaceRules
{
    public const int MaxStates = 64, MaxRecent = 4;
    public const double DefaultHold = 300, MaxWithin = 7 * 86400;

    public PlaceStateDef[] States = [];
    public int TurnVerb = -1; // the event a place's change records ("Turn"), -1: none in the data (states still change)
    public double LongestWithin; // the longest "within" any state counts over

    private static readonly string[] FileKeys = ["about", "states"];
    private static readonly string[] StateKeys = ["name", "about", "if", "recent", "people", "players", "hold", "sentence", "ends"];
    private static readonly string[] RecentKeys = ["verb", "within", "count"];

    public static PlaceRules Load(StoryRules rules, string folder)
    {
        var pr = new PlaceRules { TurnVerb = rules.VerbId("Turn") };
        var all = new List<PlaceStateDef>();
        foreach (var f in StoryRules.Files(Path.Combine(folder, "Places")))
        {
            var name = $"Places/{Path.GetFileName(f)}";
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
        pr.States = all.ToArray();
        pr.LongestWithin = all.SelectMany(s => s.Recent).Select(r => r.Within).DefaultIfEmpty(0).Max();
        return pr;
    }

    private static List<PlaceStateDef> Read(StoryRules rules, JsonElement root, string file, List<PlaceStateDef> before)
    {
        StoryRules.Keys(root, FileKeys, file);
        var list = new List<PlaceStateDef>();
        foreach (var e in StoryRules.List(root, "states", file, true))
        {
            var where = $"{file}: state {list.Count + 1}";
            if (e.ValueKind != JsonValueKind.Object)
                throw new StoryException($"{where}: a state is like {{\"name\": \"Mourning\", \"recent\": [{{\"verb\": \"Kill\", \"within\": \"2h\"}}]}}");
            StoryRules.Keys(e, StateKeys, where);
            var s = new PlaceStateDef { Name = StoryRules.Str(e, "name", where, true), About = StoryRules.Str(e, "about", where), File = file, Id = before.Count + list.Count };
            where = $"{file}: {s.Name}";
            if (before.Concat(list).Any(o => o.Name.Equals(s.Name, StringComparison.OrdinalIgnoreCase)))
                throw new StoryException($"{where}: a state of that name already exists");
            s.Quality = rules.Quality(s.Name)?.Id ?? throw new StoryException($"{where}: a place in this state carries the quality {s.Name}: declare it (Story/Relations)");
            var parts = new List<string>();
            var cond = StoryRules.Str(e, "if", where);
            if (cond.Length > 0)
            {
                try
                {
                    s.If = StoryQuery.Compile(cond, rules, ["place"]);
                }
                catch (StoryException ex)
                {
                    throw new StoryException($"{where}: \"if\": \"{cond}\": {ex.Message}");
                }
                parts.Add(cond);
            }
            var recent = new List<PlaceRecent>();
            foreach (var r in StoryRules.List(e, "recent", where))
            {
                var at = $"{where}: recent {recent.Count + 1}";
                if (r.ValueKind != JsonValueKind.Object)
                    throw new StoryException($"{at}: like {{\"verb\": \"Kill\", \"within\": \"2h\", \"count\": 1}}");
                StoryRules.Keys(r, RecentKeys, at);
                var verb = StoryRules.Str(r, "verb", at, true);
                var v = rules.VerbId(verb);
                if (v < 0)
                    throw new StoryException($"{at}: no verb {verb}");
                var within = StoryRules.Duration(StoryRules.Str(r, "within", at, true)) is { } d and >= 60 and <= MaxWithin ? d : throw new StoryException($"{at}: \"within\" is a time from 1m to 7d, like \"2h\"");
                var count = 1;
                if (r.TryGetProperty("count", out var c))
                    count = c.ValueKind == JsonValueKind.Number && c.TryGetInt32(out var n) && n is >= 1 and <= 100 ? n : throw new StoryException($"{at}: \"count\" is a whole number from 1 to 100");
                recent.Add(new PlaceRecent((ushort)v, within, count));
                parts.Add($"{(count > 1 ? $"{count} " : "")}{verb} within {StoryRules.Span(within)}");
            }
            if (recent.Count > MaxRecent)
                throw new StoryException($"{where}: \"recent\" has {MaxRecent} at most");
            s.Recent = recent.ToArray();
            (s.PeopleOp, s.People) = Count(e, "people", where);
            if (s.PeopleOp != StoryQuery.Cmp.NonZero)
                parts.Add($"people {StoryRules.Str(e, "people", where)}");
            (s.PlayersOp, s.Players) = Count(e, "players", where);
            if (s.PlayersOp != StoryQuery.Cmp.NonZero)
                parts.Add($"players {StoryRules.Str(e, "players", where)}");
            if (parts.Count == 0)
                throw new StoryException($"{where}: a state asks for something (\"if\", \"recent\", \"people\" or \"players\")");
            if (e.TryGetProperty("hold", out _))
                s.Hold = StoryRules.Duration(StoryRules.Str(e, "hold", where)) is { } h and <= 86400 ? h : throw new StoryException($"{where}: \"hold\" is a time up to 1d, like \"10m\"");
            s.Sentence = StoryRules.Str(e, "sentence", where, true);
            s.Ends = StoryRules.Str(e, "ends", where);
            foreach (var t in (ReadOnlySpan<string>)[s.Sentence, s.Ends])
                if (t.Length > 0 && (t.Any(ch => ch < ' ' || ch > '~') || t.Replace("{place}", "").Contains('{') || t.Length > 90))
                    throw new StoryException($"{where}: \"{t}\": a sentence is printable ASCII, 90 characters at most, and fills only {{place}}");
            s.Text = string.Join(", ", parts);
            list.Add(s);
            if (before.Count + list.Count > MaxStates)
                throw new StoryException($"{file}: too many states ({MaxStates} at most in all)");
        }
        return list;
    }

    /// <summary>"people": "0", "&gt;= 3", "&lt; 2": a count compared (none: not asked).</summary>
    private static (StoryQuery.Cmp, int) Count(JsonElement e, string key, string where)
    {
        var s = StoryRules.Str(e, key, where).Replace(" ", "");
        if (s.Length == 0)
            return (StoryQuery.Cmp.NonZero, 0);
        var ops = (ReadOnlySpan<(string, StoryQuery.Cmp)>)[(">=", StoryQuery.Cmp.Ge), ("<=", StoryQuery.Cmp.Le), ("!=", StoryQuery.Cmp.Ne), (">", StoryQuery.Cmp.Gt), ("<", StoryQuery.Cmp.Lt), ("=", StoryQuery.Cmp.Eq)];
        var op = StoryQuery.Cmp.Eq;
        foreach (var (text, cmp) in ops)
            if (s.StartsWith(text, StringComparison.Ordinal))
            {
                (op, s) = (cmp, s[text.Length..]);
                break;
            }
        return int.TryParse(s, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n) && n <= 1000
            ? (op, n) : throw new StoryException($"{where}: \"{key}\" is a count, like \"0\" or \">= 3\"");
    }
}

/// <summary>
/// Places that change with what happens in them (docs/story/STORY.md "Places"; Kenshi): every <see cref="LookEvery"/> s
/// of the story's clock each place is in the first of the states (Story/Places) whose terms hold, or in none. A place in a
/// state carries its quality (so the people's goals, the lines and the radio can ask for it: <c>?here Mourning</c>), and
/// a change is a Turn event at the place, which the people there see and pass on like any news. A state lasts at least its
/// hold. The state is the quality, so it is saved; what happened lately is read again from the chronicle on load.
/// </summary>
public sealed class StoryPlaces
{
    public const double LookEvery = 10;
    public const int RecentSize = 1024;

    private readonly StoryEngine _e;
    private double _lookAt;
    private readonly List<int> _places = new();
    private int _placesOf = -1; // the world's entity count when _places was made
    private readonly Dictionary<int, (int State, double Since)> _now = new(); // place -> its state (-1 none) and since when

    private readonly (int Place, ushort Verb, double At)[] _recent = new (int, ushort, double)[RecentSize];
    private int _recentNext, _recentCount;
    private bool[] _tracked = []; // per verb: a state counts it

    public long Turns, Looks;

    public StoryPlaces(StoryEngine engine) => _e = engine;

    private PlaceRules R => _e.Rules.Places;

    public void Clear()
    {
        _lookAt = 0;
        _places.Clear();
        _placesOf = -1;
        _now.Clear();
        _recentNext = _recentCount = 0;
        Turns = Looks = 0;
    }

    /// <summary>A place's state now (an index into the rules' states), or -1.</summary>
    public int StateOf(int place) => _now.TryGetValue(place, out var s) ? s.State : FromQualities(place);

    /// <summary>Since when a place has been in its state (the story clock; for a loaded one, since the load).</summary>
    public double SinceOf(int place) => _now.TryGetValue(place, out var s) ? s.Since : 0;

    private int FromQualities(int place)
    {
        var w = _e.World;
        if (!w.Valid(place))
            return -1;
        foreach (var st in R.States)
            if (w.Quality(place, st.Quality) != 0)
                return st.Id;
        return -1;
    }

    private bool Tracked(int verb)
    {
        if (_tracked.Length != _e.Rules.Verbs.Length)
        {
            _tracked = new bool[_e.Rules.Verbs.Length];
            foreach (var st in R.States)
                foreach (var r in st.Recent)
                    _tracked[r.Verb] = true;
        }
        return (uint)verb < (uint)_tracked.Length && _tracked[verb];
    }

    /// <summary>An event was recorded: one a state counts is kept (where and when).</summary>
    public void OnEvent(in StoryEvent ev)
    {
        if (ev.Place < 0 || R.States.Length == 0 || !Tracked(ev.Verb))
            return;
        _recent[_recentNext] = (ev.Place, ev.Verb, ev.At);
        _recentNext = (_recentNext + 1) % RecentSize;
        _recentCount = Math.Min(_recentCount + 1, RecentSize);
        _lookAt = Math.Min(_lookAt, ev.At + 1); // (look again soon: a killing shouldn't wait for the next look)
    }

    /// <summary>After a load: what happened lately, from the chronicle (the states themselves are the places' qualities).</summary>
    public void FromChronicle()
    {
        _recentNext = _recentCount = 0;
        _now.Clear();
        var from = _e.Time - R.LongestWithin;
        foreach (ref readonly var ev in _e.Chronicle.All)
            if (ev.At >= from)
                OnEvent(ev);
        _lookAt = 0;
    }

    private int CountRecent(int place, in PlaceRecent r, double now)
    {
        var n = 0;
        for (var i = 0; i < _recentCount; i++)
        {
            ref readonly var x = ref _recent[i];
            if (x.Place == place && x.Verb == r.Verb && now - x.At <= r.Within)
                n++;
        }
        return n;
    }

    /// <summary>The clock moved on (<see cref="StoryEngine.Advance"/>): now and then every place's state is worked out again.</summary>
    public void Update(double now)
    {
        if (now < _lookAt || R.States.Length == 0)
            return;
        _lookAt = now + LookEvery;
        Looks++;
        var w = _e.World;
        if (_placesOf != w.Count)
        {
            _placesOf = w.Count;
            _places.Clear();
            for (var i = 0; i < w.Count; i++)
                if (w[i].Kind == StoryKind.Place && !w[i].Gone)
                    _places.Add(i);
        }
        foreach (var place in _places)
        {
            (int State, double Since) was = _now.TryGetValue(place, out var cur) ? cur : (FromQualities(place), now);
            if (!_now.ContainsKey(place))
                _now[place] = was;
            if (was.State >= 0 && now - was.Since < R.States[was.State].Hold)
                continue; // (it lasts at least its hold)
            var next = Work(place, now);
            if (next == was.State)
                continue;
            Turn(place, was.State, next, now);
        }
    }

    /// <summary>The first state whose terms all hold for the place now, or -1.</summary>
    public int Work(int place, double now)
    {
        var w = _e.World;
        int people = -1, players = -1;
        foreach (var st in R.States)
        {
            if (st.PeopleOp != StoryQuery.Cmp.NonZero || st.PlayersOp != StoryQuery.Cmp.NonZero)
            {
                if (people < 0)
                    (people, players) = Count(place);
                if (st.PeopleOp != StoryQuery.Cmp.NonZero && !Test(st.PeopleOp, people, st.People)
                    || st.PlayersOp != StoryQuery.Cmp.NonZero && !Test(st.PlayersOp, players, st.Players))
                    continue;
            }
            var ok = true;
            foreach (var r in st.Recent)
                if (CountRecent(place, r, now) < r.Count)
                {
                    ok = false;
                    break;
                }
            if (!ok)
                continue;
            if (st.If != null)
            {
                Span<int> b = stackalloc int[StoryRules.MaxVars];
                b.Fill(-1);
                b[0] = place;
                if (!st.If.Any(w, b))
                    continue;
            }
            return st.Id;
        }
        return -1;
    }

    /// <summary>The living people and the players in a place now.</summary>
    public (int People, int Players) Count(int place)
    {
        var w = _e.World;
        var rel = _e.Rules.People.In >= 0 ? _e.Rules.People.In : _e.Rules.Relation("In")?.Id ?? -1;
        if (rel < 0 || !w.Valid(place))
            return (0, 0);
        int people = 0, players = 0;
        var (a, b) = w.HeldRange(place, rel);
        var e = w[place];
        for (var k = a; k < b; k++)
        {
            var who = (int)(uint)e.Held[k];
            if (!w.Valid(who) || w[who].Gone)
                continue;
            if (w[who].Kind == StoryKind.Player)
                players++;
            else if (w[who].Kind == StoryKind.Person && _e.Agents.Of(who) is not { Dead: true })
                people++;
        }
        return (people, players);
    }

    private static bool Test(StoryQuery.Cmp op, int v, int n) => op switch
    {
        StoryQuery.Cmp.Eq => v == n,
        StoryQuery.Cmp.Ne => v != n,
        StoryQuery.Cmp.Lt => v < n,
        StoryQuery.Cmp.Le => v <= n,
        StoryQuery.Cmp.Gt => v > n,
        StoryQuery.Cmp.Ge => v >= n,
        _ => true,
    };

    /// <summary>A place comes to a new state (or out of one): its qualities, and a Turn event there.</summary>
    private void Turn(int place, int from, int to, double now)
    {
        var w = _e.World;
        if (from >= 0)
            w.SetQuality(place, R.States[from].Quality, 0);
        if (to >= 0)
            w.SetQuality(place, R.States[to].Quality, 1);
        _now[place] = (to, now);
        Turns++;
        // The event: entering a state (value: the state + 1), or leaving one for none (minus that) when it says so.
        if (R.TurnVerb >= 0 && (to >= 0 || R.States[from].Ends.Length > 0))
            _e.Record(R.TurnVerb, -1, -1, -1, place, -1, to >= 0 ? to + 1 : -(from + 1));
    }

    /// <summary>A Turn event as a sentence ("Landing is in mourning"), or null when it isn't one.</summary>
    public string? Sentence(in StoryEvent ev)
    {
        if (ev.Verb != R.TurnVerb || R.TurnVerb < 0)
            return null;
        var i = ev.Value > 0 ? ev.Value - 1 : -ev.Value - 1;
        if ((uint)i >= (uint)R.States.Length)
            return $"{_e.World.NameOf(ev.Place)} changed";
        var st = R.States[i];
        var s = ev.Value > 0 ? st.Sentence : st.Ends.Length > 0 ? st.Ends : $"{{place}} is no longer {st.Name.ToLowerInvariant()}";
        s = s.Replace("{place}", _e.World.NameOf(ev.Place));
        return s.Length > 0 ? char.ToUpperInvariant(s[0]) + s[1..] : s;
    }
}
