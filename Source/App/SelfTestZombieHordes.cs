using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Self-test section "Zombie hordes" (docs/zombies/ZOMBIES-MODE.md, "Hordes" and "Horde controller"): the alive curve
/// and the host's cap; scripted hordes of 24, 100 and 300 zombies on Blackmoor Asylum (server tick cost, allocations,
/// snapshot bytes per client per second, the client's update and drawing cost); round 30 filling to its cap of 300; the
/// horde controller's group decisions (spreading over the players, a long window queue splitting to the other windows,
/// a gunshot turning a group, a blast scattering one that then regroups, a down sending a group to someone else, a
/// lure, a blocked group regrouping), its determinism; and a loopback client with a bad connection receiving a
/// 300-zombie horde.
/// </summary>
public static partial class SelfTest
{
    private static readonly List<string> HordeNotes = new();

    private static void HLine(string s)
    {
        Line(s);
        HordeNotes.Add(s);
    }

    private static void CheckZombieHordes()
    {
        HordeNotes.Clear();
        var now = 0.0;
        if (Maps.Find("Blackmoor Asylum") is not { Zombies: { } zm } map)
        {
            Fail("no Blackmoor Asylum map");
            return;
        }
        HordeCurve(map, zm, ref now);
        var rows = new List<string>();
        foreach (var n in new[] { 24, 100, 300, Game.MaxZombies }) // (the last: the most the rules can ask for, 600: the player scaling's budget check)
            rows.Add(HordeMeasure(map, zm, n, ref now));
        HLine("table (zombies | server tick ms CPU mean / worst | ZEnts KB/s per client | client ms a frame): " + string.Join("; ", rows));
        HordeFill(map, zm, ref now);
        HordePool(map, zm, ref now);
        HordeSpread(map, zm, ref now);
        HordeWindowSplit(map, zm, ref now);
        HordeStimuli(map, zm, ref now);
        HordeBlocked(map, zm, ref now);
        HordeDeterminism(map, zm);
        HordeLoopback(map, zm);
        Line("::warning title=Zombie hordes (results)::" + string.Join("%0A", HordeNotes.Select(n => n.Replace("%", "%25").Replace("\r", "").Replace("\n", " "))));
    }

    // ---- The alive curve and the caps --------------------------------------------------------------------------

    private static void HordeCurve(Map map, ZombiesMap zm, ref double now)
    {
        var rules = ZombiesRules.Load(File.ReadAllText(ZombiesRules.PathName), "rules");
        int[] rounds = [1, 5, 10, 15, 20, 25, 30, 40, 60];
        var solo = rounds.Select(r => rules.MaxAliveFor(r, 1)).ToArray();
        var four = rounds.Select(r => rules.MaxAliveFor(r, 4)).ToArray();
        var counts = rounds.Select(r => rules.ZombieCount(r, 1)).ToArray();
        HLine($"alive cap by round (solo): {string.Join(" ", rounds.Select((r, i) => $"r{r} {solo[i]}"))}; 4 players: {string.Join(" ", rounds.Select((r, i) => $"r{r} {four[i]}"))}; "
              + $"round sizes solo: {string.Join(" ", rounds.Select((r, i) => $"r{r} {counts[i]}"))}; bursts r1 {rules.SpawnBurstCurve.At(1)} r20 {rules.SpawnBurstCurve.At(20)} r30 {rules.SpawnBurstCurve.At(30)}, "
              + $"active spawners r1 {rules.ActiveSpawnersCurve.At(1)} r30 {rules.ActiveSpawnersCurve.At(30)}; hard cap {rules.MaxAlive} (most {Game.MaxZombies})");
        if (solo[0] != 24 || solo[2] < 85 || solo[2] > 95 || solo[4] != 220 || solo[6] < 400 || solo.Zip(solo.Skip(1)).Any(p => p.Second < p.First) || four[6] <= solo[6] || four.Any(v => v > rules.MaxAlive))
            Fail("the alive cap should be 24 early, about 90 by round 10, 220 by round 20 and 400+ by round 30, never falling, more for more players, never over the hard cap");
        // A broken curve or horde section is refused with a reason.
        string Refused(string from, string to)
        {
            try
            {
                ZombiesRules.Load(File.ReadAllText(ZombiesRules.PathName).Replace(from, to), "bad rules");
                return "(loaded)";
            }
            catch (WeaponFileException e)
            {
                return e.Message;
            }
        }
        var why = Refused("\"maxAliveCurve\": [[1, 24]", "\"maxAliveCurve\": [[3, 24], [2, 30]");
        var why2 = Refused("\"thinkEvery\": [1, 2, 4, 8]", "\"thinkEvery\": [1, 2]");
        HLine($"  broken curve: '{why}'; broken horde section: '{why2}'");
        if (!why.Contains("rounds go up") || !why2.Contains("thinkEvery"))
            Fail("a broken alive curve or horde section should be refused with a reason");

        // The host's cap lowers the round's cap.
        using var g = ZGame(0, ref now, tweak: r => r.FirstRoundDelay = 9999, onMap: map);
        g.SelfTestBeginRound(30, now);
        var r30 = g.ZAliveCap();
        g.HostHordeCap = 100;
        var host = g.ZAliveCap();
        g.HostHordeCap = Game.MaxZombies;
        Settings FromLine(string line)
        {
            var tmp = Path.Combine(Path.GetTempPath(), $"cos-hordecap-{Environment.ProcessId}.cfg");
            try
            {
                File.WriteAllLines(tmp, [line]);
                return Settings.Load(tmp);
            }
            finally
            {
                File.Delete(tmp);
            }
        }
        var settings = FromLine("zombie_horde_cap=150");
        var settingsLow = FromLine("zombie_horde_cap=3");
        var settingsOld = FromLine("zombie_horde_cap=512"); // (the old "no limit" before the player scaling: still no limit)
        HLine($"  round 30 cap {r30}, with the host's horde cap at 100: {host}; settings 'zombie_horde_cap=150' -> {settings.ZombieHordeCap}, '=3' -> {settingsLow.ZombieHordeCap}, '=512' (the old no limit) -> {settingsOld.ZombieHordeCap}, default {new Settings().ZombieHordeCap}");
        if (r30 < 400 || host != 100 || settings.ZombieHordeCap != 150 || settingsLow.ZombieHordeCap != Game.MinHordeCap || settingsOld.ZombieHordeCap != Game.MaxZombies || new Settings().ZombieHordeCap != Game.MaxZombies)
            Fail("the host's Zombie horde cap should lower the alive cap (and load from the settings file)");
    }

    // ---- Reuse: spawning and dying allocate nothing ----------------------------------------------------------

    /// <summary>
    /// Zombies are pooled (Source/Game/ZombiePool.cs): bursts of spawns and deaths allocate nothing once the pool is warm,
    /// a spawn reuses a body that left the horde, and a reused zombie is exactly a new one (every field, by reflection).
    /// </summary>
    private static void HordePool(Map map, ZombiesMap zm, ref double now)
    {
        using var g = ZGame(0, ref now, tweak: r => r.FirstRoundDelay = 9999, onMap: map);
        var me = g.Local!;
        me.ImmuneUntil = 1e9;
        var seen = new HashSet<Zombie>(ReferenceEqualityComparer.Instance);
        var reused = 0;
        var t = now;
        long Burst()
        {
            var a0 = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 40; i++)
                g.SelfTestSpawnZombie(i % zm.Spawners.Count, t);
            for (var k = 0; k < 10; k++)
                g.SelfTestZombiesTick(t += World.TickDt);
            foreach (var z in g.Horde)
            {
                z.State = ZState.Dead; // (dead long enough to leave the horde on the next tick)
                z.StateAt = t - 2;
            }
            for (var k = 0; k < 3; k++)
                g.SelfTestZombiesTick(t += World.TickDt);
            return GC.GetAllocatedBytesForCurrentThread() - a0;
        }
        Burst(); // (fills the pool)
        Burst(); // (JIT, flow fields, list capacities)
        foreach (var z in g.Horde)
            seen.Add(z);
        var bytes = SteadyAllocBytes(Burst, warmups: 0, rounds: 3);
        var left = g.Horde.Count;
        var pooled = g.ZombiePoolCount;
        var probe = g.SelfTestSpawnZombie(0, t);
        reused = probe.Hm.Slot >= 0 && pooled > 0 && g.ZombiePoolCount == pooled - 1 ? 1 : 0;
        now = t;

        // A zombie with every field scribbled on, reset, reads back as a new one.
        var used = new Zombie();
        PoolScribble(used, 0);
        used.ResetForReuse();
        var diff = PoolDiff(new Zombie(), used, "Zombie", 0);
        HLine($"  pool: 3 bursts of 40 spawns and deaths after warm-up allocate {bytes} bytes ({left} left in the horde, {pooled} in the pool, next spawn reused {reused == 1}); reset zombie vs new: {diff ?? "same"}");
        if (bytes > 0 || left != 0 || pooled < 40 || reused != 1)
            Fail($"spawning and killing zombies should reuse pooled zombies and allocate nothing after warm-up ({bytes} bytes, {left} left, {pooled} pooled)");
        if (diff != null)
            Fail($"a pooled zombie's reset should give exactly a new zombie (Zombie.ResetForReuse, ZombiePool.cs): {diff} differs");
    }

    private const System.Reflection.BindingFlags PoolFields = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;

    /// <summary>Writes a non-default value to every field of <paramref name="o"/> (and its HordeMember, arrays and lists).</summary>
    private static void PoolScribble(object o, int depth)
    {
        foreach (var f in o.GetType().GetFields(PoolFields))
        {
            var ft = f.FieldType;
            var v = f.GetValue(o);
            if (ft.IsArray && v is Array arr)
            {
                for (var i = 0; i < arr.Length; i++)
                    arr.SetValue(PoolJunk(ft.GetElementType()!), i);
            }
            else if (v is System.Collections.IList list && ft.IsGenericType)
                list.Add(PoolJunk(ft.GetGenericArguments()[0]));
            else if (ft.IsClass && v != null && depth < 2)
                PoolScribble(v, depth + 1);
            else if (ft.IsValueType)
                f.SetValue(o, PoolJunk(ft));
        }
    }

    /// <summary>A value of <paramref name="t"/> with every byte 0x5A (true for a bool).</summary>
    private static object PoolJunk(Type t) =>
        t == typeof(bool) ? true : typeof(SelfTest).GetMethod(nameof(PoolJunkOf), System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.MakeGenericMethod(t).Invoke(null, null)!;

    private static object PoolJunkOf<T>() where T : unmanaged
    {
        T v = default;
        System.Runtime.InteropServices.MemoryMarshal.AsBytes(new Span<T>(ref v)).Fill(0x5A);
        return v;
    }

    /// <summary>Byte for byte for a struct (an inline array has no Equals), else Equals.</summary>
    private static bool PoolSame(object? a, object? b) =>
        a != null && b != null && a.GetType().IsValueType && a.GetType() == b.GetType() && a is not bool
            ? (bool)typeof(SelfTest).GetMethod(nameof(PoolBytesSame), System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.MakeGenericMethod(a.GetType()).Invoke(null, [a, b])!
            : Equals(a, b);

    private static bool PoolBytesSame<T>(object a, object b) where T : unmanaged
    {
        var (x, y) = ((T)a, (T)b);
        return System.Runtime.InteropServices.MemoryMarshal.AsBytes(new Span<T>(ref x)).SequenceEqual(System.Runtime.InteropServices.MemoryMarshal.AsBytes(new Span<T>(ref y)));
    }

    /// <summary>The first field (by path) where <paramref name="a"/> and <paramref name="b"/> differ, or null.</summary>
    private static string? PoolDiff(object a, object b, string path, int depth)
    {
        foreach (var f in a.GetType().GetFields(PoolFields))
        {
            var (va, vb) = (f.GetValue(a), f.GetValue(b));
            var name = path + "." + f.Name;
            if (va is Array xa && vb is Array xb)
            {
                if (xa.Length != xb.Length)
                    return name + ".Length";
                for (var i = 0; i < xa.Length; i++)
                    if (!PoolSame(xa.GetValue(i), xb.GetValue(i)))
                        return $"{name}[{i}]";
            }
            else if (va is System.Collections.ICollection ca && vb is System.Collections.ICollection cb)
            {
                if (ca.Count != cb.Count)
                    return name + ".Count";
            }
            else if (f.FieldType.IsClass && va != null && vb != null && depth < 2)
            {
                if (PoolDiff(va, vb, name, depth + 1) is { } d)
                    return d;
            }
            else if (!PoolSame(va, vb))
                return name;
        }
        return null;
    }

    // ---- Scripted hordes: the costs ---------------------------------------------------------------------------

    /// <summary>Places <paramref name="count"/> zombies spread over the areas (seeded), chasing.</summary>
    private static void HordePlace(Game g, Map map, ZombiesMap zm, int count, double now, uint seed = 12345u, Func<int, int>? areaOf = null)
    {
        var rng = seed;
        float R()
        {
            rng ^= rng << 13;
            rng ^= rng >> 17;
            rng ^= rng << 5;
            return (rng >> 8) / 16777216f;
        }
        for (var i = 0; i < count; i++)
        {
            var ai = areaOf?.Invoke(i) ?? i % zm.Areas.Count;
            var a = zm.Areas[ai];
            Vector3 p = default;
            var ok = false;
            for (var k = 0; k < 20 && !ok; k++)
            {
                p = new Vector3(a.X0 + 0.7f + R() * (a.X1 - a.X0 - 1.4f), 0, a.Z0 + 0.7f + R() * (a.Z1 - a.Z0 - 1.4f));
                p.Y = map.GroundHeight(p.X, p.Z);
                ok = a.Contains(p) && !World.Blocked(p + new Vector3(0, 0.05f, 0));
            }
            if (!ok)
                p = FreeSpot(map, a);
            ZPlace(g, zm, p, ai, (ZSpeed)(i % 3), now);
        }
    }

    /// <summary>
    /// A scripted horde of <paramref name="count"/> chasing the immune host through every open door: server tick cost
    /// (CPU), allocations, ZEnts bytes per second for a client at the host's spot, and the client's read, pose, draw and
    /// sound cost. Returns the table row.
    /// </summary>
    private static string HordeMeasure(Map map, ZombiesMap zm, int count, ref double now)
    {
        using var g = ZGame(0, ref now, tweak: r => r.FirstRoundDelay = 9999, onMap: map);
        for (var d = 0; d < zm.Doors.Count; d++)
            g.OpenDoor(d, null);
        var me = g.Local!;
        me.ImmuneUntil = 1e9;
        me.Teleport(FreeSpot(map, zm.Areas[zm.StartArea]));
        HordePlace(g, map, zm, count, now);
        ZTicks(g, ref now, 1.0); // warm up (JIT, the flow fields)
        var view = new Game.ZEntView();
        var w = new PacketWriter();
        var cpuTimer = new TickCpu();
        var ticks = 0;
        double sumCpu = 0, worstCpu = 0;
        long bytes = 0;
        var biggest = 0;
        var alloc = 0L;
        int nearAgeWorst = 0, nearCount = 0;
        for (var t = 0; t < 60 * 3; t++)
        {
            now += World.TickDt;
            var a0 = GC.GetAllocatedBytesForCurrentThread();
            cpuTimer.Start();
            var c0 = ThreadCpu.NowMs();
            var t0 = Stopwatch.GetTimestamp();
            g.SelfTestZombiesTick(now);
            var wall = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
            var cpu = ThreadCpu.NowMs() - c0;
            cpuTimer.Stop(wall, t > 30);
            alloc += GC.GetAllocatedBytesForCurrentThread() - a0;
            sumCpu += cpu;
            if (t > 30)
                worstCpu = Math.Max(worstCpu, cpu);
            ticks++;
            if (g.ServerTick % World.SnapshotEvery == 0)
            {
                var b0 = GC.GetAllocatedBytesForCurrentThread();
                g.WriteZEntsFor(w, view, me.Position);
                alloc += GC.GetAllocatedBytesForCurrentThread() - b0;
                bytes += w.Length;
                biggest = Math.Max(biggest, w.Length);
                if (t > 30)
                    foreach (var z in g.Horde)
                        if (z.Hm.Slot >= 0 && ZFlat(z.Position - me.Position) < Game.ZEntNear * 0.8f)
                        {
                            nearAgeWorst = Math.Max(nearAgeWorst, view.Age[z.Hm.Slot]);
                            nearCount = Math.Max(nearCount, 1);
                        }
            }
        }
        var nearNow = g.Horde.Count(z => ZFlat(z.Position - me.Position) < Game.ZEntNear * 0.8f);
        // The least of a few more identical seconds if this one saw bytes (a one-off runtime allocation can't fail it).
        var tNow = now;
        alloc = RecheckAllocBytes(alloc, () =>
        {
            for (var t = 0; t < 60; t++)
            {
                tNow += World.TickDt;
                g.SelfTestZombiesTick(tNow);
                if (g.ServerTick % World.SnapshotEvery == 0)
                    g.WriteZEntsFor(w, view, me.Position);
            }
        });
        now = tNow;
        var bytesPerSec = bytes / 3.0;
        var meanCpu = sumCpu / ticks;

        // The client: reading the snapshots (a second of them) and posing every zombie it knows.
        var packets = new List<byte[]>();
        for (var t = 0; t < 60; t++)
        {
            now += World.TickDt;
            g.SelfTestZombiesTick(now);
            if (g.ServerTick % World.SnapshotEvery == 0)
            {
                g.WriteZEntsFor(w, view, me.Position);
                packets.Add(w.Span.ToArray());
            }
        }
        var w2 = new PacketWriter();
        g.WriteZWorld(w2);
        var world = w2.Span.ToArray();
        var hostNow = now;
        using var c = ZGame(0, ref now, tweak: r => r.FirstRoundDelay = 9999, onMap: map);
        World.SetMap(map);
        c.SelfTestReadZombies(packets[0], world);
        var readMs = ThreadCpu.TimeMs(() =>
        {
            for (var k = 1; k < packets.Count; k++)
                c.SelfTestReadEnts(packets[k], hostNow);
        }) / Math.Max(1, packets.Count - 1);
        var rec = new DrawRecorder { Keep = false };
        var gfx = new Renderer { Recorder = rec };
        var frame = 0;
        var cam = me.Position + new Vector3(0, 1.6f, 0);
        void Pose()
        {
            frame++;
            foreach (var z in c.Horde)
            {
                c.ZombieAnimInputFor(z, hostNow, out var a);
                var dist = Vector3.Distance(cam, a.Pos);
                var every = ZombieAnim.PoseEvery(dist, c.Horde.Count);
                ZombiePose pose;
                if (every > 1 && (frame + z.Id) % every != 0 && frame - z.AnimPoseFrame < every)
                {
                    pose = z.AnimPose;
                    ZombieAnim.Shift(ref pose, a.Pos - z.AnimPoseAt);
                }
                else
                {
                    ZombieAnim.Targets(a, out var tg);
                    ZombieAnim.Solve(a, tg, out pose);
                    z.AnimPose = pose;
                    z.AnimPoseAt = a.Pos;
                    z.AnimPoseFrame = frame;
                }
                ZombieAnim.Draw(gfx, pose, new Vector3(0.3f), new Vector3(0.4f, 0.5f, 0.3f), new Vector3(0.2f), false, dist > ZombieAnim.LodRange, true);
            }
        }
        Pose();
        rec.Reset();
        var poseMs = ThreadCpu.TimeMs(() =>
        {
            for (var k = 0; k < 20; k++)
                Pose();
        }) / 20;
        var draws = rec.Count / 20.0;
        var soundMs = ThreadCpu.TimeMs(() =>
        {
            for (var k = 0; k < 20; k++)
                c.ZSounds.Frame(c.Audio, c.Horde, 1 / 60f);
        }) / 20;
        var clientMs = readMs * 30 / 60 + poseMs + soundMs; // per 60 Hz frame: half a snapshot read, the posing and drawing, the sounds
        HLine($"horde {count}: {g.AliveZombies} alive, server tick {meanCpu:0.00} ms CPU mean, worst {worstCpu:0.00} ms (Windows thread cycles: {cpuTimer.WorstText}), AI {g.ZAiMs / Math.Max(1, g.ZAiTicks):0.00} ms; "
              + $"{alloc / ticks} bytes allocated per tick; ZEnts {bytesPerSec / 1024:0.0} KB/s per client (biggest {biggest} bytes, the client knows {c.Horde.Count}; {nearNow} within {Game.ZEntNear * 0.8f:0} m at the end, the longest a near one waited {nearAgeWorst} snapshots); "
              + $"client {clientMs:0.00} ms a frame (read {readMs:0.000} ms a snapshot, pose+draw {poseMs:0.00} ms, {draws:0} boxes, sounds {soundMs:0.000} ms); {g.HordeSummary()}");
        if (g.AliveZombies != count)
            Fail($"horde {count}: zombies died or vanished in the scripted horde ({g.AliveZombies} alive)");
        if (alloc > 0)
            Fail($"horde {count}: {alloc} bytes allocated in the steady state (want none per tick)");
        if (TickCpu.Available && cpuTimer.WorstMs > MaxTickWorkMs)
            Fail($"horde {count}: a server tick did {cpuTimer.WorstMs:0.00} ms of work (budget {MaxTickWorkMs} ms): {cpuTimer.WorstDetail}");
        var linuxBudget = count >= 600 ? 6.0 : count >= 300 ? 4.0 : count >= 100 ? 2.0 : 1.0; // mean CPU per tick (a Linux runner is noisy: the slack)
        if (meanCpu > linuxBudget)
            Fail($"horde {count}: the server tick took {meanCpu:0.00} ms of CPU on average (budget {linuxBudget} ms)");
        if (bytesPerSec > 30 * 1024 || biggest > PacketWriter.Mtu)
            Fail($"horde {count}: ZEnts is {bytesPerSec / 1024:0.0} KB/s per client, biggest {biggest} bytes (budget 30 KB/s, one datagram)");
        if (c.Horde.Count < count)
            Fail($"horde {count}: the client knows only {c.Horde.Count} zombies after a second of snapshots");
        if (clientMs > (count >= 600 ? 5.0 : count >= 300 ? 3.0 : 1.5))
            Fail($"horde {count}: the client's zombie work is {clientMs:0.00} ms a frame");
        return $"{count} | {meanCpu:0.00} / {worstCpu:0.00} | {bytesPerSec / 1024:0.0} | {clientMs:0.00}";
    }

    /// <summary>Round 30 with the doors open and the queue full: the horde fills to its cap (300) quickly, spread over the spawners.</summary>
    private static void HordeFill(Map map, ZombiesMap zm, ref double now)
    {
        using var g = ZGame(0, ref now, tweak: r => r.FirstRoundDelay = 9999, onMap: map);
        for (var d = 0; d < zm.Doors.Count; d++)
            g.OpenDoor(d, null);
        var me = g.Local!;
        me.ImmuneUntil = 1e9;
        me.Teleport(FreeSpot(map, zm.Areas[zm.StartArea]));
        g.SelfTestBeginRound(30, now);
        var cap = g.ZAliveCap();
        var seenSpawners = new HashSet<int>();
        var stacked = 0;
        var reached = -1.0;
        double worst = 0, sum = 0;
        var ticks = 0;
        var t0 = now;
        for (var t = 0; t < 60 * 40 && reached < 0; t++)
        {
            now += World.TickDt;
            var before = g.Horde.Count;
            var c0 = ThreadCpu.NowMs();
            g.SelfTestZombiesTick(now);
            var ms = ThreadCpu.NowMs() - c0;
            sum += ms;
            worst = Math.Max(worst, ms);
            ticks++;
            // New zombies this tick: from which spawner, and none on top of another.
            for (var i = before; i < g.Horde.Count; i++)
            {
                var z = g.Horde[i];
                var s = zm.Spawners.FindIndex(sp => ZFlat(sp.At - z.Position) < 2f);
                if (s >= 0)
                    seenSpawners.Add(s);
                for (var j = before; j < i; j++)
                    if (ZFlat(g.Horde[j].Position - z.Position) < 0.5f)
                        stacked++;
            }
            if (g.AliveZombies >= cap)
                reached = now - t0;
        }
        var inside = g.Horde.Count(z => z.Alive && z.Window < 0);
        var queued = g.Horde.Count(z => z.Alive && z.State == ZState.ToWindow && z.Hm.QueueRank >= g.ZRules.Horde.Tearers);
        HLine($"round 30: cap {cap}, {g.AliveZombies} alive after {(reached >= 0 ? $"{reached:0.0} s" : "40 s (not reached)")} ({g.SelfTestToSpawn} still queued), {seenSpawners.Count} of {zm.Spawners.Count} spawners used, "
              + $"{stacked} spawned on top of another; {inside} inside, {queued} queueing behind windows; tick {sum / ticks:0.00} ms CPU mean, worst {worst:0.00} ms; {g.HordeSummary()}");
        if (reached < 0 || reached > 35)
            Fail($"round 30 should fill to its cap of {cap} within 35 s (got {g.AliveZombies})");
        if (stacked > 0 || seenSpawners.Count < 6)
            Fail("spawn bursts should spread over the spawners (none on top of another)");
    }

    // ---- The horde controller ------------------------------------------------------------------------------------

    private static Vector3 HordeSpot(Map map, Vector3 near)
    {
        for (var r = 0f; r < 6f; r += 0.4f)
            for (var k = 0; k < 12; k++)
            {
                var p = near + new Vector3(MathF.Sin(k * 0.5236f) * r, 0, MathF.Cos(k * 0.5236f) * r);
                p.Y = map.GroundHeight(p.X, p.Z);
                if (!World.Blocked(p + new Vector3(0, 0.05f, 0)))
                    return p;
            }
        return near;
    }

    /// <summary>Two players at either end of the Courtyard, 60 zombies nearer one of them: the horde spreads over both (pressure).</summary>
    private static void HordeSpread(Map map, ZombiesMap zm, ref double now)
    {
        using var g = ZGame(0, ref now, tweak: r => r.FirstRoundDelay = 9999, onMap: map);
        for (var d = 0; d < zm.Doors.Count; d++)
            g.OpenDoor(d, null);
        var court = zm.AreaIndex("Courtyard");
        var a = zm.Areas[court];
        var me = g.Local!;
        me.ImmuneUntil = 1e9;
        var mate = g.SelfTestAddHuman();
        g.SelfTestSpawn(mate);
        mate.ImmuneUntil = 1e9;
        me.Teleport(HordeSpot(map, new Vector3(a.X0 + 3, 0, (a.Z0 + a.Z1) / 2)));
        mate.Teleport(HordeSpot(map, new Vector3(a.X1 - 3, 0, (a.Z0 + a.Z1) / 2)));
        HordePlace(g, map, zm, 60, now, 777u, _ => court);
        var mid = (a.X0 + a.X1) / 2;
        foreach (var z in g.Horde)
        {
            var p = z.Position;
            if (p.X > mid)
            {
                p.X = a.X0 + (a.X1 - p.X);
                if (!World.Blocked(p + new Vector3(0, 0.05f, 0)))
                    z.Sim.Position = z.PrevPos = p;
            }
        }
        ZTicks(g, ref now, 1.5);
        var onMe = g.Horde.Count(z => z.Alive && z.Target == me.Id);
        var onMate = g.Horde.Count(z => z.Alive && z.Target == mate.Id);
        HLine($"spreading: 60 zombies starting nearer the host, two players {ZFlat(me.Position - mate.Position):0} m apart: {onMe} after the host, {onMate} after the other (the first {g.ZRules.Horde.PressureFree} free, then {g.ZRules.Horde.PressureWeight} m each)");
        if (onMate < 10 || onMe < 10 || onMe + onMate < 55)
            Fail("a big horde should spread over the players (each gets a share), not all go for the nearest");
    }

    /// <summary>20 zombies at the Receiving Hall's middle window, boarded: they queue on spots behind it, and the back of the queue goes round to the other two windows.</summary>
    private static void HordeWindowSplit(Map map, ZombiesMap zm, ref double now)
    {
        using var g = ZGame(0, ref now, tweak: r => { r.FirstRoundDelay = 9999; r.BoardTearSeconds = 30; }, onMap: map);
        var me = g.Local!;
        me.ImmuneUntil = 1e9;
        me.Teleport(FreeSpot(map, zm.Areas[zm.StartArea]));
        var hall = zm.StartArea;
        var wins = Enumerable.Range(0, zm.Windows.Count).Where(i => zm.Windows[i].Area == hall).ToList();
        var mid = wins.OrderBy(i => MathF.Abs(zm.Windows[i].Outside.X)).First();
        var sp = zm.Spawners.FindIndex(s => s.Window == mid);
        var list = new List<Zombie>();
        for (var i = 0; i < 20; i++)
        {
            var z = g.SelfTestSpawnZombie(sp, now);
            var p = zm.Spawners[sp].At + new Vector3(((i % 5) - 2) * 0.9f, 0, (i / 5) * 0.9f);
            if (!World.Blocked(p + new Vector3(0, 0.05f, 0)))
                z.Sim.Position = z.PrevPos = p;
            list.Add(z);
        }
        var crowdWorst = 0;
        for (var t = 0; t < 60 * 12; t++)
        {
            now += World.TickDt;
            g.SelfTestZombiesTick(now);
            if (t > 60 * 6)
                crowdWorst = Math.Max(crowdWorst, list.Count(z => ZFlat(z.Position - zm.Windows[mid].Outside) < 0.6f));
        }
        var byWindow = wins.Select(wi => list.Count(z => z.Window == wi)).ToArray();
        var waiting = list.Count(z => z.State == ZState.ToWindow && z.Hm.QueueRank >= g.ZRules.Horde.Tearers && ZFlat(z.Position - z.Hm.QueueSpot) < 0.6f);
        var portal = g.HordeAI.Portals[mid];
        HLine($"window queue: 20 zombies at window {mid} ({portal.Spots.Length} queue spots): now {string.Join("/", byWindow)} at windows {string.Join("/", wins)}, {waiting} waiting on their spots, at most {crowdWorst} at the window itself; "
              + $"flanked {g.HordeAI.Flanked}, split out {g.HordeAI.Groups[mid].SplitOut}");
        var others = 0;
        for (var k = 0; k < wins.Count; k++)
            if (wins[k] != mid)
                others += byWindow[k];
        if (g.HordeAI.Flanked < 3 || others < 3)
            Fail("a long window queue should send its back members round to the other windows");
        if (crowdWorst > g.ZRules.Horde.Tearers + 2 || waiting < 3)
            Fail("zombies at a window should queue on spots behind it, not jam the window");
    }

    /// <summary>Gunfire, a blast, a down and a lure, each with its group's reaction.</summary>
    private static void HordeStimuli(Map map, ZombiesMap zm, ref double now)
    {
        using var g = ZGame(0, ref now, tweak: r => r.FirstRoundDelay = 9999, onMap: map);
        for (var d = 0; d < zm.Doors.Count; d++)
            g.OpenDoor(d, null);
        var me = g.Local!;
        me.ImmuneUntil = 1e9;
        var mate = g.SelfTestAddHuman();
        g.SelfTestSpawn(mate);
        mate.ImmuneUntil = 1e9;
        var west = zm.Areas[zm.AreaIndex("West Offices")];
        var court = zm.AreaIndex("Courtyard");
        var ca = zm.Areas[court];
        me.Teleport(HordeSpot(map, new Vector3(west.X0 + 2, 0, (west.Z0 + west.Z1) / 2))); // the host far west
        mate.Teleport(HordeSpot(map, new Vector3(ca.X1 - 3, 0, (ca.Z0 + ca.Z1) / 2))); // the mate in the courtyard's east end
        // 30 zombies in the courtyard's east half, all after the host (far away), within earshot of the mate.
        HordePlace(g, map, zm, 30, now, 4242u, _ => court);
        foreach (var z in g.Horde)
        {
            var p = z.Position;
            p.X = MathF.Abs(p.X) + 1f;
            p = HordeSpot(map, p);
            z.Sim.Position = z.PrevPos = p;
        }
        for (var k = 0; k < 4; k++)
        {
            now += World.TickDt;
            g.SelfTestZombiesTick(now);
            var hostSlot = g.HordeAI.SlotOf(me.Id);
            foreach (var z in g.Horde) // (scripted: everyone starts after the host)
                if (hostSlot >= 0)
                {
                    z.Hm.Group = g.HordeAI.TargetGroup(hostSlot);
                    z.Hm.TargetSlot = hostSlot;
                    z.Hm.GroupSince = now;
                }
        }
        // Gunfire from the mate: the ones in earshot (and not fighting) turn on them.
        var heard = g.Horde.Count(z => ZFlat(z.Position - mate.Position) < g.ZRules.Horde.HearGunfire);
        g.HordeAI.Stimulus(HordeStimulusKind.Gunfire, mate.Position, 0, mate.Id, now);
        ZTicks(g, ref now, 0.1);
        var turned = g.Horde.Count(z => z.Target == mate.Id);
        var turns = g.HordeAI.Turned;
        ZTicks(g, ref now, 2.0);
        var mine = g.Horde.Where(z => z.Target == mate.Id).ToList();
        var closer = mine.Count > 0 ? mine.Average(z => (double)ZFlat(z.Position - mate.Position)) : 99;

        // A blast in the middle of them: the ones near it scatter away, then their group regroups and goes on.
        var centre = g.Horde.Where(z => z.Alive).Select(z => z.Position).Aggregate(Vector3.Zero, (x, y) => x + y) / Math.Max(1, g.Horde.Count);
        var nearBlast = g.Horde.Where(z => ZFlat(z.Position - centre) < 4f).ToList();
        var d0 = nearBlast.Count > 0 ? nearBlast.Average(z => (double)ZFlat(z.Position - centre)) : 0;
        g.SelfTestBlast(mate, centre + new Vector3(0, 0.3f, 0), 3f, 1f, now);
        ZTicks(g, ref now, g.ZRules.Horde.ExplosionStagger + g.ZRules.Horde.ScatterSeconds); // staggered, then away
        var d1 = nearBlast.Count > 0 ? nearBlast.Average(z => (double)ZFlat(z.Position - centre)) : 0;
        var gi = g.HordeAI.TargetGroup(Math.Max(0, g.HordeAI.SlotOf(mate.Id)));
        var regrouping = g.HordeAI.Groups[gi].Mode == HordeGroupMode.Regroup;
        ZTicks(g, ref now, 3.0);
        var advancing = g.HordeAI.Groups[gi].Mode == HordeGroupMode.Advance;

        // The mate goes down: everyone after them goes for the host.
        var onMate = g.Horde.Count(z => z.Target == mate.Id);
        g.SelfTestDown(mate, now);
        ZTicks(g, ref now, g.ZRules.ZombieWindup + 0.2); // (a swipe already under way lands first)
        var afterDown = g.Horde.Count(z => z.Target == mate.Id);
        var alive = g.Horde.Count(z => z.Alive);
        var onHost = g.Horde.Count(z => z.Target == me.Id);

        // A lure in the courtyard: the ones in range go to it and stay round it, then back to the host when it's over.
        var lure = HordeSpot(map, new Vector3((ca.X0 + ca.X1) / 2, 0, (ca.Z0 + ca.Z1) / 2));
        g.HordeLure(lure, 30f, 4.0, now);
        ZTicks(g, ref now, 3.5);
        var atLure = g.Horde.Count(z => ZFlat(z.Position - lure) < 4f);
        var lured = g.HordeAI.Lured;
        ZTicks(g, ref now, 1.5);
        var backOnHost = g.Horde.Count(z => z.Target == me.Id);
        HLine($"stimuli: gunshot heard by {heard} of {g.Horde.Count} -> {turned} turned on the shooter ({turns} turns), 2 s later {closer:0.0} m from them on average; "
              + $"blast: {nearBlast.Count} near it went from {d0:0.0} m to {d1:0.0} m away (scattered {g.HordeAI.Scattered}), group regrouping {regrouping}, advancing again 3 s later {advancing}; "
              + $"down: {onMate} on the downed player -> {afterDown}, {onHost} of {alive} on the host; lure: {lured} lured, {atLure} within 4 m of it after 3.5 s, {backOnHost} back on the host after it ended");
        if (turned < heard / 2 || turned == 0 || closer > 12)
            Fail("a gunshot should turn the zombies in earshot (not fighting) on the shooter");
        if (nearBlast.Count < 2 || d1 < d0 + 0.5 || !regrouping || !advancing)
            Fail("a blast should scatter the zombies near it, then their group regroups and goes on");
        if (afterDown != 0 || onHost < alive - 2)
            Fail("when a player goes down, the zombies after them should go for someone else");
        if (lured < 10 || atLure < 8 || backOnHost < alive - 2)
            Fail("a lure should draw the zombies in range to it, and they go back to the players when it ends");
    }

    /// <summary>A group shut in an area whose doors are all closed, its target out of reach: it regroups instead of grinding on.</summary>
    private static void HordeBlocked(Map map, ZombiesMap zm, ref double now)
    {
        using var g = ZGame(0, ref now, tweak: r => r.FirstRoundDelay = 9999, onMap: map);
        var me = g.Local!;
        me.ImmuneUntil = 1e9;
        me.Teleport(FreeSpot(map, zm.Areas[zm.StartArea]));
        var lab = zm.AreaIndex("Research Lab");
        HordePlace(g, map, zm, 12, now, 99u, _ => lab);
        var trace = new List<string>();
        for (var k = 0; k < 24; k++)
        {
            ZTicks(g, ref now, 0.5);
            var tg = g.HordeAI.Groups[g.HordeAI.TargetGroup(Math.Max(0, g.HordeAI.SlotOf(me.Id)))];
            if (k % 2 == 1)
                trace.Add($"{tg.MeanDist:0.0}{(tg.Mode == HordeGroupMode.Regroup ? "R" : "")}");
        }
        var gi = g.HordeAI.TargetGroup(Math.Max(0, g.HordeAI.SlotOf(me.Id)));
        HLine($"blocked: 12 zombies shut in the {zm.Areas[lab].Name} (every door shut), the host out of reach: the group regrouped {g.HordeAI.Groups[gi].Regroups} time(s) in 12 s (mean distance each second: {string.Join(" ", trace)}; R regrouping)");
        if (g.HordeAI.Groups[gi].Regroups < 1)
            Fail("a group getting nowhere should regroup");
    }

    /// <summary>The same scripted horde twice: the same positions, groups and targets.</summary>
    private static void HordeDeterminism(Map map, ZombiesMap zm)
    {
        string Run()
        {
            var t = 0.0;
            using var g = ZGame(0, ref t, tweak: r => r.FirstRoundDelay = 9999, onMap: map);
            for (var d = 0; d < zm.Doors.Count; d++)
                g.OpenDoor(d, null);
            var me = g.Local!;
            me.ImmuneUntil = 1e9;
            me.Teleport(FreeSpot(map, zm.Areas[zm.StartArea]));
            HordePlace(g, map, zm, 150, t, 31337u);
            g.SelfTestBeginRound(25, t);
            for (var k = 0; k < 60 * 6; k++)
            {
                t += World.TickDt;
                g.SelfTestZombiesTick(t);
                if (k == 120)
                    g.HordeAI.Stimulus(HordeStimulusKind.Gunfire, me.Position, 0, me.Id, t);
            }
            var h = 17L;
            foreach (var z in g.Horde)
                h = h * 31 + z.Id * 7 + (long)(z.Position.X * 100) * 3 + (long)(z.Position.Z * 100) + z.Hm.Group * 11 + z.Window;
            return $"{g.Horde.Count}/{g.HordeAI.Flow.Built}/{h:x}";
        }
        var a = Run();
        var b = Run();
        HLine($"determinism: {a} vs {b}");
        if (a != b)
            Fail("the same scripted horde played out differently");
    }

    // ---- Over the network -------------------------------------------------------------------------------------------

    /// <summary>A loopback host with 300 zombies and a client with 80 ms of lag and 3 percent loss: it knows them all, the near ones fresh.</summary>
    private static void HordeLoopback(Map map, ZombiesMap zm)
    {
        const ushort port = 60271;
        var clock = Stopwatch.StartNew();
        var now = 0.0;
        World.SetMap(map);
        using var server = new Game(NetPeer.Host(port, Game.MaxPlayers - 1), new Audio(), "Host", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: map.Id)
        {
            MapVoteEnabled = false,
            LobbyEnabled = true,
            WarmupEnabled = true,
        };
        server.ZRules = ZombiesRules.Load(File.ReadAllText(ZombiesRules.PathName), "test rules");
        server.ZRules.FirstRoundDelay = 9999;
        server.ZSeed = 7;
        server.Mode = GameMode.Zombies;
        using var a = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Alpha", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        Pump(server, [a], 1.0, ref now, clock);
        server.LobbyStart(true, now);
        PumpFast(server, [a], Game.LobbyCountdownSeconds + 0.5, ref now);
        server.ForceStart();
        PumpFast(server, [a], Game.CountdownSeconds + 0.7, ref now);
        if (server.Phase != MatchPhase.Playing || a.Phase != MatchPhase.Playing)
        {
            Fail($"the loopback Zombies match didn't start (server {server.Phase}, client {a.Phase})");
            return;
        }
        var sim = server.Net.SimulateBadNetwork(80, 10, 3f);
        foreach (var p in server.Players.Values)
            p.ImmuneUntil = 1e9;
        for (var d = 0; d < zm.Doors.Count; d++)
            server.OpenDoor(d, null);
        HordePlace(server, map, zm, 300, now, 2024u);
        var read0 = a.ZEntsRead;
        var bytes0 = server.ZEntsBytesSent;
        var packets0 = server.ZEntsPacketsSent;
        var t0 = now;
        const double dt = 1.0 / 60;
        var nearStaleWorst = 0.0;
        var clientFrameMs = 0.0;
        var frames = 0;
        var wall0 = clock.Elapsed.TotalSeconds;
        for (var t = 0.0; t < 4.0; t += dt)
        {
            while (clock.Elapsed.TotalSeconds - wall0 < t)
                Thread.Sleep(1); // real time: the network simulator's lag and loss are in wall-clock time
            now += dt;
            server.Update(now, dt);
            var c0 = ThreadCpu.NowMs();
            a.Update(now, dt);
            clientFrameMs += ThreadCpu.NowMs() - c0;
            frames++;
            if (t > 2.0 && t - dt <= 2.0)
            {
                a.ZEntsGapWorst = 0;
                server.ZEntsNearPeak = server.ZEntsNearWaitWorst = 0;
            }
            if (t > 2.0 && a.Local is { } me)
                foreach (var z in a.Horde)
                    if (z.Alive && ZFlat(z.Position - me.Position) < Game.ZEntNear * 0.8f)
                        nearStaleWorst = Math.Max(nearStaleWorst, now - z.LastHeard);
        }
        var seconds = now - t0;
        var bytesPerSec = (server.ZEntsBytesSent - bytes0) / seconds;
        // The near zombies' freshness. This horde chases the host, so ~150-180 zombies end up within ZEntNear: more than
        // the ZEntsMax that fit in one ZEnts, so they can't all go in every snapshot; they share the places by age (each
        // in about every third snapshot, 100 ms, on 2026-10-28). Measured on the server, without the network, the near
        // ones must take at least two thirds of the places (the 8x weight; at least every other snapshot). On the
        // client, a lost packet costs a near zombie a whole share cycle (until its next turn), not one snapshot: two of its
        // packets lost in the 2 s window happen in ~1 run in 10 at 3% loss (that was the old flake, "one went 283-300 ms
        // without" against a limit of the longest gap + 200 ms), three in ~1 in 300. So the client may hear of a near
        // zombie as late as three cycles (or the longest gap between packets plus two).
        var nearWaitAllowed = Math.Max(2, (int)Math.Ceiling(server.ZEntsNearPeak / (Game.ZEntsMax * 2.0 / 3.0)));
        var cycle = Math.Max(1, server.ZEntsNearWaitWorst) * World.SnapshotEvery * World.TickDt;
        var nearStaleAllowed = Math.Max(a.ZEntsGapWorst, cycle) + 2 * cycle + 0.05; // (+50 ms: the 10 ms jitter and a frame)
        var known = a.Horde.Count(z => z.Alive);
        var serverAlive = server.AliveZombies;
        HLine($"loopback (80 ms lag, 3% loss, simulator {(sim ? "on" : "unavailable")}): server {serverAlive} alive, the client knows {known}; ZEnts {bytesPerSec / 1024:0.0} KB/s to the client "
              + $"({(server.ZEntsPacketsSent - packets0) / seconds:0} packets/s sent, {(a.ZEntsRead - read0) / seconds:0} read; the longest gap between two {a.ZEntsGapWorst * 1000:0} ms), "
              + $"{server.ZEntsNearPeak} zombies within {Game.ZEntNear:0} m, the server sent each near one at least every {server.ZEntsNearWaitWorst} snapshot(s) (allowed {nearWaitAllowed}), "
              + $"the client heard of each at most {nearStaleWorst * 1000:0} ms apart (allowed {nearStaleAllowed * 1000:0}); client update {clientFrameMs / frames:0.000} ms a frame");
        if (known < serverAlive - 5)
            Fail($"a loopback client should know the whole horde ({known} of {serverAlive})");
        if (bytesPerSec > 30 * 1024)
            Fail($"ZEnts to one client is {bytesPerSec / 1024:0.0} KB/s (budget 30 KB/s)");
        if (server.ZEntsNearPeak == 0)
            Fail("no zombie came near the loopback client (the near-zombie checks measured nothing)");
        if (server.ZEntsNearWaitWorst > nearWaitAllowed)
            Fail($"near zombies should take most of each ZEnts: one waited {server.ZEntsNearWaitWorst} snapshots with {server.ZEntsNearPeak} near (allowed {nearWaitAllowed})");
        if (nearStaleWorst > nearStaleAllowed)
            Fail($"near zombies should be heard of every share cycle (one went {nearStaleWorst * 1000:0} ms without; allowed {nearStaleAllowed * 1000:0}: three {cycle * 1000:0} ms cycles, or the longest gap {a.ZEntsGapWorst * 1000:0} ms plus two)");
    }
}
