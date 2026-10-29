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

/// <summary>
/// The playback boost for voices turned up past 100% (Settings > Audio "Voice volume" and the per-player volume, up to
/// <see cref="Max"/> each). OpenAL clamps a source's gain to AL_MAX_GAIN (often 1), so the part above 1 is applied here, to
/// the decoded 16-bit samples before they are queued (<see cref="Split"/>), and the rest stays on the OpenAL gain with
/// proximity, radio and the mixer on top. A per-stream limiter keeps loud talkers clean: when a frame's boosted peak
/// would pass <see cref="Ceiling"/> its boost drops at once to fit, then recovers over about 150 ms; a soft knee above
/// <see cref="Knee"/> rounds off anything left, so the output never clips. A boost never makes a voice quieter than 100%
/// (a voice already louder than the ceiling plays as it would at 100%).
/// No allocations.
/// </summary>
public static class VoiceBoost
{
    /// <summary>The highest voice volume (300%).</summary>
    public const float Max = 3f;

    /// <summary>The limiter's target peak, and where the soft knee starts (fractions of full scale).</summary>
    public const float Ceiling = 0.9f, Knee = 0.8f;

    /// <summary>How much of the way back to the full boost the limiter goes each 20 ms frame.</summary>
    public const float Release = 0.12f;

    /// <summary>Splits your volume for a voice into the gain on the samples (1 or more) and the gain on the OpenAL source (1 or less).</summary>
    public static (float Samples, float Source) Split(float volume) =>
        !(volume > 0f) ? (1f, 0f) : volume > 1f ? (MathF.Min(volume, Max * Max), 1f) : (1f, volume);

    /// <summary>
    /// Boosts <paramref name="pcm"/> by <paramref name="gain"/> (above 1) through the limiter. <paramref name="state"/> is the
    /// stream's limiter (the fraction of the boost it let through last frame; start it at 1). A gain of 1 or less leaves the
    /// samples alone and resets it.
    /// </summary>
    public static void Apply(Span<short> pcm, float gain, ref float state)
    {
        if (!(gain > 1f) || pcm.Length == 0)
        {
            state = 1f;
            return;
        }
        gain = MathF.Min(gain, Max * Max);
        var peak = 0;
        foreach (var x in pcm)
            peak = Math.Max(peak, Math.Abs((int)x));
        var p = peak / 32768f;
        var fit = p * gain > Ceiling ? Ceiling / (p * gain) : 1f; // the share of the boost this frame's peak allows
        if (!(state > 0f) || state > 1f)
            state = 1f;
        var from = state;
        var to = fit < from ? fit : from + (fit - from) * Release;
        var n = pcm.Length;
        for (var i = 0; i < n; i++)
        {
            var g = gain * MathF.Min(from + (to - from) * (i + 1) / n, fit);
            pcm[i] = g > 1f ? SoftClip(pcm[i] * g) : pcm[i];
        }
        state = to;
    }

    /// <summary>The soft knee's highest output: just under full scale, so a boosted voice never reaches a hard clip.</summary>
    public const short MaxSample = 32700;

    /// <summary>One sample through the soft knee: straight below <see cref="Knee"/>, then bent smoothly to stay under full scale.</summary>
    public static short SoftClip(float x)
    {
        const float k = Knee * 32767f, room = 32767f - k;
        var a = MathF.Abs(x);
        if (a <= k)
            return (short)x;
        var y = MathF.Min(k + room * MathF.Tanh((a - k) / room), MaxSample);
        return (short)(x < 0 ? -y : y);
    }

    /// <summary>A frame's peak (0-1) and mean square (for the audio mixer's Voice chat meter).</summary>
    public static void Level(ReadOnlySpan<short> pcm, out float peak, out float meanSquare)
    {
        var pk = 0;
        double sum = 0;
        foreach (var x in pcm)
        {
            pk = Math.Max(pk, Math.Abs((int)x));
            sum += (double)x * x;
        }
        peak = pk / 32768f;
        meanSquare = pcm.Length == 0 ? 0f : (float)(sum / pcm.Length / (32768.0 * 32768.0));
    }
}
