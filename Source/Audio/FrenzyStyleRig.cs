namespace CallOfSlop;

/// <summary>
/// Plays one weapon class's frenzy character (a <see cref="FrenzyStyle"/>) on top of the rendered hardbass track:
/// effects on the track (drive, bit crusher, beat-repeat stutters, slap-back echo), then the style's instrument layers
/// sequenced on the track's own 16ths (the renderer's step log) and following its chords, with a reverb for the layers
/// that send to it, all times the style's trim. Each layer comes in at its frenzy level (fading in over half a bar), so
/// the levels still build up inside the class character. Every voice and buffer is made in the constructor:
/// <see cref="Process"/> never allocates.
/// </summary>
public sealed class FrenzyStyleRig
{
    /// <summary>Most instrument layers a style may have.</summary>
    public const int MaxLayers = 6;

    private const int MaxChunk = SawSynth.MaxBlock;
    private const int Ring = 8192; // stutter and slap-back buffers (frames): more than a 16th at any frenzy tempo, and 180 ms

    public readonly WeaponClass Class;
    public readonly FrenzyStyle Style;

    private readonly Layer[] _layers;
    private readonly bool _hasVerb;
    private readonly HalfRateReverb? _verb;
    private readonly float[] _vL = new float[MaxChunk], _vR = new float[MaxChunk], _scratch = new float[MaxChunk];
    private readonly float[] _verbIn = new float[MaxChunk], _verbL = new float[MaxChunk], _verbR = new float[MaxChunk];
    private readonly float[] _ringL = new float[Ring], _ringR = new float[Ring], _slapL = new float[Ring], _slapR = new float[Ring];
    private MusicRng _rng;
    private readonly uint _seed;
    private int _ringPos, _slapPos, _crushCount, _stutLeft, _stutGrain, _stutT, _stutStart;
    private float _crushL, _crushR;

    /// <summary>True when the style leaves the track untouched.</summary>
    public bool Plain => Style.IsPlain;

    public FrenzyStyleRig(WeaponClass cls, FrenzyStyle style)
    {
        Class = cls;
        Style = style;
        _seed = 0xF2E1u + (uint)cls * 7919u;
        _rng = new MusicRng(_seed);
        _layers = style.Layers.Take(MaxLayers).Select(s => new Layer(s)).ToArray();
        _hasVerb = _layers.Any(l => l.Spec.Reverb > 0);
        if (_hasVerb)
            _verb = new HalfRateReverb(0.86f, 0.3f, MaxChunk);
    }

    /// <summary>A new frenzy (or this style taking over): silence every voice and effect, tune to the track.</summary>
    public void Reset(MusicRenderer r, int level)
    {
        _rng = new MusicRng(_seed);
        foreach (var l in _layers)
        {
            l.Reset();
            l.Gate = level >= l.Spec.From ? 1f : 0f;
            if (l.Kick != null && l.Spec.Voice == FrenzyVoice.Gabber)
            {
                // The gabber kick lands on the track's key note.
                var hz = r.ClassHz(0, 0);
                while (hz > 90f)
                    hz *= 0.5f;
                while (hz < 45f)
                    hz *= 2f;
                l.Kick.EndHz = hz;
            }
        }
        _verb?.Reset();
        Array.Clear(_ringL);
        Array.Clear(_ringR);
        Array.Clear(_slapL);
        Array.Clear(_slapR);
        _ringPos = _slapPos = _crushCount = _stutLeft = _stutT = 0;
        _crushL = _crushR = 0;
    }

    /// <summary>
    /// Renders the style for one segment: <paramref name="dry"/> is the track (interleaved stereo, the renderer's last
    /// <see cref="MusicRenderer.Render"/> call, whose step log is still in <paramref name="r"/>); the result goes to
    /// <paramref name="output"/> (overwritten). <paramref name="level"/> is the frenzy level playing.
    /// </summary>
    public void Process(ReadOnlySpan<float> dry, Span<float> output, int frames, MusicRenderer r, int level, double framesPerBar)
    {
        if (Plain)
        {
            dry[..(frames * 2)].CopyTo(output);
            return;
        }
        var gateStep = 1f / (float)Math.Max(1.0, framesPerBar * 0.5);
        var log = 0;
        var pos = 0;
        while (pos < frames)
        {
            while (log < r.StepLogCount && r.StepLogFrame[log] <= pos)
            {
                Step(r, log, level);
                log++;
            }
            foreach (var l in _layers)
                if (l.Pending >= 0 && l.Pending <= pos)
                {
                    l.Pending = -1;
                    if (l.Gate > 0)
                        l.Hit(0.55f);
                }
            var next = Math.Min(frames, pos + MaxChunk);
            if (log < r.StepLogCount)
                next = Math.Min(next, Math.Max(pos + 1, r.StepLogFrame[log]));
            foreach (var l in _layers)
                if (l.Pending > pos)
                    next = Math.Min(next, l.Pending);
            Chunk(dry, output, pos, next - pos, level, gateStep);
            pos = next;
        }
        foreach (var l in _layers)
            if (l.Pending >= 0)
                l.Pending = Math.Max(0, l.Pending - frames);
    }

    private void Step(MusicRenderer r, int i, int level)
    {
        var step = r.StepLogStep[i];
        var chord = r.StepLogChord[i];
        var sps = r.StepLogSamples[i];
        var frame = r.StepLogFrame[i];
        // Stutter: this 16th repeats its first 64th ('s') or 128th ('S').
        var st = Style.Fx.Stutter;
        if (st.Length > 0)
        {
            var c = st[(step % st.Length + st.Length) % st.Length];
            if (c is 's' or 'S')
            {
                _stutGrain = Math.Max(64, (int)(sps / (c == 's' ? 4f : 8f)));
                _stutLeft = (int)sps;
                _stutT = 0;
                _stutStart = _ringPos;
            }
            else
            {
                _stutLeft = 0;
            }
        }
        foreach (var l in _layers)
        {
            var on = level >= l.Spec.From;
            if (!on && l.Gate <= 0)
                continue;
            var p = l.Spec.Pattern;
            var ch = p[(step % p.Length + p.Length) % p.Length];
            switch (ch)
            {
                case '-':
                    break;
                case '.':
                    l.Off();
                    break;
                default:
                {
                    var vel = ch switch { 'X' => 1f, 'g' => 0.4f, 'r' => 0.7f, _ => 0.82f };
                    var deg = ch switch { >= '0' and <= '9' => ch - '0', 'z' => _rng.Range(8), _ => 0 };
                    if (!on)
                        break; // fading out: no new notes
                    l.Note(r, chord + deg, vel);
                    if (ch == 'r')
                        l.Pending = frame + (int)(sps * 0.5f);
                    break;
                }
            }
        }
    }

    private void Chunk(ReadOnlySpan<float> dry, Span<float> output, int start, int n, int level, float gateStep)
    {
        if (n <= 0)
            return;
        Effects(dry, output, start, n);

        var verbIn = _verbIn.AsSpan(0, n);
        if (_hasVerb)
            verbIn.Clear();
        var vl = _vL.AsSpan(0, n);
        var vr = _vR.AsSpan(0, n);
        foreach (var l in _layers)
        {
            var want = level >= l.Spec.From ? 1f : 0f;
            var g0 = l.Gate;
            var g1 = want > g0 ? MathF.Min(1f, g0 + gateStep * n) : MathF.Max(0f, g0 - gateStep * n);
            l.Gate = g1;
            if (g0 <= 0 && g1 <= 0)
                continue; // not playing at this level: no CPU
            var stereo = l.Render(vl, vr, _scratch.AsSpan(0, n), ref _rng);
            var dg = (g1 - g0) / n;
            var g = g0 * l.Spec.Level;
            var send = l.Spec.Reverb;
            var lv = l.Spec.Level;
            for (var i = 0; i < n; i++)
            {
                var a = g + dg * lv * i;
                var sl = vl[i] * a;
                var sr = stereo ? vr[i] * a : sl;
                output[(start + i) * 2] += sl;
                output[(start + i) * 2 + 1] += sr;
                if (send > 0)
                    verbIn[i] += (sl + sr) * send;
            }
            if (g1 <= 0)
                l.Reset(); // faded out: start clean when it comes back
        }
        if (_verb != null)
        {
            var wl = _verbL.AsSpan(0, n);
            var wr = _verbR.AsSpan(0, n);
            _verb.Render(verbIn, wl, wr);
            for (var i = 0; i < n; i++)
            {
                output[(start + i) * 2] += wl[i] * 0.5f;
                output[(start + i) * 2 + 1] += wr[i] * 0.5f;
            }
        }
        var trim = Style.Trim;
        if (trim != 1f)
            for (var i = start * 2; i < (start + n) * 2; i++)
                output[i] *= trim;
    }

    /// <summary>The track through the style's effects: drive, crusher, stutter, slap-back.</summary>
    private void Effects(ReadOnlySpan<float> dry, Span<float> output, int start, int n)
    {
        var fx = Style.Fx;
        var drive = fx.Drive > 0 ? 1f + fx.Drive : 0f;
        var driveNorm = drive > 0 ? 1f / Dsp.Tanh(drive * 0.9f) * 0.9f : 1f;
        var crush = fx.CrushBits > 0;
        var q = crush ? MathF.Pow(2f, fx.CrushBits - 1) : 1f;
        var invQ = 1f / q; // a power of two: multiplying by it is exact, and cheaper than dividing
        var hold = Math.Max(1, fx.CrushHold);
        var stutter = fx.Stutter.Length > 0;
        var slapLen = fx.SlapMs > 0 ? Math.Clamp((int)(fx.SlapMs * 0.001f * Dsp.Sr), 1, Ring - 1) : 0;
        float fb = fx.SlapFeedback, mix = fx.SlapMix;
        for (var i = 0; i < n; i++)
        {
            var j = (start + i) * 2;
            float l = dry[j], r = dry[j + 1];
            if (drive > 0)
            {
                l = Dsp.Tanh(l * drive) * driveNorm;
                r = Dsp.Tanh(r * drive) * driveNorm;
            }
            if (crush || hold > 1)
            {
                if (_crushCount <= 0)
                {
                    _crushL = crush ? MathF.Round(l * q) * invQ : l;
                    _crushR = crush ? MathF.Round(r * q) * invQ : r;
                    _crushCount = hold;
                }
                _crushCount--;
                l = _crushL;
                r = _crushR;
            }
            if (stutter)
            {
                _ringL[_ringPos] = l;
                _ringR[_ringPos] = r;
                if (_stutLeft > 0)
                {
                    if (_stutT >= _stutGrain)
                    {
                        // Repeat the grain recorded at the start of this 16th, with tiny fades so the loop doesn't click.
                        var ph = _stutT % _stutGrain;
                        var k = (_stutStart + ph) & (Ring - 1);
                        var w = MathF.Min(1f, MathF.Min(ph, _stutGrain - ph) / 24f);
                        l = _ringL[k] * w;
                        r = _ringR[k] * w;
                    }
                    _stutT++;
                    _stutLeft--;
                }
                _ringPos = (_ringPos + 1) & (Ring - 1);
            }
            if (slapLen > 0)
            {
                var k = (_slapPos - slapLen) & (Ring - 1);
                float dl = _slapL[k], dr = _slapR[k];
                // Ping-pong a little: each repeat crosses over, so the echo bounces off the canyon walls.
                _slapL[_slapPos] = l + dr * fb;
                _slapR[_slapPos] = r + dl * fb;
                _slapPos = (_slapPos + 1) & (Ring - 1);
                l += dl * mix;
                r += dr * mix;
            }
            output[j] = l;
            output[j + 1] = r;
        }
    }

    /// <summary>One instrument layer: its voice (one of four kinds), gate and a pending ratchet hit.</summary>
    private sealed class Layer
    {
        public readonly FrenzyLayerSpec Spec;
        public readonly KickVoice? Kick;
        public readonly SnareVoice? Snare;
        public readonly BassVoice? Bass;
        public readonly SawSynth? Synth;
        public readonly WhistleVoice? Whistle;
        public float Gate;
        public int Pending = -1;
        private float _lastVel = 0.8f;

        public Layer(FrenzyLayerSpec spec)
        {
            Spec = spec;
            switch (spec.Voice)
            {
                case FrenzyVoice.BreakKick:
                    Kick = new KickVoice { StartHz = 190, EndHz = 52, PitchTime = 0.022f, Hold = 0.008f, DecayTime = 0.09f, Drive = 3f, ToneHz = 5000, Click = 0.35f };
                    break;
                case FrenzyVoice.BassDrum:
                    Kick = new KickVoice { StartHz = 120, EndHz = 58, PitchTime = 0.04f, Hold = 0.01f, DecayTime = 0.22f, Drive = 1.4f, ToneHz = 1800, Click = 0.15f };
                    break;
                case FrenzyVoice.Boom:
                    Kick = new KickVoice { StartHz = 170, EndHz = 36, PitchTime = 0.07f, Hold = 0.06f, DecayTime = 0.8f, Drive = 2.2f, ToneHz = 2200, Click = 0.25f, Top = 0.35f, TopStartHz = 700, TopEndHz = 90, TopDecay = 0.05f };
                    break;
                case FrenzyVoice.Gabber:
                    Kick = new KickVoice { StartHz = 340, EndHz = 55, PitchTime = 0.024f, Hold = 0.06f, DecayTime = 0.16f, Drive = 13f, ToneHz = 6500, Click = 0.3f, Clip = 1.35f, Top = 0.6f, TopStartHz = 1600, TopEndHz = 190, TopDecay = 0.02f };
                    break;
                case FrenzyVoice.Whip:
                    Snare = new SnareVoice { ToneHz = 1250, NoiseHz = 5200, NoiseTime = 0.035f, ToneTime = 0.012f, Drive = 2f, ToneLevel = 0.35f };
                    break;
                case FrenzyVoice.BreakSnare:
                    Snare = new SnareVoice { ToneHz = 215, NoiseHz = 2500, NoiseTime = 0.075f, ToneTime = 0.04f, Drive = 2.6f, ToneLevel = 0.55f };
                    break;
                case FrenzyVoice.March:
                    Snare = new SnareVoice { ToneHz = 330, NoiseHz = 4300, NoiseTime = 0.09f, ToneTime = 0.028f, Drive = 1.8f, ToneLevel = 0.35f };
                    break;
                case FrenzyVoice.BigSnare:
                    Snare = new SnareVoice { ToneHz = 165, NoiseHz = 1500, NoiseTime = 0.32f, ToneTime = 0.11f, Drive = 1.6f, ToneLevel = 0.6f };
                    break;
                case FrenzyVoice.Anvil:
                    Snare = new SnareVoice { ToneHz = 2350, NoiseHz = 6800, NoiseTime = 0.025f, ToneTime = 0.13f, Drive = 3f, ToneLevel = 0.9f };
                    break;
                case FrenzyVoice.Twang:
                    Bass = new BassVoice { Cutoff = 700, EnvAmount = 7, FilterTime = 0.05f, Drive = 2.2f, DecayTime = 0.2f, Release = 0.04f, Sub = 0.25f, Resonance = 0.62f, Detune = 0.004f, Bounce = 2.5f, BounceTime = 0.03f };
                    break;
                case FrenzyVoice.Chip:
                    Synth = new SawSynth { Square = true, Saws = 1, HarmonySaws = 1, Attack = 0.001f, DecayTime = 0.09f, Sustain = 0.55f, Release = 0.025f, Cutoff = 7000, EnvAmount = 0.5f, FilterTime = 0.1f, Resonance = 0f, Drive = 1.1f };
                    break;
                case FrenzyVoice.Glitch:
                    Synth = new SawSynth { Square = true, Saws = 1, HarmonySaws = 1, Attack = 0.001f, DecayTime = 0.03f, Sustain = 0f, Release = 0.01f, Cutoff = 5000, EnvAmount = 1f, FilterTime = 0.02f, Resonance = 0.5f, Drive = 2f, BendSemis = 12f, BendTime = 0.015f };
                    break;
                case FrenzyVoice.Brass:
                    // Half rate (as the braam): the filter never opens past ~5 kHz, so nobody hears the difference, and it halves the cost.
                    Synth = new SawSynth { HalfRate = true, Saws = 3, HarmonySaws = 2, Detune = 0.006f, Attack = 0.012f, DecayTime = 0.22f, Sustain = 0.45f, Release = 0.1f, Cutoff = 850, EnvAmount = 4.5f, FilterTime = 0.11f, Resonance = 0.1f, Drive = 2f, BendSemis = -1f, BendTime = 0.03f };
                    break;
                case FrenzyVoice.Braam:
                    Synth = new SawSynth { Saws = 3, HarmonySaws = 2, Detune = 0.011f, Attack = 0.06f, DecayTime = 2.2f, Sustain = 0.65f, Release = 0.9f, Cutoff = 260, EnvAmount = 5.5f, FilterTime = 0.8f, Resonance = 0.2f, Drive = 3.5f, HalfRate = true };
                    break;
                case FrenzyVoice.Metal:
                    // Half rate: the filter tops out near 4.5 kHz, far under the half-rate Nyquist (11 kHz).
                    Synth = new SawSynth { HalfRate = true, Saws = 2, HarmonySaws = 2, Detune = 0.004f, Attack = 0.002f, DecayTime = 0.085f, Sustain = 0.2f, Release = 0.035f, Cutoff = 1300, EnvAmount = 2.5f, FilterTime = 0.05f, Resonance = 0.15f, Drive = 6f };
                    break;
                case FrenzyVoice.Whistle:
                    Whistle = new WhistleVoice();
                    break;
            }
            Kick?.Configure(0.1f);
            Snare?.Configure();
            Bass?.Configure();
            Synth?.Configure();
        }

        public void Reset()
        {
            Kick?.Reset();
            Snare?.Reset();
            Bass?.Reset();
            Synth?.Reset();
            Whistle?.Reset();
            Pending = -1;
        }

        public void Note(MusicRenderer r, int degree, float vel)
        {
            _lastVel = vel;
            if (Kick != null)
                Kick.Trigger(vel);
            else if (Snare != null)
                Snare.Trigger(vel);
            else if (Bass != null)
                Bass.NoteOn(r.ClassHz(degree, Spec.Octave), vel);
            else if (Whistle != null)
                Whistle.NoteOn(r.ClassHz(degree, Spec.Octave), vel);
            else if (Synth != null)
                for (var k = 0; k < Spec.Chord.Length; k++)
                    Synth.NoteOn(k, r.ClassHz(degree + Spec.Chord[k], Spec.Octave), k == 0 ? vel : vel * 0.8f);
        }

        /// <summary>The second hit of a ratchet ('r').</summary>
        public void Hit(float vel)
        {
            Kick?.Trigger(vel * _lastVel / 0.7f);
            Snare?.Trigger(vel * _lastVel / 0.7f);
        }

        public void Off()
        {
            Bass?.NoteOff();
            Whistle?.NoteOff();
            Synth?.AllOff();
        }

        /// <summary>Renders a chunk; returns true when <paramref name="right"/> holds a separate right channel.</summary>
        public bool Render(Span<float> left, Span<float> right, Span<float> scratch, ref MusicRng rng)
        {
            if (Kick != null)
                Kick.Render(left, scratch, ref rng);
            else if (Snare != null)
                Snare.Render(left, ref rng);
            else if (Bass != null)
                Bass.Render(left);
            else if (Whistle != null)
                Whistle.Render(left, ref rng);
            else if (Synth != null)
            {
                Synth.Render(left, right);
                return true;
            }
            return false;
        }
    }
}

/// <summary>
/// A whistled lead: a sine with a touch of second harmonic and breath noise, sliding into each note, with a vibrato
/// that comes in once the note has been held a moment (the western whistle).
/// </summary>
internal sealed class WhistleVoice
{
    private float _phase, _hz, _target, _env, _vel, _vib, _held, _vibMul = 1f;
    private int _ctl;
    private byte _stage; // 0 off, 1 attack, 2 sustain, 3 release
    private Svf _breath;

    public void Reset()
    {
        _phase = _hz = _target = _env = _vel = _vib = _held = 0;
        _vibMul = 1f;
        _ctl = 0;
        _stage = 0;
        _breath.Reset();
    }

    public void NoteOn(float hz, float vel)
    {
        _target = hz;
        if (_stage == 0 || _hz <= 0)
            _hz = hz * 0.94f; // scoop up into the first note
        _stage = 1;
        _vel = vel;
        _held = 0;
    }

    public void NoteOff()
    {
        if (_stage != 0)
            _stage = 3;
    }

    public void Render(Span<float> dst, ref MusicRng rng)
    {
        if (_stage == 0)
        {
            dst.Clear();
            return;
        }
        var attack = 1f / (0.018f * Dsp.Sr);
        var release = Dsp.Decay(0.06f);
        var glide = 1f - Dsp.Decay(0.03f);
        var vibDt = 5.6f * Dsp.InvSr * 16f; // the vibrato moves at control rate (every 16 samples): inaudible at 5.6 Hz
        Svf.Coefs(Dsp.SvfG(MathF.Min(_target * 2f, 12000f)), 1.2f, out var a1, out var a2, out var a3);
        float phase = _phase, hz = _hz, env = _env, vib = _vib, held = _held, target = _target, vel = _vel, vibMul = _vibMul;
        var stage = _stage;
        var ctl = _ctl;
        var breath = _breath;
        for (var i = 0; i < dst.Length; i++)
        {
            if (stage == 1)
            {
                env += attack;
                if (env >= 1f)
                {
                    env = 1f;
                    stage = 2;
                }
            }
            else if (stage == 3)
            {
                env *= release;
                if (env < 1e-4f)
                {
                    env = 0;
                    stage = 0;
                }
            }
            hz += (target - hz) * glide;
            held += Dsp.InvSr;
            if ((ctl++ & 15) == 0)
            {
                vib += vibDt;
                if (vib >= 1f)
                    vib -= 1f;
                var depth = MathF.Min(1f, MathF.Max(0f, held - 0.15f) * 4f) * 0.016f; // a quarter tone of vibrato after 0.15 s
                vibMul = 1f + depth * MathF.Sin(vib * Dsp.TwoPi);
            }
            phase += hz * vibMul * Dsp.InvSr;
            if (phase >= 1f)
                phase -= 1f;
            breath.Process(rng.Noise(), 1.2f, a1, a2, a3);
            // sin(p) + 0.1 sin(2p) = sin(p) (1 + 0.2 cos(p)), with cos from sin (one sine per sample instead of two).
            var sn = MathF.Sin(phase * Dsp.TwoPi);
            var cs = MathF.Sqrt(MathF.Max(0f, 1f - sn * sn));
            if (phase is >= 0.25f and < 0.75f)
                cs = -cs;
            var s = sn * (1f + 0.2f * cs) + breath.Band * 0.25f;
            dst[i] = s * env * vel;
        }
        _breath = breath;
        _vibMul = vibMul;
        _ctl = ctl;
        _phase = phase;
        _hz = hz;
        _env = env;
        _vib = vib;
        _held = held;
        _stage = stage;
    }
}

/// <summary>
/// The style rig's reverb at half the sample rate (it only carries the instrument layers' sends, with the room's
/// high end damped anyway): each pair of input samples averaged into one, the reverb run on those, and its output
/// interpolated back up (1.5 samples late, which nobody hears in a reverb). Half the cost of a full-rate
/// <see cref="RoomReverb"/>, with the same echo times and the same damping per second. Allocation-free.
/// </summary>
internal sealed class HalfRateReverb
{
    private readonly RoomReverb _verb;
    private readonly float[] _in, _l, _r;
    private bool _odd;
    private float _pending, _curL, _curR;

    public HalfRateReverb(float feedback, float damp, int maxBlock)
    {
        _verb = new RoomReverb(halfRate: true) { Feedback = feedback, Damp = damp * damp }; // a one-pole at half rate: the pole squared
        _in = new float[maxBlock / 2 + 1];
        _l = new float[maxBlock / 2 + 1];
        _r = new float[maxBlock / 2 + 1];
    }

    public void Reset()
    {
        _verb.Reset();
        _odd = false;
        _pending = _curL = _curR = 0;
    }

    /// <summary>Processes a block (at most the constructor's maxBlock samples): input in, the wet left and right out.</summary>
    public void Render(ReadOnlySpan<float> input, Span<float> left, Span<float> right)
    {
        var n = input.Length;
        var m = 0;
        var odd = _odd;
        var pending = _pending;
        for (var i = 0; i < n; i++)
        {
            if (!odd)
            {
                pending = input[i];
                odd = true;
            }
            else
            {
                _in[m++] = 0.5f * (pending + input[i]);
                odd = false;
            }
        }
        _verb.Render(_in.AsSpan(0, m), _l.AsSpan(0, m), _r.AsSpan(0, m));
        odd = _odd;
        float cl = _curL, cr = _curR;
        var k = 0;
        for (var i = 0; i < n; i++)
        {
            if (!odd)
            {
                left[i] = cl;
                right[i] = cr;
                odd = true;
            }
            else
            {
                float nl = _l[k], nr = _r[k];
                k++;
                left[i] = 0.5f * (cl + nl);
                right[i] = 0.5f * (cr + nr);
                cl = nl;
                cr = nr;
                odd = false;
            }
        }
        _odd = odd;
        _pending = pending;
        _curL = cl;
        _curR = cr;
    }
}
