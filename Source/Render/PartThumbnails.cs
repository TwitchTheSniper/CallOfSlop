using System.Diagnostics;
using System.Numerics;
using Brutal.OpenGlApi;
using static Brutal.OpenGlApi.OpenGl;

namespace CallOfSlop;

/// <summary>
/// Little 3D pictures of weapon parts for the workbench (the inventory icons and the grip picker's cards), rendered with
/// the gun renderer into offscreen textures and cached for the whole session (part files don't change while the game
/// runs). Same pattern as <see cref="MapThumbs"/>: <see cref="Get"/> queues a part it hasn't drawn yet (no GL work while
/// the UI is being built), and <see cref="Pump"/>, at the start of the next frame, draws a few queued parts (each is a
/// handful of boxes, well under a millisecond). If the framebuffer can't be made, the UI draws a plain tile instead.
/// </summary>
public static unsafe class PartThumbs
{
    /// <summary>Picture size in pixels (wider than tall: most parts are long).</summary>
    public const int Width = 160, Height = 112;

    /// <summary>Parts drawn per frame at most.</summary>
    public const int PerFrame = 8;

    /// <summary>The colour behind each part (the workbench's card colour, so icons sit on their tiles).</summary>
    public static readonly Vector3 Backdrop = new(0.075f, 0.085f, 0.105f);

    private static readonly Dictionary<PartDef, uint> Ready = new(ReferenceEqualityComparer.Instance);
    private static readonly HashSet<PartDef> Queued = new(ReferenceEqualityComparer.Instance);
    private static readonly List<PartDef> Queue = new();
    private static GlFramebuffer _fbo;
    private static bool _glReady, _glBroken;
    private static double _ms;

    /// <summary>Pictures drawn so far, and the time they took (log and smoke test).</summary>
    public static int Rendered { get; private set; }

    public static bool Broken => _glBroken;

    /// <summary>The picture of <paramref name="p"/> (a GL texture name), or 0 while it's queued (or if pictures can't be made).</summary>
    public static uint Get(PartDef p)
    {
        if (Ready.TryGetValue(p, out var tex))
            return tex;
        if (!_glBroken && Queued.Add(p))
            Queue.Add(p);
        return 0;
    }

    /// <summary>Draws up to <see cref="PerFrame"/> queued pictures (call before the scene: it changes framebuffer, viewport and shader state).</summary>
    public static void Pump(Renderer gfx)
    {
        if (Queue.Count == 0)
            return;
        if (!_glReady && !_glBroken)
            InitGl();
        if (_glBroken)
        {
            Queue.Clear();
            return;
        }
        var sw = Stopwatch.StartNew();
        int prevFbo;
        Native.glGetIntegerv(GL_FRAMEBUFFER_BINDING, &prevFbo);
        var vp = stackalloc int[4];
        Native.glGetIntegerv(GL_VIEWPORT, vp);
        var n = 0;
        try
        {
            Native.glBindFramebuffer(GL_FRAMEBUFFER, _fbo);
            while (Queue.Count > 0 && n < PerFrame)
            {
                var p = Queue[^1];
                Queue.RemoveAt(Queue.Count - 1);
                var tex = MakeTexture();
                Native.glFramebufferTexture2D(GL_FRAMEBUFFER, GL_COLOR_ATTACHMENT0, GL_TEXTURE_2D, tex, 0);
                var status = (uint)Native.glCheckFramebufferStatus(GL_FRAMEBUFFER);
                if (status != GL_FRAMEBUFFER_COMPLETE)
                {
                    var t = new GlTexture(tex);
                    Native.glDeleteTextures(1, &t);
                    throw new InvalidOperationException($"framebuffer incomplete (0x{status:X})");
                }
                Render(gfx, p);
                Native.glFramebufferTexture2D(GL_FRAMEBUFFER, GL_COLOR_ATTACHMENT0, GL_TEXTURE_2D, 0, 0);
                Native.glBindTexture(GL_TEXTURE_2D, tex);
                Native.glGenerateMipmap(GL_TEXTURE_2D);
                Native.glBindTexture(GL_TEXTURE_2D, 0);
                Ready[p] = tex;
                n++;
            }
        }
        catch (Exception ex)
        {
            _glBroken = true;
            Queue.Clear();
            Console.WriteLine($"[workbench] part pictures off ({ex.Message}): the workbench shows plain tiles");
        }
        finally
        {
            Native.glBindFramebuffer(GL_FRAMEBUFFER, new GlFramebuffer((uint)prevFbo));
            Native.glViewport(vp[0], vp[1], vp[2], vp[3]);
            gfx.Rebind();
        }
        Rendered += n;
        _ms += sw.Elapsed.TotalMilliseconds;
        var err = (uint)Native.glGetError();
        if (err != 0)
            Console.WriteLine($"[workbench] GL error 0x{err:X} after part pictures");
        if (Queue.Count == 0 && n > 0)
            Console.WriteLine($"[workbench] {Rendered} part picture(s) cached ({Width}x{Height}), {_ms:0.0} ms in all");
    }

    /// <summary>The camera for a part's picture: a 3/4 view from above-left, framing its boxes.</summary>
    public static BenchCamera CameraFor(GunMesh? mesh)
    {
        var cam = BenchCamera.Default;
        cam.Yaw = 0.55f;
        cam.Pitch = 0.32f;
        cam.Frame(mesh);
        cam.Zoom = 1.05f;
        return cam;
    }

    private static void Render(Renderer gfx, PartDef p)
    {
        gfx.BeginFrame(Width, Height, Backdrop);
        SkyRenderer.UseDefaultLight(); // lit the same whatever the map's sky
        var asm = WeaponBuilder.Assemble(new WeaponBuild(p.Name, p));
        if (asm.PreviewDef is not { } def)
            return;
        var cam = CameraFor(def.BuiltMesh);
        cam.Matrices(Width / (float)Height, out var pose, out var view, out var proj);
        gfx.SetCamera(view, proj);
        GunModel.Draw(gfx, def, pose, false);
    }

    private static void InitGl()
    {
        try
        {
            GlFramebuffer fbo;
            Native.glGenFramebuffers(1, &fbo);
            GlRenderbuffer depth;
            Native.glGenRenderbuffers(1, &depth);
            Native.glBindRenderbuffer(GL_RENDERBUFFER, depth);
            Native.glRenderbufferStorage(GL_RENDERBUFFER, GL_DEPTH_COMPONENT24, Width, Height);
            Native.glBindRenderbuffer(GL_RENDERBUFFER, new GlRenderbuffer(0));
            int prev;
            Native.glGetIntegerv(GL_FRAMEBUFFER_BINDING, &prev);
            Native.glBindFramebuffer(GL_FRAMEBUFFER, fbo);
            Native.glFramebufferRenderbuffer(GL_FRAMEBUFFER, GL_DEPTH_ATTACHMENT, GL_RENDERBUFFER, depth);
            Native.glBindFramebuffer(GL_FRAMEBUFFER, new GlFramebuffer((uint)prev));
            _fbo = fbo;
            var err = (uint)Native.glGetError();
            if (fbo.Id == 0 || depth.Id == 0 || err != 0)
                throw new InvalidOperationException($"framebuffer {fbo.Id}, depth buffer {depth.Id}, GL error 0x{err:X}");
            _glReady = true;
        }
        catch (Exception ex)
        {
            _glBroken = true;
            Console.WriteLine($"[workbench] no part picture framebuffer ({ex.Message}): plain tiles instead");
        }
    }

    private static uint MakeTexture()
    {
        GlTexture tex = default;
        Native.glGenTextures(1, &tex);
        Native.glBindTexture(GL_TEXTURE_2D, tex.Id);
        Native.glTexImage2D(GL_TEXTURE_2D, 0, (int)GL_RGBA8, Width, Height, 0, GL_RGBA, GL_UNSIGNED_BYTE, null);
        Native.glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, (int)GL_LINEAR_MIPMAP_LINEAR);
        Native.glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, (int)GL_LINEAR);
        Native.glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, (int)GL_CLAMP_TO_EDGE);
        Native.glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, (int)GL_CLAMP_TO_EDGE);
        Native.glBindTexture(GL_TEXTURE_2D, 0);
        if (tex.Id == 0)
            throw new InvalidOperationException("glGenTextures gave no texture");
        return tex.Id;
    }
}
