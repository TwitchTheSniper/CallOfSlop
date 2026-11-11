using System.Numerics;

namespace CallOfSlop.EngineKit;

/// <summary>
/// <c>--speed-test FILE... [--seconds N]</c> (docs/vehiclekit/ENGINEKIT.md, "Why cars are slow"): a blueprint built on the drive check's flat floor and driven on
/// full throttle for N seconds (default 40): its mass, engine, gearing, wheel and what tops it out, the top speed and the times to 10, 20 m/s and 95 % of its top.
/// What limits it: <b>gearing</b> when its top gear reaches the limiter, <b>grip</b> when the engine asks more of the driven tyres at the top speed than they
/// hold (their friction limits; a two-wheel drive on Off-Road Wheels tops out near 27 m/s whatever the engine), else <b>power against drag</b> (the arcade air drag, <see cref="ConstructPhysics.AirDrag"/>,
/// takes the same share of speed squared from every body under <see cref="ConstructPhysics.DragMass"/>, so power a kilogram is what sets a top speed).
/// </summary>
public static class EngineKitSpeed
{
    /// <summary>What one run measured.</summary>
    public readonly record struct Run(string Name, float Mass, string Engine, float PeakKw, float Torque, float MaxRpm, string Gears, float Final, float Radius,
        float Top, float T10, float T20, float T95, int TopGear, int Gear, float Rpm, float GearTop, float DragKw, string Limit, string Problem);

    public static int Cli(string[] args)
    {
        var seconds = 40f;
        var files = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i].Equals("--seconds", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                seconds = float.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture);
            else if (!args[i].StartsWith("--", StringComparison.Ordinal) && (args[i].EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
                files.Add(args[i]);
        }
        Console.WriteLine("name | mass kg | engine | peak kW | N m | gears x final | wheel r m | top m/s | 0-10 s | 0-20 s | 0-95% s | gear at top | rpm at top | gearing top m/s | drag kW at top | limit");
        foreach (var f in files)
        {
            var r = Measure(File.ReadAllText(f), Path.GetFileNameWithoutExtension(f), seconds);
            Console.WriteLine(Line(r));
        }
        return 0;
    }

    public static string Line(Run r) =>
        $"{r.Name} | {r.Mass:0} | {r.Engine} | {r.PeakKw:0} | {r.Torque:0} | {r.Gears} x {r.Final:0.##} | {r.Radius:0.00} | {r.Top:0.0} | {T(r.T10)} | {T(r.T20)} | {T(r.T95)} | {r.Gear + 1}/{r.TopGear} | {r.Rpm:0} | {r.GearTop:0.0} | {r.DragKw:0} | {r.Limit}{(r.Problem.Length > 0 ? " (" + r.Problem + ")" : "")}";

    private static string T(float t) => t < 0 ? "-" : t.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Builds <paramref name="text"/> on a flat floor, settles it, and drives it on full throttle for <paramref name="seconds"/>. Allocates: a tool, never on a tick.</summary>
    public static Run Measure(string text, string name, float seconds)
    {
        var bp = ConstructBuild.ParseBlueprint(text, name);
        var q = new SelfTest.DriveWorld();
        var w = new ConstructWorld();
        var c = ConstructBuild.BuildOnGround(w, bp, Vector3.Zero, 0, out var why, VehicleKit.VehicleChecks.TowOf(bp));
        if (c == null)
            return new Run(name, 0, "-", 0, 0, 0, "-", 0, 0, 0, -1, -1, -1, 0, 0, 0, 0, 0, "doesn't build", why);
        for (var t = 0; t < 30; t++)
            w.Step(q, World.TickDt);
        c.Driver = 1;
        c.DriverSeat = c.DriveSeat;
        var ticks = (int)(seconds * World.TickRate);
        var speeds = new float[ticks];
        float top = 0;
        for (var t = 0; t < ticks; t++)
        {
            if (c.DriveSeat >= 0)
                c.Parts[c.DriveSeat].Keys = (ushort)((ushort)Buttons.Forward | CPart.Occupied);
            ConstructEdits.DriveInput(c, Buttons.Forward, World.TickDt);
            w.Step(q, World.TickDt);
            speeds[t] = new Vector2(c.Vel.X, c.Vel.Z).Length();
            top = MathF.Max(top, speeds[t]);
            if (t % World.TickRate == World.TickRate - 1)
                ConstructPipes.Tick(w, (uint)t);
        }
        float At(float v)
        {
            for (var t = 0; t < ticks; t++)
                if (speeds[t] >= v)
                    return t * World.TickDt;
            return -1;
        }
        var e = c.Engine >= 0 ? c.EngineDef : null;
        var tr = c.Transmission >= 0 ? c.DefOf(c.Transmission) : null;
        float radius = 0, final = 0;
        for (var k = 0; k < c.WheelCount; k++)
            if (c.Wheels[k].Driven)
            {
                radius = c.Wheels[k].Radius;
                final = c.Wheels[k].Ratio;
                break;
            }
        var peakKw = e == null ? 0 : e.EngineTorque * e.PeakRpm * MathF.Tau / 60f / 1000f;
        var gears = tr == null ? "-" : string.Join("/", tr.Settings.Select(g => g.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)));
        var topGear = tr?.Settings.Length ?? 0;
        var lastRatio = tr is { Settings.Length: > 0 } ? tr.Settings[^1] : 1f;
        var gearTop = e == null || final <= 0 ? 0 : e.MaxRpm / ConstructDrive.RpmPerRadS / (lastRatio * final) * radius;
        var dragKw = ConstructPhysics.AirDragOf(c) * top * top * c.Mass * top / 1000f;
        float grip = 0; // (what the driven tyres can push with, N: each one's friction limit this tick, an open differential's least-gripping wheel for all of its group)
        for (var k = 0; k < c.WheelCount; k++)
            if (c.Wheels[k].Driven && c.Wheels[k].Touching)
                grip += c.Wheels[k].LastGrip / World.TickDt;
        var push = radius > 0 ? c.DriveTorque * c.GearRatio * final / radius : 0; // (what the engine asks of the tyres at the top speed, N)
        var limit = e == null ? "no engine"
            : c.Gear >= topGear - 1 && c.Rpm >= e.MaxRpm * 0.97f ? "gearing (limiter in top gear)"
            : top < 1 ? "doesn't move"
            : grip > 0 && push > grip ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"grip (the engine asks {push / 1000:0.0} kN of driven tyres that hold {grip / 1000:0.0} kN)")
            : "power against drag";
        return new Run(name, c.Mass, e?.Name ?? "-", peakKw, e?.EngineTorque ?? 0, e?.MaxRpm ?? 0, gears, final, radius, top, At(10), At(20), At(top * 0.95f),
            topGear, c.Gear, c.Rpm, gearTop, dragKw, limit, c.DriveProblem);
    }
}
