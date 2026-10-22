using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace CallOfSlop;

/// <summary>
/// Paints every decal's picture into one RGBA8 image per decal (<see cref="LayerSize"/> square), stacked as the layers
/// of the decal atlas (a 2D texture array, so mipmaps never bleed between decals). Built once at startup from the
/// decal files' procedural layers (signed-distance shapes, anti-aliased, optional grunge noise) over an optional PNG.
/// Pure CPU work: the self-test checks it without a GPU.
///
/// <para>Pixels are straight (not premultiplied) alpha with the colour of fully transparent pixels filled in with the
/// decal's average colour, so mipmapped edges don't go dark. Row 0 is the bottom of the picture (v = 0).</para>
/// </summary>
public static class DecalAtlas
{
    /// <summary>Pixels per side of each decal's layer.</summary>
    public const int LayerSize = 128;

    /// <summary>
    /// The most layers the atlas holds: the global library (<see cref="Decals.MaxGlobal"/>) plus every loaded pack's decals
    /// (<see cref="Decals.MaxPerPack"/> each). GL 4.1 guarantees 2048 array texture layers. Only the layers in use are
    /// allocated (64 KB each plus a third for mipmaps), so the cap costs nothing by itself.
    /// </summary>
    public const int MaxLayers = 1024;

    public const int BytesPerLayer = LayerSize * LayerSize * 4;

    /// <summary>The atlas for <paramref name="defs"/> (at least one layer, so the texture is never empty).</summary>
    public static byte[] Build(IReadOnlyList<DecalDef> defs, List<string>? errors = null) => Build(defs, errors, null, null, out _);

    /// <summary>
    /// The atlas for <paramref name="defs"/>, reusing the layers of an earlier build: while <paramref name="previousDefs"/>
    /// matches <paramref name="defs"/> decal for decal (the same objects) those layers are copied from
    /// <paramref name="previousData"/>, and only the rest are painted (a pack's decals loading when one of its maps does).
    /// </summary>
    public static byte[] Build(IReadOnlyList<DecalDef> defs, List<string>? errors, IReadOnlyList<DecalDef>? previousDefs, byte[]? previousData, out int painted)
    {
        var layers = Math.Max(1, defs.Count);
        var data = new byte[layers * BytesPerLayer];
        var problems = new string?[defs.Count];
        var reuse = 0;
        if (previousDefs != null && previousData != null)
            while (reuse < defs.Count && reuse < previousDefs.Count && ReferenceEquals(defs[reuse], previousDefs[reuse]) && (reuse + 1) * BytesPerLayer <= previousData.Length)
                reuse++;
        if (reuse > 0)
            previousData!.AsSpan(0, reuse * BytesPerLayer).CopyTo(data);
        painted = defs.Count - reuse;
        // Decals are independent: paint them on all cores (each layer is the same whichever thread paints it).
        Parallel.For(reuse, defs.Count, () => new Vector4[LayerSize * LayerSize], (i, _, scratch) =>
        {
            try
            {
                Paint(defs[i], scratch, data.AsSpan(i * BytesPerLayer, BytesPerLayer));
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                problems[i] = ex.Message;
            }
            return scratch;
        }, _ => { });
        for (var i = reuse; i < defs.Count; i++)
        {
            if (problems[i] is not { } problem)
                continue;
            errors?.Add($"{defs[i].SourceFile}: image: {problem}");
            Console.WriteLine($"[decals] {defs[i].Name}: image not used ({problem})");
        }
        return data;
    }

    /// <summary>Paints one decal into <paramref name="rgba"/> (<see cref="BytesPerLayer"/> bytes). <paramref name="px"/> is scratch space.</summary>
    public static void Paint(DecalDef def, Vector4[] px, Span<byte> rgba)
    {
        Array.Clear(px);
        if (def.ImagePath != null)
            DrawImage(def.ImagePath, px);
        foreach (var s in def.Layers)
            DrawShape(s, px);

        // Fill the colour of empty pixels with the average colour (mipmaps then fade to the right colour, not to black).
        Vector3 sum = default;
        float weight = 0;
        foreach (var p in px)
        {
            sum += new Vector3(p.X, p.Y, p.Z);
            weight += p.W;
        }
        var avg = weight > 1e-4f ? sum / weight : Vector3.One;
        for (var i = 0; i < px.Length; i++)
        {
            var p = px[i];
            var c = p.W > 1e-4f ? new Vector3(p.X, p.Y, p.Z) / p.W : avg;
            if (p.W < 0.05f)
                c = Vector3.Lerp(avg, c, p.W / 0.05f); // nearly empty: lean towards the average too
            rgba[i * 4] = ToByte(c.X);
            rgba[i * 4 + 1] = ToByte(c.Y);
            rgba[i * 4 + 2] = ToByte(c.Z);
            rgba[i * 4 + 3] = ToByte(p.W);
        }
    }

    private static byte ToByte(float v) => (byte)Math.Clamp((int)MathF.Round(v * 255f), 0, 255);

    // ---- Image ---------------------------------------------------------------------------------------------

    private static void DrawImage(string path, Vector4[] px)
    {
        var (w, h, src) = Png.Decode(File.ReadAllBytes(path));
        if (w > DecalFile.MaxImageSize || h > DecalFile.MaxImageSize)
            throw new InvalidDataException($"{w} x {h} is bigger than {DecalFile.MaxImageSize} x {DecalFile.MaxImageSize}");
        for (var j = 0; j < LayerSize; j++)
        for (var i = 0; i < LayerSize; i++)
        {
            // Bilinear sample; the PNG's top row is the top of the decal (v = 1).
            var fx = (i + 0.5f) / LayerSize * w - 0.5f;
            var fy = (1f - (j + 0.5f) / LayerSize) * h - 0.5f;
            int x0 = Math.Clamp((int)MathF.Floor(fx), 0, w - 1), y0 = Math.Clamp((int)MathF.Floor(fy), 0, h - 1);
            int x1 = Math.Min(x0 + 1, w - 1), y1 = Math.Min(y0 + 1, h - 1);
            float tx = Math.Clamp(fx - x0, 0, 1), ty = Math.Clamp(fy - y0, 0, 1);
            var c = Vector4.Lerp(Vector4.Lerp(Texel(src, w, x0, y0), Texel(src, w, x1, y0), tx), Vector4.Lerp(Texel(src, w, x0, y1), Texel(src, w, x1, y1), tx), ty);
            px[j * LayerSize + i] = c; // premultiplied
        }
    }

    private static Vector4 Texel(byte[] src, int w, int x, int y)
    {
        var k = (y * w + x) * 4;
        var a = src[k + 3] / 255f;
        return new Vector4(src[k] / 255f * a, src[k + 1] / 255f * a, src[k + 2] / 255f * a, a);
    }

    // ---- Shapes ------------------------------------------------------------------------------------------

    /// <summary>Draws one layer over (or erases from) the premultiplied pixels.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)] // straight to optimised code: it runs once, at startup
    private static void DrawShape(DecalShape s, Vector4[] px)
    {
        var geo = Prepare(s);
        const float pixel = 1f / LayerSize;
        var edge = MathF.Max(pixel, s.Softness);
        for (var j = 0; j < LayerSize; j++)
        for (var i = 0; i < LayerSize; i++)
        {
            var p = new Vector2((i + 0.5f) * pixel, (j + 0.5f) * pixel);
            var d = Distance(s, geo, p);
            var cov = Math.Clamp(0.5f - d / edge, 0f, 1f);
            if (cov <= 0f)
                continue;
            if (s.Noise > 0)
                cov *= 1f - s.Noise * Grunge(p * s.NoiseScale, s.Seed);
            var a = cov * s.Color.W;
            ref var dst = ref px[j * LayerSize + i];
            if (s.Erase)
                dst *= 1f - a;
            else
                dst = new Vector4(s.Color.X * a, s.Color.Y * a, s.Color.Z * a, a) + dst * (1f - a);
        }
    }

    /// <summary>Precomputed pieces of an irregular shape (splat harmonics and droplets, crack segments, speckle dots).</summary>
    private sealed class Geometry
    {
        public float[] Amp = [], Phase = [];
        public readonly List<(Vector2 A, Vector2 B, float Ra, float Rb)> Segments = new();
        public readonly List<(Vector2 C, float R)> Dots = new();
    }

    private static Geometry Prepare(DecalShape s)
    {
        var g = new Geometry();
        var rng = new Rng((uint)s.Seed * 2654435761u + (uint)s.Kind * 40503u + 17u);
        switch (s.Kind)
        {
            case DecalShapeKind.Splat:
            {
                var lobes = Math.Clamp(s.Count, 2, 24);
                g.Amp = new float[lobes];
                g.Phase = new float[lobes];
                var total = 0f;
                for (var k = 0; k < lobes; k++)
                {
                    g.Amp[k] = rng.Next() / (1 + k * 0.6f);
                    g.Phase[k] = rng.Next() * MathF.Tau;
                    total += g.Amp[k];
                }
                for (var k = 0; k < lobes; k++)
                    g.Amp[k] /= MathF.Max(total, 1e-4f);
                var drops = lobes / 2;
                for (var k = 0; k < drops; k++)
                {
                    var ang = rng.Next() * MathF.Tau;
                    var dist = s.Radius * (1.15f + 0.5f * rng.Next());
                    g.Dots.Add((s.At + new Vector2(MathF.Cos(ang), MathF.Sin(ang)) * dist, s.Radius * (0.04f + 0.1f * rng.Next()) * (0.4f + s.Jag)));
                }
                break;
            }
            case DecalShapeKind.Cracks:
            {
                var cracks = Math.Max(1, s.Count);
                for (var c = 0; c < cracks; c++)
                {
                    var ang = (c + rng.Next() * 0.6f) / cracks * MathF.Tau;
                    var p = s.At;
                    const int steps = 6;
                    var len = s.Length * (0.6f + 0.6f * rng.Next());
                    for (var k = 0; k < steps; k++)
                    {
                        ang += (rng.Next() - 0.5f) * 1.8f * s.Jag;
                        var q = p + new Vector2(MathF.Cos(ang), MathF.Sin(ang)) * (len / steps * (0.7f + 0.6f * rng.Next()));
                        float t0 = k / (float)steps, t1 = (k + 1) / (float)steps;
                        g.Segments.Add((p, q, s.Width * 0.5f * (1f - 0.75f * t0), s.Width * 0.5f * (1f - 0.75f * t1)));
                        if (k == 2 && rng.Next() < 0.6f)
                        {
                            // A short branch off the middle.
                            var ba = ang + (rng.Next() < 0.5f ? 0.7f : -0.7f);
                            var bq = q + new Vector2(MathF.Cos(ba), MathF.Sin(ba)) * len * 0.25f;
                            g.Segments.Add((q, bq, s.Width * 0.25f, s.Width * 0.08f));
                        }
                        p = q;
                    }
                }
                break;
            }
            case DecalShapeKind.Speckle:
            {
                for (var k = 0; k < Math.Max(1, s.Count); k++)
                {
                    var ang = rng.Next() * MathF.Tau;
                    var dist = s.Radius * MathF.Sqrt(rng.Next());
                    g.Dots.Add((s.At + new Vector2(MathF.Cos(ang), MathF.Sin(ang)) * dist, s.Width * (0.3f + 0.7f * rng.Next())));
                }
                break;
            }
        }
        return g;
    }

    /// <summary>Signed distance from <paramref name="p"/> to the shape's edge (negative inside), in the square's units.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static float Distance(DecalShape s, Geometry g, Vector2 p)
    {
        switch (s.Kind)
        {
            case DecalShapeKind.Circle:
                return Vector2.Distance(p, s.At) - s.Radius;
            case DecalShapeKind.Ring:
                return MathF.Abs(Vector2.Distance(p, s.At) - s.Radius) - s.Width * 0.5f;
            case DecalShapeKind.Rect:
                return BoxDistance(Rotate(p - s.At, -s.Rotation), s.Size * 0.5f);
            case DecalShapeKind.Line:
                return SegmentDistance(p, s.At, s.To) - s.Width * 0.5f;
            case DecalShapeKind.Splat:
            {
                var v = p - s.At;
                var ang = MathF.Atan2(v.Y, v.X);
                var wobble = 0f;
                for (var k = 0; k < g.Amp.Length; k++)
                    wobble += g.Amp[k] * MathF.Sin((k + 1) * ang + g.Phase[k]);
                var d = v.Length() - s.Radius * (1f + s.Jag * wobble);
                foreach (var (c, r) in g.Dots)
                    d = MathF.Min(d, Vector2.Distance(p, c) - r);
                return d;
            }
            case DecalShapeKind.Cracks:
            {
                var d = float.MaxValue;
                foreach (var (a, b, ra, rb) in g.Segments)
                {
                    var t = SegmentT(p, a, b);
                    d = MathF.Min(d, Vector2.Distance(p, a + (b - a) * t) - (ra + (rb - ra) * t));
                }
                return d;
            }
            case DecalShapeKind.Speckle:
            {
                var d = float.MaxValue;
                foreach (var (c, r) in g.Dots)
                    d = MathF.Min(d, Vector2.Distance(p, c) - r);
                return d;
            }
            case DecalShapeKind.Stripes:
            {
                var q = Rotate(p - s.At, -s.Rotation);
                var box = BoxDistance(q, s.Size * 0.5f);
                var u = (q.X + q.Y) / s.Period; // diagonal bands
                var f = u - MathF.Floor(u);
                var band = (f < s.Duty ? -MathF.Min(f, s.Duty - f) : MathF.Min(f - s.Duty, 1f - f)) * s.Period * 0.7071f;
                return MathF.Max(box, band);
            }
            default:
                return 1f;
        }
    }

    private static Vector2 Rotate(Vector2 v, float degrees)
    {
        var a = degrees * MathF.PI / 180f;
        float c = MathF.Cos(a), sn = MathF.Sin(a);
        return new Vector2(v.X * c - v.Y * sn, v.X * sn + v.Y * c);
    }

    private static float BoxDistance(Vector2 q, Vector2 half)
    {
        var d = Vector2.Abs(q) - half;
        return Vector2.Max(d, Vector2.Zero).Length() + MathF.Min(MathF.Max(d.X, d.Y), 0f);
    }

    private static float SegmentT(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        var len2 = ab.LengthSquared();
        return len2 < 1e-12f ? 0f : Math.Clamp(Vector2.Dot(p - a, ab) / len2, 0f, 1f);
    }

    private static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b) => Vector2.Distance(p, a + (b - a) * SegmentT(p, a, b));

    /// <summary>Two octaves of smooth value noise, 0..1.</summary>
    private static float Grunge(Vector2 p, int seed) => Math.Clamp(ValueNoise(p, seed) * 0.65f + ValueNoise(p * 2.3f + new Vector2(17.1f, 3.7f), seed + 1) * 0.35f, 0f, 1f);

    private static float ValueNoise(Vector2 p, int seed)
    {
        int x0 = (int)MathF.Floor(p.X), y0 = (int)MathF.Floor(p.Y);
        float tx = p.X - x0, ty = p.Y - y0;
        tx = tx * tx * (3 - 2 * tx);
        ty = ty * ty * (3 - 2 * ty);
        float a = Hash01(x0, y0, seed), b = Hash01(x0 + 1, y0, seed), c = Hash01(x0, y0 + 1, seed), d = Hash01(x0 + 1, y0 + 1, seed);
        return (a + (b - a) * tx) * (1 - ty) + (c + (d - c) * tx) * ty;
    }

    private static float Hash01(int x, int y, int seed)
    {
        var h = (uint)x * 374761393u + (uint)y * 668265263u + (uint)seed * 2246822519u;
        h = (h ^ (h >> 13)) * 1274126177u;
        return ((h ^ (h >> 16)) & 0xFFFFFF) / 16777215f;
    }

    /// <summary>A tiny deterministic random source (xorshift), the same on every PC and .NET version.</summary>
    internal struct Rng(uint seed)
    {
        private uint _s = seed == 0 ? 0x9E3779B9u : seed;

        /// <summary>0 (inclusive) to 1 (exclusive).</summary>
        public float Next()
        {
            _s ^= _s << 13;
            _s ^= _s >> 17;
            _s ^= _s << 5;
            return (_s >> 8) / 16777216f;
        }
    }

    /// <summary>Builds the atlas for the whole library and reports the time (for the log and the self-test).</summary>
    public static byte[] BuildLibrary(out double ms)
    {
        var sw = Stopwatch.StartNew();
        var data = Build(Decals.All, Decals.LoadErrors);
        ms = sw.Elapsed.TotalMilliseconds;
        return data;
    }
}
