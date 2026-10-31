using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The construct physics step (server; the client only draws what it hears). Cheap and stable on purpose:
/// <list type="bullet">
/// <item>each construct is one rigid body (box inertia from its parts) integrated at the fixed 60 Hz tick;</item>
/// <item>against the world: up to <see cref="Construct.MaxPoints"/> hull points per body, each probed with two rays (down,
/// and along its motion), giving speculative contacts solved with sequential impulses (friction included);</item>
/// <item>between constructs: hull points of one against the part boxes of the other;</item>
/// <item>wheels are raycast wheels: a spring-damper along the suspension, side grip, drive and brakes, clamped to a
/// friction circle;</item>
/// <item>joints are angles (reduced coordinates), and the parts beyond them collide too: each segment has its own
/// contact points (<see cref="Construct.SegPoints"/>) probed like the hull's (and swept against other constructs when
/// they move fast), and every contact's impulse acts on the body and on each joint up its segment's chain at once
/// (generalised velocities: the body's six and one per joint). The motors and the joint limits are solved in the same
/// loop, so a blocked piece stops its joint: a passive hinge rests on the ground, a motor stalls at its strength or, if
/// it is strong enough, lifts or pushes its construct;</item>
/// <item>a body that stays still sleeps and costs nothing until something wakes it (an anchored one with joints too).</item>
/// </list>
/// Everything uses fixed arrays: no allocation per tick.
/// </summary>
public static class ConstructPhysics
{
    public const int Iterations = 6;
    public const int JointIterations = 6; // more passes over the joints and the contacts on jointed constructs (the strength model: chains of actuators stay rigid)
    public const int MaxContacts = 1024;
    public const float Slop = 0.01f;
    public const float Beta = 0.25f;
    public const float MaxSpeed = 60f;
    public const float MaxSpin = 18f;
    public const float MaxJointSpin = 25f; // radians (or metres) a second: anything faster is clamped
    public const int SleepAfterTicks = 40;
    public const float StillSpeed2 = 0.02f; // (m/s or rad/s, squared) slower than this counts as still, for sleep
    public const float DirtGrip = 0.85f; // terrain (dirt) grips this much of hard ground, times a tyre's off-road grip
    public const float AirDrag = 0.006f; // a wheeled construct on the ground slows by this times its speed squared (m/s2): arcade air drag
    public const float AntiRoll = 0.7f; // 0: tyres push at the ground; 1: at the centre of mass's height (no roll at all)
    public const int MaxChain = 4096; // the joint chain entries of every contact on a segment, in all
    public const float PivotBeta = 0.3f; // a coupling's free pivot: the share of its error put right each tick
    public const float PivotMaxFix = 4f; // m/s at most to put it right (a coupling made apart pulls together, not snaps)
    public const float HitchClear = 0.7f;
    // The strength model (board item dev-sandbox-actuator-sizes-as01; MotorPrep): a motor holds rigidly up to its rating, then gives.
    public const float OverCap = 4f; // the most an overloaded motor pushes, times its rating (then it slips)
    public const float RefGain = 10f; // a second: how hard a motor pulls its joint back onto its reference (the speed it asks per unit behind)
    public const float YieldDamp = 20f; // a second: how fast an overloaded joint's speed away from its reference dies (damping the give)
    public const float ForceSmooth = 0.25f; // the share of this tick's load the shown load takes // coupled constructs' parts this near a hitch point don't collide with each other

    /// <summary>A motor's push and a joint's stops push the body back (milestone 3: a turret swung hard rocks a light buggy). The self-test turns it off to compare.</summary>
    public static bool JointReactions = true;

    public struct Contact
    {
        public short A, B; // body indices in the world's list (B -1: the static world)
        public Vector3 Point, Normal; // normal points from B into A
        public float Gap; // > 0: still apart by this much; < 0: overlapping
        public float Friction;
        public float Jn, Jt1, Jt2; // accumulated impulses
        public Vector3 T1, T2;
        public float MassN, MassT1, MassT2, Bias;
        public Vector3 RA, RB; // from each body's centre of mass
        public int UA, UB; // each side's joint chain in the chain arrays (start; the count is NA, NB)
        public byte NA, NB;
        public bool Bilateral; // a coupling's free pivot (milestone 3): pushes either way, no friction; Gap is the error along the normal
        public bool TwoPoints; // a coupling: B's point is PointB (each body's own hitch point)
        public Vector3 PointB;
    }

    private static readonly Contact[] _contacts = new Contact[MaxContacts];
    private static int _contactCount;
    private static readonly Matrix4x4[] _invI = new Matrix4x4[ConstructWorld.MaxBodies];
    private static readonly Matrix4x4[] _invIB = new Matrix4x4[ConstructWorld.MaxBodies]; // what a joint's push turns (Construct.InvInertiaBaseLocal)
    private static readonly bool[] _dyn = new bool[ConstructWorld.MaxBodies]; // the body itself moves (free and awake)
    private static readonly bool[] _jact = new bool[ConstructWorld.MaxBodies]; // its joints move (awake, with joints)

    // Joints this tick, by slot (body index * MaxJoints + joint): their world axis and pivot, inverse inertia, motor and limits.
    private const int Slots = ConstructWorld.MaxBodies * Construct.MaxJoints;
    private static readonly Vector3[] _jAxis = new Vector3[Slots], _jPivot = new Vector3[Slots];
    private static readonly float[] _jInvI = new float[Slots], _jWant = new float[Slots], _jMotorMax = new float[Slots];
    private static readonly float[] _jLoB = new float[Slots], _jHiB = new float[Slots], _jP = new float[Slots], _jLo = new float[Slots], _jHi = new float[Slots];
    private static readonly float[] _jRating = new float[Slots], _jSide = new float[Slots]; // the strength model: this tick's rating (side load taken off), side load against what it takes
    private static readonly short[] _active = new short[Slots];
    private static int _activeCount;
    // Segment frames this tick (body index * (MaxJoints + 1) + segment).
    private static readonly Quaternion[] _segQ = new Quaternion[ConstructWorld.MaxBodies * (Construct.MaxJoints + 1)];
    private static readonly Vector3[] _segT = new Vector3[ConstructWorld.MaxBodies * (Construct.MaxJoints + 1)];
    // A contact's joint chain: for each joint up its segment's chain, the velocity a unit of joint speed gives the point.
    private static readonly Vector3[] _chainU = new Vector3[MaxChain];
    private static readonly short[] _chainSlot = new short[MaxChain];
    private static int _chainCount;
    private static IList<Construct> _bodies = Array.Empty<Construct>();
    private static ConstructWorld? _world;
    // Coupled pairs this tick (milestone 3): their parts near the hitch points don't collide with each other.
    private static readonly short[] _exA = new short[ConstructWorld.MaxCouplings], _exB = new short[ConstructWorld.MaxCouplings];
    private static readonly Vector3[] _exPa = new Vector3[ConstructWorld.MaxCouplings], _exPb = new Vector3[ConstructWorld.MaxCouplings];
    private static int _exCount;
    private static bool _exOn; // the pair being tested is coupled: skip points near
    private static Vector3 _exP0, _exP1;

    public static int LastContacts => _contactCount;
    public static int LastRays { get; private set; }
    /// <summary>Of <see cref="LastContacts"/>, how many were on parts beyond joints.</summary>
    public static int LastJointContacts { get; private set; }

    public static float GravityAccel => World.Gravity * World.GravityScale;

    /// <summary>One tick for every awake free body of <paramref name="world"/>, and the joints of every awake body.</summary>
    public static void Step(ConstructWorld world, IWorldQuery query, float dt)
    {
        var bodies = world.Bodies;
        _bodies = bodies;
        var n = Math.Min(bodies.Count, ConstructWorld.MaxBodies);
        _contactCount = 0;
        _chainCount = 0;
        _activeCount = 0;
        LastRays = 0;
        LastJointContacts = 0;
        var g = new Vector3(0, -GravityAccel, 0);
        for (var i = 0; i < n; i++)
        {
            var c = bodies[i];
            if (c.Dirty)
                c.Rebuild();
            c.SimIndex = i;
            c.PrevPos = c.Pos;
            c.PrevRot = c.Rot;
            for (var j = 0; j < c.JointCount; j++)
                c.Joints[j].PrevAngle = c.Joints[j].Angle;
            if (c.GrabbedBy != 255)
                c.Wake(); // (held by a physics gun: never asleep, ConstructGrab.cs)
            if (c.Asleep && c.MovingJoints > 0)
                WakeCheck(c);
            _dyn[i] = !c.Anchored && !c.Asleep && c.CarriedBy == 0; // (cargo in a frozen volume rides kinematically: ConstructCargo.cs)
            _jact[i] = !c.Asleep && c.MovingJoints > 0 && c.CarriedBy == 0; // (freeform welds are rigid: their parts are hull points)
            _invI[i] = _dyn[i] ? c.InvInertiaWorld() : default;
            _invIB[i] = _dyn[i] && c.MovingJoints > 0 ? c.InvInertiaBaseWorld() : default;
            if (_dyn[i])
            {
                c.Vel += g * dt;
                if (c.GrabbedBy != 255)
                    ConstructGrab.Drive(c, dt); // (the physics gun's spring, after gravity: the contacts below still stop it)
                if (c.WheelCount > 0)
                    Wheels(c, query, _invI[i], dt);
                WorldContacts(c, (short)i, query, dt);
            }
            if (_jact[i])
            {
                JointPrep(c, i, dt);
                SegmentWorldContacts(c, (short)i, query, dt);
                SegmentEdgeContacts(c, (short)i, query, dt);
            }
        }
        _world = world;
        CouplingExclusions(world, n);
        BodyContacts(world, n, dt);
        CouplingRows(world, n, dt);
        Solve(dt);
        for (var i = 0; i < n; i++)
        {
            var c = bodies[i];
            if (_dyn[i])
                Integrate(c, dt);
            if (_jact[i])
                IntegrateJoints(c, i, dt);
            if (_dyn[i] || _jact[i])
                Sleep(c);
        }
    }

    // ---- Wheels -------------------------------------------------------------------------------------------------

    private static void Wheels(Construct c, IWorldQuery query, in Matrix4x4 invI, float dt)
    {
        var up = Vector3.Transform(Vector3.UnitY, c.Rot);
        var fwdBody = Vector3.Transform(c.SeatForward(), c.Rot);
        var com = c.WorldCom;
        var share = c.Mass / c.WheelCount;
        var driving = c.CanDrive && (c.Driver != 255 || c.EngineWired) && c.EngineRunning; // (the power step starts and stops the engine: ConstructPower.cs)
        var throttle = c.EngineWired ? c.EngineSignal : c.Throttle; // a wired engine runs on its signal
        var steer = c.Driver != 255 ? c.SteerInput : 0f;
        var brake = c.Brake || c.BrakeSignal >= 0.999f;
        var partBrake = brake ? 0f : c.BrakeSignal; // (a wired brake set below full strength drags rather than locks)
        var touching = 0;
        for (var k = 0; k < c.WheelCount; k++)
            if (c.Wheels[k].Touching)
                touching++;
        var forwardSpeed = Vector3.Dot(c.Vel, fwdBody);
        ConstructDrive.Tick(c, driving, throttle, steer, forwardSpeed, dt); // the engine: gear, rpm, torque, each wheel's share
        var want = MathF.Max(MathF.Abs(throttle), c.SkidSteer ? MathF.Abs(steer) : 0);
        var g = GravityAccel;
        var rot = Matrix4x4.CreateFromQuaternion(c.Rot);
        var lockedTouch = 0;
        for (var k = 0; k < c.WheelCount; k++)
        {
            ref var w = ref c.Wheels[k];
            c.WheelFrame(k, out var centreBody, out var axleBody);
            var origin = c.ToWorld(centreBody);
            var len = w.Travel + w.Radius;
            LastRays++;
            w.PrevCompression = w.Compression;
            if (!query.Raycast(origin, -up, len, QueryFilter.All, out var hit))
            {
                w.Touching = false;
                w.Compression = 0;
                continue;
            }
            var wasTouching = w.Touching;
            w.Touching = true;
            var x = MathF.Min(len - hit.Distance, w.Travel);
            w.Compression = x;
            if (!wasTouching)
                w.PrevCompression = x; // first touch: no damping kick
            // The spring: an adjustable one's preset, an active one's height; a load past what it is built for sags it.
            ref var sp = ref c.Parts[w.Suspension];
            var sd = sp.D;
            var freq = ConstructConfig.Get(sp, sd, CfgKey.Stiffness, w.Frequency); // (the configure tool's spring settings)
            var damp = ConstructConfig.Get(sp, sd, CfgKey.Damping, w.Damping);
            var hold = -1f;
            if (sd.SuspFrequency.Length > 0)
            {
                var si = Math.Min((int)sp.Setting, sd.SuspFrequency.Length - 1);
                freq = sd.SuspFrequency[si];
                damp = sd.SuspDamping[si];
                hold = sd.SuspHold[si];
            }
            else if (sd.ActiveSuspension)
                hold = !ConstructPower.On(c, w.Suspension) ? 0f : sp.InCount > 0 ? Math.Clamp(sp.In, 0, 1) : 0.5f; // (unwired: half way; no power: it sinks)
            var load = MathF.Min(share, w.MaxLoad);
            var omega = MathF.Tau * freq;
            var k_s = load * omega * omega;
            var c_d = 2 * damp * MathF.Sqrt(k_s * load);
            var rate = (w.Compression - w.PrevCompression) / dt;
            var force = hold >= 0
                ? MathF.Max(0, k_s * (x - w.Travel * (0.7f - 0.6f * hold)) + load * g + c_d * rate) // held at a ride height (it carries the weight it is built for)
                : MathF.Max(0, k_s * x + c_d * rate);
            var at = origin - up * hit.Distance; // (the contact under the wheel)
            var r = at - com;
            // Bottomed out: a hard stop on top of the spring (whatever is left of the step, at once).
            var normalImpulse = force * dt;
            if (len - hit.Distance > w.Travel)
            {
                var vn = Vector3.Dot(c.PointVel(at), up);
                if (vn < 0)
                    normalImpulse += -vn / InvMassAt(c, invI, r, up);
            }
            ApplyImpulse(c, invI, r, up * normalImpulse);
            // Tyre: forward along the ground across the axle, side along the axle.
            var n = hit.Normal;
            var axle = Vector3.TransformNormal(axleBody, rot); // (a wheel on a steering pivot: its axle turns with the pivot)
            var fwd = Vector3.Cross(axle, n);
            if (fwd.LengthSquared() < 1e-6f)
                continue;
            fwd = Vector3.Normalize(fwd);
            if (Vector3.Dot(fwd, fwdBody) < 0)
                fwd = -fwd;
            var side = Vector3.Normalize(Vector3.Cross(n, fwd));
            var vp = c.PointVel(at);
            var maxFriction = normalImpulse * w.Grip * (hit.Kind == ShapeKind.Terrain ? DirtGrip * w.Offroad : 1f); // (terrain is dirt: looser than hard ground; off-road tyres grip it, slicks don't)
            w.LastGrip = maxFriction;
            var vSide = Vector3.Dot(vp, side);
            var shareOf = 1f / MathF.Max(1, touching); // each wheel takes its share of stopping the body (not all of it)
            var jSide = -vSide / InvMassAt(c, invI, r, side) * 0.9f * shareOf * w.Side;
            var vFwd = Vector3.Dot(vp, fwd);
            var jFwd = 0f;
            if (driving && w.Share > 0)
            {
                // Skid steering (no steered wheels): each side's throttle is the throttle plus or minus the steering.
                var dir = c.SkidSteer ? Math.Clamp(throttle + steer * w.SideSign, -1, 1) : MathF.Sign(throttle);
                var amount = !c.SkidSteer ? 1f : want > 0 ? MathF.Abs(dir) / want : 0f; // (the engine's torque already follows the throttle)
                var jDrive = amount * ConstructDrive.WheelTorque(c, k) / w.Radius * dt;
                // An open differential drives each wheel no harder than the one that grips least (a limited-slip one half as much again).
                var kind = w.Group < c.GroupCount ? ConstructDrive.GroupKind(c, w.Group) : DiffKind.Locked;
                if (kind != DiffKind.Locked && float.IsFinite(c.GroupGrip[w.Group]))
                    jDrive = MathF.Min(jDrive, c.GroupGrip[w.Group] * (kind == DiffKind.Limited ? 1.5f : 1f));
                jFwd += MathF.Sign(dir) * jDrive;
            }
            var stop = -vFwd / InvMassAt(c, invI, r, fwd) * shareOf;
            if (brake || ((!driving || want == 0) && MathF.Abs(vFwd) < 1.5f && !c.Towed))
                jFwd = stop; // brakes (and a car left in neutral stays put, unless something drives it on a tow)
            else
            {
                jFwd += MathF.Sign(stop) * MathF.Min(MathF.Abs(stop), w.Roll * normalImpulse); // rolling resistance (a share of the load: a knobbly or flat tyre drags more)
                if (partBrake > 0)
                    jFwd = float.Lerp(jFwd, stop, partBrake);
            }
            // Friction circle.
            var total = MathF.Sqrt(jSide * jSide + jFwd * jFwd);
            if (total > maxFriction && total > 0)
            {
                var k2 = maxFriction / total;
                jSide *= k2;
                jFwd *= k2;
            }
            // Grip pushes at a point raised most of the way to the centre of mass: much less roll (an arcade anti-roll bar).
            var rGrip = r + up * (Vector3.Dot(-r, up) * AntiRoll);
            ApplyImpulse(c, invI, rGrip, side * jSide + fwd * jFwd);
            if (!w.Track)
                w.Spin += vFwd / w.Radius * dt;
            if (w.Share > 0 && w.Group < Construct.MaxGroups && ConstructDrive.GroupKind(c, w.Group) == DiffKind.Locked && c.GroupPart[w.Group] < c.PartCount && c.Parts[c.GroupPart[w.Group]].D.Role == PartRole.Differential)
                lockedTouch++;
        }
        // Air drag on the body: what tops out a car in a tall gear before its engine's limiter does.
        if (touching > 0)
            c.Vel -= c.Vel * (c.Vel.Length() * AirDrag * dt);
        // A locked axle fights the steering: its wheels can't turn at different speeds, so they scrub the turn away.
        if (lockedTouch >= 2 && !c.SkidSteer)
        {
            var yaw = Vector3.Dot(c.AngVel, up);
            c.AngVel -= up * (yaw * MathF.Min(1, 2.2f * dt));
        }
    }

    private static float InvMassAt(Construct c, in Matrix4x4 invI, Vector3 r, Vector3 dir)
    {
        var rn = Vector3.Cross(r, dir);
        return c.InvMass + Vector3.Dot(rn, Construct.Mul(invI, rn));
    }

    private static void ApplyImpulse(Construct c, in Matrix4x4 invI, Vector3 r, Vector3 j)
    {
        c.Vel += j * c.InvMass;
        c.AngVel += Construct.Mul(invI, Vector3.Cross(r, j));
    }

    // ---- Contacts -----------------------------------------------------------------------------------------------

    private static void WorldContacts(Construct c, short index, IWorldQuery query, float dt)
    {
        var com = c.WorldCom;
        for (var k = 0; k < c.PointCount; k++)
        {
            if (_contactCount >= MaxContacts - 2)
                return;
            var p = c.ToWorld(c.Points[k]);
            var v = c.Vel + Vector3.Cross(c.AngVel, p - com);
            ProbeWorld(index, p, v, query, dt, 0, 0);
        }
    }

    /// <summary>
    /// A point's two rays against the world: down (the ground under it) and along its motion (walls, kerbs, and for a fast
    /// piece the whole of this tick's travel, so a long arm swung hard can't pass through a thin wall between ticks).
    /// </summary>
    private static bool ProbeWorld(short index, Vector3 p, Vector3 v, IWorldQuery query, float dt, int u0, int un)
    {
        var up = Vector3.UnitY;
        var added = false;
        // Down: the ground under this corner.
        const float lift = 0.4f;
        var reach = lift + MathF.Max(0.05f, -v.Y * dt * 2 + 0.05f);
        LastRays++;
        if (query.Raycast(p + up * lift, -up, reach, QueryFilter.All, out var hit))
            added |= Add(index, -1, p, hit.Normal, hit.Distance - lift, 0.7f, u0, un);
        // Along the motion: walls and kerbs.
        var speed = v.Length();
        if (speed * dt > 0.002f)
        {
            var dir = v / speed;
            if (dir.Y > -0.7f && (dir.Y < 0.5f || un > 0)) // (straight down is the first ray's job; a rising hull point never meets a wall that matters, a rising arm may)
                added |= ProbeMotion(index, p, v, query, dt, u0, un);
        }
        return added;
    }

    /// <summary>A point's ray along its motion (from a little behind it to this tick's travel and a bit): a speculative contact with what it would reach.</summary>
    private static bool ProbeMotion(short index, Vector3 p, Vector3 v, IWorldQuery query, float dt, int u0, int un)
    {
        var speed = v.Length();
        if (speed * dt <= 0.002f)
            return false;
        var dir = v / speed;
        const float back = 0.25f;
        LastRays++;
        return query.Raycast(p - dir * back, dir, back + speed * dt + 0.05f, QueryFilter.All, out var hit) && hit.Distance > 1e-3f && Vector3.Dot(hit.Normal, dir) < -0.1f // (a ray starting inside something tells nothing)
               && Add(index, -1, p, hit.Normal, (hit.Distance - back) * MathF.Max(0.1f, -Vector3.Dot(hit.Normal, dir)), 0.6f, u0, un);
    }

    /// <summary>
    /// The long edges of the parts beyond the joints against the world: each edge is cast where it is now and where it
    /// will be through this tick (a few steps when it moves far), and where one meets something the point of the edge
    /// there gets a ray along its motion like any other: a post or a kerb between two of a part's points still stops it.
    /// </summary>
    private static void SegmentEdgeContacts(Construct c, short index, IWorldQuery query, float dt)
    {
        var com = c.WorldCom;
        var baseSeg = index * (Construct.MaxJoints + 1);
        for (var k = 0; k < c.SegEdgeCount; k++)
        {
            if (_contactCount >= MaxContacts - 1)
                return;
            int seg = c.SegEdgeSeg[k];
            if (seg > c.JointCount)
                continue;
            var sq = _segQ[baseSeg + seg];
            var st = _segT[baseSeg + seg];
            var a = st + Vector3.Transform(c.SegEdgeA[k], sq);
            var b = st + Vector3.Transform(c.SegEdgeB[k], sq);
            if (Chain(c, index, seg, a, out var va) < 0 || Chain(c, index, seg, b, out var vb) < 0)
                return;
            if (_dyn[index])
            {
                va += c.Vel + Vector3.Cross(c.AngVel, a - com);
                vb += c.Vel + Vector3.Cross(c.AngVel, b - com);
            }
            var sweep = MathF.Max(va.Length(), vb.Length()) * dt;
            if (sweep < 0.002f)
                continue; // (still: its points hold it)
            var steps = Math.Clamp((int)MathF.Ceiling(sweep / 0.15f), 1, 4);
            var f = -1f;
            for (var s = 0; s <= steps && f < 0; s++)
            {
                var along = dt * s / steps;
                var a1 = a + va * along;
                var ab = b + vb * along - a1;
                var len = ab.Length();
                if (len < 1e-3f)
                    continue;
                LastRays++;
                if (!query.Raycast(a1, ab / len, len, QueryFilter.All, out var hit))
                    continue;
                // Where the edge comes out again (cast back from its far end): the middle of the piece in the way is what it meets.
                var enter = hit.Distance / len;
                var exit = 1f;
                LastRays++;
                if (query.Raycast(a1 + ab, -ab / len, len - hit.Distance, QueryFilter.All, out var back))
                    exit = 1 - back.Distance / len;
                f = (enter + MathF.Max(enter, exit)) * 0.5f;
            }
            if (f < 0 || f > 1)
                continue;
            var p = a + (b - a) * f;
            var un = Chain(c, index, seg, p, out var v);
            if (un < 0)
                return;
            if (_dyn[index])
                v += c.Vel + Vector3.Cross(c.AngVel, p - com);
            if (ProbeMotion(index, p, v, query, dt, _chainCount, un))
                _chainCount += un;
        }
    }

    private static bool Add(short a, short b, Vector3 p, Vector3 n, float gap, float friction, int ua = 0, int na = 0, int ub = 0, int nb = 0)
    {
        if (_contactCount >= MaxContacts)
            return false;
        ref var ct = ref _contacts[_contactCount++];
        ct = default;
        ct.A = a;
        ct.B = b;
        ct.Point = p;
        ct.Normal = n;
        ct.Gap = gap;
        ct.Friction = friction;
        ct.UA = ua;
        ct.NA = (byte)na;
        ct.UB = ub;
        ct.NB = (byte)nb;
        if (na + nb > 0)
            LastJointContacts++;
        return true;
    }

    /// <summary>
    /// Writes the joint chain of a point on segment <paramref name="seg"/> of body <paramref name="index"/> into the chain
    /// arrays (not yet kept: <see cref="_chainCount"/> moves on only when a contact uses it), and the velocity the joints may
    /// give the point this tick (for the probes). Returns how many joints, -1 when full.
    /// </summary>
    private static int Chain(Construct c, int index, int seg, Vector3 p, out Vector3 vel)
    {
        vel = Vector3.Zero;
        var n = 0;
        for (var guard = 0; seg > 0 && seg <= c.JointCount && guard <= Construct.MaxJoints; guard++)
        {
            if (_chainCount + n >= MaxChain)
                return -1;
            var j = seg - 1;
            if (c.Joints[j].Freeform)
            {
                seg = c.Joints[j].ParentSeg; // (a freeform weld: no freedom, rigid with its parent)
                continue;
            }
            var slot = index * Construct.MaxJoints + j;
            var u = c.Joints[j].Slides ? _jAxis[slot] : Vector3.Cross(_jAxis[slot], p - _jPivot[slot]);
            _chainU[_chainCount + n] = u;
            _chainSlot[_chainCount + n] = (short)slot;
            // (the speed it may reach this tick: a motor can get to what it wants, so a stalled arm still looks ahead)
            var jv = c.Joints[j].Vel;
            vel += u * (_jMotorMax[slot] > 0 && MathF.Abs(_jWant[slot]) > MathF.Abs(jv) ? _jWant[slot] : jv);
            n++;
            seg = c.Joints[j].ParentSeg;
        }
        return n;
    }

    /// <summary>The parts beyond the joints against the world: each segment's points, moving with the body and every joint up their chain.</summary>
    private static void SegmentWorldContacts(Construct c, short index, IWorldQuery query, float dt)
    {
        var com = c.WorldCom;
        var baseSeg = index * (Construct.MaxJoints + 1);
        for (var k = 0; k < c.SegPointCount; k++)
        {
            if (_contactCount >= MaxContacts - 2)
                return;
            int seg = c.SegPointSeg[k];
            if (seg > c.JointCount)
                continue;
            var p = _segT[baseSeg + seg] + Vector3.Transform(c.SegPoints[k], _segQ[baseSeg + seg]);
            var un = Chain(c, index, seg, p, out var v);
            if (un < 0)
                return;
            if (_dyn[index])
                v += c.Vel + Vector3.Cross(c.AngVel, p - com);
            if (ProbeWorld(index, p, v, query, dt, _chainCount, un))
                _chainCount += un;
        }
    }

    /// <summary>Hull points (and the points beyond joints) of each moving body against the part boxes of every body near it.</summary>
    private static void BodyContacts(ConstructWorld world, int n, float dt)
    {
        var bodies = world.Bodies;
        for (var i = 0; i < n; i++)
        {
            if (!_dyn[i] && !_jact[i])
                continue;
            var a = bodies[i];
            for (var j = 0; j < n; j++)
            {
                if (i == j)
                    continue;
                var b = bodies[j];
                if (a.CarriedBy == b.Id || b.CarriedBy == a.Id || a.CarriedBy != 0 && a.CarriedBy == b.CarriedBy)
                    continue; // (a carrier and what its frozen cargo volumes carry don't collide)
                var reach = a.Radius + b.Radius;
                if (Vector3.DistanceSquared(a.WorldCom, b.WorldCom) > reach * reach)
                    continue;
                if (_dyn[i] && b.Asleep && !b.Anchored && (a.Vel.LengthSquared() >= StillSpeed2 || a.AngVel.LengthSquared() >= StillSpeed2))
                    b.Wake(); // (moves from the next tick; a body settling as still as sleep asks doesn't keep waking its neighbours)
                Exclusion((short)i, (short)j);
                if (_dyn[i])
                    PointsAgainst(a, (short)i, b, (short)j);
                if (_jact[i])
                {
                    SegPointsAgainst(a, (short)i, b, (short)j, dt);
                    if (!_dyn[j])
                        PointsIntoSegments(a, (short)i, b, (short)j); // (a moving b's own points do this the other way round)
                }
            }
        }
    }

    /// <summary>Is a point at world <paramref name="p"/> inside one of <paramref name="b"/>'s part boxes: the shallowest way out, and the part's segment.</summary>
    private static bool Inside(Construct b, Vector3 p, out Vector3 normal, out float pen, out int seg)
    {
        pen = float.MaxValue;
        normal = Vector3.Zero;
        seg = 0;
        for (var pi = 0; pi < b.PartCount; pi++)
        {
            ref var part = ref b.Parts[pi];
            b.WorldFrame(part.Seg, out var q, out var t);
            var local = Vector3.Transform(p - t, Quaternion.Conjugate(q));
            var boxes = b.SolidCount(pi);
            for (var bi = 0; bi < boxes; bi++)
            {
                b.SolidBox(pi, bi, out var centre, out var half);
                var d = local - centre;
                var o = half + new Vector3(0.02f) - Vector3.Abs(d);
                if (o.X <= 0 || o.Y <= 0 || o.Z <= 0)
                    continue;
                // The shallowest way out.
                Vector3 nLocal;
                float pn;
                if (o.X < o.Y && o.X < o.Z)
                {
                    pn = o.X;
                    nLocal = new Vector3(MathF.Sign(d.X) is var s && s == 0 ? 1 : s, 0, 0);
                }
                else if (o.Y < o.Z)
                {
                    pn = o.Y;
                    nLocal = new Vector3(0, MathF.Sign(d.Y) is var s && s == 0 ? 1 : s, 0);
                }
                else
                {
                    pn = o.Z;
                    nLocal = new Vector3(0, 0, MathF.Sign(d.Z) is var s && s == 0 ? 1 : s);
                }
                if (pn < pen)
                {
                    pen = pn;
                    normal = Vector3.Transform(nLocal, q);
                    seg = part.Seg;
                }
            }
        }
        return pen < float.MaxValue;
    }

    /// <summary>The nearest of <paramref name="b"/>'s part boxes along a ray (its distance, entry normal and segment).</summary>
    private static bool RayBody(Construct b, Vector3 origin, Vector3 dir, float maxDist, out float dist, out Vector3 normal, out int seg)
    {
        dist = maxDist;
        normal = Vector3.Zero;
        seg = 0;
        var found = false;
        for (var pi = 0; pi < b.PartCount; pi++)
        {
            ref var part = ref b.Parts[pi];
            b.WorldFrame(part.Seg, out var q, out var t);
            var iq = Quaternion.Conjugate(q);
            var lo = Vector3.Transform(origin - t, iq);
            var ld = Vector3.Transform(dir, iq);
            var boxes = b.SolidCount(pi);
            for (var bi = 0; bi < boxes; bi++)
            {
                b.SolidBox(pi, bi, out var centre, out var half);
                if (Geometry.RayAabb(centre - half, centre + half, lo, ld, dist) is { } d && d < dist)
                {
                    dist = d;
                    found = true;
                    seg = part.Seg;
                    normal = Vector3.Transform(Geometry.BoxEntryNormal(centre - half, centre + half, lo, ld, d), q);
                }
            }
        }
        return found;
    }

    /// <summary>The other side of a contact on <paramref name="b"/>: its index (-1: it can't move there) and its joint chain at the point.</summary>
    private static short OtherSide(Construct b, short ib, int seg, Vector3 p, out int u0, out int un)
    {
        u0 = _chainCount;
        un = 0;
        if (seg > 0 && _jact[ib])
        {
            un = Chain(b, ib, seg, p, out _);
            if (un < 0)
                un = 0;
        }
        else if (seg > 0 && b.Asleep)
            b.Wake(); // something pushes a sleeping joint: it answers from the next tick
        return _dyn[ib] || un > 0 ? ib : (short)-1;
    }

    private static void PointsAgainst(Construct a, short ia, Construct b, short ib)
    {
        var bc = b.WorldCom;
        var r2 = b.Radius * b.Radius;
        for (var k = 0; k < a.PointCount; k++)
        {
            if (_contactCount >= MaxContacts - 1)
                return;
            var p = a.ToWorld(a.Points[k]);
            if (Vector3.DistanceSquared(p, bc) > r2 || NearHitch(p))
                continue;
            if (!Inside(b, p, out var normal, out var pen, out var seg))
                continue;
            var other = OtherSide(b, ib, seg, p, out var u0, out var un);
            if (Add(ia, other, p, normal, -(pen - 0.02f), 0.6f, 0, 0, u0, other >= 0 ? un : 0) && other >= 0)
                _chainCount += un;
        }
    }

    /// <summary>
    /// The hull points of a body that doesn't move itself (anchored, or asleep) against the parts beyond
    /// <paramref name="a"/>'s joints: a thin wall's corners inside a swinging door stop the door.
    /// </summary>
    private static void PointsIntoSegments(Construct a, short ia, Construct b, short ib)
    {
        var ac = a.WorldCom;
        var r2 = a.Radius * a.Radius;
        for (var k = 0; k < b.PointCount; k++)
        {
            if (_contactCount >= MaxContacts - 1)
                return;
            var p = b.ToWorld(b.Points[k]);
            if (Vector3.DistanceSquared(p, ac) > r2 || NearHitch(p))
                continue;
            if (!Inside(a, p, out var normal, out var pen, out var seg) || seg == 0)
                continue;
            var un = Chain(a, ia, seg, p, out _);
            if (un <= 0)
                continue;
            // (the normal points out of a's box towards the point: from b into a is the other way)
            if (Add(ia, -1, p, -normal, -(pen - 0.02f), 0.6f, _chainCount, un))
            {
                _chainCount += un;
                if (b.Asleep && !b.Anchored)
                    b.Wake();
            }
        }
    }

    /// <summary>
    /// The points beyond <paramref name="a"/>'s joints against <paramref name="b"/>'s part boxes: overlaps, and for a point
    /// moving fast a ray along this tick's travel (a speculative contact: a swung arm stops at a thin panel, not past it).
    /// </summary>
    private static void SegPointsAgainst(Construct a, short ia, Construct b, short ib, float dt)
    {
        var bc = b.WorldCom;
        var com = a.WorldCom;
        var baseSeg = ia * (Construct.MaxJoints + 1);
        for (var k = 0; k < a.SegPointCount; k++)
        {
            if (_contactCount >= MaxContacts - 1)
                return;
            int seg = a.SegPointSeg[k];
            if (seg > a.JointCount)
                continue;
            var p = _segT[baseSeg + seg] + Vector3.Transform(a.SegPoints[k], _segQ[baseSeg + seg]);
            if (NearHitch(p))
                continue;
            var v2 = 0f;
            var near = Vector3.DistanceSquared(p, bc) <= b.Radius * b.Radius;
            var ua = _chainCount;
            var na = Chain(a, ia, seg, p, out var v);
            if (na < 0)
                return;
            if (_dyn[ia])
                v += a.Vel + Vector3.Cross(a.AngVel, p - com);
            v2 = v.LengthSquared();
            var step = MathF.Sqrt(v2) * dt;
            if (!near && step < 0.05f)
                continue;
            _chainCount += na; // (kept for now: given back below when nothing touches)
            var used = false;
            if (near && Inside(b, p, out var normal, out var pen, out var hitSeg))
            {
                var other = OtherSide(b, ib, hitSeg, p, out var u0, out var un);
                if (Add(ia, other, p, normal, -(pen - 0.02f), 0.6f, ua, na, u0, other >= 0 ? un : 0))
                {
                    used = true;
                    if (other >= 0)
                        _chainCount += un;
                }
            }
            else if (step >= 0.05f)
            {
                const float back = 0.25f;
                var dir = v / MathF.Sqrt(v2);
                if (RayBody(b, p - dir * back, dir, back + step + 0.05f, out var dist, out var n2, out var raySeg) && dist > 1e-3f && Vector3.Dot(n2, dir) < -0.1f)
                {
                    var at = p + dir * (dist - back);
                    var other = OtherSide(b, ib, raySeg, at, out var u0, out var un);
                    if (Add(ia, other, p, n2, (dist - back) * MathF.Max(0.1f, -Vector3.Dot(n2, dir)), 0.6f, ua, na, u0, other >= 0 ? un : 0))
                    {
                        used = true;
                        if (other >= 0)
                            _chainCount += un;
                    }
                }
            }
            if (!used && _chainCount == ua + na)
                _chainCount = ua;
        }
    }

    // ---- The solver: contacts, motors and limits on generalised velocities ------------------------------------------

    /// <summary>How easily a side moves along <paramref name="d"/> at its contact: the body's share, and each joint's up the chain.</summary>
    private static float EffSide(int body, Vector3 r, Vector3 d, int u0, int un)
    {
        var e = 0f;
        if (_dyn[body])
        {
            var c = _bodies[body];
            var rn = Vector3.Cross(r, d);
            e = c.InvMass + Vector3.Dot(rn, Construct.Mul(_invI[body], rn));
        }
        for (var k = 0; k < un; k++)
        {
            var ud = Vector3.Dot(_chainU[u0 + k], d);
            e += _jInvI[_chainSlot[u0 + k]] * ud * ud;
        }
        return e;
    }

    private static Vector3 VelSide(int body, Vector3 r, int u0, int un)
    {
        var v = Vector3.Zero;
        var c = _bodies[body];
        if (_dyn[body])
            v = c.Vel + Vector3.Cross(c.AngVel, r);
        for (var k = 0; k < un; k++)
        {
            int slot = _chainSlot[u0 + k];
            v += _chainU[u0 + k] * _bodies[slot / Construct.MaxJoints].Joints[slot % Construct.MaxJoints].Vel;
        }
        return v;
    }

    private static void PushSide(int body, Vector3 r, int u0, int un, Vector3 j)
    {
        var c = _bodies[body];
        if (_dyn[body])
        {
            c.Vel += j * c.InvMass;
            c.AngVel += Construct.Mul(_invI[body], Vector3.Cross(r, j));
        }
        for (var k = 0; k < un; k++)
        {
            int slot = _chainSlot[u0 + k];
            _bodies[slot / Construct.MaxJoints].Joints[slot % Construct.MaxJoints].Vel += _jInvI[slot] * Vector3.Dot(_chainU[u0 + k], j);
        }
    }

    private static float Eff2(in Contact ct, Vector3 d) =>
        EffSide(ct.A, ct.RA, d, ct.UA, ct.NA) + (ct.B >= 0 ? EffSide(ct.B, ct.RB, d, ct.UB, ct.NB) : 0);

    private static Vector3 Rel(in Contact ct) =>
        VelSide(ct.A, ct.RA, ct.UA, ct.NA) - (ct.B >= 0 ? VelSide(ct.B, ct.RB, ct.UB, ct.NB) : Vector3.Zero);

    private static void Push2(in Contact ct, Vector3 j)
    {
        PushSide(ct.A, ct.RA, ct.UA, ct.NA, j);
        if (ct.B >= 0)
            PushSide(ct.B, ct.RB, ct.UB, ct.NB, -j);
    }

    private static void Solve(float dt)
    {
        var bodies = _bodies;
        // Prepare.
        for (var k = 0; k < _contactCount; k++)
        {
            ref var ct = ref _contacts[k];
            ct.RA = ct.Point - bodies[ct.A].WorldCom;
            ct.RB = ct.B >= 0 ? (ct.TwoPoints ? ct.PointB : ct.Point) - bodies[ct.B].WorldCom : Vector3.Zero;
            var en = Eff2(ct, ct.Normal);
            ct.MassN = en > 1e-9f ? 1f / MathF.Max(1e-6f, en) : 0; // (0: nothing on either side can move there)
            var t1 = MathF.Abs(ct.Normal.X) < 0.9f ? Vector3.Cross(ct.Normal, Vector3.UnitX) : Vector3.Cross(ct.Normal, Vector3.UnitZ);
            t1 = Vector3.Normalize(t1);
            var t2 = Vector3.Cross(ct.Normal, t1);
            ct.T1 = t1;
            ct.T2 = t2;
            var e1 = Eff2(ct, t1);
            var e2 = Eff2(ct, t2);
            ct.MassT1 = e1 > 1e-9f ? 1f / MathF.Max(1e-6f, e1) : 0;
            ct.MassT2 = e2 > 1e-9f ? 1f / MathF.Max(1e-6f, e2) : 0;
            // Speculative: approaching is fine up to the gap; overlap is pushed out a share per tick. A free pivot: its error, a share per tick.
            ct.Bias = ct.Bilateral ? Math.Clamp(PivotBeta * ct.Gap / dt, -PivotMaxFix, PivotMaxFix)
                : ct.Gap > 0 ? ct.Gap / dt : MathF.Max(-2.5f, Beta * (ct.Gap + Slop) / dt);
        }
        for (var it = 0; it < Iterations + JointIterations; it++)
        {
            // The joints first (motors, limits), then the contacts: what can't pass through has the last word. The last
            // passes are only the joints and the contacts on jointed constructs (a chain of actuators converges rigid).
            JointRows(bodies);
            ContactRows(it >= Iterations);
        }
    }

    private static void JointRows(IList<Construct> bodies)
    {
        for (var a = 0; a < _activeCount; a++)
        {
            int slot = _active[a];
            var inv = _jInvI[slot];
            if (inv <= 0)
                continue;
            ref var jt = ref bodies[slot / Construct.MaxJoints].Joints[slot % Construct.MaxJoints];
            var max = _jMotorMax[slot];
            var react = 0f; // (milestone 3: what the motor and the stops push the joint with, the body takes back)
            if (max > 0)
            {
                var old = _jP[slot];
                _jP[slot] = Math.Clamp(old + (_jWant[slot] - jt.Vel) / inv, -max, max);
                jt.Vel += (_jP[slot] - old) * inv;
                react += _jP[slot] - old;
            }
            if (float.IsFinite(_jLoB[slot]))
            {
                var old = _jLo[slot];
                _jLo[slot] = MathF.Max(0, old + (_jLoB[slot] - jt.Vel) / inv);
                jt.Vel += (_jLo[slot] - old) * inv;
                react += _jLo[slot] - old;
            }
            if (float.IsFinite(_jHiB[slot]))
            {
                var old = _jHi[slot];
                _jHi[slot] = MathF.Min(0, old + (_jHiB[slot] - jt.Vel) / inv);
                jt.Vel += (_jHi[slot] - old) * inv;
                react += _jHi[slot] - old;
            }
            if (react != 0 && JointReactions)
                React(slot, react);
        }
    }

    private static void ContactRows(bool jointedOnly)
    {
        for (var k = 0; k < _contactCount; k++)
        {
            ref var ct = ref _contacts[k];
            if (ct.MassN <= 0 || jointedOnly && ct.NA + ct.NB == 0 && !_jact[ct.A] && (ct.B < 0 || !_jact[ct.B]))
                continue;
            var rel = Rel(ct);
            var vn = Vector3.Dot(rel, ct.Normal);
            var dj = -(vn + ct.Bias) * ct.MassN;
            if (ct.Bilateral)
            {
                ct.Jn += dj; // (a free pivot holds either way)
                Push2(ct, ct.Normal * dj);
                continue;
            }
            var old = ct.Jn;
            ct.Jn = MathF.Max(0, old + dj);
            dj = ct.Jn - old;
            if (dj != 0)
                Push2(ct, ct.Normal * dj);
            if (ct.Jn <= 0)
                continue;
            // Friction (two tangents, inside the cone).
            rel = Rel(ct);
            var max = ct.Friction * ct.Jn;
            var d1 = -Vector3.Dot(rel, ct.T1) * ct.MassT1;
            var o1 = ct.Jt1;
            ct.Jt1 = Math.Clamp(o1 + d1, -max, max);
            var d2 = -Vector3.Dot(rel, ct.T2) * ct.MassT2;
            var o2 = ct.Jt2;
            ct.Jt2 = Math.Clamp(o2 + d2, -max, max);
            var jt = ct.T1 * (ct.Jt1 - o1) + ct.T2 * (ct.Jt2 - o2);
            if (jt != Vector3.Zero)
                Push2(ct, jt);
        }
    }

    // ---- Couplings between constructs (milestone 3; ConstructLinks.cs) ----------------------------------------------

    /// <summary>The coupled pairs this tick and their hitch points (their parts near them don't collide with each other).</summary>
    private static void CouplingExclusions(ConstructWorld world, int n)
    {
        _exCount = 0;
        _exOn = false;
        for (var k = 0; k < world.CouplingCount; k++)
        {
            ref var cp = ref world.Couplings[k];
            if (world.Get(cp.BodyA) is not { } a || world.Get(cp.BodyB) is not { } b || a.SimIndex >= n || b.SimIndex >= n
                || cp.PartA >= a.PartCount || cp.PartB >= b.PartCount || !ReferenceEquals(_bodies[a.SimIndex], a) || !ReferenceEquals(_bodies[b.SimIndex], b))
                continue;
            _exA[_exCount] = (short)a.SimIndex;
            _exB[_exCount] = (short)b.SimIndex;
            _exPa[_exCount] = ConstructWorld.HitchPoint(a, cp.PartA);
            _exPb[_exCount] = ConstructWorld.HitchPoint(b, cp.PartB);
            _exCount++;
        }
    }

    private static void Exclusion(short i, short j)
    {
        _exOn = false;
        for (var k = 0; k < _exCount; k++)
            if (_exA[k] == i && _exB[k] == j || _exA[k] == j && _exB[k] == i)
            {
                _exOn = true;
                _exP0 = _exPa[k];
                _exP1 = _exPb[k];
                return;
            }
    }

    private static bool NearHitch(Vector3 p) =>
        _exOn && (Vector3.DistanceSquared(p, _exP0) < HitchClear * HitchClear || Vector3.DistanceSquared(p, _exP1) < HitchClear * HitchClear);

    /// <summary>
    /// Each coupling's rows in the solve: a tow ball holds the two hitch points together (three rows that push either way:
    /// a free pivot), a chain keeps them within its length (one row that only pulls). Each side moves its body (when it is
    /// free and awake) and every joint up its hitch's chain, like a contact.
    /// </summary>
    private static void CouplingRows(ConstructWorld world, int n, float dt)
    {
        _exOn = false;
        for (var k = 0; k < world.CouplingCount; k++)
        {
            ref var cp = ref world.Couplings[k];
            if (world.Get(cp.BodyA) is not { } a || world.Get(cp.BodyB) is not { } b || a.SimIndex >= n || b.SimIndex >= n
                || cp.PartA >= a.PartCount || cp.PartB >= b.PartCount || !ReferenceEquals(_bodies[a.SimIndex], a) || !ReferenceEquals(_bodies[b.SimIndex], b))
                continue;
            var pa = ConstructWorld.HitchPoint(a, cp.PartA);
            var pb = ConstructWorld.HitchPoint(b, cp.PartB);
            int sa = a.Parts[cp.PartA].Seg, sb = b.Parts[cp.PartB].Seg;
            short ia = (short)a.SimIndex, ib = (short)b.SimIndex;
            var movesA = _dyn[ia] || sa > 0 && _jact[ia];
            var movesB = _dyn[ib] || sb > 0 && _jact[ib];
            if (!movesA && !movesB)
                continue;
            if (!movesA)
            {
                (a, b, pa, pb, sa, sb, ia, ib) = (b, a, pb, pa, sb, sa, ib, ia);
                movesB = false;
            }
            if (cp.Length > 0)
            {
                var d = pb - pa;
                var dist = d.Length();
                if (dist < cp.Length - 0.5f || dist < 1e-4f)
                    continue; // (slack: nothing to hold)
                Row(a, ia, sa, pa, b, ib, sb, pb, movesB, d / dist, cp.Length - dist, false);
            }
            else
            {
                var e = pa - pb;
                Row(a, ia, sa, pa, b, ib, sb, pb, movesB, Vector3.UnitX, e.X, true);
                Row(a, ia, sa, pa, b, ib, sb, pb, movesB, Vector3.UnitY, e.Y, true);
                Row(a, ia, sa, pa, b, ib, sb, pb, movesB, Vector3.UnitZ, e.Z, true);
            }
        }
        _ = dt;
    }

    private static void Row(Construct a, short ia, int sa, Vector3 pa, Construct b, short ib, int sb, Vector3 pb, bool movesB, Vector3 normal, float gap, bool bilateral)
    {
        if (_contactCount >= MaxContacts)
            return;
        var ua = _chainCount;
        var na = sa > 0 && _jact[ia] ? Chain(a, ia, sa, pa, out _) : 0;
        if (na < 0)
            return;
        _chainCount += na;
        var ub = _chainCount;
        var nb = movesB && sb > 0 && _jact[ib] ? Chain(b, ib, sb, pb, out _) : 0;
        if (nb < 0)
            nb = 0;
        _chainCount += nb;
        if (!Add(ia, movesB ? ib : (short)-1, pa, normal, gap, 0f, ua, na, ub, nb))
            return;
        ref var ct = ref _contacts[_contactCount - 1];
        ct.Bilateral = bilateral;
        ct.TwoPoints = true;
        ct.PointB = pb;
        if (na + nb > 0)
            LastJointContacts--; // (not a contact on a joint's parts)
    }

    /// <summary>A joint's push (from its motor or its stops) taken back by its body: a turn about its axis, or a piston's push along it at its pivot.</summary>
    private static void React(int slot, float impulse)
    {
        var body = slot / Construct.MaxJoints;
        if (!_dyn[body])
            return;
        var c = _bodies[body];
        var axis = _jAxis[slot];
        ref var jt = ref c.Joints[slot % Construct.MaxJoints];
        if (jt.Type == JointType.Steering)
            return; // (a steering pivot is the driver's hands on a rack: it doesn't swing the car about)
        if (jt.Slides)
        {
            var j = -axis * impulse;
            c.Vel += j * c.InvMass;
            c.AngVel += Construct.Mul(_invIB[body], Vector3.Cross(_jPivot[slot] - c.WorldCom, j));
        }
        else
            c.AngVel -= Construct.Mul(_invIB[body], axis * impulse); // (the base turns back: what swings on the joint doesn't help hold it)
    }

    // ---- Integration, joints, sleep -------------------------------------------------------------------------------

    private static void Integrate(Construct c, float dt)
    {
        // Gentle damping (air, the axles' losses) keeps a still body from creeping.
        c.Vel *= 0.999f;
        c.AngVel *= 0.985f;
        var v2 = c.Vel.LengthSquared();
        if (v2 > MaxSpeed * MaxSpeed)
            c.Vel *= MaxSpeed / MathF.Sqrt(v2);
        var w2 = c.AngVel.LengthSquared();
        if (w2 > MaxSpin * MaxSpin)
            c.AngVel *= MaxSpin / MathF.Sqrt(w2);
        var com = c.WorldCom + c.Vel * dt;
        var w = c.AngVel;
        var spin = new Quaternion(w.X, w.Y, w.Z, 0) * c.Rot;
        var rot = new Quaternion(c.Rot.X + 0.5f * dt * spin.X, c.Rot.Y + 0.5f * dt * spin.Y, c.Rot.Z + 0.5f * dt * spin.Z, c.Rot.W + 0.5f * dt * spin.W);
        c.Rot = Quaternion.Normalize(rot);
        c.Pos = com - Vector3.Transform(c.Com, c.Rot);
        if (!float.IsFinite(c.Pos.X + c.Pos.Y + c.Pos.Z))
        {
            // Never let a bad step poison the world: put it back still.
            c.Pos = c.PrevPos;
            c.Rot = c.PrevRot;
            c.Vel = c.AngVel = Vector3.Zero;
        }
    }

    /// <summary>
    /// A joint's load this tick: gravity on what it carries (its segment and everything hanging on it, and the riders),
    /// about its axis (N m) or along it (N), and the inertia it moves with; its axis and pivot in the world.
    /// </summary>
    private static void JointLoad(Construct c, int j, int riders, out float load, out float inertia, out Vector3 axis, out Vector3 pivot)
    {
        ref var jt = ref c.Joints[j];
        var seg = j + 1;
        c.WorldFrame(jt.ParentSeg, out var pq, out var pt);
        axis = Vector3.Transform(jt.Axis, pq);
        pivot = pt + Vector3.Transform(jt.Pivot, pq);
        var mass = c.SegMass[seg] + riders * Construct.RiderMass;
        c.SegPose(seg, out var sq, out var st);
        var com = c.ToWorld(Vector3.Transform(c.SegCom[seg], sq) + st);
        var down = new Vector3(0, -GravityAccel * mass, 0);
        if (jt.Slides)
        {
            load = Vector3.Dot(down, axis);
            inertia = MathF.Max(mass, 1f);
        }
        else
        {
            var r = com - pivot;
            load = Vector3.Dot(Vector3.Cross(r, down), axis);
            var across = r - axis * Vector3.Dot(r, axis);
            inertia = MathF.Max(mass * (across.LengthSquared() + 0.05f), 0.5f);
        }
    }

    /// <summary>What a moving joint's motor wants this tick: a speed (turntable), or a speed towards the target its input asks for (the rest).</summary>
    private static float MotorWant(Construct c, in CJoint jt)
    {
        if (!ConstructPower.On(c, jt.Hinge))
            return 0; // (milestone 3: an electrical construct's motor with no power)
        if (jt.Aimed)
        {
            // A gunner (or a sentry) points it (milestone 3, ConstructWeapons.cs): towards the error its gun is off by.
            if (jt.Type == JointType.Turntable)
                return Math.Clamp(jt.AimErr * 6f, -jt.Speed, jt.Speed);
            var aim = Math.Clamp(jt.Angle + jt.AimErr, jt.Min, jt.Max);
            return Math.Clamp((aim - jt.Angle) * 10f, -jt.Speed, jt.Speed);
        }
        if (jt.Type == JointType.Turntable)
            return Math.Clamp(jt.Wired ? jt.Input : 0, -1, 1) * jt.Speed;
        float target;
        var input = jt.Wired ? jt.Input : jt.Open ? 1f : 0f;
        if (jt.Type == JointType.Steering && !jt.Wired)
        {
            // Unwired, a steering pivot follows the driver (c.Steer: right is positive; a turn about +y is to the left).
            var upRest = Vector3.Dot(jt.Axis, Vector3.UnitY) >= 0 ? 1f : -1f;
            target = -c.Steer * upRest;
        }
        else if (jt.Slides)
            target = Math.Clamp(input, 0, 1) * jt.Max;
        else
        {
            input = Math.Clamp(input, -1, 1);
            target = input >= 0 ? input * jt.Max : -input * jt.Min;
        }
        target = Math.Clamp(target, jt.Min, jt.Max);
        return Math.Clamp((target - jt.Angle) * 10f, -jt.Speed, jt.Speed);
    }

    /// <summary>
    /// The joints before the solve. A passive hinge swings, damped, towards its latch (or holds still while its lock is on)
    /// and falls under gravity. A moving joint is driven by a motor: a position joint (motorised hinge, piston, steering
    /// pivot) towards the angle or extension its input asks for, a turntable at the speed it asks for; the motor pushes
    /// with at most its strength (solved with the contacts) against the load (gravity on everything the joint carries,
    /// players standing on it included), so an overloaded lift sinks and a blocked arm stalls. A joint that isn't a number
    /// any more is put back at rest. Also each segment's frame in the world for this tick.
    /// </summary>
    private static void JointPrep(Construct c, int index, float dt)
    {
        for (var j = 0; j < c.JointCount; j++)
        {
            ref var jt = ref c.Joints[j];
            if (!float.IsFinite(jt.Angle) || !float.IsFinite(jt.Vel))
            {
                jt.Angle = 0;
                jt.Vel = 0;
            }
            if (jt.Freeform)
            {
                jt.Vel = jt.Angle = 0; // (a freeform weld: nothing to solve)
                continue;
            }
            var seg = j + 1;
            var riders = System.Numerics.BitOperations.PopCount(c.RiderBits[seg]);
            c.RiderBits[seg] = 0;
            JointLoad(c, j, riders, out var load, out var inertia, out var axis, out var pivot);
            jt.Load = load;
            var slot = index * Construct.MaxJoints + j;
            _jAxis[slot] = axis;
            _jPivot[slot] = pivot;
            _jP[slot] = _jLo[slot] = _jHi[slot] = 0;
            _jMotorMax[slot] = 0;
            _jRating[slot] = jt.Strength;
            _jSide[slot] = 0;
            if (jt.Slides && jt.SideLoad > 0)
            {
                // A piston's side load: gravity across its axis on what it carries (and its riders), against what it takes.
                var w = GravityAccel * (c.SegMass[seg] + riders * Construct.RiderMass);
                var across = MathF.Sqrt(MathF.Max(0, w * w - load * load));
                _jSide[slot] = across / jt.SideLoad;
                _jRating[slot] = jt.Strength / MathF.Max(1, _jSide[slot]); // (pushed sideways past what it takes, it holds less along its axis)
            }
            _jWant[slot] = 0;
            _active[_activeCount++] = (short)slot;
            if (jt.Type == JointType.Passive)
            {
                if (jt.Locked)
                {
                    jt.Vel = 0; // a lock holds it where it is
                    _jInvI[slot] = 0;
                    _jLoB[slot] = _jHiB[slot] = float.NaN;
                    continue;
                }
                var target = jt.Open ? (jt.Max >= -jt.Min ? jt.Max : jt.Min) * 0.95f : 0f;
                jt.Vel += (target - jt.Angle) * 30f * dt;
                jt.Vel *= MathF.Max(0, 1 - 7f * dt);
            }
            else if (ConstructPower.On(c, jt.Hinge))
                MotorPrep(c, ref jt, slot, dt);
            else
            {
                jt.Vel *= MathF.Max(0, 1 - 2f * dt); // (no power: the motor lets go, a little drag in its gears)
                jt.Ref = jt.Angle; // (power back: it starts from where it is)
                jt.RefSet = true;
            }
            jt.Vel += load / inertia * dt; // the load acts whatever the motor does
            _jInvI[slot] = 1f / inertia;
            _jLoB[slot] = float.IsFinite(jt.Min) ? (jt.Min - jt.Angle) / dt : float.NaN;
            _jHiB[slot] = float.IsFinite(jt.Max) ? (jt.Max - jt.Angle) / dt : float.NaN;
        }
        var baseSeg = index * (Construct.MaxJoints + 1);
        for (var s = 0; s <= c.JointCount; s++)
            c.WorldFrame(s, out _segQ[baseSeg + s], out _segT[baseSeg + s]);
    }

    /// <summary>
    /// The strength model (board item dev-sandbox-actuator-sizes-as01; docs/sandbox/CONSTRUCTS.md, "Actuator sizes and
    /// strength"). A moving joint's motor follows a reference: the target its input asks for (a turntable: the speed),
    /// reached at its speed. The motor holds the joint on it with at most its rating while the joint is on it, so up to
    /// its rating the joint is rigid (a velocity constraint solved with the contacts and the other joints, no spring). A
    /// load above the rating pushes the joint off its reference, and the further off it is the more the motor pushes:
    /// rating x (1 + sqrt(behind / give)), so it settles where that meets the load, give x (load / rating - 1)^2 behind
    /// (a little just over the rating, more and more as the overload grows; "give" is how far at twice the rating). It
    /// never asks to move faster than its speed, so it comes back at its speed when the load goes (and doesn't bounce:
    /// what pushes it off is resisted, what it moves back with is its speed). At OverCap times its rating it slips: the
    /// reference is dragged along. A gunner's aim and a steering pivot keep the plain motor (at most their strength).
    /// </summary>
    private static void MotorPrep(Construct c, ref CJoint jt, int slot, float dt)
    {
        if (jt.Aimed || jt.Type == JointType.Steering)
        {
            _jWant[slot] = MotorWant(c, jt);
            _jMotorMax[slot] = jt.Strength * dt;
            jt.Ref = jt.Angle;
            jt.RefSet = true;
            return;
        }
        var wraps = jt.Type == JointType.Turntable && !float.IsFinite(jt.Min);
        if (!jt.RefSet || !float.IsFinite(jt.Ref))
        {
            jt.Ref = jt.Angle;
            jt.RefSet = true;
        }
        float refVel;
        if (jt.Type == JointType.Turntable && jt.Wired && jt.Input != 0)
            refVel = Math.Clamp(jt.Input, -1, 1) * jt.Speed;
        else
        {
            var to = jt.Type == JointType.Turntable ? jt.Ref : PositionTarget(c, jt); // (a turntable with no input holds where it is)
            var d = wraps ? World.WrapAngle(to - jt.Ref) : to - jt.Ref;
            refVel = Math.Clamp(d / dt, -jt.Speed, jt.Speed);
        }
        jt.Ref += refVel * dt;
        if (wraps)
            jt.Ref = World.WrapAngle(jt.Ref);
        else
            jt.Ref = Math.Clamp(jt.Ref, jt.Min, jt.Max);
        var behind = wraps ? World.WrapAngle(jt.Ref - jt.Angle) : jt.Ref - jt.Angle;
        var give = MathF.Max(jt.Give, 1e-3f);
        var lead = give * (OverCap - 1) * (OverCap - 1);
        if (MathF.Abs(behind) > lead)
        {
            // Past OverCap times its rating: it slips (the reference comes along).
            behind = MathF.CopySign(lead, behind);
            jt.Ref = wraps ? World.WrapAngle(jt.Angle + behind) : jt.Angle + behind;
        }
        if (MathF.Abs(behind) > 1e-4f && jt.Vel * behind < 0)
            jt.Vel *= MathF.Max(0, 1 - YieldDamp * dt); // (giving way, damped: it settles on its curve without bouncing)
        _jWant[slot] = Math.Clamp(refVel + behind * RefGain, -jt.Speed, jt.Speed);
        _jMotorMax[slot] = _jRating[slot] * (1 + MathF.Sqrt(MathF.Abs(behind) / give)) * dt;
    }

    /// <summary>Where a position joint's input sends it (a motorised hinge, a piston): inside its limits.</summary>
    private static float PositionTarget(Construct c, in CJoint jt)
    {
        var input = jt.Wired ? jt.Input : jt.Open ? 1f : 0f;
        float target;
        if (jt.Slides)
            target = Math.Clamp(input, 0, 1) * jt.Max;
        else
        {
            input = Math.Clamp(input, -1, 1);
            target = input >= 0 ? input * jt.Max : -input * jt.Min;
        }
        _ = c;
        return Math.Clamp(target, jt.Min, jt.Max);
    }

    /// <summary>After the solve: each joint moves at the speed it was left with, inside its limits.</summary>
    private static void IntegrateJoints(Construct c, int index, float dt)
    {
        for (var j = 0; j < c.JointCount; j++)
        {
            ref var jt = ref c.Joints[j];
            if (jt.Freeform)
                continue;
            var slot = index * Construct.MaxJoints + j;
            if (_jMotorMax[slot] > 0)
            {
                c.MotorWork += MathF.Abs(_jP[slot] / dt * jt.Vel); // (the power drain: torque x speed, ConstructPower.cs)
                // The load it held (the strength model): what the panel's bar and the tint show, and what overload damage counts.
                jt.Force += (MathF.Abs(_jP[slot]) / dt - jt.Force) * ForceSmooth;
                jt.Over = MathF.Max(jt.Force / MathF.Max(1, _jRating[slot]), _jSide[slot]);
                if (jt.Over > 1 && jt.Hinge < c.PartCount && c.Parts[jt.Hinge].D.OverloadDamage > 0)
                    jt.Strain += c.Parts[jt.Hinge].D.OverloadDamage * (jt.Over - 1) * dt;
            }
            else if (jt.Motorised)
            {
                jt.Force *= 1 - ForceSmooth;
                jt.Over = 0;
            }
            if (jt.Type == JointType.Passive && jt.Locked)
            {
                jt.Vel = 0;
                continue;
            }
            var cap = jt.Motorised ? MathF.Max(jt.Speed * 3, 2f) : MaxJointSpin;
            jt.Vel = Math.Clamp(jt.Vel, -cap, cap);
            jt.Angle += jt.Vel * dt;
            if (jt.Type == JointType.Turntable && !float.IsFinite(jt.Min))
                jt.Angle = World.WrapAngle(jt.Angle);
            Limit(ref jt);
        }
    }

    /// <summary>
    /// A sleeping body with joints: does something ask them to move (a motor's input, the driver's steering, more riders
    /// than a motor can hold)? Then it wakes. Doors and hinges are woken by whatever pushes or opens them.
    /// </summary>
    private static void WakeCheck(Construct c)
    {
        var wake = false;
        for (var j = 0; j < c.JointCount && !wake; j++)
        {
            ref var jt = ref c.Joints[j];
            if (!jt.Motorised)
                continue;
            if (MathF.Abs(MotorWant(c, jt)) > 0.05f)
                wake = true;
            else if (c.RiderBits[j + 1] != 0)
            {
                JointLoad(c, j, System.Numerics.BitOperations.PopCount(c.RiderBits[j + 1]), out var load, out _, out _, out _);
                wake = MathF.Abs(load) > jt.Strength;
            }
        }
        if (wake)
        {
            c.Wake();
            return; // (the riders count in this tick's step)
        }
        for (var s = 0; s <= c.JointCount; s++)
            c.RiderBits[s] = 0;
    }

    private static void Limit(ref CJoint jt)
    {
        if (jt.Angle < jt.Min)
        {
            jt.Angle = jt.Min;
            jt.Vel = MathF.Max(0, jt.Vel);
        }
        else if (jt.Angle > jt.Max)
        {
            jt.Angle = jt.Max;
            jt.Vel = MathF.Min(0, jt.Vel);
        }
    }

    private static void Sleep(Construct c)
    {
        var still = c.Anchored || c.Vel.LengthSquared() < StillSpeed2 && c.AngVel.LengthSquared() < StillSpeed2;
        still &= (c.Driver == 255 || (c.Throttle == 0 && c.SteerInput == 0)) && !(c.EngineWired && c.EngineSignal != 0);
        for (var j = 0; j < c.JointCount && still; j++)
        {
            ref var jt = ref c.Joints[j];
            still = MathF.Abs(jt.Vel) < 0.05f && (!jt.Motorised || MathF.Abs(MotorWant(c, jt)) <= 0.05f && jt.Over <= 1); // (a stalled or overloaded motor still pushes)
        }
        if (!still)
        {
            c.SleepTicks = 0;
            return;
        }
        if (++c.SleepTicks >= SleepAfterTicks)
        {
            c.Asleep = true;
            c.Vel = c.AngVel = Vector3.Zero;
            for (var j = 0; j < c.JointCount; j++)
                c.Joints[j].Vel = 0;
        }
    }
}
