namespace CallOfSlop;

/// <summary>
/// The "harder" hardbass recipe (<see cref="HardbassHarder"/>): a layered, heavier driven and hard-clipped gabber kick
/// with its own drop variation, nine-saw supersaws with wider detune, a growling off-beat bass, a bigger crowd for the
/// chants, sirens over the builds and the first bars of every drop, a pitch riser that climbs three octaves into a
/// silent gap, and gabber kick rolls. Classic hardbass tracks (no <see cref="HardbassFlavor.Harder"/>) render exactly
/// as before. Everything here is allocation-free like the rest of the renderer.
/// </summary>
public sealed partial class MusicRenderer
{
    private HardbassHarder? _harder;
    private readonly SweepVoice _siren = new(), _riser = new();
    private readonly float[] _bSiren = new float[MaxChunk], _bRiser = new float[MaxChunk];
    private float _gSweep, _riserBase = 220f;

    /// <summary>True while a track with the harder recipe is loaded.</summary>
    internal bool Harder => _harder != null;

    /// <summary>Every style starts without the harder extras (called from ConfigureStyle on each load).</summary>
    private void ResetHarder()
    {
        _harder = null;
        _kick.Top = 0;
        _vox.Crowd = 0;
        _siren.Reset();
        _riser.Reset();
    }

    /// <summary>Turns the hardbass setup up (called at the end of the hardbass style setup; Configure() follows).</summary>
    private void ApplyHarder(HardbassHarder h)
    {
        var beat = 60f / _track.Bpm;
        // Harder driven and clipped, the kick stays squared off at full scale for longer, so its decay is worked out
        // from how far the drive * clip has to fall: under 6% of its peak by 0.47 beat (well under the self-test's 10%
        // at the next 8th) at any tempo. A longer hold at full scale is what makes it heavier than the classic kick.
        var clip = MathF.Max(1f, _flavor.KickClip);
        var hold = beat * 0.12f;
        var decay = (0.47f * beat - hold) / MathF.Log(h.KickDrive * clip / 0.06f);
        Kick(_kick.StartHz * 1.25f, _kick.EndHz, 0.014f, hold, decay, h.KickDrive, 6500, 0.3f);
        _kick.Top = h.KickTop;
        _kick.TopStartHz = 900 + 40 * (_track.Root % 5);
        _kick.TopEndHz = _kick.EndHz * 3f;
        _kick.Punch = 0.35f;
        _kickB = P(h.KickB);

        // A growling, harder driven off-beat bass (same envelope, so it still never reaches the next kick).
        _bass.Drive = h.BassDrive;
        _bass.Resonance = 0.55f;
        _bass.EnvAmount = 7.5f;
        _bass.Detune = 0.016f;

        _vox.Crowd = h.Crowd;
        _masterGain = h.Master;
        _gSweep = 1f / _gVox; // the sweeps ride the vocal bus (and its delay and reverb sends) at their own levels

        _siren.Hz = Dsp.MidiHz(_track.Root + 36 + _track.Scale[4]);
        _siren.LfoSemis = h.SirenSemis;
        _siren.LfoHz = _track.Bpm / 120f;
        _siren.Drive = 3f;
        _siren.ToneHz = 3500f;
        _siren.Detune = 0.008f;
        _siren.Configure();
        _riserBase = Dsp.MidiHz(_track.Root + 24);
        _riser.Hz = _riserBase;
        _riser.LfoSemis = 0;
        _riser.Drive = 2f;
        _riser.ToneHz = 6000f;
        _riser.Detune = 0.02f;
        _riser.GlideTime = 0.05f;
        _riser.Configure();
        _siren.Reset();
        _riser.Reset();
    }

    /// <summary>More, wider detuned saws for the supersaw and hoover leads now that the CPU budget allows it.</summary>
    private void HarderLead(HardbassHarder h, HardbassLead kind)
    {
        switch (kind)
        {
            case HardbassLead.Supersaw:
            case HardbassLead.Hoover:
                _lead.Saws = h.LeadSaws;
                _lead.HarmonySaws = 5;
                _lead.Detune *= h.LeadDetune;
                _lead.Drive *= 1.4f;
                break;
            case HardbassLead.Rave:
                _lead.Saws = 5;
                _lead.HarmonySaws = 3;
                _lead.Detune = 0.012f;
                break;
            default: // Screech
                _lead.Saws = 5;
                _lead.Detune = 0.02f;
                break;
        }
    }

    /// <summary>The harder extras per 16th: sirens, the pitch riser, gabber kick rolls and the drop impact.</summary>
    private void HarderStep(int barIn, int s, float prog, bool lastBar)
    {
        var h = _harder!;
        float siren = 0, riser = 0;
        var beatHz = _track.Bpm / 60f;
        switch (_part)
        {
            case MusicPart.Build:
                if (prog < 0.875f) // then the gap: everything stops dead before the drop
                {
                    riser = h.Riser * (0.25f + 0.75f * prog);
                    _riser.Hz = _riserBase * MathF.Pow(2f, 3f * prog);
                    if (prog >= 0.5f)
                    {
                        siren = h.Siren * 0.7f;
                        _siren.LfoHz = beatHz * (0.25f + 0.75f * prog); // the wail speeds up into the drop
                    }
                    // Gabber kick roll: the build's four-on-the-floor doubles to 8ths, then 16ths.
                    if (prog >= 0.625f && s % 4 != 0 && (prog >= 0.75f || (s & 1) == 0))
                        _kick.Trigger(0.6f + 0.4f * prog);
                }
                break;
            case MusicPart.Drop:
                if (barIn < 2)
                {
                    siren = h.Siren;
                    _siren.LfoHz = beatHz * 0.5f;
                }
                if (barIn == 0 && s == 0)
                {
                    _fx.SubDrop(1f); // the huge drop: a deeper sub hit and the whole crowd at once
                    _vox.Trigger(FirstChant(), _voxHz, 1f);
                }
                break;
            case MusicPart.Break:
                if (_gapAfter && barIn >= _secBars - 2)
                {
                    var x = (barIn - (_secBars - 2) + s / 16f) / 2f;
                    riser = h.Riser * x;
                    _riser.Hz = _riserBase * MathF.Pow(2f, 3f * x);
                }
                break;
        }
        if (lastBar && _gapAfter && s >= 12)
            siren = riser = 0;
        _siren.Level = siren;
        _riser.Level = riser;
    }

    /// <summary>Renders the siren and the pitch riser onto the vocal bus (before its level and sends).</summary>
    private void RenderSweeps(Span<float> vox)
    {
        var n = vox.Length;
        var a = _bSiren.AsSpan(0, n);
        var b = _bRiser.AsSpan(0, n);
        _siren.Render(a);
        _riser.Render(b);
        var g = _gSweep;
        for (var i = 0; i < n; i++)
            vox[i] += (a[i] + b[i]) * g;
    }

    /// <summary>For the self-test: the RMS of one kick hit of a track over one beat. Reload the renderer afterwards.</summary>
    internal float KickRms(MusicTrack track)
    {
        Load(track, 1);
        var beat = (int)(Sr * 60.0 / track.Bpm);
        _kick.Trigger(1f);
        double sum = 0;
        for (var done = 0; done < beat;)
        {
            var n = Math.Min(MaxChunk, beat - done);
            _kick.Render(_bKick.AsSpan(0, n), _bDuck.AsSpan(0, n), ref _noise);
            for (var i = 0; i < n; i++)
                sum += _bKick[i] * (double)_bKick[i];
            done += n;
        }
        Load(track, 1);
        return (float)Math.Sqrt(sum / beat);
    }
}
