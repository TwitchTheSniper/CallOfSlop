using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// <c>--bench-cookoff [KEY] [--rows R] [--columns C] [--map NAME]</c>: what an ordnance cook-off chain costs (docs/sandbox/CONSTRUCTS.md, "Cook-offs of
/// ordnance"). A command-line benchmark, not a self-test section: it fails nothing. A host and a client over loopback; a frozen Ordnance Rack of R by C
/// stations (16 by 15, 240, by default) loaded with the design (by default a small HE bomb), one bomb shot to pieces; every server tick of the
/// chain is timed (wall clock and the thread's CPU: the box is shared), then what went off. Runs twice (the first warms the JIT; read the last). Then two designed bombs in the air: one blasted, one shot by a ray. Prints one line a phase.
/// </summary>
public static partial class SelfTest
{
    public static int BenchCookOff(string[] args)
    {
        var code = 0;
        for (var run = 1; run <= 2 && code == 0; run++)
        {
            Console.WriteLine($"[cookoff bench] run {run} of 2{(run == 1 ? " (warms the JIT: read the last)" : "")}");
            code = BenchCookOffOnce(args, (ushort)(26534 + run));
        }
        return code;
    }

    private static int BenchCookOffOnce(string[] args, ushort port)
    {
        string? Arg(string name) => args.SkipWhile(a => !a.Equals(name, StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault();
        var key = Arg("--bench-cookoff") is { } k && k.StartsWith(Ordnance.Prefix, StringComparison.OrdinalIgnoreCase) ? k : "Ordnance:B25:Impact Fuse+Warhead HE+Bomb Tail Fins";
        var rows = int.TryParse(Arg("--rows"), out var nr) ? nr : 16;
        var cols = int.TryParse(Arg("--columns"), out var nc) ? nc : 15;
        var map = Maps.Find(Arg("--map") ?? "Proving Ground") ?? Maps.All.First(m => m.Terrain != null);
        if (Ordnance.Parse(key) is not { Explosive: not null } def)
        {
            Console.WriteLine($"[cookoff bench] \"{key}\" isn't a design that adds up");
            return 1;
        }
        Console.WriteLine($"[cookoff bench] {key}: warhead {def.Warhead}, health {def.Health:0}, armour {def.Armour:0}, burns {def.Burns}, {def.Mass:0} kg, {def.Explosive!.ExplodeRadius:0.#} m blast");
        if (ModularRack.Make(RackLayout.Grid, rows, cols, 1250, 250) is not { } shape || ModularRack.Def(shape) is not { } rackDef)
        {
            Console.WriteLine($"[cookoff bench] no {rows} x {cols} rack");
            return 1;
        }
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
        var at = map.Spawns.Count > 0 ? map.Spawns[0] + new Vector3(40, 0, 40) : new Vector3(40, 0, 40);
        at.Y = (map.Terrain?.Sample(at.X, at.Z) ?? 0) + 6f;
        foreach (var p in server.Players.Values)
            if (p.Alive)
                p.Teleport(at + new Vector3(300, 0, 0));
        var rack = w.Create(at, Quaternion.Identity)!;
        w.AddPart(rack, rackDef, Vector3.Zero, 0);
        rack.Frozen = rack.Anchored = true;
        rack.Rebuild();
        // Loaded as a player loads one: a bomb held under each free station snaps on.
        var loaded = 0;
        var lug = def.Sockets[0].At;
        for (var s = 0; s < rackDef.Sockets.Length; s++)
        {
            if (!rackDef.Sockets[s].Name.StartsWith("lug", StringComparison.Ordinal))
                continue;
            var sAt = rack.ToWorld(rack.Parts[0].Pos + Rot24.Apply(rack.Parts[0].Rot, rackDef.Sockets[s].At));
            if (w.Create(sAt - lug, Quaternion.Identity) is not { } held)
                break;
            w.AddPart(held, def, Vector3.Zero, 0);
            held.Rebuild();
            if (ConstructOrdnance.TrySnap(w, held))
                loaded++;
            else if (loaded == 0 && s < 4)
                Console.WriteLine($"[cookoff bench] station {s}: found {ConstructOrdnance.FindSnap(w, held, out var spot)}, {spot.Distance:0.00} m, {spot.Why ?? "fits"}, rack {rack.RackCount}");
            if (w.IsLive(held))
                w.Remove(held);
        }
        rack.Rebuild();
        Pump(server, [client], 2.0, ref now, clock);
        Console.WriteLine($"[cookoff bench] {rackDef.Name}: {loaded} bombs loaded, {rack.PartCount} parts; client has {client.Constructs.Bodies.Count} bodies");
        var first = -1;
        for (var i = 0; i < rack.PartCount && first < 0; i++)
            if (rack.DefOf(i) == def)
                first = i;
        if (first < 0)
            return 1;
        const double dt = 1.0 / World.TickRate;
        var set0 = w.CookOffsSet;
        var land0 = w.ShellsLanded;
        w.DamagePart(rack, first, def.Health * 10f, out _);
        var ticks = new List<double>();
        var cpu = new List<double>();
        long alloc = 0;
        var quiet = 0;
        for (var i = 0; i < 30 * World.TickRate; i++)
        {
            var a0 = GC.GetAllocatedBytesForCurrentThread();
            var sw = Stopwatch.StartNew();
            var c0 = ThreadCpu.NowMs();
            now += dt;
            server.Update(now, dt);
            cpu.Add(ThreadCpu.NowMs() - c0);
            ticks.Add(sw.Elapsed.TotalMilliseconds);
            alloc += GC.GetAllocatedBytesForCurrentThread() - a0;
            client.Update(now, dt);
            quiet = w.ShellBlastsWaiting == 0 && w.CookOffCount == 0 ? quiet + 1 : 0;
            if (quiet > World.TickRate)
                break;
            Thread.Sleep(1);
        }
        var sorted = ticks.OrderBy(v => v).ToList();
        var left = 0;
        for (var i = 0; w.IsLive(rack) && i < rack.PartCount; i++)
            left += rack.DefOf(i) == def ? 1 : 0;
        Console.WriteLine($"[cookoff bench] chain: {w.CookOffsSet - set0} set off, {w.ShellsLanded - land0} blasts, {left} bombs left on the rack; {ticks.Count} ticks, median {sorted[sorted.Count / 2]:0.00} ms, worst {sorted[^1]:0.00} ms (tick {ticks.IndexOf(sorted[^1])}), {ticks.Count(v => v > 8)} over 8 ms, {alloc / 1024} KB allocated");
        var cs = cpu.OrderBy(v => v).ToList();
        Console.WriteLine($"[cookoff bench] server tick CPU ({ThreadCpu.Kind}): median {cs[cs.Count / 2]:0.00} ms, worst {cs[^1]:0.00} ms (tick {cpu.IndexOf(cs[^1])}), {cpu.Count(v => v > 8)} over 8 ms; {w.Bodies.Count} bodies after");
        Console.WriteLine($"[cookoff bench] the first second: {string.Join(" ", ticks.Take(60).Select(v => v.ToString("0.0")))}");

        // In the air: two bombs falling side by side, one blasted near, one shot by a ray.
        var air = at + new Vector3(0, 80, 0);
        var ch = def.OrdStats!.Flight.Charges[0];
        CShell Falling(Vector3 p) => new() { Kind = ShellKind.Bomb, Pos = p, Prev = p, Tested = p, Vel = new Vector3(0, -5, 0), Blast = ch.Blast, Damage = ch.Damage, Crater = ch.Crater, Design = def, Flags = ShellFlags.Designed, By = 255, Size = def.BoundsMax - def.BoundsMin, Delay = 1 };
        w.AddShell(Falling(air));
        w.AddShell(Falling(air + new Vector3(30, 0, 0)));
        var down0 = w.ShellsShotDown;
        w.Blast(air + new Vector3(2, 0, 0), 6f, def.Health * 3f);
        var blasted = w.ShellsShotDown - down0;
        var rayHit = w.RayShell(air + new Vector3(30, 0, -50), Vector3.UnitZ, 100f, out var rd, out var rk);
        for (var n = 0; n < 40 && rayHit && w.ShellsShotDown - down0 < 2; n++)
            w.HurtShell(rk, 40f); // (rifle-sized rounds until it goes)
        Console.WriteLine($"[cookoff bench] in the air: blasted {blasted} (of 1 in reach), ray hit {rayHit} at {rd:0.0} m (want ~50), shot down {w.ShellsShotDown - down0} of 2");
        for (var i = 0; i < 2 * World.TickRate; i++)
        {
            now += dt;
            server.Update(now, dt);
            client.Update(now, dt);
        }
        Console.WriteLine($"[cookoff bench] shells left {w.ShellCount}");
        return 0;
    }
}
