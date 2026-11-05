using System.Numerics;
using Brutal.OpenGlApi;
using static Brutal.OpenGlApi.OpenGl;

namespace CallOfSlop;

/// <summary>
/// Live pictures for screens (docs/sandbox/CONSTRUCTS.md, "Cameras and screens"). A camera renders only because a screen asked for it, and a screen
/// asks only while its panel is being filled: in range, facing the player and in view on this machine (<see cref="PanelRender"/>).
/// So a camera nobody is watching costs nothing, and nothing goes on the wire: each peer draws from its own copy of the world. The
/// pictures are layers of one small texture array. At most <see cref="MaxFeeds"/> cameras have a layer and
/// one renders a frame, the one longest due, each at most <see cref="RefreshHz"/> times a second, and less when a pass is slow.
/// </summary>
public static unsafe class CameraFeeds
{
    public const int Width = 256, Height = 144, MaxFeeds = 4;
    public const float RefreshHz = 10, WantSeconds = 0.6f; // (screens fill at 4 Hz, so a watched camera is asked for every 0.25 s)
    private const uint Unit = 15;
    private const float BudgetShare = 12; // at least this many times a pass's own time between passes

    private struct Feed
    {
        public ushort Body, Part;
        public double Wanted, Drawn;
        public bool Live;
    }

    private static readonly Feed[] Feeds = new Feed[MaxFeeds];
    private static bool _ready, _broken;
    private static GlTexture _tex;
    private static GlFramebuffer _fbo;
    private static double _now;

    public static long Passes; // cameras drawn since the start
    public static double LastMs; // the last pass's CPU time (the F9 window, a throwaway check)

    public static bool InPass;

    /// <summary>A screen wants this camera's picture. Its layer plus one, or 0: nothing drawn yet, or no layer free.</summary>
    public static int Request(Construct body, int part)
    {
        var free = -1;
        for (var k = 0; k < MaxFeeds; k++)
        {
            ref var f = ref Feeds[k];
            if (f.Live && f.Body == body.Id && f.Part == part)
            {
                f.Wanted = _now;
                return f.Drawn > 0 ? k + 1 : 0;
            }
            if (!f.Live || _now - f.Wanted > WantSeconds)
                free = k;
        }
        if (free >= 0)
            Feeds[free] = new Feed { Body = body.Id, Part = (ushort)part, Wanted = _now, Live = true };
        return 0;
    }

    /// <summary>The camera to draw this frame, or false: nothing wanted, or none due. <paramref name="layer"/> is its picture.</summary>
    public static bool Next(double now, out ushort body, out ushort part, out int layer)
    {
        _now = now;
        body = part = 0;
        layer = -1;
        var due = -1;
        var oldest = double.MaxValue;
        var gap = Math.Clamp(LastMs * 0.001 * BudgetShare, 1 / RefreshHz, 1.0); // (a slow pass waits longer, up to a second: a pass stays a small share of the time)
        for (var k = 0; k < MaxFeeds; k++)
        {
            ref var f = ref Feeds[k];
            if (!f.Live)
                continue;
            if (now - f.Wanted > WantSeconds)
            {
                f.Live = false; // (nobody near and looking: it stops, and its layer is free)
                continue;
            }
            if (now - f.Drawn >= gap && f.Drawn < oldest)
            {
                oldest = f.Drawn;
                due = k;
            }
        }
        if (due < 0)
            return false;
        body = Feeds[due].Body;
        part = Feeds[due].Part;
        layer = due;
        return true;
    }

    public static void Forget(int layer) => Feeds[layer].Live = false;

    public static void MarkDrawn(int layer, double now) => Feeds[layer].Drawn = now;

    /// <summary>Points drawing at a layer. False: no render targets on this machine (screens stay on "Waiting").</summary>
    public static bool Bind(int layer, out int previous)
    {
        int was;
        Native.glGetIntegerv(GL_DRAW_FRAMEBUFFER_BINDING, &was);
        previous = was;
        if (_broken || !_ready && !Init())
            return false;
        Native.glBindFramebuffer(GL_FRAMEBUFFER, _fbo);
        Native.glFramebufferTextureLayer(GL_FRAMEBUFFER, GL_COLOR_ATTACHMENT0, _tex.Id, 0, layer);
        return true;
    }

    public static void Unbind(int previous) => Native.glBindFramebuffer(GL_FRAMEBUFFER, new GlFramebuffer((uint)previous));

    private static bool Init()
    {
        GlTexture t = default;
        Native.glGenTextures(1, &t);
        _tex = t;
        Native.glActiveTexture(GL_TEXTURE0 + Unit);
        Native.glBindTexture(GL_TEXTURE_2D_ARRAY, t.Id);
        Native.glTexImage3D(GL_TEXTURE_2D_ARRAY, 0, (int)GL_RGBA8, Width, Height, MaxFeeds, 0, GL_RGBA, GL_UNSIGNED_BYTE, null);
        Native.glTexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_MIN_FILTER, (int)GL_LINEAR);
        Native.glTexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_MAG_FILTER, (int)GL_LINEAR);
        Native.glTexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_WRAP_S, (int)GL_CLAMP_TO_EDGE);
        Native.glTexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_WRAP_T, (int)GL_CLAMP_TO_EDGE);
        Native.glActiveTexture(GL_TEXTURE0);
        GlFramebuffer fb = default;
        Native.glGenFramebuffers(1, &fb);
        _fbo = fb;
        GlRenderbuffer rb = default;
        Native.glGenRenderbuffers(1, &rb);
        Native.glBindRenderbuffer(GL_RENDERBUFFER, rb);
        Native.glRenderbufferStorage(GL_RENDERBUFFER, GL_DEPTH_COMPONENT24, Width, Height);
        Native.glBindFramebuffer(GL_FRAMEBUFFER, fb);
        Native.glFramebufferRenderbuffer(GL_FRAMEBUFFER, GL_DEPTH_ATTACHMENT, GL_RENDERBUFFER, rb);
        Native.glFramebufferTextureLayer(GL_FRAMEBUFFER, GL_COLOR_ATTACHMENT0, t.Id, 0, 0);
        if ((uint)Native.glCheckFramebufferStatus(GL_FRAMEBUFFER) != (uint)GL_FRAMEBUFFER_COMPLETE)
        {
            _broken = true;
            Console.WriteLine("[cameras] off: the render target isn't complete");
            return false;
        }
        _ready = true;
        return true;
    }
}
