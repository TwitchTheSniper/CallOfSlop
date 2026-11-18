using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// pg01 (user, 2026-11-17): a joined holder's own held piece is simulated on the client by the server's own spring
/// (<see cref="ConstructGrab.Follow"/>: the same settle time, top pull, the beam's acceleration for the piece's mass and the
/// holder's pull, gravity, and the physics step's damping and speed limits), so it swings and trails behind the crosshair
/// as it does on the host, instead of being drawn on the crosshair. The client steps it once a local tick, labelled on
/// the stamped mover clock (GameMovers.cs: the server runs our look of mover tick T at its own tick T), keeps the poses it predicted by tick, and
/// eases the whole predicted path towards the server's pose of the same tick (the bodies' drawn tick,
/// <see cref="ConstructWorld.NetRenderTick"/>), so a disagreement closes without a snap and without the delay feeding back.
/// A wall or a floor the server stopped it at still shows: the drawn piece leads the server's by at most what the old
/// prediction allowed (<see cref="ConstructGrab.Predict"/>'s lead). Let go or thrown, it flies on ballistically (a throw
/// adds the server's throw speed) until the server's drawn pose has passed the let-go, then the gap fades as before.
/// pg02: a holder standing on a deck simulates it in the deck's frame (the segment carrying them, as the server's beam is
/// on top of their deck's motion: GameSandboxRide.SandboxRiderGrab): the pose, the velocities, the path kept and the lead
/// are the deck's, and the drawn deck carries it, so a ship under way doesn't leave it behind or glue it to the crosshair.
/// A held joint has its own prediction beside this one (GameSandboxPhysGunSimJoint.cs).
/// </summary>
public sealed partial class Game
{
    private const int SimHistory = 128;
    private const float SimReconcileSeconds = 0.15f, SimReleaseFade = 0.25f;
    private readonly Vector3[] _simHistPos = new Vector3[SimHistory];
    private readonly Quaternion[] _simHistRot = new Quaternion[SimHistory];
    private readonly uint[] _simHistTick = new uint[SimHistory];
    private bool _simOn, _simFree, _simStill;
    private float _simThrowSpeed;
    private ushort _simBody;
    private uint _simTick, _simFreeTick, _simCmds;
    private Vector3 _simPos, _simVel, _simSpin, _simPrevPos, _simGrabLocal, _simAimWas, _simDrawnPos;
    private Quaternion _simRot = Quaternion.Identity, _simPrevRot = Quaternion.Identity, _simDrawnRot = Quaternion.Identity;
    private float _simAimSpeed, _simBodySpeed;
    private bool _simServerHave;
    private Vector3 _simServerPos;
    private Quaternion _simServerRot = Quaternion.Identity;
    private ushort _simFrameBody; // pg02: the deck the simulation is kept in (0: the world), and its segment
    private int _simFrameSeg;
    private Vector3 _simFt; // the frame's drawn pose this frame
    private Quaternion _simFq = Quaternion.Identity;

    /// <summary>For the window tool's check: is the holder's piece drawn from the client's simulation now?</summary>
    public bool SandboxHeldSimulated => _simOn;

    /// <summary>For the window tool's check: how far the drawn piece leads the server's pose of the drawn tick (m), in the frame.</summary>
    public float SandboxHeldLead { get; private set; }

    /// <summary>
    /// Client, every frame from <see cref="SandboxPredictHeld"/>: the held piece (or the one just let go) drawn from the
    /// client's own simulation. False: not simulated (the old drawing or the let-go fade goes on).
    /// </summary>
    private bool SandboxSimHeld(Construct? held, Player? me, float dt)
    {
        if (IsServer || !_moverClockSet || double.IsNaN(Constructs.NetRenderTick))
            return SimStop();
        if (held != null && me != null)
        {
            SimRiding(me, held, out var frameBody, out var frameSeg);
            if (_simOn && _simBody != held.Id)
                SimStop(); // (another piece taken while the last one flies on: that one fades from where it was drawn, no snap)
            if (!_simOn || _simBody != held.Id)
            {
                if (!SimSetFrame(frameBody, frameSeg))
                    return SimStop();
                SimStart(held);
            }
            else if (frameBody != _simFrameBody || frameSeg != _simFrameSeg)
                SimReframe(held, frameBody, frameSeg); // (stepped onto a deck or off it, or onto a lift on it: the same pose, kept in the new frame)
            _simFree = _simStill = false;
            _simGrabLocal = held.GrabLocal;
        }
        else if (_simOn && !_simFree)
        {
            _simFree = true; // (let go: it flies on from the predicted motion; a throw adds what the server adds)
            _simFreeTick = _simTick;
            if (_simThrowSpeed > 0)
                _simVel += Vector3.Transform(Vector3.Normalize(LookDir(LookYaw, LookPitch)), Quaternion.Conjugate(_simFq)) * _simThrowSpeed;
            _simThrowSpeed = 0;
            if (_simStill)
                _simVel = _simSpin = Vector3.Zero;
        }
        if (!_simOn)
            return false;
        var c = Constructs.Get(_simBody);
        if (c == null || c.CarriedBy != 0 || !SimFramePose())
            return SimStop();
        // One step a local tick (a predicted command made), labelled by the mover clock, as the server runs this look. Not
        // by the clock's own count: its steering (GameMovers.SteerMoverClock) moves it a tick without a local tick, and a
        // step more or less in one frame drew the piece a tick's travel on or back (845 m/s2 on Windows CI; a jump of the
        // clock past the history starts the path again).
        var target = _moverTick;
        if (target > _simTick + 30 || target + 30 < _simTick)
            SimStart(c);
        // pg02: everything below is in the frame: the aim and the turn the look asks for, the server's pose, the path kept.
        var inv = Quaternion.Conjugate(_simFq);
        var due = _moverCmds - _simCmds;
        _simCmds = _moverCmds;
        var steps = 0;
        var eye = me?.RenderEye ?? Vector3.Zero;
        var look = LookDir(LookYaw, LookPitch);
        ref var hand = ref SandboxHands.Hand(LocalId);
        var aim = Vector3.Transform(eye + look * _sbPredDist - _simFt, inv);
        var creative = SandboxInfiniteParts;
        var accel = ConstructGrab.AccelFor(c.Mass, creative, SandboxHands.PullOf(LocalId, creative));
        var wantRot = Quaternion.Normalize(inv * ConstructGrab.LookQ(hand, LookYaw) * _sbPredRel);
        while (due > 0 && steps++ < 8)
        {
            due--;
            SimStep(c, aim, wantRot, accel, !_simFree && me != null, inv);
        }
        if (_simTick != target)
        {
            _simTick = target; // (the steering's tick: the path carries on, labelled on the clock again)
            SimRecord();
        }
        // The aim's speed and the server's, held up for a moment when they drop (as the old prediction's lead).
        var keep = MathF.Exp(-dt / 0.3f);
        var aimSpeed = dt > 1e-4f ? MathF.Min(Vector3.Distance(aim, _simAimWas) / dt, 60f) : 0f;
        _simAimWas = aim;
        _simAimSpeed = MathF.Max(aimSpeed, _simAimSpeed * keep);
        _simBodySpeed = MathF.Max((c.Vel - SimFrameVel(c.Pos)).Length(), _simBodySpeed * keep);
        _simServerPos = c.Pos;
        _simServerRot = c.Rot;
        var serverPos = Vector3.Transform(c.Pos - _simFt, inv);
        var serverRot = Quaternion.Normalize(inv * c.Rot);
        // Ease the predicted path towards the server's pose of the same tick (c.Pos/Rot: the blended pose of the drawn tick).
        var rt = Constructs.NetRenderTick;
        if (SimHistoryAt(rt, out var hp, out var hr))
        {
            var a = 1f - MathF.Exp(-dt / SimReconcileSeconds);
            var dp = (serverPos - hp) * a;
            var er = Quaternion.Normalize(serverRot * Quaternion.Conjugate(hr));
            if (er.W < 0)
                er = -er;
            var dq = Quaternion.Normalize(Quaternion.Slerp(Quaternion.Identity, er, a));
            if (float.IsFinite(dp.X + dp.Y + dp.Z + dq.W))
            {
                _simPos += dp;
                _simPrevPos += dp;
                _simRot = Quaternion.Normalize(dq * _simRot);
                _simPrevRot = Quaternion.Normalize(dq * _simPrevRot);
                for (var i = 0; i < SimHistory; i++)
                    if (_simHistTick[i] != 0)
                    {
                        _simHistPos[i] += dp;
                        _simHistRot[i] = Quaternion.Normalize(dq * _simHistRot[i]);
                    }
            }
        }
        // The drawn piece between the last two predicted ticks, as the local player is drawn.
        var alpha = Math.Clamp(_alpha, 0f, 1f);
        var pos = Vector3.Lerp(_simPrevPos, _simPos, alpha);
        var rot = Quaternion.Normalize(Quaternion.Slerp(_simPrevRot, _simRot, alpha));
        {
            // A wall or the floor the server stopped it at: never more than the old prediction's lead ahead of the server.
            // Let go too (the flight has no contacts): only the server's own motion leads it then, so a piece let go on the
            // floor stays on it rather than sinking until the server's pose has passed the let-go.
            var lead = (float)Math.Max(0, (_simTick - rt) * World.TickDt) + ConstructGrab.LeadSeconds;
            var moving = _simFree ? 1f : Math.Clamp(_simAimSpeed / ConstructGrab.RestSpeed, 0f, 1f);
            var allowed = MathF.Max(ConstructGrab.LeadMin * moving, ConstructGrab.RestLead) + (_simFree ? _simBodySpeed : MathF.Max(_simBodySpeed, _simAimSpeed)) * lead;
            var off = pos - serverPos;
            if (off.LengthSquared() > allowed * allowed)
            {
                var len = off.Length();
                var back = off * (1f - allowed / len);
                pos -= back;
                _simPos -= back;
                _simPrevPos -= back;
                if (_simFree)
                {
                    var along = Vector3.Dot(_simVel, off / len); // (and it stops flying on that way: gravity on the floor, the push into a wall)
                    if (along > 0)
                        _simVel -= off / len * along;
                }
            }
        }
        SandboxHeldLead = Vector3.Distance(pos, serverPos);
        c.Pos = _simFt + Vector3.Transform(pos, _simFq); // (the drawn deck carries it)
        c.Rot = Quaternion.Normalize(_simFq * rot);
        _simDrawnPos = c.Pos;
        _simDrawnRot = c.Rot;
        if (_simFree && rt >= _simFreeTick + 2)
        {
            // The server's drawn pose is past the let-go: the gap to it fades, as a let-go of the old drawing did.
            _simServerHave = true;
            SimStop();
            return false;
        }
        return true;
    }

    /// <summary>Ends the simulation; a piece let go fades from where it was drawn to the server's pose.</summary>
    private bool SimStop()
    {
        if (_simOn && Constructs.Get(_simBody) is { } was && _simFree)
        {
            var (serverPos, serverRot) = _simServerHave ? (_simServerPos, _simServerRot) : (was.Pos, was.Rot);
            _sbFadeBody = _simBody;
            _sbFadePos = _simDrawnPos - serverPos;
            _sbFadeRot = Quaternion.Normalize(_simDrawnRot * Quaternion.Conjugate(serverRot));
            _sbFadeSlow = true;
        }
        _simServerHave = false;
        _simOn = false;
        _simFree = false;
        _simThrowSpeed = 0;
        return false;
    }

    /// <summary>Starts the simulation from the piece's drawn pose and the server's motion of it.</summary>
    private void SimStart(Construct c)
    {
        _simOn = true;
        _simFree = false;
        _simBody = c.Id;
        var inv = Quaternion.Conjugate(_simFq);
        _simPos = _simPrevPos = Vector3.Transform(c.Pos - _simFt, inv);
        _simRot = _simPrevRot = Quaternion.Normalize(inv * c.Rot);
        _simVel = Vector3.Transform((c.Asleep ? Vector3.Zero : c.Vel) - SimFrameVel(c.Pos), inv);
        _simSpin = Vector3.Transform((c.Asleep ? Vector3.Zero : c.AngVel) - SimFrameSpin(), inv);
        _simTick = _moverTick;
        _simCmds = _moverCmds;
        _simAimSpeed = _simBodySpeed = 0;
        _simThrowSpeed = 0;
        _simAimWas = _simPos + Vector3.Transform(c.GrabLocal, _simRot);
        Array.Clear(_simHistTick);
        SimRecord();
    }

    /// <summary>One server tick of the held piece, by the physics step's rules for a body in the beam (no contacts: the server's pose brings those).</summary>
    private void SimStep(Construct c, Vector3 aim, Quaternion wantRot, float accel, bool beam, Quaternion inv)
    {
        var dt = (float)World.TickDt;
        _simPrevPos = _simPos;
        _simPrevRot = _simRot;
        var com = c.Com;
        var worldCom = _simPos + Vector3.Transform(com, _simRot); // (in the frame)
        var weight = GravityFrames.Curved || GravityFrames.HookCount > 0 ? ConstructGravity.GravityFor(c, _simFt + Vector3.Transform(worldCom, _simFq)) : new Vector3(0, -ConstructPhysics.GravityAccel, 0);
        _simVel += Vector3.Transform(weight, inv) * ((1 - c.AntiShare) * dt); // (the world's gravity, turned into the deck's frame)
        if (_simFree && _simStill)
        {
            _simTick++;
            SimRecord();
            return;
        }
        if (beam)
            ConstructGrab.Follow(_simPos, _simRot, com, _simGrabLocal, ref _simVel, ref _simSpin, aim, wantRot, accel, Vector3.Zero, Vector3.Zero, dt);
        // ConstructPhysics.Integrate: the gentle damping, the limits, then the move about the centre of mass.
        _simVel *= 0.999f;
        _simSpin *= 0.985f;
        if (_simVel.LengthSquared() > ConstructPhysics.MaxSpeed * ConstructPhysics.MaxSpeed)
            _simVel *= ConstructPhysics.MaxSpeed / _simVel.Length();
        if (_simSpin.LengthSquared() > ConstructPhysics.MaxSpin * ConstructPhysics.MaxSpin)
            _simSpin *= ConstructPhysics.MaxSpin / _simSpin.Length();
        var next = worldCom + _simVel * dt;
        var spin = new Quaternion(_simSpin.X, _simSpin.Y, _simSpin.Z, 0) * _simRot;
        _simRot = Quaternion.Normalize(new Quaternion(_simRot.X + 0.5f * dt * spin.X, _simRot.Y + 0.5f * dt * spin.Y, _simRot.Z + 0.5f * dt * spin.Z, _simRot.W + 0.5f * dt * spin.W));
        _simPos = next - Vector3.Transform(com, _simRot);
        if (!float.IsFinite(_simPos.X + _simPos.Y + _simPos.Z + _simRot.W))
        {
            _simPos = Vector3.Transform(c.Pos - _simFt, inv);
            _simRot = Quaternion.Normalize(inv * c.Rot);
            _simVel = _simSpin = Vector3.Zero;
        }
        _simTick++;
        SimRecord();
    }

    private void SimRecord()
    {
        var i = (int)(_simTick % SimHistory);
        _simHistTick[i] = _simTick;
        _simHistPos[i] = _simPos;
        _simHistRot[i] = _simRot;
    }

    /// <summary>The predicted pose at a (fractional) server tick, if it is still kept.</summary>
    private bool SimHistoryAt(double tick, out Vector3 pos, out Quaternion rot)
    {
        pos = default;
        rot = Quaternion.Identity;
        if (tick < 1)
            return false;
        var t0 = (uint)Math.Floor(tick);
        var i0 = (int)(t0 % SimHistory);
        if (t0 == 0 || _simHistTick[i0] != t0)
            return false;
        var f = (float)(tick - t0);
        var i1 = (int)((t0 + 1) % SimHistory);
        if (f > 1e-4f && _simHistTick[i1] == t0 + 1)
        {
            pos = Vector3.Lerp(_simHistPos[i0], _simHistPos[i1], f);
            rot = Quaternion.Normalize(Quaternion.Slerp(_simHistRot[i0], _simHistRot[i1], f));
        }
        else
        {
            pos = _simHistPos[i0];
            rot = _simHistRot[i0];
        }
        return true;
    }

    /// <summary>pg02: the frame the holder stands in (the segment carrying them, as the server keeps a rider's beam in), or 0: the world.</summary>
    private static void SimRiding(Player me, Construct held, out ushort body, out int seg)
    {
        body = 0;
        seg = 0;
        var ride = me.Sim.RideBody;
        if (me.Sim.RideUp == Vector3.Zero || ride == 0 || ride == held.Id || held.CarriedBy == ride)
            return;
        body = ride;
        seg = ConstructRide.Carrier(me.Sim);
    }

    /// <summary>The frame's drawn pose this frame (a client draws its bodies at their drawn pose already). False: its deck is gone.</summary>
    private bool SimFramePose()
    {
        if (_simFrameBody == 0)
        {
            _simFt = Vector3.Zero;
            _simFq = Quaternion.Identity;
            return true;
        }
        if (Constructs.Get(_simFrameBody) is not { } deck)
            return false;
        deck.WorldFrame(_simFrameSeg <= deck.JointCount ? _simFrameSeg : 0, out _simFq, out _simFt);
        _simFq = Quaternion.Normalize(_simFq);
        return float.IsFinite(_simFt.X + _simFt.Y + _simFt.Z + _simFq.W);
    }

    private bool SimSetFrame(ushort body, int seg)
    {
        (_simFrameBody, _simFrameSeg) = (body, seg);
        if (SimFramePose())
            return true;
        (_simFrameBody, _simFrameSeg) = (0, 0);
        return SimFramePose();
    }

    /// <summary>The frame's own velocity at a world point, and its spin (zero for the world).</summary>
    private Vector3 SimFrameVel(Vector3 at) =>
        _simFrameBody != 0 && Constructs.Get(_simFrameBody) is { } deck ? ConstructRide.SurfaceVel(deck, _simFrameSeg <= deck.JointCount ? _simFrameSeg : 0, at) : Vector3.Zero;

    private Vector3 SimFrameSpin() =>
        _simFrameBody != 0 && Constructs.Get(_simFrameBody) is { } deck ? ConstructRide.SurfaceSpin(deck, _simFrameSeg <= deck.JointCount ? _simFrameSeg : 0) : Vector3.Zero;

    /// <summary>
    /// pg02: the holder moved into another frame mid-hold: the predicted pose and motion are the same in the world, written in the
    /// new frame. The path kept can't be (the old deck's past poses aren't kept), so it starts again: the reconcile waits for it
    /// for the bodies' delay, the lead clamp holds meanwhile.
    /// </summary>
    private void SimReframe(Construct c, ushort body, int seg)
    {
        if (!SimFramePose())
        {
            SimSetFrame(body, seg);
            SimStart(c);
            return;
        }
        var pos = _simFt + Vector3.Transform(_simPos, _simFq);
        var prev = _simFt + Vector3.Transform(_simPrevPos, _simFq);
        var rot = Quaternion.Normalize(_simFq * _simRot);
        var prevRot = Quaternion.Normalize(_simFq * _simPrevRot);
        var vel = Vector3.Transform(_simVel, _simFq) + SimFrameVel(pos);
        var spin = Vector3.Transform(_simSpin, _simFq) + SimFrameSpin();
        var aimWas = _simFt + Vector3.Transform(_simAimWas, _simFq);
        if (!SimSetFrame(body, seg))
        {
            SimStop();
            return;
        }
        var inv = Quaternion.Conjugate(_simFq);
        _simPos = Vector3.Transform(pos - _simFt, inv);
        _simPrevPos = Vector3.Transform(prev - _simFt, inv);
        _simRot = Quaternion.Normalize(inv * rot);
        _simPrevRot = Quaternion.Normalize(inv * prevRot);
        _simVel = Vector3.Transform(vel - SimFrameVel(pos), inv);
        _simSpin = Vector3.Transform(spin - SimFrameSpin(), inv);
        _simAimWas = Vector3.Transform(aimWas - _simFt, inv);
        Array.Clear(_simHistTick);
        SimRecord();
    }
}
