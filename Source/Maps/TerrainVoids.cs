using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Holes and caves in a terrain (the "hole" and "cave" terrain features): one hollow space per column of ground, between a
/// floor and a ceiling surface sampled on the terrain's own grid, plus a kind per terrain cell:
/// <list type="bullet">
/// <item><see cref="Solid"/>: ordinary ground, solid from far below up to the terrain surface.</item>
/// <item><see cref="Open"/>: a hole. No terrain surface at all; the ground is the floor below (a pit's bottom or a cave's
/// floor), and nothing is drawn or collides at the surface. Walls are where the neighbouring cells stay solid.</item>
/// <item><see cref="Cave"/>: a roofed cave. Solid up to the floor, hollow from the floor up to the ceiling, then solid rock
/// again up to the terrain surface (at least <see cref="RoofMin"/> thick). Where the ceiling comes down to meet the floor
/// (a tunnel's sides) the column is solid again, which makes the cave's curved walls.</item>
/// </list>
/// Built once by <see cref="TerrainVoidGen"/> from the map's features (deterministic, part of the terrain's fingerprint).
/// Maps without holes or caves have no <see cref="TerrainVoids"/> at all, so they cost and hash exactly what they always did.
/// </summary>
public sealed class TerrainVoids
{
    /// <summary>Cell kinds.</summary>
    public const byte Solid = 0, Open = 1, Cave = 2;

    /// <summary>Rock left between a cave's ceiling and the terrain surface above it (metres).</summary>
    public const float RoofMin = 1f;

    /// <summary>Where floor and ceiling are closer than this, the column counts as solid (a cave wall).</summary>
    public const float MinGap = 0.02f;

    /// <summary>Kind of each terrain cell, (CountX - 1) x (CountZ - 1), row-major.</summary>
    public readonly byte[] Kinds;

    /// <summary>Floor height per sample (same layout as <see cref="TerrainData.Heights"/>); NaN where no hole or cave reaches.</summary>
    public readonly float[] Floor;

    /// <summary>Ceiling height per sample, already kept <see cref="RoofMin"/> under the surface; NaN where unused.</summary>
    public readonly float[] Ceiling;

    /// <summary>How much daylight reaches each floor sample (0-1): 1 at the bottom of an open hole, fading away from openings
    /// under a roof. Drawing only (derived from the rest, not part of the fingerprint).</summary>
    public readonly float[] Light;

    /// <summary>Feet positions along every cave's centre line (about every 2 m), for the bots' navigation graph.</summary>
    public readonly Vector3[] NavPoints;

    public readonly int OpenCells, CaveCells;

    /// <summary>Lowest floor anywhere (for clamps: nobody falls below this).</summary>
    public readonly float MinFloor;

    public TerrainVoids(byte[] kinds, float[] floor, float[] ceiling, float[] light, Vector3[] navPoints)
    {
        Kinds = kinds;
        Floor = floor;
        Ceiling = ceiling;
        Light = light;
        NavPoints = navPoints;
        foreach (var k in kinds)
        {
            if (k == Open)
                OpenCells++;
            else if (k == Cave)
                CaveCells++;
        }
        var lo = float.MaxValue;
        foreach (var f in floor)
            if (float.IsFinite(f))
                lo = MathF.Min(lo, f);
        MinFloor = lo == float.MaxValue ? 0f : lo;
    }

    /// <summary>Mixes the kinds, floors and ceilings into an FNV-1a hash (NaN has one fixed bit pattern here).</summary>
    public uint Mix(uint h)
    {
        void Byte(uint v)
        {
            h ^= v & 0xFF;
            h *= 16777619u;
        }
        void Word(uint v)
        {
            for (var b = 0; b < 4; b++)
                Byte(v >> (b * 8));
        }
        Word(0x484F4C45u); // "HOLE": a marker, so a map with voids can never hash like one without
        foreach (var k in Kinds)
            Byte(k);
        foreach (var f in Floor)
            Word(float.IsNaN(f) ? 0x7FC00000u : BitConverter.SingleToUInt32Bits(f));
        foreach (var c in Ceiling)
            Word(float.IsNaN(c) ? 0x7FC00000u : BitConverter.SingleToUInt32Bits(c));
        return h;
    }
}

/// <summary>
/// Builds <see cref="TerrainVoids"/> from a terrain's "hole" and "cave" features, after every height feature has run (so
/// holes and caves follow the finished ground). They run in file order among themselves; overlapping caves join up.
/// Plain double arithmetic in a fixed order, so every machine builds the same thing. See Maps/README.md, "Holes and caves".
/// </summary>
public static class TerrainVoidGen
{
    /// <summary>Default cave tunnel width, height and depth below the surface (metres).</summary>
    public const double DefaultWidth = 5, DefaultHeight = 3.5, DefaultDepth = 6;

    /// <summary>Default depth of a hole with nothing under it (a pit).</summary>
    public const double DefaultHoleDepth = 4;

    public static bool IsVoidFeature(string type) => type is "hole" or "cave";

    /// <summary>The voids for a finished heightfield, or null when the map has no hole or cave features.</summary>
    public static TerrainVoids? Build(IReadOnlyList<TerrainFeature> features, double[] h, int nx, int nz, double cell, double ox, double oz)
    {
        if (!features.Any(f => IsVoidFeature(f.Type)))
            return null;
        var n = nx * nz;
        var cx = nx - 1;
        var cz = nz - 1;
        var floor = new double[n];
        var ceil = new double[n];
        Array.Fill(floor, double.NaN);
        Array.Fill(ceil, double.NaN);
        var forced = new bool[cx * cz];
        var nav = new List<Vector3>();
        var g = new Ctx(h, nx, nz, cell, ox, oz);
        foreach (var f in features)
        {
            if (f.Type == "cave")
                CaveOp(g, f, floor, ceil, nav);
            else if (f.Type == "hole")
                HoleOp(g, f, floor, ceil, forced);
        }

        // Floors never stand above the surface (a cave mouth on a slope).
        for (var k = 0; k < n; k++)
            if (!double.IsNaN(floor[k]))
                floor[k] = Math.Min(floor[k], h[k]);

        // Cell kinds: holes the "hole" features cut, then wherever a cave's ceiling comes within a roof's thickness of the
        // surface (a cave mouth), then roofed caves.
        var kinds = new byte[cx * cz];
        for (var j = 0; j < cz; j++)
        for (var i = 0; i < cx; i++)
        {
            var c = j * cx + i;
            if (forced[c])
            {
                kinds[c] = TerrainVoids.Open;
                continue;
            }
            int k00 = j * nx + i, k10 = k00 + 1, k01 = k00 + nx, k11 = k01 + 1;
            if (double.IsNaN(floor[k00] + floor[k10] + floor[k01] + floor[k11] + ceil[k00] + ceil[k10] + ceil[k01] + ceil[k11]))
                continue;
            var fc = (floor[k00] + floor[k10] + floor[k01] + floor[k11]) * 0.25;
            var cc = (ceil[k00] + ceil[k10] + ceil[k01] + ceil[k11]) * 0.25;
            var hc = (h[k00] + h[k10] + h[k01] + h[k11]) * 0.25;
            if (cc - fc <= 0.25)
                continue;
            kinds[c] = cc >= hc - TerrainVoids.RoofMin ? TerrainVoids.Open : TerrainVoids.Cave;
        }

        // Every corner of a hollow cell needs a floor (holes cut right at a shape's edge borrow their neighbours').
        for (var j = 0; j < cz; j++)
        for (var i = 0; i < cx; i++)
        {
            if (kinds[j * cx + i] == TerrainVoids.Solid)
                continue;
            int k00 = j * nx + i;
            Span<int> ks = [k00, k00 + 1, k00 + nx, k00 + nx + 1];
            double sum = 0;
            var have = 0;
            foreach (var k in ks)
                if (!double.IsNaN(floor[k]))
                {
                    sum += floor[k];
                    have++;
                }
            foreach (var k in ks)
            {
                if (double.IsNaN(floor[k]))
                    floor[k] = have > 0 ? Math.Min(sum / have, h[k]) : h[k] - DefaultHoleDepth;
                if (double.IsNaN(ceil[k]))
                    ceil[k] = floor[k];
            }
        }

        var floorF = new float[n];
        var ceilF = new float[n];
        for (var k = 0; k < n; k++)
        {
            floorF[k] = double.IsNaN(floor[k]) ? float.NaN : (float)(Math.Round(floor[k] / TerrainGen.Quantum) * TerrainGen.Quantum);
            var c = Math.Min(ceil[k], h[k] - TerrainVoids.RoofMin);
            ceilF[k] = double.IsNaN(ceil[k]) ? float.NaN : (float)(Math.Round(c / TerrainGen.Quantum) * TerrainGen.Quantum);
        }
        var light = SkyLight(kinds, floorF, h, nx, nz, cell);
        return new TerrainVoids(kinds, floorF, ceilF, light, nav.ToArray());
    }

    private sealed class Ctx(double[] h, int nx, int nz, double cell, double ox, double oz)
    {
        public readonly double[] H = h;
        public readonly int Nx = nx, Nz = nz;
        public readonly double Cell = cell, Ox = ox, Oz = oz;

        public double X(int i) => Ox + i * Cell;
        public double Z(int j) => Oz + j * Cell;

        public double Sample(double x, double z)
        {
            var fx = Math.Clamp((x - Ox) / Cell, 0, Nx - 1);
            var fz = Math.Clamp((z - Oz) / Cell, 0, Nz - 1);
            var i = Math.Min((int)Math.Floor(fx), Nx - 2);
            var j = Math.Min((int)Math.Floor(fz), Nz - 2);
            var tx = fx - i;
            var tz = fz - j;
            var k = j * Nx + i;
            return (H[k] * (1 - tx) + H[k + 1] * tx) * (1 - tz) + (H[k + Nx] * (1 - tx) + H[k + Nx + 1] * tx) * tz;
        }

        public (int I0, int I1, int J0, int J1) Range(double minX, double maxX, double minZ, double maxZ) =>
            (Math.Max(0, (int)Math.Ceiling((minX - Ox) / Cell)), Math.Min(Nx - 1, (int)Math.Floor((maxX - Ox) / Cell)),
             Math.Max(0, (int)Math.Ceiling((minZ - Oz) / Cell)), Math.Min(Nz - 1, (int)Math.Floor((maxZ - Oz) / Cell)));
    }

    /// <summary>Joins a new hollow (floor f, ceiling c; hollow where c &gt; f) into sample k: hollows join (lowest floor,
    /// highest ceiling); otherwise the closer-to-hollow values are kept, so the edge cells of a cave interpolate sensibly.</summary>
    private static void Merge(double[] floor, double[] ceil, int k, double f, double c)
    {
        var oldF = floor[k];
        var oldC = ceil[k];
        if (double.IsNaN(oldF) || double.IsNaN(oldC))
        {
            floor[k] = f;
            ceil[k] = c;
            return;
        }
        var hasNew = c > f;
        var hasOld = oldC > oldF;
        if (hasNew && hasOld)
        {
            floor[k] = Math.Min(oldF, f);
            ceil[k] = Math.Max(oldC, c);
        }
        else if (hasNew || (!hasOld && c - f > oldC - oldF))
        {
            floor[k] = f;
            ceil[k] = c;
        }
    }

    /// <summary>
    /// "cave": a tunnel along "points" ([x, z] or [x, z, floor height]), "width" wide and "height" tall (an arch: flat floor
    /// in the middle, curving up into the walls). Points without a height sit "depth" under the surface, except the ends
    /// named by "open" ("both" by default), which sit on the surface: the tunnel ramps up and breaks out there as a hole.
    /// One point makes a round chamber "width" across.
    /// </summary>
    private static void CaveOp(Ctx g, TerrainFeature f, double[] floor, double[] ceil, List<Vector3> nav)
    {
        var src = f.Points;
        var w = f.Num("width", DefaultWidth);
        var height = f.Num("height", DefaultHeight);
        var depth = f.Num("depth", DefaultDepth);
        var open = src.Length < 2 ? "none" : f.Str("open", "both");
        var pts = new double[src.Length][];
        for (var p = 0; p < src.Length; p++)
        {
            double x = src[p][0], z = src[p][1];
            var end = (p == 0 && open is "both" or "start") || (p == src.Length - 1 && open is "both" or "end");
            var y = src[p].Length > 2 ? src[p][2] : end ? g.Sample(x, z) : g.Sample(x, z) - depth;
            pts[p] = [x, z, y];
        }
        var half = w / 2;
        var reach = half * 1.5;
        var pad = reach + 2 * g.Cell;
        double minX = pts.Min(p => p[0]) - pad, maxX = pts.Max(p => p[0]) + pad, minZ = pts.Min(p => p[1]) - pad, maxZ = pts.Max(p => p[1]) + pad;
        var (i0, i1, j0, j1) = g.Range(minX, maxX, minZ, maxZ);
        for (var j = j0; j <= j1; j++)
        for (var i = i0; i <= i1; i++)
        {
            var (seg, t, d2) = Closest(pts, g.X(i), g.Z(j));
            var u = Math.Sqrt(d2) / half;
            if (u > 1.5)
                continue;
            var a = pts[seg];
            var b = pts[Math.Min(seg + 1, pts.Length - 1)];
            var f0 = a[2] + (b[2] - a[2]) * t;
            var u2 = u * u;
            var c = f0 + height * (1 - u2 * u2); // the arch: flat-ish roof, steep sides, carries on below the floor past the edge
            var fl = f0 + height * 0.4 * TerrainGen.Smoothstep((u - 0.6) / 0.5); // the floor curves up into the walls
            Merge(floor, ceil, j * g.Nx + i, fl, c);
        }

        // Nav points down the middle, about every 2 m (and each end).
        for (var p = 0; p < pts.Length; p++)
        {
            if (p == pts.Length - 1)
            {
                nav.Add(new Vector3((float)pts[p][0], (float)pts[p][2], (float)pts[p][1]));
                break;
            }
            var a = pts[p];
            var b = pts[p + 1];
            var len = Math.Sqrt((b[0] - a[0]) * (b[0] - a[0]) + (b[1] - a[1]) * (b[1] - a[1]));
            var steps = Math.Max(1, (int)Math.Ceiling(len / 2));
            for (var s = 0; s < steps; s++)
            {
                var t = s / (double)steps;
                nav.Add(new Vector3((float)(a[0] + (b[0] - a[0]) * t), (float)(a[2] + (b[2] - a[2]) * t), (float)(a[1] + (b[1] - a[1]) * t)));
            }
        }
    }

    /// <summary>
    /// "hole": cuts the surface away inside a "circle" (x, z, radius), "ellipse" (x, z, radius, radiusZ, angle in degrees)
    /// or "polygon" (points). Over a cave it opens into the cave; elsewhere it's a pit "depth" deep (or down to an absolute
    /// "floor" height).
    /// </summary>
    private static void HoleOp(Ctx g, TerrainFeature f, double[] floor, double[] ceil, bool[] forced)
    {
        var shape = f.Str("shape", "circle");
        double x = f.Num("x", 0), z = f.Num("z", 0), r = f.Num("radius", 1), rz = f.Num("radiusZ", r);
        var ang = f.Num("angle", 0) * Math.PI / 180.0;
        double ca = Math.Cos(ang), sa = Math.Sin(ang);
        var poly = f.Points;
        var depth = f.Num("depth", DefaultHoleDepth);
        double? floorAt = f.Has("floor") ? f.Num("floor", 0) : null;

        // Signed distance-ish: <= 0 inside the shape, roughly metres outside it.
        double Outside(double px, double pz)
        {
            switch (shape)
            {
                case "polygon":
                {
                    var inside = false;
                    var best = double.MaxValue;
                    for (int a = 0, b = poly.Length - 1; a < poly.Length; b = a++)
                    {
                        double ax = poly[a][0], az = poly[a][1], bx = poly[b][0], bz = poly[b][1];
                        if ((az > pz) != (bz > pz) && px < (bx - ax) * (pz - az) / (bz - az) + ax)
                            inside = !inside;
                        double ex = bx - ax, ez = bz - az;
                        var t = Math.Clamp(((px - ax) * ex + (pz - az) * ez) / (ex * ex + ez * ez), 0, 1);
                        double qx = ax + ex * t - px, qz = az + ez * t - pz;
                        best = Math.Min(best, qx * qx + qz * qz);
                    }
                    return inside ? -Math.Sqrt(best) : Math.Sqrt(best);
                }
                case "ellipse":
                {
                    double dx = px - x, dz = pz - z;
                    double lx = dx * ca + dz * sa, lz = -dx * sa + dz * ca;
                    var e = Math.Sqrt(lx / r * (lx / r) + lz / rz * (lz / rz));
                    return (e - 1) * Math.Min(r, rz);
                }
                default:
                    return Math.Sqrt((px - x) * (px - x) + (pz - z) * (pz - z)) - r;
            }
        }

        double minX, maxX, minZ, maxZ;
        if (shape == "polygon")
            (minX, maxX, minZ, maxZ) = (poly.Min(p => p[0]), poly.Max(p => p[0]), poly.Min(p => p[1]), poly.Max(p => p[1]));
        else
        {
            var big = Math.Max(r, rz);
            (minX, maxX, minZ, maxZ) = (x - big, x + big, z - big, z + big);
        }
        var margin = 1.5 * g.Cell;
        var (i0, i1, j0, j1) = g.Range(minX - margin - g.Cell, maxX + margin + g.Cell, minZ - margin - g.Cell, maxZ + margin + g.Cell);
        for (var j = j0; j <= j1; j++)
        for (var i = i0; i <= i1; i++)
        {
            var o = Outside(g.X(i), g.Z(j));
            if (o > margin)
                continue;
            var k = j * g.Nx + i;
            var hollow = !double.IsNaN(floor[k]) && !double.IsNaN(ceil[k]) && ceil[k] > floor[k];
            if (!hollow)
            {
                floor[k] = floorAt ?? g.H[k] - depth; // a pit (or the floor its edge cells interpolate towards)
                if (o > 0)
                    continue;
            }
            if (o <= 0)
                ceil[k] = g.H[k] + 100; // open to the sky here (over a cave: down to the cave's floor)
        }
        // Cells whose middle is inside the shape are holes.
        for (var j = Math.Max(0, j0 - 1); j <= Math.Min(g.Nz - 2, j1); j++)
        for (var i = Math.Max(0, i0 - 1); i <= Math.Min(g.Nx - 2, i1); i++)
            if (Outside(g.X(i) + g.Cell / 2, g.Z(j) + g.Cell / 2) <= 0)
                forced[j * (g.Nx - 1) + i] = true;
    }

    /// <summary>Closest point on a polyline (ends clamped; a single point is a circle round it).</summary>
    private static (int Seg, double T, double Dist2) Closest(double[][] pts, double px, double pz)
    {
        if (pts.Length == 1)
            return (0, 0, (px - pts[0][0]) * (px - pts[0][0]) + (pz - pts[0][1]) * (pz - pts[0][1]));
        var best = (Seg: 0, T: 0.0, Dist2: double.MaxValue);
        for (var k = 0; k < pts.Length - 1; k++)
        {
            double ax = pts[k][0], az = pts[k][1];
            double abx = pts[k + 1][0] - ax, abz = pts[k + 1][1] - az;
            var t = Math.Clamp(((px - ax) * abx + (pz - az) * abz) / (abx * abx + abz * abz), 0, 1);
            double qx = ax + abx * t - px, qz = az + abz * t - pz;
            var d2 = qx * qx + qz * qz;
            if (d2 < best.Dist2)
                best = (k, t, d2);
        }
        return best;
    }

    /// <summary>
    /// Daylight on each floor sample: open holes get the sky (a little less at the bottom of a deep pit); under a roof it
    /// fades with the walking distance (through hollow cells) from the nearest opening, so caves get darker inside.
    /// </summary>
    private static float[] SkyLight(byte[] kinds, float[] floor, double[] h, int nx, int nz, double cell)
    {
        int cx = nx - 1, cz = nz - 1;
        var dist = new int[kinds.Length];
        Array.Fill(dist, int.MaxValue);
        var queue = new Queue<int>();
        for (var c = 0; c < kinds.Length; c++)
            if (kinds[c] == TerrainVoids.Open)
            {
                dist[c] = 0;
                queue.Enqueue(c);
            }
        while (queue.Count > 0)
        {
            var c = queue.Dequeue();
            int i = c % cx, j = c / cx;
            Span<int> nb = [i > 0 ? c - 1 : -1, i < cx - 1 ? c + 1 : -1, j > 0 ? c - cx : -1, j < cz - 1 ? c + cx : -1];
            foreach (var m in nb)
                if (m >= 0 && kinds[m] == TerrainVoids.Cave && dist[m] == int.MaxValue)
                {
                    dist[m] = dist[c] + 1;
                    queue.Enqueue(m);
                }
        }
        var light = new float[nx * nz];
        for (var j = 0; j < nz; j++)
        for (var i = 0; i < nx; i++)
        {
            var best = int.MaxValue;
            for (var dj = -1; dj <= 0; dj++)
            for (var di = -1; di <= 0; di++)
            {
                int ci = i + di, cj = j + dj;
                if (ci < 0 || cj < 0 || ci >= cx || cj >= cz)
                    continue;
                var c = cj * cx + ci;
                if (kinds[c] != TerrainVoids.Solid)
                    best = Math.Min(best, dist[c]);
            }
            var k = j * nx + i;
            if (best == int.MaxValue)
                light[k] = 1f;
            else if (best == 0)
            {
                var deep = float.IsFinite(floor[k]) ? (float)h[k] - floor[k] : 0f;
                light[k] = 1f - 0.4f * Math.Clamp(deep / 12f, 0f, 1f);
            }
            else
                light[k] = 0.18f + 0.6f * MathF.Exp(-(float)(best * cell) / 4f);
        }
        return light;
    }
}
