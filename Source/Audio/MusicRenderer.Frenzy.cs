namespace CallOfSlop;

/// <summary>
/// The hooks the adaptive kill-frenzy layer (MusicFrenzy.cs) uses on a renderer: stem gains on the instrument groups,
/// drop loops and bar-synced jumps to another bar of the track. The menu and lobby decks never touch them, and at
/// their defaults the renderer's output is bit-for-bit what it was without them.
/// </summary>
public sealed partial class MusicRenderer
{
    /// <summary>Gain on the drums (kick, clap/snare, hats). 1 = as written.</summary>
    public float StemDrums = 1f;

    /// <summary>Gain on the bass (off-beat bass and reverse bass). 1 = as written.</summary>
    public float StemBass = 1f;

    /// <summary>
    /// Gain on the melodic parts (lead and its harmonies, pads, arps and chants). At 0 those voices aren't rendered at
    /// all (their notes freeze until the gain comes back), which saves most of the cost of a hardbass track.
    /// </summary>
    public float StemLead = 1f;

    /// <summary>
    /// Keep the energy up: when a drop is about to hand over to anything but another drop, the sequencer jumps back to
    /// the start of that drop instead (on the bar line; the voices, delay and reverb keep ringing, so it's seamless).
    /// </summary>
    public bool LoopDrops;

    private int _jumpBar = -1;

    /// <summary>Jumps to <paramref name="bar"/> at the next bar line (the current bar plays out). -1 cancels.</summary>
    public void JumpAtNextBar(int bar) => _jumpBar = bar < 0 ? -1 : Math.Clamp(bar, 0, Math.Max(0, _track.TotalBars - 1));

    /// <summary>True while a jump is waiting for the next bar line.</summary>
    public bool JumpPending => _jumpBar >= 0;

    /// <summary>The section playing now (Intro before the first bar).</summary>
    public MusicPart Part => _part;

    /// <summary>Index of the next 16th the sequencer will play (16 per bar).</summary>
    public int StepIndex => _step;

    /// <summary>Frames until the next bar line (0 when it's due on the very next frame rendered).</summary>
    public int FramesToNextBar
    {
        get
        {
            var stepsLeft = (16 - (_step & 15)) & 15;
            return (int)Math.Ceiling(Math.Max(0, _toNextStep) + stepsLeft * _samplesPerStep);
        }
    }

    /// <summary>Frames in one bar of the loaded track.</summary>
    public double FramesPerBar => _samplesPerStep * 16;

    /// <summary>The first bar of the last drop of a track (the hardest part: later leads, reverse bass).</summary>
    public static int LastDropBar(MusicTrack t)
    {
        int bar = 0, last = 0;
        foreach (var (part, bars) in t.Sections)
        {
            if (part == MusicPart.Drop)
                last = bar;
            bar += bars;
        }
        return last;
    }

    /// <summary>Called at a bar line: applies a pending jump or a drop loop, returning the bar to play.</summary>
    private int Retarget(int bar)
    {
        var to = _jumpBar;
        _jumpBar = -1;
        if (to < 0)
        {
            if (_part != MusicPart.Drop || _section < 0 || bar < _secStart + _secBars)
                return bar; // still inside the drop (or not in one)
            var next = _section + 1;
            if (next < _track.Sections.Length && _track.Sections[next].Part == MusicPart.Drop)
                return bar; // drop into drop: let it run
            to = _secStart;
        }
        _step = to * 16;
        _section = -1; // EnterBar finds the section again and starts it (a crash on a drop)
        _drops = 0;
        var b = 0;
        foreach (var (part, bars) in _track.Sections)
        {
            if (b + bars > to)
                break;
            if (part == MusicPart.Drop)
                _drops++;
            b += bars;
        }
        return to;
    }

    private void ApplyStems(ref float gKick, ref float gSnare, ref float gHat, ref float gBass, ref float gLead, ref float gPad, ref float gArp, ref float gVox)
    {
        gKick *= StemDrums;
        gSnare *= StemDrums;
        gHat *= StemDrums;
        gBass *= StemBass;
        gLead *= StemLead;
        gPad *= StemLead;
        gArp *= StemLead;
        gVox *= StemLead;
    }

    private void MuteLeads(int n)
    {
        _bLeadL.AsSpan(0, n).Clear();
        _bLeadR.AsSpan(0, n).Clear();
        _bPadL.AsSpan(0, n).Clear();
        _bPadR.AsSpan(0, n).Clear();
        _bArpL.AsSpan(0, n).Clear();
        _bArpR.AsSpan(0, n).Clear();
    }
}
