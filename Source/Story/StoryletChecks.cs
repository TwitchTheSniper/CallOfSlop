namespace CallOfSlop.Story;

/// <summary>
/// What <c>--check-story</c> warns about storylets (docs/story/STORY.md "Storylets"), the same rules as the storylet
/// builder's live warnings: storylets that can never play (nothing sets what they need, or more than a quality's max),
/// what storylets set that nothing reads, dead ends (nothing a storylet changes opens another, and it isn't "final"),
/// roles nobody in any cast can fill, a "next" that names no storylet, and choices with no end by time. Warnings, not
/// refusals: a storylet still loads. Only the storylets' own facts are followed (their qualities, Course, Lead, Visited,
/// Inducted, Contraband); how people feel about each other comes from the rest of the story and isn't checked here.
/// </summary>
public static class StoryletChecks
{
    public static readonly string[] OwnFacts = ["Course", "Lead", "Visited", "Inducted", "Contraband"];

    /// <summary>A fact: a quality, or a relation towards a named thing (null: towards anyone).</summary>
    private readonly record struct Fact(bool Quality, int What, string? Name);

    public static List<string> Check(StoryRules rules)
    {
        var sr = rules.Storylets;
        var warnings = new List<string>();
        if (sr.Storylets.Length == 0)
            return warnings;
        var own = new HashSet<(bool, int)>();
        foreach (var q in sr.Qualities)
            own.Add((true, q.Id));
        foreach (var n in OwnFacts)
        {
            if (rules.Quality(n) is { } q)
                own.Add((true, q.Id));
            if (rules.Relation(n) is { } r)
                own.Add((false, r.Id));
        }
        string FactText(Fact f) => f.Quality ? rules.Qualities[f.What].Name : $"{rules.Relations[f.What].Name}{(f.Name != null ? $" '{f.Name}'" : "")}";

        // What sets each fact: the storylets' ends and beats, the verbs' effects, the patterns' "then", the spots.
        var writes = new Dictionary<int, List<Fact>>(); // per storylet
        var anyWrite = new HashSet<Fact>();
        var outside = new HashSet<Fact>();
        foreach (var v in rules.Verbs)
            foreach (var fx in v.Effects)
                outside.Add(new Fact(fx.Quality, fx.What, null));
        foreach (var p in rules.Patterns)
            foreach (var fx in p.Then)
                outside.Add(new Fact(fx.Quality, fx.What, null));
        if (sr.Visited >= 0)
            foreach (var s in sr.Spots)
                outside.Add(new Fact(false, sr.Visited, s.Name));
        foreach (var d in sr.Storylets)
        {
            var list = new List<Fact>();
            foreach (var end in d.Ends)
            {
                foreach (var fx in end.Then)
                    list.Add(new Fact(fx.Quality, fx.What, !fx.Quality && fx.Object >= d.CastVars ? d.Named[fx.Object - d.CastVars] : null));
                if (end.Learn >= 0 && sr.Lead >= 0)
                    list.Add(new Fact(false, sr.Lead, d.Named[end.Learn - d.CastVars]));
            }
            if (d.Play >= 0)
                foreach (var b in rules.Plays.Plays[d.Play].Beats)
                    foreach (var fx in b.Then)
                        list.Add(new Fact(fx.Quality, fx.What, null));
            writes[d.Index] = list;
            foreach (var f in list)
                anyWrite.Add(f);
        }
        bool Written(Fact read)
        {
            foreach (var w in anyWrite.Concat(outside))
                if (w.Quality == read.Quality && w.What == read.What && (read.Name == null || w.Name == null || w.Name.Equals(read.Name, StringComparison.OrdinalIgnoreCase)))
                    return true;
            return false;
        }

        // What each storylet reads: its needs (each alternative), its cast and its ends' "if".
        static IEnumerable<(StoryQuery.Clause C, string Text)> Clauses(StoryQuery? q) =>
            q == null ? [] : q.Clauses.Select((c, i) => (c, q.ClauseTexts[i]));
        Fact? Read(StoryQuery.Clause c) => c.Kind switch
        {
            StoryQuery.ClauseKind.Quality => new Fact(true, c.What, null),
            StoryQuery.ClauseKind.Relation => new Fact(false, c.What, c.B.Var < 0 ? c.B.Name : null),
            _ => null,
        };
        var reads = new Dictionary<int, List<Fact>>();
        foreach (var d in sr.Storylets)
        {
            var list = new List<Fact>();
            foreach (var q in d.Needs.Append(d.Cast).Concat(d.Ends.Select(e => e.If)))
                foreach (var (c, _) in Clauses(q))
                    if (Read(c) is { } f)
                        list.Add(f);
            reads[d.Index] = list;
        }
        bool ReadBy(Fact w, int except)
        {
            foreach (var (k, list) in reads)
                if (k != except)
                    foreach (var r in list)
                        if (r.Quality == w.Quality && r.What == w.What && (r.Name == null || w.Name == null || r.Name.Equals(w.Name, StringComparison.OrdinalIgnoreCase)))
                            return true;
            return false;
        }

        foreach (var d in sr.Storylets)
        {
            var at = $"Storylets: {d.Id} ({d.File})";
            // Can never play: an alternative of its needs reads one of the storylets' facts nothing sets, or asks more than a quality's max.
            var blocked = new List<string>();
            foreach (var q in d.Needs)
            {
                string? why = null;
                foreach (var (c, text) in Clauses(q))
                {
                    if (c.Not || Read(c) is not { } f || !own.Contains((f.Quality, f.What)))
                        continue;
                    if (!Written(f))
                        why = $"nothing sets {FactText(f)} (\"{text}\")";
                    else if (f.Quality && sr.Qualities.FirstOrDefault(x => x.Id == f.What) is { } sq
                             && (c.Op == StoryQuery.Cmp.Ge && c.N > sq.Max || c.Op == StoryQuery.Cmp.Gt && c.N >= sq.Max || c.Op == StoryQuery.Cmp.Eq && (c.N > sq.Max || c.N < sq.Min)))
                        why = $"{sq.Name} goes up to {sq.Max} (\"{text}\")";
                    if (why != null)
                        break;
                }
                if (why != null)
                    blocked.Add(why);
            }
            if (blocked.Count > 0 && blocked.Count == d.Needs.Length)
                warnings.Add($"{at}: can never play: {string.Join("; or ", blocked)}");
            else
                foreach (var b in blocked)
                    warnings.Add($"{at}: one way in can never hold: {b}");

            // Unread: something it sets that no other storylet reads.
            foreach (var f in writes[d.Index].Distinct())
                if (own.Contains((f.Quality, f.What)) && !ReadBy(f, -1))
                    warnings.Add($"{at}: sets {FactText(f)}, which no storylet reads");

            // Dead end.
            if (!d.Final && !writes[d.Index].Any(f => ReadBy(f, d.Index)) && !d.Ends.Any(e => e.Next.Length > 0))
                warnings.Add($"{at}: a dead end: nothing it changes opens another storylet (\"final\": true if that's meant)");
            foreach (var e in d.Ends)
                if (e.Next.Length > 0 && sr.Find(e.Next) == null)
                    warnings.Add($"{at}: end {e.Id}: \"next\": no storylet {e.Next}");
            if (d.HasChoice && !d.Ends.Any(e => e.After > 0))
                warnings.Add($"{at}: has choices but no end by time: it waits for ever if the player never answers");
            if (d.Job >= 0 && !d.Ends.Any(e => e.When?.Verb == rules.Factions.Job))
                warnings.Add($"{at}: hands out {d.JobText} but no end waits for the Job");
        }
        warnings.AddRange(Uncastable(rules));
        return warnings;
    }

    /// <summary>Roles nobody can fill: each storylet's cast tried in the world of every cast (Story/Casts), a player there, its needs aside (a clause on how someone stands to the player passes: that comes with play).</summary>
    private static IEnumerable<string> Uncastable(StoryRules rules)
    {
        var sr = rules.Storylets;
        var casts = rules.People.Casts;
        var best = new int[sr.Storylets.Length]; // the most clauses that held, in any cast's world (-1: all of them)
        var bestText = new string[sr.Storylets.Length];
        Array.Fill(best, -2);
        foreach (var cast in casts)
        {
            var e = new StoryEngine(rules);
            var world = e.World.Create(StoryKind.Place, "World", 1, 0);
            e.Agents.AddCast(cast, world, scripted: false);
            e.Factions.Ensure();
            var me = e.World.Create(StoryKind.Player, "Tester", 2, 0);
            if (sr.In >= 0)
                e.World.Set(me, sr.In, world, 1);
            Span<int> b = stackalloc int[StoryRules.MaxVars];
            foreach (var d in sr.Storylets)
            {
                if (d.Cast == null || best[d.Index] == -1)
                    continue;
                b.Fill(-1);
                b[0] = me;
                var k = d.Cast.FirstFailing(e.World, b);
                if (k >= 0 && d.Cast.Clauses[k] is var c && (c.A.Var == 0 || c.B.Var == 0))
                    k = -1; // (how they stand to the player comes with play: not this check's to judge)
                if (k < 0)
                    best[d.Index] = -1;
                else if (k > best[d.Index])
                    (best[d.Index], bestText[d.Index]) = (k, $"{d.Cast.ClauseTexts[k]} (in {cast.Name})");
            }
        }
        foreach (var d in sr.Storylets)
            if (d.Cast != null && best[d.Index] != -1)
                yield return $"Storylets: {d.Id} ({d.File}): nobody in any cast can be cast: {(casts.Length == 0 ? "there are no casts" : bestText[d.Index])}";
    }
}
