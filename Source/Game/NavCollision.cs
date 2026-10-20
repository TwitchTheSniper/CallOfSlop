using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// A read-only snapshot of a map's collision for building its <see cref="NavGraph"/>: the boxes filed into a grid of
/// square columns, so each of the build's many ground probes, body checks and sight rays tests the few boxes nearby
/// instead of every box on the map (big maps have well over a thousand). It gives exactly the answers of
/// <see cref="World.RaycastWorld"/> and <see cref="World.Blocked"/> (same box tests, same terrain), only faster;
/// the self-test compares the two on every map.
/// </summary>
public sealed class NavCollision
{
    public const float Cell = 4f;
    private const float Pad = 0.05f; // boxes are filed into every column they come within this distance of

    private readonly Box[] _boxes;
    private readonly ITerrainCollision? _terrain;
    private readonly float _minX, _minZ, _maxX, _maxZ;
    private readonly int _cols, _rows;
    private readonly int[] _cellStart; // boxes of column c: _cellBoxes[_cellStart[c] .. _cellStart[c + 1]), in map order
    private readonly int[] _cellBoxes;
    private readonly int[] _stamp; // per box: the ray that last tested it (each box once per ray)
    private int _ray;

    public int BoxCount => _boxes.Length;

    public NavCollision(Map map)
    {
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
            ForCells(b, c => counts[c + 1]++);
        for (var c = 0; c < _cols * _rows; c++)
            counts[c + 1] += counts[c];
        _cellStart = counts;
        _cellBoxes = new int[counts[^1]];
        var fill = (int[])counts.Clone();
        for (var i = 0; i < _boxes.Length; i++)
        {
            var index = i;
            ForCells(_boxes[i], c => _cellBoxes[fill[c]++] = index);
        }
    }

    private void ForCells(Box b, Action<int> action)
    {
        int c0 = Col(b.Min.X - Pad), c1 = Col(b.Max.X + Pad), r0 = Row(b.Min.Z - Pad), r1 = Row(b.Max.Z + Pad);
        for (var r = r0; r <= r1; r++)
            for (var c = c0; c <= c1; c++)
                action(r * _cols + c);
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
