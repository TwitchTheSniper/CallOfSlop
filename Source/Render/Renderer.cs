using System.Numerics;
using Brutal.OpenGlApi;
using static Brutal.OpenGlApi.OpenGl;

namespace CallOfSlop;

/// <summary>
/// Minimal OpenGL 3.3 renderer. Everything in the game is a coloured box: the room, the crates,
/// the players, the gun, the tracers and even the HUD.
/// Matrices are System.Numerics (row-vector, M = S * R * T) uploaded untransposed, which GLSL
/// reads as the transpose, so "uMVP * vec4(p, 1)" in the shader is correct.
/// </summary>
public sealed unsafe class Renderer
{
    private const string VertexSrc = """
        #version 330 core
        layout(location = 0) in vec3 aPos;
        layout(location = 1) in vec3 aNormal;
        uniform mat4 uMVP;
        uniform mat4 uModel;
        out vec3 vWorld;
        out vec3 vNormal;
        void main()
        {
            vWorld = (uModel * vec4(aPos, 1.0)).xyz;
            vNormal = mat3(uModel) * aNormal;
            gl_Position = uMVP * vec4(aPos, 1.0);
        }
        """;

    // uMode: 0 = lit, 1 = lit + world-space texture (layer uLayer of uTex), 2 = unlit (HUD, tracers, muzzle flash)
    private const string FragmentSrc = """
        #version 330 core
        in vec3 vWorld;
        in vec3 vNormal;
        uniform vec4 uColor;
        uniform float uMode;
        uniform float uLayer;
        uniform sampler2DArray uTex;
        uniform vec3 uCam;
        uniform vec3 uFog;
        uniform vec2 uFogRange; // start, end (metres); end <= start turns fog off
        out vec4 FragColor;

        void main()
        {
            vec3 col = uColor.rgb;
            if (uMode > 1.5)
            {
                FragColor = uColor;
                return;
            }

            vec3 n = normalize(vNormal);
            if (uMode > 0.5)
            {
                // Planar mapping in world space, one repeat per 2 m. Side faces keep "up" as v.
                vec2 uv = abs(n.y) > 0.5 ? vWorld.xz : (abs(n.x) > 0.5 ? vec2(vWorld.z, vWorld.y) : vec2(vWorld.x, vWorld.y));
                float t = texture(uTex, vec3(uv * 0.5, uLayer)).r;
                col *= t * 1.25;
            }

            vec3 lightDir = normalize(vec3(0.35, 1.0, 0.25));
            float diff = max(dot(n, lightDir), 0.0);
            float rim = 0.12 * (1.0 - abs(n.y));
            col *= 0.45 + 0.55 * diff + rim;
            if (uFogRange.y > uFogRange.x)
            {
                // Distance haze towards the sky colour, so big maps fade out instead of ending abruptly.
                float f = clamp((distance(vWorld, uCam) - uFogRange.x) / (uFogRange.y - uFogRange.x), 0.0, 1.0);
                col = mix(col, uFog, f * 0.85);
            }
            FragColor = vec4(col, uColor.a);
        }
        """;

    private GlProgram _program;
    private GlVertexArray _vao;
    private GlBuffer _vbo;
    private int _locMvp, _locModel, _locColor, _locMode, _locLayer, _locTex, _locCam, _locFog, _locFogRange;
    private uint _texture;

    private Matrix4x4 _viewProj = Matrix4x4.Identity;

    public void Init()
    {
        // --- Shaders -----------------------------------------------------------
        var vs = CreateShader(GlShaderType.Vertex).SetSource(VertexSrc).Compile();
        var fs = CreateShader(GlShaderType.Fragment).SetSource(FragmentSrc).Compile();

        _program = CreateProgram();
        Native.glAttachShader(_program, vs);
        Native.glAttachShader(_program, fs);
        Native.glLinkProgram(_program);

        int linked;
        Native.glGetProgramiv(_program, GL_LINK_STATUS, &linked);
        if (linked == 0)
        {
            var log = stackalloc byte[2048];
            int len;
            Native.glGetProgramInfoLog(_program, 2048, &len, log);
            throw new InvalidOperationException(
                "Shader link failed: " + System.Text.Encoding.UTF8.GetString(log, Math.Max(len, 0))
            );
        }
        vs.Dispose();
        fs.Dispose();

        _locMvp = Uniform("uMVP\0"u8);
        _locModel = Uniform("uModel\0"u8);
        _locColor = Uniform("uColor\0"u8);
        _locMode = Uniform("uMode\0"u8);
        _locLayer = Uniform("uLayer\0"u8);
        _locTex = Uniform("uTex\0"u8);
        _locCam = Uniform("uCam\0"u8);
        _locFog = Uniform("uFog\0"u8);
        _locFogRange = Uniform("uFogRange\0"u8);
        CreateTextures();

        // --- Unit cube (36 verts, position + normal) ----------------------------
        var verts = BuildCube();
        _vao = GenVertexArray();
        _vao.Bind();
        _vbo = GenBuffer();
        Native.glBindBuffer(GL_ARRAY_BUFFER, _vbo);
        fixed (float* p = verts)
            Native.glBufferData(GL_ARRAY_BUFFER, verts.Length * sizeof(float), p, GL_STATIC_DRAW);

        const int stride = 6 * sizeof(float);
        Native.glVertexAttribPointer(0, 3, GL_FLOAT, false, stride, (void*)0);
        EnableVertexAttribArray(0);
        Native.glVertexAttribPointer(1, 3, GL_FLOAT, false, stride, (void*)(3 * sizeof(float)));
        EnableVertexAttribArray(1);

        Enable(GlCapability.DepthTest);
        DepthFunc(GlFunc.LessEqual);
        BlendFunc(GL_SRC_ALPHA, GL_ONE_MINUS_SRC_ALPHA);
    }

    private int Uniform(ReadOnlySpan<byte> nameZ)
    {
        fixed (byte* p = nameZ)
            return Native.glGetUniformLocation(_program, p);
    }

    public void BeginFrame(int width, int height, Vector3 clear)
    {
        Viewport(0, 0, width, height);
        Native.glClearColor(clear.X, clear.Y, clear.Z, 1f);
        Clear(GlClearBit.Color | GlClearBit.Depth);
        _program.Use();
        _vao.Bind();
        Native.glActiveTexture(GL_TEXTURE0);
        Native.glBindTexture(GL_TEXTURE_2D_ARRAY, _texture);
        Native.glUniform1i(_locTex, 0);
        Enable(GlCapability.DepthTest);
        Native.glDisable(GlCapability.Blend);
    }

    public void SetCamera(Matrix4x4 view, Matrix4x4 proj)
    {
        _viewProj = view * proj;
        if (Matrix4x4.Invert(view, out var inv))
            Native.glUniform3f(_locCam, inv.M41, inv.M42, inv.M43);
    }

    /// <summary>Distance fog towards <paramref name="color"/> between start and end metres (end &lt;= start: off).</summary>
    public void SetFog(Vector3 color, float start, float end)
    {
        Native.glUniform3f(_locFog, color.X, color.Y, color.Z);
        Native.glUniform2f(_locFogRange, start, end);
    }

    public void ClearDepthBuffer() => Clear(GlClearBit.Depth);

    /// <summary>Restrict drawing to a rectangle of the framebuffer (pixels, origin bottom-left).</summary>
    public void SetViewport(int x, int y, int w, int h) => Viewport(x, y, Math.Max(1, w), Math.Max(1, h));

    public void SetDepthTest(bool on)
    {
        if (on)
            Enable(GlCapability.DepthTest);
        else
            Native.glDisable(GlCapability.DepthTest);
    }

    /// <summary>Turn depth writes off for soft transparent things like smoke.</summary>
    public void SetDepthWrite(bool on) => DepthMask(on);

    public void SetBlend(bool on)
    {
        if (on)
            Enable(GlCapability.Blend);
        else
            Native.glDisable(GlCapability.Blend);
    }

    /// <summary>Draws the unit cube transformed by <paramref name="model"/>.</summary>
    public void Draw(in Matrix4x4 model, Vector3 color, float alpha = 1f, int mode = 0, Material material = Material.Grid)
    {
        Native.glUniform1f(_locLayer, (float)material);
        var mvp = model * _viewProj;
        var m = model;
        Native.glUniformMatrix4fv(_locMvp, 1, false, &mvp.M11);
        Native.glUniformMatrix4fv(_locModel, 1, false, &m.M11);
        Native.glUniform4f(_locColor, color.X, color.Y, color.Z, alpha);
        Native.glUniform1f(_locMode, mode);
        Native.glDrawArrays(GL_TRIANGLES, 0, 36);
    }

    public void DrawBox(Box box, Vector3 color, int mode = 0, Material material = Material.Grid) =>
        Draw(Matrix4x4.CreateScale(box.Half * 2) * Matrix4x4.CreateTranslation(box.Center), color, 1f, mode, material);

    /// <summary>Uploads the generated textures (see <see cref="Textures"/>) as one 2D texture array with mipmaps.</summary>
    private void CreateTextures()
    {
        var pixels = Textures.Generate();
        GlTexture tex = default;
        Native.glGenTextures(1, &tex);
        _texture = tex.Id;
        Native.glActiveTexture(GL_TEXTURE0);
        Native.glBindTexture(GL_TEXTURE_2D_ARRAY, _texture);
        Native.glPixelStorei(GL_UNPACK_ALIGNMENT, 1);
        fixed (byte* p = pixels)
            Native.glTexImage3D(GL_TEXTURE_2D_ARRAY, 0, (int)GL_RGBA8, Textures.Size, Textures.Size, (int)Material.Count, 0, GL_RGBA, GL_UNSIGNED_BYTE, p);
        Native.glGenerateMipmap(GL_TEXTURE_2D_ARRAY);
        // Crisp texels up close (blocky style), smooth mipmaps far away so it doesn't shimmer.
        Native.glTexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_MIN_FILTER, (int)GL_LINEAR_MIPMAP_LINEAR);
        Native.glTexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_MAG_FILTER, (int)GL_NEAREST);
        Native.glTexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_WRAP_S, (int)GL_REPEAT);
        Native.glTexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_WRAP_T, (int)GL_REPEAT);
        Native.glTexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_MAX_ANISOTROPY, 8);
    }

    /// <summary>Screen-space rectangle in pixels (origin bottom-left). Use with an ortho camera.</summary>
    public void Rect(float x, float y, float w, float h, Vector3 color, float alpha = 1f) =>
        Draw(
            Matrix4x4.CreateScale(w, h, 1) * Matrix4x4.CreateTranslation(x + w / 2, y + h / 2, 0),
            color,
            alpha,
            mode: 2
        );

    private static float[] BuildCube()
    {
        // Each face: normal, then 2 triangles.
        (Vector3 n, Vector3 u, Vector3 v)[] faces =
        [
            (Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ),
            (-Vector3.UnitX, Vector3.UnitZ, Vector3.UnitY),
            (Vector3.UnitY, Vector3.UnitZ, Vector3.UnitX),
            (-Vector3.UnitY, Vector3.UnitX, Vector3.UnitZ),
            (Vector3.UnitZ, Vector3.UnitX, Vector3.UnitY),
            (-Vector3.UnitZ, Vector3.UnitY, Vector3.UnitX),
        ];

        var data = new List<float>(36 * 6);
        foreach (var (n, u, v) in faces)
        {
            var c = n * 0.5f;
            Vector3 a = c - u * 0.5f - v * 0.5f,
                b = c + u * 0.5f - v * 0.5f,
                d = c + u * 0.5f + v * 0.5f,
                e = c - u * 0.5f + v * 0.5f;
            Vector3[] corners = [a, b, d, a, d, e];
            foreach (var p in corners)
                data.AddRange([p.X, p.Y, p.Z, n.X, n.Y, n.Z]);
        }
        return data.ToArray();
    }
}
