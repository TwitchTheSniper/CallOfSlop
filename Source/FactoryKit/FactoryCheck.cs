using System.Diagnostics;
using System.Numerics;
using CallOfSlop.VehicleKit;

namespace CallOfSlop.FactoryKit;

/// <summary>
/// <c>--factory-check NAME [--seconds 90]</c> (factory-generator.md 3.8 check 2): a generated factory spawned headless on a creative world, its intake fed from
/// creative stock, switched on and run for game time (bounded, not a long simulation). Every main edge must carry something, the product must move and
/// the tick must stay under 8 ms; a dry edge names itself. NAME is "Style function seed" ("Militia refinery 3") or a blueprint name. Exit code: failures.
/// </summary>
public static class FactoryCheck
{
    public static int Cli(string name, string[] args)
    {
        var seconds = int.TryParse(args.SkipWhile(a => !a.Equals("--seconds", StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault(), out var s) ? s : 90;
        FactoryBrief brief;
        try
        {
            brief = Parse(name);
        }
        catch (FactoryKitException e)
        {
            Console.WriteLine($"--factory-check: {e.Message}");
            return 1;
        }
        var fails = 0;
        void Say(bool ok, string what)
        {
            Console.WriteLine($"  {(ok ? "ok  " : "FAIL")} {what}");
            fails += ok ? 0 : 1;
        }
        var script = FactoryScript.Build(brief);
        var text = script.Text();
        var bp = ConstructBuild.ParseBlueprint(text, FactoryScript.FileName(brief));
        var place = VehicleDriveCheck.Place(1);
        var w = new ConstructWorld { CreativeRecycling = true };
        var q = new SelfTest.DriveWorld();
        var job = new ConstructBuild.GroundJob(w, bp, null);
        var slices = 0;
        var worstSlice = 0.0;
        var clock = new Stopwatch();
        while (true)
        {
            clock.Restart();
            var done = job.Step(2); // (the game's sliced spawn: a slice a tick, VehicleSpawn.RequestBlueprint)
            worstSlice = Math.Max(worstSlice, clock.Elapsed.TotalMilliseconds);
            slices++;
            if (done)
                break;
        }
        Construct c;
        clock.Restart();
        using (place.In())
            c = job.Join(Vector3.Zero, 0, out var why) ?? throw new FactoryKitException($"{bp.Name} doesn't build: {why}");
        Console.WriteLine($"{bp.Name}: {c.PartCount} parts, spawned in {slices} slices of 2 ms (worst {worstSlice:0.0} ms), joined to the world in {clock.Elapsed.TotalMilliseconds:0.0} ms");
        // The objects' parts: the script's own indexes (a blueprint builds its steps in order, one part each).
        int PartOf(FactoryObject o) => script.IndexOf(o);
        foreach (var o in script.Programme.Objects)
            if (PartOf(o) is var i && i >= 0 && c.DefOf(i).Name != o.Part)
                Say(false, $"object {o.Id} ({o.Part}) is a {c.DefOf(i).Name} in the built body");
        // Feed: the heaps and the tipping bay from creative stock (the hauler's load), and an extraction's buffer (no deposit headless).
        var table = RecyclingTable.Current;
        foreach (var o in script.Programme.Objects.Where(o => o.Role is "heap" or "tipping" || o.Role == "buffer"))
        {
            var put = w.Deposit(c, PartOf(o), 2000, table.IdOf(o.Material));
            Console.WriteLine($"  fed {o.Part} {o.Id} ({o.Role}): {put} {o.Material}");
        }
        var got = new int[script.Programme.Objects.Count];
        var worked = new bool[script.Programme.Objects.Count];
        var start = script.Programme.Objects.Select(o => Amount(w, c, PartOf(o))).ToArray();
        double worst = 0, total = 0;
        int ticks = 0, over = 0, worstAt = 0;
        using (place.In())
            for (var t = 0; t < seconds * World.TickRate; t++)
            {
                var c0 = SelfTest.ThreadCpu.NowMs(); // (CPU time: another build on the box mustn't count against the tick)
                var j0 = System.Runtime.JitInfo.GetCompilationTime(true);
                var g0 = GC.CollectionCount(0);
                var a0 = GC.GetAllocatedBytesForCurrentThread();
                w.Step(q, World.TickDt);
                var ms = SelfTest.ThreadCpu.NowMs() - c0;
                var jit = (System.Runtime.JitInfo.GetCompilationTime(true) - j0).TotalMilliseconds; // (a first call's compile: one-off, CLAUDE.md's 8 ms rule leaves it out)
                ms -= jit;
                if (t >= 3 * World.TickRate) // (the first 3 s: JIT, the first pours and seals)
                {
                    if (ms > worst)
                        (worst, worstAt) = (ms, t);
                    if (jit > 4)
                        Console.WriteLine($"  tick {t}: {jit:0.0} ms compiling code met for the first time (left out of the tick)");
                    if (ms > 8 && over++ < 8)
                        Console.WriteLine($"  tick {t}: {ms:0.0} ms CPU, {GC.CollectionCount(0) - g0} collection(s) in it, {GC.GetAllocatedBytesForCurrentThread() - a0} bytes allocated in it");
                    total += ms;
                    ticks++;
                }
                if (t % 30 == 0)
                    foreach (var o in script.Programme.Objects)
                    {
                        got[o.Id] = Math.Max(got[o.Id], Amount(w, c, PartOf(o)) - start[o.Id]);
                        worked[o.Id] |= Working(w, c, PartOf(o));
                    }
            }
        Console.WriteLine($"  ran {seconds} s of game time: tick CPU less first-call compiling ({SelfTest.ThreadCpu.Kind}) {total / Math.Max(1, ticks):0.00} ms mean, {worst:0.00} ms worst (tick {worstAt}), {over} tick(s) over 8 ms");
        Say(over <= 1, $"the tick stays under 8 ms (one-off JIT aside: {over} over)");
        foreach (var e in script.Programme.Edges.Where(e => e.Main))
        {
            var to = script.Programme.Objects[e.To];
            var from = script.Programme.Objects[e.From];
            if (from.Role == "drill")
            {
                Console.WriteLine($"  --   {from.Part} {from.Id} -> {to.Part} {to.Id}: drills need a deposit, none headless (not run)");
                continue;
            }
            var passed = to.Role == "bin" && script.Programme.Edges.Any(x => x.From == to.Id && got[x.To] > 0); // (a bin passes it straight on)
            Say(got[to.Id] > 0 || worked[to.Id] || passed, $"{from.Part} {from.Id} -> {to.Part} {to.Id} ({e.Material}) carried {got[to.Id]}");
        }
        foreach (var o in script.Programme.Objects.Where(o => o.Role == "process"))
            Say(worked[o.Id], $"{o.Part} {o.Id} ({o.Material}) worked");
        var product = script.Programme.Objects.Where(o => o.Role is "bin" or "loading").Sum(o => Amount(w, c, PartOf(o)));
        foreach (var o in script.Programme.Objects.Where(o => o.Role is "bin" or "loading"))
            Console.WriteLine($"  {o.Role} {o.Id} holds {Amount(w, c, PartOf(o))} {(w.StoreOf(c, PartOf(o)) is { Material: > 0 } st ? table.NameOf(st.Material) : "nothing")}");
        Say(product > 0, $"the product moved: {product} in the bins and the loading bay");
        Console.WriteLine(fails == 0 ? $"{bp.Name}: works" : $"{bp.Name}: {fails} failure(s)");
        return fails;
    }

    /// <summary>All a store holds: a bin's bulk, a machine's output, input and mixed input.</summary>
    private static int Amount(ConstructWorld w, Construct c, int part) => part >= 0 && w.StoreOf(c, part) is { } st ? st.Amount + st.InAmount + (st.MixAmount?.Take(st.MixN).Sum() ?? 0) : 0;

    /// <summary>A machine at work now: a batch in hand or output waiting.</summary>
    private static bool Working(ConstructWorld w, Construct c, int part) => part >= 0 && w.StoreOf(c, part) is { } st && (st.JobTicks > 0 || st.Amount > 0);

    /// <summary>"Militia refinery 3", or a shipped blueprint's name "Militia Refinery 3".</summary>
    public static FactoryBrief Parse(string name)
    {
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 3 || !int.TryParse(words[^1], out var seed))
            throw new FactoryKitException($"\"{name}\": give \"Style function seed\", as \"Militia refinery 3\"");
        var function = string.Join(" ", words[1..^1]).ToLowerInvariant() switch
        {
            "extraction post" => "extraction",
            var f => f,
        };
        return new FactoryBrief { Style = words[0], Function = function, Seed = seed };
    }
}
