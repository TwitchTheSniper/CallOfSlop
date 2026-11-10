using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Smooth tops on a planetoid's dug ground (<see cref="GroundTops"/>): each dug cell's corner altitudes, worked out when
/// first asked for and kept until something next to the cell changes (<see cref="Bump"/>), and the queries that read them.
/// </summary>
public sealed partial class PlanetEdits
{
    // Per column: its top's altitude at its four corners (0,0), (1,0), (0,1), (1,1) of the face's grid.
    private float[] _tops = [];
    // Per dug cell (its first column / columns a cell): the ground as built at its (Sub + 1)^2 column corners.
    private float[] _cornerBase = [];
    private bool[] _topsMade = [];
    // Per column: its open top's colour (rgb, rockiness), for drawing; per dug cell: are they made?
    private Vector4[] _topColour = [];
    private bool[] _coloursMade = [];

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
        Array.Resize(ref _coloursMade, cells);
    }

    /// <summary>Every cell's tops are stale (everything was filled in again).</summary>
    private void ForgetTops()
    {
        Array.Clear(_topsMade);
        Array.Clear(_coloursMade);
    }

    /// <summary>The tops of the cells i0..i1, j0..j1 of a face are stale.</summary>
    private void ForgetTops(int face, int i0, int j0, int i1, int j1)
    {
        if (_slot == null)
            return;
        for (var j = Math.Max(j0, 0); j <= Math.Min(j1, _n - 1); j++)
        for (var i = Math.Max(i0, 0); i <= Math.Min(i1, _n - 1); i++)
        {
            var s = SlotAt(face, i, j);
            if (s == 0)
                continue;
            var o = CellOrdinal(s - 1);
            if (o < _topsMade.Length)
                _topsMade[o] = false;
            if (o < _coloursMade.Length)
                _coloursMade[o] = false;
        }
    }

    /// <summary>Makes dug cell (i, j)'s top colours if they are stale (drawing only: collision never asks).</summary>
    private void EnsureColours(int face, int i, int j, int first)
    {
        var ord = CellOrdinal(first);
        if (ord < _coloursMade.Length && _coloursMade[ord])
            return;
        for (var b = 0; b < Sub; b++)
        for (var a = 0; a < Sub; a++)
        {
            var c = first + b * Sub + a;
            ColDir(face, i * Sub + a, j * Sub + b, out var x, out var y, out var z);
            var (col, rock) = TopShade(c, x, y, z, _spans[c][_count[c] * 2 - 1], false);
            _topColour[c] = new Vector4(col, Math.Clamp(rock, 0f, 1f));
        }
        _coloursMade[ord] = true;
    }

    /// <summary>Makes dug cell (i, j)'s tops if they are stale. Returns its first column.</summary>
    private int EnsureTops(int face, int i, int j)
    {
        var first = SlotAt(face, i, j) - 1;
        var ord = CellOrdinal(first);
        if (ord < _topsMade.Length && _topsMade[ord])
            return first;
        MakeTops(face, i, j, first, ord);
        return first;
    }

    private void MakeTops(int face, int i, int j, int first, int ord)
    {
        var vs = Sub + 1;
        Span<byte> kind = stackalloc byte[4];
        Span<float> lo = stackalloc float[4];
        Span<float> top = stackalloc float[4];
        Span<float> delta = stackalloc float[4];
        Span<float> h = stackalloc float[4];
        for (var b = 0; b < vs; b++)
        for (var a = 0; a < vs; a++)
        {
            int vi = i * Sub + a, vj = j * Sub + b;
            _p.DirOf(face, i + (double)a / Sub, j + (double)b / Sub, out var x, out var y, out var z);
            var at = BaseAlt(x, y, z);
            _cornerBase[ord * vs * vs + b * vs + a] = at;
            for (var k = 0; k < 4; k++)
            {
                int ci = vi - 1 + (k & 1), cj = vj - 1 + (k >> 1);
                bool offI = (uint)ci >= (uint)Cols, offJ = (uint)cj >= (uint)Cols;
                if (offI && offJ)
                {
                    kind[k] = GroundTops.None; // (a cube's corner: three columns meet there, not four)
                    continue;
                }
                var c = offI || offJ ? Neighbour(face, ci, cj) : Col(face, ci, cj);
                if (c < 0)
                {
                    kind[k] = GroundTops.Untouched;
                    delta[k] = 0;
                    continue;
                }
                var s = _spans[c];
                var n = _count[c] * 2;
                lo[k] = MathF.Max(s[n - 2], _inner);
                top[k] = s[n - 1];
                delta[k] = top[k] - _orig[c];
                kind[k] = top[k] > lo[k] ? GroundTops.Dug : GroundTops.None; // (dug open to a hollow planetoid's cavity: nothing there)
            }
            GroundTops.Corner(kind, lo, top, delta, at, (float)Size, h);
            // The corner is (1,1) of the column before it both ways, (0,1) of the one before it in j, and so on.
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
        _topsMade[ord] = true;
    }

    /// <summary>The four corner altitudes of dug column <paramref name="c"/>'s top (its cell's tops are made).</summary>
    private ReadOnlySpan<float> Tops(int c) => new(_tops, c * GroundTops.Stride, GroundTops.Stride);

    /// <summary>
    /// The solid spans of the dug column in unit direction (x, y, z) and its top's altitude right there (the highest span ends
    /// at it: <see cref="GroundTops"/>); false where the ground is untouched.
    /// </summary>
    public bool TryColumn(double x, double y, double z, out ReadOnlySpan<float> spans, out float top)
    {
        spans = default;
        top = 0;
        if (_cols == 0)
            return false;
        if (!ColumnAt(x, y, z, out var f, out var ci, out var cj, out var u, out var v, out var c))
            return false;
        spans = new ReadOnlySpan<float>(_spans[c], 0, _count[c] * 2);
        top = GroundTops.At(Tops(c), u, v);
        return true;
    }

    /// <summary>The dug column a unit direction is in, where across it (0..1 each way) and its storage index, its tops made; false where the ground is untouched.</summary>
    private bool ColumnAt(double x, double y, double z, out int face, out int ci, out int cj, out float u, out float v, out int c)
    {
        _p.Locate(x, y, z, out face, out var fi, out var fj);
        double gi = fi * Sub, gj = fj * Sub;
        ci = Math.Clamp((int)Math.Floor(gi), 0, Cols - 1);
        cj = Math.Clamp((int)Math.Floor(gj), 0, Cols - 1);
        u = (float)Math.Clamp(gi - ci, 0, 1);
        v = (float)Math.Clamp(gj - cj, 0, 1);
        c = Col(face, ci, cj);
        if (c < 0)
            return false;
        EnsureTops(face, ci / Sub, cj / Sub);
        return true;
    }

    /// <summary>The normal of a dug column's top at (u, v) across it: of the triangle there, facing out.</summary>
    private Vector3 TopNormal(int face, int ci, int cj, int c, float u, float v)
    {
        GroundTops.Slope(Tops(c), u, v, out var di, out var dj);
        // The column's own edges along i and along j (differences worked out in doubles: half a metre, kilometres from the centre).
        _p.DirOf(face, (double)ci / Sub, (double)cj / Sub, out var ax, out var ay, out var az);
        _p.DirOf(face, (ci + 1.0) / Sub, (double)cj / Sub, out var bx, out var by, out var bz);
        _p.DirOf(face, (double)ci / Sub, (cj + 1.0) / Sub, out var ex, out var ey, out var ez);
        var along = new Vector3((float)((bx - ax) * _r), (float)((by - ay) * _r), (float)((bz - az) * _r));
        var across = new Vector3((float)((ex - ax) * _r), (float)((ey - ay) * _r), (float)((ez - az) * _r));
        var up = Vector3.Normalize(new Vector3((float)(bx + ex), (float)(by + ey), (float)(bz + ez)));
        var n = Vector3.Cross(across + up * dj, along + up * di);
        var len = n.Length();
        if (len < 1e-12f)
            return up;
        n /= len;
        return Vector3.Dot(n, up) < 0 ? -n : n;
    }

    /// <summary>The ground's normal at a point of the dug column in a unit direction (its top's slope when the point is on or over the highest span; else "up").</summary>
    public Vector3 NormalAt(double x, double y, double z, float alt)
    {
        var up = new Vector3((float)x, (float)y, (float)z);
        if (!ColumnAt(x, y, z, out var f, out var ci, out var cj, out var u, out var v, out var c))
            return up;
        var s = _spans[c];
        return alt >= s[_count[c] * 2 - 2] ? TopNormal(f, ci, cj, c, u, v) : up;
    }
}
