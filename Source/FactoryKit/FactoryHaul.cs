using System.Numerics;
using CallOfSlop.VehicleKit;

namespace CallOfSlop.FactoryKit;

/// <summary>
/// <c>--haul-check [--post N] [--refinery N] [--hauler N]</c> (factory-generator.md phase 1, the ore loop): a Militia extraction post, a refinery and an Ore
/// Hauler spawned headless on one creative world. The post's buffer is fed (no deposit headless) and fills the loading skip by chute; the hauler is set
/// down under the skip and LOAD is switched on; the hauler is moved to the refinery's tipping bay (the drive itself is a player's: not simulated) and TIP is
/// switched on; then the refinery runs. Each step names what it moved. Exit code: failures. Bounded game time (about 2 minutes), not a long simulation.
/// </summary>
public static class FactoryHaul
{
    public static int Cli(string[] args)
    {
        int Num(string flag, int d) => int.TryParse(args.SkipWhile(a => !a.Equals(flag, StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault(), out var v) ? v : d;
        var fails = 0;
        void Say(bool ok, string what)
        {
            Console.WriteLine($"  {(ok ? "ok  " : "FAIL")} {what}");
            fails += ok ? 0 : 1;
        }
        var post = FactoryScript.Build(new FactoryBrief { Function = "extraction", Style = "Militia", Seed = Num("--post", 1) });
        var refinery = FactoryScript.Build(new FactoryBrief { Function = "refinery", Style = "Militia", Seed = Num("--refinery", 1) });
        var gen = VehicleRegistry.Find("Militia Ore Hauler") ?? throw new FactoryKitException("no Militia Ore Hauler generator");
        var truckText = VehicleRegistry.Make(gen, Num("--hauler", 1)).Text();
        var place = VehicleDriveCheck.Place(1);
        var w = new ConstructWorld { CreativeRecycling = true };
        var q = new SelfTest.DriveWorld();
        Construct Spawn(string text, string name, Vector3 at, float yaw)
        {
            var bp = ConstructBuild.ParseBlueprint(text, name);
            var job = new ConstructBuild.GroundJob(w, bp, null);
            while (!job.Step(2))
            {
            }
            using (place.In())
                return job.Join(at, yaw, out var why) ?? throw new FactoryKitException($"{bp.Name} doesn't build: {why}");
        }
        var pc = Spawn(post.Text(), "post.json", Vector3.Zero, 0);
        var rc = Spawn(refinery.Text(), "refinery.json", new Vector3(120, 0, 0), 0);
        var tc = Spawn(truckText, "hauler.json", new Vector3(60, 0, 40), MathF.PI / 2);
        var table = RecyclingTable.Current;
        var ore = table.IdOf("iron ore");
        var skip = post.IndexOf(post.Programme.Of("loading").First());
        var loadSwitch = skip + 1; // (FactoryScript.Skip: post, hinge, skip, switch)
        var buffer = post.IndexOf(post.Programme.Of("buffer").First());
        var bay = refinery.IndexOf(refinery.Programme.Of("tipping").First());
        int Find(Construct c, string part) => Enumerable.Range(0, c.PartCount).First(i => c.DefOf(i).Name == part);
        var bed = Find(tc, "Tipper Body");
        var tip = Enumerable.Range(0, tc.PartCount).Last(i => tc.DefOf(i).Name == "Switch"); // (OreHauler: the TIP switch is the last; a cab has its own)
        int Amount(Construct c, int part) => w.StoreOf(c, part) is { } st ? st.Amount : 0;
        Vector3 At(Construct c, int part) => ConstructCargo.FromPart(c, part, Vector3.Zero);
        void Run(float seconds)
        {
            using (place.In())
                for (var t = 0; t < seconds * World.TickRate; t++)
                    w.Step(q, World.TickDt);
        }
        void Flip(Construct c, int part)
        {
            c.Parts[part].State ^= 1;
            c.Wake();
        }
        // Moves the truck so its bed's middle is at (x, z), a little over the ground there, and lets it settle.
        void Park(float x, float z, float y)
        {
            var b = At(tc, bed);
            tc.Pos += new Vector3(x - b.X, y - tc.Pos.Y, z - b.Z);
            tc.Vel = Vector3.Zero;
            tc.AngVel = Vector3.Zero;
            tc.Wake();
            Run(3);
        }
        Console.WriteLine($"haul check: {pc.PartCount}-part post, {rc.PartCount}-part refinery, {tc.PartCount}-part hauler");
        var fwd = At(tc, Find(tc, "Fuel Drum")) - At(tc, bed);
        Console.WriteLine($"  hauler faces {(fwd.X < 0 ? "east" : "west")} (drum behind the bed at {fwd.X:0.0}, {fwd.Z:0.0})");
        // 1. The buffer fills the skip by chute.
        w.Deposit(pc, buffer, 2000, ore);
        Run(20);
        var inSkip = Amount(pc, skip);
        Say(inSkip > 0, $"the buffer's chute filled the loading skip: {inSkip} iron ore");
        // 2. The hauler under the skip, LOAD on: the skip tips over the road into its bed.
        var s0 = At(pc, skip);
        var truckY = tc.Pos.Y + FactoryScript.FloorY + 0.05f;
        Park(s0.X, s0.Z + 0.875f + 1.05f, truckY); // (the skip's lip lands about 1 m out from its pivot, which is 0.875 m from its middle)
        var before = Amount(tc, bed);
        Flip(pc, loadSwitch);
        Run(15);
        var s1 = At(pc, skip);
        Console.WriteLine($"  skip moved from {s0.Y:0.00} m to {s1.Y:0.00} m up, {s0.Z:0.00} to {s1.Z:0.00} across ({(s1.Z > s0.Z ? "towards" : "away from")} the road)");
        var loaded = Amount(tc, bed) - before;
        Say(loaded > 0, $"LOAD tipped the skip into the hauler's bed: {loaded} iron ore (skip left {Amount(pc, skip)})");
        Flip(pc, loadSwitch);
        Run(5);
        // 3. At the refinery's tipping bay, the bay on its left: TIP on.
        var hb = At(rc, bay);
        var bayFront = hb.Z + refinery.Programme.Of("tipping").First().Size * 0.5f; // (the hopper's road-side edge)
        Park(hb.X, bayFront + 1.0f, truckY); // (the left wheels along the bay's edge: a side tipper's lip lands about 1.5 m out from its middle)

        var b0 = At(tc, bed);
        Flip(tc, tip);
        var bayMax = 0;
        var sawInto = false;
        var bed0 = Amount(tc, bed);
        var bay0 = Amount(rc, bay);
        var bodies0 = w.Bodies.Count;
        using (place.In())
            for (var t = 0; t < 15 * World.TickRate; t++)
            {
                w.Step(q, World.TickDt);
                bayMax = Math.Max(bayMax, Amount(rc, bay) - bay0);
                sawInto |= w.StoreOf(tc, bed) is { Blocked: ConstructScoops.PourInto };
            }
        Console.WriteLine($"  new bodies in the world while it tipped: {string.Join(", ", w.Bodies.Skip(bodies0).Select(b => $"{b.DefOf(0).Name} at {b.Pos.X:0.0} {b.Pos.Y:0.0} {b.Pos.Z:0.0}").DefaultIfEmpty("none"))}");
        var b1 = At(tc, bed);
        var up = ConstructCargo.FromPart(tc, bed, Vector3.UnitY) - b1;
        Console.WriteLine($"  bed moved from {b0.Y:0.00} m to {b1.Y:0.00} m up, {b0.Z:0.00} to {b1.Z:0.00} across; its up now {up.X:0.00} {up.Y:0.00} {up.Z:0.00}; truck at {tc.Pos.X:0.0} {tc.Pos.Y:0.0} {tc.Pos.Z:0.0}, switch {tc.Parts[tip].State}");
        var tipped = bed0 - Amount(tc, bed);
        var spilt = w.Bodies.Count - bodies0;
        Say(tipped > 0 && sawInto && spilt == 0, $"TIP poured the bed into the Tipping Bunker: {tipped} iron ore out of the bed, the bunker's most at once {bayMax}, {spilt} bundle(s) spilt");
        Flip(tc, tip);
        // 4. The refinery takes it from there (the stone a Foundry wants comes as creative stock: the loop carries ore).
        foreach (var o in refinery.Programme.Of("heap").Where(o => o.Material != "iron ore"))
            w.Deposit(rc, refinery.IndexOf(o), 2000, table.IdOf(o.Material));
        var product = refinery.Programme.Of("loading").Concat(refinery.Programme.Of("bin")).Select(o => refinery.IndexOf(o)).ToArray();
        var made0 = product.Sum(i => Amount(rc, i));
        Run(60);
        var made = product.Sum(i => Amount(rc, i)) - made0;
        Say(made > 0, $"the refinery made {made} from it in 60 s");
        Console.WriteLine(fails == 0 ? "haul check: the ore loop runs" : $"haul check: {fails} failure(s)");
        return fails;
    }
}
