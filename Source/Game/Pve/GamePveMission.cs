using System.Numerics;

namespace CallOfSlop;

/// <summary>Where a co-op mission run is.</summary>
public enum CoopStatus : byte
{
    /// <summary>No mission running (no mission for this map, or before the match).</summary>
    Idle,

    Active,

    /// <summary>A wipe or a failed stage: a few seconds, then the stage restarts at its checkpoint.</summary>
    Regroup,

    Complete,
    Failed,
}

/// <summary>A destroy target's state (or a collect item's: Intact = on the ground, Planted = carried, Destroyed = delivered).</summary>
public enum CoopTarget : byte
{
    Intact,
    Planted,
    Destroyed,
}

/// <summary>
/// The co-op objective state: what the HUD shows. The server owns it; clients get it in the mode-state tail
/// (<see cref="CoopModule.WriteState"/>, 10 Hz).
/// </summary>
public sealed class CoopObjective
{
    public const int MaxTargets = 8;

    /// <summary>Index into <see cref="Missions.All"/> (-1: none).</summary>
    public int Mission = -1;

    public int Stage, StageCount;
    public StageKind Kind;
    public CoopStatus Status;
    public int Progress, Goal;

    /// <summary>Seconds: left on a defend timer or a time limit, else the stage's elapsed time.</summary>
    public float Timer;

    /// <summary>Defend: the loss meter (0-1).</summary>
    public float Lose;

    public readonly CoopTarget[] Targets = new CoopTarget[MaxTargets];

    /// <summary>Destroy: planting progress (0-1) while intact, fuse left (s) once planted. Collect: unused.</summary>
    public readonly float[] TargetValue = new float[MaxTargets];

    /// <summary>Collect: where each item is now (on the ground or with its carrier).</summary>
    public readonly Vector3[] ItemPos = new Vector3[MaxTargets];

    public int TargetCount;
    public int Wipes, Retries;
    public bool NamedAlive;
    public Vector3 NamedPos;
    public int Alive, Budget, Wave;
    public DirectorPhase Phase;
    public float RespawnSeconds = 10;

    /// <summary>Reach: players in the zone, and how many must be.</summary>
    public int InZone, NeedInZone;

    public BotDifficulty Difficulty = BotDifficulty.Normal;

    public void Clear()
    {
        Mission = -1;
        Override = null;
        Stage = StageCount = Progress = Goal = TargetCount = Wipes = Alive = Budget = Wave = InZone = NeedInZone = 0;
        Status = CoopStatus.Idle;
        Timer = Lose = 0;
        NamedAlive = false;
        Array.Clear(Targets);
        Array.Clear(TargetValue);
        Array.Clear(ItemPos);
    }

    /// <summary>Self-test: a mission built in code, played instead of the map's (server only).</summary>
    public Mission? Override;

    /// <summary>The mission being played (null: none).</summary>
    public Mission? MissionDef => Override ?? (Mission >= 0 && Mission < Missions.All.Count ? Missions.All[Mission] : null);

    public MissionStage? StageDef => MissionDef is { } m && Stage >= 0 && Stage < m.Stages.Count ? m.Stages[Stage] : null;
}

/// <summary>
/// The co-op mission runner (server; docs/pve/DESIGN.md "Mode structure"): stages in order, each objective type's rules,
/// checkpoints, wipes and retries, the director's squads, respawn timing, XP. The objective state lives in
/// <see cref="Coop"/> for the HUD.
/// </summary>
public sealed partial class Game
{
    /// <summary>The co-op objective state (both sides).</summary>
    public readonly CoopObjective Coop = new();

    /// <summary>Co-op XP (docs/pve/DESIGN.md "Progression").</summary>
    public const int CoopKillXp = 20, CoopHeadshotXp = 10, CoopObjectiveXp = 150, CoopMissionXp = 1000;

    private int _coopRunMatch = -1;
    private double _coopStageStart, _coopRegroupUntil;
    private bool _coopHadAlive;
    private int _coopCheckpointStage = -1;
    private readonly Dictionary<byte, double> _coopDiedAt = new();
    private readonly HashSet<byte> _coopDeployed = new();
    private readonly List<(Vector3 At, float Radius)> _coopZones = new();
    private readonly List<Vector3> _coopCheckpoint = new();
    private readonly Vector3[] _coopTargetAt = new Vector3[CoopObjective.MaxTargets];
    private readonly float[] _coopTargetRadius = new float[CoopObjective.MaxTargets];
    private readonly byte[] _coopCarrier = new byte[CoopObjective.MaxTargets];
    private Vector3 _coopFocus, _coopAt, _coopDeliver;
    private ushort _coopNamedId;
    private string _coopRestartReason = "";

    /// <summary>The players on the mission (people and bots): how the budget and the kill counts scale.</summary>
    public int CoopSquadSize => Math.Max(1, Players.Count(p => p.Value.Bot != null || _coopDeployed.Contains(p.Key) || p.Value.Alive));

    /// <summary>Server: the current stage's focus (the director's attack point, the HUD arrow).</summary>
    public Vector3 CoopFocus => _coopFocus;

    // ---- The run ---------------------------------------------------------------------------------------------

    /// <summary>Server and client: a new match or a mode change: no mission, no enemies.</summary>
    internal void ResetCoopRun()
    {
        ClearEnemies();
        Coop.Clear();
        _coopRunMatch = -1;
        _coopDiedAt.Clear();
        _coopDeployed.Clear();
        _coopCheckpointStage = -1;
        _coopCheckpoint.Clear();
        _coopZones.Clear();
        PvePeakAlive = PveSpawnedTotal = PveRecycledTotal = 0;
        PveShotsFired = PveShotsHit = PveGrenadesThrown = 0;
    }

    private void StartCoopRun(double now, Mission? forced = null)
    {
        _coopRunMatch = MatchNumber;
        ClearEnemies();
        Coop.Clear();
        var mission = forced ?? Missions.For(World.Map);
        if (mission == null)
            return;
        Coop.Override = forced;
        Coop.Mission = forced != null ? 0 : IndexOfMission(mission);
        Coop.StageCount = mission.Stages.Count;
        Coop.Difficulty = BotSkill;
        Coop.Retries = PveDifficulty.Retries(BotSkill);
        Coop.RespawnSeconds = PveDifficulty.Respawn(BotSkill) * mission.RespawnScale;
        _pveRng = 0x51ED2701u ^ (uint)MatchNumber * 2654435761u;
        _coopCheckpointStage = -1;
        StartStage(0, now, restart: false);
        ModeEvent($"Co-op on {World.Map.Name}: {mission.Name}. {mission.Stages.Count} objectives, difficulty {BotSkill}.", Sfx.StreakCall);
        Console.WriteLine($"[coop] {mission.Name} started: {Players.Count} player(s), budget {Coop.Budget}, difficulty {BotSkill}");
    }

    private static int IndexOfMission(Mission m)
    {
        for (var i = 0; i < Missions.All.Count; i++)
            if (ReferenceEquals(Missions.All[i], m))
                return i;
        return -1;
    }

    /// <summary>Server, every tick while a co-op match is playing (CoopModule.ServerTickPlaying).</summary>
    internal void ServerTickCoop(double now)
    {
        if (_coopRunMatch != MatchNumber)
            StartCoopRun(now);
        if (Coop.MissionDef is not { } mission || Coop.StageDef is not { } st)
            return;
        foreach (var p in Players.Values)
            if (p.Alive)
            {
                _coopHadAlive = true;
                break;
            }

        switch (Coop.Status)
        {
            case CoopStatus.Regroup:
                if (now >= _coopRegroupUntil)
                    RestartStage(now);
                break;
            case CoopStatus.Active:
            {
                // The director: squads out of sight, never over the budget.
                PveAttackPoint = _coopFocus;
                PveHuntPlayers = st.Kind != StageKind.Defend;
                var want = Director.Update(now, AliveEnemies);
                if (PveNoSpawns)
                    want = 0;
                if (want > 0)
                {
                    var placed = SpawnSquad(_coopZones, _coopFocus, st.Mix ?? mission.Mix, want, now);
                    Director.OnSpawned(placed);
                    if (placed == 0)
                        Director.Retry(now);
                }
                TickEnemies(now); // GamePveAi.cs
                TickStage(mission, st, now);
                if (Coop.Status == CoopStatus.Active && _coopHadAlive && !AnyAlive())
                    FailStage("SQUAD WIPED", now);
                break;
            }
        }
        Coop.Alive = AliveEnemies;
        Coop.Budget = Director.Budget;
        Coop.Phase = Director.Phase;
        Coop.Wave = Director.Wave;
        ServerSendPve(now); // GamePveNet.cs
    }

    private bool AnyAlive()
    {
        foreach (var p in Players.Values)
            if (p.Alive)
                return true;
        return false;
    }

    // ---- Stages ------------------------------------------------------------------------------------------------

    private void StartStage(int index, double now, bool restart)
    {
        var mission = Coop.MissionDef!;
        var st = mission.Stages[index];
        Coop.Stage = index;
        Coop.Kind = st.Kind;
        Coop.Status = CoopStatus.Active;
        Coop.Progress = 0;
        Coop.Lose = 0;
        Coop.NamedAlive = false;
        Coop.InZone = Coop.NeedInZone = 0;
        Array.Clear(Coop.Targets);
        Array.Clear(Coop.TargetValue);
        var squad = CoopSquadSize;
        Coop.Goal = st.Kind switch
        {
            StageKind.Eliminate => st.Count > 0 ? st.Count + st.CountPerPlayer * (squad - 1) : 1,
            StageKind.Destroy => st.Targets.Count,
            StageKind.Collect => st.Count,
            _ => 0,
        };
        Coop.TargetCount = Math.Min(st.Targets.Count, CoopObjective.MaxTargets);
        for (var i = 0; i < Coop.TargetCount; i++)
        {
            _coopTargetAt[i] = st.Targets[i].At.Resolve();
            _coopTargetRadius[i] = st.Targets[i].Radius;
            _coopCarrier[i] = byte.MaxValue;
            Coop.ItemPos[i] = _coopTargetAt[i];
        }
        _coopFocus = st.FocusPoint.Resolve();
        _coopAt = st.At?.Resolve() ?? _coopFocus;
        _coopDeliver = st.Deliver?.Resolve() ?? _coopAt;
        _coopZones.Clear();
        foreach (var z in st.SpawnZones)
            _coopZones.Add((z.At.Resolve(), z.Radius));
        if (st.Checkpoint.Count > 0)
            _coopCheckpointStage = index;
        _coopCheckpoint.Clear();
        if (_coopCheckpointStage >= 0)
            foreach (var c in mission.Stages[_coopCheckpointStage].Checkpoint)
                _coopCheckpoint.Add(c.Resolve());
        else
            foreach (var c in mission.Start)
                _coopCheckpoint.Add(c.Resolve());
        _coopStageStart = now;
        Coop.Timer = st.Kind == StageKind.Defend ? st.Seconds : st.TimeLimit > 0 ? st.TimeLimit : 0;

        // The director for this stage.
        var budget = Math.Max(1, (int)MathF.Round(mission.BudgetFor(squad, BotSkill) * st.BudgetScale));
        var total = st.Kind == StageKind.Eliminate && st.Count > 0 ? Coop.Goal + 4 + Coop.Goal / 3 : -1;
        Director.Reset(budget, total, st.Intensity, escalate: st.Kind == StageKind.Defend, seed: PveNext(), now);
        if (!restart && index > 0)
            RecycleBehind(_coopFocus, 90f, now); // the squad moved on: enemies far behind go (the director refills ahead)

        // A named target.
        _coopNamedId = 0;
        if (st.TargetArch is { } arch && st.TargetAt is { } tat)
        {
            var at = tat.Resolve();
            if (SpawnEnemy(arch, at + new Vector3(0, 0.05f, 0), now, st.TargetHealth, named: true) is { } boss)
            {
                _coopNamedId = boss.Id;
                Coop.NamedAlive = true;
                Coop.NamedPos = boss.Position;
                boss.LastSeenByPlayer = now;
            }
        }
        _coopHadAlive = AnyAlive();
        CoopBanner(restart ? $"RETRY: {st.Text}" : $"OBJECTIVE {index + 1}/{mission.Stages.Count}: {st.Text}");
        Console.WriteLine($"[coop] stage {index + 1}/{mission.Stages.Count} {st.Kind} '{st.Text}'{(restart ? " (restart)" : "")}: goal {Coop.Goal}, budget {budget}, total {total}, "
                          + $"focus {_coopFocus.X:0.0},{_coopFocus.Y:0.0},{_coopFocus.Z:0.0}, {_coopZones.Count} spawn zones, {_coopCheckpoint.Count} checkpoint spawns");
    }

    private void TickStage(Mission mission, MissionStage st, double now)
    {
        var elapsed = now - _coopStageStart;
        const float dt = World.TickDt;
        switch (st.Kind)
        {
            case StageKind.Eliminate:
                if (_coopNamedId != 0)
                {
                    var boss = EnemyById(_coopNamedId);
                    Coop.NamedAlive = boss is { Alive: true };
                    if (boss != null)
                        Coop.NamedPos = boss.Position;
                    if (!Coop.NamedAlive && (st.Count == 0 || Coop.Progress >= Coop.Goal))
                    {
                        CompleteStage(now);
                        return;
                    }
                    if (st.Count == 0)
                        break;
                }
                if (Coop.Progress >= Coop.Goal && _coopNamedId == 0)
                {
                    CompleteStage(now);
                    return;
                }
                if (Director.TotalLeft == 0 && AliveEnemies == 0 && Coop.Progress < Coop.Goal)
                    Director.TotalLeft = Coop.Goal - Coop.Progress + 2; // (recycled ones were refunded; this is a safety net)
                break;

            case StageKind.Destroy:
            {
                var done = 0;
                for (var i = 0; i < Coop.TargetCount; i++)
                {
                    switch (Coop.Targets[i])
                    {
                        case CoopTarget.Intact:
                            if (PlayerNear(_coopTargetAt[i], _coopTargetRadius[i], 2f) != null)
                            {
                                Coop.TargetValue[i] += dt / st.PlantSeconds;
                                if (Coop.TargetValue[i] >= 1f)
                                {
                                    Coop.Targets[i] = CoopTarget.Planted;
                                    Coop.TargetValue[i] = st.FuseSeconds;
                                    ModeEvent($"Charge planted on {st.Targets[i].Name}: {st.FuseSeconds:0} seconds!", Sfx.HitTick);
                                }
                            }
                            else
                                Coop.TargetValue[i] = MathF.Max(0, Coop.TargetValue[i] - dt / st.PlantSeconds * 0.5f);
                            break;
                        case CoopTarget.Planted:
                            Coop.TargetValue[i] -= dt;
                            if (Coop.TargetValue[i] <= 0)
                            {
                                Coop.Targets[i] = CoopTarget.Destroyed;
                                Coop.TargetValue[i] = 0;
                                CoopDetonate(_coopTargetAt[i], now);
                                ModeEvent($"{st.Targets[i].Name} destroyed", Sfx.StreakCall);
                            }
                            break;
                        default:
                            done++;
                            break;
                    }
                }
                Coop.Progress = done;
                if (done >= Coop.TargetCount)
                {
                    CompleteStage(now);
                    return;
                }
                break;
            }

            case StageKind.Defend:
            {
                Coop.Timer = (float)Math.Max(0, st.Seconds - elapsed);
                var players = 0;
                foreach (var p in Players.Values)
                    if (p.Alive && InZone(p.Position, _coopAt, st.Radius, 3f))
                        players++;
                var enemies = 0;
                foreach (var e in Enemies)
                    if (e.Alive && InZone(e.Position, _coopAt, st.Radius, 3f))
                        enemies++;
                Coop.InZone = players;
                if (enemies > 0 && players == 0)
                    Coop.Lose = MathF.Min(1f, Coop.Lose + dt / st.LoseSeconds);
                else
                    Coop.Lose = MathF.Max(0f, Coop.Lose - dt / (st.LoseSeconds * 2f));
                Coop.Progress = (int)Math.Min(elapsed, st.Seconds);
                Coop.Goal = (int)st.Seconds;
                if (Coop.Lose >= 1f)
                {
                    FailStage("POINT LOST", now);
                    return;
                }
                if (elapsed >= st.Seconds)
                {
                    CompleteStage(now);
                    return;
                }
                break;
            }

            case StageKind.Reach:
            {
                int people = 0, peopleIn = 0, all = 0, allIn = 0;
                foreach (var p in Players.Values)
                {
                    if (!p.Alive)
                        continue;
                    var inside = InZone(p.Position, _coopAt, st.Radius, 4f);
                    all++;
                    allIn += inside ? 1 : 0;
                    if (p.Bot == null)
                    {
                        people++;
                        peopleIn += inside ? 1 : 0;
                    }
                }
                Coop.InZone = people > 0 ? peopleIn : allIn;
                Coop.NeedInZone = people > 0 ? people : all;
                Coop.Progress = Coop.InZone;
                Coop.Goal = Coop.NeedInZone;
                if (Coop.NeedInZone > 0 && Coop.InZone >= Coop.NeedInZone)
                {
                    CompleteStage(now);
                    return;
                }
                break;
            }

            case StageKind.Collect:
            {
                var delivered = 0;
                for (var i = 0; i < Coop.TargetCount; i++)
                {
                    switch (Coop.Targets[i])
                    {
                        case CoopTarget.Intact when PlayerNear(Coop.ItemPos[i], 1.6f, 2f) is { } picker && !Carrying(picker.Id):
                            if (st.Deliver == null)
                            {
                                Coop.Targets[i] = CoopTarget.Destroyed;
                                ModeEvent($"{picker.Name} picked up {st.Targets[i].Name}", Sfx.HitTick);
                            }
                            else
                            {
                                Coop.Targets[i] = CoopTarget.Planted;
                                _coopCarrier[i] = picker.Id;
                                ModeEvent($"{picker.Name} has {st.Targets[i].Name}", Sfx.HitTick);
                            }
                            break;
                        case CoopTarget.Planted:
                            if (!Players.TryGetValue(_coopCarrier[i], out var c) || !c.Alive)
                            {
                                Coop.Targets[i] = CoopTarget.Intact; // dropped where the carrier fell
                                _coopCarrier[i] = byte.MaxValue;
                            }
                            else
                            {
                                Coop.ItemPos[i] = c.Position;
                                if (InZone(c.Position, _coopDeliver, st.DeliverRadius, 3f))
                                {
                                    Coop.Targets[i] = CoopTarget.Destroyed;
                                    _coopCarrier[i] = byte.MaxValue;
                                    ModeEvent($"{c.Name} delivered {st.Targets[i].Name}", Sfx.StreakCall);
                                }
                            }
                            break;
                    }
                    if (Coop.Targets[i] == CoopTarget.Destroyed)
                        delivered++;
                }
                Coop.Progress = delivered;
                if (delivered >= Coop.Goal)
                {
                    CompleteStage(now);
                    return;
                }
                break;
            }
        }
        if (st.Kind != StageKind.Defend && st.TimeLimit > 0)
        {
            Coop.Timer = (float)Math.Max(0, st.TimeLimit - elapsed);
            if (elapsed >= st.TimeLimit)
                FailStage("OUT OF TIME", now);
        }
    }

    private bool Carrying(byte id)
    {
        for (var i = 0; i < Coop.TargetCount; i++)
            if (Coop.Targets[i] == CoopTarget.Planted && _coopCarrier[i] == id)
                return true;
        return false;
    }

    private static bool InZone(Vector3 p, Vector3 center, float radius, float height) =>
        PFlat(p - center) <= radius && MathF.Abs(p.Y - center.Y) <= height;

    private Player? PlayerNear(Vector3 at, float radius, float height)
    {
        foreach (var p in Players.Values)
            if (p.Alive && InZone(p.Position, at, radius, height))
                return p;
        return null;
    }

    private PveEnemy? EnemyById(ushort id)
    {
        foreach (var e in Enemies)
            if (e.Id == id)
                return e;
        return null;
    }

    /// <summary>Server: a planted charge goes off: a big blast that breaks props, doors, destructible walls and kills enemies near it (not players).</summary>
    private void CoopDetonate(Vector3 at, double now)
    {
        var center = at + new Vector3(0, 0.5f, 0);
        _w.Begin(Msg.Explode).U16(PropExplosionId).U8((byte)ThrowableKind.Frag).U8(byte.MaxValue).Vec(center).U8(0);
        Net.Broadcast(_w.Span, Brutal.RakNetApi.PacketReliability.ReliableOrdered);
        OnDetonated(ThrowableKind.Frag, byte.MaxValue, center, 0, now);
        BlastProps(null, center, 8f, 500f, PropDamage.Explosion, now);
        BlastBuildings(null, center, 8f, 500f, PropDamage.Explosion, now);
        BlastStructures(null, center, 8f, 500f, StructureHit.Grenade, PveKillId, now);
        BlastPve(null, center, 10f, 800f, now);
        CoopCharges++;
    }

    /// <summary>Charges that went off this match (self-test).</summary>
    public int CoopCharges;

    private void CompleteStage(double now)
    {
        var mission = Coop.MissionDef!;
        var st = mission.Stages[Coop.Stage];
        Console.WriteLine($"[coop] stage {Coop.Stage + 1} complete after {now - _coopStageStart:0} s ({AliveEnemies} enemies alive, {PveSpawnedTotal} spawned so far)");
        CoopAwardAll(CoopObjectiveXp, "OBJECTIVE", now);
        CoopStagesCompleted++;
        if (Coop.Stage + 1 >= mission.Stages.Count)
        {
            Coop.Status = CoopStatus.Complete;
            CoopAwardAll(CoopMissionXp, "MISSION COMPLETE", now);
            CoopBanner("MISSION COMPLETE");
            var result = $"Mission complete: {mission.Name} ({Coop.Wipes} wipe{(Coop.Wipes == 1 ? "" : "s")})";
            Console.WriteLine($"[coop] {result}");
            EndMatch(null, result, now);
            return;
        }
        ModeEvent($"Objective complete: {st.Text}", Sfx.StreakCall);
        StartStage(Coop.Stage + 1, now, restart: false);
    }

    /// <summary>Stages completed this match (self-test).</summary>
    public int CoopStagesCompleted;

    /// <summary>Server: the squad wiped, or the stage failed (point lost, out of time): regroup, then restart at the checkpoint.</summary>
    private void FailStage(string reason, double now)
    {
        Coop.Wipes++;
        _coopRestartReason = reason;
        ClearEnemies();
        SendPveEvent(PveEventKind.Clear, 0, 0, "");
        if (Coop.Wipes > Coop.Retries)
        {
            Coop.Status = CoopStatus.Failed;
            var result = $"Mission failed: {reason.ToLowerInvariant()} on objective {Coop.Stage + 1}";
            CoopBanner("MISSION FAILED");
            EndMatch(null, result, now);
            return;
        }
        Coop.Status = CoopStatus.Regroup;
        _coopRegroupUntil = now + 4;
        CoopBanner($"{reason} - REGROUPING");
        Console.WriteLine($"[coop] {reason} on stage {Coop.Stage + 1} (wipe {Coop.Wipes})");
    }

    /// <summary>Server: the stage again from the top, everyone on the mission back at the checkpoint with full health.</summary>
    private void RestartStage(double now)
    {
        StartStage(Coop.Stage, now, restart: true);
        foreach (var p in Players.Values)
        {
            if (p.Bot == null && !_coopDeployed.Contains(p.Id))
                continue; // never deployed (still on the loadout screen): they come in when they're ready
            _coopDiedAt.Remove(p.Id);
            Spawn(p, ServerTick, now);
            if (p.Id == LocalId)
                OnLocalSpawn(p);
        }
        _coopHadAlive = AnyAlive();
    }

    /// <summary>Server: a stage restart / wipe count reason (the last one).</summary>
    public string CoopLastRestart => _coopRestartReason;

    // ---- Self-test hooks ------------------------------------------------------------------------------------------

    /// <summary>Self-test: the director spawns nothing (objective tests place their own enemies).</summary>
    public bool PveNoSpawns;

    /// <summary>Self-test: one server tick of the co-op mode only (no players moving, no bots).</summary>
    public void SelfTestCoopTick(double now)
    {
        ServerTick++;
        ServerTickCoop(now);
    }

    /// <summary>Self-test: play <paramref name="m"/> (built in code) from its first stage now.</summary>
    public void SelfTestStartMission(Mission m, double now) => StartCoopRun(now, m);

    /// <summary>Self-test and the smoke run: skip to stage <paramref name="index"/> (its checkpoint, everyone on the mission respawned there).</summary>
    public void SelfTestJumpToStage(int index, double now)
    {
        if (Coop.MissionDef is not { } m || index < 0 || index >= m.Stages.Count || !IsServer)
            return;
        ClearEnemies();
        _coopCheckpointStage = -1;
        for (var i = index; i >= 0 && _coopCheckpointStage < 0; i--)
            if (m.Stages[i].Checkpoint.Count > 0)
                _coopCheckpointStage = i;
        StartStage(index, now, restart: false);
        foreach (var p in Players.Values)
        {
            if (p.Bot == null && !_coopDeployed.Contains(p.Id))
                continue;
            Spawn(p, ServerTick, now);
            if (p.Id == LocalId)
                OnLocalSpawn(p);
        }
    }

    /// <summary>Self-test: the enemies' difficulty (the lobby's bot difficulty).</summary>
    public void SelfTestSetSkill(BotDifficulty d) => _botSkill = d;

    /// <summary>The current stage's spawn zones, resolved (self-test).</summary>
    public IReadOnlyList<(Vector3 At, float Radius)> CoopZones => _coopZones;

    /// <summary>The current checkpoint's spawn points, resolved (self-test).</summary>
    public IReadOnlyList<Vector3> CoopCheckpoint => _coopCheckpoint;

    // ---- Hooks from the module and the damage code ---------------------------------------------------------------

    /// <summary>Server: an enemy died (DamageEnemy): the stage's count, the killer's XP, the director's stress.</summary>
    private void OnEnemyKilled(Player? attacker, PveEnemy e, bool head, bool explosive, byte weaponId, double now)
    {
        if (Coop.Status == CoopStatus.Active && Coop.Kind == StageKind.Eliminate && !e.Named)
            Coop.Progress++;
        CoopKills++;
        if (NearestPlayerDistance(e.Position) < 10f)
            Director.AddStress(0.04f); // a fight at arm's length is tense even when you win it
        if (attacker != null && attacker.Bot == null)
            CoopAwardKill(attacker, head, explosive ? (byte)255 : weaponId, now);
    }

    /// <summary>Enemies killed this match (self-test).</summary>
    public int CoopKills;

    /// <summary>Server: someone on the squad died: their respawn timer starts.</summary>
    internal void CoopOnPlayerDied(Player victim, double now) => _coopDiedAt[victim.Id] = now;

    /// <summary>Server: may this dead player come back yet? (Not while regrouping; after the difficulty's respawn time.)</summary>
    internal bool CoopCanRespawn(Player p, double now) =>
        Coop.Status == CoopStatus.Active && (!_coopDiedAt.TryGetValue(p.Id, out var at) || now - at >= Coop.RespawnSeconds);

    /// <summary>Server: a (re)spawn goes to the current checkpoint (spread by player id), facing the objective.</summary>
    internal void CoopOnSpawn(Player p)
    {
        var mission = Coop.MissionDef ?? Missions.For(World.Map);
        if (mission == null)
            return;
        if (_coopCheckpoint.Count == 0)
            foreach (var c in mission.Start)
                _coopCheckpoint.Add(c.Resolve()); // spawned before the run's first tick: the mission start
        if (_coopCheckpoint.Count == 0)
            return;
        _coopDeployed.Add(p.Id);
        var n = _coopCheckpoint.Count;
        var at = _coopCheckpoint[p.Id % n];
        if (p.Id >= n)
            at += World.FlatForward(p.Id * 2.4f) * 1.1f; // more players than spots: beside it
        p.Teleport(at + new Vector3(0, 0.05f, 0));
        var to = _coopFocus - at;
        p.Yaw = MathF.Atan2(to.X, -to.Z);
        for (var i = 0; i < Player.HistorySize; i++)
            p.History[i] = new HistoryEntry { Position = p.Position, Yaw = p.Yaw };
    }

    // ---- XP and banners ------------------------------------------------------------------------------------------

    private void CoopAwardKill(Player killer, bool head, byte weaponId, double now)
    {
        if (killer.Id == LocalId)
            CoopLocalAward(PveEventKind.Kill, (ushort)((head ? 1 : 0) | (weaponId << 1)), now);
        else
            SendPveEventTo(killer, PveEventKind.Kill, (ushort)((head ? 1 : 0) | (weaponId << 1)), "");
    }

    private void CoopAwardAll(int xp, string label, double now)
    {
        if (Local != null)
            CoopLocalAward(label == "OBJECTIVE" ? PveEventKind.Objective : PveEventKind.Mission, (ushort)xp, now);
        SendPveEvent(label == "OBJECTIVE" ? PveEventKind.Objective : PveEventKind.Mission, (ushort)xp, 0, "", toClientsOnly: true);
    }

    /// <summary>Client (or the host): co-op XP into the local progress (Progression.cs).</summary>
    private void CoopLocalAward(PveEventKind kind, ushort arg, double now)
    {
        if (Progress == null)
            return;
        int? levelUp = kind switch
        {
            PveEventKind.Kill => Progress.AwardCoop(CoopKillXp + ((arg & 1) != 0 ? CoopHeadshotXp : 0), (arg & 1) != 0 ? "HEADSHOT KILL" : "KILL", now, (byte)(arg >> 1)),
            PveEventKind.Objective => Progress.AwardCoop(arg, "OBJECTIVE", now),
            PveEventKind.Mission => Progress.AwardCoop(arg, "MISSION COMPLETE", now),
            _ => null,
        };
        if (levelUp != null)
            Audio.Effect(Sfx.LevelUp, 0.8f);
    }

    /// <summary>The big centre line (OBJECTIVE 2/4...) and when it went up (local clock).</summary>
    public string CoopBannerText = "";

    public double CoopBannerTime = -100;

    private void CoopBanner(string text)
    {
        CoopBannerText = text;
        CoopBannerTime = Brutal.GlfwApi.Glfw.Time;
        SendPveEvent(PveEventKind.Banner, 0, 0, text);
    }
}
