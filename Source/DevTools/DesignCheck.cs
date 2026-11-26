using System.Diagnostics;
using System.Numerics;
using CallOfSlop.ShipDesigner;

namespace CallOfSlop;

/// <summary>
/// <c>--design-check [NAME|FILE] [--seconds S] [--write-shipped]</c>: a ship design as a body, headless (DesignParts.cs). The design (a shipped
/// one by name, a file, or the Zero Day template) is made into its elements and built as the held spawn builds it, set down on the ground and
/// stepped for S seconds (default 6): what it came to, how long each stage took, where it settled and what a world step of it costs; then its
/// design through the wire and a save. <c>--write-shipped</c> writes the designer's templates into Constructs/Designs (the shipped designs).
/// Exit code: the failures.
/// </summary>
public static class DesignCheck
{
    public static int Cli(string[] args)
    {
        if (args.Any(a => a.Equals("--write-shipped", StringComparison.OrdinalIgnoreCase)))
        {
            var dir = Path.Combine(Directory.GetCurrentDirectory(), "Constructs", "Designs");
            Directory.CreateDirectory(dir);
            var d = DesignTemplates.Start("Zero Day");
            d.Name = "Zero Day";
            File.WriteAllText(Path.Combine(dir, "Zero Day.json"), d.ToJson() + "\n");
            Console.WriteLine($"[design] wrote {Path.Combine(dir, "Zero Day.json")}");
            return 0;
        }
        var fails = 0;
        void Fail(string why)
        {
            fails++;
            Console.WriteLine("[design] FAIL " + why);
        }
        var at = Array.FindIndex(args, a => a.Equals("--design-check", StringComparison.OrdinalIgnoreCase));
        var what = at >= 0 && at + 1 < args.Length && !args[at + 1].StartsWith("--", StringComparison.Ordinal) ? args[at + 1] : "Zero Day";
        var si = Array.FindIndex(args, a => a.Equals("--seconds", StringComparison.OrdinalIgnoreCase));
        var seconds = si >= 0 && si + 1 < args.Length && float.TryParse(args[si + 1], System.Globalization.CultureInfo.InvariantCulture, out var sv) ? sv : 6f;
        string? json = File.Exists(what) ? File.ReadAllText(what) : ShipDesignStore.List().FirstOrDefault(x => x.Name.Equals(what, StringComparison.OrdinalIgnoreCase)).Json;
        json ??= DesignTemplates.Start("Zero Day").ToJson();
        var t0 = Stopwatch.GetTimestamp();
        if (DesignParts.Canon(json) is not { } text || DesignParts.Register(text) is not { } hash)
        {
            Fail($"not a design: {DesignParts.LastRefusal}");
            return fails;
        }
        Console.WriteLine($"[design] {what}: {text.Length} bytes, hash {hash}, read in {Ms(t0):0} ms");
        var t1 = Stopwatch.GetTimestamp();
        if (DesignParts.Elements(hash) is not { } els)
        {
            Fail("its elements couldn't be made");
            return fails;
        }
        Console.WriteLine($"[design] elements in {Ms(t1):0} ms: {DesignParts.LastSummary}");
        foreach (var g in els.GroupBy(e => e.Kind))
            Console.WriteLine($"[design]   {g.Key}: {g.Count()}, {g.Sum(e => e.Kg) / 1000:0.0} t, hulls {g.Sum(e => e.Hulls.Length)} (most corners {g.SelectMany(e => e.Hulls).Select(h => h.V.Length).DefaultIfEmpty(0).Max()}), triangles {g.Sum(e => e.Look.Vertices) / 3}");
        foreach (var e in els.Where(e => e.Kind != "floor" && e.Hulls.Length == 0))
            Console.WriteLine($"[design]   no collider: {e.Id} ({e.Name})");
        // Built as the held spawn builds it, then joined on the ground of the proving ground (flat and wide: the bare map's ground ends 20 m out).
        if (Maps.Find("Proving Ground") is { } yard)
        {
            yard.RestoreProps();
            World.SetMap(yard);
        }
        var world = new ConstructWorld();
        var bp = DesignParts.BlueprintOf(hash, what);
        var t2 = Stopwatch.GetTimestamp();
        var job = new ConstructBuild.GroundJob(world, bp);
        while (!job.Step(VehicleSpawner.SliceMs))
        {
        }
        Console.WriteLine($"[design] built in {Ms(t2):0} ms over {job.Slices} slices (longest {job.WorstSliceMs:0.0} ms)");
        if (job.Body is not { } scratch)
        {
            Fail($"refused: {job.Why}");
            return fails;
        }
        var low = ConstructBuild.LowOf(job.Scratch, scratch);
        var ground = ConstructWorld.Ground(World.Query, 0, 0);
        var t3 = Stopwatch.GetTimestamp();
        var c = job.Join(ground, 0, out var why);
        if (c == null)
        {
            Fail($"join refused: {why}");
            return fails;
        }
        Console.WriteLine($"[design] joined in {Ms(t3):0} ms: construct {c.Id}, {c.PartCount} parts, {c.LinkCount} welds, {c.Mass / 1000:0.0} t, bounds {c.LocalMin} .. {c.LocalMax}, low {low:0.00}, ground {ground.Y:0.00}");
        var steps = (int)(seconds * World.TickRate);
        double worst = 0, sum = 0, last = 0;
        var startY = c.WorldCom.Y;
        for (var k = 0; k < steps; k++)
        {
            var s = Stopwatch.GetTimestamp();
            world.Step(World.Query, World.TickDt);
            var ms = Ms(s);
            if (k > 10)
                (worst, sum) = (Math.Max(worst, ms), sum + ms);
            if (k >= steps - 60)
                last += ms;
        }
        var lowest = float.MaxValue;
        foreach (var b in world.Bodies)
            lowest = MathF.Min(lowest, b.Pos.Y + Vector3.Transform(b.LocalMin, b.Rot).Y);
        Console.WriteLine($"[design] {seconds:0} s stepped: {world.Bodies.Count} bodies; centre of mass {startY:0.00} -> {c.WorldCom.Y:0.00} m, speed {c.Vel.Length():0.000} m/s, asleep {c.Asleep}; step mean {sum / Math.Max(1, steps - 11):0.000} ms, worst {worst:0.00} ms, last second's mean {last / 60:0.000} ms");
        if (world.Bodies.Count != 1)
            Fail($"{world.Bodies.Count} bodies after the drop (it came apart)");
        if (c.WorldCom.Y < ground.Y)
            Fail("it fell through the ground");
        // The design through the wire and a save.
        var e0 = DesignParts.Find(hash)!;
        var pw = new PacketWriter(text.Length + 64);
        DesignParts.Write(pw, e0);
        var r = new PacketReader(pw.Span[2..]);
        var other = new ConstructWorld();
        DesignParts.Read(ref r, other);
        if (other.ShipDesigns.Count != 1 || other.ShipDesigns[0] != hash)
            Fail("the wire's copy doesn't match");
        DesignParts.NoteInUse(world);
        var sw = new SaveWriter(text.Length + 64);
        DesignParts.WriteSave(sw, DesignParts.TextsOf(world));
        var third = new ConstructWorld();
        DesignParts.ReadSave(new SaveReader(sw.Span.ToArray()), third);
        if (third.ShipDesigns.Count != 1 || third.ShipDesigns[0] != hash)
            Fail("the save's copy doesn't match");
        Console.WriteLine(fails == 0 ? "[design] check: passed" : $"[design] check: {fails} failure(s)");
        return fails;
    }

    private static double Ms(long from) => (Stopwatch.GetTimestamp() - from) * 1000.0 / Stopwatch.Frequency;
}
