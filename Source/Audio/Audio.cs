using System.Diagnostics;
using System.Numerics;
using Brutal.Numerics;
using Brutal.OpenAlApi;

namespace CallOfSlop;

// New effects go at the end (before COUNT): mode events send an Sfx as a byte, so existing values must not move.
public enum Sfx
{
    DryFire,
    HitTick, // UI click; also the old hit tick
    Headshot, // shooter feedback: hitmarker ping + meaty crunch
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
    StreakReady, // a kill streak earned
    StreakCall, // a kill streak called in (radio blip)
    MortarWhistle, // incoming shell
    TurretShot, // sentry turret / drone / jet cannon
    JetFlyby, // strafe run jet passing over
    BreakWood, // a breakable prop breaking (crate, pallet, chair)
    BreakGlass, // bottles
    BreakMetal, // bins, barrels, cones
    BreakSoft, // cardboard, plastic, paper
    HitBody, // shooter feedback: a body hit (flesh thwack + tick)
    Headshot3D, // a headshot heard by everyone else, in the world
    FleshHit3D, // a body hit heard by everyone else
    HitMetal, // shooter feedback: hitting a turret or drone
    KillHeadshot, // extra accent on a headshot kill
    Whizz, // a subsonic bullet passing close by
    Crack, // a supersonic bullet passing close by
    Ricochet,
    ImpactConcrete, // bullet impacts by surface
    ImpactMetal,
    ImpactWood,
    ImpactDirt,
    Scuffle, // prone crawl
    LandThud, // landing from a fall
    JumpPush, // jump push-off
    ShieldBlock, // Last Stand's shield blocking a bullet hit (an energy zap and a glassy ring)
    FriendlyHit, // shooter feedback: hitting a teammate (no damage): a muted low bonk and a soft double beep
    FlashBang, // tactical equipment (SynthTactical.cs): a flashbang's sharp crack
    StunPop, // a stun grenade's thump and warble
    Tinnitus, // the ringing after a flash (played to the flashed player only)
    TrophyZap, // a Trophy System shooting a grenade down
    MotionPing, // a Motion Sensor ping on your HUD
    SensorBeep, // a Motion Sensor's own beep in the world (so it can be found)
    StimHiss, // a Stim Shot going in
    DeployDevice, // a device clicking down (trophy, cover, insertion, a landed decoy)
    EmpBurst, // kill streaks added later (SynthStreaks.cs): an EMP going off
    HunterBeep, // a Hunter Drone's seeker beep
    GunshipCannon, // a gunship's cannon round leaving the barrel
    NapalmIgnite, // napalm bursting into flame
    CrateLand, // a Supply Drop crate thumping down
    RocketMotor, // launchers (SynthLaunchers.cs): a rocket motor's roar, played every few tenths of a second along its flight
    LockTone, // a lock-on launcher building its lock (a short low beep; ticks faster as it builds)
    LockedTone, // locked on (a high solid tone)
    FlarePop, // a kill streak machine popping flares against an incoming lock-on rocket
    GibSquelch, // an animal or a body bursting into meaty gibs (SynthGore.cs)
    COUNT,
}

/// <summary>
/// OpenAL (via Brutal.OpenAl) playback with 3D positioning for other players' shots, steps and impacts.
/// Every sound is synthesised at startup: each weapon gets its own gunshot (near and distant layers), reload and equip
/// sound built from its stats. Real firearms then get recorded gunshot layers baked in (<see cref="GunSamples"/>, from
/// Audio/Samples/Guns; the synth stays as the fallback when those files are missing). Footsteps have their own small voice pool
/// (<see cref="FootstepVoicePool"/>). With OpenAL EFX (checked by a canary at start-up) far sounds are low-passed by
/// distance and everything gets a light reverb send that suits the map (<see cref="AudioSpace"/>).
/// </summary>
public sealed partial class Audio : IDisposable
{
    public const int SampleRate = 44100;
    private const int Voices = 48;

    /// <summary>Voices only footsteps use (so 32 running players can't starve the gunfire).</summary>
    public const int StepVoices = 12;

    private Device? _device;
    private Context? _context;
    private AlSource[] _sources = [];
    private AlSource[] _stepSources = [];
    private readonly double[] _startedAt = new double[Voices];
    private AlBuffer[][] _shots = [], _shotsSuppressed = []; // [weapon][variant]
    private AlBuffer[] _shotsFar = [], _reloads = [], _equips = [];
    private readonly AlBuffer[][] _sfx = new AlBuffer[(int)Sfx.COUNT][];
    private AlBuffer[] _steps = []; // [surface][run][variant], see StepIndex
    private readonly Random _rng = new(1234);
    private Vector3 _listener;

    public bool Enabled { get; private set; }
    public float MasterVolume = 0.8f;

    /// <summary>Gunfire, hits, explosions and everything else that isn't music (on top of the master volume).</summary>
    public float EffectsVolume = 1f;

    /// <summary>Footsteps, jumps and landings (on top of the effects volume).</summary>
    public float FootstepVolume = 1f;

    /// <summary>Settings > Audio "Own gun volume": your own gunshots (and their frenzy layers), on top of the effects volume.</summary>
    public float OwnGunVolume = 1f;

    /// <summary>Your own footsteps are this much quieter than someone else's at your feet.</summary>
    public const float SelfStepGain = 0.5f;

    /// <summary>The footstep voices' bookkeeping (cap and distance priority).</summary>
    public readonly FootstepVoicePool StepPool = new(StepVoices);

    /// <summary>Where the listener is (the camera), for distance culling and filtering.</summary>
    public Vector3 ListenerPosition => _listener;

    /// <summary>Procedural background music for the menus and lobbies (Music.cs); streams only once OpenAL is up.</summary>
    public readonly MusicPlayer Music = new();

    /// <summary>How long start-up synthesis took and how much it made (for the [audio] log line and the self-test).</summary>
    public double SynthMs { get; private set; }

    public int BufferCount { get; private set; }
    public long BufferBytes { get; private set; }

    public void Init() => Init(music: true);

    /// <summary>Opens the device, synthesises every sound and (optionally) starts the music.</summary>
    public void Init(bool music)
    {
        try
        {
            _device = Alc.OpenDevice();
            _context = _device.CreateContext();
            _context.MakeCurrent();
            _sources = Al.GenSources(Voices);
            _stepSources = Al.GenSources(StepVoices);

            var sw = Stopwatch.StartNew();
            var pcm = SynthesiseAll();
            SynthMs = sw.Elapsed.TotalMilliseconds;
            var defs = Weapons.All;
            _shots = defs.Select(d => pcm.Shots[d.Id].Select(Upload).ToArray()).ToArray();
            _shotsSuppressed = defs.Select(d => pcm.Suppressed[d.Id].Select(Upload).ToArray()).ToArray();
            _shotsFar = defs.Select(d => pcm.Far[d.Id] is { } f ? Upload(f) : default).ToArray();
            _farTrims = defs.Select(d => FarTrimFor(pcm.Shots[d.Id][0], pcm.Far[d.Id])).ToArray(); // Audio.GunMix.cs
            _reloads = defs.Select(d => Upload(pcm.Reloads[d.Id])).ToArray();
            _equips = defs.Select(d => Upload(pcm.Equips[d.Id])).ToArray();
            for (var i = 0; i < (int)Sfx.COUNT; i++)
                _sfx[i] = pcm.Effects[i].Select(Upload).ToArray();
            _steps = pcm.Steps.Select(Upload).ToArray();
            InitFrenzyGun(pcm); // Audio.FrenzyGun.cs: per-class frenzy layers on your own gun
            BufferCount = pcm.Count;
            BufferBytes = pcm.Bytes;

            Enabled = true;
            InitEfx();
            var hrtf = "unknown";
            try
            {
                hrtf = _device.HrtfStatus.ToString();
            }
            catch (Exception)
            {
                // older drivers: no HRTF query
            }
            Console.WriteLine($"Audio: OpenAL ready, synthesised {BufferCount} sounds in {SynthMs:0} ms.");
            Console.WriteLine($"[audio] {BufferCount} buffers ({BufferBytes / 1024} KB) synthesised in {SynthMs:0} ms on {Environment.ProcessorCount} threads; "
                + $"{Voices} effect voices + {StepVoices} footstep voices; footsteps {(int)FootSurface.Count} surfaces x 2 gaits x {Synth.StepVariants} variants; "
                + $"EFX {EfxStatus}; HRTF {hrtf}");
            var lib = GunSampleLibrary.Shared;
            Console.WriteLine($"[audio] gun samples: {lib.FilesLoaded}/{lib.FilesListed} files ({lib.BytesLoaded / 1024} KB) loaded in {lib.LoadMs:0} ms from {lib.Folder}"
                + (lib.Problems.Count > 0 ? $"; {lib.Problems.Count} problem(s), synth fallback: {string.Join("; ", lib.Problems.Take(6))}" : ""));
            foreach (var line in lib.Describe(defs))
                Console.WriteLine(line);
            try
            {
                if (music)
                    Music.Start();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Music disabled: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            Enabled = false;
            Console.WriteLine($"Audio disabled: {ex.Message}");
        }
    }

    /// <summary>Every sound the game makes, as 16-bit mono PCM (made in parallel; no OpenAL needed).</summary>
    public sealed class SoundSet
    {
        public required short[][][] Shots, Suppressed; // [weapon][variant]: recorded variants, or the synth alone
        public required short[][] Reloads, Equips;
        public required short[]?[] Far;
        public required short[][][] Effects; // [sfx][variant]
        public required short[][] Steps; // StepIndex order
        public int Count;
        public long Bytes;
    }

    /// <summary>The footstep buffer for a surface, gait and variant.</summary>
    public static int StepIndex(FootSurface s, bool run, int variant) => ((int)s * 2 + (run ? 1 : 0)) * Synth.StepVariants + variant;

    /// <summary>Synthesises everything (also run headlessly by the self-test), with the game's gun samples.</summary>
    public static SoundSet SynthesiseAll() => SynthesiseAll(GunSampleLibrary.Shared);

    /// <summary>Synthesises everything, baking gunshots from <paramref name="guns"/> (<see cref="GunSampleLibrary.Empty"/> = synth only).</summary>
    public static SoundSet SynthesiseAll(GunSampleLibrary guns)
    {
        var defs = Weapons.All;
        var n = defs.Length;
        var set = new SoundSet
        {
            Shots = new short[n][][],
            Suppressed = new short[n][][],
            Reloads = new short[n][],
            Equips = new short[n][],
            Far = new short[n][],
            Effects = new short[(int)Sfx.COUNT][][],
            Steps = new short[(int)FootSurface.Count * 2 * Synth.StepVariants][],
        };
        for (var i = 0; i < (int)Sfx.COUNT; i++)
            set.Effects[i] = new short[Synth.VariantCount((Sfx)i)][];

        // One flat job list so the short effects spread over the cores along with the long gunshots.
        var jobs = new List<Action>();
        for (var k = 0; k < n; k++)
        {
            var i = k;
            jobs.Add(() =>
            {
                var synth = Synth.Gunshot(defs[i]);
                set.Shots[i] = GunSamples.Shots(guns, defs[i], synth);
                // The same gun with a Suppressor attachment on it.
                if (defs[i].Sound is SoundStyle.Gun or SoundStyle.Boom)
                {
                    var quiet = defs[i].Clone();
                    quiet.Sound = SoundStyle.Suppressed;
                    set.Suppressed[i] = GunSamples.Suppressed(guns, defs[i], Synth.Gunshot(quiet));
                }
                else
                    set.Suppressed[i] = set.Shots[i];
            });
            jobs.Add(() => set.Far[i] = GunSamples.Far(guns, defs[i], Synth.GunshotDistant(defs[i])));
            jobs.Add(() =>
            {
                set.Reloads[i] = Synth.Reload(defs[i]);
                set.Equips[i] = Synth.Equip(defs[i]);
            });
        }
        for (var e = 0; e < (int)Sfx.COUNT; e++)
            for (var v = 0; v < set.Effects[e].Length; v++)
            {
                var (ee, vv) = (e, v);
                jobs.Add(() => set.Effects[ee][vv] = Synth.Effect((Sfx)ee, vv));
            }
        for (var s = 0; s < (int)FootSurface.Count; s++)
            for (var r = 0; r < 2; r++)
                for (var v = 0; v < Synth.StepVariants; v++)
                {
                    var (ss, rr, vv) = ((FootSurface)s, r == 1, v);
                    jobs.Add(() => set.Steps[StepIndex(ss, rr, vv)] = Synth.Footstep(ss, rr, vv));
                }
        Parallel.For(0, jobs.Count, j => jobs[j]());

        void Add(short[]? a)
        {
            if (a == null)
                return;
            set.Count++;
            set.Bytes += a.Length * 2L;
        }
        for (var i = 0; i < n; i++)
        {
            foreach (var a in set.Shots[i])
                Add(a);
            foreach (var a in set.Suppressed[i])
                Add(a);
            Add(set.Far[i]);
            Add(set.Reloads[i]);
            Add(set.Equips[i]);
        }
        foreach (var v in set.Effects)
            foreach (var a in v)
                Add(a);
        foreach (var a in set.Steps)
            Add(a);
        return set;
    }

    private static AlBuffer Upload(short[] pcm)
    {
        var buffer = Al.GenBuffer();
        buffer.SetData(AlFormat.Mono16, System.Runtime.InteropServices.MemoryMarshal.AsBytes(pcm.AsSpan()), SampleRate);
        return buffer;
    }

    public void SetListener(Vector3 position, Vector3 forward)
    {
        _listener = position;
        if (!Enabled)
            return;
        Al.Listener.Position = new float3(position.X, position.Y, position.Z);
        Al.Listener.Orientation = (new float3(forward.X, forward.Y, forward.Z), new float3(0, 1, 0));
        Al.Listener.Gain = MasterVolume;
    }

    // ---- Playback -----------------------------------------------------------

    /// <summary>
    /// Distance falloff for a sound: where it starts to fade, how fast, and where it stops fading (OpenAL's default
    /// inverse-distance-clamped model, see <see cref="Attenuation"/>). <c>Thin</c>: the distance filter also takes
    /// the low end away with distance (gunshots, see <see cref="DistanceLf"/>).
    /// </summary>
    internal readonly record struct Falloff(float Reference, float Rolloff, float Max, bool Thin = false)
    {
        /// <summary>The gain OpenAL gives this falloff at <paramref name="distance"/> (inverse distance, clamped to Reference..Max).</summary>
        public float Attenuation(float distance)
        {
            var d = Math.Clamp(distance, Reference, MathF.Max(Reference, Max));
            return Reference / (Reference + Rolloff * (d - Reference));
        }

        public static readonly Falloff Normal = new(6f, 1.1f, 120f);
        public static readonly Falloff Step = new(3f, 1f, 60f);
        public static readonly Falloff Small = new(3f, 1.3f, 60f); // impacts, whizzes
    }

    /// <summary>Plays a buffer. Null position = attached to the listener (our own gun / UI).</summary>
    private void Play(AlBuffer buffer, Vector3? position, float gain, float pitch) => Play(buffer, position, gain, pitch, Falloff.Normal);

    private void Play(AlBuffer buffer, Vector3? position, float gain, float pitch, Falloff falloff)
    {
        if (!Enabled || buffer.IsNull())
            return;
        var t0 = Stopwatch.GetTimestamp();

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

        Start(_sources[pick], buffer, position, gain * EffectsVolume * DeafenGain, pitch, falloff); // muffled after a flash (Audio.Tactical.cs)
        _startedAt[pick] = now;
        _played++;
        _cpuTicks += Stopwatch.GetTimestamp() - t0;
    }

    private void Start(AlSource src, AlBuffer buffer, Vector3? position, float gain, float pitch, Falloff falloff)
    {
        src.Stop();
        src.Buffer = buffer;
        src.Gain = gain;
        src.Pitch = pitch;
        src.ReferenceDistance = falloff.Reference;
        src.RolloffFactor = falloff.Rolloff;
        src.MaxDistance = falloff.Max;
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
        ApplyDistanceFilter(src, position, falloff.Thin);
        src.Play();
    }

    private static double Now() => Brutal.GlfwApi.Glfw.Time;

    private float Jitter(float amount) => 1f + ((float)_rng.NextDouble() - 0.5f) * 2f * amount;

    private AlBuffer Pick(Sfx sfx)
    {
        var v = _sfx[(int)sfx];
        return v.Length == 1 ? v[0] : v[_rng.Next(v.Length)];
    }

    /// <summary>
    /// How much of a near gunshot is replaced by its distant layer at a distance (0 up close, 1 far away): a smooth
    /// crossfade from 25 m to 90 m (equal power, on one falloff curve: see <see cref="PlanShot"/>).
    /// </summary>
    public static float FarMix(float distance)
    {
        var x = Math.Clamp((distance - 25f) / 65f, 0f, 1f);
        return x * x * (3 - 2 * x);
    }

    // Every public sound call checks Enabled first: with no audio device the sound tables are empty.
    public void Shot(WeaponDef w, Vector3? position)
    {
        if (!Enabled)
            return;
        var hasFar = !_shotsFar[w.Id].IsNull();
        var plan = PlanShot(position == null, w.Suppressor, hasFar, position is { } at ? Vector3.Distance(at, _listener) : 0f); // Audio.GunMix.cs
        var gain = Jitter(0.07f);
        var pitch = Jitter(0.04f);
        var (kitPitch, kitGain) = KitVoice(w); // Audio.GunVoice.cs: the attachments' colour
        pitch *= kitPitch;
        gain *= kitGain;
        var nearGain = plan.Near * gain * (position == null ? OwnGunVolume : 1f);
        if (position == null && FrenzyGunLevel > 0 && FrenzyGunMode != FrenzyGunSoundMode.Off)
            nearGain = FrenzyOwnShot(w, nearGain, ref pitch); // Audio.FrenzyGun.cs: your frenzy class's layers on your own gun
        if (plan.Near > 0f)
            Play(PickVariant(w.Suppressor ? _shotsSuppressed[w.Id] : _shots[w.Id]), position, nearGain, pitch, plan.NearFalloff);
        if (plan.Far > 0f && hasFar)
            Play(_shotsFar[w.Id], position, plan.Far * gain * FarTrim(w.Id), pitch, plan.FarFalloff);
    }

    /// <summary>A random recorded variant (never the same one twice in a row when there are several).</summary>
    private AlBuffer PickVariant(AlBuffer[] v)
    {
        if (v.Length <= 1)
            return v.Length == 1 ? v[0] : default;
        var i = _rng.Next(v.Length - 1);
        if (i >= _lastVariant)
            i++;
        _lastVariant = i;
        return v[i];
    }

    private int _lastVariant;

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
            Play(Pick(sfx), null, gain, Synth.VariantCount(sfx) > 1 ? Jitter(0.03f) : 1f);
    }

    /// <summary>A sound effect positioned in the world (explosions, bounces, beeps).</summary>
    public void Effect3D(Sfx sfx, System.Numerics.Vector3 position, float gain = 1f)
    {
        if (Enabled)
            Play(Pick(sfx), position, gain, Jitter(0.05f));
    }

    /// <summary>A small, short sound in the world (impacts, whizzes): fades out sooner and is skipped beyond <paramref name="range"/>.</summary>
    public void Effect3DSmall(Sfx sfx, Vector3 position, float gain, float range = 50f)
    {
        if (Enabled && Vector3.DistanceSquared(position, _listener) <= range * range)
            Play(Pick(sfx), position, gain, Jitter(0.07f), Falloff.Small);
    }

    // ---- Footsteps ----------------------------------------------------------

    /// <summary>
    /// Loudness (before distance) and how far a step carries, by kind: a sprint is heavy and heard 45 m away, a walk
    /// 24 m, a crouch-walk barely at all; a landing scales with how hard it was.
    /// </summary>
    public static (float Gain, float Range) StepLoudness(StepKind kind, float strength) => kind switch
    {
        StepKind.Run => (0.8f, 45f),
        StepKind.Walk => (0.34f * Math.Clamp(strength, 0.5f, 1f), 24f),
        StepKind.Crouch => (0.06f, 6f),
        StepKind.Crawl => (0.12f, 8f),
        StepKind.Land => (0.35f + 0.65f * strength, 25f + 25f * strength),
        StepKind.Jump => (0.35f, 20f),
        _ => (0.3f, 20f),
    };

    /// <summary>
    /// <see cref="StepLoudness(StepKind, float)"/> for a step someone else hears: <paramref name="silenced"/> (the stepper
    /// carries Dead Silence) makes it <see cref="Perks.DeadSilenceGain"/> as loud, heard within <see cref="Perks.DeadSilenceRange"/> of the range.
    /// </summary>
    public static (float Gain, float Range) StepLoudness(StepKind kind, float strength, bool silenced)
    {
        var (gain, range) = StepLoudness(kind, strength);
        return silenced ? (gain * Perks.DeadSilenceGain, range * Perks.DeadSilenceRange) : (gain, range);
    }

    /// <summary>
    /// Plays a footstep event (<see cref="Footsteps"/>): positional for others, quieter and centred for yourself.
    /// <paramref name="silenced"/>: the stepper carries Dead Silence (ignored for your own steps).
    /// </summary>
    public void Footstep(in StepEvent e, bool self, bool silenced = false)
    {
        if (!Enabled)
            return;
        var t0 = Stopwatch.GetTimestamp();
        var (gain, range) = StepLoudness(e.Kind, e.Strength, silenced && !self);
        var d = self ? 0f : Vector3.Distance(e.Position, _listener);
        gain *= FootstepVolume * EffectsVolume * (self ? SelfStepGain : 1f);
        if (gain > 0.002f && d <= range)
        {
            var run = e.Kind is StepKind.Run or StepKind.Land;
            var step = _steps[StepIndex(e.Surface, run, _rng.Next(Synth.StepVariants))];
            var pitch = Jitter(0.06f) * (e.Kind == StepKind.Run ? 0.95f : 1f);
            var at = self ? (Vector3?)null : e.Position;
            switch (e.Kind)
            {
                case StepKind.Crawl:
                    StepVoice(Pick(Sfx.Scuffle), at, d, gain, pitch);
                    break;
                case StepKind.Land:
                    StepVoice(step, at, d, gain * 0.8f, pitch * 0.92f);
                    StepVoice(Pick(Sfx.LandThud), at, d, gain * (0.4f + 0.6f * e.Strength), Jitter(0.05f));
                    break;
                case StepKind.Jump:
                    StepVoice(Pick(Sfx.JumpPush), at, d, gain, Jitter(0.05f));
                    StepVoice(step, at, d, gain * 0.5f, pitch * 1.05f);
                    break;
                default:
                    StepVoice(step, at, d, gain * Jitter(0.12f), pitch);
                    break;
            }
        }
        _cpuTicks += Stopwatch.GetTimestamp() - t0;
    }

    private void StepVoice(AlBuffer buffer, Vector3? position, float distance, float gain, float pitch)
    {
        var v = StepPool.Acquire(Now(), distance, 0.45f / MathF.Max(pitch, 0.5f));
        if (v < 0)
            return;
        Start(_stepSources[v], buffer, position, gain, pitch, Falloff.Step);
        _stepsPlayed++;
    }

    // ---- Stats --------------------------------------------------------------

    private long _cpuTicks;
    private int _played, _stepsPlayed, _droppedAtLog;
    private double _statsSince = double.NaN;

    /// <summary>Once a minute while sounds play: an [audio] line with counts and the CPU spent starting them.</summary>
    public void Tick(double now)
    {
        if (!Enabled)
            return;
        if (double.IsNaN(_statsSince))
            _statsSince = now;
        if (now - _statsSince < 60)
            return;
        var n = _played + _stepsPlayed;
        if (n > 0)
            Console.WriteLine($"[audio] last {now - _statsSince:0} s: {_played} sounds, {_stepsPlayed} footstep voices ({StepPool.Dropped - _droppedAtLog} steps dropped by the {StepVoices}-voice cap), "
                + $"{_cpuTicks * 1e6 / Stopwatch.Frequency / Math.Max(n, 1):0.0} us CPU per sound started; EFX {(EfxEnabled ? "on" : "off")}, space {Space}");
        _statsSince = now;
        _played = _stepsPlayed = 0;
        _droppedAtLog = StepPool.Dropped;
        _cpuTicks = 0;
    }

    public void Dispose()
    {
        Mic.Dispose(); // voice chat's microphone (Audio.Voice.cs): capture doesn't need the context
        if (_context == null)
            return; // never initialised (no audio device): nothing to release
        Music.Dispose(); // stops the streaming thread before the context goes
        DisposeVoice(); // voice chat's streaming sources (Audio.Voice.cs)
        foreach (var set in new[] { _sources, _stepSources })
        {
            if (set.Length == 0)
                continue;
            foreach (var s in set)
                s.Stop();
            Al.DeleteSources(set);
        }
        DisposeFrenzyGun();
        DisposeEfx();
        Al.DeleteBuffers(_shots.SelectMany(v => v).Concat(_shotsSuppressed.SelectMany(v => v)).Distinct().ToArray());
        Al.DeleteBuffers(_shotsFar.Where(b => !b.IsNull()).ToArray());
        Al.DeleteBuffers(_reloads);
        Al.DeleteBuffers(_equips);
        Al.DeleteBuffers(_sfx.Where(v => v != null).SelectMany(v => v).Where(b => !b.IsNull()).ToArray());
        Al.DeleteBuffers(_steps);
        Context.MakeCurrent(null);
        _context?.Dispose();
        _device?.Dispose();
        Enabled = false;
    }
}

/// <summary>Tiny procedural sound synthesiser (mono, 16-bit, 44.1 kHz).</summary>
public static partial class Synth
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
            if (!float.IsFinite(b.S[i]))
            {
                Interlocked.Increment(ref NonFinite);
                b.S[i] = 0;
            }
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
            case SoundStyle.Rocket:
            {
                // Rocket launch: a hard ignition crack, the backblast roar (long filtered noise swelling and falling), a deep
                // shove on the shoulder and the motor's hiss pulling away. Caliber deepens it, bright opens the roar, tail stretches it.
                var roar = V(0.18f + 0.25f * w.Tail);
                Noise(b, rng, 0, V(0.9f), 0.0004f, V(0.006f), V(5000 + 3000 * w.Bright), highpass: true); // ignition crack
                Noise(b, rng, 0.002f, 1.0f, V(0.012f), roar, V(800 + 2600 * w.Bright)); // backblast roar
                Tone(b, 0, V(0.9f), V(120 - 60 * c), V(35 + 15 * (1 - c)), V(0.05f + 0.06f * c), 0.002f, V(0.14f + 0.12f * c)); // the shove
                Noise(b, rng, V(0.05f), V(0.35f), V(0.06f), roar * 1.4f, V(3500 + 3000 * w.Bright), highpass: true, length: roar * 3f); // motor hiss
                Tone(b, V(0.03f), 0.2f, V(380 + 300 * w.Bright), V(900 + 900 * w.Bright), 0.2f, 0.03f, roar, harmonics: 0.3f); // the motor's whine climbing away
                Echo(b, 0.35f + 0.35f * w.Tail, V(0.9f + 0.8f * w.Tail));
                return Finish(b, 0.95f);
            }
            case SoundStyle.Thump:
            {
                // Grenade launcher: a hollow "thoomp" (a falling low tone through a tube), the breech clack and a little air.
                var pitch = V(1.25f - 0.6f * c, 0.15f);
                Tone(b, 0, 1.0f, V(260 * pitch), V(70 * pitch), V(0.03f + 0.04f * c), 0.001f, V(0.07f + 0.05f * c), harmonics: V(0.25f + 0.4f * w.Bright)); // thoomp
                Noise(b, rng, 0, V(0.5f), 0.001f, V(0.03f + 0.02f * c), V(900 + 1400 * w.Bright)); // the puff of gas
                Tone(b, V(0.004f), 0.3f, V(620 * pitch), V(540 * pitch), 0.05f, 0.001f, V(0.05f)); // the tube ringing
                Click(b, rng, V(0.02f), V(0.5f), V(0.8f + 0.4f * w.Bright)); // breech
                Echo(b, 0.2f + 0.3f * w.Tail, V(0.6f + 0.6f * w.Tail));
                return Finish(b, 0.75f);
            }
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
            WeaponClass.Launcher => 0.7f,
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
            case WeaponClass.Launcher when w.Mag > 1:
                // Drum / revolving launcher: swing the cylinder out, shells in one by one, slap it shut.
                Clunk(b, rng, 0.1f, 0.8f, pitch * 0.8f);
                for (var i = 0; i < Math.Min(w.Mag, 6); i++)
                    Clunk(b, rng, 0.3f + i * (T * 0.55f / Math.Min(w.Mag, 6)), 0.55f, pitch * V(0.9f));
                Slide(b, rng, T * 0.85f, 0.4f, 0.1f, 1600);
                Clunk(b, rng, T * 0.92f, 1.0f, pitch * 0.75f);
                break;
            case WeaponClass.Launcher:
                // Single tube: a hollow knock, the round sliding in down the tube, a latch.
                Clunk(b, rng, 0.12f, 0.7f, pitch * 0.7f);
                Slide(b, rng, T * 0.35f, 0.5f, T * 0.25f, 900);
                Clunk(b, rng, T * 0.68f, 1.0f, pitch * 0.6f);
                Click(b, rng, T * 0.88f, 0.8f, pitch * 0.9f);
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

    public static short[] Effect(Sfx sfx) => Effect(sfx, 0);

    /// <summary>An effect, variant <paramref name="variant"/> (0 = the original; others differ in their random detail).</summary>
    public static short[] Effect(Sfx sfx, int variant)
    {
        if (Foley(sfx, variant) is { } foley)
            return foley;
        if (TacticalFoley(sfx, variant) is { } tactical)
            return tactical; // SynthTactical.cs
        if (StreakFoley(sfx, variant) is { } streak)
            return streak; // SynthStreaks.cs
        if (LauncherFoley(sfx, variant) is { } launcher)
            return launcher; // SynthLaunchers.cs
        if (GoreFoley(sfx, variant) is { } gore)
            return gore; // SynthGore.cs
        var rng = new Random(77 + (int)sfx + variant * 1000);
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
            case Sfx.StreakReady:
            {
                b = new Buf(0.8f);
                float[] chime = [660, 880, 1320];
                for (var i = 0; i < chime.Length; i++)
                    Tone(b, i * 0.08f, 0.55f, chime[i], chime[i], 1, 0.002f, i == chime.Length - 1 ? 0.25f : 0.06f, harmonics: 0.35f);
                Echo(b, 0.25f, 0.8f);
                return Finish(b, 0.55f);
            }
            case Sfx.StreakCall:
                b = new Buf(0.55f);
                Noise(b, rng, 0, 0.35f, 0.002f, 0.05f, 5000, highpass: true); // radio key-up hiss
                Tone(b, 0.06f, 0.6f, 1050, 1050, 1, 0.002f, 0.05f, harmonics: 0.5f, length: 0.09f);
                Tone(b, 0.2f, 0.6f, 1400, 1400, 1, 0.002f, 0.05f, harmonics: 0.5f, length: 0.09f);
                Noise(b, rng, 0.32f, 0.25f, 0.002f, 0.05f, 5000, highpass: true);
                return Finish(b, 0.5f);
            case Sfx.MortarWhistle:
                b = new Buf(1.2f);
                Tone(b, 0, 0.7f, 2100, 650, 0.75f, 0.35f, 0.5f, length: 1.15f); // falling whistle
                Noise(b, rng, 0, 0.12f, 0.4f, 0.4f, 3000, highpass: true, length: 1.15f); // air rush
                return Finish(b, 0.6f);
            case Sfx.TurretShot:
                b = new Buf(0.3f);
                Noise(b, rng, 0, 1.0f, 0.0005f, 0.02f, 4500); // crack
                Tone(b, 0, 0.8f, 240, 90, 0.02f, 0.001f, 0.05f); // thump
                Click(b, rng, 0.002f, 0.5f, 1.2f); // mechanical action
                Echo(b, 0.3f, 0.7f);
                return Finish(b, 0.7f);
            case Sfx.JetFlyby:
                b = new Buf(2.8f);
                Noise(b, rng, 0, 0.9f, 0.9f, 0.6f, 1400, length: 2.7f); // roar swelling as it passes
                Noise(b, rng, 0.2f, 0.4f, 0.7f, 0.5f, 6000, highpass: true, length: 2.4f); // turbine whine
                Tone(b, 0, 0.35f, 420, 190, 1.2f, 0.8f, 0.6f, harmonics: 0.3f, length: 2.6f); // doppler drop
                return Finish(b, 0.85f);
            case Sfx.BreakWood:
                b = new Buf(0.6f);
                Noise(b, rng, 0, 0.9f, 0.001f, 0.05f, 1800); // crack
                Tone(b, 0, 0.6f, 190, 110, 0.06f, 0.001f, 0.08f); // thud
                for (var i = 0; i < 7; i++) // splinters and planks landing
                    Click(b, rng, 0.03f + (float)rng.NextDouble() * 0.45f, 0.35f, 0.7f + (float)rng.NextDouble() * 0.5f);
                return Finish(b, 0.6f);
            case Sfx.BreakGlass:
                b = new Buf(0.6f);
                Noise(b, rng, 0, 0.7f, 0.0005f, 0.03f, 6000, highpass: true); // shatter
                for (var i = 0; i < 9; i++) // tinkling shards
                    Tone(b, 0.01f + (float)rng.NextDouble() * 0.4f, 0.25f, 3000 + (float)rng.NextDouble() * 3000, 2800 + (float)rng.NextDouble() * 2500, 1, 0.0005f, 0.03f);
                return Finish(b, 0.5f);
            case Sfx.BreakMetal:
                b = new Buf(0.7f);
                Tone(b, 0, 0.7f, 420, 380, 1, 0.001f, 0.25f, harmonics: 0.6f); // clang
                Tone(b, 0, 0.35f, 1130, 1090, 1, 0.001f, 0.15f);
                Noise(b, rng, 0, 0.5f, 0.001f, 0.04f, 2500);
                Click(b, rng, 0.25f, 0.3f, 0.8f);
                return Finish(b, 0.55f);
            case Sfx.BreakSoft:
                b = new Buf(0.35f);
                Noise(b, rng, 0, 0.7f, 0.004f, 0.1f, 1200); // crumple
                Noise(b, rng, 0.04f, 0.35f, 0.01f, 0.1f, 3500, highpass: true); // rustle
                return Finish(b, 0.45f);
            default:
                return new short[1];
        }
    }
}
