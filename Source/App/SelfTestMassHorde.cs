using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Self-test section "Mass horde" (docs/zombies/MASS-HORDE.md, the spike): Mass Horde Test's crowd of 5,000, 10,000 and
/// 20,000 zombies for 4 seconds each (short and seeded): the server tick's CPU mean and worst (Windows thread cycles:
/// every count must stay under the 8 ms budget), the CPU a frame spends preparing the crowd's instances, allocations, memory, Msg.ZCrowd's bytes per client (per-cell
/// counts and headings) read into a client, promotion and demotion (the player walks off: far individuals go back to the crowd, near particles
/// come out), counts adding up every tick (crowd + alive + killed = the round's total) with kills from shots, the
/// same seed giving the same crowd, and the crowd tier off on every other map.
/// </summary>
public static partial class SelfTest
{
    private static readonly List<string> MassNotes = new();

    /// <summary>The CPU a frame may spend sorting the crowd into instances (Windows; the GPU does the rest).</summary>
    private const double CrowdPrepBudgetMs = 4;

    private static void MassLine(string s)
    {
        Line(s);
        MassNotes.Add(s);
    }

    private static void CheckMassHorde()
    {
        MassNotes.Clear();
        var now = 0.0;
        if (Maps.Find("Mass Horde Test") is not { Zombies: { Crowd: > 0 } zm } map)
        {
            Fail("no Mass Horde Test map (or no \"crowd\" in its zombies section)");
            return;
        }
        MassOff(ref now);
        var rows = new List<string>();
        foreach (var n in new[] { 5000, 10000, 20000 })
            rows.Add(MassMeasure(map, zm, n, ref now));
        MassLine("table (crowd | server tick ms CPU mean / worst (Windows) | crowd ms | ZCrowd KB/s per client | MB): " + string.Join("; ", rows));
        MassDeterminism(map, ref now);
        MassRigs();
        MassCombat(map, ref now);
        MassSurface(map, ref now);
        Line("::warning title=Mass horde (results)::" + string.Join("%0A", MassNotes.Select(n => n.Replace("%", "%25").Replace("\r", "").Replace("\n", " "))));
    }

    /// <summary>Every other map: the crowd tier never runs (and the round flow is the old one).</summary>
    private static void MassOff(ref double now)
    {
        if (Maps.Find("Blackmoor Asylum") is not { Zombies: { } zm } map)
            return;
        using var g = ZGame(0, ref now, tweak: r => r.FirstRoundDelay = 9999, onMap: map);
        g.SelfTestBeginRound(5, now);
        var total = g.SelfTestRoundTotal;
        ZTicks(g, ref now, 2.0);
        MassLine($"off: Blackmoor round 5: crowd on {g.CrowdOn}, crowd ticks {g.Crowd.Ticks}, particles {g.Crowd.Count}, round total {total}, {g.AliveZombies} alive, {g.SelfTestToSpawn} to spawn");
        if (g.CrowdOn || g.Crowd.Ticks != 0 || g.Crowd.Count != 0 || g.AliveZombies == 0 || g.SelfTestRoundTotal != total)
            Fail("the crowd tier should stay off on maps without \"crowd\" (normal spawning, no crowd ticks)");
    }

    private static string MassMeasure(Map map, ZombiesMap zm, int count, ref double clock)
    {
        var now = clock;
        var mem0 = GC.GetTotalMemory(true);
        using var g = ZGame(0, ref now, tweak: r =>
        {
            r.FirstRoundDelay = 9999;
            r.Crowd.PaceByRound = false; // (the fixed pace these numbers were measured at; "Horde pace" checks the round's pace)
        }, onMap: map);
        var me = g.Local!;
        me.ImmuneUntil = 1e9;
        me.Teleport(new Vector3(0, 0, 0));
        g.CrowdOverride = count;
        g.SelfTestBeginRound(1, now);
        var seeded = g.Crowd.Total;
        var mem = GC.GetTotalMemory(true) - mem0;
        var cpuTimer = new TickCpu();
        double sumCpu = 0, worstCpu = 0;
        var ticks = 0;
        var kills = 0;
        var bad = 0;
        var badText = "";
        long alloc = 0;
        var maxAlive = 0;
        // The network (Msg.ZCrowd, protocol 71): what one client at the player gets, recorded to replay into a client.
        var (cols, rows) = g.Crowd.DensitySize(g.ZRules.Crowd.NetCell);
        var view = new Game.CrowdNetView();
        var cw = new PacketWriter(32 * 1024);
        var packets = new List<byte[]>();
        long netBytes = 0;
        var keyframe = 0;
        var biggest = 0;
        double netMs = 0;
        var netSends = 0;
        long netAlloc = 0;
        var lastSum = 0;
        var serverAtSend = 0;
        void Check(string when)
        {
            var sum = g.Crowd.Total + g.AliveZombies + g.CrowdRoundKills;
            if (sum != g.CrowdRoundTotal || g.CrowdRoundKills < kills)
            {
                bad++;
                if (badText.Length == 0)
                    badText = $"{when}: crowd {g.Crowd.Count} + mass {g.Crowd.MassTotal} + alive {g.AliveZombies} + killed {g.CrowdRoundKills} (the test shot {kills}) != {g.CrowdRoundTotal}";
            }
        }
        void Tick(int t, bool measure)
        {
            now += World.TickDt;
            me.Yaw += MathF.Tau / 4 * World.TickDt; // (the player looks round, a full turn in 4 s: the crowd wakes by sight, MASS-HORDE.md "Waking and sleeping")
            var a0 = GC.GetAllocatedBytesForCurrentThread();
            cpuTimer.Start();
            var c0 = ThreadCpu.NowMs();
            var t0 = Stopwatch.GetTimestamp();
            g.SelfTestZombiesTick(now);
            var wall = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
            var cpu = ThreadCpu.NowMs() - c0;
            cpuTimer.Stop(wall, measure);
            if (measure)
            {
                alloc += GC.GetAllocatedBytesForCurrentThread() - a0;
                sumCpu += cpu;
                worstCpu = Math.Max(worstCpu, cpu);
                ticks++;
            }
            maxAlive = Math.Max(maxAlive, g.AliveZombies);
            Check($"tick {t}");
            // Shots: every half second the nearest few individuals die (kill credit as ever).
            if (t % 30 == 15)
            {
                var shot = 0;
                foreach (var z in g.Horde)
                    if (z.Alive && shot < 4 && ZFlat(z.Position - me.Position) < 25f)
                    {
                        g.DamageZombie(me, z, 1_000_000, true, false, now);
                        kills++;
                        shot++;
                    }
                Check($"after shots at tick {t}");
            }
            if (g.ServerTick % g.ZRules.Crowd.NetEvery == 0)
            {
                var b0 = GC.GetAllocatedBytesForCurrentThread();
                var n0 = ThreadCpu.NowMs();
                g.CrowdCount();
                g.WriteZCrowdFor(cw, view, me.Position);
                netMs += ThreadCpu.NowMs() - n0;
                netSends++;
                if (packets.Count > 0)
                    netAlloc += GC.GetAllocatedBytesForCurrentThread() - b0; // (the first: the view's arrays)
                if (packets.Count == 0)
                    keyframe = cw.Length;
                else if (measure)
                {
                    netBytes += cw.Length;
                    biggest = Math.Max(biggest, cw.Length);
                }
                packets.Add(cw.Span.ToArray());
                lastSum = 0;
                foreach (var c in view.Count)
                    lastSum += c;
                serverAtSend = g.Crowd.Total;
            }
        }
        // 1 s warm-up (the crowd closes in, the first promotions, JIT), then 3 s measured.
        for (var t = 0; t < 60; t++)
            Tick(t, false);
        for (var t = 60; t < 240; t++)
            Tick(t, true);
        var promoted = g.Crowd.Promoted;
        var demoted0 = g.Crowd.Demoted;
        var near = g.Crowd.Near;
        var alive = g.AliveZombies;
        var crowdMs = g.Crowd.TickMs / Math.Max(1, g.Crowd.Ticks);
        var flow = $"{g.Crowd.FlowBuilds} flow fields, {g.Crowd.FlowMs / Math.Max(1, g.Crowd.FlowBuilds):0.00} ms each over several ticks (the most in one tick {g.Crowd.FlowWorstMs:0.00} ms)";
        var meanCpu = sumCpu / Math.Max(1, ticks);
        var kbs = netBytes / 3.0 / 1024;
        var bytes = g.Crowd.Bytes;
        // Steady state: a one-off growth (the zombie body grid's first build when the first ones arrive) can't fail it.
        alloc = RecheckAllocBytes(alloc, () =>
        {
            for (var k = 0; k < 60; k++)
            {
                now += World.TickDt;
                g.SelfTestZombiesTick(now);
            }
        });

        // The player walks off (10,000 only): the individuals left behind go back to the crowd, near particles come out.
        var walk = "";
        if (count == 10000)
        {
            me.Teleport(new Vector3(60, 0, -15));
            var d0 = g.Crowd.Demoted;
            var p0 = g.Crowd.Promoted;
            for (var t = 240; t < 240 + 60 * 4; t++)
                Tick(t, false);
            var farLeft = g.Horde.Count(z => z.Alive && ZFlat(z.Position - me.Position) > g.Crowd.Tuning.DemoteRange + 10);
            walk = $"; walked 60 m off: {g.Crowd.Demoted - d0} demoted, {g.Crowd.Promoted - p0} promoted in 4 s, {farLeft} individuals still over {g.Crowd.Tuning.DemoteRange + 10:0} m away, {g.AliveZombies} alive";
            if (g.Crowd.Demoted - d0 < 20 || g.Crowd.Promoted - p0 < 20)
                Fail($"crowd {count}: walking off should send the far individuals back to the crowd and bring near particles out ({walk})");
        }
        // The client: every message read in order; its picture must hold what the server's record says it was sent.
        var t1 = now;
        using var c = ZGame(0, ref t1, tweak: r => r.FirstRoundDelay = 9999, onMap: map);
        var readMs = ThreadCpu.TimeMs(() =>
        {
            foreach (var pk in packets)
                c.SelfTestReadCrowd(pk);
        }) / Math.Max(1, packets.Count);
        var seen = c.CrowdSeen.Total;
        double hostPrepMs, clientPrepMs;
        var draw = "";
        // Drawing (CPU side): this frame's instances, every crowd zombie in view (no frustum, no fog: the worst case), by
        // level of detail, from the host's particles and mass and from the client's picture (CrowdView: the persistent
        // agents); nothing allocated once warm. Timed in one stripe on this thread (CrowdJobs.Serial): the whole CPU cost.
        ZombieAnim.ClearFrustum();
        var hostView = new CrowdView();
        var clientView = new CrowdView();
        var cam = me.Position + new Vector3(0, 1.6f, 0);
        var pt = now;
        void HostPrep() => hostView.Prepare(g, cam, pt += 1 / 60.0, 0);
        void ClientPrep() => clientView.Prepare(c, cam, pt += 1 / 60.0, 0);
        HostPrep();
        ClientPrep();
        CrowdJobs.Serial = true;
        try
        {
            HostPrep();
            ClientPrep();
            var prepAlloc = GC.GetAllocatedBytesForCurrentThread();
            HostPrep();
            ClientPrep();
            prepAlloc = GC.GetAllocatedBytesForCurrentThread() - prepAlloc;
            hostPrepMs = ThreadCpu.TimeMs(() =>
            {
                for (var k = 0; k < 5; k++)
                    HostPrep();
            }) / 5;
            clientPrepMs = ThreadCpu.TimeMs(() =>
            {
                for (var k = 0; k < 5; k++)
                    ClientPrep();
            }) / 5;
            var hi = hostView.Inst;
            var ci = clientView.Inst;
            draw = $"; drawing prep {hostPrepMs:0.00} ms host ({hi.Count[0]}/{hi.Count[1]}/{hi.Count[2]} near/mid/far, {hi.Particles} particles), {clientPrepMs:0.00} ms client ({ci.Count[0]}/{ci.Count[1]}/{ci.Count[2]}), {prepAlloc} bytes allocated";
            if (prepAlloc > 0)
                Fail($"crowd {count}: preparing the crowd's instances allocated {prepAlloc} bytes once warm");
            // Every zombie in the client's picture, up to its cell's cap (by distance), stands as an agent.
            var capped = 0;
            var pic = c.CrowdSeen;
            var ag = clientView.Client;
            for (var k = 0; k < pic.Count.Length; k++)
            {
                if (pic.Count[k] == 0)
                    continue;
                float cx = pic.MinX + (k % pic.Cols + 0.5f) * pic.Cell - cam.X, cz = pic.MinZ + (k / pic.Cols + 0.5f) * pic.Cell - cam.Z;
                capped += Math.Min(pic.Count[k], ag.CapAt(MathF.Sqrt(cx * cx + cz * cz)));
            }
            if (ci.Agents != capped)
                Fail($"crowd {count}: the client's instances ({ci.Agents}) should be every zombie in its picture up to its cell's cap ({capped} of {pic.Total}) with no frustum");
        }
        finally
        {
            CrowdJobs.Serial = false;
        }
        World.SetMap(map);
        var net = $"ZCrowd {kbs:0.0} KB/s per client ({netSends} sends, every {g.ZRules.Crowd.NetEvery} ticks, {g.ZRules.Crowd.NetCell:0} m cells {cols}x{rows}; keyframe {keyframe} bytes, biggest change {biggest} bytes; "
                  + $"server {netMs / Math.Max(1, netSends):0.000} ms a send, {netAlloc} bytes allocated); client read {readMs:0.000} ms a message, picture {seen} zombies (sent {lastSum})";
        if (seen != lastSum || c.CrowdSeen.Reads != packets.Count)
            Fail($"crowd {count}: the client's picture should match what it was sent ({seen} vs {lastSum}, {c.CrowdSeen.Reads} of {packets.Count} messages read)");
        if (netAlloc > 0)
            Fail($"crowd {count}: writing Msg.ZCrowd allocated {netAlloc} bytes (want none)");
        if (Math.Abs(lastSum - serverAtSend) > serverAtSend / 50 + 50) // (far cells lag a send or two)
            Fail($"crowd {count}: the last ZCrowd sent {lastSum} crowd zombies, the server had {serverAtSend}");
        MassLine($"crowd {count}: seeded {seeded} ({bytes / 1024} KB of crowd arrays, {mem / 1024 / 1024.0:0.0} MB managed for the game and crowd); server tick {meanCpu:0.00} ms CPU mean, worst {worstCpu:0.00} ms (Windows thread cycles: {cpuTimer.WorstText}), crowd {crowdMs:0.00} ms a tick, {flow}; "
              + $"{alloc} bytes allocated in the steady state; individuals {alive} (most {maxAlive}, cap {g.CrowdIndividualCap}), {near} held near, {promoted} promoted, {demoted0} demoted, {g.CrowdRoundKills} killed ({kills} by the test's shots), counts off on {bad} ticks{(badText.Length > 0 ? " (" + badText + ")" : "")}; "
              + $"{net}{draw}{walk}");
        if (seeded != count)
            Fail($"crowd {count}: only {seeded} seeded");
        if (alloc > 0)
            Fail($"crowd {count}: {alloc} bytes allocated in the steady state (want none per tick)");
        if (bad > 0)
            Fail($"crowd {count}: the counts didn't add up on {bad} ticks: {badText}");
        if (promoted < count / 100 || maxAlive < count / 100)
            Fail($"crowd {count}: the crowd should reach the player and fill the individual tier ({promoted} promoted, at most {maxAlive} alive)");
        if (maxAlive > g.CrowdIndividualCap)
            Fail($"crowd {count}: {maxAlive} individuals, over the cap {g.CrowdIndividualCap}");
        if (TickCpu.Available && cpuTimer.WorstMs > MaxTickWorkMs)
            Fail($"crowd {count}: a server tick did {cpuTimer.WorstMs:0.00} ms of work (budget {MaxTickWorkMs} ms): {cpuTimer.WorstDetail}");
        if (TickCpu.Available && Math.Max(hostPrepMs, clientPrepMs) > CrowdPrepBudgetMs)
            Fail($"crowd {count}: preparing a frame's crowd instances took {hostPrepMs:0.00} ms on the host, {clientPrepMs:0.00} ms on a client (budget {CrowdPrepBudgetMs} ms of CPU)");
        clock = now;
        return $"{count} | {meanCpu:0.00} / {cpuTimer.WorstText} | {crowdMs:0.00} | {kbs:0.0} | {bytes / 1024 / 1024.0:0.0}";
    }

    /// <summary>
    /// Combat against the crowd: shots into the crowd past the front hurt and kill crowd zombies (kill credit and points as
    /// for individuals); a blast in the crowd takes out every crowd zombie its damage kills, by count, each a kill for the
    /// thrower, with only a few drops rolled; the counts add up throughout.
    /// </summary>
    private static void MassCombat(Map map, ref double now)
    {
        using var g = ZGame(0, ref now, tweak: r => r.FirstRoundDelay = 9999, onMap: map);
        var me = g.Local!;
        me.ImmuneUntil = 1e9;
        me.Teleport(Vector3.Zero);
        g.CrowdOverride = 3000;
        g.SelfTestBeginRound(1, now);
        g.SelfTestZombiesTick(now += World.TickDt); // (the crowd's grid)
        var zp = g.ZP(me);
        var kills0 = zp.Kills;
        var points0 = zp.Points;
        var c = g.Crowd;
        // Shots: at the 20 crowd zombies nearest the player, from the eye at their heads, four trigger pulls each.
        var order = Enumerable.Range(0, c.Count).OrderBy(i => c.X[i] * c.X[i] + c.Z[i] * c.Z[i]).Take(20).Select(i => new Vector3(c.X[i], c.Y + 1.65f, c.Z[i])).ToList();
        foreach (var target in order)
            for (var k = 0; k < 4; k++)
            {
                var eye = me.Eye;
                now += 0.5; // (the gun's fire rate: the crowd doesn't move, nothing ticks)
                g.SelfTestServerShot(me, eye, Vector3.Normalize(target - eye), now);
            }
        var shotKills = g.CrowdShotKills;
        var afterShots = c.Count;
        var sumOk = c.Total + g.AliveZombies + g.CrowdRoundKills == g.CrowdRoundTotal;
        // A blast in the thick of the crowd, far from the player.
        var far = Enumerable.Range(0, c.Count).OrderByDescending(i => c.X[i] * c.X[i] + c.Z[i] * c.Z[i]).First();
        var at = new Vector3(c.X[far] * 0.6f, c.Y + 0.5f, c.Z[far] * 0.6f);
        Span<int> inR = stackalloc int[512];
        var inRange = c.Within(at, 3f, inR);
        var before = c.Total;
        var drops0 = g.CrowdDropRolls;
        g.SelfTestBlast(me, at, 4f, 100000f, now);
        var blastKills = before - c.Total;
        var drops = g.CrowdDropRolls - drops0;
        // A blast out in the mass tier (a mass cell far from the player): its dead leave the mass by count.
        var massCell = Array.FindIndex(c.MassCount, m => m >= 3);
        var massBefore = c.MassTotal;
        var massKills0 = g.CrowdMassKills;
        if (massCell >= 0)
            g.SelfTestBlast(me, c.MassCentre(massCell) + new Vector3(0, 0.5f, 0), 3f, 100000f, now);
        var massKills = g.CrowdMassKills - massKills0;
        blastKills += massKills;
        sumOk &= c.Total + g.AliveZombies + g.CrowdRoundKills == g.CrowdRoundTotal;
        sumOk &= c.Total + g.AliveZombies + g.CrowdRoundKills == g.CrowdRoundTotal;
        var credited = zp.Kills - kills0;
        MassLine($"combat: 80 head shots into the crowd killed {shotKills} ({afterShots} particles left, the rest in the mass); a blast took {blastKills - massKills} out of the crowd ({inRange} within 3 m), {drops} drops rolled; a blast in the mass killed {massKills} ({massBefore} -> {c.MassTotal} in the mass); "
                 + $"kills credited {credited} (crowd round kills {g.CrowdRoundKills}), points {points0} -> {zp.Points}; counts add up {sumOk}");
        if (shotKills < 5)
            Fail($"shots into the crowd should kill crowd zombies ({shotKills} of 20 targets)");
        if (massCell < 0 || massKills < 1 || massBefore - c.MassTotal != massKills)
            Fail($"a blast in the mass tier should kill mass zombies by count ({massKills} killed, mass {massBefore} -> {c.MassTotal})");
        if (blastKills - massKills < inRange || blastKills == 0)
            Fail($"a blast should kill every crowd zombie close to it ({blastKills} out, {inRange} within 3 m)");
        if (credited != g.CrowdRoundKills || credited != shotKills + blastKills || zp.Points <= points0)
            Fail($"crowd kills should be credited like individual kills ({credited} credited, {shotKills} + {blastKills} killed, {g.CrowdRoundKills} counted)");
        if (drops > Game.CrowdBlastDrops)
            Fail($"a blast should roll at most {Game.CrowdBlastDrops} drops for its crowd kills ({drops})");
        if (!sumOk)
            Fail("the counts should add up after shots and blasts in the crowd");
    }

    /// <summary>
    /// The crowd as ground (ZombiesCrowdSurface.cs): a player put on the floor in the densest cell is lifted onto the crowd
    /// and stands there; one on the crowd's edge slides down its slope towards thinner crowd; a client's picture gives the
    /// same heights as the server's counts; nothing is allocated; off the crowd the surface does nothing.
    /// </summary>
    private static void MassSurface(Map map, ref double now)
    {
        using var g = ZGame(0, ref now, tweak: r => r.FirstRoundDelay = 9999, onMap: map);
        var me = g.Local!;
        me.ImmuneUntil = 1e9;
        me.Teleport(Vector3.Zero);
        g.CrowdOverride = 2000;
        g.SelfTestBeginRound(1, now);
        for (var t = 0; t < 30; t++)
            g.SelfTestZombiesTick(now += World.TickDt);
        var cell = g.ZRules.Crowd.NetCell;
        var (cols, rows) = g.Crowd.DensitySize(cell);
        // A packed blob (scripted: the crowd takes a while to bunch up): 120 particles in each of 3 x 3 open cells (the middle
        // one flat on top, the outer ones sloping down to the floor), far from the player (they hold their ground past the promote range only when thinking; the few ticks here hardly move them).
        var blob = Vector3.Zero;
        for (var r = rows - 3; r >= 2 && blob == Vector3.Zero; r--)
            for (var c = 2; c < cols - 2; c++)
            {
                var p = new Vector3(g.Crowd.MinX + (c + 0.5f) * cell, g.Crowd.Y, g.Crowd.MinZ + (r + 0.5f) * cell);
                var clear = true;
                for (var dz = -2; dz <= 2 && clear; dz++)
                    for (var dx = -2; dx <= 2 && clear; dx++)
                        clear = g.Crowd.Walkable(p.X + dx * cell, p.Z + dz * cell) && !World.Blocked(new Vector3(p.X + dx * cell, p.Y + 0.05f, p.Z + dz * cell));
                if (clear && p.Length() > 40)
                {
                    blob = p;
                    break;
                }
            }
        if (blob == Vector3.Zero)
        {
            Fail("no open 20 x 20 m patch on Mass Horde Test for the crowd surface check");
            return;
        }
        for (var k = 0; k < 9 * 120; k++)
        {
            var h = HordeCrowd.Hash((uint)k * 31u + 7);
            var ox = (h & 0xFFF) / 4096f * cell * 0.9f - cell * 0.45f;
            var oz = (h >> 12 & 0xFFF) / 4096f * cell * 0.9f - cell * 0.45f;
            g.Crowd.Add(blob.X + ox + (k % 3 - 1) * cell, blob.Z + oz + (k / 3 % 3 - 1) * cell, 0, 0, (uint)k, g.ServerTick, 100);
        }
        g.SelfTestZombiesTick(now += World.TickDt); // (the cells counted: NetEvery divides the tick below)
        for (var t = 0; t < g.ZRules.Crowd.NetEvery; t++)
            g.SelfTestZombiesTick(now += World.TickDt);
        // The densest cell whose middle is open floor, and the edge cell (dense, an empty neighbour on one side) with the steepest slope.
        var best = -1f;
        var at = Vector3.Zero;
        var steep = 0f;
        var edge = Vector3.Zero;
        var edgeSlope = Vector2.Zero;
        for (var r = 0; r < rows; r++)
            for (var c = 0; c < cols; c++)
            {
                var p = new Vector3(g.Crowd.MinX + (c + 0.5f) * cell, g.Crowd.Y, g.Crowd.MinZ + (r + 0.5f) * cell);
                if (World.Blocked(p + new Vector3(0, 0.05f, 0)))
                    continue;
                var h = g.SelfTestCrowdSurfaceHeight(p.X, p.Z);
                if (h > best)
                {
                    best = h;
                    at = p;
                }
                var (sx, sz) = g.CrowdGround.SlopeAt(p.X, p.Z);
                var s = MathF.Sqrt(sx * sx + sz * sz);
                if (h >= g.CrowdGround.MinHeight && s > steep)
                {
                    steep = s;
                    edge = p;
                    edgeSlope = new Vector2(sx, sz);
                }
            }
        var floor = g.Crowd.Y;
        // Lifted: from the floor in the blob's middle cell (flat on top: full cells either side) up onto it, in a few seconds, and standing there.
        at = blob;
        best = g.SelfTestCrowdSurfaceHeight(at.X, at.Z);
        me.Teleport(at);
        var still = new InputCmd();
        var lifted = 0f;
        for (var t = 0; t < 180; t++)
        {
            g.SelfTestCrowdSurfaceMove(me, ref me.Sim, still);
            lifted = MathF.Max(lifted, me.Sim.Position.Y - floor);
        }
        var stayed = me.Sim.Position.Y - floor;
        var grounded = me.Sim.Grounded;
        var lifts = g.CrowdLifts;
        var stands = g.CrowdStands;
        // No allocation standing there.
        var a0 = GC.GetAllocatedBytesForCurrentThread();
        for (var t = 0; t < 60; t++)
            g.SelfTestCrowdSurfaceMove(me, ref me.Sim, still);
        var alloc = GC.GetAllocatedBytesForCurrentThread() - a0;
        // Slid: put on top at the edge, carried down the slope.
        me.Teleport(edge + new Vector3(0, g.SelfTestCrowdSurfaceHeight(edge.X, edge.Z), 0));
        for (var t = 0; t < 60; t++)
            g.SelfTestCrowdSurfaceMove(me, ref me.Sim, still);
        var moved = new Vector2(me.Sim.Position.X - edge.X, me.Sim.Position.Z - edge.Z);
        var downhill = edgeSlope.LengthSquared() > 0 ? -Vector2.Dot(moved, Vector2.Normalize(edgeSlope)) : 0;
        var slides = g.CrowdSlides;
        // A client's picture: the same surface.
        var view = new Game.CrowdNetView();
        var w = new PacketWriter(32 * 1024);
        g.CrowdCount();
        g.WriteZCrowdFor(w, view, me.Position);
        var t1 = now;
        using var cl = ZGame(0, ref t1, tweak: r => r.FirstRoundDelay = 9999, onMap: map);
        cl.SelfTestReadCrowd(w.Span);
        var worst = 0f;
        for (var k = 0; k < 40; k++)
        {
            var x = (HordeCrowd.Hash((uint)k * 7u) & 0xFFFF) / 65535f * 150f - 75f;
            var z = (HordeCrowd.Hash((uint)k * 13u + 5) & 0xFFFF) / 65535f * 150f - 75f;
            worst = MathF.Max(worst, MathF.Abs(g.SelfTestCrowdSurfaceHeight(x, z) - cl.SelfTestCrowdSurfaceHeight(x, z)));
        }
        // The roar (CrowdRoar.cs): with the ear 30 m from the blob its loudest voice sits on the blob and is near full; a
        // frame allocates nothing; with the ear far away the blob still roars (quieter clusters are nearer).
        var ear = blob + new Vector3(30, 1.6f, 0);
        for (var f = 0; f < 30; f++)
            g.SelfTestCrowdRoarFrame(ear, 1 / 60f);
        var ra0 = GC.GetAllocatedBytesForCurrentThread();
        for (var f = 0; f < 30; f++)
            g.SelfTestCrowdRoarFrame(ear, 1 / 60f);
        var roarAlloc = GC.GetAllocatedBytesForCurrentThread() - ra0;
        var loudest = -1;
        for (var v = 0; v < CrowdRoar.Voices; v++)
            if (loudest < 0 || g.Roar.GoalGain[v] > g.Roar.GoalGain[loudest])
                loudest = v;
        var roarAt = g.Roar.Goal[loudest];
        var roarOff = new Vector2(roarAt.X - blob.X, roarAt.Z - blob.Z).Length();
        var roarGain = g.Roar.GoalGain[loudest];
        var roarN = g.Roar.Zombies[loudest];
        var sounding = g.Roar.Sounding;

        var farY = 0f;
        var stands0 = g.CrowdStands;
        var lifts0 = g.CrowdLifts;
        var open = Vector3.Zero;
        for (var r = 0; r < rows && farY == 0; r++)
            for (var c = 0; c < cols; c++)
            {
                var p = new Vector3(g.Crowd.MinX + (c + 0.5f) * cell, floor, g.Crowd.MinZ + (r + 0.5f) * cell);
                if (World.Blocked(p + new Vector3(0, 0.05f, 0)) || g.SelfTestCrowdSurfaceHeight(p.X, p.Z) > 0)
                    continue;
                open = p;
                farY = 1;
                break;
            }
        me.Teleport(open);
        for (var t = 0; t < 30; t++)
            g.SelfTestCrowdSurfaceMove(me, ref me.Sim, still);
        var openMoved = (me.Sim.Position - open).Length();
        World.SetMap(map);
        MassLine($"surface: the blob's middle cell {best:0.00} m high at ({at.X:0},{at.Z:0}); from the floor there the player rose {lifted:0.00} m in 3 s (ended {stayed:0.00} m up, grounded {grounded}, {lifts} lifts, {stands} stands), {alloc} bytes allocated standing; "
                 + $"on the edge (slope {steep:0.00}) slid {downhill:0.00} m downhill in 1 s ({slides} slides); client picture vs server heights differ by {worst:0.000} m at most; on open floor moved {openMoved:0.000} m; "
                 + $"roar: {sounding} voices sounding, the loudest {roarGain:0.00} for {roarN} zombies {roarOff:0.0} m from the blob's middle ({g.Roar.Picks} picks, {roarAlloc} bytes allocated in 30 frames)");
        if (best < g.CrowdGround.MinHeight)
            Fail($"4,000 crowd zombies should make a surface somewhere ({best:0.00} m at most)");
        if (lifted < best * 0.8f || stayed < best * 0.5f || !grounded || lifts == 0)
            Fail($"a player inside the crowd should be lifted onto it and stay there ({lifted:0.00} of {best:0.00} m, ended {stayed:0.00} m up, grounded {grounded})");
        if (alloc > 0)
            Fail($"standing on the crowd allocated {alloc} bytes");
        if (steep > 0.05f && (downhill < 0.3f || slides == 0))
            Fail($"a player on the crowd's edge should slide down its slope ({downhill:0.00} m, slope {steep:0.00})");
        if (worst > 0.05f)
            Fail($"a client's crowd surface should match the server's ({worst:0.000} m off)");
        if (sounding < 1 || roarGain < 0.8f || roarOff > 4f || roarN < 400 || roarAlloc > 0)
            Fail($"the crowd roar's loudest voice should sit on the blob at near full loudness ({sounding} sounding, gain {roarGain:0.00}, {roarOff:0.0} m off, {roarN} zombies, {roarAlloc} bytes allocated)");
        if (openMoved > 0.01f || g.CrowdStands != stands0 || g.CrowdLifts != lifts0)
            Fail($"the surface should do nothing on open floor (moved {openMoved:0.000} m)");
    }

    /// <summary>The baked crowd rigs (CrowdRig.cs): the same boxes every frame, the near rig fuller than the mid, an animated walk, finite numbers.</summary>
    private static void MassRigs()
    {
        var rigs = Enumerable.Range(0, CrowdInstances.Lods).Select(l => CrowdRig.Bake(l)).ToArray();
        var moves = new float[rigs.Length];
        for (var r = 0; r < rigs.Length; r++)
        {
            var rig = rigs[r];
            var w = rig.Width * 4;
            for (var f = 1; f < rig.Frames; f++)
                for (var k = 0; k < w; k++)
                    moves[r] = MathF.Max(moves[r], MathF.Abs(rig.Texels[f * w + k] - rig.Texels[k]));
        }
        var finite = rigs.All(r => r.Texels.All(float.IsFinite));
        MassLine($"rigs: {string.Join(", ", rigs.Select((r, i) => $"{r.Name} {r.Boxes} boxes x {r.Frames} frames ({r.Texels.Length * 4 / 1024.0:0.0} KB, moves {moves[i]:0.00})"))}, walk cycle {rigs[0].CycleMetres:0.00} m, finite {finite}");
        if (!finite || rigs[0].Boxes <= rigs[1].Boxes || rigs[1].Boxes <= rigs[2].Boxes || rigs[0].Frames < 8 || moves[0] < 0.05f || moves[1] < 0.05f)
            Fail("the crowd rigs should bake a walk cycle: near fuller than mid fuller than far, the limbs moving, finite numbers");
    }

    /// <summary>The same seed, the same crowd: two runs of 2 s with 3,000 particles end with the same digest.</summary>
    private static void MassDeterminism(Map map, ref double now)
    {
        ulong Run1(ref double t0)
        {
            using var g = ZGame(0, ref t0, tweak: r => r.FirstRoundDelay = 9999, onMap: map);
            g.Local!.ImmuneUntil = 1e9;
            g.Local.Teleport(Vector3.Zero);
            g.CrowdOverride = 3000;
            g.SelfTestBeginRound(1, t0);
            var t = t0;
            for (var k = 0; k < 120; k++)
                g.SelfTestZombiesTick(t += World.TickDt);
            t0 = t;
            var h = g.Crowd.Digest();
            foreach (var z in g.Horde)
                h = (h ^ BitConverter.SingleToUInt32Bits(z.Position.X) ^ ((ulong)z.Id << 32)) * 1099511628211UL;
            return h;
        }
        var a = Run1(ref now);
        var b = Run1(ref now);
        MassLine($"determinism: 3,000 for 2 s twice: {a:x16} / {b:x16}");
        if (a != b)
            Fail("the crowd should replay exactly from the same seed");
    }
}
