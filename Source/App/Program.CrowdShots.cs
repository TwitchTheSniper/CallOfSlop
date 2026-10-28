using System.Globalization;
using System.Numerics;
using Brutal.GlfwApi;

namespace CallOfSlop;

/// <summary>
/// <c>--crowd-shots [count]</c>: pictures of the mass horde spike (docs/zombies/MASS-HORDE.md) on Mass Horde Test, written
/// next to the log as smoke_crowd_&lt;view&gt;_&lt;seconds&gt;s.bmp, then the game quits. An offline Zombies game with the
/// crowd round seeded (10,000 by default), the player at the middle (unhurt), pictures from above and from the player
/// after 0, 3, 8 and 16 seconds: the tier colours (yellow crowd, orange held back, red markers on individuals). Uses the
/// smoke test's throwaway save copy; runs under a virtual X server with Mesa's software GL.
/// </summary>
public static unsafe partial class Program
{
    private static bool _crowdShots;

    private static void RunCrowdShots(string[] args)
    {
        var arg = args.SkipWhile(a => !a.Equals("--crowd-shots", StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault(a => !a.StartsWith("--"));
        var count = arg != null && int.TryParse(arg, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 10000;
        if (Maps.Find("Mass Horde Test") is not { } map)
        {
            Console.WriteLine("[crowdshots] no Mass Horde Test map");
            return;
        }
        World.SetMap(map);
        var g = new Game(NetPeer.Offline(), AudioSystem, "Shots", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: map.Id)
        {
            MapVoteEnabled = false,
            Mode = GameMode.Zombies,
        };
        _session = g;
        var now = Glfw.Time;
        for (var t = 0.0; t < Game.CountdownSeconds + 0.3; t += World.TickDt)
            g.Update(now += World.TickDt, World.TickDt);
        g.RequestRespawn();
        for (var k = 0; k < 6; k++)
            g.Update(now += World.TickDt, World.TickDt);
        var me = g.Local!;
        me.ImmuneUntil = 1e9;
        me.Teleport(Vector3.Zero);
        g.CrowdOverride = count;
        g.SelfTestBeginRound(1, now);
        Console.WriteLine($"[crowdshots] {g.Crowd.Count} in the crowd");
        var views = new (string Name, Vector3 Eye, Vector3 Target)[]
        {
            ("above", new Vector3(0, 95, 70), new Vector3(0, 0, -5)),
            ("near", new Vector3(-2, 6, 14), new Vector3(0, 0, -20)),
        };
        var simulated = 0.0;
        var cw = new PacketWriter(32 * 1024);
        var cview = new Game.CrowdNetView();
        foreach (var at in new[] { 0.0, 3.0, 8.0, 16.0 })
        {
            for (; simulated < at; simulated += World.TickDt)
            {
                now += World.TickDt;
                g.SelfTestZombiesTick(now);
                foreach (var z in g.Horde)
                    z.RenderPos = z.Position; // (no client interpolation here)
            }
            Console.WriteLine($"[crowdshots] {at:0} s: crowd {g.Crowd.Count} ({g.Crowd.Near} near), individuals {g.AliveZombies}, promoted {g.Crowd.Promoted}, demoted {g.Crowd.Demoted}; {g.HordeSummary()}");
            // Three looks: the crowd as the host draws it, with the tier colours, and as a client draws it from Msg.ZCrowd.
            g.CrowdCount();
            g.WriteZCrowdFor(cw, cview, me.Position);
            g.SelfTestReadCrowd(cw.Span);
            foreach (var look in new[] { "host", "tiers", "client" })
            {
                CrowdTiers = look == "tiers";
                CrowdAsClient = look == "client";
                foreach (var view in views)
                    for (var frame = 0; frame < 3 && !_window.ShouldClose; frame++)
                    {
                        Glfw.PollEvents();
                        var fb = _window.FramebufferSize;
                        Gfx.BeginFrame(fb.X, fb.Y, map.Sky);
                        Gfx.SetCamera(Matrix4x4.CreateLookAt(view.Eye, view.Target, Vector3.UnitY),
                            Matrix4x4.CreatePerspectiveFieldOfView(1.1f, fb.X / (float)Math.Max(fb.Y, 1), 0.05f, map.FarPlane));
                        Gfx.SetFog(map.Sky, map.FogStart, map.FogEnd);
                        DrawArena();
                        DrawZombiesMode(now);
                        Gfx.SetFog(map.Sky, 0, 0);
                        if (frame == 2)
                        {
                            SaveScreenshot($"crowd_{look}_{view.Name}_{at:0}s");
                            Console.WriteLine($"[crowdshots] {look} {view.Name}: {CrowdInst.Count[0]}/{CrowdInst.Count[1]}/{CrowdInst.Count[2]} near/mid/far, {CrowdInst.Culled} culled, prep {CrowdPrepMs:0.00} ms; GL error 0x{(uint)Brutal.OpenGlApi.OpenGl.Native.glGetError():x}");
                        }
                        _window.SwapBuffers();
                    }
            }
            CrowdTiers = CrowdAsClient = false;
        }
        _session = null;
        g.Dispose();
        Console.WriteLine("[crowdshots] done");
    }
}
