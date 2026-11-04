using System.Numerics;
using Brutal.OpenGlApi;
using static Brutal.OpenGlApi.OpenGl;

namespace CallOfSlop;

/// <summary>
/// The display panels' text and shapes (docs/sandbox/CONSTRUCTS.md, "Display panels"). ImGui draws over the finished picture
/// and only between NewFrame and Render, so panels can't use it: they are plain world geometry, depth-tested, drawn at the end
/// of the opaque pass with one small shader and one font texture, all visible panels in one draw. The font is a 5 by 7
/// bitmap font held here, drawn once into a mipmapped atlas at start-up, so there is no text shaping and no render target per panel.
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
    private const int QuadFloats = 12; // x, y, u0, v0, u1, v1, r, g, b, layer; x1, y1 (panel metres)
    private const int VertFloats = 8; // xyz, uv, rgb
    private const int Cols = 16, GlyphRows = 6, CellW = 6, CellH = 8, Scale = 4; // the atlas: font pixels a cell, texels a font pixel
    private const float LayerStep = 0.004f; // what is on the screen stands this share of its height off it
    private const uint FontUnit = 8;

    /// <summary>Panels drawn, and skipped for range, for facing away and for being out of view, last frame; cards laid out since the start (the F9 window, a throwaway check).</summary>
    public static int LastDrawn, LastRange, LastBack, LastView;
    public static long Layouts;

    private static readonly byte[] Font =
    [
        0x00,0x00,0x00,0x00,0x00, 0x00,0x00,0x5F,0x00,0x00, 0x00,0x07,0x00,0x07,0x00, 0x14,0x7F,0x14,0x7F,0x14, 0x24,0x2A,0x7F,0x2A,0x12, 0x23,0x13,0x08,0x64,0x62, 0x36,0x49,0x55,0x22,0x50, 0x00,0x05,0x03,0x00,0x00,
        0x00,0x1C,0x22,0x41,0x00, 0x00,0x41,0x22,0x1C,0x00, 0x14,0x08,0x3E,0x08,0x14, 0x08,0x08,0x3E,0x08,0x08, 0x00,0x50,0x30,0x00,0x00, 0x08,0x08,0x08,0x08,0x08, 0x00,0x60,0x60,0x00,0x00, 0x20,0x10,0x08,0x04,0x02,
        0x3E,0x51,0x49,0x45,0x3E, 0x00,0x42,0x7F,0x40,0x00, 0x42,0x61,0x51,0x49,0x46, 0x21,0x41,0x45,0x4B,0x31, 0x18,0x14,0x12,0x7F,0x10, 0x27,0x45,0x45,0x45,0x39, 0x3C,0x4A,0x49,0x49,0x30, 0x01,0x71,0x09,0x05,0x03,
        0x36,0x49,0x49,0x49,0x36, 0x06,0x49,0x49,0x29,0x1E, 0x00,0x36,0x36,0x00,0x00, 0x00,0x56,0x36,0x00,0x00, 0x08,0x14,0x22,0x41,0x00, 0x14,0x14,0x14,0x14,0x14, 0x00,0x41,0x22,0x14,0x08, 0x02,0x01,0x51,0x09,0x06,
        0x32,0x49,0x79,0x41,0x3E, 0x7E,0x11,0x11,0x11,0x7E, 0x7F,0x49,0x49,0x49,0x36, 0x3E,0x41,0x41,0x41,0x22, 0x7F,0x41,0x41,0x22,0x1C, 0x7F,0x49,0x49,0x49,0x41, 0x7F,0x09,0x09,0x09,0x01, 0x3E,0x41,0x49,0x49,0x7A,
        0x7F,0x08,0x08,0x08,0x7F, 0x00,0x41,0x7F,0x41,0x00, 0x20,0x40,0x41,0x3F,0x01, 0x7F,0x08,0x14,0x22,0x41, 0x7F,0x40,0x40,0x40,0x40, 0x7F,0x02,0x0C,0x02,0x7F, 0x7F,0x04,0x08,0x10,0x7F, 0x3E,0x41,0x41,0x41,0x3E,
        0x7F,0x09,0x09,0x09,0x06, 0x3E,0x41,0x51,0x21,0x5E, 0x7F,0x09,0x19,0x29,0x46, 0x46,0x49,0x49,0x49,0x31, 0x01,0x01,0x7F,0x01,0x01, 0x3F,0x40,0x40,0x40,0x3F, 0x1F,0x20,0x40,0x20,0x1F, 0x3F,0x40,0x38,0x40,0x3F,
        0x63,0x14,0x08,0x14,0x63, 0x07,0x08,0x70,0x08,0x07, 0x61,0x51,0x49,0x45,0x43, 0x00,0x7F,0x41,0x41,0x00, 0x02,0x04,0x08,0x10,0x20, 0x00,0x41,0x41,0x7F,0x00, 0x04,0x02,0x01,0x02,0x04, 0x40,0x40,0x40,0x40,0x40,
        0x00,0x01,0x02,0x04,0x00, 0x20,0x54,0x54,0x54,0x78, 0x7F,0x48,0x44,0x44,0x38, 0x38,0x44,0x44,0x44,0x20, 0x38,0x44,0x44,0x48,0x7F, 0x38,0x54,0x54,0x54,0x18, 0x08,0x7E,0x09,0x01,0x02, 0x0C,0x52,0x52,0x52,0x3E,
        0x7F,0x08,0x04,0x04,0x78, 0x00,0x44,0x7D,0x40,0x00, 0x20,0x40,0x44,0x3D,0x00, 0x7F,0x10,0x28,0x44,0x00, 0x00,0x41,0x7F,0x40,0x00, 0x7C,0x04,0x18,0x04,0x78, 0x7C,0x08,0x04,0x04,0x78, 0x38,0x44,0x44,0x44,0x38,
        0x7C,0x14,0x14,0x14,0x08, 0x08,0x14,0x14,0x18,0x7C, 0x7C,0x08,0x04,0x04,0x08, 0x48,0x54,0x54,0x54,0x20, 0x04,0x3F,0x44,0x40,0x20, 0x3C,0x40,0x40,0x20,0x7C, 0x1C,0x20,0x40,0x20,0x1C, 0x3C,0x40,0x30,0x40,0x3C,
        0x44,0x28,0x10,0x28,0x44, 0x0C,0x50,0x50,0x50,0x3C, 0x44,0x64,0x54,0x4C,0x44, 0x00,0x08,0x36,0x41,0x00, 0x00,0x00,0x7F,0x00,0x00, 0x00,0x41,0x36,0x08,0x00, 0x10,0x08,0x08,0x10,0x08,
    ];

    private sealed class Slot
    {
        public long Key;
        public readonly PanelCard Card = new();
        public float[] Quads = new float[64 * QuadFloats];
        public int QuadCount, Hash, Seen, Stamp;
        public double Filled;
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
        out vec4 FragColor;
        void main()
        {
            float a = vUv.x < 0.0 ? 1.0 : smoothstep(0.2, 0.55, texture(uFont, vUv).r);
            if (a < 0.02)
                discard;
            FragColor = vec4(vColour, a);
        }
        """;

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
                var key = (long)c.Id << 24 | (long)part << 8 | (uint)n;
                if (Find(key) is not { } s)
                    continue;
                s.Seen = _frame;
                if (now - s.Filled >= 1 / FillHz || s.Stamp != c.RebuildStamp)
                    Fill(world, c, part, panel, s, now);
                Emit(s, panel.Size.Y, centre, normal, Vector3.TransformNormal(ConstructParts.DirVector(Rot24.ApplyDir(p.Rot, panel.Up)), seg));
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
        return _slots[key] = s;
    }

    private static void Fill(ConstructWorld world, Construct c, int part, PanelDef panel, Slot s, double now)
    {
        s.Filled = now;
        s.Stamp = c.RebuildStamp;
        s.Card.Clear();
        panel.Fill!(world, c, part, panel, s.Card);
        var hash = s.Card.Hash();
        if (hash == s.Hash && s.QuadCount > 0)
            return;
        s.Hash = hash;
        Layout(s, s.Card, panel.Size, panel.Rows);
    }

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
        var bright = new Vector3(0.9f, 0.95f, 0.97f);
        for (var l = 0; l < rows && l < PanelCard.MaxLines; l++)
        {
            var left = -hw + pad + (l > 0 && l < 3 ? indent : 0);
            var y = bottom + (rows - 1 - l) * row + (row - glyphH) * 0.5f;
            var line = card.Line(l);
            var room = (int)((hw - pad - left) / glyphW + 0.2f);
            var colour = l == 0 ? state : bright;
            for (var k = 0; k < line.Length && k < room; k++)
                if (line[k] > 32)
                    Glyph(s, line[k], left + k * glyphW, y, glyphW, glyphH, colour);
        }
    }

    private static ref float Next(Slot s)
    {
        if ((s.QuadCount + 1) * QuadFloats > s.Quads.Length)
            Array.Resize(ref s.Quads, s.Quads.Length * 2); // (only when a card lays out)
        return ref s.Quads[s.QuadCount++ * QuadFloats];
    }

    private static void Solid(Slot s, float x0, float y0, float x1, float y1, Vector3 colour, int layer) =>
        Quad(s, x0, y0, x1, y1, -1, 0, -1, 0, colour, layer);

    private static void Glyph(Slot s, char ch, float x, float y, float w, float h, Vector3 colour)
    {
        var g = ch - 32;
        var u = 1f / Cols;
        var v = 1f / GlyphRows;
        var col = g % Cols;
        var row = g / Cols;
        Quad(s, x, y, x + w, y + h, col * u, (row + 1) * v, (col + 1) * u, row * v, colour, 2);
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
        gfx.SetBlend(true);
        Native.glDrawArrays(GL_TRIANGLES, 0, _vertCount / VertFloats);
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

    /// <summary>The font as an R8 picture: 16 by 6 cells, each 6 by 8 font pixels (the last column and row left clear), the first row of cells at the top of the texture's v.</summary>
    private static byte[] Atlas(out int w, out int h)
    {
        w = Cols * CellW * Scale;
        h = GlyphRows * CellH * Scale;
        var a = new byte[w * h];
        for (var g = 0; g < 95; g++)
        {
            var ox = g % Cols * CellW * Scale;
            var oy = g / Cols * CellH * Scale;
            for (var x = 0; x < 5; x++)
            {
                var col = Font[g * 5 + x];
                for (var y = 0; y < 7; y++)
                    if ((col >> y & 1) != 0)
                        for (var sy = 0; sy < Scale; sy++)
                            for (var sx = 0; sx < Scale; sx++)
                                a[(oy + y * Scale + sy) * w + ox + x * Scale + sx] = 255;
            }
        }
        return a;
    }
}
