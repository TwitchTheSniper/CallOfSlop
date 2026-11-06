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

    private const int MediaVoiceCount = MediaDirector.MaxPages + 1;
    private const int MediaBuffers = 4, MediaFrames = 2048; // 4 x 46 ms queued, as the music's stream

    private sealed class MediaVoice
    {
        public AlSource Source;
        public AlBuffer[] Buffers = [];
        public readonly short[] Pcm = new short[MediaFrames];
        public readonly float[] Mono = new float[MediaFrames];
        public volatile MediaBlock? Block;
    }

    private sealed record MediaTrackPlay(MusicTrack Track, uint Seed, int Bar);

    private readonly MediaVoice[] _media = new MediaVoice[MediaVoiceCount];
    private readonly AlBuffer[] _mediaOne = new AlBuffer[1];
    private readonly float[] _mediaStereo = new float[MusicPlayer.BlockFrames * 2];
    private Thread? _mediaThread;
    private volatile bool _mediaRun;
    private int _mediaLive;
    private MusicRenderer? _mediaRenderer; // (the thread's)
    private MediaTrackPlay? _mediaWant, _mediaHas;
    private int _mediaTrackPaused, _mediaTrackHeld;

    /// <summary>The game's track holds for the music thread: for the panel.</summary>
    public bool MediaTrackHeld => Volatile.Read(ref _mediaTrackHeld) != 0;

    private bool MediaStart()
    {
        if (!Enabled)
            return false;
        if (_mediaThread != null)
            return true;
        try
        {
            for (var i = 0; i < _media.Length; i++)
            {
                if (_media[i] != null)
                    continue;
                var v = new MediaVoice { Source = Al.GenSource(), Buffers = Al.GenBuffers(MediaBuffers) };
                _media[i] = v;
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"[media] no sound: {e.Message}");
            return false;
        }
        _mediaRun = true;
        _mediaThread = new Thread(MediaLoop) { IsBackground = true, Name = "Media sound", Priority = ThreadPriority.AboveNormal };
        _mediaThread.Start();
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
        var s = v.Source;
        s.Position = new float3(at.X, at.Y, at.Z);
        s.ReferenceDistance = near;
        s.RolloffFactor = 1f;
        s.MaxDistance = hear;
        s.Gain = Math.Clamp(gain * Mix.Gain(MixBus.Media), 0f, 1f);
    }

    private void MediaLoop()
    {
        try
        {
            foreach (var v in _media)
            {
                foreach (var b in v.Buffers)
                {
                    MediaFill(v, b);
                }
                v.Source.QueueBuffers(v.Buffers);
                v.Source.Play();
            }
            var idle = 0;
            while (_mediaRun)
            {
                foreach (var v in _media)
                {
                    var processed = v.Source.BuffersProcessed;
                    while (processed-- > 0)
                    {
                        v.Source.UnqueueBuffers(_mediaOne);
                        MediaFill(v, _mediaOne[0]);
                        v.Source.QueueBuffers(_mediaOne);
                    }
                    if (v.Source.State != AlSourceState.Playing)
                        v.Source.Play(); // (after an underrun)
                }
                idle = Volatile.Read(ref _mediaLive) > 0 ? 0 : idle + 1;
                if (idle > 500)
                    break; // (nothing has played for five seconds: no thread until something does)
                Thread.Sleep(10);
            }
            foreach (var v in _media)
            {
                v.Source.Stop();
                var left = v.Source.BuffersProcessed;
                while (left-- > 0)
                    v.Source.UnqueueBuffers(_mediaOne);
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"[media] sound stopped: {e.Message}");
        }
        _mediaRun = false;
        _mediaThread = null;
    }

    private void MediaFill(MediaVoice v, AlBuffer buffer)
    {
        var mono = v.Mono.AsSpan();
        if (ReferenceEquals(v, _media[MediaTrackVoice]))
            MediaRender(mono);
        else if (v.Block is { } block)
            mono[block.ReadAudio(mono)..].Clear();
        else
            mono.Clear();
        for (var i = 0; i < mono.Length; i++)
            v.Pcm[i] = (short)(Math.Clamp(mono[i], -1f, 1f) * 32767f);
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
                Al.DeleteBuffers(v.Buffers);
            }
    }
}
