using System.Numerics;
using Brutal.GlfwApi;

namespace CallOfSlop;

/// <summary>
/// <c>--joint-shots</c>: pictures of the parts beyond construct joints colliding (docs/sandbox/CONSTRUCTS.md "Joint
/// collision"), written next to the log as smoke_joints_&lt;view&gt;.bmp, then the game quits: a hinged flap before and
/// after it swings down onto the yard's ground, a free arm driven into the ground lifting its construct, and the yard's
/// turret turned into a wall. An offline Sandbox game on Construct Yard; runs under a virtual X server with Mesa's software GL.
/// </summary>
public static unsafe partial class Program
{
    private static bool _jointShots;

    private static void RunJointShots()
    {
        if (Maps.Find("Construct Yard") is not { } map)
        {
            Console.WriteLine("[jointshots] no Construct Yard map");
            return;
        }
        World.SetMap(map);
        var g = new Game(NetPeer.Offline(), AudioSystem, "Shots", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: map.Id)
        {
            MapVoteEnabled = false,
            Mode = GameMode.Sandbox,
        };
        _session = g;
        var now = Glfw.Time;
        void Run(double seconds)
        {
            for (var t = 0.0; t < seconds; t += World.TickDt)
                g.Update(now += World.TickDt, World.TickDt);
        }
        Run(Game.CountdownSeconds + 0.3);
        g.RequestRespawn();
        Run(1.0);
        var me = g.Local!;
        me.ImmuneUntil = 1e9;
        me.Teleport(new Vector3(-20, 0, 18));

        void Shot(string name, Vector3 eye, Vector3 target)
        {
            for (var frame = 0; frame < 3 && !_window.ShouldClose; frame++)
            {
                Glfw.PollEvents();
                var fb = _window.FramebufferSize;
                Gfx.BeginFrame(fb.X, fb.Y, map.Sky);
                Gfx.SetCamera(Matrix4x4.CreateLookAt(eye, target, Vector3.UnitY),
                    Matrix4x4.CreatePerspectiveFieldOfView(1.1f, fb.X / (float)Math.Max(fb.Y, 1), 0.05f, map.FarPlane));
                Gfx.SetFog(map.Sky, map.FogStart, map.FogEnd);
                DrawArena();
                DrawSandbox(now);
                Gfx.SetFog(map.Sky, 0, 0);
                if (frame == 2)
                {
                    SaveScreenshot($"joints_{name}");
                    Console.WriteLine($"[jointshots] {name}: {ConstructRender.LastBoxes} construct boxes");
                }
                _window.SwapBuffers();
            }
        }

        var w = g.Constructs;
        // A flap on a foundation: built level, then latched open and flung down onto the ground.
        if (SelfTest.BuildFlap(w, World.Query, 6, 14) is { } flap)
        {
            Run(0.3);
            var fc = flap.WorldCom + new Vector3(1.2f, 0, 0);
            Shot("flap_level", fc + new Vector3(2.5f, 1.6f, 4.5f), fc);
            flap.Joints[0].Open = true;
            flap.Joints[0].Vel = 6;
            flap.Wake();
            Run(2.0);
            Shot("flap_ground", fc + new Vector3(2.5f, 0.9f, 4.5f), fc + new Vector3(0, -0.2f, 0));
            Shot("flap_ground_low", fc + new Vector3(3.6f, 0.25f, 0.4f), fc + new Vector3(0, 0.1f, 0));
            Console.WriteLine($"[jointshots] flap: hinge at {flap.Joints[0].Angle * 180 / MathF.PI:0.0} deg, asleep {flap.Asleep}");
        }
        // A free arm (beam, motor hinge, beam) driven down into the ground: it lifts its own construct.
        if (SelfTest.BuildArm(w, World.Query, 2, 10) is { } arm)
        {
            Run(0.5);
            var ac = arm.WorldCom;
            Shot("arm_level", ac + new Vector3(4.5f, 1.8f, 1.5f), ac);
            arm.Joints[0].Open = true;
            arm.Wake();
            Run(3.0);
            ac = arm.WorldCom;
            Shot("arm_lifted", ac + new Vector3(4.5f, 1.2f, 1.5f), ac);
            Console.WriteLine($"[jointshots] arm: motor at {arm.Joints[0].Angle * 180 / MathF.PI:0} deg, the body tilted {MathF.Acos(Math.Clamp(Vector3.Transform(Vector3.UnitY, arm.Rot).Y, -1, 1)) * 180 / MathF.PI:0} deg");
        }
        // The yard's turret turned by its lever into a wall built beside it.
        var turret = w.Bodies.FirstOrDefault(b => b.JointCount > 0 && b.Joints[0].Type == JointType.Turntable);
        if (turret != null)
        {
            var o = turret.Pos;
            SelfTest.BuildWallArm(w, World.Query, o.X + 2.7f, o.Z + 0.75f, false);
            SelfTest.BuildWallArm(w, World.Query, o.X - 3.2f, o.Z + 0.75f, true);
            Run(0.3);
            var lever = Enumerable.Range(0, turret.PartCount).First(i => turret.DefOf(i).Name == "Lever");
            turret.Parts[lever].State = 1;
            turret.Wake();
            Run(3.0);
            var tc = turret.WorldCom + new Vector3(0, 0.6f, 0);
            Shot("turret_wall", tc + new Vector3(-1.5f, 3.6f, 5.0f), tc + new Vector3(0.6f, 0, 0.6f));
            Console.WriteLine($"[jointshots] turret: stopped at {turret.Joints[0].Angle * 180 / MathF.PI:0.0} deg");
        }
        _session = null;
        g.Dispose();
        Console.WriteLine("[jointshots] done");
    }
}
