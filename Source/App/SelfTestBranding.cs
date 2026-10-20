using System.Security.Cryptography;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>The BRUTAL logo for the launch splash (licence section 2.2): shipped unaltered, readable, and shown long enough.</summary>
    private static void CheckBranding()
    {
        var path = Path.Combine(AppContext.BaseDirectory, Splash.LogoFile);
        if (!File.Exists(path))
        {
            Fail($"{Splash.LogoFile} is missing from the build output (the BRUTAL licence needs the logo shown at launch)");
            return;
        }
        var bytes = File.ReadAllBytes(path);
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (hash != Splash.LogoSha256)
            Fail($"{Splash.LogoFile} isn't the official logo file (sha256 {hash}); the licence allows scaling only");
        try
        {
            var (w, h, rgba) = Png.Decode(bytes);
            int yellow = 0, white = 0, clear = 0;
            for (var i = 0; i < rgba.Length; i += 4)
            {
                if (rgba[i + 3] < 8)
                    clear++;
                else if (rgba[i] > 200 && rgba[i + 1] > 160 && rgba[i + 2] < 80)
                    yellow++;
                else if (rgba[i] > 230 && rgba[i + 1] > 230 && rgba[i + 2] > 230)
                    white++;
            }
            Line($"BRUTAL logo {w}x{h}: {yellow} yellow, {white} white, {clear} transparent pixels; splash {Splash.Total:0.0} s (skippable after {Splash.MinSeconds:0.0} s)");
            if (w != 712 || h != 168 || yellow < 5000 || white < 2000 || clear < 20000)
                Fail("the BRUTAL logo didn't decode as expected");
        }
        catch (Exception ex)
        {
            Fail($"the BRUTAL logo didn't decode: {ex.Message}");
        }
        if (Splash.MinSeconds < 1.0 || Splash.Total < 2.0)
            Fail("the splash must stay up long enough to be legible");

        // The PNG reader on a hand-made image: every row filter type, RGB and grey + alpha.
        var made = MakeTestPng();
        var (tw, th, px) = Png.Decode(made);
        var ok = tw == 3 && th == 5;
        for (var y = 0; y < 5 && ok; y++)
            for (var x = 0; x < 3 && ok; x++)
                ok = px[(y * 3 + x) * 4] == (byte)(x * 40 + y * 7) && px[(y * 3 + x) * 4 + 1] == (byte)(y * 50) && px[(y * 3 + x) * 4 + 3] == 255;
        if (!ok)
            Fail("the PNG reader got a filtered test image wrong");
    }

    /// <summary>A 3x5 RGB PNG whose rows use filters 0-4 in turn.</summary>
    private static byte[] MakeTestPng()
    {
        const int w = 3, h = 5, ch = 3;
        var img = new byte[w * h * ch];
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                img[(y * w + x) * ch] = (byte)(x * 40 + y * 7);
                img[(y * w + x) * ch + 1] = (byte)(y * 50);
                img[(y * w + x) * ch + 2] = (byte)(x * y);
            }
        var raw = new List<byte>();
        for (var y = 0; y < h; y++)
        {
            var f = y % 5;
            raw.Add((byte)f);
            for (var i = 0; i < w * ch; i++)
            {
                int v = img[y * w * ch + i];
                int a = i >= ch ? img[y * w * ch + i - ch] : 0;
                int b = y > 0 ? img[(y - 1) * w * ch + i] : 0;
                int c = i >= ch && y > 0 ? img[(y - 1) * w * ch + i - ch] : 0;
                var pr = f switch { 1 => a, 2 => b, 3 => (a + b) / 2, 4 => PaethRef(a, b, c), _ => 0 };
                raw.Add((byte)(v - pr));
            }
        }
        using var z = new MemoryStream();
        using (var zs = new System.IO.Compression.ZLibStream(z, System.IO.Compression.CompressionLevel.Optimal, true))
            zs.Write(raw.ToArray());
        var o = new List<byte> { 137, 80, 78, 71, 13, 10, 26, 10 };
        void Chunk(string type, byte[] data)
        {
            o.AddRange([(byte)(data.Length >> 24), (byte)(data.Length >> 16), (byte)(data.Length >> 8), (byte)data.Length]);
            o.AddRange(System.Text.Encoding.ASCII.GetBytes(type));
            o.AddRange(data);
            o.AddRange([0, 0, 0, 0]); // CRC (not checked by the reader)
        }
        Chunk("IHDR", [0, 0, 0, w, 0, 0, 0, h, 8, 2, 0, 0, 0]);
        Chunk("IDAT", z.ToArray());
        Chunk("IEND", []);
        return o.ToArray();
    }

    private static int PaethRef(int a, int b, int c)
    {
        var p = a + b - c;
        int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }
}
