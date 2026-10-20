using System.Numerics;
using Brutal.Numerics;
using Brutal.OpenAlApi;

namespace CallOfSlop;

public enum Sfx
{
    DryFire,
    HitTick,
    Headshot,
    Kill,
    Hurt,
    Death,
    Respawn,
    Throw,
    Bounce,
    Explosion,
    SmokeHiss,
    ReconBeep,
    LevelUp,
    MeleeSwing,
    MeleeHit,
    COUNT,
}

/// <summary>
/// OpenAL (via Brutal.OpenAl) playback with 3D positioning for other players' shots.
/// Every sound is synthesised at startup: each weapon gets its own gunshot, reload and equip
/// sound built from its stats, so there are no audio files to ship.
/// </summary>
public sealed class Audio : IDisposable
{
    public const int SampleRate = 44100;
    private const int Voices = 48;

    private Device? _device;
    private Context? _context;
    private AlSource[] _sources = [];
    private readonly double[] _startedAt = new double[Voices];
    private AlBuffer[] _shots = [], _shotsSuppressed = [], _reloads = [], _equips = [];
    private readonly AlBuffer[] _sfx = new AlBuffer[(int)Sfx.COUNT];
    private readonly Random _rng = new(1234);

    public bool Enabled { get; private set; }
    public float MasterVolume = 0.8f;

    public void Init()
    {
        try
        {
            _device = Alc.OpenDevice();
            _context = _device.CreateContext();
            _context.MakeCurrent();
            _sources = Al.GenSources(Voices);
            for (var i = 0; i < _sources.Length; i++)
            {
                _sources[i].ReferenceDistance = 6f;
                _sources[i].RolloffFactor = 1.1f;
                _sources[i].MaxDistance = 120f;
            }

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var defs = Weapons.All;
            var shotPcm = new short[defs.Length][];
            var suppressedPcm = new short[defs.Length][];
            var reloadPcm = new short[defs.Length][];
            var equipPcm = new short[defs.Length][];
            Parallel.For(0, defs.Length, i =>
            {
                shotPcm[i] = Synth.Gunshot(defs[i]);
                // The same gun with a Suppressor attachment on it.
                var quiet = defs[i].Clone();
                quiet.Sound = SoundStyle.Suppressed;
                suppressedPcm[i] = defs[i].Sound is SoundStyle.Gun or SoundStyle.Boom ? Synth.Gunshot(quiet) : shotPcm[i];
                reloadPcm[i] = Synth.Reload(defs[i]);
                equipPcm[i] = Synth.Equip(defs[i]);
            });
            _shots = shotPcm.Select(Upload).ToArray();
            _shotsSuppressed = suppressedPcm.Select(Upload).ToArray();
            _reloads = reloadPcm.Select(Upload).ToArray();
            _equips = equipPcm.Select(Upload).ToArray();
            for (var i = 0; i < (int)Sfx.COUNT; i++)
                _sfx[i] = Upload(Synth.Effect((Sfx)i));

            Enabled = true;
            Console.WriteLine($"Audio: OpenAL ready, synthesised {defs.Length * 4 + (int)Sfx.COUNT} sounds in {sw.ElapsedMilliseconds} ms.");
        }
        catch (Exception ex)
        {
            Enabled = false;
            Console.WriteLine($"Audio disabled: {ex.Message}");
        }
    }

    private static AlBuffer Upload(short[] pcm)
    {
        var buffer = Al.GenBuffer();
        buffer.SetData(AlFormat.Mono16, System.Runtime.InteropServices.MemoryMarshal.AsBytes(pcm.AsSpan()), SampleRate);
        return buffer;
    }

    public void SetListener(Vector3 position, Vector3 forward)
    {
        if (!Enabled)
            return;
        Al.Listener.Position = new float3(position.X, position.Y, position.Z);
        Al.Listener.Orientation = (new float3(forward.X, forward.Y, forward.Z), new float3(0, 1, 0));
        Al.Listener.Gain = MasterVolume;
    }

    // ---- Playback -----------------------------------------------------------

    /// <summary>Plays a buffer. Null position = attached to the listener (our own gun / UI).</summary>
    private void Play(AlBuffer buffer, Vector3? position, float gain, float pitch)
    {
        if (!Enabled || buffer.IsNull())
            return;

        // Prefer a free voice, else steal the oldest one.
        var now = Now();
        var pick = 0;
        var oldest = double.MaxValue;
        for (var i = 0; i < _sources.Length; i++)
        {
            if (_sources[i].State != AlSourceState.Playing)
            {
                pick = i;
                break;
            }
            if (_startedAt[i] < oldest)
            {
                oldest = _startedAt[i];
                pick = i;
            }
        }

        var src = _sources[pick];
        src.Stop();
        src.Buffer = buffer;
        src.Gain = gain;
        src.Pitch = pitch;
        if (position is { } p)
        {
            src.SourceRelative = false;
            src.Position = new float3(p.X, p.Y, p.Z);
        }
        else
        {
            src.SourceRelative = true;
            src.Position = new float3(0, 0, 0);
        }
        src.Play();
        _startedAt[pick] = now;
    }

    private static double Now() => Brutal.GlfwApi.Glfw.Time;

    private float Jitter(float amount) => 1f + ((float)_rng.NextDouble() - 0.5f) * 2f * amount;

    // Every public sound call checks Enabled first: with no audio device the sound tables are empty.
    public void Shot(WeaponDef w, Vector3? position)
    {
        if (Enabled)
            Play(w.Suppressor ? _shotsSuppressed[w.Id] : _shots[w.Id], position, (position == null ? 0.75f : 1f) * (w.Suppressor ? 0.7f : 1f), Jitter(0.04f));
    }

    public void Reload(WeaponDef w, Vector3? position)
    {
        if (Enabled)
            Play(_reloads[w.Id], position, 0.8f, 1f);
    }

    public void Equip(WeaponDef w)
    {
        if (Enabled)
            Play(_equips[w.Id], null, 0.6f, Jitter(0.03f));
    }

    public void Effect(Sfx sfx, float gain = 0.7f)
    {
        if (Enabled)
            Play(_sfx[(int)sfx], null, gain, 1f);
    }

    /// <summary>A sound effect positioned in the world (explosions, bounces, beeps).</summary>
    public void Effect3D(Sfx sfx, System.Numerics.Vector3 position, float gain = 1f)
    {
        if (Enabled)
            Play(_sfx[(int)sfx], position, gain, Jitter(0.05f));
    }

    public void Dispose()
    {
        if (_context == null)
            return; // never initialised (no audio device): nothing to release
        if (_sources.Length > 0)
        {
            foreach (var s in _sources)
                s.Stop();
            Al.DeleteSources(_sources);
        }
        Al.DeleteBuffers(_shots);
        Al.DeleteBuffers(_shotsSuppressed);
        Al.DeleteBuffers(_reloads);
        Al.DeleteBuffers(_equips);
        Al.DeleteBuffers(_sfx.Where(b => !b.IsNull()).ToArray());
        Context.MakeCurrent(null);
        _context?.Dispose();
        _device?.Dispose();
        Enabled = false;
    }
}

/// <summary>Tiny procedural sound synthesiser (mono, 16-bit, 44.1 kHz).</summary>
public static class Synth
{
    private const int Sr = Audio.SampleRate;

    // ---- Building blocks --------------------------------------------------------

    private sealed class Buf(float seconds)
    {
        public readonly float[] S = new float[Math.Max(1, (int)(seconds * Sr))];
        public int Len => S.Length;
    }

    private static float Env(float t, float attack, float decay) =>
        t < 0 ? 0 : t < attack ? t / attack : MathF.Exp(-(t - attack) / decay);

    /// <summary>Filtered noise burst. cutoff in Hz (one-pole low-pass), highpass subtracts the low part.</summary>
    private static void Noise(Buf b, Random rng, float start, float gain, float attack, float decay, float cutoff, bool highpass = false, float length = 10f)
    {
        var a = 1f - MathF.Exp(-2f * MathF.PI * cutoff / Sr);
        float lp = 0;
        var i0 = (int)(start * Sr);
        var i1 = Math.Min(b.Len, i0 + (int)(Math.Min(length, decay * 9 + attack) * Sr));
        for (var i = Math.Max(0, i0); i < i1; i++)
        {
            var t = (i - i0) / (float)Sr;
            var n = (float)(rng.NextDouble() * 2 - 1);
            lp += a * (n - lp);
            b.S[i] += (highpass ? n - lp : lp) * gain * Env(t, attack, decay);
        }
    }

    /// <summary>Sine with an exponential pitch sweep from f0 to f1.</summary>
    private static void Tone(Buf b, float start, float gain, float f0, float f1, float sweep, float attack, float decay, float harmonics = 0f, float length = 10f)
    {
        var i0 = (int)(start * Sr);
        var i1 = Math.Min(b.Len, i0 + (int)(Math.Min(length, decay * 9 + attack) * Sr));
        double phase = 0;
        for (var i = Math.Max(0, i0); i < i1; i++)
        {
            var t = (i - i0) / (float)Sr;
            var f = f1 + (f0 - f1) * MathF.Exp(-t / MathF.Max(sweep, 1e-4f));
            phase += 2 * Math.PI * f / Sr;
            var s = (float)Math.Sin(phase);
            if (harmonics > 0)
                s += harmonics * MathF.Sign(s) * 0.5f + harmonics * 0.3f * (float)Math.Sin(phase * 3);
            b.S[i] += s * gain * Env(t, attack, decay);
        }
    }

    /// <summary>Short metallic click (two inharmonic partials + noise).</summary>
    private static void Click(Buf b, Random rng, float start, float gain, float pitch)
    {
        Tone(b, start, gain * 0.6f, 2600 * pitch, 2600 * pitch, 1, 0.0005f, 0.006f);
        Tone(b, start, gain * 0.4f, 4100 * pitch, 4100 * pitch, 1, 0.0005f, 0.004f);
        Noise(b, rng, start, gain * 0.5f, 0.0003f, 0.004f, 6000, highpass: true);
    }

    /// <summary>Heavier mechanical clunk (low thud + click).</summary>
    private static void Clunk(Buf b, Random rng, float start, float gain, float pitch)
    {
        Tone(b, start, gain * 0.7f, 260 * pitch, 140 * pitch, 0.02f, 0.001f, 0.03f);
        Noise(b, rng, start, gain * 0.4f, 0.001f, 0.02f, 1500 * pitch);
        Click(b, rng, start + 0.004f, gain * 0.6f, pitch * 0.8f);
    }

    /// <summary>Sliding / rattling metal (filtered noise swell).</summary>
    private static void Slide(Buf b, Random rng, float start, float gain, float duration, float cutoff)
    {
        Noise(b, rng, start, gain, duration * 0.5f, duration * 0.35f, cutoff, highpass: true, length: duration * 1.5f);
    }

    /// <summary>Cheap room reverb: a few decaying echoes of the signal.</summary>
    private static void Echo(Buf b, float amount, float roomSize)
    {
        if (amount <= 0)
            return;
        int[] taps = [(int)(0.031f * roomSize * Sr), (int)(0.047f * roomSize * Sr), (int)(0.073f * roomSize * Sr), (int)(0.11f * roomSize * Sr)];
        float[] gains = [0.5f, 0.38f, 0.27f, 0.18f];
        var src = (float[])b.S.Clone();
        for (var k = 0; k < taps.Length; k++)
            for (var i = taps[k]; i < b.Len; i++)
                b.S[i] += src[i - taps[k]] * gains[k] * amount;
    }

    private static short[] Finish(Buf b, float loudness)
    {
        // Soft-clip, normalise, add a 2 ms fade-out.
        var peak = 1e-6f;
        for (var i = 0; i < b.Len; i++)
        {
            b.S[i] = MathF.Tanh(b.S[i] * 1.4f);
            peak = MathF.Max(peak, MathF.Abs(b.S[i]));
        }
        var scale = loudness / peak;
        var fade = Math.Min(b.Len, Sr / 500);
        var pcm = new short[b.Len];
        for (var i = 0; i < b.Len; i++)
        {
            var f = i >= b.Len - fade ? (b.Len - i) / (float)fade : 1f;
            pcm[i] = (short)Math.Clamp(b.S[i] * scale * f * 32767f, short.MinValue, short.MaxValue);
        }
        return pcm;
    }

    // ---- Weapon sounds -----------------------------------------------------------

    public static short[] Gunshot(WeaponDef w)
    {
        var rng = new Random(1000 + w.Id * 7919);
        float V(float x, float amount = 0.1f) => x * (1f + ((float)rng.NextDouble() - 0.5f) * 2 * amount); // per-weapon variety
        var c = w.Caliber;
        var tail = 0.12f + w.Tail * 0.9f;
        var b = new Buf(0.06f + tail * 1.3f);

        switch (w.Sound)
        {
            case SoundStyle.Suppressed:
                // "Thwip": muffled pressure puff + loud mechanism.
                Noise(b, rng, 0, V(0.8f), 0.001f, V(0.018f + 0.02f * c), V(700 + 500 * w.Bright));
                Tone(b, 0, V(0.5f), V(220 - 80 * c), V(90), 0.015f, 0.001f, 0.03f);
                Noise(b, rng, 0, 0.25f, 0.0005f, 0.006f, 4000, highpass: true);
                Click(b, rng, V(0.012f), 0.35f, V(1f));
                Click(b, rng, V(0.045f), 0.25f, V(0.8f));
                Echo(b, 0.25f, 0.6f);
                return Finish(b, 0.55f);

            case SoundStyle.Zap:
                // Energy discharge: falling sine with harmonics, crackle, hum tail.
                Tone(b, 0, 0.7f, V(2400 + 1600 * w.Bright), V(160 + 120 * (1 - c)), V(0.04f + 0.05f * c), 0.001f, V(0.06f + 0.12f * c), harmonics: 0.6f);
                Tone(b, 0.002f, 0.35f, V(5200), V(900), 0.02f, 0.001f, 0.04f);
                Noise(b, rng, 0, 0.5f, 0.0005f, 0.015f, 9000, highpass: true);
                for (var i = 0; i < 6; i++)
                    Noise(b, rng, 0.01f + (float)rng.NextDouble() * 0.12f, 0.25f, 0.0002f, 0.004f, 7000, highpass: true);
                Tone(b, 0, 0.18f, 120, 110, 1, 0.01f, tail * 0.5f);
                Echo(b, 0.3f, 0.8f);
                return Finish(b, 0.8f);

            case SoundStyle.Toy:
                // Square-wave "pew".
                Tone(b, 0, 0.6f, V(1800), V(420), V(0.035f), 0.001f, 0.05f, harmonics: 1f, length: 0.12f);
                Tone(b, 0, 0.2f, V(3600), V(840), 0.035f, 0.001f, 0.03f, length: 0.08f);
                return Finish(b, 0.45f);

            case SoundStyle.Pneumatic:
                // Nailgun: air hiss + thunk + metallic ping.
                Noise(b, rng, 0, 0.6f, 0.002f, V(0.03f), V(2500), highpass: true);
                Clunk(b, rng, 0.003f, 0.8f, V(0.9f));
                Tone(b, 0.006f, 0.25f, V(1850), V(1800), 1, 0.0005f, 0.05f);
                Echo(b, 0.2f, 0.5f);
                return Finish(b, 0.6f);
        }

        // Real firearm: supersonic crack + muzzle blast + low thump + mechanism + room tail.
        var boom = w.Sound == SoundStyle.Boom;
        var blastDecay = V(0.02f + 0.05f * c + (boom ? 0.05f : 0));
        var cutoff = V(1400 + 6500 * w.Bright);

        Noise(b, rng, 0, V(0.7f), 0.0003f, V(0.0035f), 9000, highpass: true); // crack
        Noise(b, rng, 0, 1.0f, 0.0008f, blastDecay, cutoff); // blast
        Noise(b, rng, 0.002f, 0.6f, 0.001f, blastDecay * 1.8f, cutoff * 0.35f); // body
        Tone(b, 0, V(0.6f + 0.6f * c), V(180 - 110 * c), V(45 + 25 * (1 - c)), V(0.02f + 0.03f * c), 0.0008f, V(0.05f + 0.12f * c + (boom ? 0.15f : 0))); // thump
        if (w.Mode != FireMode.Semi || w.Class is WeaponClass.Pistol)
            Click(b, rng, V(0.018f + 0.02f * (1 - c)), 0.18f, V(1f)); // bolt / slide cycling
        Noise(b, rng, V(0.012f), V(0.25f + 0.25f * c), 0.01f, tail * 0.45f, V(500 + 900 * w.Bright), length: tail * 2.5f); // tail
        Echo(b, 0.35f + 0.4f * w.Tail, V(0.8f + 0.8f * w.Tail));
        return Finish(b, boom ? 0.98f : 0.9f);
    }

    public static short[] Reload(WeaponDef w)
    {
        var rng = new Random(5000 + w.Id * 104729);
        float V(float x) => x * (1f + ((float)rng.NextDouble() - 0.5f) * 0.2f);
        var T = w.Reload;
        var b = new Buf(T + 0.15f);
        var pitch = V(w.Class switch
        {
            WeaponClass.Pistol => 1.25f,
            WeaponClass.SMG => 1.1f,
            WeaponClass.LMG => 0.75f,
            WeaponClass.Sniper => 0.85f,
            WeaponClass.Shotgun => 0.8f,
            _ => 1f,
        });

        switch (w.Class)
        {
            case WeaponClass.Shotgun when w.Mag > 2 && w.Pellets > 1 || w.Name.Contains("Slug"):
            {
                // Shell by shell, then rack the pump.
                var shells = Math.Min(w.Mag, 6);
                for (var i = 0; i < shells; i++)
                {
                    var t = 0.12f + i * (T * 0.72f / shells);
                    Slide(b, rng, t, 0.25f, 0.05f, 2500);
                    Clunk(b, rng, t + 0.06f, 0.6f, pitch * V(1f));
                }
                Slide(b, rng, T * 0.85f, 0.5f, 0.06f, 1500);
                Clunk(b, rng, T * 0.88f, 0.9f, pitch * 0.85f);
                Clunk(b, rng, T * 0.95f, 0.9f, pitch);
                break;
            }
            case WeaponClass.Shotgun:
                // Break-action: open, drop shells, load, snap shut.
                Clunk(b, rng, 0.1f, 0.8f, pitch);
                Slide(b, rng, 0.3f, 0.3f, 0.12f, 1800);
                Clunk(b, rng, T * 0.55f, 0.5f, pitch * 1.2f);
                Clunk(b, rng, T * 0.68f, 0.5f, pitch * 1.2f);
                Clunk(b, rng, T * 0.9f, 1.0f, pitch * 0.9f);
                break;
            case WeaponClass.Sniper or WeaponClass.DMR when w.Mode == FireMode.Semi && w.Rpm < 160:
                // Bolt / lever: open, mag out, mag in, close.
                Click(b, rng, 0.08f, 0.6f, pitch);
                Slide(b, rng, 0.12f, 0.4f, 0.08f, 2200);
                Slide(b, rng, T * 0.35f, 0.3f, 0.1f, 1800);
                Clunk(b, rng, T * 0.6f, 0.8f, pitch);
                Slide(b, rng, T * 0.82f, 0.4f, 0.07f, 2200);
                Clunk(b, rng, T * 0.9f, 0.9f, pitch * 1.1f);
                break;
            case WeaponClass.LMG:
                // Box open, belt rattle, slam shut, charge.
                Clunk(b, rng, 0.15f, 0.9f, pitch);
                Slide(b, rng, T * 0.25f, 0.4f, T * 0.15f, 3000);
                for (var i = 0; i < 6; i++)
                    Click(b, rng, T * 0.3f + i * 0.05f, 0.25f, pitch * V(1.2f));
                Clunk(b, rng, T * 0.7f, 1.0f, pitch * 0.9f);
                Slide(b, rng, T * 0.82f, 0.5f, 0.08f, 1800);
                Clunk(b, rng, T * 0.88f, 0.9f, pitch);
                break;
            case WeaponClass.Special when w.Sound == SoundStyle.Zap:
                // Cell eject + power-up whine.
                Clunk(b, rng, 0.1f, 0.6f, 1.1f);
                Tone(b, T * 0.5f, 0.3f, 300, 2400, 0.15f, 0.2f, 0.3f);
                Tone(b, T * 0.55f, 0.15f, 600, 4800, 0.15f, 0.2f, 0.3f);
                Click(b, rng, T * 0.9f, 0.6f, 1.3f);
                break;
            case WeaponClass.Special when w.Sound == SoundStyle.Toy:
                Click(b, rng, 0.1f, 0.4f, 1.6f);
                Tone(b, T * 0.5f, 0.3f, 600, 1400, 0.06f, 0.05f, 0.1f, harmonics: 1f);
                Click(b, rng, T * 0.85f, 0.4f, 1.6f);
                break;
            default:
                // Magazine weapons: release, mag out, mag in, charging handle / slide.
                Click(b, rng, 0.08f, 0.6f, pitch * 1.1f);
                Slide(b, rng, 0.12f, 0.35f, 0.1f, 2600 * pitch);
                Slide(b, rng, T * 0.5f, 0.3f, 0.09f, 2400 * pitch);
                Clunk(b, rng, T * 0.62f, 0.9f, pitch);
                Slide(b, rng, T * 0.8f, 0.4f, 0.05f, 3000 * pitch);
                Clunk(b, rng, T * 0.86f, 0.8f, pitch * 1.15f);
                break;
        }
        return Finish(b, 0.55f);
    }

    public static short[] Equip(WeaponDef w)
    {
        var rng = new Random(9000 + w.Id * 31337);
        var b = new Buf(0.35f);
        var weight = Math.Clamp(w.Bulk, 0.7f, 1.7f);
        Noise(b, rng, 0, 0.35f, 0.08f, 0.06f, 900 / weight); // cloth / swing
        Clunk(b, rng, 0.12f + 0.05f * weight, 0.5f, 1.2f / weight);
        Click(b, rng, 0.2f + 0.05f * weight, 0.4f, 1.1f);
        return Finish(b, 0.45f);
    }

    public static short[] Effect(Sfx sfx)
    {
        var rng = new Random(77 + (int)sfx);
        Buf b;
        switch (sfx)
        {
            case Sfx.DryFire:
                b = new Buf(0.08f);
                Click(b, rng, 0, 0.8f, 1.3f);
                return Finish(b, 0.4f);
            case Sfx.HitTick:
                b = new Buf(0.08f);
                Tone(b, 0, 0.8f, 1900, 1700, 0.02f, 0.0005f, 0.012f);
                Noise(b, rng, 0, 0.3f, 0.0003f, 0.004f, 5000, highpass: true);
                return Finish(b, 0.5f);
            case Sfx.Headshot:
                b = new Buf(0.35f);
                Tone(b, 0, 0.7f, 2600, 2600, 1, 0.0005f, 0.08f);
                Tone(b, 0, 0.4f, 3900, 3900, 1, 0.0005f, 0.06f);
                Tone(b, 0, 0.2f, 6100, 6100, 1, 0.0005f, 0.04f);
                return Finish(b, 0.55f);
            case Sfx.Kill:
                b = new Buf(0.4f);
                Tone(b, 0, 0.7f, 880, 880, 1, 0.002f, 0.06f, length: 0.1f);
                Tone(b, 0.08f, 0.7f, 1320, 1320, 1, 0.002f, 0.12f);
                return Finish(b, 0.5f);
            case Sfx.Hurt:
                b = new Buf(0.25f);
                Tone(b, 0, 0.9f, 140, 60, 0.04f, 0.002f, 0.06f);
                Noise(b, rng, 0, 0.5f, 0.002f, 0.04f, 600);
                return Finish(b, 0.6f);
            case Sfx.Death:
                b = new Buf(1.2f);
                Tone(b, 0, 0.7f, 300, 50, 0.4f, 0.01f, 0.4f);
                Noise(b, rng, 0, 0.4f, 0.01f, 0.3f, 400);
                return Finish(b, 0.6f);
            case Sfx.Respawn:
                b = new Buf(0.6f);
                Tone(b, 0, 0.5f, 400, 1200, 0.08f, 0.05f, 0.15f);
                Tone(b, 0.05f, 0.3f, 800, 2400, 0.08f, 0.05f, 0.15f);
                return Finish(b, 0.4f);
            case Sfx.Throw:
                b = new Buf(0.3f);
                Noise(b, rng, 0, 0.6f, 0.06f, 0.06f, 1400); // whoosh
                Click(b, rng, 0.01f, 0.4f, 1.2f); // pin
                return Finish(b, 0.45f);
            case Sfx.Bounce:
                b = new Buf(0.15f);
                Tone(b, 0, 0.6f, 1500, 1450, 1, 0.0005f, 0.02f);
                Tone(b, 0, 0.3f, 2700, 2700, 1, 0.0005f, 0.012f);
                Noise(b, rng, 0, 0.4f, 0.0005f, 0.01f, 3000);
                return Finish(b, 0.5f);
            case Sfx.Explosion:
            {
                b = new Buf(2.2f);
                Noise(b, rng, 0, 1.0f, 0.001f, 0.06f, 5000); // crack
                Noise(b, rng, 0.002f, 1.0f, 0.004f, 0.25f, 900); // blast
                Tone(b, 0, 1.2f, 90, 32, 0.15f, 0.002f, 0.45f); // sub thump
                Noise(b, rng, 0.05f, 0.5f, 0.05f, 0.7f, 350, length: 2f); // rumble tail
                for (var i = 0; i < 10; i++) // debris
                    Click(b, rng, 0.15f + (float)rng.NextDouble() * 0.8f, 0.12f, 0.6f + (float)rng.NextDouble());
                Echo(b, 0.5f, 1.4f);
                return Finish(b, 0.98f);
            }
            case Sfx.SmokeHiss:
                b = new Buf(2.0f);
                Click(b, rng, 0, 0.5f, 0.9f);
                Noise(b, rng, 0.02f, 0.6f, 0.15f, 0.7f, 6000, highpass: true, length: 1.9f);
                return Finish(b, 0.5f);
            case Sfx.ReconBeep:
                b = new Buf(0.2f);
                Tone(b, 0, 0.7f, 2200, 2200, 1, 0.002f, 0.04f, length: 0.08f);
                Tone(b, 0.09f, 0.5f, 3300, 3300, 1, 0.002f, 0.03f, length: 0.06f);
                return Finish(b, 0.4f);
            case Sfx.LevelUp:
                b = new Buf(1.2f);
                float[] notes = [523, 659, 784, 1047];
                for (var i = 0; i < notes.Length; i++)
                    Tone(b, i * 0.11f, 0.6f, notes[i], notes[i], 1, 0.003f, i == notes.Length - 1 ? 0.35f : 0.1f, harmonics: 0.2f);
                Echo(b, 0.3f, 1f);
                return Finish(b, 0.6f);
            case Sfx.MeleeSwing:
                b = new Buf(0.3f);
                Noise(b, rng, 0, 0.7f, 0.07f, 0.07f, 1100); // whoosh
                Noise(b, rng, 0.03f, 0.3f, 0.05f, 0.05f, 2600, highpass: true);
                return Finish(b, 0.45f);
            case Sfx.MeleeHit:
                b = new Buf(0.3f);
                Tone(b, 0, 1.0f, 160, 70, 0.05f, 0.001f, 0.07f); // thud
                Noise(b, rng, 0, 0.8f, 0.001f, 0.03f, 900);
                Click(b, rng, 0, 0.4f, 0.8f);
                return Finish(b, 0.65f);
            default:
                return new short[1];
        }
    }
}
