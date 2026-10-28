using System.Numerics;
using Brutal.OpenGlApi;

namespace CallOfSlop;

/// <summary>Mesh and model drawing (see <see cref="MeshRenderer"/>) and the mesh smoke-test step.</summary>
public static unsafe partial class Program
{
    private static readonly MeshRenderer MeshGfx = new();

    // ---- Smoke step: the "Mesh Test" map from fixed viewpoints ----------------------------------------------------
    // Runs after the props step. For each view: 0.5 s to settle, 1.5 s measured, one "[smoke] mesh" line (fps, worst
    // frame, the renderer's parts, draw calls, LODs, CPU/GPU ms) and a screenshot smoke_mesh_VIEW.bmp. VSync is off for
    // the step so the fps numbers mean something; settings are restored afterwards (never saved).

    private const string MeshSmokeMap = "Mesh Test";

    private static readonly (string Name, Vector3 Eye, Vector3 Target, string Map)[] MeshSmokeViews =
    [
        ("overview", new Vector3(0f, 24f, 44f), new Vector3(0f, 0f, 2f), MeshSmokeMap),
        ("primitives", new Vector3(0f, 4.5f, -25f), new Vector3(0f, 0.8f, -8f), MeshSmokeMap),
        ("models", new Vector3(-7f, 2.4f, 7f), new Vector3(0f, 0.9f, 16f), MeshSmokeMap),
        ("lod", new Vector3(-36f, 1.8f, 6f), new Vector3(30f, 1f, 6f), MeshSmokeMap),
        // The Dev pack's bean-shaped map (a map boundary): its wall is instanced meshes, its floor a mesh following the outline.
        ("shape_inside", new Vector3(30f, 1.7f, -3f), new Vector3(-30f, 1.4f, 3f), "Shape Test"),
        ("shape_overview", new Vector3(0f, 52f, 48f), new Vector3(0f, 0f, -2f), "Shape Test"),
        // The Space pack's planet (point gravity, MapPlanet.cs): from orbit, and at eye height near the north pole looking
        // south along the ground (the horizon about 13 m away, the ground dropping away beyond it).
        ("planet_orbit", new Vector3(60f, 70f, 130f), Vector3.Zero, "Pocket Planet"),
        ("planet_eye", new Vector3(0f, 54.22f, 19.73f), new Vector3(0f, 43.96f, 47.92f), "Pocket Planet"),
    ];

    private static int _meshSmoke = -1; // view index; -1 = not running
    private static double _meshSmokeStart, _meshSmokeMeasureStart, _meshSmokeWorst;
    private static bool _meshSmokeMeasuring, _meshSmokeAdvance, _meshSmokeSavedVSync;
    private static int _meshSmokeFrames;

    private static bool MeshSmokeActive => _meshSmoke >= 0;

    private static void StartMeshSmoke(double now)
    {
        if (Maps.Find(MeshSmokeMap) is not { } map)
        {
            Console.WriteLine($"[smoke] mesh: map '{MeshSmokeMap}' not found, step skipped");
            StartLegsSmoke(now); // then the legs step (Program.Legs.cs), then on to the maps
            return;
        }
        if (World.Map != map)
            World.SetMap(map);
        _meshSmokeSavedVSync = _settings.VSync;
        _settings.VSync = false;
        _meshSmoke = 0;
        BeginMeshSmokeView(now);
    }

    private static void BeginMeshSmokeView(double now)
    {
        if (Maps.Find(MeshSmokeViews[_meshSmoke].Map) is { } viewMap && World.Map != viewMap)
            World.SetMap(viewMap);
        _meshSmokeStart = now;
        _meshSmokeMeasuring = false;
        _meshSmokeAdvance = false;
    }

    private static void MeshSmokeTick(double now)
    {
        if (_meshSmokeAdvance)
        {
            _meshSmoke++;
            if (_meshSmoke >= MeshSmokeViews.Length)
            {
                _meshSmoke = -1;
                _settings.VSync = _meshSmokeSavedVSync;
                StartLegsSmoke(now); // then the legs step (Program.Legs.cs), then on to the maps
                return;
            }
            BeginMeshSmokeView(now);
            return;
        }
        var t = now - _meshSmokeStart;
        if (!_meshSmokeMeasuring)
        {
            if (t > 0.5)
            {
                _meshSmokeMeasuring = true;
                _meshSmokeFrames = 0;
                _meshSmokeWorst = 0;
                _meshSmokeMeasureStart = now;
                MeshGfx.ResetWindow(now);
            }
            return;
        }
        _meshSmokeFrames++;
        _meshSmokeWorst = Math.Max(_meshSmokeWorst, FrameDt);
        if (t < 2.0)
            return;
        var view = MeshSmokeViews[_meshSmoke];
        var secs = Math.Max(now - _meshSmokeMeasureStart, 1e-6);
        Console.WriteLine($"[smoke] mesh {view.Name,-10} ({World.Map.Name}): {_meshSmokeFrames / secs:0} fps avg ({secs * 1000 / Math.Max(_meshSmokeFrames, 1):0.00} ms/frame), "
            + $"worst {_meshSmokeWorst * 1000:0.0} ms, [mesh] {MeshGfx.Describe(MeshGfx.Window)}, GL error 0x{GlDebug.Error():X}");
        SmokeShot("mesh_" + view.Name);
        _meshSmokeAdvance = true;
    }

    /// <summary>Renders the current mesh smoke view (no menu over it).</summary>
    private static void DrawMeshSmoke()
    {
        var fb = _window.FramebufferSize;
        if (fb.X <= 0 || fb.Y <= 0)
            return;
        var (_, eye, target, _) = MeshSmokeViews[Math.Max(_meshSmoke, 0)];
        var map = World.Map;
        Gfx.BeginFrame(fb.X, fb.Y, map.Sky);
        Gfx.SetCamera(Matrix4x4.CreateLookAt(eye, target, Vector3.UnitY),
            Matrix4x4.CreatePerspectiveFieldOfView(BaseFov, fb.X / (float)fb.Y, 0.05f, map.FarPlane));
        Gfx.SetFog(map.Sky, map.FogStart, map.FogEnd);
        DrawArena();
        Gfx.SetFog(map.Sky, 0, 0);
    }
}
