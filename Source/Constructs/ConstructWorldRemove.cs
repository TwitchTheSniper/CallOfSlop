namespace CallOfSlop;

/// <summary>
/// The remover tool's batch removal (board item dev-sandbox-remover-tool-rt01; ConstructRemover.cs): many parts out of one
/// construct with a single structure rebuild, where <see cref="ConstructWorld.RemovePart"/> takes one and compacts each time.
/// </summary>
public sealed partial class ConstructWorld
{
    /// <summary>
    /// Takes every part marked in <paramref name="doomed"/> (by part index) out of <paramref name="c"/> at once, destroying them
    /// outright (the caller gives nothing back): a container spills what it holds, the links and wires on a part go with it,
    /// and every piece no longer joined to the root splits off as a wreck of its own, exactly as <see cref="RemovePart"/> does for
    /// one part. A construct with nothing left is removed. Returns how many parts were destroyed.
    /// </summary>
    public int RemoveParts(Construct c, ReadOnlySpan<bool> doomed)
    {
        var n = 0;
        for (var i = 0; i < c.PartCount && i < doomed.Length; i++)
            n += doomed[i] ? 1 : 0;
        if (n == 0)
            return 0;
        if (c.Dirty)
            c.Rebuild(); // (the merges as they stand)
        for (var i = 0; i < c.PartCount && i < doomed.Length; i++)
            if (doomed[i] && c.DefOf(i).IsReceiverGun && c.MergeOf[i] != 0)
                return RemovePartsOneByOne(c, doomed, n); // (a merged receiver's cell re-merges the rest: ConstructMerge.Settle, one at a time)
        if (n >= c.PartCount)
        {
            SpillAll(c);
            Remove(c);
            return n;
        }
        for (var i = 0; i < c.PartCount && i < doomed.Length; i++)
            if (doomed[i] && c.Parts[i].Store != 0)
                Spill(c, i); // (a container that goes spills what it holds)
        var keepDriver = c.Driver;
        Span<int> comp = stackalloc int[Construct.MaxParts];
        for (var i = 0; i < c.PartCount; i++)
            comp[i] = i < doomed.Length && doomed[i] ? -2 : -1;
        var root = c.Root >= 0 && c.Root < c.PartCount && comp[c.Root] != -2 ? c.Root : -1;
        if (root < 0)
        {
            for (var i = 0; i < c.PartCount && root < 0; i++)
                if (comp[i] != -2 && c.Parts[i].D.Role is PartRole.Seat or PartRole.Foundation)
                    root = i;
            for (var i = 0; i < c.PartCount && root < 0; i++)
                if (comp[i] != -2)
                    root = i;
        }
        var comps = 0;
        Flood(c, root, comps++, comp);
        for (var i = 0; i < c.PartCount; i++)
            if (comp[i] == -1)
                Flood(c, i, comps++, comp);
        for (var k = 1; k < comps; k++)
            SplitOff(c, comp, k);
        Compact(c, comp, 0);
        c.Dirty = true;
        c.Wake();
        StructureDirty.Add(c.Id);
        if (c.PartCount == 0)
            Remove(c);
        else if (keepDriver != 255 && c.Driver == 255)
            Events.Add($"driver {keepDriver} thrown out");
        Events.Add($"construct {c.Id}: {n} part(s) deleted");
        return n;
    }

    /// <summary>Whole-body deletion: every container spills first (Remove frees a store without spilling).</summary>
    public void SpillAll(Construct c)
    {
        for (var i = 0; i < c.PartCount; i++)
            if (c.Parts[i].Store != 0)
                Spill(c, i);
    }

    /// <summary>
    /// The slow way, for a batch with a merged receiver in it: one <see cref="RemovePart"/> at a time, finding what is left by
    /// the parts' stable tags (indices move as pieces split off and merges settle). Pieces cut off meanwhile are searched too.
    /// </summary>
    private int RemovePartsOneByOne(Construct c, ReadOnlySpan<bool> doomed, int n)
    {
        var tags = new HashSet<ushort>();
        for (var i = 0; i < c.PartCount && i < doomed.Length; i++)
            if (doomed[i])
                tags.Add(c.Parts[i].Tag);
        var known = new HashSet<ushort>();
        foreach (var b in Bodies)
            if (b != c)
                known.Add(b.Id);
        var done = 0;
        for (var guard = 0; guard < n * 2 + 8 && tags.Count > 0; guard++)
        {
            Construct? at = null;
            var index = -1;
            for (var bi = 0; bi < Bodies.Count && index < 0; bi++)
            {
                var body = Bodies[bi];
                if (body != c && known.Contains(body.Id))
                    continue; // (only the construct and the pieces cut off from it meanwhile)
                for (var i = 0; i < body.PartCount && index < 0; i++)
                    if (tags.Contains(body.Parts[i].Tag))
                    {
                        at = body;
                        index = i;
                    }
            }
            if (at == null)
                break;
            tags.Remove(at.Parts[index].Tag);
            RemovePart(at, index);
            done++;
        }
        return done;
    }
}
