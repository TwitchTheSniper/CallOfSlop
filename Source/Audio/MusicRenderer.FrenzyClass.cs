namespace CallOfSlop;

/// <summary>
/// What the per-weapon-class frenzy styles (FrenzyStyleRig.cs) need from a renderer: a log of the 16ths it played
/// during the last <see cref="Render"/> call (where in the block, which step, the chord of the bar), so the class
/// layers play exactly on the track's grid and follow its harmony. Off by default; when off, nothing changes.
/// </summary>
public sealed partial class MusicRenderer
{
    /// <summary>Most steps one render call can log (a 1024-frame block holds at most a few 16ths, even at 215 BPM).</summary>
    internal const int MaxStepLog = 32;

    /// <summary>Log the 16ths played by each <see cref="Render"/> call (the frenzy layer turns it on).</summary>
    internal bool StepLogOn;

    /// <summary>Steps logged by the last render call.</summary>
    internal int StepLogCount;

    /// <summary>Per logged step: frame offset in the render call, absolute 16th index, chord root (scale degree) of its bar.</summary>
    internal readonly int[] StepLogFrame = new int[MaxStepLog], StepLogStep = new int[MaxStepLog], StepLogChord = new int[MaxStepLog];

    /// <summary>Samples per 16th of the logged step.</summary>
    internal readonly float[] StepLogSamples = new float[MaxStepLog];

    /// <summary>Samples per 16th right now.</summary>
    internal double SamplesPerStep => _samplesPerStep;

    /// <summary>The pitch of a scale degree of the loaded track, <paramref name="octaveOffset"/> semitones above the bass octave.</summary>
    internal float ClassHz(int degree, int octaveOffset) => Hz(degree, octaveOffset);

    private void LogStep(int frame)
    {
        if (StepLogCount >= MaxStepLog)
            return;
        var step = _step - 1; // OnStep has just played it
        var bar = step >> 4;
        var i = StepLogCount++;
        StepLogFrame[i] = frame;
        StepLogStep[i] = step;
        StepLogChord[i] = _section >= 0 && bar >= _secStart ? ChordAt(bar - _secStart) : _track.Chords[0];
        StepLogSamples[i] = (float)_samplesPerStep;
    }
}
