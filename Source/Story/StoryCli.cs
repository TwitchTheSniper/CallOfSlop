namespace CallOfSlop.Story;

/// <summary>
/// The story layer's command lines (docs/story/STORY.md): <c>--story-sim FILE</c> plays a scenario headless and prints
/// the chronicle, what the sifter did after each event (with --trace, what the people chose), the patterns' counts and the
/// world at the end; its exit code is
/// how many of the scenario's "expect" counts were missed (plus one when the story doesn't save and read back the same). <c>--check-story [DIR]</c> loads the story data and every
/// scenario and prints what is refused and why.
/// </summary>
public static class StoryCli
{
    public static int Sim(string[] args) => Tee(() => SimBody(args));

    public static int Check(string[] args) => Tee(() => CheckBody(args));

    /// <summary>Prints to the terminal and to callofslop.log (where Console goes in the game).</summary>
    private static int Tee(Func<int> body)
    {
        using var stdout = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
        var log = Console.Out;
        Console.SetOut(new TeeWriter(stdout, log));
        try
        {
            return body();
        }
        finally
        {
            Console.SetOut(log);
        }
    }

    private static int SimBody(string[] args)
    {
        var file = args.SkipWhile(a => !a.Equals("--story-sim", StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault(a => !a.StartsWith("--"));
        var rules = StoryRules.LoadFolder();
        Console.WriteLine(Summary(rules));
        foreach (var e in rules.Errors)
            Console.WriteLine($"  refused: {e}");
        if (file == null)
        {
            var errors = new List<string>();
            var all = StoryScenario.LoadFolder(rules, null, errors);
            Console.WriteLine("Usage: --story-sim FILE (a scenario). The game's scenarios:");
            foreach (var s in all)
                Console.WriteLine($"  {Path.Combine(StoryRules.FolderName, StoryScenario.FolderName, s.File)}: {s.Name}: {s.About}");
            return 1;
        }
        StoryScenario scenario;
        try
        {
            var path = File.Exists(file) ? file : Path.Combine(AppFolders.Data, file);
            scenario = StoryScenario.Load(rules, File.ReadAllText(path), Path.GetFileName(path));
        }
        catch (Exception ex) when (ex is StoryException or IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"Scenario refused: {ex.Message}");
            return 1;
        }
        // --drama=compare [--runs=N]: the scenario N times (seeds in turn) without and with the drama manager, stories counted.
        var drama = args.FirstOrDefault(a => a.StartsWith("--drama=", StringComparison.OrdinalIgnoreCase))?[8..].ToLowerInvariant() ?? "on";
        if (drama is not ("on" or "off" or "compare"))
        {
            Console.WriteLine("--drama= is on, off or compare");
            return 1;
        }
        if (drama == "compare")
            return CompareDrama(rules, scenario, args);
        var engine = new StoryEngine(rules);
        engine.Drama.On = drama == "on";
        engine.Drama.Strength = Strength(args);
        engine.Drama.Pacing = Pacing(args);
        // --trace (everyone) or --trace=NAME: what the people choose, learn and fail at, as it happens.
        var trace = args.FirstOrDefault(a => a.StartsWith("--trace", StringComparison.OrdinalIgnoreCase));
        if (trace != null)
        {
            var who = trace.Contains('=') ? trace[(trace.IndexOf('=') + 1)..] : "";
            engine.Agents.Trace = (a, line) =>
            {
                var name = engine.World.NameOf(a.Entity);
                if (who.Length == 0 || name.Contains(who, StringComparison.OrdinalIgnoreCase))
                    Console.WriteLine($"{StoryRules.Clock(engine.Time),8}    {name}: {line}");
            };
        }
        // --lines: what the people say, as they say it (each step's line as it starts: headless, there are no bodies to meet).
        var lines = args.Any(a => a.Equals("--lines", StringComparison.OrdinalIgnoreCase));
        var spoke = new Dictionary<int, double>();
        long said = 0;
        var run = new StoryScenarioRun(scenario, engine);
        var playNotes = new List<string>();
        if (lines)
            engine.Plays.Note = text => playNotes.Add($"{StoryRules.Clock(engine.Time - run.Began),8}    ~ {text}");
        Console.WriteLine($"Scenario: {scenario.Name} ({scenario.File}): {scenario.Things.Length} entities, {scenario.Steps.Length} events");
        if (scenario.About.Length > 0)
            Console.WriteLine($"  {scenario.About}");
        Console.WriteLine();
        long seen = 0, dramaSeen = 0;
        var printed = 0;
        // Every event as it is recorded (the people's too), each followed by what the sifter did with it.
        void Print()
        {
            foreach (ref readonly var ev in engine.Chronicle.All)
                if (ev.Id > printed)
                {
                    printed = ev.Id;
                    var story = ev.Verb == rules.StoryVerb;
                    Console.WriteLine($"{(story ? "" : StoryRules.Clock(ev.At - run.Began)),8}  {(story ? "  " : "")}{engine.Sentence(ev)}");
                }
            seen = PrintNotes(engine, seen, run.Began);
            dramaSeen = PrintDrama(engine, dramaSeen);
            if (!lines)
                return;
            var plays = engine.Plays;
            foreach (var a in engine.Agents.All)
                if (a.Busy && !plays.Holds(a.Entity) && (!spoke.TryGetValue(a.Entity, out var was) || was != a.DoingFrom))
                {
                    spoke[a.Entity] = a.DoingFrom;
                    engine.Talk.ForStep(a, engine.Time);
                }
            var talk = engine.Talk;
            foreach (var note in playNotes)
                if (note.Contains(" begins "))
                    Console.WriteLine(note);
            for (said = Math.Max(said, talk.Lines - StoryTalk.Keep); said < talk.Lines; said++)
            {
                ref readonly var t = ref talk.Said[said % StoryTalk.Keep];
                Console.WriteLine($"{StoryRules.Clock(t.At - run.Began),8}    {(t.Radio ? "[RADIO] " : "")}{engine.World.NameOf(t.Speaker)}{(t.To >= 0 && !t.Radio ? $" to {engine.World.NameOf(t.To)}" : "")}: \"{t.Text}\"  ({(t.Play >= 0 ? $"play {engine.Rules.Plays.Plays[t.Play].Name}" : engine.Rules.Lines.Concepts[t.Concept])})");
            }
            foreach (var note in playNotes)
                if (!note.Contains(" begins "))
                    Console.WriteLine(note);
            playNotes.Clear();
        }
        // With people the clock goes a second at a time, so what they do prints in order with the scripted events.
        void RunTo(double t)
        {
            while (engine.Time < t)
            {
                engine.Advance(Math.Min(1, t - engine.Time));
                Print();
            }
        }
        while (!run.Finished)
        {
            RunTo(run.Began + scenario.Steps[run.Next].At);
            run.Play(engine);
            Print();
        }
        if (scenario.Run > 0)
        {
            var end = run.Began + (scenario.Steps.Length > 0 ? scenario.Steps[^1].At : 0) + scenario.Run;
            Console.WriteLine($"{"",8}  (the people live on to {StoryRules.Clock(end - run.Began)})");
            RunTo(end);
        }
        Console.WriteLine();
        Console.WriteLine($"{"Pattern",-24} {"started",8} {"completed",10} {"ran out",8} {"ended",6} {"dropped",8} {"live",5}");
        var live = new int[rules.Patterns.Length];
        foreach (var m in engine.Sifter.Pool)
            if (m.Live)
                live[m.Pattern]++;
        for (var p = 0; p < rules.Patterns.Length; p++)
            Console.WriteLine($"{rules.Patterns[p].Name,-24} {engine.Sifter.Started[p],8} {engine.Sifter.Completed[p],10} {engine.Sifter.RanOut[p],8} {engine.Sifter.Ended[p],6} {engine.Sifter.Dropped[p],8} {live[p],5}");
        Console.WriteLine();
        Console.WriteLine($"World at the end: {engine.World.Count} entities, {engine.World.PairTotal} relationships, {engine.Chronicle.Count} events");
        foreach (var e in engine.World.All)
            Console.WriteLine($"  {e.Name} ({e.Kind}): {Describe(engine, e)}");
        Console.WriteLine();
        var missed = 0;
        foreach (var (name, count) in scenario.Expect)
        {
            var got = engine.Sifter.Completed[rules.Pattern(name)!.Id];
            var ok = got == count;
            if (!ok)
                missed++;
            Console.WriteLine($"Expect {name}: {count}, got {got}: {(ok ? "ok" : "MISSED")}");
        }
        if (engine.Agents.Count > 0)
            PrintPeople(engine);
        if (args.Any(a => a.Equals("--roster", StringComparison.OrdinalIgnoreCase)))
        {
            var roster = StoryRoster.Make(engine);
            Console.WriteLine($"Roster: {roster.Count} names, {roster.Count(r => r.Dead)} dead");
            foreach (var r in roster)
                Console.WriteLine($"  {r.Name} ({(r.Kind == StoryKind.Player ? "player" : "person")}), {StoryRoster.Status(engine, r)}{(r.Dead && r.DiedAt >= 0 ? $" ({StoryRules.Clock(r.DiedAt - run.Began)})" : "")}; renown {r.Renown}"
                    + $"{(r.DeedCount > 0 ? $"; did {StoryRoster.DeedText(engine, r)}" : "")}{(r.StoryCount > 0 ? $"; in {StoryRoster.StoryText(engine, r)}" : "")}{(r.Epitaph.Length > 0 ? $". \"{r.Epitaph}\"" : "")}");
            Console.WriteLine();
        }
        var pl = engine.Plays;
        if (rules.Places.States.Length > 0)
            for (var f = 0; f < engine.Factions.Count; f++)
            {
                var fd = engine.Factions.Def(f);
                var players = Enumerable.Range(0, engine.World.Count).Where(i => engine.World[i].Kind == StoryKind.Player).Select(i => engine.Factions.Party(i)).Distinct().ToList();
                if (players.Count > 0)
                    Console.WriteLine($"Faction {fd.Name}: {string.Join(", ", players.Select(p => $"{engine.World.NameOf(p)} {fd.TierName(engine.Factions.Standing(f, p))} ({engine.Factions.Standing(f, p):+0;-0;0}){(engine.Factions.Notorious(p) ? ", notorious" : "")}"))}");
            }
            Console.WriteLine($"Places: {engine.Places.Turns} changes; now {string.Join(", ", Enumerable.Range(0, engine.World.Count).Where(i => engine.World[i].Kind == StoryKind.Place && !engine.World[i].Gone).Select(i => $"{engine.World.NameOf(i)} {(engine.Places.StateOf(i) is var st and >= 0 ? rules.Places.States[st].Name : "as it was")}"))}");
        Console.WriteLine($"Plays: {pl.Started} started, {pl.Finished} played out, {pl.BrokenOff} broken off ({engine.Rules.Plays.Plays.Length} in the data, {pl.Looks} looks for one)");
        Console.WriteLine($"Talk: {engine.Talk.Lines} lines{(lines ? "" : " (--lines prints them, with each step's)")}, {engine.Talk.Broadcasts} on the radio, {engine.Talk.Quiet} times a rule kept quiet");
        var dm = engine.Drama;
        Console.WriteLine(dm.On
            ? $"Drama: {dm.Given} directions given, {dm.Followed} followed, {dm.MovedOn} done by someone else, {dm.Lapsed} lapsed, {dm.LiveCount} live; {dm.Ranks} ranks, {dm.TotalRankMs / Math.Max(1, dm.Ranks) * 1000:0.0} us each on average, worst {dm.WorstRankMs:0.000} ms"
            : "Drama: off (--drama=off)");
        if (dm.On)
            Console.WriteLine(dm.Pacing
                ? $"Pacing: {dm.Quiets} quiet stretches, {StoryRules.Span(dm.QuietTotal)} quiet in all, highest tension {dm.PeakTension:0.0} (peak {StoryDrama.Peak:0.#}), {dm.Strangers} strangers came; now {(dm.Pace == DramaPace.Quiet ? "quiet" : "building")}, tension {dm.Tension(engine.Time):0.0}"
                : "Pacing: off (--pacing=off)");
        Console.WriteLine($"Sifting: {engine.TotalSiftMs / Math.Max(1, engine.Recorded) * 1000:0.0} us an event on average, worst {engine.WorstSiftMs:0.000} ms (with the stories it completed; the first events include the JIT), {engine.Recorded} events recorded");
        // The save format: written and read back into a fresh engine, the story is the same.
        var save = new SaveWriter(1 << 16);
        var snap = new StorySave();
        engine.Capture(snap); // (once to grow its arrays, as the game's first save does)
        var clock = System.Diagnostics.Stopwatch.StartNew();
        engine.Capture(snap);
        var captureMs = clock.Elapsed.TotalMilliseconds;
        snap.Write(save);

        var copy = new StoryEngine(rules);
        try
        {
            copy.Read(new SaveReader(save.Span.ToArray()));
        }
        catch (WorldFileException ex)
        {
            Console.WriteLine($"Save: {save.Length} bytes, NOT read back: {ex.Message}");
            return missed + 1;
        }
        var same = Fingerprint(engine) == Fingerprint(copy);
        Console.WriteLine($"Save: {save.Length} bytes; the copy on the host's tick took {captureMs:0.00} ms (the bytes are made on the save's worker); read back {(same ? "the same" : "DIFFERENT")}");
        if (engine.Agents.Count > 0)
        {
            // The people's cost on their own: an hour more, nothing printed, as the host's ticks would run it.
            engine.Agents.Trace = null;
            engine.Agents.PerTick = 4;
            var events = engine.Recorded;
            var bytes = GC.GetAllocatedBytesForCurrentThread();
            var clock2 = System.Diagnostics.Stopwatch.StartNew();
            for (var t = 0; t < 3600 * 60; t++)
                engine.Advance(1.0 / 60);
            Console.WriteLine($"An hour more at 60 ticks a second, 4 thinks a tick at most: {clock2.Elapsed.TotalMilliseconds:0} ms, {engine.Recorded - events} events, {(GC.GetAllocatedBytesForCurrentThread() - bytes) / 1024.0:0.#} KB allocated (the chronicle and sifter included)");
        }
        return missed + (same ? 0 : 1);
    }

    /// <summary>Everything a story holds, as text (the save check compares two).</summary>
    private static string Fingerprint(StoryEngine s)
    {
        var b = new System.Text.StringBuilder();
        b.Append(StoryRules.Clock(s.Time)).Append('\n');
        foreach (var e in s.World.All)
            b.Append($"{e.Id} {e.Kind} {e.Name} {e.Key} {e.Gone} {e.Scripted}: {Describe(s, e)}\n");
        foreach (ref readonly var ev in s.Chronicle.All)
            b.Append($"{ev.Id} {ev.At} {ev.Flags} {s.Sentence(ev)}\n");
        foreach (ref readonly var m in s.Sifter.Pool.AsSpan())
            if (m.Live)
                b.Append($"live {m.Pattern} {m.Stage} {s.Sifter.BindText(m.Pattern, m.Binds)} {m.Started} {m.Last}\n");
        foreach (var d in s.Sifter.Done)
            b.Append($"told {d.Pattern} {d.At} {d.StoryEvent} {s.Sifter.Tell(d.Pattern, d.Binds)}\n");
        for (var p = 0; p < s.Rules.Patterns.Length; p++)
            b.Append($"{s.Sifter.Started[p]} {s.Sifter.Completed[p]} {s.Sifter.RanOut[p]} {s.Sifter.Ended[p]} {s.Sifter.Dropped[p]}\n");
        b.Append($"people {s.Agents.Seed}\n");
        foreach (var a in s.Agents.All)
        {
            b.Append($"{a.Entity} {a.Scripted} {a.NeedAt} {a.WakeAt} {a.Goal} {a.GoalTarget} {a.Busy}");
            if (a.Busy)
                b.Append($" {a.Doing.Action} {a.Doing.Target} {a.Doing.Owner} {a.Doing.Place} {a.Doing.Item} {a.DoingFrom} {a.DoingUntil} {a.DoingFromPlace}");
            b.Append($" needs {string.Join(" ", a.Need)} goals {string.Join(" ", a.GoalLast)} rests {string.Join(" ", a.Rests.Select(r => $"{r.Goal}:{r.Target}:{r.Until}"))}\n");
            for (var k = 0; k < a.KnownCount; k++)
            {
                ref readonly var it = ref a.Known[k];
                b.Append($"  {it.Event} {it.Verb} {it.Actor} {it.Target} {it.Owner} {it.Place} {it.Source} {it.From} {it.Interest} {it.At}\n");
            }
        }
        return b.ToString();
    }

    /// <summary>The people at the end: where, doing what and why, their needs and feelings, what they know, how they did.</summary>
    private static void PrintPeople(StoryEngine engine)
    {
        var ag = engine.Agents;
        var w = engine.World;
        var p = engine.Rules.People;
        Console.WriteLine($"People at the end ({ag.Count}{(ag.Deaths > 0 ? $", {ag.Deaths} dead" : "")}{(ag.Meet != null ? $", {ag.MeetWaits} waits to meet, {ag.Unreached} never met" : "")}): {ag.Thinks} thinks, {ag.PlansMade} plans ({ag.PlanFails} found none), {ag.StepsDone} steps; a think took {ag.ThinkMs / Math.Max(1, ag.Thinks) * 1000:0.0} us on average, worst {ag.WorstThinkMs:0.000} ms (the first include the JIT)");
        foreach (var a in ag.All)
        {
            var doing = a.Busy ? $"{p.Actions[a.Doing.Action].Name}{(a.Doing.Target >= 0 ? $" {w.NameOf(a.Doing.Target)}" : "")}" : "idle";
            var where = ag.Where(a.Entity) is var at and >= 0 ? w.NameOf(at) : a.Busy && a.DoingFromPlace >= 0 ? $"on the road to {w.NameOf(a.Doing.Place)}" : "nowhere";
            var goal = a.Goal >= 0 ? $"{p.Goals[a.Goal].Name}{(a.GoalTarget >= 0 ? $" ({w.NameOf(a.GoalTarget)})" : "")}" : "none";
            if (a.Dead)
                (where, doing) = (a.DiedAt >= 0 ? $"dead since {StoryRules.Clock(a.DiedAt)}" : "dead", "nothing");
            Console.WriteLine($"  {w.NameOf(a.Entity)}: {where}, {doing}, goal {goal}; {a.Steps} steps ({a.StepFails} failed), {a.Plans} plans ({a.PlanFails} found none), knows {a.KnownCount}");
            Console.WriteLine($"    needs {string.Join(", ", p.Needs.Select((n, i) => $"{n.Name} {ag.NeedNow(a, i, engine.Time):0}"))}; Wealth {w.Quality(a.Entity, engine.Rules.Quality("Wealth")?.Id ?? 0)}");
            var feel = new List<string>();
            var e = w[a.Entity];
            for (var i = 0; i < e.PairCount; i++)
            {
                var pr = e.Pairs[i];
                var rel = engine.Rules.Relations[pr.Rel].Name;
                if (rel is "Likes" or "Grudge" or "Fears" or "Owes")
                    feel.Add($"{rel} {w.NameOf(pr.Target)} {pr.Value}");
            }
            if (feel.Count > 0)
                Console.WriteLine($"    {string.Join(", ", feel)}");
        }
        Console.WriteLine();
    }

    /// <summary>--pacing=off: the drama manager nudges at full strength all the time and brings no strangers.</summary>
    private static bool Pacing(string[] args) => !args.Any(a => a.Equals("--pacing=off", StringComparison.OrdinalIgnoreCase));

    /// <summary>--strength=X (0 to 2): how hard the drama manager nudges.</summary>
    private static float Strength(string[] args)
    {
        var a = args.FirstOrDefault(x => x.StartsWith("--strength=", StringComparison.OrdinalIgnoreCase));
        return a != null && float.TryParse(a[11..], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) && v is >= 0 and <= 2 ? v : 0.6f;
    }

    private static long PrintDrama(StoryEngine engine, long seen)
    {
        var d = engine.Drama;
        if (d.NoteTotal - seen > StoryDrama.NoteRing)
            seen = d.NoteTotal - StoryDrama.NoteRing;
        for (; seen < d.NoteTotal; seen++)
            Console.WriteLine($"{"",8}    {d.NoteLine(d.Notes[seen % StoryDrama.NoteRing])}");
        return seen;
    }

    /// <summary>
    /// Awash's comparison: the scenario played <c>--runs=N</c> times (8; its seed, then the next ones) with the drama
    /// manager off and on, and each pattern's completed stories counted both ways. Nothing else is printed.
    /// </summary>
    private static int CompareDrama(StoryRules rules, StoryScenario scenario, string[] args)
    {
        var runsArg = args.FirstOrDefault(a => a.StartsWith("--runs=", StringComparison.OrdinalIgnoreCase));
        var runs = runsArg != null && int.TryParse(runsArg[7..], out var n) && n is >= 1 and <= 100 ? n : 8;
        var seed0 = scenario.Seed != 0 ? scenario.Seed : 1;
        var off = new int[rules.Patterns.Length];
        var on = new int[rules.Patterns.Length];
        long given = 0, followed = 0, movedOn = 0, events0 = 0, events1 = 0, quiets = 0, strangers = 0;
        double quiet = 0;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        for (var r = 0; r < runs; r++)
            foreach (var withDrama in (bool[])[false, true])
            {
                scenario.Seed = seed0 + (ulong)r;
                var engine = new StoryEngine(rules);
                engine.Drama.On = withDrama;
                engine.Drama.Strength = Strength(args);
                engine.Drama.Pacing = Pacing(args);
                var run = new StoryScenarioRun(scenario, engine);
                while (!run.Finished)
                {
                    var t = run.Began + scenario.Steps[run.Next].At;
                    while (engine.Time < t)
                        engine.Advance(Math.Min(1, t - engine.Time));
                    run.Play(engine);
                }
                var end = run.Began + (scenario.Steps.Length > 0 ? scenario.Steps[^1].At : 0) + scenario.Run;
                while (engine.Time < end)
                    engine.Advance(Math.Min(1, end - engine.Time));
                var into = withDrama ? on : off;
                for (var p = 0; p < into.Length; p++)
                    into[p] += engine.Sifter.Completed[p];
                if (withDrama)
                {
                    (given, followed, movedOn, events1) = (given + engine.Drama.Given, followed + engine.Drama.Followed, movedOn + engine.Drama.MovedOn, events1 + engine.Recorded);
                    (quiets, strangers, quiet) = (quiets + engine.Drama.Quiets, strangers + engine.Drama.Strangers, quiet + engine.Drama.QuietTotal);
                }
                else
                    events0 += engine.Recorded;
            }
        scenario.Seed = seed0;
        Console.WriteLine($"Scenario: {scenario.Name}, {runs} runs (seeds {seed0} to {seed0 + (ulong)runs - 1}) without and with the drama manager, {clock.Elapsed.TotalSeconds:0.0} s");
        Console.WriteLine();
        Console.WriteLine($"{"Pattern",-24} {"without",8} {"with",8}");
        for (var p = 0; p < rules.Patterns.Length; p++)
            if (off[p] > 0 || on[p] > 0)
                Console.WriteLine($"{rules.Patterns[p].Name,-24} {off[p],8} {on[p],8}");
        Console.WriteLine($"{"All stories",-24} {off.Sum(),8} {on.Sum(),8}");
        Console.WriteLine($"{"Events",-24} {events0,8} {events1,8}");
        Console.WriteLine();
        Console.WriteLine($"With: {given} directions given (strength {Strength(args):0.##}), {followed} followed ({(given > 0 ? 100.0 * followed / given : 0):0}%), {movedOn} done by someone else first");
        Console.WriteLine(Pacing(args)
            ? $"Pacing: {quiets} quiet stretches ({StoryRules.Span(quiet / runs)} quiet a run on average), {strangers} strangers came"
            : "Pacing: off (--pacing=off)");
        return 0;
    }

    private static long PrintNotes(StoryEngine engine, long seen, double began)
    {
        var s = engine.Sifter;
        if (s.NoteTotal - seen > StorySifter.NoteRing)
            seen = s.NoteTotal - StorySifter.NoteRing;
        for (; seen < s.NoteTotal; seen++)
            Console.WriteLine($"{"",8}    {engine.NoteLine(s.Notes[seen % StorySifter.NoteRing], began)}");
        return seen;
    }

    /// <summary>An entity's relationships and qualities on one line.</summary>
    public static string Describe(StoryEngine engine, StoryEntity e)
    {
        var parts = new List<string>();
        for (var i = 0; i < e.PairCount; i++)
        {
            var p = e.Pairs[i];
            parts.Add($"{engine.Rules.Relations[p.Rel].Name} {engine.World.NameOf(p.Target)}{(p.Value != 1 ? $" {p.Value}" : "")}");
        }
        for (var i = 0; i < e.QualityCount; i++)
            parts.Add($"{engine.Rules.Qualities[e.Qualities[i].Quality].Name} {e.Qualities[i].Value}");
        return parts.Count == 0 ? "-" : string.Join(", ", parts);
    }

    public static string Summary(StoryRules rules) =>
        $"[story] rules: {rules.Relations.Length} relations, {rules.Qualities.Length} qualities, {rules.Verbs.Length} verbs, {rules.Patterns.Length} patterns{(rules.Errors.Count > 0 ? $", {rules.Errors.Count} refused" : "")}";

    private static int CheckBody(string[] args)
    {
        var dir = args.SkipWhile(a => !a.Equals("--check-story", StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault(a => !a.StartsWith("--"));
        var folder = dir ?? Path.Combine(AppFolders.Data, StoryRules.FolderName);
        var rules = StoryRules.LoadFolder(folder);
        Console.WriteLine($"{Summary(rules)} from {folder}");
        foreach (var r in rules.Relations)
            Console.WriteLine($"  relation {r.Name}{(r.Exclusive ? " (exclusive)" : "")}{(r.Symmetric ? " (symmetric)" : "")}{(r.Max < 1_000_000 ? $" (max {r.Max})" : "")}: {r.About}");
        foreach (var q in rules.Qualities)
            Console.WriteLine($"  quality {q.Name}: {q.About}");
        foreach (var v in rules.Verbs)
            Console.WriteLine($"  verb {v.Name}{(v.Players ? " [players]" : "")}{(v.Npcs ? " [npcs]" : "")}: {v.About}");
        foreach (var p in rules.Patterns)
            Console.WriteLine($"  pattern {p.Name} ({p.File}): {string.Join(" -> ", p.Stages.Select(s => s.Nudge != 1 ? $"{s.Text} nudge {s.Nudge:0.##}" : s.Text))}{(p.Unless.Length > 0 ? $"; unless {string.Join(" or ", p.Unless.Select(u => u.Text))}" : "")}{(p.Drama != 1 ? $"; drama {p.Drama:0.##}" : "")}");
        var pr = rules.People;
        foreach (var n in pr.Needs)
            Console.WriteLine($"  need {n.Name}: falls {n.FallsPerSecond * 3600:0.#} an hour; {n.About}");
        foreach (var a in pr.Actions)
            Console.WriteLine($"  action {a.Name}: {(a.Road ? "the road's time" : StoryRules.Clock(a.Takes))}{(a.If != null ? $", if {a.If.Text}" : "")}{string.Concat(a.Refills.Select(r => $", {pr.Needs[r.Need].Name} {r.Amount:+0;-0}"))}{(a.Chronicle ? "" : " (routine)")}{(a.News ? " (tells news)" : "")}{(a.Sneaky ? " (sneaky)" : "")}");
        foreach (var t in pr.Tasks)
            Console.WriteLine($"  task {t.Name}({string.Join(", ", t.Params)}): {string.Join("; ", t.Methods.Select(m => $"{m.Name}{(m.If != null ? $" if {m.If.Text}" : "")}: {(m.Do.Length == 0 ? "nothing" : string.Join(", ", m.Do.Select(c => c.Text)))}"))}");
        foreach (var g in pr.Goals)
            Console.WriteLine($"  goal {g.Name} x{g.Weight:0.##}{(g.If != null ? $", if {g.If.Text}" : "")}{(g.Pick != null ? $", pick {g.Pick.Text}" : "")}: {string.Join(" * ", g.Score.Select(c => c.Text))} -> {g.Do.Text}");
        if (pr.Reactions.Length > 0)
            Console.WriteLine($"  reactions: {string.Join("; ", pr.Reactions.Select(r => r.Text))}");
        foreach (var c in pr.Casts)
            Console.WriteLine($"  cast {c.Name} ({c.File}): {c.People.Length} people in {c.Places.Length} places, {c.Roads.Length} roads, {c.Feelings.Length} feelings");
        var lr = rules.Lines;
        if (lr.Rules.Length > 0)
            Console.WriteLine($"  lines: {lr.Rules.Length} rules, {lr.Rules.Sum(r => r.Lines.Length)} lines, on {string.Join(", ", Enumerable.Range(0, lr.Concepts.Count).Select(c => $"{lr.Concepts[c]} {lr.OfConcept[c].Length}"))}");
        for (var c = 0; c < lr.Concepts.Count; c++)
            if (lr.OfConcept[c].Length == 0 && c >= LineRules.Builtin.Length)
                Console.WriteLine($"  lines: the reply {lr.Concepts[c]} has no rules of its own (nobody answers)");
        foreach (var st in rules.Places.States)
            Console.WriteLine($"  place state {st.Name} ({st.File}): {st.Text}, at least {StoryRules.Span(st.Hold)}: \"{st.Sentence}\"{(st.Ends.Length > 0 ? $", then \"{st.Ends}\"" : "")}");
        if (rules.Places.States.Length > 0 && rules.Places.TurnVerb < 0)
            Console.WriteLine("  place states: there is no Turn verb, so a place's change isn't an event (nobody hears of it)");
        foreach (var p in rules.Plays.Plays)
            Console.WriteLine($"  play {p.Name} ({p.File}): cast {p.Cast.Text}{(p.Extra != null ? $"; extra {p.Extra.Text}" : "")}; {p.Beats.Length} beats, every {StoryRules.Span(p.Every)}{(p.Once ? p.OnceRoles == (1 << p.Roles.Length) - 1 ? ", once per cast" : $", once per {string.Join(" and ", p.Roles.Where((_, k) => (p.OnceRoles & 1 << k) != 0).Select(r => "?" + r))}" : "")}");
        foreach (var f in rules.Factions.Factions)
            Console.WriteLine($"  faction {f.Name} ({f.File}): home {(f.Home.Length > 0 ? f.Home : "none")}, {f.Members.Length} members, claims {(f.Claims.Length > 0 ? string.Join(", ", f.Claims.Select(c => $"\"{c}...\"")) : "nothing")}; wants {string.Join(", ", f.Wants.Select(w => $"{w.Name} {w.Start} ({w.PerHour:+0.##;-0.##;0}/h)"))}; tiers {string.Join(", ", f.Tiers.Select(t => t.Name))}; {f.Deeds.Length} deeds");
        var errors = new List<string>(rules.Errors);
        var scenarios = StoryScenario.LoadFolder(rules, Path.Combine(folder, StoryScenario.FolderName), errors);
        foreach (var s in scenarios)
            Console.WriteLine($"  scenario {s.Name} ({s.File}): {s.Steps.Length} events");
        var deskFile = Path.Combine(folder, "Office", "IT.json");
        if (File.Exists(deskFile))
            try
            {
                var desk = ItDesk.Read(File.ReadAllText(deskFile), "Office/IT.json", rules);
                if (rules.Factions.Find(desk.From) is { } its)
                    foreach (var (on, _) in desk.Notices)
                        if (!on.Equals("Notorious", StringComparison.OrdinalIgnoreCase) && !its.Deeds.Any(d => d.Name.Equals(on, StringComparison.OrdinalIgnoreCase)) && !its.Tiers.Any(t => t.Name.Equals(on, StringComparison.OrdinalIgnoreCase)))
                            errors.Add($"Office/IT.json: a notice is on \"{on}\", which is no deed or tier of (nor Notorious) {its.Name} (Story/Factions)");
                Console.WriteLine($"  the IT desk (Office/IT.json): {desk.StepCount} pages ({desk.All.Length} with their alternatives and notices), the first in {StoryRules.Span(desk.FirstMin)} to {StoryRules.Span(desk.FirstMax)}; {desk.Replies.Length} replies");
            }
            catch (StoryException ex)
            {
                errors.Add(ex.Message);
            }
        var scamFile = Path.Combine(folder, "Office", "Scammers.json");
        if (File.Exists(scamFile))
            try
            {
                var scams = ScamDesk.Read(File.ReadAllText(scamFile), "Office/Scammers.json");
                Console.WriteLine($"  the scammers (Office/Scammers.json): {scams.Adverts.Length} adverts, {scams.Scams.Length} scams, {scams.Replies.Length} replies");
            }
            catch (StoryException ex)
            {
                errors.Add(ex.Message);
            }
        foreach (var e in errors.Skip(rules.Errors.Count))
            Console.WriteLine($"  refused: {e}");
        foreach (var e in rules.Errors)
            Console.WriteLine($"  refused: {e}");
        Console.WriteLine(errors.Count == 0 ? "All story data loads." : $"{errors.Count} refused.");
        return errors.Count;
    }

    private sealed class TeeWriter(TextWriter a, TextWriter b) : TextWriter
    {
        public override System.Text.Encoding Encoding => a.Encoding;
        public override void Write(char value) { a.Write(value); b.Write(value); }
        public override void Write(string? value) { a.Write(value); b.Write(value); }
        public override void WriteLine(string? value) { a.WriteLine(value); b.WriteLine(value); }
        public override void WriteLine() { a.WriteLine(); b.WriteLine(); }
    }
}
