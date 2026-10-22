namespace CallOfSlop;

/// <summary>
/// The kill-frenzy meter (game thread, pure maths, deterministic for a sequence of times): every kill of the local
/// player adds heat, much more for a fast follow-up, a bit more for headshots and multi-kill chains; heat bleeds away
/// slowly for <see cref="HoldSeconds"/> after the last kill, then drains faster until it's gone. Dying wipes it. The
/// heat maps to an intensity level 0 (off) to 4 with hysteresis, which <see cref="FrenzyLayer"/> turns into music.
/// Decay is integrated exactly, so the result doesn't depend on the frame rate.
/// </summary>
public sealed class FrenzyMeter
{
    public const float MaxHeat = 100f;

    /// <summary>Heat for any kill, plus up to <see cref="FastBonus"/> for a follow-up within <see cref="FastGap"/> s (none after <see cref="SlowGap"/> s).</summary>
    public const float BaseHeat = 6f, FastBonus = 14f, FastGap = 2f, SlowGap = 7f;

    /// <summary>Kills within <see cref="ChainWindow"/> s of each other form a chain; each link adds <see cref="ChainStep"/> more (up to 4 links).</summary>
    public const float ChainWindow = 4f, ChainStep = 3f, HeadshotBonus = 3f;

    public const int MaxChainLinks = 4;

    /// <summary>Decay: exponential at <see cref="HoldDecay"/>/s for the hold, then <see cref="Decay"/>/s plus a linear <see cref="Drain"/>/s.</summary>
    public const float HoldDecay = 0.04f, Decay = 0.07f, Drain = 3f, HoldSeconds = 5f;

    /// <summary>Zombies: kills come in hordes, so each is worth less and the hold is shorter.</summary>
    public const float ZombieScale = 0.5f, ZombieHoldSeconds = 3.5f;

    /// <summary>Heat needed to reach levels 1-4, and the heat below which each level is left again (hysteresis).</summary>
    public static readonly float[] Up = [12f, 30f, 52f, 85f], Down = [8f, 24f, 44f, 74f];

    public const int MaxLevel = 4;

    /// <summary>Zombies tuning (<see cref="ZombieScale"/>, <see cref="ZombieHoldSeconds"/>).</summary>
    public bool Zombies;

    public float Heat { get; private set; }
    public int Level { get; private set; }

    /// <summary>Links in the current multi-kill chain (0 for a lone kill).</summary>
    public int Chain { get; private set; }

    private double _lastKill = double.NegativeInfinity, _clock = double.NaN;

    private float Hold => Zombies ? ZombieHoldSeconds : HoldSeconds;

    /// <summary>Heat a kill would add after a gap of <paramref name="gap"/> seconds with <paramref name="links"/> chain links.</summary>
    public float KillHeat(double gap, int links, bool headshot)
    {
        var fast = (float)Math.Clamp((SlowGap - gap) / (SlowGap - FastGap), 0, 1);
        var add = BaseHeat + FastBonus * fast + ChainStep * Math.Min(links, MaxChainLinks) + (headshot ? HeadshotBonus : 0f);
        return Zombies ? add * ZombieScale : add;
    }

    /// <summary>The local player got a kill at clock time <paramref name="now"/> (seconds).</summary>
    public void OnKill(double now, bool headshot)
    {
        Update(now);
        var gap = now - _lastKill;
        Chain = gap <= ChainWindow ? Chain + 1 : 0;
        Heat = MathF.Min(MaxHeat, Heat + KillHeat(gap, Chain, headshot));
        _lastKill = now;
        UpdateLevel();
    }

    /// <summary>The local player died: the frenzy is over (the music fades out fast).</summary>
    public void OnDeath(double now)
    {
        _clock = now;
        Reset();
    }

    /// <summary>Forgets everything (new match, left the game).</summary>
    public void Reset()
    {
        Heat = 0;
        Level = 0;
        Chain = 0;
        _lastKill = double.NegativeInfinity;
    }

    /// <summary>Advances the decay to <paramref name="now"/>.</summary>
    public void Update(double now)
    {
        if (double.IsNaN(_clock) || now < _clock)
        {
            _clock = now;
            return;
        }
        var dt = now - _clock;
        _clock = now;
        if (Heat <= 0 || dt <= 0)
            return;
        // The hold part of this interval, then the drain part.
        var holdEnd = _lastKill + Hold;
        var start = now - dt;
        var holdDt = Math.Clamp(holdEnd - start, 0, dt);
        var drainDt = dt - holdDt;
        var h = (double)Heat;
        if (holdDt > 0)
            h *= Math.Exp(-HoldDecay * holdDt);
        if (drainDt > 0)
        {
            // dh/dt = -Decay*h - Drain, solved exactly.
            var c = Drain / Decay;
            h = (h + c) * Math.Exp(-Decay * drainDt) - c;
        }
        Heat = (float)Math.Max(0, h);
        UpdateLevel();
    }

    private void UpdateLevel()
    {
        var l = Level;
        while (l < MaxLevel && Heat >= Up[l])
            l++;
        while (l > 0 && Heat < Down[l - 1])
            l--;
        Level = l;
    }
}

/// <summary>
/// The kill-frenzy music layer (mixed by <see cref="MusicPlayer"/> on the streaming thread). While a match is live and
/// the local player is on a roll, a hardbass track from the pool fades in from its drop, and its intensity follows the
/// <see cref="FrenzyMeter"/> level:
/// level 1 = drums and bass only, behind a closed low-pass (the club through the wall), quiet;
/// level 2 = the filter opens: the full beat with claps, hats and the bouncing bass;
/// level 3 = the leads, harmonies, pads and chants come in;
/// level 4 = full volume, and the track jumps to its last drop (the later lead sound, reverse bass, a crash and sub drop).
/// Changes happen on the bar line and crossfade smoothly: up within half a bar, down one level at a time at most every
/// two bars, over two bars. Drops loop, so the energy never falls into a breakdown. Dying (or the setting going off, or
/// leaving the match) fades it out over <see cref="QuickFadeSeconds"/> with the filter closing; heat running out
/// fades it over two bars. One renderer, reused: allocation-free, about the cost of one hardbass track at level 3-4
/// and much less at levels 1-2 (the leads aren't rendered).
/// </summary>
public sealed class FrenzyLayer
{
    private const int Sr = Audio.SampleRate;

    /// <summary>Per level: bus gain, low-pass cutoff (Hz) and the melodic stem gain.</summary>
    public static readonly float[] LevelGain = [0f, 0.5f, 0.72f, 0.86f, 1f], LevelCutoff = [250f, 380f, 9000f, 20000f, 20000f], LevelLead = [0f, 0f, 0f, 1f, 1f];

    /// <summary>The whole layer sits a bit under the menu music, so gunfire and footsteps stay on top.</summary>
    public const float BusLevel = 0.75f;

    /// <summary>Death, setting off, leaving the match: the fade time.</summary>
    public const float QuickFadeSeconds = 2f;

    /// <summary>Gunfire ducking: depth and release time.</summary>
    public const float DuckDepth = 0.3f, DuckRelease = 0.5f;

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
    private float _gain, _gainTo, _gainStep, _logCut, _logCutTo, _logCutStep, _lead, _leadTo, _leadStep, _duck;
    private Svf _l1, _l2, _r1, _r2;
    private long _frames;
    private int _published;
    private string? _trackName;

    public FrenzyLayer(uint seed = 1)
    {
        _seed = seed | 1u;
        _pool = new MusicPlaylist(MusicTracks.All.Where(t => t.Style == MusicStyle.Hardbass && (t.Timeline?.SteadyDrops ?? true)).ToArray(), _seed * 7919u + 11u);
    }

    // ---- Game thread -------------------------------------------------------------------------

    /// <summary>The level the meter asks for (0-4); 0 while dead or outside a live match.</summary>
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

    /// <summary>The local player fired: duck the layer a little so the gunfire stays on top.</summary>
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
            if (next == 4 && _level < 4 && !_inLastDrop)
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
        var track = _pool.Next();
        _seed = _seed * 1664525u + 1013904223u;
        _r.JumpAtNextBar(-1);
        _inLastDrop = level >= 4;
        _r.Load(track, _seed, _inLastDrop ? MusicRenderer.LastDropBar(track) : track.FirstDropBar);
        _r.LoopDrops = true;
        _active = true;
        _fading = false;
        _frames = 0;
        _level = level;
        _barsAtLevel = 0;
        _duck = 0;
        _l1.Reset();
        _l2.Reset();
        _r1.Reset();
        _r2.Reset();
        _gain = 0;
        _logCut = MathF.Log(LevelCutoff[level]);
        _lead = LevelLead[level];
        SetTargets(level, (float)_r.FramesPerBar); // fade in over a bar
        LastChangeFrame = 0;
        Volatile.Write(ref _trackName, track.Name);
    }

    private void OnBar(int next)
    {
        _barsAtLevel++;
        if (next == _level)
            return;
        var up = next > _level;
        _level = next;
        _barsAtLevel = 0;
        LastChangeFrame = _frames;
        var bar = (float)_r.FramesPerBar;
        SetTargets(next, up ? bar * 0.5f : bar * 2f);
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
        LastChangeFrame = _frames;
    }

    private void Stop()
    {
        _active = false;
        _fading = false;
        _level = 0;
        _gain = 0;
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
        var buf = _buf.AsSpan(0, frames * 2);
        _r.Render(buf);

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
        var vol = Volume * BusLevel;
        var m0 = g0 * (1f - DuckDepth * d0) * vol;
        var m1 = g1 * (1f - DuckDepth * d1) * vol;
        var dm = (m1 - m0) / frames;
        var m = m0;
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
        _frames += frames;
    }
}
