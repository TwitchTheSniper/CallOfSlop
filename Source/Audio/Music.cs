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
        public float Trim = 1f; // the mixer's menu/lobby level, as applied on the last block
        public bool Active;
        public MusicSet Set;
    }

    private readonly Deck[] _decks = [new(), new()];
    private readonly MusicPlaylist _menu, _lobby, _menuHardbass, _lobbyHardbass;
    private Deck? _lead; // the deck the current track plays on (null while fading to silence)
    private int _mood = (int)MusicMood.Off;
    private int _appliedMood = -1;
    private int _hardbassOnly, _appliedHardbassOnly;
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
        _menuHardbass = new MusicPlaylist(MusicTracks.For(MusicSet.Menu, hardbassOnly: true), _seed * 17u + 3u);
        _lobbyHardbass = new MusicPlaylist(MusicTracks.For(MusicSet.Lobby, hardbassOnly: true), _seed * 13u + 5u);
        Frenzy = new FrenzyLayer(_seed * 29u + 1u);
        _frenzyLimiter.Configure();
    }

    /// <summary>The in-match kill-frenzy layer (MusicFrenzy.cs); it only plays while the mood is Off (a live match).</summary>
    public readonly FrenzyLayer Frenzy;

    // The frenzy's levels 5 and 6 push the bus past full scale at loud settings: a limiter after the music volume, on
    // while the frenzy plays (the menu and lobby music never reach it and stay bit-for-bit as before).
    private readonly MasterLimiter _frenzyLimiter = new() { Ceiling = FrenzyLayer.LimiterCeiling };
    private bool _limiting;

    /// <summary>The lowest gain the frenzy limiter has applied since the frenzy started (1 = never limited).</summary>
    public float FrenzyLimiterMinGain => _frenzyLimiter.MinGain;

    /// <summary>
    /// Settings > Audio > Music style: true plays only the hardbass tracks. Switching it on while another style plays
    /// crossfades straight into a hardbass track; switching it off lets the current track finish.
    /// </summary>
    public bool HardbassOnly
    {
        get => Volatile.Read(ref _hardbassOnly) != 0;
        set => Volatile.Write(ref _hardbassOnly, value ? 1 : 0);
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
        _meter = MeterEnabled; // the mixer window is open (Audio.Mixer.cs)
        Frenzy.Metering = _meter;
        if (_meter)
            MeterBegin();
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
            // The mixer's level for this deck's set, ramped over the block (1 and 1 with the built-in mix: unchanged).
            var t0 = deck.Trim;
            var t1 = deck.Set == MusicSet.Menu ? MenuTrim : LobbyTrim;
            deck.Trim = t1;
            var s0 = g0 * t0;
            var s1 = g1 * t1;
            var dg = (s1 - s0) / frames;
            var g = s0;
            for (var i = 0; i < output.Length; i += 2)
            {
                output[i] += buf[i] * g;
                output[i + 1] += buf[i + 1] * g;
                g += dg;
            }
            if (_meter)
                MeterDeck(buf, MathF.Max(s0, s1), 0.5f * (s0 + s1), deck.Set == MusicSet.Menu);
            deck.Gain = g1;
            if ((g1 <= 0f && deck.Target <= 0f) || deck.R.Finished)
            {
                deck.Active = false;
                deck.Gain = 0;
                if (_lead == deck)
                    _lead = null;
            }
        }

        Frenzy.MixInto(output, Volatile.Read(ref _mood) == (int)MusicMood.Off || Frenzy.Preview); // the kill-frenzy layer (MusicFrenzy.cs); the mixer can preview it anywhere

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
        if (Frenzy.Active)
        {
            if (!_limiting)
            {
                _frenzyLimiter.Reset(); // starts from silence: the frenzy fades in from 0
                _limiting = true;
            }
            _frenzyLimiter.Process(output);
        }
        else
        {
            _limiting = false;
        }
        if (_meter)
            MeterOutput(output);
        var name = _lead is { Active: true } l ? l.R.Track.Name : null;
        if (!ReferenceEquals(name, _nowPlaying))
            Volatile.Write(ref _nowPlaying, name);
    }

    private static float Approach(float from, float to, float maxStep) =>
        from < to ? MathF.Min(to, from + maxStep) : MathF.Max(to, from - maxStep);

    // ---- The audio mixer (Audio.Mixer.cs): levels and meters -----------------------------------

    private float _menuTrim = 1f, _lobbyTrim = 1f;
    private bool _meter;
    private float _mMenuPeak, _mMenuMs, _mLobbyPeak, _mLobbyMs, _mOutPeak, _mOutMs;

    /// <summary>The mixer's menu-music level (linear; 1 = as tuned).</summary>
    public float MenuTrim
    {
        get => Volatile.Read(ref _menuTrim);
        set => Volatile.Write(ref _menuTrim, float.IsFinite(value) ? Math.Clamp(value, 0f, 4f) : 1f);
    }

    /// <summary>The mixer's lobby-music level (linear; 1 = as tuned).</summary>
    public float LobbyTrim
    {
        get => Volatile.Read(ref _lobbyTrim);
        set => Volatile.Write(ref _lobbyTrim, float.IsFinite(value) ? Math.Clamp(value, 0f, 4f) : 1f);
    }

    /// <summary>The frenzy limiter's ceiling (the mixer's "Music limiter ceiling"; default <see cref="FrenzyLayer.LimiterCeiling"/>).</summary>
    public float LimiterCeiling
    {
        get => _frenzyLimiter.Ceiling;
        set => _frenzyLimiter.Ceiling = float.IsFinite(value) ? Math.Clamp(value, 0.1f, MasterLimiter.SafetyCeiling) : FrenzyLayer.LimiterCeiling;
    }

    /// <summary>The mixer window is open: measure the decks, the frenzy and the output (else nothing extra is done).</summary>
    public volatile bool MeterEnabled;

    private void MeterDeck(Span<float> buf, float peakGain, float meanGain, bool menu)
    {
        float pk = 0;
        double sum = 0;
        foreach (var x in buf)
        {
            pk = MathF.Max(pk, MathF.Abs(x));
            sum += x * (double)x;
        }
        var ms = (float)(sum / Math.Max(1, buf.Length)) * meanGain * meanGain;
        pk *= peakGain;
        if (menu)
        {
            _mMenuPeak = MathF.Max(_mMenuPeak, pk);
            _mMenuMs += ms;
        }
        else
        {
            _mLobbyPeak = MathF.Max(_mLobbyPeak, pk);
            _mLobbyMs += ms;
        }
    }

    /// <summary>Starts a block's readings (the last block's stay readable until this block's are done).</summary>
    private void MeterBegin()
    {
        _mMenuPeak = _mMenuMs = _mLobbyPeak = _mLobbyMs = 0;
    }

    /// <summary>Publishes a block's readings: peaks fall back slowly (about 70% a block), so the UI sees every transient.</summary>
    private void MeterPublish()
    {
        _pMenuPeak = MathF.Max(_mMenuPeak, _pMenuPeak * 0.7f);
        _pLobbyPeak = MathF.Max(_mLobbyPeak, _pLobbyPeak * 0.7f);
        _pMenuMs = _mMenuMs;
        _pLobbyMs = _mLobbyMs;
    }

    private float _pMenuPeak, _pMenuMs, _pLobbyPeak, _pLobbyMs;

    private void MeterOutput(Span<float> output)
    {
        float pk = 0;
        double sum = 0;
        foreach (var x in output)
        {
            pk = MathF.Max(pk, MathF.Abs(x));
            sum += x * (double)x;
        }
        _mOutPeak = MathF.Max(pk, _mOutPeak * 0.7f);
        _mOutMs = (float)(sum / Math.Max(1, output.Length));
        MeterPublish();
    }

    /// <summary>
    /// The music's latest levels (before the music volume, except the output which is after it and the limiter):
    /// slowly falling peaks and the latest block's mean squares. Read on the UI thread; benign races (floats).
    /// </summary>
    public (float MenuPeak, float MenuMs, float LobbyPeak, float LobbyMs, float FrenzyPeak, float FrenzyMs, float OutPeak, float OutMs) TakeMeters() =>
        (_pMenuPeak, _pMenuMs, _pLobbyPeak, _pLobbyMs, Frenzy.MeterPeak, Frenzy.MeterMs, _mOutPeak, _mOutMs);

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
        var hardbassOnly = Volatile.Read(ref _hardbassOnly);
        if (hardbassOnly != _appliedHardbassOnly)
        {
            _appliedHardbassOnly = hardbassOnly;
            if (hardbassOnly != 0 && _lead is { Active: true } now && now.Target > 0 && now.R.Track.Style != MusicStyle.Hardbass)
                StartNext(now.Set);
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

        var track = (_appliedHardbassOnly != 0
            ? set == MusicSet.Menu ? _menuHardbass : _lobbyHardbass
            : set == MusicSet.Menu ? _menu : _lobby).Next();
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
