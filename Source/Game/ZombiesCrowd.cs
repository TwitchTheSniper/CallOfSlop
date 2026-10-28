using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The mass horde's crowd tier in the Zombies mode (docs/zombies/MASS-HORDE.md, the spike): only on maps whose section
/// says <c>"crowd": N</c> (Dev/MassHordeTest.json). Each round is N zombies, seeded as crowd particles
/// (<see cref="HordeCrowd"/>) out in the field; near the players the nearest are promoted to full zombies (the
/// individual tier: AI, collision, hit boxes, animation and network as ever), a few a tick up to the individual cap;
/// healthy individuals far from everyone go back to the crowd (hysteresis: promote under 22 m, demote over 34 m held
/// for 2 s). Counts always add up: crowd + alive + killed = the round's total. Every other map: nothing here runs.
/// </summary>
public sealed partial class Game
{
    /// <summary>The crowd tier (server; empty on every map without "crowd").</summary>
    public readonly HordeCrowd Crowd = new();

    /// <summary>Self-test: the next crowd round's size instead of the map's (0: the map's).</summary>
    public int CrowdOverride;

    /// <summary>The round's crowd total and the zombies killed in it (the conservation check: crowd + alive + killed = total).</summary>
    public int CrowdRoundTotal, CrowdRoundKills;

    private readonly float[] _crowdTX = new float[HordeController<Zombie>.MaxTargets], _crowdTZ = new float[HordeController<Zombie>.MaxTargets];
    private readonly int[] _crowdPick = new int[64];

    /// <summary>Is this a crowd map (in the Zombies mode)?</summary>
    public bool CrowdOn => ZMap is { Crowd: > 0 };

    /// <summary>The most individuals at once on a crowd map: the crowd's cap, the host's horde cap.</summary>
    public int CrowdIndividualCap => Math.Clamp(Math.Min(ZRules.Crowd.IndividualCap, HostHordeCap), 1, MaxZombies);

    /// <summary>Server, at a crowd round's start: the whole round seeded as particles over the field, away from the players.</summary>
    private void CrowdBeginRound(ZombiesMap zm, int round, double now)
    {
        if (zm.Stress != null)
        {
            StressBeginRound(zm, zm.Stress, round, now); // a stress map: the hordes on their staging areas (ZombiesCrowdStress.cs)
            return;
        }
        var n = Math.Clamp(CrowdOverride > 0 ? CrowdOverride : zm.Crowd, 1, ZombiesMap.MaxCrowd);
        var floor = zm.Areas[zm.StartArea];
        Crowd.Tuning = ZRules.Crowd; // (the rules file's "crowd" section)
        Crowd.SetGoal(false);
        Crowd.Hold = false;
        StressState = StressPhase.Off;
        Crowd.Setup(World.Map, floor.Y0 > -1e5f && floor.Y0 < 1e5f ? floor.Y0 : 0f, n);
        Crowd.Clear();
        _crowdRound = (_crowdRound + 1) & 0xFFFF; // (clients get a keyframe: ZombiesCrowdNet.cs)
        _zToSpawn = 0;
        _zRoundTotal = n;
        CrowdRoundTotal = n;
        CrowdRoundKills = 0;
        // The players' middle: the start area's centre.
        var cx = (floor.X0 + floor.X1) / 2;
        var cz = (floor.Z0 + floor.Z1) / 2;
        var keep = Crowd.Tuning.PromoteRange + 2; // (just outside the promote range: the first arrive in a second or two)
        var rng = HordeCrowd.Hash(ZSeed ^ (uint)(round * 7919) ^ 0xC20D);
        float Rand()
        {
            rng ^= rng << 13;
            rng ^= rng >> 17;
            rng ^= rng << 5;
            return (rng >> 8) / 16777216f;
        }
        var hp = ZRules.ZombieHealth(Math.Max(1, round));
        var hx = World.Map.HalfX - 1;
        var hz = World.Map.HalfZ - 1;
        Crowd.MassHP = hp;
        var massFrom = Crowd.Tuning.Mass ? Crowd.Tuning.MassRange + Crowd.Tuning.MassHysteresis : 0f;
        for (var tries = 0; Crowd.Total < n && tries < n * 20; tries++)
        {
            var x = (Rand() * 2 - 1) * hx;
            var z = (Rand() * 2 - 1) * hz;
            var dx = x - cx;
            var dz = z - cz;
            if (dx * dx + dz * dz < keep * keep || !Crowd.Walkable(x, z))
                continue;
            if (massFrom > 0 && dx * dx + dz * dz > massFrom * massFrom)
                Crowd.AddMass(x, z); // far out: straight into the mass tier (HordeCrowd.Mass.cs)
            else
                Crowd.Add(x, z, 0, 0, (uint)Crowd.Count * 2654435761u ^ rng, ServerTick, hp);
        }
        _zRoundTotal = CrowdRoundTotal = Crowd.Total;
        Console.WriteLine($"[zombies] crowd round {round}: {Crowd.Total} zombies in the crowd ({Crowd.MassTotal} of them in the mass tier) ({Crowd.FlowCells} flow cells, {Crowd.Bytes / 1024} KB)");
    }

    /// <summary>
    /// Server, every tick on a crowd map (from <see cref="TickHorde"/>, before the horde controller's tick): demote far
    /// healthy individuals, tick the crowd, promote this tick's nearest candidates. No allocation.
    /// </summary>
    internal void CrowdTick(ZombiesMap zm, double now)
    {
        if (!Crowd.Ready)
            return; // (no crowd round yet)
        if (zm.Stress is { } stress)
            StressTick(stress, now); // a stress map: staging, descent, merge, waves (ZombiesCrowdStress.cs)
        var tuning = Crowd.Tuning;
        // The players standing, in id order.
        var targets = 0;
        for (var id = 0; id < 256 && targets < _crowdTX.Length; id++)
            if (Players.TryGetValue((byte)id, out var p) && IsStanding(p))
            {
                _crowdTX[targets] = p.Position.X;
                _crowdTZ[targets] = p.Position.Z;
                targets++;
            }
            else if (id > 0 && Players.Count == 0)
                break;

        // Demote: healthy, plain chasing individuals far from everyone for a while go back to the crowd.
        var alive = 0;
        var demoted = 0;
        var dr2 = tuning.DemoteRange * tuning.DemoteRange;
        for (var i = Horde.Count - 1; i >= 0; i--)
        {
            var z = Horde[i];
            if (z.State == ZState.Dead)
                continue;
            if (demoted < tuning.DemotePerTick && targets > 0 && z.State == ZState.Chase && now - z.StateAt >= tuning.DemoteSeconds
                && z.Health == z.MaxHealth && z.Limbs == 0 && z.BurnUntil <= now && z.StunnedUntil <= now && z.SlowUntil <= now && Crowd.Count < Crowd.X.Length)
            {
                var pos = z.Position;
                var far = true;
                for (var t = 0; t < targets && far; t++)
                {
                    var dx = _crowdTX[t] - pos.X;
                    var dz = _crowdTZ[t] - pos.Z;
                    far = dx * dx + dz * dz > dr2;
                }
                if (far)
                {
                    var v = (pos - z.PrevPos) / World.TickDt;
                    Crowd.Add(pos.X, pos.Z, v.X, v.Z, z.Id, ServerTick, z.Health);
                    Horde.RemoveAt(i);
                    _zById.Remove(z.Id);
                    FreeZombieSlot(z);
                    Crowd.Demoted++;
                    demoted++;
                    continue;
                }
            }
            alive++;
        }

        // The individuals push the crowd's particles.
        var b = 0;
        foreach (var z in Horde)
            if (z.State != ZState.Dead && b < Crowd.BlockX.Length)
            {
                Crowd.BlockX[b] = z.Position.X;
                Crowd.BlockZ[b] = z.Position.Z;
                b++;
            }
        Crowd.BlockCount = b;

        var room = Math.Max(0, CrowdIndividualCap - alive);
        var ms0 = Crowd.TickMs;
        Crowd.Tick(ServerTick, _crowdTX.AsSpan(0, targets), _crowdTZ.AsSpan(0, targets), room);
        _crowdLastTickMs = Crowd.TickMs - ms0;
        CrowdPerfSample(); // the F9 window's one-second figures (ZombiesCrowdStress.cs)
        if (ServerTick % Math.Max(1, tuning.NetEvery) == 0)
            CrowdCount(); // the cells' counts: the network's (ZombiesCrowdNet.cs) and the crowd surface's (ZombiesCrowdSurface.cs)

        // Promote this tick's nearest candidates (removed from the crowd highest index first, so the others keep theirs).
        var n = Math.Min(Crowd.CandidateCount, Math.Min(room, _crowdPick.Length));
        if (n == 0)
            return;
        Array.Copy(Crowd.Candidates, _crowdPick, n);
        Array.Sort(_crowdPick, 0, n);
        var spawner = zm.Spawners.Count > 0 ? zm.Spawners.FindIndex(s => s.Window < 0) : -1;
        if (spawner < 0)
            spawner = 0;
        for (var k = n - 1; k >= 0; k--)
        {
            var i = _crowdPick[k];
            var at = new Vector3(Crowd.X[i], Crowd.GroundAt(Crowd.X[i], Crowd.Z[i]), Crowd.Z[i]);
            var vx = Crowd.VX[i];
            var vz = Crowd.VZ[i];
            var hp = Crowd.HP[i];
            Crowd.RemoveAt(i);
            var z = SpawnZombie(zm, spawner, now, at);
            z.Health = Math.Clamp((int)MathF.Ceiling(hp), 1, z.MaxHealth); // (a crowd zombie hurt by a blast stays hurt)
            z.Window = -1;
            z.Area = zm.AreaAt(at);
            SetZState(z, ZState.Chase, now); // (straight in: no rising out of the ground)
            if (vx * vx + vz * vz > 1e-4f)
                z.Yaw = z.RenderYaw = MathF.Atan2(vx, -vz);
            _zAlive++;
            Crowd.Promoted++;
        }
    }

    /// <summary>Server: a zombie died (kill credit as ever); the crowd round's conservation count.</summary>
    private void CrowdNoteKill()
    {
        if (CrowdRoundTotal > 0)
            CrowdRoundKills++;
    }

    // ---- Combat against the crowd (docs/zombies/MASS-HORDE.md, "Combat") --------------------------------------------

    private const int CrowdShotMax = 64;
    private readonly int[] _crowdShotIdx = new int[CrowdShotMax];
    private readonly float[] _crowdShotDmg = new float[CrowdShotMax];
    private readonly bool[] _crowdShotHead = new bool[CrowdShotMax];
    private int _crowdShotN;
    private readonly int[] _crowdHits = new int[4096];

    /// <summary>Crowd zombies killed by shots and blasts (log, self-test), and drops rolled for them.</summary>
    public int CrowdShotKills, CrowdBlastKills, CrowdDropRolls;

    /// <summary>The most drops and power-ups rolled for one blast's crowd kills (a blast in a crowd of hundreds stays readable).</summary>
    public const int CrowdBlastDrops = 4;

    /// <summary>
    /// Server, per ray of a shot that hit no individual zombie: the nearest crowd zombie on it (a shot past the front
    /// into the crowd). Shortens <paramref name="dist"/>. Returns whether it hit one.
    /// </summary>
    internal bool CrowdRayHit(Vector3 o, Vector3 d, ref float dist, float damageAt1, float headMult)
    {
        if (!CrowdOn || Crowd.Count == 0)
            return false;
        var i = Crowd.RayHit(o, d, ref dist, out var head);
        if (i < 0)
            return false;
        var dmg = damageAt1 * (head ? headMult * ZRules.ZombieHeadMult : 1f);
        for (var k = 0; k < _crowdShotN; k++)
            if (_crowdShotIdx[k] == i)
            {
                _crowdShotDmg[k] += dmg;
                _crowdShotHead[k] |= head;
                return true;
            }
        if (_crowdShotN < CrowdShotMax)
        {
            _crowdShotIdx[_crowdShotN] = i;
            _crowdShotDmg[_crowdShotN] = dmg;
            _crowdShotHead[_crowdShotN] = head;
            _crowdShotN++;
        }
        return true;
    }

    /// <summary>Server: a trigger pull's damage on crowd zombies (one event each, like individuals). Returns hit flags (1 hit, 2 head, 4 kill).</summary>
    internal byte CrowdShotApply(Player shooter, double now)
    {
        byte flags = 0;
        var n = _crowdShotN;
        _crowdShotN = 0;
        if (n == 0)
            return 0;
        // Highest index first (removing one moves the last particle into its place).
        for (var a = 1; a < n; a++)
            for (var b = a; b > 0 && _crowdShotIdx[b] > _crowdShotIdx[b - 1]; b--)
            {
                (_crowdShotIdx[b], _crowdShotIdx[b - 1]) = (_crowdShotIdx[b - 1], _crowdShotIdx[b]);
                (_crowdShotDmg[b], _crowdShotDmg[b - 1]) = (_crowdShotDmg[b - 1], _crowdShotDmg[b]);
                (_crowdShotHead[b], _crowdShotHead[b - 1]) = (_crowdShotHead[b - 1], _crowdShotHead[b]);
            }
        for (var k = 0; k < n; k++)
        {
            var i = _crowdShotIdx[k];
            if (i >= Crowd.Count)
                continue;
            var head = _crowdShotHead[k];
            flags |= (byte)(head ? 3 : 1);
            if (CrowdDamage(shooter, i, _crowdShotDmg[k], head, explosive: false, drops: true, now))
            {
                flags |= 4;
                CrowdShotKills++;
            }
        }
        if (Horde.Count == 0)
            HordeGunfire(shooter, now); // (ZombieShotApply already told the horde otherwise)
        return flags;
    }

    /// <summary>
    /// Server: a blast in the crowd: every crowd zombie in the radius takes the blast's damage falling off with distance
    /// (as individuals do); the dead are taken out of the crowd by count, each credited to the thrower as a kill.
    /// </summary>
    internal void CrowdBlast(Player owner, Vector3 center, float radius, float maxDamage, double now)
    {
        if (!CrowdOn || Crowd.Total == 0 || MathF.Abs(center.Y - Crowd.GroundAt(center.X, center.Z)) > radius + HordeCrowd.BodyHeight)
            return;
        var n = Crowd.Within(center, radius, _crowdHits);
        Array.Sort(_crowdHits, 0, n);
        var drops = 0;
        for (var k = n - 1; k >= 0; k--)
        {
            var i = _crowdHits[k];
            if (i >= Crowd.Count)
                continue;
            var to = new Vector3(Crowd.X[i], Crowd.GroundAt(Crowd.X[i], Crowd.Z[i]) + 1.1f, Crowd.Z[i]) - center;
            var dist = to.Length();
            if (dist > radius)
                continue;
            var dmg = maxDamage * MathF.Pow(1f - dist / radius, 1.4f);
            if (CrowdDamage(owner, i, dmg, head: false, explosive: true, drops: drops < CrowdBlastDrops, now))
            {
                CrowdBlastKills++;
                drops++;
            }
        }
        // The mass: the cells the blast covers lose their dead by count.
        var cells = Crowd.BlastMass(center, radius, maxDamage, _crowdMassKills);
        for (var k = 0; k < cells; k++)
        {
            var (cell, dead) = _crowdMassKills[k];
            var at = Crowd.MassCentre(cell);
            for (var d = 0; d < dead; d++)
            {
                CrowdKillCredit(owner, at, head: false, explosive: true, drops: drops < CrowdBlastDrops, now);
                drops++;
                CrowdBlastKills++;
                CrowdMassKills++;
            }
        }
    }

    /// <summary>
    /// Server: damage on crowd zombie <paramref name="i"/>: points for the hit, or for the kill exactly as an individual
    /// (DamageZombie's kill: kills, headshots, frenzy, kill points, drops and power-ups when <paramref name="drops"/>);
    /// a dead one leaves the crowd. Returns true if it killed.
    /// </summary>
    private bool CrowdDamage(Player attacker, int i, float amount, bool head, bool explosive, bool drops, double now)
    {
        if (amount <= 0 || !DamageEnabled)
            return false;
        if (now < ZInstaKillUntil)
            amount = MathF.Max(amount, Crowd.HP[i]);
        Crowd.HP[i] -= MathF.Round(amount);
        attacker.LastCombatTime = now;
        if (Crowd.HP[i] > 0)
        {
            EarnPoints(attacker, ZRules.HitPoints, now);
            return false;
        }
        var at = new Vector3(Crowd.X[i], Crowd.GroundAt(Crowd.X[i], Crowd.Z[i]), Crowd.Z[i]);
        Crowd.RemoveAt(i);
        CrowdKillCredit(attacker, at, head, explosive, drops, now);
        return true;
    }

    private readonly (int Cell, int Count)[] _crowdMassKills = new (int, int)[256];

    /// <summary>Mass zombies killed by blasts (log, self-test).</summary>
    public int CrowdMassKills;

    /// <summary>
    /// Server: one crowd or mass zombie killed by <paramref name="attacker"/> at <paramref name="at"/>, credited exactly
    /// as an individual's kill (DamageZombie): the count, kills, headshots, frenzy, kill points, and the drop and
    /// power-up rolls when <paramref name="drops"/>.
    /// </summary>
    private void CrowdKillCredit(Player attacker, Vector3 at, bool head, bool explosive, bool drops, double now)
    {
        CrowdNoteKill();
        var zp = ZP(attacker);
        zp.Kills++;
        if (head)
            zp.Headshots++;
        attacker.Kills++;
        FrenzyOnZombieKill(attacker, head, explosive, now);
        var killPoints = head ? ZRules.HeadshotKillPoints + (int)PerkSum(zp, static d => d.HeadshotPoints) : ZRules.KillPoints;
        EarnPoints(attacker, killPoints, now);
        if (drops)
        {
            CrowdDropRolls++;
            RollDrop(at, ZRound, now, PerkMult(zp, static d => d.PartDrops));
            RollPowerUp(at, now, zp);
        }
    }
}
