using System.Numerics;

namespace CallOfSlop;

/// <summary>One change to the ground: what it was and where (the server's log, sent to every client and to joiners).</summary>
public readonly record struct DigOp(DigKind Kind, Vector3 At, float Radius)
{
    /// <summary>Bytes of an op on the wire: kind, centre, radius in centimetres.</summary>
    public const int WireBytes = 1 + 12 + 2;

    /// <summary>The op as the wire carries it (radius rounded to a centimetre), so the server applies exactly what clients will.</summary>
    public DigOp Quantised => this with { Radius = MathF.Round(Math.Clamp(Radius, 0.05f, 600f) * 100f) / 100f };
}

/// <summary>Kinds of ground change. Wire bytes: add new kinds at the end.</summary>
public enum DigKind : byte
{
    /// <summary>A blast's crater: a flattened ball of ground (as deep as <see cref="TerrainEdits.CraterDepth"/> of its radius) taken out.</summary>
    Crater,

    /// <summary>A round scoop taken out (a shovel or a drill): a ball of ground, as tall as it is wide.</summary>
    Scoop,

    /// <summary>Ground put back, made of dirt (protocol 113; <see cref="TerrainEdits.FillSpan"/>): a ball of ground added, a small gap under it closed.</summary>
    FillDirt,

    /// <summary>The same, made of stone.</summary>
    FillStone,

    /// <summary>The same, made of sand.</summary>
    FillSand,
}

/// <summary>What ground is made of, as digging sees it (<see cref="DigKind.FillDirt"/> + this is the fill of it).</summary>
public enum GroundKind : byte
{
    Dirt,
    Stone,
    Sand,
    COUNT,
}

/// <summary>Receives the faces of dug ground (drawing and physics share one face list).</summary>
public interface IDigFaceSink
{
    /// <summary>A quad a-b-c-d, wound counter-clockwise seen from the side <paramref name="n"/> points to.</summary>
    void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n, Vector3 color, float rock);
}

/// <summary>
/// Dug ground (craters, scoops, tunnels): the terrain as layered material stacks where it has been changed (after
/// Peytavie et al., "Arches", 2009). Untouched ground stays the map's heightfield (with its holes and caves) and costs
/// nothing; the first change in a terrain cell turns that cell into <see cref="Sub"/> x <see cref="Sub"/> columns about
/// half a metre wide, each a short list of solid spans (bottom, top; the lowest reaches down for ever), read off the
/// heightfield at the column's middle. A dig takes an interval out of the columns it covers, so craters, overhangs,
/// tunnels and caves above caves are all the same thing: spans. Drawn as blocks (only the faces where solid meets air),
/// and every terrain query (<see cref="TerrainData"/>, so movement, rays, grenades, physics bodies) answers from the spans.
/// <para>Runtime state only: never baked, never in the map's fingerprint, cleared when a match restarts the map
/// (<see cref="Map.RestoreBuildings"/>). The server keeps the ops in <see cref="Log"/> and every peer applies the same ops
/// in the same order with the same maths (no trigonometry), so every peer ends with the same ground.</para>
/// </summary>
public sealed partial class TerrainEdits : IDigGround
{
    /// <summary>The width a column aims for (metres); a terrain cell splits into a whole number of columns.</summary>
    public const float TargetSize = 0.5f;

    /// <summary>Terrain cells per side of a drawing chunk (the terrain renderer's chunks).</summary>
    public const int ChunkCells = 32;

    /// <summary>Slivers of ground thinner than this (metres) are taken away with the dig.</summary>
    public const float MinSpan = 0.05f;

    /// <summary>Nothing digs deeper than this far below the map's lowest ground (bedrock).</summary>
    public const float BedrockDepth = 24f;

    /// <summary>A crater is this deep for its radius (a flattened ball).</summary>
    public const float CraterDepth = 0.55f;

    /// <summary>
    /// Most ops a match keeps (joiners replay them); past this the ground stops changing. 65,536 ops are about 980 KB to a
    /// joiner: the log stays smaller than a state of columns would be (a column is about 8 bytes, a dug cell up to 64 columns).
    /// </summary>
    public const int MaxOps = 65536;

    /// <summary>Most spans a column holds; a dig that would split a column past it leaves that column as it is.</summary>
    public const int MaxSpans = 16;

    private readonly TerrainData _t;

    /// <summary>Columns per terrain cell side.</summary>
    public readonly int Sub;

    /// <summary>Column width (metres).</summary>
    public readonly float Size;

    /// <summary>Columns across the whole terrain.</summary>
    public readonly int ColsX, ColsZ;

    /// <summary>Drawing chunks across the terrain (<see cref="ChunkCells"/> cells each).</summary>
    public readonly int ChunksX, ChunksZ;

    /// <summary>The lowest height anything can be dug to.</summary>
    public readonly float Bedrock;

    /// <summary>Bumped when a chunk's dug ground changes (the renderer rebuilds what it has seen change).</summary>
    public readonly int[] ChunkVersion;

    /// <summary>Bumped on every change anywhere (one compare per frame for the renderer).</summary>
    public int Version { get; private set; }

    /// <summary>Every op applied since the map was last restored, in order.</summary>
    public readonly List<DigOp> Log = new();

    List<DigOp> IDigGround.Ops => Log;

    /// <summary>How far a point is above the surface as built straight under it (a dug column's top as built, else the heightfield's).</summary>
    public float AboveBuilt(Vector3 p) => p.Y - (TryBuiltTop(p.X, p.Z, out var top) ? top : _t.BaseGroundTop(p.X, p.Z));

    private readonly int _cellsX, _cellsZ;
    // Per terrain cell: 0 = untouched, else 1 + the index of its first column (Sub x Sub in a row). Paged in blocks of
    // SlotPage x SlotPage cells made on first dig, so a 25 km streamed map doesn't hold a slot for every cell.
    private const int SlotShift = 6, SlotPage = 1 << SlotShift, SlotMask = SlotPage - 1;
    private int[]?[]? _slot;
    private readonly int _slotPagesX;
    private float[][] _spans = [];
    private int[] _count = [];
    private float[] _orig = [];
    private byte[] _fillKind = []; // per column: 0 none, else 1 + the GroundKind of the last fill here
    private float[] _fillLo = []; // per column: the ground from here up reads as that fill (one fill per column, the last one: FillLo's note)
    private int _cols;
    private readonly List<int> _touchedCells = new();
    private readonly TerrainIsland[] _islands;

    public TerrainEdits(TerrainData t)
    {
        _t = t;
        Sub = Math.Max(1, (int)MathF.Round(t.Cell / TargetSize));
        Size = t.Cell / Sub;
        _cellsX = t.CountX - 1;
        _cellsZ = t.CountZ - 1;
        ColsX = _cellsX * Sub;
        ColsZ = _cellsZ * Sub;
        ChunksX = (_cellsX + ChunkCells - 1) / ChunkCells;
        ChunksZ = (_cellsZ + ChunkCells - 1) / ChunkCells;
        ChunkVersion = new int[ChunksX * ChunksZ];
        _slotPagesX = (_cellsX + SlotMask) >> SlotShift;
        Bedrock = MathF.Min(t.MinHeight, t.Voids?.MinFloor ?? t.MinHeight) - BedrockDepth;
        Highest = t.MaxHeight;
        _islands = TerrainIsland.Of(t);
        foreach (var island in _islands)
            Bedrock = MathF.Min(Bedrock, (float)island.LowestFloor(t) - BedrockDepth);
        BuildIslands();
    }

    /// <summary>The map's floating islands (part of its own ground: rebuilt by <see cref="Clear"/>, never in <see cref="Log"/>).</summary>
    public IReadOnlyList<TerrainIsland> Islands => _islands;

    /// <summary>
    /// Lifts every floating island out of the ground: each column inside one keeps the ground below its hole and gets a
    /// second span, the island, the same shape as the hole and <see cref="TerrainIsland.Top"/> high. Same maths on every peer.
    /// </summary>
    /// <summary>A floating island's underside and its hole's floor go in steps this tall.</summary>
    public const float IslandStep = 0.5f;

    private void BuildIslands()
    {
        foreach (var island in _islands)
        {
            var reach = (float)island.Reach;
            float cx = (float)island.X, cz = (float)island.Z;
            var ci0 = Math.Max((int)MathF.Floor((cx - reach - _t.OriginX) / Size), 0);
            var ci1 = Math.Min((int)MathF.Floor((cx + reach - _t.OriginX) / Size), ColsX - 1);
            var cj0 = Math.Max((int)MathF.Floor((cz - reach - _t.OriginZ) / Size), 0);
            var cj1 = Math.Min((int)MathF.Floor((cz + reach - _t.OriginZ) / Size), ColsZ - 1);
            for (var cj = cj0; cj <= cj1; cj++)
            for (var ci = ci0; ci <= ci1; ci++)
            {
                double x = _t.OriginX + (ci + 0.5) * Size, z = _t.OriginZ + (cj + 0.5) * Size;
                var thick = island.Thickness(x, z);
                if (thick < MinSpan * 4)
                    continue;
                var col = Col(ci, cj);
                if (col < 0)
                    col = Touch(ci / Sub, cj / Sub) + cj % Sub * Sub + ci % Sub;
                var s = _spans[col];
                var ground = s[_count[col] * 2 - 1];
                // Heights in whole steps, so neighbouring columns mostly line up and only the steps between them get
                // sides (a smooth underside would wall every column against every neighbour: about 4x the faces).
                var thickQ = MathF.Max(MathF.Round((float)thick / IslandStep) * IslandStep, (float)TerrainIsland.RimThickness);
                var top = (float)island.TopAt(x, z);
                var floor = MathF.Max(MathF.Floor((ground - thickQ) / IslandStep) * IslandStep, Bedrock);
                var bottom = top - thickQ;
                if (bottom <= ground + MinSpan)
                    continue; // (the island would sit in the ground here: leave the column as it is)
                if (s.Length < 4)
                    _spans[col] = s = new float[8];
                s[0] = float.NegativeInfinity;
                s[1] = floor;
                s[2] = bottom;
                s[3] = top;
                _count[col] = 2;
                _orig[col] = top; // (its top is the island's surface: grass, not dug dirt)
                Highest = MathF.Max(Highest, top);
            }
            Bump(ci0 / Sub - 1, cj0 / Sub - 1, ci1 / Sub + 1, cj1 / Sub + 1);
        }
    }

    /// <summary>The highest solid ground can be right now: the map's highest sample, or the top of a fill or an island over it.</summary>
    public float Highest { get; private set; }

    /// <summary>Has any ground been dug?</summary>
    public bool Any => _cols > 0;

    /// <summary>Terrain cells turned into columns so far.</summary>
    public int TouchedCells => _touchedCells.Count;

    /// <summary>Has terrain cell (i, j) been dug (so its columns, not the heightfield, are the ground there)?</summary>
    public bool Touched(int i, int j) => _slot != null && (uint)i < (uint)_cellsX && (uint)j < (uint)_cellsZ && SlotAt(i, j) != 0;

    private int SlotAt(int i, int j) => _slot![(j >> SlotShift) * _slotPagesX + (i >> SlotShift)] is { } p ? p[((j & SlotMask) << SlotShift) | (i & SlotMask)] : 0;

    private ref int SlotRef(int i, int j)
    {
        _slot ??= new int[]?[_slotPagesX * ((_cellsZ + SlotMask) >> SlotShift)];
        var p = _slot[(j >> SlotShift) * _slotPagesX + (i >> SlotShift)] ??= new int[SlotPage * SlotPage];
        return ref p[((j & SlotMask) << SlotShift) | (i & SlotMask)];
    }

    /// <summary>Column (ci, cj) of the whole terrain (clamped to it).</summary>
    public void ColumnOf(float x, float z, out int ci, out int cj)
    {
        ci = Math.Clamp((int)MathF.Floor((x - _t.OriginX) / Size), 0, ColsX - 1);
        cj = Math.Clamp((int)MathF.Floor((z - _t.OriginZ) / Size), 0, ColsZ - 1);
    }

    /// <summary>The solid spans (bottom, top pairs, lowest first) of the dug column at (x, z); false where the ground is untouched.</summary>
    public bool TryColumn(float x, float z, out ReadOnlySpan<float> spans)
    {
        spans = default;
        if (_slot == null)
            return false;
        ColumnOf(x, z, out var ci, out var cj);
        var c = Col(ci, cj);
        if (c < 0)
            return false;
        spans = new ReadOnlySpan<float>(_spans[c], 0, _count[c] * 2);
        return true;
    }

    /// <summary>The top the dug column at (x, z) had as built (a floating island's surface included); false where the ground is untouched.</summary>
    public bool TryBuiltTop(float x, float z, out float top)
    {
        top = 0;
        ColumnOf(x, z, out var ci, out var cj);
        var c = Col(ci, cj);
        if (c < 0)
            return false;
        top = _orig[c];
        return true;
    }

    /// <summary>The column index of (ci, cj) in this object's storage, or -1 when its cell is untouched.</summary>
    private int Col(int ci, int cj)
    {
        if (_slot == null || (uint)ci >= (uint)ColsX || (uint)cj >= (uint)ColsZ)
            return -1;
        var s = SlotAt(ci / Sub, cj / Sub);
        return s == 0 ? -1 : s - 1 + cj % Sub * Sub + ci % Sub;
    }

    // ---- Queries on a column's spans ---------------------------------------------------------------------------

    /// <summary>The top of the highest span.</summary>
    public static float Top(ReadOnlySpan<float> s) => s[^1];

    /// <summary>The ground under a point at height <paramref name="y"/>: the top of the span it is in, else of the highest span below it.</summary>
    public static float GroundBelow(ReadOnlySpan<float> s, float y)
    {
        for (var k = s.Length - 2; k > 0; k -= 2)
            if (y >= s[k])
                return s[k + 1];
        return s[1];
    }

    /// <summary>The underside of the lowest span above a point in the open, else +infinity (in solid ground too).</summary>
    public static float CeilingAbove(ReadOnlySpan<float> s, float y)
    {
        for (var k = 0; k < s.Length; k += 2)
        {
            if (y > s[k + 1])
                continue;
            return y >= s[k] ? float.PositiveInfinity : s[k];
        }
        return float.PositiveInfinity;
    }

    /// <summary>Is height <paramref name="y"/> inside one of the spans?</summary>
    public static bool Solid(ReadOnlySpan<float> s, float y)
    {
        for (var k = 0; k < s.Length; k += 2)
            if (y <= s[k + 1])
                return y >= s[k];
        return false;
    }

    /// <summary>
    /// Normal at a ray hit in a dug column: up on a span's top, down on its underside, else a wall facing back along the
    /// ray across the nearer column line.
    /// </summary>
    public Vector3 HitNormal(ReadOnlySpan<float> s, Vector3 p, Vector3 dir)
    {
        const float near = 0.04f;
        for (var k = 0; k < s.Length; k += 2)
        {
            if (MathF.Abs(p.Y - s[k + 1]) < near)
                return Vector3.UnitY;
            if (k > 0 && MathF.Abs(p.Y - s[k]) < near)
                return -Vector3.UnitY;
        }
        var fx = (p.X - _t.OriginX) / Size;
        var fz = (p.Z - _t.OriginZ) / Size;
        var ex = MathF.Abs(fx - MathF.Round(fx));
        var ez = MathF.Abs(fz - MathF.Round(fz));
        if (ex < ez && MathF.Abs(dir.X) > 1e-4f)
            return new Vector3(-MathF.Sign(dir.X), 0, 0);
        if (MathF.Abs(dir.Z) > 1e-4f)
            return new Vector3(0, 0, -MathF.Sign(dir.Z));
        return dir.Y > 0 ? -Vector3.UnitY : Vector3.UnitY;
    }

    // ---- Changes ------------------------------------------------------------------------------------------------

    /// <summary>
    /// Applies one op (logged; quantise it first when it will be sent). Returns the columns it changed (0: nothing to dig,
    /// nothing to fill, out of the map, or the log is full).
    /// </summary>
    public int Apply(DigOp op)
    {
        if (Log.Count >= MaxOps || !(op.Radius > 0) || !float.IsFinite(op.At.X + op.At.Y + op.At.Z))
            return 0;
        var r = op.Radius;
        var vs = op.Kind == DigKind.Crater ? CraterDepth : 1f;
        var r2 = r * r;
        var c = op.At;
        Footprint(c, r, out var ci0, out var ci1, out var cj0, out var cj1);
        var changed = 0;
        for (var cj = cj0; cj <= cj1; cj++)
        for (var ci = ci0; ci <= ci1; ci++)
        {
            if (op.Kind >= DigKind.FillDirt)
            {
                if (!FillSpan(in op, ci, cj, out var fillLo, out var fillHi))
                    continue;
                var filled = Col(ci, cj);
                if (filled < 0)
                    filled = Touch(ci / Sub, cj / Sub) + cj % Sub * Sub + ci % Sub;
                if (Add(filled, fillLo, fillHi, op.Kind))
                    changed++;
                continue;
            }
            var dx = _t.OriginX + (ci + 0.5f) * Size - c.X;
            var dz = _t.OriginZ + (cj + 0.5f) * Size - c.Z;
            var d2 = dx * dx + dz * dz;
            if (d2 >= r2)
                continue;
            var h = MathF.Sqrt(r2 - d2) * vs;
            var lo = MathF.Max(c.Y - h, Bedrock);
            var hi = c.Y + h;
            if (hi - lo < MinSpan)
                continue; // the rim of the ball: too thin to dig (no air slivers)
            // Only touch a cell the dig really reaches (its column has solid ground in the interval).
            var col = Col(ci, cj);
            if (col < 0)
            {
                var top = _t.BaseGroundTop(_t.OriginX + (ci + 0.5f) * Size, _t.OriginZ + (cj + 0.5f) * Size);
                if (lo >= top)
                    continue;
                col = Touch(ci / Sub, cj / Sub) + cj % Sub * Sub + ci % Sub;
            }
            if (Subtract(col, lo, hi))
                changed++;
        }
        if (changed > 0)
            Log.Add(op); // (only ops that changed something: what clients and joiners replay)
        if (changed > 0)
            Bump(ci0 / Sub - 1, cj0 / Sub - 1, ci1 / Sub + 1, cj1 / Sub + 1);
        return changed;
    }

    /// <summary>The columns a ball of radius <paramref name="r"/> at <paramref name="c"/> can reach (clamped to the terrain).</summary>
    private void Footprint(Vector3 c, float r, out int ci0, out int ci1, out int cj0, out int cj1)
    {
        ci0 = Math.Max((int)MathF.Floor((c.X - r - _t.OriginX) / Size), 0);
        ci1 = Math.Min((int)MathF.Floor((c.X + r - _t.OriginX) / Size), ColsX - 1);
        cj0 = Math.Max((int)MathF.Floor((c.Z - r - _t.OriginZ) / Size), 0);
        cj1 = Math.Min((int)MathF.Floor((c.Z + r - _t.OriginZ) / Size), ColsZ - 1);
    }

    /// <summary>
    /// The interval a fill adds to column (ci, cj): the ball of <paramref name="op"/> over that column, reaching down to the
    /// ground under it when the gap is no taller than the ball's radius (on a slope a ball's downhill side would otherwise
    /// float over an air gap). False when the ball misses the column or is only a sliver there.
    /// </summary>
    public bool FillSpan(in DigOp op, int ci, int cj, out float lo, out float hi)
    {
        lo = hi = 0;
        var c = op.At;
        var dx = _t.OriginX + (ci + 0.5f) * Size - c.X;
        var dz = _t.OriginZ + (cj + 0.5f) * Size - c.Z;
        var r2 = op.Radius * op.Radius;
        var d2 = dx * dx + dz * dz;
        if (d2 >= r2)
            return false;
        var h = MathF.Sqrt(r2 - d2);
        lo = MathF.Max(c.Y - h, Bedrock);
        hi = c.Y + h;
        if (hi - lo < MinSpan)
            return false;
        Span<float> s = stackalloc float[(MaxSpans + 1) * 2];
        var below = TerrainEdits.GroundBelow(ColumnNow(ci, cj, s), lo);
        if (below < lo && lo - below <= op.Radius)
            lo = below;
        return true;
    }

    /// <summary>The solid spans of column (ci, cj) now, dug or not, without touching it (<paramref name="into"/> holds them for an untouched column).</summary>
    private ReadOnlySpan<float> ColumnNow(int ci, int cj, Span<float> into)
    {
        var c = Col(ci, cj);
        if (c >= 0)
            return new ReadOnlySpan<float>(_spans[c], 0, _count[c] * 2);
        var n = BaseSpans(_t.OriginX + (ci + 0.5f) * Size, _t.OriginZ + (cj + 0.5f) * Size, into, out _);
        return into[..(n * 2)];
    }

    /// <summary>The spans of the map's own ground at (x, z): one, or two over a roofed cave. Returns how many; <paramref name="orig"/> is its top as built.</summary>
    private int BaseSpans(float x, float z, Span<float> s, out float orig)
    {
        var kind = _t.Column(x, z, out var floor, out var ceiling, out var top, out var hollow);
        s[0] = float.NegativeInfinity;
        if (kind == TerrainVoids.Open)
        {
            s[1] = orig = floor;
            return 1;
        }
        if (kind == TerrainVoids.Cave && hollow)
        {
            s[1] = floor;
            s[2] = ceiling;
            s[3] = orig = top;
            return 2;
        }
        s[1] = orig = top;
        return 1;
    }

    /// <summary>
    /// What <paramref name="op"/> would change, without changing it: the cubic metres it would take out, by what they are made
    /// of (a dig), or put in (a fill, all under its own kind). Returns the total. A dig reads each column: what a fill laid
    /// there is that fill; the rest is the surface's material (<paramref name="surfaceGives"/>, by <see cref="TerrainMaterial"/>)
    /// down to <paramref name="stoneDepth"/> under the surface as built, then stone. Allocates nothing.
    /// </summary>
    public float Measure(in DigOp op, ReadOnlySpan<GroundKind> surfaceGives, float stoneDepth, Span<float> byKind)
    {
        byKind.Clear();
        if (!(op.Radius > 0) || !float.IsFinite(op.At.X + op.At.Y + op.At.Z))
            return 0;
        var vs = op.Kind == DigKind.Crater ? CraterDepth : 1f;
        var r2 = op.Radius * op.Radius;
        var cell = Size * Size;
        var c = op.At;
        Footprint(c, op.Radius, out var ci0, out var ci1, out var cj0, out var cj1);
        Span<float> s = stackalloc float[(MaxSpans + 1) * 2];
        var total = 0f;
        for (var cj = cj0; cj <= cj1; cj++)
        for (var ci = ci0; ci <= ci1; ci++)
        {
            float lo, hi;
            if (op.Kind >= DigKind.FillDirt)
            {
                if (!FillSpan(in op, ci, cj, out lo, out hi))
                    continue;
                var open = hi - lo;
                var have = ColumnNow(ci, cj, s);
                for (var k = 0; k < have.Length; k += 2)
                    open -= MathF.Max(MathF.Min(have[k + 1], hi) - MathF.Max(have[k], lo), 0);
                if (open > 0)
                {
                    byKind[op.Kind - DigKind.FillDirt] += open * cell;
                    total += open * cell;
                }
                continue;
            }
            var dx = _t.OriginX + (ci + 0.5f) * Size - c.X;
            var dz = _t.OriginZ + (cj + 0.5f) * Size - c.Z;
            var d2 = dx * dx + dz * dz;
            if (d2 >= r2)
                continue;
            var h = MathF.Sqrt(r2 - d2) * vs;
            lo = MathF.Max(c.Y - h, Bedrock);
            hi = c.Y + h;
            if (hi - lo < MinSpan)
                continue;
            var col = Col(ci, cj);
            var spans = ColumnNow(ci, cj, s);
            var orig = col >= 0 ? _orig[col] : _t.BaseGroundTop(_t.OriginX + (ci + 0.5f) * Size, _t.OriginZ + (cj + 0.5f) * Size);
            var surface = (int)SurfaceKindAt(ci, cj, col, surfaceGives);
            var stoneBelow = orig - stoneDepth;
            var fillFrom = col >= 0 && _fillKind[col] != 0 ? _fillLo[col] : float.PositiveInfinity;
            var fillKind = col >= 0 && _fillKind[col] != 0 ? _fillKind[col] - 1 : 0;
            for (var k = 0; k < spans.Length; k += 2)
            {
                var a = MathF.Max(spans[k], lo);
                var b = MathF.Min(spans[k + 1], hi);
                if (b <= a)
                    continue;
                total += (b - a) * cell;
                var inFill = MathF.Max(b - MathF.Max(a, fillFrom), 0); // (the part that was laid down, the rest is the map's own ground)
                byKind[fillKind] += inFill * cell;
                b -= inFill;
                var inStone = MathF.Max(MathF.Min(b, stoneBelow) - a, 0);
                byKind[(int)GroundKind.Stone] += inStone * cell;
                byKind[surface] += MathF.Max(b - a - inStone, 0) * cell;
            }
        }
        return total;
    }

    /// <summary>What the surface over column (ci, cj) gives: its nearest terrain sample's material through the table (a floating island's is grass).</summary>
    private GroundKind SurfaceKindAt(int ci, int cj, int col, ReadOnlySpan<GroundKind> gives)
    {
        if (col >= 0 && _islands.Length > 0 && IslandColumn(col, ci, cj))
            return gives[(int)TerrainMaterial.Grass];
        var i = Math.Clamp((int)MathF.Round((ci + 0.5f) / Sub), 0, _t.CountX - 1);
        var j = Math.Clamp((int)MathF.Round((cj + 0.5f) / Sub), 0, _t.CountZ - 1);
        return gives[Math.Min((int)_t.Material(i, j), gives.Length - 1)];
    }

    /// <summary>
    /// Would <paramref name="op"/> (a fill) put ground inside the box <paramref name="min"/>..<paramref name="max"/>? Column by
    /// column, the gap it closes included, so a body beside a heap is left alone. Only new ground counts: a box already in the
    /// ground isn't "buried" by a fill that adds nothing to it.
    /// </summary>
    public bool FillTouches(in DigOp op, Vector3 min, Vector3 max)
    {
        ColumnOf(min.X, min.Z, out var bi0, out var bj0);
        ColumnOf(max.X, max.Z, out var bi1, out var bj1);
        Footprint(op.At, op.Radius, out var ci0, out var ci1, out var cj0, out var cj1);
        ci0 = Math.Max(ci0, bi0);
        ci1 = Math.Min(ci1, bi1);
        cj0 = Math.Max(cj0, bj0);
        cj1 = Math.Min(cj1, bj1);
        Span<float> s = stackalloc float[(MaxSpans + 1) * 2];
        for (var cj = cj0; cj <= cj1; cj++)
        for (var ci = ci0; ci <= ci1; ci++)
        {
            if (!FillSpan(in op, ci, cj, out var lo, out var hi))
                continue;
            lo = MathF.Max(lo, min.Y);
            hi = MathF.Min(hi, max.Y);
            if (hi <= lo)
                continue;
            var open = hi - lo;
            var have = ColumnNow(ci, cj, s);
            for (var k = 0; k < have.Length; k += 2)
                open -= MathF.Max(MathF.Min(have[k + 1], hi) - MathF.Max(have[k], lo), 0);
            if (open > 0.001f)
                return true;
        }
        return false;
    }

    /// <summary>Every dug cell back to the map's own ground and the log emptied (a new match on this map).</summary>
    public void Clear()
    {
        foreach (var k in _touchedCells)
        {
            int i = k % _cellsX, j = k / _cellsX;
            SlotRef(i, j) = 0;
            ChunkVersion[j / ChunkCells * ChunksX + i / ChunkCells]++;
        }
        _touchedCells.Clear();
        _cols = 0;
        Log.Clear();
        Highest = _t.MaxHeight;
        Version++;
        BuildIslands(); // (the map's own floating islands come back with its ground)
    }

    private void Bump(int i0, int j0, int i1, int j1)
    {
        var a0 = Math.Max(i0, 0) / ChunkCells;
        var a1 = Math.Min(i1, _cellsX - 1) / ChunkCells;
        var b0 = Math.Max(j0, 0) / ChunkCells;
        var b1 = Math.Min(j1, _cellsZ - 1) / ChunkCells;
        for (var b = b0; b <= b1; b++)
        for (var a = a0; a <= a1; a++)
            ChunkVersion[b * ChunksX + a]++;
        Version++;
    }

    /// <summary>Turns terrain cell (i, j) into columns read off the heightfield (and its holes and caves) at their middles. Returns its first column.</summary>
    private int Touch(int i, int j)
    {
        var k = j * _cellsX + i;
        ref var slot = ref SlotRef(i, j);
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
            var x = _t.OriginX + (i * Sub + a + 0.5f) * Size;
            var z = _t.OriginZ + (j * Sub + b + 0.5f) * Size;
            _count[c] = BaseSpans(x, z, s, out _orig[c]);
            _fillKind[c] = 0;
        }
        _cols += n;
        slot = first + 1;
        _touchedCells.Add(k);
        return first;
    }

    /// <summary>Takes [lo, hi] out of column <paramref name="c"/>'s spans. False when nothing solid was there.</summary>
    private bool Subtract(int c, float lo, float hi)
    {
        var s = _spans[c];
        Span<float> o = stackalloc float[(MaxSpans + 1) * 2];
        var m = SubtractSpans(new ReadOnlySpan<float>(s, 0, _count[c] * 2), lo, hi, Bedrock, o);
        if (m < 0)
            return false;
        if (s.Length < m)
            _spans[c] = s = new float[Math.Max(m, s.Length * 2)];
        o[..m].CopyTo(s);
        _count[c] = m / 2;
        return true;
    }

    /// <summary>
    /// A column's spans <paramref name="s"/> with [lo, hi] taken out, into <paramref name="o"/> (room for MaxSpans + 1
    /// spans). Returns the floats written, or -1 when nothing solid was there or the column would need more than
    /// <see cref="MaxSpans"/> spans. Shared by flat ground and planetoids (PlanetEdits.cs).
    /// </summary>
    public static int SubtractSpans(ReadOnlySpan<float> s, float lo, float hi, float bedrock, Span<float> o)
    {
        var n = s.Length / 2;
        var m = 0;
        var any = false;
        for (var k = 0; k < n; k++)
        {
            float b = s[2 * k], t = s[2 * k + 1];
            if (t <= lo || b >= hi)
            {
                o[m++] = b;
                o[m++] = t;
                continue;
            }
            any = true;
            if (b < lo && (lo - b >= MinSpan || float.IsNegativeInfinity(b)))
            {
                o[m++] = b;
                o[m++] = lo;
            }
            if (t > hi && t - hi >= MinSpan)
            {
                o[m++] = hi;
                o[m++] = t;
            }
        }
        if (!any || m / 2 > MaxSpans)
            return -1;
        if (m == 0)
        {
            // Never dig a column away entirely (the bottom span reaches down for ever, so only a bedrock clamp gets here).
            o[m++] = float.NegativeInfinity;
            o[m++] = bedrock;
        }
        return m;
    }

    /// <summary>
    /// Adds [lo, hi] to column <paramref name="c"/>'s spans, joining what it meets (a gap under <see cref="MinSpan"/> closes
    /// too), and remembers the fill. False when it was all solid already, or the column has no room for another span.
    /// <para>A column remembers one fill, the last one, from its lowest point up: a fill of another kind over it changes
    /// how the fill below reads and is drawn, so a stack of stone with dirt on top digs as dirt. Spans per kind would
    /// be exact and cost a list per column; the simple rule has no way to make ground out of nothing.</para>
    /// </summary>
    private bool Add(int c, float lo, float hi, DigKind kind)
    {
        var s = _spans[c];
        Span<float> o = stackalloc float[(MaxSpans + 1) * 2];
        var m = AddSpans(new ReadOnlySpan<float>(s, 0, _count[c] * 2), lo, hi, o, out var airFrom);
        if (m < 0)
            return false;
        if (s.Length < m)
            _spans[c] = s = new float[Math.Max(m, s.Length * 2)];
        o[..m].CopyTo(s);
        _count[c] = m / 2;
        _fillLo[c] = _fillKind[c] == 0 ? airFrom : MathF.Min(_fillLo[c], airFrom);
        _fillKind[c] = (byte)(kind - DigKind.FillDirt + 1);
        Highest = MathF.Max(Highest, s[m - 1]);
        return true;
    }

    /// <summary>
    /// A column's spans <paramref name="s"/> with [lo, hi] added (joining what it meets; a gap under <see cref="MinSpan"/>
    /// closes too), into <paramref name="o"/>. Returns the floats written, or -1 when it was all solid already or the column
    /// has no room for another span; <paramref name="airFrom"/> is where the new ground starts. Shared by flat ground and
    /// planetoids (PlanetEdits.cs).
    /// </summary>
    public static int AddSpans(ReadOnlySpan<float> s, float lo, float hi, Span<float> o, out float airFrom)
    {
        var n = s.Length / 2;
        var m = 0;
        var placed = false;
        float wantLo = lo, wantHi = hi;
        airFrom = lo; // (where the new ground starts: the part of the ball inside old ground stays what it was)
        for (var k = 0; k < n; k++)
        {
            float b = s[2 * k], t = s[2 * k + 1];
            if (b <= wantLo && t >= wantHi)
                return -1; // (it's solid already)
            if (b <= airFrom && t >= airFrom)
                airFrom = t;
            if (t < lo - MinSpan)
            {
                o[m++] = b;
                o[m++] = t;
                continue;
            }
            if (b > hi + MinSpan)
            {
                if (!placed)
                {
                    o[m++] = lo;
                    o[m++] = hi;
                    placed = true;
                }
                o[m++] = b;
                o[m++] = t;
                continue;
            }
            lo = MathF.Min(lo, b);
            hi = MathF.Max(hi, t);
        }
        if (!placed)
        {
            o[m++] = lo;
            o[m++] = hi;
        }
        return m / 2 > MaxSpans ? -1 : m;
    }

    // ---- Faces -------------------------------------------------------------------------------------------------

    /// <summary>
    /// The faces of dug terrain cell (i, j) (nothing when untouched): each span's top and underside, and the sides where a
    /// span meets air in the next column (or air meets the next column's ground, when that one is untouched heightfield,
    /// up to its sloping surface so the seam is closed). Untouched ground draws itself.
    /// <para><paramref name="smooth"/> (drawing only): rims are drawn with cut and filled corners (TerrainEditsSmooth.cs),
    /// so a hole's outline reads as a smooth curve, not a staircase of columns. Collision stays the columns.</para>
    /// </summary>
    public void CellFaces<T>(int i, int j, ref T sink, bool smooth = false) where T : struct, IDigFaceSink
    {
        if (!Touched(i, j))
            return;
        var sf = _t.Surface;
        var vs = Sub + 1;
        Span<bool> rim = stackalloc bool[vs * vs];
        var anyRim = smooth && RimVertices(i, j, rim);
        for (var b = 0; b < Sub; b++)
        for (var a = 0; a < Sub; a++)
        {
            int ci = i * Sub + a, cj = j * Sub + b;
            var c = Col(ci, cj);
            if (anyRim && (rim[b * vs + a] || rim[b * vs + a + 1] || rim[(b + 1) * vs + a] || rim[(b + 1) * vs + a + 1]))
            {
                RimColumn(ref sink, ci, cj, c, rim.Slice(b * vs + a), vs);
                continue;
            }
            var s = new ReadOnlySpan<float>(_spans[c], 0, _count[c] * 2);
            float x0 = _t.OriginX + ci * Size, x1 = x0 + Size, z0 = _t.OriginZ + cj * Size, z1 = z0 + Size;
            var top = s[^1];
            var island = _islands.Length > 0 && IslandColumn(c, ci, cj); // (a floating island's column: TerrainIslands.cs)
            for (var k = 0; k < s.Length; k += 2)
            {
                float lo = s[k], hi = s[k + 1];
                var covered = k + 2 < s.Length;
                var (col, rock) = TopShade(c, ci, cj, hi, covered);
                Emit(ref sink, new(x0, hi, z0), new(x0, hi, z1), new(x1, hi, z1), new(x1, hi, z0), Vector3.UnitY, col, rock);
                if (k > 0)
                {
                    var (bc, br) = Shade(c, lo, !island); // (an island's underside sees the sky below: not darkened as cover)
                    Emit(ref sink, new(x0, lo, z0), new(x1, lo, z0), new(x1, lo, z1), new(x0, lo, z1), -Vector3.UnitY, bc * 0.85f, br);
                }
            }
            Side(ref sink, s, c, top, ci - 1, cj, new(x0, z0), new(x0, z1), -Vector3.UnitX);
            Side(ref sink, s, c, top, ci + 1, cj, new(x1, z1), new(x1, z0), Vector3.UnitX);
            Side(ref sink, s, c, top, ci, cj - 1, new(x1, z0), new(x0, z0), -Vector3.UnitZ);
            Side(ref sink, s, c, top, ci, cj + 1, new(x0, z1), new(x1, z1), Vector3.UnitZ);
        }
    }

    /// <summary>The sides of one column facing neighbour (ni, nj) along the edge p..q (outward normal <paramref name="n"/>).</summary>
    private void Side<T>(ref T sink, ReadOnlySpan<float> s, int c, float top, int ni, int nj, Vector2 p, Vector2 q, Vector3 n) where T : struct, IDigFaceSink
    {
        if ((uint)ni >= (uint)ColsX || (uint)nj >= (uint)ColsZ)
            return; // the terrain's edge
        var nc = Col(ni, nj);
        if (nc >= 0)
        {
            // Both dug: where this column is solid and the next is not, facing out (the next column draws its own).
            var ns = new ReadOnlySpan<float>(_spans[nc], 0, _count[nc] * 2);
            for (var k = 0; k < s.Length; k += 2)
            {
                var lo = s[k];
                var hi = s[k + 1];
                var covered = k + 2 < s.Length;
                // Walk the next column's spans that overlap [lo, hi]: the gaps between them are open faces.
                var y = lo;
                for (var m = 0; m < ns.Length && y < hi; m += 2)
                {
                    if (ns[m + 1] <= y)
                        continue;
                    if (ns[m] >= hi)
                        break;
                    if (ns[m] > y)
                        Wall(ref sink, p, q, y, y, ns[m], ns[m], n, c, covered);
                    y = MathF.Max(y, ns[m + 1]);
                }
                if (y < hi)
                    Wall(ref sink, p, q, y, y, hi, hi, n, c, covered);
            }
            return;
        }
        // The next column is untouched heightfield: solid up to its surface along the shared edge.
        var sp = _t.BaseGroundTop(p.X, p.Y);
        var sq = _t.BaseGroundTop(q.X, q.Y);
        for (var k = 0; k < s.Length; k += 2)
        {
            float lo = s[k], hi = s[k + 1];
            var covered = k + 2 < s.Length;
            // This column's ground standing above the next one's surface: faces out. Air over this span (up to the next
            // span, or the sky) below the next column's surface: the dug wall, facing in.
            var g1 = covered ? s[k + 2] : float.PositiveInfinity;
            Sloped(ref sink, p, q, sp, sq, lo, hi, true, n, c, covered);
            Sloped(ref sink, p, q, sp, sq, hi, g1, false, -n, c, covered || MathF.Max(sp, sq) < top);
        }
    }

    /// <summary>
    /// A wall between a dug column and untouched ground whose surface runs from <paramref name="sp"/> to <paramref name="sq"/>
    /// along the edge: <paramref name="above"/>, the part of [lo, hi] over the surface; else the part under it. Split where
    /// the surface crosses lo or hi, so every piece's edges are straight and the seam is exact.
    /// </summary>
    private void Sloped<T>(ref T sink, Vector2 p, Vector2 q, float sp, float sq, float lo, float hi, bool above, Vector3 n, int c, bool covered)
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
            var a = Vector2.Lerp(p, q, u0);
            var b = Vector2.Lerp(p, q, u1);
            if (above)
            {
                float l0 = MathF.Min(MathF.Max(lo, s0), hi), l1 = MathF.Min(MathF.Max(lo, s1), hi);
                if (hi - l0 > 0.001f || hi - l1 > 0.001f)
                    Wall(ref sink, a, b, l0, l1, hi, hi, n, c, covered);
            }
            else
            {
                float h0 = MathF.Max(MathF.Min(hi, s0), lo), h1 = MathF.Max(MathF.Min(hi, s1), lo);
                if (h0 - lo > 0.001f || h1 - lo > 0.001f)
                    Wall(ref sink, a, b, lo, lo, h0, h1, n, c, covered);
            }
        }
    }

    private void Wall<T>(ref T sink, Vector2 p, Vector2 q, float loP, float loQ, float hiP, float hiQ, Vector3 n, int c, bool covered)
        where T : struct, IDigFaceSink
    {
        var (col, rock) = Shade(c, (MathF.Min(loP, loQ) + MathF.Max(hiP, hiQ)) * 0.5f, covered);
        Emit(ref sink, new(p.X, loP, p.Y), new(q.X, loQ, q.Y), new(q.X, hiQ, q.Y), new(p.X, hiP, p.Y), n, col * 0.92f, rock);
    }

    /// <summary>Hands a quad on wound to face <paramref name="n"/> (diagonals' cross product, robust when one end is a point).</summary>
    private static void Emit<T>(ref T sink, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n, Vector3 col, float rock) where T : struct, IDigFaceSink
    {
        if (Vector3.Dot(Vector3.Cross(c - a, d - b), n) < 0)
            (b, d) = (d, b);
        sink.Quad(a, b, c, d, n, col, rock);
    }

    /// <summary>
    /// How column <paramref name="c"/> looks at height <paramref name="y"/> on a dug face: what a fill laid there is made of
    /// (the map's own dirt, rock or sand colour), else freshly dug ground by its depth.
    /// </summary>
    private (Vector3 Color, float Rock) Shade(int c, float y, bool covered)
    {
        var sf = _t.Surface;
        if (_fillKind[c] == 0 || y < _fillLo[c] - 0.01f)
            return Dug(sf, _orig[c] - y, covered);
        var (col, rock) = (GroundKind)(_fillKind[c] - 1) switch
        {
            GroundKind.Stone => (sf.Rock, 1f),
            GroundKind.Sand => (sf.Sand, 0.2f),
            _ => (sf.Dirt, 0.25f),
        };
        return (col * (covered ? 0.6f : 1f), rock);
    }

    /// <summary><see cref="Shade"/> for a span's top: open ground as built keeps the surface's own colour.</summary>
    private (Vector3 Color, float Rock) TopShade(int c, int ci, int cj, float y, bool covered) =>
        !covered && MathF.Abs(y - _orig[c]) < 0.01f && _fillKind[c] == 0 ? SurfaceColour(ci, cj, IslandColumn(c, ci, cj) ? _orig[c] : float.NaN) : Shade(c, y, covered);

    /// <summary>Freshly dug ground: dirt, then rock deeper down; darker under cover.</summary>
    private static (Vector3 Color, float Rock) Dug(TerrainSurface sf, float depth, bool covered)
    {
        var f = Math.Clamp(depth / 4f, 0f, 1f);
        var col = Vector3.Lerp(sf.Dirt * 0.85f, sf.Rock * 0.8f, f) * (covered ? 0.6f : 1f);
        return (col, Math.Clamp(0.25f + depth / 3f, 0.25f, 1f));
    }

    /// <summary>The surface's own colour at a column (its nearest terrain sample), for the undug tops of a dug cell; an island's top (<paramref name="top"/> finite) by the surface rules at its height.</summary>
    private (Vector3, float) SurfaceColour(int ci, int cj, float top)
    {
        if (float.IsFinite(top))
            return TerrainData.ColourOf(_t.Surface, Vector3.UnitY, top, (byte)TerrainMaterial.Grass, _t.OriginX + (ci + 0.5f) * Size, _t.OriginZ + (cj + 0.5f) * Size);
        var i = Math.Clamp((int)MathF.Round((ci + 0.5f) / Sub), 0, _t.CountX - 1);
        var j = Math.Clamp((int)MathF.Round((cj + 0.5f) / Sub), 0, _t.CountZ - 1);
        return _t.VertexColor(i, j);
    }
}
