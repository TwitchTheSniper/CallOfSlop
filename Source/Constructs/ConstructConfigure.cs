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
    public string? Configure(ConstructWorld world, byte player, Vector3 eye, ushort body, ReadOnlySpan<ushort> parts, ReadOnlySpan<byte> slots, ReadOnlySpan<float> values, string? refusal)
    {
        ref var h = ref Hands[player];
        if (h.Seated)
            return "get out of the seat first";
        if (h.Tool != ConstructTool.Configure)
            return "take out the configure tool (the tool wheel) to change settings";
        if (world.Get(body) is not { } c)
            return "that construct is gone";
        if (Contested(c, player) is { } defended)
            return defended; // (decision 19's shared rule: survival, an owner alive nearby)
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
        {
            if (s >= d.Config.Length)
                return $"a {d.Name} has no setting {s}";
            if (d.Config[s].CreativeOnly && !InfiniteParts)
                return $"a {d.Name}'s {d.Config[s].Label.ToLowerInvariant()} is set in creative worlds only";
        }
        // In reach: the nearest of the parts (the panel stays open as you step back a little).
        var near = float.MaxValue;
        foreach (var i in parts)
            near = MathF.Min(near, Vector3.Distance(eye, d.IsSizedBox ? ConstructStoreVolume.NearestPoint(c, i, eye) : ReachPoint(c, i, eye))); // (a big box is set from wherever its face is)
        if (near > ConstructConfig.Reach)
            return "too far away";
        var changed = 0;
        foreach (var i in parts)
        {
            ref var p = ref c.Parts[i];
            var any = false;
            Span<float> before = stackalloc float[ConstructConfig.MaxSlots];
            var overlap0 = 0f;
            var litres0 = d.IsTankVolume ? ConstructFluid.Litres(p, d) : 0f;
            var fill0 = d.IsTankVolume ? ConstructConfig.Get(p, d, CfgKey.Fill, 0) : 0f;
            if (d.IsSizedBox)
            {
                overlap0 = ConstructStoreVolume.Overlap(c, i);
                for (var k = 0; k < d.Config.Length; k++)
                    before[k] = p.Cfg[k];
            }
            for (var k = 0; k < slots.Length; k++)
                any |= ConstructConfig.Set(ref p, d, slots[k], values[k]);
            if (!any)
                continue;
            if (d.IsSizedBox && ((d.IsTankVolume && !(d.IsEnvelope && InfiniteParts) ? ConstructFluid.ResizeRefusal(ref p, d, litres0, fill0) : null) ?? ConstructStoreVolume.ResizeRefusal(world, c, i, overlap0)) is { } small)
            {
                for (var k = 0; k < d.Config.Length; k++)
                    p.Cfg[k] = before[k];
                return small;
            }
            if (d.IsVolumeStore && ConstructStoreVolume.ContentsChanged(ref p, d, before) && ConstructStoreVolume.SetContents(world, c, i, limit: true) is { } busy)
            {
                for (var k = 0; k < d.Config.Length; k++)
                    p.Cfg[k] = before[k];
                return busy;
            }
            changed++;
            if (d.Logic is CLogic.Timer or CLogic.Delay)
                p.Count = 0; // (a timer starts its new period afresh)
            if (d.Logic == CLogic.AnalogLever)
                AnalogLever.SettingsChanged(ref p, d); // (on its snap step)
        }
        if (changed > 0 && ConstructConfig.Has(d, CfgKey.Charge))
            c.Dirty = true; // (a gun's numbers follow its powder charge)
        if (changed == 0)
            return null;
        if (d.Role == PartRole.FluidTank)
            ConstructFluid.Refresh(c);
        if (d.IsSizedBox)
            c.SealWait = 2; // (a box being dragged or typed: no penalties until it has stood half a second)
        if (d.HasJoint || d.IsSizedBox)
            c.Dirty = true; // (the joint's limits, speed and strength are rebuilt from the settings; a Storage Volume's load sits in its new box)
        world.StructureDirty.Add(c.Id);
        c.Wake();
        Done.Add($"player {player}: configured {changed} {d.Name} on construct {c.Id}");
        return null;
    }
}
