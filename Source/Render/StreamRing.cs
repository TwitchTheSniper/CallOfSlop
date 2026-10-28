using Brutal.OpenGlApi;
using static Brutal.OpenGlApi.OpenGl;

namespace CallOfSlop;

/// <summary>
/// Per-frame instance data in one persistently mapped buffer (OpenGL 4.4 glBufferStorage, the 4.6 path's
/// <see cref="RenderCaps.PersistentBuffers"/> experiment, docs/render/OPENGL46.md). The buffer is split into three
/// frame segments; a frame writes into its own segment straight through the mapped pointer and draws with a base
/// instance (glDraw*BaseInstance) pointing at what it wrote, so nothing is orphaned or copied by the driver. Before a
/// segment is reused (three frames later) its fence is waited on, which in practice never blocks. Made with DSA
/// (glCreateBuffers / glNamedBufferStorage) when the context has it.
///
/// The VAO that reads the ring points at offset 0; when the ring has to grow it is made again and
/// <see cref="Generation"/> changes, so the owner points its VAO at the new <see cref="Buffer"/>.
/// The 4.1 path never makes one.
/// </summary>
public sealed unsafe class StreamRing : IDisposable
{
    /// <summary>Frames in flight (segments).</summary>
    public const int Segments = 3;

    /// <summary>The render frame (bumped by <see cref="Renderer.BeginFrame"/>): a ring moves to its next segment on its first write of a frame.</summary>
    public static long Frame;

    /// <summary>Times a fence wait actually had to wait (should stay 0: the GPU is under three frames behind).</summary>
    public static long Waits;

    private readonly int _stride;
    private readonly string _label;
    private int _segCap;
    private GlBuffer _buffer;
    private byte* _ptr;
    private readonly GlSync[] _fences = new GlSync[Segments];
    private readonly bool[] _fenced = new bool[Segments];
    private int _seg = -1, _cursor;
    private long _frame = -1;

    /// <summary>Bumped each time the buffer is (re)made; the owner re-points its VAO when it changes.</summary>
    public int Generation { get; private set; }

    /// <summary>The GL buffer (for the owner's VAO).</summary>
    public GlBuffer Buffer => _buffer;

    /// <summary>Bytes on the GPU.</summary>
    public long Bytes => (long)_segCap * Segments * _stride;

    /// <param name="stride">Bytes per instance.</param>
    /// <param name="instances">Instances per frame to start with (it grows when a frame needs more).</param>
    /// <param name="label">The buffer's debug name.</param>
    public StreamRing(int stride, int instances, string label)
    {
        _stride = stride;
        _label = label;
        Make(Math.Max(instances, 64));
    }

    /// <summary>
    /// Room for <paramref name="count"/> instances in this frame's segment: returns where to write them and, in
    /// <paramref name="first"/>, the base instance to draw them with. The pointer is good until the next call.
    /// </summary>
    public void* Reserve(int count, out int first)
    {
        if (_frame != Frame)
        {
            if (_seg >= 0 && _cursor > 0)
                Fence(_seg);
            _seg = (_seg + 1) % Segments;
            Wait(_seg);
            _cursor = 0;
            _frame = Frame;
        }
        if (_cursor + count > _segCap)
        {
            // Rare: a frame wants more than the ring holds. Make a bigger one (old draws still read the old buffer,
            // which GL keeps until they finish), and start this frame afresh in segment 0.
            var cap = _segCap;
            while (cap < _cursor + count)
                cap *= 2;
            Make(cap);
            _seg = 0;
            _cursor = 0;
            _frame = Frame;
        }
        first = _seg * _segCap + _cursor;
        _cursor += count;
        return _ptr + (long)first * _stride;
    }

    /// <summary>Copies <paramref name="count"/> instances of <paramref name="floatsPerInstance"/> floats from <paramref name="src"/>; returns the base instance.</summary>
    public int Write(float* src, int count, int floatsPerInstance)
    {
        var dst = Reserve(count, out var first);
        var bytes = (long)count * floatsPerInstance * sizeof(float);
        System.Buffer.MemoryCopy(src, dst, bytes, bytes);
        return first;
    }

    private void Make(int segCap)
    {
        Free();
        _segCap = segCap;
        var size = (nint)((long)segCap * Segments * _stride);
        var flags = GL_MAP_WRITE_BIT | GL_MAP_PERSISTENT_BIT | GL_MAP_COHERENT_BIT;
        GlBuffer b;
        if (RenderCaps.Dsa)
        {
            Native.glCreateBuffers(1, &b);
            Native.glNamedBufferStorage(b, size, null, flags);
            _ptr = (byte*)Native.glMapNamedBufferRange(b, 0, size, flags);
        }
        else
        {
            b = GenBuffer();
            Native.glBindBuffer(GL_ARRAY_BUFFER, b);
            Native.glBufferStorage(GL_ARRAY_BUFFER, size, null, flags);
            _ptr = (byte*)Native.glMapBufferRange(GL_ARRAY_BUFFER, 0, size, flags);
        }
        _buffer = b;
        if (_ptr == null)
            throw new InvalidOperationException($"could not map the {_label} ring");
        GlDebug.Label(GL_BUFFER, b.Id, _label);
        Generation++;
    }

    private void Fence(int seg)
    {
        if (_fenced[seg])
            Native.glDeleteSync(_fences[seg]);
        _fences[seg] = Native.glFenceSync(GL_SYNC_GPU_COMMANDS_COMPLETE, 0);
        _fenced[seg] = true;
    }

    private void Wait(int seg)
    {
        if (!_fenced[seg])
            return;
        var r = (uint)Native.glClientWaitSync(_fences[seg], 0, 0);
        if (r != GL_ALREADY_SIGNALED && r != GL_CONDITION_SATISFIED)
        {
            Waits++;
            do
                r = (uint)Native.glClientWaitSync(_fences[seg], GL_SYNC_FLUSH_COMMANDS_BIT, 1_000_000); // 1 ms at a time
            while (r == GL_TIMEOUT_EXPIRED);
        }
        Native.glDeleteSync(_fences[seg]);
        _fenced[seg] = false;
    }

    private void Free()
    {
        for (var i = 0; i < Segments; i++)
            if (_fenced[i])
            {
                Native.glDeleteSync(_fences[i]);
                _fenced[i] = false;
            }
        if (_buffer.Id != 0)
        {
            var b = _buffer;
            if (RenderCaps.Dsa)
                Native.glUnmapNamedBuffer(b);
            else
            {
                Native.glBindBuffer(GL_ARRAY_BUFFER, b);
                Native.glUnmapBuffer(GL_ARRAY_BUFFER);
            }
            Native.glDeleteBuffers(1, &b);
            _buffer = default;
        }
        _ptr = null;
    }

    public void Dispose() => Free();
}
