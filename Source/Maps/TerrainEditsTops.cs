using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Smooth tops on a flat map's dug ground (<see cref="GroundTops"/>): each dug cell's corner heights, worked out when first
/// asked for and kept until something next to the cell changes (<see cref="Bump"/>), and the queries that read them.
/// <para>Two kinds of column keep a level top: a floating island's (its own ground, not the map's surface), and the ones
/// along a smoothed rim (TerrainEditsSmooth.cs: a tall step's corners are cut and filled there, on level tops).</para>
/// </summary>
public sealed partial class TerrainEdits
{
    // Per column: its top's height at its four corners (0,0), (1,0), (0,1), (1,1).
    private float[] _tops = [];
    // Per dug cell (its first column / columns a cell): the ground as built at its (Sub + 1)^2 column corners.
    private float[] _cornerBase = [];
    private bool[] _topsMade = [];
    // Per column: its open top's colour (rgb, rockiness), for drawing; per dug cell: are they made?
    private Vector4[] _topColour = [];
    private bool[] _coloursMade = [];
    // Per column: the ground's own colour there as built (made with the top colours), which dug faces start from.
    private Vector3[] _ownColour = [];

    private int CellOrdinal(int first) => first / (Sub * Sub);

    /// <summary>The tops' arrays follow the columns' (grown with them, so a query never allocates).</summary>
    private void GrowTops()
    {
        var vs = Sub + 1;
        var cells = _spans.Length / (Sub * Sub) + 1;
        Array.Resize(ref _tops, _spans.Length * GroundTops.Stride);
        Array.Resize(ref _topsMade, cells);
        Array.Resize(ref _cornerBase, cells * vs * vs);
        Array.Resize(ref _topColour, _spans.Length);
        Array.Resize(ref _ownColour, _spans.Length);
        Array.Resize(ref _coloursMade, cells);
    }

    /// <summary>Every cell's tops are stale (everything was filled in again).</summary>
    private void ForgetTops()
    {
        Array.Clear(_topsMade);
        Array.Clear(_coloursMade);
    }

    /// <summary>The tops of the cells i0..i1, j0..j1 are stale.</summary>
    private void ForgetTops(int i0, int j0, int i1, int j1)
    {
        if (_slot == null)
            return;
        for (var j = Math.Max(j0, 0); j <= Math.Min(j1, _cellsZ - 1); j++)
        for (var i = Math.Max(i0, 0); i <= Math.Min(i1, _cellsX - 1); i++)
        {
            var s = SlotAt(i, j);
            if (s != 0)
                ForgetCell(s - 1);
        }
    }

    private void ForgetCell(int first)
    {
        var o = CellOrdinal(first);
        if (o < _topsMade.Length)
            _topsMade[o] = false;
        if (o < _coloursMade.Length)
            _coloursMade[o] = false;
    }

    /// <summary>Makes dug cell (i, j)'s tops if they are stale. Returns its first column.</summary>
    private int EnsureTops(int i, int j)
    {
        var first = SlotAt(i, j) - 1;
        var ord = CellOrdinal(first);
        if (ord < _topsMade.Length && _topsMade[ord])
            return first;
        MakeTops(i, j, first, ord);
        return first;
    }

    private void MakeTops(int i, int j, int first, int ord)
    {
        var vs = Sub + 1;
        // Rim vertices of the cell and a ring round it: every corner of a column with one is level (the rim's own drawing).
        var ws = Sub + 3;
        Span<bool> rim = stackalloc bool[ws * ws];
        var anyRim = false;
        for (var b = 0; b < ws; b++)
        for (var a = 0; a < ws; a++)
            anyRim |= rim[b * ws + a] = RimVertex(i * Sub + a - 1, j * Sub + b - 1);
        Span<byte> kind = stackalloc byte[4];
        Span<float> lo = stackalloc float[4];
        Span<float> top = stackalloc float[4];
        Span<float> delta = stackalloc float[4];
        Span<float> h = stackalloc float[4];
        for (var b = 0; b < vs; b++)
        for (var a = 0; a < vs; a++)
        {
            int vi = i * Sub + a, vj = j * Sub + b;
            var at = _t.BaseGroundTop(_t.OriginX + vi * Size, _t.OriginZ + vj * Size);
            _cornerBase[ord * vs * vs + b * vs + a] = at;
            var level = false;
            if (anyRim)
                for (var q = 0; q < 9 && !level; q++)
                    level = rim[(b + q / 3) * ws + a + q % 3];
            for (var k = 0; k < 4; k++)
            {
                int ci = vi - 1 + (k & 1), cj = vj - 1 + (k >> 1);
                if ((uint)ci >= (uint)ColsX || (uint)cj >= (uint)ColsZ)
                {
                    kind[k] = GroundTops.None; // (the terrain's edge)
                    continue;
                }
                var c = Col(ci, cj);
                if (c < 0)
                {
                    kind[k] = GroundTops.Untouched;
                    delta[k] = 0;
                    continue;
                }
                var s = _spans[c];
                var n = _count[c] * 2;
                lo[k] = s[n - 2];
                top[k] = s[n - 1];
                delta[k] = top[k] - _orig[c];
                kind[k] = level || (_islands.Length > 0 && IslandColumn(c, ci, cj)) ? GroundTops.None : GroundTops.Dug;
            }
            GroundTops.Corner(kind, lo, top, delta, at, Size, h);
            // The corner is (1,1) of the column before it both ways, (0,1) of the one before it in z, and so on.
            for (var k = 0; k < 4; k++)
            {
                int ca = a - 1 + (k & 1), cb = b - 1 + (k >> 1);
                if ((uint)ca >= (uint)Sub || (uint)cb >= (uint)Sub)
                    continue;
                var c = first + cb * Sub + ca;
                _tops[c * GroundTops.Stride + 3 - k] = kind[k] == GroundTops.Dug ? h[k] : _spans[c][_count[c] * 2 - 1];
                _tops[c * GroundTops.Stride + 4] = _spans[c][_count[c] * 2 - 1];
            }
        }
        for (var c = first; c < first + Sub * Sub; c++)
        {
            var t = new Span<float>(_tops, c * GroundTops.Stride, GroundTops.Stride);
            var own = _spans[c][_count[c] * 2 - 1];
            var floor = _spans[c][_count[c] * 2 - 2];
            if (own > floor)
                t[4] = GroundTops.Middle(t, own, MathF.Min(floor + TerrainEdits.MinSpan, own));
        }
        _topsMade[ord] = true;
    }

    /// <summary>Makes dug cell (i, j)'s top colours if they are stale (drawing only: collision never asks).</summary>
    private void EnsureColours(int i, int j, int first)
    {
        var ord = CellOrdinal(first);
        if (ord < _coloursMade.Length && _coloursMade[ord])
            return;
        for (var b = 0; b < Sub; b++)
        for (var a = 0; a < Sub; a++)
        {
            var c = first + b * Sub + a;
            int ci = i * Sub + a, cj = j * Sub + b;
            _ownColour[c] = SurfaceColour(ci, cj, IslandColumn(c, ci, cj) ? _orig[c] : float.NaN).Item1;
            var (col, rock) = TopShade(c, ci, cj, _spans[c][_count[c] * 2 - 1], false);
            _topColour[c] = new Vector4(col, Math.Clamp(rock, 0f, 1f));
        }
        _coloursMade[ord] = true;
    }

    /// <summary>The four corner heights of dug column <paramref name="c"/>'s top (its cell's tops are made).</summary>
    private ReadOnlySpan<float> Tops(int c) => new(_tops, c * GroundTops.Stride, GroundTops.Stride);

    /// <summary>
    /// The solid spans of the dug column at (x, z) and its top's height right there (the highest span ends at it:
    /// <see cref="GroundTops"/>); false where the ground is untouched.
    /// </summary>
    public bool TryColumn(float x, float z, out ReadOnlySpan<float> spans, out float top)
    {
        spans = default;
        top = 0;
        if (_slot == null || !ColumnAt(x, z, out _, out _, out var u, out var v, out var c))
            return false;
        spans = new ReadOnlySpan<float>(_spans[c], 0, _count[c] * 2);
        top = GroundTops.At(Tops(c), u, v);
        return true;
    }

    /// <summary>The dug column at (x, z), where across it (0..1 each way) and its storage index, its tops made; false where the ground is untouched.</summary>
    private bool ColumnAt(float x, float z, out int ci, out int cj, out float u, out float v, out int c)
    {
        float gx = (x - _t.OriginX) / Size, gz = (z - _t.OriginZ) / Size;
        ci = Math.Clamp((int)MathF.Floor(gx), 0, ColsX - 1);
        cj = Math.Clamp((int)MathF.Floor(gz), 0, ColsZ - 1);
        u = Math.Clamp(gx - ci, 0f, 1f);
        v = Math.Clamp(gz - cj, 0f, 1f);
        c = Col(ci, cj);
        if (c < 0)
            return false;
        // (a cell's columns are stored together, Sub x Sub of them: its ordinal and first column straight from c)
        var per = Sub * Sub;
        if (!_topsMade[c / per])
            MakeTops(ci / Sub, cj / Sub, c - c % per, c / per);
        return true;
    }

    /// <summary>The normal of dug column <paramref name="c"/>'s top at (u, v) across it: of the triangle there.</summary>
    private Vector3 TopNormal(int c, float u, float v)
    {
        GroundTops.Slope(Tops(c), u, v, out var dx, out var dz);
        return Vector3.Normalize(new Vector3(-dx, Size, -dz));
    }

    /// <summary>The ground's normal at a point of the dug column at (x, z): its top's slope when the point is on or over the highest span, else up.</summary>
    public Vector3 NormalAt(float x, float z, float y)
    {
        if (_slot == null || !ColumnAt(x, z, out _, out _, out var u, out var v, out var c))
            return Vector3.UnitY;
        return y >= _spans[c][_count[c] * 2 - 2] ? TopNormal(c, u, v) : Vector3.UnitY;
    }
}
