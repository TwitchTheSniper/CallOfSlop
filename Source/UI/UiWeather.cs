using System.Numerics;
using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// The weather map window (F11 by default, on maps with a "weather" section; docs/weather/WEATHER.md): the simulated
/// region seen from above (satellite clouds or rain radar), every storm with its track, class, wind and pressure, where
/// you are, what the weather is doing over you, and the time controls. Right-click the map to start a depression
/// there. Closed, it costs one bool check a frame.
/// </summary>
public sealed partial class Ui
{
    public bool WeatherMapOpen;

    private readonly Storm[] _wxStorms = new Storm[WeatherSim.MaxStorms];
    private readonly float[] _wxTracks = new float[WeatherSim.MaxStorms * WeatherSim.TrackLength * 3];

    private static readonly (string Label, float Minutes)[] WxSpeeds =
        [("1 min/s", 1f), ("10 min/s", 10f), ("1 h/s", 60f), ("3 h/s", 180f), ("6 h/s", 360f)];

    public void DrawWeatherMap(Vector3 camera, Map map, int bind)
    {
        if (!WeatherMapOpen)
        {
            Weather.MapWanted = false;
            return;
        }
        if (!Weather.MapWanted)
        {
            Weather.MapWanted = true;
            Weather.RequestMap();
        }
        ImGui.SetNextWindowSize(new float2(760 * _scale, 860 * _scale), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowBgAlpha(0.96f);
        if (ImGui.Begin("Weather map###weathermap"u8, ref WeatherMapOpen))
        {
            if (!Weather.Active)
            {
                ImGuiSafe.TextWrapped("This map has no weather. Streamed Terrain (Dev pack) has it; any map can, with a \"weather\" section (Maps/README.md, \"Weather\").");
                ImGui.End();
                return;
            }
            Weather.ReadStorms(_wxStorms, _wxTracks, out var local, out var time, out var formed, out var strongest, out var stepMs);
            var (size, _, latitude) = Weather.Domain;
            var day = (int)(time / 86400) + 1;
            var hour = time % 86400 / 3600;
            ImGuiSafe.TextColored(Orange, $"Day {day}, {(int)hour:00}:{(int)(hour % 1 * 60):00}   ({size / 1000:0} km square round the map, latitude {latitude:0} {(latitude >= 0 ? "N" : "S")})");
            ImGuiSafe.TextColored(Dim, $"{formed} storm(s) so far, strongest {(strongest < -1 ? "none yet" : WeatherSim.CategoryName(strongest))}; a step (10 min of weather) takes {stepMs:0.0} ms on its own thread");

            // Time controls.
            if (ImGui.RadioButton("Paused##wxspeed"u8, Weather.Paused))
                Weather.Paused = true;
            foreach (var (label, minutes) in WxSpeeds)
            {
                ImGui.SameLine();
                if (ImGui.RadioButton(label, !Weather.Paused && MathF.Abs(Weather.Speed - minutes) < 0.01f))
                {
                    Weather.Paused = false;
                    Weather.Speed = minutes;
                }
            }
            var radar = Weather.Radar;
            if (ImGui.Checkbox("Rain radar"u8, ref radar))
            {
                Weather.Radar = radar;
                Weather.RequestMap();
            }
            ImGui.SameLine();
            if (ImGui.Button("Seed a storm"u8))
                Weather.SpawnStorm(size * 0.3, -size * 0.22 * MathF.Sign(latitude));
            if (ImGui.IsItemHovered())
                ImGui.SetItemTooltip("Starts a tropical depression over the warm sea to the south-east; or right-click the map anywhere."u8);
            ImGui.SameLine();
            if (ImGui.Button("Restart"u8))
                Weather.Restart();

            // The map: square, as big as fits above the list.
            var avail = ImGui.GetContentRegionAvail();
            var side = MathF.Max(64f, MathF.Min(avail.X, avail.Y - 190 * _scale));
            var p0 = ImGui.GetCursorScreenPos();
            var p1 = p0 + new float2(side, side);
            ImGui.InvisibleButton("##wxmap"u8, new float2(side, side));
            var hovered = ImGui.IsItemHovered();
            var dl = ImGui.GetWindowDrawList();
            var tex = Weather.MapTexture;
            if (tex != 0)
                dl.AddImage(new ImTextureRef { _TexID = new ImTextureID((nint)tex) }, p0, p1, new float2(0, 1), new float2(1, 0), new ImColor8(255, 255, 255, 255));
            else
                dl.AddRectFilled(p0, p1, new ImColor8(20, 30, 50, 255));
            float2 Screen(double x, double y) => new(p0.X + (float)((x / size + 0.5) * side), p0.Y + (float)((0.5 - y / size) * side));
            var pxPerM = side / (float)size;

            // The map itself (tiny at this scale) and you.
            var me = Screen(camera.X, -camera.Z);
            var mapHalf = MathF.Max(map.HalfX, map.HalfZ) * pxPerM;
            dl.AddRect(me - new float2(mapHalf, mapHalf), me + new float2(mapHalf, mapHalf), new ImColor8(255, 230, 90, 200), 0, 0, 1f);
            dl.AddLine(me - new float2(7 * _scale, 0), me + new float2(7 * _scale, 0), new ImColor8(255, 230, 90, 255), 2f * _scale);
            dl.AddLine(me - new float2(0, 7 * _scale), me + new float2(0, 7 * _scale), new ImColor8(255, 230, 90, 255), 2f * _scale);
            dl.AddText(me + new float2(8 * _scale, -16 * _scale), new ImColor8(255, 230, 90, 255), "you");

            // Storms: tracks coloured by class, a ring at their eyewall's reach, name and class.
            for (var s = 0; s < WeatherSim.MaxStorms; s++)
            {
                ref readonly var st = ref _wxStorms[s];
                if (!st.Alive)
                    continue;
                var count = st.TrackCount;
                var prev = default(float2);
                for (var t = 0; t < count; t++)
                {
                    var idx = (st.TrackHead - count + t + WeatherSim.TrackLength) % WeatherSim.TrackLength;
                    var b = (s * WeatherSim.TrackLength + idx) * 3;
                    var p = Screen(_wxTracks[b], _wxTracks[b + 1]);
                    if (t > 0)
                        dl.AddLine(prev, p, CategoryColour(WeatherSim.CategoryOf(_wxTracks[b + 2])), 2.5f * _scale);
                    prev = p;
                }
                var c = Screen(st.X, st.Y);
                if (count > 0)
                    dl.AddLine(prev, c, CategoryColour(st.Category), 2.5f * _scale);
                var col = CategoryColour(st.Category);
                ImDrawListExtensions.AddCircle(dl, in c, MathF.Max(st.Rmax * 2.5f * pxPerM, 6 * _scale), col, 32, 2f * _scale);
                // A little cyclone symbol: two hooked arms.
                var arm = 9 * _scale;
                var h = latitude >= 0 ? 1f : -1f;
                dl.AddLine(c, c + new float2(arm, -arm * 0.4f * h), col, 2.5f * _scale);
                dl.AddLine(c, c + new float2(-arm, arm * 0.4f * h), col, 2.5f * _scale);
                var label = $"{WeatherSim.NameOf(st.Id)}: {ShortCategory(st.Category)}";
                dl.AddText(c + new float2(12 * _scale, -8 * _scale) + new float2(1, 1), new ImColor8(0, 0, 0, 200), label);
                dl.AddText(c + new float2(12 * _scale, -8 * _scale), col, label);
            }
            if (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
            {
                var m = ImGui.GetMousePos();
                Weather.SpawnStorm(((m.X - p0.X) / side - 0.5) * size, (0.5 - (m.Y - p0.Y) / side) * size);
            }
            if (hovered)
                ImGui.SetItemTooltip("Right-click: start a tropical depression here (it needs warm sea and low shear to grow)."u8);

            // Over you.
            var wind = MathF.Sqrt(local.WindU * local.WindU + local.WindV * local.WindV);
            ImGuiSafe.TextColored(Orange, $"Over you: wind {wind * 3.6f:0} km/h from the {Compass(-local.WindU, -local.WindV)}, cloud {local.Cover * 100:0}%, rain {local.Rain:0.0} mm/h"
                + (local.NearestStorm >= 0 ? $"; nearest storm {local.NearestKm:0} km away" : ""));

            if (ImGui.BeginTable("##wxstorms"u8, 6, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
            {
                ImGui.TableSetupColumn("Storm"u8);
                ImGui.TableSetupColumn("Class"u8);
                ImGui.TableSetupColumn("Wind"u8);
                ImGui.TableSetupColumn("Pressure"u8);
                ImGui.TableSetupColumn("Heading"u8);
                ImGui.TableSetupColumn("From you"u8);
                ImGui.TableHeadersRow();
                var any = false;
                for (var s = 0; s < WeatherSim.MaxStorms; s++)
                {
                    ref readonly var st = ref _wxStorms[s];
                    if (!st.Alive)
                        continue;
                    any = true;
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGuiSafe.TextColored(CategoryColour4(st.Category), WeatherSim.NameOf(st.Id));
                    ImGui.TableNextColumn();
                    ImGuiSafe.TextColored(WxText, WeatherSim.CategoryName(st.Category));
                    ImGui.TableNextColumn();
                    ImGuiSafe.TextColored(WxText, $"{st.Vmax * 3.6f:0} km/h ({st.Knots:0} kt)");
                    ImGui.TableNextColumn();
                    ImGuiSafe.TextColored(WxText, $"{st.Pressure:0} hPa");
                    ImGui.TableNextColumn();
                    var speed = MathF.Sqrt(st.Vx * st.Vx + st.Vy * st.Vy);
                    ImGuiSafe.TextColored(WxText, $"{Compass(st.Vx, st.Vy)} {speed * 3.6f:0} km/h");
                    ImGui.TableNextColumn();
                    var dx = st.X - camera.X;
                    var dy = st.Y + camera.Z;
                    ImGuiSafe.TextColored(WxText, $"{Math.Sqrt(dx * dx + dy * dy) / 1000:0} km {Compass((float)dx, (float)dy)}");
                }
                ImGui.EndTable();
                if (!any)
                    ImGuiSafe.TextColored(Dim, "No storms yet. They form over sea warmer than about 26.5 C where the shear is low and a cloud cluster turns; speed time up, or seed one.");
            }
            ImGuiSafe.TextColored(Dim, $"F11 or Esc closes. Weather runs on each PC from the map's seed; the sky's clouds come from this map.");
        }
        ImGui.End();
    }

    private static readonly float4 WxText = new(0.92f, 0.93f, 0.95f, 1f);

    private static string ShortCategory(int cat) => cat switch
    {
        -1 => "TD",
        0 => "TS",
        _ => $"Cat {cat}",
    };

    private static readonly string[] CompassPoints = ["N", "NE", "E", "SE", "S", "SW", "W", "NW"];

    /// <summary>The compass point a vector (x east, y north) points towards.</summary>
    private static string Compass(float x, float y)
    {
        if (x == 0 && y == 0)
            return "-";
        var deg = MathF.Atan2(x, y) * 180f / MathF.PI;
        var i = (int)MathF.Round(((deg % 360f) + 360f) % 360f / 45f) % 8;
        return CompassPoints[i];
    }

    private static Vector3 CategoryRgb(int cat) => cat switch
    {
        -1 => new Vector3(0.55f, 0.8f, 1f),
        0 => new Vector3(0.4f, 1f, 0.55f),
        1 => new Vector3(1f, 1f, 0.45f),
        2 => new Vector3(1f, 0.8f, 0.35f),
        3 => new Vector3(1f, 0.55f, 0.25f),
        4 => new Vector3(1f, 0.3f, 0.25f),
        _ => new Vector3(1f, 0.35f, 0.9f),
    };

    private static ImColor8 CategoryColour(int cat)
    {
        var c = CategoryRgb(cat);
        return new ImColor8((byte)(c.X * 255), (byte)(c.Y * 255), (byte)(c.Z * 255), 255);
    }

    private static float4 CategoryColour4(int cat)
    {
        var c = CategoryRgb(cat);
        return new float4(c.X, c.Y, c.Z, 1f);
    }
}
