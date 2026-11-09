using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// <c>--bot-bench [sizes] [--seconds S] [--map NAME]</c>: headless, the server's cost of a Sandbox fight of spawned bots
/// (GameSandboxSpawns.cs): half friendly and half enemy, spawned from the Spawn tab's request on either side of the host,
/// then a fight of S game seconds (20 unless named) at each size (50, 100 and 200 unless named). Prints, per size, the
/// server tick's milliseconds (mean, 95th percentile, worst), the profiler's areas, bytes allocated a tick, deaths, and
/// the snapshot one client would get (bytes and parts). Writes <c>botbench.log</c> next to the self-test log; nothing
/// else is touched (the self-test's throwaway save folder). With <c>--net</c>: over the real transport, a joined player
/// spawns the largest size, then what the client sees (bodies, names, sides, dropped packets) and whether a second person
/// can still join.
/// </summary>
public static partial class SelfTest
{
    public static int BotBench(string[] args)
    {
        string? After(string flag) => args.SkipWhile(a => !a.Equals(flag, StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault();
        var arg = After("--bot-bench");
        var sizes = arg != null && !arg.StartsWith("--") && arg.Split(',').Select(s => int.TryParse(s, out var n) ? n : 0).Where(n => n > 0).ToArray() is { Length: > 0 } list
            ? list
            : [50, 100, 200];
        var seconds = double.TryParse(After("--seconds"), System.Globalization.CultureInfo.InvariantCulture, out var s) ? Math.Clamp(s, 1, 120) : 20;
        var mapName = After("--map") ?? ConstructYardMap;
        Brutal.GlfwApi.Glfw.Init(); // (the clock only)
        SavePaths.UseFolder(SelfTestSaveFolder, "self-test");
        BlueprintStore.ReadOnly = true;
        NavBake.Enabled = false;
        MapBake.Enabled = false;
        MapBake.CacheWrites = false;
        using (_log = new StreamWriter(AppFolders.LogPath("botbench.log"), false))
        {
            Line($"Call of Slop bot bench {DateTime.Now}: {Environment.ProcessorCount} logical processors; {seconds} s a size on {mapName}");
            if (Maps.Find(mapName) is not { } map)
            {
                Line($"no {mapName} map");
                return 1;
            }
            map.RestoreProps();
            World.SetMap(map);
            if (args.Any(x => x.Equals("--net", StringComparison.OrdinalIgnoreCase)))
                BotBenchNet(map, sizes.Max());
            else
                foreach (var size in sizes)
                    BotBenchSize(map, size, seconds);
            Line("DONE");
        }
        return 0;
    }

    private static void BotBenchSize(Map map, int size, double seconds)
    {
        var now = 0.0;
        using var g = ModeGame(0, GameMode.Sandbox, map.Id);
        var hostAt = ConstructWorld.Ground(World.Query, 0, -30);
        if (SpawnHost(g, ref now, hostAt) is not { } me)
        {
            Line($"bots {size}: no local player");
            return;
        }
        var a = ConstructWorld.Ground(World.Query, -40, 0);
        var b = ConstructWorld.Ground(World.Query, 40, 0);
        var r1 = g.SandboxSpawnRequest(me, SbSpawnKind.FriendlyBot, BotDifficulty.Normal, SbLoadKind.Any, 0, 0, size / 2, a);
        var r2 = g.SandboxSpawnRequest(me, SbSpawnKind.EnemyBot, BotDifficulty.Normal, SbLoadKind.Any, 0, 0, size - size / 2, b);
        var spawnSw = Stopwatch.StartNew();
        var spawnTicks = 0;
        var spawnWorst = 0.0;
        while (g.SandboxSpawnsQueued > 0 && spawnTicks < 60 * 60)
        {
            var t0 = Stopwatch.GetTimestamp();
            g.Update(now += 1 / 60.0, 1 / 60f);
            spawnWorst = Math.Max(spawnWorst, Stopwatch.GetElapsedTime(t0).TotalMilliseconds);
            spawnTicks++;
        }
        Line($"bots {size}: asked \"{r1 ?? "ok"}\", \"{r2 ?? "ok"}\"; {g.SandboxBotCount()} spawned in {spawnTicks} ticks ({spawnSw.Elapsed.TotalSeconds:F1} s wall, worst tick {spawnWorst:F1} ms)");
        Perf.SetOn(true);
        var ticks = (int)(seconds * World.TickRate);
        var ms = new double[ticks];
        var areas = new double[Perf.Count];
        var deaths0 = g.Players.Values.Sum(p => p.Deaths);
        var gc0 = GC.CollectionCount(0);
        AllocProfile.Reset();
        AllocProfile.On = true;
        var alloc0 = GC.GetAllocatedBytesForCurrentThread();
        var timeline = new List<string>();
        for (var k = 0; k < ticks; k++)
        {
            var t0 = Stopwatch.GetTimestamp();
            g.Update(now += 1 / 60.0, 1 / 60f);
            ms[k] = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
            Perf.EndFrame((float)ms[k]);
            for (var i = 0; i < Perf.Count; i++)
                areas[i] += Perf.AreaMs(0, i);
            if ((k + 1) % World.TickRate == 0)
            {
                var second = ms.AsSpan(k + 1 - World.TickRate, World.TickRate);
                var worst = 0.0;
                var sum = 0.0;
                foreach (var x in second)
                    (worst, sum) = (Math.Max(worst, x), sum + x);
                timeline.Add($"{g.Players.Values.Count(p => p.SbSpawned && p.Alive)}:{sum / World.TickRate:F1}/{worst:F0}");
            }
        }
        var alloc = (GC.GetAllocatedBytesForCurrentThread() - alloc0) / ticks;
        AllocProfile.Stop();
        AllocProfile.On = false;
        Perf.SetOn(false);
        var sorted = ms.OrderBy(x => x).ToArray();
        var alive = g.Players.Values.Count(p => p.SbSpawned && p.Alive);
        var deaths = g.Players.Values.Sum(p => p.Deaths) - deaths0;
        Line($"bots {size}: tick mean {ms.Average():F2} ms, p95 {sorted[(int)(ticks * 0.95)]:F2} ms, worst {sorted[^1]:F2} ms; {alloc} B a tick, {GC.CollectionCount(0) - gc0} gen 0 GCs; "
             + $"{alive} alive at the end, {g.SandboxBotsSpawned} spawned, {deaths} deaths counted on bodies still here");
        var top = Enumerable.Range(0, Perf.Count).Where(i => areas[i] > 0).OrderByDescending(i => areas[i]).Take(10)
            .Select(i => $"{Perf.Names[i]} {areas[i] / ticks:F2}");
        Line($"bots {size}: each second, alive:mean/worst ms: {string.Join(" ", timeline)}");
        Line($"bots {size}: ms a tick by area: {string.Join(", ", top)}");
        Line($"bots {size}: ms a tick by mark: {AllocProfile.TopTime(14, ticks)}");
        Line($"bots {size}: B a tick by mark: {AllocProfile.Top(8, ticks)}");
        var (bytes, parts) = g.SelfTestSnapshotFor(me);
        Line($"bots {size}: a client's snapshot here: {bytes} B in {parts} part(s), {bytes * World.TickRate / World.SnapshotEvery / 1024} KB/s");
    }

    private static void BotBenchNet(Map map, int size)
    {
        const ushort port = 26361;
        var clock = Stopwatch.StartNew();
        var now = 0.0;
        using var server = new Game(NetPeer.Host(port, 4), new Audio(), "Host", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: map.Id) { MapVoteEnabled = false };
        server.Mode = GameMode.Sandbox;
        using var a = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Alpha", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        Pump(server, [a], 1.0, ref now, clock);
        for (var wait = 0; wait < 40 && server.Phase != MatchPhase.Playing; wait++)
            PumpFast(server, [a], 0.25, ref now);
        Pump(server, [a], 0.3, ref now, clock);
        if (a.LocalId == byte.MaxValue || !server.Players.TryGetValue(a.LocalId, out var sp))
        {
            Line("net: the client didn't join");
            return;
        }
        if (!sp.Alive)
            server.SelfTestRespawn(sp, now);
        sp.Teleport(ConstructWorld.Ground(World.Query, 0, -30));
        sp.ImmuneUntil = now + 1e6;
        Pump(server, [a], 0.3, ref now, clock);
        var dropped0 = NetSafety.Dropped;
        // Both sides far apart, so the bodies live long enough to be counted.
        a.SandboxSendSpawn(SbSpawnKind.FriendlyBot, BotDifficulty.Easy, 0, 0, 0, size / 2, ConstructWorld.Ground(World.Query, -40, 0));
        a.SandboxSendSpawn(SbSpawnKind.EnemyBot, BotDifficulty.Easy, 0, 0, 0, size - size / 2, ConstructWorld.Ground(World.Query, 40, 0));
        Pump(server, [a], 3.0, ref now, clock);
        var onServer = server.Players.Values.Count(p => p.SbSpawned);
        var onClient = a.Players.Values.Count(p => p.Id >= Game.MaxPeople);
        var named = a.Players.Values.Count(p => p.Id >= Game.MaxPeople && p.Name.StartsWith("[BOT]"));
        var sides = a.Players.Values.Count(p => p.Id >= Game.MaxPeople && (p.Team == Game.SbFriendlyTeam || p.Team == Game.SbHostileTeam));
        var far = a.Players.Values.Where(p => p.Id >= Game.MaxPeople && server.Players.ContainsKey(p.Id))
            .Select(p => Vector3.Distance(p.Position, server.Players[p.Id].Position)).DefaultIfEmpty(0).Max();
        Line($"net {size}: \"{a.SandboxNotice}\"; spawned bodies on the server {onServer}, on the client {onClient}, named {named}, with sides {sides}; worst client position off the server's {far:F1} m; "
             + $"server snapshot parts at most {server.SnapMaxParts} ({server.SnapMaxBytes} B the largest); packets dropped {NetSafety.Dropped - dropped0} {NetSafety.LastDrop}");
        using var b = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Bravo", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        Pump(server, [a, b], 2.0, ref now, clock);
        Line($"net {size}: a second person joined {b.LocalId != byte.MaxValue && server.Players.ContainsKey(b.LocalId)} (id {b.LocalId}), sees {b.Players.Values.Count(p => p.Id >= Game.MaxPeople)} spawned bodies; people {server.HumanCount}, seats {server.SeatCount}");
    }
}
