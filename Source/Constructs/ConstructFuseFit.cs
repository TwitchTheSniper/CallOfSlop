using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// A fused unit's rough collider (docs/sandbox/CONSTRUCTS.md, "Fusing"): a few boxes, deterministic, conservative, in the
/// unit's segment frame. The method:
/// <list type="number">
/// <item>The covered members' solid boxes (the collider's own: `SolidCount`/`SolidBox`) are marked on a grid of cubes over
/// their bounds, at most <see cref="CellBudget"/> cells (the cell is the bounds' volume over the budget, cube-rooted, never
/// under <see cref="MinCell"/>). A cell is full when any box reaches into it.</item>
/// <item>The full cells are merged greedily into boxes (along x as far as it goes, then whole rows along y, then whole
/// slabs along z, in a fixed order). Past <see cref="MaxRough"/> boxes the cells are made 1.6 times as big and it starts again.</item>
/// <item>Each box is shrunk to the real boxes inside it: the union of every member box clipped to it. A box still holds every
/// bit of real box that was in its cells, so the boxes together cover the unit (conservative); a face that real boxes reach is
/// where they are, so a flat deck rests on its own underside.</item>
/// </list>
/// The error is the cell: a point of a rough box is within one cell diagonal of a real box (where a cell is only partly
/// filled, or a step in a slope), and nothing real is outside. Cost: O(cells + the cells the boxes cover), no allocation
/// beyond the unit's own arrays when they grow (the grid is the thread's scratch).
/// </summary>
internal static class ConstructFuseFit
{
    public const int MaxRough = 256, CellBudget = 1 << 17, Attempts = 8;
    public const float MinCell = 0.05f;

    public static int Fits; // (counts for the harness)
    public static double FitMs;

    public static void Fit(Construct c, Construct.FuseUnit u, ReadOnlySpan<int> members)
    {
        var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        Fits++;
        u.RoughCount = 0;
        u.Covered = 0;
        u.RoughVolume = u.ExactVolume = 0;
        var lo = new Vector3(float.MaxValue);
        var hi = new Vector3(float.MinValue);
        var boxes = 0;
        foreach (var i in members)
            if (c.FuseCovers(i))
            {
                u.Covered++;
                boxes += c.SolidCount(i);
            }
        using var scratch = Scratch.Begin();
        var bmn = scratch.Take<Vector3>(boxes);
        var bmx = scratch.Take<Vector3>(boxes);
        boxes = 0;
        foreach (var i in members)
        {
            if (!c.FuseCovers(i))
                continue;
            var n = c.SolidCount(i);
            for (var b = 0; b < n; b++)
            {
                c.SolidBox(i, b, out var centre, out var half);
                bmn[boxes] = centre - half;
                bmx[boxes] = centre + half;
                lo = Vector3.Min(lo, bmn[boxes]);
                hi = Vector3.Max(hi, bmx[boxes]);
                u.ExactVolume += 8.0 * half.X * half.Y * half.Z;
                boxes++;
            }
        }
        if (boxes == 0)
            return;
        var size = Vector3.Max(hi - lo, new Vector3(1e-4f));
        var h = MathF.Max(MinCell, MathF.Cbrt(size.X * size.Y * size.Z / CellBudget));
        int nx, ny, nz;
        while (true)
        {
            nx = Math.Max(1, (int)MathF.Ceiling(size.X / h));
            ny = Math.Max(1, (int)MathF.Ceiling(size.Y / h));
            nz = Math.Max(1, (int)MathF.Ceiling(size.Z / h));
            if ((long)nx * ny * nz <= CellBudget)
                break;
            h *= 1.15f;
        }
        var occ = scratch.Take<int>(nx * ny * nz); // -1: empty; -2: full, no box yet; k >= 0: box k
        Span<int> cellBox = stackalloc int[6 * MaxRough];
        var count = 0;
        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            var cells = nx * ny * nz;
            occ[..cells].Fill(-1);
            for (var b = 0; b < boxes; b++)
            {
                Range(bmn[b], bmx[b], lo, h, nx, ny, nz, out var x0, out var y0, out var z0, out var x1, out var y1, out var z1);
                for (var z = z0; z <= z1; z++)
                    for (var y = y0; y <= y1; y++)
                        occ.Slice((z * ny + y) * nx + x0, x1 - x0 + 1).Fill(-2);
            }
            count = Greedy(occ, nx, ny, nz, cellBox);
            if (count >= 0 || attempt == Attempts - 1)
                break;
            h *= 1.6f;
            nx = Math.Max(1, (int)MathF.Ceiling(size.X / h));
            ny = Math.Max(1, (int)MathF.Ceiling(size.Y / h));
            nz = Math.Max(1, (int)MathF.Ceiling(size.Z / h));
        }
        if (count < 0)
        {
            // (still too many: one box round the lot, as conservative as it gets)
            count = 1;
            cellBox[0] = cellBox[1] = cellBox[2] = 0;
            cellBox[3] = nx - 1;
            cellBox[4] = ny - 1;
            cellBox[5] = nz - 1;
            occ[..(nx * ny * nz)].Fill(0);
        }
        // Each box shrunk to the real boxes in it.
        if (u.RMin.Length < count)
        {
            u.RMin = new Vector3[Math.Max(count, 4)];
            u.RMax = new Vector3[Math.Max(count, 4)];
        }
        for (var k = 0; k < count; k++)
        {
            u.RMin[k] = new Vector3(float.MaxValue);
            u.RMax[k] = new Vector3(float.MinValue);
        }
        Span<int> seen = stackalloc int[MaxRough];
        seen.Fill(-1);
        for (var serial = 0; serial < boxes; serial++)
        {
            {
                var bmin = bmn[serial];
                var bmax = bmx[serial];
                Range(bmin, bmax, lo, h, nx, ny, nz, out var x0, out var y0, out var z0, out var x1, out var y1, out var z1);
                for (var z = z0; z <= z1; z++)
                    for (var y = y0; y <= y1; y++)
                    {
                        var row = (z * ny + y) * nx;
                        for (var x = x0; x <= x1; x++)
                        {
                            var k = occ[row + x];
                            if (k < 0 || seen[k] == serial)
                                continue;
                            seen[k] = serial;
                            var cmin = lo + new Vector3(cellBox[k * 6], cellBox[k * 6 + 1], cellBox[k * 6 + 2]) * h;
                            var cmax = lo + new Vector3(cellBox[k * 6 + 3] + 1, cellBox[k * 6 + 4] + 1, cellBox[k * 6 + 5] + 1) * h;
                            u.RMin[k] = Vector3.Min(u.RMin[k], Vector3.Max(bmin, cmin));
                            u.RMax[k] = Vector3.Max(u.RMax[k], Vector3.Min(bmax, cmax));
                        }
                    }
            }
        }
        var kept = 0;
        for (var k = 0; k < count; k++)
        {
            if (u.RMin[k].X > u.RMax[k].X)
                continue; // (no real box reached it: a cell touched only at an edge)
            u.RMin[kept] = u.RMin[k];
            u.RMax[kept] = u.RMax[k];
            var s = u.RMax[kept] - u.RMin[kept];
            u.RoughVolume += (double)s.X * s.Y * s.Z;
            kept++;
        }
        u.RoughCount = kept;
        u.Cell = h;
        FitMs += (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
    }

    /// <summary>The cells a box reaches into (a face exactly on a cell boundary doesn't reach the next cell).</summary>
    private static void Range(Vector3 bmin, Vector3 bmax, Vector3 lo, float h, int nx, int ny, int nz, out int x0, out int y0, out int z0, out int x1, out int y1, out int z1)
    {
        const float Eps = 1e-4f;
        var a = (bmin - lo) / h;
        var b = (bmax - lo) / h;
        x0 = Math.Clamp((int)MathF.Floor(a.X + Eps), 0, nx - 1);
        y0 = Math.Clamp((int)MathF.Floor(a.Y + Eps), 0, ny - 1);
        z0 = Math.Clamp((int)MathF.Floor(a.Z + Eps), 0, nz - 1);
        x1 = Math.Clamp((int)MathF.Ceiling(b.X - Eps) - 1, x0, nx - 1);
        y1 = Math.Clamp((int)MathF.Ceiling(b.Y - Eps) - 1, y0, ny - 1);
        z1 = Math.Clamp((int)MathF.Ceiling(b.Z - Eps) - 1, z0, nz - 1);
    }

    /// <summary>The full cells as boxes (cell ranges into <paramref name="box"/>, each cell numbered with its box), or -1 past <see cref="MaxRough"/>.</summary>
    private static int Greedy(Span<int> occ, int nx, int ny, int nz, Span<int> box)
    {
        var count = 0;
        for (var z = 0; z < nz; z++)
            for (var y = 0; y < ny; y++)
                for (var x = 0; x < nx; x++)
                {
                    if (occ[(z * ny + y) * nx + x] != -2)
                        continue;
                    if (count == MaxRough)
                        return -1;
                    var x1 = x;
                    while (x1 + 1 < nx && occ[(z * ny + y) * nx + x1 + 1] == -2)
                        x1++;
                    var y1 = y;
                    while (y1 + 1 < ny && Row(occ, nx, ny, x, x1, y1 + 1, z))
                        y1++;
                    var z1 = z;
                    while (z1 + 1 < nz && Slab(occ, nx, ny, x, x1, y, y1, z1 + 1))
                        z1++;
                    for (var zz = z; zz <= z1; zz++)
                        for (var yy = y; yy <= y1; yy++)
                            occ.Slice((zz * ny + yy) * nx + x, x1 - x + 1).Fill(count);
                    box[count * 6] = x;
                    box[count * 6 + 1] = y;
                    box[count * 6 + 2] = z;
                    box[count * 6 + 3] = x1;
                    box[count * 6 + 4] = y1;
                    box[count * 6 + 5] = z1;
                    count++;
                }
        return count;
    }

    private static bool Row(Span<int> occ, int nx, int ny, int x0, int x1, int y, int z)
    {
        var row = (z * ny + y) * nx;
        for (var x = x0; x <= x1; x++)
            if (occ[row + x] != -2)
                return false;
        return true;
    }

    private static bool Slab(Span<int> occ, int nx, int ny, int x0, int x1, int y0, int y1, int z)
    {
        for (var y = y0; y <= y1; y++)
            if (!Row(occ, nx, ny, x0, x1, y, z))
                return false;
        return true;
    }
}
