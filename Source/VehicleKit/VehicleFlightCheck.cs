using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop.VehicleKit;

/// <summary>
/// A generator that makes aircraft (docs/vehiclekit/AIRCRAFT.md). <see cref="VehicleRegistry.Make"/> sends it down the aircraft's own path (<see cref="AircraftKit.Make"/>):
/// no road chassis, no faction marks for ground vehicles, no drive check, and the flight check (<see cref="VehicleFlightCheck"/>) as its gate.
/// </summary>
public interface IAircraftShape;

/// <summary>The aircraft's path through the kit.</summary>
public static class AircraftKit
{
    /// <summary>
    /// A seed's aircraft: built by its generator and, unless its file is committed, flown by the flight check first (a seed that fails it throws
    /// <see cref="VehicleKitException"/>, and a spawn rerolls as it does for a ground vehicle). Roles, layers and the shape grammar don't apply yet.
    /// </summary>
    public static Vehicle Make(IVehicleGenerator g, int seed)
    {
        var v = new Vehicle(VehicleRegistry.BlueprintName(g, seed), g.Style, seed);
        g.Build(v);
        if (!g.Seeds.Contains(seed) || v.Context is { Matters: true })
            v.FlightGate();
        return v;
    }
}

/// <summary>
/// The flight check, the aircraft's drive check (<see cref="VehicleDriveCheck"/>): built from its own blueprint text in a world of its own, settled on a flat floor
/// and measured for level; then flown from its seat (a Drone Receiver counts) through its Flight Stabilizer, as a pilot's keys would: Sprint to climb, nothing
/// to hover (the stabilizer's hold), W to go forward. A craft that doesn't climb, drifts up or down in the hover, won't come level, or won't go on is refused.
/// Rotorcraft only for now: a plane's check (a take-off run and level flight) comes with the plane generator.
/// </summary>
public static class VehicleFlightCheck
{
    /// <summary>Ticks: settled before anything, then the climb, the hover (measured over its second half) and the push forward.</summary>
    public const int SettleTicks = 60, ClimbTicks = 120, HoverTicks = 120, ForwardTicks = 90;

    /// <summary>The least climb in <see cref="ClimbTicks"/> (m), the most height it may gain or lose over the hover's last second (m), the most tilt in the hover (degrees), the least way forward (m).</summary>
    public const float MinClimb = 1.5f, MaxHoverDrift = 1f, MaxHoverTilt = 8f, MinForward = 1f;

    /// <summary>The most tilt at rest (degrees), and at any moment of the flight.</summary>
    public const float MaxRestTilt = 8f, MaxFlightTilt = 35f;

    public readonly record struct Result(bool Built, float RestTilt, float Climb, float HoverDrift, float HoverTilt, float HoverSpeed, float Forward, float WorstTilt, float Lift, double Ms, string Why)
    {
        public bool Ok => Why.Length == 0;

        /// <summary>A plane's check (<see cref="RunPlane"/>): its stall speed, the speed and ground run when it rotated, its centre of mass ahead of its lift (m),
        /// the height and heading it wandered by in level flight on the autopilot, and how far it turned (degrees, right positive).</summary>
        public bool Plane { get; init; }
        public float Stall { get; init; }
        public float RunSpeed { get; init; }
        public float RunDist { get; init; }
        public float Balance { get; init; }
        public float LevelDrift { get; init; }
        public float HeadingDrift { get; init; }
        public float Turned { get; init; }

        /// <summary>One line of what it measured.</summary>
        public string Line => Plane
            ? $"flight (plane): stalls at {Stall:0} m/s, rotated at {RunSpeed:0} m/s after {RunDist:0} m, climbed {Climb:0.0} m in {PlaneClimbTicks / (float)World.TickRate:0.#} s, level drift {LevelDrift:0.0} m and {HeadingDrift:0.0} deg, " +
              $"turned {Turned:0} deg, worst tilt {WorstTilt:0}, centre of mass {Balance:0.00} m ahead of the lift, {Ms:0} ms"
            : $"flight: climbed {Climb:0.0} m in {ClimbTicks / (float)World.TickRate:0.#} s, hover drift {HoverDrift:0.00} m, hover tilt {HoverTilt:0.0} deg at {HoverSpeed:0.0} m/s, forward {Forward:0.0} m, worst tilt {WorstTilt:0}, lift {Lift:0.00}x its weight, {Ms:0} ms";
    }

    /// <summary>What the gate has done in this process, for the command line's last line.</summary>
    public static int Checked, Caught;

    /// <summary>
    /// The check on a blueprint's text, at <paramref name="gravity"/> times the world's standard gravity. Yields between steps and ticks as the drive check does; allocates
    /// (it runs in a generation, never on a tick).
    /// </summary>
    public static Result Run(string text, string file, float gravity = 1, bool gate = true)
    {
        var place = VehicleDriveCheck.Place(gravity);
        var start = Stopwatch.GetTimestamp();
        var bp = ConstructBuild.ParseBlueprint(text, file);
        VehicleGenJob.Yield();
        var w = new ConstructWorld();
        var job = new ConstructBuild.GroundJob(w, bp, null);
        while (!job.Step(0.4))
            VehicleGenJob.Yield();
        Construct? c;
        string why;
        using (place.In())
            c = job.Join(Vector3.Zero, 0, out why);
        var r = new Result(false, 0, 0, 0, 0, 0, 0, 0, 0, 0, "");
        if (c == null)
            return Done(start, r with { Why = $"it doesn't build: {why}" }, gate);
        r = r with { Built = true };
        var q = new SelfTest.DriveWorld();
        Fly(w, q, place, c, 0, SettleTicks, ref r);
        var rest = Tilt(c);
        r = r with { RestTilt = rest, WorstTilt = 0 };
        var plane = c.StabCount > 0 && ConstructFlight.KeysOf(c, c.Parts[c.Stabs[0].Part], c.Parts[c.Stabs[0].Part].D) == FlightPattern.Plane;
        var restMost = plane ? MaxPlaneRestTilt : MaxRestTilt;
        if (rest > restMost)
            return Done(start, r with { Why = $"it stands {rest:0.0} degrees off level (at most {restMost:0})" }, gate);
        var seat = c.StabCount > 0 ? ConstructFlight.SeatFor(c, c.Stabs[0].Part) : -1;
        if (c.StabCount == 0)
            return Done(start, r with { Why = "no Flight Stabilizer flies it" }, gate);
        if (seat < 0)
            return Done(start, r with { Why = "no seat or Drone Receiver gives the stabilizer its orders" }, gate);
        c.Driver = 1;
        c.DriverSeat = seat;
        if (plane)
            return Done(start, RunPlane(w, q, place, c, r), gate);
        var weight = c.Mass * World.Gravity * place.Ctx.GravityScale;
        var h0 = Up(c);
        Fly(w, q, place, c, Buttons.Sprint, ClimbTicks, ref r);
        r = r with { Lift = weight > 0 ? c.Stabs[0].UpForce / weight : 0 };
        var climb = Up(c) - h0;
        r = r with { Climb = climb };
        if (climb < MinClimb)
            return Done(start, r with { Why = $"Sprint for {ClimbTicks / (float)World.TickRate:0.#} s lifted it {climb:0.00} m (at least {MinClimb:0.#}; lift {r.Lift:0.00}x its weight; {Pushing(c)})" }, gate);
        Fly(w, q, place, c, 0, HoverTicks / 2, ref r);
        var mid = Up(c);
        Fly(w, q, place, c, 0, HoverTicks / 2, ref r);
        var drift = MathF.Abs(Up(c) - mid);
        var along = c.Vel - Vector3.UnitY * c.Vel.Y;
        r = r with { HoverDrift = drift, HoverTilt = Tilt(c), HoverSpeed = along.Length() };
        if (drift > MaxHoverDrift)
            return Done(start, r with { Why = $"let go, it went {(Up(c) > mid ? "up" : "down")} {drift:0.00} m in a second (at most {MaxHoverDrift:0.#})" }, gate);
        if (r.HoverTilt > MaxHoverTilt)
            return Done(start, r with { Why = $"let go, it hangs {r.HoverTilt:0.0} degrees off level (at most {MaxHoverTilt:0})" }, gate);
        var f0 = Vector3.Transform(-Vector3.UnitZ, Quaternion.Multiply(c.Rot, Rot24.Quat(c.Parts[c.Stabs[0].Part].Rot)));
        var fwd = Vector3.Normalize(new Vector3(f0.X, 0, f0.Z));
        var p0 = c.WorldCom;
        Fly(w, q, place, c, Buttons.Forward, ForwardTicks, ref r);
        var moved = Vector3.Dot(c.WorldCom - p0, fwd);
        r = r with { Forward = moved };
        if (moved < MinForward)
            return Done(start, r with { Why = $"W for {ForwardTicks / (float)World.TickRate:0.#} s took it {moved:0.00} m forward (at least {MinForward:0.#})" }, gate);
        if (r.WorstTilt > MaxFlightTilt)
            return Done(start, r with { Why = $"it tipped {r.WorstTilt:0} degrees in flight (at most {MaxFlightTilt:0})" }, gate);
        return Done(start, r, gate);
    }

    /// <summary>A plane's ticks: the most for the take-off run and the rotation, then the climb, the level flight (settling, then measured) and the turn.</summary>
    public const int PlaneRunTicks = 1200, RotateTicks = 180, PlaneClimbTicks = 240, LevelSettleTicks = 300, ThrottleBackTicks = 36, LevelTicks = 180, TurnTicks = 180;

    /// <summary>A plane's limits: tilt standing (a tail-dragger sits nose up), the height it must be off the ground to have rotated (m), the least climb, the most height and heading
    /// it may wander by in level flight (m, degrees), the least turn on D (degrees), the most tilt in its flight (it banks to turn), the most bank left in level flight.</summary>
    public const float MaxPlaneRestTilt = 16, LiftOff = 2, PlaneMinClimb = 5, MaxLevelDrift = 8, MaxHeadingDrift = 8, MinTurn = 10, MaxPlaneTilt = 75, MaxLevelBank = 12;

    /// <summary>
    /// A plane's flight from its seat, as a pilot (or a bot through the autopilot) would fly it: Shift held down a runway (the floor) until it is past its stall speed by
    /// a twentieth, S until it is off the ground, then the autopilot's altitude hold set 40 m up to climb, set to where it is to fly level (its height and heading are measured),
    /// and D held to bank and turn right.
    /// </summary>
    private static Result RunPlane(ConstructWorld w, SelfTest.DriveWorld q, VehicleDriveCheck.CheckPlace place, Construct c, Result r)
    {
        float stall;
        using (place.In())
        {
            stall = ConstructAero.StallSpeed(c);
            r = r with { Plane = true, Stall = stall, Balance = c.LiftCentre(out var lift, out _) ? Vector3.Dot(c.Com - lift, Forward(c, Quaternion.Identity)) : 0 };
        }
        var rotateAt = MathF.Max(12, 1.05f * stall);
        var h0 = Up(c);
        var p0 = c.WorldCom;
        var t = 0;
        for (; t < PlaneRunTicks && c.Vel.Length() < rotateAt && Up(c) - h0 < LiftOff; t += 15) // (a tail-dragger sitting nose up may lift off by itself)
        {
            Fly(w, q, place, c, Buttons.Sprint, 15, ref r);
            if (Environment.GetEnvironmentVariable("PLANE_TMP") == "1" && t % 120 == 0)
                Console.WriteLine($"[tmp] t {t} v {c.Vel} " + string.Join(" ", Enumerable.Range(0, c.RotorCount).Select(k => $"{c.Rotors[k].Thrust:0} feed {c.Rotors[k].Feed} om {c.Rotors[k].Omega:0} cmd {c.Rotors[k].Command:0.00} alive {c.Rotors[k].Alive} sc {c.Rotors[k].Scanned}")));
        }
        if (Environment.GetEnvironmentVariable("PLANE_TMP") == "1")
            for (var i = 0; i < c.PartCount; i++)
            {
                var wp = c.SegToWorld(c.Parts[i].Seg, c.Parts[i].Pos);
                if (wp.Y - h0 < -0.6f)
                    Console.WriteLine($"[tmp] {c.Parts[i].D.Name} y {wp.Y - h0:0.00} rest {c.Parts[i].Pos} speed {c.Vel.Length():0.0}");
            }
        var run = c.WorldCom - p0;
        r = r with { RunSpeed = c.Vel.Length(), RunDist = MathF.Sqrt(run.X * run.X + run.Z * run.Z) };
        if (r.RunSpeed < rotateAt && Up(c) - h0 < LiftOff)
            return r with { Why = $"down the runway for {t / (float)World.TickRate:0} s it reached {r.RunSpeed:0.0} m/s, {Vector3.Dot(c.Vel, Forward(c, c.Rot)):0.0} forward, nose {MathF.Asin(Math.Clamp(Forward(c, c.Rot).Y, -1, 1)) * 180 / MathF.PI:0} degrees up (it rotates at {rotateAt:0}, stalls at {stall:0}; {Pushing(c)})" };
        for (t = 0; t < RotateTicks && Up(c) - h0 < LiftOff; t += 10)
        {
            Fly(w, q, place, c, Buttons.Back | Buttons.Sprint, 10, ref r); // (the throttle held open as it rotates)
        }
        if (Up(c) - h0 < LiftOff)
            return r with { Why = $"S for {RotateTicks / (float)World.TickRate:0} s at {r.RunSpeed:0} m/s didn't lift it {LiftOff:0} m off the runway" };
        var hc = Up(c);
        Autopilot(place, c, hc + 40);
        Fly(w, q, place, c, 0, PlaneClimbTicks, ref r);
        var climb = Up(c) - hc;
        r = r with { Climb = climb };
        if (climb < PlaneMinClimb)
            return r with { Why = $"on the autopilot's climb it went {climb:0.0} m up in {PlaneClimbTicks / (float)World.TickRate:0} s (at least {PlaneMinClimb:0}) at {c.Vel.Length():0} m/s" };
        Autopilot(place, c, Up(c));
        Fly(w, q, place, c, Buttons.Crouch, ThrottleBackTicks, ref r); // (throttled back to cruise, as a pilot levels off: at full throttle it gathers speed and climbs through the hold)
        if (Environment.GetEnvironmentVariable("PLANE_TMP") == "1")
            for (var u = 0; u < 20; u++)
            {
                Fly(w, q, place, c, 0, 30, ref r);
                Console.WriteLine($"[tmp] lv {u} h {Up(c) - hc:0.0} target {c.Parts[c.Stabs[0].Part].D.Name} vy {c.Vel.Y:0.0} v {c.Vel.Length():0.0} nose {MathF.Asin(Math.Clamp(Forward(c, c.Rot).Y, -1, 1)) * 180 / MathF.PI:0.0} mem {c.Parts[c.Stabs[0].Part].Mem:0.00} err {c.StabErr}");
            }
        Fly(w, q, place, c, 0, LevelSettleTicks, ref r);
        var hl = Up(c);
        var head = Heading(c);
        var drift = 0f;
        for (t = 0; t < LevelTicks; t += 10)
        {
            Fly(w, q, place, c, 0, 10, ref r);
            drift = MathF.Max(drift, MathF.Abs(Up(c) - hl));
        }
        var wander = MathF.Abs(Wrap(Heading(c) - head));
        r = r with { LevelDrift = drift, HeadingDrift = wander, HoverTilt = Tilt(c), HoverSpeed = c.Vel.Length() };
        if (drift > MaxLevelDrift)
            return r with { Why = $"in level flight on the autopilot its height wandered {drift:0.0} m in {LevelTicks / (float)World.TickRate:0} s (at most {MaxLevelDrift:0}; climbing {c.Vel.Y:0.0} m/s at {c.Vel.Length():0} m/s)" };
        if (wander > MaxHeadingDrift)
            return r with { Why = $"in level flight its heading wandered {wander:0.0} degrees in {LevelTicks / (float)World.TickRate:0} s (at most {MaxHeadingDrift:0})" };
        if (r.HoverTilt > MaxLevelBank)
            return r with { Why = $"in level flight it hangs {r.HoverTilt:0} degrees off level (at most {MaxLevelBank:0})" };
        head = Heading(c);
        Fly(w, q, place, c, Buttons.Right, TurnTicks, ref r);
        var turned = Wrap(Heading(c) - head);
        r = r with { Turned = turned };
        if (turned < MinTurn)
            return r with { Why = $"D for {TurnTicks / (float)World.TickRate:0.#} s turned it {turned:0} degrees (at least {MinTurn:0} to the right)" };
        if (r.WorstTilt > MaxPlaneTilt)
            return r with { Why = $"it tipped {r.WorstTilt:0} degrees in flight (at most {MaxPlaneTilt:0})" };
        return r;
    }

    /// <summary>The autopilot's altitude hold on, at <paramref name="height"/>: a bot pilot's hook, set as its panel would set it.</summary>
    private static void Autopilot(VehicleDriveCheck.CheckPlace place, Construct c, float height)
    {
        using (place.In())
        {
            ref var p = ref c.Parts[c.Stabs[0].Part];
            ConstructConfig.Put(ref p, p.D, CfgKey.ApMode, (float)FlightMode.Altitude);
            ConstructConfig.Put(ref p, p.D, CfgKey.TargetAlt, ConstructPhysics.HoldShown(MathF.Max(1, height)));
            ConstructConfig.Put(ref p, p.D, CfgKey.Autopilot, 1);
        }
    }

    /// <summary>The stabilizer's forward, turned by <paramref name="rot"/> (the body's rotation, or none for the rest frame).</summary>
    private static Vector3 Forward(Construct c, Quaternion rot) => Vector3.Transform(-Vector3.UnitZ, Quaternion.Multiply(rot, Rot24.Quat(c.Parts[c.Stabs[0].Part].Rot)));

    /// <summary>Its heading over the floor, degrees clockwise seen from above.</summary>
    private static float Heading(Construct c)
    {
        var f = Forward(c, c.Rot);
        return MathF.Atan2(f.X, -f.Z) * 180 / MathF.PI;
    }

    private static float Wrap(float deg) => ((deg % 360) + 540) % 360 - 180;

    /// <summary>Ticks of the world with the seat's keys held (0: none; then the stabilizer's hover hold).</summary>
    private static void Fly(ConstructWorld w, SelfTest.DriveWorld q, VehicleDriveCheck.CheckPlace place, Construct c, Buttons keys, int ticks, ref Result r)
    {
        var worst = r.WorstTilt;
        for (var t = 0; t < ticks; t++)
        {
            VehicleGenJob.Yield();
            using (place.In())
            {
                if (c.DriverSeat >= 0 && c.DriverSeat < c.PartCount)
                {
                    c.Parts[c.DriverSeat].Keys = (ushort)((ushort)keys | CPart.Occupied);
                    ConstructEdits.DriveInput(c, keys, World.TickDt);
                }
                w.Step(q, World.TickDt);
            }
            if (c.DriverSeat >= 0)
                worst = MathF.Max(worst, Tilt(c));
        }
        r = r with { WorstTilt = worst };
    }

    private static Result Done(long start, Result r, bool gate)
    {
        var ms = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
        if (gate)
            lock (Lock)
            {
                Checked++;
                if (!r.Ok)
                    Caught++;
            }
        return r with { Ms = ms };
    }

    private static readonly object Lock = new();

    /// <summary>What its lifters do now, for a refusal: rotors' and thrusters' push against their weight, and the charge.</summary>
    private static string Pushing(Construct c)
    {
        var rotors = 0f;
        for (var k = 0; k < c.RotorCount; k++)
            rotors += c.Rotors[k].Thrust;
        var level = 0f;
        for (var k = 0; k < c.ThrusterCount; k++)
            level += c.Thrusters[k].Level;
        return $"{c.RotorCount} rotors pushing {rotors:0} N, {c.ThrusterCount} thrusters at {(c.ThrusterCount > 0 ? level / c.ThrusterCount : 0):P0}, weight {c.Mass * World.Gravity:0} N at standard gravity, charge {c.Charge:0} of {c.ChargeCapacity:0} kJ{(c.Electrical ? "" : ", no circuit")}";
    }

    private static float Up(Construct c) => c.WorldCom.Y;

    private static float Tilt(Construct c) => MathF.Acos(Math.Clamp(Vector3.Transform(Vector3.UnitY, c.Rot).Y, -1, 1)) * 180 / MathF.PI;

    /// <summary>The full check's part for an aircraft (<see cref="VehicleCheckOptions.Flies"/>): the flight check's numbers as a note, its refusal as a failure.</summary>
    public static void Into(string text, string file, VehicleReport report)
    {
        var r = Run(text, file, gate: false);
        report.Notes.Add(r.Line);
        if (!r.Ok)
            report.Failures.Add(r.Why);
    }
}

public sealed partial class Vehicle
{
    /// <summary>The flight check as a gate (<see cref="VehicleFlightCheck"/>), as <see cref="DriveGate"/> is for a ground vehicle: a seed that doesn't fly is made again.</summary>
    public void FlightGate()
    {
        if (!VehicleDriveCheck.GateOn)
            return;
        VehicleGenJob.Yield();
        var r = VehicleFlightCheck.Run(Text(), Name + ".json", Context?.Gravity ?? 1);
        FlightCheck = r;
        if (!r.Ok)
        {
            Console.WriteLine($"[vehicles] {Name}: the flight check refused it ({r.Why}); planning again. It was: {Description}");
            throw new VehicleKitException("flight check: " + r.Why);
        }
    }

    /// <summary>What the flight check measured on the plan that was kept (null: not checked).</summary>
    public VehicleFlightCheck.Result? FlightCheck { get; private set; }
}
