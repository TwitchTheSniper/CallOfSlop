namespace CallOfSlop;

/// <summary>How a body's engine generator set stands (server; clients get it in the pose, protocol 339).</summary>
public enum GenState : byte
{
    None, // no Generator Head on the body
    NoEngine, // no head has an engine on its Drive in (a shaft from the flywheel, a gearbox or a power take-off)
    NotFuel, // the engine on it doesn't burn fuel (an electric motor, a hamster wheel): it can't run a generator
    HeldOff, // every head's run wire says off
    NoFuel, // the engine's tanks are dry
    Standby, // the batteries are full and nothing draws: the engine is stopped
    Starting, // asked to run, the engine isn't turning yet (it needs its starter, or it is spooling)
    NoStart, // the starter failed: no battery, or a flat one
    Running, // it follows the circuit's demand
    Overloaded, // the circuit asks more than the set gives: the batteries take the rest
    Stalled, // the engine is held down (its other loads too) and stalled
    COUNT,
}

/// <summary>The engine generator set's state on the body (ConstructGenset).</summary>
public sealed partial class Construct
{
    public int GenHeads; // Generator Heads among the plants (both ends work it out with the plants)
    public bool GenRuns; // the heads ask the engine to run (server)
    public float GenLevel; // 0..1: the share of the set's output it gives (it comes up over the heads' spool, as fast as the engine carries it)
    public float GenThrottle; // 0..1: the throttle the set asks of the engine
    public float GenRpm; // the rpm the set governs the engine to
    public float GenLoad; // N m at the crank that the heads take (server; carried like a drill's: ConstructDrillDrive.Carry)
    public float GenIdle; // seconds it has run with nothing asked of it
    public float GenMax; // kW the set can give: the heads' rating, or the engine's power times their efficiency, whichever is less
    public float GenOut; // kW it gives this tick (server; a client's from the pose)
    public float GenDemand; // kW the circuit draws (server; a client's from the pose)
    public GenState Gen; // how it stands
    internal byte GenOutSent, GenDemandSent, GenSent; // what the last pose said
}

/// <summary>
/// Engine generator sets (docs/sandbox/constructs/power-and-links.md "Engine generators"; design notes in
/// docs/sandbox/design/engine-generators-notes.md). A Generator Head (role <c>generator</c> with <c>"shaft"</c>: its efficiency)
/// is a power plant turned by the body's engine through its Drive in: bolted on a flywheel's output, on a gearbox or power take-off,
/// or at the end of a drive shaft. The head follows what the circuit asks, as a plant does, and asks the engine for the power: the
/// engine starts when the batteries run down (or the draw would flatten them within <see cref="StartSeconds"/>), is governed to the
/// rpm and throttle the load needs, burns fuel by that load (ConstructDrive.Burn: the head's torque is carried like a drill's), and
/// stops after <see cref="IdleStop"/> s with the batteries full and nothing drawing. An engine that can't carry the load sheds it: the
/// head comes up only as fast as the engine keeps up. Server, in the power step; nothing allocates.
/// </summary>
public static class ConstructGenset
{
    public const float StartBelow = 0.5f; // a stopped set starts when the batteries fall under this share
    public const float StartSeconds = 60f; // ...or when the draw would flatten them within this
    public const float IdleStop = 20f; // seconds at full charge with no draw before it stops
    public const float IdleShare = 0.02f; // "no draw": under this share of what the set gives
    public const float KeepUp = 0.98f; // the head comes up only while the engine carries this share of its load

    /// <summary>The engine's power at its peak, kW (the curve is 1 there).</summary>
    public static float EngineKw(ConstructPartDef e) => e.EngineTorque * e.PeakRpm / ConstructDrive.RpmPerRadS / 1000f;

    /// <summary>
    /// One tick, from the plants' step: the heads give what the circuit still needs (<paramref name="need"/>, kW, the draw plus the
    /// batteries' lack), up to <see cref="Construct.GenMax"/>, and set what they ask of the engine for the next physics step.
    /// </summary>
    public static void Step(Construct c, float drawKw, ref float need, ref float rated, float dt)
    {
        c.GenDemand = drawKw;
        float rate = 0, effSum = 0, spool = 0, all = 0;
        var heldOff = true;
        var any = false;
        for (var k = 0; k < c.PlantCount; k++)
        {
            int part = c.Plants[k];
            if (part >= c.PartCount)
                continue;
            ref var p = ref c.Parts[part];
            var d = p.D;
            if (d.PlantShaft <= 0)
                continue;
            all += d.PlantRate;
            var feed = c.ShaftFeedCount > 0 ? ConstructDrillDrive.FeedOf(c, part) : -1;
            if (!p.Working || feed < 0)
            {
                p.Out = 0;
                continue;
            }
            any = true;
            if (p.InCount > 0 && p.In < 0.5f)
            {
                p.Out = 0;
                continue; // (its run wire holds it off)
            }
            heldOff = false;
            rate += d.PlantRate;
            effSum += d.PlantRate * d.PlantShaft * c.ShaftFeeds![feed].Eff;
            spool = MathF.Max(spool, d.PlantSpool);
        }
        rated += all;
        var e = c.Engine >= 0 && c.Engine < c.PartCount && c.HasPowertrain ? c.EngineDef : null;
        var state = !any || e == null ? GenState.NoEngine : e.FuelKind != EngineFuel.Fuel ? GenState.NotFuel : heldOff ? GenState.HeldOff : !c.HasFuel ? GenState.NoFuel : GenState.Running;
        var eff = rate > 0 ? effSum / rate : 0;
        c.GenMax = state is GenState.Running or GenState.NoFuel && e != null ? MathF.Min(rate, EngineKw(e) * eff) : 0;
        var was = c.GenRuns;
        if (state != GenState.Running)
            c.GenRuns = false;
        else
        {
            var share = c.ChargeCapacity > 0 ? c.Charge / c.ChargeCapacity : 0;
            if (!c.GenRuns)
            {
                if (share < StartBelow || drawKw > 0 && c.Charge < drawKw * StartSeconds)
                {
                    c.GenRuns = true;
                    c.GenIdle = 0;
                }
            }
            else
            {
                c.GenIdle = share >= 0.99f && drawKw < IdleShare * c.GenMax ? c.GenIdle + dt : 0;
                if (c.GenIdle > IdleStop)
                    c.GenRuns = false;
            }
            if (!c.GenRuns)
                state = GenState.Standby;
        }
        if (c.GenRuns != was)
            c.Wake(); // (an anchored body steps its engine only while it runs: ConstructPhysics.EngineLive)
        var turning = c.GenRuns && c.EngineRunning && ConstructPhysics.DriveOn(c) && c.Rpm > 1 && e != null;
        if (c.GenRuns && !turning)
            state = c.StarterFailed ? GenState.NoStart : c.StallTicks > 0 ? GenState.Stalled : GenState.Starting;
        // The heads' share: towards what is asked, up over their spool and only while the engine keeps up, down at once.
        var want = turning && c.GenMax > 0 ? Math.Clamp(need / c.GenMax, 0, 1) : 0f;
        var up = spool > 0 ? dt / spool : 1f;
        if (c.ShaftShare < KeepUp)
            up = 0;
        c.GenLevel = Math.Clamp(c.GenLevel + Math.Clamp(want - c.GenLevel, -1f, up), 0, 1);
        var output = turning ? c.GenLevel * c.GenMax * c.ShaftShare : 0f;
        c.GenOut = output;
        c.PlantOut += output;
        need -= output;
        if (turning && drawKw > c.GenMax * c.ShaftShare + 1e-3f)
            state = GenState.Overloaded; // (the draw alone is more than it gives: the batteries run down)
        c.Gen = c.GenHeads > 0 ? state : GenState.None;
        // What it asks of the engine for the next physics step: the rpm (up with the demand, so the power is there before the load), the
        // load's torque at that speed, and the throttle that carries it.
        if (turning)
        {
            var omega = MathF.Max(c.Rpm, e!.IdleRpm) / ConstructDrive.RpmPerRadS;
            c.GenLoad = c.GenLevel * c.GenMax * 1000f / MathF.Max(0.05f, eff) / MathF.Max(1f, omega);
            var can = e.EngineTorque * ConstructDrive.Curve(e, c.Rpm);
            c.GenThrottle = Math.Clamp(c.GenLoad / MathF.Max(1f, can) * 1.05f + 0.02f, 0, 1);
            c.GenRpm = e.IdleRpm + (e.PeakRpm - e.IdleRpm) * MathF.Sqrt(MathF.Max(want, c.GenLevel));
        }
        else
        {
            c.GenLoad = 0;
            c.GenThrottle = 0;
            c.GenRpm = e?.IdleRpm ?? 0;
            if (!c.GenRuns)
                c.GenLevel = 0;
        }
        // Each working head's output: its share of the load (0 to 1), for a wire.
        var load = c.GenMax > 0 ? MathF.Round(output / c.GenMax * 100) / 100 : 0;
        for (var k = 0; k < c.PlantCount; k++)
        {
            int part = c.Plants[k];
            if (part < c.PartCount && c.Parts[part].D.PlantShaft > 0 && c.Parts[part].Working && (c.Parts[part].InCount == 0 || c.Parts[part].In >= 0.5f))
                c.Parts[part].Out = load;
        }
    }

    /// <summary>Does the set govern the engine (its rpm and throttle)? Not while a driver or a wire drives it, nor on a body with driven wheels: there it takes its load off what the engine turns.</summary>
    public static bool Governs(Construct c) => c.GenRuns && !c.CanDrive && !c.Driven && !c.EngineWired;

    /// <summary>The throttle the engine runs at: the set's while it governs.</summary>
    public static float Throttle(Construct c, float throttle) => Governs(c) ? MathF.Max(MathF.Abs(throttle), c.GenThrottle) : throttle;

    /// <summary>The look line on a Generator Head (both ends: from the pose's numbers on a client).</summary>
    public static string Look(Construct c, int part)
    {
        ref var p = ref c.Parts[part];
        var d = p.D;
        if (p.Broken)
            return SandboxKeys.Line(d.Name, "wrecked");
        var feed = c.ShaftFeedCount > 0 ? ConstructDrillDrive.FeedOf(c, part) : -1;
        if (feed < 0)
            return SandboxKeys.Line(d.Name, $"{d.PlantRate:0.#} kW, {d.PlantShaft * 100:0}%", "no engine: bolt it on a flywheel's output, or run a drive shaft to its Drive in");
        var fuel = c.FuelLeft;
        var fuelText = fuel >= 10 ? $"fuel {fuel:0} l" : $"fuel {fuel:0.0} l";
        var rpm = c.EngineRunning && c.Rpm > 1 ? $"{c.Rpm:0} rpm" : "engine off";
        var state = $"{c.GenOut:0.#} of {c.GenMax:0.#} kW, draw {c.GenDemand:0.#} kW, {rpm}, {fuelText}";
        string? why = c.Gen switch
        {
            GenState.NotFuel => "the engine burns no fuel: it can't turn a generator",
            GenState.HeldOff => "held off by its run wire",
            GenState.NoFuel => "no fuel: fill the engine's tanks",
            GenState.Standby => "standby: starts when the batteries run down",
            GenState.Starting => "starting",
            GenState.NoStart => "won't start: the starter needs a charged battery",
            GenState.Overloaded => "overloaded: the circuit asks more than it gives",
            GenState.Stalled => "stalled: the engine is held down",
            GenState.NoEngine => "no engine on its Drive in",
            _ => null,
        };
        return SandboxKeys.Line(d.Name, state, why);
    }

    /// <summary>What decides <see cref="Look"/> beyond the part (the look line's cache key).</summary>
    public static long LookKey(Construct c) =>
        (long)c.Gen ^ (long)(int)(c.GenOut * 10) << 4 ^ (long)(int)(c.GenDemand * 10) << 22 ^ (long)(int)(c.Rpm / 50) << 40 ^ (long)(int)(c.FuelLeft * 10) << 50 ^ (long)(int)c.GenMax << 30;
}
