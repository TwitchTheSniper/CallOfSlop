using System.Diagnostics;
using System.Runtime.InteropServices;
using Brutal.Numerics;
using Brutal.OpenAlApi;

namespace CallOfSlop;

/// <summary>What the music should be doing.</summary>
public enum MusicMood
{
    Off, // live gameplay: silence
    Menu, // main menu
    Lobby, // in a session between matches: warmup, ready-up, countdown, results, map vote
}

/// <summary>A shuffled playlist: every track once per round, in a new order each round, never the same track twice in a row.</summary>
public sealed class MusicPlaylist
{
    private readonly MusicTrack[] _tracks;
    private readonly int[] _order;
    private int _pos;
    private MusicRng _rng;
    private MusicTrack? _last;

    public MusicPlaylist(IReadOnlyList<MusicTrack> tracks, uint seed)
    {
        _tracks = tracks.ToArray();
        _order = new int[_tracks.Length];
        for (var i = 0; i < _order.Length; i++)
            _order[i] = i;
        _pos = _order.Length;
        _rng = new MusicRng(seed);
    }

    public int Count => _tracks.Length;

    public MusicTrack Next()
    {
        if (_tracks.Length == 0)
            return MusicTracks.All[0];
        if (_pos >= _order.Length)
        {
            for (var i = _order.Length - 1; i > 0; i--)
            {
                var j = _rng.Range(i + 1);
                (_order[i], _order[j]) = (_order[j], _order[i]);
            }
            if (_order.Length > 1 && _tracks[_order[0]] == _last)
            {
                var j = 1 + _rng.Range(_order.Length - 1);
                (_order[0], _order[j]) = (_order[j], _order[0]);
            }
            _pos = 0;
        }
        _last = _tracks[_order[_pos++]];
        return _last;
    }
}

/// <summary>
/// Background music for the menus and lobbies. Two decks of <see cref="MusicRenderer"/> synthesise the tracks in
/// real time and crossfade between them: when a track ends, when the menu turns into a lobby (or back), and a fade
/// to silence when a match goes live. The mix is streamed to one OpenAL source through a small queue of buffers,
/// refilled by a background thread, so there's never a big buffer and the game thread does nothing but say which
/// mood it wants. The music volume is applied here; the master volume applies through the OpenAL listener, as for
/// every other sound. <see cref="Mix"/> also runs headless, for the self-test.
/// </summary>
public sealed class MusicPlayer : IDisposable
{
    private const int Sr = Audio.SampleRate;
    public const int BlockFrames = 1024;
    private const int StreamBuffers = 4, StreamFrames = 2048; // 4 x 46 ms queued: ~0.19 s of latency for fades and volume
    public const float CrossfadeSeconds = 3f, FadeInSeconds = 1.2f, FadeOutSeconds = 1.5f;

    private sealed class Deck
    {
        public readonly MusicRenderer R = new();
        public readonly float[] Buf = new float[BlockFrames * 2];
        public float Gain, Target, Step;
        public bool Active;
        public MusicSet Set;
    }

    private readonly Deck[] _decks = [new(), new()];
    private readonly MusicPlaylist _menu, _lobby;
    private Deck? _lead; // the deck the current track plays on (null while fading to silence)
    private int _mood = (int)MusicMood.Off;
    private int _appliedMood = -1;
    private float _volume = 0.5f, _appliedGain = -1f;
    private uint _seed;
    private string? _nowPlaying;
    private long _renderTicks, _renderedFrames;
    private bool _log;

    public MusicPlayer(uint seed = 0)
    {
        _seed = seed != 0 ? seed : (uint)Environment.TickCount | 1u;
        _menu = new MusicPlaylist(MusicTracks.For(MusicSet.Menu), _seed);
        _lobby = new MusicPlaylist(MusicTracks.For(MusicSet.Lobby), _seed * 31u + 7u);
    }

    /// <summary>Music volume 0..1 (Settings.MusicVolume); a perceptual curve is applied when mixing.</summary>
    public float Volume
    {
        get => Volatile.Read(ref _volume);
        set => Volatile.Write(ref _volume, Math.Clamp(value, 0f, 1f));
    }

    public MusicMood Mood => (MusicMood)Volatile.Read(ref _mood);

    /// <summary>Called every frame by the game; changes take effect (with fades) on the next mixed block.</summary>
    public void SetMood(MusicMood mood) => Volatile.Write(ref _mood, (int)mood);

    /// <summary>Name of the track playing (or fading in), null when the music is off.</summary>
    public string? NowPlaying => Volatile.Read(ref _nowPlaying);

    /// <summary>The track on the leading deck (null when silent). Mixing-thread state: for the self-test.</summary>
    public MusicTrack? Current => _lead is { Active: true } d ? d.R.Track : null;

    /// <summary>Number of decks currently producing sound (2 during a crossfade).</summary>
    public int ActiveDecks => (_decks[0].Active ? 1 : 0) + (_decks[1].Active ? 1 : 0);

    /// <summary>Average mixing cost in milliseconds per second of audio.</summary>
    public double RenderMsPerSecond => _renderedFrames == 0 ? 0 : _renderTicks * 1000.0 / Stopwatch.Frequency / (_renderedFrames / (double)Sr) ;

    /// <summary>The game's state machine for music: menu music outside a session, lobby music around matches, silence while one is live.</summary>
    public static MusicMood MoodFor(bool inSession, MatchPhase phase) =>
        !inSession ? MusicMood.Menu : phase == MatchPhase.Playing ? MusicMood.Off : MusicMood.Lobby;

    // ========================================================================================
    // Mixing (the streaming thread, or the self-test; not re-entrant)
    // ========================================================================================

    /// <summary>Mixes the next frames of music into an interleaved stereo buffer (overwrites it). Allocation-free.</summary>
    public void Mix(Span<float> stereo)
    {
        var t0 = Stopwatch.GetTimestamp();
        var frames = stereo.Length / 2;
        while (stereo.Length >= 2)
        {
            var n = Math.Min(stereo.Length & ~1, BlockFrames * 2);
            MixBlock(stereo[..n]);
            stereo = stereo[n..];
        }
        _renderTicks += Stopwatch.GetTimestamp() - t0;
        _renderedFrames += frames;
    }

    private void MixBlock(Span<float> output)
    {
        UpdateDirector();
        output.Clear();
        var frames = output.Length / 2;
        for (var d = 0; d < _decks.Length; d++)
        {
            var deck = _decks[d];
            if (!deck.Active)
                continue;
            var buf = deck.Buf.AsSpan(0, output.Length);
            deck.R.Render(buf);
            var g0 = deck.Gain;
            var g1 = Approach(g0, deck.Target, deck.Step * frames);
            var dg = (g1 - g0) / frames;
            var g = g0;
            for (var i = 0; i < output.Length; i += 2)
            {
                output[i] += buf[i] * g;
                output[i + 1] += buf[i + 1] * g;
                g += dg;
            }
            deck.Gain = g1;
            if ((g1 <= 0f && deck.Target <= 0f) || deck.R.Finished)
            {
                deck.Active = false;
                deck.Gain = 0;
                if (_lead == deck)
                    _lead = null;
            }
        }

        // Music volume with a perceptual (squared) curve, ramped per block so slider moves don't click.
        var v = Volume;
        var target = v * v;
        var a0 = _appliedGain < 0 ? target : _appliedGain;
        var da = (target - a0) / frames;
        var a = a0;
        for (var i = 0; i < output.Length; i += 2)
        {
            output[i] *= a;
            output[i + 1] *= a;
            a += da;
        }
        _appliedGain = target;
        var name = _lead is { Active: true } l ? l.R.Track.Name : null;
        if (!ReferenceEquals(name, _nowPlaying))
            Volatile.Write(ref _nowPlaying, name);
    }

    private static float Approach(float from, float to, float maxStep) =>
        from < to ? MathF.Min(to, from + maxStep) : MathF.Max(to, from - maxStep);

    private void UpdateDirector()
    {
        var mood = Volatile.Read(ref _mood);
        if (mood != _appliedMood)
        {
            _appliedMood = mood;
            if (mood == (int)MusicMood.Off)
            {
                foreach (var d in _decks)
                    FadeOut(d, FadeOutSeconds);
                _lead = null;
            }
            else
            {
                var set = mood == (int)MusicMood.Menu ? MusicSet.Menu : MusicSet.Lobby;
                if (_lead is not { Active: true } l || l.Set != set || l.Target <= 0)
                    StartNext(set);
            }
        }
        // Near the end of a track, crossfade into the next one of the same playlist.
        if (_lead is { Active: true } cur && cur.Target > 0 && cur.R.Remaining < CrossfadeSeconds)
            StartNext(cur.Set);
    }

    private static void FadeOut(Deck d, float seconds)
    {
        if (!d.Active)
            return;
        d.Target = 0;
        d.Step = 1f / (seconds * Sr);
    }

    private void StartNext(MusicSet set)
    {
        var audible = _decks[0].Active || _decks[1].Active;
        var fade = audible ? CrossfadeSeconds : FadeInSeconds;
        // Use an idle deck, else the quieter one that isn't leading.
        Deck free;
        if (!_decks[0].Active && _decks[0] != _lead)
            free = _decks[0];
        else if (!_decks[1].Active && _decks[1] != _lead)
            free = _decks[1];
        else if (_lead == _decks[0])
            free = _decks[1];
        else if (_lead == _decks[1])
            free = _decks[0];
        else
            free = _decks[0].Gain <= _decks[1].Gain ? _decks[0] : _decks[1];
        foreach (var d in _decks)
            if (d != free)
                FadeOut(d, fade);

        var track = (set == MusicSet.Menu ? _menu : _lobby).Next();
        _seed = _seed * 1664525u + 1013904223u;
        free.R.Load(track, _seed);
        free.Set = set;
        free.Active = true;
        free.Gain = 0;
        free.Target = 1;
        free.Step = 1f / (fade * Sr);
        _lead = free;
        if (_log)
            Console.WriteLine($"[music] now playing \"{track.Name}\" ({set}, {track.Style}, {track.Bpm:0} BPM, {track.Key}, {track.Seconds:0} s); "
                              + $"mixing costs {RenderMsPerSecond:0.0} ms per second of audio ({RenderMsPerSecond / 10:0.00}% of a core)");
    }

    // ========================================================================================
    // Streaming to OpenAL
    // ========================================================================================

    private Thread? _thread;
    private volatile bool _running;
    private AlSource _source;
    private AlBuffer[] _buffers = [];
    private readonly AlBuffer[] _one = new AlBuffer[1];
    private readonly float[] _mix = new float[StreamFrames * 2];
    private readonly short[] _pcm = new short[StreamFrames * 2];

    /// <summary>True while the streaming thread runs.</summary>
    public bool Streaming => _running;

    /// <summary>Starts streaming on a background thread. The OpenAL context must be current.</summary>
    public void Start()
    {
        _log = true;
        _source = Al.GenSource();
        _source.SourceRelative = true; // stereo music isn't positioned; it follows the listener
        _source.Position = new float3(0, 0, 0);
        _source.Gain = 1f;
        _buffers = Al.GenBuffers(StreamBuffers);
        foreach (var b in _buffers)
            Fill(b);
        _source.QueueBuffers(_buffers);
        _source.Play();
        _running = true;
        _thread = new Thread(StreamLoop) { IsBackground = true, Name = "Music", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
        Console.WriteLine($"Music: streaming {MusicTracks.All.Length} procedural tracks ({StreamBuffers} x {StreamFrames} frame buffers).");
    }

    private void StreamLoop()
    {
        try
        {
            while (_running)
            {
                var processed = _source.BuffersProcessed;
                while (processed-- > 0 && _running)
                {
                    _source.UnqueueBuffers(_one);
                    Fill(_one[0]);
                    _source.QueueBuffers(_one);
                }
                if (_running && _source.State != AlSourceState.Playing)
                    _source.Play(); // after an underrun (e.g. a long hitch)
                Thread.Sleep(10);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Music stopped: {ex.Message}");
            _running = false;
        }
    }

    private void Fill(AlBuffer buffer)
    {
        Mix(_mix);
        for (var i = 0; i < _mix.Length; i++)
            _pcm[i] = (short)(Math.Clamp(_mix[i], -1f, 1f) * 32767f);
        buffer.SetData(AlFormat.Stereo16, MemoryMarshal.AsBytes(_pcm.AsSpan()), Sr);
    }

    public void Dispose()
    {
        var wasStarted = _thread != null;
        _running = false;
        _thread?.Join(1000);
        _thread = null;
        if (!wasStarted)
            return;
        _source.Stop();
        Al.DeleteSources([_source]);
        Al.DeleteBuffers(_buffers);
        _buffers = [];
    }
}
