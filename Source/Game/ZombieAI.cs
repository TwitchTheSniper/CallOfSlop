using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>What a zombie is doing.</summary>
public enum ZState : byte
{
    /// <summary>Climbing out of the ground at a ground spawner.</summary>
    Rising,

    /// <summary>Walking from its spawner to the outside of its window.</summary>
    ToWindow,

    /// <summary>At the window, tearing the boards off.</summary>
    Tearing,

    /// <summary>Climbing in through the window (the boards are all off).</summary>
    Climbing,

    /// <summary>Inside: after the nearest player standing.</summary>
    Chase,

    /// <summary>Swiping at a player (the windup, then the hit).</summary>
    Attack,

    /// <summary>Dead: falls over, then it's gone.</summary>
    Dead,

    /// <summary>
    /// On a climbable face (map logic "zclimb": the Cathedral's bell tower): a body in the living pyramid against the
    /// wall, or climbing over it and up the wall to the top (Source/Game/ZombiesClimb.cs). Moved by the server, not the
    /// physics; shot, it lets go and falls.
    /// </summary>
    Scaling,
}

/// <summary>How fast a zombie moves (rolled when it spawns, from the round's mix).</summary>
public enum ZSpeed : byte
{
    Walk,
    Run,
    Sprint,
}

/// <summary>One zombie. Server-simulated (movement through the shared player physics); clients get them in <see cref="Msg.ZEnts"/>.</summary>
public sealed partial class Zombie
{
    public ushort Id;
    public MoveState Sim;
    public float Yaw;
    public int Health, MaxHealth;
    public ZState State;
    public ZSpeed Speed;
    public int Window = -1;
    public int Area;
    public double StateAt;
    public double NextTear;
    public double SwingAt = -1;
    public double NextSwing;
    public double StunnedUntil;
    public byte Target = byte.MaxValue;
    public double NextTarget;
    public int Door = -1; // crossing this door (from its side in the zombie's area to the other)
    public Vector3 ClimbFrom, ClimbTo;
    public uint SpawnTick;
    public bool KilledByHead;

    // A siege up a climbable face (server; ZombiesClimb.cs): walking outside to the face's foot instead of a window,
    // its place in the climbing list (in the pyramid, or climbing above it), its height and the vault over the top.
    public bool SiegeOut;
    public int ScaleRank = -1;
    public bool ScaleClimber;
    public double VaultAt = -1;
    public Vector3 VaultFrom;

    // Special ammo effects (server; ZombiesAmmoEffects.cs): a burn or bleed ticking, slowed, a corpse that floats away.
    public double BurnUntil, BurnNext;
    public float BurnPerTick;
    public byte BurnBy = byte.MaxValue;
    public double SlowUntil, ZapUntil;
    public bool Floaty;

    /// <summary>Drawn burning / crackling with electricity (the server sets these each tick; clients get them in ZEnts).</summary>
    public bool FxBurning, FxZapped;

    // The stuck watch (routes come from the horde controller's shared flow fields: Source/Game/Horde/).
    public Vector3 StuckPos;
    public double StuckAt;
    public double UnstickUntil;
    public float UnstickYaw;

    /// <summary>Positions by server tick (lag compensation: shots are tested against where the shooter saw it).</summary>
    public readonly Vector3[] History = new Vector3[32];

    public Vector3 PrevPos;
    public Vector3 Position => Sim.Position;

    // ---- Client (and host drawing) --------------------------------------------------------------------
    public Vector3 RenderPos;
    public float RenderYaw;
    public byte HealthByte = 255;
    public double DiedAt = -1;
    public bool Ragdolled; // its body became a ragdoll (Source/Render/Ragdolls.cs; drawing only)
    public readonly List<(double Tick, Vector3 Pos, float Yaw)> Snaps = new(4);
    public double HeardAt;
    public float Gait; // walk cycle phase (drawing)
    public ZState SeenState = (ZState)255;
    public double SeenStateAt;

    public bool Alive => State != ZState.Dead;
}

/// <summary>Zombies AI (server): spawning at active spawners, walking to the windows, tearing boards, climbing in, chasing through open doors, swiping; and every hit test against zombies.</summary>
public sealed partial class Game
{
    /// <summary>Every zombie in the world, alive or falling over (by id).</summary>
    public readonly List<Zombie> Horde = new(MaxZombies); // (sized for the most at once: a growing horde never allocates mid-round)

    /// <summary>The zombies' voices and footsteps on this PC (client side, every frame: Source/Audio/ZombieSounds.cs).</summary>
    public readonly ZombieSounds ZSounds = new();

    private ushort _zNextId = 1;

    /// <summary>Zombies spawned on this server so far (self-tests: a spawn is a new object, so allocation probes leave those ticks out).</summary>
    public long ZSpawns;

    /// <summary>Zombie AI cost: total and ticks measured (the self-test reports the cost per zombie per tick).</summary>
    public double ZAiTicks, ZAiZombieTicks, ZAiMs;

    public int AliveZombies
    {
        get
        {
            var n = 0;
            foreach (var z in Horde)
                if (z.Alive)
                    n++;
            return n;
        }
    }

    // ---- Spawning -----------------------------------------------------------------------------------------

    private readonly List<int> _zActive = new();
    private float[] _zSpawnKey = [];
    private double[] _zSpawnerReady = [];
    private readonly Vector3[] _zSpawnedAt = new Vector3[16];

    /// <summary>
    /// Where a zombie can come out at spawner spot <paramref name="at"/>: the spot itself, or the nearest of a ring of
    /// spots round it (0.9 m, then 1.8 m) with nobody standing there (the horde's grid and the zombies already out this
    /// tick), room to stand and in plain view of the spawner. False: crowded all round.
    /// </summary>
    private bool FreeSpawnSpot(Vector3 at, int spawnedThisTick, out Vector3 spot)
    {
        for (var ring = 0; ring < 3; ring++)
            for (var k = 0; k < (ring == 0 ? 1 : 6); k++)
            {
                var a = k * MathF.Tau / 6 + ring * 0.5f;
                var p = ring == 0 ? at : at + new Vector3(MathF.Sin(a), 0, MathF.Cos(a)) * (0.9f * ring);
                if (HordeAI.Grid.Any(p, 0.6f))
                    continue;
                var free = true;
                for (var j = 0; j < spawnedThisTick && j < _zSpawnedAt.Length; j++)
                    if (Flat(_zSpawnedAt[j] - p) < 0.6f)
                        free = false;
                if (!free || (ring > 0 && (World.Blocked(p + new Vector3(0, 0.05f, 0)) || !NavGraph.Clear(at, p))))
                    continue;
                spot = p;
                return true;
            }
        spot = at;
        return false;
    }

    /// <summary>
    /// Server, every tick of a round: spawns while the round has zombies queued and fewer than the alive cap
    /// (<see cref="ZAliveCap"/>) are up. Each spawn time a burst (the rules' spawnBurstCurve) comes out of the active
    /// spawners (in open areas, the activeSpawnersCurve nearest the players), spread over them: one per spawner, a
    /// spawner rests <see cref="ZombiesRules.SpawnerCooldown"/> after use, and one with a zombie still standing on it waits.
    /// </summary>
    private void SpawnTick(ZombiesMap zm, double now)
    {
        if (_zToSpawn <= 0 || now < _zNextSpawn || _logicSpawnPaused) // (map logic can pause the spawning: zombies.pause)
            return;
        var cap = ZAliveCap();
        if (_zAlive >= cap)
            return;
        if (_zSpawnerReady.Length != zm.Spawners.Count)
        {
            _zSpawnerReady = new double[zm.Spawners.Count];
            _zSpawnKey = new float[zm.Spawners.Count];
        }
        // Active spawners: in unlocked areas. Prefer the ones nearest the players (World at War spawns by zone).
        _zActive.Clear();
        for (var i = 0; i < zm.Spawners.Count; i++)
            if (AreaOpen(zm.Spawners[i].Area))
                _zActive.Add(i);
        if (_zActive.Count == 0)
            return;
        var near = Math.Max(1, (int)MathF.Round(ZRules.ActiveSpawnersCurve.At(Math.Max(1, ZRound))));
        if (_zActive.Count > near && HordeAI.TargetCount > 0)
        {
            foreach (var s in _zActive)
            {
                var best = float.MaxValue;
                for (var t = 0; t < HordeController<Zombie>.MaxTargets; t++)
                    if (HordeAI.Targets[t].Live)
                        best = MathF.Min(best, Vector3.DistanceSquared(HordeAI.Targets[t].Pos, zm.Spawners[s].At));
                _zSpawnKey[s] = best;
            }
            // Insertion sort by distance, then index (a few dozen spawners at most; no allocation).
            for (var i = 1; i < _zActive.Count; i++)
            {
                var v = _zActive[i];
                var j = i - 1;
                while (j >= 0 && (_zSpawnKey[_zActive[j]] > _zSpawnKey[v] || (_zSpawnKey[_zActive[j]] == _zSpawnKey[v] && _zActive[j] > v)))
                {
                    _zActive[j + 1] = _zActive[j];
                    j--;
                }
                _zActive[j + 1] = v;
            }
            _zActive.RemoveRange(near, _zActive.Count - near);
        }
        var burst = Math.Min(Math.Max(1, (int)MathF.Round(ZRules.SpawnBurstCurve.At(Math.Max(1, ZRound)))), Math.Min(cap - _zAlive, _zToSpawn));
        var spawned = 0;
        while (spawned < burst && _zActive.Count > 0)
        {
            var k = ZRange(_zActive.Count);
            var pick = _zActive[k];
            _zActive.RemoveAt(k); // one per spawner per burst
            var sp = zm.Spawners[pick];
            if (spawned > 0 && now < _zSpawnerReady[pick])
                continue; // spread: this one rests; try another (the burst may come out short)
            if (!FreeSpawnSpot(sp.At, spawned, out var at))
                continue; // crowded all round
            _zSpawnedAt[spawned] = at;
            var type = PickZombieType(Math.Max(1, ZRound)); // the boss when due, else the round's weighted mix (ZombieTypes.cs)
            SpawnZombie(zm, pick, now, at, type);
            _zSpawnerReady[pick] = now + ZRules.SpawnerCooldown;
            _zToSpawn--;
            _zAlive++;
            spawned++;
            if (ZRules.TypeAt(type).Pack > 0)
                SpawnPack(zm, pick, now, ZRules.TypeAt(type), type, ref spawned, cap); // a pack comes out together
        }
        _zNextSpawn = now + ZRules.SpawnDelayFor(ZRound);
    }

    /// <summary>Server: a new zombie at spawner <paramref name="spawner"/> (health and speed from the round; its type <paramref name="type"/>, -1 for the round's roll: ZombieTypes.cs).</summary>
    public Zombie SpawnZombie(ZombiesMap zm, int spawner, double now, Vector3? at = null, int type = -1)
    {
        var sp = zm.Spawners[spawner];
        var pos = at ?? sp.At;
        var (run, sprint) = ZRules.SpeedMix(Math.Max(1, ZRound));
        var r = ZRand();
        var z = RentZombie(); // (as new, from the pool once it is warm: ZombiePool.cs)
        z.Id = NextZombieId();
        z.Health = ZRules.ZombieHealth(Math.Max(1, ZRound));
        z.Speed = r < sprint ? ZSpeed.Sprint : r < sprint + run ? ZSpeed.Run : ZSpeed.Walk;
        z.Window = sp.Window;
        z.Area = sp.Area;
        z.State = sp.Window >= 0 ? ZState.ToWindow : ZState.Rising;
        z.StateAt = now;
        z.SpawnTick = ServerTick;
        ZRand(); // (the old first path search delay: keeps the seeded stream in step)
        TypeSetup(z, type < 0 ? PickZombieType(Math.Max(1, ZRound)) : type, now); // its type's health, gait and plates (ZombieTypes.cs)
        z.MaxHealth = z.Health;
        InitLimbs(z); // per-limb health (ZombieLimbs.cs)
        z.Sim = new MoveState { Position = pos, Grounded = true };
        z.PrevPos = z.RenderPos = pos;
        z.Yaw = z.RenderYaw = sp.Window >= 0 ? zm.Windows[sp.Window].Yaw : ZRand() * MathF.Tau;
        for (var i = 0; i < z.History.Length; i++)
            z.History[i] = pos;
        if (World.Map.Zombies is { } hz)
            HordeSetup(hz);
        z.Hm.Slot = TakeZombieSlot();
        HordeController<Zombie>.Joined(z);
        Horde.Add(z);
        _zById[z.Id] = z;
        ZSpawns++;
        return z;
    }

    /// <summary>The highest zombie id (15 bits: the top bit of the id in <see cref="Msg.ZEnts"/> says a limbs byte follows, protocol 65).</summary>
    public const ushort MaxZombieId = 0x7FFF;

    /// <summary>The next zombie id (never 0, and never one still in use: ids wrap after <see cref="MaxZombieId"/>).</summary>
    private ushort NextZombieId()
    {
        for (var tries = 0; tries < 70000; tries++)
        {
            var id = _zNextId++;
            if (_zNextId > MaxZombieId)
                _zNextId = 1;
            if (id != 0 && !_zById.ContainsKey(id))
                return id;
        }
        return _zNextId;
    }

    // ---- Thinking and moving -------------------------------------------------------------------------

    private void TickHorde(ZombiesMap zm, double now)
    {
        using var perf = Perf.Measure(PerfArea.Horde); // the perf window (Source/Perf)
        var t0 = Stopwatch.GetTimestamp();
        var alive = 0;
        for (var i = Horde.Count - 1; i >= 0; i--)
        {
            var z = Horde[i];
            if (z.State == ZState.Dead)
            {
                if (now - z.StateAt > 1.5)
                {
                    Horde.RemoveAt(i);
                    _zById.Remove(z.Id);
                    FreeZombieSlot(z);
                }
                else if (z.Floaty)
                {
                    z.PrevPos = z.Sim.Position;
                    z.Sim.Position.Y += 1.6f * World.TickDt; // the party balloons carry it off (ZombiesAmmoEffects.cs)
                }
                z.FxBurning = z.FxZapped = false;
                continue;
            }
            alive++;
        }
        if (zm.Crowd > 0)
            CrowdTick(zm, now); // a mass-horde map: demote, the crowd's tick, promote (ZombiesCrowd.cs)
        ZBlockGatherPlayers(); // the standing players' bodies, which walking zombies are held off (ZombieBlock.cs)
        TypesCount(); // alive by type (the spawn caps; ZombieTypes.cs)
        HordeBegin(zm, now); // the horde controller's tick: targets, groups, queues, stimuli (ZombieHorde.cs, Source/Game/Horde/)
        ClimbTick(); // a siege up a climbable face: the pyramid's places (ZombiesClimb.cs)
        foreach (var z in Horde)
        {
            if (z.State == ZState.Dead)
            {
                ZombieAnimTick(z, now); // the death's clock
                continue;
            }
            z.PrevPos = z.Sim.Position;
            ThinkZombie(zm, z, now);
            ZombieBashDoors(z, now); // a shut building door in the way: swipe it down (Bots/Doors/BotDoors.cs)
            if (z.BurnUntil > 0)
                BurnTick(z, now); // a burn or bleed ticking (ZombiesAmmoEffects.cs)
            z.FxBurning = z.BurnUntil > now;
            z.FxZapped = z.ZapUntil > now;
            ZombieAnimTick(z, now); // the animation byte and clocks (ZombieAnimNet.cs)
            // The hit box for lag-compensated shots: at a window it sits on the drawn body (the step in to the boards).
            z.History[ServerTick % z.History.Length] = z.Sim.Position + ZombieHitShift(z, ServerTick, z.Sim.Position);
        }
        ZBlockBuild(client: false); // the bodies players are pushed out of this tick (ZombieBlock.cs)
        TypesTickPlayers(now); // grabbers' holds on players (ZombieTypes.cs)
        if (alive > 0)
        {
            ZAiMs += Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
            ZAiTicks++;
            ZAiZombieTicks += alive;
        }
    }

    private float SpeedOf(Zombie z) => (z.Speed switch
    {
        ZSpeed.Sprint => ZRules.SprintSpeed,
        ZSpeed.Run => ZRules.RunSpeed,
        _ => ZRules.WalkSpeed,
    }) * z.Kind.SpeedMult * (z.SlowUntil > _zNow ? ZRules.SlowMult : 1f) * (z.Limbs != 0 ? LimbSpeedMult(z) : 1f); // its type's speed (ZombieTypes.cs); plasma slows (ZombiesAmmoEffects.cs); a lost leg limps, both crawl (ZombieLimbs.cs)

    private void ThinkZombie(ZombiesMap zm, Zombie z, double now)
    {
        if (now < z.StunnedUntil && z.State != ZState.Scaling && !z.Kind.StunImmune) // (a brute shrugs stuns off: ZombieTypes.cs)
        {
            MoveZombie(z, z.Yaw, false, false);
            return;
        }
        if (z.Grabbing)
        {
            GrabTick(z, now); // a grabber holding its prey (ZombieTypes.cs)
            return;
        }
        if (z.PounceUntil > now)
        {
            PounceTick(z, now); // a pouncer in the air (ZombieTypes.cs)
            return;
        }
        if (_climbEnt >= 0 && z.Window >= 0 && z.State is ZState.ToWindow or ZState.Tearing)
            ClimbRecruit(z, now); // a siege: the horde outside leaves the windows for the face (ZombiesClimb.cs)
        switch (z.State)
        {
            case ZState.Scaling:
                if (z.FreeClimb)
                    FreeClimbMove(zm, z, now); // a wall climber on a wall of its own (ZombieTypes.cs)
                else
                    ScaleMove(zm, z, now);
                return;
            case ZState.Rising:
                MoveZombie(z, z.Yaw, false, false);
                if (now - z.StateAt >= ZRules.RiseSeconds) // climbing out of the ground (the rules file's riseSeconds)
                    SetZState(z, ZState.Chase, now);
                return;
            case ZState.ToWindow:
            {
                var w = zm.Windows[z.Window];
                var m = z.Hm;
                // Its place in the window's queue (the horde controller): the front ones go to the window, the rest to a spot behind.
                var front = m.QueueRank < 0 || m.QueueRank < Math.Max(1, ZRules.Horde.Tearers);
                var spot = front ? w.Outside : m.QueueSpot;
                var to = spot - z.Position;
                if (front && Flat(w.Outside - z.Position) < 0.35f)
                {
                    SetZState(z, ZState.Tearing, now);
                    z.NextTear = now + TearSecondsFor(z) * (0.7 + 0.3 * ZRand()); // (a brute tears fast: ZombieTypes.cs)
                    MoveZombie(z, w.Yaw, false, false);
                    return;
                }
                if (HordeAI.Fleeing(z, out var fy))
                {
                    MoveZombie(z, HordeAI.Separate(z, fy), true, false);
                    return;
                }
                if (!front && Flat(to) < 0.3f)
                {
                    MoveZombie(z, w.Yaw, false, false); // waiting its turn, facing the window
                    z.StuckAt = now;
                    z.StuckPos = z.Position;
                    return;
                }
                var portalNode = z.Window < HordeAI.Portals.Length ? HordeAI.Portals[z.Window].Node : -1;
                var p = HordeAI.Steer(z, spot, portalNode, out var jump);
                var d = p - z.Position;
                var yaw = MathF.Atan2(d.X, -d.Z);
                if (now < z.UnstickUntil)
                    yaw = z.UnstickYaw;
                ZStuckWatch(z, yaw, spot, now);
                MoveZombie(z, HordeAI.Separate(z, yaw), true, jump);
                return;
            }
            case ZState.Tearing:
            {
                var w = zm.Windows[z.Window];
                z.Yaw = w.Yaw;
                // Keep to the window's outside spot (others crowd behind).
                var to = w.Outside - z.Position;
                MoveZombie(z, MathF.Atan2(to.X, -to.Z), Flat(to) > 0.3f, false);
                if (TrySwipe(z, now, throughWindow: true))
                    return;
                if (z.Window < ZBoards.Length && ZBoards[z.Window] == 0)
                {
                    // Only one zombie climbs through at a time.
                    if (z.Window < _zClimbing.Length && _zClimbing[z.Window])
                        return;
                    if (z.Window < _zClimbing.Length)
                        _zClimbing[z.Window] = true;
                    z.ClimbFrom = z.Position;
                    z.ClimbTo = w.Inside;
                    SetZState(z, ZState.Climbing, now);
                    return;
                }
                if (now >= z.NextTear && z.Window < ZBoards.Length && ZBoards[z.Window] > 0)
                {
                    ZBoards[z.Window]--;
                    z.NextTear = now + TearSecondsFor(z);
                    Audio.Effect3D(Sfx.BreakWood, w.Opening.Center, 0.9f, MixBus.Zombies);
                }
                return;
            }
            case ZState.Climbing:
            {
                var t = (float)Math.Clamp((now - z.StateAt) / ClimbSecondsFor(z), 0, 1); // (a crawler is slow at windows)
                var p = Vector3.Lerp(z.ClimbFrom, z.ClimbTo, t);
                p.Y += MathF.Sin(t * MathF.PI) * (z.Crawler ? 0.6f : 0.9f); // up over the sill and down
                z.Sim.Position = p;
                z.Sim.Velocity = Vector3.Zero;
                z.Sim.Grounded = true;
                z.Yaw = zm.Windows[z.Window].Yaw;
                if (t >= 1)
                {
                    z.Sim.Position = z.ClimbTo;
                    z.Area = zm.Windows[z.Window].Area;
                    z.Window = -1;
                    SetZState(z, ZState.Chase, now);
                }
                return;
            }
            case ZState.Attack:
                MoveZombie(z, z.Yaw, false, false);
                if (now >= z.SwingAt)
                {
                    LandSwipe(z, now); // a swipe through a window goes back to tearing boards
                    if (z.State == ZState.Attack)
                        SetZState(z, ZState.Chase, now);
                }
                return;
            default:
                ChaseTick(zm, z, now);
                return;
        }
    }

    private static void SetZState(Zombie z, ZState s, double now)
    {
        z.State = s;
        z.StateAt = now;
    }

    /// <summary>The nearest player standing (re-picked twice a second), or null.</summary>
    private Player? ZombieTarget(Zombie z, double now)
    {
        if (now >= z.NextTarget || !Players.TryGetValue(z.Target, out var t) || !IsStanding(t))
        {
            z.NextTarget = now + 0.5;
            Player? best = null;
            var bestD = float.MaxValue;
            foreach (var p in Players.Values)
            {
                if (!IsStanding(p))
                    continue;
                var d = Vector3.DistanceSquared(p.Position, z.Position) + (ZMap!.AreaAt(p.Position) == z.Area ? 0 : 400f);
                if (d < bestD || (d == bestD && best != null && p.Id < best.Id))
                {
                    bestD = d;
                    best = p;
                }
            }
            z.Target = best?.Id ?? byte.MaxValue;
            return best;
        }
        return t;
    }

    private void ChaseTick(ZombiesMap zm, Zombie z, double now)
    {
        var m = z.Hm;
        var ai = HordeAI;
        if (z.SiegeOut)
        {
            SiegeWalk(zm, z, now); // outside, on its way to a climbable face or back to a window (ZombiesClimb.cs)
            return;
        }
        if (z.Door < 0 && m.ThinkNow && zm.Inside(z.Position))
            z.Area = zm.AreaAt(z.Position);
        // Its group's target (the horde controller spreads the horde over the players); a lure draws it instead.
        var lured = m.Group == ai.LureGroup && ai.LureUntil >= 0;
        Player? target = null;
        if (ai.IsLive(m.TargetSlot))
            Players.TryGetValue(ai.Targets[m.TargetSlot].Id, out target);
        z.Target = target?.Id ?? byte.MaxValue;
        if (target == null && !lured)
        {
            MoveZombie(z, z.Yaw, false, false); // nobody standing: wait
            return;
        }
        if (target != null && TrySwipe(z, now, throughWindow: false))
            return;
        if (ai.Fleeing(z, out var fleeYaw))
        {
            MoveZombie(z, ai.Separate(z, fleeYaw), true, false); // scattering from a blast
            return;
        }

        // Which way: through open doors to the goal's area, then along the shared flow field (or straight when in plain view).
        Vector3 goal;
        int goalArea, goalNode;
        if (lured)
        {
            goal = ai.LurePos;
            goalArea = _zLureArea;
            goalNode = _zLureNode;
        }
        else
        {
            ref var tg = ref ai.Targets[m.TargetSlot];
            goal = ai.SurroundGoal(z, tg.Pos);
            goalArea = tg.Area;
            goalNode = tg.Node;
        }
        if (!lured && ai.Regrouping(z, out var hold, out var centre))
        {
            if (hold)
            {
                MoveZombie(z, z.Yaw, false, false); // the group closes up and holds
                z.StuckAt = now;
                z.StuckPos = z.Position;
                return;
            }
            goal = centre;
            goalArea = z.Area;
            goalNode = -1;
        }
        if (z.Door >= 0)
        {
            var door = zm.Doors[z.Door];
            var outSide = door.SideOut(z.Area);
            if (Flat(outSide - z.Position) < 0.6f && MathF.Abs(outSide.Y - z.Position.Y) < 1.2f || !World.Map.IsDoorOpen(z.Door)) // (on its floor: the Cathedral has doors over each other)
            {
                z.Area = door.Other(z.Area);
                z.Door = -1;
                m.Node = -1;
            }
            else
            {
                goal = outSide;
                goalNode = DoorSideNode(z.Door, z.Area, outSide: true);
            }
        }
        else if (goalArea >= 0 && goalArea != z.Area)
        {
            var d = DoorTowards(z.Area, goalArea);
            if (d >= 0)
            {
                var inSide = zm.Doors[d].SideIn(z.Area);
                if (Flat(inSide - z.Position) < 0.7f && MathF.Abs(inSide.Y - z.Position.Y) < 1.2f)
                {
                    z.Door = d;
                    goal = zm.Doors[d].SideOut(z.Area);
                    goalNode = DoorSideNode(d, z.Area, outSide: true);
                }
                else
                {
                    goal = inSide;
                    goalNode = DoorSideNode(d, z.Area, outSide: false);
                }
            }
        }

        if (target != null && !lured && (TryPounce(z, target, now) || TryFreeClimb(zm, z, target, now)))
            return; // a pouncer's leap, a wall climber's wall (ZombieTypes.cs)
        var p = ai.Steer(z, goal, goalNode, out var jump);
        var to = p - z.Position;
        var yaw = MathF.Atan2(to.X, -to.Z);
        if (now < z.UnstickUntil)
            yaw = z.UnstickYaw;
        ZStuckWatch(z, yaw, goal, now);
        MoveZombie(z, ai.Separate(z, yaw), true, jump);
    }

    /// <summary>Stuck check: barely moved in a second while trying to: walk off sideways for a moment, plan again.</summary>
    private void ZStuckWatch(Zombie z, float yaw, Vector3 goal, double now)
    {
        if (now - z.StuckAt <= 1.0)
            return;
        if (Flat(z.Position - z.StuckPos) < 0.25f * SpeedOf(z) && Flat(goal - z.Position) > 1.2f)
        {
            z.UnstickUntil = now + 0.6;
            z.UnstickYaw = World.WrapAngle(yaw + (ZRand() < 0.5f ? 1.4f : -1.4f));
            z.Hm.Node = -1;
            z.Hm.Straight = false;
        }
        z.StuckPos = z.Position;
        z.StuckAt = now;
    }

    /// <summary>One tick of movement through the shared player physics (walking at its speed when <paramref name="walk"/>).</summary>
    private void MoveZombie(Zombie z, float yaw, bool walk, bool jump)
    {
        z.Yaw = World.LerpAngle(z.Yaw, yaw, walk ? 0.25f : 0.15f);
        var cmd = new InputCmd
        {
            Buttons = walk ? Buttons.Forward | (jump ? Buttons.Jump : Buttons.None) : Buttons.None,
            Yaw = walk ? yaw : z.Yaw,
            Pitch = 0,
            SpeedPct = (byte)Math.Clamp((int)MathF.Round(SpeedOf(z) / World.WalkSpeed * 100f), 1, 200),
        };
        World.Simulate(ref z.Sim, cmd);
        if (walk)
            ZBlockZombie(z); // held off standing players' bodies (ZombieBlock.cs)
        if (!float.IsFinite(z.Sim.Position.X + z.Sim.Position.Y + z.Sim.Position.Z))
            z.Sim = new MoveState { Position = z.PrevPos, Grounded = true };
    }

    // ---- Attacking -----------------------------------------------------------------------------------

    /// <summary>A player in reach: start the swipe (the windup; <see cref="LandSwipe"/> lands it). True if it started one.</summary>
    private bool TrySwipe(Zombie z, double now, bool throughWindow)
    {
        if (now < z.NextSwing || !Players.TryGetValue(z.Target, out var t) || !IsStanding(t))
        {
            if (throughWindow)
                ZombieTarget(z, now);
            return false;
        }
        var to = t.Position - z.Position;
        var reach = ZombieReachFor(z) + (throughWindow ? 0.35f : 0f); // (a bite or a crawler reaches less: ZombieLimbs.cs)
        if (Flat(to) > reach + World.PlayerRadius || MathF.Abs(to.Y) > ZombieReachHeight(z, false))
            return false;
        z.Yaw = MathF.Atan2(to.X, -to.Z);
        z.SwingAt = now + ZombieWindupFor(z);
        z.NextSwing = now + ZombieSwingFor(z);
        if (throughWindow)
            z.State = ZState.Attack; // lands from the window spot, then back to tearing (LandSwipe)
        else
            SetZState(z, ZState.Attack, now);
        if (throughWindow)
            z.Door = -2; // marks "back to tearing" (no door is -2)
        return true;
    }

    private void LandSwipe(Zombie z, double now)
    {
        var backToWindow = z.Door == -2;
        if (backToWindow)
            z.Door = -1;
        if (Players.TryGetValue(z.Target, out var t) && IsStanding(t))
        {
            var to = t.Position - z.Position;
            var reach = ZombieReachFor(z) + (backToWindow ? 0.35f : 0f) + 0.45f; // a little slack: you can still back away during the windup
            if (Flat(to) <= reach + World.PlayerRadius && MathF.Abs(to.Y) < ZombieReachHeight(z, true))
            {
                var landed = ZombieHurt(t, ZombieDamageFor(z), now, z.Position); // (one arm, a bite or a crawler hurts less: ZombieLimbs.cs; its type's damage: ZombieTypes.cs)
                Audio.Effect3D(Sfx.MeleeHit, t.Chest, 0.9f, MixBus.Zombies);
                // The type's special on a hit that landed (ZombieTypes.cs): a brute's shove, a grabber's hold, a bloater's burst.
                if (landed && z.Kind.Knockback > 0 && IsStanding(t))
                    TypeKnock(z, t);
                if (landed && z.Kind.GrabSeconds > 0 && !backToWindow && IsStanding(t))
                    StartGrab(z, t, now);
                if (z.Kind.BurstRadius > 0)
                {
                    TypeBurstAndDie(z, now);
                    return;
                }
            }
        }
        if (backToWindow && z.Window >= 0)
            z.State = ZState.Tearing;
    }

    // ---- Getting hurt --------------------------------------------------------------------------------

    /// <summary>
    /// The feet of the zombie's hit box (a standing player's) at server tick <paramref name="tick"/> (lag compensation).
    /// At a window the box sits on the drawn body, up to a metre ahead of the zombie's own position (<see cref="ZombieHitShift"/>).
    /// </summary>
    private Vector3 ZombieAt(Zombie z, double tick)
    {
        tick = Math.Clamp(tick, Math.Max((double)z.SpawnTick, ServerTick - (double)World.MaxRewindTicks), ServerTick);
        var t0 = (uint)Math.Floor(tick);
        var t1 = Math.Min(t0 + 1, ServerTick);
        var frac = (float)(tick - t0);
        return Vector3.Lerp(z.History[t0 % z.History.Length], z.History[t1 % z.History.Length], frac);
    }

    private readonly Dictionary<Zombie, float> _zShotDamage = new();
    private readonly List<Zombie> _zShotOrder = new();
    private readonly HashSet<Zombie> _zShotHead = new();

    /// <summary>
    /// Server, per ray of a shot: the nearest zombie the ray hits before <paramref name="dist"/> (zombies rewound to
    /// <paramref name="rewindTo"/>), or null. Shortens <paramref name="dist"/> to the hit.
    /// </summary>
    private Zombie? ZombieRayHit(Vector3 o, Vector3 d, ref float dist, double rewindTo, out bool head, float grow = 0f)
    {
        head = false;
        Zombie? hit = null;
        foreach (var z in Horde)
        {
            if (z.State == ZState.Dead)
                continue;
            var at = ZombieAt(z, rewindTo);
            if (ZombieBox(z, at, z.Yaw).Grown(grow).Raycast(o, d, dist) is { } t && t < dist) // grow: a fat round's radius; a crawler's box is low (ZombieLimbs.cs)
            {
                dist = t;
                hit = z;
                var hitAt = ZombieUnscale(z, at, o + d * t); // (a big or small type: the hit in a plain body's frame; ZombieLimbs.cs)
                head = World.IsHeadshot(at, ZombieStance(z), z.Yaw, hitAt);
                _zLastLimb = head ? -1 : LimbAt(z, at, z.Yaw, hitAt);
            }
        }
        if (hit != null)
            _zLastRay = (o, d, dist); // the pellet that hit it (special ammo: pierce goes on along it)
        return hit;
    }

    private (Vector3 O, Vector3 D, float Dist) _zLastRay;
    private readonly Dictionary<Zombie, (Vector3 O, Vector3 D, float Dist)> _zShotRay = new();

    /// <summary>Server: one pellet's damage on a zombie, added up per trigger pull (one damage event per zombie, like on players).</summary>
    private void ZombieShotAdd(Zombie z, float damage, bool head)
    {
        if (z.Plates != 0 && PlateAbsorb(z, head ? 0 : _zLastLimb < 0 ? 1 : -1, damage, _zNow))
        {
            _zShotDamage.TryAdd(z, 0f); // the plate took it (a hit, no damage: ZombieTypes.cs)
            _zShotRay.TryAdd(z, _zLastRay);
            return;
        }
        _zShotDamage[z] = _zShotDamage.GetValueOrDefault(z) + damage * (head ? ZRules.ZombieHeadMult : 1f);
        _zShotRay.TryAdd(z, _zLastRay);
        if (!head)
            LimbDamage(z, _zLastLimb, damage); // the limb it landed on (ZombieLimbs.cs)
        if (head)
        {
            _zShotHead.Add(z);
            z.HeadHitTick = ServerTick; // the head bit in ZEnts and the headshot screech (ZombieSounds.cs)
        }
    }

    /// <summary>Server: applies a trigger pull's zombie damage, then the gun's special ammo effects (ZombiesAmmoEffects.cs). Returns hit flags (1 hit, 2 headshot, 4 kill).</summary>
    private byte ZombieShotApply(Player shooter, WeaponDef def, double now)
    {
        byte flags = 0;
        var fx = def.BuildFlags.Length > 0 && ZEffectFlags(def.BuildFlags) != 0;
        if (fx)
            _zFxHits.Clear();
        _zShotOrder.Clear();
        foreach (var kv in _zShotDamage)
            _zShotOrder.Add(kv.Key);
        _zShotOrder.Sort(static (a, b) => a.Id.CompareTo(b.Id));
        foreach (var z in _zShotOrder)
        {
            var dmg = _zShotDamage[z];
            var head = _zShotHead.Contains(z);
            flags |= (byte)(head ? 3 : 1);
            var amount = (int)MathF.Round(dmg);
            if (fx && def.BuildFlags.Contains("bayonet") && Flat(z.Position - shooter.Position) <= ZRules.BayonetRange)
                amount = (int)MathF.Round(dmg * ZRules.BayonetMult); // bayonet: point-blank hits hurt more
            var killed = DamageZombie(shooter, z, amount, head, melee: false, now);
            if (killed)
                flags |= 4;
            SeverLimbs(z, shooter, now); // limbs out of health come off (a shotgun can take several: ZombieLimbs.cs)
            if (fx)
                _zFxHits.Add(new ZFxHit(z, amount, killed, _zShotRay.TryGetValue(z, out var ray) ? ray : (shooter.Eye, World.FlatForward(shooter.Yaw), Flat(z.Position - shooter.Position))));
        }
        _zShotDamage.Clear();
        _zShotHead.Clear();
        _zShotRay.Clear();
        if (fx && _zFxHits.Count > 0)
            ApplyZombieEffects(shooter, def.BuildFlags, _zFxHits, now);
        HordeGunfire(shooter, now); // the horde hears it (ZombieHorde.cs)
        return flags;
    }

    /// <summary>Server: damage on a zombie from <paramref name="attacker"/> (points for the hit or the kill). Returns true if it killed.</summary>
    /// <remarks><paramref name="dot"/>: a burn or bleed tick (no points for the hit, only for the kill). Insta-Kill makes any hit a kill; Double Points doubles the points (ZombiesPowerUps.cs).</remarks>
    public bool DamageZombie(Player attacker, Zombie z, int amount, bool head, bool melee, double now, bool explosive = false, bool dot = false)
    {
        if (z.State == ZState.Dead || amount <= 0 || !DamageEnabled)
            return false;
        if (now < ZInstaKillUntil)
            amount = Math.Max(amount, z.Health);
        z.Health -= amount;
        attacker.LastCombatTime = now;
        if (z.Grabbing && !dot)
            ReleaseGrab(z, now); // any hit frees whoever it holds (ZombieTypes.cs)
        if (z.Health > 0)
        {
            if (!dot)
            {
                EarnPoints(attacker, ZRules.HitPoints, now);
                HordeHurt(z, attacker, now); // its neighbours turn on the shooter (ZombieHorde.cs)
                if (z.State == ZState.Scaling)
                    ClimbFall(z, now); // shot off the face: it lets go (ZombiesClimb.cs)
            }
            return false;
        }
        z.Health = 0;
        z.KilledByHead = head;
        if (z.Grabbing)
            ReleaseGrab(z, now);
        SetZState(z, ZState.Dead, now);
        CrowdNoteKill(); // (a crowd map's conservation count: ZombiesCrowd.cs)
        if (z.Kind.BurstRadius > 0)
            TypeBurst(z, now); // a bloater bursts when it dies (ZombieTypes.cs)
        var zp = ZP(attacker);
        zp.Kills++;
        if (head)
            zp.Headshots++;
        attacker.Kills++;
        FrenzyOnZombieKill(attacker, head, melee || explosive, now); // per-player kill frenzy, reduced weight (FrenzyServer.cs)
        var killPoints = melee ? ZRules.MeleeKillPoints : head ? ZRules.HeadshotKillPoints + (int)PerkSum(zp, static d => d.HeadshotPoints) : ZRules.KillPoints; // Marksman Malt
        EarnPoints(attacker, (int)MathF.Round(killPoints * z.Kind.PointsMult), now); // (more for a brute: ZombieTypes.cs)
        RollDrop(z.Position, ZRound, now, PerkMult(zp, static d => d.PartDrops)); // Salvage Stout
        RollPowerUp(z.Position, now, zp); // ZombiesPowerUps.cs (Windfall Soda)
        Audio.Effect3D(Sfx.FleshHit3D, z.Position + new Vector3(0, 1.2f, 0), 0.9f, MixBus.Zombies);
        return true;
    }

    /// <summary>
    /// Server: a melee swing against zombies: up to <paramref name="cleave"/> of them in reach and in the arc, nearest
    /// first, not through walls. Returns hit flags (1 hit, 4 kill).
    /// </summary>
    /// <remarks><paramref name="flags"/>: the melee build's part flags (shock coil, nails, frozen fish...: ZombiesAmmoEffects.cs).</remarks>
    private byte ZombieMelee(Player attacker, Vector3 origin, Vector3 dir, float reach, float coneCos, float damage, int cleave, float stun, double viewTick, double now, string[]? flags = null)
    {
        if (Mode != GameMode.Zombies || cleave <= 0)
            return 0;
        var rewindTo = Math.Max(viewTick, (double)ServerTick - World.MaxRewindTicks);
        var list = _zMeleeList;
        list.Clear();
        foreach (var z in Horde)
        {
            if (z.State == ZState.Dead)
                continue;
            var box = ZombieBox(z, ZombieAt(z, rewindTo), z.Yaw);
            var closest = Vector3.Clamp(origin, box.Min, box.Max);
            var dist = (closest - origin).Length();
            if (dist > reach)
                continue;
            var toCenter = box.Center - origin;
            var aimed = box.Raycast(origin, dir, reach) != null || (toCenter.LengthSquared() > 1e-4f && Vector3.Dot(Vector3.Normalize(toCenter), dir) >= coneCos);
            if (!aimed)
                continue;
            if (toCenter.Length() > 0.3f && World.Query.Raycast(origin, Vector3.Normalize(toCenter), toCenter.Length(), QueryFilter.Shots) < toCenter.Length() - 0.35f)
                continue;
            list.Add((z, dist));
        }
        list.Sort((a, b) => a.Dist != b.Dist ? a.Dist.CompareTo(b.Dist) : a.Z.Id.CompareTo(b.Z.Id));
        byte hitFlags = 0;
        var fx = flags is { Length: > 0 } && ZEffectFlags(flags) != 0;
        if (fx)
            _zFxHits.Clear();
        damage *= ZPerkMult(attacker, static d => d.MeleeDamage); // Bloodroot Tonic
        for (var li = 0; li < list.Count && li < cleave; li++)
        {
            var (z, dist) = list[li];
            hitFlags |= 1;
            if (stun > 0 && !z.Kind.StunImmune)
                z.StunnedUntil = Math.Max(z.StunnedUntil, now + stun);
            var amount = (int)MathF.Round(damage);
            if (z.Plates != 0 && PlateAbsorb(z, 1, damage, now))
                amount = 0; // the chest plate takes a swing (ZombieTypes.cs)
            var killed = DamageZombie(attacker, z, amount, false, melee: true, now);
            if (killed)
                hitFlags |= 4;
            if (fx)
                _zFxHits.Add(new ZFxHit(z, amount, killed, (origin, dir, dist)));
        }
        if (fx && _zFxHits.Count > 0)
            ApplyZombieEffects(attacker, flags!, _zFxHits, now);
        if (hitFlags != 0 && attacker.Alive && ZPlayers.TryGetValue(attacker.Id, out var azp) && !azp.Downed
            && PerkSum(azp, static d => d.MeleeHeal) is var heal and > 0)
            attacker.Health = Math.Min(World.MaxHealth, attacker.Health + (int)heal); // Bloodroot Tonic: a swing that hits heals you
        return hitFlags;
    }

    private readonly List<(Zombie Z, float Dist)> _zMeleeList = new();

    /// <summary>Server: a grenade or blast: zombies in the radius take damage falling off with distance (walls shield).</summary>
    private void BlastZombies(Player? owner, Vector3 center, float radius, float maxDamage, double now)
    {
        if (owner == null || Mode != GameMode.Zombies)
            return;
        HordeExplosion(owner, center, radius, now); // the horde scatters and regroups (ZombieHorde.cs)
        for (var zi = 0; zi < Horde.Count; zi++)
        {
            var z = Horde[zi];
            if (z.State == ZState.Dead)
                continue;
            var to = z.Position + new Vector3(0, 1.1f, 0) - center;
            var dist = to.Length();
            if (dist > radius)
                continue;
            if (dist > 0.3f && World.Query.Raycast(center, to / dist, dist, QueryFilter.Shots) < dist - 0.05f)
                continue;
            BlastLimbs(z, center, radius, maxDamage); // a blast can take several limbs (ZombieLimbs.cs)
            DamageZombie(owner, z, (int)MathF.Round(maxDamage * MathF.Pow(1f - dist / radius, 1.4f)), false, melee: false, now, explosive: true);
            SeverLimbs(z, owner, now);
        }
        CrowdBlast(owner, center, radius, maxDamage, now); // the crowd behind the front loses its dead by count (ZombiesCrowd.cs)
    }

    // ---- Client: hit prediction against what you see ------------------------------------------------------

    /// <summary>Client: how far a ray goes before it hits a zombie as drawn (for tracers and the hit marker), or <paramref name="dist"/>.</summary>
    public float ZombieRayDrawn(Vector3 o, Vector3 d, float dist, out bool hit, float grow = 0f)
    {
        hit = false;
        foreach (var z in Horde)
        {
            if (z.State == ZState.Dead)
                continue;
            var feet = z.RenderPos + ZombieHitShift(z, ZAnimTick, z.RenderPos); // at a window: on the drawn body
            if (ZombieBox(z, feet, z.RenderYaw).Grown(grow).Raycast(o, d, dist) is { } t && t < dist)
            {
                dist = t;
                hit = true;
            }
        }
        return dist;
    }

    // ---- Self-test hooks ------------------------------------------------------------------------------

    public Zombie SelfTestSpawnZombie(int spawner, double now) => SpawnZombie(World.Map.Zombies!, spawner, now);

    /// <summary>Self-test: a blast from <paramref name="owner"/> (damage and the horde's reaction).</summary>
    public void SelfTestBlast(Player owner, Vector3 at, float radius, float damage, double now) => BlastZombies(owner, at, radius, damage, now);
}
