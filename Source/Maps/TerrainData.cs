using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// A generated heightfield: pure data, no collision logic. Samples sit on a regular grid,
/// x = <see cref="OriginX"/> + i * <see cref="Cell"/>, z = <see cref="OriginZ"/> + j * <see cref="Cell"/>,
/// stored row by row in <see cref="Heights"/> (index j * CountX + i), with one <see cref="TerrainMaterial"/> per sample.
/// Between samples the surface is bilinear. This is everything a physics engine needs to build its own
/// heightfield shape (e.g. a Jolt HeightFieldShape: square, so pad to max(CountX, CountZ) rounded up to its block size,
/// offset = (OriginX, 0, OriginZ), scale = (Cell, 1, Cell); Jolt triangulates cells, which differs from bilinear by a few
/// centimetres on smooth ground).
/// </summary>
public sealed class TerrainData
{
    public readonly int CountX, CountZ;
    public readonly float Cell, OriginX, OriginZ;

    /// <summary>Raw heights in metres, row-major (j * CountX + i). Multiples of 1/1024 m.</summary>
    public readonly float[] Heights;

    /// <summary>Surface type per sample (<see cref="TerrainMaterial"/>), same layout as <see cref="Heights"/>.</summary>
    public readonly byte[] MaterialIds;

    /// <summary>
    /// Lowest and highest ground. <see cref="MinHeight"/> includes hole and cave floors (so clamps never push anyone up
    /// out of a cave); <see cref="MaxHeight"/> is the highest surface sample.
    /// </summary>
    public readonly float MinHeight, MaxHeight;

    public readonly TerrainSurface Surface;

    /// <summary>Holes and caves (null: none, the plain heightfield it always was). See <see cref="TerrainVoids"/>.</summary>
    public readonly TerrainVoids? Voids;

    /// <summary>FNV-1a fingerprint of the grid size, heights and materials (part of <see cref="Map.ContentHash"/>).</summary>
    public readonly uint Hash;

    public float MaxX => OriginX + (CountX - 1) * Cell;
    public float MaxZ => OriginZ + (CountZ - 1) * Cell;

    public TerrainData(int countX, int countZ, float cell, float originX, float originZ, float[] heights, byte[] materials, TerrainSurface surface,
        TerrainVoids? voids = null)
    {
        if (countX < 2 || countZ < 2 || heights.Length != countX * countZ || materials.Length != heights.Length)
            throw new ArgumentException("terrain grid size doesn't match its data");
        CountX = countX;
        CountZ = countZ;
        Cell = cell;
        OriginX = originX;
        OriginZ = originZ;
        Heights = heights;
        MaterialIds = materials;
        Surface = surface;
        Voids = voids;
        MinHeight = heights.Min();
        MaxHeight = heights.Max();
        if (voids != null)
        {
            if (voids.Kinds.Length != (countX - 1) * (countZ - 1) || voids.Floor.Length != heights.Length || voids.Ceiling.Length != heights.Length)
                throw new ArgumentException("terrain hole data doesn't match its grid");
            MinHeight = MathF.Min(MinHeight, voids.MinFloor);
        }
        Hash = ComputeHash();
    }

    public float Height(int i, int j) => Heights[j * CountX + i];

    /// <summary>
    /// Bilinear height at a world position (clamped to the grid's edge): the terrain surface, or in a hole the floor under
    /// it (the next surface down). Over a roofed cave this is the surface on top; see <see cref="GroundBelow"/>.
    /// </summary>
    public float Sample(float x, float z)
    {
        Locate(x, z, out var i, out var j, out var tx, out var tz);
        var k = j * CountX + i;
        if (Voids != null && Voids.Kinds[j * (CountX - 1) + i] == TerrainVoids.Open)
            return Bilerp(Voids.Floor, k, tx, tz);
        var h00 = Heights[k];
        var h10 = Heights[k + 1];
        var h01 = Heights[k + CountX];
        var h11 = Heights[k + CountX + 1];
        return (h00 * (1 - tx) + h10 * tx) * (1 - tz) + (h01 * (1 - tx) + h11 * tx) * tz;
    }

    /// <summary>Surface normal of the bilinear patch at a world position (in a hole: of the floor under it).</summary>
    public Vector3 SampleNormal(float x, float z)
    {
        Locate(x, z, out var i, out var j, out var tx, out var tz);
        var k = j * CountX + i;
        if (Voids != null && Voids.Kinds[j * (CountX - 1) + i] == TerrainVoids.Open)
            return PatchNormal(Voids.Floor, k, tx, tz);
        return PatchNormal(Heights, k, tx, tz);
    }

    private Vector3 PatchNormal(float[] a, int k, float tx, float tz)
    {
        var h00 = a[k];
        var h10 = a[k + 1];
        var h01 = a[k + CountX];
        var h11 = a[k + CountX + 1];
        var dx = ((h10 - h00) * (1 - tz) + (h11 - h01) * tz) / Cell;
        var dz = ((h01 - h00) * (1 - tx) + (h11 - h10) * tx) / Cell;
        return Vector3.Normalize(new Vector3(-dx, 1, -dz));
    }

    private float Bilerp(float[] a, int k, float tx, float tz) =>
        (a[k] * (1 - tx) + a[k + 1] * tx) * (1 - tz) + (a[k + CountX] * (1 - tx) + a[k + CountX + 1] * tx) * tz;

    // ---- Holes and caves (all of these answer as plain terrain when Voids is null) ----------------

    /// <summary>Does the map have holes or caves?</summary>
    public bool HasVoids => Voids != null;

    /// <summary>Kind of cell (i, j): <see cref="TerrainVoids.Solid"/>, <see cref="TerrainVoids.Open"/> or <see cref="TerrainVoids.Cave"/>.</summary>
    public byte CellKind(int i, int j) => Voids == null ? TerrainVoids.Solid : Voids.Kinds[j * (CountX - 1) + i];

    /// <summary>Kind of the cell under a world position.</summary>
    public byte KindAt(float x, float z)
    {
        if (Voids == null)
            return TerrainVoids.Solid;
        Locate(x, z, out var i, out var j, out _, out _);
        return Voids.Kinds[j * (CountX - 1) + i];
    }

    /// <summary>Is (x, z) over a hole (no terrain surface there)?</summary>
    public bool IsHole(float x, float z) => KindAt(x, z) == TerrainVoids.Open;

    /// <summary>Is any hole within <paramref name="radius"/> of (x, z) (checked at the centre and 8 points round it)?</summary>
    public bool HoleNear(float x, float z, float radius)
    {
        if (Voids == null || Voids.OpenCells == 0)
            return false;
        if (IsHole(x, z))
            return true;
        for (var a = 0; a < 8; a++)
        {
            var ang = a * (MathF.PI / 4);
            if (IsHole(x + MathF.Cos(ang) * radius, z + MathF.Sin(ang) * radius))
                return true;
        }
        return false;
    }

    /// <summary>
    /// The floor, ceiling and surface heights of the column at (x, z) and its cell kind. In a roofed cave cell where the
    /// ceiling has come down to the floor (a cave wall) <paramref name="hollow"/> is false.
    /// </summary>
    public byte Column(float x, float z, out float floor, out float ceiling, out float top, out bool hollow)
    {
        Locate(x, z, out var i, out var j, out var tx, out var tz);
        return ColumnInCell(i, j, tx, tz, out floor, out ceiling, out top, out hollow);
    }

    /// <summary><see cref="Column"/> for a known cell and a position inside it (0..1).</summary>
    public byte ColumnInCell(int i, int j, float tx, float tz, out float floor, out float ceiling, out float top, out bool hollow)
    {
        var k = j * CountX + i;
        top = Bilerp(Heights, k, tx, tz);
        floor = ceiling = top;
        hollow = false;
        if (Voids == null)
            return TerrainVoids.Solid;
        var kind = Voids.Kinds[j * (CountX - 1) + i];
        if (kind == TerrainVoids.Solid)
            return kind;
        floor = Bilerp(Voids.Floor, k, tx, tz);
        if (kind == TerrainVoids.Open)
        {
            ceiling = float.PositiveInfinity;
            hollow = true;
            return kind;
        }
        ceiling = Bilerp(Voids.Ceiling, k, tx, tz);
        hollow = ceiling > floor + TerrainVoids.MinGap;
        return kind;
    }

    /// <summary>
    /// The ground under a point at height <paramref name="y"/>: the terrain surface, or in a hole the floor below, or in a
    /// roofed cave the floor when the point is in the cave (below the middle of its roof) and the surface when it's on top.
    /// </summary>
    public float GroundBelow(float x, float z, float y)
    {
        if (Voids == null)
            return Sample(x, z);
        var kind = Column(x, z, out var floor, out var ceiling, out var top, out var hollow);
        return kind switch
        {
            TerrainVoids.Open => floor,
            TerrainVoids.Cave when hollow && y < (ceiling + top) * 0.5f => floor,
            _ => top,
        };
    }

    /// <summary>The cave ceiling over a point in a cave (below the middle of its roof), else +infinity.</summary>
    public float CeilingAbove(float x, float z, float y)
    {
        if (Voids == null)
            return float.PositiveInfinity;
        var kind = Column(x, z, out _, out var ceiling, out var top, out var hollow);
        return kind == TerrainVoids.Cave && hollow && y < (ceiling + top) * 0.5f ? ceiling : float.PositiveInfinity;
    }

    /// <summary>Normal of the surface <see cref="GroundBelow"/> picks.</summary>
    public Vector3 NormalBelow(float x, float z, float y)
    {
        if (Voids == null)
            return SampleNormal(x, z);
        Locate(x, z, out var i, out var j, out var tx, out var tz);
        var kind = ColumnInCell(i, j, tx, tz, out _, out var ceiling, out var top, out var hollow);
        var k = j * CountX + i;
        if (kind == TerrainVoids.Open || (kind == TerrainVoids.Cave && hollow && y < (ceiling + top) * 0.5f))
            return PatchNormal(Voids.Floor, k, tx, tz);
        return PatchNormal(Heights, k, tx, tz);
    }

    /// <summary>Downward-facing normal of a cave's ceiling at (x, z) (for hits from below).</summary>
    public Vector3 CeilingNormal(float x, float z)
    {
        if (Voids == null)
            return -Vector3.UnitY;
        Locate(x, z, out var i, out var j, out var tx, out var tz);
        return -PatchNormal(Voids.Ceiling, j * CountX + i, tx, tz);
    }

    /// <summary>Is the point inside solid ground (counting holes and caves)?</summary>
    public bool SolidAt(float x, float z, float y)
    {
        Locate(x, z, out var i, out var j, out var tx, out var tz);
        return SolidInCell(i, j, tx, tz, y);
    }

    internal bool SolidInCell(int i, int j, float tx, float tz, float y)
    {
        var kind = ColumnInCell(i, j, tx, tz, out var floor, out var ceiling, out var top, out var hollow);
        return kind switch
        {
            TerrainVoids.Open => y <= floor,
            TerrainVoids.Cave when hollow => y <= floor || (y >= ceiling && y <= top),
            _ => y <= top,
        };
    }

    /// <summary>Cell containing (x, z) and the position inside it (0..1), clamped to the grid.</summary>
    public void Locate(float x, float z, out int i, out int j, out float tx, out float tz)
    {
        var fx = Math.Clamp((x - OriginX) / Cell, 0f, CountX - 1);
        var fz = Math.Clamp((z - OriginZ) / Cell, 0f, CountZ - 1);
        i = Math.Min((int)fx, CountX - 2);
        j = Math.Min((int)fz, CountZ - 2);
        tx = fx - i;
        tz = fz - j;
    }

    /// <summary>Smooth per-sample normal (central differences): used for drawing.</summary>
    public Vector3 VertexNormal(int i, int j)
    {
        var hl = Heights[j * CountX + Math.Max(i - 1, 0)];
        var hr = Heights[j * CountX + Math.Min(i + 1, CountX - 1)];
        var hd = Heights[Math.Max(j - 1, 0) * CountX + i];
        var hu = Heights[Math.Min(j + 1, CountZ - 1) * CountX + i];
        var sx = (Math.Min(i + 1, CountX - 1) - Math.Max(i - 1, 0)) * Cell;
        var sz = (Math.Min(j + 1, CountZ - 1) - Math.Max(j - 1, 0)) * Cell;
        return Vector3.Normalize(new Vector3(-(hr - hl) / sx, 1, -(hu - hd) / sz));
    }

    /// <summary>
    /// Drawing colour of a sample (blended smoothly by slope and height from the <see cref="Surface"/> rules, painted
    /// samples use their material's colour) and how rocky it looks (0-1, picks the cliff texture).
    /// </summary>
    public (Vector3 Color, float Rock) VertexColor(int i, int j)
    {
        var s = Surface;
        var n = VertexNormal(i, j);
        var slope = MathF.Acos(Math.Clamp(n.Y, -1f, 1f)) * (180f / MathF.PI);
        var h = Height(i, j);
        var rock = Smooth((slope - ((float)s.RockSlope - 4f)) / 8f);
        Vector3 color;
        var painted = MaterialIds[j * CountX + i];
        var auto = PaintedAuto(i, j);
        if (!auto)
            color = Vector3.Lerp(s.ColorOf((TerrainMaterial)painted), s.Rock, rock * 0.6f);
        else
        {
            var dirt = Smooth((slope - ((float)s.DirtSlope - 4f)) / 8f) * (1 - rock);
            var sand = s.SandBelow is { } sb ? Smooth(((float)sb + 0.75f - h) / 1.5f) * (1 - rock - dirt) : 0f;
            var grass = MathF.Max(0, 1 - rock - dirt - sand);
            color = s.Grass * grass + s.Dirt * dirt + s.Rock * rock + s.Sand * sand;
        }
        var x = OriginX + i * Cell;
        var z = OriginZ + j * Cell;
        var vary = 1f + (float)(s.Variation * TerrainGen.ValueNoise(x / 9.0, z / 9.0, 4242));
        return (color * vary, rock);
    }

    // A sample "follows the rules" unless a feature painted it something the rules wouldn't pick.
    private bool PaintedAuto(int i, int j)
    {
        var id = (TerrainMaterial)MaterialIds[j * CountX + i];
        var n = VertexNormal(i, j);
        var slope = MathF.Acos(Math.Clamp(n.Y, -1f, 1f)) * (180f / MathF.PI);
        var rule = slope >= Surface.RockSlope ? TerrainMaterial.Rock
            : slope >= Surface.DirtSlope ? TerrainMaterial.Dirt
            : Surface.SandBelow is { } sb && Height(i, j) <= sb ? TerrainMaterial.Sand
            : TerrainMaterial.Grass;
        return id == rule;
    }

    private static float Smooth(float e)
    {
        var t = Math.Clamp(e, 0f, 1f);
        return t * t * (3 - 2 * t);
    }

    private uint ComputeHash()
    {
        var h = 2166136261u;
        void Mix(uint v)
        {
            for (var b = 0; b < 4; b++)
            {
                h ^= (v >> (b * 8)) & 0xFF;
                h *= 16777619u;
            }
        }
        Mix((uint)CountX);
        Mix((uint)CountZ);
        foreach (var v in Heights)
            Mix(BitConverter.SingleToUInt32Bits(v));
        foreach (var m in MaterialIds)
        {
            h ^= m;
            h *= 16777619u;
        }
        if (Voids != null) // only maps with holes or caves: every other terrain keeps its fingerprint
            h = Voids.Mix(h);
        return h;
    }
}

/// <summary>
/// Collision queries against the ground. Movement, ray casts, throwables, spawns and bots only use this interface, so
/// the custom heightfield code below can be swapped for a physics engine's heightfield (e.g. Jolt) built from <see cref="Data"/>.
/// </summary>
public interface ITerrainCollision
{
    /// <summary>The heightfield this collision was built from.</summary>
    TerrainData Data { get; }

    /// <summary>Ground height at (x, z).</summary>
    float HeightAt(float x, float z);

    /// <summary>Unit surface normal at (x, z).</summary>
    Vector3 NormalAt(float x, float z);

    /// <summary>Distance along a normalised ray to the ground, or <paramref name="maxDist"/> if it doesn't hit within that.</summary>
    float Raycast(Vector3 origin, Vector3 dir, float maxDist);

    // ---- Holes and caves (TerrainVoids.cs). The defaults are the plain heightfield, so other implementations keep working. ----

    /// <summary>Does this ground have holes or caves (so a point's height matters, not just its x and z)?</summary>
    bool HasVoids => false;

    /// <summary>The ground under a point at height <paramref name="y"/> (in a cave: its floor; on top of it: the surface).</summary>
    float GroundBelow(float x, float z, float y) => HeightAt(x, z);

    /// <summary>A cave's ceiling over a point in it, else +infinity.</summary>
    float CeilingAbove(float x, float z, float y) => float.PositiveInfinity;

    /// <summary>
    /// This ground as seen from height <paramref name="y"/>: a view whose <see cref="HeightAt"/> and <see cref="NormalAt"/>
    /// answer <see cref="GroundBelow"/> for that height, for code that samples the ground around a body (slope rules,
    /// movers). Plain ground returns itself (no allocation).
    /// </summary>
    ITerrainCollision Layer(float y) => this;

    /// <summary>Normal at a ray hit (walls and ceilings of holes and caves included).</summary>
    Vector3 HitNormal(Vector3 point, Vector3 dir) => NormalAt(point.X, point.Z);
}

/// <summary>
/// The game's own heightfield collision: bilinear heights and normals, and an exact ray cast that walks the grid
/// cell by cell (2D DDA) and solves the ray against each cell's bilinear patch, skipping cells the ray passes above.
/// </summary>
public sealed class HeightfieldCollision(TerrainData data) : ITerrainCollision
{
    public TerrainData Data { get; } = data;

    public float HeightAt(float x, float z) => Data.Sample(x, z);

    public Vector3 NormalAt(float x, float z) => Data.SampleNormal(x, z);

    public bool HasVoids => Data.Voids != null;

    public float GroundBelow(float x, float z, float y) => Data.GroundBelow(x, z, y);

    public float CeilingAbove(float x, float z, float y) => Data.CeilingAbove(x, z, y);

    public ITerrainCollision Layer(float y) => Data.Voids == null ? this : new TerrainLayer(this, y);

    public Vector3 HitNormal(Vector3 point, Vector3 dir)
    {
        var d = Data;
        if (d.Voids == null)
            return d.SampleNormal(point.X, point.Z);
        const float near = 0.08f;
        var kind = d.Column(point.X, point.Z, out var floor, out var ceiling, out var top, out var hollow);
        if (kind == TerrainVoids.Solid || (kind == TerrainVoids.Cave && !hollow))
        {
            if (point.Y >= top - near)
                return d.SampleNormal(point.X, point.Z);
        }
        else
        {
            if (MathF.Abs(point.Y - floor) < near)
                return d.NormalBelow(point.X, point.Z, floor + 0.01f);
            if (kind == TerrainVoids.Cave && MathF.Abs(point.Y - ceiling) < near)
                return d.CeilingNormal(point.X, point.Z);
            if (kind == TerrainVoids.Cave && point.Y >= top - near)
                return d.NormalBelow(point.X, point.Z, top);
        }
        // A wall (a hole's side or a cave's): face back along the ray, across the nearer cell line.
        var fx = (point.X - d.OriginX) / d.Cell;
        var fz = (point.Z - d.OriginZ) / d.Cell;
        var ex = MathF.Abs(fx - MathF.Round(fx));
        var ez = MathF.Abs(fz - MathF.Round(fz));
        if (ex < ez && MathF.Abs(dir.X) > 1e-4f)
            return new Vector3(-MathF.Sign(dir.X), 0, 0);
        if (MathF.Abs(dir.Z) > 1e-4f)
            return new Vector3(0, 0, -MathF.Sign(dir.Z));
        var flat = new Vector3(-dir.X, 0, -dir.Z);
        return flat.LengthSquared() > 1e-8f ? Vector3.Normalize(flat) : Vector3.UnitY;
    }

    public float Raycast(Vector3 origin, Vector3 dir, float maxDist)
    {
        var d = Data;
        float t0 = 0, t1 = maxDist;
        if (!Clip(origin.X, dir.X, d.OriginX, d.MaxX, ref t0, ref t1) || !Clip(origin.Z, dir.Z, d.OriginZ, d.MaxZ, ref t0, ref t1))
            return maxDist;
        // Only the stretch of the ray below the highest sample can touch the ground.
        if (MathF.Abs(dir.Y) < 1e-9f)
        {
            if (origin.Y > d.MaxHeight)
                return maxDist;
        }
        else
        {
            var tTop = (d.MaxHeight - origin.Y) / dir.Y;
            if (dir.Y > 0)
                t1 = MathF.Min(t1, origin.Y > d.MaxHeight ? -1 : tTop);
            else
                t0 = MathF.Max(t0, tTop);
        }
        if (t0 > t1)
            return maxDist;

        var cell = d.Cell;
        var start = origin + dir * t0;
        d.Locate(start.X, start.Z, out var i, out var j, out _, out _);
        var stepX = dir.X > 0 ? 1 : dir.X < 0 ? -1 : 0;
        var stepZ = dir.Z > 0 ? 1 : dir.Z < 0 ? -1 : 0;
        var tNextX = stepX == 0 ? float.MaxValue : (d.OriginX + (i + (stepX > 0 ? 1 : 0)) * cell - origin.X) / dir.X;
        var tNextZ = stepZ == 0 ? float.MaxValue : (d.OriginZ + (j + (stepZ > 0 ? 1 : 0)) * cell - origin.Z) / dir.Z;
        var tDeltaX = stepX == 0 ? float.MaxValue : cell / MathF.Abs(dir.X);
        var tDeltaZ = stepZ == 0 ? float.MaxValue : cell / MathF.Abs(dir.Z);
        var tEnter = t0;
        for (var guard = 0; guard < 1 << 16; guard++)
        {
            var tExit = MathF.Min(MathF.Min(tNextX, tNextZ), t1);
            if (tExit >= tEnter && (d.Voids == null ? CellHit(i, j, origin, dir, tEnter, tExit) : VoidCellHit(i, j, origin, dir, tEnter, tExit)) is { } hit)
                return hit;
            if (tExit >= t1)
                break;
            if (tNextX < tNextZ)
            {
                i += stepX;
                tNextX += tDeltaX;
            }
            else
            {
                j += stepZ;
                tNextZ += tDeltaZ;
            }
            if (i < 0 || j < 0 || i > d.CountX - 2 || j > d.CountZ - 2)
                break;
            tEnter = tExit;
        }
        return maxDist;
    }

    private static bool Clip(float o, float dir, float lo, float hi, ref float t0, ref float t1)
    {
        if (MathF.Abs(dir) < 1e-9f)
            return o >= lo && o <= hi;
        var a = (lo - o) / dir;
        var b = (hi - o) / dir;
        if (a > b)
            (a, b) = (b, a);
        t0 = MathF.Max(t0, a);
        t1 = MathF.Min(t1, b);
        return t0 <= t1;
    }

    /// <summary>
    /// <see cref="CellHit(int, int, Vector3, Vector3, float, float)"/> on a map with holes and caves: solid cells as always,
    /// holes against their floor patch, roofed caves by marching the ray through the cell (10 cm steps, then bisection)
    /// against the solid parts of the column (below the floor, between ceiling and surface, walls).
    /// </summary>
    private float? VoidCellHit(int i, int j, Vector3 o, Vector3 dir, float tEnter, float tExit)
    {
        var d = Data;
        var kind = d.CellKind(i, j);
        if (kind == TerrainVoids.Solid)
            return CellHit(i, j, o, dir, tEnter, tExit);
        if (kind == TerrainVoids.Open)
            return CellHit(i, j, o, dir, tEnter, tExit, d.Voids!.Floor);
        bool Solid(float t)
        {
            var p = o + dir * t;
            var tx = Math.Clamp((p.X - (d.OriginX + i * d.Cell)) / d.Cell, 0f, 1f);
            var tz = Math.Clamp((p.Z - (d.OriginZ + j * d.Cell)) / d.Cell, 0f, 1f);
            return d.SolidInCell(i, j, tx, tz, p.Y);
        }
        if (Solid(tEnter))
            return tEnter;
        var span = tExit - tEnter;
        var steps = Math.Clamp((int)MathF.Ceiling(span / 0.1f), 1, 256);
        var prev = tEnter;
        for (var s = 1; s <= steps; s++)
        {
            var t = tEnter + span * s / steps;
            if (!Solid(t))
            {
                prev = t;
                continue;
            }
            float lo = prev, hi = t;
            for (var b = 0; b < 10; b++)
            {
                var mid = (lo + hi) * 0.5f;
                if (Solid(mid))
                    hi = mid;
                else
                    lo = mid;
            }
            return hi;
        }
        return null;
    }

    /// <summary>First point in [tEnter, tExit] where the ray meets cell (i, j)'s bilinear patch, or null.</summary>
    private float? CellHit(int i, int j, Vector3 o, Vector3 dir, float tEnter, float tExit) => CellHit(i, j, o, dir, tEnter, tExit, Data.Heights);

    /// <summary>The same against another sheet of samples on the grid (a hole's floor).</summary>
    private float? CellHit(int i, int j, Vector3 o, Vector3 dir, float tEnter, float tExit, float[] heights)
    {
        var d = Data;
        var k = j * d.CountX + i;
        double h00 = heights[k], h10 = heights[k + 1], h01 = heights[k + d.CountX], h11 = heights[k + d.CountX + 1];
        var yA = o.Y + dir.Y * tEnter;
        var yB = o.Y + dir.Y * tExit;
        if (MathF.Min(yA, yB) > Math.Max(Math.Max(h00, h10), Math.Max(h01, h11)))
            return null; // passes above the whole cell

        // h(u, v) = a + b u + c v + e u v over the cell (u, v in 0..1); the ray enters at (u0, v0, y0).
        double cell = d.Cell;
        double u0 = (o.X + (double)dir.X * tEnter - (d.OriginX + i * cell)) / cell;
        double v0 = (o.Z + (double)dir.Z * tEnter - (d.OriginZ + j * cell)) / cell;
        double y0 = yA;
        double du = dir.X / cell, dv = dir.Z / cell;
        double a = h00, b = h10 - h00, c = h01 - h00, e = h00 - h10 - h01 + h11;
        // f(s) = height under the ray minus the ray's height, s = distance past tEnter. Hit where f >= 0.
        var C = a + b * u0 + c * v0 + e * u0 * v0 - y0;
        if (C >= 0)
            return tEnter; // already at or under the ground here
        var B = b * du + c * dv + e * (u0 * dv + v0 * du) - dir.Y;
        var A = e * du * dv;
        double span = tExit - tEnter;
        double s;
        if (Math.Abs(A) < 1e-12)
        {
            if (B <= 0)
                return null;
            s = -C / B;
        }
        else
        {
            var disc = B * B - 4 * A * C;
            if (disc < 0)
                return null;
            var sq = Math.Sqrt(disc);
            var r1 = (-B - sq) / (2 * A);
            var r2 = (-B + sq) / (2 * A);
            if (r1 > r2)
                (r1, r2) = (r2, r1);
            s = r1 >= 0 ? r1 : r2;
        }
        return s >= 0 && s <= span ? (float)(tEnter + s) : null;
    }
}

/// <summary>
/// Ground with holes and caves as seen from one height (<see cref="ITerrainCollision.Layer"/>): HeightAt and NormalAt pick
/// the floor in a cave and the surface on top of it, so code that samples the ground round a body (the slope rules, the
/// movers) works the same in a tunnel as on a hillside.
/// </summary>
public sealed class TerrainLayer(HeightfieldCollision ground, float y) : ITerrainCollision
{
    /// <summary>The height this view looks from.</summary>
    public float Y { get; } = y;

    public TerrainData Data => ground.Data;

    public float HeightAt(float x, float z) => ground.Data.GroundBelow(x, z, Y);

    public Vector3 NormalAt(float x, float z) => ground.Data.NormalBelow(x, z, Y);

    public float Raycast(Vector3 origin, Vector3 dir, float maxDist) => ground.Raycast(origin, dir, maxDist);

    public bool HasVoids => true;

    public float GroundBelow(float x, float z, float y) => ground.Data.GroundBelow(x, z, y);

    public float CeilingAbove(float x, float z, float y) => ground.Data.CeilingAbove(x, z, y);

    public ITerrainCollision Layer(float y) => ground.Layer(y);

    public Vector3 HitNormal(Vector3 point, Vector3 dir) => ground.HitNormal(point, dir);
}
