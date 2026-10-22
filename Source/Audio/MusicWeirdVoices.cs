namespace CallOfSlop;

// Extra instruments and effects for the weird hardbass tracks (see MusicWeird.cs and MusicRenderer.Weird.cs). Like the
// rest of the synth: state in fields, buffers allocated once, a block at a time, no allocation while rendering.

/// <summary>A sound effect the toy voice can make.</summary>
public enum ToySound : byte
{
    Quack, // a rubber duck (for the game's ducks)
    Squeak, // a squeaky toy: squeeze and release
    WhistleUp, // slide whistle up
    WhistleDown, // slide whistle down
    Boing, // a cartoon spring
}

/// <summary>
/// Polka accordion: two notes (the melody and a third below), each three reeds of a narrow pulse wave: one a touch
/// flat, one a touch sharp (the "wet" musette tuning that beats against itself) and one an octave down, through a
/// two-pole low-pass and a soft clipper. The flat and sharp reeds sit left and right.
/// </summary>
internal sealed class AccordionVoice
{
    public const int Slots = 2;
    private const float Duty = 0.38f;

    private readonly float[] _p1 = new float[Slots], _p2 = new float[Slots], _p3 = new float[Slots];
    private readonly float[] _hz = new float[Slots], _env = new float[Slots], _vel = new float[Slots];
    private readonly byte[] _stage = new byte[Slots]; // 0 off, 1 on, 2 release
    private float _aInc, _rCoef, _lpA, _l1, _l2, _r1, _r2;

    public AccordionVoice()
    {
        _aInc = 1f / (0.014f * Dsp.Sr);
        _rCoef = Dsp.Decay(0.05f);
        _lpA = Dsp.OnePole(3200f);
    }

    public void Reset()
    {
        Array.Clear(_stage);
        Array.Clear(_env);
        Array.Clear(_p1);
        Array.Clear(_p2);
        Array.Clear(_p3);
        _l1 = _l2 = _r1 = _r2 = 0;
    }

    public bool Active => _stage[0] != 0 || _stage[1] != 0;

    public void NoteOn(int slot, float hz, float vel)
    {
        _hz[slot] = hz;
        _vel[slot] = vel;
        if (_stage[slot] == 0)
        {
            _p1[slot] = 0.1f;
            _p2[slot] = 0.6f;
            _p3[slot] = 0.35f;
        }
        _stage[slot] = 1;
    }

    public void AllOff()
    {
        for (var s = 0; s < Slots; s++)
            if (_stage[s] != 0)
                _stage[s] = 2;
    }

    private static float Pulse(float p, float dt)
    {
        var q = p + (1f - Duty);
        if (q >= 1f)
            q -= 1f;
        return Dsp.Saw(p, dt) - Dsp.Saw(q, dt);
    }

    /// <summary>Adds a block into left and right.</summary>
    public void Render(Span<float> left, Span<float> right)
    {
        if (!Active && MathF.Abs(_l2) + MathF.Abs(_r2) < 1e-6f)
            return;
        var n = left.Length;
        float lpA = _lpA, l1 = _l1, l2 = _l2, r1 = _r1, r2 = _r2;
        for (var i = 0; i < n; i++)
        {
            float sl = 0, sr = 0;
            for (var s = 0; s < Slots; s++)
            {
                if (_stage[s] == 0)
                    continue;
                var env = _env[s];
                if (_stage[s] == 1)
                    env = MathF.Min(1f, env + _aInc);
                else
                {
                    env *= _rCoef;
                    if (env < 1e-4f)
                    {
                        _stage[s] = 0;
                        env = 0;
                    }
                }
                _env[s] = env;
                var hz = _hz[s];
                float d1 = hz * 0.9965f * Dsp.InvSr, d2 = hz * 1.0045f * Dsp.InvSr, d3 = hz * 0.5f * Dsp.InvSr;
                var p1 = _p1[s] + d1;
                if (p1 >= 1f)
                    p1 -= 1f;
                var p2 = _p2[s] + d2;
                if (p2 >= 1f)
                    p2 -= 1f;
                var p3 = _p3[s] + d3;
                if (p3 >= 1f)
                    p3 -= 1f;
                _p1[s] = p1;
                _p2[s] = p2;
                _p3[s] = p3;
                var a = env * _vel[s];
                var low = Pulse(p3, d3) * 0.45f;
                sl += (Pulse(p1, d1) + low) * a;
                sr += (Pulse(p2, d2) + low) * a;
            }
            l1 += (sl - l1) * lpA;
            l2 += (l1 - l2) * lpA;
            r1 += (sr - r1) * lpA;
            r2 += (r1 - r2) * lpA;
            left[i] += Dsp.Tanh(l2 * 1.3f) * 0.8f;
            right[i] += Dsp.Tanh(r2 * 1.3f) * 0.8f;
        }
        if (MathF.Abs(l1) + MathF.Abs(l2) + MathF.Abs(r1) + MathF.Abs(r2) < 1e-12f)
            l1 = l2 = r1 = r2 = 0;
        _l1 = l1;
        _l2 = l2;
        _r1 = r1;
        _r2 = r2;
    }
}

/// <summary>
/// An 8-bit sound chip channel: a pulse wave whose duty steps from 12.5% to 25%, a volume envelope that steps at 60 Hz
/// with 15 levels, and output quantised to 4 bits. It plays single notes (a chip lead) or a chord as a fast arpeggio
/// that cycles a note every frame, the way old consoles faked chords.
/// </summary>
internal sealed class ChipVoice
{
    private const int FrameSamples = Dsp.Sr / 60;

    private readonly float[] _notes = new float[3];
    private int _count, _index, _frameLeft, _frames, _gate;
    private float _phase, _level, _decayPerFrame, _vel;
    private bool _on;

    public void Reset()
    {
        _on = false;
        _phase = _level = 0;
        _count = _index = _frames = _gate = 0;
        _frameLeft = FrameSamples;
    }

    public bool Active => _on;

    /// <summary>A chord arpeggio held for a while, then fading out in steps.</summary>
    public void Arp(float a, float b, float c, float vel, float seconds)
    {
        _notes[0] = a;
        _notes[1] = b;
        _notes[2] = c;
        _count = 3;
        Start(vel, seconds, 0.12f);
    }

    /// <summary>A single held note (until the next note or <see cref="Off"/>).</summary>
    public void Note(float hz, float vel)
    {
        _notes[0] = hz;
        _count = 1;
        Start(vel, 10f, 0.02f);
    }

    public void Off()
    {
        _gate = 0;
        _decayPerFrame = MathF.Max(_decayPerFrame, 0.2f);
    }

    private void Start(float vel, float seconds, float decay)
    {
        _vel = vel;
        _level = 1f;
        _index = 0;
        _frames = 0;
        _frameLeft = FrameSamples;
        _gate = Math.Max(1, (int)(seconds * 60f));
        _decayPerFrame = decay;
        _on = true;
    }

    /// <summary>Adds a block into a mono buffer.</summary>
    public void Render(Span<float> dst)
    {
        if (!_on)
            return;
        float phase = _phase, level = _level;
        int frameLeft = _frameLeft;
        var hz = _notes[_index];
        var duty = _frames < 2 ? 0.125f : 0.25f;
        var amp = MathF.Round(level * 15f) / 15f * _vel;
        for (var i = 0; i < dst.Length; i++)
        {
            if (--frameLeft <= 0)
            {
                frameLeft = FrameSamples;
                _frames++;
                if (_count > 1)
                    _index = (_index + 1) % _count;
                hz = _notes[_index];
                duty = _frames < 2 ? 0.125f : 0.25f;
                if (_gate > 0)
                    _gate--;
                else
                    level -= _decayPerFrame;
                if (level <= 0f)
                {
                    level = 0f;
                    _on = false;
                    break;
                }
                amp = MathF.Round(level * 15f) / 15f * _vel;
            }
            phase += hz * Dsp.InvSr;
            if (phase >= 1f)
                phase -= 1f;
            var x = (phase < duty ? 1f : 0f) - duty; // a narrow pulse with its DC taken out
            dst[i] += MathF.Round(x * amp * 7f) / 7f;
        }
        _phase = phase;
        _level = level;
        _frameLeft = frameLeft;
    }
}

/// <summary>
/// Music box: plucked steel tines as damped resonators (two multiplies per partial per sample, no sines): each note is
/// its fundamental plus an inharmonic partial near 3.9x that dies fast, which gives the metallic "ting". Eight voices,
/// round robin, panned alternately.
/// </summary>
internal sealed class MusicBoxVoice
{
    private const int Voices = 8;
    private readonly float[] _c1 = new float[Voices], _r1 = new float[Voices], _a1 = new float[Voices], _b1 = new float[Voices];
    private readonly float[] _c2 = new float[Voices], _r2 = new float[Voices], _a2 = new float[Voices], _b2 = new float[Voices];
    private readonly float[] _amp = new float[Voices], _decay = new float[Voices];
    private int _next;

    public void Reset()
    {
        Array.Clear(_a1);
        Array.Clear(_b1);
        Array.Clear(_a2);
        Array.Clear(_b2);
        Array.Clear(_amp);
        _next = 0;
    }

    /// <summary>Plucks a tine.</summary>
    public void Strike(float hz, float vel)
    {
        var v = _next;
        _next = (_next + 1) % Voices;
        var w = Dsp.TwoPi * MathF.Min(hz, Dsp.Sr * 0.4f) * Dsp.InvSr;
        var r = Dsp.Decay(1.3f);
        _c1[v] = 2f * r * MathF.Cos(w);
        _r1[v] = r * r;
        _a1[v] = 0f;
        _b1[v] = -vel * MathF.Sin(w) / r;
        var hz2 = hz * 3.9f;
        if (hz2 < Dsp.Sr * 0.42f)
        {
            var w2 = Dsp.TwoPi * hz2 * Dsp.InvSr;
            var r2 = Dsp.Decay(0.18f);
            _c2[v] = 2f * r2 * MathF.Cos(w2);
            _r2[v] = r2 * r2;
            _a2[v] = 0f;
            _b2[v] = -vel * 0.5f * MathF.Sin(w2) / r2;
        }
        else
        {
            _c2[v] = _r2[v] = _a2[v] = _b2[v] = 0f;
        }
        _amp[v] = vel;
        _decay[v] = r;
    }

    /// <summary>Adds a block into left and right.</summary>
    public void Render(Span<float> left, Span<float> right)
    {
        var n = left.Length;
        for (var v = 0; v < Voices; v++)
        {
            if (_amp[v] < 1e-4f)
                continue;
            float c1 = _c1[v], r1 = _r1[v], a1 = _a1[v], b1 = _b1[v], c2 = _c2[v], r2 = _r2[v], a2 = _a2[v], b2 = _b2[v];
            float pl = (v & 1) == 0 ? 0.85f : 0.5f, pr = (v & 1) == 0 ? 0.5f : 0.85f;
            for (var i = 0; i < n; i++)
            {
                var y1 = c1 * a1 - r1 * b1;
                b1 = a1;
                a1 = y1;
                var y2 = c2 * a2 - r2 * b2;
                b2 = a2;
                a2 = y2;
                var y = y1 + y2;
                left[i] += y * pl;
                right[i] += y * pr;
            }
            _a1[v] = a1;
            _b1[v] = b1;
            _a2[v] = a2;
            _b2[v] = b2;
            _amp[v] *= MathF.Pow(_decay[v], n);
            if (_amp[v] < 1e-4f)
                _a1[v] = _b1[v] = _a2[v] = _b2[v] = 0;
        }
    }
}

/// <summary>
/// Toy sound effects, one at a time: a rubber duck quack (a buzzy source through two nasal formants, pitch flipping up
/// then down), a squeaky toy (two squeals, up on the squeeze and down on the release), slide whistles (a breathy sine
/// gliding up or down with a little vibrato) and a boing (a sine with a wobbling, decaying pitch).
/// </summary>
internal sealed class ToyFxVoice
{
    private ToySound _kind;
    private int _t, _len, _ctl;
    private float _phase, _phase2, _hz, _amp, _target, _lp;
    private Svf _f1, _f2;
    private float _k1, _a11, _a12, _a13, _a21, _a22, _a23;
    private bool _on;

    public bool Active => _on;

    public void Reset()
    {
        _on = false;
        _t = _ctl = 0;
        _phase = _phase2 = _amp = _target = _lp = 0;
        _f1.Reset();
        _f2.Reset();
    }

    public void Trigger(ToySound kind)
    {
        _kind = kind;
        _t = 0;
        _ctl = 0;
        _len = (int)(Dsp.Sr * kind switch
        {
            ToySound.Quack => 0.17f,
            ToySound.Squeak => 0.26f,
            ToySound.WhistleUp => 0.42f,
            ToySound.WhistleDown => 0.6f,
            _ => 0.55f,
        });
        _amp = 0;
        _phase = _phase2 = 0;
        _k1 = 0.35f;
        _f1.Reset();
        _f2.Reset();
        _on = true;
        Control();
    }

    /// <summary>Pitch, loudness and formants for the current time (every 16 samples).</summary>
    private void Control()
    {
        var x = _t / (float)_len; // 0..1 through the sound
        var sec = _t * Dsp.InvSr;
        switch (_kind)
        {
            case ToySound.Quack:
            {
                _hz = x < 0.12f ? 300f + 500f * x : 360f - 150f * (x - 0.12f);
                _target = x < 0.05f ? x / 0.05f : x > 0.75f ? (1f - x) / 0.25f : 1f;
                var f1 = 1100f - 450f * x;
                var f2 = 1650f - 300f * x;
                Svf.Coefs(Dsp.SvfG(f1), _k1, out _a11, out _a12, out _a13);
                Svf.Coefs(Dsp.SvfG(f2), _k1, out _a21, out _a22, out _a23);
                break;
            }
            case ToySound.Squeak:
            {
                var first = x < 0.42f;
                var y = first ? x / 0.42f : (x - 0.5f) / 0.5f;
                _hz = first ? 1500f + 900f * y : 2200f - 900f * MathF.Max(0f, y);
                _target = first ? MathF.Sin(MathF.PI * y) : x < 0.5f ? 0f : MathF.Sin(MathF.PI * Math.Clamp(y, 0f, 1f));
                break;
            }
            case ToySound.WhistleUp:
            case ToySound.WhistleDown:
            {
                var up = _kind == ToySound.WhistleUp;
                var y = x * x * (3f - 2f * x);
                _hz = (up ? 450f * MathF.Pow(4.2f, y) : 1900f * MathF.Pow(0.2f, y)) * (1f + 0.012f * MathF.Sin(Dsp.TwoPi * 6f * sec));
                _target = x < 0.06f ? x / 0.06f : x > 0.85f ? (1f - x) / 0.15f : 1f;
                break;
            }
            default: // Boing
            {
                var wob = MathF.Exp(-sec / 0.25f);
                _hz = (140f + 60f * x) * (1f + 0.3f * wob * MathF.Sin(Dsp.TwoPi * 13f * sec));
                _target = MathF.Exp(-sec / 0.16f) * MathF.Min(1f, sec / 0.004f);
                break;
            }
        }
    }

    /// <summary>Parabolic sine of a phase 0..1 (cheap, close enough for a toy).</summary>
    private static float Sine(float p)
    {
        var x = 2f * p - 1f;
        var y = 4f * x * (1f - MathF.Abs(x));
        y = 0.225f * (y * MathF.Abs(y) - y) + y;
        return -y;
    }

    /// <summary>Adds a block into a mono buffer.</summary>
    public void Render(Span<float> dst, ref MusicRng rng)
    {
        if (!_on)
            return;
        for (var i = 0; i < dst.Length; i++)
        {
            if ((_ctl++ & 15) == 0)
                Control();
            _amp += (_target - _amp) * 0.02f;
            var dt = _hz * Dsp.InvSr;
            _phase += dt;
            if (_phase >= 1f)
                _phase -= 1f;
            float y;
            switch (_kind)
            {
                case ToySound.Quack:
                {
                    var src = Dsp.Saw(_phase, dt) + rng.Noise() * 0.1f;
                    _f1.Process(src, _k1, _a11, _a12, _a13);
                    _f2.Process(src, _k1, _a21, _a22, _a23);
                    y = Dsp.Tanh((_f1.Band + _f2.Band * 0.8f) * 1.6f) * 0.75f;
                    break;
                }
                case ToySound.Squeak:
                    y = Sine(_phase) * 0.8f + Dsp.Saw(_phase, dt) * 0.15f;
                    break;
                case ToySound.Boing:
                    _phase2 += dt * 2f;
                    if (_phase2 >= 1f)
                        _phase2 -= 1f;
                    y = Sine(_phase) * 0.9f + Sine(_phase2) * 0.2f;
                    break;
                default: // whistles: a sine and a breath
                    _lp += (rng.Noise() - _lp) * 0.3f;
                    y = Sine(_phase) * 0.75f + _lp * 0.05f;
                    break;
            }
            dst[i] += y * _amp;
            if (++_t >= _len)
            {
                _on = false;
                _f1.Reset();
                _f2.Reset();
                break;
            }
        }
    }
}

/// <summary>
/// A tape-deck bus effect for the weird tracks' master: wow (a short delay line whose length wobbles, bending the pitch
/// of everything), a low-pass, a bitcrusher (sample-and-hold plus fewer levels) and vinyl crackle. The amounts glide to
/// their targets, so sections can switch them without clicks. The delay always runs (a fixed 6.8 ms latency), so turning
/// the wow on and off never jumps.
/// </summary>
internal sealed class TapeBus
{
    private const int Len = 1024, Base = 300, Depth = 220;
    private readonly float[] _l = new float[Len], _r = new float[Len];
    private int _w, _hold;
    private float _wow, _lpCoef = 1f, _crush, _crackle, _lfo1, _lfo2, _l1, _l2, _r1, _r2, _hl, _hr, _crack;

    /// <summary>Targets set by the arrangement: wow 0..1, low-pass cutoff (0 = off), crush 0..1, crackle 0..1.</summary>
    public float Wow, LowpassHz, Crush, Crackle;

    public void Reset()
    {
        Array.Clear(_l);
        Array.Clear(_r);
        _w = _hold = 0;
        _wow = _crush = _crackle = _lfo1 = _lfo2 = _l1 = _l2 = _r1 = _r2 = _hl = _hr = _crack = 0;
        _lpCoef = 1f;
        Wow = LowpassHz = Crush = Crackle = 0;
    }

    /// <summary>Processes an interleaved stereo block in place.</summary>
    public void Process(Span<float> stereo, ref MusicRng rng)
    {
        var n = stereo.Length / 2;
        if (n == 0)
            return;
        // Glide each amount a little per block (about 50 ms to settle).
        var k = MathF.Min(1f, n / 2200f);
        _wow += (Wow - _wow) * k;
        _crush += (Crush - _crush) * k;
        _crackle += (Crackle - _crackle) * k;
        var lpTarget = LowpassHz > 0 ? Dsp.OnePole(LowpassHz) : 1f;
        _lpCoef += (lpTarget - _lpCoef) * k;
        float wow = _wow * Depth, lpA = _lpCoef, crackle = _crackle, l1 = _l1, l2 = _l2, r1 = _r1, r2 = _r2, hl = _hl, hr = _hr, crack = _crack;
        float lfo1 = _lfo1, lfo2 = _lfo2;
        const float d1 = 0.75f / Dsp.Sr, d2 = 0.21f / Dsp.Sr;
        var crushOn = _crush > 0.01f;
        var holdLen = 1 + (int)(_crush * 5f);
        var levels = MathF.Pow(2f, 12f - 7f * _crush);
        var hold = _hold;
        var w = _w;
        for (var i = 0; i < n; i++)
        {
            float x = stereo[i * 2], y = stereo[i * 2 + 1];
            _l[w] = x;
            _r[w] = y;
            lfo1 += d1;
            if (lfo1 >= 1f)
                lfo1 -= 1f;
            lfo2 += d2;
            if (lfo2 >= 1f)
                lfo2 -= 1f;
            // Two slow triangle LFOs, so the wobble never repeats exactly.
            var tri = (MathF.Abs(lfo1 * 4f - 2f) - 1f) * 0.7f + (MathF.Abs(lfo2 * 4f - 2f) - 1f) * 0.3f;
            var delay = Base + wow * tri;
            var pos = w - delay;
            if (pos < 0)
                pos += Len;
            var p0 = (int)pos;
            if (p0 >= Len)
                p0 -= Len; // pos can round up to exactly Len
            var frac = pos - (int)pos;
            var p1 = p0 + 1 >= Len ? 0 : p0 + 1;
            x = _l[p0] + (_l[p1] - _l[p0]) * frac;
            y = _r[p0] + (_r[p1] - _r[p0]) * frac;
            if (++w >= Len)
                w = 0;

            l1 += (x - l1) * lpA;
            l2 += (l1 - l2) * lpA;
            r1 += (y - r1) * lpA;
            r2 += (r1 - r2) * lpA;
            x = l2;
            y = r2;
            if (crushOn)
            {
                if (--hold <= 0)
                {
                    hold = holdLen;
                    hl = MathF.Round(x * levels) / levels;
                    hr = MathF.Round(y * levels) / levels;
                }
                x = hl;
                y = hr;
            }
            if (crackle > 0.01f)
            {
                if (rng.Unit() < crackle * 0.0005f)
                    crack = rng.Noise() * 0.35f;
                crack *= 0.55f;
                var hiss = rng.Noise() * 0.006f * crackle;
                x += crack + hiss;
                y += crack * 0.8f + hiss;
            }
            stereo[i * 2] = x;
            stereo[i * 2 + 1] = y;
        }
        if (MathF.Abs(l1) + MathF.Abs(l2) + MathF.Abs(r1) + MathF.Abs(r2) < 1e-15f)
            l1 = l2 = r1 = r2 = 0;
        _l1 = l1;
        _l2 = l2;
        _r1 = r1;
        _r2 = r2;
        _hl = hl;
        _hr = hr;
        _crack = crack;
        _lfo1 = lfo1;
        _lfo2 = lfo2;
        _hold = hold;
        _w = w;
    }
}
