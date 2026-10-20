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

    public readonly float MinHeight, MaxHeight;
    public readonly TerrainSurface Surface;

    /// <summary>FNV-1a fingerprint of the grid size, heights and materials (part of <see cref="Map.ContentHash"/>).</summary>
    public readonly uint Hash;

    public float MaxX => OriginX + (CountX - 1) * Cell;
    public float MaxZ => OriginZ + (CountZ - 1) * Cell;

    public TerrainData(int countX, int countZ, float cell, float originX, float originZ, float[] heights, byte[] materials, TerrainSurface surface)
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
        MinHeight = heights.Min();
        MaxHeight = heights.Max();
        Hash = ComputeHash();
    }

    public float Height(int i, int j) => Heights[j * CountX + i];

    /// <summary>Bilinear height at a world position (clamped to the grid's edge).</summary>
    public float Sample(float x, float z)
    {
        Locate(x, z, out var i, out var j, out var tx, out var tz);
        var k = j * CountX + i;
        var h00 = Heights[k];
        var h10 = Heights[k + 1];
        var h01 = Heights[k + CountX];
        var h11 = Heights[k + CountX + 1];
        return (h00 * (1 - tx) + h10 * tx) * (1 - tz) + (h01 * (1 - tx) + h11 * tx) * tz;
    }

    /// <summary>Surface normal of the bilinear patch at a world position.</summary>
    public Vector3 SampleNormal(float x, float z)
    {
        Locate(x, z, out var i, out var j, out var tx, out var tz);
        var k = j * CountX + i;
        var h00 = Heights[k];
        var h10 = Heights[k + 1];
        var h01 = Heights[k + CountX];
        var h11 = Heights[k + CountX + 1];
        var dx = ((h10 - h00) * (1 - tz) + (h11 - h01) * tz) / Cell;
        var dz = ((h01 - h00) * (1 - tx) + (h11 - h10) * tx) / Cell;
        return Vector3.Normalize(new Vector3(-dx, 1, -dz));
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
            if (tExit >= tEnter && CellHit(i, j, origin, dir, tEnter, tExit) is { } hit)
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

    /// <summary>First point in [tEnter, tExit] where the ray meets cell (i, j)'s bilinear patch, or null.</summary>
    private float? CellHit(int i, int j, Vector3 o, Vector3 dir, float tEnter, float tExit)
    {
        var d = Data;
        var k = j * d.CountX + i;
        double h00 = d.Heights[k], h10 = d.Heights[k + 1], h01 = d.Heights[k + d.CountX], h11 = d.Heights[k + d.CountX + 1];
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
