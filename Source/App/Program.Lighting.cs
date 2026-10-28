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
        var saved = _settings.Lighting;
        foreach (var view in views)
            foreach (var on in new[] { true, false })
            {
                _settings.Lighting = on ? LightingQuality.Baked : LightingQuality.Off;
                for (var frame = 0; frame < 3 && !_window.ShouldClose; frame++) // (render data is built on the first draw)
                {
                    Glfw.PollEvents();
                    var fb = _window.FramebufferSize;
                    Gfx.BeginFrame(fb.X, fb.Y, map.Sky);
                    Gfx.SetCamera(Matrix4x4.CreateLookAt(view.Eye, view.Target, Vector3.UnitY),
                        Matrix4x4.CreatePerspectiveFieldOfView(view.Fov, fb.X / (float)Math.Max(fb.Y, 1), 0.05f, map.FarPlane));
                    Gfx.SetFog(map.Sky, map.FogStart, map.FogEnd);
                    DrawArena();
                    Gfx.SetFog(map.Sky, 0, 0);
                    if (frame == 2)
                        SaveScreenshot($"light_{view.Name}_{(on ? "on" : "off")}");
                    _window.SwapBuffers();
                }
                Console.WriteLine($"[lightshots] {view.Name} {(on ? "on" : "off")}: GL error 0x{(uint)Brutal.OpenGlApi.OpenGl.Native.glGetError():x}");
            }
        _settings.Lighting = saved;
        Console.WriteLine("[lightshots] done");
    }
}
