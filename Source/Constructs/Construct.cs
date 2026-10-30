using System.Numerics;

namespace CallOfSlop;

/// <summary>One placed part of a construct: its type, where it sits in its segment's rest frame, its turn and its health.</summary>
public struct CPart
{
    public byte Def; // ConstructParts id
    public Vector3 Pos; // the part's origin in the rest frame (every segment's rest frame is the body frame at joint angle 0)
    public byte Rot; // Rot24 turn
    public byte Seg; // 0: the body itself; k > 0: hangs on joint k - 1
    public float Health;
    public bool Broken; // at zero health with PartBreak.Disable: still there, does nothing

    // Logic and seats (milestone 2; they move with the part when the arrays are compacted or split)
    public byte Setting; // index into its def's Settings (the linking tool's R cycles it)
    public byte State; // a switch's on/off, a lever's position (0..3), a toggle's output and last input, a timer's phase
    public ushort Count; // ticks: a button's pulse left, a timer's or delay's count
    public float Out, OutNext; // the signal it puts out (this tick's, and the next being worked out)
    public ushort Keys; // a seat: its sitter's keys this tick (Buttons, then fire/aim/reload bits; 0x8000: someone sits there)
    public byte SitterPlus1; // a seat: the player sitting in it, plus one (0: empty)
    public bool Stood; // a pressure plate: a player stood on it this tick

    // Milestone 3 (server; they move with the part when the arrays are compacted or split)
    public ushort Tag; // a stable id for couplings and wires between constructs (ConstructWorld gives it; 0 on a client's copy)
    public bool Powered; // a working battery's links reach it (ConstructPower.cs; rebuilt with the caches)

    public const ushort Occupied = 0x8000;

    public readonly ConstructPartDef D => ConstructParts.All[Def];
    public readonly bool Working => !Broken;
}

/// <summary>
/// A seated player's seat type (Player.RenderSeat): picks the seated pose and how far the look turns from the seat's facing.
/// None: not seated.
/// </summary>
public enum SeatKind : byte
{
    None,
    Driver, // hands on the wheel
    Passenger, // the gun resting on the lap
    Gunner, // a seat on a moving joint (turntable, hinge): the gun held up, the look all the way round
}

/// <summary>A wire (milestone 2): part <see cref="From"/>'s output goes into part <see cref="To"/>. Every wire carries the value its source had last tick.</summary>
public struct CWire
{
    public byte From, To;
}

/// <summary>An attachment between two parts: which socket of each (a weld or a bolt; a hinge's leaf link carries a joint).</summary>
public struct CLink
{
    public byte A, B; // part indices
    public byte SA, SB; // socket indices on each
}

/// <summary>
/// A one-degree-of-freedom joint. The parts of segment <c>index + 1</c> turn about <see cref="Axis"/> through
/// <see cref="Pivot"/> (both in the rest frame) relative to segment <see cref="ParentSeg"/>, or for a piston slide along
/// <see cref="Axis"/>. Reduced coordinates: the joint is one number (an angle, or a piston's extension in metres), so the
/// two pieces it links never drift apart and nothing can explode. Milestone 2's moving joints add a motor: a speed it
/// moves at and a strength (N m, or N for a piston) it can push with against the load (gravity on everything it carries,
/// players standing on it included).
/// </summary>
public struct CJoint
{
    public byte Hinge; // the joint part (a hinge, a motorised hinge, a turntable, a piston, a steering pivot)
    public byte ParentSeg;
    public Vector3 Pivot, Axis; // rest frame
    public float Angle, Vel; // radians (a piston: metres), per second
    public float Min, Max;
    public bool Open; // the latch: where Interact last sent it (closed 0, open at the far limit)
    public float PrevAngle; // last tick (drawing blends)
    public JointType Type;
    public float Speed, Strength; // the motor (from the part)
    public float Input; // the wires into its part, added up (last tick's values)
    public bool Wired, Locked; // something is wired into it; a passive hinge's lock is on
    public float Load; // the gravity load it held last tick (N m or N; the HUD and the self-test)

    public readonly bool Slides => Type == JointType.Piston;
    public readonly bool Motorised => Type != JointType.Passive;
}

/// <summary>A raycast wheel's cached set-up (rebuilt when the construct changes).</summary>
public struct CWheel
{
    public byte Part, Suspension;
    public Vector3 Centre; // body frame, at full extension
    public Vector3 Axle; // body frame, unit
    public float Radius, Travel, Frequency, Damping, Grip;
    public bool Steers; // in front of the centre of mass
    // State
    public float Compression, PrevCompression; // metres
    public bool Touching;
    public float Spin; // radians (drawing)
    public byte Seg; // the segment it hangs on (0, or a steering pivot's)
    public bool Driven; // its suspension is on a differential the transmission feeds
}

/// <summary>
/// A construct: parts welded and bolted together into one rigid body (decision: each welded group is one body), with
/// joints for the parts that hang on hinges. The root part (a seat, else a foundation, else the first part) is what
/// the construct is: anything cut off from it falls away as a separate wreck. Anchored (a working foundation) or free.
/// Arrays are sized once (<see cref="MaxParts"/>), so a construct allocates nothing as it is simulated or edited.
/// </summary>
public sealed class Construct
{
    public const int MaxParts = 96;
    public const int MaxLinks = MaxParts * 3;
    public const int MaxJoints = 8;
    public const int MaxPoints = 40; // contact points against the world
    public const int MaxWheels = 12;
    public const int MaxWires = 64;
    public const int MaxSegPoints = 64; // contact points on the parts beyond joints (every segment's, in all)
    public const float SegPointSpacing = 1.0f; // a long part gets points along its edges at most this far apart (no sinking in the middle)
    public const int MaxSegEdges = 48; // the long edges of the parts beyond joints, swept against the world (a post between two points)
    public const float SegEdgeMin = 0.3f; // edges shorter than this aren't swept (the points at their ends do)

    public ushort Id;
    public readonly CPart[] Parts = new CPart[MaxParts];
    public int PartCount;
    public readonly CLink[] Links = new CLink[MaxLinks];
    public int LinkCount;
    public readonly CJoint[] Joints = new CJoint[MaxJoints];
    public int JointCount;
    public readonly CWire[] Wires = new CWire[MaxWires];
    public int WireCount;
    public int Root;
    public bool Wreck; // cut off from a root (no seat or foundation of its own)
    public bool Anchored; // a working foundation holds it to the ground
    public bool Asleep;
    public int SleepTicks;

    // Pose: the body frame's origin and turn in the world.
    public Vector3 Pos;
    public Quaternion Rot = Quaternion.Identity;
    public Vector3 PrevPos;
    public Quaternion PrevRot = Quaternion.Identity;
    public Vector3 Vel, AngVel; // the centre of mass's velocity; angular velocity (world)

    // Mass properties (rebuilt with the caches)
    public float Mass, InvMass;
    public Vector3 Com; // body frame
    public Matrix4x4 InvInertiaLocal; // 3x3 in the top left (body frame, about the centre of mass)
    public Matrix4x4 InvInertiaBaseLocal; // milestone 3: the same for what doesn't turn on a joint (what hangs on one counts as its mass at the pivot): a joint's push turns this
    public Vector3 LocalMin, LocalMax; // round every part, rest frame
    public float Radius; // from the centre of mass, round everything (joints included)

    // Driving
    public byte Driver = 255; // player id in the seat (255: none)
    public int DriverSeat = -1;
    public float Throttle, Steer, SteerInput;
    public bool Brake;
    public float Fuel;
    public bool CanDrive; // seat + working engine + fuel path + transmission + driven wheels (the drivetrain)
    public int Engine = -1;
    public int Transmission = -1;
    public int DriveSeat = -1; // the seat whose sitter drives (the first working seat in the body); other seats carry passengers
    public string DriveProblem = ""; // why it can't drive (the HUD)

    // Logic (milestone 2; ConstructLogic.cs)
    public bool HasLogic; // any logic part or wire: the logic step runs
    public bool EngineWired; // a signal runs the engine (it drives without a driver)
    public float EngineSignal, BrakeSignal;
    public uint SigHash, SigSent; // the signal parts' values (hashed) now and as last sent to clients
    public readonly ulong[] RiderBits = new ulong[MaxJoints + 1]; // players (id & 63) standing on each segment this tick
    public readonly float[] SegMass = new float[MaxJoints + 1]; // mass carried by each segment, its own and every segment hanging on it
    public readonly Vector3[] SegCom = new Vector3[MaxJoints + 1]; // its centre (rest frame)
    public const float RiderMass = 85f; // a player standing on a joint's segment, for its load

    // Power (milestone 3; ConstructPower.cs): a construct with a battery is electrical (its logic parts, motors, lamps and
    // horns run on the charge, and its engine needs the starter); one without runs as milestone 2 did.
    public bool HasBattery; // a battery part, working or not
    public bool Electrical; // this tick: it has a battery, or the map requires power
    public float Charge; // kJ in its working batteries
    public float ChargeCapacity; // kJ they hold
    public bool PowerOn = true; // the batteries have charge (or it isn't electrical)
    public bool EngineRunning; // the engine runs (an electrical construct's engine needs the starter first)
    public bool StarterFailed; // it was asked to start and the battery couldn't turn the starter (the HUD)
    public float AlternatorRate; // kW its working alternators touching the engine put in while it runs
    public float PowerUse; // W drawn last tick (the HUD)
    public float MotorWork; // W its motors did this tick (the physics adds it; the power step drains it)
    public int Gear; // the transmission's gear (an index into its ratios)
    public float GearRatio = 1f; // top speed times this, force divided by it
    public bool Towed; // coupled to a construct that is driving (its parking brake lets go)
    public int XWireIn; // wires from other constructs into it (its logic runs for them)
    internal int SimIndex; // its index in the world's list this tick (the physics)

    // Caches
    public readonly Vector3[] Points = new Vector3[MaxPoints];
    public int PointCount;
    public readonly CWheel[] Wheels = new CWheel[MaxWheels];
    public int WheelCount;
    public readonly Vector3[] SegPoints = new Vector3[MaxSegPoints]; // rest frame of their segment (the parts beyond joints)
    public readonly byte[] SegPointSeg = new byte[MaxSegPoints];
    public int SegPointCount;
    public readonly Vector3[] SegEdgeA = new Vector3[MaxSegEdges], SegEdgeB = new Vector3[MaxSegEdges]; // rest frame of their segment
    public readonly byte[] SegEdgeSeg = new byte[MaxSegEdges];
    public int SegEdgeCount;
    public bool Dirty = true;

    // Client: the pose it is drawn at (blended towards the last one received).
    public Vector3 NetPos, NetFromPos;
    public Quaternion NetRot = Quaternion.Identity, NetFromRot = Quaternion.Identity;
    public double NetAt;
    public uint NetTick;

    public ref CPart P(int i) => ref Parts[i];

    public ConstructPartDef DefOf(int i) => ConstructParts.All[Parts[i].Def];

    public Vector3 WorldCom => Pos + Vector3.Transform(Com, Rot);

    public Vector3 ToWorld(Vector3 local) => Pos + Vector3.Transform(local, Rot);

    public Vector3 ToLocal(Vector3 world) => Vector3.Transform(world - Pos, Quaternion.Conjugate(Rot));

    /// <summary>Velocity of a world point carried by the body.</summary>
    public Vector3 PointVel(Vector3 world) => Vel + Vector3.Cross(AngVel, world - WorldCom);

    /// <summary>Segment <paramref name="seg"/>'s pose: rest frame -> body frame (q * v + t).</summary>
    public void SegPose(int seg, out Quaternion q, out Vector3 t)
    {
        q = Quaternion.Identity;
        t = Vector3.Zero;
        if (seg <= 0 || seg > JointCount)
            return;
        ref var j = ref Joints[seg - 1];
        SegPose(j.ParentSeg, out var pq, out var pt);
        if (j.Slides)
        {
            q = pq;
            t = pt + Vector3.Transform(j.Axis * j.Angle, pq);
            return;
        }
        var turn = Quaternion.CreateFromAxisAngle(j.Axis, j.Angle);
        // rest -> about the pivot -> the parent's pose
        var local = j.Pivot - Vector3.Transform(j.Pivot, turn);
        q = pq * turn;
        t = pt + Vector3.Transform(local, pq);
    }

    /// <summary>
    /// The velocity a joint segment gives a world point on it (on top of the body's own): every joint up its chain, a
    /// piston's slide or a turn about its axis. A player standing on a moving deck is carried by this.
    /// </summary>
    public Vector3 SegPointVel(int seg, Vector3 world)
    {
        var v = Vector3.Zero;
        for (var guard = 0; seg > 0 && seg <= JointCount && guard <= MaxJoints; guard++)
        {
            ref var j = ref Joints[seg - 1];
            if (j.Vel != 0)
            {
                WorldFrame(j.ParentSeg, out var q, out var t);
                var axis = Vector3.Transform(j.Axis, q);
                v += j.Slides ? axis * j.Vel : Vector3.Cross(axis * j.Vel, world - (t + Vector3.Transform(j.Pivot, q)));
            }
            seg = j.ParentSeg;
        }
        return v;
    }

    /// <summary>The joint whose part is <paramref name="part"/>, or -1.</summary>
    public int JointOf(int part)
    {
        for (var j = 0; j < JointCount; j++)
            if (Joints[j].Hinge == part)
                return j;
        return -1;
    }

    /// <summary>How many wires come into part <paramref name="part"/>.</summary>
    public int WiresInto(int part)
    {
        var n = 0;
        for (var k = 0; k < WireCount; k++)
            if (Wires[k].To == part)
                n++;
        return n;
    }

    /// <summary>The seat a player sits in on this construct, or -1.</summary>
    public int SeatOf(byte player)
    {
        for (var i = 0; i < PartCount; i++)
            if (Parts[i].SitterPlus1 == player + 1)
                return i;
        return -1;
    }

    /// <summary>What a seat is for (the seated pose, the look limits): the drive seat, a seat on a moving joint (a gunner's, on a turntable or hinge), or a passenger's.</summary>
    public SeatKind SeatKindOf(int part) =>
        part < 0 || part >= PartCount ? SeatKind.None : part == DriveSeat ? SeatKind.Driver : Parts[part].Seg != 0 ? SeatKind.Gunner : SeatKind.Passenger;

    /// <summary>A part box's frame in the world: rest-frame centre and half size (axis aligned there), and the world turn and offset of that frame.</summary>
    public void WorldFrame(int seg, out Quaternion q, out Vector3 t)
    {
        SegPose(seg, out var sq, out var st);
        q = Rot * sq;
        t = Pos + Vector3.Transform(st, Rot);
    }

    /// <summary>Part box <paramref name="b"/> of part <paramref name="i"/> in the rest frame (axis aligned: the part turns by 90 degree steps).</summary>
    public void BoxOf(int i, int b, out Vector3 centre, out Vector3 half)
    {
        ref var p = ref Parts[i];
        var box = p.D.Boxes[b];
        centre = p.Pos + Rot24.Apply(p.Rot, box.Center);
        half = Rot24.ApplyHalf(p.Rot, box.Half);
    }

    /// <summary>Part <paramref name="i"/>'s bounds in the rest frame.</summary>
    public void PartBounds(int i, out Vector3 min, out Vector3 max)
    {
        ref var p = ref Parts[i];
        var d = p.D;
        var c = (d.BoundsMin + d.BoundsMax) * 0.5f;
        var h = (d.BoundsMax - d.BoundsMin) * 0.5f;
        var wc = p.Pos + Rot24.Apply(p.Rot, c);
        var wh = Rot24.ApplyHalf(p.Rot, h);
        min = wc - wh;
        max = wc + wh;
    }

    /// <summary>Socket <paramref name="s"/> of part <paramref name="i"/> in the rest frame: where and which way.</summary>
    public void SocketAt(int i, int s, out Vector3 at, out byte dir)
    {
        ref var p = ref Parts[i];
        var sock = p.D.Sockets[s];
        at = p.Pos + Rot24.Apply(p.Rot, sock.At);
        dir = Rot24.ApplyDir(p.Rot, sock.Dir);
    }

    public bool SocketUsed(int part, int socket)
    {
        for (var k = 0; k < LinkCount; k++)
        {
            ref var l = ref Links[k];
            if ((l.A == part && l.SA == socket) || (l.B == part && l.SB == socket))
                return true;
        }
        return false;
    }

    public int CountRole(PartRole role, bool working = true)
    {
        var n = 0;
        for (var i = 0; i < PartCount; i++)
            if (Parts[i].D.Role == role && (!working || Parts[i].Working))
                n++;
        return n;
    }

    /// <summary>The part the root should be: the first seat, else the first foundation, else part 0.</summary>
    public int PickRoot()
    {
        for (var i = 0; i < PartCount; i++)
            if (Parts[i].D.Role == PartRole.Seat)
                return i;
        for (var i = 0; i < PartCount; i++)
            if (Parts[i].D.Role == PartRole.Foundation)
                return i;
        return 0;
    }

    /// <summary>A loose part: one part, whole, not a seat or a foundation (can be picked up; wrecked debris can't).</summary>
    public bool Loose => PartCount == 1 && !Parts[0].Broken && Parts[0].D.Role is not (PartRole.Seat or PartRole.Foundation) && !Anchored;

    public void Wake()
    {
        Asleep = false;
        SleepTicks = 0;
    }

    // ---- Caches: mass, inertia, contact points, wheels, the drivetrain ------------------------------------------

    /// <summary>Rebuilds everything that follows from the parts (after any edit, break or split). Keeps the world pose.</summary>
    public void Rebuild()
    {
        Dirty = false;
        var oldCom = Com;
        var oldWorldCom = WorldCom;
        Mass = 0;
        var com = Vector3.Zero;
        LocalMin = new Vector3(float.MaxValue);
        LocalMax = new Vector3(float.MinValue);
        for (var i = 0; i < PartCount; i++)
        {
            var d = Parts[i].D;
            PartBounds(i, out var mn, out var mx);
            LocalMin = Vector3.Min(LocalMin, mn);
            LocalMax = Vector3.Max(LocalMax, mx);
            com += (mn + mx) * 0.5f * d.Mass;
            Mass += d.Mass;
        }
        if (PartCount == 0)
        {
            Mass = 1;
            LocalMin = LocalMax = Vector3.Zero;
        }
        Com = PartCount > 0 ? com / Mass : Vector3.Zero;
        InvMass = 1f / Mass;
        // Inertia about the centre of mass: each box a solid block (its share of the part's mass by volume).
        float ixx = 0, iyy = 0, izz = 0, ixy = 0, ixz = 0, iyz = 0;
        for (var i = 0; i < PartCount; i++)
        {
            var d = Parts[i].D;
            var vol = 0f;
            for (var b = 0; b < d.SolidBoxes; b++)
                vol += d.Boxes[b].Half.X * d.Boxes[b].Half.Y * d.Boxes[b].Half.Z;
            for (var b = 0; b < d.SolidBoxes; b++)
            {
                BoxOf(i, b, out var c, out var h);
                var m = d.Mass * (d.Boxes[b].Half.X * d.Boxes[b].Half.Y * d.Boxes[b].Half.Z) / MathF.Max(vol, 1e-9f);
                var s = h * 2;
                var r = c - Com;
                ixx += m / 12f * (s.Y * s.Y + s.Z * s.Z) + m * (r.Y * r.Y + r.Z * r.Z);
                iyy += m / 12f * (s.X * s.X + s.Z * s.Z) + m * (r.X * r.X + r.Z * r.Z);
                izz += m / 12f * (s.X * s.X + s.Y * s.Y) + m * (r.X * r.X + r.Y * r.Y);
                ixy -= m * r.X * r.Y;
                ixz -= m * r.X * r.Z;
                iyz -= m * r.Y * r.Z;
            }
        }
        // A little extra so thin builds don't spin like tops.
        var floor = Mass * 0.05f;
        var inertia = new Matrix4x4(ixx + floor, ixy, ixz, 0, ixy, iyy + floor, iyz, 0, ixz, iyz, izz + floor, 0, 0, 0, 0, 1);
        InvInertiaLocal = Matrix4x4.Invert(inertia, out var inv) ? inv : Matrix4x4.Identity;
        // Keep the centre of mass where it was in the world (the parts didn't move; the body origin did not either).
        _ = oldCom;
        _ = oldWorldCom;
        Radius = MathF.Max(Vector3.Distance(Com, LocalMin), Vector3.Distance(Com, LocalMax)) + 0.5f;

        BuildPoints();
        BuildSegPoints();
        Radius = MathF.Max(Radius, JointReach() + 0.5f);
        BuildWheels();
        BuildDrivetrain();
        BuildSegments();
        ConstructPower.Rebuild(this);
        Anchored = false;
        for (var i = 0; i < PartCount; i++)
            if (Parts[i].D.Role == PartRole.Foundation && Parts[i].Working && Parts[i].Seg == 0)
                Anchored = true;
        Root = PickRoot();
        if (Fuel <= 0 && !_fuelSet)
        {
            Fuel = 0;
            for (var i = 0; i < PartCount; i++)
                if (Parts[i].D.Role == PartRole.FuelTank && Parts[i].Working)
                    Fuel += Parts[i].D.FuelCapacity;
            _fuelSet = PartCount > 0;
        }
        var cap = FuelCapacity();
        if (Fuel > cap)
            Fuel = cap;
        if (!_chargeSet)
        {
            Charge = ChargeCapacity; // (batteries come charged)
            _chargeSet = PartCount > 0;
        }
        Charge = Math.Clamp(Charge, 0, ChargeCapacity);
        if (Driver != 255 && (DriverSeat < 0 || DriverSeat >= PartCount || Parts[DriverSeat].D.Role != PartRole.Seat))
        {
            Driver = 255;
            DriverSeat = -1;
        }
    }

    private bool _fuelSet, _chargeSet;

    /// <summary>A piece that fell away keeps no fuel or charge (salvage takes the parts, not what was in them).</summary>
    public void Empty()
    {
        Fuel = 0;
        Charge = 0;
        _fuelSet = _chargeSet = true;
    }

    /// <summary>Each segment's carried mass and its centre (its parts and every segment hanging on it); whether logic runs.</summary>
    private void BuildSegments()
    {
        for (var s = 0; s <= MaxJoints; s++)
        {
            SegMass[s] = 0;
            SegCom[s] = Vector3.Zero;
        }
        HasLogic = WireCount > 0;
        for (var i = 0; i < PartCount; i++)
        {
            var d = Parts[i].D;
            if (d.Role == PartRole.Logic || d.Role == PartRole.Seat && WireCount > 0)
                HasLogic = true;
            PartBounds(i, out var mn, out var mx);
            var c = (mn + mx) * 0.5f;
            for (int s = Parts[i].Seg, guard = 0; guard <= MaxJoints; guard++)
            {
                if (s < 0 || s > JointCount)
                    break;
                SegMass[s] += d.Mass;
                SegCom[s] += c * d.Mass;
                if (s == 0)
                    break;
                s = Joints[s - 1].ParentSeg;
            }
        }
        for (var s = 0; s <= JointCount; s++)
            if (SegMass[s] > 0)
                SegCom[s] /= SegMass[s];
        BaseInertia();
        // The motors come from their parts (a client's copy too: the wire carries only the type).
        for (var j = 0; j < JointCount; j++)
        {
            ref var jt = ref Joints[j];
            if (jt.Hinge < PartCount && Parts[jt.Hinge].D is { HasJoint: true } jd)
            {
                jt.Type = jd.JointType;
                jt.Speed = jd.MotorSpeed;
                jt.Strength = jd.MotorStrength;
            }
        }
    }

    /// <summary>
    /// The inertia a joint's push turns (milestone 3: the reaction): the parts of the body itself as boxes, and what hangs
    /// on each of its joints as that mass at the joint's pivot (it swings freely about its own joint, so only its weight
    /// rides with the body). Without joints it is the body's own.
    /// </summary>
    private void BaseInertia()
    {
        if (JointCount == 0)
        {
            InvInertiaBaseLocal = InvInertiaLocal;
            return;
        }
        float ixx = 0, iyy = 0, izz = 0, ixy = 0, ixz = 0, iyz = 0;
        void Point(float m, Vector3 r, Vector3 s)
        {
            ixx += m / 12f * (s.Y * s.Y + s.Z * s.Z) + m * (r.Y * r.Y + r.Z * r.Z);
            iyy += m / 12f * (s.X * s.X + s.Z * s.Z) + m * (r.X * r.X + r.Z * r.Z);
            izz += m / 12f * (s.X * s.X + s.Y * s.Y) + m * (r.X * r.X + r.Y * r.Y);
            ixy -= m * r.X * r.Y;
            ixz -= m * r.X * r.Z;
            iyz -= m * r.Y * r.Z;
        }
        for (var i = 0; i < PartCount; i++)
        {
            if (Parts[i].Seg != 0)
                continue;
            var d = Parts[i].D;
            var vol = 0f;
            for (var b = 0; b < d.SolidBoxes; b++)
                vol += d.Boxes[b].Half.X * d.Boxes[b].Half.Y * d.Boxes[b].Half.Z;
            for (var b = 0; b < d.SolidBoxes; b++)
            {
                BoxOf(i, b, out var c, out var h);
                Point(d.Mass * (d.Boxes[b].Half.X * d.Boxes[b].Half.Y * d.Boxes[b].Half.Z) / MathF.Max(vol, 1e-9f), c - Com, h * 2);
            }
        }
        for (var j = 0; j < JointCount; j++)
            if (Joints[j].ParentSeg == 0)
                Point(SegMass[j + 1], Joints[j].Pivot - Com, Vector3.Zero);
        var floor = Mass * 0.05f;
        var inertia = new Matrix4x4(ixx + floor, ixy, ixz, 0, ixy, iyy + floor, iyz, 0, ixz, iyz, izz + floor, 0, 0, 0, 0, 1);
        InvInertiaBaseLocal = Matrix4x4.Invert(inertia, out var inv) ? inv : InvInertiaLocal;
    }

    /// <summary>World-space inverse of <see cref="InvInertiaBaseLocal"/>.</summary>
    public Matrix4x4 InvInertiaBaseWorld()
    {
        var r = Matrix4x4.CreateFromQuaternion(Rot);
        return Matrix4x4.Transpose(r) * InvInertiaBaseLocal * r;
    }

    /// <summary>Litres the working tanks hold.</summary>
    public float FuelCapacity()
    {
        var cap = 0f;
        for (var i = 0; i < PartCount; i++)
            if (Parts[i].D.Role == PartRole.FuelTank && Parts[i].Working)
                cap += Parts[i].D.FuelCapacity;
        return cap;
    }

    /// <summary>A new tank was fitted: it comes full.</summary>
    public void AddFuel(float litres) => Fuel += litres;

    private bool IsRaycastWheel(int i)
    {
        if (Parts[i].D.Role != PartRole.Wheel)
            return false;
        var seg = Parts[i].Seg;
        if (seg != 0 && (seg > JointCount || Joints[seg - 1].Type != JointType.Steering || Joints[seg - 1].ParentSeg != 0))
            return false; // a wheel rolls on the body, or on a steering pivot's segment
        return SuspensionOf(i) >= 0;
    }

    /// <summary>The working suspension a wheel is bolted to (by its axle socket), or -1.</summary>
    public int SuspensionOf(int wheel)
    {
        for (var k = 0; k < LinkCount; k++)
        {
            ref var l = ref Links[k];
            int other;
            if (l.A == wheel)
                other = l.B;
            else if (l.B == wheel)
                other = l.A;
            else
                continue;
            if (Parts[other].D.Role == PartRole.Suspension && Parts[other].Working && Parts[other].Seg == Parts[wheel].Seg)
                return other;
        }
        return -1;
    }

    private void BuildPoints()
    {
        PointCount = 0;
        Span<Vector3> scratch = stackalloc Vector3[MaxParts * 8 > 512 ? 512 : MaxParts * 8];
        var n = 0;
        for (var i = 0; i < PartCount && n + 8 <= scratch.Length; i++)
        {
            if (Parts[i].Seg != 0 || IsRaycastWheel(i))
                continue;
            PartBounds(i, out var mn, out var mx);
            for (var c = 0; c < 8; c++)
            {
                var p = new Vector3((c & 1) != 0 ? mx.X : mn.X, (c & 2) != 0 ? mx.Y : mn.Y, (c & 4) != 0 ? mx.Z : mn.Z);
                var dup = false;
                for (var k = 0; k < n && !dup; k++)
                    dup = Vector3.DistanceSquared(scratch[k], p) < 1e-4f;
                if (!dup)
                    scratch[n++] = p;
            }
        }
        // Too many: the ones furthest from the centre of mass (the hull carries the body; inner corners rarely touch).
        // Points shared by several part corners inside the body are skipped first: a point is "inner" when it isn't on the bounds.
        if (n > MaxPoints)
        {
            for (var a = 0; a < n; a++)
            for (var b = a + 1; b < n; b++)
                if (Score(scratch[b]) > Score(scratch[a]) || (Score(scratch[b]) == Score(scratch[a]) && Less(scratch[b], scratch[a])))
                    (scratch[a], scratch[b]) = (scratch[b], scratch[a]);
            n = MaxPoints;
        }
        for (var k = 0; k < n; k++)
            Points[k] = scratch[k];
        PointCount = n;
    }

    /// <summary>
    /// The contact points of the parts beyond joints (in their segment's rest frame): every part's bounds corners, and on
    /// a long part more along its long edges (<see cref="SegPointSpacing"/> apart), so a beam lying on a bump can't sink
    /// in the middle; and those long edges themselves (swept against the world, for a post that fits between the points).
    /// Raycast wheels on a steering pivot roll on their rays instead.
    /// </summary>
    private void BuildSegPoints()
    {
        SegPointCount = 0;
        SegEdgeCount = 0;
        Span<float> steps = stackalloc float[8];
        for (var i = 0; i < PartCount; i++)
        {
            var seg = Parts[i].Seg;
            if (seg == 0 || IsRaycastWheel(i))
                continue;
            PartBounds(i, out var mn, out var mx);
            var size = mx - mn;
            for (var axis = 0; axis < 3; axis++)
            {
                // Its long edges along this axis, swept against the world each tick.
                if (size[axis] >= SegEdgeMin)
                    for (var c = 0; c < 4 && SegEdgeCount < MaxSegEdges; c++)
                    {
                        var a = Vector3.Zero;
                        var a1 = (axis + 1) % 3;
                        var a2 = (axis + 2) % 3;
                        a[a1] = (c & 1) != 0 ? mx[a1] : mn[a1];
                        a[a2] = (c & 2) != 0 ? mx[a2] : mn[a2];
                        var b = a;
                        a[axis] = mn[axis];
                        b[axis] = mx[axis];
                        SegEdgeA[SegEdgeCount] = a;
                        SegEdgeB[SegEdgeCount] = b;
                        SegEdgeSeg[SegEdgeCount++] = seg;
                    }
                // Along this axis: both ends, and between them every SegPointSpacing (corners: the other two axes at their ends).
                var cuts = Math.Min(7, (int)MathF.Ceiling(size[axis] / SegPointSpacing));
                var n = 0;
                for (var k = 0; k <= cuts; k++)
                    steps[n++] = mn[axis] + size[axis] * k / Math.Max(1, cuts);
                for (var k = 0; k < n; k++)
                for (var c = 0; c < 4; c++)
                {
                    var p = Vector3.Zero;
                    p[axis] = steps[k];
                    var a1 = (axis + 1) % 3;
                    var a2 = (axis + 2) % 3;
                    p[a1] = (c & 1) != 0 ? mx[a1] : mn[a1];
                    p[a2] = (c & 2) != 0 ? mx[a2] : mn[a2];
                    AddSegPoint(p, seg);
                }
            }
        }
    }

    private void AddSegPoint(Vector3 p, byte seg)
    {
        if (SegPointCount >= MaxSegPoints)
            return;
        for (var k = 0; k < SegPointCount; k++)
            if (SegPointSeg[k] == seg && Vector3.DistanceSquared(SegPoints[k], p) < 1e-4f)
                return;
        SegPoints[SegPointCount] = p;
        SegPointSeg[SegPointCount++] = seg;
    }

    /// <summary>How far from the centre of mass any part beyond a joint can reach, whatever its joints' values (the broad phase's radius).</summary>
    private float JointReach()
    {
        var reach = 0f;
        for (var k = 0; k < SegPointCount; k++)
        {
            // From the point back up its chain: each turn keeps the distance to its pivot, each slide adds its travel.
            var p = SegPoints[k];
            var d = 0f;
            var from = p;
            for (int s = SegPointSeg[k], guard = 0; s > 0 && s <= JointCount && guard <= MaxJoints; guard++)
            {
                ref var j = ref Joints[s - 1];
                if (j.Slides)
                    d += MathF.Max(MathF.Abs(j.Min), MathF.Abs(j.Max));
                else
                {
                    d += Vector3.Distance(from, j.Pivot);
                    from = j.Pivot;
                }
                s = j.ParentSeg;
            }
            reach = MathF.Max(reach, d + Vector3.Distance(from, Com));
        }
        return reach;
    }

    private float Score(Vector3 p)
    {
        var onBounds = 0;
        for (var a = 0; a < 3; a++)
            if (MathF.Abs(p[a] - LocalMin[a]) < 0.01f || MathF.Abs(p[a] - LocalMax[a]) < 0.01f)
                onBounds++;
        return onBounds * 100f + Vector3.Distance(p, Com);
    }

    private static bool Less(Vector3 a, Vector3 b) => a.X != b.X ? a.X < b.X : a.Y != b.Y ? a.Y < b.Y : a.Z < b.Z;

    private void BuildWheels()
    {
        var old = WheelCount;
        Span<float> keep = stackalloc float[MaxWheels];
        for (var k = 0; k < old; k++)
            keep[k] = Wheels[k].Compression;
        WheelCount = 0;
        for (var i = 0; i < PartCount && WheelCount < MaxWheels; i++)
        {
            if (!IsRaycastWheel(i))
                continue;
            var s = SuspensionOf(i);
            var d = Parts[i].D;
            var sd = Parts[s].D;
            PartBounds(i, out var mn, out var mx);
            var centre = (mn + mx) * 0.5f;
            // The axle: the wheel's axle socket direction, turned into the body.
            var axleDir = (byte)0;
            for (var k = 0; k < d.Sockets.Length; k++)
                if (d.Sockets[k].Kind == SocketKind.Axle)
                {
                    axleDir = Rot24.ApplyDir(Parts[i].Rot, d.Sockets[k].Dir);
                    break;
                }
            var axle = ConstructParts.DirVector(axleDir);
            if (MathF.Abs(axle.Y) > 0.5f)
                continue; // a wheel lying flat doesn't roll: it is a box like any other
            ref var w = ref Wheels[WheelCount];
            w = new CWheel
            {
                Part = (byte)i,
                Suspension = (byte)s,
                Centre = centre + new Vector3(0, sd.Travel * 0.5f, 0), // drawn and cast from the top of its travel
                Axle = axle,
                Radius = d.WheelRadius,
                Travel = sd.Travel,
                Frequency = sd.Frequency,
                Damping = sd.Damping,
                Grip = d.Grip * d.Friction,
                Steers = Parts[i].Seg != 0, // (steering is a joint since milestone 2: a wheel on a steering pivot)
                Compression = WheelCount < old ? keep[WheelCount] : 0,
                Seg = Parts[i].Seg,
            };
            WheelCount++;
        }
    }

    /// <summary>A wheel's top-of-travel centre and axle in the body frame now (turned with its steering pivot).</summary>
    public void WheelFrame(int k, out Vector3 centre, out Vector3 axle)
    {
        ref var w = ref Wheels[k];
        centre = w.Centre;
        axle = w.Axle;
        if (w.Seg == 0)
            return;
        SegPose(w.Seg, out var q, out var t);
        centre = Vector3.Transform(centre, q) + t;
        axle = Vector3.Transform(axle, q);
    }

    /// <summary>Which way the vehicle faces in the body frame: the root seat's -Z (or -Z).</summary>
    public Vector3 SeatForward()
    {
        for (var i = 0; i < PartCount; i++)
            if (Parts[i].D.Role == PartRole.Seat && Parts[i].Seg == 0)
                return Rot24.Apply(Parts[i].Rot, -Vector3.UnitZ);
        return -Vector3.UnitZ;
    }

    /// <summary>
    /// The drivetrain (decision: it works only when connected): a working seat, a working engine in the body, fuel from a
    /// working tank that touches the engine directly or through working fuel lines, a working transmission touching the
    /// engine, and wheels on suspensions bolted to a working differential that the transmission feeds (touching it, or
    /// through other differentials), directly or through a steering pivot. Only those wheels are driven; the rest roll.
    /// <see cref="DriveProblem"/> says what is missing.
    /// </summary>
    private void BuildDrivetrain()
    {
        CanDrive = false;
        Engine = -1;
        Transmission = -1;
        DriveSeat = -1;
        for (var k = 0; k < WheelCount; k++)
            Wheels[k].Driven = false;
        for (var i = 0; i < PartCount; i++)
            if (Parts[i].D.Role == PartRole.Seat && Parts[i].Seg == 0 && Parts[i].Working)
            {
                DriveSeat = i;
                break;
            }
        if (DriveSeat < 0)
        {
            DriveProblem = CountRole(PartRole.Seat, false) > 0 ? "the seat is wrecked" : "no seat";
            return;
        }
        var anyEngine = false;
        for (var i = 0; i < PartCount; i++)
        {
            if (Parts[i].D.Role != PartRole.Engine || Parts[i].Seg != 0)
                continue;
            anyEngine = true;
            if (Parts[i].Working && FuelReaches(i))
            {
                Engine = i;
                break;
            }
        }
        if (Engine < 0)
        {
            DriveProblem = !anyEngine ? "no engine" : CountRole(PartRole.Engine) == 0 ? "the engine is wrecked"
                : CountRole(PartRole.FuelTank) == 0 ? "no working fuel tank" : "no fuel line from the tank to the engine";
            return;
        }
        // The transmission: on the engine.
        var anyTransmission = false;
        for (var k = 0; k < LinkCount && Transmission < 0; k++)
        {
            ref var l = ref Links[k];
            var other = l.A == Engine ? l.B : l.B == Engine ? l.A : -1;
            if (other < 0 || Parts[other].D.Role != PartRole.Transmission)
                continue;
            anyTransmission = true;
            if (Parts[other].Working)
                Transmission = other;
        }
        if (Transmission < 0)
        {
            DriveProblem = anyTransmission ? "the transmission is wrecked" : CountRole(PartRole.Transmission, false) > 0 ? "the transmission isn't on the engine" : "no transmission";
            return;
        }
        // The differentials it feeds (touching it, or each other), and the wheels on them.
        Span<bool> fed = stackalloc bool[MaxParts];
        Span<byte> queue = stackalloc byte[MaxParts];
        int head = 0, tail = 0;
        queue[tail++] = (byte)Transmission;
        fed[Transmission] = true;
        var anyDiff = false;
        while (head < tail)
        {
            int at = queue[head++];
            for (var k = 0; k < LinkCount; k++)
            {
                ref var l = ref Links[k];
                var other = l.A == at ? l.B : l.B == at ? l.A : -1;
                if (other < 0 || fed[other] || Parts[other].D.Role != PartRole.Differential)
                    continue;
                anyDiff = true;
                if (!Parts[other].Working)
                    continue;
                fed[other] = true;
                queue[tail++] = (byte)other;
            }
        }
        var driven = 0;
        for (var k = 0; k < WheelCount; k++)
        {
            int s = Wheels[k].Suspension;
            var on = false;
            for (var l = 0; l < LinkCount && !on; l++)
            {
                var other = Links[l].A == s ? Links[l].B : Links[l].B == s ? Links[l].A : -1;
                if (other < 0)
                    continue;
                if (Parts[other].D.Role == PartRole.Differential && fed[other])
                    on = true;
                else if (Parts[other].D.Role == PartRole.Joint && Parts[other].Working && Parts[other].D.JointType == JointType.Steering)
                    for (var l2 = 0; l2 < LinkCount && !on; l2++) // through a steering pivot bolted to a differential
                    {
                        var o2 = Links[l2].A == other ? Links[l2].B : Links[l2].B == other ? Links[l2].A : -1;
                        on = o2 >= 0 && Parts[o2].D.Role == PartRole.Differential && fed[o2];
                    }
            }
            Wheels[k].Driven = on;
            if (on)
                driven++;
        }
        if (driven == 0)
        {
            DriveProblem = WheelCount == 0 ? "no wheels on suspension" : !anyDiff ? "no differential on the transmission"
                : CountRole(PartRole.Differential) == 0 ? "the differential is wrecked" : "no wheels on the differential";
            return;
        }
        CanDrive = true;
        DriveProblem = "";
    }

    /// <summary>Does fuel reach engine <paramref name="engine"/>: a working tank linked to it directly or through working fuel lines?</summary>
    private bool FuelReaches(int engine)
    {
        Span<bool> seen = stackalloc bool[MaxParts];
        Span<byte> queue = stackalloc byte[MaxParts];
        int head = 0, tail = 0;
        queue[tail++] = (byte)engine;
        seen[engine] = true;
        while (head < tail)
        {
            int at = queue[head++];
            for (var k = 0; k < LinkCount; k++)
            {
                ref var l = ref Links[k];
                int other;
                if (l.A == at)
                    other = l.B;
                else if (l.B == at)
                    other = l.A;
                else
                    continue;
                if (seen[other] || !Parts[other].Working)
                    continue;
                var role = Parts[other].D.Role;
                if (role == PartRole.FuelTank)
                    return true;
                if (role == PartRole.FuelLine)
                {
                    seen[other] = true;
                    queue[tail++] = (byte)other;
                }
            }
        }
        return false;
    }

    /// <summary>World-space inverse inertia (R I^-1 R^T).</summary>
    public Matrix4x4 InvInertiaWorld()
    {
        var r = Matrix4x4.CreateFromQuaternion(Rot);
        var rt = Matrix4x4.Transpose(r);
        // System.Numerics multiplies row vectors: v * M. Transform(v, R) = v * R, so world = R^T-style; compose accordingly.
        return rt * InvInertiaLocal * r;
    }

    /// <summary>Applies inverse inertia (world) to a vector.</summary>
    public static Vector3 Mul(in Matrix4x4 m, Vector3 v) => Vector3.TransformNormal(v, m);

    /// <summary>The body's bounds in the world (round every part at its current joint angles).</summary>
    public void WorldBounds(out Vector3 min, out Vector3 max)
    {
        var c = WorldCom;
        min = c - new Vector3(Radius);
        max = c + new Vector3(Radius);
    }
}
