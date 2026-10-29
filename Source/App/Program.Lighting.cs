using System.Globalization;
using System.Numerics;
using Brutal.GlfwApi;

namespace CallOfSlop;

/// <summary>
/// Baked lighting in the game loop, and <c>--light-shots ["Map name"] [--at x,y,z,yaw,pitch ...]</c>: pictures of one map with baked lighting on and off
/// (docs/render/LIGHTING.md "Looking at it"), written next to the log as smoke_light_&lt;view&gt;_on.bmp / _off.bmp, then the
/// game quits. Views: the map-select overview, every <c>--at</c> camera (eye position, compass yaw, pitch in degrees, up is
/// positive) and, without any, each team's first spawn looking at the map's centre. Uses the smoke test's throwaway save
/// copy. Runs under a virtual X server with Mesa's software GL too (no GPU needed), so agents can look at lighting changes.
/// <c>--times t1,t2,...</c> (seconds): instead of on/off, each view with lighting on at those clock times
/// (smoke_light_&lt;view&gt;_t&lt;ms&gt;.bmp), a frame sequence of the flicker (flames and their light share the clock).
/// </summary>
public static unsafe partial class Program
{
    private static bool _shotTool;

    /// <summary>
    /// This frame's dynamic lights on lit maps (LightGrid, at most 4; the setting can turn them off): your muzzle flash, then
    /// the newest blasts. Called before the world is drawn. No allocation.
    /// </summary>
    private static void AddFrameLights(double now, Vector3 eye, Vector3 fwd)
    {
        if (World.Map.LightBake == null || !LightGrid.DynamicEnabled)
            return;
        if (now < _game.MuzzleFlashUntil)
            LightGrid.AddDynamic(eye + fwd * 0.9f, new Vector3(1.6f, 1.15f, 0.6f), 7f);
        for (var i = _game.Blasts.Count - 1; i >= 0; i--)
        {
            var b = _game.Blasts[i];
            var age = (float)(now - b.Born);
            if (age < 0f || age > 0.5f)
                continue;
            var tint = b.Tint == default ? new Vector3(1f, 0.55f, 0.15f) : b.Tint;
            LightGrid.AddDynamic(b.Pos + new Vector3(0, 0.5f, 0), tint * (3f * (1f - age / 0.5f)), MathF.Max(b.Radius * 2.5f, 6f));
        }
    }

    /// <summary>The clock --light-shots draws at, so two runs (or the 4.6 and 4.1 paths) give the same pictures.</summary>
    private const double ShotTime = 100.0;

    private static void RunLightShots(string[] args)
    {
        var name = args.SkipWhile(a => !a.Equals("--light-shots", StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault(a => !a.StartsWith("--"));
        var map = name != null ? Maps.Find(name) : Maps.All.FirstOrDefault(m => m.LightBake != null);
        if (map == null)
        {
            Console.WriteLine($"[lightshots] no map \"{name}\" (or no lit map)");
            return;
        }
        World.SetMap(map);
        var views = new List<(string Name, Vector3 Eye, Vector3 Target, float Fov)>();
        var (oe, ot, ofov, _) = MapThumbs.Camera(map);
        views.Add(("overview", oe, ot, ofov));
        var ats = args.Select((a, i) => (a, i)).Where(t => t.a.Equals("--at", StringComparison.OrdinalIgnoreCase) && t.i + 1 < args.Length).Select(t => args[t.i + 1]).ToList();
        foreach (var at in ats)
        {
            var v = at.Split(',').Select(s => float.Parse(s, CultureInfo.InvariantCulture)).ToArray();
            if (v.Length != 5)
                continue;
            float yaw = v[3] * MathF.PI / 180f, pitch = v[4] * MathF.PI / 180f;
            var dir = new Vector3(MathF.Sin(yaw) * MathF.Cos(pitch), MathF.Sin(pitch), -MathF.Cos(yaw) * MathF.Cos(pitch));
            var eye = new Vector3(v[0], v[1], v[2]);
            views.Add(($"view{views.Count}", eye, eye + dir, 1.2f));
        }
        if (ats.Count == 0)
            for (var i = 0; i < Math.Min(2, map.Spawns.Count); i++)
            {
                var s = map.Spawns[i * (map.Spawns.Count - 1)];
                views.Add(($"spawn{i}", s + new Vector3(0, 1.7f, 0), new Vector3(0, 1.5f, 0), 1.2f));
            }
        var b = map.LightBake;
        Console.WriteLine($"[lightshots] {map.Name}: {(b == null ? "not lit" : $"grid {b.NX}x{b.NY}x{b.NZ} at {b.Cell:0.##} m{(b.CellY != b.Cell ? $" ({b.CellY:0.##} m tall)" : "")}, {b.Sources.Length} lights, {b.Bytes / 1024} KB, baked in {b.BakeMs:0} ms")}; {views.Count} views");
        var times = args.SkipWhile(a => !a.Equals("--times", StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault()?
            .Split(',').Select(s => double.Parse(s, CultureInfo.InvariantCulture)).ToArray();
        var saved = _settings.Lighting;
        var debug = args.Any(a => a.Equals("--light-debug", StringComparison.OrdinalIgnoreCase));
        var kinds = new[] { (On: true, T: -1.0, Dbg: 0), (On: false, T: -1.0, Dbg: 0) };
        if (debug) // (the debug pictures: the bake's occluders and lights over the scene, and the grid's light alone)
            kinds = [.. kinds, (On: true, T: -1.0, Dbg: 1), (On: true, T: -1.0, Dbg: 3), (On: true, T: -1.0, Dbg: 4)];
        foreach (var view in views)
            foreach (var shot in times != null ? times.Select(t => (On: true, T: t, Dbg: 0)) : kinds)
            {
                var on = shot.On;
                LightGrid.DebugView = shot.Dbg >= 3 ? shot.Dbg : 0;
                _settings.Lighting = on ? LightingQuality.Baked : LightingQuality.Off;
                for (var frame = 0; frame < 3 && !_window.ShouldClose; frame++) // (render data is built on the first draw)
                {
                    Glfw.PollEvents();
                    if (shot.T >= 0)
                        Glfw.SetTime(shot.T); // (this tool quits afterwards: the clock is only borrowed)
                    var fb = _window.FramebufferSize;
                    Gfx.BeginFrame(fb.X, fb.Y, map.Sky);
                    Gfx.SetCamera(Matrix4x4.CreateLookAt(view.Eye, view.Target, Vector3.UnitY),
                        Matrix4x4.CreatePerspectiveFieldOfView(view.Fov, fb.X / (float)Math.Max(fb.Y, 1), 0.05f, map.FarPlane));
                    Gfx.SetFog(map.Sky, map.FogStart, map.FogEnd);
                    Glfw.SetTime(ShotTime); // one fixed moment: flicker and wind look (nearly) the same in every run and on both GL paths
                    DrawArena();
                    Gfx.SetFog(map.Sky, 0, 0);
                    if (shot.Dbg > 0)
                        DrawLightDebug(map, shot.Dbg == 1);
                    if (frame == 2)
                        SaveScreenshot(shot.T >= 0 ? $"light_{view.Name}_t{shot.T * 1000:00000}"
                            : $"light_{view.Name}_{(shot.Dbg switch { 1 => "occ", 3 => "grid", 4 => "sky", _ => on ? "on" : "off" })}");
                    _window.SwapBuffers();
                }
                Console.WriteLine($"[lightshots] {view.Name} {(shot.T >= 0 ? $"t={shot.T:0.###}" : shot.Dbg > 0 ? $"debug {shot.Dbg}" : on ? "on" : "off")}: GL error 0x{GlDebug.Error():x}");
            }
        LightGrid.DebugView = 0;
        _settings.Lighting = saved;
        if (debug && b != null)
            foreach (var line in LightBaker.Audit(map))
                Console.WriteLine($"[lightshots] audit: {line}");
        Console.WriteLine("[lightshots] done");
    }

    /// <summary>
    /// --light-debug: the bake's occluders (LightBaker.OccluderList) drawn over the scene (edges: map boxes white, buildings
    /// green, prop parts magenta, hidden colliders yellow, tree trunks brown; all but map boxes also filled faintly, so an
    /// invisible blocker shows) and every baked light as a small cube in its colour (seen through walls; a spot or area light with a stick
    /// along its direction). With <paramref name="occluders"/> false only the lights (the grid pictures).
    /// </summary>
    private static void DrawLightDebug(Map map, bool occluders)
    {
        Gfx.Rebind();
        Gfx.SetBlend(true);
        Gfx.SetDepthWrite(false);
        if (occluders)
            foreach (var (box, kind, _) in LightBaker.OccluderList(map, LightBaker.MinOccluder(map.LightingSettings ?? new LightingSpec())))
            {
                var col = kind switch
                {
                    LightBaker.OccluderKind.Building => new Vector3(0.2f, 1f, 0.3f),
                    LightBaker.OccluderKind.Prop => new Vector3(1f, 0.2f, 1f),
                    LightBaker.OccluderKind.Hidden => new Vector3(1f, 1f, 0.1f),
                    LightBaker.OccluderKind.Trunk => new Vector3(0.6f, 0.4f, 0.2f),
                    _ => new Vector3(0.9f, 0.9f, 0.9f),
                };
                if (kind != LightBaker.OccluderKind.MapBox)
                    Gfx.Draw(Matrix4x4.CreateScale(box.Half * 2.02f) * Matrix4x4.CreateTranslation(box.Center), col, 0.18f, 2);
                DrawEdges(box.Min, box.Max, col, 0.025f);
            }
        Gfx.SetDepthTest(false);
        if (map.LightBake is { } bake)
            foreach (var l in bake.Sources)
            {
                var c = Vector3.Normalize(l.Color + new Vector3(1e-3f)) * 1.2f;
                Gfx.Draw(Matrix4x4.CreateScale(0.12f) * Matrix4x4.CreateTranslation(l.Pos), c, 0.9f, 2);
                if (l.Dir != Vector3.Zero)
                    for (var t = 0.15f; t < 0.8f; t += 0.1f)
                        Gfx.Draw(Matrix4x4.CreateScale(0.04f) * Matrix4x4.CreateTranslation(l.Pos + l.Dir * t), c, 0.9f, 2);
            }
        Gfx.SetDepthTest(true);
        Gfx.SetDepthWrite(true);
        Gfx.SetBlend(false);
    }

    private static void DrawEdges(Vector3 a, Vector3 b, Vector3 col, float w)
    {
        var size = b - a;
        for (var axis = 0; axis < 3; axis++)
            for (var e = 0; e < 4; e++)
            {
                var p = a;
                var s = new Vector3(w);
                var u = (axis + 1) % 3;
                var v = (axis + 2) % 3;
                p = Set(p, axis, (a[axis] + b[axis]) * 0.5f);
                p = Set(p, u, (e & 1) == 0 ? a[u] : b[u]);
                p = Set(p, v, (e & 2) == 0 ? a[v] : b[v]);
                s = Set(s, axis, size[axis] + w);
                Gfx.Draw(Matrix4x4.CreateScale(s) * Matrix4x4.CreateTranslation(p), col, 0.85f, 2);
            }
    }

    private static Vector3 Set(Vector3 p, int axis, float value)
    {
        p[axis] = value;
        return p;
    }
}
