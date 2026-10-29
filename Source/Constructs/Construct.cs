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

    public readonly ConstructPartDef D => ConstructParts.All[Def];
    public readonly bool Working => !Broken;
}

/// <summary>An attachment between two parts: which socket of each (a weld or a bolt; a hinge's leaf link carries a joint).</summary>
public struct CLink
{
    public byte A, B; // part indices
    public byte SA, SB; // socket indices on each
}

/// <summary>
/// A one-degree-of-freedom joint (milestone 1: the passive hinge). The parts of segment <c>index + 1</c> turn about
/// <see cref="Axis"/> through <see cref="Pivot"/> (both in the rest frame) relative to segment <see cref="ParentSeg"/>.
/// Reduced coordinates: the joint is an angle, so it never drifts apart; turntables and pistons (milestone 2) are the
/// same record with a motor and, for a piston, a slide instead of a turn.
/// </summary>
public struct CJoint
{
    public byte Hinge; // the hinge part
    public byte ParentSeg;
    public Vector3 Pivot, Axis; // rest frame
    public float Angle, Vel; // radians, radians a second
    public float Min, Max;
    public bool Open; // the latch: where Interact last sent it (closed 0, open at the far limit)
    public float PrevAngle; // last tick (drawing blends)
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

    public ushort Id;
    public readonly CPart[] Parts = new CPart[MaxParts];
    public int PartCount;
    public readonly CLink[] Links = new CLink[MaxLinks];
    public int LinkCount;
    public readonly CJoint[] Joints = new CJoint[MaxJoints];
    public int JointCount;
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
    public Vector3 LocalMin, LocalMax; // round every part, rest frame
    public float Radius; // from the centre of mass, round everything (joints included)

    // Driving
    public byte Driver = 255; // player id in the seat (255: none)
    public int DriverSeat = -1;
    public float Throttle, Steer, SteerInput;
    public bool Brake;
    public float Fuel;
    public bool CanDrive; // seat + working engine + fuel path (the drivetrain)
    public int Engine = -1;
    public string DriveProblem = ""; // why it can't drive (the HUD)

    // Caches
    public readonly Vector3[] Points = new Vector3[MaxPoints];
    public int PointCount;
    public readonly CWheel[] Wheels = new CWheel[MaxWheels];
    public int WheelCount;
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
        var turn = Quaternion.CreateFromAxisAngle(j.Axis, j.Angle);
        // rest -> about the pivot -> the parent's pose
        var local = j.Pivot - Vector3.Transform(j.Pivot, turn);
        q = pq * turn;
        t = pt + Vector3.Transform(local, pq);
    }

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

    /// <summary>A loose part: one part, not a seat or a foundation, nobody in it (can be picked up).</summary>
    public bool Loose => PartCount == 1 && Parts[0].D.Role is not (PartRole.Seat or PartRole.Foundation) && !Anchored;

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
        BuildWheels();
        BuildDrivetrain();
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
        if (Driver != 255 && (DriverSeat < 0 || DriverSeat >= PartCount || Parts[DriverSeat].D.Role != PartRole.Seat))
        {
            Driver = 255;
            DriverSeat = -1;
        }
    }

    private bool _fuelSet;

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
        if (Parts[i].D.Role != PartRole.Wheel || Parts[i].Seg != 0)
            return false;
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
            if (Parts[other].D.Role == PartRole.Suspension && Parts[other].Working && Parts[other].Seg == 0)
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
                Steers = false,
                Compression = WheelCount < old ? keep[WheelCount] : 0,
            };
            WheelCount++;
        }
        // Steering: the wheels ahead of the centre of mass (forward is -Z in the body frame, as the seat faces).
        var fwd = SeatForward();
        for (var k = 0; k < WheelCount; k++)
            Wheels[k].Steers = Vector3.Dot(Wheels[k].Centre - Com, fwd) > 0.3f;
        // The wheels' own points are not in BuildPoints: rebuild those (a wheel whose suspension went is a plain box again).
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
    /// The drivetrain (decision: it works only when connected): a working seat, a working engine in the body, and fuel
    /// from a working tank that touches the engine directly or through working fuel lines. Wheels on working
    /// suspensions are driven. <see cref="DriveProblem"/> says what is missing.
    /// </summary>
    private void BuildDrivetrain()
    {
        CanDrive = false;
        Engine = -1;
        var seat = -1;
        for (var i = 0; i < PartCount; i++)
            if (Parts[i].D.Role == PartRole.Seat && Parts[i].Seg == 0 && Parts[i].Working)
            {
                seat = i;
                break;
            }
        if (seat < 0)
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
        if (WheelCount == 0)
        {
            DriveProblem = "no wheels on suspension";
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
