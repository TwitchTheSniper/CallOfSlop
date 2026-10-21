using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>
/// Reading and writing a map file's optional "skybox" section (see Maps/README.md, "Sky"). Every mistake gives a
/// readable <see cref="MapFileException"/> naming the key and what it accepts. Files store a preset name plus only
/// what differs from it, and writing then reading gives the same sky back.
/// </summary>
public static class SkyFile
{
    private static readonly string[] TopKeys = ["preset", "type", "zenith", "horizon", "ground", "sun", "moon", "stars", "clouds", "haze", "fog", "ambient", "image", "faces", "exposure", "rotation"];
    private static readonly string[] SunKeys = ["direction", "elevation", "azimuth", "time", "latitude", "day", "heading", "size", "color", "intensity", "glow", "disc"];
    private static readonly string[] MoonKeys = ["direction", "size", "color", "intensity"];
    private static readonly string[] CloudKeys = ["coverage", "density", "scale", "speed", "heading", "altitude", "color", "shade"];
    private static readonly string[] HazeKeys = ["color", "strength"];
    private static readonly string[] FogKeys = ["fromSky", "max"];

    /// <summary>Cubemap face names, in upload order (+X, -X, +Y, -Y, +Z, -Z); "{face}" in a "faces" pattern becomes each.</summary>
    public static readonly string[] FaceNames = ["px", "nx", "py", "ny", "pz", "nz"];

    private const float Deg = MathF.PI / 180f;

    // ---- Reading ------------------------------------------------------------------------------------------------

    /// <summary>
    /// Reads a "skybox" value: a preset name ("dusk"), or an object. Returns null for null / false (no sky). Image skies
    /// are decoded here, from files next to the map file (<paramref name="source"/> is its path relative to the Maps
    /// folder), so a missing or broken image is a load error, not a black sky later.
    /// </summary>
    public static SkySettings? Parse(JsonElement e, string source)
    {
        var where = $"{source}: skybox";
        switch (e.ValueKind)
        {
            case JsonValueKind.Null or JsonValueKind.False:
                return null;
            case JsonValueKind.String:
                return Preset(e.GetString()!, where);
            case JsonValueKind.Object:
                break;
            default:
                throw new MapFileException($"{where}: expected a preset name ({string.Join(", ", SkySettings.PresetNames)}) or an object, got {e}");
        }
        CheckKeys(e, TopKeys, where);

        var s = Get(e, "preset") is { } pe
            ? Preset(pe.ValueKind == JsonValueKind.String ? pe.GetString()! : throw new MapFileException($"{where}: \"preset\" is a name, got {pe}"), where)
            : new SkySettings();

        if (Get(e, "type") is { } te)
        {
            var t = te.ValueKind == JsonValueKind.String ? te.GetString()!.Trim().ToLowerInvariant() : "";
            s.Type = t switch
            {
                "procedural" => SkyType.Procedural,
                "cubemap" or "cube" => SkyType.Cubemap,
                "equirect" or "equirectangular" or "panorama" => SkyType.Equirect,
                _ => throw new MapFileException($"{where}: \"type\" is \"procedural\", \"cubemap\" or \"equirect\", got {te}"),
            };
        }
        else if (Get(e, "faces") != null)
            s.Type = SkyType.Cubemap;
        else if (Get(e, "image") != null)
            s.Type = SkyType.Equirect;

        if (Get(e, "zenith") is { } z)
        {
            s.Zenith = Colour(z, $"{where}: zenith");
            s.ZenithSet = true;
        }
        if (Get(e, "horizon") is { } h)
        {
            s.Horizon = Colour(h, $"{where}: horizon");
            s.HorizonSet = true;
        }
        if (Get(e, "ground") is { } g)
        {
            s.Ground = Colour(g, $"{where}: ground");
            s.GroundSet = true;
        }
        s.Stars = Opt(e, "stars", s.Stars, 0, 2, where);
        s.Ambient = Opt(e, "ambient", s.Ambient, 0.2f, 3, where);
        s.Exposure = Opt(e, "exposure", s.Exposure, 0.01f, 64, where);
        s.Rotation = Opt(e, "rotation", s.Rotation, -360, 360, where);

        if (Get(e, "sun") is { } sun)
            ReadSun(sun, s, $"{where}: sun");
        if (Get(e, "moon") is { } moon)
        {
            if (moon.ValueKind is JsonValueKind.True or JsonValueKind.False)
                s.Moon = moon.GetBoolean();
            else
            {
                var mw = $"{where}: moon";
                RequireObject(moon, mw, "true, false or an object");
                CheckKeys(moon, MoonKeys, mw);
                s.Moon = true;
                if (Get(moon, "direction") is { } md)
                    s.MoonDirection = Direction(md, $"{mw}.direction");
                s.MoonSize = Opt(moon, "size", s.MoonSize, 0.1f, 30, mw);
                s.MoonIntensity = Opt(moon, "intensity", s.MoonIntensity, 0, 3, mw);
                if (Get(moon, "color") is { } mc)
                    s.MoonColor = Colour(mc, $"{mw}.color");
            }
        }
        if (Get(e, "clouds") is { } clouds)
        {
            if (clouds.ValueKind == JsonValueKind.False)
                s.CloudCoverage = 0;
            else
            {
                var cw = $"{where}: clouds";
                RequireObject(clouds, cw, "false or an object");
                CheckKeys(clouds, CloudKeys, cw);
                s.CloudCoverage = Opt(clouds, "coverage", s.CloudCoverage, 0, 1, cw);
                s.CloudDensity = Opt(clouds, "density", s.CloudDensity, 0, 1, cw);
                s.CloudScale = Opt(clouds, "scale", s.CloudScale, 50, 50000, cw);
                s.CloudSpeed = Opt(clouds, "speed", s.CloudSpeed, -300, 300, cw);
                s.CloudHeading = Opt(clouds, "heading", s.CloudHeading, -360, 360, cw);
                s.CloudAltitude = Opt(clouds, "altitude", s.CloudAltitude, 100, 20000, cw);
                if (Get(clouds, "color") is { } cc)
                    s.CloudColor = Colour(cc, $"{cw}.color");
                if (Get(clouds, "shade") is { } cs)
                    s.CloudShade = Colour(cs, $"{cw}.shade");
            }
        }
        if (Get(e, "haze") is { } haze)
        {
            var hw = $"{where}: haze";
            RequireObject(haze, hw, "an object");
            CheckKeys(haze, HazeKeys, hw);
            s.HazeStrength = Opt(haze, "strength", s.HazeStrength, 0, 2, hw);
            if (Get(haze, "color") is { } hc)
                s.HazeColor = Colour(hc, $"{hw}.color");
        }
        if (Get(e, "fog") is { } fog)
        {
            var fw = $"{where}: fog";
            RequireObject(fog, fw, "an object");
            CheckKeys(fog, FogKeys, fw);
            if (Get(fog, "fromSky") is { } fs)
                s.FogFromSky = fs.ValueKind is JsonValueKind.True or JsonValueKind.False ? fs.GetBoolean() : throw new MapFileException($"{fw}.fromSky is true or false, got {fs}");
            s.FogMax = Opt(fog, "max", s.FogMax, 0, 1, fw);
        }

        if (Get(e, "image") is { } img)
            s.Image = img.ValueKind == JsonValueKind.String && img.GetString()!.Trim().Length > 0 ? img.GetString()!.Trim() : throw new MapFileException($"{where}: \"image\" is a file name, got {img}");
        if (Get(e, "faces") is { } faces)
        {
            s.Faces = faces.ValueKind switch
            {
                JsonValueKind.String when faces.GetString()!.Contains("{face}") => new[] { faces.GetString()!.Trim() },
                JsonValueKind.Array when faces.GetArrayLength() == 6 && faces.EnumerateArray().All(f => f.ValueKind == JsonValueKind.String && f.GetString()!.Trim().Length > 0)
                    => faces.EnumerateArray().Select(f => f.GetString()!.Trim()).ToArray(),
                _ => throw new MapFileException($"{where}: \"faces\" is 6 file names (+X, -X, +Y, -Y, +Z, -Z) or one name with {{face}} in it (it becomes {string.Join(", ", FaceNames)}), got {faces}"),
            };
        }

        switch (s.Type)
        {
            case SkyType.Equirect when s.Image.Length == 0:
                throw new MapFileException($"{where}: type \"equirect\" needs \"image\": a 2:1 panorama (.png or .hdr) next to the map file");
            case SkyType.Cubemap when s.Faces.Length == 0:
                throw new MapFileException($"{where}: type \"cubemap\" needs \"faces\": 6 square .png files (+X, -X, +Y, -Y, +Z, -Z)");
            case SkyType.Procedural when s.Image.Length > 0 || s.Faces.Length > 0:
                throw new MapFileException($"{where}: \"image\" and \"faces\" need type \"equirect\" or \"cubemap\", not \"procedural\"");
            case SkyType.Equirect when s.Faces.Length > 0:
                throw new MapFileException($"{where}: an equirect sky has one \"image\", not \"faces\"");
            case SkyType.Cubemap when s.Image.Length > 0:
                throw new MapFileException($"{where}: a cubemap sky has \"faces\", not one \"image\"");
        }
        if (s.Type != SkyType.Procedural)
            LoadImage(s, source, where);
        return s;
    }

    /// <summary>Decodes the sky's image(s) and fills in the gradient colours the file didn't give from them.</summary>
    public static void LoadImage(SkySettings s, string source, string where)
    {
        try
        {
            s.Loaded = s.Type == SkyType.Cubemap
                ? SkyImages.LoadCubemap(FacePaths(s).Select(f => SkyImages.Resolve(f, source)).ToArray())
                : SkyImages.LoadEquirect(SkyImages.Resolve(s.Image, source));
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or ArgumentException or UnauthorizedAccessException)
        {
            throw new MapFileException($"{where}: {ex.Message}");
        }
        var (zenith, horizon, ground) = s.Loaded.Measure(s.Exposure, s.Rotation);
        if (!s.ZenithSet)
            s.Zenith = zenith;
        if (!s.HorizonSet)
            s.Horizon = horizon;
        if (!s.GroundSet)
            s.Ground = ground;
    }

    /// <summary>The six face file names (a "{face}" pattern expanded).</summary>
    public static string[] FacePaths(SkySettings s) =>
        s.Faces.Length == 1 ? FaceNames.Select(f => s.Faces[0].Replace("{face}", f)).ToArray() : s.Faces;

    private static SkySettings Preset(string name, string where) =>
        SkySettings.PresetKey(name) is { } key
            ? SkySettings.FromPreset(key)
            : throw new MapFileException($"{where}: unknown preset \"{name}\" (presets: {string.Join(", ", SkySettings.PresetNames)})");

    private static void ReadSun(JsonElement sun, SkySettings s, string where)
    {
        RequireObject(sun, where, "an object");
        CheckKeys(sun, SunKeys, where);
        var hasDir = Get(sun, "direction") != null;
        var hasAngles = Get(sun, "elevation") != null || Get(sun, "azimuth") != null;
        var hasTime = Get(sun, "time") != null || Get(sun, "latitude") != null || Get(sun, "day") != null;
        if ((hasDir ? 1 : 0) + (hasAngles ? 1 : 0) + (hasTime ? 1 : 0) > 1)
            throw new MapFileException($"{where}: give the sun's position one way: \"direction\", or \"elevation\" + \"azimuth\", or \"time\" (+ \"latitude\", \"day\")");
        s.Heading = Opt(sun, "heading", s.Heading, -360, 360, where);
        if (hasDir)
            s.SunDirection = Direction(Get(sun, "direction")!.Value, $"{where}.direction");
        else if (hasAngles)
        {
            if (Get(sun, "elevation") == null || Get(sun, "azimuth") == null)
                throw new MapFileException($"{where}: \"elevation\" and \"azimuth\" go together (degrees up from the horizon; compass degrees, 0 = north, 90 = east)");
            var el = Opt(sun, "elevation", 0, -90, 90, where) * Deg;
            var az = (Opt(sun, "azimuth", 0, -360, 360, where) + s.Heading) * Deg;
            s.SunDirection = new Vector3(MathF.Cos(el) * MathF.Sin(az), MathF.Sin(el), -MathF.Cos(el) * MathF.Cos(az));
        }
        else if (hasTime)
        {
            s.SunDirection = null;
            s.TimeOfDay = Opt(sun, "time", s.TimeOfDay, 0, 24, where);
            s.Latitude = Opt(sun, "latitude", s.Latitude, -90, 90, where);
            s.DayOfYear = Opt(sun, "day", s.DayOfYear, 1, 366, where);
        }
        s.SunSize = Opt(sun, "size", s.SunSize, 0.1f, 30, where);
        s.SunIntensity = Opt(sun, "intensity", s.SunIntensity, 0, 5, where);
        s.SunGlow = Opt(sun, "glow", s.SunGlow, 0, 4, where);
        if (Get(sun, "color") is { } c)
            s.SunColor = Colour(c, $"{where}.color");
        if (Get(sun, "disc") is { } d)
            s.SunDisc = d.ValueKind is JsonValueKind.True or JsonValueKind.False ? d.GetBoolean() : throw new MapFileException($"{where}.disc is true or false, got {d}");
    }

    private static JsonElement? Get(JsonElement obj, string name)
    {
        foreach (var p in obj.EnumerateObject())
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                return p.Value;
        return null;
    }

    private static void RequireObject(JsonElement e, string where, string what)
    {
        if (e.ValueKind != JsonValueKind.Object)
            throw new MapFileException($"{where}: expected {what}, got {e}");
    }

    private static void CheckKeys(JsonElement obj, string[] known, string where)
    {
        foreach (var p in obj.EnumerateObject())
            if (!known.Any(k => k.Equals(p.Name, StringComparison.OrdinalIgnoreCase)))
                throw new MapFileException($"{where}: unknown key \"{p.Name}\" (known: {string.Join(", ", known)})");
    }

    private static float Opt(JsonElement obj, string name, float fallback, float min, float max, string where)
    {
        if (Get(obj, name) is not { } v)
            return fallback;
        if (v.ValueKind != JsonValueKind.Number)
            throw new MapFileException($"{where}: \"{name}\" is a number from {Fmt(min)} to {Fmt(max)}, got {v}");
        var f = v.GetSingle();
        if (!float.IsFinite(f) || f < min || f > max)
            throw new MapFileException($"{where}: \"{name}\" is a number from {Fmt(min)} to {Fmt(max)}, got {v}");
        return f;
    }

    private static string Fmt(float f) => f.ToString("0.###", CultureInfo.InvariantCulture);

    private static Vector3 Colour(JsonElement e, string where)
    {
        var c = MapFile.ToColour(e, where, "");
        if (!float.IsFinite(c.X + c.Y + c.Z) || c.X < 0 || c.Y < 0 || c.Z < 0 || c.X > 1 || c.Y > 1 || c.Z > 1)
            throw new MapFileException($"{where}: colours are [r, g, b] with values 0-1, or \"#rrggbb\"");
        return c;
    }

    private static Vector3 Direction(JsonElement e, string where)
    {
        if (e.ValueKind != JsonValueKind.Array || e.GetArrayLength() != 3 || e.EnumerateArray().Any(x => x.ValueKind != JsonValueKind.Number))
            throw new MapFileException($"{where}: a direction is [x, y, z] pointing towards it (y is up, -z is north), got {e}");
        var v = new Vector3(e[0].GetSingle(), e[1].GetSingle(), e[2].GetSingle());
        if (!float.IsFinite(v.X + v.Y + v.Z) || v.LengthSquared() < 1e-6f)
            throw new MapFileException($"{where}: the direction can't be [0, 0, 0]");
        return v;
    }

    // ---- Writing ------------------------------------------------------------------------------------------------

    /// <summary>
    /// The "skybox" value for <paramref name="s"/>: the preset plus whatever differs from it (just "dusk" when nothing
    /// does). <paramref name="indent"/> is the indent of the "skybox" key.
    /// </summary>
    public static string ToJson(SkySettings s, string indent = "  ")
    {
        var b = s.Preset.Length > 0 ? SkySettings.FromPreset(s.Preset) : new SkySettings();
        var top = new List<string>();
        if (s.Preset.Length > 0)
            top.Add($"\"preset\": {Str(s.Preset)}");
        if (s.Type != b.Type)
            top.Add($"\"type\": {Str(s.Type.ToString().ToLowerInvariant())}");
        var image = s.Type != SkyType.Procedural;
        // Image skies measure the colours they weren't given; write only the ones the file set.
        if (image ? s.ZenithSet : s.Zenith != b.Zenith)
            top.Add($"\"zenith\": {Col(s.Zenith)}");
        if (image ? s.HorizonSet : s.Horizon != b.Horizon)
            top.Add($"\"horizon\": {Col(s.Horizon)}");
        if (image ? s.GroundSet : s.Ground != b.Ground)
            top.Add($"\"ground\": {Col(s.Ground)}");
        if (s.Image.Length > 0)
            top.Add($"\"image\": {Str(s.Image)}");
        if (s.Faces.Length == 1)
            top.Add($"\"faces\": {Str(s.Faces[0])}");
        else if (s.Faces.Length > 0)
            top.Add("\"faces\": [" + string.Join(", ", s.Faces.Select(Str)) + "]");
        Num(top, "exposure", s.Exposure, b.Exposure);
        Num(top, "rotation", s.Rotation, b.Rotation);

        var sun = new List<string>();
        if (s.SunDirection is { } sd)
        {
            if (b.SunDirection != sd)
                sun.Add($"\"direction\": {Col(sd)}");
        }
        else
        {
            var force = b.SunDirection != null; // the base has a fixed direction: "time" switches it to the clock
            Num(sun, "time", s.TimeOfDay, b.TimeOfDay, force);
            Num(sun, "latitude", s.Latitude, b.Latitude);
            Num(sun, "day", s.DayOfYear, b.DayOfYear);
        }
        Num(sun, "heading", s.Heading, b.Heading);
        Num(sun, "size", s.SunSize, b.SunSize);
        if (s.SunColor != b.SunColor)
            sun.Add($"\"color\": {Col(s.SunColor)}");
        Num(sun, "intensity", s.SunIntensity, b.SunIntensity);
        Num(sun, "glow", s.SunGlow, b.SunGlow);
        if (s.SunDisc != b.SunDisc)
            sun.Add($"\"disc\": {(s.SunDisc ? "true" : "false")}");
        Group(top, "sun", sun);

        if (!s.Moon && b.Moon)
            top.Add("\"moon\": false");
        else if (s.Moon)
        {
            var moon = new List<string>();
            if (s.MoonDirection is { } md && md != b.MoonDirection)
                moon.Add($"\"direction\": {Col(md)}");
            Num(moon, "size", s.MoonSize, b.MoonSize);
            if (s.MoonColor != b.MoonColor)
                moon.Add($"\"color\": {Col(s.MoonColor)}");
            Num(moon, "intensity", s.MoonIntensity, b.MoonIntensity);
            if (moon.Count > 0)
                Group(top, "moon", moon);
            else if (!b.Moon)
                top.Add("\"moon\": true");
        }
        Num(top, "stars", s.Stars, b.Stars);

        var clouds = new List<string>();
        Num(clouds, "coverage", s.CloudCoverage, b.CloudCoverage);
        Num(clouds, "density", s.CloudDensity, b.CloudDensity);
        Num(clouds, "scale", s.CloudScale, b.CloudScale);
        Num(clouds, "speed", s.CloudSpeed, b.CloudSpeed);
        Num(clouds, "heading", s.CloudHeading, b.CloudHeading);
        Num(clouds, "altitude", s.CloudAltitude, b.CloudAltitude);
        if (s.CloudColor != b.CloudColor)
            clouds.Add($"\"color\": {Col(s.CloudColor)}");
        if (s.CloudShade != b.CloudShade)
            clouds.Add($"\"shade\": {Col(s.CloudShade)}");
        Group(top, "clouds", clouds);

        var haze = new List<string>();
        if (s.HazeColor != b.HazeColor)
            haze.Add($"\"color\": {Col(s.HazeColor)}");
        Num(haze, "strength", s.HazeStrength, b.HazeStrength);
        Group(top, "haze", haze);

        var fog = new List<string>();
        if (s.FogFromSky != b.FogFromSky)
            fog.Add($"\"fromSky\": {(s.FogFromSky ? "true" : "false")}");
        Num(fog, "max", s.FogMax, b.FogMax);
        Group(top, "fog", fog);
        Num(top, "ambient", s.Ambient, b.Ambient);

        if (top.Count == 1 && s.Preset.Length > 0)
            return Str(s.Preset);
        if (top.Count == 0)
            return "{}";
        var sb = new StringBuilder("{\n");
        for (var i = 0; i < top.Count; i++)
            sb.Append(indent).Append("  ").Append(top[i]).Append(i < top.Count - 1 ? ",\n" : "\n");
        sb.Append(indent).Append('}');
        return sb.ToString();
    }

    private static void Num(List<string> parts, string key, float v, float baseValue, bool force = false)
    {
        if (force || v != baseValue)
            parts.Add($"\"{key}\": {N(v)}");
    }

    private static void Group(List<string> top, string key, List<string> parts)
    {
        if (parts.Count > 0)
            top.Add($"\"{key}\": {{ {string.Join(", ", parts)} }}");
    }

    private static string N(float f) => f.ToString("R", CultureInfo.InvariantCulture);

    private static string Col(Vector3 c) => $"[{N(c.X)}, {N(c.Y)}, {N(c.Z)}]";

    private static string Str(string s) => JsonSerializer.Serialize(s);
}
