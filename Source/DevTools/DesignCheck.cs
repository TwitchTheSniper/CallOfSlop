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
/// <para>
/// One partial class, a file a part of the check: this one reads the design, builds, drops and round-trips it; DesignCheck.Walk.cs
/// (<c>--walk</c>: a player through its rooms and doorways), DesignCheck.Doors.cs (<c>--doors</c>), DesignCheck.Systems.cs (<c>--systems</c>)
/// and DesignCheck.Flight.cs (the flight check and the sortie from the helm, unless <c>--no-fly</c>) each take the built body in a
/// <see cref="Run"/> and add their failures to it. <c>--fingerprint</c> prints only what the elements and the body came to (the hashes below), for a before and after.
/// </para>
/// </summary>
public static partial class DesignCheck
{
    /// <summary>One check's run: its arguments, the built body and what it came to, and the failures so far.</summary>
    private sealed class Run(string[] args)
    {
        public readonly string[] Args = args;
        public int Fails;
        public string Hash = "", Text = "";
        public ConstructWorld World = null!;
        public Construct Body = null!;
        public IReadOnlyList<DesignParts.Element> Els = [];
        public string? Blueprint; // (a player blueprint of the body, or null when it couldn't be made)
        public Vector3 Ground;

        public void Fail(string why)
        {
            Fails++;
            Console.WriteLine("[design] FAIL " + why);
        }

        public bool Has(string flag) => Args.Any(a => a.Equals(flag, StringComparison.OrdinalIgnoreCase));
    }

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
        var run = new Run(args);
        void Fail(string why) => run.Fail(why);
        var at = Array.FindIndex(args, a => a.Equals("--design-check", StringComparison.OrdinalIgnoreCase));
        var what = at >= 0 && at + 1 < args.Length && !args[at + 1].StartsWith("--", StringComparison.Ordinal) ? args[at + 1] : "Zero Day";
        var si = Array.FindIndex(args, a => a.Equals("--seconds", StringComparison.OrdinalIgnoreCase));
        var seconds = si >= 0 && si + 1 < args.Length && float.TryParse(args[si + 1], System.Globalization.CultureInfo.InvariantCulture, out var sv) ? sv : 6f;
        string? json = File.Exists(what) ? File.ReadAllText(what) : ShipDesignStore.List().FirstOrDefault(x => x.Name.Equals(what, StringComparison.OrdinalIgnoreCase)).Json;
        json ??= DesignTemplates.Start(DesignTemplates.Starts.FirstOrDefault(n => n.Equals(what, StringComparison.OrdinalIgnoreCase)) ?? "Zero Day").ToJson(); // (a designer template by name: "Blank hull")
        var t0 = Stopwatch.GetTimestamp();
        if (DesignParts.Canon(json) is not { } text || DesignParts.Register(text) is not { } hash)
        {
            Fail($"not a design: {DesignParts.LastRefusal}");
            return run.Fails;
        }
        Console.WriteLine($"[design] {what}: {text.Length} bytes, hash {hash}, read in {Ms(t0):0} ms");
        var issues = DesignScene.Run(ShipDesign.FromJson(text)).Issues;
        Console.WriteLine($"[design] the designer's checks: {issues.Count(i => i.Level == "error")} errors, {issues.Count(i => i.Level == "warn")} warnings");
        foreach (var i in issues)
            Console.WriteLine($"[design]   {i.Level}: {i.Text}");
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
            return run.Fails;
        }
        if (DesignParts.Elements(hash) is not { } els)
        {
            Fail("its elements couldn't be made");
            return run.Fails;
        }
        Console.WriteLine($"[design] elements (made in {DesignParts.LastBuildMs:0} ms, its longest step {DesignParts.LastStepMs:0.0} ms): {DesignParts.LastSummary}");
        foreach (var g in els.GroupBy(e => e.Kind))
            Console.WriteLine($"[design]   {g.Key}: {g.Count()}, {g.Sum(e => e.Kg) / 1000:0.0} t, hulls {g.Sum(e => e.Hulls.Length)} (most corners {g.SelectMany(e => e.Hulls).Select(h => h.V.Length).DefaultIfEmpty(0).Max()}), triangles {g.Sum(e => e.Look.Vertices) / 3}");
        foreach (var e in els.Where(e => e.Kind is not ("floor" or "nozzle" or "flight") && e.Hulls.Length == 0))
            Console.WriteLine($"[design]   no collider: {e.Id} ({e.Name})");
        var low = ConstructBuild.LowOf(job.Scratch, scratch);
        var ground = ConstructWorld.Ground(World.Query, 0, 0);
        var t3 = Stopwatch.GetTimestamp();
        var c = job.Join(ground, 0, out var why);
        if (c == null)
        {
            Fail($"join refused: {why}");
            return run.Fails;
        }
        Console.WriteLine($"[design] fingerprint: {els.Count} elements, {els.Sum(e => (double)e.Kg) / 1000:0.000} t, elements {ElementsHash(els):x16}, body {BodyHash(c):x16}");
        if (run.Has("--fingerprint"))
            return run.Fails;
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
        // A player's blueprint of it (the selector's Save): its designs go in the file, and it builds again from that.
        var bpText = ConstructClip.Of(c, "design check").ToBlueprint("design check", "", out var bpWhy);
        if (bpText == null)
            Fail($"no player blueprint of it: {bpWhy}");
        else
        {
            using var doc = System.Text.Json.JsonDocument.Parse(bpText);
            var embedded = doc.RootElement.TryGetProperty("shipDesigns", out var sd) && sd.GetArrayLength() == 1 && DesignParts.Canon(sd[0].GetRawText()) is { } back ? DesignParts.HashOf(back) : "";
            if (embedded != hash)
                Fail("the player blueprint doesn't carry its design");
            var again = ConstructBuild.BuildOnGround(new ConstructWorld(), ConstructBuild.ParseBlueprint(bpText, "design check.json"), ground + new Vector3(200, 0, 0), 0, out var bw);
            if (again?.PartCount != c.PartCount)
                Fail($"the player blueprint built {again?.PartCount ?? 0} parts, not {c.PartCount}: {bw}");
            else
                Console.WriteLine($"[design] a player blueprint of it: {bpText.Length} bytes, its design in it, built again with {again.PartCount} parts");
        }
        (run.Hash, run.Text, run.World, run.Body, run.Els, run.Blueprint, run.Ground) = (hash, text, world, c, els, bpText, ground);
        if (run.Has("--walk"))
            Walk(run); // DesignCheck.Walk.cs
        if (run.Has("--doors"))
            Doors(run); // DesignCheck.Doors.cs
        if (run.Has("--systems"))
            Systems(run); // DesignCheck.Systems.cs
        Flight(run); // DesignCheck.Flight.cs
        Console.WriteLine(run.Fails == 0 ? "[design] check: passed" : $"[design] check: {run.Fails} failure(s)");
        return run.Fails;
    }

    /// <summary>A hash of everything a design's elements hold (ids, kinds, places, turns, mass, colliders, looks, welds, what they do): a change to how elements are made shows here.</summary>
    internal static ulong ElementsHash(IReadOnlyList<DesignParts.Element> els)
    {
        var h = new Fnv();
        foreach (var e in els)
        {
            h.S(e.Id).S(e.Kind).S(e.Name).S(e.Fn).S(e.Role).V(e.Origin).F(e.Area).F(e.Kg).V(e.BoxMin).V(e.BoxMax).U(e.Rot)
                .V(e.Exhaust).V(e.ThrustAt).V(e.SeatAt).F(e.Force).F(e.Mouth).U((uint)e.Links.Count);
            foreach (var l in e.Links)
                h.U((uint)l);
            foreach (var hl in e.Hulls)
            {
                h.U((uint)hl.V.Length);
                foreach (var v in hl.V)
                    h.V(v);
            }
            foreach (var f in e.Look.Pos)
                h.F(f);
            foreach (var f in e.Look.Normal)
                h.F(f);
            foreach (var u in e.Look.Colour)
                h.U(u);
        }
        return h.H;
    }

    /// <summary>A hash of a built body's parts as the game sees them (each def's name, role, mass, health, bounds, thrust and seat), its places and welds.</summary>
    internal static ulong BodyHash(Construct c)
    {
        var h = new Fnv();
        h.U((uint)c.PartCount).U((uint)c.LinkCount).U((uint)c.JointCount).U((uint)c.RouteCount);
        for (var i = 0; i < c.PartCount; i++)
        {
            var d = c.DefOf(i);
            h.S(d.Name).U((uint)d.Role).F(d.Mass).F(d.Health).V(d.BoundsMin).V(d.BoundsMax).F(d.ThrustForce).V(d.ThrustAt).V(d.SeatAt).U(d.Helm ? 1u : 0u)
                .V(c.Parts[i].Pos).U(c.Parts[i].Rot).U(c.Parts[i].Seg).U((uint)d.Hulls.Length).U((uint)d.Boxes.Length);
        }
        for (var k = 0; k < c.LinkCount; k++)
            h.U(c.Links[k].A).U(c.Links[k].B);
        return h.H;
    }

    private sealed class Fnv
    {
        public ulong H = 14695981039346656037ul;

        public Fnv U(uint v)
        {
            for (var k = 0; k < 4; k++, v >>= 8)
                H = (H ^ (v & 255)) * 1099511628211ul;
            return this;
        }

        public Fnv F(float f) => U(BitConverter.SingleToUInt32Bits(f));

        public Fnv V(Vector3 v) => F(v.X).F(v.Y).F(v.Z);

        public Fnv S(string s)
        {
            U((uint)s.Length);
            foreach (var ch in s)
                U(ch);
            return this;
        }
    }

    private static double Ms(long from) => (Stopwatch.GetTimestamp() - from) * 1000.0 / Stopwatch.Frequency;
}
