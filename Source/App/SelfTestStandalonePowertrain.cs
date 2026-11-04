using System.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// Board item dev-components-standalone-cs03 (design doc decision 32): every powertrain and joint part works from its
    /// inputs and links, never only on a "vehicle". Seeded, short and driven: an anchored base with no seat (an engine
    /// started by a switch, a lever on its throttle port) turns a wheel through its gearbox and a shaft, and the wheel's
    /// tread drives a loose crate sitting on it; a free cart with no seat drives on a switch and a logic throttle; a
    /// platform on suspensions with no wheels stands on its springs and gives when a player lands on it; an unwired
    /// turntable on an anchored base's power take-off turns with the engine; a motor hinge on an anchored base follows its
    /// lever; the seated buggy drives as before; an idle world of anchored engines costs nothing and nothing allocates;
    /// a client's copy agrees.
    /// </summary>
    private static void CheckStandalonePowertrain()
    {
        SpAnchoredWheel();
        SpSeatlessCart();
        SpSpringPlatform();
        SpPtoTurntable();
        SpAnchoredHinge();
        SpSeatedBuggy();
        SpIdleWorld();
    }

    /// <summary>A base of two frame beams with an engine, its tank and a transmission on it, and no seat; pinned to the ground.</summary>
    private static Construct? SpBase(ConstructWorld w, Vector3 at)
    {
        var bp = ConstructBuild.ParseBlueprint("""
            { "format": "call-of-slop-construct-blueprint/1", "name": "Standalone Base", "steps": [
              { "part": "Frame Beam" },
              { "part": "Frame Beam", "on": 0, "socket": "x+0", "by": "x-0" },
              { "part": "Engine", "on": 1, "socket": "y+1", "by": "-y" },
              { "part": "Fuel Tank", "on": 2, "socket": "+y", "by": "-y" },
              { "part": "Transmission", "on": 2, "socket": "+z", "by": "-z" }
            ] }
            """, "standalone-base.json");
        var c = ConstructBuild.Build(w, bp, at, Quaternion.Identity, out var why);
        if (c == null)
        {
            Fail($"standalone powertrain: the base doesn't build: {why}");
            return null;
        }
        c.Pinned = true;
        c.Rebuild();
        if (!c.Anchored || c.DriveSeat >= 0 || !c.HasPowertrain)
            Fail($"standalone powertrain: the base isn't an anchored seatless powertrain (anchored {c.Anchored}, seat {c.DriveSeat}, powertrain {c.HasPowertrain}: {c.DriveProblem})");
        return c;
    }

    /// <summary>A logic part fitted onto any free socket of the base's frame beams (parts 0 and 1).</summary>
    private static int SpFitAnywhere(ConstructWorld w, Construct c, string part, string? by = null)
    {
        var def = ConstructParts.Need(part);
        for (var on = 0; on < 2; on++)
        {
            var od = c.DefOf(on);
            for (var s = 0; s < od.Sockets.Length; s++)
            {
                if (od.Sockets[s].Kind != SocketKind.Struct || c.SocketUsed(on, s))
                    continue;
                for (var ds = 0; ds < def.Sockets.Length; ds++)
                    if ((by == null || def.Sockets[ds].Name == by) && ConstructBuild.Place(c, on, s, def, ds, 0, out var g) == null)
                    {
                        var i = ConstructBuild.Attach(w, c, def, g);
                        c.Rebuild();
                        return i;
                    }
            }
        }
        string? last = null;
        for (var s = 0; s < c.DefOf(0).Sockets.Length && last == null; s++)
            if (!c.SocketUsed(0, s))
                last = ConstructBuild.Place(c, 0, s, def, Math.Max(0, Array.FindIndex(def.Sockets, x => by == null || x.Name == by)), 0, out _);
        Fail($"standalone powertrain: a {part} doesn't fit on the frame ({last})");
        return -1;
    }

    /// <summary>A data wire into a named port of <paramref name="to"/> (0: the part itself).</summary>
    private static void SpWire(ConstructWorld w, Construct c, int from, int to, int port)
    {
        if (port == 0)
        {
            Wire(w, c, from, to);
            return;
        }
        if (ConstructRoutes.Add(w, c, RouteType.Data, from, 255, 255, 0, default, default, to, ConstructRoutes.DataPortByte(port)) < 0)
            Fail($"standalone powertrain: a wire into the {c.DefOf(to).Name}'s port {port} was refused");
        c.Rebuild();
    }

    private static void SpRun(ConstructWorld w, IWorldQuery q, int ticks)
    {
        for (var t = 0; t < ticks; t++)
            w.Step(q, World.TickDt);
    }

    /// <summary>Steps until <paramref name="done"/> (at most <paramref name="max"/> ticks); the ticks it took, or -1.</summary>
    private static int SpUntil(ConstructWorld w, IWorldQuery q, int max, Func<bool> done)
    {
        for (var t = 0; t < max; t++)
        {
            if (done())
                return t;
            w.Step(q, World.TickDt);
        }
        return done() ? max : -1;
    }

    private static Construct? SpSingle(ConstructWorld w, string part, Vector3 at, Quaternion rot)
    {
        var bp = ConstructBuild.ParseBlueprint($$"""{ "format": "call-of-slop-construct-blueprint/1", "name": "{{part}}", "steps": [ { "part": "{{part}}" } ] }""", "single.json");
        return ConstructBuild.Build(w, bp, at, rot, out _);
    }

    /// <summary>The anchored base turns a wheel (switch: run, lever: throttle) and the wheel's tread carries a crate off it; off, the crate stays.</summary>
    private static void SpAnchoredWheel()
    {
        var w = new ConstructWorld();
        var q = new DriveWorld();
        if (SpBase(w, new Vector3(0, 5, 0)) is not { } c)
            return;
        var hub = RigHub(w, c, RigFit(w, c, T(c), "+z", "Drive Shaft 1 m", "end-"), "end+");
        var sw = SpFitAnywhere(w, c, "Switch");
        var lever = SpFitAnywhere(w, c, "Lever");
        if (hub < 0 || sw < 0 || lever < 0)
            return;
        SpWire(w, c, sw, c.Engine, 0);
        SpWire(w, c, lever, c.Engine, 1);
        var wheel = PartNamed(c, "Wheel");
        if (!c.CanDrive || wheel < 0 || c.WheelCount == 0 || !c.Wheels[0].Driven)
        {
            Fail($"standalone powertrain: the anchored base's wheel isn't driven ({c.DriveProblem})");
            return;
        }
        // A small crate on the wheel's top.
        c.PartBounds(wheel, out var mn, out var mx);
        var top = c.ToWorld(new Vector3((mn.X + mx.X) * 0.5f, mx.Y, (mn.Z + mx.Z) * 0.5f));
        var crate = SpSingle(w, "Square Beam 0.25 m", top + new Vector3(0, 0.14f, 0), Quaternion.Identity);
        if (crate == null)
        {
            Fail("standalone powertrain: the crate doesn't build");
            return;
        }
        crate.Pos += top + new Vector3(0, 0.135f, 0) - crate.WorldCom; // (its centre over the wheel's top)
        var fwd = Vector3.Transform(c.SeatForward(), c.Rot);
        c.Parts[lever].State = 1; // (throttle forward, but the engine isn't asked to run)
        SpRun(w, q, 60);
        var rest = crate.WorldCom;
        var engineOff = !c.EngineRunning && c.Wheels[0].Omega == 0;
        SpRun(w, q, 30);
        var still = Vector3.Distance(crate.WorldCom, rest);
        // Run.
        c.Parts[sw].State = 1;
        var started = SpUntil(w, q, 30, () => c.EngineRunning && MathF.Abs(c.Wheels[0].Omega) > 1);
        var from = crate.WorldCom;
        SpRun(w, q, 40);
        var moved = Vector3.Dot(crate.WorldCom - from, fwd);
        var bytes = GC.GetAllocatedBytesForCurrentThread();
        SpRun(w, q, 60); // (still turning: steady state)
        bytes = GC.GetAllocatedBytesForCurrentThread() - bytes;
        var omega = c.Wheels[0].Omega;
        var live = ConstructPhysics.EngineLive(c);
        // Off again: the wheel runs down and the base leaves the active list.
        c.Parts[sw].State = 0;
        var stop = SpUntil(w, q, 240, () => !ConstructPhysics.EngineLive(c) && c.Wheels[0].Omega == 0);
        Line($"anchored wheel: engine off {engineOff}, the crate moved {still * 1000:0} mm sitting on it; switched on: running after {started} ticks, the wheel at {omega:0.0} rad/s ({c.DefOf(c.Engine).Name} {c.Rpm:0} rpm now), the crate carried {moved:0.00} m forward in 40 ticks, live {live}, {bytes} B in the next 60 ticks; off: stopped after {stop} ticks");
        if (!engineOff || still > 0.02f)
            Fail("standalone powertrain: an anchored base's engine with no run signal shouldn't turn its wheel or move the crate on it");
        if (started < 0 || moved < 0.12f || !live)
            Fail($"standalone powertrain: an anchored base's switch-started engine should turn its wheel and the tread carry the crate (started {started}, moved {moved:0.00} m, live {live})");
        if (stop < 0)
            Fail("standalone powertrain: an anchored base's wheel didn't run down (or the base stayed in the active list) after its engine stopped");
        if (bytes > 0)
            Fail($"standalone powertrain: an anchored base turning a wheel allocated {bytes} B in 60 ticks");
        SpClientAgrees(c, "anchored base");
    }

    /// <summary>The buggy with no seat (a speed gauge where it was): a switch runs the engine and a Scale at 0.5 its throttle; it drives forward.</summary>
    private static void SpSeatlessCart()
    {
        if (DriveBlueprint("Buggy") is not { } buggy)
            return;
        var w = new ConstructWorld();
        var q = new DriveWorld();
        if (DriveSpawn(w, Swap(NoModule(buggy), ("Seat", "Speed Gauge")), 0, 0, MathF.PI / 2, "the seatless cart") is not { } c)
            return;
        var sw = SpFitAnywhere(w, c, "Switch");
        var scale = SpFitAnywhere(w, c, "Scale");
        if (sw < 0 || scale < 0)
            return;
        var sd = c.DefOf(scale);
        if (ConstructConfig.Has(sd, CfgKey.Factor))
            ConstructConfig.Set(ref c.Parts[scale], sd, sd.CfgSlot[(int)CfgKey.Factor], 0.5f);
        else
            c.Parts[scale].Setting = 1; // (x 0.5)
        SpWire(w, c, sw, c.Engine, 0);
        SpWire(w, c, sw, scale, 0);
        SpWire(w, c, scale, c.Engine, 1);
        if (c.DriveSeat >= 0 || !c.CanDrive)
        {
            Fail($"standalone powertrain: the seatless cart doesn't have a drivetrain (seat {c.DriveSeat}, {c.DriveProblem})");
            return;
        }
        SpRun(w, q, 60);
        var fwd = Vector3.Transform(c.SeatForward(), c.Rot);
        var p0 = c.WorldCom;
        SpRun(w, q, 60);
        var parked = MathF.Abs(Vector3.Dot(c.WorldCom - p0, fwd));
        c.Parts[sw].State = 1;
        var p1 = c.WorldCom;
        SpRun(w, q, 120);
        var bytes = GC.GetAllocatedBytesForCurrentThread();
        SpRun(w, q, 120);
        bytes = GC.GetAllocatedBytesForCurrentThread() - bytes;
        var dist = Vector3.Dot(c.WorldCom - p1, fwd);
        Line($"seatless cart: parked {parked * 1000:0} mm; switched on, throttle {c.EngineSignal:0.0} (logic), {dist:0.0} m forward in 4 s, gear {c.Gear + 1}, {c.Rpm:0} rpm, {bytes} B in its last 2 s");
        if (parked > 0.05f)
            Fail("standalone powertrain: the seatless cart moved with its engine switched off");
        if (dist < 3f || MathF.Abs(c.EngineSignal - 0.5f) > 0.01f)
            Fail($"standalone powertrain: a free cart with no seat, a switch on its engine and a logic throttle should drive forward ({dist:0.0} m, throttle {c.EngineSignal:0.00})");
        if (bytes > 0)
            Fail($"standalone powertrain: the seatless cart allocated {bytes} B driving");
        SpClientAgrees(c, "seatless cart");
    }

    /// <summary>A steel plate on four suspensions (no wheels, axles down): it stands on its springs and dips when a player lands on it.</summary>
    private static void SpSpringPlatform()
    {
        var w = new ConstructWorld();
        var q = new DriveWorld();
        var bp = ConstructBuild.ParseBlueprint("""
            { "format": "call-of-slop-construct-blueprint/1", "name": "Spring Platform", "steps": [
              { "part": "Plate Steel 1 m" },
              { "part": "Suspension", "on": 0, "socket": "back00", "by": "mount" },
              { "part": "Suspension", "on": 0, "socket": "back03", "by": "mount" },
              { "part": "Suspension", "on": 0, "socket": "back30", "by": "mount" },
              { "part": "Suspension", "on": 0, "socket": "back33", "by": "mount" }
            ] }
            """, "spring-platform.json");
        var c = ConstructBuild.Build(w, bp, new Vector3(0, 0.6f, 0), Quaternion.Identity, out var why);
        if (c == null)
        {
            Fail($"standalone powertrain: the spring platform doesn't build: {why}");
            return;
        }
        if (c.SpringCount != 4 || c.WheelCount != 0)
        {
            Fail($"standalone powertrain: the platform has {c.SpringCount} spring feet (and {c.WheelCount} wheels), not 4");
            return;
        }
        // Settled on its springs: asleep, standing clear of the floor.
        var slept = SpUntil(w, q, 400, () => c.Asleep);
        c.PartBounds(0, out var mn, out var mx);
        var low = float.MaxValue;
        for (var i = 0; i < c.PartCount; i++)
        {
            c.PartBounds(i, out var a, out var b);
            for (var k = 0; k < 8; k++)
                low = MathF.Min(low, c.ToWorld(new Vector3((k & 1) != 0 ? b.X : a.X, (k & 2) != 0 ? b.Y : a.Y, (k & 4) != 0 ? b.Z : a.Z)).Y);
        }
        var rest = c.Pos.Y;
        var squash = c.Springs[0].Compression;
        // A player lands on its middle from 1.5 m up.
        var deck = c.ToWorld(new Vector3((mn.X + mx.X) * 0.5f, mx.Y, (mn.Z + mx.Z) * 0.5f));
        var s = new MoveState { Position = deck + new Vector3(0, 1.5f, 0) };
        var lowest = rest;
        var stood = 0;
        for (var t = 0; t < 150; t++)
        {
            // (standing on a free construct they live in its frame, and the real mover keeps them on it: ConstructRideMove.cs)
            if (s.RideUp == Vector3.Zero || !ConstructRide.Step(ref s, default))
            {
                s.Velocity.Y -= World.Gravity * World.GravityScale * World.TickDt;
                s.Position += s.Velocity * World.TickDt;
                s.Grounded = false;
            }
            if (w.BlockPlayer(ref s, World.HeightOf(s.Stance), server: true, who: 1) == c)
                stood++;
            w.Step(q, World.TickDt);
            lowest = MathF.Min(lowest, c.Pos.Y);
        }
        var loaded = c.Pos.Y;
        var dip = rest - lowest;
        var sink = rest - loaded;
        // The player steps off: it comes back up.
        for (var t = 0; t < 120; t++)
            w.Step(q, World.TickDt);
        var back = rest - c.Pos.Y;
        Line($"spring platform: asleep after {slept} ticks, its lowest corner {low:0.00} m up, springs {squash * 100:0.0} cm in; a player landed: dipped {dip * 100:0.0} cm, stood {stood} ticks and sat {sink * 100:0.0} cm lower; stepped off: {back * 100:0.0} cm from its rest");
        if (slept < 0 || low < 0.05f || squash <= 0)
            Fail("standalone powertrain: a platform on suspensions with no wheels should stand on its springs (and sleep)");
        if (stood < 100 || dip < 0.02f || sink < 0.01f || dip <= sink)
            Fail($"standalone powertrain: a platform on springs should dip when a player lands on it and sit lower under them (dip {dip:0.000}, under them {sink:0.000} m)");
        if (MathF.Abs(back) > 0.01f)
            Fail($"standalone powertrain: the spring platform didn't come back up ({back:0.000} m)");
    }

    /// <summary>An unwired turntable on an anchored base's power take-off turns while the engine runs (a switch on its run port, a lever on its throttle), and stops when it doesn't.</summary>
    private static void SpPtoTurntable()
    {
        var w = new ConstructWorld();
        var q = new DriveWorld();
        if (SpBase(w, new Vector3(0, 2, 0)) is not { } c)
            return;
        var pto = RigFit(w, c, RigFit(w, c, T(c), "+z", "Drive Shaft 1 m", "end-"), "end+", "Power Take-Off", "-z");
        var tt = RigFit(w, c, pto, "+y", "Turntable", "base");
        RigFit(w, c, tt, "top", "Frame Block", "-y");
        var sw = SpFitAnywhere(w, c, "Switch");
        var lever = SpFitAnywhere(w, c, "Lever");
        if (tt < 0 || sw < 0 || lever < 0 || c.JointOf(tt) < 0 || !c.PtoDriven[tt])
        {
            Fail("standalone powertrain: the PTO turntable rig didn't build (or the turntable isn't on the take-off)");
            return;
        }
        SpWire(w, c, sw, c.Engine, 0);
        SpWire(w, c, lever, c.Engine, 1);
        c.Parts[lever].State = 1; // (the throttle open: the switch runs it)
        var j = c.JointOf(tt);
        SpRun(w, q, 60);
        var off = MathF.Abs(c.Joints[j].Angle);
        c.Parts[sw].State = 1;
        var a0 = c.Joints[j].Angle;
        SpRun(w, q, 120);
        var on = MathF.Abs(c.Joints[j].Angle - a0);
        c.Parts[sw].State = 0;
        var stopped = SpUntil(w, q, 240, () => MathF.Abs(c.Joints[j].Vel) < 0.01f && c.Asleep);
        Line($"PTO turntable (anchored, unwired): {off * 180 / MathF.PI:0} degrees with the engine off, {on * 180 / MathF.PI:0} in 2 s running ({c.Rpm:0} rpm), asleep {stopped} ticks after it stopped");
        if (off > 0.01f || on < 0.5f)
            Fail("standalone powertrain: an unwired turntable on an anchored base's power take-off should turn only while the engine runs");
        if (stopped < 0)
            Fail("standalone powertrain: the PTO turntable's base didn't go back to sleep after the engine stopped");
        SpClientAgrees(c, "PTO base");
    }

    /// <summary>A motor hinge on an anchored base (no seat, no engine) follows its lever.</summary>
    private static void SpAnchoredHinge()
    {
        var w = new ConstructWorld();
        var q = new DriveWorld();
        if (SpBase(w, new Vector3(0, 3, 0)) is not { } c)
            return;
        var hinge = SpFitAnywhere(w, c, "Motor Hinge", "base");
        var lever = SpFitAnywhere(w, c, "Lever");
        if (hinge >= 0)
            FitTurned(w, c, hinge, "leaf", "Frame Block", "-y", _ => true); // (something on its leaf: the joint)
        if (hinge < 0 || lever < 0 || c.JointOf(hinge) < 0)
        {
            Fail("standalone powertrain: the motor hinge didn't fit on the anchored base");
            return;
        }
        Wire(w, c, lever, hinge);
        var j = c.JointOf(hinge);
        c.Parts[lever].State = 1;
        var reached = SpUntil(w, q, 180, () => MathF.Abs(c.Joints[j].Angle) > 1.0f);
        Line($"anchored motor hinge: {c.Joints[j].Angle * 180 / MathF.PI:0} degrees on its lever (after {reached} ticks)");
        if (reached < 0)
            Fail("standalone powertrain: a motor hinge on an anchored base didn't follow its lever");
    }

    /// <summary>The seated buggy drives as it did before (4 s on dirt: the "Construct drivetrain" example run's distance).</summary>
    private static void SpSeatedBuggy()
    {
        if (DriveBlueprint("Buggy") is not { } bp)
            return;
        var run = DriveRun(bp, new DriveWorld { Kind = ShapeKind.Terrain }, 240, Buttons.Forward, "the buggy");
        const float before = SpBuggyBefore; // (main 0b82955c, before this change)
        Line($"seated buggy: {run.Dist:0.000} m in 4 s on dirt (before {before:0.000} m)");
        if (MathF.Abs(run.Dist - before) > 0.01f * before)
            Fail($"standalone powertrain: the seated buggy drives differently ({run.Dist:0.000} m against {before:0.000} m in 4 s)");
    }

    private const float SpBuggyBefore = 51.752f; // (the same run on main 0b82955c; the "Construct drivetrain" section's output is unchanged too)

    /// <summary>Sixty anchored bases with engines, all off, in a big world: nothing is simulated, and a tick allocates nothing.</summary>
    private static void SpIdleWorld()
    {
        var w = new ConstructWorld();
        var q = new DriveWorld();
        var bases = new List<Construct>();
        for (var i = 0; i < 60; i++)
            if (SpBase(w, new Vector3(i % 10 * 8f, 2, i / 10 * 8f)) is { } c)
                bases.Add(c);
        if (bases.Count < 60 || !w.UseGrid)
            return;
        var settled = SpUntil(w, q, 120, () => ConstructPhysics.LastSimulated == 0);
        var sw = new System.Diagnostics.Stopwatch();
        var bytes = GC.GetAllocatedBytesForCurrentThread();
        sw.Start();
        SpRun(w, q, 120);
        sw.Stop();
        var ms = sw.Elapsed.TotalMilliseconds / 120;
        bytes = GC.GetAllocatedBytesForCurrentThread() - bytes;
        var idle = ConstructPhysics.LastSimulated;
        Line($"idle world: {bases.Count} anchored engines off, {idle} simulated after {settled} ticks, {ms:0.000} ms a tick, {bytes} B in 120 ticks");
        if (settled < 0 || idle != 0)
            Fail($"standalone powertrain: an idle world of anchored engines should simulate nothing ({idle} bodies)");
        if (bytes > 0)
            Fail($"standalone powertrain: an idle world of anchored engines allocated {bytes} B in 120 ticks");
        if (ms > 1.0)
            Fail($"standalone powertrain: an idle world of anchored engines took {ms:0.00} ms a tick");
    }

    /// <summary>A client's copy (the full body, then a pose) has the same drive state, wheel and joint as the server's.</summary>
    private static void SpClientAgrees(Construct c, string what)
    {
        var copy = new ConstructWorld();
        var pw = new PacketWriter(65536);
        ConstructNet.WriteFull(pw, c);
        var pr = new PacketReader(pw.Span[1..]);
        ConstructNet.Read(ref pr, copy, 0);
        var cc = copy.Get(c.Id);
        cc?.Rebuild();
        var same = cc != null && cc.HasPowertrain == c.HasPowertrain && cc.CanDrive == c.CanDrive && cc.WheelCount == c.WheelCount && cc.Engine == c.Engine
                   && cc.Transmission == c.Transmission && cc.Anchored == c.Anchored && cc.JointCount == c.JointCount && cc.EngineRunning == c.EngineRunning;
        for (var k = 0; same && k < c.WheelCount; k++)
            same = cc!.Wheels[k].Driven == c.Wheels[k].Driven;
        for (var j = 0; same && j < c.JointCount; j++)
            same = MathF.Abs(cc!.Joints[j].Angle - c.Joints[j].Angle) < 0.01f && cc.PtoDriven[c.Joints[j].Hinge] == c.PtoDriven[c.Joints[j].Hinge];
        if (same && cc != null)
            same = Vector3.Distance(cc.Pos, c.Pos) < 0.01f;
        if (!same)
            Fail($"standalone powertrain: a client's copy of the {what} disagrees (powertrain {cc?.HasPowertrain}/{c.HasPowertrain}, can drive {cc?.CanDrive}/{c.CanDrive}, running {cc?.EngineRunning}/{c.EngineRunning}, {cc?.DriveProblem})");
    }
}
