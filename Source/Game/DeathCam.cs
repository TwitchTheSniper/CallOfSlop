using System.Numerics;

namespace CallOfSlop;

/// <summary>How the local player died (picks the kill card's layout).</summary>
public enum KillKind : byte
{
    /// <summary>Shot with a gun.</summary>
    Gun,

    /// <summary>Melee.</summary>
    Melee,

    /// <summary>A frag, impact grenade or other throwable.</summary>
    Throwable,

    /// <summary>Killed yourself (your own grenade).</summary>
    Self,
}

/// <summary>
/// The contents of a <see cref="Msg.Kill"/> packet. The first four fields are the original packet;
/// the rest were added in protocol 5 and are only read when present (older senders leave them unknown).
/// </summary>
/// <param name="Kit">Attachment kit on the weapon that killed (0 for melee and throwables).</param>
/// <param name="KillerHp">The killer's health right after the kill, or -1 if unknown.</param>
/// <param name="Source">Where the damage came from: the killer's eye, or the blast point for throwables.</param>
public readonly record struct KillPacket(byte Killer, byte Victim, bool Headshot, byte KillId,
    uint Kit = 0, int KillerHp = -1, Vector3? Source = null)
{
    /// <summary>True when the protocol 5 extras were in the packet.</summary>
    public bool HasExtras => KillerHp >= 0;

    /// <summary>Writes the whole packet (the original four fields, then the extras).</summary>
    public void Write(PacketWriter w) =>
        w.Begin(Msg.Kill).U8(Killer).U8(Victim).U8(Headshot ? (byte)1 : (byte)0).U8(KillId)
            .U24(Kit).U8((byte)Math.Clamp(KillerHp, 0, 255)).Vec(Source ?? Vector3.Zero);

    /// <summary>Reads a packet body (after the message id). Tolerates packets without the extras.</summary>
    public static KillPacket Read(ref PacketReader r)
    {
        var p = new KillPacket(r.U8(), r.U8(), r.U8() != 0, r.U8());
        if (!r.Has(4))
            return p;
        p = p with { Kit = r.U24(), KillerHp = r.U8() };
        if (r.Has(12))
            p = p with { Source = r.Vec() };
        return p;
    }
}

/// <summary>Everything the death camera and kill card need about the local player's last death.</summary>
public sealed class KillInfo
{
    public byte KillerId, VictimId, KillId;
    public KillKind Kind;
    public bool Headshot;
    public string KillerName = "";
    public Vector3 KillerColor = Vector3.One;

    /// <summary>The killer's weapon with its attachments (guns only).</summary>
    public WeaponDef? Weapon;
    public uint Kit;

    /// <summary>The killer's health after the kill, or -1 if unknown.</summary>
    public int KillerHp = -1;

    /// <summary>Kills in a row the killer has (including this one), and their total kills, or -1 if unknown.</summary>
    public int KillerStreak = -1, KillerKills = -1;

    /// <summary>Where the victim's eye was, where the killer was and where the damage came from (blast point for throwables).</summary>
    public Vector3 VictimEye;
    public Vector3? KillerPos, Source;

    /// <summary>Metres from the killer (or the blast) to the victim, or -1 if unknown.</summary>
    public float Distance = -1;

    /// <summary>Clock time (Glfw.Time) when the kill was recorded.</summary>
    public double Time;

    /// <summary>The weapon / throwable name for the card.</summary>
    public string WeaponName => Throwables.KillName(KillId);

    /// <summary>Builds the info from the packet and what the client knows about the two players. Pure, so the self-test can call it.</summary>
    public static KillInfo Create(in KillPacket k, Player? killer, Player? victim, string killerName, int streak, double now)
    {
        var info = new KillInfo
        {
            KillerId = k.Killer, VictimId = k.Victim, KillId = k.KillId, Headshot = k.Headshot, Time = now,
            KillerName = killerName, KillerColor = Player.Palette[k.Killer % Player.Palette.Length],
            KillerHp = k.KillerHp, KillerStreak = streak, KillerKills = killer?.Kills ?? -1,
        };
        info.Kind = k.Killer == k.Victim ? KillKind.Self
            : k.KillId == Game.MeleeKillId ? KillKind.Melee
            : k.KillId >= Throwables.KillIdBase ? KillKind.Throwable
            : KillKind.Gun;
        if (k.KillId < Game.MeleeKillId)
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
            info.Distance = Vector3.Distance(f, info.VictimEye);
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
    public KillInfo? Info;

    /// <summary>The pose for this frame (valid while <see cref="Active"/>).</summary>
    public Vector3 Eye;
    public float Yaw, Pitch, FovMul = 1f;
    public Vector3 Forward => World.Forward(Yaw, Pitch);

    private Vector3 _deathEye, _rest;
    private float _startYaw, _startPitch;
    private Vector3 _target;
    private bool _hasTarget;

    /// <summary>Starts the camera at the victim's eye. <paramref name="ray"/>(origin, unit dir, max) returns the free distance.</summary>
    public void Begin(Vector3 deathEye, float yaw, float pitch, double now, Func<Vector3, Vector3, float, float> ray)
    {
        Active = true;
        Start = now;
        Info = null;
        _hasTarget = false;
        _deathEye = deathEye;
        _startYaw = Finite(yaw);
        _startPitch = Math.Clamp(Finite(pitch), -0.8f, 0.8f);
        _rest = RestPoint(deathEye, _startYaw, ray);
        Eye = deathEye;
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
        var up = MathF.Max(0, ray(eye, Vector3.UnitY, Raise + WallGap) - WallGap);
        var raised = eye + new Vector3(0, MathF.Min(up, Raise), 0);
        var back = -World.FlatForward(yaw);
        var free = MathF.Max(0, ray(raised, back, PullBack + WallGap) - WallGap);
        return raised + back * MathF.Min(free, PullBack);
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
    /// Advances the camera. <paramref name="target"/> is the point to look at (the killer's live position,
    /// or where the grenade went off), or null for "nothing to look at" (keeps the last target, or doesn't turn).
    /// </summary>
    public void Update(double now, float dt, Vector3? target)
    {
        if (!Active)
            return;
        var t = Elapsed(now);
        if (target is { } tp && float.IsFinite(tp.X + tp.Y + tp.Z))
        {
            // Light smoothing so a jittery or teleporting killer doesn't jerk the camera.
            var k = _hasTarget ? 1 - MathF.Exp(-Math.Clamp(dt, 0f, 0.25f) * 10f) : 1f;
            _target = Vector3.Lerp(_target, tp, k);
            _hasTarget = true;
        }

        Eye = Vector3.Lerp(_deathEye, _rest, EaseOut(t / RiseTime));
        var turn = EaseInOut((t - HoldTime) / TurnTime);
        var look = _hasTarget ? LookAt(Eye, _target) : null;
        if (look is { } l)
        {
            Yaw = World.WrapAngle(World.LerpAngle(_startYaw, l.Yaw, turn));
            Pitch = float.Lerp(_startPitch, Math.Clamp(l.Pitch, -1.2f, 1.2f), turn);
            FovMul = float.Lerp(1f, ZoomFor(Vector3.Distance(Eye, _target)), turn);
        }
        else
        {
            // Nobody to look at (self-kill): a slow downward drift toward the body.
            Yaw = _startYaw;
            Pitch = float.Lerp(_startPitch, -0.5f, EaseInOut(t / (HoldTime + TurnTime)));
            FovMul = 1f;
        }
    }

    private static float Finite(float v) => float.IsFinite(v) ? v : 0f;
}

public sealed partial class Game
{
    /// <summary>The local player's most recent death (for the death camera and kill card), or null.</summary>
    public KillInfo? LastDeath;

    private readonly Dictionary<byte, int> _streaks = new();

    /// <summary>The attachment kit on the weapon a kill was made with (0 for melee, throwables or a gun they don't hold).</summary>
    private static uint KitForKill(Player attacker, byte killId) =>
        killId == attacker.Primary ? attacker.PrimaryKit : killId == attacker.Secondary ? attacker.SecondaryKit : 0;

    /// <summary>Tracks kill streaks for everyone and records the local player's death.</summary>
    private void RecordKill(in KillPacket kill)
    {
        if (kill.Killer != kill.Victim)
            _streaks[kill.Killer] = _streaks.GetValueOrDefault(kill.Killer) + 1;
        _streaks[kill.Victim] = 0;
        if (kill.Victim != LocalId)
            return;
        var killer = Players.GetValueOrDefault(kill.Killer);
        var streak = kill.Killer == kill.Victim ? -1 : _streaks.GetValueOrDefault(kill.Killer);
        LastDeath = KillInfo.Create(kill, killer, Local, NameOf(kill.Killer), streak, Brutal.GlfwApi.Glfw.Time);
    }
}
