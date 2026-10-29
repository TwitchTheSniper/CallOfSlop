using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Self-test section "Horde wake" (docs/zombies/MASS-HORDE.md, "Waking and sleeping"): the mass horde wakes what the
/// players can see and puts to sleep what they can't. Short seeded scenes on Horde Stress's valley floor (a crowd of
/// 4,000 seeded round the player, the individual tier capped at 120, the player shooting what they look at): promoted
/// zombies mostly in the player's view, unseen ones put to sleep first, nothing woken again just after being put to sleep
/// over a turn, the horde closing in (mean distance falling, the front not standing at the promote range), woken
/// individuals from a wall ahead staying in front; the sight's ground test hides only what the ground hides; two players
/// back to back see all round; no allocation. HORDE_WAKE_COMPARE=1 also runs the scenes with the old behaviour's tuning.
/// </summary>
public static partial class SelfTest
{
    private static void CheckHordeWake()
    {
        if (Maps.Find("Horde Stress") is not { Zombies: { Stress: { } st }, Terrain: { } terrain } map)
        {
            Fail("no Horde Stress map (with terrain and a zombies \"stress\" section)");
            return;
        }
        WakeSightChecks(map, terrain);
        var compare = Environment.GetEnvironmentVariable("HORDE_WAKE_COMPARE") == "1";
        var old = compare ? WakeScene(map, st, ring: true, legacy: true) : null;
        var ring = WakeScene(map, st, ring: true, legacy: false);
        var wallOld = compare ? WakeScene(map, st, ring: false, legacy: true) : null;
        var wall = WakeScene(map, st, ring: false, legacy: false);

        if (ring.ViewShare < 0.6f || ring.WokeSeen < 3 * ring.WokeUnseen)
            Fail($"ring: most woken zombies should be in the player's view ({ring.ViewShare * 100:0}% of the individuals in view; woken {ring.WokeSeen} seen, {ring.WokeUnseen} unseen)");
        if (ring.TurnSleptSeen * 3 > ring.TurnSleptUnseen || ring.TurnWokeSeen < 3 * ring.TurnWokeUnseen)
            Fail($"ring: turning round should put to sleep what the player no longer sees and wake what they now see ({ring.TurnSleptUnseen} unseen and {ring.TurnSleptSeen} seen put to sleep, {ring.TurnWokeSeen} seen and {ring.TurnWokeUnseen} unseen woken)");
        if (ring.Rewoken > 0 || ring.TurnFlicker > 0)
            Fail($"ring: nothing should flicker between the tiers ({ring.Rewoken} woken within {Game.CrowdFlickerSeconds:0} s of sleeping, {ring.TurnFlicker} over the turn)");
        if (ring.MeanEnd > ring.MeanStart - 8 || ring.FrontEnd > 18f || ring.StandingFront > 0.2f)
            Fail($"ring: the horde should keep closing in (mean distance {ring.MeanStart:0.0} -> {ring.MeanEnd:0.0} m, its nearest 5% at {ring.FrontEnd:0.0} m, {ring.StandingFront * 100:0}% of the particles between {HordeWakeBand} m and the promote range standing)");
        if (wall.BehindShare > 0.15f)
            Fail($"wall: woken zombies from a horde ahead should stay in front ({wall.BehindShare * 100:0}% behind the player)");
        if (ring.Bad > 0 || wall.Bad > 0)
            Fail($"the counts should add up on every tick ({ring.Bad} + {wall.Bad} ticks off)");
        if (ring.Alloc != 0)
            Fail($"the crowd's steady ticks with sight should allocate nothing ({ring.Alloc} B over 0.5 s)");
    }

    /// <summary>The inner edge (m) of the band where the old front stood waiting (out to the promote range).</summary>
    private const float HordeWakeBand = 12f;

    private sealed class WakeResult
    {
        public float StandingFront, ViewShare, BehindShare, ViewShareAfterTurn, MeanStart, MeanEnd, FrontEnd;
        public int WokeSeen, WokeUnseen, TurnSleptSeen, TurnSleptUnseen, TurnWokeSeen, TurnWokeUnseen, SleptSeen, SleptUnseen, Rewoken, TurnFlicker, Bad;
        public long Alloc = -1;
    }

    /// <summary>The individuals in <paramref name="me"/>'s view cone (the sight's angles) and behind them.</summary>
    private static (int Alive, int InView, int Behind) WakeCount(Game g, Player me)
    {
        var fwd = World.FlatForward(me.Yaw);
        var cosView = MathF.Cos(g.Crowd.Tuning.SightHalfAngle * MathF.PI / 180f);
        int alive = 0, inView = 0, behind = 0;
        foreach (var z in g.Horde)
            if (z.Alive)
            {
                alive++;
                var v = z.Position - me.Position;
                v.Y = 0;
                var l = v.Length();
                var cos = l > 0.1f ? Vector3.Dot(v / l, fwd) : 1;
                if (cos >= cosView)
                    inView++;
                if (cos < 0)
                    behind++;
            }
        return (alive, inView, behind);
    }

    /// <summary>The crowd's (particles and mass) mean flat distance to <paramref name="at"/>, and its nearest 5%'s.</summary>
    private static (float Mean, float Front) WakeSpread(HordeCrowd c, Vector3 at, List<float> scratch)
    {
        scratch.Clear();
        double sum = 0;
        var n = 0;
        for (var i = 0; i < c.Count; i++)
        {
            var d = Vector2.Distance(new Vector2(c.X[i], c.Z[i]), new Vector2(at.X, at.Z));
            scratch.Add(d);
            sum += d;
            n++;
        }
        for (var k = 0; k < c.MassCount.Length; k++)
            if (c.MassCount[k] > 0)
            {
                var m = c.MassCentre(k);
                var d = Vector2.Distance(new Vector2(m.X, m.Z), new Vector2(at.X, at.Z));
                sum += d * c.MassCount[k];
                n += c.MassCount[k];
                for (var j = 0; j < c.MassCount[k]; j++)
                    scratch.Add(d);
            }
        scratch.Sort();
        return (n > 0 ? (float)(sum / n) : 0, scratch.Count > 0 ? scratch[scratch.Count / 20] : 0);
    }

    private static WakeResult WakeScene(Map map, ZombiesStress st, bool ring, bool legacy)
    {
        var now = 0.0;
        using var g = ZGame(0, ref now, tweak: r =>
        {
            r.FirstRoundDelay = 9999;
            r.Crowd.IndividualCap = 120;
            if (legacy)
            {
                // The old behaviour: no sight (everything counts at its distance), no swaps, the front standing at the
                // promote range, individuals closing in from all round.
                r.Crowd.UnseenWeight = 1f;
                r.Crowd.SwapMargin = 100f;
                r.Crowd.HeldPace = 0f;
                r.Crowd.SurroundArc = 360f;
            }
        }, onMap: map, setup: g => g.StressSize = 600);
        var me = g.Local!;
        me.ImmuneUntil = 1e9;
        var at = new Vector3(st.Rally.X, map.GroundHeight(st.Rally.X, st.Rally.Y) + 0.05f, st.Rally.Y);
        me.Teleport(at);
        me.Yaw = MathF.PI / 2; // (looking along +x, down the valley floor)
        me.Pitch = 0;
        g.SelfTestBeginRound(1, now);
        var rng = 0x5EEDu;
        float Rand()
        {
            rng ^= rng << 13;
            rng ^= rng >> 17;
            rng ^= rng << 5;
            return (rng >> 8) / 16777216f;
        }
        const int size = 4000;
        g.SelfTestCrowdScene(c =>
        {
            c.Reserve(size);
            for (var k = 0; k < size; k++)
            {
                // All round (a ring), or a wall ahead (+-60 degrees of the look); 12 m out to 80 m, even over the area.
                var a = ring ? Rand() * MathF.Tau : (Rand() - 0.5f) * 2.1f;
                var d = MathF.Sqrt(12 * 12 + Rand() * (80 * 80 - 12 * 12));
                var dir = World.FlatForward(me.Yaw + a);
                float x = at.X + dir.X * d, z = at.Z + dir.Z * d;
                if (!c.Walkable(x, z))
                    continue;
                if (d > 45)
                    c.AddMass(x, z);
                else
                    c.Add(x, z, 0, 0, (uint)k * 2654435761u, g.ServerTick, c.MassHP);
            }
        });
        var c = g.Crowd;
        var res = new WakeResult();
        var scratch = new List<float>(size);
        (res.MeanStart, _) = WakeSpread(c, at, scratch);
        var badText = "";
        var kills = 0;
        var shoot = true;
        void Run(double seconds, float turnPerSecond = 0)
        {
            for (var t = 0; t < (int)(seconds * World.TickRate); t++)
            {
                now += World.TickDt;
                me.Yaw += turnPerSecond * World.TickDt;
                g.SelfTestZombiesTick(now);
                // The player shoots: every 0.2 s the individual nearest the middle of their view, within 30 m.
                if (shoot && g.ServerTick % 12 == 0)
                {
                    Zombie? best = null;
                    var bestCos = MathF.Cos(25 * MathF.PI / 180f);
                    var fwd = World.FlatForward(me.Yaw);
                    foreach (var z in g.Horde)
                    {
                        if (!z.Alive)
                            continue;
                        var v = z.Position - me.Position;
                        v.Y = 0;
                        var l = v.Length();
                        if (l > 30 || l < 0.1f)
                            continue;
                        var cos = Vector3.Dot(v / l, fwd);
                        if (cos > bestCos)
                        {
                            bestCos = cos;
                            best = z;
                        }
                    }
                    if (best != null && g.DamageZombie(me, best, best.Health, head: true, melee: false, now))
                        kills++;
                }
                if (c.Total + g.AliveZombies + g.CrowdRoundKills != g.CrowdRoundTotal && res.Bad++ == 0)
                    badText = $"crowd {c.Count} + mass {c.MassTotal} + alive {g.AliveZombies} + killed {g.CrowdRoundKills} != {g.CrowdRoundTotal}";
            }
        }
        var tag = $"{(ring ? "ring" : "wall")}, {(legacy ? "old tuning" : "sight")}";
        string Snap(double t)
        {
            var (alive, inView, behind) = WakeCount(g, me);
            var (mean, front) = WakeSpread(c, at, scratch);
            var moving = 0;
            for (var i = 0; i < c.Count; i++)
                if (c.VX[i] * c.VX[i] + c.VZ[i] * c.VZ[i] > 0.09f)
                    moving++;
            return $"  {tag} {t:0} s: individuals {alive} ({inView} in view, {behind} behind), particles {c.Count} ({moving} moving, {c.Near} waiting), mass {c.MassTotal}, mean distance {mean:0.0} m, nearest 5% {front:0.0} m";
        }
        Line($"{tag}: {c.Count} particles, {c.MassTotal} mass, the individual cap {g.CrowdIndividualCap}");
        var clock = 0.0;
        for (var s = 0; s < (ring ? 4 : 5); s++)
        {
            if (s == 2 && !legacy && ring)
            {
                shoot = false; // (a kill rolls drops: not the crowd's)
                var a0 = GC.GetAllocatedBytesForCurrentThread();
                Run(0.5);
                res.Alloc = GC.GetAllocatedBytesForCurrentThread() - a0;
                shoot = true;
                Run(1.5);
            }
            else
                Run(2);
            clock += 2;
            Line(Snap(clock));
        }
        var (alive0, inView0, behind0) = WakeCount(g, me);
        res.ViewShare = inView0 / (float)Math.Max(1, alive0);
        res.BehindShare = behind0 / (float)Math.Max(1, alive0);
        (res.MeanEnd, res.FrontEnd) = WakeSpread(c, at, scratch);
        // The front standing: particles between the band and the promote range that aren't walking (the old front stood there).
        int band = 0, standing = 0;
        for (var i = 0; i < c.Count; i++)
        {
            var d = Vector2.Distance(new Vector2(c.X[i], c.Z[i]), new Vector2(at.X, at.Z));
            if (d < HordeWakeBand || d > c.Tuning.PromoteRange)
                continue;
            band++;
            if (c.VX[i] * c.VX[i] + c.VZ[i] * c.VZ[i] < 0.01f)
                standing++;
        }
        res.StandingFront = standing / (float)Math.Max(1, band);
        res.WokeSeen = g.CrowdWokeSeen;
        res.WokeUnseen = g.CrowdWokeUnseen;
        res.SleptSeen = g.CrowdSleptSeen;
        res.SleptUnseen = g.CrowdSleptUnseen;
        res.Rewoken = g.CrowdRewoken;
        if (ring)
        {
            // Turn round (180 degrees in a second), then 3 s more.
            var r0 = g.CrowdRewoken;
            var (ss0, su0, ws0, wu0) = (g.CrowdSleptSeen, g.CrowdSleptUnseen, g.CrowdWokeSeen, g.CrowdWokeUnseen);
            Run(1, MathF.PI);
            Run(3);
            clock += 4;
            Line(Snap(clock) + " (after turning round)");
            var (a1, v1, _) = WakeCount(g, me);
            res.ViewShareAfterTurn = v1 / (float)Math.Max(1, a1);
            res.TurnFlicker = g.CrowdRewoken - r0;
            (res.TurnSleptSeen, res.TurnSleptUnseen, res.TurnWokeSeen, res.TurnWokeUnseen) = (g.CrowdSleptSeen - ss0, g.CrowdSleptUnseen - su0, g.CrowdWokeSeen - ws0, g.CrowdWokeUnseen - wu0);
        }
        Line($"  {tag}: promoted {c.Promoted}, woken {g.CrowdWokeSeen} seen + {g.CrowdWokeUnseen} unseen, put to sleep {c.Demoted} ({g.CrowdSleptUnseen} unseen, {g.CrowdSleptSeen} seen; over the turn {res.TurnSleptUnseen} unseen, {res.TurnSleptSeen} seen, woken {res.TurnWokeSeen} seen + {res.TurnWokeUnseen} unseen), woken again within {Game.CrowdFlickerSeconds:0} s {g.CrowdRewoken}, "
             + $"shot {kills}; mean distance {res.MeanStart:0.0} -> {res.MeanEnd:0.0} m, {res.StandingFront * 100:0}% standing between {HordeWakeBand} m and the promote range{(res.Alloc >= 0 ? $", {res.Alloc} B allocated over 0.5 s" : "")}{(res.Bad > 0 ? "; counts off: " + badText : "")}");
        return res;
    }

    /// <summary>
    /// The sight on its own: two players back to back see all round (a point behind one is in front of the other), the
    /// unseen weight, and the ground test hides only what the ground really hides (a fine march to the top of a body).
    /// </summary>
    private static void WakeSightChecks(Map map, TerrainData terrain)
    {
        var t = new CrowdTuning();
        var s = new CrowdSight();
        float minX = -map.HalfX, minZ = -map.HalfZ, cell = 4f;
        int cols = (int)MathF.Ceiling(map.HalfX * 2 / cell), rows = (int)MathF.Ceiling(map.HalfZ * 2 / cell);
        s.Setup(null, minX, minZ, cell, cols, rows);
        s.Begin(t);
        var eye = new Vector3(0, 1.6f, 0);
        s.AddView(eye, MathF.PI / 2, 0); // (looking +x)
        var front = s.Score(10, 1, 0, out var seenF, out _);
        var back = s.Score(-10, 1, 0, out var seenB, out _);
        s.AddView(eye, -MathF.PI / 2, 0); // (a second player, looking -x)
        var backTwo = s.Score(-10, 1, 0, out var seenB2, out _);
        Line($"sight: ahead 10 m counts {front:0.0} (seen {seenF}), behind 10 m {back:0.0} (seen {seenB}), with a second player back to back {backTwo:0.0} (seen {seenB2})");
        if (!seenF || seenB || !seenB2 || MathF.Abs(front - 10) > 0.01f || MathF.Abs(back - 10 * t.UnseenWeight) > 0.01f || MathF.Abs(backTwo - 10) > 0.01f)
            Fail("sight: a zombie ahead should count at its distance, one behind at the unseen weight, and one a second player sees at its distance");

        // The ground test on Horde Stress: from a few eyes on the slopes, the knolls and the Overlook.
        s.Setup(terrain, minX, minZ, cell, cols, rows);
        int tested = 0, hidden = 0, wrong = 0;
        var probes = new[] { new Vector2(150, -60), new Vector2(150, 60), new Vector2(100, 0), new Vector2(-150, 0), new Vector2(40, 90) };
        foreach (var p in probes)
        {
            var e = new Vector3(p.X, terrain.Sample(p.X, p.Y) + World.EyeHeight, p.Y);
            s.Begin(t);
            s.AddView(e, 0, 0);
            s.Occlude(40);
            tested += s.Tested;
            hidden += s.Hidden;
            for (var r = 0; r < rows; r++)
                for (var c = 0; c < cols; c++)
                {
                    float x = minX + (c + 0.5f) * cell, z = minZ + (r + 0.5f) * cell;
                    if (!s.GroundHides(0, x, z))
                        continue;
                    // Really behind the ground? The line to the top of a body there passes under the terrain somewhere.
                    var top = terrain.Sample(x, z) + HordeCrowd.BodyHeight;
                    var under = false;
                    for (var k = 0.02f; k < 0.97f && !under; k += 0.01f)
                        under = terrain.Sample(e.X + (x - e.X) * k, e.Z + (z - e.Z) * k) > e.Y + (top - e.Y) * k;
                    if (!under)
                        wrong++;
                }
        }
        Line($"sight's ground test: {tested} cells tested from {probes.Length} eyes, {hidden} hidden, {wrong} hidden that the ground doesn't hide");
        if (hidden == 0 || wrong > 0)
            Fail("sight: the ground test should hide cells behind the ground on Horde Stress, and only those");
    }
}
