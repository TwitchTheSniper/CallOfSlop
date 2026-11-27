namespace CallOfSlop;

/// <summary>
/// Letting a part go whole (Karl, 2026-11-27: a ship design's stand-off armour plate, worn or damaged, is let go of its struts' sockets):
/// its links and freeform welds are cut and whatever that leaves apart from the root becomes a body of its own, moving as that part of the
/// body moved (<see cref="SplitOff"/>), so in space it drifts on with the ship's velocity (held station by a ship at rest, on the same orbit
/// as an orbiting one) and in gravity it falls. Nothing is destroyed.
/// </summary>
public sealed partial class ConstructWorld
{
    /// <summary>Lets part <paramref name="part"/> of <paramref name="c"/> go as a body of its own; null when it went, a reason when not.</summary>
    public string? DetachPart(Construct c, int part)
    {
        if (part < 0 || part >= c.PartCount)
            return "nothing there";
        if (c.PartCount < 2)
            return "it is already free";
        if (c.Dirty)
            c.Rebuild();
        var cut = 0;
        var n = 0;
        for (var l = 0; l < c.LinkCount; l++)
        {
            if (c.Links[l].A == part || c.Links[l].B == part)
            {
                cut++;
                continue;
            }
            c.Links[n++] = c.Links[l];
        }
        c.LinkCount = n;
        for (var j = 0; j < c.JointCount; j++)
        {
            ref var jt = ref c.Joints[j];
            if (jt.Freeform && !jt.Cut && (jt.Hinge == part || jt.Child == part))
            {
                jt.Cut = true;
                cut++;
            }
        }
        if (cut == 0)
            return "it isn't fixed to anything";
        using var scratch = Scratch.Begin();
        Span<int> comp = scratch.Take<int>(c.PartCount);
        comp.Fill(-1);
        var root = c.Root >= 0 && c.Root < c.PartCount && c.Root != part ? c.Root : part == 0 ? 1 : 0;
        var comps = 0;
        Flood(c, root, comps++, comp);
        for (var i = 0; i < c.PartCount; i++)
            if (comp[i] == -1)
                Flood(c, i, comps++, comp);
        for (var k = 1; k < comps; k++)
            SplitOff(c, comp, k, live: true);
        Compact(c, comp, 0);
        c.Dirty = true;
        c.Wake();
        StructureDirty.Add(c.Id);
        Events.Add($"construct {c.Id}: part {part} let go ({cut} weld(s) cut, {comps - 1} piece(s) free)");
        return null;
    }
}
