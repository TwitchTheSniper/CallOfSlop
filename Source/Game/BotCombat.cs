using System.Numerics;

namespace CallOfSlop;

/// <summary>Where a bot wants to go this tick (world space) and what it wants to look at, from Game.BotSteer.</summary>
public struct BotIntent
{
    public Vector3 Wish; // flat, not normalised (zero = stand still)
    public bool Sprint;
    public bool Mantle; // the path climbs a ledge right here: Jump + Forward, facing the path
    public bool Stationary; // standing still on purpose (holding a spot), so not "stuck"
    public bool HasLook;
    public Vector3 Look; // a point to face when there's no enemy in sight
}

/// <summary>The bot's view angles for this tick and how its shot lines up, from Game.BotAim.</summary>
public struct BotView
{
    public float Yaw, Pitch;
    public float Error; // radians off the aim point (large when nothing's in sight)
    public float Distance; // to the target (0 when nothing's in sight)
    public bool Ads;
}

/// <summary>Bot perception, aim, shooting and grenades.</summary>
public sealed partial class Game
{
    // ---- Aim tuning (see the self-test's headshot band) ----
    private const float BotAimHeight = 0.48f; // fraction of the target's height: mid-chest, well below the head
    private const float BotHeadHeight = 0.88f; // where a deliberate head shot aims
    private const double BotHeadChance = 0.004; // per tick, Hard only
    private const float BotKickPitchScale = 0.6f; // bots soak up some of the climb
    private const float BotPullDown = 1.8f; // high aim error settles this much faster (bots drag back down to the chest)

    /// <summary>Picks and keeps track of a target; hears gunfire; notices being hit. Scans a few times a second.</summary>
    private Player? BotPerceive(Player p, BotBrain b, double now, out bool visible)
    {
        // A new life: reset what doesn't carry over.
        if (p.SpawnTick != b.LifeTick)
        {
            b.LifeTick = p.SpawnTick;
            b.Mode = BotMode.Roam;
            b.HoldUntil = 0;
            b.GoalUntil = 0;
            b.CoverReload = false;
            b.StuckCount = 0;
            b.LastHealth = p.Health;
            b.LastSeenTime = b.HeardTime = b.HurtTime = -100;
            b.ObjectiveMinded = _rng.NextDouble() < b.Profile.ObjectiveWeight;
            b.ClearPath();
        }

        var target = Players.GetValueOrDefault(b.Target);
        if (target != null && (!target.Alive || !IsEnemy(p, target)))
        {
            target = null;
            b.Target = byte.MaxValue;
        }

        // Shot? Whoever just fired nearby is the likely culprit: turn towards them.
        var hurt = p.Health < b.LastHealth;
        if (hurt)
            b.HurtTime = now;
        if (hurt && target == null)
            b.NextScan = Math.Min(b.NextScan, now); // look around now (or next tick, if this tick's scans are used up)

        if ((now >= b.NextScan || (hurt && target == null)) && TakeBotScan()) // rationed per tick (BotMove.cs)
        {
            b.NextScan = now + 0.2 + _rng.NextDouble() * 0.1;
            var fwd = World.FlatForward(p.Yaw);
            var alert = now - b.HurtTime < 0.6; // just got hit: looks all around
            Player? best = null;
            var bestScore = float.MaxValue;
            var heardDist = now - b.HeardTime < 1 ? Vector3.Distance(p.Position, b.HeardAt) : float.MaxValue;
            var hearing = 20f + 30f * b.Profile.Aggression;
            foreach (var o in Players.Values)
            {
                if (o == p || !o.Alive || !IsEnemy(p, o))
                    continue;
                var to = o.Position - p.Position;
                var dist = to.Length();
                // Gunfire carries: remember the closest shooter we heard.
                if (now - o.LastShotTime < 0.4 && dist < MathF.Max(hearing, alert ? 60f : 0f) && dist < heardDist)
                {
                    heardDist = dist;
                    b.HeardAt = o.Position;
                    b.HeardTime = now;
                }
                var flat = Vector3.Normalize(new Vector3(to.X, 0, to.Z) + new Vector3(1e-4f, 0, 0));
                var inView = alert || Vector3.Dot(flat, fwd) > -0.2f || dist < Perks.BotSenseRange(o.Perks); // ~200 deg vision, plus "hearing" up close (Dead Silence: closer)
                if (!inView || !HasLineOfSight(p.Eye, o.Chest))
                    continue;
                var score = dist + (o.Id == b.Target ? -5f : 0f); // stick with the current target a little
                if (score < bestScore)
                {
                    bestScore = score;
                    best = o;
                }
            }

            if (best != null && best.Id != b.Target)
            {
                b.TargetSince = now;
                b.AimErrYaw = ((float)_rng.NextDouble() - 0.5f) * 2 * b.AimError * 3;
                b.AimErrPitch = ((float)_rng.NextDouble() - 0.5f) * 2 * b.AimError * 2;
            }
            b.Target = best?.Id ?? byte.MaxValue;
            target = best;
        }

        visible = target != null && HasLineOfSight(p.Eye, target.Chest);
        if (visible)
        {
            b.LastSeenAt = target!.Position;
            b.LastSeenTime = now;
        }
        return target;
    }

    /// <summary>Turns towards the target (with human-ish error that settles) or wherever steering wants to look.</summary>
    private BotView BotAim(Player p, BotBrain b, Player? target, bool visible, WeaponDef def, in BotIntent steer, double now)
    {
        var dt = World.TickDt;
        var aim = new BotView { Error = 10 };
        float wantYaw = p.Yaw, wantPitch = 0;
        if (visible)
        {
            var stance = target!.Sim.Stance;
            var h = World.HeightOf(stance);
            var point = target.Position + new Vector3(0, h * (stance == Stance.Prone ? 0.5f : BotAimHeight), 0) + target.Sim.Velocity * 0.08f;
            if (b.Skill == BotDifficulty.Hard && stance != Stance.Prone && _rng.NextDouble() < BotHeadChance)
                point.Y = target.Position.Y + h * BotHeadHeight; // now and then they go for the head
            var d = point - p.Eye;
            aim.Distance = d.Length();
            d /= aim.Distance;
            b.AimErrYaw *= MathF.Exp(-b.Settle * dt);
            b.AimErrPitch *= MathF.Exp(-b.Settle * (b.AimErrPitch > 0 ? BotPullDown : 1f) * dt);
            var wobble = b.AimError * 0.15f;
            wantYaw = MathF.Atan2(d.X, -d.Z) + b.AimErrYaw + ((float)_rng.NextDouble() - 0.5f) * wobble;
            wantPitch = MathF.Asin(Math.Clamp(d.Y, -1f, 1f)) + b.AimErrPitch;
        }
        else if (steer.HasLook)
        {
            var d = steer.Look - p.Eye;
            var flat = MathF.Sqrt(d.X * d.X + d.Z * d.Z);
            if (flat > 0.3f)
            {
                wantYaw = MathF.Atan2(d.X, -d.Z);
                wantPitch = Math.Clamp(MathF.Atan2(d.Y, flat), -0.5f, 0.5f);
            }
        }
        else if (steer.Wish.LengthSquared() > 0.01f)
        {
            wantYaw = MathF.Atan2(steer.Wish.X, -steer.Wish.Z);
        }

        var maxTurn = b.TurnSpeed * dt;
        aim.Yaw = World.WrapAngle(p.Yaw + Math.Clamp(World.WrapAngle(wantYaw - p.Yaw), -maxTurn, maxTurn));
        aim.Pitch = p.Pitch + Math.Clamp(wantPitch - p.Pitch, -maxTurn, maxTurn);
        if (visible)
            aim.Error = MathF.Abs(World.WrapAngle(wantYaw - aim.Yaw)) + MathF.Abs(wantPitch - aim.Pitch);
        aim.Ads = visible && aim.Distance > 14f && def.AdsZoom >= 1.4f;
        return aim;
    }

    /// <summary>Reloads, melees up close, and fires when the aim has settled on the target.</summary>
    private void BotShoot(Player p, BotBrain b, Player? target, bool visible, WeaponDef def, in BotView aim, double now)
    {
        if (b.Ammo <= 0 && b.ReloadUntil == 0)
        {
            BotReload(p, def, now);
            b.CoverReload = visible && _rng.NextDouble() < b.Profile.CoverUse;
        }
        else if (!visible && now - b.LastSeenTime > 2 && b.Ammo < def.Mag * 0.4f && b.ReloadUntil == 0)
        {
            BotReload(p, def, now);
        }

        var dist = aim.Distance;
        var tolerance = MathF.Atan2(0.45f, MathF.Max(dist, 1f)) + 0.02f;
        var ready = visible && now - b.TargetSince >= b.Reaction && aim.Error < tolerance;
        // Right up close: melee instead of shooting.
        var meleed = visible && DamageEnabled && now - b.TargetSince >= b.Reaction && aim.Error < 0.5f
                     && BotTryMelee(p, b, target!, dist, aim.Yaw, aim.Pitch, now);
        if (meleed || !(ready || b.BurstLeft > 0) || now < b.NextFire || b.Ammo <= 0 || b.ReloadUntil != 0 || !DamageEnabled
            || (dist >= def.RangeM * 3f && def.Class != WeaponClass.Sniper))
            return;

        var spread = def.HipSpreadRad * (aim.Ads ? def.AdsSpreadMult : Perks.SpreadMult(p.Perks, 0f)) + (b.Skill == BotDifficulty.Easy ? 0.02f : 0f); // Steady Aim
        ServerResolveShot(p, def, p.Eye, World.Forward(aim.Yaw, aim.Pitch), spread, (uint)_rng.Next(), ServerTick, now);
        b.Ammo--;
        b.NextFire = now + def.FireInterval * ZombieFireIntervalMult(p);
        BotStats.Shots[(int)b.Personality]++;
        BotStats.ShotDistance[(int)b.Personality] += dist;
        switch (def.Mode)
        {
            case FireMode.Semi:
                b.NextFire += b.TriggerDelay + _rng.NextDouble() * b.TriggerDelay;
                break;
            case FireMode.Burst:
                b.BurstLeft = b.BurstLeft > 0 ? b.BurstLeft - 1 : def.BurstCount - 1;
                if (b.BurstLeft == 0)
                    b.NextFire += def.BurstDelay + b.TriggerDelay;
                break;
        }
        // Recoil nudges their aim off target; they have to pull it back (and they pull down harder than up).
        var (kickPitch, kickYaw) = Recoil.BotKick(def, _rng);
        b.AimErrPitch += kickPitch * BotKickPitchScale;
        b.AimErrYaw += kickYaw;
    }

    /// <summary>Grenades: at someone in mid range now and then, and (grenadiers) at where an enemy ducked out of sight.</summary>
    private void BotGrenade(Player p, BotBrain b, Player? target, bool visible, float dist, double now)
    {
        if (b.Skill == BotDifficulty.Easy || p.Grenade == ThrowableKind.None || p.GrenadesLeft <= 0 || now < p.NextThrow)
            return;
        var rate = (b.Skill == BotDifficulty.Hard ? 0.006 : 0.003) * b.Profile.GrenadeRate;
        if (visible && dist is > 6f and < 15f && _rng.NextDouble() < rate)
        {
            BotThrowAt(p, b, target!.Position, now);
            return;
        }
        // Flush them out: lob one where they were last seen, a second or few after they disappeared.
        var lost = now - b.LastSeenTime;
        if (!visible && b.Profile.GrenadeRate >= 1.5f && lost is > 1 and < 4)
        {
            var d = Vector3.Distance(p.Position, b.LastSeenAt);
            if (d is > 8f and < 22f && _rng.NextDouble() < rate * 0.5)
                BotThrowAt(p, b, b.LastSeenAt, now);
        }
    }

    private void BotThrowAt(Player p, BotBrain b, Vector3 at, double now)
    {
        // Pick the launch angle that makes it go off at the target (flat-ground flight with bounces and roll,
        // Throwables.AimPitch), so the aim follows each throwable's own speed and fuse.
        var to = at - p.Position;
        Throwables.AimPitch(Throwables.Get(p.Grenade), p.Eye, at, out var launch);
        var throwYaw = MathF.Atan2(to.X, -to.Z) + ((float)_rng.NextDouble() - 0.5f) * b.AimError;
        var dir = World.Forward(throwYaw, launch);
        ServerThrow(p, p.Grenade, Throwables.LaunchPoint(p.Eye, dir), dir, now);
    }

    /// <summary>Turns the steering result into buttons: direction keys relative to the view, sprint, jump/mantle and stance.</summary>
    private Buttons BotButtons(Player p, BotBrain b, bool visible, WeaponDef def, in BotIntent steer, in BotView aim, double now)
    {
        var buttons = Buttons.None;
        var wish = steer.Wish;
        var prof = b.Profile;
        var dist = aim.Distance;

        if (steer.Sprint && !visible)
            buttons |= Buttons.Sprint;
        if (steer.Mantle)
            buttons |= Buttons.Jump | Buttons.Forward;
        if (b.JumpNext && p.Sim.Grounded)
        {
            buttons |= Buttons.Jump | Buttons.Forward; // forward too, so a ledge in front becomes a mantle
            b.JumpNext = false;
        }
        if (visible && _rng.NextDouble() < (prof.Kind is BotPersonality.Rusher or BotPersonality.Chaotic ? 0.006 : 0.002) && b.Skill != BotDifficulty.Easy)
            buttons |= Buttons.Jump; // hop in a fight now and then

        // Stance: steady long shots by crouching; marksmen (and hard snipers) go prone. Stand up to move.
        var longGun = def.Class is WeaponClass.Sniper or WeaponClass.DMR or WeaponClass.LMG;
        if (visible && dist > 16f && longGun && b.Skill != BotDifficulty.Easy)
        {
            var prone = (prof.GoesProne && def.Class is WeaponClass.Sniper or WeaponClass.DMR && dist > 22f)
                        || (b.Skill == BotDifficulty.Hard && def.Class == WeaponClass.Sniper && dist > 24f);
            if (prone)
            {
                buttons |= Buttons.Prone;
                wish = Vector3.Zero; // stay put while prone
            }
            else
            {
                buttons |= Buttons.Crouch;
            }
            buttons &= ~(Buttons.Jump | Buttons.Sprint);
        }
        else if (visible && _rng.NextDouble() < prof.CrouchPeek * World.TickDt * (b.Skill == BotDifficulty.Easy ? 0.4f : 1f))
        {
            b.CrouchUntil = now + 0.4 + _rng.NextDouble() * 0.6; // crouch-peek
        }

        // Reloading in a fight: if ducking puts something between us, duck.
        if (visible && b.ReloadUntil > 0 && now >= b.NextCoverCrouchCheck && Players.GetValueOrDefault(b.Target) is { } t)
        {
            b.NextCoverCrouchCheck = now + 0.3;
            b.CrouchCover = !HasLineOfSight(p.Position + new Vector3(0, World.EyeOf(Stance.Crouch), 0), t.Eye);
        }
        else if (!visible || b.ReloadUntil == 0)
        {
            b.CrouchCover = false;
        }
        if (b.CrouchCover || now < b.CrouchUntil || (steer.Stationary && b.Mode == BotMode.Cover))
            buttons |= Buttons.Crouch;
        else if (steer.Stationary && !visible && prof.Kind == BotPersonality.Marksman)
            buttons |= Buttons.Crouch; // waiting on a perch
        if ((buttons & Buttons.Crouch) != 0)
            buttons &= ~Buttons.Sprint;

        // World-space wish -> buttons relative to where the bot is facing.
        if (wish.LengthSquared() > 0.01f)
        {
            wish = Vector3.Normalize(wish);
            var f = Vector3.Dot(wish, World.FlatForward(aim.Yaw));
            var r = Vector3.Dot(wish, World.FlatRight(aim.Yaw));
            if (f > 0.35f) buttons |= Buttons.Forward;
            if (f < -0.35f) buttons |= Buttons.Back;
            if (r > 0.35f) buttons |= Buttons.Right;
            if (r < -0.35f) buttons |= Buttons.Left;
            if (f < 0.5f)
                buttons &= ~Buttons.Sprint; // only sprint roughly forwards
        }
        return buttons;
    }
}
