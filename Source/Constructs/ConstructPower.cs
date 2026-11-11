namespace CallOfSlop;

/// <summary>
/// The electrical system (milestone 3; docs/sandbox/CONSTRUCTS.md, "Power"). A construct with a battery part is
/// <b>electrical</b>: its electrical parts (logic parts, moving joints' motors, lamps, horns, anything with a
/// <c>"power"</c> draw) work only while a working battery's links reach them (<see cref="CPart.Powered"/>: the frame is
/// the circuit, so a shot-off beam can cut it) and the batteries have charge; its engine needs the starter (a lump of
/// charge) before it runs; working alternators touching the running engine charge the batteries. A construct with no
/// battery runs as milestone 2 did (nothing needs power) unless the map requires power. Server, every tick before the
/// logic; nothing allocates.
/// </summary>
public static class ConstructPower
{
    public const float StarterKJ = 2.5f; // what the starter takes from the batteries to turn the engine over
    public const float BackOnKJ = 0.2f; // a flat battery comes back on once it holds this much (no flicker at empty)
    public const float MotorEfficiency = 0.8f; // a motor draws its work / this

    /// <summary>Does part <paramref name="i"/> work as far as power goes (always, on a construct that isn't electrical, or for a part that needs none)?</summary>
    public static bool On(Construct c, int i) =>
        i >= 0 && i < c.PartCount && c.PtoDriven[i] ? c.EngineRunning // (a joint on a power take-off runs from the engine)
        : !c.Electrical || i < 0 || i >= c.PartCount || !c.Parts[i].D.Electric || c.PowerOn && c.Parts[i].Powered;

    /// <summary>With the caches: which parts a working battery's links reach, what the batteries hold, the alternators on the engine.</summary>
    public static void Rebuild(Construct c)
    {
        c.HasBattery = false;
        c.MainsWired = false;
        c.ChargeCapacity = 0;
        c.AlternatorRate = 0;
        using var scratch = Scratch.Begin();
        Span<ushort> queue = scratch.Take<ushort>(c.PartCount);
        int head = 0, tail = 0;
        for (var i = 0; i < c.PartCount; i++)
        {
            ref var p = ref c.Parts[i];
            p.Powered = false;
            if (p.D.Role is not (PartRole.Battery or PartRole.Generator))
                continue;
            c.HasBattery = true;
            if (p.D.Role == PartRole.Battery && c.RoutesInto(i, RouteType.Data) > 0)
                c.MainsWired = true;
            if (p.Broken)
                continue;
            c.ChargeCapacity += p.D.Role == PartRole.Generator ? p.D.PlantBuffer : p.D.BatteryCapacity; // (a plant holds a little: it feeds the circuit with no battery fitted)
            p.Powered = true;
            queue[tail++] = (ushort)i;
        }
        // Only the parts at the end of a seam, a bead or a cable look through those lists below (a big hull's plates don't).
        Span<bool> ends = scratch.Take<bool>(c.PartCount);
        ends.Clear();
        void Mark(Span<bool> list, int a, int b)
        {
            if (a < list.Length)
                list[a] = true;
            if (b < list.Length)
                list[b] = true;
        }
        for (var k = 0; k < c.SeamCount; k++)
            Mark(ends, c.SeamLinks[k].A, c.SeamLinks[k].B);
        for (var k = 0; k < c.JointCount; k++)
            if (c.Joints[k] is { Freeform: true, Cut: false } fj)
                Mark(ends, fj.Hinge, fj.Child);
        for (var k = 0; k < c.PowerEdgeCount; k++)
            Mark(ends, c.PowerEdges[k].A, c.PowerEdges[k].B);
        // Through the links of working parts (a wrecked part breaks the circuit).
        while (head < tail)
        {
            int at = queue[head++];
            for (var e = c.FirstLink(at); e >= 0; e = c.NextLink(e))
            {
                var other = c.OtherEnd(e);
                if (other >= c.PartCount || c.Parts[other].Powered || c.Parts[other].Broken)
                    continue;
                c.Parts[other].Powered = true;
                queue[tail++] = (ushort)other;
            }
            if (!ends[at])
                continue;
            // A freeform weld's bead and the sockets that meet across it (board item fd01): the circuit crosses a rigid seam.
            for (var k = 0; k < c.SeamCount + c.JointCount; k++)
            {
                int a, b;
                if (k < c.SeamCount)
                    (a, b) = (c.SeamLinks[k].A, c.SeamLinks[k].B);
                else if (c.Joints[k - c.SeamCount] is { Freeform: true, Cut: false } fj)
                    (a, b) = (fj.Hinge, fj.Child);
                else
                    continue;
                var other = a == at ? b : b == at ? a : -1;
                if (other < 0 || other >= c.PartCount || c.Parts[other].Powered || c.Parts[other].Broken)
                    continue;
                c.Parts[other].Powered = true;
                queue[tail++] = (ushort)other;
            }
            // Power cables (routed links): a cable carries the circuit to where the frame doesn't (both ways).
            for (var k = 0; k < c.PowerEdgeCount; k++)
            {
                ref var e = ref c.PowerEdges[k];
                var other = e.A == at ? e.B : e.B == at ? e.A : -1;
                if (other < 0 || other >= c.PartCount || c.Parts[other].Powered || c.Parts[other].Broken)
                    continue;
                c.Parts[other].Powered = true;
                queue[tail++] = (ushort)other;
            }
        }
        if (c.Engine >= 0)
            for (var i = c.ModActive >= 0 ? 0 : c.Engine; i < (c.ModActive >= 0 ? c.PartCount : c.Engine + 1); i++) // (a modular engine: any of its pieces)
            {
                if (!c.PartOfEngine(i))
                    continue;
                for (var e = c.FirstLink(i); e >= 0; e = c.NextLink(e))
                {
                    var other = c.OtherEnd(e);
                    if (c.Parts[other].D.Role == PartRole.Alternator && c.Parts[other].Working)
                        c.AlternatorRate += c.Parts[other].D.AlternatorRate;
                }
            }
        if (c.Engine >= 0)
            for (var k = 0; k < c.SeamCount; k++)
            {
                ref var l = ref c.SeamLinks[k];
                var other = c.PartOfEngine(l.A) ? l.B : c.PartOfEngine(l.B) ? l.A : -1; // (a modular engine: any of its pieces)
                if (other >= 0 && c.Parts[other].D.Role == PartRole.Alternator && c.Parts[other].Working)
                    c.AlternatorRate += c.Parts[other].D.AlternatorRate;
            }
    }

    /// <summary>
    /// One tick: whether it is electrical, the engine starting (the starter) and stopping, the draw of everything that is
    /// on (last tick's signals and the motors' work), the alternators' charge, and whether there is power.
    /// </summary>
    public static void Step(ConstructWorld world, Construct c, float dt)
    {
        if (c.Dirty)
            c.Rebuild();
        Mains(c);
        var electrical = c.HasBattery || world.PowerRequired;
        var wants = c.Engine >= 0 && (c.Driver != 255 || c.EngineWired || c.SteerRuns || c.RotorRuns) && c.HasFuel; // (an engine with nothing to drive still runs: a power take-off)
        if (electrical != c.Electrical)
        {
            c.Electrical = electrical;
            c.Wake();
        }
        var engineDef = c.Engine >= 0 && c.Engine < c.PartCount ? c.EngineDef : null;
        if (!electrical)
        {
            c.PowerOn = true;
            // (no battery: it runs whenever it could, unless it needs a starter: a diesel, a turbine)
            var asked = c.Driver != 255 && (c.Throttle != 0 || c.AeroTrim > 0 || c.SteerTorque && c.SteerInput != 0) || c.EngineWired && c.EngineSignal != 0 || c.SteerRuns || c.RotorRuns; // (a skid steerer pivots from a standstill)
            c.EngineRunning = wants && engineDef is not { NeedsStarter: true };
            c.StarterFailed = wants && asked && engineDef is { NeedsStarter: true };
            c.PowerUse = 0;
            c.MotorWork = 0;
            ConstructPlant.Step(c, 0, dt); // (a stack shows a running engine with no battery too)
            return;
        }
        if (engineDef is { FuelKind: EngineFuel.Electric })
        {
            // An electric motor needs no starter: it runs while the battery reaches it and has charge.
            c.EngineRunning = wants && c.PowerOn && c.Parts[c.Engine].Powered;
            c.StarterFailed = false;
        }
        // The engine: asked to go (throttle, or a signal into it) it needs the starter; it stops when nobody asks it to.
        else if (!wants)
        {
            c.EngineRunning = false;
            c.StarterFailed = false;
        }
        else if (!c.EngineRunning && (c.Driver != 255 && (c.Throttle != 0 || c.AeroTrim > 0 || c.RotorGoverned || c.SteerTorque && c.SteerInput != 0) || c.EngineWired && c.EngineSignal != 0 || c.SteerRuns || c.RotorRuns))
        {
            var starter = engineDef?.StarterKJ ?? StarterKJ;
            if (c.PowerOn && c.Engine >= 0 && c.Parts[c.Engine].Powered && c.Charge >= starter)
            {
                c.UseCharge(starter);
                c.EngineRunning = true;
                c.StarterFailed = false;
                world.Events.Add($"construct {c.Id}: the engine started");
            }
            else
                c.StarterFailed = true;
        }
        // The draw: what is on (last tick's signals), idling motors, and the motors' work.
        var watts = 0f;
        if (c.PowerOn)
        {
            for (var i = 0; i < c.PartCount; i++)
            {
                ref var p = ref c.Parts[i];
                var d = p.D;
                if (!p.Powered || p.Broken || d.PowerDraw <= 0)
                    continue;
                if ((d.IsRecycler || d.IsGatherer || d.IsRefiner || d.IsAssembler || d.IsPress || d.IsBeacon || d.IsTerminal || d.IsExtruder) && world.StoreOf(c, i) is not { Busy: true, Held: false, Drive: ShaftState.None })
                    continue; // (a Recycler, gathering module, production machine or beacon draws only while it works; one a wire holds off doesn't, even with a job in hand; a drill on a drive shaft never does)
                if (d.Hyd == HydKind.Pump && !ConstructHydraulics.Pumping(c, i))
                    continue; // (a pump on power draws only while a valve is open on it)
                if (d.IsJumpDrive ? (p.State & 1) != 0 // (a jump drive: while it charges)
                    : d.Role == PartRole.Joint ? JointMoving(c, i) : !d.IsSignal || d.IsExtruder || p.Out >= 0.5f || p.Out <= -0.5f) // (an Extruder's output is a length, not on or off)
                    watts += d.PowerDraw;
            }
            watts += c.MotorWork / MotorEfficiency;
        }
        c.MotorWork = 0;
        c.PowerUse = watts;
        ConstructPlant.Step(c, watts / 1000f, dt); // (the power plants, and what the construct's stacks show)
        var kw = (c.EngineRunning ? c.AlternatorRate : 0f) + c.PlantOut - watts / 1000f;
        c.UseCharge(-kw * dt);
        var on = c.PowerOn ? c.Charge > 0 : c.Charge >= BackOnKJ;
        if (on != c.PowerOn)
        {
            c.PowerOn = on;
            c.Wake(); // (motors let go or take hold; the logic's outputs change)
            world.Events.Add($"construct {c.Id}: power {(on ? "back on" : "out: the battery is flat")}");
        }
    }

    /// <summary>
    /// The mains (docs/sandbox/constructs/production.md "Production lines"): a wire into a Battery is its isolator. When every working battery
    /// has one and every one says off, <see cref="Construct.MainsOff"/>: production machines, recyclers, gatherers and beacons get no power
    /// (in creative too), while logic, lamps and motors stay on the batteries, so the switch that opened it can close it. Server, each tick;
    /// a body whose batteries have no wire costs one test.
    /// </summary>
    private static void Mains(Construct c)
    {
        var off = false;
        if (c.MainsWired && c.HasBattery)
        {
            var on = false;
            for (var i = 0; i < c.PartCount && !on; i++)
            {
                ref var p = ref c.Parts[i];
                if (p.D.Role != PartRole.Battery || p.Broken)
                    continue;
                if (ConstructGatherer.HeldOff(c, i))
                    off = true;
                else
                    on = true; // (an unwired battery, or one whose wire says on, keeps the mains on)
            }
            off &= !on;
        }
        if (off != c.MainsOff)
        {
            c.MainsOff = off;
            c.Wake();
        }
    }

    private static bool JointMoving(Construct c, int part)
    {
        var j = c.RailJointOf(part);
        return j >= 0 && MathF.Abs(c.Joints[j].Vel) > 0.05f;
    }

    /// <summary>The charge as a share of what the batteries hold (0 without batteries).</summary>
    public static float Share(Construct c) => c.ChargeCapacity > 0 ? Math.Clamp(c.Charge / c.ChargeCapacity, 0, 1) : 0;
}
