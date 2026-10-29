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
public sealed partial class Zombie
{
    /// <summary>A crowd map (ZombiesCrowd.cs): when it was promoted out of the crowd (woken), and when a player last saw it.</summary>
    public double CrowdWokeAt, CrowdSeenAt;
}

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
        var hp = CrowdZombieHealth(round);
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
        // The players standing, in id order: the crowd's targets, and the views it wakes and sleeps by (CrowdSight.cs).
        var targets = 0;
        var sight = Crowd.Sight;
        sight.Begin(tuning);
        for (var id = 0; id < 256 && targets < _crowdTX.Length; id++)
            if (Players.TryGetValue((byte)id, out var p) && IsStanding(p))
            {
                _crowdTX[targets] = p.Position.X;
                _crowdTZ[targets] = p.Position.Z;
                sight.AddView(p.Position + new Vector3(0, World.EyeHeight, 0), p.Yaw, p.Pitch);
                targets++;
            }
            else if (id > 0 && Players.Count == 0)
                break;
        if (ServerTick % Math.Max(1, tuning.NetEvery) == 0 || _crowdSightViews != targets)
            sight.Occlude(tuning.DemoteRange + 6f); // (the ground between each player and the cells near them, every few ticks)
        _crowdSightViews = targets;

        // Demote (put to sleep): a healthy, plain chasing individual that has been awake a while goes back to the crowd when
        // it counts as far (CrowdSight.Score: its distance, or further when nobody has seen it for a moment) for a while,
        // or, with the individual tier full and crowd zombies waiting, when it counts clearly further than the nearest of
        // them (the furthest-counting first, so what the players can't see sleeps first and what they can see wakes).
        var alive = 0;
        var demoted = 0;
        var pressure = Crowd.Near > 0 && _zAlive >= CrowdIndividualCap ? Crowd.NearBest + tuning.SwapMargin : float.MaxValue;
        _crowdSleepN = 0;
        for (var i = Horde.Count - 1; i >= 0; i--)
        {
            var z = Horde[i];
            if (z.State == ZState.Dead)
                continue;
            alive++;
            if (targets == 0)
                continue;
            var pos = z.Position;
            var score = sight.Score(pos.X, pos.Y + 1f, pos.Z, out var seen, out var nearest);
            if (seen)
                z.CrowdSeenAt = now;
            if (now - z.CrowdSeenAt < tuning.UnseenGrace)
                score = nearest; // (seen a moment ago: it counts at its distance a little longer)
            if (z.State != ZState.Chase || now - z.CrowdWokeAt < tuning.MinAwake || z.Health != z.MaxHealth || z.Limbs != 0
                || z.BurnUntil > now || z.StunnedUntil > now || z.SlowUntil > now)
                continue;
            var far = score > tuning.DemoteRange && now - z.StateAt >= tuning.DemoteSeconds;
            // (Never swapped out near the range it would wake again unseen: a zombie at the player's back stays awake, and
            // one put to sleep doesn't walk back into that range while it rests.)
            if (far || score > pressure && nearest * MathF.Max(1f, tuning.UnseenWeight) > tuning.PromoteRange + tuning.SwapMargin)
                CrowdSleepOffer(i, far ? float.MaxValue : score, tuning.DemotePerTick);
        }
        // The chosen ones, highest index first (removing one leaves the lower indices where they are).
        CrowdSleepSort();
        for (var k = 0; k < _crowdSleepN && Crowd.Count < Crowd.X.Length; k++)
        {
            var i = _crowdSleepIdx[k];
            var z = Horde[i];
            var pos = z.Position;
            var v = (pos - z.PrevPos) / World.TickDt;
            var pi = Crowd.Add(pos.X, pos.Z, v.X, v.Z, z.Id, ServerTick, z.Health);
            if (pi >= 0)
            {
                Crowd.RestUntil[pi] = ServerTick + (long)(tuning.MinAwake * World.TickRate);
                CrowdNoteSleep(Crowd.Look[pi], now);
            }
            sight.Score(pos.X, pos.Y + 1f, pos.Z, out var seenNow, out _);
            if (seenNow)
                CrowdSleptSeen++;
            else
                CrowdSleptUnseen++;
            Horde.RemoveAt(i);
            _zById.Remove(z.Id);
            FreeZombieSlot(z);
            Crowd.Demoted++;
            demoted++;
            alive--;
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
            CrowdNoteWake(Crowd.Look[i], now);
            if (sight.Views > 0)
            {
                sight.Score(at.X, at.Y + 1f, at.Z, out var seenNow, out _);
                if (seenNow)
                    CrowdWokeSeen++;
                else
                    CrowdWokeUnseen++;
            }
            Crowd.RemoveAt(i);
            var z = SpawnZombie(zm, spawner, now, at, type: 0); // (a walker: the crowd carries no types yet, see HordeCrowd.Mass.cs; ZombieTypes.cs)
            z.Health = Math.Clamp((int)MathF.Ceiling(hp), 1, z.MaxHealth); // (a crowd zombie hurt by a blast stays hurt)
            z.Window = -1;
            z.Area = zm.AreaAt(at);
            SetZState(z, ZState.Chase, now); // (straight in: no rising out of the ground)
            z.CrowdWokeAt = z.CrowdSeenAt = now;
            if (vx * vx + vz * vz > 1e-4f)
                z.Yaw = z.RenderYaw = MathF.Atan2(vx, -vz);
            _zAlive++;
            Crowd.Promoted++;
        }
    }

    // ---- Waking and sleeping by sight (docs/zombies/MASS-HORDE.md, "Waking and sleeping") ------------------------

    private int _crowdSightViews = -1;
    private readonly int[] _crowdSleepIdx = new int[64];
    private readonly float[] _crowdSleepScore = new float[64];
    private int _crowdSleepN;

    /// <summary>Keeps the <paramref name="want"/> individuals that count furthest (insertion, furthest first).</summary>
    private void CrowdSleepOffer(int i, float score, int want)
    {
        want = Math.Min(want, _crowdSleepIdx.Length);
        if (want <= 0)
            return;
        var n = _crowdSleepN;
        if (n == want && score <= _crowdSleepScore[n - 1])
            return;
        var k = n < want ? n++ : n - 1;
        while (k > 0 && _crowdSleepScore[k - 1] < score)
        {
            _crowdSleepScore[k] = _crowdSleepScore[k - 1];
            _crowdSleepIdx[k] = _crowdSleepIdx[k - 1];
            k--;
        }
        _crowdSleepScore[k] = score;
        _crowdSleepIdx[k] = i;
        _crowdSleepN = n;
    }

    /// <summary>The chosen individuals by index, highest first.</summary>
    private void CrowdSleepSort()
    {
        for (var a = 1; a < _crowdSleepN; a++)
            for (var b = a; b > 0 && _crowdSleepIdx[b] > _crowdSleepIdx[b - 1]; b--)
                (_crowdSleepIdx[b], _crowdSleepIdx[b - 1]) = (_crowdSleepIdx[b - 1], _crowdSleepIdx[b]);
    }

    // The latest zombies put to sleep (their particle's look) and when: one woken again soon after is a flicker (counted).
    private const int CrowdSleepRing = 128;
    private readonly uint[] _crowdSleptLook = new uint[CrowdSleepRing];
    private readonly double[] _crowdSleptAt = new double[CrowdSleepRing];
    private int _crowdSleptN;

    /// <summary>Zombies woken within <see cref="CrowdFlickerSeconds"/> of being put to sleep (self-test: no flicker over a turn).</summary>
    public int CrowdRewoken;

    public const double CrowdFlickerSeconds = 3;

    /// <summary>Individuals put to sleep that a player sees, and that nobody does (self-test, log).</summary>
    public int CrowdSleptSeen, CrowdSleptUnseen;

    /// <summary>Crowd zombies woken (promoted) that a player saw, and that nobody did (near enough to wake unseen; self-test, log).</summary>
    public int CrowdWokeSeen, CrowdWokeUnseen;

    private void CrowdNoteSleep(uint look, double now)
    {
        var k = _crowdSleptN++ & (CrowdSleepRing - 1);
        _crowdSleptLook[k] = look;
        _crowdSleptAt[k] = now;
    }

    private void CrowdNoteWake(uint look, double now)
    {
        for (var k = 0; k < Math.Min(_crowdSleptN, CrowdSleepRing); k++)
            if (_crowdSleptLook[k] == look && now - _crowdSleptAt[k] < CrowdFlickerSeconds)
            {
                CrowdRewoken++;
                _crowdSleptAt[k] = double.MinValue;
                return;
            }
    }

    /// <summary>A crowd zombie's health in round <paramref name="round"/>: an individual walker's (the round's health and the walker type's multiplier, as TypeSetup gives it).</summary>
    internal float CrowdZombieHealth(int round)
    {
        var t = ZRules.TypeAt(0);
        var hp = ZRules.ZombieHealth(Math.Max(1, round));
        return t.HealthFixed > 0 ? t.HealthFixed : Math.Max(1, (int)MathF.Round(hp * t.HealthMult));
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

    // A trigger pull's hits on mass zombies (cell, slot), added up per zombie like individuals', and the first one's ray (pierce).
    private readonly int[] _massShotCell = new int[CrowdShotMax], _massShotSlot = new int[CrowdShotMax];
    private readonly float[] _massShotDmg = new float[CrowdShotMax];
    private readonly bool[] _massShotHead = new bool[CrowdShotMax];
    private int _massShotN;
    private (Vector3 O, Vector3 D, float Dist) _massShotRay;

    /// <summary>Crowd zombies killed by shots and blasts (log, self-test), and drops rolled for them.</summary>
    public int CrowdShotKills, CrowdBlastKills, CrowdDropRolls;

    /// <summary>Hits on mass zombies by shots (self-test, log), and the last one's spot (the self-test aims there).</summary>
    public int MassShotHits, MassShotKills;

    /// <summary>The most drops and power-ups rolled for one blast's crowd kills (a blast in a crowd of hundreds stays readable). Points are never capped.</summary>
    public const int CrowdBlastDrops = 4;

    /// <summary>
    /// Server, per ray of a shot that hit no individual zombie: the nearest crowd zombie on it, a particle or a mass
    /// zombie (a shot past the front into the crowd, or into the far horde). Shortens <paramref name="dist"/>. Returns
    /// whether it hit one.
    /// </summary>
    internal bool CrowdRayHit(Vector3 o, Vector3 d, ref float dist, float rayDamage, WeaponDef def, float headMult)
    {
        if (!CrowdOn || Crowd.Total == 0)
            return false;
        var head = false;
        var i = Crowd.Count > 0 ? Crowd.RayHit(o, d, ref dist, out head) : -1;
        if (Crowd.MassTotal > 0 && Crowd.MassRayHit(o, d, ref dist, -1f, out var cell, out var slot, out var mHead))
        {
            var damageAt = rayDamage * def.DamageMultAt(dist) * PenFactor(dist); // (at the hit, as on an individual)
            CrowdMassShotAdd(cell, slot, damageAt * (mHead ? headMult * ZRules.ZombieHeadMult : 1f), mHead, (o, d, dist));
            return true; // (nearer than the particle, if any: the mass test ran to the particle's distance)
        }
        if (i < 0)
            return false;
        var dmg = rayDamage * def.DamageMultAt(dist) * PenFactor(dist) * (head ? headMult * ZRules.ZombieHeadMult : 1f);
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

    private void CrowdMassShotAdd(int cell, int slot, float dmg, bool head, (Vector3 O, Vector3 D, float Dist) ray)
    {
        for (var k = 0; k < _massShotN; k++)
            if (_massShotCell[k] == cell && _massShotSlot[k] == slot)
            {
                _massShotDmg[k] += dmg;
                _massShotHead[k] |= head;
                return;
            }
        if (_massShotN >= CrowdShotMax)
            return;
        if (_massShotN == 0)
            _massShotRay = ray;
        _massShotCell[_massShotN] = cell;
        _massShotSlot[_massShotN] = slot;
        _massShotDmg[_massShotN] = dmg;
        _massShotHead[_massShotN] = head;
        _massShotN++;
    }

    /// <summary>Server: a trigger pull's damage on crowd and mass zombies (one event each, like individuals). Returns hit flags (1 hit, 2 head, 4 kill).</summary>
    internal byte CrowdShotApply(Player shooter, WeaponDef def, double now)
    {
        byte flags = 0;
        var n = _crowdShotN;
        _crowdShotN = 0;
        if (n > 0)
        {
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
        }
        var m = _massShotN;
        if (m > 0)
        {
            // Penetrating rounds go on through the next mass zombies behind the first, as through individuals (ZombiesAmmoEffects.cs, Pierce).
            var mainDmg = 0f;
            for (var k = 0; k < m; k++)
                mainDmg = MathF.Max(mainDmg, _massShotDmg[k]);
            if (def.BuildFlags.Length > 0 && (ZEffectFlags(def.BuildFlags) & (uint)ZEffect.Penetrating) != 0)
                CrowdMassPierce(MathF.Round(mainDmg));
            // Per cell, highest slot first (a kill moves the cell's top slot into the dead one's).
            for (var a = 1; a < _massShotN; a++)
                for (var b = a; b > 0 && MassShotAfter(b, b - 1); b--)
                {
                    (_massShotCell[b], _massShotCell[b - 1]) = (_massShotCell[b - 1], _massShotCell[b]);
                    (_massShotSlot[b], _massShotSlot[b - 1]) = (_massShotSlot[b - 1], _massShotSlot[b]);
                    (_massShotDmg[b], _massShotDmg[b - 1]) = (_massShotDmg[b - 1], _massShotDmg[b]);
                    (_massShotHead[b], _massShotHead[b - 1]) = (_massShotHead[b - 1], _massShotHead[b]);
                }
            for (var k = 0; k < _massShotN; k++)
            {
                var head = _massShotHead[k];
                flags |= (byte)(head ? 3 : 1);
                MassShotHits++;
                if (CrowdMassDamage(shooter, _massShotCell[k], _massShotSlot[k], _massShotDmg[k], head, explosive: false, drops: true, now))
                {
                    flags |= 4;
                    CrowdShotKills++;
                    MassShotKills++;
                }
            }
            _massShotN = 0;
        }
        if ((n > 0 || m > 0) && Horde.Count == 0)
            HordeGunfire(shooter, now); // (ZombieShotApply already told the horde otherwise)
        return flags;
    }

    /// <summary>Should mass hit <paramref name="b"/> come before <paramref name="a"/> (cell ascending, slot descending)?</summary>
    private bool MassShotAfter(int b, int a) =>
        _massShotCell[b] < _massShotCell[a] || (_massShotCell[b] == _massShotCell[a] && _massShotSlot[b] > _massShotSlot[a]);

    /// <summary>A penetrating round: on along the first mass hit's ray through up to <see cref="ZombiesRules.PierceTargets"/> more mass zombies (until a wall).</summary>
    private void CrowdMassPierce(float mainDamage)
    {
        var (o, d, dist) = _massShotRay;
        var end = MathF.Min(dist + ZRules.PierceRange, World.Query.Raycast(o, d, dist + ZRules.PierceRange, QueryFilter.Shots));
        var after = dist + 1e-3f;
        var dmg = MathF.Max(1, MathF.Round(mainDamage * ZRules.PierceFraction));
        for (var p = 0; p < ZRules.PierceTargets; p++)
        {
            var reach = end;
            if (!Crowd.MassRayHit(o, d, ref reach, after, out var cell, out var slot, out _))
                break;
            Fx(ZFxKind.Pierce, o + d * reach, Sfx.FleshHit3D);
            CrowdMassShotAdd(cell, slot, dmg, false, default);
            after = reach + 1e-3f;
        }
    }

    /// <summary>
    /// Server: a blast in the crowd: every crowd zombie in the radius, particle or mass, takes the blast's damage falling
    /// off with distance (as individuals do): points for each hit and kill credit for each dead one, like individuals;
    /// at most <see cref="CrowdBlastDrops"/> drop and power-up rolls for the blast.
    /// </summary>
    internal void CrowdBlast(Player owner, Vector3 center, float radius, float maxDamage, double now)
    {
        if (!CrowdOn || Crowd.Total == 0 || MathF.Abs(center.Y - Crowd.GroundAt(center.X, center.Z)) > radius + HordeCrowd.BodyHeight)
            return;
        var drops = 0;
        if (Crowd.Count > 0)
        {
            var n = Crowd.Within(center, radius, _crowdHits);
            Array.Sort(_crowdHits, 0, n);
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
        }
        if (Crowd.MassTotal == 0)
            return;
        // The mass: each of its zombies in the radius (at its spot: HordeCrowd.MassHits.cs), highest slot first in each cell.
        Crowd.MassCellsNear(center, radius, out var c0, out var c1, out var r0, out var r1);
        var cols = Crowd.MassCols;
        for (var r = r0; r <= r1; r++)
            for (var c = c0; c <= c1; c++)
            {
                var cell = r * cols + c;
                for (var slot = Crowd.MassCount[cell] - 1; slot >= 0; slot--)
                {
                    var feet = Crowd.MassFeet(cell, slot);
                    var dist = (feet + new Vector3(0, 1.1f, 0) - center).Length();
                    if (dist > radius)
                        continue;
                    var dmg = maxDamage * MathF.Pow(1f - dist / radius, 1.4f);
                    if (CrowdMassDamage(owner, cell, slot, dmg, head: false, explosive: true, drops: drops < CrowdBlastDrops, now))
                    {
                        drops++;
                        CrowdBlastKills++;
                        CrowdMassKills++;
                    }
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
        amount = MathF.Round(amount); // (whole points of damage, as DamageZombie takes them)
        if (amount <= 0 || !DamageEnabled)
            return false;
        if (now < ZInstaKillUntil)
            amount = MathF.Max(amount, Crowd.HP[i]);
        Crowd.HP[i] -= amount;
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

    /// <summary>
    /// Server: damage on mass zombie <paramref name="slot"/> of cell <paramref name="cell"/> (HordeCrowd.MassHits.cs),
    /// exactly as on an individual (DamageZombie): the damage rounded, Insta-Kill, points for the hit, or the kill's
    /// credit; a wounded survivor keeps its health. Returns true if it killed.
    /// </summary>
    private bool CrowdMassDamage(Player attacker, int cell, int slot, float amount, bool head, bool explosive, bool drops, double now)
    {
        amount = MathF.Round(amount); // (whole points of damage, as DamageZombie takes them)
        if (amount <= 0 || !DamageEnabled || slot >= Crowd.MassCount[cell])
            return false;
        var hp = Crowd.MassHealth(cell, slot);
        if (now < ZInstaKillUntil)
            amount = MathF.Max(amount, hp);
        hp -= amount;
        attacker.LastCombatTime = now;
        if (hp > 0)
        {
            Crowd.SetMassHealth(cell, slot, hp);
            EarnPoints(attacker, ZRules.HitPoints, now);
            return false;
        }
        var at = Crowd.MassFeet(cell, slot);
        Crowd.KillMass(cell, slot);
        CrowdKillCredit(attacker, at, head, explosive, drops, now);
        return true;
    }

    /// <summary>Mass zombies killed by blasts (log, self-test).</summary>
    public int CrowdMassKills;

    /// <summary>
    /// Server: one crowd or mass zombie killed by <paramref name="attacker"/> at <paramref name="at"/>, credited exactly
    /// as an individual's kill (DamageZombie): the count, kills, headshots, frenzy, kill points (a walker's: the crowd
    /// holds walkers only), and the drop and power-up rolls when <paramref name="drops"/>.
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
        EarnPoints(attacker, (int)MathF.Round(killPoints * ZRules.TypeAt(0).PointsMult), now);
        if (drops)
        {
            CrowdDropRolls++;
            RollDrop(at, ZRound, now, PerkMult(zp, static d => d.PartDrops));
            RollPowerUp(at, now, zp);
        }
    }
}
