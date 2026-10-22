using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Zombies: co-op bots and the downed state's controls. A bot holds the area it started in: it shoots the nearest zombie
/// it can see (through windows too), backs off from zombies that get close, revives downed teammates and rebuilds
/// boards when it's quiet. Bots don't buy anything (proposed; see docs/zombies/ZOMBIES-MODE.md). Everything a bot does
/// goes through the same input commands, shots and interact key as a person.
/// </summary>
public sealed partial class Game
{
    private const Buttons ZMoveButtons = Buttons.Forward | Buttons.Back | Buttons.Left | Buttons.Right | Buttons.Jump | Buttons.Sprint | Buttons.Crouch;

    /// <summary>Downed in Zombies: lying down, no walking (both the client's prediction and the server use this).</summary>
    private Buttons ZombieButtons(Player? p, Buttons b) =>
        p != null && Mode == GameMode.Zombies && IsDowned(p) ? (b & ~ZMoveButtons) | Buttons.Prone : b;

    /// <summary>Can a bot at <paramref name="eye"/> see (and shoot) <paramref name="point"/>? Window openings don't block.</summary>
    private static bool ZClearShot(Vector3 eye, Vector3 point)
    {
        var d = point - eye;
        var len = d.Length();
        return len < 0.01f || World.Query.Raycast(eye, d / len, len, QueryFilter.Shots) >= len - 0.1f;
    }

    internal void ZombieBotTick(Player p, BotBrain b, double now)
    {
        if (!p.Alive)
            return;
        var zp = ZP(p);
        var def = p.WeaponDef;
        if (b.LifeTick != p.SpawnTick)
        {
            b.LifeTick = p.SpawnTick;
            b.HoldAnchor = p.Position;
            b.Ammo = def.Mag;
            b.ReloadUntil = 0;
            b.Target = byte.MaxValue;
        }
        if (b.ReloadUntil > 0 && now >= b.ReloadUntil)
        {
            b.Ammo = def.Mag;
            b.ReloadUntil = 0;
        }
        var yaw = p.Yaw;
        var pitch = p.Pitch;
        if (zp.Downed)
        {
            ApplyCmd(p, new InputCmd { Seq = ++b.Seq, Yaw = yaw, Pitch = pitch, Weapon = p.Weapon, SpeedPct = 100 });
            return;
        }

        // The target: the nearest zombie in a clear line of fire (looked for a few times a second).
        Zombie? target = null;
        if (now >= b.NextScan || b.Target == byte.MaxValue)
        {
            b.NextScan = now + 0.15;
            var best = float.MaxValue;
            foreach (var z in Horde)
            {
                if (z.State == ZState.Dead)
                    continue;
                var d = Vector3.DistanceSquared(z.Position, p.Position);
                if (d < best && d < 45f * 45f && ZClearShot(p.Eye, z.Position + new Vector3(0, 1.2f, 0)))
                {
                    best = d;
                    target = z;
                }
            }
            b.Target = target != null ? (byte)(target.Id & 0xFF) : byte.MaxValue;
            _zBotTarget[p.Id] = target?.Id ?? 0;
        }
        else if (_zBotTarget.TryGetValue(p.Id, out var tid) && tid != 0)
            target = Horde.FirstOrDefault(z => z.Id == tid && z.State != ZState.Dead);

        // Where to go: away from a zombie that's close, to a downed teammate, to a window that needs boards, or home.
        var nearest = Horde.Where(z => z.State != ZState.Dead).OrderBy(z => Vector3.DistanceSquared(z.Position, p.Position)).ThenBy(z => z.Id).FirstOrDefault();
        var nearD = nearest != null ? Flat(nearest.Position - p.Position) : float.MaxValue;
        Vector3? goal = null;
        var use = false;
        var downed = Players.Values.Where(o => o != p && o.Alive && IsDowned(o)).OrderBy(o => Vector3.DistanceSquared(o.Position, p.Position)).FirstOrDefault();
        if (nearest != null && nearD < 3.2f && nearest.State is ZState.Chase or ZState.Attack)
        {
            var away = p.Position - nearest.Position;
            away.Y = 0;
            goal = p.Position + (away.LengthSquared() > 1e-4f ? Vector3.Normalize(away) : World.FlatForward(p.Yaw)) * 3f;
        }
        else if (downed != null)
        {
            if (Flat(downed.Position - p.Position) > ZRules.ReviveRange * 0.7f)
                goal = downed.Position;
            else
                use = true;
        }
        else if (ZMap is { } zm && (nearest == null || nearD > 7f))
        {
            var area = zm.AreaAt(p.Position);
            for (var i = 0; i < zm.Windows.Count; i++)
            {
                var w = zm.Windows[i];
                if (w.Area != area || i >= ZBoards.Length || ZBoards[i] >= w.Boards || Horde.Any(z => z.State != ZState.Dead && Flat(z.Position - w.Outside) < 1.2f))
                    continue;
                if (Flat(w.Inside - p.Position) > 1.1f)
                    goal = w.Inside;
                else
                    use = true;
                break;
            }
        }
        if (goal == null && !use && Flat(b.HoldAnchor - p.Position) > 2.5f)
            goal = b.HoldAnchor;
        ServerSetUse(p, use, now);

        // Aim: at the target's chest (hard bots go for the head), with a little error that settles.
        var aimed = false;
        if (target != null)
        {
            var aimAt = target.Position + new Vector3(0, b.Skill == BotDifficulty.Hard ? 1.6f : 1.25f, 0);
            var to = aimAt - p.Eye;
            var wantYaw = MathF.Atan2(to.X, -to.Z);
            var wantPitch = MathF.Atan2(to.Y, Flat(to));
            var turn = b.TurnSpeed * World.TickDt;
            yaw = World.WrapAngle(yaw + Math.Clamp(World.WrapAngle(wantYaw - yaw), -turn, turn));
            pitch += Math.Clamp(wantPitch - pitch, -turn, turn);
            aimed = MathF.Abs(World.WrapAngle(wantYaw - yaw)) < 0.06f && MathF.Abs(wantPitch - pitch) < 0.06f;
        }
        else if (goal is { } gl)
            yaw = World.WrapAngle(yaw + Math.Clamp(World.WrapAngle(MathF.Atan2(gl.X - p.Position.X, -(gl.Z - p.Position.Z)) - yaw), -0.15f, 0.15f));

        // Shoot.
        if (b.Ammo <= 0 && b.ReloadUntil == 0)
            BotReload(p, def, now);
        if (aimed && target != null && b.Ammo > 0 && b.ReloadUntil == 0 && now >= b.NextFire && DamageEnabled && !use)
        {
            var dir = World.Forward(yaw, pitch);
            var aimed = Flat(target.Position - p.Position) > 8f;
            var spread = def.HipSpreadRad * (aimed ? def.AdsSpreadMult : 1f) * FrenzyBuffs.ConeMult(p, def, aimed ? 1f : 0f); // the frenzy buffs
            ServerResolveShot(p, def, p.Eye, dir, spread, ZNext(), ServerTick, now);
            b.Ammo--;
            b.NextFire = now + def.FireInterval * ZombieFireIntervalMult(p) + (def.Mode == FireMode.Semi ? b.TriggerDelay : 0);
        }

        // Move: the wish direction turned into the input's relative buttons.
        var buttons = Buttons.None;
        if (goal is { } g && Flat(g - p.Position) > 0.4f)
        {
            var to = g - p.Position;
            var rel = World.WrapAngle(MathF.Atan2(to.X, -to.Z) - yaw);
            if (MathF.Abs(rel) < 1.2f)
                buttons |= Buttons.Forward;
            else if (MathF.Abs(rel) > 1.95f)
                buttons |= Buttons.Back;
            if (rel is > 0.4f and < 2.75f)
                buttons |= Buttons.Right;
            else if (rel is < -0.4f and > -2.75f)
                buttons |= Buttons.Left;
        }
        ApplyCmd(p, new InputCmd
        {
            Seq = ++b.Seq,
            Buttons = buttons,
            Yaw = yaw,
            Pitch = Math.Clamp(pitch, -1.5f, 1.5f),
            Weapon = p.Weapon,
            SpeedPct = (byte)Math.Clamp((int)(FrenzyBuffs.Mobility(p, def) * 100), 1, 200), // the LMG frenzy buff
        });
    }

    private readonly Dictionary<byte, ushort> _zBotTarget = new();
}
