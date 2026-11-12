using System.Globalization;
using System.Numerics;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>
/// A map's optional "storm" section (Maps/README.md, "Storm"; docs/weather/WEATHER.md, "Cold storm"): the weather at
/// ground level on a cold world. Snow driven sideways round the camera, a whiteout that closes the view in, frost
/// creeping in at the edges of the screen and the wind howling while you stand outside (<see cref="ColdStorm"/>).
/// Looks and sound only, worked out on each PC: like the sky it is not part of <see cref="Map.ContentHash"/>.
/// </summary>
public sealed class StormSettings
{
    /// <summary>How far you see at the height of the storm, metres (the fog's end).</summary>
    public float Visibility = 45f;

    /// <summary>How far you see in its calmest lull, metres.</summary>
    public float Clear = 220f;

    /// <summary>How hard it blows at its worst, 0 to 1 (scales the snow, the wind and the whiteout together).</summary>
    public float Strength = 1f;

    /// <summary>Seconds from one lull to the next.</summary>
    public float Cycle = 300f;

    /// <summary>The wind at the height of the storm, m/s.</summary>
    public float Wind = 16f;

    /// <summary>The whiteout's colour in full daylight (the light of the day dims it).</summary>
    public Vector3 Color = new(0.80f, 0.85f, 0.90f);

    /// <summary>Above this height over the ground, metres, the storm is gone (it thins out from half of it).</summary>
    public float Ceiling = 700f;

    private static readonly string[] Keys = ["visibility", "clear", "strength", "cycle", "wind", "color", "ceiling"];

    public StormSettings Clone() => (StormSettings)MemberwiseClone();

    /// <summary>Reads a "storm" value: true (the defaults), false / null (none) or an object. Mistakes throw with a reason.</summary>
    public static StormSettings? Parse(JsonElement e, string source)
    {
        var where = $"{source}: storm";
        switch (e.ValueKind)
        {
            case JsonValueKind.Null or JsonValueKind.False:
                return null;
            case JsonValueKind.True:
                return new StormSettings();
            case JsonValueKind.Object:
                break;
            default:
                throw new MapFileException($"{where}: expected true, false or an object, got {e}");
        }
        var s = new StormSettings();
        foreach (var p in e.EnumerateObject())
        {
            if (!Keys.Contains(p.Name))
                throw new MapFileException($"{where}: unknown key \"{p.Name}\" (known: {string.Join(", ", Keys)})");
            if (p.Name == "color")
            {
                s.Color = MapFile.ToColour(p.Value, "storm color", source);
                continue;
            }
            if (p.Value.ValueKind != JsonValueKind.Number)
                throw new MapFileException($"{where}: \"{p.Name}\" is a number, got {p.Value}");
            var v = p.Value.GetDouble();
            switch (p.Name)
            {
                case "visibility":
                    s.Visibility = v is >= 10 and <= 2000 ? (float)v : throw new MapFileException($"{where}: \"visibility\" is 10 to 2000 m, got {v}");
                    break;
                case "clear":
                    s.Clear = v is >= 10 and <= 5000 ? (float)v : throw new MapFileException($"{where}: \"clear\" is 10 to 5000 m, got {v}");
                    break;
                case "strength":
                    s.Strength = v is >= 0 and <= 1 ? (float)v : throw new MapFileException($"{where}: \"strength\" is 0 to 1, got {v}");
                    break;
                case "cycle":
                    s.Cycle = v is >= 20 and <= 7200 ? (float)v : throw new MapFileException($"{where}: \"cycle\" is 20 to 7200 seconds, got {v}");
                    break;
                case "wind":
                    s.Wind = v is >= 0 and <= 60 ? (float)v : throw new MapFileException($"{where}: \"wind\" is 0 to 60 m/s, got {v}");
                    break;
                case "ceiling":
                    s.Ceiling = v is >= 20 and <= 20000 ? (float)v : throw new MapFileException($"{where}: \"ceiling\" is 20 to 20000 m, got {v}");
                    break;
            }
        }
        if (s.Clear < s.Visibility)
            throw new MapFileException($"{where}: \"clear\" ({s.Clear} m) is less than \"visibility\" ({s.Visibility} m): the lull should see further than the storm");
        return s;
    }

    /// <summary>The "storm" value for these settings (only what differs from the defaults).</summary>
    public string ToJson()
    {
        var d = new StormSettings();
        var parts = new List<string>();
        if (Visibility != d.Visibility) parts.Add($"\"visibility\": {N(Visibility)}");
        if (Clear != d.Clear) parts.Add($"\"clear\": {N(Clear)}");
        if (Strength != d.Strength) parts.Add($"\"strength\": {N(Strength)}");
        if (Cycle != d.Cycle) parts.Add($"\"cycle\": {N(Cycle)}");
        if (Wind != d.Wind) parts.Add($"\"wind\": {N(Wind)}");
        if (Color != d.Color) parts.Add($"\"color\": [{N(Color.X)}, {N(Color.Y)}, {N(Color.Z)}]");
        if (Ceiling != d.Ceiling) parts.Add($"\"ceiling\": {N(Ceiling)}");
        return parts.Count == 0 ? "true" : "{ " + string.Join(", ", parts) + " }";
    }

    private static string N(float f) => f.ToString("R", CultureInfo.InvariantCulture);
}
