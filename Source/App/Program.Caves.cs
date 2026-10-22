using System.Numerics;
using Brutal.OpenGlApi;

namespace CallOfSlop;

public static unsafe partial class Program
{
    // ---- Smoke step: the "Cave Test" map (terrain holes and caves), two views ---------------------------------------
    // Runs after the sky step. Each view settles for 0.4 s, measures fps for 1 s, logs one line (fps, terrain draw calls and
    // triangles, cave cells) and saves smoke_cave_<view>.bmp.

    private const string CaveSmokeMap = "Cave Test";

    private static readonly string[] CaveSmokeViews = ["entrance", "chamber"];

    private static int _caveSmoke = -1; // index into CaveSmokeViews; -1 = not running
    private static double _caveSmokeStart, _caveSmokeMeasureStart, _caveSmokeWorst;
    private static int _caveSmokeFrames;
    private static bool _caveSmokeMeasuring, _caveSmokeAdvance;

    private static bool CaveSmokeActive => _caveSmoke >= 0;

    private static void StartCaveSmoke(double now)
    {
        if (Maps.Find(CaveSmokeMap) is not { } map || map.Terrain?.Voids == null)
        {
            Console.WriteLine($"[smoke] caves: map '{CaveSmokeMap}' with holes not found, step skipped");
            SmokeNextMap(now);
            return;
        }
        if (World.Map != map)
            World.SetMap(map);
        _caveSmoke = 0;
        BeginCaveSmokeView(now);
    }

    private static void BeginCaveSmokeView(double now)
    {
        _caveSmokeStart = now;
        _caveSmokeMeasuring = false;
        _caveSmokeAdvance = false;
    }

    private static void CaveSmokeTick(double now)
    {
        if (_caveSmokeAdvance)
        {
            _caveSmoke++;
            if (_caveSmoke >= CaveSmokeViews.Length)
            {
                _caveSmoke = -1;
                SmokeNextMap(now);
                return;
            }
            BeginCaveSmokeView(now);
            return;
        }
        var t = now - _caveSmokeStart;
        if (!_caveSmokeMeasuring)
        {
            if (t > 0.4)
            {
                _caveSmokeMeasuring = true;
                _caveSmokeFrames = 0;
                _caveSmokeWorst = 0;
                _caveSmokeMeasureStart = now;
            }
            return;
        }
        _caveSmokeFrames++;
        _caveSmokeWorst = Math.Max(_caveSmokeWorst, FrameDt);
        if (t < 1.4)
            return;
        var name = CaveSmokeViews[_caveSmoke];
        var secs = Math.Max(now - _caveSmokeMeasureStart, 1e-6);
        var (calls, chunks, tris) = TerrainGfx.LastFrame;
        var v = World.Map.Terrain?.Voids;
        Console.WriteLine($"[smoke] cave {name,-8}: {_caveSmokeFrames / secs:0} fps avg ({secs * 1000 / Math.Max(_caveSmokeFrames, 1):0.00} ms/frame), worst {_caveSmokeWorst * 1000:0.0} ms, "
            + $"terrain {calls} draw call(s), {chunks} chunks, {tris} triangles; {v?.OpenCells ?? 0} hole cells, {v?.CaveCells ?? 0} cave cells, GL error 0x{(uint)OpenGl.Native.glGetError():X}");
        SmokeShot($"cave_{name}");
        _caveSmokeAdvance = true;
    }

    /// <summary>
    /// The Cave Test views: "entrance" stands on the ground outside the west tunnel mouth looking in; "chamber" stands in
    /// the middle of the underground chamber looking down the tunnel.
    /// </summary>
    private static void DrawCaveSmoke()
    {
        var fb = _window.FramebufferSize;
        var map = World.Map;
        if (fb.X <= 0 || fb.Y <= 0 || _caveSmoke < 0)
            return;
        Vector3 eye, look;
        if (CaveSmokeViews[_caveSmoke] == "entrance")
        {
            eye = new Vector3(-38f, map.GroundHeight(-38f, -4f) + 1.7f, -4f);
            look = new Vector3(-26f, map.GroundHeight(-26f, 0f) - 2.5f, 0f);
        }
        else
        {
            var floor = map.Terrain is { } t ? t.GroundBelow(0f, 0f, t.Voids!.MinFloor + 0.5f) : 0f;
            eye = new Vector3(4f, floor + 1.7f, 2f);
            look = new Vector3(-14f, floor + 1.2f, 0f);
        }
        Gfx.BeginFrame(fb.X, fb.Y, map.Sky);
        Gfx.SetCamera(Matrix4x4.CreateLookAt(eye, look, Vector3.UnitY),
            Matrix4x4.CreatePerspectiveFieldOfView(BaseFov, fb.X / (float)fb.Y, 0.05f, map.FarPlane));
        Gfx.SetFog(map.Sky, map.FogStart, map.FogEnd);
        DrawArena();
        Gfx.SetFog(map.Sky, 0, 0);
    }
}
