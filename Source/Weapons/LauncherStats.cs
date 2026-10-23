using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>What a launcher fires (the "projectile" key of a weapon file's "launcher" block).</summary>
public enum LauncherProjectile : byte
{
    /// <summary>A rocket: a motor pushes it up to its top speed, it flies nearly flat and goes off on contact.</summary>
    Rocket,

    /// <summary>A grenade: lobbed on an arc, goes off on contact (or after its bounces, or at its fuse).</summary>
    Grenade,

    /// <summary>A sticky charge: sticks to whatever it hits (people too) and waits for its owner to set it off.</summary>
    Sticky,
}

/// <summary>How a launcher's projectile is steered after it leaves the tube (the "guidance" key).</summary>
public enum LauncherGuidance : byte
{
    /// <summary>Dumb-fire: it flies where it was pointed.</summary>
    None,

    /// <summary>Lock-on: aim at an airborne kill streak until the lock tone goes solid, then fire; it homes on that target.</summary>
    LockOn,

    /// <summary>Wire / laser guided: it turns towards wherever its owner is aiming, for as long as they keep aiming.</summary>
    Wire,
}

/// <summary>A key of a weapon file's "launcher" block, with its allowed range (like <see cref="RecoilKeyAttribute"/>).</summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class LauncherKeyAttribute(string key, float min, float max) : Attribute
{
    public string Key { get; } = key;
    public float Min { get; } = min;
    public float Max { get; } = max;
}

/// <summary>
/// Launcher stats (the "launcher" block of a weapon file of class Launcher; see Weapons/README.md, "Launchers"). They're
/// ordinary weapon fields, so attachments can change them by their field names ("launcherLockTime", "launcherReserve"...),
/// but they're written to and read from their own block, and only launchers have one.
/// </summary>
public sealed partial class WeaponDef
{
    [LauncherKey("projectile", 0, 0)] public LauncherProjectile LauncherProjectile = LauncherProjectile.Rocket;
    [LauncherKey("guidance", 0, 0)] public LauncherGuidance LauncherGuidance = LauncherGuidance.None;

    /// <summary>Speed leaving the tube (m/s).</summary>
    [LauncherKey("speed", 1, 300)] public float LauncherSpeed = 30f;

    /// <summary>Rockets: the motor pushes it up to this speed (m/s).</summary>
    [LauncherKey("maxSpeed", 1, 300)] public float LauncherMaxSpeed = 60f;

    /// <summary>Rockets: motor push in m/s per second (0 = no motor: it keeps its launch speed).</summary>
    [LauncherKey("thrust", 0, 1000)] public float LauncherThrust = 60f;

    /// <summary>How hard gravity pulls it, as a fraction of the world's gravity (rockets 0.05-0.2, grenades about 0.7-1).</summary>
    [LauncherKey("gravity", 0, 3)] public float LauncherGravity = 0.1f;

    /// <summary>Air drag: the speed falls by this fraction per second (before thrust).</summary>
    [LauncherKey("drag", 0, 5)] public float LauncherDrag = 0f;

    /// <summary>Blast radius (m). The weapon's "damage" is the damage at the centre.</summary>
    [LauncherKey("radius", 0.5f, 20)] public float LauncherRadius = 5f;

    /// <summary>Blast falloff: damage x (1 - distance / radius) to this power (1 = straight line, higher = drops faster).</summary>
    [LauncherKey("falloff", 0.3f, 4)] public float LauncherFalloff = 1.2f;

    /// <summary>Extra damage to a player hit straight on (on top of the blast).</summary>
    [LauncherKey("direct", 0, 1000)] public float LauncherDirect = 50f;

    /// <summary>Multiplier on everything a hit does to a kill streak machine (turret, drone, gunship...): launchers are the anti-streak tool.</summary>
    [LauncherKey("vehicleMult", 0, 10)] public float LauncherVehicleMult = 1f;

    /// <summary>Fraction of the blast its owner takes when caught in it.</summary>
    [LauncherKey("selfDamage", 0, 1)] public float LauncherSelfDamage = 0.6f;

    /// <summary>Metres of flight before it arms. An unarmed hit is a dud: only the direct hit's damage, no blast.</summary>
    [LauncherKey("armDistance", 0, 60)] public float LauncherArmDistance = 6f;

    /// <summary>Proximity fuse (m): an armed projectile goes off this close to an enemy airborne kill streak (0 = none).</summary>
    [LauncherKey("proximity", 0, 12)] public float LauncherProximity = 0f;

    /// <summary>Seconds of flight before it goes off on its own (a grenade's fuse; a rocket self-destructs; stickies wait this long at most).</summary>
    [LauncherKey("fuse", 0.1f, 40)] public float LauncherFuse = 8f;

    /// <summary>Grenades: bounces before contact sets it off (0 = goes off on the first contact).</summary>
    [LauncherKey("bounces", 0, 10)] public int LauncherBounces;

    /// <summary>Spare rounds carried (the magazine is extra). Launchers don't have endless reloads.</summary>
    [LauncherKey("reserve", 0, 30)] public int LauncherReserve = 3;

    /// <summary>Lock-on: seconds of keeping the target in the lock cone (while aiming) to lock.</summary>
    [LauncherKey("lockTime", 0.1f, 10)] public float LauncherLockTime = 1.2f;

    /// <summary>Lock-on: furthest target (m).</summary>
    [LauncherKey("lockRange", 5, 600)] public float LauncherLockRange = 160f;

    /// <summary>Lock-on: half-angle of the lock cone (degrees).</summary>
    [LauncherKey("lockCone", 1, 45)] public float LauncherLockCone = 9f;

    /// <summary>Guided: how fast it can turn (degrees per second).</summary>
    [LauncherKey("turnRate", 0, 720)] public float LauncherTurnRate = 90f;

    /// <summary>Cluster: bomblets thrown out by the blast, each with a smaller blast of its own (0 = none).</summary>
    [LauncherKey("cluster", 0, 12)] public int LauncherCluster;

    /// <summary>Thermobaric: fraction of the blast that still gets through walls (0 = walls stop it, like a grenade).</summary>
    [LauncherKey("wallPierce", 0, 1)] public float LauncherWallPierce;

    /// <summary>Blast push on the living (m/s at the centre, falling off to a small share at the edge; Knockback.cs). 0 = from the damage (5 + damage / 25, at most 12).</summary>
    [LauncherKey("knockback", 0, 30)] public float LauncherKnockback;

    /// <summary>True for launchers (they fire projectiles from Source/Game/Launchers instead of hitscan bullets).</summary>
    public bool IsLauncher => Class == WeaponClass.Launcher;
}

/// <summary>Reading, writing and checking a weapon file's "launcher" block (see Weapons/README.md, "Launchers").</summary>
public static class LauncherStats
{
    /// <summary>The launcher fields, in declaration order.</summary>
    public static readonly FieldInfo[] Fields = typeof(WeaponDef).GetFields(BindingFlags.Public | BindingFlags.Instance)
        .Where(f => f.IsDefined(typeof(LauncherKeyAttribute)))
        .OrderBy(f => f.MetadataToken)
        .ToArray();

    private static readonly Dictionary<string, FieldInfo> ByKey = Fields.ToDictionary(Key, StringComparer.OrdinalIgnoreCase);

    /// <summary>The key a launcher field has in the "launcher" block ("LauncherLockTime" -> "lockTime").</summary>
    public static string Key(FieldInfo f) => f.GetCustomAttribute<LauncherKeyAttribute>()!.Key;

    /// <summary>Reads a "launcher" block over the defaults. Unknown keys are errors, like everywhere else in weapon files.</summary>
    public static void ReadBlock(WeaponDef w, JsonElement e, string where)
    {
        if (e.ValueKind != JsonValueKind.Object)
            throw new WeaponFileException($"{where}: expected {{ \"projectile\": \"Rocket\", \"speed\": ..., ... }} (see Weapons/README.md, \"Launchers\")");
        var given = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in e.EnumerateObject())
        {
            var at = $"{where}.{p.Name}";
            if (!given.Add(p.Name))
                throw new WeaponFileException($"{at}: given twice");
            if (!ByKey.TryGetValue(p.Name, out var f))
                throw new WeaponFileException($"{where}: unknown launcher field \"{p.Name}\". Known: {string.Join(", ", Fields.Select(Key))}");
            f.SetValue(w, WeaponFile.FromJson(p.Value, f.FieldType, at));
        }
    }

    /// <summary>The "launcher" block as JSON (every field), indented for a weapon file.</summary>
    public static string BlockJson(WeaponDef w)
    {
        var sb = new StringBuilder("{\n");
        for (var i = 0; i < Fields.Length; i++)
            sb.Append("    \"").Append(Key(Fields[i])).Append("\": ").Append(WeaponFile.Fmt(Fields[i].GetValue(w))).Append(i < Fields.Length - 1 ? ",\n" : "\n");
        return sb.Append("  }").ToString();
    }

    /// <summary>Checks every launcher number is in range. Throws <see cref="WeaponFileException"/> if not.</summary>
    public static void Validate(WeaponDef w, string source)
    {
        foreach (var f in Fields)
        {
            if (f.FieldType.IsEnum)
                continue;
            var a = f.GetCustomAttribute<LauncherKeyAttribute>()!;
            var v = Convert.ToSingle(f.GetValue(w), CultureInfo.InvariantCulture);
            if (!(v >= a.Min && v <= a.Max))
                throw new WeaponFileException($"{source}: \"launcher\".\"{a.Key}\" is {WeaponFile.Num(v)}, but must be between {WeaponFile.Num(a.Min)} and {WeaponFile.Num(a.Max)}");
        }
        if (w.LauncherMaxSpeed < w.LauncherSpeed && w.LauncherThrust > 0)
            throw new WeaponFileException($"{source}: \"launcher\".\"maxSpeed\" ({WeaponFile.Num(w.LauncherMaxSpeed)}) is below \"speed\" ({WeaponFile.Num(w.LauncherSpeed)}) while it has \"thrust\"");
    }

    /// <summary>
    /// Kitted copies (Attachments.Apply): whatever is stacked, a launcher keeps sane numbers: at least 70% of its lock
    /// time, speeds within reach of the base, and no more than double the reserve.
    /// </summary>
    public static void ClampKit(WeaponDef baseDef, WeaponDef d)
    {
        if (!baseDef.IsLauncher)
            return;
        d.LauncherLockTime = Math.Max(d.LauncherLockTime, baseDef.LauncherLockTime * 0.7f);
        d.LauncherSpeed = Math.Clamp(d.LauncherSpeed, baseDef.LauncherSpeed * 0.7f, baseDef.LauncherSpeed * 1.4f);
        d.LauncherMaxSpeed = Math.Clamp(d.LauncherMaxSpeed, Math.Max(d.LauncherSpeed, baseDef.LauncherMaxSpeed * 0.7f), baseDef.LauncherMaxSpeed * 1.4f);
        d.LauncherReserve = Math.Clamp(d.LauncherReserve, 0, Math.Max(baseDef.LauncherReserve * 2, baseDef.LauncherReserve + 1));
        d.LauncherTurnRate = Math.Clamp(d.LauncherTurnRate, 0, baseDef.LauncherTurnRate * 1.5f);
    }
}
