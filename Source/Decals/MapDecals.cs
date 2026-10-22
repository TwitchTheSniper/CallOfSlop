using System.Numerics;

namespace CallOfSlop;

/// <summary>Which way a Decal or DecalScatter op projects (see <see cref="Map.Decal"/>).</summary>
public enum DecalOnto : byte
{
    Floor,
    Ceiling,
    WallPosX,
    WallNegX,
    WallPosZ,
    WallNegZ,

    /// <summary>The nearest wall in any of the four directions.</summary>
    Wall,
}

/// <summary>
/// One Decal or DecalScatter op as recorded on a map. Decals are resolved and projected later, against the finished map
/// (every box, building, terrain and mesh is known by then): see <see cref="DecalBaker"/>.
/// </summary>
public sealed record DecalRequest(
    string[] Names, bool Scatter, float X0, float Z0, float X1, float Z1, float Y, bool OnGround, DecalOnto Onto,
    int Count, int Seed, float Size, float MaxSize, float Rotation, Vector3 Tint, float Reach, float Gap, int OpIndex);

/// <summary>
/// Decals on a map: the Decal and DecalScatter ops. They are cosmetic: no collision and no part of the map's
/// fingerprint (<see cref="ContentHash"/>), so adding or moving decals never stops older clients joining.
/// </summary>
public sealed partial class Map
{
    /// <summary>The most decals one map may ask for (single decals plus every scatter's count).</summary>
    public const int MaxDecals = 6000;

    /// <summary>Every Decal and DecalScatter op, in op order.</summary>
    public readonly List<DecalRequest> DecalRequests = new();

    /// <summary>The decals this map asks for (single ones plus every scatter's count).</summary>
    public int DecalCount => DecalRequests.Sum(r => r.Scatter ? r.Count : 1);

    /// <summary>
    /// A decal from the Decals folder, projected onto the surface found from (x, y, z) looking <paramref name="onto"/>:
    /// "floor" (down, from 1 m above y), "ceiling" (up), "wall+x", "wall-x", "wall+z", "wall-z" (towards that side) or
    /// "wall" (the nearest wall round the point). On terrain maps y is above the ground at (x, z). It wraps over edges
    /// and steps within its depth, never over terrain holes. <paramref name="size"/> metres across (0: the decal's own
    /// size), <paramref name="rotation"/> degrees (-1: the decal's rule, usually random), <paramref name="tint"/> a colour
    /// multiplied in ("" none), <paramref name="reach"/> how far to look for the surface. Cosmetic: no collision.
    /// </summary>
    [MapOp]
    public void Decal(string decal, float x, float z, float y = 0, string onto = "floor", float size = 0, float rotation = -1, string tint = "", float reach = 3, int seed = 0)
    {
        using var __ = Op([decal, x, z, y, onto, size, rotation, tint, reach, seed]);
        CheckDecalNames([decal]);
        var where = ParseOnto(onto);
        CheckDecalNumbers(size, size, reach, 1);
        var t = ParseTint(tint);
        var ground = HasTerrain && _topAnchor != OpAnchor.World;
        AddDecalRequest(new DecalRequest([decal], false, x, z, x, z, y, ground, where, 1, seed, size, size, rotation, t, reach, 0, Ops.Count - 1));
    }

    /// <summary>
    /// Scatters <paramref name="count"/> decals from the list (repeat a name to make it more common) over the rectangle
    /// (x0, z0)-(x1, z1), projected <paramref name="onto"/> like <see cref="Decal"/> (walls: at random heights from
    /// y + 0.3 to y + 2.4 m, onto the nearest wall within 3 m). Sizes are random between minSize and maxSize (0: each
    /// decal's own), at least <paramref name="gap"/> metres apart. Deterministic for a seed; a decal only lands on
    /// surfaces its file allows.
    /// </summary>
    [MapOp]
    public void DecalScatter(string[] decals, float x0, float z0, float x1, float z1, int count, int seed = 0, string onto = "floor", float y = 0,
        float minSize = 0, float maxSize = 0, string tint = "", float gap = 0)
    {
        using var __ = Op([decals, x0, z0, x1, z1, count, seed, onto, y, minSize, maxSize, tint, gap]);
        CheckDecalNames(decals);
        var where = ParseOnto(onto);
        if (count is < 0 or > 2000)
            throw new InvalidOperationException("count is 0 to 2000");
        if (gap is < 0 or > 20)
            throw new InvalidOperationException("gap is 0 to 20");
        CheckDecalNumbers(minSize, maxSize, 3, count);
        if (maxSize < minSize)
            throw new InvalidOperationException("maxSize must be at least minSize");
        var t = ParseTint(tint);
        var ground = HasTerrain && _topAnchor != OpAnchor.World;
        AddDecalRequest(new DecalRequest(decals, true, MathF.Min(x0, x1), MathF.Min(z0, z1), MathF.Max(x0, x1), MathF.Max(z0, z1), y, ground, where,
            count, seed, minSize, maxSize, -1, t, 3, gap, Ops.Count - 1));
    }

    private void AddDecalRequest(DecalRequest r)
    {
        if (DecalCount + (r.Scatter ? r.Count : 1) > MaxDecals)
            throw new InvalidOperationException($"too many decals (the limit is {MaxDecals} per map)");
        DecalRequests.Add(r);
        _decalMesh = null;
    }

    private static void CheckDecalNames(string[] names)
    {
        if (names.Length == 0)
            throw new InvalidOperationException("\"decals\" needs at least one decal name");
        foreach (var n in names)
            if (CallOfSlop.Decals.Find(n) == null)
                throw new InvalidOperationException($"unknown decal \"{n}\": {CallOfSlop.Decals.Suggest(n)}");
    }

    private static void CheckDecalNumbers(float minSize, float maxSize, float reach, int count)
    {
        if (minSize < 0 || maxSize > DecalFile.MaxSize || !float.IsFinite(minSize + maxSize))
            throw new InvalidOperationException($"size is 0 (the decal's own) to {DecalFile.MaxSize} m");
        if (reach is < 0.1f or > 50f || !float.IsFinite(reach))
            throw new InvalidOperationException("reach is 0.1 to 50 m");
        _ = count;
    }

    /// <summary>"floor", "ceiling", "wall", "wall+x", "wall-x", "wall+z" or "wall-z".</summary>
    public static DecalOnto ParseOnto(string onto) => onto.Trim().ToLowerInvariant() switch
    {
        "floor" or "ground" or "down" => DecalOnto.Floor,
        "ceiling" or "up" => DecalOnto.Ceiling,
        "wall" or "walls" => DecalOnto.Wall,
        "wall+x" or "+x" => DecalOnto.WallPosX,
        "wall-x" or "-x" => DecalOnto.WallNegX,
        "wall+z" or "+z" => DecalOnto.WallPosZ,
        "wall-z" or "-z" => DecalOnto.WallNegZ,
        var other => throw new InvalidOperationException($"\"onto\" is floor, ceiling, wall, wall+x, wall-x, wall+z or wall-z; got \"{other}\""),
    };

    private static Vector3 ParseTint(string tint)
    {
        if (string.IsNullOrWhiteSpace(tint))
            return Vector3.One;
        var t = tint.Trim();
        if (PropFile.Colors.TryGetValue(t, out var named))
            return named;
        if (t.Length == 7 && t[0] == '#' && int.TryParse(t[1..], System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var rgb))
            return new Vector3(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f);
        throw new InvalidOperationException($"\"tint\" is a colour name or \"#rrggbb\"; got \"{tint}\"");
    }

    private DecalMesh? _decalMesh;
    private int _decalGeneration = -1;

    /// <summary>
    /// The map's decals projected onto its surfaces (built on first use, again after the decal library reloads).
    /// CPU only; the renderer uploads it once per map.
    /// </summary>
    public DecalMesh DecalMesh
    {
        get
        {
            if (_decalMesh == null || _decalGeneration != CallOfSlop.Decals.Generation)
            {
                _decalGeneration = CallOfSlop.Decals.Generation;
                _decalMesh = DecalBaker.Bake(this);
            }
            return _decalMesh;
        }
    }

    /// <summary>Is box <paramref name="i"/> a surface decals may land on (drawn boxes, buildings, Zombies doors; not props, glass or window openings)?</summary>
    public bool IsDecalSurface(int i) => (uint)i < (uint)Solid.Count && !IsPropCollider(i) && !IsClipBox(i);
}
