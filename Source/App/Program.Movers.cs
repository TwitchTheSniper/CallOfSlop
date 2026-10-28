using System.Numerics;
using Brutal.GlfwApi;
using Brutal.OpenGlApi;

namespace CallOfSlop;

/// <summary>Mover drawing hook and the movers smoke-test step (the "Physics Test" map).</summary>
public static unsafe partial class Program
{
    // ---- Smoke step: ride the slow rotating platform, then the elevator ---------------------------------------
    // No game session: a player state is simulated here with World.Simulate at 60 Hz (exactly as the game does),
    // the camera sits at its eye and turns with the platform. Each ride lasts a few seconds and logs a
    // "[smoke] movers" line per second (position, velocity, the mover under the feet, fps) plus screenshots
    // smoke_movers_*.bmp. Runs before the foliage step.

    private const string MoversSmokeMap = "Physics Test";

    private static readonly (string Mover, Vector3 Offset, float Seconds, float Pitch)[] MoversSmokeRides =
    [
        ("Slow disc", new Vector3(3f, 0, 0), 4f, -0.25f),
        ("Elevator", new Vector3(0, 0, 0.5f), 7f, -0.1f),
    ];

    private static int _moversSmoke = -1; // ride index; -1 = not running
    private static double _moversSmokeStart, _moversSmokeAcc, _moversSmokeNextLog, _moversSmokeWorst;
    private static int _moversSmokeFrames, _moversSmokeShots;
    private static uint _moversSmokeTick;
    private static MoveState _moversSmokeState;
    private static Vector3 _moversSmokePrev;
    private static float _moversSmokeYaw;
    private static double? _moverTickOverride; // the smoke step's clock for drawing movers

    private static bool MoversSmokeActive => _moversSmoke >= 0;

    /// <summary>Draws the movers of the current map at this frame's pose (called from DrawArena).</summary>
    private static void DrawMovers(Map map)
    {
        if (map.Movers.Count == 0)
            return;
        var tick = _moverTickOverride ?? _session?.MoverRenderTick ?? Glfw.Time * World.TickRate; // lobby background: free-running
        MoverRender.Draw(Gfx, map, tick);
    }

    private static void StartMoversSmoke(double now)
    {
        if (Maps.Find(MoversSmokeMap) is not { } map)
        {
            Console.WriteLine($"[smoke] movers: map '{MoversSmokeMap}' not found, step skipped");
            StartFoliageSmoke(now);
            return;
        }
        World.SetMap(map);
        _moversSmoke = 0;
        _moversSmokeShots = 0;
        BeginMoversRide(map, now);
    }

    private static void BeginMoversRide(Map map, double now)
    {
        var (name, offset, _, _) = MoversSmokeRides[_moversSmoke];
        var mover = map.Movers.FirstOrDefault(m => m.Name == name) ?? map.Movers[0];
        _moversSmokeTick = 600; // any tick: poses are a pure function of it
        var pose = mover.PoseAt(_moversSmokeTick);
        var feet = pose.ToWorld(offset) + new Vector3(0, 0.02f, 0);
        _moversSmokeState = new MoveState { Position = feet, Grounded = false };
        _moversSmokePrev = feet;
        _moversSmokeYaw = pose.Yaw + MathF.PI / 2; // facing along the platform's motion
        _moversSmokeStart = now;
        _moversSmokeAcc = 0;
        _moversSmokeNextLog = 0.5;
        _moversSmokeFrames = 0;
        _moversSmokeWorst = 0;
        Console.WriteLine($"[smoke] movers: riding '{mover.Name}' on {map.Name} from {SmokeVec(feet)}");
    }

    private static void MoversSmokeTick(double now)
    {
        var map = World.Map;
        _moversSmokeFrames++;
        _moversSmokeWorst = Math.Max(_moversSmokeWorst, FrameDt);
        _moversSmokeAcc += FrameDt;
        while (_moversSmokeAcc >= World.TickDt)
        {
            _moversSmokeAcc -= World.TickDt;
            _moversSmokeTick++;
            MoverPhysics.Clock = _moversSmokeTick;
            _moversSmokeYaw = World.WrapAngle(_moversSmokeYaw + MoverPhysics.SupportYawDelta(_moversSmokeState, _moversSmokeTick));
            _moversSmokePrev = _moversSmokeState.Position;
            World.Simulate(ref _moversSmokeState, new InputCmd { Tick = _moversSmokeTick, Yaw = _moversSmokeYaw, SpeedPct = 100 });
        }
        _moverTickOverride = _moversSmokeTick - 1 + _moversSmokeAcc / World.TickDt;

        var ride = MoversSmokeRides[_moversSmoke];
        var t = now - _moversSmokeStart;
        if (t >= _moversSmokeNextLog)
        {
            _moversSmokeNextLog += 1.0;
            var s = _moversSmokeState;
            var under = MoverPhysics.SupportMover(s.Position, _moversSmokeTick);
            var v = (s.Position - _moversSmokePrev) / World.TickDt;
            Console.WriteLine($"[smoke] movers {ride.Mover,-9} t {t,4:0.0} s: feet {SmokeVec(s.Position)}, moved {v.Length():0.00} m/s, grounded {s.Grounded}, "
                + $"on {(under >= 0 ? map.Movers[under].Name : "nothing")}, {_moversSmokeFrames / Math.Max(t, 1e-6):0} fps, worst {_moversSmokeWorst * 1000:0.0} ms, "
                + $"{MoverRender.LastBoxes} mover boxes, GL error 0x{GlDebug.Error():X}");
        }
        if (_moversSmokeShots == _moversSmoke * 2 && t > 1.5)
        {
            SmokeShot($"movers_{_moversSmoke}_{(_moversSmoke == 0 ? "disc" : "elevator")}_ride");
            _moversSmokeShots++;
        }
        else if (_moversSmokeShots == _moversSmoke * 2 + 1 && t > ride.Seconds - 0.5)
        {
            SmokeShot($"movers_{_moversSmoke}_{(_moversSmoke == 0 ? "disc" : "elevator")}_end");
            _moversSmokeShots++;
        }
        if (t < ride.Seconds)
            return;
        _moversSmoke++;
        if (_moversSmoke >= MoversSmokeRides.Length)
        {
            _moversSmoke = -1;
            _moverTickOverride = null;
            MoverPhysics.Clock = 0;
            StartFoliageSmoke(now);
            return;
        }
        BeginMoversRide(map, now);
    }

    /// <summary>Renders the movers smoke view: first person from the simulated player.</summary>
    private static void DrawMoversSmoke()
    {
        var fb = _window.FramebufferSize;
        var map = World.Map;
        if (fb.X <= 0 || fb.Y <= 0 || _moversSmoke < 0)
            return;
        var alpha = (float)(_moversSmokeAcc / World.TickDt);
        var eye = Vector3.Lerp(_moversSmokePrev, _moversSmokeState.Position, alpha) + new Vector3(0, World.EyeHeight, 0);
        var fwd = World.Forward(_moversSmokeYaw, MoversSmokeRides[_moversSmoke].Pitch);
        Gfx.BeginFrame(fb.X, fb.Y, map.Sky);
        Gfx.SetCamera(Matrix4x4.CreateLookAt(eye, eye + fwd, Vector3.UnitY),
            Matrix4x4.CreatePerspectiveFieldOfView(BaseFov, fb.X / (float)fb.Y, 0.05f, map.FarPlane));
        Gfx.SetFog(map.Sky, map.FogStart, map.FogEnd);
        DrawArena();
        Gfx.SetFog(map.Sky, 0, 0);
    }

    private static string SmokeVec(Vector3 v) => $"({v.X:0.00}, {v.Y:0.00}, {v.Z:0.00})";
}
