namespace CallOfSlop;

/// <summary>
/// Textures imported with models (glTF base colour textures), resized to one size so the mesh renderer keeps them in
/// a single texture array next to the procedural one (<see cref="Textures"/>): no texture switches between models.
/// Decoding goes through Brutal.Texture (stb) when its native passed the canary, otherwise through the game's own PNG
/// reader (<see cref="Png"/>; JPEG textures are then skipped and the material keeps its colour factor).
/// </summary>
public static class MeshTextures
{
    /// <summary>Texels per side of every imported layer.</summary>
    public const int Size = 256;

    /// <summary>The most imported layers (later ones fall back to their material colour).</summary>
    public const int MaxLayers = 64;

    private static readonly List<byte[]> Layers = new();
    private static readonly List<string> Names = new();
    private static readonly object Gate = new();

    /// <summary>Bumped whenever a layer is added (the renderer re-uploads the array).</summary>
    public static int Generation { get; private set; }

    public static int Count
    {
        get
        {
            lock (Gate)
                return Layers.Count;
        }
    }

    /// <summary>Copies of every layer (RGBA8, <see cref="Size"/> squared each), for the upload.</summary>
    public static byte[][] Snapshot()
    {
        lock (Gate)
            return Layers.ToArray();
    }

    public static string NameOf(int layer)
    {
        lock (Gate)
            return (uint)layer < (uint)Names.Count ? Names[layer] : "";
    }

    /// <summary>Adds an RGBA8 image (any size; resampled to <see cref="Size"/>) and returns its layer, or -1 when full.</summary>
    public static int Add(string name, int width, int height, ReadOnlySpan<byte> rgba)
    {
        if (width <= 0 || height <= 0 || rgba.Length < width * height * 4)
            throw new ArgumentException($"texture {name}: bad image data");
        var resized = Resample(width, height, rgba);
        lock (Gate)
        {
            if (Layers.Count >= MaxLayers)
                return -1;
            Layers.Add(resized);
            Names.Add(name);
            Generation++;
            return Layers.Count - 1;
        }
    }

    /// <summary>Bilinear resample (box-filtered when shrinking a lot) to <see cref="Size"/> x <see cref="Size"/>.</summary>
    private static byte[] Resample(int w, int h, ReadOnlySpan<byte> src)
    {
        var dst = new byte[Size * Size * 4];
        if (w == Size && h == Size)
        {
            src[..dst.Length].CopyTo(dst);
            return dst;
        }
        // Average blocks first when the source is much bigger (keeps thin details from aliasing away).
        var stepX = Math.Max(1, w / Size);
        var stepY = Math.Max(1, h / Size);
        for (var y = 0; y < Size; y++)
            for (var x = 0; x < Size; x++)
            {
                var fx = (x + 0.5f) * w / Size - 0.5f;
                var fy = (y + 0.5f) * h / Size - 0.5f;
                int x0 = Math.Clamp((int)MathF.Floor(fx), 0, w - 1), y0 = Math.Clamp((int)MathF.Floor(fy), 0, h - 1);
                int x1 = Math.Min(x0 + stepX, w - 1), y1 = Math.Min(y0 + stepY, h - 1);
                float tx = stepX > 1 ? 0.5f : Math.Clamp(fx - x0, 0, 1), ty = stepY > 1 ? 0.5f : Math.Clamp(fy - y0, 0, 1);
                for (var c = 0; c < 4; c++)
                {
                    float a = src[(y0 * w + x0) * 4 + c], b = src[(y0 * w + x1) * 4 + c];
                    float cc = src[(y1 * w + x0) * 4 + c], d = src[(y1 * w + x1) * 4 + c];
                    var v = (a + (b - a) * tx) * (1 - ty) + (cc + (d - cc) * tx) * ty;
                    dst[(y * Size + x) * 4 + c] = (byte)Math.Clamp((int)MathF.Round(v), 0, 255);
                }
            }
        return dst;
    }

    // ---- Decoding --------------------------------------------------------------------------------------------------

    private static int _stbState; // 0 = unknown, 1 = works, -1 = fall back

    /// <summary>One line describing the Brutal.Texture (stb) canary.</summary>
    public static string CanaryReport { get; private set; } = "not run";

    /// <summary>Does Brutal.Texture decode PNGs here? Runs a canary on first use: decodes a PNG with both decoders and compares.</summary>
    public static bool StbAvailable
    {
        get
        {
            if (_stbState == 0)
                lock (Gate)
                    if (_stbState == 0)
                        _stbState = RunCanary() ? 1 : -1;
            return _stbState > 0;
        }
    }

    private static bool RunCanary()
    {
        try
        {
            var png = TinyPng();
            var (w, h, own) = Png.Decode(png);
            var (sw, sh, stb) = DecodeStb(png, "png");
            if (sw != w || sh != h || !stb.AsSpan(0, w * h * 4).SequenceEqual(own))
                return Report(false, $"stb decoded the test PNG differently ({sw}x{sh} vs {w}x{h})");
            return Report(true, $"ok (TextureLoader.LoadFromMemory png {w}x{h}, RGBA8, matches the built-in PNG reader)");
        }
        catch (Exception ex)
        {
            return Report(false, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static bool Report(bool ok, string text)
    {
        CanaryReport = (ok ? "" : "FALLBACK (built-in PNG reader): ") + text;
        Console.WriteLine($"[mesh] Brutal.Texture canary: {CanaryReport}");
        return ok;
    }

    /// <summary>Decodes a PNG or JPEG to RGBA8. Throws a readable message if it can't.</summary>
    public static (int Width, int Height, byte[] Rgba) Decode(byte[] data, string mime)
    {
        var isPng = data.Length > 8 && data[0] == 0x89 && data[1] == (byte)'P';
        var isJpg = data.Length > 3 && data[0] == 0xFF && data[1] == 0xD8;
        if (!isPng && !isJpg)
            throw new InvalidDataException($"image is not PNG or JPEG (declared \"{mime}\")");
        if (StbAvailable)
            return DecodeStb(data, isPng ? "png" : "jpg");
        if (isPng)
            return Png.Decode(data);
        throw new InvalidDataException("JPEG textures need Brutal.Texture, whose native isn't working here");
    }

    private static (int, int, byte[]) DecodeStb(byte[] data, string kind)
    {
        var tex = Brutal.TextureApi.TextureLoader.LoadFromMemory(data,
            kind == "png" ? Brutal.TextureApi.TextureLoader.FormatType.Png : Brutal.TextureApi.TextureLoader.FormatType.Jpg,
            Brutal.TextureApi.Stb.Loader.LoadSettings.ForceRgba8);
        try
        {
            var w = tex.Extent.X;
            var h = tex.Extent.Y;
            var bytes = tex.Data;
            if (w <= 0 || h <= 0 || bytes.Length < w * h * 4)
                throw new InvalidDataException($"Brutal.Texture returned {w}x{h} with {bytes.Length} bytes");
            return (w, h, bytes[..(w * h * 4)].ToArray());
        }
        finally
        {
            Brutal.TextureApi.TextureLoader.Unload(tex);
        }
    }

    /// <summary>A 4x3 RGBA PNG (stored, uncompressed deflate) for the canary.</summary>
    private static byte[] TinyPng()
    {
        const int w = 4, h = 3;
        var raw = new List<byte>();
        for (var y = 0; y < h; y++)
        {
            raw.Add(0);
            for (var x = 0; x < w; x++)
                raw.AddRange([(byte)(x * 60), (byte)(y * 90), (byte)(200 - x * 20), (byte)(255 - y * 40)]);
        }
        var rawArr = raw.ToArray();
        var z = new List<byte> { 0x78, 0x01, 1, (byte)rawArr.Length, (byte)(rawArr.Length >> 8), (byte)~rawArr.Length, (byte)(~rawArr.Length >> 8) };
        z.AddRange(rawArr);
        uint a = 1, b = 0;
        foreach (var v in rawArr)
        {
            a = (a + v) % 65521;
            b = (b + a) % 65521;
        }
        var adler = (b << 16) | a;
        z.AddRange([(byte)(adler >> 24), (byte)(adler >> 16), (byte)(adler >> 8), (byte)adler]);
        var png = new List<byte> { 0x89, (byte)'P', (byte)'N', (byte)'G', 13, 10, 26, 10 };
        void Chunk(string type, byte[] body)
        {
            png.AddRange([(byte)(body.Length >> 24), (byte)(body.Length >> 16), (byte)(body.Length >> 8), (byte)body.Length]);
            var tb = System.Text.Encoding.ASCII.GetBytes(type).Concat(body).ToArray();
            png.AddRange(tb);
            var crc = Crc32(tb);
            png.AddRange([(byte)(crc >> 24), (byte)(crc >> 16), (byte)(crc >> 8), (byte)crc]);
        }
        Chunk("IHDR", [0, 0, 0, w, 0, 0, 0, h, 8, 6, 0, 0, 0]);
        Chunk("IDAT", z.ToArray());
        Chunk("IEND", []);
        return png.ToArray();
    }

    private static uint Crc32(byte[] data)
    {
        var c = 0xFFFFFFFFu;
        foreach (var v in data)
        {
            c ^= v;
            for (var k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
        }
        return ~c;
    }
}
