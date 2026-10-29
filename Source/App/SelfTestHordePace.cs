using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Self-test section "Horde pace" (docs/zombies/MASS-HORDE.md, "Pace by round" and "Strays rejoin"): the mass horde's
/// cheap tiers move at the round's speed mix. Short seeded scenes on Horde Stress's valley floor: round 1 and round 20,
/// the crowd's particles and the mass travelling at the pace the round's individuals (walkers spawned as ever) have; a
/// particle woken into an individual keeps its speed class and pace, and one put back to sleep keeps its; the drawn crowd
/// (agents) walks, runs and sprints in the round's mix at its gait's pace; far individuals that are hurt go back into the
/// crowd with their wounds and their slots go to zombies near the player, while hurt ones near and seen stay; the counts
/// add up on every tick; no allocation.
/// </summary>
public static partial class SelfTest
{
    private static void CheckHordePace()
    {
        if (Maps.Find("Horde Stress") is not { Zombies: { Stress: { } st } } map)
        {
            Fail("no Horde Stress map (with a zombies \"stress\" section)");
            return;
        }
        foreach (var round in (int[])[1, 20])
        {
            var ind = PaceIndividuals(round);
            var r = PaceScene(map, st, round, byRound: true);
            Line($"round {round}: individuals (400 walkers spawned) mean {ind:0.00} m/s; crowd pace mean {r.PaceMean:0.00} m/s (walk {r.Shares[0] * 100:0}%, run {r.Shares[1] * 100:0}%, sprint {r.Shares[2] * 100:0}% of the particles); "
                 + $"particles travelled {r.ParticleSpeed:0.00} m/s (walkers {r.ClassSpeed[0]:0.00}, runners {r.ClassSpeed[1]:0.00}, sprinters {r.ClassSpeed[2]:0.00}; {r.Tracked} tracked), the mass {r.MassSpeed:0.00} m/s; "
                 + $"woken: {r.WakeOk} of 3 kept their class (pace off by {r.WakeErr * 100:0.0}% at most); {r.Alloc} B allocated over 0.5 s{(r.Bad > 0 ? $"; counts off on {r.Bad} ticks" : "")}");
            if (MathF.Abs(r.PaceMean / ind - 1) > 0.06f)
                Fail($"round {round}: the crowd's pace ({r.PaceMean:0.00} m/s) should be the individuals' mix ({ind:0.00} m/s)");
            if (MathF.Abs(r.ParticleSpeed / ind - 1) > 0.12f)
                Fail($"round {round}: crowd particles should travel at the individuals' pace ({r.ParticleSpeed:0.00} m/s, individuals {ind:0.00})");
            for (var k = 0; k < 3; k++)
                if (r.ClassCount[k] >= 10 && MathF.Abs(r.ClassSpeed[k] / r.Pace.SpeedOf(k) - 1) > 0.12f)
                    Fail($"round {round}: crowd particles of class {(ZSpeed)k} should travel at its speed ({r.ClassSpeed[k]:0.00} m/s, the class {r.Pace.SpeedOf(k):0.00})");
            if (r.MassSpeed < 0.8f * r.PaceMean || r.MassSpeed > 1.1f * r.PaceMean)
                Fail($"round {round}: the mass should flow at the round's mean pace ({r.MassSpeed:0.00} m/s, the mean {r.PaceMean:0.00})");
            if (r.WakeOk < 3 || r.WakeErr > r.Pace.Spread + 0.01f)
                Fail($"round {round}: a woken crowd zombie should keep its speed class and pace ({r.WakeOk} of 3 kept the class, pace off by {r.WakeErr * 100:0.0}%)");
            if (r.Bad > 0)
                Fail($"round {round}: the counts should add up on every tick ({r.Bad} ticks off)");
            if (r.Alloc != 0)
                Fail($"round {round}: the crowd's ticks should allocate nothing ({r.Alloc} B over 0.5 s)");
        }
        var old = PaceScene(map, st, 20, byRound: false);
        Line($"round 20 with the old fixed pace (paceByRound off): particles travelled {old.ParticleSpeed:0.00} m/s, the mass {old.MassSpeed:0.00} m/s");
        PaceAgents();

        var before = StrayScene(map, st, rejoin: false);
        var after = StrayScene(map, st, rejoin: true);
        foreach (var (s, tag) in ((StrayResult, string)[])[(before, "before (rejoinStrays off)"), (after, "after")])
            Line($"strays {tag}: {s.Strays} hurt individuals left 80 m behind; after 4 s {s.FarLeft} still awake far off, {s.Rejoined} rejoined the crowd, {s.Near} individuals within 30 m of the player (cap {StrayCap}), "
                 + $"the 2 hurt ones next to the player {(s.NearKept ? "still awake" : "put to sleep")}; hurt particles by class {s.HurtCls[0]}/{s.HurtCls[1]}/{s.HurtCls[2]} (strays {s.StrayCls[0]}/{s.StrayCls[1]}/{s.StrayCls[2]}), their health {s.HurtHp:0} (the strays' {s.StrayHp:0}); {s.Alloc} B allocated over 0.5 s{(s.Bad > 0 ? $"; counts off on {s.Bad} ticks" : "")}");
        if (after.FarLeft > 0 || after.Rejoined < after.Strays)
            Fail($"strays: far hurt individuals should go back into the crowd ({after.FarLeft} still awake far off, {after.Rejoined} of {after.Strays} rejoined)");
        if (after.Near < StrayCap * 3 / 4)
            Fail($"strays: the freed slots should go to zombies near the player ({after.Near} individuals within 30 m, cap {StrayCap})");
        if (!after.NearKept)
            Fail("strays: hurt individuals near the player and in view should stay awake");
        if (after.HurtCls[0] != after.StrayCls[0] || after.HurtCls[1] != after.StrayCls[1] || after.HurtCls[2] != after.StrayCls[2] || MathF.Abs(after.HurtHp - after.StrayHp) > 0.5f)
            Fail($"strays: a stray put back should keep its speed class and its wounds (hurt particles {after.HurtCls[0]}/{after.HurtCls[1]}/{after.HurtCls[2]} with {after.HurtHp:0} health, strays {after.StrayCls[0]}/{after.StrayCls[1]}/{after.StrayCls[2]} with {after.StrayHp:0})");
        if (after.Bad > 0 || before.Bad > 0)
            Fail($"strays: the counts should add up on every tick ({after.Bad} + {before.Bad} ticks off)");
        if (after.Alloc != 0)
            Fail($"strays: the ticks should allocate nothing ({after.Alloc} B over 0.5 s)");
    }

    private const int StrayCap = 40;

    /// <summary>The mean speed (m/s) of 400 walkers spawned in round <paramref name="round"/> on an ordinary map (the individuals' mix).</summary>
    private static float PaceIndividuals(int round)
    {
        var now = 0.0;
        using var g = ZGame(0, ref now, tweak: r => r.FirstRoundDelay = 9999);
        g.SelfTestBeginRound(round, now);
        var zm = g.ZMap!;
        double sum = 0;
        for (var k = 0; k < 400; k++)
        {
            var z = g.SpawnZombie(zm, 0, now, type: 0);
            sum += g.ZombieNominalSpeed(z.Speed) * z.Kind.SpeedMult;
        }
        return (float)(sum / 400);
    }

    private sealed class PaceResult
    {
        public CrowdPace Pace;
        public float PaceMean, ParticleSpeed, MassSpeed, WakeErr;
        public readonly float[] ClassSpeed = new float[3], Shares = new float[3];
        public readonly int[] ClassCount = new int[3];
        public int Tracked, WakeOk, Bad;
        public long Alloc = -1;
    }

    /// <summary>
    /// Round <paramref name="round"/> on the valley floor: 400 particles 42-52 m round the player and 5,000 in the mass
    /// 80-110 m out (a few a cell: nothing jams), 2.5 s of travel measured after half a second (headings read, the
    /// cells' flows out of step); then three
    /// particles of each class woken right in front of the player.
    /// </summary>
    private static PaceResult PaceScene(Map map, ZombiesStress st, int round, bool byRound)
    {
        var now = 0.0;
        using var g = ZGame(0, ref now, tweak: r =>
        {
            r.FirstRoundDelay = 9999;
            r.Crowd.PaceByRound = byRound;
        }, onMap: map, setup: g => g.StressSize = 600);
        var me = g.Local!;
        me.ImmuneUntil = 1e9;
        var at = new Vector3(st.Rally.X, map.GroundHeight(st.Rally.X, st.Rally.Y) + 0.05f, st.Rally.Y);
        me.Teleport(at);
        me.Yaw = MathF.PI / 2;
        me.Pitch = 0;
        g.SelfTestBeginRound(round, now);
        var rng = 0x9ACEu ^ (uint)round;
        float Rand()
        {
            rng ^= rng << 13;
            rng ^= rng >> 17;
            rng ^= rng << 5;
            return (rng >> 8) / 16777216f;
        }
        var res = new PaceResult();
        g.SelfTestCrowdScene(c =>
        {
            c.Reserve(1000);
            for (var k = 0; k < 400; k++)
            {
                var dir = World.FlatForward(Rand() * MathF.Tau);
                var d = 42 + Rand() * 10;
                float x = at.X + dir.X * d, z = at.Z + dir.Z * d;
                if (c.Walkable(x, z))
                    c.Add(x, z, 0, 0, (uint)k * 2654435761u ^ 0x51u, g.ServerTick, c.MassHP);
            }
            for (var k = 0; k < 5000; k++)
            {
                var dir = World.FlatForward(Rand() * MathF.Tau);
                var d = 80 + Rand() * 30;
                float x = at.X + dir.X * d, z = at.Z + dir.Z * d;
                if (c.Walkable(x, z))
                    c.AddMass(x, z);
            }
        });
        var c = g.Crowd;
        res.Pace = c.Pace;
        res.PaceMean = c.Pace.Mean;
        for (var i = 0; i < c.Count; i++)
            res.Shares[c.Cls[i]] += 1f / c.Count;
        void Run(double seconds)
        {
            for (var t = 0; t < (int)(seconds * World.TickRate); t++)
            {
                now += World.TickDt;
                g.SelfTestZombiesTick(now);
                if (c.Total + g.AliveZombies + g.CrowdRoundKills != g.CrowdRoundTotal)
                    res.Bad++;
            }
        }
        float Flat(float x, float z) => MathF.Sqrt((x - at.X) * (x - at.X) + (z - at.Z) * (z - at.Z));
        float MassMean()
        {
            double sum = 0;
            var n = 0;
            for (var k = 0; k < c.MassCount.Length; k++)
                if (c.MassCount[k] > 0)
                {
                    var m = c.MassCentre(k);
                    sum += Flat(m.X, m.Z) * c.MassCount[k];
                    n += c.MassCount[k];
                }
            return n > 0 ? (float)(sum / n) : 0;
        }
        Run(0.25);
        var a0 = GC.GetAllocatedBytesForCurrentThread();
        Run(0.25);
        res.Alloc = GC.GetAllocatedBytesForCurrentThread() - a0;
        // Each particle by its look (its index moves when another is removed).
        var start = new Dictionary<uint, (float D, byte Cls)>();
        for (var i = 0; i < c.Count; i++)
            start[c.Look[i]] = (Flat(c.X[i], c.Z[i]), c.Cls[i]);
        var mass0 = MassMean();
        const double span = 2.5;
        Run(span);
        var sumCls = new double[3];
        double sumAll = 0;
        for (var i = 0; i < c.Count; i++)
            if (!c.Held[i] && start.TryGetValue(c.Look[i], out var s0))
            {
                var v = (s0.D - Flat(c.X[i], c.Z[i])) / (float)span;
                sumCls[s0.Cls] += v;
                res.ClassCount[s0.Cls]++;
                sumAll += v;
                res.Tracked++;
            }
        for (var k = 0; k < 3; k++)
            res.ClassSpeed[k] = res.ClassCount[k] > 0 ? (float)(sumCls[k] / res.ClassCount[k]) : 0;
        res.ParticleSpeed = res.Tracked > 0 ? (float)(sumAll / res.Tracked) : 0;
        res.MassSpeed = (mass0 - MassMean()) / (float)span;

        // Waking keeps the class: one particle of each class 6-8 m in front of the player (the nearest in view: woken first).
        var fwd = World.FlatForward(me.Yaw);
        var spots = new Vector2[3];
        var spd = new float[3];
        for (var k = 0; k < 3; k++)
        {
            var side = World.FlatForward(me.Yaw + MathF.PI / 2) * (k - 1) * 1.5f;
            spots[k] = new Vector2(at.X + fwd.X * (6 + k) + side.X, at.Z + fwd.Z * (6 + k) + side.Z);
            var pi = c.Add(spots[k].X, spots[k].Y, 0, 0, 0xA11CEu + (uint)k, g.ServerTick, c.MassHP, cls: k);
            spd[k] = pi >= 0 ? c.Spd[pi] : 0;
        }
        g.SelfTestCrowdAdded(3);
        Run(1 / 60.0);
        for (var k = 0; k < 3; k++)
            foreach (var z in g.Horde)
                if (z.Alive && Vector2.Distance(new Vector2(z.Position.X, z.Position.Z), spots[k]) < 0.3f)
                {
                    var want = byRound ? (ZSpeed)k : z.Speed;
                    if (z.Speed == want)
                        res.WakeOk++;
                    if (byRound)
                        res.WakeErr = MathF.Max(res.WakeErr, MathF.Abs(g.ZombieNominalSpeed(z.Speed) * z.Kind.SpeedMult / spd[k] - 1));
                    break;
                }
        return res;
    }

    /// <summary>The drawn crowd (a client's agents, from its picture): the round's gaits in its mix, each at its gait's pace.</summary>
    private static void PaceAgents()
    {
        var rules = ZombiesRules.Current;
        var pace = CrowdPace.For(rules, rules.Crowd, 7); // (a round with both runners and sprinters)
        var pic = new CrowdPicture();
        pic.Reset(1, 16, 16, 4f, 0f, 0f, 0f);
        for (var c = 0; c < 16 * 16; c++)
            pic.Set(c, 20, HordeCrowd.HeadingByte(MathF.PI / 2)); // (every cell pressing east: walking on the spot or on)
        var a = new CrowdAgents();
        var inst = new CrowdInstances();
        var cam = new Vector3(32, 30, -20);
        ZombieAnim.ClearFrustum();
        a.SetPace(pace);
        var walk = new double[3];
        var n = new int[3];
        var phaseGait = new int[3];
        float[] walk0 = [];
        uint[] ids0 = [];
        for (var f = 0; f < 90; f++)
        {
            if (f == 30)
            {
                walk0 = a.Walk[..a.Count];
                ids0 = a.Id[..a.Count];
            }
            a.FromPicture(pic, null);
            inst.Begin(cam);
            a.Update(cam, 1 / 60f, 0, inst);
            inst.End();
            if (f == 89)
                foreach (var s in inst.Stripes)
                    for (var l = 0; l < CrowdInstances.Lods; l++)
                        for (var k = 0; k < s.Count[l]; k++)
                            phaseGait[Math.Clamp((int)(float)s.Data[l][k].Phase, 0, 2)]++;
        }
        // Metres walked over the last 59 frames by gait (the walk cycle's rate: its gait's pace).
        for (var i = 0; i < a.Count; i++)
        {
            var j = Array.IndexOf(ids0, a.Id[i]);
            if (j < 0 || a.State[i] != CrowdAgents.Mode.Live)
                continue;
            walk[a.Gait[i]] += (a.Walk[i] - walk0[j]) / (59 / 60.0) / a.Pace[i];
            n[a.Gait[i]]++;
        }
        var total = n[0] + n[1] + n[2];
        var shareRun = total > 0 ? n[1] / (float)total : 0;
        var shareSprint = total > 0 ? n[2] / (float)total : 0;
        float Rate(int k) => n[k] > 0 ? (float)(walk[k] / n[k]) : 0;
        Line($"agents (round 7: {pace.RunShare * 100:0}% run, {pace.SprintShare * 100:0}% sprint): {a.Count} agents, {shareRun * 100:0}% running and {shareSprint * 100:0}% sprinting; "
             + $"walk cycle rate {Rate(0):0.00} / {Rate(1):0.00} / {Rate(2):0.00} m/s per share (gaits {pace.Walk:0.0} / {pace.Run:0.0} / {pace.Sprint:0.0}); instances by gait {phaseGait[0]}/{phaseGait[1]}/{phaseGait[2]}");
        if (MathF.Abs(shareRun - pace.RunShare) > 0.06f || MathF.Abs(shareSprint - pace.SprintShare) > 0.06f)
            Fail($"agents: the drawn crowd should run and sprint in the round's mix ({shareRun * 100:0}% / {shareSprint * 100:0}%, the round {pace.RunShare * 100:0}% / {pace.SprintShare * 100:0}%)");
        if (n[1] > 0 && n[2] > 0 && (Rate(1) <= 0 || MathF.Abs(Rate(2) / Rate(1) / (pace.Sprint / pace.Run) - 1) > 0.15f))
            Fail($"agents: sprinters should step at the sprint's pace and runners at the run's ({Rate(2):0.00} vs {Rate(1):0.00} m/s, the gaits {pace.Sprint:0.0} vs {pace.Run:0.0})");
        if (phaseGait[1] == 0 || phaseGait[2] == 0)
            Fail($"agents: the instances should carry the run and sprint gaits ({phaseGait[0]}/{phaseGait[1]}/{phaseGait[2]})");
        var rig = CrowdRig.Bake(0);
        if (rig.Gaits != 3 || rig.Texels.Length != rig.Rows * rig.Width * 4)
            Fail($"agents: the near rig should bake the walk, run and sprint cycles ({rig.Gaits} gaits, {rig.Texels.Length} texels)");
    }

    private sealed class StrayResult
    {
        public int Strays, FarLeft, Rejoined, Near, Bad;
        public bool NearKept;
        public float HurtHp, StrayHp;
        public readonly int[] HurtCls = new int[3], StrayCls = new int[3];
        public long Alloc = -1;
    }

    /// <summary>
    /// Round 20, the individual tier capped at <see cref="StrayCap"/>: the cap filled with zombies woken round the player,
    /// all hurt; the player moves 80 m along the valley (two of the hurt ones with them, right in front) into a crowd of
    /// 300; 4 s.
    /// </summary>
    private static StrayResult StrayScene(Map map, ZombiesStress st, bool rejoin)
    {
        var now = 0.0;
        using var g = ZGame(0, ref now, tweak: r =>
        {
            r.FirstRoundDelay = 9999;
            r.Crowd.IndividualCap = StrayCap;
            r.Crowd.RejoinStrays = rejoin;
        }, onMap: map, setup: g => g.StressSize = 600);
        var me = g.Local!;
        me.ImmuneUntil = 1e9;
        var at = new Vector3(st.Rally.X, map.GroundHeight(st.Rally.X, st.Rally.Y) + 0.05f, st.Rally.Y);
        me.Teleport(at);
        me.Yaw = MathF.PI / 2;
        me.Pitch = 0;
        g.SelfTestBeginRound(20, now);
        var c = g.Crowd;
        var res = new StrayResult();
        var fwd = World.FlatForward(me.Yaw);
        g.SelfTestCrowdScene(cr =>
        {
            for (var k = 0; k < StrayCap; k++)
            {
                var side = World.FlatForward(me.Yaw + MathF.PI / 2) * ((k % 8) - 3.5f) * 1.2f;
                var d = 5 + k / 8 * 1.2f;
                cr.Add(at.X + fwd.X * d + side.X, at.Z + fwd.Z * d + side.Z, 0, 0, (uint)k * 0x9E3779B9u ^ 0x5742u, g.ServerTick, cr.MassHP);
            }
        });
        void Run(double seconds)
        {
            for (var t = 0; t < (int)(seconds * World.TickRate); t++)
            {
                now += World.TickDt;
                g.SelfTestZombiesTick(now);
                if (c.Total + g.AliveZombies + g.CrowdRoundKills != g.CrowdRoundTotal)
                    res.Bad++;
            }
        }
        Run(1);
        // All hurt (as if shot), and awake long enough to sleep.
        var near = new List<Zombie>();
        foreach (var z in g.Horde)
            if (z.Alive)
            {
                z.Health = Math.Max(1, z.MaxHealth / 2);
                z.CrowdWokeAt = now - 10;
                res.Strays++;
                if (near.Count < 2)
                    near.Add(z);
            }
        // The player walks off 80 m down the valley (the two with them, just in front), into a crowd waiting there.
        var to = new Vector3(at.X, 0, at.Z + 80);
        to.Y = map.GroundHeight(to.X, to.Z) + 0.05f;
        me.Teleport(to);
        for (var k = 0; k < near.Count; k++)
        {
            var p = new Vector3(to.X + fwd.X * 3 + k * 1.2f, to.Y, to.Z + fwd.Z * 3);
            near[k].Sim.Position = near[k].PrevPos = near[k].RenderPos = p;
        }
        res.Strays -= near.Count;
        foreach (var z in g.Horde)
            if (z.Alive && !near.Contains(z))
            {
                res.StrayCls[(int)z.Speed]++;
                res.StrayHp += z.Health;
            }
        var rng = 0x57A7u;
        float Rand()
        {
            rng ^= rng << 13;
            rng ^= rng >> 17;
            rng ^= rng << 5;
            return (rng >> 8) / 16777216f;
        }
        var added = 0;
        for (var k = 0; k < 300; k++)
        {
            var dir = World.FlatForward(me.Yaw + (Rand() - 0.5f) * 2.2f);
            var d = 12 + Rand() * 23;
            float x = to.X + dir.X * d, z = to.Z + dir.Z * d;
            if (c.Walkable(x, z) && c.Add(x, z, 0, 0, (uint)k * 2654435761u ^ 0x77u, g.ServerTick, c.MassHP) >= 0)
                added++;
        }
        g.SelfTestCrowdAdded(added);
        var rejoined0 = g.CrowdStraysRejoined;
        Run(2);
        var a0 = GC.GetAllocatedBytesForCurrentThread();
        Run(0.5);
        res.Alloc = GC.GetAllocatedBytesForCurrentThread() - a0;
        Run(1.5);
        res.Rejoined = g.CrowdStraysRejoined - rejoined0;
        foreach (var z in g.Horde)
            if (z.Alive)
            {
                var d = Vector2.Distance(new Vector2(z.Position.X, z.Position.Z), new Vector2(to.X, to.Z));
                if (d < 30)
                    res.Near++;
                else if (d > 44 && z.Health < z.MaxHealth)
                    res.FarLeft++;
            }
        res.NearKept = near.TrueForAll(z => z.Alive && g.Horde.Contains(z));
        for (var i = 0; i < c.Count; i++)
            if (c.HP[i] < c.MassHP)
            {
                res.HurtCls[c.Cls[i]]++;
                res.HurtHp += c.HP[i];
            }
        return res;
    }
}
