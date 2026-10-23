using System.Numerics;
using System.Runtime.InteropServices;
using Brutal.Numerics;
using Brutal.OpenAlApi;

namespace CallOfSlop;

/// <summary>
/// The microphone, through OpenAL capture (Brutal.OpenAl exposes alcCaptureOpenDevice / alcCaptureSamples as
/// <c>Alc.CaptureOpenDevice</c> and <see cref="CaptureDevice"/>), at the voice format (16 kHz mono 16-bit; OpenAL Soft
/// converts from whatever the device does). It is only ever open while the player has turned their mic on (or is testing
/// it in Settings): the OS's "microphone in use" indicator is honest. Read on the main thread, 20 ms at a time.
/// </summary>
public sealed class MicCapture : IDisposable
{
    private CaptureDevice? _device;

    /// <summary>The device asked for ("" = the system default) and the one actually open.</summary>
    public string Requested { get; private set; } = "";

    public string OpenName { get; private set; } = "";

    /// <summary>One line for Settings and the log: "closed", "open: ...", or why it couldn't open.</summary>
    public string Status { get; private set; } = "closed";

    public bool IsOpen => _device != null;

    /// <summary>Frames read since opening, and frames thrown away because they piled up (a hitch).</summary>
    public long FramesRead, FramesDropped;

    private double _failedAt = double.NegativeInfinity;
    private string _failedFor = "\u0001";

    /// <summary>The capture devices OpenAL lists (empty if none or no OpenAL).</summary>
    public static string[] Devices()
    {
        try
        {
            return Alc.GetCaptureDeviceSpecifiers();
        }
        catch (Exception)
        {
            return [];
        }
    }

    /// <summary>Opens <paramref name="device"/> ("" = default) unless it is already open. A failure is retried at most every 5 s.</summary>
    public bool Open(string device, double now)
    {
        device ??= "";
        if (_device != null && device == Requested)
            return true;
        if (device == _failedFor && now - _failedAt < 5)
            return false;
        Close();
        Requested = device;
        try
        {
            // A 0.5 s ring: we read every frame, so it only fills on a long hitch.
            _device = Alc.CaptureOpenDevice(device.Length == 0 ? null : device, VoiceCodec.SampleRate, AlFormat.Mono16, VoiceCodec.SampleRate / 2);
            _device.Start();
            OpenName = _device.Specifier ?? (device.Length == 0 ? "default" : device);
            Status = $"open: {OpenName}";
            FramesRead = FramesDropped = 0;
            Console.WriteLine($"[voice] microphone open: {OpenName} (16 kHz mono)");
            return true;
        }
        catch (Exception ex)
        {
            _device?.Dispose();
            _device = null;
            _failedAt = now;
            _failedFor = device;
            Status = $"couldn't open the microphone: {ex.Message}";
            Console.WriteLine($"[voice] {Status}");
            return false;
        }
    }

    /// <summary>Reads one 20 ms frame into <paramref name="pcm"/> if one is ready. Skips the backlog past 200 ms (keeps latency low).</summary>
    public bool Read(short[] pcm)
    {
        if (_device == null)
            return false;
        try
        {
            var available = _device.AvailableSamples;
            while (available >= VoiceCodec.FrameSamples * 11)
            {
                _device.Samples(MemoryMarshal.AsBytes(pcm.AsSpan(0, VoiceCodec.FrameSamples)), VoiceCodec.FrameSamples);
                available -= VoiceCodec.FrameSamples;
                FramesDropped++;
            }
            if (available < VoiceCodec.FrameSamples)
                return false;
            _device.Samples(MemoryMarshal.AsBytes(pcm.AsSpan(0, VoiceCodec.FrameSamples)), VoiceCodec.FrameSamples);
            FramesRead++;
            return true;
        }
        catch (Exception ex)
        {
            Status = $"microphone stopped: {ex.Message}";
            Console.WriteLine($"[voice] {Status}");
            Close();
            Status = $"microphone stopped: {ex.Message}";
            return false;
        }
    }

    public void Close()
    {
        if (_device == null)
            return;
        try
        {
            _device.Stop();
        }
        catch (Exception)
        {
            // closing anyway
        }
        _device.Dispose();
        _device = null;
        Status = "closed";
        Console.WriteLine($"[voice] microphone closed ({FramesRead} frames read, {FramesDropped} skipped)");
    }

    public void Dispose() => Close();
}

/// <summary>
/// Voice playback: a small pool of streaming OpenAL sources, one per talker heard at the moment. Each source plays its
/// talker's <see cref="JitterBuffer"/> 20 ms at a time. Proximity voices sit at the talker's head, fade with distance
/// (<see cref="VoiceRules.ProximityGain"/>, computed here rather than by OpenAL so it is exactly 0 at the range) and go
/// dull through walls (a ray from the listener, then the EFX low-pass); team radio and flat voices follow the listener.
/// Sources and buffers are made the first time anyone talks. No per-frame allocations.
/// </summary>
public sealed partial class Audio
{
    /// <summary>Talkers heard at once.</summary>
    public const int VoiceSources = 8;

    private const int VoiceBuffersEach = 6, VoiceQueueTarget = 3;

    /// <summary>The microphone (Settings > Audio opens it; Program reads it).</summary>
    public readonly MicCapture Mic = new();

    /// <summary>Settings > Audio "Voice volume" (on top of the master volume) and "Mute all voice".</summary>
    public float VoiceVolume = 1f;

    public bool VoiceMuteAll;

    /// <summary>The microphone's last frame level (dBFS) and whether it was sent (Settings' meter; Program sets them).</summary>
    public float MicLevelDb = -96f;

    public bool MicTransmitting;

    /// <summary>"Test mic": your own voice, played straight back to you (flat).</summary>
    public readonly VoiceStream Loopback = new(254);

    private sealed class VoiceOut
    {
        public AlSource Source;
        public AlBuffer[] Free = [], All = [];
        public int FreeCount;
        public VoiceStream? Bound;
    }

    private VoiceOut[] _voiceOut = [];
    private readonly short[] _voicePcm = new short[VoiceCodec.FrameSamples];
    private readonly AlBuffer[] _voiceOne = new AlBuffer[1];
    private bool _voiceFailed;

    /// <summary>Voice frames handed to OpenAL, concealed frames among them, and talkers that found no free source.</summary>
    public long VoiceFramesPlayed, VoiceFramesConcealed, VoiceNoSource;

    /// <summary>Sources playing a talker right now.</summary>
    public int VoiceSourcesBusy
    {
        get
        {
            var n = 0;
            foreach (var v in _voiceOut)
                n += v.Bound != null ? 1 : 0;
            return n;
        }
    }

    private bool EnsureVoiceSources()
    {
        if (_voiceOut.Length > 0)
            return true;
        if (_voiceFailed)
            return false;
        try
        {
            var outs = new VoiceOut[VoiceSources];
            for (var i = 0; i < outs.Length; i++)
            {
                var src = Al.GenSource();
                src.Gain = 0f;
                src.RolloffFactor = 0f; // the gain is ours (VoiceRules.ProximityGain)
                if (EfxEnabled && _device != null && _device.Efx.MaxAuxiliarySends > 0)
                    src.SetAuxiliarySend(_slot, 0, AlFilter.Null); // the map's reverb, lightly
                var all = Al.GenBuffers(VoiceBuffersEach);
                outs[i] = new VoiceOut { Source = src, All = all, Free = (AlBuffer[])all.Clone(), FreeCount = VoiceBuffersEach };
            }
            _voiceOut = outs;
            Console.WriteLine($"[voice] playback ready: {VoiceSources} streaming sources x {VoiceBuffersEach} buffers of {VoiceCodec.FrameMs} ms");
            return true;
        }
        catch (Exception ex)
        {
            _voiceFailed = true;
            Console.WriteLine($"[voice] playback disabled: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Plays what <paramref name="hub"/>'s talkers (and the mic test loopback) have buffered. Call once a frame after
    /// <see cref="SetListener"/>; the game sets each stream's head position first.
    /// </summary>
    public void UpdateVoice(VoiceHub? hub, double now)
    {
        if (!Enabled)
            return;
        var anything = Loopback.Jitter.Buffered > 0 || Loopback.Jitter.Playing;
        if (hub != null)
            foreach (var s in hub.Active)
                anything |= s.Jitter.Buffered > 0 || s.Jitter.Playing;
        if (!anything && _voiceOut.Length == 0)
            return; // nobody has talked yet: nothing made
        if (!EnsureVoiceSources())
            return;
        try
        {
            if (hub != null)
                foreach (var s in hub.Active)
                    Bind(s);
            Bind(Loopback);
            foreach (var v in _voiceOut)
                if (v.Bound != null)
                    Service(v, now);
        }
        catch (Exception ex)
        {
            _voiceFailed = true;
            Console.WriteLine($"[voice] playback stopped: {ex.Message}");
            DisposeVoice();
        }
    }

    private void Bind(VoiceStream s)
    {
        if (s.Jitter.Buffered == 0 && !s.Jitter.Playing)
            return;
        VoiceOut? free = null;
        foreach (var v in _voiceOut)
        {
            if (v.Bound == s)
                return;
            if (v.Bound == null)
                free ??= v;
        }
        if (free == null)
        {
            VoiceNoSource++;
            return;
        }
        free.Bound = s;
        s.Occlusion = 0;
        s.OcclusionCheckedAt = double.NegativeInfinity;
    }

    private void Service(VoiceOut v, double now)
    {
        var s = v.Bound!;
        var src = v.Source;
        for (var processed = src.BuffersProcessed; processed > 0; processed--)
        {
            src.UnqueueBuffers(_voiceOne);
            v.Free[v.FreeCount++] = _voiceOne[0];
        }

        // Where and how loud.
        var spatial = s.Route == VoiceRoute.Proximity && s.HeadKnown;
        var gain = VoiceMuteAll ? 0f : VoiceVolume;
        var hf = 1f;
        if (spatial)
        {
            var d = Vector3.Distance(s.Head, _listener);
            if (now - s.OcclusionCheckedAt > 0.1)
            {
                s.OcclusionCheckedAt = now;
                var target = VoiceOccluded(_listener, s.Head) ? 1f : 0f;
                s.Occlusion += (target - s.Occlusion) * 0.5f; // eases over a few checks
            }
            gain *= VoiceRules.ProximityGain(d, s.Occlusion);
            hf = VoiceRules.ProximityHf(d, s.Occlusion);
            src.SourceRelative = false;
            src.Position = new float3(s.Head.X, s.Head.Y, s.Head.Z);
        }
        else
        {
            if (s.Route == VoiceRoute.Radio)
                gain *= 0.9f;
            src.SourceRelative = true;
            src.Position = new float3(0, 0, 0);
        }
        src.Gain = gain;
        if (EfxEnabled)
        {
            try
            {
                if (hf < 0.99f)
                {
                    _lowpass.GainHf = hf;
                    src.DirectFilter = _lowpass; // copies the filter's settings
                }
                else
                    src.DirectFilter = AlFilter.Null;
            }
            catch (Exception)
            {
                // the dry voice is fine
            }
        }

        // Keep a few frames queued.
        while (v.FreeCount > 0 && src.BuffersQueued < VoiceQueueTarget)
        {
            var got = s.Jitter.Pull(_voicePcm);
            if (got == VoicePull.Silence)
                break;
            if (s.Route == VoiceRoute.Radio)
                s.Radio.Process(_voicePcm);
            var buffer = v.Free[--v.FreeCount];
            buffer.SetData(AlFormat.Mono16, MemoryMarshal.AsBytes(_voicePcm.AsSpan()), VoiceCodec.SampleRate);
            _voiceOne[0] = buffer;
            src.QueueBuffers(_voiceOne);
            VoiceFramesPlayed++;
            if (got == VoicePull.Concealed)
                VoiceFramesConcealed++;
        }
        var queued = src.BuffersQueued;
        if (queued > 0 && src.State != AlSourceState.Playing)
            src.Play(); // a new talk spurt, or after an underrun
        if (queued == 0 && !s.Jitter.Playing && s.Jitter.Buffered == 0)
        {
            src.Stop();
            v.Bound = null; // free for the next talker
        }
    }

    /// <summary>Is there level geometry between the listener and a talker's head?</summary>
    internal static bool VoiceOccluded(Vector3 from, Vector3 to)
    {
        var d = to - from;
        var len = d.Length();
        if (len < 1f)
            return false;
        try
        {
            return World.RaycastWorld(from, d / len, len) < len - 0.6f;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Plays a mic frame straight back (the "Test mic" button), through the same jitter buffer path.</summary>
    public void LoopbackFrame(ReadOnlySpan<byte> frame, ushort seq, double now)
    {
        Loopback.Route = VoiceRoute.Open;
        if (Loopback.Jitter.Push(seq, frame))
            Loopback.LastFrame = now;
    }

    private void DisposeVoice()
    {
        foreach (var v in _voiceOut)
        {
            try
            {
                v.Source.Stop();
                v.Source.Buffer = AlBuffer.Null; // unqueues everything
                Al.DeleteSources([v.Source]);
                Al.DeleteBuffers(v.All);
            }
            catch (Exception)
            {
                // best effort on the way out
            }
        }
        _voiceOut = [];
    }

    /// <summary>One line for the log about voice playback.</summary>
    public string VoiceStats() =>
        $"{VoiceFramesPlayed} frames played ({VoiceFramesConcealed} concealed), {VoiceSourcesBusy}/{_voiceOut.Length} sources busy, {VoiceNoSource} talkers without a source";
}
