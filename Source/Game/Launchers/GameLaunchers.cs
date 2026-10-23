using System.Numerics;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>
/// Launchers on the server: firing (ammo per life, fire rate and reload checked against the weapon), flight, contact,
/// arming, proximity and timed fuses, lock-on and wire guidance, flares, sticky charges, and the blast (the grenade
/// rules: walls shield you unless it's thermobaric, Flak Jacket, prone, Demolitions, your own blast hurts you).
///
/// Lag compensation: a projectile lives in the server's present (like a thrown grenade) and isn't rewound, because it
/// is slow enough to dodge and everyone sees it fly. What is compensated is the shooter's delay: the server flies a new
/// rocket forward by the shooter's one-way latency (at most <see cref="MaxLeadTicks"/>) so it is where the shooter's
/// own predicted rocket already is, and every client flies the spawn forward by the spawn's age plus its own latency.
/// </summary>
public sealed partial class Game
{
    /// <summary>Every launcher projectile in flight (server: the real ones; client: the copies flown from the spawn messages).</summary>
    public readonly Dictionary<ushort, Rocket> Rockets = new();

    /// <summary>At most this many projectiles at once (the oldest go off first if it is reached).</summary>
    public const int MaxRockets = 96;

    /// <summary>Most ticks a spawn is flown forward to make up for latency (100 ms).</summary>
    public const int MaxLeadTicks = 6;

    private readonly Stack<Rocket> _rocketPool = new();
    private readonly List<Rocket> _rocketEnded = new();
    private readonly List<Player> _blastVictims = new();
    private readonly List<StreakEntity> _blastEntities = new();
    private readonly List<(double At, byte Owner, byte Weapon, uint Kit, Vector3 Pos)> _clusterBlasts = new();
    private ushort _nextRocketId = 1;

    /// <summary>The lock-on target a client's Fire packet claims (its trailing byte), read just before the shot is resolved.</summary>
    private byte _launcherLockClaim = byte.MaxValue;

    /// <summary>Set while a bot's launcher logic fires (BotLaunchers.cs): other bot code never fires a launcher by accident.</summary>
    private bool _botLauncherShot;

    /// <summary>Server: launcher rounds per player this life (ammo is limited, reloads checked).</summary>
    private sealed class LauncherAmmo
    {
        public uint Life = uint.MaxValue;
        public byte Weapon = byte.MaxValue;
        public int Fired, InMag;
        public double NextShot, LastShot;
    }

    private readonly Dictionary<byte, LauncherAmmo> _launcherAmmo = new();

    /// <summary>Server: flares used per streak entity (id -> count, next allowed time).</summary>
    private readonly Dictionary<byte, (int Used, double Next)> _flares = new();

    /// <summary>
    /// Hook for anything else a launcher blast should do (called on the server for every one, after the knockback push):
    /// a partial method, so it costs nothing until something implements it.
    /// </summary>
    partial void OnLauncherBlast(Vector3 center, float radius, float maxDamage, byte owner);

    private Rocket NewRocket()
    {
        var r = _rocketPool.Count > 0 ? _rocketPool.Pop() : new Rocket();
        r.Reset();
        return r;
    }

    private void FreeRocket(Rocket r)
    {
        r.Def = null!;
        if (_rocketPool.Count < MaxRockets * 2)
            _rocketPool.Push(r);
    }

    /// <summary>Launcher stats with attachments for a spawn message (cached by Attachments.Apply).</summary>
    private static WeaponDef LauncherDef(byte weaponId, uint kit) => Attachments.Apply(Weapons.Get(weaponId), kit);

    // =====================================================================
    // Server: firing
    // =====================================================================

    /// <summary>From <see cref="ServerResolveShot"/>: a launcher's trigger pull (a client's, the host's or a bot's).</summary>
    private void ServerLauncherShot(Player shooter, WeaponDef def, Vector3 origin, Vector3 dir, float spread, uint seed, double now)
    {
        var claim = _launcherLockClaim;
        _launcherLockClaim = byte.MaxValue;
        if (shooter.Bot != null && !_botLauncherShot)
            return; // bots only fire launchers from their launcher logic (BotLaunchers.cs)
        ServerFireLauncher(shooter, def, origin, dir, spread, seed, claim, now);
    }

    /// <summary>Server: the launcher ammo this life (reset on each spawn and weapon change).</summary>
    private LauncherAmmo AmmoOf(Player p, WeaponDef def)
    {
        if (!_launcherAmmo.TryGetValue(p.Id, out var a))
            _launcherAmmo[p.Id] = a = new LauncherAmmo();
        if (a.Life != p.SpawnTick || a.Weapon != def.Id)
        {
            a.Life = p.SpawnTick;
            a.Weapon = def.Id;
            a.Fired = 0;
            a.InMag = def.Mag;
            a.NextShot = a.LastShot = -10;
        }
        return a;
    }

    /// <summary>Rounds the player may still fire this life (magazine plus reserve), as the server counts them.</summary>
    public int LauncherRoundsLeft(Player p, WeaponDef def) => Math.Max(0, def.Mag + def.LauncherReserve - AmmoOf(p, def).Fired);

    /// <summary>
    /// Server: fire one launcher round. Checks the ammo left this life, the fire interval and (once the magazine is
    /// empty) that a reload's worth of time has passed, then spawns the projectile, flies it forward by the shooter's
    /// latency and tells everyone. Returns the rocket (null if refused).
    /// </summary>
    private Rocket? ServerFireLauncher(Player p, WeaponDef def, Vector3 origin, Vector3 dir, float spread, uint seed, byte lockClaim, double now)
    {
        if (!p.Alive || !def.IsLauncher || !p.Owns(def.Id))
            return null;
        var a = AmmoOf(p, def);
        if (a.Fired >= def.Mag + def.LauncherReserve || now < a.NextShot)
            return null;
        if (a.InMag <= 0)
        {
            var reload = def.Reload * StreakReloadMult(p, now) * Perks.ReloadMult(p.Perks) * FrenzyBuffs.Value(p, def, FrenzyStat.Reload);
            if (now - a.LastShot < reload * 0.75)
                return null; // the magazine is empty and it can't have been reloaded yet
            a.InMag = def.Mag;
        }
        if (!float.IsFinite(origin.X + origin.Y + origin.Z) || Vector3.Distance(origin, p.Eye) > 3f)
            origin = p.Eye;
        if (!float.IsFinite(dir.X + dir.Y + dir.Z) || dir.LengthSquared() < 0.5f)
            dir = GravityFrames.Forward(p.Position, p.Yaw, p.Pitch);
        dir = Vector3.Normalize(dir);
        a.Fired++;
        a.InMag--;
        a.LastShot = now;
        a.NextShot = now + def.FireInterval * 0.85;
        p.LastShotTime = now;
        p.LastCombatTime = now;

        if (Rockets.Count >= MaxRockets)
            ServerRetireOldestRocket(now);

        spread = Math.Clamp(float.IsFinite(spread) ? spread : def.HipSpreadRad, def.MinSpreadRad, def.MaxSpreadRad);
        var launch = RocketFlight.LaunchDirection(dir, spread, seed);
        var r = NewRocket();
        r.Id = _nextRocketId++;
        if (_nextRocketId == 0)
            _nextRocketId = 1;
        r.Owner = p.Id;
        r.WeaponId = def.Id;
        r.Kit = def.Kit;
        r.Def = def;
        r.Seed = seed;
        r.Pos = RocketFlight.LaunchPoint(origin, launch);
        r.Vel = RocketFlight.LaunchVelocity(def, launch);
        r.BouncesLeft = def.LauncherBounces;
        r.Wire = def.LauncherGuidance == LauncherGuidance.Wire;
        r.Born = now;
        if (def.LauncherGuidance == LauncherGuidance.LockOn && lockClaim != byte.MaxValue && ValidLock(p, def, lockClaim, origin, dir))
            r.Target = lockClaim;
        Rockets[r.Id] = r;

        // Make up for the shooter's latency: their own rocket has been flying on their screen that long already.
        var lead = LeadTicksFor(p);
        var age = 0;
        for (; age < lead && r.End == RocketEnd.None; age++)
            ServerStepRocket(r, now);
        WriteRocketSpawn(_w, r, (byte)age);
        Net.Broadcast(_w.Span, PacketReliability.ReliableOrdered);
        if (p.Id != LocalId)
            Audio.Shot(def, p.RenderEye); // the host hears other people's launches (clients do on the spawn)
        if (r.End != RocketEnd.None)
            ServerRocketEnds(r, now);
        return r;
    }

    /// <summary>Ticks of one-way latency to fly a new rocket forward (0 for the host and bots).</summary>
    private int LeadTicksFor(Player p)
    {
        if (p.Id == LocalId || p.Bot != null || Net.IsOffline)
            return 0;
        foreach (var (guid, id) in _clients.Values)
            if (id == p.Id)
                return Math.Clamp((int)MathF.Round(Net.PingTo(guid) * 0.5f / 1000f * World.TickRate), 0, MaxLeadTicks);
        return 0;
    }

    /// <summary>Is a claimed lock-on target real: an enemy's airborne machine, in range, inside the cone (with slack) and in sight?</summary>
    private bool ValidLock(Player p, WeaponDef def, byte target, Vector3 eye, Vector3 dir)
    {
        if (!StreakEntities.TryGetValue(target, out var e) || !StreakMachineHostile(e, p) || !LauncherTargets.IsAirborne(e))
            return false;
        var to = e.Hitbox.Center - eye;
        var dist = to.Length();
        if (dist > def.LauncherLockRange * 1.15f || dist < 1f)
            return false;
        if (Vector3.Dot(to / dist, dir) < MathF.Cos(MathF.Min(def.LauncherLockCone * 2.5f, 60f) * MathF.PI / 180f))
            return false; // generous: the target moved while the packet travelled
        return World.Query.Raycast(eye, to / dist, dist, QueryFilter.Shots) >= dist - 1.5f;
    }

    /// <summary>Too many in the air: the oldest goes off where it is.</summary>
    private void ServerRetireOldestRocket(double now)
    {
        Rocket? oldest = null;
        foreach (var r in Rockets.Values)
            if (oldest == null || r.Age > oldest.Age)
                oldest = r;
        if (oldest == null)
            return;
        oldest.End = RocketEnd.Explode;
        oldest.EndPos = oldest.Pos;
        oldest.EndNormal = Vector3.UnitY;
        ServerRocketEnds(oldest, now);
    }

    /// <summary>Server: a player's sticky charges all go off (the "aim and pull the trigger" detonation).</summary>
    private void ServerDetonateStickies(Player p, double now)
    {
        _rocketEnded.Clear();
        foreach (var r in Rockets.Values)
            if (r.Owner == p.Id && r.Def.LauncherProjectile == LauncherProjectile.Sticky && r.End == RocketEnd.None && r.Age > 6)
                _rocketEnded.Add(r);
        foreach (var r in _rocketEnded)
        {
            r.End = r.Armed || r.IsArmed ? RocketEnd.Explode : RocketEnd.Dud;
            r.EndPos = r.Pos;
            r.EndNormal = Vector3.UnitY;
            ServerRocketEnds(r, now);
        }
        _rocketEnded.Clear();
    }

    // =====================================================================
    // Server: flight
    // =====================================================================

    /// <summary>Server tick: every projectile flies, then the ones that went off explode. Guided ones are synced to clients.</summary>
    private void ServerTickRockets(double now)
    {
        for (var i = _clusterBlasts.Count - 1; i >= 0; i--)
        {
            var c = _clusterBlasts[i];
            if (now < c.At)
                continue;
            _clusterBlasts.RemoveAt(i);
            if (Players.TryGetValue(c.Owner, out var o))
                ServerLauncherBlast(o, LauncherDef(c.Weapon, c.Kit), c.Pos, Vector3.UnitY, byte.MaxValue, byte.MaxValue, false, RocketFlight.ClusterRadiusMult, RocketFlight.ClusterDamageMult, now);
        }
        if (Rockets.Count == 0)
            return;
        _rocketEnded.Clear();
        foreach (var r in Rockets.Values)
        {
            if (r.End == RocketEnd.None)
                ServerStepRocket(r, now);
            if (r.End != RocketEnd.None)
                _rocketEnded.Add(r);
        }
        foreach (var r in _rocketEnded)
            ServerRocketEnds(r, now);
        _rocketEnded.Clear();
        if (ServerTick % World.SnapshotEvery == 0)
            SendRocketSync();
    }

    /// <summary>Where a guided projectile steers this tick (null: unguided). Server and clients call the same rules.</summary>
    private Vector3? RocketSteer(Rocket r, bool server)
    {
        if (r.Decoyed)
            return r.Decoy;
        if (r.Target != byte.MaxValue)
            return StreakEntities.TryGetValue(r.Target, out var e)
                ? (server ? e.Hitbox.Center : e.Hitbox.Center - e.Pos + e.RenderPos)
                : null; // it's gone: fly on
        if (!r.Wire || !Players.TryGetValue(r.Owner, out var owner) || !owner.Alive)
            return null;
        if (!server && owner.Id == LocalId)
        {
            if (Active.Def.Id != r.WeaponId)
                return null; // put away: the wire is cut
            return AimPoint(owner.RenderEye, LookDir(LookYaw, LookPitch));
        }
        if (owner.Weapon != r.WeaponId)
            return null;
        return server ? AimPoint(owner.Eye, GravityFrames.Forward(owner.Position, owner.Yaw, owner.Pitch)) : AimPoint(owner.RenderEye, GravityFrames.Forward(owner.RenderPosition, owner.RenderYaw, owner.RenderPitch));
    }

    /// <summary>The point a player looks at (where a wire-guided rocket goes): the first thing along their view, 400 m at most.</summary>
    private static Vector3 AimPoint(Vector3 eye, Vector3 dir) => eye + dir * World.Query.Raycast(eye, dir, 400f, QueryFilter.Shots);

    /// <summary>
    /// Server: one tick of one projectile: fly, then what it touched first along the way (the map, a player, an enemy's
    /// machine, Deployable Cover), its proximity and timed fuses, flares. Sets <see cref="Rocket.End"/> when it goes off.
    /// </summary>
    private void ServerStepRocket(Rocket r, double now)
    {
        var d = r.Def;
        r.Age++;
        r.PrevPos = r.Pos;
        var seconds = r.Age * World.TickDt;
        Players.TryGetValue(r.Owner, out var owner);
        if (owner == null)
        {
            r.End = RocketEnd.Gone;
            return;
        }
        if (r.Stuck)
        {
            if (r.StuckTo != byte.MaxValue)
            {
                if (Players.TryGetValue(r.StuckTo, out var host) && host.Alive)
                    r.Pos = host.Position + r.StuckOffset;
                else
                    r.StuckTo = byte.MaxValue; // they died: it drops where they were
            }
            if (seconds >= d.LauncherFuse)
                EndAt(r, r.Armed ? RocketEnd.Explode : RocketEnd.Dud, r.Pos, Vector3.UnitY);
            return;
        }
        ServerFlares(r, now);
        var start = r.Pos;
        var contact = RocketFlight.Step(r, RocketSteer(r, server: true), out var hit);
        var seg = r.Pos - start;
        var segLen = seg.Length();
        if (contact == RocketContact.OutOfBounds)
        {
            r.End = RocketEnd.Gone;
            return;
        }

        // The first thing along this tick's path: the map (from the step), a player, an enemy machine or a device.
        if (segLen > 1e-5f)
        {
            var dir = seg / segLen;
            var best = segLen;
            Player? victim = null;
            StreakEntity? machine = null;
            foreach (var o in Players.Values)
            {
                if (!o.Alive || o == owner && r.Age < 20)
                    continue; // (your own rocket can't hit you as it leaves the tube; a bounced grenade can)
                if (World.Hitbox(o.Position, o.Sim.Stance, o.Yaw).Raycast(start, dir, best) is { } t && t < best)
                {
                    best = t;
                    victim = o;
                }
            }
            foreach (var e in StreakEntities.Values)
            {
                if (!StreakMachineHostile(e, owner))
                    continue; // (yours, your team's, and crates: rockets fly through)
                if (e.Hitbox.Raycast(start, dir, best) is { } t && t < best)
                {
                    best = t;
                    machine = e;
                    victim = null;
                }
            }
            var device = TacDevices.Count > 0 ? TacticalRaycast(start, dir, ref best, owner) : null;
            if (device != null)
            {
                victim = null;
                machine = null;
            }
            if (victim != null || machine != null || device != null)
            {
                var at = start + dir * best;
                r.Travelled -= segLen - best;
                r.Pos = at;
                if (d.LauncherProjectile == LauncherProjectile.Sticky && device == null)
                {
                    Stick(r, at, victim);
                    if (machine != null)
                        EndAt(r, r.IsArmed ? RocketEnd.Explode : RocketEnd.Dud, at, -dir, machine: machine); // machines don't hold charges: it goes off
                    return;
                }
                EndAt(r, r.IsArmed ? RocketEnd.Explode : RocketEnd.Dud, at, -dir, victim, machine);
                return;
            }
        }
        if (contact == RocketContact.World)
        {
            if (d.LauncherProjectile == LauncherProjectile.Sticky)
            {
                Stick(r, hit.Point + hit.Normal * RocketFlight.Radius, null);
                return;
            }
            EndAt(r, r.IsArmed ? RocketEnd.Explode : RocketEnd.Dud, hit.Point, hit.Normal);
            return;
        }

        // Proximity fuse: an armed round goes off next to an enemy's flying machine.
        if (d.LauncherProximity > 0 && r.IsArmed && StreakEntities.Count > 0)
            foreach (var e in StreakEntities.Values)
            {
                if (!StreakMachineHostile(e, owner) || !LauncherTargets.IsAirborne(e))
                    continue;
                if (SegmentDistance(start, r.Pos, e.Hitbox.Center) <= d.LauncherProximity)
                {
                    EndAt(r, RocketEnd.Explode, r.Pos, Vector3.UnitY, machine: e);
                    return;
                }
            }
        if (seconds >= d.LauncherFuse)
            EndAt(r, r.IsArmed || d.LauncherProjectile == LauncherProjectile.Grenade ? RocketEnd.Explode : RocketEnd.Dud, r.Pos, Vector3.UnitY);
    }

    private static void EndAt(Rocket r, RocketEnd end, Vector3 pos, Vector3 normal, Player? victim = null, StreakEntity? machine = null)
    {
        r.End = end;
        r.EndPos = pos;
        r.EndNormal = normal;
        r.HitPlayer = victim?.Id ?? byte.MaxValue;
        r.HitEntity = machine?.Id ?? byte.MaxValue;
    }

    /// <summary>A sticky charge sticks: to a player (it rides along on them), or to the map where it touched.</summary>
    private static void Stick(Rocket r, Vector3 at, Player? onto)
    {
        r.Stuck = true;
        r.Armed = r.IsArmed;
        r.Vel = Vector3.Zero;
        r.Pos = at;
        if (onto != null)
        {
            r.StuckTo = onto.Id;
            r.StuckOffset = at - onto.Position;
        }
    }

    /// <summary>Distance from a point to the segment a..b.</summary>
    private static float SegmentDistance(Vector3 a, Vector3 b, Vector3 p)
    {
        var ab = b - a;
        var t = Math.Clamp(Vector3.Dot(p - a, ab) / MathF.Max(ab.LengthSquared(), 1e-6f), 0f, 1f);
        return Vector3.Distance(a + ab * t, p);
    }

    /// <summary>Server: a lock-on rocket closing on a target that carries flares makes it pop one: the rocket chases the flare.</summary>
    private void ServerFlares(Rocket r, double now)
    {
        if (r.Target == byte.MaxValue || r.Decoyed || !StreakEntities.TryGetValue(r.Target, out var e))
            return;
        var prof = LauncherTargets.Get(e);
        if (prof.Flares <= 0 || Vector3.Distance(r.Pos, e.Hitbox.Center) > prof.FlareRange)
            return;
        var (used, next) = _flares.GetValueOrDefault(e.Id);
        if (used >= prof.Flares || now < next)
            return;
        _flares[e.Id] = (used + 1, now + prof.FlareCooldown);
        // Every lock-on rocket chasing it is pulled off by the same flare.
        var decoy = e.Hitbox.Center + new Vector3(0, -6f, 0) - Vector3.Normalize(r.Vel + new Vector3(0, 1e-3f, 0)) * 4f;
        foreach (var o in Rockets.Values)
            if (o.Target == e.Id && !o.Decoyed)
            {
                o.Decoyed = true;
                o.Decoy = decoy;
            }
        WriteRocketBoom(_w, r, RocketEnd.Flares, e.Hitbox.Center, 0);
        Net.Broadcast(_w.Span, PacketReliability.ReliableOrdered);
        OnRocketBoom(r.Id, r.Owner, r.WeaponId, r.Kit, RocketEnd.Flares, e.Hitbox.Center, 0, r.Seed, now);
    }

    /// <summary>Server: a projectile is done: its blast (or dud hit), the message to everyone, and back to the pool.</summary>
    private void ServerRocketEnds(Rocket r, double now)
    {
        if (!Rockets.Remove(r.Id))
            return;
        byte flags = 0;
        if (r.End is RocketEnd.Explode or RocketEnd.Dud && Players.TryGetValue(r.Owner, out var owner))
        {
            var locked = r.Target != byte.MaxValue && !r.Decoyed && r.HitEntity == r.Target;
            flags = r.End == RocketEnd.Explode
                ? ServerLauncherBlast(owner, r.Def, r.EndPos, r.EndNormal, r.HitPlayer, r.HitEntity, locked, 1f, 1f, now)
                : ServerLauncherDud(owner, r.Def, r.HitPlayer, r.HitEntity, now);
            if (r.End == RocketEnd.Explode)
                for (var i = 0; i < r.Def.LauncherCluster; i++)
                    _clusterBlasts.Add((now + RocketFlight.ClusterDelay(r.Seed, i), r.Owner, r.WeaponId, r.Kit,
                        RocketFlight.ClusterPoint(r.EndPos, r.Def.LauncherRadius, r.Seed, i, r.Def.LauncherCluster)));
        }
        var pos = r.End is RocketEnd.Gone ? r.Pos : r.EndPos;
        WriteRocketBoom(_w, r, r.End, pos, flags);
        Net.Broadcast(_w.Span, PacketReliability.ReliableOrdered);
        OnRocketBoom(r.Id, r.Owner, r.WeaponId, r.Kit, r.End, pos, flags, r.Seed, now);
        FreeRocket(r);
    }

    /// <summary>An unarmed hit: the direct hit's damage only (to the player or machine it struck), no blast.</summary>
    private byte ServerLauncherDud(Player owner, WeaponDef d, byte playerHit, byte entityHit, double now)
    {
        byte flags = 0;
        if (playerHit != byte.MaxValue && Players.TryGetValue(playerHit, out var v) && v.Alive && v != owner)
        {
            if (IsFriendlyHit(owner, v))
                flags |= ShotHitFriendly;
            else
            {
                flags |= 1 | ShotHitPlayer;
                if (DamagePlayer(owner, v, (int)MathF.Round(d.LauncherDirect), false, d.Id, now, DamageKind.Bullet, v.Chest))
                    flags |= 4;
            }
        }
        if (entityHit != byte.MaxValue && StreakEntities.TryGetValue(entityHit, out var e))
        {
            flags |= ShotHitMachine | 1;
            if (TakeLauncherHit(e, owner, d, LauncherTargets.RawHit(d, false, d.LauncherDirect), false, now))
                flags |= 4;
        }
        if (owner.Id == LocalId && flags != 0)
            OnOwnShotConfirmed(flags, now);
        return flags;
    }

    /// <summary>
    /// Server: a launcher blast at <paramref name="pos"/>. The player or machine hit straight on takes the direct-hit
    /// damage; everyone in the player radius takes blast damage (times "playerMult") that falls off with distance (walls shield them except for a
    /// thermobaric round's "wallPierce" share, enemy Deployable Cover takes it), the owner takes their "selfDamage" share,
    /// prone takes 70%, Flak Jacket applies (DamagePlayer). Enemy machines take blast damage times the launcher's
    /// vehicle multiplier. Props, building doors and windows, zombies and enemy devices too. Returns the owner's hit flags.
    /// </summary>
    private byte ServerLauncherBlast(Player owner, WeaponDef d, Vector3 pos, Vector3 normal, byte playerHit, byte entityHit, bool locked,
        float radiusMult, float damageMult, double now)
    {
        var center = pos + normal * 0.15f;
        var perk = Perks.BlastRadiusMult(owner.Perks); // Demolitions
        var radius = d.LauncherRadius * radiusMult * perk; // props, buildings, structures, machines
        var playerRadius = d.PlayerBlastRadius * radiusMult * perk; // players (wider), the push
        var playerMult = damageMult * d.LauncherPlayerMult; // players take the launcher's "playerMult" on top
        var killId = d.Id;
        byte flags = 0;

        // The direct hit first (a player hit straight on takes the bonus on top of the blast).
        Player? direct = playerHit != byte.MaxValue && Players.TryGetValue(playerHit, out var dp) && dp.Alive ? dp : null;
        if (direct != null && direct != owner)
        {
            if (IsFriendlyHit(owner, direct))
                flags |= ShotHitFriendly;
            else
            {
                flags |= 1 | ShotHitPlayer;
                if (DamagePlayer(owner, direct, (int)MathF.Round(d.LauncherDirect * playerMult), false, killId, now, DamageKind.Explosive, center))
                    flags |= 4;
            }
        }
        StreakEntity? directMachine = entityHit != byte.MaxValue && StreakEntities.TryGetValue(entityHit, out var dm) ? dm : null;
        if (directMachine != null)
        {
            flags |= ShotHitMachine | 1;
            if (TakeLauncherHit(directMachine, owner, d, LauncherTargets.RawHit(d, true, 0f) * damageMult, locked, now))
                flags |= 4;
        }

        // The blast.
        _blastVictims.Clear();
        foreach (var v in Players.Values)
            if (v.Alive)
                _blastVictims.Add(v);
        foreach (var victim in _blastVictims)
        {
            if (!victim.Alive)
                continue;
            var dmg = LauncherBlastAt(d, center, victim.Chest, playerRadius) * playerMult;
            if (dmg <= 0 || CoverBlocks(center, victim.Chest))
                continue;
            if (victim == owner)
                dmg *= d.LauncherSelfDamage;
            if (victim.Sim.Stance == Stance.Prone)
                dmg *= 0.7f;
            var amount = (int)MathF.Round(dmg);
            if (amount <= 0)
                continue;
            if (IsFriendlyHit(owner, victim))
            {
                flags |= ShotHitFriendly;
                continue;
            }
            if (victim != owner)
                flags |= 1;
            if (DamagePlayer(owner, victim, amount, false, killId, now, DamageKind.Explosive, center) && victim != owner)
                flags |= 4;
        }
        _blastVictims.Clear();

        // Enemy machines (not the one hit straight on: that hit already counted the centre of the blast).
        if (StreakEntities.Count > 0)
        {
            _blastEntities.Clear();
            foreach (var e in StreakEntities.Values)
                if (e != directMachine && StreakMachineHostile(e, owner))
                    _blastEntities.Add(e);
            foreach (var e in _blastEntities)
            {
                var b = LauncherBlastAt(d, center, e.Hitbox.Center, radius) * damageMult;
                if (b <= 0)
                    continue;
                flags |= ShotHitMachine | 1;
                if (TakeLauncherHit(e, owner, d, LauncherTargets.RawHit(d, false, b), false, now))
                    flags |= 4;
            }
            _blastEntities.Clear();
        }
        var maxDamage = d.Damage * damageMult;
        BlastProps(owner, center, radius, maxDamage, PropDamage.Explosion, now); // breakable props (GameProps.cs)
        BlastBuildings(owner, center, radius, maxDamage, PropDamage.Explosion, now); // building doors and windows (GameBuildings.cs)
        BlastStructures(owner, center, radius, maxDamage, StructureHit.Launcher, killId, now); // destructible buildings (GameDestruction.cs)
        if (Horde.Count > 0)
            BlastZombies(owner, center, radius, maxDamage * 3f, now); // Zombies (ZombieAI.cs)
        BlastTactical(owner, center, radius, maxDamage, now); // enemy tactical devices (GameTactical.cs)
        BlastKnockback(owner, center, playerRadius, Knockback.LauncherStrength(d) * damageMult); // pushes the living (Knockback.cs)
        OnLauncherBlast(center, playerRadius, maxDamage, owner.Id);
        if (owner.Id == LocalId && flags != 0)
            OnOwnShotConfirmed(flags, now);
        return flags;
    }

    /// <summary>
    /// Blast damage from a launcher round at <paramref name="center"/> to a point: the falloff curve inside the radius,
    /// nothing outside it; a wall in between stops it, except for the round's "wallPierce" share (thermobaric).
    /// </summary>
    public static float LauncherBlastAt(WeaponDef d, Vector3 center, Vector3 point, float radius)
    {
        var to = point - center;
        var dist = to.Length();
        var dmg = RocketFlight.BlastDamage(d, dist, radius);
        if (dmg <= 0)
            return 0;
        if (dist > 0.3f && World.Query.Raycast(center, to / dist, dist, QueryFilter.All) < dist - 0.05f)
            dmg *= d.LauncherWallPierce; // walls shield you (a thermobaric round's share still gets round them)
        return dmg;
    }

    /// <summary>
    /// The hook every launcher hit on a kill streak machine goes through (turrets and drones today, the gunship/chopper
    /// and anything else in <see cref="StreakEntities"/> later): <paramref name="raw"/> damage scaled by the machine's
    /// explosive multiplier, or, for a locked rocket's hit on a machine whose <see cref="LauncherTargetProfile.LockOnHits"/>
    /// is set, exactly 1/LockOnHits of its hit points. Returns true if that destroyed it.
    /// </summary>
    public bool TakeLauncherHit(StreakEntity e, Player attacker, WeaponDef d, float raw, bool locked, double now)
    {
        if (!DamageEnabled || !StreakEntities.ContainsKey(e.Id) || !StreakMachineHostile(e, attacker))
            return false;
        if (locked && LauncherTargets.Get(e) is { LockOnHits: > 0 } prof)
        {
            e.Health -= (e.MaxHealth + prof.LockOnHits - 1) / prof.LockOnHits;
            if (e.Health > 0)
                return false;
            RemoveStreakEntity(e, attacker.Id, now);
            return true;
        }
        return DamageStreakMachine(e, attacker, (int)MathF.Round(raw), DamageKind.Explosive, now); // the streaks' own rules (StreakEntities.cs)
    }

    /// <summary>A fresh match: nothing in the air, fresh ammo, no flares used, no lock.</summary>
    private void ClearLaunchers()
    {
        foreach (var r in Rockets.Values)
            FreeRocket(r);
        Rockets.Clear();
        foreach (var r in _predictedRockets)
            FreeRocket(r);
        _predictedRockets.Clear();
        _rocketFx.Clear();
        _clusterBlasts.Clear();
        _launcherAmmo.Clear();
        _flares.Clear();
        ResetLock();
    }

    // =====================================================================
    // Self-test hooks
    // =====================================================================

    /// <summary>Self-test: fire the player's launcher through the server path (no latency lead). Returns the rocket, or null if refused.</summary>
    public Rocket? SelfTestFireLauncher(Player p, WeaponDef def, Vector3 dir, byte lockTarget, double now) =>
        ServerFireLauncher(p, def, p.Eye, dir, 0f, (uint)(now * 1000) | 1, lockTarget, now);

    /// <summary>Self-test: a projectile straight into the air (no ammo or fire-rate checks), owned by <paramref name="owner"/>.</summary>
    public Rocket SelfTestAddRocket(Player owner, WeaponDef def, Vector3 pos, Vector3 vel, double now)
    {
        var r = NewRocket();
        r.Id = _nextRocketId++;
        if (_nextRocketId == 0)
            _nextRocketId = 1;
        r.Owner = owner.Id;
        r.WeaponId = def.Id;
        r.Kit = def.Kit;
        r.Def = def;
        r.Seed = r.Id;
        r.Pos = r.PrevPos = r.RenderPos = pos;
        r.Vel = vel;
        r.BouncesLeft = def.LauncherBounces;
        r.Born = now;
        Rockets[r.Id] = r;
        return r;
    }

    /// <summary>Self-test: a fresh life's launcher ammo for the player.</summary>
    public void SelfTestRefillLauncher(Player p) => _launcherAmmo.Remove(p.Id);

    /// <summary>Self-test: the player's sticky charges go off.</summary>
    public void SelfTestDetonate(Player p, double now) => ServerDetonateStickies(p, now);

    /// <summary>Self-test: cluster bomblets still to go off.</summary>
    public int SelfTestPendingClusters => _clusterBlasts.Count;

    /// <summary>Self-test: one server tick of the rockets (and cluster bomblets).</summary>
    public void SelfTestRocketTick(double now)
    {
        ServerTick++;
        ServerTickRockets(now);
    }

    /// <summary>Self-test: a launcher blast through the server path.</summary>
    public void SelfTestLauncherBlast(Player owner, WeaponDef def, Vector3 pos, double now) =>
        ServerLauncherBlast(owner, def, pos, Vector3.UnitY, byte.MaxValue, byte.MaxValue, false, 1f, 1f, now);

    /// <summary>Self-test: puts a drone or turret owned by <paramref name="owner"/> at a spot (no streak needed).</summary>
    public StreakEntity SelfTestSpawnEntity(Player owner, StreakId kind, Vector3 pos, double now)
    {
        var e = NewEntity(owner, kind, pos, now, 60);
        e.RenderPos = pos;
        e.OrbitRadius = 20f; // (only moves if the test ticks the streaks; the rocket tick leaves it where it is)
        return e;
    }
}
