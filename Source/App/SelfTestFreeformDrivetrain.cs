using System.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// "Freeform drivetrain" (board item dev-freeform-drivetrain-fd01, after fw01, sw01 and wh01; docs/sandbox/CONSTRUCTS.md,
    /// "Freeform welds"): a freeform (fixed-pose) segment is rigidly part of its parent for every system, so a buggy whose
    /// transmission and engine are freeform-welded to the chassis at off-grid offsets (sockets meeting across the seam within
    /// 2 cm and 3 degrees, a drive shaft across it, a fuel tank welded onto the engine, a battery welded to the chassis)
    /// drives forward and back, changes gear and goes the wrench-built buggy's distance, with the same wheels driven; the
    /// engine turned too far off the shaft is refused with the angle reason; a shaft across a real joint is still refused;
    /// a client's copy agrees; driving allocates nothing. Driven, per the joint and wheel weld lessons.
    /// </summary>
    private static void CheckFreeformDrivetrain()
    {
        if (ConstructBuild.FindBlueprint("Buggy") is not { } bp)
        {
            Fail("freeform drivetrain: no Buggy blueprint");
            return;
        }
        var q = new DriveWorld();
        var refWorld = new ConstructWorld();
        if (DriveSpawn(refWorld, bp, 0, 0, MathF.PI / 2, "the buggy") is not { } refCar)
            return;
        var reference = FdRun(refWorld, q, refCar, "wrench-built");
        if (reference == null)
            return;
        var trans = PartNamed(refCar, "Transmission");
        var engine = PartNamed(refCar, "Engine");
        var tank = PartNamed(refCar, "Fuel Tank");
        if (trans != 18 || engine != 19 || tank != 20)
        {
            Fail($"freeform drivetrain: the Buggy's steps moved (transmission {trans}, engine {engine}, tank {tank})");
            return;
        }
        var tp = refCar.Parts[trans];
        var ep = refCar.Parts[engine];
        var off = new Vector3(0.012f, -0.006f, 0.009f); // (off the 1 cm grid, inside the seam tolerance)
        // A: the transmission welded to the differential, the engine welded onto the transmission (a chain), off-grid.
        {
            var v = FdCopy(bp);
            var tq = Rot24.Quat(tp.Rot);
            var tPos = tp.Pos + off;
            v.StepWeld[trans] = (tPos, tq);
            var iq = Quaternion.Conjugate(tq);
            var ePos = ep.Pos + off + new Vector3(0.004f, 0, -0.003f);
            v.StepWeld[engine] = (Vector3.Transform(ePos - tPos, iq), iq * Rot24.Quat(ep.Rot));
            FdVariant(v, q, reference.Value, "transmission and engine freeform-welded (chain)", expectSeams: 2);
        }
        // B: only the transmission welded (the engine socket-attached to it, on its grid in that segment).
        {
            var v = FdCopy(bp);
            v.StepWeld[trans] = (tp.Pos + off, Rot24.Quat(tp.Rot));
            FdVariant(v, q, reference.Value, "transmission freeform-welded", expectSeams: 1);
        }
        // C: the tank welded onto the engine (no pipe): the weld's bead carries the fuel.
        {
            var v = FdCopy(bp);
            v.Links.Clear();
            var tkp = refCar.Parts[tank];
            v.Steps[tank] = (v.Steps[tank].Part, engine, v.Steps[tank].Socket, v.Steps[tank].Own, v.Steps[tank].Spin);
            v.StepWeld[tank] = (tkp.Pos + new Vector3(0, 0.01f, 0), Rot24.Quat(tkp.Rot));
            FdVariant(v, q, reference.Value, "fuel tank welded onto the engine", expectSeams: -1);
        }
        FdShaft(bp, q, reference.Value, refCar);
        FdJointStillRefused(bp);
        FdPower(bp);
    }

    private static ConstructBuild.Blueprint FdCopy(ConstructBuild.Blueprint b)
    {
        var c = new ConstructBuild.Blueprint { Name = b.Name, File = b.File, Description = b.Description };
        c.Steps.AddRange(b.Steps);
        c.StepSettings.AddRange(b.StepSettings);
        c.StepConfig.AddRange(b.StepConfig);
        c.Wires.AddRange(b.Wires);
        c.Links.AddRange(b.Links);
        c.StepPaint.AddRange(b.StepPaint);
        c.StepWeld.AddRange(b.StepWeld);
        while (c.StepWeld.Count < c.Steps.Count)
            c.StepWeld.Add(null);
        return c;
    }

    private readonly record struct FdResult(float Fwd, float Back, int Gear, int Driven, long Bytes);

    /// <summary>Settles, drives 1.5 s forward and 1.5 s back (distances along the car's forward), and counts the driven wheels. Null: it can't drive.</summary>
    private static FdResult? FdRun(ConstructWorld w, IWorldQuery q, Construct c, string what)
    {
        if (!WwSettle(w, q, c))
        {
            Fail($"freeform drivetrain ({what}): the buggy didn't settle on its wheels");
            return null;
        }
        if (!c.CanDrive)
        {
            Fail($"freeform drivetrain ({what}): it can't drive: {c.DriveProblem}");
            return null;
        }
        Seat(c);
        var driven = 0;
        for (var k = 0; k < c.WheelCount; k++)
            driven += c.Wheels[k].Driven ? 1 : 0;
        var fwd = Vector3.Transform(c.SeatForward(), c.Rot) with { Y = 0 };
        fwd = Vector3.Normalize(fwd);
        var p0 = c.WorldCom;
        var gear = 0;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var t = 0; t < 90; t++)
        {
            DriveKeys(c, Buttons.Forward);
            w.Step(q, World.TickDt);
            gear = Math.Max(gear, c.Gear);
        }
        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        var p1 = c.WorldCom;
        for (var t = 0; t < 40; t++)
        {
            DriveKeys(c, Buttons.None);
            w.Step(q, World.TickDt);
        }
        // Reverse: brake to a stop and back away (the most backward speed along the car).
        var back = 0f;
        for (var t = 0; t < 300; t++)
        {
            DriveKeys(c, Buttons.Back);
            w.Step(q, World.TickDt);
            back = MathF.Min(back, Vector3.Dot(c.Vel, fwd));
        }
        var r = new FdResult(Vector3.Dot(p1 - p0, fwd), back, gear, driven, bytes);
        Line($"freeform drivetrain ({what}): {r.Driven} driven wheels, 1.5 s forward {r.Fwd:0.00} m (top gear {r.Gear}), reversing at {r.Back:0.0} m/s, {r.Bytes} bytes");
        return r;
    }

    private static void FdCompare(FdResult r, FdResult reference, string what)
    {
        var rel = MathF.Abs(r.Fwd - reference.Fwd) / MathF.Max(reference.Fwd, 0.1f);
        if (r.Driven != reference.Driven || reference.Fwd < 1f || rel > 0.15f || r.Back > -0.5f || r.Gear != reference.Gear || r.Gear < 1)
            Fail($"freeform drivetrain: the {what} buggy doesn't drive like the wrench's (driven {r.Driven}/{reference.Driven}, forward {r.Fwd:0.00} vs {reference.Fwd:0.00} m, reverse {r.Back:0.0} m/s, gear {r.Gear}/{reference.Gear})");
        if (r.Bytes > 0)
            Fail($"freeform drivetrain: {r.Bytes} bytes allocated driving the {what} buggy");
    }

    /// <summary>A copy of the construct as a client reads it: the same drive state.</summary>
    private static void FdClientAgrees(Construct c, string what)
    {
        var copy = new ConstructWorld();
        var pw = new PacketWriter(65536);
        ConstructNet.WriteFull(pw, c);
        var pr = new PacketReader(pw.Span[1..]);
        ConstructNet.Read(ref pr, copy, 0);
        var cc = copy.Get(c.Id);
        cc?.Rebuild();
        var same = cc != null && cc.CanDrive == c.CanDrive && cc.SeamCount == c.SeamCount && cc.WheelCount == c.WheelCount && cc.Transmission == c.Transmission && cc.Engine == c.Engine && cc.DriveProblem == c.DriveProblem;
        for (var k = 0; same && k < c.WheelCount; k++)
            same = cc!.Wheels[k].Driven == c.Wheels[k].Driven && cc.Wheels[k].Ratio == c.Wheels[k].Ratio;
        if (!same)
            Fail($"freeform drivetrain: a client's copy of the {what} buggy has another drive line (can drive {cc?.CanDrive}/{c.CanDrive}, seam links {cc?.SeamCount}/{c.SeamCount}, {cc?.DriveProblem})");
    }

    private static void FdVariant(ConstructBuild.Blueprint v, DriveWorld q, FdResult reference, string what, int expectSeams)
    {
        var w = new ConstructWorld();
        var car = ConstructBuild.BuildOnGround(w, v, new Vector3(0, 0, 0), MathF.PI / 2, out var why);
        if (car == null)
        {
            Fail($"freeform drivetrain ({what}): doesn't build: {why}");
            return;
        }
        var seamParts = 0;
        for (var i = 0; i < car.PartCount; i++)
            seamParts += car.Parts[i].Seg != 0 ? 1 : 0;
        Line($"freeform drivetrain ({what}): {car.FreeformCount} freeform welds, {seamParts} parts on them, {car.SeamCount} sockets meeting across a seam, can drive {car.CanDrive} {car.DriveProblem}");
        if (car.FreeformCount == 0)
            Fail($"freeform drivetrain ({what}): no freeform weld was made");
        if (expectSeams >= 0 && car.SeamCount != expectSeams)
            Fail($"freeform drivetrain ({what}): {car.SeamCount} sockets meet across the seams (expected {expectSeams})");
        if (FdRun(w, q, car, what) is not { } r)
            return;
        FdCompare(r, reference, what);
        FdClientAgrees(car, what);
    }

    /// <summary>
    /// The engine welded 0.61 m behind where it sat, joined to the transmission by a drive shaft across the seam: accepted on
    /// the axis and the buggy drives; the engine turned 60 degrees off is refused with the angle reason.
    /// </summary>
    private static void FdShaft(ConstructBuild.Blueprint bp, DriveWorld q, FdResult reference, Construct refCar)
    {
        var tp = refCar.Parts[18];
        var ep = refCar.Parts[19];
        var tdef = refCar.DefOf(18);
        var edef = refCar.DefOf(19);
        var outPort = Array.FindIndex(edef.Sockets, s => s.Name == "+z");
        var inPort = Array.FindIndex(tdef.Sockets, s => s.Name == "-z");
        if (outPort < 0 || inPort < 0)
        {
            Fail("freeform drivetrain: the engine or transmission lost the sockets the shaft test uses");
            return;
        }
        // The engine's drive-out side faces the transmission along the engine's +z; back it away along that axis.
        refCar.SocketAt(19, outPort, out _, out var outDir);
        var away = -ConstructParts.DirVector(outDir);
        for (var pass = 0; pass < 2; pass++)
        {
            var turned = pass == 1;
            var v = FdCopy(bp);
            v.Links.Clear();
            var ePos = ep.Pos + away * 0.613f + new Vector3(0.0f, 0.0f, 0.0f);
            var eRot = Rot24.Quat(ep.Rot);
            if (turned)
                eRot = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 60f * MathF.PI / 180f) * eRot;
            // (the engine is the 20th part: the tank's pipe is dropped, the tank sits on the engine's old plate beside it)
            v.StepWeld[19] = (ePos, eRot);
            v.Steps[19] = (v.Steps[19].Part, 16, v.Steps[19].Socket, v.Steps[19].Own, v.Steps[19].Spin);
            var w = new ConstructWorld();
            var car = ConstructBuild.BuildOnGround(w, v, Vector3.Zero, MathF.PI / 2, out var why);
            if (car == null)
            {
                Fail($"freeform drivetrain (shaft{(turned ? ", turned" : "")}): doesn't build: {why}");
                return;
            }
            var nodes = ReadOnlySpan<CRouteNode>.Empty;
            var bad = ConstructRoutes.Check(w, car, RouteType.Shaft, 19, (byte)outPort, 255, 0, Vector3.Zero, nodes, 18, (byte)inPort);
            Line($"freeform drivetrain (shaft{(turned ? ", engine turned 60 degrees" : "")}): {(bad == null ? "accepted" : $"refused: {bad}")}");
            if (turned)
            {
                if (bad == null || !bad.Contains("degrees off") || !bad.Contains("U joint"))
                    Fail($"freeform drivetrain: a shaft from an engine turned 60 degrees off the line wasn't refused with the angle reason ({bad ?? "accepted"})");
                continue;
            }
            if (bad != null)
            {
                Fail($"freeform drivetrain: a drive shaft from the freeform-welded engine to the transmission is refused: {bad}");
                return;
            }
            if (ConstructRoutes.Add(w, car, RouteType.Shaft, 19, (byte)outPort, 255, 0, Vector3.Zero, nodes, 18, (byte)inPort) < 0)
            {
                Fail("freeform drivetrain: the drive shaft across the seam can't be added");
                return;
            }
            // (the tank rides on the plate: pipe it to the engine across the seam too)
            car.Rebuild();
            var pipeBad = ConstructRoutes.Check(w, car, RouteType.Pipe, 20, 255, 255, 0, Vector3.Zero, nodes, 19, FdFuelIn(car));
            if (pipeBad == null)
                ConstructRoutes.Add(w, car, RouteType.Pipe, 20, 255, 255, 0, Vector3.Zero, nodes, 19, FdFuelIn(car));
            else
                Line($"freeform drivetrain (shaft): the tank's pipe to the engine: {pipeBad}");
            car.Rebuild();
            Line($"freeform drivetrain (shaft): {car.RouteCount} links, can drive {car.CanDrive} {car.DriveProblem}");
            if (FdRun(w, q, car, "drive shaft across a freeform seam") is not { } r)
                return;
            FdCompare(r, reference, "drive-shaft");
            FdClientAgrees(car, "drive-shaft");
        }
    }

    private static byte FdFuelIn(Construct car)
    {
        var d = car.DefOf(19);
        for (byte p = 0; p < 8; p++)
            if (ConstructRoutes.PortOf(d, RouteType.Pipe, p) is { Fluid: "fuel" })
                return p;
        return 255;
    }

    /// <summary>A shaft across a real joint (the buggy's steering pivot) is still refused; across a freeform seam it is not.</summary>
    private static void FdJointStillRefused(ConstructBuild.Blueprint bp)
    {
        var w = new ConstructWorld();
        if (DriveSpawn(w, bp, 0, 0, MathF.PI / 2, "the buggy") is not { } car)
            return;
        var onPivot = -1;
        for (var i = 0; i < car.PartCount && onPivot < 0; i++)
            if (!car.Rigid(car.Parts[i].Seg))
                onPivot = i;
        if (onPivot < 0)
        {
            Fail("freeform drivetrain: the buggy has nothing on a steering pivot");
            return;
        }
        Span<Vector3> pts = [new Vector3(0, 0, 0), new Vector3(0, 0, 1)];
        Span<int> hosts = [19, onPivot];
        var why = ConstructRoutes.PathRule(car, RouteType.Shaft, pts, hosts, false, 255, true, 255);
        Line($"freeform drivetrain: a shaft to a part on a steering pivot: {why ?? "accepted"}");
        if (why == null || !why.Contains("cross a joint"))
            Fail($"freeform drivetrain: a drive shaft across a real joint isn't refused ({why ?? "accepted"})");
    }

    /// <summary>A battery freeform-welded to the chassis powers a lamp on it (the bead conducts); without the weld's bead it would not.</summary>
    private static void FdPower(ConstructBuild.Blueprint bp)
    {
        if (ConstructParts.Find("Battery") is not { } batt || ConstructParts.Find("Lamp") is not { } lamp)
        {
            Fail("freeform drivetrain: no Battery or Lamp part");
            return;
        }
        var w = new ConstructWorld();
        if (DriveSpawn(w, bp, 0, 0, MathF.PI / 2, "the buggy") is not { } car)
            return;
        var plate = PartNamed(car, "Plate Steel 1 m");
        // Both welded freeform to the plate: the circuit has to cross the seams and back.
        var pp = car.Parts[plate];
        var b = w.AddFreeform(car, plate, batt, pp.Pos + new Vector3(0.1f, 0.5f, 0.2f), Quaternion.CreateFromAxisAngle(Vector3.Normalize(new Vector3(0.2f, 1, 0.1f)), 0.3f));
        if (b < 0)
        {
            Fail("freeform drivetrain: the battery can't be welded on");
            return;
        }
        var l = w.AddFreeform(car, plate, lamp, pp.Pos + new Vector3(-0.2f, 0.45f, 0.0f), Quaternion.Identity);
        car.Rebuild();
        var powered = l >= 0 && car.HasBattery && car.Parts[l].Powered;
        Line($"freeform drivetrain: a freeform-welded battery powers a lamp welded beside it: {powered}");
        if (!powered)
            Fail("freeform drivetrain: a freeform-welded battery doesn't power a freeform-welded lamp through the chassis");
    }
}
