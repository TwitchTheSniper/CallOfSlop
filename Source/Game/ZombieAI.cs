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
}

/// <summary>How fast a zombie moves (rolled when it spawns, from the round's mix).</summary>
public enum ZSpeed : byte
{
    Walk,
    Run,
    Sprint,
}

/// <summary>One zombie. Server-simulated (movement through the shared player physics); clients get them in <see cref="Msg.ZEnts"/>.</summary>
public sealed class Zombie
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

    // Path within an area (nav graph nodes).
    public readonly int[] Path = new int[64];
    public int PathLen, PathIdx;
    public Vector3 PathGoal;
    public double NextPath;
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
    public readonly List<Zombie> Horde = new();

    private ushort _zNextId = 1;
    private readonly NavSearch _zSearch = new();
    private int _zPathBudget;

    /// <summary>A* searches allowed per server tick for all zombies together (the rest steer straight until their turn).</summary>
    public const int ZombieSearchesPerTick = 2;

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

    private void SpawnTick(ZombiesMap zm, double now)
    {
        if (_zToSpawn <= 0 || now < _zNextSpawn || AliveZombies >= Math.Min(ZRules.MaxAlive, MaxZombies))
            return;
        // Active spawners: in unlocked areas. Prefer the ones nearest the players (World at War spawns by zone).
        _zActive.Clear();
        for (var i = 0; i < zm.Spawners.Count; i++)
            if (AreaOpen(zm.Spawners[i].Area))
                _zActive.Add(i);
        if (_zActive.Count == 0)
            return;
        var standing = Players.Values.Where(IsStanding).OrderBy(p => p.Id).ToList();
        if (standing.Count > 0 && _zActive.Count > 4)
        {
            _zActive.Sort((a, b) =>
            {
                float Near(int s) => standing.Min(p => Vector3.DistanceSquared(p.Position, zm.Spawners[s].At));
                var c = Near(a).CompareTo(Near(b));
                return c != 0 ? c : a.CompareTo(b);
            });
            _zActive.RemoveRange(4, _zActive.Count - 4);
        }
        var pick = _zActive[ZRange(_zActive.Count)];
        SpawnZombie(zm, pick, now);
        _zToSpawn--;
        _zNextSpawn = now + ZRules.SpawnDelayFor(ZRound);
    }

    /// <summary>Server: a new zombie at spawner <paramref name="spawner"/> (health and speed from the round).</summary>
    public Zombie SpawnZombie(ZombiesMap zm, int spawner, double now)
    {
        var sp = zm.Spawners[spawner];
        var (run, sprint) = ZRules.SpeedMix(Math.Max(1, ZRound));
        var r = ZRand();
        var z = new Zombie
        {
            Id = _zNextId++,
            Health = ZRules.ZombieHealth(Math.Max(1, ZRound)),
            Speed = r < sprint ? ZSpeed.Sprint : r < sprint + run ? ZSpeed.Run : ZSpeed.Walk,
            Window = sp.Window,
            Area = sp.Area,
            State = sp.Window >= 0 ? ZState.ToWindow : ZState.Rising,
            StateAt = now,
            SpawnTick = ServerTick,
            NextPath = now + ZRand() * 0.5,
        };
        if (_zNextId == 0)
            _zNextId = 1;
        z.MaxHealth = z.Health;
        z.Sim = new MoveState { Position = sp.At, Grounded = true };
        z.PrevPos = z.RenderPos = sp.At;
        z.Yaw = z.RenderYaw = sp.Window >= 0 ? zm.Windows[sp.Window].Yaw : ZRand() * MathF.Tau;
        for (var i = 0; i < z.History.Length; i++)
            z.History[i] = sp.At;
        Horde.Add(z);
        return z;
    }

    // ---- Thinking and moving -------------------------------------------------------------------------

    private void TickHorde(ZombiesMap zm, double now)
    {
        var t0 = Stopwatch.GetTimestamp();
        _zPathBudget = ZombieSearchesPerTick;
        var alive = 0;
        for (var i = Horde.Count - 1; i >= 0; i--)
        {
            var z = Horde[i];
            if (z.State == ZState.Dead)
            {
                if (now - z.StateAt > 1.5)
                    Horde.RemoveAt(i);
                continue;
            }
            alive++;
        }
        foreach (var z in Horde)
        {
            if (z.State == ZState.Dead)
                continue;
            z.PrevPos = z.Sim.Position;
            ThinkZombie(zm, z, now);
            z.History[ServerTick % z.History.Length] = z.Sim.Position;
        }
        if (alive > 0)
        {
            ZAiMs += Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
            ZAiTicks++;
            ZAiZombieTicks += alive;
        }
    }

    private float SpeedOf(Zombie z) => z.Speed switch
    {
        ZSpeed.Sprint => ZRules.SprintSpeed,
        ZSpeed.Run => ZRules.RunSpeed,
        _ => ZRules.WalkSpeed,
    };

    private void ThinkZombie(ZombiesMap zm, Zombie z, double now)
    {
        if (now < z.StunnedUntil)
        {
            MoveZombie(z, z.Yaw, false, false);
            return;
        }
        switch (z.State)
        {
            case ZState.Rising:
                MoveZombie(z, z.Yaw, false, false);
                if (now - z.StateAt >= 0.9)
                    SetZState(z, ZState.Chase, now);
                return;
            case ZState.ToWindow:
            {
                var w = zm.Windows[z.Window];
                var to = w.Outside - z.Position;
                if (Flat(to) < 0.35f)
                {
                    SetZState(z, ZState.Tearing, now);
                    z.NextTear = now + ZRules.BoardTearSeconds * (0.7 + 0.3 * ZRand());
                    MoveZombie(z, w.Yaw, false, false);
                    return;
                }
                var yaw = MathF.Atan2(to.X, -to.Z);
                MoveZombie(z, Separated(z, yaw), true, false);
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
                    if (Horde.Any(o => o != z && o.State == ZState.Climbing && o.Window == z.Window))
                        return;
                    z.ClimbFrom = z.Position;
                    z.ClimbTo = w.Inside;
                    SetZState(z, ZState.Climbing, now);
                    return;
                }
                if (now >= z.NextTear && z.Window < ZBoards.Length && ZBoards[z.Window] > 0)
                {
                    ZBoards[z.Window]--;
                    z.NextTear = now + ZRules.BoardTearSeconds;
                    Audio.Effect3D(Sfx.BreakWood, w.Opening.Center, 0.9f);
                }
                return;
            }
            case ZState.Climbing:
            {
                var t = (float)Math.Clamp((now - z.StateAt) / ZRules.ClimbSeconds, 0, 1);
                var p = Vector3.Lerp(z.ClimbFrom, z.ClimbTo, t);
                p.Y += MathF.Sin(t * MathF.PI) * 0.9f; // up over the sill and down
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
        if (zm.Inside(z.Position) && z.Door < 0)
            z.Area = zm.AreaAt(z.Position);
        var target = ZombieTarget(z, now);
        if (target == null)
        {
            MoveZombie(z, z.Yaw, false, false); // nobody standing: wait
            return;
        }
        if (TrySwipe(z, now, throughWindow: false))
            return;

        // Which way: through open doors to the target's area, then along the nav graph (or straight when in plain view).
        var goal = target.Position;
        var targetArea = zm.AreaAt(goal);
        if (z.Door >= 0)
        {
            var door = zm.Doors[z.Door];
            var outSide = door.SideOut(z.Area);
            if (Flat(outSide - z.Position) < 0.6f || !World.Map.IsDoorOpen(z.Door))
            {
                z.Area = door.Other(z.Area);
                z.Door = -1;
                z.PathLen = 0;
            }
            else
                goal = outSide;
        }
        else if (targetArea != z.Area)
        {
            var d = DoorTowards(z.Area, targetArea);
            if (d >= 0)
            {
                var inSide = zm.Doors[d].SideIn(z.Area);
                if (Flat(inSide - z.Position) < 0.7f)
                {
                    z.Door = d;
                    goal = zm.Doors[d].SideOut(z.Area);
                }
                else
                    goal = inSide;
            }
        }

        var yaw = SteerZombie(z, goal, now);
        if (now < z.UnstickUntil)
            yaw = z.UnstickYaw;
        // Stuck check: barely moved in a second while trying to: walk off sideways for a moment, plan again.
        if (now - z.StuckAt > 1.0)
        {
            if (Flat(z.Position - z.StuckPos) < 0.25f * SpeedOf(z) && Flat(goal - z.Position) > 1.2f)
            {
                z.UnstickUntil = now + 0.6;
                z.UnstickYaw = World.WrapAngle(yaw + (ZRand() < 0.5f ? 1.4f : -1.4f));
                z.PathLen = 0;
                z.NextPath = now;
            }
            z.StuckPos = z.Position;
            z.StuckAt = now;
        }
        var jump = z.PathLen > 0 && z.PathIdx < z.PathLen && NavGraph.Current.Nodes.Length > z.Path[z.PathIdx]
                   && NavGraph.Current.Nodes[z.Path[z.PathIdx]].Y - z.Position.Y > World.StepHeight;
        MoveZombie(z, Separated(z, yaw), true, jump);
    }

    /// <summary>The yaw to walk towards <paramref name="goal"/>: straight when it's close and in plain view, else along a nav graph path.</summary>
    private float SteerZombie(Zombie z, Vector3 goal, double now)
    {
        var pos = z.Position;
        var to = goal - pos;
        var dist = Flat(to);
        if (dist < 14f && MathF.Abs(to.Y) < 1.2f && NavGraph.Clear(pos, goal))
        {
            z.PathLen = 0;
            return MathF.Atan2(to.X, -to.Z);
        }
        var nav = NavGraph.Current;
        if ((z.PathLen == 0 || now >= z.NextPath || Vector3.DistanceSquared(goal, z.PathGoal) > 4f) && _zPathBudget > 0 && nav.Nodes.Length > 0)
        {
            _zPathBudget--;
            z.NextPath = now + 0.8 + ZRand() * 0.4;
            z.PathGoal = goal;
            var a = nav.Nearest(pos, needSight: false);
            var b = nav.Nearest(goal, needSight: false);
            z.PathLen = a >= 0 && b >= 0 ? _zSearch.Find(nav, a, b, z.Path, null, 1500) : 0;
            z.PathIdx = z.PathLen > 1 ? 1 : 0;
        }
        while (z.PathLen > 0 && z.PathIdx < z.PathLen && z.Path[z.PathIdx] < nav.Nodes.Length)
        {
            var n = nav.Nodes[z.Path[z.PathIdx]];
            if (Flat(n - pos) < 0.65f && z.PathIdx < z.PathLen - 1)
            {
                z.PathIdx++;
                continue;
            }
            // Cut a corner when the node after is in plain view.
            if (z.PathIdx + 1 < z.PathLen && z.Path[z.PathIdx + 1] < nav.Nodes.Length && NavGraph.Clear(pos, nav.Nodes[z.Path[z.PathIdx + 1]]) && (ServerTick + z.Id) % 6 == 0)
            {
                z.PathIdx++;
                continue;
            }
            var d = n - pos;
            if (z.PathIdx == z.PathLen - 1 && Flat(d) < 0.65f)
                break;
            return MathF.Atan2(d.X, -d.Z);
        }
        return MathF.Atan2(to.X, -to.Z);
    }

    /// <summary>Turns a walking yaw away from other zombies close by (so the horde doesn't stack up into one body).</summary>
    private float Separated(Zombie z, float yaw)
    {
        var wish = World.FlatForward(yaw);
        var push = Vector3.Zero;
        foreach (var o in Horde)
        {
            if (o == z || o.State == ZState.Dead)
                continue;
            var d = z.Position - o.Position;
            d.Y = 0;
            var l2 = d.LengthSquared();
            if (l2 > 0.81f || l2 < 1e-6f)
                continue;
            var l = MathF.Sqrt(l2);
            push += d / l * (0.9f - l) * 1.6f;
        }
        if (push.LengthSquared() < 1e-6f)
            return yaw;
        var w = wish + push;
        return MathF.Atan2(w.X, -w.Z);
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
        var reach = ZRules.ZombieReach + (throughWindow ? 0.35f : 0f);
        if (Flat(to) > reach + World.PlayerRadius || MathF.Abs(to.Y) > 1.4f)
            return false;
        z.Yaw = MathF.Atan2(to.X, -to.Z);
        z.SwingAt = now + ZRules.ZombieWindup;
        z.NextSwing = now + ZRules.ZombieSwing;
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
            var reach = ZRules.ZombieReach + (backToWindow ? 0.35f : 0f) + 0.45f; // a little slack: you can still back away during the windup
            if (Flat(to) <= reach + World.PlayerRadius && MathF.Abs(to.Y) < 1.6f)
            {
                ZombieHurt(t, ZRules.ZombieDamage, now, z.Position);
                Audio.Effect3D(Sfx.MeleeHit, t.Chest, 0.9f);
            }
        }
        if (backToWindow && z.Window >= 0)
            z.State = ZState.Tearing;
    }

    // ---- Getting hurt --------------------------------------------------------------------------------

    /// <summary>The zombie's hitbox (a standing player's) at its position <paramref name="ticksAgo"/> server ticks ago (lag compensation).</summary>
    private Vector3 ZombieAt(Zombie z, double tick)
    {
        tick = Math.Clamp(tick, Math.Max((double)z.SpawnTick, ServerTick - (double)World.MaxRewindTicks), ServerTick);
        var t0 = (uint)Math.Floor(tick);
        var t1 = Math.Min(t0 + 1, ServerTick);
        var frac = (float)(tick - t0);
        return Vector3.Lerp(z.History[t0 % z.History.Length], z.History[t1 % z.History.Length], frac);
    }

    private readonly Dictionary<Zombie, float> _zShotDamage = new();
    private readonly HashSet<Zombie> _zShotHead = new();

    /// <summary>
    /// Server, per ray of a shot: the nearest zombie the ray hits before <paramref name="dist"/> (zombies rewound to
    /// <paramref name="rewindTo"/>), or null. Shortens <paramref name="dist"/> to the hit.
    /// </summary>
    private Zombie? ZombieRayHit(Vector3 o, Vector3 d, ref float dist, double rewindTo, out bool head)
    {
        head = false;
        Zombie? hit = null;
        foreach (var z in Horde)
        {
            if (z.State == ZState.Dead)
                continue;
            var at = ZombieAt(z, rewindTo);
            if (World.Hitbox(at, Stance.Stand, z.Yaw).Raycast(o, d, dist) is { } t && t < dist)
            {
                dist = t;
                hit = z;
                head = World.IsHeadshot(at, Stance.Stand, z.Yaw, o + d * t);
            }
        }
        return hit;
    }

    /// <summary>Server: one pellet's damage on a zombie, added up per trigger pull (one damage event per zombie, like on players).</summary>
    private void ZombieShotAdd(Zombie z, float damage, bool head)
    {
        _zShotDamage[z] = _zShotDamage.GetValueOrDefault(z) + damage * (head ? ZRules.ZombieHeadMult : 1f);
        if (head)
            _zShotHead.Add(z);
    }

    /// <summary>Server: applies a trigger pull's zombie damage. Returns hit flags (1 hit, 2 headshot, 4 kill).</summary>
    private byte ZombieShotApply(Player shooter, double now)
    {
        byte flags = 0;
        foreach (var (z, dmg) in _zShotDamage.OrderBy(kv => kv.Key.Id))
        {
            var head = _zShotHead.Contains(z);
            flags |= (byte)(head ? 3 : 1);
            if (DamageZombie(shooter, z, (int)MathF.Round(dmg), head, melee: false, now))
                flags |= 4;
        }
        _zShotDamage.Clear();
        _zShotHead.Clear();
        return flags;
    }

    /// <summary>Server: damage on a zombie from <paramref name="attacker"/> (points for the hit or the kill). Returns true if it killed.</summary>
    public bool DamageZombie(Player attacker, Zombie z, int amount, bool head, bool melee, double now, bool explosive = false)
    {
        if (z.State == ZState.Dead || amount <= 0 || !DamageEnabled)
            return false;
        z.Health -= amount;
        attacker.LastCombatTime = now;
        if (z.Health > 0)
        {
            AddPoints(attacker, ZRules.HitPoints);
            return false;
        }
        z.Health = 0;
        z.KilledByHead = head;
        SetZState(z, ZState.Dead, now);
        var zp = ZP(attacker);
        zp.Kills++;
        if (head)
            zp.Headshots++;
        attacker.Kills++;
        FrenzyOnZombieKill(attacker, head, melee || explosive, now); // per-player kill frenzy, reduced weight (FrenzyServer.cs)
        AddPoints(attacker, melee ? ZRules.MeleeKillPoints : head ? ZRules.HeadshotKillPoints : ZRules.KillPoints);
        zp.Scrap += ZRules.ScrapPerKill;
        RollDrop(z.Position, ZRound, now);
        Audio.Effect3D(Sfx.FleshHit3D, z.Position + new Vector3(0, 1.2f, 0), 0.9f);
        return true;
    }

    /// <summary>
    /// Server: a melee swing against zombies: up to <paramref name="cleave"/> of them in reach and in the arc, nearest
    /// first, not through walls. Returns hit flags (1 hit, 4 kill).
    /// </summary>
    private byte ZombieMelee(Player attacker, Vector3 origin, Vector3 dir, float reach, float coneCos, float damage, int cleave, float stun, double viewTick, double now)
    {
        if (Mode != GameMode.Zombies || cleave <= 0)
            return 0;
        var rewindTo = Math.Max(viewTick, (double)ServerTick - World.MaxRewindTicks);
        var list = new List<(Zombie Z, float Dist)>();
        foreach (var z in Horde)
        {
            if (z.State == ZState.Dead)
                continue;
            var box = World.Hitbox(ZombieAt(z, rewindTo), Stance.Stand, z.Yaw);
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
        byte flags = 0;
        foreach (var (z, _) in list.Take(cleave))
        {
            flags |= 1;
            if (stun > 0)
                z.StunnedUntil = Math.Max(z.StunnedUntil, now + stun);
            if (DamageZombie(attacker, z, (int)MathF.Round(damage), false, melee: true, now))
                flags |= 4;
        }
        return flags;
    }

    /// <summary>Server: a grenade or blast: zombies in the radius take damage falling off with distance (walls shield).</summary>
    private void BlastZombies(Player? owner, Vector3 center, float radius, float maxDamage, double now)
    {
        if (owner == null || Mode != GameMode.Zombies)
            return;
        foreach (var z in Horde.ToList())
        {
            if (z.State == ZState.Dead)
                continue;
            var to = z.Position + new Vector3(0, 1.1f, 0) - center;
            var dist = to.Length();
            if (dist > radius)
                continue;
            if (dist > 0.3f && World.Query.Raycast(center, to / dist, dist, QueryFilter.Shots) < dist - 0.05f)
                continue;
            DamageZombie(owner, z, (int)MathF.Round(maxDamage * MathF.Pow(1f - dist / radius, 1.4f)), false, melee: false, now, explosive: true);
        }
    }

    // ---- Client: hit prediction against what you see ------------------------------------------------------

    /// <summary>Client: how far a ray goes before it hits a zombie as drawn (for tracers and the hit marker), or <paramref name="dist"/>.</summary>
    public float ZombieRayDrawn(Vector3 o, Vector3 d, float dist, out bool hit)
    {
        hit = false;
        foreach (var z in Horde)
        {
            if (z.State == ZState.Dead)
                continue;
            if (World.Hitbox(z.RenderPos, Stance.Stand, z.RenderYaw).Raycast(o, d, dist) is { } t && t < dist)
            {
                dist = t;
                hit = true;
            }
        }
        return dist;
    }

    // ---- Self-test hooks ------------------------------------------------------------------------------

    public Zombie SelfTestSpawnZombie(int spawner, double now) => SpawnZombie(World.Map.Zombies!, spawner, now);
}
