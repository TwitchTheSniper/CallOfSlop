using System.Numerics;
using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// The cold storm (Source/Weather/ColdStorm.cs, docs/weather/WEATHER.md "Cold storm") from the game's side, on a map
/// with a "storm" section: moved on once a frame with the camera, its whiteout given to the fog and the light, its snow
/// and sky veil drawn (SnowRenderer.cs), its wind sounded (Audio.Storm.cs) and its frost drawn at the screen's edges.
/// Nothing on the wire and nothing on maps without one.
/// </summary>
public static unsafe partial class Program
{
    private static double _stormSeen = -1;

    /// <summary>The storm for this frame, at the camera (just after the map's fog is set, before anything is drawn).</summary>
    private static void StormFrame(double now, Vector3 eye)
    {
        var map = World.Map;
        if (map.Storm == null && ColdStorm.Settings == null || _wsCalm)
            return; // (no storm here, or a check picture asked for clear air: --calm)
        var dt = _stormSeen < 0 ? 0 : now - _stormSeen;
        _stormSeen = now;
        ColdStorm.Frame(map, dt, eye, map.Light, _game.SandboxOn ? _game.Constructs : null);
        ConstructLamps.Dark = ColdStorm.Dusk * ColdStorm.Outside; // (out in the dusk lamps throw pools of light; indoors they stay as they were)
        if (!ColdStorm.Active)
        {
            _game.Audio.StormWindStop();
            return;
        }
        var (c, start, end) = ColdStorm.Fog(map.Sky, map.FogStart, map.FogEnd);
        Gfx.SetFog(c, start, end);
        _game.Audio.StormWind(ColdStorm.Level, ColdStorm.Outside, ColdStorm.Gust);
    }

    /// <summary>Once a frame: no world drawn for a moment (the menus, a load) means the wind stops and the storm starts afresh.</summary>
    private static void StormIdle(double now)
    {
        if (_stormSeen < 0 || now - _stormSeen < 0.3)
            return;
        _stormSeen = -1;
        ColdStorm.Reset();
        ConstructLamps.Dark = 0f;
        _game?.Audio.StormWindStop();
    }

    /// <summary>The map's light as the storm leaves it (the map's own when there is none).</summary>
    private static LightSetup StormLight(Map map) => ColdStorm.Settings != null ? ColdStorm.Light(map.Light) : map.Light;

    /// <summary>Frost creeping in from the edges of the screen while you are out in the cold (under the HUD).</summary>
    private static void DrawStormFrost()
    {
        var f = ColdStorm.Frost;
        if (f < 0.01f || !ColdStorm.Active)
            return;
        var screen = ImGui.GetIO().DisplaySize;
        var dl = ImGui.GetBackgroundDrawList();
        var a = MathF.Pow(f, 1.4f);
        var rim = new ImColor8(225, 238, 250, (byte)(a * 150));
        var clear = new ImColor8(225, 238, 250, 0);
        var bx = screen.X * (0.08f + 0.14f * a);
        var by = screen.Y * (0.08f + 0.14f * a);
        dl.AddRectFilledMultiColor(new float2(0, 0), new float2(bx, screen.Y), rim, clear, clear, rim);
        dl.AddRectFilledMultiColor(new float2(screen.X - bx, 0), screen, clear, rim, rim, clear);
        dl.AddRectFilledMultiColor(new float2(0, 0), new float2(screen.X, by), rim, rim, clear, clear);
        dl.AddRectFilledMultiColor(new float2(0, screen.Y - by), screen, clear, clear, rim, rim);
        // Thicker rime in the corners, where it starts.
        var corner = new ImColor8(235, 245, 255, (byte)(a * 120));
        var cx = bx * 1.2f;
        var cy = by * 1.2f;
        dl.AddRectFilledMultiColor(new float2(0, 0), new float2(cx, cy), corner, clear, clear, clear);
        dl.AddRectFilledMultiColor(new float2(screen.X - cx, 0), new float2(screen.X, cy), clear, corner, clear, clear);
        dl.AddRectFilledMultiColor(new float2(screen.X - cx, screen.Y - cy), screen, clear, clear, corner, clear);
        dl.AddRectFilledMultiColor(new float2(0, screen.Y - cy), new float2(cx, screen.Y), clear, clear, clear, corner);
    }
}
