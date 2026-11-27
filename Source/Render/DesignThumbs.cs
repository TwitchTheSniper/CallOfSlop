using System.Diagnostics;
using System.Numerics;
using Brutal.OpenGlApi;
using static Brutal.OpenGlApi.OpenGl;
using CallOfSlop.ShipDesigner;

namespace CallOfSlop;

/// <summary>
/// The Ship Designer's palette pictures (Karl, 2026-11-27): every piece kind (<see cref="PieceKinds.All"/>) drawn once from its own
/// mesh at a 3/4 view, in its own frame (up off the skin), with the yard's renderer and light, into one texture atlas of
/// <see cref="Tile"/> pixel tiles. A tile is drawn at twice its size into a small framebuffer and blitted down (smooth edges), as
/// the Sandbox inventory's tiles are (SandboxTiles.cs). <see cref="Want"/> (the Pieces panel) starts it and <see cref="Pump"/>, at
/// the start of a frame before the scene, draws a few each frame. A piece whose mesh can't be made, or a PC that can't make
/// pictures, keeps its plain name card: one <c>[design thumbs]</c> line in callofslop.log says why.
/// </summary>
public static unsafe class DesignThumbs
{
    public const int Tile = 128, Render = Tile * 2, Columns = 8;

    /// <summary>Tiles drawn a frame at most, and the time budget a frame (ms).</summary>
    private const int PerFrame = 6;
    private const double BudgetMs = 3.0;

    private static bool _wanted, _glReady, _broken;
    private static int _next;
    private static bool[] _ready = [];
    private static GlFramebuffer _tileFbo, _atlasFbo;
    private static uint _tileTex, _atlas;
    private static GlRenderbuffer _depth;
    private static int _atlasW, _atlasH;
    private static ShipDesign? _yard;

    /// <summary>The palette is on screen: make the pictures still missing over the next frames.</summary>
    public static void Want() => _wanted = true;

    /// <summary>A piece kind's picture: the atlas and its corners (top-left, bottom-right, for ImGui). False until it is made.</summary>
    public static bool TryGet(string kind, out uint tex, out Vector2 uv0, out Vector2 uv1)
    {
        tex = 0;
        uv0 = uv1 = default;
        var i = Array.FindIndex(PieceKinds.All, k => k.Name == kind);
        if (i < 0 || i >= _ready.Length || !_ready[i] || _atlas == 0)
            return false;
        Place(i, out var x, out var y);
        tex = _atlas;
        uv0 = new Vector2(x / (float)_atlasW, (y + Tile) / (float)_atlasH); // (GL rows go up: the tile's top is y + Tile)
        uv1 = new Vector2((x + Tile) / (float)_atlasW, y / (float)_atlasH);
        return true;
    }

    private static void Place(int i, out int x, out int y)
    {
        x = i % Columns * Tile;
        y = _atlasH - (i / Columns + 1) * Tile;
    }

    /// <summary>Draws the next few missing pictures while the palette wants them (before the scene: it changes framebuffer, viewport and shader state).</summary>
    public static void Pump(Renderer gfx)
    {
        if (!_wanted || _broken)
            return;
        _wanted = false;
        var count = PieceKinds.All.Length;
        if (_next >= count)
            return;
        if (!_glReady && !InitGl(count))
            return;
        var sw = Stopwatch.StartNew();
        int prevFbo;
        Native.glGetIntegerv(GL_FRAMEBUFFER_BINDING, &prevFbo);
        var vp = stackalloc int[4];
        Native.glGetIntegerv(GL_VIEWPORT, vp);
        var n = 0;
        try
        {
            while (_next < count && n < PerFrame && (n == 0 || sw.Elapsed.TotalMilliseconds < BudgetMs))
            {
                var i = _next++;
                var obj = PieceObj(PieceKinds.All[i]);
                if (obj == null)
                    continue;
                Native.glBindFramebuffer(GL_FRAMEBUFFER, _tileFbo);
                var (view, proj, eye) = Camera(obj.Geo);
                ShipDesignerRenderer.Draw(gfx, [obj], view, proj, eye, 0, 0, Render, Render, Vector3.Zero, 0);
                ShipDesignerRenderer.Free(obj.Geo);
                Place(i, out var x, out var y);
                Native.glBindFramebuffer(GL_READ_FRAMEBUFFER, _tileFbo);
                Native.glBindFramebuffer(GL_DRAW_FRAMEBUFFER, _atlasFbo);
                Native.glBlitFramebuffer(0, 0, Render, Render, x, y, x + Tile, y + Tile, GL_COLOR_BUFFER_BIT, GL_LINEAR);
                _ready[i] = true;
                n++;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[design thumbs] pictures off: {ex.Message}");
            _broken = true;
        }
        finally
        {
            Native.glBindFramebuffer(GL_FRAMEBUFFER, new GlFramebuffer((uint)prevFbo));
            Native.glViewport(vp[0], vp[1], vp[2], vp[3]);
            gfx.Rebind();
        }
        if (_next >= count && _yard != null)
        {
            _yard = null; // (all made: the throwaway design goes)
            var made = _ready.Count(r => r);
            Console.WriteLine($"[design thumbs] {made} of {count} piece pictures made");
        }
    }

    /// <summary>
    /// A piece kind's mesh in its own frame (x across, y up off the skin, z along), as the yard draws it: the piece is put on a
    /// throwaway blank hull, its mesh made by <see cref="DesignScene.PieceMesh"/>, then taken back out of the hull's frame.
    /// </summary>
    private static DesignObj? PieceObj(PieceKind kind)
    {
        try
        {
            var d = _yard ??= DesignTemplates.Start("Blank hull");
            var h = d.Hulls[0];
            d.Pieces.Clear();
            var p = new Piece { Id = d.NewId(), Kind = kind.Name, Hull = h.Id, Pair = false, Surface = kind.Where == "skin" ? "skin" : "fore" };
            d.Pieces.Add(p);
            DesignHangar.Init(d, p);
            DesignSeats.Init(d, p);
            var w = DesignSeats.PlaceOf(d, p) ?? DesignHangar.PlaceOf(d, p) ?? DesignScene.Where(h, p);
            var m = DesignScene.PieceMesh(d, p, copies: false, shut: true);
            var nv = m.P.Count / 3;
            if (nv == 0)
                return null;
            var g = new DesignGeo { P = new float[nv * 3], C = new uint[nv] };
            for (var v = 0; v < nv; v++)
            {
                double dx = m.P[v * 3] - w.O[0], dy = m.P[v * 3 + 1] - w.O[1], dz = m.P[v * 3 + 2] - w.O[2];
                g.P[v * 3] = (float)(dx * w.X[0] + dy * w.X[1] + dz * w.X[2]);
                g.P[v * 3 + 1] = (float)(dx * w.Y[0] + dy * w.Y[1] + dz * w.Y[2]);
                g.P[v * 3 + 2] = (float)(dx * w.Z[0] + dy * w.Z[1] + dz * w.Z[2]);
                if (v * 3 + 2 < m.C.Count)
                    g.C[v] = m.C[v * 3] | ((uint)m.C[v * 3 + 1] << 8) | ((uint)m.C[v * 3 + 2] << 16) | 0xff000000u;
            }
            g.Bound();
            return new DesignObj { Geo = g, VertexColours = true };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[design thumbs] no picture for {kind.Name}: {ex.Message}");
            return null;
        }
    }

    /// <summary>A 3/4 view from above the skin, an orthographic box fitted round every vertex (square, a margin round it).</summary>
    private static (Matrix4x4 View, Matrix4x4 Proj, Vector3 Eye) Camera(DesignGeo g)
    {
        var c = g.Centre;
        var r = MathF.Max(0.05f, (g.Max - g.Min).Length() * 0.5f);
        var dir = Vector3.Normalize(new Vector3(0.75f, 0.85f, 1.0f));
        var eye = c + dir * r * 4;
        var view = Matrix4x4.CreateLookAt(eye, c, Vector3.UnitY);
        float x0 = float.MaxValue, x1 = float.MinValue, y0 = float.MaxValue, y1 = float.MinValue;
        for (var i = 0; i + 2 < g.P.Length; i += 3)
        {
            var v = Vector3.Transform(new Vector3(g.P[i], g.P[i + 1], g.P[i + 2]), view);
            x0 = MathF.Min(x0, v.X); x1 = MathF.Max(x1, v.X);
            y0 = MathF.Min(y0, v.Y); y1 = MathF.Max(y1, v.Y);
        }
        var half = MathF.Max(x1 - x0, y1 - y0) * 0.5f * 1.1f;
        float mx = (x0 + x1) * 0.5f, my = (y0 + y1) * 0.5f;
        var proj = Matrix4x4.CreateOrthographicOffCenter(mx - half, mx + half, my - half, my + half, 0.01f, r * 8);
        return (view, proj, eye);
    }

    private static bool InitGl(int count)
    {
        GlDebug.Error(); // (an error left by earlier drawing is not ours)
        try
        {
            _atlasW = Columns * Tile;
            _atlasH = (count + Columns - 1) / Columns * Tile;
            _ready = new bool[count];
            GlFramebuffer tileFbo, atlasFbo;
            Native.glGenFramebuffers(1, &tileFbo);
            Native.glGenFramebuffers(1, &atlasFbo);
            GlTexture tex = default, atlas = default;
            Native.glGenTextures(1, &tex);
            Native.glBindTexture(GL_TEXTURE_2D, tex.Id);
            Native.glTexImage2D(GL_TEXTURE_2D, 0, (int)GL_RGBA8, Render, Render, 0, GL_RGBA, GL_UNSIGNED_BYTE, null);
            Native.glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, (int)GL_LINEAR);
            Native.glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, (int)GL_LINEAR);
            Native.glGenTextures(1, &atlas);
            Native.glBindTexture(GL_TEXTURE_2D, atlas.Id);
            Native.glTexImage2D(GL_TEXTURE_2D, 0, (int)GL_RGBA8, _atlasW, _atlasH, 0, GL_RGBA, GL_UNSIGNED_BYTE, null);
            Native.glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, (int)GL_LINEAR);
            Native.glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, (int)GL_LINEAR);
            Native.glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, (int)GL_CLAMP_TO_EDGE);
            Native.glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, (int)GL_CLAMP_TO_EDGE);
            Native.glBindTexture(GL_TEXTURE_2D, 0);
            GlRenderbuffer depth;
            Native.glGenRenderbuffers(1, &depth);
            Native.glBindRenderbuffer(GL_RENDERBUFFER, depth);
            Native.glRenderbufferStorage(GL_RENDERBUFFER, GL_DEPTH_COMPONENT24, Render, Render);
            Native.glBindRenderbuffer(GL_RENDERBUFFER, new GlRenderbuffer(0));
            int prev;
            Native.glGetIntegerv(GL_FRAMEBUFFER_BINDING, &prev);
            Native.glBindFramebuffer(GL_FRAMEBUFFER, tileFbo);
            Native.glFramebufferTexture2D(GL_FRAMEBUFFER, GL_COLOR_ATTACHMENT0, GL_TEXTURE_2D, tex.Id, 0);
            Native.glFramebufferRenderbuffer(GL_FRAMEBUFFER, GL_DEPTH_ATTACHMENT, GL_RENDERBUFFER, depth);
            var status = (uint)Native.glCheckFramebufferStatus(GL_FRAMEBUFFER);
            Native.glBindFramebuffer(GL_FRAMEBUFFER, atlasFbo);
            Native.glFramebufferTexture2D(GL_FRAMEBUFFER, GL_COLOR_ATTACHMENT0, GL_TEXTURE_2D, atlas.Id, 0);
            var status2 = (uint)Native.glCheckFramebufferStatus(GL_FRAMEBUFFER);
            var vp = stackalloc int[4];
            Native.glGetIntegerv(GL_VIEWPORT, vp);
            Native.glViewport(0, 0, _atlasW, _atlasH);
            Native.glClearColor(0, 0, 0, 0);
            Native.glClear(GL_COLOR_BUFFER_BIT);
            Native.glViewport(vp[0], vp[1], vp[2], vp[3]);
            Native.glBindFramebuffer(GL_FRAMEBUFFER, new GlFramebuffer((uint)prev));
            var err = GlDebug.Error();
            if (tileFbo.Id == 0 || atlasFbo.Id == 0 || tex.Id == 0 || atlas.Id == 0 || depth.Id == 0 || err != 0
                || status != GL_FRAMEBUFFER_COMPLETE || status2 != GL_FRAMEBUFFER_COMPLETE)
                throw new InvalidOperationException($"framebuffers {tileFbo.Id}/{atlasFbo.Id}, status 0x{status:X}/0x{status2:X}, GL error 0x{err:X}");
            (_tileFbo, _atlasFbo, _tileTex, _atlas, _depth) = (tileFbo, atlasFbo, tex.Id, atlas.Id, depth);
            _glReady = true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[design thumbs] pictures off: {ex.Message}");
            _broken = true;
        }
        return _glReady;
    }
}
