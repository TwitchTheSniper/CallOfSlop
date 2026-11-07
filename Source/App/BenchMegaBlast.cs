using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// <c>--bench-mega [KEY] [--bodies N] [--parts P] [--map NAME] [--cap M] [--no-crater] [--once]</c>: what a massive blast costs (docs/perf/LIMITS.md, "Massive blasts").
/// A command-line benchmark, not a self-test section: it fails nothing. A host and one client over the real loopback transport on a Sandbox map
/// with terrain; N hulls of P Frame Blocks (300 of 60 by default) laid out round the middle, the two players at a few hundred metres; then the
/// design (by default the biggest the engine allows: an antimatter bomb past the 2 km cap) is set off in the middle. Every server tick of the
/// blast's life is stepped by hand and timed (and the client's update beside it): the worst, the median, the ticks over 8 ms, bytes allocated
/// and bytes sent, then what it did (parts walked, bodies left, crater bands, ops the client applied). Runs twice (the first warms the JIT; --once: one). Prints one line a phase.
/// </summary>
public static partial class SelfTest
{
    public static int BenchMega(string[] args)
    {
        var runs = args.Any(a => a.Equals("--once", StringComparison.OrdinalIgnoreCase)) ? 1 : 2;
        var code = 0;
        for (var run = 1; run <= runs && code == 0; run++)
        {
            Console.WriteLine($"[mega bench] run {run} of {runs}{(run == 1 && runs > 1 ? " (warms the JIT: read the last)" : "")}");
            code = BenchMegaOnce(args);
        }
        return code;
    }

    private static int BenchMegaOnce(string[] args)
    {
        string? Arg(string name) => args.SkipWhile(a => !a.Equals(name, StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault();
        var key = Arg("--bench-mega") is { } k && k.StartsWith(Ordnance.Prefix, StringComparison.OrdinalIgnoreCase) ? k
            : "Ordnance:B200:Impact Fuse+Antimatter Warhead@length=5.4+Bomb Tail Fins";
        var bodies = int.TryParse(Arg("--bodies"), out var nb) ? Math.Clamp(nb, 0, 5000) : 300;
        var parts = int.TryParse(Arg("--parts"), out var np) ? Math.Clamp(np, 1, 2000) : 60;
        var cap = int.TryParse(Arg("--cap"), out var nc) ? nc : 0;
        var map = Maps.Find(Arg("--map") ?? "Streamed Terrain") ?? Maps.All.First(m => m.Terrain != null);
        if (Ordnance.Parse(key) is not { Explosive: { } ex } def)
        {
            Console.WriteLine($"[mega bench] \"{key}\" isn't a design that adds up ({OrdnanceDesign.Parse(key, out var why)?.Stats().Problem ?? why})");
            return 1;
        }
        var t = map.Terrain;
        Console.WriteLine($"[mega bench] {key}: {def.OrdStats!.Blast:0} m blast asked ({ex.ExplodeRadius:0} m), {ex.ExplodeDamage:0} damage, {ex.ExplodeCrater:0} m crater, {def.Mass / 1000f:0.#} t");
        Console.WriteLine($"[mega bench] {map.Name}: terrain {(t == null ? "none" : $"{t.CountX * t.Cell:0} x {t.CountZ * t.Cell:0} m")}; {bodies} hulls of {parts} Frame Blocks; a host and a client over loopback");
        const ushort port = 26533;
        var clock = Stopwatch.StartNew();
        var now = 0.0;
        map.RestoreProps();
        t?.Edits.Clear();
        World.SetMap(map);
        using var server = new Game(NetPeer.Host(port, 4), new Audio(), "Host", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: (byte)Array.IndexOf(Maps.All, map)) { MapVoteEnabled = false };
        server.Mode = GameMode.Sandbox;
        server.SetBlastCap(cap);
        if (args.Any(a => a.Equals("--no-crater", StringComparison.OrdinalIgnoreCase)))
            server.DestructionEnabled = false; // (the lobby's Destruction off: nothing dug, to see what the crater costs)
        using var client = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Client", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        Pump(server, [client], 1.0, ref now, clock);
        for (var wait = 0; wait < 40 && server.Phase != MatchPhase.Playing; wait++)
            PumpFast(server, [client], 0.25, ref now);
        Pump(server, [client], 0.5, ref now, clock);
        if (client.LocalId == byte.MaxValue || !server.Players.TryGetValue(client.LocalId, out var cp))
        {
            Console.WriteLine("[mega bench] the client didn't join");
            return 1;
        }
        var w = server.Constructs;
        var centre = t is { Edits: not null } && t.CountX * t.Cell < 5000f ? new Vector3(t.OriginX + t.CountX * t.Cell * 0.5f, 0, t.OriginZ + t.CountZ * t.Cell * 0.5f)
            : map.Spawns.Count > 0 ? map.Spawns[0] : Vector3.Zero; // (a small map's middle; a streamed one's spawn)
        float Ground(float x, float z) => t != null ? t.Sample(x, z) : centre.Y;
        centre.Y = Ground(centre.X, centre.Z);
        if (!cp.Alive)
            server.SelfTestRespawn(cp, now);
        cp.Teleport(new Vector3(centre.X + 350f, Ground(centre.X + 350f, centre.Z) + 0.1f, centre.Z));
        if (server.Local is { } hp)
        {
            if (!hp.Alive)
                server.SelfTestRespawn(hp, now);
            hp.Teleport(new Vector3(centre.X - 120f, Ground(centre.X - 120f, centre.Z) + 0.1f, centre.Z));
        }
        // The busy scene: hulls on a spiral out to 600 m, or nearly to the map's edge.
        var block = ConstructParts.Need("Frame Block");
        var reach = t == null ? 600f : MathF.Min(600f, MathF.Min(MathF.Min(centre.X - t.OriginX, t.OriginX + t.CountX * t.Cell - centre.X), MathF.Min(centre.Z - t.OriginZ, t.OriginZ + t.CountZ * t.Cell - centre.Z)) - 15f);
        var built = 0;
        for (var i = 0; i < bodies; i++)
        {
            var a = i * 2.39996f;
            var r = 20f + (reach - 20f) * MathF.Sqrt((i + 0.5f) / Math.Max(1, bodies));
            var x = centre.X + MathF.Cos(a) * r;
            var z = centre.Z + MathF.Sin(a) * r;
            if (w.Create(new Vector3(x, Ground(x, z) + 0.3f, z), Quaternion.Identity) is not { } c)
                break;
            var size = block.BoundsMax - block.BoundsMin;
            for (var p = 0; p < parts; p++)
                w.AddPart(c, block, new Vector3(p % 12 * size.X, p / 144 * size.Y, p / 12 % 12 * size.Z), 0);
            c.Rebuild();
            built++;
        }
        var bomb = ConstructBuild.PlaceNew(w, def, centre + new Vector3(0, 0.5f, 0), 0);
        if (bomb == null)
        {
            Console.WriteLine("[mega bench] couldn't set the bomb down");
            return 1;
        }
        var partsBefore = 0;
        foreach (var c in w.Bodies)
            partsBefore += c.PartCount;
        Pump(server, [client], 3.0, ref now, clock); // (the client is streamed the world)
        PumpFast(server, [client], 10.0, ref now); // (the hulls settle and sleep)
        Console.WriteLine($"[mega bench] {built} hulls out to {reach:0} m, {w.Bodies.Count} bodies, {partsBefore} parts; client has {client.Constructs.Bodies.Count} bodies");

        // Before: a second of steady ticks for the baseline.
        const double dt = 1.0 / World.TickRate;
        var steady = new List<double>();
        for (var i = 0; i < World.TickRate; i++)
        {
            var sw = Stopwatch.StartNew();
            now += dt;
            server.Update(now, dt);
            steady.Add(sw.Elapsed.TotalMilliseconds);
            client.Update(now, dt);
            Thread.Sleep(1);
        }
        steady.Sort();
        var asleep = 0;
        foreach (var c in w.Bodies)
            asleep += c.Asleep ? 1 : 0;
        Console.WriteLine($"[mega bench] steady server tick: median {steady[steady.Count / 2]:0.00} ms, worst {steady[^1]:0.00} ms; {asleep} of {w.Bodies.Count} bodies asleep");

        // The blast: shot to pieces, its fuse, its front, its walk, its crater.
        var sent0 = server.Net.BytesSent;
        w.MegaStepWorstMs = 0;
        server.MegaDigWorstMs = 0;
        var walked0 = w.MegaPartsWalked;
        var dug0 = server.DigOps;
        var cOps0 = client.DigOpsRead;
        w.DamagePart(bomb, 0, def.Health * 100f, out _);
        var ticks = new List<double>();
        var clientMs = new List<double>();
        var bytesTick = new List<long>();
        long allocAll = 0, allocWorst = 0;
        var startedAt = -1;
        var over8 = 0;
        var after = 0;
        var areas = Enum.GetValues<PerfArea>().Where(a => (int)a < Perf.Count).ToArray();
        var worstAreas = "";
        var worstSeen = 0.0;
        Perf.SetOn(true);
        for (var i = 0; i < 20 * World.TickRate; i++)
        {
            var sent = server.Net.BytesSent;
            Perf.EndFrame();
            var a0 = GC.GetAllocatedBytesForCurrentThread();
            var sw = Stopwatch.StartNew();
            now += dt;
            server.Update(now, dt);
            var ms = sw.Elapsed.TotalMilliseconds;
            var alloc = GC.GetAllocatedBytesForCurrentThread() - a0;
            if (startedAt >= 0 && ms > worstSeen)
            {
                worstSeen = ms;
                worstAreas = string.Join(", ", areas.Where(a => Perf.Depth[(int)a] >= 3 && Perf.OpenFrameMs(a) >= 0.3).OrderByDescending(a => Perf.OpenFrameMs(a)).Take(8).Select(a => $"{a} {Perf.OpenFrameMs(a):0.0}"));
            }
            sw.Restart();
            client.Update(now, dt);
            clientMs.Add(sw.Elapsed.TotalMilliseconds);
            if (startedAt < 0 && w.MegasStarted > 0)
                startedAt = i;
            if (startedAt >= 0)
            {
                ticks.Add(ms);
                bytesTick.Add(server.Net.BytesSent - sent);
                allocAll += alloc;
                allocWorst = Math.Max(allocWorst, alloc);
                if (ms > 8)
                    over8++;
                if (!w.MegaBusy && server.MegaDigsWaiting == 0 && ++after > World.TickRate && ticks.Count >= 5 * World.TickRate)
                    break; // (a second past its end, and five seconds at least: the wrecks it threw)
            }
            Thread.Sleep(1);
        }
        Perf.SetOn(false);
        Pump(server, [client], 1.0, ref now, clock); // (what is still on the wire arrives)
        if (startedAt < 0)
        {
            Console.WriteLine("[mega bench] it never went off");
            return 1;
        }
        var sorted = ticks.ToList();
        sorted.Sort();
        var worstAt = ticks.IndexOf(sorted[^1]);
        var partsAfter = 0;
        foreach (var c in w.Bodies)
            partsAfter += c.PartCount;
        clientMs.Sort();
        Console.WriteLine($"[mega bench] blast: {ticks.Count} ticks timed ({ticks.Count / (float)World.TickRate:0.0} s); server tick median {sorted[sorted.Count / 2]:0.00} ms, 95th {sorted[(int)(sorted.Count * 0.95)]:0.00} ms, worst {sorted[^1]:0.00} ms (tick {worstAt}), {over8} over 8 ms; sum {ticks.Sum():0} ms");
        Console.WriteLine($"[mega bench] the worst tick's areas (ms, F9's): {worstAreas}");
        Console.WriteLine($"[mega bench] the first ticks: {string.Join(" ", ticks.Take(12).Select(v => v.ToString("0.0")))} ms");
        Console.WriteLine($"[mega bench] allocated {allocAll / 1024} KB over the blast, worst tick {allocWorst / 1024} KB");
        Console.WriteLine($"[mega bench] sent {(server.Net.BytesSent - sent0) / 1024} KB to the client in all, worst tick {bytesTick.Max() / 1024.0:0.0} KB, {bytesTick.Take(60).Sum() / 1024} KB in the first second");
        Console.WriteLine($"[mega bench] did: {w.MegaPartsWalked - walked0} parts walked, {partsBefore} parts before and {partsAfter} after in {w.Bodies.Count} bodies; {server.MegaDigBands} crater bands, {server.MegaDigColumns} columns dug, {server.DigOps - dug0} ops logged; client applied {client.DigOpsRead - cOps0} ops; players: host {(server.Local?.Alive == true ? "alive" : "dead")}, client {(cp.Alive ? "alive" : "dead")} at 350 m");
        Console.WriteLine($"[mega bench] client update: median {clientMs[clientMs.Count / 2]:0.00} ms, worst {clientMs[^1]:0.00} ms");
        Console.WriteLine($"[mega bench] inside the server ticks: the front and walk worst {w.MegaStepWorstMs:0.00} ms a tick, a crater band worst {server.MegaDigWorstMs:0.00} ms; {w.MegaBodiesGone} bodies gone whole");
        t?.Edits.Clear();
        return 0;
    }
}
