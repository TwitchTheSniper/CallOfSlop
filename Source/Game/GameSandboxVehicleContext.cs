using System.Numerics;
using CallOfSlop.VehicleKit;

namespace CallOfSlop;

/// <summary>
/// The server's reading of a spawn point for the vehicle kit (docs/vehiclekit/VEHICLEKIT.md, "Spawn context"): what the ground is, the gravity and the air there,
/// what the place is rich in, the rain. Cheap queries only, a handful per request (never per tick): five ground samples through <see cref="TerrainData"/> (so dug
/// and streamed ground read right), the resource field's deposit under each, gravity and air at the point, and the weather's rain. Reads the world that is
/// entered (the server's, as its Act handlers run).
/// </summary>
public static class SpawnContexts
{
    /// <summary>How far round the point the ground is sampled (m), and how many samples (the point and four round it).</summary>
    public const float Spread = 8f;

    private static readonly Vector2[] Offsets = [new(0, 0), new(Spread, 0), new(-Spread, 0), new(0, Spread), new(0, -Spread)];

    private static readonly HashSet<string> Ores = new(StringComparer.OrdinalIgnoreCase) { "iron ore", "copper ore", "gold ore", "levite ore" };

    /// <summary>The context at a spawn point on the ground.</summary>
    public static SpawnContext At(Vector3 p)
    {
        var map = World.Map;
        var g = GravityFrames.GravityAt(p).Length() / World.Gravity;
        var air = ConstructAir.Density(p);
        var rain = Weather.RainHere(); // (the weather's own reading is at the player, who is near: the parts menu sets a vehicle down in front of them)
        float wood = 0, ore = 0;
        if (map.Resources is { } field)
            foreach (var o in Offsets)
            {
                if (field.KindAt(p.X + o.X, p.Z + o.Y, true) is { Surface: true })
                    wood += 1f / Offsets.Length;
                if (field.KindAt(p.X + o.X, p.Z + o.Y, false) is { } k && Ores.Contains(k.Material))
                    ore += 1f / Offsets.Length;
            }
        var (biome, read) = map.Terrain is { } t && map.Planetoid == null ? Ground(t, p, rain, wood) : (Biome.Neutral, map.Planetoid != null ? "a planetoid: no ground reading" : "no terrain");
        return new SpawnContext { Biome = biome, Gravity = MathF.Round(g, 2), Air = MathF.Round(air, 2), Wood = wood, Ore = ore, Rain = MathF.Round(rain, 2), Read = read };
    }

    /// <summary>
    /// The biome from the ground: snow over the surface's snow line; desert on sand (its material, or under the sand line); a swamp in the low ground in rain; a
    /// forest in a wood, or rough rock and steep ground; a road on flat bare dirt; else neutral. The first that holds, in that order.
    /// </summary>
    private static (Biome, string) Ground(TerrainData t, Vector3 p, float rain, float wood)
    {
        int sand = 0, rock = 0, dirt = 0, steep = 0;
        float lo = float.MaxValue, hi = float.MinValue, slope = 0;
        foreach (var o in Offsets)
        {
            var (x, z) = (p.X + o.X, p.Z + o.Y);
            var h = t.Sample(x, z);
            (lo, hi) = (MathF.Min(lo, h), MathF.Max(hi, h));
            var n = t.SampleNormal(x, z);
            var deg = MathF.Acos(Math.Clamp(n.Y, -1, 1)) * 180 / MathF.PI;
            slope = MathF.Max(slope, deg);
            if (deg > 15)
                steep++;
            t.Locate(x, z, out var i, out var j, out var tx, out var tz);
            var m = (TerrainMaterial)t.Material(Math.Min(t.CountX - 1, i + (tx > 0.5f ? 1 : 0)), Math.Min(t.CountZ - 1, j + (tz > 0.5f ? 1 : 0)));
            if (m == TerrainMaterial.Sand || t.Surface.SandBelow is { } sb && h <= sb)
                sand++;
            else if (m == TerrainMaterial.Rock)
                rock++;
            else if (m == TerrainMaterial.Dirt)
                dirt++;
        }
        var y = t.Sample(p.X, p.Z);
        if (t.Surface.SnowAbove is { } line && y >= line - 20)
            return (Biome.Snow, $"{y - line:0} m against the snow line");
        if (sand >= 3)
            return (Biome.Desert, $"sand under {sand} of {Offsets.Length} samples");
        if (rain >= 0.4f && y <= t.LowestGround + 4 && slope < 8)
            return (Biome.Swamp, $"low ground ({y - t.LowestGround:0.0} m over the lowest) in rain {rain:0.00}");
        if (wood >= 0.6f)
            return (Biome.Forest, $"a forest under {wood * Offsets.Length:0} of {Offsets.Length} samples");
        if (rock >= 3 || steep >= 3)
            return (Biome.Forest, $"rough ground: rock under {rock}, steep under {steep} of {Offsets.Length} samples");
        if (dirt >= 3 && slope < 3 && hi - lo < 0.4f)
            return (Biome.Road, $"flat dirt under {dirt} of {Offsets.Length} samples");
        return (Biome.Neutral, $"grass and the like (slope {slope:0} deg)");
    }
}
