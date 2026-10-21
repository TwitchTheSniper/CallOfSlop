using Brutal.OpenGlApi;
using static Brutal.OpenGlApi.OpenGl;

namespace CallOfSlop;

/// <summary>Where a <see cref="GpuStampRing"/> writes and reads its timestamps (GL in the game, a fake in the self-test).</summary>
public interface IGpuStamps
{
    /// <summary>Records a GPU timestamp into query <paramref name="index"/> once the GPU reaches this point.</summary>
    void Stamp(int index);

    /// <summary>Is query <paramref name="index"/>'s result ready (reading it won't stall)?</summary>
    bool Ready(int index);

    /// <summary>Query <paramref name="index"/>'s timestamp in nanoseconds (only after <see cref="Ready"/>).</summary>
    ulong Read(int index);
}

/// <summary>
/// GPU time of a few consecutive passes per frame from timestamp queries (glQueryCounter), a small ring of frames in
/// flight, read back a few frames later so it never stalls. A frame stamps 0 before the first pass, then k after pass
/// k; pass k's time is stamp k minus stamp k-1, and a pass whose end stamp wasn't written that frame (skipped) gives no
/// sample. Timestamps rather than GL_TIME_ELAPSED: they don't nest-conflict with the other renderers' elapsed queries,
/// and some drivers report 0 for elapsed queries around short instanced passes. Allocation-free per frame.
/// </summary>
public sealed class GpuStampRing
{
    private readonly IGpuStamps _gpu;
    private readonly int _stamps, _ring;
    private readonly bool[] _pending, _marked;
    private readonly Action<int, double> _sample;
    private int _frame;
    private bool _recording;

    /// <param name="passes">Passes timed per frame (stamps per frame = passes + 1).</param>
    /// <param name="ring">Frames in flight.</param>
    /// <param name="gpu">The query backend; it must hold (passes + 1) * ring queries.</param>
    /// <param name="sample">Called with (pass, milliseconds) for each pass read back.</param>
    public GpuStampRing(int passes, int ring, IGpuStamps gpu, Action<int, double> sample)
    {
        _stamps = passes + 1;
        _ring = ring;
        _gpu = gpu;
        _sample = sample;
        _pending = new bool[ring];
        _marked = new bool[ring * _stamps];
    }

    /// <summary>Queries the backend needs.</summary>
    public static int QueryCount(int passes, int ring) => (passes + 1) * ring;

    /// <summary>Is this frame being timed (its slot was free)?</summary>
    public bool Recording => _recording;

    /// <summary>Reads back finished frames, then starts this one (stamp 0) if its slot is free.</summary>
    public void BeginFrame()
    {
        Collect();
        var slot = _frame % _ring;
        _recording = !_pending[slot];
        if (!_recording)
            return;
        Array.Clear(_marked, slot * _stamps, _stamps);
        Stamp(0);
    }

    /// <summary>Marks the end of pass <paramref name="pass"/> (0-based): writes stamp pass + 1.</summary>
    public void EndPass(int pass) => Stamp(pass + 1);

    /// <summary>Ends the frame: its stamps are in flight until <see cref="Collect"/> finds them ready.</summary>
    public void EndFrame()
    {
        if (_recording)
            _pending[_frame % _ring] = true;
        _recording = false;
        _frame++;
    }

    private void Stamp(int which)
    {
        if (!_recording || which < 0 || which >= _stamps)
            return;
        var i = (_frame % _ring) * _stamps + which;
        _gpu.Stamp(i);
        _marked[i] = true;
    }

    /// <summary>Reads every pending frame whose stamps are all ready, and reports its passes.</summary>
    public void Collect()
    {
        for (var s = 0; s < _ring; s++)
        {
            if (!_pending[s])
                continue;
            var b = s * _stamps;
            var ready = true;
            for (var k = 0; k < _stamps && ready; k++)
                if (_marked[b + k] && !_gpu.Ready(b + k))
                    ready = false;
            if (!ready)
                continue;
            _pending[s] = false;
            // Each pass from the latest written stamp before it (stamps are written in order).
            var prev = _marked[b] ? _gpu.Read(b) : 0UL;
            var hasPrev = _marked[b];
            for (var k = 1; k < _stamps; k++)
            {
                if (!_marked[b + k])
                    continue;
                var t = _gpu.Read(b + k);
                if (hasPrev)
                    _sample(k - 1, t > prev ? (t - prev) / 1e6 : 0);
                prev = t;
                hasPrev = true;
            }
        }
    }
}

/// <summary>An <see cref="IGpuStamps"/> on GL timestamp queries (needs GL 3.3 / ARB_timer_query).</summary>
public sealed unsafe class GlGpuStamps : IGpuStamps
{
    private readonly GlQuery[] _queries;

    private GlGpuStamps(GlQuery[] queries) => _queries = queries;

    /// <summary>Makes <paramref name="count"/> timestamp queries, or returns null when the driver has no timestamps.</summary>
    public static GlGpuStamps? Create(int count)
    {
        if (Native.glGenQueries == null || Native.glQueryCounter == null || Native.glGetQueryObjectui64v == null || Native.glGetQueryObjectiv == null)
            return null;
        if (Native.glGetQueryiv != null)
        {
            int bits = -1;
            Native.glGetQueryiv(GL_TIMESTAMP, GL_QUERY_COUNTER_BITS, &bits);
            if (bits == 0)
                return null; // timestamps supported in name only
        }
        var q = new GlQuery[count];
        fixed (GlQuery* p = q)
            Native.glGenQueries(count, p);
        return new GlGpuStamps(q);
    }

    public void Stamp(int index) => Native.glQueryCounter(_queries[index].Id, GL_TIMESTAMP);

    public bool Ready(int index)
    {
        int available;
        Native.glGetQueryObjectiv(_queries[index].Id, GL_QUERY_RESULT_AVAILABLE, &available);
        return available != 0;
    }

    public ulong Read(int index)
    {
        ulong ns;
        Native.glGetQueryObjectui64v(_queries[index].Id, GL_QUERY_RESULT, &ns);
        return ns;
    }
}
