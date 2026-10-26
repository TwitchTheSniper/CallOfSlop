using System.Numerics;

namespace CallOfSlop;

/// <summary>A zombie sound kind (in <see cref="Sfx"/> order from <see cref="Sfx.ZombieGroan"/>).</summary>
public enum ZSound : byte
{
    Groan,
    Growl,
    Attack,
    Hurt,
    Headshot,
    Death,
    HeadDeath,
    Shuffle,
    Run,
    Sprint,
    COUNT,
}

/// <summary>What <see cref="ZombieSounds"/> remembers of a zombie (client side; drawing and sound only, never gameplay).</summary>
public sealed partial class Zombie
{
    /// <summary>
    /// The server tick its last head hit landed on (server: from the shot; clients: the first snapshot whose state byte
    /// has the head bit, protocol 50). A flinch this close to it is a headshot.
    /// </summary>
    public double HeadHitTick = -1e9;

    internal bool VoxInit, VoxDead;
    internal ZState VoxState;
    internal double VoxAttack, VoxFlinch, VoxNext, VoxUntil, VoxHurtAt;
    internal float VoxGait;
    internal sbyte VoxSlot = -1;
    internal byte VoxLast = 255;
}

/// <summary>
/// The zombies' voices and footsteps (docs/zombies/ZOMBIES-MODE.md, "Sound"), worked out on every PC from what it
/// already knows about each zombie: the walk cycle gives the footfalls (a shuffle, a run or a sprint by its speed class),
/// a new swipe the snarl, a flinch a grunt (or a screech when the server's head-hit bit is on), dying a death rattle (a
/// cut-off screech for a headshot kill), rising or climbing in a growl, and a per-zombie timer the groans, moans and
/// growls (more often chasing or tearing boards). No server work and no messages of its own.
/// <para>
/// Capped: <see cref="VoiceCap"/> vocal and <see cref="StepCap"/> step voices (nearest first; an idle groan counts as
/// further away than an event, so it never takes a voice from a snarl or a death), at most one vocal per zombie at a
/// time, and the idle groans share a budget of <see cref="GroansPerSecond"/> (bursts of <see cref="GroanBurst"/>).
/// Variety: each zombie has its own voice pitch (by id, the same on every PC) and never repeats its last variant.
/// Allocation-free.
/// </para>
/// </summary>
public sealed class ZombieSounds
{
    public const int VoiceCap = 8, StepCap = 8;

    /// <summary>The idle groan budget: this many a second on average, at most <see cref="GroanBurst"/> at once.</summary>
    public const float GroansPerSecond = 3f, GroanBurst = 4f;

    /// <summary>A flinch within this many server ticks of a head hit is a headshot.</summary>
    public const double HeadWindowTicks = 8;

    public readonly FootstepVoicePool Voices = new(VoiceCap), Steps = new(StepCap);
    private readonly ushort[] _owner = new ushort[VoiceCap];

    /// <summary>Per kind: started, skipped (too far away to hear) and dropped by the voice cap.</summary>
    public readonly int[] Played = new int[(int)ZSound.COUNT], Culled = new int[(int)ZSound.COUNT], Dropped = new int[(int)ZSound.COUNT];

    /// <summary>Idle groans put off by the groan budget, and by the zombie's own voice still sounding.</summary>
    public int RateLimited, OwnBusy;

    /// <summary>Seconds of frames seen (the director's own clock).</summary>
    public double Clock;

    private float _tokens = GroanBurst;
    private uint _rng = 0x9E3779B9u;

    public static Sfx SfxOf(ZSound k) => (Sfx)((int)Sfx.ZombieGroan + (int)k);

    /// <summary>Loudness before distance and the mix.</summary>
    public static float Gain(ZSound k) => k switch
    {
        ZSound.Groan => 0.5f,
        ZSound.Growl => 0.6f,
        ZSound.Attack => 0.85f,
        ZSound.Hurt => 0.7f,
        ZSound.Headshot => 0.85f,
        ZSound.Death or ZSound.HeadDeath => 0.8f,
        _ => 0.5f,
    };

    /// <summary>Beyond this distance (m) the sound isn't started at all.</summary>
    public static float Range(ZSound k) => k switch
    {
        ZSound.Groan => 40f,
        ZSound.Growl => 45f,
        ZSound.Attack => 35f,
        ZSound.Hurt => 40f,
        ZSound.Headshot or ZSound.Death or ZSound.HeadDeath => 45f,
        ZSound.Shuffle => 16f,
        ZSound.Run => 26f,
        _ => 36f, // a sprint is heard furthest
    };

    public static ZSound StepOf(ZSpeed s) => s switch { ZSpeed.Sprint => ZSound.Sprint, ZSpeed.Run => ZSound.Run, _ => ZSound.Shuffle };

    /// <summary>A footfall's loudness by speed class: a shuffle is soft, a sprint stamps.</summary>
    public static float StepGain(ZSpeed s) => s switch { ZSpeed.Sprint => 0.75f, ZSpeed.Run => 0.55f, _ => 0.32f };

    /// <summary>A zombie's own voice pitch (0.84-1.16), from its id: the same on every PC.</summary>
    public static float VoicePitch(ushort id)
    {
        var h = (uint)id * 2246822519u;
        h ^= h >> 13;
        h *= 3266489917u;
        h ^= h >> 16;
        return 0.84f + (h & 1023) / 1023f * 0.32f;
    }

    /// <summary>Seconds until a zombie's next idle groan in <paramref name="s"/> (sprinters are noisier).</summary>
    private double GroanGap(ZState s, ZSpeed speed)
    {
        var (lo, hi) = s switch
        {
            ZState.Chase or ZState.Attack => (3.0, 6.0),
            ZState.Tearing => (2.2, 4.5),
            _ => (5.0, 10.0),
        };
        return (lo + (hi - lo) * R()) * (speed == ZSpeed.Sprint ? 0.8 : 1.0);
    }

    private float R()
    {
        _rng ^= _rng << 13;
        _rng ^= _rng >> 17;
        _rng ^= _rng << 5;
        return (_rng & 0xFFFFFF) / (float)0x1000000;
    }

    /// <summary>Every frame (both sides, Zombies mode): what every zombie in <paramref name="horde"/> sounds like now.</summary>
    public void Frame(Audio audio, List<Zombie> horde, float dt)
    {
        if (!(dt > 0f))
            return;
        Clock += dt;
        _tokens = MathF.Min(GroanBurst, _tokens + dt * GroansPerSecond);
        var listener = audio.ListenerPosition;
        for (var i = 0; i < horde.Count; i++)
            Update(audio, horde[i], listener);
    }

    private void Update(Audio audio, Zombie z, Vector3 listener)
    {
        var feet = z.RenderPos;
        var mouth = feet + new Vector3(0, 1.55f, 0);
        var dist = Vector3.Distance(mouth, listener);
        if (!z.VoxInit)
        {
            // First seen (spawned, or already there when we joined): nothing old replays.
            z.VoxInit = true;
            z.VoxAttack = z.AttackTick;
            z.VoxFlinch = z.FlinchTick;
            z.VoxGait = z.Gait;
            z.VoxDead = z.State == ZState.Dead;
            z.VoxState = z.State;
            z.VoxNext = Clock + 0.5 + 4.0 * R(); // a horde doesn't all groan on the same frame
            if (z.State == ZState.Rising)
                Voice(audio, z, ZSound.Growl, mouth, dist);
            return;
        }
        if (z.State == ZState.Dead)
        {
            if (!z.VoxDead)
            {
                z.VoxDead = true;
                Voice(audio, z, z.KilledByHead ? ZSound.HeadDeath : ZSound.Death, mouth, dist);
            }
            z.VoxGait = z.Gait;
            return;
        }
        z.VoxDead = false;

        if (z.AttackTick != z.VoxAttack)
        {
            z.VoxAttack = z.AttackTick;
            Voice(audio, z, ZSound.Attack, mouth, dist);
        }
        if (z.FlinchTick != z.VoxFlinch)
        {
            z.VoxFlinch = z.FlinchTick;
            if (Clock - z.VoxHurtAt > 0.3) // burns and bleeds flinch every tick: one grunt at a time
            {
                z.VoxHurtAt = Clock;
                var head = Math.Abs(z.FlinchTick - z.HeadHitTick) <= HeadWindowTicks;
                Voice(audio, z, head ? ZSound.Headshot : ZSound.Hurt, mouth, dist);
            }
        }
        if (z.State != z.VoxState)
        {
            z.VoxState = z.State;
            if (z.State is ZState.Rising or ZState.Climbing)
                Voice(audio, z, ZSound.Growl, mouth, dist);
        }
        if (Clock >= z.VoxNext)
            Groan(audio, z, mouth, dist);

        // Footfalls: a foot plants each time the walk cycle passes pi/2 + k pi (ZombieAnim's foot lift is max(0, cos)).
        var g = z.Gait;
        var prev = z.VoxGait;
        if (prev - g > 100f)
            prev -= MathF.Tau * 150; // the cycle was wrapped (ZombieAnimNet.cs)
        z.VoxGait = g;
        if (g > prev && z.AnimMove > 0.2f && z.State is ZState.ToWindow or ZState.Chase or ZState.Attack
            && MathF.Floor((g - MathF.PI / 2) / MathF.PI) > MathF.Floor((prev - MathF.PI / 2) / MathF.PI))
            Step(audio, z, feet, Vector3.Distance(feet, listener));
    }

    private void Groan(Audio audio, Zombie z, Vector3 mouth, float dist)
    {
        if (z.State is ZState.Rising or ZState.Climbing)
        {
            z.VoxNext = Clock + 1.0;
            return;
        }
        var kind = z.State == ZState.ToWindow ? (R() < 0.75f ? ZSound.Groan : ZSound.Growl) : (R() < 0.65f ? ZSound.Growl : ZSound.Groan);
        if (dist > Range(kind))
        {
            Culled[(int)kind]++;
            z.VoxNext = Clock + GroanGap(z.State, z.Speed);
            return;
        }
        if (_tokens < 1f)
        {
            RateLimited++;
            z.VoxNext = Clock + 0.4 + 0.8 * R();
            return;
        }
        if (z.VoxUntil > Clock && z.VoxSlot >= 0 && _owner[z.VoxSlot] == z.Id)
        {
            OwnBusy++;
            z.VoxNext = z.VoxUntil + 0.5 + R();
            return;
        }
        _tokens -= 1f;
        Voice(audio, z, kind, mouth, dist);
        z.VoxNext = Clock + GroanGap(z.State, z.Speed);
    }

    /// <summary>Starts a vocal for <paramref name="z"/> (an event takes over the zombie's own voice if it's still sounding).</summary>
    private void Voice(Audio audio, Zombie z, ZSound kind, Vector3 mouth, float dist)
    {
        if (dist > Range(kind))
        {
            Culled[(int)kind]++;
            return;
        }
        var sfx = SfxOf(kind);
        var pitch = VoicePitch(z.Id) * (0.96f + 0.08f * R());
        var seconds = Synth.ZombieSeconds(sfx) / pitch;
        var idle = kind is ZSound.Groan or ZSound.Growl;
        int v;
        if (z.VoxUntil > Clock && z.VoxSlot >= 0 && _owner[z.VoxSlot] == z.Id)
        {
            v = z.VoxSlot; // one mouth: the new sound cuts its own last one
            Voices.Take(v, Clock, idle ? dist * 1.5f : dist, seconds);
        }
        else
            v = Voices.Acquire(Clock, idle ? dist * 1.5f : dist, seconds);
        if (v < 0)
        {
            Dropped[(int)kind]++;
            return;
        }
        _owner[v] = z.Id;
        z.VoxSlot = (sbyte)v;
        z.VoxUntil = Clock + seconds;
        if (!idle)
            z.VoxNext = Math.Max(z.VoxNext, Clock + seconds + 1.0); // no groan straight after a snarl or a grunt
        var n = Synth.VariantCount(sfx);
        var variant = n <= 1 ? 0 : z.VoxLast < n ? (z.VoxLast + 1 + (int)(R() * (n - 1))) % n : (int)(R() * n);
        z.VoxLast = (byte)variant;
        Played[(int)kind]++;
        audio.ZombieSound(v, sfx, variant, mouth, Gain(kind) * (0.92f + 0.16f * R()), pitch);
    }

    private void Step(Audio audio, Zombie z, Vector3 feet, float dist)
    {
        var kind = StepOf(z.Speed);
        if (dist > Range(kind))
        {
            Culled[(int)kind]++;
            return;
        }
        var sfx = SfxOf(kind);
        var pitch = (0.92f + 0.16f * (VoicePitch(z.Id) - 0.84f) / 0.32f) * (0.95f + 0.1f * R()); // heavier and lighter zombies
        var v = Steps.Acquire(Clock, dist, Synth.ZombieSeconds(sfx) / pitch);
        if (v < 0)
        {
            Dropped[(int)kind]++;
            return;
        }
        Played[(int)kind]++;
        audio.ZombieSound(v, sfx, (int)(R() * Synth.VariantCount(sfx)), feet, StepGain(z.Speed) * (0.85f + 0.3f * R()), pitch);
    }

    /// <summary>Totals over the kinds (self-test, logs).</summary>
    public int TotalPlayed()
    {
        var n = 0;
        foreach (var x in Played)
            n += x;
        return n;
    }

    /// <summary>Forgets the counters (not the voices).</summary>
    public void ResetStats()
    {
        Array.Clear(Played);
        Array.Clear(Culled);
        Array.Clear(Dropped);
        RateLimited = OwnBusy = 0;
    }
}
