using System.Numerics;
using Brutal.OpenGlApi;
using static Brutal.OpenGlApi.OpenGl;

namespace CallOfSlop;

/// <summary>
/// Openings (the user, 2026-11-19): a flush shell pod sinks into the floor, wall or ceiling it is welded to, and the surface part
/// it sits in must not be drawn across its pocket. Each frame <see cref="ConstructRender"/> lists the flush pods' pockets nearest
/// the eye here (<see cref="AddCut"/>, at most <see cref="CutMax"/>, grouped by body), as boxes from the render origin. Only the
/// surfaces a pod sinks into (Construct.PocketHost: parts whose bounds meet a pocket, never a pod) are drawn with the cut mark
/// (mode + 4 x code: <see cref="CutCode"/>, their body's own run of openings), by the <c>CUTS</c> variant of the instanced box
/// program, which drops their fragments inside one of those openings; a big body walks them rather than merging them. The pod
/// itself and everything else keeps the plain programs, which have no discard (it would cost early depth testing). Its lid,
/// frame and shell are drawn later, unmarked (ShellPodRender.cs). Allocates nothing.
/// </summary>
public sealed unsafe partial class Renderer
{
    /// <summary>The most openings a frame (the shader's <c>uCut</c> holds three rows each; a code's count fits in five bits).</summary>
    public const int CutMax = 16;

    private readonly float[] _cuts = new float[CutMax * 12];
    private int _cutCount, _cutVersion = 1, _cutInst;

    // Cut-marked boxes, apart from the plain batch: drawn by the instanced program's cut variant.
    private readonly float[] _cutBatch = new float[BatchMax * BatchFloats];
    private int _cutBatchCount;
    private readonly float[] _cutOne = new float[BatchFloats];
    private GlVertexArray _cutOneVao;
    private GlBuffer _cutOneVbo;

    /// <summary>
    /// While above zero, boxes drawn carry the cut mark for a run of this frame's openings (<see cref="CutRun"/>: a body's own),
    /// and aren't drawn inside them. Zero: plain boxes.
    /// </summary>
    public int CutCode;

    /// <summary>The openings so far this frame.</summary>
    public int CutCount => _cutCount;

    /// <summary>Can boxes be cut here (the cut program was made)?</summary>
    public bool CanCut => Recorder == null && !_instBroken && (_instReady || InitInstanced());

    /// <summary>The cut code for openings first..first+count-1 (0: none).</summary>
    public static int CutRun(int first, int count) => count <= 0 ? 0 : first * 32 + count;

    /// <summary>Starts a frame's (or a camera pass's) list of openings.</summary>
    public void ClearCuts()
    {
        CutCode = 0;
        if (_cutCount == 0)
            return;
        if (_cutBatchCount > 0)
            FlushBatch(); // (the boxes marked for the old list go out with it)
        _cutCount = 0;
        _cutVersion++;
    }

    /// <summary>
    /// Adds an opening: the box of half size <paramref name="half"/> at <paramref name="centre"/> in the frame
    /// <paramref name="frame"/> (from the render origin, as a body's draw has it). False when the list is full or the frame is singular.
    /// </summary>
    public bool AddCut(in Matrix4x4 frame, Vector3 centre, Vector3 half)
    {
        if (_cutCount >= CutMax || half.X <= 0 || half.Y <= 0 || half.Z <= 0)
            return false;
        var box = Matrix4x4.CreateScale(half) * Matrix4x4.CreateTranslation(centre) * frame;
        if (!Matrix4x4.Invert(box, out var inv))
            return false;
        // Row vectors: q = p * inv, so q.x = dot((M11, M21, M31, M41), (p, 1)).
        var o = _cutCount * 12;
        var c = _cuts;
        c[o] = inv.M11; c[o + 1] = inv.M21; c[o + 2] = inv.M31; c[o + 3] = inv.M41;
        c[o + 4] = inv.M12; c[o + 5] = inv.M22; c[o + 6] = inv.M32; c[o + 7] = inv.M42;
        c[o + 8] = inv.M13; c[o + 9] = inv.M23; c[o + 10] = inv.M33; c[o + 11] = inv.M43;
        _cutCount++;
        _cutVersion++;
        return true;
    }

    /// <summary>The mode a box is drawn with: with the cut mark while <see cref="CutCode"/> is set.</summary>
    private int CutMode(int mode) => CutCode > 0 && _cutCount > 0 && mode < 4 && _instReady && !_bloodOn ? mode + 4 * CutCode : mode; // (a box with blood keeps it, uncut)

    /// <summary>The list into the bound cut program, if its copy is older.</summary>
    private void PushCuts(int loc, ref int version)
    {
        if (version == _cutVersion || _cutCount == 0)
            return;
        version = _cutVersion;
        fixed (float* p = _cuts)
            Native.glUniform4fv(loc, _cutCount * 3, p);
    }

    /// <summary>A cut-marked box into the cut batch (opaque, a plain box). Flushes both batches when it fills.</summary>
    private void BatchCut(in Matrix4x4 m, Vector3 color, float alpha, int mode, float layer)
    {
        WriteInstance(_cutBatch, _cutBatchCount * BatchFloats, m, color, alpha, mode, layer);
        if (++_cutBatchCount == BatchMax)
            FlushBatch();
    }

    private static void WriteInstance(float[] b, int o, in Matrix4x4 m, Vector3 color, float alpha, int mode, float layer)
    {
        b[o] = m.M11; b[o + 1] = m.M12; b[o + 2] = m.M13; b[o + 3] = m.M14;
        b[o + 4] = m.M21; b[o + 5] = m.M22; b[o + 6] = m.M23; b[o + 7] = m.M24;
        b[o + 8] = m.M31; b[o + 9] = m.M32; b[o + 10] = m.M33; b[o + 11] = m.M34;
        b[o + 12] = m.M41; b[o + 13] = m.M42; b[o + 14] = m.M43; b[o + 15] = m.M44;
        b[o + 16] = color.X; b[o + 17] = color.Y; b[o + 18] = color.Z; b[o + 19] = alpha;
        b[o + 20] = mode; b[o + 21] = layer;
    }

    /// <summary>
    /// One cut-marked box (a contoured shape, a blended box, or one drawn outside a batch) at once, by the instanced program's cut
    /// variant from a buffer of its own: the plain box program never takes the openings. <paramref name="m"/> is from the origin.
    /// </summary>
    private void DrawCutOne(in Matrix4x4 m, Vector3 color, float alpha, int mode, float layer, int shape)
    {
        int first = 0, count = 36;
        if (shape != 0)
        {
            if (_shapeVersion != PartShapes.Version)
                UploadShapes();
            if (shape + 1 >= _shapeFirst.Count)
                return;
            (first, count) = (_shapeFirst[shape], _shapeFirst[shape + 1] - _shapeFirst[shape]);
        }
        if (_cutOneVao.Id == 0)
        {
            _cutOneVao = GenVertexArray();
            _cutOneVao.Bind();
            _cutOneVbo = GenBuffer();
            Native.glBindBuffer(GL_ARRAY_BUFFER, _cutOneVbo);
            Native.glBufferData(GL_ARRAY_BUFFER, BatchFloats * sizeof(float), null, GL_STREAM_DRAW);
            GlDebug.Label(GL_VERTEX_ARRAY, _cutOneVao.Id, "Renderer.cutOne");
            BoxInstanceAttributes(_cutOneVbo);
        }
        WriteInstance(_cutOne, 0, m, color, alpha, mode, layer);
        InstanceUniforms(_instCutProgram, in _icLoc);
        _cutOneVao.Bind();
        Native.glBindBuffer(GL_ARRAY_BUFFER, _cutOneVbo);
        Native.glBufferData(GL_ARRAY_BUFFER, BatchFloats * sizeof(float), null, GL_STREAM_DRAW); // orphan
        fixed (float* p = _cutOne)
            Native.glBufferSubData(GL_ARRAY_BUFFER, 0, BatchFloats * sizeof(float), p);
        PerfCounters.Draw(count);
        Native.glDrawArraysInstanced(GL_TRIANGLES, first, count, 1);
        Native.glBindBuffer(GL_ARRAY_BUFFER, _vbo);
        _program.Use();
        _vao.Bind();
    }
}
