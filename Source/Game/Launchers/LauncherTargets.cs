using System.Numerics;

namespace CallOfSlop;

/// <summary>How launchers treat one kind of kill streak machine (see <see cref="LauncherTargets"/>).</summary>
public sealed class LauncherTargetProfile
{
    /// <summary>Flies: lock-on launchers can lock on to it and proximity fuses go off next to it. Ground machines (the sentry) only take dumb fire.</summary>
    public bool Airborne;

    /// <summary>
    /// Lock-on hits that bring it down from full health, whatever its hit points (0 = worked out from its health and the
    /// rocket's damage like any other hit). A locked rocket's direct (or proximity) hit takes off ceil(MaxHealth / this).
    /// </summary>
    public int LockOnHits;

    /// <summary>Flares it carries: each one pulls one incoming lock-on rocket off (0 = none).</summary>
    public int Flares;

    /// <summary>Seconds between flares.</summary>
    public float FlareCooldown = 6f;

    /// <summary>A locked rocket this close (m) makes it pop a flare (if it has one left).</summary>
    public float FlareRange = 40f;
}

/// <summary>
/// The launchers' view of the kill streak machines (<see cref="StreakEntity"/>): which ones fly, how many lock-on hits
/// they take, whether they carry flares. The documented hook for new streaks (the gunship / chopper of the
/// more-killstreaks work): a new flying streak that lives in <see cref="Game.StreakEntities"/> calls
/// <see cref="Register"/> once (e.g. in a static constructor or where its definition is built), for example
/// <c>LauncherTargets.Register(StreakId.Gunship, new() { Airborne = true, LockOnHits = 3, Flares = 1 })</c>. Rockets
/// hit its <see cref="StreakEntity.Hitbox"/> and do their damage through <see cref="Game.TakeLauncherHit"/> (which also
/// applies its <see cref="StreakDef.ExplosiveDamageMult"/>). A kind nobody registered counts as airborne when it is more
/// than 8 m above the ground under it.
/// </summary>
public static class LauncherTargets
{
    private static readonly LauncherTargetProfile Ground = new();
    private static readonly LauncherTargetProfile UnknownAir = new() { Airborne = true };
    private static readonly Dictionary<StreakId, LauncherTargetProfile> Profiles = new()
    {
        [StreakId.SentryTurret] = new LauncherTargetProfile { Airborne = false },
        // 200 hit points: one lock-on hit (the anti-air launcher's rocket alone does 250+).
        [StreakId.AttackDrone] = new LauncherTargetProfile { Airborne = true, LockOnHits = 1 },
    };

    /// <summary>Registers (or replaces) how launchers treat a kind of streak machine.</summary>
    public static void Register(StreakId kind, LauncherTargetProfile profile)
    {
        lock (Profiles)
            Profiles[kind] = profile;
    }

    /// <summary>The profile for a kind (null when nobody registered it).</summary>
    public static LauncherTargetProfile? Find(StreakId kind)
    {
        lock (Profiles)
            return Profiles.GetValueOrDefault(kind);
    }

    /// <summary>The profile for an entity (registered, or the height rule).</summary>
    public static LauncherTargetProfile Get(StreakEntity e) => Find(e.Kind) ?? (IsHighUp(e.Pos) ? UnknownAir : Ground);

    /// <summary>Does it fly (lock-on and proximity fuses work on it)?</summary>
    public static bool IsAirborne(StreakEntity e) => Get(e).Airborne;

    private static bool IsHighUp(Vector3 p) => World.Query.Raycast(p, -Vector3.UnitY, 8f, QueryFilter.All) >= 7.99f;

    /// <summary>
    /// The damage a launcher hit does to a machine before its own <see cref="StreakDef.ExplosiveDamageMult"/>: the direct
    /// hit (rocket damage plus its direct bonus) or the blast share, times the launcher's vehicle multiplier.
    /// </summary>
    public static float RawHit(WeaponDef def, bool direct, float blast) =>
        ((direct ? def.Damage + def.LauncherDirect : 0f) + blast) * def.LauncherVehicleMult;

    /// <summary>Hits from full health to destroy a machine of <paramref name="kind"/> with this launcher (locked, direct).</summary>
    public static int HitsToKill(WeaponDef def, StreakId kind, bool locked)
    {
        var sd = KillStreaks.Get(kind);
        var hp = Math.Max(1, sd.Health);
        if (locked && Find(kind) is { LockOnHits: > 0 } p)
            return p.LockOnHits;
        var per = Game.StreakEntityDamage(kind, (int)MathF.Round(RawHit(def, true, 0f)), DamageKind.Explosive);
        return per <= 0 ? int.MaxValue : (int)Math.Ceiling(hp / (double)per);
    }
}
