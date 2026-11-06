using System.IO.MemoryMappedFiles;
using System.Runtime.CompilerServices;

namespace SlopBrowser;

/// <summary>
/// One page's memory shared with the game, made by the game (Source/Media/MediaBlock.cs holds the same layout): a header, a ring of mono
/// floats for the sound and three frames of BGRA for the picture. Three, so a frame is written beside the one being read and the newest
/// one finished is swapped in with a single int: neither side ever waits for the other.
/// </summary>
internal sealed unsafe class SharedBlock : IDisposable
{
    public const int HeaderBytes = 4096, RingFrames = 1 << 17;
    private const int OffLatest = 16, OffReading = 20, OffSeq = 24, OffAudioWrite = 32;

    private readonly MemoryMappedFile _file;
    private readonly MemoryMappedViewAccessor _view;
    private readonly byte* _base;
    private readonly int _frameBytes;
    public readonly int Width, Height;

    public SharedBlock(string name, int width, int height)
    {
        Width = width;
        Height = height;
        _frameBytes = width * height * 4;
        _file = MemoryMappedFile.OpenExisting(name, MemoryMappedFileRights.ReadWrite);
        _view = _file.CreateViewAccessor(0, 0, MemoryMappedFileAccess.ReadWrite);
        byte* p = null;
        _view.SafeMemoryMappedViewHandle.AcquirePointer(ref p);
        _base = p;
    }

    private ref int Int(int offset) => ref Unsafe.AsRef<int>(_base + offset);
    private ref long Long(int offset) => ref Unsafe.AsRef<long>(_base + offset);
    private byte* Frame(int index) => _base + HeaderBytes + RingFrames * sizeof(float) + (long)index * _frameBytes;

    /// <summary>A frame's rows from a paint. Runs on CEF's UI thread: one copy, nothing allocated.</summary>
    public void WriteFrame(IntPtr bgra, int width, int height)
    {
        if (width != Width || height != Height)
            return;
        var latest = Volatile.Read(ref Int(OffLatest));
        var reading = Volatile.Read(ref Int(OffReading));
        var into = 0;
        while (into == latest || into == reading)
            into++;
        Buffer.MemoryCopy((void*)bgra, Frame(into), _frameBytes, _frameBytes);
        Volatile.Write(ref Int(OffLatest), into);
        Volatile.Write(ref Long(OffSeq), Volatile.Read(ref Long(OffSeq)) + 1);
    }

    /// <summary>A packet of planar sound, mixed to one channel: a source in the world is a point. Runs on CEF's audio thread.</summary>
    public void WriteAudio(IntPtr planes, int frames, int channels)
    {
        var ch = (float**)planes;
        var ring = (float*)(_base + HeaderBytes);
        var at = Volatile.Read(ref Long(OffAudioWrite));
        var scale = 1f / Math.Max(1, channels);
        for (var i = 0; i < frames; i++)
        {
            var sum = 0f;
            for (var c = 0; c < channels; c++)
                sum += ch[c][i];
            ring[(at + i) & (RingFrames - 1)] = sum * scale;
        }
        Volatile.Write(ref Long(OffAudioWrite), at + frames);
    }

    public void Dispose()
    {
        _view.SafeMemoryMappedViewHandle.ReleasePointer();
        _view.Dispose();
        _file.Dispose();
    }
}
