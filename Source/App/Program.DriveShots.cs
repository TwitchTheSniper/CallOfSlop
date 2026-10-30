using System.Numerics;
using Brutal.GlfwApi;

namespace CallOfSlop;

/// <summary>
/// <c>--drive-shots</c>: pictures of the drivetrain families (docs/sandbox/CONSTRUCTS.md "Drivetrain parts"), written next
/// to the log as smoke_drive_&lt;view&gt;.bmp, then the game quits: the example vehicles in a row with a player beside each
/// for scale, the 4x4 truck's front axle (CV joints into the steering pivots) and its transfer case, the crawler's track,
/// the racer, the electric cart, the drivetrain part galleries (wheels, engines, gearboxes, transfer parts), and the
/// driving HUD with the rpm and torque. An offline Sandbox game on Construct Yard; runs under a virtual X server.
/// </summary>
public static unsafe partial class Program
{
    private static bool _driveShots;

    private static void RunDriveShots()
    {
        if (Maps.Find("Construct Yard") is not { } map)
        {
            Console.WriteLine("[driveshots] no Construct Yard map");
            return;
        }
        World.SetMap(map);
        var g = new Game(NetPeer.Offline(), AudioSystem, "Shots", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 1, mapId: map.Id)
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
        // A bot stands beside each vehicle for scale (1.8 m).
        var stand = g.Players.Values.FirstOrDefault(p => p.Id != me.Id) ?? me;
        stand.ImmuneUntil = 1e9;
        Console.WriteLine($"[driveshots] scale figure: player {stand.Id} ({(stand == me ? "me" : "a bot")}), alive {stand.Alive}, {g.Players.Count} players");
        var w = g.Constructs;
        // Clear the yard's own vehicles out of the way of the row.
        foreach (var b in w.Bodies.ToList())
            if (b.WheelCount > 0)
                w.Remove(b);

        void Shot(string name, Vector3 eye, Vector3 target, bool hud = false, float fov = 1.0f)
        {
            for (var frame = 0; frame < 3 && !_window.ShouldClose; frame++)
            {
                Glfw.PollEvents();
                var fb = _window.FramebufferSize;
                Gfx.BeginFrame(fb.X, fb.Y, map.Sky);
                Gfx.SetCamera(Matrix4x4.CreateLookAt(eye, target, Vector3.UnitY),
                    Matrix4x4.CreatePerspectiveFieldOfView(fov, fb.X / (float)Math.Max(fb.Y, 1), 0.05f, map.FarPlane));
                Gfx.SetFog(map.Sky, map.FogStart, map.FogEnd);
                DrawArena();
                DrawSandbox(now);
                if (stand != me)
                    DrawPlayer(stand); // (the figure for scale)
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
                    SaveScreenshot($"drive_{name}");
                    Console.WriteLine($"[driveshots] {name}: {ConstructRender.LastBoxes} construct boxes; GL error 0x{(uint)Brutal.OpenGlApi.OpenGl.Native.glGetError():x}");
                }
                _window.SwapBuffers();
            }
        }

        // The examples in a row, facing +X, 6 m apart along Z, with the player standing beside each.
        string[] names = ["Buggy", "4x4 Truck", "Tracked Crawler", "Racer", "Electric Cart"];
        var cars = new List<Construct>();
        for (var i = 0; i < names.Length; i++)
            if (ConstructBuild.FindBlueprint(names[i]) is { } bp
                && ConstructBuild.BuildOnGround(w, bp, ConstructWorld.Ground(World.Query, 2, -2 + i * 6f), MathF.PI / 2, out var why) is { } c)
                cars.Add(c);
            else
                Console.WriteLine($"[driveshots] {names[i]} doesn't build");
        Run(1.0);
        var rowMid = cars.Aggregate(Vector3.Zero, (a, c) => a + c.WorldCom) / Math.Max(1, cars.Count);
        me.Teleport(ConstructWorld.Ground(World.Query, rowMid.X - 12, rowMid.Z - 12));
        stand.Teleport(ConstructWorld.Ground(World.Query, rowMid.X - 4.5f, rowMid.Z - 3));
        Run(0.05);
        Shot("row", rowMid + new Vector3(-16, 9, -14), rowMid, fov: 0.9f);
        Shot("row_side", rowMid + new Vector3(0, 3, -22), rowMid + new Vector3(0, 0.5f, 0), fov: 1.1f);
        foreach (var c in cars)
        {
            ConstructDrive.Size(c, out var len, out var wid, out _);
            var side = Vector3.Transform(Vector3.UnitX, c.Rot); // (across the vehicle)
            stand.Teleport(ConstructWorld.Ground(World.Query, c.WorldCom.X, c.WorldCom.Z) - side * (wid * 0.5f + 0.8f));
            stand.Yaw = 0;
            Run(0.05);
            var name = names[cars.IndexOf(c)].ToLowerInvariant().Replace(' ', '_').Replace("4x4_", "");
            var fwd = Vector3.Transform(c.SeatForward(), c.Rot);
            Shot($"{name}_scale", c.WorldCom + fwd * (len * 0.75f + 2.5f) - side * (wid * 0.5f + 2.6f) + new Vector3(0, 1.6f, 0), c.WorldCom - side * 0.6f, fov: 1.15f);
        }
        // Close-ups: the truck's front axle and transfer case, the crawler's track.
        if (cars.FirstOrDefault(c => c.DefOf(c.Root).Role == PartRole.Seat && Enumerable.Range(0, c.PartCount).Any(i => c.DefOf(i).Drive == DriveKind.TransferCase && c.DefOf(i).Role == PartRole.Transfer)) is { } truck)
        {
            var cv = Enumerable.Range(0, truck.PartCount).First(i => truck.DefOf(i).Name == "CV Joint");
            var at = ConstructRender.PartCentre(truck, cv);
            Shot("truck_front_axle", at + new Vector3(1.2f, 1.0f, -1.6f), at, fov: 0.9f);
            var tc = Enumerable.Range(0, truck.PartCount).First(i => truck.DefOf(i).Name == "Transfer Case");
            var tcAt = ConstructRender.PartCentre(truck, tc);
            Shot("truck_transfer_case", tcAt + new Vector3(-1.5f, 2.2f, -2.0f), tcAt, fov: 0.9f);
        }
        if (cars.FirstOrDefault(c => Enumerable.Range(0, c.PartCount).Any(i => c.DefOf(i).TrackRays > 0)) is { } crawler)
        {
            var tr = Enumerable.Range(0, crawler.PartCount).First(i => crawler.DefOf(i).TrackRays > 0);
            var at = ConstructRender.PartCentre(crawler, tr);
            Shot("crawler_track", at + new Vector3(-2.5f, 1.5f, -3.5f), at, fov: 0.9f);
        }
        // Galleries: every wheel, engine, gearbox and transfer part set down in rows.
        foreach (var (shot, roles) in new[] { ("wheels", new[] { PartRole.Wheel, PartRole.Suspension }), ("engines", new[] { PartRole.Engine, PartRole.Transmission, PartRole.Differential }), ("transfer", new[] { PartRole.Transfer }) })
        {
            var placed = new List<Construct>();
            var x = 0f;
            foreach (var d in ConstructParts.All.Where(p => roles.Contains(p.Role)))
            {
                var size = d.BoundsMax - d.BoundsMin;
                if (ConstructBuild.PlaceNew(w, d, ConstructWorld.Ground(World.Query, 14 + x + size.X * 0.5f, -14), 0) is { } p)
                {
                    p.Anchored = true;
                    placed.Add(p);
                }
                x += size.X + 0.5f;
            }
            Run(0.1);
            var mid = new Vector3(14 + x * 0.5f, 0.5f, -14);
            me.Teleport(ConstructWorld.Ground(World.Query, 13, -12.5f));
            Shot($"gallery_{shot}", mid + new Vector3(0, 3.5f + x * 0.12f, 4.5f + x * 0.35f), mid, fov: 1.0f);
            foreach (var p in placed)
                w.Remove(p);
        }
        // The driving HUD: the truck driven, with its rpm, torque and gear.
        if (cars.Count > 1)
        {
            var truck2 = cars[1];
            me.Teleport(ConstructWorld.Ground(World.Query, truck2.WorldCom.X - 1.5f, truck2.WorldCom.Z - 2));
            Run(0.2);
            g.SandboxSend(CActKind.Use, body: truck2.Id, part: (byte)truck2.DriveSeat);
            Run(0.2);
            g.HeldButtons = Buttons.Forward;
            Run(1.5);
            var wc = truck2.WorldCom;
            var f = Vector3.Transform(truck2.SeatForward(), truck2.Rot);
            Shot("driving_hud", wc - f * 9f + new Vector3(0, 4f, 0), wc + f * 3f, hud: true);
            g.HeldButtons = Buttons.Jump;
            Run(0.5);
            Console.WriteLine($"[driveshots] the truck at {truck2.Rpm:0} rpm, gear {truck2.Gear + 1}, {truck2.Vel.Length():0.0} m/s, note {truck2.Note}");
        }
        Console.WriteLine("[driveshots] done");
    }
}
