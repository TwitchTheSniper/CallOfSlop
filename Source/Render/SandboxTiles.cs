using System.Diagnostics;
using System.Numerics;
using Brutal.OpenGlApi;
using static Brutal.OpenGlApi.OpenGl;

namespace CallOfSlop;

/// <summary>One box of a tile's picture: its model matrix (the unit cube scaled, turned and placed) and its colour.</summary>
/// <summary>One box of a tile's picture; <see cref="Glass"/> &gt; 0: a see-through pane, drawn blended over the rest (its opacity).</summary>
public readonly record struct TileBox(Matrix4x4 Model, Vector3 Color, ushort Shape = 0, float Glass = 0);

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
/// <para>
/// Failures don't turn pictures off for good: a slot whose data throws gets a swatch alone; a set-up or batch failure
/// (incomplete framebuffer, a first picture read back empty) is tried again <see cref="MaxTries"/> times before the
/// swatches stay, and Options > Regenerate icons (<see cref="Regenerate"/>) starts over. Every reason is one throttled
/// <c>[icons]</c> line in callofslop.log. The atlas is GPU memory only: no icon cache on disk.
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
        Resource, // a loose resource by material id - 1 (ResourceIcons.cs): drawn first, before the parts
    }

    private static uint[] _pages = [];
    private static bool[] _ready = [];
    private static int _key, _next, _count, _resNext;
    private static bool _wanted, _glReady, _glBroken;
    private static GlFramebuffer _tileFbo, _pageFbo;
    private static uint _tileTex;
    private static double _ms;
    private static readonly List<TileBox> Scratch = new(256);

    /// <summary>Tiles drawn since start-up, and pages made (smoke log, self-test).</summary>
    public static int Rendered { get; private set; }

    public static int Pages => _pages.Length;

    /// <summary>Pictures can't be made on this PC after <see cref="MaxTries"/> tries (the window draws plain swatches until <see cref="Regenerate"/>).</summary>
    public static bool Broken => _glBroken;

    /// <summary>Set-up or drawing failures in a row before the atlas gives up, and the wait between tries (ms).</summary>
    public const int MaxTries = 3;

    public const int RetryMs = 2000;

    private static int _failures, _slotErrors, _remakes;
    private static long _retryAt, _progressAt, _remakeWindow, _whyAt;
    private static bool _regenerate, _checked;
    private static string _why = "";
    private static byte[]? _probe;

    /// <summary>
    /// Options > Regenerate icons: forgets the atlas and every failure, and makes it again over the next frames (the
    /// button only notes it; <see cref="Pump"/> does it, outside the ImGui pass). The atlas lives in GPU memory only:
    /// there is no icon file on disk to clear.
    /// </summary>
    public static void Regenerate() => _regenerate = true;

    /// <summary>
    /// One <c>[icons]</c> line saying why the inventory shows plain tiles: a new reason at once, the same reason at most
    /// every 30 s (callofslop.log). Only called on rare paths, never in a steady frame.
    /// </summary>
    private static void Why(string text)
    {
        var now = Environment.TickCount64;
        if (text == _why && now - _whyAt < 30_000)
            return;
        _why = text;
        _whyAt = now;
        Console.WriteLine($"[icons] {text}");
    }

    /// <summary>Every slot's tile is in the atlas.</summary>
    public static bool Complete => _count > 0 && _next >= _count && _key == Key();

    /// <summary>How many tiles there are: every part, blueprint and tool model.</summary>
    public static int Count => ConstructParts.All.Count + ConstructBuild.Blueprints.Count + ConstructTools.All.Count + RecyclingTable.Current.Materials.Length;

    /// <summary>The first loose resource's slot.</summary>
    private static int ResourceBase => ConstructParts.All.Count + ConstructBuild.Blueprints.Count + ConstructTools.All.Count;

    /// <summary>A loose resource's slot (material id, 1-based), or -1.</summary>
    public static int ResourceSlot(byte material) => material >= 1 && material <= RecyclingTable.Current.Materials.Length ? ResourceBase + material - 1 : -1;

    /// <summary>The slot of a part (by id), a blueprint or a tool model (by index).</summary>
    public static int SlotOf(Kind kind, int index) => kind switch
    {
        Kind.Part => index,
        Kind.Blueprint => ConstructParts.All.Count + index,
        Kind.Resource => ResourceBase + index,
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
        var counts = HashCode.Combine(ConstructBuild.Blueprints.Count, ConstructTools.All.Count, ConstructParts.All.Count, RecyclingTable.Current.Materials.Length);
        if (counts != _keyCounts || _keyHash == 0)
        {
            _keyCounts = counts;
            _keyHash = HashCode.Combine(ConstructParts.Hash, counts) | 1;
        }
        return _keyHash;
    }

    private static int _keyCounts, _keyHash;

    /// <summary>The window is open: make the atlas (over the next frames) if it isn't made yet.</summary>
    public static void Want()
    {
        _wanted = true;
        // A window that has wanted pictures for a while with none made: say why (e.g. Pump not reached, a wait that never ends).
        if (_progressAt != 0 && !_glBroken && (_count == 0 || _next < _count) && Environment.TickCount64 - _progressAt > 10_000)
            Stalled();
    }

    private static void Stalled()
    {
        _progressAt = Environment.TickCount64;
        Why($"no tile pictures made for 10 s: {_next} of {_count} drawn, GL {(_glReady ? "ready" : "not ready")}, {_failures} failure(s), key {_key:X8}/{Key():X8}");
    }

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
                if (!b.Hidden)
                    boxes.Add(new TileBox(b.Model(0, b.Center), b.Color, b.Shape, b.Glass));
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
                        drop = ConstructRender.HubMatrix(c, k);
                var rig = d.HasRig ? ConstructRender.RigOf(c, i) : default;
                for (var b = 0; b < d.Boxes.Length; b++)
                {
                    if (d.Boxes[b].Hidden)
                        continue;
                    c.BoxOf(i, b, out var centre, out _);
                    var rest = d.Boxes[b].Model(c.Parts[i].Rot, centre);
                    if (d.Boxes[b].Rig != BoxRig.None)
                        rest = rig.Model(in d.Boxes[b], in c.Parts[i], rest);
                    boxes.Add(new TileBox(rest * drop * seg, d.Boxes[b].Color, d.Boxes[b].Shape, d.Boxes[b].Glass));
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
        slot -= tools.Count;
        var names = RecyclingTable.Current;
        if (slot < names.Materials.Length)
        {
            var id = (byte)(slot + 1);
            ResourceIcons.Boxes(names.ShapeOf(id), names.ColourOf(id), slot, boxes);
            return boxes.Count > 0;
        }
        return false;
    }

    private static readonly int[] GlassOrder = new int[256];

    /// <summary>
    /// A tile's boxes: the opaque ones, then the glass panes blended over them (farthest from the tile's eye first, no
    /// depth writes), so a pane shows the frame and parts behind it as it does in the world. Leaves blending off and
    /// depth writes on.
    /// </summary>
    public static void DrawTile(Renderer gfx, List<TileBox> boxes)
    {
        var glass = 0;
        foreach (var b in boxes)
            if (b.Glass <= 0)
                gfx.Draw(b.Model, b.Color, 1f, mode: 0, shape: b.Shape);
        for (var i = 0; i < boxes.Count && glass < GlassOrder.Length; i++)
            if (boxes[i].Glass > 0)
            {
                // Insertion by depth along the view (EyeDir points at the eye: the smaller the dot, the farther).
                var key = Vector3.Dot(boxes[i].Model.Translation, EyeDir);
                var at = glass++;
                while (at > 0 && Vector3.Dot(boxes[GlassOrder[at - 1]].Model.Translation, EyeDir) > key)
                {
                    GlassOrder[at] = GlassOrder[at - 1];
                    at--;
                }
                GlassOrder[at] = i;
            }
        if (glass == 0)
            return;
        gfx.SetBlend(true);
        gfx.SetDepthWrite(false);
        for (var k = 0; k < glass; k++)
        {
            var b = boxes[GlassOrder[k]];
            gfx.Draw(b.Model, b.Color, b.Glass, mode: 0, shape: b.Shape);
        }
        gfx.SetDepthWrite(true);
        gfx.SetBlend(false);
    }

    private static readonly PanelCard PreviewCard = new();

    /// <summary>The view of a display's tile: at its screen, a little from the side and above, so the part still has depth.</summary>
    private static Vector3 ScreenEye(Vector3 normal, Vector3 up) => Vector3.Normalize(normal + up * 0.25f + Vector3.Cross(up, normal) * -0.3f);

    /// <summary>The view direction: from above, front-left (things face -Z), a 3/4 view.</summary>
    private static readonly Vector3 EyeDir = Vector3.Normalize(new Vector3(-0.62f, 0.55f, -0.74f));

    /// <summary>
    /// The camera for a tile: a 3/4 view along <see cref="EyeDir"/>, orthographic, framing every box corner with a margin
    /// in a square (so long beams and tall posts both fill their tile).
    /// </summary>
    public static void Frame(List<TileBox> boxes, out Matrix4x4 view, out Matrix4x4 proj) => Frame(boxes, EyeDir, Vector3.UnitY, out view, out proj);

    /// <summary>The same from another side: a display's tile looks at its screen.</summary>
    public static void Frame(List<TileBox> boxes, Vector3 eyeDir, Vector3 up, out Matrix4x4 view, out Matrix4x4 proj)
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
        view = Matrix4x4.CreateLookAt(centre + eyeDir * (radius * 3 + 1), centre, up);
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
        _next = _count = _resNext = 0;
        _key = 0;
    }

    /// <summary>Draws the next few missing tiles while the window wants them (call before the scene: it changes framebuffer, viewport and shader state).</summary>
    public static void Pump(Renderer gfx)
    {
        if (_regenerate)
            DoRegenerate();
        if (!_wanted || _glBroken)
            return;
        _wanted = false;
        if (_progressAt == 0)
            _progressAt = Environment.TickCount64;
        var key = Key();
        if (key != _key || _ready.Length != Count)
            Remake(key);
        if (_next >= _count)
            return;
        if (_retryAt != 0)
        {
            if (Environment.TickCount64 < _retryAt)
                return;
            _retryAt = 0;
        }
        if (!_glReady)
            InitGl();
        if (!_glReady)
            return;
        var sw = Stopwatch.StartNew();
        int prevFbo;
        Native.glGetIntegerv(GL_FRAMEBUFFER_BINDING, &prevFbo);
        var vp = stackalloc int[4];
        Native.glGetIntegerv(GL_VIEWPORT, vp);
        var n = 0;
        int pageLo = int.MaxValue, pageHi = -1;
        string? failed = null;
        var stale = GlDebug.Error(); // (an error left by earlier drawing is not ours: cleared so the check below reads only the tiles)
        if (stale != 0 && _next == 0)
            Why($"a GL error 0x{stale:X} was pending before the tiles (not theirs; ignored)");
        try
        {
            var resources = _count - ResourceBase;
            while ((_resNext < resources || _next < _count) && n < PerFrame && (n == 0 || sw.Elapsed.TotalMilliseconds < BudgetMs))
            {
                // The loose resources first (a few small tiles the recipe picker and the material lists want at once), then every slot in order.
                var slot = _resNext < resources ? ResourceBase + _resNext++ : _next++;
                if (_ready[slot])
                    continue;
                Place(slot, out var page, out var x, out var y);
                EnsurePage(page);
                Native.glBindFramebuffer(GL_FRAMEBUFFER, _tileFbo);
                gfx.BeginFrame(Render, Render, Vector3.Zero);
                Native.glClearColor(0, 0, 0, 0); // a clear backdrop: the window's tile shows through round the thing
                Native.glClear(GL_COLOR_BUFFER_BIT);
                SkyRenderer.UseDefaultLight(); // lit the same whatever the map's sky
                if (!DrawSlot(gfx, slot))
                    continue; // (no boxes, or the slot's data failed: a plain swatch for this one, the rest go on)
                if (!_checked)
                {
                    // The first picture of a run is read back once: a driver that draws nothing into the framebuffer
                    // would otherwise give invisible icons forever.
                    _checked = true;
                    if (!TileHasPixels())
                        throw new InvalidOperationException("the first tile picture came out empty");
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
            failed = ex.Message;
        }
        finally
        {
            Native.glBindFramebuffer(GL_FRAMEBUFFER, new GlFramebuffer((uint)prevFbo));
            Native.glViewport(vp[0], vp[1], vp[2], vp[3]);
            gfx.Rebind();
        }
        if (failed != null)
        {
            Fail(failed);
            return;
        }
        if (n > 0)
        {
            _failures = 0;
            _progressAt = Environment.TickCount64;
        }
        Rendered += n;
        _ms += sw.Elapsed.TotalMilliseconds;
        var err = GlDebug.Error();
        if (err != 0)
            Why($"GL error 0x{err:X} after tile pictures");
        if (_next >= _count && n > 0)
            Console.WriteLine($"[icons] {_count} inventory tiles in {_pages.Length} atlas page(s) of {PageSize} px ({Tile} px tiles), {_ms:0.0} ms in all");
    }

    /// <summary>One slot's boxes into the bound tile framebuffer. False (and a throttled line) when it has none or its data throws.</summary>
    private static bool DrawSlot(Renderer gfx, int slot)
    {
        try
        {
            if (!CollectBoxes(slot, Scratch))
                return false;
            var d = slot < ConstructParts.All.Count ? ConstructParts.All[slot] : null;
            var screen = d != null && LogicDisplays.Preview(d, PreviewCard, out _);
            var normal = screen ? ConstructParts.DirVector(d!.Panels[0].Face) : default;
            var up = screen ? ConstructParts.DirVector(d!.Panels[0].Up) : Vector3.UnitY;
            Frame(Scratch, screen ? ScreenEye(normal, up) : EyeDir, up, out var view, out var proj);
            gfx.SetCamera(view, proj);
            gfx.SetFog(Vector3.Zero, 0, 0);
            DrawTile(gfx, Scratch);
            if (screen)
            {
                // The real panel on the part's own screen, through the panels' own path (LogicDisplays.Preview).
                var panel = d!.Panels[0];
                PanelRender.Begin();
                PanelRender.DrawOnce(PreviewCard, panel.Size, panel.Rows, panel.At - gfx.Origin, normal, up);
                PanelRender.Flush(gfx);
            }
            return true;
        }
        catch (Exception ex)
        {
            // One thing's data, not the GL: before, this turned every picture off for the session.
            if (++_slotErrors <= 5)
                Why($"slot {slot} has no picture ({ex.GetType().Name}: {ex.Message})");
            gfx.SetDepthWrite(true);
            gfx.SetBlend(false);
            return false;
        }
    }

    /// <summary>Any drawn pixel (alpha) in the tile framebuffer: read back once per run (a stall of one 256 px read).</summary>
    private static bool TileHasPixels()
    {
        _probe ??= new byte[Render * Render * 4];
        Native.glBindFramebuffer(GL_READ_FRAMEBUFFER, _tileFbo);
        fixed (byte* p = _probe)
            Native.glReadPixels(0, 0, Render, Render, GL_RGBA, GL_UNSIGNED_BYTE, p);
        for (var i = 3; i < _probe.Length; i += 4)
            if (_probe[i] != 0)
                return true;
        return false;
    }

    /// <summary>The atlas starts again for a new key (parts, blueprints or tools changed); a key that keeps changing is logged.</summary>
    private static void Remake(int key)
    {
        var now = Environment.TickCount64;
        if (now - _remakeWindow > 60_000)
        {
            _remakeWindow = now;
            _remakes = 0;
        }
        if (_count > 0 && ++_remakes == 4)
            Why($"atlas remade 4 times in a minute: its key keeps changing ({ConstructParts.All.Count} parts, {ConstructBuild.Blueprints.Count} blueprints, {ConstructTools.All.Count} tools, {_next} of {_count} drawn)");
        Invalidate();
        _key = key;
        _count = Count;
        _ready = new bool[_count];
        _checked = false;
    }

    /// <summary>A failed set-up or batch: the atlas and the framebuffers go, and it is tried again after a wait, <see cref="MaxTries"/> times.</summary>
    private static void Fail(string why)
    {
        _failures++;
        Invalidate();
        ReleaseGl();
        _ready = new bool[Count];
        _key = Key();
        _count = _ready.Length;
        _checked = false;
        if (_failures >= MaxTries)
        {
            _glBroken = true;
            Why($"tile pictures off after {_failures} tries ({why}): the inventory shows plain tiles; Options > Regenerate icons tries again");
        }
        else
        {
            _retryAt = Environment.TickCount64 + RetryMs;
            Why($"tile pictures failed ({why}): try {_failures} of {MaxTries}, again in {RetryMs / 1000} s");
        }
    }

    private static void DoRegenerate()
    {
        _regenerate = false;
        Invalidate();
        ReleaseGl();
        _glBroken = false;
        _failures = _slotErrors = _remakes = 0;
        _retryAt = 0;
        _progressAt = Environment.TickCount64;
        _ms = 0;
        _key = Key();
        _count = Count;
        _ready = new bool[_count];
        _checked = false;
        _wanted = true;
        _why = ""; // (a reason seen again after a regenerate is logged again)
        Console.WriteLine($"[icons] regenerating {_count} inventory tiles (Options > Regenerate icons)");
    }

    /// <summary>Deletes the tile and page framebuffers and the tile texture (made again by <see cref="InitGl"/>).</summary>
    private static void ReleaseGl()
    {
        if (!_glReady)
            return;
        var fbos = stackalloc GlFramebuffer[2];
        fbos[0] = _tileFbo;
        fbos[1] = _pageFbo;
        Native.glDeleteFramebuffers(2, fbos);
        var tex = new GlTexture(_tileTex);
        Native.glDeleteTextures(1, &tex);
        if (_tileDepth.Id != 0)
        {
            var depth = _tileDepth;
            Native.glDeleteRenderbuffers(1, &depth);
        }
        _tileFbo = _pageFbo = default;
        _tileTex = 0;
        _tileDepth = default;
        _glReady = false;
    }

    private static GlRenderbuffer _tileDepth;

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
        var stale = GlDebug.Error(); // (an error left by earlier drawing would fail the check below: not ours)
        if (stale != 0)
            Why($"a GL error 0x{stale:X} was pending before the tile set-up (not theirs; ignored)");
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
            _tileDepth = depth;
            _glReady = true;
        }
        catch (Exception ex)
        {
            Fail($"no tile framebuffer: {ex.Message}");
        }
    }
}
