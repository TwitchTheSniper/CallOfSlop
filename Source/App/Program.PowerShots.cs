using System.Numerics;
using Brutal.GlfwApi;

namespace CallOfSlop;

/// <summary>
/// <c>--power-shots</c>: pictures of Sandbox milestone 3's links and power (docs/sandbox/CONSTRUCTS.md "Milestone 3"),
/// written next to the log as smoke_power_&lt;view&gt;.bmp, then the game quits: the yard's buggy and trailer, the trailer
/// towed round a turn, the wall button's wire into the Motor Gate (the linking tool's view) and the gate it opened, the
/// battery lamp post lit with its charge gauge, the wired buggy's gauge and the driving HUD (charge and gear), and a
/// wrecking ball swinging on a chain. An offline Sandbox game on Construct Yard; runs under a virtual X server with Mesa's software GL.
/// </summary>
public static unsafe partial class Program
{
    private static bool _powerShots;

    private static void RunPowerShots()
    {
        if (Maps.Find("Construct Yard") is not { } map)
        {
            Console.WriteLine("[powershots] no Construct Yard map");
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
        Run(1.5);
        var me = g.Local!;
        me.ImmuneUntil = 1e9;
        me.Teleport(new Vector3(-20, 0, 18));
        var w = g.Constructs;

        void Shot(string name, Vector3 eye, Vector3 target, bool wires = false, bool hud = false)
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
                if (wires)
                {
                    ConstructRender.ShowWires = true;
                    ConstructRender.WireEye = eye;
                    Gfx.SetBlend(true);
                    Gfx.SetDepthWrite(false);
                    ConstructRender.DrawWires(Gfx, w);
                    Gfx.SetDepthWrite(true);
                    Gfx.SetBlend(false);
                    ConstructRender.ShowWires = false;
                }
                Gfx.SetFog(map.Sky, 0, 0);
                if (hud)
                {
                    Ui.BeginFrame();
                    Ui.DrawHud(g, now, "", _settings);
                    Ui.EndFrame();
                }
                if (frame == 2)
                {
                    _shotFull = hud;
                    SaveScreenshot($"power_{name}");
                    Console.WriteLine($"[powershots] {name}: {ConstructRender.LastBoxes} construct boxes, {ConstructRender.LastChainLinks} chain links, {ConstructRender.LastWires} wires; GL error 0x{(uint)Brutal.OpenGlApi.OpenGl.Native.glGetError():x}");
                }
                _window.SwapBuffers();
            }
        }

        Construct? Having(string part, string? also = null) => w.Bodies.FirstOrDefault(b => Part(b, part) >= 0 && (also == null || Part(b, also) >= 0));
        static int Part(Construct b, string name) => Enumerable.Range(0, b.PartCount).Where(i => b.DefOf(i).Name == name).DefaultIfEmpty(-1).First();
        // A gauge's dial face, looked at from in front of it.
        void Dial(string name, Construct c, int part)
        {
            ref var p = ref c.Parts[part];
            c.WorldFrame(p.Seg, out var q, out var t);
            var d = p.D;
            var hub = t + Vector3.Transform(p.Pos + Rot24.Apply(p.Rot, d.DialAt), q);
            var n = Vector3.Transform(ConstructParts.DirVector(Rot24.ApplyDir(p.Rot, d.DialDir)), q);
            Shot(name, hub + n * 0.45f + new Vector3(0, 0.08f, 0), hub);
        }
        Console.WriteLine($"[powershots] {w.Bodies.Count} constructs, {w.CouplingCount} couplings, {w.XWireCount} wires between constructs");

        // The buggy and its trailer, then towed round a turn.
        if (Having("Tow Hitch") is { } car && Having("Tow Eye") is { } trailer)
        {
            var mid = (car.WorldCom + trailer.WorldCom) * 0.5f;
            Shot("trailer", mid + new Vector3(-5.5f, 3.2f, 3.5f), mid);
            Shot("hitch_close", ConstructWorld.HitchPoint(car, Part(car, "Tow Hitch")) + new Vector3(-1.4f, 0.9f, 0.6f), ConstructWorld.HitchPoint(car, Part(car, "Tow Hitch")));
            me.Teleport(ConstructWorld.Ground(World.Query, car.WorldCom.X - 1.5f, car.WorldCom.Z));
            Run(0.2);
            g.SandboxSend(CActKind.Use, body: car.Id, part: (byte)car.DriveSeat);
            Run(0.2);
            g.HeldButtons = Buttons.Forward;
            Run(1.6);
            g.HeldButtons = Buttons.Forward | Buttons.Right;
            Run(1.4);
            g.HeldButtons = Buttons.Jump;
            Run(0.6);
            g.HeldButtons = Buttons.None;
            mid = (car.WorldCom + trailer.WorldCom) * 0.5f;
            Shot("towed", mid + new Vector3(-4f, 5.5f, 6f), mid);
            g.SandboxSend(CActKind.Stand);
            Run(0.3);
        }
        // The wall button's wire into the Motor Gate (as the linking tool shows it), then pressed: the gate opens.
        if (Having("Button", "Foundation") is { } _ && w.XWireCount > 0 && w.Get(w.XWires[0].BodyFrom) is { } wall && w.Get(w.XWires[0].BodyTo) is { } gate)
        {
            var mid = (wall.WorldCom + gate.WorldCom) * 0.5f;
            Shot("wall_button_wire", mid + new Vector3(2.5f, 4.5f, -6.5f), mid + new Vector3(0, 0.6f, 0), wires: true);
            var button = w.XWires[0].From;
            me.Teleport(ConstructWorld.Ground(World.Query, wall.WorldCom.X, wall.WorldCom.Z - 2f));
            Run(0.2);
            g.SandboxSend(CActKind.Use, body: wall.Id, part: button);
            Run(2.0);
            Shot("wall_button_gate_open", mid + new Vector3(2.5f, 3.5f, -7.5f), mid + new Vector3(-1, 0.8f, 0));
        }
        // The lamp post on its battery, lit with someone near (its charge gauge on the post).
        if (Having("Proximity Sensor", "Battery") is { } post)
        {
            me.Teleport(ConstructWorld.Ground(World.Query, post.WorldCom.X + 2.5f, post.WorldCom.Z + 1f));
            Run(0.5);
            var pc = post.WorldCom;
            Shot("lamp_post", pc + new Vector3(3.5f, 1.2f, 4.5f), pc + new Vector3(0, 0.6f, 0));
            var gauge = Part(post, "Charge Gauge");
            if (gauge >= 0)
                Dial("charge_gauge", post, gauge);
        }
        // The wired buggy: driven (the HUD's charge and gear), and its charge gauge close up.
        if (Having("Alternator") is { } wired)
        {
            me.Teleport(ConstructWorld.Ground(World.Query, wired.WorldCom.X - 1.5f, wired.WorldCom.Z));
            Run(0.2);
            g.SandboxSend(CActKind.Use, body: wired.Id, part: (byte)wired.DriveSeat);
            Run(0.2);
            g.HeldButtons = Buttons.Forward;
            Run(1.2);
            var wc = wired.WorldCom;
            var f = Vector3.Transform(wired.SeatForward(), wired.Rot);
            Shot("driving_hud", wc - f * 6f + new Vector3(0, 3f, 0), wc + f * 3f, hud: true);
            g.HeldButtons = Buttons.Jump;
            Run(0.8);
            g.HeldButtons = Buttons.None;
            g.SandboxSend(CActKind.Stand);
            Run(0.3);
            var gi = Part(wired, "Charge Gauge");
            if (gi >= 0)
                Dial("buggy_gauge", wired, gi);
        }
        // A wrecking ball on a chain from a post, swinging.
        if (ConstructBuild.PlaceNew(w, ConstructParts.Need("Foundation"), ConstructWorld.Ground(World.Query, 14, -4), 0) is { } crane)
        {
            int Fit(Construct c, int on, string socket, string part, string by)
            {
                var def = ConstructParts.Need(part);
                var ts = Array.FindIndex(c.DefOf(on).Sockets, s => s.Name == socket);
                var ds = Array.FindIndex(def.Sockets, s => s.Name == by);
                if (ts < 0 || ds < 0 || ConstructBuild.Place(c, on, ts, def, ds, 0, out var gh) != null)
                    return -1;
                var i = ConstructBuild.Attach(w, c, def, gh);
                c.Rebuild();
                return i;
            }
            var top = Fit(crane, 0, "top11", "Frame Block", "-y");
            for (var k = 0; k < 8 && top >= 0; k++)
                top = Fit(crane, top, "+y", "Frame Block", "-y");
            var jib = top < 0 ? -1 : Fit(crane, top, "+x", "Frame Beam", "end-");
            var hook = jib < 0 ? -1 : Fit(crane, jib, "y-3", "Chain Hook", "+y");
            if (hook >= 0 && ConstructBuild.PlaceNew(w, ConstructParts.Need("Wrecking Ball"), ConstructWorld.HitchPoint(crane, hook) - new Vector3(0, 3.3f, 0), 0) is { } ball
                && w.CanCouple(crane, hook, ball, 0) == null)
            {
                w.Couple(crane, hook, ball, 0);
                Run(1.0);
                ball.Vel = new Vector3(0, 0, 4f);
                ball.Wake();
                Run(0.45);
                var hp = ConstructWorld.HitchPoint(crane, hook);
                Shot("wrecking_ball", hp + new Vector3(7f, -1.5f, 1f), hp + new Vector3(0, -2f, 0));
            }
        }
        Console.WriteLine("[powershots] done");
    }
}
