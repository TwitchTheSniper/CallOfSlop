using System.Diagnostics;
using System.Numerics;
using Brutal.OpenGlApi;
using static Brutal.OpenGlApi.OpenGl;

namespace CallOfSlop;

/// <summary>
/// Pictures of made parts (docs/sandbox/design/inventory-run-time-parts.md, the user's answer 4): a run-time part (an extrusion, a
/// pressed, formed or cut part, a rack...) has no slot in the atlas, since any key can be made. A pool of <see cref="MadePool"/> tiles on
/// pages of their own is filled on demand: <see cref="TryGetPart"/> asks for a part's tile while it is on screen, <see cref="Pump"/>
/// draws at most <see cref="MadePerFrame"/> waiting tiles a frame (inside the atlas's time budget), and a tile stays until its slot is
/// needed for another part (the one longest unseen goes; never one seen this frame). A tile belongs to a def, not an id: an id
/// freed by <see cref="RuntimeParts"/> and made again for another key is drawn again. Nothing is allocated after the first use.
/// </summary>
public static unsafe partial class SandboxTiles
{
    /// <summary>Made-part tiles kept at most (two atlas pages at 128 px), and drawn a frame at most.</summary>
    public const int MadePool = 512, MadePerFrame = 4;

    private static readonly ConstructPartDef?[] _madeDef = new ConstructPartDef?[MadePool];
    private static readonly ushort[] _madeId = new ushort[MadePool];
    private static readonly long[] _madeSeen = new long[MadePool];
    private static readonly byte[] _madeState = new byte[MadePool]; // 0 waiting, 1 drawn, 2 no picture (no boxes, or its data threw): a swatch
    private static readonly Dictionary<ushort, int> _madeAt = new(MadePool * 2);
    private static uint[] _madePages = new uint[(MadePool + PerPage - 1) / PerPage];
    private static int _madeUsed, _madePending;
    private static long _madeFrame = 1;

    /// <summary>Made-part tiles drawn since start-up, and slots handed to another part (tools, checks).</summary>
    public static int MadeRendered { get; private set; }

    public static int MadeEvicted { get; private set; }

    /// <summary>Made-part tiles in use now.</summary>
    public static int MadeInUse => _madeUsed;

    /// <summary>
    /// Any part's picture: a catalogue part's atlas tile, or a made part's pooled tile (asked for when missing: drawn over the next frames).
    /// False while it isn't drawn (draw a swatch). Allocates nothing.
    /// </summary>
    public static bool TryGetPart(ushort id, out uint texture, out Vector2 uv0, out Vector2 uv1)
    {
        if (!Extrusion.IsRuntimeId(id))
            return TryGet(SlotOf(Kind.Part, id), out texture, out uv0, out uv1);
        return TryGetMade(id, out texture, out uv0, out uv1);
    }

    /// <summary>A made part's tile; false while it isn't drawn yet (it is asked for) or can't be.</summary>
    public static bool TryGetMade(ushort id, out uint texture, out Vector2 uv0, out Vector2 uv1)
    {
        texture = 0;
        uv0 = uv1 = default;
        if (_glBroken || ConstructParts.Get(id) is not { RuntimeMade: true } d)
            return false;
        if (!_madeAt.TryGetValue(id, out var s) || !ReferenceEquals(_madeDef[s], d))
        {
            s = MadeSlotFor(id, d);
            if (s < 0)
                return false;
        }
        _madeSeen[s] = _madeFrame;
        if (_madeState[s] != 1)
            return false;
        Place(s, out var page, out var x, out var y);
        texture = _madePages[page];
        uv0 = new Vector2(x / (float)PageSize, (y + Tile) / (float)PageSize);
        uv1 = new Vector2((x + Tile) / (float)PageSize, y / (float)PageSize);
        return texture != 0;
    }

    /// <summary>A slot for a part that has none (or whose id now names another part): a free one, else the one longest unseen. -1: every slot was seen this frame.</summary>
    private static int MadeSlotFor(ushort id, ConstructPartDef d)
    {
        int s;
        if (_madeAt.TryGetValue(id, out var stale))
            s = stale; // (the id was freed and made again for another key: its slot is drawn again)
        else if (_madeUsed < MadePool)
            s = _madeUsed++;
        else
        {
            s = -1;
            var oldest = long.MaxValue;
            for (var k = 0; k < MadePool; k++)
                if (_madeSeen[k] < oldest && _madeSeen[k] != _madeFrame)
                {
                    oldest = _madeSeen[k];
                    s = k;
                }
            if (s < 0)
                return -1;
            if (_madeAt.TryGetValue(_madeId[s], out var was) && was == s)
                _madeAt.Remove(_madeId[s]);
            MadeEvicted++;
        }
        if (_madeDef[s] != null && _madeState[s] == 0)
            _madePending--;
        _madeDef[s] = d;
        _madeId[s] = id;
        _madeState[s] = 0;
        _madeAt[id] = s;
        _madePending++;
        return s;
    }

    /// <summary>Forgets every made-part tile (their pages go): an atlas remade, a regenerate.</summary>
    private static void ClearMade()
    {
        for (var p = 0; p < _madePages.Length; p++)
        {
            var tex = new GlTexture(_madePages[p]);
            if (tex.Id != 0 && _glReady)
                Native.glDeleteTextures(1, &tex);
            _madePages[p] = 0;
        }
        Array.Clear(_madeDef);
        Array.Clear(_madeState);
        Array.Clear(_madeSeen);
        _madeAt.Clear();
        _madeUsed = _madePending = 0;
    }

    /// <summary>The waiting made-part tiles, a few a frame (from <see cref="Pump"/>, before the scene).</summary>
    private static void PumpMade(Renderer gfx)
    {
        _madeFrame++;
        if (_madePending <= 0 || _glBroken)
            return;
        if (_retryAt != 0 && Environment.TickCount64 < _retryAt)
            return;
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
        var touched = 0; // (a bit a page)
        string? failed = null;
        try
        {
            for (var s = 0; s < _madeUsed && _madePending > 0 && n < MadePerFrame && (n == 0 || sw.Elapsed.TotalMilliseconds < BudgetMs); s++)
            {
                if (_madeState[s] != 0 || _madeDef[s] is not { } d)
                    continue;
                _madePending--;
                if (d.Reclaimed)
                {
                    _madeState[s] = 2;
                    continue;
                }
                Place(s, out var page, out var x, out var y);
                EnsureMadePage(page);
                Native.glBindFramebuffer(GL_FRAMEBUFFER, _tileFbo);
                gfx.BeginFrame(Render, Render, Vector3.Zero);
                Native.glClearColor(0, 0, 0, 0);
                Native.glClear(GL_COLOR_BUFFER_BIT);
                SkyRenderer.UseDefaultLight();
                n++;
                if (!DrawMade(gfx, d))
                {
                    _madeState[s] = 2;
                    continue;
                }
                Native.glBindFramebuffer(GL_READ_FRAMEBUFFER, _tileFbo);
                Native.glBindFramebuffer(GL_DRAW_FRAMEBUFFER, _pageFbo);
                Native.glFramebufferTexture2D(GL_DRAW_FRAMEBUFFER, GL_COLOR_ATTACHMENT0, GL_TEXTURE_2D, _madePages[page], 0);
                Native.glBlitFramebuffer(0, 0, Render, Render, x, y, x + Tile, y + Tile, GL_COLOR_BUFFER_BIT, GL_LINEAR);
                Native.glFramebufferTexture2D(GL_DRAW_FRAMEBUFFER, GL_COLOR_ATTACHMENT0, GL_TEXTURE_2D, 0, 0);
                _madeState[s] = 1;
                touched |= 1 << page;
                MadeRendered++;
            }
            for (var page = 0; page < _madePages.Length; page++)
                if ((touched >> page & 1) != 0)
                {
                    Native.glBindTexture(GL_TEXTURE_2D, _madePages[page]);
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
            Fail($"made-part tiles: {failed}");
            return;
        }
        if (n > 0)
            _ms += sw.Elapsed.TotalMilliseconds;
    }

    /// <summary>One made part's boxes into the bound tile framebuffer, as a catalogue part's are. False when it has none or its data throws.</summary>
    private static bool DrawMade(Renderer gfx, ConstructPartDef d)
    {
        try
        {
            Scratch.Clear();
            foreach (var b in d.Boxes)
                if (!b.Hidden)
                    Scratch.Add(new TileBox(b.Model(0, b.Center), b.Color, b.Shape, b.Glass));
            if (Scratch.Count == 0)
                return false;
            Frame(Scratch, out var view, out var proj);
            gfx.SetCamera(view, proj);
            gfx.SetFog(Vector3.Zero, 0, 0);
            DrawTile(gfx, Scratch);
            return true;
        }
        catch (Exception ex)
        {
            if (++_slotErrors <= 5)
                Why($"made part {d.Name} has no picture ({ex.GetType().Name}: {ex.Message})");
            gfx.SetDepthWrite(true);
            gfx.SetBlend(false);
            return false;
        }
    }

    private static void EnsureMadePage(int page)
    {
        if (_madePages[page] == 0)
            _madePages[page] = NewPage(); // (pages of their own: the atlas never grows for made parts)
    }
}
