namespace CallOfSlop;

/// <summary>
/// A floating island (the terrain's "island" feature): a lump of ground torn out of the terrain and hung in the air, its
/// flat top <see cref="Top"/> metres up and the hole it left below it the same shape. It has no heights of its own: the
/// dug-ground columns (<see cref="TerrainEdits"/>) make it, each column inside its ragged outline keeping the ground under
/// the hole and gaining a second span, the island. So walking, rays, grenades, digging and drawing all just work on it.
/// Only arithmetic and square roots (no trigonometry), so every peer builds the same island.
/// </summary>
public sealed class TerrainIsland
{
    /// <summary>Centre (metres).</summary>
    public double X, Z;

    /// <summary>Radius of the outline before its noise (metres).</summary>
    public double R;

    /// <summary>Height of the island's flat top (metres, absolute).</summary>
    public double Top;

    /// <summary>Thickness at the middle (metres): the underside hangs down to a rough point.</summary>
    public double Depth;

    public int Seed;

    /// <summary>The ragged outline wanders up to this share of <see cref="R"/> either way.</summary>
    public const double Ragged = 0.22;

    /// <summary>The thinnest the island gets at its rim (metres).</summary>
    public const double RimThickness = 1.5;

    /// <summary>Furthest a column of it can be from the centre.</summary>
    public double Reach => R * (1 + Ragged) + 1;

    /// <summary>The islands of a terrain's "island" features (streamed terrains only: TerrainSpec refuses them elsewhere).</summary>
    public static TerrainIsland[] Of(TerrainData t)
    {
        if (t.Stream is not { } s)
            return [];
        var list = new List<TerrainIsland>();
        foreach (var f in s.Spec.Features)
            if (f.Type == "island")
                list.Add(new TerrainIsland
                {
                    X = f.Num("x", 0), Z = f.Num("z", 0), R = f.Num("radius", 40), Top = f.Num("top", 100),
                    Depth = f.Num("depth", f.Num("radius", 40) * 0.8), Seed = (int)f.Num("seed", 1),
                });
        return list.ToArray();
    }

    /// <summary>How far out (x, z) is: 0 at the centre, 1 on the ragged outline, more outside.</summary>
    public double Out(double x, double z)
    {
        var d = Math.Sqrt((x - X) * (x - X) + (z - Z) * (z - Z));
        var edge = 1 + Ragged * TerrainGen.ValueNoise(x / (R * 0.4), z / (R * 0.4), Seed + 71);
        return d / (R * edge);
    }

    /// <summary>The island's thickness over (x, z) (0 outside it): a rim, then deeper towards a craggy point in the middle.</summary>
    public double Thickness(double x, double z)
    {
        var u = Out(x, z);
        if (u >= 1)
            return 0;
        var t = 1 - u;
        var crag = 0.8 + 0.25 * TerrainGen.ValueNoise(x / 7, z / 7, Seed + 5) + 0.3 * Math.Max(0, TerrainGen.ValueNoise(x / 15, z / 15, Seed + 9));
        return RimThickness + Depth * t * Math.Sqrt(t) * crag;
    }

    /// <summary>The island's top over (x, z): flat to its rim (a rounded rim drew as terraces in 0.5 m columns).</summary>
    public double TopAt(double x, double z) => Top;

    /// <summary>A height below every hole floor it can dig (for the bedrock).</summary>
    public double LowestFloor(TerrainData t) => t.BaseGroundTop((float)X, (float)Z) - RimThickness - Depth * 1.4 - 60;
}
