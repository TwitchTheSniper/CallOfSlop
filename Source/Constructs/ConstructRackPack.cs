using System.Numerics;

namespace CallOfSlop;

/// <summary>One column of a Piece Rack: pieces of one kind across its width in stacks, at one place along its length.</summary>
public struct RackSlab
{
    public ushort Def; // this machine's id of the kind (the wire names it by its place in the store's kinds)
    public ushort Z0Mm; // its near end, mm from the hold's -z end
    public ushort LenMm; // its length along the rack (the piece's), mm
    public byte Lanes, Layers; // stacks across, and pieces a stack holds
}

/// <summary>
/// A Piece Rack's room is its hold's volume, not a count (docs/sandbox/constructs/production.md, "Conveyors"). Pieces pack
/// in columns: a column is one kind, as long as the piece, at a place along the rack; across it stand stacks (lanes) as many
/// as the piece's width lets, each as high as the hold lets. A piece goes on the lowest stack of the first column of its kind
/// with room; with none, a new column opens in the first gap along the rack long enough for it (an emptied column's gap is
/// reused). F takes the top piece of the stack looked at, so nothing else moves. The columns are the store's own state
/// (<see cref="CStore.Slabs"/>): the server sends them with the store; a save keeps only the kinds and the places are packed
/// again on load (<see cref="Ensure"/>). Nothing here allocates once a rack's arrays exist.
/// </summary>
public static class ConstructRackPack
{
    public const int MaxSlabs = 192; // columns a rack holds: a 13 m Long Piece Rack of 6 cm pieces (a wire byte)
    public const int MaxLanes = 32; // stacks across a column (a 2.8 m hold of 7 cm pieces)
    public const float GapX = 0.02f, GapY = 0.005f, GapZ = 0.02f; // between stacks, layers and columns

    /// <summary>The box a rack's pieces pack into, in its own frame (the part kit writes it; older rack files: inside its bounds).</summary>
    public static void Hold(ConstructPartDef rd, out Vector3 lo, out Vector3 hi)
    {
        if (rd.RackHoldMax.X > rd.RackHoldMin.X)
        {
            lo = rd.RackHoldMin;
            hi = rd.RackHoldMax;
            return;
        }
        lo = rd.BoundsMin + new Vector3(0.115f, 0.133f, 0.074f);
        hi = rd.BoundsMax - new Vector3(0.115f, 0.01f, 0.02f);
    }

    /// <summary>How a piece stands in a rack: stacks across and pieces a stack, and its length in mm; false when it is too big.</summary>
    public static bool Fit(ConstructPartDef rd, ConstructPartDef d, out int lanes, out int layers, out int lenMm)
    {
        Hold(rd, out var lo, out var hi);
        var s = d.BoundsMax - d.BoundsMin;
        var w = hi - lo;
        lanes = Math.Min(MaxLanes, (int)((w.X + GapX + 1e-4f) / (s.X + GapX)));
        layers = Math.Min(255, (int)((w.Y + GapY + 1e-4f) / (s.Y + GapY)));
        lenMm = (int)MathF.Ceiling(s.Z * 1000 - 0.01f);
        return lanes > 0 && layers > 0 && lenMm <= (int)(w.Z * 1000 + 0.5f) && s.Z > 0;
    }

    private static int HoldMm(ConstructPartDef rd)
    {
        Hold(rd, out var lo, out var hi);
        return (int)((hi.Z - lo.Z) * 1000 + 0.5f);
    }

    /// <summary>The columns fit the kinds (a loaded save, a copy, an old client's store): else they are packed again from the kinds, in order.</summary>
    public static void Ensure(ConstructPartDef rd, CStore st)
    {
        if (st.SlabSum == st.ItemCount && (st.ItemCount == 0 || st.SlabN > 0))
            return;
        st.SlabN = st.SlabSum = 0;
        for (var k = 0; k < st.KindN; k++)
            if (ConstructParts.Get(st.KindDef![k]) is { } d)
                for (var j = 0; j < st.KindCount![k] && Place(rd, st, d, false, out _, out _) == 0; j++)
                {
                }
        st.SlabSum = st.ItemCount; // (what didn't fit any more, a rack made smaller, stays counted and unplaced)
    }

    /// <summary>
    /// Where a piece would go (<paramref name="put"/>: and puts it): 0, 3 too big for this rack, 4 full. Kinds and the count
    /// are the caller's (<see cref="ConstructConveyor.RackCheck"/>).
    /// </summary>
    public static int Place(ConstructPartDef rd, CStore st, ConstructPartDef d, bool dryRun, out int slab, out int lane)
    {
        slab = lane = -1;
        if (!Fit(rd, d, out var lanes, out var layers, out var lenMm))
            return 3;
        // A column of its kind with room: its lowest stack.
        for (var s = 0; s < st.SlabN; s++)
        {
            ref var sl = ref st.Slabs![s];
            if (sl.Def != d.Id)
                continue;
            var low = -1;
            for (var l = 0; l < sl.Lanes; l++)
                if (st.SlabLanes![s * MaxLanes + l] < sl.Layers && (low < 0 || st.SlabLanes[s * MaxLanes + l] < st.SlabLanes[s * MaxLanes + low]))
                    low = l;
            if (low >= 0)
            {
                slab = s;
                lane = low;
                if (!dryRun)
                {
                    st.SlabLanes![s * MaxLanes + low]++;
                    st.SlabSum++;
                }
                return 0;
            }
        }
        // A new column in the first gap long enough (the columns are kept in order along the rack).
        if (st.SlabN >= MaxSlabs)
            return 4;
        var hold = HoldMm(rd);
        var gap = (int)(GapZ * 1000);
        var at = 0;
        var i = 0;
        for (; i < st.SlabN; i++)
        {
            ref var sl = ref st.Slabs![i];
            if (sl.Z0Mm >= at + lenMm + gap)
                break;
            at = sl.Z0Mm + sl.LenMm + gap;
        }
        if (i == st.SlabN && at + lenMm > hold)
            return 4;
        slab = i;
        lane = 0;
        if (dryRun)
            return 0;
        st.Slabs ??= new RackSlab[MaxSlabs];
        st.SlabLanes ??= new byte[MaxSlabs * MaxLanes];
        Array.Copy(st.Slabs, i, st.Slabs, i + 1, st.SlabN - i);
        Array.Copy(st.SlabLanes, i * MaxLanes, st.SlabLanes, (i + 1) * MaxLanes, (st.SlabN - i) * MaxLanes);
        st.Slabs[i] = new RackSlab { Def = d.Id, Z0Mm = (ushort)at, LenMm = (ushort)lenMm, Lanes = (byte)lanes, Layers = (byte)layers };
        Array.Clear(st.SlabLanes, i * MaxLanes, MaxLanes);
        st.SlabLanes[i * MaxLanes] = 1;
        st.SlabN++;
        st.SlabSum++;
        return 0;
    }

    /// <summary>The top piece off a stack; an emptied column goes, and the columns after it stay where they are.</summary>
    public static ushort Take(CStore st, int slab, int lane)
    {
        if ((uint)slab >= (uint)st.SlabN || (uint)lane >= st.Slabs![slab].Lanes || st.SlabLanes![slab * MaxLanes + lane] == 0)
            return ConstructParts.None;
        var def = st.Slabs[slab].Def;
        st.SlabLanes[slab * MaxLanes + lane]--;
        st.SlabSum--;
        var any = false;
        for (var l = 0; l < st.Slabs[slab].Lanes && !any; l++)
            any = st.SlabLanes[slab * MaxLanes + l] > 0;
        if (!any)
        {
            st.SlabN--;
            Array.Copy(st.Slabs, slab + 1, st.Slabs, slab, st.SlabN - slab);
            Array.Copy(st.SlabLanes, (slab + 1) * MaxLanes, st.SlabLanes, slab * MaxLanes, (st.SlabN - slab) * MaxLanes);
        }
        return def;
    }

    /// <summary>The stack of a kind to take from when none is aimed at: its last column's highest stack.</summary>
    public static bool LastOf(CStore st, ushort def, out int slab, out int lane)
    {
        slab = lane = -1;
        for (var s = st.SlabN - 1; s >= 0; s--)
        {
            if (st.Slabs![s].Def != def)
                continue;
            for (var l = 0; l < st.Slabs[s].Lanes; l++)
                if (st.SlabLanes![s * MaxLanes + l] > 0 && (lane < 0 || st.SlabLanes[s * MaxLanes + l] >= st.SlabLanes[s * MaxLanes + lane]))
                    lane = l;
            if (lane >= 0)
            {
                slab = s;
                return true;
            }
        }
        return false;
    }

    /// <summary>Where piece <paramref name="layer"/> of a stack has its own origin, in the rack part's frame (unturned).</summary>
    public static Vector3 Origin(ConstructPartDef rd, in RackSlab sl, ConstructPartDef d, int lane, int layer)
    {
        Hold(rd, out var lo, out var hi);
        var s = d.BoundsMax - d.BoundsMin;
        var used = sl.Lanes * s.X + (sl.Lanes - 1) * GapX;
        var x = lo.X + (hi.X - lo.X - used) * 0.5f + lane * (s.X + GapX);
        return new Vector3(x, lo.Y + layer * (s.Y + GapY), lo.Z + sl.Z0Mm * 0.001f) - d.BoundsMin;
    }

    /// <summary>A stack as one box (rack frame): its pieces from the bed to the top one.</summary>
    public static void StackBox(ConstructPartDef rd, in RackSlab sl, ConstructPartDef d, int lane, int count, out Vector3 min, out Vector3 max)
    {
        var o = Origin(rd, sl, d, lane, 0);
        min = o + d.BoundsMin;
        max = Origin(rd, sl, d, lane, count - 1) + d.BoundsMax;
    }

    /// <summary>A column as one box (rack frame), as high as its highest stack: the far proxy, and the ray's first test.</summary>
    public static void SlabBox(ConstructPartDef rd, CStore st, int s, ConstructPartDef d, out Vector3 min, out Vector3 max)
    {
        ref var sl = ref st.Slabs![s];
        var top = 0;
        int first = -1, last = -1;
        for (var l = 0; l < sl.Lanes; l++)
        {
            int n = st.SlabLanes![s * MaxLanes + l];
            if (n == 0)
                continue;
            top = Math.Max(top, n);
            if (first < 0)
                first = l;
            last = l;
        }
        min = Origin(rd, sl, d, Math.Max(first, 0), 0) + d.BoundsMin;
        max = Origin(rd, sl, d, Math.Max(last, 0), Math.Max(top - 1, 0)) + d.BoundsMax;
    }

    /// <summary>
    /// The stack a ray (rack frame) meets first before <paramref name="best"/>: its column and lane, and the distance in
    /// <paramref name="best"/>. A column's box first, then its stacks: a full rack costs its columns, not its pieces.
    /// </summary>
    public static bool Ray(ConstructPartDef rd, CStore st, Vector3 o, Vector3 dir, ref float best, out int slab, out int lane)
    {
        slab = lane = -1;
        for (var s = 0; s < st.SlabN; s++)
        {
            if (ConstructParts.Get(st.Slabs![s].Def) is not { } d)
                continue;
            SlabBox(rd, st, s, d, out var smin, out var smax);
            if (Geometry.RayAabb(smin, smax, o, dir, best) is not { } sd || sd >= best)
                continue;
            for (var l = 0; l < st.Slabs[s].Lanes; l++)
            {
                int n = st.SlabLanes![s * MaxLanes + l];
                if (n == 0)
                    continue;
                StackBox(rd, st.Slabs[s], d, l, n, out var mn, out var mx);
                if (Geometry.RayAabb(mn, mx, o, dir, best) is { } hit && hit < best)
                {
                    best = hit;
                    slab = s;
                    lane = l;
                }
            }
        }
        return slab >= 0;
    }

    /// <summary>A ray (rack frame) into a rack's hold box before <paramref name="best"/> (a look into a rack that holds something lands on it).</summary>
    public static bool HoldRay(ConstructPartDef rd, Vector3 o, Vector3 dir, ref float best)
    {
        Hold(rd, out var lo, out var hi);
        if (Geometry.RayAabb(lo, hi, o, dir, best) is { } d && d < best)
        {
            best = d;
            return true;
        }
        return false;
    }

    /// <summary>The stack of a rack whose box holds a point (rack frame), 3 cm about: the physics gun's beam names it so.</summary>
    public static bool At(ConstructPartDef rd, CStore st, Vector3 point, out int slab, out int lane)
    {
        slab = lane = -1;
        var near = 0.03f;
        for (var s = 0; s < st.SlabN; s++)
        {
            if (ConstructParts.Get(st.Slabs![s].Def) is not { } d)
                continue;
            for (var l = 0; l < st.Slabs[s].Lanes; l++)
            {
                int n = st.SlabLanes![s * MaxLanes + l];
                if (n == 0)
                    continue;
                StackBox(rd, st.Slabs[s], d, l, n, out var mn, out var mx);
                var gap = Vector3.Distance(point, Vector3.Clamp(point, mn, mx));
                if (gap <= near)
                {
                    near = gap;
                    slab = s;
                    lane = l;
                }
            }
        }
        return slab >= 0;
    }

    /// <summary>The share of the hold its pieces fill (their boxes' volume over the hold's), 0 to 1.</summary>
    public static float Fill(ConstructPartDef rd, CStore st)
    {
        Hold(rd, out var lo, out var hi);
        var w = hi - lo;
        var hold = w.X * w.Y * w.Z;
        var v = 0f;
        for (var k = 0; k < st.KindN; k++)
            if (ConstructParts.Get(st.KindDef![k]) is { } d)
            {
                var s = d.BoundsMax - d.BoundsMin;
                v += s.X * s.Y * s.Z * st.KindCount![k];
            }
        return hold > 0 ? Math.Clamp(v / hold, 0, 1) : 0;
    }

    /// <summary>A hash of every piece's place (def key, column, lane, layer count): host and client compare it.</summary>
    public static ulong PlacesHash(CStore st)
    {
        var h = 1469598103934665603UL;
        for (var s = 0; s < st.SlabN; s++)
        {
            ref var sl = ref st.Slabs![s];
            var key = ConstructParts.Get(sl.Def) is { IsExtrusion: true } ex ? (ulong)ex.ExKey.GetHashCode() : sl.Def; // (an extrusion's id is this machine's own)
            h = (h ^ key ^ (ulong)sl.Z0Mm << 20 ^ (ulong)sl.Lanes << 40) * 1099511628211UL;
            for (var l = 0; l < sl.Lanes; l++)
                h = (h ^ st.SlabLanes![s * MaxLanes + l]) * 1099511628211UL;
        }
        return h;
    }

    // ---- The wire: the columns after the kinds ------------------------------------------------------------------

    public static void Write(PacketWriter w, CStore st)
    {
        w.U8((byte)st.SlabN);
        for (var s = 0; s < st.SlabN; s++)
        {
            ref var sl = ref st.Slabs![s];
            w.U8((byte)Math.Max(0, st.KindIndex(sl.Def))).U16(sl.Z0Mm).U8(sl.Lanes);
            for (var l = 0; l < sl.Lanes; l++)
                w.U8(st.SlabLanes![s * MaxLanes + l]);
        }
    }

    /// <summary>Reads what <see cref="Write"/> wrote (after the kinds); a client never places, so a column's height limit is left open.</summary>
    public static void Read(ref PacketReader r, CStore st)
    {
        int n = r.U8();
        if (n > MaxSlabs)
            throw new MalformedPacketException($"a rack of {n} columns");
        st.Slabs ??= new RackSlab[MaxSlabs];
        st.SlabLanes ??= new byte[MaxSlabs * MaxLanes];
        var sum = 0;
        for (var s = 0; s < n; s++)
        {
            int kind = r.U8();
            var z0 = r.U16();
            int lanes = r.U8();
            if (kind >= st.KindN || lanes > MaxLanes)
                throw new MalformedPacketException($"rack column {s}: kind {kind} of {st.KindN}, {lanes} stacks");
            var def = st.KindDef![kind];
            var lenMm = ConstructParts.Get(def) is { } d ? (int)MathF.Ceiling((d.BoundsMax.Z - d.BoundsMin.Z) * 1000 - 0.01f) : 0;
            const int layers = 255;
            st.Slabs[s] = new RackSlab { Def = def, Z0Mm = z0, LenMm = (ushort)lenMm, Lanes = (byte)lanes, Layers = (byte)layers };
            Array.Clear(st.SlabLanes, s * MaxLanes, MaxLanes);
            for (var l = 0; l < lanes; l++)
                sum += st.SlabLanes[s * MaxLanes + l] = r.U8();
        }
        st.SlabN = n;
        st.SlabSum = sum;
    }

    /// <summary>A copy's columns (a travelling body, a snapshot).</summary>
    public static void Copy(CStore from, CStore to)
    {
        to.SlabN = to.SlabSum = 0;
        if (from.SlabN == 0)
            return;
        to.Slabs ??= new RackSlab[MaxSlabs];
        to.SlabLanes ??= new byte[MaxSlabs * MaxLanes];
        Array.Copy(from.Slabs!, to.Slabs, from.SlabN);
        Array.Copy(from.SlabLanes!, to.SlabLanes, from.SlabN * MaxLanes);
        to.SlabN = from.SlabN;
        to.SlabSum = from.SlabSum;
    }
}
