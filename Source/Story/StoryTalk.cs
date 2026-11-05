using System.Text;

namespace CallOfSlop.Story;

/// <summary>
/// What an occasion is about, for the response rules (<see cref="LineRule"/>): who speaks (<see cref="Me"/>), to whom, the
/// roles of what it is about (an event seen, news told, a step's), the speaker's place, the occasion's verb and the verb of
/// the news. -1 (0 for <see cref="News"/>): none.
/// </summary>
public struct LineFacts
{
    public int Me, Them, Actor, Target, Place, Here, Owner;
    public int Verb, About;
    public int News; // the event it is about ({news}), 0 none

    public static LineFacts Of(int me) => new() { Me = me, Them = -1, Actor = -1, Target = -1, Place = -1, Here = -1, Owner = -1, Verb = -1, About = -1 };

    /// <summary>The same, said back: the one spoken to answers the speaker.</summary>
    public readonly LineFacts Back() => this with { Me = Them, Them = Me };

    public readonly int Fill(int i) => i switch
    {
        0 => Me,
        1 => Them,
        2 => Actor,
        3 => Target,
        4 => Place,
        5 => Here,
        6 => Owner,
        _ => News > 0 ? News : -1,
    };
}

/// <summary>A line someone said: who, to whom, on what occasion (by which rule), when, and whether on the radio.</summary>
public struct TalkLine
{
    public long N; // its number: the n-th line said
    public int Speaker, To, Concept, Rule; // (a play's line: no concept (-1), and its beat as the rule)
    public int Play; // the play it is a beat of, -1 none
    public string Text;
    public double At;
    public bool Radio;
}

/// <summary>
/// The people's words (docs/story/STORY.md "Lines"; box 5, Valve's response rules): on an occasion the most specific rule
/// whose criteria all hold says one of its lines, with the names filled in. The game asks when a step plays out between
/// two bodies, a player comes up to someone or someone is hurt; the engine itself when a person sees something happen
/// (<see cref="OnEvent"/>), when a rule's line wants a reply, and when a radio operator broadcasts. Everything said is
/// kept in <see cref="Said"/> (the last <see cref="Keep"/>), which the game reads to let the players near the speaker hear
/// it (and everyone hear the radio). Words change nothing in the story: they only show it. Not saved.
/// </summary>
public sealed class StoryTalk
{
    public const int Keep = 64, QueueSize = 32, MaxWitnesses = 2;
    public const double Gap = 8; // s between one speaker's own words (replies, what they saw and the radio aside)
    public const double ReplyAfter = 2.5, SawAfter = 0.8; // s
    public const int MaxText = 118; // a filled line is cut to this (the chat holds 120)

    private readonly StoryEngine _e;

    /// <summary>The host's switch (the Story window): off, nobody says anything.</summary>
    public bool On = true;

    /// <summary>The last <see cref="Keep"/> lines: line n is at n % Keep.</summary>
    public readonly TalkLine[] Said = new TalkLine[Keep];

    /// <summary>Lines said in all, broadcasts among them, and rules that won but kept quiet (their chance).</summary>
    public long Lines, Broadcasts, Quiet;

    private struct Due
    {
        public int Concept;
        public LineFacts F;
        public double At;
    }

    private readonly Due[] _due = new Due[QueueSize];
    private int _dueCount;
    private readonly Dictionary<long, double> _ruleAt = new(); // (speaker, rule) -> when it last won
    private readonly Dictionary<int, double> _spokeAt = new(); // speaker -> when they last spoke of their own accord
    private readonly HashSet<long> _once = new(); // (speaker, rule, listener)
    private readonly Dictionary<int, int> _lastLine = new(); // rule -> its line said last (not twice running)
    private readonly StringBuilder _sb = new(160);
    private ulong _seed = 0x2545F4914F6CDD1DUL;

    public StoryTalk(StoryEngine engine) => _e = engine;

    private LineRules L => _e.Rules.Lines;

    public void Clear()
    {
        Array.Clear(Said);
        Lines = Broadcasts = Quiet = 0;
        _dueCount = 0;
        _ruleAt.Clear();
        _spokeAt.Clear();
        _once.Clear();
        _lastLine.Clear();
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

    /// <summary>The lines a person said, newest first (the window).</summary>
    public IEnumerable<TalkLine> SaidBy(int entity)
    {
        for (var n = Lines - 1; n >= 0 && n >= Lines - Keep; n--)
            if (Said[n % Keep] is { } t && (t.Speaker == entity || entity < 0))
                yield return t;
    }

    // ---- Saying -------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The most specific rule for the occasion whose criteria all hold (ties drawn at random) says one of its lines, filled
    /// in, kept in <see cref="Said"/>; a rule with a reply has the one spoken to answer a moment later. Null: nothing fits,
    /// the speaker spoke too lately (<see cref="Gap"/>, unless <paramref name="prompted"/>), is dead, or the rule kept quiet.
    /// </summary>
    public string? Say(int concept, in LineFacts f, double now, bool prompted = false)
    {
        var lr = L;
        if (!On || (uint)concept >= (uint)lr.OfConcept.Length || f.Me < 0 || _e.Agents.Of(f.Me) is { Dead: true })
            return null;
        if (!prompted && _spokeAt.TryGetValue(f.Me, out var last) && now - last < Gap)
            return null;
        var frame = Ints8.Empty();
        (frame[LineRule.Me], frame[LineRule.Them], frame[LineRule.Actor], frame[LineRule.Target], frame[LineRule.Place], frame[LineRule.Here], frame[LineRule.Owner]) =
            (f.Me, f.Them, f.Actor, f.Target, f.Place, f.Here, f.Owner);
        var have = 0;
        for (var i = 0; i < LineRules.Fills.Length; i++)
            if (f.Fill(i) >= 0)
                have |= 1 << i;
        int best = -1, ties = 0;
        foreach (var id in lr.OfConcept[concept])
        {
            var r = lr.Rules[id];
            if (best >= 0 && r.Weight < lr.Rules[best].Weight)
                break; // (the most specific first: nothing below the best can win)
            if (r.Verb >= 0 && r.Verb != f.Verb || r.About >= 0 && r.About != f.About || (r.Needs & ~have) != 0)
                continue;
            if (r.Every > 0 && _ruleAt.TryGetValue(Key(f.Me, id), out var at) && now - at < r.Every)
                continue;
            if (r.Once && _once.Contains(Key(f.Me, id) * 4099 + f.Them))
                continue;
            if (r.If != null && !r.If.Any(_e.World, frame))
                continue;
            ties++;
            if (best < 0 || Random01() * ties < 1)
                best = id;
        }
        if (best < 0)
            return null;
        var rule = lr.Rules[best];
        _ruleAt[Key(f.Me, best)] = now; // (its "every" counts from here, said or not, so a missed chance isn't tried every moment)
        if (rule.Chance < 1 && Random01() >= rule.Chance)
        {
            Quiet++;
            return null;
        }
        if (rule.Once)
            _once.Add(Key(f.Me, best) * 4099 + f.Them);
        var n = rule.Lines.Length;
        var pick = (int)(Random01() * n) % n;
        if (n > 1 && _lastLine.TryGetValue(best, out var was) && was == pick)
            pick = (pick + 1) % n;
        _lastLine[best] = pick;
        var text = Fill(rule.Lines[pick], f);
        Said[Lines % Keep] = new TalkLine { N = Lines, Speaker = f.Me, To = f.Them, Concept = concept, Rule = best, Text = text, At = now, Radio = concept == LineRules.Radio, Play = -1 };
        Lines++;
        if (concept == LineRules.Radio)
            Broadcasts++;
        if (!prompted)
            _spokeAt[f.Me] = now;
        if (rule.Reply >= 0 && f.Them >= 0 && _e.Agents.Of(f.Them) is { Dead: false })
            Queue(rule.Reply, f.Back(), now + ReplyAfter);
        return text;
    }

    /// <summary>A play's line (<see cref="StoryPlays"/>), already filled: said as it is, kept in <see cref="Said"/>. False: talk is off, or they are dead.</summary>
    public bool SayFor(int speaker, int to, string text, double now, int play, int beat)
    {
        if (!On || speaker < 0 || _e.Agents.Of(speaker) is { Dead: true })
            return false;
        Said[Lines % Keep] = new TalkLine { N = Lines, Speaker = speaker, To = to, Concept = -1, Rule = beat, Text = text, At = now, Play = play };
        Lines++;
        _spokeAt[speaker] = now; // (nothing of their own accord straight after)
        return true;
    }

    /// <summary>
    /// The line the most specific rule for the occasion would say, filled in, without saying it: the same each time for
    /// the same <paramref name="seed"/> (ties and lines drawn from it), with no "every", "once", chance or gap (the
    /// roster's Remembered). Null: no rule fits.
    /// </summary>
    public string? Line(int concept, in LineFacts f, ulong seed)
    {
        var lr = L;
        if ((uint)concept >= (uint)lr.OfConcept.Length || f.Me < 0)
            return null;
        var frame = Ints8.Empty();
        (frame[LineRule.Me], frame[LineRule.Them], frame[LineRule.Actor], frame[LineRule.Target], frame[LineRule.Place], frame[LineRule.Here], frame[LineRule.Owner]) =
            (f.Me, f.Them, f.Actor, f.Target, f.Place, f.Here, f.Owner);
        var have = 0;
        for (var i = 0; i < LineRules.Fills.Length; i++)
            if (f.Fill(i) >= 0)
                have |= 1 << i;
        int best = -1, ties = 0;
        foreach (var id in lr.OfConcept[concept])
        {
            var r = lr.Rules[id];
            if (best >= 0 && r.Weight < lr.Rules[best].Weight)
                break;
            if (r.Verb >= 0 && r.Verb != f.Verb || r.About >= 0 && r.About != f.About || (r.Needs & ~have) != 0 || r.If != null && !r.If.Any(_e.World, frame))
                continue;
            ties++;
            seed = seed * 6364136223846793005UL + 1442695040888963407UL;
            if (best < 0 || (seed >> 33) % (ulong)ties == 0)
                best = id;
        }
        if (best < 0)
            return null;
        var lines = lr.Rules[best].Lines;
        seed = seed * 6364136223846793005UL + 1442695040888963407UL;
        return Fill(lines[(int)((seed >> 33) % (ulong)lines.Length)], f);
    }

    private static long Key(int speaker, int rule) => (long)speaker << 16 | (uint)rule;

    /// <summary>A line with its names filled in ({them}, {them.first}, {news}...), cut to <see cref="MaxText"/>.</summary>
    public string Fill(string line, in LineFacts f)
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
            var slot = Array.IndexOf(LineRules.Fills, name.ToString());
            if (slot == LineRules.Fills.Length - 1)
                sb.Append(NewsText(f));
            else if (slot >= 0)
            {
                var who = w.NameOf(f.Fill(slot));
                sb.Append(first && who.IndexOf(' ') is var sp and > 0 ? who[..sp] : who);
            }
            i = end;
        }
        if (sb.Length > MaxText)
        {
            var cut = sb.ToString(0, MaxText - 3);
            return (cut.LastIndexOf(' ') is var sp and > 40 ? cut[..sp] : cut) + "...";
        }
        return sb.ToString();
    }

    /// <summary>What the news is, as a sentence: the event from the chronicle, or (folded away) from the facts.</summary>
    public string NewsText(in LineFacts f)
    {
        if (f.News > 0 && _e.Chronicle.IndexOf(f.News) is var i and >= 0)
            return _e.Sentence(_e.Chronicle[i]);
        return f.About >= 0 ? _e.Sentence(new StoryEvent { Verb = (ushort)f.About, Actor = f.Actor, Target = f.Target, Owner = f.Owner, Place = f.Place }) : "nothing much";
    }

    private void Queue(int concept, in LineFacts f, double at)
    {
        if (_dueCount >= QueueSize)
            return; // (a crowd talking over itself: the rest go unsaid)
        _due[_dueCount++] = new Due { Concept = concept, F = f, At = at };
    }

    /// <summary>The clock moved on (<see cref="StoryEngine.Advance"/>): the replies and exclamations that are due are said.</summary>
    public void Update(double now)
    {
        for (var i = 0; i < _dueCount;)
        {
            if (_due[i].At > now)
            {
                i++;
                continue;
            }
            var d = _due[i];
            _due[i] = _due[--_dueCount];
            Say(d.Concept, d.F, now, prompted: true);
        }
    }

    // ---- Occasions ----------------------------------------------------------------------------------------------------

    /// <summary>
    /// A person's step plays out (the game: when their body reaches whoever it is with; headless: as it starts): what they
    /// say doing it. A Tell's line is about the news they will tell (its roles are the news's).
    /// </summary>
    public string? ForStep(StoryAgent a, double now)
    {
        if (!On || !a.Busy || a.Dead)
            return null;
        var ag = _e.Agents;
        var act = ag.P.Actions[a.Doing.Action];
        var f = LineFacts.Of(a.Entity);
        (f.Them, f.Actor, f.Target, f.Owner, f.Place, f.Verb) = (a.Doing.Target, a.Entity, a.Doing.Target, a.Doing.Owner, a.Doing.Place, act.Verb);
        f.Here = act.Road ? a.DoingFromPlace : ag.Where(a.Entity);
        if (act.News)
        {
            var k = -1;
            if (act.Radio)
                ag.BestRadioNews(a, now, out k);
            else
                ag.BestNews(a, a.Doing.Target, now, out k);
            if (k < 0)
                return null; // (nothing to tell: the step will fail)
            ref readonly var it = ref a.Known[k];
            (f.News, f.About, f.Actor, f.Target, f.Owner, f.Place) = (it.Event, it.Verb, it.Actor, it.Target, it.Owner, it.Place);
        }
        return Say(LineRules.Step, f, now);
    }

    /// <summary>
    /// An event was recorded (after the people learnt of it): a broadcast goes out on the radio, and up to
    /// <see cref="MaxWitnesses"/> people who saw it happen (not its actor or target) say something a moment later.
    /// </summary>
    public void OnEvent(in StoryEvent ev)
    {
        if (!On || ev.Verb == _e.Rules.StoryVerb || L.Rules.Length == 0)
            return;
        var ag = _e.Agents;
        var p = ag.P;
        var act = (uint)ev.Verb < (uint)p.ActionOfVerb.Length && p.ActionOfVerb[ev.Verb] >= 0 ? p.Actions[p.ActionOfVerb[ev.Verb]] : null;
        if (act is { Radio: true })
        {
            if (ev.Value <= 0)
                return;
            var f = LineFacts.Of(ev.Actor);
            (f.Here, f.Verb, f.News, f.Actor, f.Target) = (ev.Place, ev.Verb, ev.Value, ev.Item, ev.Owner);
            if (_e.Chronicle.IndexOf(ev.Value) is var i and >= 0)
            {
                ref readonly var news = ref _e.Chronicle[i];
                (f.About, f.Owner, f.Place) = (news.Verb, news.Owner, news.Place);
            }
            Say(LineRules.Radio, f, _e.Time, prompted: true);
            return;
        }
        if (ev.Place < 0 || act is { Chronicle: false })
            return;
        var n = ag.All.Count;
        if (n == 0)
            return;
        var start = (int)(Random01() * n) % n;
        var said = 0;
        for (var k = 0; k < n && said < MaxWitnesses; k++)
        {
            var a = ag.All[(start + k) % n];
            if (a.Dead || a.Entity == ev.Actor || a.Entity == ev.Target || ag.Where(a.Entity) != ev.Place)
                continue;
            var f = LineFacts.Of(a.Entity);
            (f.Them, f.Actor, f.Target, f.Owner, f.Place, f.Here, f.Verb, f.About, f.News) = (ev.Actor, ev.Actor, ev.Target, ev.Owner, ev.Place, ev.Place, ev.Verb, ev.Verb, ev.Id);
            Queue(LineRules.Saw, f, _e.Time + SawAfter + said * 1.2);
            said++;
        }
    }
}
