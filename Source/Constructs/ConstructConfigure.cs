using System.Numerics;

namespace CallOfSlop;

/// <summary>The configure tool on the server (docs/sandbox/CONSTRUCTS.md, "The configure tool"): a part's settings changed.</summary>
public sealed partial class ConstructEdits
{
    /// <summary>The most parts one Configure request sets at once (Apply to all selected).</summary>
    public const int MaxConfigParts = 32;

    /// <summary>
    /// Server: the configure tool sets <paramref name="slots"/> to <paramref name="values"/> (each clamped to its range) on
    /// every part listed, all of one kind on construct <paramref name="body"/>. <paramref name="refusal"/> is the game's
    /// permission check (decision 19: someone else's construct, defended or not), already worked out. Null, or why not.
    /// </summary>
    public string? Configure(ConstructWorld world, byte player, Vector3 eye, ushort body, ReadOnlySpan<byte> parts, ReadOnlySpan<byte> slots, ReadOnlySpan<float> values, string? refusal)
    {
        ref var h = ref Hands[player];
        if (h.Seated)
            return "get out of the seat first";
        if (h.Tool != ConstructTool.Configure)
            return "take out the configure tool (the tool wheel) to change settings";
        if (world.Get(body) is not { } c)
            return "that construct is gone";
        if (refusal != null)
            return refusal;
        if (parts.Length == 0 || parts.Length > MaxConfigParts || slots.Length != values.Length || slots.Length > ConstructConfig.MaxSlots)
            return "nothing to set";
        if (parts[0] >= c.PartCount)
            return "that part is gone";
        var def = c.Parts[parts[0]].Def;
        var d = c.DefOf(parts[0]);
        if (!d.Configurable)
            return $"a {d.Name} has no settings";
        foreach (var i in parts)
            if (i >= c.PartCount || c.Parts[i].Def != def)
                return "those parts aren't all the same kind";
        foreach (var s in slots)
            if (s >= d.Config.Length)
                return $"a {d.Name} has no setting {s}";
        // In reach: the nearest of the parts (the panel stays open as you step back a little).
        var near = float.MaxValue;
        foreach (var i in parts)
            near = MathF.Min(near, Vector3.Distance(eye, ConstructRender.PartCentre(c, i)));
        if (near > ConstructConfig.Reach)
            return "too far away";
        var changed = 0;
        foreach (var i in parts)
        {
            ref var p = ref c.Parts[i];
            var any = false;
            for (var k = 0; k < slots.Length; k++)
                any |= ConstructConfig.Set(ref p, d, slots[k], values[k]);
            if (!any)
                continue;
            changed++;
            if (d.Logic is CLogic.Timer or CLogic.Delay)
                p.Count = 0; // (a timer starts its new period afresh)
        }
        if (changed == 0)
            return null;
        if (d.HasJoint)
            c.Dirty = true; // (the joint's limits, speed and strength are rebuilt from the settings)
        world.StructureDirty.Add(c.Id);
        c.Wake();
        Done.Add($"player {player}: configured {changed} {d.Name} on construct {c.Id}");
        return null;
    }
}
