using System.Numerics;

namespace CallOfSlop;

/// <summary>What shape a <see cref="MapLight"/> gives off light from.</summary>
public enum MapLightType : byte
{
    /// <summary>Every way from one point (a candle, a bulb, a torch).</summary>
    Point = 0,

    /// <summary>A cone from one point (a spotlight, a downlight, a floodlight).</summary>
    Spot = 1,

    /// <summary>A glowing rectangle, lighting the side it faces (a ceiling panel, a window of light, a light box).</summary>
    Area = 2,
}

/// <summary>
/// One static light source (docs/render/LIGHTING.md). Map files give them with the <c>LightSource</c> op, props carry them in
/// their "lights" list (in the prop's own frame until the prop is placed). Lights are looks only: they are baked into
/// the map's light grid (<see cref="LightBake"/>) and never reach the fingerprint or the network.
/// </summary>
/// <param name="Pos">Where it is (for an area light, the rectangle's centre).</param>
/// <param name="Color">Its colour (0-1 each), times nothing: <see cref="Intensity"/> is apart.</param>
/// <param name="Intensity">How bright: 1 lights a white wall facing it at its centre about as much as full sun does.</param>
/// <param name="Range">Metres: the light fades to nothing here (a smooth curve, no hard edge).</param>
/// <param name="Dir">Spot and area: the unit direction it shines in (area: the side of the rectangle that glows).</param>
/// <param name="CosOuter">Spot: cosine of half the cone angle (outside: no light).</param>
/// <param name="CosInner">Spot: cosine of the angle within which it's at full strength (the soft edge is between).</param>
/// <param name="Size">Area: the rectangle's width and depth in metres (across <see cref="Dir"/>).</param>
/// <param name="Flicker">A cheap animated light (flames, a dodgy bulb): its baked share wavers at draw time.</param>
public readonly record struct MapLight(Vector3 Pos, Vector3 Color, float Intensity, float Range, MapLightType Type = MapLightType.Point,
    Vector3 Dir = default, float CosOuter = -1f, float CosInner = -1f, Vector2 Size = default, bool Flicker = false)
{
    public const float MinRange = 0.5f, MaxRange = 40f, MaxIntensity = 20f;

    /// <summary>The unit direction for a compass <paramref name="yawDeg"/> (0 = north, -Z; 90 = east) and <paramref name="pitchDeg"/> below the horizontal (90 = straight down).</summary>
    public static Vector3 Direction(float yawDeg, float pitchDeg)
    {
        float y = yawDeg * (MathF.PI / 180f), p = pitchDeg * (MathF.PI / 180f);
        var d = new Vector3(MathF.Sin(y) * MathF.Cos(p), -MathF.Sin(p), -MathF.Cos(y) * MathF.Cos(p));
        return Vector3.Normalize(d);
    }

    /// <summary>
    /// Checks and builds one light from the values a map op or prop file gives (throws <see cref="InvalidOperationException"/>
    /// with a readable message). <paramref name="cone"/> is the full cone angle in degrees (spot).
    /// </summary>
    public static MapLight Make(Vector3 pos, Vector3 color, float intensity, float range, string type, float yaw, float pitch, float cone, float w, float d, bool flicker)
    {
        var kind = type.Trim().ToLowerInvariant() switch
        {
            "point" or "" => MapLightType.Point,
            "spot" => MapLightType.Spot,
            "area" => MapLightType.Area,
            _ => throw new InvalidOperationException($"light \"type\" is \"point\", \"spot\" or \"area\", got \"{type}\""),
        };
        if (!(intensity >= 0 && intensity <= MaxIntensity))
            throw new InvalidOperationException($"light \"intensity\" is 0 to {MaxIntensity}");
        if (!(range >= MinRange && range <= MaxRange))
            throw new InvalidOperationException($"light \"range\" is {MinRange} to {MaxRange} metres");
        if (!(color.X >= 0 && color.Y >= 0 && color.Z >= 0 && color.X <= 1 && color.Y <= 1 && color.Z <= 1))
            throw new InvalidOperationException("light \"color\" values are 0 to 1");
        if (!float.IsFinite(pos.X) || !float.IsFinite(pos.Y) || !float.IsFinite(pos.Z))
            throw new InvalidOperationException("light position must be finite");
        if (!(cone >= 1 && cone <= 170))
            throw new InvalidOperationException("light \"cone\" is 1 to 170 degrees");
        if (!(w >= 0.05f && w <= 20 && d >= 0.05f && d <= 20))
            throw new InvalidOperationException("an area light's \"w\" and \"d\" are 0.05 to 20 metres");
        if (!(MathF.Abs(yaw) <= 360 && MathF.Abs(pitch) <= 90))
            throw new InvalidOperationException("light \"yaw\" is -360 to 360 degrees and \"pitch\" -90 (up) to 90 (down)");
        var dir = kind == MapLightType.Point ? Vector3.Zero : Direction(yaw, pitch);
        var half = cone * 0.5f * (MathF.PI / 180f);
        return new MapLight(pos, color, intensity, range, kind, dir,
            kind == MapLightType.Spot ? MathF.Cos(half) : -1f, kind == MapLightType.Spot ? MathF.Cos(half * 0.75f) : -1f,
            kind == MapLightType.Area ? new Vector2(w, d) : Vector2.Zero, flicker);
    }

    /// <summary>This light (in a prop's frame) on a prop placed at <paramref name="at"/>, turned by <paramref name="yaw"/> radians and scaled.</summary>
    public MapLight Placed(Vector3 at, float yaw, float scale)
    {
        float cs = MathF.Cos(yaw), sn = MathF.Sin(yaw);
        var p = Pos * scale;
        var world = new Vector3(at.X + p.X * cs + p.Z * sn, at.Y + p.Y, at.Z - p.X * sn + p.Z * cs);
        var dir = Dir == Vector3.Zero ? Dir : new Vector3(Dir.X * cs + Dir.Z * sn, Dir.Y, -Dir.X * sn + Dir.Z * cs);
        return this with { Pos = world, Dir = dir, Range = Range * scale, Size = Size * scale };
    }
}

/// <summary>The <c>BakedLighting</c> op's settings (docs/render/LIGHTING.md). Defaults are what a map with only LightSource ops gets.</summary>
/// <param name="Cell">Grid spacing in metres (the baker may coarsen it on big maps to stay within its memory budget).</param>
/// <param name="Indoor">How much of the sky's ambient light reaches a fully enclosed spot (0 = black, 1 = no darkening).</param>
/// <param name="SunShadows">Shade the sun (or moon) where boxes block it.</param>
/// <param name="PropLights">Count the lights props carry (lamps, torches, candles).</param>
/// <param name="Region">The part of the map the grid covers: min x, min z, max x, max z (all zero: the whole map). Outside it: the classic light.</param>
/// <param name="Top">The grid's top in metres (0: just above the tallest box or light).</param>
public readonly record struct LightingSpec(float Cell = 0.5f, float Indoor = 0.25f, bool SunShadows = true, bool PropLights = true, Vector4 Region = default, float Top = 0f);

/// <summary>Static lights on a map: the LightSource and BakedLighting ops, and the bake run at the end of <see cref="Validate"/>.</summary>
public sealed partial class Map
{
    /// <summary>The most lights (ops and props together) a map bakes; more are left out with a log line.</summary>
    public const int MaxLights = 2048;

    /// <summary>Lights placed by LightSource ops, in op order.</summary>
    public readonly List<MapLight> Lights = new();

    /// <summary>The BakedLighting op's settings (null: none; with LightSource ops the map is still lit, with the defaults).</summary>
    public LightingSpec? LightingSettings;

    /// <summary>The baked light grid (null: the map isn't lit, and looks exactly as maps always did).</summary>
    public LightBake? LightBake;

    /// <summary>Is the map lit (a BakedLighting op or a LightSource op)? Lit maps bake their light when validated.</summary>
    public bool IsLit => LightingSettings != null || Lights.Count > 0;

    /// <summary>
    /// A static light baked into the map's light grid: a point light at (x, y, z) (y above the ground on terrain maps),
    /// or with <paramref name="type"/> "spot" a cone pointing at compass <paramref name="yaw"/> and <paramref name="pitch"/>
    /// degrees down (90 = straight down) <paramref name="cone"/> degrees wide, or "area" a glowing <paramref name="w"/> x
    /// <paramref name="d"/> rectangle facing that way. <paramref name="flicker"/> makes it waver (flames). Looks only.
    /// </summary>
    [MapOp]
    public void LightSource(float x, float y, float z, Vector3 color, float intensity = 1f, float range = 8f, string type = "point",
        float yaw = 0f, float pitch = 90f, float cone = 60f, float w = 1f, float d = 1f, bool flicker = false)
    {
        using var __ = Op([x, y, z, color, intensity, range, type, yaw, pitch, cone, w, d, flicker]);
        Lights.Add(MapLight.Make(new Vector3(x, y + PointGround(x, z), z), color, intensity, range, type, yaw, pitch, cone, w, d, flicker));
    }

    /// <summary>
    /// Turns baked lighting on for this map and sets it up: the grid's <paramref name="cell"/> size, the ambient left in
    /// fully enclosed places (<paramref name="indoor"/>), sun shadows and prop lights. Without it a map with LightSource ops
    /// gets the defaults; without either a map is unlit and looks as maps always did.
    /// </summary>
    /// <remarks>
    /// <paramref name="region"/>: only this rectangle gets a grid (a big map with one lit building: finer cells for the
    /// same memory); outside it everything has the classic light. <paramref name="top"/>: the grid's top height (0: above
    /// the tallest box or light).
    /// </remarks>
    [MapOp]
    public void BakedLighting(float cell = 0.5f, float indoor = 0.25f, bool sunShadows = true, bool propLights = true,
        (float X0, float Z0, float X1, float Z1)? region = null, float top = 0f)
    {
        using var __ = Op([cell, indoor, sunShadows, propLights, region, top]);
        if (!(cell >= 0.25f && cell <= 4f))
            throw new InvalidOperationException("\"cell\" is 0.25 to 4 metres");
        if (!(indoor >= 0f && indoor <= 1f))
            throw new InvalidOperationException("\"indoor\" is 0 to 1");
        if (!(top >= 0f && top <= 200f))
            throw new InvalidOperationException("\"top\" is 0 (automatic) to 200 metres");
        var r = Vector4.Zero;
        if (region is { } g)
        {
            r = new Vector4(MathF.Min(g.X0, g.X1), MathF.Min(g.Z0, g.Z1), MathF.Max(g.X0, g.X1), MathF.Max(g.Z0, g.Z1));
            if (!(r.Z - r.X >= 2f && r.W - r.Y >= 2f))
                throw new InvalidOperationException("\"region\" is a rectangle at least 2 m each way");
        }
        LightingSettings = new LightingSpec(cell, indoor, sunShadows, propLights, r, top);
    }

    /// <summary>Every light the bake uses: the LightSource ops' in order, then each placed prop's (when prop lights are on), capped at <see cref="MaxLights"/>.</summary>
    public List<MapLight> BakeLights()
    {
        var all = new List<MapLight>(Lights);
        if (LightingSettings?.PropLights ?? true)
            foreach (var p in PropInstances)
                foreach (var l in p.Def.Lights)
                    all.Add(l.Placed(p.Position, p.Yaw, p.Scale));
        if (all.Count > MaxLights)
        {
            Console.WriteLine($"Map '{Name}': {all.Count} lights, only the first {MaxLights} are baked");
            all.RemoveRange(MaxLights, all.Count - MaxLights);
        }
        return all;
    }

    /// <summary>Bakes the light grid of a lit map (the end of <see cref="Validate"/>); unlit maps keep none.</summary>
    private void BakeLighting()
    {
        LightBake = IsLit ? LightBaker.Bake(this) : null;
    }
}
