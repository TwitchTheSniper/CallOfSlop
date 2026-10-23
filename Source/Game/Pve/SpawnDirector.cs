namespace CallOfSlop;

/// <summary>The director's pacing phase (docs/pve/DESIGN.md "Spawn director").</summary>
public enum DirectorPhase : byte
{
    /// <summary>The spawn rate ramps up and the alive target climbs from 40% to 100% of the budget.</summary>
    BuildUp,

    /// <summary>The alive target stays at the full budget; squads keep coming.</summary>
    Peak,

    /// <summary>No new spawns (bar a trickle when nearly nobody is left): the players get a breather.</summary>
    Relax,
}

/// <summary>
/// The co-op spawn director: decides WHEN and HOW MANY enemies to spawn, never more alive than <see cref="Budget"/>,
/// in squads, with intensity pacing (build-up, peak, relax, like an AI director). It knows nothing about the map or the
/// game: the game asks it every tick and picks the spawn point itself (GamePve.cs), so this class is plain maths that
/// the self-test can drive directly. Deterministic: its own seeded random numbers.
/// </summary>
public sealed class SpawnDirector
{
    /// <summary>The most enemies alive at once (the alive budget).</summary>
    public int Budget = 24;

    /// <summary>Enemies still to spawn this stage (-1: no total, e.g. a defend stage until its timer ends).</summary>
    public int TotalLeft = -1;

    /// <summary>Share of the budget the peaks aim for (a stage's intensity: 0.2 light harassment to 1.0 all in).</summary>
    public float Intensity = 1f;

    /// <summary>Waves come stronger each time (defend stages).</summary>
    public bool Escalate;

    public DirectorPhase Phase;
    public double PhaseStart, PhaseUntil;

    /// <summary>Waves started this stage (each build-up starts one).</summary>
    public int Wave;

    /// <summary>How hard the players are having it (0-1): damage they took and kills close to them push it up, it decays.</summary>
    public float Stress;

    public int Spawned, Recycled;
    public bool Paused;

    private double _nextSquad, _lastUpdate;
    private uint _rng = 1;

    /// <summary>A fresh stage: budget, total (-1: none), intensity, seed.</summary>
    public void Reset(int budget, int total, float intensity, bool escalate, uint seed, double now)
    {
        Budget = Math.Max(0, budget);
        TotalLeft = total;
        Intensity = Math.Clamp(intensity, 0.05f, 1f);
        Escalate = escalate;
        Stress = 0;
        Spawned = Recycled = 0;
        Paused = false;
        Wave = 0;
        _rng = seed == 0 ? 0x9E3779B9u : seed;
        _lastUpdate = now;
        BeginBuildUp(now);
        _nextSquad = now + 1.0;
    }

    private uint Next()
    {
        var x = _rng;
        x ^= x << 13;
        x ^= x >> 17;
        x ^= x << 5;
        return _rng = x == 0 ? 0x9E3779B9u : x;
    }

    private float Rand() => (Next() >> 8) / 16777216f;

    private void BeginBuildUp(double now)
    {
        Wave++;
        Phase = DirectorPhase.BuildUp;
        PhaseStart = now;
        PhaseUntil = now + 18 + Rand() * 10;
    }

    /// <summary>The wave's strength: escalating stages start at 75% and gain 10% a wave up to 125% of the intensity.</summary>
    public float WaveScale => Escalate ? Math.Min(1.25f, 0.75f + 0.1f * (Wave - 1)) : 1f;

    /// <summary>How many enemies the director wants alive right now.</summary>
    public int TargetAlive(double now)
    {
        var full = Budget * Math.Min(1f, Intensity * WaveScale);
        var f = Phase switch
        {
            DirectorPhase.BuildUp => 0.4f + 0.6f * (float)Math.Clamp((now - PhaseStart) / Math.Max(1, PhaseUntil - PhaseStart), 0, 1),
            DirectorPhase.Peak => 1f,
            _ => 0.15f,
        };
        return Math.Clamp((int)MathF.Round(full * f), 0, Budget);
    }

    /// <summary>Damage taken / danger near the players feeds the stress (0-1).</summary>
    public void AddStress(float s) => Stress = Math.Clamp(Stress + s, 0f, 1f);

    /// <summary>
    /// Every server tick: advance the pacing, and return the size of the squad to spawn now (0: none). Never more than
    /// <see cref="Budget"/> minus <paramref name="alive"/>, never more than <see cref="TotalLeft"/>.
    /// </summary>
    public int Update(double now, int alive)
    {
        var dt = Math.Clamp(now - _lastUpdate, 0, 1);
        _lastUpdate = now;
        Stress = MathF.Max(0, Stress - (float)dt * 0.06f); // ~15 s from full to calm

        // Pacing.
        switch (Phase)
        {
            case DirectorPhase.BuildUp when now >= PhaseUntil:
                Phase = DirectorPhase.Peak;
                PhaseStart = now;
                PhaseUntil = now + 14 + Rand() * 8 + (Escalate ? Math.Min(15, 3 * (Wave - 1)) : 0);
                break;
            case DirectorPhase.Peak when now >= PhaseUntil || (Stress > 0.85f && now - PhaseStart > 6):
                Phase = DirectorPhase.Relax;
                PhaseStart = now;
                PhaseUntil = now + (Escalate ? 6 : 9) + Rand() * 5;
                break;
            case DirectorPhase.Relax when now >= PhaseUntil || (alive <= Budget * 0.3f * Intensity && now - PhaseStart > 3 && Stress < 0.5f):
                BeginBuildUp(now);
                break;
        }

        if (Paused || TotalLeft == 0 || now < _nextSquad)
            return 0;
        var room = Math.Min(TargetAlive(now) - alive, Budget - alive);
        if (TotalLeft > 0)
            room = Math.Min(room, TotalLeft);
        if (room <= 0)
            return 0;
        // Squads of 2-6 (bigger at the peak), one every 1.5-4 s.
        var heat = Phase == DirectorPhase.Peak ? 1f : Phase == DirectorPhase.BuildUp ? 0.5f : 0f;
        var size = Math.Clamp(2 + (int)MathF.Round(heat * 3 + Rand() * 1.5f), 2, 6);
        _nextSquad = now + (Phase == DirectorPhase.Peak ? 1.5 : 2.5) + Rand() * 1.5;
        return Math.Min(size, room);
    }

    /// <summary>The game spawned <paramref name="n"/> of the squad (fewer when it found no spawn spot).</summary>
    public void OnSpawned(int n)
    {
        Spawned += n;
        if (TotalLeft > 0)
            TotalLeft = Math.Max(0, TotalLeft - n);
    }

    /// <summary>An enemy was recycled (far away and unseen, or stuck): its slot comes back to be spawned near the action.</summary>
    public void OnRecycled()
    {
        Recycled++;
        if (TotalLeft >= 0)
            TotalLeft++;
    }

    /// <summary>Try again soon (no spawn spot was free).</summary>
    public void Retry(double now) => _nextSquad = Math.Min(_nextSquad, now + 0.5);
}
