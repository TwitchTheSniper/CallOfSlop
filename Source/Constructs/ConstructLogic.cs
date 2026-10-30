using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The logic step (milestone 2; docs/sandbox/CONSTRUCTS.md, "Logic"), server only, every tick before the physics. Each
/// part puts out a signal (<see cref="CPart.Out"/>: on/off as 1/0, or a number). <b>Every wire carries the value its
/// source had last tick</b>: each tick works out every part's next output from last tick's outputs, then all of them
/// change at once. So the order parts are worked in never matters, a chain of N parts takes N ticks, and a loop (a NOT
/// wired into itself) is simply a clock: on one tick, off the next. Outputs drive the actuators the same tick: a joint's
/// motor, the engine's throttle, a brake, a passive hinge's lock. Fixed spans only: nothing allocates.
/// </summary>
public static class ConstructLogic
{
    public const float ButtonSeconds = 0.5f; // a button stays on this long
    public const float MaxSignal = 1000f;

    /// <summary>A lever's four positions (each Interact moves it one on): middle, forward, middle, back.</summary>
    public static float LeverValue(byte state) => (state & 3) switch { 1 => 1f, 3 => -1f, _ => 0f };

    public static void Step(ConstructWorld world, Construct c, float dt)
    {
        if (c.Dirty)
            c.Rebuild();
        if (!c.HasLogic && c.XWireIn == 0)
        {
            SetGear(c, false, 0);
            // No logic: only a hinge's lock and the engine's wiring need clearing (nothing is wired).
            for (var i = 0; i < c.PartCount; i++)
            {
                c.Parts[i].Keys = 0;
                c.Parts[i].Stood = false;
                c.Parts[i].In = 0;
                c.Parts[i].InCount = 0;
            }
            for (var j = 0; j < c.JointCount; j++)
            {
                c.Joints[j].Wired = false;
                c.Joints[j].Locked = false;
            }
            c.EngineWired = false;
            c.BrakeSignal = 0;
            return;
        }
        Span<float> sum = stackalloc float[Construct.MaxParts];
        Span<byte> count = stackalloc byte[Construct.MaxParts];
        Span<byte> high = stackalloc byte[Construct.MaxParts];
        Span<byte> firstFrom = stackalloc byte[Construct.MaxParts];
        Span<float> sum1 = stackalloc float[Construct.MaxParts], sum2 = stackalloc float[Construct.MaxParts]; // (named data ports 1 and 2: the spawners' spawn now and rate)
        Span<byte> count1 = stackalloc byte[Construct.MaxParts], count2 = stackalloc byte[Construct.MaxParts];
        sum.Clear();
        count.Clear();
        high.Clear();
        sum1.Clear();
        sum2.Clear();
        count1.Clear();
        count2.Clear();
        for (var k = 0; k < c.WireCount; k++)
        {
            ref var w = ref c.Wires[k];
            if (w.From >= c.PartCount || w.To >= c.PartCount)
                continue;
            var v = c.Parts[w.From].Broken ? 0f : w.FromPort == 1 ? c.Parts[w.From].Out1 : c.Parts[w.From].Out;
            if (w.ToPort == 1)
            {
                sum1[w.To] += v;
                count1[w.To]++;
                continue;
            }
            if (w.ToPort >= 2)
            {
                sum2[w.To] += v;
                count2[w.To]++;
                continue;
            }
            if (count[w.To] == 0)
                firstFrom[w.To] = w.From;
            sum[w.To] += v;
            count[w.To]++;
            if (v >= 0.5f)
                high[w.To]++;
        }
        // Wires from other constructs (milestone 3): their sources' values as they were last tick (ConstructLinks.cs).
        if (c.XWireIn > 0)
            for (var k = 0; k < world.XWireCount; k++)
            {
                ref var x = ref world.XWires[k];
                if (x.BodyTo != c.Id || x.To >= c.PartCount)
                    continue;
                if (count[x.To] == 0)
                    firstFrom[x.To] = 255; // (not a seat of this construct)
                sum[x.To] += x.Value;
                count[x.To]++;
                if (x.Value >= 0.5f)
                    high[x.To]++;
            }
        var ticks = 1f / MathF.Max(dt, 1e-4f);
        var changed = false;
        for (var i = 0; i < c.PartCount; i++)
        {
            ref var p = ref c.Parts[i];
            var d = p.D;
            float next;
            if (p.Broken || !ConstructPower.On(c, i))
                next = 0; // (wrecked, or an electrical part with no power: milestone 3)
            else if (d.Role == PartRole.Seat)
                next = (p.Keys & CPart.Occupied) != 0 ? 1 : 0;
            else if (d.Role != PartRole.Logic)
                next = 0;
            else
            {
                var setting = d.Settings.Length > 0 ? d.Settings[Math.Min(p.Setting, (byte)(d.Settings.Length - 1))] : 0f;
                var on = high[i] > 0;
                switch (d.Logic)
                {
                    case CLogic.Button:
                        // Latched (a setting): each press flips it on or off; else on for its hold time after a press.
                        if (ConstructConfig.Get(p, d, CfgKey.Latched, 0) >= 0.5f)
                        {
                            next = (p.State & 1) != 0 ? 1 : 0;
                            break;
                        }
                        next = p.Count > 0 ? 1 : 0;
                        if (p.Count > 0)
                            p.Count--;
                        break;
                    case CLogic.Switch:
                        next = (p.State & 1) != 0 ? 1 : 0;
                        break;
                    case CLogic.Cargo:
                    {
                        // A cargo volume (ConstructCargo.cs): Interact flips it; a change of its input sets it (the last change wins).
                        if (count[i] > 0)
                        {
                            if (on != ((p.State & 2) != 0))
                                p.State = (byte)((p.State & ~3) | (on ? 3 : 0));
                        }
                        else
                            p.State = (byte)(p.State & ~2);
                        next = (p.State & 1) != 0 ? 1 : 0;
                        if ((next >= 0.5f) != (p.Out >= 0.5f))
                            world.CargoScanNow = true; // (frozen or let go: the cargo step looks at once)
                        break;
                    }
                    case CLogic.Lever:
                        next = LeverValue(p.State);
                        break;
                    case CLogic.Plate:
                        next = p.Stood ? 1 : 0;
                        break;
                    case CLogic.Reader:
                    {
                        // The seat wired into it, else the driver's seat.
                        var seat = count[i] > 0 && firstFrom[i] < c.PartCount && c.Parts[firstFrom[i]].D.Role == PartRole.Seat ? firstFrom[i] : c.DriveSeat;
                        next = seat >= 0 && seat < c.PartCount && (c.Parts[seat].Keys & (ushort)setting) != 0 ? 1 : 0;
                        break;
                    }
                    case CLogic.Speed:
                    {
                        // In its units (m/s, km/h, mph); with a threshold above 0, on/off: 1 at or over it.
                        var units = ConstructConfig.Get(p, d, CfgKey.Units, 0);
                        var v = c.Vel.Length() * (units >= 1.5f ? 2.23694f : units >= 0.5f ? 3.6f : 1f);
                        var threshold = ConstructConfig.Get(p, d, CfgKey.Threshold, 0);
                        next = threshold > 0 ? v >= threshold ? 1 : 0 : v;
                        break;
                    }
                    case CLogic.Proximity:
                    {
                        c.WorldFrame(p.Seg, out var q, out var t);
                        c.PartBounds(i, out var mn, out var mx);
                        var at = t + Vector3.Transform((mn + mx) * 0.5f, q);
                        setting = ConstructConfig.Get(p, d, CfgKey.Radius, setting);
                        next = 0;
                        for (var k = 0; k < world.PlayerSpotCount; k++)
                            if (Vector3.DistanceSquared(world.PlayerSpots[k], at) <= setting * setting)
                            {
                                next = 1;
                                break;
                            }
                        break;
                    }
                    case CLogic.And:
                        next = count[i] > 0 && high[i] == count[i] ? 1 : 0;
                        break;
                    case CLogic.Or:
                        next = on ? 1 : 0;
                        break;
                    case CLogic.Not:
                        next = on ? 0 : 1;
                        break;
                    case CLogic.Timer:
                    {
                        // On for its duty share of the period, off for the rest, while enabled (no input, or an input on);
                        // "start on" begins each run with the on part.
                        var startOn = ConstructConfig.Get(p, d, CfgKey.StartOn, 0) >= 0.5f;
                        if (count[i] > 0 && !on)
                        {
                            p.Count = 0;
                            p.State = (byte)(startOn ? 1 : 0);
                            next = 0;
                            break;
                        }
                        var period = ConstructConfig.Get(p, d, CfgKey.Period, setting);
                        var duty = Math.Clamp(ConstructConfig.Get(p, d, CfgKey.Duty, 50) / 100f, 0.01f, 0.99f);
                        var phase = Math.Max(1, (int)MathF.Round(period * ((p.State & 1) != 0 ? duty : 1 - duty) * ticks));
                        if (++p.Count >= phase)
                        {
                            p.Count = 0;
                            p.State ^= 1;
                        }
                        next = (p.State & 1) != 0 ? 1 : 0;
                        break;
                    }
                    case CLogic.Delay:
                    {
                        // The output copies the input once the input has held its new value for the delay (its mode: both
                        // ways, only switching on, or only switching off; the other way follows at once).
                        var want = on ? 1f : 0f;
                        var mode = (int)ConstructConfig.Get(p, d, CfgKey.Mode, 0);
                        if (want != p.Out && (mode == 1 && want == 0 || mode == 2 && want == 1))
                        {
                            p.Count = 0;
                            next = want;
                            break;
                        }
                        if (want != p.Out)
                        {
                            if (++p.Count >= Math.Max(1, (int)MathF.Round(ConstructConfig.Get(p, d, CfgKey.Seconds, setting) * ticks)))
                            {
                                p.Count = 0;
                                next = want;
                                break;
                            }
                        }
                        else
                            p.Count = 0;
                        next = p.Out;
                        break;
                    }
                    case CLogic.Toggle:
                    {
                        // State bit 0: the output; bit 1: the input last tick (a rising edge flips the output).
                        var was = (p.State & 2) != 0;
                        if (on && !was)
                            p.State ^= 1;
                        p.State = (byte)((p.State & 1) | (on ? 2 : 0));
                        next = (p.State & 1) != 0 ? 1 : 0;
                        break;
                    }
                    case CLogic.Compare:
                    {
                        // Its operator against the threshold; hysteresis keeps it as it is until the sum is that far past.
                        var th = ConstructConfig.Get(p, d, CfgKey.Threshold, setting);
                        var hy = ConstructConfig.Get(p, d, CfgKey.Hysteresis, 0);
                        var was = p.Out >= 0.5f;
                        var x = sum[i];
                        next = (int)ConstructConfig.Get(p, d, CfgKey.Operator, 0) switch
                        {
                            1 => x >= (was ? th - hy : th) ? 1 : 0,
                            2 => MathF.Abs(x - th) <= MathF.Max(was ? hy * 2 : hy, 0.001f) ? 1 : 0,
                            3 => x <= (was ? th + hy : th) ? 1 : 0,
                            4 => x < (was ? th + hy : th) ? 1 : 0,
                            _ => x > (was ? th - hy : th) ? 1 : 0,
                        };
                        break;
                    }
                    case CLogic.Scale:
                    {
                        var lo = ConstructConfig.Get(p, d, CfgKey.Low, -MaxSignal);
                        var hi = ConstructConfig.Get(p, d, CfgKey.High, MaxSignal);
                        next = Math.Clamp(sum[i] * ConstructConfig.Get(p, d, CfgKey.Factor, setting) + ConstructConfig.Get(p, d, CfgKey.Offset, 0), MathF.Min(lo, hi), MathF.Max(lo, hi));
                        break;
                    }
                    case CLogic.FuelGauge:
                    {
                        var cap = c.FuelCapacity();
                        next = cap > 0 ? MathF.Round(Math.Clamp(c.Fuel / cap, 0, 1) * 100) / 100 : 0; // (in hundredths: a slow drain doesn't change it every tick)
                        break;
                    }
                    case CLogic.ChargeGauge:
                        next = MathF.Round(ConstructPower.Share(c) * 100) / 100;
                        break;
                    case CLogic.Selector:
                    {
                        // State: the position (0 .. positions - 1; Interact or a rising input steps it on); Count: the input last tick.
                        var positions = Math.Max(2, (int)setting);
                        if (on && p.Count == 0)
                            p.State = (byte)((p.State + 1) % positions);
                        p.Count = (ushort)(on ? 1 : 0);
                        if (p.State >= positions)
                            p.State = 0;
                        next = p.State + 1;
                        break;
                    }
                    case CLogic.BotSpawner:
                    case CLogic.ZombieSpawner:
                        next = p.SpawnAlive; // (how many it has alive: the game counts them, GameSandboxSpawns.cs)
                        break;
                    default: // lamp, horn, brake: on while the input is
                        next = on ? 1 : 0;
                        break;
                }
            }
            if (!float.IsFinite(next))
                next = 0;
            p.OutNext = Math.Clamp(next, -MaxSignal, MaxSignal);
        }
        // Everything changes at once (one tick a wire), and the actuators take their inputs.
        var hash = 2166136261u;
        c.BrakeSignal = 0;
        c.EngineWired = false;
        c.EngineSignal = 0;
        for (var i = 0; i < c.PartCount; i++)
        {
            ref var p = ref c.Parts[i];
            if (p.Out != p.OutNext)
                changed = true;
            p.Out = p.OutNext;
            p.Keys = 0;
            p.Stood = false;
            p.In = sum[i]; // (a gun's trigger reads it next tick: ConstructWeapons.cs)
            p.InCount = count[i];
            p.In1 = sum1[i];
            p.InCount1 = count1[i];
            p.In2 = sum2[i];
            p.InCount2 = count2[i];
            var d = p.D;
            if (d.IsSignal)
                hash = (hash ^ (uint)Quantise(p.Out)) * 16777619u;
            if (d.Logic == CLogic.Brake && p.Out >= 0.5f)
                c.BrakeSignal = MathF.Max(c.BrakeSignal, Math.Clamp(ConstructConfig.Get(p, d, CfgKey.Strength, 100) / 100f, 0, 1)); // (its strength setting: how hard)
            if (i == c.Engine && count[i] > 0)
            {
                c.EngineWired = true;
                c.EngineSignal = Math.Clamp(sum[i], -1, 1);
            }
        }
        for (var j = 0; j < c.JointCount; j++)
        {
            ref var jt = ref c.Joints[j];
            int part = jt.Hinge;
            var wired = part < c.PartCount && count[part] > 0;
            var input = wired ? sum[part] : 0;
            if (wired != jt.Wired || input != jt.Input)
                changed = true;
            jt.Wired = wired;
            jt.Input = input;
            jt.Locked = !jt.Motorised && wired && high[part] > 0;
        }
        var tr = c.Transmission;
        SetGear(c, tr >= 0 && tr < c.PartCount && count[tr] > 0, tr >= 0 && tr < c.PartCount ? sum[tr] : 0);
        c.SigHash = hash;
        if (changed || c.EngineWired && c.EngineSignal != 0)
            c.Wake();
    }

    /// <summary>
    /// The transmission's gear (milestone 3): a signal wired into it picks the gear (1 the first; rounded, held inside the
    /// gears it has); unwired, or a signal below 0.5, it stays in the gear of its setting (the linking tool's R).
    /// </summary>
    private static void SetGear(Construct c, bool wired, float signal)
    {
        var tr = c.Transmission;
        if (tr < 0 || tr >= c.PartCount || c.Parts[tr].D is not { HasGears: true } d)
        {
            c.Gear = 0;
            c.GearRatio = 1f;
            return;
        }
        if (d.Trans is TransKind.Automatic or TransKind.Cvt)
            return; // (they pick their own gear: ConstructDrive.Tick)
        var gear = Math.Min(c.Parts[tr].Setting, (byte)(d.Settings.Length - 1));
        if (wired && signal >= 0.5f)
            gear = (byte)Math.Clamp((int)MathF.Round(signal) - 1, 0, d.Settings.Length - 1);
        if (gear != c.Gear)
            c.Wake();
        c.Gear = gear;
        c.GearRatio = d.Settings[gear];
    }

    /// <summary>A signal on the wire to clients: tenths, -12.7 to 12.7.</summary>
    public static sbyte Quantise(float v) => (sbyte)Math.Clamp(MathF.Round(v * 10), -127, 127);
}
