using Brutal.OpenGlApi;
using static Brutal.OpenGlApi.OpenGl;

namespace CallOfSlop;

/// <summary>
/// The browser's pictures for the panels (docs/sandbox/MEDIA.md). As with <see cref="CameraFeeds"/>, a page's frames are only copied while a panel
/// is being filled, which means someone is near and looking. The pictures are layers of one array, made the first time a screen shows
/// anything; at most one layer is copied a frame, straight from the shared memory, and only if the browser made a new frame.
/// </summary>
public static unsafe class VideoFeeds
{
    public const float WantSeconds = 0.6f; // (panels fill at 4 Hz)
    public const uint Unit = 3; // (0: the box texture array, 1-2 and 4-11: other renderers, 12-14: the light grid, 15: cameras)

    private static readonly double[] Wanted = new double[MediaDirector.MaxPages];
    private static readonly bool[] Shown = new bool[MediaDirector.MaxPages];
    private static bool _ready, _broken;
    private static GlTexture _tex;
    private static int _next;
    private static double _now;

    public static long Uploads; // frames copied since the start
    public static double LastMs; // the last copy's CPU time

    /// <summary>A panel wants page <paramref name="page"/>'s picture. Its layer plus one, or 0: nothing to show yet.</summary>
    public static int Request(int page)
    {
        Wanted[page] = _now;
        return Shown[page] ? page + 1 : 0;
    }

    /// <summary>The page has a new track: its old picture isn't shown.</summary>
    public static void Forget(int page) => Shown[page] = false;

    /// <summary>Each frame, before the world's pass: one wanted page's newest frame goes up.</summary>
    public static void Upload(double now)
    {
        _now = now;
        if (_broken)
            return;
        for (var k = 0; k < Wanted.Length; k++)
        {
            var page = (_next + k) % Wanted.Length;
            if (now - Wanted[page] > WantSeconds || MediaBrowser.Block(page) is not { } b)
                continue;
            var pixels = b.TakeFrame();
            if (pixels == null)
                continue;
            if (!_ready && !Init())
                return;
            var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            Native.glActiveTexture(GL_TEXTURE0 + Unit);
            Native.glBindTexture(GL_TEXTURE_2D_ARRAY, _tex.Id);
            Native.glTexSubImage3D(GL_TEXTURE_2D_ARRAY, 0, 0, 0, page, b.Width, b.Height, 1, GL_BGRA, GL_UNSIGNED_BYTE, pixels);
            Native.glActiveTexture(GL_TEXTURE0);
            Shown[page] = true;
            Uploads++;
            LastMs = System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
            _next = page + 1;
            return;
        }
    }

    /// <summary>Before the panels' draw, if a video may be in it: other renderers use the units too.</summary>
    public static void Bind()
    {
        if (!_ready)
            return;
        Native.glActiveTexture(GL_TEXTURE0 + Unit);
        Native.glBindTexture(GL_TEXTURE_2D_ARRAY, _tex.Id);
        Native.glActiveTexture(GL_TEXTURE0);
    }

    private static bool Init()
    {
        GlTexture t = default;
        Native.glGenTextures(1, &t);
        _tex = t;
        Native.glActiveTexture(GL_TEXTURE0 + Unit);
        Native.glBindTexture(GL_TEXTURE_2D_ARRAY, t.Id);
        Native.glTexImage3D(GL_TEXTURE_2D_ARRAY, 0, (int)GL_RGBA8, MediaBlock.MaxWidth, MediaBlock.MaxHeight, MediaDirector.MaxPages, 0, GL_RGBA, GL_UNSIGNED_BYTE, null);
        Native.glTexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_MIN_FILTER, (int)GL_LINEAR);
        Native.glTexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_MAG_FILTER, (int)GL_LINEAR);
        Native.glTexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_WRAP_S, (int)GL_CLAMP_TO_EDGE);
        Native.glTexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_WRAP_T, (int)GL_CLAMP_TO_EDGE);
        Native.glActiveTexture(GL_TEXTURE0);
        if (Native.glGetError() != GL_NO_ERROR)
        {
            _broken = true;
            Console.WriteLine("[media] pictures off: no room for the texture");
            return false;
        }
        _ready = true;
        return true;
    }
}
