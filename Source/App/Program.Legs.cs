using System.Numerics;
using Brutal.OpenGlApi;

namespace CallOfSlop;

/// <summary>The legs smoke-test step (Source/Render/LegAnimator.cs).</summary>
public static unsafe partial class Program
{
    // ---- Smoke step: a stand-in player walks, then sprints, along the "Physics Test" map and up its 0.5 m stair ramp ----
    // Runs after the mesh step. No game session: the player's movement is simulated here with World.Simulate at 60 Hz
    // (exactly as the game does) and drawn through the normal player rig, so the gait, foot IK and pelvis come from the
    // real velocity and the real physics queries. A side-on chase camera; screenshots smoke_legs_walk.bmp (on the stairs),
    // smoke_legs_run.bmp (sprinting on the flat) and smoke_legs_firstperson.bmp (your own legs, looking down), with a
    // "[smoke] legs" line each (speed, gait, feet, cost, fps).

    private const string LegsSmokeMap = "Physics Test";
    private const byte LegsSmokeId = 250;

    private static readonly (string Shot, Vector3 Start, Buttons Buttons, float ShotX, bool FirstPerson)[] LegsSmokeRuns =
    [
        ("legs_walk", new Vector3(-12f, 0, 12f), Buttons.Forward, 1.7f, false),
        ("legs_run", new Vector3(-36f, 0, 12f), Buttons.Forward | Buttons.Sprint, -7f, false),
        ("legs_firstperson", new Vector3(-20f, 0, 12f), Buttons.Forward, -15f, true),
    ];

    private static int _legsSmoke = -1; // run index; -1 = not running
    private static double _legsSmokeStart, _legsSmokeAcc, _legsSmokeShotAt;
    private static int _legsSmokeFrames;
    private static bool _legsSmokeShot;
    private static MoveState _legsSmokeState;
    private static Vector3 _legsSmokePrev;
    private static Player? _legsSmokePlayer;

    private static bool LegsSmokeActive => _legsSmoke >= 0;

    private static void StartLegsSmoke(double now)
    {
        if (Maps.Find(LegsSmokeMap) is not { } map)
        {
            Console.WriteLine($"[smoke] legs: map '{LegsSmokeMap}' not found, step skipped");
            StartSkySmoke(now); // then the sky step (Program.Sky.cs), then on to the maps
            return;
        }
        if (World.Map != map)
            World.SetMap(map);
        _legsSmokePlayer = new Player { Id = LegsSmokeId, Name = "Legs" };
        _legsSmoke = 0;
        BeginLegsRun(now);
    }

    private static void BeginLegsRun(double now)
    {
        var run = LegsSmokeRuns[_legsSmoke];
        var p = _legsSmokePlayer!;
        p.Teleport(run.Start);
        p.RenderYaw = p.Yaw = MathF.PI / 2; // facing +X, towards the stairs
        _legsSmokeState = new MoveState { Position = run.Start, Grounded = true };
        _legsSmokePrev = run.Start;
        _legsSmokeStart = now;
        _legsSmokeAcc = 0;
        _legsSmokeFrames = 0;
        _legsSmokeShot = false;
        LegAnimator.Reset(LegsSmokeId);
        _ = LegAnimator.Stats; // start the cost window
    }

    private static void LegsSmokeTick(double now)
    {
        var run = LegsSmokeRuns[_legsSmoke];
        var p = _legsSmokePlayer!;
        _legsSmokeFrames++;
        _legsSmokeAcc += FrameDt;
        while (_legsSmokeAcc >= World.TickDt)
        {
            _legsSmokeAcc -= World.TickDt;
            _legsSmokePrev = _legsSmokeState.Position;
            World.Simulate(ref _legsSmokeState, new InputCmd { Buttons = run.Buttons, Yaw = p.RenderYaw, SpeedPct = 100 });
        }
        var alpha = (float)(_legsSmokeAcc / World.TickDt);
        p.Sim = _legsSmokeState;
        p.RenderPosition = Vector3.Lerp(_legsSmokePrev, _legsSmokeState.Position, alpha);
        p.RenderVelocity = _legsSmokeState.Velocity;
        p.RenderGrounded = _legsSmokeState.Grounded;
        p.RenderMantling = _legsSmokeState.MantleTicks > 0;
        p.RenderStance = _legsSmokeState.Stance;

        var t = now - _legsSmokeStart;
        if (!_legsSmokeShot && (p.RenderPosition.X >= run.ShotX || t > 5))
        {
            _legsSmokeShot = true;
            _legsSmokeShotAt = now;
            SmokeShot(run.Shot);
            var legs = LegAnimator.Pose;
            var v = _legsSmokeState.Velocity;
            Console.WriteLine($"[smoke] legs {run.Shot["legs_".Length..],-12} at {SmokeVec(p.RenderPosition)}: {new Vector2(v.X, v.Z).Length():0.0} m/s, gait run weight {legs.Run:0.00}, "
                + $"phase {legs.Phase:0.00}, feet reached {legs.Reached[0]}/{legs.Reached[1]}, planted {legs.Planted[0]}/{legs.Planted[1]}, pelvis drop {legs.Drop * 100:0.0} cm, "
                + $"foot IK {legs.GroundIk}; [legs] {LegAnimator.Stats}; {_legsSmokeFrames / Math.Max(t, 1e-6):0} fps, GL error 0x{GlDebug.Error():X}");
            return;
        }
        if (!_legsSmokeShot || now - _legsSmokeShotAt < 0.15)
            return;
        // one frame after the screenshot: next run, or on to the maps
        _legsSmoke++;
        if (_legsSmoke >= LegsSmokeRuns.Length)
        {
            _legsSmoke = -1;
            _legsSmokePlayer = null;
            LegAnimator.Viewer = new Vector3(float.NaN);
            StartSkySmoke(now); // then the sky step (Program.Sky.cs), then on to the maps
            return;
        }
        BeginLegsRun(now);
    }

    /// <summary>Renders the legs smoke view: a side-on chase camera, or the player's own eyes looking down.</summary>
    private static void DrawLegsSmoke()
    {
        var fb = _window.FramebufferSize;
        var map = World.Map;
        if (fb.X <= 0 || fb.Y <= 0 || _legsSmoke < 0 || _legsSmokePlayer is not { } p)
            return;
        var run = LegsSmokeRuns[_legsSmoke];
        Vector3 eye, target;
        if (run.FirstPerson)
        {
            eye = p.RenderPosition + new Vector3(0, World.EyeHeight, 0);
            target = eye + World.Forward(p.RenderYaw, -1.15f);
        }
        else
        {
            target = p.RenderPosition + new Vector3(0.3f, 0.75f, 0);
            eye = p.RenderPosition + new Vector3(-0.6f, 1.25f, -4.4f); // from the -Z side, a little behind
        }
        Gfx.BeginFrame(fb.X, fb.Y, map.Sky);
        Gfx.SetCamera(Matrix4x4.CreateLookAt(eye, target, Vector3.UnitY),
            Matrix4x4.CreatePerspectiveFieldOfView(BaseFov, fb.X / (float)fb.Y, 0.05f, map.FarPlane));
        Gfx.SetFog(map.Sky, map.FogStart, map.FogEnd);
        ScenePresentation.BeginFrame(MeshGfx);
        DrawArena();
        LegAnimator.Viewer = eye;
        LegAnimator.MoverTick = double.NaN;
        if (run.FirstPerson)
            ScenePresentation.DrawLocalLegs(Gfx, p, Brutal.GlfwApi.Glfw.Time);
        else
            DrawPlayer(p);
        Gfx.SetFog(map.Sky, 0, 0);
    }
}
