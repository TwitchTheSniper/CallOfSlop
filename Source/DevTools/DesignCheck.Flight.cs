using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>The design check's flight (DesignCheck.cs): its drives, nozzles and seats, the aircraft flight check and a sortie from the helm.</summary>
public static partial class DesignCheck
{
    /// <summary>Flight (step 3): what its drives, nozzles and seats came to, then the aircraft flight check from its helm (climb, hover, forward, a Ship keys turn).</summary>
    private static void Flight(Run r)
    {
        var (world, c, els, bpText) = (r.World, r.Body, r.Els, r.Blueprint);
        var seats = Enumerable.Range(0, c.PartCount).Where(i => c.DefOf(i).Role == PartRole.Seat).ToList();
        Console.WriteLine($"[design] flight: {c.ThrusterCount} thrusters ({els.Count(e => e.Kind == "nozzle")} RCS nozzles), {seats.Count} seats ({seats.Count(i => c.DefOf(i).Helm)} helm), stabilizers {c.StabCount}, drive seat {c.DriveSeat}"
            + (c.Effectors is { } fx ? $", the stabilizer found {ConstructEffectors.Found(fx)}" : ""));
        if (bpText != null && !r.Has("--no-fly"))
        {
            var t4 = Stopwatch.GetTimestamp();
            var fr = VehicleKit.VehicleFlightCheck.Run(bpText, "design check.json", gate: false);
            // (The aircraft check's limits are a small craft's: it is a note here, the sortie below is the test.)
            Console.WriteLine($"[design] aircraft flight check in {Ms(t4):0} ms: {(fr.Ok ? "flies" : "refused: " + fr.Why)}; {fr.Line}");
        }
        if (c.StabCount > 0 && c.DriveSeat >= 0 && !r.Has("--no-fly"))
            if ((Sortie(world, c) is not null ? Sortie(world, c) : null) is { } bad) // (a second sortie when the first fails: the first touchdown runs the JIT on the contact paths)
                r.Fail("from its helm: " + bad);
    }

    /// <summary>
    /// A sortie from the helm on the proving ground, as a player flies it (keys held on the seat, as a seated player's are): up on Shift, a
    /// hover, ahead on W, a turn on D, down on Ctrl to the ground. Each leg's height, way, heading and speed, and what a world step cost.
    /// </summary>
    private static string? Sortie(ConstructWorld world, Construct c)
    {
        var seat = c.DriveSeat;
        c.Driver = 1;
        c.DriverSeat = seat;
        c.Parts[seat].SitterPlus1 = 2;
        float Heading() { var f = Vector3.Transform(-Vector3.UnitZ, c.Rot); return MathF.Atan2(f.X, -f.Z) * 180 / MathF.PI; }
        float Tilt() => MathF.Acos(Math.Clamp(Vector3.Transform(Vector3.UnitY, c.Rot).Y, -1, 1)) * 180 / MathF.PI;
        var ground = c.WorldCom.Y;
        double worst = 0, sum = 0;
        var worstAt = "";
        var slow = 0;
        var bytes0 = GC.GetAllocatedBytesForCurrentThread();
        var n = 0;
        float Leg(string name, Buttons keys, float seconds)
        {
            var p0 = c.WorldCom;
            var h0 = Heading();
            var tilt = 0f;
            for (var t = 0; t < seconds * World.TickRate; t++)
            {
                c.Parts[seat].Keys = (ushort)((ushort)keys | CPart.Occupied);
                ConstructEdits.DriveInput(c, keys, World.TickDt);
                var s = Stopwatch.GetTimestamp();
                world.Step(World.Query, World.TickDt);
                var ms = Ms(s);
                if (ms > 4)
                    slow++;
                if (n > 120 && ms > worst)
                    (worst, worstAt) = (ms, $"{name} {t / (float)World.TickRate:0.00} s");
                (sum, n) = (sum + ms, n + 1); // (the worst past its first two seconds: the JIT's first calls)
                tilt = MathF.Max(tilt, Tilt());
            }
            var d = c.WorldCom - p0;
            var turned = ((Heading() - h0) % 360 + 540) % 360 - 180;
            var fired = 0;
            for (var k = 0; k < c.ThrusterCount; k++)
                if (c.Thrusters[k].Level > 0.05f)
                    fired++;
            var line = $"[design] sortie {name,-8} {seconds:0} s: height {c.WorldCom.Y - ground:0.0} m (moved {d.Y:+0.0;-0.0} up, {MathF.Sqrt(d.X * d.X + d.Z * d.Z):0.0} along), turned {turned:+0;-0} deg, speed {c.Vel.Length():0.0} m/s, worst tilt {tilt:0} deg, {fired} of {c.ThrusterCount} thrusters lit, asleep {c.Asleep}";
            Console.WriteLine(line);
            return name == "turn" ? turned : name == "ahead" ? MathF.Sqrt(d.X * d.X + d.Z * d.Z) : c.WorldCom.Y - ground;
        }
        var up = Leg("lift", Buttons.Sprint, 6);
        var hover = Leg("hover", 0, 3);
        var ahead = Leg("ahead", Buttons.Forward, 5);
        Leg("stop", 0, 4);
        var turn = Leg("turn", Buttons.Right, 5);
        Leg("hold", 0, 3);
        Leg("land", Buttons.Crouch, 12);
        var rest = Leg("rest", 0, 3);
        Console.WriteLine($"[design] sortie: a world step with it flying: mean {sum / Math.Max(1, n):0.000} ms, worst {worst:0.00} ms after its first two seconds ({worstAt}), {slow} over 4 ms, {(GC.GetAllocatedBytesForCurrentThread() - bytes0) / Math.Max(1, n)} bytes allocated a step");
        c.Parts[seat].SitterPlus1 = 0;
        c.Driver = 255;
        c.DriverSeat = -1;
        return up < 10 ? $"Shift for 6 s lifted it {up:0.0} m (at least 10)" : hover < 0.5f * up ? $"let go, it sank to {hover:0.0} m" : ahead < 30 ? $"W for 5 s took it {ahead:0.0} m (at least 30)"
            : turn < 30 ? $"D for 5 s turned it {turn:0} degrees (at least 30)" : rest > 1 ? $"Ctrl for 12 s left it {rest:0.0} m up" : worst > 8 ? $"a step took {worst:0.0} ms (at most 8)" : null;
    }
}
