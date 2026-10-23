using System.Numerics;

namespace CallOfSlop;

/// <summary>How a tactical item is used from the Equipment slot.</summary>
public enum TacticalUse : byte
{
    /// <summary>Not one of the tactical items in this file (frags, smoke, the Recon Pulse).</summary>
    None,

    /// <summary>Thrown like a grenade (Flashbang, Stun, Decoy, Motion Sensor).</summary>
    Thrown,

    /// <summary>Put down in front of you or at your feet (Trophy System, Deployable Cover, Tactical Insertion).</summary>
    Placed,

    /// <summary>Used on yourself (Stim Shot).</summary>
    Self,
}

/// <summary>What a <see cref="Msg.TacEvent"/> announces (one byte on the wire; new kinds go at the end).</summary>
public enum TacEventKind : byte
{
    None,
    Afflict, // a: victim, b: Flash or Stun, c: strength (0..65535), pos: where it went off
    Intercept, // a: trophy id, c: projectile id, pos: where the projectile was shot down
    Destroyed, // a: device id, b: kind, c: owner | by << 8 (by 255: ran out / used up), pos: where it was
    DecoyBurst, // a: decoy id, b: shots, c: weapon | interval (ms / 4) << 8, pos: the decoy
    Ping, // a: sensor owner, b: pinged player, c: sensor id, pos: the pinged player's feet
    Stim, // a: the player who used it
    Placed, // a: device id, b: kind, c: owner, pos: where it went down
    Inserted, // a: the player who spawned on their Tactical Insertion, pos: the spot
}

/// <summary>
/// A tactical device in the world: a placed Trophy System, Deployable Cover or Tactical Insertion, or a landed Decoy or
/// Motion Sensor. The server owns them; clients get a compact update (<see cref="NetBytes"/> each) with the snapshots
/// while any exist, plus reliable <see cref="Msg.TacEvent"/>s for the moments that matter (intercepts, pings, breaks).
/// </summary>
public sealed class TacDevice
{
    public byte Id;
    public ThrowableKind Kind;
    public byte Owner;
    public Vector3 Pos; // feet / the point it sits on
    public float Yaw; // which way it faces (Deployable Cover: the wall's normal)
    public int Health;
    public byte Charges; // Trophy System: shots left
    public double Born, Until;

    // ---- Server ----
    public double NextAction; // trophy re-arm, sensor scan, decoy burst
    public uint Seed;

    // ---- Client (and host) visuals ----
    public double LastUpdate;
    public int BurstLeft; // decoy: fake shots still to play
    public double NextShot;
    public float ShotInterval;
    public byte BurstWeapon;
    public double LastHit = -10; // flash the model when it takes a hit or fires

    public int MaxHealth => TacticalGear.MaxHealth(Kind);

    /// <summary>What bullets and blasts hit (Deployable Cover: see <see cref="TacticalGear.CoverRay"/>, it's turned).</summary>
    public Box Hitbox => TacticalGear.Hitbox(Kind, Pos);

    /// <summary>Bytes per device in a <see cref="Msg.TacState"/> update.</summary>
    public const int NetBytes = 19;
}

/// <summary>
/// The tactical equipment: tuning, the pure formulas the server, the HUD and the self-test share (how hard a flash
/// blinds by angle and distance, how much a stun slows), unlock levels and the definitions that go into
/// <see cref="Throwables.All"/>. Server and client logic live in GameTactical.cs, visuals in TacticalRender.cs and
/// TacticalHud.cs, sounds in SynthTactical.cs.
/// </summary>
public static class TacticalGear
{
    // ---- Flashbang ------------------------------------------------------------------------------------
    public const float FlashRadius = 14f;
    /// <summary>Longest full white-out (s), at strength 1. Control is never taken away: you can always move and shoot.</summary>
    public const float FlashHoldMax = 1.6f;
    /// <summary>The fade back to normal after the hold (s): <see cref="FlashFadeBase"/> + this x strength.</summary>
    public const float FlashFadeMax = 2.2f, FlashFadeBase = 0.4f;
    /// <summary>Strength multiplier when the flash is fully behind you (facing it straight on is 1).</summary>
    public const float FlashBehind = 0.2f;
    /// <summary>Weaker flashes than this do nothing.</summary>
    public const float FlashMinStrength = 0.08f;
    /// <summary>Your own flash or stun affects you this much.</summary>
    public const float SelfMult = 0.5f;
    /// <summary>Flak Jacket: flashes and stuns are this strong (and so this much shorter).</summary>
    public const float FlakTacticalMult = 0.5f;
    /// <summary>Bots lose their target while the white-out is at least this opaque.</summary>
    public const float BotBlindAlpha = 0.5f;

    // ---- Stun Grenade ---------------------------------------------------------------------------------
    public const float StunRadius = 8f;
    public const float StunBaseTime = 1f, StunExtraTime = 3f; // duration = base + extra x strength (at most 4 s)
    public const float StunMoveSlow = 0.5f, StunLookSlow = 0.6f, StunAdsSlow = 0.6f; // at strength 1: 50% speed, 40% turn, 40% aim speed

    // ---- Decoy ----------------------------------------------------------------------------------------
    public const double DecoyLife = 12;
    public const float DecoyHearing = 45f; // enemy bots this close go and check the "gunfire"
    public const float DecoyTagRange = 40f; // enemies see its fake name tag this close
    public const int DecoyHealth = 10;

    // ---- Motion Sensor --------------------------------------------------------------------------------
    public const double SensorLife = 25;
    public const float SensorRadius = 16f;
    public const double SensorScan = 0.5;
    /// <summary>Only enemies moving faster than this (flat m/s) are pinged: walking (6) and sprinting are, crouch-walking (3) and prone aren't.</summary>
    public const float SensorMinSpeed = 3.5f;
    public const double PingShow = 1.2; // how long a ping stays on the HUD
    public const int SensorHealth = 15;

    // ---- Trophy System --------------------------------------------------------------------------------
    public const double TrophyLife = 60;
    public const float TrophyRadius = 7f;
    public const byte TrophyCharges = 3;
    public const double TrophyRearm = 0.3;
    public const int TrophyHealth = 60;

    // ---- Deployable Cover -----------------------------------------------------------------------------
    public const double CoverLife = 40;
    public const int CoverHealth = 350;
    /// <summary>Half extents in the wall's own frame: 2 m wide, 1.2 m tall (crouch behind it, stand to shoot over it), 16 cm thick.</summary>
    public static readonly Vector3 CoverHalf = new(1.0f, 0.6f, 0.08f);
    public const float CoverAhead = 1.4f;
    public const float CoverExplosiveMult = 2f; // explosives tear it down twice as fast

    // ---- Tactical Insertion ---------------------------------------------------------------------------
    public const double InsertionLife = 300;
    public const int InsertionHealth = 40;

    // ---- Stim Shot ------------------------------------------------------------------------------------
    public const int StimHeal = 48; // 40% of World.MaxHealth (was 40 of 100)
    public const double StimTime = 4;
    public const float StimRegen = 18f; // HP per second during StimTime, no waiting (was 15 at 100 HP): 48 + 72 = a full heal

    /// <summary>The tactical items as they go into <see cref="Throwables.All"/> (in enum order, from Flash).</summary>
    public static readonly ThrowableDef[] Defs =
    [
        new() { Kind = ThrowableKind.Flash, Name = "Flashbang", Fuse = 1.4f, Radius = FlashRadius, PerLife = 2, Color = new(0.92f, 0.92f, 0.86f),
            Blurb = "Goes off 1.4 s after the throw. Anyone facing it within 14 m is blinded white for up to 1.6 s and deafened, then sight fades back over about 2.5 s. Looking away cuts it to a fifth." },
        new() { Kind = ThrowableKind.Stun, Name = "Stun Grenade", Fuse = 4f, Radius = StunRadius, PerLife = 2, Color = new(0.95f, 0.75f, 0.2f),
            Blurb = "Goes off on contact. Everyone within 8 m with a clear line moves at down to half speed, turns and aims slower for up to 4 s." },
        new() { Kind = ThrowableKind.Decoy, Name = "Decoy Grenade", Fuse = 6f, Radius = DecoyHearing, PerLife = 1, Color = new(0.55f, 0.5f, 0.32f),
            Blurb = "Lands and fires fake bursts of your gun for 12 s, with a fake name tag of yours for enemies to chase. Bots go and check it out. One shot breaks it." },
        new() { Kind = ThrowableKind.MotionSensor, Name = "Motion Sensor", Fuse = 6f, Radius = SensorRadius, PerLife = 1, Color = new(0.3f, 0.9f, 0.4f),
            Blurb = "Sticks where it lands. For 25 s it pings every enemy moving within 16 m on your team's HUD. Crouch-walking, going prone and Ghost stay off it, and it beeps so it can be found and shot." },
        new() { Kind = ThrowableKind.Trophy, Name = "Trophy System", Radius = TrophyRadius, PerLife = 1, Color = new(0.38f, 0.5f, 0.32f),
            Blurb = "Put it down in front of you. It shoots down up to 3 enemy grenades or thrown gadgets within 7 m (every size, Demolitions frags too), then breaks. Shoot it or use a streak to clear it first." },
        new() { Kind = ThrowableKind.Cover, Name = "Deployable Cover", Radius = 0f, PerLife = 1, Color = new(0.3f, 0.62f, 1f),
            Blurb = "An energy wall 2 m wide and 1.2 m tall in front of you for 40 s. It stops bullets and grenade blasts from both sides until it takes 350 damage. People walk straight through it." },
        new() { Kind = ThrowableKind.Insertion, Name = "Tactical Insertion", Radius = 0f, PerLife = 1, Color = new(1f, 0.38f, 0.22f),
            Blurb = "Drop a flare at your feet: your next spawn is right there. Its glow gives it away, and enemies can shoot it out before you get back." },
        new() { Kind = ThrowableKind.Stim, Name = "Stim Shot", Radius = 0f, PerLife = 1, Color = new(0.92f, 0.32f, 0.62f),
            Blurb = "Inject it at once: 40 health back, then 15 health a second for 4 s with no wait. It also shakes off a stun." },
    ];

    /// <summary>Unlock levels of the tactical items (the Recon Pulse stays in the main order), cheapest first.</summary>
    public static readonly (ThrowableKind Kind, int Level)[] UnlockLevels =
    [
        (ThrowableKind.Stim, 5), (ThrowableKind.Flash, 10), (ThrowableKind.Decoy, 13), (ThrowableKind.MotionSensor, 16),
        (ThrowableKind.Stun, 19), (ThrowableKind.Trophy, 24), (ThrowableKind.Cover, 28), (ThrowableKind.Insertion, 32),
    ];

    /// <summary>Is this one of the tactical items in this file?</summary>
    public static bool Is(ThrowableKind k) => k >= ThrowableKind.Flash && k <= ThrowableKind.Stim;

    public static TacticalUse UseOf(ThrowableKind k) => k switch
    {
        ThrowableKind.Flash or ThrowableKind.Stun or ThrowableKind.Decoy or ThrowableKind.MotionSensor => TacticalUse.Thrown,
        ThrowableKind.Trophy or ThrowableKind.Cover or ThrowableKind.Insertion => TacticalUse.Placed,
        ThrowableKind.Stim => TacticalUse.Self,
        _ => TacticalUse.None,
    };

    /// <summary>Thrown items that stop where they first touch something (they become devices there).</summary>
    public static bool Sticks(ThrowableKind k) => k is ThrowableKind.Recon or ThrowableKind.MotionSensor or ThrowableKind.Decoy;

    /// <summary>Does this kind live on as a <see cref="TacDevice"/>?</summary>
    public static bool IsDevice(ThrowableKind k) => k is ThrowableKind.Decoy or ThrowableKind.MotionSensor or ThrowableKind.Trophy or ThrowableKind.Cover or ThrowableKind.Insertion;

    public static int MaxHealth(ThrowableKind k) => k switch
    {
        ThrowableKind.Decoy => DecoyHealth,
        ThrowableKind.MotionSensor => SensorHealth,
        ThrowableKind.Trophy => TrophyHealth,
        ThrowableKind.Cover => CoverHealth,
        ThrowableKind.Insertion => InsertionHealth,
        _ => 1,
    };

    public static double Life(ThrowableKind k) => k switch
    {
        ThrowableKind.Decoy => DecoyLife,
        ThrowableKind.MotionSensor => SensorLife,
        ThrowableKind.Trophy => TrophyLife,
        ThrowableKind.Cover => CoverLife,
        ThrowableKind.Insertion => InsertionLife,
        _ => 1,
    };

    /// <summary>The axis-aligned box bullets and blasts hit (for Deployable Cover, the box around the turned wall: use <see cref="CoverRay"/> for shots).</summary>
    public static Box Hitbox(ThrowableKind k, Vector3 pos) => k switch
    {
        ThrowableKind.Trophy => new Box(pos + new Vector3(0, 0.35f, 0), new Vector3(0.28f, 0.35f, 0.28f)),
        ThrowableKind.MotionSensor => new Box(pos + new Vector3(0, 0.12f, 0), new Vector3(0.16f, 0.16f, 0.16f)),
        ThrowableKind.Decoy => new Box(pos + new Vector3(0, 0.1f, 0), new Vector3(0.14f, 0.12f, 0.14f)),
        ThrowableKind.Cover => new Box(pos + new Vector3(0, CoverHalf.Y, 0), new Vector3(CoverHalf.X, CoverHalf.Y, CoverHalf.X)),
        ThrowableKind.Insertion => new Box(pos + new Vector3(0, 0.3f, 0), new Vector3(0.15f, 0.3f, 0.15f)),
        _ => new Box(pos, new Vector3(0.2f)),
    };

    /// <summary>
    /// A ray against a Deployable Cover wall standing at <paramref name="pos"/> (its base centre) facing
    /// <paramref name="yaw"/>: the ray is turned into the wall's own frame and tested against its box. Returns the
    /// distance, or null for a miss within <paramref name="maxDist"/>.
    /// </summary>
    public static float? CoverRay(Vector3 pos, float yaw, Vector3 origin, Vector3 dir, float maxDist)
    {
        var right = World.FlatRight(yaw);
        var fwd = World.FlatForward(yaw);
        var rel = origin - (pos + new Vector3(0, CoverHalf.Y, 0));
        var o = new Vector3(Vector3.Dot(rel, right), rel.Y, Vector3.Dot(rel, fwd));
        var d = new Vector3(Vector3.Dot(dir, right), dir.Y, Vector3.Dot(dir, fwd));
        return new Box(Vector3.Zero, CoverHalf).Raycast(o, d, maxDist);
    }

    // ---- Flash formulas -----------------------------------------------------------------------------

    /// <summary>
    /// How hard a flash at <paramref name="flash"/> hits someone whose eye is at <paramref name="eye"/> looking along
    /// <paramref name="forward"/> (unit): 0 (nothing) to 1. Distance: 1 - (d / 14 m)^2. Angle: full while facing it
    /// (within about 45 degrees), down to <see cref="FlashBehind"/> with it straight behind. No clear line: 0.
    /// </summary>
    public static float FlashStrength(Vector3 eye, Vector3 forward, Vector3 flash, bool clearLine, bool flak, bool self)
    {
        if (!clearLine)
            return 0f;
        var to = flash - eye;
        var d = to.Length();
        if (d >= FlashRadius)
            return 0f;
        var near = 1f - d * d / (FlashRadius * FlashRadius);
        var cos = d > 0.05f ? Vector3.Dot(forward, to / d) : 1f;
        var x = Math.Clamp((cos + 0.6f) / 1.3f, 0f, 1f); // cos -0.6 (about 127 degrees off) .. 0.7 (about 45 degrees)
        var facing = FlashBehind + (1f - FlashBehind) * x * x * (3f - 2f * x);
        var s = near * facing * (flak ? FlakTacticalMult : 1f) * (self ? SelfMult : 1f);
        return s < FlashMinStrength ? 0f : Math.Min(s, 1f);
    }

    /// <summary>Seconds of full white-out at strength <paramref name="s"/>.</summary>
    public static float FlashHold(float s) => FlashHoldMax * Math.Clamp(s, 0f, 1f);

    /// <summary>Seconds the white fades over after the hold.</summary>
    public static float FlashFade(float s) => FlashFadeBase + FlashFadeMax * Math.Clamp(s, 0f, 1f);

    /// <summary>Total seconds a flash of strength <paramref name="s"/> affects you.</summary>
    public static float FlashTotal(float s) => s <= 0 ? 0 : FlashHold(s) + FlashFade(s);

    /// <summary>
    /// The white overlay's opacity <paramref name="t"/> seconds after a flash of strength <paramref name="s"/>. It never
    /// rises again once it starts to fade, so it can't strobe: a flat hold, then a smooth fall to 0.
    /// </summary>
    public static float FlashAlpha(float s, float t)
    {
        if (s <= 0 || t < 0)
            return 0f;
        var peak = Math.Min(1f, 0.35f + 0.75f * s);
        var hold = FlashHold(s);
        if (t <= hold)
            return peak;
        var u = (t - hold) / FlashFade(s);
        if (u >= 1f)
            return 0f;
        var k = 1f - u;
        return peak * k * k;
    }

    /// <summary>How long a bot stays blinded (target lost, no new ones) by a flash of strength <paramref name="s"/>.</summary>
    public static float BotBlindTime(float s)
    {
        if (s <= 0 || Math.Min(1f, 0.35f + 0.75f * s) < BotBlindAlpha)
            return 0f;
        var peak = Math.Min(1f, 0.35f + 0.75f * s);
        // peak (1 - u)^2 = BotBlindAlpha  ->  u = 1 - sqrt(BotBlindAlpha / peak)
        return FlashHold(s) + FlashFade(s) * (1f - MathF.Sqrt(BotBlindAlpha / peak));
    }

    /// <summary>Deafness (0..1: how muffled everything is) <paramref name="t"/> seconds after a flash of strength <paramref name="s"/>.</summary>
    public static float FlashDeafen(float s, float t)
    {
        if (s <= 0 || t < 0)
            return 0f;
        var top = Math.Min(1f, s * 1.1f);
        var hold = FlashHold(s) + 0.5f;
        if (t <= hold)
            return top;
        var u = (t - hold) / (FlashFade(s) * 1.2f);
        return u >= 1f ? 0f : top * (1f - u);
    }

    // ---- Stun formulas ------------------------------------------------------------------------------

    /// <summary>How hard a stun grenade <paramref name="dist"/> metres away hits (0..1): 1 - (d / 8 m)^1.5, with a clear line only.</summary>
    public static float StunStrength(float dist, bool clearLine, bool flak, bool self)
    {
        if (!clearLine || dist >= StunRadius)
            return 0f;
        var s = (1f - MathF.Pow(Math.Max(dist, 0f) / StunRadius, 1.5f)) * (flak ? FlakTacticalMult : 1f) * (self ? SelfMult : 1f);
        return s < FlashMinStrength ? 0f : Math.Min(s, 1f);
    }

    public static float StunTime(float s) => s <= 0 ? 0 : StunBaseTime + StunExtraTime * Math.Clamp(s, 0f, 1f);

    /// <summary>Movement speed multiplier while stunned at strength <paramref name="s"/>.</summary>
    public static float StunMove(float s) => 1f - StunMoveSlow * Math.Clamp(s, 0f, 1f);

    /// <summary>Turn (mouse look) multiplier while stunned.</summary>
    public static float StunLook(float s) => 1f - StunLookSlow * Math.Clamp(s, 0f, 1f);

    /// <summary>Aim-down-sights speed multiplier while stunned.</summary>
    public static float StunAds(float s) => 1f - StunAdsSlow * Math.Clamp(s, 0f, 1f);

    /// <summary>A stun's strength <paramref name="t"/> seconds in: full, then it eases off over the last half second.</summary>
    public static float StunNow(float s, float t)
    {
        var total = StunTime(s);
        if (s <= 0 || t < 0 || t >= total)
            return 0f;
        return s * Math.Clamp((total - t) / 0.5f, 0f, 1f);
    }

    // ---- Bots ---------------------------------------------------------------------------------------

    /// <summary>
    /// The tactical item a bot of personality <paramref name="kind"/> carries, from a roll in 0..1 (deterministic per bot
    /// and life, so a seeded match replays the same). Some carry nothing, like before.
    /// </summary>
    public static ThrowableKind BotPick(BotPersonality kind, double roll)
    {
        var pool = BotPool(kind);
        return pool[Math.Clamp((int)(roll * pool.Length), 0, pool.Length - 1)];
    }

    /// <summary>The tactical items a personality picks from (<see cref="BotPick"/>; shared arrays, don't change them).</summary>
    public static ThrowableKind[] BotPool(BotPersonality kind) => kind switch
    {
        BotPersonality.Rusher => RusherPool,
        BotPersonality.Marksman => MarksmanPool,
        BotPersonality.Tactician => TacticianPool,
        BotPersonality.Chaotic => ChaoticPool,
        _ => OtherPool,
    };

    private static readonly ThrowableKind[] RusherPool = [ThrowableKind.Flash, ThrowableKind.Stun, ThrowableKind.Stim, ThrowableKind.None];
    private static readonly ThrowableKind[] MarksmanPool = [ThrowableKind.MotionSensor, ThrowableKind.Cover, ThrowableKind.Trophy, ThrowableKind.None];
    private static readonly ThrowableKind[] TacticianPool = [ThrowableKind.Flash, ThrowableKind.Trophy, ThrowableKind.MotionSensor, ThrowableKind.Insertion, ThrowableKind.Decoy];
    private static readonly ThrowableKind[] ChaoticPool = [ThrowableKind.Decoy, ThrowableKind.Stun, ThrowableKind.Flash, ThrowableKind.Stim];
    private static readonly ThrowableKind[] OtherPool = [ThrowableKind.Flash, ThrowableKind.Stim, ThrowableKind.Cover, ThrowableKind.Insertion, ThrowableKind.None, ThrowableKind.None];

    /// <summary>A cheap deterministic hash in 0..1 (bot rolls that mustn't disturb the server's shared random sequence).</summary>
    public static double Roll(uint a, uint b, uint salt)
    {
        var h = a * 0x9E3779B1u ^ (b + 0x7F4A7C15u) * 0x85EBCA77u ^ salt * 0xC2B2AE3Du;
        h ^= h >> 15;
        h *= 0x2C1B3C6Du;
        h ^= h >> 12;
        h *= 0x297A2D39u;
        h ^= h >> 15;
        return (h & 0xFFFFFF) / (double)0x1000000;
    }

    /// <summary>Short stat lines for the loadout's info panel.</summary>
    public static IEnumerable<string> StatLines(ThrowableKind k)
    {
        switch (k)
        {
            case ThrowableKind.Flash:
                yield return $"Blind: up to {FlashHoldMax:0.0} s white, then a {FlashFadeBase + FlashFadeMax:0.0} s fade (within {FlashRadius:0} m)";
                yield return "Looking away or walls: much weaker; Flak Jacket halves it";
                break;
            case ThrowableKind.Stun:
                yield return $"Slow: down to {StunMove(1) * 100:0}% speed, {StunLook(1) * 100:0}% turning for up to {StunTime(1):0} s (within {StunRadius:0} m)";
                yield return "Flak Jacket halves it; a Stim Shot clears it";
                break;
            case ThrowableKind.Decoy:
                yield return $"Fake gunfire for {DecoyLife:0} s, heard by bots within {DecoyHearing:0} m";
                yield return "No tracers or hits give it away; one shot breaks it";
                break;
            case ThrowableKind.MotionSensor:
                yield return $"Pings enemies moving faster than {SensorMinSpeed:0.0} m/s within {SensorRadius:0} m for {SensorLife:0} s";
                yield return $"Ghost hides you; {SensorHealth} health";
                break;
            case ThrowableKind.Trophy:
                yield return $"{TrophyCharges} intercepts within {TrophyRadius:0} m, lasts {TrophyLife:0} s";
                yield return $"{TrophyHealth} health; doesn't stop streaks or exploding barrels";
                break;
            case ThrowableKind.Cover:
                yield return $"{CoverHalf.X * 2:0} m x {CoverHalf.Y * 2:0.0} m wall for {CoverLife:0} s, {CoverHealth} health";
                yield return $"Explosives do {CoverExplosiveMult:0}x damage to it";
                break;
            case ThrowableKind.Insertion:
                yield return $"Your next spawn; {InsertionHealth} health, gone once used";
                break;
            case ThrowableKind.Stim:
                yield return $"+{StimHeal} health now, then {StimRegen:0} a second for {StimTime:0} s";
                break;
        }
    }
}
