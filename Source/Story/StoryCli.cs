namespace CallOfSlop.Story;

/// <summary>
/// The story layer's command lines (docs/story/STORY.md): <c>--story-sim FILE</c> plays a scenario headless and prints
/// the chronicle, what the sifter did after each event, the patterns' counts and the world at the end; its exit code is
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
        var engine = new StoryEngine(rules);
        var run = new StoryScenarioRun(scenario, engine);
        Console.WriteLine($"Scenario: {scenario.Name} ({scenario.File}): {scenario.Things.Length} entities, {scenario.Steps.Length} events");
        if (scenario.About.Length > 0)
            Console.WriteLine($"  {scenario.About}");
        Console.WriteLine();
        long seen = 0;
        while (!run.Finished)
        {
            var due = run.Began + scenario.Steps[run.Next].At;
            if (due > engine.Time)
            {
                engine.Advance(due - engine.Time);
                seen = PrintNotes(engine, seen, run.Began);
            }
            var before = engine.Chronicle.Count > 0 ? engine.Chronicle[engine.Chronicle.Count - 1].Id : 0;
            run.Play(engine);
            foreach (ref readonly var ev in engine.Chronicle.All)
                if (ev.Id > before)
                {
                    var story = ev.Verb == rules.StoryVerb;
                    Console.WriteLine($"{(story ? "" : StoryRules.Clock(ev.At - run.Began)),8}  {(story ? "  " : "")}{engine.Sentence(ev)}");
                    if (!story)
                        seen = PrintNotes(engine, seen, run.Began);
                }
            seen = PrintNotes(engine, seen, run.Began);
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
        return b.ToString();
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
            Console.WriteLine($"  relation {r.Name}{(r.Exclusive ? " (exclusive)" : "")}{(r.Symmetric ? " (symmetric)" : "")}: {r.About}");
        foreach (var q in rules.Qualities)
            Console.WriteLine($"  quality {q.Name}: {q.About}");
        foreach (var v in rules.Verbs)
            Console.WriteLine($"  verb {v.Name}{(v.Players ? " [players]" : "")}{(v.Npcs ? " [npcs]" : "")}: {v.About}");
        foreach (var p in rules.Patterns)
            Console.WriteLine($"  pattern {p.Name} ({p.File}): {string.Join(" -> ", p.Stages.Select(s => s.Text))}{(p.Unless.Length > 0 ? $"; unless {string.Join(" or ", p.Unless.Select(u => u.Text))}" : "")}");
        var errors = new List<string>(rules.Errors);
        var scenarios = StoryScenario.LoadFolder(rules, Path.Combine(folder, StoryScenario.FolderName), errors);
        foreach (var s in scenarios)
            Console.WriteLine($"  scenario {s.Name} ({s.File}): {s.Steps.Length} events");
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
