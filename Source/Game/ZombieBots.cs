using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Zombies: co-op bots and the downed state's controls. A bot holds the area it started in: it shoots the nearest zombie
/// it can see (through windows too), backs off from zombies that get close, revives downed teammates and rebuilds
/// boards when it's quiet, and spends its points when it's safe (ZombieBotsShop.cs, 2026-10-26). Everything a bot does
/// goes through the same input commands, shots and interact key as a person.
/// </summary>
public sealed partial class Game
{
    /// <summary>What a downed player can't do: jump (so no mantling), sprint, crouch or stand up.</summary>
    private const Buttons ZDownedStrip = Buttons.Jump | Buttons.Sprint | Buttons.Crouch;

    /// <summary>
    /// Downed in Zombies: lying on the floor, crawling (2026-10-23): the move keys still work, at
    /// <see cref="ZombiesRules.CrawlSpeed"/> of the prone speed; no jump, mantle, sprint, crouch or standing up. Both the
    /// client's prediction and the server run this, so the crawl is predicted like any movement.
    /// </summary>
    private InputCmd ZombieInput(Player? p, InputCmd cmd)
    {
        if (p == null || Mode != GameMode.Zombies || !IsDowned(p))
            return cmd;
        cmd.Buttons = (cmd.Buttons & ~ZDownedStrip) | Buttons.Prone;
        var cap = (byte)Math.Clamp((int)MathF.Round(ZRules.CrawlSpeed * 100f), 1, 200);
        if (cmd.SpeedPct == 0 || cmd.SpeedPct > cap)
            cmd.SpeedPct = cap;
        return cmd;
    }

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
            BotShop(p).Clear(); // (a new life: a new shopping plan)
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
            ZombieBotCrawl(p, b, yaw, pitch); // crawl away from a zombie that's close, else towards the nearest teammate standing
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
                if (b.Insane && d > 36f && ZTargetTaken(p, z.Id))
                    d *= 2.5f; // Insane: spread the fire, a zombie a teammate already shoots at counts as further away
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
        if (downed != null && b.Insane && ZCloserReviver(p, downed))
            downed = null; // Insane: the nearest bot revives, the rest keep the zombies off them
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
        // Shopping (ZombieBotsShop.cs): only when nothing above needs doing; it's safe, it decides itself.
        var shopping = false;
        if (goal == null && !use && downed == null && ZombieBotShop(p, b, zp, nearD, now, out var shopGoal, out var shopUse))
        {
            shopping = true;
            goal = shopGoal;
            use = shopUse;
        }
        if (goal == null && !use && !shopping && Flat(b.HoldAnchor - p.Position) > 2.5f)
            goal = ZMap is { } home ? BotWaypoint(home, p.Position, b.HoldAnchor, home.AreaAt(b.HoldAnchor)) : b.HoldAnchor; // back to its post, through the doorways
        ServerSetUse(p, use, now);

        // Aim: at the target's chest (hard bots go for the head), with a little error that settles.
        var aimed = false;
        if (target != null)
        {
            var aimAt = target.Position + new Vector3(0, b.Skill >= BotDifficulty.Hard ? 1.6f : 1.25f, 0);
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
            var scoped = Flat(target.Position - p.Position) > 8f;
            var spread = def.HipSpreadRad * (scoped ? def.AdsSpreadMult : 1f) * FrenzyBuffs.ConeMult(p, def, scoped ? 1f : 0f); // the frenzy buffs
            ServerResolveShot(p, def, p.Eye, dir, spread, ZNext(), ServerTick, now);
            if (!InfiniteAmmoOn(now))
                b.Ammo--; // (Infinite Ammo: the magazine stays full)
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
        ZombieBotStuck(p, b, goal is { } want && Flat(want - p.Position) > 0.4f, ref buttons, now); // stuck too long: hop, strafe, then join a teammate (BotStuck.cs)
        ApplyCmd(p, new InputCmd
        {
            Seq = ++b.Seq,
            Buttons = buttons,
            Yaw = yaw,
            Pitch = Math.Clamp(pitch, -1.5f, 1.5f),
            Weapon = p.Weapon,
            SpeedPct = (byte)Math.Clamp((int)(FrenzyBuffs.Mobility(p, def) * ZombieMoveMult(p) * 100), 1, 200), // the LMG frenzy buff; Fleetfoot Fizz
        });
    }

    /// <summary>
    /// A downed bot crawls (2026-10-23): away from a chasing zombie within 4 m, else towards the nearest teammate who's
    /// standing until it's well inside their revive reach, else it lies still. Straight lines only (it's slow, and walls
    /// and window openings stop it like anyone); it doesn't shoot.
    /// </summary>
    private void ZombieBotCrawl(Player p, BotBrain b, float yaw, float pitch)
    {
        Vector3? goal = null;
        Zombie? close = null;
        var closeD = 4f * 4f;
        foreach (var z in Horde)
        {
            if (z.State is not (ZState.Chase or ZState.Attack))
                continue;
            var dx = z.Position.X - p.Position.X;
            var dz = z.Position.Z - p.Position.Z;
            if (dx * dx + dz * dz < closeD)
            {
                closeD = dx * dx + dz * dz;
                close = z;
            }
        }
        if (close != null)
        {
            var away = p.Position - close.Position;
            away.Y = 0;
            if (away.LengthSquared() > 1e-4f)
                goal = p.Position + Vector3.Normalize(away) * 3f;
        }
        else
        {
            Player? mate = null;
            var best = float.MaxValue;
            foreach (var o in Players.Values)
            {
                if (o == p || !IsStanding(o) || (ZHostSitsOut && o.Id == LocalId))
                    continue;
                var d = Vector3.DistanceSquared(o.Position, p.Position);
                if (d < best || (d == best && mate != null && o.Id < mate.Id))
                {
                    best = d;
                    mate = o;
                }
            }
            if (mate != null && Flat(mate.Position - p.Position) > ZRules.ReviveRange * 0.6f)
                goal = mate.Position;
        }
        var buttons = Buttons.None;
        if (goal is { } g)
        {
            var want = MathF.Atan2(g.X - p.Position.X, -(g.Z - p.Position.Z));
            yaw = World.WrapAngle(yaw + Math.Clamp(World.WrapAngle(want - yaw), -0.12f, 0.12f));
            if (MathF.Abs(World.WrapAngle(want - yaw)) < 0.6f)
                buttons |= Buttons.Forward;
        }
        ApplyCmd(p, new InputCmd { Seq = ++b.Seq, Buttons = buttons, Yaw = yaw, Pitch = Math.Clamp(pitch, -1.5f, 1.5f), Weapon = p.Weapon, SpeedPct = 100 });
    }

    private readonly Dictionary<byte, ushort> _zBotTarget = new();

    /// <summary>Insane co-op: is another Insane bot already shooting at zombie <paramref name="id"/>?</summary>
    private bool ZTargetTaken(Player p, ushort id)
    {
        foreach (var o in Players.Values)
            if (o != p && o.Alive && o.Bot is { Insane: true } && _zBotTarget.TryGetValue(o.Id, out var t) && t == id)
                return true;
        return false;
    }

    /// <summary>Insane co-op: is another bot (up and alive) closer to the downed player than this one?</summary>
    private bool ZCloserReviver(Player p, Player downed)
    {
        var mine = Vector3.DistanceSquared(p.Position, downed.Position);
        foreach (var o in Players.Values)
            if (o != p && o != downed && o.Alive && o.Bot != null && !IsDowned(o) && Vector3.DistanceSquared(o.Position, downed.Position) < mine)
                return true;
        return false;
    }
}
