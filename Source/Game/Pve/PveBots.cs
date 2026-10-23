using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Co-op bots on the players' side. With an enemy in a clear line of fire within 60 m, a bot fights it itself (aim with
/// its skill's turn speed, fire through the normal shot path, so its hits land on enemies like a person's, back off from
/// rushers, keep heading for the objective when the enemy is far); with nobody in sight it hands the tick to the normal
/// bot brain, which routes it to the objective (<see cref="CoopModule.BotObjective"/>) along the nav graph.
/// </summary>
public sealed partial class Game
{
    private readonly Dictionary<byte, ushort> _pveBotTarget = new();

    /// <summary>Server: a co-op bot's tick. False: no enemy to fight, let the normal brain walk it to the objective.</summary>
    internal bool PveBotTick(Player p, BotBrain b, double now)
    {
        if (!p.Alive)
            return false;
        var def = p.WeaponDef;
        if (b.LifeTick != p.SpawnTick)
        {
            b.LifeTick = p.SpawnTick;
            b.Ammo = def.Mag;
            b.ReloadUntil = 0;
            _pveBotTarget.Remove(p.Id);
        }
        if (b.ReloadUntil > 0 && now >= b.ReloadUntil)
        {
            b.Ammo = def.Mag;
            b.ReloadUntil = 0;
        }

        // The target: the nearest enemy in a clear line of fire (looked for a few times a second).
        PveEnemy? target = null;
        if (now >= b.NextScan || !_pveBotTarget.ContainsKey(p.Id))
        {
            b.NextScan = now + 0.2;
            var best = float.MaxValue;
            foreach (var e in Enemies)
            {
                if (!e.Alive)
                    continue;
                var d = Vector3.DistanceSquared(e.Position, p.Position);
                if (d < best && d < 60f * 60f && ClearRay(p.Eye, e.Position + new Vector3(0, e.Crouched ? 0.9f : 1.3f, 0)))
                {
                    best = d;
                    target = e;
                }
            }
            if (target != null)
                _pveBotTarget[p.Id] = target.Id;
            else
                _pveBotTarget.Remove(p.Id);
        }
        else if (_pveBotTarget.TryGetValue(p.Id, out var tid))
        {
            target = EnemyById(tid);
            if (target is not { Alive: true })
            {
                _pveBotTarget.Remove(p.Id);
                target = null;
            }
        }
        if (target == null)
            return false; // nobody to shoot: the normal brain walks it to the objective

        var yaw = p.Yaw;
        var pitch = p.Pitch;
        var aimAt = target.Position + new Vector3(0, target.Crouched ? 0.9f : b.Skill >= BotDifficulty.Hard ? 1.55f : 1.25f, 0);
        var to = aimAt - p.Eye;
        var wantYaw = MathF.Atan2(to.X, -to.Z);
        var wantPitch = MathF.Atan2(to.Y, PFlat(to));
        var turn = b.TurnSpeed * World.TickDt;
        yaw = World.WrapAngle(yaw + Math.Clamp(World.WrapAngle(wantYaw - yaw), -turn, turn));
        pitch += Math.Clamp(wantPitch - pitch, -turn, turn);
        var aimed = MathF.Abs(World.WrapAngle(wantYaw - yaw)) < 0.05f && MathF.Abs(wantPitch - pitch) < 0.05f;

        if (b.Ammo <= 0 && b.ReloadUntil == 0)
            BotReload(p, def, now);
        var dist = PFlat(target.Position - p.Position);
        if (aimed && b.Ammo > 0 && b.ReloadUntil == 0 && now >= b.NextFire && DamageEnabled)
        {
            var scoped = dist > 10f;
            var spread = def.HipSpreadRad * (scoped ? def.AdsSpreadMult : 1f);
            ServerResolveShot(p, def, p.Eye, World.Forward(yaw, pitch), spread, PveNext(), ServerTick, now);
            b.Ammo--;
            b.NextFire = now + def.FireInterval + (def.Mode == FireMode.Semi ? b.TriggerDelay : 0);
        }

        // Move: back off from anyone at arm's length, close in on the objective when the fight is far, else hold and strafe a little.
        Vector3? goal = null;
        if (dist < 4f)
        {
            var away = p.Position - target.Position;
            away.Y = 0;
            goal = p.Position + (away.LengthSquared() > 1e-4f ? Vector3.Normalize(away) : World.FlatForward(p.Yaw)) * 3f;
        }
        else if (Module.BotObjective(this, p) is { } obj && PFlat(obj - p.Position) > 8f && dist > 25f && ClearRay(p.Position + Vector3.UnitY, obj + Vector3.UnitY))
            goal = obj;
        var buttons = Buttons.None;
        if (goal is { } g && PFlat(g - p.Position) > 0.4f)
        {
            var dir = g - p.Position;
            var rel = World.WrapAngle(MathF.Atan2(dir.X, -dir.Z) - yaw);
            if (MathF.Abs(rel) < 1.2f)
                buttons |= Buttons.Forward;
            else if (MathF.Abs(rel) > 1.95f)
                buttons |= Buttons.Back;
            if (rel is > 0.4f and < 2.75f)
                buttons |= Buttons.Right;
            else if (rel is < -0.4f and > -2.75f)
                buttons |= Buttons.Left;
        }
        else if (((int)(now * 0.7) + p.Id) % 3 == 0)
            buttons |= ((int)(now * 1.3) & 1) == 0 ? Buttons.Left : Buttons.Right; // a little strafing
        ApplyCmd(p, new InputCmd
        {
            Seq = ++b.Seq,
            Buttons = buttons,
            Yaw = yaw,
            Pitch = Math.Clamp(pitch, -1.5f, 1.5f),
            Weapon = p.Weapon,
            SpeedPct = 100,
        });
        b.LastHealth = p.Health;
        return true;
    }
}
