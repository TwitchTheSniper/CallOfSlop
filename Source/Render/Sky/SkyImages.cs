using System.Globalization;
using System.Numerics;
using System.Text;

namespace CallOfSlop;

/// <summary>
/// A decoded image sky: six RGBA8 cubemap faces, or one equirectangular panorama (RGBA8 from a PNG, or float RGB from a
/// Radiance .hdr). CPU-side sampling here uses exactly the shader's direction mapping, so the colours measured for fog
/// (<see cref="Measure"/>) match what is drawn.
/// </summary>
public sealed class SkyImage
{
    public SkyType Type;

    /// <summary>Face size (cubemap) or panorama size (equirect), in pixels.</summary>
    public int Width, Height;

    /// <summary>Cubemap faces, +X, -X, +Y, -Y, +Z, -Z, RGBA8, rows top to bottom (GL's cube map orientation).</summary>
    public byte[][] Faces = [];

    /// <summary>An LDR panorama, RGBA8, rows top (zenith) to bottom.</summary>
    public byte[]? Rgba;

    /// <summary>An HDR panorama, linear float RGB, rows top to bottom.</summary>
    public float[]? Rgb;

    /// <summary>Which decoder read it ("Brutal.Texture (stb)", "built-in PNG reader", "built-in RGBE reader").</summary>
    public string Decoder = "";

    public bool Hdr => Rgb != null;

    /// <summary>Bytes the image takes on the GPU (for the log).</summary>
    public long GpuBytes => Type == SkyType.Cubemap ? 6L * Width * Height * 4 : (long)Width * Height * (Hdr ? 8 : 4);

    /// <summary>The raw texel in direction <paramref name="d"/> (unit; nearest texel, no rotation, no exposure).</summary>
    public Vector3 Sample(Vector3 d)
    {
        if (Type == SkyType.Cubemap)
        {
            var (face, s, t) = CubeFace(d);
            var px = Math.Clamp((int)(s * Width), 0, Width - 1);
            var py = Math.Clamp((int)(t * Height), 0, Height - 1);
            var f = Faces[face];
            var i = (py * Width + px) * 4;
            return new Vector3(f[i], f[i + 1], f[i + 2]) / 255f;
        }
        var (u, v) = EquirectUv(d);
        var x = Math.Clamp((int)(u * Width), 0, Width - 1);
        var y = Math.Clamp((int)(v * Height), 0, Height - 1);
        if (Rgb != null)
        {
            var k = (y * Width + x) * 3;
            return new Vector3(Rgb[k], Rgb[k + 1], Rgb[k + 2]);
        }
        var j = (y * Width + x) * 4;
        return new Vector3(Rgba![j], Rgba[j + 1], Rgba[j + 2]) / 255f;
    }

    /// <summary>What the shader shows for texel colour <paramref name="t"/>: HDR is tone mapped, LDR just scaled.</summary>
    public Vector3 Display(Vector3 t, float exposure) =>
        Hdr ? Vector3.One - new Vector3(MathF.Exp(-t.X * exposure), MathF.Exp(-t.Y * exposure), MathF.Exp(-t.Z * exposure)) : Vector3.Min(t * exposure, Vector3.One);

    /// <summary>
    /// Average displayed colour straight up (above ~55 degrees), along the horizon (0-6 degrees up) and below (under
    /// -18 degrees): the gradient the fog and ambient use for an image sky when the file doesn't give one.
    /// </summary>
    public (Vector3 Zenith, Vector3 Horizon, Vector3 Ground) Measure(float exposure, float rotationDeg)
    {
        Vector3 zen = default, hor = default, gnd = default;
        int nz = 0, nh = 0, ng = 0;
        var rot = rotationDeg * MathF.PI / 180f;
        for (var a = 0; a < 96; a++)
        {
            var phi = (a + 0.5f) / 96f * MathF.Tau;
            for (var b = 0; b < 64; b++)
            {
                var y = -1f + (b + 0.5f) / 32f; // uniform in height = uniform over the sphere
                var r = MathF.Sqrt(MathF.Max(0f, 1f - y * y));
                var d = new Vector3(MathF.Sin(phi) * r, y, -MathF.Cos(phi) * r);
                var c = Display(Sample(SkyRenderer.Rotate(d, rot)), exposure);
                if (y > 0.82f)
                {
                    zen += c;
                    nz++;
                }
                else if (y is >= 0f and < 0.1f)
                {
                    hor += c;
                    nh++;
                }
                else if (y < -0.3f)
                {
                    gnd += c;
                    ng++;
                }
            }
        }
        // Extra horizon samples: the band is thin, so take a dense ring as well.
        for (var a = 0; a < 360; a++)
        {
            var phi = (a + 0.5f) / 360f * MathF.Tau;
            var d = Vector3.Normalize(new Vector3(MathF.Sin(phi), 0.03f, -MathF.Cos(phi)));
            hor += Display(Sample(SkyRenderer.Rotate(d, rot)), exposure);
            nh++;
        }
        return (zen / Math.Max(nz, 1), hor / Math.Max(nh, 1), gnd / Math.Max(ng, 1));
    }

    /// <summary>OpenGL's cube map face selection: face index and (s, t) in 0-1, t = 0 at the top row.</summary>
    public static (int Face, float S, float T) CubeFace(Vector3 d)
    {
        float ax = MathF.Abs(d.X), ay = MathF.Abs(d.Y), az = MathF.Abs(d.Z);
        int face;
        float ma, sc, tc;
        if (ax >= ay && ax >= az)
        {
            face = d.X > 0 ? 0 : 1;
            ma = ax;
            sc = d.X > 0 ? -d.Z : d.Z;
            tc = -d.Y;
        }
        else if (ay >= az)
        {
            face = d.Y > 0 ? 2 : 3;
            ma = ay;
            sc = d.X;
            tc = d.Y > 0 ? d.Z : -d.Z;
        }
        else
        {
            face = d.Z > 0 ? 4 : 5;
            ma = az;
            sc = d.Z > 0 ? d.X : -d.X;
            tc = -d.Y;
        }
        ma = MathF.Max(ma, 1e-9f);
        return (face, (sc / ma + 1f) * 0.5f, (tc / ma + 1f) * 0.5f);
    }

    /// <summary>The shader's panorama mapping: u = compass angle (0.5 = north, -Z), v = 0 at the zenith.</summary>
    public static (float U, float V) EquirectUv(Vector3 d) =>
        (MathF.Atan2(d.X, -d.Z) / MathF.Tau + 0.5f, MathF.Acos(Math.Clamp(d.Y, -1f, 1f)) / MathF.PI);
}

/// <summary>
/// Loads image skies. PNGs go through <see cref="MeshTextures.Decode"/> (Brutal.Texture / stb when its canary passed,
/// otherwise the game's own <see cref="Png"/> reader). Radiance .hdr files go through Brutal.Texture when its HDR canary
/// passes (<see cref="HdrCanary"/>: a small .hdr decoded by stb and by the built-in RGBE reader must match), otherwise
/// through the built-in RGBE reader (<see cref="DecodeHdr"/>), so an HDR sky never depends on a native working.
/// </summary>
public static class SkyImages
{
    /// <summary>Biggest cubemap face and panorama the game accepts (keeps load times and VRAM sane).</summary>
    public const int MaxFace = 2048, MaxPanoramaWidth = 8192;

    private static int _hdrState; // 0 = not run, 1 = stb works, -1 = built-in reader
    private static readonly object Gate = new();

    /// <summary>One line describing the HDR canary.</summary>
    public static string HdrCanaryReport { get; private set; } = "not run";

    /// <summary>Does Brutal.Texture decode .hdr files here (and agree with the built-in reader)? Runs the canary on first use.</summary>
    public static bool HdrCanary
    {
        get
        {
            if (_hdrState == 0)
                lock (Gate)
                    if (_hdrState == 0)
                        _hdrState = RunHdrCanary() ? 1 : -1;
            return _hdrState > 0;
        }
    }

    /// <summary>
    /// The file <paramref name="file"/> named by a map (<paramref name="source"/> is the map's path relative to the Maps
    /// folder): next to the map file first, then in its pack folder (Maps/&lt;Pack&gt;, for a map in a pack subfolder),
    /// then from the Maps folder (the shared images). "global:Skies/x.png" looks in the Maps folder only. Absolute paths
    /// and ".." aren't allowed, so maps stay self-contained.
    /// </summary>
    public static string Resolve(string file, string source)
    {
        var forceGlobal = file.StartsWith(PropScope.GlobalPrefix, StringComparison.OrdinalIgnoreCase);
        if (forceGlobal)
            file = file[PropScope.GlobalPrefix.Length..].Trim();
        var norm = file.Replace('\\', '/');
        if (Path.IsPathRooted(file) || norm.Split('/').Any(p => p == ".."))
            throw new ArgumentException($"image \"{file}\" must be a path inside the Maps folder (no drive letters or \"..\")");
        var looked = new List<string>();
        if (!forceGlobal)
        {
            var dir = (Path.GetDirectoryName(source) ?? "").Replace('\\', '/');
            var near = Path.Combine(Maps.Folder, dir, norm);
            if (File.Exists(near))
                return near;
            looked.Add(dir.Length > 0 ? "Maps/" + dir : "Maps");
            var slash = dir.IndexOf('/');
            if (slash > 0) // a map in a subfolder of its pack: the pack folder next
            {
                var pack = Path.Combine(Maps.Folder, dir[..slash], norm);
                if (File.Exists(pack))
                    return pack;
                looked.Add("Maps/" + dir[..slash]);
            }
        }
        var root = Path.Combine(Maps.Folder, norm);
        if (File.Exists(root))
            return root;
        if (!looked.Contains("Maps"))
            looked.Add("Maps");
        throw new IOException($"image \"{file}\" not found (looked in {string.Join(", ", looked)})");
    }

    /// <summary>Six square PNG faces of one size, +X, -X, +Y, -Y, +Z, -Z.</summary>
    public static SkyImage LoadCubemap(string[] paths)
    {
        if (paths.Length != 6)
            throw new ArgumentException("a cubemap needs 6 faces");
        var img = new SkyImage { Type = SkyType.Cubemap, Faces = new byte[6][] };
        for (var i = 0; i < 6; i++)
        {
            var name = Path.GetFileName(paths[i]);
            if (!paths[i].EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"cubemap face {name}: faces are .png files");
            var (w, h, rgba) = DecodePng(File.ReadAllBytes(paths[i]), name);
            if (w != h)
                throw new InvalidDataException($"cubemap face {name} is {w}x{h}; faces must be square");
            if (w > MaxFace)
                throw new InvalidDataException($"cubemap face {name} is {w}x{h}; the largest allowed is {MaxFace}x{MaxFace}");
            if (i > 0 && w != img.Width)
                throw new InvalidDataException($"cubemap face {name} is {w}x{h} but the first face is {img.Width}x{img.Height}; all six must be the same size");
            (img.Width, img.Height) = (w, h);
            img.Faces[i] = rgba;
        }
        img.Decoder = MeshTextures.StbAvailable ? "Brutal.Texture (stb)" : "built-in PNG reader";
        return img;
    }

    /// <summary>A 2:1 panorama, .png or .hdr.</summary>
    public static SkyImage LoadEquirect(string path)
    {
        var name = Path.GetFileName(path);
        var bytes = File.ReadAllBytes(path);
        var img = new SkyImage { Type = SkyType.Equirect };
        if (path.EndsWith(".hdr", StringComparison.OrdinalIgnoreCase))
        {
            (img.Width, img.Height, img.Rgb, img.Decoder) = LoadHdr(bytes, name);
        }
        else if (path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
        {
            (img.Width, img.Height, img.Rgba) = DecodePng(bytes, name);
            img.Decoder = MeshTextures.StbAvailable ? "Brutal.Texture (stb)" : "built-in PNG reader";
        }
        else
            throw new InvalidDataException($"panorama {name}: use a .png or .hdr file");
        var aspect = img.Width / (float)img.Height;
        if (aspect < 1.8f || aspect > 2.2f)
            throw new InvalidDataException($"panorama {name} is {img.Width}x{img.Height}; an equirect sky is twice as wide as it is tall");
        if (img.Width > MaxPanoramaWidth)
            throw new InvalidDataException($"panorama {name} is {img.Width}x{img.Height}; the largest allowed is {MaxPanoramaWidth}x{MaxPanoramaWidth / 2}");
        return img;
    }

    private static (int, int, byte[]) DecodePng(byte[] bytes, string name)
    {
        try
        {
            return MeshTextures.Decode(bytes, "image/png");
        }
        catch (Exception ex) when (ex is not IOException)
        {
            throw new InvalidDataException($"{name} isn't a readable PNG ({ex.Message})");
        }
    }

    /// <summary>Decodes a .hdr with Brutal.Texture if its canary passed, otherwise with the built-in reader.</summary>
    public static (int Width, int Height, float[] Rgb, string Decoder) LoadHdr(byte[] bytes, string name)
    {
        try
        {
            if (HdrCanary)
            {
                var (w, h, rgb) = DecodeHdrStb(bytes);
                return (w, h, rgb, "Brutal.Texture (stb)");
            }
            var (bw, bh, brgb) = DecodeHdr(bytes);
            return (bw, bh, brgb, "built-in RGBE reader");
        }
        catch (Exception ex) when (ex is not IOException)
        {
            throw new InvalidDataException($"{name} isn't a readable Radiance .hdr ({ex.Message})");
        }
    }

    private static (int, int, float[]) DecodeHdrStb(byte[] bytes)
    {
        var tex = Brutal.TextureApi.TextureLoader.LoadFromMemory(bytes, Brutal.TextureApi.TextureLoader.FormatType.Hdr,
            new Brutal.TextureApi.Stb.Loader.LoadSettings { ForceChannels = 3, ForceDataType = Brutal.StbApi.Stb.DataType.Float });
        try
        {
            var w = tex.Extent.X;
            var h = tex.Extent.Y;
            var data = tex.Data;
            if (w <= 0 || h <= 0 || data.Length < w * h * 12)
                throw new InvalidDataException($"Brutal.Texture returned {w}x{h} with {data.Length} bytes");
            var rgb = new float[w * h * 3];
            System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(data[..(w * h * 12)]).CopyTo(rgb);
            return (w, h, rgb);
        }
        finally
        {
            Brutal.TextureApi.TextureLoader.Unload(tex);
        }
    }

    /// <summary>
    /// The built-in Radiance RGBE reader: "#?RADIANCE" / "#?RGBE" header, "-Y height +X width" (the standard
    /// orientation), flat or adaptive run-length encoded scanlines. Same maths as stb_image: value = byte * 2^(e - 136).
    /// </summary>
    public static (int Width, int Height, float[] Rgb) DecodeHdr(byte[] data)
    {
        var pos = 0;
        string ReadLine()
        {
            var start = pos;
            while (pos < data.Length && data[pos] != (byte)'\n')
                pos++;
            if (pos >= data.Length)
                throw new InvalidDataException("the header never ends");
            var line = Encoding.ASCII.GetString(data, start, pos - start).TrimEnd('\r');
            pos++;
            return line;
        }
        var magic = ReadLine();
        if (!magic.StartsWith("#?RADIANCE") && !magic.StartsWith("#?RGBE"))
            throw new InvalidDataException("not a Radiance file (no #?RADIANCE line)");
        for (var line = ReadLine(); line.Length > 0; line = ReadLine())
            if (line.StartsWith("FORMAT=") && line != "FORMAT=32-bit_rle_rgbe")
                throw new InvalidDataException($"unsupported {line} (only 32-bit_rle_rgbe)");
        var res = ReadLine().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (res.Length != 4 || res[0] != "-Y" || res[2] != "+X"
            || !int.TryParse(res[1], NumberStyles.None, CultureInfo.InvariantCulture, out var h)
            || !int.TryParse(res[3], NumberStyles.None, CultureInfo.InvariantCulture, out var w) || w <= 0 || h <= 0 || w > 16384 || h > 16384)
            throw new InvalidDataException("unsupported resolution line (expected \"-Y height +X width\")");
        var rgb = new float[w * h * 3];
        var line4 = new byte[w * 4];
        for (var y = 0; y < h; y++)
        {
            if (w >= 8 && w < 32768 && pos + 4 <= data.Length && data[pos] == 2 && data[pos + 1] == 2 && (data[pos + 2] & 0x80) == 0)
            {
                if (((data[pos + 2] << 8) | data[pos + 3]) != w)
                    throw new InvalidDataException("a scanline's width doesn't match the image");
                pos += 4;
                for (var c = 0; c < 4; c++)
                {
                    var x = 0;
                    while (x < w)
                    {
                        if (pos >= data.Length)
                            throw new InvalidDataException("the pixel data ends early");
                        int count = data[pos++];
                        if (count > 128)
                        {
                            count -= 128;
                            if (count > w - x || pos >= data.Length)
                                throw new InvalidDataException("bad run-length data");
                            var v = data[pos++];
                            for (var k = 0; k < count; k++)
                                line4[(x++) * 4 + c] = v;
                        }
                        else
                        {
                            if (count == 0 || count > w - x || pos + count > data.Length)
                                throw new InvalidDataException("bad run-length data");
                            for (var k = 0; k < count; k++)
                                line4[(x++) * 4 + c] = data[pos++];
                        }
                    }
                }
            }
            else
            {
                if (pos + w * 4 > data.Length)
                    throw new InvalidDataException("the pixel data ends early");
                Buffer.BlockCopy(data, pos, line4, 0, w * 4);
                pos += w * 4;
            }
            for (var x = 0; x < w; x++)
            {
                var e = line4[x * 4 + 3];
                var o = (y * w + x) * 3;
                if (e == 0)
                    continue;
                var f = MathF.ScaleB(1f, e - 136);
                rgb[o] = line4[x * 4] * f;
                rgb[o + 1] = line4[x * 4 + 1] * f;
                rgb[o + 2] = line4[x * 4 + 2] * f;
            }
        }
        return (w, h, rgb);
    }

    /// <summary>A small flat (not run-length encoded) .hdr for the canary and the self-test.</summary>
    public static byte[] TinyHdr(int w = 5, int h = 3)
    {
        var head = Encoding.ASCII.GetBytes($"#?RADIANCE\nFORMAT=32-bit_rle_rgbe\n\n-Y {h} +X {w}\n");
        var px = new byte[w * h * 4];
        for (var i = 0; i < w * h; i++)
        {
            px[i * 4] = (byte)(40 + i * 13);
            px[i * 4 + 1] = (byte)(200 - i * 9);
            px[i * 4 + 2] = (byte)(90 + i * 5);
            px[i * 4 + 3] = (byte)(126 + i % 6); // exponents around 2^-2..2^3: values from ~0.04 to ~10
        }
        return [.. head, .. px];
    }

    private static bool RunHdrCanary()
    {
        try
        {
            if (!MeshTextures.StbAvailable)
                return Report(false, "Brutal.Texture's PNG canary failed, so stb isn't used for .hdr either");
            var hdr = TinyHdr();
            var (w, h, own) = DecodeHdr(hdr);
            var (sw, sh, stb) = DecodeHdrStb(hdr);
            if (sw != w || sh != h)
                return Report(false, $"stb read the test .hdr as {sw}x{sh}, expected {w}x{h}");
            for (var i = 0; i < own.Length; i++)
                if (MathF.Abs(own[i] - stb[i]) > 1e-4f * MathF.Max(1f, own[i]))
                    return Report(false, $"stb decoded the test .hdr differently (value {i}: {stb[i]} vs {own[i]})");
            return Report(true, $"ok (TextureLoader.LoadFromMemory hdr {w}x{h}, RGB float, matches the built-in RGBE reader)");
        }
        catch (Exception ex)
        {
            return Report(false, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static bool Report(bool ok, string text)
    {
        HdrCanaryReport = (ok ? "" : "FALLBACK (built-in RGBE reader): ") + text;
        Console.WriteLine($"[sky] Brutal.Texture HDR canary: {HdrCanaryReport}");
        return ok;
    }
}
