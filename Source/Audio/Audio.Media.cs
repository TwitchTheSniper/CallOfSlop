using System.Numerics;
using System.Runtime.InteropServices;
using Brutal.Numerics;
using Brutal.OpenAlApi;

namespace CallOfSlop;

/// <summary>
/// Sound from Video Screens and Music Players (docs/sandbox/MEDIA.md): streamed mono sources in the world, on their own thread, one per browser page
/// and one for the game's own tracks. Mono because OpenAL only places mono, and so the distance fades it as it does any other sound, through
/// the Media bus. The sources and the thread are made when something first plays, and the thread stops when nothing does.
/// <para>
/// The music CPU rule (at most 1.5x the baseline): a game track renders on this thread with a <see cref="MusicRenderer"/> of its own, so one render
/// is one baseline. There is only ever one, the nearest, and it holds while the music thread is rendering anything (the lobby's
/// decks, the kill frenzy): two renders at a time would be 2x.
/// </para>
/// </summary>
public sealed partial class Audio
{
    /// <summary>The voice of the game's own tracks; below it, one voice a browser page.</summary>
    public const int MediaTrackVoice = MediaDirector.MaxPages;

    /// <summary>The first speaker's voice: each repeats a player's voice and decodes nothing of its own.</summary>
    public const int MediaSpeakerVoice = MediaTrackVoice + 1;

    private const int MediaVoiceCount = MediaSpeakerVoice + MediaDirector.MaxSpeakers;
    private const int MediaSnapFrames = 256; // a speaker further off its player than this, twice running, snaps back (6 ms). It can't drift: this is for a voice the device held up.
    private const int MediaBuffers = 4, MediaFrames = 2048; // 4 x 46 ms queued, as the music's stream

    private sealed class MediaVoice
    {
        public AlSource Source;
        public AlBuffer[] Buffers = [];
        public readonly short[] Pcm = new short[MediaFrames];
        public readonly float[] Mono = new float[MediaFrames];
        public volatile MediaBlock? Block;
        public volatile int Follows = -1; // a speaker: the voice it should repeat (the game thread says)
        public int Joined = -1; // a speaker: the voice whose buffers are on its queue now (the sound thread's)
        public int Off; // a speaker: readings in a row past the snap distance
        public int Worst; // a player: the furthest any speaker was from it since the last log line, frames
        public int Queued; // a player: how many of Queue are queued, oldest first
        public readonly AlBuffer[] Queue = new AlBuffer[MediaBuffers];
        public long Got, Fills; // frames of real sound and buffers filled since the last log line
        public float Peak;
    }

    private sealed record MediaTrackPlay(MusicTrack Track, uint Seed, int Bar);

    private readonly MediaVoice[] _media = new MediaVoice[MediaVoiceCount];
    private readonly AlBuffer[] _mediaOne = new AlBuffer[1];
    private readonly AlSource[] _mediaGroup = new AlSource[MediaVoiceCount];
    private bool _mediaNoDefer;
    private readonly float[] _mediaStereo = new float[MusicPlayer.BlockFrames * 2];
    private Thread? _mediaThread;
    private volatile bool _mediaRun;
    private int _mediaLive;
    private MusicRenderer? _mediaRenderer; // (the thread's)
    private MediaTrackPlay? _mediaWant, _mediaHas;
    private int _mediaTrackPaused, _mediaTrackHeld;
    private bool _mediaSaidOff;
    private long _mediaNextLog;
    private const long MediaLogEvery = 5000;

    /// <summary>The game's track holds for the music thread: for the panel.</summary>
    public bool MediaTrackHeld => Volatile.Read(ref _mediaTrackHeld) != 0;

    private bool MediaStart()
    {
        if (!Enabled)
        {
            if (!_mediaSaidOff)
                Console.WriteLine("[media] no sound: this game has no audio device");
            _mediaSaidOff = true;
            return false;
        }
        if (_mediaThread != null)
            return true;
        try
        {
            for (var i = 0; i < _media.Length; i++)
            {
                if (_media[i] != null)
                    continue;
                _media[i] = new MediaVoice { Source = Al.GenSource(), Buffers = i < MediaSpeakerVoice ? Al.GenBuffers(MediaBuffers) : [] };
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"[media] no sound: no OpenAL sources: {e.Message}");
            return false;
        }
        _mediaRun = true;
        _mediaThread = new Thread(MediaLoop) { IsBackground = true, Name = "Media sound", Priority = ThreadPriority.AboveNormal };
        _mediaThread.Start();
        Console.WriteLine("[media] sound thread started");
        return true;
    }

    /// <summary>A page's sound comes through this voice from now on (null: nothing).</summary>
    public void MediaPage(int voice, MediaBlock? block)
    {
        if (block != null && !MediaStart())
            return;
        if (_media[voice] is not { } v || ReferenceEquals(v.Block, block))
            return;
        v.Block = block;
        MediaCount(block != null ? 1 : -1, v);
    }

    /// <summary>Speaker voice <paramref name="speaker"/> repeats this voice from now on (-1: silence).</summary>
    public void MediaSpeaker(int speaker, int follows)
    {
        if (_media[MediaSpeakerVoice + speaker] is { } v)
            v.Follows = follows;
    }

    /// <summary>A speaker's place and level, already worked out (<see cref="SpeakerShape.Level"/>): OpenAL only pans it.</summary>
    public void MediaSpeakerPlace(int speaker, Vector3 at, float gain)
    {
        if (!Enabled || _media[MediaSpeakerVoice + speaker] is not { } v)
            return;
        var s = v.Source;
        s.Position = new float3(at.X, at.Y, at.Z);
        s.RolloffFactor = 0f;
        s.Gain = Math.Clamp(gain * Mix.Gain(MixBus.Media), 0f, 1f);
    }

    /// <summary>A game track from a place in it (null: nothing). The same track and seed again is ignored, so this can be said on every scan.</summary>
    public void MediaTrack(MusicTrack? track, uint seed, double seconds, bool paused)
    {
        Volatile.Write(ref _mediaTrackPaused, paused ? 1 : 0);
        if (track == null)
        {
            if (_mediaWant != null)
            {
                Volatile.Write(ref _mediaWant, null);
                MediaCount(-1, _media[MediaTrackVoice]);
            }
            return;
        }
        if (_mediaWant is { } w && ReferenceEquals(w.Track, track) && w.Seed == seed)
            return;
        if (!MediaStart())
            return;
        if (_mediaWant == null)
            MediaCount(1, _media[MediaTrackVoice]);
        Volatile.Write(ref _mediaWant, new MediaTrackPlay(track, seed, track.BarAt(seconds)));
    }

    private void MediaCount(int by, MediaVoice? v)
    {
        _mediaLive += by;
        if (by > 0 && v != null)
        {
            v.Source.SourceRelative = false;
            v.Source.Gain = 0;
        }
    }

    /// <summary>Where a voice is this frame, and how loud: <paramref name="gain"/> is the part's level times its fade at the end of its range. Distance is OpenAL's, as for any sound.</summary>
    public void MediaPlace(int voice, Vector3 at, float gain, float near, float hear)
    {
        if (!Enabled || _media[voice] is not { } v)
            return;
        if (_mediaThread == null)
            MediaStart(); // (it stopped itself in the moment this voice started)
        var s = v.Source;
        s.Position = new float3(at.X, at.Y, at.Z);
        s.ReferenceDistance = near;
        s.RolloffFactor = 1f;
        s.MaxDistance = hear;
        s.Gain = Math.Clamp(gain * Mix.Gain(MixBus.Media), 0f, 1f);
    }

    /// <summary>
    /// The streams. A speaker has no stream or buffers of its own: its source queues the very buffers its player's does, and they come off and go on together, so one decode
    /// reaches every source in the group and nothing can drift, since one mixer reads them all. A speaker joining takes the player's queue and its sample position in one deferred update, so it starts on the same sample.
    /// </summary>
    private void MediaLoop()
    {
        try
        {
            for (var i = 0; i < MediaSpeakerVoice; i++)
            {
                var v = _media[i];
                foreach (var b in v.Buffers)
                    MediaFill(v, b);
                v.Source.QueueBuffers(v.Buffers);
                v.Buffers.CopyTo(v.Queue, 0);
                v.Queued = v.Buffers.Length;
                _mediaGroup[i] = v.Source;
            }
            Al.PlaySources(_mediaGroup.AsSpan(0, MediaSpeakerVoice));
            var idle = 0;
            var turn = 0;
            while (_mediaRun)
            {
                for (var i = 0; i < MediaSpeakerVoice; i++)
                    MediaPump(i);
                if (++turn % 20 == 0)
                    MediaCheckStep();
                MediaLog();
                idle = Volatile.Read(ref _mediaLive) > 0 ? 0 : idle + 1;
                if (idle > 500)
                    break; // (nothing has played for five seconds: no thread until something does)
                Thread.Sleep(10);
            }
            for (var i = MediaSpeakerVoice; i < _media.Length; i++)
                MediaLeave(_media[i]);
            for (var i = 0; i < MediaSpeakerVoice; i++)
            {
                var v = _media[i];
                v.Source.Stop();
                var left = v.Source.BuffersProcessed;
                while (left-- > 0)
                    v.Source.UnqueueBuffers(_mediaOne);
                v.Queued = 0;
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"[media] sound stopped: {e.Message}");
        }
        Console.WriteLine("[media] sound thread stopped");
        _mediaRun = false;
        _mediaThread = null;
    }

    /// <summary>One player's group: speakers come and go, spent buffers are made again and put back on every source together.</summary>
    private void MediaPump(int lead)
    {
        var l = _media[lead];
        for (var i = MediaSpeakerVoice; i < _media.Length; i++)
        {
            var s = _media[i];
            if (s.Joined == lead && s.Follows != lead)
                MediaLeave(s);
        }
        var spent = l.Source.BuffersProcessed;
        for (var i = MediaSpeakerVoice; i < _media.Length; i++)
            if (_media[i].Joined == lead)
                spent = Math.Min(spent, _media[i].Source.BuffersProcessed);
        while (spent-- > 0)
        {
            l.Source.UnqueueBuffers(_mediaOne);
            var b = _mediaOne[0];
            for (var i = MediaSpeakerVoice; i < _media.Length; i++)
                if (_media[i].Joined == lead)
                    _media[i].Source.UnqueueBuffers(_mediaOne);
            _mediaOne[0] = b;
            MediaFill(l, b);
            l.Source.QueueBuffers(_mediaOne);
            for (var i = MediaSpeakerVoice; i < _media.Length; i++)
                if (_media[i].Joined == lead)
                    _media[i].Source.QueueBuffers(_mediaOne);
            Array.Copy(l.Queue, 1, l.Queue, 0, MediaBuffers - 1);
            l.Queue[MediaBuffers - 1] = b;
        }
        if (l.Source.State != AlSourceState.Playing)
        {
            // (after an underrun: the player goes on, and its speakers join it again rather than each restarting by itself)
            l.Source.Play();
            for (var i = MediaSpeakerVoice; i < _media.Length; i++)
                if (_media[i].Joined == lead)
                    MediaLeave(_media[i]);
        }
        for (var i = MediaSpeakerVoice; i < _media.Length; i++)
        {
            var s = _media[i];
            if (s.Follows == lead && s.Joined != lead)
                MediaJoin(s, l, lead);
        }
    }

    private void MediaJoin(MediaVoice s, MediaVoice l, int lead)
    {
        s.Source.Stop();
        s.Source.Buffer = default;
        s.Source.QueueBuffers(l.Queue.AsSpan(0, l.Queued));
        MediaDefer(true);
        s.Source.SampleOffset = l.Source.SampleOffset;
        s.Source.Play();
        MediaDefer(false);
        s.Joined = lead;
        s.Off = 0;
    }

    private static void MediaLeave(MediaVoice s)
    {
        if (s.Joined < 0)
            return;
        s.Source.Stop();
        s.Source.Buffer = default;
        s.Joined = -1;
    }

    /// <summary>Changes between these two calls reach the mixer together. A device without the extension gets them one at a time, a few samples apart at worst, and the step check still holds it.</summary>
    private void MediaDefer(bool on)
    {
        if (_mediaNoDefer)
            return;
        try
        {
            if (on)
                Al.PInvoke.DeferUpdatesSOFT();
            else
                Al.PInvoke.ProcessUpdatesSOFT();
        }
        catch (Exception e) when (e is EntryPointNotFoundException or InvalidOperationException or NotSupportedException)
        {
            _mediaNoDefer = true;
            Console.WriteLine("[media] no deferred updates: speakers join one call apart");
        }
    }

    /// <summary>Measures each speaker against its player, and snaps one that a device stall really moved.</summary>
    private void MediaCheckStep()
    {
        for (var i = MediaSpeakerVoice; i < _media.Length; i++)
        {
            var s = _media[i];
            if (s.Joined < 0 || s.Source.State != AlSourceState.Playing)
                continue;
            var l = _media[s.Joined];
            if (s.Source.BuffersProcessed != l.Source.BuffersProcessed)
                continue; // (one reading fell across a buffer's end: it says nothing)
            var off = Math.Abs(s.Source.SampleOffset - l.Source.SampleOffset);
            l.Worst = Math.Max(l.Worst, off);
            s.Off = off > MediaSnapFrames ? s.Off + 1 : 0;
            if (s.Off < 2)
                continue;
            MediaDefer(true);
            s.Source.SampleOffset = l.Source.SampleOffset;
            MediaDefer(false);
            s.Off = 0;
            Console.WriteLine($"[media] speaker voice {i - MediaSpeakerVoice} snapped back to voice {s.Joined}, {off} frames off");
        }
    }

    /// <summary>Hop 5 and 6 on a throttle: what came out of the ring, and what OpenAL is doing with it.</summary>
    private void MediaLog()
    {
        var now = Environment.TickCount64;
        if (now < _mediaNextLog)
            return;
        _mediaNextLog = now + MediaLogEvery;
        for (var i = 0; i < _media.Length; i++)
        {
            var v = _media[i];
            var block = v.Block;
            if (block != null || i == MediaTrackVoice && Volatile.Read(ref _mediaWant) != null)
            {
                var s = v.Source;
                var ring = block != null ? $"ring written {block.AudioWritten} read {block.AudioRead}, " : "game track, ";
                Console.WriteLine($"[media] sound voice {i} (hop 5): {ring}{v.Got} frames of sound in {v.Fills} buffers, peak {v.Peak:0.000}; "
                    + $"OpenAL (hop 6): {s.State}, gain {s.Gain:0.000}, at {s.Position.X:0.0} {s.Position.Y:0.0} {s.Position.Z:0.0}, listener {_listener.X:0.0} {_listener.Y:0.0} {_listener.Z:0.0}, near {s.ReferenceDistance:0.0} far {s.MaxDistance:0.0}, master {ListenerGain:0.00}");
            }
            if (i < MediaSpeakerVoice)
            {
                var speakers = 0;
                for (var k = MediaSpeakerVoice; k < _media.Length; k++)
                    if (_media[k].Joined == i)
                        speakers++;
                if (speakers > 0)
                    Console.WriteLine($"[media] voice {i}: {speakers} speakers on its buffers, worst offset {v.Worst} frames ({v.Worst * 1000f / SampleRate:0.0} ms)");
                v.Worst = 0;
            }
            v.Got = v.Fills = 0;
            v.Peak = 0;
        }
    }

    private void MediaFill(MediaVoice v, AlBuffer buffer)
    {
        var mono = v.Mono.AsSpan();
        if (ReferenceEquals(v, _media[MediaTrackVoice]))
            MediaRender(mono);
        else if (v.Block is { } block)
        {
            var got = block.ReadAudio(mono);
            v.Got += got;
            mono[got..].Clear();
        }
        else
            mono.Clear();
        v.Fills++;
        for (var i = 0; i < mono.Length; i++)
        {
            v.Peak = MathF.Max(v.Peak, MathF.Abs(mono[i]));
            v.Pcm[i] = (short)(Math.Clamp(mono[i], -1f, 1f) * 32767f);
        }
        buffer.SetData(AlFormat.Mono16, MemoryMarshal.AsBytes(v.Pcm.AsSpan()), SampleRate);
    }

    /// <summary>The game's track, mixed to one channel. Renders nothing while it is paused, wanted by nobody or holding for the music thread.</summary>
    private void MediaRender(Span<float> mono)
    {
        var want = Volatile.Read(ref _mediaWant);
        var held = want != null && (Music.ActiveDecks > 0 || Music.Frenzy.Active);
        Volatile.Write(ref _mediaTrackHeld, held ? 1 : 0);
        if (want == null || held || Volatile.Read(ref _mediaTrackPaused) != 0)
        {
            if (want == null)
                _mediaHas = null;
            mono.Clear();
            return;
        }
        var r = _mediaRenderer ??= new MusicRenderer();
        if (!ReferenceEquals(want, _mediaHas))
        {
            r.Load(want.Track, want.Seed, want.Bar);
            _mediaHas = want;
        }
        var at = 0;
        while (at < mono.Length)
        {
            var n = Math.Min(mono.Length - at, MusicPlayer.BlockFrames);
            var stereo = _mediaStereo.AsSpan(0, n * 2);
            r.Render(stereo);
            for (var i = 0; i < n; i++)
                mono[at + i] = (stereo[i * 2] + stereo[i * 2 + 1]) * 0.5f;
            at += n;
        }
    }

    private void DisposeMedia()
    {
        _mediaRun = false;
        _mediaThread?.Join(500);
        foreach (var v in _media)
            if (v != null)
            {
                v.Source.Stop();
                Al.DeleteSources([v.Source]);
                if (v.Buffers.Length > 0)
                    Al.DeleteBuffers(v.Buffers);
            }
    }
}
