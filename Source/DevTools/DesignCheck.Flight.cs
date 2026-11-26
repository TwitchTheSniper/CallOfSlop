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
        Seats(c);
        if (c.StabCount > 0 && c.DriveSeat >= 0 && !r.Has("--no-fly"))
        {
            if ((Sortie(world, c) is not null ? Sortie(world, c) : null) is { } bad) // (a second sortie when the first fails: the first touchdown runs the JIT on the contact paths)
                r.Fail("from its helm: " + bad);
            if (r.Has("--cones") && Cones(world, c) is { } stuck)
                r.Fail("on the cones: " + stuck);
        }
    }

    /// <summary>
    /// Which way each seat faces in the ship (its yaw from the ship's -z, positive to the right seen from above), as its sitter is posed
    /// (ConstructRender.SeatPose, which turns by the def's SeatTurn past its Rot24), with the Rot24 part of it: a designer's free turn shows here.
    /// </summary>
    private static void Seats(Construct c)
    {
        static float Yaw(Vector3 f) => MathF.Atan2(f.X, -f.Z) * 180 / MathF.PI;
        for (var i = 0; i < c.PartCount; i++)
        {
            var d = c.DefOf(i);
            if (d.Role != PartRole.Seat)
                continue;
            ConstructRender.SeatPose(c, i, 1, false, out var q, out _);
            var inShip = Quaternion.Conjugate(c.Rot) * q;
            Console.WriteLine($"[design] seat {i}{(d.Helm ? " (helm)" : "")}: faces {Yaw(Vector3.Transform(-Vector3.UnitZ, inShip)):+0.0;-0.0} deg in the ship (its Rot24 alone {Yaw(Rot24.Apply(c.Parts[i].Rot, -Vector3.UnitZ)):+0;-0}), up {Vector3.Transform(Vector3.UnitY, inShip).Y:0.00}"
                + (i == c.DriveSeat ? $", the ship's front {Yaw(c.SeatForward()):+0.0;-0.0} deg" : ""));
        }
    }

    /// <summary>
    /// The cones (<c>--cones</c>, the lift-off snag): the ship set down from its helm onto the proving ground's slalom cones, then Shift, each half
    /// second logged with its climb, height, the stabilizer's Landed time and armed bit, the thrusters' levels, its broken parts and the world
    /// contacts (their count, how many stand above the ground, so on a cone, and the mean normal of those). Fails if Shift for 6 s lifts it under 5 m.
    /// Found 2026-11-26: in this headless world the map's props are not solid shapes (the 12 cones' boxes gather 1 shape between them, the ground's), so
    /// the Zero Day rests on its keel on the ground with cones under it and lifts off at once; the snag needs a run with the game's props (a window run).
    /// </summary>
    private static string? Cones(ConstructWorld world, Construct c)
    {
        // Over the wide slalom (ProvingGround.Slalom: cones 9 m apart along x at z -122 +- 2.6), its middle between two of them.
        var at = new Vector3(-64.5f, 0, -122); // (two of them under its keel, the Zero Day's: a ship lands on its belly)
        var under = ConstructWorld.Ground(World.Query, at.X, at.Z).Y;
        c.Pos = c.PrevPos = c.Pos + (at with { Y = under + 12 } - c.WorldCom);
        (c.Vel, c.AngVel) = (Vector3.Zero, Vector3.Zero);
        c.Wake();
        var seat = c.DriveSeat;
        c.Driver = 1;
        c.DriverSeat = seat;
        c.Parts[seat].SitterPlus1 = 2;
        // (the world's contacts of the last step, for this diagnosis only: ConstructPhysics keeps them private)
        var contacts = typeof(ConstructPhysics).GetField("_contacts", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)?.GetValue(null) as ConstructPhysics.Contact[];
        var lifted = 0f;
        void Leg(string name, Buttons keys, float seconds)
        {
            var low = c.WorldCom.Y - under;
            for (var t = 0; t < seconds * World.TickRate; t++)
            {
                c.Parts[seat].Keys = (ushort)((ushort)keys | CPart.Occupied);
                ConstructEdits.DriveInput(c, keys, World.TickDt);
                world.Step(World.Query, World.TickDt);
                low = MathF.Min(low, c.WorldCom.Y - under);
                if ((t + 1) % (World.TickRate / 2) != 0)
                    continue;
                ref var st = ref c.Stabs[0];
                var armed = (c.Parts[st.Part].State & 1) != 0;
                float most = 0, sum = 0;
                for (var k = 0; k < c.ThrusterCount; k++)
                    (most, sum) = (MathF.Max(most, c.Thrusters[k].Level), sum + c.Thrusters[k].Level);
                var broken = 0;
                for (var i = 0; i < c.PartCount; i++)
                    if (c.Parts[i].Broken || c.Parts[i].Health <= 0)
                        broken++;
                int touching = 0, high = 0;
                var normal = Vector3.Zero;
                var deepest = 0f;
                for (var k = 0; contacts != null && k < ConstructPhysics.LastContacts; k++)
                {
                    ref var ct = ref contacts[k];
                    if (ct.A != -1 && ct.B != -1)
                        continue;
                    touching++;
                    deepest = MathF.Min(deepest, ct.Gap);
                    if (ct.Point.Y > ConstructWorld.Ground(World.Query, ct.Point.X, ct.Point.Z).Y + 0.05f)
                    {
                        high++;
                        normal += ct.B == -1 ? ct.Normal : -ct.Normal; // (from the world into the ship)
                    }
                }
                Console.WriteLine($"[design] cones {name,-5} {(t + 1) / (float)World.TickRate:0.0} s: height {c.WorldCom.Y - under:0.00} m, climb {c.Vel.Y:+0.00;-0.00} m/s, landed {st.Landed:0.00} s, armed {armed}, thrusters max {most:0.00} mean {sum / Math.Max(1, c.ThrusterCount):0.00}, "
                    + $"broken {broken}, world contacts {touching} ({high} above the ground{(high > 0 ? $", mean normal {Vector3.Normalize(normal):0.00}" : "")}, deepest {deepest:0.000} m), tilt {MathF.Acos(Math.Clamp(Vector3.Transform(Vector3.UnitY, c.Rot).Y, -1, 1)) * 180 / MathF.PI:0} deg, asleep {c.Asleep}");
            }
            if (name == "lift")
                lifted = c.WorldCom.Y - under - low;
        }
        int ConeShapes()
        {
            var found = new List<ContactShape>();
            for (var i = 0; i < 12; i++)
            {
                var cone = new Vector3(-110 + i * 9, under, -122 + (i % 2 == 0 ? 2.6f : -2.6f));
                World.Query.Gather(cone - new Vector3(0.4f, -0.1f, 0.4f), cone + new Vector3(0.4f, 0.6f, 0.4f), new QueryFilter(QueryLayers.World & ~QueryLayers.Trunks & ~QueryLayers.Terrain), found);
            }
            return found.Count;
        }
        Console.WriteLine($"[design] cones: {ConeShapes()} solid shapes at the 12 wide-slalom cones before it comes down");
        Leg("drop", Buttons.Crouch, 10);
        Console.WriteLine($"[design] cones: {ConeShapes()} after");
        var sat = c.WorldCom.Y - under;
        // Where it rests (the ship's frame), and the cones under its footprint (ProvingGround.Slalom's), so a miss is seen as one.
        var inv = Quaternion.Conjugate(c.Rot);
        var rests = new List<string>();
        for (var k = 0; contacts != null && k < ConstructPhysics.LastContacts && rests.Count < 12; k++)
            if (contacts[k].A == -1 || contacts[k].B == -1)
                rests.Add($"{Vector3.Transform(contacts[k].Point - c.Pos, inv):0.0}");
        var cones = new List<string>();
        var shapes = new List<ContactShape>();
        for (var i = 0; i < 12; i++)
        {
            var cone = new Vector3(-110 + i * 9, under, -122 + (i % 2 == 0 ? 2.6f : -2.6f));
            var l = Vector3.Transform(cone - c.Pos, inv);
            shapes.Clear();
            World.Query.Gather(cone - new Vector3(0.4f, -0.1f, 0.4f), cone + new Vector3(0.4f, 0.6f, 0.4f), new QueryFilter(QueryLayers.World & ~QueryLayers.Trunks & ~QueryLayers.Terrain), shapes);
            if (l.X >= c.LocalMin.X && l.X <= c.LocalMax.X && l.Z >= c.LocalMin.Z && l.Z <= c.LocalMax.Z)
                cones.Add($"{l:0.0} ({shapes.Count} solid shapes there)");
        }
        Console.WriteLine($"[design] cones: it rests on {string.Join(" ", rests)}; the cones under its bounds (their foot, the ship's frame): {string.Join(" ", cones)}");
        Leg("lift", Buttons.Sprint, 6);
        Leg("rest", 0, 2);
        c.Parts[seat].SitterPlus1 = 0;
        c.Driver = 255;
        c.DriverSeat = -1;
        Console.WriteLine($"[design] cones: its middle sat {sat:0.00} m over the ground under it, then Shift for 6 s lifted it {lifted:0.0} m");
        return lifted < 5 ? $"Shift for 6 s lifted it {lifted:0.0} m off the cones (at least 5)" : null;
    }

    /// <summary>
    /// A sortie from the helm on the proving ground, as a player flies it (keys held on the seat, as a seated player's are): up on Shift, a
    /// hover, ahead on W, a turn on D, down on Ctrl to the ground. Each leg's height, way, heading and speed, and what a world step cost.
    /// Straight ahead it holds its height: it may sink at most 0.5 m below where W was pressed (it sank 7 m before 2026-11-26).
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
        var sank = 0f;
        float Leg(string name, Buttons keys, float seconds)
        {
            var p0 = c.WorldCom;
            var h0 = Heading();
            var tilt = 0f;
            var low = float.MaxValue;
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
                low = MathF.Min(low, c.WorldCom.Y);
            }
            if (name == "ahead")
                sank = p0.Y - low;
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
        Console.WriteLine($"[design] sortie: straight ahead it sank {sank:0.00} m at the most");
        Console.WriteLine($"[design] sortie: a world step with it flying: mean {sum / Math.Max(1, n):0.000} ms, worst {worst:0.00} ms after its first two seconds ({worstAt}), {slow} over 4 ms, {(GC.GetAllocatedBytesForCurrentThread() - bytes0) / Math.Max(1, n)} bytes allocated a step");
        c.Parts[seat].SitterPlus1 = 0;
        c.Driver = 255;
        c.DriverSeat = -1;
        return up < 10 ? $"Shift for 6 s lifted it {up:0.0} m (at least 10)" : hover < 0.5f * up ? $"let go, it sank to {hover:0.0} m" : ahead < 30 ? $"W for 5 s took it {ahead:0.0} m (at least 30)" : sank > 0.5f ? $"W for 5 s let it sink {sank:0.00} m (at most 0.5)"
            : turn < 30 ? $"D for 5 s turned it {turn:0} degrees (at least 30)" : rest > 1 ? $"Ctrl for 12 s left it {rest:0.0} m up" : worst > 8 ? $"a step took {worst:0.0} ms (at most 8)" : null;
    }
}
