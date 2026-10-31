using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// "Wheel welds" (developer report: welds between a wheel and a suspension didn't allow rotation or suspension;
    /// docs/sandbox/CONSTRUCTS.md, "Socket weld" and "Freeform welds"): the buggy's four wheels taken off and welded back on
    /// with the welder (socket weld at on- and off-grid spins, the front ones onto the steering pivots' suspensions; and
    /// freeform welds of wheels held a few centimetres off their axles) give the same raycast wheels as the wrench's: they
    /// spin when driven, the springs carry the car and compress landing from a drop, it drives and turns like the
    /// blueprint's buggy, a client's copy from the construct message has the same wheels; a wheel freeform-welded to a beam is
    /// a fixed wheel (it doesn't roll, the look line says so). Driving allocates nothing and stays in the tick budget.
    /// Seeded, scripted, on flat ground; settling waits for the state with a deadline.
    /// </summary>
    private static void CheckWheelWeld()
    {
        if (ConstructBuild.FindBlueprint("Buggy") is not { } bp)
        {
            Fail("wheel welds: no Buggy blueprint");
            return;
        }
        var q = new DriveWorld();
        var refWorld = new ConstructWorld();
        if (DriveSpawn(refWorld, bp, 0, 0, MathF.PI / 2, "the buggy") is not { } refCar)
            return;
        var reference = WwMeasure(refWorld, q, refCar, "wrench (blueprint)");
        if (reference == null)
            return;
        WwVariant(bp, q, reference.Value, freeform: false);
        WwVariant(bp, q, reference.Value, freeform: true);
        WwFixedWheel(bp, q);
    }

    private readonly record struct WwResult(int Wheels, int Steers, float Rest, float Peak, float Dist, float Spin, float Yaw, double Ms, long Bytes);

    /// <summary>Settles: at least 20 ticks, then until every wheel touches and the body is still (deadline 240 ticks). False: it didn't.</summary>
    private static bool WwSettle(ConstructWorld w, IWorldQuery q, Construct c)
    {
        for (var t = 0; t < 240; t++)
        {
            w.Step(q, World.TickDt);
            if (t < 20)
                continue;
            var touching = c.WheelCount > 0;
            for (var k = 0; k < c.WheelCount; k++)
                touching &= c.Wheels[k].Touching;
            if (touching && c.Vel.Length() < 0.05f && c.AngVel.Length() < 0.05f)
                return true;
        }
        return false;
    }

    private static WwResult? WwMeasure(ConstructWorld w, IWorldQuery q, Construct c, string what)
    {
        if (!WwSettle(w, q, c))
        {
            Fail($"wheel welds ({what}): the buggy didn't settle on its wheels in 4 s ({c.WheelCount} wheels, speed {c.Vel.Length():0.00})");
            return null;
        }
        var rest = float.MaxValue;
        var steers = 0;
        for (var k = 0; k < c.WheelCount; k++)
        {
            rest = MathF.Min(rest, c.Wheels[k].Compression);
            steers += c.Wheels[k].Steers ? 1 : 0;
        }
        // A bump: dropped from 0.35 m, the springs take the landing (the most compression until it has bounced).
        c.Pos += new Vector3(0, 0.35f, 0);
        c.PrevPos = c.Pos;
        c.Vel = c.AngVel = Vector3.Zero;
        c.Wake();
        var peak = 0f;
        for (var t = 0; t < 90; t++)
        {
            w.Step(q, World.TickDt);
            for (var k = 0; k < c.WheelCount; k++)
                peak = MathF.Max(peak, c.Wheels[k].Compression);
        }
        if (!WwSettle(w, q, c))
        {
            Fail($"wheel welds ({what}): the buggy didn't settle after the drop");
            return null;
        }
        // Throttle for 1 s: how far, how fast the driven wheels spin (the slowest), then 1 s turning right.
        Seat(c);
        var p0 = c.WorldCom;
        Span<float> spin0 = stackalloc float[Construct.MaxWheels];
        for (var k = 0; k < c.WheelCount; k++)
            spin0[k] = c.Wheels[k].Spin;
        var before = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        var worst = 0.0;
        for (var t = 0; t < 60; t++)
        {
            DriveKeys(c, Buttons.Forward);
            var t0 = sw.Elapsed.TotalMilliseconds;
            w.Step(q, World.TickDt);
            worst = Math.Max(worst, sw.Elapsed.TotalMilliseconds - t0);
        }
        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        var dist = Vector3.Distance(p0 with { Y = 0 }, c.WorldCom with { Y = 0 });
        var spin = float.MaxValue;
        for (var k = 0; k < c.WheelCount; k++)
            if (c.Wheels[k].Driven)
                spin = MathF.Min(spin, MathF.Abs(c.Wheels[k].Spin - spin0[k]) / 1f);
        if (spin == float.MaxValue)
            spin = 0;
        var y0 = YawOf(c);
        for (var t = 0; t < 60; t++)
        {
            DriveKeys(c, Buttons.Forward | Buttons.Right);
            w.Step(q, World.TickDt);
        }
        var yaw = World.WrapAngle(YawOf(c) - y0) * 180 / MathF.PI;
        var r = new WwResult(c.WheelCount, steers, rest, peak, dist, spin, yaw, worst, bytes);
        Line($"wheel welds ({what}): {r.Wheels} raycast wheels ({r.Steers} steer), rest compression {r.Rest * 100:0.0} cm, {r.Peak * 100:0.0} cm landing a 0.35 m drop; 1 s of throttle: {r.Dist:0.00} m, driven wheels spin at {r.Spin:0.0} rad/s or more; 1 s turning right {r.Yaw:0.0} deg; worst tick {r.Ms:0.00} ms, {r.Bytes} bytes");
        return r;
    }

    /// <summary>The buggy with its wheels off and welded back on with the welder (socket weld, or freeform weld near each axle).</summary>
    private static void WwVariant(ConstructBuild.Blueprint bp, IWorldQuery q, WwResult reference, bool freeform)
    {
        var what = freeform ? "freeform-welded" : "socket-welded";
        var w = new ConstructWorld();
        if (DriveSpawn(w, bp, 0, 0, MathF.PI / 2, "the buggy") is not { } car || ConstructParts.Find("Wheel") is not { } wheelDef)
            return;
        WwSettle(w, q, car);
        for (var i = car.PartCount - 1; i >= 0; i--)
            if (car.DefOf(i).Role == PartRole.Wheel)
                w.RemovePart(car, i);
        car.Rebuild();
        var wheelAxle = SwSocket(wheelDef, "axle");
        var hands = FwHands();
        float[] spins = [0, 15, 90, 200]; // (on the grid and off it: a wheel turned about its own axle is the same wheel)
        var welded = 0;
        for (var n = 0; n < 4; n++)
        {
            // The next suspension with a free axle socket.
            int susp = -1, axle = -1;
            for (var i = 0; i < car.PartCount && susp < 0; i++)
                if (car.DefOf(i).Role == PartRole.Suspension && (axle = SwSocket(car.DefOf(i), "axle")) >= 0 && !car.SocketUsed(i, axle))
                    susp = i;
            if (susp < 0)
                break;
            var onPivot = car.Parts[susp].Seg != 0;
            var target = new WeldTarget { Body = car.Id, Part = (byte)susp, Socket = (byte)axle };
            car.SocketAt(susp, axle, out var tAt, out var tDir);
            car.WorldFrame(car.Parts[susp].Seg, out var sq, out var st);
            var axleWorld = st + Vector3.Transform(tAt, sq);
            var outward = Vector3.Transform(ConstructParts.DirVector(tDir), sq);
            var eye = axleWorld + outward * 0.8f + new Vector3(0, 1.1f, 0);
            string? why;
            if (!freeform)
            {
                if (FwLoose(w, "Wheel", axleWorld + outward * 1.1f + new Vector3(0, 0.4f, 0), Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.5f + n), frozen: true) is not { } loose)
                    return;
                why = hands.BeginSocketWeld(w, 0, eye, loose.Id, 0, wheelAxle, target, spins[n], 0);
            }
            else
            {
                // Held by hand a few centimetres off the axle and a few degrees out, where a player lets go of it.
                if (FwLoose(w, "Wheel", Vector3.Zero, Quaternion.Identity, frozen: true) is not { } loose)
                    return;
                SocketWeld.Pose(loose, 0, wheelAxle, car, target, spins[n], out _, out _, out var gp, out var gq);
                var tilt = Quaternion.CreateFromAxisAngle(Vector3.Normalize(new Vector3(0.3f, 1, 0.2f)), 4f * MathF.PI / 180);
                loose.Pos = loose.PrevPos = gp + outward * 0.04f + new Vector3(0, 0.02f, 0);
                loose.Rot = loose.PrevRot = Quaternion.Normalize(tilt * gq);
                var snaps = Freeform.AxleSnap(loose, 0, car, susp, out _) && !Freeform.FixedWheel(loose, 0, car, susp);
                why = snaps ? hands.BeginFreeform(w, 0, eye, loose.Id, 0, car.Id, susp, 0) : "the axle snap didn't find the suspension's axle";
            }
            var ok = why == null && car.SocketUsed(susp, axle) && car.FreeformCount == 0;
            Line($"wheel welds ({what}): wheel {n + 1} onto the {car.DefOf(susp).Name} (part {susp}{(onPivot ? ", on a steering pivot" : "")}) at {spins[n]:0} deg: {(ok ? "linked on its axle" : $"not linked ({why ?? $"{car.FreeformCount} freeform segments"})")}");
            if (!ok)
            {
                Fail($"wheel welds: a {what} wheel isn't linked to its suspension's axle ({why})");
                return;
            }
            welded++;
        }
        car.Rebuild();
        if (welded != 4 || car.WheelCount != reference.Wheels)
        {
            Fail($"wheel welds: {welded} {what} wheels gave {car.WheelCount} raycast wheels (the wrench's buggy has {reference.Wheels})");
            return;
        }
        // A client's copy (the construct message) has the same wheels.
        var copy = new ConstructWorld();
        var pw = new PacketWriter(65536);
        ConstructNet.WriteFull(pw, car);
        var pr = new PacketReader(pw.Span[1..]);
        ConstructNet.Read(ref pr, copy, 0);
        var cc = copy.Get(car.Id);
        cc?.Rebuild();
        var same = cc != null && cc.WheelCount == car.WheelCount;
        for (var k = 0; same && k < car.WheelCount; k++)
            same = cc!.Wheels[k].Part == car.Wheels[k].Part && cc.Wheels[k].Suspension == car.Wheels[k].Suspension && cc.Wheels[k].Seg == car.Wheels[k].Seg;
        Line($"wheel welds ({what}): a client's copy has {cc?.WheelCount ?? -1} raycast wheels on the same suspensions {same}");
        if (!same)
            Fail($"wheel welds: a client's copy of the {what} buggy has other wheels");
        if (WwMeasure(w, q, car, what) is not { } r)
            return;
        var rel = MathF.Abs(r.Dist - reference.Dist) / MathF.Max(reference.Dist, 0.1f);
        var yawRel = MathF.Abs(r.Yaw - reference.Yaw) / MathF.Max(MathF.Abs(reference.Yaw), 1f);
        if (r.Steers != reference.Steers || r.Spin < 3f || r.Rest < 0.005f || r.Peak < r.Rest + 0.01f || rel > 0.1f || yawRel > 0.2f || MathF.Sign(r.Yaw) != MathF.Sign(reference.Yaw))
            Fail($"wheel welds: the {what} buggy doesn't roll, spring or steer like the wrench's (steer {r.Steers}/{reference.Steers}, spin {r.Spin:0.0} rad/s, rest {r.Rest * 100:0.0} cm, peak {r.Peak * 100:0.0} cm, {r.Dist:0.00} m vs {reference.Dist:0.00} m, {r.Yaw:0.0} vs {reference.Yaw:0.0} deg)");
        if (r.Bytes > 0)
            Fail($"wheel welds: {r.Bytes} bytes allocated in 1 s of driving the {what} buggy");
        if (OperatingSystem.IsWindows() && r.Ms > 8) // (wall clock: only the Windows runners are quiet enough to judge it)
            Fail($"wheel welds: a tick took {r.Ms:0.00} ms (the budget is 8 ms)");
    }

    /// <summary>A wheel freeform-welded to a beam (not a suspension) is a fixed wheel: welded, but it doesn't roll.</summary>
    private static void WwFixedWheel(ConstructBuild.Blueprint bp, IWorldQuery q)
    {
        var w = new ConstructWorld();
        if (DriveSpawn(w, bp, 0, 0, MathF.PI / 2, "the buggy") is not { } car)
            return;
        WwSettle(w, q, car);
        var wheels = car.WheelCount;
        var beam = PartNamed(car, "Square Beam 2 m");
        if (beam < 0)
        {
            Fail("wheel welds: the buggy has no 2 m beam");
            return;
        }
        Freeform.PartBox(car, beam, out var bq, out var bc, out var bh);
        var up = Vector3.Transform(Vector3.UnitY, bq);
        if (FwLoose(w, "Wheel", bc + up * (bh.Y + 0.42f), bq, frozen: true) is not { } loose)
            return;
        var fixedWheel = Freeform.FixedWheel(loose, 0, car, beam);
        var eye = bc + up * 1.8f;
        var why = FwHands().BeginFreeform(w, 0, eye, loose.Id, 0, car.Id, beam, 0);
        car.Rebuild();
        var ok = fixedWheel && why == null && car.FreeformCount == 1 && car.WheelCount == wheels;
        Line($"wheel welds: a wheel freeform-welded onto the beam: a fixed wheel {fixedWheel}, welded {why ?? "ok"}, {car.FreeformCount} freeform segment, raycast wheels {wheels} -> {car.WheelCount}");
        if (!ok)
            Fail($"wheel welds: a wheel welded to a beam isn't a fixed wheel ({why})");
    }
}
