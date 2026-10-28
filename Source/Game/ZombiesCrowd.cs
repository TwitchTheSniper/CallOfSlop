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
        var n = Math.Clamp(CrowdOverride > 0 ? CrowdOverride : zm.Crowd, 1, ZombiesMap.MaxCrowd);
        var floor = zm.Areas[zm.StartArea];
        Crowd.Tuning = ZRules.Crowd; // (the rules file's "crowd" section)
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
        var hx = World.Map.HalfX - 1;
        var hz = World.Map.HalfZ - 1;
        for (var tries = 0; Crowd.Count < n && tries < n * 20; tries++)
        {
            var x = (Rand() * 2 - 1) * hx;
            var z = (Rand() * 2 - 1) * hz;
            var dx = x - cx;
            var dz = z - cz;
            if (dx * dx + dz * dz < keep * keep || !Crowd.Walkable(x, z))
                continue;
            Crowd.Add(x, z, 0, 0, (uint)Crowd.Count * 2654435761u ^ rng, ServerTick);
        }
        _zRoundTotal = CrowdRoundTotal = Crowd.Count;
        Console.WriteLine($"[zombies] crowd round {round}: {Crowd.Count} zombies in the crowd ({Crowd.FlowCells} flow cells, {Crowd.Bytes / 1024} KB)");
    }

    /// <summary>
    /// Server, every tick on a crowd map (from <see cref="TickHorde"/>, before the horde controller's tick): demote far
    /// healthy individuals, tick the crowd, promote this tick's nearest candidates. No allocation.
    /// </summary>
    internal void CrowdTick(ZombiesMap zm, double now)
    {
        if (!Crowd.Ready)
            return; // (no crowd round yet)
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
                    Crowd.Add(pos.X, pos.Z, v.X, v.Z, z.Id, ServerTick);
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
        Crowd.Tick(ServerTick, _crowdTX.AsSpan(0, targets), _crowdTZ.AsSpan(0, targets), room);

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
            var at = new Vector3(Crowd.X[i], Crowd.Y, Crowd.Z[i]);
            var vx = Crowd.VX[i];
            var vz = Crowd.VZ[i];
            Crowd.RemoveAt(i);
            var z = SpawnZombie(zm, spawner, now, at);
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
}
