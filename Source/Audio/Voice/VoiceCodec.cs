namespace CallOfSlop;

/// <summary>
/// The voice chat's audio format and codec. No Opus package is available, so voice uses IMA-ADPCM: 16 kHz mono, 4 bits a
/// sample (64 kbps), in 20 ms frames of <see cref="FrameSamples"/> samples. Each frame starts with the coder state (the
/// predicted sample and the step index), so every frame decodes on its own: a lost packet never corrupts the next one.
/// Encoding and decoding cost a few table lookups a sample and never allocate.
/// </summary>
public static class VoiceCodec
{
    /// <summary>Voice sample rate (Hz).</summary>
    public const int SampleRate = 16000;

    /// <summary>One frame: 20 ms.</summary>
    public const int FrameMs = 20;

    /// <summary>Samples in a frame (320).</summary>
    public const int FrameSamples = SampleRate * FrameMs / 1000;

    /// <summary>Frame header: predictor (int16 little-endian), step index, and a spare byte (0).</summary>
    public const int HeaderBytes = 4;

    /// <summary>An encoded frame: the header plus two samples a byte (164 bytes).</summary>
    public const int FrameBytes = HeaderBytes + FrameSamples / 2;

    /// <summary>Frames a second (50).</summary>
    public const int FramesPerSecond = 1000 / FrameMs;

    private static readonly int[] StepTable =
    [
        7, 8, 9, 10, 11, 12, 13, 14, 16, 17, 19, 21, 23, 25, 28, 31, 34, 37, 41, 45, 50, 55, 60, 66, 73, 80, 88, 97, 107, 118, 130, 143,
        157, 173, 190, 209, 230, 253, 279, 307, 337, 371, 408, 449, 494, 544, 598, 658, 724, 796, 876, 963, 1060, 1166, 1282, 1411, 1552,
        1707, 1878, 2066, 2272, 2499, 2749, 3024, 3327, 3660, 4026, 4428, 4871, 5358, 5894, 6484, 7132, 7845, 8630, 9493, 10442, 11487,
        12635, 13899, 15289, 16818, 18500, 20350, 22385, 24623, 27086, 29794, 32767,
    ];

    private static readonly int[] IndexTable = [-1, -1, -1, -1, 2, 4, 6, 8, -1, -1, -1, -1, 2, 4, 6, 8];

    /// <summary>The encoder's running state (kept across frames so each frame starts where the last ended).</summary>
    public struct EncoderState
    {
        public int Predictor;
        public int Index;
    }

    /// <summary>
    /// Encodes <see cref="FrameSamples"/> samples of <paramref name="pcm"/> into <paramref name="frame"/>
    /// (<see cref="FrameBytes"/> bytes). Returns the bytes written.
    /// </summary>
    public static int Encode(ReadOnlySpan<short> pcm, Span<byte> frame, ref EncoderState state)
    {
        if (pcm.Length < FrameSamples || frame.Length < FrameBytes)
            throw new ArgumentException("voice frame buffers too small");
        var predictor = Math.Clamp(state.Predictor, short.MinValue, short.MaxValue);
        var index = Math.Clamp(state.Index, 0, 88);
        frame[0] = (byte)predictor;
        frame[1] = (byte)(predictor >> 8);
        frame[2] = (byte)index;
        frame[3] = 0;
        for (var i = 0; i < FrameSamples; i += 2)
        {
            var lo = EncodeSample(pcm[i], ref predictor, ref index);
            var hi = EncodeSample(pcm[i + 1], ref predictor, ref index);
            frame[HeaderBytes + i / 2] = (byte)(lo | (hi << 4));
        }
        state.Predictor = predictor;
        state.Index = index;
        return FrameBytes;
    }

    private static int EncodeSample(int sample, ref int predictor, ref int index)
    {
        var step = StepTable[index];
        var diff = sample - predictor;
        var nibble = 0;
        if (diff < 0)
        {
            nibble = 8;
            diff = -diff;
        }
        var delta = step >> 3;
        if (diff >= step)
        {
            nibble |= 4;
            diff -= step;
            delta += step;
        }
        step >>= 1;
        if (diff >= step)
        {
            nibble |= 2;
            diff -= step;
            delta += step;
        }
        step >>= 1;
        if (diff >= step)
        {
            nibble |= 1;
            delta += step;
        }
        predictor = Math.Clamp((nibble & 8) != 0 ? predictor - delta : predictor + delta, short.MinValue, short.MaxValue);
        index = Math.Clamp(index + IndexTable[nibble], 0, 88);
        return nibble;
    }

    /// <summary>
    /// Decodes one frame into <paramref name="pcm"/> (<see cref="FrameSamples"/> samples). A frame of the wrong size or with a
    /// bad step index (garbage from the network) decodes to silence and returns false.
    /// </summary>
    public static bool Decode(ReadOnlySpan<byte> frame, Span<short> pcm)
    {
        if (pcm.Length < FrameSamples)
            throw new ArgumentException("voice pcm buffer too small");
        if (frame.Length != FrameBytes || frame[2] > 88)
        {
            pcm[..FrameSamples].Clear();
            return false;
        }
        int predictor = (short)(frame[0] | (frame[1] << 8));
        int index = frame[2];
        for (var i = 0; i < FrameSamples; i += 2)
        {
            var b = frame[HeaderBytes + i / 2];
            pcm[i] = DecodeSample(b & 15, ref predictor, ref index);
            pcm[i + 1] = DecodeSample(b >> 4, ref predictor, ref index);
        }
        return true;
    }

    private static short DecodeSample(int nibble, ref int predictor, ref int index)
    {
        var step = StepTable[index];
        var delta = step >> 3;
        if ((nibble & 4) != 0)
            delta += step;
        if ((nibble & 2) != 0)
            delta += step >> 1;
        if ((nibble & 1) != 0)
            delta += step >> 2;
        predictor = Math.Clamp((nibble & 8) != 0 ? predictor - delta : predictor + delta, short.MinValue, short.MaxValue);
        index = Math.Clamp(index + IndexTable[nibble], 0, 88);
        return (short)predictor;
    }

    /// <summary>Signal-to-noise ratio (dB) of <paramref name="decoded"/> against <paramref name="original"/> (self-test).</summary>
    public static double Snr(ReadOnlySpan<short> original, ReadOnlySpan<short> decoded)
    {
        double sig = 0, err = 0;
        var n = Math.Min(original.Length, decoded.Length);
        for (var i = 0; i < n; i++)
        {
            double s = original[i], e = original[i] - decoded[i];
            sig += s * s;
            err += e * e;
        }
        return err <= 0 ? 120 : 10 * Math.Log10(Math.Max(sig, 1e-9) / err);
    }
}
