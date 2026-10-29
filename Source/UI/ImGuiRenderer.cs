using Brutal.ImGuiApi;
using Brutal.OpenGlApi;
using static Brutal.OpenGlApi.OpenGl;

namespace CallOfSlop;

/// <summary>
/// Draws ImGui's lists with OpenGL (in place of Brutal's <c>OpenGlBackend</c>, a port of imgui_impl_opengl3). The same
/// shader, blending and scissoring, with two differences (2026-10-29, perf/imgui-spike):
/// <list type="bullet">
/// <item>No GL state is read back. The C++ backend saves and restores the whole GL state with about twenty
/// glGetIntegerv / glIsEnabled calls a frame; with a threaded GL driver (NVIDIA's "threaded optimisation", Mesa's
/// glthread) every one is a round trip that waits for the driver thread to finish the whole frame queued so far, so the
/// UI's draw took whatever the 3D frame's submission still owed: small on average, several milliseconds at times. Here
/// the state is set outright and left as the game expects it (<see cref="Leave"/>), and the game's renderer rebinds its
/// own program after (<c>Renderer.Rebind</c>).</item>
/// <item>One vertex and one index upload a frame (one orphaned buffer each, every list copied into it), instead of one
/// glBufferData per draw list.</item>
/// </list>
/// Textures follow ImGui 1.92's protocol (<c>ImGuiBackendFlags.RendererHasTextures</c>): the font atlas is made, updated
/// in rectangles and destroyed as ImGui asks. Other textures (map thumbnails, avatars, the splash) are GL texture names
/// in <c>ImTextureID</c>, as before. Every GL call is on the main thread.
/// </summary>
public sealed unsafe class ImGuiRenderer : IDisposable
{
    private const string VertexSrc = """
        #version 410 core
        layout (location = 0) in vec2 Position;
        layout (location = 1) in vec2 UV;
        layout (location = 2) in vec4 Color;
        uniform mat4 ProjMtx;
        out vec2 Frag_UV;
        out vec4 Frag_Color;
        void main()
        {
            Frag_UV = UV;
            Frag_Color = Color;
            gl_Position = ProjMtx * vec4(Position.xy, 0, 1);
        }
        """;

    private const string FragmentSrc = """
        #version 410 core
        in vec2 Frag_UV;
        in vec4 Frag_Color;
        uniform sampler2D Texture;
        layout (location = 0) out vec4 Out_Color;
        void main()
        {
            Out_Color = Frag_Color * texture(Texture, Frag_UV.st);
        }
        """;

    private GlProgram _program;
    private GlVertexArray _vao;
    private GlBuffer _vbo, _ebo;
    private int _locProj, _locTex;
    private nint _vboBytes, _eboBytes;
    private bool _ready;

    /// <summary>Atlas texture creations, rectangle updates and destructions so far (the perf log line, the self-test).</summary>
    public static long TexCreates, TexUpdates, TexDestroys;

    /// <summary>Draw lists and draw calls in the last frame.</summary>
    public int Lists, Draws;

    public ImGuiRenderer()
    {
        var io = ImGui.GetIO();
        io.BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset | ImGuiBackendFlags.RendererHasTextures;
        int max = 0;
        Native.glGetIntegerv(GL_MAX_TEXTURE_SIZE, &max); // (once, at start)
        var pio = ImGui.GetPlatformIO();
        pio.Renderer_TextureMaxWidth = pio.Renderer_TextureMaxHeight = max > 0 ? max : 4096;
    }

    private void Make()
    {
        using var vs = CreateShader(GlShaderType.Vertex).SetSource(RenderCaps.Glsl(VertexSrc)).Compile();
        using var fs = CreateShader(GlShaderType.Fragment).SetSource(RenderCaps.Glsl(FragmentSrc)).Compile();
        _program = CreateProgram().Attach(vs, fs).Link();
        DetachShader(_program, vs);
        DetachShader(_program, fs);
        GlDebug.Label(GL_PROGRAM, _program.Id, "ImGui");
        fixed (byte* p = "ProjMtx\0"u8)
            _locProj = Native.glGetUniformLocation(_program, p);
        fixed (byte* p = "Texture\0"u8)
            _locTex = Native.glGetUniformLocation(_program, p);
        _vao = GenVertexArray();
        _vao.Bind();
        GlDebug.Label(GL_VERTEX_ARRAY, _vao.Id, "ImGui");
        _vbo = GenBuffer();
        _ebo = GenBuffer();
        Native.glBindBuffer(GL_ARRAY_BUFFER, _vbo);
        Native.glBindBuffer(GL_ELEMENT_ARRAY_BUFFER, _ebo); // (VAO state)
        GlDebug.Label(GL_BUFFER, _vbo.Id, "ImGui vertices");
        GlDebug.Label(GL_BUFFER, _ebo.Id, "ImGui indices");
        var stride = sizeof(ImDrawVert);
        Native.glEnableVertexAttribArray(0);
        Native.glEnableVertexAttribArray(1);
        Native.glEnableVertexAttribArray(2);
        Native.glVertexAttribPointer(0, 2, GL_FLOAT, false, stride, (void*)0);
        Native.glVertexAttribPointer(1, 2, GL_FLOAT, false, stride, (void*)8);
        Native.glVertexAttribPointer(2, 4, GL_UNSIGNED_BYTE, true, stride, (void*)16);
        Native.glBindVertexArray(default);
        _ready = true;
    }

    /// <summary>Draws a frame's lists (after <c>ImGui.Render</c>) to the default framebuffer.</summary>
    public void Render(ImDrawDataPtr dd)
    {
        Lists = Draws = 0;
        if (!_ready)
            Make();
        if (!dd.Textures.IsNull())
        {
            ref var texs = ref dd.Textures.Value;
            for (var i = 0; i < texs.Count; i++)
                if (texs[i].Status != ImTextureStatus.OK)
                    UpdateTexture(texs[i]);
        }
        var fbW = (int)(dd.DisplaySize.X * dd.FramebufferScale.X);
        var fbH = (int)(dd.DisplaySize.Y * dd.FramebufferScale.Y);
        if (fbW <= 0 || fbH <= 0 || dd.TotalVtxCount == 0)
            return;

        // State: alpha blending, no culling or depth, scissor on (the game's own state is put back in Leave).
        Native.glEnable(GlCapability.Blend);
        Native.glBlendEquation(GL_FUNC_ADD);
        Native.glBlendFuncSeparate(GL_SRC_ALPHA, GL_ONE_MINUS_SRC_ALPHA, GL_ONE, GL_ONE_MINUS_SRC_ALPHA);
        Native.glDisable(GlCapability.CullFace);
        Native.glDisable(GlCapability.DepthTest);
        Native.glEnable(GlCapability.ScissorTest);
        Native.glViewport(0, 0, fbW, fbH);
        float l = dd.DisplayPos.X, r = dd.DisplayPos.X + dd.DisplaySize.X, t = dd.DisplayPos.Y, b = dd.DisplayPos.Y + dd.DisplaySize.Y;
        var proj = stackalloc float[16]
        {
            2f / (r - l), 0, 0, 0,
            0, 2f / (t - b), 0, 0,
            0, 0, -1, 0,
            (r + l) / (l - r), (t + b) / (b - t), 0, 1,
        };
        _program.Use();
        Native.glUniform1i(_locTex, 0);
        Native.glUniformMatrix4fv(_locProj, 1, false, proj);
        Native.glActiveTexture(GL_TEXTURE0);
        _vao.Bind();
        Native.glBindBuffer(GL_ARRAY_BUFFER, _vbo);

        // Every list into one vertex and one index buffer (orphaned: the driver never waits for last frame's).
        var vBytes = (nint)dd.TotalVtxCount * sizeof(ImDrawVert);
        var iBytes = (nint)dd.TotalIdxCount * sizeof(ushort);
        if (vBytes > _vboBytes)
            _vboBytes = Math.Max(vBytes, _vboBytes * 2);
        if (iBytes > _eboBytes)
            _eboBytes = Math.Max(iBytes, _eboBytes * 2);
        Native.glBufferData(GL_ARRAY_BUFFER, _vboBytes, null, GL_STREAM_DRAW);
        Native.glBufferData(GL_ELEMENT_ARRAY_BUFFER, _eboBytes, null, GL_STREAM_DRAW);
        ref var lists = ref dd.CmdLists;
        nint vOff = 0, iOff = 0;
        for (var n = 0; n < lists.Count; n++)
        {
            var list = lists[n];
            ref var vtx = ref list.VtxBuffer;
            ref var idx = ref list.IdxBuffer;
            var vb = (nint)vtx.Count * sizeof(ImDrawVert);
            var ib = (nint)idx.Count * sizeof(ushort);
            if (vb > 0)
                Native.glBufferSubData(GL_ARRAY_BUFFER, vOff, vb, vtx.DataRaw);
            if (ib > 0)
                Native.glBufferSubData(GL_ELEMENT_ARRAY_BUFFER, iOff, ib, idx.DataRaw);
            vOff += vb;
            iOff += ib;
        }

        var clipOff = dd.DisplayPos;
        var clipScale = dd.FramebufferScale;
        nint bound = -1;
        int vBase = 0, iBase = 0;
        for (var n = 0; n < lists.Count; n++)
        {
            var list = lists[n];
            ref var cmds = ref list.CmdBuffer;
            for (var c = 0; c < cmds.Count; c++)
            {
                ref var cmd = ref cmds.DataRaw[c];
                if (cmd.UserCallback != 0)
                    continue; // (the game adds no callbacks)
                var x0 = (cmd.ClipRect.X - clipOff.X) * clipScale.X;
                var y0 = (cmd.ClipRect.Y - clipOff.Y) * clipScale.Y;
                var x1 = (cmd.ClipRect.Z - clipOff.X) * clipScale.X;
                var y1 = (cmd.ClipRect.W - clipOff.Y) * clipScale.Y;
                if (x1 <= x0 || y1 <= y0 || cmd.ElemCount == 0)
                    continue;
                Native.glScissor((int)x0, (int)(fbH - y1), (int)(x1 - x0), (int)(y1 - y0));
                var tex = cmd.GetTexID().Value;
                if (tex != bound)
                {
                    Native.glBindTexture(GL_TEXTURE_2D, (uint)tex);
                    bound = tex;
                }
                Native.glDrawElementsBaseVertex(GL_TRIANGLES, (int)cmd.ElemCount, GL_UNSIGNED_SHORT, (void*)((iBase + cmd.IdxOffset) * sizeof(ushort)), (int)(vBase + cmd.VtxOffset));
                Draws++;
            }
            vBase += list.VtxBuffer.Count;
            iBase += list.IdxBuffer.Count;
            Lists++;
        }
        Leave();
    }

    /// <summary>
    /// Puts back the state the game draws with (Renderer.Init and BeginFrame): no blending (with its usual blend
    /// function), depth test on, no scissor; the VAO and the 2D texture unbound. The game's program is rebound by the caller.
    /// </summary>
    private static void Leave()
    {
        Native.glDisable(GlCapability.ScissorTest);
        Native.glDisable(GlCapability.Blend);
        Native.glBlendFunc(GL_SRC_ALPHA, GL_ONE_MINUS_SRC_ALPHA);
        Native.glEnable(GlCapability.DepthTest);
        Native.glBindTexture(GL_TEXTURE_2D, 0);
        Native.glBindVertexArray(default);
    }

    private static void UpdateTexture(ImTextureDataPtr tex)
    {
        switch (tex.Status)
        {
            case ImTextureStatus.WantCreate:
            {
                GlTexture id = default;
                Native.glGenTextures(1, &id);
                Native.glBindTexture(GL_TEXTURE_2D, id.Id);
                Native.glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, (int)GL_LINEAR);
                Native.glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, (int)GL_LINEAR);
                Native.glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, (int)GL_CLAMP_TO_EDGE);
                Native.glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, (int)GL_CLAMP_TO_EDGE);
                Native.glPixelStorei(GL_UNPACK_ALIGNMENT, 1);
                Native.glPixelStorei(GL_UNPACK_ROW_LENGTH, 0);
                Native.glTexImage2D(GL_TEXTURE_2D, 0, (int)GL_RGBA8, tex.Width, tex.Height, 0, GL_RGBA, GL_UNSIGNED_BYTE, (void*)tex.Pixels.Pointer);
                GlDebug.Label(GL_TEXTURE, id.Id, "ImGui font atlas");
                tex.SetTexID(new ImTextureID((nint)id.Id));
                tex.SetStatus(ImTextureStatus.OK);
                TexCreates++;
                break;
            }
            case ImTextureStatus.WantUpdates:
            {
                Native.glBindTexture(GL_TEXTURE_2D, (uint)tex.TexID.Value);
                Native.glPixelStorei(GL_UNPACK_ALIGNMENT, 1);
                Native.glPixelStorei(GL_UNPACK_ROW_LENGTH, tex.Width);
                ref var ups = ref tex.Updates;
                for (var i = 0; i < ups.Count; i++)
                {
                    var u = ups[i];
                    var px = tex.Pixels.Pointer + ((long)u.y * tex.Width + u.x) * tex.BytesPerPixel;
                    Native.glTexSubImage2D(GL_TEXTURE_2D, 0, u.x, u.y, u.w, u.h, GL_RGBA, GL_UNSIGNED_BYTE, px);
                }
                Native.glPixelStorei(GL_UNPACK_ROW_LENGTH, 0);
                tex.SetStatus(ImTextureStatus.OK);
                TexUpdates++;
                break;
            }
            case ImTextureStatus.WantDestroy when tex.UnusedFrames > 0:
                Destroy(tex);
                break;
        }
    }

    private static void Destroy(ImTextureDataPtr tex)
    {
        var id = new GlTexture((uint)tex.TexID.Value);
        if (id.Id != 0)
            Native.glDeleteTextures(1, &id);
        tex.SetTexID(ImTextureID.Invalid);
        tex.SetStatus(ImTextureStatus.Destroyed);
        TexDestroys++;
    }

    public void Dispose()
    {
        ref var texs = ref ImGui.GetPlatformIO().Textures;
        for (var i = 0; i < texs.Count; i++)
            if (texs[i].RefCount == 1)
                Destroy(texs[i]);
        if (!_ready)
            return;
        var vbo = _vbo;
        var ebo = _ebo;
        Native.glDeleteBuffers(1, &vbo);
        Native.glDeleteBuffers(1, &ebo);
        var vao = _vao;
        Native.glDeleteVertexArrays(1, &vao);
        Native.glDeleteProgram(_program);
        _ready = false;
    }
}
