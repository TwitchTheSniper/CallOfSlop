using Brutal.OpenGlApi;

namespace CallOfSlop;

/// <summary>
/// Screenshots for the picture tools (--perf-shot, --stress-shots, --wake-shots, the vehicle shots, the zombie range):
/// the finished frame read back and saved as a .bmp next to the log. (The old window smoke test, --smoketest, that these
/// came from was removed on 2026-11-17, Karl.)
/// </summary>
public static unsafe partial class Program
{
    /// <summary>The next finished frame is saved as smoke_NAME.bmp (the picture tools: --perf-shot, the zombie range).</summary>
    private static void ShotNext(string name) => _shotName = name;

    private static string? _shotName;

    /// <summary>The next screenshot is saved at full size (not halved).</summary>
    private static bool _shotFull;

    /// <summary>Reads the finished frame back and saves it (half size, 2 x 2 averaged) as smoke_NAME.bmp next to the game.</summary>
    private static void SaveScreenshot(string name)
    {
        var fb = _window.FramebufferSize;
        int w = fb.X, h = fb.Y;
        var pixels = new byte[w * h * 4];
        Gfx.Present(); // (the scene target's picture, if one is still bound: reversed depth)
        OpenGl.Native.glPixelStorei(OpenGl.GL_PACK_ALIGNMENT, 1);
        fixed (byte* p = pixels)
            OpenGl.Native.glReadPixels(0, 0, w, h, OpenGl.GL_RGBA, OpenGl.GL_UNSIGNED_BYTE, p);
        var full = _shotFull; // (--perf-shot: full size, so the small text reads)
        _shotFull = false;
        var div = full ? 1 : 2;
        int ow = w / div, oh = h / div;
        var rowBytes = (ow * 3 + 3) & ~3;
        using var f = new BinaryWriter(File.Create(AppFolders.LogPath($"smoke_{name}.bmp")));
        f.Write((byte)'B');
        f.Write((byte)'M');
        f.Write(54 + rowBytes * oh);
        f.Write(0);
        f.Write(54);
        f.Write(40);
        f.Write(ow);
        f.Write(oh); // positive height = bottom-up rows, same order glReadPixels gives
        f.Write((short)1);
        f.Write((short)24);
        f.Write(0);
        f.Write(rowBytes * oh);
        f.Write(2835);
        f.Write(2835);
        f.Write(0);
        f.Write(0);
        var row = new byte[rowBytes];
        for (var y = 0; y < oh; y++)
        {
            for (var x = 0; x < ow; x++)
            {
                // The average of each 2 x 2 block (not one pixel of it), so dithered see-through things (prop glass, faded
                // litter) show as they look at full size instead of aliasing to solid or empty.
                var i = ((y * div) * w + x * div) * 4;
                if (full)
                {
                    (row[x * 3], row[x * 3 + 1], row[x * 3 + 2]) = (pixels[i + 2], pixels[i + 1], pixels[i]);
                    continue;
                }
                var j = i + w * 4;
                row[x * 3] = (byte)((pixels[i + 2] + pixels[i + 6] + pixels[j + 2] + pixels[j + 6] + 2) >> 2);
                row[x * 3 + 1] = (byte)((pixels[i + 1] + pixels[i + 5] + pixels[j + 1] + pixels[j + 5] + 2) >> 2);
                row[x * 3 + 2] = (byte)((pixels[i] + pixels[i + 4] + pixels[j] + pixels[j + 4] + 2) >> 2);
            }
            f.Write(row);
        }
        Console.WriteLine($"[shot] screenshot smoke_{name}.bmp ({ow}x{oh})");
    }
}
