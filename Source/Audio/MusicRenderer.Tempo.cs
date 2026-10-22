namespace CallOfSlop;

/// <summary>
/// Tempo changes for the kill-frenzy layer (levels 5 and 6 play faster, see MusicFrenzy.Tempo.cs): a real change of
/// the beat clock (the length of each 16th), not a resampling, so the pitch stays where it is. The tempo-bound parts
/// of the sound follow it: the kick's hold and decay and its sidechain, the off-beat bass decay (so they still end
/// before the next 8th), the screech lead's 8th-note wah, the harder sirens and the dotted-8th echo. A change either
/// lands at once or glides over a number of 16ths (one bar = 16); <see cref="FramesToNextBar"/> follows the glide
/// exactly, so bar lines stay exact. The menu and lobby decks never touch it: at tempo 1 everything is as before.
/// Allocation-free.
/// </summary>
public sealed partial class MusicRenderer
{
    private double _tempo = 1, _tempoFrom = 1, _tempoTo = 1, _baseSps;
    private int _tempoRamp, _tempoDone;

    // The tempo-bound settings as the style set them up at the track's own tempo.
    private float _kickHold0, _kickDecay0, _bassDecay0;

    /// <summary>The tempo multiplier playing now (1 = the track's own BPM).</summary>
    public double Tempo => _tempo;

    /// <summary>The tempo multiplier the renderer is at or gliding to.</summary>
    public double TempoTarget => _tempoRamp > 0 ? _tempoTo : _tempo;

    /// <summary>The BPM playing now: the track's times <see cref="Tempo"/>.</summary>
    public double Bpm => _track.Bpm * _tempo;

    /// <summary>True while a glide is under way.</summary>
    public bool TempoGliding => _tempoRamp > 0;

    /// <summary>The 16th (absolute index) the latest tempo change started on (-1 = none since the load).</summary>
    public int TempoChangeStep { get; private set; } = -1;

    /// <summary>The track's BPM at the current tempo, as a float (exactly the track's BPM at tempo 1).</summary>
    private float EffBpm => (float)(_track.Bpm * _tempo);

    /// <summary>Called by <see cref="Load"/>: back to the track's own tempo, and the tempo-bound settings remembered.</summary>
    private void ResetTempo()
    {
        _tempo = _tempoFrom = _tempoTo = 1;
        _tempoRamp = _tempoDone = 0;
        TempoChangeStep = -1;
        _baseSps = _samplesPerStep;
    }

    /// <summary>Called by <see cref="Load"/> after the style is set up.</summary>
    private void CaptureTempoBase()
    {
        _kickHold0 = _kick.Hold;
        _kickDecay0 = _kick.DecayTime;
        _bassDecay0 = _bass.DecayTime;
    }

    /// <summary>
    /// Sets the tempo multiplier (clamped to 0.5-2). <paramref name="glideSteps"/> 0 = from the next 16th on (call it on
    /// a bar line, as the frenzy layer does); otherwise the tempo moves in even steps over that many 16ths, starting with
    /// the next one, and reaches <paramref name="tempo"/> on the last.
    /// </summary>
    public void SetTempo(double tempo, int glideSteps = 0)
    {
        tempo = Math.Clamp(tempo, 0.5, 2.0);
        TempoChangeStep = _step;
        if (glideSteps <= 0)
        {
            var old = _tempo;
            _tempo = _tempoFrom = _tempoTo = tempo;
            _tempoRamp = _tempoDone = 0;
            // The 16th about to play already has the new length (a weird timeline sets its own on the next step).
            _samplesPerStep = _wd ? _samplesPerStep * old / tempo : _baseSps / tempo;
            RetimeVoices(true);
            return;
        }
        _tempoFrom = _tempo;
        _tempoTo = tempo;
        _tempoRamp = glideSteps;
        _tempoDone = 0;
    }

    /// <summary>The tempo of the glide's <paramref name="index"/>-th 16th (1-based).</summary>
    private double GlideTempo(int index) => index >= _tempoRamp ? _tempoTo : _tempoFrom + (_tempoTo - _tempoFrom) * index / _tempoRamp;

    /// <summary>A 16th is about to start: the glide's next tempo (only while one is under way).</summary>
    private void TempoStep()
    {
        _tempoDone++;
        _tempo = GlideTempo(_tempoDone);
        var done = _tempoDone >= _tempoRamp;
        if (done)
            _tempoRamp = _tempoDone = 0;
        if (!_wd)
            _samplesPerStep = _baseSps / _tempo;
        RetimeVoices(done);
    }

    /// <summary>The tempo-bound settings at the current tempo; the echo only at the end of a glide (it fades to retime).</summary>
    private void RetimeVoices(bool echo)
    {
        var k = (float)(1.0 / _tempo);
        var bpm = EffBpm;
        _kick.Hold = _kickHold0 * k;
        _kick.DecayTime = _kickDecay0 * k;
        _kick.Configure(60f / bpm * 0.45f);
        _bass.DecayTime = _bassDecay0 * k;
        _bass.Configure();
        if (_hb && _leadKind == HardbassLead.Screech)
            _lead.FilterLfoHz = bpm / 60f * 2f; // wah in 8ths
        if (echo)
            _delay.Retime(60f / bpm * 0.75f); // dotted eighth
    }

    /// <summary>Frames the next <paramref name="steps"/> 16ths will take (after the one playing), following any glide.</summary>
    private double StepsFrames(int steps)
    {
        if (_tempoRamp <= 0)
            return steps * _samplesPerStep;
        var baseSps = _wd ? _samplesPerStep * _tempo : _baseSps;
        double sum = 0;
        for (var j = 1; j <= steps; j++)
            sum += baseSps / GlideTempo(_tempoDone + j);
        return sum;
    }

    /// <summary>Frames in the bar that starts at the next bar line (exact during a glide; call it on a bar line for the coming bar).</summary>
    public double NextBarFrames => StepsFrames(16);
}
