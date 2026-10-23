namespace CallOfSlop;

/// <summary>What <see cref="JitterBuffer.Pull"/> produced.</summary>
public enum VoicePull : byte
{
    /// <summary>Nothing to play (not talking, or still filling): don't queue anything.</summary>
    Silence,

    /// <summary>A real frame.</summary>
    Frame,

    /// <summary>A lost frame, concealed (the last frame repeated, fading).</summary>
    Concealed,
}

/// <summary>
/// One talker's jitter buffer: frames arrive out of order and late over an unreliable channel and are played in sequence
/// order after a short delay (<see cref="TargetFrames"/>, 60 ms; it grows to <see cref="MaxTargetFrames"/> when frames keep
/// arriving late). A missing frame is concealed by repeating the last one more quietly each time (up to
/// <see cref="MaxConceal"/>); after that, or when the talker stops, playback stops until the buffer fills again. Frames are
/// stored encoded and decoded as they play. Fixed arrays, no allocations after construction.
/// </summary>
public sealed class JitterBuffer
{
    /// <summary>Ring size in frames (320 ms): a frame further ahead than this restarts the stream.</summary>
    public const int Slots = 16;

    public const int MinTargetFrames = 3, MaxTargetFrames = 5;

    /// <summary>Concealed frames in a row before playback stops (60 ms).</summary>
    public const int MaxConceal = 3;

    /// <summary>Each concealed frame is this much quieter than the one before.</summary>
    public const float ConcealFade = 0.55f;

    private readonly byte[] _data = new byte[Slots * VoiceCodec.FrameBytes];
    private readonly ushort[] _seq = new ushort[Slots];
    private readonly bool[] _has = new bool[Slots];
    private readonly short[] _last = new short[VoiceCodec.FrameSamples];
    private ushort _next;
    private bool _started, _playing;
    private int _lossRun, _lateRecent;
    private float _concealGain;

    /// <summary>The delay the buffer fills to before playing (frames).</summary>
    public int TargetFrames { get; private set; } = MinTargetFrames;

    public bool Playing => _playing;

    /// <summary>Statistics: frames received, played, concealed, dropped as late or duplicate, and overflow skips.</summary>
    public int Received, Played, Concealed, Late, Skipped;

    /// <summary>Frames stored and not yet played.</summary>
    public int Buffered
    {
        get
        {
            var n = 0;
            for (var i = 0; i < Slots; i++)
                n += _has[i] ? 1 : 0;
            return n;
        }
    }

    /// <summary>Stores a received frame (<see cref="VoiceCodec.FrameBytes"/> bytes). Returns false if it was dropped (late, duplicate, wrong size).</summary>
    public bool Push(ushort seq, ReadOnlySpan<byte> frame)
    {
        if (frame.Length != VoiceCodec.FrameBytes)
            return false;
        if (!_started)
        {
            _started = true;
            _next = seq;
        }
        var ahead = (short)(seq - _next);
        if (ahead < 0)
        {
            Late++;
            _lateRecent++;
            if (_lateRecent >= 3 && TargetFrames < MaxTargetFrames)
            {
                TargetFrames++; // the network is jittery: wait a little longer
                _lateRecent = 0;
            }
            return false;
        }
        if (ahead >= Slots)
        {
            // Far ahead (a new talk spurt after a long gap, or a burst after a stall): start over from this frame.
            Array.Clear(_has);
            _next = seq;
            _playing = false;
            Skipped++;
        }
        var slot = seq % Slots;
        if (_has[slot] && _seq[slot] == seq)
        {
            Late++; // a duplicate
            return false;
        }
        _has[slot] = true;
        _seq[slot] = seq;
        frame.CopyTo(_data.AsSpan(slot * VoiceCodec.FrameBytes, VoiceCodec.FrameBytes));
        Received++;
        return true;
    }

    /// <summary>The newest stored frame's distance ahead of the next one to play, +1 (0 when empty).</summary>
    private int Depth()
    {
        var depth = 0;
        for (var i = 0; i < Slots; i++)
            if (_has[i])
                depth = Math.Max(depth, (short)(_seq[i] - _next) + 1);
        return depth;
    }

    /// <summary>Produces the next 20 ms to play into <paramref name="pcm"/> (<see cref="VoiceCodec.FrameSamples"/> samples).</summary>
    public VoicePull Pull(Span<short> pcm)
    {
        if (!_playing)
        {
            if (!_started || Depth() < TargetFrames)
                return VoicePull.Silence;
            _playing = true;
            _lossRun = 0;
        }
        else
        {
            // Too far behind (a burst arrived at once): skip the oldest so the delay stays bounded.
            while (Depth() > TargetFrames + 3)
            {
                _has[_next % Slots] = false;
                _next++;
                Skipped++;
            }
        }

        var slot = _next % Slots;
        if (_has[slot] && _seq[slot] == _next)
        {
            _has[slot] = false;
            _next++;
            VoiceCodec.Decode(_data.AsSpan(slot * VoiceCodec.FrameBytes, VoiceCodec.FrameBytes), pcm);
            pcm[..VoiceCodec.FrameSamples].CopyTo(_last);
            _lossRun = 0;
            _concealGain = 1f;
            Played++;
            return VoicePull.Frame;
        }

        // Missing: conceal while more is coming (or briefly at the end), else stop.
        _lossRun++;
        var more = Depth() > 0;
        if (_lossRun > MaxConceal || (!more && _lossRun > 1))
        {
            _playing = false;
            _lossRun = 0;
            if (!more)
                _lateRecent = Math.Max(0, _lateRecent - 1);
            return VoicePull.Silence;
        }
        _next++;
        _concealGain *= ConcealFade;
        for (var i = 0; i < VoiceCodec.FrameSamples; i++)
            pcm[i] = (short)(_last[i] * _concealGain);
        Concealed++;
        return VoicePull.Concealed;
    }

    /// <summary>Forgets everything (the talker left, or voice was turned off).</summary>
    public void Reset()
    {
        Array.Clear(_has);
        _started = _playing = false;
        _lossRun = _lateRecent = 0;
        TargetFrames = MinTargetFrames;
        Received = Played = Concealed = Late = Skipped = 0;
    }
}
