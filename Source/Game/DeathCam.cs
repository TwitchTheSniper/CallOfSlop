using System.Numerics;

namespace CallOfSlop;

/// <summary>How the local player died (picks the kill card's layout and the death camera's behaviour).</summary>
public enum KillKind : byte
{
    /// <summary>Shot with a gun.</summary>
    Gun,

    /// <summary>Melee.</summary>
    Melee,

    /// <summary>A frag, impact grenade or other throwable.</summary>
    Throwable,

    /// <summary>A kill-streak reward (kill ids 230-238 and 214-229) or any other id that isn't a gun.</summary>
    Streak,

    /// <summary>Killed yourself (your own grenade).</summary>
    Self,
}

/// <summary>
/// The contents of a <see cref="Msg.Kill"/> packet. The first four fields are the original packet;
/// the rest were added in protocol 5 and are only read when present (older senders leave them unknown).
/// </summary>
/// <param name="Kit">Attachment kit on the weapon that killed (0 for melee and throwables).</param>
/// <param name="KillerHp">The killer's health right after the kill (0 if they're dead too), or -1 if unknown.</param>
/// <param name="Source">Where the damage came from: the killer's eye, or the blast point for throwables.</param>
public readonly record struct KillPacket(byte Killer, byte Victim, bool Headshot, byte KillId,
    uint Kit = 0, int KillerHp = -1, Vector3? Source = null)
{
    /// <summary>Bytes after the message id: the original 4, plus kit (3), killer HP (1) and source (12).</summary>
    public const int Size = 4 + 3 + 1 + 12;

    /// <summary>
    /// The victim was blown apart (a blast kill right next to the explosion, <see cref="Game.ShouldGib"/>): every PC bursts
    /// the body into gibs instead of a ragdoll. Bit 1 of the headshot byte (protocol 33).
    /// </summary>
    public bool Gibbed { get; init; }

    /// <summary>True when the protocol 5 extras were in the packet.</summary>
    public bool HasExtras => KillerHp >= 0;

    /// <summary>Writes the whole packet (the original four fields, then the extras).</summary>
    public void Write(PacketWriter w) =>
        w.Begin(Msg.Kill).U8(Killer).U8(Victim).U8((byte)((Headshot ? 1 : 0) | (Gibbed ? 2 : 0))).U8(KillId)
            .U24(Kit).U8((byte)Math.Clamp(KillerHp, 0, 255)).Vec(Source ?? Vector3.Zero);

    /// <summary>Reads a packet body (after the message id). Tolerates packets without the extras.</summary>
    public static KillPacket Read(ref PacketReader r)
    {
        var killer = r.U8();
        var victim = r.U8();
        var flags = r.U8();
        var p = new KillPacket(killer, victim, (flags & 1) != 0, r.U8()) { Gibbed = (flags & 2) != 0 };
        if (!r.Has(4))
            return p;
        p = p with { Kit = r.U24(), KillerHp = r.U8() };
        if (r.Has(12))
        {
            var s = r.Vec();
            if (float.IsFinite(s.X + s.Y + s.Z))
                p = p with { Source = s };
        }
        return p;
    }
}

/// <summary>Everything the death camera and kill card need about the local player's last death.</summary>
public sealed class KillInfo
{
    /// <summary>First kill id of the first eight kill-streak rewards (230-238; the later ones use 214-229, melee is 239, throwables 240+).</summary>
    public const byte StreakKillIdFirst = 230;

    public byte KillerId, VictimId, KillId;
    public KillKind Kind;
    public bool Headshot;
    public string KillerName = "";
    public Vector3 KillerColor = Vector3.One;

    /// <summary>The killer's weapon with its attachments (guns only, else null).</summary>
    public WeaponDef? Weapon;
    public uint Kit;

    /// <summary>The killer's health after the kill, or -1 if unknown.</summary>
    public int KillerHp = -1;

    /// <summary>Kills in a row the killer has (including this one), and their total kills, or -1 if unknown.</summary>
    public int KillerStreak = -1, KillerKills = -1;

    /// <summary>Where the victim's eye was, where the killer was and where the damage came from (blast point for throwables).</summary>
    public Vector3 VictimEye;
    public Vector3? KillerPos, Source;

    /// <summary>Metres from the killer to the victim (the thrower for throwables), or -1 if unknown.</summary>
    public float Distance = -1;

    /// <summary>Clock time (Glfw.Time) when the kill was recorded.</summary>
    public double Time;

    /// <summary>The weapon / throwable / streak name for the card.</summary>
    public string WeaponName { get; private set; } = "";

    /// <summary>Is this kill id one of the gun ids?</summary>
    public static bool IsGunId(byte killId) => killId < Game.MeleeKillId && killId < Weapons.All.Length;

    /// <summary>Picks the card layout for a kill.</summary>
    public static KillKind KindOf(byte killer, byte victim, byte killId) =>
        killer == victim ? KillKind.Self
        : killId == Game.MeleeKillId ? KillKind.Melee
        : killId >= Throwables.KillIdBase ? KillKind.Throwable
        : IsGunId(killId) ? KillKind.Gun
        : KillKind.Streak;

    /// <summary>A display name for any kill id, including ones this build doesn't know (e.g. new kill-streak ids).</summary>
    public static string NameFor(byte killId)
    {
        if (killId == Game.MeleeKillId || killId >= Throwables.KillIdBase || IsGunId(killId))
            return Throwables.KillName(killId);
        // Not a gun: if KillName fell back to weapon 0, it doesn't know this id.
        var name = Throwables.KillName(killId);
        return name.Length > 0 && name != Weapons.Get(killId).Name ? name : KillStreaks.IsStreakKill(killId) ? "Kill Streak" : "Unknown";
    }

    /// <summary>Builds the info from the packet and what the client knows about the two players. Pure, so the self-test can call it.</summary>
    public static KillInfo Create(in KillPacket k, Player? killer, Player? victim, string killerName, int streak, double now)
    {
        var info = new KillInfo
        {
            KillerId = k.Killer, VictimId = k.Victim, KillId = k.KillId, Headshot = k.Headshot, Time = now,
            KillerName = killerName, KillerColor = Player.Palette[k.Killer % Player.Palette.Length],
            KillerHp = k.KillerHp, KillerStreak = streak,
            Kind = KindOf(k.Killer, k.Victim, k.KillId), WeaponName = NameFor(k.KillId),
        };
        if (killer != null)
            info.KillerKills = Math.Max(killer.Kills, streak);
        if (info.Kind == KillKind.Gun || (info.Kind == KillKind.Self && IsGunId(k.KillId)))
        {
            // Guns: the exact kit from the packet, or the killer's replicated kit for that gun.
            var kit = k.HasExtras ? k.Kit
                : killer == null ? 0
                : k.KillId == killer.Primary ? killer.PrimaryKit
                : k.KillId == killer.Secondary ? killer.SecondaryKit : 0;
            var baseDef = Weapons.Get(k.KillId);
            info.Kit = Attachments.Sanitize(baseDef, kit);
            info.Weapon = Attachments.Apply(baseDef, info.Kit);
        }

        info.VictimEye = victim?.RenderEye ?? Vector3.Zero;
        info.KillerPos = killer?.RenderEye;
        info.Source = k.Source ?? info.KillerPos;
        // Throwables: how far away the thrower was (falls back to the blast); everything else: the shot's length.
        var from = info.Kind == KillKind.Throwable ? info.KillerPos ?? info.Source : info.Source;
        if (victim != null && from is { } f && info.Kind != KillKind.Self)
        {
            var d = Vector3.Distance(f, info.VictimEye);
            if (float.IsFinite(d))
                info.Distance = d;
        }
        return info;
    }
}

/// <summary>
/// The death camera: after you die it stays near your body (raised and pulled back), then eases
/// round to face your killer and zooms in a little, tracking them as they move. The math is pure
/// (a raycast is passed in), so the self-test runs it headless.
/// </summary>
public sealed class DeathCam
{
    /// <summary>Seconds after death before the loadout / respawn screen opens.</summary>
    public const float Duration = 2.8f;

    /// <summary>Seconds the camera holds its first direction before turning.</summary>
    public const float HoldTime = 0.55f;

    /// <summary>Seconds the turn toward the killer takes.</summary>
    public const float TurnTime = 1.1f;

    /// <summary>Seconds the camera takes to rise and pull back from the eye.</summary>
    public const float RiseTime = 0.7f;

    private const float Raise = 0.9f, PullBack = 1.8f, WallGap = 0.3f;

    public bool Active;
    public double Start;

    /// <summary>The death being shown (null until the kill message arrives).</summary>
    public KillInfo? Info;

    /// <summary>The pose for this frame (valid while <see cref="Active"/>).</summary>
    public Vector3 Eye;
    public float Yaw, Pitch, FovMul = 1f;
    public Vector3 Forward => World.Forward(Yaw, Pitch);

    private Vector3 _deathEye, _rest;
    private float _startYaw, _startPitch;
    private Vector3 _target;
    private bool _hasTarget, _killerDown;
    private float _acquiredAt;

    /// <summary>Has the camera got something to look at?</summary>
    public bool HasTarget => _hasTarget;

    /// <summary>Starts the camera at the victim's eye. <paramref name="ray"/>(origin, unit dir, max) returns the free distance.</summary>
    public void Begin(Vector3 deathEye, float yaw, float pitch, double now, Func<Vector3, Vector3, float, float> ray)
    {
        Active = true;
        Start = now;
        Info = null;
        _hasTarget = _killerDown = false;
        _acquiredAt = 0;
        _deathEye = float.IsFinite(deathEye.X + deathEye.Y + deathEye.Z) ? deathEye : Vector3.Zero;
        _startYaw = Finite(yaw);
        _startPitch = Math.Clamp(Finite(pitch), -0.8f, 0.8f);
        _rest = RestPoint(_deathEye, _startYaw, ray);
        Eye = _deathEye;
        Yaw = _startYaw;
        Pitch = _startPitch;
        FovMul = 1f;
    }

    public void Stop()
    {
        Active = false;
        Info = null;
    }

    /// <summary>Seconds since the camera started.</summary>
    public float Elapsed(double now) => Active ? (float)Math.Max(0, now - Start) : 0;

    /// <summary>True while the camera is still in its timed part (before the loadout screen opens).</summary>
    public bool Running(double now) => Active && now - Start < Duration;

    /// <summary>
    /// Where the camera settles: up from the eye, then back away from where you were looking,
    /// stopping short of any wall in between.
    /// </summary>
    public static Vector3 RestPoint(Vector3 eye, float yaw, Func<Vector3, Vector3, float, float> ray)
    {
        var up = Math.Clamp(Finite(ray(eye, Vector3.UnitY, Raise + WallGap)) - WallGap, 0, Raise);
        var raised = eye + new Vector3(0, up, 0);
        var back = -World.FlatForward(Finite(yaw));
        var free = Math.Clamp(Finite(ray(raised, back, PullBack + WallGap)) - WallGap, 0, PullBack);
        return raised + back * free;
    }

    /// <summary>Smooth start and end (0..1 -> 0..1).</summary>
    public static float EaseInOut(float t)
    {
        t = Math.Clamp(Finite(t), 0f, 1f);
        return t < 0.5f ? 4 * t * t * t : 1 - MathF.Pow(-2 * t + 2, 3) / 2;
    }

    /// <summary>Fast start, gentle end.</summary>
    public static float EaseOut(float t)
    {
        t = Math.Clamp(Finite(t), 0f, 1f);
        return 1 - MathF.Pow(1 - t, 3);
    }

    /// <summary>Yaw and pitch that look from <paramref name="from"/> to <paramref name="to"/> (null if they're the same point).</summary>
    public static (float Yaw, float Pitch)? LookAt(Vector3 from, Vector3 to)
    {
        var d = to - from;
        var len = d.Length();
        if (!(len > 1e-3f) || !float.IsFinite(len))
            return null;
        d /= len;
        return (MathF.Atan2(d.X, -d.Z), MathF.Asin(Math.Clamp(d.Y, -1f, 1f)));
    }

    /// <summary>How far the camera zooms in (FOV multiplier) for a killer this far away.</summary>
    public static float ZoomFor(float distance) =>
        Math.Clamp(1f - Finite(distance) * 0.008f, 0.62f, 0.9f);

    /// <summary>
    /// What to look at this frame: the killer's live eye while they're alive; once they've died
    /// (even if they respawn somewhere else) their body; nothing for self-kills. Disconnected killers
    /// keep the last tracked point, or where they were (the blast point for throwables) if there wasn't one.
    /// </summary>
    public static Vector3? PickTarget(KillInfo? info, Player? killer, ref bool killerDown, bool hasTarget)
    {
        if (info == null || info.Kind == KillKind.Self)
            return null;
        if (killer != null)
        {
            if (!killer.Alive)
            {
                killerDown = true;
                return killer.RenderPosition + new Vector3(0, 0.3f, 0);
            }
            if (!killerDown)
                return killer.RenderEye;
            return null; // respawned after dying: stay on where they fell
        }
        if (hasTarget)
            return null;
        return info.Kind == KillKind.Throwable ? info.Source ?? info.KillerPos : info.KillerPos ?? info.Source;
    }

    /// <summary>Per frame: picks up the kill message when it arrives and follows the killer.</summary>
    public void Track(Game game, double now, float dt)
    {
        if (!Active)
            return;
        // The kill message and the snapshot that says you're dead can arrive in either order.
        if (Info == null && game.LastDeath is { } d && d.Time > Start - 1.0)
            Info = d;
        var killer = Info == null ? null : game.Players.GetValueOrDefault(Info.KillerId);
        var target = PickTarget(Info, killer, ref _killerDown, _hasTarget);
        if (target != null && killer is { Alive: false } && Ragdolls.BodyPoint(killer.Id) is { } body)
            target = body + new Vector3(0, 0.3f, 0); // the killer died: look at where their ragdoll lies, not where they fell from
        Update(now, dt, target, Info?.Kind == KillKind.Self);
    }

    /// <summary>
    /// Advances the camera. <paramref name="target"/> is the point to look at, or null to keep the last
    /// one (or hold still if there never was one). <paramref name="drift"/> slowly tilts down toward the
    /// body instead (self-kills).
    /// </summary>
    public void Update(double now, float dt, Vector3? target, bool drift = false)
    {
        if (!Active)
            return;
        var t = Elapsed(now);
        if (target is { } tp && float.IsFinite(tp.X + tp.Y + tp.Z))
        {
            // Light smoothing so a jittery or teleporting killer doesn't jerk the camera.
            var k = _hasTarget ? 1 - MathF.Exp(-Math.Clamp(Finite(dt), 0f, 0.25f) * 10f) : 1f;
            if (!_hasTarget)
                _acquiredAt = t;
            _target = Vector3.Lerp(_target, tp, k);
            _hasTarget = true;
        }

        // Rise first, then pull back (an L, the same path the ray casts checked, so it never cuts through a crate).
        var rise = EaseOut(t / (RiseTime * 0.45f));
        var pull = EaseInOut((t - RiseTime * 0.35f) / (RiseTime * 0.65f));
        Eye = _deathEye + new Vector3(0, (_rest.Y - _deathEye.Y) * rise, 0) + new Vector3(_rest.X - _deathEye.X, 0, _rest.Z - _deathEye.Z) * pull;
        // The turn starts after the hold, or as soon as we know who to look at if that's later.
        var turn = EaseInOut((t - MathF.Max(HoldTime, _acquiredAt)) / TurnTime);
        var look = _hasTarget ? LookAt(Eye, _target) : null;
        if (look is { } l)
        {
            Yaw = World.WrapAngle(World.LerpAngle(_startYaw, l.Yaw, turn));
            Pitch = float.Lerp(_startPitch, Math.Clamp(l.Pitch, -1.2f, 1.2f), turn);
            FovMul = float.Lerp(1f, ZoomFor(Vector3.Distance(Eye, _target)), turn);
        }
        else
        {
            Yaw = _startYaw;
            Pitch = drift ? float.Lerp(_startPitch, -0.6f, EaseInOut(t / (HoldTime + TurnTime))) : _startPitch;
            FovMul = 1f;
        }
    }

    private static float Finite(float v) => float.IsFinite(v) ? v : 0f;
}

public sealed partial class Game
{
    /// <summary>The local player's most recent death (for the death camera and kill card), or null.</summary>
    public KillInfo? LastDeath;

    private readonly Dictionary<byte, int> _deathCamStreaks = new();

    /// <summary>The attachment kit on the weapon a kill was made with (0 for melee, throwables or a gun they don't hold).</summary>
    private static uint KitForKill(Player attacker, byte killId) =>
        killId == attacker.Primary ? attacker.PrimaryKit : killId == attacker.Secondary ? attacker.SecondaryKit : 0;

    /// <summary>Tracks kill streaks for everyone (cheap, for the kill card) and records the local player's death.</summary>
    private void RecordKill(in KillPacket kill)
    {
        var killer = Players.GetValueOrDefault(kill.Killer);
        if (kill.Killer != kill.Victim)
        {
            var s = _deathCamStreaks.GetValueOrDefault(kill.Killer) + 1;
            if (killer != null)
                s = Math.Min(s, killer.Kills + 1); // kills reset with a new match, so the streak can't be longer
            _deathCamStreaks[kill.Killer] = s;
        }
        _deathCamStreaks[kill.Victim] = 0;
        Ragdolls.NoteKill(kill, Brutal.GlfwApi.Glfw.Time); // the victim's body falls the way this kill pushed it (Source/Render/Ragdolls.cs)
        NoteLocalKill(kill); // the killer's "KILLED Ash" notice (KillNotices.cs)
        NoteFrenzyKill(kill); // the kill-frenzy music's feed (FrenzyFeed.cs)
        ScavengeLocal(kill, Brutal.GlfwApi.Glfw.Time); // Scavenger: your own grenade count (PerksMore.cs)
        if (kill.Victim != LocalId)
            return;
        var streak = kill.Killer == kill.Victim ? -1 : _deathCamStreaks.GetValueOrDefault(kill.Killer);
        LastDeath = KillInfo.Create(kill, killer, Local, NameOf(kill.Killer), streak, Brutal.GlfwApi.Glfw.Time);
    }

    /// <summary>
    /// Server only (smoke test and self-test): <paramref name="attacker"/> kills <paramref name="victim"/>
    /// through the normal damage path (kill message, feed, kill card). Returns true if it killed.
    /// </summary>
    public bool DebugKill(Player attacker, Player victim, byte killId, bool head, double now)
    {
        if (!IsServer)
            return false;
        victim.ImmuneUntil = 0;
        victim.LastStandUsed = true; // no Last Stand save
        victim.ShieldCharges = 0; // and no shield left over from one
        return DamagePlayer(attacker, victim, 9999, head, killId, now,
            killId >= Throwables.KillIdBase ? DamageKind.Explosive : DamageKind.Bullet);
    }

    /// <summary>Smoke test: the nearest living bot shoots the local player with its primary. Returns the bot, or null.</summary>
    public Player? DebugKillLocalByBot(double now)
    {
        if (Local is not { Alive: true } me)
            return null;
        Player? best = null;
        foreach (var p in Players.Values)
            if (p.Bot != null && p.Alive && (best == null || Vector3.DistanceSquared(p.Position, me.Position) < Vector3.DistanceSquared(best.Position, me.Position)))
                best = p;
        if (best == null)
            return null;
        return DebugKill(best, me, best.Primary, head: true, now) ? best : null;
    }
}
