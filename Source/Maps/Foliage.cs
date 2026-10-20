using System.Numerics;

namespace CallOfSlop;

/// <summary>The kinds of plant a map's "foliage" section can place (see Maps/README.md, "Foliage").</summary>
public enum FoliageType : byte
{
    Grass, // knee-high blade clumps
    Bush, // round leafy blobs, waist to chest high
    Shrub, // spiky fern-like fronds, knee to waist high
    Tree, // broadleaf tree: solid trunk + leafy canopy
    Pine, // conifer: solid trunk + stacked cones
}

/// <summary>Foliage detail setting (Settings > Video). Off draws nothing (trunks still show as plain boxes).</summary>
public enum FoliageQuality : byte
{
    Off,
    Low,
    Medium,
    High,
}

/// <summary>One placed plant. Deterministic: the same map file gives the same list on every PC.</summary>
public readonly record struct FoliageInstance(FoliageType Type, Vector3 Position, float Scale, float Yaw01, float Rand, Vector3 Color)
{
    /// <summary>For trees: the trunk's collision box (Scale is the tree height in metres).</summary>
    public Box TrunkBox => Foliage.TrunkBox(Type, Position, Scale);
}

/// <summary>The area a scatter region covers.</summary>
public enum FoliageAreaKind : byte
{
    Rect,
    Circle,
    Polygon,
}

/// <summary>A rectangle (x0, z0, x1, z1), a circle (x, z, r) or a polygon of (x, z) points.</summary>
public sealed record FoliageArea(FoliageAreaKind Kind, float[] Values)
{
    public bool Contains(float x, float z)
    {
        var v = Values;
        switch (Kind)
        {
            case FoliageAreaKind.Rect:
                return x >= MathF.Min(v[0], v[2]) && x <= MathF.Max(v[0], v[2]) && z >= MathF.Min(v[1], v[3]) && z <= MathF.Max(v[1], v[3]);
            case FoliageAreaKind.Circle:
            {
                float dx = x - v[0], dz = z - v[1];
                return dx * dx + dz * dz <= v[2] * v[2];
            }
            default:
            {
                // Even-odd rule (only + - * / and comparisons, so it's bit-identical everywhere).
                var inside = false;
                var n = v.Length / 2;
                for (int i = 0, j = n - 1; i < n; j = i++)
                {
                    float xi = v[i * 2], zi = v[i * 2 + 1], xj = v[j * 2], zj = v[j * 2 + 1];
                    if ((zi > z) != (zj > z) && x < (xj - xi) * (z - zi) / (zj - zi) + xi)
                        inside = !inside;
                }
                return inside;
            }
        }
    }

    /// <summary>Bounding rectangle (minX, minZ, maxX, maxZ).</summary>
    public (float X0, float Z0, float X1, float Z1) Bounds
    {
        get
        {
            var v = Values;
            if (Kind == FoliageAreaKind.Rect)
                return (MathF.Min(v[0], v[2]), MathF.Min(v[1], v[3]), MathF.Max(v[0], v[2]), MathF.Max(v[1], v[3]));
            if (Kind == FoliageAreaKind.Circle)
                return (v[0] - v[2], v[1] - v[2], v[0] + v[2], v[1] + v[2]);
            float x0 = float.MaxValue, z0 = float.MaxValue, x1 = float.MinValue, z1 = float.MinValue;
            for (var i = 0; i + 1 < v.Length; i += 2)
            {
                x0 = MathF.Min(x0, v[i]);
                x1 = MathF.Max(x1, v[i]);
                z0 = MathF.Min(z0, v[i + 1]);
                z1 = MathF.Max(z1, v[i + 1]);
            }
            return (x0, z0, x1, z1);
        }
    }

    public bool Equals(FoliageArea? other) => other != null && Kind == other.Kind && Values.AsSpan().SequenceEqual(other.Values);

    public override int GetHashCode() => HashCode.Combine(Kind, Values.Length);
}

/// <summary>A scatter region: plants of one type spread over an area at a given density.</summary>
public sealed class FoliageRegion
{
    public FoliageType Type;

    /// <summary>Plants per square metre (at High quality; lower settings draw a subset).</summary>
    public float Density;

    public required FoliageArea Area;
    public int Seed;

    /// <summary>Size range: a scale multiplier, or for trees the height in metres.</summary>
    public float SizeMin, SizeMax;

    public Vector3? Color;
    public bool AvoidSolid = true;
    public bool AvoidPaths = true;

    /// <summary>0 = even cover, 1 = strongly clumped into patches.</summary>
    public float Patchiness;
}

/// <summary>A single hand-placed plant.</summary>
public sealed record FoliageItem(FoliageType Type, float X, float Z, float Size, Vector3? Color);

/// <summary>A path (polyline of x, z points) that scatter regions keep clear.</summary>
public sealed record FoliagePath(float[] Points, float Width);

/// <summary>A map's "foliage" section, as written in the map file.</summary>
public sealed class FoliageSpec
{
    public int Seed;
    public readonly List<FoliagePath> Paths = new();
    public readonly List<FoliageRegion> Regions = new();
    public readonly List<FoliageItem> Items = new();
}

/// <summary>
/// Foliage placement: turns a map's <see cref="FoliageSpec"/> into a fixed list of <see cref="FoliageInstance"/>s.
/// Only tree trunks are solid (they become boxes in <see cref="Map.Solid"/>); grass, bushes, shrubs and canopies
/// are purely cosmetic. Placement uses its own integer hash RNG and only exactly-rounded float maths (+ - * /),
/// so the result is bit-identical on every PC (trunk boxes have to match for collision and multiplayer).
/// </summary>
public static class Foliage
{
    /// <summary>Most plants one map may have (about 14 MB of GPU instance data); the rest are dropped with a warning.</summary>
    public const int MaxInstances = 400_000;

    /// <summary>Highest density a region may ask for (plants per square metre).</summary>
    public const float MaxDensity = 20f;

    public static readonly Vector3 GrassGreen = new(0.36f, 0.52f, 0.21f);
    public static readonly Vector3 BushGreen = new(0.24f, 0.42f, 0.17f);
    public static readonly Vector3 ShrubGreen = new(0.31f, 0.47f, 0.20f);
    public static readonly Vector3 PineGreen = new(0.17f, 0.35f, 0.19f);

    public static bool IsTree(FoliageType t) => t is FoliageType.Tree or FoliageType.Pine;

    public static Vector3 DefaultColor(FoliageType t) => t switch
    {
        FoliageType.Grass => GrassGreen,
        FoliageType.Bush => BushGreen,
        FoliageType.Shrub => ShrubGreen,
        FoliageType.Pine => PineGreen,
        _ => Map.Leaf,
    };

    /// <summary>Default size range: scale for small plants, height in metres for trees.</summary>
    public static (float Min, float Max) DefaultSize(FoliageType t) => t switch
    {
        FoliageType.Tree => (5f, 8f),
        FoliageType.Pine => (6f, 10f),
        _ => (0.8f, 1.25f),
    };

    public static float DefaultPatchiness(FoliageType t) => t == FoliageType.Grass ? 0.35f : IsTree(t) ? 0f : 0.25f;

    /// <summary>Trunk width (metres) for a tree of height <paramref name="h"/>.</summary>
    public static float TrunkWidth(float h) => Round2(Math.Clamp(0.07f * h + 0.1f, 0.35f, 0.9f));

    /// <summary>Trunk height: up into the canopy, so the box ends where the leaves hide it.</summary>
    public static float TrunkHeight(FoliageType t, float h) => Round2(h * (t == FoliageType.Pine ? 0.5f : 0.62f));

    public static Box TrunkBox(FoliageType t, Vector3 pos, float h)
    {
        var w = TrunkWidth(h);
        var th = TrunkHeight(t, h);
        const float sink = 0.1f; // a little into the ground, so slopes don't leave a gap under it
        return new Box(new Vector3(pos.X, pos.Y + (th - sink) / 2, pos.Z), new Vector3(w / 2, (th + sink) / 2, w / 2));
    }

    private static float Round2(float f) => MathF.Round(f * 100f) / 100f;

    // ---- Quality ---------------------------------------------------------------------------------

    /// <summary>Fraction of a type's plants kept at a quality level (applied at build time, a nested subset).</summary>
    public static float DensityFor(FoliageType t, FoliageQuality q)
    {
        if (q == FoliageQuality.Off)
            return 0f;
        if (IsTree(t))
            return 1f; // trunks are solid: every tree is always drawn
        return t switch
        {
            FoliageType.Grass => q switch { FoliageQuality.Low => 0.4f, FoliageQuality.Medium => 0.7f, _ => 1f },
            FoliageType.Shrub => q switch { FoliageQuality.Low => 0.6f, FoliageQuality.Medium => 0.85f, _ => 1f },
            _ => q switch { FoliageQuality.Low => 0.7f, _ => 1f },
        };
    }

    // Per quality (Low, Medium, High), in metres.
    private static readonly float[] FadeGrass = [22f, 38f, 60f], FadeShrub = [45f, 70f, 110f], FadeBush = [60f, 95f, 150f];
    private static readonly float[] LodGrass = [10f, 16f, 25f], LodShrub = [20f, 30f, 45f], LodBush = [25f, 40f, 60f], LodTree = [40f, 60f, 90f];

    /// <summary>Trees never thin out with distance (their trunks are solid boxes that are always drawn).</summary>
    public const float NoFade = 1e6f;

    /// <summary>
    /// Distance (metres) at which a type has completely thinned out; it starts thinning at <see cref="FadeStartFraction"/>
    /// of this. Each plant gets its own cutoff in between and fades out with a dither, so nothing pops.
    /// </summary>
    public static float FadeDistance(FoliageType t, FoliageQuality q)
    {
        var i = Math.Clamp((int)q - 1, 0, 2);
        return t switch
        {
            FoliageType.Grass => FadeGrass[i],
            FoliageType.Shrub => FadeShrub[i],
            FoliageType.Bush => FadeBush[i],
            _ => NoFade,
        };
    }

    /// <summary>Where distance thinning starts, as a fraction of <see cref="FadeDistance"/>.</summary>
    public const float FadeStartFraction = 0.45f;

    /// <summary>Beyond this distance a type is drawn with its low-detail mesh.</summary>
    public static float LodDistance(FoliageType t, FoliageQuality q)
    {
        var i = Math.Clamp((int)q - 1, 0, 2);
        return t switch
        {
            FoliageType.Grass => LodGrass[i],
            FoliageType.Shrub => LodShrub[i],
            FoliageType.Bush => LodBush[i],
            _ => LodTree[i],
        };
    }

    /// <summary>Does this instance survive the quality filter?</summary>
    public static bool Kept(in FoliageInstance f, FoliageQuality q) => f.Rand < DensityFor(f.Type, q);

    /// <summary>How many plants of each type a quality level keeps (what the renderer uploads).</summary>
    public static int[] CountFor(IReadOnlyList<FoliageInstance> all, FoliageQuality q)
    {
        var counts = new int[5];
        foreach (var f in all)
            if (Kept(f, q))
                counts[(int)f.Type]++;
        return counts;
    }

    // ---- Deterministic random numbers --------------------------------------------------------------

    public static uint Hash(uint a)
    {
        a ^= a >> 16;
        a *= 0x7feb352du;
        a ^= a >> 15;
        a *= 0x846ca68bu;
        a ^= a >> 16;
        return a;
    }

    /// <summary>A tiny deterministic generator (one per grid cell, so editing one spot doesn't reshuffle the rest).</summary>
    public struct Rng(uint seed)
    {
        private uint _s = Hash(seed ^ 0x9e3779b9u) | 1u;

        public uint NextU()
        {
            _s ^= _s << 13;
            _s ^= _s >> 17;
            _s ^= _s << 5;
            return _s;
        }

        /// <summary>[0, 1) with 24 bits, exact in float.</summary>
        public float Next() => (NextU() >> 8) * (1f / 16777216f);
    }

    /// <summary>Smooth value noise in [0, 1] on a 7 m lattice (for patchy cover).</summary>
    public static float Noise(float x, float z, uint seed)
    {
        const float cell = 7f;
        float gx = x / cell, gz = z / cell;
        float fx = MathF.Floor(gx), fz = MathF.Floor(gz);
        int ix = (int)fx, iz = (int)fz;
        float tx = gx - fx, tz = gz - fz;
        tx = tx * tx * (3f - 2f * tx);
        tz = tz * tz * (3f - 2f * tz);
        float Corner(int cx, int cz) => (Hash(seed ^ Hash((uint)cx * 73856093u ^ (uint)cz * 19349663u)) >> 8) * (1f / 16777216f);
        float a = Corner(ix, iz), b = Corner(ix + 1, iz), c = Corner(ix, iz + 1), d = Corner(ix + 1, iz + 1);
        float top = a + (b - a) * tx, bottom = c + (d - c) * tx;
        return top + (bottom - top) * tz;
    }
}

public sealed partial class Map
{
    /// <summary>The map file's "foliage" section (null: none).</summary>
    public FoliageSpec? FoliageSection;

    /// <summary>Every plant on the map at full (High) density, in a fixed order. Empty without foliage.</summary>
    public FoliageInstance[] FoliageInstances = [];

    /// <summary>Tree trunk boxes are <see cref="Solid"/>[TrunkStart .. TrunkStart + TrunkCount).</summary>
    public int TrunkStart = -1;

    public int TrunkCount;

    /// <summary>Is <see cref="Solid"/>[i] a foliage tree trunk?</summary>
    public bool IsTrunk(int i) => TrunkCount > 0 && i >= TrunkStart && i < TrunkStart + TrunkCount;

    /// <summary>
    /// Ground height under (x, z) for foliage placement. Everything that sits on the ground goes through here:
    /// the terrain on terrain maps, y = 0 on flat ones (so their plants and fingerprints don't change).
    /// </summary>
    private float FoliageGround(float x, float z) => GroundHeight(x, z);

    /// <summary>
    /// Places the plants described by <see cref="Foliage"/> (after the ops have built the map, so they can keep out
    /// of buildings) and adds a solid box for every tree trunk. Called by the map loader; deterministic.
    /// </summary>
    public void PlantFoliage()
    {
        var spec = FoliageSection;
        if (spec == null)
            return;
        var list = new List<FoliageInstance>();
        var trunks = new List<Box>();
        var grid = new BoxGrid(Solid);

        // Hand-placed plants first: they always appear, and scattered trees keep out of their way.
        var itemIndex = 0;
        foreach (var it in spec.Items)
        {
            var rng = new Foliage.Rng((uint)spec.Seed * 2654435761u ^ (uint)itemIndex++ * 40503u ^ 0x51ed27u);
            var y = FoliageGround(it.X, it.Z);
            var yaw = rng.Next();
            var tint = 0.92f + rng.Next() * 0.16f;
            rng.Next(); // reserved
            var inst = new FoliageInstance(it.Type, new Vector3(it.X, y, it.Z), it.Size, yaw, 0f, (it.Color ?? Foliage.DefaultColor(it.Type)) * tint);
            list.Add(inst);
            if (Foliage.IsTree(it.Type))
            {
                var b = inst.TrunkBox;
                trunks.Add(b);
                grid.Add(b);
            }
        }

        for (var r = 0; r < spec.Regions.Count; r++)
            Scatter(spec, spec.Regions[r], list, trunks, grid);

        if (list.Count > Foliage.MaxInstances)
        {
            Console.WriteLine($"Map '{Name}': {list.Count} plants is more than the limit of {Foliage.MaxInstances}; the rest are dropped. Lower some densities.");
            list.RemoveRange(Foliage.MaxInstances, list.Count - Foliage.MaxInstances);
            trunks.Clear();
            foreach (var f in list)
                if (Foliage.IsTree(f.Type))
                    trunks.Add(f.TrunkBox);
        }

        FoliageInstances = list.ToArray();
        FoliageHash = HashFoliage(FoliageInstances);
        AddTrunkColliders(trunks);
    }

    /// <summary>
    /// The one place foliage touches collision: every tree trunk becomes a solid box, so movement, bullets and bot
    /// sight treat it like any other box. (When the game moves to Jolt, this is where trunks become static bodies.)
    /// Trunks go straight into <see cref="Solid"/>, not through Block, which would record them as ops and write them
    /// back out to the file.
    /// </summary>
    private void AddTrunkColliders(List<Box> trunks)
    {
        TrunkStart = Solid.Count;
        TrunkCount = trunks.Count;
        foreach (var b in trunks)
        {
            Solid.Add(b);
            Colors.Add(TrunkBrown);
        }
        _materials = null;
    }

    /// <summary>A fingerprint of every placed plant (0 when there are none), mixed into <see cref="ContentHash"/>.</summary>
    public uint FoliageHash;

    private static uint HashFoliage(FoliageInstance[] all)
    {
        if (all.Length == 0)
            return 0;
        var h = 2166136261u;
        void Mix(uint v)
        {
            for (var i = 0; i < 4; i++)
            {
                h ^= (v >> (i * 8)) & 0xFF;
                h *= 16777619u;
            }
        }
        Mix((uint)all.Length);
        foreach (var f in all)
        {
            Mix((uint)f.Type);
            Mix(BitConverter.SingleToUInt32Bits(f.Position.X));
            Mix(BitConverter.SingleToUInt32Bits(f.Position.Y));
            Mix(BitConverter.SingleToUInt32Bits(f.Position.Z));
            Mix(BitConverter.SingleToUInt32Bits(f.Scale));
            Mix(BitConverter.SingleToUInt32Bits(f.Yaw01));
            Mix(BitConverter.SingleToUInt32Bits(f.Rand));
        }
        return h == 0 ? 1u : h;
    }

    private void Scatter(FoliageSpec spec, FoliageRegion reg, List<FoliageInstance> list, List<Box> trunks, BoxGrid grid)
    {
        var density = Math.Clamp(reg.Density, 0f, Foliage.MaxDensity);
        if (density <= 0f)
            return;
        var tree = Foliage.IsTree(reg.Type);
        var cell = 1f / MathF.Sqrt(density);
        var jitter = tree ? 0.7f : 1f; // trees keep at least 30% of a cell apart
        var (bx0, bz0, bx1, bz1) = reg.Area.Bounds;
        bx0 = MathF.Max(bx0, -HalfX);
        bz0 = MathF.Max(bz0, -HalfZ);
        bx1 = MathF.Min(bx1, HalfX);
        bz1 = MathF.Min(bz1, HalfZ);
        if (bx1 <= bx0 || bz1 <= bz0)
            return;
        // Cells are on a world grid (not relative to the region), so moving a region's edge doesn't reshuffle the inside.
        var ix0 = (int)MathF.Floor(bx0 / cell);
        var ix1 = (int)MathF.Floor(bx1 / cell);
        var iz0 = (int)MathF.Floor(bz0 / cell);
        var iz1 = (int)MathF.Floor(bz1 / cell);
        var seed = Foliage.Hash((uint)spec.Seed * 0x9E3779B1u ^ (uint)reg.Seed * 0x85EBCA77u ^ (uint)reg.Type * 0xC2B2AE3Du);
        var color = reg.Color ?? Foliage.DefaultColor(reg.Type);
        var patch = Math.Clamp(reg.Patchiness, 0f, 1f);
        // How far plants keep from walls and boxes, and how high above the ground a box has to reach to count.
        var margin = reg.Type switch { FoliageType.Grass => 0.1f, FoliageType.Shrub => 0.3f, FoliageType.Bush => 0.5f, _ => 1.0f };

        for (var iz = iz0; iz <= iz1; iz++)
        for (var ix = ix0; ix <= ix1; ix++)
        {
            // Always draw the same numbers per cell, whatever gets rejected, so placement is stable under edits.
            var rng = new Foliage.Rng(seed ^ Foliage.Hash((uint)ix * 73856093u ^ (uint)iz * 19349663u));
            var x = (ix + 0.5f + (rng.Next() - 0.5f) * jitter) * cell;
            var z = (iz + 0.5f + (rng.Next() - 0.5f) * jitter) * cell;
            var accept = rng.Next();
            var size = rng.Next();
            var yaw = rng.Next();
            var rand = rng.Next();
            var tint = 0.9f + rng.Next() * 0.2f;
            var hueShift = rng.Next() - 0.5f;

            if (x < bx0 || x > bx1 || z < bz0 || z > bz1 || !reg.Area.Contains(x, z))
                continue;
            if (patch > 0f)
            {
                var n = Foliage.Noise(x, z, seed);
                n = Math.Clamp((n - 0.2f) / 0.6f, 0f, 1f);
                n = n * n * (3f - 2f * n);
                if (accept >= 1f - patch + patch * n)
                    continue;
            }
            if (reg.AvoidPaths && NearPath(spec, x, z, margin))
                continue;
            var g = FoliageGround(x, z);
            if (HasTerrain && GroundNormal(x, z).Y < World.MinWalkNormalY)
                continue; // nothing grows on cliff faces
            var s = reg.SizeMin + (reg.SizeMax - reg.SizeMin) * size;
            var top = tree ? s : 1.6f * s;
            if (reg.AvoidSolid && grid.Blocks(x, z, margin + (tree ? Foliage.TrunkWidth(s) / 2 : 0f), g + 0.05f, g + top))
                continue;
            if (tree)
            {
                var near = false;
                foreach (var sp in Spawns)
                {
                    float dx = sp.X - x, dz = sp.Z - z;
                    if (dx * dx + dz * dz < 2.25f) // keep 1.5 m clear of spawn points
                    {
                        near = true;
                        break;
                    }
                }
                if (near)
                    continue;
            }
            // Slight per-plant colour variation: brightness plus a nudge between yellow-green and blue-green.
            var c = color * tint + new Vector3(hueShift * 0.06f, 0f, -hueShift * 0.04f);
            c = Vector3.Clamp(c, Vector3.Zero, Vector3.One);
            var inst = new FoliageInstance(reg.Type, new Vector3(x, g, z), tree ? MathF.Round(s * 100f) / 100f : s, yaw, rand, c);
            if (tree)
            {
                var b = inst.TrunkBox;
                trunks.Add(b);
                grid.Add(b);
            }
            list.Add(inst);
        }
    }

    private static bool NearPath(FoliageSpec spec, float x, float z, float margin)
    {
        foreach (var p in spec.Paths)
        {
            var r = p.Width / 2 + margin;
            var r2 = r * r;
            var pts = p.Points;
            for (var i = 0; i + 3 < pts.Length; i += 2)
            {
                float ax = pts[i], az = pts[i + 1], bx = pts[i + 2], bz = pts[i + 3];
                float dx = bx - ax, dz = bz - az;
                var len2 = dx * dx + dz * dz;
                var t = len2 > 0f ? Math.Clamp(((x - ax) * dx + (z - az) * dz) / len2, 0f, 1f) : 0f;
                float qx = ax + dx * t - x, qz = az + dz * t - z;
                if (qx * qx + qz * qz < r2)
                    return true;
            }
            if (pts.Length == 2)
            {
                float qx = pts[0] - x, qz = pts[1] - z;
                if (qx * qx + qz * qz < r2)
                    return true;
            }
        }
        return false;
    }

    /// <summary>A coarse 4 m grid over the solid boxes, so "is this spot inside something" is quick for thousands of plants.</summary>
    private sealed class BoxGrid
    {
        private const float CellSize = 4f;
        private readonly Dictionary<long, List<Box>> _cells = new();

        public BoxGrid(List<Box> boxes)
        {
            foreach (var b in boxes)
                Add(b);
        }

        private static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;

        public void Add(Box b)
        {
            var min = b.Min;
            var max = b.Max;
            // Huge boxes (the ground slab, perimeter walls) span many cells; that's fine, there are few of them.
            int x0 = (int)MathF.Floor(min.X / CellSize), x1 = (int)MathF.Floor(max.X / CellSize);
            int z0 = (int)MathF.Floor(min.Z / CellSize), z1 = (int)MathF.Floor(max.Z / CellSize);
            if ((long)(x1 - x0 + 1) * (z1 - z0 + 1) > 1_000_000)
                return; // absurd box; ignore rather than allocate forever
            for (var z = z0; z <= z1; z++)
            for (var x = x0; x <= x1; x++)
            {
                if (!_cells.TryGetValue(Key(x, z), out var l))
                    _cells[Key(x, z)] = l = new List<Box>(4);
                l.Add(b);
            }
        }

        /// <summary>Does a box come within <paramref name="margin"/> of (x, z) somewhere between heights y0 and y1?</summary>
        public bool Blocks(float x, float z, float margin, float y0, float y1)
        {
            int cx0 = (int)MathF.Floor((x - margin) / CellSize), cx1 = (int)MathF.Floor((x + margin) / CellSize);
            int cz0 = (int)MathF.Floor((z - margin) / CellSize), cz1 = (int)MathF.Floor((z + margin) / CellSize);
            for (var cz = cz0; cz <= cz1; cz++)
            for (var cx = cx0; cx <= cx1; cx++)
            {
                if (!_cells.TryGetValue(Key(cx, cz), out var l))
                    continue;
                foreach (var b in l)
                {
                    var min = b.Min;
                    var max = b.Max;
                    if (max.Y > y0 && min.Y < y1 && x + margin > min.X && x - margin < max.X && z + margin > min.Z && z - margin < max.Z)
                        return true;
                }
            }
            return false;
        }
    }
}
