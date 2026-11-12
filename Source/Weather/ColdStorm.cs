using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The cold storm on a map with a "storm" section (<see cref="StormSettings"/>; docs/weather/WEATHER.md, "Cold storm"),
/// as this PC sees it: how hard it blows now (<see cref="Level"/>, rising and falling through lulls and gusts), where the
/// wind is going, and whether the camera is out in it (<see cref="Outside"/>: a few rays up and round from the eye find a
/// roof, a wall, a cabin or a mine's ceiling). The renderers and the sound read it; nothing goes on the wire, and the
/// storm never touches the simulation. One frame's work is a handful of rays every tenth of a second, no allocations.
/// </summary>
public static class ColdStorm
{
    /// <summary>The storm of the map last given to <see cref="Frame"/>, or null (no storm there).</summary>
    public static StormSettings? Settings { get; private set; }

    /// <summary>Is there a storm here and is it not thinned out to nothing (high above the ground, or a calm map)?</summary>
    public static bool Active => Settings != null && Level > 0.002f;

    /// <summary>How hard it blows where the camera is, 0 to 1 (its strength, the lulls and the height above the ground).</summary>
    public static float Level { get; private set; }

    /// <summary>The gusts on top, 0 to 1, quicker than <see cref="Level"/> (the wind's sound and the snow's speed follow it).</summary>
    public static float Gust { get; private set; }

    /// <summary>How much the camera is out in the open, 0 (under a roof, in a cabin, down a mine) to 1, eased.</summary>
    public static float Outside { get; private set; }

    /// <summary>Frost at the edges of the view, 0 to 1: it creeps in while you stand outside and melts once you are in.</summary>
    public static float Frost { get; private set; }

    /// <summary>The wind at the camera, m/s, along the ground (world axes).</summary>
    public static Vector3 Wind { get; private set; }

    /// <summary>Up at the camera (a planetoid's up is away from its centre).</summary>
    public static Vector3 Up { get; private set; } = Vector3.UnitY;

    /// <summary>How far you see now, metres (the fog's end).</summary>
    public static float Visibility { get; private set; } = 1000f;

    /// <summary>The whiteout's colour now: the storm's colour in this light.</summary>
    public static Vector3 Color { get; private set; }

    /// <summary>The snow's drift so far, metres, wrapped to <see cref="SnowBox"/> (the snow renderer's offset; double so hours of it stay exact).</summary>
    public static Vector3 Drift => new((float)_driftX, (float)_driftY, (float)_driftZ);

    /// <summary>The box of snow round the camera, metres across (the flakes wrap round inside it).</summary>
    public const float SnowBox = 28f;

    private static double _clock, _driftX, _driftY, _driftZ, _nextShelter;
    private static float _openTarget = 1f;
    private static Map? _map;

    /// <summary>Back to calm (a new map, or the menus).</summary>
    public static void Reset()
    {
        Settings = null;
        _map = null;
        Level = Gust = Frost = 0f;
        Outside = _openTarget = 1f;
        Visibility = 1000f;
        Wind = Vector3.Zero;
        _nextShelter = 0;
    }

    /// <summary>
    /// Moves the storm on by <paramref name="dt"/> seconds with the camera at <paramref name="eye"/> on <paramref name="map"/>
    /// under <paramref name="light"/> (the map's own light, before the storm dims it). <paramref name="constructs"/>: the
    /// builds that can shelter you (cabins, sheds), or null.
    /// </summary>
    public static void Frame(Map map, double dt, Vector3 eye, in LightSetup light, ConstructWorld? constructs)
    {
        if (!ReferenceEquals(map, _map))
        {
            Reset();
            _map = map;
            Settings = map.Storm;
        }
        if (Settings is not { } s)
            return;
        dt = Math.Clamp(dt, 0, 0.25);
        _clock += dt;
        var up = GravityFrames.UpAt(eye);
        Up = up;

        // How hard it blows: a slow swell through lulls, a quicker wander, then the height over the ground thins it.
        var t = _clock / s.Cycle * Math.Tau;
        var swell = 0.5 + 0.5 * Math.Sin(t) * 0.75 + 0.5 * Math.Sin(t * 2.7 + 1.3) * 0.25;
        var storm = (float)(0.55 + 0.45 * swell) * s.Strength;
        var height = map.Planetoid is { } pl ? pl.RoughAltitude(eye) : 0f;
        var low = 1f - Smooth(s.Ceiling * 0.5f, s.Ceiling, height);
        var level = storm * low;
        Level = level;
        Gust = (float)Math.Clamp(0.5 + 0.35 * Math.Sin(_clock * 0.83) * Math.Sin(_clock * 0.31 + 2.0) + 0.15 * Math.Sin(_clock * 2.3 + 0.7), 0, 1);

        // The wind along the ground: a heading that wanders slowly round a prevailing one, harder in gusts.
        var east = Vector3.Cross(Vector3.UnitZ, up);
        if (east.LengthSquared() < 1e-4f)
            east = Vector3.Cross(Vector3.UnitX, up);
        east = Vector3.Normalize(east);
        var north = Vector3.Cross(up, east);
        var heading = 0.6 + 0.5 * Math.Sin(_clock / (s.Cycle * 1.7) * Math.Tau);
        var dir = east * (float)Math.Cos(heading) + north * (float)Math.Sin(heading);
        Wind = dir * (s.Wind * level * (0.65f + 0.55f * Gust));

        // The snow drifts with the wind and falls a little: the renderer wraps it round the camera.
        var fall = Wind * 0.85f - up * (1.1f + 0.6f * level);
        _driftX = Wrap(_driftX + fall.X * dt);
        _driftY = Wrap(_driftY + fall.Y * dt);
        _driftZ = Wrap(_driftZ + fall.Z * dt);

        // Out in it or sheltered: rays up and round, ten times a second, then eased so a doorway doesn't flicker.
        if (_clock >= _nextShelter)
        {
            _nextShelter = _clock + 0.1;
            _openTarget = Openness(eye, up, dir, constructs);
        }
        Outside += (_openTarget - Outside) * (1f - MathF.Exp(-(float)dt * 3f));

        var cold = level * Outside;
        Frost = Math.Clamp(Frost + (float)dt * (cold > 0.25f ? cold / 45f : -1f / 12f), 0f, 1f);

        // How far you see: from the lull's to the storm's, opening out again as it thins high up.
        var q = Math.Clamp(storm / MathF.Max(s.Strength, 0.01f), 0f, 1f) * MathF.Min(s.Strength * 1.5f, 1f);
        Visibility = MathF.Min((s.Clear + (s.Visibility - s.Clear) * q) / MathF.Max(low, 0.02f), map.FarPlane * 0.9f);

        // The whiteout's colour: the storm's colour in this light (dark at night, warm under a low sun).
        var sunUp = MathF.Max(Vector3.Dot(light.SunDir, up), 0f);
        var lum = (light.AmbSky.X + light.AmbSky.Y + light.AmbSky.Z) / 3f * 0.9f + (light.SunColor.X + light.SunColor.Y + light.SunColor.Z) / 3f * MathF.Sqrt(sunUp) * 0.7f;
        Color = s.Color * Math.Clamp(lum, 0.05f, 1.05f);
    }

    /// <summary>How open the sky is over <paramref name="eye"/>, 0 to 1: rays straight up and leaning four ways.</summary>
    private static float Openness(Vector3 eye, Vector3 up, Vector3 wind, ConstructWorld? constructs)
    {
        var side = Vector3.Normalize(Vector3.Cross(up, wind));
        var from = eye + up * 0.15f;
        var open = 0f;
        open += Clear(from, up, 30f, constructs) ? 0.4f : 0f;
        open += Clear(from, Vector3.Normalize(up + wind * 0.9f), 14f, constructs) ? 0.15f : 0f; // (into the wind counts most of the slanted ones)
        open += Clear(from, Vector3.Normalize(up - wind * 0.9f), 14f, constructs) ? 0.15f : 0f;
        open += Clear(from, Vector3.Normalize(up + side * 0.9f), 14f, constructs) ? 0.15f : 0f;
        open += Clear(from, Vector3.Normalize(up - side * 0.9f), 14f, constructs) ? 0.15f : 0f;
        return open;
    }

    private static bool Clear(Vector3 from, Vector3 dir, float reach, ConstructWorld? constructs)
    {
        if (World.RaycastWorld(from, dir, reach) < reach)
            return false;
        return constructs == null || !constructs.Raycast(from, dir, reach, out _);
    }

    private static double Wrap(double v)
    {
        v %= SnowBox;
        return v < 0 ? v + SnowBox : v;
    }

    private static float Smooth(float a, float b, float x)
    {
        var k = Math.Clamp((x - a) / MathF.Max(b - a, 1e-3f), 0f, 1f);
        return k * k * (3f - 2f * k);
    }

    /// <summary>
    /// <paramref name="light"/> under the storm: the sun's light spread into a flat overcast, the sky's colours pulled to
    /// the whiteout and the fog to its flat colour, all the way out. Steps of 1/40 in the level, so the light (re-sent to
    /// every scene shader when it changes) changes a few times a minute, not every frame.
    /// </summary>
    public static LightSetup Light(in LightSetup light)
    {
        if (!Active)
            return light;
        var k = MathF.Round(Level * 40f) / 40f;
        var c = new Vector3(MathF.Round(Color.X * 64f), MathF.Round(Color.Y * 64f), MathF.Round(Color.Z * 64f)) / 64f;
        var sun = light.SunColor * (1f - 0.7f * k);
        var spread = light.SunColor * (0.35f * k); // (what the cloud takes from the sun comes back as light from all round)
        return light with
        {
            SunColor = sun,
            AmbSky = Vector3.Lerp(light.AmbSky, c * 1.1f, 0.55f * k) + spread,
            AmbGround = Vector3.Lerp(light.AmbGround, c * 0.9f, 0.55f * k) + spread * 0.8f,
            FogMax = light.FogMax + (1f - light.FogMax) * MathF.Min(k * 3f, 1f),
            SkyFog = light.SkyFog && k < 0.15f,
            Zenith = Vector3.Lerp(light.Zenith, c, k),
            Horizon = Vector3.Lerp(light.Horizon, c, k),
            Ground = Vector3.Lerp(light.Ground, c, k),
            Haze = light.Haze * (1f - k),
        };
    }

    /// <summary>The fog under the storm: from the eye out to <see cref="Visibility"/>, in the whiteout's colour.</summary>
    public static (Vector3 Color, float Start, float End) Fog(Vector3 mapSky, float start, float end)
    {
        if (!Active)
            return (mapSky, start, end);
        var v = MathF.Round(Visibility);
        var e = end > start ? MathF.Min(end, v) : v;
        var c = new Vector3(MathF.Round(Color.X * 64f), MathF.Round(Color.Y * 64f), MathF.Round(Color.Z * 64f)) / 64f;
        c = Vector3.Lerp(mapSky, c, MathF.Min(MathF.Round(Level * 40f) / 40f * 3f, 1f));
        return (c, 0f, e);
    }
}
