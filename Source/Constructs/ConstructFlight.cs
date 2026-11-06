using System.Numerics;

namespace CallOfSlop;

/// <summary>What its keys mean (the "keys" setting; a saved number, so add at the end). A stabilizer works out what it can do by itself; this is only how the seat asks.</summary>
public enum FlightPattern : byte
{
    Auto, // from what it found: wings make it a plane, nozzles a rocket, anything else a rotorcraft
    Rotorcraft, // anything that hangs on its thrust or floats: quadcopters, helicopters, hover platforms, airships
    Plane,
    Rocket,
}

/// <summary>What the autopilot holds (the "apMode" setting; a saved number, so add at the end).</summary>
public enum FlightMode : byte
{
    Altitude,
    Heading,
    Hover,
    Waypoint,
    Home,
}

/// <summary>One Flight Stabilizer's loops (with the construct's caches; the state goes with its part by index). The first on a construct flies it; the others stand by.</summary>
public struct CStab
{
    public ushort Part;
    public ushort WiredIn; // bit k: something is wired into input port k
    public ushort Refit; // ticks to the next look at what has died or moved
    public bool Fitted; // the mix stands for the construct as it is

    public Vector3 Inertia; // kg m2 about its right, its forward, its up
    public float Spool; // s: the slowest thruster it works
    public float UpForce, FrontForce; // N: all it can push up, and on, now
    public Vector3 Auth; // a joint gimbal on the pitch, yaw or roll output: N m for a unit of output at full thrust, by its loops' axes (pitch, roll, yaw); 0: not known
    public Vector3 Most; // the most of a unit such a joint is asked to go
    public bool Repulsors; // most of its lift is repulsors: height is the gap under them
    public float RepulsorRange;

    // The loops
    public Vector3 Integral; // pitch, roll, yaw
    public Vector2 Lean; // radians: the lean a steady drift has worked out (forward, right)
    public float AltIntegral;
    public float HoldAlt, HoldHead;
    public Vector2 HoldPos; // x, z
    public bool AltHeld, HeadHeld, PosHeld, ApWas;
    public float AltCfg, HeadCfg; // the settings when last read: a change takes over from the pilot's nudges
    public float Landed; // s: asked down and not going down
    public float HomeWas; // the "set home" input last tick
    public byte NumberTick; // wired numbers reach the settings twice a second, not every tick
}

public sealed partial class Construct
{
    public CStab[] Stabs = [];
    public int StabCount;
    public FlightEffectors? Effectors; // what the first stabilizer found (made when there is one)
    public bool StabLive; // a stabilizer is at work this tick: it stays awake
    public bool StabOwns; // this tick the stabilizer has set its effectors (a wrecked one sets nothing: they go back to their wires and the driver)
    public Vector3 GyroNow; // N m, world: what the gyro asks for this tick
    public bool HasFlightDiag, DiagSent; // a Flight Diagnostic Panel is on it; clients were last told something non-zero
    public float StabBurn = -1, StabVent; // what it asks of the burners (below 0: nothing) and the cells' vents
}

/// <summary>
/// Flight Stabilizers (docs/sandbox/CONSTRUCTS.md, "Flight control"), server, in the logic step. The first on a construct flies it, from its seat (linked, else the construct's) or a drone's
/// receiver. Sensors are built in: its own frame is what it reads, so it is mounted level and facing forward. When the structure changes it looks over the construct for everything it can
/// command (<see cref="ConstructEffectors"/>) and what a unit of each does from where it sits. Each tick: what is asked (<see cref="Ask"/>), what the autopilot takes over, an attitude and a
/// rate loop per axis, a height loop, then a demand of force and torque shared among them all by least squares inside their limits (<see cref="Mix"/>). So four jets in any spacing, six,
/// a lopsided eight, or one rotor and a tail rotor fly on the same part with no wiring. Its sixteen named outputs still carry the same demands for anything wired by hand. Nothing allocates.
/// </summary>
public static partial class ConstructFlight
{
    public const int MaxStabs = 8;
    private const int RefitTicks = World.TickRate / 2;

    public const int InSeat = 0, InThrottle = 1, InPitch = 2, InYaw = 3, InRoll = 4, InLift = 5, InAutopilot = 6, InMode = 7, InAltitude = 8, InHeading = 9, InWaypointX = 10, InWaypointZ = 11, InSetHome = 12;
    public static readonly string[] InPorts = ["seat", "throttle", "pitch", "yaw", "roll", "lift", "autopilot", "mode", "altitude", "heading", "waypoint x", "waypoint z", "set home"]; // (add at the end: a link names its port by index)

    public const int OutThrottle = 0, OutReverse = 1, OutLift = 2, OutVent = 3, OutPitch = 4, OutYaw = 5, OutRoll = 6, OutLeftAileron = 7, OutRightAileron = 8, OutFlaps = 9, OutFrontLeft = 10, OutFrontRight = 11, OutRearLeft = 12, OutRearRight = 13, OutStrafeLeft = 14, OutStrafeRight = 15, OutFire = 16;
    public static readonly string[] OutPorts = ["throttle", "reverse", "lift", "vent", "pitch", "yaw", "roll", "left aileron", "right aileron", "flaps", "front left", "front right", "rear left", "rear right", "strafe left", "strafe right", "fire"]; // (add at the end, up to LogicBlocks.PortWidth, which also sizes every wide part: a link names its port by index)

    /// <summary>The limits while their settings are 0: the most it tilts (degrees), turns (degrees a second), climbs and goes (m/s).</summary>
    private static readonly Vector4 RotorLimits = new(30, 90, 8, 30), ShipLimits = new(5, 12, 3, 15), PlaneLimits = new(45, 30, 20, 120), RocketLimits = new(25, 30, 30, 300);

    private const float LoopPlane = 1.5f, LoopRocket = 1.5f, LoopRotor = 2.5f; // (1/s: how fast an attitude comes back where nothing it knows answers it)
    private const float PlaneSpeed = 40f; // m/s: a surface's gain falls with the square of the speed past this
    private const float MinCos = 0.5f; // past this lean the height loop stops adding thrust for it
    private const float MinMargin = 0.05f; // rad: the lean left to a craft with no thrust to spare
    private const float GimbalMost = 0.35f; // rad: past this a gimbal's joint isn't asked to go
    private const int MixSteps = 3; // rounds of putting back what the limits took off

    /// <summary>With the caches: the stabilizers on it, what is wired into each, and (the first's) what it can command.</summary>
    public static void Build(Construct c)
    {
        var n = 0;
        for (var i = 0; i < c.PartCount; i++)
            if (c.Parts[i].D.Logic == CLogic.FlightStabilizer)
                n++;
        n = Math.Min(n, MaxStabs);
        if (n == 0)
        {
            c.StabCount = 0;
            c.StabLive = false;
            c.StabBurn = -1;
            return;
        }
        Span<CStab> keep = stackalloc CStab[MaxStabs];
        var had = Math.Min(c.StabCount, MaxStabs);
        for (var k = 0; k < had; k++)
            keep[k] = c.Stabs[k];
        if (c.Stabs.Length < n)
            c.Stabs = new CStab[n];
        c.StabCount = 0;
        for (var i = 0; i < c.PartCount && c.StabCount < n; i++)
        {
            if (c.Parts[i].D.Logic != CLogic.FlightStabilizer || !c.Parts[i].Working)
                continue;
            ref var s = ref c.Stabs[c.StabCount];
            s = default;
            for (var k = 0; k < had; k++)
                if (keep[k].Part == i)
                    s = keep[k];
            s.Part = (ushort)i;
            s.Fitted = false;
            s.WiredIn = 0;
            for (var w = 0; w < c.WireCount; w++)
                if (c.Wires[w].To == i)
                    s.WiredIn |= (ushort)(1 << Math.Min((int)c.Wires[w].ToPort, 15));
            c.StabCount++;
        }
        c.HasFlightDiag = false;
        for (var i = 0; i < c.PartCount && !c.HasFlightDiag; i++)
            foreach (var pd in c.Parts[i].D.Panels)
                c.HasFlightDiag |= pd.Source == "flight.diagnostic";
        c.Effectors ??= new FlightEffectors();
        ConstructEffectors.Scan(c, c.Effectors);
        if (c.StabCount > 0)
            Refit(c, ref c.Stabs[0], c.Effectors, KeysOf(c, c.Parts[c.Stabs[0].Part], c.Parts[c.Stabs[0].Part].D));
    }

    private static int IndexOf(Construct c, int part)
    {
        for (var k = 0; k < c.StabCount; k++)
            if (c.Stabs[k].Part == part)
                return k;
        return -1;
    }

    /// <summary>Is a stabilizer's output linked to anything?</summary>
    public static bool Reaches(Construct c, int part)
    {
        for (var k = 0; k < c.WireCount; k++)
            if (c.Wires[k].From == part)
                return true;
        return false;
    }

    /// <summary>The seat a stabilizer takes its orders from: linked to it, else the construct's first (a helm before it).</summary>
    public static int SeatFor(Construct c, int part)
    {
        var seat = ConstructModules.SeatOf(c, part);
        if (seat >= 0)
            return seat;
        for (var i = 0; i < c.PartCount; i++)
            if (c.Parts[i].D.Role == PartRole.Seat && c.Parts[i].Working && (seat < 0 || c.Parts[i].D.Helm && !c.Parts[seat].D.Helm))
                seat = i;
        return seat;
    }

    private static float Rad(float degrees) => degrees * MathF.PI / 180f;

    private static float Wrap(float a)
    {
        a %= MathF.Tau;
        return a > MathF.PI ? a - MathF.Tau : a < -MathF.PI ? a + MathF.Tau : a;
    }

    private static float Setting(in CPart p, ConstructPartDef d, CfgKey k, float limit)
    {
        var v = ConstructConfig.Get(p, d, k, 0);
        return v > 0 ? v : limit;
    }

    /// <summary>What the driver's HUD says the keys do.</summary>
    public static string KeyHint(Construct c)
    {
        if (c.StabCount == 0)
            return SandboxKeys.Fly;
        ref var p = ref c.Parts[c.Stabs[0].Part];
        return KeysOf(c, p, p.D) switch { FlightPattern.Plane => SandboxKeys.FlyPlane, FlightPattern.Rocket => SandboxKeys.FlyRocket, _ => SandboxKeys.FlyRotor };
    }

    /// <summary>The keys it answers to: what the setting says, or on Auto what it found.</summary>
    public static FlightPattern KeysOf(Construct c, in CPart p, ConstructPartDef d)
    {
        var set = (FlightPattern)Math.Clamp((int)ConstructConfig.Get(p, d, CfgKey.Keys, 0), 0, 3);
        if (set != FlightPattern.Auto)
            return set;
        var f = c.Effectors;
        return f == null ? FlightPattern.Rotorcraft : f.Winged(Quaternion.Conjugate(Rot24.Quat(p.Rot))) ? FlightPattern.Plane : f.Gimbals > 0 ? FlightPattern.Rocket : FlightPattern.Rotorcraft;
    }

    /// <summary>
    /// The mix, for the construct as it is: what a unit of each effector does in the stabilizer's frame, each row on one scale, and the least squares share of each row, A^T (A A^T)^-1. A row nothing
    /// much answers is left out (four thrusters all pushing up have no yaw: the gyro has it). Runs when the structure changes, or something died or woke, or the centre of mass moved.
    /// </summary>
    private static void Refit(Construct c, ref CStab s, FlightEffectors f, FlightPattern pattern)
    {
        ref var me = ref c.Parts[s.Part];
        var frame = Rot24.Quat(me.Rot);
        var inv = Quaternion.Conjugate(frame);
        s.Fitted = true;
        s.Spool = 0;
        s.Auth = default;
        s.Most = Vector3.One;
        s.Repulsors = false;
        f.ComFitted = c.Com;
        var repulsorForce = 0f;
        var upForce = 0f;
        var n = f.Count;
        var a = f.A;
        ulong aliveA = 0, aliveB = 0;
        for (var k = 0; k < n; k++)
        {
            ref var e = ref f.Items[k];
            var live = e.Auth > 0 || e.Kind is EffectorKind.Gimbal or EffectorKind.Cyclic or EffectorKind.Surface; // (a nozzle has no authority until it is lit, a surface until it moves, and they are no less there)
            if (live)
            {
                if (k < 64)
                    aliveA |= 1ul << k;
                else
                    aliveB |= 1ul << (k - 64);
            }
            var force = Vector3.Transform(e.Force, inv);
            var torque = Vector3.Transform(Vector3.Cross(e.At - c.Com, e.Force) + e.Torque, inv);
            var at = k * FlightEffectors.Rows;
            var share = live ? 1f : 0f;
            var turns = e.Kind is EffectorKind.Gimbal or EffectorKind.Cyclic ? 0f : share; // (a nozzle or a disc tipping is for turning: the push it leans is small, and a craft isn't flown on it)
            a[at + FlightEffectors.Forward] = -force.Z * turns;
            a[at + FlightEffectors.Up] = force.Y * turns;
            a[at + FlightEffectors.Right] = force.X * turns;
            a[at + FlightEffectors.Pitch] = torque.X * share;
            a[at + FlightEffectors.Roll] = -torque.Z * share;
            a[at + FlightEffectors.Yaw] = -torque.Y * share;
            if (e.Kind is EffectorKind.Thruster or EffectorKind.Repulsor or EffectorKind.Rotor && live)
            {
                upForce += MathF.Max(0, force.Y);
                s.Spool = MathF.Max(s.Spool, c.Parts[e.Part].D.Rotor is { } rotor ? rotor.Heli ? 0 : rotor.Spool : c.Parts[e.Part].D.ThrustSpool); // (a helicopter's blades take a while to wind up; then its pitch answers at once)
            }
            if (e.Kind == EffectorKind.Repulsor && live)
            {
                repulsorForce += MathF.Max(0, force.Y);
                s.RepulsorRange = c.Parts[e.Part].D.RepulsorRange;
            }
        }
        f.AliveA = aliveA;
        f.AliveB = aliveB;
        s.Repulsors = repulsorForce > 0.5f * upForce && upForce > 0;
        for (var w = 0; w < c.WireCount; w++)
        {
            ref var wire = ref c.Wires[w];
            if (wire.From == s.Part && wire.FromPort is OutPitch or OutYaw or OutRoll && wire.To < c.PartCount && c.Parts[wire.To].D.ThrustGimbal <= 0)
                GimbalAuth(c, ref s, wire.To, wire.FromPort, pattern, inv);
        }
        Solve(f, pattern, c.CellCount > 0);
        s.UpForce = 0;
        s.FrontForce = 0;
        for (var k = 0; k < n; k++)
        {
            s.UpForce += MathF.Max(0, a[k * FlightEffectors.Rows + FlightEffectors.Up]) * f.Scale[FlightEffectors.Up];
            s.FrontForce += MathF.Max(0, a[k * FlightEffectors.Rows + FlightEffectors.Forward]) * f.Scale[FlightEffectors.Forward];
        }
        Vector3 unit;
        unit.X = Inertia(c, Vector3.Transform(Vector3.UnitX, frame));
        unit.Y = Inertia(c, Vector3.Transform(-Vector3.UnitZ, frame));
        unit.Z = Inertia(c, Vector3.Transform(Vector3.UnitY, frame));
        s.Inertia = unit;
    }

    /// <summary>The rows on one scale, the rows that count, and P = A^T (A A^T + a little)^-1.</summary>
    private static void Solve(FlightEffectors f, FlightPattern pattern, bool floats)
    {
        const int R = FlightEffectors.Rows;
        var n = f.Count;
        var a = f.A;
        Span<float> scale = stackalloc float[R];
        scale.Clear();
        for (var k = 0; k < n * R; k++)
            scale[k % R] = MathF.Max(scale[k % R], MathF.Abs(a[k]));
        Span<int> ways = stackalloc int[R];
        ways.Clear();
        for (var k = 0; k < n * R; k++)
            ways[k % R] |= a[k] > 0 ? 1 : a[k] < 0 ? 2 : 0;
        var rotor = pattern == FlightPattern.Rotorcraft;
        var forces = MathF.Max(scale[0], MathF.Max(scale[1], scale[2]));
        var torques = MathF.Max(scale[3], MathF.Max(scale[4], scale[5]));
        f.Can = 0;
        for (var r = 0; r < R; r++)
        {
            var off = scale[r] <= 0 || scale[r] < 0.05f * (r < 3 ? forces : torques);
            off |= pattern == FlightPattern.Plane && r is FlightEffectors.Up or FlightEffectors.Right || pattern == FlightPattern.Rocket && r is FlightEffectors.Forward or FlightEffectors.Right;
            if (rotor && r is FlightEffectors.Forward or FlightEffectors.Right && !(ways[r] == 3 || floats))
                off = true; // (a tail rotor pushes one way: that is a turn, not a way of going sideways, and the craft leans instead)
            if (off)
                scale[r] = 0;
            else
                f.Can |= 1 << r;
            f.Scale[r] = scale[r];
        }
        for (var k = 0; k < n; k++)
            for (var r = 0; r < R; r++)
                a[k * R + r] = scale[r] > 0 ? a[k * R + r] / scale[r] : 0;
        Span<float> m = stackalloc float[R * R * 2];
        for (var r = 0; r < R; r++)
        {
            for (var q = 0; q < R; q++)
            {
                var dot = 0f;
                for (var k = 0; k < n; k++)
                    dot += a[k * R + r] * a[k * R + q];
                m[r * 2 * R + q] = dot;
                m[r * 2 * R + R + q] = r == q ? 1 : 0;
            }
            m[r * 2 * R + r] += scale[r] > 0 ? 1e-3f : 1f; // (a little give: a line of thrusters can't be twisted across it)
        }
        for (var col = 0; col < R; col++)
        {
            var best = col;
            for (var r = col + 1; r < R; r++)
                if (MathF.Abs(m[r * 2 * R + col]) > MathF.Abs(m[best * 2 * R + col]))
                    best = r;
            if (best != col)
                for (var q = 0; q < 2 * R; q++)
                    (m[col * 2 * R + q], m[best * 2 * R + q]) = (m[best * 2 * R + q], m[col * 2 * R + q]);
            var pivot = m[col * 2 * R + col];
            if (MathF.Abs(pivot) < 1e-9f)
                continue;
            for (var q = 0; q < 2 * R; q++)
                m[col * 2 * R + q] /= pivot;
            for (var r = 0; r < R; r++)
            {
                if (r == col)
                    continue;
                var x = m[r * 2 * R + col];
                for (var q = 0; q < 2 * R; q++)
                    m[r * 2 * R + q] -= x * m[col * 2 * R + q];
            }
        }
        for (var k = 0; k < n; k++)
            for (var r = 0; r < R; r++)
            {
                var share = 0f;
                for (var q = 0; q < R; q++)
                    share += a[k * R + q] * m[q * 2 * R + R + r];
                f.P[k * R + r] = scale[r] > 0 ? share : 0;
            }
    }

    /// <summary>
    /// A demand (N and N m, on <see cref="FlightEffectors"/>' rows) shared among the effectors: the mix, then a few rounds of putting back what each one's limits took off, turning first,
    /// so a thruster that can only push and one that has run out both leave the others to make it up. <paramref name="weights"/>: how much each row counts when they can't all be met.
    /// </summary>
    private static void Mix(Construct c, FlightEffectors f, ReadOnlySpan<float> demand, ReadOnlySpan<float> weights)
    {
        const int R = FlightEffectors.Rows;
        var n = f.Count;
        Span<float> want = stackalloc float[R];
        Span<float> left = stackalloc float[R];
        for (var r = 0; r < R; r++)
            want[r] = f.Scale[r] > 0 ? demand[r] / f.Scale[r] : 0;
        var u = f.U;
        for (var k = 0; k < n; k++)
        {
            ref var e = ref f.Items[k];
            var has = MathF.Max(e.Auth, 0);
            f.Low[k] = e.Lo * has;
            f.High[k] = e.Hi * has;
            u[k] = 0;
        }
        for (var step = 0; step <= MixSteps; step++)
        {
            want.CopyTo(left);
            for (var k = 0; k < n; k++)
                if (u[k] != 0)
                    for (var r = 0; r < R; r++)
                        left[r] -= f.A[k * R + r] * u[k];
            for (var r = 0; r < R; r++)
                left[r] *= step == 0 ? 1 : weights[r];
            for (var k = 0; k < n; k++)
            {
                var add = 0f;
                for (var r = 0; r < R; r++)
                    add += f.P[k * R + r] * left[r];
                u[k] = Math.Clamp(u[k] + add, f.Low[k], f.High[k]);
            }
        }
        for (var k = 0; k < n; k++)
        {
            ref var e = ref f.Items[k];
            e.Cmd = e.Auth > 0.001f ? Math.Clamp(u[k] / e.Auth, e.Lo, e.Hi) : 0;
            ConstructEffectors.Sources[e.Source].Command(c, e, e.Cmd);
        }
    }

    private static void Idle(Construct c, FlightEffectors f)
    {
        for (var k = 0; k < f.Count; k++)
        {
            ref var e = ref f.Items[k];
            if (e.Cmd != 0 || e.Kind != EffectorKind.Gyro)
                ConstructEffectors.Sources[e.Source].Command(c, e, 0);
            e.Cmd = 0;
        }
    }

    /// <summary>The moment of inertia about a body-frame axis, as the physics will answer a torque about it.</summary>
    private static float Inertia(Construct c, Vector3 axis) => 1f / MathF.Max(1e-12f, Vector3.Dot(axis, Construct.Mul(c.InvInertiaLocal, axis)));

    /// <summary>A joint on the pitch, yaw or roll port that carries a thruster: what a whole unit of that output does to the craft, signed, so it can't be fitted backwards.</summary>
    private static void GimbalAuth(Construct c, ref CStab s, int part, int port, FlightPattern pattern, Quaternion inv)
    {
        for (var j = 0; j < c.JointCount; j++)
        {
            ref var jt = ref c.Joints[j];
            if (jt.DrivePart != part || jt.Slides)
                continue;
            var range = MathF.Max(MathF.Abs(jt.Min), MathF.Abs(jt.Max));
            if (range < 1e-3f)
                continue;
            var most = MathF.Min(1, GimbalMost / range);
            var axis = Axis(pattern, port);
            s.Most = axis == 0 ? s.Most with { X = most } : axis == 1 ? s.Most with { Y = most } : s.Most with { Z = most };
            for (var k = 0; k < c.ThrusterCount; k++)
            {
                ref var t = ref c.Thrusters[k];
                if (!OnJoint(c, t.Seg, j))
                    continue;
                var force = c.Parts[t.Part].D.ThrustForce;
                // The push turning about the joint: its change with the angle, and the arm from the centre of mass.
                var dir = Vector3.Cross(jt.Axis, t.Dir);
                var torque = Vector3.Transform(Vector3.Cross(t.At - c.Com, dir * force) * jt.Max, inv);
                var along = axis == 0 ? torque.X : axis == 1 ? -torque.Z : -torque.Y;
                s.Auth = axis == 0 ? s.Auth with { X = s.Auth.X + along } : axis == 1 ? s.Auth with { Y = s.Auth.Y + along } : s.Auth with { Z = s.Auth.Z + along };
            }
        }
    }

    private static bool OnJoint(Construct c, int seg, int joint)
    {
        for (var guard = 0; guard <= Construct.MaxJoints && seg > 0 && seg <= c.JointCount; guard++)
        {
            if (seg - 1 == joint)
                return true;
            seg = c.Joints[seg - 1].ParentSeg;
        }
        return false;
    }

    /// <summary>Which loop an output carries: 0 pitch, 1 roll, 2 yaw. A rocket's gimbal leans it on "pitch" and "yaw", and "roll" spins it about its length.</summary>
    private static int Axis(FlightPattern pattern, int port) =>
        port == OutPitch ? 0 : pattern == FlightPattern.Rocket ? port == OutYaw ? 1 : 2 : port == OutRoll ? 1 : 2;

    /// <summary>One tick of a Flight Stabilizer. Returns its first output; the others go to its ports.</summary>
    public static float Step(ConstructWorld world, Construct c, int part, ref CPart p, ConstructPartDef d, int from, float dt)
    {
        var first = FlyStep(world, c, part, ref p, d, from, dt);
        if (c.PortBase.Length > 0 && c.PortBase[part] >= 0)
        {
            // The pilot's fire key, whatever the stabilizer is doing: a rack or a gun on it works on the ground and with the engine off too.
            var seat = from >= 0 && from < c.PartCount && c.Parts[from].D.Role == PartRole.Seat ? from : c.DriveSeat;
            c.PortOut[c.PortBase[part] + OutFire] = seat >= 0 && seat < c.PartCount && (c.Parts[seat].Keys & ConstructParts.KeyFire) != 0 ? 1 : 0;
        }
        return first;
    }

    private static float FlyStep(ConstructWorld world, Construct c, int part, ref CPart p, ConstructPartDef d, int from, float dt)
    {
        var k = IndexOf(c, part);
        if (c.PortBase.Length == 0 || c.PortBase[part] < 0)
            return 0;
        var output = c.PortOut.AsSpan(c.PortBase[part], LogicBlocks.PortWidth);
        if (k != 0 || c.Effectors is not { } f)
        {
            output.Clear(); // (the first on a construct flies it: ConstructFlight.Problem says so)
            return 0;
        }
        c.StabLive = false;
        c.GyroNow = Vector3.Zero;
        c.StabOwns = true;
        c.StabBurn = -1;
        c.StabVent = 0;
        ref var s = ref c.Stabs[0];
        var input = c.PortIn.AsSpan(c.PortBase[part], LogicBlocks.PortWidth);
        var seat = from >= 0 && from < c.PartCount && c.Parts[from].D.Role == PartRole.Seat ? from : c.DriveSeat;
        var keys = seat >= 0 && seat < c.PartCount ? c.Parts[seat].Keys : (ushort)0;
        var manned = (keys & CPart.Occupied) != 0;
        p.Count = (ushort)(manned ? seat + 1 : 0);
        var pattern = KeysOf(c, p, d);
        var autopilot = Autopilot(world, c, ref s, ref p, d, input);
        var ask = Ask(pattern, keys, input, ConstructConfig.Get(p, d, CfgKey.InvertPitch, 0) >= 0.5f);
        if ((s.WiredIn & 1 << InSetHome) != 0)
        {
            if (input[InSetHome] >= 0.5f && s.HomeWas < 0.5f)
                SetHome(world, c, part);
            s.HomeWas = input[InSetHome];
        }
        // Armed: at work. It stays armed with nobody at the controls: a craft in the air isn't dropped by its pilot getting up.
        var armed = (p.State & 1) != 0;
        var flies = pattern != FlightPattern.Rotorcraft;
        if (flies && ask.Lift != 0)
            p.Mem = Math.Clamp(p.Mem + ask.Lift * 0.5f * dt, 0, 1); // (Shift and Ctrl work a throttle that stays where it is left)
        var wants = autopilot || (flies ? p.Mem > 0 || ask.Throttle > 0 : ask.Lift > 0 || ask.Forward != 0 && c.CellCount > 0);
        if (wants && !armed)
        {
            armed = true;
            SetHome(world, c, part); // (home is where it took off from, until something says otherwise)
            s.AltHeld = s.HeadHeld = s.PosHeld = false;
            s.AltIntegral = 0;
            s.Integral = default;
            s.Landed = 0;
        }
        var height = s.Repulsors && !flies ? c.RepulsorGap : ConstructPhysics.HoldHeight(c.WorldCom);
        var up = ConstructPhysics.HoldUp(c.WorldCom);
        var climb = Vector3.Dot(c.Vel, up);
        if (!flies && !autopilot)
        {
            // Asked down and not going down: it has landed, and lets go.
            s.Landed = ask.Lift < 0 && climb > -0.3f ? s.Landed + dt : 0;
            if (s.Landed > 0.6f)
                armed = false;
        }
        if (flies && !autopilot && p.Mem <= 0)
            armed = false;
        p.State = (byte)(armed ? p.State | 1 : p.State & ~1);
        if (!armed && !(manned && flies))
        {
            output.Clear();
            Idle(c, f);
            s.Integral = default;
            s.AltIntegral = 0;
            s.ApWas = false;
            return 0;
        }
        c.StabLive |= armed;
        if (armed)
            c.Wake();
        // What each has now; what died or woke, or a shifted load, wants a new mix.
        var stale = !s.Fitted;
        var look = s.Refit == 0;
        s.Refit = (ushort)(look ? RefitTicks : s.Refit - 1);
        for (var e = 0; e < f.Count; e++)
        {
            ref var it = ref f.Items[e];
            it.Auth = ConstructEffectors.Sources[it.Source].Authority(c, it);
            if (look && it.Kind is not (EffectorKind.Gimbal or EffectorKind.Cyclic or EffectorKind.Surface))
                stale |= (it.Auth > 0) != ((e < 64 ? f.AliveA >> e : f.AliveB >> (e - 64)) % 2 != 0);
        }
        if (stale || look && Vector3.DistanceSquared(c.Com, f.ComFitted) > 0.04f)
            Refit(c, ref s, f, pattern);
        Fly(world, c, ref s, f, ref p, d, pattern, in ask, autopilot, armed, seat, height, climb, up, output, dt);
        p.Out1 = output[1];
        return output[0];
    }

    /// <summary>The axes asked of it this tick, by its keys, with what is wired in added.</summary>
    private struct Asked
    {
        public float Pitch, Roll, Yaw, Lift, Throttle, Forward, Side; // -1..1: nose up, right wing down, nose right, up, the main engines; forward and right over the ground
    }

    private static Asked Ask(FlightPattern pattern, ushort keys, ReadOnlySpan<float> input, bool invert)
    {
        var ws = ((keys & (ushort)Buttons.Forward) != 0 ? 1f : 0f) - ((keys & (ushort)Buttons.Back) != 0 ? 1f : 0f);
        if (invert)
            ws = -ws;
        var ad = ((keys & (ushort)Buttons.Right) != 0 ? 1f : 0f) - ((keys & (ushort)Buttons.Left) != 0 ? 1f : 0f);
        var qe = ConstructEdits.RollOf(keys);
        var lift = ((keys & (ushort)Buttons.Sprint) != 0 ? 1f : 0f) - ((keys & (ushort)Buttons.Crouch) != 0 ? 1f : 0f);
        var a = default(Asked);
        a.Lift = Math.Clamp(lift + input[InLift], -1, 1);
        a.Yaw = Math.Clamp(ad + input[InYaw], -1, 1);
        a.Pitch = Math.Clamp(input[InPitch], -1, 1);
        a.Roll = Math.Clamp(input[InRoll], -1, 1);
        a.Throttle = Math.Clamp(input[InThrottle], -1, 1);
        switch (pattern)
        {
            case FlightPattern.Plane:
                a.Pitch = Math.Clamp(a.Pitch - ws, -1, 1); // (W: the nose goes down)
                a.Roll = Math.Clamp(a.Roll + ad, -1, 1); // (A and D bank it, and the rudder follows; Q and E are the rudder)
                a.Yaw = Math.Clamp(input[InYaw] + qe, -1, 1);
                break;
            case FlightPattern.Rocket:
                a.Pitch = Math.Clamp(a.Pitch - ws, -1, 1);
                a.Roll = Math.Clamp(a.Roll + ad, -1, 1); // (A and D lean it; Q and E spin it)
                a.Yaw = Math.Clamp(input[InYaw] + qe, -1, 1);
                break;
            default:
                a.Forward = Math.Clamp(ws + a.Throttle - a.Pitch, -1, 1); // (W: forward, by a push if it has one, else it leans into it)
                a.Side = Math.Clamp(qe + a.Roll, -1, 1);
                a.Pitch = a.Roll = 0;
                break;
        }
        return a;
    }

    /// <summary>Its wired inputs on the settings; true while the autopilot is on. A change a wire makes goes in the settings, so a panel anywhere shows it.</summary>
    private static bool Autopilot(ConstructWorld world, Construct c, ref CStab s, ref CPart p, ConstructPartDef d, ReadOnlySpan<float> input)
    {
        var on = ConstructConfig.Get(p, d, CfgKey.Autopilot, 0) >= 0.5f;
        var dirty = false;
        if ((s.WiredIn & 1 << InAutopilot) != 0 && on != input[InAutopilot] >= 0.5f)
        {
            on = !on;
            dirty |= Put(ref p, d, CfgKey.Autopilot, on ? 1 : 0);
        }
        if ((s.WiredIn & 1 << InMode) != 0 && input[InMode] >= 0.5f)
            dirty |= Put(ref p, d, CfgKey.ApMode, MathF.Round(input[InMode]) - 1);
        if (++s.NumberTick >= RefitTicks)
        {
            s.NumberTick = 0;
            if ((s.WiredIn & 1 << InAltitude) != 0)
                dirty |= Put(ref p, d, CfgKey.TargetAlt, input[InAltitude]);
            if ((s.WiredIn & 1 << InHeading) != 0)
                dirty |= Put(ref p, d, CfgKey.TargetHeading, (input[InHeading] % 360 + 360) % 360);
            if ((s.WiredIn & 1 << InWaypointX) != 0)
                dirty |= Put(ref p, d, CfgKey.WaypointX, input[InWaypointX]);
            if ((s.WiredIn & 1 << InWaypointZ) != 0)
                dirty |= Put(ref p, d, CfgKey.WaypointZ, input[InWaypointZ]);
        }
        if (dirty)
            world.StructureDirty.Add(c.Id);
        return on;
    }

    private static bool Put(ref CPart p, ConstructPartDef d, CfgKey k, float v)
    {
        var slot = d.CfgSlot[(int)k];
        return slot >= 0 && ConstructConfig.Set(ref p, d, slot, v);
    }

    /// <summary>Home is here, now.</summary>
    public static void SetHome(ConstructWorld world, Construct c, int part)
    {
        ref var p = ref c.Parts[part];
        var at = c.WorldCom;
        var moved = Put(ref p, p.D, CfgKey.HomeX, at.X) | Put(ref p, p.D, CfgKey.HomeZ, at.Z);
        if (moved)
            world.StructureDirty.Add(c.Id);
    }

    /// <summary>What it reads now, in its own frame: its axes, its attitude against the way up and its turning.</summary>
    private struct Attitude
    {
        public Vector3 Right, Up, Front, FlatFront, FlatRight;
        public float Pitch, Roll, Heading; // rad: nose up, right wing down, clockwise from north
        public Vector3 Rate; // rad/s: nose up, right wing down, nose right
    }

    private static Attitude Read(Construct c, in CPart p, Vector3 up)
    {
        c.WorldFrame(p.Seg, out var q, out _);
        var frame = q * Rot24.Quat(p.Rot);
        var a = default(Attitude);
        a.Right = Vector3.Transform(Vector3.UnitX, frame);
        a.Up = Vector3.Transform(Vector3.UnitY, frame);
        a.Front = Vector3.Transform(-Vector3.UnitZ, frame);
        float fu = Vector3.Dot(a.Front, up), ru = Vector3.Dot(a.Right, up), uu = Vector3.Dot(a.Up, up);
        a.Pitch = MathF.Atan2(fu, MathF.Sqrt(ru * ru + uu * uu));
        a.Roll = MathF.Atan2(-ru, uu);
        var flat = a.Front - up * fu;
        if (flat.LengthSquared() < 0.01f)
            flat = a.Up * (fu > 0 ? -1 : 1) - up * Vector3.Dot(a.Up, up) * (fu > 0 ? -1 : 1); // (nose straight up: its belly gives the heading)
        a.FlatFront = flat.LengthSquared() > 1e-6f ? Vector3.Normalize(flat) : Vector3.UnitZ;
        a.FlatRight = Vector3.Cross(a.FlatFront, up);
        a.Heading = MathF.Atan2(a.FlatFront.X, -a.FlatFront.Z);
        a.Rate = new Vector3(Vector3.Dot(c.AngVel, a.Right), Vector3.Dot(c.AngVel, a.Front), -Vector3.Dot(c.AngVel, a.Up));
        return a;
    }

    private static void Fly(ConstructWorld world, Construct c, ref CStab s, FlightEffectors f, ref CPart p, ConstructPartDef d, FlightPattern pattern, in Asked ask, bool autopilot, bool armed, int seat, float height, float climb, Vector3 up, Span<float> output, float dt)
    {
        var a = Read(c, p, up);
        var rotor = pattern == FlightPattern.Rotorcraft;
        var g = ConstructPhysics.GravityAccel;
        var carried = 1 - c.AntiShare;
        var weight = c.Mass * g * carried;
        var floats = c.CellCount > 0 && c.LiftNow > 0.4f * MathF.Max(1, weight); // (her gas keeps her level, and she goes as a ship goes)
        var lim = pattern == FlightPattern.Plane ? PlaneLimits : pattern == FlightPattern.Rocket ? RocketLimits : floats ? ShipLimits : RotorLimits;
        var maxTilt = Rad(Setting(p, d, CfgKey.MaxTilt, lim.X));
        var maxTurn = Rad(Setting(p, d, CfgKey.MaxTurn, lim.Y));
        var maxClimb = Setting(p, d, CfgKey.MaxClimb, lim.Z);
        var maxSpeed = Setting(p, d, CfgKey.MaxSpeed, lim.W);
        var strength = ConstructConfig.Get(p, d, CfgKey.Strength, 100) / 100f;
        var level = ConstructConfig.Get(p, d, CfgKey.Level, 1) >= 0.5f;
        var mouse = ConstructConfig.Get(p, d, CfgKey.MouseAim, 0) >= 0.5f && seat >= 0 && seat < c.PartCount && (c.Parts[seat].Keys & CPart.Occupied) != 0;
        var trimPitch = Rad(ConstructConfig.Get(p, d, CfgKey.TrimPitch, 0));
        var trimRoll = Rad(ConstructConfig.Get(p, d, CfgKey.TrimRoll, 0));
        if (s.Repulsors)
            maxTilt = MathF.Min(maxTilt, Rad(6)); // (a field pushes along its part: lean far and it slides off what it rides)
        if (rotor && s.UpForce > 0)
            maxTilt = MathF.Min(maxTilt, MathF.Max(MinMargin, MathF.Acos(MathF.Min(1, 1.15f * MathF.Max(0, weight - c.LiftNow) / s.UpForce)))); // (the lean it has the thrust to hold up)
        var spool = s.Spool > 0 ? MathF.Max(0.05f, s.Spool) : 0.4f;
        var knows = (f.Can & (1 << FlightEffectors.Pitch | 1 << FlightEffectors.Roll)) != 0;
        var loop = knows && pattern != FlightPattern.Plane ? MathF.Min(4f, 1.2f / spool) * MathF.Min(strength, 1.5f) // (it knows what its thrusters do: as quick as they follow, and no quicker for the slider)
            : (pattern == FlightPattern.Plane ? LoopPlane : pattern == FlightPattern.Rocket ? LoopRocket : LoopRotor) * strength;
        var ground = MathF.Min(0.8f, 0.4f * loop); // (the speed loop stays well inside the attitude loop, or a big slow craft swings about what it asks)

        // The autopilot: what it holds, and the point it flies to.
        var pos = new Vector2(c.WorldCom.X, c.WorldCom.Z);
        var mode = (FlightMode)Math.Clamp((int)ConstructConfig.Get(p, d, CfgKey.ApMode, 0), 0, 4);
        if (autopilot && !s.ApWas)
        {
            s.AltHeld = s.HeadHeld = false;
            s.PosHeld = false;
            s.AltCfg = s.HeadCfg = float.NaN;
        }
        s.ApWas = autopilot;
        var holdAlt = autopilot && mode != FlightMode.Heading;
        var holdHead = autopilot && mode is FlightMode.Heading or FlightMode.Hover;
        var holdPos = autopilot && mode is FlightMode.Hover or FlightMode.Waypoint or FlightMode.Home;
        var byGap = s.Repulsors && rotor;
        if (autopilot)
        {
            var offset = height - ConstructPhysics.HoldShown(height);
            var alt = ConstructConfig.Get(p, d, CfgKey.TargetAlt, 0);
            if (alt != s.AltCfg)
            {
                s.AltCfg = alt;
                s.HoldAlt = alt > 0 && !byGap ? alt + offset : height;
                s.AltHeld = true;
            }
            var head = ConstructConfig.Get(p, d, CfgKey.TargetHeading, -1);
            if (head != s.HeadCfg)
            {
                s.HeadCfg = head;
                s.HoldHead = head >= 0 ? Rad(head) : a.Heading;
                s.HeadHeld = true;
            }
            s.PosHeld &= mode == FlightMode.Hover;
            if (mode == FlightMode.Hover && !s.PosHeld)
            {
                s.HoldPos = pos;
                s.PosHeld = true;
            }
            else if (mode == FlightMode.Waypoint)
                s.HoldPos = new Vector2(ConstructConfig.Get(p, d, CfgKey.WaypointX, 0), ConstructConfig.Get(p, d, CfgKey.WaypointZ, 0));
            else if (mode == FlightMode.Home)
                s.HoldPos = new Vector2(ConstructConfig.Get(p, d, CfgKey.HomeX, 0), ConstructConfig.Get(p, d, CfgKey.HomeZ, 0));
        }

        // The height: the pilot's hand moves what is held.
        var stopping = MathF.Max(0.5f, g * carried * 0.8f);
        float climbWant;
        if (ask.Lift != 0 && rotor)
        {
            climbWant = ask.Lift * maxClimb;
            s.HoldAlt = height + (climb > 0 ? climb * climb / (2 * stopping) : 0); // (let go, it stops as soon as it can: a craft that hardly weighs can't be pulled down)
            s.AltHeld = true;
            if (byGap && ask.Lift > 0 && height > 0.7f * s.RepulsorRange)
                climbWant = 0; // (its field gives out above this)
        }
        else
        {
            if (!s.AltHeld)
            {
                s.HoldAlt = byGap ? MathF.Max(height, 0.25f * s.RepulsorRange) : height;
                s.AltHeld = true;
            }
            var off = s.HoldAlt - height;
            climbWant = Math.Clamp(0.8f * off, -maxClimb, off > 0 ? maxClimb : maxClimb);
            if (off > 0)
                climbWant = MathF.Min(climbWant, MathF.Sqrt(2 * stopping * off));
        }

        // The way it goes: to a point, by the mouse, or by hand.
        var toward = s.HoldPos - pos;
        var far = toward.Length();
        var course = far > 1 ? MathF.Atan2(toward.X, -toward.Y) : a.Heading;
        var look = default(Vector3);
        var aiming = false;
        if (mouse)
        {
            look = World.Forward(c.Parts[seat].AimYaw, c.Parts[seat].AimPitch);
            var flat = look - up * Vector3.Dot(look, up);
            aiming = flat.LengthSquared() > 1e-4f;
            if (aiming)
                s.HoldHead = MathF.Atan2(flat.X, -flat.Z);
        }
        else if (holdPos && far > (floats ? 25 : 6))
            s.HoldHead = course; // (a ship near the point stops turning for it: she can only go forward, and would hunt round it)
        else if (!s.HeadHeld || ask.Yaw != 0)
        {
            s.HoldHead = a.Heading;
            s.HeadHeld = true;
        }
        var headErr = Wrap(s.HoldHead - a.Heading);

        // Over the ground: the speed each way it is asked to go at.
        var vel = new Vector2(c.Vel.X, c.Vel.Z);
        Vector2 flatFront = new(a.FlatFront.X, a.FlatFront.Z), flatRight = new(a.FlatRight.X, a.FlatRight.Z);
        var vFront = Vector2.Dot(vel, flatFront);
        var vSide = Vector2.Dot(vel, flatRight);
        float wantFront, wantSide;
        if (ask.Forward != 0 || ask.Side != 0 || !holdPos)
        {
            wantFront = ask.Forward * maxSpeed;
            wantSide = ask.Side * maxSpeed;
            if (holdPos && mode == FlightMode.Hover)
                s.HoldPos = pos;
        }
        else
        {
            var wish = far > 0.01f ? toward / far * MathF.Min(0.5f * ground * far, maxSpeed) : Vector2.Zero;
            wantFront = Vector2.Dot(wish, flatFront);
            wantSide = Vector2.Dot(wish, flatRight);
        }

        // What each loop is asked for.
        float tiltPitch = trimPitch, tiltRoll = trimRoll;
        var yawRate = ask.Yaw != 0 ? ask.Yaw * maxTurn : Math.Clamp(1.5f * strength * headErr, -maxTurn, maxTurn);
        var acroPitch = float.NaN;
        var acroRoll = float.NaN;
        var pushes = f.CanDo(FlightEffectors.Forward);
        var slides = f.CanDo(FlightEffectors.Right);
        switch (pattern)
        {
            case FlightPattern.Plane:
            {
                var speed = MathF.Max(c.Vel.Length(), 1f);
                if (holdPos && far < 3 * speed)
                    headErr = Wrap(course + MathF.PI / 2 - a.Heading); // (it can't stop: it goes round the point)
                var turn = holdHead || holdPos || aiming ? Math.Clamp(1.2f * headErr, -maxTilt, maxTilt) : 0f;
                tiltRoll += ask.Roll * maxTilt + turn;
                if (holdAlt)
                    tiltPitch += Math.Clamp(MathF.Asin(Math.Clamp(climbWant / speed, -1, 1)), -maxTilt, maxTilt) + ask.Pitch * maxTilt * 0.5f;
                else
                    tiltPitch += ask.Pitch * maxTilt + (aiming ? Math.Clamp(MathF.Asin(Math.Clamp(Vector3.Dot(look, up), -1, 1)), -maxTilt, maxTilt) : 0);
                yawRate = (speed > 5 ? g * MathF.Tan(Math.Clamp(a.Roll, -1.2f, 1.2f)) / speed : 0) + ask.Yaw * maxTurn; // (the rudder follows the bank)
                if (!level && !holdAlt && !holdHead && !holdPos && !aiming)
                {
                    acroPitch = ask.Pitch * maxTurn;
                    acroRoll = ask.Roll * maxTurn * 2;
                }
                break;
            }
            case FlightPattern.Rocket:
                tiltPitch += ask.Pitch * maxTilt;
                tiltRoll += ask.Roll * maxTilt;
                if (aiming)
                {
                    // The nose (its up) on the look.
                    tiltPitch += Math.Clamp(MathF.Asin(Math.Clamp(Vector3.Dot(look, a.FlatFront), -1, 1)), -maxTilt, maxTilt);
                    tiltRoll += Math.Clamp(MathF.Asin(Math.Clamp(Vector3.Dot(look, a.FlatRight), -1, 1)), -maxTilt, maxTilt);
                }
                else if (holdPos)
                {
                    tiltPitch += -LeanFor(ground * (wantFront - vFront), g, maxTilt);
                    tiltRoll += LeanFor(ground * (wantSide - vSide), g, maxTilt);
                }
                yawRate = ask.Yaw * maxTurn;
                break;
            default:
                if (!level)
                {
                    acroPitch = -ask.Forward * maxTurn;
                    acroRoll = ask.Side * maxTurn;
                }
                else
                {
                    // It leans only along what it has no jet for: a hover platform stays level.
                    // A steady push from something it can't cancel (a tail rotor's side force, a load off the mast, the wind) is worked out slowly, or it would drift for ever.
                    var ease = armed ? 0.1f * ground * dt : 0;
                    if (!pushes && !floats)
                    {
                        s.Lean.X = Math.Clamp(s.Lean.X + ease * (wantFront - vFront), -maxTilt * 0.5f, maxTilt * 0.5f);
                        tiltPitch += -LeanFor(ground * (wantFront - vFront), g, maxTilt) - s.Lean.X;
                    }
                    if (!slides && !floats)
                    {
                        s.Lean.Y = Math.Clamp(s.Lean.Y + ease * (wantSide - vSide), -maxTilt * 0.5f, maxTilt * 0.5f);
                        tiltRoll += LeanFor(ground * (wantSide - vSide), g, maxTilt) + s.Lean.Y;
                    }
                }
                break;
        }

        // Attitude to turning, turning to torque.
        var want = new Vector3(
            !float.IsNaN(acroPitch) ? acroPitch : Math.Clamp(loop * Wrap(tiltPitch - a.Pitch), -maxTurn, maxTurn),
            !float.IsNaN(acroRoll) ? acroRoll : Math.Clamp(loop * Wrap(tiltRoll - a.Roll), -maxTurn, maxTurn),
            yawRate);
        var rateErr = want - a.Rate;
        var torque = default(Vector3); // (N m, for the mix)
        var units = default(Vector3); // (-1..1, for the outputs)
        var kRate = 3f * loop;
        var speedNow = c.Vel.Length();
        var surface = pattern == FlightPattern.Plane ? MathF.Min(1, PlaneSpeed / MathF.Max(speedNow, PlaneSpeed * 0.5f)) : 1f;
        surface *= surface;
        var settling = armed && (c.Vel.LengthSquared() > 0.25f || !float.IsNaN(acroPitch) || pattern != FlightPattern.Plane); // (a plane parked: its surfaces just follow the stick)
        var mainLevel = MathF.Max(0.1f, pattern == FlightPattern.Rocket ? p.Mem : 1f);
        for (var axis = 0; axis < 3; axis++)
        {
            var known = axis == 0 ? s.Inertia.X : axis == 1 ? s.Inertia.Y : s.Inertia.Z;
            var auth = axis == 0 ? s.Auth.X : axis == 1 ? s.Auth.Y : s.Auth.Z;
            var integral = axis == 0 ? s.Integral.X : axis == 1 ? s.Integral.Y : s.Integral.Z;
            var err = axis == 0 ? rateErr.X : axis == 1 ? rateErr.Y : rateErr.Z;
            var accel = kRate * err + integral;
            var t = accel * known;
            var most = axis == 0 ? s.Most.X : axis == 1 ? s.Most.Y : s.Most.Z;
            var unit = auth != 0 ? Math.Clamp(t / (auth * mainLevel), -most, most) : Math.Clamp((0.5f * strength * err + 0.1f * integral) * surface, -1, 1);
            if (settling && MathF.Abs(unit) < most)
                integral = Math.Clamp(integral + 0.6f * loop * err * dt, -2f, 2f);
            if (axis == 0)
            {
                torque.X = t;
                units.X = unit;
                s.Integral.X = integral;
            }
            else if (axis == 1)
            {
                torque.Y = t;
                units.Y = unit;
                s.Integral.Y = integral;
            }
            else
            {
                torque.Z = t;
                units.Z = unit;
                s.Integral.Z = integral;
            }
        }

        if (pattern == FlightPattern.Plane)
        {
            // A stick moves the surface as well as asking for a rate: what the keys do doesn't wait for a loop, and it pulls a tail down on the ground.
            torque.X += ask.Pitch * 0.6f * f.Scale[FlightEffectors.Pitch];
            torque.Y += ask.Roll * 0.6f * f.Scale[FlightEffectors.Roll];
            torque.Z += ask.Yaw * 0.6f * f.Scale[FlightEffectors.Yaw];
        }

        // The height loop.
        var kv = 2.4f * MathF.Min(1.2f, 0.8f / spool) * strength;
        var climbErr = climbWant - climb;
        var lean = MathF.Max(MinCos, Vector3.Dot(a.Up, up));
        var climbAccel = Math.Clamp(kv * climbErr + s.AltIntegral, -0.9f * g, 2f * g);
        var liftForce = MathF.Max(0, (c.Mass * (g * carried + climbAccel) - c.LiftNow) / lean);

        // The demand, and everything it found shares it.
        Span<float> demand = stackalloc float[FlightEffectors.Rows];
        demand.Clear();
        ReadOnlySpan<float> weights = [0.25f, 0.6f, 0.25f, 1, 1, 1];
        demand[FlightEffectors.Pitch] = floats ? 0 : torque.X;
        demand[FlightEffectors.Roll] = floats ? 0 : torque.Y;
        demand[FlightEffectors.Yaw] = torque.Z;
        var forwardPush = 0f;
        var side = 0f;
        if (rotor)
        {
            demand[FlightEffectors.Up] = liftForce;
            forwardPush = pushes ? Math.Clamp(c.Mass * ground * 2 * (wantFront - vFront) / MathF.Max(1, s.FrontForce), -1, 1) : Math.Clamp(ground * (wantFront - vFront) / 3, -1, 1);
            side = Math.Clamp(ground * (wantSide - vSide) / 3, -1, 1);
            if (pushes)
                demand[FlightEffectors.Forward] = c.Mass * ground * 2 * (wantFront - vFront);
            if (slides)
                demand[FlightEffectors.Right] = c.Mass * ground * 2 * (wantSide - vSide);
            if (!f.CanDo(FlightEffectors.Up) || c.BurnerCount + c.CellCount > 0)
                Height(c, ref s, ref p, liftForce, climbErr, height, climb, output, dt);
        }
        else if (pattern == FlightPattern.Rocket)
        {
            if (holdAlt)
                p.Mem = s.UpForce > 0 ? Math.Clamp(liftForce / s.UpForce, 0, 1) : Math.Clamp(p.Mem + climbErr * 0.05f * dt, 0, 1);
            demand[FlightEffectors.Up] = p.Mem * s.UpForce;
        }
        else
            demand[FlightEffectors.Forward] = p.Mem * s.FrontForce;
        Mix(c, f, demand, weights);

        // The same demands on its outputs, for whatever is wired by hand.
        output.Clear();
        var top = rotor ? s.UpForce > 0 ? Math.Clamp(liftForce / s.UpForce, 0, 1) : output[OutLift] : 0;
        if (rotor)
        {
            output[OutThrottle] = MathF.Max(0, forwardPush);
            output[OutReverse] = MathF.Max(0, -forwardPush);
            output[OutStrafeRight] = MathF.Max(0, side);
            output[OutStrafeLeft] = MathF.Max(0, -side);
            output[OutLift] = s.UpForce > 0 ? top : c.StabBurn >= 0 ? c.StabBurn : Math.Clamp(0.5f + climbAccel / (2 * g), 0, 1);
            output[OutVent] = c.StabVent;
            output[OutFrontLeft] = Math.Clamp(output[OutLift] + 0.25f * (units.X + units.Y), 0, 1);
            output[OutFrontRight] = Math.Clamp(output[OutLift] + 0.25f * (units.X - units.Y), 0, 1);
            output[OutRearLeft] = Math.Clamp(output[OutLift] + 0.25f * (units.Y - units.X), 0, 1);
            output[OutRearRight] = Math.Clamp(output[OutLift] - 0.25f * (units.X + units.Y), 0, 1);
        }
        else
        {
            output[OutThrottle] = Math.Clamp(p.Mem + MathF.Max(0, ask.Throttle), 0, 1);
            output[OutReverse] = MathF.Max(0, -ask.Throttle);
            if (pattern == FlightPattern.Plane)
                output[OutFlaps] = Math.Clamp(1 - speedNow / MathF.Max(1, 0.5f * maxSpeed), 0, 1);
        }
        output[OutPitch] = units.X;
        if (pattern == FlightPattern.Rocket)
        {
            output[OutYaw] = units.Y;
            output[OutRoll] = units.Z;
        }
        else
        {
            output[OutYaw] = units.Z;
            output[OutRoll] = units.Y;
            output[OutLeftAileron] = units.Y; // (down, more lift: the right wing goes down)
            output[OutRightAileron] = -units.Y;
        }
        s.AltIntegral = Math.Clamp(s.AltIntegral + 0.3f * kv * climbErr * dt, -0.5f * g, 0.5f * g);
    }

    /// <summary>The lean that gives this acceleration over the ground.</summary>
    private static float LeanFor(float accel, float g, float most) => Math.Clamp(MathF.Atan2(accel, MathF.Max(g, 0.1f)), -most, most);

    /// <summary>Burners and vents: a slow trim, as the air in a bag takes a while to answer.</summary>
    private static void Height(Construct c, ref CStab s, ref CPart p, float force, float climbErr, float height, float climb, Span<float> output, float dt)
    {
        if (c.BurnerCount > 0)
        {
            p.Mem = Math.Clamp(p.Mem + (0.02f * (s.HoldAlt - height) - 0.25f * climb) * dt, 0, 1);
            c.StabBurn = Math.Clamp(p.Mem + 0.03f * (s.HoldAlt - height) - 0.4f * climb, 0, 1);
        }
        if (climbErr < -0.5f && force <= 0 && c.CellCount > 0)
            c.StabVent = Math.Clamp(-0.2f * climbErr, 0, 1);
    }
}
