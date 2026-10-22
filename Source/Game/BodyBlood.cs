using System.Numerics;

namespace CallOfSlop;

/// <summary>Where on the body a hit landed, for the blood marks (3 bits on the wire; 0 = an empty slot).</summary>
public enum BloodPart : byte
{
    None, Head, Front, Back, ArmL, ArmR, LegL, LegR,
}

/// <summary>Settings > Video "Blood overlay": the screen-edge blood for your own health and the blood marks on bodies.</summary>
public enum BloodMode : byte
{
    Full, Subtle, Off,
}

/// <summary>Settings > Video "Blood colour": bright red, or a dark (low-saturation, colour-blind friendlier) maroon.</summary>
public enum BloodColour : byte
{
    Red, Dark,
}

/// <summary>
/// The replicated blood state of one player: a 32-bit word (<see cref="Player.Blood"/>, in every snapshot fast entry since
/// protocol 32) the server writes on each hit and everyone draws the same way.
///   bits 0-23: 8 hit slots, 3 bits each (<see cref="BloodPart"/>; filled in order, 0 = empty, full slots keep the first 8 hits);
///   bits 24-27: generation (bumped each time the marks clear: back to full health or a new life; seeds the mark layout);
///   bits 28-31: the direction the most recent hit came from, in 16 sectors of the victim's facing (0 = in front, 4 = right).
/// How many marks show comes from health / <see cref="World.MaxHealth"/> (already in the snapshot), never from literal
/// hit points, so a different max health needs no change here. Pure functions, so the self-test checks them headlessly.
/// </summary>
public static class BloodMarks
{
    public const int Slots = 8;

    /// <summary>Most marks one body shows (the first <see cref="Slots"/> follow the recorded hits, the rest repeat them).</summary>
    public const int MaxMarks = 8;

    /// <summary>Health fractions below which each mark appears (mark i fades in over <see cref="Band"/> below Thresholds[i]).</summary>
    public static readonly float[] Thresholds = [0.85f, 0.72f, 0.62f, 0.52f, 0.42f, 0.32f, 0.22f, 0.12f];

    /// <summary>How far (as a health fraction) a mark takes to fade fully in or out as health falls or regenerates.</summary>
    public const float Band = 0.06f;

    public static BloodPart Part(uint blood, int slot) => (BloodPart)((blood >> (slot * 3)) & 7);

    /// <summary>How many hit slots are filled.</summary>
    public static int Count(uint blood)
    {
        var n = 0;
        while (n < Slots && Part(blood, n) != BloodPart.None)
            n++;
        return n;
    }

    public static int Gen(uint blood) => (int)((blood >> 24) & 15);

    public static int Sector(uint blood) => (int)(blood >> 28);

    /// <summary>The health tier 0-8: how many marks are at least partly visible at <paramref name="frac"/> (health / max).</summary>
    public static int Tier(float frac)
    {
        var n = 0;
        foreach (var t in Thresholds)
            if (frac < t)
                n++;
        return n;
    }

    /// <summary>Mark <paramref name="i"/>'s opacity at health fraction <paramref name="frac"/>: 0 above its threshold, 1 once health is <see cref="Band"/> below it.</summary>
    public static float MarkAlpha(int i, float frac) => Math.Clamp((Thresholds[i] - frac) / Band, 0f, 1f);

    /// <summary>One more hit: the part goes in the next free slot (none left: only the direction updates).</summary>
    public static uint AddHit(uint blood, BloodPart part, int sector)
    {
        var n = Count(blood);
        if (n < Slots && part != BloodPart.None)
            blood |= (uint)part << (n * 3);
        return (blood & 0x0FFFFFFFu) | ((uint)(sector & 15) << 28);
    }

    /// <summary>No marks, next generation (so the next set of hits is laid out differently).</summary>
    public static uint Cleared(uint blood) => (uint)((Gen(blood) + 1) & 15) << 24;

    /// <summary>The 16-sector direction of <paramref name="source"/> as seen by a body at <paramref name="feet"/> facing <paramref name="yaw"/> (0 = front, 4 = right, 8 = behind, 12 = left).</summary>
    public static int SectorOf(Vector3 feet, float yaw, Vector3 source)
    {
        var rel = source - feet;
        var lx = Vector3.Dot(rel, World.FlatRight(yaw));
        var lf = Vector3.Dot(rel, World.FlatForward(yaw));
        if (lx * lx + lf * lf < 1e-6f)
            return 0;
        var a = MathF.Atan2(lx, lf); // 0 = straight ahead, +pi/2 = right
        return (int)MathF.Round(a / MathF.Tau * 16f + 16f) & 15;
    }

    /// <summary>The body part a hit at <paramref name="point"/> landed on, for a body at <paramref name="feet"/> in <paramref name="stance"/> facing <paramref name="yaw"/>.</summary>
    public static BloodPart Classify(Vector3 feet, Stance stance, float yaw, Vector3 point)
    {
        if (World.IsHeadshot(feet, stance, yaw, point))
            return BloodPart.Head;
        var rel = point - feet;
        var lx = Vector3.Dot(rel, World.FlatRight(yaw));
        var lf = Vector3.Dot(rel, World.FlatForward(yaw));
        if (stance == Stance.Prone)
        {
            // Lying along the facing: the legs trail behind, the back faces up.
            if (lf < -0.25f)
                return lx < 0 ? BloodPart.LegL : BloodPart.LegR;
            return MathF.Abs(lx) > 0.22f ? (lx < 0 ? BloodPart.ArmL : BloodPart.ArmR) : BloodPart.Back;
        }
        var h = World.HeightOf(stance);
        if (rel.Y < h * 0.45f)
            return lx < 0 ? BloodPart.LegL : BloodPart.LegR;
        if (MathF.Abs(lx) > 0.27f)
            return lx < 0 ? BloodPart.ArmL : BloodPart.ArmR;
        return lf >= 0 ? BloodPart.Front : BloodPart.Back;
    }

    /// <summary>A part for a hit without a known point (blasts, swipes, melee): the side it came from; blasts low on the body.</summary>
    public static BloodPart PartFromSector(int sector, DamageKind kind, uint seed)
    {
        var s = sector & 15;
        if (kind == DamageKind.Explosive && (seed & 1) == 0)
            return s < 8 ? BloodPart.LegR : BloodPart.LegL;
        return s switch
        {
            >= 3 and <= 5 => BloodPart.ArmR,
            >= 11 and <= 13 => BloodPart.ArmL,
            >= 6 and <= 10 => BloodPart.Back,
            _ => BloodPart.Front,
        };
    }

    /// <summary>A sector for a hit with no known source (falls back to the part's side).</summary>
    public static int SectorOfPart(BloodPart p) => p switch
    {
        BloodPart.Back => 8, BloodPart.ArmR or BloodPart.LegR => 4, BloodPart.ArmL or BloodPart.LegL => 12, _ => 0,
    };

    /// <summary>A small deterministic hash (the same on every PC): mark layout and jitter.</summary>
    public static uint Hash(uint a, uint b, uint c)
    {
        var h = 2166136261u;
        h = (h ^ a) * 16777619u;
        h = (h ^ b) * 16777619u;
        h = (h ^ c) * 16777619u;
        h ^= h >> 15;
        h *= 0x2C1B3C6Du;
        h ^= h >> 12;
        return h;
    }

    /// <summary>
    /// The part mark <paramref name="i"/> sits on: the i-th recorded hit, or (more marks than hits: a big hit took a lot of
    /// health at once) one of the recorded hits again, or a torso / head spread when nothing was recorded (zombies,
    /// a client that joined late). Deterministic from the blood word and the owner's id.
    /// </summary>
    public static BloodPart MarkPart(uint blood, uint owner, int i)
    {
        var n = Count(blood);
        if (i < n)
            return Part(blood, i);
        var h = Hash(owner, (uint)Gen(blood), (uint)i + 101u);
        if (n > 0)
            return Part(blood, (int)(h % (uint)n));
        return (h % 10) switch
        {
            0 or 1 or 2 => BloodPart.Front,
            3 or 4 => BloodPart.Back,
            5 => BloodPart.Head,
            6 => BloodPart.ArmL,
            7 => BloodPart.ArmR,
            8 => BloodPart.LegL,
            _ => BloodPart.LegR,
        };
    }
}

/// <summary>
/// Blood on the server: each hit that takes health records the body part it landed on and the direction it came from in
/// <see cref="Player.Blood"/> (bullets: the exact pellet point against the lag-compensated pose; blasts, melee and zombie
/// swipes: the side they came from). Armour (Ballistic Vests, the Juggernaut suit) and Last Stand's shield soak hits before
/// this, so they leave no blood until health itself drops. The marks clear when health is full again (regeneration, Stim,
/// Bloodthirst) and with each new life; they stay while dead, so ragdolls keep them.
/// </summary>
public sealed partial class Game
{
    /// <summary>Set by the bullet path for the victim being damaged (the pellet's body part); None: work it out from the source.</summary>
    private BloodPart _bloodPending;

    /// <summary>Per shot: the body part each victim's first pellet hit (reused, no per-shot allocation).</summary>
    private readonly Dictionary<Player, BloodPart> _bloodShotPart = new();

    /// <summary>Server: <paramref name="victim"/> just lost health from <paramref name="attacker"/>.</summary>
    private void BloodOnHit(Player victim, Player attacker, DamageKind kind, Vector3? from)
    {
        var src = from ?? (attacker != victim ? attacker.Eye : (Vector3?)null);
        var part = _bloodPending;
        var sector = src is { } s ? BloodMarks.SectorOf(victim.Position, victim.Yaw, s) : -1;
        if (part == BloodPart.None)
            part = BloodMarks.PartFromSector(sector < 0 ? 0 : sector, kind, BloodMarks.Hash(victim.Id, (uint)victim.Health, ServerTick));
        if (sector < 0)
            sector = BloodMarks.SectorOfPart(part);
        victim.Blood = BloodMarks.AddHit(victim.Blood, part, sector);
    }

    /// <summary>Server: a zombie's swipe from <paramref name="from"/> took health from <paramref name="victim"/>.</summary>
    private void BloodOnSwipe(Player victim, Vector3 from)
    {
        var sector = BloodMarks.SectorOf(victim.Position, victim.Yaw, from);
        victim.Blood = BloodMarks.AddHit(victim.Blood, BloodMarks.PartFromSector(sector, DamageKind.Melee, 0), sector);
    }

    /// <summary>Server, every tick (with health regen): marks clear once a living player is back to full health.</summary>
    private static void BloodTick(Player p)
    {
        if (p.Alive && p.Health >= World.MaxHealth && (p.Blood & 0x00FFFFFFu) != 0)
            p.Blood = BloodMarks.Cleared(p.Blood);
    }

    // ---- Self-test hooks -----------------------------------------------------------------------

    /// <summary>Self-test: a bullet hit at <paramref name="point"/> from <paramref name="from"/> (the bullet path's blood handling).</summary>
    public bool SelfTestBloodShot(Player attacker, Player victim, int amount, Vector3 point, double now)
    {
        _bloodPending = BloodMarks.Classify(victim.Position, victim.Sim.Stance, victim.Yaw, point);
        var killed = DamagePlayer(attacker, victim, amount, World.IsHeadshot(victim.Position, victim.Sim.Stance, victim.Yaw, point), 0, now, DamageKind.Bullet);
        _bloodPending = BloodPart.None;
        return killed;
    }
}
