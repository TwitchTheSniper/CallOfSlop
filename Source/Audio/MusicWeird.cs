namespace CallOfSlop;

// The "weird" hardbass tracks: the hardbass core (pumping kick, off-beat bass, chants) with odd meters, tempo ramps,
// a drunk half-time breakdown, lo-fi and music-box swerves, microtonal scales, accordion and 8-bit leads and toy sound
// effects. The recipe is a WeirdFlavor on the track; the timeline below turns its section codes into bar lengths and
// tempos, used both by the renderer (MusicRenderer.Weird.cs) and by MusicTrack.Seconds, so the two always agree.

/// <summary>A lead sound that can take over the hardbass lead line in a weird track.</summary>
public enum WeirdLead : byte
{
    None, // the track's normal hardbass lead
    Accordion, // a polka accordion: three detuned reeds playing the melody with a third below
    Chip, // an 8-bit pulse lead
    MusicBox, // plucked music-box tines
}

/// <summary>When a weird lead takes over.</summary>
public enum WeirdUse : byte
{
    Always, // everywhere the lead plays (builds, breaks, drops)
    Drops,
    LaterDrops, // the second and later drops
    Alternate, // every other four bars of every drop
}

/// <summary>
/// The weird part of a hardbass recipe. <see cref="Sections"/> holds one code per section of the track's form:
/// '.' plain 4/4, '7' bars of 7/8 (14 16ths), '5' bars of 5/4 (the last beat comes round again), 'u' speeds up from
/// 0.8x to the track tempo, 'r' an accelerando from the tempo to 1.12x, and for breaks only: 'd' a drunk half-time
/// breakdown (slows to 0.55x with tape wobble and sagging pitch, then speeds back up), 'l' a few bars of dusty lo-fi
/// (0.6x, swung, crackle and bitcrush) and 'm' a music-box interlude that winds down before the drop.
/// <see cref="Sfx"/> is 32 16ths over two bars, played in every other four bars of the drops: '.' none, 'q' rubber
/// duck quack, 's' squeaky toy, 'w' slide whistle up, 'v' slide whistle down, 'b' boing.
/// </summary>
public sealed record WeirdFlavor(
    string Sections,
    WeirdLead Lead = WeirdLead.None,
    WeirdUse LeadUse = WeirdUse.Drops,
    bool ChipArp = false,
    string Sfx = "",
    char ExtraBeatSfx = '.',
    bool WhistleIntoDrops = false,
    float ChantSemis = 0f,
    float ChantSemisAlt = 0f,
    float ChantCrush = 0f,
    float[]? Micro = null,
    float HarmonyCents = 0f)
{
    /// <summary>The section codes the timeline understands.</summary>
    public const string Codes = ".75urdlm";

    /// <summary>Codes that replace a whole section's arrangement (only allowed on breaks).</summary>
    public const string BreakCodes = "dlm";
}

/// <summary>
/// Bar lengths and tempos of a weird track: how many 16ths each bar has (14, 16 or 20) and how long each 16th lasts.
/// Built once per track; lookups don't allocate.
/// </summary>
public sealed class WeirdTimeline
{
    private readonly char[] _code;
    private readonly byte[] _steps;
    private readonly int[] _barIn, _secBars;
    private readonly double[] _start;
    private readonly double _stepSeconds;

    public WeirdTimeline(MusicTrack track, WeirdFlavor flavor)
    {
        var bars = track.TotalBars + MusicTrack.TailBars;
        _code = new char[bars];
        _steps = new byte[bars];
        _barIn = new int[bars];
        _secBars = new int[bars];
        _start = new double[bars + 1];
        _stepSeconds = 60.0 / track.Bpm / 4.0;
        var bar = 0;
        for (var s = 0; s < track.Sections.Length; s++)
        {
            var code = s < flavor.Sections.Length ? flavor.Sections[s] : '.';
            if (!WeirdFlavor.Codes.Contains(code))
                code = '.';
            var len = track.Sections[s].Bars;
            SteadyDrops &= track.Sections[s].Part != MusicPart.Drop || code == '.';
            for (var b = 0; b < len; b++, bar++)
            {
                _code[bar] = code;
                _steps[bar] = (byte)(code == '7' ? 14 : code == '5' ? 20 : 16);
                _barIn[bar] = b;
                _secBars[bar] = len;
            }
        }
        for (; bar < bars; bar++)
        {
            _code[bar] = '.';
            _steps[bar] = 16;
            _barIn[bar] = 0;
            _secBars[bar] = 1;
        }
        for (var b = 0; b < bars; b++)
        {
            var sum = 0.0;
            for (var v = 0; v < _steps[b]; v++)
                sum += StepSeconds(b, v);
            _start[b + 1] = _start[b] + sum;
        }
    }

    /// <summary>
    /// True when every drop is plain 4/4 at the track's tempo: the kill-frenzy layer (which loops drops on bar lines of
    /// a fixed length) only picks tracks like that.
    /// </summary>
    public bool SteadyDrops { get; } = true;

    /// <summary>Length of the track including the tail.</summary>
    public double Seconds => _start[^1];

    /// <summary>16ths in a bar: 14 (7/8), 16 or 20 (5/4).</summary>
    public int Steps(int bar) => bar >= 0 && bar < _steps.Length ? _steps[bar] : 16;

    /// <summary>The section code of a bar ('.' outside the track).</summary>
    public char Code(int bar) => bar >= 0 && bar < _code.Length ? _code[bar] : '.';

    /// <summary>When a bar starts, in seconds from the top.</summary>
    public double BarStart(int bar) => _start[Math.Clamp(bar, 0, _start.Length - 1)];

    /// <summary>The bar playing at a time.</summary>
    public int BarAt(double seconds)
    {
        int lo = 0, hi = _start.Length - 1;
        while (hi - lo > 1)
        {
            var mid = (lo + hi) >> 1;
            if (_start[mid] <= seconds)
                lo = mid;
            else
                hi = mid;
        }
        return seconds >= _start[^1] ? _start.Length - 1 : lo;
    }

    private double Progress(int bar, int v) => (_barIn[bar] + v / (double)_steps[bar]) / _secBars[bar];

    private static double Smooth(double x)
    {
        x = Math.Clamp(x, 0, 1);
        return x * x * (3 - 2 * x);
    }

    /// <summary>Tempo multiplier at 16th v of a bar.</summary>
    public double Tempo(int bar, int v)
    {
        if (bar < 0 || bar >= _code.Length)
            return 1;
        switch (_code[bar])
        {
            case 'd':
            {
                var p = Progress(bar, v);
                return p < 0.15 ? 1 - 0.45 * Smooth(p / 0.15) : p < 0.7 ? 0.55 : 0.55 + 0.45 * Smooth((p - 0.7) / 0.3);
            }
            case 'l':
                return 0.6;
            case 'm':
                return _barIn[bar] == _secBars[bar] - 1 ? 0.8 - 0.3 * v / 16.0 : 0.8;
            case 'u':
                return 0.8 + 0.2 * Progress(bar, v);
            case 'r':
                return 1 + 0.12 * Progress(bar, v);
            default:
                return 1;
        }
    }

    /// <summary>Length of 16th v of a bar in seconds (the lo-fi bars swing: long-short pairs).</summary>
    public double StepSeconds(int bar, int v)
    {
        var s = _stepSeconds / Tempo(bar, v);
        if (Code(bar) == 'l')
            s *= (v & 1) == 0 ? 1.16 : 0.84;
        return s;
    }

    /// <summary>Pitch multiplier at 16th v: the drunk breakdown sags with its tempo, the music box sinks as it winds down.</summary>
    public float Pitch(int bar, int v)
    {
        var c = Code(bar);
        if (c == 'd')
            return (float)Math.Pow(Tempo(bar, v), 0.3);
        if (c == 'm' && _barIn[bar] == _secBars[bar] - 1)
            return (float)Math.Pow(Tempo(bar, v) / 0.8, 0.5);
        return 1f;
    }
}
