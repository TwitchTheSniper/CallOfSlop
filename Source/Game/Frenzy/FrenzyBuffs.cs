namespace CallOfSlop;

/// <summary>
/// Frenzy gameplay buffs: small, per weapon class, growing with the frenzy level (data in the "buffs" sections of
/// Weapons/Frenzy/classes.json, built-in defaults in <see cref="FrenzyClasses.DefaultBuffs"/>). A buff only works on
/// weapons of the player's frenzy class (the class of their most recent kill, <see cref="Player.FrenzyClass"/>), from
/// the buff's first level (3) up to level 6, and only while the host's "Frenzy buffs" setting is on
/// (<see cref="Player.FrenzyBuffsOn"/>, replicated with the class). Everything reads the replicated level, so the
/// server and the predicting client agree: draw time, ADS time, spread and recoil are predicted on the client; move
/// speed is checked by the server with the same multiplier; the Special heal happens on the server. Bots get them too
/// (their reload, spread and speed go through the same calls), and so does Zombies (with its half-weight kills).
/// </summary>
public static class FrenzyBuffs
{
    /// <summary>The value of <paramref name="stat"/> for <paramref name="p"/> using <paramref name="def"/> (the identity when no buff applies).</summary>
    public static float Value(Player? p, WeaponDef? def, FrenzyStat stat) =>
        p == null ? (stat == FrenzyStat.KillHeal ? 0f : 1f) : Value(p.FrenzyClass, p.FrenzyLevel, p.FrenzyBuffsOn, def, stat);

    /// <summary>The value of <paramref name="stat"/> for <paramref name="def"/> under a frenzy of <paramref name="cls"/> at <paramref name="level"/>.</summary>
    public static float Value(WeaponClass cls, int level, bool on, WeaponDef? def, FrenzyStat stat)
    {
        if (def == null || !on || level <= 0 || def.Class != cls)
            return stat == FrenzyStat.KillHeal ? 0f : 1f;
        return FrenzyClasses.Get(cls).Buffs.Value(stat, level);
    }

    /// <summary>The value of <paramref name="stat"/> for a weapon of <paramref name="cls"/> at <paramref name="level"/>, buffs on (tables, self-test).</summary>
    public static float At(WeaponClass cls, int level, FrenzyStat stat) => FrenzyClasses.Get(cls).Buffs.Value(stat, level);

    /// <summary>A weapon's mobility after the LMG "less move penalty" buff: the slowdown below 1 shrinks.</summary>
    public static float Mobility(Player? p, WeaponDef def) => MobilityWith(def, Value(p, def, FrenzyStat.MovePenalty));

    /// <summary>A weapon's mobility with a "movePenalty" multiplier <paramref name="m"/>.</summary>
    public static float MobilityWith(WeaponDef def, float m) => m >= 1f || def.Mobility >= 1f ? def.Mobility : 1f - (1f - def.Mobility) * m;

    /// <summary>
    /// The multiplier on the whole spread cone: the Shotgun "spread" buff, times the Sniper "aimedSpread" buff blended
    /// in by <paramref name="aimT"/> (0 hip .. 1 fully aimed).
    /// </summary>
    public static float ConeMult(Player? p, WeaponDef def, float aimT) =>
        Value(p, def, FrenzyStat.Spread) * float.Lerp(1f, Value(p, def, FrenzyStat.AimedSpread), Math.Clamp(aimT, 0f, 1f));

    /// <summary>How far the buffs can shrink a weapon's cone at most (the server's minimum-spread check allows for it).</summary>
    public static float MinSpreadMult(WeaponClass cls, int level, bool on, WeaponDef def) =>
        Value(cls, level, on, def, FrenzyStat.Spread) * Value(cls, level, on, def, FrenzyStat.AimedSpread);

    /// <summary>One line describing a class's buff at every level, e.g. "Quick draw: equipTime x1.00/.../0.70 (L0..L6)".</summary>
    public static string Describe(WeaponClass cls)
    {
        var b = FrenzyClasses.Get(cls).Buffs;
        var parts = new List<string>();
        foreach (var e in b.Effects)
        {
            var vals = new string[FrenzyMeterCore.MaxLevel + 1];
            for (var l = 0; l <= FrenzyMeterCore.MaxLevel; l++)
                vals[l] = e.At(l, b.MinLevel).ToString(e.Stat == FrenzyStat.KillHeal ? "0" : "0.00", System.Globalization.CultureInfo.InvariantCulture);
            parts.Add($"{e.Stat} {string.Join("/", vals)}");
        }
        return $"{cls}: {b.Name} ({string.Join("; ", parts)} at L0..L{FrenzyMeterCore.MaxLevel})";
    }
}
