namespace SlopBrowser;

/// <summary>
/// Where a page's picture, sound, mouse and reports go: the game's shared block and slot while it is on a screen, and nowhere while it is on the shelf
/// (a Browser Screen out of range, kept as it was: docs/sandbox/MEDIA.md, "Sign-ins and out of range"). Paints, sound and the mouse read the block under a read lock and
/// <see cref="Detach"/> takes the write lock, so a block is never touched after it was let go, whichever CEF thread is painting. A shelved page says nothing:
/// its old slot may hold another page by then.
/// </summary>
internal sealed class PageSink(SharedBlock block, int slot)
{
    private readonly ReaderWriterLockSlim _gate = new(); // (never disposed: a late callback from CEF must find it working)
    private SharedBlock? _block = block;
    private int _slot = slot;
    public readonly int Width = block.Width, Height = block.Height;

    /// <summary>The slot this page answers to, or -1 on the shelf.</summary>
    public int Slot => Volatile.Read(ref _slot);

    /// <summary>A frame into the block. False on the shelf.</summary>
    public bool Paint(IntPtr bgra, int width, int height)
    {
        _gate.EnterReadLock();
        try
        {
            _block?.WriteFrame(bgra, width, height);
            return _block != null;
        }
        finally
        {
            _gate.ExitReadLock();
        }
    }

    public void Audio(IntPtr planes, int frames, int channels, int rate)
    {
        _gate.EnterReadLock();
        try
        {
            _block?.WriteAudio(planes, frames, channels, rate);
        }
        finally
        {
            _gate.ExitReadLock();
        }
    }

    /// <summary>For the log: what has gone into the ring, and the loudest sample since the last ask.</summary>
    public (long Frames, float Peak) AudioLevels()
    {
        _gate.EnterReadLock();
        try
        {
            if (_block is not { } b)
                return default;
            var levels = (b.AudioFrames, b.AudioPeak);
            b.AudioPeak = 0;
            return levels;
        }
        finally
        {
            _gate.ExitReadLock();
        }
    }

    /// <summary>The next mouse or key event from the game; none on the shelf.</summary>
    public bool NextMouse(out int kind, out int x, out int y, out int data)
    {
        _gate.EnterReadLock();
        try
        {
            if (_block is { } b)
                return b.NextMouse(out kind, out x, out y, out data);
            kind = x = y = data = 0;
            return false;
        }
        finally
        {
            _gate.ExitReadLock();
        }
    }

    /// <summary>A report for this page's slot, as <c>VERB SLOT REST</c>. Nothing on the shelf.</summary>
    public void Say(string verb, string rest)
    {
        _gate.EnterReadLock();
        try
        {
            if (_slot >= 0)
                Program.Say(rest.Length == 0 ? $"{verb} {_slot}" : $"{verb} {_slot} {rest}");
        }
        finally
        {
            _gate.ExitReadLock();
        }
    }

    /// <summary>Off the screen: the block is the caller's to dispose, and from here on nothing writes, reads or reports.</summary>
    public SharedBlock? Detach()
    {
        _gate.EnterWriteLock();
        try
        {
            var b = _block;
            _block = null;
            Volatile.Write(ref _slot, -1);
            return b;
        }
        finally
        {
            _gate.ExitWriteLock();
        }
    }

    /// <summary>
    /// Onto a screen again, in slot <paramref name="slot"/>, with that slot's block (the same size as this page's). <paramref name="announce"/> runs under the write lock, so what it says
    /// comes before any report the page makes once it is attached (it calls <see cref="Program.Say"/>, never this sink).
    /// </summary>
    public void Attach(SharedBlock to, int slot, Action? announce = null)
    {
        _gate.EnterWriteLock();
        try
        {
            _block = to;
            Volatile.Write(ref _slot, slot);
            announce?.Invoke();
        }
        finally
        {
            _gate.ExitWriteLock();
        }
    }
}
