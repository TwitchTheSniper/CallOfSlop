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
        c.ChargeCapacity = 0;
        c.AlternatorRate = 0;
        Span<byte> queue = stackalloc byte[Construct.MaxParts];
        int head = 0, tail = 0;
        for (var i = 0; i < c.PartCount; i++)
        {
            ref var p = ref c.Parts[i];
            p.Powered = false;
            if (p.D.Role != PartRole.Battery)
                continue;
            c.HasBattery = true;
            if (p.Broken)
                continue;
            c.ChargeCapacity += p.D.BatteryCapacity;
            p.Powered = true;
            queue[tail++] = (byte)i;
        }
        // Through the links of working parts (a wrecked part breaks the circuit).
        while (head < tail)
        {
            int at = queue[head++];
            for (var k = 0; k < c.LinkCount; k++)
            {
                ref var l = ref c.Links[k];
                var other = l.A == at ? l.B : l.B == at ? l.A : -1;
                if (other < 0 || other >= c.PartCount || c.Parts[other].Powered || c.Parts[other].Broken)
                    continue;
                c.Parts[other].Powered = true;
                queue[tail++] = (byte)other;
            }
        }
        if (c.Engine >= 0)
            for (var k = 0; k < c.LinkCount; k++)
            {
                ref var l = ref c.Links[k];
                var other = l.A == c.Engine ? l.B : l.B == c.Engine ? l.A : -1;
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
        var electrical = c.HasBattery || world.PowerRequired;
        var wants = c.CanDrive && (c.Driver != 255 || c.EngineWired) && c.HasFuel;
        if (electrical != c.Electrical)
        {
            c.Electrical = electrical;
            c.Wake();
        }
        var engineDef = c.Engine >= 0 && c.Engine < c.PartCount ? c.DefOf(c.Engine) : null;
        if (!electrical)
        {
            c.PowerOn = true;
            // (no battery: it runs whenever it could, unless it needs a starter: a diesel, a turbine)
            var asked = c.Driver != 255 && c.Throttle != 0 || c.EngineWired && c.EngineSignal != 0;
            c.EngineRunning = wants && engineDef is not { NeedsStarter: true };
            c.StarterFailed = wants && asked && engineDef is { NeedsStarter: true };
            c.PowerUse = 0;
            c.MotorWork = 0;
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
        else if (!c.EngineRunning && (c.Driver != 255 && c.Throttle != 0 || c.EngineWired && c.EngineSignal != 0))
        {
            if (c.PowerOn && c.Engine >= 0 && c.Parts[c.Engine].Powered && c.Charge >= StarterKJ)
            {
                c.Charge -= StarterKJ;
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
                if (d.Role == PartRole.Joint ? JointMoving(c, i) : !d.IsSignal || p.Out >= 0.5f || p.Out <= -0.5f)
                    watts += d.PowerDraw;
            }
            watts += c.MotorWork / MotorEfficiency;
        }
        c.MotorWork = 0;
        c.PowerUse = watts;
        var kw = (c.EngineRunning ? c.AlternatorRate : 0f) - watts / 1000f;
        c.Charge = Math.Clamp(c.Charge + kw * dt, 0, c.ChargeCapacity);
        var on = c.PowerOn ? c.Charge > 0 : c.Charge >= BackOnKJ;
        if (on != c.PowerOn)
        {
            c.PowerOn = on;
            c.Wake(); // (motors let go or take hold; the logic's outputs change)
            world.Events.Add($"construct {c.Id}: power {(on ? "back on" : "out: the battery is flat")}");
        }
    }

    private static bool JointMoving(Construct c, int part)
    {
        var j = c.JointOf(part);
        return j >= 0 && MathF.Abs(c.Joints[j].Vel) > 0.05f;
    }

    /// <summary>The charge as a share of what the batteries hold (0 without batteries).</summary>
    public static float Share(Construct c) => c.ChargeCapacity > 0 ? Math.Clamp(c.Charge / c.ChargeCapacity, 0, 1) : 0;
}
