using System.Numerics;

namespace CallOfSlop;

/// <summary>What a building sound is. Add new ones at the end, with a row in <see cref="BuildSounds"/>.</summary>
public enum BuildSound : byte
{
    Snap, // the ghost locks onto a socket (at your ear)
    Rotate, // the ghost turns (at your ear)
    Refuse, // a click was refused (at your ear)
    Place, // a part lands: a click to a deep thud by size, flavoured by material
    Unbolt, // a part comes off with a tool
    Pop, // the remover deletes a part
    WeldDone, // a weld finishes
    LinkNode, // the linking tool sets a node (at your ear)
    LinkCable, // a cable or wire is finished
    LinkPipe, // a pipe, shaft or chute is finished
    Grab, // the physics gun takes hold
    Drop, // the physics gun lets go
    Count,
}

/// <summary>What a part sounds like when it lands (from its <c>material</c>, see <see cref="BuildSounds.MaterialOf"/>).</summary>
public enum BuildMaterial : byte
{
    Metal,
    Wood,
    Stone,
    Rubber,
    Count,
}

/// <summary>
/// Building's sounds (the Sandbox; docs/sandbox/CONSTRUCTS.md "Building sounds"). <see cref="Play"/> is the one call: it
/// plays on THIS peer only, and is safe from anywhere on the game thread (nothing with no audio device, as on a test's
/// headless peers). A bulk action never makes a crowd: hits of the same kind close together merge into one short burst of
/// at most <see cref="BurstHits"/>, on a voice pool of their own, and sounds beyond their range don't start. Parts going on
/// and coming off are already heard by every peer, from the structure the server sends (Game.SandboxBuildSounds), line
/// fill and paste included: call this only for what that can't see, and a call for the same thing just merges with it.
/// Nothing allocates.
/// </summary>
public static class BuildSounds
{
    /// <summary>Voices for one-shots (their own: building never takes a gun's voice).</summary>
    public const int ShotVoices = 8;

    /// <summary>Voices for welders' arcs and wrenches' ratchets (the nearest players').</summary>
    public const int LoopVoices = 3;

    /// <summary>The most hits a burst plays, however many parts it stands for.</summary>
    public const int BurstHits = 5;

    /// <summary>How long a burst takes more parts in, and how far apart it may reach (s, m).</summary>
    private const double BurstWindow = 0.3;

    private const float BurstReach = 7f;

    private const double BurstGap = 0.045; // (s between hits)
    private const float BurstFade = 0.72f; // (each hit, of the one before)

    private const int MaxBursts = 6;

    /// <summary>A part with no mass given counts as this (kg): a middling one.</summary>
    public const float DefaultMass = 200f;

    /// <param name="Gain">Before the bus (mix row "build"; the clunk's is per size class, <see cref="TierGain"/>).</param>
    /// <param name="Range">Metres beyond which it doesn't start (ear sounds: none).</param>
    /// <param name="Gap">The least time between two of it (s).</param>
    /// <param name="Ear">Yours alone, at your ear.</param>
    /// <param name="Burst">Bulk actions merge it into a burst.</param>
    private readonly record struct Row(Sfx Sfx, float Gain, float Range, double Gap, bool Ear, bool Burst);

    private static readonly Row[] Rows = BuildRows();

    private static Row[] BuildRows()
    {
        var r = new Row[(int)BuildSound.Count];
        r[(int)BuildSound.Snap] = new(Sfx.BuildSnap, 0.16f, 0, 0.07, true, false);
        r[(int)BuildSound.Rotate] = new(Sfx.BuildRotate, 0.14f, 0, 0.05, true, false);
        r[(int)BuildSound.Refuse] = new(Sfx.BuildRefuse, 0.22f, 0, 0.35, true, false);
        r[(int)BuildSound.Place] = new(Sfx.BuildPlace, 1f, 0, 0, false, true);
        r[(int)BuildSound.Unbolt] = new(Sfx.BuildUnbolt, 0.3f, 30, 0, false, true);
        r[(int)BuildSound.Pop] = new(Sfx.BuildPop, 0.26f, 30, 0, false, true);
        r[(int)BuildSound.WeldDone] = new(Sfx.BuildWeldDone, 0.24f, 30, 0.1, false, false);
        r[(int)BuildSound.LinkNode] = new(Sfx.BuildLinkNode, 0.18f, 0, 0.05, true, false);
        r[(int)BuildSound.LinkCable] = new(Sfx.BuildLinkCable, 0.26f, 24, 0.1, false, false);
        r[(int)BuildSound.LinkPipe] = new(Sfx.BuildLinkPipe, 0.3f, 30, 0.1, false, false);
        r[(int)BuildSound.Grab] = new(Sfx.BuildGrab, 0.16f, 24, 0.1, false, false);
        r[(int)BuildSound.Drop] = new(Sfx.BuildDrop, 0.14f, 24, 0.1, false, false);
        return r;
    }

    /// <summary>The welder's arc: its gain, range and falloff.</summary>
    public const float WeldLoopGain = 0.2f, WeldLoopRange = 28f;

    /// <summary>The wrench's ratchet.</summary>
    public const float RatchetLoopGain = 0.16f, RatchetLoopRange = 18f;

    private static readonly Audio.Falloff BuildFalloff = new(3f, 1.2f, 45f);

    /// <summary>How big a clunk: its heaviest mass in a class: small under 40 kg, medium under 1.6 t, large under 63 t, else capital.</summary>
    private static readonly float[] TierTop = [40f, 1600f, 63000f, float.MaxValue];

    /// <summary>A clunk's gain by size class (before the bus).</summary>
    public static readonly float[] TierGain = [0.26f, 0.34f, 0.42f, 0.5f];

    /// <summary>How far a clunk carries by size class (m).</summary>
    public static readonly float[] TierRange = [30f, 45f, 70f, 110f];

    private static readonly float[] TierReference = [3f, 4f, 6f, 10f];

    /// <summary>The size class of a mass (kg).</summary>
    public static int Tier(float mass)
    {
        var t = 0;
        while (t < TierTop.Length - 1 && mass >= TierTop[t])
            t++;
        return t;
    }

    /// <summary>The sound of a part's <c>material</c> (steel, armour, glass and anything else are metal).</summary>
    public static BuildMaterial MaterialOf(string material) => material switch
    {
        "wood" => BuildMaterial.Wood,
        "stone" or "concrete" or "sand" => BuildMaterial.Stone,
        "rubber" or "cloth" => BuildMaterial.Rubber,
        _ => BuildMaterial.Metal,
    };

    private struct Pending
    {
        public BuildSound Kind;
        public BuildMaterial Material;
        public Vector3 At;
        public float Size;
        public double Start, Next;
        public int Done, Left;
    }

    private static Audio? _audio;
    private static readonly Pending[] Bursts = new Pending[MaxBursts];
    private static readonly double[] LastAt = new double[(int)BuildSound.Count];
    private static readonly Random Rng = new(4711);

    /// <summary>The audio these play on (the game sets it every Sandbox frame; until then they do nothing).</summary>
    public static void Bind(Audio audio) => _audio = audio;

    /// <summary>
    /// Plays a building sound on this peer. <paramref name="size"/>: the part's mass in kg (a clunk's size; 0: a middling part);
    /// for an ear sound, a gain scale (0: 1). <paramref name="count"/>: how many parts this one call stands for.
    /// </summary>
    public static void Play(BuildSound kind, Vector3 position, float size = 0f, BuildMaterial material = BuildMaterial.Metal, int count = 1)
    {
        if (_audio is not { Enabled: true } audio || (uint)kind >= (uint)BuildSound.Count)
            return;
        ref readonly var row = ref Rows[(int)kind];
        var now = Audio.BuildClock;
        if (row.Ear)
        {
            if (now - LastAt[(int)kind] < row.Gap)
                return;
            LastAt[(int)kind] = now;
            audio.BuildShot(row.Sfx, Rng.Next(16), null, row.Gain * (size > 0f ? size : 1f), Jitter(0.02f), BuildFalloff);
            return;
        }
        if (size <= 0f)
            size = DefaultMass;
        var range = kind == BuildSound.Place ? TierRange[Tier(size)] : row.Range;
        if (Vector3.DistanceSquared(position, audio.ListenerPosition) > range * range)
            return;
        if (row.Burst)
        {
            StartBurst(audio, kind, position, size, material, Math.Max(count, 1), now);
            return;
        }
        if (now - LastAt[(int)kind] < row.Gap)
            return;
        LastAt[(int)kind] = now;
        Hit(audio, kind, position, size, material, 0);
    }

    /// <summary>Joins the burst of this kind going on nearby, or starts one with its first hit now.</summary>
    private static void StartBurst(Audio audio, BuildSound kind, Vector3 at, float size, BuildMaterial material, int count, double now)
    {
        var free = 0;
        var oldest = double.MaxValue;
        for (var i = 0; i < Bursts.Length; i++)
        {
            ref var b = ref Bursts[i];
            if (b.Kind == kind && now - b.Start < BurstWindow && b.Start > 0 && Vector3.DistanceSquared(b.At, at) < BurstReach * BurstReach)
            {
                b.Size = MathF.Max(b.Size, size);
                b.Left = Math.Min(b.Left + count, BurstHits - b.Done);
                return;
            }
            if (b.Start < oldest)
            {
                oldest = b.Start;
                free = i;
            }
        }
        Bursts[free] = new Pending { Kind = kind, Material = material, At = at, Size = size, Start = now, Next = now + BurstGap, Done = 1, Left = Math.Min(count - 1, BurstHits - 1) };
        Hit(audio, kind, at, size, material, 0);
    }

    /// <summary>Every Sandbox frame: the bursts' next hits.</summary>
    public static void Tick()
    {
        if (_audio is not { Enabled: true } audio)
            return;
        var now = Audio.BuildClock;
        for (var i = 0; i < Bursts.Length; i++)
        {
            ref var b = ref Bursts[i];
            if (b.Left <= 0 || now < b.Next)
                continue;
            if (now > b.Next + 0.4)
            {
                b.Left = 0; // (the game was away: not a late rattle)
                continue;
            }
            Hit(audio, b.Kind, b.At, b.Size, b.Material, b.Done);
            b.Done++;
            b.Left--;
            b.Next = now + BurstGap * (0.8 + 0.4 * Rng.NextDouble());
        }
    }

    private static void Hit(Audio audio, BuildSound kind, Vector3 at, float size, BuildMaterial material, int n)
    {
        ref readonly var row = ref Rows[(int)kind];
        var fade = MathF.Pow(BurstFade, n);
        if (kind != BuildSound.Place)
        {
            audio.BuildShot(row.Sfx, Rng.Next(16), at, row.Gain * fade, Jitter(0.05f), BuildFalloff);
            return;
        }
        var tier = Tier(size);
        // (within its class a heavier part plays a little lower; a burst's later hits are lighter pieces settling)
        var low = tier == 0 ? 1f : TierTop[tier - 1];
        var top = tier == TierTop.Length - 1 ? low * 400f : TierTop[tier];
        var along = Math.Clamp(MathF.Log(MathF.Max(size, 1f) / low) / MathF.Log(top / low), 0f, 1f);
        var pitch = (1.12f - 0.24f * along) * Jitter(0.03f) * (1f + 0.04f * n);
        var refd = TierReference[tier];
        audio.BuildShot(row.Sfx, Synth.BuildPlaceVariant(tier, material, Rng.Next(Synth.BuildTakes)), at, TierGain[tier] * fade, pitch,
            new Audio.Falloff(refd, 1.1f, TierRange[tier]));
    }

    private static float Jitter(float amount) => 1f + ((float)Rng.NextDouble() - 0.5f) * 2f * amount;

    /// <summary>A welder's arc or a wrench's ratchet at <paramref name="at"/> on loop voice <paramref name="voice"/>.</summary>
    public static void LoopAt(int voice, bool weld, Vector3 at)
    {
        if (_audio is { Enabled: true } audio)
            audio.BuildLoopAt(voice, weld ? Sfx.BuildWeldLoop : Sfx.BuildRatchetLoop, at, weld ? WeldLoopGain : RatchetLoopGain, BuildFalloff);
    }

    /// <summary>Loop voice <paramref name="voice"/> off.</summary>
    public static void LoopStop(int voice) => _audio?.BuildLoopStop(voice);

    /// <summary>How far a welder's arc or a wrench's ratchet is heard.</summary>
    public static float LoopRange(bool weld) => weld ? WeldLoopRange : RatchetLoopRange;
}
