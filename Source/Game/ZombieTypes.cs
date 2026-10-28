using System.Numerics;

namespace CallOfSlop;

/// <summary>What a zombie's type adds to it (both sides; Source/Game/ZombieTypes.cs).</summary>
public sealed partial class Zombie
{
    /// <summary>Its type: the index into the rules' types (0: the walker; on the wire in <see cref="Msg.ZEnts"/>, protocol 72) and the type itself (never null).</summary>
    public byte Type;

    public ZombieTypeDef Kind = ZombieTypeDef.DefaultWalker;

    /// <summary>Armour plates still on (bit 0 the helmet, bit 1 the chest plate; both sides) and the health left on each (server).</summary>
    public byte Plates;

    public float PlateHpHead, PlateHpChest;

    /// <summary>A grabber holding its target (both sides), until when and when it bites next (server).</summary>
    public bool Grabbing;

    public double GrabUntil, GrabNext;

    /// <summary>A pouncer in the air (server): until when, the way it leapt, and when it may leap again.</summary>
    public double PounceUntil, PounceReadyAt;

    public float PounceYaw;

    /// <summary>A wall climber scaling a wall of its own choosing (server; the bell siege's climb is <see cref="ScaleRank"/>): out from the wall, the wall's top and when it may try again.</summary>
    public bool FreeClimb;

    public Vector3 ClimbOut;
    public float ClimbTopY;
    public double ClimbRetryAt;

    /// <summary>The type state byte on the wire (clients): plates (bits 0-1), grabbing (bit 2).</summary>
    public byte TypeState => (byte)((Plates & 3) | (Grabbing ? 4 : 0));
}

/// <summary>
/// Zombie types (docs/zombies/ZOMBIES-MODE.md, "Zombie types"; the rules' "types" section, Source/Zombies/ZombieTypes.cs):
/// which type a spawn becomes (the round's weights, the first rounds, the caps, packs, the boss rounds), and what each
/// special does on the server: the brute's knockback and shrugged-off stuns, the armoured one's plates that shots take
/// off before the body, the grabber's hold (freed by any hit on it), the pouncer's leap (a melee lunge: it still has to
/// land in reach to hurt), the wall climber's climb up any wall (the bell siege's climbing state reused), the bloater's
/// melee-range burst (off by default) and the boss round's one juggernaut. No type has a ranged attack: every one of
/// them hurts only what it reaches. Allocation-free per tick.
/// </summary>
public sealed partial class Game
{
    /// <summary>Alive zombies by type this tick (server, from the horde's pass), and the type spawned most recently.</summary>
    private readonly int[] _zAliveByType = new int[ZombieTypeDef.MaxTypes];

    /// <summary>The boss type due to come out this round (-1: none).</summary>
    private int _zBossDue = -1;

    /// <summary>Counters (self-test, logs): spawns by type, grabs started and released, pounces, climbs started and finished, bursts, plates shot off.</summary>
    public readonly int[] ZSpawnedByType = new int[ZombieTypeDef.MaxTypes];

    public int ZGrabs, ZGrabReleases, ZPounces, ZFreeClimbs, ZFreeClimbOver, ZBursts, ZPlatesOff, ZKnocks;

    /// <summary>Alive zombies of type <paramref name="type"/> (server, as of this tick).</summary>
    public int ZAliveOfType(int type) => type >= 0 && type < _zAliveByType.Length ? _zAliveByType[type] : 0;

    /// <summary>Server, at the start of the horde's tick: the alive counts by type (the spawn caps).</summary>
    private void TypesCount()
    {
        Array.Clear(_zAliveByType);
        foreach (var z in Horde)
            if (z.State != ZState.Dead && z.Type < _zAliveByType.Length)
                _zAliveByType[z.Type]++;
    }

    /// <summary>Server, at a round's start: a boss round queues its one boss (on top of the round's count).</summary>
    private void TypesBeginRound(int round)
    {
        _zBossDue = -1;
        var types = ZRules.Types;
        for (var i = 1; i < types.Count; i++)
        {
            var t = types[i];
            if (t.Enabled && t.BossEvery > 0 && round >= t.FirstRound && round % t.BossEvery == 0)
            {
                _zBossDue = i;
                _zToSpawn++;
                break;
            }
        }
    }

    /// <summary>Can a spawn become type <paramref name="i"/> in round <paramref name="round"/> (on, phased in, under its cap, weighted)?</summary>
    private bool TypeEligible(int i, int round)
    {
        var t = ZRules.Types[i];
        return t.Enabled && t.BossEvery == 0 && round >= t.FirstRound && (t.MaxAlive == 0 || _zAliveByType[i] < t.MaxAlive) && (i == 0 || t.Weight.At(round) > 0);
    }

    /// <summary>
    /// Server: the type a spawn in round <paramref name="round"/> becomes: the boss when one is due, else a weighted roll
    /// over the eligible types (the walker always eligible, with its own weight; specials phase in by round and are capped).
    /// </summary>
    private int PickZombieType(int round)
    {
        if (_zBossDue >= 0 && _zBossDue < ZRules.Types.Count)
        {
            var b = _zBossDue;
            _zBossDue = -1;
            return b;
        }
        var types = ZRules.Types;
        var total = 0f;
        for (var i = 0; i < types.Count; i++)
            if (TypeEligible(i, round))
                total += MathF.Max(0, types[i].Weight.At(round));
        var roll = ZRand() * total;
        for (var i = 0; i < types.Count; i++)
        {
            if (!TypeEligible(i, round))
                continue;
            roll -= MathF.Max(0, types[i].Weight.At(round));
            if (roll < 0)
                return i;
        }
        return 0;
    }

    /// <summary>Server, as a zombie spawns: its type's health, gait, plates and clocks.</summary>
    private void TypeSetup(Zombie z, int type, double now)
    {
        var t = ZRules.TypeAt(type);
        z.Type = (byte)Math.Clamp(type, 0, ZombieTypeDef.MaxTypes - 1);
        z.Kind = t;
        if (t.Gait >= 0)
            z.Speed = (ZSpeed)t.Gait;
        z.Health = t.HealthFixed > 0 ? t.HealthFixed : Math.Max(1, (int)MathF.Round(z.Health * t.HealthMult));
        if (t.Armour > 0)
        {
            z.Plates = 3;
            z.PlateHpHead = z.PlateHpChest = t.Armour;
        }
        z.PounceReadyAt = now + 1.0;
        z.ClimbRetryAt = now + 0.5;
        if (z.Type < ZSpawnedByType.Length)
        {
            ZSpawnedByType[z.Type]++;
            _zAliveByType[z.Type]++;
        }
    }

    /// <summary>Server, in a spawn burst: a pack type brings its pack out of the same spawner (within the cap and the round's count).</summary>
    private void SpawnPack(ZombiesMap zm, int spawner, double now, ZombieTypeDef t, int type, ref int spawned, int cap)
    {
        for (var k = 0; k < t.Pack && _zToSpawn > 0 && _zAlive < cap && spawned < _zSpawnedAt.Length && (t.MaxAlive == 0 || _zAliveByType[type] < t.MaxAlive); k++)
        {
            if (!FreeSpawnSpot(zm.Spawners[spawner].At, spawned, out var at))
                return;
            _zSpawnedAt[spawned] = at;
            SpawnZombie(zm, spawner, now, at, type);
            _zToSpawn--;
            _zAlive++;
            spawned++;
        }
    }

    /// <summary>Seconds a board takes this zombie (a brute tears faster).</summary>
    public float TearSecondsFor(Zombie z) => ZRules.BoardTearSeconds / MathF.Max(0.2f, z.Kind.TearMult);

    /// <summary>Server, each horde tick after the zombies: the players' holds (a grabber gone or done lets go).</summary>
    private void TypesTickPlayers(double now)
    {
        foreach (var zp in ZPlayers.Values)
        {
            if (zp.HeldBy == 0)
            {
                zp.Held = false;
                continue;
            }
            var on = now < zp.HeldUntil && _zById.TryGetValue(zp.HeldBy, out var z) && z.Grabbing && z.State != ZState.Dead;
            if (!on)
            {
                zp.HeldBy = 0;
                zp.HeldUntil = 0;
            }
            zp.Held = on;
        }
    }

    // ---- The brute: a knockback ---------------------------------------------------------------------------------

    /// <summary>Server: a brute's hit throws the player back (and off their feet a little), through the blast push (the client predicts it).</summary>
    private void TypeKnock(Zombie z, Player t)
    {
        var k = z.Kind.Knockback;
        if (k <= 0)
            return;
        var dir = t.Position - z.Position;
        dir.Y = 0;
        dir = dir.LengthSquared() > 1e-4f ? Vector3.Normalize(dir) : World.FlatForward(z.Yaw);
        Knock(t, dir * k + new Vector3(0, Knockback.LiftBase + k * Knockback.Lift, 0));
        ZKnocks++;
    }

    // ---- The armoured: plates shot off first ----------------------------------------------------------------------

    /// <summary>
    /// Server: a hit on the head (<paramref name="zone"/> 0) or the chest (1) of an armoured zombie goes into that plate
    /// while it is on: the plate takes it all, and comes off when its health is gone (the hit still counts as a hit, for
    /// no damage). Limbs are bare. Returns true when the plate took the hit.
    /// </summary>
    private bool PlateAbsorb(Zombie z, int zone, float damage, double now)
    {
        if (z.Plates == 0 || zone is < 0 or > 1 || (z.Plates & (1 << zone)) == 0)
            return false;
        ref var hp = ref zone == 0 ? ref z.PlateHpHead : ref z.PlateHpChest;
        hp -= damage;
        if (hp <= 0)
        {
            hp = 0;
            z.Plates &= (byte)~(1 << zone);
            ZPlatesOff++;
            Audio.Effect3D(Sfx.BreakMetal, z.Position + new Vector3(0, zone == 0 ? 1.6f * z.Kind.Height : 1.1f * z.Kind.Height, 0), 0.8f, MixBus.Zombies);
        }
        else
            Audio.Effect3D(Sfx.HitTick, z.Position + new Vector3(0, 1.2f, 0), 0.4f, MixBus.Zombies);
        z.FlinchTick = ServerTick; // (it still shows the hit)
        return true;
    }

    // ---- The grabber: a hold until it is hit ----------------------------------------------------------------------

    /// <summary>Server: a grabber's landed swipe takes hold of <paramref name="t"/>: they can't move until it is hit, it lets go or the hold runs out.</summary>
    private void StartGrab(Zombie z, Player t, double now)
    {
        var zp = ZP(t);
        if (zp.Downed || !t.Alive || zp.HeldBy != 0)
            return;
        zp.HeldBy = z.Id;
        zp.HeldUntil = now + z.Kind.GrabSeconds;
        zp.Held = true;
        z.Grabbing = true;
        z.GrabUntil = now + z.Kind.GrabSeconds;
        z.GrabNext = now + 1.0;
        z.Target = t.Id;
        ZGrabs++;
    }

    /// <summary>Server, each tick of a hold: faces the held player and bites every second; lets go when the hold is over, they go down or get away.</summary>
    private void GrabTick(Zombie z, double now)
    {
        if (now >= z.GrabUntil || !Players.TryGetValue(z.Target, out var t) || !IsStanding(t) || ZP(t).HeldBy != z.Id)
        {
            ReleaseGrab(z, now);
            return;
        }
        var to = t.Position - z.Position;
        if (Flat(to) > ZombieReachFor(z) + World.PlayerRadius + 0.8f || MathF.Abs(to.Y) > 2f)
        {
            ReleaseGrab(z, now); // (pushed or teleported out of its arms)
            return;
        }
        z.Yaw = MathF.Atan2(to.X, -to.Z);
        MoveZombie(z, z.Yaw, false, false);
        if (now >= z.GrabNext)
        {
            z.GrabNext = now + 1.0;
            ZombieHurt(t, Math.Max(1, (int)MathF.Round(z.Kind.GrabDamage)), now, z.Position);
            Audio.Effect3D(Sfx.MeleeHit, t.Chest, 0.7f, MixBus.Zombies);
            if (ZP(t).Downed)
                ReleaseGrab(z, now);
        }
    }

    /// <summary>Server: the grabber lets go (hit, done, or its prey is gone); it swings again after its usual pause.</summary>
    private void ReleaseGrab(Zombie z, double now)
    {
        if (!z.Grabbing)
            return;
        z.Grabbing = false;
        z.GrabUntil = 0;
        if (Players.TryGetValue(z.Target, out var t) && ZP(t).HeldBy == z.Id)
        {
            var zp = ZP(t);
            zp.HeldBy = 0;
            zp.HeldUntil = 0;
            zp.Held = false;
        }
        z.NextSwing = Math.Max(z.NextSwing, now + ZombieSwingFor(z));
        if (z.State == ZState.Attack)
            SetZState(z, ZState.Chase, now);
        ZGrabReleases++;
    }

    // ---- The pouncer: a melee lunge -----------------------------------------------------------------------------

    /// <summary>
    /// Server: a pouncer with its prey in plain view a few metres off leaps at them (a ballistic jump that lands beside
    /// them); it swipes where it lands, as any zombie does, so it hurts nothing it doesn't reach. True if it leapt.
    /// </summary>
    private bool TryPounce(Zombie z, Player target, double now)
    {
        var k = z.Kind;
        if (k.PounceRange <= 0 || now < z.PounceReadyAt || !z.Sim.Grounded || z.Crawler || z.Door >= 0)
            return false;
        var to = target.Position - z.Position;
        var flat = Flat(to);
        if (flat < ZombieReachFor(z) + World.PlayerRadius + 0.8f || flat > k.PounceRange || MathF.Abs(to.Y) > 2.5f)
            return false;
        var o = z.Position + new Vector3(0, 1.2f, 0);
        var d = target.Position + new Vector3(0, 1f, 0) - o;
        var len = d.Length();
        if (len > 0.3f && World.Query.Raycast(o, d / len, len, QueryFilter.Shots) < len - 0.3f)
            return false;
        var dir = new Vector3(to.X, 0, to.Z) / flat;
        var t = flat / k.PounceSpeed;
        var vy = Math.Clamp((to.Y + 0.5f * World.Gravity * t * t) / t, 2.5f, 9f);
        z.Sim.Velocity = dir * k.PounceSpeed + new Vector3(0, vy, 0);
        z.Sim.Grounded = false;
        z.Sim.Position += new Vector3(0, 0.05f, 0);
        z.PounceYaw = z.Yaw = MathF.Atan2(dir.X, -dir.Z);
        z.PounceUntil = now + t + 0.6;
        z.PounceReadyAt = now + k.PounceCooldown;
        z.NextSwing = Math.Min(z.NextSwing, now);
        z.StuckAt = now + 1;
        ZPounces++;
        return true;
    }

    /// <summary>Server, each tick in the air: flies on (the forward input holds its speed against the air's drag); landed, it swipes at once if anyone is in reach.</summary>
    private void PounceTick(Zombie z, double now)
    {
        var cmd = new InputCmd
        {
            Buttons = Buttons.Forward,
            Yaw = z.PounceYaw,
            SpeedPct = (byte)Math.Clamp((int)MathF.Round(z.Kind.PounceSpeed / World.WalkSpeed * 100f), 1, 200),
        };
        World.Simulate(ref z.Sim, cmd);
        if (!float.IsFinite(z.Sim.Position.X + z.Sim.Position.Y + z.Sim.Position.Z))
            z.Sim = new MoveState { Position = z.PrevPos, Grounded = true };
        if (z.Sim.Grounded && now > z.PounceReadyAt - z.Kind.PounceCooldown + 0.15)
        {
            z.PounceUntil = 0;
            z.Hm.Node = -1;
            z.StuckAt = now;
            z.StuckPos = z.Position;
            if (Players.TryGetValue(z.Target, out var t) && IsStanding(t))
                TrySwipe(z, now, throughWindow: false);
        }
    }

    // ---- The wall climber: up any wall ----------------------------------------------------------------------------

    /// <summary>
    /// Server: a wall climber whose prey is above it (or that is stuck against something) with a wall in front looks for
    /// the wall's top (a flat probe every half metre up, as far as its type climbs) and a landing just past it, then
    /// climbs: the bell siege's climbing state (<see cref="ZState.Scaling"/>) moved by <see cref="FreeClimbMove"/>. True if it started.
    /// </summary>
    private bool TryFreeClimb(ZombiesMap zm, Zombie z, Player? target, double now)
    {
        var k = z.Kind;
        if (k.ClimbHeight <= 0 || now < z.ClimbRetryAt || !z.Sim.Grounded || z.Crawler || target == null || z.Door >= 0)
            return false;
        var rise = target.Position.Y - z.Position.Y;
        var stuck = now < z.UnstickUntil;
        if (rise < 1.5f && !stuck)
            return false;
        var dir = target.Position - z.Position;
        dir.Y = 0;
        if (rise < 1.5f || dir.LengthSquared() < 1e-4f)
            dir = World.FlatForward(z.Yaw);
        else
            dir = Vector3.Normalize(dir);
        var o = z.Position + new Vector3(0, 1f, 0);
        var hit = World.Query.Raycast(o, dir, 1.4f, QueryFilter.Navigation);
        if (hit >= 1.4f)
        {
            z.ClimbRetryAt = now + 0.5;
            return false;
        }
        var face = o + dir * hit;
        var top = -1f;
        for (var h = 1.5f; h <= k.ClimbHeight; h += 0.5f)
        {
            var p = new Vector3(z.Position.X, z.Position.Y + h, z.Position.Z);
            if (World.Query.Raycast(p, dir, hit + 0.8f, QueryFilter.Navigation) >= hit + 0.8f)
            {
                top = h;
                break;
            }
            if (World.Blocked(face - dir * 0.45f + new Vector3(0, h - 0.9f, 0), 1.2f))
            {
                z.ClimbRetryAt = now + 2.0; // a ceiling or an overhang: not this wall
                return false;
            }
        }
        if (top < 0)
        {
            z.ClimbRetryAt = now + 2.0;
            return false;
        }
        var topY = z.Position.Y + top;
        var over = new Vector3(face.X, topY + 0.3f, face.Z) + dir * 0.7f;
        var drop = World.Query.Raycast(over, -Vector3.UnitY, 2.5f, QueryFilter.Navigation);
        var land = drop < 2.5f ? over - new Vector3(0, drop, 0) : over - new Vector3(0, 0.3f, 0); // (a free-standing wall: it drops off the far side)
        if (World.Blocked(land + new Vector3(0, 0.05f, 0)))
        {
            z.ClimbRetryAt = now + 2.0;
            return false;
        }
        z.FreeClimb = true;
        z.ClimbOut = -dir;
        z.ClimbTopY = topY;
        z.ClimbFrom = z.Position;
        z.ClimbTo = land;
        z.VaultAt = -1;
        z.ScaleRank = -1;
        z.ScaleClimber = true;
        z.Sim.Position = new Vector3(face.X, z.Position.Y, face.Z) - dir * 0.42f;
        z.Sim.Velocity = Vector3.Zero;
        z.Yaw = MathF.Atan2(dir.X, -dir.Z);
        SetZState(z, ZState.Scaling, now);
        ZFreeClimbs++;
        return true;
    }

    /// <summary>Server: one tick of a wall climber on its wall: hand over hand up at its speed, then over the top onto the landing and after its prey again.</summary>
    private void FreeClimbMove(ZombiesMap zm, Zombie z, double now)
    {
        var k = z.Kind;
        const float dt = World.TickDt;
        z.Sim.Velocity = Vector3.Zero;
        z.Sim.Grounded = true;
        z.Yaw = MathF.Atan2(-z.ClimbOut.X, z.ClimbOut.Z);
        if (z.VaultAt >= 0)
        {
            var vs = ZRules.Climb.VaultSeconds;
            var t = (float)Math.Clamp((now - z.VaultAt) / vs, 0, 1);
            var v = Vector3.Lerp(z.VaultFrom, z.ClimbTo, t);
            v.Y = MathF.Max(v.Y, MathF.Min(z.VaultFrom.Y, z.ClimbTo.Y)) + MathF.Sin(t * MathF.PI) * 0.5f;
            z.Sim.Position = t >= 1 ? z.ClimbTo : v;
            if (t >= 1)
            {
                z.VaultAt = -1;
                z.FreeClimb = false;
                z.ScaleRank = -1;
                z.ScaleClimber = false;
                z.Sim.Grounded = false; // (a drop beyond a free-standing wall falls)
                z.Area = zm.Inside(z.Position) ? zm.AreaAt(z.Position) : z.Area;
                z.Hm.Node = -1;
                z.Hm.Straight = false;
                z.StuckAt = now;
                z.StuckPos = z.Position;
                z.ClimbRetryAt = now + 1.0;
                z.NextSwing = Math.Max(z.NextSwing, now + 0.3);
                SetZState(z, ZState.Chase, now);
                ZFreeClimbOver++;
            }
            return;
        }
        var pos = z.Sim.Position;
        var h = pos.Y + k.ClimbSpeed * dt;
        z.Sim.Position = new Vector3(pos.X, h, pos.Z);
        if (h >= z.ClimbTopY)
        {
            z.VaultAt = now;
            z.VaultFrom = z.Sim.Position;
        }
    }

    // ---- The bloater: a burst in reach ----------------------------------------------------------------------------

    /// <summary>Server: a burst at the zombie: everyone standing within its type's burst radius (and in plain view of it) is hurt. Nothing further off feels it.</summary>
    private void TypeBurst(Zombie z, double now)
    {
        var k = z.Kind;
        if (k.BurstRadius <= 0)
            return;
        var at = z.Position + new Vector3(0, 1f, 0);
        foreach (var p in Players.Values)
        {
            if (!IsStanding(p))
                continue;
            var to = p.Chest - at;
            var d = to.Length();
            if (d > k.BurstRadius + World.PlayerRadius)
                continue;
            if (d > 0.3f && World.Query.Raycast(at, to / d, d, QueryFilter.Shots) < d - 0.3f)
                continue;
            ZombieHurt(p, k.BurstDamage, now, z.Position);
        }
        Audio.Effect3D(Sfx.GibSquelch, at, 1f, MixBus.Zombies);
        ZBursts++;
    }

    /// <summary>Server: a bloater's swipe landed: it bursts and dies on the spot (no kill, no points).</summary>
    private void TypeBurstAndDie(Zombie z, double now)
    {
        TypeBurst(z, now);
        if (z.Grabbing)
            ReleaseGrab(z, now);
        z.Health = 0;
        SetZState(z, ZState.Dead, now);
    }

    // ---- The boss ------------------------------------------------------------------------------------------------

    /// <summary>The boss alive now (both sides: the HUD's health bar), or null.</summary>
    public Zombie? ZBoss()
    {
        foreach (var z in Horde)
            if (z.State != ZState.Dead && z.Kind.BossEvery > 0)
                return z;
        return null;
    }

    /// <summary>A zombie's health as a fraction (the host's numbers, a client's health byte).</summary>
    public float ZombieHealthFraction(Zombie z) => IsServer ? (z.MaxHealth > 0 ? Math.Clamp(z.Health / (float)z.MaxHealth, 0f, 1f) : 0f) : z.HealthByte / 255f;

    // ---- Self-test hooks --------------------------------------------------------------------------------------------

    /// <summary>Self-test: a new zombie of type <paramref name="type"/> at spawner <paramref name="spawner"/>.</summary>
    public Zombie SelfTestSpawnTyped(int spawner, int type, double now) => SpawnZombie(World.Map.Zombies!, spawner, now, null, type);

    /// <summary>Self-test: the type a spawn in <paramref name="round"/> would get from the seeded stream (the counts by type as given).</summary>
    public int SelfTestPickType(int round, ReadOnlySpan<int> alive)
    {
        Array.Clear(_zAliveByType);
        for (var i = 0; i < alive.Length && i < _zAliveByType.Length; i++)
            _zAliveByType[i] = alive[i];
        return PickZombieType(round);
    }

    /// <summary>Self-test: the boss type queued for this round (-1: none), after a round start.</summary>
    public int SelfTestBossDue => _zBossDue;

    /// <summary>Self-test: a round's start for the types alone (the boss queue).</summary>
    public void SelfTestTypesBeginRound(int round) => TypesBeginRound(round);
}
