namespace CallOfSlop.Story;

/// <summary>One name on the roster: who they were, whether they are still about, what they did, and how they are remembered.</summary>
public sealed class RosterEntry
{
    public int Entity;
    public StoryKind Kind;
    public string Name = "";
    public bool Dead, Gone;
    public double Born, DiedAt = -1;
    public int KilledBy = -1, DiedIn = -1, Where = -1, Home = -1, Works = -1;
    public int Renown, StoryCount, DeedCount;
    public (int Verb, int Count)[] Deeds = []; // as the actor, most first
    public (int Pattern, int Count)[] Stories = []; // the stories told with them in, most first
    public string Epitaph = "";
}

/// <summary>
/// The legacy roster (docs/story/STORY.md "Roster"; box 5): everyone the story has known, people and players, the living
/// and the dead, with their deeds (the chronicle's events they did), the stories told with them in (the sifter's), how
/// they died, and a line to remember them by (the response rules' Remembered occasion: the most specific rule for them,
/// the same line each time). Made when asked (the window, the CLI), from what the story already holds: nothing is kept
/// for it, so it costs nothing until it is looked at.
/// </summary>
public static class StoryRoster
{
    public static List<RosterEntry> Make(StoryEngine e)
    {
        var w = e.World;
        var rules = e.Rules;
        var p = rules.People;
        var list = new List<RosterEntry>();
        var of = new Dictionary<int, RosterEntry>();
        for (var i = 0; i < w.Count; i++)
        {
            var ent = w[i];
            if (ent.Kind is not (StoryKind.Person or StoryKind.Player))
                continue;
            var r = new RosterEntry { Entity = i, Kind = ent.Kind, Name = ent.Name, Gone = ent.Gone, Born = ent.Born };
            var a = e.Agents.Of(i);
            r.Dead = a?.Dead == true || p.Dead >= 0 && w.Quality(i, p.Dead) != 0;
            if (a is { Dead: true })
                r.DiedAt = a.DiedAt;
            r.Where = e.Agents.Where(i);
            r.Home = First(w, i, p.Home);
            r.Works = First(w, i, p.WorksAt);
            list.Add(r);
            of[i] = r;
        }
        // Deeds, from the chronicle (the people's routine steps never were events), and how the dead died.
        var deeds = new Dictionary<(int, int), int>();
        var skip = new bool[rules.Verbs.Length]; // (coming and going isn't a deed: trips, joining and leaving)
        foreach (var act in p.Actions)
            if (act.Road)
                skip[act.Verb] = true;
        foreach (var name in (ReadOnlySpan<string>)["Join", "Leave"])
            if (rules.VerbId(name) is var v and >= 0)
                skip[v] = true;
        foreach (ref readonly var ev in e.Chronicle.All)
        {
            if (ev.Verb == rules.StoryVerb || ev.Verb == rules.Places.TurnVerb)
                continue;
            if (ev.Actor >= 0 && of.ContainsKey(ev.Actor) && !(ev.Verb < skip.Length && skip[ev.Verb]))
                deeds[(ev.Actor, ev.Verb)] = deeds.GetValueOrDefault((ev.Actor, ev.Verb)) + 1;
            if (ev.Verb == p.KillVerb && ev.Target >= 0 && of.TryGetValue(ev.Target, out var killed) && killed.Dead)
                (killed.KilledBy, killed.DiedIn, killed.DiedAt) = (ev.Actor, ev.Place, killed.DiedAt >= 0 ? killed.DiedAt : ev.At);
            else if (ev.Verb == p.DieVerb && ev.Actor >= 0 && of.TryGetValue(ev.Actor, out var died) && died.Dead && died.KilledBy < 0)
                (died.DiedIn, died.DiedAt) = (ev.Place, died.DiedAt >= 0 ? died.DiedAt : ev.At);
        }
        foreach (var r in list)
        {
            r.Deeds = deeds.Where(d => d.Key.Item1 == r.Entity).Select(d => (d.Key.Item2, d.Value)).OrderByDescending(d => d.Value).ToArray();
            r.DeedCount = r.Deeds.Sum(d => d.Count);
        }
        // The stories told with them in (each story once, whichever part they had).
        var stories = new Dictionary<(int, int), int>();
        foreach (var d in e.Sifter.Done)
        {
            var vars = rules.Patterns[d.Pattern].Vars.Length;
            for (var k = 0; k < vars; k++)
            {
                var who = d.Binds[k];
                if (who < 0 || !of.ContainsKey(who))
                    continue;
                var seen = false;
                for (var j = 0; j < k; j++)
                    seen |= d.Binds[j] == who;
                if (!seen)
                    stories[(who, d.Pattern)] = stories.GetValueOrDefault((who, d.Pattern)) + 1;
            }
        }
        foreach (var r in list)
        {
            r.Stories = stories.Where(s => s.Key.Item1 == r.Entity).Select(s => (s.Key.Item2, s.Value)).OrderByDescending(s => s.Value).ToArray();
            r.StoryCount = r.Stories.Sum(s => s.Count);
            // Renown: a story counts five; a deed as much as it is news (a killing 8, a theft 7, a trade 1).
            r.Renown = r.StoryCount * 5 + r.Deeds.Sum(d => d.Count * Math.Max(1, ((uint)d.Verb < (uint)p.NewsInterest.Length ? p.NewsInterest[d.Verb] : 0) / 10));
            var f = LineFacts.Of(r.Entity);
            (f.Target, f.Actor, f.Place) = (r.Entity, r.KilledBy, r.DiedIn);
            f.Here = r.Home >= 0 ? r.Home : r.Works >= 0 ? r.Works : r.DiedIn >= 0 ? r.DiedIn : r.Where;
            if (r.Dead)
                f.About = r.KilledBy >= 0 ? p.KillVerb : p.DieVerb;
            r.Epitaph = e.Talk.Line(LineRules.Remembered, f, (ulong)r.Entity * 0x9E3779B97F4A7C15UL + 0x5EED) ?? "";
        }
        return list.OrderByDescending(r => r.Renown).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static int First(StoryWorld w, int id, int rel)
    {
        if (rel < 0)
            return -1;
        var (a, b) = w.Range(id, rel);
        return b > a ? w[id].Pairs[a].Target : -1;
    }

    /// <summary>How they stand, in words: "alive, at Landing", "dead: killed by Karl at Landing", "on the road".</summary>
    public static string Status(StoryEngine e, RosterEntry r)
    {
        var w = e.World;
        if (r.Dead)
            return r.KilledBy >= 0 ? $"killed by {w.NameOf(r.KilledBy)}{(r.DiedIn >= 0 ? $" at {w.NameOf(r.DiedIn)}" : "")}"
                : $"died{(r.DiedIn >= 0 ? $" at {w.NameOf(r.DiedIn)}" : "")}";
        if (r.Kind == StoryKind.Player)
            return r.Where >= 0 ? $"here, at {w.NameOf(r.Where)}" : "away";
        return r.Where >= 0 ? $"alive, at {w.NameOf(r.Where)}" : "on the road";
    }

    public static string DeedText(StoryEngine e, RosterEntry r, int most = 4) =>
        r.Deeds.Length == 0 ? "" : string.Join(", ", r.Deeds.Take(most).Select(d => $"{e.VerbName(d.Verb)} x{d.Count}")) + (r.Deeds.Length > most ? ", ..." : "");

    public static string StoryText(StoryEngine e, RosterEntry r, int most = 3) =>
        r.Stories.Length == 0 ? "" : string.Join(", ", r.Stories.Take(most).Select(s => $"{e.Rules.Patterns[s.Pattern].Name}{(s.Count > 1 ? $" x{s.Count}" : "")}")) + (r.Stories.Length > most ? ", ..." : "");
}
