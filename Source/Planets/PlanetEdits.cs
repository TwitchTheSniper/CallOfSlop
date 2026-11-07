using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Dug ground on a <see cref="Planetoid"/> (docs/planets/PLANETS.md "Digging"): the flat terrain's layered columns
/// (<see cref="TerrainEdits"/>, after Peytavie et al., "Arches", 2009) stood up along "up". The first change in a planetoid
/// cell (face, i, j) turns it into <see cref="Sub"/> x <see cref="Sub"/> <b>radial columns</b> about half a metre wide, each a
/// short list of solid spans in <b>altitude</b> (metres above <see cref="Planetoid.Radius"/>), read off the ground's
/// triangles along the column's middle. Untouched ground stays the planetoid's triangles and costs nothing.
/// <para>
/// A column's walls are planes through the centre: on an equiangular cube-sphere every grid line is a great circle, so a
/// column is a frustum between two altitudes and its neighbour's wall is the same plane. Along a face's seam the columns of
/// the two faces line up exactly (the grid points on a cube edge are the same on both faces), so a crater on a seam or on a
/// cube corner is one crater; a column off a face's edge is found through its direction (<see cref="Planetoid.Locate"/>).
/// </para>
/// <para>
/// An op is a ball in the world (<see cref="DigOp"/>, the flat map's own wire format): for every column whose middle line
/// passes through it, the interval of that line inside the ball (a crater's flattened along the column) is taken out or put
/// in. Column directions are worked out once per use and rounded to 2^-30, so every peer applies the same ops to the same
/// numbers (square roots only after that), and ends with the same ground.
/// </para>
/// Runtime state only: never baked, never in the map's fingerprint, cleared when a match restores the map.
/// </summary>
public sealed class PlanetEdits : IDigGround
{
    /// <summary>Planetoid cells per side of a drawing chunk.</summary>
    public const int ChunkCells = TerrainEdits.ChunkCells;

    private const float MinSpan = TerrainEdits.MinSpan;
    private const int MaxSpans = TerrainEdits.MaxSpans;
    private const int SlotShift = 6, SlotPage = 1 << SlotShift, SlotMask = SlotPage - 1;
    private const double DirQuantum = 1073741824.0; // 2^30

    private readonly Planetoid _p;
    private readonly int _n;
    private readonly double _r;

    /// <summary>Columns per cell side.</summary>
    public readonly int Sub;

    /// <summary>Columns along a face's side.</summary>
    public readonly int Cols;

    /// <summary>A column's width at a face's middle (metres).</summary>
    public readonly double Size;

    /// <summary>Drawing chunks along a face's side (<see cref="ChunkCells"/> cells each).</summary>
    public readonly int ChunksPerSide;

    /// <summary>
    /// The lowest altitude anything can be dug to (bedrock: <see cref="TerrainEdits.BedrockDepth"/> under the lowest ground).
    /// On a hollow planetoid it is the cavity's roof: a column dug that far is open to the cavity.
    /// </summary>
    public readonly float Bedrock;

    /// <summary>A hollow planetoid's cavity roof (<see cref="Planetoid.Inner"/>; -infinity when solid): no span is solid below it.</summary>
    private readonly float _inner;

    /// <summary>Every op applied since the map was last restored, in order.</summary>
    public readonly List<DigOp> Log = new();

    List<DigOp> IDigGround.Ops => Log;

    /// <summary>Bumped on every change anywhere.</summary>
    public int Version { get; private set; }

    /// <summary>Bumped when everything is filled in again (the drawing drops its blocks).</summary>
    public int Generation { get; private set; }

    /// <summary>Has any column been dug right through a hollow planetoid's crust (the cavity can be seen from outside)?</summary>
    public bool OpenToCavity { get; private set; }

    /// <summary>Most a fill has raised a column over the surface as built (metres): rays far from the ground allow for it.</summary>
    public float MaxRise { get; private set; }

    // Per cell: 0 = untouched, else 1 + its first column; paged in blocks of SlotPage x SlotPage cells per face.
    private int[]?[]? _slot;
    private readonly int _pagesPerSide;
    private float[][] _spans = [];
    private int[] _count = [];
    private float[] _orig = [];
    private byte[] _fillKind = [];
    private float[] _fillLo = [];
    private int _cols;
    private readonly List<int> _touchedCells = new();
    private readonly Dictionary<int, int> _chunkVersion = new();
    private readonly List<int> _chunks = new();

    public PlanetEdits(Planetoid p)
    {
        _p = p;
        _n = p.N;
        _r = p.Radius;
        Sub = Math.Max(1, (int)Math.Round(p.Cell / TerrainEdits.TargetSize));
        Cols = _n * Sub;
        Size = p.Cell / Sub;
        ChunksPerSide = (_n + ChunkCells - 1) / ChunkCells;
        _pagesPerSide = (_n + SlotMask) >> SlotShift;
        _inner = p.Inner;
        Bedrock = p.Hollow ? _inner : p.MinHeight - TerrainEdits.BedrockDepth;
    }

    public Planetoid Planet => _p;

    /// <summary>Has any ground been dug?</summary>
    public bool Any => _cols > 0;

    /// <summary>Cells turned into columns so far.</summary>
    public int TouchedCells => _touchedCells.Count;

    /// <summary>Chunks that have ever held dug cells this generation, in the order they were first touched (the drawing walks them).</summary>
    public IReadOnlyList<int> Chunks => _chunks;

    /// <summary>A chunk's change count (0: never touched). Key: (face x per side + row) x per side + column.</summary>
    public int ChunkVersion(int key) => _chunkVersion.TryGetValue(key, out var v) ? v : 0;

    /// <summary>The face and first cell of a chunk key.</summary>
    public void ChunkOf(int key, out int face, out int i0, out int j0)
    {
        var per = ChunksPerSide;
        face = key / (per * per);
        var rest = key - face * per * per;
        j0 = rest / per * ChunkCells;
        i0 = rest % per * ChunkCells;
    }

    // ---- Cells and columns --------------------------------------------------------------------------------------------

    /// <summary>Has cell (i, j) of <paramref name="face"/> been dug (its columns, not the triangles, are the ground there)?</summary>
    public bool Touched(int face, int i, int j) => _slot != null && (uint)i < (uint)_n && (uint)j < (uint)_n && SlotAt(face, i, j) != 0;

    /// <summary><see cref="Touched"/>, for a cell that may be off the face's edge (then the neighbouring face's cell there).</summary>
    public bool TouchedAround(int face, int i, int j)
    {
        if (_slot == null)
            return false;
        if ((uint)i < (uint)_n && (uint)j < (uint)_n)
            return SlotAt(face, i, j) != 0;
        _p.DirOf(face, i + 0.5, j + 0.5, out var x, out var y, out var z);
        _p.Locate(x, y, z, out var f, out var fi, out var fj);
        return Touched(f, Math.Clamp((int)Math.Floor(fi), 0, _n - 1), Math.Clamp((int)Math.Floor(fj), 0, _n - 1));
    }

    private int SlotAt(int face, int i, int j) =>
        _slot![(face * _pagesPerSide + (j >> SlotShift)) * _pagesPerSide + (i >> SlotShift)] is { } p ? p[((j & SlotMask) << SlotShift) | (i & SlotMask)] : 0;

    private ref int SlotRef(int face, int i, int j)
    {
        _slot ??= new int[]?[6 * _pagesPerSide * _pagesPerSide];
        var p = _slot[(face * _pagesPerSide + (j >> SlotShift)) * _pagesPerSide + (i >> SlotShift)] ??= new int[SlotPage * SlotPage];
        return ref p[((j & SlotMask) << SlotShift) | (i & SlotMask)];
    }

    /// <summary>Column (ci, cj) of a face in this object's storage, or -1 when its cell is untouched or it's off the face.</summary>
    private int Col(int face, int ci, int cj)
    {
        if (_slot == null || (uint)ci >= (uint)Cols || (uint)cj >= (uint)Cols)
            return -1;
        var s = SlotAt(face, ci / Sub, cj / Sub);
        return s == 0 ? -1 : s - 1 + cj % Sub * Sub + ci % Sub;
    }

    /// <summary>A column's middle direction, rounded to 2^-30 (the same numbers on every peer: ops use nothing else).</summary>
    public void ColDir(int face, int ci, int cj, out double x, out double y, out double z)
    {
        _p.DirOf(face, (ci + 0.5) / Sub, (cj + 0.5) / Sub, out x, out y, out z);
        x = Math.Round(x * DirQuantum) / DirQuantum;
        y = Math.Round(y * DirQuantum) / DirQuantum;
        z = Math.Round(z * DirQuantum) / DirQuantum;
    }

    /// <summary>The ground's altitude along a column's middle as built (the triangles there).</summary>
    private float BaseTop(int face, int ci, int cj)
    {
        ColDir(face, ci, cj, out var x, out var y, out var z);
        return BaseAlt(x, y, z);
    }

    private float BaseAlt(double x, double y, double z) => (float)(_p.SurfaceRadius(new Vector3((float)x, (float)y, (float)z), out _) - _r);

    /// <summary>The column a unit direction is in (face, column indices).</summary>
    public void ColumnAt(double x, double y, double z, out int face, out int ci, out int cj)
    {
        _p.Locate(x, y, z, out face, out var fi, out var fj);
        ci = Math.Clamp((int)Math.Floor(fi * Sub), 0, Cols - 1);
        cj = Math.Clamp((int)Math.Floor(fj * Sub), 0, Cols - 1);
    }

    /// <summary>The solid spans (altitudes, bottom-top pairs, lowest first) of the dug column in unit direction (x, y, z); false where the ground is untouched.</summary>
    public bool TryColumn(double x, double y, double z, out ReadOnlySpan<float> spans)
    {
        spans = default;
        if (_cols == 0)
            return false;
        ColumnAt(x, y, z, out var f, out var ci, out var cj);
        var c = Col(f, ci, cj);
        if (c < 0)
            return false;
        spans = new ReadOnlySpan<float>(_spans[c], 0, _count[c] * 2);
        return true;
    }

    /// <summary>Is the cell under unit direction (x, y, z), or one next to it, dug (rays step finely there)?</summary>
    public bool NearDug(double x, double y, double z)
    {
        if (_cols == 0)
            return false;
        _p.Locate(x, y, z, out var f, out var fi, out var fj);
        int i = Math.Clamp((int)Math.Floor(fi), 0, _n - 1), j = Math.Clamp((int)Math.Floor(fj), 0, _n - 1);
        for (var b = -1; b <= 1; b++)
        for (var a = -1; a <= 1; a++)
            if (TouchedAround(f, i + a, j + b))
                return true;
        return false;
    }

    /// <summary>The solid spans of column (face, ci, cj) now, dug or not, without touching it.</summary>
    private ReadOnlySpan<float> ColumnNow(int face, int ci, int cj, Span<float> into)
    {
        var c = Col(face, ci, cj);
        if (c >= 0)
            return new ReadOnlySpan<float>(_spans[c], 0, _count[c] * 2);
        into[0] = float.NegativeInfinity;
        into[1] = BaseTop(face, ci, cj);
        return into[..2];
    }

    /// <summary>How far a point is above the surface as built straight under it.</summary>
    public float AboveBuilt(Vector3 p)
    {
        double x = p.X - _p.Cx, y = p.Y - _p.Cy, z = p.Z - _p.Cz;
        var d = Math.Sqrt(x * x + y * y + z * z);
        if (d < 1e-3)
            return -(float)_r;
        x /= d;
        y /= d;
        z /= d;
        ColumnAt(x, y, z, out var f, out var ci, out var cj);
        var c = Col(f, ci, cj);
        var top = c >= 0 ? _orig[c] : BaseAlt(x, y, z);
        return (float)(d - _r) - top;
    }

    // ---- Changes ------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Applies one op (logged; quantise it first when it will be sent). Returns the columns it changed (0: nothing to dig,
    /// nothing to fill, or the log is full).
    /// </summary>
    public int Apply(DigOp op)
    {
        if (Log.Count >= TerrainEdits.MaxOps || !(op.Radius > 0) || !float.IsFinite(op.At.X + op.At.Y + op.At.Z))
            return 0;
        var changed = 0;
        var fill = op.Kind >= DigKind.FillDirt;
        Span<float> s = stackalloc float[(MaxSpans + 1) * 2];
        for (var f = 0; f < 6; f++)
        {
            if (!Footprint(op.At, op.Radius, f, out var ci0, out var ci1, out var cj0, out var cj1))
                continue;
            op.Rows(cj0, cj1, out cj0, out cj1); // (a big crater's band of each face: Game.MegaDigTick)
            if (cj1 < cj0)
                continue;
            var here = 0;
            for (var cj = cj0; cj <= cj1; cj++)
            for (var ci = ci0; ci <= ci1; ci++)
            {
                if (fill)
                {
                    if (!FillSpan(in op, f, ci, cj, s, out var flo, out var fhi))
                        continue;
                    var filled = Col(f, ci, cj);
                    if (filled < 0)
                        filled = Touch(f, ci / Sub, cj / Sub) + cj % Sub * Sub + ci % Sub;
                    if (Add(filled, flo, fhi, op.Kind))
                        here++;
                    continue;
                }
                if (!Ball(in op, f, ci, cj, out var lo, out var hi))
                    continue;
                var col = Col(f, ci, cj);
                if (col < 0)
                {
                    if (lo >= BaseTop(f, ci, cj))
                        continue; // (only touch a cell the dig really reaches)
                    col = Touch(f, ci / Sub, cj / Sub) + cj % Sub * Sub + ci % Sub;
                }
                if (Subtract(col, lo, hi))
                    here++;
            }
            if (here > 0)
                Bump(f, ci0 / Sub - 1, cj0 / Sub - 1, ci1 / Sub + 1, cj1 / Sub + 1);
            changed += here;
        }
        if (changed > 0)
            Log.Add(op); // (only ops that changed something: what clients and joiners replay)
        return changed;
    }

    /// <summary>
    /// The interval of column (face, ci, cj)'s middle line inside <paramref name="op"/>'s ball (a crater's flattened to
    /// <see cref="TerrainEdits.CraterDepth"/> along the column), as altitudes. False when the line misses it or only grazes it.
    /// </summary>
    private bool Ball(in DigOp op, int face, int ci, int cj, out float lo, out float hi)
    {
        lo = hi = 0;
        ColDir(face, ci, cj, out var dx, out var dy, out var dz);
        double cx = op.At.X - _p.Cx, cy = op.At.Y - _p.Cy, cz = op.At.Z - _p.Cz;
        var t = cx * dx + cy * dy + cz * dz;
        if (t <= 0)
            return false;
        var perp2 = cx * cx + cy * cy + cz * cz - t * t;
        var r2 = (double)op.Radius * op.Radius;
        if (perp2 >= r2)
            return false;
        var h = Math.Sqrt(r2 - Math.Max(perp2, 0)) * (op.Kind == DigKind.Crater ? TerrainEdits.CraterDepth : 1.0);
        lo = (float)Math.Max(t - h - _r, Bedrock);
        hi = (float)(t + h - _r);
        return hi - lo >= MinSpan;
    }

    /// <summary>
    /// The interval a fill adds to column (face, ci, cj): the ball over that column, reaching down to the ground under it
    /// when the gap is no taller than the ball's radius (as on flat ground: no air under a heap on a slope).
    /// </summary>
    private bool FillSpan(in DigOp op, int face, int ci, int cj, Span<float> scratch, out float lo, out float hi)
    {
        if (!Ball(in op, face, ci, cj, out lo, out hi))
            return false;
        var below = TerrainEdits.GroundBelow(ColumnNow(face, ci, cj, scratch), lo);
        if (below < lo && lo - below <= op.Radius)
            lo = below;
        return true;
    }

    /// <summary>The columns of <paramref name="face"/> a ball can reach (false: none). The ball's box projected onto the face's grid.</summary>
    private bool Footprint(Vector3 at, float r, int face, out int ci0, out int ci1, out int cj0, out int cj1)
    {
        ci0 = ci1 = cj0 = cj1 = 0;
        Planetoid.FaceBasis(face, out var fn, out var fu, out var fv);
        var c = at - _p.Center;
        double lo0 = double.MaxValue, hi0 = double.MinValue, lo1 = double.MaxValue, hi1 = double.MinValue;
        var any = false;
        for (var k = 0; k < 8; k++)
        {
            var p = c + new Vector3((k & 1) == 0 ? -r : r, (k & 2) == 0 ? -r : r, (k & 4) == 0 ? -r : r);
            var dn = Vector3.Dot(p, fn);
            if (dn <= 0.1f * p.Length())
                continue;
            double gi = (Planetoid.Unwarp(Vector3.Dot(p, fu) / dn) + 1) * 0.5 * Cols, gj = (Planetoid.Unwarp(Vector3.Dot(p, fv) / dn) + 1) * 0.5 * Cols;
            lo0 = Math.Min(lo0, gi);
            hi0 = Math.Max(hi0, gi);
            lo1 = Math.Min(lo1, gj);
            hi1 = Math.Max(hi1, gj);
            any = true;
        }
        if (!any || hi0 < -1 || lo0 > Cols + 1 || hi1 < -1 || lo1 > Cols + 1)
            return false;
        ci0 = Math.Max(0, (int)Math.Floor(lo0) - 1);
        ci1 = Math.Min(Cols - 1, (int)Math.Floor(hi0) + 1);
        cj0 = Math.Max(0, (int)Math.Floor(lo1) - 1);
        cj1 = Math.Min(Cols - 1, (int)Math.Floor(hi1) + 1);
        return ci0 <= ci1 && cj0 <= cj1;
    }

    /// <summary>What <paramref name="op"/> would change (cubic metres, by kind of ground), without changing it. As <see cref="TerrainEdits.Measure"/>.</summary>
    public float Measure(in DigOp op, ReadOnlySpan<GroundKind> surfaceGives, float stoneDepth, Span<float> byKind)
    {
        byKind.Clear();
        if (!(op.Radius > 0) || !float.IsFinite(op.At.X + op.At.Y + op.At.Z))
            return 0;
        Span<float> s = stackalloc float[(MaxSpans + 1) * 2];
        var total = 0f;
        for (var f = 0; f < 6; f++)
        {
            if (!Footprint(op.At, op.Radius, f, out var ci0, out var ci1, out var cj0, out var cj1))
                continue;
            for (var cj = cj0; cj <= cj1; cj++)
            for (var ci = ci0; ci <= ci1; ci++)
            {
                float lo, hi;
                if (op.Kind >= DigKind.FillDirt)
                {
                    if (!FillSpan(in op, f, ci, cj, s, out lo, out hi))
                        continue;
                    var open = hi - lo;
                    var have = ColumnNow(f, ci, cj, s);
                    for (var k = 0; k < have.Length; k += 2)
                        open -= MathF.Max(MathF.Min(have[k + 1], hi) - MathF.Max(have[k], lo), 0);
                    if (open > 0)
                    {
                        var v = open * ColumnArea(f, ci, cj);
                        byKind[op.Kind - DigKind.FillDirt] += v;
                        total += v;
                    }
                    continue;
                }
                if (!Ball(in op, f, ci, cj, out lo, out hi))
                    continue;
                var area = ColumnArea(f, ci, cj);
                var col = Col(f, ci, cj);
                var spans = ColumnNow(f, ci, cj, s);
                var orig = col >= 0 ? _orig[col] : spans[1];
                var surface = (int)SurfaceKind(f, ci, cj, surfaceGives);
                var stoneBelow = orig - stoneDepth;
                var fillFrom = col >= 0 && _fillKind[col] != 0 ? _fillLo[col] : float.PositiveInfinity;
                var fillKind = col >= 0 && _fillKind[col] != 0 ? _fillKind[col] - 1 : 0;
                for (var k = 0; k < spans.Length; k += 2)
                {
                    var a = MathF.Max(spans[k], lo);
                    var b = MathF.Min(spans[k + 1], hi);
                    if (b <= a)
                        continue;
                    total += (b - a) * area;
                    var inFill = MathF.Max(b - MathF.Max(a, fillFrom), 0);
                    byKind[fillKind] += inFill * area;
                    b -= inFill;
                    var inStone = MathF.Max(MathF.Min(b, stoneBelow) - a, 0);
                    byKind[(int)GroundKind.Stone] += inStone * area;
                    byKind[surface] += MathF.Max(b - a - inStone, 0) * area;
                }
            }
        }
        return total;
    }

    /// <summary>A column's cross-section at the ground (square metres).</summary>
    private float ColumnArea(int face, int ci, int cj)
    {
        _p.DirOf(face, (double)ci / Sub, (double)cj / Sub, out var ax, out var ay, out var az);
        _p.DirOf(face, (ci + 1.0) / Sub, (cj + 1.0) / Sub, out var bx, out var by, out var bz);
        _p.DirOf(face, (ci + 1.0) / Sub, (double)cj / Sub, out var cx, out var cy, out var cz);
        _p.DirOf(face, (double)ci / Sub, (cj + 1.0) / Sub, out var dx, out var dy, out var dz);
        var e = new Vector3((float)(bx - ax), (float)(by - ay), (float)(bz - az));
        var g = new Vector3((float)(dx - cx), (float)(dy - cy), (float)(dz - cz));
        return (float)(Vector3.Cross(e, g).Length() * 0.5 * _r * _r);
    }

    /// <summary>What the surface over a column is, by the slope and height rules (the drawing's), through the table.</summary>
    private GroundKind SurfaceKind(int face, int ci, int cj, ReadOnlySpan<GroundKind> gives)
    {
        ColDir(face, ci, cj, out var x, out var y, out var z);
        return gives[Math.Min((int)SurfaceMaterial(x, y, z, out _, out _), gives.Length - 1)];
    }

    private TerrainMaterial SurfaceMaterial(double x, double y, double z, out float cosSlope, out float h)
    {
        var dir = new Vector3((float)x, (float)y, (float)z);
        h = (float)(_p.SurfaceRadius(dir, out var n) - _r);
        cosSlope = Math.Clamp(Vector3.Dot(n, dir), -1f, 1f);
        var slope = MathF.Acos(cosSlope) * (180f / MathF.PI);
        var surf = _p.Surface;
        return slope >= surf.RockSlope ? TerrainMaterial.Rock
            : slope >= surf.DirtSlope ? TerrainMaterial.Dirt
            : surf.SandBelow is { } sb && h <= sb ? TerrainMaterial.Sand
            : TerrainMaterial.Grass;
    }

    /// <summary>Would a fill put new ground inside the world box min..max (along the columns whose middles cross it)?</summary>
    public bool FillTouches(in DigOp op, Vector3 min, Vector3 max)
    {
        Span<float> s = stackalloc float[(MaxSpans + 1) * 2];
        for (var f = 0; f < 6; f++)
        {
            if (!Footprint(op.At, op.Radius, f, out var ci0, out var ci1, out var cj0, out var cj1))
                continue;
            for (var cj = cj0; cj <= cj1; cj++)
            for (var ci = ci0; ci <= ci1; ci++)
            {
                if (!FillSpan(in op, f, ci, cj, s, out var lo, out var hi))
                    continue;
                ColDir(f, ci, cj, out var dx, out var dy, out var dz);
                // The box along this column's line (slabs), as altitudes.
                double s0 = double.NegativeInfinity, s1 = double.PositiveInfinity;
                if (!Slab(_p.Cx, dx, min.X, max.X, ref s0, ref s1) || !Slab(_p.Cy, dy, min.Y, max.Y, ref s0, ref s1) || !Slab(_p.Cz, dz, min.Z, max.Z, ref s0, ref s1))
                    continue;
                lo = MathF.Max(lo, (float)(s0 - _r));
                hi = MathF.Min(hi, (float)(s1 - _r));
                if (hi <= lo)
                    continue;
                var open = hi - lo;
                var have = ColumnNow(f, ci, cj, s);
                for (var k = 0; k < have.Length; k += 2)
                    open -= MathF.Max(MathF.Min(have[k + 1], hi) - MathF.Max(have[k], lo), 0);
                if (open > 0.001f)
                    return true;
            }
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

    /// <summary>Every dug cell back to the planetoid's own ground and the log emptied (a new match on this map).</summary>
    public void Clear()
    {
        foreach (var k in _touchedCells)
        {
            var face = k / (_n * _n);
            var rest = k - face * _n * _n;
            SlotRef(face, rest % _n, rest / _n) = 0;
        }
        _touchedCells.Clear();
        _chunkVersion.Clear();
        _chunks.Clear();
        _cols = 0;
        Log.Clear();
        MaxRise = 0;
        OpenToCavity = false;
        Version++;
        Generation++;
    }

    private void Bump(int face, int i0, int j0, int i1, int j1)
    {
        var a0 = Math.Max(i0, 0) / ChunkCells;
        var a1 = Math.Min(i1, _n - 1) / ChunkCells;
        var b0 = Math.Max(j0, 0) / ChunkCells;
        var b1 = Math.Min(j1, _n - 1) / ChunkCells;
        for (var b = b0; b <= b1; b++)
        for (var a = a0; a <= a1; a++)
        {
            var key = (face * ChunksPerSide + b) * ChunksPerSide + a;
            if (_chunkVersion.TryGetValue(key, out var v))
                _chunkVersion[key] = v + 1;
            else
            {
                _chunkVersion[key] = 1;
                _chunks.Add(key);
            }
        }
        Version++;
    }

    /// <summary>Turns cell (i, j) of a face into columns read off the triangles along their middles. Returns its first column.</summary>
    private int Touch(int face, int i, int j)
    {
        ref var slot = ref SlotRef(face, i, j);
        if (slot != 0)
            return slot - 1;
        var first = _cols;
        var n = Sub * Sub;
        if (_cols + n > _spans.Length)
        {
            var size = Math.Max(64, Math.Max(_spans.Length * 2, _cols + n));
            Array.Resize(ref _spans, size);
            Array.Resize(ref _count, size);
            Array.Resize(ref _orig, size);
            Array.Resize(ref _fillKind, size);
            Array.Resize(ref _fillLo, size);
        }
        for (var b = 0; b < Sub; b++)
        for (var a = 0; a < Sub; a++)
        {
            var c = first + b * Sub + a;
            var s = _spans[c] ??= new float[8];
            s[0] = float.NegativeInfinity;
            s[1] = _orig[c] = BaseTop(face, i * Sub + a, j * Sub + b);
            _count[c] = 1;
            _fillKind[c] = 0;
        }
        _cols += n;
        slot = first + 1;
        _touchedCells.Add((face * _n + j) * _n + i);
        return first;
    }

    private bool Subtract(int c, float lo, float hi)
    {
        var s = _spans[c];
        Span<float> o = stackalloc float[(MaxSpans + 1) * 2];
        var m = TerrainEdits.SubtractSpans(new ReadOnlySpan<float>(s, 0, _count[c] * 2), lo, hi, Bedrock, o);
        if (m < 0)
            return false;
        if (s.Length < m)
            _spans[c] = s = new float[Math.Max(m, s.Length * 2)];
        o[..m].CopyTo(s);
        _count[c] = m / 2;
        if (s[1] <= _inner)
            OpenToCavity = true; // (its lowest span stops at the cavity's roof: a way in)
        return true;
    }

    private bool Add(int c, float lo, float hi, DigKind kind)
    {
        var s = _spans[c];
        Span<float> o = stackalloc float[(MaxSpans + 1) * 2];
        var m = TerrainEdits.AddSpans(new ReadOnlySpan<float>(s, 0, _count[c] * 2), lo, hi, o, out var airFrom);
        if (m < 0)
            return false;
        if (s.Length < m)
            _spans[c] = s = new float[Math.Max(m, s.Length * 2)];
        o[..m].CopyTo(s);
        _count[c] = m / 2;
        _fillLo[c] = _fillKind[c] == 0 ? airFrom : MathF.Min(_fillLo[c], airFrom);
        _fillKind[c] = (byte)(kind - DigKind.FillDirt + 1);
        MaxRise = MathF.Max(MaxRise, s[m - 1] - _orig[c]);
        return true;
    }

    /// <summary>A digest of every dug column (cells in the order they were touched, then their spans bit for bit): equal on peers that applied the same ops.</summary>
    public ulong Digest()
    {
        var h = 14695981039346656037UL;
        foreach (var k in _touchedCells)
        {
            var face = k / (_n * _n);
            var rest = k - face * _n * _n;
            var first = SlotAt(face, rest % _n, rest / _n) - 1;
            h = (h ^ (uint)k) * 1099511628211UL;
            for (var c = first; c < first + Sub * Sub; c++)
            {
                h = (h ^ (uint)_count[c]) * 1099511628211UL;
                for (var q = 0; q < _count[c] * 2; q++)
                    h = (h ^ BitConverter.SingleToUInt32Bits(_spans[c][q])) * 1099511628211UL;
            }
        }
        return h;
    }

    /// <summary>Is a point (relative to the centre) under the planetoid's own triangles straight below it (by more than <paramref name="by"/>)?</summary>
    private bool UnderTriangles(Vector3 p, float by = 0)
    {
        var d = p.Length();
        return d > 1e-3f && d - _p.SurfaceRadius(p / d, out _) < -by && d - (float)_r > _inner + by; // (and over a hollow planetoid's cavity)
    }

    /// <summary>Is altitude <paramref name="a"/> solid in a column's spans (never in a hollow planetoid's cavity)?</summary>
    private bool SolidAt(ReadOnlySpan<float> s, float a) => a >= _inner && TerrainEdits.Solid(s, a);

    // ---- Ray hits -----------------------------------------------------------------------------------------------------

    /// <summary>
    /// Where a short segment <paramref name="p0"/>..<paramref name="p1"/> (relative to the centre) first goes into a dug
    /// column's span, as a fraction of it (-1: it doesn't). Pieces within one column are exact (a column is a convex cone,
    /// and the altitude along a short chord is near enough linear); a piece over a column line is halved down to 2 mm. The
    /// planetoid's triangles (untouched cells) are the ray march's own business.
    /// </summary>
    public float SegmentHit(Vector3 p0, Vector3 p1)
    {
        if (_cols == 0)
            return -1;
        var len = Vector3.Distance(p0, p1);
        return Piece(p0, p1, 0f, 1f, len, 0);
    }

    private float Piece(Vector3 p0, Vector3 p1, float u0, float u1, float len, int depth)
    {
        var a = Vector3.Lerp(p0, p1, u0);
        var b = Vector3.Lerp(p0, p1, u1);
        ColumnAt(a.X, a.Y, a.Z, out var fa, out var ia, out var ja);
        ColumnAt(b.X, b.Y, b.Z, out var fb, out var ib, out var jb);
        if (fa == fb && ia == ib && ja == jb)
        {
            var c = Col(fa, ia, ja);
            if (c < 0)
                return UnderTriangles(b) ? u1 : -1; // (untouched ground: the ray march settles onto its triangles)
            var s = new ReadOnlySpan<float>(_spans[c], 0, _count[c] * 2);
            float aa = (float)(a.Length() - _r), ab = (float)(b.Length() - _r);
            if (SolidAt(s, aa))
                return u0;
            var best = float.MaxValue;
            for (var k = 0; k < s.Length; k += 2)
            {
                // Going down it enters a span through its top, going up through its underside (a hollow planetoid's lowest
                // span has one at the cavity's roof; a column dug open to the cavity has nothing left over it).
                var bottom = MathF.Max(s[k], _inner);
                if (s[k + 1] <= bottom)
                    continue;
                var y = ab < aa ? s[k + 1] : bottom;
                if (!float.IsFinite(y) || (y - aa) * (y - ab) > 0 || aa == ab)
                    continue;
                best = MathF.Min(best, (y - aa) / (ab - aa));
            }
            return best <= 1 ? u0 + (u1 - u0) * Math.Clamp(best, 0f, 1f) : -1;
        }
        if ((u1 - u0) * len < 0.002f || depth > 20)
        {
            // Over a column line (within 2 mm): solid on the near side at the far end (a rim's very corner), or on the far side.
            var ab = (float)(b.Length() - _r);
            var ca = Col(fa, ia, ja);
            if (ca >= 0 ? SolidAt(new ReadOnlySpan<float>(_spans[ca], 0, _count[ca] * 2), ab + 0.001f) : UnderTriangles(b, 0.001f))
                return u0; // (more than a millimetre into it: a graze closer than that is no hit)
            var c = Col(fb, ib, jb);
            return (c >= 0 ? SolidAt(new ReadOnlySpan<float>(_spans[c], 0, _count[c] * 2), ab) : UnderTriangles(b)) ? u1 : -1;
        }
        var um = (u0 + u1) * 0.5f;
        var r = Piece(p0, p1, u0, um, len, depth + 1);
        return r >= 0 ? r : Piece(p0, p1, um, u1, len, depth + 1);
    }

    /// <summary>
    /// The normal at a ray hit in a dug column (world point <paramref name="p"/>): "up" on a span's top, down on its underside,
    /// else the wall plane (through the centre) of the nearer column line, facing back along the ray.
    /// </summary>
    public Vector3 HitNormal(Vector3 p, Vector3 dir)
    {
        double x = p.X - _p.Cx, y = p.Y - _p.Cy, z = p.Z - _p.Cz;
        var d = Math.Sqrt(x * x + y * y + z * z);
        if (d < 1e-3)
            return -dir;
        x /= d;
        y /= d;
        z /= d;
        var up = new Vector3((float)x, (float)y, (float)z);
        _p.Locate(x, y, z, out var f, out var fi, out var fj);
        double gi = fi * Sub, gj = fj * Sub;
        int ci = Math.Clamp((int)Math.Floor(gi), 0, Cols - 1), cj = Math.Clamp((int)Math.Floor(gj), 0, Cols - 1);
        var c = Col(f, ci, cj);
        if (c >= 0)
        {
            var a = (float)(d - _r);
            const float near = 0.04f;
            var s = new ReadOnlySpan<float>(_spans[c], 0, _count[c] * 2);
            for (var k = 0; k < s.Length; k += 2)
            {
                var bottom = MathF.Max(s[k], _inner);
                if (s[k + 1] <= bottom)
                    continue; // (dug open to a hollow planetoid's cavity)
                if (MathF.Abs(a - s[k + 1]) < near)
                    return up;
                if (float.IsFinite(bottom) && MathF.Abs(a - bottom) < near)
                    return -up;
            }
        }
        var ei = Math.Abs(gi - Math.Round(gi));
        var ej = Math.Abs(gj - Math.Round(gj));
        double ax, ay, az, bx, by, bz;
        if (ei < ej)
        {
            var li = Math.Round(gi) / Sub;
            _p.DirOf(f, li, (double)cj / Sub, out ax, out ay, out az);
            _p.DirOf(f, li, (cj + 1.0) / Sub, out bx, out by, out bz);
        }
        else
        {
            var lj = Math.Round(gj) / Sub;
            _p.DirOf(f, (double)ci / Sub, lj, out ax, out ay, out az);
            _p.DirOf(f, (ci + 1.0) / Sub, lj, out bx, out by, out bz);
        }
        var n = Vector3.Cross(new Vector3((float)ax, (float)ay, (float)az), new Vector3((float)bx, (float)by, (float)bz));
        var len = n.Length();
        if (len < 1e-12f)
            return -dir;
        n /= len;
        return Vector3.Dot(n, dir) > 0 ? -n : n;
    }

    // ---- Faces --------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The faces of dug cell (i, j) of <paramref name="face"/> (nothing when untouched), with positions relative to
    /// (<paramref name="ox"/>, <paramref name="oy"/>, <paramref name="oz"/>) (worked out in doubles, so a chunk's own origin keeps
    /// them precise): each span's top and underside, and the sides where a span meets air in the next column, or where it
    /// meets the next cell's untouched ground (up to that ground's triangles along the shared edge, so the seam is closed).
    /// Collision and drawing share these.
    /// </summary>
    public void CellFaces<T>(int face, int i, int j, double ox, double oy, double oz, ref T sink) where T : struct, IDigFaceSink
    {
        if (!Touched(face, i, j))
            return;
        var vs = Sub + 1;
        Span<double> corner = stackalloc double[vs * vs * 3];
        for (var b = 0; b < vs; b++)
        for (var a = 0; a < vs; a++)
        {
            var k = (b * vs + a) * 3;
            _p.DirOf(face, i + (double)a / Sub, j + (double)b / Sub, out corner[k], out corner[k + 1], out corner[k + 2]);
        }
        var ctx = new FaceCtx { Ox = _p.Cx - ox, Oy = _p.Cy - oy, Oz = _p.Cz - oz, R = _r };
        for (var b = 0; b < Sub; b++)
        for (var a = 0; a < Sub; a++)
        {
            int ci = i * Sub + a, cj = j * Sub + b;
            var c = Col(face, ci, cj);
            var s = new ReadOnlySpan<float>(_spans[c], 0, _count[c] * 2);
            var d00 = D(corner, (b * vs + a) * 3);
            var d10 = D(corner, (b * vs + a + 1) * 3);
            var d01 = D(corner, ((b + 1) * vs + a) * 3);
            var d11 = D(corner, ((b + 1) * vs + a + 1) * 3);
            ColDir(face, ci, cj, out var ux, out var uy, out var uz);
            var up = new Vector3((float)ux, (float)uy, (float)uz);
            for (var k = 0; k < s.Length; k += 2)
            {
                float lo = MathF.Max(s[k], _inner), hi = s[k + 1];
                if (hi <= lo)
                    continue; // (dug open to a hollow planetoid's cavity: nothing left here)
                var covered = k + 2 < s.Length;
                var (col, rock) = TopShade(c, ux, uy, uz, hi, covered);
                Emit(ref sink, ctx.P(d00, hi), ctx.P(d01, hi), ctx.P(d11, hi), ctx.P(d10, hi), up, col, rock);
                if (k > 0 || float.IsFinite(lo)) // (and a hollow planetoid's cavity roof under a dug cell: the cavity's own drawing leaves dug cells out)
                {
                    var (bc, br) = Shade(c, lo, true);
                    Emit(ref sink, ctx.P(d00, lo), ctx.P(d10, lo), ctx.P(d11, lo), ctx.P(d01, lo), -up, bc * 0.85f, br);
                }
            }
            var top = s[^1];
            Side(ref sink, ctx, s, c, top, face, ci - 1, cj, d00, d01, up);
            Side(ref sink, ctx, s, c, top, face, ci + 1, cj, d11, d10, up);
            Side(ref sink, ctx, s, c, top, face, ci, cj - 1, d10, d00, up);
            Side(ref sink, ctx, s, c, top, face, ci, cj + 1, d01, d11, up);
        }

        static Dir3 D(Span<double> v, int k) => new(v[k], v[k + 1], v[k + 2]);
    }

    /// <summary>A unit direction in doubles.</summary>
    private readonly record struct Dir3(double X, double Y, double Z)
    {
        public Dir3 Lerp(Dir3 o, double u)
        {
            double x = X + (o.X - X) * u, y = Y + (o.Y - Y) * u, z = Z + (o.Z - Z) * u;
            var l = Math.Sqrt(x * x + y * y + z * z);
            return new Dir3(x / l, y / l, z / l);
        }
    }

    /// <summary>Where positions are made: the centre relative to the output's origin, and the radius.</summary>
    private struct FaceCtx
    {
        public double Ox, Oy, Oz, R;

        public readonly Vector3 P(Dir3 d, float alt)
        {
            var r = R + alt;
            return new Vector3((float)(Ox + d.X * r), (float)(Oy + d.Y * r), (float)(Oz + d.Z * r));
        }
    }

    /// <summary>The sides of one column facing neighbour (ni, nj) along the edge p..q.</summary>
    private void Side<T>(ref T sink, in FaceCtx ctx, ReadOnlySpan<float> s, int c, float top, int face, int ni, int nj, Dir3 p, Dir3 q, Vector3 up)
        where T : struct, IDigFaceSink
    {
        // The wall's plane passes through the centre; its normal faces away from this column.
        var n = Vector3.Normalize(Vector3.Cross(new Vector3((float)p.X, (float)p.Y, (float)p.Z), new Vector3((float)q.X, (float)q.Y, (float)q.Z)));
        var mid = new Vector3((float)(p.X + q.X), (float)(p.Y + q.Y), (float)(p.Z + q.Z)) * 0.5f;
        if (Vector3.Dot(n, up - mid) > 0)
            n = -n;
        var nc = Neighbour(face, ni, nj);
        if (nc >= 0)
        {
            var ns = new ReadOnlySpan<float>(_spans[nc], 0, _count[nc] * 2);
            for (var k = 0; k < s.Length; k += 2)
            {
                var lo = MathF.Max(s[k], _inner);
                var hi = s[k + 1];
                if (hi <= lo)
                    continue;
                var covered = k + 2 < s.Length;
                var y = lo;
                for (var m = 0; m < ns.Length && y < hi; m += 2)
                {
                    if (ns[m + 1] <= y)
                        continue;
                    if (ns[m] >= hi)
                        break;
                    if (ns[m] > y)
                        Wall(ref sink, ctx, p, q, y, y, ns[m], ns[m], n, c, covered);
                    y = MathF.Max(y, ns[m + 1]);
                }
                if (y < hi)
                    Wall(ref sink, ctx, p, q, y, y, hi, hi, n, c, covered);
            }
            return;
        }
        // The next cell is untouched: solid up to its triangles along the shared edge.
        var sp = BaseAlt(p.X, p.Y, p.Z);
        var sq = BaseAlt(q.X, q.Y, q.Z);
        for (var k = 0; k < s.Length; k += 2)
        {
            float lo = MathF.Max(s[k], _inner), hi = MathF.Max(s[k + 1], _inner);
            var covered = k + 2 < s.Length;
            var g1 = covered ? s[k + 2] : float.PositiveInfinity;
            Sloped(ref sink, ctx, p, q, sp, sq, lo, hi, true, n, c, covered);
            Sloped(ref sink, ctx, p, q, sp, sq, hi, g1, false, -n, c, covered || MathF.Max(sp, sq) < top);
        }
    }

    /// <summary>The storage index of the column next to (ni, nj) on a face, which may be over the face's edge (-1: untouched).</summary>
    private int Neighbour(int face, int ni, int nj)
    {
        if ((uint)ni < (uint)Cols && (uint)nj < (uint)Cols)
            return Col(face, ni, nj);
        _p.DirOf(face, (ni + 0.5) / Sub, (nj + 0.5) / Sub, out var x, out var y, out var z);
        ColumnAt(x, y, z, out var f, out var ci, out var cj);
        return Col(f, ci, cj);
    }

    /// <summary>A wall against untouched ground whose surface runs sp..sq along the edge (as TerrainEdits.Sloped): above it, or under it.</summary>
    private void Sloped<T>(ref T sink, in FaceCtx ctx, Dir3 p, Dir3 q, float sp, float sq, float lo, float hi, bool above, Vector3 n, int c, bool covered)
        where T : struct, IDigFaceSink
    {
        Span<float> us = stackalloc float[4];
        var nu = 0;
        us[nu++] = 0;
        foreach (var y in (ReadOnlySpan<float>)[lo, hi])
        {
            if (!float.IsFinite(y) || (sp - y) * (sq - y) >= 0)
                continue;
            var u = (y - sp) / (sq - sp);
            if (u > 1e-4f && u < 1 - 1e-4f)
                us[nu++] = u;
        }
        us[nu++] = 1;
        us[1..(nu - 1)].Sort();
        for (var k = 0; k + 1 < nu; k++)
        {
            float u0 = us[k], u1 = us[k + 1];
            float s0 = sp + (sq - sp) * u0, s1 = sp + (sq - sp) * u1;
            var a = u0 == 0 ? p : p.Lerp(q, u0);
            var b = u1 == 1 ? q : p.Lerp(q, u1);
            if (above)
            {
                float l0 = MathF.Min(MathF.Max(lo, s0), hi), l1 = MathF.Min(MathF.Max(lo, s1), hi);
                if (hi - l0 > 0.001f || hi - l1 > 0.001f)
                    Wall(ref sink, ctx, a, b, l0, l1, hi, hi, n, c, covered);
            }
            else
            {
                float h0 = MathF.Max(MathF.Min(hi, s0), lo), h1 = MathF.Max(MathF.Min(hi, s1), lo);
                if (h0 - lo > 0.001f || h1 - lo > 0.001f)
                    Wall(ref sink, ctx, a, b, lo, lo, h0, h1, n, c, covered);
            }
        }
    }

    private void Wall<T>(ref T sink, in FaceCtx ctx, Dir3 p, Dir3 q, float loP, float loQ, float hiP, float hiQ, Vector3 n, int c, bool covered)
        where T : struct, IDigFaceSink
    {
        var (col, rock) = Shade(c, (MathF.Min(loP, loQ) + MathF.Max(hiP, hiQ)) * 0.5f, covered);
        Emit(ref sink, ctx.P(p, loP), ctx.P(q, loQ), ctx.P(q, hiQ), ctx.P(p, hiP), n, col * 0.92f, rock);
    }

    private static void Emit<T>(ref T sink, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n, Vector3 col, float rock) where T : struct, IDigFaceSink
    {
        if (Vector3.Dot(Vector3.Cross(c - a, d - b), n) < 0)
            (b, d) = (d, b);
        sink.Quad(a, b, c, d, n, col, rock);
    }

    /// <summary>How a column looks at an altitude on a dug face: a fill's own colour, else freshly dug ground by its depth.</summary>
    private (Vector3 Color, float Rock) Shade(int c, float y, bool covered)
    {
        var sf = _p.Surface;
        if (_fillKind[c] == 0 || y < _fillLo[c] - 0.01f)
        {
            var depth = _orig[c] - y;
            var f = Math.Clamp(depth / 4f, 0f, 1f);
            var dug = Vector3.Lerp(sf.Dirt * 0.85f, sf.Rock * 0.8f, f) * (covered ? 0.6f : 1f);
            return (dug, Math.Clamp(0.25f + depth / 3f, 0.25f, 1f));
        }
        var (col, rock) = (GroundKind)(_fillKind[c] - 1) switch
        {
            GroundKind.Stone => (sf.Rock, 1f),
            GroundKind.Sand => (sf.Sand, 0.2f),
            _ => (sf.Dirt, 0.25f),
        };
        return (col * (covered ? 0.6f : 1f), rock);
    }

    /// <summary><see cref="Shade"/> for a span's top: open ground as built keeps the surface's own colour (the drawing's rules).</summary>
    private (Vector3 Color, float Rock) TopShade(int c, double ux, double uy, double uz, float y, bool covered)
    {
        if (covered || MathF.Abs(y - _orig[c]) >= 0.01f || _fillKind[c] != 0)
            return Shade(c, y, covered);
        var rule = SurfaceMaterial(ux, uy, uz, out var cs, out var h);
        var local = new Vector3(MathF.Sqrt(MathF.Max(0, 1 - cs * cs)), cs, 0);
        double px = ux * _r, py = uy * _r, pz = uz * _r;
        return TerrainData.ColourOf(_p.Surface, local, h, (byte)rule, (float)(px + py * 0.5), (float)(pz - py * 0.5));
    }
}
