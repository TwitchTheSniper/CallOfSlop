namespace CallOfSlop;

/// <summary>
/// The weird hardbass tracks (see MusicWeird.cs): odd meters and tempo changes from the track's
/// <see cref="WeirdTimeline"/>, sections that swerve into a drunk breakdown, lo-fi or a music box, weird leads
/// (accordion, 8-bit chip, music box, an out-of-tune harmony), a chip arpeggio layer, toy sound effects, crushed and
/// pitch-shifted chants, microtonal scales and a tape-deck master. Hooks in MusicRenderer.cs call in here only when
/// the track has a <see cref="WeirdFlavor"/>; every other track renders exactly as before.
/// </summary>
public sealed partial class MusicRenderer
{
    private bool _wd; // the track is weird
    private WeirdFlavor _wf = WeirdNone;
    private WeirdTimeline? _tl;
    private float[]? _micro; // semitone offsets per scale degree (microtonal), or null
    private float _pitchMul = 1f, _voxBase = 150f;
    private int _repeatBar = -1, _wPrevBar = -1, _wv, _crushHold;
    private char _wcode = '.';
    private WeirdLead _routed;
    private double _loadOffset;
    private float _crushLast;

    private static readonly WeirdFlavor WeirdNone = new("");

    private readonly AccordionVoice _acc = new();
    private readonly ChipVoice _chip = new();
    private readonly MusicBoxVoice _box = new();
    private readonly ToyFxVoice _toy = new();
    private readonly TapeBus _tape = new();
    private readonly float[] _bWL = new float[MaxChunk], _bWR = new float[MaxChunk], _bWM = new float[MaxChunk], _bWM2 = new float[MaxChunk];

    /// <summary>Weird-track state for a load (called by <see cref="Load"/> for every track).</summary>
    private void LoadWeird(int startBar)
    {
        var f = _track.Style == MusicStyle.Hardbass ? _track.Weird : null;
        _tl = f != null ? _track.Timeline : null;
        _wd = _tl != null;
        _wf = _wd ? f! : WeirdNone;
        _micro = _wd && _wf.Micro is { Length: 7 } m ? m : null;
        _pitchMul = 1f;
        _repeatBar = -1;
        _wPrevBar = -1;
        _wv = 0;
        _wcode = '.';
        _routed = WeirdLead.None;
        _voxBase = _voxHz;
        _loadOffset = _tl?.BarStart(Math.Clamp(startBar, 0, _track.TotalBars)) ?? 0;
        _crushHold = 0;
        _crushLast = 0;
        _acc.Reset();
        _chip.Reset();
        _box.Reset();
        _toy.Reset();
        _tape.Reset();
        _pad.CutoffMul = 1f;
    }

    /// <summary>Seconds left in a weird track (its bars aren't all the same length).</summary>
    private double WeirdRemaining => Math.Max(0, _track.Seconds - _loadOffset - Position);

    // ========================================================================================
    // Sequencer hooks
    // ========================================================================================

    /// <summary>
    /// Before each 16th: the meter (a 7/8 bar ends after 14 16ths; a 5/4 bar plays its last beat again), the length of
    /// this 16th from the timeline, the pitch sag, section changes, the tape-deck settings and the chant pitch.
    /// </summary>
    private void WeirdBeforeStep()
    {
        var tl = _tl!;
        var bar = _step >> 4;
        var s = _step & 15;
        if (s == 14 && tl.Steps(bar) == 14)
        {
            bar++;
            s = 0;
            _step = bar << 4;
        }
        if (s == 0 && bar > 0 && _wPrevBar == bar - 1 && tl.Steps(bar - 1) == 20 && _repeatBar != bar - 1)
        {
            bar--;
            s = 12;
            _step = (bar << 4) | 12;
            _repeatBar = bar;
        }
        _wPrevBar = bar;
        _wv = bar == _repeatBar ? s + 4 : s;
        _samplesPerStep = tl.StepSeconds(bar, _wv) * Sr;
        _pitchMul = tl.Pitch(bar, _wv);

        var code = tl.Code(bar);
        if (code != _wcode)
        {
            // Leaving a swerve: put back what it changed.
            if (_wcode == 'd')
            {
                ApplyLead(_leadKind);
                _lead.Configure();
            }
            if (_wcode == 'l')
                _pad.CutoffMul = 1f;
            _wcode = code;
            if (code == 'd')
                _toy.Trigger(ToySound.WhistleDown); // falling over
        }

        // The tape deck: wobbles while drunk (more the slower it goes), dusty and crushed in the lo-fi bars.
        var tempo = (float)tl.Tempo(bar, _wv);
        switch (code)
        {
            case 'd':
                _tape.Wow = Math.Clamp((1f - tempo) / 0.45f, 0f, 1f);
                _tape.LowpassHz = 0;
                _tape.Crush = 0;
                _tape.Crackle = 0.2f;
                break;
            case 'l':
                _tape.Wow = 0.35f;
                _tape.LowpassHz = 2600f;
                _tape.Crush = 0.6f;
                _tape.Crackle = 1f;
                break;
            case 'm':
                _tape.Wow = 0.12f;
                _tape.LowpassHz = 0;
                _tape.Crush = 0;
                _tape.Crackle = 0.5f;
                break;
            default:
                _tape.Wow = _tape.LowpassHz = _tape.Crush = _tape.Crackle = 0;
                break;
        }

        // Chants: shifted, and shifted differently every other four bars.
        if (s == 0)
        {
            var semis = ((bar >> 2) & 1) == 1 ? _wf.ChantSemisAlt : _wf.ChantSemis;
            _voxHz = _voxBase * MathF.Pow(2f, semis / 12f);
        }
    }

    /// <summary>Plays a whole section that swerves away from hardbass (breaks only); false for every other section.</summary>
    private bool WeirdPart(int barIn, int s, float prog, int chord, bool lastBar)
    {
        if (_part != MusicPart.Break || !WeirdFlavor.BreakCodes.Contains(_wcode))
            return false;
        var gap = lastBar && _gapAfter && s >= 12;
        switch (_wcode)
        {
            case 'm':
                MusicBoxStep(barIn, s, chord, gap);
                break;
            case 'l':
                LofiStep(barIn, s, chord, gap);
                break;
            default:
                DrunkStep(barIn, s, prog, chord, lastBar, gap);
                break;
        }
        WeirdOverlay(barIn, s, prog, chord, lastBar);
        return true;
    }

    /// <summary>A music-box interlude: the melody on tines with a low chord on each bar, a clockwork tick, then silence before the drop.</summary>
    private void MusicBoxStep(int barIn, int s, int chord, bool gap)
    {
        if (gap)
            return;
        if (s == 0)
        {
            _box.Strike(Hz(chord, 24), 0.45f);
            _box.Strike(Hz(chord + 4, 24), 0.3f);
        }
        var v = _phraseB[((barIn & 7) << 4) | s];
        if (v != Rest && v != Hold)
            _box.Strike(Hz(v, 36), s % 4 == 0 ? 0.8f : 0.6f);
        if (s % 4 == 0)
            _hat.Trigger(0.18f, false); // the clockwork
    }

    /// <summary>A few bars of lo-fi: a lazy swung boom-bap, a dark pad, sparse keys, crackle.</summary>
    private void LofiStep(int barIn, int s, int chord, bool gap)
    {
        _pad.CutoffMul = 0.45f;
        _kickLevel = 0.6f;
        if (gap)
            return;
        if (s == 0 || s == 7 || s == 10)
            _kick.Trigger(0.8f, 0.3f);
        if (s == 4 || s == 12)
            _snare.Trigger(0.55f);
        if ((s & 1) == 0)
            _hat.Trigger(s % 4 == 2 ? 0.45f : 0.3f, false);
        if (s == 0 || s == 10)
            _bass.NoteOn(Hz(chord, 0), 0.6f);
        PadStep(s, chord, 0.8f);
        var v = _phraseA[((barIn & 7) << 4) | s];
        if ((s & 1) == 0 && v != Rest && v != Hold)
            _box.Strike(Hz(v, 24), 0.4f);
    }

    /// <summary>The drunk breakdown: half-time drums, a drowsy off-beat bass, a wobbly lead; then a snare roll as it speeds back up.</summary>
    private void DrunkStep(int barIn, int s, float prog, int chord, bool lastBar, bool gap)
    {
        PadStep(s, chord, 0.6f);
        _kickLevel = 0.7f;
        if (!gap)
        {
            if (s == 0 || s == 8)
                _kick.Trigger(0.7f);
            if (s == 4 || s == 12)
                _snare.Trigger(0.55f);
            if ((s & 3) == 2)
                _bass.NoteOn(Hz(chord, 0), 0.6f);
            if ((s & 1) == 0)
                _hat.Trigger(0.3f, false);
        }
        else if (s == 12)
        {
            _vox.Trigger(FirstChant(), _voxHz, 1f);
        }
        _lead.Vibrato = 0.45f;
        _lead.VibratoHz = 3.5f;
        _lead.CutoffMul = 0.5f;
        _leadLevel = 0.55f;
        LeadStep(_phraseA, barIn, s);
        if (prog >= 0.7f && !gap)
        {
            var x = (prog - 0.7f) / 0.3f;
            if (s % (x < 0.5f ? 2 : 1) == 0)
            {
                _snare.PitchMul = 1f + 0.6f * x;
                _snare.Trigger(0.25f + 0.5f * x);
            }
            _fx.RiserLevel = 0.25f * x * x;
            _fx.RiserHz = 300f * MathF.Pow(30f, x);
        }
    }

    /// <summary>After every 16th: toy sound effects, the extra beat of a 5/4 bar, a slide whistle into drops and the chip arpeggio layer.</summary>
    private void WeirdOverlay(int barIn, int s, float prog, int chord, bool lastBar)
    {
        var gap = lastBar && _gapAfter && s >= 12;
        if (_part == MusicPart.Drop && !gap && _wv < 16 && _wf.Sfx.Length == 32 && ((barIn >> 2) & 1) == 1)
            Toy(_wf.Sfx[((barIn & 1) << 4) | s]);
        if (_wv == 16 && !gap)
            Toy(_wf.ExtraBeatSfx);
        if (_wf.WhistleIntoDrops && lastBar && _gapAfter && _wv == 8 && _part != MusicPart.Drop)
            _toy.Trigger(ToySound.WhistleUp);
        if (_wf.ChipArp && !gap && (s & 1) == 0 && (_part == MusicPart.Drop && ((barIn >> 3) & 1) == 1 || _part == MusicPart.Intro && prog >= 0.5f))
            _chip.Arp(Hz(chord, 24), Hz(chord + 2, 24), Hz(chord + 4, 24), s % 4 == 0 ? 1f : 0.7f, (float)(_samplesPerStep * 1.5 / Sr));
    }

    private void Toy(char c)
    {
        switch (c)
        {
            case 'q': _toy.Trigger(ToySound.Quack); break;
            case 's': _toy.Trigger(ToySound.Squeak); break;
            case 'w': _toy.Trigger(ToySound.WhistleUp); break;
            case 'v': _toy.Trigger(ToySound.WhistleDown); break;
            case 'b': _toy.Trigger(ToySound.Boing); break;
        }
    }

    /// <summary>Which lead plays the melody right now.</summary>
    private WeirdLead WeirdLeadNow(int barIn)
    {
        if (_wf.Lead == WeirdLead.None)
            return WeirdLead.None;
        var on = _wf.LeadUse switch
        {
            WeirdUse.Always => true,
            WeirdUse.Drops => _part == MusicPart.Drop,
            WeirdUse.LaterDrops => _part == MusicPart.Drop && _drops >= 2,
            _ => _part == MusicPart.Drop && ((barIn >> 2) & 1) == 1,
        };
        return on ? _wf.Lead : WeirdLead.None;
    }

    /// <summary>
    /// A melody note (or a rest) from LeadStep: sends it to the weird lead when one is playing, or plays the saw lead
    /// with its out-of-tune harmony. False lets LeadStep play it as usual.
    /// </summary>
    private bool WeirdLeadNote(sbyte v, int barIn)
    {
        var want = WeirdLeadNow(barIn);
        if (want != _routed)
        {
            switch (_routed)
            {
                case WeirdLead.Accordion: _acc.AllOff(); break;
                case WeirdLead.Chip: _chip.Off(); break;
            }
            if (want != WeirdLead.None)
                _lead.AllOff();
            _routed = want;
        }
        var vel = _leadLevel;
        switch (want)
        {
            case WeirdLead.Accordion:
                if (v == Rest)
                    _acc.AllOff();
                else
                {
                    _acc.NoteOn(0, Hz(v, 24), vel);
                    _acc.NoteOn(1, Hz(v - 2, 24), vel * 0.6f); // polka thirds
                }
                return true;
            case WeirdLead.Chip:
                if (v == Rest)
                    _chip.Off();
                else
                    _chip.Note(Hz(v, 24), vel);
                return true;
            case WeirdLead.MusicBox:
                if (v != Rest)
                    _box.Strike(Hz(v, 36), vel * 0.8f);
                return true;
        }
        if (_wf.HarmonyCents == 0f)
            return false;
        if (v == Rest)
        {
            _lead.AllOff();
            return true;
        }
        // The saw lead with a harmony that's deliberately out of tune by a fraction of a semitone.
        _lead.NoteOn(0, Hz(v, 24), vel);
        var off = MathF.Pow(2f, _wf.HarmonyCents / 1200f);
        _lead.NoteOn(1, Hz(v + (_harmony1 != 0 ? _harmony1 : 2), 24) * off, vel * 0.7f);
        if (_harmony2 != 0)
            _lead.NoteOn(2, Hz(v + _harmony2, 24) / off, vel * 0.6f);
        return true;
    }

    // ========================================================================================
    // Rendering hooks
    // ========================================================================================

    /// <summary>After the chants render: crush them, and render the weird voices (toys and tines ride the chant bus for its reverb).</summary>
    private void RenderWeird(int n)
    {
        var vox = _bVox.AsSpan(0, n);
        var c = _wf.ChantCrush;
        if (c > 0f)
        {
            var holdLen = 1 + (int)(c * 6f);
            var levels = MathF.Pow(2f, 11f - 7f * c);
            int hold = _crushHold;
            var last = _crushLast;
            for (var i = 0; i < n; i++)
            {
                if (--hold <= 0)
                {
                    hold = holdLen;
                    last = MathF.Round(vox[i] * levels) / levels;
                }
                vox[i] = last;
            }
            _crushHold = hold;
            _crushLast = last;
        }

        var wl = _bWL.AsSpan(0, n);
        var wr = _bWR.AsSpan(0, n);
        var m = _bWM.AsSpan(0, n);
        var m2 = _bWM2.AsSpan(0, n);
        wl.Clear();
        wr.Clear();
        m.Clear();
        m2.Clear();
        _acc.Render(wl, wr);
        _chip.Render(m);
        _box.Render(m2, m2); // both channels onto the (mono) chant bus
        for (var i = 0; i < n; i++)
            m2[i] *= 0.5f;
        _toy.Render(m2, ref _noise);
        for (var i = 0; i < n; i++)
        {
            vox[i] += m2[i] * 1.2f;
            var ch = m[i] * 0.55f;
            wl[i] += ch;
            wr[i] += ch;
        }
    }

    /// <summary>The weird layer into the master (ducked by the kick like the leads), then the tape deck, before the limiter.</summary>
    private void MixWeird(Span<float> stereo, int n)
    {
        for (var i = 0; i < n; i++)
        {
            var d = 1f - 0.5f * _bDuck[i];
            stereo[i * 2] += _bWL[i] * 0.42f * d;
            stereo[i * 2 + 1] += _bWR[i] * 0.42f * d;
        }
        _tape.Process(stereo[..(n * 2)], ref _noise);
    }
}
