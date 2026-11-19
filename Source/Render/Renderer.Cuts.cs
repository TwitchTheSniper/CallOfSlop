using System.Numerics;
using Brutal.OpenGlApi;
using static Brutal.OpenGlApi.OpenGl;

namespace CallOfSlop;

/// <summary>
/// Openings (the user, 2026-11-19): a flush shell pod sinks into the floor, wall or ceiling it is welded to, and the surface part
/// it sits in must not be drawn across its pocket. Each frame <see cref="ConstructRender"/> lists the near flush pods' pockets
/// here (<see cref="AddCut"/>, at most <see cref="CutMax"/>), as boxes from the render origin; boxes drawn while
/// <see cref="Cutting"/> is set (the body that holds the pods) carry the cut mark (mode + 4) and the box shader drops their
/// fragments inside any of them (<c>InCut</c> in <see cref="FragmentSrc"/>). A pocket box stays clear of the pod's own boxes
/// (inside its rim, above its lining: ShellParts.Pod), so the pod needs no exemption; its lid, frame and shell are drawn later
/// unmarked (ShellPodRender.cs). The three box programs (alone, instanced, merged) take the list when they next draw after
/// it changed. Allocates nothing.
/// </summary>
public sealed unsafe partial class Renderer
{
    /// <summary>The most openings a frame (the shader's <c>uCut</c> holds three rows each).</summary>
    public const int CutMax = 16;

    private readonly float[] _cuts = new float[CutMax * 12];
    private int _cutCount, _cutVersion = 1, _cutLone, _cutInst, _cutMerged;
    private int _locCut = -2, _locCutN = -2, _iLocCut = -2, _iLocCutN = -2, _mLocCut = -2, _mLocCutN = -2, _mLocCutOn = -2;

    /// <summary>While set, boxes drawn carry the cut mark: the openings listed this frame are not drawn on them.</summary>
    public bool Cutting;

    /// <summary>The openings so far this frame.</summary>
    public int CutCount => _cutCount;

    /// <summary>Starts a frame's (or a camera pass's) list of openings.</summary>
    public void ClearCuts()
    {
        if (_cutCount == 0)
            return;
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

    /// <summary>The mode a box is drawn with: with the cut mark while <see cref="Cutting"/>.</summary>
    private int CutMode(int mode) => Cutting && _cutCount > 0 && mode < 4 ? mode + 4 : mode;

    /// <summary>The list into a program (bound) whose copy is older.</summary>
    private void PushCuts(GlProgram program, ref int locCut, ref int locCutN, ref int version)
    {
        if (version == _cutVersion)
            return;
        version = _cutVersion;
        if (locCut == -2)
        {
            fixed (byte* p = "uCut\0"u8)
                locCut = Native.glGetUniformLocation(program, p);
            fixed (byte* p = "uCutN\0"u8)
                locCutN = Native.glGetUniformLocation(program, p);
        }
        if (locCut >= 0 && _cutCount > 0)
            fixed (float* p = _cuts)
                Native.glUniform4fv(locCut, _cutCount * 3, p);
        if (locCutN >= 0)
            Native.glUniform1f(locCutN, _cutCount);
    }

    private void PushCutsLone() => PushCuts(_program, ref _locCut, ref _locCutN, ref _cutLone);
    private void PushCutsInst() => PushCuts(_instProgram, ref _iLocCut, ref _iLocCutN, ref _cutInst);
    private void PushCutsMerged() => PushCuts(_mergeProgram, ref _mLocCut, ref _mLocCutN, ref _cutMerged);

    /// <summary>The merged program's per-group mark (bound).</summary>
    private void MergedCutOn(bool on)
    {
        if (_mLocCutOn == -2)
            fixed (byte* p = "uCutOn\0"u8)
                _mLocCutOn = Native.glGetUniformLocation(_mergeProgram, p);
        if (_mLocCutOn >= 0)
            Native.glUniform1f(_mLocCutOn, on && _cutCount > 0 ? 1f : 0f);
    }
}
