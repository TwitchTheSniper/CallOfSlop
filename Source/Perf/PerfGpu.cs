namespace CallOfSlop;

/// <summary>
/// The frame's GPU passes timed for the performance window, in the order they are drawn (a pass's time runs from the
/// previous mark to its own, so the order must match the frame). Passes not drawn this frame give no sample.
/// </summary>
public enum PerfGpuPass : byte
{
    Buildings,
    Terrain,
    Props,
    Meshes,
    Foliage,
    Decals,
    Probe,
    Sky,
    Bodies,
    Entities,
    Effects,
    ViewModel,
    Hud,
    Ui,
    COUNT,
}

/// <summary>
/// GPU time per pass for the performance window: one <see cref="GpuStampRing"/> of timestamp queries (glQueryCounter,
/// core since GL 3.3, so fine on the GL 4.1 / Mac target) over the whole frame, a ring of <see cref="Ring"/> frames in
/// flight, each read back only once its results are available (never a stall). Each pass is stamped where it starts
/// (<see cref="Begin"/>, from <see cref="Perf.Pass"/>) and where it ends (<see cref="Mark"/>), so time the GPU spends
/// waiting for the CPU between passes is shown apart (<see cref="GapAvgMs"/>) instead of billed to the next pass;
/// <see cref="PerfGpuPass.Probe"/> is an empty pass just before the sky that checks it (it should read about 0). Timestamps rather than GL_TIME_ELAPSED
/// because the terrain, foliage and mesh renderers keep GL_TIME_ELAPSED queries of their own, and two elapsed queries
/// can't be open at once. Only runs while <see cref="Perf.On"/>; closed, it makes no GL calls at all. The one place to
/// build further GPU timing on (GL debug output, finer passes).
/// </summary>
public sealed class PerfGpu
{
    public const int Ring = 4;
    public const int Count = (int)PerfGpuPass.COUNT;

    public static readonly string[] Names =
        ["Buildings", "Terrain", "Props", "Meshes", "Foliage", "Decals", "Empty (timer check)", "Sky", "Players", "Entities, zombies", "Effects", "View model", "HUD (2D)", "UI (ImGui)"];

    private readonly Func<int, IGpuStamps?> _make;
    private GpuStampRing? _ring;
    private bool _tried, _inFrame;
    private int _lastMarked = -1, _framesSeen;
    private double _gapsSeen;

    /// <summary>Smoothed GPU time per frame between the timed passes (the GPU waiting for the CPU, or untimed work).</summary>
    public float GapAvgMs;

    /// <summary>Per pass: the latest reading (ms) and its smoothed average.</summary>
    public readonly float[] LastMs = new float[Count], AvgMs = new float[Count];

    /// <summary>Per pass: frames read back with the pass in them.</summary>
    public readonly int[] Samples = new int[Count];

    /// <summary>Marks out of pass order this frame and in total (they would mix up the passes' times; skipped).</summary>
    public int OutOfOrder;

    /// <summary>Readings so far (any pass), for the self-test and the window.</summary>
    public int Readings;

    private readonly Action<int, double> _onSample;

    /// <param name="make">Makes the query backend for a number of queries (null: no timer queries on this driver).</param>
    public PerfGpu(Func<int, IGpuStamps?> make)
    {
        _make = make;
        _onSample = OnSample;
    }

    /// <summary>Is there a timer (after the first frame tried to make one)?</summary>
    public bool Available => _ring != null;

    /// <summary>Did the first frame try (so <see cref="Available"/> is meaningful)?</summary>
    public bool Tried => _tried;

    /// <summary>Starts a frame's timing (call before anything is drawn): reads back finished frames first.</summary>
    public void BeginFrame()
    {
        _inFrame = false;
        if (!Perf.On)
            return;
        if (!_tried)
        {
            _tried = true;
            if (_make(GpuStampRing.QueryCount(Count, Ring)) is { } stamps)
                _ring = new GpuStampRing(Count, Ring, stamps, _onSample);
        }
        if (_ring == null)
            return;
        _ring.BeginFrame();
        _inFrame = _ring.Recording;
        _lastMarked = -1;
        CountGaps();
    }

    /// <summary>Marks the start of a pass (optional; in frame order, before its <see cref="Mark"/>).</summary>
    public void Begin(PerfGpuPass pass)
    {
        if (!_inFrame)
            return;
        var p = (int)pass;
        if (p > _lastMarked)
            _ring!.BeginPass(p); // (out of order: its Mark is skipped and counted)
    }

    /// <summary>Marks the end of a pass (in frame order; a pass out of order is skipped and counted).</summary>
    public void Mark(PerfGpuPass pass)
    {
        if (!_inFrame)
            return;
        var p = (int)pass;
        if (p <= _lastMarked)
        {
            OutOfOrder++;
            return;
        }
        _lastMarked = p;
        _ring!.EndPass(p);
    }

    /// <summary>Ends the frame's timing (before the swap).</summary>
    public void EndFrame()
    {
        if (_ring == null)
            return;
        if (_inFrame)
            _ring.EndFrame();
        _inFrame = false;
    }

    /// <summary>Reads back whatever is ready without starting a frame (the self-test's drain; never waits).</summary>
    public void Collect()
    {
        _ring?.Collect();
        CountGaps();
    }

    private void CountGaps()
    {
        if (_ring == null || _ring.FramesRead == _framesSeen)
            return;
        var f = (float)((_ring.Gaps - _gapsSeen) / (_ring.FramesRead - _framesSeen));
        GapAvgMs = _framesSeen == 0 ? f : GapAvgMs + (f - GapAvgMs) * 0.1f;
        (_framesSeen, _gapsSeen) = (_ring.FramesRead, _ring.Gaps);
    }

    private void OnSample(int pass, double ms)
    {
        var f = (float)ms;
        LastMs[pass] = f;
        AvgMs[pass] = Samples[pass] == 0 ? f : AvgMs[pass] + (f - AvgMs[pass]) * 0.1f;
        Samples[pass]++;
        Readings++;
    }

    /// <summary>The smoothed GPU time of the passes timed lately (ms).</summary>
    public float TotalAvgMs()
    {
        var t = 0f;
        for (var i = 0; i < Count; i++)
            t += AvgMs[i];
        return t;
    }
}
