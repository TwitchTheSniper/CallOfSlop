using System.Numerics;
using Brutal.GlfwApi;

namespace CallOfSlop;

/// <summary>
/// <c>--weapon-shots</c>: pictures of the Sandbox construct weapons (docs/sandbox/CONSTRUCTS.md "Construct weapons"),
/// written next to the log as smoke_weapons_&lt;view&gt;.bmp, then the game quits: the yard's guns and targets, the
/// gunner turret firing at the target wall with its gunner in the seat, the hood gun buggy firing, the sentry turned onto
/// a player and firing, the gun bench (a weapon builder gun from parts), the five heavy guns firing side by side, an ammo
/// crate cooking off, and wood burning from the flamethrower. An offline Sandbox game on Construct Yard; runs under a
/// virtual X server with Mesa's software GL.
/// </summary>
public static unsafe partial class Program
{
    private static bool _weaponShots;

    private static void RunWeaponShots()
    {
        if (Maps.Find("Construct Yard") is not { } map)
        {
            Console.WriteLine("[weaponshots] no Construct Yard map");
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
        Run(2.0);
        var me = g.Local!;
        me.ImmuneUntil = 1e9;
        var w = g.Constructs;
        int Part(Construct b, string name, int nth = 0) => Enumerable.Range(0, b.PartCount).Where(i => b.DefOf(i).Name == name).Skip(nth).FirstOrDefault(-1);
        Construct? Named(string part, Func<Construct, bool>? also = null) => w.Bodies.FirstOrDefault(b => Part(b, part) >= 0 && (also == null || also(b)));

        void Shot(string name, Vector3 eye, Vector3 target, bool players = true)
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
                if (players)
                {
                    LegAnimator.Viewer = eye;
                    foreach (var p in g.Players.Values)
                        if (p.Alive)
                            DrawPlayer(p);
                }
                // The tracers and blasts as a match draws them (a plain version: streaks, slugs and fireballs).
                Gfx.SetBlend(true);
                Gfx.SetDepthWrite(false);
                foreach (var t in g.Tracers)
                {
                    var age = (float)(now - t.Born);
                    var d = t.To - t.From;
                    var len = d.Length();
                    if (len < 0.01f || age > (t.Life > 0 ? t.Life : 0.25f))
                        continue;
                    d /= len;
                    var head = t.Speed > 0 ? t.From + d * MathF.Min(len, age * t.Speed) : t.To;
                    var tail = t.Speed > 0 ? head - d * MathF.Min(MathF.Min(len, age * t.Speed), MathF.Max(0.6f, t.Speed * 0.012f)) : t.From;
                    var seg = head - tail;
                    var sl = seg.Length();
                    if (sl < 0.01f)
                        continue;
                    var yaw = MathF.Atan2(d.X, -d.Z);
                    var pitch = MathF.Asin(Math.Clamp(d.Y, -1f, 1f));
                    Gfx.Draw(Matrix4x4.CreateScale(t.Width * (t.Speed > 0 ? 1.6f : 1f), t.Width * (t.Speed > 0 ? 1.6f : 1f), sl) * Matrix4x4.CreateRotationX(pitch) * Matrix4x4.CreateRotationY(-yaw)
                             * Matrix4x4.CreateTranslation((head + tail) * 0.5f), t.Color, 0.9f, mode: 2);
                }
                foreach (var b in g.Blasts)
                {
                    var age = (float)(now - b.Born);
                    var k = Math.Clamp(age / 0.35f, 0f, 1f);
                    var alpha = Math.Clamp(1f - age / 0.6f, 0f, 1f);
                    var size = b.Radius * (0.3f + 0.7f * k) * 1.2f;
                    var tint = b.Tint == default ? new Vector3(1f, 0.55f, 0.15f) : b.Tint;
                    Gfx.Draw(Matrix4x4.CreateScale(size) * Matrix4x4.CreateRotationY(0.6f) * Matrix4x4.CreateTranslation(b.Pos), tint, alpha * 0.55f, mode: 2);
                    Gfx.Draw(Matrix4x4.CreateScale(size * 0.6f) * Matrix4x4.CreateRotationY(-0.3f) * Matrix4x4.CreateTranslation(b.Pos), Vector3.Lerp(tint, Vector3.One, 0.6f), alpha * 0.8f, mode: 2);
                }
                Gfx.SetDepthWrite(true);
                Gfx.SetBlend(false);
                Gfx.SetFog(map.Sky, 0, 0);
                if (frame == 2)
                {
                    SaveScreenshot($"weapons_{name}");
                    Console.WriteLine($"[weaponshots] {name}: {ConstructRender.LastBoxes} construct boxes, {g.Tracers.Count} tracers, {g.Blasts.Count} blasts, {w.ShotsFired} shots so far");
                }
                _window.SwapBuffers();
            }
        }

        var turret = Named("Motor Hinge", b => b.GunCount > 0);
        var wall = Named("Wood Panel");
        var buggy = Named("Mounted MG", b => b.CanDrive);
        var sentry = w.Bodies.FirstOrDefault(b => b.GunCount > 0 && b.Guns[0].Sensor != 255);
        var bench = w.Bodies.FirstOrDefault(b => b.GunCount > 0 && b.DefOf(b.Guns[0].Part).IsReceiverGun);
        var dump = Named("Ammo Crate");
        Console.WriteLine($"[weaponshots] {w.Bodies.Count} constructs; turret {turret != null}, wall {wall != null}, buggy {buggy != null}, sentry {sentry != null}, bench {bench != null}, dump {dump != null}");
        if (turret == null || wall == null || buggy == null || sentry == null || bench == null || dump == null)
            return;
        Shot("yard_guns", new Vector3(-6, 13, -6), new Vector3(-10, 0, -24));

        // The gunner turret: the local player in its seat, looking at the target wall, firing.
        var seat = turret.Guns[0].Seat;
        var feet = ConstructEdits.SeatFeet(turret, seat);
        me.Teleport(ConstructWorld.Ground(World.Query, feet.X, feet.Z + 1.4f));
        Run(0.3);
        g.SandboxSend(CActKind.Use, body: turret.Id, part: (byte)seat);
        Run(0.3);
        var aimAt = ConstructRender.PartCentre(wall, Part(wall, "Panel"));
        for (var k = 0; k < 200; k++)
        {
            var to = aimAt - ConstructWeapons.SeatEye(turret, seat);
            g.LookYaw = MathF.Atan2(to.X, -to.Z);
            g.LookPitch = MathF.Asin(Math.Clamp(to.Y / to.Length(), -1, 1));
            Run(World.TickDt);
        }
        g.SandboxSend(CActKind.SeatKeys, part: 1);
        Run(0.55);
        var tc = turret.WorldCom;
        Shot("gunner_turret", tc + new Vector3(4.5f, 2.6f, 3.5f), (tc + aimAt) * 0.5f);
        Shot("gunner_turret_side", tc + new Vector3(-6.5f, 1.6f, -3f), (tc + aimAt) * 0.5f);
        g.SandboxSend(CActKind.SeatKeys, part: 0);
        g.SandboxSend(CActKind.Stand);
        Run(0.4);

        // The hood gun: its driver's fire key through the input reader.
        var bc = buggy.WorldCom;
        me.Teleport(ConstructWorld.Ground(World.Query, bc.X - 1.6f, bc.Z));
        Run(0.3);
        g.SandboxSend(CActKind.Use, body: buggy.Id, part: (byte)buggy.DriveSeat);
        Run(0.3);
        g.SandboxSend(CActKind.SeatKeys, part: 1);
        Run(0.45);
        bc = buggy.WorldCom;
        Shot("hood_gun", bc + new Vector3(3.2f, 1.8f, -4.5f), bc + new Vector3(0, 0.4f, -3f));
        g.SandboxSend(CActKind.SeatKeys, part: 0);
        g.SandboxSend(CActKind.Stand);
        Run(0.4);

        // The sentry: a second player walks into its range; it turns onto them and fires.
        var victim = g.SelfTestAddHuman();
        g.SelfTestRespawn(victim, now);
        victim.ImmuneUntil = 1e9;
        var sc = ConstructRender.PartCentre(sentry, sentry.Guns[0].Sensor);
        victim.Teleport(ConstructWorld.Ground(World.Query, sc.X - 3.5f, sc.Z + 2.5f));
        me.Teleport(new Vector3(-20, 0, 18));
        uint seq = 0;
        for (var k = 0; k < 260; k++)
        {
            victim.InputQueue.Add(new InputCmd { Seq = ++seq, Yaw = 1.2f });
            Run(World.TickDt);
        }
        Shot("sentry", sc + new Vector3(3.5f, 2.8f, 4.5f), sc + new Vector3(-1.5f, 0.8f, 1f));
        victim.Teleport(new Vector3(-26, 0, 12));
        for (var k = 0; k < 60; k++)
        {
            victim.InputQueue.Add(new InputCmd { Seq = ++seq, Yaw = 1.2f });
            Run(World.TickDt);
        }

        // The gun bench: a weapon builder gun from parts (receiver, barrel, brake, magazine, box, AP rounds, scope), firing at the ammo dump.
        var benchGun = ConstructRender.PartCentre(bench, bench.Guns[0].Part);
        Shot("gun_bench", benchGun + new Vector3(1.6f, 0.9f, 1.2f), benchGun);
        bench.Parts[Part(bench, "Switch")].State = 1;
        var cooked = false;
        for (var k = 0; k < 600 && !cooked; k++)
        {
            Run(World.TickDt);
            cooked = g.ConstructBlastsSeen > 0 && g.Blasts.Count > 0;
        }
        Run(0.12);
        var dc = dump.WorldCom;
        Shot("cook_off", dc + new Vector3(5.5f, 3.2f, 7.5f), dc);
        bench.Parts[Part(bench, "Switch")].State = 0;
        Run(1.0);

        // The heavy guns side by side on foundations, each on a switch, firing north.
        var heavy = new[] { "Mounted MG", "Autocannon", "Cannon", "Rocket Pod", "Flamethrower" };
        var row = new List<Construct>();
        for (var k = 0; k < heavy.Length; k++)
        {
            var bp = ConstructBuild.ParseBlueprint($$"""
                { "name": "Row {{k}}", "steps": [ { "part": "Foundation" }, { "part": "{{heavy[k]}}", "on": 0, "socket": "top11", "by": "bottom" },
                  { "part": "Ammo Crate", "on": 0, "socket": "top33", "by": "bottom" }, { "part": "Switch", "on": 0, "socket": "top30", "by": "-y" } ],
                  "wires": [[3, 1]] }
                """, "row.json");
            if (ConstructBuild.BuildOnGround(w, bp, ConstructWorld.Ground(World.Query, 6 + k * 3.2f, 6), 0, out var why) is { } c)
            {
                c.Parts[c.Guns[0].Part].Rounds = 1000;
                row.Add(c);
            }
            else
                Console.WriteLine($"[weaponshots] {heavy[k]}: {why}");
        }
        // A wooden wall in front of the flamethrower.
        var woodBp = ConstructBuild.ParseBlueprint("""
            { "name": "Woodpile", "steps": [ { "part": "Foundation" }, { "part": "Wood Panel", "on": 0, "socket": "top11", "by": "-z" }, { "part": "Wood Panel", "on": 0, "socket": "top31", "by": "-z" } ] }
            """, "wood.json");
        ConstructBuild.BuildOnGround(w, woodBp, ConstructWorld.Ground(World.Query, 6 + 4 * 3.2f, 6 - 6.2f), 0, out _);
        Run(0.4);
        foreach (var c in row)
            c.Parts[Part(c, "Switch")].State = 1;
        Run(1.35);
        Shot("heavy_guns", new Vector3(12.5f, 3.2f, 13.5f), new Vector3(12.5f, 0.6f, 1f));
        Shot("heavy_guns_front", new Vector3(13f, 2.2f, -4f), new Vector3(12.5f, 0.6f, 6f));
        Run(1.5);
        var fc = row[^1].WorldCom + new Vector3(0, 0, -6f);
        Shot("flames", fc + new Vector3(3.5f, 2.2f, 3.5f), fc);
        foreach (var c in row)
            c.Parts[Part(c, "Switch")].State = 0;
        Run(1.0);
        Shot("burning", fc + new Vector3(3.0f, 1.8f, -3.0f), fc);
        _session = null;
        g.Dispose();
        Console.WriteLine("[weaponshots] done");
    }
}
