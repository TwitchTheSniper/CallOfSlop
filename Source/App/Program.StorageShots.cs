using System.Numerics;
using Brutal.GlfwApi;

namespace CallOfSlop;

/// <summary>
/// <c>--storage-shots</c>: pictures of Sandbox physical storage (decision 18; docs/sandbox/CONSTRUCTS.md "Physical
/// storage"), written next to the log as smoke_storage_&lt;view&gt;.bmp, then the game quits: a row of storage on Construct
/// Yard (a chest, a parts shelf, a wheel rack, a scrap bin), the chest shut and open with parts in its slots, the ghost
/// outlines of the slots a carried part fits (with the look-at tooltip), the stocked shelf with its piles, the wheel
/// rack, the bin half full and full, and a destroyed chest's contents spilled on the ground. An offline Sandbox game;
/// runs under a virtual X server with Mesa's software GL.
/// </summary>
public static unsafe partial class Program
{
    private static bool _storageShots;

    private static void RunStorageShots()
    {
        if (Maps.Find("Construct Yard") is not { } map)
        {
            Console.WriteLine("[storageshots] no Construct Yard map");
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
        var w = g.Constructs;

        void Shot(string name, Vector3 eye, Vector3 target, bool ghosts = false, bool hud = false, bool players = false, int frames = 3)
        {
            for (var frame = 0; frame < frames && !_window.ShouldClose; frame++)
            {
                Glfw.PollEvents();
                SandboxTiles.Pump(Gfx); // (the backpack strip's tiny tiles, as the main loop makes them)
                var fb = _window.FramebufferSize;
                Gfx.BeginFrame(fb.X, fb.Y, map.Sky);
                Gfx.SetCamera(Matrix4x4.CreateLookAt(eye, target, Vector3.UnitY),
                    Matrix4x4.CreatePerspectiveFieldOfView(1.1f, fb.X / (float)Math.Max(fb.Y, 1), 0.05f, map.FarPlane));
                Gfx.SetFog(map.Sky, map.FogStart, map.FogEnd);
                DrawArena();
                DrawSandbox(now);
                if (players)
                {
                    LegAnimator.Viewer = eye;
                    foreach (var p in g.Players.Values)
                        if (p.Alive)
                            DrawPlayer(p);
                }
                if (ghosts)
                {
                    Gfx.SetBlend(true);
                    Gfx.SetDepthWrite(false);
                    ConstructRender.DrawGlass(Gfx, Gfx.CameraPosition);
                    ConstructRender.DrawGhosts(Gfx, now);
                    Gfx.SetDepthWrite(true);
                    Gfx.SetBlend(false);
                }
                Gfx.SetFog(map.Sky, 0, 0);
                if (hud)
                {
                    Ui.BeginFrame();
                    Ui.DrawHud(g, now, "", _settings);
                    Ui.EndFrame();
                }
                if (frame == frames - 1)
                {
                    _shotFull = hud;
                    SaveScreenshot($"storage_{name}");
                    Console.WriteLine($"[storageshots] {name}: {ConstructRender.LastBoxes} construct boxes, {ConstructRender.LastStored} stored parts drawn; look \"{g.SandboxLookText}\"; GL error 0x{(uint)Brutal.OpenGlApi.OpenGl.Native.glGetError():x}");
                }
                _window.SwapBuffers();
            }
        }

        static int Part(Construct b, string name) => Enumerable.Range(0, b.PartCount).Where(i => b.DefOf(i).Name == name).DefaultIfEmpty(-1).First();
        var x0 = -19f;
        var z0 = 12f;
        Vector3 Ground(float dx) => ConstructWorld.Ground(World.Query, x0 + dx, z0);
        var chest = ConstructBuild.BuildOnGround(w, ConstructBuild.FindBlueprint("Chest")!, Ground(0), 0, out _)!;
        var shelf = ConstructBuild.PlaceNew(w, ConstructParts.Need("Parts Shelf"), Ground(2.4f), 0)!;
        var rack = ConstructBuild.PlaceNew(w, ConstructParts.Need("Wheel Rack"), Ground(4.8f), 0)!;
        var bin = ConstructBuild.PlaceNew(w, ConstructParts.Need("Scrap Bin"), Ground(-2.2f), 0)!;
        me.Teleport(ConstructWorld.Ground(World.Query, x0 + 1, z0 + 4) + new Vector3(0, 0.05f, 0));
        Run(1.5);
        var box = Part(chest, "Chest");
        var lid = Part(chest, "Chest Lid");
        Console.WriteLine($"[storageshots] chest {chest.Id} ({chest.PartCount} parts), shelf {shelf.Id}, rack {rack.Id}, bin {bin.Id}; {w.LiveStores} stores");
        var eye = new Vector3(x0 + 1.2f, Ground(1.2f).Y + 2.6f, z0 + 4.8f);
        Shot("row_empty", eye, Ground(1.2f) + new Vector3(0, 0.5f, 0));

        // The chest shut, then opened, with a suspension and a battery set in.
        var cc = chest.WorldCom;
        Shot("chest_shut", cc + new Vector3(0.4f, 1.3f, 1.6f), cc);
        g.SandboxHands.Use(w, me.Id, cc + new Vector3(0, 1.2f, 1.2f), chest.Id, lid);
        Run(1.2);
        w.SetIn(chest, box, 0, ConstructParts.Need("Suspension"));
        w.SetIn(chest, box, 1, ConstructParts.Need("Battery"));
        Run(0.2);
        Shot("chest_open", cc + new Vector3(0.3f, 1.5f, 1.3f), cc);

        // Carrying a lamp to the shelf: the slots it fits light up (the chosen one green, with the lamp where it would sit).
        var shelfDef = shelf.DefOf(0);
        var lamp = ConstructParts.Need("Lamp");
        w.SetIn(shelf, 0, 0, ConstructParts.Need("Engine"));
        for (var k = 0; k < 4; k++)
            w.SetIn(shelf, 0, 3, ConstructParts.Need("Button"));
        Run(0.2);
        var sc = shelf.WorldCom;
        var st = w.StoreOf(shelf, 0)!;
        ref var hand = ref g.SandboxHands.Hand(me.Id);
        hand.Held = lamp.Id;
        ConstructRender.ClearAim();
        ConstructRender.SlotBody = shelf;
        ConstructRender.SlotPart = 0;
        ConstructRender.SlotDef = lamp;
        for (var k = 0; k < shelfDef.Slots.Length; k++)
            if (ConstructStorage.Check(shelfDef, st, k, lamp, out var rot) == SlotCheck.Ok)
            {
                ConstructRender.SlotMask |= 1 << k;
                if (k == 2)
                {
                    ConstructRender.SlotSelected = k;
                    ConstructRender.SlotRot = rot;
                }
            }
        var shelfEye = sc + new Vector3(0.2f, 0.9f, 2.0f);
        var lookHit = new ConstructHit { Body = shelf, Part = 0, Distance = 2f };
        g.SandboxLookText = StorageLook(g, me, lookHit, st, true, lamp);
        Shot("shelf_ghosts", shelfEye, sc + new Vector3(0, 0.1f, 0), ghosts: true, hud: true);
        ConstructRender.ClearAim();
        hand.Held = ConstructParts.None;
        for (var k = 0; k < 6; k++)
            w.SetIn(shelf, 0, 2, lamp);
        w.SetIn(shelf, 0, 1, ConstructParts.Need("Transmission"));
        w.SetIn(shelf, 0, 4, ConstructParts.Need("Ammo Box"));
        Run(0.2);
        lookHit.SlotPlus1 = 3;
        g.SandboxLookText = StorageLook(g, me, lookHit, st, true, null);
        Shot("shelf_stocked", shelfEye, sc + new Vector3(0, 0.1f, 0), hud: true);

        // The wheel rack with two wheels, and the bin half full and full.
        w.SetIn(rack, 0, 0, ConstructParts.Need("Wheel"));
        w.SetIn(rack, 0, 2, ConstructParts.Need("Wheel"));
        Run(0.2);
        var rc = rack.WorldCom;
        Shot("wheel_rack", rc + new Vector3(0.6f, 1.2f, 2.0f), rc);
        w.Deposit(bin, 0, 230);
        Run(0.2);
        var bc = bin.WorldCom;
        g.SandboxInventory(me.Id).AddScrap(40);
        g.SandboxLookText = StorageLook(g, me, new ConstructHit { Body = bin, Part = 0, Distance = 2f }, w.StoreOf(bin, 0)!, true, null);
        Shot("bin_half", bc + new Vector3(0.4f, 1.6f, 1.3f), bc, hud: true);
        w.Deposit(bin, 0, 500);
        Run(0.2);
        Shot("bin_full", bc + new Vector3(0.4f, 1.6f, 1.3f), bc);
        g.SandboxLookText = "";

        // The row stocked, then the chest destroyed: what it held lies spilled on the ground.
        Shot("row_stocked", eye, Ground(1.2f) + new Vector3(0, 0.5f, 0));
        w.DamagePart(chest, box, 100000, out _);
        Run(2.0);
        Shot("chest_spilled", cc + new Vector3(0.5f, 2.2f, 2.4f), cc);
        // The backpack: worn with a lamp, a battery, a suspension and a gun barrel in it, from behind and the side; then
        // the first-person strip on the HUD.
        ToolRender.Hands = g.SandboxHands;
        me.Teleport(ConstructWorld.Ground(World.Query, x0 + 1, z0 + 6) + new Vector3(0, 0.05f, 0));
        foreach (var name in new[] { "Lamp", "Battery", "Suspension", "Gun Barrel", "Button" })
        {
            hand.Held = ConstructParts.Need(name).Id;
            g.SandboxHands.Stow(me.Id);
        }
        hand.Held = ConstructParts.None;
        Run(0.5);
        var back = me.Position + new Vector3(0, 1.2f, 0);
        var fwd = World.FlatForward(me.Yaw);
        Shot("backpack_behind", back - fwd * 2.4f + new Vector3(0.6f, 0.5f, 0), back, players: true);
        Shot("backpack_side", back + new Vector3(-fwd.Z, 0, fwd.X) * 2.4f - fwd * 1.0f + new Vector3(0, 0.3f, 0), back, players: true);
        g.SandboxLookText = "";
        SandboxTiles.Want();
        Shot("backpack_hud", me.Eye, me.Eye + fwd * 5f - new Vector3(0, 1f, 0), hud: true, frames: 40);
        ToolRender.Hands = null;
        Console.WriteLine($"[storageshots] done: {w.Bodies.Count} constructs, {w.LiveStores} stores");
    }
}
