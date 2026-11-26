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
        json ??= DesignTemplates.Start(DesignTemplates.Starts.FirstOrDefault(n => n.Equals(what, StringComparison.OrdinalIgnoreCase)) ?? "Zero Day").ToJson(); // (a designer template by name: "Blank hull")
        var t0 = Stopwatch.GetTimestamp();
        if (DesignParts.Canon(json) is not { } text || DesignParts.Register(text) is not { } hash)
        {
            Fail($"not a design: {DesignParts.LastRefusal}");
            return fails;
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
            return fails;
        }
        if (DesignParts.Elements(hash) is not { } els)
        {
            Fail("its elements couldn't be made");
            return fails;
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
            return fails;
        }
        Console.WriteLine($"[design] fingerprint: {els.Count} elements, {els.Sum(e => (double)e.Kg) / 1000:0.000} t, elements {ElementsHash(els):x16}, body {BodyHash(c):x16}");
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
        // Flight (step 3): what its drives, nozzles and seats came to, then the aircraft flight check from its helm (climb, hover, forward, a Ship keys turn).
        var seats = Enumerable.Range(0, c.PartCount).Where(i => c.DefOf(i).Role == PartRole.Seat).ToList();
        Console.WriteLine($"[design] flight: {c.ThrusterCount} thrusters ({els.Count(e => e.Kind == "nozzle")} RCS nozzles), {seats.Count} seats ({seats.Count(i => c.DefOf(i).Helm)} helm), stabilizers {c.StabCount}, drive seat {c.DriveSeat}"
            + (c.Effectors is { } fx ? $", the stabilizer found {ConstructEffectors.Found(fx)}" : ""));
        if (bpText != null && !args.Any(a => a.Equals("--no-fly", StringComparison.OrdinalIgnoreCase)))
        {
            var t4 = Stopwatch.GetTimestamp();
            var fr = VehicleKit.VehicleFlightCheck.Run(bpText, "design check.json", gate: false);
            // (The aircraft check's limits are a small craft's: it is a note here, the sortie below is the test.)
            Console.WriteLine($"[design] aircraft flight check in {Ms(t4):0} ms: {(fr.Ok ? "flies" : "refused: " + fr.Why)}; {fr.Line}");
        }
        if (c.StabCount > 0 && c.DriveSeat >= 0 && !args.Any(a => a.Equals("--no-fly", StringComparison.OrdinalIgnoreCase)))
            if ((Sortie(world, c) is not null ? Sortie(world, c) : null) is { } bad) // (a second sortie when the first fails: the first touchdown runs the JIT on the contact paths)
                Fail("from its helm: " + bad);
        Console.WriteLine(fails == 0 ? "[design] check: passed" : $"[design] check: {fails} failure(s)");
        return fails;
    }

    /// <summary>
    /// A sortie from the helm on the proving ground, as a player flies it (keys held on the seat, as a seated player's are): up on Shift, a
    /// hover, ahead on W, a turn on D, down on Ctrl to the ground. Each leg's height, way, heading and speed, and what a world step cost.
    /// </summary>
    private static string? Sortie(ConstructWorld world, Construct c)
    {
        var seat = c.DriveSeat;
        c.Driver = 1;
        c.DriverSeat = seat;
        c.Parts[seat].SitterPlus1 = 2;
        float Heading() { var f = Vector3.Transform(-Vector3.UnitZ, c.Rot); return MathF.Atan2(f.X, -f.Z) * 180 / MathF.PI; }
        float Tilt() => MathF.Acos(Math.Clamp(Vector3.Transform(Vector3.UnitY, c.Rot).Y, -1, 1)) * 180 / MathF.PI;
        var ground = c.WorldCom.Y;
        double worst = 0, sum = 0;
        var worstAt = "";
        var slow = 0;
        var bytes0 = GC.GetAllocatedBytesForCurrentThread();
        var n = 0;
        float Leg(string name, Buttons keys, float seconds)
        {
            var p0 = c.WorldCom;
            var h0 = Heading();
            var tilt = 0f;
            for (var t = 0; t < seconds * World.TickRate; t++)
            {
                c.Parts[seat].Keys = (ushort)((ushort)keys | CPart.Occupied);
                ConstructEdits.DriveInput(c, keys, World.TickDt);
                var s = Stopwatch.GetTimestamp();
                world.Step(World.Query, World.TickDt);
                var ms = Ms(s);
                if (ms > 4)
                    slow++;
                if (n > 120 && ms > worst)
                    (worst, worstAt) = (ms, $"{name} {t / (float)World.TickRate:0.00} s");
                (sum, n) = (sum + ms, n + 1); // (the worst past its first two seconds: the JIT's first calls)
                tilt = MathF.Max(tilt, Tilt());
            }
            var d = c.WorldCom - p0;
            var turned = ((Heading() - h0) % 360 + 540) % 360 - 180;
            var fired = 0;
            for (var k = 0; k < c.ThrusterCount; k++)
                if (c.Thrusters[k].Level > 0.05f)
                    fired++;
            var line = $"[design] sortie {name,-8} {seconds:0} s: height {c.WorldCom.Y - ground:0.0} m (moved {d.Y:+0.0;-0.0} up, {MathF.Sqrt(d.X * d.X + d.Z * d.Z):0.0} along), turned {turned:+0;-0} deg, speed {c.Vel.Length():0.0} m/s, worst tilt {tilt:0} deg, {fired} of {c.ThrusterCount} thrusters lit, asleep {c.Asleep}";
            Console.WriteLine(line);
            return name == "turn" ? turned : name == "ahead" ? MathF.Sqrt(d.X * d.X + d.Z * d.Z) : c.WorldCom.Y - ground;
        }
        var up = Leg("lift", Buttons.Sprint, 6);
        var hover = Leg("hover", 0, 3);
        var ahead = Leg("ahead", Buttons.Forward, 5);
        Leg("stop", 0, 4);
        var turn = Leg("turn", Buttons.Right, 5);
        Leg("hold", 0, 3);
        Leg("land", Buttons.Crouch, 12);
        var rest = Leg("rest", 0, 3);
        Console.WriteLine($"[design] sortie: a world step with it flying: mean {sum / Math.Max(1, n):0.000} ms, worst {worst:0.00} ms after its first two seconds ({worstAt}), {slow} over 4 ms, {(GC.GetAllocatedBytesForCurrentThread() - bytes0) / Math.Max(1, n)} bytes allocated a step");
        c.Parts[seat].SitterPlus1 = 0;
        c.Driver = 255;
        c.DriverSeat = -1;
        return up < 10 ? $"Shift for 6 s lifted it {up:0.0} m (at least 10)" : hover < 0.5f * up ? $"let go, it sank to {hover:0.0} m" : ahead < 30 ? $"W for 5 s took it {ahead:0.0} m (at least 30)"
            : turn < 30 ? $"D for 5 s turned it {turn:0} degrees (at least 30)" : rest > 1 ? $"Ctrl for 12 s left it {rest:0.0} m up" : worst > 8 ? $"a step took {worst:0.0} ms (at most 8)" : null;
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
