// The game project (CallOfSlop.csproj) compiles every .cs under the repo, so the spike's code only exists
// when JOLT_SPIKE is defined (JoltSpike.csproj defines it).
#if JOLT_SPIKE
using Brutal.JoltApi;
using Brutal.Numerics;
using static JoltSpike.Report;

namespace JoltSpike;

/// <summary>Sections a (basic world + ray casts) and b (CharacterVirtual movement).</summary>
public static class TestsWorldAndMovement
{
    const float East = 1.5707964f; // yaw that faces +X

    // ---------------------------------------------------------------- a
    public static void Basic()
    {
        var t0 = Clock.Now;
        var w = new TestWorld();
        var buildMs = Clock.MsSince(t0);
        Metric("world bodies", w.BodyCount.ToString());
        Metric("world build ms (600 boxes + 256^2 terrain + shapes, incl. OptimizeBroadPhase)", F(buildMs, 1));
        if (Phys.ProbeReport != "")
            Metric("triangle fallback", Phys.ProbeReport);
        // Without ray casts, check the terrain by standing on it: flat part and a hill.
        foreach (var (x, z) in new[] { (-30f, 0f), (-90f, 0f) })
        {
            var ground = TestWorld.TerrainHeight(x, z);
            var m = new Mover(w.P, new float3(x, ground + 1, z));
            for (var i = 0; i < 90; i++)
                m.Step(default);
            var fy = m.Ch.GetPosition().Y;
            Check($"player stands on terrain at x={x}", MathF.Abs(fy - ground) < 0.3f, $"feet y={F(fy)}, terrain {F(ground)} ({(w.HeightField ? "HeightFieldShape" : Phys.TriModeNames[Math.Max(0, Phys.TriMode)])})");
        }
        w.SetMovers(0);

        var down = new float3(0, -1, 0);
        w.P.Raycast(new float3(0, 50, 0), down, 100, out _);
        Result("a.rays", Phys.RaysAvailable ? "yes" : "no");
        if (!Phys.RaysAvailable)
        {
            Blocked("Jolt ray casts", "NarrowPhaseQuery/TransformedShape/BroadPhaseQuery ray casts have no native export in Ahwoo.BRUTAL.Jolt 2026.10.2");
            Metric("jolt config", Jolt.GetConfigurationString().ToString16().Trim());
            return;
        }
        float HitY(float x, float z, out uint body) => 50 - w.P.Raycast(new float3(x, 50, z), down, 100, out body);

        var y = HitY(-90, 0, out var b);
        var expect = TestWorld.TerrainHeight(-90, 0);
        Check("ray hits terrain", w.TerrainBodies.Contains(b) && MathF.Abs(y - expect) < 0.15f, $"y={F(y)} expected~{F(expect)} ({(w.HeightField ? "HeightFieldShape" : "triangle-compound fallback")})");
        Result("a.heightfield", w.HeightField ? "yes" : "no");
        y = HitY(0, TestWorld.LaneShapes, out b);
        Check("ray hits cylinder", b == w.CylinderBody && MathF.Abs(y - 3) < 0.01f, $"y={F(y)}");
        y = HitY(6, TestWorld.LaneShapes, out b);
        Check("ray hits convex hull", b == w.HullBody && y > 1.8f && y <= 2.01f, $"apex y={F(y)} (2.0 minus convex-radius rounding)");
        y = HitY(14, TestWorld.LaneShapes, out b);
        Check("ray hits triangle mesh", b == w.MeshBody && MathF.Abs(y - 3) < 0.01f, $"arch top y={F(y)}");
        var through = w.P.Raycast(new float3(14, 1, TestWorld.LaneShapes - 5), new float3(0, 0, 1), 200, out b);
        Check("ray passes under the mesh arch", b != w.MeshBody && through > 10, $"dist={F(through)} body={b}");
        var leg = w.P.Raycast(new float3(15.75f, 1, TestWorld.LaneShapes - 5), new float3(0, 0, 1), 200, out b);
        Check("ray hits the mesh arch leg", b == w.MeshBody && MathF.Abs(leg - 4.5f) < 0.01f, $"dist={F(leg)}");
        y = HitY(TestWorld.MoverStart.X, TestWorld.MoverStart.Z, out b);
        Check("ray hits kinematic platform", b == w.MovingPlatform && MathF.Abs(y - 0.75f) < 0.01f, $"y={F(y)}");
        w.SetMovers(60); // 1 s later the platform is 2 m further along +X
        y = HitY(TestWorld.MoverStart.X + 2, TestWorld.MoverStart.Z, out b);
        Check("moved platform found at its new pose", b == w.MovingPlatform, $"y={F(y)} body={b}");
        Result("a.config", Jolt.GetConfigurationString().ToString16().Trim().Replace(' ', '_'));
        Metric("jolt config", Jolt.GetConfigurationString().ToString16().Trim());
    }

    // ---------------------------------------------------------------- b
    static Mover Spawn(TestWorld w, float3 feet, int settle = 15)
    {
        var m = new Mover(w.P, feet);
        for (var i = 0; i < settle; i++)
            m.Step(default);
        return m;
    }

    static void Run(TestWorld w, Mover m, Btn buttons, float yaw, int ticks, int tick0 = 0, Action<int>? each = null)
    {
        var cmd = new Input { Buttons = buttons, Yaw = yaw };
        for (var i = 0; i < ticks; i++)
        {
            w.SetMovers(tick0 + i);
            m.Step(cmd);
            each?.Invoke(i);
        }
    }

    /// <summary>What the game's movement gives on flat ground with no walls (same blend, same Euler step).</summary>
    static float ExpectedDistance(float speed, int ticks)
    {
        float v = 0, x = 0;
        for (var i = 0; i < ticks; i++)
        {
            v += (speed - v) * 0.20839f;
            x += v * Sim.Dt;
        }
        return x;
    }

    public static void Character()
    {
        var w = new TestWorld();
        w.SetMovers(0);
        var us = new List<double>();

        // Walk and sprint on flat ground.
        var m = Spawn(w, new float3(-30, 0.05f, 0));
        var p0 = m.Ch.GetPosition();
        Run(w, m, Btn.Forward, East, 60);
        var d = m.Ch.GetPosition().X - p0.X;
        var e = ExpectedDistance(Sim.WalkSpeed, 60);
        Check("walk 1 s", MathF.Abs(d - e) < 0.1f && m.Grounded, $"moved {F(d)} m, game formula {F(e)} m, y={F(m.Ch.GetPosition().Y)}");
        p0 = m.Ch.GetPosition();
        Run(w, m, Btn.Forward | Btn.Sprint, East, 60);
        d = m.Ch.GetPosition().X - p0.X;
        Check("sprint 1 s", d > 7.5f && d < 9.1f, $"moved {F(d)} m (9 m/s target)");

        // Stairs (0.4 m), one 0.55 m step, a 0.7 m step that must block.
        m = Spawn(w, new float3(0, 0.05f, TestWorld.LaneStairs));
        Run(w, m, Btn.Forward, East, 100);
        var p = m.Ch.GetPosition();
        Check("climb 0.4 m stairs", MathF.Abs(p.Y - 1.6f) < 0.05f && p.X > 6.5f, $"feet at x={F(p.X)} y={F(p.Y)} (top 1.6)");
        m = Spawn(w, new float3(0, 0.05f, TestWorld.LaneStep55));
        Run(w, m, Btn.Forward, East, 80);
        p = m.Ch.GetPosition();
        Check("step up 0.55 m", MathF.Abs(p.Y - 0.55f) < 0.05f && p.X > 4, $"feet at x={F(p.X)} y={F(p.Y)}");
        m = Spawn(w, new float3(0, 0.05f, TestWorld.LaneStep70));
        Run(w, m, Btn.Forward, East, 80);
        p = m.Ch.GetPosition();
        Check("0.70 m step blocks", p.Y < 0.1f && p.X < 2.7f, $"feet at x={F(p.X)} y={F(p.Y)}");

        // Step-height sweep: which single steps does each step-up method climb? (WalkStairs tries / successes in brackets.)
        var sweep = new List<string>();
        var savedExt = Mover.Ext;
        var savedManual = Mover.ManualExtended;
        var savedOwn = Mover.OwnStep;
        var configs = new (string Name, bool Manual, bool Own, float Up, float MinFwd, float FwdTest)[]
        {
            ("native ExtendedUpdate", false, false, 0.6f, 0.02f, 0.15f),
            ("C# ExtendedUpdate + Jolt WalkStairs", true, false, 0.6f, 0.02f, 0.15f),
            ("WalkStairs min forward 0.3, up 0.6", true, false, 0.6f, 0.3f, 0.15f),
            ("same, up 0.5", true, false, 0.5f, 0.3f, 0.15f),
            ("same, up 0.45", true, false, 0.45f, 0.3f, 0.15f),
            ("same, up 0.4", true, false, 0.4f, 0.3f, 0.15f),
            ("own step up 0.6", true, true, 0.6f, 0.02f, 0.15f),
            ("own step up 0.55", true, true, 0.55f, 0.02f, 0.15f),
        };
        foreach (var cfg in configs)
        {
            Mover.ManualExtended = cfg.Manual;
            Mover.OwnStep = cfg.Own;
            Mover.Ext.WalkStairsStepUp = new float3(0, cfg.Up, 0);
            Mover.Ext.WalkStairsMinStepForward = cfg.MinFwd;
            Mover.Ext.WalkStairsStepForwardTest = cfg.FwdTest;
            Mover.StairCalls = Mover.StairOk = 0;
            var climbed = new List<string>();
            var diag = "";
            foreach (var h in new[] { 0.3f, 0.4f, 0.45f, 0.5f, 0.55f, 0.6f, 0.7f })
            {
                Mover.LastStep = "";
                var sp = new Phys(64);
                sp.AddBox(new float3(0, -0.5f, 0), new float3(20, 0.5f, 20));
                sp.AddBox(new float3(15, h / 2, 0), new float3(12, h / 2, 2)); // a long step from x = 3 to 27
                sp.System.OptimizeBroadPhase();
                var sm = new Mover(sp, new float3(0, 0.05f, 0));
                for (var i = 0; i < 60; i++)
                    sm.Step(new Input { Buttons = Btn.Forward, Yaw = East });
                if (sm.Ch.GetPosition().Y > h - 0.05f && sm.Ch.GetPosition().X > 3.5f)
                    climbed.Add(F(h, 2));
                if (cfg.Own && h > 0.55f && Mover.LastStep != "")
                    diag += $" {F(h, 2)}: {Mover.LastStep};";
            }
            var tries = (cfg.Manual ? $" ({Mover.StairOk}/{Mover.StairCalls})" : "") + diag;
            sweep.Add($"{cfg.Name}: [{string.Join(" ", climbed)}]{tries}");
            Result($"b.sweep.{sweep.Count}", string.Join("_", climbed));
        }
        Mover.Ext = savedExt;
        Mover.ManualExtended = savedManual;
        Mover.OwnStep = savedOwn;
        Metric("step sweep, heights climbed of 0.30-0.70 (tries ok/total)", string.Join("; ", sweep));

        // Ramps: 30 deg walkable (stand still on it, walk up it), 50 deg too steep (slide, can't climb).
        foreach (var (lane, deg) in new[] { (TestWorld.LaneRamp30, 30f), (TestWorld.LaneRamp50, 50f) })
        {
            var a = deg * MathF.PI / 180f;
            var n = new float3(-DMath.Sin(a), DMath.Cos(a), 0);
            var mid = new float3(2 + 6 * DMath.Cos(a), 6 * DMath.Sin(a) - 0.25f, lane) + n * 0.25f;
            m = Spawn(w, mid + new float3(0, 0.15f, 0), settle: 30);
            var start = m.Ch.GetPosition();
            var state = m.Ch.GetGroundState();
            Run(w, m, Btn.None, 0, 120);
            var drift = (m.Ch.GetPosition() - start).Length();
            if (deg < 45)
                Check("stand still on 30 deg ramp", drift < 0.05f && m.Grounded, $"drift {F(drift)} m in 2 s, ground={m.Ch.GetGroundState()}");
            else
                Check("slide down 50 deg ramp", drift > 0.5f && state != CharacterBase.GroundState.OnGround, $"slid {F(drift)} m in 2 s, ground state when placed={state}");
            m = Spawn(w, new float3(0, 0.05f, lane));
            var maxY = 0f;
            Run(w, m, Btn.Forward, East, 120, each: _ => maxY = MathF.Max(maxY, m.Ch.GetPosition().Y));
            if (deg < 45)
                Check("walk up 30 deg ramp", maxY > 3, $"reached y={F(maxY)}");
            else
                Check("can't walk up 50 deg ramp", maxY < 1.0f, $"max y={F(maxY)}");
        }

        // Jump: apex should match the game (6 m/s up, 18 m/s^2 gravity, ~1 m).
        m = Spawn(w, new float3(-20, 0.05f, -2));
        var y0 = m.Ch.GetPosition().Y;
        var apex = 0f;
        Run(w, m, Btn.Jump, 0, 1);
        Run(w, m, Btn.None, 0, 59, each: _ => apex = MathF.Max(apex, m.Ch.GetPosition().Y - y0));
        Check("jump apex", apex > 0.85f && apex < 1.05f && m.Grounded, $"apex {F(apex)} m (game 0.95), landed={m.Grounded}");

        // Crouch under a 1.4 m ceiling, standing up is refused there, prone and back.
        m = Spawn(w, new float3(-3, 0.05f, TestWorld.LaneCeiling));
        Run(w, m, Btn.Forward | Btn.Crouch, East, 110);
        var under = m.Ch.GetPosition();
        Run(w, m, Btn.None, 0, 10);
        var stayed = m.Stance;
        Check("crouch under 1.4 m ceiling, can't stand", under.X > 0.6f && stayed == Stance.Crouch, $"x={F(under.X)} stance after releasing crouch={stayed}");
        var tSwap = Clock.Now;
        var refused = !m.TrySetStance(Stance.Stand);
        us.Add(Clock.UsSince(tSwap));
        Check("stand-up refused under ceiling (SetShape with max penetration 0)", refused, $"refused={refused}");
        Run(w, m, Btn.Forward, -East, 120);
        Check("stands up automatically once clear", m.Stance == Stance.Stand, $"x={F(m.Ch.GetPosition().X)} stance={m.Stance}");
        Run(w, m, Btn.Prone, 0, 10);
        var prone = m.Stance;
        Run(w, m, Btn.None, 0, 10);
        Check("prone and back up", prone == Stance.Prone && m.Stance == Stance.Stand, $"prone={prone} then {m.Stance}");
        tSwap = Clock.Now;
        for (var i = 0; i < 100; i++)
        {
            m.TrySetStance(Stance.Crouch);
            m.TrySetStance(Stance.Stand);
        }
        Metric("stance shape swap us", F(Clock.UsSince(tSwap) / 200, 2));

        // Mantle: a 1.5 m ledge is found and fits; a 3 m wall is not climbable.
        m = Spawn(w, new float3(2.2f, 0.05f, TestWorld.LaneLedge));
        var tProbe = Clock.Now;
        var ledge = m.ProbeLedge(East, out var fits);
        var probeUs = Clock.UsSince(tProbe);
        if (Mover.LedgeProbeAvailable)
            Check("mantle: 1.5 m ledge found and player fits on top", MathF.Abs(ledge - 1.5f) < 0.05f && fits, $"ledge {F(ledge)} m above feet, fits={fits}");
        else
            Blocked("mantle ledge probe", "CastShape / CheckCollision have no native export");
        if (fits)
        {
            var at = m.Ch.GetPosition();
            m.Ch.SetPosition(new float3(at.X + 0.95f + Sim.PlayerRadius, at.Y + ledge + 0.02f, at.Z));
            Run(w, m, Btn.None, 0, 20);
            Check("mantle: standing on the ledge after the climb", MathF.Abs(m.Ch.GetPosition().Y - 1.5f) < 0.05f && m.Grounded, $"y={F(m.Ch.GetPosition().Y)}");
        }
        tProbe = Clock.Now;
        for (var i = 0; i < 100; i++)
            m.ProbeLedge(East, out _);
        Metric("mantle probe us (sphere cast + overlap)", F(Clock.UsSince(tProbe) / 100, 1));
        m = Spawn(w, new float3(2.2f, 0.05f, TestWorld.LaneWall));
        var wall = m.ProbeLedge(East, out _);
        if (Mover.LedgeProbeAvailable)
            Check("mantle: 3 m wall is not a ledge", float.IsNaN(wall), $"result {F(wall)} (first probe took {F(probeUs, 0)} us incl. JIT)");

        // Moving platform (2 m/s along +X) and a spinning one (1 rad/s).
        var tick = 0;
        m = Spawn(w, TestWorld.MoverStart + new float3(0, 0.6f, 0), settle: 0);
        Run(w, m, Btn.None, 0, 20, tick);
        var onStart = m.Ch.GetPosition();
        Run(w, m, Btn.None, 0, 100, tick + 20);
        var carried = m.Ch.GetPosition().X - onStart.X;
        Check("carried by moving platform", MathF.Abs(carried - 2f * 100 / 60) < 0.3f && m.Grounded, $"moved {F(carried)} m in 100 ticks (platform {F(2f * 100 / 60)} m)");
        m = Spawn(w, TestWorld.SpinnerCenter + new float3(2.5f, 0.6f, 0), settle: 0);
        Run(w, m, Btn.None, 0, 15);
        var s0 = m.Ch.GetPosition() - TestWorld.SpinnerCenter;
        Run(w, m, Btn.None, 0, 90, 15);
        var s1 = m.Ch.GetPosition() - TestWorld.SpinnerCenter;
        var r1 = MathF.Sqrt(s1.X * s1.X + s1.Z * s1.Z);
        var turned = MathF.Abs(MathF.Atan2(s1.Z, s1.X) - MathF.Atan2(s0.Z, s0.X));
        if (turned > MathF.PI)
            turned = MathF.Tau - turned;
        Check("rides spinning platform", MathF.Abs(r1 - 2.5f) < 0.35f && MathF.Abs(turned - 1.5f) < 0.3f, $"radius {F(r1)} (2.5), turned {F(turned)} rad (platform 1.5)");
    }
}
#endif
