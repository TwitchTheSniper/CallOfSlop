using System.Numerics;
using Brutal.GlfwApi;

namespace CallOfSlop;

/// <summary>
/// <c>--placement-shots</c>: pictures of free placement (docs/sandbox/CONSTRUCTS.md "New constructs"), written next to the
/// log as smoke_placement_&lt;view&gt;.bmp, then the game quits: the Construct Core's ghost on open ground (green), a Frame
/// Beam's ghost turned on its side, a ghost over a construct (red, with the reason on the HUD), the core built on, a second
/// construct set down beside it, and the two joined into one. An offline Sandbox game on Construct Yard; runs under a
/// virtual X server with Mesa's software GL.
/// </summary>
public static unsafe partial class Program
{
    private static bool _placementShots;

    private static void RunPlacementShots()
    {
        if (Maps.Find("Construct Yard") is not { } map)
        {
            Console.WriteLine("[placementshots] no Construct Yard map");
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
        var w = g.Constructs;
        // Open level ground: the yard's south-east, clear of its constructs.
        var spot = ConstructWorld.Ground(World.Query, 12, 22);
        foreach (var (x, z) in new[] { (12f, 22f), (20f, 20f), (8f, 24f), (22f, -10f) })
        {
            var p = ConstructWorld.Ground(World.Query, x, z);
            if (w.Bodies.All(b => Vector3.Distance(b.WorldCom with { Y = 0 }, p with { Y = 0 }) > 6 + b.Radius))
            {
                spot = p;
                break;
            }
        }
        me.Teleport(spot + new Vector3(0, 0.05f, 2.5f));
        me.Yaw = 0;
        Run(0.3);

        void Shot(string name, Vector3 eye, Vector3 target, bool hud = false)
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
                Gfx.SetBlend(true);
                Gfx.SetDepthWrite(false);
                ConstructRender.DrawGhosts(Gfx, now);
                Gfx.SetDepthWrite(true);
                Gfx.SetBlend(false);
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
                    SaveScreenshot($"placement_{name}");
                    Console.WriteLine($"[placementshots] {name}: {ConstructRender.LastBoxes} construct boxes");
                }
                _window.SwapBuffers();
            }
        }

        var feet = new[] { me.Position };
        // The ghost of a held part on the surface (as the client shows it: green when it fits, red with the reason).
        void Ghost(ConstructPartDef def, Vector3 at, byte turn, bool grid)
        {
            ConstructRender.ClearAim();
            ConstructRender.GhostDef = def;
            var why = ConstructPlace.Pose(World.Query, def, at, Vector3.UnitY, 0.35f, turn, grid, out var fp) ?? ConstructPlace.Fits(w, World.Query, def, fp, feet);
            ConstructRender.PlaceAt = fp.Pos;
            ConstructRender.PlaceRot = fp.Rot;
            ConstructRender.PlaceTurn = fp.Turn;
            ConstructRender.PlaceValid = why == null;
            g.SandboxLookText = why != null ? $"Can't set the {def.Name} down here: {why}" : $"New construct: hold fire to set down the {def.Name} (free{(grid ? ", on the grid" : "")}; R turns it)";
            Console.WriteLine($"[placementshots] ghost {def.Name} turn {turn}: {(why ?? "fits")}");
        }
        var core = ConstructParts.Need("Construct Core");
        var block = ConstructParts.Need("Frame Block");
        var beam = ConstructParts.Need("Frame Beam");
        var view = spot + new Vector3(-2.2f, 2.0f, 3.6f);
        g.SandboxSend(CActKind.Tool, part: (byte)ConstructTool.Welder);
        g.SandboxTakePart(core.Id);
        Ghost(core, spot, 0, true);
        Shot("core_ghost", view, spot, hud: true);
        Ghost(beam, spot, ConstructPlace.Turns[5], false);
        Shot("beam_turned", view, spot);

        // Set the core down, build two blocks onto it.
        g.SandboxSend(CActKind.PlaceNew, at: spot, yaw: 0, normal: Vector3.UnitY, variant: 0, flags: ConstructPlace.FlagGrid);
        Run(core.AttachSeconds + 0.3);
        var a = w.Bodies[^1];
        Console.WriteLine($"[placementshots] core: construct {a.Id}, {a.PartCount} part, notice \"{g.SandboxNotice}\"");
        foreach (var face in new[] { "+x", "+y" })
        {
            g.SandboxTakePart(block.Id);
            var ghosts = new ConstructGhost[ConstructBuild.MaxGhosts];
            var n = ConstructBuild.Ghosts(a, block, ghosts, Vector3.Zero, 3f);
            for (var k = 0; k < n; k++)
                if (ghosts[k].Part == 0 && core.Sockets[ghosts[k].Socket].Name == face)
                    g.SandboxSend(CActKind.Attach, body: a.Id, part: 0, socket: (byte)ghosts[k].Socket, variant: (byte)ghosts[k].Variant);
            Run(block.AttachSeconds + 0.3);
        }
        Run(1.0);
        // A ghost over it: red, the reason on the HUD.
        g.SandboxTakePart(block.Id);
        Ghost(block, spot + new Vector3(-0.2f, 0, 0.3f), 0, false);
        Shot("blocked_ghost", view, spot, hud: true);
        ConstructRender.ClearAim();
        Shot("built", view, spot + new Vector3(0.4f, 0.3f, 0));

        // A second construct beside the first's +x block, then the two joined.
        var bi = 1;
        var faceAt = Vector3.Zero;
        for (var s = 0; s < block.Sockets.Length; s++)
        {
            a.SocketAt(bi, s, out var at, out var d);
            if (d == 0)
                faceAt = at;
        }
        var beside = a.ToWorld(faceAt) + new Vector3(0.25f, 0, 0);
        g.SandboxSend(CActKind.PlaceNew, at: beside with { Y = spot.Y }, yaw: 0, normal: Vector3.UnitY, variant: 0, flags: 0);
        Run(block.AttachSeconds + 0.3);
        var b = w.Bodies[^1];
        Run(1.0);
        Console.WriteLine($"[placementshots] beside: construct {b.Id} ({b.PartCount} part), touches {ConstructPlace.FindJoin(w, a, bi, out _)}; notice \"{g.SandboxNotice}\"");
        g.SandboxLookText = $"Frame Block: touches construct {b.Id} - F to weld the two into one";
        ConstructRender.AimBody = a;
        ConstructRender.AimPart = bi;
        Shot("join_offer", view, spot + new Vector3(0.6f, 0.3f, 0), hud: true);
        g.SandboxSend(CActKind.Join, body: a.Id, part: (byte)bi);
        Run(0.5);
        g.SandboxLookText = "";
        ConstructRender.ClearAim();
        Console.WriteLine($"[placementshots] joined: construct {a.Id} has {a.PartCount} parts, construct {b.Id} gone {w.Get(b.Id) == null}; notice \"{g.SandboxNotice}\"");
        // Pushed: the joined construct moves as one.
        a.Vel = new Vector3(2.5f, 0, 0);
        a.Wake();
        Run(0.8);
        Shot("joined_pushed", view + new Vector3(1.2f, 0, 0), a.WorldCom);
        _session = null;
        g.Dispose();
        Console.WriteLine("[placementshots] done");
    }
}
