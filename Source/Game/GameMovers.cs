using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The mover clock for networked play (see <see cref="MoverPhysics"/>).
///
/// A client stamps each predicted command with the server tick it expects that command to be simulated at, and the
/// server simulates it at exactly that tick (clamped to a sane window). So the predicted player on a moving
/// platform and the server's player see the platform in the same place, and replaying commands during
/// reconciliation gives the same answer: no jitter or rubber-banding. The stamp is steered from the snapshots: the
/// server says which tick it processed our last acknowledged command at (the snapshot's tick), and the client nudges
/// its stamps a tick at a time until they line up.
///
/// The ticks travel as a trailer after the commands in <see cref="Msg.Input"/>, so older servers ignore them and
/// older clients (no trailer) are simulated at the server's clock: no protocol bump.
/// </summary>
public sealed partial class Game
{
    /// <summary>A client's stamps may differ from the server's tick by at most this before the server ignores them.</summary>
    public const int MaxMoverTickSkew = 30;

    private uint _moverTick; // client: stamp of the latest predicted command
    private bool _moverClockSet;
    private double _moverTickError;
    private readonly uint[] _moverStampBySeq = new uint[256];
    private int _viewYawMover = -1;
    private float _viewYawLast;

    /// <summary>Pose tick for drawing movers this frame: matches the tick blend used for the local player.</summary>
    public double MoverRenderTick =>
        IsServer ? Math.Max(0.0, ServerTick - 1.0 + _alpha) : Math.Max(0.0, (_moverClockSet ? _moverTick : ServerTick) - 1.0 + _alpha);

    /// <summary>Client: the tick to stamp on the next predicted command.</summary>
    private uint NextMoverTick(uint seq)
    {
        if (!_moverClockSet)
            return 0; // no snapshot yet: let the server use its own clock
        _moverTick++;
        _moverStampBySeq[seq & 255] = _moverTick;
        MoverPhysics.Clock = _moverTick;
        return _moverTick;
    }

    /// <summary>Client, on each snapshot: steer the stamps so the server processes each command at its stamped tick.</summary>
    private void SteerMoverClock(uint serverTick, uint ackSeq)
    {
        if (!_moverClockSet)
        {
            _moverClockSet = true;
            _moverTick = serverTick + 6; // a guess (latency + the server's input buffer); steering fixes it
            MoverPhysics.Clock = _moverTick;
            return;
        }
        if (ackSeq == 0 || _nextSeq - ackSeq > 200 || _moverStampBySeq[ackSeq & 255] == 0)
            return;
        var err = (double)serverTick - _moverStampBySeq[ackSeq & 255]; // > 0: our stamps run behind the server
        if (Math.Abs(err) > 20)
        {
            _moverTick = (uint)Math.Max(1, _moverTick + err);
            _moverTickError = 0;
            Array.Clear(_moverStampBySeq); // older stamps say nothing about the new clock
            return;
        }
        _moverTickError += (err - _moverTickError) * 0.1;
        if (_moverTickError > 1)
        {
            _moverTick++;
            _moverTickError -= 1;
        }
        else if (_moverTickError < -1)
        {
            _moverTick--;
            _moverTickError += 1;
        }
    }

    /// <summary>Server: the tick a client's command is simulated at (its stamp if it's sane, else the server clock).</summary>
    private uint ServerMoverTick(uint stamp) =>
        stamp != 0 && stamp + MaxMoverTickSkew >= ServerTick && stamp <= ServerTick + MaxMoverTickSkew ? stamp : 0;

    /// <summary>Per frame: the local view turns with the rotating platform under you (see <see cref="MoverPhysics.TurnViewWithPlatform"/>).</summary>
    private void FollowMoverYaw()
    {
        if (!MoverPhysics.TurnViewWithPlatform || World.Map.Movers.Count == 0 || Local is not { Alive: true } me || !me.Sim.Grounded)
        {
            _viewYawMover = -1;
            return;
        }
        var simTick = IsServer ? ServerTick : _moverClockSet ? _moverTick : ServerTick;
        var m = MoverPhysics.SupportMover(me.Position, simTick);
        if (m < 0 || World.Map.Movers[m].Spin == 0)
        {
            _viewYawMover = -1;
            return;
        }
        var yaw = World.Map.Movers[m].PoseAt(MoverRenderTick).Yaw;
        if (_viewYawMover == m)
            LookYaw = World.WrapAngle(LookYaw + World.WrapAngle(yaw - _viewYawLast));
        _viewYawMover = m;
        _viewYawLast = yaw;
    }

    /// <summary>Client: a remote player drawn at the interpolation tick, standing on a mover, rides it to the tick movers are drawn at.</summary>
    private void CarryRemoteRender(Player p)
    {
        if (World.Map.Movers.Count == 0)
            return;
        var yaw = p.RenderYaw;
        p.RenderPosition = MoverPhysics.CarryBetween(p.RenderPosition, RenderTick, MoverRenderTick, ref yaw);
        p.RenderYaw = yaw;
    }

    /// <summary>
    /// Lag compensation: a target rewound to <paramref name="viewTick"/> who stood on a mover is moved with it to the
    /// current tick, which is about where the shooter saw the platform (clients draw movers at their predicted tick).
    /// </summary>
    private Vector3 CarryRewound(Vector3 feet, double viewTick, ref float yaw) =>
        World.Map.Movers.Count == 0 ? feet : MoverPhysics.CarryBetween(feet, viewTick, ServerTick, ref yaw);
}
