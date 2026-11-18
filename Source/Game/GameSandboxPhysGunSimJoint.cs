using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// pg02 (user, 2026-11-18): a joined holder's held joint (a door's hinge, a swivel's leaf, a slider's carriage held on its own)
/// is simulated on the client by the server's beam on a joint (<see cref="ConstructPhysics"/> BeamRows: the held point's wanted
/// speed closes the gap over <see cref="ConstructGrab.SettleTime"/>, no faster than the beam can brake, and the beam gives what it
/// moves at most its acceleration and gravity's a tick), so it swings and trails behind the crosshair as on the host, instead
/// of being drawn at the angle the aim asks for. As the held body's (GameSandboxPhysGunSim.cs): one step a local tick labelled on
/// the mover clock, the predicted path eased towards the server's angle of the same tick (<see cref="CJoint.NetDrawn"/>, sampled at
/// the drawn tick), no more than the old prediction's lead ahead of it, and let go it swings on at its own speed until the
/// server's drawn angle has passed the let-go, then the gap fades. A piece that hangs on another free joint (a chain) is not
/// simulated: only the server knows how the pull shares out, and the old drawing of the server's angle stays.
/// </summary>
public sealed partial class Game
{
    private readonly float[] _jsHistAngle = new float[SimHistory];
    private readonly uint[] _jsHistTick = new uint[SimHistory];
    private bool _jsOn, _jsFree;
    private ushort _jsBody, _jsHinge;
    private int _jsSeg;
    private uint _jsTick, _jsFreeTick, _jsCmds;
    private float _jsAngle, _jsPrev, _jsVel, _jsDrawn, _jsWas, _jsSpeed, _jsServerSpeed;
    private Vector3 _jsLocal;
    private ushort _jfBody; // the let-go joint whose gap to the server's angle fades
    private int _jfSeg;
    private float _jfOff;

    /// <summary>For the window tool's check: is the holder's joint drawn from the client's simulation now?</summary>
    public bool SandboxJointSimulated => _jsOn;

    /// <summary>
    /// Client, every frame from <see cref="SandboxPredictHeld"/>: the held joint (or the one just let go) drawn from the client's own
    /// simulation. <paramref name="c"/> null: nothing held on a joint now. False: not simulated (the old drawing goes on).
    /// </summary>
    private bool SandboxSimJoint(Construct? c, int seg, Vector3 local, Player? me, float dt)
    {
        JointFade(dt);
        if (IsServer || !_moverClockSet || double.IsNaN(Constructs.NetRenderTick))
            return JointStop();
        if (c != null && me != null)
        {
            if (_jsOn && (_jsBody != c.Id || _jsSeg != seg))
                JointStop();
            if (!_jsOn)
                JointStart(c, seg);
            _jsFree = false;
            _jsLocal = local;
        }
        else if (_jsOn && !_jsFree)
        {
            _jsFree = true; // (let go: it swings on at the predicted speed)
            _jsFreeTick = _jsTick;
        }
        if (!_jsOn)
            return false;
        var b = Constructs.Get(_jsBody);
        if (b == null || _jsSeg > b.JointCount || b.Joints[_jsSeg - 1].Hinge != _jsHinge || !b.Joints[_jsSeg - 1].FreeMoving)
            return JointStop(); // (rebuilt, broken or locked meanwhile: the server's angle as it comes)
        ref var jt = ref b.Joints[_jsSeg - 1];
        var server = jt.NetDrawn; // (the server's angle at the drawn tick: ConstructNet.SampleJoints, before this draws over jt.Angle)
        var wraps = !float.IsFinite(jt.Min);
        var target = _moverTick;
        if (target > _jsTick + 30 || target + 30 < _jsTick)
            JointStart(b, _jsSeg);
        var due = _moverCmds - _jsCmds;
        _jsCmds = _moverCmds;
        var beam = (me?.RenderEye ?? Vector3.Zero) + LookDir(LookYaw, LookPitch) * _sbPredDist;
        var creative = SandboxInfiniteParts;
        var accel = ConstructGrab.AccelFor(b.SegMass[MovedSeg(b, _jsSeg)], creative, SandboxHands.PullOf(LocalId, creative));
        var radius = 1f;
        for (var steps = 0; due > 0 && steps < 8; steps++)
        {
            due--;
            JointStep(b, ref jt, beam, accel, !_jsFree && me != null, out radius);
        }
        if (_jsTick != target)
        {
            _jsTick = target; // (the steering's tick: the path carries on, labelled on the clock again)
            JointRecord();
        }
        // The aim's speed along the joint (what angle it asks for), and the server's, held up for a moment when they drop.
        var keep = MathF.Exp(-dt / 0.3f);
        jt.Angle = _jsAngle;
        var want = !_jsFree && me != null ? ConstructGrab.JointTarget(b, _jsSeg, _jsLocal, beam, out radius) : _jsWas;
        var speed = dt > 1e-4f ? MathF.Min(MathF.Abs(Gap(want, _jsWas, wraps)) / dt, 60f) : 0f;
        _jsWas = want;
        _jsSpeed = MathF.Max(speed, _jsSpeed * keep);
        _jsServerSpeed = MathF.Max(MathF.Abs(jt.Vel), _jsServerSpeed * keep);
        // Ease the predicted path towards the server's angle of the same tick.
        var rt = Constructs.NetRenderTick;
        if (JointHistoryAt(rt, wraps, out var ha))
        {
            var d = Gap(server, ha, wraps) * (1f - MathF.Exp(-dt / SimReconcileSeconds));
            if (float.IsFinite(d))
            {
                _jsAngle += d;
                _jsPrev += d;
                for (var i = 0; i < SimHistory; i++)
                    if (_jsHistTick[i] != 0)
                        _jsHistAngle[i] += d;
            }
        }
        var alpha = Math.Clamp(_alpha, 0f, 1f);
        var at = _jsPrev + Gap(_jsAngle, _jsPrev, wraps) * alpha;
        {
            // A stop or what the server held it at: never more than the old prediction's lead ahead of the server's angle (the
            // lead's distances in metres at the held point's reach from the axis; a slider's are metres already).
            var lead = (float)Math.Max(0, (_jsTick - rt) * World.TickDt) + ConstructGrab.LeadSeconds;
            var moving = _jsFree ? 1f : Math.Clamp(_jsSpeed * MathF.Max(radius, 0.25f) / ConstructGrab.RestSpeed, 0f, 1f);
            var allowed = MathF.Max(ConstructGrab.LeadMin * moving, ConstructGrab.RestLead) / (jt.Slides ? 1f : MathF.Max(radius, 0.25f))
                + (_jsFree ? _jsServerSpeed : MathF.Max(_jsServerSpeed, _jsSpeed)) * lead;
            var off = Gap(at, server, wraps);
            if (MathF.Abs(off) > allowed)
            {
                var back = off - MathF.CopySign(allowed, off);
                at -= back;
                _jsAngle -= back;
                _jsPrev -= back;
                if (_jsFree && _jsVel * off > 0)
                    _jsVel = 0; // (and it stops swinging on that way)
            }
        }
        jt.Angle = Put(ref jt, at);
        _jsDrawn = jt.Angle;
        if (_jsFree && rt >= _jsFreeTick + 2)
        {
            // The server's drawn angle is past the let-go: the gap to it fades.
            JointStop();
            return false;
        }
        return true;
    }

    /// <summary>One server tick of the held joint by the beam's rule (BeamRows), for a piece that hangs on nothing else free.</summary>
    private void JointStep(Construct b, ref CJoint jt, Vector3 beam, float accel, bool held, out float radius)
    {
        var dt = (float)World.TickDt;
        radius = 1f;
        _jsPrev = _jsAngle;
        jt.Angle = _jsAngle;
        if (held)
        {
            b.WorldFrame(_jsSeg, out var sq, out var st);
            var at = st + Vector3.Transform(_jsLocal, sq);
            b.WorldFrame(jt.ParentSeg, out var pq, out var pt);
            var axis = Vector3.Transform(jt.Axis, pq);
            var u = jt.Slides ? axis : Vector3.Cross(axis, at - (pt + Vector3.Transform(jt.Pivot, pq))); // (the held point's velocity a unit of the joint's speed gives)
            var uu = u.LengthSquared();
            radius = MathF.Sqrt(uu);
            var gap = beam - at;
            var want = gap / ConstructGrab.SettleTime;
            var cap = MathF.Min(ConstructGrab.MaxPull, MathF.Sqrt(2f * ConstructGrab.BrakeShare * accel * gap.Length()));
            if (want.LengthSquared() > cap * cap)
                want *= cap / want.Length();
            if (uu > 1e-6f && float.IsFinite(want.X + want.Y + want.Z))
            {
                var dv = Vector3.Dot(u, want) / uu - _jsVel;
                var lim = (accel + ConstructPhysics.GravityAccel) * dt / radius; // (what the beam may give what it moves this tick, at the held point)
                _jsVel += Math.Clamp(dv, -lim, lim);
            }
        }
        _jsAngle += _jsVel * dt;
        if (float.IsFinite(jt.Min))
        {
            if (_jsAngle < jt.Min || _jsAngle > jt.Max)
            {
                _jsAngle = Math.Clamp(_jsAngle, jt.Min, jt.Max);
                _jsVel = 0; // (on its stop)
            }
        }
        else
            _jsAngle = World.WrapAngle(_jsAngle);
        if (!float.IsFinite(_jsAngle + _jsVel))
        {
            _jsAngle = jt.NetDrawn;
            _jsVel = 0;
        }
        _jsTick++;
        JointRecord();
    }

    /// <summary>The segment the beam's pull is given to (GrabJointTick: the held piece and every joint behind it that can give).</summary>
    private static int MovedSeg(Construct c, int seg)
    {
        var moved = seg;
        for (int s = seg, guard = 0; s > 0 && s <= c.JointCount && guard <= Construct.MaxJoints; guard++)
        {
            ref var jt = ref c.Joints[s - 1];
            if (!jt.Freeform)
            {
                if (jt.FrozenLock || jt.Locked)
                    break;
                moved = s;
            }
            s = jt.ParentSeg;
        }
        return (uint)moved < (uint)c.SegMass.Length ? moved : 0;
    }

    private void JointStart(Construct c, int seg)
    {
        ref var jt = ref c.Joints[seg - 1];
        _jsOn = true;
        _jsFree = false;
        _jsBody = c.Id;
        _jsSeg = seg;
        _jsHinge = jt.Hinge;
        _jsAngle = _jsPrev = _jsDrawn = _jsWas = jt.NetDrawn;
        _jsVel = c.Asleep ? 0 : jt.Vel;
        _jsTick = _moverTick;
        _jsCmds = _moverCmds;
        _jsSpeed = _jsServerSpeed = 0;
        if (_jfBody == c.Id && _jfSeg == seg)
        {
            _jsAngle = _jsPrev = _jsDrawn = jt.NetDrawn + _jfOff; // (taken again while it fades: from where it is drawn)
            _jfBody = 0;
        }
        Array.Clear(_jsHistTick);
        JointRecord();
    }

    /// <summary>Ends the simulation; a joint let go fades from where it was drawn to the server's angle.</summary>
    private bool JointStop()
    {
        if (_jsOn && Constructs.Get(_jsBody) is { } b && _jsSeg <= b.JointCount && b.Joints[_jsSeg - 1].Hinge == _jsHinge)
        {
            ref var jt = ref b.Joints[_jsSeg - 1];
            (_jfBody, _jfSeg) = (_jsBody, _jsSeg);
            _jfOff = Gap(_jsDrawn, jt.NetDrawn, !float.IsFinite(jt.Min));
            if (!float.IsFinite(_jfOff))
                _jfBody = 0;
        }
        _jsOn = false;
        _jsFree = false;
        return false;
    }

    /// <summary>Every frame: the let-go joint's gap to the server's angle closes, as a let-go body's does.</summary>
    private void JointFade(float dt)
    {
        if (_jfBody == 0)
            return;
        if (Constructs.Get(_jfBody) is not { } b || _jfSeg > b.JointCount || MathF.Abs(_jfOff) < 1e-4f)
        {
            if (b != null && _jfSeg <= b.JointCount)
                b.Joints[_jfSeg - 1].Angle = Put(ref b.Joints[_jfSeg - 1], b.Joints[_jfSeg - 1].NetDrawn);
            _jfBody = 0;
            return;
        }
        _jfOff *= MathF.Exp(-dt / SimReleaseFade);
        ref var jt = ref b.Joints[_jfSeg - 1];
        jt.Angle = Put(ref jt, jt.NetDrawn + _jfOff);
    }

    private static float Put(ref CJoint jt, float angle) => float.IsFinite(jt.Min) ? Math.Clamp(angle, jt.Min, jt.Max) : World.WrapAngle(angle);

    private static float Gap(float a, float b, bool wraps) => wraps ? World.WrapAngle(a - b) : a - b;

    private void JointRecord()
    {
        var i = (int)(_jsTick % SimHistory);
        _jsHistTick[i] = _jsTick;
        _jsHistAngle[i] = _jsAngle;
    }

    /// <summary>The predicted angle at a (fractional) server tick, if it is still kept.</summary>
    private bool JointHistoryAt(double tick, bool wraps, out float angle)
    {
        angle = 0;
        if (tick < 1)
            return false;
        var t0 = (uint)Math.Floor(tick);
        var i0 = (int)(t0 % SimHistory);
        if (_jsHistTick[i0] != t0)
            return false;
        angle = _jsHistAngle[i0];
        var i1 = (int)((t0 + 1) % SimHistory);
        if (_jsHistTick[i1] == t0 + 1)
            angle += Gap(_jsHistAngle[i1], angle, wraps) * (float)(tick - t0);
        return true;
    }
}
