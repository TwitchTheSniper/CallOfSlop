using System.Text.Json;

namespace CallOfSlop;

/// <summary>Every tunable ragdoll number (<see cref="RagdollTuning"/>), in file order.</summary>
public enum RT
{
    // Soft joint limits (Ragdoll.SolveLinks)
    SoftRange, SoftStiffness, SoftExponent, SoftDamping, HardMargin, KneeSoft, KneeHard,

    // The pull towards the relaxed pose once a body slows (Ragdoll.RelaxPull)
    NeutralPull, NeutralMaxStep, NeutralSpeedFull, NeutralSpeedNone, NeutralQuiet, NeutralTime, NeutralArms, NeutralLegs, NeutralHead,

    // Bullet and blast pushes (Ragdolls.GunPower, Ragdolls.ApplyDeath, Ragdolls.BlastBody)
    GunScale, GunMin, GunCap, BodyShare, HitShare, NeighbourShare, HitLift, BlastScale,

    // Active ragdolls (RagdollActive): when a death is a near thing, how long, how many at once, the muscles
    OverkillShare, OverkillHp, ActiveMin, ActiveMax, MaxActive, MaxActiveZombies, ActivePush, MuscleGain, MuscleBlend, MuscleSpeed,
}

/// <summary>
/// The ragdoll's tuning in one place: soft joint limits (where the spring starts, how stiff, how it rises, its damping,
/// the hard backstop), the pull towards the relaxed pose, and how hard shots and blasts push bodies. The defaults are
/// here; Ragdolls/Ragdolls.json "tuning" overrides them (every key is optional, out-of-range values are refused with a
/// reason and the defaults kept), and the F7 weapon tweak window has a "Ragdoll tuning" panel with a slider per value
/// (live, local only: ragdolls are cosmetic). Reads are one array load: no allocation.
/// </summary>
public static class RagdollTuning
{
    /// <summary>One tunable: its JSON key, a label for the slider, the default and the allowed range.</summary>
    public readonly record struct Param(RT Id, string Key, string Label, float Default, float Min, float Max, string Help);

    public static readonly Param[] Params =
    [
        new(RT.SoftRange, "softRange", "Soft range", 0.4f, 0f, 1f,
            "Where a joint's spring starts, as a share of its free range (rest pose to limit): 0 = only at the limit, 1 = all the way from the rest pose."),
        new(RT.SoftStiffness, "softStiffness", "Soft stiffness", 0.14f, 0f, 1f, "The share of the overshoot a joint spring takes back per solver pass at the limit."),
        new(RT.SoftExponent, "softExponent", "Soft exponent", 2.5f, 1f, 4f, "How the spring stiffens towards the limit: k * t^exponent (t: 0 at the soft start, 1 at the limit)."),
        new(RT.SoftDamping, "softDamping", "Soft damping", 0.35f, 0f, 1f, "The share of a joint's bending speed taken per sub-step at the limit (less inside the soft zone): no bounce or jitter."),
        new(RT.HardMargin, "hardMargin", "Hard backstop", 0.06f, 0f, 0.3f, "How far past the limit (share of its distance) the hard backstop sits: nothing bends past it."),
        new(RT.KneeSoft, "kneeSoft", "Knee soft start (m)", 0.05f, 0f, 0.2f, "How far in front of the hip-foot line a knee starts to resist straightening."),
        new(RT.KneeHard, "kneeHard", "Knee backstop (m)", 0.03f, 0f, 0.1f, "How far a knee may bend backwards past its limit before the hard rule stops it."),

        new(RT.NeutralPull, "neutralPull", "Relax pull", 0.08f, 0f, 0.5f, "The share of the way to the relaxed pose a slow body's limbs move per step."),
        new(RT.NeutralMaxStep, "neutralMaxStep", "Relax max step (m)", 0.006f, 0f, 0.02f, "The most a limb moves towards the relaxed pose in one step."),
        new(RT.NeutralSpeedFull, "neutralSpeedFull", "Relax full below (m/s)", 0.5f, 0f, 5f, "The pull is at full strength when the fastest particle is slower than this."),
        new(RT.NeutralSpeedNone, "neutralSpeedNone", "Relax none above (m/s)", 1.6f, 0.01f, 10f, "No pull while the fastest particle is faster than this (the body is still falling)."),
        new(RT.NeutralQuiet, "neutralQuiet", "Relax quiet (m)", 0.0015f, 0f, 0.01f, "While relaxing, a body only counts as still (to fall asleep) when no particle moved more than this in a step."),
        new(RT.NeutralTime, "neutralTime", "Relax time (s)", 3f, 0f, 10f, "The pull works for at most this long after a body slows (or is woken), then fades out."),
        new(RT.NeutralArms, "neutralArms", "Relax arms", 1f, 0f, 2f, "The pull's weight on elbows and hands."),
        new(RT.NeutralLegs, "neutralLegs", "Relax legs", 0.8f, 0f, 2f, "The pull's weight on knees and feet."),
        new(RT.NeutralHead, "neutralHead", "Relax head", 0.5f, 0f, 2f, "The pull's weight on the head."),

        new(RT.GunScale, "gunScale", "Gun push scale", 1.75f, 0.1f, 4f, "Scales every shot's push: damage x (0.6 + calibre) / 18 x this."),
        new(RT.GunMin, "gunMin", "Gun push min (m/s)", 1.5f, 0f, 10f, "The weakest shot's push."),
        new(RT.GunCap, "gunCap", "Gun push cap (m/s)", 10f, 1f, 20f, "The strongest shot's push."),
        new(RT.BodyShare, "bodyShare", "Shot: whole body", 0.6f, 0f, 2f, "The share of a shot's push every particle gets (weighted towards the torso)."),
        new(RT.HitShare, "hitShare", "Shot: hit particle", 0.9f, 0f, 3f, "The extra share of a shot's push the hit particle gets."),
        new(RT.NeighbourShare, "neighbourShare", "Shot: neighbours", 0.4f, 0f, 2f, "The extra share of a shot's push the hit particle's neighbours get."),
        new(RT.HitLift, "hitLift", "Shot: lift", 0.15f, 0f, 1f, "An upward share of the push on the hit particle (and half on its neighbours)."),
        new(RT.BlastScale, "blastScale", "Blast scale", 1.15f, 0.1f, 4f, "Scales every blast's push on bodies (kept above the stronger gun push)."),

        new(RT.OverkillShare, "overkillShare", "Near thing: overkill share", 0.3f, 0f, 1f, "A kill is a near thing (an active death reaction) when the killing hit's overkill is under this share of the hit..."),
        new(RT.OverkillHp, "overkillHp", "Near thing: overkill HP", 30f, 0f, 200f, "...or under this many HP."),
        new(RT.ActiveMin, "activeMin", "Active time min (s)", 0.4f, 0f, 3f, "The active phase of a barely-near thing."),
        new(RT.ActiveMax, "activeMax", "Active time max (s)", 1.5f, 0f, 3f, "The active phase of a hit that did exactly enough (times the reaction's own duration)."),
        new(RT.MaxActive, "maxActive", "Most active bodies", 4f, 0f, 16f, "At most this many bodies are powered at once (the rest go limp)."),
        new(RT.MaxActiveZombies, "maxActiveZombies", "Most active zombies", 2f, 0f, 16f, "Of those, at most this many zombies (hordes)."),
        new(RT.ActivePush, "activePush", "Active: shot push share", 0.6f, 0f, 1f, "An active body takes this share of the shot's push (it staggers instead of flying)."),
        new(RT.MuscleGain, "muscleGain", "Muscle gain", 0.3f, 0f, 1f, "The share of the way to its target a driven particle aims to move per step."),
        new(RT.MuscleBlend, "muscleBlend", "Muscle blend", 0.35f, 0f, 1f, "How fast a driven particle's speed turns to the muscle's (per step, at full strength)."),
        new(RT.MuscleSpeed, "muscleSpeed", "Muscle top speed (m/s)", 4f, 0f, 12f, "The fastest a muscle drives a particle."),
    ];

    /// <summary>The live values, by <see cref="RT"/>.</summary>
    public static readonly float[] Values = Defaults();

    /// <summary>Problems found in the file the last time it was read (the self-test lists them).</summary>
    public static readonly List<string> LoadErrors = new();

    public static string PathName => Path.Combine(AppFolders.Data, "Ragdolls", "Ragdolls.json");

    private static bool _loaded;

    public static float Get(RT id) => Values[(int)id];

    private static float[] Defaults()
    {
        var v = new float[Params.Length];
        foreach (var p in Params)
            v[(int)p.Id] = p.Default;
        return v;
    }

    /// <summary>Reads Ragdolls/Ragdolls.json once (called when the first body spawns, and by the self-test).</summary>
    public static void EnsureLoaded()
    {
        if (_loaded)
            return;
        _loaded = true;
        Reload();
    }

    /// <summary>Back to the defaults, then the file's values and death reactions (none if the file is missing or broken: every death limp).</summary>
    public static void Reload()
    {
        _loaded = true;
        LoadErrors.Clear();
        Array.Copy(Defaults(), Values, Values.Length);
        RagdollActive.Reactions = [];
        string json;
        try
        {
            json = File.ReadAllText(PathName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LoadErrors.Add($"Ragdolls/Ragdolls.json: {ex.Message}");
            Console.WriteLine($"[ragdolls] {LoadErrors[^1]} (using the defaults)");
            return;
        }
        try
        {
            Load(json, "Ragdolls/Ragdolls.json");
        }
        catch (WeaponFileException ex)
        {
            LoadErrors.Add(ex.Message);
            Console.WriteLine($"[ragdolls] {ex.Message} (using the defaults)");
            Array.Copy(Defaults(), Values, Values.Length);
            RagdollActive.Reactions = [];
        }
    }

    /// <summary>Applies a ragdoll file's "tuning" section (throws <see cref="WeaponFileException"/> with the reason on a bad file).</summary>
    public static void Load(string json, string source)
    {
        using var doc = WeaponFile.Parse(json, source);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new WeaponFileException($"{source}: the ragdoll file is one JSON object");
        foreach (var p in root.EnumerateObject())
        {
            switch (p.Name)
            {
                case "format":
                    WeaponFile.CheckFormat(p.Value, "call-of-slop-ragdolls/", "call-of-slop-ragdolls/1", $"{source}: \"format\"");
                    break;
                case "note":
                    break;
                case "tuning":
                    LoadTuning(p.Value, $"{source}: \"tuning\"");
                    break;
                case "reactions":
                    RagdollActive.Reactions = RagdollActive.Load(p.Value, $"{source}: \"reactions\"");
                    break;
                default:
                    throw new WeaponFileException($"{source}: unknown section \"{p.Name}\"");
            }
        }
    }

    private static void LoadTuning(JsonElement e, string where)
    {
        if (e.ValueKind != JsonValueKind.Object)
            throw new WeaponFileException($"{where}: an object of name: number");
        foreach (var f in e.EnumerateObject())
        {
            if (f.Name == "note")
                continue;
            var i = Array.FindIndex(Params, x => x.Key == f.Name);
            if (i < 0)
                throw new WeaponFileException($"{where}: unknown value \"{f.Name}\" (known: {string.Join(", ", Params.Select(x => x.Key))})");
            var p = Params[i];
            var v = WeaponFile.Number(f.Value, $"{where}.{f.Name}");
            if (!(v >= p.Min && v <= p.Max))
                throw new WeaponFileException($"{where}.{f.Name}: {v} is outside {p.Min}..{p.Max}");
            Values[(int)p.Id] = v;
        }
    }
}
