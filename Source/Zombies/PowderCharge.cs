using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The powder charge of a Sandbox gun (docs/sandbox/CONSTRUCTS.md, "Powder charge"): one number, 25% to 300% of the standard load, that
/// sets how fast the round leaves. 100% is the gun as it always was. Everything else follows from the velocity, so a player reads one
/// slider: damage goes with its square root, recoil with it, and a barrel can only burn so much: past its <see cref="BurnLimit"/> the
/// extra powder adds speed at half the rate and opens the spread. A hand gun keeps it in its build text; a construct gun on its receiver.
/// </summary>
public static class PowderCharge
{
    public const int Min = 25, Max = 300, Default = 100;

    /// <summary>How much of a charge (percent) a barrel of this length (metres, breech to muzzle) burns fully.</summary>
    public static float BurnLimit(float barrelM) => Math.Clamp(100f + 300f * (barrelM - 0.2f), 120f, 300f);

    /// <summary>The muzzle velocity against the standard load's: the charge, then half rate past the barrel's limit.</summary>
    public static float Speed(int charge, float barrelM)
    {
        var limit = BurnLimit(barrelM);
        return (charge <= limit ? charge : limit + (charge - limit) * 0.5f) / 100f;
    }

    /// <summary>The spread against the standard load's: 1 up to the barrel's limit.</summary>
    public static float Spread(int charge, float barrelM) => 1f + 1.5f * MathF.Max(0f, charge - BurnLimit(barrelM)) / 100f;

    /// <summary>Does this receiver burn powder? Not a beam, a flame, a harpoon or a rocket.</summary>
    public static bool Takes(PartDef receiver) => receiver.Receiver is { } r && r.Bore is not ("energy" or "fuel" or "harpoon" or "launcher");

    /// <summary>A barrel's length, as its muzzle sits from its breech.</summary>
    public static float BarrelLength(PartDef barrel, int muzzle) => barrel.Muzzles.Length == 0 ? 0.3f : MathF.Abs(barrel.Muzzles[Math.Clamp(muzzle, 0, barrel.Muzzles.Length - 1)].Z);

    /// <summary>One barrel's numbers under a charge. The default touches nothing.</summary>
    public static void Apply(WeaponDef w, int charge, float barrelM)
    {
        if (charge == Default)
            return;
        var v = Speed(charge, barrelM);
        w.ChargeV = v;
        w.ChargeDamage = MathF.Sqrt(v);
        w.Damage = (int)MathF.Round(w.Damage * w.ChargeDamage);
        w.HipSpreadDeg *= Spread(charge, barrelM);
        w.RangeM *= v;
    }

    /// <summary>The speed (m/s) a hand-held built gun's round leaves at, or 0 for a shot that doesn't fall (a stock gun, a beam, a flame, a rope, a launcher).</summary>
    public static float HandSpeed(WeaponDef def)
    {
        if (def.BuildText == null || def.Reach > 0 || def.Harpoon || def.IsLauncher || def.PowderLess)
            return 0;
        return ConstructWeaponParts.Current.RoundSpeed * MathF.Max(0.25f, def.BulletSpeed) * def.ChargeV;
    }

    /// <summary>How far a round of this speed flies level before it has dropped a metre.</summary>
    public static float OneMetreDrop(float speed) => speed * MathF.Sqrt(2f / MathF.Max(1f, World.Gravity * World.GravityScale));

    /// <summary>How far a tracer of a round this fast bows above its straight line at the middle: gravity over the flight.</summary>
    public static float Bow(float speed, float distance)
    {
        if (speed <= 0)
            return 0;
        var t = distance / speed;
        return World.Gravity * World.GravityScale * t * t / 8f;
    }

    /// <summary>The player's line: velocity, flat range, recoil.</summary>
    public static string Line(float speed, float recoil) => $"{speed:0} m/s, 1 m drop at {OneMetreDrop(speed):0} m, recoil x{recoil:0.0}";
}
