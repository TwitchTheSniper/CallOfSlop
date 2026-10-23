using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Co-op enemies on the server (docs/pve/DESIGN.md "Enemies"): the pool, spawning out of sight (the director's squads),
/// recycling, and every hit test and damage path against them (players' shots, melee, blasts). The AI is in
/// GamePveAi.cs, the stages in GamePveMission.cs, the packets in GamePveNet.cs, the co-op bots in PveBots.cs.
/// Nothing here runs outside the Co-op mode: every hook checks <see cref="Enemies"/> first.
/// </summary>
public sealed partial class Game
{
    /// <summary>The most co-op enemies alive at once (the pool size; a mission's budget is capped at it).</summary>
    public const int PveMaxAlive = 96;

    /// <summary>Kill-feed id for a player killed by a co-op enemy (bullets or grenades).</summary>
    public const byte PveKillId = 253;

    /// <summary>The <see cref="Projectile.Owner"/> of an enemy's grenade (no player has this id).</summary>
    public const byte PveGrenadeOwner = 253;

    /// <summary>Every co-op enemy in the world, alive or falling over.</summary>
    public readonly List<PveEnemy> Enemies = new(PveMaxAlive + 8);

    private readonly Stack<PveEnemy> _pvePool = new(PveMaxAlive + 8);
    private bool _pvePoolFilled;
    private ushort _pveNextId = 1;
    private uint _pveRng = 0x51ED2701u;

    /// <summary>The co-op spawn director (SpawnDirector.cs).</summary>
    public readonly SpawnDirector Director = new();

    /// <summary>Living enemies (kept up to date as they spawn, die and go).</summary>
    public int AliveEnemies { get; private set; }

    /// <summary>Most enemies alive at once this match, and how many spawned / were recycled in total (self-test, logs).</summary>
    public int PvePeakAlive, PveSpawnedTotal, PveRecycledTotal;

    /// <summary>Enemy AI cost: total ms, ticks and enemy-ticks measured (the self-test reports the cost per enemy per tick).</summary>
    public double PveAiMs, PveAiTicks, PveAiEnemyTicks;

    private uint PveNext()
    {
        var x = _pveRng;
        x ^= x << 13;
        x ^= x >> 17;
        x ^= x << 5;
        return _pveRng = x == 0 ? 0x9E3779B9u : x;
    }

    private float PveRand() => (PveNext() >> 8) / 16777216f;

    private int PveRange(int n) => n <= 1 ? 0 : (int)(PveNext() % (uint)n);

    private static float PFlat(Vector3 v) => MathF.Sqrt(v.X * v.X + v.Z * v.Z);

    // ---- The pool ----------------------------------------------------------------------------------------

    private PveEnemy? TakeEnemy()
    {
        if (!_pvePoolFilled)
        {
            _pvePoolFilled = true;
            for (var i = 0; i < PveMaxAlive + 8; i++)
                _pvePool.Push(new PveEnemy());
        }
        return _pvePool.Count > 0 ? _pvePool.Pop() : null;
    }

    private void ReleaseEnemy(PveEnemy e)
    {
        e.Reset();
        _pvePool.Push(e);
    }

    /// <summary>Removes every enemy (a wipe, a new match, a mode change). Both sides.</summary>
    public void ClearEnemies()
    {
        foreach (var e in Enemies)
            ReleaseEnemy(e);
        Enemies.Clear();
        AliveEnemies = 0;
        _pveById.Clear();
    }

    // ---- Spawning ------------------------------------------------------------------------------------------

    /// <summary>Server: a new enemy of <paramref name="arch"/> standing at <paramref name="at"/> (null: the pool or the budget is full).</summary>
    public PveEnemy? SpawnEnemy(PveArch arch, Vector3 at, double now, float healthMult = 1f, bool named = false)
    {
        if (AliveEnemies >= PveMaxAlive || TakeEnemy() is not { } e)
            return null;
        var def = PveArchetypes.Get(arch);
        e.Id = _pveNextId++;
        if (_pveNextId == 0)
            _pveNextId = 1;
        e.Arch = arch;
        e.Def = def;
        e.State = PveState.Advance;
        e.StateAt = now;
        e.Health = e.MaxHealth = Math.Max(1, (int)MathF.Round(def.Health * healthMult));
        e.Named = named;
        e.Sim = new MoveState { Position = at, Grounded = true };
        e.PrevPos = e.RenderPos = at;
        e.SnapPosA = e.SnapPosB = at;
        e.Yaw = e.RenderYaw = PveRand() * MathF.Tau;
        e.SpawnTick = ServerTick;
        e.Ammo = def.Def.Mag;
        e.NextThink = now + PveRand() * 0.2;
        e.NextPath = now;
        e.NextGrenade = now + def.GrenadeCooldown * (0.5 + PveRand()) * PveDifficulty.Grenades(BotSkill);
        e.StuckAt = now;
        e.StuckPos = at;
        e.LastSeenByPlayer = now;
        for (var i = 0; i < e.History.Length; i++)
            e.History[i] = at;
        Enemies.Add(e);
        AliveEnemies++;
        PveSpawnedTotal++;
        PvePeakAlive = Math.Max(PvePeakAlive, AliveEnemies);
        return e;
    }

    private readonly List<int> _pveCand = new(4096);
    private readonly int[] _pveSample = new int[24];
    private readonly float[] _pveScore = new float[24];

    /// <summary>How close to a player a spawn may be (m).</summary>
    public const float PveSpawnMinDistance = 22f;

    /// <summary>
    /// Server: a spawn spot for a squad: a nav node on the main walkable area, inside one of the stage's spawn zones (or
    /// 25-70 m round <paramref name="focus"/>), at least <see cref="PveSpawnMinDistance"/> from every living player and
    /// out of every player's sight, preferring spots near the focus. False if none of the sampled nodes qualifies.
    /// </summary>
    public bool PickSpawnSpot(IReadOnlyList<(Vector3 At, float Radius)> zones, Vector3 focus, out Vector3 at, out int node)
    {
        at = default;
        node = -1;
        var nav = NavGraph.Current;
        if (nav.Nodes.Length == 0)
            return false;
        _pveCand.Clear();
        Vector3 center;
        float rMin, rMax, dyMax;
        if (zones.Count > 0)
        {
            var z = zones[PveRange(zones.Count)];
            center = z.At;
            rMin = 0;
            rMax = z.Radius;
            dyMax = 3.5f;
        }
        else
        {
            center = focus;
            rMin = 25;
            rMax = 70;
            dyMax = 12f;
        }
        nav.Within(center, rMax, _pveCand);
        if (_pveCand.Count == 0)
            return false;
        // Sample candidates and score the ones that pass the cheap checks (distance, level, the walkable area).
        var n = 0;
        for (var k = 0; k < _pveSample.Length * 3 && n < _pveSample.Length; k++)
        {
            var i = _pveCand[PveRange(_pveCand.Count)];
            if (!SpawnableNode(nav, i))
                continue;
            var q = nav.Nodes[i];
            if (MathF.Abs(q.Y - center.Y) > dyMax || PFlat(q - center) < rMin)
                continue;
            var near = float.MaxValue;
            foreach (var p in Players.Values)
                if (p.Alive)
                    near = MathF.Min(near, Vector3.Distance(p.Position, q));
            if (near < PveSpawnMinDistance)
                continue;
            _pveSample[n] = i;
            _pveScore[n] = Vector3.Distance(q, focus) + PveRand() * 12f; // close to the objective, with some spread
            n++;
        }
        // Best first: the first one nobody can see wins.
        for (var pass = 0; pass < n; pass++)
        {
            var best = -1;
            for (var j = 0; j < n; j++)
                if (_pveSample[j] >= 0 && (best < 0 || _pveScore[j] < _pveScore[best]))
                    best = j;
            if (best < 0)
                break;
            var i = _pveSample[best];
            _pveSample[best] = -1;
            if (SeenByAnyPlayer(nav.Nodes[i]))
                continue;
            at = nav.Nodes[i];
            node = i;
            return true;
        }
        return false;
    }

    private static bool SpawnableNode(NavGraph nav, int i) =>
        (uint)i < (uint)nav.Nodes.Length && (nav.Component.Length == 0 || nav.Component[i] == nav.MainComponent) && !nav.IsTrapped(i)
        && (nav.NodeOff.Length == 0 || !nav.NodeOff[i]);

    /// <summary>Can any living player see a body standing at <paramref name="feet"/> (chest or knees in plain view)?</summary>
    public bool SeenByAnyPlayer(Vector3 feet)
    {
        foreach (var p in Players.Values)
        {
            if (!p.Alive)
                continue;
            var eye = p.Eye;
            if (ClearRay(eye, feet + new Vector3(0, 1.5f, 0)) || ClearRay(eye, feet + new Vector3(0, 0.4f, 0)))
                return true;
        }
        return false;
    }

    private static bool ClearRay(Vector3 a, Vector3 b)
    {
        var d = b - a;
        var len = d.Length();
        return len < 0.01f || World.Query.Raycast(a, d / len, len, QueryFilter.Shots) >= len - 0.1f;
    }

    /// <summary>Server: spawns a squad of <paramref name="count"/> from the mix at one spot (and its neighbours). Returns how many it placed.</summary>
    private int SpawnSquad(IReadOnlyList<(Vector3 At, float Radius)> zones, Vector3 focus, (PveArch Arch, float Weight)[] mix, int count, double now)
    {
        if (count <= 0 || !PickSpawnSpot(zones, focus, out var at, out var node))
            return 0;
        var nav = NavGraph.Current;
        var placed = 0;
        var spot = at;
        var edge = node >= 0 && node + 1 < nav.EdgeStart.Length ? nav.EdgeStart[node] : 0;
        var edgeEnd = node >= 0 && node + 1 < nav.EdgeStart.Length ? nav.EdgeStart[node + 1] : 0;
        for (var k = 0; k < count; k++)
        {
            if (SpawnEnemy(PickArch(mix), spot + new Vector3(0, 0.05f, 0), now) == null)
                break;
            placed++;
            // The next one beside it: a neighbouring node, else a small step sideways.
            if (edge < edgeEnd && (uint)nav.EdgeTo[edge] < (uint)nav.Nodes.Length && (nav.EdgeFlags.Length == 0 || nav.EdgeFlags[edge] == 0))
                spot = nav.Nodes[nav.EdgeTo[edge]];
            else
                spot = at + World.FlatForward(k * 2.1f) * 0.9f;
            edge++;
        }
        return placed;
    }

    private PveArch PickArch((PveArch Arch, float Weight)[] mix)
    {
        var total = 0f;
        foreach (var (_, w) in mix)
            total += w;
        var r = PveRand() * total;
        foreach (var (a, w) in mix)
        {
            r -= w;
            if (r <= 0)
                return a;
        }
        return mix[^1].Arch;
    }

    // ---- Recycling -----------------------------------------------------------------------------------------

    /// <summary>An enemy this far from every player, unseen for <see cref="PveRecycleUnseen"/> s, is recycled.</summary>
    public const float PveRecycleDistance = 110f;

    public const double PveRecycleUnseen = 8;

    /// <summary>Server, once a second: far-away or stuck enemies nobody sees go back to the pool (the director refills near the action).</summary>
    private void RecycleEnemies(double now)
    {
        for (var i = Enemies.Count - 1; i >= 0; i--)
        {
            var e = Enemies[i];
            if (!e.Alive || e.Named)
                continue;
            var far = NearestPlayerDistance(e.Position) > PveRecycleDistance && now - e.LastSeenByPlayer > PveRecycleUnseen;
            var stuck = e.StuckSince > 0 && now - e.StuckSince > 10 && now - e.LastSeenByPlayer > 3;
            if (!far && !stuck)
                continue;
            RecycleEnemy(i);
        }
    }

    private void RecycleEnemy(int index)
    {
        var e = Enemies[index];
        Enemies.RemoveAt(index);
        if (e.Alive)
            AliveEnemies--;
        ReleaseEnemy(e);
        Director.OnRecycled();
        PveRecycledTotal++;
    }

    /// <summary>Server: recycles alive enemies further than <paramref name="dist"/> from <paramref name="from"/> that nobody sees (a stage moved on).</summary>
    private void RecycleBehind(Vector3 from, float dist, double now)
    {
        for (var i = Enemies.Count - 1; i >= 0; i--)
        {
            var e = Enemies[i];
            if (e.Alive && !e.Named && Vector3.Distance(e.Position, from) > dist && now - e.LastSeenByPlayer > 2)
                RecycleEnemy(i);
        }
    }

    private float NearestPlayerDistance(Vector3 at)
    {
        var best = float.MaxValue;
        foreach (var p in Players.Values)
            if (p.Alive)
                best = MathF.Min(best, Vector3.Distance(p.Position, at));
        return best;
    }

    // ---- Players hitting enemies -----------------------------------------------------------------------------

    /// <summary>The enemy's hitbox position <paramref name="tick"/> (lag compensation for players' shots, like zombies).</summary>
    private Vector3 EnemyAt(PveEnemy e, double tick)
    {
        tick = Math.Clamp(tick, Math.Max((double)e.SpawnTick, ServerTick - (double)World.MaxRewindTicks), ServerTick);
        var t0 = (uint)Math.Floor(tick);
        var t1 = Math.Min(t0 + 1, ServerTick);
        return Vector3.Lerp(e.History[t0 % e.History.Length], e.History[t1 % e.History.Length], (float)(tick - t0));
    }

    private static Stance StanceOf(PveEnemy e) => e.Crouched ? Stance.Crouch : Stance.Stand;

    /// <summary>Server, per ray of a shot: the nearest living enemy the ray hits before <paramref name="dist"/> (rewound), or null.</summary>
    private PveEnemy? PveRayHit(Vector3 o, Vector3 d, ref float dist, double rewindTo, out bool head)
    {
        head = false;
        PveEnemy? hit = null;
        foreach (var e in Enemies)
        {
            if (!e.Alive)
                continue;
            var at = EnemyAt(e, rewindTo);
            var stance = StanceOf(e);
            if (World.Hitbox(at, stance, e.Yaw).Raycast(o, d, dist) is { } t && t < dist)
            {
                dist = t;
                hit = e;
                head = World.IsHeadshot(at, stance, e.Yaw, o + d * t);
            }
        }
        return hit;
    }

    private readonly PveEnemy?[] _pveShotHit = new PveEnemy?[64];
    private readonly float[] _pveShotDamage = new float[64];
    private readonly bool[] _pveShotHead = new bool[64];
    private int _pveShotCount;

    /// <summary>Server: one pellet's damage on an enemy, added up per trigger pull (one damage event per enemy).</summary>
    private void PveShotAdd(PveEnemy e, float damage, bool head)
    {
        for (var i = 0; i < _pveShotCount; i++)
            if (_pveShotHit[i] == e)
            {
                _pveShotDamage[i] += damage;
                _pveShotHead[i] |= head;
                return;
            }
        if (_pveShotCount >= _pveShotHit.Length)
            return;
        _pveShotHit[_pveShotCount] = e;
        _pveShotDamage[_pveShotCount] = damage;
        _pveShotHead[_pveShotCount] = head;
        _pveShotCount++;
    }

    /// <summary>Server: applies a trigger pull's enemy damage. Returns hit flags (1 hit, 2 headshot, 4 kill).</summary>
    private byte PveShotApply(Player shooter, byte weaponId, double now)
    {
        byte flags = 0;
        for (var i = 0; i < _pveShotCount; i++)
        {
            var e = _pveShotHit[i]!;
            flags |= (byte)(_pveShotHead[i] ? 3 : 1);
            if (DamageEnemy(shooter, e, (int)MathF.Round(_pveShotDamage[i]), _pveShotHead[i], false, weaponId, now))
                flags |= 4;
            _pveShotHit[i] = null;
        }
        _pveShotCount = 0;
        return flags;
    }

    /// <summary>
    /// Server: damage on an enemy (<paramref name="attacker"/> null: the world, a charge). The enemy turns on whoever hurt
    /// it. Returns true if it killed (the kill counts for the stage and earns the attacker co-op XP).
    /// </summary>
    public bool DamageEnemy(Player? attacker, PveEnemy e, int amount, bool head, bool explosive, byte weaponId, double now)
    {
        if (!e.Alive || amount <= 0 || !DamageEnabled)
            return false;
        e.Health -= amount;
        if (attacker != null)
        {
            attacker.LastCombatTime = now;
            if (attacker.Alive && (e.Target != attacker.Id || !e.TargetVisible))
            {
                e.Target = attacker.Id; // it knows where that came from
                e.LastSeen = attacker.Position;
                e.LastSawTarget = now;
            }
        }
        if (e.Health > 0)
            return false;
        e.Health = 0;
        e.KilledByHead = head;
        e.State = PveState.Dead;
        e.StateAt = e.DiedAt = now;
        AliveEnemies--;
        if (attacker != null)
            attacker.Kills++;
        OnEnemyKilled(attacker, e, head, explosive, weaponId, now); // the stage's count, XP (GamePveMission.cs)
        Audio.Effect3D(Sfx.FleshHit3D, e.Position + new Vector3(0, 1.2f, 0), 0.9f);
        return true;
    }

    /// <summary>Server: a melee swing against enemies in reach and in the arc (not through walls), nearest first. Hit flags (1 hit, 4 kill).</summary>
    private byte PveMelee(Player attacker, Vector3 origin, Vector3 dir, float reach, float coneCos, float damage, double viewTick, double now)
    {
        if (AliveEnemies == 0)
            return 0;
        var rewindTo = Math.Max(viewTick, (double)ServerTick - World.MaxRewindTicks);
        PveEnemy? best = null;
        var bestD = float.MaxValue;
        foreach (var e in Enemies)
        {
            if (!e.Alive)
                continue;
            var box = World.Hitbox(EnemyAt(e, rewindTo), StanceOf(e), e.Yaw);
            var closest = Vector3.Clamp(origin, box.Min, box.Max);
            var dist = (closest - origin).Length();
            if (dist > reach || dist >= bestD)
                continue;
            var toCenter = box.Center - origin;
            if (box.Raycast(origin, dir, reach) == null && (toCenter.LengthSquared() < 1e-4f || Vector3.Dot(Vector3.Normalize(toCenter), dir) < coneCos))
                continue;
            if (toCenter.Length() > 0.3f && World.Query.Raycast(origin, Vector3.Normalize(toCenter), toCenter.Length(), QueryFilter.Shots) < toCenter.Length() - 0.35f)
                continue;
            best = e;
            bestD = dist;
        }
        if (best == null)
            return 0;
        return DamageEnemy(attacker, best, (int)MathF.Round(damage), false, false, MeleeKillId, now) ? (byte)5 : (byte)1;
    }

    /// <summary>Server: a blast (grenade, rocket, mine, barrel, a planted charge): enemies in the radius take damage falling off with distance; walls shield.</summary>
    internal void BlastPve(Player? owner, Vector3 center, float radius, float maxDamage, double now, byte killId = 255)
    {
        if (AliveEnemies == 0)
            return;
        for (var i = 0; i < Enemies.Count; i++)
        {
            var e = Enemies[i];
            if (!e.Alive)
                continue;
            var to = e.Position + new Vector3(0, 1.0f, 0) - center;
            var dist = to.Length();
            if (dist > radius)
                continue;
            if (dist > 0.3f && World.Query.Raycast(center, to / dist, dist, QueryFilter.Shots) < dist - 0.05f)
                continue;
            DamageEnemy(owner, e, (int)MathF.Round(maxDamage * MathF.Pow(1f - dist / radius, 1.4f)), false, true, killId, now);
        }
    }

    // ---- Client: hit prediction against what you see ------------------------------------------------------------

    /// <summary>Client: how far a ray goes before it hits an enemy as drawn (tracers, the hit marker), or <paramref name="dist"/>.</summary>
    public float PveRayDrawn(Vector3 o, Vector3 d, float dist, out bool hit)
    {
        hit = false;
        foreach (var e in Enemies)
        {
            if (!e.Alive)
                continue;
            if (World.Hitbox(e.RenderPos, StanceOf(e), e.RenderYaw).Raycast(o, d, dist) is { } t && t < dist)
            {
                dist = t;
                hit = true;
            }
        }
        return dist;
    }

    // ---- The per-tick driver (from CoopModule.ServerTickPlaying, via GamePveMission.cs) ---------------------------

    private int _pvePathBudget;

    /// <summary>A* searches allowed per server tick for all enemies together.</summary>
    public const int PveSearchesPerTick = 2;

    /// <summary>Server, every tick while a mission plays: think (LOD), move, shoot; drop the dead; recycle once a second.</summary>
    internal void TickEnemies(double now)
    {
        var t0 = Stopwatch.GetTimestamp();
        _pvePathBudget = PveSearchesPerTick;
        for (var i = Enemies.Count - 1; i >= 0; i--)
        {
            var e = Enemies[i];
            if (!e.Alive && now - e.DiedAt > 1.6)
            {
                Enemies.RemoveAt(i);
                ReleaseEnemy(e);
            }
        }
        RefreshOfficers();
        var alive = 0;
        foreach (var e in Enemies)
        {
            if (!e.Alive)
                continue;
            alive++;
            e.PrevPos = e.Sim.Position;
            TickEnemy(e, now); // GamePveAi.cs
            e.History[ServerTick % e.History.Length] = e.Sim.Position;
        }
        if (ServerTick % World.TickRate == 0)
            RecycleEnemies(now);
        if (alive > 0)
        {
            PveAiMs += Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
            PveAiTicks++;
            PveAiEnemyTicks += alive;
        }
    }
}
