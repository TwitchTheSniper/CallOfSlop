namespace CallOfSlop;

/// <summary>
/// The surface materials gameplay rules are keyed by (bullet penetration, Materials/penetration.json; later building
/// destruction too). The names are the impact surface names <see cref="ImpactDecals.SurfaceOf"/> returns (concrete, brick,
/// wood, metal, plaster, tiles, asphalt, dirt, grass, sand, rock, snow, grid, default), plus a few that only props have
/// (glass, soft for cardboard and plastic, hedgerow for the Bocage earth banks).
/// </summary>
public enum MaterialKey : byte
{
    Default,
    Grid,
    Concrete,
    Brick,
    Wood,
    Metal,
    Tiles,
    Dirt,
    Asphalt,
    Plaster,
    Grass,
    Sand,
    Snow,
    Rock,
    Glass,
    Soft,
    Hedgerow,
    Count,
}

/// <summary>Surface name to <see cref="MaterialKey"/> and back (case-insensitive, allocation-free lookups).</summary>
public static class MaterialKeys
{
    /// <summary>Every key's name, by key.</summary>
    public static readonly string[] Names = Enumerable.Range(0, (int)MaterialKey.Count).Select(i => ((MaterialKey)i).ToString()).ToArray();

    private static readonly Dictionary<string, MaterialKey> ByName = BuildLookup();

    private static Dictionary<string, MaterialKey> BuildLookup()
    {
        var d = new Dictionary<string, MaterialKey>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < (int)MaterialKey.Count; i++)
            d[((MaterialKey)i).ToString()] = (MaterialKey)i;
        d["Terrain"] = MaterialKey.Dirt; // the decal library's generic terrain impact
        return d;
    }

    /// <summary>The key of a surface name, or false for a name that isn't one.</summary>
    public static bool TryParse(string? name, out MaterialKey key)
    {
        if (name != null && ByName.TryGetValue(name, out key))
            return true;
        key = MaterialKey.Default;
        return false;
    }

    /// <summary>The key of a surface name (<see cref="MaterialKey.Default"/> for anything unknown).</summary>
    public static MaterialKey Of(string? name) => TryParse(name, out var k) ? k : MaterialKey.Default;

    /// <summary>The key of a box texture as drawn (before <see cref="ImpactDecals.Refine"/> looks at colour and facing).</summary>
    public static MaterialKey Of(Material m) => m switch
    {
        Material.Grid => MaterialKey.Grid,
        Material.Concrete => MaterialKey.Concrete,
        Material.Brick => MaterialKey.Brick,
        Material.Wood => MaterialKey.Wood,
        Material.Metal => MaterialKey.Metal,
        Material.Tiles => MaterialKey.Tiles,
        Material.Dirt => MaterialKey.Dirt,
        _ => MaterialKey.Default,
    };

    /// <summary>The name of a key.</summary>
    public static string Name(MaterialKey k) => (uint)k < (uint)Names.Length ? Names[(int)k] : "Default";
}
