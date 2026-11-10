using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// <c>--bench-vehicle-guns [BLUEPRINT...] [--shells N] [--map NAME]</c>: the gunsmith's guns by hand (docs/sandbox/VEHICLE-WEAPONS.md). A command-line benchmark, not a
/// self-test section: it fails nothing. A host alone on a Sandbox map with terrain; the committed blueprints named (by default some that carry the gunsmith's shell and
/// rapid guns) stand in a row. Each gun is listed with its numbers and fired once (did a round or a shell leave?). Then each shell gun's shell lands, N at once (4 by
/// default), on the next vehicle in the row: every server tick until the blasts are done is timed (the thread's CPU), then the parts lost, the shove and the ground dug.
/// </summary>
public static partial class SelfTest
{
    public static int BenchVehicleGuns(string[] args)
    {
        string? Arg(string name) => args.SkipWhile(a => !a.Equals(name, StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault();
        var names = args.SkipWhile(a => !a.Equals("--bench-vehicle-guns", StringComparison.OrdinalIgnoreCase)).Skip(1).TakeWhile(a => !a.StartsWith("--", StringComparison.Ordinal)).ToList();
        if (names.Count == 0)
            names = ["HighlandFlatbedFort8", "ITBoxTruck3", "HighlandBoxTruck7", "ITWarBus10", "ScamFlatbedFort6", "BanditTankerRig10", "ITTankerRig20", "BritishDoubleDecker9"];
        var salvo = int.TryParse(Arg("--shells"), out var ns) ? Math.Clamp(ns, 1, 64) : 4;
        var merge = int.TryParse(Arg("--merge"), out var nm) ? Math.Clamp(nm, 1, 6) : 0; // (shells as an N x N Howitzer's, whatever the guns are)
        var map = Maps.Find(Arg("--map") ?? "Proving Ground") ?? Maps.All.First(m => m.Terrain != null);
        var clock = Stopwatch.StartNew();
        var now = 0.0;
        map.RestoreProps();
        map.Terrain?.Edits.Clear();
        World.SetMap(map);
        using var server = new Game(NetPeer.Host(26541, 4), new Audio(), "Host", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: (byte)Array.IndexOf(Maps.All, map)) { MapVoteEnabled = false };
        server.Mode = GameMode.Sandbox;
        Console.WriteLine($"[guns bench] host up on {map.Name}");
        Pump(server, [], 1.0, ref now, clock);
        for (var wait = 0; wait < 40 && server.Phase != MatchPhase.Playing; wait++)
            PumpFast(server, [], 0.25, ref now);
        var w = server.Constructs;
        Console.WriteLine($"[guns bench] phase {server.Phase}");
        var t = map.Terrain;
        var at = map.Spawns.Count > 0 ? map.Spawns[0] + new Vector3(40, 0, 40) : new Vector3(40, 0, 40);
        float Ground(float x, float z) => t?.Sample(x, z) ?? 0;
        if (server.Local is { } hp)
            hp.Teleport(at + new Vector3(0, Ground(at.X, at.Z - 200) + 1, -200));
        var row = new List<(string Name, Construct C)>();
        foreach (var n in names)
        {
            var file = Path.Combine(AppContext.BaseDirectory, "Constructs", "Blueprints", n + ".json");
            if (!File.Exists(file))
            {
                Console.WriteLine($"[guns bench] no blueprint {n}");
                continue;
            }
            var bp = ConstructBuild.ParseBlueprint(File.ReadAllText(file), file);
            var job = new ConstructBuild.GroundJob(w, bp, null);
            while (!job.Step(50))
            {
            }
            var x = at.X + row.Count * 30f;
            var c = job.Join(new Vector3(x, Ground(x, at.Z), at.Z), 0, out var why);
            if (c == null)
            {
                Console.WriteLine($"[guns bench] {n} doesn't build: {why}");
                continue;
            }
            row.Add((n, c));
        }
        Pump(server, [], 2.0, ref now, clock); // (settle)
        const double dt = 1.0 / World.TickRate;
        var shells = new List<(string Name, CGun G)>();
        foreach (var (name, c) in row)
        {
            c.Rebuild();
            Console.WriteLine($"[guns bench] {name}: {c.PartCount} parts, {c.Mass / 1000f:0.0} t, {c.GunCount} guns");
            for (var k = 0; k < c.GunCount; k++)
            {
                var g = c.Guns[k];
                if (g.Def == null)
                {
                    Console.WriteLine($"[guns bench]   gun {k}: can't fire: {g.Problem}");
                    continue;
                }
                var shell = (g.Effects & GunEffect.Shell) != 0;
                var shots0 = w.ShotsFired;
                var count0 = w.ShellCount;
                var vel0 = c.Vel;
                ConstructWeapons.Fire(w, c, k, World.Query);
                Console.WriteLine($"[guns bench]   gun {k}: {g.Def.Name} {Math.Max(1, (int)g.MergeN)}x{Math.Max(1, (int)g.MergeN)}, {g.Def.Damage} dmg x {g.Shots}, every {g.Interval} ticks, kick {g.Kick:0} N s"
                    + (shell ? $", {g.Shell} shell {g.BlastDamage:0} dmg, blast {g.Blast:0.0} m, crater {g.Crater:0.0} m, reach {g.MaxRange:0} m" : g.Blast > 0 ? $", splash {g.Blast:0.0} m" : "")
                    + $"; fired {w.ShotsFired - shots0}, shells +{w.ShellCount - count0}, recoil {(c.Vel - vel0).Length():0.00} m/s");
                if (shell)
                    shells.Add((name, g));
            }
        }
        Pump(server, [], 6.0, ref now, clock); // (the test shots land)
        var dug0 = t?.Edits?.TouchedCells ?? 0;
        for (var s = 0; s < shells.Count && row.Count > 1; s++)
        {
            var (from, g) = shells[s];
            if (merge > 0)
            {
                g.BlastDamage = 512 * merge * merge;
                g.Shell = s % 2 == 1 ? ShellKind.Siege : ShellKind.Explosive;
                (g.Blast, g.Crater) = ConstructShells.ShellSize(g.BlastDamage, g.Shell, merge);
                from = $"{merge}x{merge} Howitzer ({g.Shell})";
            }
            var target = row[(s + 1) % row.Count];
            if (!w.IsLive(target.C))
            {
                Console.WriteLine($"[guns bench] {from}'s shell: {target.Name} is gone");
                continue;
            }
            var parts0 = target.C.PartCount;
            var bodies0 = w.Bodies.Count;
            var com0 = target.C.WorldCom;
            var hp0 = 0f;
            for (var i = 0; i < target.C.PartCount; i++)
                hp0 += target.C.Parts[i].Health;
            for (var k = 0; k < salvo; k++)
            {
                var p = target.C.WorldCom + new Vector3(k % 2 * 3f - 1.5f, 6f, k / 2 * 3f - 1.5f);
                w.AddShell(new CShell { Kind = g.Shell, Pos = p, Prev = p, Tested = p, Vel = new Vector3(0, -60, 0), Blast = g.Blast, Damage = g.BlastDamage, Crater = g.Crater, By = 255 });
            }
            var cpu = new List<double>();
            var notes = new List<string>();
            long alloc = 0;
            for (var i = 0; i < 6 * World.TickRate; i++)
            {
                var a0 = GC.GetAllocatedBytesForCurrentThread();
                var c0 = ThreadCpu.NowMs();
                var (b0, pw0, l0) = (w.Bodies.Count, w.ShellPartsWalked, w.ShellsLanded);
                now += dt;
                server.Update(now, dt);
                cpu.Add(ThreadCpu.NowMs() - c0);
                notes.Add($"t{i} {cpu[^1]:0.0} ms: bodies {b0}->{w.Bodies.Count}, walked {w.ShellPartsWalked - pw0}, landed {w.ShellsLanded - l0}, waiting {w.ShellBlastsWaiting}");
                alloc += GC.GetAllocatedBytesForCurrentThread() - a0;
                if (i > 2 * World.TickRate && w.ShellBlastsWaiting == 0 && w.ShellCount == 0 && w.CookOffCount == 0)
                    break;
            }
            var cs = cpu.OrderBy(v => v).ToList();
            foreach (var k in Enumerable.Range(0, cpu.Count).OrderByDescending(k => cpu[k]).Take(6).Order())
                Console.WriteLine($"[guns bench]     {notes[k]}");
            var live = w.IsLive(target.C);
            var hp1 = 0f;
            for (var i = 0; live && i < target.C.PartCount; i++)
                hp1 += target.C.Parts[i].Health;
            Console.WriteLine($"[guns bench] {salvo} x {from}'s {g.Def!.Name} shells ({g.BlastDamage:0} dmg, {g.Blast:0.0} m, crater {g.Crater:0.0} m) on {target.Name}: parts {parts0} -> {(live ? target.C.PartCount : 0)}, hp {hp0:0} -> {hp1:0}, "
                + $"bodies {bodies0} -> {w.Bodies.Count}, moved {(live ? Vector3.Distance(target.C.WorldCom, com0) : -1):0.0} m; ticks {cpu.Count}, CPU median {cs[cs.Count / 2]:0.00} ms, worst {cs[^1]:0.00} ms, {cpu.Count(v => v > 8)} over 8 ms, {alloc / 1024} KB allocated");
        }
        Console.WriteLine($"[guns bench] craters dug {(t?.Edits?.TouchedCells ?? 0) - dug0}; shells landed {w.ShellsLanded}, parts walked {w.ShellPartsWalked}");
        return 0;
    }
}
