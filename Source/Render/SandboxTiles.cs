using System.Diagnostics;
using System.Numerics;
using Brutal.OpenGlApi;
using static Brutal.OpenGlApi.OpenGl;

namespace CallOfSlop;

/// <summary>One box of a tile's picture: its model matrix (the unit cube scaled, turned and placed) and its colour.</summary>
public readonly record struct TileBox(Matrix4x4 Model, Vector3 Color);

/// <summary>
/// The Sandbox inventory's 3D tiles (UiSandboxParts.cs; docs/sandbox/CONSTRUCTS.md, "The inventory"): every construct
/// part, every blueprint (built, as the server would build it) and every tool model, drawn from its boxes at a 3/4 view
/// with the default light into square tiles of a texture atlas (pages of <see cref="PageSize"/> pixels, <see cref="Tile"/>
/// pixels a tile). A tile is drawn at twice its size into a small offscreen framebuffer and blitted down into its page
/// (smooth edges), the same on the GL 4.1 and 4.6 paths (plain framebuffer objects and blits, nothing newer).
/// <para>
/// The atlas is made once and kept: <see cref="Want"/> (the open window calls it) starts it, and <see cref="Pump"/>, at
/// the start of a frame before the scene, draws a few tiles each frame (a time budget, so opening the window never hitches
/// a frame badly). It is made again only when the parts, blueprints or tools change (<see cref="Key"/>). Until a tile is
/// ready (or if pictures can't be made at all) the window draws a plain swatch in its place.
/// </para>
/// Slots: parts by id, then blueprints by index, then tool models by index (<see cref="SlotOf"/>).
/// </summary>
public static unsafe class SandboxTiles
{
    /// <summary>A tile in the atlas (pixels), and the size it is drawn at before it is blitted down.</summary>
    public const int Tile = 128, Render = Tile * 2;

    /// <summary>An atlas page's side (pixels): <see cref="PerPage"/> tiles a page.</summary>
    public const int PageSize = 2048, Columns = PageSize / Tile, PerPage = Columns * Columns;

    /// <summary>Tiles drawn a frame at most, and the time budget a frame (ms): it stops early when it runs over.</summary>
    public const int PerFrame = 12;

    public const double BudgetMs = 3.0;

    public enum Kind : byte
    {
        Part,
        Blueprint,
        Tool,
    }

    private static uint[] _pages = [];
    private static bool[] _ready = [];
    private static int _key, _next, _count;
    private static bool _wanted, _glReady, _glBroken;
    private static GlFramebuffer _tileFbo, _pageFbo;
    private static uint _tileTex;
    private static double _ms;
    private static readonly List<TileBox> Scratch = new(256);

    /// <summary>Tiles drawn since start-up, and pages made (smoke log, self-test).</summary>
    public static int Rendered { get; private set; }

    public static int Pages => _pages.Length;

    /// <summary>Pictures can't be made on this PC (the window draws plain swatches).</summary>
    public static bool Broken => _glBroken;

    /// <summary>Every slot's tile is in the atlas.</summary>
    public static bool Complete => _count > 0 && _next >= _count && _key == Key();

    /// <summary>How many tiles there are: every part, blueprint and tool model.</summary>
    public static int Count => ConstructParts.All.Count + ConstructBuild.Blueprints.Count + ConstructTools.All.Count;

    /// <summary>The slot of a part (by id), a blueprint or a tool model (by index).</summary>
    public static int SlotOf(Kind kind, int index) => kind switch
    {
        Kind.Part => index,
        Kind.Blueprint => ConstructParts.All.Count + index,
        _ => ConstructParts.All.Count + ConstructBuild.Blueprints.Count + index,
    };

    /// <summary>Where a slot sits: its page and its tile's pixel corner in the page (GL rows: y up from the bottom).</summary>
    public static void Place(int slot, out int page, out int x, out int y)
    {
        page = slot / PerPage;
        var k = slot % PerPage;
        x = k % Columns * Tile;
        y = PageSize - (k / Columns + 1) * Tile;
    }

    /// <summary>What the atlas was made from: the loaded parts, blueprints and tools (a change makes it again).</summary>
    public static int Key()
    {
        // (the parts' content hash builds strings: it is taken once per set of loaded files, not every frame)
        var counts = HashCode.Combine(ConstructBuild.Blueprints.Count, ConstructTools.All.Count, ConstructParts.All.Count);
        if (counts != _keyCounts || _keyHash == 0)
        {
            _keyCounts = counts;
            _keyHash = HashCode.Combine(ConstructParts.Hash, counts) | 1;
        }
        return _keyHash;
    }

    private static int _keyCounts, _keyHash;

    /// <summary>The window is open: make the atlas (over the next frames) if it isn't made yet.</summary>
    public static void Want() => _wanted = true;

    /// <summary>A slot's picture: the page's texture and its UVs (ImGui's: v from the top), or false while it isn't drawn yet.</summary>
    public static bool TryGet(int slot, out uint texture, out Vector2 uv0, out Vector2 uv1)
    {
        texture = 0;
        uv0 = uv1 = default;
        if ((uint)slot >= (uint)_ready.Length || !_ready[slot])
            return false;
        Place(slot, out var page, out var x, out var y);
        texture = _pages[page];
        // GL's rows run from the bottom: the tile's top row is at y + Tile.
        uv0 = new Vector2(x / (float)PageSize, (y + Tile) / (float)PageSize);
        uv1 = new Vector2((x + Tile) / (float)PageSize, y / (float)PageSize);
        return true;
    }

    // ---- What a tile shows (headless: the self-test checks it) -----------------------------------------------------

    /// <summary>
    /// A slot's boxes (in the thing's own frame): a part's boxes; a blueprint built in a scratch construct world through
    /// the same rules as the server (joints at rest, wheels on their springs); a tool model's boxes. False (no boxes) for
    /// a slot that doesn't exist or a blueprint that doesn't build.
    /// </summary>
    public static bool CollectBoxes(int slot, List<TileBox> boxes)
    {
        boxes.Clear();
        var parts = ConstructParts.All;
        var bps = ConstructBuild.Blueprints;
        var tools = ConstructTools.All;
        if (slot < 0)
            return false;
        if (slot < parts.Count)
        {
            foreach (var b in parts[slot].Boxes)
                boxes.Add(new TileBox(Matrix4x4.CreateScale(b.Half * 2) * Matrix4x4.CreateTranslation(b.Center), b.Color));
            return boxes.Count > 0;
        }
        slot -= parts.Count;
        if (slot < bps.Count)
        {
            var world = new ConstructWorld();
            var c = ConstructBuild.Build(world, bps[slot], Vector3.Zero, Quaternion.Identity, out _);
            if (c == null)
                return false;
            for (var i = 0; i < c.PartCount; i++)
            {
                var d = c.DefOf(i);
                var seg = c.Parts[i].Seg > 0 && c.Parts[i].Seg <= c.JointCount ? ConstructRender.SegMatrix(c, c.Parts[i].Seg, 1f) : Matrix4x4.Identity;
                var drop = Matrix4x4.Identity;
                for (var k = 0; k < c.WheelCount; k++)
                    if (c.Wheels[k].Part == i)
                        drop = Matrix4x4.CreateTranslation(0, c.Wheels[k].Compression - c.Wheels[k].Travel * 0.5f, 0);
                for (var b = 0; b < d.Boxes.Length; b++)
                {
                    c.BoxOf(i, b, out var centre, out var half);
                    boxes.Add(new TileBox(Matrix4x4.CreateScale(half * 2) * Matrix4x4.CreateTranslation(centre) * drop * seg, d.Boxes[b].Color));
                }
            }
            world.Remove(c);
            return boxes.Count > 0;
        }
        slot -= bps.Count;
        if (slot < tools.Count)
        {
            foreach (var b in tools[slot].Boxes)
                boxes.Add(new TileBox(Matrix4x4.CreateScale(b.Size) * Matrix4x4.CreateTranslation(b.Center), b.Color));
            return boxes.Count > 0;
        }
        return false;
    }

    /// <summary>The view direction: from above, front-left (things face -Z), a 3/4 view.</summary>
    private static readonly Vector3 EyeDir = Vector3.Normalize(new Vector3(-0.62f, 0.55f, -0.74f));

    /// <summary>
    /// The camera for a tile: a 3/4 view along <see cref="EyeDir"/>, orthographic, framing every box corner with a margin
    /// in a square (so long beams and tall posts both fill their tile).
    /// </summary>
    public static void Frame(List<TileBox> boxes, out Matrix4x4 view, out Matrix4x4 proj)
    {
        var mn = new Vector3(float.MaxValue);
        var mx = new Vector3(float.MinValue);
        foreach (var b in boxes)
            for (var k = 0; k < 8; k++)
            {
                var p = Vector3.Transform(new Vector3((k & 1) == 0 ? -0.5f : 0.5f, (k & 2) == 0 ? -0.5f : 0.5f, (k & 4) == 0 ? -0.5f : 0.5f), b.Model);
                mn = Vector3.Min(mn, p);
                mx = Vector3.Max(mx, p);
            }
        if (boxes.Count == 0)
            mn = mx = Vector3.Zero;
        var centre = (mn + mx) * 0.5f;
        var radius = MathF.Max(0.05f, (mx - mn).Length() * 0.5f);
        view = Matrix4x4.CreateLookAt(centre + EyeDir * (radius * 3 + 1), centre, Vector3.UnitY);
        // The corners in view space: the tightest square round them.
        float x0 = float.MaxValue, x1 = float.MinValue, y0 = float.MaxValue, y1 = float.MinValue, z0 = float.MaxValue, z1 = float.MinValue;
        foreach (var b in boxes)
            for (var k = 0; k < 8; k++)
            {
                var p = Vector3.Transform(Vector3.Transform(new Vector3((k & 1) == 0 ? -0.5f : 0.5f, (k & 2) == 0 ? -0.5f : 0.5f, (k & 4) == 0 ? -0.5f : 0.5f), b.Model), view);
                x0 = MathF.Min(x0, p.X);
                x1 = MathF.Max(x1, p.X);
                y0 = MathF.Min(y0, p.Y);
                y1 = MathF.Max(y1, p.Y);
                z0 = MathF.Min(z0, p.Z);
                z1 = MathF.Max(z1, p.Z);
            }
        if (boxes.Count == 0)
            x0 = x1 = y0 = y1 = z0 = z1 = 0;
        var half = MathF.Max(MathF.Max(x1 - x0, y1 - y0) * 0.5f * 1.1f, 0.02f);
        var cx = (x0 + x1) * 0.5f;
        var cy = (y0 + y1) * 0.5f;
        proj = Matrix4x4.CreateOrthographicOffCenter(cx - half, cx + half, cy - half, cy + half, MathF.Max(0.01f, -z1 - 0.5f), -z0 + 0.5f);
    }

    // ---- Drawing (GL) ----------------------------------------------------------------------------------------------

    /// <summary>Forgets the atlas (it is made again when next wanted).</summary>
    public static void Invalidate()
    {
        foreach (var t in _pages)
        {
            var tex = new GlTexture(t);
            if (t != 0 && _glReady)
                Native.glDeleteTextures(1, &tex);
        }
        _pages = [];
        _ready = [];
        _next = _count = 0;
        _key = 0;
    }

    /// <summary>Draws the next few missing tiles while the window wants them (call before the scene: it changes framebuffer, viewport and shader state).</summary>
    public static void Pump(Renderer gfx)
    {
        if (!_wanted || _glBroken)
            return;
        _wanted = false;
        var key = Key();
        if (key != _key || _ready.Length != Count)
        {
            Invalidate();
            _key = key;
            _count = Count;
            _ready = new bool[_count];
        }
        if (_next >= _count)
            return;
        if (!_glReady)
            InitGl();
        if (_glBroken)
            return;
        var sw = Stopwatch.StartNew();
        int prevFbo;
        Native.glGetIntegerv(GL_FRAMEBUFFER_BINDING, &prevFbo);
        var vp = stackalloc int[4];
        Native.glGetIntegerv(GL_VIEWPORT, vp);
        var n = 0;
        int pageLo = int.MaxValue, pageHi = -1;
        try
        {
            while (_next < _count && n < PerFrame && (n == 0 || sw.Elapsed.TotalMilliseconds < BudgetMs))
            {
                var slot = _next++;
                Place(slot, out var page, out var x, out var y);
                EnsurePage(page);
                Native.glBindFramebuffer(GL_FRAMEBUFFER, _tileFbo);
                gfx.BeginFrame(Render, Render, Vector3.Zero);
                Native.glClearColor(0, 0, 0, 0); // a clear backdrop: the window's tile shows through round the thing
                Native.glClear(GL_COLOR_BUFFER_BIT);
                SkyRenderer.UseDefaultLight(); // lit the same whatever the map's sky
                if (CollectBoxes(slot, Scratch))
                {
                    Frame(Scratch, out var view, out var proj);
                    gfx.SetCamera(view, proj);
                    gfx.SetFog(Vector3.Zero, 0, 0);
                    foreach (var b in Scratch)
                        gfx.Draw(b.Model, b.Color, 1f, mode: 0);
                }
                // Down into the page, averaging 2x2 (smooth edges).
                Native.glBindFramebuffer(GL_READ_FRAMEBUFFER, _tileFbo);
                Native.glBindFramebuffer(GL_DRAW_FRAMEBUFFER, _pageFbo);
                Native.glFramebufferTexture2D(GL_DRAW_FRAMEBUFFER, GL_COLOR_ATTACHMENT0, GL_TEXTURE_2D, _pages[page], 0);
                Native.glBlitFramebuffer(0, 0, Render, Render, x, y, x + Tile, y + Tile, GL_COLOR_BUFFER_BIT, GL_LINEAR);
                Native.glFramebufferTexture2D(GL_DRAW_FRAMEBUFFER, GL_COLOR_ATTACHMENT0, GL_TEXTURE_2D, 0, 0);
                _ready[slot] = true;
                pageLo = Math.Min(pageLo, page);
                pageHi = Math.Max(pageHi, page);
                n++;
            }
            for (var page = Math.Max(0, pageLo); page <= pageHi; page++)
            {
                Native.glBindTexture(GL_TEXTURE_2D, _pages[page]);
                Native.glGenerateMipmap(GL_TEXTURE_2D);
            }
            Native.glBindTexture(GL_TEXTURE_2D, 0);
        }
        catch (Exception ex)
        {
            _glBroken = true;
            Console.WriteLine($"[inventory] tile pictures off ({ex.Message}): the inventory shows plain tiles");
        }
        finally
        {
            Native.glBindFramebuffer(GL_FRAMEBUFFER, new GlFramebuffer((uint)prevFbo));
            Native.glViewport(vp[0], vp[1], vp[2], vp[3]);
            gfx.Rebind();
        }
        Rendered += n;
        _ms += sw.Elapsed.TotalMilliseconds;
        var err = GlDebug.Error();
        if (err != 0)
            Console.WriteLine($"[inventory] GL error 0x{err:X} after tile pictures");
        if (_next >= _count && n > 0)
            Console.WriteLine($"[inventory] {_count} tiles in {_pages.Length} atlas page(s) of {PageSize} px ({Tile} px tiles), {_ms:0.0} ms in all");
    }

    private static void EnsurePage(int page)
    {
        if (page < _pages.Length && _pages[page] != 0)
            return;
        if (page >= _pages.Length)
            Array.Resize(ref _pages, page + 1);
        GlTexture tex = default;
        Native.glGenTextures(1, &tex);
        Native.glBindTexture(GL_TEXTURE_2D, tex.Id);
        Native.glTexImage2D(GL_TEXTURE_2D, 0, (int)GL_RGBA8, PageSize, PageSize, 0, GL_RGBA, GL_UNSIGNED_BYTE, null);
        Native.glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, (int)GL_LINEAR_MIPMAP_LINEAR);
        Native.glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, (int)GL_LINEAR);
        Native.glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, (int)GL_CLAMP_TO_EDGE);
        Native.glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, (int)GL_CLAMP_TO_EDGE);
        Native.glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAX_LEVEL, 4); // (8 px tiles at the smallest: no bleeding between tiles)
        Native.glBindTexture(GL_TEXTURE_2D, 0);
        if (tex.Id == 0)
            throw new InvalidOperationException("glGenTextures gave no atlas page");
        // A new page starts clear (the window draws its own tile backdrop).
        int prev;
        Native.glGetIntegerv(GL_FRAMEBUFFER_BINDING, &prev);
        Native.glBindFramebuffer(GL_FRAMEBUFFER, _pageFbo);
        Native.glFramebufferTexture2D(GL_FRAMEBUFFER, GL_COLOR_ATTACHMENT0, GL_TEXTURE_2D, tex.Id, 0);
        var status = (uint)Native.glCheckFramebufferStatus(GL_FRAMEBUFFER);
        if (status != GL_FRAMEBUFFER_COMPLETE)
            throw new InvalidOperationException($"atlas framebuffer incomplete (0x{status:X})");
        Native.glViewport(0, 0, PageSize, PageSize);
        Native.glClearColor(0, 0, 0, 0);
        Native.glClear(GL_COLOR_BUFFER_BIT);
        Native.glFramebufferTexture2D(GL_FRAMEBUFFER, GL_COLOR_ATTACHMENT0, GL_TEXTURE_2D, 0, 0);
        Native.glBindFramebuffer(GL_FRAMEBUFFER, new GlFramebuffer((uint)prev));
        _pages[page] = tex.Id;
    }

    private static void InitGl()
    {
        try
        {
            GlFramebuffer tileFbo, pageFbo;
            Native.glGenFramebuffers(1, &tileFbo);
            Native.glGenFramebuffers(1, &pageFbo);
            GlTexture tex = default;
            Native.glGenTextures(1, &tex);
            Native.glBindTexture(GL_TEXTURE_2D, tex.Id);
            Native.glTexImage2D(GL_TEXTURE_2D, 0, (int)GL_RGBA8, Render, Render, 0, GL_RGBA, GL_UNSIGNED_BYTE, null);
            Native.glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, (int)GL_LINEAR);
            Native.glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, (int)GL_LINEAR);
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
            Native.glBindFramebuffer(GL_FRAMEBUFFER, new GlFramebuffer((uint)prev));
            var err = GlDebug.Error();
            if (tileFbo.Id == 0 || pageFbo.Id == 0 || tex.Id == 0 || depth.Id == 0 || err != 0 || status != GL_FRAMEBUFFER_COMPLETE)
                throw new InvalidOperationException($"framebuffers {tileFbo.Id}/{pageFbo.Id}, texture {tex.Id}, depth {depth.Id}, status 0x{status:X}, GL error 0x{err:X}");
            _tileFbo = tileFbo;
            _pageFbo = pageFbo;
            _tileTex = tex.Id;
            _glReady = true;
        }
        catch (Exception ex)
        {
            _glBroken = true;
            Console.WriteLine($"[inventory] no tile framebuffer ({ex.Message}): plain tiles instead");
        }
    }
}
