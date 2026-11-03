using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Smoothed rims for drawing dug ground (<see cref="TerrainEdits.CellFaces{T}"/> with <c>smooth</c>): marching squares on
/// the columns. At every column corner (a vertex, where four columns meet) and every height, count the columns that are
/// solid there: with one, that column's quarter by the vertex loses its corner triangle (a convex step is cut); with three,
/// the open column's quarter gains one (a concave step is filled). The triangles' legs are half a column, so a staircase
/// becomes a straight line through the middles of its steps, and a ragged round hole reads as round. Two (side by side or
/// diagonal), none or all four: unchanged.
/// <para>Each quarter of a column depends only on its own vertex, so neighbours always agree and nothing is left open.
/// Only columns with such a vertex (the rims) take this path; everything else is drawn as plain blocks, so it costs a few
/// faces along each rim and nothing elsewhere. Drawing only: collision, rays and physics stay the square columns (the
/// corners differ by at most 0.18 m on 0.5 m columns).</para>
/// </summary>
public sealed partial class TerrainEdits
{
    // A quarter of a column at one vertex: the inner triangle (centre side), the outer one (vertex side), both, or none.
    private const int QInner = 1, QOuter = 2, QFull = 3;

    /// <summary>
    /// A step shorter than this (metres) keeps its square corners: the 0.5 m terraces of an island's underside and its
    /// hole's floor would turn nearly every column there into a rim (about 3x the triangles) for little to see.
    /// </summary>
    public const float MinSmoothStep = 0.75f;

    /// <summary>Most height levels four columns can have between them (every span's bottom and top).</summary>
    private const int MaxLevels = 4 * MaxSpans * 2 + 2;

    /// <summary>
    /// Marks which of cell (i, j)'s (Sub + 1)^2 vertices are rim vertices (some height has one or three of its four
    /// columns solid). False when it has none.
    /// </summary>
    private bool RimVertices(int i, int j, Span<bool> rim)
    {
        var vs = Sub + 1;
        var any = false;
        Span<float> ys = stackalloc float[MaxLevels];
        Span<byte> solid = stackalloc byte[MaxLevels];
        for (var b = 0; b < vs; b++)
        for (var a = 0; a < vs; a++)
        {
            int vi = i * Sub + a, vj = j * Sub + b;
            var r = false;
            if (Corner(vi - 1, vj - 1, out var c0) && Corner(vi, vj - 1, out var c1) && Corner(vi - 1, vj, out var c2) && Corner(vi, vj, out var c3))
            {
                if (!Plain(c0, c1, c2, c3))
                {
                    var n = Levels(Spans(c0), Spans(c1), Spans(c2), Spans(c3), ys, solid);
                    for (var k = 0; k < n && !r; k++)
                        r = Count(ys, solid, k) is 1 or 3;
                }
            }
            rim[b * vs + a] = r;
            any |= r;
        }
        return any;
    }

    /// <summary>
    /// A quick no for most vertices: the four columns have as many spans, each bottom and top within
    /// <see cref="MinSmoothStep"/> across them and in the same order, so every odd count is a short step.
    /// </summary>
    private bool Plain(int c0, int c1, int c2, int c3)
    {
        var n = _count[c0];
        if (_count[c1] != n || _count[c2] != n || _count[c3] != n)
            return false;
        float[] s0 = _spans[c0], s1 = _spans[c1], s2 = _spans[c2], s3 = _spans[c3];
        var below = float.NegativeInfinity;
        for (var k = 1; k < n * 2; k++)
        {
            float lo = MathF.Min(MathF.Min(s0[k], s1[k]), MathF.Min(s2[k], s3[k]));
            float hi = MathF.Max(MathF.Max(s0[k], s1[k]), MathF.Max(s2[k], s3[k]));
            if (hi - lo >= MinSmoothStep || lo <= below)
                return false;
            below = hi;
        }
        return true;
    }

    private bool Corner(int ci, int cj, out int c)
    {
        c = Col(ci, cj);
        return c >= 0;
    }

    private ReadOnlySpan<float> Spans(int c) => new(_spans[c], 0, _count[c] * 2);

    /// <summary>
    /// The height levels of four columns: interval k runs from ys[k] to ys[k + 1] (ys[0] is -infinity, ys[n] +infinity),
    /// and solid[k] has bit m set when column m is solid in it. Returns the interval count n.
    /// </summary>
    private static int Levels(ReadOnlySpan<float> s0, ReadOnlySpan<float> s1, ReadOnlySpan<float> s2, ReadOnlySpan<float> s3, Span<float> ys, Span<byte> solid)
    {
        var m = 1;
        ys[0] = float.NegativeInfinity;
        Add(s0, ys, ref m);
        Add(s1, ys, ref m);
        Add(s2, ys, ref m);
        Add(s3, ys, ref m);
        // Sort the finite levels (a few dozen at most) and drop repeats.
        var w = ys[1..m];
        for (var k = 1; k < w.Length; k++)
        {
            var v = w[k];
            var q = k - 1;
            while (q >= 0 && w[q] > v)
            {
                w[q + 1] = w[q];
                q--;
            }
            w[q + 1] = v;
        }
        var u = 1;
        for (var k = 1; k < m; k++)
            if (u == 1 || ys[k] - ys[u - 1] > 1e-4f)
                ys[u++] = ys[k];
        ys[u] = float.PositiveInfinity;
        var n = u;
        for (var k = 0; k < n; k++)
        {
            var mid = k == 0 ? ys[1] - 1 : k == n - 1 ? ys[k] + 1 : (ys[k] + ys[k + 1]) * 0.5f;
            if (n == 1)
                mid = 0;
            solid[k] = (byte)((Solid(s0, mid) ? 1 : 0) | (Solid(s1, mid) ? 2 : 0) | (Solid(s2, mid) ? 4 : 0) | (Solid(s3, mid) ? 8 : 0));
        }
        return n;

        static void Add(ReadOnlySpan<float> s, Span<float> ys, ref int m)
        {
            foreach (var v in s)
                if (float.IsFinite(v))
                    ys[m++] = v;
        }
    }

    /// <summary>The solid count of interval k at a vertex, as smoothing sees it: a short odd step (<see cref="MinSmoothStep"/>) counts as plain.</summary>
    private static int Count(ReadOnlySpan<float> ys, ReadOnlySpan<byte> solid, int k)
    {
        var cnt = System.Numerics.BitOperations.PopCount(solid[k]);
        return cnt is 1 or 3 && ys[k + 1] - ys[k] < MinSmoothStep ? 2 : cnt;
    }

    /// <summary>A column's quarter at a vertex in one height interval: from its own solidity and the vertex's solid count.</summary>
    private static int Quarter(bool solid, int count) => solid ? count == 1 ? QInner : QFull : count == 3 ? QOuter : 0;

    /// <summary>Does the column have a span starting at or above <paramref name="y"/> (ground over it: darker)?</summary>
    private static bool CoveredAbove(ReadOnlySpan<float> s, float y)
    {
        for (var k = 2; k < s.Length; k += 2)
            if (s[k] >= y - 1e-4f)
                return true;
        return false;
    }

    /// <summary>Is the column a floating island's (its own top far above the base ground)?</summary>
    private bool IslandColumn(int c, int ci, int cj) =>
        _islands.Length > 0 && _orig[c] > _t.BaseGroundTop(_t.OriginX + (ci + 0.5f) * Size, _t.OriginZ + (cj + 0.5f) * Size) + 0.5f;

    /// <summary>The colour of a span top at <paramref name="y"/> in column c (as the plain blocks colour it).</summary>
    private (Vector3, float) TopColour(int c, int ci, int cj, float y)
    {
        var s = Spans(c);
        var covered = CoveredAbove(s, y + 1e-3f);
        return TopShade(c, ci, cj, y, covered);
    }

    /// <summary>The colour of a span's underside at <paramref name="y"/> in column c.</summary>
    private (Vector3, float) UnderColour(int c, int ci, int cj, float y)
    {
        var (col, rock) = Shade(c, y, !IslandColumn(c, ci, cj));
        return (col * 0.85f, rock);
    }

    /// <summary>
    /// A rim column's faces, a quarter at a time: plain quarters (their vertex isn't a rim vertex) as blocks, the others
    /// with their corners cut or filled. <paramref name="rim"/> starts at the column's low corner, rows <paramref name="vs"/> apart.
    /// </summary>
    private void RimColumn<T>(ref T sink, int ci, int cj, int c, ReadOnlySpan<bool> rim, int vs) where T : struct, IDigFaceSink
    {
        var s = Spans(c);
        var orig = _orig[c];
        var top = s[^1];
        float x0 = _t.OriginX + ci * Size, z0 = _t.OriginZ + cj * Size;
        float cx = x0 + Size * 0.5f, cz = z0 + Size * 0.5f;
        var mid = new Vector2(cx, cz);
        Span<float> ys = stackalloc float[MaxLevels];
        Span<byte> solid = stackalloc byte[MaxLevels];
        for (var qb = 0; qb < 2; qb++)
        for (var qa = 0; qa < 2; qa++)
        {
            var sx = qa == 0 ? -1 : 1;
            var sz = qb == 0 ? -1 : 1;
            var v = new Vector2(qa == 0 ? x0 : x0 + Size, qb == 0 ? z0 : z0 + Size);
            var ma = new Vector2(v.X, cz); // the middle of the edge shared with the X neighbour (A)
            var mb = new Vector2(cx, v.Y); // ... and with the Z neighbour (B)
            int ai = ci + sx, bj = cj + sz;
            var nA = new Vector3(sx, 0, 0);
            var nB = new Vector3(0, 0, sz);
            if (!rim[qb * vs + qa])
            {
                // A plain quarter: the blocks' faces, cut to the quarter.
                for (var k = 0; k < s.Length; k += 2)
                {
                    float lo = s[k], hi = s[k + 1];
                    var (col, rock) = TopColour(c, ci, cj, hi);
                    Quarter(ref sink, QFull, v, ma, mid, mb, hi, Vector3.UnitY, col, rock);
                    if (k > 0)
                    {
                        var (bc, br) = UnderColour(c, ci, cj, lo);
                        Quarter(ref sink, QFull, v, ma, mid, mb, lo, -Vector3.UnitY, bc, br);
                    }
                }
                Side(ref sink, s, c, top, ai, cj, v, ma, nA);
                Side(ref sink, s, c, top, ci, bj, v, mb, nB);
                continue;
            }
            // A rim quarter: the four columns' levels at this vertex (all four are dug: RimVertices).
            int cA = Col(ai, cj), cB = Col(ci, bj), cD = Col(ai, bj);
            ReadOnlySpan<float> sA = Spans(cA), sB = Spans(cB), sD = Spans(cD);
            var n = Levels(s, sA, sB, sD, ys, solid);
            var diagOut = Vector3.Normalize(new Vector3(sx, 0, sz)); // (towards the vertex)
            int runDiag = 0, runA = 0, runB = 0;
            float startDiag = 0, startA = 0, startB = 0;
            var prev = 0;
            for (var k = 0; k <= n; k++)
            {
                // The state in interval k (none past the top).
                int q = 0, qA = 0, qB = 0;
                byte sol = 0;
                if (k < n)
                {
                    sol = solid[k];
                    var cnt = Count(ys, solid, k);
                    q = Quarter((sol & 1) != 0, cnt);
                    qA = Quarter((sol & 2) != 0, cnt);
                    qB = Quarter((sol & 4) != 0, cnt);
                }
                var y = ys[k];
                // Flat faces where the quarter changes: up where ground below meets less above, down the other way.
                if (k > 0 && k < n && float.IsFinite(y))
                {
                    var up = prev & ~q;
                    if (up != 0)
                    {
                        var below = solid[k - 1];
                        var (oc, oci, ocj) = (below & 1) != 0 ? (c, ci, cj) : Owner(c, cA, cB, cD, ci, cj, ai, bj, y, true);
                        var (col, rock) = TopColour(oc, oci, ocj, y);
                        Quarter(ref sink, up, v, ma, mid, mb, y, Vector3.UnitY, col, rock);
                    }
                    var down = q & ~prev;
                    if (down != 0)
                    {
                        var (oc, oci, ocj) = (sol & 1) != 0 ? (c, ci, cj) : Owner(c, cA, cB, cD, ci, cj, ai, bj, y, false);
                        var (col, rock) = UnderColour(oc, oci, ocj, y);
                        Quarter(ref sink, down, v, ma, mid, mb, y, -Vector3.UnitY, col, rock);
                    }
                }
                // Walls, in runs: the cut or fill's slant, and the outer half edges where this quarter is ground and the
                // neighbour's isn't (the ground's side draws it, as the blocks do).
                var diag = q is QInner or QOuter ? q : 0;
                if (diag != runDiag)
                {
                    if (runDiag != 0)
                    {
                        var own = runDiag == QInner ? c : cA;
                        var nrm = runDiag == QInner ? diagOut : -diagOut;
                        Wall(ref sink, ma, mb, startDiag, startDiag, y, y, nrm, own, CoveredAbove(Spans(own), y));
                    }
                    runDiag = diag;
                    startDiag = y;
                }
                var wa = (q & QOuter) != 0 && (qA & QOuter) == 0 ? 1 : 0;
                if (wa != runA)
                {
                    if (runA != 0)
                        Wall(ref sink, v, ma, startA, startA, y, y, nA, c, CoveredAbove(s, y));
                    runA = wa;
                    startA = y;
                }
                var wb = (q & QOuter) != 0 && (qB & QOuter) == 0 ? 1 : 0;
                if (wb != runB)
                {
                    if (runB != 0)
                        Wall(ref sink, v, mb, startB, startB, y, y, nB, c, CoveredAbove(s, y));
                    runB = wb;
                    startB = y;
                }
                prev = q;
            }
        }
    }

    /// <summary>
    /// Which neighbour a filled corner's flat face belongs to (for its colour): one whose span ends (<paramref name="top"/>)
    /// or starts there, else the X neighbour.
    /// </summary>
    private (int C, int Ci, int Cj) Owner(int c, int cA, int cB, int cD, int ci, int cj, int ai, int bj, float y, bool top)
    {
        if (Ends(Spans(cA), y, top))
            return (cA, ai, cj);
        if (Ends(Spans(cB), y, top))
            return (cB, ci, bj);
        if (Ends(Spans(cD), y, top))
            return (cD, ai, bj);
        return (cA, ai, cj);

        static bool Ends(ReadOnlySpan<float> s, float y, bool top)
        {
            for (var k = top ? 1 : 0; k < s.Length; k += 2)
                if (MathF.Abs(s[k] - y) < 1e-4f)
                    return true;
            return false;
        }
    }

    /// <summary>A flat face of a quarter at height y: the whole quarter, its inner triangle or its outer one.</summary>
    private static void Quarter<T>(ref T sink, int shape, Vector2 v, Vector2 ma, Vector2 mid, Vector2 mb, float y, Vector3 n, Vector3 col, float rock)
        where T : struct, IDigFaceSink
    {
        var V = new Vector3(v.X, y, v.Y);
        var A = new Vector3(ma.X, y, ma.Y);
        var C = new Vector3(mid.X, y, mid.Y);
        var B = new Vector3(mb.X, y, mb.Y);
        switch (shape)
        {
            case QFull:
                Emit(ref sink, V, A, C, B, n, col, rock);
                break;
            case QInner:
                Emit(ref sink, A, C, B, B, n, col, rock);
                break;
            case QOuter:
                Emit(ref sink, V, A, B, B, n, col, rock);
                break;
        }
    }

    /// <summary>
    /// Is (x, y, z) inside the ground as <see cref="CellFaces{T}"/> with <c>smooth</c> draws it (cut and filled corners)?
    /// For the self-tests: every drawn face has this solid just behind it and air in front.
    /// </summary>
    internal bool DrawnSolid(float x, float z, float y)
    {
        var fx = (x - _t.OriginX) / Size;
        var fz = (z - _t.OriginZ) / Size;
        int ci = (int)MathF.Floor(fx), cj = (int)MathF.Floor(fz);
        var c = Col(ci, cj);
        if (c < 0)
            return _t.SolidAt(x, z, y);
        var own = Solid(Spans(c), y);
        int vi = fx - ci >= 0.5f ? ci + 1 : ci, vj = fz - cj >= 0.5f ? cj + 1 : cj;
        if (!(Corner(vi - 1, vj - 1, out var c0) && Corner(vi, vj - 1, out var c1) && Corner(vi - 1, vj, out var c2) && Corner(vi, vj, out var c3)))
            return own;
        Span<float> ys = stackalloc float[MaxLevels];
        Span<byte> solid = stackalloc byte[MaxLevels];
        var n = Levels(Spans(c0), Spans(c1), Spans(c2), Spans(c3), ys, solid);
        var k = 0;
        while (k + 1 < n && ys[k + 1] <= y)
            k++;
        var count = Count(ys, solid, k);
        var near = MathF.Abs(fx - vi) * 2 + MathF.Abs(fz - vj) * 2 < 1; // (in the quarter's outer triangle)
        return Quarter(own, count) switch
        {
            QFull => true,
            QInner => !near,
            QOuter => near,
            _ => false,
        };
    }
}
