using System.Numerics;
using Brutal.GlfwApi;

namespace CallOfSlop;

/// <summary>
/// <c>--ragdoll-shots [Map] [--ragdoll-legacy] [--ragdoll-only a,b]</c>: picture strips of ragdoll deaths (Source/Render/Ragdolls.cs),
/// written next to the log as smoke_ragdoll_&lt;scenario&gt;_&lt;frame&gt;.bmp (frames at fixed times after the kill, side
/// on), then the game quits. Each scenario is one body on an open spot of the map (Proto Box by default) killed one way
/// (a rifle, a sniper, a shotgun, a pistol, a headshot, a frag, ...). <c>--ragdoll-legacy</c> uses the tuning of the
/// old ragdolls (hard limits, no relax pull, the old shot push) for before/after pictures. Uses the smoke test's
/// throwaway save copy; runs under a virtual X server with Mesa's software GL.
/// </summary>
public static unsafe partial class Program
{
    private static bool _ragdollShots;

    /// <summary>Seconds after the kill each picture is taken at (the last: once the body sleeps, or 6 s).</summary>
    private static readonly float[] RagdollShotTimes = [0f, 0.1f, 0.2f, 0.35f, 0.5f, 0.75f, 1.1f, 1.6f, 2.4f, -1f];

    /// <summary>The old ragdolls' tuning (before soft limits): hard limits, no relax pull, the old push.</summary>
    public static void RagdollLegacyTuning()
    {
        var v = RagdollTuning.Values;
        v[(int)RT.SoftRange] = 0;
        v[(int)RT.SoftStiffness] = 1;
        v[(int)RT.SoftDamping] = 0;
        v[(int)RT.HardMargin] = 0;
        v[(int)RT.KneeSoft] = -Ragdoll.KneeSlack;
        v[(int)RT.KneeHard] = 0;
        v[(int)RT.NeutralPull] = 0;
        v[(int)RT.GunScale] = 1;
        v[(int)RT.GunMin] = 1.2f;
        v[(int)RT.GunCap] = 6;
        v[(int)RT.BodyShare] = 1;
        v[(int)RT.HitShare] = v[(int)RT.NeighbourShare] = v[(int)RT.HitLift] = 0;
        v[(int)RT.BlastScale] = 1;
    }

    private static WeaponDef ShotGun(WeaponClass c) => Weapons.All.FirstOrDefault(w => w.Class == c && !w.IsLauncher) ?? Weapons.DefaultPrimary;

    /// <summary>The scenarios: a name and the death (body space: the body at the origin facing -Z, the killer in front).</summary>
    private static List<(string Name, RagdollDeath Death)> RagdollShotScenarios(Vector3 at)
    {
        RagdollDeath Shot(WeaponClass c, float dist, bool head = false, float height = 1.35f) => new()
        {
            Feet = at, Yaw = 0, Stance = Stance.Stand, Victim = 7, Killer = 3, KillId = ShotGun(c).Id, Headshot = head,
            Source = at + new Vector3(0, height, -dist), HasSource = true, GunPower = Ragdolls.GunPower(ShotGun(c)),
        };
        return
        [
            ("pistol", Shot(WeaponClass.Pistol, 8)),
            ("rifle", Shot(WeaponClass.AssaultRifle, 12)),
            ("sniper", Shot(WeaponClass.Sniper, 30)),
            ("shotgun", Shot(WeaponClass.Shotgun, 3, height: 1.2f)),
            ("headshot", Shot(WeaponClass.AssaultRifle, 12, head: true, height: 1.6f)),
            ("running", Shot(WeaponClass.AssaultRifle, 12) with { Velocity = new Vector3(0, 0, -6) }),
            ("frag", new RagdollDeath { Feet = at, Victim = 7, Killer = 3, KillId = (byte)(Throwables.KillIdBase + (byte)ThrowableKind.Frag),
                BlastAt = at + new Vector3(0.9f, 0.2f, -0.8f), BlastStrength = Throwables.Get(ThrowableKind.Frag).Knockback, BlastRadius = 6 }),
        ];
    }

    /// <summary>An open, flat spot of the map: the floor under it found by a ray, nothing within 3.5 m around at body height.</summary>
    private static Vector3 RagdollShotSpot(Map map)
    {
        var rng = new Random(11);
        for (var k = 0; k < 400; k++)
        {
            var x = (float)(rng.NextDouble() - 0.5) * map.HalfX * 1.6f;
            var z = (float)(rng.NextDouble() - 0.5) * map.HalfZ * 1.6f;
            var top = new Vector3(x, 60, z);
            var down = World.RaycastWorld(top, -Vector3.UnitY, 120);
            if (down >= 120)
                continue;
            var floor = top - new Vector3(0, down, 0);
            var clear = true;
            for (var a = 0; a < 8 && clear; a++)
            {
                var dir = new Vector3(MathF.Cos(a * MathF.PI / 4), 0, MathF.Sin(a * MathF.PI / 4));
                clear = World.RaycastWorld(floor + new Vector3(0, 0.6f, 0), dir, 3.5f) >= 3.5f && World.RaycastWorld(floor + new Vector3(0, 1.4f, 0), dir, 3.5f) >= 3.5f;
            }
            if (clear && World.RaycastWorld(floor + new Vector3(1.5f, 1, 0), -Vector3.UnitY, 1.2f) is > 0.95f and < 1.05f
                && World.RaycastWorld(floor + new Vector3(-1.5f, 1, 1.5f), -Vector3.UnitY, 1.2f) is > 0.95f and < 1.05f)
                return floor;
        }
        return Vector3.Zero;
    }

    private static void RunRagdollShots(string[] args)
    {
        var name = args.SkipWhile(a => !a.Equals("--ragdoll-shots", StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault(a => !a.StartsWith("--"));
        var map = (name != null ? Maps.Find(name) : null) ?? Maps.Find("Proto Box") ?? Maps.All[0];
        var legacy = args.Any(a => a.Equals("--ragdoll-legacy", StringComparison.OrdinalIgnoreCase));
        var only = args.SkipWhile(a => !a.Equals("--ragdoll-only", StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault()?.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        World.SetMap(map);
        RagdollTuning.Reload();
        if (legacy)
            RagdollLegacyTuning();
        Gibs.Mode = GoreMode.Off; // (a frag at point-blank shouldn't burst the body: these are ragdoll pictures)
        var at = RagdollShotSpot(map);
        Console.WriteLine($"[ragdollshots] {map.Name}, spot {at:0.0}, {(legacy ? "legacy tuning" : "current tuning")}");
        var tag = legacy ? "_old" : "";
        var now = 100.0;
        foreach (var (scenario, death) in RagdollShotScenarios(at))
        {
            if (only != null && !only.Contains(scenario, StringComparer.OrdinalIgnoreCase))
                continue;
            Ragdolls.Clear();
            var body = Ragdolls.SpawnForTest(death, RagdollLook.Soldier, now, new Vector3(0.25f, 0.5f, 0.95f));
            var t = 0f;
            for (var f = 0; f < RagdollShotTimes.Length; f++)
            {
                var want = RagdollShotTimes[f];
                while (want >= 0 ? t < want - 1e-4f : !body.Sim.Asleep && t < 6f)
                {
                    Ragdolls.StepAll();
                    t += Ragdoll.Step;
                }
                Ragdolls.Advance(now + t, 0);
                // Side on: from the body's right, a little above, looking at where it is now.
                var mid = body.Sim.Pelvis;
                var look = new Vector3(mid.X, at.Y + 0.6f, mid.Z);
                var eye = look + new Vector3(3.3f, 0.9f, 0.5f);
                for (var frame = 0; frame < 2 && !_window.ShouldClose; frame++)
                {
                    Glfw.PollEvents();
                    var fb = _window.FramebufferSize;
                    Gfx.BeginFrame(fb.X, fb.Y, map.Sky);
                    Gfx.SetCamera(Matrix4x4.CreateLookAt(eye, look, Vector3.UnitY),
                        Matrix4x4.CreatePerspectiveFieldOfView(0.9f, fb.X / (float)Math.Max(fb.Y, 1), 0.05f, MathF.Max(map.FarPlane, 300)));
                    Gfx.SetFog(map.Sky, map.FogStart, map.FogEnd);
                    DrawArena();
                    DrawRagdollsFallback();
                    Gfx.SetFog(map.Sky, 0, 0);
                    if (frame == 1)
                        SaveScreenshot($"ragdoll_{scenario}{tag}_{f}");
                    _window.SwapBuffers();
                }
            }
            Console.WriteLine($"[ragdollshots] {scenario}{tag}: hips moved {Vector3.Distance(body.Sim.Pelvis with { Y = 0 }, at with { Y = 0 }):0.00} m, "
                              + $"{(body.Sim.Asleep ? $"asleep after {body.Sim.AwakeSteps / 60f:0.0} s" : "awake")}, {body.Sim.RelaxError() * 100:0} cm from the relaxed pose");
        }
        Ragdolls.Clear();
        RagdollTuning.Reload();
        Console.WriteLine("[ragdollshots] done");
    }
}
