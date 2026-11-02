using System.Globalization;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>
/// A map's optional "weather" section (Maps/README.md, "Weather"; docs/weather/WEATHER.md): a regional weather
/// simulation round the map (<see cref="WeatherSim"/>) whose clouds the sky draws and whose storms the F11 weather map
/// shows. Visual only for now: like the sky, it is not part of <see cref="Map.ContentHash"/>.
/// </summary>
public sealed class WeatherSettings
{
    /// <summary>Seed for the coastline, the noise and storm genesis.</summary>
    public int Seed = 1;

    /// <summary>Latitude of the map (the domain's centre), degrees; positive is north (storms turn anticlockwise).</summary>
    public float Latitude = 24f;

    /// <summary>Width of the square simulated region, km (the map sits in its middle).</summary>
    public float SizeKm = 3072f;

    /// <summary>Cells across the region (the grid is Cells x Cells).</summary>
    public int Cells = 256;

    /// <summary>Default speed: minutes of weather per real second (60 = an hour a second; a storm's life is minutes).</summary>
    public float Speed = 60f;

    private static readonly string[] Keys = ["seed", "latitude", "size", "cells", "speed"];

    public WeatherSettings Clone() => (WeatherSettings)MemberwiseClone();

    public bool SameAs(WeatherSettings o) =>
        Seed == o.Seed && Latitude == o.Latitude && SizeKm == o.SizeKm && Cells == o.Cells && Speed == o.Speed;

    /// <summary>Reads a "weather" value: true (the defaults), false / null (none) or an object. Mistakes throw with a reason.</summary>
    public static WeatherSettings? Parse(JsonElement e, string source)
    {
        var where = $"{source}: weather";
        switch (e.ValueKind)
        {
            case JsonValueKind.Null or JsonValueKind.False:
                return null;
            case JsonValueKind.True:
                return new WeatherSettings();
            case JsonValueKind.Object:
                break;
            default:
                throw new MapFileException($"{where}: expected true, false or an object, got {e}");
        }
        foreach (var p in e.EnumerateObject())
            if (!Keys.Contains(p.Name))
                throw new MapFileException($"{where}: unknown key \"{p.Name}\" (known: {string.Join(", ", Keys)})");
        var w = new WeatherSettings();
        foreach (var p in e.EnumerateObject())
        {
            if (p.Value.ValueKind != JsonValueKind.Number)
                throw new MapFileException($"{where}: \"{p.Name}\" is a number, got {p.Value}");
            var v = p.Value.GetDouble();
            switch (p.Name)
            {
                case "seed":
                    w.Seed = v is >= 0 and <= int.MaxValue && v == Math.Floor(v) ? (int)v : throw new MapFileException($"{where}: \"seed\" is a whole number from 0, got {v}");
                    break;
                case "latitude":
                    w.Latitude = v is >= -60 and <= 60 && Math.Abs(v) >= 5 ? (float)v : throw new MapFileException($"{where}: \"latitude\" is 5 to 60 degrees north or south (-5 to -60), got {v}");
                    break;
                case "size":
                    w.SizeKm = v is >= 200 and <= 8000 ? (float)v : throw new MapFileException($"{where}: \"size\" is 200 to 8000 km, got {v}");
                    break;
                case "cells":
                    w.Cells = v is >= 32 and <= 512 && v == Math.Floor(v) && ((int)v & 15) == 0 ? (int)v : throw new MapFileException($"{where}: \"cells\" is 32 to 512, a multiple of 16, got {v}");
                    break;
                case "speed":
                    w.Speed = v is >= 0 and <= 1440 ? (float)v : throw new MapFileException($"{where}: \"speed\" is 0 to 1440 minutes of weather per second, got {v}");
                    break;
            }
        }
        return w;
    }

    /// <summary>The "weather" value for these settings (only what differs from the defaults).</summary>
    public string ToJson()
    {
        var d = new WeatherSettings();
        var parts = new List<string>();
        if (Seed != d.Seed) parts.Add($"\"seed\": {Seed}");
        if (Latitude != d.Latitude) parts.Add($"\"latitude\": {N(Latitude)}");
        if (SizeKm != d.SizeKm) parts.Add($"\"size\": {N(SizeKm)}");
        if (Cells != d.Cells) parts.Add($"\"cells\": {Cells}");
        if (Speed != d.Speed) parts.Add($"\"speed\": {N(Speed)}");
        return parts.Count == 0 ? "true" : "{ " + string.Join(", ", parts) + " }";
    }

    private static string N(float f) => f.ToString("R", CultureInfo.InvariantCulture);
}
