using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// A read-only snapshot of a map's collision: the boxes filed into a grid of square columns, so a ray cast, body check
/// or movement step tests the few boxes nearby instead of every box on the map (big maps have well over a thousand).
/// It gives exactly the answers of the every-box scans it replaces (same box tests, same terrain, and boxes visited in
/// map order where the order matters).
/// <para>Superseded by <see cref="PhysicsWorld"/> (a BVH, measured faster for rays and as fast for body checks; see the
/// README's Physics section). Kept only for <see cref="LegacyCollision"/>, the frozen old code the "Collision parity"
/// self-test compares the new world against (<see cref="LegacyCollision.Grid"/>).</para>
/// </summary>
public sealed class CollisionGrid
{
    public const float Cell = 4f;
    private const float Pad = 0.05f; // boxes are filed into every column they come within this distance of

    private readonly Map _map;
    private readonly Box[] _boxes;
    private readonly ITerrainCollision? _terrain;
    private readonly float _minX, _minZ, _maxX, _maxZ;
    private readonly int _cols, _rows;
    private readonly int[] _cellStart; // boxes of column c: _cellBoxes[_cellStart[c] .. _cellStart[c + 1]), in map order
    private readonly int[] _cellBoxes;
    private readonly int[] _stamp; // per box: the ray that last tested it (each box once per ray)
    private int _ray;
    private int[] _near = new int[64]; // Near's answer

    public int BoxCount => _boxes.Length;

    /// <summary>The boxes, in map order (indices from <see cref="Near"/> point into this).</summary>
    public ReadOnlySpan<Box> Boxes => _boxes;

    public CollisionGrid(Map map)
    {
        _map = map;
        _boxes = map.Solid.ToArray();
        _terrain = map.TerrainCollision;
        _stamp = new int[_boxes.Length];
        float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
        foreach (var b in _boxes)
        {
            minX = MathF.Min(minX, b.Min.X - Pad);
            minZ = MathF.Min(minZ, b.Min.Z - Pad);
            maxX = MathF.Max(maxX, b.Max.X + Pad);
            maxZ = MathF.Max(maxZ, b.Max.Z + Pad);
        }
        if (_boxes.Length == 0)
            minX = minZ = maxX = maxZ = 0;
        _minX = minX;
        _minZ = minZ;
        _maxX = maxX;
        _maxZ = maxZ;
        _cols = Math.Clamp((int)MathF.Ceiling((maxX - minX) / Cell), 1, 1024);
        _rows = Math.Clamp((int)MathF.Ceiling((maxZ - minZ) / Cell), 1, 1024);

        // Two passes (count, then fill) so each column's list is one slice of a single array, in box order.
        var counts = new int[_cols * _rows + 1];
        foreach (var b in _boxes)
        {
            CellRange(b, out var c0, out var c1, out var r0, out var r1);
            for (var r = r0; r <= r1; r++)
                for (var c = c0; c <= c1; c++)
                    counts[r * _cols + c + 1]++;
        }
        for (var c = 0; c < _cols * _rows; c++)
            counts[c + 1] += counts[c];
        _cellStart = counts;
        _cellBoxes = new int[counts[^1]];
        var fill = (int[])counts.Clone();
        for (var i = 0; i < _boxes.Length; i++)
        {
            CellRange(_boxes[i], out var c0, out var c1, out var r0, out var r1);
            for (var r = r0; r <= r1; r++)
                for (var c = c0; c <= c1; c++)
                    _cellBoxes[fill[r * _cols + c]++] = i;
        }
    }

    /// <summary>
    /// Swaps box <paramref name="index"/> for a box that lies inside the columns the old one was filed in (a broken prop's
    /// collider, shrunk to a speck under the world). A box that grows needs a new grid instead.
    /// </summary>
    public void ReplaceShrunkBox(int index, Box box) => _boxes[index] = box;

    /// <summary>Is this a snapshot of <paramref name="map"/> as it is now? (Maps don't change once built; the count is a cheap guard.)</summary>
    public bool IsFor(Map map) => ReferenceEquals(map, _map) && map.Solid.Count == _boxes.Length && ReferenceEquals(map.TerrainCollision, _terrain);

    private void CellRange(Box b, out int c0, out int c1, out int r0, out int r1)
    {
        c0 = Col(b.Min.X - Pad);
        c1 = Col(b.Max.X + Pad);
        r0 = Row(b.Min.Z - Pad);
        r1 = Row(b.Max.Z + Pad);
    }

    private int Col(float x) => Math.Clamp((int)MathF.Floor((x - _minX) / Cell), 0, _cols - 1);

    private int Row(float z) => Math.Clamp((int)MathF.Floor((z - _minZ) / Cell), 0, _rows - 1);

    /// <summary>Same as <see cref="World.RaycastWorld"/>: nearest hit on a box or the terrain, or maxDist.</summary>
    public float Raycast(Vector3 origin, Vector3 dir, float maxDist)
    {
        var best = RaycastBoxes(origin, dir, maxDist);
        if (_terrain != null && best > 0)
            best = MathF.Min(best, _terrain.Raycast(origin, dir, best));
        return best;
    }

    /// <summary>
    /// Same as <see cref="World.RaycastBoxes"/>. Walks the columns the ray crosses in order (a 2D DDA) and stops
    /// once the next column starts beyond the nearest hit so far: a box hit there would be further away.
    /// </summary>
    public float RaycastBoxes(Vector3 origin, Vector3 dir, float maxDist)
    {
        var best = maxDist;
        if (_boxes.Length == 0)
            return best;
        // Clip the ray to the grid's rectangle (nothing can be hit outside it).
        float t0 = 0f, t1 = maxDist;
        var flatX = MathF.Abs(dir.X) < 1e-6f;
        var flatZ = MathF.Abs(dir.Z) < 1e-6f;
        if (!Clip(origin.X, dir.X, flatX, _minX, _maxX, ref t0, ref t1) || !Clip(origin.Z, dir.Z, flatZ, _minZ, _maxZ, ref t0, ref t1))
            return best;

        if (++_ray == int.MaxValue)
        {
            Array.Clear(_stamp);
            _ray = 1;
        }
        var cx = Col(origin.X + dir.X * t0);
        var cz = Row(origin.Z + dir.Z * t0);
        int stepX = flatX ? 0 : dir.X > 0 ? 1 : -1, stepZ = flatZ ? 0 : dir.Z > 0 ? 1 : -1;
        var nextX = flatX ? float.PositiveInfinity : (_minX + (cx + (stepX > 0 ? 1 : 0)) * Cell - origin.X) / dir.X;
        var nextZ = flatZ ? float.PositiveInfinity : (_minZ + (cz + (stepZ > 0 ? 1 : 0)) * Cell - origin.Z) / dir.Z;
        var deltaX = flatX ? float.PositiveInfinity : Cell / MathF.Abs(dir.X);
        var deltaZ = flatZ ? float.PositiveInfinity : Cell / MathF.Abs(dir.Z);
        var enter = t0;
        while (enter <= best + 1e-3f)
        {
            var cell = cz * _cols + cx;
            for (var k = _cellStart[cell]; k < _cellStart[cell + 1]; k++)
            {
                var i = _cellBoxes[k];
                if (_stamp[i] == _ray)
                    continue;
                _stamp[i] = _ray;
                if (_boxes[i].Raycast(origin, dir, best) is { } t && t < best)
                    best = t;
            }
            if (nextX < nextZ)
            {
                if (nextX > t1)
                    break;
                enter = nextX;
                nextX += deltaX;
                cx += stepX;
            }
            else
            {
                if (nextZ > t1)
                    break;
                enter = nextZ;
                nextZ += deltaZ;
                cz += stepZ;
            }
            if (cx < 0 || cx >= _cols || cz < 0 || cz >= _rows)
                break;
        }
        return best;
    }

    private static bool Clip(float o, float d, bool flat, float lo, float hi, ref float t0, ref float t1)
    {
        if (flat)
            return o >= lo && o <= hi;
        var a = (lo - o) / d;
        var b = (hi - o) / d;
        if (a > b)
            (a, b) = (b, a);
        t0 = MathF.Max(t0, a);
        t1 = MathF.Min(t1, b);
        return t0 <= t1;
    }

    /// <summary>Same as <see cref="World.Blocked"/>: would a player standing at <paramref name="feet"/> overlap a box or be under the terrain?</summary>
    public bool Blocked(Vector3 feet, float height = World.PlayerHeight)
    {
        if (_terrain != null && _terrain.HeightAt(feet.X, feet.Z) > feet.Y + 0.05f)
            return true;
        return BlockedByBoxes(feet, height);
    }

    /// <summary>Same as <see cref="World.BlockedByBoxes"/>: would a player standing at <paramref name="feet"/> overlap a box?</summary>
    public bool BlockedByBoxes(Vector3 feet, float height = World.PlayerHeight)
    {
        const float r = World.PlayerRadius;
        int c0 = Col(feet.X - r), c1 = Col(feet.X + r), r0 = Row(feet.Z - r), r1 = Row(feet.Z + r);
        for (var row = r0; row <= r1; row++)
            for (var col = c0; col <= c1; col++)
            {
                var cell = row * _cols + col;
                for (var k = _cellStart[cell]; k < _cellStart[cell + 1]; k++)
                {
                    var b = _boxes[_cellBoxes[k]];
                    if (feet.Y < b.Max.Y - 0.001f && feet.Y + height > b.Min.Y + 0.001f
                        && feet.X + r > b.Min.X && feet.X - r < b.Max.X && feet.Z + r > b.Min.Z && feet.Z - r < b.Max.Z)
                        return true;
                }
            }
        return false;
    }

    /// <summary>Is point <paramref name="p"/> inside a box grown by <paramref name="r"/> on every side (the projectile test)?</summary>
    public bool PointInside(Vector3 p, float r)
    {
        int c0 = Col(p.X - r), c1 = Col(p.X + r), r0 = Row(p.Z - r), r1 = Row(p.Z + r);
        for (var row = r0; row <= r1; row++)
            for (var col = c0; col <= c1; col++)
            {
                var cell = row * _cols + col;
                for (var k = _cellStart[cell]; k < _cellStart[cell + 1]; k++)
                {
                    var b = _boxes[_cellBoxes[k]];
                    if (p.X > b.Min.X - r && p.X < b.Max.X + r && p.Y > b.Min.Y - r && p.Y < b.Max.Y + r && p.Z > b.Min.Z - r && p.Z < b.Max.Z + r)
                        return true;
                }
            }
        return false;
    }

    /// <summary>
    /// Every box that could touch the rectangle x0..x1, z0..z1 (in the ground plane), as indices into
    /// <see cref="Boxes"/> in map order with no repeats: a superset of the boxes really touching it, so a loop over
    /// these visits the boxes that matter in the same order as a loop over the whole map. The answer lives in a buffer
    /// that the next call reuses: finish with it before calling again.
    /// </summary>
    public ReadOnlySpan<int> Near(float x0, float z0, float x1, float z1)
    {
        int c0 = Col(x0), c1 = Col(x1), r0 = Row(z0), r1 = Row(z1);
        if (c0 == c1 && r0 == r1)
        {
            var cell = r0 * _cols + c0;
            return _cellBoxes.AsSpan(_cellStart[cell], _cellStart[cell + 1] - _cellStart[cell]); // one column: already in order
        }
        var n = 0;
        for (var row = r0; row <= r1; row++)
            for (var col = c0; col <= c1; col++)
            {
                var cell = row * _cols + col;
                var count = _cellStart[cell + 1] - _cellStart[cell];
                if (n + count > _near.Length)
                    Array.Resize(ref _near, Math.Max(_near.Length * 2, n + count));
                _cellBoxes.AsSpan(_cellStart[cell], count).CopyTo(_near.AsSpan(n));
                n += count;
            }
        var all = _near.AsSpan(0, n);
        all.Sort();
        var unique = 0;
        for (var k = 0; k < all.Length; k++)
            if (unique == 0 || all[k] != all[unique - 1])
                all[unique++] = all[k];
        return all[..unique];
    }

    /// <summary>
    /// Tops of the boxes whose footprint strictly contains (x, z), in map order (the order a scan over
    /// <see cref="Map.Solid"/> finds them), lower than <paramref name="below"/>. Returns how many were written.
    /// </summary>
    public int TopsAt(float x, float z, float below, Span<float> into)
    {
        var n = 0;
        var cell = Row(z) * _cols + Col(x);
        for (var k = _cellStart[cell]; k < _cellStart[cell + 1] && n < into.Length; k++)
        {
            var b = _boxes[_cellBoxes[k]];
            if (x > b.Min.X && x < b.Max.X && z > b.Min.Z && z < b.Max.Z && b.Max.Y < below)
                into[n++] = b.Max.Y;
        }
        return n;
    }
}
