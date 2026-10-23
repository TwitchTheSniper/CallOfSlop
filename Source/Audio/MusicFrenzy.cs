namespace CallOfSlop;

/// <summary>
/// The local player's kill-frenzy meter for the music: the shared heat model (<see cref="FrenzyMeterCore"/>, in
/// Source/Game/Frenzy/FrenzyMeterCore.cs) under the name the music code and its self-tests use.
/// </summary>
public sealed class FrenzyMeter : FrenzyMeterCore
{
}

/// <summary>
/// The kill-frenzy music layer (mixed by <see cref="MusicPlayer"/> on the streaming thread). While a match is live and
/// the local player is on a roll, a hardbass track from the pool fades in from its drop, and its intensity follows the
/// <see cref="FrenzyMeter"/> level:
/// level 1 = drums and bass only, behind a closed low-pass (the club through the wall), quiet;
/// level 2 = the filter opens: the full beat with claps, hats and the bouncing bass;
/// level 3 = the leads, harmonies, pads and chants come in;
/// level 4 = full volume, and the track jumps to its last drop (the later lead sound, reverse bass, a crash and sub drop);
/// level 5 (FRENZY) = louder: as loud as the menu music (the frenzy normally sits under it), gunfire ducks it half as much;
/// level 6 (MAX FRENZY) = as loud as the gunfire itself (<see cref="Level6Gain"/>, calibrated against the game's own
/// gunshot and explosion sounds), no ducking at all, a crash and a crowd roar when it lands. The whole music bus then
/// goes through a limiter (in <see cref="MusicPlayer"/>, after the music volume) so it never clips.
/// Changes happen on the bar line and crossfade smoothly: up within half a bar, down one level at a time at most every
/// two bars, over two bars. Drops loop, so the energy never falls into a breakdown. Dying (or the setting going off, or
/// leaving the match) fades it out over <see cref="QuickFadeSeconds"/> with the filter closing; heat running out
/// fades it over two bars. One renderer, reused: allocation-free, about the cost of one hardbass track at level 3-6
/// and much less at levels 1-2 (the leads aren't rendered). Levels 5 and 6 only change gains (and the roar for a
/// moment), so they cost the same as level 4. Levels 5 and 6 also play faster: a higher BPM per weapon class
/// (MusicFrenzy.Tempo.cs), level 6 faster than level 5.
/// </summary>
public sealed partial class FrenzyLayer
{
    private const int Sr = Audio.SampleRate;

    /// <summary>The whole layer sits a bit under the menu music, so gunfire and footsteps stay on top (levels 1-4).</summary>
    public const float BusLevel = 0.75f;

    /// <summary>
    /// The loudness level 6 matches: the RMS (sample level, effects volume 1) of a second of your own gun firing at its
    /// rate (<see cref="Audio.Shot"/> plays it at <see cref="Audio.OwnShotGain"/>, 1) with a frag explosion (gain 1) on
    /// top, median over the real firearms; measured from the game's own synthesised sounds by
    /// <see cref="Audio.CombatReferenceRms"/> and checked against this constant by the self-test (with your own gun at 1,
    /// the gun mix: 0.461, gunfire alone 0.189, the explosion alone 0.424. With your gun at 0.75 and the snappy recorded
    /// gunshots: 0.440 / 0.119. Before the snappy bake: 0.48 / 0.22, the dry short bodies carry less energy per second at
    /// the same peaks; the all-synth gunfire measured 0.552 / 0.35).
    /// </summary>
    public const float SfxReferenceRms = 0.46f;

    /// <summary>The RMS of a frenzy track's drop as rendered (before any gain), the pool's median (0.47-0.54); checked by the self-test.</summary>
    public const float DropRms = 0.50f;

    /// <summary>
    /// Level 6 aims this much above the SFX reference before the limiter: the limiter (ceiling <see cref="LimiterCeiling"/>)
    /// shaves the kicks' peaks, so after it the drop comes out at about 1.05-1.1x the reference: as loud as everything else,
    /// if not slightly louder.
    /// </summary>
    public const float Level6Headroom = 1.2f;

    /// <summary>The music bus limiter's ceiling while the frenzy plays (its soft safety clipper never passes 0.985).</summary>
    public const float LimiterCeiling = 0.96f;

    /// <summary>The settings level 6 is calibrated at: Settings defaults (music volume 0.50, a squared curve; frenzy volume 0.80).</summary>
    public const float DefaultMusicVolume = 0.5f, DefaultVolume = 0.8f;

    /// <summary>
    /// The level-6 bus gain: with the sliders at their defaults the frenzy's drop comes out at <see cref="Level6Headroom"/>
    /// times <see cref="SfxReferenceRms"/>. The music and frenzy volume sliders still multiply it (and the limiter
    /// catches what a loud setting would clip).
    /// </summary>
    public static readonly float Level6Gain = SfxReferenceRms * Level6Headroom / (DropRms * BusLevel * DefaultVolume * DefaultMusicVolume * DefaultMusicVolume);

    /// <summary>Level 5 is as loud as the menu music: the bus level cancelled out.</summary>
    public const float Level5Gain = 1f / BusLevel;

    /// <summary>Per level: bus gain, low-pass cutoff (Hz), the melodic stem gain and the gunfire ducking depth.</summary>
    public static readonly float[] LevelGain = [0f, 0.5f, 0.72f, 0.86f, 1f, Level5Gain, Level6Gain],
        LevelCutoff = [250f, 380f, 9000f, 20000f, 20000f, 20000f, 20000f],
        LevelLead = [0f, 0f, 0f, 1f, 1f, 1f, 1f],
        LevelDuck = [0.3f, 0.3f, 0.3f, 0.3f, 0.3f, 0.15f, 0f];

    /// <summary>Death, setting off, leaving the match: the fade time.</summary>
    public const float QuickFadeSeconds = 2f;

    /// <summary>Gunfire ducking: depth at levels 1-4 (see <see cref="LevelDuck"/>) and release time.</summary>
    public const float DuckDepth = 0.3f, DuckRelease = 0.5f;

    /// <summary>The level-6 hit: a crowd roar (filtered noise, this long) with a crash on the bar line it lands on.</summary>
    public const float RoarSeconds = 1.8f, RoarLevel = 0.55f;

    private readonly MusicRenderer _r = new();
    private readonly float[] _buf = new float[MusicPlayer.BlockFrames * 2];
    private readonly MusicPlaylist _pool;
    private uint _seed;

    // Game thread -> mixing thread
    private int _target, _enabled = 1, _deathSeq, _shotSeq;
    private float _volume = 0.8f;

    // Mixing thread
    private int _seenDeath, _seenShot;
    private bool _active, _fading, _inLastDrop;
    private int _level, _barsAtLevel;
    private float _gain, _gainTo, _gainStep, _logCut, _logCutTo, _logCutStep, _lead, _leadTo, _leadStep, _duck, _duckDepth, _duckDepthTo, _duckDepthStep;
    private Svf _l1, _l2, _r1, _r2;
    private long _frames;
    private int _published;
    private string? _trackName;

    // The level-6 roar: two noise generators (left, right) through band-passes, with an envelope.
    private int _roarLeft;
    private uint _roarL = 12345u, _roarR = 67890u;
    private Svf _roarBL, _roarBR, _roarHL, _roarHR;

    public FrenzyLayer(uint seed = 1)
    {
        _seed = seed | 1u;
        _pool = new MusicPlaylist(MusicTracks.All.Where(t => t.Style == MusicStyle.Hardbass && (t.Timeline?.SteadyDrops ?? true)).ToArray(), _seed * 7919u + 11u);
        InitClasses(); // the per-weapon-class styles (MusicFrenzy.Classes.cs)
    }

    // ---- Game thread -------------------------------------------------------------------------

    /// <summary>The level the meter asks for (0-6); 0 while dead or outside a live match.</summary>
    public int Target
    {
        get => Volatile.Read(ref _target);
        set => Volatile.Write(ref _target, Math.Clamp(value, 0, FrenzyMeter.MaxLevel));
    }

    /// <summary>Settings > Audio > Kill frenzy music.</summary>
    public bool Enabled
    {
        get => Volatile.Read(ref _enabled) != 0;
        set => Volatile.Write(ref _enabled, value ? 1 : 0);
    }

    /// <summary>Settings > Audio > Frenzy volume (0..1, on top of the music volume).</summary>
    public float Volume
    {
        get => Volatile.Read(ref _volume);
        set => Volatile.Write(ref _volume, float.IsFinite(value) ? Math.Clamp(value, 0f, 1f) : 0.8f);
    }

    /// <summary>The local player died: fade out fast.</summary>
    public void Died() => Interlocked.Increment(ref _deathSeq);

    /// <summary>The local player fired: duck the layer a little so the gunfire stays on top (less at level 5, not at 6).</summary>
    public void Shot() => Interlocked.Increment(ref _shotSeq);

    /// <summary>The level playing now (0 = silent). Written by the mixing thread.</summary>
    public int Level => Volatile.Read(ref _published);

    /// <summary>The track the frenzy plays, null while silent.</summary>
    public string? TrackName => Volatile.Read(ref _trackName);

    // ---- Mixing thread (or the self-test) ------------------------------------------------------------

    /// <summary>True while the layer produces sound.</summary>
    public bool Active => _active;

    /// <summary>The renderer (for the self-test: bar grid and section).</summary>
    internal MusicRenderer Renderer => _r;

    /// <summary>Frames mixed since the layer last started (for the self-test's bar-grid check).</summary>
    public long FramesSinceStart => _frames;

    /// <summary>Frame (since the start) of the latest level change, and the level it went to.</summary>
    public long LastChangeFrame { get; private set; } = -1;

    /// <summary>Current bus gain (before volume and ducking).</summary>
    public float Gain => _gain;

    /// <summary>Current low-pass cutoff in Hz.</summary>
    public float Cutoff => MathF.Exp(_logCut);

    /// <summary>Current gunfire ducking depth (0.3 at levels 1-4, 0.15 at 5, 0 at 6).</summary>
    public float DuckingDepth => _duckDepth;

    /// <summary>Times the level-6 hit (crash and roar) has fired since the layer was made.</summary>
    public int Hits { get; private set; }

    /// <summary>
    /// Adds the layer into <paramref name="output"/> (interleaved stereo, at most one <see cref="MusicPlayer.BlockFrames"/>
    /// block). <paramref name="allowed"/> is false outside a live match. Allocation-free.
    /// </summary>
    public void MixInto(Span<float> output, bool allowed)
    {
        var frames = output.Length / 2;
        var enabled = allowed && Volatile.Read(ref _enabled) != 0;
        var target = enabled ? Volatile.Read(ref _target) : 0;
        var death = Volatile.Read(ref _deathSeq);
        if (death != _seenDeath)
        {
            _seenDeath = death;
            if (_active)
                QuickFade();
        }
        if (!enabled && _active && !_fading)
            QuickFade();
        var shot = Volatile.Read(ref _shotSeq);
        var shotNow = shot != _seenShot;
        _seenShot = shot;

        if (!_active)
        {
            if (target <= 0)
                return;
            Begin(target);
        }

        // Bar line in this block? Decide the level change first, so a jump lands exactly on it.
        var toBar = _fading ? int.MaxValue : _r.FramesToNextBar;
        if (toBar < frames)
        {
            var next = NextLevel(target);
            if (next >= 4 && _level < 4 && !_inLastDrop)
            {
                _r.JumpAtNextBar(MusicRenderer.LastDropBar(_r.Track));
                _inLastDrop = true;
            }
            Segment(output[..(toBar * 2)], toBar, shotNow);
            OnBar(next);
            Segment(output[(toBar * 2)..], frames - toBar, false);
        }
        else
        {
            Segment(output, frames, shotNow);
        }
        if (_active && _gain <= 0f && _gainTo <= 0f)
            Stop();
        Volatile.Write(ref _published, _active ? _level : 0);
    }

    private int NextLevel(int target)
    {
        if (target > _level)
            return target; // up straight away (on this bar line)
        if (target < _level && _barsAtLevel + 1 >= 2)
            return _level - 1; // down one level at a time, at most every two bars
        return _level;
    }

    private void Begin(int level)
    {
        var track = NextTrack(); // the class style's pool (MusicFrenzy.Classes.cs)
        _seed = _seed * 1664525u + 1013904223u;
        _r.JumpAtNextBar(-1);
        _inLastDrop = level >= 4;
        _r.Load(track, _seed, _inLastDrop ? MusicRenderer.LastDropBar(track) : track.FirstDropBar);
        _r.LoopDrops = true;
        BeginClass(level); // the class style first: it sets the tempo (MusicFrenzy.Classes.cs)
        BeginTempo(level); // levels 5 and 6 play faster (MusicFrenzy.Tempo.cs)
        _active = true;
        _fading = false;
        _frames = 0;
        _level = level;
        _barsAtLevel = 0;
        _duck = 0;
        _roarLeft = 0;
        _l1.Reset();
        _l2.Reset();
        _r1.Reset();
        _r2.Reset();
        _gain = 0;
        _logCut = MathF.Log(LevelCutoff[level]);
        _lead = LevelLead[level];
        _duckDepth = LevelDuck[level];
        SetTargets(level, (float)_r.FramesPerBar); // fade in over a bar
        LastChangeFrame = 0;
        Volatile.Write(ref _trackName, track.Name);
    }

    private void OnBar(int next)
    {
        ClassBar(next); // a class switch starts on the bar line (MusicFrenzy.Classes.cs)
        _barsAtLevel++;
        var up = next > _level;
        BarTempo(next, up); // levels 5 and 6 play faster (MusicFrenzy.Tempo.cs)
        if (next == _level)
            return;
        if (next == FrenzyMeter.MaxLevel && up)
            Hit();
        _level = next;
        _barsAtLevel = 0;
        LastChangeFrame = _frames;
        var bar = (float)_r.NextBarFrames; // the coming bar at its new tempo
        SetTargets(next, up ? bar * 0.5f : bar * 2f);
    }

    /// <summary>MAX FRENZY lands: a crash on this bar line and the crowd roars.</summary>
    private void Hit()
    {
        Hits++;
        _r.FrenzyCrash(0.7f);
        _roarLeft = (int)(RoarSeconds * Sr);
        _roarBL.Reset();
        _roarBR.Reset();
        _roarHL.Reset();
        _roarHR.Reset();
    }

    private void SetTargets(int level, float frames)
    {
        frames = MathF.Max(1f, frames);
        _gainTo = LevelGain[level];
        _gainStep = MathF.Abs(_gainTo - _gain) / frames;
        _logCutTo = MathF.Log(LevelCutoff[level]);
        _logCutStep = MathF.Abs(_logCutTo - _logCut) / frames;
        _leadTo = LevelLead[level];
        _leadStep = MathF.Abs(_leadTo - _lead) / frames;
        _duckDepthTo = LevelDuck[level];
        _duckDepthStep = MathF.Abs(_duckDepthTo - _duckDepth) / frames;
    }

    private void QuickFade()
    {
        _fading = true;
        _level = 0;
        var frames = QuickFadeSeconds * Sr;
        _gainTo = 0;
        _gainStep = MathF.Max(_gain, 1e-3f) / frames;
        _logCutTo = MathF.Log(LevelCutoff[0]);
        _logCutStep = MathF.Abs(_logCutTo - _logCut) / frames;
        _leadTo = _lead;
        _leadStep = 0;
        _duckDepthTo = _duckDepth;
        _duckDepthStep = 0;
        LastChangeFrame = _frames;
    }

    private void Stop()
    {
        _active = false;
        _fading = false;
        _level = 0;
        _gain = 0;
        _roarLeft = 0;
        Volatile.Write(ref _trackName, null);
    }

    private static float Approach(float from, float to, float maxStep) =>
        from < to ? MathF.Min(to, from + maxStep) : MathF.Max(to, from - maxStep);

    private void Segment(Span<float> output, int frames, bool shot)
    {
        if (frames <= 0 || !_active)
            return;
        // The stems follow their ramps once per segment (at most ~23 ms), the gain per sample.
        _lead = Approach(_lead, _leadTo, _leadStep * frames);
        _r.StemLead = _lead;
        PreClass();
        var buf = _buf.AsSpan(0, frames * 2);
        _r.Render(buf);
        LogBarLines(frames); // MusicFrenzy.Tempo.cs
        PostClass(buf, frames);
        if (_roarLeft > 0)
            Roar(buf, frames);

        var c0 = _logCut;
        _logCut = Approach(_logCut, _logCutTo, _logCutStep * frames);
        var g = Dsp.SvfG(MathF.Exp(0.5f * (c0 + _logCut)));
        const float k = 1.3f; // a touch of resonance: the sweep is audible as the filter opens
        Svf.Coefs(g, k, out var a1, out var a2, out var a3);

        var g0 = _gain;
        var g1 = Approach(g0, _gainTo, _gainStep * frames);
        _gain = g1;
        var d0 = _duck;
        var d1 = shot ? 1f : MathF.Max(0f, d0 - frames / (DuckRelease * Sr));
        _duck = d1;
        var dd0 = _duckDepth;
        var dd1 = Approach(dd0, _duckDepthTo, _duckDepthStep * frames);
        _duckDepth = dd1;
        var vol = Volume * BusLevel;
        // The mixer's level for the level playing (1 with the built-in mix: unchanged), ramped over the segment.
        var tr0 = _trim;
        var tr1 = Approach(tr0, LevelTrim(_level > 0 ? _level : _trimLevel), frames / (0.05f * Sr));
        _trim = tr1;
        if (_level > 0)
            _trimLevel = _level;
        var m0 = g0 * (1f - dd0 * d0) * (tr0 == 1f ? vol : vol * tr0);
        var m1 = g1 * (1f - dd1 * d1) * (tr1 == 1f ? vol : vol * tr1);
        var dm = (m1 - m0) / frames;
        var m = m0;
        if (Metering)
        {
            MeterSegment(output, buf, frames, k, a1, a2, a3, m, dm);
        }
        else
        {
            for (var i = 0; i < frames; i++)
            {
                _l1.Process(buf[i * 2], k, a1, a2, a3);
                _l2.Process(_l1.Low, k, a1, a2, a3);
                _r1.Process(buf[i * 2 + 1], k, a1, a2, a3);
                _r2.Process(_r1.Low, k, a1, a2, a3);
                output[i * 2] += _l2.Low * m;
                output[i * 2 + 1] += _r2.Low * m;
                m += dm;
            }
        }
        _frames += frames;
    }

    // ---- The audio mixer (Audio.Mixer.cs) --------------------------------------------------------

    private readonly float[] _levelTrim = [1f, 1f, 1f, 1f, 1f, 1f, 1f];
    private float _trim = 1f;
    private int _trimLevel = 1, _preview;

    /// <summary>The mixer's level for frenzy level <paramref name="level"/> (1-6; linear, 1 = as tuned). Game thread.</summary>
    public void SetLevelTrim(int level, float gain)
    {
        if (level is >= 1 and <= 6)
            Volatile.Write(ref _levelTrim[level], float.IsFinite(gain) ? Math.Clamp(gain, 0f, 4f) : 1f);
    }

    private float LevelTrim(int level) => Volatile.Read(ref _levelTrim[Math.Clamp(level, 1, 6)]);

    /// <summary>The mixer's frenzy preview: the layer may play outside a live match (the lobby) while this is on.</summary>
    public bool Preview
    {
        get => Volatile.Read(ref _preview) != 0;
        set => Volatile.Write(ref _preview, value ? 1 : 0);
    }

    /// <summary>Set by the music player while the mixer window is open: measure the layer's output.</summary>
    internal bool Metering;

    /// <summary>The layer's output level (after its gains, before the music volume): a slowly falling peak and the latest segment's mean square.</summary>
    internal float MeterPeak, MeterMs;

    /// <summary>The segment's filter-and-gain loop of <see cref="Segment"/>, also measuring what it adds.</summary>
    private void MeterSegment(Span<float> output, Span<float> buf, int frames, float k, float a1, float a2, float a3, float m, float dm)
    {
        float pk = 0;
        double sum = 0;
        for (var i = 0; i < frames; i++)
        {
            _l1.Process(buf[i * 2], k, a1, a2, a3);
            _l2.Process(_l1.Low, k, a1, a2, a3);
            _r1.Process(buf[i * 2 + 1], k, a1, a2, a3);
            _r2.Process(_r1.Low, k, a1, a2, a3);
            var l = _l2.Low * m;
            var r = _r2.Low * m;
            output[i * 2] += l;
            output[i * 2 + 1] += r;
            pk = MathF.Max(pk, MathF.Max(MathF.Abs(l), MathF.Abs(r)));
            sum += l * (double)l + r * (double)r;
            m += dm;
        }
        MeterPeak = MathF.Max(pk, MeterPeak * 0.7f);
        MeterMs = (float)(sum / Math.Max(1, frames * 2));
    }

    /// <summary>
    /// The crowd roar, added to the rendered track: white noise through a band-pass around 900 Hz (the crowd) with a
    /// touch of a brighter band (the whistles), a slow flutter, a 0.12 s swell and a long tail. Stereo: two independent
    /// noise sources. Cheap: two filters per channel for <see cref="RoarSeconds"/>.
    /// </summary>
    private void Roar(Span<float> buf, int frames)
    {
        var total = RoarSeconds * Sr;
        Svf.Coefs(Dsp.SvfG(900f), 0.9f, out var b1, out var b2, out var b3);
        Svf.Coefs(Dsp.SvfG(2600f), 1.2f, out var h1, out var h2, out var h3);
        const float k = 0.9f, kh = 1.2f;
        for (var i = 0; i < frames && _roarLeft > 0; i++, _roarLeft--)
        {
            var t = (total - _roarLeft) / total; // 0..1 over the roar
            var sec = t * RoarSeconds;
            var env = sec < 0.12f ? sec / 0.12f : MathF.Exp(-(sec - 0.12f) / 0.55f);
            env *= 1f + 0.15f * MathF.Sin(sec * 37f) + 0.1f * MathF.Sin(sec * 61f); // the flutter of many voices
            _roarL = _roarL * 1664525u + 1013904223u;
            _roarR = _roarR * 1664525u + 1013904223u;
            var nl = (_roarL >> 8) * (1f / 8388608f) - 1f;
            var nr = (_roarR >> 8) * (1f / 8388608f) - 1f;
            _roarBL.Process(nl, k, b1, b2, b3);
            _roarBR.Process(nr, k, b1, b2, b3);
            _roarHL.Process(nl, kh, h1, h2, h3);
            _roarHR.Process(nr, kh, h1, h2, h3);
            var a = RoarLevel * env;
            buf[i * 2] += (_roarBL.Band * 1.4f + _roarHL.Band * 0.35f) * a;
            buf[i * 2 + 1] += (_roarBR.Band * 1.4f + _roarHR.Band * 0.35f) * a;
        }
    }
}
