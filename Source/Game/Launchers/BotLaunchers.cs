using System.Numerics;

namespace CallOfSlop;

/// <summary>A bot's launcher state (BotLaunchers.cs).</summary>
public sealed partial class BotBrain
{
    /// <summary>The enemy machine (streak entity id) or the player at the middle of a group it's taking the launcher to (255 = none).</summary>
    public byte LauncherEntity = byte.MaxValue, LauncherPlayer = byte.MaxValue;

    public double LauncherSince, LauncherNextCheck, LauncherFireAt;
    public uint LauncherLife = uint.MaxValue;
}

/// <summary>
/// Bots and launchers. Some bots carry a launcher as their secondary (their own roll, so a match's shared random
/// sequence is untouched). They bring it out for an enemy's kill streak machine they can see (a lock-on launcher locks
/// an airborne one first; other launchers lead it) and, on Hard and up, for two or more enemies bunched together at
/// a safe distance (never point blank: a rocket's own blast hurts). One shot per engagement, then back to the main gun.
/// </summary>
public sealed partial class Game
{
    /// <summary>Closest a bot fires a launcher at a player (its own blast) and how close the second enemy of a "group" must be.</summary>
    public const float BotLauncherMinRange = 12f, BotLauncherGroup = 4.5f;

    /// <summary>Bot loadouts: about a quarter of bots (four in ten tacticians) carry a launcher instead of a pistol; never on Easy.</summary>
    private void BotPickLauncher(Player bot, BotProfile prof, int level)
    {
        if (bot.Bot is not { } b || b.Skill == BotDifficulty.Easy)
            return;
        var chance = prof.Kind == BotPersonality.Tactician ? 0.4 : 0.25;
        if (TacticalGear.Roll(bot.Id, bot.SpawnTick, 0x1A0) >= chance)
            return;
        WeaponDef? pick = null;
        var n = 0;
        foreach (var w in Weapons.All)
            if (w.IsLauncher && BotGear.Weapon(w, level)) // only launchers the gear level has unlocked (BotGear.cs)
                n++;
        if (n == 0)
            return;
        var want = (int)(TacticalGear.Roll(bot.Id, bot.SpawnTick, 0x1A1) * n);
        var lockOn = TacticalGear.Roll(bot.Id, bot.SpawnTick, 0x1A2) < 0.45; // the anti-air launcher, more often than its share
        foreach (var w in Weapons.All)
        {
            if (!w.IsLauncher || !BotGear.Weapon(w, level))
                continue;
            if (lockOn && w.LauncherGuidance == LauncherGuidance.LockOn)
            {
                pick = w;
                break;
            }
            if (want-- == 0)
                pick ??= w;
        }
        if (pick == null)
            return;
        bot.WantSecondary = pick.Id;
        bot.WantSecondaryKit = 0;
    }

    private void BotLauncherDisengage(Player p, BotBrain b, double now, double pause)
    {
        b.LauncherEntity = b.LauncherPlayer = byte.MaxValue;
        b.LauncherNextCheck = now + pause;
        if (p.Weapon != p.Primary)
            p.Weapon = p.Primary;
    }

    /// <summary>
    /// Every bot tick, after aiming: picks a launcher target now and then, brings the launcher out, aims (leading a moving
    /// machine, arcing a grenade), waits for the lock or the draw, fires once. Returns true while the launcher is in use
    /// (the normal shooting is skipped; <paramref name="aim"/> is replaced by the launcher's aim).
    /// </summary>
    private bool BotLauncherTick(Player p, BotBrain b, Player? target, bool visible, ref BotView aim, double now)
    {
        var def = p.DefFor(p.Secondary);
        if (!def.IsLauncher || !DamageEnabled)
        {
            if (p.Weapon != p.Primary && Weapons.Get(p.Weapon).IsLauncher)
                p.Weapon = p.Primary;
            return false;
        }
        if (b.LauncherLife != p.SpawnTick)
        {
            b.LauncherLife = p.SpawnTick;
            b.LauncherEntity = b.LauncherPlayer = byte.MaxValue;
            b.LauncherNextCheck = now + 1.0;
        }
        var engaged = b.LauncherEntity != byte.MaxValue || b.LauncherPlayer != byte.MaxValue;
        if (!engaged)
        {
            if (p.Weapon != p.Primary)
                p.Weapon = p.Primary;
            if (now < b.LauncherNextCheck || b.ReloadUntil != 0 || LauncherRoundsLeft(p, def) <= 0)
                return false;
            b.LauncherNextCheck = now + 0.5;
            if (!BotLauncherPick(p, b, def, target, visible))
                return false;
            b.LauncherSince = now;
            p.Weapon = p.Secondary;
            var wait = def.EquipTime + (def.LauncherGuidance == LauncherGuidance.LockOn && b.LauncherEntity != byte.MaxValue ? def.LauncherLockTime : 0.3) + b.Reaction;
            b.LauncherFireAt = now + wait;
        }

        // Where to aim, and is the target still good?
        Vector3 at;
        Vector3 vel = Vector3.Zero;
        if (b.LauncherEntity != byte.MaxValue)
        {
            if (!StreakEntities.TryGetValue(b.LauncherEntity, out var e) || !HasLineOfSight(p.Eye, e.Hitbox.Center))
            {
                BotLauncherDisengage(p, b, now, 1.0);
                return false;
            }
            at = e.Hitbox.Center;
            if (e.Kind == StreakId.AttackDrone)
                vel = new Vector3(-MathF.Sin(e.OrbitAngle), 0, MathF.Cos(e.OrbitAngle)) * (e.OrbitDir * KillStreaks.DroneSpeed);
        }
        else
        {
            if (!Players.TryGetValue(b.LauncherPlayer, out var t) || !t.Alive || !HasLineOfSight(p.Eye, t.Chest)
                || Vector3.Distance(p.Position, t.Position) < BotLauncherMinRange)
            {
                BotLauncherDisengage(p, b, now, 1.5);
                return false;
            }
            at = t.Position + new Vector3(0, 0.3f, 0); // at their feet: the blast does the work
            vel = t.Sim.Velocity * 0.5f;
        }
        var (yaw, pitch) = BotLauncherAim(p.Eye, at, vel, def);
        var maxTurn = b.TurnSpeed * World.TickDt * BotStunTurn(p, now);
        aim.Yaw = World.WrapAngle(p.Yaw + Math.Clamp(World.WrapAngle(yaw - p.Yaw), -maxTurn, maxTurn));
        aim.Pitch = p.Pitch + Math.Clamp(pitch - p.Pitch, -maxTurn, maxTurn);
        aim.Error = MathF.Abs(World.WrapAngle(yaw - aim.Yaw)) + MathF.Abs(pitch - aim.Pitch);
        aim.Distance = Vector3.Distance(p.Eye, at);
        aim.Ads = true;
        if (now - b.LauncherSince > 6)
        {
            BotLauncherDisengage(p, b, now, 2); // couldn't line it up
            return false;
        }
        if (now < b.LauncherFireAt || aim.Error > 0.05f)
            return true;

        // Fire: the bot's aim error, skill-scaled, as the shot's direction.
        var h = (uint)(p.Id * 7919 + (int)(now * 1000));
        var err = b.AimError * 0.5f;
        var dir = World.Forward(aim.Yaw + ((float)TacticalGear.Roll(h, 1, 0x1A3) - 0.5f) * err, aim.Pitch + ((float)TacticalGear.Roll(h, 2, 0x1A3) - 0.5f) * err);
        var lockOn = def.LauncherGuidance == LauncherGuidance.LockOn && b.LauncherEntity != byte.MaxValue ? b.LauncherEntity : byte.MaxValue;
        _botLauncherShot = true;
        var fired = ServerFireLauncher(p, def, p.Eye, dir, def.MinSpreadRad, h | 1, lockOn, now);
        _botLauncherShot = false;
        BotStats.Shots[(int)b.Personality]++;
        BotLauncherDisengage(p, b, now, fired != null ? def.Reload + 1.0 : 1.0);
        return true;
    }

    /// <summary>A launcher target for the bot: an enemy machine in sight (flying ones only for lock-on launchers' lock), else a group.</summary>
    private bool BotLauncherPick(Player p, BotBrain b, WeaponDef def, Player? target, bool visible)
    {
        var range = def.LauncherGuidance == LauncherGuidance.LockOn ? def.LauncherLockRange : def.LauncherProjectile == LauncherProjectile.Rocket ? 80f : 40f;
        StreakEntity? best = null;
        var bestDist = float.MaxValue;
        foreach (var e in StreakEntities.Values)
        {
            if (!StreakMachineHostile(e, p))
                continue;
            var air = LauncherTargets.IsAirborne(e);
            if (air && def.LauncherProjectile != LauncherProjectile.Rocket)
                continue; // grenades can't reach a drone
            var d = Vector3.Distance(p.Eye, e.Hitbox.Center);
            if (d > range || d < 6f || d >= bestDist || !HasLineOfSight(p.Eye, e.Hitbox.Center))
                continue;
            best = e;
            bestDist = d;
        }
        if (best != null)
        {
            b.LauncherEntity = best.Id;
            return true;
        }
        if (b.Skill < BotDifficulty.Hard || !visible || target == null)
            return false;
        var dist = Vector3.Distance(p.Position, target.Position);
        if (dist < BotLauncherMinRange + 3f || dist > range * 0.7f)
            return false;
        foreach (var o in Players.Values)
            if (o != target && o.Alive && IsEnemy(p, o) && Vector3.Distance(o.Position, target.Position) < BotLauncherGroup)
            {
                b.LauncherPlayer = target.Id;
                return true;
            }
        return false;
    }

    /// <summary>Aim angles that put a launcher round on a point moving at <paramref name="vel"/>: lead by the flight time; grenades on their arc.</summary>
    public static (float Yaw, float Pitch) BotLauncherAim(Vector3 eye, Vector3 at, Vector3 vel, WeaponDef def)
    {
        var d0 = Vector3.Distance(eye, at);
        var speed = def.LauncherProjectile == LauncherProjectile.Rocket ? (def.LauncherSpeed + def.LauncherMaxSpeed) * 0.5f : def.LauncherSpeed;
        var lead = at + vel * (d0 / MathF.Max(speed, 1f));
        var to = lead - eye;
        var flat = MathF.Sqrt(to.X * to.X + to.Z * to.Z);
        var yaw = MathF.Atan2(to.X, -to.Z);
        var pitch = MathF.Atan2(to.Y, MathF.Max(flat, 1e-3f));
        var g = World.Gravity * def.LauncherGravity;
        if (g > 0.5f && flat > 0.5f)
        {
            // The low arc that lands on the point: tan = (v^2 - sqrt(v^4 - g (g x^2 + 2 y v^2))) / (g x).
            var v2 = speed * speed;
            var disc = v2 * v2 - g * (g * flat * flat + 2 * to.Y * v2);
            pitch = disc >= 0 ? MathF.Atan((v2 - MathF.Sqrt(disc)) / (g * flat)) : MathF.PI / 4;
        }
        return (yaw, Math.Clamp(pitch, -1.4f, 1.4f));
    }

    /// <summary>Self-test: one launcher decision tick for a bot (without the rest of its brain). Returns true while it uses the launcher.</summary>
    public bool SelfTestBotLauncher(Player bot, double now)
    {
        var aim = new BotView { Yaw = bot.Yaw, Pitch = bot.Pitch, Error = 10 };
        var busy = BotLauncherTick(bot, bot.Bot!, null, false, ref aim, now);
        bot.Yaw = aim.Yaw;
        bot.Pitch = aim.Pitch;
        return busy;
    }
}
