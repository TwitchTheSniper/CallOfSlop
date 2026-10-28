using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Reuse (both sides): a zombie that leaves the horde goes back to a pool, and the next one spawned (server) or first
/// heard of (client) is taken from it, so a round's spawning allocates nothing once the pool is warm. A taken zombie is
/// reset to exactly what <c>new Zombie()</c> gives (the "Zombie hordes" self-test compares every field), so nothing
/// carries over from its last life. The reset happens on taking, not on returning: code that still writes to a zombie
/// in the tick it leaves (the dead-body pass in TickHorde) does no harm. Nothing keeps a zombie past its removal from
/// <see cref="Game.Horde"/>: ragdolls copy what they need, voices own a slot by id, and the climb list drops anyone who
/// isn't scaling every tick (a body is dead for 1.5 s before it leaves the horde).
/// </summary>
public sealed partial class Zombie
{
    /// <summary>Back to the state a new zombie starts in (every field; the arrays and lists are cleared, not replaced).</summary>
    public void ResetForReuse()
    {
        // ZombieAI.cs
        Id = 0;
        Sim = default;
        Yaw = 0;
        Health = MaxHealth = 0;
        State = default;
        Speed = default;
        Window = -1;
        Area = 0;
        StateAt = NextTear = 0;
        SwingAt = -1;
        NextSwing = StunnedUntil = 0;
        Target = byte.MaxValue;
        NextTarget = 0;
        Door = -1;
        ClimbFrom = ClimbTo = default;
        SpawnTick = 0;
        KilledByHead = false;
        SiegeOut = false;
        ScaleRank = -1;
        ScaleClimber = false;
        VaultAt = -1;
        VaultFrom = default;
        BurnUntil = BurnNext = 0;
        BurnPerTick = 0;
        BurnBy = byte.MaxValue;
        SlowUntil = ZapUntil = 0;
        Floaty = false;
        FxBurning = FxZapped = false;
        StuckPos = default;
        StuckAt = UnstickUntil = 0;
        UnstickYaw = 0;
        Array.Clear(History);
        PrevPos = RenderPos = default;
        RenderYaw = 0;
        HealthByte = 255;
        DiedAt = -1;
        Ragdolled = false;
        Snaps.Clear();
        HeardAt = 0;
        Gait = 0;
        SeenState = (ZState)255;
        SeenStateAt = 0;

        // ZombieAnimNet.cs
        AnimByte = 0;
        AnimByteTick = 0;
        AnimState = AnimPrevState = (ZState)255;
        AnimStateTick = 0;
        WindowTick = AttackTick = LandTick = FlinchTick = -1e9;
        FlinchAmt = 0;
        AnimHealth = 255;
        AnimSpeed = 0;
        AnimPrevPos = default;
        AnimPrevSet = false;
        AnimPrevY = AnimClimb = 0;
        AnimPrevSetY = false;
        AnimMove = 0;
        AnimTargets = default;
        AnimSmoothAt = -1e9;
        AnimPose = default;
        AnimPoseAt = default;
        AnimPoseState = (ZState)255;
        AnimPoseFrame = -100;

        // ZombieHorde.cs (a new HordeMember's field values, not HordeMember.Reset's: that one sets ThinkNow)
        Hm.Reset();
        Hm.ThinkNow = false;
        LastHeard = 0;

        // ZombieLimbs.cs
        Limbs = 0;
        LimbHp = default;
        LimbsShown = 0;

        // ZombieTypes.cs
        Type = 0;
        Kind = ZombieTypeDef.DefaultWalker;
        Plates = 0;
        PlateHpHead = PlateHpChest = 0;
        Grabbing = false;
        GrabUntil = GrabNext = 0;
        PounceUntil = PounceReadyAt = 0;
        PounceYaw = 0;
        FreeClimb = false;
        ClimbOut = default;
        ClimbTopY = 0;
        ClimbRetryAt = 0;

        // Source/Audio/ZombieSounds.cs
        HeadHitTick = -1e9;
        VoxInit = VoxDead = false;
        VoxState = default;
        VoxAttack = VoxFlinch = VoxNext = VoxUntil = VoxHurtAt = 0;
        VoxGait = 0;
        VoxSlot = -1;
        VoxLast = 255;
    }
}

public sealed partial class Game
{
    /// <summary>Zombies that have left the horde, kept for reuse (at most <see cref="MaxZombies"/>).</summary>
    private readonly Stack<Zombie> _zPool = new(MaxZombies);

    /// <summary>Self-test: zombies waiting in the pool.</summary>
    public int ZombiePoolCount => _zPool.Count;

    /// <summary>A zombie as new (from the pool when it has one).</summary>
    private Zombie RentZombie()
    {
        if (!_zPool.TryPop(out var z))
            return new Zombie();
        z.ResetForReuse();
        return z;
    }

    /// <summary>A zombie that has just left the horde (and <c>_zById</c>): kept for the next spawn.</summary>
    private void ReturnZombie(Zombie z)
    {
        if (_zPool.Count < MaxZombies)
            _zPool.Push(z);
    }

    /// <summary>
    /// Server, at a round's start (and when the player count moves its cap): enough zombies made up front for the round's
    /// alive cap plus half again for bodies still lying there (a pack of zombie types comes out in one burst and a siege
    /// kills fast: Source/Game/ZombieTypes.cs), so the round's spawning allocates nothing mid-round.
    /// </summary>
    private void WarmZombiePool()
    {
        var cap = ZAliveCap();
        var want = Math.Min(MaxZombies, cap + cap / 2);
        for (var n = Horde.Count + _zPool.Count; n < want; n++)
            _zPool.Push(new Zombie());
    }

    /// <summary>Every zombie in the horde back to the pool (call before clearing the horde).</summary>
    private void ReturnHorde()
    {
        foreach (var z in Horde)
            ReturnZombie(z);
    }
}
