using System.IO.MemoryMappedFiles;
using System.Runtime.CompilerServices;

namespace CallOfSlop;

/// <summary>
/// One page's memory shared with the browser process (tools/browser/SharedBlock.cs holds the same layout): a header, a ring of mono floats
/// for the sound and three frames of BGRA for the picture. The newest finished frame is swapped in with one int, so neither side ever waits
/// for the other, and reading a frame or a block of sound allocates nothing. Made once per page size, when a page first needs it.
/// </summary>
public sealed unsafe class MediaBlock : IDisposable
{
    public const int MaxWidth = 1280, MaxHeight = 720, HeaderBytes = 4096, RingFrames = 1 << 17;
    private const int OffLatest = 16, OffReading = 20, OffSeq = 24, OffAudioWrite = 32, OffMouseHead = 64, OffMouseTail = 68, OffMouse = 256;
    public const int MouseSlots = 128, MouseMove = 1, MouseDown = 2, MouseUp = 3, MouseWheel = 4, MouseLeave = 5;

    /// <summary>Sound is dropped to this far behind the browser. It builds up only after a stall; past this the picture would be visibly ahead.</summary>
    private const int MaxBehindFrames = Audio.SampleRate / 3;

    private readonly MemoryMappedFile _file;
    private readonly MemoryMappedViewAccessor _view;
    private readonly byte* _base;
    private readonly string? _path;
    private readonly int _frameBytes;
    private long _seenFrame, _audioRead = -1;
    public readonly string Name;
    public readonly int Width, Height;

    public MediaBlock(string name, int width, int height)
    {
        Name = name;
        Width = width;
        Height = height;
        _frameBytes = width * height * 4;
        var size = HeaderBytes + RingFrames * sizeof(float) + 3L * _frameBytes;
        if (OperatingSystem.IsWindows())
            _file = MemoryMappedFile.CreateNew(name, size);
        else
        {
            // (no named memory here: a file, in memory where there is such a place)
            _path = Name = Path.Combine(Directory.Exists("/dev/shm") ? "/dev/shm" : Path.GetTempPath(), name);
            _file = MemoryMappedFile.CreateFromFile(_path, FileMode.Create, null, size);
        }
        _view = _file.CreateViewAccessor(0, size, MemoryMappedFileAccess.ReadWrite);
        byte* p = null;
        _view.SafeMemoryMappedViewHandle.AcquirePointer(ref p);
        _base = p;
        new Span<byte>(_base, HeaderBytes).Clear();
        Volatile.Write(ref Int(OffLatest), -1);
        Volatile.Write(ref Int(OffReading), -1);
    }

    private ref int Int(int offset) => ref Unsafe.AsRef<int>(_base + offset);
    private ref long Long(int offset) => ref Unsafe.AsRef<long>(_base + offset);

    public long FrameSeq => Volatile.Read(ref Long(OffSeq));

    /// <summary>How much sound the browser has written, and this side has read: for the log.</summary>
    public long AudioWritten => Volatile.Read(ref Long(OffAudioWrite));
    public long AudioRead => _audioRead;

    /// <summary>The newest frame if it is one not yet taken, or null. Valid until the next call.</summary>
    public byte* TakeFrame()
    {
        var seq = FrameSeq;
        if (seq == _seenFrame)
            return null;
        var latest = Volatile.Read(ref Int(OffLatest));
        if (latest < 0)
            return null;
        Volatile.Write(ref Int(OffReading), latest);
        if (Volatile.Read(ref Int(OffLatest)) != latest)
        {
            // (a newer one came in between, and this one may be written over: the next call takes that)
            return null;
        }
        _seenFrame = seq;
        return _base + HeaderBytes + RingFrames * sizeof(float) + (long)latest * _frameBytes;
    }

    /// <summary>A mouse event for the page. The ring is the header's: four ints each, read by the browser every few ms. Full (the browser stopped reading): dropped.</summary>
    public void Mouse(int kind, int x, int y, int data)
    {
        var head = Int(OffMouseHead);
        if (head - Volatile.Read(ref Int(OffMouseTail)) >= MouseSlots)
            return;
        var e = (int*)(_base + OffMouse) + (head & (MouseSlots - 1)) * 4;
        e[0] = kind;
        e[1] = x;
        e[2] = y;
        e[3] = data;
        Volatile.Write(ref Int(OffMouseHead), head + 1);
    }

    /// <summary>Starts over: a new track in this block. The browser's ring goes on from where it is.</summary>
    public void Reset()
    {
        _seenFrame = FrameSeq;
        _audioRead = -1;
    }

    /// <summary>Up to <paramref name="into"/>'s length of sound. The rest of it is left as it is; the count is what came.</summary>
    public int ReadAudio(Span<float> into)
    {
        var write = Volatile.Read(ref Long(OffAudioWrite));
        if (_audioRead < 0 || write - _audioRead > MaxBehindFrames)
            _audioRead = Math.Max(0, write - into.Length); // (first read, or a long stall)
        var n = (int)Math.Min(write - _audioRead, into.Length);
        var ring = (float*)(_base + HeaderBytes);
        for (var i = 0; i < n; i++)
            into[i] = ring[(_audioRead + i) & (RingFrames - 1)];
        _audioRead += n;
        return n;
    }

    public void Dispose()
    {
        _view.SafeMemoryMappedViewHandle.ReleasePointer();
        _view.Dispose();
        _file.Dispose();
        if (_path != null)
            try
            {
                File.Delete(_path);
            }
            catch (IOException)
            {
            }
    }
}
