using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop.VehicleKit;

/// <summary>
/// The spawn-time drive check (docs/vehiclekit/VEHICLEKIT.md, "The drive check"): a generated vehicle is built from its own blueprint text in a world of its own,
/// settled on a flat floor, measured for level, and given a short throttle pulse through its seat; one that stands crooked or doesn't move is refused, and the
/// plan is made again as the identity gate does (<see cref="Vehicle.DriveGate"/>). Spawn generation used to check only that a vehicle builds, so Bandit convoy 3's
/// leader (a Box Truck on Heavy Pogo Struts in front and Heavy Suspension behind) spawned standing 10.5 degrees nose up on its tail rails, unable to move.
/// <para>
/// It runs inside a generation (<see cref="VehicleGenJob"/>), so every step yields: the build goes a step at a time (<see cref="ConstructBuild.GroundJob"/>), the
/// physics a tick at a time. The physics runs inside a world context of its own (flat, plain gravity at the spawn context's strength, no gravity parts), entered
/// round each span and left before each yield, so the game's world statics (gravity, its gravity parts, the ride bodies) are never touched and the
/// same seed checks the same in the game as on the command line.
/// </para>
/// </summary>
public static class VehicleDriveCheck
{
    /// <summary>On unless <c>--no-drive-gate</c>: to measure what the check catches and costs.</summary>
    public static bool GateOn = true;

    /// <summary>Ticks it settles for before it is measured, and ticks of full throttle.</summary>
    public const int SettleTicks = 60, PulseTicks = 75;

    /// <summary>The most it may stand off level (degrees), as the full check's (<see cref="VehicleChecks"/>: 8).</summary>
    public const float MaxTilt = 8;

    /// <summary>How far forward the pulse must take its centre of mass (m): a working Box Truck goes 2 to 3 m in its 1.25 s.</summary>
    public const float MinMove = 1f;

    /// <summary>What the gate has done in this process: vehicles checked, refused, and the check's own time (ms), for the sweeps and the log.</summary>
    public static int Checked, Caught;

    public static double TotalMs, WorstMs;

    /// <summary>What one check found.</summary>
    public readonly record struct Result(bool Built, float Tilt, float Moved, float Speed, double Ms, string Why)
    {
        public bool Ok => Why.Length == 0;
    }

    /// <summary>
    /// The check on a blueprint's text: built, settled, measured and pulsed, at <paramref name="gravity"/> times the world's standard gravity. Yields between steps
    /// and ticks; allocates (it runs in a generation, never on a tick).
    /// </summary>
    public static Result Run(string text, string file, float gravity = 1)
    {
        var place = Place(gravity); // (first: on the command line it loads the maps once, which isn't the check's cost)
        var start = Stopwatch.GetTimestamp();
        var bp = ConstructBuild.ParseBlueprint(text, file);
        VehicleGenJob.Yield();
        var w = new ConstructWorld();
        var job = new ConstructBuild.GroundJob(w, bp, null);
        while (!job.Step(0.4))
            VehicleGenJob.Yield();
        VehicleGenJob.Yield();
        Construct? c;
        string why;
        using (place.In())
            c = job.Join(Vector3.Zero, 0, out why);
        if (c == null)
            return Done(start, new Result(false, 0, 0, 0, 0, $"it doesn't build: {why}"));
        var q = new SelfTest.DriveWorld();
        for (var t = 0; t < SettleTicks; t++)
        {
            VehicleGenJob.Yield();
            using (place.In())
                w.Step(q, World.TickDt);
        }
        var tilt = Tilt(c);
        if (tilt > MaxTilt)
            return Done(start, new Result(true, tilt, 0, 0, 0, $"it stands {tilt:0.0} degrees off level (at most {MaxTilt:0})"));
        if (c.DriveSeat < 0 || !c.CanDrive)
            return Done(start, new Result(true, tilt, 0, 0, 0, $"it can't drive: {c.DriveProblem}"));
        c.Driver = 1;
        c.DriverSeat = c.DriveSeat;
        using (place.In())
            ConstructPipes.Tick(w, 0); // (as the server does once a second: the fuel reaches the engine)
        var p0 = c.WorldCom;
        var f0 = Vector3.Transform(c.SeatForward(), c.Rot);
        var fwd = Vector3.Normalize(new Vector3(f0.X, 0, f0.Z));
        var top = 0f;
        for (var t = 0; t < PulseTicks; t++)
        {
            VehicleGenJob.Yield();
            using (place.In())
            {
                c.Parts[c.DriveSeat].Keys = (ushort)((ushort)Buttons.Forward | CPart.Occupied);
                ConstructEdits.DriveInput(c, Buttons.Forward, World.TickDt);
                w.Step(q, World.TickDt);
            }
            top = MathF.Max(top, c.Vel.Length());
        }
        var moved = Vector3.Dot(c.WorldCom - p0, fwd);
        var after = Tilt(c);
        var r = new Result(true, MathF.Max(tilt, after), moved, top, 0, "");
        if (moved < MinMove)
            r = r with { Why = $"a {PulseTicks / (float)World.TickRate:0.##} s throttle pulse moved it {moved:0.00} m (at least {MinMove:0.#}; rpm {c.Rpm:0}{(c.DriveProblem.Length > 0 ? ", " + c.DriveProblem : "")})" };
        else if (after > 25)
            r = r with { Why = $"it tipped {after:0} degrees on the throttle pulse" };
        return Done(start, r);
    }

    private static Result Done(long start, Result r)
    {
        var ms = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
        lock (Lock)
        {
            Checked++;
            if (!r.Ok)
                Caught++;
            TotalMs += ms;
            WorstMs = Math.Max(WorstMs, ms);
        }
        return r with { Ms = ms };
    }

    private static readonly object Lock = new();

    private static float Tilt(Construct c) => MathF.Acos(Math.Clamp(Vector3.Transform(Vector3.UnitY, c.Rot).Y, -1, 1)) * 180 / MathF.PI;

    // ---- The check's own world ------------------------------------------------------------------------------------------

    /// <summary>A flat world with plain straight-down gravity at a strength: what the check's physics reads while it runs.</summary>
    private sealed class CheckPlace(WorldContext ctx)
    {
        public readonly WorldContext Ctx = ctx;

        /// <summary>Enters it for one span: the world that was entered comes back when the span ends (before any yield).</summary>
        public Scope In() => new(Ctx);
    }

    private readonly struct Scope : IDisposable
    {
        private readonly WorldContext _back;

        public Scope(WorldContext into)
        {
            _back = WorldContext.Current;
            World.Enter(into);
        }

        public void Dispose() => World.Enter(_back);
    }

    private static readonly Dictionary<float, CheckPlace> Places = new();

    private static CheckPlace Place(float gravity)
    {
        var g = MathF.Round(Math.Clamp(gravity, 0.05f, 4f), 2);
        lock (Places)
        {
            if (Places.TryGetValue(g, out var p))
                return p;
            var map = Maps.All.FirstOrDefault(m => m.Planetoid == null && m.Gravity == null) ?? Maps.All[0];
            var ctx = new WorldContext
            {
                Name = "vehicle drive check",
                Map = map,
                Physics = null,
                Mover = World.Mover,
                GravityScale = g,
                GravitySetting = (int)MathF.Round(g * 100),
                Gravity = new GravityFrames.Saved(),
            };
            return Places[g] = new CheckPlace(ctx);
        }
    }

    // ---- The command line ----------------------------------------------------------------------------------------------

    /// <summary>A tool's exit code, after a line of what the drive check did in the run (when it ran at all).</summary>
    public static int Report(int code)
    {
        if (Checked > 0)
            Console.WriteLine($"drive check: {Checked} plans checked, {Caught} refused ({(double)Caught / Checked:P1}), {TotalMs / Checked:0} ms mean, {WorstMs:0} ms worst{(GateOn ? "" : " (gate off)")}");
        return code;
    }

    /// <summary><c>--drive-check FILE [--gravity G]</c>: the spawn's drive check on any blueprint, with what it measured.</summary>
    public static int Cli(string file, string[] args)
    {
        var g = float.TryParse(args.SkipWhile(a => !a.Equals("--gravity", StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var gv) ? gv : 1f;
        var r = Run(File.ReadAllText(file), Path.GetFileName(file), g);
        Console.WriteLine($"{Path.GetFileName(file)}: {(r.Ok ? "passes" : "FAILS: " + r.Why)}; tilt {r.Tilt:0.0} deg, pulse {r.Moved:0.00} m, top {r.Speed:0.0} m/s, {r.Ms:0} ms at gravity x{g:0.##}");
        return r.Ok ? 0 : 1;
    }
}

public sealed partial class Vehicle
{
    /// <summary>
    /// The drive check as a gate (<see cref="VehicleDriveCheck"/>): the vehicle as its text builds, settled, level and moving on a throttle pulse, else a
    /// <see cref="VehicleKitException"/> and the plan is made again (the shape grammar and kit 4 take the next plan; a kit-1 seed made for a spawn rerolls).
    /// Only for what no file holds: a committed seed is never gated. The spawn context's gravity is the check's.
    /// </summary>
    public void DriveGate()
    {
        if (!VehicleDriveCheck.GateOn)
            return;
        VehicleGenJob.Yield();
        var text = Text();
        var r = VehicleDriveCheck.Run(text, Name + ".json", Context?.Gravity ?? 1);
        DriveCheck = r;
        if (!r.Ok)
        {
            Console.WriteLine($"[vehicles] {Name}: the drive check refused it ({r.Why}); planning again");
            throw new VehicleKitException("drive check: " + r.Why);
        }
    }

    /// <summary>What the drive check measured on the plan that was kept (null: not checked).</summary>
    public VehicleDriveCheck.Result? DriveCheck { get; private set; }
}
