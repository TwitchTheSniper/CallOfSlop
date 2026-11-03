using System.Text.Json;

namespace CallOfSlop;

/// <summary>
/// The part-file blocks of the big systems (docs/sandbox/CONSTRUCTS.md, "Large systems"): <c>"dock"</c> on a cargo logic
/// part (a docking clamp or magnetic lock), <c>"launch"</c> on a catapult or arrestor, <c>"conveyor"</c> on a conveyor, and
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
            Known(dock, ["side", "settle", "maxMass", "charge", "refuel", "chute"], $"{file}, dock");
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
        }
        var launch = Opt(root, "launch");
        if (launch is { } lb)
        {
            if (d.Logic is not (CLogic.Launch or CLogic.Arrest))
                throw new ConstructPartException($"{file}: only a catapult or arrestor (logic kind launch, arrest) has a \"launch\" block");
            Known(lb, ["width", "height", "accel", "maxMass"], $"{file}, launch");
            d.LaunchWidth = Num(Need(lb, "width", $"{file}, launch"), file, "launch.width");
            Range(d.LaunchWidth, 0.5f, 128, file, "launch.width");
            d.LaunchHeight = Num(Need(lb, "height", $"{file}, launch"), file, "launch.height");
            Range(d.LaunchHeight, 0.5f, 64, file, "launch.height");
            d.LaunchAccel = Num(Need(lb, "accel", $"{file}, launch"), file, "launch.accel");
            Range(d.LaunchAccel, 0.5f, 100, file, "launch.accel");
            d.LaunchMaxMass = Opt(lb, "maxMass") is { } lm ? Num(lm, file, "launch.maxMass") : float.MaxValue;
            if (d.LaunchMaxMass < 1)
                throw new ConstructPartException($"{file}: \"launch.maxMass\" is at least 1 kg");
        }
        else if (d.Logic is CLogic.Launch or CLogic.Arrest)
            throw new ConstructPartException($"{file}: a catapult or arrestor needs a \"launch\" block (width, height, accel)");
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
        Known(w, ["min", "speed", "rating"], $"{file}, winch");
        d.WinchMin = Num(Need(w, "min", $"{file}, winch"), file, "winch.min");
        Range(d.WinchMin, 0.3f, d.HitchChain, file, "winch.min");
        d.WinchSpeed = Num(Need(w, "speed", $"{file}, winch"), file, "winch.speed");
        Range(d.WinchSpeed, 0.05f, 20, file, "winch.speed");
        d.WinchRating = Num(Need(w, "rating", $"{file}, winch"), file, "winch.rating");
        Range(d.WinchRating, 1, 2e9f, file, "winch.rating");
    }
}
