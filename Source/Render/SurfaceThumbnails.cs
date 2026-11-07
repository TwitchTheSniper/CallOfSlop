using System.Diagnostics;
using System.Numerics;
using Brutal.OpenGlApi;
using static Brutal.OpenGlApi.OpenGl;

namespace CallOfSlop;

/// <summary>
/// Little pictures of the shared materials (docs/render/MATERIALS.md) for the texture paint tool's list (UiSandboxTexture.cs):
/// a block with a bevelled plate on it, drawn by the real box shader with the material's row, so the swatch is what a part
/// will look like. The same pattern as <see cref="PartThumbs"/>: <see cref="Get"/> queues a row it hasn't drawn yet (no GL
/// work while the UI is being built) and <see cref="Pump"/>, at the start of the next frame, draws a few. Cached for the
/// session; if the framebuffer can't be made the list shows plain tiles.
/// </summary>
public static unsafe class SurfaceThumbs
{
    public const int Size = 96;
    public const int PerFrame = 4;

    /// <summary>The colour a swatch is drawn in: a mid grey, so a material that keeps the part's colour shows its own pattern and shine.</summary>
    public static readonly Vector3 Grey = new(0.62f, 0.64f, 0.66f);

    public static readonly Vector3 Backdrop = new(0.075f, 0.085f, 0.105f);

    private static readonly uint[] Ready = new uint[Surfaces.MaxRows];
    private static readonly bool[] Queued = new bool[Surfaces.MaxRows];
    private static readonly List<int> Queue = new(Surfaces.MaxRows);
    private static GlFramebuffer _fbo;
    private static bool _glReady, _glBroken;
    private static double _ms;

    public static int Rendered { get; private set; }

    /// <summary>The picture of material row <paramref name="row"/> (a GL texture name), or 0 while it's queued (or if pictures can't be made).</summary>
    public static uint Get(int row)
    {
        if ((uint)row >= Surfaces.MaxRows)
            return 0;
        if (Ready[row] != 0)
            return Ready[row];
        if (!_glBroken && !Queued[row])
        {
            Queued[row] = true;
            Queue.Add(row);
        }
        return 0;
    }

    /// <summary>Draws up to <see cref="PerFrame"/> queued swatches (call before the scene: it changes framebuffer, viewport and shader state).</summary>
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
                var row = Queue[^1];
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
                Render(gfx, row);
                Native.glFramebufferTexture2D(GL_FRAMEBUFFER, GL_COLOR_ATTACHMENT0, GL_TEXTURE_2D, 0, 0);
                Native.glBindTexture(GL_TEXTURE_2D, tex);
                Native.glGenerateMipmap(GL_TEXTURE_2D);
                Native.glBindTexture(GL_TEXTURE_2D, 0);
                Ready[row] = tex;
                n++;
            }
        }
        catch (Exception ex)
        {
            _glBroken = true;
            Queue.Clear();
            Console.WriteLine($"[textures] material swatches off ({ex.Message}): the list shows plain tiles");
        }
        finally
        {
            LightGrid.Swatches(false);
            Native.glBindFramebuffer(GL_FRAMEBUFFER, new GlFramebuffer((uint)prevFbo));
            Native.glViewport(vp[0], vp[1], vp[2], vp[3]);
            gfx.Rebind();
        }
        Rendered += n;
        _ms += sw.Elapsed.TotalMilliseconds;
        var err = GlDebug.Error();
        if (err != 0)
            Console.WriteLine($"[textures] GL error 0x{err:X} after material swatches");
        if (Queue.Count == 0 && n > 0)
            Console.WriteLine($"[textures] {Rendered} material swatch(es) cached ({Size}x{Size}), {_ms:0.0} ms in all");
    }

    /// <summary>A block seen from above and to one side, a thinner plate on its top: three faces, box edges for the wear, and the light across them.</summary>
    private static void Render(Renderer gfx, int row)
    {
        gfx.BeginFrame(Size, Size, Backdrop);
        SkyRenderer.UseDefaultLight();
        LightGrid.Swatches(true);
        var eye = new Vector3(1.05f, 0.95f, 1.25f);
        gfx.SetCamera(Matrix4x4.CreateLookAt(eye, new Vector3(0, -0.05f, 0), Vector3.UnitY), Matrix4x4.CreatePerspectiveFieldOfView(0.62f, 1f, 0.05f, 20f));
        var code = Surfaces.Code(row, 0f);
        gfx.Draw(Matrix4x4.CreateScale(1.0f, 0.5f, 1.0f) * Matrix4x4.CreateTranslation(0, -0.3f, 0), Grey, 1f, mode: 0, surface: code);
        gfx.Draw(Matrix4x4.CreateScale(0.7f, 0.08f, 0.7f) * Matrix4x4.CreateTranslation(0, -0.012f, 0), Grey * 0.92f, 1f, mode: 0, surface: code);
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
            Native.glRenderbufferStorage(GL_RENDERBUFFER, GL_DEPTH_COMPONENT24, Size, Size);
            Native.glBindRenderbuffer(GL_RENDERBUFFER, new GlRenderbuffer(0));
            int prev;
            Native.glGetIntegerv(GL_FRAMEBUFFER_BINDING, &prev);
            Native.glBindFramebuffer(GL_FRAMEBUFFER, fbo);
            Native.glFramebufferRenderbuffer(GL_FRAMEBUFFER, GL_DEPTH_ATTACHMENT, GL_RENDERBUFFER, depth);
            Native.glBindFramebuffer(GL_FRAMEBUFFER, new GlFramebuffer((uint)prev));
            _fbo = fbo;
            var err = GlDebug.Error();
            if (fbo.Id == 0 || depth.Id == 0 || err != 0)
                throw new InvalidOperationException($"framebuffer {fbo.Id}, depth buffer {depth.Id}, GL error 0x{err:X}");
            _glReady = true;
        }
        catch (Exception ex)
        {
            _glBroken = true;
            Console.WriteLine($"[textures] no material swatch framebuffer ({ex.Message}): plain tiles instead");
        }
    }

    private static uint MakeTexture()
    {
        GlTexture tex = default;
        Native.glGenTextures(1, &tex);
        Native.glBindTexture(GL_TEXTURE_2D, tex.Id);
        Native.glTexImage2D(GL_TEXTURE_2D, 0, (int)GL_RGBA8, Size, Size, 0, GL_RGBA, GL_UNSIGNED_BYTE, null);
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
