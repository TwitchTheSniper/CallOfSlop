using System.Numerics;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>A patch of burning napalm (Napalm Strike). Both sides make the same ones from the strike's plan; the server burns people in them.</summary>
public sealed class FireZone
{
    public byte Owner;
    public Vector3 Pos;
    public double Start, Until, NextTick;
}

/// <summary>Tuning and helpers for the kill streaks added later (Smoke Screen to Juggernaut). Definitions are in <see cref="All"/>.</summary>
public static partial class KillStreaks
{
    // ---- Smoke Screen ----
    public const int SmokeScreenClouds = 5;
    public const float SmokeScreenLength = 24f, SmokeScreenDelay = 1.2f;

    // ---- Ballistic Vests ----
    public const int VestArmor = 60;

    // ---- Hunter Drone ----
    public const int HunterHealth = 60;
    public const double HunterSeconds = 20, HunterRescan = 0.4;
    /// <summary>
    /// It cruises at <see cref="HunterCruiseSpeed"/> about <see cref="HunterClimb"/> m above its target, then dives at
    /// <see cref="HunterDiveSpeed"/> once it has a clear line down. The proximity fuse goes off within <see cref="HunterFuse"/>
    /// m of any enemy; the blast (175 at the centre, 5 m) kills from the fuse distance, but Flak Jacket (75% less) survives it.
    /// </summary>
    public const float HunterCruiseSpeed = 12f, HunterDiveSpeed = 19f, HunterTurnRate = 3f, HunterClimb = 9f, HunterFuse = 1.2f;
    public const float HunterRadius = 5f, HunterDamage = 175f;

    // ---- Supply Drop ----
    public const double CrateSeconds = 45;
    public const float CrateDropHeight = 30f, CrateFallSpeed = 10f, CrateTouch = 1.7f;

    // ---- Napalm Strike ----
    public const int NapalmHits = 7;
    public const float NapalmDelay = 2.4f, NapalmLength = 30f, NapalmSpeed = 70f, NapalmRadius = 3.5f, NapalmDamage = 45f;
    /// <summary>Each patch burns enemies within <see cref="FireRadius"/> m for <see cref="FireTickDamage"/> every <see cref="FireTick"/> s (40/s; Flak Jacket: 10/s).</summary>
    public const float FireRadius = 3.2f, FireTickDamage = 16f;
    public const double FireSeconds = 8, FireTick = 0.4;

    // ---- EMP Blast ----
    public const double EmpSeconds = 12;

    // ---- Gunship ----
    public const double GunshipSeconds = 35, GunshipInterval = 1.2, GunshipWarmup = 2.5;
    /// <summary>
    /// 750 hit points; bullets do 80% (a Vanguard AR's 24 -> 19: 40 hits, so a team brings it down fast) and explosives
    /// 150% (launchers are the hard counter).
    /// </summary>
    public const int GunshipHealth = 750;
    public const float GunshipBulletMult = 0.8f, GunshipExplosiveMult = 1.5f;
    public const float GunshipRange = 130f, GunshipSpeed = 9f, GunshipLift = 6f, GunshipRadius = 3.5f, GunshipDamage = 90f, GunshipSpread = 0.012f;

    // ---- Juggernaut ----
    public const int JuggernautArmor = 400;
    public const float JuggernautMove = 0.75f, JuggernautExplosiveMult = 1.5f;
    public const double JuggernautMaxSeconds = 600; // the effect's timer: in practice it ends when the armour breaks or you die

    /// <summary>Blast radius and centre damage of each impact of a strike (Smoke Screen: none).</summary>
    public static (float Radius, float Damage) StrikeBlast(StreakId id) => id switch
    {
        StreakId.MortarBarrage => (MortarRadius, MortarDamage),
        StreakId.StrafeRun => (StrafeRadius, StrafeDamage),
        StreakId.NapalmStrike => (NapalmRadius, NapalmDamage),
        _ => (0f, 0f),
    };

    /// <summary>The blast radius of a streak's explosions (strikes, Hunter Drone, gunship rounds); 0 for streaks that don't explode.</summary>
    public static float BlastRadiusOf(StreakId id) => id switch
    {
        StreakId.HunterDrone => HunterRadius,
        StreakId.Gunship => GunshipRadius,
        _ => StrikeBlast(id).Radius,
    };

    /// <summary>A line streak's marks: its length, how many impacts, and whether it runs across the way you face (Smoke Screen) or along it.</summary>
    public static (float Length, int Count, bool Across) LineShape(StreakId id) => id switch
    {
        StreakId.NapalmStrike => (NapalmLength, NapalmHits, false),
        StreakId.SmokeScreen => (SmokeScreenLength, SmokeScreenClouds, true),
        _ => (StrafeLength, StrafeHits, false),
    };

    /// <summary>Seconds after the call when the strike jet passes over the spot.</summary>
    public static double JetPassTime(StreakId id) => id == StreakId.NapalmStrike
        ? NapalmDelay + NapalmLength / 2 / NapalmSpeed
        : StrafeDelay + StrafeLength / 2 / StrafeSpeed;

    public static float JetSpeed(StreakId id) => id == StreakId.NapalmStrike ? NapalmSpeed : StrafeSpeed;

    /// <summary>How long a streak's world object lasts.</summary>
    public static double EntityLife(StreakId id) => id switch
    {
        StreakId.SentryTurret => TurretSeconds,
        StreakId.AttackDrone => DroneSeconds,
        StreakId.HunterDrone => HunterSeconds,
        StreakId.Gunship => GunshipSeconds,
        StreakId.SupplyDrop => CrateSeconds,
        _ => 30,
    };

    /// <summary>The targeting hint's second half for an aimed streak.</summary>
    public static string AimHint(StreakId id) => id switch
    {
        StreakId.StrafeRun or StreakId.NapalmStrike => "the run passes over the spot, along the way you face",
        StreakId.SmokeScreen => "the wall goes up across the spot",
        _ => "shells land around the spot",
    };
}

/// <summary>
/// The later kill streaks, server and both sides: Smoke Screen and Napalm Strike (strikes planned like the others),
/// Ballistic Vests and Juggernaut (armour in <see cref="StreakState"/>), Hunter Drone, Gunship and Supply Drop
/// (streak entities), EMP Blast (a timed effect that silences enemy machines, devices and intel).
/// Extra Msg.StreakEvent kinds: 2 = an effect ended early (the Juggernaut's armour broke), 3 = a crate was taken
/// (other = who took it), 4 = a streak explosion's visuals (yaw = its radius).
/// </summary>
public sealed partial class Game
{
    public readonly List<FireZone> FireZones = new();

    // =====================================================================
    // Server
    // =====================================================================

    /// <summary>Server: the instant streaks that do more than add a timed effect. Returns where it happened (for the event).</summary>
    private Vector3 ServerUseMoreStreak(Player p, StreakId id, float yaw, double now)
    {
        switch (id)
        {
            case StreakId.BallisticVests:
                // You and every living teammate (free-for-all: just you). A Juggernaut keeps the suit.
                foreach (var q in Players.Values)
                {
                    if (!q.Alive || (q != p && StreakIsEnemy(p, q)) || q.Streak.ArmorHeavy)
                        continue;
                    q.Streak.Armor = Math.Max(q.Streak.Armor, KillStreaks.VestArmor);
                    if (q != p)
                        SendStreakState(q);
                }
                return p.Position;
            case StreakId.Juggernaut:
                p.Streak.Armor = KillStreaks.JuggernautArmor;
                p.Streak.ArmorHeavy = true;
                return p.Position;
            case StreakId.EmpBlast:
                ServerEmp(p, now);
                return p.Position;
            case StreakId.HunterDrone:
                return SpawnHunter(p, yaw, now).Pos;
            case StreakId.Gunship:
                return SpawnGunship(p, now).Pos;
            case StreakId.SupplyDrop:
                return SpawnCrate(p, yaw, now).Pos;
            default:
                return p.Position; // Recon Sweep, Counter Sweep, Overdrive: just the effect (OnStreakCalled)
        }
    }

    /// <summary>EMP Blast: enemy Hunter Drones and tactical devices are destroyed at once; the rest is the timed effect.</summary>
    private void ServerEmp(Player p, double now)
    {
        List<StreakEntity>? fried = null;
        foreach (var e in StreakEntities.Values)
            if (e.Kind == StreakId.HunterDrone && Players.TryGetValue(e.Owner, out var o) && StreakIsEnemy(o, p))
                (fried ??= new()).Add(e);
        if (fried != null)
            foreach (var e in fried)
                RemoveStreakEntity(e, p.Id, now);
        for (var i = TacDevices.Count - 1; i >= 0; i--)
        {
            if (i >= TacDevices.Count)
                continue;
            var d = TacDevices[i];
            if (d.Owner != p.Id && Players.TryGetValue(d.Owner, out var o) && StreakIsEnemy(o, p))
                RemoveTacDevice(d, p.Id, now);
        }
    }

    /// <summary>Are <paramref name="owner"/>'s streak machines shut down by an enemy EMP Blast right now?</summary>
    public bool StreakMachinesDisabled(Player owner, double now)
    {
        foreach (var e in StreakEffects)
            if (e.Kind == StreakId.EmpBlast && now < e.Until && e.Owner != owner.Id && Players.TryGetValue(e.Owner, out var o) && StreakIsEnemy(o, owner))
                return true;
        return false;
    }

    /// <summary>Server, each tick: napalm burns enemies standing in it (walls between the fire and them, and roofs over it, shield them).</summary>
    private void ServerTickFireZones(double now)
    {
        if (FireZones.Count == 0)
            return;
        for (var i = 0; i < FireZones.Count; i++)
        {
            var z = FireZones[i];
            if (now < z.Start || now >= z.Until || now < z.NextTick)
                continue;
            z.NextTick = Math.Max(z.NextTick + KillStreaks.FireTick, now);
            if (!Players.TryGetValue(z.Owner, out var owner))
                continue;
            var flame = z.Pos + new Vector3(0, 0.5f, 0);
            foreach (var v in Players.Values)
            {
                if (!v.Alive || !StreakIsEnemy(owner, v))
                    continue;
                var dx = v.Position.X - z.Pos.X;
                var dz = v.Position.Z - z.Pos.Z;
                var dy = v.Position.Y - z.Pos.Y;
                if (dx * dx + dz * dz > KillStreaks.FireRadius * KillStreaks.FireRadius || dy < -1f || dy > 2f)
                    continue;
                var to = v.Chest - flame;
                var dist = to.Length();
                if (dist > 0.3f && World.Query.Raycast(flame, to / dist, dist, QueryFilter.All) < dist - 0.05f)
                    continue;
                _fireHits.Add(v);
            }
            foreach (var v in _fireHits) // (outside the loop over Players: a kill may change the match)
            {
                _deathCause = DeathCause.Fire; // napalm burns (the death reaction flails)
                if (DamagePlayer(owner, v, (int)KillStreaks.FireTickDamage, false, KillStreaks.KillId(StreakId.NapalmStrike), now, DamageKind.Explosive) && owner.Id == LocalId)
                    OnOwnShotConfirmed(5, now);
                _deathCause = DeathCause.Unknown;
            }
            _fireHits.Clear();
        }
    }

    private readonly List<Player> _fireHits = new();

    // ---- Armour (Ballistic Vests, Juggernaut) ----------------------------------------------------

    /// <summary>
    /// From DamagePlayer: armour soaks the hit first and returns what's left for health. Vests: body-shot bullets only.
    /// The Juggernaut suit: bullets (headshots too) and blasts, blasts at 1.5x. Melee always goes straight through.
    /// </summary>
    private int StreakArmorAbsorb(Player victim, int amount, bool head, DamageKind kind, double now)
    {
        var s = victim.Streak;
        if (s.Armor <= 0 || amount <= 0 || kind == DamageKind.Melee)
            return amount;
        if (!s.ArmorHeavy && (kind != DamageKind.Bullet || head))
            return amount;
        var mult = s.ArmorHeavy && kind == DamageKind.Explosive ? KillStreaks.JuggernautExplosiveMult : 1f;
        var cost = amount * mult;
        int left;
        if (cost <= s.Armor)
        {
            s.Armor = Math.Max(0, s.Armor - (int)MathF.Ceiling(cost));
            left = 0;
        }
        else
        {
            left = Math.Max(1, (int)MathF.Round((cost - s.Armor) / mult));
            s.Armor = 0;
        }
        if (s.Armor <= 0)
        {
            var wasHeavy = s.ArmorHeavy;
            s.Armor = 0;
            s.ArmorHeavy = false;
            if (wasHeavy)
                EndStreakEffect(victim.Id, StreakId.Juggernaut, now);
        }
        SendStreakState(victim);
        return left;
    }

    /// <summary>Is the Juggernaut suit on (server, and the local player's own copy)?</summary>
    public static bool JuggernautOn(Player p) => p.Alive && p.Streak.ArmorHeavy && p.Streak.Armor > 0;

    /// <summary>ApplyCmd: a Juggernaut moves slower. Bots are scaled exactly; people's own (already slowed) input is capped.</summary>
    private InputCmd StreakMoveFilter(Player p, InputCmd cmd)
    {
        if (!JuggernautOn(p))
            return cmd;
        if (p.Bot != null)
        {
            cmd.SpeedPct = (byte)Math.Clamp((int)MathF.Round(cmd.SpeedPct * KillStreaks.JuggernautMove), 1, 200);
            return cmd;
        }
        var fastest = Perks.MoveSpeed(p.Perks, MathF.Max(p.DefFor(p.Primary).Mobility, p.DefFor(p.Secondary).Mobility), false);
        var cap = (byte)Math.Clamp((int)MathF.Ceiling(fastest * KillStreaks.JuggernautMove * 100f) + 1, 1, 200);
        if (cmd.SpeedPct > cap)
            cmd.SpeedPct = cap;
        return cmd;
    }

    /// <summary>The local player's speed multiplier from streaks (Juggernaut: 0.75).</summary>
    private float LocalStreakMove() => Local is { } me && JuggernautOn(me) ? KillStreaks.JuggernautMove : 1f;

    /// <summary>Server: a timed effect ends early (the Juggernaut's armour broke): tell everyone.</summary>
    private void EndStreakEffect(byte owner, StreakId kind, double now)
    {
        KillStreaks.WriteEvent(_w, 2, owner, kind, Vector3.Zero, 0, 0, 255);
        Net.Broadcast(_w.Span, PacketReliability.ReliableOrdered);
        OnMoreStreakEvent(2, owner, kind, Vector3.Zero, 0, 255, now);
    }

    // ---- Hunter Drone, Gunship, Supply Drop --------------------------------------------------------

    private StreakEntity SpawnHunter(Player p, float yaw, double now)
    {
        var e = NewEntity(p, StreakId.HunterDrone, p.Eye + new Vector3(0, 0.8f, 0) + World.FlatForward(yaw) * 0.6f, now, KillStreaks.HunterSeconds);
        e.Vel = World.FlatForward(yaw) * 4f + new Vector3(0, 5f, 0); // a hop up and away first
        e.Yaw = e.RenderYaw = yaw;
        e.NextScan = now;
        return e;
    }

    private StreakEntity SpawnGunship(Player p, double now)
    {
        var map = World.Map;
        var e = NewEntity(p, StreakId.Gunship, Vector3.Zero, now, KillStreaks.GunshipSeconds);
        e.OrbitRadius = Math.Clamp(MathF.Min(map.HalfX, map.HalfZ) * 0.55f, 12f, 60f);
        e.OrbitAngle = MathF.Atan2(p.Position.Z, p.Position.X) + MathF.PI; // comes in from the far side
        e.OrbitDir = _rng.NextDouble() < 0.5 ? -1 : 1;
        e.Pos = e.RenderPos = GunshipPosition(e);
        e.NextShot = now + KillStreaks.GunshipWarmup;
        return e;
    }

    /// <summary>A crate drops from the sky a couple of metres in front of the caller (at their feet if that's inside a wall).</summary>
    private StreakEntity SpawnCrate(Player p, float yaw, double now)
    {
        var spot = p.Position + World.FlatForward(yaw) * 2.5f;
        var look = spot + Vector3.UnitY - p.Eye;
        var len = look.Length();
        if (World.Query.OverlapBody(spot, 1.1f, QueryFilter.StaticOnly) || World.Query.Raycast(p.Eye, look / len, len, QueryFilter.All) < len - 0.05f)
            spot = p.Position;
        // The ground under the spot (not a roof above it: it falls through the gap it came from, so look from just above head height).
        var down = World.Query.Raycast(spot + new Vector3(0, 1.5f, 0), -Vector3.UnitY, 6f, QueryFilter.All);
        var ground = down < 6f ? spot.Y + 1.5f - down : p.Position.Y;
        var e = NewEntity(p, StreakId.SupplyDrop, new Vector3(spot.X, ground + KillStreaks.CrateDropHeight, spot.Z), now, KillStreaks.CrateSeconds);
        e.GroundY = ground;
        e.Yaw = e.RenderYaw = yaw;
        e.Firing = true; // falling
        return e;
    }

    private Vector3 GunshipPosition(StreakEntity e)
    {
        var p = DronePosition(e);
        p.Y = MathF.Min(p.Y + KillStreaks.GunshipLift, 46f);
        return p;
    }

    /// <summary>Server tick for the later entities. Returns true when it's done (detonated or taken) and should go quietly.</summary>
    private bool TickMoreEntity(StreakEntity e, Player owner, double now, bool emp)
    {
        switch (e.Kind)
        {
            case StreakId.HunterDrone:
                TickHunter(e, owner, now, emp);
                break;
            case StreakId.Gunship:
                TickGunship(e, owner, now, emp);
                break;
            case StreakId.SupplyDrop:
                TickCrate(e, owner, now);
                break;
        }
        return e.Done;
    }

    private void TickHunter(StreakEntity e, Player owner, double now, bool emp)
    {
        var dt = World.TickDt;
        if (emp)
        {
            // Launched into an enemy EMP: it fizzles out harmlessly.
            e.Done = true;
            BroadcastStreakFx(owner.Id, StreakId.HunterDrone, e.Pos, 1f, now);
            return;
        }
        if (!e.Firing && now >= e.NextScan)
        {
            e.NextScan = now + KillStreaks.HunterRescan;
            var best = byte.MaxValue;
            var bestD = float.MaxValue;
            foreach (var o in Players.Values)
            {
                if (!o.Alive || !StreakIsEnemy(owner, o) || !Perks.MachinesCanTarget(o.Perks))
                    continue; // Cold Blooded: it can't find you
                var d = Vector3.Distance(o.Position, e.Pos) - (o.Id == e.Target ? 6f : 0f);
                if (d < bestD)
                {
                    bestD = d;
                    best = o.Id;
                }
            }
            e.Target = best;
        }
        Players.TryGetValue(e.Target, out var target);
        if (target is { Alive: false })
            target = null;

        Vector3 want;
        if (target != null)
        {
            var chest = target.Chest;
            var flat = new Vector2(chest.X - e.Pos.X, chest.Z - e.Pos.Z).Length();
            var above = e.Pos.Y - chest.Y;
            if (!e.Firing && flat < MathF.Max(above, 2f) * 1.3f + 3f && ClearLine(e.Pos, chest, now))
                e.Firing = true; // the dive: it beeps fast and doesn't pull out
            if (e.Firing)
                want = Vector3.Normalize(chest + target.Sim.Velocity * 0.1f - e.Pos) * KillStreaks.HunterDiveSpeed;
            else
            {
                var to = new Vector3(chest.X, chest.Y + KillStreaks.HunterClimb, chest.Z) - e.Pos;
                var len = to.Length();
                want = len > 0.01f ? to / len * KillStreaks.HunterCruiseSpeed : Vector3.Zero;
            }
        }
        else if (e.Firing)
            want = e.Vel; // lost its target mid-dive: carries on down
        else
        {
            // Nobody to hunt: hover over its owner.
            var to = owner.Eye + new Vector3(0, KillStreaks.HunterClimb * 0.5f, 0) - e.Pos;
            var len = to.Length();
            want = len > 1f ? to / len * MathF.Min(KillStreaks.HunterCruiseSpeed, len * 2f) : Vector3.Zero;
        }
        if (e.Firing)
            e.Vel = want; // the dive: straight at them
        else
            e.Vel += (want - e.Vel) * MathF.Min(1f, KillStreaks.HunterTurnRate * dt);

        var step = e.Vel * dt;
        var stepLen = step.Length();
        if (stepLen > 1e-4f)
        {
            var dir = step / stepLen;
            var hit = World.Query.Raycast(e.Pos, dir, stepLen + 0.3f, QueryFilter.All);
            if (hit < stepLen + 0.3f)
            {
                if (e.Firing)
                {
                    e.Pos += dir * MathF.Max(0f, hit - 0.2f);
                    DetonateHunter(e, owner, now); // hit a wall or the ground in the dive
                    return;
                }
                // Cruising: slide along whatever's in the way (flat first, then straight up or down), or wait.
                var flatStep = new Vector3(step.X, 0, step.Z);
                var flatLen = flatStep.Length();
                if (flatLen > 1e-4f && World.Query.Raycast(e.Pos, flatStep / flatLen, flatLen + 0.3f, QueryFilter.All) >= flatLen + 0.3f)
                    step = flatStep;
                else if (MathF.Abs(step.Y) > 1e-4f && World.Query.Raycast(e.Pos, new Vector3(0, MathF.Sign(step.Y), 0), MathF.Abs(step.Y) + 0.3f, QueryFilter.All) >= MathF.Abs(step.Y) + 0.3f)
                    step = new Vector3(0, step.Y, 0);
                else
                    step = Vector3.Zero;
                e.Vel = step / dt;
            }
            e.Pos += step;
        }
        if (e.Vel.LengthSquared() > 0.04f)
        {
            e.Yaw = MathF.Atan2(e.Vel.X, -e.Vel.Z);
            e.Pitch = MathF.Asin(Math.Clamp(e.Vel.Y / e.Vel.Length(), -1f, 1f));
        }
        // Proximity fuse: any enemy close enough (Cold Blooded doesn't stop the fuse, only the seeking).
        foreach (var o in Players.Values)
            if (o.Alive && StreakIsEnemy(owner, o) && Vector3.DistanceSquared(o.Chest, e.Pos) < KillStreaks.HunterFuse * KillStreaks.HunterFuse)
            {
                DetonateHunter(e, owner, now);
                return;
            }
    }

    /// <summary>Is the straight line clear of walls and smoke?</summary>
    private bool ClearLine(Vector3 a, Vector3 b, double now)
    {
        var d = b - a;
        var dist = d.Length();
        if (dist < 0.01f)
            return true;
        return World.Query.Raycast(a, d / dist, dist, QueryFilter.All) >= dist - 0.1f && !SmokeBlocks(a, b, now);
    }

    /// <summary>A Hunter Drone goes off: the blast happens on the next tick (outside the entity loop), the visuals now.</summary>
    private void DetonateHunter(StreakEntity e, Player owner, double now)
    {
        if (e.Done)
            return;
        e.Done = true;
        _pendingBlasts.Add((now, owner.Id, e.Pos, KillStreaks.HunterRadius, KillStreaks.HunterDamage, KillStreaks.KillId(StreakId.HunterDrone)));
        BroadcastStreakFx(owner.Id, StreakId.HunterDrone, e.Pos, KillStreaks.HunterRadius, now);
    }

    private void TickGunship(StreakEntity e, Player owner, double now, bool emp)
    {
        e.OrbitAngle += e.OrbitDir * KillStreaks.GunshipSpeed / e.OrbitRadius * World.TickDt;
        e.Pos = GunshipPosition(e);
        e.Yaw = MathF.Atan2(-MathF.Sin(e.OrbitAngle) * e.OrbitDir, -MathF.Cos(e.OrbitAngle) * e.OrbitDir);
        e.Firing = now - e.LastShot < 0.3;
        if (emp || now < e.NextShot)
            return;
        var muzzle = e.Muzzle(e.Pos, e.Yaw, 0);
        var target = StreakFindTarget(owner, muzzle, KillStreaks.GunshipRange, 0, -2, e.Target);
        e.Target = target?.Id ?? byte.MaxValue;
        if (target == null)
        {
            e.NextShot = now + 0.3;
            return;
        }
        e.NextShot = now + KillStreaks.GunshipInterval;
        e.LastShot = now;
        var lead = target.Chest + target.Sim.Velocity * 0.25f - muzzle;
        var d = Weapons.SpreadDirection(Vector3.Normalize(lead), KillStreaks.GunshipSpread, e.Seed++, 0);
        e.Pitch = MathF.Asin(Math.Clamp(d.Y, -1f, 1f));
        var range = KillStreaks.GunshipRange + 20;
        var dist = World.Query.Raycast(muzzle, d, range, QueryFilter.All);
        foreach (var o in Players.Values)
            if (o.Alive && StreakIsEnemy(owner, o) && World.Hitbox(o.Position, o.Sim.Stance, o.Yaw).Raycast(muzzle, d, dist) is { } t && t < dist)
                dist = t;
        if (dist >= range)
            return; // into the sky
        var impact = muzzle + d * MathF.Max(0f, dist - 0.1f);
        _pendingBlasts.Add((now + 0.1, owner.Id, impact, KillStreaks.GunshipRadius, KillStreaks.GunshipDamage, KillStreaks.KillId(StreakId.Gunship)));
        BroadcastStreakFx(owner.Id, StreakId.Gunship, impact, KillStreaks.GunshipRadius, now);
    }

    private void TickCrate(StreakEntity e, Player owner, double now)
    {
        if (!e.Landed)
        {
            e.Pos.Y -= KillStreaks.CrateFallSpeed * World.TickDt;
            if (e.Pos.Y <= e.GroundY)
            {
                e.Pos.Y = e.GroundY;
                e.Landed = true;
                e.Firing = false;
            }
            return;
        }
        // Whoever touches it first takes it, the caller's enemies included.
        Player? taker = null;
        var best = float.MaxValue;
        foreach (var p in Players.Values)
        {
            if (!p.Alive)
                continue;
            var dx = p.Position.X - e.Pos.X;
            var dz = p.Position.Z - e.Pos.Z;
            var d = dx * dx + dz * dz;
            if (d < KillStreaks.CrateTouch * KillStreaks.CrateTouch && MathF.Abs(p.Position.Y - e.Pos.Y) < 2f && d < best)
            {
                best = d;
                taker = p;
            }
        }
        if (taker != null)
            TakeCrate(e, owner, taker, now);
    }

    /// <summary>Server: a crate is taken: full health, grenades and equipment, and one kill toward each unearned streak.</summary>
    private void TakeCrate(StreakEntity e, Player owner, Player taker, double now)
    {
        e.Done = true;
        taker.Health = World.MaxHealth;
        taker.GrenadesLeft = Throwables.Get(taker.Grenade).PerLife;
        taker.EquipmentLeft = Throwables.Get(taker.Equipment).PerLife;
        if (StreaksEnabled)
        {
            var s = taker.Streak;
            foreach (var id in s.Slots.Ids)
            {
                if (s.IsReady(id) || id == StreakId.SupplyDrop)
                    continue; // (a crate never earns another crate)
                var kills = Perks.StreakCost(taker.Perks, KillStreaks.Get(id).Kills);
                if (s.Progress[(int)id] < kills)
                    s.Progress[(int)id]++;
                if (s.Progress[(int)id] >= kills)
                    s.Ready |= StreakState.Bit(id);
            }
            SendStreakState(taker);
        }
        KillStreaks.WriteEvent(_w, 3, owner.Id, StreakId.SupplyDrop, e.Pos, 0, 0, taker.Id);
        Net.Broadcast(_w.Span, PacketReliability.ReliableOrdered);
        OnMoreStreakEvent(3, owner.Id, StreakId.SupplyDrop, e.Pos, 0, taker.Id, now);
    }

    /// <summary>Server: a streak explosion's visuals for everyone (the damage is the server's own <see cref="StreakBlast"/>).</summary>
    private void BroadcastStreakFx(byte owner, StreakId id, Vector3 pos, float radius, double now)
    {
        KillStreaks.WriteEvent(_w, 4, owner, id, pos, radius, 0, 255);
        Net.Broadcast(_w.Span, PacketReliability.ReliableOrdered);
        OnMoreStreakEvent(4, owner, id, pos, radius, 255, now);
    }

    // =====================================================================
    // Both sides
    // =====================================================================

    /// <summary>A strike was called: napalm patches and smoke clouds from the same plan everywhere, each starting when its canister lands.</summary>
    private void OnStrikePlanned(StreakId id, byte owner, List<StrikeShell> plan, double now)
    {
        foreach (var shell in plan)
        {
            var at = now + shell.Delay;
            if (id == StreakId.NapalmStrike)
                FireZones.Add(new FireZone { Owner = owner, Pos = shell.Pos, Start = at, Until = at + KillStreaks.FireSeconds, NextTick = at });
            else if (id == StreakId.SmokeScreen)
                Smokes.Add(new SmokeCloud(shell.Pos, at, at + Throwables.SmokeDuration)); // blocks sight once it has billowed out (SmokeBlocks)
        }
    }

    /// <summary>Both sides: the extra streak events (2 = effect ended, 3 = crate taken, 4 = streak explosion visuals).</summary>
    private void OnMoreStreakEvent(byte evt, byte owner, StreakId id, Vector3 pos, float yaw, byte other, double now)
    {
        string Who(byte pid) => pid == LocalId ? "You" : NameOf(pid);
        switch (evt)
        {
            case 2:
                for (var i = StreakEffects.Count - 1; i >= 0; i--)
                    if (StreakEffects[i].Owner == owner && StreakEffects[i].Kind == id)
                        StreakEffects.RemoveAt(i);
                if (id == StreakId.Juggernaut)
                {
                    Log(owner == LocalId ? "Your Juggernaut armour is broken" : $"{NameOf(owner)}'s Juggernaut armour is broken", owner == LocalId);
                    Audio.Effect3D(Sfx.BreakMetal, Players.TryGetValue(owner, out var jp) ? jp.RenderPosition : pos, 0.8f);
                }
                break;
            case 3:
            {
                if (other == LocalId && Local is { Alive: true })
                {
                    GrenadesLeft = Throwables.Get(LocalGrenade).PerLife;
                    EquipmentLeft = Throwables.Get(LocalEquipment).PerLife;
                    StreakBanner = "SUPPLY DROP: restocked, +1 streak kill";
                    StreakBannerTime = now;
                    StreakBannerEnemy = false;
                    Audio.Effect(Sfx.StreakReady, 0.6f);
                }
                var stolen = Players.TryGetValue(owner, out var op) && Players.TryGetValue(other, out var tp) && op != tp && StreakIsEnemy(op, tp);
                var whose = owner == LocalId ? "your" : $"{NameOf(owner)}'s";
                if (other != owner)
                    Log($"{Who(other)} {(stolen ? "stole" : "took")} {whose} Supply Drop", other == LocalId || owner == LocalId);
                Audio.Effect3D(Sfx.DeployDevice, pos, 0.6f);
                if (!IsServer)
                    foreach (var e in StreakEntities.Values)
                        if (e.Kind == StreakId.SupplyDrop && e.Owner == owner && Vector3.DistanceSquared(e.Pos, pos) < 4f)
                        {
                            StreakEntities.Remove(e.Id); // (one at most, so removing inside the loop is fine: we stop here)
                            break;
                        }
                break;
            }
            case 4:
            {
                var radius = Math.Clamp(yaw, 0.5f, 10f);
                Blasts.Add(new Blast(pos, now, radius, radius < 1.5f ? ThrowableKind.Impact : ThrowableKind.Frag));
                if (radius >= 1.5f)
                {
                    var push = KillStreaks.Get(id).Knockback;
                    Ragdolls.Blast(pos, radius, push, now); // corpses fly (cosmetic, Source/Render/Ragdolls.cs)
                    PropDebris.Push(pos, radius, push);
                    Audio.Effect3D(Sfx.Explosion, pos, id == StreakId.Gunship ? 0.8f : 0.9f);
                    ImpactDecals.OnBlast(World.Map, pos, owner, now); // a scorch where it hit (Source/Decals)
                    if (Local is { } me)
                    {
                        var d = Vector3.Distance(me.RenderEye, pos);
                        Shake = MathF.Max(Shake, Math.Clamp(1.1f - d / 16f, 0f, 1f));
                    }
                }
                if (id == StreakId.Gunship)
                    foreach (var e in StreakEntities.Values)
                        if (e.Kind == StreakId.Gunship && e.Owner == owner)
                        {
                            var from = e.Muzzle(e.RenderPos, e.RenderYaw, 0);
                            Tracers.Add(new Tracer { From = from, To = pos, Color = new Vector3(1f, 0.75f, 0.35f), Width = 0.09f, Born = now });
                            Audio.Effect3D(Sfx.GunshipCannon, from, 0.9f);
                            break;
                        }
                if (id == StreakId.HunterDrone && !IsServer)
                    foreach (var e in StreakEntities.Values)
                        if (e.Kind == StreakId.HunterDrone && e.Owner == owner && Vector3.DistanceSquared(e.Pos, pos) < 25f)
                        {
                            StreakEntities.Remove(e.Id);
                            break;
                        }
                break;
            }
        }
    }

    /// <summary>Every frame: burnt-out fire, the Hunter Drone's beeps, the gunship's engine and crates landing.</summary>
    private void UpdateMoreStreakEffects(double now, float dt)
    {
        for (var i = FireZones.Count - 1; i >= 0; i--)
            if (now > FireZones[i].Until)
                FireZones.RemoveAt(i);
        if (StreakEntities.Count == 0)
            return;
        foreach (var e in StreakEntities.Values)
        {
            switch (e.Kind)
            {
                case StreakId.HunterDrone when now >= e.NextBeep:
                    e.NextBeep = now + (e.Firing ? 0.11 : 0.55);
                    Audio.Effect3D(Sfx.HunterBeep, e.RenderPos, e.Firing ? 0.8f : 0.55f);
                    break;
                case StreakId.Gunship when now >= e.NextBeep:
                    e.NextBeep = now + 2.4;
                    Audio.Effect3D(Sfx.JetFlyby, e.RenderPos, 0.35f); // the engine's drone, round and round
                    break;
                case StreakId.SupplyDrop when !e.Firing && !e.FxLanded && now - e.Born > 0.3:
                    e.FxLanded = true;
                    Audio.Effect3D(Sfx.CrateLand, e.RenderPos, 0.8f);
                    break;
            }
        }
    }

    /// <summary>Is <paramref name="p"/> wearing the Juggernaut suit (seen by everyone, from the replicated effect)?</summary>
    public bool JuggernautShown(Player p, double now)
    {
        if (!p.Alive)
            return false;
        foreach (var e in StreakEffects)
            if (e.Kind == StreakId.Juggernaut && e.Owner == p.Id && now < e.Until)
                return true;
        return false;
    }

    // =====================================================================
    // Self-test hooks
    // =====================================================================

    /// <summary>Self-test: the per-frame streak update (effects ending, strike sounds, fire burning out).</summary>
    public void SelfTestStreakFrame(double now, float dt) => UpdateStreakEffects(now, dt);

    /// <summary>Self-test: hand a player a crate as if they had walked onto it.</summary>
    public void SelfTestTakeCrate(StreakEntity e, Player taker, double now)
    {
        if (Players.TryGetValue(e.Owner, out var owner))
            TakeCrate(e, owner, taker, now);
        StreakEntities.Remove(e.Id);
    }

    /// <summary>Self-test: a hit through DamagePlayer with the headshot flag.</summary>
    public bool SelfTestHit(Player attacker, Player victim, int amount, bool head, double now, DamageKind kind) =>
        DamagePlayer(attacker, victim, amount, head, Weapons.DefaultPrimary.Id, now, kind);

    /// <summary>Self-test: the Juggernaut's movement filter.</summary>
    public InputCmd SelfTestStreakMoveFilter(Player p, InputCmd cmd) => StreakMoveFilter(p, cmd);

    /// <summary>Self-test: run the bot streak logic for one bot once its thinking delay is over.</summary>
    public void SelfTestBotStreaks(Player bot, double now)
    {
        _botStreakAt[bot.Id] = now - 1;
        BotUseStreaks(bot, now);
    }
}
