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
        if (!c.HasLogic)
        {
            // No logic: only a hinge's lock and the engine's wiring need clearing (nothing is wired).
            for (var i = 0; i < c.PartCount; i++)
            {
                c.Parts[i].Keys = 0;
                c.Parts[i].Stood = false;
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
        sum.Clear();
        count.Clear();
        high.Clear();
        for (var k = 0; k < c.WireCount; k++)
        {
            ref var w = ref c.Wires[k];
            if (w.From >= c.PartCount || w.To >= c.PartCount)
                continue;
            var v = c.Parts[w.From].Broken ? 0f : c.Parts[w.From].Out;
            if (count[w.To] == 0)
                firstFrom[w.To] = w.From;
            sum[w.To] += v;
            count[w.To]++;
            if (v >= 0.5f)
                high[w.To]++;
        }
        var ticks = 1f / MathF.Max(dt, 1e-4f);
        var changed = false;
        for (var i = 0; i < c.PartCount; i++)
        {
            ref var p = ref c.Parts[i];
            var d = p.D;
            float next;
            if (p.Broken)
                next = 0;
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
                        next = p.Count > 0 ? 1 : 0;
                        if (p.Count > 0)
                            p.Count--;
                        break;
                    case CLogic.Switch:
                        next = (p.State & 1) != 0 ? 1 : 0;
                        break;
                    case CLogic.Lever:
                        next = LeverValue(p.State);
                        break;
                    case CLogic.Plate:
                        next = p.Stood ? 1 : 0;
                        break;
                    case CLogic.Reader:
                    {
                        // The seat wired into it, else the driver's seat.
                        var seat = count[i] > 0 && c.Parts[firstFrom[i]].D.Role == PartRole.Seat ? firstFrom[i] : c.DriveSeat;
                        next = seat >= 0 && seat < c.PartCount && (c.Parts[seat].Keys & (ushort)setting) != 0 ? 1 : 0;
                        break;
                    }
                    case CLogic.Speed:
                        next = c.Vel.Length();
                        break;
                    case CLogic.Proximity:
                    {
                        c.WorldFrame(p.Seg, out var q, out var t);
                        c.PartBounds(i, out var mn, out var mx);
                        var at = t + Vector3.Transform((mn + mx) * 0.5f, q);
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
                        // On for half the period, off for the other half, while enabled (no input, or an input on).
                        if (count[i] > 0 && !on)
                        {
                            p.Count = 0;
                            p.State = 0;
                            next = 0;
                            break;
                        }
                        var half = Math.Max(1, (int)MathF.Round(setting * 0.5f * ticks));
                        if (++p.Count >= half)
                        {
                            p.Count = 0;
                            p.State ^= 1;
                        }
                        next = (p.State & 1) != 0 ? 1 : 0;
                        break;
                    }
                    case CLogic.Delay:
                    {
                        // The output copies the input once the input has held its new value for the delay.
                        var want = on ? 1f : 0f;
                        if (want != p.Out)
                        {
                            if (++p.Count >= Math.Max(1, (int)MathF.Round(setting * ticks)))
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
                        next = sum[i] > setting ? 1 : 0;
                        break;
                    case CLogic.Scale:
                        next = sum[i] * setting;
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
            var d = p.D;
            if (d.IsSignal)
                hash = (hash ^ (uint)Quantise(p.Out)) * 16777619u;
            if (d.Logic == CLogic.Brake && p.Out >= 0.5f)
                c.BrakeSignal = 1;
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
        c.SigHash = hash;
        if (changed || c.EngineWired && c.EngineSignal != 0)
            c.Wake();
    }

    /// <summary>A signal on the wire to clients: tenths, -12.7 to 12.7.</summary>
    public static sbyte Quantise(float v) => (sbyte)Math.Clamp(MathF.Round(v * 10), -127, 127);
}
