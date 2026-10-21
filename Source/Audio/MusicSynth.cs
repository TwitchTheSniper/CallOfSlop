namespace CallOfSlop;

// The instruments of the procedural music engine (see MusicRenderer.cs). Every voice keeps its state in fields and
// fixed arrays allocated once, so rendering never allocates. All voices run at 44.1 kHz and render a block at a time.

/// <summary>Small allocation-free DSP helpers for the music synth.</summary>
internal static class Dsp
{
    public const int Sr = Audio.SampleRate;
    public const float InvSr = 1f / Sr;
    public const float TwoPi = MathF.PI * 2f;

    /// <summary>Cheap tanh (rational approximation, exact ±1 beyond ±3): the soft clipper used for every distortion.</summary>
    public static float Tanh(float x)
    {
        if (x <= -3f)
            return -1f;
        if (x >= 3f)
            return 1f;
        var x2 = x * x;
        return x * (27f + x2) / (27f + 9f * x2);
    }

    /// <summary>Per-sample multiplier that decays to 1/e over the given time.</summary>
    public static float Decay(float seconds) => seconds <= 0 ? 0f : MathF.Exp(-1f / (seconds * Sr));

    public static float MidiHz(float midi) => 440f * MathF.Pow(2f, (midi - 69f) / 12f);

    /// <summary>One-pole low-pass coefficient for a cutoff in Hz.</summary>
    public static float OnePole(float hz) => 1f - MathF.Exp(-TwoPi * Math.Clamp(hz, 10f, Sr * 0.45f) * InvSr);

    /// <summary>TPT state-variable filter coefficient for a cutoff in Hz.</summary>
    public static float SvfG(float hz) => MathF.Tan(MathF.PI * Math.Clamp(hz, 20f, Sr * 0.42f) * InvSr);

    /// <summary>PolyBLEP correction that removes most of a saw's aliasing at its jump.</summary>
    public static float PolyBlep(float t, float dt)
    {
        if (t < dt)
        {
            t /= dt;
            return t + t - t * t - 1f;
        }
        if (t > 1f - dt)
        {
            t = (t - 1f) / dt;
            return t * t + t + t + 1f;
        }
        return 0f;
    }

    /// <summary>Band-limited saw (-1..1) at a phase in 0..1 advancing dt per sample.</summary>
    public static float Saw(float phase, float dt) => 2f * phase - 1f - PolyBlep(phase, dt);

    /// <summary>Band-limited square made of two saws half a cycle apart.</summary>
    public static float Square(float phase, float dt)
    {
        var p2 = phase + 0.5f;
        if (p2 >= 1f)
            p2 -= 1f;
        return Saw(phase, dt) - Saw(p2, dt);
    }
}

/// <summary>Xorshift random numbers: deterministic for a seed, allocation-free (a struct kept in a field).</summary>
internal struct MusicRng(uint seed)
{
    private uint _s = seed == 0 ? 0x9E3779B9u : seed;

    public uint Next()
    {
        if (_s == 0)
            _s = 0x9E3779B9u; // a default-constructed generator would be stuck at zero
        _s ^= _s << 13;
        _s ^= _s >> 17;
        _s ^= _s << 5;
        return _s;
    }

    /// <summary>0..1.</summary>
    public float Unit() => (Next() >> 8) * (1f / 16777216f);

    /// <summary>-1..1 (white noise).</summary>
    public float Noise() => (Next() >> 8) * (2f / 16777216f) - 1f;

    public int Range(int n) => (int)(Unit() * n) % Math.Max(1, n);

    public bool Chance(float p) => Unit() < p;
}

/// <summary>Topology-preserving state-variable filter (stable when its cutoff moves fast).</summary>
internal struct Svf
{
    private float _ic1, _ic2;
    public float Low, Band, High;

    /// <summary>The per-sample coefficients for g (from <see cref="Dsp.SvfG"/>) and k; compute them only when g changes.</summary>
    public static void Coefs(float g, float k, out float a1, out float a2, out float a3)
    {
        a1 = 1f / (1f + g * (g + k));
        a2 = g * a1;
        a3 = g * a2;
    }

    /// <summary>One sample. k = 2 - 2*resonance (2 = no resonance, small = sharp peak); a1..a3 from <see cref="Coefs"/>.</summary>
    public void Process(float x, float k, float a1, float a2, float a3)
    {
        var v3 = x - _ic2;
        var v1 = a1 * _ic1 + a2 * v3;
        var v2 = _ic2 + a2 * _ic1 + a3 * v3;
        _ic1 = 2f * v1 - _ic1;
        _ic2 = 2f * v2 - _ic2;
        // Keep the state out of the denormal range (denormals make float maths very slow).
        if (MathF.Abs(_ic1) < 1e-15f)
            _ic1 = 0;
        if (MathF.Abs(_ic2) < 1e-15f)
            _ic2 = 0;
        Low = v2;
        Band = v1;
        High = x - k * v1 - v2;
    }

    public void Reset() => this = default;
}

// Every voice renders a whole block at once (Render), with its state in locals during the loop: much cheaper than a
// call per sample per voice. The sequencer only changes notes between blocks (MusicRenderer cuts blocks at steps).

/// <summary>
/// Kick drum: a sine with a fast downward pitch sweep, held then decaying, driven into a soft clipper. With a long hold,
/// heavy drive and the end pitch tuned to the key, it becomes the distorted, tonal hardstyle / gabber kick.
/// </summary>
internal sealed class KickVoice
{
    public float StartHz = 200, EndHz = 50, PitchTime = 0.03f, Hold = 0.02f, DecayTime = 0.15f, Drive = 2f, ToneHz = 3000, Click = 0.3f;

    /// <summary>Hard clipping after the soft clipper (0 = off; e.g. 1.6 squares the waveform off): the pounding hardbass kick.</summary>
    public float Clip;

    /// <summary>Level and pitch of a short sine "tok" at the start of each hit (0 = none): a punchy click that cuts through.</summary>
    public float Punch, PunchHz = 2500;

    private float _phase, _pitch, _amp, _ampCoef, _pitchCoef, _click, _lpA, _lp, _duck, _duckCoef, _punch, _punchPhase, _punchCoef;
    private int _hold;

    public void Configure(float duckTime)
    {
        _ampCoef = Dsp.Decay(DecayTime);
        _pitchCoef = Dsp.Decay(PitchTime);
        _lpA = Dsp.OnePole(ToneHz);
        _duckCoef = Dsp.Decay(duckTime);
        _punchCoef = Dsp.Decay(0.005f);
    }

    public void Reset()
    {
        _phase = _pitch = _amp = _click = _lp = _duck = _punch = _punchPhase = 0;
        _hold = 0;
        _lpA = Dsp.OnePole(ToneHz);
    }

    /// <summary>muffle &lt; 1 darkens this hit (the filtered kick of an intro).</summary>
    public void Trigger(float vel, float muffle = 1f)
    {
        _phase = 0;
        _pitch = 1;
        _amp = vel;
        _hold = (int)(Hold * Dsp.Sr);
        _click = Click * vel * muffle;
        _lpA = Dsp.OnePole(ToneHz * muffle);
        _duck = 1;
        _punch = Punch * vel * muffle;
        _punchPhase = 0;
    }

    /// <summary>Renders the kick into dst and its sidechain envelope (1 on every hit, falling back) into duck.</summary>
    public void Render(Span<float> dst, Span<float> duck, ref MusicRng rng)
    {
        var n = dst.Length;
        var d = _duck;
        if (_amp <= 0 && MathF.Abs(_lp) < 1e-5f && _click <= 1e-4f && _punch <= 1e-4f)
        {
            // Silent: only the sidechain envelope moves.
            for (var i = 0; i < n; i++)
            {
                d *= _duckCoef;
                duck[i] = d;
            }
            dst.Clear();
            _duck = d < 1e-4f ? 0 : d;
            _lp = 0;
            return;
        }
        float phase = _phase, pitch = _pitch, amp = _amp, click = _click, lp = _lp;
        float lpA = _lpA, startHz = StartHz, endHz = EndHz, drive = Drive, ampCoef = _ampCoef, pitchCoef = _pitchCoef, duckCoef = _duckCoef;
        float clip = Clip, punch = _punch, punchPhase = _punchPhase, punchCoef = _punchCoef, punchDt = PunchHz * Dsp.InvSr;
        var hold = _hold;
        for (var i = 0; i < n; i++)
        {
            d *= duckCoef;
            duck[i] = d;
            var hz = endHz + (startHz - endHz) * pitch;
            pitch = pitch < 1e-12f ? 0 : pitch * pitchCoef;
            phase += hz * Dsp.InvSr;
            if (phase >= 1f)
                phase -= 1f;
            var s = MathF.Sin(phase * Dsp.TwoPi) * amp;
            if (hold > 0)
                hold--;
            else if ((amp *= ampCoef) < 1e-4f)
                amp = 0;
            s = Dsp.Tanh(s * drive);
            if (clip > 0)
                s = Math.Clamp(s * clip, -1f, 1f);
            lp += lpA * (s - lp);
            if (MathF.Abs(lp) < 1e-15f)
                lp = 0; // no denormals in the tail
            var c = 0f;
            if (click > 1e-4f)
            {
                c = rng.Noise() * click;
                click *= 0.95f;
            }
            if (punch > 1e-4f)
            {
                punchPhase += punchDt;
                if (punchPhase >= 1f)
                    punchPhase -= 1f;
                c += MathF.Sin(punchPhase * Dsp.TwoPi) * punch;
                punch *= punchCoef;
            }
            dst[i] = lp + c;
        }
        _phase = phase;
        _pitch = pitch;
        _amp = amp < 1e-4f ? 0 : amp;
        _click = click;
        _punch = punch <= 1e-4f ? 0 : punch;
        _punchPhase = punchPhase;
        _lp = MathF.Abs(lp) < 1e-15f ? 0 : lp;
        _hold = hold;
        _duck = d < 1e-4f ? 0 : d;
    }
}

/// <summary>Snare or clap: band-passed noise plus a short tone body; a clap adds three quick slaps first.</summary>
internal sealed class SnareVoice
{
    public float ToneHz = 190, NoiseHz = 1800, NoiseTime = 0.11f, ToneTime = 0.05f, Drive = 1.2f, ToneLevel = 0.5f;
    public bool Clap;

    /// <summary>Pitch multiplier for the next hits (rises through a build's snare roll).</summary>
    public float PitchMul = 1f;

    private const int ClapGap = 441; // 10 ms between slaps
    private const float K = 1.1f;
    private float _env, _tone, _phase, _vel, _nCoef, _tCoef, _toneHz, _a1, _a2, _a3;
    private int _t;
    private Svf _f;

    public void Configure()
    {
        _nCoef = Dsp.Decay(NoiseTime);
        _tCoef = Dsp.Decay(ToneTime);
    }

    public void Reset()
    {
        _env = _tone = _phase = _vel = 0;
        _t = 0;
        _f.Reset();
    }

    public void Trigger(float vel)
    {
        _vel = vel;
        _env = vel;
        _tone = vel * ToneLevel;
        _phase = 0;
        _t = 0;
        Svf.Coefs(Dsp.SvfG(NoiseHz * PitchMul), K, out _a1, out _a2, out _a3);
        _toneHz = ToneHz * PitchMul;
    }

    public void Render(Span<float> dst, ref MusicRng rng)
    {
        if (_env <= 0 && _tone <= 0)
        {
            dst.Clear();
            return;
        }
        var f = _f;
        float env = _env, tone = _tone, phase = _phase, vel = _vel, a1 = _a1, a2 = _a2, a3 = _a3, nCoef = _nCoef, tCoef = _tCoef, drive = Drive;
        var dphase = _toneHz * Dsp.InvSr;
        var t = _t;
        var clap = Clap;
        for (var i = 0; i < dst.Length; i++)
        {
            f.Process(rng.Noise(), K, a1, a2, a3);
            var envN = env;
            var slapping = clap && t < ClapGap * 3;
            if (slapping)
                envN = vel * (1f - (t % ClapGap) / (float)ClapGap);
            t++;
            var s = (f.Band * 1.6f + f.High * 0.5f) * envN;
            if (tone > 0)
            {
                phase += dphase;
                if (phase >= 1f)
                    phase -= 1f;
                s += MathF.Sin(phase * Dsp.TwoPi) * tone;
                tone *= tCoef;
                if (tone < 1e-4f)
                    tone = 0;
            }
            if (!slapping)
            {
                env *= nCoef;
                if (env < 1e-4f)
                    env = 0;
            }
            dst[i] = Dsp.Tanh(s * drive);
        }
        _f = f;
        _env = env;
        _tone = tone;
        _phase = phase;
        _t = t;
    }
}

/// <summary>Hi-hat: high-passed noise; a closed hit chokes an open one, like a real hat.</summary>
internal sealed class HatVoice
{
    public float ClosedTime = 0.03f, OpenTime = 0.2f;
    private float _env, _coef, _closed, _open, _lp, _lp2;

    public void Configure()
    {
        _closed = Dsp.Decay(ClosedTime);
        _open = Dsp.Decay(OpenTime);
    }

    public void Reset()
    {
        _env = _lp = _lp2 = 0;
        _coef = _closed;
    }

    public void Trigger(float vel, bool open)
    {
        _env = vel;
        _coef = open ? _open : _closed;
    }

    public void Render(Span<float> dst, ref MusicRng rng)
    {
        if (_env <= 0)
        {
            dst.Clear();
            return;
        }
        float env = _env, coef = _coef, lp = _lp, lp2 = _lp2;
        for (var i = 0; i < dst.Length; i++)
        {
            var n = rng.Noise();
            lp += 0.62f * (n - lp); // ~7 kHz
            var h = n - lp;
            lp2 += 0.85f * (h - lp2); // tame the very top
            dst[i] = lp2 * env;
            env *= coef;
        }
        _env = env < 1e-4f ? 0 : env;
        _lp = lp;
        _lp2 = lp2;
    }
}

/// <summary>
/// Polyphonic detuned-saw (or square) synth: the supersaw leads, the pads and the arps. Up to four notes of up to
/// seven oscillators each, spread across the stereo field, through a soft clipper and a stereo low-pass filter
/// with its own envelope.
/// </summary>
internal sealed class SawSynth
{
    public const int MaxNotes = 4, MaxSaws = 7;

    /// <summary>The longest block <see cref="Render"/> takes.</summary>
    public const int MaxBlock = 1024;

    public int Saws = 5;

    /// <summary>Oscillators for notes in slots 1+ (the quieter harmony voices); fewer than <see cref="Saws"/> saves CPU.</summary>
    public int HarmonySaws = 5;

    /// <summary>Runs the oscillators, envelopes and filter at half the sample rate and interpolates: half the cost, for dark sounds (pads).</summary>
    public bool HalfRate;

    public float Detune = 0.012f, Attack = 0.003f, DecayTime = 0.3f, Sustain = 0.7f, Release = 0.08f;
    public float Cutoff = 3000, EnvAmount = 2f, FilterTime = 0.2f, Resonance = 0.15f, Drive = 1.5f, Glide;
    public bool Square;

    /// <summary>Filter automation from the arrangement (a build opens it from nearly closed to 1).</summary>
    public float CutoffMul = 1f;

    /// <summary>Every note starts this many semitones off and slides to its pitch over <see cref="BendTime"/> (0 = off): the hoover swoop.</summary>
    public float BendSemis, BendTime = 0.06f;

    /// <summary>Pitch vibrato depth in semitones (0 = off) and rate.</summary>
    public float Vibrato, VibratoHz = 5.5f;

    /// <summary>Filter cutoff LFO depth in octaves (0 = off) and rate: the wah of a screech.</summary>
    public float FilterLfo, FilterLfoHz = 4f;

    /// <summary>Band-pass instead of low-pass output: a nasal, screaming tone (with high resonance).</summary>
    public bool BandPass;

    private readonly float[] _phase = new float[MaxNotes * MaxSaws];
    private readonly float[] _hz = new float[MaxNotes], _target = new float[MaxNotes], _env = new float[MaxNotes], _vel = new float[MaxNotes];
    private readonly byte[] _stage = new byte[MaxNotes]; // 0 off, 1 attack, 2 decay/sustain, 3 release
    private readonly float[] _spread = new float[MaxSaws], _panL = new float[MaxSaws], _panR = new float[MaxSaws];
    private readonly float[] _hSpread = new float[MaxSaws], _hPanL = new float[MaxSaws], _hPanR = new float[MaxSaws];
    private readonly float[] _amp = new float[MaxBlock], _dt = new float[MaxBlock];
    private readonly float[] _rawL = new float[MaxBlock], _rawR = new float[MaxBlock], _outL = new float[MaxBlock], _outR = new float[MaxBlock];
    private readonly float[] _vib = new float[MaxBlock];
    private float _fenv, _fCoef, _aInc, _dCoef, _rCoef, _gCoef, _g, _k, _norm, _hNorm, _dtMul = 1f, _curL, _curR;
    private float _bendRatio = 1f, _vibPhase, _flfoPhase;
    private int _ctl;
    private bool _odd;
    private Svf _l, _r;

    public void Configure()
    {
        Saws = Math.Clamp(Saws, 1, MaxSaws);
        HarmonySaws = Math.Clamp(HarmonySaws, 1, Saws);
        Spread(Saws, _spread, _panL, _panR);
        Spread(HarmonySaws, _hSpread, _hPanL, _hPanR);
        _norm = 1f / MathF.Sqrt(Saws);
        _hNorm = 1f / MathF.Sqrt(HarmonySaws);
        // At half rate every internal sample covers two output samples.
        var t = HalfRate ? 0.5f : 1f;
        _dtMul = HalfRate ? 2f : 1f;
        _fCoef = Dsp.Decay(FilterTime * t);
        _aInc = 1f / MathF.Max(1f, Attack * Dsp.Sr * t);
        _dCoef = Dsp.Decay(DecayTime * t);
        _rCoef = Dsp.Decay(Release * t);
        _gCoef = Glide > 0 ? 1f - Dsp.Decay(Glide * t) : BendSemis != 0 ? 1f - Dsp.Decay(BendTime * t) : 1f;
        _bendRatio = BendSemis != 0 ? MathF.Pow(2f, BendSemis / 12f) : 1f;
        _k = 2f - 2f * Math.Clamp(Resonance, 0f, 0.95f);
        _ctl = 0;
    }

    private void Spread(int saws, float[] spread, float[] panL, float[] panR)
    {
        for (var j = 0; j < saws; j++)
        {
            var x = saws == 1 ? 0f : j / (float)(saws - 1) * 2f - 1f; // -1..1
            spread[j] = 1f + Detune * x * (0.6f + 0.4f * MathF.Abs(x)); // outer saws spread a bit more
            var pan = saws == 1 ? 0f : (j % 2 == 0 ? -1f : 1f) * (0.3f + 0.7f * MathF.Abs(x));
            panL[j] = MathF.Sqrt(0.5f * (1f - pan));
            panR[j] = MathF.Sqrt(0.5f * (1f + pan));
        }
    }

    public void Reset()
    {
        Array.Clear(_stage);
        Array.Clear(_env);
        Array.Clear(_phase);
        Array.Clear(_hz);
        Array.Clear(_target);
        Array.Clear(_vel);
        _fenv = _g = _curL = _curR = _vibPhase = _flfoPhase = 0;
        _ctl = 0;
        _odd = false;
        _l.Reset();
        _r.Reset();
        CutoffMul = 1f;
    }

    public bool Active(int slot) => _stage[slot] != 0;

    public void NoteOn(int slot, float hz, float vel)
    {
        _target[slot] = hz;
        if (_stage[slot] == 0 || Glide <= 0)
            _hz[slot] = hz * _bendRatio;
        if (_stage[slot] == 0)
            for (var j = 0; j < Saws; j++)
                _phase[slot * MaxSaws + j] = (j * 0.618034f + slot * 0.37f) % 1f; // spread phases: a fat start, no phase-cancel thump
        _stage[slot] = 1;
        _vel[slot] = vel;
        _fenv = 1f;
    }

    public void NoteOff(int slot)
    {
        if (_stage[slot] != 0)
            _stage[slot] = 3;
    }

    public void AllOff()
    {
        for (var n = 0; n < MaxNotes; n++)
            NoteOff(n);
    }

    /// <summary>Renders a block (at most <see cref="MaxBlock"/> samples) into left and right (overwrites them).</summary>
    public void Render(Span<float> left, Span<float> right)
    {
        var n = left.Length;
        if (!HalfRate)
        {
            Compute(n);
            _outL.AsSpan(0, n).CopyTo(left);
            _outR.AsSpan(0, n).CopyTo(right);
            return;
        }
        // Half rate: a new value every other output sample, linearly interpolated in between.
        var m = _odd ? n / 2 : (n + 1) / 2;
        Compute(m);
        float cl = _curL, cr = _curR;
        var odd = _odd;
        var k = 0;
        for (var i = 0; i < n; i++)
        {
            if (odd)
            {
                left[i] = cl;
                right[i] = cr;
                odd = false;
            }
            else
            {
                float pl = cl, pr = cr;
                cl = _outL[k];
                cr = _outR[k];
                k++;
                left[i] = 0.5f * (pl + cl);
                right[i] = 0.5f * (pr + cr);
                odd = true;
            }
        }
        _curL = cl;
        _curR = cr;
        _odd = odd;
    }

    /// <summary>m internal samples into _outL/_outR.</summary>
    private void Compute(int m)
    {
        var rawL = _rawL.AsSpan(0, m);
        var rawR = _rawR.AsSpan(0, m);
        var outL = _outL.AsSpan(0, m);
        var outR = _outR.AsSpan(0, m);
        rawL.Clear();
        rawR.Clear();
        var any = false;
        var amps = _amp.AsSpan(0, m);
        var dts = _dt.AsSpan(0, m);
        var vibOn = Vibrato != 0;
        if (vibOn)
        {
            // One shared LFO for every note, stepped every 16 samples (inaudible at this rate). ln(2)/12 = 0.05776.
            var vib = _vib.AsSpan(0, m);
            float vp = _vibPhase, vdt = VibratoHz * Dsp.InvSr * _dtMul * 16f, depth = Vibrato * 0.05776f, cur = 1f;
            for (var i = 0; i < m; i++)
            {
                if ((i & 15) == 0)
                {
                    vp += vdt;
                    if (vp >= 1f)
                        vp -= 1f;
                    cur = 1f + depth * MathF.Sin(vp * Dsp.TwoPi);
                }
                vib[i] = cur;
            }
            _vibPhase = vp;
        }
        for (var note = 0; note < MaxNotes; note++)
        {
            var stage = _stage[note];
            if (stage == 0)
                continue;
            any = true;
            var harmony = note > 0;
            var vel = _vel[note] * (harmony ? _hNorm : _norm);
            float e = _env[note], hz = _hz[note], target = _target[note];
            float aInc = _aInc, dCoef = _dCoef, rCoef = _rCoef, gCoef = _gCoef, sustain = Sustain, dtScale = Dsp.InvSr * _dtMul;
            for (var i = 0; i < m; i++)
            {
                if (stage == 1)
                {
                    e += aInc;
                    if (e >= 1f)
                    {
                        e = 1f;
                        stage = 2;
                    }
                }
                else if (stage == 2)
                {
                    e = sustain + (e - sustain) * dCoef;
                }
                else if (stage == 3)
                {
                    e *= rCoef;
                    if (e < 1e-3f) // -60 dB: inaudible, and the oscillators stop costing anything
                    {
                        e = 0;
                        stage = 0;
                    }
                }
                hz += (target - hz) * gCoef;
                amps[i] = e * vel;
                dts[i] = hz * dtScale;
            }
            if (vibOn)
                for (var i = 0; i < m; i++)
                    dts[i] *= _vib[i];
            _env[note] = e;
            _hz[note] = hz;
            _stage[note] = stage;

            var saws = harmony ? HarmonySaws : Saws;
            var spread = harmony ? _hSpread : _spread;
            var panL = harmony ? _hPanL : _panL;
            var panR = harmony ? _hPanR : _panR;
            var off = note * MaxSaws;
            for (var j = 0; j < saws; j++)
            {
                var ph = _phase[off + j];
                float sp = spread[j], gl = panL[j], gr = panR[j];
                if (Square)
                {
                    for (var i = 0; i < m; i++)
                    {
                        var dt = dts[i] * sp;
                        ph += dt;
                        if (ph >= 1f)
                            ph -= 1f;
                        var v = Dsp.Square(ph, dt) * 0.5f * amps[i];
                        rawL[i] += v * gl;
                        rawR[i] += v * gr;
                    }
                }
                else
                {
                    for (var i = 0; i < m; i++)
                    {
                        var dt = dts[i] * sp;
                        ph += dt;
                        if (ph >= 1f)
                            ph -= 1f;
                        var v = Dsp.Saw(ph, dt) * amps[i];
                        rawL[i] += v * gl;
                        rawR[i] += v * gr;
                    }
                }
                _phase[off + j] = ph;
            }
        }
        if (!any && MathF.Abs(_l.Low) + MathF.Abs(_r.Low) < 1e-6f)
        {
            outL.Clear();
            outR.Clear();
            return;
        }

        // Drive and the stereo filter; its coefficients update at control rate (every 16 samples).
        Svf l = _l, r = _r;
        float g = _g, fenv = _fenv, fCoef = _fCoef, k = _k, drive = Drive, cutoff = Cutoff * CutoffMul * _dtMul, envAmount = EnvAmount;
        Svf.Coefs(g, k, out var a1, out var a2, out var a3);
        var ctl = _ctl;
        if (FilterLfo == 0 && !BandPass)
        {
            for (var i = 0; i < m; i++)
            {
                if ((ctl++ & 15) == 0)
                {
                    g = Dsp.SvfG(cutoff * (1f + envAmount * fenv));
                    Svf.Coefs(g, k, out a1, out a2, out a3);
                }
                fenv *= fCoef;
                l.Process(Dsp.Tanh(rawL[i] * drive), k, a1, a2, a3);
                r.Process(Dsp.Tanh(rawR[i] * drive), k, a1, a2, a3);
                outL[i] = l.Low;
                outR[i] = r.Low;
            }
        }
        else
        {
            // Screech: the cutoff wobbles with an LFO, and the band-pass output screams at the resonance.
            float fp = _flfoPhase, fdt = FilterLfoHz * Dsp.InvSr * _dtMul * 16f, depth = FilterLfo * 0.6931f, bpGain = 1.5f * k;
            var bandPass = BandPass;
            for (var i = 0; i < m; i++)
            {
                if ((ctl++ & 15) == 0)
                {
                    fp += fdt;
                    if (fp >= 1f)
                        fp -= 1f;
                    var lfo = depth != 0 ? MathF.Exp(depth * MathF.Sin(fp * Dsp.TwoPi)) : 1f;
                    g = Dsp.SvfG(cutoff * (1f + envAmount * fenv) * lfo);
                    Svf.Coefs(g, k, out a1, out a2, out a3);
                }
                fenv *= fCoef;
                l.Process(Dsp.Tanh(rawL[i] * drive), k, a1, a2, a3);
                r.Process(Dsp.Tanh(rawR[i] * drive), k, a1, a2, a3);
                outL[i] = bandPass ? l.Band * bpGain : l.Low;
                outR[i] = bandPass ? r.Band * bpGain : r.Low;
            }
            _flfoPhase = fp;
        }
        _l = l;
        _r = r;
        _g = g;
        _fenv = fenv < 1e-5f ? 0 : fenv;
        _ctl = ctl;
    }
}

/// <summary>
/// Mono bass: two detuned saws plus a sine sub an octave down, a resonant low-pass with an envelope and heavy drive.
/// Short envelopes give plucks, chugs and rolling basslines; long ones with glide and a slow filter wobble give a
/// drum-and-bass reese.
/// </summary>
internal sealed class BassVoice
{
    public float Cutoff = 400, EnvAmount = 6, FilterTime = 0.08f, Drive = 3, DecayTime = 0.15f, Sustain, Release = 0.03f;
    public float Glide, Detune = 0.006f, Sub = 0.5f, Resonance = 0.3f, Wobble; // wobble: filter LFO depth (0..1)
    public float CutoffMul = 1f;

    /// <summary>Each note starts this many semitones high and drops onto its pitch in a few ms (0 = off): the bouncy hardbass "boing".</summary>
    public float Bounce, BounceTime = 0.014f;

    private float _bounceRatio = 1f;

    private float _p1, _p2, _ps, _hz, _target, _env, _vel, _fenv, _lfo;
    private float _dCoef, _rCoef, _fCoef, _gCoef, _g, _k;
    private byte _stage;
    private int _ctl;
    private Svf _f;

    public void Configure()
    {
        _dCoef = Dsp.Decay(DecayTime);
        _rCoef = Dsp.Decay(Release);
        _fCoef = Dsp.Decay(FilterTime);
        _gCoef = Glide > 0 ? 1f - Dsp.Decay(Glide) : Bounce > 0 ? 1f - Dsp.Decay(BounceTime) : 1f;
        _bounceRatio = Bounce > 0 ? MathF.Pow(2f, Bounce / 12f) : 1f;
        _k = 2f - 2f * Math.Clamp(Resonance, 0f, 0.95f);
    }

    public void Reset()
    {
        _stage = 0;
        _env = _fenv = _lfo = _p1 = _p2 = _ps = _hz = _target = _vel = _g = 0;
        _ctl = 0;
        _f.Reset();
        CutoffMul = 1f;
    }

    public void NoteOn(float hz, float vel)
    {
        _target = hz;
        if (_stage == 0 || Glide <= 0)
            _hz = hz * _bounceRatio;
        _stage = 1;
        _vel = vel;
        _fenv = 1f;
    }

    public void NoteOff()
    {
        if (_stage != 0)
            _stage = 3;
    }

    public void Render(Span<float> dst)
    {
        if (_stage == 0 && MathF.Abs(_f.Low) < 1e-6f)
        {
            dst.Clear();
            return;
        }
        var f = _f;
        var stage = _stage;
        float env = _env, p1 = _p1, p2 = _p2, ps = _ps, hz = _hz, target = _target, vel = _vel, fenv = _fenv, lfo = _lfo, g = _g;
        float dCoef = _dCoef, rCoef = _rCoef, fCoef = _fCoef, gCoef = _gCoef, k = _k, sustain = Sustain, detune = Detune, drive = Drive, sub = Sub;
        float cutoff = Cutoff * CutoffMul, envAmount = EnvAmount, wobble = Wobble;
        Svf.Coefs(g, k, out var a1, out var a2, out var a3);
        var ctl = _ctl;
        for (var i = 0; i < dst.Length; i++)
        {
            if (stage == 1)
            {
                env += 1f / 88f; // 2 ms attack
                if (env >= 1f)
                {
                    env = 1f;
                    stage = 2;
                }
            }
            else if (stage == 2)
            {
                env = sustain + (env - sustain) * dCoef;
            }
            else if (stage == 3)
            {
                env *= rCoef;
                if (env < 1e-4f)
                {
                    env = 0;
                    stage = 0;
                }
            }
            hz += (target - hz) * gCoef;
            var dt = hz * Dsp.InvSr;
            var dt1 = dt * (1f + detune);
            var dt2 = dt * (1f - detune);
            p1 += dt1;
            if (p1 >= 1f)
                p1 -= 1f;
            p2 += dt2;
            if (p2 >= 1f)
                p2 -= 1f;
            ps += dt * 0.5f;
            if (ps >= 1f)
                ps -= 1f;
            var raw = (Dsp.Saw(p1, dt1) + Dsp.Saw(p2, dt2)) * 0.5f;
            if ((ctl++ & 15) == 0)
            {
                var mod = 1f;
                if (wobble > 0)
                {
                    lfo += 16f * 0.23f * Dsp.InvSr; // slow sweep, ~4 s per cycle
                    if (lfo >= 1f)
                        lfo -= 1f;
                    mod = 1f + wobble * MathF.Sin(lfo * Dsp.TwoPi);
                }
                g = Dsp.SvfG(cutoff * mod * (1f + envAmount * fenv));
                Svf.Coefs(g, k, out a1, out a2, out a3);
            }
            fenv *= fCoef;
            f.Process(Dsp.Tanh(raw * drive), k, a1, a2, a3);
            dst[i] = (f.Low + MathF.Sin(ps * Dsp.TwoPi) * sub) * env * vel;
        }
        _f = f;
        _stage = stage;
        _env = env;
        _p1 = p1;
        _p2 = p2;
        _ps = ps;
        _hz = hz;
        _fenv = fenv < 1e-5f ? 0 : fenv;
        _lfo = lfo;
        _g = g;
        _ctl = ctl;
    }
}

/// <summary>Crash cymbal (wide high-passed noise with a long tail), riser (sweeping band-passed noise) and sub drop.</summary>
internal sealed class FxVoices
{
    /// <summary>Riser level (0..1) and centre frequency, set by the arrangement every step.</summary>
    public float RiserLevel, RiserHz = 400;

    private const float RiserK = 0.5f;
    private float _crash, _crashCoef, _lpL, _lpR, _subAmp, _subPitch, _subPhase, _subCoef, _subPitchCoef, _riserLevel;
    private Svf _riser;

    public void Configure()
    {
        _crashCoef = Dsp.Decay(0.9f);
        _subCoef = Dsp.Decay(0.45f);
        _subPitchCoef = Dsp.Decay(0.35f);
    }

    public void Reset()
    {
        _crash = _subAmp = RiserLevel = _riserLevel = _lpL = _lpR = _subPitch = _subPhase = 0;
        RiserHz = 400;
        _riser.Reset();
    }

    public void Crash(float vel) => _crash = MathF.Max(_crash, vel);

    public void SubDrop(float vel)
    {
        _subAmp = vel;
        _subPitch = 1f;
        _subPhase = 0;
    }

    /// <summary>Renders a block into left and right (overwrites them).</summary>
    public void Render(Span<float> left, Span<float> right, ref MusicRng rng)
    {
        var n = left.Length;
        left.Clear();
        right.Clear();
        if (_crash > 0)
        {
            float crash = _crash, lpL = _lpL, lpR = _lpR, coef = _crashCoef;
            for (var i = 0; i < n; i++)
            {
                var nl = rng.Noise();
                var nr = rng.Noise();
                lpL += 0.55f * (nl - lpL);
                lpR += 0.55f * (nr - lpR);
                left[i] = (nl - lpL) * crash;
                right[i] = (nr - lpR) * crash;
                crash *= coef;
            }
            _crash = crash < 1e-3f ? 0 : crash;
            _lpL = lpL;
            _lpR = lpR;
        }
        var target = RiserLevel;
        if (_riserLevel > 1e-4f || target > 1e-4f)
        {
            // The centre frequency only changes per step, so one set of coefficients covers the block.
            Svf.Coefs(Dsp.SvfG(RiserHz), RiserK, out var a1, out var a2, out var a3);
            var f = _riser;
            var level = _riserLevel;
            for (var i = 0; i < n; i++)
            {
                level += (target - level) * 0.001f; // smooth the per-step changes
                f.Process(rng.Noise(), RiserK, a1, a2, a3);
                var v = f.Band * level;
                left[i] += v;
                right[i] += v;
            }
            _riser = f;
            _riserLevel = level;
        }
        else
        {
            _riserLevel = 0;
        }
        if (_subAmp > 0)
        {
            float amp = _subAmp, pitch = _subPitch, phase = _subPhase, coef = _subCoef, pitchCoef = _subPitchCoef;
            for (var i = 0; i < n; i++)
            {
                var hz = 32f + 70f * pitch;
                pitch *= pitchCoef;
                phase += hz * Dsp.InvSr;
                if (phase >= 1f)
                    phase -= 1f;
                var v = MathF.Sin(phase * Dsp.TwoPi) * amp;
                amp *= coef;
                left[i] += v;
                right[i] += v;
            }
            _subAmp = amp < 1e-4f ? 0 : amp;
            _subPitch = pitch;
            _subPhase = phase;
        }
    }
}

/// <summary>Ping-pong delay with a darkened feedback path (leads and arps).</summary>
internal sealed class PingPongDelay
{
    private readonly float[] _l = new float[Dsp.Sr], _r = new float[Dsp.Sr];
    private int _pos, _len = Dsp.Sr / 4;
    private float _lpL, _lpR;
    public float Feedback = 0.4f;

    public void SetTime(float seconds) => _len = Math.Clamp((int)(seconds * Dsp.Sr), 1, _l.Length);

    public void Reset()
    {
        Array.Clear(_l);
        Array.Clear(_r);
        _pos = 0;
        _lpL = _lpR = 0;
    }

    /// <summary>Processes a block: input in, the wet left and right out.</summary>
    public void Render(ReadOnlySpan<float> input, Span<float> left, Span<float> right)
    {
        var bl = _l;
        var br = _r;
        int pos = _pos, len = _len;
        float lpL = _lpL, lpR = _lpR, fb = Feedback;
        for (var i = 0; i < input.Length; i++)
        {
            var dl = bl[pos];
            var dr = br[pos];
            lpL += 0.4f * (dl - lpL);
            lpR += 0.4f * (dr - lpR);
            if (MathF.Abs(lpL) < 1e-15f)
                lpL = 0; // no denormals circulating in silence
            if (MathF.Abs(lpR) < 1e-15f)
                lpR = 0;
            bl[pos] = input[i] + lpR * fb;
            br[pos] = lpL * fb;
            if (++pos >= len)
                pos = 0;
            left[i] = dl;
            right[i] = dr;
        }
        _pos = pos;
        _lpL = MathF.Abs(lpL) < 1e-15f ? 0 : lpL;
        _lpR = MathF.Abs(lpR) < 1e-15f ? 0 : lpR;
    }
}

/// <summary>Small stereo room reverb (four damped combs and two all-passes per side, after Freeverb).</summary>
internal sealed class RoomReverb
{
    private static readonly int[] CombLengths = [1116, 1277, 1422, 1557];
    private static readonly int[] AllpassLengths = [556, 341];
    private const int Spread = 23;

    private readonly float[][] _combL, _combR, _apL, _apR;
    private readonly int[] _ciL = new int[4], _ciR = new int[4], _aiL = new int[2], _aiR = new int[2];
    private readonly float[] _storeL = new float[4], _storeR = new float[4];
    public float Feedback = 0.8f, Damp = 0.35f;

    public RoomReverb()
    {
        _combL = CombLengths.Select(n => new float[n]).ToArray();
        _combR = CombLengths.Select(n => new float[n + Spread]).ToArray();
        _apL = AllpassLengths.Select(n => new float[n]).ToArray();
        _apR = AllpassLengths.Select(n => new float[n + Spread]).ToArray();
    }

    public void Reset()
    {
        // Plain loops: Load() runs on the streaming thread and must not allocate.
        for (var i = 0; i < 4; i++)
        {
            Array.Clear(_combL[i]);
            Array.Clear(_combR[i]);
        }
        for (var i = 0; i < 2; i++)
        {
            Array.Clear(_apL[i]);
            Array.Clear(_apR[i]);
        }
        Array.Clear(_storeL);
        Array.Clear(_storeR);
        Array.Clear(_ciL);
        Array.Clear(_ciR);
        Array.Clear(_aiL);
        Array.Clear(_aiR);
    }

    /// <summary>
    /// Processes a block: input in, the wet left and right out. Each comb and all-pass runs over the whole block in
    /// turn (they're independent filters in parallel, then in series), which keeps every loop small and fast.
    /// </summary>
    public void Render(ReadOnlySpan<float> input, Span<float> left, Span<float> right)
    {
        left.Clear();
        right.Clear();
        for (var c = 0; c < 4; c++)
        {
            Comb(_combL[c], ref _ciL[c], ref _storeL[c], input, left);
            Comb(_combR[c], ref _ciR[c], ref _storeR[c], input, right);
        }
        for (var a = 0; a < 2; a++)
        {
            Allpass(_apL[a], ref _aiL[a], left);
            Allpass(_apR[a], ref _aiR[a], right);
        }
    }

    private void Comb(float[] buf, ref int index, ref float store, ReadOnlySpan<float> input, Span<float> sum)
    {
        int idx = index, len = buf.Length;
        float s = store, damp = Damp, keep = 1f - Damp, fb = Feedback;
        for (var i = 0; i < input.Length; i++)
        {
            var y = buf[idx];
            s = y * keep + s * damp;
            if (MathF.Abs(s) < 1e-15f)
                s = 0; // no denormals circulating in silence
            buf[idx] = input[i] * 0.03f + s * fb;
            if (++idx >= len)
                idx = 0;
            sum[i] += y;
        }
        index = idx;
        store = MathF.Abs(s) < 1e-15f ? 0 : s;
    }

    private static void Allpass(float[] buf, ref int index, Span<float> io)
    {
        int idx = index, len = buf.Length;
        for (var i = 0; i < io.Length; i++)
        {
            var b = buf[idx];
            var x = io[i];
            var w = x + b * 0.5f;
            buf[idx] = MathF.Abs(w) < 1e-15f ? 0f : w;
            if (++idx >= len)
                idx = 0;
            io[i] = b - x;
        }
        index = idx;
    }
}

/// <summary>
/// Hardstyle reverse bass: a heavily driven sine on the chord root that swells up between two kicks (the kick's tail
/// played backwards), bends up at the very end and cuts off just before the next kick, so the two never smear.
/// </summary>
internal sealed class ReverseBassVoice
{
    public float Drive = 5f, ToneHz = 1400f, BendSemis = 7f;

    private float _phase, _hz, _vel, _lp, _lpA, _fade, _bend;
    private int _t, _len;
    private bool _on;

    public void Configure()
    {
        _lpA = Dsp.OnePole(ToneHz);
        _bend = MathF.Pow(2f, BendSemis / 12f) - 1f;
    }

    public void Reset()
    {
        _phase = _hz = _vel = _lp = _fade = 0;
        _t = _len = 0;
        _on = false;
    }

    /// <summary>Starts a swell at hz lasting the given seconds (then a 2 ms cut).</summary>
    public void Trigger(float hz, float seconds, float vel)
    {
        _hz = hz;
        _vel = vel;
        _t = 0;
        _len = Math.Max(64, (int)(seconds * Dsp.Sr));
        _fade = 1f;
        _on = true;
    }

    public void Render(Span<float> dst)
    {
        if (!_on && MathF.Abs(_lp) < 1e-6f)
        {
            dst.Clear();
            _lp = 0;
            return;
        }
        float phase = _phase, lp = _lp, lpA = _lpA, fade = _fade, hz = _hz, vel = _vel, drive = Drive, bend = _bend;
        int t = _t, len = _len;
        var on = _on;
        var invLen = 1f / len;
        for (var i = 0; i < dst.Length; i++)
        {
            var s = 0f;
            if (on)
            {
                var x = MathF.Min(1f, t * invLen);
                var x2 = x * x;
                var x4 = x2 * x2;
                phase += hz * (1f + bend * x4 * x4) * Dsp.InvSr;
                if (phase >= 1f)
                    phase -= 1f;
                if (t >= len)
                {
                    fade -= 1f / 88f;
                    if (fade <= 0)
                    {
                        fade = 0;
                        on = false;
                    }
                }
                t++;
                var env = (0.08f + 0.92f * x2 * x) * fade * vel;
                s = Dsp.Tanh(MathF.Sin(phase * Dsp.TwoPi) * drive * (0.4f + env)) * env;
            }
            lp += lpA * (s - lp);
            if (MathF.Abs(lp) < 1e-15f)
                lp = 0;
            dst[i] = lp;
        }
        _phase = phase;
        _lp = lp;
        _fade = fade;
        _t = t;
        _on = on;
    }
}

/// <summary>A word the vocal stab voice can shout.</summary>
public enum VocalWord : byte
{
    Hey,
    Oi,
    Ha,
    Yah,
}

/// <summary>
/// Synthesised chant stabs ("HEY!", "OI!", "HA!", "YAH!"): a small crowd of three detuned buzzing saws with a falling
/// shouted pitch, plus breath noise for an "h", through three formant band-passes that glide from one vowel to the
/// next. No samples: it's a cartoon of a crowd shouting, which is exactly right for hardbass.
/// </summary>
internal sealed class VocalVoice
{
    // Formants (F1, F2, F3 in Hz) of the vowels a, e, i, o.
    private static readonly float[] VowelA = [780, 1220, 2600], VowelE = [480, 1850, 2550], VowelI = [300, 2250, 3000], VowelO = [520, 860, 2500];
    private static readonly float[] FormantGain = [1f, 0.7f, 0.35f], FormantBw = [90f, 130f, 190f];

    public float Drive = 2.2f;

    private readonly float[] _from = new float[3], _to = new float[3];
    private Svf _f1, _f2, _f3;
    private float _p1, _p2, _p3, _hz, _hzEnd, _pitchCoef, _env, _vel, _breath, _x, _xInc, _k1, _k2, _k3;
    private float _a11, _a12, _a13, _a21, _a22, _a23, _a31, _a32, _a33;
    private int _t, _attack, _hold, _ctl;
    private float _decay;
    private bool _on;

    public void Reset()
    {
        _f1.Reset();
        _f2.Reset();
        _f3.Reset();
        _p1 = _p2 = _p3 = _env = _breath = _x = 0;
        _on = false;
        _t = _ctl = 0;
    }

    /// <summary>Shouts a word at a base pitch.</summary>
    public void Trigger(VocalWord word, float hz, float vel)
    {
        float[] a, b;
        float breath, length;
        switch (word)
        {
            case VocalWord.Hey: a = VowelE; b = VowelI; breath = 0.9f; length = 0.2f; break;
            case VocalWord.Oi: a = VowelO; b = VowelI; breath = 0f; length = 0.24f; break;
            case VocalWord.Ha: a = VowelA; b = VowelO; breath = 1f; length = 0.15f; break;
            default: a = VowelI; b = VowelA; breath = 0.2f; length = 0.22f; break;
        }
        for (var i = 0; i < 3; i++)
        {
            _from[i] = a[i];
            _to[i] = b[i];
        }
        _breath = breath * vel;
        _hz = hz * 1.19f; // shouted: starts three semitones high and falls
        _hzEnd = hz * 0.89f;
        _pitchCoef = 1f - Dsp.Decay(length * 0.6f);
        _vel = vel;
        _env = 0;
        _t = 0;
        _ctl = 0;
        _attack = (int)(0.006f * Dsp.Sr) + (breath > 0 ? (int)(0.03f * Dsp.Sr) : 0); // the "h" comes before the vowel
        _hold = _attack + (int)(length * 0.35f * Dsp.Sr);
        _decay = Dsp.Decay(length * 0.2f);
        _x = 0;
        _xInc = 1f / (length * 0.7f * Dsp.Sr);
        _on = true;
    }

    public void Render(Span<float> dst, ref MusicRng rng)
    {
        if (!_on)
        {
            dst.Clear();
            return;
        }
        Svf f1 = _f1, f2 = _f2, f3 = _f3;
        float p1 = _p1, p2 = _p2, p3 = _p3, hz = _hz, hzEnd = _hzEnd, pitchCoef = _pitchCoef, env = _env, vel = _vel, breath = _breath, x = _x, xInc = _xInc;
        float k1 = _k1, k2 = _k2, k3 = _k3, decay = _decay, drive = Drive;
        float a11 = _a11, a12 = _a12, a13 = _a13, a21 = _a21, a22 = _a22, a23 = _a23, a31 = _a31, a32 = _a32, a33 = _a33;
        int t = _t, attack = _attack, hold = _hold, ctl = _ctl;
        var on = true;
        float g1 = FormantGain[0], g2 = FormantGain[1], g3 = FormantGain[2];
        for (var i = 0; i < dst.Length; i++)
        {
            if ((ctl++ & 15) == 0)
            {
                // Formants glide from the first vowel to the second (smoothstep).
                var sx = x * x * (3f - 2f * x);
                var c1 = _from[0] + (_to[0] - _from[0]) * sx;
                var c2 = _from[1] + (_to[1] - _from[1]) * sx;
                var c3 = _from[2] + (_to[2] - _from[2]) * sx;
                k1 = FormantBw[0] / c1;
                k2 = FormantBw[1] / c2;
                k3 = FormantBw[2] / c3;
                Svf.Coefs(Dsp.SvfG(c1), k1, out a11, out a12, out a13);
                Svf.Coefs(Dsp.SvfG(c2), k2, out a21, out a22, out a23);
                Svf.Coefs(Dsp.SvfG(c3), k3, out a31, out a32, out a33);
            }
            // Envelope: breath first (for an "h"), a quick attack, a hold, then the decay.
            var voiced = t >= attack - (int)(0.006f * Dsp.Sr);
            if (t < attack)
                env = MathF.Min(1f, env + 1f / 200f);
            else if (t > hold)
                env *= decay;
            t++;
            if (t > attack)
                x = MathF.Min(1f, x + xInc);
            hz += (hzEnd - hz) * pitchCoef;
            var dt = hz * Dsp.InvSr;
            float d1 = dt * 1.012f, d2 = dt * 0.991f, d3 = dt * 1.003f;
            p1 += d1;
            if (p1 >= 1f)
                p1 -= 1f;
            p2 += d2;
            if (p2 >= 1f)
                p2 -= 1f;
            p3 += d3;
            if (p3 >= 1f)
                p3 -= 1f;
            var src = 0f;
            if (voiced)
                src = (Dsp.Saw(p1, d1) + Dsp.Saw(p2, d2) + Dsp.Saw(p3, d3)) * 0.33f;
            var noise = rng.Noise() * (t < attack ? breath : breath * 0.15f);
            var exc = src + noise;
            f1.Process(exc, k1, a11, a12, a13);
            f2.Process(exc, k2, a21, a22, a23);
            f3.Process(exc, k3, a31, a32, a33);
            var y = (f1.Band * k1 * g1 + f2.Band * k2 * g2 + f3.Band * k3 * g3) * 3f;
            dst[i] = Dsp.Tanh(y * drive) * env * vel;
            if (t > hold && env < 1e-3f)
            {
                on = false;
                for (var j = i + 1; j < dst.Length; j++)
                    dst[j] = 0;
                break;
            }
        }
        if (!on)
        {
            f1.Reset();
            f2.Reset();
            f3.Reset();
        }
        _f1 = f1;
        _f2 = f2;
        _f3 = f3;
        _p1 = p1;
        _p2 = p2;
        _p3 = p3;
        _hz = hz;
        _env = env;
        _x = x;
        _k1 = k1;
        _k2 = k2;
        _k3 = k3;
        (_a11, _a12, _a13, _a21, _a22, _a23, _a31, _a32, _a33) = (a11, a12, a13, a21, a22, a23, a31, a32, a33);
        _t = t;
        _ctl = ctl;
        _on = on;
    }
}

/// <summary>
/// Master bus limiter: a 2 ms look-ahead peak limiter (fast attack, smooth release) followed by a soft safety clipper
/// that never goes past <see cref="SafetyCeiling"/>. It lets a track be driven loud without hard clipping.
/// </summary>
internal sealed class MasterLimiter
{
    public const int Lookahead = 88; // 2 ms
    public const float SafetyCeiling = 0.985f;
    private const float Knee = 0.9f;

    /// <summary>Peak level the limiter aims for (before the safety clipper).</summary>
    public float Ceiling = 0.89f, ReleaseTime = 0.08f;

    private readonly float[] _dl = new float[Lookahead], _dr = new float[Lookahead];
    private float _gain = 1f, _attackCoef, _releaseCoef;
    private int _pos;

    /// <summary>The lowest gain applied since the last reset (for the self-test).</summary>
    public float MinGain { get; private set; } = 1f;

    public void Configure()
    {
        _attackCoef = 1f - MathF.Exp(-5f / Lookahead);
        _releaseCoef = 1f - Dsp.Decay(ReleaseTime);
    }

    public void Reset()
    {
        Array.Clear(_dl);
        Array.Clear(_dr);
        _gain = 1f;
        _pos = 0;
        MinGain = 1f;
    }

    /// <summary>Limits an interleaved stereo block in place (delays it by <see cref="Lookahead"/> samples).</summary>
    public void Process(Span<float> stereo)
    {
        var dl = _dl;
        var dr = _dr;
        float gain = _gain, ceiling = Ceiling, attack = _attackCoef, release = _releaseCoef, minGain = MinGain;
        var pos = _pos;
        for (var i = 0; i < stereo.Length; i += 2)
        {
            float l = stereo[i], r = stereo[i + 1];
            var peak = MathF.Max(MathF.Abs(l), MathF.Abs(r));
            var target = peak > ceiling ? ceiling / peak : 1f;
            gain += (target - gain) * (target < gain ? attack : release);
            if (gain < minGain)
                minGain = gain;
            float ol = dl[pos] * gain, or = dr[pos] * gain;
            dl[pos] = l;
            dr[pos] = r;
            if (++pos >= Lookahead)
                pos = 0;
            stereo[i] = SoftClip(ol);
            stereo[i + 1] = SoftClip(or);
        }
        _gain = gain;
        _pos = pos;
        MinGain = minGain;
    }

    /// <summary>Linear below the knee, then a tanh curve into <see cref="SafetyCeiling"/>.</summary>
    public static float SoftClip(float x)
    {
        var a = MathF.Abs(x);
        if (a <= Knee)
            return x;
        const float room = SafetyCeiling - Knee;
        var y = Knee + room * Dsp.Tanh((a - Knee) / room);
        return x < 0 ? -y : y;
    }
}
