using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Brutal.GlfwApi;

namespace CallOfSlop;

/// <summary>
/// <c>--stress-shots [size] [--stress-speed N]</c>: pictures of Horde Stress (docs/zombies/MASS-HORDE.md, "Horde Stress"),
/// written next to the log as smoke_stress_&lt;view&gt;_&lt;moment&gt;.bmp, then the game quits. An offline Zombies game
/// with a stress round of <c>size</c> (10,000 by default), the player on the Overlook (unhurt), the crowd walking
/// <c>N</c> times as fast (3 by default, so a run takes a minute or two): pictures from above the Overlook and from the
/// player's eye while the hordes wait on their pads, as they descend, when they have merged and as they advance, with
/// the tier counts, the crowd's tick and the instances drawn in the log ("[stressshots]"). Uses the smoke test's
/// throwaway save copy; runs under a virtual X server with Mesa's software GL. <c>--stress-shots wake</c>: the horde
/// round the player instead (Program.WakeShots.cs); <c>--stress-shots waves</c>: a strip of the blob's front waking
/// (Program.WaveShots.cs).
/// </summary>
public static unsafe partial class Program
{
    private static bool _stressShots;

    private static void RunStressShots(string[] args)
    {
        var arg = args.SkipWhile(a => !a.Equals("--stress-shots", StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault(a => !a.StartsWith("--"));
        if (arg != null && arg.Equals("wake", StringComparison.OrdinalIgnoreCase))
        {
            RunWakeShots(args); // --stress-shots wake: a horde round the player on the valley floor (Program.WakeShots.cs)
            return;
        }
        if (arg != null && arg.Equals("waves", StringComparison.OrdinalIgnoreCase))
        {
            RunWaveShots(args); // --stress-shots waves: the blob's front waking, a strip of pictures (Program.WaveShots.cs)
            return;
        }
        var size = arg != null && int.TryParse(arg, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 10000;
        var sp = args.SkipWhile(a => !a.Equals("--stress-speed", StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault();
        var speed = sp != null && float.TryParse(sp, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? Math.Clamp(f, 0.25f, 20f) : 3f;
        if (Maps.Find("Horde Stress") is not { Zombies.Stress: { } st } map)
        {
            Console.WriteLine("[stressshots] no Horde Stress map");
            return;
        }
        // --crowd-no-horizon: the same pictures without the terrain horizon culling (they must match: it hides only what the ground hides).
        Crowd.Host.HorizonCull = Crowd.Client.HorizonCull = !args.Any(a => a.Equals("--crowd-no-horizon", StringComparison.OrdinalIgnoreCase));
        CrowdAsClient = args.Any(a => a.Equals("--crowd-as-client", StringComparison.OrdinalIgnoreCase)); // (the host draws its picture, as a client)
        World.SetMap(map);
        var g = new Game(NetPeer.Offline(), AudioSystem, "Shots", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: map.Id)
        {
            MapVoteEnabled = false,
            Mode = GameMode.Zombies,
            StressSize = size,
            StressSpeedScale = speed,
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
        var eye = new Vector3(-170, map.GroundHeight(-170, 0), 0);
        me.Teleport(eye + new Vector3(0, 0.05f, 0));
        g.SelfTestBeginRound(1, now);
        var views = new (string Name, Vector3 Eye, Vector3 Target)[]
        {
            ("above", eye + new Vector3(-40, 70, 0), new Vector3(60, -10, 0)),
            ("eye", eye + new Vector3(0, 1.6f, 0), new Vector3(60, -4, 0)),
            ("plan", new Vector3(0, 420, 0.01f), Vector3.Zero),
        };
        var simulated = 0.0;
        double tickMs = 0;
        var ticks = 0;
        double worstTick = 0;
        void Step()
        {
            now += World.TickDt;
            simulated += World.TickDt;
            var t0 = Stopwatch.GetTimestamp();
            g.SelfTestZombiesTick(now);
            var ms = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
            tickMs += ms;
            worstTick = Math.Max(worstTick, ms);
            ticks++;
            foreach (var z in g.Horde)
                z.RenderPos = z.Position; // (no client interpolation here)
        }
        var cw = new PacketWriter(32 * 1024);
        var cview = new Game.CrowdNetView();
        void Shoot(string moment)
        {
            var c = g.Crowd;
            // What one client on the Overlook would be sent now (Msg.ZCrowd: a keyframe the first time, then the changes).
            g.CrowdCount();
            var sends = 0;
            var bytes = 0;
            var biggest = 0;
            // (Three sends: cells further than the far range go every third one; more while a message is capped.)
            do
            {
                g.WriteZCrowdFor(cw, cview, me.Position);
                g.SelfTestReadCrowd(cw.Span);
                bytes += cw.Length;
                biggest = Math.Max(biggest, cw.Length);
                sends++;
            }
            while ((sends < g.ZRules.Crowd.NetFarEvery || cw.Length >= Game.ZCrowdMaxBytes - 64) && sends < 64);
            Console.WriteLine($"[stressshots] {moment}: Msg.ZCrowd to a client: {bytes} B in {sends} message(s), biggest {biggest} B; the client's picture holds {g.CrowdSeen.Total} of the crowd's {c.Total}");
            Console.WriteLine($"[stressshots] {moment} at {simulated:0} s: {g.StressState}, live {g.StressLive} (individuals {g.AliveZombies}, particles {c.Count}, mass {c.MassTotal}), "
                              + $"{g.StressNearShare * 100:0}% in the blob at the rally point; zombies tick avg {(ticks > 0 ? tickMs / ticks : 0):0.00} ms, worst {worstTick:0.00} ms over {ticks}");
            (tickMs, ticks, worstTick) = (0, 0, 0);
            foreach (var view in views)
                for (var frame = 0; frame < 3 && !_window.ShouldClose; frame++)
                {
                    Glfw.PollEvents();
                    var fb = _window.FramebufferSize;
                    Gfx.BeginFrame(fb.X, fb.Y, map.Sky);
                    Gfx.SetCamera(Matrix4x4.CreateLookAt(view.Eye, view.Target, Vector3.UnitY),
                        Matrix4x4.CreatePerspectiveFieldOfView(1.1f, fb.X / (float)Math.Max(fb.Y, 1), 0.05f, MathF.Max(map.FarPlane, 900)));
                    Gfx.SetFog(map.Sky, view.Name == "plan" ? 0 : map.FogStart, view.Name == "plan" ? 0 : map.FogEnd); // (the plan from 420 m up: no fog)
                    DrawArena();
                    DrawZombiesMode(now);
                    Gfx.SetFog(map.Sky, 0, 0);
                    if (frame == 2)
                    {
                        _shotFull = true;
                        SaveScreenshot($"stress_{view.Name}_{moment}");
                        var ag = Crowd.Agents;
                        Console.WriteLine($"[stressshots] {view.Name} {moment}: {CrowdInst.Count[0]}/{CrowdInst.Count[1]}/{CrowdInst.Count[2]} near/mid/far ({CrowdInst.Particles} particles, {CrowdInst.Agents} agents), {CrowdInst.Culled} culled, "
                                          + $"cells {ag.CellsDrawn} drawn, {ag.CellsCulled} out of view, {ag.CellsFogged} fogged, {ag.CellsHidden} behind the ground; prep {CrowdPrepMs:0.00} ms; GL error 0x{(uint)Brutal.OpenGlApi.OpenGl.Native.glGetError():x}");
                    }
                    _window.SwapBuffers();
                }
        }
        Step();
        Shoot("staging");
        while (g.StressState == Game.StressPhase.Staging && simulated < 120)
            Step();
        var descent = simulated;
        while (g.StressState == Game.StressPhase.Gather && simulated - descent < 15 / speed * 3)
            Step();
        Shoot("descending");
        while (g.StressState == Game.StressPhase.Gather && simulated < 900)
            Step();
        Shoot("merged");
        var merged = simulated;
        while (simulated - merged < 60 / speed * 3 && g.StressLive > 0)
            Step();
        Shoot("advancing");
        g.StressSummary("stress shots");
        _session = null;
        g.Dispose();
        Console.WriteLine("[stressshots] done");
    }
}
