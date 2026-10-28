namespace CallOfSlop;

/// <summary>
/// The crowd as a surface (docs/zombies/MASS-HORDE.md, "Crowd physics"): a height over the floor from how dense the
/// crowd is in each network cell (<see cref="Msg.ZCrowd"/>'s grid, which the server counts every few ticks and a client
/// has as its picture). A cell packed at <see cref="Packed"/> zombies a square metre is <see cref="Height"/> m high; a
/// thinner one lower, in proportion. Between cell centres the height is bilinear, so the surface slopes and its slope
/// pushes a player sideways towards thinner crowd. Reads the counts in place: nothing is built or allocated.
/// </summary>
public sealed class CrowdSurface
{
    /// <summary>A packed cell's height (m, shoulder height of a tight crowd) and what packed is (zombies a square metre: 0.7 m apart).</summary>
    public float Height = 1.3f, Packed = 2f;

    /// <summary>Below this height (m) the crowd is too thin to be a surface: a few zombies are bodies, not ground.</summary>
    public float MinHeight = 0.3f;

    private int _cols, _rows;
    private float _cell = 4f, _minX, _minZ, _perZombie;
    private byte[] _count = [];

    /// <summary>The floor the surface rises from.</summary>
    public float FloorY;

    /// <summary>Is there a grid bound?</summary>
    public bool Bound => _count.Length > 0 && _cols > 0 && _rows > 0;

    /// <summary>Points the surface at a grid of counts (no copy: the counts are read as they are each query).</summary>
    public void Bind(int cols, int rows, float cell, float minX, float minZ, float floorY, byte[] counts)
    {
        if (cols <= 0 || rows <= 0 || counts.Length < cols * rows || !(cell > 0))
        {
            Unbind();
            return;
        }
        _cols = cols;
        _rows = rows;
        _cell = cell;
        _minX = minX;
        _minZ = minZ;
        FloorY = floorY;
        _count = counts;
        _perZombie = Height / (Packed * cell * cell);
    }

    public void Unbind()
    {
        _count = [];
        _cols = _rows = 0;
    }

    /// <summary>A cell's height (m over the floor; 0 off the grid).</summary>
    public float CellHeight(int c, int r)
    {
        if (c < 0 || r < 0 || c >= _cols || r >= _rows)
            return 0;
        var h = _count[r * _cols + c] * _perZombie;
        return h > Height ? Height : h;
    }

    /// <summary>The surface's height over the floor at (x, z): bilinear between the four nearest cell centres (0 with no crowd).</summary>
    public float HeightAt(float x, float z)
    {
        if (!Bound)
            return 0;
        var u = (x - _minX) / _cell - 0.5f;
        var v = (z - _minZ) / _cell - 0.5f;
        var c = (int)MathF.Floor(u);
        var r = (int)MathF.Floor(v);
        var fu = u - c;
        var fv = v - r;
        var h00 = CellHeight(c, r);
        var h10 = CellHeight(c + 1, r);
        var h01 = CellHeight(c, r + 1);
        var h11 = CellHeight(c + 1, r + 1);
        return (h00 * (1 - fu) + h10 * fu) * (1 - fv) + (h01 * (1 - fu) + h11 * fu) * fv;
    }

    /// <summary>The surface's slope at (x, z): how the height changes per metre in x and z (central differences, half a cell apart).</summary>
    public (float Dx, float Dz) SlopeAt(float x, float z)
    {
        if (!Bound)
            return (0, 0);
        var d = _cell * 0.5f;
        return ((HeightAt(x + d, z) - HeightAt(x - d, z)) / (2 * d), (HeightAt(x, z + d) - HeightAt(x, z - d)) / (2 * d));
    }
}
