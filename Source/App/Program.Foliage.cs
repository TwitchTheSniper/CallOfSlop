using System.Numerics;
using Brutal.OpenGlApi;

namespace CallOfSlop;

/// <summary>Foliage drawing (see <see cref="FoliageRenderer"/>) and its smoke-test step.</summary>
public static partial class Program
{
    private static readonly FoliageRenderer FoliageGfx = new();

    // ---- Smoke step: the "Foliage Test" map from fixed viewpoints at every quality level -------------
    // For each level (High, Medium, Low, Off) and viewpoint: 0.5 s to settle, 1.5 s measured, one "[smoke] foliage"
    // line (fps, worst frame, plants drawn, draw calls, foliage CPU/GPU ms), screenshots at High and Low. VSync is
    // turned off for the step so the fps numbers mean something; settings are restored afterwards (and never saved).

    private const string FoliageSmokeMap = "Foliage Test";

    private static readonly FoliageQuality[] FoliageSmokeLevels = [FoliageQuality.High, FoliageQuality.Medium, FoliageQuality.Low, FoliageQuality.Off];

    private static readonly (string Name, Vector3 Eye, Vector3 Target)[] FoliageSmokeViews =
    [
        ("ground", new Vector3(-30f, 1.7f, 34f), new Vector3(6f, 1.4f, -8f)), // eye height, across the meadow to the thicket and grove
        ("overview", new Vector3(-48f, 22f, 48f), new Vector3(4f, 0f, -4f)), // high corner view over the whole map
    ];

    private static int _foliageSmoke = -1; // level * views + view; -1 = not running
    private static double _foliageSmokeStart;
    private static bool _foliageSmokeMeasuring, _foliageSmokeAdvance;
    private static int _foliageSmokeFrames;
    private static double _foliageSmokeWorst, _foliageSmokeMeasureStart;
    private static FoliageQuality _foliageSmokeSavedQuality;
    private static bool _foliageSmokeSavedVSync;

    private static bool FoliageSmokeActive => _foliageSmoke >= 0;

    private static void StartFoliageSmoke(double now)
    {
        if (Maps.Find(FoliageSmokeMap) == null)
        {
            Console.WriteLine($"[smoke] foliage: map '{FoliageSmokeMap}' not found, step skipped");
            SmokeNextMap(now);
            return;
        }
        _foliageSmokeSavedQuality = _settings.FoliageQuality;
        _foliageSmokeSavedVSync = _settings.VSync;
        _settings.VSync = false;
        _foliageSmoke = 0;
        BeginFoliageSmokeView(now);
    }

    private static void BeginFoliageSmokeView(double now)
    {
        _settings.FoliageQuality = FoliageSmokeLevels[_foliageSmoke / FoliageSmokeViews.Length];
        _foliageSmokeStart = now;
        _foliageSmokeMeasuring = false;
        _foliageSmokeAdvance = false;
    }

    private static void FoliageSmokeTick(double now)
    {
        if (_foliageSmokeAdvance)
        {
            // The screenshot (if any) was taken at the end of the previous frame; move on.
            _foliageSmoke++;
            if (_foliageSmoke >= FoliageSmokeLevels.Length * FoliageSmokeViews.Length)
            {
                _foliageSmoke = -1;
                _settings.FoliageQuality = _foliageSmokeSavedQuality;
                _settings.VSync = _foliageSmokeSavedVSync;
                SmokeNextMap(now);
                return;
            }
            BeginFoliageSmokeView(now);
            return;
        }
        var t = now - _foliageSmokeStart;
        if (!_foliageSmokeMeasuring)
        {
            if (t > 0.5) // buffers built and shaders warm
            {
                _foliageSmokeMeasuring = true;
                _foliageSmokeFrames = 0;
                _foliageSmokeWorst = 0;
                _foliageSmokeMeasureStart = now;
                FoliageGfx.ResetWindow(now);
            }
            return;
        }
        _foliageSmokeFrames++;
        _foliageSmokeWorst = Math.Max(_foliageSmokeWorst, FrameDt);
        if (t < 2.0)
            return;
        var level = FoliageSmokeLevels[_foliageSmoke / FoliageSmokeViews.Length];
        var view = FoliageSmokeViews[_foliageSmoke % FoliageSmokeViews.Length].Name;
        var secs = Math.Max(now - _foliageSmokeMeasureStart, 1e-6);
        var w = FoliageGfx.Window;
        Console.WriteLine($"[smoke] foliage {level,-6} {view,-8}: {_foliageSmokeFrames / secs:0} fps avg ({secs * 1000 / Math.Max(_foliageSmokeFrames, 1):0.00} ms/frame), "
            + $"worst {_foliageSmokeWorst * 1000:0.0} ms, {FoliageGfx.Uploaded} plants uploaded, {w.AvgInstances:0} drawn in {w.AvgDraws:0.0} draw calls, "
            + $"foliage CPU {w.AvgCpuMs:0.000} ms, GPU {(w.AvgGpuMs >= 0 ? $"{w.AvgGpuMs:0.000} ms" : "n/a")}, GL error 0x{(uint)OpenGl.Native.glGetError():X}");
        if (level is FoliageQuality.High or FoliageQuality.Low)
            SmokeShot($"foliage_{level.ToString().ToLowerInvariant()}_{view}");
        _foliageSmokeAdvance = true;
    }

    /// <summary>Renders the foliage test map from the current smoke viewpoint (no menu over it).</summary>
    private static void DrawFoliageSmoke()
    {
        var fb = _window.FramebufferSize;
        var map = Maps.Find(FoliageSmokeMap);
        if (fb.X <= 0 || fb.Y <= 0 || map == null)
            return;
        if (World.Map != map)
            World.SetMap(map);
        var (_, eye, target) = FoliageSmokeViews[Math.Max(_foliageSmoke, 0) % FoliageSmokeViews.Length];
        Gfx.BeginFrame(fb.X, fb.Y, map.Sky);
        Gfx.SetCamera(Matrix4x4.CreateLookAt(eye, target, Vector3.UnitY),
            Matrix4x4.CreatePerspectiveFieldOfView(BaseFov, fb.X / (float)fb.Y, 0.05f, map.FarPlane));
        Gfx.SetFog(map.Sky, map.FogStart, map.FogEnd);
        DrawArena();
        Gfx.SetFog(map.Sky, 0, 0);
    }
}
