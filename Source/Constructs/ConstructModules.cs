using System.Numerics;

namespace CallOfSlop;

/// <summary>One Turret Module with a seat linked into it (with the caches): which of that seat's modules it is.</summary>
public struct CTurretMod
{
    public ushort Part, Seat;
    public byte Slot, Of; // its place among the seat's Turret Modules, and how many it has
}

/// <summary>
/// Control modules (docs/sandbox/CONSTRUCTS.md, "Control modules"). A seat does nothing by itself: one data link from it
/// (its one output) carries its sitter's controls into a module, and the module breaks them out. A Driving Module's
/// "drive" output, linked into any part of the drivetrain, hands the whole construct to that seat
/// (<see cref="Construct.DriveSeat"/>); its other outputs are the same controls one by one, for a builder who wires by
/// hand. A Turret Module's "guns" output, linked into a gun, gives that gun and the joints under it to the seat. Everything
/// here runs from the keys the seat holds this tick; nothing allocates.
/// </summary>
public static class ConstructModules
{
    public const string Bus = "drive"; // the input port a Driving Module's drive output ends at (it carries no signal to the part)
    public const float InUse = -0.2f; // a Turret Module's "guns" output while it is the seat's turret and isn't firing (never a trigger, and clients see which is in use)
    public const int MaxTurretMods = 32;

    public static readonly string[] DrivePorts = [Bus, "steer", "throttle", "brake", "reverse", "handbrake", "run", "gear up", "gear down", "lift", "left", "right", "roll"]; // (add at the end: a link names its port by index)
    public static readonly string[] TurretPorts = ["guns", "fire", "yaw", "pitch", "aim", "reload"];
    private const int PortThrottle = 2, PortBrake = 3, PortReverse = 4, PortHandbrake = 5, PortRun = 6, PortUp = 7, PortDown = 8, PortLift = 9, PortLeft = 10, PortRight = 11, PortRoll = 12;
    private const int PortYaw = 2, PortPitch = 3, PortAim = 4, PortReload = 5;

    /// <summary>The input port a drive link ends at on this part, or -1: it isn't part of a drivetrain.</summary>
    public static int BusPort(ConstructPartDef d) => Array.IndexOf(d.DataIn, Bus);

    /// <summary>Does a data link leaving this port carry the whole drive?</summary>
    public static bool IsBus(ConstructPartDef d, byte port) => d.Logic == CLogic.DriveModule && port == 255;

    /// <summary>The seat linked into a module, or -1.</summary>
    public static int SeatOf(Construct c, int module)
    {
        for (var k = 0; k < c.WireCount; k++)
            if (c.Wires[k].To == module && c.Wires[k].From < c.PartCount && c.Parts[c.Wires[k].From].D.Role == PartRole.Seat)
                return c.Wires[k].From;
        return -1;
    }

    /// <summary>With the caches, after the wires: the seat that drives, the seat forward is taken from, and the turrets.</summary>
    public static void Build(Construct c)
    {
        c.DriveSeat = -1;
        c.ControlSeat = -1;
        c.TurretModCount = 0;
        for (var i = 0; i < c.PartCount; i++)
        {
            ref var p = ref c.Parts[i];
            if (p.D.Logic is not (CLogic.DriveModule or CLogic.TurretModule) || !p.Working)
                continue;
            var seat = SeatOf(c, i);
            if (seat < 0 || !c.Parts[seat].Working)
                continue;
            if (p.D.Logic == CLogic.TurretModule)
            {
                AddTurret(c, i, seat);
                continue;
            }
            if (c.ControlSeat < 0)
                c.ControlSeat = seat;
            if (!Linked(c, i))
                continue;
            if (c.DriveSeat < 0 || c.Parts[seat].D.Helm && !c.Parts[c.DriveSeat].D.Helm)
                c.DriveSeat = seat; // (two modules that both reach the drivetrain: the helm's, else the first)
        }
        if (c.DriveSeat >= 0)
            c.ControlSeat = c.DriveSeat;
    }

    private static void AddTurret(Construct c, int module, int seat)
    {
        if (c.TurretModCount >= MaxTurretMods)
            return;
        if (c.TurretMods.Length == c.TurretModCount)
            Array.Resize(ref c.TurretMods, Math.Min(MaxTurretMods, Math.Max(4, c.TurretModCount * 2)));
        byte slot = 0;
        for (var k = 0; k < c.TurretModCount; k++)
            if (c.TurretMods[k].Seat == seat)
            {
                slot++;
                c.TurretMods[k].Of++;
            }
        c.TurretMods[c.TurretModCount++] = new CTurretMod { Part = (ushort)module, Seat = (ushort)seat, Slot = slot, Of = (byte)(slot + 1) };
    }

    /// <summary>Does a Driving Module's drive output reach a part of the drivetrain?</summary>
    private static bool Linked(Construct c, int module)
    {
        for (var k = 0; k < c.WireCount; k++)
            if (c.Wires[k].From == module && c.Wires[k].FromPort == 0 && c.Wires[k].To < c.PartCount && BusPort(c.Parts[c.Wires[k].To].D) >= 0)
                return true;
        return false;
    }

    /// <summary>Is anything wired to any of a Driving Module's outputs (its drive link or a single control)?</summary>
    private static bool AnyOutput(Construct c, int module)
    {
        for (var k = 0; k < c.WireCount; k++)
            if (c.Wires[k].From == module)
                return true;
        return false;
    }

    /// <summary>The Turret Module of <paramref name="part"/>, an index into <see cref="Construct.TurretMods"/>, or -1.</summary>
    private static int ModIndex(Construct c, int part)
    {
        for (var k = 0; k < c.TurretModCount; k++)
            if (c.TurretMods[k].Part == part)
                return k;
        return -1;
    }

    /// <summary>Is <paramref name="seat"/> linked to a Turret Module?</summary>
    public static bool HasTurret(Construct c, int seat)
    {
        for (var k = 0; k < c.TurretModCount; k++)
            if (c.TurretMods[k].Seat == seat)
                return true;
        return false;
    }

    /// <summary>The seat's Aim key steps round its Turret Modules (its State: the one in use, then the key last tick).</summary>
    public static void SeatCycle(Construct c, int seat, ref CPart p)
    {
        var of = 0;
        for (var k = 0; k < c.TurretModCount && of == 0; k++)
            if (c.TurretMods[k].Seat == seat)
                of = c.TurretMods[k].Of;
        if (of < 2)
            return;
        var down = (p.Keys & ConstructParts.KeyAim) != 0;
        var was = (p.State & 0x80) != 0;
        var at = p.State & 0x7F;
        if (down && !was)
            at = (at + 1) % of;
        p.State = (byte)(at | (down ? 0x80 : 0));
    }

    /// <summary>One tick of a Driving Module: who sits in its seat (its Count, plus one) and its steering, eased like a driver's hands. Its output: 1 while someone does.</summary>
    public static float StepDrive(Construct c, ref CPart p, int seat, float dt)
    {
        var manned = seat >= 0 && seat < c.PartCount && (c.Parts[seat].Keys & CPart.Occupied) != 0;
        p.Count = (ushort)(manned ? seat + 1 : 0);
        var keys = manned ? c.Parts[seat].Keys : (ushort)0;
        var steer = (Has(keys, Buttons.Left) ? 1f : 0f) - (Has(keys, Buttons.Right) ? 1f : 0f); // (left is plus: a steering pivot's wired input is its turn about up)
        var speed = MathF.Abs(Vector3.Dot(c.Vel, Vector3.Transform(c.DriveFwd, c.Rot)));
        var rate = 4.5f * dt; // (ConstructEdits.DriveInput's rate, out of its full lock)
        p.Out1 += Math.Clamp(steer / (1f + speed * 0.06f) - p.Out1, -rate, rate);
        return manned ? 1 : 0;
    }

    /// <summary>One tick of a Turret Module: its seat's sitter, while it is the turret that seat is working (its Count, plus one, for the guns). Its output: 1 firing, <see cref="InUse"/> in use, else 0.</summary>
    public static float StepTurret(Construct c, int module, ref CPart p, int seat)
    {
        var at = ModIndex(c, module);
        var manned = at >= 0 && seat == c.TurretMods[at].Seat && (c.Parts[seat].Keys & CPart.Occupied) != 0
                     && (c.TurretMods[at].Of < 2 || (c.Parts[seat].State & 0x7F) % c.TurretMods[at].Of == c.TurretMods[at].Slot);
        p.Count = (ushort)(manned ? seat + 1 : 0);
        var fire = manned && (c.Parts[seat].Keys & ConstructParts.KeyFire) != 0;
        p.Out1 = fire ? 1 : 0;
        return fire ? 1 : manned ? InUse : 0;
    }

    /// <summary>A module's output <paramref name="port"/> this tick (port 2 and up; 0 and 1 are the part's own values).</summary>
    public static float Output(Construct c, int part, int port)
    {
        ref var p = ref c.Parts[part];
        if (p.D.WidePorts)
            return LogicBlocks.OutPort(c, part, port); // (a Data Bus's breakout)
        var seat = p.Count - 1;
        if (seat < 0 || seat >= c.PartCount)
            return 0;
        var keys = c.Parts[seat].Keys;
        return p.D.Logic switch
        {
            CLogic.DriveModule => DriveOutput(c, keys, port),
            CLogic.TurretModule => TurretOutput(c, seat, keys, port),
            _ => 0,
        };
    }

    private static float DriveOutput(Construct c, ushort keys, int port)
    {
        var throttle = (Has(keys, Buttons.Forward) ? 1f : 0f) - (Has(keys, Buttons.Back) ? 1f : 0f);
        var steer = (Has(keys, Buttons.Right) ? 1f : 0f) - (Has(keys, Buttons.Left) ? 1f : 0f);
        var rolling = Vector3.Dot(c.Vel, Vector3.Transform(c.DriveFwd, c.Rot)) > 1f; // (Back while rolling forward brakes first: ConstructEdits.DriveInput)
        var braking = throttle < 0 && rolling;
        if (braking)
            throttle = 0;
        return port switch
        {
            PortThrottle => throttle,
            PortBrake => braking || Has(keys, Buttons.Jump) ? 1 : 0,
            PortReverse => throttle < 0 ? 1 : 0,
            PortHandbrake => Has(keys, Buttons.Jump) ? 1 : 0,
            PortRun => 1,
            PortUp => (keys & ConstructParts.KeyReload) != 0 ? 1 : 0,
            PortDown => Has(keys, Buttons.Sprint) ? 1 : 0,
            PortLift => (Has(keys, Buttons.Sprint) ? 1 : 0) - (Has(keys, Buttons.Crouch) ? 1 : 0),
            PortLeft => Math.Clamp(throttle + steer, -1, 1),
            PortRight => Math.Clamp(throttle - steer, -1, 1),
            PortRoll => ConstructEdits.RollOf(keys),
            _ => 0,
        };
    }

    private static float TurretOutput(Construct c, int seat, ushort keys, int port)
    {
        switch (port)
        {
            case PortAim:
                return (keys & ConstructParts.KeyAim) != 0 ? 1 : 0;
            case PortReload:
                return (keys & ConstructParts.KeyReload) != 0 ? 1 : 0;
            case PortYaw:
            case PortPitch:
            {
                // The look against the seat's own frame: yaw -1..1 is half a turn either way (right is plus), pitch -1..1 straight down to straight up.
                ref var s = ref c.Parts[seat];
                c.WorldFrame(s.Seg, out var q, out _);
                var frame = q * Rot24.Quat(s.Rot);
                var look = Vector3.Transform(World.Forward(s.AimYaw, s.AimPitch), Quaternion.Conjugate(frame));
                return port == PortYaw ? MathF.Atan2(look.X, -look.Z) / MathF.PI : MathF.Asin(Math.Clamp(look.Y, -1, 1)) / (MathF.PI * 0.5f);
            }
            default:
                return 0;
        }
    }

    private static bool Has(ushort keys, Buttons b) => (keys & (ushort)b) != 0;

    /// <summary>Why a data link from <paramref name="fromPort"/> of <paramref name="fd"/> can't end at <paramref name="toPort"/> of <paramref name="td"/> by this rule, or null: a module reads a seat, and the drive output goes to a drive port and nothing else does.</summary>
    public static string? LinkWhy(ConstructPartDef fd, byte fromPort, ConstructPartDef td, byte toPort)
    {
        if ((td.IsSeatModule || td.Logic == CLogic.RemoteLink) && fd.Role != PartRole.Seat)
            return $"a {td.Name} reads a seat: link it from one";
        var bus = BusPort(td);
        var intoBus = bus > 0 && toPort == ConstructRoutes.DataPortByte(bus);
        if (IsBus(fd, fromPort))
            return bus < 0 ? $"the drive output goes to the drivetrain: an engine, gearbox, steering pivot, thruster or hub motor, not a {td.Name}"
                : intoBus ? null : $"the drive output ends at the {td.Name}'s drive port";
        return intoBus ? $"only a Driving Module's drive output ends at the drive port" : null;
    }

    /// <summary>What is wrong with a seat or a module on the look line and the HUD, or null: why this seat controls nothing, or why this module does nothing.</summary>
    public static string? Problem(Construct c, int part)
    {
        if (part < 0 || part >= c.PartCount)
            return null;
        var d = c.DefOf(part);
        return d.Role == PartRole.Seat ? SeatProblem(c, part) : d.Logic == CLogic.DriveModule ? DriveProblem(c, part) : d.Logic == CLogic.TurretModule ? TurretProblem(c, part) : null;
    }

    private static string? SeatProblem(Construct c, int seat)
    {
        if (seat == c.DriveSeat || c.Parts[seat].D.RemoteRange > 0)
            return null; // (a console flies something else: ConstructRemote.SeatProblem)
        var driving = false;
        var turret = false;
        for (var k = 0; k < c.WireCount; k++)
            if (c.Wires[k].From == seat && c.Wires[k].To < c.PartCount)
            {
                ref var p = ref c.Parts[c.Wires[k].To];
                if (p.D.Logic == CLogic.DriveModule)
                {
                    driving = true;
                    if (DriveProblem(c, c.Wires[k].To) is { } why)
                        return why;
                }
                else if (p.D.Logic == CLogic.TurretModule)
                {
                    turret = true;
                    if (TurretProblem(c, c.Wires[k].To) is { } why)
                        return why;
                }
            }
        if (driving || turret)
            return null;
        if (c.DriveSeat >= 0)
            return !c.Rigid(c.Parts[seat].Seg) && GunOnChain(c, seat) ? "no Turret Module: this seat doesn't work the gun" : null; // (a passenger's seat on a driven vehicle is no fault)
        if (c.Engine >= 0 || c.HasHubMotor || c.ThrusterCount > 0 || c.WheelCount > 0)
            return "no Driving Module linked: this seat controls nothing";
        return c.GunCount > 0 ? "no Turret Module linked: this seat doesn't work the guns" : null;
    }

    private static string? DriveProblem(Construct c, int module)
    {
        var seat = SeatOf(c, module);
        if (seat < 0)
            return "no seat linked into it";
        if (c.Parts[module].Broken)
            return "wrecked";
        if (!AnyOutput(c, module))
            return "drive output not linked: link it to the engine, gearbox or other drive part";
        return null;
    }

    private static string? TurretProblem(Construct c, int module)
    {
        var seat = SeatOf(c, module);
        if (seat < 0)
            return "no seat linked into it";
        if (c.Parts[module].Broken)
            return "wrecked";
        if (!AnyOutput(c, module))
            return "guns output not linked: link it to a gun";
        return null;
    }

    private static bool GunOnChain(Construct c, int seat)
    {
        for (int s = c.Parts[seat].Seg, guard = 0; guard <= Construct.MaxJoints; guard++)
        {
            for (var k = 0; k < c.GunCount; k++)
                if (c.Guns[k].Seg == s || GunOver(c, k, s))
                    return true;
            if (s <= 0 || s > c.JointCount)
                break;
            s = c.Joints[s - 1].ParentSeg;
        }
        return false;
    }

    private static bool GunOver(Construct c, int k, int seg)
    {
        for (int s = c.Guns[k].Seg, guard = 0; guard <= Construct.MaxJoints && s > 0 && s <= c.JointCount; guard++)
        {
            s = c.Joints[s - 1].ParentSeg;
            if (s == seg && s != 0)
                return true;
        }
        return false;
    }

    /// <summary>The look line's state of a module: who it answers to.</summary>
    public static string Look(Construct c, int part)
    {
        var d = c.DefOf(part);
        if (d.Logic == CLogic.DriveModule)
        {
            var seat = SeatOf(c, part);
            return seat >= 0 && seat == c.DriveSeat ? "drives this construct" : seat >= 0 && !c.Parts[seat].Broken && AnyOutput(c, part) ? "wired by hand" : "";
        }
        var at = ModIndex(c, part);
        return at < 0 ? "" : c.TurretMods[at].Of > 1 ? $"turret {c.TurretMods[at].Slot + 1} of {c.TurretMods[at].Of} on its seat" : "";
    }

    /// <summary>The gun the sitter of <paramref name="seat"/> is working now (the module in use's first), or the first of any of its modules, or -1.</summary>
    public static int ActiveGun(Construct c, int seat)
    {
        var any = -1;
        for (var k = 0; k < c.GunCount; k++)
        {
            ref var g = ref c.Guns[k];
            if (g.Seat != seat)
                continue;
            if (any < 0)
                any = k;
            if (g.Turret >= 0 && g.Turret < c.PartCount && c.Parts[g.Turret].Out != 0)
                return k;
        }
        return any;
    }

    /// <summary>How many Turret Modules a seat has (the HUD's "next turret").</summary>
    public static int TurretsOf(Construct c, int seat)
    {
        for (var k = 0; k < c.TurretModCount; k++)
            if (c.TurretMods[k].Seat == seat)
                return c.TurretMods[k].Of;
        return 0;
    }
}
