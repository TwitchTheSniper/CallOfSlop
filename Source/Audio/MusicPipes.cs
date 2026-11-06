namespace CallOfSlop;

/// <summary>
/// A tune for the Highland bagpipe (docs/audio/MUSIC.md, "Pipe tunes"): the Clan MacSlop's vehicles play these from their Pipe Boxes
/// (docs/sandbox/MEDIA.md, "Pipe Boxes"). Written out note by note, the way pipers write them, rather than from a seed: a pipe tune is
/// its melody. The renderer is <see cref="PipeRenderer"/>.
/// <para>
/// Notes are the chanter's nine, in the pipers' own letters: <c>G A B c d e f g a</c> (low G to high A, A mixolydian, tuned just against
/// the drones). A digit after a note is its length in units (one by default); a unit is <see cref="UnitsPerBeat"/> to the beat.
/// <c>|</c> is a bar line, and a part's bars must each add up to a bar. Before a note: <c>^</c> a doubling, <c>~</c> a birl (low A only),
/// <c>!</c> no grace note. Every other note on a beat, and every repeated note, gets a G grace note by itself: a chanter never stops
/// sounding, so grace notes are the only way to separate one note from the next.
/// </para>
/// </summary>
public sealed class PipeTune
{
    /// <summary>Low A on the chanter. Pipes are tuned sharp of concert A (466 Hz is B flat): about 477 today.</summary>
    public const float LowA = 477f;

    /// <summary>The chanter's notes over low A: low G 7/8, then just intervals up to high A.</summary>
    public static readonly float[] Ratio = [7f / 8f, 1f, 9f / 8f, 5f / 4f, 4f / 3f, 3f / 2f, 5f / 3f, 7f / 4f, 2f];

    public const string Letters = "GABcdefga";
    private const int LowG = 0, LowAIdx = 1, HighG = 7, HighA = 8, E = 5;

    /// <summary>How long a grace note sounds.</summary>
    public const float GraceSeconds = 0.024f;

    /// <summary>When the bag is empty and the drones start to die, after the tune's last note has been held a bar.</summary>
    public readonly double BagEmpty;

    /// <summary>The chanter comes in on its long E, this long after the drones strike in.</summary>
    public const double ChanterIn = 0.9;

    /// <summary>How long the drones take to die away (with the groan every piper knows).</summary>
    public const double DieSeconds = 0.9;

    public string Name { get; }

    /// <summary>"2/4 march", "6/8 jig"...: shown in the catalogue.</summary>
    public string Kind { get; }

    /// <summary>Beats a minute, the beat as the tune counts it (a dotted crotchet in 6/8).</summary>
    public float Bpm { get; }

    public int UnitsPerBeat { get; }
    public int BeatsPerBar { get; }

    /// <summary>A pipe band's snare and bass drum join from the second time through. A slow air has none.</summary>
    public bool Drums { get; }

    public double UnitSeconds => 60.0 / Bpm / UnitsPerBeat;
    public double BarSeconds => UnitSeconds * UnitsPerBeat * BeatsPerBar;

    /// <summary>Bars from the top to the bag going empty: two of drones and the long E, the tune, and a bar of its last note.</summary>
    public int Bars { get; }

    // The chanter's notes and the drums, in seconds from the top, made once here so a renderer only reads them.
    internal readonly float[] NoteAt;
    internal readonly byte[] NotePitch;
    internal readonly float[] DrumAt;
    internal readonly byte[] DrumKind; // 0 snare, 1 bass drum
    internal readonly float[] DrumVel;

    /// <param name="parts">The parts (A, B...), each a string of notes.</param>
    /// <param name="form">Parts in order by letter, e.g. "AABB"; the whole form is played <paramref name="times"/> times.</param>
    public PipeTune(string name, string kind, float bpm, int beatsPerBar, int unitsPerBeat, bool drums, string[] parts, string form = "AABB", int times = 2)
    {
        Name = name;
        Kind = kind;
        Bpm = bpm;
        BeatsPerBar = beatsPerBar;
        UnitsPerBeat = unitsPerBeat;
        Drums = drums;
        var barUnits = beatsPerBar * unitsPerBeat;
        var notes = new List<(float At, byte Pitch)>();
        var drumList = new List<(float At, byte Kind, float Vel)>();
        var unit = UnitSeconds;
        var bar = BarSeconds;
        notes.Add(((float)ChanterIn, E)); // (the long E every tune starts on)
        var top = 2 * bar;
        long u = 0;
        var prev = E;
        var tuneBars = 0;
        for (var pass = 0; pass < times; pass++)
            for (var f = 0; f < form.Length; f++)
            {
                var part = Parse(parts[form[f] - 'A'], barUnits, name);
                var partBars = part.Sum(n => n.Units) / barUnits;
                if (drums)
                    DrumPart(drumList, top + u * unit, partBars, pass, f == form.Length - 1, unit, unitsPerBeat, beatsPerBar);
                foreach (var n in part)
                {
                    var at = top + u * unit;
                    var dur = n.Units * unit;
                    Span<byte> graces = stackalloc byte[3];
                    var g = 0;
                    if (n.Mark == '~' && n.Pitch == LowAIdx)
                    {
                        graces[g++] = LowG;
                        graces[g++] = LowAIdx;
                        graces[g++] = LowG;
                    }
                    else if (n.Mark == '^')
                    {
                        graces[g++] = n.Pitch >= HighG ? (byte)(n.Pitch == HighG ? HighA : HighG) : (byte)HighG;
                        graces[g++] = n.Pitch;
                        graces[g++] = Upper(n.Pitch);
                    }
                    else if (n.Mark != '!' && (u % unitsPerBeat == 0 || n.Pitch == prev))
                        graces[g++] = n.Pitch == HighG ? (byte)HighA : (byte)HighG;
                    var gd = Math.Min(GraceSeconds, dur * 0.45 / Math.Max(1, g));
                    for (var k = 0; k < g; k++)
                        notes.Add(((float)(at + k * gd), graces[k]));
                    notes.Add(((float)(at + g * gd), n.Pitch));
                    prev = n.Pitch;
                    u += n.Units;
                }
                tuneBars += partBars;
            }
        var end = top + u * unit; // (every tune ends home on low A, held a bar, whatever its last note led into)
        notes.Add(((float)end, HighG));
        notes.Add(((float)(end + GraceSeconds), LowAIdx));
        Bars = 2 + tuneBars + 1;
        BagEmpty = Bars * bar;
        NoteAt = notes.Select(n => n.At).ToArray();
        NotePitch = notes.Select(n => n.Pitch).ToArray();
        drumList.Sort((a, b) => a.At.CompareTo(b.At));
        DrumAt = drumList.Select(d => d.At).ToArray();
        DrumKind = drumList.Select(d => d.Kind).ToArray();
        DrumVel = drumList.Select(d => d.Vel).ToArray();
    }

    /// <summary>The grace note a doubling ends on, above its note.</summary>
    private static byte Upper(byte pitch) => pitch switch
    {
        LowG or LowAIdx or 2 or 3 => 4, // (G, A, B and C double on D)
        HighA => HighG,
        _ => (byte)(pitch + 1),
    };

    private readonly record struct Note(byte Pitch, int Units, char Mark);

    private static List<Note> Parse(string s, int barUnits, string tune)
    {
        var list = new List<Note>();
        var mark = ' ';
        var inBar = 0;
        for (var i = 0; i < s.Length; i++)
        {
            var ch = s[i];
            if (ch == ' ')
                continue;
            if (ch == '|')
            {
                if (inBar != 0 && inBar != barUnits)
                    throw new InvalidOperationException($"pipe tune \"{tune}\": a bar of {inBar} units, not {barUnits}, before column {i}");
                inBar = 0;
                continue;
            }
            if (ch is '^' or '~' or '!')
            {
                mark = ch;
                continue;
            }
            var pitch = Letters.IndexOf(ch);
            if (pitch < 0)
                throw new InvalidOperationException($"pipe tune \"{tune}\": '{ch}' at column {i} isn't a chanter note ({Letters})");
            var units = 0;
            while (i + 1 < s.Length && char.IsAsciiDigit(s[i + 1]))
                units = units * 10 + (s[++i] - '0');
            units = Math.Max(1, units);
            list.Add(new Note((byte)pitch, units, mark));
            inBar += units;
            mark = ' ';
        }
        if (inBar != 0 && inBar != barUnits)
            throw new InvalidOperationException($"pipe tune \"{tune}\": its last bar is {inBar} units, not {barUnits}");
        return list;
    }

    /// <summary>
    /// A pipe band's drums under one part: the bass drum on every beat, the snare's flams, taps and rolls by the bar from the tune's
    /// metre. The first time through only the bass drum plays (softly, on the bar), with the snare's roll into the second time.
    /// </summary>
    private static void DrumPart(List<(float, byte, float)> list, double start, int bars, int pass, bool lastPart, double unit, int upb, int bpb)
    {
        var units = upb * bpb;
        var patterns = (bpb, upb) switch
        {
            (2, 4) => new[] { "F.x.F.x.", "F.xxF.rr", "F.x.FxF.", "rrrrF.x." },
            (2, 3) => new[] { "F.xF.x", "FxxF.x", "F.xFxx", "rrrF.." },
            (4, 4) => new[] { "F..xF..xF..xF.x.", "F..xF.rrF..xF...", "F.x.F..xF.x.F.xx", "rrrrrrrrF...F..." },
            _ => new[] { "F" + new string('.', units - 1) },
        };
        for (var b = 0; b < bars; b++)
        {
            var barAt = start + b * units * unit;
            var last = b == bars - 1;
            for (var beat = 0; beat < bpb; beat++)
                if (pass > 0 || beat == 0)
                    list.Add(((float)(barAt + beat * upb * unit), 1, pass == 0 ? 0.45f : beat == 0 ? 0.9f : 0.6f));
            string pat;
            if (pass == 0)
            {
                if (!(last && lastPart))
                    continue;
                pat = new string('r', units - 1) + "."; // (the roll in)
            }
            else
                pat = patterns.Length < 4 ? patterns[0] : last ? patterns[3] : patterns[b % 3];
            for (var k = 0; k < pat.Length && k < units; k++)
            {
                var at = barAt + k * unit;
                switch (pat[k])
                {
                    case 'F':
                        list.Add(((float)(at - 0.016), 0, 0.35f));
                        list.Add(((float)at, 0, 1f));
                        break;
                    case 'x':
                        list.Add(((float)at, 0, 0.6f));
                        break;
                    case 'r':
                        for (var t = 0.0; t < unit - 0.004; t += 0.021)
                            list.Add(((float)(at + t), 0, 0.42f));
                        break;
                }
            }
        }
    }

    /// <summary>The <see cref="MusicTrack"/> a Music Player or the menu's music player plays it by. Its bars are the tune's bars.</summary>
    internal MusicTrack AsTrack(uint seed) =>
        new(Name, MusicSet.Lobby, MusicStyle.Pipes, (float)(240.0 / BarSeconds), 45, "A mixolydian", [0, 2, 4, 5, 7, 9, 10], [0], seed, $"D{Bars}") { Pipes = this };
}

/// <summary>The pipe tunes, every one written for the Clan MacSlop (docs/audio/MUSIC.md, "Pipe tunes"). Original melodies, in the old forms.</summary>
public static class PipeTunes
{
    public static readonly PipeTune[] Tunes =
    [
        new("The Haggis Has Landed", "2/4 march", 104, 2, 4, true,
        [
            "^A3 B1 c2 A2 | e3 f1 e2 c2 | d3 e1 f2 d2 | c3 B1 A2 B2 | ^A3 B1 c2 A2 | e3 f1 g2 a2 | f3 e1 c2 B2 | ~A6 e2",
            "a3 g1 f2 e2 | f3 e1 c2 e2 | a3 g1 f2 a2 | ^g3 f1 e2 d2 | ^e3 f1 g2 e2 | a3 e1 c2 A2 | B3 c1 d2 B2 | ~A6 e2",
        ]),
        new("Kilts at Full Throttle", "6/8 jig", 112, 2, 3, true,
        [
            "A2 B1 c2 A1 | e2 c1 e2 f1 | g2 e1 d2 B1 | G2 B1 d2 B1 | A2 B1 c2 e1 | a2 f1 e2 c1 | B2 G1 B1 c1 d1 | c2 A1 ~A3",
            "a2 e1 a2 e1 | f2 e1 c2 e1 | g2 d1 g2 d1 | e2 d1 B2 G1 | a2 e1 a1 g1 f1 | e2 c1 A2 c1 | B2 d1 g2 B1 | ~A3 A3",
        ]),
        new("Lament for a Flat Tyre", "3/4 slow air", 60, 3, 2, false,
        [
            "^e4 c2 | B3 A1 B2 | ^c4 e2 | ^f6 | ^e3 f1 e2 | c3 B1 A2 | ^B3 c1 B2 | ~A6",
            "^a4 g2 | f3 e1 f2 | ^g4 e2 | ^d6 | ^e3 f1 g2 | a3 f1 e2 | ^c3 B1 c2 | ~A6",
        ], "AB"),
        new("Midges on the Windscreen", "4/4 strathspey", 120, 4, 4, true,
        [
            "^A3 c1 e3 A1 B1 c3 d3 B1 | c1 A3 e3 c1 d3 B1 G4 | ^A3 c1 e3 a1 f1 e3 c3 e1 | f1 e3 c3 B1 ~A8",
            "a3 f1 e3 c1 a1 f3 e3 c1 | d3 B1 g3 B1 d1 B3 G4 | a3 f1 e3 c1 a1 g3 f3 e1 | c1 e3 B3 c1 ~A8",
        ]),
    ];

    /// <summary>Their tracks, made once: <see cref="MediaEntry.Track"/> and the catalogue find them here, never in <see cref="MusicTracks.All"/>.</summary>
    public static readonly MusicTrack[] Tracks = Tunes.Select((t, i) => t.AsTrack(0x9A1B_0000u + (uint)i)).ToArray();
}

/// <summary>
/// Plays a <see cref="PipeTune"/>: three drones (two tenors a hair apart, so they beat, and a bass) that strike in with a growl and die with
/// a groan, a chanter that never stops sounding, with its grace notes, and a pipe band's snare and bass drum. Everything is a function of
/// the time from the top or of state reset in <see cref="Load"/>, so a joiner starts mid-tune at a bar line. Allocation-free.
/// </summary>
public sealed class PipeRenderer
{
    private const int Sr = Audio.SampleRate;

    private PipeTune? _tune;
    private double _length;
    private long _n;
    private int _note, _drum;
    private float _hz, _cur;
    private float _phC, _phT1, _phT2, _phB, _phBd, _phSn;
    private float _hpX, _hpY, _bpLow, _bpBand, _lpC, _lpT1, _lpT2, _lpB, _snHp;
    private float _snEnv, _snTone, _bdEnv, _bdPitch, _noiseLast;
    private MusicRng _rng = new(1);

    public bool Finished { get; private set; }

    public double Remaining => Math.Max(0, _length - _n / (double)Sr);

    /// <summary>From the top, or from a bar of a track whose bars are <paramref name="barSeconds"/> long (a joiner).</summary>
    public void Load(PipeTune tune, uint seed, int startBar, double barSeconds, double length)
    {
        _tune = tune;
        _length = length;
        _n = (long)(Math.Max(0, startBar) * barSeconds * Sr);
        var t = _n / (double)Sr;
        _note = 0;
        while (_note + 1 < tune.NoteAt.Length && tune.NoteAt[_note + 1] <= t)
            _note++;
        _hz = _cur = PipeTune.LowA * PipeTune.Ratio[tune.NotePitch[_note]];
        _drum = 0;
        while (_drum < tune.DrumAt.Length && tune.DrumAt[_drum] < t)
            _drum++;
        _phC = _phT1 = _phT2 = _phB = _phBd = _phSn = 0;
        _phT2 = 0.37f; // (the second tenor starts out of step with the first)
        _hpX = _hpY = _bpLow = _bpBand = _lpC = _lpT1 = _lpT2 = _lpB = _snHp = 0;
        _snEnv = _snTone = _bdEnv = _bdPitch = _noiseLast = 0;
        _rng = new MusicRng(seed * 2654435761u + 91);
        Finished = t >= length;
    }

    private static float Smooth(double x) => x <= 0 ? 0 : x >= 1 ? 1 : (float)(x * x * (3 - 2 * x));

    private static float Blep(float t, float dt)
    {
        if (t < dt)
        {
            t /= dt;
            return t + t - t * t - 1;
        }
        if (t > 1 - dt)
        {
            t = (t - 1) / dt;
            return t * t + t + t + 1;
        }
        return 0;
    }

    /// <summary>A band-limited saw, -1..1.</summary>
    private static float Saw(float ph, float dt) => 2 * ph - 1 - Blep(ph, dt);

    /// <summary>A band-limited pulse of duty <paramref name="duty"/>.</summary>
    private static float Pulse(float ph, float dt, float duty)
    {
        var p2 = ph + duty;
        if (p2 >= 1)
            p2 -= 1;
        return Saw(ph, dt) - Saw(p2, dt);
    }

    private static void Step(ref float ph, float dt)
    {
        ph += dt;
        if (ph >= 1)
            ph -= 1;
    }

    /// <summary>Overwrites an interleaved stereo buffer with the next frames.</summary>
    public void Render(Span<float> stereo)
    {
        var frames = stereo.Length / 2;
        if (_tune is not { } tune || Finished)
        {
            stereo.Clear();
            return;
        }
        // Coefficients (one-pole: 1 - e^(-2 pi f / Sr)).
        const float glide = 0.016f;
        const float hpA = 0.957f; // (about 300 Hz)
        const float lpC = 0.6f, lpT = 0.22f, lpB = 0.1f; // (about 6.5 kHz, 1.7 kHz, 750 Hz)
        const float bpF = 2 * 0.1205f; // (2 sin(pi 1700 / Sr): the chanter's nasal peak)
        const float bpQ = 0.4f;
        var snDecay = MathF.Exp(-1f / (0.045f * Sr));
        var toneDecay = MathF.Exp(-1f / (0.02f * Sr));
        var bdDecay = MathF.Exp(-1f / (0.3f * Sr));
        var bdPitchDecay = MathF.Exp(-1f / (0.03f * Sr));
        var tenor = PipeTune.LowA / 2;
        var bass = PipeTune.LowA / 4;
        var noteAt = tune.NoteAt;
        var drumAt = tune.DrumAt;
        for (var i = 0; i < frames; i++)
        {
            var t = _n / (double)Sr;
            while (_note + 1 < noteAt.Length && noteAt[_note + 1] <= t)
                _hz = PipeTune.LowA * PipeTune.Ratio[tune.NotePitch[++_note]];
            while (_drum < drumAt.Length && drumAt[_drum] <= t)
            {
                var vel = tune.DrumVel[_drum] * (0.9f + 0.2f * _rng.Unit());
                if (tune.DrumKind[_drum] == 0)
                {
                    _snEnv = MathF.Max(_snEnv, vel);
                    _snTone = MathF.Max(_snTone, vel);
                }
                else
                {
                    _bdEnv = vel;
                    _bdPitch = 1;
                }
                _drum++;
            }

            // The bag: drones strike in, rising to pitch with a growl, and die with a groan once it's empty.
            var die = t - tune.BagEmpty;
            var pitch = 0.82f + 0.18f * Smooth(t / 0.6);
            var amp = Smooth(t / 0.25) * (1 - 0.35f * MathF.Exp(-(float)t / 0.3f) * (0.5f + 0.5f * MathF.Sin(2 * MathF.PI * 17 * (float)t)));
            var chanterAmp = Smooth((t - PipeTune.ChanterIn) / 0.015);
            if (die > 0)
            {
                pitch *= 1 - 0.3f * Smooth(die / PipeTune.DieSeconds);
                amp *= 1 - Smooth(die / PipeTune.DieSeconds);
                chanterAmp *= 1 - Smooth(die / (PipeTune.DieSeconds * 0.5));
            }

            // The drones: squares with some saw, darkened.
            float dt;
            dt = tenor * pitch * 1.0004f / Sr;
            Step(ref _phT1, dt);
            var t1 = 0.6f * Pulse(_phT1, dt, 0.5f) + 0.4f * Saw(_phT1, dt);
            dt = tenor * pitch * 0.9997f / Sr;
            Step(ref _phT2, dt);
            var t2 = 0.6f * Pulse(_phT2, dt, 0.5f) + 0.4f * Saw(_phT2, dt);
            dt = bass * pitch / Sr;
            Step(ref _phB, dt);
            var b = 0.6f * Pulse(_phB, dt, 0.5f) + 0.4f * Saw(_phB, dt);
            _lpT1 += lpT * (t1 - _lpT1);
            _lpT2 += lpT * (t2 - _lpT2);
            _lpB += lpB * (b - _lpB);

            // The chanter: bright and nasal, a saw and a narrow pulse through a peak near 1.7 kHz. It glides between notes in a moment.
            _cur += glide * (_hz - _cur);
            dt = _cur * (die > 0 ? pitch * pitch : 1f) / Sr;
            Step(ref _phC, dt);
            var c = 0.55f * Saw(_phC, dt) + 0.45f * Pulse(_phC, dt, 0.3f);
            var hp = hpA * (_hpY + c - _hpX);
            _hpX = c;
            _hpY = hp;
            _bpLow += bpF * _bpBand;
            var high = hp - _bpLow - bpQ * _bpBand;
            _bpBand += bpF * high;
            var shaped = 0.55f * hp + 0.9f * _bpBand;
            _lpC += lpC * (shaped - _lpC);

            // The drums: a tight, crisp pipe band snare and a soft bass drum.
            var noise = _rng.Noise();
            var bright = noise - _noiseLast;
            _noiseLast = noise;
            _snHp += 0.5f * (bright - _snHp);
            Step(ref _phSn, 340f / Sr);
            var snare = _snEnv * _snHp * 0.9f + _snTone * 0.35f * MathF.Sin(2 * MathF.PI * _phSn);
            _snEnv *= snDecay;
            _snTone *= toneDecay;
            Step(ref _phBd, (50f + 45f * _bdPitch) / Sr);
            var bassDrum = _bdEnv * MathF.Sin(2 * MathF.PI * _phBd);
            _bdEnv *= bdDecay;
            _bdPitch *= bdPitchDecay;

            var drones = amp * (0.1f * _lpT1 + 0.1f * _lpT2 + 0.16f * _lpB);
            var chanter = amp * chanterAmp * 0.34f * _lpC;
            var drums = 0.3f * snare + 0.5f * bassDrum;
            var l = chanter + drones + 0.03f * amp * _lpT2 + drums;
            var r = chanter + drones + 0.03f * amp * _lpT1 + drums;
            stereo[i * 2] = MathF.Tanh(l * 1.45f) * 0.9f;
            stereo[i * 2 + 1] = MathF.Tanh(r * 1.45f) * 0.9f;
            _n++;
        }
        if (MathF.Abs(_snEnv) < 1e-15f) _snEnv = 0;
        if (MathF.Abs(_snTone) < 1e-15f) _snTone = 0;
        if (MathF.Abs(_bdEnv) < 1e-15f) _bdEnv = 0;
        if (MathF.Abs(_snHp) < 1e-15f) _snHp = 0;
        if (MathF.Abs(_bpLow) < 1e-15f) _bpLow = 0;
        if (MathF.Abs(_bpBand) < 1e-15f) _bpBand = 0;
        if (MathF.Abs(_hpY) < 1e-15f) _hpY = 0;
        if (MathF.Abs(_lpC) < 1e-15f) _lpC = 0;
        if (MathF.Abs(_lpT1) < 1e-15f) _lpT1 = 0;
        if (MathF.Abs(_lpT2) < 1e-15f) _lpT2 = 0;
        if (MathF.Abs(_lpB) < 1e-15f) _lpB = 0;
        Finished = _n / (double)Sr >= _length;
    }
}
