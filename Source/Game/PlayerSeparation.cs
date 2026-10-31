using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Soft separation between the bodies of living players and bots (every team, every mode; board item ps01). Two upright
/// cylinders (<see cref="World.PlayerRadius"/> each) that overlap in height and on the floor are eased apart: after each
/// tick of a body's movement it moves itself away from the bodies it overlaps (along the sum of the overlaps) by
/// <see cref="SepMaxPush"/>, or half the overlap when that is less: a stiff spring at its cap, which clears a full overlap
/// in a quarter of a second. (A steady rate rather than one in proportion to the overlap: a client sees other bodies a
/// little late, and a push that doesn't depend on exactly how deep the overlap is predicts the same as the server's.) It is soft, not a block: no velocity is taken away, and a moving body is not pushed back
/// against its own motion (the push it may take that way shrinks to nothing as its step reaches <see cref="SepMaxPush"/>),
/// while the body it walks into is moved at most <see cref="SepMaxPush"/> a tick, slower than a crouch walk: so you always
/// walk through a teammate in a doorway (eased apart, not shoved along or stopped) and nobody is trapped. Bodies on exactly the same spot part along a fixed direction worked out from their two ids. A push
/// never goes into a wall (it is dropped or kept to one axis, like <see cref="ZBlockTryPush"/>) and never takes a body
/// standing on something off its edge. Dead, downed, seated (Sandbox) and climbing bodies, and practice dummies, are left
/// out; curved-gravity maps (planets) have no separation, like zombie bodies.
///
/// It runs in the shared movement step (<see cref="ZBlockPlayer"/>): the server for every body, and a client for its own
/// player only, in its prediction and its replay, against the other bodies as last heard, so prediction agrees with the
/// server. Remote players are pushed on the server only and arrive by snapshot. Bodies are found with a
/// <see cref="HordeGrid"/> rebuilt once a tick (and on each reconcile); nothing allocates once its arrays have grown.
/// Bots also steer apart (<see cref="BotSeparate"/>), so they don't stand on one nav point.
/// </summary>
public sealed partial class Game
{
    /// <summary>
    /// How far one body is moved by separation in one tick (m), or half the overlap when that is less: two bodies on one
    /// spot (0.8 m of overlap) are apart in 15 ticks (0.25 s). 1.6 m/s, under a crouch walk (3 m/s), so a body walked into
    /// can't be shoved along ahead of the walker.
    /// </summary>
    public const float SepMaxPush = World.PlayerRadius * 2 / (0.25f * World.TickRate) / 2;

    /// <summary>How far round a body the grid is asked (m): contact plus a tick's movement of either body, with slack.</summary>
    private const float SepQueryRadius = World.PlayerRadius * 2 + 0.8f;

    private const int SepQueryMax = 32;

    /// <summary>
    /// Overlaps under this (m) are left alone: a client sees other bodies at the snapshots' 1/256 m (up to ~3 mm off), so
    /// at rest in contact it would push where the server doesn't. A pair parts to exact contact, well inside it.
    /// </summary>
    private const float SepDeadZone = 0.004f;

    private readonly HordeGrid _sepGrid = new();
    private Player[] _sepBodies = new Player[64];
    private Vector3[] _sepPos = new Vector3[64];
    private bool[] _sepOn = new bool[64];
    private Vector3[] _sepRate = new Vector3[64]; // client: a remote body's own (input) movement a tick, from its newest snapshot
    private uint[] _sepSnapTick = new uint[64]; // client: the tick its newest snapshot is from
    private bool[] _sepMirror = new bool[64]; // client: it was touching the local player then (it is being pushed off them too)

    /// <summary>Client: where the local player ended each tick it predicted (ring by stamp; the tag says which).</summary>
    private readonly Vector3[] _sepMinePos = new Vector3[64];
    private readonly uint[] _sepMineTick = new uint[64];

    /// <summary>
    /// Client: how many ticks after its stamp the server runs our input (its jitter buffer), from the newest snapshot: the
    /// server's other bodies are pushed off where we were that long ago.
    /// </summary>
    private int _sepLag;
    private int _sepCount;

    /// <summary>
    /// The tick a client's input is stamped for (<see cref="InputCmd.Tick"/>; 0: none), so both sides push off the same
    /// spots (<see cref="SepOther"/>). Client: remote bodies are carried on from their newest snapshot: by their own
    /// velocity, and a body touching the local player by its own pushes off us, worked out as the server does. Server: a client's input run a few ticks after its stamp (the jitter buffer) meets the other
    /// bodies where they were then (<see cref="Player.History"/>).
    /// </summary>
    private uint _sepAtTick;

    /// <summary>The most ticks a remote body is carried on from its newest snapshot (or looked back in its history).</summary>
    private const int SepMaxCarry = 12;

    /// <summary>Bodies moved by separation, and pushes refused by a wall or a ledge (self-test, logs).</summary>
    public long SepPushes, SepWallRefused, SepLedgeRefused;

    /// <summary>Bodies in the separation grid now (self-test).</summary>
    public int SepBodies => _sepCount;

    private static bool SepActive => !GravityFrames.Curved;

    /// <summary>Does this body take part in separation (pushes and is pushed)?</summary>
    private bool SepEligible(Player p)
    {
        if (!p.Alive || IsDowned(p) || p.Bot is { Dummy: true })
            return false;
        if (SandboxSeated(p))
            return false;
        // A client knows a remote body is seated from its newest snapshot.
        return p.Snaps.Count == 0 || p.Snaps[^1].SeatBody == 0;
    }

    /// <summary>Rebuilds the bodies: the server each tick before anyone moves, a client before predicting and replaying.</summary>
    private void SepBuild()
    {
        var n = 0;
        if (SepActive && Phase != MatchPhase.Lobby)
            foreach (var p in Players.Values)
            {
                if (!SepEligible(p))
                    continue;
                if (n == _sepBodies.Length)
                {
                    Array.Resize(ref _sepBodies, n * 2); // (only when a lobby grows past 64 bodies)
                    Array.Resize(ref _sepPos, n * 2);
                    Array.Resize(ref _sepOn, n * 2);
                    Array.Resize(ref _sepRate, n * 2);
                    Array.Resize(ref _sepSnapTick, n * 2);
                    Array.Resize(ref _sepMirror, n * 2);
                }
                _sepBodies[n] = p;
                _sepRate[n] = Vector3.Zero;
                _sepSnapTick[n] = 0;
                _sepMirror[n] = false;
                if (!IsServer && p.Id != LocalId && p.Snaps.Count > 0)
                {
                    var last = p.Snaps[^1];
                    _sepRate[n] = new Vector3(last.Velocity.X, 0, last.Velocity.Z) * World.TickDt;
                    _sepSnapTick[n] = last.Tick;
                    if (Local is { } me)
                    {
                        var lp = SandboxRiderAt(p.Sim); // (a rider: in the construct as we have it, as we are: ConstructRide.cs)
                        var d = new Vector3(lp.X - me.Sim.Position.X, 0, lp.Z - me.Sim.Position.Z);
                        _sepMirror[n] = d.LengthSquared() < (World.PlayerRadius * 2 + 0.3f) * (World.PlayerRadius * 2 + 0.3f);
                    }
                }
                _sepPos[n] = IsServer ? p.Sim.Position : SepCarried(n, p);
                _sepOn[n] = true;
                n++;
            }
        for (var i = n; i < _sepCount; i++)
            _sepBodies[i] = null!; // (don't keep players who left alive)
        if (n == 0 && _sepCount == 0 && _sepGrid.Count == 0)
            return;
        _sepCount = n;
        _sepGrid.Setup(World.Map.HalfX, World.Map.HalfZ);
        _sepGrid.Reserve(_sepBodies.Length);
        _sepGrid.Build(_sepPos.AsSpan(0, n), _sepOn.AsSpan(0, n));
    }

    /// <summary>
    /// Where body <paramref name="i"/> was at the start of the tick being simulated: every body is pushed off the others'
    /// starting spots, so the push doesn't depend on who moves first and a pair halves what is left alike. Server: as the
    /// grid was built, or (a client's input run after its stamp) its history at the tick before the stamp. Client: its
    /// newest snapshot carried on to the tick before <see cref="_sepAtTick"/>.
    /// </summary>
    private Vector3 SepOther(int i, Player o)
    {
        if (IsServer)
        {
            var t = _sepAtTick;
            if (t != 0 && t < ServerTick && ServerTick - t < SepMaxCarry)
            {
                var h = o.History[(t - 1) % Player.HistorySize];
                if (h.Tick == t - 1)
                    return h.Position;
            }
            return _sepPos[i];
        }
        return SepCarried(i, o);
    }

    /// <summary>
    /// Client: a remote body's newest snapshot (the end of its tick) carried on to the end of the tick before
    /// <see cref="_sepAtTick"/>: by its own velocity, and (touching the local player) by its pushes off us.
    /// </summary>
    private Vector3 SepCarried(int i, Player o)
    {
        var at = SandboxRiderAt(o.Sim);
        if (_sepAtTick == 0 || _sepSnapTick[i] == 0)
            return at;
        var from = _sepSnapTick[i];
        var ticks = Math.Clamp((int)(_sepAtTick - 1 - from), 0, SepMaxCarry);
        at += _sepRate[i] * ticks;
        if (!_sepMirror[i] || Local is not { } me)
            return at;
        // Touching us: its own pushes off us, as the server works them out: off where we were at the start of each tick as
        // the server had us then (our input from _sepLag ticks earlier), under its own rules (less against its own
        // motion, never into a wall or off an edge).
        var h = World.HeightOf(o.Sim.Stance);
        var r = _sepRate[i];
        var v = MathF.Sqrt(r.X * r.X + r.Z * r.Z);
        var most = -MathF.Max(0, SepMaxPush - v);
        const float contact = World.PlayerRadius * 2;
        at = SandboxRiderAt(o.Sim);
        for (var t = from + 1; t != from + 1 + (uint)ticks; t++)
        {
            at += r;
            var k = t - 1 - (uint)_sepLag;
            var mp = _sepMineTick[k & 63] == k ? _sepMinePos[k & 63] : me.Sim.Position;
            var dx = at.X - mp.X;
            var dz = at.Z - mp.Z;
            var d2 = dx * dx + dz * dz;
            if (d2 >= (contact - SepDeadZone) * (contact - SepDeadZone))
                continue;
            float nx, nz, d;
            if (d2 < 1e-8f)
            {
                (nx, nz) = SepStackedDir(o.Id, me.Id);
                d = 0;
            }
            else
            {
                d = MathF.Sqrt(d2);
                nx = dx / d;
                nz = dz / d;
            }
            var step = MathF.Min(SepMaxPush, (contact - d) * 0.5f);
            var m = new Vector3(nx * step, 0, nz * step);
            if (v > 1e-4f)
            {
                var along = (m.X * r.X + m.Z * r.Z) / v;
                if (along < most)
                {
                    m.X -= r.X / v * (along - most);
                    m.Z -= r.Z / v * (along - most);
                }
            }
            if (m.X * m.X + m.Z * m.Z < 1e-10f || ZBlockBodyBlocked(at + m, h))
                continue;
            if (o.Sim.Grounded && !SepHasGround(at + m) && SepHasGround(at))
                continue;
            at += m;
        }
        return at;
    }

    /// <summary>Client, on each snapshot: how late the server runs our input (<see cref="_sepLag"/>).</summary>
    private void SepNoteLag(uint snapTick, uint ackSeq)
    {
        var stamp = _moverStampBySeq[ackSeq & 255];
        if (ackSeq != 0 && stamp != 0 && snapTick >= stamp)
            _sepLag = Math.Min((int)(snapTick - stamp), SepMaxCarry);
    }

    /// <summary>The fixed way two bodies on exactly the same spot part: from their ids, opposite for each of them.</summary>
    private static (float X, float Z) SepStackedDir(byte self, byte other)
    {
        var lo = Math.Min(self, other);
        var hi = Math.Max(self, other);
        var a = (lo * 73 + hi * 151) % 360 * (MathF.PI / 180f);
        var sign = self < other ? 1f : -1f;
        return (MathF.Sin(a) * sign, MathF.Cos(a) * sign);
    }

    /// <summary>
    /// After a tick of <paramref name="p"/>'s movement (from <see cref="ZBlockPlayer"/>): its half of the push away from every
    /// body it overlaps. Server: every body. Client: its own player only.
    /// </summary>
    private void SepPlayer(Player p, ref MoveState s)
    {
        if (_sepCount < 2 || s.MantleTicks > 0 || !SepActive)
            return;
        if (!IsServer && p.Id != LocalId)
            return; // remote players are pushed on the server and arrive by snapshot
        var mine = !IsServer && _sepAtTick != 0;
        if (mine)
        {
            _sepMinePos[_sepAtTick & 63] = s.Position;
            _sepMineTick[_sepAtTick & 63] = _sepAtTick;
        }
        if (!SepEligible(p))
            return;
        var feet = s.Position;
        var h = World.HeightOf(s.Stance);
        Span<int> near = stackalloc int[SepQueryMax];
        var n = _sepGrid.Query(feet, _sepAtTick == 0 ? SepQueryRadius : SepQueryRadius + 0.6f, near); // (bodies carried on or looked back)
        if (n < 2)
            return;
        const float contact = World.PlayerRadius * 2;
        float px = 0, pz = 0;
        var touching = false;
        for (var k = 0; k < n; k++)
        {
            var i = near[k];
            var o = _sepBodies[i];
            if (o == p || o.Id == p.Id || o.Sim.MantleTicks > 0 || !o.Alive)
                continue;
            if (!Players.TryGetValue(o.Id, out var live) || live != o)
                continue; // (left since the grid was built)
            var op = SepOther(i, o);
            if (feet.Y >= op.Y + World.HeightOf(o.Sim.Stance) - 0.05f || feet.Y + h <= op.Y + 0.05f)
                continue; // one is over the other's head
            var dx = feet.X - op.X;
            var dz = feet.Z - op.Z;
            var d2 = dx * dx + dz * dz;
            if (d2 >= (contact - SepDeadZone) * (contact - SepDeadZone))
                continue; // (touching within a millimetre: rounding, not an overlap)
            float nx, nz, d;
            if (d2 < 1e-8f)
            {
                (nx, nz) = SepStackedDir(p.Id, o.Id);
                d = 0;
            }
            else
            {
                d = MathF.Sqrt(d2);
                nx = dx / d;
                nz = dz / d;
            }
            px += nx * (contact - d);
            pz += nz * (contact - d);
            touching = true;
        }
        if (!touching)
            return;
        var total = MathF.Sqrt(px * px + pz * pz);
        if (total < 1e-6f)
            return; // (squeezed evenly from both sides: nowhere better to go)
        var dirX = px / total;
        var dirZ = pz / total;
        // My share: the steady rate, or half of what is left.
        var step = MathF.Min(SepMaxPush, total * 0.5f);
        var push = new Vector3(dirX * step, 0, dirZ * step);
        // Soft: a moving body is pushed back against its motion less the faster it goes (not at all from a cap's step up).
        var vx = s.Velocity.X * World.TickDt;
        var vz = s.Velocity.Z * World.TickDt;
        var v = MathF.Sqrt(vx * vx + vz * vz);
        if (v > 1e-4f)
        {
            var along = (push.X * vx + push.Z * vz) / v;
            var most = -MathF.Max(0, SepMaxPush - v);
            if (along < most)
            {
                push.X -= vx / v * (along - most);
                push.Z -= vz / v * (along - most);
            }
        }
        if (push.X * push.X + push.Z * push.Z < 1e-10f)
            return;
        if (SepTryPush(ref s.Position, push, h, s.Grounded))
        {
            SepPushes++;
            if (mine)
                _sepMinePos[_sepAtTick & 63] = s.Position;
        }
    }

    /// <summary>Self-test (client): the newest input the server has acknowledged, and the next one to be sent.</summary>
    public (uint Acked, uint Next) SelfTestInputSeqs => (_lastAckSeq, _nextSeq);

    /// <summary>Self-test: the shared push step alone for <paramref name="p"/> (cost and allocation checks).</summary>
    public void SelfTestSepStep(Player p) => SepPlayer(p, ref p.Sim);

    /// <summary>Self-test: the bots' steering term for <paramref name="p"/> added to <paramref name="wish"/>.</summary>
    public Vector3 SelfTestBotSeparate(Player p, Vector3 wish)
    {
        BotSeparate(p, ref wish);
        return wish;
    }

    /// <summary>
    /// Moves <paramref name="feet"/> by the flat <paramref name="push"/> unless that puts the body in a wall or (standing on
    /// something) over the edge: then one axis of it, or nothing. True if it moved.
    /// </summary>
    private bool SepTryPush(ref Vector3 feet, Vector3 push, float h, bool grounded)
    {
        var supported = grounded && SepHasGround(feet);
        var ax = new Vector3(push.X, 0, 0);
        var az = new Vector3(0, 0, push.Z);
        var (first, second) = MathF.Abs(push.X) >= MathF.Abs(push.Z) ? (ax, az) : (az, ax);
        var wall = false;
        for (var i = 0; i < 3; i++)
        {
            var m = i == 0 ? push : i == 1 ? first : second;
            if (m.LengthSquared() < 1e-10f)
                continue;
            var to = feet + m;
            if (ZBlockBodyBlocked(to, h))
            {
                wall = true;
                continue;
            }
            if (supported && !SepHasGround(to))
                continue;
            feet = to;
            return true;
        }
        if (wall)
            SepWallRefused++;
        else
            SepLedgeRefused++;
        return false;
    }

    /// <summary>Is there something to stand on under the middle of <paramref name="feet"/> (within a step down)?</summary>
    private static bool SepHasGround(Vector3 feet)
    {
        const float above = 0.3f;
        const float depth = above + World.StepHeight;
        return World.Query.Raycast(feet + new Vector3(0, above, 0), -Vector3.UnitY, depth, QueryFilter.All) < depth;
    }

    /// <summary>
    /// Bots' steering term: step away from bodies closer than 1.2 m, so a group following one target or queuing at a door
    /// spreads out instead of stacking on one nav point (the body push above then only has small overlaps to clear).
    /// </summary>
    private void BotSeparate(Player p, ref Vector3 wish)
    {
        if (_sepCount < 2)
            return;
        Span<int> near = stackalloc int[SepQueryMax];
        var n = _sepGrid.Query(p.Position, 1.2f + 0.6f, near);
        for (var k = 0; k < n; k++)
        {
            var o = _sepBodies[near[k]];
            if (o == p || !o.Alive)
                continue;
            var d = FlatVec(p.Position - o.Position);
            var dist = d.Length();
            if (dist >= 1.2f)
                continue;
            if (dist > 0.001f)
                wish += d / dist * (1.2f - dist) * 1.5f;
            else
            {
                var (x, z) = SepStackedDir(p.Id, o.Id);
                wish += new Vector3(x, 0, z) * 1.8f;
            }
        }
    }
}

public sealed partial class Game
{
    /// <summary>Self-test: rebuild the separation bodies from where everyone is now.</summary>
    public void SelfTestSepBuild() => SepBuild();

    /// <summary>Self-test: one tick of <paramref name="p"/>'s movement with <paramref name="cmd"/>, then the shared push step.</summary>
    public void SelfTestSepMove(Player p, in InputCmd cmd)
    {
        World.Simulate(ref p.Sim, cmd);
        ZBlockPlayer(p, ref p.Sim);
    }
}
