namespace CallOfSlop.MapKit;

/// <summary>
/// Static lights in the kit (docs/mapkit/MAPKIT.md "Lights", Maps/README.md "Lighting"): the BakedLighting and LightSource
/// ops. Lights are looks only (never in the fingerprint) and are baked into the map's light grid when it loads, so a map
/// can have hundreds at no cost per frame. Lamps, torches, candles and chandeliers from the Props folder bring their own
/// lights; see <see cref="Lights"/> for rows, rings and wall fittings.
/// </summary>
public sealed partial class Kit
{
    /// <summary>
    /// Turns baked lighting on (Maps/README.md "Lighting"): grid <paramref name="cell"/> in metres, the ambient left in fully
    /// enclosed places (<paramref name="indoor"/>, 0-1), sun shadows and prop lights. Only the values that differ from the
    /// defaults are written. A map with LightSource ops is lit even without it.
    /// </summary>
    public Kit BakedLighting(double cell = 0.5, double indoor = 0.25, bool sunShadows = true, bool propLights = true)
    {
        var o = new JObject(("op", "BakedLighting"));
        if (cell != 0.5) o.Add("cell", R(cell));
        if (indoor != 0.25) o.Add("indoor", R(indoor));
        if (!sunShadows) o.Add("sunShadows", false);
        if (!propLights) o.Add("propLights", false);
        Emit(o);
        return this;
    }

    /// <summary>A point light at (x, y, z) (y above the ground on terrain maps): a candle, a bulb, a brazier.</summary>
    public JObject Light(double x, double y, double z, Color color, double intensity = 1, double range = 8, bool flicker = false, string? anchor = null)
    {
        Count("lights");
        var o = new JObject(("op", "LightSource"), ("x", R(x)), ("y", R(y)), ("z", R(z)), ("color", color.Node));
        if (intensity != 1) o.Add("intensity", R(intensity));
        if (range != 8) o.Add("range", R(range));
        if (flicker) o.Add("flicker", true);
        if (anchor != null) o.Add("anchor", anchor);
        return Emit(o);
    }

    /// <summary>
    /// A spotlight at (x, y, z) pointing at compass <paramref name="yaw"/> (0 = north, -Z; 90 = east) and
    /// <paramref name="pitch"/> degrees below the horizontal (90 = straight down), <paramref name="cone"/> degrees wide.
    /// </summary>
    public JObject SpotLight(double x, double y, double z, Color color, double yaw = 0, double pitch = 90, double cone = 60, double intensity = 1, double range = 10, bool flicker = false)
    {
        Count("lights");
        var o = new JObject(("op", "LightSource"), ("x", R(x)), ("y", R(y)), ("z", R(z)), ("color", color.Node), ("type", "spot"));
        if (intensity != 1) o.Add("intensity", R(intensity));
        if (range != 8) o.Add("range", R(range));
        if (Yaw(yaw) != 0) o.Add("yaw", Yaw(yaw));
        if (pitch != 90) o.Add("pitch", R(pitch, 1));
        if (cone != 60) o.Add("cone", R(cone, 1));
        if (flicker) o.Add("flicker", true);
        return Emit(o);
    }

    /// <summary>
    /// A glowing <paramref name="w"/> x <paramref name="d"/> rectangle centred at (x, y, z) shining towards compass
    /// <paramref name="yaw"/> and <paramref name="pitch"/> degrees down (90: a ceiling panel; 0: a window's light falling
    /// into a room, the rectangle then stands upright, w wide and d tall).
    /// </summary>
    public JObject AreaLight(double x, double y, double z, Color color, double w, double d, double yaw = 0, double pitch = 90, double intensity = 1, double range = 8)
    {
        Count("lights");
        var o = new JObject(("op", "LightSource"), ("x", R(x)), ("y", R(y)), ("z", R(z)), ("color", color.Node), ("type", "area"), ("w", R(w)), ("d", R(d)));
        if (intensity != 1) o.Add("intensity", R(intensity));
        if (range != 8) o.Add("range", R(range));
        if (Yaw(yaw) != 0) o.Add("yaw", Yaw(yaw));
        if (pitch != 90) o.Add("pitch", R(pitch, 1));
        return Emit(o);
    }
}

/// <summary>Light layouts built from the kit's light ops and lit props (docs/mapkit/MAPKIT.md "Lights").</summary>
public static class Lights
{
    /// <summary>Warm flame, tungsten bulb, cold daylight and moonlight colours to start from.</summary>
    public static readonly Color Flame = "#ffb45a", Candle = "#ffc878", Tungsten = "#ffd8a8", Daylight = "#e8f0ff", Moon = "#9fb4e8", Ember = "#ff7a30";

    /// <summary><paramref name="count"/> point lights evenly along the line (x0, z0)-(x1, z1) at height y (both ends included when count > 1).</summary>
    public static void Row(Kit k, double x0, double z0, double x1, double z1, double y, int count, Color color, double intensity = 1, double range = 8, bool flicker = false)
    {
        for (var i = 0; i < count; i++)
        {
            var t = count == 1 ? 0.5 : (double)i / (count - 1);
            k.Light(x0 + (x1 - x0) * t, y, z0 + (z1 - z0) * t, color, intensity, range, flicker);
        }
    }

    /// <summary><paramref name="count"/> point lights round a circle (a chandelier's candles, a ring of braziers), the first at compass bearing <paramref name="startYaw"/>.</summary>
    public static void Ring(Kit k, double cx, double cz, double y, double radius, int count, Color color, double intensity = 0.4, double range = 6, bool flicker = true, double startYaw = 0)
    {
        for (var i = 0; i < count; i++)
        {
            var a = (startYaw + 360.0 * i / count) * Math.PI / 180;
            k.Light(cx + Math.Sin(a) * radius, y, cz - Math.Cos(a) * radius, color, intensity, range, flicker);
        }
    }

    /// <summary>
    /// A lit prop (a lamp, torch, sconce or chandelier that carries its own lights) every <paramref name="spacing"/> metres
    /// along the line (x0, z0)-(x1, z1), at height y, all turned <paramref name="yaw"/> (props' yaw: a wall fitting's back
    /// against the wall). Returns how many were placed.
    /// </summary>
    public static int Fittings(Kit k, string prop, double x0, double z0, double x1, double z1, double spacing, double y = 0, double yaw = 0)
    {
        var len = Math.Sqrt((x1 - x0) * (x1 - x0) + (z1 - z0) * (z1 - z0));
        var n = Math.Max(1, (int)Math.Floor(len / Math.Max(spacing, 0.1)) + 1);
        for (var i = 0; i < n; i++)
        {
            var t = n == 1 ? 0.5 : (double)i / (n - 1);
            k.Prop(prop, x0 + (x1 - x0) * t, z0 + (z1 - z0) * t, y, yaw);
        }
        return n;
    }

    /// <summary>
    /// Light falling in through a window: an upright area light just inside the opening (centre x, y, z, the opening
    /// <paramref name="w"/> wide and <paramref name="h"/> tall), shining towards compass <paramref name="yaw"/> into the room.
    /// Coloured glass: pass the glass colour.
    /// </summary>
    public static JObject Window(Kit k, double x, double y, double z, double w, double h, double yaw, Color color, double intensity = 0.8, double range = 10) =>
        k.AreaLight(x, y, z, color, w, h, yaw, pitch: 0, intensity: intensity, range: range);
}
