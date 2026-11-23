using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// --prepare-check NAME: where a held spawn's "Preparing" time goes, headless. The blueprint's steps run as the spawner's
/// GroundJob runs them (one scratch world, no slice limit), timed per part kind, then the ghost's measure and the join.
/// </summary>
public static class PrepareCheck
{
    public static int Cli(string[] args)
    {
        var at = Array.FindIndex(args, a => a.Equals("--prepare-check", StringComparison.OrdinalIgnoreCase));
        var name = at >= 0 && at + 1 < args.Length && !args[at + 1].StartsWith("--") ? args[at + 1] : "Zero Day Works";
        if (name.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            // Every shipped blueprint built as a held spawn builds it: its time and what it came to, to compare two builds of the game.
            var total = 0.0;
            foreach (var each in ConstructBuild.Blueprints)
            {
                if (each.Uses.Count > 0 && each.Map == null)
                    continue;
                var t = Stopwatch.GetTimestamp();
                var j = new ConstructBuild.GroundJob(new ConstructWorld(), each);
                while (!j.Step(1000))
                {
                }
                var ms = Ms(t, Stopwatch.GetTimestamp());
                total += ms;
                Console.WriteLine(j.Body is { } b ? $"[prepare] {each.Name}: {b.PartCount} parts, {b.LinkCount} links, hash {Hash(b):x16}, {ms:0} ms" : $"[prepare] {each.Name}: refused: {j.Why}");
            }
            Console.WriteLine($"[prepare] all: {total:0} ms of steps");
            return 0;
        }
        var bp = ConstructBuild.FindBlueprint(name);
        if (bp == null)
        {
            Console.WriteLine($"[prepare] {name}: no such blueprint");
            return 1;
        }
        var top = 0;
        for (var round = 0; round < 5; round++)
        {
            var world = new ConstructWorld();
            var t0 = Stopwatch.GetTimestamp();
            var job = new ConstructBuild.GroundJob(world, bp);
            var byDef = new Dictionary<string, (double Ms, int N)>();
            var steps = 0;
            var last = Stopwatch.GetTimestamp();
            while (!job.Step(0))
            {
                var now = Stopwatch.GetTimestamp();
                var k = Math.Min(steps, bp.Steps.Count - 1);
                var key = bp.Steps[k].Part;
                var e = byDef.GetValueOrDefault(key);
                byDef[key] = (e.Ms + Ms(last, now), e.N + 1);
                last = now;
                steps++;
            }
            var tSteps = Ms(t0, Stopwatch.GetTimestamp());
            if (job.Body is not { } body)
            {
                Console.WriteLine($"[prepare] {bp.Name}: refused: {job.Why}");
                return 1;
            }
            var t1 = Stopwatch.GetTimestamp();
            var low = ConstructBuild.LowOf(job.Scratch, body);
            var tLow = Ms(t1, Stopwatch.GetTimestamp());
            var t2 = Stopwatch.GetTimestamp();
            var c = job.Join(Vector3.Zero, 0, out var why);
            var tJoin = Ms(t2, Stopwatch.GetTimestamp());
            Console.WriteLine($"[prepare] round {round}: {bp.Name}: {bp.Steps.Count} steps in {tSteps:0} ms ({job.Slices} slices; at the spawner's {VehicleSpawner.SliceMs} ms a tick that is {tSteps / VehicleSpawner.SliceMs / 60:0.0} s), measure {tLow:0} ms, join {tJoin:0} ms{(c == null ? $" (refused: {why})" : "")}");
            if (c != null)
                Console.WriteLine($"[prepare] body: {c.PartCount} parts, {c.LinkCount} links, {c.WireCount} wires, {c.JointCount} joints, hash {Hash(c):x16}");
            if (c != null && round == 0 && Array.FindIndex(args, a => a == "--dump") is var di and >= 0 && di + 1 < args.Length)
                File.WriteAllLines(args[di + 1], Enumerable.Range(0, c.PartCount).Select(i => $"{i} {c.Parts[i].D.Name} {c.Parts[i].Pos} {c.Parts[i].Rot} {c.Parts[i].Seg}")
                    .Concat(Enumerable.Range(0, c.LinkCount).Select(k => $"link {c.Links[k].A} {c.Links[k].SA} {c.Links[k].B} {c.Links[k].SB}")));
            if (round == 4)
                foreach (var (k, v) in byDef.OrderByDescending(x => x.Value.Ms).Take(25))
                    Console.WriteLine($"[prepare]   {v.Ms,8:0.0} ms  {v.N,5} x  {v.Ms / v.N,7:0.000} ms  {k}");
            top++;
        }
        return 0;
    }

    /// <summary>What was built, as one number: every part's kind, place, turn and segment, every link and wire (two builds alike or not).</summary>
    private static ulong Hash(Construct c)
    {
        var h = 14695981039346656037UL;
        void Mix(long v) => h = (h ^ (ulong)v) * 1099511628211UL;
        for (var i = 0; i < c.PartCount; i++)
        {
            ref var p = ref c.Parts[i];
            foreach (var ch in p.D.Name)
                Mix(ch);
            Mix(BitConverter.SingleToInt32Bits(p.Pos.X));
            Mix(BitConverter.SingleToInt32Bits(p.Pos.Y));
            Mix(BitConverter.SingleToInt32Bits(p.Pos.Z));
            Mix(p.Rot);
            Mix(p.Seg);
        }
        for (var k = 0; k < c.LinkCount; k++)
            Mix(c.Links[k].A | (long)c.Links[k].B << 16 | (long)c.Links[k].SA << 32 | (long)c.Links[k].SB << 40);
        for (var k = 0; k < c.WireCount; k++)
            Mix(c.Wires[k].From | (long)c.Wires[k].To << 16 | (long)c.Wires[k].FromPort << 32 | (long)c.Wires[k].ToPort << 40);
        return h;
    }

    private static double Ms(long a, long b) => (b - a) * 1000.0 / Stopwatch.Frequency;
}
