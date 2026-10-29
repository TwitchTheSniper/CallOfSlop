using System.Numerics;
using Brutal.GlfwApi;

namespace CallOfSlop;

/// <summary>
/// <c>--sandbox-shots</c>: pictures of the Sandbox mode on Construct Yard (docs/sandbox/CONSTRUCTS.md), written next to
/// the log as smoke_sandbox_&lt;view&gt;.bmp, then the game quits: the yard, a buggy close up, ghost sockets for a held
/// part, the buggy driven across the yard, the gate open, and a buggy broken by a blast and a shot-off suspension (its
/// wheel lying beside it). An offline Sandbox game; runs under a virtual X server with Mesa's software GL.
/// </summary>
public static unsafe partial class Program
{
    private static bool _sandboxShots;

    private static void RunSandboxShots(string[] args)
    {
        if (Maps.Find("Construct Yard") is not { } map)
        {
            Console.WriteLine("[sandboxshots] no Construct Yard map");
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
        Run(2.0); // everything settles
        var me = g.Local!;
        me.ImmuneUntil = 1e9;
        var cars = g.Constructs.Bodies.Where(b => b.CanDrive).ToList();
        var gate = g.Constructs.Bodies.FirstOrDefault(b => b.JointCount > 0);
        Console.WriteLine($"[sandboxshots] {g.Constructs.Bodies.Count} constructs, {g.Constructs.TotalParts} parts; {cars.Count} cars, gate {(gate != null ? "yes" : "no")}, phase {g.Phase}");
        if (cars.Count < 2)
            return;
        var car = cars[0];
        var other = cars[1];
        me.Teleport(new Vector3(-20, 0, 18));

        void Shot(string name, Vector3 eye, Vector3 target, bool ghosts = false)
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
                if (ghosts)
                {
                    Gfx.SetBlend(true);
                    Gfx.SetDepthWrite(false);
                    ConstructRender.DrawGhosts(Gfx, now);
                    Gfx.SetDepthWrite(true);
                    Gfx.SetBlend(false);
                }
                Gfx.SetFog(map.Sky, 0, 0);
                if (frame == 2)
                {
                    SaveScreenshot($"sandbox_{name}");
                    Console.WriteLine($"[sandboxshots] {name}: {ConstructRender.LastBoxes} construct boxes; GL error 0x{(uint)Brutal.OpenGlApi.OpenGl.Native.glGetError():x}");
                }
                _window.SwapBuffers();
            }
        }

        Shot("yard", new Vector3(-2, 24, 36), new Vector3(-4, 0, -2));
        var c0 = car.WorldCom;
        Shot("buggy", c0 + new Vector3(-3.2f, 2.0f, -3.6f), c0);

        // Ghost sockets: a wheel held next to the other buggy with one wheel taken off (its suspension's axle lights up),
        // and a frame block (every free frame face).
        var wheel = Enumerable.Range(0, other.PartCount).First(i => other.DefOf(i).Role == PartRole.Wheel);
        g.Constructs.RemovePart(other, wheel);
        Run(0.6);
        ConstructRender.ClearAim();
        foreach (var (held, name) in new[] { ("Wheel", "ghosts_wheel"), ("Frame Block", "ghosts_block") })
        {
            var def = ConstructParts.Need(held);
            ConstructRender.GhostDef = def;
            ConstructRender.GhostBody = other;
            ConstructRender.GhostCount = ConstructBuild.Ghosts(other, def, ConstructRender.Ghosts, other.Com, 10);
            ConstructRender.GhostSelected = ConstructRender.GhostCount > 0 ? 0 : -1;
            ConstructRender.GhostValid = true;
            var oc = other.WorldCom;
            Shot(name, oc + new Vector3(-3.6f, 2.4f, 2.6f), oc, ghosts: true);
            Console.WriteLine($"[sandboxshots] {name}: {ConstructRender.GhostCount} ghost sockets for a {held}");
        }
        ConstructRender.ClearAim();

        // Drive the first buggy across the yard: seated, keys held, the server drives it.
        var seat = car.Root;
        me.Teleport(ConstructWorld.Ground(World.Query, car.WorldCom.X - 1.5f, car.WorldCom.Z));
        g.SandboxSend(CActKind.Use, body: car.Id, part: (byte)seat);
        var route = new (Buttons Keys, double Seconds)[] { (Buttons.Forward, 1.6), (Buttons.Forward | Buttons.Right, 0.5), (Buttons.Jump, 1.5) };
        foreach (var (keys, seconds) in route)
        {
            g.HeldButtons = keys;
            Run(seconds);
        }
        g.HeldButtons = Buttons.None;
        var cd = car.WorldCom;
        Console.WriteLine($"[sandboxshots] drove to {cd.X:0.0}, {cd.Y:0.00}, {cd.Z:0.0} (driver {car.Driver}, can drive {car.CanDrive})");
        Shot("driven", cd + new Vector3(-6f, 3.5f, 5f), cd);
        g.SandboxSend(CActKind.Stand);
        Run(0.2);

        // The gate, opened.
        if (gate != null)
        {
            var door = Enumerable.Range(0, gate.PartCount).First(i => gate.DefOf(i).Role == PartRole.Door);
            gate.Joints[0].Open = true;
            gate.Wake();
            Run(1.2);
            var gc = gate.WorldCom;
            Shot("gate_open", gc + new Vector3(3.5f, 2.6f, 5.5f), gc + new Vector3(0, 1, 0));
            _ = door;
        }

        // Broken: a blast by the other buggy, then its suspension and fuel line shot off.
        var ob = other.WorldCom;
        g.Constructs.Blast(ob + new Vector3(0.6f, 0.4f, -1.2f), 2.5f, 160f);
        for (var i = 0; i < other.PartCount; i++)
            if (other.DefOf(i).Role == PartRole.Suspension && other.Wheels.AsSpan(0, other.WheelCount).ToArray().Any(w => w.Suspension == i))
            {
                g.Constructs.DamagePart(other, i, 10000, out _);
                break;
            }
        for (var i = 0; i < other.PartCount; i++)
            if (other.DefOf(i).Role == PartRole.FuelLine)
            {
                g.Constructs.DamagePart(other, i, 10000, out _);
                break;
            }
        Run(2.0);
        Console.WriteLine($"[sandboxshots] broken: {other.PartCount} parts left, drives {other.CanDrive} ({other.DriveProblem}); {g.Constructs.Bodies.Count(b => b.Wreck)} wrecks");
        var obc = other.WorldCom;
        foreach (var b in g.Constructs.Bodies)
            Console.WriteLine($"[sandboxshots]   construct {b.Id}: {b.PartCount} parts ({b.DefOf(0).Name}...), at {b.WorldCom.X:0.0}, {b.WorldCom.Y:0.00}, {b.WorldCom.Z:0.0}{(b.Wreck ? ", wreck" : "")}{(b == other ? " (the broken buggy)" : "")}");
        Shot("broken", obc + new Vector3(2.5f, 3.2f, 3.6f), obc + new Vector3(0.3f, 0, 0));
        _session = null;
        g.Dispose();
        Console.WriteLine("[sandboxshots] done");
    }
}
