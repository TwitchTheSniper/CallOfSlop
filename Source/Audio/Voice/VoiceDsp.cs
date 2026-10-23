namespace CallOfSlop;

/// <summary>
/// The talking side of voice chat, without any device (so the self-test can drive it): takes 20 ms microphone frames,
/// removes DC, measures the level, decides whether to transmit (push-to-talk, or voice activation above a threshold with a
/// short hang time), applies a gentle noise gate and encodes the frame (<see cref="VoiceCodec"/>). No allocations.
/// </summary>
public sealed class VoiceSender
{
    /// <summary>Below this level (dBFS) push-to-talk frames are turned down (the noise gate), so an open mic doesn't hiss.</summary>
    public const float GateDb = -56f;

    /// <summary>How far the gate turns the hiss down.</summary>
    public const float GateFloor = 0.15f;

    /// <summary>Voice activation stays open this long after the level drops under the threshold (no clipped word endings).</summary>
    public const double VadHangSeconds = 0.4;

    /// <summary>The voice-activation threshold range in Settings > Audio (dBFS).</summary>
    public const float MinThresholdDb = -60f, MaxThresholdDb = -10f, DefaultThresholdDb = -38f;

    private float _dcIn, _dcOut, _gate = 1f;
    private double _vadUntil = double.NegativeInfinity;
    private VoiceCodec.EncoderState _enc;

    /// <summary>The encoded frame after <see cref="Process"/> returned true.</summary>
    public readonly byte[] Frame = new byte[VoiceCodec.FrameBytes];

    /// <summary>The next frame's sequence number (it wraps).</summary>
    public ushort Seq;

    /// <summary>The last frame's RMS level (dBFS, -96 for silence) and peak level (0-1).</summary>
    public float LevelDb = -96f, Peak;

    /// <summary>The last frame was sent.</summary>
    public bool Transmitting;

    /// <summary>
    /// Processes one frame in place (DC removed, gated). Returns true (with <see cref="Frame"/> filled and <see cref="Seq"/>
    /// moved on) when it should be sent: <paramref name="pushToTalk"/> is held, or with <paramref name="voiceActivation"/>
    /// the level is over <paramref name="thresholdDb"/> (or was, within <see cref="VadHangSeconds"/>).
    /// </summary>
    public bool Process(Span<short> pcm, bool pushToTalk, bool voiceActivation, float thresholdDb, double now)
    {
        var n = Math.Min(pcm.Length, VoiceCodec.FrameSamples);
        double sum = 0;
        var peak = 0f;
        for (var i = 0; i < n; i++)
        {
            // One-pole DC blocker (about 20 Hz at 16 kHz).
            float x = pcm[i];
            var y = x - _dcIn + 0.992f * _dcOut;
            _dcIn = x;
            _dcOut = y;
            var v = Math.Clamp(y, -32768f, 32767f);
            pcm[i] = (short)v;
            sum += (double)v * v;
            peak = MathF.Max(peak, MathF.Abs(v));
        }
        var rms = Math.Sqrt(sum / Math.Max(1, n)) / 32768.0;
        LevelDb = rms <= 1.6e-5 ? -96f : (float)(20 * Math.Log10(rms));
        Peak = peak / 32768f;

        bool send;
        if (voiceActivation)
        {
            if (LevelDb >= thresholdDb)
                _vadUntil = now + VadHangSeconds;
            send = now <= _vadUntil;
        }
        else
            send = pushToTalk;

        // Noise gate (push-to-talk only: voice activation is its own gate). Ramped across the frame: no clicks.
        var target = !voiceActivation && LevelDb < GateDb ? GateFloor : 1f;
        if (MathF.Abs(target - _gate) > 1e-3f || target < 1f)
        {
            var g0 = _gate;
            for (var i = 0; i < n; i++)
                pcm[i] = (short)(pcm[i] * (g0 + (target - g0) * (i + 1) / n));
            _gate = target;
        }

        Transmitting = send;
        if (!send)
            return false;
        VoiceCodec.Encode(pcm, Frame, ref _enc);
        Seq++;
        return true;
    }

    /// <summary>Starts over (new device, new session).</summary>
    public void Reset()
    {
        _dcIn = _dcOut = 0;
        _gate = 1f;
        _vadUntil = double.NegativeInfinity;
        _enc = default;
        LevelDb = -96f;
        Peak = 0;
        Transmitting = false;
    }

    /// <summary>A level in dBFS as 0-1 for a meter (-60 dB .. 0 dB).</summary>
    public static float Meter(float db) => Math.Clamp((db + 60f) / 60f, 0f, 1f);
}

/// <summary>
/// Team radio's sound: a telephone band (about 400 Hz to 3 kHz) with a little saturation, applied to decoded frames in
/// place. One per voice stream (it keeps its filter state across frames). No allocations.
/// </summary>
public struct RadioFilter
{
    private float _hpIn, _hpOut, _lp1, _lp2;

    public void Process(Span<short> pcm)
    {
        // At 16 kHz: high-pass pole for ~400 Hz, two one-pole low-passes for ~3 kHz.
        const float hp = 0.855f, lp = 0.68f;
        for (var i = 0; i < pcm.Length; i++)
        {
            float x = pcm[i] / 32768f;
            var h = hp * (_hpOut + x - _hpIn);
            _hpIn = x;
            _hpOut = h;
            _lp1 += lp * (h - _lp1);
            _lp2 += lp * (_lp1 - _lp2);
            var y = _lp2 * 1.8f; // make up the level the band took away
            y = y / (1f + MathF.Abs(y) * 0.6f); // soft clip: the radio's grit
            pcm[i] = (short)Math.Clamp(y * 32767f, -32768f, 32767f);
        }
    }

    public void Reset() => this = default;
}
