using System.Numerics;
using Brutal.GlfwApi;

namespace CallOfSlop;

/// <summary>
/// <c>--tool-shots</c>: pictures of every Sandbox tool's held model (Constructs/Tools; Source/Render/ToolRender.cs) on
/// Construct Yard, written next to the log as smoke_tool_&lt;tool&gt;_fp.bmp (first person, with the arms) and _tp.bmp (third
/// person, in the player's hands), _use (a timed edit running: the wrench cranking, the welder's sparks, the linking
/// tool's beam), a part carried with the wrench out and in bare hands, and a swap half-way. An offline Sandbox game; runs
/// under a virtual X server with Mesa's software GL.
/// </summary>
public static unsafe partial class Program
{
    private static bool _toolShots;

    private static void RunToolShots()
    {
        if (Maps.Find("Construct Yard") is not { } map)
        {
            Console.WriteLine("[toolshots] no Construct Yard map");
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
        for (var t = 0.0; t < Game.CountdownSeconds + 0.3; t += World.TickDt)
            g.Update(now += World.TickDt, World.TickDt);
        g.RequestRespawn();
        for (var t = 0.0; t < 1.5; t += World.TickDt)
            g.Update(now += World.TickDt, World.TickDt);
        var me = g.Local!;
        me.ImmuneUntil = 1e9;
        me.Teleport(ConstructWorld.Ground(World.Query, -8, 20));
        g.LookPitch = 0;
        for (var t = 0.0; t < 0.5; t += World.TickDt)
            g.Update(now += World.TickDt, World.TickDt);
        Console.WriteLine($"[toolshots] {ConstructTools.All.Count} tool models, {ConstructTools.Refused.Count} refused{(ConstructTools.Refused.Count > 0 ? ": " + string.Join("; ", ConstructTools.Refused) : "")}");
        var hands = g.SandboxHands.Hands;
        var id = me.Id;

        void Shot(string name, bool firstPerson, double at)
        {
            ToolRender.Hands = g.SandboxHands;
            me.RenderPitch = 0;
            for (var frame = 0; frame < 3 && !_window.ShouldClose; frame++)
            {
                Glfw.PollEvents();
                Glfw.SetTime(at); // (third person animates on the game clock: this tool quits afterwards, the clock is only borrowed)
                var fb = _window.FramebufferSize;
                var p = me.RenderPosition;
                var fwd = World.FlatForward(me.Yaw);
                var right = World.FlatRight(me.Yaw);
                Vector3 eye, target;
                if (firstPerson)
                {
                    eye = p + new Vector3(0, 1.62f, 0);
                    target = eye + fwd * 10 + new Vector3(0, -0.8f, 0);
                }
                else
                {
                    // Off to the player's right and a little ahead, looking at the hands.
                    eye = p + fwd * 1.0f + right * 1.9f + new Vector3(0, 1.6f, 0);
                    target = p + new Vector3(0, 1.15f, 0) + fwd * 0.35f;
                }
                Gfx.BeginFrame(fb.X, fb.Y, map.Sky);
                var view = Matrix4x4.CreateLookAt(eye, target, Vector3.UnitY);
                Gfx.SetCamera(view, Matrix4x4.CreatePerspectiveFieldOfView(firstPerson ? 1.4f : 0.9f, fb.X / (float)Math.Max(fb.Y, 1), 0.05f, map.FarPlane));
                Gfx.SetFog(map.Sky, map.FogStart, map.FogEnd);
                _worldEye = eye;
                _worldView = view;
                ScenePresentation.BeginFrame(MeshGfx);
                DrawArena();
                DrawSandbox(at);
                if (!firstPerson)
                    DrawPlayer(me);
                Gfx.SetFog(map.Sky, 0, 0);
                if (firstPerson)
                    DrawSandboxTool(at, eye, Vector3.Normalize(target - eye));
                if (frame == 2)
                {
                    _shotFull = true;
                    SaveScreenshot($"tool_{name}");
                    Console.WriteLine($"[toolshots] {name}: {ToolRender.BoxCount(_sbTool)} tool boxes in first person; GL error 0x{(uint)Brutal.OpenGlApi.OpenGl.Native.glGetError():x}");
                }
                _window.SwapBuffers();
            }
        }

        // Hold a tool, fully raised (the swap long done), idle or in use.
        void Hold(ToolModel model, ConstructTool tool, bool use, ushort held, double at)
        {
            ToolRender.Reset();
            ToolRender.ShowModel = model;
            hands[id].Tool = tool;
            hands[id].Held = held;
            hands[id].Edit = use ? EditKind.Attach : EditKind.None;
            ToolRender.Shown(ToolRender.ViewSlot, tool, at - 2, out _);
            ToolRender.Shown(me.Id, tool, at - 2, out _);
            if (tool == ConstructTool.Linker && use)
                _lkBody = 1; // (the beam: a link being routed)
        }

        var shotAt = 100.25;
        foreach (var model in ConstructTools.All)
        {
            var tool = model.Tool ?? ConstructTool.Wrench;
            Hold(model, tool, false, ConstructParts.None, shotAt);
            Shot($"{model.Key}_fp", true, shotAt);
            Shot($"{model.Key}_tp", false, shotAt);
            Hold(model, tool, true, ConstructParts.None, shotAt);
            Shot($"{model.Key}_fp_use", true, shotAt);
            Shot($"{model.Key}_tp_use", false, shotAt);
            _lkBody = 0;
        }
        ToolRender.ShowModel = null;
        // A part carried to place: with the wrench out (in the off hand), and in bare hands (both hands).
        var wheel = ConstructParts.Find("Wheel")?.Id ?? 0;
        var block = ConstructParts.Find("Frame Block")?.Id ?? 0;
        Hold(ToolRender.ModelFor(ConstructTool.Wrench), ConstructTool.Wrench, false, wheel, shotAt);
        Shot("held_wrench_fp", true, shotAt);
        Shot("held_wrench_tp", false, shotAt);
        Hold(ToolRender.ModelFor(ConstructTool.Wrench), ConstructTool.None, false, block, shotAt);
        Shot("held_hands_fp", true, shotAt);
        Shot("held_hands_tp", false, shotAt);
        // A swap from the welder to the wrench, the welder half-way down.
        Hold(ToolRender.ModelFor(ConstructTool.Welder), ConstructTool.Welder, false, ConstructParts.None, shotAt);
        ToolRender.ShowModel = null;
        hands[id].Tool = ConstructTool.Wrench;
        ToolRender.Shown(ToolRender.ViewSlot, ConstructTool.Wrench, shotAt - ToolRender.LowerSeconds * 0.5, out _);
        Shot("swap_fp", true, shotAt);
        Console.WriteLine("[toolshots] swap: the welder half-way down, the wrench next");
        hands[id] = SandboxHand.Empty;
        _session = null;
        g.Dispose();
        Console.WriteLine("[toolshots] done");
    }
}
