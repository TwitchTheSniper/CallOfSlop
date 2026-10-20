using System.IO.Compression;

namespace CallOfSlop;

/// <summary>
/// A small PNG reader (8-bit greyscale, grey + alpha, RGB and RGBA, not interlaced): enough for the branding images
/// shipped with the game, with no extra package. Decodes to tightly packed RGBA8, top row first.
/// </summary>
public static class Png
{
    private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];

    /// <summary>Decodes a PNG file. Throws <see cref="InvalidDataException"/> for anything it can't read.</summary>
    public static (int Width, int Height, byte[] Rgba) Decode(byte[] file)
    {
        if (file.Length < 33 || !file.AsSpan(0, 8).SequenceEqual(Signature))
            throw new InvalidDataException("not a PNG file");
        int width = 0, height = 0, bitDepth = 0, colorType = -1, interlace = 0;
        using var idat = new MemoryStream();
        var pos = 8;
        while (pos + 12 <= file.Length)
        {
            var len = (int)BigEndian(file, pos);
            var type = System.Text.Encoding.ASCII.GetString(file, pos + 4, 4);
            if (len < 0 || pos + 12 + len > file.Length)
                throw new InvalidDataException("truncated PNG chunk");
            var data = file.AsSpan(pos + 8, len);
            switch (type)
            {
                case "IHDR":
                    width = (int)BigEndian(file, pos + 8);
                    height = (int)BigEndian(file, pos + 12);
                    bitDepth = data[8];
                    colorType = data[9];
                    interlace = data[12];
                    break;
                case "IDAT":
                    idat.Write(data);
                    break;
            }
            pos += 12 + len;
            if (type == "IEND")
                break;
        }
        var channels = colorType switch { 0 => 1, 2 => 3, 4 => 2, 6 => 4, _ => 0 };
        if (width <= 0 || height <= 0 || width > 8192 || height > 8192 || bitDepth != 8 || channels == 0 || interlace != 0)
            throw new InvalidDataException($"unsupported PNG ({width}x{height}, {bitDepth}-bit, colour type {colorType}, interlace {interlace})");

        // Inflate (IDAT is one zlib stream across all chunks), then undo the per-row filters.
        var stride = width * channels;
        var raw = new byte[(stride + 1) * height];
        idat.Position = 0;
        using (var z = new ZLibStream(idat, CompressionMode.Decompress))
        {
            var read = 0;
            while (read < raw.Length)
            {
                var n = z.Read(raw, read, raw.Length - read);
                if (n == 0)
                    throw new InvalidDataException("PNG image data ends early");
                read += n;
            }
        }
        var img = new byte[stride * height];
        for (var y = 0; y < height; y++)
        {
            var filter = raw[y * (stride + 1)];
            var src = raw.AsSpan(y * (stride + 1) + 1, stride);
            var row = img.AsSpan(y * stride, stride);
            var prev = y > 0 ? img.AsSpan((y - 1) * stride, stride) : Span<byte>.Empty;
            for (var i = 0; i < stride; i++)
            {
                int a = i >= channels ? row[i - channels] : 0;
                int b = y > 0 ? prev[i] : 0;
                int c = i >= channels && y > 0 ? prev[i - channels] : 0;
                row[i] = filter switch
                {
                    0 => src[i],
                    1 => (byte)(src[i] + a),
                    2 => (byte)(src[i] + b),
                    3 => (byte)(src[i] + (a + b) / 2),
                    4 => (byte)(src[i] + Paeth(a, b, c)),
                    _ => throw new InvalidDataException($"bad PNG row filter {filter}"),
                };
            }
        }
        if (channels == 4)
            return (width, height, img);
        var rgba = new byte[width * height * 4];
        for (var p = 0; p < width * height; p++)
        {
            var s = p * channels;
            var d = p * 4;
            switch (channels)
            {
                case 1:
                    rgba[d] = rgba[d + 1] = rgba[d + 2] = img[s];
                    rgba[d + 3] = 255;
                    break;
                case 2:
                    rgba[d] = rgba[d + 1] = rgba[d + 2] = img[s];
                    rgba[d + 3] = img[s + 1];
                    break;
                default:
                    rgba[d] = img[s];
                    rgba[d + 1] = img[s + 1];
                    rgba[d + 2] = img[s + 2];
                    rgba[d + 3] = 255;
                    break;
            }
        }
        return (width, height, rgba);
    }

    private static uint BigEndian(byte[] b, int at) => (uint)(b[at] << 24 | b[at + 1] << 16 | b[at + 2] << 8 | b[at + 3]);

    private static int Paeth(int a, int b, int c)
    {
        var p = a + b - c;
        int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }
}
