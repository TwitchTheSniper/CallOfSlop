using System.Diagnostics;
using System.Numerics;
using Brutal.OpenAlApi;

namespace CallOfSlop;

/// <summary>
/// The audio mixer's side of the playback (the Audio mixer debug window, UiAudioMixer.cs): every sound is sorted into
/// a <see cref="MixBus"/> when it starts (<see cref="BusFor"/>, or the caller says), and its voice gain is multiplied by
/// that bus's level from <see cref="Mix"/>. OpenAL caps a voice at its AL_MAX_GAIN (1 by default, after distance), so
/// the voice's cap is set to the bus gain too: a bus at +6 dB plays exactly twice as loud as before, capped sounds
/// included, and at 0 dB nothing is touched (the cap is never set). A muted bus doesn't start voices at all.
/// The music's levels (menu, lobby, frenzy levels 1-6, the limiter ceiling) go to the music thread when the mix changes.
/// While the window is open <see cref="Meters"/> records each voice's start (bus, gain after the mix and distance, the
/// buffer's envelope) and estimates per-bus peak/RMS from it; closed, nothing is recorded.
/// </summary>
public sealed partial class Audio
{
    /// <summary>The player's mix (local playback only). Program loads audio_mix.json into it at start-up.</summary>
    public readonly AudioMix Mix = new();

    /// <summary>Per-bus level estimates for the mixer window (only fed while <see cref="AudioMeters.Enabled"/>).</summary>
    public readonly AudioMeters Meters = new();

    private readonly Dictionary<uint, int> _envByBuffer = new();
    private List<(AlBuffer Buffer, short[] Pcm)>? _pendingEnv = new();
    private readonly float[] _voiceMaxGain = InitMaxGains();
    private Vector3 _listenerForward = -Vector3.UnitZ;
    private int _mixRevision = -1;

    private static float[] InitMaxGains()
    {
        var a = new float[AudioMeters.MaxVoices];
        Array.Fill(a, 1f);
        return a;
    }

    /// <summary>The voice slot of footstep voice <paramref name="i"/> (effect voices are 0..47) for the meters and caps.</summary>
    private static int StepSlot(int i) => Voices + i;

    /// <summary>The voice slot of frenzy-layer voice <paramref name="i"/>.</summary>
    private static int FrenzySlot(int i) => Voices + StepVoices + i;

    /// <summary>Where the listener looks (for the mixer's test sounds).</summary>
    public Vector3 ListenerForward => _listenerForward;

    /// <summary>
    /// The bus a sound effect plays on: <paramref name="world"/> = positioned in the world (Effect3D), else at your ear
    /// (Effect). Call sites that know better pass the bus (landmines, zombies, mode events).
    /// </summary>
    public static MixBus BusFor(Sfx sfx, bool world) => sfx switch
    {
        Sfx.DryFire => MixBus.GunMechanism,
        Sfx.HitTick => MixBus.Ui,
        Sfx.Headshot or Sfx.KillHeadshot => MixBus.Headshot,
        Sfx.Kill => MixBus.KillConfirm,
        Sfx.HitBody or Sfx.HitMetal => world ? MixBus.BodyHitWorld : MixBus.HitMarker,
        Sfx.FriendlyHit => MixBus.FriendlyHit,
        Sfx.Hurt or Sfx.Death => MixBus.Hurt,
        Sfx.ShieldBlock => world ? MixBus.BodyHitWorld : MixBus.Hurt,
        Sfx.Respawn or Sfx.LevelUp or Sfx.StreakReady => MixBus.Notify,
        Sfx.ReconBeep => world ? MixBus.Tactical : MixBus.Notify,
        Sfx.Throw or Sfx.Bounce or Sfx.SmokeHiss or Sfx.FlashBang or Sfx.StunPop or Sfx.Tinnitus or Sfx.TrophyZap
            or Sfx.MotionPing or Sfx.SensorBeep or Sfx.StimHiss or Sfx.DeployDevice => MixBus.Tactical,
        Sfx.Explosion => MixBus.Explosion,
        Sfx.MeleeSwing or Sfx.MeleeHit => MixBus.Melee,
        Sfx.StreakCall or Sfx.MortarWhistle or Sfx.TurretShot or Sfx.JetFlyby or Sfx.EmpBurst or Sfx.HunterBeep
            or Sfx.GunshipCannon or Sfx.NapalmIgnite or Sfx.CrateLand or Sfx.FlarePop => MixBus.Streaks,
        Sfx.BreakWood or Sfx.BreakGlass or Sfx.BreakMetal or Sfx.BreakSoft => MixBus.PropBreak,
        Sfx.Headshot3D or Sfx.FleshHit3D => MixBus.BodyHitWorld,
        Sfx.Whizz or Sfx.Crack => MixBus.BulletWhizz,
        Sfx.Ricochet or Sfx.ImpactConcrete or Sfx.ImpactMetal or Sfx.ImpactWood or Sfx.ImpactDirt => MixBus.BulletImpact,
        Sfx.Scuffle or Sfx.LandThud or Sfx.JumpPush => world ? MixBus.FoleyOther : MixBus.FoleyOwn,
        Sfx.RocketMotor => MixBus.LauncherOther,
        Sfx.LockTone or Sfx.LockedTone => MixBus.LauncherLock,
        Sfx.GibSquelch => MixBus.Gore,
        Sfx.ZombieShuffle or Sfx.ZombieRun or Sfx.ZombieSprint => MixBus.ZombieSteps, // (ZombieSounds plays these on top of the Zombies row)
        >= Sfx.ZombieGroan and <= Sfx.ZombieHeadDeath => MixBus.ZombieVoices,
        _ => world ? MixBus.Tactical : MixBus.Ui,
    };

    /// <summary>The bus of a footstep event (steps, or the jump/land/crawl foley), yours or someone else's.</summary>
    public static MixBus BusFor(StepKind kind, bool self) => kind is StepKind.Land or StepKind.Jump or StepKind.Crawl
        ? self ? MixBus.FoleyOwn : MixBus.FoleyOther
        : self ? MixBus.StepOwn : MixBus.StepOther;

    /// <summary>
    /// Sets a voice's gain through the mix: <paramref name="gain"/> times the bus gain, with the voice's cap at the bus
    /// gain (so the old cap of 1 scales with it). At a gain of exactly 1 the cap is left as it is (1).
    /// </summary>
    private void ApplyVoiceGain(int slot, AlSource src, float gain, float mix)
    {
        var (g, cap) = VoiceGain(gain, mix);
        if ((uint)slot < (uint)_voiceMaxGain.Length && _voiceMaxGain[slot] != cap)
        {
            src.MaxGain = cap;
            _voiceMaxGain[slot] = cap;
        }
        src.Gain = g;
    }

    /// <summary>
    /// A voice's AL_GAIN and AL_MAX_GAIN for a gain before the mix and a bus gain: the gain times the bus gain, and the
    /// cap (OpenAL's default 1) times the bus gain, so a +20 dB bus plays 10x louder, capped sounds included, up to
    /// +50 dB (about 316x; OpenAL Soft accepts any AL_MAX_GAIN and only limits the total at AL_GAIN_LIMIT_SOFT, +60 dB).
    /// With a bus gain of exactly 1 both are untouched.
    /// </summary>
    internal static (float Gain, float MaxGain) VoiceGain(float gain, float mix) => (mix == 1f ? gain : gain * mix, mix);

    /// <summary>What OpenAL plays for <see cref="VoiceGain"/> before distance and the listener: the gain, capped at the cap.</summary>
    internal static float VoiceEffectiveGain(float gain, float mix)
    {
        var (g, cap) = VoiceGain(gain, mix);
        return MathF.Min(g, cap);
    }

    /// <summary>Records a started voice for the meters (only while they're on).</summary>
    private void MeterVoice(int slot, AlBuffer buffer, Vector3? position, float gain, float pitch, Falloff falloff, float mix, MixBus bus)
    {
        if (!Meters.Enabled || !_envByBuffer.TryGetValue(buffer.Name, out var env))
            return;
        var att = position is { } p ? falloff.Attenuation(Vector3.Distance(p, _listener)) : 1f;
        Meters.Record(slot, bus, MathF.Min(gain * att, 1f) * mix, Now(), pitch, env);
    }

    /// <summary>Uploads a buffer and keeps its PCM until the meters' envelopes are built (end of <see cref="Init(bool)"/>).</summary>
    private AlBuffer Upload(short[] pcm)
    {
        var buffer = UploadRaw(pcm);
        _pendingEnv?.Add((buffer, pcm));
        return buffer;
    }

    /// <summary>Builds every uploaded buffer's envelope (in parallel) and lets go of the PCM.</summary>
    private void BuildEnvelopes()
    {
        var pending = _pendingEnv;
        _pendingEnv = null;
        if (pending == null || pending.Count == 0)
            return;
        var sw = Stopwatch.StartNew();
        var pcms = new short[pending.Count][];
        for (var i = 0; i < pending.Count; i++)
            pcms[i] = pending[i].Pcm;
        var first = Meters.BuildEnvelopes(pcms);
        for (var i = 0; i < pending.Count; i++)
            _envByBuffer[pending[i].Buffer.Name] = first + i;
        Console.WriteLine($"[audiomix] meter envelopes for {pending.Count} buffers ({Meters.EnvelopeBlocks * 8 / 1024} KB) in {sw.Elapsed.TotalMilliseconds:0} ms; "
                          + $"mix {(Mix.IsDefault ? "built-in defaults" : $"{Mix.Changed} value(s) changed")}");
        LogGainLimit();
    }

    /// <summary>Logs OpenAL's total gain limit (AL_GAIN_LIMIT_SOFT): a level row can ask for up to +50 dB.</summary>
    private static void LogGainLimit()
    {
        try
        {
            var limit = Al.GainLimit;
            var note = limit >= AudioMixDefaults.MaxLevelGain * 0.999f ? "covers" : "BELOW";
            Console.WriteLine($"[audiomix] OpenAL gain limit {limit:0.#}x ({AudioMix.GainToDb(limit):+0.0} dB): {note} the mixer's +{AudioMixDefaults.MaxDb:0} dB ({AudioMixDefaults.MaxLevelGain:0}x)");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[audiomix] OpenAL gain limit unknown ({ex.Message})");
        }
    }

    /// <summary>Pushes the mix to the music thread when it changed (called with the listener, once a frame).</summary>
    internal void SyncMix()
    {
        if (Mix.Revision == _mixRevision)
            return;
        _mixRevision = Mix.Revision;
        Music.MenuTrim = Mix.Gain(MixBus.MusicMenu);
        Music.LobbyTrim = Mix.Gain(MixBus.MusicLobby);
        for (var l = 1; l <= 6; l++)
            Music.Frenzy.SetLevelTrim(l, Mix.Gain((MixBus)((int)MixBus.Frenzy1 + l - 1)));
        Music.LimiterCeiling = Mix.LimiterCeiling;
    }

    /// <summary>Turns the meters on (the mixer window opened) or off (closed: nothing is recorded or measured any more).</summary>
    public void SetMetering(bool on)
    {
        if (Meters.Enabled == on)
            return;
        Meters.Enabled = on;
        Music.MeterEnabled = on;
        if (!on)
            Meters.Clear();
    }

    /// <summary>The listener's gain: Settings' master volume times the mix's master level.</summary>
    private float ListenerGain => MasterVolume * Mix.Gain(MixBus.Master);

    /// <summary>
    /// Once a frame while the mixer window is open: the voices' estimated levels per bus, the music's measured ones,
    /// the master estimate and the clip light. Allocation-free.
    /// </summary>
    public void UpdateMeters(double now, float dt)
    {
        Meters.Begin();
        Meters.AccumulateVoices(now, ListenerGain);
        var music = Music.Volume * Music.Volume * ListenerGain; // the music volume's squared curve, then the listener
        var (menuPeak, menuMs, lobbyPeak, lobbyMs, frenzyPeak, frenzyMs, outPeak, outMs) = Music.TakeMeters();
        Meters.AddExternal(MixBus.MusicMenu, menuPeak * music, menuMs * music * music, toMaster: false);
        Meters.AddExternal(MixBus.MusicLobby, lobbyPeak * music, lobbyMs * music * music, toMaster: false);
        var level = Music.Frenzy.Level;
        if (level >= 1)
            Meters.AddExternal((MixBus)((int)MixBus.Frenzy1 + Math.Min(level, 6) - 1), frenzyPeak * music, frenzyMs * music * music, toMaster: false);
        Meters.AddMusicOutput(outPeak * ListenerGain, outMs * ListenerGain * ListenerGain);
        var talkers = 0;
        foreach (var v in _voiceOut)
            if (v.Bound != null)
                talkers++;
        Meters.VoiceCount[(int)MixBus.Voice] = talkers; // voice chat streams: counted, not measured
        Meters.End(now, dt);
    }

    // ---- Test sounds (the mixer's buttons) ---------------------------------------------------

    /// <summary>A point <paramref name="distance"/> m from the listener, <paramref name="angleDeg"/> to the right of where it looks (level).</summary>
    public Vector3 AroundListener(float distance, float angleDeg)
    {
        var f = new Vector3(_listenerForward.X, 0f, _listenerForward.Z);
        f = f.LengthSquared() > 1e-6f ? Vector3.Normalize(f) : -Vector3.UnitZ;
        var right = new Vector3(-f.Z, 0f, f.X);
        var a = angleDeg * MathF.PI / 180f;
        return _listener + (f * MathF.Cos(a) + right * MathF.Sin(a)) * distance;
    }

    /// <summary>Test: a shot of <paramref name="w"/>: your own (distance 0) or another player's from that far away.</summary>
    public void TestShot(WeaponDef w, float distance, float angleDeg) => Shot(w, distance <= 0f ? null : AroundListener(distance, angleDeg));

    /// <summary>Test: a frag explosion that far away.</summary>
    public void TestExplosion(float distance, float angleDeg) => Effect3D(Sfx.Explosion, AroundListener(distance, angleDeg), FragExplosionGain);

    /// <summary>Test: a run step (yours, or someone else's that far away).</summary>
    public void TestFootstep(bool self, float distance, float angleDeg, StepKind kind = StepKind.Run) =>
        Footstep(new StepEvent(self ? (byte)0 : (byte)1, kind, FootSurface.Concrete, self ? _listener : AroundListener(distance, angleDeg), 1f), self);
}

/// <summary>
/// The mixer window's level meters, estimated from the voices as they start: each uploaded buffer gets an envelope (peak
/// and mean square per <see cref="Block"/> samples, built once at start-up), and a voice's level at a moment is its gain
/// (after the mix, distance attenuation and OpenAL's cap) times its buffer's envelope at the time it has played to
/// (pitch included). The music is measured on its own thread. Off (<see cref="Enabled"/> false), nothing is recorded or
/// computed. Record and the per-frame update allocate nothing.
/// </summary>
public sealed class AudioMeters
{
    /// <summary>Samples per envelope block (about 11.6 ms).</summary>
    public const int Block = 512;

    /// <summary>Voice slots: 48 effect voices, 12 footstep voices, 10 frenzy-layer voices, 16 zombie voices (and a little spare).</summary>
    public const int MaxVoices = 96;

    private const int Buses = (int)MixBus.COUNT;

    /// <summary>The meters are fed (the mixer window is open).</summary>
    public bool Enabled;

    private float[] _peak = [], _ms = [];
    private int[] _start = [], _len = [];

    /// <summary>Envelope blocks held (two floats each).</summary>
    public int EnvelopeBlocks => _peak.Length;

    /// <summary>Envelopes held.</summary>
    public int Envelopes => _start.Length;

    private readonly byte[] _vBus = new byte[MaxVoices];
    private readonly float[] _vLevel = new float[MaxVoices], _vPitch = new float[MaxVoices];
    private readonly double[] _vStart = new double[MaxVoices];
    private readonly int[] _vEnv = InitEnv();

    private static int[] InitEnv()
    {
        var a = new int[MaxVoices];
        Array.Fill(a, -1);
        return a;
    }

    /// <summary>Peak and RMS (linear, after the master) per bus this frame, and the peak hold (decays about 10 dB a second).</summary>
    public readonly float[] Peak = new float[Buses], Rms = new float[Buses], Hold = new float[Buses];

    /// <summary>Voices playing per bus (the music counts one while it's audible).</summary>
    public readonly int[] VoiceCount = new int[Buses];

    /// <summary>
    /// The master estimate: peak = the sum of every voice's peak and the music output's (an upper bound: in-phase
    /// transients), RMS = their powers summed. <see cref="MasterHold"/> decays like the bus holds.
    /// </summary>
    public float MasterPeak, MasterRms, MasterHold;

    /// <summary>Voices playing in all.</summary>
    public int ActiveVoices;

    /// <summary>The master estimate went over full scale this many times (frames) since the meters were reset; the clip light stays on until <see cref="ClipUntil"/>.</summary>
    public int Clips;

    public double ClipUntil = -1;

    private float _sumPeak, _sumMs;

    /// <summary>
    /// Adds envelopes for <paramref name="pcms"/> (in parallel) and returns the index of the first; the rest follow in order.
    /// Not thread-safe with <see cref="AccumulateVoices"/> (start-up only).
    /// </summary>
    public int BuildEnvelopes(IReadOnlyList<short[]> pcms)
    {
        var first = _start.Length;
        var n = pcms.Count;
        var start = new int[first + n];
        var len = new int[first + n];
        Array.Copy(_start, start, first);
        Array.Copy(_len, len, first);
        var total = _peak.Length;
        for (var i = 0; i < n; i++)
        {
            start[first + i] = total;
            len[first + i] = (pcms[i].Length + Block - 1) / Block;
            total += len[first + i];
        }
        var peak = new float[total];
        var ms = new float[total];
        Array.Copy(_peak, peak, _peak.Length);
        Array.Copy(_ms, ms, _ms.Length);
        Parallel.For(0, n, i =>
        {
            var pcm = pcms[i];
            var o = start[first + i];
            for (var b = 0; b < len[first + i]; b++)
            {
                var a = b * Block;
                var e = Math.Min(pcm.Length, a + Block);
                float pk = 0;
                double sum = 0;
                for (var k = a; k < e; k++)
                {
                    var x = pcm[k] / 32768f;
                    pk = MathF.Max(pk, MathF.Abs(x));
                    sum += x * x;
                }
                peak[o + b] = pk;
                ms[o + b] = (float)(sum / Block);
            }
        });
        (_peak, _ms, _start, _len) = (peak, ms, start, len);
        return first;
    }

    /// <summary>How long envelope <paramref name="env"/> lasts in seconds (at pitch 1).</summary>
    public double EnvelopeSeconds(int env) => (uint)env < (uint)_len.Length ? _len[env] * Block / (double)Audio.SampleRate : 0;

    /// <summary>A voice started in <paramref name="slot"/> at <paramref name="level"/> (linear, before the master). Ignored while off.</summary>
    public void Record(int slot, MixBus bus, float level, double now, float pitch, int env)
    {
        if (!Enabled || (uint)slot >= MaxVoices || (uint)env >= (uint)_start.Length)
            return;
        _vBus[slot] = (byte)bus;
        _vLevel[slot] = level;
        _vStart[slot] = now;
        _vPitch[slot] = MathF.Max(pitch, 0.05f);
        _vEnv[slot] = env;
    }

    /// <summary>Forgets every recorded voice (the window closed).</summary>
    public void Clear()
    {
        Array.Fill(_vEnv, -1);
        Array.Clear(Hold);
        MasterHold = 0;
    }

    /// <summary>Starts a frame's readings.</summary>
    public void Begin()
    {
        Array.Clear(Peak);
        Array.Clear(Rms);
        Array.Clear(VoiceCount);
        _sumPeak = _sumMs = 0;
        ActiveVoices = 0;
    }

    /// <summary>Adds every recorded voice still playing at <paramref name="now"/> (times <paramref name="master"/>, the listener gain).</summary>
    public void AccumulateVoices(double now, float master)
    {
        for (var s = 0; s < MaxVoices; s++)
        {
            var env = _vEnv[s];
            if (env < 0)
                continue;
            var t = (now - _vStart[s]) * _vPitch[s];
            var b = (int)(Math.Max(0, t) * Audio.SampleRate / Block);
            if (b >= _len[env])
            {
                _vEnv[s] = -1; // finished
                continue;
            }
            var g = _vLevel[s] * master;
            var i = _start[env] + b;
            var pk = g * _peak[i];
            var ms = g * g * _ms[i];
            var bus = _vBus[s];
            Peak[bus] = MathF.Max(Peak[bus], pk);
            Rms[bus] += ms; // power for now; End takes the root
            VoiceCount[bus]++;
            ActiveVoices++;
            _sumPeak += pk;
            _sumMs += ms;
        }
    }

    /// <summary>Adds a level measured elsewhere (the music's) to a bus: a peak and a mean square; <paramref name="toMaster"/> adds it to the master estimate too.</summary>
    public void AddExternal(MixBus bus, float peak, float meanSquare, bool toMaster = true)
    {
        if ((uint)bus >= Buses || !(peak > 0f))
            return;
        Peak[(int)bus] = MathF.Max(Peak[(int)bus], peak);
        Rms[(int)bus] += meanSquare;
        VoiceCount[(int)bus]++;
        if (toMaster)
        {
            _sumPeak += peak;
            _sumMs += meanSquare;
        }
    }

    /// <summary>The music output (after its volume and limiter), for the master estimate.</summary>
    public void AddMusicOutput(float peak, float meanSquare)
    {
        if (!(peak > 0f))
            return;
        _sumPeak += peak;
        _sumMs += meanSquare;
    }

    /// <summary>Ends a frame: RMS roots, holds, the master and the clip light.</summary>
    public void End(double now, float dt)
    {
        var decay = MathF.Exp(-MathF.Max(dt, 0f) * 2.3f); // about 10 dB a second
        for (var i = 0; i < Buses; i++)
        {
            Rms[i] = MathF.Sqrt(Rms[i]);
            Hold[i] = MathF.Max(Peak[i], Hold[i] * decay);
        }
        MasterPeak = _sumPeak;
        MasterRms = MathF.Sqrt(_sumMs);
        MasterHold = MathF.Max(MasterPeak, MasterHold * decay);
        if (MasterPeak > 1f)
        {
            Clips++;
            ClipUntil = now + 1.5;
        }
    }
}
