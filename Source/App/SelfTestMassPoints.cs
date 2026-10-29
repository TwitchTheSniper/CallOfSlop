using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Self-test section "Mass points" (board item dev-mass-points-mp01; docs/zombies/MASS-HORDE.md, "Combat"): shots and
/// blasts into the mass tier on Mass Horde Test pay exactly what the same damage on individuals pays. Scripted volleys
/// (body shots to a kill, a body shot then head shots, Double Points, Insta-Kill) on an individual walker and on a mass
/// zombie at the same spot give the same points shot by shot, the same kills and headshots; a blast over ten individuals
/// and over ten mass zombies at the same spots pays the same; a big blast pays every hit and kill but rolls at most
/// <see cref="Game.CrowdBlastDrops"/> drops; a wounded mass zombie keeps its wound through the flow and comes out of the
/// mass hurt; a penetrating ray finds the next mass zombie behind; the drawn crowd bleeds and loses the zombie a kill
/// took; the ray test allocates nothing and is cheap through a packed horde; the counts add up throughout.
/// </summary>
public static partial class SelfTest
{
    private static void CheckMassPoints()
    {
        var now = 0.0;
        if (Maps.Find("Mass Horde Test") is not { Zombies: { Crowd: > 0 } } map)
        {
            Fail("no Mass Horde Test map (or no \"crowd\" in its zombies section)");
            return;
        }
        using var g = ZGame(0, ref now, tweak: r => r.FirstRoundDelay = 9999, onMap: map);
        var me = g.Local!;
        me.ImmuneUntil = 1e9;
        me.Teleport(Vector3.Zero);
        g.CrowdOverride = 500;
        g.SelfTestBeginRound(1, now);
        g.SelfTestZombiesTick(now += World.TickDt); // (the crowd's grid)
        var c = g.Crowd;
        var zp = g.ZP(me);
        var r = g.ZRules;
        if (!c.Tuning.Mass || c.MassCols == 0)
        {
            Fail("Mass Horde Test's crowd should have the mass tier on");
            return;
        }

        // A clean field: nothing in the crowd; the round's total follows what the test puts in.
        c.Clear();
        g.CrowdRoundTotal = g.AliveZombies + g.CrowdRoundKills;
        var counted = true;
        void Check() => counted &= c.Total + g.AliveZombies + g.CrowdRoundKills == g.CrowdRoundTotal && c.WoundsConsistent();

        // A spot 14 m from the player with a clear line to it at body and head height.
        var eye = me.Eye;
        var target = Vector3.Zero;
        for (var a = 0; a < 24 && target == Vector3.Zero; a++)
        {
            var yaw = a * MathF.Tau / 24;
            var p = new Vector3(MathF.Cos(yaw) * 14, 0, MathF.Sin(yaw) * 14);
            p.Y = c.GroundAt(p.X, p.Z);
            if (!c.Walkable(p.X, p.Z) || !Clear(p + new Vector3(0, 1.0f, 0)) || !Clear(p + new Vector3(0, 1.65f, 0)))
                continue;
            target = p;
        }
        bool Clear(Vector3 to)
        {
            var d = to - eye;
            var l = d.Length();
            return World.Query.Raycast(eye, d / l, l + 1, QueryFilter.Shots) >= l + 0.5f;
        }
        if (target == Vector3.Zero)
        {
            Fail("no clear line from the player to a spot 14 m away on Mass Horde Test");
            return;
        }
        var cell = c.MassCellAt(target.X, target.Z);

        // ---- Volleys: an individual and a mass zombie at the same spot, the same shots ----
        var points = new List<int>();
        int Shoot(Vector3 feet, float height)
        {
            var p0 = zp.Points;
            now += 0.5; // (the gun's fire rate)
            g.SelfTestServerShot(me, me.Eye, Vector3.Normalize(feet + new Vector3(0, height, 0) - me.Eye), now);
            return zp.Points - p0;
        }
        string Volley(bool mass, float[] heights, bool doublePts, bool insta, out Vector3 feet)
        {
            points.Clear();
            g.ZDoublePointsUntil = doublePts ? now + 1e6 : 0;
            g.ZInstaKillUntil = insta ? now + 1e6 : 0;
            var k0 = zp.Kills;
            var h0 = zp.Headshots;
            Zombie? z = null;
            if (mass)
            {
                c.AddMass(target.X, target.Z);
                g.CrowdRoundTotal++;
                feet = c.MassFeet(cell, c.MassCount[cell] - 1);
            }
            else
            {
                z = g.SelfTestSpawnZombie(0, now);
                g.CrowdRoundTotal++;
                feet = c.MassFeet(cell, 0); // (where the mass zombie will stand)
                z.Sim.Position = z.PrevPos = feet;
                z.State = ZState.Chase;
                z.Yaw = MathF.Atan2(me.Position.X - feet.X, -(me.Position.Z - feet.Z));
                for (var i = 0; i < z.History.Length; i++)
                    z.History[i] = feet;
            }
            for (var s = 0; s < 40; s++)
            {
                points.Add(Shoot(feet, heights[Math.Min(s, heights.Length - 1)]));
                Check();
                if (mass ? c.MassCount[cell] == 0 : z!.State == ZState.Dead)
                    break;
            }
            g.ZDoublePointsUntil = g.ZInstaKillUntil = 0;
            return $"{string.Join(",", points)} | kills {zp.Kills - k0}, headshots {zp.Headshots - h0}";
        }
        var body = new[] { 1.0f };
        var headAfter = new[] { 1.0f, 1.65f };
        var hp = c.MassHP;
        var walker = g.SelfTestSpawnZombie(0, now);
        var walkerHp = walker.MaxHealth;
        walker.Health = 0;
        walker.State = ZState.Dead; // (only for its health: never counted)
        var scripts = new (string Name, float[] Heights, bool Double, bool Insta)[]
        {
            ("body shots to a kill", body, false, false),
            ("a body shot then head shots", headAfter, false, false),
            ("Double Points", headAfter, true, false),
            ("Insta-Kill", body, false, true),
        };
        var allSame = true;
        var massHits0 = g.MassShotHits;
        var massKills0 = g.MassShotKills;
        foreach (var (name, heights, dbl, insta) in scripts)
        {
            var ind = Volley(false, heights, dbl, insta, out _);
            var mas = Volley(true, heights, dbl, insta, out _);
            Line($"mass points: {name}: individual {ind}; mass {mas}");
            if (ind != mas)
            {
                allSame = false;
                Fail($"mass points: {name}: a mass zombie should pay what an individual pays for the same shots (individual {ind}; mass {mas})");
            }
            if (!ind.Contains("kills 1"))
                Fail($"mass points: {name}: the volley should end in a kill ({ind})");
        }
        var massHits = g.MassShotHits - massHits0;
        var massKills = g.MassShotKills - massKills0;
        Line($"mass points: health individual {walkerHp}, mass {hp}; mass shot hits {massHits}, kills {massKills}");
        if (walkerHp != (int)hp)
            Fail($"mass points: a mass zombie's health should be an individual walker's ({hp} vs {walkerHp})");
        if (massKills != scripts.Length || massHits <= massKills)
            Fail($"mass points: the mass volleys should hit several times and kill once each ({massHits} hits, {massKills} kills)");
        if (!allSame)
            Line("mass points: the volleys differ (above)");

        // A wound stays: one body shot, then the mass zombie's health is less and the pool holds it.
        c.AddMass(target.X, target.Z);
        g.CrowdRoundTotal++;
        var p1 = zp.Points;
        Shoot(c.MassFeet(cell, 0), 1.0f);
        var wounded = c.MassHealth(cell, 0);
        Line($"mass points: one body shot: +{zp.Points - p1} points, health {hp} -> {wounded}, {c.Wounded} wounded");
        if (zp.Points - p1 != r.HitPoints || wounded >= hp || c.Wounded != 1)
            Fail("mass points: a body shot on a mass zombie should pay hit points and leave it wounded");
        Check();

        // ---- Blasts: ten individuals and ten mass zombies at the same spots ----
        c.Clear();
        g.CrowdRoundTotal = g.AliveZombies + g.CrowdRoundKills;
        var centre = c.MassCentre(cell) + new Vector3(0, 0.6f, 0);
        var spots = new Vector3[10];
        for (var k = 0; k < 10; k++)
            c.AddMass(centre.X, centre.Z);
        for (var k = 0; k < 10; k++)
            spots[k] = c.MassFeet(cell, k);
        c.Clear();
        var blastR = 3.2f;
        var blastDmg = hp * 1.6f;
        var bp0 = zp.Points;
        var bk0 = zp.Kills;
        var ind10 = new List<Zombie>();
        foreach (var s in spots)
        {
            var z = g.SelfTestSpawnZombie(0, now);
            g.CrowdRoundTotal++;
            z.Sim.Position = z.PrevPos = s;
            z.State = ZState.Chase;
            for (var i = 0; i < z.History.Length; i++)
                z.History[i] = s;
            ind10.Add(z);
        }
        g.SelfTestBlast(me, centre, blastR, blastDmg, now += 0.1);
        var indPts = zp.Points - bp0;
        var indKills = zp.Kills - bk0;
        foreach (var z in ind10)
            if (z.State != ZState.Dead)
                g.DamageZombie(me, z, 1 << 20, false, melee: false, now); // (out of the way)
        Check();
        for (var k = 0; k < 10; k++)
            c.AddMass(centre.X, centre.Z);
        g.CrowdRoundTotal += 10;
        bp0 = zp.Points;
        bk0 = zp.Kills;
        g.SelfTestBlast(me, centre, blastR, blastDmg, now += 0.1);
        var massPts = zp.Points - bp0;
        var massBlastKills = zp.Kills - bk0;
        Check();
        Line($"mass points: a blast over ten: individuals +{indPts} points, {indKills} killed; mass +{massPts} points, {massBlastKills} killed, {c.MassCount[cell]} left ({c.Wounded} wounded)");
        if (indPts != massPts || indKills != massBlastKills)
            Fail($"mass points: a blast on mass zombies should pay what it pays on individuals at the same spots ({massPts} vs {indPts} points, {massBlastKills} vs {indKills} kills)");
        if (massBlastKills == 0 || c.MassCount[cell] == 0 || c.Wounded != c.MassCount[cell])
            Fail($"mass points: the blast should kill the near mass zombies and wound the far ones ({massBlastKills} killed, {c.MassCount[cell]} left, {c.Wounded} wounded)");

        // ---- A big blast in a packed blob: every hit and kill pays; drops stay capped ----
        c.Clear();
        g.CrowdRoundTotal = g.AliveZombies + g.CrowdRoundKills;
        var blob = c.SeedMass(centre.X, centre.Z, 600, c.Tuning.MassCapacity, 14f);
        g.CrowdRoundTotal += blob;
        float bigR = 6f, bigDmg = hp * 4;
        int expHits = 0, expKills = 0;
        c.MassCellsNear(centre, bigR, out var c0, out var c1, out var r0, out var r1);
        for (var rr = r0; rr <= r1; rr++)
            for (var cc = c0; cc <= c1; cc++)
            {
                var k = rr * c.MassCols + cc;
                for (var s = 0; s < c.MassCount[k]; s++)
                {
                    var d = (c.MassFeet(k, s) + new Vector3(0, 1.1f, 0) - centre).Length();
                    if (d > bigR)
                        continue;
                    var dmg = MathF.Round(bigDmg * MathF.Pow(1f - d / bigR, 1.4f));
                    if (dmg <= 0)
                        continue;
                    expHits++;
                    if (dmg >= hp)
                        expKills++;
                }
            }
        bp0 = zp.Points;
        bk0 = zp.Kills;
        var drops0 = g.CrowdDropRolls;
        var mass0 = c.MassTotal;
        g.SelfTestBlast(me, centre, bigR, bigDmg, now += 0.1);
        var bigPts = zp.Points - bp0;
        var bigKills = zp.Kills - bk0;
        var drops = g.CrowdDropRolls - drops0;
        var expPts = expKills * r.KillPoints + (expHits - expKills) * r.HitPoints;
        Check();
        Line($"mass points: a blast in a blob of {blob}: {bigKills} killed (expected {expKills}), {expHits - expKills} hurt, +{bigPts} points (expected {expPts}), {drops} drop rolls, mass {mass0} -> {c.MassTotal}");
        if (bigKills != expKills || bigPts != expPts || mass0 - c.MassTotal != bigKills)
            Fail($"mass points: a blast in the mass should pay each hit and kill ({bigPts} points, {bigKills} kills; expected {expPts}, {expKills})");
        if (drops > Game.CrowdBlastDrops || (bigKills >= Game.CrowdBlastDrops && drops != Game.CrowdBlastDrops))
            Fail($"mass points: a blast should roll {Game.CrowdBlastDrops} drops at most ({drops} for {bigKills} kills)");

        // ---- Pierce: the next mass zombie behind the first on the same ray ----
        {
            var o = new Vector3(centre.X - 30, c.GroundAt(centre.X, centre.Z) + 1.0f, centre.Z + 0.3f);
            var dir = Vector3.UnitX;
            var dist = 60f;
            var first = c.MassRayHit(o, dir, ref dist, -1f, out var fc, out var fs, out _);
            var dist2 = 60f;
            var second = first && c.MassRayHit(o, dir, ref dist2, dist + 1e-3f, out var sc, out var ss, out _) && (sc != fc || ss != fs) && dist2 > dist;
            Line($"mass points: a ray through the blob hits at {dist:0.00} m, the next behind at {dist2:0.00} m");
            if (!first || !second)
                Fail("mass points: a ray through the mass should hit a zombie and find the next one behind it (pierce)");
        }

        // ---- A wound goes with its zombie: through the flow, out of the mass as a hurt particle ----
        c.Clear();
        g.CrowdRoundTotal = g.AliveZombies + g.CrowdRoundKills;
        var far = new Vector3(target.X * 3f, 0, target.Z * 3f); // (42 m out: inside the release range, outside promotion)
        if (!c.Walkable(far.X, far.Z))
            far = target;
        var fcell = c.MassCellAt(far.X, far.Z);
        for (var k = 0; k < 5; k++)
            c.AddMass(far.X, far.Z);
        g.CrowdRoundTotal += 5;
        c.SetMassHealth(fcell, 2, hp - 37);
        var hurtOut = false;
        for (var t = 0; t < 240 && !hurtOut; t++)
        {
            g.SelfTestZombiesTick(now += World.TickDt);
            Check();
            for (var i = 0; i < c.Count; i++)
                hurtOut |= c.HP[i] == hp - 37;
            foreach (var z in g.Horde)
                hurtOut |= z.Alive && z.Health == (int)(hp - 37);
        }
        Line($"mass points: a wounded mass zombie came out hurt {hurtOut} ({c.MassTotal} left in the mass, {c.Count} particles, {c.Wounded} wounded)");
        if (!hurtOut)
            Fail("mass points: a wounded mass zombie should keep its wound through the flow and come out of the mass hurt");

        // ---- What players see: blood in the drawn crowd, and a kill sinks a drawn zombie ----
        c.Clear();
        g.CrowdRoundTotal = g.AliveZombies + g.CrowdRoundKills;
        for (var k = 0; k < 6; k++)
            c.AddMass(target.X, target.Z);
        g.CrowdRoundTotal += 6;
        me.Teleport(Vector3.Zero);
        var cam = me.Eye;
        ZombieAnim.SetFrustum(Matrix4x4.CreateLookAt(cam, target + new Vector3(0, 1, 0), Vector3.UnitY) * Matrix4x4.CreatePerspectiveFieldOfView(1.1f, 16f / 9f, 0.05f, 900));
        CrowdJobs.Serial = true;
        try
        {
            var view = new CrowdView();
            var tv = now;
            for (var k = 0; k < 3; k++)
                view.Prepare(g, cam, tv += 1 / 60.0, 0);
            var agents = view.Agents;
            var blood0 = g.CrowdBloodFx;
            var members0 = agents.MembersOf(cell);
            g.ZInstaKillUntil = now + 1e6;
            Shoot(c.MassFeet(cell, 0), 1.0f);
            g.ZInstaKillUntil = 0;
            var members1 = agents.MembersOf(cell);
            Line($"mass points: drawn crowd: {members0} agents in the cell, blood bursts +{g.CrowdBloodFx - blood0}, {members1} standing after the kill");
            if (!ReferenceEquals(g.DrawnCrowd, agents) || g.CrowdBloodFx == blood0 || members0 == 0 || members1 >= members0)
                Fail("mass points: a hit in the far crowd should bleed and a kill should sink one of the drawn zombies");
        }
        finally
        {
            CrowdJobs.Serial = false;
        }
        Check();

        // ---- Cost: rays over a packed horde (just above the heads: every zombie on the way tested), no allocation ----
        c.Clear();
        g.CrowdRoundTotal = g.AliveZombies + g.CrowdRoundKills;
        var packed = c.SeedMass(0, 0, 60000, c.Tuning.MassCapacity, 400f);
        g.CrowdRoundTotal += packed;
        var gy = c.GroundAt(0, 0);
        var rays = 0;
        long hits = 0;
        void Rays()
        {
            for (var k = 0; k < 400; k++)
            {
                var yaw = k * 0.0157f;
                var o = new Vector3(0, gy + HordeCrowd.BodyHeight + 0.05f, 0);
                var d = new Vector3(MathF.Cos(yaw), 0, MathF.Sin(yaw));
                var dist = 200f;
                if (c.MassRayHit(o, d, ref dist, -1f, out _, out _, out _))
                    hits++;
                rays++;
            }
        }
        Rays();
        var a0 = GC.GetAllocatedBytesForCurrentThread();
        var ts = Stopwatch.GetTimestamp();
        Rays();
        var perRay = Stopwatch.GetElapsedTime(ts).TotalMilliseconds / 400;
        var alloc = GC.GetAllocatedBytesForCurrentThread() - a0;
        Line($"mass points: {packed} in the mass, 400 rays of 200 m just over their heads: {perRay * 1000:0.0} us a ray, {hits} hits, {alloc} bytes allocated");
        if (alloc > 0)
            Fail($"mass points: the mass ray test allocated {alloc} bytes");
        if (hits != 0 && map.Terrain == null)
            Fail("mass points: a ray over the zombies' heads should hit none");
        if (TickCpu.Available && perRay > 0.25)
            Fail($"mass points: a ray through a packed mass should cost well under a quarter of a millisecond ({perRay:0.000} ms)");
        Check();
        if (!counted)
            Fail("mass points: the counts should add up (crowd + alive + killed = the round's total) and the wounds stay in their cells throughout");
    }
}
