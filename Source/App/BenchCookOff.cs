using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// <c>--bench-cookoff [KEY] [--rows R] [--columns C] [--map NAME]</c>: what an ordnance cook-off chain costs (docs/sandbox/CONSTRUCTS.md, "Cook-offs of
/// ordnance"). A command-line benchmark, not a self-test section: it fails nothing. A host and a client over loopback; a frozen Ordnance Rack of R by C
/// stations (16 by 15, 240, by default) loaded with the design (by default a small HE bomb), one bomb shot to pieces; every server tick of the
/// chain is timed, then what went off. Then two designed bombs in the air: one blasted, one shot by a ray. Prints one line a phase.
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
        for (var rep = 0; rep < 3; rep++)
        {
            var rb = Stopwatch.StartNew();
            rack.Dirty = true;
            rack.Rebuild();
            var r1 = rb.Elapsed.TotalMilliseconds;
            rack.ColliderChanged();
            ConstructCollider.DiagA = ConstructCollider.DiagC = 0;
            _ = rack.Collider;
            Console.WriteLine($"[cookoff bench] DIAG rebuild {r1:0.00} ms, collider {rb.Elapsed.TotalMilliseconds - r1:0.00} ms (leaves {ConstructCollider.DiagA:0.00} of {ConstructCollider.DiagC:0.00}, {ConstructCollider.DiagLeaves} leaves)");
        }
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
        {
            var rb = Stopwatch.StartNew();
            if (rack.Dirty)
                rack.Rebuild();
            var r1 = rb.Elapsed.TotalMilliseconds;
            ConstructCollider.DiagA = ConstructCollider.DiagB = ConstructCollider.DiagC = ConstructCollider.DiagRough = ConstructCollider.DiagBoxes = ConstructCollider.DiagHulls = ConstructCollider.DiagReuse = ConstructCollider.DiagMoved = ConstructCollider.DiagBnd = ConstructCollider.DiagStore = ConstructCollider.DiagAdd = ConstructCollider.DiagWorstPart = 0;
            var al0 = GC.GetAllocatedBytesForCurrentThread();
            var gcs = GC.CollectionCount(0);
            var pause0 = GC.GetTotalPauseDuration();
            var dummyAt = Stopwatch.StartNew();
            var dummy = new byte[4096];
            dummy[1] = 1;
            Console.WriteLine($"[cookoff bench] DIAG a 4 KB array took {dummyAt.Elapsed.TotalMilliseconds:0.00} ms");
            var cpu0 = ThreadCpu.NowMs();
            _ = rack.Collider;
            Console.WriteLine($"[cookoff bench] DIAG collider thread CPU {ThreadCpu.NowMs() - cpu0:0.00} ms ({ThreadCpu.Kind})");
            Console.WriteLine($"[cookoff bench] DIAG gc during {GC.CollectionCount(0) - gcs}, pause {(GC.GetTotalPauseDuration() - pause0).TotalMilliseconds:0.0} ms");
            var again = Stopwatch.StartNew();
            rack.ColliderChanged();
            _ = rack.Collider;
            Console.WriteLine($"[cookoff bench] DIAG part {ConstructCollider.DiagWorstAt}: {rack.DefOf(ConstructCollider.DiagWorstAt).Name}, hullAt {ConstructCollider.DiagWorstHulls} of {ConstructCollider.DiagLen}, big reuse {ConstructCollider.DiagBigReuse:0.00}, part 1 {rack.DefOf(1).Name}");
            Console.WriteLine($"[cookoff bench] DIAG again {again.Elapsed.TotalMilliseconds:0.00} ms");
            Console.WriteLine($"[cookoff bench] DIAG after the hit: rebuild {r1:0.00} ms, collider {rb.Elapsed.TotalMilliseconds - r1:0.00} ms (count {ConstructCollider.DiagB:0.00}, rough {ConstructCollider.DiagRough:0.00}, boxes {ConstructCollider.DiagBoxes:0.00}, hulls {ConstructCollider.DiagHulls:0.00}, alloc {GC.GetAllocatedBytesForCurrentThread() - al0}, miss {ConstructCollider.DiagMiss} reuse {ConstructCollider.DiagReuse:0.00} moved {ConstructCollider.DiagMoved:0.00} bnd {ConstructCollider.DiagBnd:0.00} store {ConstructCollider.DiagStore:0.00} add {ConstructCollider.DiagAdd:0.00} worst part {ConstructCollider.DiagWorstPart:0.00} at {ConstructCollider.DiagWorstAt} ({ConstructCollider.DiagWorstHulls} hulls), leaves {ConstructCollider.DiagA:0.00} of {ConstructCollider.DiagC:0.00}, {ConstructCollider.DiagLeaves} leaves), fuse due {rack.FuseFitDue}");
        }
        var ticks = new List<double>();
        ConstructWorld.DiagWeapons = ConstructWorld.DiagPhysics = ConstructWorld.DiagTargets = 0;
        long alloc = 0;
        var quiet = 0;
        for (var i = 0; i < 30 * World.TickRate; i++)
        {
            var a0 = GC.GetAllocatedBytesForCurrentThread();
            var (dw, dp, gc0, dcol) = (ConstructWorld.DiagWeapons, ConstructWorld.DiagPhysics, GC.CollectionCount(0), ConstructPhysics.DiagCollider);
            var sw = Stopwatch.StartNew();
            now += dt;
            server.Update(now, dt);
            ticks.Add(sw.Elapsed.TotalMilliseconds);
            alloc += GC.GetAllocatedBytesForCurrentThread() - a0;
            client.Update(now, dt);
            if (ticks[^1] > 4)
                Console.WriteLine($"[cookoff bench] DIAG t {i}: {ticks[^1]:0.0} ms, weapons {ConstructWorld.DiagWeapons - dw:0.0} physics {ConstructWorld.DiagPhysics - dp:0.0} (collider {ConstructPhysics.DiagCollider - dcol:0.0}), gc {GC.CollectionCount(0) - gc0}, rack {(w.IsLive(rack) ? rack.PartCount : -1)}");
            if (i % 10 == 0)
                Console.WriteLine($"[cookoff bench] DIAG tick {i}: {ticks[^1]:0.0} ms, bodies {w.Bodies.Count}, awake {w.Bodies.Count(b => !b.Asleep)}, rack {(w.IsLive(rack) ? rack.PartCount : -1)}, waiting {w.ShellBlastsWaiting}, set {w.CookOffsSet - set0}");
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
        Console.WriteLine($"[cookoff bench] DIAG weapons {ConstructWorld.DiagWeapons:0} ms physics {ConstructWorld.DiagPhysics:0} ms (targets {ConstructWorld.DiagTargets:0} ms) of {ticks.Sum():0} ms; bodies {w.Bodies.Count}");
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
