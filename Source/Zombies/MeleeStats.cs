namespace CallOfSlop;

/// <summary>
/// A built melee weapon's numbers (the Zombies melee slot; see docs/zombies/WEAPON-BUILDER.md, "Melee slot"). Made from
/// a hilt (the root: base damage, swing time, reach, cleave, stun), a head and up to two modifiers.
/// </summary>
public sealed class MeleeDef
{
    public required string Name;

    /// <summary>Damage per target hit.</summary>
    public float Damage;

    /// <summary>Seconds between swings.</summary>
    public float Swing;

    /// <summary>Reach from the eyes (m).</summary>
    public float Reach;

    /// <summary>How many targets one swing hits (nearest first).</summary>
    public int Cleave;

    /// <summary>Seconds a hit target is stunned (bots and, later, zombies stop in their tracks).</summary>
    public float Stun;

    public float Weight;
    public bool TwoHanded;
    public string[] Flags = [];

    /// <summary>The stand-in drawn in first person during a swing (the built model, swung with the one-handed melee arc).</summary>
    public required WeaponDef View;

    /// <summary>The melee build as text (<see cref="WeaponBuild.ToText"/>), so a part hot reload can rebuild it in place.</summary>
    public string? BuildText;

    /// <summary>Length of the swing animation: a bit over half the swing time, within the existing animation's range.</summary>
    public float SwingAnim => Math.Clamp(Swing * 0.55f, 0.25f, 0.6f);

    /// <summary>Half-angle cosine of the swing arc: wider when it cleaves (a sweep), tighter for a single target.</summary>
    public float ConeCos => Cleave > 1 ? 0.6f : Game.MeleeConeCos;

    /// <summary>Damage per second on one target.</summary>
    public float Dps => Damage / MathF.Max(0.05f, Swing);

    public override string ToString() =>
        $"{Name}: {Damage:0} dmg, {Swing:0.00} s swing, {Reach:0.0} m reach, cleave {Cleave}, stun {Stun:0.0} s, {Weight:0.0} kg";
}

/// <summary>
/// Turns a melee build into a <see cref="MeleeDef"/>. Deterministic: the hilt's numbers, then every head and modifier in
/// tree order (damage added then multiplied, swing time multiplied, reach / cleave / stun added), then weight (every kg
/// over 2 adds 0.03 s to the swing), then clamps.
/// </summary>
public static class MeleeStats
{
    public static MeleeDef Compute(WeaponBuild b, WeaponDef view)
    {
        var root = b.Root!;
        var h = root.Part.Hilt!;
        float add = 0, mult = 1, swing = h.Swing, reach = h.Reach, stun = h.Stun;
        var cleave = h.Cleave;
        foreach (var n in b.Nodes)
        {
            if (n.Part.Melee is not { } m)
                continue;
            add += m.Damage;
            mult *= m.DamageMult;
            swing *= m.SwingMult;
            reach += m.Reach;
            cleave += m.Cleave;
            stun += m.Stun;
        }
        var weight = b.Weight;
        swing += 0.03f * MathF.Max(0, weight - 2);
        return new MeleeDef
        {
            Name = WeaponBuild.CleanName(b.Name),
            Damage = MathF.Round(Math.Clamp((h.Damage + add) * mult, 1, 400)),
            Swing = Math.Clamp(swing, 0.25f, 3f),
            Reach = Math.Clamp(reach, 1f, 3.5f),
            Cleave = Math.Clamp(cleave, 1, 8),
            Stun = Math.Clamp(stun, 0f, 3f),
            Weight = weight,
            TwoHanded = h.Hands == "two",
            Flags = b.Nodes.SelectMany(n => n.Part.Flags).Distinct().OrderBy(f => f, StringComparer.Ordinal).ToArray(),
            View = view,
        };
    }
}
