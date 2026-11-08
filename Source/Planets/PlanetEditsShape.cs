using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The ground drill's shaping ops on a planetoid (<see cref="GroundShape"/>, as TerrainEditsShape.cs on flat ground): heights are
/// altitudes along each radial column. A grade's top is the altitude between its ends' (each end's distance from the centre less
/// the radius), along the column's place on the segment; a smooth's surface is closed over the columns' places on the sphere. The
/// columns of every face the op reaches are planned once for the ground's <see cref="Version"/>.
/// </summary>
public sealed partial class PlanetEdits
{
    private float[] _shT = [], _shS = [], _shTmp = [];
    private Vector3[] _shP = [];
    private int[] _shF = [], _shI = [], _shJ = [];
    private DigOp _shOp;
    private int _shVersion = -1, _shGeneration = -1, _shN;

    private int ShapePlan(in DigOp op)
    {
        if (_shVersion == Version && _shGeneration == Generation && _shOp == op)
            return _shN;
        _shOp = op;
        _shVersion = Version;
        _shGeneration = Generation;
        _shN = 0;
        if (!GroundShape.Fits(in op))
            return 0;
        var grade = GroundShape.IsGrade(op.Kind);
        var centre = grade ? (op.At + op.To) * 0.5f : op.At;
        var reach = grade ? Vector3.Distance(op.At, op.To) * 0.5f + op.Radius + 0.5f : op.Radius + 2 * GroundShape.Kernel;
        var n = 0;
        for (var f = 0; f < 6; f++)
            if (Footprint(centre, reach, f, out var ci0, out var ci1, out var cj0, out var cj1))
                n += (ci1 - ci0 + 1) * (cj1 - cj0 + 1);
        if (n == 0)
            return 0;
        if (_shT.Length < n)
        {
            var size = Math.Max(n, _shT.Length * 2);
            Array.Resize(ref _shT, size);
            Array.Resize(ref _shS, size);
            Array.Resize(ref _shTmp, size);
            Array.Resize(ref _shP, size);
            Array.Resize(ref _shF, size);
            Array.Resize(ref _shI, size);
            Array.Resize(ref _shJ, size);
        }
        // The ends relative to the centre, and their altitudes.
        var a = op.At - _p.Center;
        var b = (grade ? op.To : op.At) - _p.Center;
        var altA = a.Length() - (float)_r;
        var altB = b.Length() - (float)_r;
        var e = b - a;
        var len2 = e.LengthSquared();
        var mid = (float)_r + (altA + altB) * 0.5f;
        var r2 = op.Radius * op.Radius;
        var probe = altA + op.Radius;
        var aimOnSphere = a.LengthSquared() > 1e-6f ? Vector3.Normalize(a) * (float)_r : a;
        Span<float> s = stackalloc float[(MaxSpans + 1) * 2];
        var m = 0;
        for (var f = 0; f < 6; f++)
        {
            if (!Footprint(centre, reach, f, out var ci0, out var ci1, out var cj0, out var cj1))
                continue;
            for (var cj = cj0; cj <= cj1; cj++)
            for (var ci = ci0; ci <= ci1; ci++)
            {
                ColDir(f, ci, cj, out var dx, out var dy, out var dz);
                var d = new Vector3((float)dx, (float)dy, (float)dz);
                (_shF[m], _shI[m], _shJ[m]) = (f, ci, cj);
                if (grade)
                {
                    var p = d * mid;
                    var t = len2 > 1e-6f ? Math.Clamp(Vector3.Dot(p - a, e) / len2, 0f, 1f) : 0f;
                    var x = a + e * t;
                    var xl = x.Length();
                    var lateral = xl > 1e-3f ? p - x * (mid / xl) : p - x; // (the segment's point put on the column's sphere: a ramp's rise isn't width)
                    _shT[m] = lateral.LengthSquared() < r2 ? altA + (altB - altA) * t : float.NaN;
                }
                else
                {
                    _shP[m] = d * (float)_r;
                    _shS[m] = MathF.Min(TerrainEdits.GroundBelow(ColumnNow(f, ci, cj, s), probe), probe);
                }
                m++;
            }
        }
        if (!grade)
        {
            _shS.AsSpan(0, m).CopyTo(_shT);
            GroundShape.Close(_shP.AsSpan(0, m), _shT.AsSpan(0, m), GroundShape.Kernel, _shTmp.AsSpan(0, m));
            for (var k = 0; k < m; k++)
            {
                var top = MathF.Min(_shT[k], _shS[k] + GroundShape.SmoothRise);
                _shT[k] = Vector3.DistanceSquared(_shP[k], aimOnSphere) < r2 && top - _shS[k] >= MinSpan ? top : float.NaN;
            }
        }
        return _shN = m;
    }

    private int ApplyShape(in DigOp op)
    {
        var n = ShapePlan(in op);
        var grade = GroundShape.IsGrade(op.Kind);
        var depth = grade ? GroundShape.Depth : GroundShape.SmoothRise;
        Span<float> s = stackalloc float[(MaxSpans + 1) * 2];
        Span<int> box = stackalloc int[6 * 4];
        for (var f = 0; f < 6; f++)
            (box[f * 4], box[f * 4 + 1], box[f * 4 + 2], box[f * 4 + 3]) = (int.MaxValue, int.MaxValue, -1, -1);
        var changed = 0;
        for (var k = 0; k < n; k++)
        {
            var top = _shT[k];
            if (float.IsNaN(top))
                continue;
            int f = _shF[k], ci = _shI[k], cj = _shJ[k];
            var spans = ColumnNow(f, ci, cj, s);
            var fill = GroundShape.FillFrom(spans, top, depth, out var lo);
            var cut = grade && GroundShape.SolidIn(spans, top, top + GroundShape.Headroom);
            if (!fill && !cut)
                continue;
            var col = Col(f, ci, cj);
            if (col < 0)
                col = Touch(f, ci / Sub, cj / Sub) + cj % Sub * Sub + ci % Sub;
            var did = cut && Subtract(col, top, top + GroundShape.Headroom);
            did |= fill && Add(col, lo, top, op.Kind);
            if (!did)
                continue;
            changed++;
            box[f * 4] = Math.Min(box[f * 4], ci);
            box[f * 4 + 1] = Math.Min(box[f * 4 + 1], cj);
            box[f * 4 + 2] = Math.Max(box[f * 4 + 2], ci);
            box[f * 4 + 3] = Math.Max(box[f * 4 + 3], cj);
        }
        if (changed == 0)
            return 0;
        for (var f = 0; f < 6; f++)
            if (box[f * 4 + 2] >= 0)
                Bump(f, box[f * 4] / Sub - 1, box[f * 4 + 1] / Sub - 1, box[f * 4 + 2] / Sub + 1, box[f * 4 + 3] / Sub + 1);
        Log.Add(op);
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
            int f = _shF[k], ci = _shI[k], cj = _shJ[k];
            if (grade)
                cut += CutVolume(f, ci, cj, top, top + GroundShape.Headroom, surfaceGives, stoneDepth, cutByKind, s2);
            if (GroundShape.FillFrom(ColumnNow(f, ci, cj, s), top, depth, out var lo))
                fill += (top - lo) * ColumnArea(f, ci, cj);
        }
        return cut;
    }

    /// <summary><see cref="FillTouches"/> for a shaping op: does the ground it adds reach into the box (along each column's line)?</summary>
    private bool ShapeFillTouches(in DigOp op, Vector3 min, Vector3 max)
    {
        var n = ShapePlan(in op);
        var depth = GroundShape.IsGrade(op.Kind) ? GroundShape.Depth : GroundShape.SmoothRise;
        Span<float> s = stackalloc float[(MaxSpans + 1) * 2];
        for (var k = 0; k < n; k++)
        {
            var top = _shT[k];
            if (float.IsNaN(top))
                continue;
            int f = _shF[k], ci = _shI[k], cj = _shJ[k];
            if (!GroundShape.FillFrom(ColumnNow(f, ci, cj, s), top, depth, out var lo))
                continue;
            ColDir(f, ci, cj, out var dx, out var dy, out var dz);
            double s0 = double.NegativeInfinity, s1 = double.PositiveInfinity;
            if (!Slab(_p.Cx, dx, min.X, max.X, ref s0, ref s1) || !Slab(_p.Cy, dy, min.Y, max.Y, ref s0, ref s1) || !Slab(_p.Cz, dz, min.Z, max.Z, ref s0, ref s1))
                continue;
            if (MathF.Min(top, (float)(s1 - _r)) - MathF.Max(lo, (float)(s0 - _r)) > 0.001f)
                return true;
        }
        return false;

        static bool Slab(double o, double d, double lo, double hi, ref double s0, ref double s1)
        {
            if (Math.Abs(d) < 1e-12)
                return o >= lo && o <= hi;
            double a = (lo - o) / d, b = (hi - o) / d;
            if (a > b)
                (a, b) = (b, a);
            s0 = Math.Max(s0, a);
            s1 = Math.Min(s1, b);
            return s1 >= s0;
        }
    }
}
