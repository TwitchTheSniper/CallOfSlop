using System.Numerics;

namespace CallOfSlop;

/// <summary>How a Flight Stabilizer turns what it is asked into its outputs (its "pattern" setting; a saved number, so add at the end).</summary>
public enum FlightPattern : byte
{
    Plane,
    Helicopter,
    Quadcopter,
    Rocket, // gimballed
    Hover, // a platform: it stays level and moves by side jets
    Airship,
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

/// <summary>One Flight Stabilizer's cached set-up and its loops (rebuilt with the construct's caches; the loops' state goes with its part by index).</summary>
public struct CStab
{
    public ushort Part;
    public ushort WiredIn; // bit k: something is wired into input port k
    public ushort Refit; // ticks to the next look at what its outputs reach
    public bool Mapped; // Refit has run since the caches were built

    // What its outputs reach, by the look (Refit): newtons, and the mix: a place in Construct.StabMix
    public int MixAt;
    public bool Mixed; // corner channels reach thrusters
    public float MainForce, LiftForce, CornerForce; // N: thrusters on the throttle port, on the lift port, along the main push on the corner ports
    public float Spool; // s: the slowest thruster it works
    public Vector3 Inertia; // kg m2 about its right, its forward, its up
    public Vector3 Auth; // N m for a whole unit of output at full thrust, by its loops' axes (pitch, roll, yaw); 0: not known, the gain alone
    public Vector3 Most; // the most of a unit a gimbal's joint is asked to go, the same axes
    public float MainLevel; // what the thrusters on the throttle port do now, 0..1

    // The loops
    public Vector3 Integral; // pitch, roll, yaw
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
    public float[] StabMix = []; // four channels by four demands (lift, pitch, roll, yaw) a stabilizer
    public bool StabLive; // a stabilizer is at work this tick: it stays awake
    public Vector3 GyroNow; // N m, world: what the stabilizers' gyros ask for this tick
}

/// <summary>
/// Flight Stabilizers (docs/sandbox/CONSTRUCTS.md, "Flight control"), server, in the logic step. A seat or a drone's receiver is linked in; wires may add to its
/// axes. Sensors are built in: its own frame is what it reads, so it is mounted level and facing forward. Each tick: what is asked (<see cref="Ask"/>), what the
/// autopilot takes over, an attitude and a rate loop per axis, a height loop, and the mix on sixteen named data outputs, which any part's logic input can take, a
/// thruster's throttle among them. Where its outputs reach thrusters it knows their force and place, so nothing there needs tuning: it asks for torque and the mix
/// (<see cref="Refit"/>, a least squares fit) shares it among them. Where they reach something it knows nothing of, the gain is the strength setting. Nothing allocates.
/// </summary>
public static partial class ConstructFlight
{
    public const int MaxStabs = 8;
    private const int RefitTicks = World.TickRate / 2;

    public const int InSeat = 0, InThrottle = 1, InPitch = 2, InYaw = 3, InRoll = 4, InLift = 5, InAutopilot = 6, InMode = 7, InAltitude = 8, InHeading = 9, InWaypointX = 10, InWaypointZ = 11, InSetHome = 12;
    public static readonly string[] InPorts = ["seat", "throttle", "pitch", "yaw", "roll", "lift", "autopilot", "mode", "altitude", "heading", "waypoint x", "waypoint z", "set home"]; // (add at the end: a link names its port by index)

    public const int OutThrottle = 0, OutReverse = 1, OutLift = 2, OutVent = 3, OutPitch = 4, OutYaw = 5, OutRoll = 6, OutLeftAileron = 7, OutRightAileron = 8, OutFlaps = 9, OutFrontLeft = 10, OutFrontRight = 11, OutRearLeft = 12, OutRearRight = 13, OutStrafeLeft = 14, OutStrafeRight = 15;
    public static readonly string[] OutPorts = ["throttle", "reverse", "lift", "vent", "pitch", "yaw", "roll", "left aileron", "right aileron", "flaps", "front left", "front right", "rear left", "rear right", "strafe left", "strafe right"]; // (add at the end, sixteen at most: a link names its port by index)

    /// <summary>A pattern's limits while their settings are 0: the most it tilts (degrees), turns (degrees a second), climbs and goes (m/s).</summary>
    private static readonly Vector4[] Limits =
    [
        new(45, 30, 20, 120), // plane: bank and pitch, turn, climb, speed its flaps go by
        new(20, 60, 6, 40), // helicopter
        new(30, 90, 8, 30), // quadcopter
        new(25, 30, 30, 300), // rocket
        new(0, 45, 4, 15), // hover platform
        new(5, 12, 3, 15), // airship
    ];

    private const float LoopPlane = 1.5f, LoopRotor = 2.5f, LoopRocket = 1.5f, LoopShip = 0.6f; // (1/s: how fast an attitude comes back where the outputs' authority isn't known)
    private const float PlaneSpeed = 40f; // m/s: a surface's gain falls with the square of the speed past this
    private const float MinCos = 0.5f; // past this lean the height loop stops adding thrust for it
    private const float MinMargin = 0.05f; // rad: the lean left to a craft with no thrust to spare
    private const float GimbalMost = 0.35f; // rad: past this a gimbal's joint isn't asked to go

    /// <summary>With the caches: the stabilizers on it, and what is wired into each.</summary>
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
            return;
        }
        Span<CStab> keep = stackalloc CStab[MaxStabs];
        var had = Math.Min(c.StabCount, MaxStabs);
        for (var k = 0; k < had; k++)
            keep[k] = c.Stabs[k];
        if (c.Stabs.Length < n)
            c.Stabs = new CStab[n];
        if (c.StabMix.Length < n * 16)
            c.StabMix = new float[n * 16];
        c.StabCount = 0;
        for (var i = 0; i < c.PartCount && c.StabCount < n; i++)
        {
            if (c.Parts[i].D.Logic != CLogic.FlightStabilizer)
                continue;
            ref var s = ref c.Stabs[c.StabCount];
            s = default;
            for (var k = 0; k < had; k++)
                if (keep[k].Part == i)
                    s = keep[k];
            s.Part = (ushort)i;
            s.MixAt = c.StabCount * 16;
            s.Mapped = false;
            s.WiredIn = 0;
            for (var w = 0; w < c.WireCount; w++)
                if (c.Wires[w].To == i)
                    s.WiredIn |= (ushort)(1 << Math.Min((int)c.Wires[w].ToPort, 15));
            c.StabCount++;
        }
    }

    private static int IndexOf(Construct c, int part)
    {
        for (var k = 0; k < c.StabCount; k++)
            if (c.Stabs[k].Part == part)
                return k;
        return -1;
    }

    /// <summary>Is a stabilizer's output linked to anything? A seat linked into one then drives the construct, as through a Driving Module.</summary>
    public static bool Reaches(Construct c, int part)
    {
        for (var k = 0; k < c.WireCount; k++)
            if (c.Wires[k].From == part)
                return true;
        return false;
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

    /// <summary>
    /// A look at what the outputs reach, twice a second while it works, so a thruster shot off or out of fuel leaves the mix: the force each corner channel has and
    /// where, and the mix, the least squares share of a demand (lift along the way its pattern pushes, then torque about each axis) among them. A demand none of them can
    /// answer (the turn of four thrusters all pushing up) is left out of the fit, and the gyro has it.
    /// </summary>
    public static void Refit(Construct c, ref CStab s, FlightPattern pattern)
    {
        ref var me = ref c.Parts[s.Part];
        var frame = Rot24.Quat(me.Rot);
        var inv = Quaternion.Conjugate(frame);
        s.Refit = RefitTicks;
        s.Mapped = true;
        s.MainForce = s.LiftForce = s.CornerForce = 0;
        s.Spool = 0;
        s.Auth = default;
        s.Most = Vector3.One;
        Span<float> demand = stackalloc float[16]; // (four rows, each of four channels)
        demand.Clear();
        var mainAxis = pattern == FlightPattern.Airship ? -Vector3.UnitZ : Vector3.UnitY;
        var level = 0f;
        var levels = 0;
        for (var w = 0; w < c.WireCount; w++)
        {
            ref var wire = ref c.Wires[w];
            if (wire.From != s.Part || wire.To >= c.PartCount)
                continue;
            var port = wire.FromPort;
            if (port is OutPitch or OutYaw or OutRoll)
            {
                GimbalAuth(c, ref s, wire.To, port, pattern, inv);
                continue;
            }
            if (c.Parts[wire.To].D.Role != PartRole.Thruster || wire.ToPort != 0)
                continue;
            var t = -1;
            for (var k = 0; k < c.ThrusterCount; k++)
                if (c.Thrusters[k].Part == wire.To)
                    t = k;
            if (t < 0 || !ConstructPhysics.ThrusterLive(c, ref c.Thrusters[t], c.Parts[wire.To]))
                continue;
            ref var th = ref c.Thrusters[t];
            var force = c.Parts[wire.To].D.ThrustForce;
            s.Spool = MathF.Max(s.Spool, c.Parts[wire.To].D.ThrustSpool);
            var dir = Vector3.Transform(th.Dir, inv);
            if (port == OutThrottle)
            {
                s.MainForce += force;
                level += th.Level;
                levels++;
            }
            else if (port == OutLift)
                s.LiftForce += force * MathF.Max(0, dir.Y);
            if (port is < OutFrontLeft or > OutRearRight)
                continue;
            var arm = Vector3.Transform(th.At - c.Com, inv);
            var torque = Vector3.Cross(arm, dir * force);
            var ch = port - OutFrontLeft;
            demand[ch] += force * Vector3.Dot(dir, mainAxis);
            s.CornerForce += MathF.Max(0, force * Vector3.Dot(dir, mainAxis));
            demand[4 + ch] += torque.X; // (about its right: nose up)
            demand[8 + ch] += -torque.Z; // (about its forward: right wing down)
            demand[12 + ch] += -torque.Y; // (about its up, the other way: nose right)
        }
        s.MainLevel = levels > 0 ? level / levels : 0;
        s.Mixed = Fit(demand, c.StabMix.AsSpan(s.MixAt, 16));
        Vector3 unit;
        unit.X = Inertia(c, Vector3.Transform(Vector3.UnitX, frame));
        unit.Y = Inertia(c, Vector3.Transform(-Vector3.UnitZ, frame));
        unit.Z = Inertia(c, Vector3.Transform(Vector3.UnitY, frame));
        s.Inertia = unit;
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

    /// <summary>The share each channel takes of each demand: M^T (M M^T)^-1, each row put on one scale, a row nothing can answer left out. False: no channel reaches a thruster.</summary>
    private static bool Fit(Span<float> m, Span<float> mix)
    {
        mix.Clear();
        Span<float> scale = stackalloc float[4];
        var any = false;
        for (var r = 0; r < 4; r++)
        {
            var most = 0f;
            for (var k = 0; k < 4; k++)
                most = MathF.Max(most, MathF.Abs(m[r * 4 + k]));
            scale[r] = most;
            any |= most > 0;
        }
        if (!any)
            return false;
        var torques = MathF.Max(scale[1], MathF.Max(scale[2], scale[3]));
        for (var r = 0; r < 4; r++)
        {
            var off = scale[r] <= 0 || r > 0 && scale[r] < 0.1f * torques; // (an arm a hair long is no way to turn it)
            for (var k = 0; k < 4; k++)
                m[r * 4 + k] = off ? 0 : m[r * 4 + k] / scale[r];
            if (off)
                scale[r] = 0;
        }
        Span<float> a = stackalloc float[32]; // (M M^T beside the identity)
        for (var r = 0; r < 4; r++)
        {
            for (var q = 0; q < 4; q++)
            {
                var dot = 0f;
                for (var k = 0; k < 4; k++)
                    dot += m[r * 4 + k] * m[q * 4 + k];
                a[r * 8 + q] = dot;
                a[r * 8 + 4 + q] = r == q ? 1 : 0;
            }
            a[r * 8 + r] += scale[r] > 0 ? 1e-3f : 1f; // (a little give: a line of thrusters can't be twisted across it)
        }
        for (var col = 0; col < 4; col++)
        {
            var best = col;
            for (var r = col + 1; r < 4; r++)
                if (MathF.Abs(a[r * 8 + col]) > MathF.Abs(a[best * 8 + col]))
                    best = r;
            if (best != col)
                for (var q = 0; q < 8; q++)
                    (a[col * 8 + q], a[best * 8 + q]) = (a[best * 8 + q], a[col * 8 + q]);
            var pivot = a[col * 8 + col];
            if (MathF.Abs(pivot) < 1e-9f)
                return false;
            for (var q = 0; q < 8; q++)
                a[col * 8 + q] /= pivot;
            for (var r = 0; r < 4; r++)
            {
                if (r == col)
                    continue;
                var f = a[r * 8 + col];
                for (var q = 0; q < 8; q++)
                    a[r * 8 + q] -= f * a[col * 8 + q];
            }
        }
        for (var k = 0; k < 4; k++)
            for (var r = 0; r < 4; r++)
            {
                if (scale[r] <= 0)
                    continue;
                var share = 0f;
                for (var q = 0; q < 4; q++)
                    share += m[q * 4 + k] * a[q * 8 + 4 + r];
                mix[k * 4 + r] = share / scale[r];
            }
        return true;
    }

    /// <summary>One tick of a Flight Stabilizer. Returns its first output; the others go to its ports.</summary>
    public static float Step(ConstructWorld world, Construct c, int part, ref CPart p, ConstructPartDef d, int from, float dt)
    {
        var k = IndexOf(c, part);
        if (k == 0)
        {
            c.StabLive = false;
            c.GyroNow = Vector3.Zero;
        }
        if (k < 0 || c.PortBase.Length == 0 || c.PortBase[part] < 0)
            return 0;
        ref var s = ref c.Stabs[k];
        var input = c.PortIn.AsSpan(c.PortBase[part], LogicBlocks.PortWidth);
        var output = c.PortOut.AsSpan(c.PortBase[part], LogicBlocks.PortWidth);
        var seat = from >= 0 && from < c.PartCount && c.Parts[from].D.Role == PartRole.Seat ? from : -1;
        var keys = seat >= 0 ? c.Parts[seat].Keys : (ushort)0;
        var manned = (keys & CPart.Occupied) != 0;
        p.Count = (ushort)(manned ? seat + 1 : 0);
        var pattern = (FlightPattern)Math.Clamp((int)ConstructConfig.Get(p, d, CfgKey.Pattern, 2), 0, Limits.Length - 1);
        var autopilot = Autopilot(world, c, ref s, ref p, d, input);
        var ask = Ask(pattern, keys, input);
        if ((s.WiredIn & 1 << InSetHome) != 0)
        {
            if (input[InSetHome] >= 0.5f && s.HomeWas < 0.5f)
                SetHome(world, c, part);
            s.HomeWas = input[InSetHome];
        }
        // Armed: at work. It stays armed with nobody at the controls: a craft in the air isn't dropped by its pilot getting up.
        var armed = (p.State & 1) != 0;
        var flies = pattern is FlightPattern.Plane or FlightPattern.Rocket;
        if (flies && ask.Lift != 0)
            p.Mem = Math.Clamp(p.Mem + ask.Lift * 0.5f * dt, 0, 1); // (Shift and Ctrl work a throttle that stays where it is left)
        var wants = autopilot || (flies ? p.Mem > 0 || ask.Throttle > 0 : ask.Lift > 0) || pattern == FlightPattern.Airship && (ask.Forward != 0 || ask.Lift != 0);
        if (wants && !armed)
        {
            armed = true;
            SetHome(world, c, part); // (home is where it took off from, until something says otherwise)
            s.AltHeld = s.HeadHeld = s.PosHeld = false;
            s.AltIntegral = 0;
            s.Integral = default;
            s.Landed = 0;
        }
        var height = ConstructPhysics.HoldHeight(c.WorldCom);
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
            s.Integral = default;
            s.AltIntegral = 0;
            s.ApWas = false;
            return 0;
        }
        c.StabLive |= armed;
        if (armed)
            c.Wake();
        if (!s.Mapped || s.Refit == 0)
            Refit(c, ref s, pattern);
        s.Refit--;
        Fly(world, c, ref s, ref p, d, pattern, in ask, autopilot, armed, seat, height, climb, up, output, dt);
        p.Out1 = output[1];
        return output[0];
    }

    /// <summary>The axes asked of it this tick, by its pattern's keys, with what is wired in added.</summary>
    private struct Asked
    {
        public float Pitch, Roll, Yaw, Lift, Throttle, Forward, Side; // -1..1: nose up, right wing down, nose right, up, the main engines; forward and right over the ground
    }

    private static Asked Ask(FlightPattern pattern, ushort keys, ReadOnlySpan<float> input)
    {
        var ws = ((keys & (ushort)Buttons.Forward) != 0 ? 1f : 0f) - ((keys & (ushort)Buttons.Back) != 0 ? 1f : 0f);
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
                a.Roll = Math.Clamp(a.Roll + qe + ad, -1, 1); // (A and D bank it, and the rudder follows)
                a.Yaw = Math.Clamp(input[InYaw], -1, 1);
                break;
            case FlightPattern.Helicopter:
            case FlightPattern.Quadcopter:
                a.Forward = Math.Clamp(ws + a.Throttle - a.Pitch, -1, 1); // (W: it leans into going forward)
                a.Side = Math.Clamp(qe + a.Roll, -1, 1);
                a.Pitch = a.Roll = 0;
                break;
            case FlightPattern.Rocket:
                a.Pitch = Math.Clamp(a.Pitch - ws, -1, 1);
                a.Roll = Math.Clamp(a.Roll + ad, -1, 1); // (A and D lean it; Q and E spin it)
                a.Yaw = Math.Clamp(input[InYaw] + qe, -1, 1);
                break;
            case FlightPattern.Hover:
                a.Forward = Math.Clamp(ws + a.Throttle, -1, 1);
                a.Side = Math.Clamp(qe + a.Roll, -1, 1);
                a.Pitch = a.Roll = 0;
                break;
            default: // airship
                a.Forward = Math.Clamp(ws + a.Throttle, -1, 1);
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

    private static void Fly(ConstructWorld world, Construct c, ref CStab s, ref CPart p, ConstructPartDef d, FlightPattern pattern, in Asked ask, bool autopilot, bool armed, int seat, float height, float climb, Vector3 up, Span<float> output, float dt)
    {
        var a = Read(c, p, up);
        var lim = Limits[(int)pattern];
        var maxTilt = Rad(pattern == FlightPattern.Hover ? 0 : Setting(p, d, CfgKey.MaxTilt, lim.X));
        var maxTurn = Rad(Setting(p, d, CfgKey.MaxTurn, lim.Y));
        var maxClimb = Setting(p, d, CfgKey.MaxClimb, lim.Z);
        var maxSpeed = Setting(p, d, CfgKey.MaxSpeed, lim.W);
        var strength = ConstructConfig.Get(p, d, CfgKey.Strength, 100) / 100f;
        var level = ConstructConfig.Get(p, d, CfgKey.Level, 1) >= 0.5f;
        var mouse = ConstructConfig.Get(p, d, CfgKey.MouseAim, 0) >= 0.5f && seat >= 0 && (c.Parts[seat].Keys & CPart.Occupied) != 0;
        var trimPitch = Rad(ConstructConfig.Get(p, d, CfgKey.TrimPitch, 0));
        var trimRoll = Rad(ConstructConfig.Get(p, d, CfgKey.TrimRoll, 0));
        var g = ConstructPhysics.GravityAccel;
        var weight = c.Mass * g;
        if (s.Mixed && pattern != FlightPattern.Airship && s.CornerForce > 0)
            maxTilt = MathF.Min(maxTilt, MathF.Max(MinMargin, MathF.Acos(MathF.Min(1, 1.15f * MathF.Max(0, weight - c.LiftNow) / s.CornerForce)))); // (the lean it has the thrust to hold up)
        var hasThrust = s.Spool > 0;
        var spool = hasThrust ? MathF.Max(0.05f, s.Spool) : 0.4f;

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
        if (autopilot)
        {
            var offset = height - ConstructPhysics.HoldShown(height);
            var alt = ConstructConfig.Get(p, d, CfgKey.TargetAlt, 0);
            if (alt != s.AltCfg)
            {
                s.AltCfg = alt;
                s.HoldAlt = alt > 0 ? alt + offset : height;
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
        float climbWant;
        if (ask.Lift != 0 && pattern is not (FlightPattern.Plane or FlightPattern.Rocket))
        {
            climbWant = ask.Lift * maxClimb;
            s.HoldAlt = height;
            s.AltHeld = true;
        }
        else
        {
            if (!s.AltHeld)
            {
                s.HoldAlt = height;
                s.AltHeld = true;
            }
            climbWant = Math.Clamp(0.8f * (s.HoldAlt - height), -maxClimb, maxClimb);
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
        else if (holdPos && far > (pattern == FlightPattern.Airship ? 25 : 6))
            s.HoldHead = course; // (an airship near the point stops turning for it: she can only go forward, and would hunt round it)
        else if (!s.HeadHeld || ask.Yaw != 0)
        {
            s.HoldHead = a.Heading;
            s.HeadHeld = true;
        }
        var headErr = Wrap(s.HoldHead - a.Heading);

        var loop = pattern switch { FlightPattern.Plane => LoopPlane, FlightPattern.Helicopter => LoopRotor, FlightPattern.Rocket => LoopRocket, FlightPattern.Airship => LoopShip, _ => LoopRotor } * strength;
        if (s.Mixed)
            loop = MathF.Min(4f, 1.2f / spool) * MathF.Min(strength, 1.5f); // (it knows what its thrusters do: as quick as they follow, and no quicker for the slider)
        var ground = MathF.Min(0.8f, 0.4f * loop); // (the speed loop stays well inside the attitude loop, or a big slow craft swings about what it asks)

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
        float tiltPitch = trimPitch, tiltRoll = trimRoll, yawRate;
        yawRate = ask.Yaw != 0 ? ask.Yaw * maxTurn : Math.Clamp(1.5f * strength * headErr, -maxTurn, maxTurn);
        var acroPitch = float.NaN;
        var acroRoll = float.NaN;
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
            case FlightPattern.Helicopter:
            case FlightPattern.Quadcopter:
                if (level)
                {
                    tiltPitch += -LeanFor(ground * (wantFront - vFront), g, maxTilt);
                    tiltRoll += LeanFor(ground * (wantSide - vSide), g, maxTilt);
                }
                else
                {
                    acroPitch = -ask.Forward * maxTurn;
                    acroRoll = ask.Side * maxTurn;
                }
                break;
        }

        // Attitude to turning, turning to torque.
        var want = new Vector3(
            !float.IsNaN(acroPitch) ? acroPitch : Math.Clamp(loop * Wrap(tiltPitch - a.Pitch), -maxTurn, maxTurn),
            !float.IsNaN(acroRoll) ? acroRoll : Math.Clamp(loop * Wrap(tiltRoll - a.Roll), -maxTurn, maxTurn),
            yawRate);
        var rateErr = want - a.Rate;
        var mainLevel = hasThrust && s.MainForce > 0 ? s.MainLevel : 1f;
        if (s.Mapped && s.MainForce > 0)
            mainLevel = MainLevelNow(c, ref s);
        var torque = default(Vector3); // (N m, for the fit and the gyro)
        var units = default(Vector3); // (-1..1, for what it knows nothing of)
        var kRate = 3f * loop;
        var speedNow = c.Vel.Length();
        var surface = pattern == FlightPattern.Plane ? MathF.Min(1, PlaneSpeed / MathF.Max(speedNow, PlaneSpeed * 0.5f)) : 1f;
        surface *= surface;
        var settling = armed && (c.Vel.LengthSquared() > 0.25f || !float.IsNaN(acroPitch) || pattern != FlightPattern.Plane); // (a plane parked: its surfaces just follow the stick)
        for (var axis = 0; axis < 3; axis++)
        {
            var known = axis == 0 ? s.Inertia.X : axis == 1 ? s.Inertia.Y : s.Inertia.Z;
            var auth = axis == 0 ? s.Auth.X : axis == 1 ? s.Auth.Y : s.Auth.Z;
            var integral = axis == 0 ? s.Integral.X : axis == 1 ? s.Integral.Y : s.Integral.Z;
            var err = axis == 0 ? rateErr.X : axis == 1 ? rateErr.Y : rateErr.Z;
            var accel = kRate * err + integral;
            var t = accel * known;
            var most = axis == 0 ? s.Most.X : axis == 1 ? s.Most.Y : s.Most.Z;
            var unit = auth != 0 ? Math.Clamp(t / (auth * MathF.Max(0.1f, mainLevel)), -most, most) : Math.Clamp((0.5f * strength * err + 0.1f * integral) * surface, -1, 1);
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

        // The height loop.
        var kv = 2.4f * MathF.Min(1.2f, 0.8f / spool) * strength;
        var climbErr = climbWant - climb;
        var lean = MathF.Max(MinCos, Vector3.Dot(a.Up, up));
        var climbAccel = Math.Clamp(kv * climbErr + s.AltIntegral, -0.9f * g, 2f * g);
        var liftForce = MathF.Max(0, (c.Mass * (g + climbAccel) - c.LiftNow) / lean);
        var offLimit = false;

        // The gyro takes what yaw it can; a fit that can turn it has the rest.
        var gyro = MathF.Min(d.Gyro * strength, MathF.Abs(torque.Z));
        if (gyro > 0)
        {
            c.GyroNow -= a.Up * (MathF.Sign(torque.Z) * gyro);
            torque.Z -= MathF.Sign(torque.Z) * gyro;
        }

        // The mix.
        output.Clear();
        float forwardPush;
        if (pattern == FlightPattern.Airship || pattern == FlightPattern.Hover)
        {
            var acc = 0.8f * (wantFront - vFront);
            forwardPush = ask.Forward != 0 && !holdPos ? ask.Forward : s.MainForce > 0 ? Math.Clamp(c.Mass * acc / s.MainForce, -1, 1) : Math.Clamp(acc / 3, -1, 1);
            var mainPush = pattern == FlightPattern.Airship ? forwardPush : 0;
            output[OutThrottle] = MathF.Max(0, forwardPush);
            output[OutReverse] = MathF.Max(0, -forwardPush);
            var side = Math.Clamp(0.8f * (wantSide - vSide) / 3, -1, 1);
            output[OutStrafeRight] = MathF.Max(0, side);
            output[OutStrafeLeft] = MathF.Max(0, -side);
            if (pattern == FlightPattern.Airship)
            {
                // Engines at the corners push it on; lift is the "lift" port's, or the gas's.
                if (s.Mixed)
                    offLimit = Corners(c, ref s, mainPush * s.CornerForce, new Vector3(0, 0, torque.Z), output); // (her gas keeps her level)
                Height(c, ref s, ref p, liftForce, climbErr, height, climb, weight, output, dt);
            }
            else if (s.Mixed)
                offLimit = Corners(c, ref s, liftForce, torque, output);
            else
                Height(c, ref s, ref p, liftForce, climbErr, height, climb, weight, output, dt);
        }
        else if (pattern == FlightPattern.Quadcopter)
        {
            if (s.Mixed)
                offLimit = Corners(c, ref s, liftForce, torque, output);
            else
                Height(c, ref s, ref p, liftForce, climbErr, height, climb, weight, output, dt);
        }
        else if (pattern == FlightPattern.Helicopter)
            Height(c, ref s, ref p, liftForce, climbErr, height, climb, weight, output, dt);
        else if (pattern == FlightPattern.Rocket)
        {
            if (holdAlt)
            {
                p.Mem = s.MainForce > 0 ? Math.Clamp(liftForce / s.MainForce, 0, 1) : Math.Clamp(p.Mem + climbErr * 0.05f * dt, 0, 1);
            }
            output[OutThrottle] = Math.Clamp(p.Mem + MathF.Max(0, ask.Throttle), 0, 1);
        }
        else
        {
            output[OutThrottle] = Math.Clamp(p.Mem + MathF.Max(0, ask.Throttle), 0, 1);
            output[OutReverse] = MathF.Max(0, -ask.Throttle);
            output[OutFlaps] = Math.Clamp(1 - speedNow / MathF.Max(1, 0.5f * maxSpeed), 0, 1);
        }
        if (!offLimit)
            s.AltIntegral = Math.Clamp(s.AltIntegral + 0.3f * kv * climbErr * dt, -0.5f * g, 0.5f * g);

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
    }

    private static float MainLevelNow(Construct c, ref CStab s)
    {
        var sum = 0f;
        var n = 0;
        for (var w = 0; w < c.WireCount; w++)
        {
            ref var wire = ref c.Wires[w];
            if (wire.From != s.Part || wire.FromPort != OutThrottle)
                continue;
            for (var k = 0; k < c.ThrusterCount; k++)
                if (c.Thrusters[k].Part == wire.To)
                {
                    sum += c.Thrusters[k].Level;
                    n++;
                }
        }
        return n > 0 ? sum / n : 1;
    }

    /// <summary>The lean that gives this acceleration over the ground.</summary>
    private static float LeanFor(float accel, float g, float most) => Math.Clamp(MathF.Atan2(accel, MathF.Max(g, 0.1f)), -most, most);

    /// <summary>The corner channels: a demand through the fit, holding attitude before height where one runs out of range. True when one did.</summary>
    private static bool Corners(Construct c, ref CStab s, float force, Vector3 torque, Span<float> output)
    {
        var mix = c.StabMix.AsSpan(s.MixAt, 16);
        Span<float> l = stackalloc float[4];
        var high = 0;
        var low = 0;
        for (var k = 0; k < 4; k++)
        {
            l[k] = mix[k * 4] * force + mix[k * 4 + 1] * torque.X + mix[k * 4 + 2] * torque.Y + mix[k * 4 + 3] * torque.Z;
            if (l[k] > l[high])
                high = k;
            if (l[k] < l[low])
                low = k;
        }
        var over = l[high] > 1;
        var under = l[low] < 0;
        if (over != under)
        {
            // More or less of the main push, shared as it always is (so it turns nothing), brings the worst one back. One that hardly carries any can't be helped that way.
            var worst = over ? high : low;
            var share = mix[worst * 4];
            if (share * s.CornerForce >= 0.25f)
            {
                var extra = Math.Clamp(((over ? 1 : 0) - l[worst]) / share, -0.5f * s.CornerForce, 0.5f * s.CornerForce);
                for (var k = 0; k < 4; k++)
                    l[k] += mix[k * 4] * extra;
            }
        }
        for (var k = 0; k < 4; k++)
            output[OutFrontLeft + k] = Math.Clamp(l[k], 0, 1);
        return over || under;
    }

    /// <summary>The lift port and the vent: a force it knows how to make, or a slow trim, as the air in a bag takes a while to answer.</summary>
    private static void Height(Construct c, ref CStab s, ref CPart p, float force, float climbErr, float height, float climb, float weight, Span<float> output, float dt)
    {
        if (s.LiftForce > 0)
            output[OutLift] = Math.Clamp(force / s.LiftForce, 0, 1);
        else
        {
            p.Mem = Math.Clamp(p.Mem + (0.02f * (s.HoldAlt - height) - 0.25f * climb) * dt, 0, 1);
            output[OutLift] = Math.Clamp(p.Mem + 0.03f * (s.HoldAlt - height) - 0.4f * climb, 0, 1);
        }
        if (climbErr < -0.5f && force <= 0)
            output[OutVent] = Math.Clamp(-0.2f * climbErr, 0, 1);
    }
}
