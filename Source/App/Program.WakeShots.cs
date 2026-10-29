using System.Globalization;
using System.Numerics;
using Brutal.GlfwApi;

namespace CallOfSlop;

/// <summary>
/// <c>--stress-shots wake [size] [--wake-old]</c>: pictures of the mass horde waking and sleeping by sight
/// (docs/zombies/MASS-HORDE.md, "Waking and sleeping"), written next to the log as smoke_wake_&lt;view&gt;_&lt;s&gt;s.bmp.
/// An offline Zombies game on Horde Stress with <c>size</c> zombies (20,000 by default) seeded all round the player on
/// the valley floor (particles out to 45 m, the mass beyond), the player looking down the valley and shooting the
/// individual nearest the middle of their view every 0.2 s (unhurt). Every 2 s the log gets the tiers ("[wakeshots]":
/// individuals in view and behind, particles moving and waiting, the crowd's mean distance); at 6 and 20 s pictures from
/// the player's eye, from their eye looking back, and from above. <c>--wake-old</c>: the old behaviour's tuning (no
/// sight, no swaps, the front standing at the promote range, closing in from all round), for a before and after. Add
/// <c>--crowd-tiers</c> for the tier colours. Runs under a virtual X server like the other picture tools.
/// </summary>
public static unsafe partial class Program
{
    private static void RunWakeShots(string[] args)
    {
        var arg = args.SkipWhile(a => !a.Equals("wake", StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault(a => !a.StartsWith("--"));
        var size = arg != null && int.TryParse(arg, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? Math.Clamp(n, 100, Game.StressMax) : 20000;
        var old = args.Any(a => a.Equals("--wake-old", StringComparison.OrdinalIgnoreCase));
        if (Maps.Find("Horde Stress") is not { Zombies.Stress: { } st } map)
        {
            Console.WriteLine("[wakeshots] no Horde Stress map");
            return;
        }
        World.SetMap(map);
        var g = new Game(NetPeer.Offline(), AudioSystem, "Shots", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: map.Id)
        {
            MapVoteEnabled = false,
            Mode = GameMode.Zombies,
            StressSize = 600,
        };
        if (old)
        {
            var c0 = g.ZRules.Crowd;
            (c0.UnseenWeight, c0.SwapMargin, c0.HeldPace, c0.SurroundArc) = (1f, 100f, 0f, 360f);
        }
        _session = g;
        var now = Glfw.Time;
        for (var t = 0.0; t < Game.CountdownSeconds + 0.3; t += World.TickDt)
            g.Update(now += World.TickDt, World.TickDt);
        g.RequestRespawn();
        for (var k = 0; k < 6; k++)
            g.Update(now += World.TickDt, World.TickDt);
        var me = g.Local!;
        me.ImmuneUntil = 1e9;
        var at = new Vector3(st.Rally.X, map.GroundHeight(st.Rally.X, st.Rally.Y) + 0.05f, st.Rally.Y);
        me.Teleport(at);
        me.Yaw = MathF.PI / 2;
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
        var outer = MathF.Sqrt(12 * 12 + size / (MathF.PI * 2.2f)); // (about 2.2 a square metre on average)
        g.SelfTestCrowdScene(c =>
        {
            c.Reserve(Math.Min(size, HordeCrowd.ParticleCap));
            for (var k = 0; k < size * 3 && c.Total < size; k++)
            {
                var dir = World.FlatForward(Rand() * MathF.Tau);
                var d = MathF.Sqrt(12 * 12 + Rand() * (outer * outer - 12 * 12));
                float x = at.X + dir.X * d, z = at.Z + dir.Z * d;
                if (!c.Walkable(x, z))
                    continue;
                if (d > 45 || c.Count >= c.X.Length)
                    c.AddMass(x, z);
                else
                    c.Add(x, z, 0, 0, (uint)k * 2654435761u, g.ServerTick, c.MassHP);
            }
        });
        var tag = old ? "old" : "sight";
        Console.WriteLine($"[wakeshots] {tag}: {g.Crowd.Total} zombies round the player ({g.Crowd.Count} particles, {g.Crowd.MassTotal} mass, out to {outer:0} m), individual cap {g.CrowdIndividualCap}");
        var simulated = 0.0;
        var kills = 0;
        void Step()
        {
            now += World.TickDt;
            simulated += World.TickDt;
            g.SelfTestZombiesTick(now);
            if (g.ServerTick % 12 == 0)
            {
                Zombie? best = null;
                var bestCos = MathF.Cos(25 * MathF.PI / 180f);
                var fwd = World.FlatForward(me.Yaw);
                foreach (var z in g.Horde)
                {
                    var v = z.Position - me.Position;
                    v.Y = 0;
                    var l = v.Length();
                    if (!z.Alive || l > 30 || l < 0.1f)
                        continue;
                    var cos = Vector3.Dot(v / l, fwd);
                    if (cos > bestCos)
                        (bestCos, best) = (cos, z);
                }
                if (best != null && g.DamageZombie(me, best, best.Health, head: true, melee: false, now))
                    kills++;
            }
            foreach (var z in g.Horde)
                z.RenderPos = z.Position; // (no client interpolation here)
        }
        void Report()
        {
            var c = g.Crowd;
            var fwd = World.FlatForward(me.Yaw);
            var cosView = MathF.Cos(c.Tuning.SightHalfAngle * MathF.PI / 180f);
            int alive = 0, inView = 0, behind = 0, moving = 0;
            foreach (var z in g.Horde)
                if (z.Alive)
                {
                    alive++;
                    var v = z.Position - me.Position;
                    v.Y = 0;
                    var l = v.Length();
                    var cos = l > 0.1f ? Vector3.Dot(v / l, fwd) : 1;
                    inView += cos >= cosView ? 1 : 0;
                    behind += cos < 0 ? 1 : 0;
                }
            double sum = 0;
            for (var i = 0; i < c.Count; i++)
            {
                sum += Vector2.Distance(new Vector2(c.X[i], c.Z[i]), new Vector2(at.X, at.Z));
                moving += c.VX[i] * c.VX[i] + c.VZ[i] * c.VZ[i] > 0.09f ? 1 : 0;
            }
            for (var k = 0; k < c.MassCount.Length; k++)
                if (c.MassCount[k] > 0)
                {
                    var m = c.MassCentre(k);
                    sum += c.MassCount[k] * Vector2.Distance(new Vector2(m.X, m.Z), new Vector2(at.X, at.Z));
                }
            Console.WriteLine($"[wakeshots] {tag} {simulated:0} s: individuals {alive} ({inView} in view, {behind} behind), particles {c.Count} ({moving} moving, {c.Near} waiting), "
                              + $"mass {c.MassTotal}, mean distance {sum / Math.Max(1, c.Total):0.0} m; woken {g.CrowdWokeSeen} seen + {g.CrowdWokeUnseen} unseen, put to sleep {g.CrowdSleptUnseen} unseen + {g.CrowdSleptSeen} seen, woken again soon after {g.CrowdRewoken}, shot {kills}");
        }
        var eye = at + new Vector3(0, World.EyeHeight, 0);
        var views = new (string Name, Vector3 Eye, Vector3 Target)[]
        {
            ("eye", eye, eye + new Vector3(40, -2, 0)),
            ("back", eye, eye + new Vector3(-40, -2, 0)),
            ("above", at + new Vector3(-45, 55, 0.01f), at + new Vector3(5, 0, 0)),
        };
        void Shoot()
        {
            foreach (var view in views)
                for (var frame = 0; frame < 3 && !_window.ShouldClose; frame++)
                {
                    Glfw.PollEvents();
                    var fb = _window.FramebufferSize;
                    Gfx.BeginFrame(fb.X, fb.Y, map.Sky);
                    Gfx.SetCamera(Matrix4x4.CreateLookAt(view.Eye, view.Target, Vector3.UnitY),
                        Matrix4x4.CreatePerspectiveFieldOfView(1.1f, fb.X / (float)Math.Max(fb.Y, 1), 0.05f, MathF.Max(map.FarPlane, 900)));
                    Gfx.SetFog(map.Sky, map.FogStart, map.FogEnd);
                    DrawArena();
                    DrawZombiesMode(now);
                    Gfx.SetFog(map.Sky, 0, 0);
                    if (frame == 2)
                    {
                        _shotFull = true;
                        SaveScreenshot($"wake_{tag}_{view.Name}_{simulated:0}s");
                    }
                    _window.SwapBuffers();
                }
        }
        Report();
        for (var s = 1; s <= 10; s++)
        {
            for (var t = 0; t < 2 * World.TickRate; t++)
                Step();
            Report();
            if (s == 3 || s == 10)
                Shoot();
        }
        _session = null;
        g.Dispose();
        Console.WriteLine("[wakeshots] done");
    }
}
