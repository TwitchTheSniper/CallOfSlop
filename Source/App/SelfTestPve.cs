using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Self-test section "Co-op" (docs/pve/DESIGN.md): the mission files and their loader, the spawn director's maths (the
/// alive budget, pacing, totals), Silo Breach's points on the nav graph, spawns only out of sight and on the walkable
/// area, recycling far enemies, 80 enemies' AI cost (10 s) and allocations, their replication round trip and bandwidth,
/// enemy fire with spread and its damage, every objective type completing and failing, checkpoint restarts on a wipe,
/// retries. Short scenarios only: no long bot matches.
/// </summary>
public static partial class SelfTest
{
    private static Map? CoopMap => Maps.Find("Silo");
    private static readonly List<string> CoopNotes = new();

    private static void CoopLine(string s)
    {
        Line(s);
        CoopNotes.Add(s);
    }

    private static void CheckCoop()
    {
        CoopNotes.Clear();
        try
        {
            CheckCoopBody();
        }
        finally
        {
            Line("::warning title=Co-op (results)::" + string.Join("%0A", CoopNotes.Select(n => n.Replace("%", "%25").Replace("\r", "").Replace("\n", " "))));
            World.SetMap(Maps.All[0]);
        }
    }

    /// <summary>A Co-op game on Silo (offline), playing, the host deployed at the mission start.</summary>
    private static Game CoopGame(int bots, ref double now, BotDifficulty skill = BotDifficulty.Normal, bool hostIn = true)
    {
        var map = CoopMap!;
        ClutteredLargeNav(map);
        var g = new Game(NetPeer.Offline(), new Audio(), "SelfTest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: bots, botSkill: skill, mapId: map.Id)
        {
            MapVoteEnabled = false,
        };
        g.Mode = GameMode.Coop;
        Run(g, ref now, Game.CountdownSeconds + 0.2);
        if (hostIn)
        {
            g.RequestRespawn();
            Run(g, ref now, 0.1);
        }
        return g;
    }

    private static void CoopTicks(Game g, ref double now, double seconds)
    {
        for (var t = 0.0; t < seconds; t += World.TickDt)
        {
            now += World.TickDt;
            g.SelfTestCoopTick(now);
        }
    }

    private static string Pt(Vector3 p) => $"[{F(p.X)}, {F(p.Y)}, {F(p.Z)}]";

    private static void CheckCoopBody()
    {
        // ---- The mission files ----
        foreach (var e in Missions.LoadErrors)
            Fail($"co-op mission: {e}");
        var map = CoopMap;
        var mission = Missions.For(map);
        if (map == null || mission == null)
        {
            Fail("co-op: no Silo map or no mission for it (Maps/World/Missions/SiloBreach.json)");
            return;
        }
        var kinds = mission.Stages.Select(s => s.Kind).ToList();
        CoopLine($"{Missions.All.Count} mission(s); {mission.Name} on {mission.Map}: {mission.Stages.Count} stages ({string.Join(", ", kinds)}); alive budget 1 player Normal "
                 + $"{mission.BudgetFor(1, BotDifficulty.Normal)}, 4 Normal {mission.BudgetFor(4, BotDifficulty.Normal)}, 4 Insane {mission.BudgetFor(4, BotDifficulty.Insane)}, 1 Easy {mission.BudgetFor(1, BotDifficulty.Easy)}");
        if (mission.Stages.Count is < 3 or > 5 || !kinds.Contains(StageKind.Eliminate) || !kinds.Contains(StageKind.Destroy) || !kinds.Contains(StageKind.Defend) || !kinds.Contains(StageKind.Reach))
            Fail("Silo Breach should have 3-5 stages: eliminate, destroy, defend and reach");
        if (mission.BudgetFor(4, BotDifficulty.Normal) <= 32 || mission.BudgetFor(4, BotDifficulty.Insane) > 80 || mission.BudgetFor(1, BotDifficulty.Easy) >= mission.BudgetFor(1, BotDifficulty.Normal))
            Fail("the alive budget should pass the 32 player slots with a full squad, stay within 80 and scale with difficulty");
        if (!GameModes.Fits(GameMode.Coop, map) || GameModes.Fits(GameMode.Coop, Maps.All[0]) || GameModes.Fits(GameMode.Coop, null) || !GameModes.ForMap(map).Contains(GameMode.Coop))
            Fail("Co-op should be offered on maps with a mission only");
        if (Maps.All.Any(m => m.Name.Equals(mission.Name, StringComparison.OrdinalIgnoreCase) || (m.SourceFile ?? "").Replace('\\', '/').Contains("/Missions/")))
            Fail("a mission file was loaded as a map (the Missions folder must stay out of the map loader)");
        var bad = new[]
        {
            """{"format": "call-of-slop-mission/1", "name": "X", "map": "Silo", "start": [[0, 0]], "stages": [{"type": "eliminate", "text": "x", "count": 3}], "bogus": 1}""",
            """{"format": "nope", "name": "X", "map": "Silo", "start": [[0, 0]], "stages": [{"type": "eliminate", "text": "x", "count": 3}]}""",
            """{"format": "call-of-slop-mission/1", "name": "X", "map": "Silo", "start": [[0, 0]], "stages": [{"type": "eliminate", "text": "x"}]}""",
            """{"format": "call-of-slop-mission/1", "name": "X", "map": "Silo", "start": [[0, 0]], "stages": [{"type": "destroy", "text": "x"}]}""",
            """{"format": "call-of-slop-mission/1", "name": "X", "map": "Silo", "start": [[0]], "stages": [{"type": "reach", "text": "x", "at": [0, 0]}]}""",
            """{"format": "call-of-slop-mission/1", "name": "X", "map": "Silo", "start": [[0, 0]], "stages": [{"type": "defend", "text": "x"}]}""",
            """{"format": "call-of-slop-mission/1", "name": "X", "map": "Silo", "start": [[0, 0]], "stages": [{"type": "eliminate", "text": "x", "count": 3, "mix": {"dragon": 1}}]}""",
            """{"format": "call-of-slop-mission/1", "name": "X", "map": "Silo", "start": [[0, 0]], "stages": [{"type": "fly", "text": "x"}]}""",
        };
        foreach (var json in bad)
            try
            {
                Mission.Load(json, "bad mission");
                Fail($"a broken mission loaded: {json}");
            }
            catch (MapFileException)
            {
            }

        CheckDirectorMaths();

        var now = 0.0;
        CheckCoopSilo(mission, ref now);
        CheckCoopCost(ref now);
        CheckCoopFire(ref now);
        CheckCoopObjectives(now);
    }

    // ---- The director: plain maths --------------------------------------------------------------------------------

    private static void CheckDirectorMaths()
    {
        var rng = new Random(11);
        var d = new SpawnDirector();
        d.Reset(40, -1, 1f, escalate: false, seed: 7, now: 0);
        int alive = 0, maxAlive = 0, over = 0, squads = 0, biggest = 0;
        var phases = new HashSet<DirectorPhase>();
        var peakTimes = new List<double>();
        var peakStart = -1.0;
        for (var t = 0.0; t < 600; t += World.TickDt)
        {
            // Kills: the squad kills about one enemy a second when the place is full.
            if (alive > 0 && rng.NextDouble() < alive / 40.0 * World.TickDt * 1.2)
                alive--;
            var n = d.Update(t, alive);
            if (n > d.Budget - alive)
                over++;
            if (n > 0)
            {
                squads++;
                biggest = Math.Max(biggest, n);
            }
            alive += n;
            d.OnSpawned(n);
            maxAlive = Math.Max(maxAlive, alive);
            phases.Add(d.Phase);
            if (d.Phase == DirectorPhase.Peak && peakStart < 0)
                peakStart = t;
            else if (d.Phase != DirectorPhase.Peak && peakStart >= 0)
            {
                peakTimes.Add(t - peakStart);
                peakStart = -1;
            }
        }
        // Stress cuts the peaks short.
        var s = new SpawnDirector();
        s.Reset(40, -1, 1f, false, 7, 0);
        var stressedPeaks = new List<double>();
        var sp = -1.0;
        for (var t = 0.0; t < 600; t += World.TickDt)
        {
            if (s.Phase == DirectorPhase.Peak)
                s.AddStress(0.5f * World.TickDt * 4);
            s.OnSpawned(s.Update(t, 10));
            if (s.Phase == DirectorPhase.Peak && sp < 0)
                sp = t;
            else if (s.Phase != DirectorPhase.Peak && sp >= 0)
            {
                stressedPeaks.Add(t - sp);
                sp = -1;
            }
        }
        // A total: exactly that many, then nothing.
        var tot = new SpawnDirector();
        tot.Reset(30, 50, 1f, false, 3, 0);
        var spawned = 0;
        var talive = 0;
        for (var t = 0.0; t < 400; t += World.TickDt)
        {
            if (talive > 0 && rng.NextDouble() < 0.02)
                talive--;
            var n = tot.Update(t, talive);
            talive += n;
            spawned += n;
            tot.OnSpawned(n);
        }
        // Escalating waves get stronger.
        var esc = new SpawnDirector();
        esc.Reset(50, -1, 0.8f, true, 5, 0);
        var waveTargets = new List<int>();
        for (var t = 0.0; t < 400; t += World.TickDt)
        {
            esc.OnSpawned(esc.Update(t, 0));
            if (esc.Phase == DirectorPhase.Peak && (waveTargets.Count < esc.Wave))
                waveTargets.Add(esc.TargetAlive(t));
        }
        CoopLine($"director (budget 40, 10 min): max alive {maxAlive}, over budget {over}, {squads} squads (biggest {biggest}), phases {string.Join("/", phases)}, "
                 + $"peaks {(peakTimes.Count > 0 ? peakTimes.Average() : 0):0.0} s calm vs {(stressedPeaks.Count > 0 ? stressedPeaks.Average() : 0):0.0} s stressed; total 50 -> {spawned}; "
                 + $"escalating wave targets {string.Join(" ", waveTargets.Take(6))}");
        if (over > 0 || maxAlive > 40)
            Fail($"the director went over its alive budget ({maxAlive} alive, {over} over)");
        if (phases.Count != 3 || peakTimes.Count < 3)
            Fail("the director should cycle build-up, peak and relax several times in 10 minutes");
        if (biggest > 6 || squads < 20)
            Fail("the director should send squads of 2-6");
        if (stressedPeaks.Count == 0 || stressedPeaks.Average() >= peakTimes.Average())
            Fail("stress should cut the director's peaks short");
        if (spawned != 50)
            Fail($"a stage total of 50 spawned {spawned}");
        if (waveTargets.Count < 3 || waveTargets[2] <= waveTargets[0])
            Fail("escalating (defend) waves should get stronger");
    }

    // ---- Silo Breach: points, spawn spots, the budget in play -----------------------------------------------------

    private static void CheckCoopSilo(Mission mission, ref double now)
    {
        using var g = CoopGame(3, ref now);
        var nav = NavGraph.Current;
        if (g.Coop.MissionDef != mission || g.Coop.Status != CoopStatus.Active)
        {
            Fail($"co-op: Silo Breach didn't start (status {g.Coop.Status}, mission {g.Coop.MissionDef?.Name})");
            return;
        }
        // Every point of the mission stands on the walkable area (a nav node in the main area close by, same level).
        var misses = new List<string>();
        var checkedPoints = 0;
        void Check(string what, MissionPoint p, float across = 3f)
        {
            checkedPoints++;
            var at = p.Resolve();
            var best = float.MaxValue;
            var bestNode = Vector3.Zero;
            for (var i = 0; i < nav.Nodes.Length; i++)
            {
                if (nav.Component[i] != nav.MainComponent)
                    continue;
                var q = nav.Nodes[i];
                if (MathF.Abs(q.Y - at.Y) > 1.5f)
                    continue;
                var dd = MathF.Sqrt((q.X - at.X) * (q.X - at.X) + (q.Z - at.Z) * (q.Z - at.Z));
                if (dd < best)
                {
                    best = dd;
                    bestNode = q;
                }
            }
            if (best > across)
                misses.Add($"{what} {p} -> {Pt(at)} (nearest node {(best < float.MaxValue ? Pt(bestNode) + $" {best:0.0} m" : "none")})");
        }
        for (var i = 0; i < mission.Start.Count; i++)
            Check($"start {i + 1}", mission.Start[i]);
        for (var s = 0; s < mission.Stages.Count; s++)
        {
            var st = mission.Stages[s];
            var tag = $"stage {s + 1}";
            Check($"{tag} focus", st.FocusPoint, 6f);
            if (st.At is { } at)
                Check($"{tag} at", at, 4f);
            foreach (var t in st.Targets)
                Check($"{tag} target {t.Name}", t.At);
            foreach (var z in st.SpawnZones)
                Check($"{tag} spawn zone {z.Name}", z.At, z.Radius);
            for (var c = 0; c < st.Checkpoint.Count; c++)
                Check($"{tag} checkpoint {c + 1}", st.Checkpoint[c]);
        }
        CoopLine($"Silo Breach: {checkedPoints} points checked against the nav graph ({nav.Nodes.Length} nodes): {misses.Count} off the walkable area");
        foreach (var m in misses)
        {
            CoopLine($"  off the nav graph: {m}");
            Fail($"Silo Breach: {m}");
        }
        var me = g.Local!;
        var start = mission.Start[me.Id % mission.Start.Count].Resolve();
        if (!me.Alive || Vector3.Distance(me.Position, start) > 2.5f)
            Fail($"the host didn't deploy at the mission start ({Pt(me.Position)}, start {Pt(start)})");

        // Spawn spots: out of every player's sight, far enough, on the main walkable area, inside the zone.
        g.PveNoSpawns = true;
        var zoneSets = new (string Label, List<(Vector3 At, float Radius)> Zones)[] { ("stage 1 zones", g.CoopZones.ToList()), ("ring round the focus", new List<(Vector3 At, float Radius)>()) };
        foreach (var (label, zones) in zoneSets)
        {
            int found = 0, seen = 0, close = 0, offArea = 0;
            for (var k = 0; k < 120; k++)
            {
                if (!g.PickSpawnSpot(zones, g.CoopFocus, out var at, out var node))
                    continue;
                found++;
                if (g.SeenByAnyPlayer(at))
                    seen++;
                if (g.Players.Values.Any(p => p.Alive && Vector3.Distance(p.Position, at) < Game.PveSpawnMinDistance))
                    close++;
                if (node < 0 || nav.Component[node] != nav.MainComponent)
                    offArea++;
            }
            CoopLine($"spawn spots ({label}): {found}/120 found, {seen} in sight, {close} too close, {offArea} off the main area");
            if (found < 60 || seen > 0 || close > 0 || offArea > 0)
                Fail($"spawn spots ({label}): {found} found, {seen} in sight, {close} too close, {offArea} off the main area");
        }

        // The budget in play: 12 s of the real game (bots, director, AI): never more alive than the budget.
        g.PveNoSpawns = false;
        int overBudget = 0, peak = 0, miscount = 0;
        for (var t = 0.0; t < 12; t += World.TickDt)
        {
            now += World.TickDt;
            g.Update(now, World.TickDt);
            if (g.AliveEnemies > g.Director.Budget)
                overBudget++;
            if (g.AliveEnemies != g.Enemies.Count(e => e.Alive))
                miscount++;
            peak = Math.Max(peak, g.AliveEnemies);
        }
        CoopLine($"12 s of play: budget {g.Director.Budget}, peak {peak} alive, {g.PveSpawnedTotal} spawned, {g.CoopKills} killed, {g.PveShotsFired} enemy rounds ({g.PveShotsHit} hit), "
                 + $"{g.PveGrenadesThrown} grenades, phase {g.Director.Phase} wave {g.Director.Wave}, stage {g.Coop.Stage + 1} ({g.Coop.Progress}/{g.Coop.Goal}), wipes {g.Coop.Wipes}");
        if (overBudget > 0 || miscount > 0)
            Fail($"co-op: over the alive budget on {overBudget} ticks, alive count wrong on {miscount}");
        if (g.PveSpawnedTotal == 0)
            Fail("co-op: the director spawned nobody in 12 s");
        if (g.Module.SelfTestVerify(g) is { } problem)
            Fail($"co-op: {problem}");
    }

    // ---- 80 enemies: AI cost, recycling, allocations, replication and bandwidth ----------------------------------------

    private static void CheckCoopCost(ref double now)
    {
        using var g = CoopGame(3, ref now, BotDifficulty.Hard);
        g.PveNoSpawns = true;
        g.ClearEnemies();
        var nav = NavGraph.Current;
        var players = g.Players.Values.Where(p => p.Alive).ToList();
        foreach (var p in g.Players.Values)
            p.ImmuneUntil = 1e9;
        var me = g.Local!;
        var rng = new Random(80);
        var near = new List<int>();
        nav.Within(me.Position, 90f, near);
        near = near.Where(i => nav.Component[i] == nav.MainComponent && players.All(p => Vector3.Distance(p.Position, nav.Nodes[i]) > 15f)).ToList();
        var far = nav.MainNodes.Where(i => players.All(p => Vector3.Distance(p.Position, nav.Nodes[i]) > 170f)).ToList();
        if (near.Count < 40 || far.Count < 40)
        {
            Fail($"co-op cost: not enough nav nodes to place 80 enemies ({near.Count} near, {far.Count} far)");
            return;
        }
        var nearIds = new HashSet<ushort>();
        for (var k = 0; k < 80; k++)
        {
            var list = k < 40 ? near : far;
            var e = g.SpawnEnemy((PveArch)(k % 6), nav.Nodes[list[rng.Next(list.Count)]] + new Vector3(0, 0.05f, 0), now);
            if (e != null && k < 40)
                nearIds.Add(e.Id);
        }
        if (g.AliveEnemies != 80)
            Fail($"co-op cost: {g.AliveEnemies} of 80 enemies placed");

        // Replication and bandwidth with 80 alive: what each of the four players' clients gets over 10 s at 20 Hz.
        long bytes = 0;
        var biggest = 0;
        var most = 0;
        for (var i = 0; i < 200; i++)
            foreach (var p in players)
            {
                var pkt = g.SelfTestPEnts(p.Position, i, now);
                bytes += pkt.Length;
                biggest = Math.Max(biggest, pkt.Length);
                most = Math.Max(most, pkt[5]);
            }
        var perClient = bytes / (double)players.Count / 10.0;
        using (var c = new Game(NetPeer.Offline(), new Audio(), "SelfTest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: CoopMap!.Id) { MapVoteEnabled = false })
        {
            var pkt = g.SelfTestPEnts(me.Position, 0, now);
            c.SelfTestReadPEnts(pkt, now);
            var worst = 0f;
            var wrong = 0;
            foreach (var ce in c.Enemies)
            {
                var se = g.Enemies.FirstOrDefault(x => x.Id == ce.Id);
                if (se == null || se.Arch != ce.Arch || se.State != ce.State)
                {
                    wrong++;
                    continue;
                }
                worst = MathF.Max(worst, Vector3.Distance(se.Position, ce.Position));
            }
            CoopLine($"replication: 80 alive, {c.Enemies.Count} in the host's packet ({pkt.Length} bytes, worst {worst * 100:0.0} cm, {wrong} wrong); "
                     + $"per client {perClient / 1024:0.0} KB/s at 20 Hz (biggest packet {biggest} bytes, most enemies {most}), protocol {ServerInfo.ProtocolVersion}");
            if (c.Enemies.Count == 0 || worst > 0.05f || wrong > 0)
                Fail("co-op: the enemies didn't replicate");
            if (biggest > PacketWriter.Mtu || most > Game.PEntsMax || perClient > 12 * 1024)
                Fail($"co-op: enemy replication over budget ({perClient / 1024:0.0} KB/s per client, biggest {biggest} bytes)");
        }

        // The AI cost: 10 s with 80 alive (40 fighting near the squad, 40 far away).
        CoopTicks(g, ref now, 0.5);
        g.PveAiMs = g.PveAiTicks = g.PveAiEnemyTicks = 0;
        var worstTick = 0.0;
        var sw = new Stopwatch();
        for (var t = 0.0; t < 10; t += World.TickDt)
        {
            now += World.TickDt;
            sw.Restart();
            g.SelfTestCoopTick(now);
            worstTick = Math.Max(worstTick, sw.Elapsed.TotalMilliseconds);
            foreach (var e in g.Enemies)
                if (!float.IsFinite(e.Position.X + e.Position.Y + e.Position.Z))
                {
                    Fail($"co-op: enemy {e.Id} position is NaN");
                    return;
                }
        }
        var perTick = g.PveAiMs / Math.Max(1, g.PveAiTicks);
        var perEnemy = g.PveAiMs * 1000 / Math.Max(1, g.PveAiEnemyTicks);
        var nearLeft = g.Enemies.Count(e => e.Alive && nearIds.Contains(e.Id));
        CoopLine($"AI cost: {perTick:0.00} ms per tick, {perEnemy:0.0} us per enemy per tick (worst whole tick {worstTick:0.0} ms); {g.PveShotsFired} rounds fired; "
                 + $"recycled {g.PveRecycledTotal} (40 far away), near ones left {nearLeft}/40");
        if (perTick > 3 || perEnemy > 40)
            Fail($"co-op: the enemy AI is too slow ({perTick:0.00} ms per tick, {perEnemy:0.0} us per enemy)");
        if (g.PveRecycledTotal < 30)
            Fail($"co-op: far enemies weren't recycled ({g.PveRecycledTotal} of 40)");
        if (nearLeft < 30)
            Fail($"co-op: enemies near the squad were recycled ({nearLeft} of 40 left)");
        if (g.PveShotsFired == 0)
            Fail("co-op: 40 enemies near the squad never fired");

        // Steady state allocates nothing: the host stands far off (its tracers and sounds are visuals), the bots fight on.
        me.Teleport(me.Position + new Vector3(170, 0, 0));
        CoopTicks(g, ref now, 1);
        var before = GC.GetAllocatedBytesForCurrentThread();
        CoopTicks(g, ref now, 2);
        var perTickBytes = (GC.GetAllocatedBytesForCurrentThread() - before) / 120.0;
        CoopLine($"allocations: {perTickBytes:0} bytes per tick with {g.AliveEnemies} enemies alive (steady state, host away)");
        if (perTickBytes > 64)
            Fail($"co-op: the enemy tick allocates {perTickBytes:0} bytes per tick");
    }

    // ---- Enemy fire: spread, damage, the kill ------------------------------------------------------------------------

    private static void CheckCoopFire(ref double now)
    {
        using var g = CoopGame(0, ref now);
        g.PveNoSpawns = true;
        g.ClearEnemies();
        var me = g.Local!;
        // A spot 12-18 m from the host in plain view.
        Vector3? spot = null;
        for (var a = 0; a < 16 && spot == null; a++)
            foreach (var r in new[] { 14f, 12f, 17f })
            {
                var p = me.Position + World.FlatForward(a * MathF.Tau / 16) * r;
                var node = NavGraph.Current.Nearest(p, needSight: false);
                if (node < 0)
                    continue;
                var q = NavGraph.Current.Nodes[node];
                if (MathF.Abs(q.Y - me.Position.Y) < 1.5f && Vector3.Distance(q, me.Position) > 10f && g.SeenByAnyPlayer(q))
                {
                    spot = q;
                    break;
                }
            }
        if (spot is not { } at)
        {
            Fail("co-op fire: no spot in plain view of the host");
            return;
        }
        var e = g.SpawnEnemy(PveArch.Rifleman, at + new Vector3(0, 0.05f, 0), now)!;
        e.Target = me.Id;
        e.TargetVisible = true;
        e.TargetSince = now - 100;
        var eye = e.Position + new Vector3(0, 1.55f, 0);
        var ratios = new Dictionary<BotDifficulty, float>();
        var lowest = World.MaxHealth;
        foreach (var skill in new[] { BotDifficulty.Easy, BotDifficulty.Hard })
        {
            g.SelfTestSetSkill(skill);
            g.PveShotsFired = g.PveShotsHit = 0;
            for (var k = 0; k < 300; k++)
            {
                me.Health = World.MaxHealth;
                me.ImmuneUntil = 0;
                g.EnemyShot(e, me, eye, false, now + 200);
                lowest = Math.Min(lowest, me.Health);
            }
            ratios[skill] = g.PveShotsHit / (float)Math.Max(1, g.PveShotsFired);
        }
        // The kill: a player brought down by enemy fire is nobody's kill, and waits out the respawn time.
        me.Health = 1;
        for (var k = 0; k < 200 && me.Alive; k++)
            g.EnemyShot(e, me, eye, false, now + 200);
        var killId = g.LastKill.KillId;
        g.SelfTestCoopTick(now += World.TickDt); // (a wipe: the host was the squad)
        var dist = Vector3.Distance(at, me.Position);
        CoopLine($"enemy fire at {dist:0.0} m: rifleman hits {ratios[BotDifficulty.Easy] * 100:0}% on Easy, {ratios[BotDifficulty.Hard] * 100:0}% on Hard; one hit leaves {lowest} of {World.MaxHealth}; "
                 + $"killed: {!me.Alive}, kill id {killId} ({Throwables.KillName(killId)}), then {g.Coop.Status}");
        if (ratios[BotDifficulty.Easy] is < 0.05f or > 0.95f || ratios[BotDifficulty.Hard] < 0.3f || ratios[BotDifficulty.Hard] <= ratios[BotDifficulty.Easy])
            Fail("co-op: enemy fire should hit with a spread that tightens with difficulty");
        if (lowest >= World.MaxHealth || lowest < World.MaxHealth - 60)
            Fail($"co-op: an enemy rifle hit should hurt but not one-shot (left {lowest})");
        if (me.Alive || killId != Game.PveKillId)
            Fail("co-op: enemy fire should be able to kill, as kill id 253 (no player credited)");
        if (g.Coop.Status != CoopStatus.Regroup || g.Coop.Wipes != 1)
            Fail($"co-op: the squad's last player dying should be a wipe (status {g.Coop.Status}, wipes {g.Coop.Wipes})");
    }

    // ---- Objectives: each type completes and fails; wipes restart at the checkpoint; retries run out ---------------------

    private static Mission TestMission(Vector3 home, string stages)
    {
        var json = """{"format": "call-of-slop-mission/1", "name": "Test", "map": "Silo", "start": [@HOME], "stages": [@STAGES, {"type": "reach", "text": "never", "at": [100, 70], "radius": 1}]}"""
            .Replace("@STAGES", stages).Replace("@HOME", Pt(home));
        return Mission.Load(json, "test mission");
    }

    private static void CheckCoopObjectives(double startTime)
    {
        var now = startTime; // (a local: the helpers below capture it)
        using var g = CoopGame(0, ref now);
        g.PveNoSpawns = true;
        var me = g.Local!;
        var home = me.Position;
        var away = home + new Vector3(0, 0, 30);
        var results = new List<string>();
        void Start(string stages)
        {
            g.SelfTestStartMission(TestMission(home, stages.Replace("@HOME", Pt(home)).Replace("@AWAY", Pt(away))), now);
            me.Teleport(home);
            me.Health = World.MaxHealth;
            me.ImmuneUntil = 1e9;
            g.SelfTestCoopTick(now += World.TickDt);
        }
        PveEnemy Spawn(PveArch a, Vector3 at) => g.SpawnEnemy(a, at + new Vector3(0, 0.05f, 0), now)!;

        // Eliminate: three kills.
        Start("""{"type": "eliminate", "text": "kill 3", "count": 3, "focus": @HOME}""");
        for (var k = 0; k < 3; k++)
            g.DamageEnemy(me, Spawn(PveArch.Rifleman, away), 999, false, false, me.Primary, now);
        g.SelfTestCoopTick(now += World.TickDt);
        var elim = g.Coop.Stage == 1;
        results.Add($"eliminate {(elim ? "ok" : $"FAILED (stage {g.Coop.Stage + 1}, {g.Coop.Progress}/{g.Coop.Goal})")}");

        // Eliminate a named target.
        Start("""{"type": "eliminate", "text": "kill him", "target": {"arch": "officer", "name": "Colonel", "at": @AWAY}}""");
        var boss = g.Enemies.FirstOrDefault(x => x.Named);
        var namedUp = g.Coop.NamedAlive && boss is { Arch: PveArch.Officer } && boss.MaxHealth > PveArchetypes.Get(PveArch.Officer).Health;
        if (boss != null)
            g.DamageEnemy(me, boss, 99999, true, false, me.Primary, now);
        g.SelfTestCoopTick(now += World.TickDt);
        var named = namedUp && g.Coop.Stage == 1;
        results.Add($"named target {(named ? "ok" : "FAILED")}");

        // Destroy: plant (stand at it), the fuse, the blast (an enemy beside it dies), done; a target nobody stands at stays.
        Start("""{"type": "destroy", "text": "blow it", "targets": [{"name": "A", "at": @HOME}, {"name": "B", "at": @AWAY}], "plantSeconds": 0.5, "fuseSeconds": 1}""");
        var victim = Spawn(PveArch.Rifleman, home + new Vector3(3, 0, 0));
        CoopTicks(g, ref now, 0.7);
        var planted = g.Coop.Targets[0] == CoopTarget.Planted && g.Coop.Targets[1] == CoopTarget.Intact;
        CoopTicks(g, ref now, 1.2);
        var blown = g.Coop.Targets[0] == CoopTarget.Destroyed && g.CoopCharges == 1 && !victim.Alive;
        me.Teleport(away);
        CoopTicks(g, ref now, 2.2);
        var destroy = planted && blown && g.Coop.Stage == 1;
        results.Add($"destroy {(destroy ? "ok" : $"FAILED (planted {planted}, blown {blown}, charges {g.CoopCharges}, stage {g.Coop.Stage + 1})")}");

        // Defend: hold 5 s.
        Start("""{"type": "defend", "text": "hold", "at": @HOME, "radius": 6, "seconds": 5, "loseSeconds": 2}""");
        CoopTicks(g, ref now, 5.3);
        var held = g.Coop.Stage == 1;
        results.Add($"defend {(held ? "ok" : "FAILED")}");

        // Defend lost: enemies in the zone and nobody of the squad: the loss meter fills, regroup, the checkpoint.
        Start("""{"type": "defend", "text": "hold", "at": @HOME, "radius": 8, "seconds": 60, "loseSeconds": 2}""");
        me.Teleport(away);
        for (var k = 0; k < 3; k++)
            Spawn(PveArch.Heavy, home + new Vector3(k - 1, 0, 0));
        CoopTicks(g, ref now, 2.5);
        var lost = g.Coop.Status == CoopStatus.Regroup && g.Coop.Wipes == 1 && g.CoopLastRestart == "POINT LOST" && g.AliveEnemies == 0;
        CoopTicks(g, ref now, 4.2);
        var back = g.Coop.Status == CoopStatus.Active && g.Coop.Stage == 0 && me.Alive && Vector3.Distance(me.Position, g.CoopCheckpoint[0]) < 2.5f;
        results.Add($"defend lost {(lost && back ? "ok" : $"FAILED (lost {lost}, back at the checkpoint {back}, {g.Coop.Status}, {g.CoopLastRestart})")}");

        // Reach: everyone (the host) in the zone.
        Start("""{"type": "reach", "text": "go", "at": @AWAY, "radius": 4}""");
        CoopTicks(g, ref now, 0.5);
        var notYet = g.Coop.Stage == 0;
        me.Teleport(away);
        CoopTicks(g, ref now, 0.2);
        var reach = notYet && g.Coop.Stage == 1;
        results.Add($"reach {(reach ? "ok" : "FAILED")}");

        // Collect: pick up, carry, deliver; a carrier who dies drops it.
        Start("""{"type": "collect", "text": "fetch", "items": [{"name": "Codes", "at": @HOME}], "deliver": @AWAY, "deliverRadius": 3}""");
        CoopTicks(g, ref now, 0.2);
        var carried = g.Coop.Targets[0] == CoopTarget.Planted;
        me.Teleport(away);
        CoopTicks(g, ref now, 0.2);
        var collect = carried && g.Coop.Stage == 1;
        Start("""{"type": "collect", "text": "fetch", "items": [{"name": "Codes", "at": @HOME}], "deliver": [100, 70]}""");
        CoopTicks(g, ref now, 0.2);
        var dropAt = home + new Vector3(4, 0, 0);
        me.Teleport(dropAt);
        CoopTicks(g, ref now, 0.1);
        me.ImmuneUntil = 0;
        g.SelfTestDamageWith(me, me, 999, Game.PveKillId, now);
        g.SelfTestCoopTick(now += World.TickDt);
        var dropped = g.Coop.Targets[0] == CoopTarget.Intact && Vector3.Distance(g.Coop.ItemPos[0], dropAt) < 1.5f;
        results.Add($"collect {(collect && dropped ? "ok" : $"FAILED (carried {carried}, delivered {collect}, dropped {dropped})")}");

        // The wipe that just happened (the carrier was the whole squad): regroup, then everyone back at the checkpoint with full health.
        var wiped = g.Coop.Status == CoopStatus.Regroup && g.CoopLastRestart == "SQUAD WIPED";
        CoopTicks(g, ref now, 4.2);
        var restarted = g.Coop.Status == CoopStatus.Active && me.Alive && me.Health == World.MaxHealth && Vector3.Distance(me.Position, g.CoopCheckpoint[0]) < 2.5f
                        && g.Coop.Targets[0] != CoopTarget.Destroyed && Vector3.Distance(g.Coop.ItemPos[0], home) < 2.5f; // (back where it started: the host respawns on it and picks it up)
        results.Add($"wipe -> checkpoint restart {(wiped && restarted ? "ok" : $"FAILED (wiped {wiped}, restarted {restarted}, {g.Coop.Status})")}");

        // A time limit runs out.
        Start("""{"type": "eliminate", "text": "quick", "count": 50, "timeLimit": 1}""");
        CoopTicks(g, ref now, 1.3);
        var timedOut = g.Coop.Status == CoopStatus.Regroup && g.CoopLastRestart == "OUT OF TIME";
        results.Add($"time limit {(timedOut ? "ok" : "FAILED")}");

        // Respawn timing: dead, not back before the difficulty's respawn time.
        g.SelfTestSetSkill(BotDifficulty.Normal);
        Start("""{"type": "eliminate", "text": "x", "count": 50}""");
        var bot = g.SelfTestAddHuman();
        g.SelfTestSpawn(bot);
        bot.ImmuneUntil = 0;
        g.SelfTestDamageWith(bot, bot, 999, Game.PveKillId, now);
        g.SelfTestCoopTick(now += World.TickDt);
        var early = g.Module.CanRespawn(g, bot, now + 1);
        var later = g.Module.CanRespawn(g, bot, now + g.Coop.RespawnSeconds + 0.1);
        var spawnAt = Vector3.Distance(bot.Position, g.CoopCheckpoint[bot.Id % g.CoopCheckpoint.Count]) < 2.5f;
        results.Add($"respawn after {g.Coop.RespawnSeconds:0} s {(!early && later && g.Coop.Status == CoopStatus.Active ? "ok" : $"FAILED (early {early}, later {later}, {g.Coop.Status})")}");

        // Retries: Insane allows 3 wipes; the 4th fails the mission.
        g.SelfTestSetSkill(BotDifficulty.Insane);
        Start("""{"type": "eliminate", "text": "x", "count": 50, "timeLimit": 0.5}""");
        for (var k = 0; k < 4 && g.Phase == MatchPhase.Playing; k++)
            CoopTicks(g, ref now, 4.9);
        var failed = g.Coop.Status == CoopStatus.Failed && g.Phase == MatchPhase.Ended && g.MatchResult.StartsWith("Mission failed");
        results.Add($"retries (Insane {g.Coop.Retries}) {(failed ? "ok" : $"FAILED ({g.Coop.Status}, {g.Coop.Wipes} wipes, {g.MatchResult})")}");

        CoopLine($"objectives: {string.Join("; ", results)}; checkpoint spawn ok {spawnAt}");
        foreach (var r in results.Where(r => r.Contains("FAILED")))
            Fail($"co-op objective: {r}");
    }
}
