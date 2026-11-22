using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Where a planetoid's plants stand (docs/planets/BIOMES.md): a fixed function of the world, never stored or sent. Each
/// layer of plants has a lattice of sample points on the six faces' grids (one point per <see cref="Spacing"/> metres, jittered
/// by its hash); a point takes a plant from the biomes under it by their weights and each plant's density, or none.
/// The drawing (FoliageRendererPlanet.cs) makes whole cells of a layer on worker threads; the trunk question
/// (<see cref="GatherTrunks"/>, for constructs near trees) asks only the points it needs, through a small cache.
/// Everything here reads the features' height (<see cref="Planetoid.HeightAt"/>), so any thread may ask.
/// </summary>
public sealed class PlanetFlora
{
    /// <summary>Layers: ground cover, brush, trees and big rocks.</summary>
    public const int Layers = 3;

    /// <summary>Metres between a layer's sample points.</summary>
    public static readonly float[] Spacing = [0.9f, 1.8f, 4.5f];

    /// <summary>A layer's drawing cell, in metres (rounded to a power of two of the world's grid cells).</summary>
    private static readonly float[] CellMetres = [64f, 150f, 300f];

    /// <summary>Which layer a plant grows in (-1: not a planet plant).</summary>
    public static int LayerOf(FoliageType t) => t switch
    {
        FoliageType.Grass or FoliageType.Heather or FoliageType.Scree => 0,
        FoliageType.Shrub or FoliageType.Bracken or FoliageType.Reed or FoliageType.Bush => 1,
        FoliageType.Tree or FoliageType.Pine or FoliageType.Birch or FoliageType.Spruce or FoliageType.ScotsPine or FoliageType.Boulder => 2,
        _ => -1,
    };

    /// <summary>The thickest a plant of a layer may grow (one a sample point).</summary>
    public static float MaxDensity(int layer) => layer < 0 ? 0 : 1f / (Spacing[layer] * Spacing[layer]);

    /// <summary>A planet tree's trunk (a <see cref="FoliageType.Stem"/>) as a share of the tree's height.</summary>
    public static float StemShare(FoliageType t) => t switch
    {
        FoliageType.ScotsPine => 0.86f,
        FoliageType.Spruce or FoliageType.Pine => 0.55f,
        FoliageType.Birch => 0.7f,
        _ => 0.66f,
    };

    /// <summary>How far plants are sunk into the ground (slopes leave no gap under them).</summary>
    private static float Sink(FoliageType t) => Foliage.IsTree(t) ? 0.35f : t == FoliageType.Boulder ? 0f : 0.06f;

    public readonly Planetoid Planet;
    public readonly PlanetBiomes Biomes;

    /// <summary>Per layer: grid cells a drawing cell spans (a power of two) and how many sample points a grid cell holds across.</summary>
    public readonly int[] CellGrid = new int[Layers];

    private readonly double[] _step = new double[Layers]; // grid cells per sample point
    private readonly float[] _layerMax = new float[Layers]; // the most density any one place's biomes can add up to, per layer
    private readonly (Vector3 Dir, float Cos)[] _clear;
    private readonly double _cell;

    public PlanetFlora(Planetoid planet, PlanetBiomes biomes)
    {
        Planet = planet;
        Biomes = biomes;
        _cell = planet.Cell;
        for (var l = 0; l < Layers; l++)
        {
            var g = (int)Math.Pow(2, Math.Round(Math.Log2(CellMetres[l] / _cell)));
            CellGrid[l] = Math.Clamp(g, 8, planet.N);
            _step[l] = Spacing[l] / _cell;
        }
        foreach (var b in biomes.List)
        {
            Span<float> sum = stackalloc float[Layers];
            foreach (var p in b.Plants)
                sum[LayerOf(p.Type)] += p.Density;
            for (var l = 0; l < Layers; l++)
                _layerMax[l] = MathF.Max(_layerMax[l], sum[l]);
        }
        var clear = new List<(Vector3, float)>();
        if (biomes.ClearFlats)
            foreach (var f in planet.Features)
                if (f.Type == "flatten")
                {
                    var lat = f.Num("lat", 0) * Math.PI / 180;
                    var lon = f.Num("lon", 0) * Math.PI / 180;
                    var dir = new Vector3((float)(Math.Cos(lat) * Math.Sin(lon)), (float)Math.Sin(lat), (float)(Math.Cos(lat) * Math.Cos(lon)));
                    var reach = (f.Num("radius", 50) + f.Num("edge", 20) * 0.5) / planet.Radius;
                    clear.Add((dir, (float)Math.Cos(reach)));
                }
        _clear = clear.ToArray();
    }

    /// <summary>Does this layer grow anything anywhere?</summary>
    public bool Grows(int layer) => _layerMax[layer] > 0;

    /// <summary>A plant at a sample point.</summary>
    public struct Plant
    {
        public FoliageType Type;
        public Vector3 Pos;   // its foot, a little sunk into the ground
        public Vector3 Up;
        public float Scale;   // trees: height in metres; others: a multiplier
        public float Yaw01;
        public float Rand;    // 0..1, for the quality filter
        public Vector3 Color, Bark;
    }

    /// <summary>Sample points along a face's grid in a layer.</summary>
    public int PointsAcross(int layer) => (int)Math.Ceiling(Planet.N / _step[layer]);

    /// <summary>The range of sample points (a0..a1, end excluded) whose base lies in drawing cell <paramref name="c"/> of a layer.</summary>
    public (int A0, int A1) PointsInCell(int layer, int c)
    {
        var g = CellGrid[layer];
        var s = _step[layer];
        int a0 = (int)Math.Ceiling(c * g / s), a1 = (int)Math.Ceiling((c + 1) * g / s);
        return (a0, Math.Min(a1, PointsAcross(layer)));
    }

    [ThreadStatic] private static float[]? _w;

    /// <summary>
    /// What grows at sample point (a, b) of a layer on a face, if anything. Deterministic, any thread when
    /// <paramref name="dug"/> is false; with it (the simulation's thread only: the dug ground is changed there) a point on
    /// dug ground grows nothing. The drawing's workers leave it out and hide dug plants as they upload (<see cref="IsDug"/>).
    /// </summary>
    public bool At(int layer, int face, int a, int b, out Plant plant, bool dug = false)
    {
        plant = default;
        var max = _layerMax[layer];
        if (max <= 0)
            return false;
        var key = Foliage.Hash((uint)(Biomes.Seed * 7919) ^ Foliage.Hash((uint)(face * 3 + layer) ^ Foliage.Hash((uint)a * 0x9E3779B1u ^ Foliage.Hash((uint)b))));
        var r = Unit(key);
        var area = Spacing[layer] * Spacing[layer];
        if (r >= max * area * (1 + Biomes.Clumps * (PlanetBiomes.MaxVigour - 1)))
            return false; // (nothing anywhere grows this thick: the cheap answer, before any height is asked)
        var k2 = Foliage.Hash(key + 1);
        var k3 = Foliage.Hash(key + 2);
        var s = _step[layer];
        var n = Planet.N;
        double fi = (a + Unit(k2)) * s, fj = (b + Unit(k3)) * s;
        if (fi >= n || fj >= n)
            return false;
        var pl = Planet;
        pl.DirOf(face, fi, fj, out var x, out var y, out var z);
        var dir = new Vector3((float)x, (float)y, (float)z);
        foreach (var (cd, cos) in _clear)
            if (Vector3.Dot(cd, dir) > cos)
                return false;
        if (dug && pl.IsDugAt(dir))
            return false;
        var h = pl.HeightAt(x, y, z);
        // The slope, across one grid cell each way.
        double e = 0.7;
        pl.DirOf(face, fi + e, fj, out var x1, out var y1, out var z1);
        pl.DirOf(face, fi, fj + e, out var x2, out var y2, out var z2);
        double h1 = pl.HeightAt(x1, y1, z1), h2 = pl.HeightAt(x2, y2, z2);
        double rad = pl.Radius;
        var p0 = new Vector3((float)(x * (rad + h)), (float)(y * (rad + h)), (float)(z * (rad + h)));
        var p1 = new Vector3((float)(x1 * (rad + h1)), (float)(y1 * (rad + h1)), (float)(z1 * (rad + h1)));
        var p2 = new Vector3((float)(x2 * (rad + h2)), (float)(y2 * (rad + h2)), (float)(z2 * (rad + h2)));
        var nrm = Vector3.Cross(p2 - p0, p1 - p0);
        var nl = nrm.Length();
        var cosUp = nl > 1e-9f ? MathF.Abs(Vector3.Dot(nrm / nl, dir)) : 1f;
        var slope = MathF.Acos(Math.Clamp(cosUp, -1f, 1f)) * (180f / MathF.PI);
        var w = _w ??= new float[PlanetBiomes.MaxBiomes];
        if (Biomes.Weights(x, y, z, rad, (float)h, slope, w) <= 0)
            return false;
        var list = Biomes.List;
        var vigour = Biomes.Vigour(x, y, z, rad); // (thickets and clearings, shared by every plant)
        if (vigour <= 0.01f)
            return false;
        var acc = 0f;
        for (var bi = 0; bi < list.Count; bi++)
        {
            if (w[bi] <= 0)
                continue;
            foreach (var p in list[bi].Plants)
            {
                if (LayerOf(p.Type) != layer || slope > p.Slope)
                    continue;
                var d = p.Density;
                if (p.Patch > 0)
                {
                    var nz = Planetoid.Noise3(x * rad / p.PatchScale, y * rad / p.PatchScale, z * rad / p.PatchScale, p.Seed + 3001);
                    d *= Math.Clamp(1 - p.Patch + p.Patch * (float)(nz * 2.4 + 1.0), 0f, 2.5f); // (about 0 to 2.2: clumps of this plant, bare between)
                }
                acc += w[bi] * d * vigour * area;
                if (r >= acc)
                    continue;
                var k4 = Foliage.Hash(key + 3);
                var k5 = Foliage.Hash(key + 4);
                var size = p.SizeMin + (p.SizeMax - p.SizeMin) * Unit(k4);
                var tint = 1f + p.Jitter * (Unit(k5) * 2 - 1);
                var hue = 1f + p.Jitter * 0.6f * (Unit(Foliage.Hash(key + 5)) * 2 - 1);
                plant.Type = p.Type;
                plant.Up = dir;
                plant.Pos = new Vector3((float)(pl.Cx + x * (rad + h)), (float)(pl.Cy + y * (rad + h)), (float)(pl.Cz + z * (rad + h))) - dir * (Sink(p.Type) * (Foliage.IsTree(p.Type) ? 1 : size));
                plant.Scale = size;
                plant.Yaw01 = Unit(Foliage.Hash(key + 6));
                plant.Rand = Unit(Foliage.Hash(key + 7));
                plant.Color = new Vector3(p.Color.X * tint * hue, p.Color.Y * tint, p.Color.Z * tint / hue);
                plant.Bark = p.Bark * (0.85f + 0.3f * Unit(Foliage.Hash(key + 8)));
                return true;
            }
        }
        return false;
    }

    private static float Unit(uint h) => (h >> 8) * (1f / 16777216f);

    /// <summary>Is a plant's foot on dug ground? (The simulation's thread only.)</summary>
    public bool IsDug(Vector3 foot)
    {
        var rel = foot - Planet.Center;
        var len = rel.Length();
        return len > 1 && Planet.IsDugAt(rel / len);
    }

    // ---- Trunks for constructs ------------------------------------------------------------------------------------

    /// <summary>The tallest tree a planet grows (metres): sizes are capped at 40 by the map file.</summary>
    public const float MaxTreeHeight = 40;

    // A direct-mapped cache of the tree layer's points (key, then whether a tree stands there and its plant): no
    // allocation, a fixed size, and a miss only costs the one point. Cleared when the dug ground changes.
    private const int CacheBits = 14;
    private readonly long[] _trunkKey = new long[1 << CacheBits];
    private readonly Plant[] _trunkPlant = new Plant[1 << CacheBits];
    private readonly bool[] _trunkTree = new bool[1 << CacheBits];
    private int _trunkDug = -1;
    private bool _bigLogged;

    /// <summary>The most tree-layer points one question asks (about 90 m across): a bigger box asks none, once logged.</summary>
    private const int MaxPoints = 400;

    /// <summary>
    /// The solid trunks of the trees whose points lie round <paramref name="min"/>..<paramref name="max"/>, as boxes standing
    /// along "up" (<see cref="ShapeKind.Trunk"/>). Sean (2026-11-22): trees are solid only near constructs, so only the
    /// construct contacts ask this (the simulation's thread); people walk through them. One face's points only (a box over
    /// a cube edge misses the far face's trees), and a group more than about 90 m across meets no trees (logged once).
    /// </summary>
    public void GatherTrunks(Vector3 min, Vector3 max, List<ContactShape> into)
    {
        if (!Grows(2))
            return;
        var pl = Planet;
        var mid = (min + max) * 0.5f;
        var half = (max - min).Length() * 0.5f;
        if (pl.RoughAltitude(mid) > half + MaxTreeHeight)
            return;
        var rel = mid - pl.Center;
        var len = rel.Length();
        if (len < 1)
            return;
        if (pl.DugVersion != _trunkDug)
        {
            Array.Clear(_trunkKey); // (keys are stored + 1: 0 is empty)
            _trunkDug = pl.DugVersion;
        }
        pl.Locate(rel.X / len, rel.Y / len, rel.Z / len, out var face, out var fi, out var fj);
        var s = _step[2];
        var reach = (half + 1.5) / _cell / s; // (in points; a trunk is at most about 1 m thick)
        int a0 = (int)Math.Floor(fi / s - reach), a1 = (int)Math.Floor(fi / s + reach);
        int b0 = (int)Math.Floor(fj / s - reach), b1 = (int)Math.Floor(fj / s + reach);
        if ((long)(a1 - a0 + 1) * (b1 - b0 + 1) > MaxPoints)
        {
            if (!_bigLogged)
                Console.WriteLine($"[flora] a construct group {half * 2:0} m across meets no planet trees (at most about {Math.Sqrt(MaxPoints) * Spacing[2]:0} m)");
            _bigLogged = true;
            return;
        }
        var across = PointsAcross(2);
        for (var b = Math.Max(b0, 0); b <= Math.Min(b1, across - 1); b++)
        for (var a = Math.Max(a0, 0); a <= Math.Min(a1, across - 1); a++)
        {
            var key = ((long)face << 56) | ((long)a << 28) | (uint)b;
            var slot = (int)(Foliage.Hash((uint)key ^ Foliage.Hash((uint)(key >> 28))) & ((1 << CacheBits) - 1));
            if (_trunkKey[slot] != key + 1)
            {
                _trunkTree[slot] = At(2, face, a, b, out _trunkPlant[slot], dug: true) && Foliage.IsTree(_trunkPlant[slot].Type);
                _trunkKey[slot] = key + 1;
            }
            if (!_trunkTree[slot])
                continue;
            var box = TrunkBox(_trunkPlant[slot]);
            box.Bounds(out var lo, out var hi);
            if (hi.X < min.X || lo.X > max.X || hi.Y < min.Y || lo.Y > max.Y || hi.Z < min.Z || lo.Z > max.Z)
                continue;
            into.Add(ContactShape.FromObb(box, new ShapeId(ShapeKind.Trunk, (int)(key & 0x7FFFFFFF), face)));
        }
    }

    /// <summary>A tree's trunk as a box: as tall as its stem, as thick as the stem's middle.</summary>
    public static Obb TrunkBox(in Plant p)
    {
        var up = p.Up;
        var ex = Vector3.Normalize(Vector3.Cross(MathF.Abs(up.Y) < 0.95f ? Vector3.UnitY : Vector3.UnitX, up));
        var ez = Vector3.Cross(ex, up);
        var stem = p.Scale * StemShare(p.Type);
        var r = stem * 0.024f;
        return new Obb(p.Pos + up * (stem * 0.5f), new Vector3(r, stem * 0.5f, r), ex, up, ez);
    }
}
