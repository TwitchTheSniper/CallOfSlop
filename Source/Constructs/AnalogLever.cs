using System.Globalization;
using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The Analog Lever (docs/sandbox/CONSTRUCTS.md, "Logic"): a handle dragged by hand between 0 and 1, for throttles and
/// speed settings. Its value is a part setting ("value": <see cref="CPart.Cfg"/>), so blueprints, world saves and a joiner's
/// CBody carry it with no code of their own, and the configure tool's slider sets it. A hand on it goes through
/// <see cref="CActKind.AnalogLever"/> (<see cref="ConstructEdits.AnalogSet"/>); moves reach clients in small
/// <see cref="CBodyKind.Analog"/> messages, not as the whole construct. A pose's signal is in tenths, too coarse for a handle,
/// so the handle is drawn from the value and never from <see cref="CPart.Out"/>. The input is Program.SandboxLever.cs.
/// A Joystick is an Analog Lever with <c>"axes": 2</c> (<see cref="ConstructPartDef.Stick"/>): "value" is its X (side to side,
/// the part's +x), "valueY" its Y (fore and aft, the part's +z), each with its own rest; X is its first output and Y its
/// second (<see cref="CPart.Out1"/>), and both ride every message a lever's value does.
/// </summary>
public static class AnalogLever
{
    public const float Arc = 35f * MathF.PI / 180f; // the handle's tilt either side of upright
    public const float KnobTurn = 135f / 35f; // a knob turns this many times a lever's tilt: 135 degrees either side of its middle
    public const float FlipAngle = 12f * MathF.PI / 180f; // a rocker either way
    public const float ShownRate = 16f; // how fast a handle eases towards a value heard from the server (1/s)
    public const float SpringRate = 1f; // how fast a spring takes it back (of its whole travel, a second)
    public const float WheelStep = 0.05f; // a notch of the mouse wheel, unless it has a snap step of its own
    public const float DragReach = 1.5f; // beyond the use reach, a hand slips off
    public const int HoldTicks = World.TickRate / 2; // a spring waits this long past a hand's last word (its client's keep-alive comes sooner)
    private const float SendStep = 0.05f; // a spring's way back reaches clients in steps of this

    public static float Value(in CPart p, ConstructPartDef d) => ConstructConfig.Get(p, d, CfgKey.Value, 0);

    /// <summary>A Joystick's Y (0 on a one-axis lever).</summary>
    public static float ValueY(in CPart p, ConstructPartDef d) => d.Stick ? ConstructConfig.Get(p, d, CfgKey.ValueY, 0.5f) : 0;

    /// <summary><paramref name="v"/> within 0..1 and on the part's snap step.</summary>
    public static float Snapped(in CPart p, ConstructPartDef d, float v)
    {
        v = Math.Clamp(v, 0, 1);
        var step = ConstructConfig.Get(p, d, CfgKey.Snap, 0);
        return step > 0 ? Math.Clamp(MathF.Round(v / step) * step, 0, 1) : v;
    }

    /// <summary>The handle's tilt at value <paramref name="v"/>: from back, through upright at 0.5, to forward.</summary>
    public static float Angle(float v) => (v - 0.5f) * 2 * Arc;

    public static ushort ToWire(float v) => (ushort)MathF.Round(Math.Clamp(v, 0, 1) * ushort.MaxValue);

    public static float FromWire(ushort v) => v / (float)ushort.MaxValue;

    /// <summary>The part's value set, as it stands on its setting's step (no snap: the server has done that). True when it changed.</summary>
    public static bool SetValue(ref CPart p, ConstructPartDef d, float v) =>
        d.Logic == CLogic.AnalogLever && ConstructConfig.Set(ref p, d, d.CfgSlot[(int)CfgKey.Value], v);

    /// <summary>Both axes (<paramref name="y"/> only on a Joystick). True when either changed.</summary>
    public static bool SetValue(ref CPart p, ConstructPartDef d, float v, float y)
    {
        var changed = SetValue(ref p, d, v);
        if (d.Stick)
            changed |= ConstructConfig.Set(ref p, d, d.CfgSlot[(int)CfgKey.ValueY], y);
        return changed;
    }

    /// <summary>Server, after the configure tool: on its snap step, and a hand's hold gone.</summary>
    public static void SettingsChanged(ref CPart p, ConstructPartDef d)
    {
        SetValue(ref p, d, Snapped(p, d, Value(p, d)), Snapped(p, d, ValueY(p, d)));
        p.Count = 0;
    }

    /// <summary>
    /// Server, the logic step: the spring, then the output (0..1 of the way from "low" to "high"). A Joystick's Y goes
    /// out on its second output.
    /// </summary>
    public static float Step(ConstructWorld world, Construct c, int i, float dt)
    {
        ref var p = ref c.Parts[i];
        var d = p.D;
        var v = Value(p, d);
        var y = ValueY(p, d);
        if (p.Count > 0)
            p.Count--; // (a hand is on it)
        else if (ConstructConfig.Get(p, d, CfgKey.Spring, 0) >= 0.5f)
        {
            var sent = Spring(ref p, d, CfgKey.Value, CfgKey.Rest, ref v, dt);
            if (d.Stick)
                sent |= Spring(ref p, d, CfgKey.ValueY, CfgKey.RestY, ref y, dt);
            if (sent)
                world.AnalogDirty.Add((c.Id, (ushort)i));
        }
        var low = ConstructConfig.Get(p, d, CfgKey.Low, 0);
        var high = ConstructConfig.Get(p, d, CfgKey.High, 1);
        if (d.Stick)
            p.Out1 = float.Lerp(low, high, y);
        return float.Lerp(low, high, v);
    }

    /// <summary>One axis glides towards its rest. True when clients should hear it (at rest, or past a send step).</summary>
    private static bool Spring(ref CPart p, ConstructPartDef d, CfgKey key, CfgKey restKey, ref float v, float dt)
    {
        var rest = ConstructConfig.Get(p, d, restKey, 0);
        if (v == rest)
            return false;
        var next = v < rest ? MathF.Min(v + SpringRate * dt, rest) : MathF.Max(v - SpringRate * dt, rest);
        p.Cfg[d.CfgSlot[(int)key]] = next; // (not on the setting's step: it glides)
        var send = next == rest || (int)(next / SendStep) != (int)(v / SendStep);
        v = next;
        return send;
    }

    /// <summary>The value as the look line shows it.</summary>
    public static string Text(float v) => v.ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>A part's look-line value: a lever's 0..1; a Joystick's two outputs, "X 0.00 Y 0.00".</summary>
    public static string Text(in CPart p, ConstructPartDef d, float v, float y)
    {
        if (!d.Stick)
            return Text(v);
        var low = ConstructConfig.Get(p, d, CfgKey.Low, 0);
        var high = ConstructConfig.Get(p, d, CfgKey.High, 1);
        return "X " + Text(float.Lerp(low, high, v)) + " Y " + Text(float.Lerp(low, high, y));
    }

    /// <summary>The tilt of a Joystick's stick at <paramref name="y"/>, about the part's x (as a lever's), and at <paramref name="x"/> about its z.</summary>
    public static System.Numerics.Matrix4x4 StickTurn(byte rot, float x, float y) =>
        System.Numerics.Matrix4x4.CreateFromAxisAngle(Rot24.Apply(rot, Vector3.UnitZ), -Angle(x)) * System.Numerics.Matrix4x4.CreateFromAxisAngle(Rot24.Apply(rot, Vector3.UnitX), Angle(y));
}

public sealed partial class ConstructEdits
{
    /// <summary>
    /// Server: a hand on an Analog Lever (any tool, seated or not): sets its value, on its snap step. A lever is used like
    /// any other, so no owner's rule; the configure tool's slider has that. <paramref name="final"/>: the hand let go.
    /// </summary>
    public string? AnalogSet(ConstructWorld world, byte player, Vector3 eye, ushort body, ushort part, float value, float y, bool final)
    {
        if (world.Get(body) is not { } c || part >= c.PartCount || c.Loose)
            return "that lever is gone";
        if (c.Dirty)
            c.Rebuild();
        ref var p = ref c.Parts[part];
        var d = p.D;
        if (d.Logic != CLogic.AnalogLever)
            return "that lever is gone";
        if (p.Broken)
            return $"the {d.Name} is wrecked";
        if (Reachable(eye, ReachPoint(c, part, eye), ConstructBuild.UseReach + AnalogLever.DragReach) is { } far)
            return far;
        AnalogLever.SetValue(ref p, d, AnalogLever.Snapped(p, d, value), AnalogLever.Snapped(p, d, y));
        p.Count = (ushort)(final ? 0 : AnalogLever.HoldTicks);
        world.AnalogDirty.Add((c.Id, part)); // (even at the same value: the host's own hand has already moved it)
        c.Wake();
        return null;
    }
}
