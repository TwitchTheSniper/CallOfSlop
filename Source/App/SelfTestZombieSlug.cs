using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Self-test section "Zombie slugs" (Source/Game/ZombieSlug.cs, docs/zombies/ZOMBIES-MODE.md "Dismemberment"): a crawler
/// that loses both arms becomes a slug; its hit box is the torso and head at ground height (a head shot still kills);
/// it inches along at the rules' slugSpeed, reaches a player near it and bites only at their feet (weaker and shorter
/// than a crawler); outside it lies at its window's foot and never climbs in; the horde walks over it (out of the
/// neighbour grid) and it doesn't hold the spawning back; one stranded unseen is culled; a client sees the slug and draws
/// it flat, wriggling along. Short seeded scenarios on Horde Test.
/// </summary>
public static partial class SelfTest
{
    private static void CheckZombieSlug()
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
        Zombie Z(Vector3 p, byte limbs, int health = 1000)
        {
            var z = ZPlace(g, zm, p, arena, ZSpeed.Walk, t);
            z.Health = z.MaxHealth = health;
            z.Yaw = 0; // facing -Z: +X is its right
            g.SelfTestInitLimbs(z);
            z.Limbs = z.LimbsShown = limbs;
            z.SlugSeenAt = t;
            return z;
        }
        void Clear()
        {
            foreach (var z in g.Horde)
                z.State = ZState.Dead;
            g.SelfTestTickHorde(t += 2); // (the dead are gone after 1.5 s)
        }
        me.Teleport(At(-9, 14));

        // ---- The state and the hit box: a crawler loses both arms; torso and head at ground height ----
        {
            var feet = At(-9, -6);
            var z = Z(feet, 12, health: 100000);
            var fwd = World.FlatForward(z.Yaw);
            var side = World.FlatRight(z.Yaw);
            var slugs0 = g.ZSlugs;
            for (var s = 1; s >= -1; s -= 2)
            {
                var arm = s > 0 ? (int)ZLimb.ArmR : (int)ZLimb.ArmL;
                var at = feet + side * (0.25f * s) + fwd * 0.3f + new Vector3(0, 0.3f, 0);
                g.SelfTestLimbShot(me, z, at, z.LimbHp[arm] + 1, t, from: at + side * (3f * s));
            }
            Line($"a crawler shot in both arms: limbs {z.Limbs:X}, slug {z.Slug}, crawler {z.Crawler}, alive {z.Alive}; slugs made {g.ZSlugs - slugs0}");
            if (!z.Slug || !z.Alive || g.ZSlugs - slugs0 != 1)
                Fail("a zombie that loses all four limbs should become a slug (alive)");
            var box = Game.ZombieBox(z, feet, z.Yaw);
            var chest = g.SelfTestLimbRay(feet + side * 3 + fwd * 0.3f + new Vector3(0, 0.55f, 0), -side); // above it: a crawler's box is hit here
            var body = g.SelfTestLimbRay(feet + side * 3 + fwd * 0.3f + new Vector3(0, 0.2f, 0), -side);
            var behind = g.SelfTestLimbRay(feet + side * 3 - fwd * 0.5f + new Vector3(0, 0.2f, 0), -side); // where a crawler's legs were
            var headAt = feet + fwd * 0.8f + new Vector3(0, 0.3f, 0);
            var flags = g.SelfTestLimbShot(me, z, headAt, 50, t, from: headAt + fwd * 3f);
            Line($"slug hit box {box.Max.Y - box.Min.Y:0.00} m high, {Vector3.Dot(box.Max - box.Min, fwd):0.00} m long; a shot 0.55 m up from the side {LimbName(chest)}, 0.2 m up {LimbName(body)}, behind it {LimbName(behind)}; "
                 + $"a shot at its head from the front: flags {flags} (headshot {(flags & 2) != 0})");
            if (box.Max.Y - box.Min.Y > 0.45f || chest != -2 || body != -1 || behind != -2)
                Fail("a slug's hit box should be the torso and head only, at ground height");
            if ((flags & 2) == 0)
                Fail("a shot at a slug's head should be a headshot");
            z.Health = 10;
            var killFlags = g.SelfTestLimbShot(me, z, headAt, 50, t, from: headAt + fwd * 3f);
            Line($"a head shot on a slug with 10 health: flags {killFlags}, killed by the head {z.KilledByHead}");
            if ((killFlags & 4) == 0 || !z.KilledByHead)
                Fail("a head shot should still kill a slug");
            Clear();
        }

        // ---- Moving: slugSpeed, whatever its class; it doesn't hold the spawning back ----
        {
            var slug = Z(At(-12, -12), 15);
            slug.Speed = ZSpeed.Sprint;
            slug.Yaw = MathF.PI; // (facing the player: turning round takes a slug a while)
            var whole = Z(At(-6, -12), 0);
            me.Teleport(At(-9, 10));
            var from = slug.Position;
            for (var k = 0; k < 180; k++)
                g.SelfTestZombiesTick(t += World.TickDt);
            var moved = Flat2(slug.Position - from) / 3f;
            var speed = g.SelfTestSpeedOf(slug);
            Line($"a sprinter slug for 3 s: {moved:0.00} m/s over the ground (speed {speed:0.00} m/s, slugSpeed {lt.SlugSpeed}); the spawner counts {g.SelfTestAliveForCap} of {g.AliveZombies} alive against the cap");
            if (MathF.Abs(speed - lt.SlugSpeed) > 0.01f || moved < 0.2f || moved > 0.4f)
                Fail($"a slug should inch along at about {lt.SlugSpeed} m/s (0.2 to 0.4)");
            if (g.SelfTestAliveForCap != 1 || g.AliveZombies != 2)
                Fail("a slug shouldn't count towards the alive cap");
            Clear();
        }

        // ---- Biting: it reaches a player near it and bites at foot height, weak and short ----
        {
            me.ImmuneUntil = 0;
            (int Damage, double At) Run(byte limbs, float up, float dist, int ticks)
            {
                me.Health = World.MaxHealth;
                var zp = At(-9, -6);
                var z = Z(zp, limbs);
                var spot = zp + new Vector3(0, up, -dist);
                me.Teleport(spot);
                var t0 = t;
                for (var k = 0; k < ticks; k++)
                {
                    g.SelfTestZombiesTick(t += World.TickDt);
                    me.Teleport(spot);
                    if (me.Health < World.MaxHealth)
                    {
                        var r = (World.MaxHealth - me.Health, t - t0);
                        Clear();
                        return r;
                    }
                }
                Clear();
                return (0, -1);
            }
            var reachSlug = g.ZombieReachFor(Z(At(-20, -20), 15));
            var reachCrawl = g.ZombieReachFor(Z(At(-18, -20), 12));
            Clear();
            var near = Run(15, 0, 2.5f, 60 * 12);
            var crawl = Run(12, 0, 1.0f, 120);
            var slugUp = Run(15, 0.6f, 0.6f, 180);
            var crawlUp = Run(12, 0.6f, 1.0f, 120);
            me.ImmuneUntil = 1e9;
            me.Health = World.MaxHealth;
            Line($"bites: a slug 2.5 m away {near.Damage} at {near.At:0.00} s, a crawler next to you {crawl.Damage} at {crawl.At:0.00} s; target 0.6 m up: slug {slugUp.Damage}, crawler {crawlUp.Damage}; "
                 + $"reach slug {reachSlug:0.00} m, crawler {reachCrawl:0.00} m");
            if (near.Damage <= 0)
                Fail("a slug should crawl to a player near it and bite");
            else if (near.At < 2.5 / 0.45 || near.Damage >= crawl.Damage)
                Fail("a slug's bite should come slowly (it has to inch over first) and hurt less than a crawler's");
            if (slugUp.Damage != 0 || crawlUp.Damage <= 0 || reachSlug >= reachCrawl)
                Fail("a slug should bite only at your feet (lower and shorter than a crawler)");
        }

        // ---- Windows: outside, a slug lies at the window's foot and never climbs in ----
        {
            var sp = zm.Spawners.FindIndex(s => s.Window >= 0);
            var z = g.SelfTestSpawnZombie(sp, t);
            z.Limbs = z.LimbsShown = 15;
            z.SlugSeenAt = t;
            var win = z.Window;
            g.ZBoards[win] = 0;
            me.Teleport(zm.Windows[win].Inside + World.FlatForward(zm.Windows[win].Yaw) * 2.5f);
            var d0 = Flat2(z.Position - zm.Windows[win].Outside);
            var climbed = false;
            var queued = false;
            for (var k = 0; k < 60 * 25; k++)
            {
                g.SelfTestZombiesTick(t += World.TickDt);
                climbed |= z.State is ZState.Climbing or ZState.Tearing || z.Window < 0;
                queued |= z.Hm.QueueRank >= 0 && z.HordePortal >= 0;
            }
            var d1 = Flat2(z.Position - zm.Windows[win].Outside);
            Line($"a slug outside window {win} (no boards): {d0:0.00} m from its foot, after 25 s {d1:0.00} m, state {z.State}, climbed in {climbed}, queued {queued}, alive {z.Alive}");
            if (climbed || queued || !z.Alive || (d1 > 0.6f && d1 > d0 - 3f))
                Fail("a slug outside should inch to its window's foot and lie there (never tearing, queueing or climbing in)");
            Clear();
        }

        // ---- The horde walks over it ----
        {
            // One walking forward (-Z) with one lying just ahead of it and to its side: a crawler pushes it aside, a slug doesn't.
            (float Turn, int InGrid) Beside(byte lying)
            {
                var b = Z(At(-9, -10.5f), lying);
                b.StunnedUntil = 1e9; // lies still
                var w = Z(At(-8.6f, -10f), 0);
                g.SelfTestTickHorde(t += World.TickDt); // (the neighbour grid is built at the start of the horde's tick)
                var turn = MathF.Abs(World.WrapAngle(g.HordeAI.Separate(w, 0) - 0));
                var n = g.HordeAI.Grid.Count;
                Clear();
                return (turn, n);
            }
            var sp = Beside(15);
            var cp = Beside(12);
            Line($"a walker with one lying just ahead of it: a slug turns it {sp.Turn:0.00} rad ({sp.InGrid} of 2 in the neighbour grid), a crawler {cp.Turn:0.00} rad ({cp.InGrid} of 2 in the grid)");
            if (sp.InGrid != 1 || cp.InGrid != 2 || sp.Turn > 0.01f || cp.Turn < 0.05f)
                Fail("the horde should walk over a slug (out of the neighbour grid) and keep going");
        }

        // ---- Stranded: one unseen with nobody near is culled; one seen stays ----
        {
            var cull = lt.SlugCullSeconds;
            lt.SlugCullSeconds = 5;
            var seen = Z(At(-9, -6), 15);
            me.Teleport(At(-9, -3));
            for (var k = 0; k < 60 * 7; k++)
                g.SelfTestTickHorde(t += World.TickDt);
            var seenAlive = seen.Alive;
            me.Alive = false; // nobody standing: nobody sees it
            var culled0 = g.ZSlugsCulled;
            var killsBefore = g.ZP(me).Kills;
            for (var k = 0; k < 60 * 7; k++)
                g.SelfTestTickHorde(t += World.TickDt);
            me.Alive = true;
            lt.SlugCullSeconds = cull;
            Line($"stranded: a slug seen for 7 s alive {seenAlive}; unseen for 7 s (cull after 5 s) alive {seen.Alive}, culled {g.ZSlugsCulled - culled0}, kills credited {g.ZP(me).Kills - killsBefore}");
            if (!seenAlive || seen.Alive || g.ZSlugsCulled - culled0 != 1 || g.ZP(me).Kills != killsBefore)
                Fail("a slug stranded unseen should be culled (no kill credit), one in view kept");
            Clear();
        }

        // ---- The network and the pose: a client sees the slug and draws it flat, wriggling ----
        {
            var zs = Z(At(-9, -6), 15);
            var zc = Z(At(-6, -6), 12);
            g.ServerTick++;
            var w = new PacketWriter();
            g.WriteZEnts(w);
            using var c = ZGame(0, ref now, tweak: r => r.FirstRoundDelay = 9999, onMap: map);
            World.SetMap(map);
            c.SelfTestReadEnts(w.Span, now);
            var cs = c.Horde.FirstOrDefault(x => x.Id == zs.Id);
            var sameBox = cs != null && Game.ZombieBox(cs, zs.Position, zs.Yaw).Center == Game.ZombieBox(zs, zs.Position, zs.Yaw).Center;
            ZombieAnimInput Pose(byte limbs, float gait)
            {
                var a = default(ZombieAnimInput);
                a.Id = 41;
                a.Mode = ZAnimMode.Locomotion;
                a.Move = 1;
                a.Gait = gait;
                a.Limbs = limbs;
                a.Attack = -1;
                a.Windup = 0.45f;
                a.Stagger = -1;
                return a;
            }
            float chestSum = 0, headHigh = -9, hipMin = 9, hipMax = -9;
            var finite = true;
            for (var k = 0; k < 16; k++)
            {
                ZombieAnim.Evaluate(Pose(15, k * 0.4f), out var p);
                chestSum += p.Upper.M42 / 16;
                headHigh = MathF.Max(headHigh, Vector3.Transform(ZombieAnim.HeadCenter, p.Head).Y);
                hipMin = MathF.Min(hipMin, p.Upper.M43);
                hipMax = MathF.Max(hipMax, p.Upper.M43);
                finite &= float.IsFinite(p.Upper.M41 + p.Upper.M42 + p.Head.M42 + p.Root.M43);
            }
            ZombieAnim.Evaluate(Pose(12, 0), out var crawl);
            var client = cs != null && cs.Slug;
            Line($"network: the client sees a slug {client} (limbs {cs?.Limbs ?? 255:X}), same hit box {sameBox}, protocol {ServerInfo.ProtocolVersion}; "
                 + $"the slug pose: chest {chestSum:0.00} m up on average (a crawler's {crawl.Upper.M42:0.00} m), head at most {headHigh:0.00} m, the body moving {hipMax - hipMin:0.00} m back and forth over a wave");
            if (!client || !sameBox)
                Fail("a client should see the slug (from its limb bits) with the same hit box");
            if (!finite || chestSum >= crawl.Upper.M42 || headHigh > 0.6f || hipMax - hipMin < 0.08f)
                Fail("a slug should be drawn flat on its belly (lower than a crawler), inching along with a body wave");
            Clear();
        }
    }
}
