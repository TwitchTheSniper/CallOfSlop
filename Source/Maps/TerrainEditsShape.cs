using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The ground drill's shaping ops on flat ground (<see cref="GroundShape"/>): a plan gives every column of the op's footprint the top
/// it should have (NaN: left alone), then a grade cuts [top, top + <see cref="GroundShape.Headroom"/>] and both fill the gap under the
/// top. The last plan is kept for the ground's <see cref="Version"/>, so the server's measure, its burial checks and the op itself
/// plan once. Nothing allocates once the plan's arrays have grown to the biggest op.
/// </summary>
public sealed partial class TerrainEdits
{
    private float[] _shT = [], _shS = [], _shTmp = [];
    private Vector3[] _shP = [];
    private DigOp _shOp;
    private int _shVersion = -1, _shN, _shCi0, _shCj0, _shW;

    /// <summary>Plans <paramref name="op"/> (or keeps the plan made for it on this ground). Returns the footprint's columns (0: nothing to shape).</summary>
    private int ShapePlan(in DigOp op)
    {
        if (_shVersion == Version && _shOp == op)
            return _shN;
        _shOp = op;
        _shVersion = Version;
        _shN = 0;
        if (!GroundShape.Fits(in op))
            return 0;
        var grade = GroundShape.IsGrade(op.Kind);
        var reach = grade ? op.Radius : op.Radius + 2 * GroundShape.Kernel;
        var lo = grade ? Vector3.Min(op.At, op.To) : op.At;
        var hi = grade ? Vector3.Max(op.At, op.To) : op.At;
        var ci0 = Math.Max((int)MathF.Floor((lo.X - reach - _t.OriginX) / Size), 0);
        var ci1 = Math.Min((int)MathF.Floor((hi.X + reach - _t.OriginX) / Size), ColsX - 1);
        var cj0 = Math.Max((int)MathF.Floor((lo.Z - reach - _t.OriginZ) / Size), 0);
        var cj1 = Math.Min((int)MathF.Floor((hi.Z + reach - _t.OriginZ) / Size), ColsZ - 1);
        if (ci1 < ci0 || cj1 < cj0)
            return 0;
        var w = ci1 - ci0 + 1;
        var n = w * (cj1 - cj0 + 1);
        if (_shT.Length < n)
        {
            var size = Math.Max(n, _shT.Length * 2);
            Array.Resize(ref _shT, size);
            Array.Resize(ref _shS, size);
            Array.Resize(ref _shTmp, size);
            Array.Resize(ref _shP, size);
        }
        (_shCi0, _shCj0, _shW) = (ci0, cj0, w);
        Span<float> s = stackalloc float[(MaxSpans + 1) * 2];
        float ax = op.At.X, az = op.At.Z, ex = op.To.X - ax, ez = op.To.Z - az;
        var len2 = ex * ex + ez * ez;
        var r2 = op.Radius * op.Radius;
        var probe = op.At.Y + op.Radius;
        for (var cj = cj0; cj <= cj1; cj++)
        for (var ci = ci0; ci <= ci1; ci++)
        {
            var k = (cj - cj0) * w + (ci - ci0);
            var x = _t.OriginX + (ci + 0.5f) * Size;
            var z = _t.OriginZ + (cj + 0.5f) * Size;
            if (grade)
            {
                var t = len2 > 1e-6f ? Math.Clamp(((x - ax) * ex + (z - az) * ez) / len2, 0f, 1f) : 0f;
                float dx = x - (ax + ex * t), dz = z - (az + ez * t);
                _shT[k] = dx * dx + dz * dz < r2 ? op.At.Y + (op.To.Y - op.At.Y) * t : float.NaN;
                continue;
            }
            _shP[k] = new Vector3(x, 0, z);
            _shS[k] = MathF.Min(GroundBelow(ColumnNow(ci, cj, s), probe), probe); // (the surface under the aim; under an overhang, the aim's own height)
        }
        if (!grade)
        {
            _shS.AsSpan(0, n).CopyTo(_shT);
            GroundShape.Close(_shP.AsSpan(0, n), _shT.AsSpan(0, n), GroundShape.Kernel, _shTmp.AsSpan(0, n));
            for (var k = 0; k < n; k++)
            {
                var d = _shP[k] - new Vector3(op.At.X, 0, op.At.Z);
                var top = MathF.Min(_shT[k], _shS[k] + GroundShape.SmoothRise);
                _shT[k] = d.LengthSquared() < r2 && top - _shS[k] >= MinSpan ? top : float.NaN;
            }
        }
        return _shN = n;
    }

    private (int ci, int cj) ShapeColumn(int k) => (_shCi0 + k % _shW, _shCj0 + k / _shW);

    private int ApplyShape(in DigOp op)
    {
        var n = ShapePlan(in op);
        var grade = GroundShape.IsGrade(op.Kind);
        var depth = grade ? GroundShape.Depth : GroundShape.SmoothRise;
        Span<float> s = stackalloc float[(MaxSpans + 1) * 2];
        int changed = 0, ci0 = int.MaxValue, cj0 = int.MaxValue, ci1 = -1, cj1 = -1;
        for (var k = 0; k < n; k++)
        {
            var top = _shT[k];
            if (float.IsNaN(top))
                continue;
            var (ci, cj) = ShapeColumn(k);
            var spans = ColumnNow(ci, cj, s);
            var fill = GroundShape.FillFrom(spans, top, depth, out var lo);
            var cut = grade && GroundShape.SolidIn(spans, top, top + GroundShape.Headroom);
            if (!fill && !cut)
                continue;
            var col = Col(ci, cj);
            if (col < 0)
                col = Touch(ci / Sub, cj / Sub) + cj % Sub * Sub + ci % Sub;
            var did = cut && Subtract(col, top, top + GroundShape.Headroom);
            did |= fill && Add(col, lo, top, op.Kind);
            if (!did)
                continue;
            changed++;
            (ci0, cj0, ci1, cj1) = (Math.Min(ci0, ci), Math.Min(cj0, cj), Math.Max(ci1, ci), Math.Max(cj1, cj));
        }
        if (changed == 0)
            return 0;
        Log.Add(op);
        Bump(ci0 / Sub - 1, cj0 / Sub - 1, ci1 / Sub + 1, cj1 / Sub + 1);
        return changed;
    }

    /// <inheritdoc cref="IDigGround.MeasureShape"/>
    public float MeasureShape(in DigOp op, ReadOnlySpan<GroundKind> surfaceGives, float stoneDepth, Span<float> cutByKind, out float fill)
    {
        cutByKind.Clear();
        fill = 0;
        if (!GroundShape.IsShape(op.Kind))
            return 0;
        var n = ShapePlan(in op);
        var grade = GroundShape.IsGrade(op.Kind);
        var depth = grade ? GroundShape.Depth : GroundShape.SmoothRise;
        Span<float> s = stackalloc float[(MaxSpans + 1) * 2];
        Span<float> s2 = stackalloc float[(MaxSpans + 1) * 2];
        var cut = 0f;
        for (var k = 0; k < n; k++)
        {
            var top = _shT[k];
            if (float.IsNaN(top))
                continue;
            var (ci, cj) = ShapeColumn(k);
            if (grade)
                cut += CutVolume(ci, cj, top, top + GroundShape.Headroom, surfaceGives, stoneDepth, cutByKind, s2);
            if (GroundShape.FillFrom(ColumnNow(ci, cj, s), top, depth, out var lo))
                fill += (top - lo) * Size * Size; // (all air: the ground under the top is where it starts)
        }
        return cut;
    }

    /// <summary><see cref="FillTouches"/> for a shaping op: does the ground it adds reach into the box?</summary>
    private bool ShapeFillTouches(in DigOp op, Vector3 min, Vector3 max)
    {
        var n = ShapePlan(in op);
        var depth = GroundShape.IsGrade(op.Kind) ? GroundShape.Depth : GroundShape.SmoothRise;
        ColumnOf(min.X, min.Z, out var bi0, out var bj0);
        ColumnOf(max.X, max.Z, out var bi1, out var bj1);
        Span<float> s = stackalloc float[(MaxSpans + 1) * 2];
        for (var k = 0; k < n; k++)
        {
            var top = _shT[k];
            if (float.IsNaN(top))
                continue;
            var (ci, cj) = ShapeColumn(k);
            if (ci < bi0 || ci > bi1 || cj < bj0 || cj > bj1)
                continue;
            if (GroundShape.FillFrom(ColumnNow(ci, cj, s), top, depth, out var lo) && MathF.Min(top, max.Y) - MathF.Max(lo, min.Y) > 0.001f)
                return true;
        }
        return false;
    }
}
