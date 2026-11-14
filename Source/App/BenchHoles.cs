using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// <c>--bench-holes [--holes N] [--radius R] [--capital P] [--lines-per-tick K] [--seed S]</c>: the phase 0 spike of holes through ships
/// (docs/sandbox/design/hull-holes-spike.md, Source/Constructs/HullHoles.cs). A command-line benchmark, not a self-test section: it fails
/// nothing. A host and a client over loopback; the Rust Bucket and a capital hull of P parts (SelfTest.CapHull, 8,000 by default) held in
/// the air; seeded railgun lines (<see cref="HullHoles.Bore"/>) through each until about N holes (1,000) are made, K lines a tick. Each tick
/// is timed apart: the bore, the body's rebuild, the server's update (the structure going out), the client's (it reads it and rebuilds its
/// copy). Then what the client holds is checked against the host, part by part. Runs twice (the first warms the JIT: read the last).
/// </summary>
public static partial class SelfTest
{
    public static int BenchHoles(string[] args)
    {
        var code = 0;
        for (var run = 1; run <= 2 && code == 0; run++)
        {
            Console.WriteLine($"[holes bench] run {run} of 2{(run == 1 ? " (warms the JIT: read the last)" : "")}");
            code = BenchHolesOnce(args, (ushort)(26544 + run), run);
        }
        return code;
    }

    private sealed class HoleTally
    {
        public readonly List<double> Bore = new(), Rebuild = new(), Collider = new(), Server = new(), ServerCpu = new(), Client = new();
        public readonly double[] Areas = new double[Perf.Count];
        public int Lines, Parts, Holed, Refused, Skipped, Whole;

        /// <summary>Server: what the tick just ended spent in each area (F9's).</summary>
        public void TakeAreas()
        {
            Perf.EndFrame();
            for (var a = 0; a < Perf.Count; a++)
                Areas[a] += Perf.AreaMs(0, a);
        }

        public string TopAreas(int ticks)
        {
            var top = Enumerable.Range(0, Perf.Count).Where(a => Perf.Depth[a] >= 2).OrderByDescending(a => Areas[a]).Take(5);
            return string.Join(", ", top.Select(a => $"{Perf.Names[a]} {Areas[a] / Math.Max(1, ticks):0.00}"));
        }
        public long Bytes;

        public static string Of(List<double> v)
        {
            if (v.Count == 0)
                return "-";
            var s = v.OrderBy(x => x).ToList();
            return $"median {s[s.Count / 2]:0.00}, p95 {s[(int)(s.Count * 0.95)]:0.00}, worst {s[^1]:0.00} ms";
        }
    }

    /// <summary>
    /// One railgun line through <paramref name="c"/> at random (the bench's and the GL bench's): through a point inside it, mostly across
    /// its long side, some along it and some up and down.
    /// </summary>
    internal static HullHoles.BoreReport BoreAtRandom(ConstructWorld w, Construct c, Random rng, float radius)
    {
        var size = c.LocalMax - c.LocalMin;
        var mid = (c.LocalMax + c.LocalMin) * 0.5f;
        var reach = size.Length() * 1.5f;
        var p = mid + new Vector3(((float)rng.NextDouble() - 0.5f) * size.X * 0.9f, ((float)rng.NextDouble() - 0.5f) * size.Y * 0.9f, ((float)rng.NextDouble() - 0.5f) * size.Z * 0.9f);
        var d = Vector3.Normalize(new Vector3(((float)rng.NextDouble() - 0.5f) * 0.6f, ((float)rng.NextDouble() - 0.5f) * 0.6f, rng.Next(2) == 0 ? 1f : -1f));
        if (size.X > size.Z)
            d = new Vector3(d.Z, d.Y, d.X); // (across the long side)
        var wd = Vector3.Transform(d, c.Rot);
        return HullHoles.Bore(w, c, c.ToWorld(p) - wd * reach * 0.5f, wd, radius, reach);
    }

    private static int BenchHolesOnce(string[] args, ushort port, int run)
    {
        string? Arg(string name) => args.SkipWhile(a => !a.Equals(name, StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault();
        var want = int.TryParse(Arg("--holes"), out var nh) ? nh : 1000;
        var radius = float.TryParse(Arg("--radius"), System.Globalization.CultureInfo.InvariantCulture, out var nr) ? nr : 0.15f;
        var capParts = int.TryParse(Arg("--capital"), out var np) ? np : 8000;
        var perTick = int.TryParse(Arg("--lines-per-tick"), out var nk) ? Math.Max(1, nk) : 1;
        var rng = new Random((int.TryParse(Arg("--seed"), out var ns) ? ns : 7) + run); // (a new seed a run: the second makes its own parts, not the first's)
        var map = Maps.Find(Arg("--map") ?? "Proving Ground") ?? Maps.All.First(m => m.Terrain != null);
        var clock = Stopwatch.StartNew();
        var now = 0.0;
        map.RestoreProps();
        map.Terrain?.Edits.Clear();
        World.SetMap(map);
        using var server = new Game(NetPeer.Host(port, 4), new Audio(), "Host", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: (byte)Array.IndexOf(Maps.All, map)) { MapVoteEnabled = false };
        server.Mode = GameMode.Sandbox;
        using var client = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Client", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        Pump(server, [client], 1.0, ref now, clock);
        for (var wait = 0; wait < 40 && server.Phase != MatchPhase.Playing; wait++)
            PumpFast(server, [client], 0.25, ref now);
        var w = server.Constructs;
        var at = map.Spawns.Count > 0 ? map.Spawns[0] : Vector3.Zero;
        at.Y = (map.Terrain?.Sample(at.X, at.Z) ?? 0) + 120f;
        foreach (var p in server.Players.Values)
            if (p.Alive)
                p.Teleport(at + new Vector3(0, -100f, 260f));

        var ships = new List<(string Name, Construct Body)>();
        if (ConstructBuild.FindBlueprint("Rust Bucket") is { } bp && ConstructBuild.Build(w, bp, at + new Vector3(-60, 0, 0), Quaternion.Identity, out _) is { } rust)
            ships.Add(("Rust Bucket", rust));
        else
            Console.WriteLine("[holes bench] the Rust Bucket didn't build");
        string? whyCap = null;
        if (capParts > 0 && CapHull(w, at + new Vector3(40, 0, 0), Math.Min(capParts, Construct.MaxParts - 400), out whyCap) is { } cap)
            ships.Add(($"capital hull ({cap.PartCount} parts)", cap));
        else if (capParts > 0)
            Console.WriteLine($"[holes bench] the capital hull didn't build: {whyCap}");
        foreach (var (_, c) in ships)
        {
            c.Frozen = c.Anchored = true;
            c.Rebuild();
        }
        Pump(server, [client], 3.0, ref now, clock);
        foreach (var (name, c) in ships)
        {
            var takes = 0;
            for (var i = 0; i < c.PartCount; i++)
                takes += HullHoles.Refusal(c.DefOf(i)) == null ? 1 : 0;
            Console.WriteLine($"[holes bench] {name}: {c.PartCount} parts ({takes} can take holes), {(c.LocalMax - c.LocalMin).Length():0} m across; client has it: {client.Constructs.Get(c.Id) != null}; the journal would take a swap of part 0: {c.SwapRefusal(0, c.Parts[0].Def) ?? "yes"}, of the last: {c.SwapRefusal(c.PartCount - 1, c.Parts[c.PartCount - 1].Def) ?? "yes"}");
        }
        if (ships.Count == 0)
            return 1;

        const double dt = 1.0 / World.TickRate;
        Perf.SetOn(true);
        Perf.EndFrame();
        for (var awake = 0; awake < 2; awake++)
        {
            // The same ticks with no lines: what the server and the client cost holding these ships still, asleep, then woken each tick.
            var idle = new HoleTally();
            for (var i = 0; i < 2 * World.TickRate; i++)
            {
                if (awake == 1)
                    foreach (var (_, c) in ships)
                        c.Wake();
                var sw = Stopwatch.StartNew();
                var c0 = ThreadCpu.NowMs();
                var sent0 = server.Net.BytesSent;
                now += dt;
                server.Update(now, dt);
                idle.Bytes += server.Net.BytesSent - sent0;
                idle.ServerCpu.Add(ThreadCpu.NowMs() - c0);
                idle.Server.Add(sw.Elapsed.TotalMilliseconds);
                idle.TakeAreas();
                sw.Restart();
                client.Update(now, dt);
                Perf.EndFrame();
                idle.Client.Add(sw.Elapsed.TotalMilliseconds);
            }
            Console.WriteLine($"[holes bench] idle{(awake == 1 ? ", woken" : "")}: server update {HoleTally.Of(idle.Server)} (cpu {HoleTally.Of(idle.ServerCpu)}); client update {HoleTally.Of(idle.Client)}; {idle.Bytes / Math.Max(1, idle.Server.Count)} B sent a tick");
            Console.WriteLine($"[holes bench]   server areas a tick: {idle.TopAreas(idle.Server.Count)} ms");
        }
        var defs0 = HullHoles.DefsMade;
        var refused0 = HullHoles.Refused;
        var shapes0 = PartShapes.Version;
        var build0 = HullHoles.BuildMs;
        var why = new Dictionary<string, int>();
        // Holes go to each ship by its share of parts that can take them, the Rust Bucket at least a tenth.
        var plain0 = Construct.PlainRuns;
        var full0 = Construct.FullRuns;
        var checks0 = Construct.CheckRuns;
        var fails0 = Construct.CheckFails;
        var unplaced0 = HullHoles.Unplaced;
        var holeBytes0 = HullHoles.MessageBytes;
        foreach (var (name, c) in ships)
        {
            var t = new HoleTally();
            var goal = ships.Count == 1 ? want : name == "Rust Bucket" ? want / 10 : want - want / 10;
            var dry = 0;
            while (t.Holed < goal && dry < 400)
            {
                var sent0 = server.Net.BytesSent;
                var bore = 0.0;
                var made = 0;
                for (var k = 0; k < perTick; k++)
                {
                    var r = BoreAtRandom(w, c, rng, radius);
                    bore += r.Ms;
                    t.Lines++;
                    t.Parts += r.Parts;
                    t.Holed += r.Holed;
                    t.Refused += r.Refused;
                    t.Skipped += r.Skipped;
                    t.Whole += r.Whole ? 1 : 0;
                    made += r.Holed;
                    if (r.Refused > 0)
                        why[HullHoles.LastRefusal] = why.GetValueOrDefault(HullHoles.LastRefusal) + r.Refused;
                }
                dry = made > 0 ? 0 : dry + 1;
                t.Bore.Add(bore);
                var sw = Stopwatch.StartNew();
                if (c.Dirty)
                    c.Rebuild();
                t.Rebuild.Add(sw.Elapsed.TotalMilliseconds);
                sw.Restart();
                _ = c.Collider; // (what the physics step would make first: the compound collider)
                t.Collider.Add(sw.Elapsed.TotalMilliseconds);
                sw.Restart();
                var c0 = ThreadCpu.NowMs();
                now += dt;
                server.Update(now, dt);
                t.ServerCpu.Add(ThreadCpu.NowMs() - c0);
                t.Server.Add(sw.Elapsed.TotalMilliseconds);
                t.Bytes += server.Net.BytesSent - sent0;
                t.TakeAreas();
                sw.Restart();
                client.Update(now, dt);
                Perf.EndFrame();
                t.Client.Add(sw.Elapsed.TotalMilliseconds);
            }
            Console.WriteLine($"[holes bench] {name}: {t.Lines} lines, {t.Holed} holes made in {t.Parts} parts crossed ({t.Refused} refused, {t.Skipped} passed), {t.Whole} lines rebuilt the body whole, {t.Bytes / 1024} KB sent ({(t.Lines > 0 ? t.Bytes / t.Lines : 0)} B a line)");
            Console.WriteLine($"[holes bench]   bore {HoleTally.Of(t.Bore)}; rebuild {HoleTally.Of(t.Rebuild)}; collider {HoleTally.Of(t.Collider)}");
            Console.WriteLine($"[holes bench]   server update {HoleTally.Of(t.Server)} (cpu {HoleTally.Of(t.ServerCpu)}); client update {HoleTally.Of(t.Client)}");
            Console.WriteLine($"[holes bench]   server areas a tick: {t.TopAreas(t.Server.Count)} ms");
        }
        Perf.SetOn(false);
        Pump(server, [client], 3.0, ref now, clock);
        Console.WriteLine($"[holes bench] {HullHoles.DefsMade - defs0} holed parts made ({HullHoles.BuildMs - build0:0} ms building them, {HullHoles.Refused - refused0} refused), shape table moved {PartShapes.Version - shapes0}; {HullHoles.FacetsCut} facets and {HullHoles.BoxesCut} boxes cut, {HullHoles.PiecesDropped} pieces dropped");
        Console.WriteLine($"[holes bench] rebuilds (host and client): {Construct.PlainRuns - plain0} by the journal, {Construct.FullRuns - full0} whole; {HullHoles.Unplaced - unplaced0} holes the client couldn't place; hole messages {(HullHoles.MessageBytes - holeBytes0) / 1024} KB{(Construct.RebuildCheck ? $"; journal checked against a full rebuild {Construct.CheckRuns - checks0} times, {Construct.CheckFails - fails0} differed" : "")}");
        if (Construct.RebuildCheck)
            foreach (var line in Construct.CheckLog.Take(10))
                Console.WriteLine($"[holes bench]   check: {line}");
        foreach (var (k, v) in why.OrderByDescending(x => x.Value))
            Console.WriteLine($"[holes bench]   refused {v}: {k}");
        var bad = 0;
        foreach (var (name, c) in ships)
        {
            var holed = 0;
            var boxes = 0;
            var mostBoxes = 0;
            for (var i = 0; i < c.PartCount; i++)
            {
                var d = c.DefOf(i);
                if (!HullHoles.IsHoled(d))
                    continue;
                holed++;
                boxes += d.Boxes.Length;
                mostBoxes = Math.Max(mostBoxes, d.Boxes.Length);
            }
            var cc = client.Constructs.Get(c.Id);
            var differ = 0;
            for (var i = 0; cc != null && i < c.PartCount; i++)
                differ += i < cc.PartCount && cc.DefOf(i).Name == c.DefOf(i).Name ? 0 : 1;
            if (cc == null || cc.PartCount != c.PartCount || differ > 0)
                bad++;
            Console.WriteLine($"[holes bench] {name}: {holed} parts holed, {boxes} boxes in them (most {mostBoxes}); client: {(cc == null ? "no body" : $"{cc.PartCount} parts, {differ} differ")}");
        }
        Console.WriteLine($"[holes bench] {(bad == 0 ? "the client matches the host" : $"{bad} bodies differ on the client")}; {clock.Elapsed.TotalSeconds:0} s");
        return 0;
    }
}
