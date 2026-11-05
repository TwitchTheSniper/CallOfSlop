using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The keyed inputs (docs/sandbox/CONSTRUCTS.md, "Keys and panels"): a Numpad and a Code Lock. A press names one key, a real
/// box of the part (<c>"key": "7"</c>); digits go to the entry, 'E' accepts and 'C' clears. A Numpad's entry is <see cref="CPart.Reading"/>,
/// which clients hear to show it, and what it accepted is its <see cref="CfgKey.Value"/>. A Code Lock keeps its entry in
/// <see cref="CPart.Mem"/>, server only, so clients only ever see how many digits were typed.
/// </summary>
public static class LogicInputs
{
    public const float NoEntry = -1;
    public const string KeyItem = "Key"; // the part that turns any Key Switch
    private const float MaxEntry = 999999; // (a float holds this many digits exactly)
    private const int AcceptedBit = 2;

    /// <summary>The key box nearest a point of the part (world), or -1: the part's own face, not a key.</summary>
    public static int KeyAt(Construct c, int part, Vector3 world)
    {
        ref var p = ref c.Parts[part];
        var d = p.D;
        c.WorldFrame(p.Seg, out var q, out var t);
        var local = Vector3.Transform(Vector3.Transform(world - t, Quaternion.Conjugate(q)) - p.Pos, Quaternion.Conjugate(Rot24.Quat(p.Rot)));
        var best = -1;
        var near = float.MaxValue;
        foreach (var k in d.UseBoxes)
        {
            ref readonly var b = ref d.Boxes[k];
            if (b.Key == 0)
                continue;
            var off = local - b.Center;
            var inside = Vector3.Abs(off) - b.Half * 1.25f;
            var dist = off.LengthSquared();
            if (MathF.Max(inside.X, MathF.Max(inside.Y, inside.Z)) <= 0.03f && dist < near)
            {
                near = dist;
                best = k;
            }
        }
        return best;
    }

    /// <summary>Server: why this player can't turn this Key Switch, or null: their own, nobody's, or a Key in their hands.</summary>
    public static string? KeyRefusal(Construct c, byte player, ushort held) =>
        Factions.Shares(c.Owner, player) || c.Owner == 255 || c.Wreck || ConstructParts.Get(held)?.Name == KeyItem ? null : "locked: it turns for its owner, or with a Key in your hands";

    /// <summary>The logic step's part: what it puts out.</summary>
    public static float Step(ref CPart p, ConstructPartDef d)
    {
        if (d.Logic == CLogic.Numpad)
        {
            p.Out1 = (p.State & AcceptedBit) != 0 ? 1 : 0;
            p.State = (byte)(p.State & ~AcceptedBit);
            return ConstructConfig.Get(p, d, CfgKey.Value, 0);
        }
        if (p.Count > 0 && --p.Count == 0)
            p.State = (byte)(p.State & ~1);
        var open = (p.State & 1) != 0;
        return open != ConstructConfig.Get(p, d, CfgKey.Invert, 0) >= 0.5f ? 1 : 0;
    }

    /// <summary>Server: a key goes down.</summary>
    public static void Press(ref CPart p, ConstructPartDef d, char key)
    {
        var numpad = d.Logic == CLogic.Numpad;
        if (key is >= '0' and <= '9')
        {
            var digit = key - '0';
            if (numpad)
            {
                var v = p.Reading < 0 ? 0 : p.Reading;
                p.Reading = v * 10 + digit <= MaxEntry ? v * 10 + digit : v;
            }
            else if (p.Mem * 10 + digit <= MaxEntry)
            {
                p.Mem = p.Mem * 10 + digit;
                p.Reading++;
            }
            return;
        }
        if (key == 'C')
        {
            if (numpad)
            {
                p.Reading = NoEntry;
                SetValue(ref p, d, 0);
            }
            else
            {
                p.Mem = p.Reading = 0;
                p.State = (byte)(p.State & ~1);
                p.Count = 0;
            }
            return;
        }
        if (key != 'E')
            return;
        if (numpad)
        {
            if (p.Reading < 0)
                return;
            SetValue(ref p, d, p.Reading / MathF.Pow(10, ConstructConfig.Get(p, d, CfgKey.Decimals, 0)));
            p.State |= AcceptedBit;
            p.Reading = NoEntry;
            return;
        }
        var right = p.Reading > 0 && p.Mem == ConstructConfig.Get(p, d, CfgKey.Code, 0);
        p.Mem = p.Reading = 0;
        p.State = (byte)(right ? p.State | 1 : p.State & ~1);
        p.Count = (ushort)(right ? MathF.Round(ConstructConfig.Get(p, d, CfgKey.Hold, 0) * World.TickRate) : 0);
    }

    private static void SetValue(ref CPart p, ConstructPartDef d, float v) => p.Cfg[d.CfgSlot[(int)CfgKey.Value]] = v;
}

public sealed partial class ConstructEdits
{
    /// <summary>Server: a key of a Numpad or Code Lock pressed. Anyone in reach may press one; a lock's code is its defence.</summary>
    public string? KeyPress(ConstructWorld world, Vector3 eye, ushort body, ushort part, char key)
    {
        if (world.Get(body) is not { } c || part >= c.PartCount || c.Loose)
            return "that keypad is gone";
        if (c.Dirty)
            c.Rebuild();
        ref var p = ref c.Parts[part];
        var d = p.D;
        if (d.Logic is not (CLogic.Numpad or CLogic.CodeLock))
            return "that keypad is gone";
        if (p.Broken)
            return $"the {d.Name} is wrecked";
        if (Reachable(eye, ReachPoint(c, part, eye), ConstructBuild.UseReach + AnalogLever.DragReach) is { } far)
            return far;
        LogicInputs.Press(ref p, d, key);
        world.ReadingDirty.Add((c.Id, part));
        c.Wake();
        return null;
    }

    /// <summary>Server: a message set on a sign or the labels on a lamp bank, in hand or by the configure tool: the configure tool's permission rule, no tool needed.</summary>
    public string? PartText(ConstructWorld world, byte player, Vector3 eye, ushort body, ushort part, int slot, string text, string? refusal)
    {
        if (world.Get(body) is not { } c || part >= c.PartCount)
            return "that part is gone";
        if (Contested(c, player) is { } defended)
            return defended;
        if (refusal != null)
            return refusal;
        var d = c.DefOf(part);
        if (slot >= d.TextSlots)
            return $"a {d.Name} takes no text";
        if (Vector3.Distance(eye, ReachPoint(c, part, eye)) > ConstructConfig.Reach)
            return "too far away";
        LogicText.Set(ref c.Parts[part], slot, text);
        world.StructureDirty.Add(c.Id);
        return null;
    }
}
