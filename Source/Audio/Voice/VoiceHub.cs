using System.Numerics;

namespace CallOfSlop;

/// <summary>One talker as a listener hears them: their jitter buffer, how the frames reach us, and where they are.</summary>
public sealed class VoiceStream(byte talker)
{
    public readonly byte Talker = talker;
    public readonly JitterBuffer Jitter = new();

    /// <summary>How the latest frame was routed (spatial, radio or flat).</summary>
    public VoiceRoute Route = VoiceRoute.Proximity;

    /// <summary>When the latest frame arrived (talking indicator, idle clean-up).</summary>
    public double LastFrame = double.NegativeInfinity;

    /// <summary>Set by the game each frame: the talker's head, and whether it's known (still in the roster).</summary>
    public Vector3 Head;
    public bool HeadKnown;

    /// <summary>Playback's smoothed wall occlusion (0 clear .. 1 blocked) and when it last cast its ray.</summary>
    public float Occlusion;
    public double OcclusionCheckedAt = double.NegativeInfinity;

    /// <summary>The radio band-pass state (team radio frames).</summary>
    public RadioFilter Radio;

    /// <summary>Your volume for this talker (0 to <see cref="VoiceBoost.Max"/>, 1 = as sent): set by the game each frame from Settings.</summary>
    public float Volume = 1f;

    /// <summary>The boost limiter's gain last frame (<see cref="VoiceBoost.Apply"/>).</summary>
    public float BoostState = 1f;

    public bool Talking(double now) => now - LastFrame < VoiceRules.TalkingHold;
}

/// <summary>Every talker this listener hears (by player id). Streams are made the first time someone talks and then reused.</summary>
public sealed class VoiceHub
{
    private readonly VoiceStream?[] _streams = new VoiceStream?[256];

    /// <summary>Streams that exist (for iterating without scanning 256 slots).</summary>
    public readonly List<VoiceStream> Active = new(8);

    /// <summary>Frames taken in, and dropped (muted, voice off, garbage).</summary>
    public int FramesIn, FramesDropped;

    public VoiceStream? Get(byte talker) => _streams[talker];

    /// <summary>Stores a frame from <paramref name="talker"/>.</summary>
    public void Push(byte talker, ushort seq, VoiceRoute route, ReadOnlySpan<byte> frame, double now)
    {
        var s = _streams[talker];
        if (s == null)
        {
            s = _streams[talker] = new VoiceStream(talker);
            Active.Add(s);
        }
        if (route != s.Route)
            s.Radio.Reset();
        s.Route = route;
        if (s.Jitter.Push(seq, frame))
        {
            s.LastFrame = now;
            FramesIn++;
        }
    }

    public bool Talking(byte talker, double now) => _streams[talker] is { } s && s.Talking(now);

    /// <summary>Forgets a talker's buffered audio (they left, or were muted).</summary>
    public void Clear(byte talker)
    {
        if (_streams[talker] is { } s)
        {
            s.Jitter.Reset();
            s.LastFrame = double.NegativeInfinity;
        }
    }

    public void ClearAll()
    {
        foreach (var s in Active)
        {
            s.Jitter.Reset();
            s.LastFrame = double.NegativeInfinity;
        }
    }
}
