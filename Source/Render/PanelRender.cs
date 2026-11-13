using System.Numerics;
using Brutal.OpenGlApi;
using static Brutal.OpenGlApi.OpenGl;

namespace CallOfSlop;

/// <summary>
/// The display panels' text and shapes (docs/sandbox/CONSTRUCTS.md, "Display panels"). ImGui draws over the finished picture
/// and only between NewFrame and Render, so panels can't use it: they are plain world geometry, depth-tested, drawn at the end
/// of the opaque pass with one small shader and one font texture, all visible panels in one draw. The font is a signed distance
/// field (<see cref="PanelFontData"/>, written by tools/render/make_panel_font.py), one texture read a pixel at any size, unpacked into
/// a mipmapped atlas when first needed, so there is no text shaping and no render target per panel.
/// <para>
/// Cost. Only constructs drawn part by part are looked at, and of those only the parts with panels
/// (<see cref="Construct.PanelParts"/>). A panel past its range, facing away or out of view costs a few comparisons and no card.
/// One that shows is filled <see cref="FillHz"/> times a second, and laid out only if its card changed; a frame then only
/// turns its quads into the stream buffer, which moves with the body. Nothing allocates in a steady frame.
/// </para>
/// </summary>
public static unsafe class PanelRender
{
    public const float FillHz = 4f;
    private const int MaxSlots = 128; // panels shown at once: past this the rest wait
    public const int QuadFloats = 12; // x, y, u0, v0, u1, v1, r, g, b, layer; x1, y1 (panel metres)
    private const int VertFloats = 8; // xyz, uv, rgb
    private const float PictureU = 2; // a quad's u from here up is a camera's picture, its layer in the red of its colour
    private const float VideoU = 3; // and from here up a browser page's (VideoFeeds.cs)
    private const int CellW = 6, CellH = 8; // a character's box in the layout: the proportions text is measured with
    private const int BarSlices = 32, TraceSteps = 3; // quads a full coloured bar takes, and a graph's sample (a Graph Screen stays under 200)
    private const float LayerStep = 0.004f; // what is on the screen stands this share of its height off it
    private const uint FontUnit = 8;

    /// <summary>Panels drawn, and skipped for range, for facing away and for being out of view, last frame; cards laid out since the start (the F9 window, a throwaway check).</summary>
    public static int LastDrawn, LastRange, LastBack, LastView;
    public static long Layouts;

    /// <summary>Goes up when something a player just did shows on a panel (a Smart Terminal's wheel): panels in view fill again at once, not at <see cref="FillHz"/>.</summary>
    public static int Touch;

    private sealed class Slot
    {
        public long Key;
        public readonly PanelCard Card = new();
        public float[] Quads = new float[64 * QuadFloats];
        public int QuadCount, Hash, Seen, Stamp, Touched;
        public Vector3 DrawnCentre, DrawnNormal, DrawnUp; // where it was drawn, from the render origin
        public int DrawnPass;
        public double Filled;
        public int Scroll, ScrollMax, Follow = -1, Rows; // the viewer's own place in a long list (never on the wire), and the rows it was laid out in
    }

    private static readonly Dictionary<long, Slot> _slots = new(MaxSlots);
    private static readonly List<Slot> _free = new(MaxSlots);
    private static readonly List<long> _gone = new(MaxSlots);
    private static int _frame;
    private static float[] _verts = new float[8192];
    private static int _vertCount;
    private static bool _ready, _broken;
    private static GlProgram _program;
    private static GlVertexArray _vao;
    private static GlBuffer _vbo;
    private static int _locViewProj, _bufferBytes;

    private const string VertexSrc = """
        #version 410 core
        layout(location = 0) in vec3 aPos;
        layout(location = 1) in vec2 aUv;
        layout(location = 2) in vec3 aColour;
        uniform mat4 uViewProj;
        out vec2 vUv;
        out vec3 vColour;
        void main()
        {
            vUv = aUv;
            vColour = aColour;
            gl_Position = uViewProj * vec4(aPos, 1.0);
        }
        """;

    private const string FragmentSrc = """
        #version 410 core
        in vec2 vUv;
        in vec3 vColour;
        uniform sampler2D uFont;
        uniform sampler2DArray uFeeds;
        uniform sampler2DArray uVideo;
        out vec4 FragColor;
        void main()
        {
            // The field falls 1/6 a texel: the edge is smoothed over about one screen pixel, whatever the size (taken outside the
            // choice below, where the derivatives are defined).
            vec2 texels = vUv * vec2(textureSize(uFont, 0));
            float w = clamp(0.083 * max(length(dFdx(texels)), length(dFdy(texels))), 0.01, 0.3);
            if (vUv.x >= 3.0)
            {
                FragColor = vec4(texture(uVideo, vec3(vUv.x - 3.0, vUv.y, vColour.r)).rgb, 1.0);
                return;
            }
            if (vUv.x >= 2.0)
            {
                FragColor = vec4(texture(uFeeds, vec3(vUv.x - 2.0, vUv.y, vColour.r)).rgb, 1.0);
                return;
            }
            float a = vUv.x < 0.0 ? 1.0 : smoothstep(0.5 - w, 0.5 + w, texture(uFont, vUv).r);
            if (a < 0.02)
                discard;
            FragColor = vec4(vColour, a);
        }
        """;

    private static Vector3 _camEye, _camForward;
    private static int _worldPass;

    /// <summary>The world's own pass begins, seen from here (from the render origin). Panels drawn in it can be pointed at (<see cref="DrawnSpot"/>).</summary>
    public static void BeginWorld(Vector3 eye, Vector3 forward)
    {
        _worldPass++;
        _camEye = eye;
        _camForward = forward;
    }

    /// <summary>A panel as this pass draws it.</summary>
    public static void NoteDrawn(long key, Vector3 centre, Vector3 normal, Vector3 up)
    {
        if (Find(key) is not { } s)
            return;
        s.DrawnCentre = centre;
        s.DrawnNormal = normal;
        s.DrawnUp = up;
        s.DrawnPass = _worldPass;
    }

    public static long Key(ushort body, int part, int panel) => (long)body << 24 | (long)part << 8 | (uint)panel;

    /// <summary>
    /// Where the crosshair was on a panel in the last picture drawn: metres from its centre, x to the viewer's right and y up. It takes the camera and the panel from that picture, so the answer is what the
    /// player saw, whatever pose input, ticks or the network hold by now: at any speed, seated or riding, on a host or a client. False: the panel wasn't drawn, or faces away.
    /// </summary>
    public static bool DrawnSpot(long key, out Vector2 at)
    {
        at = default;
        if (!_slots.TryGetValue(key, out var s) || s.DrawnPass != _worldPass)
            return false;
        var along = Vector3.Dot(_camForward, s.DrawnNormal);
        if (along >= -1e-4f)
            return false;
        var hit = _camEye + _camForward * (Vector3.Dot(s.DrawnCentre - _camEye, s.DrawnNormal) / along);
        var right = Vector3.Cross(s.DrawnUp, s.DrawnNormal);
        at = new Vector2(Vector3.Dot(hit - s.DrawnCentre, right), Vector3.Dot(hit - s.DrawnCentre, s.DrawnUp));
        return true;
    }

    /// <summary>Starts a frame's panels.</summary>
    public static void Begin()
    {
        _frame++;
        _vertCount = 0;
        LastDrawn = LastRange = LastBack = LastView = 0;
    }

    /// <summary>
    /// The panels of one construct drawn part by part: skips what is out of range, facing away or out of view, keeps the rest for
    /// <see cref="Flush"/>. <paramref name="body"/> is the body's matrix from the render origin, as <see cref="ConstructRender.Draw"/> has it.
    /// </summary>
    public static void Gather(Renderer gfx, ConstructWorld world, Construct c, in Matrix4x4 body, float alpha, ReadOnlySpan<Vector4> planes, double now)
    {
        var eye = gfx.CameraPosition - gfx.Origin;
        var origin = gfx.Origin;
        for (var k = 0; k < c.PanelParts.Length; k++)
        {
            var part = c.PanelParts[k];
            if (part >= c.PartCount)
                continue;
            ref var p = ref c.Parts[part];
            var d = p.D;
            if (d.Panels.Length == 0 || p.Broken)
                continue;
            var seg = p.Seg == 0 ? body : ConstructRender.SegMatrix(c, p.Seg, alpha) * body;
            for (var n = 0; n < d.Panels.Length; n++)
            {
                var panel = d.Panels[n];
                var centre = Vector3.Transform(p.Pos + Rot24.Apply(p.Rot, panel.At), seg);
                var to = eye - centre;
                if (to.LengthSquared() > panel.Range * panel.Range)
                {
                    LastRange++;
                    continue;
                }
                var normal = Vector3.TransformNormal(ConstructParts.DirVector(Rot24.ApplyDir(p.Rot, panel.Face)), seg);
                if (Vector3.Dot(to, normal) <= 0)
                {
                    LastBack++;
                    continue;
                }
                if (planes.Length > 0 && Outside(planes, centre + origin, panel.Size.Length() * 0.5f))
                {
                    LastView++;
                    continue;
                }
                var key = Key(c.Id, part, n);
                if (Find(key) is not { } s)
                    continue;
                s.Seen = _frame;
                NoteDrawn(key, centre, normal, Vector3.TransformNormal(ConstructParts.DirVector(Rot24.ApplyDir(p.Rot, panel.Up)), seg));
                if (now - s.Filled >= 1 / FillHz || s.Stamp != c.RebuildStamp || s.Touched != Touch)
                    Fill(world, c, part, panel, s, now);
                Emit(s, panel.Size.Y, centre, normal, Vector3.TransformNormal(ConstructParts.DirVector(Rot24.ApplyDir(p.Rot, panel.Up)), seg));
                if (key == _pointerKey && _pointerRegion < 0)
                    EmitPointer(panel.Size.Y, centre, normal, Vector3.TransformNormal(ConstructParts.DirVector(Rot24.ApplyDir(p.Rot, panel.Up)), seg));
                LastDrawn++;
            }
        }
    }

    private static bool Outside(ReadOnlySpan<Vector4> planes, Vector3 at, float radius)
    {
        foreach (var p in planes)
            if (p.X * at.X + p.Y * at.Y + p.Z * at.Z + p.W < -radius)
                return true;
        return false;
    }

    private static Slot? Find(long key)
    {
        if (_slots.TryGetValue(key, out var s))
            return s;
        if (_slots.Count >= MaxSlots)
        {
            // Those not shown for a while give their slots up.
            foreach (var (k, old) in _slots)
                if (_frame - old.Seen > 60)
                    _gone.Add(k);
            foreach (var k in _gone)
            {
                _free.Add(_slots[k]);
                _slots.Remove(k);
            }
            _gone.Clear();
            if (_slots.Count >= MaxSlots)
                return null;
        }
        if (_free.Count > 0)
        {
            s = _free[^1];
            _free.RemoveAt(_free.Count - 1);
        }
        else
            s = new Slot();
        s.Key = key;
        s.Hash = 0;
        s.Filled = double.MinValue; // (a stale filling isn't kept across owners)
        s.Scroll = s.ScrollMax = 0;
        s.Follow = -1;
        return _slots[key] = s;
    }

    private static void Fill(ConstructWorld world, Construct c, int part, PanelDef panel, Slot s, double now)
    {
        s.Filled = now;
        s.Stamp = c.RebuildStamp;
        s.Touched = Touch;
        s.Card.Clear();
        s.Card.Touch = panel.Touch;
        s.Card.Hover = s.Key == _pointerKey ? _pointerRegion : -1;
        var d = c.DefOf(part);
        var percent = ConstructConfig.Has(d, CfgKey.TextSize) ? ConstructConfig.Get(c.Parts[part], d, CfgKey.TextSize, 100) : 100;
        var card = s.Card;
        card.PanelSize = panel.Size;
        // A display whose input is a console's, a machine's or a rack's Data out is a production monitor (ProductionLines.Monitors.cs).
        var source = panel == d.Panels[0] ? ProductionLines.MonitorSource(c, part) : -1;
        int rows;
        if (source >= 0)
        {
            rows = ProductionLines.MonitorRows(panel, percent, out var cols);
            card.Rows = card.TextRows = rows;
            card.Cols = cols;
            ProductionLines.FillMonitor(world, c, source, card);
            rows = card.Rows;
        }
        else
        {
            rows = ConstructPanels.RowsAt(panel, percent);
            card.Rows = rows;
            card.Cols = Math.Min(PanelCard.MaxChars, Columns(panel.Size, rows));
            card.TextRows = ConstructPanels.FreeRows(panel, rows);
            card.Scroll = s.Scroll;
            card.Follow = s.Follow;
            panel.Fill!(world, c, part, panel, card);
        }
        s.Scroll = Math.Clamp(card.Scroll, 0, card.ScrollMax);
        s.ScrollMax = card.ScrollMax;
        s.Follow = card.Follow;
        if (percent > 100.5f && source < 0)
        {
            // Bigger text: fewer rows, but never fewer than a source that doesn't reflow has filled (its text gets no bigger than fits).
            for (var l = PanelCard.MaxLines - 1; l >= rows; l--)
                if (card.Length(l) > 0)
                {
                    rows = Math.Min(l + 1, panel.Rows);
                    break;
                }
        }
        var hash = card.Hash();
        if (hash == s.Hash && s.QuadCount > 0 && rows == s.Rows)
            return;
        s.Hash = hash;
        s.Rows = rows;
        Layout(s, card, panel.Size, rows);
    }

    /// <summary>This PC's wheel or Up/Down on a panel's list: <paramref name="by"/> entries. False when the panel has no list longer than it shows.</summary>
    public static bool Scroll(long key, int by)
    {
        if (!_slots.TryGetValue(key, out var s) || s.ScrollMax <= 0)
            return false;
        s.Scroll = Math.Clamp(s.Scroll + by, 0, s.ScrollMax);
        s.Filled = double.MinValue; // (fill again this frame)
        return true;
    }

    /// <summary>Does this panel show a list longer than it is (the wheel scrolls it)?</summary>
    public static bool Scrollable(long key) => _slots.TryGetValue(key, out var s) && s.ScrollMax > 0;

    /// <summary>How many rows a panel's list pages by: its text rows less the title.</summary>
    public static int PageRows(long key) => _slots.TryGetValue(key, out var s) ? Math.Max(1, s.Card.TextRows - 2) : 1;

    /// <summary>How many characters a text line of a panel this size holds (no bar; a card's bar makes its rows a little smaller).</summary>
    public static int Columns(Vector2 size, int rows)
    {
        var h = size.Y;
        var pad = h * 0.055f;
        var glyphW = (h - 2 * pad) / rows * 0.86f * CellW / CellH;
        return (int)((size.X - 2 * pad) / glyphW + 0.2f);
    }

    private static Vector3 StateColour(PanelState st) => st switch
    {
        PanelState.Idle => new Vector3(0.45f, 0.62f, 0.72f),
        PanelState.Working => new Vector3(0.35f, 0.72f, 1f),
        PanelState.Ok => new Vector3(0.35f, 0.9f, 0.45f),
        PanelState.Wants => new Vector3(1f, 0.74f, 0.2f),
        PanelState.Refused or PanelState.Fault => new Vector3(1f, 0.28f, 0.2f),
        _ => new Vector3(0.4f),
    };

    private static void Layout(Slot s, PanelCard card, Vector2 size, int rows)
    {
        Layouts++;
        s.QuadCount = 0;
        var w = size.X;
        var h = size.Y;
        var hw = w * 0.5f;
        var hh = h * 0.5f;
        var edge = h * 0.025f;
        var pad = edge + h * 0.03f;
        var state = StateColour(card.State);
        Solid(s, -hw, -hh, hw, hh, new Vector3(0.03f, 0.045f, 0.055f), 0);
        Solid(s, -hw, hh - edge, hw, hh, state, 1);
        Solid(s, -hw, -hh, hw, -hh + edge, state, 1);
        Solid(s, -hw, -hh + edge, -hw + edge, hh - edge, state, 1);
        Solid(s, hw - edge, -hh + edge, hw, hh - edge, state, 1);
        if (card.Picture > 0)
            Quad(s, -hw + edge, -hh + edge, hw - edge, hh - edge, PictureU, 0, PictureU + 1, 1, new Vector3(card.Picture - 1, 0, 0), 1);
        if (card.Video > 0) // (the browser's rows come top first)
        {
            PictureArea(size, card.VideoRect, out var px0, out var py0, out var px1, out var py1);
            Quad(s, px0, py0, px1, py1, VideoU, card.VideoHeight / (float)MediaBlock.MaxHeight, VideoU + card.VideoWidth / (float)MediaBlock.MaxWidth, 0, new Vector3(card.Video - 1, 0, 0), 1);
        }
        for (var t = 0; t < card.Touch.Length; t++)
            Button(s, card, t, size, state);
        var barH = card.Bar >= 0 ? h * 0.075f : 0;
        var row = (h - 2 * pad - barH) / rows;
        var glyphH = row * 0.86f;
        var glyphW = glyphH * CellW / CellH;
        var bottom = -hh + pad + barH;
        if (card.Bar >= 0)
        {
            var x0 = -hw + pad;
            var x1 = hw - pad;
            Solid(s, x0, -hh + pad * 0.8f, x1, -hh + pad * 0.8f + barH * 0.7f, new Vector3(0.11f, 0.15f, 0.18f), 1);
            Solid(s, x0, -hh + pad * 0.8f, x0 + (x1 - x0) * Math.Clamp(card.Bar, 0, 1), -hh + pad * 0.8f + barH * 0.7f, state, 2);
        }
        var indent = 0f;
        if (card.HasIcon && rows >= 3)
        {
            // Under the title, to the left of the rest: the colour, and the picture on it.
            var side = row * 2 * 0.94f;
            var x = -hw + pad;
            var top = hh - pad - row * 1.03f;
            Solid(s, x, top - side, x + side, top, card.IconMask == 0 ? card.IconColour : new Vector3(0.1f, 0.13f, 0.15f), 1);
            if (card.IconMask != 0)
            {
                var cell = side / 8;
                for (var y = 0; y < 8; y++)
                    for (var cx = 0; cx < 8; cx++)
                        if ((card.IconMask >> (y * 8 + cx) & 1) != 0)
                            Solid(s, x + cx * cell, top - side + y * cell, x + (cx + 1) * cell, top - side + (y + 1) * cell, Vector3.Max(card.IconColour, new Vector3(0.2f)), 2);
            }
            indent = side + glyphW * 0.6f;
        }
        var bright = card.HasInk ? card.Ink : new Vector3(0.9f, 0.95f, 0.97f);
        var title = card.HasInk ? card.Ink : state;
        for (var l = 0; l < rows && l < PanelCard.MaxLines; l++)
        {
            var left = -hw + pad + (l > 0 && l < 3 ? indent : 0);
            var y = bottom + (rows - 1 - l) * row + (row - glyphH) * 0.5f;
            if (l == card.Mark)
                Solid(s, -hw + pad * 0.6f, bottom + (rows - 1 - l) * row, hw - pad * 0.6f, bottom + (rows - l) * row, state * 0.32f, 1);
            var line = card.Line(l);
            var room = (int)((hw - pad - left) / glyphW + 0.2f);
            var colour = l == 0 && !card.Plain ? title : card.IsDim(l) ? bright * 0.62f : bright;
            if (card.Fit)
            {
                // A readout: as big as this line's length lets it be, in the middle of the panel.
                var across = hw - pad - left;
                var ga = MathF.Min(glyphW, across / MathF.Max(1, line.Length));
                var gh = ga * CellH / CellW;
                for (var k = 0; k < line.Length; k++)
                    if (line[k] > 32)
                        Glyph(s, line[k], left + k * ga, bottom + (h - 2 * pad - barH - gh) * 0.5f, ga, gh, colour);
                continue;
            }
            for (var k = 0; k < line.Length && k < room; k++)
                if (line[k] > 32)
                    Glyph(s, line[k], left + k * glyphW, y, glyphW, glyphH, !card.HasInk && card.TintAt(l, k, out var tint) ? tint : colour);
        }
        if (card.Draw != PanelDraw.None)
            Shapes(s, card, size, pad, card.Draw is PanelDraw.Lamps or PanelDraw.Graph ? hh - pad : bottom + (rows - 1) * row, state);
    }

    /// <summary>A touch button: its plate, lit when aimed at, and its label in the middle, as big as fits.</summary>
    private static void Button(Slot s, PanelCard card, int t, Vector2 size, Vector3 state)
    {
        var r = card.Touch[t].At;
        var gap = size.Y * 0.012f;
        var x0 = -size.X * 0.5f + r.X * size.X + gap;
        var y0 = -size.Y * 0.5f + r.Y * size.Y + gap;
        var x1 = -size.X * 0.5f + r.Z * size.X - gap;
        var y1 = -size.Y * 0.5f + r.W * size.Y - gap;
        Solid(s, x0, y0, x1, y1, t == card.Hover ? state * 0.75f : new Vector3(0.13f, 0.17f, 0.2f), 1);
        var text = card.TouchLabels[t] ?? card.Touch[t].Label;
        var glyphH = MathF.Min((y1 - y0) * 0.7f, (x1 - x0) * 0.9f / MathF.Max(1, text.Length) * CellH / CellW);
        var glyphW = glyphH * CellW / CellH;
        var x = (x0 + x1 - glyphW * text.Length) * 0.5f;
        for (var k = 0; k < text.Length; k++)
            if (text[k] > 32)
                Glyph(s, text[k], x + k * glyphW, (y0 + y1 - glyphH) * 0.5f, glyphW, glyphH, new Vector3(0.9f, 0.95f, 0.97f));
    }

    /// <summary>Where a picture sits on a panel, in metres from its centre: its rectangle, inside the border.</summary>
    public static void PictureArea(Vector2 size, Vector4 rect, out float x0, out float y0, out float x1, out float y1)
    {
        var edge = size.Y * 0.025f;
        x0 = MathF.Max(-size.X * 0.5f + rect.X * size.X, -size.X * 0.5f + edge);
        y0 = MathF.Max(-size.Y * 0.5f + rect.Y * size.Y, -size.Y * 0.5f + edge);
        x1 = MathF.Min(-size.X * 0.5f + rect.Z * size.X, size.X * 0.5f - edge);
        y1 = MathF.Min(-size.Y * 0.5f + rect.W * size.Y, size.Y * 0.5f - edge);
    }

    /// <summary>A place on a panel is on its picture.</summary>
    public static bool InPicture(PanelDef panel, Vector2 at)
    {
        PictureArea(panel.Size, panel.Picture, out var x0, out var y0, out var x1, out var y1);
        return at.X >= x0 && at.X <= x1 && at.Y >= y0 && at.Y <= y1;
    }

    /// <summary>A place on a panel as a pixel of its page, held to the page's edge.</summary>
    public static void PageSpot(PanelDef panel, Vector2 at, int width, int height, out int x, out int y)
    {
        PictureArea(panel.Size, panel.Picture, out var x0, out var y0, out var x1, out var y1);
        x = Math.Clamp((int)((at.X - x0) / (x1 - x0) * width), 0, width - 1);
        y = Math.Clamp((int)((y1 - at.Y) / (y1 - y0) * height), 0, height - 1);
    }

    private static long _pointerKey = -1;
    private static Vector2 _pointerAt;
    private static int _pointerRegion = -1;
    private static readonly Slot PointerSlot = new();

    /// <summary>This frame's crosshair on a panel: a cursor there, or (region at 0 or up) that button lit.</summary>
    public static void SetPointer(long key, Vector2 at, int region)
    {
        if (key != _pointerKey || region != _pointerRegion)
            Touch++;
        _pointerKey = key;
        _pointerAt = at;
        _pointerRegion = region;
    }

    public static void ClearPointer()
    {
        if (_pointerKey != -1)
            Touch++;
        _pointerKey = -1;
        _pointerRegion = -1;
    }

    /// <summary>The cursor, on top of what is on the panel. A plain arrow of three quads: a white one with a dark edge, and a tail.</summary>
    private static void EmitPointer(float height, Vector3 centre, Vector3 normal, Vector3 up)
    {
        var size = height * 0.03f;
        var s = PointerSlot;
        s.QuadCount = 0;
        var x = _pointerAt.X;
        var y = _pointerAt.Y;
        Solid(s, x - size * 0.1f, y - size * 1.1f, x + size * 0.8f, y + size * 0.1f, new Vector3(0.05f), 4);
        Solid(s, x, y - size, x + size * 0.7f, y, Vector3.One, 5);
        Emit(s, height, centre, normal, up);
    }

    /// <summary>What a card draws besides text. Every shape is made of the same upright quads: a needle or a line is a row of small ones, so a panel needs no second shader or vertex format.</summary>
    private static void Shapes(Slot s, PanelCard card, Vector2 size, float pad, float top, Vector3 state)
    {
        var hw = size.X * 0.5f;
        var hh = size.Y * 0.5f;
        var dim = new Vector3(0.11f, 0.15f, 0.18f);
        var bright = card.HasInk ? card.Ink : new Vector3(0.9f, 0.95f, 0.97f);
        var ink = card.HasInk ? card.Ink : state;
        var red = card.HasInk ? card.Ink : new Vector3(1f, 0.32f, 0.2f);
        var value = Math.Clamp(card.Value, 0, 1);
        switch (card.Draw)
        {
            case PanelDraw.BarV:
            case PanelDraw.BarH:
            {
                // The whole screen. Coloured along the value in slices, one quad if it's all one colour.
                var across = card.Draw == PanelDraw.BarH;
                var x0 = -hw + pad;
                var y0 = -hh + pad;
                var x1 = hw - pad;
                var y1 = hh - pad;
                Solid(s, x0, y0, x1, y1, dim, 1);
                var n = card.Ramp == PanelRamp.Solid ? 1 : (int)MathF.Ceiling(value * BarSlices);
                for (var k = 0; k < n; k++)
                {
                    var a = card.Ramp == PanelRamp.Solid ? 0 : (float)k / BarSlices;
                    var b = card.Ramp == PanelRamp.Solid ? value : MathF.Min((k + 1f) / BarSlices, value);
                    var c = RampColour(card.Ramp, (a + b) * 0.5f, ink);
                    if (across)
                        Solid(s, x0 + (x1 - x0) * a, y0, x0 + (x1 - x0) * b, y1, c, 2);
                    else
                        Solid(s, x0, y0 + (y1 - y0) * a, x1, y0 + (y1 - y0) * b, c, 2);
                }
                break;
            }
            case PanelDraw.Dial:
            {
                var bottom = -hh + pad;
                var r = MathF.Min(size.X * 0.46f, (top - bottom) * 0.58f);
                var cy = bottom + r * 0.8f;
                var dot = MathF.Max(r * 0.045f, size.Y * 0.006f);
                for (var k = 0; k <= 20; k++)
                {
                    var a = (-135 + 13.5f * k) * MathF.PI / 180;
                    var big = k % 5 == 0;
                    var rr = big ? r * 0.92f : r;
                    var dd = big ? dot * 1.5f : dot;
                    Solid(s, MathF.Sin(a) * rr - dd, cy + MathF.Cos(a) * rr - dd, MathF.Sin(a) * rr + dd, cy + MathF.Cos(a) * rr + dd, big ? bright : dim * 3, 1);
                }
                var needle = (-135 + 270 * value) * MathF.PI / 180;
                const int Steps = 24;
                for (var k = 0; k <= Steps; k++)
                {
                    var t = r * 0.82f * k / Steps;
                    var x = MathF.Sin(needle) * t;
                    var y = cy + MathF.Cos(needle) * t;
                    var w = dot * (1.3f - 0.8f * k / Steps);
                    Solid(s, x - w, y - w, x + w, y + w, red, 2);
                }
                Solid(s, -dot * 2, cy - dot * 2, dot * 2, cy + dot * 2, bright, 2);
                break;
            }
            case PanelDraw.Graph:
            {
                var x0 = -hw + pad;
                var x1 = hw - pad;
                var y0 = -hh + pad;
                var y1 = top;
                Solid(s, x0, y0, x1, y1, new Vector3(0.05f, 0.07f, 0.09f), 1);
                for (var k = 1; k < 4; k++)
                    Solid(s, x0, y0 + (y1 - y0) * k / 4 - size.Y * 0.002f, x1, y0 + (y1 - y0) * k / 4 + size.Y * 0.002f, dim, 1);
                if (card.Trace is not { } trace)
                    break;
                var lo = card.TraceLow;
                var hi = card.TraceHigh;
                if (hi == lo)
                {
                    lo = float.MaxValue;
                    hi = float.MinValue;
                    foreach (var t in trace)
                    {
                        lo = MathF.Min(lo, t);
                        hi = MathF.Max(hi, t);
                    }
                    if (hi - lo < 1e-3f)
                        hi = lo + 1e-3f;
                }
                var n = trace.Length;
                var dx = (x1 - x0) / (n - 1);
                var thick = MathF.Max((y1 - y0) * 0.012f, size.Y * 0.004f);
                var last = 0f;
                for (var k = 0; k < n; k++)
                {
                    var share = Math.Clamp((trace[(card.TraceAt + k) % n] - lo) / (hi - lo), 0, 1);
                    // Each sample's step in a few short ones along the slope: quads, but no stairs.
                    for (var t = k > 0 ? 1 : TraceSteps; t <= TraceSteps; t++)
                    {
                        var v0 = last + (share - last) * (t - 1) / TraceSteps;
                        var v1 = last + (share - last) * t / TraceSteps;
                        var ya = y0 + (y1 - y0) * v0;
                        var yb = y0 + (y1 - y0) * v1;
                        var xb = x0 + (k - 1 + (float)t / TraceSteps) * dx;
                        if (k > 0)
                            Solid(s, xb - dx / TraceSteps, MathF.Min(ya, yb) - thick, xb, MathF.Max(ya, yb) + thick, RampColour(card.Ramp, (v0 + v1) * 0.5f, ink), 2);
                    }
                    last = share;
                }
                break;
            }
            case PanelDraw.Flight:
                FlightPlot(s, card, -hw + pad, -hh + pad, hw - pad, top, size);
                break;
            case PanelDraw.Map:
            {
                // Between the title and the two caption lines at the bottom (ConstructLogisticsBoard: the map's caption).
                var rowH = (top - (-hh + pad)) / Math.Max(1, card.Rows - 1);
                MapPlot(s, card, -hw + pad, -hh + pad + rowH * 2.1f, hw - pad, top - rowH * 0.1f, size);
                break;
            }
            case PanelDraw.Lamps:
            {
                var side = (top - (-hh + pad)) / Math.Max(1, card.Lamps);
                for (var k = 0; k < card.Lamps; k++)
                {
                    var y = top - side * k;
                    var lit = (card.LampBits >> k & 1) != 0;
                    Solid(s, hw - pad - side * 0.8f, y - side * 0.85f, hw - pad, y - side * 0.1f, lit ? (card.HasInk ? card.Ink : new Vector3(0.35f, 1f, 0.45f)) : dim, lit ? 2 : 1);
                }
                break;
            }
            case PanelDraw.Compass:
            {
                var x0 = -hw + pad;
                var x1 = hw - pad;
                var y0 = -hh + pad;
                var y1 = top;
                var mid = (y0 + y1) * 0.5f;
                var w = size.X * 0.008f;
                var span = 90f; // degrees shown each side of the mark
                var glyph = (y1 - y0) * 0.42f;
                for (var deg = 0; deg < 360; deg += 15)
                {
                    var off = (deg - card.Value * 360 + 540) % 360 - 180;
                    if (MathF.Abs(off) > span)
                        continue;
                    var x = off / span * (x1 - x0) * 0.46f;
                    if (deg % 90 == 0)
                        Glyph(s, deg == 0 ? 'N' : deg == 90 ? 'E' : deg == 180 ? 'S' : 'W', x - glyph * 0.375f, mid - glyph * 0.5f, glyph * 0.75f, glyph, deg == 0 ? ink : bright);
                    else
                        Solid(s, x - w, y0, x + w, deg % 45 == 0 ? mid : y0 + (y1 - y0) * 0.25f, bright, 1);
                }
                Solid(s, -w * 1.5f, y0, w * 1.5f, y1, red, 2);
                break;
            }
        }
    }

    /// <summary>A bar's or a trace's colour at a share of its range (<paramref name="ink"/>: the display's own).</summary>
    public static Vector3 RampColour(PanelRamp ramp, float share, Vector3 ink)
    {
        switch (ramp)
        {
            case PanelRamp.Level:
                return share < 0.6f ? new Vector3(0.3f, 0.9f, 0.4f) : share < 0.85f ? new Vector3(1f, 0.8f, 0.2f) : new Vector3(1f, 0.25f, 0.2f);
            case PanelRamp.Gradient:
                return ink * (0.25f + 0.75f * share);
            case PanelRamp.Heat:
                return share < 0.4f
                    ? new Vector3(0.25f + 0.75f * share / 0.4f, 0.04f, 0.02f)
                    : share < 0.75f ? new Vector3(1f, 0.04f + 0.86f * (share - 0.4f) / 0.35f, 0.02f)
                    : new Vector3(1f, 0.9f + 0.1f * (share - 0.75f) / 0.25f, 0.02f + 0.93f * (share - 0.75f) / 0.25f);
            default:
                return ink;
        }
    }

    private static ref float Next(Slot s)
    {
        if ((s.QuadCount + 1) * QuadFloats > s.Quads.Length)
            Array.Resize(ref s.Quads, s.Quads.Length * 2); // (only when a card lays out)
        return ref s.Quads[s.QuadCount++ * QuadFloats];
    }

    private const int PlotArrows = 24, PlotSteps = 6;
    private const int MapLineQuads = 1024; // the most quads the map's conveyor lines take (a bigger network's lines are thinned)

    /// <summary>
    /// The network board's plan map (<see cref="NetPlan"/>, smart logistics design 9.2) in the box (x0, y0)-(x1, y1): conveyors as lines
    /// (cut ones red, in dashes), machines as squares in their lamp's colour with a two-letter tag when there is room, stores as boxes with
    /// a fill bar, Routers as diamonds, Splitters as triangles, Joins as dots; pieces as dots (orange an order's, white storage's, red stuck);
    /// a red ring blinking round the first stuck Router. Aspect kept, centred, with a margin.
    /// </summary>
    private static void MapPlot(Slot s, PanelCard card, float x0, float y0, float x1, float y1, Vector2 size)
    {
        if (card.Plan is not { } p || x1 <= x0 || y1 <= y0)
            return;
        var span = Vector2.Max(p.Max - p.Min, new Vector2(1f));
        var scale = MathF.Min((x1 - x0) / (span.X * 1.08f), (y1 - y0) / (span.Y * 1.08f));
        var mid = (p.Min + p.Max) * 0.5f;
        var c = new Vector2((x0 + x1) * 0.5f, (y0 + y1) * 0.5f);
        Vector2 At(Vector2 v) => c + (v - mid) * scale;
        var line = MathF.Max(size.Y * 0.0035f, 0.002f);
        var cell = MathF.Max(line * 3.2f, MathF.Min(scale * 0.9f, size.Y * 0.035f)); // a node's square: about its metre, within reason
        // Conveyors.
        var budget = MapLineQuads;
        var conveyor = new Vector3(0.3f, 0.4f, 0.46f);
        var cut = new Vector3(0.95f, 0.22f, 0.16f);
        for (var l = 0; l < p.LineN && budget > 0; l++)
        {
            int k = p.LineRoute[l], first = p.RouteFirst[k], n = p.RouteCount[k];
            for (var i = 1; i < n && budget > 0; i++)
                budget -= MapLine(s, At(p.Points[first + i - 1]), At(p.Points[first + i]), line, p.LineCut[l] ? cut : conveyor, p.LineCut[l], budget);
        }
        // Nodes.
        var tagH = cell * 0.62f;
        for (var i = 0; i < p.NodeN; i++)
        {
            var at = At(p.NodeAt[i]);
            var col = p.NodeColour[i];
            var h = cell * 0.5f;
            switch (p.NodeKind[i])
            {
                case PlanNode.Machine:
                case PlanNode.Terminal:
                case PlanNode.Other:
                    Solid(s, at.X - h, at.Y - h, at.X + h, at.Y + h, col * (p.NodeKind[i] == PlanNode.Terminal ? 0.8f : 1f), 2);
                    if (cell >= size.Y * 0.03f) // (the tag where a cell is big enough to read: about 10 px on a 1 m panel seen close)
                    {
                        var gw = tagH * CellW / CellH;
                        Glyph(s, p.NodeTag[i * 2], at.X - gw, at.Y - tagH * 0.5f, gw, tagH, new Vector3(0.04f));
                        Glyph(s, p.NodeTag[i * 2 + 1], at.X, at.Y - tagH * 0.5f, gw, tagH, new Vector3(0.04f));
                    }
                    break;
                case PlanNode.Store:
                {
                    var edge = new Vector3(0.62f, 0.68f, 0.72f);
                    Solid(s, at.X - h, at.Y - h, at.X + h, at.Y + h, edge, 2);
                    Solid(s, at.X - h + line, at.Y - h + line, at.X + h - line, at.Y + h - line, new Vector3(0.06f, 0.08f, 0.1f), 3);
                    if (p.NodeFill[i] > 0)
                        Solid(s, at.X - h + line, at.Y - h + line, at.X + h - line, at.Y - h + line + (2 * h - 2 * line) * p.NodeFill[i], RampColour(PanelRamp.Level, p.NodeFill[i], edge), 4);
                    break;
                }
                case PlanNode.Router:
                    for (var r = 0; r < 4; r++) // (a diamond: four bands narrowing up and down)
                    {
                        var w = h * (1 - r / 4f);
                        var b = h * r / 4f;
                        Solid(s, at.X - w, at.Y + b, at.X + w, at.Y + b + h / 4f, col, 2);
                        Solid(s, at.X - w, at.Y - b - h / 4f, at.X + w, at.Y - b, col, 2);
                    }
                    break;
                case PlanNode.Splitter:
                    for (var r = 0; r < 3; r++) // (a triangle, point up)
                    {
                        var w = h * 0.8f * (1 - r / 3f);
                        Solid(s, at.X - w, at.Y - h * 0.6f + r * h * 0.4f, at.X + w, at.Y - h * 0.2f + r * h * 0.4f, col, 2);
                    }
                    break;
                case PlanNode.Join:
                    Solid(s, at.X - h * 0.35f, at.Y - h * 0.35f, at.X + h * 0.35f, at.Y + h * 0.35f, col, 2);
                    break;
            }
        }
        // Pieces.
        var dot = line * 1.8f;
        for (var i = 0; i < p.DotN; i++)
        {
            var at = At(p.Dots[i]);
            var col = p.DotKind[i] switch { 2 => new Vector3(1f, 0.22f, 0.18f), 1 => new Vector3(1f, 0.62f, 0.15f), _ => new Vector3(0.92f) };
            Solid(s, at.X - dot, at.Y - dot, at.X + dot, at.Y + dot, col, 5);
        }
        // The stuck ring.
        if (p.Ring >= 0 && p.RingOn)
        {
            var at = At(p.NodeAt[p.Ring]);
            var r = cell * 1.1f;
            var red = new Vector3(1f, 0.2f, 0.15f);
            Solid(s, at.X - r, at.Y + r - line, at.X + r, at.Y + r, red, 5);
            Solid(s, at.X - r, at.Y - r, at.X + r, at.Y - r + line, red, 5);
            Solid(s, at.X - r, at.Y - r, at.X - r + line, at.Y + r, red, 5);
            Solid(s, at.X + r - line, at.Y - r, at.X + r, at.Y + r, red, 5);
        }
    }

    /// <summary>A line from <paramref name="a"/> to <paramref name="b"/> of upright quads (one when it runs along an axis, a stair of short ones else; every other one when dashed). The quads it took.</summary>
    private static int MapLine(Slot s, Vector2 a, Vector2 b, float thick, Vector3 colour, bool dashed, int budget)
    {
        var d = b - a;
        var len = d.Length();
        if (len < 1e-5f)
            return 0;
        var h = thick * 0.5f;
        if (!dashed && (MathF.Abs(d.X) < thick * 0.5f || MathF.Abs(d.Y) < thick * 0.5f))
        {
            Solid(s, MathF.Min(a.X, b.X) - h, MathF.Min(a.Y, b.Y) - h, MathF.Max(a.X, b.X) + h, MathF.Max(a.Y, b.Y) + h, colour, 1);
            return 1;
        }
        var steps = Math.Clamp((int)(len / (thick * (dashed ? 3f : 1.5f))), 1, Math.Min(32, budget));
        var used = 0;
        for (var t = 0; t < steps; t++)
        {
            if (dashed && t % 2 == 1)
                continue;
            var p0 = a + d * (t / (float)steps);
            var p1 = a + d * ((t + 1) / (float)steps);
            Solid(s, MathF.Min(p0.X, p1.X) - h, MathF.Min(p0.Y, p1.Y) - h, MathF.Max(p0.X, p1.X) + h, MathF.Max(p0.Y, p1.Y) + h, colour, 1);
            used++;
        }
        return used;
    }

    /// <summary>A craft from above and from the side, an arrow at each effector by the way it pushes, long and bright by what it is told, red when it has nothing to give. A fixed number of quads.</summary>
    private static void FlightPlot(Slot s, PanelCard card, float x0, float y0, float x1, float y1, Vector2 size)
    {
        if (card.Flight is not { } f)
            return;
        var gap = (x1 - x0) * 0.04f;
        var mid = (x0 + x1) * 0.5f;
        var line = size.Y * 0.006f;
        for (var view = 0; view < 2; view++)
        {
            float a0 = view == 0 ? x0 : mid + gap * 0.5f, a1 = view == 0 ? mid - gap * 0.5f : x1;
            Solid(s, a0, y0, a1, y1, new Vector3(0.05f, 0.07f, 0.09f), 1);
            var lo = new Vector2(view == 0 ? card.FlightMin.X : card.FlightMin.Z, view == 0 ? card.FlightMin.Z : card.FlightMin.Y);
            var hi = new Vector2(view == 0 ? card.FlightMax.X : card.FlightMax.Z, view == 0 ? card.FlightMax.Z : card.FlightMax.Y);
            var span = MathF.Max(0.5f, MathF.Max(hi.X - lo.X, hi.Y - lo.Y)) * 1.25f;
            var scale = MathF.Min(a1 - a0, y1 - y0) / span;
            var c = new Vector2((a0 + a1) * 0.5f, (y0 + y1) * 0.5f);
            var at0 = (lo + hi) * 0.5f;
            Vector2 Place(Vector3 p) => view == 0 ? c + new Vector2(p.X - at0.X, at0.Y - p.Z) * scale : c + new Vector2(at0.X - p.Z, p.Y - at0.Y) * scale; // (forward is up on the first, left on the second)
            var bl = c + (lo - at0) * scale;
            var tr = c + (hi - at0) * scale;
            var edge = new Vector3(0.3f, 0.36f, 0.42f);
            Solid(s, bl.X, bl.Y, tr.X, bl.Y + line, edge, 1);
            Solid(s, bl.X, tr.Y - line, tr.X, tr.Y, edge, 1);
            Solid(s, bl.X, bl.Y, bl.X + line, tr.Y, edge, 1);
            Solid(s, tr.X - line, bl.Y, tr.X, tr.Y, edge, 1);
            var com = Place(card.FlightCom);
            Solid(s, com.X - 2 * line, com.Y - 2 * line, com.X + 2 * line, com.Y + 2 * line, new Vector3(1f, 0.85f, 0.2f), 2);
            var shown = 0;
            for (var k = 0; k < f.Count && shown < PlotArrows; k++)
            {
                ref var e = ref f.Items[k];
                if (e.Kind == EffectorKind.Gyro || e.Force == default)
                    continue;
                shown++;
                var from = Place(e.At);
                var dir = Place(e.At + Vector3.Normalize(e.Force)) - from;
                var mag = dir.Length();
                dir = mag > 1e-4f ? dir / mag : Vector2.Zero;
                var amount = MathF.Abs(e.Cmd);
                var dead = e.Auth <= 0;
                var colour = dead ? new Vector3(0.9f, 0.15f, 0.12f) : e.Kind is EffectorKind.Surface or EffectorKind.Wing or EffectorKind.Gimbal or EffectorKind.Cyclic ? (e.Cmd >= 0 ? new Vector3(0.3f, 0.7f, 1f) : new Vector3(1f, 0.6f, 0.2f)) : Vector3.Lerp(new Vector3(0.3f, 0.5f, 0.35f), new Vector3(1f, 0.95f, 0.3f), amount);
                var length = (0.08f + 0.22f * (dead ? 0 : amount)) * MathF.Min(a1 - a0, y1 - y0);
                var dot = line * (e.Kind == EffectorKind.Gimbal ? 0.7f : 1.1f);
                for (var t = 0; t <= PlotSteps; t++)
                {
                    var q = from + dir * (length * t / PlotSteps);
                    var r = t == PlotSteps ? dot * 1.8f : dot;
                    var px = SafeMath.Clamp(q.X, a0 + r, a1 - r);
                    var py = SafeMath.Clamp(q.Y, y0 + r, y1 - r);
                    Solid(s, px - r, py - r, px + r, py + r, colour, 2);
                }
            }
        }
    }

    private static void Solid(Slot s, float x0, float y0, float x1, float y1, Vector3 colour, int layer) =>
        Quad(s, x0, y0, x1, y1, -1, 0, -1, 0, colour, layer);

    private static void Glyph(Slot s, char ch, float x, float y, float w, float h, Vector3 colour)
    {
        var g = ch - 32;
        var u = 1f / PanelFontData.Columns;
        var v = 1f / PanelFontData.Rows;
        var col = g % PanelFontData.Columns;
        var row = g / PanelFontData.Columns;
        // The whole cell: (x, y, w, h) is the character's box, and the margin round it goes round the quad too.
        var mx = w * PanelFontData.Margin / PanelFontData.BoxW;
        var my = h * PanelFontData.Margin / PanelFontData.BoxH;
        Quad(s, x - mx, y - my, x + w + mx, y + h + my, col * u, (row + 1) * v, (col + 1) * u, row * v, colour, 2);
    }

    private static void Quad(Slot s, float x0, float y0, float x1, float y1, float u0, float v0, float u1, float v1, Vector3 c, int layer)
    {
        ref var f = ref Next(s);
        var q = System.Runtime.InteropServices.MemoryMarshal.CreateSpan(ref f, QuadFloats);
        q[0] = x0; q[1] = y0; q[2] = u0; q[3] = v0; q[4] = u1; q[5] = v1;
        q[6] = c.X; q[7] = c.Y; q[8] = c.Z; q[9] = layer; q[10] = x1; q[11] = y1;
    }

    private static void Emit(Slot s, float height, Vector3 centre, Vector3 normal, Vector3 up)
    {
        var right = Vector3.Cross(up, normal);
        var need = _vertCount + s.QuadCount * 6 * VertFloats;
        if (need > _verts.Length)
            Array.Resize(ref _verts, need * 3 / 2); // (grows to the most panels shown, then stays)
        var v = _verts;
        var o = _vertCount;
        var q = s.Quads;
        for (var k = 0; k < s.QuadCount; k++)
        {
            var b = k * QuadFloats;
            var at = centre + normal * (q[b + 9] * LayerStep * height);
            var a = at + right * q[b] + up * q[b + 1];
            var r = at + right * q[b + 10] + up * q[b + 1];
            var t = at + right * q[b] + up * q[b + 11];
            var e = at + right * q[b + 10] + up * q[b + 11];
            Vert(v, ref o, a, q[b + 2], q[b + 3], q, b);
            Vert(v, ref o, r, q[b + 4], q[b + 3], q, b);
            Vert(v, ref o, e, q[b + 4], q[b + 5], q, b);
            Vert(v, ref o, a, q[b + 2], q[b + 3], q, b);
            Vert(v, ref o, e, q[b + 4], q[b + 5], q, b);
            Vert(v, ref o, t, q[b + 2], q[b + 5], q, b);
        }
        _vertCount = o;
    }

    private static void Vert(float[] v, ref int o, Vector3 p, float u, float w, float[] q, int b)
    {
        v[o] = p.X; v[o + 1] = p.Y; v[o + 2] = p.Z;
        v[o + 3] = u; v[o + 4] = w;
        v[o + 5] = q[b + 6]; v[o + 6] = q[b + 7]; v[o + 7] = q[b + 8];
        o += VertFloats;
    }

    /// <summary>
    /// A card on a surface anywhere, not on a part: a held tool's display. <paramref name="centre"/>, <paramref name="normal"/> and
    /// <paramref name="up"/> are from the render origin. Add it between <see cref="Begin"/> and <see cref="Flush"/>; call it from one
    /// place: the layout is kept by <paramref name="key"/> from one frame to the next and made again when the card changes. A pass with a camera of its own brackets this with its own Begin and Flush (Program.DrawSandboxTool).
    /// </summary>
    public static void DrawCard(long key, PanelCard card, Vector2 size, int rows, Vector3 centre, Vector3 normal, Vector3 up)
    {
        if (Find(key | 1L << 62) is not { } s)
            return;
        s.Seen = _frame;
        var hash = card.Hash();
        if (hash != s.Hash || s.QuadCount == 0)
        {
            s.Hash = hash;
            Layout(s, card, size, rows);
        }
        Emit(s, size.Y, centre, normal, up);
    }

    /// <summary>
    /// One card, once, with no kept layout: an inventory tile's preview of a display (<see cref="LogicDisplays.Preview"/>). Add it between
    /// <see cref="Begin"/> and <see cref="Flush"/>, with the tile's camera.
    /// </summary>
    public static void DrawOnce(PanelCard card, Vector2 size, int rows, Vector3 centre, Vector3 normal, Vector3 up)
    {
        Layout(_once, card, size, rows);
        Emit(_once, size.Y, centre, normal, up);
    }

    private static readonly Slot _once = new();

    /// <summary>What a card lays out to, for a check: quads in the layout's own form (<see cref="QuadFloats"/> floats each: x0, y0, u0, v0, u1, v1, r, g, b, layer, x1, y1; u0 below 0: solid).</summary>
    public static ReadOnlySpan<float> LaidOut(PanelCard card, Vector2 size, int rows)
    {
        Layout(_once, card, size, rows);
        return _once.Quads.AsSpan(0, _once.QuadCount * QuadFloats);
    }

    /// <summary>Draws what was gathered: one draw, at the end of the opaque pass. The renderer's own shader is back in place afterwards.</summary>
    public static void Flush(Renderer gfx)
    {
        if (_vertCount == 0 || gfx.Recorder != null || _broken)
            return;
        if (!_ready && !Init())
            return;
        _program.Use();
        var vp = gfx.ViewProjRelative;
        Native.glUniformMatrix4fv(_locViewProj, 1, false, &vp.M11);
        _vao.Bind();
        Native.glBindBuffer(GL_ARRAY_BUFFER, _vbo);
        var bytes = _vertCount * sizeof(float);
        if (bytes > _bufferBytes)
            _bufferBytes = bytes * 2;
        Native.glBufferData(GL_ARRAY_BUFFER, _bufferBytes, null, GL_STREAM_DRAW); // orphan: no stall on the last copy
        fixed (float* p = _verts)
            Native.glBufferSubData(GL_ARRAY_BUFFER, 0, bytes, p);
        VideoFeeds.Bind();
        gfx.SetBlend(true);
        if (gfx.MarkGlow)
        {
            // A screen gives light: the back buffer's alpha is its bloom mark (PostFx.cs), at a constant share, as a panel's own alpha is its text's edge.
            Native.glBlendColor(0f, 0f, 0f, 0.6f);
            Native.glBlendFuncSeparate(GL_SRC_ALPHA, GL_ONE_MINUS_SRC_ALPHA, GL_ZERO, GL_CONSTANT_ALPHA);
        }
        Native.glDrawArrays(GL_TRIANGLES, 0, _vertCount / VertFloats);
        Renderer.BlendDefault();
        gfx.SetBlend(false);
        PerfCounters.Draw(_vertCount / VertFloats);
        gfx.Rebind();
    }

    private static bool Init()
    {
        try
        {
            using var vs = CreateShader(GlShaderType.Vertex).SetSource(RenderCaps.Glsl(VertexSrc)).Compile();
            using var fs = CreateShader(GlShaderType.Fragment).SetSource(RenderCaps.Glsl(FragmentSrc)).Compile();
            var program = CreateProgram();
            Native.glAttachShader(program, vs);
            Native.glAttachShader(program, fs);
            Native.glLinkProgram(program);
            int linked;
            Native.glGetProgramiv(program, GL_LINK_STATUS, &linked);
            if (linked == 0)
                throw new InvalidOperationException("panel shader link failed");
            GlDebug.Label(GL_PROGRAM, program.Id, "PanelRender");
            _program = program;
            program.Use();
            fixed (byte* n = "uViewProj\0"u8)
                _locViewProj = Native.glGetUniformLocation(program, n);
            fixed (byte* n = "uFont\0"u8)
                Native.glUniform1i(Native.glGetUniformLocation(program, n), (int)FontUnit);
            fixed (byte* n = "uFeeds\0"u8)
                Native.glUniform1i(Native.glGetUniformLocation(program, n), 15); // (CameraFeeds' pictures)
            fixed (byte* n = "uVideo\0"u8)
                Native.glUniform1i(Native.glGetUniformLocation(program, n), (int)VideoFeeds.Unit);
            GlVertexArray vao = default;
            Native.glGenVertexArrays(1, &vao);
            _vao = vao;
            GlBuffer vbo = default;
            Native.glGenBuffers(1, &vbo);
            _vbo = vbo;
            _vao.Bind();
            Native.glBindBuffer(GL_ARRAY_BUFFER, _vbo);
            var stride = VertFloats * sizeof(float);
            Native.glEnableVertexAttribArray(0);
            Native.glEnableVertexAttribArray(1);
            Native.glEnableVertexAttribArray(2);
            Native.glVertexAttribPointer(0, 3, GL_FLOAT, false, stride, (void*)0);
            Native.glVertexAttribPointer(1, 2, GL_FLOAT, false, stride, (void*)(3 * sizeof(float)));
            Native.glVertexAttribPointer(2, 3, GL_FLOAT, false, stride, (void*)(5 * sizeof(float)));
            var atlas = Atlas(out var w, out var h);
            GlTexture t = default;
            Native.glGenTextures(1, &t);
            Native.glActiveTexture(GL_TEXTURE0 + FontUnit);
            Native.glBindTexture(GL_TEXTURE_2D, t.Id);
            Native.glPixelStorei(GL_UNPACK_ALIGNMENT, 1);
            fixed (byte* p = atlas)
                Native.glTexImage2D(GL_TEXTURE_2D, 0, (int)GL_R8, w, h, 0, GL_RED, GL_UNSIGNED_BYTE, p);
            Native.glPixelStorei(GL_UNPACK_ALIGNMENT, 4);
            Native.glGenerateMipmap(GL_TEXTURE_2D);
            Native.glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, (int)GL_LINEAR_MIPMAP_LINEAR);
            Native.glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, (int)GL_LINEAR);
            Native.glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, (int)GL_CLAMP_TO_EDGE);
            Native.glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, (int)GL_CLAMP_TO_EDGE);
            Native.glActiveTexture(GL_TEXTURE0);
            _ready = true;
            return true;
        }
        catch (Exception e)
        {
            _broken = true;
            Console.WriteLine($"[panels] off: {e.Message}");
            return false;
        }
    }

    /// <summary>The font as an R8 picture, unpacked from <see cref="PanelFontData.Atlas"/>: the first row of cells at the top of the texture's v.</summary>
    private static byte[] Atlas(out int w, out int h)
    {
        w = PanelFontData.Columns * PanelFontData.CellW;
        h = PanelFontData.Rows * PanelFontData.CellH;
        var runs = Convert.FromBase64String(PanelFontData.Atlas);
        var a = new byte[w * h];
        var at = 0;
        for (var k = 0; k + 1 < runs.Length; k += 2)
        {
            Array.Fill(a, runs[k + 1], at, runs[k]);
            at += runs[k];
        }
        if (at != a.Length)
            throw new InvalidOperationException($"the panel font holds {at} texels, not {a.Length}");
        return a;
    }
}
