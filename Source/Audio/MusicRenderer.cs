namespace CallOfSlop;

/// <summary>
/// Renders one <see cref="MusicTrack"/> in real time: a 16th-note step sequencer drives the instruments in
/// MusicSynth.cs through the track's arrangement (intro, builds with snare rolls and risers, drops, breakdowns,
/// outro). Drum grooves, bass and lead sounds come from the track's <see cref="MusicStyle"/>; melodies are written
/// from the track's seed over its chord loop when the track is loaded, so every track has its own riffs, and the
/// fills vary with the play seed.
///
/// Loading and rendering never allocate: every buffer is allocated once in the constructor, so one renderer is
/// reused for track after track. Output is interleaved stereo floats, soft-limited to -1..1.
/// </summary>
public sealed partial class MusicRenderer
{
    private const int Sr = Audio.SampleRate;
    private const int PhraseSteps = 128; // 8 bars of 16ths
    private const sbyte Rest = sbyte.MinValue, Hold = sbyte.MinValue + 1;

    private MusicTrack _track = MusicTracks.All[0];
    private MusicStyle _style;
    private MusicRng _noise = new(1), _var = new(1);

    // Instruments
    private readonly KickVoice _kick = new();
    private readonly SnareVoice _snare = new();
    private readonly HatVoice _hat = new();
    private readonly BassVoice _bass = new();
    private readonly SawSynth _lead = new(), _pad = new(), _arp = new();
    private readonly FxVoices _fx = new();
    private readonly ReverseBassVoice _rev = new();
    private readonly VocalVoice _vox = new();
    private readonly MasterLimiter _limiter = new();
    private readonly PingPongDelay _delay = new();
    private readonly RoomReverb _verb = new();

    // Written at load: two 8-bar lead phrases (scale degrees, Rest or Hold per 16th).
    private readonly sbyte[] _phraseA = new sbyte[PhraseSteps], _phraseB = new sbyte[PhraseSteps];
    private readonly sbyte[] _motifKind = new sbyte[32], _motifVal = new sbyte[32];

    // Grooves for this track (bit i = step i of the bar).
    private ushort _kickA, _kickB, _snareA, _snareB, _hatA, _hatB, _openA, _openB, _bassA, _bassB, _octaveMask, _accentMask;

    // Mix levels and sends for this style
    private float _gKick, _gSnare, _gHat, _gBass, _gLead, _gPad, _gArp, _duckDepth, _leadDelay, _leadVerb;
    private int _harmony1, _harmony2; // extra lead voices as scale-degree offsets (0 = none)
    private bool _arpInDrop, _introPad, _rollingBuildBass;

    // Hardbass: bass notes per 16th (0 rest, 1 root, 2 octave, 3 fifth), chants per 16th over two bars (-1 none),
    // the lead sound in use, extra levels, the separate bass sidechain and the drive into the master limiter.
    private bool _hb;
    private HardbassFlavor _flavor = MusicTracks.DefaultHardbass;
    private readonly sbyte[] _hbBassA = new sbyte[16], _hbBassB = new sbyte[16], _chants = new sbyte[32];
    private HardbassLead _leadKind;
    private float _gRev, _gVox, _bassDuck, _masterGain = 1f, _voxHz = 150f;

    // Sequencer
    private double _samplesPerStep, _toNextStep;
    private int _step, _section, _secStart, _secBars, _drops, _lastPadChord = int.MinValue;
    private MusicPart _part;
    private bool _fillBar, _gapAfter;
    private float _leadLevel = 1f, _kickMuffle = 1f, _kickLevel = 1f;
    private long _rendered;

    // Master
    private float _dcInL, _dcInR, _dcOutL, _dcOutR;

    // Per-block work buffers, one per instrument and send (rendering goes instrument by instrument, a block at a time).
    private const int MaxChunk = SawSynth.MaxBlock;
    private readonly float[] _bKick = new float[MaxChunk], _bDuck = new float[MaxChunk], _bSnare = new float[MaxChunk], _bHat = new float[MaxChunk], _bBass = new float[MaxChunk];
    private readonly float[] _bLeadL = new float[MaxChunk], _bLeadR = new float[MaxChunk], _bPadL = new float[MaxChunk], _bPadR = new float[MaxChunk];
    private readonly float[] _bArpL = new float[MaxChunk], _bArpR = new float[MaxChunk], _bFxL = new float[MaxChunk], _bFxR = new float[MaxChunk];
    private readonly float[] _bDelayIn = new float[MaxChunk], _bVerbIn = new float[MaxChunk];
    private readonly float[] _bDelayL = new float[MaxChunk], _bDelayR = new float[MaxChunk], _bVerbL = new float[MaxChunk], _bVerbR = new float[MaxChunk];
    private readonly float[] _bRev = new float[MaxChunk], _bVox = new float[MaxChunk];

    public MusicRenderer()
    {
        Load(MusicTracks.All[0], 1);
    }

    public MusicTrack Track => _track;

    /// <summary>True once the track and its tail have played out.</summary>
    public bool Finished { get; private set; }

    /// <summary>Seconds rendered since the load point.</summary>
    public double Position => _rendered / (double)Sr;

    /// <summary>Seconds left until <see cref="Finished"/>.</summary>
    public double Remaining => _wd ? WeirdRemaining : Math.Max(0, (_track.TotalBars + MusicTrack.TailBars - (_step / 16.0)) * _track.SecondsPerBar + _toNextStep / Sr);

    /// <summary>Starts a track from the top (or from a bar, for tests). Deterministic for (track, seed, startBar).</summary>
    public void Load(MusicTrack track, uint seed, int startBar = 0)
    {
        _track = track;
        _style = track.Style;
        _noise = new MusicRng(seed * 2654435761u + 17);
        _var = new MusicRng(track.Seed ^ (seed * 0x9E3779B9u) ^ 0x5bd1e995u);
        _samplesPerStep = Sr * 60.0 / track.Bpm / 4.0;
        _toNextStep = 0;
        ResetTempo(); // the frenzy's tempo changes (MusicRenderer.Tempo.cs)
        _step = Math.Clamp(startBar, 0, track.TotalBars) * 16;
        _rendered = 0;
        _drops = 0;
        _section = -1;
        _secStart = 0;
        _secBars = 0;
        _lastPadChord = int.MinValue;
        _fillBar = false;
        _leadLevel = 1f;
        _kickMuffle = 1f;
        _kickLevel = 1f;
        Finished = false;
        _dcInL = _dcInR = _dcOutL = _dcOutR = 0;

        ConfigureStyle();
        CaptureTempoBase();
        _kick.Reset();
        _snare.Reset();
        _hat.Reset();
        _bass.Reset();
        _lead.Reset();
        _pad.Reset();
        _arp.Reset();
        _fx.Reset();
        _rev.Reset();
        _vox.Reset();
        _limiter.Reset();
        _delay.Reset();
        _verb.Reset();
        _delay.SetTime((float)(_samplesPerStep * 3 / Sr)); // dotted eighth
        LoadWeird(startBar); // MusicRenderer.Weird.cs

        // Melodies come from the track's own seed, so the track keeps its identity whatever the play seed is.
        var mel = new MusicRng(track.Seed);
        var rhythms = LeadRhythms(_style);
        var ra = mel.Range(rhythms.Length);
        var rb = (ra + 1 + mel.Range(rhythms.Length - 1)) % rhythms.Length;
        WritePhrase(_phraseA, rhythms[ra], ref mel);
        WritePhrase(_phraseB, rhythms[rb], ref mel);

        // Some per-track variety in the bass: which 16ths jump an octave, which get accents.
        _octaveMask = (ushort)(mel.Next() & 0b1010_0100_1010_0100);
        _accentMask = (ushort)(mel.Next() | 0b0001_0001_0001_0001);

        // Count the drops before the start bar, and find its section.
        var bar = 0;
        foreach (var (part, bars) in track.Sections)
        {
            if (bar + bars > startBar)
                break;
            if (part == MusicPart.Drop)
                _drops++;
            bar += bars;
        }
    }

    // ========================================================================================
    // Styles
    // ========================================================================================

    private static ushort P(string pattern)
    {
        ushort m = 0;
        for (var i = 0; i < 16 && i < pattern.Length; i++)
            if (pattern[i] != '.')
                m |= (ushort)(1 << i);
        return m;
    }

    // 16-step grooves per style (x = hit). Parsed once.
    private static readonly ushort FourFloor = P("x...x...x...x..."), Backbeat = P("....x.......x..."), Offbeat8 = P("..x...x...x...x.");

    private static readonly string[] HardstyleRhythms =
    [
        "x--x--x-x--x--x-x--x--x-x-x-x-x-",
        "x-x-x--x-x-x--x-x-x-x--x-x--x---",
        "x--x--x-x---x-x-x--x--x-x-x-----",
    ];

    private static readonly string[] HardcoreRhythms =
    [
        "x---x--x--x-x---x---x--x--x-x-x-",
        "x-----x---x-x---x-----x---x-x---",
        "x--x---x--x---x-x--x---x-x-x----",
    ];

    private static readonly string[] IndustrialRhythms =
    [
        "x..x..x..x..x.x.x..x..x..x..x.x.",
        "x.x..x.x..x.x...x.x..x.x..x.x...",
        "x..x.x..x..x.x..x..x.x..x.x.x...",
    ];

    private static readonly string[] DnbRhythms =
    [
        "x-----x---x-----x-----x-x---x---",
        "x---x---x-x-----x---x---x-----x-",
        "x-----x-x---x---x-----x-x-x-----",
    ];

    private static readonly string[] DarksynthRhythms =
    [
        "x-x-x-x-x---x-x-x-x-x-x-x---x---",
        "x---x-x-x---x---x---x-x-x-x-x---",
        "x-x-x---x-x-x---x-x-x-x-x-------",
    ];

    private static readonly string[] HardbassRhythms =
    [
        "x.x.x.x.x--.x.x.x.x.x.x.x--.x...",
        "x--x--x-x-x-x---x--x--x-x-x-x---",
        "x.xx.xx.x.xx.x..x.xx.xx.x.x.x...",
        "x---x-x-x--x--x-x---x-x-x-x-x---",
    ];

    private static string[] LeadRhythms(MusicStyle s) => s switch
    {
        MusicStyle.Hardbass => HardbassRhythms,
        MusicStyle.Hardstyle => HardstyleRhythms,
        MusicStyle.Hardcore => HardcoreRhythms,
        MusicStyle.Industrial => IndustrialRhythms,
        MusicStyle.DrumAndBass => DnbRhythms,
        _ => DarksynthRhythms,
    };

    private void ConfigureStyle()
    {
        // Defaults, then the style's own sound.
        _lead.Glide = 0;
        _lead.Square = false;
        _lead.Resonance = 0.15f;
        _bass.Glide = 0;
        _bass.Wobble = 0;
        _bass.Sustain = 0;
        _bass.Release = 0.03f;
        _snare.Clap = false;
        _snare.PitchMul = 1f;
        _lead.BendSemis = 0;
        _lead.Vibrato = 0;
        _lead.FilterLfo = 0;
        _lead.BandPass = false;
        _bass.Bounce = 0;
        _bass.Resonance = 0.3f;
        _kick.Clip = 0;
        _kick.Punch = 0;
        ResetHarder(); // MusicRendererHarder.cs
        _hb = false;
        _masterGain = 1f;
        _harmony1 = _harmony2 = 0;
        _arpInDrop = _introPad = _rollingBuildBass = false;
        _gKick = 0.85f;
        _gSnare = 0.45f;
        _gHat = 0.16f;
        _gBass = 0.4f;
        _gLead = 0.3f;
        _gPad = 0.16f;
        _gArp = 0.12f;
        _leadDelay = 0.25f;
        _leadVerb = 0.15f;

        // Hardstyle and gabber kicks end on the key's root so the kick tail is the bassline.
        var kickRoot = Dsp.MidiHz(24 + _track.Root % 12);
        while (kickRoot < 41f)
            kickRoot *= 2f;

        switch (_style)
        {
            case MusicStyle.Hardstyle:
                Kick(260, kickRoot, 0.025f, 0.11f, 0.11f, 5f, 2800, 0.25f);
                _kickA = _kickB = FourFloor;
                _snare.Clap = true;
                Snare(200, 1500, 0.13f, 0.04f, 1.4f, 0.3f);
                _snareA = _snareB = Backbeat;
                _hatA = P("x.x.x.x.x.x.x.x.");
                _hatB = P("xxx.xxx.xxx.xxx.");
                _openA = _openB = Offbeat8;
                Bass(320, 7f, 0.06f, 4f, 0.13f, 0.3f, 0.006f);
                _bassA = Offbeat8;
                _bassB = P("..x.x.x...x.x.x.");
                Lead(7, 0.02f, 0.002f, 0.5f, 0.8f, 0.12f, 2600, 1.5f, 0.25f, 1.8f);
                _harmony1 = 2;
                _harmony2 = -7;
                _duckDepth = 0.65f;
                _gBass = 0.3f;
                break;

            case MusicStyle.Hardcore:
                Kick(420, kickRoot, 0.02f, 0.09f, 0.09f, 14f, 4500, 0.35f);
                _kickA = FourFloor;
                _kickB = P("x...x...x...x.x.");
                _snare.Clap = true;
                Snare(210, 1700, 0.1f, 0.04f, 1.6f, 0.3f);
                _snareA = Backbeat;
                _snareB = P("....x.......x..x");
                _hatA = Offbeat8;
                _hatB = P("..x...x...x.x.x.");
                _openA = 0;
                _openB = Offbeat8;
                Bass(400, 4f, 0.05f, 3f, 0.08f, 0.2f, 0.01f);
                _bassA = 0; // the kick is the bass
                _bassB = 0;
                Lead(5, 0.03f, 0.003f, 0.6f, 0.85f, 0.1f, 1900, 1f, 0.2f, 3f);
                _lead.Glide = 0.045f;
                _harmony1 = -7;
                _duckDepth = 0.55f;
                _gKick = 0.8f;
                _gLead = 0.33f;
                break;

            case MusicStyle.Industrial:
                Kick(200, 48, 0.03f, 0.02f, 0.16f, 3f, 2000, 0.2f);
                _kickA = _kickB = FourFloor;
                Snare(180, 1300, 0.2f, 0.06f, 2.2f, 0.4f);
                _snareA = Backbeat;
                _snareB = P("....x..x....x...");
                _hatA = P("xxxxxxxxxxxxxxxx");
                _hatB = P("x.xxx.xxx.xxx.xx");
                _openA = 0;
                _openB = Offbeat8;
                Bass(260, 6f, 0.05f, 5f, 0.08f, 0.4f, 0.004f);
                _bassA = P(".xxx.xxx.xxx.xxx");
                _bassB = P(".xxx.xx..xxx.x.x");
                Lead(3, 0.006f, 0.002f, 0.12f, 0.25f, 0.06f, 1500, 4f, 0.1f, 4f);
                _lead.Square = true;
                _lead.Resonance = 0.4f;
                _harmony1 = 4;
                _arpInDrop = true;
                _rollingBuildBass = true;
                _duckDepth = 0.5f;
                _gLead = 0.26f;
                _gArp = 0.1f;
                _leadDelay = 0.35f;
                break;

            case MusicStyle.DrumAndBass:
                Kick(180, 52, 0.025f, 0.01f, 0.12f, 1.8f, 3500, 0.3f);
                _kickA = P("x.........x.....");
                _kickB = P("x.x.......x..x..");
                Snare(200, 2400, 0.12f, 0.05f, 1.5f, 0.5f);
                _snareA = Backbeat;
                _snareB = P("....x..x.x..x..x");
                _hatA = P("x.x.x.x.x.x.x.x.");
                _hatB = P("xxx.x.xxx.x.xxx.");
                _openA = 0;
                _openB = P("......x.......x.");
                Bass(650, 0.6f, 0.3f, 2.5f, 1f, 0.6f, 0.008f);
                _bass.Sustain = 0.9f;
                _bass.Release = 0.08f;
                _bass.Glide = 0.06f;
                _bass.Wobble = 0.6f;
                _bassA = P("x...............");
                _bassB = P("x.......x.......");
                Lead(5, 0.01f, 0.004f, 0.25f, 0.5f, 0.1f, 2200, 2f, 0.15f, 1.3f);
                _harmony1 = 2;
                _introPad = true;
                _duckDepth = 0.3f;
                _gKick = 0.9f;
                _gSnare = 0.55f;
                _gBass = 0.5f;
                _leadDelay = 0.35f;
                break;

            case MusicStyle.Hardbass:
            {
                var hb = _flavor = _track.Hardbass ?? MusicTracks.DefaultHardbass;
                _hb = true;
                _harder = hb.Harder;
                var beat = 60f / _track.Bpm;
                // A short, hard-clipped, pounding kick with a punchy click; short enough at any tempo that it never
                // smears into the off-beat bass (it ends well before the next 8th).
                // Driven 7x, the kick only falls silent ~4.3 time constants after its hold, so that has to land before the 8th.
                var kickHold = beat * 0.06f;
                Kick(300 + 12 * (_track.Root % 7), kickRoot, 0.016f, kickHold, (0.42f * beat - kickHold) / 4.3f, 7f, 4200, 0.22f);
                _kick.Clip = hb.KickClip;
                _kick.Punch = 0.3f;
                _kick.PunchHz = 1900 + 120 * (_track.Root % 6);
                _kickA = _kickB = FourFloor;
                _snare.Clap = true;
                Snare(225, 1650, 0.11f, 0.035f, 1.9f, 0.3f);
                _snareA = Backbeat;
                _snareB = P("....x.......x.x.");
                _hatA = P("xxxxxxxxxxxxxxxx");
                _hatB = P("x.xxx.xxx.xxx.xx");
                _openA = _openB = Offbeat8;
                Bass(620, 5f, 0.04f, 6f, 0.45f * beat / 3.5f, 0.45f, 0.009f); // each off-beat note is gone (-30 dB) before the next kick
                _bass.Resonance = 0.4f;
                _bass.Release = 0.015f;
                _bass.Bounce = hb.Bounce;
                _bassA = ParseBass(hb.BassA, _hbBassA);
                _bassB = ParseBass(hb.BassB, _hbBassB);
                for (var i = 0; i < 32; i++)
                    _chants[i] = (sbyte)(i < hb.Chants.Length ? hb.Chants[i] switch { 'h' => 0, 'o' => 1, 'a' => 2, 'y' => 3, _ => -1 } : -1);
                ApplyLead(hb.Lead);
                _rollingBuildBass = true;
                _duckDepth = 0.55f;
                _bassDuck = 0.85f;
                _gKick = 0.72f;
                _gSnare = 0.5f;
                _gHat = 0.14f;
                _gBass = 0.55f;
                _gPad = 0.12f;
                _gRev = 0.5f;
                _gVox = 0.45f;
                _leadDelay = 0.2f;
                _leadVerb = 0.12f;
                _masterGain = 1.6f;
                _voxHz = Dsp.MidiHz(48 + _track.Root % 12);
                if (_harder is { } harder)
                    ApplyHarder(harder);
                break;
            }

            default: // Darksynth
                Kick(160, 62, 0.015f, 0f, 0.07f, 2f, 5000, 0.45f);
                _kickA = P("x.x.x.x.x.x.x.x.");
                _kickB = P("xxxxxxxxxxxxxxxx");
                Snare(220, 1900, 0.15f, 0.06f, 1.4f, 0.55f);
                _snareA = _snareB = Backbeat;
                _hatA = FourFloor;
                _hatB = P("x.x.x.x.x.x.x.x.");
                _openA = _openB = 0;
                Bass(520, 5f, 0.04f, 8f, 0.07f, 0.2f, 0.005f);
                _bassA = P("xx.x.xx.xx.x.x.x");
                _bassB = P("xxx.xxx.xx.xxx.x");
                Lead(3, 0.008f, 0.003f, 0.3f, 0.7f, 0.12f, 3000, 1f, 0.2f, 5f);
                _lead.Square = true;
                _harmony1 = -7;
                _arpInDrop = true;
                _introPad = true;
                _duckDepth = 0.35f;
                _gKick = 0.75f;
                _gBass = 0.33f;
                _gLead = 0.24f;
                break;
        }

        _hat.ClosedTime = 0.03f;
        _hat.OpenTime = 0.18f;

        // Pad and arp are the same recipe for every style.
        _pad.Saws = 4;
        _pad.HarmonySaws = 4; // every pad note is a full voice
        _pad.HalfRate = true; // a dark, filtered sound: half-rate synthesis halves its cost
        _pad.Detune = 0.01f;
        _pad.Attack = 0.6f;
        _pad.DecayTime = 1f;
        _pad.Sustain = 0.8f;
        _pad.Release = 0.8f;
        _pad.Cutoff = 1300;
        _pad.EnvAmount = 0.3f;
        _pad.FilterTime = 1f;
        _pad.Drive = 1f;
        _arp.Saws = 1;
        _arp.HarmonySaws = 1;
        _arp.Square = true;
        _arp.Detune = 0;
        _arp.Attack = 0.001f;
        _arp.DecayTime = 0.09f;
        _arp.Sustain = 0;
        _arp.Release = 0.05f;
        _arp.Cutoff = 2400;
        _arp.EnvAmount = 3f;
        _arp.FilterTime = 0.08f;
        _arp.Drive = 1.5f;

        _kick.Configure(60f / _track.Bpm * 0.45f);
        _snare.Configure();
        _hat.Configure();
        _bass.Configure();
        _lead.Configure();
        _pad.Configure();
        _arp.Configure();
        _fx.Configure();
        _rev.Configure();
        _limiter.Configure();
    }

    private static ushort ParseBass(string pattern, sbyte[] notes)
    {
        ushort mask = 0;
        for (var i = 0; i < 16; i++)
        {
            notes[i] = (sbyte)(i < pattern.Length ? pattern[i] switch { 'x' => 1, 'o' => 2, 'f' => 3, _ => 0 } : 0);
            if (notes[i] != 0)
                mask |= (ushort)(1 << i);
        }
        return mask;
    }

    /// <summary>Sets up the lead synth for a hardbass lead sound (call _lead.Configure() after).</summary>
    private void ApplyLead(HardbassLead kind)
    {
        _leadKind = kind;
        _lead.Glide = 0;
        _lead.Square = false;
        _lead.Resonance = 0.15f;
        _lead.BendSemis = 0;
        _lead.Vibrato = 0;
        _lead.FilterLfo = 0;
        _lead.BandPass = false;
        _harmony1 = _harmony2 = 0;
        switch (kind)
        {
            case HardbassLead.Supersaw:
                Lead(7, 0.024f, 0.002f, 0.35f, 0.75f, 0.1f, 3200, 1.2f, 0.2f, 2.2f);
                _harmony1 = 2;
                _harmony2 = -7;
                _gLead = 0.36f;
                break;
            case HardbassLead.Hoover:
                Lead(5, 0.04f, 0.004f, 0.4f, 0.85f, 0.12f, 2600, 0.8f, 0.3f, 3f);
                _lead.BendSemis = 5f;
                _lead.BendTime = 0.07f;
                _lead.Glide = 0.04f;
                _lead.Vibrato = 0.3f;
                _lead.VibratoHz = 5.5f;
                _harmony1 = -7;
                _gLead = 0.36f;
                break;
            case HardbassLead.Screech:
                Lead(3, 0.012f, 0.002f, 0.2f, 0.8f, 0.08f, 1500, 2f, 0.12f, 6f);
                _lead.Resonance = 0.88f;
                _lead.BandPass = true;
                _lead.FilterLfo = 1.1f;
                _lead.FilterLfoHz = EffBpm / 60f * 2f; // wah in 8ths (at the frenzy's tempo, MusicRenderer.Tempo.cs)
                _lead.Vibrato = 0.5f;
                _lead.VibratoHz = 7f;
                _lead.BendSemis = -4f;
                _lead.BendTime = 0.05f;
                _harmony1 = 7;
                _gLead = 0.3f;
                break;
            default: // Rave
                Lead(3, 0.01f, 0.001f, 0.12f, 0.35f, 0.06f, 2200, 4f, 0.08f, 4f);
                _lead.Square = true;
                _lead.Resonance = 0.4f;
                _harmony1 = 2;
                _harmony2 = 4;
                _gLead = 0.32f;
                break;
        }
        if (_harder is { } h)
            HarderLead(h, kind);
    }

    private void Kick(float start, float end, float pitchTime, float hold, float decay, float drive, float tone, float click)
    {
        _kick.StartHz = start;
        _kick.EndHz = end;
        _kick.PitchTime = pitchTime;
        _kick.Hold = hold;
        _kick.DecayTime = decay;
        _kick.Drive = drive;
        _kick.ToneHz = tone;
        _kick.Click = click;
    }

    private void Snare(float toneHz, float noiseHz, float noiseTime, float toneTime, float drive, float toneLevel)
    {
        _snare.ToneHz = toneHz;
        _snare.NoiseHz = noiseHz;
        _snare.NoiseTime = noiseTime;
        _snare.ToneTime = toneTime;
        _snare.Drive = drive;
        _snare.ToneLevel = toneLevel;
    }

    private void Bass(float cutoff, float envAmount, float filterTime, float drive, float decay, float sub, float detune)
    {
        _bass.Cutoff = cutoff;
        _bass.EnvAmount = envAmount;
        _bass.FilterTime = filterTime;
        _bass.Drive = drive;
        _bass.DecayTime = decay;
        _bass.Sub = sub;
        _bass.Detune = detune;
    }

    private void Lead(int saws, float detune, float attack, float decay, float sustain, float release, float cutoff, float envAmount, float filterTime, float drive)
    {
        _lead.Saws = saws;
        _lead.HarmonySaws = Math.Min(saws, 3); // the quieter harmony voices get a thinner unison (CPU)
        _lead.Detune = detune;
        _lead.Attack = attack;
        _lead.DecayTime = decay;
        _lead.Sustain = sustain;
        _lead.Release = release;
        _lead.Cutoff = cutoff;
        _lead.EnvAmount = envAmount;
        _lead.FilterTime = filterTime;
        _lead.Drive = drive;
    }

    // ========================================================================================
    // Melody writing
    // ========================================================================================

    /// <summary>
    /// Writes an 8-bar lead phrase: a 2-bar motif (rhythm from the style, contour from the seed) that follows the
    /// chords: strong beats land on chord tones, the notes between step through the scale. Bars 1-2 state it, 3-4
    /// answer it with a changed ending, 5-6 repeat it, and 7-8 resolve to the root.
    /// </summary>
    private void WritePhrase(sbyte[] dst, string rhythm, ref MusicRng r)
    {
        for (var s = 0; s < 32; s++)
        {
            var c = rhythm[s];
            if (c == 'x')
            {
                if (s % 4 == 0 || s == 0)
                {
                    _motifKind[s] = 1; // chord tone: 0 root, 1 third, 2 fifth, 3 octave
                    var pick = r.Unit();
                    _motifVal[s] = (sbyte)(pick < 0.35f ? 0 : pick < 0.6f ? 1 : pick < 0.85f ? 2 : 3);
                }
                else
                {
                    _motifKind[s] = 2; // scale step from the previous note
                    var pick = r.Unit();
                    _motifVal[s] = (sbyte)(pick < 0.3f ? 1 : pick < 0.6f ? -1 : pick < 0.75f ? 2 : pick < 0.9f ? -2 : 0);
                }
            }
            else
            {
                _motifKind[s] = (sbyte)(c == '-' ? 0 : -1); // hold / rest
                _motifVal[s] = 0;
            }
        }

        var prev = 4;
        for (var bar = 0; bar < 8; bar++)
        {
            var chord = ChordAt(bar);
            for (var st = 0; st < 16; st++)
            {
                var ms = (bar & 1) * 16 + st;
                var kind = _motifKind[ms];
                var idx = bar * 16 + st;
                if (bar == 7 && st >= 8)
                {
                    // Cadence: land on the root and hold it.
                    if (st == 8)
                    {
                        var root = Nearest(0, prev);
                        dst[idx] = (sbyte)root;
                        prev = root;
                    }
                    else
                    {
                        dst[idx] = st == 15 ? Rest : Hold;
                    }
                    continue;
                }
                if (kind < 0)
                {
                    dst[idx] = Rest;
                    continue;
                }
                if (kind == 0)
                {
                    dst[idx] = Hold;
                    continue;
                }
                int deg;
                if (kind == 1)
                {
                    var v = _motifVal[ms];
                    deg = Nearest(chord + (v == 3 ? 7 : v * 2), prev);
                }
                else
                {
                    deg = prev + _motifVal[ms];
                    if (bar == 3 && st >= 8)
                        deg += 1; // the answer ends differently
                }
                while (deg > 11)
                    deg -= 7;
                while (deg < -3)
                    deg += 7;
                dst[idx] = (sbyte)deg;
                prev = deg;
            }
        }
    }

    /// <summary>The octave of a degree closest to the previous note.</summary>
    private static int Nearest(int deg, int prev)
    {
        while (deg - prev > 4)
            deg -= 7;
        while (prev - deg > 4)
            deg += 7;
        return deg;
    }

    private int ChordAt(int barInSection) => _track.Chords[((barInSection % _track.Chords.Length) + _track.Chords.Length) % _track.Chords.Length];

    private float Hz(int degree, int octaveOffset)
    {
        var sc = _track.Scale;
        var o = (int)MathF.Floor(degree / (float)sc.Length);
        var i = degree - o * sc.Length;
        if (_wd)
            return Dsp.MidiHz(_track.Root + octaveOffset + 12 * o + sc[i] + (_micro != null ? _micro[i] : 0f)) * _pitchMul; // weird tracks: microtones, pitch sag
        return Dsp.MidiHz(_track.Root + octaveOffset + 12 * o + sc[i]);
    }

    // ========================================================================================
    // Sequencer
    // ========================================================================================

    private static bool On(ushort mask, int step) => (mask & (1 << step)) != 0;

    private void EnterBar(int bar)
    {
        // Advance to the section holding this bar.
        while (_section < 0 || bar >= _secStart + _secBars)
        {
            _section++;
            if (_section >= _track.Sections.Length)
                return;
            var start = 0;
            for (var i = 0; i < _section; i++)
                start += _track.Sections[i].Bars;
            if (bar >= start + _track.Sections[_section].Bars)
                continue; // skipped by a start bar
            _secStart = start;
            _secBars = _track.Sections[_section].Bars;
            _part = _track.Sections[_section].Part;
            _gapAfter = _section + 1 < _track.Sections.Length && _track.Sections[_section + 1].Part == MusicPart.Drop;
            StartSection();
        }

        var barIn = bar - _secStart;
        _fillBar = _part == MusicPart.Drop && (barIn & 7) == 7 && barIn < _secBars - 1 && _var.Chance(0.7f);
        if (_part == MusicPart.Drop && (barIn & 7) == 0 && barIn > 0)
            _fx.Crash(0.35f);
    }

    private void StartSection()
    {
        _lead.AllOff();
        _arp.AllOff();
        _fx.RiserLevel = 0;
        _lead.CutoffMul = 1f;
        _bass.CutoffMul = 1f;
        _snare.PitchMul = 1f;
        _leadLevel = 1f;
        _kickMuffle = 1f;
        _kickLevel = 1f;
        switch (_part)
        {
            case MusicPart.Drop:
                _drops++;
                _fx.Crash(0.9f);
                _fx.SubDrop(0.8f);
                _pad.AllOff();
                _lastPadChord = int.MinValue;
                break;
            case MusicPart.Break:
                _fx.Crash(0.6f);
                _bass.NoteOff();
                break;
            case MusicPart.Outro:
                _fx.Crash(0.6f);
                _pad.AllOff();
                _lastPadChord = int.MinValue;
                break;
            case MusicPart.Build:
                _bass.NoteOff();
                break;
        }
        if (_hb)
        {
            // Later drops can bring a different lead sound; every other section goes back to the first one.
            var want = _part == MusicPart.Drop && _drops >= 2 ? _flavor.LaterLead : _flavor.Lead;
            if (want != _leadKind)
            {
                ApplyLead(want);
                _lead.Configure();
            }
        }
    }

    /// <summary>A hardbass chant on this 16th of the two-bar chant pattern, if there is one.</summary>
    private void ChantStep(int barIn, int s)
    {
        var w = _chants[((barIn & 1) << 4) | s];
        if (w >= 0)
            _vox.Trigger((VocalWord)w, _voxHz, 1f);
    }

    /// <summary>The first word of the chant pattern (for the hits before a drop).</summary>
    private VocalWord FirstChant()
    {
        foreach (var w in _chants)
            if (w >= 0)
                return (VocalWord)w;
        return VocalWord.Hey;
    }

    private bool ReverseBassNow(int barIn) => _flavor.Reverse switch
    {
        ReverseBassUse.LaterDrops => _drops >= 2,
        ReverseBassUse.Alternate => ((barIn >> 2) & 1) == 1,
        _ => false,
    };

    private float ReverseHz(int chord)
    {
        var hz = Hz(chord, 0);
        while (hz > 95f)
            hz *= 0.5f;
        while (hz < 45f)
            hz *= 2f;
        return hz;
    }

    private void OnStep()
    {
        if (_wd)
            WeirdMeter(); // odd meters (MusicRenderer.Weird.cs)
        var bar = _step >> 4;
        var s = _step & 15;
        if (s == 0 && (_jumpBar >= 0 || LoopDrops))
            bar = Retarget(bar); // the frenzy layer's bar-synced jumps and drop loops (MusicFrenzy.cs)
        if (_wd)
            WeirdTiming(bar, s); // this 16th's length, pitch and section effects (after any jump)
        _step++;
        if (bar >= _track.TotalBars)
        {
            if (bar == _track.TotalBars && s == 0)
            {
                _lead.AllOff();
                _pad.AllOff();
                _arp.AllOff();
                _bass.NoteOff();
                _fx.RiserLevel = 0;
            }
            if (bar >= _track.TotalBars + MusicTrack.TailBars)
                Finished = true;
            return;
        }
        if (s == 0)
            EnterBar(bar);

        var barIn = bar - _secStart;
        var prog = (barIn * 16 + s) / (float)(_secBars * 16);
        var lastBar = barIn == _secBars - 1;
        var secondHalf = barIn >= _secBars / 2;
        var chord = ChordAt(barIn);
        var dv = (barIn >> 3) & 1; // groove variant alternates every 8 bars
        var phrase = ((_drops - 1 + (barIn >> 4)) & 1) == 0 ? _phraseA : _phraseB;
        if (_wd && WeirdPart(barIn, s, prog, chord, lastBar))
            return; // a weird track's swerve section (drunk, lo-fi, music box)

        switch (_part)
        {
            case MusicPart.Intro:
            {
                _kickMuffle = secondHalf ? 1f : 0.18f;
                if (_hb)
                {
                    // The driven hardbass kick is loud at any velocity: keep it muffled and down for most of the intro.
                    _kickMuffle = prog < 0.75f ? 0.25f : 1f;
                    _kickLevel = prog < 0.75f ? 0.4f : 1f;
                }
                if (!lastBar || s < 12 || !_gapAfter)
                    Drums(s, 0, kick: true, snare: secondHalf, hats: barIn >= 2, open: secondHalf);
                if (secondHalf && _bassA != 0)
                {
                    _bass.CutoffMul = 0.3f + 0.7f * prog;
                    BassStep(s, chord, 0);
                }
                PadStep(s, chord, _introPad ? 0.7f : 0.45f);
                if (_arpInDrop && secondHalf)
                    ArpStep(s, chord);
                if (_hb && lastBar && s == 12)
                    _vox.Trigger(FirstChant(), _voxHz, 0.8f);
                break;
            }

            case MusicPart.Build:
            {
                // Kick pattern, then four on the floor, then a gap; a snare roll that doubles up as it rises.
                var gap = prog >= 0.875f;
                if (!gap)
                {
                    if (prog < 0.5f)
                        Drums(s, 0, kick: _style != MusicStyle.DrumAndBass || prog < 0.25f, snare: false, hats: true, open: false);
                    else if (s % 4 == 0)
                        _kick.Trigger(0.9f);
                    var every = prog < 0.25f ? 4 : prog < 0.5f ? 2 : 1;
                    if (s % every == 0)
                    {
                        _snare.PitchMul = 1f + 0.6f * prog;
                        _snare.Trigger((0.3f + 0.7f * prog) * 0.8f);
                    }
                }
                if (_rollingBuildBass && prog < 0.5f)
                {
                    _bass.CutoffMul = 0.5f - 0.4f * prog;
                    BassStep(s, chord, 0);
                }
                _fx.RiserLevel = 0.05f + 0.25f * prog * prog;
                _fx.RiserHz = 300f * MathF.Pow(30f, prog);
                _lead.CutoffMul = 0.06f + 0.94f * prog * prog;
                _leadLevel = 0.8f;
                if (gap)
                {
                    if (s == 0 || s == 8)
                        _lead.AllOff();
                    if (_hb && lastBar && (s == 8 || s == 12))
                        _vox.Trigger(FirstChant(), _voxHz, s == 12 ? 1f : 0.8f); // "HEY! HEY!" into the drop
                }
                else
                {
                    LeadStep(_phraseA, barIn, s);
                }
                if (prog < 0.5f)
                    PadStep(s, chord, 0.6f);
                else if (s == 0)
                {
                    _pad.AllOff();
                    _lastPadChord = int.MinValue;
                }
                break;
            }

            case MusicPart.Drop:
            {
                var fill = _fillBar && s >= 12;
                if (fill)
                {
                    _snare.PitchMul = 1f + 0.08f * (s - 12);
                    _snare.Trigger(0.6f + 0.1f * (s - 12));
                    if (_style == MusicStyle.Darksynth || _style == MusicStyle.Hardcore || _hb)
                        _kick.Trigger(0.9f);
                    _hat.Trigger(0.6f, false);
                }
                else
                {
                    _snare.PitchMul = 1f;
                    Drums(s, dv, kick: true, snare: true, hats: true, open: true);
                }
                if (lastBar && _gapAfter && s >= 12)
                {
                    _bass.NoteOff();
                }
                else if (_hb && ReverseBassNow(barIn))
                {
                    // Hardstyle reverse bass: swells from just after each kick and cuts just before the next.
                    _bass.NoteOff();
                    if (s % 4 == 1)
                        _rev.Trigger(ReverseHz(chord), (float)(_samplesPerStep * 2.6 / Sr), 1f);
                }
                else
                {
                    BassStep(s, chord, dv);
                }
                if (_hb && !(lastBar && _gapAfter && s >= 12))
                    ChantStep(barIn, s);
                LeadStep(phrase, barIn, s);
                if (_arpInDrop && dv == 1)
                    ArpStep(s, chord);
                break;
            }

            case MusicPart.Break:
            {
                PadStep(s, chord, 1f);
                if (secondHalf)
                {
                    _lead.CutoffMul = 0.3f + 0.3f * prog;
                    _leadLevel = 0.6f;
                    LeadStep(_phraseA, barIn, s);
                }
                else if (_arpInDrop || _introPad)
                {
                    ArpStep(s, chord);
                }
                if (secondHalf && s % 4 == 0)
                    _hat.Trigger(0.25f + 0.3f * prog, true); // a pulse to keep the time
                if (_hb && _flavor.HalfTimeBreak && secondHalf)
                {
                    // Half time: the kick on 1 and the clap on 3, so a fast track can breathe.
                    if (s == 0)
                        _kick.Trigger(0.75f, 0.5f);
                    else if (s == 8)
                        _snare.Trigger(0.6f);
                }
                if (_gapAfter && barIn >= _secBars - 2)
                {
                    _fx.RiserLevel = 0.2f * (barIn - _secBars + 2 + s / 16f) / 2f;
                    _fx.RiserHz = 500f + 6000f * prog;
                }
                break;
            }

            case MusicPart.Outro:
            {
                if (lastBar)
                {
                    if (s == 0)
                    {
                        _kick.Trigger(1f);
                        _fx.Crash(0.7f);
                        _bass.NoteOff();
                    }
                    break;
                }
                Drums(s, 0, kick: true, snare: prog < 0.5f, hats: prog < 0.75f, open: prog < 0.5f);
                if (prog < 0.5f)
                    BassStep(s, chord, 0);
                else if (s == 0)
                    _bass.NoteOff();
                if (prog >= 0.5f)
                    PadStep(s, chord, 0.7f);
                break;
            }
        }
        if (_harder != null)
            HarderStep(barIn, s, prog, lastBar);
        if (_wd)
            WeirdOverlay(barIn, s, prog, chord, lastBar);
    }

    private void Drums(int s, int variant, bool kick, bool snare, bool hats, bool open)
    {
        var kickMask = variant == 0 ? _kickA : _kickB;
        if (kick && On(kickMask, s))
            _kick.Trigger((s % 4 == 0 ? 1f : 0.85f) * (_kickMuffle < 1f ? 0.6f : 1f), _kickMuffle);
        if (snare && On(variant == 0 ? _snareA : _snareB, s))
            _snare.Trigger(s % 4 == 0 ? 1f : 0.6f);
        if (open && On(variant == 0 ? _openA : _openB, s))
        {
            _hat.Trigger(0.8f, true);
        }
        else if (hats && On(variant == 0 ? _hatA : _hatB, s))
        {
            var vel = (s & 1) == 0 ? 0.8f : 0.5f;
            vel *= 0.85f + 0.3f * _var.Unit();
            _hat.Trigger(vel, false);
        }
        else if (hats && _var.Chance(0.06f))
        {
            _hat.Trigger(0.3f, false); // the odd ghost note
        }
    }

    private void BassStep(int s, int chord, int variant)
    {
        var mask = variant == 0 ? _bassA : _bassB;
        if (mask == 0)
            return;
        if (_hb)
        {
            // The bouncy off-beat bass: root, octave or fifth per 16th, accents on the off-beats.
            var code = (variant == 0 ? _hbBassA : _hbBassB)[s];
            if (code == 0)
                return;
            _bass.NoteOn(Hz(code == 3 ? chord + 4 : chord, code == 2 ? 12 : 0), (s & 3) == 2 ? 1f : 0.82f);
            return;
        }
        if (_style == MusicStyle.DrumAndBass)
        {
            // Reese: long notes that glide; variant B moves to the fifth halfway through the bar.
            if (On(mask, s))
                _bass.NoteOn(Hz(s >= 8 ? chord + 4 : chord, 0), 1f);
            return;
        }
        if (!On(mask, s))
            return;
        var deg = chord;
        var oct = 0;
        if (_style == MusicStyle.Industrial && On(_octaveMask, s))
            oct = 12;
        if (_style == MusicStyle.Darksynth && s >= 14 && On(_octaveMask, s - 8))
            deg += 1; // a dissonant pickup into the next bar
        _bass.NoteOn(Hz(deg, oct), On(_accentMask, s) ? 1f : 0.8f);
    }

    private void LeadStep(sbyte[] phrase, int barIn, int s)
    {
        var v = phrase[((barIn & 7) << 4) | s];
        if (v == Hold)
            return;
        if (_wd && WeirdLeadNote(v, barIn))
            return; // a weird lead (accordion, chip, music box) or an out-of-tune harmony
        if (v == Rest)
        {
            _lead.AllOff();
            return;
        }
        var vel = _leadLevel;
        _lead.NoteOn(0, Hz(v, 24), vel);
        if (_harmony1 != 0)
            _lead.NoteOn(1, Hz(v + _harmony1, 24), vel * 0.7f);
        if (_harmony2 != 0)
            _lead.NoteOn(2, Hz(v + _harmony2, 24), vel * 0.6f);
    }

    private void PadStep(int s, int chord, float level)
    {
        if (s != 0 || chord == _lastPadChord)
            return;
        _lastPadChord = chord;
        _pad.NoteOn(0, Hz(chord, 12), level);
        _pad.NoteOn(1, Hz(chord + 2, 12), level * 0.8f);
        _pad.NoteOn(2, Hz(chord + 4, 12), level * 0.8f);
        _pad.NoteOn(3, Hz(chord + 7, 12), level * 0.5f);
    }

    private void ArpStep(int s, int chord)
    {
        // Up through the chord in 16ths, a different turn on the off-beats.
        var i = s & 3;
        var deg = chord + (i == 3 ? 7 : i * 2);
        if ((s & 4) != 0 && i == 2)
            deg = chord + 4 + 2;
        _arp.NoteOn(0, Hz(deg, 24), (s & 3) == 0 ? 1f : 0.7f);
    }

    // ========================================================================================
    // Rendering
    // ========================================================================================

    /// <summary>Renders the next frames into an interleaved stereo buffer (overwrites it). Allocation-free.</summary>
    public void Render(Span<float> stereo)
    {
        var frames = stereo.Length / 2;
        var i = 0;
        StepLogCount = 0;
        while (i < frames)
        {
            if (_toNextStep <= 0)
            {
                if (_tempoRamp > 0)
                    TempoStep(); // a frenzy tempo glide (MusicRenderer.Tempo.cs)
                OnStep();
                if (StepLogOn)
                    LogStep(i); // the frenzy's class styles (MusicRenderer.FrenzyClass.cs)
                _toNextStep += _samplesPerStep;
            }
            var n = Math.Min(Math.Min(frames - i, MaxChunk), Math.Max(1, (int)Math.Ceiling(_toNextStep)));
            RenderSamples(stereo.Slice(i * 2, n * 2));
            i += n;
            _toNextStep -= n;
        }
        _rendered += frames;
    }

    private void RenderSamples(Span<float> stereo)
    {
        var n = stereo.Length / 2;
        var kick = _bKick.AsSpan(0, n);
        var duck = _bDuck.AsSpan(0, n);
        var snare = _bSnare.AsSpan(0, n);
        var hat = _bHat.AsSpan(0, n);
        var bass = _bBass.AsSpan(0, n);
        var leadL = _bLeadL.AsSpan(0, n);
        var leadR = _bLeadR.AsSpan(0, n);
        var padL = _bPadL.AsSpan(0, n);
        var padR = _bPadR.AsSpan(0, n);
        var arpL = _bArpL.AsSpan(0, n);
        var arpR = _bArpR.AsSpan(0, n);
        var fxL = _bFxL.AsSpan(0, n);
        var fxR = _bFxR.AsSpan(0, n);
        var delayIn = _bDelayIn.AsSpan(0, n);
        var verbIn = _bVerbIn.AsSpan(0, n);
        var delayL = _bDelayL.AsSpan(0, n);
        var delayR = _bDelayR.AsSpan(0, n);
        var verbL = _bVerbL.AsSpan(0, n);
        var verbR = _bVerbR.AsSpan(0, n);
        var rev = _bRev.AsSpan(0, n);
        var vox = _bVox.AsSpan(0, n);

        _kick.Render(kick, duck, ref _noise);
        _snare.Render(snare, ref _noise);
        _hat.Render(hat, ref _noise);
        _bass.Render(bass);
        if (StemLead > 0f)
        {
            _lead.Render(leadL, leadR);
            _pad.Render(padL, padR);
            _arp.Render(arpL, arpR);
        }
        else
        {
            MuteLeads(n); // the frenzy layer's lower levels: no leads, and no CPU spent on them
        }
        _fx.Render(fxL, fxR, ref _noise);
        _rev.Render(rev);
        if (StemLead > 0f)
            _vox.Render(vox, ref _noise);
        else
            vox.Clear();
        if (_harder != null)
            RenderSweeps(vox);
        if (_wd)
            RenderWeird(n); // MusicRenderer.Weird.cs

        // Levels, then the delay and reverb sends.
        float gKick = _gKick * _kickLevel, gSnare = _gSnare, gHat = _gHat, gBass = _gBass, gLead = _gLead, gPad = _gPad, gArp = _gArp;
        float leadDelay = _leadDelay, leadVerb = _leadVerb, gVox = _gVox;
        if (StemDrums != 1f || StemBass != 1f || StemLead != 1f)
            ApplyStems(ref gKick, ref gSnare, ref gHat, ref gBass, ref gLead, ref gPad, ref gArp, ref gVox);
        for (var i = 0; i < n; i++)
        {
            var vx = vox[i] *= gVox;
            kick[i] *= gKick;
            var sn = snare[i] *= gSnare;
            var ht = hat[i] *= gHat;
            bass[i] *= gBass;
            var ll = leadL[i] *= gLead;
            var lr = leadR[i] *= gLead;
            var pl = padL[i] *= gPad;
            var pr = padR[i] *= gPad;
            var al = arpL[i] *= gArp;
            var ar = arpR[i] *= gArp;
            delayIn[i] = (ll + lr) * leadDelay + (al + ar) * 0.4f + vx * 0.25f;
            verbIn[i] = sn * 0.5f + (pl + pr) * 0.5f + (ll + lr) * leadVerb + (al + ar) * 0.2f + ht * 0.2f + vx * 0.5f;
        }
        _delay.Render(delayIn, delayL, delayR);
        _verb.Render(verbIn, verbL, verbR);
        if (_hb)
        {
            MixHardbass(stereo, n);
            return;
        }

        // Sidechain ducking, the mix, a DC blocker and the soft limiter.
        float dcInL = _dcInL, dcInR = _dcInR, dcOutL = _dcOutL, dcOutR = _dcOutR, duckDepth = _duckDepth;
        for (var i = 0; i < n; i++)
        {
            var d = 1f - duckDepth * duck[i];
            var bs = bass[i];
            var synthL = (bs + leadL[i] + padL[i] + arpL[i] + delayL[i] * 0.5f) * d;
            var synthR = (bs + leadR[i] + padR[i] + arpR[i] + delayR[i] * 0.5f) * d;
            var k = kick[i];
            var sn = snare[i];
            var ht = hat[i];
            var l = k + sn + ht * 0.8f + synthL + fxL[i] * 0.22f + verbL[i];
            var r = k + sn + ht + synthR + fxR[i] * 0.22f + verbR[i];

            var hl = l - dcInL + 0.9985f * dcOutL;
            dcInL = l;
            dcOutL = hl;
            var hr = r - dcInR + 0.9985f * dcOutR;
            dcInR = r;
            dcOutR = hr;
            if (MathF.Abs(hl) + MathF.Abs(hr) < 1e-15f)
                dcOutL = dcOutR = 0; // no denormals in silence
            stereo[i * 2] = Dsp.Tanh(hl * 0.9f);
            stereo[i * 2 + 1] = Dsp.Tanh(hr * 0.9f);
        }
        _dcInL = dcInL;
        _dcInR = dcInR;
        _dcOutL = dcOutL;
        _dcOutR = dcOutR;
    }

    /// <summary>
    /// The hardbass mix: the bass pumps harder than the leads (its own sidechain depth), the reverse bass and chants
    /// sit on top, and the whole bus is driven into the look-ahead limiter: loud, never clipped.
    /// </summary>
    private void MixHardbass(Span<float> stereo, int n)
    {
        float dcInL = _dcInL, dcInR = _dcInR, dcOutL = _dcOutL, dcOutR = _dcOutR, duckDepth = _duckDepth, bassDuck = _bassDuck, gRev = _gRev * StemBass, master = _masterGain;
        for (var i = 0; i < n; i++)
        {
            var dk = _bDuck[i];
            var d = 1f - duckDepth * dk;
            var bs = _bBass[i] * (1f - bassDuck * dk) + _bRev[i] * gRev;
            var synthL = bs + (_bLeadL[i] + _bPadL[i] + _bArpL[i] + _bDelayL[i] * 0.5f) * d;
            var synthR = bs + (_bLeadR[i] + _bPadR[i] + _bArpR[i] + _bDelayR[i] * 0.5f) * d;
            var k = _bKick[i];
            var sn = _bSnare[i];
            var ht = _bHat[i];
            var vx = _bVox[i];
            var l = k + sn + ht * 0.8f + synthL + _bFxL[i] * 0.22f + _bVerbL[i] + vx;
            var r = k + sn + ht + synthR + _bFxR[i] * 0.22f + _bVerbR[i] + vx;

            var hl = l - dcInL + 0.9985f * dcOutL;
            dcInL = l;
            dcOutL = hl;
            var hr = r - dcInR + 0.9985f * dcOutR;
            dcInR = r;
            dcOutR = hr;
            if (MathF.Abs(hl) + MathF.Abs(hr) < 1e-15f)
                dcOutL = dcOutR = 0;
            // A soft clipper shaves the kick's peaks first, then the limiter takes what's left: loud, never clipped.
            stereo[i * 2] = Dsp.Tanh(hl * master) * 1.05f;
            stereo[i * 2 + 1] = Dsp.Tanh(hr * master) * 1.05f;
        }
        _dcInL = dcInL;
        _dcInR = dcInR;
        _dcOutL = dcOutL;
        _dcOutR = dcOutR;
        if (_wd)
            MixWeird(stereo, n);
        _limiter.Process(stereo[..(n * 2)]);
    }

    /// <summary>
    /// For the self-test: loads a track and plays its kick and its bass alone, returning how loud the kick still is at
    /// the next 8th and how loud an off-beat bass note still is at the next kick, both relative to their peaks. Small
    /// numbers mean the two never smear into each other. Reload the renderer afterwards.
    /// </summary>
    internal (float KickAtOffbeat, float BassAtNextKick) GrooveTails(MusicTrack track)
    {
        Load(track, 1);
        var half = (int)(Sr * 30.0 / track.Bpm); // an 8th note in samples
        _kick.Trigger(1f);
        var kickPeak = 0f;
        var kickTail = 0f;
        for (var done = 0; done < half;)
        {
            var n = Math.Min(MaxChunk, half - done);
            _kick.Render(_bKick.AsSpan(0, n), _bDuck.AsSpan(0, n), ref _noise);
            for (var i = 0; i < n; i++)
            {
                var a = MathF.Abs(_bKick[i]);
                kickPeak = MathF.Max(kickPeak, a);
                if (done + i >= half - 64)
                    kickTail = MathF.Max(kickTail, a);
            }
            done += n;
        }
        _bass.NoteOn(Hz(_track.Chords[0], 0), 1f);
        var bassPeak = 0f;
        var bassTail = 0f;
        for (var done = 0; done < half;)
        {
            var n = Math.Min(MaxChunk, half - done);
            _bass.Render(_bBass.AsSpan(0, n));
            for (var i = 0; i < n; i++)
            {
                var a = MathF.Abs(_bBass[i]);
                bassPeak = MathF.Max(bassPeak, a);
                if (done + i >= half - 64)
                    bassTail = MathF.Max(bassTail, a);
            }
            done += n;
        }
        Load(track, 1);
        return (kickPeak > 0 ? kickTail / kickPeak : 1f, bassPeak > 0 ? bassTail / bassPeak : 1f);
    }

    /// <summary>The lowest gain the master limiter has applied since the load (1 = never limited; hardbass only).</summary>
    public float LimiterMinGain => _limiter.MinGain;
}
