using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// A uniform grid over the map's floor plan for horde neighbour queries (separation, crowding at a spawner, who heard a
/// stimulus): rebuilt from scratch every tick with a counting sort (O(n), no allocation once its arrays have grown),
/// cells of <see cref="Cell"/> metres. Positions are kept with the indices so a query touches no agent objects.
/// </summary>
public sealed class HordeGrid
{
    /// <summary>Cell size (m): a little over the separation radius, so a separation query reads at most 3 x 3 cells.</summary>
    public const float Cell = 1.25f;

    private float _minX, _minZ;
    private int _cols = 1, _rows = 1;
    private int[] _start = new int[2];
    private int[] _fill = [];
    private int[] _cellOf = new int[64];
    private int[] _items = new int[64];
    private Vector3[] _pos = new Vector3[64];
    private int _count;

    /// <summary>Agents in the grid this tick.</summary>
    public int Count => _count;

    /// <summary>Sets the area covered (the map's half size; outside it positions clamp to the edge cells).</summary>
    public void Setup(float halfX, float halfZ)
    {
        _minX = -halfX - Cell;
        _minZ = -halfZ - Cell;
        _cols = Math.Clamp((int)MathF.Ceiling((halfX * 2 + Cell * 2) / Cell), 1, 1024);
        _rows = Math.Clamp((int)MathF.Ceiling((halfZ * 2 + Cell * 2) / Cell), 1, 1024);
        if (_start.Length < _cols * _rows + 1)
            _start = new int[_cols * _rows + 1];
    }

    private int CellAt(float x, float z)
    {
        var cx = Math.Clamp((int)((x - _minX) / Cell), 0, _cols - 1);
        var cz = Math.Clamp((int)((z - _minZ) / Cell), 0, _rows - 1);
        return cz * _cols + cx;
    }

    /// <summary>Sizes the per-agent arrays for <paramref name="n"/> agents now, so a growing horde never allocates mid-round.</summary>
    public void Reserve(int n)
    {
        if (_cellOf.Length >= n)
            return;
        _cellOf = new int[n];
        _items = new int[n];
        _pos = new Vector3[n];
    }

    /// <summary>Rebuilds from <paramref name="positions"/> (index i is agent i; <paramref name="active"/>[i] false: left out).</summary>
    public void Build(ReadOnlySpan<Vector3> positions, ReadOnlySpan<bool> active)
    {
        var n = positions.Length;
        if (_cellOf.Length < n)
        {
            var cap = Math.Max(n, _cellOf.Length * 2);
            _cellOf = new int[cap];
            _items = new int[cap];
            _pos = new Vector3[cap];
        }
        var cells = _cols * _rows;
        Array.Clear(_start, 0, cells + 1);
        _count = 0;
        for (var i = 0; i < n; i++)
        {
            if (!active[i])
            {
                _cellOf[i] = -1;
                continue;
            }
            var c = CellAt(positions[i].X, positions[i].Z);
            _cellOf[i] = c;
            _start[c + 1]++;
            _count++;
        }
        for (var c = 0; c < cells; c++)
            _start[c + 1] += _start[c];
        // Fill (stable: lower indices first in each cell, so queries see agents in a fixed order).
        if (_fill.Length < cells)
            _fill = new int[cells];
        var fill = _fill;
        Array.Copy(_start, fill, cells);
        for (var i = 0; i < n; i++)
        {
            var c = _cellOf[i];
            if (c < 0)
                continue;
            var k = fill[c]++;
            _items[k] = i;
            _pos[k] = positions[i];
        }
    }

    /// <summary>
    /// Every agent within <paramref name="r"/> (flat distance) of <paramref name="p"/>, written into
    /// <paramref name="into"/> (up to its length). Returns how many.
    /// </summary>
    public int Query(Vector3 p, float r, Span<int> into)
    {
        var c0 = Math.Clamp((int)((p.X - r - _minX) / Cell), 0, _cols - 1);
        var c1 = Math.Clamp((int)((p.X + r - _minX) / Cell), 0, _cols - 1);
        var r0 = Math.Clamp((int)((p.Z - r - _minZ) / Cell), 0, _rows - 1);
        var r1 = Math.Clamp((int)((p.Z + r - _minZ) / Cell), 0, _rows - 1);
        var n = 0;
        var r2 = r * r;
        for (var cz = r0; cz <= r1; cz++)
            for (var cx = c0; cx <= c1; cx++)
            {
                var cell = cz * _cols + cx;
                for (var k = _start[cell]; k < _start[cell + 1]; k++)
                {
                    var q = _pos[k];
                    var dx = q.X - p.X;
                    var dz = q.Z - p.Z;
                    if (dx * dx + dz * dz > r2)
                        continue;
                    if (n == into.Length)
                        return n;
                    into[n++] = _items[k];
                }
            }
        return n;
    }

    /// <summary>
    /// The separation push on agent <paramref name="self"/> at <paramref name="p"/> from the others within
    /// <paramref name="r"/> (flat, away from each, stronger the closer): the old all-pairs loop's maths, from the grid.
    /// </summary>
    public Vector3 Push(int self, Vector3 p, float r, float strength)
    {
        var c0 = Math.Clamp((int)((p.X - r - _minX) / Cell), 0, _cols - 1);
        var c1 = Math.Clamp((int)((p.X + r - _minX) / Cell), 0, _cols - 1);
        var r0 = Math.Clamp((int)((p.Z - r - _minZ) / Cell), 0, _rows - 1);
        var r1 = Math.Clamp((int)((p.Z + r - _minZ) / Cell), 0, _rows - 1);
        var push = Vector3.Zero;
        var r2 = r * r;
        for (var cz = r0; cz <= r1; cz++)
            for (var cx = c0; cx <= c1; cx++)
            {
                var cell = cz * _cols + cx;
                for (var k = _start[cell]; k < _start[cell + 1]; k++)
                {
                    if (_items[k] == self)
                        continue;
                    var d = p - _pos[k];
                    d.Y = 0;
                    var l2 = d.LengthSquared();
                    if (l2 > r2 || l2 < 1e-6f)
                        continue;
                    var l = MathF.Sqrt(l2);
                    push += d / l * (r - l) * strength;
                }
            }
        return push;
    }

    /// <summary>Is any agent within <paramref name="r"/> of <paramref name="p"/>?</summary>
    public bool Any(Vector3 p, float r)
    {
        Span<int> one = stackalloc int[1];
        return Query(p, r, one) > 0;
    }
}
