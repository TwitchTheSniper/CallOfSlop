using System.Globalization;
using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// <c>--engine-check FILE [--seconds S] [--cut-at S]</c>: an engine train run headless (the "Ship engines with interior parts" design).
/// The blueprint is built on a flat floor and held there; each thruster is given full throttle through its wire (a Toggle that starts
/// on, wired to the bell, is the rig's way), and once a second its level, cap, feed and injector buffer are printed, and any Sensor Ring's four outputs. With
/// <c>--cut-at</c> the fuel pipes into the train are removed at that time, as a shot through them would, so the buffer's run-down and
/// the stop show. A tool: it allocates, never on a tick.
/// </summary>
public static class EngineTrainCheck
{
    public static int Cli(string file, string[] args)
    {
        var seconds = 20f;
        var cutAt = -1f;
        for (var i = 0; i < args.Length - 1; i++)
            if (args[i].Equals("--seconds", StringComparison.OrdinalIgnoreCase))
                seconds = float.Parse(args[i + 1], CultureInfo.InvariantCulture);
            else if (args[i].Equals("--cut-at", StringComparison.OrdinalIgnoreCase))
                cutAt = float.Parse(args[i + 1], CultureInfo.InvariantCulture);
        if (!File.Exists(file) && ConstructBuild.FindBlueprint(file) is { } shipped)
            return Run(shipped, seconds, cutAt);
        var text = File.ReadAllText(file);
        return Run(ConstructBuild.ParseBlueprint(text, Path.GetFileNameWithoutExtension(file)), seconds, cutAt);
    }

    private static int Run(ConstructBuild.Blueprint bp, float seconds, float cutAt)
    {
        var q = new SelfTest.DriveWorld();
        var w = new ConstructWorld();
        var c = ConstructBuild.BuildOnGround(w, bp, Vector3.Zero, 0, out var why);
        if (c == null)
        {
            Console.WriteLine($"{bp.Name}: doesn't build: {why}");
            return 1;
        }
        // (it flies off under its own push: the burn is what is read)
        Console.WriteLine($"{bp.Name}: {c.PartCount} parts, {c.ThrusterCount} thruster(s), fuel {c.FuelLeft:0.0} l");
        foreach (var m in c.Modules)
            Console.WriteLine($"  module on its plug: peak push {m.Thrust / 1000:0} kN{(m.Rich ? " (a Mixture Controller: on rich)" : "")}, {m.Mass:0} kg");
        for (var k = 0; k < c.ThrusterCount; k++)
        {
            ref var th = ref c.Thrusters[k];
            var gp = ConstructBoost.GimbalOf(c, th);
            if (gp >= 0)
                Console.WriteLine($"  {c.Parts[th.Part].D.Name}: swings {c.Parts[gp].D.ThrustGimbal * 180f / MathF.PI:0.#} deg ({(gp == th.Part ? "its own gimbal" : c.Parts[gp].D.Name)}), throw {ConstructAero.ThrowOf(c.Parts[gp], c.Parts[gp].D, c.Parts[gp].D.ThrustGimbal) * 180f / MathF.PI:0.#} deg");
            if (ConstructWorks.TrainArmour(c, th.Part) is var arm and > 0)
                Console.WriteLine($"  {c.Parts[th.Part].D.Name}: Armour Collars take {arm:0} more off each hit on it and its hot zone");
        }
        var problems = 0;
        var ticks = (int)(seconds * World.TickRate);
        var cut = false;
        for (var t = 0; t <= ticks; t++)
        {
            var now = t * World.TickDt;
            if (!cut && cutAt >= 0 && now >= cutAt)
            {
                cut = true;
                var n = 0;
                for (var k = c.RouteCount - 1; k >= 0; k--)
                    if (c.Routes[k].Type == RouteType.Pipe && c.Routes[k].To < c.PartCount && c.DefOf(c.Routes[k].To).WorksKind == WorksKind.Injector)
                    {
                        ConstructRoutes.Remove(w, c, k);
                        n++;
                    }
                Console.WriteLine($"  t {now,5:0.0} s: cut {n} pipe(s) into the injector");
            }
            if (t % World.TickRate == 0)
                Swing(c, w, now);
            if (t % World.TickRate == 0)
                for (var k = 0; k < c.ThrusterCount; k++)
                {
                    ref var th = ref c.Thrusters[k];
                    var h = c.WorksHeadOf(th.Part);
                    Console.WriteLine($"  t {now,5:0.0} s: {c.Parts[th.Part].D.Name}: level {th.Level:0.00}, cap {th.Cap * 100:0}%, fed {(th.Fed ? "yes" : "no")}, buffer {th.Buffer:0.0} s, push {c.Parts[th.Part].D.ThrustForce * th.Cap * th.Level * (th.Mixer >= 0 ? ConstructPhysics.MixOf(c, th.Mixer).Push : 1) / 1000:0} kN, fuel {c.FuelLeft:0.00} l{(h >= 0 ? "" : " (no works)")}{(th.Reheat >= 0 ? $", reheat used {th.ReheatUsed:0.0} s{(th.ReheatSpent ? " (spent)" : "")}" : "")}");
                }
            if (t % World.TickRate == 0)
                for (var i = 0; i < c.PartCount; i++)
                    if (c.Parts[i].D.TrainFn == TrainFunction.Sensor)
                        Console.WriteLine($"  t {now,5:0.0} s: Sensor Ring outputs: level {c.Parts[i].Out:0}, buffer {c.Parts[i].Out1:0}, health {ConstructModules.Output(c, i, 2):0}, heat {ConstructModules.Output(c, i, 3):0}");
            c.Wake(); // (held still, it would sleep: a drive at work keeps stepping)
            w.Step(q, World.TickDt);
        }
        for (var k = 0; k < c.ThrusterCount; k++)
            if (c.Thrusters[k].Level > 0.01f && cut && !c.Thrusters[k].Fed && c.Thrusters[k].Buffer <= 0)
            {
                Console.WriteLine($"  FAIL {c.Parts[c.Thrusters[k].Part].D.Name} still burns with its pipe cut and its buffer spent");
                problems++;
            }
        return problems;
    }

    private static readonly PacketWriter _swingW = new();

    /// <summary>Each swung bell: its gimbal's angles, the push's way against the drawn bell's axis (ConstructRender.GimbalTurn), and what a client is told.</summary>
    private static void Swing(Construct c, ConstructWorld w, float now)
    {
        if (!ConstructThrust.WriteShown(_swingW, c))
            c.ThrustSentKey = 0; // (unchanged since the last: sent again below)
        if (c.ThrustSentKey == 0 && !ConstructThrust.WriteShown(_swingW, c))
            return;
        var r = new PacketReader(_swingW.Span);
        r.U8();
        r.U8();
        ConstructThrust.ReadShown(ref r, w);
        for (var k = 0; k < c.ThrusterCount; k++)
        {
            ref var th = ref c.Thrusters[k];
            if (!ConstructRender.GimbalTurn(c, k, true, out var turn))
                continue;
            var pushed = Vector3.Normalize(th.Dir + Vector3.Cross(th.GimbalX, th.Dir) * th.AngleX + Vector3.Cross(th.GimbalY, th.Dir) * th.AngleY);
            var drawn = Vector3.Normalize(Vector3.TransformNormal(th.Dir, turn));
            ConstructRender.GimbalTurn(c, k, false, out var heard);
            var client = Vector3.Normalize(Vector3.TransformNormal(th.Dir, heard));
            Console.WriteLine($"  t {now,5:0.0} s: {c.Parts[th.Part].D.Name}: gimbal {th.AngleX * 180f / MathF.PI:0.00}, {th.AngleY * 180f / MathF.PI:0.00} deg; drawn bell {MathF.Acos(Math.Clamp(Vector3.Dot(th.Dir, drawn), -1, 1)) * 180f / MathF.PI:0.00} deg off straight, {MathF.Acos(Math.Clamp(Vector3.Dot(pushed, drawn), -1, 1)) * 180f / MathF.PI:0.000} deg off the push, a client's {MathF.Acos(Math.Clamp(Vector3.Dot(pushed, client), -1, 1)) * 180f / MathF.PI:0.000} deg");
        }
    }
}
