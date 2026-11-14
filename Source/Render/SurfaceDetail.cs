using Brutal.OpenGlApi;
using static Brutal.OpenGlApi.OpenGl;

namespace CallOfSlop;

/// <summary>
/// The materials' detail textures (docs/render/MATERIALS.md, "Detail textures"): tileable pictures the game draws for itself at
/// start-up, one layer of a texture array each, which a material row names by "texture" (its sides) and "textureTop" (faces
/// that look up). Nothing is read from disk and nothing is licensed: every layer is maths in this file, so a layer is changed
/// here. A layer is a square repeat of the row's "tile" metres, its v running up a wall. Channels, the same for every layer:
/// R the colour (0.5 keeps it), G the height (0.5 flat: the light's bumps), B the cavity (joints, holes, pits: where dirt
/// lies), A the wear mask (the row's wear colour shows through: chipped paint, grout, mortar). Layer 0, "grime", is the
/// weathering's own: R streaks that run down (stretched by the shader), G splashes, B big blotches, A fine grit.
/// </summary>
public static unsafe class SurfaceDetail
{
    /// <summary>The texture unit (0-18 are taken: VideoFeeds.Unit, SurfaceGpu.Unit, PostFx's two).</summary>
    public const int Unit = 19;

    /// <summary>Texels a side of a layer.</summary>
    public const int Size = 512;

    /// <summary>The layers by name, in order: a row's "texture" is one of these (not "grime", which is the weathering's).</summary>
    public static readonly string[] Names =
        ["grime", "board concrete", "precast concrete", "slab", "rust", "chipped paint", "tiles", "lino", "brick", "terrazzo"];

    /// <summary>A layer's number by name (any case), or -1.</summary>
    public static int Layer(string name)
    {
        for (var i = 0; i < Names.Length; i++)
            if (string.Equals(Names[i], name, StringComparison.OrdinalIgnoreCase))
                return i;
        return -1;
    }

    private static bool _ready;
    private static uint _tex;

    /// <summary>Called with <see cref="SurfaceGpu.Register"/> for every scene program: the array exists and the sampler reads its unit.</summary>
    public static void Register(GlProgram program)
    {
        Upload();
        fixed (byte* n = "uSurfDetail\0"u8)
        {
            var loc = Native.glGetUniformLocation(program, n);
            if (loc >= 0)
                Native.glUniform1i(loc, Unit);
        }
    }

    private static void Upload()
    {
        if (_ready)
            return;
        _ready = true;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var pixels = Build(Size);
        GlTexture t = default;
        Native.glGenTextures(1, &t);
        _tex = t.Id;
        Native.glActiveTexture(GL_TEXTURE0 + Unit);
        Native.glBindTexture(GL_TEXTURE_2D_ARRAY, _tex);
        Native.glPixelStorei(GL_UNPACK_ALIGNMENT, 1);
        fixed (byte* p = pixels)
            Native.glTexImage3D(GL_TEXTURE_2D_ARRAY, 0, (int)GL_RGBA8, Size, Size, Names.Length, 0, GL_RGBA, GL_UNSIGNED_BYTE, p);
        Native.glGenerateMipmap(GL_TEXTURE_2D_ARRAY);
        Native.glTexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_MIN_FILTER, (int)GL_LINEAR_MIPMAP_LINEAR);
        Native.glTexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_MAG_FILTER, (int)GL_LINEAR);
        Native.glTexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_WRAP_S, (int)GL_REPEAT);
        Native.glTexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_WRAP_T, (int)GL_REPEAT);
        RenderCaps.Anisotropy(GL_TEXTURE_2D_ARRAY, 8f);
        Native.glActiveTexture(GL_TEXTURE0);
        GlDebug.Label(GL_TEXTURE, _tex, "SurfaceDetail.layers");
        Console.WriteLine($"[surfaces] detail textures: {Names.Length} layers of {Size}x{Size} in {sw.ElapsedMilliseconds} ms");
    }

    /// <summary>Every layer's texels, RGBA8, layer after layer (no GL: <c>--surface-detail</c> writes them out to look at).</summary>
    public static byte[] Build(int size)
    {
        var px = new byte[Names.Length * size * size * 4];
        Parallel.For(0, Names.Length * size, row =>
        {
            int layer = row / size, y = row % size;
            var v = (y + 0.5f) / size;
            var o = (layer * size + y) * size * 4;
            for (var x = 0; x < size; x++)
            {
                var u = (x + 0.5f) / size;
                var c = Texel(layer, u, v, 1f / size);
                px[o++] = Byte(c.R);
                px[o++] = Byte(c.G);
                px[o++] = Byte(c.B);
                px[o++] = Byte(c.A);
            }
        });
        return px;
    }

    /// <summary><c>--surface-detail DIR</c>: each layer as a PNG of four squares (colour, height, cavity, wear), to look at.</summary>
    public static int Cli(string dir)
    {
        Directory.CreateDirectory(dir);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var px = Build(Size);
        Console.WriteLine($"[surfaces] detail textures: {Names.Length} layers of {Size}x{Size} in {sw.ElapsedMilliseconds} ms");
        for (var l = 0; l < Names.Length; l++)
        {
            var rgb = new byte[Size * 4 * Size * 3];
            for (var y = 0; y < Size; y++)
                for (var x = 0; x < Size; x++)
                {
                    var src = ((l * Size + (Size - 1 - y)) * Size + x) * 4; // v up
                    for (var c = 0; c < 4; c++)
                    {
                        var dst = (y * Size * 4 + c * Size + x) * 3;
                        rgb[dst] = rgb[dst + 1] = rgb[dst + 2] = px[src + c];
                    }
                }
            var path = Path.Combine(dir, Names[l].Replace(' ', '_') + ".png");
            MapKit.PngWriter.Write(path, Size * 4, Size, rgb);
            Console.WriteLine(path);
        }
        return 0;
    }

    private static byte Byte(float f) => (byte)Math.Clamp((int)(f * 255f + 0.5f), 0, 255);

    private readonly record struct Rgba(float R, float G, float B, float A);

    // ---- The layers. u, v in 0..1 over one repeat (v up a wall); px is a texel in the same units. ----

    private static Rgba Texel(int layer, float u, float v, float px) => layer switch
    {
        0 => Grime(u, v),
        1 => Board(u, v, px),
        2 => Precast(u, v),
        3 => Slab(u, v, px),
        4 => Rust(u, v),
        5 => Chipped(u, v, px),
        6 => Tiles(u, v, px),
        7 => Lino(u, v, px),
        8 => Brick(u, v, px),
        9 => Terrazzo(u, v),
        _ => new Rgba(0.5f, 0.5f, 0f, 0f),
    };

    private static Rgba Grime(float u, float v) =>
        new(Contrast(Fbm(u, v, 20, 2, 4, 61), 1.8f), Fbm(u, v, 10, 10, 3, 62), Contrast(Fbm(u, v, 3, 3, 4, 63), 1.4f), Fbm(u, v, 48, 48, 2, 64));

    // Board-marked concrete, the Ministry's: 24 boards up a repeat (15 cm on a 3.6 m tile), each a run of 1.8 m with its butt joint
    // staggered, the wood's grain printed in it, a ridge where two boards met, tie holes on a 60 cm grid and blowholes.
    private static Rgba Board(float u, float v, float px)
    {
        const int rows = 24;
        var bv = v * rows;
        var row = (int)MathF.Floor(bv);
        var fv = bv - row;
        var stagger = Hash(row, 0, 11);
        var bu = u * 2f + stagger; // two runs a repeat
        var seg = Mod((int)MathF.Floor(bu), 2);
        var fu = bu - MathF.Floor(bu);
        var tone = (Hash(row, seg, 12) - 0.5f) * 0.07f;
        var grain = Fbm(u, v, 3, rows * 5, 3, 13) - 0.5f;
        var rings = MathF.Sin((fv * 5f + (Fbm(u, v, 5, rows, 2, 14) - 0.5f) * 9f) * MathF.PI) * 0.5f;
        var pv = px * rows; // a texel in board heights
        var joint = 1f - Smooth(0.4f * pv, 1.6f * pv, MathF.Min(fv, 1f - fv));
        var pu = px * 2f;
        var butt = 1f - Smooth(0.4f * pu, 1.4f * pu, MathF.Min(fu, 1f - fu));
        var blot = Fbm(u, v, 3, 3, 4, 15) - 0.5f;
        var lime = Fbm(u, v, 12, 12, 3, 17) - 0.5f; // the pour's own cloudiness, across the boards
        var pit = Pits(u, v, 46, 0.0022f, 0.22f, 16);
        // Tie holes, 6 by 6 (60 cm), each a dark hole in a plug a little paler than the wall.
        float tx = u * 6f, ty = v * 6f;
        var dx = (tx - MathF.Floor(tx) - 0.5f) / 6f;
        var dy = (ty - MathF.Floor(ty) - 0.5f) / 6f;
        var d = MathF.Sqrt(dx * dx + dy * dy);
        var hole = 1f - Smooth(0.0024f, 0.0024f + px * 1.2f, d);
        var plug = (1f - Smooth(0.0055f, 0.0055f + px, d)) * (1f - hole);
        var r = 0.5f + tone + grain * 0.05f + rings * 0.025f + blot * 0.2f + lime * 0.08f - joint * 0.08f - butt * 0.05f - pit * 0.18f - hole * 0.3f + plug * 0.05f;
        var g = 0.5f + grain * 0.18f + rings * 0.06f + joint * 0.28f - butt * 0.2f - pit * 0.42f - hole * 0.5f;
        var b = MathF.Max(MathF.Max(joint * 0.45f, butt * 0.6f), MathF.Max(pit * 0.8f, hole));
        return new(r, g, b, 0f);
    }

    // Smooth precast concrete: fine aggregate, cloudy patches, blowholes.
    private static Rgba Precast(float u, float v)
    {
        var grit = Fbm(u, v, 96, 96, 2, 21) - 0.5f;
        var fine = Fbm(u, v, 220, 220, 1, 22) - 0.5f;
        var blot = Fbm(u, v, 2, 2, 4, 23) - 0.5f;
        var pit = Pits(u, v, 52, 0.0022f, 0.5f, 24);
        return new(0.5f + grit * 0.12f + fine * 0.1f + blot * 0.15f - pit * 0.28f, 0.5f + grit * 0.22f + fine * 0.16f - pit * 0.45f, pit * 0.8f, 0f);
    }

    // A slab that is walked or driven on: broom marks across it, stains, the odd crack and oil spot.
    private static Rgba Slab(float u, float v, float px)
    {
        var broom = Fbm(u, v, 2, 260, 2, 31) - 0.5f;
        var grit = Fbm(u, v, 128, 128, 2, 35) - 0.5f;
        var stain = Fbm(u, v, 3, 3, 4, 32) - 0.5f;
        var (f1, f2, cell) = Worley(u, v, 5, 33);
        var crack = (1f - Smooth(0.15f * px, 1.3f * px, f2 - f1)) * (Hash(cell, 1, 34) < 0.35f ? 1f : 0f) * Smooth(0.45f, 0.6f, Fbm(u, v, 6, 6, 2, 36));
        var (s1, _, sc) = Worley(u, v, 7, 37);
        var spot = Hash(sc, 2, 38) < 0.12f ? (1f - Smooth(0.015f, 0.05f, s1 + (Fbm(u, v, 24, 24, 2, 39) - 0.5f) * 0.05f)) * 0.7f : 0f;
        return new(0.5f + broom * 0.08f + grit * 0.08f + stain * 0.22f - crack * 0.3f - spot * 0.18f,
            0.5f + broom * 0.25f + grit * 0.15f - crack * 0.5f, MathF.Max(crack, spot * 0.5f), 0f);
    }

    private static Rgba Rust(float u, float v)
    {
        var big = Fbm(u, v, 4, 4, 5, 41) - 0.5f;
        var fine = Fbm(u, v, 64, 64, 2, 42) - 0.5f;
        var pit = Pits(u, v, 60, 0.003f, 0.6f, 43);
        return new(0.5f + big * 0.5f + fine * 0.14f - pit * 0.2f, 0.5f + big * 0.3f + fine * 0.25f - pit * 0.4f, pit * 0.7f, 0f);
    }

    // Old paint over steel or plaster: orange peel, and chips in clusters where the wear colour shows through.
    private static Rgba Chipped(float u, float v, float px)
    {
        var peel = Fbm(u, v, 64, 64, 2, 51) - 0.5f;
        var blot = Fbm(u, v, 3, 3, 3, 53) - 0.5f;
        var m = Fbm(u, v, 6, 6, 5, 52);
        var chip = Smooth(0.635f - px * 4f, 0.635f + px * 4f, m);
        var rim = Smooth(0.6f, 0.635f, m) * (1f - chip);
        return new(0.5f + peel * 0.05f + blot * 0.07f - rim * 0.08f, 0.5f + 0.15f * (1f - chip) + peel * 0.08f, rim * 0.5f, chip);
    }

    // Glazed tiles, 8 by 8 a repeat (15 cm on a 1.2 m tile), each a little different, with grout (the wear colour) between.
    private static Rgba Tiles(float u, float v, float px)
    {
        float tu = u * 8f, tv = v * 8f;
        int cx = (int)MathF.Floor(tu), cy = (int)MathF.Floor(tv);
        float fu = tu - cx, fv = tv - cy;
        var edge = MathF.Min(MathF.Min(fu, 1f - fu), MathF.Min(fv, 1f - fv));
        var pe = px * 8f;
        var grout = 1f - Smooth(pe * 0.9f, pe * 2.0f, edge);
        var tone = (Hash(Mod(cx, 8), Mod(cy, 8), 55) - 0.5f) * 0.09f;
        var glaze = Fbm(u, v, 16, 16, 2, 56) - 0.5f;
        var bevel = Smooth(pe * 2f, pe * 6f, edge);
        return new(0.5f + tone + glaze * 0.04f - (1f - bevel) * 0.03f, 0.35f + 0.25f * bevel - grout * 0.2f, grout * 0.7f, grout);
    }

    // Linoleum tiles, 4 by 4 (30 cm on a 1.2 m tile) in a quiet chequer, marbled, with fine seams.
    private static Rgba Lino(float u, float v, float px)
    {
        float tu = u * 4f, tv = v * 4f;
        int cx = (int)MathF.Floor(tu), cy = (int)MathF.Floor(tv);
        float fu = tu - cx, fv = tv - cy;
        var edge = MathF.Min(MathF.Min(fu, 1f - fu), MathF.Min(fv, 1f - fv));
        var seam = 1f - Smooth(px * 4f * 0.5f, px * 4f * 1.5f, edge);
        var check = ((cx + cy) & 1) == 0 ? 0.42f : 0.58f;
        var warp = Fbm(u, v, 4, 4, 2, 57);
        var marble = Fbm(u + warp * 0.08f, v, 6, 6, 4, 58) - 0.5f;
        var scuff = Fbm(u, v, 40, 3, 2, 59) - 0.5f;
        return new(check + marble * 0.14f + scuff * 0.04f - seam * 0.12f, 0.5f - seam * 0.2f, seam * 0.6f, 0f);
    }

    // Brick in stretcher bond: 4 bricks across and 12 courses up (on a 0.9 m tile), mortar the wear colour.
    private static Rgba Brick(float u, float v, float px)
    {
        var rv = v * 12f;
        var course = (int)MathF.Floor(rv);
        var fv = rv - course;
        var ru = u * 4f + ((course & 1) == 1 ? 0.5f : 0f);
        var brick = (int)MathF.Floor(ru);
        var fu = ru - brick;
        var bed = 1f - Smooth(0.11f, 0.11f + px * 12f * 1.2f, MathF.Min(fv, 1f - fv) * 2f);
        var head = 1f - Smooth(0.035f, 0.035f + px * 4f * 1.2f, MathF.Min(fu, 1f - fu) * 2f);
        var mortar = MathF.Max(bed, head);
        var tone = (Hash(Mod(brick, 4), Mod(course, 12), 81) - 0.5f) * 0.3f;
        var fleck = Fbm(u, v, 90, 90, 2, 82) - 0.5f;
        return new(0.5f + tone + fleck * 0.12f, 0.62f - mortar * 0.3f + fleck * 0.1f, mortar * 0.4f, mortar);
    }

    // Terrazzo: chips of stone in a pale ground, polished flat.
    private static Rgba Terrazzo(float u, float v)
    {
        var (f1, f2, cell) = Worley(u, v, 40, 91);
        var chip = 1f - Smooth(-0.004f, 0.002f, 0.006f - (f2 - f1)); // the ground shows along the cells' borders
        var tone = Hash(cell, 3, 92);
        var (g1, g2, gcell) = Worley(u, v, 90, 93);
        var small = 1f - Smooth(-0.002f, 0.001f, 0.003f - (g2 - g1));
        var stone = small * (0.25f + Hash(gcell, 4, 94) * 0.6f) + (1f - small) * 0.5f;
        var r = chip > 0.5f ? 0.2f + tone * 0.65f : stone;
        return new(0.5f + (r - 0.5f) * 0.6f, 0.5f, 0f, 0f);
    }

    // ---- Tileable noise: every lattice wraps at its period, so a layer repeats without a seam. ----

    private static int Mod(int a, int m) => ((a % m) + m) % m;

    private static float Hash(int x, int y, int seed)
    {
        var h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
        h = (h ^ (h >> 13)) * 1274126177u;
        h ^= h >> 16;
        return (h & 0xffffff) / 16777216f;
    }

    private static float Smooth(float a, float b, float x)
    {
        var t = Math.Clamp((x - a) / (b - a), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    private static float Contrast(float v, float k) => Math.Clamp(0.5f + (v - 0.5f) * k, 0f, 1f);

    private static float Noise(float x, float y, int px, int py, int seed)
    {
        var xf = MathF.Floor(x);
        var yf = MathF.Floor(y);
        float fx = x - xf, fy = y - yf;
        fx = fx * fx * (3f - 2f * fx);
        fy = fy * fy * (3f - 2f * fy);
        int x0 = Mod((int)xf, px), y0 = Mod((int)yf, py);
        int x1 = (x0 + 1) % px, y1 = (y0 + 1) % py;
        var a = Hash(x0, y0, seed) + (Hash(x1, y0, seed) - Hash(x0, y0, seed)) * fx;
        var b = Hash(x0, y1, seed) + (Hash(x1, y1, seed) - Hash(x0, y1, seed)) * fx;
        return a + (b - a) * fy;
    }

    /// <summary>Fractal noise 0..1 over u, v in 0..1, its first octave px by py cells (each octave doubles both).</summary>
    private static float Fbm(float u, float v, int px, int py, int octaves, int seed)
    {
        float sum = 0, amp = 0.5f, norm = 0;
        for (var o = 0; o < octaves; o++)
        {
            sum += Noise(u * px, v * py, px, py, seed + o * 101) * amp;
            norm += amp;
            amp *= 0.5f;
            px *= 2;
            py *= 2;
        }
        return sum / norm;
    }

    /// <summary>Cellular noise over n by n wrapping cells: the nearest and second-nearest point's distance (in u units) and the nearest's cell.</summary>
    private static (float F1, float F2, int Cell) Worley(float u, float v, int n, int seed)
    {
        float x = u * n, y = v * n;
        int cx = (int)MathF.Floor(x), cy = (int)MathF.Floor(y);
        float f1 = 9f, f2 = 9f;
        var best = 0;
        for (var j = -1; j <= 1; j++)
            for (var i = -1; i <= 1; i++)
            {
                int gx = cx + i, gy = cy + j;
                int wx = Mod(gx, n), wy = Mod(gy, n);
                var ddx = gx + Hash(wx, wy, seed) - x;
                var ddy = gy + Hash(wx, wy, seed + 7) - y;
                var d = MathF.Sqrt(ddx * ddx + ddy * ddy) / n;
                if (d < f1)
                {
                    f2 = f1;
                    f1 = d;
                    best = wx + wy * n;
                }
                else if (d < f2)
                    f2 = d;
            }
        return (f1, f2, best);
    }

    /// <summary>Small round pits (blowholes): a share of n by n cells holds one of up to radius r (u units), 0..1 deep.</summary>
    private static float Pits(float u, float v, int n, float r, float share, int seed)
    {
        var (f1, _, cell) = Worley(u, v, n, seed);
        var h = Hash(cell, 9, seed);
        if (h > share)
            return 0f;
        var rr = r * (0.4f + h / share * 0.6f);
        return 1f - Smooth(rr * 0.6f, rr, f1);
    }
}
