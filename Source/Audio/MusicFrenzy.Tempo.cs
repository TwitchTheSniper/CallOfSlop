namespace CallOfSlop;

/// <summary>
/// The frenzy speeds up at the top: levels 1-4 play the track at its own tempo, level 5 (FRENZY) faster, level 6 (MAX
/// FRENZY) faster again. Each class sets its two multipliers (Weapons/Frenzy/classes.json, "music" "tempo"); tracks
/// faster than 160 BPM get a smaller share of the boost (down to 40% of it from 208 BPM), so the 200-215 BPM monsters
/// don't run away. The renderer changes its beat
/// clock (MusicRenderer.Tempo.cs), so the pitch stays put. Going up lands on the bar line at once (with the level's
/// gain, and the level-6 crash); going down, or a class switch to another tempo, glides over the next bar, from bar
/// line to bar line. Allocation-free.
/// </summary>
public sealed partial class FrenzyLayer
{
    /// <summary>Tempo multipliers at levels 5 and 6 for a class without its own (the classic frenzy).</summary>
    public const float DefaultTempo5 = 1.10f, DefaultTempo6 = 1.20f;

    /// <summary>The boost tapers from full at <see cref="TaperStartBpm"/> to <see cref="MinBoost"/> of it by <see cref="TaperEndBpm"/> (linear toward 240 BPM).</summary>
    public const float TaperStartBpm = 160f, TaperEndBpm = 240f, MinBoost = 0.4f;

    /// <summary>The 16ths a slow-down (or a class's tempo change) glides over: one bar.</summary>
    public const int TempoGlideSteps = 16;

    private const int BarLog = 8;
    private readonly long[] _barLines = new long[BarLog];
    private int _barLineCount;
    private int _lastStep = -1;
    private long _lastStepFrame;
    private float _lastStepLen;

    /// <summary>The BPM a track plays at in a class style at a frenzy level.</summary>
    public static float LevelBpm(float trackBpm, FrenzyStyle style, int level) => level switch
    {
        >= 6 => trackBpm * (1f + (style.Tempo6 - 1f) * BoostShare(trackBpm)),
        5 => trackBpm * (1f + (style.Tempo5 - 1f) * BoostShare(trackBpm)),
        _ => trackBpm,
    };

    /// <summary>The share of a class's tempo boost a track gets: 1 up to 160 BPM, tapering to 0.4 (from 208 BPM on).</summary>
    public static float BoostShare(float trackBpm) => Math.Clamp((TaperEndBpm - trackBpm) / (TaperEndBpm - TaperStartBpm), MinBoost, 1f);

    /// <summary>The BPM playing now (the renderer's beat clock; mid-glide, between the two).</summary>
    public double Bpm => _r.Bpm;

    /// <summary>Frame (since the start) of the latest tempo change (-1 = none yet).</summary>
    public long TempoChangeFrame { get; private set; } = -1;

    /// <summary>Tempo changes since the layer was made.</summary>
    public int TempoChanges { get; private set; }

    /// <summary>The tempo multiplier for a level in the playing style.</summary>
    private double TempoFor(int level)
    {
        var bpm = _r.Track.Bpm;
        var style = _cur?.Style ?? FrenzyStyle.Plain;
        return LevelBpm(bpm, style, level) / (double)bpm;
    }

    /// <summary>A new frenzy: straight in at the level's tempo.</summary>
    private void BeginTempo(int level)
    {
        _barLineCount = 0;
        _lastStep = -1;
        _r.SetTempo(TempoFor(level));
        TempoChangeFrame = 0;
    }

    /// <summary>On a bar line (after the class switch is decided): the tempo for the coming level and class.</summary>
    private void BarTempo(int next, bool up)
    {
        var want = TempoFor(next);
        if (Math.Abs(want - _r.TempoTarget) < 1e-9)
            return;
        _r.SetTempo(want, up ? 0 : TempoGlideSteps);
        TempoChangeFrame = _frames;
        TempoChanges++;
        if (_prev != null && ClassChangeFrame == _frames)
            _xfStep = 1f / (float)Math.Max(1.0, _r.NextBarFrames); // the switch's crossfade spans the bar as it will really be
    }

    /// <summary>
    /// After a render: remember where its bar lines fell, and check the beat ran on (from the renderer's own step log):
    /// every 16th follows the one before (a drop loop or jump keeps the place in the bar) exactly one 16th later.
    /// </summary>
    private void LogBarLines(int frames)
    {
        for (var i = 0; i < _r.StepLogCount; i++)
        {
            var step = _r.StepLogStep[i];
            var at = _frames + _r.StepLogFrame[i];
            if ((step & 15) == 0 && _r.StepLogFrame[i] < frames)
                _barLines[_barLineCount++ % BarLog] = at;
            if (_lastStep >= 0 && (((_lastStep + 1) & 15) != (step & 15) || Math.Abs(at - _lastStepFrame - _lastStepLen) > 1.01f))
                StepGlitches++;
            _lastStep = step;
            _lastStepFrame = at;
            _lastStepLen = _r.StepLogSamples[i];
        }
    }

    /// <summary>Self-test: 16ths that didn't follow on from the one before (skipped, repeated or mistimed) since the layer was made.</summary>
    internal int StepGlitches { get; private set; }

    /// <summary>Self-test: frames between the last two bar lines played (0 before two).</summary>
    internal long LatestBarFrames => _barLineCount < 2 ? 0 : _barLines[(_barLineCount - 1) % BarLog] - _barLines[(_barLineCount - 2) % BarLog];

    /// <summary>Self-test: bar lines played since the frenzy started.</summary>
    internal int BarLinesPlayed => _barLineCount;

    /// <summary>Self-test: true when <paramref name="frame"/> is (within 2 frames of) one of the last few bar lines the track really played.</summary>
    internal bool IsBarLine(long frame)
    {
        for (var i = 0; i < Math.Min(_barLineCount, BarLog); i++)
            if (Math.Abs(_barLines[i] - frame) <= 2)
                return true;
        return false;
    }
}
