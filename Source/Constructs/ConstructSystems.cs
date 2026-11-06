using System.Text.Json;

namespace CallOfSlop;

/// <summary>
/// The part-file blocks of the big systems (docs/sandbox/CONSTRUCTS.md, "Large systems"): <c>"dock"</c> on a cargo logic
/// part (a docking clamp or magnetic lock), <c>"launch"</c> on a catapult or arrestor, <c>"conveyor"</c> on a conveyor, <c>"jump"</c> on a jump drive (phase 6), and
/// <c>"winch"</c> in a hook's hitch block. Refused with the reason, like everything else in a part file.
/// </summary>
public static partial class ConstructParts
{
    private static void ParseSystems(ConstructPartDef d, JsonElement root, string file)
    {
        if (Opt(root, "dock") is { } dock)
        {
            if (d.Logic != CLogic.Cargo)
                throw new ConstructPartException($"{file}: only a cargo logic part has a \"dock\" block");
            Known(dock, ["side", "settle", "maxMass", "charge", "refuel", "chute", "pull", "eject"], $"{file}, dock");
            d.DockSide = Num(Need(dock, "side", $"{file}, dock"), file, "dock.side");
            Range(d.DockSide, ConstructCargo.MinSide, 128, file, "dock.side");
            d.DockSettle = Opt(dock, "settle") is { } st ? Num(st, file, "dock.settle") : ConstructCargo.SettleSpeed;
            Range(d.DockSettle, 0.1f, 30, file, "dock.settle");
            d.DockMaxMass = Opt(dock, "maxMass") is { } mm ? Num(mm, file, "dock.maxMass") : float.MaxValue;
            if (d.DockMaxMass < 1)
                throw new ConstructPartException($"{file}: \"dock.maxMass\" is at least 1 kg");
            d.DockCharge = Opt(dock, "charge") is { } ch ? Num(ch, file, "dock.charge") : 0f;
            Range(d.DockCharge, 0, 100000, file, "dock.charge");
            d.DockFuel = Opt(dock, "refuel") is { } rf ? Num(rf, file, "dock.refuel") : 0f;
            Range(d.DockFuel, 0, 10000, file, "dock.refuel");
            d.DockChute = Opt(dock, "chute") is { ValueKind: JsonValueKind.True };
            d.DockPull = Opt(dock, "pull") is { } pl ? Num(pl, file, "dock.pull") : 0f;
            Range(d.DockPull, 0, 256, file, "dock.pull");
            d.DockEject = Opt(dock, "eject") is { } ej ? Num(ej, file, "dock.eject") : 0f;
            Range(d.DockEject, 0, 60, file, "dock.eject");
        }
        var launch = Opt(root, "launch");
        if (launch is { } lb)
        {
            if (d.Logic is not (CLogic.Launch or CLogic.Arrest))
                throw new ConstructPartException($"{file}: only a catapult or arrestor (logic kind launch, arrest) has a \"launch\" block");
            Known(lb, ["width", "height", "accel", "maxMass", "speed", "reach"], $"{file}, launch");
            d.LaunchWidth = Num(Need(lb, "width", $"{file}, launch"), file, "launch.width");
            Range(d.LaunchWidth, 0.5f, 128, file, "launch.width");
            d.LaunchHeight = Num(Need(lb, "height", $"{file}, launch"), file, "launch.height");
            Range(d.LaunchHeight, 0.5f, 64, file, "launch.height");
            d.LaunchAccel = Num(Need(lb, "accel", $"{file}, launch"), file, "launch.accel");
            Range(d.LaunchAccel, 0.5f, 500, file, "launch.accel");
            d.LaunchSpeed = Opt(lb, "speed") is { } ls ? Num(ls, file, "launch.speed") : 0f;
            Range(d.LaunchSpeed, 0, 400, file, "launch.speed");
            d.LaunchReach = Opt(lb, "reach") is { } lr ? Num(lr, file, "launch.reach") : 0f;
            Range(d.LaunchReach, 0, 256, file, "launch.reach");
            d.LaunchMaxMass = Opt(lb, "maxMass") is { } lm ? Num(lm, file, "launch.maxMass") : float.MaxValue;
            if (d.LaunchMaxMass < 1)
                throw new ConstructPartException($"{file}: \"launch.maxMass\" is at least 1 kg");
        }
        else if (d.Logic is CLogic.Launch or CLogic.Arrest)
            throw new ConstructPartException($"{file}: a catapult or arrestor needs a \"launch\" block (width, height, accel)");
        if (Opt(root, "mooring") is { } mb)
        {
            if (d.Logic != CLogic.Mooring)
                throw new ConstructPartException($"{file}: only a mooring mast (logic kind mooring) has a \"mooring\" block");
            Known(mb, ["reach", "rope"], $"{file}, mooring");
            d.MastReach = Num(Need(mb, "reach", $"{file}, mooring"), file, "mooring.reach");
            Range(d.MastReach, 1, 64, file, "mooring.reach");
            d.MastRope = Opt(mb, "rope") is { } mr ? (byte)Num(mr, file, "mooring.rope") : (byte)2;
        }
        if (Opt(root, "wear") is { } wb)
            d.Wear = Str(wb, file, "wear") switch
            {
                "parachute" => PilotGear.Chute,
                "glider" => PilotGear.Glider,
                "jetpack" => PilotGear.Jet,
                var w => throw new ConstructPartException($"{file}: \"wear\" is parachute, glider or jetpack, not \"{w}\""),
            };
        if (Opt(root, "jump") is { } jb)
        {
            if (d.Logic != CLogic.JumpDrive)
                throw new ConstructPartException($"{file}: only a jump drive (logic kind jumpdrive) has a \"jump\" block");
            Known(jb, ["range", "charge", "energy"], $"{file}, jump");
            d.JumpRange = Num(Need(jb, "range", $"{file}, jump"), file, "jump.range");
            Range(d.JumpRange, 0.1f, 2000, file, "jump.range");
            d.JumpCharge = Num(Need(jb, "charge", $"{file}, jump"), file, "jump.charge");
            Range(d.JumpCharge, 0.5f, 600, file, "jump.charge");
            d.JumpEnergy = Num(Need(jb, "energy", $"{file}, jump"), file, "jump.energy");
            Range(d.JumpEnergy, 0, 1e6f, file, "jump.energy");
        }
        else if (d.Logic == CLogic.JumpDrive)
            throw new ConstructPartException($"{file}: a jump drive needs a \"jump\" block (range in light-years, charge seconds, energy kJ a tonne a light-year)");
        var belt = Opt(root, "conveyor");
        if (belt is { } bb)
        {
            if (d.Logic != CLogic.Conveyor)
                throw new ConstructPartException($"{file}: only a conveyor (logic kind conveyor) has a \"conveyor\" block");
            Known(bb, ["speed", "dir"], $"{file}, conveyor");
            d.BeltSpeed = Num(Need(bb, "speed", $"{file}, conveyor"), file, "conveyor.speed");
            Range(d.BeltSpeed, 0.05f, 30, file, "conveyor.speed");
            d.BeltDir = DirVector(DirOf(Str(Need(bb, "dir", $"{file}, conveyor"), file, "conveyor.dir"), file, "conveyor.dir"));
        }
        else if (d.Logic == CLogic.Conveyor)
            throw new ConstructPartException($"{file}: a conveyor needs a \"conveyor\" block (speed, dir)");
    }

    private static void ParseWinch(ConstructPartDef d, JsonElement? block, string file)
    {
        if (block is not { } w)
            return;
        if (d.Hitch != HitchKind.Hook)
            throw new ConstructPartException($"{file}: only a hook has a \"winch\"");
        Known(w, ["min", "speed", "rating", "grab"], $"{file}, winch");
        d.WinchMin = Num(Need(w, "min", $"{file}, winch"), file, "winch.min");
        Range(d.WinchMin, 0.3f, d.HitchChain, file, "winch.min");
        d.WinchSpeed = Num(Need(w, "speed", $"{file}, winch"), file, "winch.speed");
        Range(d.WinchSpeed, 0.05f, 20, file, "winch.speed");
        d.WinchRating = Num(Need(w, "rating", $"{file}, winch"), file, "winch.rating");
        Range(d.WinchRating, 1, 2e9f, file, "winch.rating");
        if (Opt(w, "grab") is { ValueKind: JsonValueKind.True })
        {
            d.IsCargoWinch = true;
            d.DataIn = ["reel", "grab", "pay out"];
        }
    }
}
