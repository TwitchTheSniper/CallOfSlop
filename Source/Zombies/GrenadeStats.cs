using System.Numerics;

namespace CallOfSlop;

/// <summary>Which kind of thing a build makes: a gun (rooted on a grip), a melee weapon (a hilt) or a grenade (a casing).</summary>
public enum BuildKind : byte
{
    Gun,
    Melee,
    Grenade,
}

/// <summary>
/// A built grenade's numbers (the Zombies grenade slot; docs/zombies/WEAPON-BUILDER.md, "Modular grenades"). Made from
/// a casing (the root: handling, bounce, how much it holds), a fuse (what sets it off), a payload (what it does) and up
/// to three mods. The server throws and sets it off (<see cref="Game"/>, GameGrenades.cs); every client flies the same
/// arc from the same numbers.
/// </summary>
public sealed class GrenadeDef
{
    public required string Name;

    public FuseKind Fuse;

    /// <summary>Timer: the fuse. Proximity: the arming time after it lands. Impact and remote: the longest it waits.</summary>
    public float FuseSeconds;

    /// <summary>A timer fuse you can cook (hold the grenade key, let go to throw).</summary>
    public bool Cookable;

    /// <summary>Proximity fuses: how close a zombie sets it off (m).</summary>
    public float Trigger;

    public PayloadKind Payload;

    /// <summary>Damage at the centre against zombies (or a fire's / cloud's total per zombie).</summary>
    public float Damage;

    public float Radius;
    public float Knockback;

    /// <summary>Seconds a fire, gas cloud or lure lasts; a cryo payload's slow.</summary>
    public float Duration;

    /// <summary>Cryo and shock: stun seconds. Heal: health given.</summary>
    public float Strength;

    /// <summary>Launch speed (m/s) and how much speed a bounce keeps.</summary>
    public float ThrowSpeed, Bounce;

    public bool Sticky;

    /// <summary>Bomblets it splits into when it goes off (0: none).</summary>
    public int Cluster;

    /// <summary>Extra bangs after the first (an aftershock hops up and goes off again).</summary>
    public int Stages;

    /// <summary>Extra grenades every round's refill gives (on top of the round's count).</summary>
    public int Extra;

    public float Weight;
    public string[] Flags = [];

    /// <summary>The blast's colour (by payload).</summary>
    public Vector3 Tint;

    /// <summary>The model: the parts' boxes put together (drawn in flight and on the workbench).</summary>
    public required WeaponDef View;

    /// <summary>The grenade build as text (<see cref="WeaponBuild.ToText"/>), so a part hot reload can rebuild it in place.</summary>
    public string? BuildText;

    /// <summary>A bomblet's share of the payload's damage, and of its radius.</summary>
    public const float BombletDamage = 0.4f, BombletRadius = 0.55f;

    /// <summary>Each aftershock's share of the damage of the bang before it.</summary>
    public const float StageDamage = 0.6f;

    /// <summary>Roughly how far it flies on flat ground at the best angle (m): the standard throw's 34 m scaled by the speed squared.</summary>
    public float ThrowRange => 34f * (ThrowSpeed / Throwables.ThrowSpeed) * (ThrowSpeed / Throwables.ThrowSpeed);

    /// <summary>The fuse in words ("2.5 s timer (cook)", "impact", "proximity 2.5 m", "remote").</summary>
    public string FuseText => Fuse switch
    {
        FuseKind.Timer => $"{FuseSeconds:0.0} s timer{(Cookable ? " (cook)" : "")}",
        FuseKind.Impact => "impact",
        FuseKind.Proximity => $"proximity {Trigger:0.0} m",
        _ => "remote",
    };

    public static string PayloadName(PayloadKind k) => k switch
    {
        PayloadKind.Frag => "frag",
        PayloadKind.Incendiary => "incendiary",
        PayloadKind.Cryo => "cryo",
        PayloadKind.Shock => "shock",
        PayloadKind.Gas => "poison gas",
        PayloadKind.Lure => "lure",
        PayloadKind.Heal => "healing",
        _ => "rubber ducks",
    };

    /// <summary>The colour a payload's blast and cloud are drawn in.</summary>
    public static Vector3 PayloadTint(PayloadKind k) => k switch
    {
        PayloadKind.Frag => new(1f, 0.55f, 0.15f),
        PayloadKind.Incendiary => new(1f, 0.35f, 0.08f),
        PayloadKind.Cryo => new(0.55f, 0.85f, 1f),
        PayloadKind.Shock => new(0.45f, 0.75f, 1f),
        PayloadKind.Gas => new(0.45f, 0.85f, 0.25f),
        PayloadKind.Lure => new(1f, 0.4f, 0.75f),
        PayloadKind.Heal => new(0.4f, 1f, 0.55f),
        _ => new(1f, 0.9f, 0.2f),
    };

    /// <summary>Does the payload hurt zombies at all (a heal doesn't)?</summary>
    public bool HurtsZombies => Payload != PayloadKind.Heal;

    public override string ToString() =>
        $"{Name}: {FuseText}, {PayloadName(Payload)} {Damage:0} dmg r {Radius:0.0} m{(Duration > 0 ? $" {Duration:0.0} s" : "")}{(Strength > 0 ? $" str {Strength:0.0}" : "")}, "
        + $"throw {ThrowSpeed:0.0} m/s ({ThrowRange:0} m), bounce {Bounce:0.00}{(Sticky ? ", sticky" : "")}{(Cluster > 0 ? $", cluster {Cluster}" : "")}"
        + $"{(Stages > 0 ? $", +{Stages} stage(s)" : "")}{(Extra > 0 ? $", +{Extra} a round" : "")}, {Weight:0.00} kg";
}

/// <summary>
/// Turns a grenade build into a <see cref="GrenadeDef"/>. Deterministic, like the gun pipeline (BuildStats.cs) and the
/// melee one (MeleeStats.cs): the payload's numbers, scaled by the casing's and every mod's multipliers in tree order;
/// the throw from the casing and mods, slowed by weight over 0.6 kg; then clamps.
/// </summary>
public static class GrenadeStats
{
    /// <summary>The heaviest grenade anyone can throw (kg).</summary>
    public const float MaxWeight = 3f;

    public static GrenadeDef Compute(WeaponBuild b, WeaponDef view)
    {
        var root = b.Root!;
        var c = root.Part.Casing!;
        FuseInfo fuse = new();
        PayloadInfo payload = new();
        float damage = c.DamageMult, radius = c.RadiusMult, thrw = c.Throw, fuseMult = 1, duration = 1, bounce = c.Bounce;
        var sticky = c.Sticky;
        int cluster = 0, stages = 0, extra = c.Extra;
        foreach (var n in b.Nodes)
        {
            if (n.Part.Fuse is { } f)
                fuse = f;
            if (n.Part.Payload is { } p)
                payload = p;
            if (n.Part.GMod is not { } m)
                continue;
            damage *= m.DamageMult;
            radius *= m.RadiusMult;
            thrw *= m.ThrowMult;
            fuseMult *= m.FuseMult;
            duration *= m.DurationMult;
            bounce += m.Bounce;
            sticky |= m.Sticky;
            cluster += m.Cluster;
            stages += m.Stages;
            extra += m.Extra;
        }
        var weight = b.Weight;
        var speed = Throwables.ThrowSpeed * thrw * MathF.Max(0.55f, 1f - 0.18f * MathF.Max(0, weight - 0.6f));
        return new GrenadeDef
        {
            Name = WeaponBuild.CleanName(b.Name),
            Fuse = fuse.Kind,
            FuseSeconds = Math.Clamp(fuse.Seconds * fuseMult, 0.3f, 30f),
            Cookable = fuse.Cook && fuse.Kind == FuseKind.Timer,
            Trigger = Math.Clamp(fuse.Trigger, 0.5f, 8f),
            Payload = payload.Kind,
            Damage = MathF.Round(Math.Clamp(payload.Damage * damage, 0, 5000)),
            Radius = Math.Clamp(payload.Radius * radius, 1f, 16f),
            Knockback = Math.Clamp(payload.Knockback, 0, 30),
            Duration = Math.Clamp(payload.Duration * duration, 0, 25f),
            Strength = Math.Clamp(payload.Strength, 0, 500),
            ThrowSpeed = Math.Clamp(speed, 8f, 34f),
            Bounce = Math.Clamp(bounce, 0f, 0.9f),
            Sticky = sticky,
            Cluster = Math.Clamp(cluster, 0, MaxCluster),
            Stages = Math.Clamp(stages, 0, MaxStages),
            Extra = Math.Clamp(extra, 0, MaxExtra),
            Weight = weight,
            Flags = b.Nodes.SelectMany(n => n.Part.Flags).Distinct().OrderBy(f => f, StringComparer.Ordinal).ToArray(),
            Tint = GrenadeDef.PayloadTint(payload.Kind),
            View = view,
        };
    }

    /// <summary>Most bomblets, aftershocks and extra grenades a round one grenade can have.</summary>
    public const int MaxCluster = 12, MaxStages = 4, MaxExtra = 4;

    /// <summary>
    /// The rules that look at the grenade as a whole (BuildRules.LimitProblem): weight, one cluster mod at most, sticky
    /// and bouncy don't mix, a proximity fuse can't carry a lure. Null when none is broken.
    /// </summary>
    public static string? Problem(WeaponBuild b)
    {
        if (b.Weight > MaxWeight)
            return $"Too heavy: {b.Weight:0.0} kg; nobody can throw a grenade over {MaxWeight:0} kg.";
        PartDef? cluster = null, sticky = b.Root?.Part.Casing?.Sticky == true ? b.Root.Part : null, bouncy = null, fuse = null, payload = null;
        foreach (var n in b.Nodes)
        {
            var p = n.Part;
            if (p.Fuse != null)
                fuse = p;
            if (p.Payload != null)
                payload = p;
            if (p.GMod is not { } m)
                continue;
            if (m.Cluster > 0)
            {
                if (cluster != null)
                    return $"A grenade splits once: the {cluster.Name} already makes it a cluster, so the {p.Name} can't go on too.";
                cluster = p;
            }
            if (m.Sticky)
                sticky ??= p;
            if (m.Bounce > 0)
                bouncy ??= p;
        }
        if (sticky != null && bouncy != null)
            return $"The {bouncy.Name} makes it bounce, but the {sticky.Name} makes it stick: pick one.";
        if (fuse?.Fuse?.Kind == FuseKind.Proximity && payload?.Payload?.Kind == PayloadKind.Lure)
            return $"The {payload.Name} pulls zombies in: the {fuse.Name} would set it off before it got to play.";
        return null;
    }
}
