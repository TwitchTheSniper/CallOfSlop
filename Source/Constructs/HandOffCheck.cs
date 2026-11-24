using System.Numerics;
using CallOfSlop.VehicleKit;

namespace CallOfSlop;

/// <summary>
/// <c>--handoff-check [BLUEPRINT] [--seconds S]</c>: the hand-offs toward a building station (ConstructLifterHandOff.cs), headless, in a
/// world of its own. A small Building Station builds BLUEPRINT (default Storage Corner) with no machine linked to it; every part it
/// needs lies on a stock pad 40 m away. Between them: a Kit Plasma Arm by the stock, a Roller Tile run, a second arm, a second run,
/// and the station's own arm beside its pick pad. Each arm reaches only its neighbours, so a part gets to the build only if it is
/// handed on: stock, run 1, run 2, pick pad, build. Prints the leads, each hop and the build's end. Exit code: the failures.
/// </summary>
public static class HandOffCheck
{
    public static int Cli(string[] args)
    {
        var name = args.SkipWhile(a => !a.Equals("--handoff-check", StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault() is { } n0 && !n0.StartsWith("--", StringComparison.Ordinal) ? n0 : "Storage Corner";
        var seconds = args.SkipWhile(a => !a.Equals("--seconds", StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault() is { } s && float.TryParse(s, out var sv) ? sv : 120f;
        var fails = 0;
        void Say(bool ok, string what)
        {
            Console.WriteLine($"  {(ok ? "ok  " : "FAIL")} {what}");
            fails += ok ? 0 : 1;
        }
        var place = VehicleDriveCheck.Place(1);
        var w = new ConstructWorld { CreativeRecycling = true };
        var q = new SelfTest.DriveWorld();
        using var inPlace = place.In();
        float G(float x, float z) => World.Map.GroundHeight(x, z);

        // The station: a Foundation, the Building Station's Core on it and two Bay Blocks (as the Submodule Yard's).
        const string stationText = """
            { "format": "call-of-slop-construct-blueprint/1", "name": "Hand-off Station", "steps": [
              { "part": "Foundation" },
              { "part": "Foundation", "on": 0, "socket": "x+0", "by": "x-0" },
              { "part": "Building Station", "on": 0, "socket": "top01", "by": "-y.0.0" },
              { "part": "Bay Block", "on": 2, "socket": "+x.1", "by": "-x" },
              { "part": "Bay Block", "on": 2, "socket": "+x.2", "by": "-x" },
              { "part": "Bay Block", "on": 3, "socket": "+x", "by": "-x" },
              { "part": "Bay Block", "on": 4, "socket": "+x", "by": "-x" } ] }
            """;
        var bp = ConstructBuild.ParseBlueprint(stationText, "handoff-station.json");
        var job = new ConstructBuild.GroundJob(w, bp, null);
        while (!job.Step(2)) { }
        var sc = job.Join(new Vector3(0, 0, 20), 0, out var why) ?? throw new InvalidOperationException("the station doesn't build: " + why);
        sc.Anchored = true;
        var t = Enumerable.Range(0, sc.PartCount).First(i => sc.DefOf(i).IsBuildStation);
        Freeform.PartWorld(sc, t, out var core, out _);
        ConstructStation.Bay(sc, t, out var floor, out _, out var bay);
        Console.WriteLine($"station: core at {core.X:0.0}, {core.Z:0.0}; bay floor at {floor.X:0.0}, {floor.Z:0.0}, {bay.X:0.0} x {bay.Z:0.0} m");

        Construct Put(string part, float x, float z, float yaw = 0)
        {
            var b = ConstructBuild.PlaceNew(w, ConstructParts.Need(part), new Vector3(x, G(x, z), z), yaw) ?? throw new InvalidOperationException("no room for a " + part);
            b.Anchored = true;
            return b;
        }
        Construct Run(float x, float z0, int tiles)
        {
            var d = ConstructParts.Need("Roller Tile");
            var b = w.Create(new Vector3(x, G(x, z0) - d.BoundsMin.Y + 0.01f, z0), Quaternion.Identity)!;
            for (var i = 0; i < tiles; i++)
                w.AddPart(b, d, new Vector3(0, 0, i * RollerRuns.Cell), 0);
            w.AutoLinkMany(b, 0, b.PartCount);
            b.Anchored = b.Pinned = true;
            b.Rebuild();
            w.StructureDirty.Add(b.Id);
            return b;
        }
        Construct Arm(float x, float z) // (a Kit Plasma Arm: the fixed Plasma Loader Arm went, 2026-11-24)
        {
            var aj = new ConstructBuild.GroundJob(w, ConstructBuild.FindBlueprint("Kit Plasma Arm") ?? throw new InvalidOperationException("no Kit Plasma Arm blueprint"), null);
            while (!aj.Step(2)) { }
            var b = aj.Join(new Vector3(x, G(x, z), z), 0, out var awhy) ?? throw new InvalidOperationException("no room for a Kit Plasma Arm: " + awhy);
            b.Anchored = true;
            return b;
        }
        // West of the core, north to south (+z): the stock pad, arm A, run 1, arm B, run 2, the station's arm C and its pick pad.
        var x0 = core.X - 3;
        var stock = Put("Lay-down Pad", x0, -30);
        var armA = Arm(x0, -24);
        var run1 = Run(x0, -18, 5); // (tiles at z -18 to -10: its start and end about 5 m from arms A and B)
        var armB = Arm(x0, -4);
        var run2 = Run(x0, 2, 5); // (z 2 to 10)
        var armC = Arm(x0, 16);
        var pad = Put("Lay-down Pad", x0 - 5.5f, 16);
        Console.WriteLine($"stock pad {stock.Id}, arm A {armA.Id}, run 1 {run1.Id}, arm B {armB.Id}, run 2 {run2.Id}, arm C {armC.Id}, pick pad {pad.Id}, station {sc.Id}");

        void Step(int ticks)
        {
            for (var k = 0; k < ticks; k++)
            {
                MoverPhysics.Clock++; // (the server tick: Game.ServerTickStep sets it in a game; lifters' moves run on it)
                w.Step(q, World.TickDt);
            }
        }
        Step(30);
        if (ConstructStation.Start(w, sc, t, name, 0) is { } refused)
        {
            Say(false, $"the station won't start {name}: {refused}");
            return fails;
        }
        var j = sc.Parts[t].Station!;
        for (var k = 0; k < 60 * 20 && j.State != StationState.Building; k++)
            Step(1);
        if (j.State != StationState.Building)
        {
            Say(false, $"the job never began building ({j.State})");
            return fails;
        }
        var stocked = 0;
        var need = 0;
        for (var i = 0; i < j.KindN; i++)
            if (ConstructParts.Get(j.KindDef[i]) is { } d)
                for (var m = j.KindFit[i]; m < j.KindNeed[i]; m++, need++)
                    stocked += w.RackPut(stock, 0, d) ? 1 : 0;
        Console.WriteLine($"{j.Name}: {j.Steps} steps, {j.KindN} kinds, {need} parts to fit; {stocked} put on the stock pad");
        // --pad-full: the pick pad filled with parts the build doesn't use, so no hand can put there: the pieces must wait, not go
        // round a run for good (Sean, 2026-11-22: a Hall Crane moved a piece back and forth without end).
        var padFull = args.Any(a => a.Equals("--pad-full", StringComparison.OrdinalIgnoreCase));
        var junk = 0;
        if (padFull)
        {
            // A second pick pad on arm C's other side, full too: a refused hand must not swing between the two.
            var pad2 = Put("Lay-down Pad", x0 + 5.5f, 16);
            foreach (var pp in new[] { pad, pad2 })
                while (junk < 128 && w.RackPut(pp, 0, ConstructParts.Need("Bay Block")))
                    junk++;
        }
        var carries0 = LifterProbe.Carries;
        Step(90);
        Console.WriteLine(ConstructLifters.LeadsText(w));
        if (args.Any(a => a.Equals("--points", StringComparison.OrdinalIgnoreCase)))
        {
            Console.WriteLine(LifterProbe.Points(w).Replace(" [body", "\n [body"));
            foreach (var r in new[] { run1, run2 })
                for (var k = 0; k < r.RouteCount; k++)
                    Console.WriteLine($"  run body {r.Id} route {k}: from {r.Routes[k].From}/{r.Routes[k].FromPort} to {r.Routes[k].To}/{r.Routes[k].ToPort}, nodes {r.Routes[k].Count}, start host {r.RouteNodes[r.Routes[k].Start].Host}, isRun {RollerRuns.IsRun(r, k)}; StartRunAt(0) {RollerRuns.StartRunAt(r, 0)}, EndRunAt(4) {RollerRuns.EndRunAt(r, 4)}");
        }
        var runs = 0;
        var steps = 0;
        var last = -1;
        var stockStore = w.StoreOf(stock, 0)!;
        for (var k = 0; k < seconds * World.TickRate && j.State == StationState.Building; k++)
        {
            Step(1);
            if (k % (5 * World.TickRate) == 0)
            {
                var riders = run1.ConveyedCount + run2.ConveyedCount;
                runs = Math.Max(runs, riders);
                var fitted = 0;
                for (var i = 0; i < j.KindN; i++)
                    fitted += j.KindFit[i];
                if (fitted != last || args.Any(a => a.Equals("--points", StringComparison.OrdinalIgnoreCase)))
                    Console.WriteLine($"  {k / World.TickRate,4} s: fitted {fitted}/{need}, stock {stockStore.ItemCount}, on run 1 {run1.ConveyedCount}, run 2 {run2.ConveyedCount}, pick pad {w.StoreOf(pad, 0)?.ItemCount ?? 0}");
                last = fitted;
                steps = fitted;
            }
        }
        if (args.Any(a => a.Equals("--points", StringComparison.OrdinalIgnoreCase)))
            Console.WriteLine("at the end: " + LifterProbe.Points(w).Replace(" [body", "\n [body"));
        if (padFull)
        {
            // Each part goes at most stock -> run 1 -> run 2 and waits there (or in a hand): a few carries a part, not one every few seconds.
            var carries = LifterProbe.Carries - carries0;
            Say(carries <= 3 * need, $"with the pick pad full ({junk} Bay Blocks) the parts wait: {carries} carries in {seconds:0} s for {need} parts");
            Console.WriteLine(fails == 0 ? "handoff-check: ok" : $"handoff-check: {fails} failure(s)");
            return fails;
        }
        Say(stockStore.ItemCount < stocked, $"the stock pad gave up its parts ({stocked - stockStore.ItemCount} of {stocked} taken)");
        Say(runs > 0, "pieces rode the runs");
        if (j.State == StationState.Building)
        {
            steps = 0;
            for (var i = 0; i < j.KindN; i++)
                steps += j.KindFit[i];
        }
        else
            steps = need;
        Say(j.State != StationState.Building, $"the build finished ({j.State}; {steps} of {need} parts fitted)");
        Console.WriteLine(LifterProbe.LeadCost.Text("leads"));
        Console.WriteLine(fails == 0 ? "handoff-check: ok" : $"handoff-check: {fails} failure(s)");
        return fails;
    }
}
