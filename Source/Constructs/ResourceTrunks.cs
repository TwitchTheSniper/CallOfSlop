using System.Numerics;

namespace CallOfSlop;

/// <summary>One standing tree's solid trunk: an upright cylinder in its lattice cell (always clear of the cell's edges).</summary>
public readonly record struct Trunk(uint Key, float X, float Z, float Radius, float Height)
{
    /// <summary>How far the trunk goes into the ground (slopes leave no gap under it).</summary>
    public const float Sink = 0.3f;

    /// <summary>Its share of the tree, as drawn (FoliageType.Trunk's mesh; the canopy above it is not solid).</summary>
    public const float HeightShare = 0.62f;
}

/// <summary>
/// Why generated trees are solid without being in <see cref="Map.Solid"/> (that list feeds the map's identity, nav graphs,
/// bakes and pins, and trees are not map data; there would be millions on a big world): a collision question looks only
/// at the lattice cells it crosses. A coarse mask says at once that a place has no forest, a tree's place and size come
/// from its hash, and the costly "is this in a forest" answer is kept in a small direct-mapped table of single 64-bit
/// words, so any thread may read and write it. Nothing here allocates, nothing loops over the map, and a felled tree
/// simply stops answering. <see cref="PhysicsWorld"/> asks these for the world's own collision world only
/// (<see cref="QueryLayers.Trunks"/>; the nav graph's copy has no trees).
/// </summary>
public sealed partial class ResourceField
{
    private const int MaskShift = 4; // a mask cell is 16 lattice cells a side (96 m)
    private const int CacheBits = 14;
    public const float MinTreeHeight = 7f, MaxTreeHeight = 14f;
    private const float TrunkWidth = 0.05f; // radius per metre of tree (the drawn trunk near the ground)
    public const float MaxTrunkRadius = MaxTreeHeight * TrunkWidth;

    private bool[] _mask = [];
    private int _maskX, _maskZ, _maskW, _maskH; // the mask's first cell and size
    private readonly ulong[] _cache = new ulong[1 << CacheBits]; // (key + 1) << 1 | in a forest; 0: empty

    private static float TreeHeight(float unit) => MinTreeHeight + (MaxTreeHeight - MinTreeHeight) * unit;

    /// <summary>Marks the mask cells each surface deposit can reach. Walks the deposits, not the map.</summary>
    private void BuildForestMask()
    {
        _maskX = Lattice(-_map.HalfX) >> MaskShift;
        _maskZ = Lattice(-_map.HalfZ) >> MaskShift;
        _maskW = (Lattice(_map.HalfX) >> MaskShift) - _maskX + 1;
        _maskH = (Lattice(_map.HalfZ) >> MaskShift) - _maskZ + 1;
        _mask = new bool[_maskW * _maskH];
        foreach (var d in _deposits)
        {
            if (!_table.Kinds[d.Kind].Surface)
                continue;
            var r = MathF.Max(d.Rx, d.Rz) * 1.25f;
            var x1 = Math.Min((Lattice(d.X + r) >> MaskShift) - _maskX, _maskW - 1);
            var z1 = Math.Min((Lattice(d.Z + r) >> MaskShift) - _maskZ, _maskH - 1);
            for (var z = Math.Max((Lattice(d.Z - r) >> MaskShift) - _maskZ, 0); z <= z1; z++)
                for (var x = Math.Max((Lattice(d.X - r) >> MaskShift) - _maskX, 0); x <= x1; x++)
                    _mask[z * _maskW + x] = true;
        }
    }

    private bool InForestMask(int ix, int iz)
    {
        var x = (ix >> MaskShift) - _maskX;
        var z = (iz >> MaskShift) - _maskZ;
        return (uint)x < (uint)_maskW && (uint)z < (uint)_maskH && _mask[z * _maskW + x];
    }

    /// <summary>Is a tree spot of this cell in a forest and clear of the map's boxes? The answer is kept.</summary>
    private bool InForest(uint key, float x, float z)
    {
        var tag = ((ulong)key + 1) << 1;
        ref var slot = ref _cache[key * 2654435761u >> 32 - CacheBits];
        var seen = slot;
        if ((seen & ~1UL) == tag)
            return (seen & 1) != 0;
        var yes = !_blocked.Contains(key) && At(x, z, true, false, out var s) >= 0 && s > EdgeThin;
        slot = tag | (yes ? 1UL : 0UL);
        return yes;
    }

    /// <summary>The standing tree of a lattice cell, if there is one.</summary>
    public bool TrunkAt(int ix, int iz, out Trunk t)
    {
        t = default;
        if (!TreeAt(ix, iz, out var x, out var z, out var h))
            return false;
        var key = TreeKey(ix, iz);
        if (_felled.ContainsKey(key))
            return false;
        Unit(ref h); // (the drawn kind)
        var height = TreeHeight(Unit(ref h));
        t = new Trunk(key, x, z, height * TrunkWidth, height);
        return true;
    }

    /// <summary>A trunk's height range in the world (the ground is only asked for a trunk that may matter).</summary>
    public void Span(in Trunk t, out float bottom, out float top)
    {
        var ground = _map.GroundHeight(t.X, t.Z);
        bottom = ground - Trunk.Sink;
        top = ground + t.Height * Trunk.HeightShare;
    }

    /// <summary>The nearest trunk along a ray before <paramref name="maxDist"/>: its distance, or <paramref name="maxDist"/>. A ray that starts inside a trunk doesn't hit it.</summary>
    public float RayTrunks(Vector3 o, Vector3 d, float maxDist, out uint key, out Vector3 normal)
    {
        key = 0;
        normal = default;
        var best = maxDist;
        var ix = Lattice(o.X);
        var iz = Lattice(o.Z);
        var stepX = d.X > 0 ? 1 : -1;
        var stepZ = d.Z > 0 ? 1 : -1;
        var deltaX = MathF.Abs(d.X) < 1e-7f ? float.PositiveInfinity : TreeSpacing / MathF.Abs(d.X);
        var deltaZ = MathF.Abs(d.Z) < 1e-7f ? float.PositiveInfinity : TreeSpacing / MathF.Abs(d.Z);
        var nextX = float.IsInfinity(deltaX) ? float.PositiveInfinity : ((d.X > 0 ? ix + 1 : ix) * TreeSpacing - o.X) / d.X;
        var nextZ = float.IsInfinity(deltaZ) ? float.PositiveInfinity : ((d.Z > 0 ? iz + 1 : iz) * TreeSpacing - o.Z) / d.Z;
        for (var guard = 0; guard < 65536; guard++)
        {
            if (InForestMask(ix, iz) && TrunkAt(ix, iz, out var t) && RayTrunk(t, o, d, best, out var at, out var n))
            {
                (best, key, normal) = (at, t.Key, n);
                return best; // (a trunk stays inside its cell, so nothing in a later cell can be nearer)
            }
            var leave = MathF.Min(nextX, nextZ);
            if (leave >= best)
                break;
            if (nextX < nextZ)
            {
                ix += stepX;
                nextX += deltaX;
            }
            else
            {
                iz += stepZ;
                nextZ += deltaZ;
            }
        }
        return best;
    }

    private bool RayTrunk(in Trunk t, Vector3 o, Vector3 d, float maxDist, out float at, out Vector3 normal)
    {
        at = 0;
        normal = default;
        // The infinite upright cylinder.
        float ox = o.X - t.X, oz = o.Z - t.Z;
        var a = d.X * d.X + d.Z * d.Z;
        var c = ox * ox + oz * oz - t.Radius * t.Radius;
        float near, far;
        if (a < 1e-12f)
        {
            if (c >= 0)
                return false;
            (near, far) = (float.NegativeInfinity, float.PositiveInfinity);
        }
        else
        {
            var b = ox * d.X + oz * d.Z;
            var disc = b * b - a * c;
            if (disc <= 0)
                return false;
            var root = MathF.Sqrt(disc);
            near = (-b - root) / a;
            far = (-b + root) / a;
            if (far <= 0 || near >= maxDist)
                return false;
        }
        // Its height range.
        Span(t, out var bottom, out var top);
        float yNear, yFar;
        if (MathF.Abs(d.Y) < 1e-7f)
        {
            if (o.Y <= bottom || o.Y >= top)
                return false;
            (yNear, yFar) = (float.NegativeInfinity, float.PositiveInfinity);
        }
        else
        {
            var y0 = (bottom - o.Y) / d.Y;
            var y1 = (top - o.Y) / d.Y;
            (yNear, yFar) = y0 < y1 ? (y0, y1) : (y1, y0);
        }
        var enter = MathF.Max(near, yNear);
        if (enter >= MathF.Min(far, yFar) || enter < 0 || enter >= maxDist)
            return false; // (enter < 0: it starts inside)
        at = enter;
        normal = yNear > near ? new Vector3(0, d.Y > 0 ? -1 : 1, 0) : Vector3.Normalize(new Vector3(ox + d.X * enter, 0, oz + d.Z * enter));
        return true;
    }

    /// <summary>Would an upright body of <paramref name="radius"/> from <paramref name="feet"/> up overlap a trunk? Round, as <see cref="PushOut"/> pushes, so a body pushed out is free.</summary>
    public bool BodyInTrunk(Vector3 feet, float height, float radius)
    {
        var reach = radius + MaxTrunkRadius;
        for (var iz = Lattice(feet.Z - reach); iz <= Lattice(feet.Z + reach); iz++)
            for (var ix = Lattice(feet.X - reach); ix <= Lattice(feet.X + reach); ix++)
                if (InForestMask(ix, iz) && TrunkAt(ix, iz, out var t))
                {
                    var r = radius + t.Radius;
                    if (Flat2(feet, t) >= r * r)
                        continue;
                    Span(t, out var bottom, out var top);
                    if (feet.Y < top && feet.Y + height > bottom)
                        return true;
                }
        return false;
    }

    private static float Flat2(Vector3 p, in Trunk t) => (p.X - t.X) * (p.X - t.X) + (p.Z - t.Z) * (p.Z - t.Z);

    /// <summary>A body pushed clear of the trunks around it, round each one, its speed into them gone (the classic mover: it slides round a tree).</summary>
    public Vector3 PushOut(Vector3 feet, ref Vector3 velocity, float height, float radius)
    {
        var reach = radius + MaxTrunkRadius;
        for (var iz = Lattice(feet.Z - reach); iz <= Lattice(feet.Z + reach); iz++)
            for (var ix = Lattice(feet.X - reach); ix <= Lattice(feet.X + reach); ix++)
            {
                if (!InForestMask(ix, iz) || !TrunkAt(ix, iz, out var t))
                    continue;
                var r = radius + t.Radius;
                var flat = Flat2(feet, t);
                if (flat >= r * r)
                    continue;
                Span(t, out var bottom, out var top);
                if (feet.Y >= top || feet.Y + height <= bottom)
                    continue;
                var dist = MathF.Sqrt(flat);
                var (nx, nz) = dist > 1e-4f ? ((feet.X - t.X) / dist, (feet.Z - t.Z) / dist) : (1f, 0f); // (dead centre: any way out, the same on every peer)
                feet.X = t.X + nx * (r + 0.001f);
                feet.Z = t.Z + nz * (r + 0.001f);
                var into = velocity.X * nx + velocity.Z * nz;
                if (into < 0)
                {
                    velocity.X -= into * nx;
                    velocity.Z -= into * nz;
                }
            }
        return feet;
    }

    /// <summary>Is the point inside a trunk grown by <paramref name="radius"/>?</summary>
    public bool PointInTrunk(Vector3 p, float radius) => BodyInTrunk(p with { Y = p.Y - radius }, radius * 2, radius);

    /// <summary>A trunk as a box (its bounds, for the shape queries of the capsule mover and ragdolls).</summary>
    private Obb TrunkBox(in Trunk t, float bottom, float top) =>
        new(new Vector3(t.X, (bottom + top) / 2, t.Z), new Vector3(t.Radius, (top - bottom) / 2, t.Radius), Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ);

    /// <summary>Does the box overlap a trunk? (<paramref name="lo"/> and <paramref name="hi"/> are its bounds.)</summary>
    public bool ObbInTrunk(in Obb box, Vector3 lo, Vector3 hi)
    {
        const float far = MaxTrunkRadius;
        for (var iz = Lattice(lo.Z - far); iz <= Lattice(hi.Z + far); iz++)
            for (var ix = Lattice(lo.X - far); ix <= Lattice(hi.X + far); ix++)
                if (InForestMask(ix, iz) && TrunkAt(ix, iz, out var t))
                {
                    Span(t, out var bottom, out var top);
                    if (Geometry.ObbOverlap(box, TrunkBox(t, bottom, top)))
                        return true;
                }
        return false;
    }

    /// <summary>Adds the trunks reaching min..max as boxes, cell by cell (a fixed order).</summary>
    public void GatherTrunks(Vector3 min, Vector3 max, List<ContactShape> into)
    {
        const float far = MaxTrunkRadius;
        for (var iz = Lattice(min.Z - far); iz <= Lattice(max.Z + far); iz++)
            for (var ix = Lattice(min.X - far); ix <= Lattice(max.X + far); ix++)
            {
                if (!InForestMask(ix, iz) || !TrunkAt(ix, iz, out var t) || t.X + t.Radius < min.X || t.X - t.Radius > max.X || t.Z + t.Radius < min.Z || t.Z - t.Radius > max.Z)
                    continue;
                Span(t, out var bottom, out var top);
                if (top >= min.Y && bottom <= max.Y)
                    into.Add(ContactShape.FromObb(TrunkBox(t, bottom, top), new ShapeId(ShapeKind.Trunk, (int)t.Key)));
            }
    }
}
