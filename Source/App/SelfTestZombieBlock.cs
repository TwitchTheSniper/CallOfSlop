using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Self-test section "Zombie bodies" (Source/Game/ZombieBlock.cs; docs/zombies/ZOMBIES-MODE.md, "Zombie bodies"):
/// on an open patch of floor, a player sprinting at a wall of zombies is stopped; walking at a single zombie slides
/// round it; a gap in a wall lets them through; a zombie spawning right on them or climbing onto them pushes them out and
/// they walk away; landing on a head slides off; a crawler blocks low and a jump over it is clear; a walking zombie is
/// held off a standing player; the client's copy (the zombies as last heard, quantised) agrees with the server's; no
/// allocation; the cost a tick with 600 zombies.
/// </summary>
public static partial class SelfTest
{
    private static void CheckZombieBlock()
    {
        var now = 0.0;
        if (ZTestMap is not { Zombies: { } zm } map)
        {
            Fail("no Zombies Test map");
            return;
        }
        using var g = ZGame(0, ref now, tweak: r => r.FirstRoundDelay = 9999, onMap: map);
        for (var d = 0; d < zm.Doors.Count; d++)
            g.OpenDoor(d, null);
        var me = g.Local!;
        me.ImmuneUntil = 1e9;
        if (ZBlockOpenPatch(map, zm, 5.5f) is not { } o)
        {
            Fail("zombie bodies: no open 11 m square of flat floor on the Zombies Test map");
            return;
        }
        var t = now;
        var lines = new List<string>();
        var ok = true;
        void Check(string what, bool pass, string detail)
        {
            lines.Add($"{what} {(pass ? "ok" : "FAILED")} ({detail})");
            ok &= pass;
        }
        Zombie Z(float x, float z, float y = 0)
        {
            var p = o + new Vector3(x, y, z);
            var zz = ZPlace(g, zm, p, zm.AreaAt(p), ZSpeed.Walk, t);
            zz.Health = zz.MaxHealth = 5000;
            return zz;
        }
        void Clear()
        {
            foreach (var zz in g.Horde)
            {
                zz.State = ZState.Dead;
                zz.Snaps.Clear();
            }
            g.SelfTestTickHorde(t += 2); // (the dead are gone after 1.5 s)
            g.SelfTestZBlockBuild(false);
        }
        void Put(float x, float z, float y = 0)
        {
            me.Teleport(o + new Vector3(x, y, z));
            me.Sim.Velocity = Vector3.Zero;
            me.Sim.Grounded = y == 0;
        }
        float Nearest()
        {
            var best = float.MaxValue;
            foreach (var zz in g.Horde)
                if (zz.Alive)
                    best = MathF.Min(best, ZFlat(zz.Position - me.Position));
            return best;
        }
        // Runs ticks of input; returns the closest the player came to any zombie (flat) while their bodies overlapped in height.
        float Go(Buttons b, float yaw, int ticks)
        {
            var closest = float.MaxValue;
            for (var i = 0; i < ticks; i++)
            {
                g.SelfTestZBlockMove(me, ref me.Sim, new InputCmd { Buttons = b, Yaw = yaw, SpeedPct = 100 });
                closest = MathF.Min(closest, Nearest());
            }
            return closest;
        }
        const float contact = World.PlayerRadius + Game.ZBlockRadius;
        const float slack = 0.02f;
        const float north = 0f; // yaw 0 walks towards -Z

        // A wall of zombies 0.9 m apart (the horde's separation): sprinting at it stops at their bodies.
        Clear();
        for (var x = -4.5f; x <= 4.51f; x += 0.9f)
            Z(x, -1f);
        g.SelfTestZBlockBuild(false);
        Put(0, 4);
        var cl = Go(Buttons.Forward | Buttons.Sprint, north, 120);
        var stoppedAt = me.Position.Z - o.Z;
        Check("wall", stoppedAt > -1f + contact - 0.05f && cl > contact - 0.05f && g.ZBlockBodies == 11,
            $"sprinting 2 s at 11 zombies at z -1: stopped at z {stoppedAt:0.00}, closest {cl:0.00} m (contact {contact:0.00})");

        // One zombie a little off the line: walked past, slid round (never through it).
        Clear();
        Z(0.15f, 0);
        g.SelfTestZBlockBuild(false);
        Put(0, 4);
        cl = Go(Buttons.Forward, north, 80);
        Check("single", me.Position.Z - o.Z < -1.5f && cl > contact - 0.05f,
            $"walked from z 4 past a zombie at 0: now z {me.Position.Z - o.Z:0.00}, x {me.Position.X - o.X:0.00}, closest {cl:0.00} m");

        // A wall with a 2 m gap (centres): a player walks through it.
        Clear();
        foreach (var x in new[] { 1f, 1.9f, 2.8f, 3.7f, 4.6f })
        {
            Z(x, 0);
            Z(-x, 0);
        }
        g.SelfTestZBlockBuild(false);
        Put(0, 3);
        cl = Go(Buttons.Forward, north, 70);
        Check("gap", me.Position.Z - o.Z < -2.5f && cl > contact - 0.05f, $"through a 2 m gap: now z {me.Position.Z - o.Z:0.00}, closest {cl:0.00} m");

        // A zombie appearing right on a standing player (and one just off centre): pushed out, then they walk away.
        Clear();
        Put(0, 0);
        var on = Z(0, 0);
        g.SelfTestZBlockBuild(false);
        Go(Buttons.None, north, 12);
        var outD = ZFlat(me.Position - on.Position);
        var from = me.Position;
        Go(Buttons.Forward, MathF.PI, 30); // (away: back towards +Z, whichever side it went)
        var walked = ZFlat(me.Position - from);
        Clear();
        Put(0, 0);
        var off = Z(0.1f, 0.05f);
        g.SelfTestZBlockBuild(false);
        Go(Buttons.Forward, MathF.PI / 2, 20); // walking into it (towards +X) from inside
        var outD2 = ZFlat(me.Position - off.Position);
        Check("spawned on", outD >= contact - slack && walked > 2.5f && outD2 >= contact - slack,
            $"one on the feet: out to {outD:0.00} m in 12 ticks, then walked {walked:0.00} m; one 0.1 m off, walking into it: {outD2:0.00} m");

        // A zombie climbing in through a window onto the player (moved by the server, not the physics).
        Clear();
        Put(0, 0);
        var cz = Z(0, -2.5f);
        cz.State = ZState.Climbing;
        var climbClosest = float.MaxValue;
        for (var i = 0; i <= 40; i++)
        {
            cz.Sim.Position = o + new Vector3(0, 0, -2.5f + 2.5f * Math.Min(1f, i / 30f));
            g.SelfTestZBlockBuild(false);
            g.SelfTestZBlockMove(me, ref me.Sim, new InputCmd { Yaw = north, SpeedPct = 100 });
            if (i > 32)
                climbClosest = MathF.Min(climbClosest, ZFlat(me.Position - cz.Position));
        }
        from = me.Position;
        Go(Buttons.Forward, MathF.PI, 30);
        Check("climbed on", climbClosest >= contact - slack && ZFlat(me.Position - from) > 2.5f,
            $"a climber ending on the player's spot: {climbClosest:0.00} m from it after, then walked {ZFlat(me.Position - from):0.00} m");

        // Landing on a head: slid off to the floor beside it.
        Clear();
        Z(0, 0);
        g.SelfTestZBlockBuild(false);
        Put(0.1f, 0, 1.9f);
        Go(Buttons.None, north, 90);
        Check("head", me.Sim.Grounded && MathF.Abs(me.Position.Y - o.Y) < 0.05f && Nearest() >= contact - slack,
            $"dropped onto a head: ended on the floor {me.Sim.Grounded} at y {me.Position.Y - o.Y:0.00}, {Nearest():0.00} m from it");

        // A crawler (no legs) blocks low; a player above its height is clear of it.
        Clear();
        var cr = Z(0, 0);
        cr.Limbs = Game.LegsMask;
        g.SelfTestZBlockBuild(false);
        Put(0, 3);
        cl = Go(Buttons.Forward, north, 60);
        var crawlStop = me.Position.Z - o.Z;
        Put(0, 0.2f, Game.ZBlockCrawlHeight + 0.05f);
        g.SelfTestZBlockMove(me, ref me.Sim, new InputCmd { Yaw = north, SpeedPct = 100 });
        var overIt = ZFlat(me.Position - cr.Position);
        Check("crawler", crawlStop > contact - 0.05f && overIt < 0.3f, $"walked at a crawler: stopped at z {crawlStop:0.00}; above its back: not pushed ({overIt:0.00} m from it)");

        // A walking zombie is held off a standing player's body.
        Clear();
        Put(0, 0);
        var wz = Z(0, -3);
        var held = float.MaxValue;
        for (var i = 0; i < 240; i++)
        {
            var to = me.Position - wz.Position;
            g.SelfTestZBlockWalkZombie(wz, MathF.Atan2(to.X, -to.Z));
            held = MathF.Min(held, ZFlat(me.Position - wz.Position));
        }
        Check("zombie held", held >= contact - slack && held < contact + 0.15f, $"walking 4 s at the player: closest {held:0.00} m");

        // The client's copy: the zombies as last heard (positions quantised as ZEnts sends them) give the same movement.
        Clear();
        for (var x = -4.5f; x <= 4.51f; x += 0.9f)
            Z(x, -1.2f);
        Z(0.2f, 1.7f);
        Z(-1.3f, 2.4f);
        var server = new List<Vector3>();
        var client = new List<Vector3>();
        foreach (var asClient in new[] { false, true })
        {
            foreach (var zz in g.Horde)
            {
                zz.Snaps.Clear();
                var p = zz.Position;
                zz.Snaps.Add((g.ServerTick, new Vector3(MathF.Round(p.X * 32) / 32, MathF.Round(p.Y * 64) / 64, MathF.Round(p.Z * 32) / 32), 0f));
            }
            g.SelfTestZBlockBuild(asClient);
            Put(0.05f, 4.5f);
            var path = asClient ? client : server;
            for (var i = 0; i < 150; i++)
            {
                var yaw = i < 90 ? 0.12f : -0.6f; // at the zombies, then along the wall
                g.SelfTestZBlockMove(me, ref me.Sim, new InputCmd { Buttons = Buttons.Forward | (i % 40 < 20 ? Buttons.Sprint : 0), Yaw = yaw, SpeedPct = 100 });
                path.Add(me.Position);
            }
        }
        var worst = 0f;
        for (var i = 0; i < server.Count; i++)
            worst = MathF.Max(worst, Vector3.Distance(server[i], client[i]));
        Check("prediction", worst < 0.08f, $"server and client paths over 150 ticks against the horde: worst {worst * 100:0.0} cm apart");

        // 600 zombies (most of them round the player): the bodies' rebuild and four players' pushes a tick, no allocation.
        Clear();
        var rng = new Random(7);
        for (var i = 0; i < Game.MaxZombies; i++)
        {
            var near = i < 200;
            var p = near ? new Vector3((float)(rng.NextDouble() * 10 - 5), 0, (float)(rng.NextDouble() * 10 - 5))
                : new Vector3((float)(rng.NextDouble() * 2 - 1) * (map.HalfX - 2) - o.X, 0, (float)(rng.NextDouble() * 2 - 1) * (map.HalfZ - 2) - o.Z);
            Z(p.X, p.Z);
        }
        Put(0, 0);
        var st = me.Sim;
        void Round()
        {
            g.SelfTestZBlockBuild(false);
            for (var k = 0; k < 4; k++)
            {
                me.Sim = st;
                g.SelfTestZBlockMove(me, ref me.Sim, new InputCmd { Buttons = Buttons.Forward, Yaw = k * 1.5f, SpeedPct = 100 });
            }
        }
        for (var i = 0; i < 30; i++)
            Round(); // warm up
        var moveOnly = 0.0;
        var c0 = ThreadCpu.NowMs();
        for (var i = 0; i < 300; i++)
            Round();
        var withBodies = (ThreadCpu.NowMs() - c0) / 300;
        // The same moves with no zombies, so the cost shown is the bodies' own.
        var saved = g.Horde.ToList();
        g.Horde.Clear();
        g.SelfTestZBlockBuild(false);
        for (var i = 0; i < 30; i++)
            for (var k = 0; k < 4; k++)
            {
                me.Sim = st;
                g.SelfTestZBlockMove(me, ref me.Sim, new InputCmd { Buttons = Buttons.Forward, Yaw = k * 1.5f, SpeedPct = 100 });
            }
        c0 = ThreadCpu.NowMs();
        for (var i = 0; i < 300; i++)
            for (var k = 0; k < 4; k++)
            {
                me.Sim = st;
                g.SelfTestZBlockMove(me, ref me.Sim, new InputCmd { Buttons = Buttons.Forward, Yaw = k * 1.5f, SpeedPct = 100 });
            }
        moveOnly = (ThreadCpu.NowMs() - c0) / 300;
        g.Horde.AddRange(saved);
        var alloc = SteadyAllocBytes(() =>
        {
            for (var i = 0; i < 60; i++)
                Round();
        });
        var cost = withBodies - moveOnly;
        Check("600 zombies", alloc == 0 && cost < 1.0, $"rebuild + 4 players a tick: {cost:0.000} ms CPU over the plain moves ({withBodies:0.000} vs {moveOnly:0.000}; {ThreadCpu.Kind}), {g.ZBlockBodies} bodies, {alloc} bytes allocated");
        Clear();

        ZLine("zombie bodies: " + string.Join("; ", lines));
        if (!ok)
            Fail("zombie bodies: " + string.Join("; ", lines.Where(l => l.Contains("FAILED"))));
    }

    /// <summary>The centre (feet on the floor) of an open square of flat floor <paramref name="half"/> m each way, or null.</summary>
    private static Vector3? ZBlockOpenPatch(Map map, ZombiesMap zm, float half)
    {
        foreach (var a in zm.Areas)
        {
            if (a.Banded)
                continue;
            for (var cx = a.X0 + half + 0.5f; cx <= a.X1 - half - 0.5f; cx += 1f)
                for (var cz = a.Z0 + half + 0.5f; cz <= a.Z1 - half - 0.5f; cz += 1f)
                {
                    var y = map.GroundHeight(cx, cz);
                    var clear = true;
                    for (var x = -half; x <= half && clear; x += 0.5f)
                        for (var z = -half; z <= half && clear; z += 0.5f)
                        {
                            var p = new Vector3(cx + x, y, cz + z);
                            clear = MathF.Abs(map.GroundHeight(p.X, p.Z) - y) < 0.01f && !World.Blocked(p + new Vector3(0, 0.05f, 0));
                        }
                    if (clear)
                        return new Vector3(cx, y, cz);
                }
        }
        return null;
    }
}
