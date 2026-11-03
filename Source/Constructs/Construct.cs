using System.Numerics;

namespace CallOfSlop;

/// <summary>One placed part of a construct: its type, where it sits in its segment's rest frame, its turn and its health.</summary>
public struct CPart
{
    public ushort Def; // ConstructParts id (two bytes since protocol 87)
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

    // Weapons (milestone 3; ConstructWeapons.cs)
    public ushort Rounds; // an ammo box: rounds left; a gun: rounds loaded
    public ushort Cool, Reload; // a gun: ticks to its next shot, ticks of its reload left
    public ushort Burn; // ticks it still burns (incendiary rounds, a cook-off)
    public float In; // the wires into it this tick, added up (a gun's trigger)
    public byte InCount; // how many wires come into it
    public float AimYaw, AimPitch; // a seat: its sitter's look (world), for a gunner aiming

    // Physical storage (decision 18; ConstructStorage.cs): a storage part's contents, a handle into ConstructWorld.Stores (0: none). It moves with the part.
    public ushort Store;

    // The spawners (GameSandboxSpawns.cs; server): their second and third inputs (spawn now, rate) added up and how many
    // wires come into each, the second output (the spawned pulse), how many it has alive (its first output), how many it
    // has spawned in all, and its clock towards the next spawn (one spawn at 1).
    public float In1, In2;
    public byte InCount1, InCount2;
    public float Out1;
    public float SpawnAlive, SpawnTotal, SpawnClock;

    // Settings (the configure tool; ConstructConfig.cs): one value a slot of its def's "config" schema. It moves with the part.
    public CfgValues Cfg;

    // Paint (the paint tool; ConstructPaint.cs): the colour the part's paintable boxes are drawn in, 0 when it has its own (else 0x01000000 | 0xRRGGBB). It moves with the part.
    public uint Paint;

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
    public byte FromPort, ToPort; // named data ports (the spawners: ConstructPartDef.DataIn / DataOut); 0: the part itself
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
    public bool Aimed; // milestone 3: a gunner (or a sentry's sensor) points it this tick: it turns towards AimErr
    public float AimErr; // radians (about its axis) between where its gun points and where it should
    // The strength model (board item dev-sandbox-actuator-sizes-as01; ConstructPhysics.cs): Strength is its rating
    public float Ref; // server: where the motor's reference is (its target reached at its speed); the joint lags it only when overloaded
    public bool RefSet; // Ref holds a value (a new joint takes its angle)
    public float Give; // how far it has given at twice its rating (radians; a piston's metres): the yield curve's scale
    public float SideLoad; // a piston: the most load across its axis before it gives (N; 0: none)
    public float Force; // the load the motor held, smoothed (N m; a piston's N)
    public float Over; // load against rating (1: at its rating, more: overloaded); clients hear it in the pose
    public float Strain; // overload damage owed to its part (hp; only with the part's overloadDamage on)
    // A freeform weld (JointType.Fixed; board item fw01, ConstructFreeform.cs): Hinge is the anchor part (on ParentSeg),
    // Child the welded-on part (on this segment); the segment's rest frame sits at FixRot / FixPos in its parent's.
    public Quaternion FixRot;
    public Vector3 FixPos;
    public byte Child;
    public bool Cut; // server, transient: the welder cut this seam (the split no longer crosses it)

    public readonly bool Slides => Type == JointType.Piston;
    public readonly bool Motorised => Type is not (JointType.Passive or JointType.Fixed);
    public readonly bool Freeform => Type == JointType.Fixed;
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
    // Drivetrain families (ConstructDrive.cs)
    public float Ratio, Eff; // reduction from the gearbox's output to this wheel (the final drive and transfer parts), what reaches it
    public float CapEq; // the most gearbox-output torque its drive line carries (a belt slips above it)
    public ushort GateMask; // the gates (clutches, a transfer case's front, U joints) its drive goes through
    public byte Group; // the differential or hub that drives it (index into Construct.GroupPart)
    public float Offroad, Side, Roll; // grip on terrain (times Grip), sideways, rolling resistance
    public float MaxLoad; // kg its spring carries before it sags
    public bool Track, Drivable; // one of a track unit's contact rays (drawn without spin); can be driven
    public float SideSign; // which side of the centre of mass (skid steering: +1 right, -1 left)
    public float Share; // this tick: its share of the engine's torque (the differentials)
    public float LastGrip; // last tick: the most friction impulse its tyre had (an open differential drives its wheels no harder than the one that grips least)
    public float Omega; // rad/s it turns (server): rolling on the ground, its speed over it; on an anchored construct, what the engine turns it at (its tread then drives what touches it: board item cs03)
}

/// <summary>
/// A spring foot (board item cs03): a suspension with nothing on its axle, pointing down under the body. A raycast spring
/// like a wheel's, with no tyre: a platform on suspensions stands on its springs and gives under a load (a player landing).
/// </summary>
public struct CSpring
{
    public byte Part;
    public Vector3 Origin; // body frame: the axle socket's point, where the ray starts
    public float Travel, Frequency, Damping, MaxLoad;
    public float Compression, PrevCompression; // metres
    public bool Touching;
}

/// <summary>
/// A construct: parts welded and bolted together into one rigid body (decision: each welded group is one body), with
/// joints for the parts that hang on hinges. The root part (a seat, else a foundation, else the first part) is what
/// the construct is: anything cut off from it falls away as a separate wreck. Anchored (a working foundation) or free.
/// Arrays are sized once (<see cref="MaxParts"/>), so a construct allocates nothing as it is simulated or edited.
/// </summary>
public sealed partial class Construct
{
    public const int MaxParts = 96;
    public const int MaxLinks = MaxParts * 3;
    public const int MaxJoints = 16; // (16 since the actuator sizes: a 10-joint leg or arm fits)
    public const int MaxPoints = 40; // contact points against the world
    public const int MaxWheels = 12;
    public const int MaxSprings = 12;
    public const int MaxWires = 64;
    public const int MaxSegPoints = 64; // contact points on the parts beyond joints (every segment's, in all)
    public const float SegPointSpacing = 1.0f; // a long part gets points along its edges at most this far apart (no sinking in the middle)
    public const int MaxSegEdges = 48; // the long edges of the parts beyond joints, swept against the world (a post between two points)
    public const float SegEdgeMin = 0.3f; // edges shorter than this aren't swept (the points at their ends do)
    public const int MaxGuns = 8; // milestone 3: guns a construct
    public const int BoxesPerGun = 4; // ammo boxes a gun's feed chain reaches (the nearest first)

    public ushort Id;
    public readonly CPart[] Parts = new CPart[MaxParts];
    public int PartCount;
    public readonly CLink[] Links = new CLink[MaxLinks];

    /// <summary>
    /// Socket links across a freeform seam (board item fd01): two free structural sockets of parts in different frames that
    /// face each other within <see cref="SeamTolerance"/> / <see cref="SeamAngleDeg"/> count as touching for the drivetrain, the
    /// fuel and the circuit, like a link on the grid. Derived by <see cref="BuildSeamLinks"/> on every rebuild (never saved or sent).
    /// </summary>
    public const int MaxSeamLinks = 48;
    public const float SeamTolerance = 0.02f, SeamAngleDeg = 3f;
    public readonly CLink[] SeamLinks = new CLink[MaxSeamLinks];
    public int SeamCount;
    public int LinkCount;
    public readonly CJoint[] Joints = new CJoint[MaxJoints];
    public int JointCount;
    public readonly CWire[] Wires = new CWire[MaxWires];
    public int WireCount;
    public int Root;
    public bool Wreck; // cut off from a root (no seat or foundation of its own)
    public byte Owner = 255; // the player who built it (set down its first part, or spawned its blueprint); 255: nobody's (the map's). A sentry's kills are theirs (milestone 3)
    public bool Damaged; // a part of it has been wrecked (broken off or stopped working): disabled, so anyone may salvage it
    public double IdleSince = -1; // server: a wreck's last time with a player near or salvaging it (debris expiry; -1: not yet looked at)
    public bool Anchored; // a working foundation holds it to the ground (or it is pinned)
    public bool Pinned; // set down "anchored to the ground" (creative's free placement toggle): held still without a foundation
    public bool Frozen; // the physics gun froze it where it is (ConstructGrab.cs): held still like an anchored one until grabbed or unfrozen
    public byte FrozenBy = 255; // ...by this player (unfreeze-all frees only your own; 255: nobody's now)
    // The physics gun holding it (server; ConstructGrab.cs): the player, the grab point (body frame), where the beam wants
    // that point and the body's turn, and how hard it may pull. 255: nobody holds it (the physics then never looks further).
    public byte GrabbedBy = 255;
    public Vector3 GrabLocal, GrabTarget;
    public Quaternion GrabRot = Quaternion.Identity;
    public float GrabAccel;
    public bool Asleep;
    public int SleepTicks;
    // Cargo volumes (decision 21; ConstructCargo.cs). Carried: this body rides kinematically in a frozen cargo volume of
    // construct CarriedBy (0: free), at CarryPos / CarryRot in the frame of that volume part's segment (CarrySeg); the
    // server finds the volume again by its part tag. A carrier counts what it carries in Cargo (body frame) for its mass.
    public ushort CarriedBy;
    public ushort CarryTag;
    public byte CarryPart, CarrySeg;
    public Vector3 CarryPos;
    public Quaternion CarryRot = Quaternion.Identity;
    public readonly CCargoMass[] Cargo = new CCargoMass[ConstructCargo.MaxPerConstruct];
    public int CargoCount;
    public float CargoMass; // what Cargo adds up to (kg)

    // Pose: the body frame's origin and turn in the world.
    public Vector3 Pos;
    public Quaternion Rot = Quaternion.Identity;
    public Vector3 PrevPos;
    public Quaternion PrevRot = Quaternion.Identity;
    public Vector3 Vel, AngVel; // the centre of mass's velocity; angular velocity (world)
    public Vector3 Accel; // server: the centre of mass's acceleration over the last physics step, smoothed a little (a crash throws riders off; ConstructRide.cs)
    // Server: the recoil of this tick's shots (board item gr01; ConstructPhysics.AddRecoil), one entry a segment, put into
    // the next physics step (the guns step first). Made with its guns (BuildGuns), sized for every gun on a segment of its own.
    internal CRecoil[]? Recoils;
    internal int RecoilCount;
    internal Vector3 VelBefore, VelThen; // (its velocity at this step's start, and at the one before)

    // Mass properties (rebuilt with the caches)
    public float Mass, InvMass;
    public Vector3 Com; // body frame
    internal CStore[]? StorePool; // the world's stores (ConstructWorld.Create): a bin's load is read from its store, never kept twice
    public float LoadMass; // what the bins hold (kg), in Mass as of the last mass build (ConstructWorld.LoadStep)
    internal double LoadKey; // that load weighted by part, so one that moved between two bins of this body shows as a change
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
    public bool HasVolumeStore; // a Storage Volume: its box is drawn with the cargo volumes' (ConstructRenderCargo.cs)
    public bool HasRecycler; // a Recycler part: the recycler step looks at it (ConstructRecycler.cs)
    public bool HasGatherer; // a gathering module: the gatherer step looks at it (ConstructGatherer.cs)
    public bool HasRefiner; // a production machine: the refining step looks at it (ConstructRefining.cs)
    public bool HasAssembler; // an Assembler: the assembling step looks at it (ConstructAssembling.cs)
    public bool HasBin; // a part with storage.bulk: the load step looks at it (ConstructWorld.LoadStep)
    public bool EngineWired; // a signal runs the engine (it drives without a driver)
    public float EngineSignal, BrakeSignal;
    public uint SigHash, SigSent; // the signal parts' values (hashed) now and as last sent to clients
    public readonly ulong[] RiderBits = new ulong[MaxJoints + 1]; // players (id & 63) standing on each segment this tick
    public readonly float[] SegMass = new float[MaxJoints + 1]; // mass carried by each segment, its own and every segment hanging on it
    public readonly Vector3[] SegCom = new Vector3[MaxJoints + 1]; // its centre (rest frame)
    public const float RiderMass = 85f; // a player standing on a joint's segment, for its load
    // Riders on a body on spring feet (board item cs03; server): this tick's count, their feet added up, and the landings' impulse (N s, down).
    public int SpringRiders;
    public Vector3 SpringRiderSum;
    public float SpringLanding;
    public readonly CSpring[] Springs = new CSpring[MaxSprings];
    public int SpringCount;

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
    internal int SimIndex; // its index in the physics' list this tick (ConstructPhysics: the awake bodies and what they touch)
    internal uint PoseHot; // big worlds: the pose tick until which it counts as moving for the network (ConstructNet.PoseMovers)
    internal int QueryMark, GridMark, SimMark; // the broadphase's query and grid stamps, and the physics' (ConstructBroadphase.cs)

    // Weapons (milestone 3; ConstructWeapons.cs): every gun's numbers and feed, rebuilt with the caches
    public readonly CGun[] Guns = new CGun[MaxGuns];
    public int GunCount;
    public readonly byte[] GunBoxes = new byte[MaxGuns * BoxesPerGun];
    public const int MaxMuzzles = 16; // muzzles a gun with a splitter fires from (one a barrel)
    public readonly Vector3[] GunMuzzles = new Vector3[MaxGuns * MaxMuzzles]; // gun k's barrel tips (rest frame; CGun.MuzzleCount of them, a splitter's barrels in turn)
    public readonly byte[] GunMuzzlePellets = new byte[MaxGuns * MaxMuzzles]; // rounds each of those muzzles puts out a shot
    public int Burning; // parts on fire (the server's burn step skips a body with none)

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
    public uint StoreNetKey, StoreNetSeen; // a hash of its stores and chutes, and the last one the beacons took in (ConstructBeacons.cs)

    // Client: the pose it is drawn at (blended towards the last one received).
    public Vector3 NetPos, NetFromPos;
    public Quaternion NetRot = Quaternion.Identity, NetFromRot = Quaternion.Identity;
    public double NetAt;
    public uint NetTick; // the server tick of the pose it is going to (a CPose's, or a CBody Full's since protocol 101)

    // Client (cj01): the last few poses heard with their server ticks, oldest first, so the body is drawn between them on the
    // server's own clock (ConstructWorld.NetRenderTick) rather than on when the packets happened to arrive. Eight (24 ticks)
    // cover the drawn tick's delay with a pose either side of its segment to spare: four did not once the delay grew with
    // the snapshots' jitter (a loaded machine), so the tick fell in the first segment (its tangent one-sided, changing
    // when the oldest pose went) or before them all (held still, then jumping), and the body kinked at 1,500 m/s2.
    public const int NetHistMax = 8;
    public readonly uint[] NetHistTick = new uint[NetHistMax];
    public readonly Vector3[] NetHistPos = new Vector3[NetHistMax];
    public readonly Quaternion[] NetHistRot = new Quaternion[NetHistMax];
    public int NetHistCount;

    /// <summary>Client: the body was put somewhere directly (spawned, teleported, rebased): the poses heard before don't apply.</summary>
    public void NetHistReset()
    {
        NetHistCount = 0;
        NetHistVer++;
        NetRawRt = NetRawRt0 = double.NaN; // (and the smoothing below starts again: a put body is drawn where it was put)
    }

    // Client: the history's version (bumped on every pose heard or reset), and the smoothing of the curve's changes
    // (ConstructNet.Smooth): the raw sample last frame (at NetRawRt, with its velocity) and the offset still being eased
    // out, in position and as a rotation vector, each with its own rate of change.
    public int NetHistVer, NetRawVer;
    public double NetRawRt = double.NaN, NetRawRt0 = double.NaN;
    public Vector3 NetRawPos, NetRawVel, NetRawSpin, NetOffPos, NetOffVel, NetOffRot, NetOffSpin;
    public Quaternion NetRawRot = Quaternion.Identity;

    /// <summary>Client: a pose heard for server tick <paramref name="tick"/> (a newer one than the last, or the same tick again).</summary>
    public void NetHistAdd(uint tick, Vector3 pos, Quaternion rot)
    {
        var n = NetHistCount;
        if (n > 0 && tick <= NetHistTick[n - 1])
        {
            if (tick < NetHistTick[n - 1])
                return;
            n--; // (the same tick again: replaced)
        }
        else if (n == NetHistMax)
        {
            for (var i = 1; i < n; i++)
            {
                NetHistTick[i - 1] = NetHistTick[i];
                NetHistPos[i - 1] = NetHistPos[i];
                NetHistRot[i - 1] = NetHistRot[i];
            }
            n--;
        }
        NetHistTick[n] = tick;
        NetHistPos[n] = pos;
        NetHistRot[n] = rot;
        NetHistCount = n + 1;
        NetHistVer++;
    }
    public uint DigestRound; // client: the last body digest that listed it (protocol 101; ConstructNet.ReadDigest)
    public byte PoseRepeats; // server: poses still to send after it went still (its final pose goes more than once: a lost packet is not the last word)

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
        if (j.Freeform)
        {
            q = pq * j.FixRot;
            t = pt + Vector3.Transform(j.FixPos, pq);
            return;
        }
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
    /// Segment <paramref name="seg"/>'s rest pose: rest frame -> body frame with every moving joint at 0 (only the freeform
    /// welds up its chain turn it; identity for the body and a plain joint's segment).
    /// </summary>
    public void RestPose(int seg, out Quaternion q, out Vector3 t)
    {
        q = Quaternion.Identity;
        t = Vector3.Zero;
        if (seg <= 0 || seg > JointCount)
            return;
        ref var j = ref Joints[seg - 1];
        RestPose(j.ParentSeg, out var pq, out var pt);
        if (!j.Freeform)
        {
            q = pq;
            t = pt;
            return;
        }
        q = pq * j.FixRot;
        t = pt + Vector3.Transform(j.FixPos, pq);
    }

    /// <summary>The frame a segment's parts are placed in: the nearest freeform segment up its chain (itself included), else 0 (the body's).</summary>
    public int FrameOf(int seg)
    {
        for (var guard = 0; seg > 0 && seg <= JointCount && guard <= MaxJoints; guard++)
        {
            if (Joints[seg - 1].Freeform)
                return seg;
            seg = Joints[seg - 1].ParentSeg;
        }
        return 0;
    }

    /// <summary>Is part <paramref name="part"/> on segment <paramref name="seg"/> or on a segment hanging from it (a joint's leaf side)? Every part is under segment 0.</summary>
    public bool Under(int part, int seg)
    {
        var s = (uint)part < (uint)PartCount ? Parts[part].Seg : 0;
        for (var guard = 0; s > 0 && s <= JointCount && guard <= MaxJoints; guard++)
        {
            if (s == seg)
                return true;
            s = Joints[s - 1].ParentSeg;
        }
        return seg == 0;
    }

    /// <summary>Does segment <paramref name="seg"/> carry any part (on it, or on a segment hanging from it)?</summary>
    public bool SegCarries(int seg)
    {
        for (var i = 0; i < PartCount; i++)
            if (Under(i, seg))
                return true;
        return false;
    }

    /// <summary>Is segment <paramref name="seg"/> rigid with the body (the body itself, or only freeform welds up its chain)?</summary>
    public bool Rigid(int seg)
    {
        for (var guard = 0; seg > 0 && guard <= MaxJoints; guard++)
        {
            if (seg > JointCount || !Joints[seg - 1].Freeform)
                return false;
            seg = Joints[seg - 1].ParentSeg;
        }
        return true;
    }

    /// <summary>
    /// The segment whose joint really moves <paramref name="seg"/>: <paramref name="seg"/> itself unless it is a freeform
    /// (fixed-pose) segment, then the nearest segment up its chain that is not one; 0 for the body. Two parts with the same
    /// <c>MoveSeg</c> are one rigid piece for every system (drive shafts, the drive line, fuel, power; board item fd01).
    /// </summary>
    public int MoveSeg(int seg)
    {
        for (var guard = 0; guard <= MaxJoints; guard++)
        {
            if (seg <= 0 || seg > JointCount)
                return 0;
            if (!Joints[seg - 1].Freeform)
                return seg;
            seg = Joints[seg - 1].ParentSeg;
        }
        return 0;
    }

    /// <summary>A point of segment <paramref name="seg"/>'s frame in the body's rest frame (every moving joint at 0; only freeform welds turn it).</summary>
    public Vector3 ToRest(int seg, Vector3 p)
    {
        if (FreeformCount == 0 || seg <= 0)
            return p;
        RestPose(seg, out var q, out var t);
        return t + Vector3.Transform(p, q);
    }

    /// <summary>A direction of segment <paramref name="seg"/>'s frame in the body's rest frame.</summary>
    public Vector3 DirToRest(int seg, Vector3 d)
    {
        if (FreeformCount == 0 || seg <= 0)
            return d;
        RestPose(seg, out var q, out _);
        return Vector3.Transform(d, q);
    }

    /// <summary>
    /// The seam links (<see cref="SeamLinks"/>): free structural sockets of parts in different freeform frames that meet within
    /// the tolerance, face to face. Nothing allocates.
    /// </summary>
    private void BuildSeamLinks()
    {
        SeamCount = 0;
        if (FreeformCount == 0)
            return;
        Span<Vector3> mn = stackalloc Vector3[MaxParts];
        Span<Vector3> mx = stackalloc Vector3[MaxParts];
        Span<int> frame = stackalloc int[MaxParts];
        for (var i = 0; i < PartCount; i++)
        {
            RestBounds(i, out mn[i], out mx[i]);
            frame[i] = FrameOf(Parts[i].Seg);
        }
        var cosLimit = -MathF.Cos(SeamAngleDeg * MathF.PI / 180f);
        var pad = new Vector3(SeamTolerance + 0.01f);
        for (var i = 0; i < PartCount; i++)
        {
            if (Parts[i].Broken)
                continue;
            for (var j = i + 1; j < PartCount; j++)
            {
                if (frame[i] == frame[j] || Parts[j].Broken)
                    continue;
                if (mn[i].X - pad.X > mx[j].X || mn[j].X - pad.X > mx[i].X || mn[i].Y - pad.Y > mx[j].Y || mn[j].Y - pad.Y > mx[i].Y
                    || mn[i].Z - pad.Z > mx[j].Z || mn[j].Z - pad.Z > mx[i].Z)
                    continue;
                var di = Parts[i].D;
                var dj = Parts[j].D;
                for (var s = 0; s < di.Sockets.Length; s++)
                {
                    if (di.Sockets[s].Kind != SocketKind.Struct || di.HasJoint && s == di.LeafIndex || SocketUsed(i, s) || SeamUsed(i, s))
                        continue;
                    SocketAt(i, s, out var ai, out var ri);
                    var pa = ToRest(Parts[i].Seg, ai);
                    var da = DirToRest(Parts[i].Seg, ConstructParts.DirVector(ri));
                    for (var t = 0; t < dj.Sockets.Length; t++)
                    {
                        if (dj.Sockets[t].Kind != SocketKind.Struct || dj.HasJoint && t == dj.LeafIndex || SocketUsed(j, t) || SeamUsed(j, t))
                            continue;
                        SocketAt(j, t, out var aj, out var rj);
                        if (Vector3.DistanceSquared(pa, ToRest(Parts[j].Seg, aj)) > SeamTolerance * SeamTolerance
                            || Vector3.Dot(da, DirToRest(Parts[j].Seg, ConstructParts.DirVector(rj))) > cosLimit)
                            continue;
                        if (SeamCount < MaxSeamLinks)
                            SeamLinks[SeamCount++] = new CLink { A = (byte)i, SA = (byte)s, B = (byte)j, SB = (byte)t };
                        break;
                    }
                }
            }
        }
    }

    private bool SeamUsed(int part, int socket)
    {
        for (var k = 0; k < SeamCount; k++)
            if (SeamLinks[k].A == part && SeamLinks[k].SA == socket || SeamLinks[k].B == part && SeamLinks[k].SB == socket)
                return true;
        return false;
    }

    /// <summary>Freeform welds on this construct (rebuilt with the caches); joints that move (the rest).</summary>
    public int FreeformCount, MovingJoints;

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
            if (Joints[j].Hinge == part && !Joints[j].Freeform) // (a freeform weld's anchor is any part, not a joint part)
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

    /// <summary>The gun on part <paramref name="part"/> (its index in <see cref="Guns"/>), or -1.</summary>
    public int GunOf(int part)
    {
        if ((uint)part < (uint)PartCount && MergeOf[part] != 0)
            part = Merges[MergeOf[part] - 1].Lead; // (a merged receiver's member: its lead is the gun)
        for (var k = 0; k < GunCount; k++)
            if (Guns[k].Part == part)
                return k;
        return -1;
    }

    /// <summary>Does the gunner in seat <paramref name="part"/> aim a gun's joints with their look (the view then stays where they look as the turret turns)?</summary>
    public bool SeatAims(int part)
    {
        for (var k = 0; k < GunCount; k++)
            if (Guns[k].Seat == part && Guns[k].AimJoints > 0)
                return true;
        return false;
    }

    /// <summary>What a seat is for (the seated pose, the look limits): the drive seat, a seat on a moving joint (a gunner's, on a turntable or hinge), or a passenger's.</summary>
    public SeatKind SeatKindOf(int part) =>
        part < 0 || part >= PartCount ? SeatKind.None : part == DriveSeat ? SeatKind.Driver : !Rigid(Parts[part].Seg) ? SeatKind.Gunner : SeatKind.Passenger;

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
        var k = ScaleOf(i); // (a gun part on a merged receiver: N times bigger)
        centre = p.Pos + Rot24.Apply(p.Rot, box.Center * k);
        half = Rot24.ApplyHalf(p.Rot, box.Half * k);
    }

    /// <summary>Part <paramref name="i"/>'s bounds in the rest frame.</summary>
    public void PartBounds(int i, out Vector3 min, out Vector3 max)
    {
        ref var p = ref Parts[i];
        var d = p.D;
        var k = ScaleOf(i);
        var c = (d.BoundsMin + d.BoundsMax) * 0.5f * k;
        var h = (d.BoundsMax - d.BoundsMin) * 0.5f * k;
        var wc = p.Pos + Rot24.Apply(p.Rot, c);
        var wh = Rot24.ApplyHalf(p.Rot, h);
        min = wc - wh;
        max = wc + wh;
    }

    /// <summary>Socket <paramref name="s"/> of part <paramref name="i"/> in the rest frame: where and which way.</summary>
    public void SocketAt(int i, int s, out Vector3 at, out byte dir)
    {
        ref var p = ref Parts[i];
        var len = p.D.Sockets.Length;
        if (s >= len && MergeLead(i))
        {
            ConstructMerge.CentreSocket(this, Merges[MergeOf[i] - 1], s - len, out at, out dir); // (a merged receiver's centre socket)
            return;
        }
        var sock = p.D.Sockets[s % len];
        at = p.Pos + Rot24.Apply(p.Rot, sock.At * ScaleOf(i));
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
        for (var i = 0; i < PartCount; i++)
            if (Parts[i].D.Core)
                return i;
        return 0;
    }

    /// <summary>A loose part: one part, whole, not a seat or a foundation (can be picked up; wrecked debris can't).</summary>
    public bool Loose => PartCount == 1 && !Parts[0].Broken && Parts[0].D.Role is not (PartRole.Seat or PartRole.Foundation) && !Parts[0].D.Core && !Anchored;

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
        ConstructMerge.Refresh(this); // (merged receivers first: they scale gun parts)
        FreeformCount = MovingJoints = 0;
        for (var j = 0; j < JointCount; j++)
            if (Joints[j].Freeform)
                FreeformCount++;
            else
                MovingJoints++;
        BuildMass();
        BuildSeamLinks();
        ConstructRoutes.Rebuild(this); // (routed links: the logic's wires, the shafts' and cables' edges, before the drive line and the circuit)
        BuildPoints();
        BuildSegPoints();
        Radius = MathF.Max(Radius, JointReach() + 0.5f);
        BuildWheels();
        BuildDrivetrain();
        BuildSegments();
        ConstructPower.Rebuild(this);
        StoreNetKey = ConstructBeacons.KeyOf(this);
        ConstructWeapons.BuildGuns(this);
        Anchored = (Pinned || Frozen) && PartCount > 0;
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

    /// <summary>
    /// The mass, centre of mass and inertia, from the parts, the bins' loads and the cargo, with the reach round the centre
    /// of mass. No structure changes here: a change of load alone comes through <see cref="RefreshMass"/>.
    /// </summary>
    private void BuildMass()
    {
        var oldCom = Com;
        var oldWorldCom = WorldCom;
        Mass = 0;
        LoadMass = 0;
        LoadKey = 0;
        var com = Vector3.Zero;
        LocalMin = new Vector3(float.MaxValue);
        LocalMax = new Vector3(float.MinValue);
        // (the bounds first: a Storage Volume's load sits inside them, ConstructStoreVolume.Usable)
        for (var i = 0; i < PartCount; i++)
        {
            RestBounds(i, out var mn, out var mx);
            LocalMin = Vector3.Min(LocalMin, mn);
            LocalMax = Vector3.Max(LocalMax, mx);
            if (MergeOf[i] != 0)
            {
                ShapeBounds(i, out var smn, out var smx); // (a merged receiver's drawn box reaches further than its members)
                LocalMin = Vector3.Min(LocalMin, smn);
                LocalMax = Vector3.Max(LocalMax, smx);
            }
        }
        for (var i = 0; i < PartCount; i++)
        {
            RestBounds(i, out var mn, out var mx);
            var pm = PartMass(i);
            com += (mn + mx) * 0.5f * pm;
            Mass += pm;
            if (LoadOf(i, out var lm, out var lc, out _))
            {
                com += RestLoad(i, lc) * lm; // (the fill, not the part's middle: a load sits where it is drawn)
                Mass += lm;
                LoadMass += lm;
                LoadKey += LoadKeyOf(i, lm, lc);
            }
        }
        if (PartCount == 0)
        {
            Mass = 1;
            LocalMin = LocalMax = Vector3.Zero;
        }
        for (var k = 0; k < CargoCount && PartCount > 0; k++)
        {
            com += Cargo[k].Centre * Cargo[k].Mass; // (cargo volumes: what a frozen volume carries rides as part of the body)
            Mass += Cargo[k].Mass;
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
            var turned = FreeformCount > 0 && Parts[i].Seg != 0;
            var rq = Quaternion.Identity;
            var rt = Vector3.Zero;
            if (turned)
                RestPose(Parts[i].Seg, out rq, out rt);
            for (var b = 0; b < d.SolidBoxes; b++)
            {
                BoxOf(i, b, out var c, out var h);
                var m = PartMass(i) * (d.Boxes[b].Half.X * d.Boxes[b].Half.Y * d.Boxes[b].Half.Z) / MathF.Max(vol, 1e-9f);
                var s = h * 2;
                if (turned)
                {
                    // A freeform segment's box (board item fw01): turned off the body's axes, its own inertia turns with it.
                    var r2 = rt + Vector3.Transform(c, rq) - Com;
                    TurnedBox(m, s, rq, ref ixx, ref iyy, ref izz, ref ixy, ref ixz, ref iyz);
                    ixx += m * (r2.Y * r2.Y + r2.Z * r2.Z);
                    iyy += m * (r2.X * r2.X + r2.Z * r2.Z);
                    izz += m * (r2.X * r2.X + r2.Y * r2.Y);
                    ixy -= m * r2.X * r2.Y;
                    ixz -= m * r2.X * r2.Z;
                    iyz -= m * r2.Y * r2.Z;
                    continue;
                }
                var r = c - Com;
                ixx += m / 12f * (s.Y * s.Y + s.Z * s.Z) + m * (r.Y * r.Y + r.Z * r.Z);
                iyy += m / 12f * (s.X * s.X + s.Z * s.Z) + m * (r.X * r.X + r.Z * r.Z);
                izz += m / 12f * (s.X * s.X + s.Y * s.Y) + m * (r.X * r.X + r.Y * r.Y);
                ixy -= m * r.X * r.Y;
                ixz -= m * r.X * r.Z;
                iyz -= m * r.Y * r.Z;
            }
            if (LoadOf(i, out var lm, out var lc, out var lh))
            {
                var r = RestLoad(i, lc) - Com;
                var s = lh * 2;
                if (turned)
                    TurnedBox(lm, s, rq, ref ixx, ref iyy, ref izz, ref ixy, ref ixz, ref iyz);
                else
                {
                    ixx += lm / 12f * (s.Y * s.Y + s.Z * s.Z);
                    iyy += lm / 12f * (s.X * s.X + s.Z * s.Z);
                    izz += lm / 12f * (s.X * s.X + s.Y * s.Y);
                }
                ixx += lm * (r.Y * r.Y + r.Z * r.Z);
                iyy += lm * (r.X * r.X + r.Z * r.Z);
                izz += lm * (r.X * r.X + r.Y * r.Y);
                ixy -= lm * r.X * r.Y;
                ixz -= lm * r.X * r.Z;
                iyz -= lm * r.Y * r.Z;
            }
        }
        for (var k = 0; k < CargoCount && PartCount > 0; k++)
        {
            var m = Cargo[k].Mass;
            var s = Cargo[k].Half * 2;
            var r = Cargo[k].Centre - Com;
            ixx += m / 12f * (s.Y * s.Y + s.Z * s.Z) + m * (r.Y * r.Y + r.Z * r.Z);
            iyy += m / 12f * (s.X * s.X + s.Z * s.Z) + m * (r.X * r.X + r.Z * r.Z);
            izz += m / 12f * (s.X * s.X + s.Y * s.Y) + m * (r.X * r.X + r.Y * r.Y);
            ixy -= m * r.X * r.Y;
            ixz -= m * r.X * r.Z;
            iyz -= m * r.Y * r.Z;
        }
        // A little extra so thin builds don't spin like tops.
        var floor = Mass * 0.05f;
        var inertia = new Matrix4x4(ixx + floor, ixy, ixz, 0, ixy, iyy + floor, iyz, 0, ixz, iyz, izz + floor, 0, 0, 0, 0, 1);
        InvInertiaLocal = Matrix4x4.Invert(inertia, out var inv) ? inv : Matrix4x4.Identity;
        // Keep the centre of mass where it was in the world (the parts didn't move; the body origin did not either).
        _ = oldCom;
        _ = oldWorldCom;
        Radius = MathF.Max(Vector3.Distance(Com, LocalMin), Vector3.Distance(Com, LocalMax)) + 0.5f;
    }

    /// <summary>
    /// A change of load alone (ConstructWorld.LoadStep): the mass properties again, without the rest of <see cref="Rebuild"/>.
    /// The pose stays; the velocity stays with the centre of mass. Allocates nothing.
    /// </summary>
    public void RefreshMass()
    {
        BuildMass();
        Radius = MathF.Max(Radius, JointReach() + 0.5f);
        BuildSegMass();
    }

    /// <summary>
    /// What bin part <paramref name="i"/> holds: its weight (Constructs/Recycling.json) and its fill box in the part's
    /// segment frame. A production machine's two bins count as one load: their weights added, at their shared centre, in a
    /// box round both. False when it holds nothing.
    /// </summary>
    public bool LoadOf(int i, out float kg, out Vector3 centre, out Vector3 half)
    {
        kg = 0;
        centre = half = Vector3.Zero;
        var h = Parts[i].Store;
        if (h == 0 || StorePool == null || h >= StorePool.Length || StorePool[h] is not { Live: true } st)
            return false;
        var table = RecyclingTable.Current;
        if (ConstructStorage.FillBox(this, i, st, out centre, out half))
            kg = st.Amount > 0 ? st.Amount * table.WeightOf(st.Material) : st.ItemKg; // (a Storage Volume's parts weigh what they weigh)
        if (ConstructStorage.InputBox(Parts[i], Parts[i].D, st.InTotal, out var ic, out var ih))
        {
            var ik = st.InAmount * table.WeightOf(st.InMaterial) + st.ItemKg; // (a mixed input's materials and an Assembler's parts weigh what they weigh)
            for (var k = 0; k < st.MixN; k++)
                ik += st.MixAmount![k] * table.WeightOf(st.MixMat![k]);
            if (kg <= 0)
            {
                (kg, centre, half) = (ik, ic, ih);
                return true;
            }
            var mid = (centre * kg + ic * ik) / (kg + ik);
            half = Vector3.Max(Vector3.Abs(centre - mid) + half, Vector3.Abs(ic - mid) + ih);
            centre = mid;
            kg += ik;
        }
        return kg > 0;
    }

    /// <summary>Does part <paramref name="i"/>'s store hold anything (as this machine has heard)?</summary>
    public bool StoreHolds(int i)
    {
        var h = Parts[i].Store;
        return h != 0 && StorePool != null && h < StorePool.Length && StorePool[h] is { Live: true, Empty: false };
    }

    /// <summary>A load's share of <see cref="LoadKey"/>: by part, and by where it sits (a machine's batch moves its weight within the part).</summary>
    private static double LoadKeyOf(int i, float kg, Vector3 centre) => (double)kg * (i + 1) + centre.X + centre.Y + centre.Z; // (a double: a million-unit load still shows a unit's change)

    /// <summary><see cref="LoadKey"/> as the bins hold it now.</summary>
    internal double BinLoadKey()
    {
        var key = 0.0;
        for (var i = 0; i < PartCount; i++)
            if (LoadOf(i, out var kg, out var at, out _))
                key += LoadKeyOf(i, kg, at);
        return key;
    }

    /// <summary>A point in part <paramref name="i"/>'s segment frame in the body frame at rest (a freeform weld's fixed pose turned in).</summary>
    private Vector3 RestLoad(int i, Vector3 at)
    {
        if (FreeformCount == 0 || Parts[i].Seg == 0)
            return at;
        RestPose(Parts[i].Seg, out var q, out var t);
        return t + Vector3.Transform(at, q);
    }

    private bool _fuelSet, _chargeSet;

    /// <summary>A piece that fell away keeps no fuel or charge (salvage takes the parts, not what was in them).</summary>
    public void Empty()
    {
        Fuel = 0;
        Charge = 0;
        _fuelSet = _chargeSet = true;
    }

    /// <summary>Part <paramref name="i"/>'s bounds in the body frame at rest (a freeform segment's part turned into it: the box round it).</summary>
    public void RestBounds(int i, out Vector3 min, out Vector3 max)
    {
        PartBounds(i, out min, out max);
        var seg = Parts[i].Seg;
        if (FreeformCount == 0 || seg == 0 || FrameOf(seg) == 0)
            return;
        RestPose(seg, out var q, out var t);
        var c = t + Vector3.Transform((min + max) * 0.5f, q);
        var h = (max - min) * 0.5f;
        var m = Matrix4x4.CreateFromQuaternion(q);
        var e = new Vector3(
            MathF.Abs(m.M11) * h.X + MathF.Abs(m.M21) * h.Y + MathF.Abs(m.M31) * h.Z,
            MathF.Abs(m.M12) * h.X + MathF.Abs(m.M22) * h.Y + MathF.Abs(m.M32) * h.Z,
            MathF.Abs(m.M13) * h.X + MathF.Abs(m.M23) * h.Y + MathF.Abs(m.M33) * h.Z);
        min = c - e;
        max = c + e;
    }

    /// <summary>Adds a solid box's own inertia (mass <paramref name="m"/>, size <paramref name="s"/>) turned by <paramref name="q"/> into the body frame (R D R^T).</summary>
    private static void TurnedBox(float m, Vector3 s, Quaternion q, ref float xx, ref float yy, ref float zz, ref float xy, ref float xz, ref float yz)
    {
        var dx = m / 12f * (s.Y * s.Y + s.Z * s.Z);
        var dy = m / 12f * (s.X * s.X + s.Z * s.Z);
        var dz = m / 12f * (s.X * s.X + s.Y * s.Y);
        var r = Matrix4x4.CreateFromQuaternion(q); // (row k: where local axis k points in the body frame)
        xx += r.M11 * r.M11 * dx + r.M21 * r.M21 * dy + r.M31 * r.M31 * dz;
        yy += r.M12 * r.M12 * dx + r.M22 * r.M22 * dy + r.M32 * r.M32 * dz;
        zz += r.M13 * r.M13 * dx + r.M23 * r.M23 * dy + r.M33 * r.M33 * dz;
        xy += r.M11 * r.M12 * dx + r.M21 * r.M22 * dy + r.M31 * r.M32 * dz;
        xz += r.M11 * r.M13 * dx + r.M21 * r.M23 * dy + r.M31 * r.M33 * dz;
        yz += r.M12 * r.M13 * dx + r.M22 * r.M23 * dy + r.M32 * r.M33 * dz;
    }

    /// <summary>Each segment's carried mass and its centre (its parts and every segment hanging on it); whether logic runs.</summary>
    private void BuildSegments()
    {
        BuildSegMass();
        HasLogic = WireCount > 0;
        HasRecycler = false;
        HasGatherer = false;
        HasRefiner = false;
        HasAssembler = false;
        HasBin = false;
        HasVolumeStore = false;
        for (var i = 0; i < PartCount; i++)
        {
            var d = Parts[i].D;
            if (d.IsVolumeStore)
                HasVolumeStore = true;
            if (d.IsRecycler)
                HasRecycler = true;
            if (d.IsGatherer)
                HasGatherer = true;
            if (d.IsRefiner)
                HasRefiner = true;
            if (d.IsAssembler)
                HasAssembler = true;
            if (d.IsBin || d.IsAssembler)
                HasBin = true;
            if (d.Role == PartRole.Logic || d.Role == PartRole.Seat && WireCount > 0 || d.Role == PartRole.Hose && d.HoseCarry == HoseCarry.Data) // (a data lead's reel and socket: board item wl01)
                HasLogic = true;
        }
        // The motors come from their parts (a client's copy too: the wire carries only the type).
        for (var j = 0; j < JointCount; j++)
        {
            ref var jt = ref Joints[j];
            // (not a freeform weld anchored on a joint part: it stays fixed. Taking that part's joint type made it a second
            // joint about the body's origin, dropping its fixed pose: the piece jumped through to the other side, board item wh01)
            if (!jt.Freeform && jt.Hinge < PartCount && Parts[jt.Hinge].D is { HasJoint: true } jd)
            {
                jt.Type = jd.JointType;
                // The part's settings (the configure tool): its limits, speed and strength, inside what the part allows.
                ref var hp = ref Parts[jt.Hinge];
                var unit = jd.JointType == JointType.Piston ? 1f : MathF.PI / 180f;
                jt.Speed = ConstructConfig.Has(jd, CfgKey.Speed) ? MathF.Max(0, ConstructConfig.Get(hp, jd, CfgKey.Speed, 0) * unit) : jd.MotorSpeed;
                jt.Strength = ConstructConfig.Get(hp, jd, CfgKey.Strength, jd.MotorStrength);
                jt.Give = jd.JointGive;
                jt.SideLoad = jd.SideLoad;
                if (jd.JointLimited && (ConstructConfig.Has(jd, CfgKey.Min) || ConstructConfig.Has(jd, CfgKey.Max)))
                {
                    var a = Math.Clamp(ConstructConfig.Get(hp, jd, CfgKey.Min, jd.JointMin / unit) * unit, jd.JointMin, jd.JointMax);
                    var b = Math.Clamp(ConstructConfig.Get(hp, jd, CfgKey.Max, jd.JointMax / unit) * unit, jd.JointMin, jd.JointMax);
                    jt.Min = MathF.Min(a, b);
                    jt.Max = MathF.Max(MathF.Max(a, b), jt.Min + (jd.JointType == JointType.Piston ? 0.02f : 0.02f));
                    if (jd.JointType == JointType.Piston)
                        jt.Max = MathF.Min(jt.Max, jd.JointMax);
                    jt.Angle = Math.Clamp(jt.Angle, jt.Min, jt.Max);
                    jt.PrevAngle = Math.Clamp(jt.PrevAngle, jt.Min, jt.Max);
                }
            }
        }
    }

    /// <summary>Each segment's carried mass and centre (a bin's load included), and the inertia a joint's push turns.</summary>
    private void BuildSegMass()
    {
        for (var s = 0; s <= MaxJoints; s++)
        {
            SegMass[s] = 0;
            SegCom[s] = Vector3.Zero;
        }
        for (var i = 0; i < PartCount; i++)
        {
            PartBounds(i, out var mn, out var mx);
            AddSegMass(i, (mn + mx) * 0.5f, PartMass(i));
            if (LoadOf(i, out var kg, out var at, out _))
                AddSegMass(i, at, kg);
        }
        for (var s = 0; s <= JointCount; s++)
            if (SegMass[s] > 0)
                SegCom[s] /= SegMass[s];
        BaseInertia();
    }

    /// <summary>Mass <paramref name="m"/> at <paramref name="at"/> in part <paramref name="i"/>'s segment frame, on its segment and every segment it hangs from.</summary>
    private void AddSegMass(int i, Vector3 at, float m)
    {
        var c = at;
        var cb = c; // (with freeform welds: the body frame at rest, turned into each segment's own frame below)
        if (FreeformCount > 0 && Parts[i].Seg != 0)
        {
            RestPose(Parts[i].Seg, out var pq, out var pt);
            cb = pt + Vector3.Transform(c, pq);
        }
        for (int s = Parts[i].Seg, guard = 0; guard <= MaxJoints; guard++)
        {
            if (s < 0 || s > JointCount)
                break;
            if (FreeformCount > 0)
            {
                RestPose(s, out var sq, out var st);
                c = Vector3.Transform(cb - st, Quaternion.Conjugate(sq));
            }
            SegMass[s] += m;
            SegCom[s] += c * m;
            if (s == 0)
                break;
            s = Joints[s - 1].ParentSeg;
        }
    }

    /// <summary>
    /// The inertia a joint's push turns (milestone 3: the reaction): the parts of the body itself as boxes, and what hangs
    /// on each of its joints as that mass at the joint's pivot (it swings freely about its own joint, so only its weight
    /// rides with the body). Without joints it is the body's own.
    /// </summary>
    private void BaseInertia()
    {
        if (MovingJoints == 0)
        {
            InvInertiaBaseLocal = InvInertiaLocal; // (freeform welds are rigid: the whole body turns)
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
            var turned = Parts[i].Seg != 0;
            if (turned && !(FreeformCount > 0 && Rigid(Parts[i].Seg)))
                continue;
            var d = Parts[i].D;
            var vol = 0f;
            for (var b = 0; b < d.SolidBoxes; b++)
                vol += d.Boxes[b].Half.X * d.Boxes[b].Half.Y * d.Boxes[b].Half.Z;
            var rq = Quaternion.Identity;
            var rt = Vector3.Zero;
            if (turned)
                RestPose(Parts[i].Seg, out rq, out rt);
            for (var b = 0; b < d.SolidBoxes; b++)
            {
                BoxOf(i, b, out var c, out var h);
                var m = PartMass(i) * (d.Boxes[b].Half.X * d.Boxes[b].Half.Y * d.Boxes[b].Half.Z) / MathF.Max(vol, 1e-9f);
                if (turned)
                {
                    TurnedBox(m, h * 2, rq, ref ixx, ref iyy, ref izz, ref ixy, ref ixz, ref iyz); // (a freeform segment rides with the body)
                    Point(m, rt + Vector3.Transform(c, rq) - Com, Vector3.Zero);
                }
                else
                    Point(m, c - Com, h * 2);
            }
            if (LoadOf(i, out var kg, out var lc, out var lh))
                Point(kg, RestLoad(i, lc) - Com, lh * 2); // (a bin's load, a solid box: turned or not, close enough here)
        }
        for (var j = 0; j < JointCount; j++)
        {
            if (Joints[j].Freeform)
                continue;
            if (Joints[j].ParentSeg == 0)
                Point(SegMass[j + 1], Joints[j].Pivot - Com, Vector3.Zero);
            else if (FreeformCount > 0 && Rigid(Joints[j].ParentSeg))
            {
                RestPose(Joints[j].ParentSeg, out var pq, out var pt);
                Point(SegMass[j + 1], pt + Vector3.Transform(Joints[j].Pivot, pq) - Com, Vector3.Zero);
            }
        }
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
            var seg = Parts[i].Seg;
            var turned = seg != 0 && FreeformCount > 0 && Rigid(seg); // (a freeform segment's corners are the body's own hull: board item fw01)
            if (seg != 0 && !turned || IsRaycastWheel(i))
                continue;
            ShapeBounds(i, out var mn, out var mx); // (a merged receiver's members: the one merged box's corners)
            var rq = Quaternion.Identity;
            var rt = Vector3.Zero;
            if (turned)
                RestPose(seg, out rq, out rt);
            for (var c = 0; c < 8; c++)
            {
                var p = new Vector3((c & 1) != 0 ? mx.X : mn.X, (c & 2) != 0 ? mx.Y : mn.Y, (c & 4) != 0 ? mx.Z : mn.Z);
                if (turned)
                    p = rt + Vector3.Transform(p, rq);
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
            if (seg == 0 || IsRaycastWheel(i) || FreeformCount > 0 && Rigid(seg))
                continue; // (a freeform segment's corners are hull points)
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
                if (j.Freeform)
                    from = j.FixPos + Vector3.Transform(from, j.FixRot); // (into its parent's frame: a weld adds no reach)
                else if (j.Slides)
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

    /// <summary>Does fuel reach engine <paramref name="engine"/>: an intact pipe from a working tank, or a working tank linked to it directly or through working (legacy) fuel line parts?</summary>
    private bool FuelReaches(int engine)
    {
        if (ConstructRoutes.PipeFeeds(this, engine))
            return true;
        Span<bool> seen = stackalloc bool[MaxParts];
        Span<byte> queue = stackalloc byte[MaxParts];
        int head = 0, tail = 0;
        queue[tail++] = (byte)engine;
        seen[engine] = true;
        while (head < tail)
        {
            int at = queue[head++];
            for (var k = 0; k < LinkCount + SeamCount + JointCount; k++)
            {
                int a, b;
                if (k < LinkCount)
                    (a, b) = (Links[k].A, Links[k].B);
                else if (k < LinkCount + SeamCount)
                    (a, b) = (SeamLinks[k - LinkCount].A, SeamLinks[k - LinkCount].B); // (sockets that meet across a freeform seam, and its bead: board item fd01)
                else if (Joints[k - LinkCount - SeamCount] is { Freeform: true, Cut: false } fj)
                    (a, b) = (fj.Hinge, fj.Child);
                else
                    continue;
                int other;
                if (a == at)
                    other = b;
                else if (b == at)
                    other = a;
                else
                    continue;
                if (other >= PartCount || seen[other] || !Parts[other].Working)
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
