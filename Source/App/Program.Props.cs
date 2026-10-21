using System.Numerics;
using Brutal.OpenGlApi;

namespace CallOfSlop;

/// <summary>Prop drawing (see <see cref="PropRenderer"/>) and its smoke-test step.</summary>
public static unsafe partial class Program
{
    private static readonly PropRenderer PropGfx = new();

    // ---- Smoke step: the "Prop Gallery" and "Prop Yard" maps from fixed viewpoints -------------------------------
    // Runs after the foliage step. For each view: 0.5 s to settle, 1.5 s measured, one "[smoke] props" line (fps, worst
    // frame, props drawn, draw calls, debris, prop CPU/GPU ms) and a screenshot smoke_props_VIEW.bmp. The "break" view
    // breaks every breakable prop in front of the camera first, and shoots the debris in flight. VSync is off for the
    // step so the fps numbers mean something; settings are restored afterwards (never saved).

    private static readonly (string Name, string Map, Vector3 Eye, Vector3 Target, bool Break)[] PropsSmokeViews =
    [
        ("gallery", "Prop Gallery", new Vector3(0f, 17f, 42f), new Vector3(0f, 0f, 2f), false),
        ("gallery_street", "Prop Gallery", new Vector3(-21f, 1.8f, -16.5f), new Vector3(-6f, 0.6f, -24f), false),
        ("gallery2", "Prop Gallery 2", new Vector3(0f, 38f, 50f), new Vector3(0f, 0f, -2f), false), // the outdoor props: smoke_props_gallery2.bmp
        ("yard", "Prop Yard", new Vector3(-37f, 1.7f, 1f), new Vector3(8f, 1f, -1f), false),
        ("yard_break", "Prop Yard", new Vector3(-37f, 1.7f, 1f), new Vector3(8f, 1f, -1f), true),
        ("yard_overview", "Prop Yard", new Vector3(-30f, 24f, 34f), new Vector3(0f, 0f, 0f), false),
        ("cluttered_small", "Carousel", new Vector3(-27f, 13f, 25f), new Vector3(0f, 1f, -3f), false), // Cluttered pack: smoke_cluttered_small.bmp
        // The Cluttered pack's large map (thousands of props and plants): overview from the south-west, then the market at eye height.
        ("cluttered_large", "Sprawl", new Vector3(-96f, 42f, 84f), new Vector3(8f, 0f, -12f), false),
        ("cluttered_large_market", "Sprawl", new Vector3(-24f, 1.7f, 6f), new Vector3(20f, 1.2f, -4f), false),
        ("cluttered_medium", "Crescent Bazaar", new Vector3(-46f, 15f, 33f), new Vector3(-2f, 0f, -6f), false), // Cluttered pack: smoke_cluttered_medium.bmp
    ];

    private static int _propsSmoke = -1; // view index; -1 = not running
    private static double _propsSmokeStart, _propsSmokeMeasureStart, _propsSmokeWorst;
    private static bool _propsSmokeMeasuring, _propsSmokeAdvance, _propsSmokeShot, _propsSmokeSavedVSync;
    private static int _propsSmokeFrames;

    private static bool PropsSmokeActive => _propsSmoke >= 0;

    private static void StartPropsSmoke(double now)
    {
        if (Maps.Find("Prop Gallery") == null && Maps.Find("Prop Yard") == null)
        {
            Console.WriteLine("[smoke] props: maps 'Prop Gallery' and 'Prop Yard' not found, step skipped");
            StartMeshSmoke(now); // then the mesh step (Program.Meshes.cs), then on to the maps
            return;
        }
        _propsSmokeSavedVSync = _settings.VSync;
        _settings.VSync = false;
        _propsSmoke = 0;
        BeginPropsSmokeView(now);
    }

    private static void BeginPropsSmokeView(double now)
    {
        _propsSmokeStart = now;
        _propsSmokeMeasuring = false;
        _propsSmokeAdvance = false;
        _propsSmokeShot = false;
        var view = PropsSmokeViews[_propsSmoke];
        if (Maps.Find(view.Map) is { } map)
        {
            if (World.Map != map)
                World.SetMap(map);
            if (view.Break)
                SmokeBreakProps(map, view.Eye, view.Target);
        }
    }

    /// <summary>Breaks every breakable prop within 30 m in front of the camera (no session: straight on the map, then debris).</summary>
    private static void SmokeBreakProps(Map map, Vector3 eye, Vector3 target)
    {
        var fwd = Vector3.Normalize(target - eye);
        var broken = 0;
        foreach (var i in map.BreakableProps)
        {
            var p = map.PropInstances[i];
            var to = p.Bounds.Center - eye;
            if (to.Length() > 30f || Vector3.Dot(Vector3.Normalize(to), fwd) < 0.75f || !map.BreakProp(i))
                continue;
            PropDebris.Spawn(p, eye, PropDamage.Bullet, i);
            broken++;
        }
        Console.WriteLine($"[smoke] props: broke {broken} prop(s) in view on {map.Name}, {PropDebris.Count} debris pieces flying");
    }

    private static void PropsSmokeTick(double now)
    {
        if (_propsSmokeAdvance)
        {
            if (PropsSmokeViews[_propsSmoke].Break)
                World.Map.RestoreProps();
            _propsSmoke++;
            if (_propsSmoke >= PropsSmokeViews.Length)
            {
                _propsSmoke = -1;
                _settings.VSync = _propsSmokeSavedVSync;
                PropDebris.Clear();
                StartMeshSmoke(now); // then the mesh step (Program.Meshes.cs), then on to the maps
                return;
            }
            BeginPropsSmokeView(now);
            return;
        }
        var view = PropsSmokeViews[_propsSmoke];
        var t = now - _propsSmokeStart;
        if (view.Break && !_propsSmokeShot && t > 0.35)
        {
            SmokeShot("props_" + view.Name); // debris in the air
            _propsSmokeShot = true;
        }
        if (!_propsSmokeMeasuring)
        {
            if (t > 0.5)
            {
                _propsSmokeMeasuring = true;
                _propsSmokeFrames = 0;
                _propsSmokeWorst = 0;
                _propsSmokeMeasureStart = now;
                PropGfx.ResetWindow(now);
                FoliageGfx.ResetWindow(now);
            }
            return;
        }
        _propsSmokeFrames++;
        _propsSmokeWorst = Math.Max(_propsSmokeWorst, FrameDt);
        if (t < 2.0)
            return;
        var secs = Math.Max(now - _propsSmokeMeasureStart, 1e-6);
        var w = PropGfx.Window;
        Console.WriteLine($"[smoke] props {view.Name,-14} ({World.Map.Name}): {_propsSmokeFrames / secs:0} fps avg ({secs * 1000 / Math.Max(_propsSmokeFrames, 1):0.00} ms/frame), "
            + $"worst {_propsSmokeWorst * 1000:0.0} ms, {PropGfx.Uploaded} props uploaded, {w.AvgInstances:0} drawn in {w.AvgDraws:0.0} draw calls, "
            + $"debris {w.AvgDebris:0}, props CPU {w.AvgCpuMs:0.000} ms, GPU {(w.AvgGpuMs >= 0 ? $"{w.AvgGpuMs:0.000} ms" : "n/a")}, "
            + $"{World.Map.BrokenProps.Count} broken, GL error 0x{(uint)OpenGl.Native.glGetError():X}");
        if (view.Name.StartsWith("cluttered_", StringComparison.Ordinal))
        {
            // Cluttered pack maps: the foliage and box side of the same frames (boxes are still one draw call each).
            var f = FoliageGfx.Window;
            var map = World.Map;
            var boxes = 0;
            for (var i = 0; i < map.Solid.Count; i++)
                if (map.IsDrawnBox(i))
                    boxes++;
            Console.WriteLine($"[smoke] foliage {view.Name} ({map.Name}): {FoliageGfx.Uploaded} plants uploaded, {f.AvgInstances:0} drawn in {f.AvgDraws:0.0} draw calls, "
                + $"foliage CPU {f.AvgCpuMs:0.000} ms, GPU {(f.AvgGpuMs >= 0 ? $"{f.AvgGpuMs:0.000} ms" : "n/a")}; {boxes} boxes, {map.PropInstances.Count} props, "
                + $"{map.MeshPlacements.Count} meshes, quality {_settings.FoliageQuality}");
        }
        if (!view.Break)
            SmokeShot(view.Name.StartsWith("cluttered") ? view.Name : "props_" + view.Name); // the Cluttered pack's views keep their own names
        _propsSmokeAdvance = true;
    }

    /// <summary>Renders the current props smoke view (no menu over it).</summary>
    private static void DrawPropsSmoke()
    {
        var fb = _window.FramebufferSize;
        if (fb.X <= 0 || fb.Y <= 0)
            return;
        var (_, _, eye, target, _) = PropsSmokeViews[Math.Max(_propsSmoke, 0)];
        var map = World.Map;
        Gfx.BeginFrame(fb.X, fb.Y, map.Sky);
        Gfx.SetCamera(Matrix4x4.CreateLookAt(eye, target, Vector3.UnitY),
            Matrix4x4.CreatePerspectiveFieldOfView(BaseFov, fb.X / (float)fb.Y, 0.05f, map.FarPlane));
        Gfx.SetFog(map.Sky, map.FogStart, map.FogEnd);
        DrawArena();
        Gfx.SetFog(map.Sky, 0, 0);
    }
}
