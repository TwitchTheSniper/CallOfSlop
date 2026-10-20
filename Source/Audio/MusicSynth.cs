namespace CallOfSlop;

// The instruments of the procedural music engine (see MusicRenderer.cs). Every voice keeps its state in fields and
// fixed arrays allocated once, so rendering never allocates. All voices run at 44.1 kHz, one sample at a time.

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

    /// <summary>g from <see cref="Dsp.SvfG"/>; k = 2 - 2*resonance (2 = no resonance, small = sharp peak).</summary>
    public void Process(float x, float g, float k)
    {
        var a1 = 1f / (1f + g * (g + k));
        var a2 = g * a1;
        var a3 = g * a2;
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

/// <summary>
/// Kick drum: a sine with a fast downward pitch sweep, held then decaying, driven into a soft clipper. With a long hold,
/// heavy drive and the end pitch tuned to the key, it becomes the distorted, tonal hardstyle / gabber kick.
/// </summary>
internal sealed class KickVoice
{
    public float StartHz = 200, EndHz = 50, PitchTime = 0.03f, Hold = 0.02f, DecayTime = 0.15f, Drive = 2f, ToneHz = 3000, Click = 0.3f;

    /// <summary>Sidechain envelope: jumps to 1 on every kick and falls back; the bass and synths duck under it.</summary>
    public float Duck;

    private float _phase, _pitch, _amp, _ampCoef, _pitchCoef, _click, _lpA, _lp, _duckCoef;
    private int _hold;

    public void Configure(float duckTime)
    {
        _ampCoef = Dsp.Decay(DecayTime);
        _pitchCoef = Dsp.Decay(PitchTime);
        _lpA = Dsp.OnePole(ToneHz);
        _duckCoef = Dsp.Decay(duckTime);
    }

    public void Reset()
    {
        _phase = _pitch = _amp = _click = _lp = Duck = 0;
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
        Duck = 1;
    }

    public float Next(ref MusicRng rng)
    {
        Duck *= _duckCoef;
        if (Duck < 1e-4f)
            Duck = 0;
        if (_amp <= 0 && MathF.Abs(_lp) < 1e-5f)
            return 0;
        var hz = EndHz + (StartHz - EndHz) * _pitch;
        _pitch *= _pitchCoef;
        _phase += hz * Dsp.InvSr;
        if (_phase >= 1f)
            _phase -= 1f;
        var s = MathF.Sin(_phase * Dsp.TwoPi) * _amp;
        if (_hold > 0)
        {
            _hold--;
        }
        else
        {
            _amp *= _ampCoef;
            if (_amp < 1e-4f)
                _amp = 0;
        }
        s = Dsp.Tanh(s * Drive);
        _lp += _lpA * (s - _lp);
        var c = 0f;
        if (_click > 1e-4f)
        {
            c = rng.Noise() * _click;
            _click *= 0.95f;
        }
        return _lp + c;
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
    private float _env, _tone, _phase, _vel, _nCoef, _tCoef, _g, _toneHz;
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
        _g = Dsp.SvfG(NoiseHz * PitchMul);
        _toneHz = ToneHz * PitchMul;
    }

    public float Next(ref MusicRng rng)
    {
        if (_env <= 0 && _tone <= 0)
            return 0;
        _f.Process(rng.Noise(), _g, 1.1f);
        var envN = _env;
        if (Clap && _t < ClapGap * 3)
            envN = _vel * (1f - (_t % ClapGap) / (float)ClapGap);
        _t++;
        var s = (_f.Band * 1.6f + _f.High * 0.5f) * envN;
        if (_tone > 0)
        {
            _phase += _toneHz * Dsp.InvSr;
            if (_phase >= 1f)
                _phase -= 1f;
            s += MathF.Sin(_phase * Dsp.TwoPi) * _tone;
            _tone *= _tCoef;
            if (_tone < 1e-4f)
                _tone = 0;
        }
        if (!Clap || _t >= ClapGap * 3)
        {
            _env *= _nCoef;
            if (_env < 1e-4f)
                _env = 0;
        }
        return Dsp.Tanh(s * Drive);
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

    public float Next(ref MusicRng rng)
    {
        if (_env <= 0)
            return 0;
        var n = rng.Noise();
        _lp += 0.62f * (n - _lp); // ~7 kHz
        var h = n - _lp;
        _lp2 += 0.85f * (h - _lp2); // tame the very top
        var s = _lp2 * _env;
        _env *= _coef;
        if (_env < 1e-4f)
            _env = 0;
        return s;
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

    private readonly float[] _phase = new float[MaxNotes * MaxSaws];
    private readonly float[] _hz = new float[MaxNotes], _target = new float[MaxNotes], _env = new float[MaxNotes], _vel = new float[MaxNotes];
    private readonly byte[] _stage = new byte[MaxNotes]; // 0 off, 1 attack, 2 decay/sustain, 3 release
    private readonly float[] _spread = new float[MaxSaws], _panL = new float[MaxSaws], _panR = new float[MaxSaws];
    private readonly float[] _hSpread = new float[MaxSaws], _hPanL = new float[MaxSaws], _hPanR = new float[MaxSaws];
    private float _fenv, _fCoef, _aInc, _dCoef, _rCoef, _gCoef, _g, _k, _norm, _hNorm, _dtMul = 1f, _cutMul = 1f, _curL, _curR;
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
        // At half rate every per-sample step covers two output samples.
        var t = HalfRate ? 0.5f : 1f;
        _dtMul = HalfRate ? 2f : 1f;
        _cutMul = _dtMul;
        _fCoef = Dsp.Decay(FilterTime * t);
        _aInc = 1f / MathF.Max(1f, Attack * Dsp.Sr * t);
        _dCoef = Dsp.Decay(DecayTime * t);
        _rCoef = Dsp.Decay(Release * t);
        _gCoef = Glide > 0 ? 1f - Dsp.Decay(Glide * t) : 1f;
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
        _fenv = _g = _curL = _curR = 0;
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
            _hz[slot] = hz;
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

    public void Next(out float left, out float right)
    {
        if (!HalfRate)
        {
            Compute(out left, out right);
            return;
        }
        // Half rate: a new value every other sample, linearly interpolated in between.
        if (_odd)
        {
            _odd = false;
            left = _curL;
            right = _curR;
            return;
        }
        _odd = true;
        float pl = _curL, pr = _curR;
        Compute(out _curL, out _curR);
        left = 0.5f * (pl + _curL);
        right = 0.5f * (pr + _curR);
    }

    private void Compute(out float left, out float right)
    {
        float sl = 0, sr = 0;
        var any = false;
        for (var n = 0; n < MaxNotes; n++)
        {
            var st = _stage[n];
            if (st == 0)
                continue;
            any = true;
            var e = _env[n];
            if (st == 1)
            {
                e += _aInc;
                if (e >= 1f)
                {
                    e = 1f;
                    _stage[n] = 2;
                }
            }
            else if (st == 2)
            {
                e = Sustain + (e - Sustain) * _dCoef;
            }
            else
            {
                e *= _rCoef;
                if (e < 1e-3f) // -60 dB: inaudible, and the oscillators stop costing anything
                {
                    e = 0;
                    _stage[n] = 0;
                }
            }
            _env[n] = e;
            if (Glide > 0)
                _hz[n] += (_target[n] - _hz[n]) * _gCoef;
            var harmony = n > 0;
            var saws = harmony ? HarmonySaws : Saws;
            var spread = harmony ? _hSpread : _spread;
            var panL = harmony ? _hPanL : _panL;
            var panR = harmony ? _hPanR : _panR;
            var amp = e * _vel[n] * (harmony ? _hNorm : _norm);
            var baseDt = _hz[n] * Dsp.InvSr * _dtMul;
            var off = n * MaxSaws;
            for (var j = 0; j < saws; j++)
            {
                var dt = baseDt * spread[j];
                var ph = _phase[off + j] + dt;
                if (ph >= 1f)
                    ph -= 1f;
                _phase[off + j] = ph;
                var v = (Square ? Dsp.Square(ph, dt) * 0.5f : Dsp.Saw(ph, dt)) * amp;
                sl += v * panL[j];
                sr += v * panR[j];
            }
        }
        if (!any && MathF.Abs(_l.Low) + MathF.Abs(_r.Low) < 1e-6f)
        {
            left = right = 0;
            return;
        }
        if ((_ctl++ & 15) == 0) // filter coefficients at control rate (every 16 samples)
            _g = Dsp.SvfG(Cutoff * CutoffMul * (1f + EnvAmount * _fenv) * _cutMul);
        _fenv *= _fCoef;
        if (_fenv < 1e-5f)
            _fenv = 0;
        _l.Process(Dsp.Tanh(sl * Drive), _g, _k);
        _r.Process(Dsp.Tanh(sr * Drive), _g, _k);
        left = _l.Low;
        right = _r.Low;
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
        _gCoef = Glide > 0 ? 1f - Dsp.Decay(Glide) : 1f;
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
            _hz = hz;
        _stage = 1;
        _vel = vel;
        _fenv = 1f;
    }

    public void NoteOff()
    {
        if (_stage != 0)
            _stage = 3;
    }

    public float Next()
    {
        if (_stage == 0 && MathF.Abs(_f.Low) < 1e-6f)
            return 0;
        if (_stage == 1)
        {
            _env += 1f / 88f; // 2 ms attack
            if (_env >= 1f)
            {
                _env = 1f;
                _stage = 2;
            }
        }
        else if (_stage == 2)
        {
            _env = Sustain + (_env - Sustain) * _dCoef;
        }
        else if (_stage == 3)
        {
            _env *= _rCoef;
            if (_env < 1e-4f)
            {
                _env = 0;
                _stage = 0;
            }
        }
        _hz += (_target - _hz) * _gCoef;
        var dt = _hz * Dsp.InvSr;
        var dt1 = dt * (1f + Detune);
        var dt2 = dt * (1f - Detune);
        _p1 += dt1;
        if (_p1 >= 1f)
            _p1 -= 1f;
        _p2 += dt2;
        if (_p2 >= 1f)
            _p2 -= 1f;
        _ps += dt * 0.5f;
        if (_ps >= 1f)
            _ps -= 1f;
        var raw = (Dsp.Saw(_p1, dt1) + Dsp.Saw(_p2, dt2)) * 0.5f;
        if ((_ctl++ & 15) == 0)
        {
            var lfo = 1f;
            if (Wobble > 0)
            {
                _lfo += 16f * 0.23f * Dsp.InvSr; // slow sweep, ~4 s per cycle
                if (_lfo >= 1f)
                    _lfo -= 1f;
                lfo = 1f + Wobble * MathF.Sin(_lfo * Dsp.TwoPi);
            }
            _g = Dsp.SvfG(Cutoff * CutoffMul * lfo * (1f + EnvAmount * _fenv));
        }
        _fenv *= _fCoef;
        if (_fenv < 1e-5f)
            _fenv = 0;
        _f.Process(Dsp.Tanh(raw * Drive), _g, _k);
        var amp = _env * _vel;
        return (_f.Low + MathF.Sin(_ps * Dsp.TwoPi) * Sub) * amp;
    }
}

/// <summary>Crash cymbal (wide high-passed noise with a long tail), riser (sweeping band-passed noise) and sub drop.</summary>
internal sealed class FxVoices
{
    /// <summary>Riser level (0..1) and centre frequency, set by the arrangement every step.</summary>
    public float RiserLevel, RiserHz = 400;

    private float _crash, _crashCoef, _lpL, _lpR, _subAmp, _subPitch, _subPhase, _subCoef, _subPitchCoef, _riserLevel, _g;
    private int _ctl;
    private Svf _riser;

    public void Configure()
    {
        _crashCoef = Dsp.Decay(0.9f);
        _subCoef = Dsp.Decay(0.45f);
        _subPitchCoef = Dsp.Decay(0.35f);
    }

    public void Reset()
    {
        _crash = _subAmp = RiserLevel = _riserLevel = _lpL = _lpR = _subPitch = _subPhase = _g = 0;
        RiserHz = 400;
        _ctl = 0;
        _riser.Reset();
    }

    public void Crash(float vel) => _crash = MathF.Max(_crash, vel);

    public void SubDrop(float vel)
    {
        _subAmp = vel;
        _subPitch = 1f;
        _subPhase = 0;
    }

    public void Next(ref MusicRng rng, out float left, out float right)
    {
        float l = 0, r = 0;
        if (_crash > 0)
        {
            var nl = rng.Noise();
            var nr = rng.Noise();
            _lpL += 0.55f * (nl - _lpL);
            _lpR += 0.55f * (nr - _lpR);
            l += (nl - _lpL) * _crash;
            r += (nr - _lpR) * _crash;
            _crash *= _crashCoef;
            if (_crash < 1e-4f)
                _crash = 0;
        }
        _riserLevel += (RiserLevel - _riserLevel) * 0.001f; // smooth the per-step changes
        if (_riserLevel > 1e-4f)
        {
            if ((_ctl++ & 15) == 0)
                _g = Dsp.SvfG(RiserHz);
            _riser.Process(rng.Noise(), _g, 0.5f);
            var v = _riser.Band * _riserLevel;
            l += v;
            r += v;
        }
        if (_subAmp > 0)
        {
            var hz = 32f + 70f * _subPitch;
            _subPitch *= _subPitchCoef;
            _subPhase += hz * Dsp.InvSr;
            if (_subPhase >= 1f)
                _subPhase -= 1f;
            var v = MathF.Sin(_subPhase * Dsp.TwoPi) * _subAmp;
            _subAmp *= _subCoef;
            if (_subAmp < 1e-4f)
                _subAmp = 0;
            l += v;
            r += v;
        }
        left = l;
        right = r;
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

    public void Process(float input, out float left, out float right)
    {
        var dl = _l[_pos];
        var dr = _r[_pos];
        _lpL += 0.4f * (dl - _lpL);
        _lpR += 0.4f * (dr - _lpR);
        if (MathF.Abs(_lpL) < 1e-15f)
            _lpL = 0;
        if (MathF.Abs(_lpR) < 1e-15f)
            _lpR = 0;
        _l[_pos] = input + _lpR * Feedback;
        _r[_pos] = _lpL * Feedback;
        if (++_pos >= _len)
            _pos = 0;
        left = dl;
        right = dr;
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

    public void Process(float input, out float left, out float right)
    {
        var x = input * 0.03f;
        float l = 0, r = 0;
        for (var i = 0; i < 4; i++)
        {
            l += Comb(_combL[i], ref _ciL[i], ref _storeL[i], x);
            r += Comb(_combR[i], ref _ciR[i], ref _storeR[i], x);
        }
        for (var i = 0; i < 2; i++)
        {
            l = Allpass(_apL[i], ref _aiL[i], l);
            r = Allpass(_apR[i], ref _aiR[i], r);
        }
        left = l;
        right = r;
    }

    private float Comb(float[] buf, ref int idx, ref float store, float x)
    {
        var y = buf[idx];
        store = y * (1f - Damp) + store * Damp;
        if (MathF.Abs(store) < 1e-15f)
            store = 0;
        buf[idx] = x + store * Feedback;
        if (++idx >= buf.Length)
            idx = 0;
        return y;
    }

    private static float Allpass(float[] buf, ref int idx, float x)
    {
        var b = buf[idx];
        var y = b - x;
        var w = x + b * 0.5f;
        buf[idx] = MathF.Abs(w) < 1e-15f ? 0f : w;
        if (++idx >= buf.Length)
            idx = 0;
        return y;
    }
}
