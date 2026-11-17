using System.Globalization;
using System.Numerics;
using CallOfSlop.VehicleKit;

namespace CallOfSlop.EngineKit;

/// <summary>
/// <c>--gen-engine --genset-run FILE|NAME [--seconds N]</c>: an engine generator set (a blueprint file or a loaded blueprint's name) spawned headless on a flat
/// floor and run against a load that rises and falls (a stand-in draw through the circuit's motor work, the same sum every part's draw goes into): no load,
/// half, full, past full, a fifth; then the tanks run dry and are filled again. Prints every two seconds what the set does (its state, kW out, the draw, rpm,
/// throttle, fuel, charge) and the fuel a kWh at each step, the tick's CPU, and a joined client's copy of the readout (a pose written and read back).
/// Bounded game time (default 150 s), not a long simulation. Exit code: the checks that failed.
/// </summary>
public static class GensetRun
{
    public static int Cli(string file, string[] args)
    {
        var ci = CultureInfo.InvariantCulture;
        var seconds = int.TryParse(args.SkipWhile(a => !a.Equals("--seconds", StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault(), out var sec) ? sec : 150;
        var bp = File.Exists(file) ? ConstructBuild.ParseBlueprint(File.ReadAllText(file), Path.GetFileNameWithoutExtension(file)) : ConstructBuild.FindBlueprint(file);
        if (bp == null)
        {
            Console.WriteLine($"--genset-run: no blueprint {file}");
            return 1;
        }
        var fails = 0;
        void Say(bool ok, string what)
        {
            Console.WriteLine($"  {(ok ? "ok  " : "FAIL")} {what}");
            fails += ok ? 0 : 1;
        }
        var place = VehicleDriveCheck.Place(1);
        var w = new ConstructWorld();
        var q = new SelfTest.DriveWorld();
        var job = new ConstructBuild.GroundJob(w, bp, null);
        while (!job.Step(2))
        {
        }
        Construct c;
        using (place.In())
            c = job.Join(Vector3.Zero, 0, out var why) ?? throw new InvalidOperationException($"{bp.Name} doesn't build: {why}");
        Console.WriteLine(string.Create(ci, $"{bp.Name}: {c.PartCount} parts, anchored {c.Anchored}, heads {c.GenHeads}, engine {(c.Engine >= 0 ? c.DefOf(c.Engine).Name : "none")}, fuel {c.FuelLeft:0} l, charge {c.Charge:0}/{c.ChargeCapacity:0} kJ"));
        if (c.GenHeads == 0 || c.Engine < 0)
        {
            Say(false, "a set: a Generator Head and an engine");
            return fails;
        }
        // The steps: (from s, load as a share of what the set gives, what happens to the fuel then).
        (float At, float Load, string Fuel)[] steps = [(0, 0, ""), (12, 0.5f, ""), (37, 1f, ""), (62, 1.3f, ""), (77, 0.2f, ""), (102, 0.2f, "dry"), (117, 0.2f, "fill"), (seconds, 0, "")];
        var fullFuel = c.Fuel;
        double worst = 0;
        var over = 0;
        var step = 0;
        float fuelAt = c.FuelLeft, kwhAt = 0, kwh = 0;
        var burnMax = 0f;
        var stateSeen = new HashSet<GenState>();
        var outAt = new float[steps.Length];
        var stopped = false;
        var restarted = false;
        using (place.In())
            for (var t = 0; t < seconds * World.TickRate; t++)
            {
                var now = t / (float)World.TickRate;
                if (step + 1 < steps.Length && now >= steps[step + 1].At)
                {
                    var used = fuelAt - c.FuelLeft;
                    var made = kwh - kwhAt;
                    Console.WriteLine(string.Create(ci, $"  step {step} (load {steps[step].Load * 100:0}%): {made:0.###} kWh for {used:0.###} l{(made > 0.001f ? $", {used / made:0.00} l a kWh" : "")}"));
                    outAt[step] = c.GenOut;
                    step++;
                    fuelAt = c.FuelLeft;
                    kwhAt = kwh;
                    if (steps[step].Fuel == "dry")
                    {
                        c.Fuel = 0; // (the tanks run dry)
                        c.Wake();
                    }
                    else if (steps[step].Fuel == "fill")
                    {
                        c.Fuel = fullFuel; // (a tanker fills them)
                        fuelAt = c.FuelLeft;
                        c.Wake();
                    }
                }
                var load = steps[step].Load * MathF.Max(c.GenMax, 1);
                c.MotorWork = load * 1000 * ConstructPower.MotorEfficiency; // (a stand-in draw: ConstructPower.Step counts MotorWork / MotorEfficiency watts, as every part's draw)
                var c0 = SelfTest.ThreadCpu.NowMs();
                var j0 = System.Runtime.JitInfo.GetCompilationTime(true);
                var f0 = c.FuelLeft;
                w.Step(q, World.TickDt);
                var ms = SelfTest.ThreadCpu.NowMs() - c0 - (System.Runtime.JitInfo.GetCompilationTime(true) - j0).TotalMilliseconds;
                if (t > 3 * World.TickRate)
                {
                    worst = Math.Max(worst, ms);
                    over += ms > 8 ? 1 : 0;
                }
                burnMax = MathF.Max(burnMax, (f0 - c.FuelLeft) * World.TickRate);
                kwh += c.GenOut * World.TickDt / 3600f;
                stateSeen.Add(c.Gen);
                if (steps[step].Fuel == "dry" && c.Gen == GenState.NoFuel && !c.EngineRunning)
                    stopped = true;
                if (steps[step].Fuel == "fill" && c.GenOut > 0)
                    restarted = true;
                if (t % (2 * World.TickRate) == 0)
                    Console.WriteLine(string.Create(ci, $"  {now,5:0} s  draw {load,8:0.0} kW  out {c.GenOut,8:0.0} of {c.GenMax:0.0} kW  {c.Gen,-10}  rpm {c.Rpm,5:0}  throttle {c.GenThrottle:0.00}  level {c.GenLevel:0.00}  carried {c.ShaftShare:0.00}  fuel {c.FuelLeft,7:0.0} l  charge {ConstructPower.Share(c) * 100,3:0}%  smoke {c.SmokeLevel:0.00}"));
            }
        Console.WriteLine(string.Create(ci, $"  tick CPU ({SelfTest.ThreadCpu.Kind}): worst {worst:0.00} ms, {over} over 8 ms; most burnt {burnMax:0.###} l/s; states seen {string.Join(", ", stateSeen)}"));
        Say(over == 0, "the tick stays under 8 ms");
        Say(outAt[1] > 0.3f * c.GenMax && outAt[1] < 0.7f * c.GenMax, string.Create(ci, $"half load: {outAt[1]:0.0} kW out"));
        Say(outAt[2] > 0.9f * c.GenMax, string.Create(ci, $"full load: {outAt[2]:0.0} kW out of {c.GenMax:0.0}"));
        Say(stateSeen.Contains(GenState.Overloaded), "past full it says overloaded");
        Say(stopped, "dry tanks stop it");
        Say(restarted, "filled again, it runs again");
        // A joined client's copy: a full body, then a pose, read back into a client's world.
        var client = new ConstructWorld();
        var pw = new PacketWriter(1 << 16);
        ConstructNet.WriteFull(pw, c);
        var r = new PacketReader(pw.Span[1..]);
        ConstructNet.Read(ref r, client, 0);
        c.Asleep = false;
        var cursor = 0;
        uint last = 0;
        pw = new PacketWriter(1 << 16);
        ConstructNet.WritePoses(pw, w, 100, ref cursor);
        var pr = new PacketReader(pw.Span[1..]);
        ConstructNet.ReadPoses(ref pr, client, 1.0, ref last);
        var copy = client.Get(c.Id);
        var head = -1;
        for (var i = 0; i < c.PartCount && head < 0; i++)
            if (c.DefOf(i).PlantShaft > 0)
                head = i;
        Console.WriteLine($"  host's look:   {ConstructGenset.Look(c, head)}");
        if (copy != null)
            Console.WriteLine($"  client's look: {ConstructGenset.Look(copy, head)}");
        Say(copy != null && copy.Gen == c.Gen && MathF.Abs(copy.GenOut - c.GenOut) <= c.GenMax / 100 + 0.1f && MathF.Abs(copy.GenMax - c.GenMax) < 0.01f, "a joined client gets the readout");
        Console.WriteLine(fails == 0 ? $"{bp.Name}: works" : $"{bp.Name}: {fails} failure(s)");
        return fails;
    }
}
