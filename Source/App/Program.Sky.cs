using System.Numerics;
using Brutal.OpenGlApi;

namespace CallOfSlop;

/// <summary>The sky smoke-test step (see <see cref="SkyRenderer"/>).</summary>
public static unsafe partial class Program
{
    // ---- Smoke step: the "Sky Test" map under every preset and both image skies ------------------------------------
    // For each view: 0.4 s to settle, 1.0 s measured, one "[smoke] sky" line (fps, worst frame, sky GPU ms), and a
    // screenshot smoke_sky_<name>.bmp; "clear" is measured again at Low quality to show what Low saves. Ends with one
    // summary line. VSync is off for the step; the map's own sky and the settings are put back afterwards.

    private const string SkySmokeMap = "Sky Test";

    private static readonly (string Name, SkyQuality Quality)[] SkySmokeViews =
    [
        ("clear", SkyQuality.High), ("overcast", SkyQuality.High), ("dusk", SkyQuality.High), ("night", SkyQuality.High),
        ("desert", SkyQuality.High), ("alpine", SkyQuality.High), ("cubemap", SkyQuality.High), ("hdr", SkyQuality.High),
        ("clear_low", SkyQuality.Low),
    ];

    private static int _skySmoke = -1; // index into SkySmokeViews; -1 = not running
    private static double _skySmokeStart, _skySmokeMeasureStart, _skySmokeWorst;
    private static int _skySmokeFrames;
    private static bool _skySmokeMeasuring, _skySmokeAdvance, _skySmokeSavedVSync;
    private static SkyQuality _skySmokeSavedQuality;
    private static SkySettings? _skySmokeSavedSky;
    private static readonly List<(string Name, double Gpu)> SkySmokeCosts = new();

    private static bool SkySmokeActive => _skySmoke >= 0;

    private static void StartSkySmoke(double now)
    {
        if (Maps.Find(SkySmokeMap) is not { } map)
        {
            Console.WriteLine($"[smoke] sky: map '{SkySmokeMap}' not found, step skipped");
            SmokeNextMap(now);
            return;
        }
        if (World.Map != map)
            World.SetMap(map);
        _skySmokeSavedSky = map.Skybox;
        _skySmokeSavedQuality = _settings.SkyQuality;
        _skySmokeSavedVSync = _settings.VSync;
        _settings.VSync = false;
        SkySmokeCosts.Clear();
        _skySmoke = 0;
        BeginSkySmokeView(now);
    }

    private static void BeginSkySmokeView(double now)
    {
        var (name, quality) = SkySmokeViews[_skySmoke];
        var map = World.Map;
        try
        {
            map.Skybox = SkySmokeSky(name, map);
        }
        catch (MapFileException ex)
        {
            Console.WriteLine($"[smoke] sky {name}: couldn't load ({ex.Message}); using the map's own sky");
            map.Skybox = _skySmokeSavedSky;
        }
        if (map.Skybox != null)
            map.Sky = map.Skybox.Horizon;
        _settings.SkyQuality = quality;
        _skySmokeStart = now;
        _skySmokeMeasuring = false;
        _skySmokeAdvance = false;
    }

    /// <summary>The sky for a smoke view: a preset, or one of the sample image skies in Maps/Dev/Skies.</summary>
    private static SkySettings SkySmokeSky(string name, Map map)
    {
        var source = map.SourceFile ?? "Dev/SkyTest.json";
        switch (name)
        {
            case "cubemap":
            {
                var s = new SkySettings { Type = SkyType.Cubemap, Faces = ["Skies/sample_{face}.png"], SunDirection = SkySettings.SunFromTime(17.3f, 40f, 80f, 0f) };
                SkyFile.LoadImage(s, source, "smoke cubemap");
                return s;
            }
            case "hdr":
            {
                var s = new SkySettings { Type = SkyType.Equirect, Image = "Skies/sample.hdr", SunDirection = SkySettings.SunFromTime(17.3f, 40f, 80f, 0f) };
                SkyFile.LoadImage(s, source, "smoke hdr");
                return s;
            }
            default:
                return SkySettings.FromPreset(name.Replace("_low", ""));
        }
    }

    private static void SkySmokeTick(double now)
    {
        if (_skySmokeAdvance)
        {
            // The screenshot was taken at the end of the previous frame; move on.
            _skySmoke++;
            if (_skySmoke >= SkySmokeViews.Length)
            {
                EndSkySmoke();
                StartCaveSmoke(now); // then the cave step (Program.Caves.cs), then on to the maps
                return;
            }
            BeginSkySmokeView(now);
            return;
        }
        var t = now - _skySmokeStart;
        if (!_skySmokeMeasuring)
        {
            if (t > 0.4)
            {
                _skySmokeMeasuring = true;
                _skySmokeFrames = 0;
                _skySmokeWorst = 0;
                _skySmokeMeasureStart = now;
                SkyRenderer.ResetWindow();
            }
            return;
        }
        _skySmokeFrames++;
        _skySmokeWorst = Math.Max(_skySmokeWorst, FrameDt);
        if (t < 1.4)
            return;
        var (name, quality) = SkySmokeViews[_skySmoke];
        var secs = Math.Max(now - _skySmokeMeasureStart, 1e-6);
        var gpu = SkyRenderer.WindowGpuMs;
        SkySmokeCosts.Add((name, gpu));
        var fb = _window.FramebufferSize;
        Console.WriteLine($"[smoke] sky {name,-9}: {_skySmokeFrames / secs:0} fps avg ({secs * 1000 / Math.Max(_skySmokeFrames, 1):0.00} ms/frame), worst {_skySmokeWorst * 1000:0.0} ms, "
            + $"sky GPU {(gpu >= 0 ? $"{gpu:0.000} ms" : "n/a")} at {fb.X}x{fb.Y} ({quality}), {World.Map.Skybox?.Describe() ?? "no sky"}, GL error 0x{GlDebug.Error():X}");
        SmokeShot($"sky_{name}");
        _skySmokeAdvance = true;
    }

    private static void EndSkySmoke()
    {
        _skySmoke = -1;
        var map = World.Map;
        map.Skybox = _skySmokeSavedSky;
        if (map.Skybox != null)
            map.Sky = map.Skybox.Horizon;
        _settings.SkyQuality = _skySmokeSavedQuality;
        _settings.VSync = _skySmokeSavedVSync;
        var high = SkySmokeCosts.Where(c => !c.Name.EndsWith("_low") && c.Gpu >= 0).ToList();
        var low = SkySmokeCosts.FirstOrDefault(c => c.Name.EndsWith("_low"));
        var clear = SkySmokeCosts.FirstOrDefault(c => c.Name == "clear");
        if (high.Count == 0)
        {
            Console.WriteLine("[smoke] sky summary: no GPU timings (timer queries unavailable)");
            return;
        }
        var worst = high.MaxBy(c => c.Gpu);
        var fb = _window.FramebufferSize;
        var scale = 2560.0 * 1440.0 / Math.Max(1.0, (double)fb.X * fb.Y); // the sky is per pixel: scale to 1440p
        Console.WriteLine($"[smoke] sky summary: High avg {high.Average(c => c.Gpu):0.000} ms, worst {worst.Gpu:0.000} ms ({worst.Name}); clear at Low {low.Gpu:0.000} ms vs High {clear.Gpu:0.000} ms; "
            + $"worst scaled to 2560x1440 ~{worst.Gpu * scale:0.000} ms (budget 0.2 ms: {(worst.Gpu * scale <= 0.2 ? "ok" : "OVER")})");
    }

    private static bool SkySmokeBlocked(Map map, Vector3 p)
    {
        foreach (var b in map.Solid)
            if (MathF.Abs(p.X - b.Center.X) < b.Half.X + 0.5f && MathF.Abs(p.Y - b.Center.Y) < b.Half.Y + 0.5f && MathF.Abs(p.Z - b.Center.Z) < b.Half.Z + 0.5f)
                return true;
        return false;
    }

    /// <summary>Renders the Sky Test map from the ground, facing the sun (or the moon at night), a little upwards.</summary>
    private static void DrawSkySmoke()
    {
        var fb = _window.FramebufferSize;
        var map = World.Map;
        if (fb.X <= 0 || fb.Y <= 0)
            return;
        var sky = map.Skybox;
        var toward = sky == null ? -Vector3.UnitZ : sky.Moon && sky.SunDir.Y < 0 ? sky.MoonDir : sky.SunDir;
        var flat = Vector3.Normalize(new Vector3(toward.X, 0, toward.Z) + new Vector3(0.0001f, 0, 0));
        var side = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, flat));
        // Stand back from the middle on the far side from the sun, so the tower and the far hills are in front; skip spots
        // inside buildings.
        var eye = Vector3.Zero;
        foreach (var (back, across) in new[] { (34f, 10f), (34f, -10f), (40f, 0f), (26f, 14f), (26f, -14f), (20f, 0f) })
        {
            var x = -flat.X * back + side.X * across;
            var z = -flat.Z * back + side.Z * across;
            eye = new Vector3(x, (map.HasTerrain ? map.GroundHeight(x, z) : 0f) + 1.7f, z);
            if (!SkySmokeBlocked(map, eye) && !SkySmokeBlocked(map, eye + flat * 3f))
                break;
        }
        var dir = Vector3.Normalize(flat + new Vector3(0, 0.22f, 0) + side * 0.35f);
        Gfx.BeginFrame(fb.X, fb.Y, map.Sky);
        Gfx.SetCamera(Matrix4x4.CreateLookAt(eye, eye + dir, Vector3.UnitY),
            Matrix4x4.CreatePerspectiveFieldOfView(BaseFov, fb.X / (float)fb.Y, 0.05f, map.FarPlane));
        Gfx.SetFog(map.Sky, map.FogStart, map.FogEnd);
        DrawArena();
        Gfx.SetFog(map.Sky, 0, 0);
    }
}
