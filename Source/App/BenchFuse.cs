using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// <c>--bench-fuse [PARTS] [--capital] [--stack] [--sheet]</c> (the sheet scenes run alone, or with --sheet beside the others): what fusing buys (docs/sandbox/CONSTRUCTS.md, "Fusing"; numbers in
/// docs/perf/LIMITS.md, "Fused units"). A command-line benchmark, not a self-test section: it fails nothing. Every scene is
/// built twice, once plain and once with the hull fused whole (the fuse tool's own tags), and measured the same way: the full
/// rebuild, the collider build, the rough fit, the physics step against the floor, rolling ground and another construct,
/// edits and breaks, memory and steady allocation. Prints min / median of repeats.
/// </summary>
public static partial class SelfTest
{
    public static int BenchFuse(string[] args)
    {
        var parts = Construct.MaxParts;
        var at = Array.FindIndex(args, a => a.Equals("--bench-fuse", StringComparison.OrdinalIgnoreCase));
        if (at >= 0 && at + 1 < args.Length && int.TryParse(args[at + 1], out var n))
            parts = Math.Clamp(n, 64, Construct.MaxParts);
        var block = ConstructParts.Need("Frame Block");
        var q = new JointTestWorld();
        Console.WriteLine($"[fuse bench] {parts} parts a hull (Frame Blocks in a 12-wide sheet linked on four sides, a Lamp on every 200th; the blocks fused), plain against fused; ms min / median");
        // Warm the paths.
        foreach (var fused in new[] { false, true })
        {
            var ww = new ConstructWorld();
            var wc = BenchHull(ww, new Vector3(0, -block.BoundsMin.Y + 0.02f, 0), 2048, block, fused);
            for (var t = 0; t < 30; t++)
                ww.Step(q, World.TickDt);
            ww.RemovePart(wc, 1000);
            ww.Step(q, World.TickDt);
        }
        var only = args.Any(a => a.Equals("--capital", StringComparison.OrdinalIgnoreCase) || a.Equals("--stack", StringComparison.OrdinalIgnoreCase));
        if (!only || args.Any(a => a.Equals("--sheet", StringComparison.OrdinalIgnoreCase)))
            foreach (var fused in new[] { false, true })
                BenchSheet(parts, block, q, fused);
        if (args.Any(a => a.Equals("--capital", StringComparison.OrdinalIgnoreCase)))
            foreach (var fused in new[] { false, true })
                BenchCapital(parts, q, fused);
        if (args.Any(a => a.Equals("--stack", StringComparison.OrdinalIgnoreCase)))
            foreach (var fused in new[] { false, true })
                BenchStack(parts, block, q, fused);
        return 0;
    }

    private static string Label(bool fused) => fused ? "fused" : "plain";

    /// <summary>A sheet hull (CostHull's), fused whole when asked, rebuilt.</summary>
    private static Construct BenchHull(ConstructWorld w, Vector3 at, int parts, ConstructPartDef block, bool fused)
    {
        var lamps = parts / 200;
        var c = CostHull(w, at, parts - lamps, block);
        var lamp = ConstructParts.Need("Lamp");
        var size = block.BoundsMax - block.BoundsMin;
        for (var k = 0; k < lamps; k++)
            w.AddPart(c, lamp, c.Parts[k * 200 % c.PartCount].Pos + new Vector3(0, size.Y, 0), 0); // (a few fittings on top, not fused: special parts)
        w.AutoLinkMany(c, 0, c.PartCount); // (linked on four sides, as a player's sheet is: CostHull's own isn't)
        c.Rebuild();
        if (fused)
            BenchFuseAll(c);
        return c;
    }

    private static void BenchFuseAll(Construct c)
    {
        if (c.Dirty)
            c.Rebuild();
        var (m0, c0, i0) = (c.Mass, c.Com, c.InvInertiaLocal);
        var tag = ConstructFuse.NewTag();
        for (var i = 0; i < c.PartCount; i++)
            if (c.DefOf(i).Name != "Lamp")
                c.Parts[i].Fuse = tag; // (the hull, as the fuse tool's selection would take it: the fittings stay out)
        c.Dirty = true;
        c.Rebuild();
        _ = c.Collider;
        Console.WriteLine($"[fuse bench] fused {c.PartCount} parts: mass {m0:0.###} -> {c.Mass:0.###} kg, centre of mass moved {Vector3.Distance(c0, c.Com) * 1000:0.000} mm, inverse inertia diagonal {i0.M11:E3} {i0.M22:E3} {i0.M33:E3} -> {c.InvInertiaLocal.M11:E3} {c.InvInertiaLocal.M22:E3} {c.InvInertiaLocal.M33:E3}");
    }

    private static (double Min, double Med) Times(int repeats, Action run, Action? before = null)
    {
        var t = new double[repeats];
        var sw = new Stopwatch();
        for (var k = 0; k < repeats; k++)
        {
            before?.Invoke();
            sw.Restart();
            run();
            t[k] = sw.Elapsed.TotalMilliseconds;
        }
        Array.Sort(t);
        return (t[0], t[repeats / 2]);
    }

    private static string Ms((double Min, double Med) t) => $"{t.Min:0.000} / {t.Med:0.000}";

    private static string Steps(ConstructWorld w, IWorldQuery q, int ticks, Action? each = null)
    {
        var t = new double[ticks];
        var sw = new Stopwatch();
        long bytes = 0;
        var contacts = 0;
        int builds = ConstructCollider.Builds, rebuilds = Construct.FullRuns + Construct.PlainRuns;
        for (var k = 0; k < ticks; k++)
        {
            each?.Invoke();
            var b0 = GC.GetAllocatedBytesForCurrentThread();
            sw.Restart();
            w.Step(q, World.TickDt);
            t[k] = sw.Elapsed.TotalMilliseconds;
            bytes += GC.GetAllocatedBytesForCurrentThread() - b0;
            contacts = Math.Max(contacts, ConstructPhysics.LastContacts);
        }
        Array.Sort(t);
        return $"{t[0]:0.000} / {t[ticks / 2]:0.000} ms a tick, up to {contacts} contacts, {bytes} B in {ticks} ticks ({Construct.FullRuns + Construct.PlainRuns - rebuilds} rebuilds, {ConstructCollider.Builds - builds} colliders built)";
    }

    private static void BenchSheet(int parts, ConstructPartDef block, IWorldQuery q, bool fused)
    {
        var name = Label(fused);
        var w = new ConstructWorld();
        var c = BenchHull(w, new Vector3(0, -block.BoundsMin.Y + 0.02f, 0), parts, block, fused);
        var fits = ConstructFuseFit.Fits;
        var fitMs = ConstructFuseFit.FitMs;
        if (fused)
        {
            c.FuseMassStale();
            c.Dirty = true;
            c.Rebuild();
            c.ColliderChanged();
            for (var u = 0; u < c.UnitCount; u++)
                c.Unit(u).NeedFit = true;
            _ = c.Collider;
            var unit = c.Unit(0);
            var fitTimes = Times(5, () => ConstructFuseFit.Fit(c, unit, c.UnitMembers(0)));
            Console.WriteLine($"[fuse bench] {name}: rough fit alone {Ms(fitTimes)}");
            Console.WriteLine($"[fuse bench] {name}: rough fit {(ConstructFuseFit.FitMs - fitMs) / Math.Max(1, ConstructFuseFit.Fits - fits):0.00} ms, {unit.RoughCount} boxes over {unit.Covered} parts, cell {unit.Cell * 100:0.0} cm, rough volume {unit.RoughVolume:0.0} m3 against {unit.ExactVolume:0.0} m3 of boxes");
        }
        Console.WriteLine($"[fuse bench] {name}: rebuild, full: {Ms(Times(7, () => c.Rebuild(), () => c.Dirty = true))}");
        Console.WriteLine($"[fuse bench] {name}: collider built: {Ms(Times(7, () => _ = c.Collider, c.ColliderChanged))}");
        var col = c.Collider;
        long bytes = (long)col.LeafCount * System.Runtime.CompilerServices.Unsafe.SizeOf<ConstructCollider.Leaf>() + (long)col.NodeCount * System.Runtime.CompilerServices.Unsafe.SizeOf<ConstructCollider.Node>();
        Console.WriteLine($"[fuse bench] {name}: collider {col.LeafCount} leaves, {col.NodeCount} nodes, {bytes / 1024} KB in use ({(long)col.Leaves.Length * System.Runtime.CompilerServices.Unsafe.SizeOf<ConstructCollider.Leaf>() / 1024} KB of leaf arrays)");
        Console.WriteLine($"[fuse bench] {name}: sliding on a floor: {Steps(w, q, 40, () => { c.Vel = new Vector3(3, c.Vel.Y, 0); c.Wake(); })}; its lowest box at {c.ToWorld(new Vector3(0, block.BoundsMin.Y, 0)).Y * 1000:0.0} mm, tilt {MathF.Acos(Math.Clamp(Vector3.Transform(Vector3.UnitY, c.Rot).Y, -1, 1)) * 180 / MathF.PI:0.00} deg");
        // Another construct: 40 blocks dropped on the hull (construct against construct).
        for (var i = 0; i < 40; i++)
            ConstructBuild.PlaceNew(w, block, c.ToWorld(new Vector3(0.6f + i % 8 * 0.4f, 0.6f, 4 + i / 8 * 1.5f)), 0);
        Console.WriteLine($"[fuse bench] {name}: 40 blocks on it, settling: {Steps(w, q, 40, () => { c.Vel = new Vector3(0, c.Vel.Y, 0); c.Wake(); })}");
        // Rolling ground of half-metre cells.
        var rolling = CostGround();
        var land = new ConstructWorld();
        var track = BenchHull(land, new Vector3(0, 1.6f, 0), parts, block, fused);
        Console.WriteLine($"[fuse bench] {name}: on half-metre ground: {Steps(land, rolling, 40, () => { track.Vel = new Vector3(3, track.Vel.Y, 0); track.Wake(); })}");
        // Edits and breaks, each with the tick that rebuilds it, less a steady tick measured just before.
        var rng = new Random(5);
        void Edit(string what, int repeats, Action edit)
        {
            var steady = Times(7, () => w.Step(q, World.TickDt), c.Wake);
            var t = Times(repeats, () =>
            {
                edit();
                w.Step(q, World.TickDt);
            }, c.Wake);
            Console.WriteLine($"[fuse bench] {name}: {what} (edit + step, less a steady tick of {steady.Min:0.000} / {steady.Med:0.000}): {t.Min - steady.Min:0.000} / {t.Med - steady.Med:0.000}");
        }
        Edit("a part placed beside it", 9, () => ConstructBuild.PlaceNew(w, block, c.ToWorld(new Vector3(0, 0.3f, -0.6f)), 0));
        Edit("take off an inner block", 9, () => w.RemovePart(c, c.PartCount / 2 + rng.Next(100)));
        Edit("ten blocks off in one tick", 5, () =>
        {
            for (var k = 0; k < 10; k++)
                w.RemovePart(c, c.PartCount / 3 + rng.Next(200));
        });
        Edit("four blocks shot off a tick", 5, () =>
        {
            for (var k = 0; k < 4; k++)
                w.DamagePart(c, Math.Min(c.PartCount - 1, 24 + rng.Next(c.PartCount - 48)), 1e6f, out _, raw: true);
        });
        var lamp = ConstructParts.Need("Lamp");
        Edit("a special part added (a full rebuild)", 5, () => w.AddPart(c, lamp, c.Parts[0].Pos + new Vector3(0, 0.3f, 0), 0));
        var steadyCut = Times(7, () => w.Step(q, World.TickDt), c.Wake).Min;
        // A split: the sheet cut in half (a row of blocks across it taken off).
        var before = w.Bodies.Count;
        var cut = Times(1, () =>
        {
            var row = c.PartCount / 2 / 12 * 12;
            Span<int> gone = stackalloc int[12];
            for (var k = 0; k < 12; k++)
                gone[k] = row + 11 - k;
            foreach (var i in gone)
                if (i < c.PartCount)
                    w.RemovePart(c, i);
            w.Step(q, World.TickDt);
        }, c.Wake);
        Console.WriteLine($"[fuse bench] {name}: cut in half (12 off, split, edit + step): {cut.Min - steadyCut:0.000} ms, bodies {before} -> {w.Bodies.Count}, units on the hull {c.UnitCount}");
        var b0 = GC.GetAllocatedBytesForCurrentThread();
        for (var t = 0; t < 30; t++)
        {
            c.Wake();
            w.Step(q, World.TickDt);
        }
        Console.WriteLine($"[fuse bench] {name}: steady allocation {GC.GetAllocatedBytesForCurrentThread() - b0} B in 30 ticks");
    }

    private static void BenchCapital(int parts, IWorldQuery q, bool fused)
    {
        var name = Label(fused);
        var w = new ConstructWorld();
        if (CapHull(w, new Vector3(0, 2f, 0), Math.Min(parts, Construct.MaxParts - 400), out var why) is not { } c)
        {
            Console.WriteLine($"[fuse bench] capital hull didn't build: {why}");
            return;
        }
        c.Rebuild();
        c.Pos = c.PrevPos = c.Pos + new Vector3(0, 0.5f - c.ToWorld(c.LocalMin).Y, 0); // (half a metre over the floor, all of it)
        if (fused)
        {
            var fits = ConstructFuseFit.Fits;
            var fitMs = ConstructFuseFit.FitMs;
            BenchFuseAll(c);
            var unit = c.Unit(0);
            Console.WriteLine($"[fuse bench] capital {name}: {c.PartCount} parts, rough fit {(ConstructFuseFit.FitMs - fitMs) / Math.Max(1, ConstructFuseFit.Fits - fits):0.00} ms, {unit.RoughCount} boxes over {unit.Covered} parts, cell {unit.Cell * 100:0.0} cm, rough {unit.RoughVolume:0} m3 against {unit.ExactVolume:0} m3");
        }
        Console.WriteLine($"[fuse bench] capital {name}: rebuild, full: {Ms(Times(5, () => c.Rebuild(), () => c.Dirty = true))}");
        Console.WriteLine($"[fuse bench] capital {name}: collider built: {Ms(Times(5, () => _ = c.Collider, c.ColliderChanged))}, {c.Collider.LeafCount} leaves");
        var path = "";
        for (var t = 0; t < 120; t++)
        {
            if (t % 20 == 0)
                path += $" {c.WorldCom.Y:0.00}";
            w.Step(q, World.TickDt);
        }
        Console.WriteLine($"[fuse bench] capital {name}: centre of mass height every 20 ticks from the start:{path}");
        Console.WriteLine($"[fuse bench] capital {name}: falling onto the floor, sliding: {Steps(w, q, 60, () => { c.Vel = new Vector3(2, c.Vel.Y, 0); c.Wake(); })}, centre of mass at {c.WorldCom.Y:0.000} m, its lowest bound at {c.ToWorld(c.LocalMin).Y:0.000} m, tilt {MathF.Acos(Math.Clamp(Vector3.Transform(Vector3.UnitY, c.Rot).Y, -1, 1)) * 180 / MathF.PI:0.00} deg");
    }

    private static void BenchStack(int parts, ConstructPartDef block, IWorldQuery q, bool fused)
    {
        var size = block.BoundsMax - block.BoundsMin;
        var many = new ConstructWorld();
        var hulls = new List<Construct>();
        for (var i = 0; i < 8; i++)
            hulls.Add(BenchHull(many, new Vector3(i * 0.2f, 0.3f + i * size.Y * 1.5f, i * 0.2f), parts, block, fused));
        Console.WriteLine($"[fuse bench] {Label(fused)}: eight hulls stacked: {Steps(many, q, 20, () =>
        {
            for (var i = 1; i < hulls.Count; i += 2)
            {
                hulls[i].Vel = new Vector3(1, hulls[i].Vel.Y, 0);
                hulls[i].Wake();
            }
        })}");
    }
}
