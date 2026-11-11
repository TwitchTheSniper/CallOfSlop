using CallOfSlop.Interiors;
using CallOfSlop.VehicleKit;

namespace CallOfSlop;

public static unsafe partial class Program
{
    /// <summary>
    /// <c>--gen-interior &lt;blueprint name or file&gt; [--seed S] [--out DIR] [--plan] [--chassis hollow|armoured]</c>: the interior planner and builder (docs/sandbox/INTERIORS.md).
    /// Replays the blueprint through the build rules, plans an interior for its deck by its tonnage class, builds it and writes
    /// "&lt;Name&gt; Fitted" under DIR (default: the repository's Constructs/Blueprints), loads it back and prints the check. --plan prints the
    /// ASCII deck maps and the room list. --chassis swaps the blueprint's root for the Capital Hollow Chassis 128x64 m or the Capital Armoured
    /// Hull 128x64 m in the written file only (the blueprint itself is never changed). A root with floor sockets inside its hollow gets the
    /// chassis void built too (phase 4). Exit code: the check's failures.
    /// </summary>
    private static int GenInterior(string target, string[] args)
    {
        string? Flag(string f) => args.SkipWhile(a => !a.Equals(f, StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal));
        var seed = 1;
        if (Flag("--seed") is { } s && !int.TryParse(s, out seed))
        {
            Console.WriteLine($"--seed {s}: not a whole number");
            return 1;
        }
        InteriorData data;
        try
        {
            data = InteriorData.Load(InteriorData.Folder);
        }
        catch (InteriorDataException e)
        {
            Console.WriteLine($"interior data refused: {e.Message}");
            return 1;
        }
        if (data.UnknownParts() is { Count: > 0 } unknown)
            Console.WriteLine($"note: parts the data names that this build lacks: {string.Join(", ", unknown)}");
        foreach (var pr in ConstructLoot.Problems.Where(pr => pr.StartsWith("Loot.json: room ", StringComparison.Ordinal)))
            Console.WriteLine($"note: a room loot table item left out: {pr}");
        foreach (var hook in data.LootHooks.Values.OfType<string>().Distinct())
            if (!ConstructLoot.Tables.TryGetValue(hook, out var lt))
                Console.WriteLine($"note: loot hook table \"{hook}\" isn't in Loot.json");
            else
                Console.WriteLine($"loot table \"{hook}\": {lt.Items.Length} items, {lt.RollsMin}-{lt.RollsMax} rolls, {lt.Empty:0%} empty, tier {lt.MaxTier}");
        ConstructBuild.Blueprint bp;
        if (File.Exists(target))
            bp = ConstructBuild.ParseBlueprint(File.ReadAllText(target), Path.GetFileName(target));
        else if (ConstructBuild.FindBlueprint(target) is { } named)
            bp = named;
        else
        {
            Console.WriteLine($"no blueprint file or name \"{target}\"");
            return 1;
        }
        if (Flag("--chassis") is { } chassis)
        {
            var root = chassis.ToLowerInvariant() switch { "hollow" => "Capital Hollow Chassis 128x64 m", "armoured" or "armored" => "Capital Armoured Hull 128x64 m", _ => null };
            if (root == null || ConstructParts.Find(root) == null)
            {
                Console.WriteLine($"--chassis {chassis}: hollow or armoured{(root != null ? $" (no part \"{root}\" in this build)" : "")}");
                return 1;
            }
            Console.WriteLine($"--chassis {chassis}: the root {bp.Steps[0].Part} is swapped for the {root} in the written file");
            bp.Steps[0] = bp.Steps[0] with { Part = root };
        }
        var clock = System.Diagnostics.Stopwatch.StartNew();
        InteriorBuilder b;
        InteriorPlan plan;
        InteriorPlan? vplan = null;
        try
        {
            b = InteriorBuilder.From(data, bp);
            var top = b.Class!;
            if (args.Any(a => a.Equals("--no-halls", StringComparison.OrdinalIgnoreCase)))
                top = top with { Halls = [] }; // (phase 9: the plan as before the halls, to compare)
            if (top.Chassis.Count > 0 && b.VoidHull(out var notHollow) is { } hollow)
            {
                // phase 4: the class's chassis rooms go below, in the hollow (sealed: its way in is the stairs and the lift), and leave the deck's programme
                var decks = (int)MathF.Floor(hollow.Size.Y / top.Deck.Storey + 1e-4f);
                var vcls = top.ForChassis(decks); // (the fill entries that may go below the deck: InteriorData.FillMix)
                vplan = InteriorPlanner.Plan(data, vcls, hollow, seed, @sealed: true);
                var rest = top.Programme.ToList();
                foreach (var room in top.Chassis)
                    rest.Remove(room);
                top = top with { Programme = rest };
                Console.WriteLine($"chassis void: {hollow.Size.X:0}x{hollow.Size.Y:0.0}x{hollow.Size.Z:0} m on the deck's grid, {vplan.Decks} storeys from level {b.VoidLevel}, {hollow.KeepOuts.Count} beams as keep-outs; the deck keeps {rest.Count} of {b.Class.Programme.Count} programme rooms");
            }
            plan = InteriorPlanner.Plan(data, top, b.Hull!, seed);
        }
        catch (VehicleKitException e)
        {
            Console.WriteLine($"{bp.Name}: FAIL {e.Message}");
            return 1;
        }
        var planMs = clock.Elapsed.TotalMilliseconds;
        Console.WriteLine($"{bp.Name}: {b.Tonnes:0} t, class {b.Class!.Name}{(data.ProvisionalClasses ? " (provisional bands)" : "")}, hull {b.Hull!.Size.X:0}x{b.Hull.Size.Z:0} m, {b.Hull.KeepOuts.Count} parts checked as keep-outs");
        if (args.Any(a => a.Equals("--plan", StringComparison.OrdinalIgnoreCase)))
            Console.Write(plan.Ascii());
        if (vplan != null && args.Any(a => a.Equals("--plan", StringComparison.OrdinalIgnoreCase)))
            Console.Write(vplan.Ascii());
        if (args.Any(a => a.Equals("--plan", StringComparison.OrdinalIgnoreCase)) && b.Class.Chassis.Count > 0 && vplan == null)
        {
            // the chassis void (phase 3): planned with the class's chassis rooms on as many storeys as the hollow holds; not built yet
            if (b.ChassisVoid(out var whyNot, out var inner) is not { } hollow)
                Console.WriteLine($"chassis void: none ({whyNot})");
            else
            {
                var decks = (int)MathF.Floor(hollow.Size.Y / b.Class.Deck.Storey + 1e-4f);
                var cls = b.Class.ForChassis(decks); // (crew stay above: the void fills only with what may go below)
                var planned = InteriorPlanner.Plan(data, cls, hollow, seed);
                Console.WriteLine($"chassis void: {hollow.Size.X:0}x{hollow.Size.Y:0.0}x{hollow.Size.Z:0} m, {decks} storeys, {hollow.KeepOuts.Count} beams as keep-outs, {inner} floor sockets inside: PLANNED ONLY, not built");
                Console.Write(planned.Ascii());
                foreach (var l in InteriorPlanner.Check(planned, data, out _))
                    Console.WriteLine("  void " + l);
            }
        }
        var lines = InteriorPlanner.Check(plan, data, out var failures);
        if (vplan != null)
        {
            lines.AddRange(InteriorPlanner.Check(vplan, data, out var vf).Select(l => "void " + l));
            failures += vf;
        }
        CallOfSlop.VehicleKit.KitProfile.Reset();
        CallOfSlop.VehicleKit.KitProfile.On = true; // (phase 10: where the build's time goes, by the kit's stages)
        try
        {
            b.Build(plan, seed, vplan);
        }
        catch (VehicleKitException e)
        {
            Console.WriteLine($"build: FAIL {e.Message}");
            failures++;
        }
        var text = b.Text();
        var buildMs = clock.Elapsed.TotalMilliseconds - planMs;
        var outDir = Flag("--out") ?? VehicleRegistry.RepoBlueprints();
        Directory.CreateDirectory(outDir);
        var path = Path.Combine(outDir, VehicleRegistry.Slug(b.Name) + ".json");
        File.WriteAllText(path, text);
        Console.WriteLine($"wrote {path}");
        foreach (var l in lines)
            Console.WriteLine("  " + l);
        Console.WriteLine($"  parts: {b.Placed} placed ({b.Welded} welded), {b.Doors} doors hung ({b.SwingDoors} swing, {b.SlideDoors} sliding, {b.DoubleDoors} double; {b.OpenDoorways} doorways open), {b.LootSpotsSet} loot containers, {b.Refused.Count} refused; plan {planMs:0} ms, build {buildMs:0} ms");
        Console.WriteLine($"  build stages: {string.Join(", ", b.StageMs.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value} ms"))}");
        CallOfSlop.VehicleKit.KitProfile.On = false;
        Console.Write(CallOfSlop.VehicleKit.KitProfile.Report(1));
        foreach (var g in b.Refused.GroupBy(r => string.Join(' ', r.Split(' ').Take(2))).Where(g => g.Count() > 1).OrderByDescending(g => g.Count()))
            Console.WriteLine($"  refused {g.Count()} times: {g.Key} ...");
        foreach (var r in b.Refused.GroupBy(r => string.Join(' ', r.Split(' ').Take(2))).SelectMany(g => g.Take(3)).Take(40))
            Console.WriteLine($"  refused: {r}");
        if (b.Refused.Count > 40)
            Console.WriteLine($"  ... and {b.Refused.Count - 40} more refused");
        foreach (var n in b.Notes)
            Console.WriteLine($"  note: {n}");
        foreach (var m in b.Missing.Distinct())
            Console.WriteLine($"  missing part: {m}");
        foreach (var l in b.BudgetLines())
            Console.WriteLine($"  {l}");
        // the real loader on what was written
        var back = ConstructBuild.ParseBlueprint(text, Path.GetFileName(path));
        var c = ConstructBuild.BuildOnGround(new ConstructWorld(), back, System.Numerics.Vector3.Zero, 0, out var why);
        if (c == null)
        {
            Console.WriteLine($"  load back: FAIL it doesn't build: {why}");
            failures++;
        }
        else
            Console.WriteLine($"  load back: {c.PartCount} parts from {back.Steps.Count} steps{(c.PartCount == b.Live.PartCount ? "" : $" (the script had {b.Live.PartCount})")}");
        // the drive chain (phase 9, after a regression where the bridge's helm lost its wire into a leg): a base vehicle that drives must
        // still drive from the fitted one's seat, or the run fails loudly
        var bare = ConstructBuild.BuildOnGround(new ConstructWorld(), bp, System.Numerics.Vector3.Zero, 0, out _);
        if (bare != null && bare.DriveSeat >= 0)
        {
            if (c == null || c.DriveSeat < 0)
            {
                Console.WriteLine($"  drive: FAIL the base {bp.Name} drives from its {bare.Parts[bare.DriveSeat].D.Name}, but the fitted one has no seat wired through a Driving Module to its drivetrain or legs");
                failures++;
            }
            else
                Console.WriteLine($"  drive: the fitted {b.Name} drives from its {c.Parts[c.DriveSeat].D.Name} (part {c.DriveSeat}), as the base drives from its {bare.Parts[bare.DriveSeat].D.Name}");
        }
        failures += b.Refused.Count > 0 ? 1 : 0;
        if (Flag("--seed") != null && plan.Ascii() != InteriorPlanner.Plan(data, plan.Class, b.Hull, seed).Ascii())
        {
            Console.WriteLine("  FAIL two plans of one seed differ");
            failures++;
        }
        return failures;
    }
}
