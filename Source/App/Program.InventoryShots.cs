using System.Numerics;
using Brutal.GlfwApi;

namespace CallOfSlop;

/// <summary>
/// <c>--inventory-shots</c>: pictures of the Sandbox inventory (UiSandboxParts.cs, its tile atlas SandboxTiles.cs) over
/// Construct Yard, written next to the log as smoke_inventory_&lt;view&gt;.bmp, then the game quits: the Parts tab, one
/// category, one section, a search, big tiles, and the Blueprints and Tools tabs. The atlas is made the way the
/// game makes it (a few tiles a frame, from the main loop's pump), and the log says how long it took. An offline Sandbox
/// game; runs under a virtual X server with Mesa's software GL (both GL paths: see docs/render/OPENGL46.md).
/// </summary>
public static unsafe partial class Program
{
    private static bool _inventoryShots;

    private static void RunInventoryShots()
    {
        if (Maps.Find("Construct Yard") is not { } map)
        {
            Console.WriteLine("[inventoryshots] no Construct Yard map");
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
        me.Teleport(ConstructWorld.Ground(World.Query, -12, 22));
        g.Update(now += World.TickDt, World.TickDt);
        var eye = me.Position + new Vector3(0, 1.6f, 0);
        var target = eye + World.FlatForward(me.Yaw) * 10 + new Vector3(0, -1.5f, 0);
        var frames = 0;

        void Frame(bool save, string name)
        {
            Glfw.PollEvents();
            SandboxTiles.Pump(Gfx); // (as the main loop does, before the scene)
            var fb = _window.FramebufferSize;
            Gfx.BeginFrame(fb.X, fb.Y, map.Sky);
            Gfx.SetCamera(Matrix4x4.CreateLookAt(eye, target, Vector3.UnitY),
                Matrix4x4.CreatePerspectiveFieldOfView(1.1f, fb.X / (float)Math.Max(fb.Y, 1), 0.05f, map.FarPlane));
            Gfx.SetFog(map.Sky, map.FogStart, map.FogEnd);
            DrawArena();
            DrawSandbox(now);
            Gfx.SetFog(map.Sky, 0, 0);
            Ui.BeginFrame();
            Ui.DrawHud(g, now, "", _settings);
            Ui.DrawSandboxParts(g, _settings);
            Ui.EndFrame();
            frames++;
            if (save)
            {
                _shotFull = true; // (full size, so the text reads)
                SaveScreenshot($"inventory_{name}");
                Console.WriteLine($"[inventoryshots] {name}: tab {Ui.SandboxInventoryTab}, {Ui.SandboxPartsListed} listed, {Ui.SandboxTilesWithPicture} tiles with pictures and {Ui.SandboxTilesPlain} plain drawn; "
                                  + $"GL error 0x{(uint)Brutal.OpenGlApi.OpenGl.Native.glGetError():x}");
            }
            _window.SwapBuffers();
        }

        void Shot(string name)
        {
            for (var i = 0; i < 3 && !_window.ShouldClose; i++)
                Frame(i == 2, name);
        }

        Ui.SandboxPartsKeepOpen = true;
        Ui.OpenSandboxParts(focusSearch: false);
        Ui.SetSandboxPartsSearch("");
        Ui.SandboxPartsPick(g, ConstructParts.Need("Wheel"));
        Ui.SandboxPartsPick(g, ConstructParts.Need("Frame Block"));
        Ui.SandboxPartsPick(g, ConstructParts.Need("Lamp"));
        // The atlas, made as in the game: the first frame wants it, then a few tiles a frame.
        var clock = System.Diagnostics.Stopwatch.StartNew();
        Frame(false, "");
        var first = frames;
        var worst = 0.0;
        while (!SandboxTiles.Complete && !SandboxTiles.Broken && frames < first + 400 && !_window.ShouldClose)
        {
            var t0 = clock.Elapsed.TotalMilliseconds;
            Frame(false, "");
            worst = Math.Max(worst, clock.Elapsed.TotalMilliseconds - t0);
        }
        Console.WriteLine($"[inventoryshots] atlas: {SandboxTiles.Rendered} tiles of {SandboxTiles.Count} over {frames - first} frames in {SandboxTiles.Pages} page(s), complete {SandboxTiles.Complete}, "
                          + $"broken {SandboxTiles.Broken}; slowest frame {worst:0.0} ms (software GL); GL {RenderCaps.Version}");

        Shot("parts");
        var cat = Ui.SandboxInventoryCatalogue!;
        Ui.SetSandboxInventoryNode(Array.FindIndex(cat.Parts.Groups, x => x.Name == "Wheels and drivetrain"), -1);
        Shot("category");
        var logic = Array.FindIndex(cat.Parts.Groups, x => x.Name == "Logic");
        Ui.SetSandboxInventoryNode(logic, Array.FindIndex(cat.Parts.Sections, x => x.Name == "Gates"));
        Shot("section");
        Ui.SetSandboxInventoryNode(-1, -1);
        Ui.SetSandboxPartsSearch("gate");
        Shot("search");
        Ui.SetSandboxPartsSearch("");
        var keep = Ui.SandboxTileSize;
        Ui.SandboxTileSize = Ui.SandboxTileMax;
        Ui.SetSandboxInventoryNode(Array.FindIndex(cat.Parts.Groups, x => x.Name == "Structure"), -1);
        Shot("big_tiles");
        Ui.SandboxTileSize = keep;
        Ui.SetSandboxInventoryNode(-1, -1);
        Ui.SetSandboxInventoryTab(Ui.SbTab.Blueprints);
        Shot("blueprints");
        Ui.SetSandboxInventoryTab(Ui.SbTab.Tools);
        Shot("tools");
        Ui.SetSandboxInventoryTab(Ui.SbTab.Parts);
        Ui.CloseSandboxParts();
        Ui.SandboxPartsKeepOpen = false;
        Console.WriteLine($"[inventoryshots] done: {frames} frames");
    }
}
