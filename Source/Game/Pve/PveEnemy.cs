using System.Numerics;

namespace CallOfSlop;

/// <summary>What a co-op enemy is doing (4 bits on the wire).</summary>
public enum PveState : byte
{
    /// <summary>Following its path towards the director's attack point.</summary>
    Advance,

    /// <summary>A player in sight: standing (or crouching) and shooting.</summary>
    Engage,

    /// <summary>Moving to, or holding, a cover spot (crouched there between bursts).</summary>
    Cover,

    /// <summary>Charging the target (rushers, or anyone with nowhere better to be).</summary>
    Rush,

    /// <summary>Dead: falls over, then it's gone.</summary>
    Dead,
}

/// <summary>
/// One co-op enemy soldier (docs/pve/DESIGN.md "Enemies"). Not a <see cref="Player"/>: a pooled server-side entity with its
/// own compact replication (<see cref="Msg.PEnts"/>). Everything inside is allocated once with the pool, so the
/// steady-state tick allocates nothing. Clients keep one too (filled from the packets) for drawing and hit prediction.
/// </summary>
public sealed class PveEnemy
{
    public ushort Id;
    public PveArch Arch;
    public PveArchDef Def = PveArchetypes.All[0];
    public PveState State;
    public MoveState Sim;
    public float Yaw, Pitch;
    public int Health, MaxHealth;
    public bool Crouched;

    /// <summary>A named target (an eliminate stage's boss): drawn with a marker, counted by name.</summary>
    public bool Named;

    /// <summary>Moving through the shared player physics (near a player) or kinematically along its path (far: nobody sees it).</summary>
    public bool NearPhysics;

    public uint SpawnTick;
    public double StateAt, NextThink, DiedAt;
    public bool KilledByHead;

    // ---- Targeting ----
    public byte Target = byte.MaxValue;
    public double TargetSince, LastSawTarget = -100;
    public Vector3 LastSeen;
    public bool TargetVisible;

    // ---- Weapon ----
    public int Ammo, BurstLeft;
    public double NextShot, ReloadUntil, NextGrenade;

    /// <summary>Counts every round fired (4 bits on the wire): clients draw a tracer and a shot per step.</summary>
    public byte ShotSeq;

    // ---- Movement ----
    public readonly int[] Path = new int[96];
    public int PathLen, PathIdx;
    public Vector3 PathGoal, Goal, CoverSpot;
    public double NextPath, NextCover, CoverUntil;
    public Vector3 StuckPos;
    public double StuckAt, StuckSince = -1, UnstickUntil;
    public float UnstickYaw;
    public double LastSeenByPlayer;

    /// <summary>The goal is close and in plain view: walk straight at it (re-checked every few ticks).</summary>
    public bool DirectOk;

    /// <summary>Positions by server tick (lag compensation for players' shots, like zombies).</summary>
    public readonly Vector3[] History = new Vector3[32];

    public Vector3 PrevPos;
    public Vector3 Position => Sim.Position;
    public bool Alive => State != PveState.Dead;

    // ---- Client (and host drawing) ----
    public Vector3 RenderPos;
    public float RenderYaw, RenderPitch;
    public byte HealthByte = 255;
    public double LastUpdate;
    public float Gait;
    public bool Moving;

    /// <summary>The last two updates (tick, position, yaw), for interpolation.</summary>
    public double SnapTickA, SnapTickB;

    public Vector3 SnapPosA, SnapPosB;
    public float SnapYawA, SnapYawB;

    /// <summary>Back to a blank state for the pool (the arrays are kept).</summary>
    public void Reset()
    {
        Id = 0;
        State = PveState.Advance;
        Sim = default;
        Yaw = Pitch = 0;
        Health = MaxHealth = 0;
        Crouched = Named = NearPhysics = KilledByHead = TargetVisible = Moving = DirectOk = false;
        SpawnTick = 0;
        StateAt = NextThink = DiedAt = 0;
        Target = byte.MaxValue;
        TargetSince = 0;
        LastSawTarget = -100;
        LastSeen = Vector3.Zero;
        Ammo = BurstLeft = 0;
        NextShot = ReloadUntil = NextGrenade = 0;
        ShotSeq = 0;
        PathLen = PathIdx = 0;
        PathGoal = Goal = CoverSpot = Vector3.Zero;
        NextPath = NextCover = CoverUntil = 0;
        StuckPos = Vector3.Zero;
        StuckAt = 0;
        StuckSince = -1;
        UnstickUntil = 0;
        UnstickYaw = 0;
        LastSeenByPlayer = 0;
        PrevPos = RenderPos = Vector3.Zero;
        RenderYaw = RenderPitch = 0;
        HealthByte = 255;
        LastUpdate = 0;
        Gait = 0;
        SnapTickA = SnapTickB = 0;
        SnapPosA = SnapPosB = Vector3.Zero;
        SnapYawA = SnapYawB = 0;
    }
}
