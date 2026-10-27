using System.Numerics;
using System.Runtime.CompilerServices;

namespace CallOfSlop;

/// <summary>A zombie's four limbs (the bit <c>1 &lt;&lt; limb</c> in <see cref="Zombie.Limbs"/> means that limb is gone). The head stays the kill shot.</summary>
public enum ZLimb : byte
{
    ArmR,
    ArmL,
    LegR,
    LegL,
}

/// <summary>Health left on each of a zombie's four limbs (by <see cref="ZLimb"/>; inline, no allocation).</summary>
[InlineArray(4)]
public struct ZLimbHealth
{
    private float _e;
}

/// <summary>Dismemberment (docs/zombies/ZOMBIES-MODE.md, "Dismemberment"): what each zombie has left.</summary>
public sealed partial class Zombie
{
    /// <summary>Limbs it has lost (bit <c>1 &lt;&lt; (int)ZLimb</c>; both sides: the server's, and on clients from <see cref="Msg.ZEnts"/>, protocol 65).</summary>
    public byte Limbs;

    /// <summary>Server: health left on each limb (a hit on a limb's part of the hit box takes it off this and the zombie's health).</summary>
    public ZLimbHealth LimbHp;

    /// <summary>The limbs this PC has already thrown gore for (both sides: the severing effect plays once).</summary>
    public byte LimbsShown;

    /// <summary>Both legs gone: it crawls (slower, lower, a crawler-sized hit box).</summary>
    public bool Crawler => (Limbs & Game.LegsMask) == Game.LegsMask;

    /// <summary>Arms it still has (0-2) and legs (0-2).</summary>
    public int ArmsLeft => 2 - ((Limbs & 1) + ((Limbs >> 1) & 1));

    public int LegsLeft => 2 - (((Limbs >> 2) & 1) + ((Limbs >> 3) & 1));
}

/// <summary>
/// Zombie dismemberment (the server's limb health and the states it leads to, both sides' gore and hit boxes). Each limb
/// has its own health (the rules' "limbs" section: a share of the zombie's, per speed class); a hit on a limb's part of
/// the hit box (<see cref="LimbAt"/>) takes it off that limb as well as the zombie's health, and a blast off every limb
/// in reach. A limb at 0 comes off while the zombie lives on: one arm swipes with the other (weaker, slower), no arms
/// bites, one leg limps (slower), no legs crawls (<see cref="Zombie.Crawler"/>: a prone-sized box, a low reach, slow
/// at windows). Four bits a zombie on the wire (<see cref="Msg.ZEnts"/>); every PC throws the limb once when its bit
/// appears (<see cref="ShowLimbs"/>: pooled gibs, a splat, a squelch). No allocation.
/// </summary>
public sealed partial class Game
{
    public const byte ArmsMask = 3, LegsMask = 12, AllLimbs = 15;

    /// <summary>Limbs severed on this PC (gore thrown) and limbs the server took off (self-test, logs).</summary>
    public long ZLimbsShown, ZLimbsSevered;

    /// <summary>Server: a new zombie's limb health (its speed class's share; a class that ignores dismemberment never loses one).</summary>
    private void InitLimbs(Zombie z)
    {
        var lt = ZRules.Limbs;
        var hp = !lt.Enabled || !lt.Severs[(int)z.Speed]
            ? float.PositiveInfinity
            : MathF.Max(lt.HealthMin, z.MaxHealth * lt.HealthFraction) * lt.HealthMult[(int)z.Speed];
        for (var i = 0; i < 4; i++)
            z.LimbHp[i] = hp;
        z.Limbs = 0;
        z.LimbsShown = 0;
    }

    /// <summary>The stance a zombie's hit box has: a crawler's is a prone body's (long and low along its facing).</summary>
    public static Stance ZombieStance(Zombie z) => z.Crawler ? Stance.Prone : Stance.Stand;

    /// <summary>A zombie's hit box with its feet at <paramref name="feet"/>.</summary>
    public static Box ZombieBox(Zombie z, Vector3 feet, float yaw) => World.Hitbox(feet, ZombieStance(z), yaw);

    /// <summary>Where bots aim on a zombie (feet up): the chest or the head, a crawler's low body.</summary>
    public static float ZombieAimHeight(Zombie z, bool head) => z.Crawler ? (head ? 0.35f : 0.28f) : head ? 1.6f : 1.25f;

    /// <summary>
    /// Which limb a hit at <paramref name="point"/> landed on (-1: the torso or head). Standing: below the hips a leg (by
    /// side), at the shoulders' height out to the side an arm. A crawler: an arm at the front of its body, by side. A limb
    /// already gone isn't there to hit (the hit counts on the body).
    /// </summary>
    public static int LimbAt(Zombie z, Vector3 feet, float yaw, Vector3 point)
    {
        var rel = point - feet;
        var right = Vector3.Dot(rel, World.FlatRight(yaw));
        var fwd = Vector3.Dot(rel, World.FlatForward(yaw));
        int limb;
        if (z.Crawler)
        {
            if (fwd < 0.1f || fwd >= 0.55f || MathF.Abs(right) < 0.1f)
                return -1;
            limb = right >= 0 ? (int)ZLimb.ArmR : (int)ZLimb.ArmL;
        }
        else if (rel.Y < LegTop)
            limb = right >= 0 ? (int)ZLimb.LegR : (int)ZLimb.LegL;
        else if (rel.Y >= ArmBottom && rel.Y < World.HeadStart && MathF.Abs(right) >= ArmSide)
            limb = right >= 0 ? (int)ZLimb.ArmR : (int)ZLimb.ArmL;
        else
            return -1;
        return (z.Limbs & (1 << limb)) != 0 ? -1 : limb;
    }

    /// <summary>Standing: legs below this height (m), arms from <see cref="ArmBottom"/> to the head and at least <see cref="ArmSide"/> out to the side.</summary>
    public const float LegTop = 0.88f, ArmBottom = 0.85f, ArmSide = 0.2f;

    /// <summary>Server: the limb the last ray that hit a zombie landed on (set by <see cref="ZombieRayHit"/>, like <see cref="_zLastRay"/>).</summary>
    private int _zLastLimb = -1;

    /// <summary>Server: <paramref name="damage"/> on limb <paramref name="limb"/> (the share the rules give limbs). The limb comes off later (<see cref="SeverLimbs"/>).</summary>
    private void LimbDamage(Zombie z, int limb, float damage)
    {
        if (limb is < 0 or > 3 || (z.Limbs & (1 << limb)) != 0 || !(damage > 0))
            return;
        z.LimbHp[limb] -= damage * ZRules.Limbs.DamageShare;
    }

    /// <summary>
    /// Server: a blast's share on each limb in reach (falling off with each limb's own distance from the blast): a grenade
    /// at its feet takes the legs, one at its shoulder an arm or two.
    /// </summary>
    private void BlastLimbs(Zombie z, Vector3 center, float radius, float maxDamage)
    {
        var share = ZRules.Limbs.BlastShare;
        if (!(share > 0) || z.Limbs == AllLimbs)
            return;
        var right = World.FlatRight(z.Yaw);
        for (var l = 0; l < 4; l++)
        {
            if ((z.Limbs & (1 << l)) != 0)
                continue;
            var side = l is (int)ZLimb.ArmR or (int)ZLimb.LegR ? 1f : -1f;
            var at = z.Position + right * (side * (l < 2 ? 0.35f : 0.13f)) + new Vector3(0, z.Crawler ? 0.3f : l < 2 ? 1.2f : 0.5f, 0);
            var d = Vector3.Distance(at, center);
            if (d >= radius)
                continue;
            z.LimbHp[l] -= maxDamage * share * MathF.Pow(1f - d / radius, 1.4f);
        }
    }

    /// <summary>
    /// Server, after the damage: every limb whose health ran out comes off (a kill too: the body falls without it). The
    /// state follows from <see cref="Zombie.Limbs"/>; this PC throws the gore (<see cref="ShowLimbs"/>), clients when the
    /// bits reach them. A small optional bonus per limb (the rules' "limbPoints", 0 by default). Returns the limbs taken off.
    /// </summary>
    private int SeverLimbs(Zombie z, Player? by, double now)
    {
        var lost = 0;
        for (var l = 0; l < 4; l++)
            if ((z.Limbs & (1 << l)) == 0 && z.LimbHp[l] <= 0)
                lost |= 1 << l;
        if (lost == 0)
            return 0;
        z.Limbs |= (byte)lost;
        var n = System.Numerics.BitOperations.PopCount((uint)lost);
        ZLimbsSevered += n;
        if (z.Crawler && (lost & LegsMask) != 0)
        {
            z.Sim.Velocity = Vector3.Zero; // drops onto its belly
            z.Hm.Node = -1;
        }
        if (by != null && ZRules.Limbs.LimbPoints > 0 && z.Alive)
            EarnPoints(by, ZRules.Limbs.LimbPoints * n, now);
        ShowLimbs(z, z.Position, z.Yaw, now);
        return lost;
    }

    /// <summary>
    /// Both sides: throws the limbs this PC hasn't shown yet (<see cref="Zombie.LimbsShown"/>), once each: the limb's
    /// pieces fly off (pooled gibs with a cap and a short life, <see cref="Gibs.SpawnLimb"/>), a splat where they land,
    /// and a squelch. <paramref name="fresh"/>: a zombie first heard of already without them (no gore, it was before your time).
    /// </summary>
    public void ShowLimbs(Zombie z, Vector3 pos, float yaw, double now, bool fresh = false)
    {
        var news = z.Limbs & ~z.LimbsShown & AllLimbs;
        z.LimbsShown = z.Limbs;
        if (news == 0 || fresh)
            return;
        var lt = ZRules.Limbs;
        for (var l = 0; l < 4; l++)
        {
            if ((news & (1 << l)) == 0)
                continue;
            ZLimbsShown++;
            Gibs.SpawnLimb(pos, yaw, l, z.Crawler, ZSkinColor, ZPantsColor, Gibs.SeedFor(z.Id * 4 + l, pos), lt.GoreSeconds, lt.GoreCap);
        }
        Audio.Effect3D(Gibs.Mode == GoreMode.Off ? Sfx.FleshHit3D : Sfx.GibSquelch, pos + new Vector3(0, z.Crawler ? 0.3f : 1f, 0), 0.85f, MixBus.Gore);
    }

    /// <summary>The colours a severed limb is thrown in (Program sets them to the drawn zombie's).</summary>
    public static Vector3 ZSkinColor = new(0.36f, 0.52f, 0.3f), ZPantsColor = new(0.18f, 0.2f, 0.27f);

    // ---- What a missing limb changes ----------------------------------------------------------------

    /// <summary>A zombie's speed multiplier for its legs: one gone limps, none crawls.</summary>
    private float LimbSpeedMult(Zombie z) => z.LegsLeft switch
    {
        2 => 1f,
        1 => ZRules.Limbs.OneLegSpeed,
        _ => ZRules.Limbs.CrawlSpeed,
    };

    /// <summary>The swipe's windup for this zombie (both sides: the animation times the strike by it): one arm is slower, a bite a little.</summary>
    public float ZombieWindupFor(Zombie z) => ZRules.ZombieWindup * (z.ArmsLeft switch
    {
        2 => 1f,
        1 => ZRules.Limbs.OneArmWindup,
        _ => ZRules.Limbs.BiteWindup,
    });

    /// <summary>Seconds between swipes for this zombie (one arm: slower).</summary>
    private float ZombieSwingFor(Zombie z) => ZRules.ZombieSwing * (z.ArmsLeft == 1 ? ZRules.Limbs.OneArmSwing : 1f);

    /// <summary>A swipe's (or bite's) damage for this zombie: one arm or none hurts less, a crawler a little less again.</summary>
    private int ZombieDamageFor(Zombie z)
    {
        var lt = ZRules.Limbs;
        var m = z.ArmsLeft switch { 2 => 1f, 1 => lt.OneArmDamage, _ => lt.BiteDamage };
        if (z.Crawler)
            m *= lt.CrawlDamage;
        return Math.Max(1, (int)MathF.Round(ZRules.ZombieDamage * m));
    }

    /// <summary>How far this zombie reaches: a bite is short, a crawler shorter.</summary>
    private float ZombieReachFor(Zombie z)
    {
        var lt = ZRules.Limbs;
        var r = ZRules.ZombieReach * (z.ArmsLeft == 0 ? lt.BiteReach : 1f);
        return z.Crawler ? r * lt.CrawlReach : r;
    }

    /// <summary>How far above or below its feet a target can be and still be hit (a crawler only reaches low: stand on something).</summary>
    private float ZombieReachHeight(Zombie z, bool landing) => z.Crawler ? ZRules.Limbs.CrawlAttackHeight : landing ? 1.6f : 1.4f;

    /// <summary>Seconds this zombie takes to climb in through a window (both sides: the climb's progress byte): a crawler is slow.</summary>
    public float ClimbSecondsFor(Zombie z) => ZRules.ClimbSeconds * (z.Crawler ? ZRules.Limbs.CrawlClimb : 1f);

    // ---- Self-test hooks -----------------------------------------------------------------------------

    /// <summary>
    /// Self-test: one shot at zombie <paramref name="z"/> from 3 m away towards the world point <paramref name="at"/>, as
    /// the server resolves a pellet (the ray test, the limb, the damage, the trigger pull's apply). Returns the hit flags.
    /// </summary>
    public byte SelfTestLimbShot(Player p, Zombie z, Vector3 at, float damage, double now, Vector3? from = null)
    {
        var o = from ?? at + World.FlatForward(z.Yaw) * 3f; // (in front of it, facing it)
        var d = Vector3.Normalize(at - o);
        var dist = 10f;
        var hit = ZombieRayHit(o, d, ref dist, ServerTick, out var head);
        if (hit == null)
            return 0;
        ZombieShotAdd(hit, damage, head);
        return ZombieShotApply(p, p.WeaponDef, now);
    }

    /// <summary>Self-test: one trigger pull of pellets at the world points <paramref name="at"/> on zombie <paramref name="z"/> (a shotgun: one damage event, several limbs).</summary>
    public byte SelfTestLimbPellets(Player p, Zombie z, ReadOnlySpan<Vector3> at, float damage, double now)
    {
        for (var i = 0; i < at.Length; i++)
        {
            var o = at[i] + World.FlatForward(z.Yaw) * 3f;
            var d = Vector3.Normalize(at[i] - o);
            var dist = 10f;
            if (ZombieRayHit(o, d, ref dist, ServerTick, out var head) is { } hit)
                ZombieShotAdd(hit, damage, head);
        }
        return ZombieShotApply(p, p.WeaponDef, now);
    }

    /// <summary>Self-test: a zombie's limb health from its (changed) health, all limbs back on.</summary>
    public void SelfTestInitLimbs(Zombie z) => InitLimbs(z);

    /// <summary>Self-test: how fast this zombie moves (m/s: its class, slows and lost legs).</summary>
    public float SelfTestSpeedOf(Zombie z) => SpeedOf(z);

    /// <summary>Self-test: the limb a ray from <paramref name="o"/> along <paramref name="d"/> hits on the first zombie in its way (-2: none).</summary>
    public int SelfTestLimbRay(Vector3 o, Vector3 d)
    {
        var dist = 20f;
        return ZombieRayHit(o, d, ref dist, ServerTick, out _) != null ? _zLastLimb : -2;
    }
}

/// <summary>Dismemberment's numbers (the rules file's "limbs" section: Parts/Tables/ZombiesRules.json; Source/Game/ZombieLimbs.cs).</summary>
public sealed class LimbTuning
{
    /// <summary>Off: zombies never lose a limb (hits count on the body as before).</summary>
    public bool Enabled = true;

    /// <summary>Each limb's health: this share of the zombie's (at least <see cref="HealthMin"/>), times its speed class's <see cref="HealthMult"/>.</summary>
    public float HealthFraction = 0.4f;

    public int HealthMin = 40;

    /// <summary>How much of a pellet's damage on a limb counts on the limb, and how much of a blast's (each limb by its own distance).</summary>
    public float DamageShare = 1f, BlastShare = 0.8f;

    /// <summary>One arm left: the swipe's damage, windup and time between swipes (multipliers).</summary>
    public float OneArmDamage = 0.7f, OneArmWindup = 1.3f, OneArmSwing = 1.3f;

    /// <summary>No arms: it bites (damage, reach and windup multipliers).</summary>
    public float BiteDamage = 0.5f, BiteReach = 0.7f, BiteWindup = 1.15f;

    /// <summary>One leg left: its speed multiplier (a hop and a limp).</summary>
    public float OneLegSpeed = 0.55f;

    /// <summary>No legs (a crawler): speed, reach and damage multipliers, the highest a target may stand above or below it (m), and its window climb's slowdown.</summary>
    public float CrawlSpeed = 0.32f, CrawlReach = 0.8f, CrawlDamage = 0.8f, CrawlAttackHeight = 0.7f, CrawlClimb = 2.2f;

    /// <summary>Points for each limb shot off a living zombie (0: none; an option for the developer).</summary>
    public int LimbPoints;

    /// <summary>A severed limb's pieces lie about this long (s), and at most this many limb pieces are out at once (the oldest go first).</summary>
    public float GoreSeconds = 6f;

    public int GoreCap = 48;

    /// <summary>Per speed class (walk, run, sprint): limb health multiplier, and whether it can lose limbs at all (brutes and specials, when there are some, get their own).</summary>
    public readonly float[] HealthMult = [1f, 1f, 1.2f];

    public readonly bool[] Severs = [true, true, true];
}
