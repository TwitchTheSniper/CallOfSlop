using System.Globalization;
using System.Numerics;
using Brutal.GlfwApi;

namespace CallOfSlop;

/// <summary>
/// <c>--stress-shots waves [size] [--waves-old]</c>: a strip of pictures of the mass horde waking at its edge
/// (docs/zombies/MASS-HORDE.md, "Wake waves"), written next to the log as smoke_waves_&lt;soft|old&gt;_&lt;nn&gt;.bmp. An
/// offline Zombies game on Horde Stress at round 10's pace: a blob of <c>size</c> zombies (20,000 by default) in the mass
/// 60-150 m ahead of the player on the valley floor, coming on; from 2 s, every quarter second for 3 s, a picture from
/// the player's eye zoomed on the blob's front as it crosses the wake edge (the host drawing every tick, so the agents
/// carry on as in the game). Every picture's line in the log ("[waveshots]") has the releases, the hand-overs and the
/// agents rising and sinking so far. <c>--waves-old</c>: softWake off (the old release, rising and sinking), for a before
/// and after. Add <c>--crowd-tiers</c> for the tier colours. Runs under a virtual X server like the other picture tools.
/// </summary>
public static unsafe partial class Program
{
    private static void RunWaveShots(string[] args)
    {
        var arg = args.SkipWhile(a => !a.Equals("waves", StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault(a => !a.StartsWith("--"));
        var size = arg != null && int.TryParse(arg, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? Math.Clamp(n, 500, 100000) : 20000;
        var old = args.Any(a => a.Equals("--waves-old", StringComparison.OrdinalIgnoreCase));
        if (Maps.Find("Horde Stress") is not { Zombies.Stress: { } st } map)
        {
            Console.WriteLine("[waveshots] no Horde Stress map");
            return;
        }
        World.SetMap(map);
        var g = new Game(NetPeer.Offline(), AudioSystem, "Shots", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: map.Id)
        {
            MapVoteEnabled = false,
            Mode = GameMode.Zombies,
            StressSize = 600,
        };
        g.ZRules.Crowd.SoftWake = !old;
        _session = g;
        var now = Glfw.Time;
        for (var t = 0.0; t < Game.CountdownSeconds + 0.3; t += World.TickDt)
            g.Update(now += World.TickDt, World.TickDt);
        g.RequestRespawn();
        for (var k = 0; k < 6; k++)
            g.Update(now += World.TickDt, World.TickDt);
        var me = g.Local!;
        me.ImmuneUntil = 1e9;
        var at = new Vector3(st.Rally.X - 6, map.GroundHeight(st.Rally.X - 6, st.Rally.Y + 4) + 0.05f, st.Rally.Y + 4); // (beside the rally post, not in it)
        me.Teleport(at);
        me.Yaw = MathF.PI / 2;
        me.Pitch = 0;
        g.SelfTestBeginRound(10, now);
        g.SelfTestCrowdScene(c =>
        {
            c.Reserve(Math.Min(size, HordeCrowd.ParticleCap));
            var radius = MathF.Sqrt(size / (float)c.Tuning.MassCapacity) * c.MassCell * 0.6f;
            c.SeedMass(at.X + 60 + radius, at.Z, size, c.Tuning.MassCapacity, radius);
        });
        var tag = old ? "old" : "soft";
        var c = g.Crowd;
        Console.WriteLine($"[waveshots] {tag}: {c.Total} zombies in the mass ahead of the player (softWake {(!old ? "on" : "off")})");
        var eye = at + new Vector3(0, World.EyeHeight, 0);
        var target = new Vector3(at.X + 50, at.Y + 0.6f, at.Z);
        var fb0 = _window.FramebufferSize;
        var proj = Matrix4x4.CreatePerspectiveFieldOfView(0.55f, fb0.X / (float)Math.Max(fb0.Y, 1), 0.05f, MathF.Max(map.FarPlane, 900));
        var viewM = Matrix4x4.CreateLookAt(eye, target, Vector3.UnitY);
        var fog = map.FogEnd > map.FogStart ? map.FogEnd : 0f;
        var simulated = 0.0;
        void Step()
        {
            now += World.TickDt;
            simulated += World.TickDt;
            g.SelfTestZombiesTick(now);
            foreach (var z in g.Horde)
                z.RenderPos = z.Position; // (no client interpolation here)
            ZombieAnim.SetFrustum(viewM * proj);
            Crowd.Prepare(g, eye, now, fog); // (the drawing every tick, as in the game, so the agents carry on)
        }
        void Shoot(int k)
        {
            for (var frame = 0; frame < 3; frame++) // (the first frames after a jump draw while the ground's buffers fill)
            {
                Glfw.PollEvents();
                var fb = _window.FramebufferSize;
                Gfx.BeginFrame(fb.X, fb.Y, map.Sky);
                Gfx.SetCamera(viewM, proj);
                Gfx.SetFog(map.Sky, map.FogStart, map.FogEnd);
                DrawArena();
                DrawZombiesMode(now);
                Gfx.SetFog(map.Sky, 0, 0);
                if (frame == 2)
                {
                    _shotFull = true;
                    SaveScreenshot($"waves_{tag}_{k:00}");
                }
                _window.SwapBuffers();
            }
            var a = Crowd.Agents;
            Console.WriteLine($"[waveshots] {tag} {simulated:0.00} s: particles {c.Count}, mass {c.MassTotal}, individuals {g.AliveZombies}; released {c.Released}, merged {c.Merged}; "
                              + $"hand-overs {Crowd.HandOvers} ({Crowd.HandMisses} with no agent), agents raised {a.Raised}, sunk {a.Sunk}, pulled forward {a.Pulled}");
        }
        for (var t = 0; t < 2 * World.TickRate; t++)
            Step();
        for (var k = 0; k < 12 && !_window.ShouldClose; k++)
        {
            Shoot(k);
            for (var t = 0; t < World.TickRate / 4; t++)
                Step();
        }
        _session = null;
        g.Dispose();
        Console.WriteLine("[waveshots] done");
    }
}
