using System.Numerics;
using System.Runtime.InteropServices;

namespace CallOfSlop;

/// <summary>
/// A map's baked light (docs/render/LIGHTING.md): a 3D grid over the map, one cell every <see cref="Cell"/> metres,
/// uploaded as three small 3D textures and sampled per pixel by every scene shader (<see cref="Lighting.Glsl"/>), so
/// map boxes, props, buildings, terrain, players, zombies, guns and gibs are all lit by the same light at no per-light
/// cost. Built once by <see cref="LightBaker"/> when a lit map is validated, and kept in the map's bake (MapBake.cs).
/// Cells are x fastest, then y, then z (the GL texture layout); cell (i, j, k)'s centre is
/// <see cref="Origin"/> + (i + 0.5, j + 0.5, k + 0.5) * <see cref="Cell"/>.
/// </summary>
public sealed class LightBake
{
    public Vector3 Origin;
    public float Cell;
    public int NX, NY, NZ;

    /// <summary>4 half floats a cell: the static lights' colour reaching it (r, g, b, as seen by a surface facing them) and the share of it that flickers.</summary>
    public ushort[] Light = [];

    /// <summary>4 signed bytes a cell (snorm): x, y, z the lights' average direction (its length: how one-sided the light is), w the ambient left (sky visibility).</summary>
    public sbyte[] Dir = [];

    /// <summary>2 bytes a cell: how much of the sun reaches it (0-255) and the flicker phase of the brightest flickering light.</summary>
    public byte[] Sun = [];

    /// <summary>The lights baked (LightSource ops, then props' lights), in world space.</summary>
    public MapLight[] Sources = [];

    public LightingSpec Spec;

    /// <summary>The sun direction the bake shaded (the map's key light).</summary>
    public Vector3 SunDir;

    /// <summary>FNV-1a 64 over the grid (logs and the self-test's determinism checks).</summary>
    public ulong Hash;

    /// <summary>Wall-clock ms the bake took (not baked: it differs every time).</summary>
    [NotBaked]
    public double BakeMs;

    public int Cells => NX * NY * NZ;

    /// <summary>Bytes the grid takes (in memory, in the map's bake and on the GPU alike).</summary>
    public long Bytes => Light.Length * 2L + Dir.Length + Sun.Length;

    /// <summary>The grid's far corner.</summary>
    public Vector3 Max => Origin + new Vector3(NX, NY, NZ) * Cell;

    /// <summary>
    /// The grid at <paramref name="p"/>, filtered like the GPU does (trilinear between cell centres, clamped at the edges;
    /// outside the grid: no static light, full sun and ambient): light (rgb, flicker share), direction + ambient, sun + phase.
    /// No allocation (the viewmodel's probe samples this every frame, see LightGrid).
    /// </summary>
    public void Sample(Vector3 p, out Vector4 light, out Vector4 dir, out Vector2 sun)
    {
        var g = (p - Origin) / Cell - new Vector3(0.5f);
        if (p.X < Origin.X || p.Y < Origin.Y || p.Z < Origin.Z || p.X > Origin.X + NX * Cell || p.Y > Origin.Y + NY * Cell || p.Z > Origin.Z + NZ * Cell)
        {
            light = Vector4.Zero;
            dir = new Vector4(0, 0, 0, 1);
            sun = new Vector2(1, 0);
            return;
        }
        int x0 = Math.Clamp((int)MathF.Floor(g.X), 0, NX - 1), y0 = Math.Clamp((int)MathF.Floor(g.Y), 0, NY - 1), z0 = Math.Clamp((int)MathF.Floor(g.Z), 0, NZ - 1);
        int x1 = Math.Min(x0 + 1, NX - 1), y1 = Math.Min(y0 + 1, NY - 1), z1 = Math.Min(z0 + 1, NZ - 1);
        float fx = Math.Clamp(g.X - x0, 0, 1), fy = Math.Clamp(g.Y - y0, 0, 1), fz = Math.Clamp(g.Z - z0, 0, 1);
        light = Vector4.Zero;
        dir = Vector4.Zero;
        sun = Vector2.Zero;
        for (var c = 0; c < 8; c++)
        {
            int x = (c & 1) == 0 ? x0 : x1, y = (c & 2) == 0 ? y0 : y1, z = (c & 4) == 0 ? z0 : z1;
            var w = ((c & 1) == 0 ? 1 - fx : fx) * ((c & 2) == 0 ? 1 - fy : fy) * ((c & 4) == 0 ? 1 - fz : fz);
            var i = (z * NY + y) * NX + x;
            light += w * new Vector4((float)BitConverter.UInt16BitsToHalf(Light[i * 4]), (float)BitConverter.UInt16BitsToHalf(Light[i * 4 + 1]),
                (float)BitConverter.UInt16BitsToHalf(Light[i * 4 + 2]), (float)BitConverter.UInt16BitsToHalf(Light[i * 4 + 3]));
            dir += w * new Vector4(Snorm(Dir[i * 4]), Snorm(Dir[i * 4 + 1]), Snorm(Dir[i * 4 + 2]), Snorm(Dir[i * 4 + 3]));
            sun += w * new Vector2(Sun[i * 2] / 255f, Sun[i * 2 + 1] / 255f);
        }
    }

    private static float Snorm(sbyte b) => MathF.Max(b / 127f, -1f);

    /// <summary>
    /// C# mirror of <see cref="Lighting.Glsl"/>'s static-light term for a surface with normal <paramref name="n"/>: the baked
    /// colour times how much it faces the light's average direction (no flicker).
    /// </summary>
    public static Vector3 Shade(Vector4 light, Vector4 dir, Vector3 n)
    {
        var d = new Vector3(dir.X, dir.Y, dir.Z);
        var facing = (1f - MathF.Min(d.Length(), 1f)) * 0.6f + MathF.Max(Vector3.Dot(n, d), 0f);
        return new Vector3(light.X, light.Y, light.Z) * facing;
    }
}

/// <summary>
/// Bakes a lit map's light grid (docs/render/LIGHTING.md): for every cell, each static light within range (point, spot
/// or area, smooth falloff, shadowed by the map's static boxes), how much of the sun reaches it (one shadow ray), and how
/// much sky it sees (16 rays over the upper hemisphere on a half-resolution grid, as ambient occlusion). Deterministic:
/// each cell depends only on the map, cells are independent and summed in a fixed order, so threads change nothing.
/// Shadows come from <see cref="Map.Solid"/> without the boxes that come and go (doors, windows, Zombies barriers, map
/// logic boxes, clip boxes); destroyed boxes keep the light they had (no re-light at run time).
/// </summary>
public static class LightBaker
{
    /// <summary>The most cells a grid has (about 14 MB); bigger maps get coarser cells.</summary>
    public const int MaxCells = 1 << 20;

    /// <summary>Sky visibility: rays per cell and how far one must get to count as seeing the sky.</summary>
    public const int SkyRays = 16;

    public const float SkyReach = 24f;

    /// <summary>Run the bake on worker threads (the self-test checks the result is the same either way).</summary>
    public static bool Parallel = true;

    private static readonly Vector3[] SkyDirs = MakeSkyDirs();

    private static Vector3[] MakeSkyDirs()
    {
        // Cosine-weighted directions over the upper hemisphere (a Fibonacci spiral): equal weights give the irradiance-style average.
        var dirs = new Vector3[SkyRays];
        for (var k = 0; k < SkyRays; k++)
        {
            var u = (k + 0.5f) / SkyRays;
            var r = MathF.Sqrt(u);
            var phi = k * 2.39996323f;
            dirs[k] = Vector3.Normalize(new Vector3(r * MathF.Cos(phi), MathF.Sqrt(1f - u), r * MathF.Sin(phi)));
        }
        return dirs;
    }

    public static LightBake Bake(Map m)
    {
        var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        var spec = m.LightingSettings ?? new LightingSpec();
        var lights = m.BakeLights().ToArray();
        var sunDir = m.Light.SunDir;
        var occ = new Occluders(m);

        // ---- The grid: the playable area, from below the floor to above the tallest box or light ----------------
        var yMin = (m.Terrain is { } terr ? terr.MinHeight : 0f) - 0.5f;
        var yMax = yMin + 4f;
        foreach (var b in occ.Boxes)
            if (b.Max.Y < yMin + 96f)
                yMax = MathF.Max(yMax, b.Max.Y + 1.5f);
        foreach (var l in lights)
            yMax = MathF.Max(yMax, l.Pos.Y + 1f);
        yMax = MathF.Min(yMax, yMin + 96f);
        var min = new Vector3(-m.HalfX - 1f, yMin, -m.HalfZ - 1f);
        var size = new Vector3(m.HalfX * 2 + 2f, yMax - yMin, m.HalfZ * 2 + 2f);
        var cell = spec.Cell;
        int nx, ny, nz;
        while (true)
        {
            nx = Math.Max(2, (int)MathF.Ceiling(size.X / cell));
            ny = Math.Max(2, (int)MathF.Ceiling(size.Y / cell));
            nz = Math.Max(2, (int)MathF.Ceiling(size.Z / cell));
            if ((long)nx * ny * nz <= MaxCells && nx <= 2048 && ny <= 2048 && nz <= 2048)
                break;
            cell *= 1.25f;
        }
        var bake = new LightBake
        {
            Origin = min, Cell = cell, NX = nx, NY = ny, NZ = nz, Spec = spec, Sources = lights, SunDir = sunDir,
            Light = new ushort[nx * ny * nz * 4], Dir = new sbyte[nx * ny * nz * 4], Sun = new byte[nx * ny * nz * 2],
        };
        occ.Index(min - new Vector3(2f), bake.Max + new Vector3(2f));
        var bins = new LightBins(lights, min, bake.Max);

        // ---- Sky visibility on a half-resolution grid, then the cells ------------------------------------------
        int cx = (nx + 1) / 2, cy = (ny + 1) / 2, cz = (nz + 1) / 2;
        var sky = new float[cx * cy * cz];
        For(cz, () => new Tracer(occ, m), (k, tr) =>
        {
            for (var j = 0; j < cy; j++)
            for (var i = 0; i < cx; i++)
            {
                var p = min + new Vector3(2 * i + 1, 2 * j + 1, 2 * k + 1) * cell;
                if (!tr.PushOut(ref p))
                {
                    sky[(k * cy + j) * cx + i] = 0f;
                    continue;
                }
                var seen = 0;
                foreach (var d in SkyDirs)
                    if (!tr.Blocked(p, d, SkyReach, default, false))
                        seen++;
                sky[(k * cy + j) * cx + i] = seen / (float)SkyRays;
            }
        });
        var solid = new bool[nx * ny * nz];
        For(nz, () => new Tracer(occ, m), (k, tr) =>
        {
            for (var j = 0; j < ny; j++)
            for (var i = 0; i < nx; i++)
            {
                var idx = (k * ny + j) * nx + i;
                var p = min + new Vector3(i + 0.5f, j + 0.5f, k + 0.5f) * cell;
                if (!tr.PushOut(ref p))
                {
                    solid[idx] = true;
                    continue;
                }
                Cell(bake, idx, p, tr, lights, bins, spec, sunDir, SampleSky(sky, cx, cy, cz, (i - 0.5f) * 0.5f, (j - 0.5f) * 0.5f, (k - 0.5f) * 0.5f));
            }
        });
        FillSolid(bake, solid);
        bake.Hash = HashOf(bake);
        bake.BakeMs = System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
        return bake;
    }

    private static void For(int count, Func<Tracer> make, Action<int, Tracer> body)
    {
        if (!Parallel || count < 4)
        {
            var tr = make();
            for (var k = 0; k < count; k++)
                body(k, tr);
            return;
        }
        System.Threading.Tasks.Parallel.For(0, count, make, (k, _, tr) =>
        {
            body(k, tr);
            return tr;
        }, _ => { });
    }

    private static float SampleSky(float[] sky, int cx, int cy, int cz, float x, float y, float z)
    {
        x = Math.Clamp(x, 0, cx - 1);
        y = Math.Clamp(y, 0, cy - 1);
        z = Math.Clamp(z, 0, cz - 1);
        int x0 = (int)x, y0 = (int)y, z0 = (int)z;
        int x1 = Math.Min(x0 + 1, cx - 1), y1 = Math.Min(y0 + 1, cy - 1), z1 = Math.Min(z0 + 1, cz - 1);
        float fx = x - x0, fy = y - y0, fz = z - z0;
        float S(int i, int j, int k) => sky[(k * cy + j) * cx + i];
        var a = S(x0, y0, z0) + (S(x1, y0, z0) - S(x0, y0, z0)) * fx;
        var b = S(x0, y1, z0) + (S(x1, y1, z0) - S(x0, y1, z0)) * fx;
        var c = S(x0, y0, z1) + (S(x1, y0, z1) - S(x0, y0, z1)) * fx;
        var d = S(x0, y1, z1) + (S(x1, y1, z1) - S(x0, y1, z1)) * fx;
        var e = a + (b - a) * fy;
        var f = c + (d - c) * fy;
        return e + (f - e) * fz;
    }

    /// <summary>Luminance weights for "how bright" (direction and flicker shares are weighted by it).</summary>
    private static readonly Vector3 Lum = new(0.2126f, 0.7152f, 0.0722f);

    /// <summary>The smooth falloff: 1 at the light, 0 at <paramref name="range"/> (<c>(1 - (d/r)^2)^2</c>).</summary>
    public static float Falloff(float dist, float range)
    {
        if (dist >= range)
            return 0f;
        var q = dist / range;
        var a = 1f - q * q;
        return a * a;
    }

    private static void Cell(LightBake bake, int idx, Vector3 p, Tracer tr, MapLight[] lights, LightBins bins, LightingSpec spec, Vector3 sunDir, float skyVis)
    {
        var sum = Vector3.Zero;
        var dirSum = Vector3.Zero;
        float lumSum = 0, flickLum = 0, bestFlick = 0;
        var bestFlickIndex = -1;
        foreach (var li in bins.At(p))
        {
            ref readonly var l = ref lights[li];
            var lit = Vector3.Zero;
            var ldir = Vector3.Zero;
            if (l.Type == MapLightType.Area)
            {
                var n = Math.Clamp((int)MathF.Ceiling(MathF.Max(l.Size.X, l.Size.Y) / (2f * bake.Cell)), 1, 3);
                var up = MathF.Abs(l.Dir.Y) < 0.99f ? Vector3.UnitY : Vector3.UnitX;
                var t1 = Vector3.Normalize(Vector3.Cross(up, l.Dir));
                var t2 = Vector3.Cross(l.Dir, t1);
                var share = l.Intensity / (n * n);
                for (var a = 0; a < n; a++)
                for (var b = 0; b < n; b++)
                {
                    var sp = l.Pos + t1 * (((a + 0.5f) / n - 0.5f) * l.Size.X) + t2 * (((b + 0.5f) / n - 0.5f) * l.Size.Y);
                    var to = sp - p;
                    var dist = to.Length();
                    if (dist < 1e-4f || dist >= l.Range)
                        continue;
                    var dn = to / dist;
                    var emit = -Vector3.Dot(dn, l.Dir); // the glowing side faces the cell
                    if (emit <= 0f)
                        continue;
                    if (tr.Blocked(p, dn, dist - 0.02f, sp, true))
                        continue;
                    var e = l.Color * (share * Falloff(dist, l.Range) * emit);
                    lit += e;
                    ldir += dn * Vector3.Dot(e, Lum);
                }
            }
            else
            {
                var to = l.Pos - p;
                var dist = to.Length();
                if (dist >= l.Range)
                    continue;
                var dn = dist > 1e-4f ? to / dist : Vector3.UnitY;
                var s = 1f;
                if (l.Type == MapLightType.Spot)
                {
                    var c = -Vector3.Dot(dn, l.Dir);
                    if (c <= l.CosOuter)
                        continue;
                    var t = Math.Clamp((c - l.CosOuter) / MathF.Max(l.CosInner - l.CosOuter, 1e-4f), 0f, 1f);
                    s = t * t * (3f - 2f * t);
                }
                if (dist > 1e-4f && tr.Blocked(p, dn, dist - 0.02f, l.Pos, true))
                    continue;
                lit = l.Color * (l.Intensity * Falloff(dist, l.Range) * s);
                ldir = dn * Vector3.Dot(lit, Lum);
            }
            var lum = Vector3.Dot(lit, Lum);
            if (lum <= 0f)
                continue;
            sum += lit;
            dirSum += ldir;
            lumSum += lum;
            if (l.Flicker)
            {
                flickLum += lum;
                if (lum > bestFlick)
                {
                    bestFlick = lum;
                    bestFlickIndex = li;
                }
            }
        }
        var o = idx * 4;
        bake.Light[o] = Half(sum.X);
        bake.Light[o + 1] = Half(sum.Y);
        bake.Light[o + 2] = Half(sum.Z);
        bake.Light[o + 3] = Half(lumSum > 0 ? flickLum / lumSum : 0f);
        var dir = lumSum > 1e-6f ? dirSum / lumSum : Vector3.Zero;
        bake.Dir[o] = Snorm(dir.X);
        bake.Dir[o + 1] = Snorm(dir.Y);
        bake.Dir[o + 2] = Snorm(dir.Z);
        bake.Dir[o + 3] = Snorm(spec.Indoor + (1f - spec.Indoor) * skyVis);
        var sunVis = !spec.SunShadows || !tr.Blocked(p, sunDir, 400f, default, false) ? 1f : 0f;
        bake.Sun[idx * 2] = (byte)MathF.Round(sunVis * 255f);
        bake.Sun[idx * 2 + 1] = bestFlickIndex < 0 ? (byte)0 : (byte)(((uint)bestFlickIndex * 2654435761u) >> 24);
    }

    private static ushort Half(float v) => BitConverter.HalfToUInt16Bits((Half)Math.Clamp(v, 0f, 1000f));

    private static sbyte Snorm(float v) => (sbyte)MathF.Round(Math.Clamp(v, -1f, 1f) * 127f);

    /// <summary>Cells inside solid boxes take the average of their open neighbours, so filtering at a wall's face doesn't pull in black.</summary>
    private static void FillSolid(LightBake b, bool[] solid)
    {
        var light = (ushort[])b.Light.Clone();
        var dir = (sbyte[])b.Dir.Clone();
        var sun = (byte[])b.Sun.Clone();
        Span<int> nb = stackalloc int[6];
        for (var k = 0; k < b.NZ; k++)
        for (var j = 0; j < b.NY; j++)
        for (var i = 0; i < b.NX; i++)
        {
            var idx = (k * b.NY + j) * b.NX + i;
            if (!solid[idx])
                continue;
            var n = 0;
            if (i > 0) nb[n++] = idx - 1;
            if (i < b.NX - 1) nb[n++] = idx + 1;
            if (j > 0) nb[n++] = idx - b.NX;
            if (j < b.NY - 1) nb[n++] = idx + b.NX;
            if (k > 0) nb[n++] = idx - b.NX * b.NY;
            if (k < b.NZ - 1) nb[n++] = idx + b.NX * b.NY;
            var acc = Vector4.Zero;
            var accD = Vector4.Zero;
            var accS = Vector2.Zero;
            var open = 0;
            for (var q = 0; q < n; q++)
            {
                var c = nb[q];
                if (solid[c])
                    continue;
                open++;
                acc += new Vector4((float)BitConverter.UInt16BitsToHalf(light[c * 4]), (float)BitConverter.UInt16BitsToHalf(light[c * 4 + 1]),
                    (float)BitConverter.UInt16BitsToHalf(light[c * 4 + 2]), (float)BitConverter.UInt16BitsToHalf(light[c * 4 + 3]));
                accD += new Vector4(dir[c * 4], dir[c * 4 + 1], dir[c * 4 + 2], dir[c * 4 + 3]);
                accS += new Vector2(sun[c * 2], sun[c * 2 + 1]);
            }
            if (open == 0)
            {
                b.Dir[idx * 4 + 3] = Snorm(b.Spec.Indoor); // deep inside a wall: dark (never seen)
                continue;
            }
            acc /= open;
            accD /= open;
            accS /= open;
            b.Light[idx * 4] = Half(acc.X);
            b.Light[idx * 4 + 1] = Half(acc.Y);
            b.Light[idx * 4 + 2] = Half(acc.Z);
            b.Light[idx * 4 + 3] = Half(acc.W);
            b.Dir[idx * 4] = (sbyte)MathF.Round(accD.X);
            b.Dir[idx * 4 + 1] = (sbyte)MathF.Round(accD.Y);
            b.Dir[idx * 4 + 2] = (sbyte)MathF.Round(accD.Z);
            b.Dir[idx * 4 + 3] = (sbyte)MathF.Round(accD.W);
            b.Sun[idx * 2] = (byte)MathF.Round(accS.X);
            b.Sun[idx * 2 + 1] = (byte)MathF.Round(accS.Y);
        }
    }

    public static ulong HashOf(LightBake b)
    {
        var h = 14695981039346656037ul;
        void Mix(ReadOnlySpan<byte> bytes)
        {
            foreach (var x in bytes)
                h = (h ^ x) * 1099511628211ul;
        }
        Mix(MemoryMarshal.AsBytes(new ReadOnlySpan<int>([b.NX, b.NY, b.NZ])));
        Mix(MemoryMarshal.AsBytes(new ReadOnlySpan<float>([b.Origin.X, b.Origin.Y, b.Origin.Z, b.Cell])));
        Mix(MemoryMarshal.AsBytes(b.Light.AsSpan()));
        Mix(MemoryMarshal.AsBytes(b.Dir.AsSpan()));
        Mix(b.Sun);
        return h;
    }

    // ---- Lights by region -------------------------------------------------------------------------------------

    /// <summary>The lights filed into 4 m bins by the box round their range, so a cell only looks at lights that can reach it (in light order).</summary>
    private sealed class LightBins
    {
        private const float Size = 4f;
        private readonly Vector3 _min;
        private readonly int _nx, _ny, _nz;
        private readonly int[] _start;
        private readonly int[] _items;

        public LightBins(MapLight[] lights, Vector3 min, Vector3 max)
        {
            _min = min;
            _nx = Math.Max(1, (int)MathF.Ceiling((max.X - min.X) / Size));
            _ny = Math.Max(1, (int)MathF.Ceiling((max.Y - min.Y) / Size));
            _nz = Math.Max(1, (int)MathF.Ceiling((max.Z - min.Z) / Size));
            var lists = new List<int>?[_nx * _ny * _nz];
            for (var li = 0; li < lights.Length; li++)
            {
                var l = lights[li];
                var reach = l.Range + (l.Type == MapLightType.Area ? MathF.Max(l.Size.X, l.Size.Y) * 0.5f : 0f);
                Range(l.Pos - new Vector3(reach), l.Pos + new Vector3(reach), out var a, out var b);
                for (var k = a.Z; k <= b.Z; k++)
                for (var j = a.Y; j <= b.Y; j++)
                for (var i = a.X; i <= b.X; i++)
                    (lists[(k * _ny + j) * _nx + i] ??= new List<int>()).Add(li);
            }
            _start = new int[lists.Length + 1];
            var all = new List<int>();
            for (var c = 0; c < lists.Length; c++)
            {
                _start[c] = all.Count;
                if (lists[c] != null)
                    all.AddRange(lists[c]!);
            }
            _start[lists.Length] = all.Count;
            _items = all.ToArray();
        }

        private void Range(Vector3 lo, Vector3 hi, out (int X, int Y, int Z) a, out (int X, int Y, int Z) b)
        {
            a = (C(lo.X - _min.X, _nx), C(lo.Y - _min.Y, _ny), C(lo.Z - _min.Z, _nz));
            b = (C(hi.X - _min.X, _nx), C(hi.Y - _min.Y, _ny), C(hi.Z - _min.Z, _nz));
        }

        private static int C(float v, int n) => Math.Clamp((int)MathF.Floor(v / Size), 0, n - 1);

        public ReadOnlySpan<int> At(Vector3 p)
        {
            var c = (C(p.Z - _min.Z, _nz) * _ny + C(p.Y - _min.Y, _ny)) * _nx + C(p.X - _min.X, _nx);
            return _items.AsSpan(_start[c], _start[c + 1] - _start[c]);
        }
    }

    // ---- Occluders and rays -------------------------------------------------------------------------------------

    private readonly record struct AaBox(Vector3 Min, Vector3 Max);

    /// <summary>The static boxes that cast shadows, filed into a uniform 3D grid of box lists (built once per bake, then read only).</summary>
    private sealed class Occluders
    {
        public readonly List<AaBox> Boxes = new();
        public Vector3 Min, Max;
        public float G;
        public int NX, NY, NZ;
        public int[] Start = [];
        public int[] Items = [];

        public Occluders(Map m)
        {
            for (var i = 0; i < m.Solid.Count; i++)
            {
                if (m.IsBuildingDoorBox(i) || m.IsBuildingGlass(i) || m.IsZombieBox(i) || m.IsLogicBox(i) || m.IsClipBox(i))
                    continue; // (boxes that open, break away or are invisible: light passes)
                var b = m.Solid[i];
                if (b.Half.X <= 0 || b.Half.Y <= 0 || b.Half.Z <= 0 || b.Center.Y < -500f)
                    continue;
                Boxes.Add(new AaBox(b.Min, b.Max));
            }
        }

        public void Index(Vector3 min, Vector3 max)
        {
            Min = min;
            Max = max;
            var size = max - min;
            G = MathF.Max(2f, MathF.Max(size.X, MathF.Max(size.Y, size.Z)) / 96f);
            NX = Math.Max(1, (int)MathF.Ceiling(size.X / G));
            NY = Math.Max(1, (int)MathF.Ceiling(size.Y / G));
            NZ = Math.Max(1, (int)MathF.Ceiling(size.Z / G));
            var counts = new int[NX * NY * NZ + 1];
            for (var pass = 0; pass < 2; pass++)
            {
                if (pass == 1)
                {
                    for (var c = 1; c < counts.Length; c++)
                        counts[c] += counts[c - 1];
                    Start = new int[counts.Length];
                    Array.Copy(counts, 0, Start, 1, counts.Length - 1);
                    Items = new int[counts[^1]];
                    Array.Copy(Start, counts, counts.Length); // (reused as the write cursors)
                }
                for (var bi = 0; bi < Boxes.Count; bi++)
                {
                    var b = Boxes[bi];
                    if (b.Max.X < min.X || b.Max.Y < min.Y || b.Max.Z < min.Z || b.Min.X > max.X || b.Min.Y > max.Y || b.Min.Z > max.Z)
                        continue;
                    int x0 = Cl(b.Min.X - min.X, NX), x1 = Cl(b.Max.X - min.X, NX), y0 = Cl(b.Min.Y - min.Y, NY), y1 = Cl(b.Max.Y - min.Y, NY);
                    int z0 = Cl(b.Min.Z - min.Z, NZ), z1 = Cl(b.Max.Z - min.Z, NZ);
                    for (var z = z0; z <= z1; z++)
                    for (var y = y0; y <= y1; y++)
                    for (var x = x0; x <= x1; x++)
                    {
                        var c = (z * NY + y) * NX + x;
                        if (pass == 0)
                            counts[c + 1]++;
                        else
                            Items[counts[c]++] = bi;
                    }
                }
            }
        }

        public int Cl(float v, int n) => Math.Clamp((int)MathF.Floor(v / G), 0, n - 1);
    }

    /// <summary>One worker's ray caster: shadow rays through the occluder grid (3D DDA), each box tested once per ray.</summary>
    private sealed class Tracer
    {
        private readonly Occluders _o;
        private readonly AaBox[] _boxes;
        private readonly int[] _stamp;
        private int _ray;
        private readonly Map? _terrain;

        public Tracer(Occluders o, Map m)
        {
            _o = o;
            _boxes = o.Boxes.ToArray();
            _stamp = new int[_boxes.Length];
            _terrain = m.HasTerrain ? m : null;
        }

        /// <summary>Moves <paramref name="p"/> out of any box it is inside (to the nearest face, a little past it). False: still inside after a few tries.</summary>
        public bool PushOut(ref Vector3 p)
        {
            for (var tries = 0; tries < 4; tries++)
            {
                var moved = false;
                if (p.X >= _o.Min.X && p.Y >= _o.Min.Y && p.Z >= _o.Min.Z && p.X <= _o.Max.X && p.Y <= _o.Max.Y && p.Z <= _o.Max.Z)
                {
                    var c = (_o.Cl(p.Z - _o.Min.Z, _o.NZ) * _o.NY + _o.Cl(p.Y - _o.Min.Y, _o.NY)) * _o.NX + _o.Cl(p.X - _o.Min.X, _o.NX);
                    for (var q = _o.Start[c]; q < _o.Start[c + 1]; q++)
                    {
                        var b = _boxes[_o.Items[q]];
                        if (p.X <= b.Min.X || p.Y <= b.Min.Y || p.Z <= b.Min.Z || p.X >= b.Max.X || p.Y >= b.Max.Y || p.Z >= b.Max.Z)
                            continue;
                        // The nearest face (ties: the first in x-, x+, y-, y+, z-, z+ order).
                        Span<float> d = [p.X - b.Min.X, b.Max.X - p.X, p.Y - b.Min.Y, b.Max.Y - p.Y, p.Z - b.Min.Z, b.Max.Z - p.Z];
                        var best = 0;
                        for (var f = 1; f < 6; f++)
                            if (d[f] < d[best])
                                best = f;
                        const float past = 0.03f;
                        switch (best)
                        {
                            case 0: p.X = b.Min.X - past; break;
                            case 1: p.X = b.Max.X + past; break;
                            case 2: p.Y = b.Min.Y - past; break;
                            case 3: p.Y = b.Max.Y + past; break;
                            case 4: p.Z = b.Min.Z - past; break;
                            default: p.Z = b.Max.Z + past; break;
                        }
                        moved = true;
                        break;
                    }
                }
                if (_terrain != null && p.Y < _terrain.GroundHeight(p.X, p.Z))
                {
                    p.Y = _terrain.GroundHeight(p.X, p.Z) + 0.05f;
                    moved = true;
                }
                if (!moved)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Does a static box (or the terrain) block the ray from <paramref name="o"/> along unit <paramref name="d"/> before
        /// <paramref name="tMax"/>? With <paramref name="skipHolders"/>, boxes that hold <paramref name="light"/> (a bulb
        /// inside its lamp's collider) don't count.
        /// </summary>
        public bool Blocked(Vector3 o, Vector3 d, float tMax, Vector3 light, bool skipHolders)
        {
            if (tMax <= 0f)
                return false;
            if (++_ray == int.MaxValue)
            {
                Array.Clear(_stamp);
                _ray = 1;
            }
            var inv = new Vector3(1f / (d.X != 0 ? d.X : 1e-30f), 1f / (d.Y != 0 ? d.Y : 1e-30f), 1f / (d.Z != 0 ? d.Z : 1e-30f));
            // Clip to the grid.
            var t1 = (_o.Min - o) * inv;
            var t2 = (_o.Max - o) * inv;
            var tn = Vector3.Min(t1, t2);
            var tf = Vector3.Max(t1, t2);
            var tEnter = MathF.Max(MathF.Max(tn.X, tn.Y), MathF.Max(tn.Z, 0f));
            var tExit = MathF.Min(MathF.Min(tf.X, tf.Y), MathF.Min(tf.Z, tMax));
            if (tEnter <= tExit)
            {
                var p = o + d * tEnter;
                int x = _o.Cl(p.X - _o.Min.X, _o.NX), y = _o.Cl(p.Y - _o.Min.Y, _o.NY), z = _o.Cl(p.Z - _o.Min.Z, _o.NZ);
                int sx = d.X > 0 ? 1 : -1, sy = d.Y > 0 ? 1 : -1, sz = d.Z > 0 ? 1 : -1;
                var g = _o.G;
                float nextX = d.X == 0 ? float.MaxValue : ((_o.Min.X + (x + (sx > 0 ? 1 : 0)) * g) - o.X) * inv.X;
                float nextY = d.Y == 0 ? float.MaxValue : ((_o.Min.Y + (y + (sy > 0 ? 1 : 0)) * g) - o.Y) * inv.Y;
                float nextZ = d.Z == 0 ? float.MaxValue : ((_o.Min.Z + (z + (sz > 0 ? 1 : 0)) * g) - o.Z) * inv.Z;
                float dx = d.X == 0 ? float.MaxValue : g * MathF.Abs(inv.X), dy = d.Y == 0 ? float.MaxValue : g * MathF.Abs(inv.Y), dz = d.Z == 0 ? float.MaxValue : g * MathF.Abs(inv.Z);
                while (true)
                {
                    var c = (z * _o.NY + y) * _o.NX + x;
                    for (var q = _o.Start[c]; q < _o.Start[c + 1]; q++)
                    {
                        var bi = _o.Items[q];
                        if (_stamp[bi] == _ray)
                            continue;
                        _stamp[bi] = _ray;
                        var b = _boxes[bi];
                        var a1 = (b.Min - o) * inv;
                        var a2 = (b.Max - o) * inv;
                        var near = Vector3.Min(a1, a2);
                        var far = Vector3.Max(a1, a2);
                        var bn = MathF.Max(near.X, MathF.Max(near.Y, near.Z));
                        var bf = MathF.Min(far.X, MathF.Min(far.Y, far.Z));
                        if (bf <= MathF.Max(bn, 1e-4f) || bn >= tMax)
                            continue;
                        if (skipHolders && light.X >= b.Min.X - 0.02f && light.Y >= b.Min.Y - 0.02f && light.Z >= b.Min.Z - 0.02f
                            && light.X <= b.Max.X + 0.02f && light.Y <= b.Max.Y + 0.02f && light.Z <= b.Max.Z + 0.02f)
                            continue;
                        return true;
                    }
                    if (nextX <= nextY && nextX <= nextZ)
                    {
                        if (nextX > tExit)
                            break;
                        x += sx;
                        nextX += dx;
                        if ((uint)x >= (uint)_o.NX)
                            break;
                    }
                    else if (nextY <= nextZ)
                    {
                        if (nextY > tExit)
                            break;
                        y += sy;
                        nextY += dy;
                        if ((uint)y >= (uint)_o.NY)
                            break;
                    }
                    else
                    {
                        if (nextZ > tExit)
                            break;
                        z += sz;
                        nextZ += dz;
                        if ((uint)z >= (uint)_o.NZ)
                            break;
                    }
                }
            }
            if (_terrain != null)
            {
                // The ground: stepped along the ray (half-metre steps, then every metre past 8 m).
                var t = 0.25f;
                while (t < tMax && t < 160f)
                {
                    var p = o + d * t;
                    if (p.Y < _terrain.GroundHeight(p.X, p.Z) - 0.05f)
                        return true;
                    if (p.Y > 400f)
                        break;
                    t += t < 8f ? 0.5f : 1f;
                }
            }
            return false;
        }
    }
}
