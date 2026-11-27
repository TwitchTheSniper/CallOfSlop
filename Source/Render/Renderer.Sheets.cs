using System.Numerics;
using Brutal.OpenGlApi;
using static Brutal.OpenGlApi.OpenGl;

namespace CallOfSlop;

/// <summary>
/// Floor sheets on the GPU (ConstructRenderSheets.cs, docs/render/MERGED-MESHES.md "Floor sheets"): a merged chunk's sheet slots
/// are one group of shape <see cref="SheetShape"/>, a cube scaled to a rectangle of tiles and turned with them. The main view
/// draws them with the sheet program, the merged program with the box fragment shader's colour taken from the baked tile
/// texture (one texture array, <see cref="ConstructRender.SheetLayersPerKind"/> layers a kind); every other pass draws them as
/// plain boxes. Plain GL 4.1.
/// </summary>
public sealed unsafe partial class Renderer
{
    /// <summary>The shape number of a chunk's floor sheet group (past every part shape's).</summary>
    public const int SheetShape = 0x3FFFFF;

    /// <summary>The texture unit of the baked tiles (0-23 are taken: VideoFeeds.Unit, PostFx, RoomShade, LampShadows list them).</summary>
    private const int SheetUnit = 24;

    /// <summary>While set (the main view's merged flush), <see cref="DrawMerged"/> leaves floor sheets for <see cref="DrawSheets"/>.</summary>
    public bool SheetsApart;

    private GlProgram _sheetProgram;
    private MergeLoc _sLoc;
    private int _sheetKindLoc = -1;
    private bool _sheetReady, _sheetBroken, _sheetOn;
    private uint _sheetTex;
    private int _sheetVersion = -1, _sheetKindsUploaded, _sheetCapacity;

    /// <summary>Can floor sheets be drawn textured here (merged meshes on, and the sheet program made)?</summary>
    public bool SheetsDrawable => MergedMeshes && !_sheetBroken && (_sheetReady || InitSheets());

    private bool InitSheets()
    {
        try
        {
            var vs = MergedVsSrc
                .Replace("out vec4 vColorI;", "out vec4 vColorI;\n                flat out vec4 vSheetI;")
                .Replace("vLayerI = glow ? 0.0 : code;", "vLayerI = 0.0;\n                    float kind = floor(code / 16.0 + 0.001);\n                    vSheetI = vec4(kind, code - 16.0 * kind, 0.0, 0.0);");
            if (!vs.Contains("vSheetI = vec4"))
                throw new InvalidOperationException("the merged vertex shader changed: the sheet program's replacements found nothing");
            var fsSrc = InstancedFragment(false);
            const string colLine = "vec3 col = uColor.rgb;";
            if (!fsSrc.Contains(colLine) || !fsSrc.Contains("uniform sampler2DArray uTex;"))
                throw new InvalidOperationException("the box fragment shader changed: the sheet program's replacements found nothing");
            fsSrc = fsSrc
                .Replace("uniform sampler2DArray uTex;", "uniform sampler2DArray uTex;\n        uniform sampler2DArray uSheets;\n        uniform vec4 uSheetKind[64];\n        flat in vec4 vSheetI;")
                .Replace(colLine, colLine + SheetFs);
            var v = CreateShader(GlShaderType.Vertex).SetSource(RenderCaps.Glsl(vs)).Compile();
            var f = CreateShader(GlShaderType.Fragment).SetSource(RenderCaps.Glsl(fsSrc)).Compile();
            var program = CreateProgram();
            Native.glAttachShader(program, v);
            Native.glAttachShader(program, f);
            Native.glLinkProgram(program);
            GlDebug.Label(GL_PROGRAM, program.Id, "Renderer.sheetProgram");
            int linked;
            Native.glGetProgramiv(program, GL_LINK_STATUS, &linked);
            if (linked == 0)
            {
                var log = stackalloc byte[2048];
                int len;
                Native.glGetProgramInfoLog(program, 2048, &len, log);
                throw new InvalidOperationException("floor sheet shader link failed: " + System.Text.Encoding.UTF8.GetString(log, Math.Max(len, 0)));
            }
            v.Dispose();
            f.Dispose();
            Lighting.Upload(program); // (binds it)
            int Loc(ReadOnlySpan<byte> name)
            {
                fixed (byte* p = name)
                    return Native.glGetUniformLocation(program, p);
            }
            _sLoc = new MergeLoc
            {
                ViewProj = Loc("uViewProj\0"u8), Body = Loc("uBody\0"u8), Origin = Loc("uOrigin\0"u8), Cam = Loc("uCam\0"u8),
                Fog = Loc("uFog\0"u8), FogRange = Loc("uFogRange\0"u8), Glow = Loc("uGlowAlpha\0"u8),
            };
            Native.glUniform1i(Loc("uTex\0"u8), 0);
            Native.glUniform1i(Loc("uSheets\0"u8), SheetUnit);
            _sheetKindLoc = Loc("uSheetKind\0"u8);
            _sheetProgram = program;
            _program.Use();
            Console.WriteLine("[render] floor sheets ready");
            _sheetReady = true;
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[render] floor sheets drawn as plain boxes: {ex.Message}");
            _sheetBroken = true;
            _program.Use();
            _vao.Bind();
            return false;
        }
    }

    // The sheet's colour: which tile of the rectangle this is, which of its sides are welded (inside the rectangle all, on its
    // edge as the tiles there are: vSheetI.y), and so which baked layer (ConstructRender.Bake's order) to read, repeating
    // every tile. uSheetKind[k]: the tile's step along x and z, the sheet's height, the kind's first layer.
    private const string SheetFs = """

            {
                vec4 K = uSheetKind[int(vSheetI.x + 0.5)];
                int R = int(vSheetI.y + 0.5);
                vec3 sp = vLocal + vSize * 0.5;
                float nx = max(1.0, floor(vSize.x / K.x + 0.5));
                float nz = max(1.0, floor(vSize.z / K.y + 0.5));
                float ci = clamp(floor(sp.x / K.x), 0.0, nx - 1.0);
                float cj = clamp(floor(sp.z / K.y), 0.0, nz - 1.0);
                float jpx = (ci < nx - 1.5 || (R & 1) != 0) ? 1.0 : 0.0;
                float jmx = (ci > 0.5 || (R & 2) != 0) ? 1.0 : 0.0;
                float jpz = (cj < nz - 1.5 || (R & 4) != 0) ? 1.0 : 0.0;
                float jmz = (cj > 0.5 || (R & 8) != 0) ? 1.0 : 0.0;
                vec2 suv;
                float layer;
                if (abs(vLocalN.y) > 0.5)
                {
                    suv = vec2(sp.x / K.x, sp.z / K.y);
                    layer = K.w + jpx + 2.0 * jmx + 4.0 * jpz + 8.0 * jmz;
                }
                else if (abs(vLocalN.x) > 0.5)
                {
                    suv = vec2(sp.z / K.y, sp.y / K.z);
                    layer = K.w + (vLocalN.x > 0.0 ? 16.0 : 20.0) + jpz + 2.0 * jmz;
                }
                else
                {
                    suv = vec2(sp.x / K.x, sp.y / K.z);
                    layer = K.w + (vLocalN.z > 0.0 ? 24.0 : 28.0) + jpx + 2.0 * jmx;
                }
                col *= texture(uSheets, vec3(suv, layer)).rgb;
            }
    """;

    /// <summary>The baked tiles, baked and uploaded again when a kind was added since; the kinds' uniforms with them.</summary>
    private void SheetTexture()
    {
        var kinds = ConstructRender.SheetKinds;
        if (_sheetVersion == ConstructRender.SheetVersion && _sheetTex != 0)
            return;
        const int n = ConstructRender.SheetTexels, per = ConstructRender.SheetLayersPerKind;
        var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        var from = _sheetKindsUploaded;
        Native.glActiveTexture(GL_TEXTURE0 + SheetUnit);
        if (_sheetTex == 0 || kinds.Count > _sheetCapacity)
        {
            // (made again with room for as many kinds again, all baked anew: kinds come rarely, a new paint on a floor, so the
            // doubling keeps the re-bakes to a handful in a game)
            if (_sheetTex != 0)
            {
                var old = _sheetTex;
                Native.glDeleteTextures(1, (GlTexture*)&old);
            }
            GlTexture tex = default;
            Native.glGenTextures(1, &tex);
            _sheetTex = tex.Id;
            Native.glBindTexture(GL_TEXTURE_2D_ARRAY, _sheetTex);
            _sheetCapacity = Math.Min(ConstructRender.SheetMaxKinds, Math.Max(4, _sheetCapacity * 2));
            while (_sheetCapacity < kinds.Count)
                _sheetCapacity *= 2;
            Native.glTexImage3D(GL_TEXTURE_2D_ARRAY, 0, (int)GL_RGBA8, n, n, _sheetCapacity * per, 0, GL_RGBA, GL_UNSIGNED_BYTE, null);
            Native.glTexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_MIN_FILTER, (int)GL_LINEAR_MIPMAP_LINEAR);
            Native.glTexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_MAG_FILTER, (int)GL_LINEAR);
            Native.glTexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_WRAP_S, (int)GL_REPEAT);
            Native.glTexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_WRAP_T, (int)GL_REPEAT);
            RenderCaps.Anisotropy(GL_TEXTURE_2D_ARRAY, 8f);
            from = 0;
        }
        else
            Native.glBindTexture(GL_TEXTURE_2D_ARRAY, _sheetTex);
        Native.glPixelStorei(GL_UNPACK_ALIGNMENT, 1);
        for (var k = from; k < kinds.Count; k++)
        {
            var px = ConstructRender.Bake(kinds[k]);
            fixed (byte* p = px)
                Native.glTexSubImage3D(GL_TEXTURE_2D_ARRAY, 0, 0, 0, k * per, n, n, per, GL_RGBA, GL_UNSIGNED_BYTE, p);
        }
        Native.glGenerateMipmap(GL_TEXTURE_2D_ARRAY);
        Native.glActiveTexture(GL_TEXTURE0);
        _sheetKindsUploaded = kinds.Count;
        Console.WriteLine($"[render] floor sheets: {kinds.Count - from} tile kind(s) baked in {System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds:0} ms, {kinds.Count} of {_sheetCapacity} ({(long)_sheetCapacity * per * n * n * 4 / 1024 / 1024} MB before mipmaps)");
        _sheetProgram.Use();
        var u = stackalloc float[ConstructRender.SheetMaxKinds * 4];
        for (var k = 0; k < kinds.Count; k++)
        {
            var sk = kinds[k];
            u[k * 4] = sk.Cell;
            u[k * 4 + 1] = sk.Cell;
            u[k * 4 + 2] = MathF.Max(0.01f, sk.Top - sk.Bottom);
            u[k * 4 + 3] = k * per;
        }
        Native.glUniform4fv(_sheetKindLoc, Math.Max(1, kinds.Count), u);
        _sheetVersion = ConstructRender.SheetVersion;
    }

    /// <summary>Starts a run of <see cref="DrawSheets"/> calls: the sheet program, the baked tiles bound, this frame's camera.</summary>
    public void BeginSheets()
    {
        if (!SheetsDrawable)
            return;
        if (_shapeVersion != PartShapes.Version)
            UploadShapes();
        SheetTexture();
        _sheetOn = true;
        MergedUniforms(_sheetProgram, in _sLoc);
        Native.glActiveTexture(GL_TEXTURE0 + SheetUnit);
        Native.glBindTexture(GL_TEXTURE_2D_ARRAY, _sheetTex);
        Native.glActiveTexture(GL_TEXTURE0);
    }

    /// <summary>Draws a group's floor sheets (if it has any) at <paramref name="frame"/>, as <see cref="DrawMerged"/> draws its boxes.</summary>
    public void DrawSheets(MergedMesh m, in Matrix4x4 frame, bool occluded = false)
    {
        if (!_sheetOn || m.Slots == 0 || m.Vao.Id == 0 || m.Groups == 0 || m.GroupShape[m.Groups - 1] != SheetShape)
            return;
        var g = m.Groups - 1; // (the sheets are always the last group: their shape is past every other)
        var f = FromOrigin(frame);
        Native.glUniformMatrix4fv(_sLoc.Body, 1, false, &f.M11);
        m.Vao.Bind();
        var conditional = occluded && m.Query.Id != 0 && m.QueryFrame == OcclusionFrame - 1 && Native.glBeginConditionalRender != null;
        if (conditional)
            Native.glBeginConditionalRender(m.Query.Id, GL_QUERY_NO_WAIT);
        if (m.PointedAt != m.GroupFirst[g])
            PointMerged(m, m.GroupFirst[g]);
        PerfCounters.Draw(36, m.GroupCount[g]);
        Native.glDrawArraysInstanced(GL_TRIANGLES, 0, 36, m.GroupCount[g]);
        MergedDraws++;
        MergedSlots += m.GroupCount[g];
        if (conditional)
            Native.glEndConditionalRender();
    }

    /// <summary>Ends a run of sheet draws: the box shader and its cube as they were.</summary>
    public void EndSheets()
    {
        if (!_sheetOn)
            return;
        _sheetOn = false;
        Native.glBindBuffer(GL_ARRAY_BUFFER, _vbo);
        _program.Use();
        _vao.Bind();
    }
}
