using System.Globalization;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>An engine's torque curve (docs/sandbox/CONSTRUCTS.md, "Drivetrain parts").</summary>
public enum EngineCurve : byte
{
    Petrol, // weak at idle, rises to its peak high up, falls a little to the limiter
    Diesel, // strong from idle, peaks low, falls off early
    Electric, // full torque from standstill to its peak rpm, then constant power to the limiter
    Turbine, // little at idle, its peak high; and it spools up (SpoolSeconds)
    Flat, // the same torque all the way (a hamster wheel)
}

/// <summary>What an engine runs on.</summary>
public enum EngineFuel : byte
{
    Fuel, // a fuel tank's fuel, through fuel lines
    Electric, // the construct's batteries (it needs a battery; no tank)
    None, // nothing (a hamster wheel)
}

/// <summary>How a transmission picks its gear.</summary>
public enum TransKind : byte
{
    Manual, // the driver (R up, sprint down), the linking tool's R or a wired signal picks the gear
    Sequential, // a manual that steps one gear at a time, with quick shifts
    Automatic, // picks its gear by the engine's rpm (a wired signal: the highest gear it may use)
    Cvt, // a continuous range: holds the engine at its peak
    Reversing, // one ratio, as fast backwards as forwards
}

/// <summary>How a differential shares the drive between its wheels.</summary>
public enum DiffKind : byte
{
    Open, // equal shares; a wheel in the air takes it all (little reaches the other)
    Locked, // the wheels turn together: the drive goes to whichever touch, but it fights the steering
    Limited, // limited slip: most of a lifted wheel's share goes to the other
    Lockable, // open, or locked while its setting (R) or a wired signal says so
}

/// <summary>What a transfer part does to the drive (role <c>transfer</c>, the <c>"drive"</c> block).</summary>
public enum DriveKind : byte
{
    Shaft, // straight in one end and out of the other
    UJoint, // carries the drive through a joint (a steering pivot, a hinge) up to its angle
    CvJoint, // the same, further and smoother
    Gearbox, // turns or splits the drive (right-angle, T, reduction): its ports, its ratio
    Chain, // sprockets at either end of a chain: the ratio is the sprockets' sizes
    Belt, // pulleys and a belt: light and quiet, but it slips above its torque
    Clutch, // passes the drive while engaged (unwired: always; wired: while its input is on)
    TransferCase, // passes the drive on; its front output only in 4WD (its setting or a wired signal)
    Pto, // a power take-off: moving joints bolted to it run from the engine (not the battery) while it runs
    Hub, // an independent hub: drives the suspension bolted to it (like one end of a differential)
    Stub, // a stub axle: holds a suspension but carries no drive
}

public static partial class ConstructParts
{
    private static readonly string[] EngineKeys = ["torque", "idleRpm", "peakRpm", "maxRpm", "curve", "fuel", "burnRate", "starter", "spool", "noise"];

    private static void ParseEngine(ConstructPartDef d, JsonElement b, string file, string bw)
    {
        Known(b, EngineKeys, bw);
        d.EngineTorque = Num(Need(b, "torque", bw), file, "engine.torque");
        Range(d.EngineTorque, 1, 100_000, file, "engine.torque");
        d.MaxRpm = Num(Need(b, "maxRpm", bw), file, "engine.maxRpm");
        Range(d.MaxRpm, 100, 60_000, file, "engine.maxRpm");
        d.Curve = Opt(b, "curve") is { } cv ? EnumOf<EngineCurve>(Str(cv, file, "engine.curve"), file, "engine.curve") : EngineCurve.Petrol;
        d.FuelKind = Opt(b, "fuel") is { } fu ? EnumOf<EngineFuel>(Str(fu, file, "engine.fuel"), file, "engine.fuel") : d.Curve == EngineCurve.Electric ? EngineFuel.Electric : EngineFuel.Fuel;
        d.IdleRpm = Opt(b, "idleRpm") is { } ir ? Num(ir, file, "engine.idleRpm") : d.FuelKind == EngineFuel.Electric ? 0 : d.MaxRpm * 0.15f;
        Range(d.IdleRpm, 0, d.MaxRpm * 0.6f, file, "engine.idleRpm");
        d.PeakRpm = Opt(b, "peakRpm") is { } pr ? Num(pr, file, "engine.peakRpm") : d.MaxRpm * 0.6f;
        if (d.PeakRpm <= d.IdleRpm || d.PeakRpm > d.MaxRpm)
            throw new ConstructPartException($"{file}: \"engine.peakRpm\" must be above \"idleRpm\" and at most \"maxRpm\"");
        d.BurnRate = Opt(b, "burnRate") is { } br ? Num(br, file, "engine.burnRate") : d.FuelKind == EngineFuel.Fuel ? 0.05f : 0f;
        Range(d.BurnRate, 0, 100, file, "engine.burnRate");
        if (d.FuelKind != EngineFuel.Fuel && d.BurnRate > 0)
            throw new ConstructPartException($"{file}: only an engine that runs on fuel has a \"burnRate\"");
        d.NeedsStarter = Opt(b, "starter") is { ValueKind: JsonValueKind.True };
        if (d.NeedsStarter && d.FuelKind == EngineFuel.Electric)
            throw new ConstructPartException($"{file}: an electric motor needs no \"starter\" (it runs on the battery)");
        d.SpoolSeconds = Opt(b, "spool") is { } sp ? Num(sp, file, "engine.spool") : 0f;
        Range(d.SpoolSeconds, 0, 30, file, "engine.spool");
        d.Noise = Opt(b, "noise") is { } nz ? Num(nz, file, "engine.noise") : 0.5f;
        Range(d.Noise, 0, 1, file, "engine.noise");
    }

    private static void ParseWheel(ConstructPartDef d, JsonElement b, string file, string bw)
    {
        Known(b, ["radius", "grip", "offroad", "side", "roll", "drive", "flat", "track"], bw);
        d.WheelRadius = Num(Need(b, "radius", bw), file, "wheel.radius");
        Range(d.WheelRadius, 0.05f, 3, file, "wheel.radius");
        d.Grip = Opt(b, "grip") is { } g ? Num(g, file, "wheel.grip") : 1f;
        Range(d.Grip, 0.05f, 4, file, "wheel.grip");
        d.OffroadGrip = Opt(b, "offroad") is { } o ? Num(o, file, "wheel.offroad") : 1f;
        Range(d.OffroadGrip, 0.1f, 3, file, "wheel.offroad");
        d.SideGrip = Opt(b, "side") is { } sg ? Num(sg, file, "wheel.side") : 1f;
        Range(d.SideGrip, 0.01f, 2, file, "wheel.side");
        d.RollResist = Opt(b, "roll") is { } rr ? Num(rr, file, "wheel.roll") : 0.01f;
        Range(d.RollResist, 0, 0.5f, file, "wheel.roll");
        d.Drivable = Opt(b, "drive") is not { ValueKind: JsonValueKind.False };
        d.FlatTyre = Opt(b, "flat") is { ValueKind: JsonValueKind.True };
        if (Opt(b, "track") is { } tr)
        {
            if (tr.ValueKind != JsonValueKind.Object)
                throw new ConstructPartException($"{file}: \"wheel.track\" is an object (length, rays)");
            Known(tr, ["length", "rays"], $"{bw}.track");
            d.TrackLength = Num(Need(tr, "length", $"{bw}.track"), file, "wheel.track.length");
            Range(d.TrackLength, 0.5f, 8, file, "wheel.track.length");
            d.TrackRays = (int)Num(Need(tr, "rays", $"{bw}.track"), file, "wheel.track.rays");
            Range(d.TrackRays, 2, 6, file, "wheel.track.rays");
        }
        if (!d.Sockets.Any(s => s.Kind == SocketKind.Axle))
            throw new ConstructPartException($"{file}: a wheel needs an \"axle\" socket (it bolts onto a suspension)");
    }

    private static void ParseSuspension(ConstructPartDef d, JsonElement b, string file, string bw)
    {
        Known(b, ["travel", "frequency", "damping", "maxLoad", "active", "presets", "setting"], bw);
        d.Travel = Num(Need(b, "travel", bw), file, "suspension.travel");
        Range(d.Travel, 0.02f, 2, file, "suspension.travel");
        d.Frequency = Opt(b, "frequency") is { } f ? Num(f, file, "suspension.frequency") : 2f;
        Range(d.Frequency, 0.3f, 6, file, "suspension.frequency"); // stiffer than 6 Hz is unstable at 60 Hz
        d.Damping = Opt(b, "damping") is { } dm ? Num(dm, file, "suspension.damping") : 0.6f;
        Range(d.Damping, 0, 2, file, "suspension.damping");
        d.MaxLoad = Opt(b, "maxLoad") is { } ml ? Num(ml, file, "suspension.maxLoad") : 100000f;
        Range(d.MaxLoad, 10, 100000, file, "suspension.maxLoad");
        d.ActiveSuspension = Opt(b, "active") is { ValueKind: JsonValueKind.True };
        if (Opt(b, "presets") is { } ps)
        {
            if (ps.ValueKind != JsonValueKind.Array || ps.GetArrayLength() is < 2 or > 6)
                throw new ConstructPartException($"{file}: \"suspension.presets\" lists 2 to 6 presets (label, frequency, damping, hold)");
            if (d.ActiveSuspension)
                throw new ConstructPartException($"{file}: an active suspension takes its height from its signal: no \"presets\"");
            var n = ps.GetArrayLength();
            d.Settings = new float[n];
            d.SettingLabels = new string[n];
            d.SuspFrequency = new float[n];
            d.SuspDamping = new float[n];
            d.SuspHold = new float[n];
            var k = 0;
            foreach (var p in ps.EnumerateArray())
            {
                var w = $"{bw}.presets[{k}]";
                Known(p, ["label", "frequency", "damping", "hold"], w);
                d.Settings[k] = k;
                d.SettingLabels[k] = AsciiText(Need(p, "label", w), file, "suspension.presets.label");
                d.SuspFrequency[k] = Num(Need(p, "frequency", w), file, "suspension.presets.frequency");
                Range(d.SuspFrequency[k], 0.3f, 6, file, "suspension.presets.frequency");
                d.SuspDamping[k] = Opt(p, "damping") is { } pd ? Num(pd, file, "suspension.presets.damping") : d.Damping;
                Range(d.SuspDamping[k], 0, 2, file, "suspension.presets.damping");
                d.SuspHold[k] = Opt(p, "hold") is { } ph ? Num(ph, file, "suspension.presets.hold") : -1f;
                if (d.SuspHold[k] != -1f)
                    Range(d.SuspHold[k], 0, 1, file, "suspension.presets.hold");
                k++;
            }
            d.SettingName = "setting";
            if (Opt(b, "setting") is { } s0)
            {
                var v = Num(s0, file, "suspension.setting");
                if (v < 0 || v >= n || v != MathF.Floor(v))
                    throw new ConstructPartException($"{file}: \"suspension.setting\" is the index of its starting preset (0 to {n - 1})");
                d.DefaultSetting = (byte)v;
            }
        }
        if (!d.Sockets.Any(s => s.Kind == SocketKind.Axle))
            throw new ConstructPartException($"{file}: a suspension needs an \"axle\" socket for its wheel");
    }

    private static void ParseTransmission(ConstructPartDef d, JsonElement? block, string file, string bw)
    {
        d.Trans = TransKind.Manual;
        d.TransEfficiency = 0.95f;
        d.ShiftSeconds = 0.4f;
        if (block == null)
        {
            d.ReverseRatio = 1f;
            return;
        }
        var b = block.Value;
        Known(b, ["kind", "gears", "labels", "gear", "reverse", "shift", "efficiency", "range"], bw);
        d.Trans = Opt(b, "kind") is { } k ? EnumOf<TransKind>(Str(k, file, "transmission.kind"), file, "transmission.kind") : TransKind.Manual;
        (d.ShiftSeconds, d.TransEfficiency) = d.Trans switch
        {
            TransKind.Sequential => (0.12f, 0.95f),
            TransKind.Automatic => (0.3f, 0.87f),
            TransKind.Cvt => (0f, 0.84f),
            TransKind.Reversing => (0.5f, 0.94f),
            _ => (0.4f, 0.95f),
        };
        if (d.Trans == TransKind.Cvt)
        {
            if (Opt(b, "gears") != null)
                throw new ConstructPartException($"{file}: a CVT has a \"range\" (its highest and lowest reduction), not \"gears\"");
            if (Opt(b, "range") is not { ValueKind: JsonValueKind.Array } rg || rg.GetArrayLength() != 2)
                throw new ConstructPartException($"{file}: a CVT needs \"range\": [its highest reduction, its lowest]");
            d.CvtLow = Num(rg[0], file, "transmission.range");
            d.CvtHigh = Num(rg[1], file, "transmission.range");
            if (d.CvtLow > 20 || d.CvtHigh < 0.2f || d.CvtHigh >= d.CvtLow)
                throw new ConstructPartException($"{file}: a CVT's \"range\" goes from its highest reduction down to its lowest, within 20 to 0.2");
            d.ReverseRatio = d.CvtLow;
            d.SettingLabels = ["CVT"];
        }
        else
        {
            if (Opt(b, "range") != null)
                throw new ConstructPartException($"{file}: only a CVT has a \"range\"");
            if (Opt(b, "gears") is not { ValueKind: JsonValueKind.Array } gears || gears.GetArrayLength() is 0 or > 8)
                throw new ConstructPartException($"{file}: \"transmission.gears\" lists 1 to 8 reductions (the engine turns that many times for each turn out)");
            d.Settings = gears.EnumerateArray().Select(v => Num(v, file, "transmission.gears")).ToArray();
            if (d.Settings.Any(v => v < 0.2f || v > 20f))
                throw new ConstructPartException($"{file}: a gear ratio is 0.2 to 20");
            for (var i = 1; i < d.Settings.Length; i++)
                if (d.Settings[i] >= d.Settings[i - 1])
                    throw new ConstructPartException($"{file}: the gears go from the lowest (the biggest reduction) up");
            if (d.Trans == TransKind.Reversing && d.Settings.Length != 1)
                throw new ConstructPartException($"{file}: a reversing gearbox has one ratio (forwards and back)");
            d.SettingLabels = Opt(b, "labels") is { ValueKind: JsonValueKind.Array } lab
                ? lab.EnumerateArray().Select(v => AsciiText(v, file, "transmission.labels")).ToArray()
                : d.Settings.Select((_, i) => (i + 1).ToString(CultureInfo.InvariantCulture)).ToArray();
            if (d.SettingLabels.Length != d.Settings.Length)
                throw new ConstructPartException($"{file}: \"transmission.labels\" must have one label per gear");
            d.SettingName = "gear";
            if (Opt(b, "gear") is { } g0)
            {
                var v = Num(g0, file, "transmission.gear");
                if (v < 1 || v > d.Settings.Length || v != MathF.Floor(v))
                    throw new ConstructPartException($"{file}: \"transmission.gear\" is the starting gear, 1 to {d.Settings.Length}");
                d.DefaultSetting = (byte)(v - 1);
            }
            d.ReverseRatio = d.Trans == TransKind.Reversing ? d.Settings[0] : d.Settings[0] * 1.1f;
        }
        if (Opt(b, "reverse") is { } rv)
        {
            d.ReverseRatio = Num(rv, file, "transmission.reverse");
            Range(d.ReverseRatio, 0.2f, 20, file, "transmission.reverse");
        }
        if (Opt(b, "shift") is { } sh)
        {
            d.ShiftSeconds = Num(sh, file, "transmission.shift");
            Range(d.ShiftSeconds, 0, 3, file, "transmission.shift");
        }
        if (Opt(b, "efficiency") is { } ef)
        {
            d.TransEfficiency = Num(ef, file, "transmission.efficiency");
            Range(d.TransEfficiency, 0.3f, 1, file, "transmission.efficiency");
        }
    }

    private static void ParseDifferential(ConstructPartDef d, JsonElement? block, string file, string bw)
    {
        d.Diff = DiffKind.Open;
        d.DiffRatio = 3.5f;
        d.DiffEfficiency = 0.97f;
        if (block is { } b)
        {
            Known(b, ["kind", "ratio", "efficiency"], bw);
            d.Diff = Opt(b, "kind") is { } k ? EnumOf<DiffKind>(Str(k, file, "differential.kind"), file, "differential.kind") : DiffKind.Open;
            d.DiffRatio = Opt(b, "ratio") is { } r ? Num(r, file, "differential.ratio") : 3.5f;
            Range(d.DiffRatio, 0.2f, 20, file, "differential.ratio");
            d.DiffEfficiency = Opt(b, "efficiency") is { } e ? Num(e, file, "differential.efficiency") : 0.97f;
            Range(d.DiffEfficiency, 0.3f, 1, file, "differential.efficiency");
        }
        if (d.Diff == DiffKind.Lockable)
        {
            d.Settings = [0, 1];
            d.SettingLabels = ["open", "locked"];
            d.SettingName = "lock";
        }
    }

    private static void ParseTransfer(ConstructPartDef d, JsonElement b, string file, string bw)
    {
        Known(b, ["kind", "ratio", "efficiency", "ports", "maxAngle", "maxTorque", "front", "setting"], bw);
        d.Drive = EnumOf<DriveKind>(Str(Need(b, "kind", bw), file, "drive.kind").Replace("-", ""), file, "drive.kind");
        d.DriveRatio = Opt(b, "ratio") is { } r ? Num(r, file, "drive.ratio") : 1f;
        Range(d.DriveRatio, 0.1f, 10, file, "drive.ratio");
        if (d.DriveRatio != 1f && d.Drive is not (DriveKind.Gearbox or DriveKind.Chain or DriveKind.Belt or DriveKind.Hub))
            throw new ConstructPartException($"{file}: only gearboxes, chains, belts and hubs have a \"ratio\"");
        d.DriveEfficiency = Opt(b, "efficiency") is { } e ? Num(e, file, "drive.efficiency") : 0.98f;
        Range(d.DriveEfficiency, 0.3f, 1, file, "drive.efficiency");
        if (Opt(b, "ports") is { } ps)
        {
            if (ps.ValueKind != JsonValueKind.Array || ps.GetArrayLength() is < 1 or > 6)
                throw new ConstructPartException($"{file}: \"drive.ports\" lists 1 to 6 of its sockets (where the drive goes in and out)");
            d.Ports = ps.EnumerateArray().Select(v =>
            {
                var name = Str(v, file, "drive.ports");
                var i = Array.FindIndex(d.Sockets, s => s.Name == name);
                return i >= 0 ? i : throw new ConstructPartException($"{file}: \"drive.ports\" names \"{name}\", which isn't one of its sockets");
            }).ToArray();
        }
        if (d.Drive is DriveKind.UJoint or DriveKind.CvJoint)
        {
            d.MaxAngle = Num(Need(b, "maxAngle", bw), file, "drive.maxAngle") * MathF.PI / 180f;
            Range(d.MaxAngle, 1 * MathF.PI / 180f, 80 * MathF.PI / 180f, file, "drive.maxAngle");
        }
        else if (Opt(b, "maxAngle") != null)
            throw new ConstructPartException($"{file}: only U joints and CV joints have a \"maxAngle\"");
        d.MaxTorque = Opt(b, "maxTorque") is { } mt ? Num(mt, file, "drive.maxTorque") : 0f;
        Range(d.MaxTorque, 0, 1_000_000, file, "drive.maxTorque");
        if (d.Drive == DriveKind.TransferCase)
        {
            var front = Str(Need(b, "front", bw), file, "drive.front");
            d.FrontPort = Array.FindIndex(d.Sockets, s => s.Name == front);
            if (d.FrontPort < 0 || d.Ports.Length > 0 && !d.Ports.Contains(d.FrontPort))
                throw new ConstructPartException($"{file}: \"drive.front\" names one of its ports (the output 4WD adds)");
            d.Settings = [0, 1];
            d.SettingLabels = ["2WD", "4WD"];
            d.SettingName = "drive";
        }
        else if (Opt(b, "front") != null)
            throw new ConstructPartException($"{file}: only a transfer case has a \"front\" output");
        if (Opt(b, "setting") is { } s0)
        {
            var v = Num(s0, file, "drive.setting");
            if (d.Settings.Length == 0 || v < 0 || v >= d.Settings.Length || v != MathF.Floor(v))
                throw new ConstructPartException($"{file}: \"drive.setting\" is the starting setting of a part that has one (a transfer case: 0 2WD, 1 4WD)");
            d.DefaultSetting = (byte)v;
        }
    }
}
