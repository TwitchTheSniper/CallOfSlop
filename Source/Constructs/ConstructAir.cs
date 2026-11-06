using System.Numerics;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>The air's numbers (Constructs/Air.json), loaded once; the defaults when the file is missing or refused.</summary>
public sealed class AirTable
{
    public const string Format = "call-of-slop-air/1";
    public float ScaleHeight = 1200, SeaLevel;
    public float LiftHydrogen = 1.12f, LiftHelium = 1.04f, LiftHotAir = 0.31f, LiftVacuum = 1.225f;
    public float GasLitresPerM3 = 10;
    public float SoftFromM3 = 20000, SoftPower = 0.35f, SoftMost = 6;
    public float LeakSpeed = 6, HolePerDamage = 0.002f, HoleMost = 2, HoleCell = 40, IgniteDamage = 80;
    public float CoolShare = 0.008f, VentShare = 0.04f, VentHotShare = 0.12f, PumpShare = 0.02f, BurnShare = 0.25f;
    public float Drag = 0.6f, DragArm = 0.25f, SpinDrag = 0.5f, Turbulence = 0.012f, WindAloftAt = 800, NearMetres = 600;
    public float GroundEffect = 0.25f, GroundEffectSpans = 1;
    public string Problem = "";

    public static string FilePath => Path.Combine(AppFolders.Data, "Constructs", "Air.json");

    private static AirTable? _current;

    public static AirTable Current => _current ??= Load(FilePath);

    /// <summary>Sets the table in use (a test; null: load the file again).</summary>
    public static void Use(AirTable? t) => _current = t;

    public static AirTable Load(string path)
    {
        try
        {
            return Parse(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ConstructPartException or InvalidOperationException)
        {
            Console.WriteLine($"[constructs] {path}: {ex.Message}: the defaults stand");
            return new AirTable { Problem = ex.Message };
        }
    }

    public static AirTable Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (ConstructParts.Opt(root, "format")?.GetString() != Format)
            throw new ConstructPartException($"\"format\" must be \"{Format}\"");
        ConstructParts.Known(root, ["format", "about", "scaleHeight", "seaLevel", "lift", "gasLitresPerM3", "softFromM3", "softPower", "softMost", "leakSpeed", "holePerDamage", "holeMost", "holeCell", "igniteDamage", "coolShare", "ventShare", "ventHotShare", "pumpShare", "burnShare", "drag", "dragArm", "spinDrag", "turbulence", "windAloftAt", "nearMetres", "groundEffect", "groundEffectSpans"], "Air.json");
        var t = new AirTable();
        static float Num(JsonElement e, string key, float now, float lo, float hi)
        {
            if (ConstructParts.Opt(e, key) is not { } v)
                return now;
            var n = v.GetSingle();
            if (!(n >= lo && n <= hi))
                throw new ConstructPartException($"\"{key}\" is {lo} to {hi}");
            return n;
        }
        t.ScaleHeight = Num(root, "scaleHeight", t.ScaleHeight, 10, 100_000);
        t.SeaLevel = Num(root, "seaLevel", t.SeaLevel, -100_000, 100_000);
        if (ConstructParts.Opt(root, "lift") is { } lift)
        {
            ConstructParts.Known(lift, ["hydrogen", "helium", "hotAir", "vacuum"], "Air.json, lift");
            t.LiftHydrogen = Num(lift, "hydrogen", t.LiftHydrogen, 0, 100);
            t.LiftHelium = Num(lift, "helium", t.LiftHelium, 0, 100);
            t.LiftHotAir = Num(lift, "hotAir", t.LiftHotAir, 0, 100);
            t.LiftVacuum = Num(lift, "vacuum", t.LiftVacuum, 0, 100);
        }
        t.GasLitresPerM3 = Num(root, "gasLitresPerM3", t.GasLitresPerM3, 0.01f, 1000);
        t.SoftFromM3 = Num(root, "softFromM3", t.SoftFromM3, 1, 1e9f);
        t.SoftPower = Num(root, "softPower", t.SoftPower, 0, 2);
        t.SoftMost = Num(root, "softMost", t.SoftMost, 1, 1000);
        t.LeakSpeed = Num(root, "leakSpeed", t.LeakSpeed, 0, 1000);
        t.HolePerDamage = Num(root, "holePerDamage", t.HolePerDamage, 0, 10);
        t.HoleMost = Num(root, "holeMost", t.HoleMost, 0, 10000);
        t.HoleCell = Num(root, "holeCell", t.HoleCell, 0, 100000);
        t.IgniteDamage = Num(root, "igniteDamage", t.IgniteDamage, 0, 1e6f);
        t.CoolShare = Num(root, "coolShare", t.CoolShare, 0, 1);
        t.VentShare = Num(root, "ventShare", t.VentShare, 0, 1);
        t.VentHotShare = Num(root, "ventHotShare", t.VentHotShare, 0, 1);
        t.PumpShare = Num(root, "pumpShare", t.PumpShare, 0, 1);
        t.BurnShare = Num(root, "burnShare", t.BurnShare, 0, 1);
        t.Drag = Num(root, "drag", t.Drag, 0, 10);
        t.DragArm = Num(root, "dragArm", t.DragArm, 0, 1);
        t.SpinDrag = Num(root, "spinDrag", t.SpinDrag, 0, 100);
        t.Turbulence = Num(root, "turbulence", t.Turbulence, 0, 10);
        t.WindAloftAt = Num(root, "windAloftAt", t.WindAloftAt, 1, 1e6f);
        t.NearMetres = Num(root, "nearMetres", t.NearMetres, 0, 1e7f);
        t.GroundEffect = Num(root, "groundEffect", t.GroundEffect, 0, 10);
        t.GroundEffectSpans = Num(root, "groundEffectSpans", t.GroundEffectSpans, 0.01f, 100);
        return t;
    }
}

/// <summary>
/// The air (docs/sandbox/CONSTRUCTS.md, "Air and lift"): the one place flight reads it. How thick it is at a point (1 at sea
/// level, thinning with height; none off world: on a planetoid or in space there is no lift, only thrusters and anti-grav),
/// the wind there from the weather, and the hooks for what flies through it. Nothing here allocates.
/// </summary>
public static class ConstructAir
{
    /// <summary>The wind low down and at cloud height (m/s, world x and z), read from the weather four times a second; both zero with no weather. The server's own.</summary>
    public static Vector3 WindLow, WindHigh;
    private static bool _weatherWind;

    /// <summary>Is there air on this world at all? No on a planetoid or in curved gravity (the same test flying rounds use).</summary>
    public static bool HasAir => !(GravityFrames.Curved || World.Map.Planetoid != null);

    /// <summary>How thick the air is at a point: 1 at sea level, thinner with height, 0 off world.</summary>
    public static float Density(Vector3 p)
    {
        if (!HasAir)
            return 0;
        var t = AirTable.Current;
        return p.Y <= t.SeaLevel ? 1f : MathF.Exp((t.SeaLevel - p.Y) / t.ScaleHeight);
    }

    /// <summary>
    /// How much of the arcade air drag a flyer feels (the old <see cref="ConstructPhysics.AirAt"/>): a planetoid's sky keeps its
    /// thinning damping, so the climb out of the bubble flies as it did, though nothing floats there.
    /// </summary>
    public static float DragShare(Vector3 p) =>
        GravityFrames.Point is { } planet && World.Map.Planetoid is { } pl ? MathF.Exp(-MathF.Max(0, Vector3.Distance(p, planet.Center) - (float)pl.Radius) / ConstructPhysics.AirScale) : 1f;

    /// <summary>Reads the weather's wind (server, four times a second). Weather only runs on a map with a "weather" section.</summary>
    public static void ReadWeather()
    {
        if (!Weather.Active)
        {
            if (_weatherWind)
                WindLow = WindHigh = Vector3.Zero;
            _weatherWind = false;
            return;
        }
        _weatherWind = true;
        Weather.ReadWind(out var low, out var high);
        WindLow = new Vector3(low.X, 0, low.Y);
        WindHigh = new Vector3(high.X, 0, high.Y);
    }

    /// <summary>The wind at a point (m/s, world axes): the weather's low wind, turning into its cloud-level wind with height. None off world.</summary>
    public static Vector3 Wind(Vector3 p)
    {
        if (!HasAir)
            return Vector3.Zero;
        var t = AirTable.Current;
        return Vector3.Lerp(WindLow, WindHigh, Math.Clamp((p.Y - t.SeaLevel) / t.WindAloftAt, 0, 1));
    }

    /// <summary>A hook for lifting surfaces: how much more they lift this far over the ground, for this span (1: none).</summary>
    public static float GroundEffect(float height, float span)
    {
        var t = AirTable.Current;
        return 1 + t.GroundEffect * MathF.Max(0, 1 - MathF.Max(0, height) / MathF.Max(0.1f, span * t.GroundEffectSpans));
    }

    /// <summary>The gusts at a point (m/s2): smooth noise in space and time, stronger with the wind. The same at the same point and tick.</summary>
    public static Vector3 Turbulence(Vector3 p, uint tick, float windSpeed)
    {
        var a = AirTable.Current.Turbulence * windSpeed;
        if (a <= 0)
            return Vector3.Zero;
        var w = tick * 0.021f;
        return a * new Vector3(MathF.Sin(p.Z * 0.031f + w) + MathF.Sin(p.Y * 0.053f + w * 1.7f), 0.5f * (MathF.Sin(p.X * 0.041f - w * 1.3f) + MathF.Sin(p.Z * 0.027f + w * 0.6f)), MathF.Sin(p.X * 0.029f + w * 0.8f) + MathF.Sin(p.Y * 0.047f - w * 1.1f));
    }
}
