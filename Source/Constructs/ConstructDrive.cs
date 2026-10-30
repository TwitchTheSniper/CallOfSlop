using System.Numerics;

namespace CallOfSlop;

/// <summary>Why a drivetrain that is built right isn't driving this tick (the HUD; it goes to clients in the pose).</summary>
public enum DriveNote : byte
{
    None,
    ClutchOut, // a clutch on the drive line is disengaged
    JointAngle, // a U joint is bent past its angle (full steering lock): its wheels lose the drive
    NeedsStarter, // a diesel or a turbine on a construct with no battery
    Flat, // an electric motor's battery is flat (or doesn't reach it)
    Slipping, // a belt is slipping (more torque than it carries)
    Shifting, // a gear change: the power is cut for a moment
    Spooling, // a turbine spooling up
}

/// <summary>
/// The drivetrain families (docs/sandbox/CONSTRUCTS.md, "Drivetrain parts"): which wheels the engine drives, through
/// what (gearboxes, shafts, U and CV joints, chains and belts, clutches, transfer cases, differentials and hubs), and
/// how hard. Built with the caches (<see cref="Construct.Rebuild"/>); <see cref="ConstructDrive.Tick"/> runs the engine
/// each tick for the wheels.
/// </summary>
public sealed partial class Construct
{
    public const int MaxGates = 16;
    public const int MaxGroups = 12;
    public float Rpm; // the engine (server; clients get it in the pose)
    public float Spool; // a turbine's spool, 0..1
    public float ShiftTimer; // seconds of a gear change's power cut left
    public float DriveTorque; // N m out of the engine this tick (after its curve, the throttle, a spool, a shift)
    public DriveNote Note; // why it isn't driving right now (with a drivetrain that is built right)
    public string DriveBlock = ""; // what in the drive line stops it (part of DriveProblem)
    public float TransEff = 1f; // the gearbox's efficiency (or 0 without one)
    public int ShiftGear = -1; // the gear the last tick ran in (a change starts the shift's power cut)
    public ushort ShiftKeys; // the driver's shift keys last tick (R up, sprint down)
    public int AutoMax = 99; // an automatic's highest gear (a wired signal)
    public bool SkidSteer; // driven wheels and none that steer: the steering drives the sides against each other (a tracked crawler)
    public readonly byte[] GatePart = new byte[MaxGates];
    public readonly byte[] GateKind = new byte[MaxGates]; // 0 clutch, 1 a transfer case's front, 2 a U/CV joint's angle
    public readonly float[] GateAngle = new float[MaxGates]; // a U/CV joint: the most it bends (radians)
    public readonly sbyte[] GateJoint = new sbyte[MaxGates]; // a U/CV joint: the joint whose angle it follows
    public int GateCount;
    public ushort GateOpen; // this tick
    public readonly byte[] GroupPart = new byte[MaxGroups]; // the differential or hub of each drive group
    public int GroupCount;
    public readonly bool[] PtoDriven = new bool[MaxParts]; // a moving joint on a power take-off: runs while the engine does
    public bool BeltSlipping;
    public readonly float[] GroupGrip = new float[MaxGroups]; // this tick: the least grip among each group's touching wheels (last tick's)

    /// <summary>The engine has what it runs on (fuel in the tanks, or it needs none).</summary>
    public bool HasFuel => Engine < 0 || Engine >= PartCount || DefOf(Engine).FuelKind != EngineFuel.Fuel || Fuel > 0;

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
            var flat = Parts[i].Broken && d.FlatTyre; // a punctured tyre runs on its rim
            var rays = d.TrackRays > 0 ? d.TrackRays : 1;
            var along = Vector3.Cross(axle, Vector3.UnitY); // a track's length runs across its axle
            for (var r = 0; r < rays && WheelCount < MaxWheels; r++)
            {
                var at = rays == 1 ? centre : centre + along * ((r / (rays - 1f) - 0.5f) * (d.TrackLength - 2 * d.WheelRadius));
                ref var w = ref Wheels[WheelCount];
                w = new CWheel
                {
                    Part = (byte)i,
                    Suspension = (byte)s,
                    Centre = at + new Vector3(0, sd.Travel * 0.5f, 0), // drawn and cast from the top of its travel
                    Axle = axle,
                    Radius = flat ? d.WheelRadius * 0.75f : d.WheelRadius,
                    Travel = sd.Travel,
                    Frequency = sd.Frequency,
                    Damping = sd.Damping,
                    Grip = d.Grip * d.Friction * (flat ? 0.5f : 1f),
                    Offroad = d.OffroadGrip,
                    Side = d.SideGrip,
                    Roll = flat ? d.RollResist * 4 + 0.03f : d.RollResist,
                    MaxLoad = sd.MaxLoad,
                    Track = d.TrackRays > 0,
                    Drivable = d.Drivable,
                    Steers = Parts[i].Seg != 0, // (steering is a joint since milestone 2: a wheel on a steering pivot)
                    Compression = WheelCount < old ? keep[WheelCount] : 0,
                    Seg = Parts[i].Seg,
                };
                WheelCount++;
            }
        }
    }

    /// <summary>
    /// The drivetrain (decision: it works only when connected): a working seat, a working engine in the body that has what
    /// it runs on (fuel from a working tank touching it or through working fuel lines; a battery for an electric motor),
    /// a working transmission touching the engine, then the drive line: from the transmission through working
    /// differentials and transfer parts (each through its ports), to the suspensions bolted to a differential or a hub, or
    /// on a steering pivot that a U joint or CV joint feeds. Only those wheels are driven; the rest roll.
    /// <see cref="DriveProblem"/> says what is missing.
    /// </summary>
    private void BuildDrivetrain()
    {
        CanDrive = false;
        Engine = -1;
        Transmission = -1;
        DriveSeat = -1;
        DriveBlock = "";
        GateCount = 0;
        GroupCount = 0;
        SkidSteer = false;
        TransEff = 0;
        Array.Clear(PtoDriven);
        for (var k = 0; k < WheelCount; k++)
        {
            ref var w = ref Wheels[k];
            w.Driven = false;
            w.Group = 255;
            w.GateMask = 0;
            w.Ratio = 1;
            w.Eff = 1;
            w.CapEq = float.PositiveInfinity;
        }
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
        var electricNoBattery = false;
        for (var i = 0; i < PartCount; i++)
        {
            if (Parts[i].D.Role != PartRole.Engine || Parts[i].Seg != 0)
                continue;
            anyEngine = true;
            if (!Parts[i].Working)
                continue;
            var fuel = Parts[i].D.FuelKind;
            if (fuel == EngineFuel.Electric && CountRole(PartRole.Battery) == 0)
            {
                electricNoBattery = true;
                continue;
            }
            if (fuel != EngineFuel.Fuel || FuelReaches(i))
            {
                Engine = i;
                break;
            }
        }
        if (Engine < 0)
        {
            DriveProblem = !anyEngine ? "no engine" : CountRole(PartRole.Engine) == 0 ? "the engine is wrecked"
                : electricNoBattery ? "the electric motor needs a working battery"
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
        TransEff = DefOf(Transmission).TransEfficiency;
        // The drive line, breadth first from the transmission: each part fed once, with the reduction, efficiency, torque
        // cap and gates of the way it was reached, and the differential or hub its axle output belongs to.
        Span<bool> fed = stackalloc bool[MaxParts];
        Span<float> ratio = stackalloc float[MaxParts];
        Span<float> eff = stackalloc float[MaxParts];
        Span<float> cap = stackalloc float[MaxParts];
        Span<ushort> mask = stackalloc ushort[MaxParts];
        Span<byte> group = stackalloc byte[MaxParts];
        Span<byte> queue = stackalloc byte[MaxParts];
        int head = 0, tail = 0;
        queue[tail++] = (byte)Transmission;
        fed[Transmission] = true;
        ratio[Transmission] = 1;
        eff[Transmission] = 1;
        cap[Transmission] = float.PositiveInfinity;
        group[Transmission] = 255;
        var anyDiff = false;
        while (head < tail)
        {
            int at = queue[head++];
            var da = Parts[at].D;
            // Leaving this part: its own ratio and losses (a differential's final drive only on its axle outputs).
            var outRatio = ratio[at] * (da.Role == PartRole.Transfer ? da.DriveRatio : 1f);
            var outEff = eff[at] * (da.Role == PartRole.Transfer ? da.DriveEfficiency : 1f);
            var outCap = da.Role == PartRole.Transfer && da.MaxTorque > 0 ? MathF.Min(cap[at], da.MaxTorque / MathF.Max(0.01f, ratio[at])) : cap[at];
            var outMask = mask[at];
            if (da.Role == PartRole.Transfer && da.Drive is DriveKind.Clutch && GateCount < MaxGates)
                outMask |= AddGate(at, 0, -1, 0);
            if (da.Role == PartRole.Transfer && da.Drive is DriveKind.Hub or DriveKind.Stub)
                continue; // (a hub drives only its suspension: after this loop)
            for (var k = 0; k < LinkCount; k++)
            {
                ref var l = ref Links[k];
                int other, sa, sb;
                if (l.A == at) (other, sa, sb) = (l.B, l.SA, l.SB);
                else if (l.B == at) (other, sa, sb) = (l.A, l.SB, l.SA);
                else continue;
                if (fed[other])
                    continue;
                var dob = Parts[other].D;
                var joint = dob.Role is PartRole.Joint or PartRole.Hinge;
                if (dob.Role is not (PartRole.Differential or PartRole.Transfer) && !joint)
                    continue;
                if (da.Role == PartRole.Joint)
                    continue; // (a steering pivot passes the drive on only to the suspension on its leaf: after this loop)
                if (dob.Role == PartRole.Transfer && dob.Ports.Length > 0 && Array.IndexOf(dob.Ports, sb) < 0
                    || da.Role == PartRole.Transfer && da.Ports.Length > 0 && Array.IndexOf(da.Ports, sa) < 0)
                {
                    Block($"the {(dob.Role == PartRole.Transfer && dob.Ports.Length > 0 && Array.IndexOf(dob.Ports, sb) < 0 ? dob.Name : da.Name).ToLowerInvariant()} is fitted by its side: the drive goes in and out of its ends");
                    continue;
                }
                if (da.Role == PartRole.Transfer && da.Drive == DriveKind.Pto)
                {
                    if (joint && dob.JointType != JointType.Steering)
                    {
                        PtoDriven[other] = true; // (a turntable or a winch on the power take-off)
                        continue;
                    }
                }
                if (joint)
                {
                    var uj = da.Role == PartRole.Transfer && da.Drive is DriveKind.UJoint or DriveKind.CvJoint;
                    if (dob.JointType != JointType.Steering)
                    {
                        if (uj || dob.JointType == JointType.Piston)
                            Block(dob.JointType == JointType.Piston ? "a piston can't carry the drive" : $"the drive only goes through a steering pivot, not a {dob.Name.ToLowerInvariant()}");
                        continue;
                    }
                    if (!uj)
                    {
                        Block($"a steered wheel needs a U joint or CV joint to be driven (between the {da.Name.ToLowerInvariant()} and the {dob.Name.ToLowerInvariant()})");
                        continue;
                    }
                    if (!Parts[other].Working)
                        continue;
                    fed[other] = true;
                    ratio[other] = outRatio;
                    eff[other] = outEff;
                    cap[other] = outCap;
                    mask[other] = (ushort)(outMask | (GateCount < MaxGates ? AddGate(at, 2, (sbyte)JointOf(other), da.MaxAngle) : 0));
                    group[other] = group[at];
                    queue[tail++] = (byte)other;
                    continue;
                }
                if (dob.Role == PartRole.Differential)
                    anyDiff = true;
                if (dob.Role == PartRole.Transfer && dob.Drive == DriveKind.Stub)
                {
                    Block($"a {dob.Name.ToLowerInvariant()} carries no drive: fit a hub or a differential");
                    continue;
                }
                if (!Parts[other].Working)
                {
                    Block($"the {dob.Name.ToLowerInvariant()} is wrecked");
                    continue;
                }
                if (da.Role == PartRole.Transfer && da.Drive == DriveKind.Pto)
                    continue; // (a power take-off runs joints, not wheels)
                fed[other] = true;
                var m = outMask;
                if (da.Role == PartRole.Transfer && da.Drive == DriveKind.TransferCase && sa == da.FrontPort && GateCount < MaxGates)
                    m |= AddGate(at, 1, -1, 0);
                // Out of a differential's axle (into a U or CV joint): its final drive, and its wheels are its group.
                var axleOut = da.Role == PartRole.Differential && dob.Role == PartRole.Transfer && dob.Drive is DriveKind.UJoint or DriveKind.CvJoint;
                ratio[other] = axleOut ? outRatio * da.DiffRatio : outRatio;
                eff[other] = axleOut ? outEff * da.DiffEfficiency : outEff;
                cap[other] = outCap;
                mask[other] = m;
                group[other] = axleOut ? (byte)at : group[at];
                queue[tail++] = (byte)other;
            }
        }
        // The wheels: a suspension on a fed differential or hub, or on the leaf of a fed steering pivot.
        var driven = 0;
        var casters = 0;
        for (var k = 0; k < WheelCount; k++)
        {
            ref var w = ref Wheels[k];
            int s = w.Suspension;
            for (var l = 0; l < LinkCount && !w.Driven; l++)
            {
                int other, so;
                if (Links[l].A == s) (other, so) = (Links[l].B, Links[l].SB);
                else if (Links[l].B == s) (other, so) = (Links[l].A, Links[l].SA);
                else continue;
                if (!fed[other])
                    continue;
                var d = Parts[other].D;
                if (d.Role == PartRole.Transfer && d.Ports.Length > 0 && Array.IndexOf(d.Ports, so) < 0)
                    continue;
                float r, e;
                int g;
                if (d.Role == PartRole.Differential)
                    (r, e, g) = (ratio[other] * d.DiffRatio, eff[other] * d.DiffEfficiency, other);
                else if (d.Role == PartRole.Transfer && d.Drive == DriveKind.Hub)
                    (r, e, g) = (ratio[other] * d.DriveRatio, eff[other] * d.DriveEfficiency, other);
                else if (d.Role == PartRole.Joint && d.JointType == JointType.Steering && so == d.LeafIndex)
                    (r, e, g) = (ratio[other], eff[other], group[other] != 255 ? group[other] : other);
                else
                    continue;
                if (!w.Drivable)
                {
                    casters++;
                    break;
                }
                w.Driven = true;
                w.Ratio = r;
                w.Eff = e;
                w.CapEq = cap[other];
                w.GateMask = mask[other];
                var gi = -1;
                for (var q = 0; q < GroupCount; q++)
                    if (GroupPart[q] == g)
                        gi = q;
                if (gi < 0 && GroupCount < MaxGroups)
                {
                    gi = GroupCount++;
                    GroupPart[gi] = (byte)g;
                }
                w.Group = (byte)Math.Max(0, gi);
                driven++;
            }
        }
        if (driven == 0)
        {
            DriveProblem = WheelCount == 0 ? "no wheels on suspension" : casters > 0 ? "a caster wheel can't be driven"
                : DriveBlock.Length > 0 ? DriveBlock : !anyDiff ? "no differential on the transmission"
                : CountRole(PartRole.Differential) == 0 ? "the differential is wrecked" : "no wheels on the differential";
            return;
        }
        // Skid steering: driven wheels and none that steer (a tracked crawler).
        var steers = false;
        for (var k = 0; k < WheelCount; k++)
            steers |= Wheels[k].Steers;
        SkidSteer = !steers;
        var right = Vector3.Cross(SeatForward(), Vector3.UnitY);
        for (var k = 0; k < WheelCount; k++)
            Wheels[k].SideSign = Vector3.Dot(Wheels[k].Centre - Com, right) >= 0 ? 1f : -1f;
        CanDrive = true;
        DriveProblem = "";
    }

    private void Block(string why)
    {
        if (DriveBlock.Length == 0)
            DriveBlock = why;
    }

    private ushort AddGate(int part, byte kind, sbyte joint, float angle)
    {
        for (var g = 0; g < GateCount; g++)
            if (GatePart[g] == part && GateKind[g] == kind && GateJoint[g] == joint)
                return (ushort)(1 << g);
        var i = GateCount++;
        GatePart[i] = (byte)part;
        GateKind[i] = kind;
        GateJoint[i] = joint;
        GateAngle[i] = angle;
        return (ushort)(1 << i);
    }
}

/// <summary>The engine each tick (server, before the wheels): gears, rpm, torque, fuel and charge, the gates.</summary>
public static class ConstructDrive
{
    public const float RpmPerRadS = 60f / MathF.Tau;

    /// <summary>
    /// The driver's shift keys (a manual or sequential box with nothing wired in): R up, sprint down, on the press. Server,
    /// every tick before the logic step (which reads and clears the seats' keys).
    /// </summary>
    public static void ShiftKeys(Construct c)
    {
        if (!c.CanDrive || c.Transmission < 0 || c.Transmission >= c.PartCount || c.DriveSeat < 0 || c.DriveSeat >= c.PartCount)
            return;
        var keys = c.Driver != 255 ? c.Parts[c.DriveSeat].Keys : (ushort)0;
        var shift = (ushort)(keys & (ConstructParts.KeyReload | (ushort)Buttons.Sprint));
        var rise = (ushort)(shift & ~c.ShiftKeys);
        c.ShiftKeys = shift;
        ref var tp = ref c.Parts[c.Transmission];
        var t = tp.D;
        var gears = t.Settings.Length;
        if (rise == 0 || t.Trans is not (TransKind.Manual or TransKind.Sequential) || gears < 2 || tp.InCount > 0)
            return;
        if ((rise & ConstructParts.KeyReload) != 0 && tp.Setting < gears - 1)
            tp.Setting++;
        if ((rise & (ushort)Buttons.Sprint) != 0 && tp.Setting > 0)
            tp.Setting--;
        c.Wake();
    }

    /// <summary>An engine's torque at <paramref name="rpm"/> as a share of its peak (0 at and past the limiter).</summary>
    public static float Curve(ConstructPartDef d, float rpm)
    {
        var max = d.MaxRpm;
        if (rpm >= max)
            return 0;
        var fade = rpm > max * 0.96f ? (max - rpm) / (max * 0.04f) : 1f;
        var idle = d.IdleRpm;
        var peak = d.PeakRpm;
        var up = Math.Clamp((rpm - idle) / MathF.Max(1, peak - idle), 0, 1);
        var down = Math.Clamp((rpm - peak) / MathF.Max(1, max - peak), 0, 1);
        var f = d.Curve switch
        {
            EngineCurve.Diesel => rpm <= peak ? 0.85f + 0.15f * up : 1 - 0.45f * down,
            EngineCurve.Electric => rpm <= peak ? 1f : peak / rpm,
            EngineCurve.Turbine => rpm <= peak ? 0.3f + 0.7f * up : 1 - 0.1f * down,
            EngineCurve.Flat => 1f,
            _ => rpm <= peak ? 0.55f + 0.45f * up : 1 - 0.25f * down,
        };
        return f * fade;
    }

    /// <summary>
    /// One tick of the engine for <paramref name="c"/>'s wheels: the gates (clutches, a transfer case, U joint angles), the
    /// gear (the driver's shift keys, an automatic's choice, a CVT's ratio), the rpm from the driven wheels, the torque out
    /// of the engine, the fuel or charge it uses, and each driven wheel's share. Nothing allocates.
    /// </summary>
    public static void Tick(Construct c, bool driving, float throttle, float steer, float forwardSpeed, float dt)
    {
        c.Note = DriveNote.None;
        c.DriveTorque = 0;
        c.BeltSlipping = false;
        if (!c.CanDrive || c.Engine < 0 || c.Engine >= c.PartCount || c.Transmission < 0 || c.Transmission >= c.PartCount)
        {
            c.Rpm = 0;
            return;
        }
        var e = c.DefOf(c.Engine);
        var t = c.DefOf(c.Transmission);
        ref var tp = ref c.Parts[c.Transmission];
        // Gates.
        ushort open = 0;
        for (var g = 0; g < c.GateCount; g++)
        {
            ref var gp = ref c.Parts[c.GatePart[g]];
            var on = c.GateKind[g] switch
            {
                0 => gp.InCount == 0 || gp.In >= 0.5f, // a clutch: engaged unless a wire says no
                1 => gp.InCount > 0 ? gp.In >= 0.5f : gp.Setting == 1, // a transfer case: 4WD
                _ => c.GateJoint[g] < 0 || c.GateJoint[g] >= c.JointCount || MathF.Abs(c.Joints[c.GateJoint[g]].Angle) <= c.GateAngle[g] + 0.002f,
            };
            if (on)
                open |= (ushort)(1 << g);
        }
        c.GateOpen = open;
        var gears = t.Settings.Length;
        // Which wheels the drive reaches, and how fast the engine turns through them.
        var spin = 0f; // sum of reduction / radius over the driven, reached, touching wheels
        var reached = 0;
        for (var k = 0; k < c.WheelCount; k++)
        {
            ref var w = ref c.Wheels[k];
            if (!w.Driven || (w.GateMask & ~open) != 0)
                continue;
            if (w.Touching)
            {
                spin += w.Ratio / w.Radius;
                reached++;
            }
        }
        var anyGated = false;
        for (var k = 0; k < c.WheelCount && !anyGated; k++)
            anyGated = c.Wheels[k].Driven && (c.Wheels[k].GateMask & ~open) != 0;
        if (anyGated)
            c.Note = NoteFor(c, open);
        var avgSpin = reached > 0 ? spin / reached : 0f;
        var reverse = throttle < 0;
        var speed = MathF.Abs(forwardSpeed);
        // The gear.
        float gearRatio;
        switch (t.Trans)
        {
            case TransKind.Cvt:
                // Holds the engine at its peak (or as near as the range allows).
                gearRatio = reverse ? t.ReverseRatio : avgSpin > 0 && speed > 0.1f ? Math.Clamp(e.PeakRpm / (speed * avgSpin * ConstructDrive.RpmPerRadS), t.CvtHigh, t.CvtLow) : t.CvtLow;
                c.Gear = 0;
                break;
            case TransKind.Automatic when gears > 0:
            {
                var top = Math.Min(gears - 1, tp.InCount > 0 && tp.In >= 0.5f ? (int)MathF.Round(tp.In) - 1 : gears - 1);
                var gear = Math.Clamp(c.Gear, 0, top);
                var rpmNow = speed * avgSpin * t.Settings[gear] * ConstructDrive.RpmPerRadS;
                if (c.ShiftTimer <= 0 && !reverse)
                {
                    if (gear < top && rpmNow > e.MaxRpm * 0.9f)
                        gear++;
                    else if (gear > 0 && rpmNow * t.Settings[gear - 1] / t.Settings[gear] < e.MaxRpm * 0.7f)
                        gear--;
                }
                if (reverse)
                    gear = 0;
                c.Gear = gear;
                gearRatio = reverse ? t.ReverseRatio : t.Settings[gear];
                break;
            }
            default:
                if (gears > 0)
                {
                    var gear = Math.Min((int)tp.Setting, gears - 1);
                    if (tp.InCount > 0 && tp.In >= 0.5f)
                        gear = Math.Clamp((int)MathF.Round(tp.In) - 1, 0, gears - 1);
                    c.Gear = gear;
                    gearRatio = reverse ? t.ReverseRatio : t.Settings[gear];
                }
                else
                    gearRatio = reverse ? t.ReverseRatio : 1f;
                break;
        }
        if (c.ShiftGear >= 0 && c.Gear != c.ShiftGear && t.Trans != TransKind.Cvt && speed > 0.5f) // (a gear picked standing still costs nothing)
            c.ShiftTimer = t.ShiftSeconds;
        c.ShiftGear = c.Gear;
        c.GearRatio = gearRatio;
        // The rpm: the driven wheels turn the engine (it can't go below idle: the clutch slips); in the air it revs freely.
        float rpm;
        var demandNow = MathF.Max(MathF.Abs(throttle), c.SkidSteer ? MathF.Abs(steer) : 0);
        if (!driving)
            rpm = 0;
        else if (reached > 0)
            rpm = MathF.Max(e.IdleRpm + (e.PeakRpm - e.IdleRpm) * 0.7f * demandNow, speed * avgSpin * gearRatio * ConstructDrive.RpmPerRadS); // (pulling away the clutch slips: the engine revs up towards its peak)
        else
        {
            var want = e.IdleRpm + MathF.Abs(throttle) * (e.MaxRpm * 0.95f - e.IdleRpm);
            rpm = c.Rpm + Math.Clamp(want - c.Rpm, -e.MaxRpm * 2 * dt, e.MaxRpm * 2 * dt);
        }
        c.Rpm = MathF.Min(rpm, e.MaxRpm * 1.1f); // (the wheels can spin it past the limiter, coasting downhill or in a shift; not without bound)
        if (!driving)
        {
            c.Spool = MathF.Max(0, c.Spool - dt);
            return;
        }
        // The torque out of the engine.
        var demand = MathF.Max(MathF.Abs(throttle), c.SkidSteer ? MathF.Abs(steer) : 0);
        float amount;
        if (e.SpoolSeconds > 0)
        {
            var rate = dt / e.SpoolSeconds;
            c.Spool = Math.Clamp(c.Spool + Math.Clamp(demand - c.Spool, -2 * rate, rate), 0, 1);
            amount = c.Spool;
            if (demand > 0 && c.Spool < 0.9f * demand)
                c.Note = DriveNote.Spooling;
        }
        else
            amount = demand;
        var torque = e.EngineTorque * Curve(e, rpm) * amount;
        if (c.ShiftTimer > 0)
        {
            c.ShiftTimer -= dt;
            torque = 0;
            if (demand > 0 && c.Note == DriveNote.None)
                c.Note = DriveNote.Shifting;
        }
        // What it runs on.
        var omega = rpm / ConstructDrive.RpmPerRadS;
        switch (e.FuelKind)
        {
            case EngineFuel.Fuel:
                if (demand > 0)
                    c.Fuel = MathF.Max(0, c.Fuel - e.BurnRate * (0.15f + 0.85f * demand) * (0.4f + 0.6f * rpm / e.MaxRpm) * dt);
                else if (e.SpoolSeconds > 0)
                    c.Fuel = MathF.Max(0, c.Fuel - e.BurnRate * 0.25f * dt); // (a turbine burns at idle too)
                break;
            case EngineFuel.Electric:
                if (!c.PowerOn || !ConstructPower.On(c, c.Engine))
                {
                    torque = 0;
                    c.Note = DriveNote.Flat;
                }
                else if (torque > 0)
                    c.Charge = MathF.Max(0, c.Charge - torque * MathF.Max(omega, 40f) / 0.9f / 1000f * dt); // kW out of the batteries
                break;
        }
        c.DriveTorque = torque;
        // Each driven wheel's share: the torque goes to the drive groups (differentials, hubs) that touch, then within
        // each by its kind: an open differential with a wheel in the air passes almost nothing, a limited-slip one most
        // of it to the wheel that grips, a locked one all of it.
        Span<byte> wheelsIn = stackalloc byte[Construct.MaxGroups];
        Span<byte> touchIn = stackalloc byte[Construct.MaxGroups];
        for (var k = 0; k < c.WheelCount; k++)
        {
            ref var w = ref c.Wheels[k];
            w.Share = 0;
            if (!w.Driven || (w.GateMask & ~open) != 0 || w.Group >= Construct.MaxGroups)
                continue;
            wheelsIn[w.Group]++;
            if (w.Touching)
                touchIn[w.Group]++;
        }
        var groups = 0;
        for (var g = 0; g < c.GroupCount; g++)
        {
            c.GroupGrip[g] = float.PositiveInfinity;
            if (touchIn[g] > 0)
                groups++;
        }
        for (var k = 0; k < c.WheelCount; k++)
        {
            ref var w = ref c.Wheels[k];
            if (w.Driven && w.Touching && w.Group < c.GroupCount && w.LastGrip > 0)
                c.GroupGrip[w.Group] = MathF.Min(c.GroupGrip[w.Group], w.LastGrip);
        }
        for (var k = 0; k < c.WheelCount; k++)
        {
            ref var w = ref c.Wheels[k];
            if (!w.Driven || (w.GateMask & ~open) != 0 || w.Group >= Construct.MaxGroups || !w.Touching)
                continue;
            int n = wheelsIn[w.Group], tc = touchIn[w.Group];
            var kind = GroupKind(c, w.Group);
            var within = kind switch
            {
                DiffKind.Locked => 1f / tc,
                DiffKind.Limited => tc < n ? 0.6f / tc : 1f / n,
                _ => tc < n ? 0.1f / n : 1f / n,
            };
            w.Share = within / MathF.Max(1, groups);
        }
        // A belt slips above what it carries.
        var atBox = torque * gearRatio;
        for (var k = 0; k < c.WheelCount; k++)
            if (c.Wheels[k].Share > 0 && atBox > c.Wheels[k].CapEq)
                c.BeltSlipping = true;
        if (c.BeltSlipping && c.Note == DriveNote.None)
            c.Note = DriveNote.Slipping;
    }

    /// <summary>A drive group's kind now (a lockable differential follows its setting or its wire; a hub, a joint: locked).</summary>
    public static DiffKind GroupKind(Construct c, int g)
    {
        int part = c.GroupPart[g];
        if (part >= c.PartCount)
            return DiffKind.Open;
        ref var p = ref c.Parts[part];
        var d = p.D;
        if (d.Role != PartRole.Differential)
            return DiffKind.Locked;
        if (d.Diff == DiffKind.Lockable)
            return (p.InCount > 0 ? p.In >= 0.5f : p.Setting == 1) ? DiffKind.Locked : DiffKind.Open;
        return d.Diff;
    }

    private static DriveNote NoteFor(Construct c, ushort open)
    {
        for (var g = 0; g < c.GateCount; g++)
            if ((open & (1 << g)) == 0 && c.GateKind[g] != 1) // (2WD is no fault)
                return c.GateKind[g] == 0 ? DriveNote.ClutchOut : DriveNote.JointAngle;
        return DriveNote.None;
    }

    /// <summary>The wheel-side torque cap for wheel <paramref name="k"/>: what its drive line carries (a belt).</summary>
    public static float WheelTorque(Construct c, int k)
    {
        ref var w = ref c.Wheels[k];
        var t = MathF.Min(c.DriveTorque * c.GearRatio, w.CapEq); // (a belt slips: no more than it carries)
        return t * c.TransEff * w.Ratio * w.Eff * w.Share;
    }

    /// <summary>A blueprint built at the origin: its size (length along its seat's facing, width, height), mass and drive (<c>--check-construct-parts</c>, the self-test).</summary>
    public static string Describe(ConstructBuild.Blueprint bp)
    {
        var c = ConstructBuild.Build(new ConstructWorld(), bp, Vector3.Zero, Quaternion.Identity, out var why);
        if (c == null)
            return $"doesn't build: {why}";
        Size(c, out var length, out var width, out var height);
        var driven = 0;
        for (var k = 0; k < c.WheelCount; k++)
            if (c.Wheels[k].Driven)
                driven++;
        return $"{length:0.00} x {width:0.00} x {height:0.00} m, {c.Mass:0} kg, " + (c.DriveSeat < 0 ? "no seat" : c.CanDrive ? $"drives {driven} of {c.WheelCount} wheel contacts" : $"doesn't drive: {c.DriveProblem}");
    }

    /// <summary>A construct's size in its rest frame: length along its seat's facing, width across it, height.</summary>
    public static void Size(Construct c, out float length, out float width, out float height)
    {
        var ext = c.LocalMax - c.LocalMin;
        var f = c.SeatForward();
        var alongX = MathF.Abs(f.X) > 0.5f;
        length = alongX ? ext.X : ext.Z;
        width = alongX ? ext.Z : ext.X;
        height = ext.Y;
    }

    /// <summary>How fast <paramref name="c"/> goes at its engine's limiter with gear reduction <paramref name="gearRatio"/> (m/s; its slowest driven wheel).</summary>
    public static float TopSpeed(Construct c, float gearRatio)
    {
        if (c.Engine < 0 || c.Engine >= c.PartCount)
            return 0;
        var omega = c.DefOf(c.Engine).MaxRpm / RpmPerRadS;
        var top = float.PositiveInfinity;
        for (var k = 0; k < c.WheelCount; k++)
            if (c.Wheels[k].Driven)
                top = MathF.Min(top, omega / (gearRatio * c.Wheels[k].Ratio) * c.Wheels[k].Radius);
        return float.IsFinite(top) ? top : 0;
    }

    /// <summary>What a note says on the HUD (empty for none).</summary>
    public static string NoteText(DriveNote n) => n switch
    {
        DriveNote.ClutchOut => "the clutch is out: nothing reaches the wheels",
        DriveNote.JointAngle => "the U joint is bent past its angle: the steered wheels lose the drive",
        DriveNote.NeedsStarter => "It won't start: this engine needs a battery for its starter",
        DriveNote.Flat => "the electric motor's battery is flat (or doesn't reach it)",
        DriveNote.Slipping => "the belt is slipping",
        DriveNote.Shifting => "shifting",
        DriveNote.Spooling => "spooling up",
        _ => "",
    };

    /// <summary>A transfer part's numbers for the look line.</summary>
    public static string TransferLine(ConstructPartDef d, in CPart p) => d.Drive switch
    {
        DriveKind.Clutch => p.InCount > 0 && p.In < 0.5f ? "disengaged" : "engaged",
        DriveKind.TransferCase => (p.InCount > 0 ? p.In >= 0.5f : p.Setting == 1) ? "4WD" : "2WD",
        DriveKind.UJoint or DriveKind.CvJoint => $"bends up to {d.MaxAngle * 180 / MathF.PI:0} degrees",
        DriveKind.Stub => "carries no drive",
        DriveKind.Pto => "runs the joints on it from the engine",
        _ => d.DriveRatio != 1 ? $"{d.DriveRatio:0.##}:1{(d.MaxTorque > 0 ? $", slips over {d.MaxTorque:0} N m" : "")}" : d.MaxTorque > 0 ? $"carries up to {d.MaxTorque:0} N m" : "1:1",
    };

    /// <summary>An engine's numbers for the look line (the configure tool is still to come).</summary>
    public static string EngineLine(ConstructPartDef d) =>
        $"{d.EngineTorque:0} N m at {d.PeakRpm:0} rpm, limiter {d.MaxRpm:0}, {(d.FuelKind == EngineFuel.Fuel ? $"{d.BurnRate * 60:0.#} l/min" : d.FuelKind == EngineFuel.Electric ? "battery" : "no fuel")}{(d.NeedsStarter ? ", starter" : "")}{(d.SpoolSeconds > 0 ? $", spools {d.SpoolSeconds:0.#} s" : "")}";
}
