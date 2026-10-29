namespace CallOfSlop;

/// <summary>
/// The mass horde's pace by round (docs/zombies/MASS-HORDE.md, "Pace by round"): the crowd tiers move at the same speed
/// mix the round's individuals get (<see cref="ZombiesRules.SpeedMix"/>: the shares that walk, run and sprint, at the
/// rules' speeds times the walker type's speed multiplier, as <see cref="Game.SpawnZombie"/> and Game.SpeedOf give them).
/// Each crowd zombie carries a speed class (<see cref="ClassOf"/>: rolled from its own number, so the same zombie gets the
/// same class), kept when it is woken into an individual and when an individual is put back to sleep, so its speed
/// doesn't jump at a tier change. The mass (per-cell counts) flows at the mix's <see cref="Mean"/>. Clients work the
/// same pace out from the round number they are sent (no wire change). With the rules' <c>paceByRound</c> off, every
/// crowd zombie walks at the fixed <c>speed</c> with its <c>speedSpread</c>, as before. A plain value: no allocation.
/// </summary>
public struct CrowdPace : IEquatable<CrowdPace>
{
    /// <summary>The speed of each class (m/s): walk, run, sprint.</summary>
    public float Walk, Run, Sprint;

    /// <summary>The shares of the round that run and sprint (the rest walk).</summary>
    public float RunShare, SprintShare;

    /// <summary>Each crowd zombie's own share of its class's speed, either way (from its number).</summary>
    public float Spread;

    /// <summary>The round's mix (else one fixed speed).</summary>
    public bool ByRound;

    /// <summary>The fixed pace (the old crowd: one speed with a spread, every zombie a walker).</summary>
    public static CrowdPace Fixed(float speed, float spread) => new() { Walk = speed, Run = speed, Sprint = speed, Spread = spread };

    /// <summary>The crowd's pace in round <paramref name="round"/> (<paramref name="t"/>: the crowd's tuning, whose speed may already be scaled; <paramref name="scale"/>: a stress run's speed-up).</summary>
    public static CrowdPace For(ZombiesRules r, CrowdTuning t, int round, float scale = 1f)
    {
        if (!t.PaceByRound)
            return Fixed(t.Speed, t.SpeedSpread);
        var walker = r.TypeAt(0);
        var mult = walker.SpeedMult * scale;
        var (run, sprint) = r.SpeedMix(Math.Max(1, round));
        if (walker.Gait >= 0)
            (run, sprint) = walker.Gait switch { (int)ZSpeed.Sprint => (0f, 1f), (int)ZSpeed.Run => (1f, 0f), _ => (0f, 0f) }; // (a walker with a gait of its own: TypeSetup)
        return new CrowdPace
        {
            Walk = r.WalkSpeed * mult,
            Run = r.RunSpeed * mult,
            Sprint = r.SprintSpeed * mult,
            RunShare = run,
            SprintShare = sprint,
            Spread = t.PaceSpread,
            ByRound = true,
        };
    }

    /// <summary>The speed class (0 walk, 1 run, 2 sprint: <see cref="ZSpeed"/>) of the crowd zombie numbered <paramref name="h"/>.</summary>
    public readonly byte ClassOf(uint h)
    {
        if (!ByRound)
            return 0;
        var r = (HordeCrowd.Hash(h ^ 0x9E3779B9u) >> 8) / 16777216f;
        return r < SprintShare ? (byte)ZSpeed.Sprint : r < SprintShare + RunShare ? (byte)ZSpeed.Run : (byte)ZSpeed.Walk;
    }

    /// <summary>A class's speed (m/s).</summary>
    public readonly float SpeedOf(int cls) => cls >= (int)ZSpeed.Sprint ? Sprint : cls == (int)ZSpeed.Run ? Run : Walk;

    /// <summary>The share of its class's speed the zombie numbered <paramref name="h"/> walks at (1 +- <see cref="Spread"/>).</summary>
    public readonly float Factor(uint h) => 1 + Spread * ((h & 0xFFFF) / 32767.5f - 1);

    /// <summary>The mix's mean speed (the mass tier's flow).</summary>
    public readonly float Mean => Walk * (1 - RunShare - SprintShare) + Run * RunShare + Sprint * SprintShare;

    /// <summary>The fastest class anyone in the mix has.</summary>
    public readonly float Max => SprintShare > 0 ? Sprint : RunShare > 0 ? Run : Walk;

    public readonly bool Equals(CrowdPace o) => Walk == o.Walk && Run == o.Run && Sprint == o.Sprint && RunShare == o.RunShare && SprintShare == o.SprintShare && Spread == o.Spread && ByRound == o.ByRound;

    public override readonly bool Equals(object? obj) => obj is CrowdPace o && Equals(o);

    public override readonly int GetHashCode() => HashCode.Combine(Walk, Run, Sprint, RunShare, SprintShare, Spread, ByRound);
}
