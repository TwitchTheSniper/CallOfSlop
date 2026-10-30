using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Zombie bodies block players (Zombies mode; docs/zombies/ZOMBIES-MODE.md, "Zombie bodies"). Every zombie that isn't dead
/// is an upright cylinder at its feet (<see cref="ZBlockRadius"/>, the standing height; a crawler is a low one,
/// <see cref="ZBlockCrawlHeight"/>). After each tick of a player's movement (server, the client's prediction and its replay:
/// the same function on both) the player's body is pushed out of every cylinder it overlaps and the part of its velocity
/// going into one is taken away, so a dense horde is a wall, a single zombie is slid round and a gap wider than a body lets
/// you through. A push never goes into a wall (it is dropped, or kept to one axis), and a zombie the player is deep inside
/// (it spawned or climbed onto them) only pushes and never holds their velocity, so nobody is ever trapped in one. Landing
/// on a head slides you off (the cylinder pushes sideways while your feet are below its top). Downed players neither block
/// nor are blocked. Zombies walking are held off standing players the same way (<see cref="ZBlockZombie"/>), so a chasing
/// horde stops at your body instead of shoving you. The server reads its zombies' positions; a client the newest
/// <see cref="Msg.ZEnts"/> position of each (no wire change). A uniform grid (<see cref="HordeGrid"/>) is rebuilt each tick
/// for the lookups; no allocation.
/// </summary>
public sealed partial class Game
{
    /// <summary>A zombie body's radius (m): a little under a player's (0.4) so two zombies 1.6 m apart leave a body-wide gap.</summary>
    public const float ZBlockRadius = 0.35f;

    /// <summary>A crawler's (no legs; ZombieLimbs.cs) body height (m): a prone body, which a jump clears.</summary>
    public const float ZBlockCrawlHeight = 0.6f;

    /// <summary>The most a player is pushed out of zombies in one tick (m): faster than a sprint step, but no snap.</summary>
    public const float ZBlockMaxPush = 0.3f;

    /// <summary>Deeper than this into a zombie (m from its centre) a player is only pushed out, never held (no trapping).</summary>
    public const float ZBlockDeep = 0.35f;

    private const int ZBlockQueryMax = 32;

    private readonly HordeGrid _zBlockGrid = new();
    private Vector3[] _zBlockPos = new Vector3[MaxZombies]; // (sized for the most zombies at once, so a growing horde never allocates mid-round)
    private float[] _zBlockHeight = new float[MaxZombies];
    private float[] _zBlockRadius = new float[MaxZombies]; // (a type's width scales its body: ZombieTypes.cs)
    private bool[] _zBlockOn = new bool[MaxZombies];

    /// <summary>The widest a type's body may be (a 2.5x type): the grid is asked this far round a player.</summary>
    private const float ZBlockMaxRadius = ZBlockRadius * 2.5f;
    private int _zBlockCount;

    /// <summary>Standing players' feet and heights (for zombies walking; rebuilt each horde tick).</summary>
    private readonly Vector3[] _zBlockPlayerPos = new Vector3[32];
    private readonly float[] _zBlockPlayerHeight = new float[32];
    private int _zBlockPlayers;

    /// <summary>Zombie bodies in the block grid this tick (self-test).</summary>
    public int ZBlockBodies => _zBlockGrid.Count;

    /// <summary>Players pushed out of zombies and pushes refused by a wall (self-test, logs).</summary>
    public long ZBlockPushes, ZBlockWallRefused;

    private static bool ZBlockActive => !GravityFrames.Curved;

    /// <summary>Rebuilds the zombie bodies from the horde: the server's positions, or (client) the newest one heard.</summary>
    private void ZBlockBuild(bool client)
    {
        var n = Horde.Count;
        if (Mode != GameMode.Zombies || !ZBlockActive)
            n = 0;
        if (n == 0 && _zBlockCount == 0 && _zBlockGrid.Count == 0)
            return; // (other modes: nothing to clear)
        if (_zBlockPos.Length < n)
        {
            var cap = Math.Max(n, _zBlockPos.Length * 2);
            _zBlockPos = new Vector3[cap];
            _zBlockHeight = new float[cap];
            _zBlockRadius = new float[cap];
            _zBlockOn = new bool[cap];
        }
        for (var i = 0; i < n; i++)
        {
            var z = Horde[i];
            _zBlockOn[i] = z.State != ZState.Dead;
            _zBlockPos[i] = client && z.Snaps.Count > 0 ? z.Snaps[^1].Pos : z.Sim.Position;
            _zBlockHeight[i] = (z.Crawler ? ZBlockCrawlHeight : World.PlayerHeight) * z.Kind.Height;
            _zBlockRadius[i] = ZBlockRadius * z.Kind.Width;
        }
        _zBlockCount = n;
        _zBlockGrid.Setup(World.Map.HalfX, World.Map.HalfZ);
        _zBlockGrid.Reserve(MaxZombies); // (once: a growing horde never allocates mid-round)
        _zBlockGrid.Build(_zBlockPos.AsSpan(0, n), _zBlockOn.AsSpan(0, n));
    }

    /// <summary>Server, each horde tick: the standing players, which walking zombies are held off.</summary>
    private void ZBlockGatherPlayers()
    {
        _zBlockPlayers = 0;
        if (!ZBlockActive)
            return;
        foreach (var p in Players.Values)
        {
            if (_zBlockPlayers == _zBlockPlayerPos.Length)
                break;
            if (!IsStanding(p) || p.Sim.MantleTicks > 0)
                continue;
            _zBlockPlayerPos[_zBlockPlayers] = p.Sim.Position;
            _zBlockPlayerHeight[_zBlockPlayers] = World.HeightOf(p.Sim.Stance);
            _zBlockPlayers++;
        }
    }

    /// <summary>
    /// After a tick of <paramref name="p"/>'s movement: out of every zombie body it overlaps, and no velocity into one
    /// (server and client prediction alike).
    /// </summary>
    private void ZBlockPlayer(Player p, ref MoveState s)
    {
        SepPlayer(p, ref s); // eased apart from other players' and bots' bodies (PlayerSeparation.cs)
        ZBlockOutOfBodies(p, ref s);
        CrowdSurfacePlayer(p, ref s); // a crowd map: the crowd as ground (ZombiesCrowdSurface.cs)
        SandboxPlayer(p, ref s); // Sandbox: drivers in their seats, everyone else out of (or on) the constructs (GameSandbox.cs)
    }

    private void ZBlockOutOfBodies(Player p, ref MoveState s)
    {
        if (_zBlockGrid.Count == 0 || Mode != GameMode.Zombies || s.MantleTicks > 0 || IsDowned(p))
            return;
        var feet = s.Position;
        var h = World.HeightOf(s.Stance);
        Span<int> near = stackalloc int[ZBlockQueryMax];
        var n = _zBlockGrid.Query(feet, World.PlayerRadius + ZBlockMaxRadius, near);
        if (n == 0)
            return;
        var push = Vector3.Zero;
        var vel = s.Velocity;
        for (var k = 0; k < n; k++)
        {
            var i = near[k];
            var zp = _zBlockPos[i];
            var top = zp.Y + _zBlockHeight[i];
            if (feet.Y >= top - 0.05f || feet.Y + h <= zp.Y + 0.05f)
                continue; // over its head, or under it
            var contact = World.PlayerRadius + _zBlockRadius[i];
            var dx = feet.X - zp.X;
            var dz = feet.Z - zp.Z;
            var d2 = dx * dx + dz * dz;
            if (d2 >= contact * contact)
                continue;
            float nx, nz, d;
            if (d2 < 1e-6f)
            {
                // Right on top of it: away along the way it was going (back), or a fixed way.
                var vl = MathF.Sqrt(vel.X * vel.X + vel.Z * vel.Z);
                (nx, nz) = vl > 1e-3f ? (-vel.X / vl, -vel.Z / vl) : (1f, 0f);
                d = 0;
            }
            else
            {
                d = MathF.Sqrt(d2);
                nx = dx / d;
                nz = dz / d;
            }
            push.X += nx * (contact - d);
            push.Z += nz * (contact - d);
            if (d >= ZBlockDeep)
            {
                // Held: no velocity into it (what is left slides along it).
                var into = vel.X * nx + vel.Z * nz;
                if (into < 0)
                {
                    vel.X -= nx * into;
                    vel.Z -= nz * into;
                }
            }
        }
        var len = MathF.Sqrt(push.X * push.X + push.Z * push.Z);
        if (len < 1e-5f)
        {
            s.Velocity = vel;
            return;
        }
        if (len > ZBlockMaxPush)
            push *= ZBlockMaxPush / len;
        s.Velocity = vel;
        if (ZBlockTryPush(ref s.Position, push, h))
        {
            ZBlockPushes++;
            NotePushed(p); // (the outside log's "pushed", ZombiesOutside.cs)
        }
    }

    /// <summary>
    /// Moves <paramref name="feet"/> by the flat <paramref name="push"/> unless that puts the body in the world: then one
    /// axis of it, or nothing (a zombie never pushes anyone into a wall). True if it moved.
    /// </summary>
    private bool ZBlockTryPush(ref Vector3 feet, Vector3 push, float h)
    {
        if (!ZBlockBodyBlocked(feet + push, h))
        {
            feet += push;
            return true;
        }
        var ax = new Vector3(push.X, 0, 0);
        var az = new Vector3(0, 0, push.Z);
        var (first, second) = MathF.Abs(push.X) >= MathF.Abs(push.Z) ? (ax, az) : (az, ax);
        if (first.LengthSquared() > 1e-10f && !ZBlockBodyBlocked(feet + first, h))
        {
            feet += first;
            return true;
        }
        if (second.LengthSquared() > 1e-10f && !ZBlockBodyBlocked(feet + second, h))
        {
            feet += second;
            return true;
        }
        ZBlockWallRefused++;
        return false;
    }

    private static bool ZBlockBodyBlocked(Vector3 feet, float h) =>
        MoverPhysics.BlockedAll(feet + new Vector3(0, 0.03f, 0), MathF.Max(0.1f, h - 0.06f), double.NaN);

    /// <summary>Server, after a walking zombie's tick of movement: held off every standing player's body.</summary>
    private void ZBlockZombie(Zombie z)
    {
        if (_zBlockPlayers == 0)
            return;
        var feet = z.Sim.Position;
        var h = (z.Crawler ? ZBlockCrawlHeight : World.PlayerHeight) * z.Kind.Height;
        var contact = World.PlayerRadius + ZBlockRadius * z.Kind.Width; // (its type's width: ZombieTypes.cs)
        var push = Vector3.Zero;
        var vel = z.Sim.Velocity;
        for (var k = 0; k < _zBlockPlayers; k++)
        {
            var pp = _zBlockPlayerPos[k];
            if (feet.Y >= pp.Y + _zBlockPlayerHeight[k] - 0.05f || feet.Y + h <= pp.Y + 0.05f)
                continue;
            var dx = feet.X - pp.X;
            var dz = feet.Z - pp.Z;
            var d2 = dx * dx + dz * dz;
            if (d2 >= contact * contact || d2 < 1e-6f)
                continue; // (right on top: the player's own push sorts it out)
            var d = MathF.Sqrt(d2);
            var nx = dx / d;
            var nz = dz / d;
            push.X += nx * (contact - d);
            push.Z += nz * (contact - d);
            var into = vel.X * nx + vel.Z * nz;
            if (into < 0)
            {
                vel.X -= nx * into;
                vel.Z -= nz * into;
            }
        }
        if (push.X == 0 && push.Z == 0)
            return;
        var len = MathF.Sqrt(push.X * push.X + push.Z * push.Z);
        if (len > ZBlockMaxPush)
            push *= ZBlockMaxPush / len;
        z.Sim.Velocity = vel;
        ZBlockTryPush(ref z.Sim.Position, push, h);
    }
}

public sealed partial class Game
{
    /// <summary>Self-test: rebuild the zombie bodies (the server's positions, or as a client the newest heard).</summary>
    public void SelfTestZBlockBuild(bool client) => ZBlockBuild(client);

    /// <summary>Self-test: one tick of <paramref name="p"/>'s movement with <paramref name="cmd"/>, then out of zombie bodies.</summary>
    public void SelfTestZBlockMove(Player p, ref MoveState s, in InputCmd cmd)
    {
        World.Simulate(ref s, cmd);
        ZBlockPlayer(p, ref s);
    }

    /// <summary>Self-test: the standing players again, then a walking zombie's tick of movement towards <paramref name="yaw"/>.</summary>
    public void SelfTestZBlockWalkZombie(Zombie z, float yaw)
    {
        ZBlockGatherPlayers();
        z.PrevPos = z.Sim.Position;
        MoveZombie(z, yaw, true, false);
    }
}
