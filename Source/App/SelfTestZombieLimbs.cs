using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Self-test section "Zombie dismemberment" (Source/Game/ZombieLimbs.cs, docs/zombies/ZOMBIES-MODE.md "Dismemberment"):
/// shots route to the limb they hit (and the head stays the kill shot), a limb out of health comes off while the zombie
/// lives, a shotgun's pellets and a blast take several at once; each lost-limb state moves and attacks as it should (one
/// arm weaker and slower, no arms a bite, one leg a limp, no legs a crawler with a low hit box and a low reach, slow at
/// windows); a horde with crawlers in it keeps going; the gore pool's limb cap and life; a client sees the limbs and
/// throws each once (one byte more a dismembered zombie); the poses; no allocations. Short seeded scenarios on Horde Test.
/// </summary>
public static partial class SelfTest
{
    private static void CheckZombieLimbs()
    {
        if (Maps.Find("Horde Test") is not { Zombies: { } zm } map)
        {
            Fail("no Horde Test map");
            return;
        }
        var now = 0.0;
        using var g = ZGame(0, ref now, tweak: r => { r.FirstRoundDelay = 9999; r.PowerFirstAt = 0; r.PowerDropChance = 0f; }, onMap: map);
        var me = g.Local!;
        me.ImmuneUntil = 1e9;
        var arena = zm.AreaIndex("Arena");
        var lt = g.ZRules.Limbs;
        var t = now;
        Vector3 At(float x, float z) => new(x, map.GroundHeight(x, z), z);
        Zombie Z(Vector3 p, int health = 1000, ZSpeed speed = ZSpeed.Walk)
        {
            var z = ZPlace(g, zm, p, arena, speed, t);
            z.Health = z.MaxHealth = health;
            z.Yaw = 0; // facing -Z: +X is its right
            g.SelfTestInitLimbs(z);
            return z;
        }
        void Clear()
        {
            foreach (var z in g.Horde)
                z.State = ZState.Dead;
            g.SelfTestTickHorde(t += 2); // (the dead are gone after 1.5 s)
        }
        me.Teleport(At(-9, 14));

        // ---- Routing: the limb a shot lands on (from in front: its right is +X) ----
        {
            var feet = At(-9, -6);
            var z = Z(feet);
            var front = new Vector3(0, 0, -3f);
            int Ray(float x, float y) => g.SelfTestLimbRay(feet + front + new Vector3(x, y, 0), new Vector3(0, 0, 1));
            var got = new[] { Ray(0.3f, 1.1f), Ray(-0.3f, 1.1f), Ray(0.2f, 0.5f), Ray(-0.2f, 0.5f), Ray(0, 1.1f), Ray(0, 1.6f), Ray(0.6f, 1.1f) };
            var want = new[] { (int)ZLimb.ArmR, (int)ZLimb.ArmL, (int)ZLimb.LegR, (int)ZLimb.LegL, -1, -1, -2 };
            var hp0 = z.LimbHp[(int)ZLimb.LegL];
            g.SelfTestLimbShot(me, z, feet + new Vector3(-0.2f, 0.5f, -0.4f), 30, t);
            var legHit = hp0 - z.LimbHp[(int)ZLimb.LegL];
            var others = z.LimbHp[0] == hp0 && z.LimbHp[1] == hp0 && z.LimbHp[2] == hp0;
            var health1 = z.Health;
            g.SelfTestLimbShot(me, z, feet + new Vector3(0, 1.6f, -0.4f), 30, t); // the head: no limb
            var headNoLimb = z.LimbHp[0] == hp0 && z.LimbHp[1] == hp0 && z.LimbHp[2] == hp0 && z.LimbHp[3] == hp0 - legHit;
            Line($"limb routing: rays hit {string.Join(", ", got.Select(LimbName))} (want {string.Join(", ", want.Select(LimbName))}); limb health {hp0:0} ({lt.HealthFraction} of 1000), "
                 + $"a 30 shot on the left leg took {legHit:0} off it and {1000 - health1} off the body, the head shot none off a limb {headNoLimb}");
            if (!got.SequenceEqual(want))
                Fail("shots should land on the limb they hit (arms out at the shoulders, legs below the hips; the head and middle are the body)");
            if (MathF.Abs(legHit - 30) > 0.01f || !others || health1 != 970 || !headNoLimb || hp0 != MathF.Max(lt.HealthMin, 1000 * lt.HealthFraction))
                Fail("a limb hit should damage that limb and the zombie's health; a head hit no limb");

            // A limb out of health comes off; the zombie lives.
            g.SelfTestLimbShot(me, z, feet + new Vector3(-0.2f, 0.5f, -0.4f), z.LimbHp[(int)ZLimb.LegL] + 1, t);
            var off = z.Limbs == 1 << (int)ZLimb.LegL && z.Alive;
            var again = g.SelfTestLimbRay(feet + front + new Vector3(-0.2f, 0.5f, 0), new Vector3(0, 0, 1)); // that leg isn't there: the body
            Line($"severed: the left leg came off ({z.Limbs:X}, alive {z.Alive}, health {z.Health}); a ray where it was hits {LimbName(again)}; severed {g.ZLimbsSevered}, shown {g.ZLimbsShown}");
            if (!off || again != -1 || g.ZLimbsSevered != 1 || g.ZLimbsShown != 1)
                Fail("a limb at 0 health should come off while the zombie lives (once)");

            // A shotgun's pellets: both arms in one trigger pull.
            var hpArm = z.LimbHp[0];
            z.Health = 100000; // (the pellets add up to more than it has left)
            ReadOnlySpan<Vector3> pellets = [feet + new Vector3(0.3f, 1.1f, -0.4f), feet + new Vector3(0.32f, 1.0f, -0.4f), feet + new Vector3(-0.3f, 1.1f, -0.4f), feet + new Vector3(-0.32f, 1.2f, -0.4f)];
            g.SelfTestLimbPellets(me, z, pellets, hpArm * 0.55f, t);
            Line($"shotgun: 4 pellets of {hpArm * 0.55f:0} took both arms {(z.Limbs & Game.ArmsMask) == Game.ArmsMask} ({z.Limbs:X}), alive {z.Alive}");
            if ((z.Limbs & Game.ArmsMask) != Game.ArmsMask || !z.Alive)
                Fail("a shotgun's pellets should take several limbs in one pull");
            Clear();

            // A blast at the feet takes both legs (and leaves a crawler); one at the shoulder an arm.
            var zb = Z(At(-9, -6), health: 400);
            g.SelfTestBlast(me, zb.Position + new Vector3(0, 0.05f, 0), 4f, 300, t);
            Line($"blast at the feet (300 in 4 m): limbs lost {zb.Limbs:X}, crawler {zb.Crawler}, alive {zb.Alive} ({zb.Health}/400)");
            if (!zb.Crawler || !zb.Alive)
                Fail("a blast at a zombie's feet should take both legs (it lives on as a crawler)");
            Clear();

            // A class that ignores dismemberment never loses one.
            lt.Severs[(int)ZSpeed.Sprint] = false;
            var zi = Z(At(-9, -6), speed: ZSpeed.Sprint);
            g.SelfTestLimbShot(me, zi, zi.Position + new Vector3(0.2f, 0.5f, -0.4f), 500, t);
            lt.Severs[(int)ZSpeed.Sprint] = true;
            Line($"a class with severs false: a 500 leg shot leaves its limbs {zi.Limbs:X} (health {zi.Health})");
            if (zi.Limbs != 0)
                Fail("a zombie type that ignores dismemberment should keep its limbs");
            Clear();
        }

        // ---- Moving: one leg limps, none crawls; the rest of the horde isn't held up ----
        {
            var masks = new byte[] { 0, 4, 12 };
            var zs = new Zombie[3];
            for (var i = 0; i < 3; i++)
            {
                zs[i] = Z(At(-12 + i * 3, -12));
                zs[i].Limbs = zs[i].LimbsShown = masks[i];
            }
            var from = zs.Select(z => z.Position).ToArray();
            for (var k = 0; k < 90; k++)
                g.SelfTestZombiesTick(t += World.TickDt);
            var moved = zs.Select((z, i) => Flat2(z.Position - from[i])).ToArray();
            var crawlBox = Game.ZombieBox(zs[2], zs[2].Position, zs[2].Yaw);
            Line($"moving 1.5 s: whole {moved[0]:0.00} m, one leg {moved[1]:0.00} m ({moved[1] / MathF.Max(0.01f, moved[0]):0.00}x), crawler {moved[2]:0.00} m ({moved[2] / MathF.Max(0.01f, moved[0]):0.00}x); "
                 + $"speeds {g.SelfTestSpeedOf(zs[0]):0.00}/{g.SelfTestSpeedOf(zs[1]):0.00}/{g.SelfTestSpeedOf(zs[2]):0.00} m/s; the crawler's box {crawlBox.Max.Y - crawlBox.Min.Y:0.00} m high");
            if (moved[0] < 1f || moved[1] > moved[0] * 0.75f || moved[1] < moved[0] * 0.3f || moved[2] > moved[0] * 0.5f || moved[2] < moved[0] * 0.12f)
                Fail("a one-legged zombie should limp (slower) and a crawler crawl (slower still), both still coming");
            // The crawler's hit box: low (a chest-high shot misses it), its arms at the front.
            var cf = zs[2].Position;
            var fwd = World.FlatForward(zs[2].Yaw);
            var side = World.FlatRight(zs[2].Yaw);
            var chest = g.SelfTestLimbRay(cf + side * 3 + fwd * 0.3f + new Vector3(0, 1.2f, 0), -side);
            var low = g.SelfTestLimbRay(cf + side * 3 + fwd * 0.3f + new Vector3(0, 0.3f, 0), -side);
            var back = g.SelfTestLimbRay(cf + side * 3 - fwd * 0.4f + new Vector3(0, 0.3f, 0), -side);
            Line($"crawler hit box: a chest-high shot from the side {LimbName(chest)}, a low one at its front {LimbName(low)}, at its back {LimbName(back)}; climbs in {g.ClimbSecondsFor(zs[2]):0.00} s (others {g.ClimbSecondsFor(zs[0]):0.00} s)");
            if (chest != -2 || low != (int)ZLimb.ArmR || back != -1 || crawlBox.Max.Y - crawlBox.Min.Y > 0.7f)
                Fail("a crawler's hit box should be crawler-sized (low and long, its arms at the front)");
            if (g.ClimbSecondsFor(zs[2]) < g.ClimbSecondsFor(zs[0]) * 1.5f)
                Fail("a crawler should be slow at windows");
            Clear();

            // A horde of 30 with a third crawling and a third limping: everyone keeps coming.
            HordePlace(g, map, zm, 30, t, 4242u, _ => arena);
            var live = g.Horde.Where(z => z.Alive).ToList();
            for (var i = 0; i < live.Count; i++)
                live[i].Limbs = live[i].LimbsShown = i % 3 == 0 ? (byte)12 : i % 3 == 1 ? (byte)8 : (byte)0;
            var last = live.Select(z => z.Position).ToArray();
            var path = new double[live.Count];
            for (var k = 0; k < 150; k++)
            {
                me.Teleport(HordeLoop(map, k * World.TickDt, 0));
                g.SelfTestZombiesTick(t += World.TickDt);
                for (var i = 0; i < live.Count; i++)
                {
                    var step = Flat2(live[i].Position - last[i]);
                    if (step < 1f)
                        path[i] += step;
                    last[i] = live[i].Position;
                }
            }
            double Ratio(int cls)
            {
                double sum = 0;
                var n = 0;
                for (var i = cls; i < live.Count; i += 3)
                {
                    sum += path[i] / (g.SelfTestSpeedOf(live[i]) * 2.5);
                    n++;
                }
                return sum / Math.Max(1, n);
            }
            var (rc, rl, rw) = (Ratio(0), Ratio(1), Ratio(2));
            Line($"a mixed horde of 30 (10 crawling, 10 one-legged) for 2.5 s: path walked over speed x time crawlers {rc:0.00}, limpers {rl:0.00}, whole {rw:0.00}");
            if (rc < 0.35 || rl < 0.35 || rw < 0.35)
                Fail("a horde with crawlers and limpers in it should keep moving (none held up)");
            Clear();
        }

        // ---- Attacking: one arm weaker and slower, no arms a bite, a crawler low ----
        {
            me.ImmuneUntil = 0;
            var cases = new (string Name, byte Limbs, float Up)[] { ("whole", 0, 0), ("one arm", 1, 0), ("no arms (bite)", 3, 0), ("crawler", 12, 0), ("crawler, target 1 m up", 12, 1f) };
            var results = new (int Damage, double At)[cases.Length];
            var zp = At(-9, -6);
            for (var c = 0; c < cases.Length; c++)
            {
                me.Health = World.MaxHealth;
                me.Teleport(zp + new Vector3(0, cases[c].Up, -1.0f));
                var z = Z(zp);
                z.Limbs = z.LimbsShown = cases[c].Limbs;
                var t0 = t;
                results[c] = (0, -1);
                for (var k = 0; k < 120 && results[c].Damage == 0; k++)
                {
                    g.SelfTestZombiesTick(t += World.TickDt);
                    me.Teleport(zp + new Vector3(0, cases[c].Up, -1.0f));
                    if (me.Health < World.MaxHealth)
                        results[c] = (World.MaxHealth - me.Health, t - t0);
                }
                Clear();
            }
            me.ImmuneUntil = 1e9;
            me.Health = World.MaxHealth;
            Line("attacks: " + string.Join(", ", cases.Select((c, i) => $"{c.Name} {results[i].Damage} at {results[i].At:0.00} s")));
            var whole = results[0];
            if (whole.Damage <= 0)
                Fail("a whole zombie should swipe a player next to it");
            else
            {
                if (results[1].Damage <= 0 || results[1].Damage >= whole.Damage || results[1].At <= whole.At)
                    Fail("a one-armed zombie should still swipe, weaker and slower");
                if (results[2].Damage <= 0 || results[2].Damage >= results[1].Damage)
                    Fail("a zombie with no arms should bite (weaker again)");
                if (results[3].Damage <= 0 || results[3].Damage >= whole.Damage)
                    Fail("a crawler should still attack (a little weaker)");
                if (results[4].Damage != 0)
                    Fail("a crawler shouldn't reach a player standing a metre above it");
            }
        }

        // ---- The gore pool: limbs have their own cap and a short life ----
        {
            Gibs.Clear();
            var thrown0 = Gibs.LimbsThrown;
            var most = 0;
            for (var i = 0; i < 60; i++)
            {
                Gibs.SpawnLimb(At(-9, -6), 0.3f * i, i % 4, i % 7 == 0, new Vector3(0.4f, 0.5f, 0.3f), new Vector3(0.2f), (uint)i * 77u, lt.GoreSeconds, lt.GoreCap);
                most = Math.Max(most, Gibs.LimbPieces);
            }
            var seconds = 0f;
            while (Gibs.LimbPieces > 0 && seconds < 30)
            {
                Gibs.Update(0.05f, t + seconds);
                seconds += 0.05f;
            }
            var mode = Gibs.Mode;
            Gibs.Mode = GoreMode.Off;
            var off = Gibs.SpawnLimb(At(-9, -6), 0, 0, false, Vector3.One, Vector3.One, 1, lt.GoreSeconds, lt.GoreCap);
            Gibs.Mode = mode;
            Line($"gore: 60 limbs thrown ({Gibs.LimbsThrown - thrown0}), at most {most} limb pieces at once (cap {lt.GoreCap}), all gone after {seconds:0.0} s (life {lt.GoreSeconds} s); gore off throws {off}");
            if (most > lt.GoreCap || most < lt.GoreCap / 2 || seconds > lt.GoreSeconds + 1.5f || off != 0)
                Fail("severed limbs should be pooled: capped, gone after their time, none with gore off");
            Gibs.Clear();
        }

        // ---- The network: a client sees the limbs and throws each once; one byte more a dismembered zombie ----
        {
            var zw = Z(At(-12, -6));
            var zd = Z(At(-6, -6));
            g.ServerTick++;
            var w = new PacketWriter();
            g.WriteZEnts(w);
            var whole = w.Length;
            var hostNow = t;
            using var c = ZGame(0, ref now, tweak: r => r.FirstRoundDelay = 9999, onMap: map);
            World.SetMap(map);
            c.SelfTestReadEnts(w.Span, now);
            g.SelfTestLimbShot(me, zd, zd.Position + new Vector3(0.2f, 0.5f, -0.4f), zd.LimbHp[(int)ZLimb.LegR] + 1, hostNow);
            g.SelfTestLimbShot(me, zd, zd.Position + new Vector3(-0.2f, 0.5f, -0.4f), zd.LimbHp[(int)ZLimb.LegL] + 1, hostNow);
            g.ServerTick += 2;
            g.WriteZEnts(w);
            var withLimbs = w.Length;
            var shown0 = c.ZLimbsShown;
            c.SelfTestReadEnts(w.Span, now += 0.03);
            var shown1 = c.ZLimbsShown;
            g.ServerTick += 2;
            g.WriteZEnts(w);
            c.SelfTestReadEnts(w.Span, now += 0.03);
            var shown2 = c.ZLimbsShown;
            var cz = c.Horde.FirstOrDefault(x => x.Id == zd.Id);
            var cw = c.Horde.FirstOrDefault(x => x.Id == zw.Id);
            // A client joining late: the zombie is already a crawler (no gore for what happened before).
            using var late = ZGame(0, ref now, tweak: r => r.FirstRoundDelay = 9999, onMap: map);
            World.SetMap(map);
            late.SelfTestReadEnts(w.Span, now);
            var lz = late.Horde.FirstOrDefault(x => x.Id == zd.Id);
            var sameBox = cz != null && Game.ZombieBox(cz, zd.Position, zd.Yaw).Center == Game.ZombieBox(zd, zd.Position, zd.Yaw).Center;
            Line($"network: {whole} then {withLimbs} bytes for 2 zombies (+{withLimbs - whole} for the crawler), protocol {ServerInfo.ProtocolVersion}; the client sees limbs {cz?.Limbs ?? 255:X} (host {zd.Limbs:X}), "
                 + $"the whole one {cw?.Limbs ?? 255:X}, crawler box the same {sameBox}; gore thrown {shown1 - shown0} then {shown2 - shown1} more; a late joiner sees {lz?.Limbs ?? 255:X} and throws {late.ZLimbsShown}");
            if (cz == null || cz.Limbs != zd.Limbs || !cz.Crawler || cw == null || cw.Limbs != 0 || !sameBox)
                Fail("a client should see each zombie's lost limbs");
            if (shown1 - shown0 != 2 || shown2 != shown1 || lz?.Limbs != zd.Limbs || late.ZLimbsShown != 0)
                Fail("a client should throw each severed limb once (and a late joiner none)");
            if (withLimbs - whole != Game.ZEntLimbBytes || ServerInfo.ProtocolVersion < 65)
                Fail($"a dismembered zombie should cost {Game.ZEntLimbBytes} byte more in ZEnts (got {withLimbs - whole}) with protocol 65 or later");
            Clear();
        }

        // ---- The poses: a crawl, a hop, the other arm swiping, a bite ----
        {
            ZombieAnimInput Pose(byte limbs, float gait, float attack = -1)
            {
                var a = default(ZombieAnimInput);
                a.Pos = Vector3.Zero;
                a.Id = 41; // (its swipe side is fixed by id)
                a.Mode = ZAnimMode.Locomotion;
                a.Move = 1;
                a.Gait = gait;
                a.Limbs = limbs;
                a.Attack = attack;
                a.Windup = 0.45f;
                a.Stagger = -1; // (not just landed from a window)
                return a;
            }
            var look = new ZombieLook(41);
            float handLow = 9, headHigh = -9;
            var finite = true;
            for (var k = 0; k < 16; k++)
            {
                ZombieAnim.Evaluate(Pose(12, k * 0.4f), out var p);
                handLow = MathF.Min(handLow, MathF.Min(p.HandR.Y, p.HandL.Y));
                headHigh = MathF.Max(headHigh, Vector3.Transform(ZombieAnim.HeadCenter, p.Head).Y);
                finite &= float.IsFinite(p.HandR.X + p.HandL.Y + p.Upper.M42 + p.FootR.Z);
            }
            ZombieAnim.Evaluate(Pose(12, 0), out var crawl);
            ZombieAnim.Evaluate(Pose(4, 0.7f), out var hop); // the right leg gone: hops on the left
            var hopFoot = MathF.Abs(hop.FootL.X);
            var swipeArm = look.SwipeSide > 0 ? 1 : 2; // take the swiping arm off: the other swipes
            ZombieAnim.Evaluate(Pose((byte)swipeArm, 0, -1), out var idle1);
            ZombieAnim.Evaluate(Pose((byte)swipeArm, 0, 0.45f + 0.14f), out var struck1);
            var other = look.SwipeSide > 0 ? Vector3.Distance(idle1.HandL, struck1.HandL) : Vector3.Distance(idle1.HandR, struck1.HandR);
            ZombieAnim.Evaluate(Pose(3, 0, -1), out var noBite);
            ZombieAnim.Evaluate(Pose(3, 0, 0.45f + 0.14f), out var bite);
            var lunge = Vector3.Distance(Vector3.Transform(ZombieAnim.HeadCenter, noBite.Head), Vector3.Transform(ZombieAnim.HeadCenter, bite.Head));
            var rec = new DrawRecorder { Keep = false };
            var gfx = new Renderer { Recorder = rec };
            rec.Reset();
            ZombieAnim.Draw(gfx, crawl, new Vector3(0.3f), new Vector3(0.4f, 0.5f, 0.3f), new Vector3(0.2f), true, false, true, 12);
            var crawlBoxes = rec.Count;
            rec.Reset();
            ZombieAnim.Draw(gfx, crawl, new Vector3(0.3f), new Vector3(0.4f, 0.5f, 0.3f), new Vector3(0.2f), true, false, true, 0);
            var wholeBoxes = rec.Count;
            Line($"poses: crawling, the chest {crawl.Upper.M42:0.00} m up, the head at most {headHigh:0.00} m, the hands down to {handLow:0.00} m; hopping on one foot {hopFoot:0.00} m off the middle; "
                 + $"the other arm swipes ({other:0.00} m), no arms lunges ({lunge:0.00} m); {crawlBoxes} boxes with no legs vs {wholeBoxes} whole");
            if (!finite || crawl.Upper.M42 > 0.45f || headHigh > 0.8f || handLow > 0.12f)
                Fail("a crawler should be drawn belly down, pulling itself along with its hands");
            if (hopFoot > 0.12f || other < 0.3f || lunge < 0.08f || crawlBoxes >= wholeBoxes)
                Fail("a one-legged zombie should hop on its foot, a one-armed one swipe with the other, an armless one bite; lost limbs not drawn");
        }

        // ---- No allocations: a dismembered horde thinking, shot (severing, gore), sent, read and posed ----
        {
            var zs = new List<Zombie>();
            for (var i = 0; i < 12; i++)
            {
                var z = Z(At(-14 + (i % 6) * 2.5f, -10 - (i / 6) * 3));
                z.Limbs = z.LimbsShown = (byte)(i % 4 == 0 ? 12 : i % 4 == 1 ? 1 : i % 4 == 2 ? 3 : 4);
                zs.Add(z);
            }
            var target = Z(At(-9, -2), health: 1_000_000);
            using var c = ZGame(0, ref now, tweak: r => r.FirstRoundDelay = 9999, onMap: map);
            World.SetMap(map);
            var w = new PacketWriter();
            var rec = new DrawRecorder { Keep = false };
            var gfx = new Renderer { Recorder = rec };
            var cnow = now;
            void Round()
            {
                for (var k = 0; k < 30; k++)
                {
                    me.Teleport(HordeLoop(map, t, 0));
                    g.SelfTestZombiesTick(t += World.TickDt);
                    if (k % 10 == 0)
                    {
                        target.Limbs = target.LimbsShown = 0;
                        for (var l = 0; l < 4; l++)
                            target.LimbHp[l] = 50;
                        g.SelfTestLimbShot(me, target, target.Position + new Vector3(0.2f, 0.5f, -0.4f), 60, t); // a leg comes off (gore)
                    }
                    if (k % 2 == 0)
                    {
                        g.WriteZEnts(w);
                        c.SelfTestReadEnts(w.Span, cnow += World.TickDt * 2);
                    }
                    Gibs.Update(World.TickDt, t);
                    foreach (var z in zs)
                    {
                        g.ZombieAnimInputFor(z, t, out var a);
                        ZombieAnim.Evaluate(a, out var pose);
                        ZombieAnim.Draw(gfx, pose, new Vector3(0.3f), new Vector3(0.4f, 0.5f, 0.3f), new Vector3(0.2f), true, false, true, z.Limbs);
                    }
                }
            }
            for (var i = 0; i < 3; i++)
                Round(); // warm up (JIT, the client's horde)
            var before = GC.GetAllocatedBytesForCurrentThread();
            Round();
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            allocated = RecheckAllocBytes(allocated, Round);
            Line($"allocations: {allocated} bytes over 30 ticks of 13 dismembered zombies thinking, a leg shot off every 10 ticks (gore), ZEnts written and read, posed and drawn");
            if (allocated > 0)
                Fail($"dismemberment allocates ({allocated} bytes over 30 ticks)");
            Gibs.Clear();
        }
    }

    private static string LimbName(int l) => l switch { -2 => "miss", -1 => "body", _ => ((ZLimb)l).ToString() };
}
