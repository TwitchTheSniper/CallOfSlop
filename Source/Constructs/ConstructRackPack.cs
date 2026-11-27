using System.Numerics;

namespace CallOfSlop;

/// <summary>One column of a Piece Rack: pieces of one kind across its width in stacks, at one place along its length.</summary>
public struct RackSlab
{
    public ushort Def; // this machine's id of the kind (the wire names it by its place in the store's kinds)
    public ushort Z0Mm; // its near end, mm from the hold's -z end
    public ushort LenMm; // its length along the rack (the piece's), mm
    public byte Lanes, Layers; // stacks across, and pieces a stack holds
    // A Pad Tile pad's column (ConstructRackPack.TilePad): its -x edge, mm from the hold's -x side, and its width across, mm. Columns there
    // lie side by side as well as one after another, so a 9.5 m deck on a 20 m pad leaves room beside it (Sean, 2026-11-27). Other racks
    // centre their columns across the hold and leave both 0.
    public ushort X0Mm, WidMm;
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
        if (FitAs(rd, d.BoundsMax - d.BoundsMin, out lanes, out layers, out lenMm))
            return true;
        if (!Turned(rd, d))
            return false;
        var s = d.BoundsMax - d.BoundsMin;
        return FitAs(rd, new Vector3(s.Z, s.Y, s.X), out lanes, out layers, out lenMm);
    }

    /// <summary>
    /// Does a piece lie on Pad Tile pad <paramref name="rd"/> turned a quarter (its own x along the pad's z)? Only when it doesn't fit the
    /// pad as it is and does so turned: a 9.5 m wall (its length along its x) rolled out of a small line's oven onto Zero Day Works II's
    /// 6 x 20 m pad beside the main station, which a pad that takes pieces only along its z could never hold, while the 17 m walls (their
    /// length along z) need it as it is (Sean, 2026-11-27). Both peers decide it from the two defs alone: nothing is saved or sent.
    /// Everything that places or draws a rack's piece turns it by <see cref="Turn"/> and reads its box from <see cref="Lo"/> and <see cref="Hi"/>.
    /// </summary>
    public static bool Turned(ConstructPartDef rd, ConstructPartDef d)
    {
        if (!rd.BlockSeed || rd.BlockCell <= 0)
            return false;
        var s = d.BoundsMax - d.BoundsMin;
        return MathF.Abs(s.X - s.Z) > 1e-3f && !FitAs(rd, s, out _, out _, out _) && FitAs(rd, new Vector3(s.Z, s.Y, s.X), out _, out _, out _);
    }

    /// <summary>A quarter turn about the rack's up (its x to the rack's -z, its z to the rack's x) for a <see cref="Turned"/> piece, else none.</summary>
    public static Quaternion Turn(ConstructPartDef rd, ConstructPartDef d) => Turned(rd, d) ? QuarterTurn : Quaternion.Identity;

    private static readonly Quaternion QuarterTurn = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI * 0.5f);

    /// <summary>The piece's box as it lies in the rack (rack frame, about its own origin): its bounds, turned when it lies <see cref="Turned"/>.</summary>
    public static Vector3 Lo(ConstructPartDef rd, ConstructPartDef d) => Turned(rd, d) ? new Vector3(d.BoundsMin.Z, d.BoundsMin.Y, -d.BoundsMax.X) : d.BoundsMin;

    /// <inheritdoc cref="Lo"/>
    public static Vector3 Hi(ConstructPartDef rd, ConstructPartDef d) => Turned(rd, d) ? new Vector3(d.BoundsMax.Z, d.BoundsMax.Y, -d.BoundsMin.X) : d.BoundsMax;

    private static bool FitAs(ConstructPartDef rd, Vector3 s, out int lanes, out int layers, out int lenMm)
    {
        Hold(rd, out var lo, out var hi);
        var w = hi - lo;
        lanes = Math.Min(MaxLanes, (int)((w.X + GapX + 1e-4f) / (s.X + GapX)));
        layers = Math.Min(255, (int)((w.Y + GapY + 1e-4f) / (s.Y + GapY)));
        lenMm = (int)MathF.Ceiling(s.Z * 1000 - 0.01f);
        if (rd.BlockSeed && rd.BlockCell > 0)
        {
            // A Pad Tile pad lies flat and open: one piece wider than its hold may use it to its tiles' edges (a 4 m pad's hold is 3.66 m),
            // and with no masts on it one piece of any height lies there alone (Sean, 2026-11-26: Works II's keel Hull Sections, 3.83 m
            // wide and 2 m tall, sat on the Laser Cutter's bed for good, as its lay-by pad could never hold them).
            if (lanes == 0 && s.X <= MathF.Ceiling(w.X / rd.BlockCell - 0.01f) * rd.BlockCell + 1e-4f)
                lanes = 1;
            if (layers == 0 && w.Y < ConstructPads.BaseStack + ConstructPads.PerMast * 0.5f)
                layers = 1;
        }
        return lanes > 0 && layers > 0 && lenMm <= (int)(w.Z * 1000 + 0.5f) && s.Z > 0;
    }

    /// <summary>The hold's size as a key (never 0): columns packed for one size are packed again for another.</summary>
    public static int Shape(ConstructPartDef rd)
    {
        Hold(rd, out var lo, out var hi);
        var w = hi - lo;
        return HashCode.Combine((int)(w.X * 1000 + 0.5f), (int)(w.Y * 1000 + 0.5f), (int)(w.Z * 1000 + 0.5f)) | 1;
    }

    private static int HoldMm(ConstructPartDef rd)
    {
        Hold(rd, out var lo, out var hi);
        return (int)((hi.Z - lo.Z) * 1000 + 0.5f);
    }

    /// <summary>The columns fit the kinds (a loaded save, a copy, an old client's store): else they are packed again from the kinds, in order.</summary>
    public static void Ensure(ConstructPartDef rd, CStore st)
    {
        var shape = Shape(rd);
        if (st.SlabSum == st.ItemCount && (st.ItemCount == 0 || st.SlabN > 0) && (st.PackedFor == 0 || st.PackedFor == shape))
            return;
        st.PackedFor = shape; // (a Pad Tile's pad grown, shrunk or given another owner: the columns are packed again for its hold)
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
    public static int Place(ConstructPartDef rd, CStore st, ConstructPartDef d, bool dryRun, out int slab, out int lane) =>
        Place(rd, st, d, dryRun, out slab, out lane, out _);

    private static int Place(ConstructPartDef rd, CStore st, ConstructPartDef d, bool dryRun, out int slab, out int lane, out RackSlab made)
    {
        slab = lane = -1;
        made = default;
        if (!Fit(rd, d, out var lanes, out var layers, out var lenMm))
            return 3;
        if (!dryRun)
        {
            var shape = Shape(rd);
            if (st.PackedFor != 0 && st.PackedFor != shape)
                Ensure(rd, st); // (columns packed for another hold size: packed again first)
            st.PackedFor = shape;
        }
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
        if (TilePad(rd))
            return PadPlace(rd, st, d, dryRun, lanes, layers, lenMm, out slab, out lane, out made);
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
        {
            // No gap long enough, but the gaps together are (pieces taken out of the middle, as a station's rovers take them off a pad):
            // the columns close up along the rack, in order, and it goes on the end.
            var used = 0;
            for (var k = 0; k < st.SlabN; k++)
                used += st.Slabs![k].LenMm + gap;
            if (used + lenMm > hold)
                return 4;
            if (dryRun)
            {
                slab = st.SlabN;
                return 0;
            }
            at = 0;
            for (var k = 0; k < st.SlabN; k++)
            {
                st.Slabs![k].Z0Mm = (ushort)at;
                at += st.Slabs[k].LenMm + gap;
            }
            i = st.SlabN;
        }
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

    /// <summary>A column's place as one number (its near end and its place across): what tells two columns of a kind apart.</summary>
    public static uint PlaceKey(in RackSlab sl) => sl.Z0Mm | (uint)sl.X0Mm << 16;

    /// <summary>Is <paramref name="rd"/> a Pad Tile pad's hold (flat and open)? Its columns lie side by side as well as along it.</summary>
    public static bool TilePad(ConstructPartDef rd) => rd.BlockSeed && rd.BlockCell > 0;

    /// <summary>A column's footprint on a pad, mm from the hold's -x side and -z end.</summary>
    private struct Foot
    {
        public int X0, Z0, W, L;
    }

    private const int MaxRelay = 24; // columns on a pad past which a full pad isn't laid out afresh (PadPlace)

    [ThreadStatic] private static Foot[]? _feet;
    [ThreadStatic] private static int[]? _order;

    /// <summary>
    /// A new column on a Pad Tile pad (Sean, 2026-11-27: a baked deck on Zero Day Works II's main station pad took a 20 m row of the pad's
    /// length for its 9.5 m width, centred, so a hull piece the station wanted next could never come on beside it and the line stood behind
    /// it for good). The column goes in the first free spot by its near end, then across (the hold's -z, -x corner first; the drawing
    /// mirrors it from the feed, ConstructPads.FeedFlip), as wide as its stacks need and no wider. With no spot free, every column on the
    /// pad is laid out again, largest first, with the new one among them; nothing moves unless they all fit. A kind's later pieces fill its
    /// columns' stacks first (Place), then open columns of their own here.
    /// </summary>
    private static int PadPlace(ConstructPartDef rd, CStore st, ConstructPartDef d, bool dryRun, int lanesFit, int layers, int lenMm, out int slab, out int lane, out RackSlab made)
    {
        slab = lane = -1;
        made = default;
        Hold(rd, out var lo, out var hi);
        int holdW = (int)((hi.X - lo.X) * 1000 + 0.5f), holdL = (int)((hi.Z - lo.Z) * 1000 + 0.5f);
        var pw = (int)MathF.Ceiling((Hi(rd, d).X - Lo(rd, d).X) * 1000 - 0.01f);
        // (a new column's stacks across cover at most half the pad, one stack of a piece wider than that: the rest of the width is left
        // for any kind, so a second deck of the same kind that may never come doesn't keep a whole row of the pad from a hull piece)
        lanesFit = Math.Clamp((holdW / 2 + (int)(GapX * 1000)) / Math.Max(1, pw + (int)(GapX * 1000)), 1, lanesFit);
        var n = st.SlabN;
        var feet = _feet ??= new Foot[MaxSlabs + 1];
        for (var s = 0; s < n; s++)
        {
            ref var sl = ref st.Slabs![s];
            feet[s] = new Foot { X0 = sl.X0Mm, Z0 = sl.Z0Mm, W = Math.Max(1, (int)sl.WidMm), L = sl.LenMm };
        }
        var moved = false;
        if (!Spot(feet.AsSpan(0, n), holdW, holdL, pw, lanesFit, lenMm, out var x0, out var z0, out var lanes, out var wid))
        {
            // No spot as they lie: all again, largest footprint first (the new one with them), each in its first free spot. Only for a
            // pad of a few columns (big pieces): the layout costs columns to the fourth, and a pad of small pieces has room by its gaps.
            if (n > MaxRelay)
                return 4;
            var order = _order ??= new int[MaxSlabs + 1];
            for (var s = 0; s <= n; s++)
                order[s] = s;
            var newArea = (long)Math.Min(pw, holdW) * lenMm;
            for (var a = 1; a <= n; a++) // (insertion sort, stable: a handful of columns on a pad)
                for (var b = a; b > 0 && Area(order[b], feet, n, newArea) > Area(order[b - 1], feet, n, newArea); b--)
                    (order[b], order[b - 1]) = (order[b - 1], order[b]);
            Span<Foot> laid = stackalloc Foot[n + 1];
            for (var k = 0; k <= n; k++)
            {
                var s = order[k];
                bool ok;
                if (s == n)
                {
                    ok = Spot(laid[..k], holdW, holdL, pw, lanesFit, lenMm, out x0, out z0, out lanes, out wid);
                    laid[k] = new Foot { X0 = x0, Z0 = z0, W = wid, L = lenMm };
                }
                else
                {
                    var f = feet[s];
                    ok = Spot(laid[..k], holdW, holdL, f.W, 1, f.L, out var fx, out var fz, out _, out _); // (as wide as it is: one stack of its width)
                    laid[k] = new Foot { X0 = fx, Z0 = fz, W = f.W, L = f.L };
                }
                if (!ok)
                    return 4;
            }
            if (!dryRun)
                for (var k = 0; k <= n; k++)
                    if (order[k] < n)
                    {
                        st.Slabs![order[k]].X0Mm = (ushort)laid[k].X0;
                        st.Slabs[order[k]].Z0Mm = (ushort)laid[k].Z0;
                    }
            moved = true;
        }
        made = new RackSlab { Def = d.Id, X0Mm = (ushort)x0, Z0Mm = (ushort)z0, WidMm = (ushort)wid, LenMm = (ushort)lenMm, Lanes = (byte)lanes, Layers = (byte)layers };
        if (dryRun)
        {
            slab = st.SlabN;
            return 0;
        }
        if (moved)
            SortColumns(st);
        // (kept in order by near end, then across: LastOf, NthLast and the drawing read them so)
        var i = 0;
        while (i < st.SlabN && (st.Slabs![i].Z0Mm < z0 || st.Slabs[i].Z0Mm == z0 && st.Slabs[i].X0Mm < x0))
            i++;
        st.Slabs ??= new RackSlab[MaxSlabs];
        st.SlabLanes ??= new byte[MaxSlabs * MaxLanes];
        Array.Copy(st.Slabs, i, st.Slabs, i + 1, st.SlabN - i);
        Array.Copy(st.SlabLanes, i * MaxLanes, st.SlabLanes, (i + 1) * MaxLanes, (st.SlabN - i) * MaxLanes);
        st.Slabs[i] = made;
        Array.Clear(st.SlabLanes, i * MaxLanes, MaxLanes);
        st.SlabLanes[i * MaxLanes] = 1;
        st.SlabN++;
        st.SlabSum++;
        slab = i;
        lane = 0;
        return 0;

        static long Area(int s, Foot[] feet, int n, long newArea) => s == n ? newArea : (long)feet[s].W * feet[s].L;
    }

    /// <summary>
    /// The first free spot (least near end, then least across) for a column <paramref name="len"/> mm long of stacks
    /// <paramref name="pw"/> mm wide (up to <paramref name="lanesFit"/>, as many as the room beside lets) among <paramref name="feet"/>,
    /// <see cref="GapX"/>/<see cref="GapZ"/> apart. A piece wider than the hold (a lone one to the tiles' edges, <see cref="Fit"/>) takes
    /// the whole width. Allocates nothing.
    /// </summary>
    private static bool Spot(ReadOnlySpan<Foot> feet, int holdW, int holdL, int pw, int lanesFit, int len, out int x0, out int z0, out int lanes, out int wid)
    {
        x0 = z0 = lanes = wid = 0;
        int gx = (int)(GapX * 1000), gz = (int)(GapZ * 1000);
        var wide = pw > holdW;
        var found = false;
        for (var zi = -1; zi < feet.Length; zi++)
        {
            var z = zi < 0 ? 0 : feet[zi].Z0 + feet[zi].L + gz;
            if (z + len > holdL || found && z > z0)
                continue;
            for (var xi = -1; xi < feet.Length; xi++)
            {
                var x = xi < 0 ? 0 : feet[xi].X0 + feet[xi].W + gx;
                if (found && z == z0 && x >= x0 || x >= holdW || wide && x > 0)
                    continue;
                var lim = holdW;
                var free = true;
                foreach (ref readonly var f in feet)
                {
                    if (z >= f.Z0 + f.L + gz || f.Z0 >= z + len + gz)
                        continue; // (not beside it along the pad)
                    if (f.X0 <= x)
                    {
                        if (f.X0 + f.W + gx > x)
                        {
                            free = false;
                            break;
                        }
                    }
                    else
                        lim = Math.Min(lim, f.X0 - gx);
                }
                if (!free)
                    continue;
                int n, w;
                if (wide)
                {
                    if (lim < holdW)
                        continue;
                    (n, w) = (1, holdW);
                }
                else
                {
                    n = Math.Min(lanesFit, (lim - x + gx) / (pw + gx));
                    if (n < 1)
                        continue;
                    w = n * pw + (n - 1) * gx;
                }
                (x0, z0, lanes, wid, found) = (x, z, n, w, true);
            }
        }
        return found;
    }

    /// <summary>A pad's columns in order again (near end, then across) after they were laid out afresh, their stacks with them.</summary>
    private static void SortColumns(CStore st)
    {
        Span<byte> row = stackalloc byte[MaxLanes];
        for (var a = 1; a < st.SlabN; a++)
            for (var b = a; b > 0; b--)
            {
                ref var p = ref st.Slabs![b - 1];
                ref var q = ref st.Slabs[b];
                if (p.Z0Mm < q.Z0Mm || p.Z0Mm == q.Z0Mm && p.X0Mm <= q.X0Mm)
                    break;
                (p, q) = (q, p);
                var lanes = st.SlabLanes.AsSpan();
                lanes.Slice((b - 1) * MaxLanes, MaxLanes).CopyTo(row);
                lanes.Slice(b * MaxLanes, MaxLanes).CopyTo(lanes.Slice((b - 1) * MaxLanes, MaxLanes));
                row.CopyTo(lanes.Slice(b * MaxLanes, MaxLanes));
            }
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

    /// <summary>
    /// Where the next piece of a kind would go (its own origin, the rack part's frame, turned by Turn), as <see cref="Place"/> would put it now:
    /// on its kind's lowest stack with room, else at the start of a new column. False: it wouldn't go in.
    /// </summary>
    public static bool NextOrigin(ConstructPartDef rd, CStore st, ConstructPartDef d, out Vector3 origin)
    {
        origin = default;
        if (Place(rd, st, d, true, out var slab, out var lane, out var made) != 0 || !Fit(rd, d, out var lanes, out var layers, out var lenMm))
            return false;
        if (slab < st.SlabN && st.Slabs![slab].Def == d.Id && st.SlabLanes![slab * MaxLanes + lane] < st.Slabs[slab].Layers)
        {
            origin = Origin(rd, st.Slabs[slab], d, lane, st.SlabLanes[slab * MaxLanes + lane]);
            return true;
        }
        if (TilePad(rd))
        {
            origin = Origin(rd, made, d, 0, 0); // (its spot on the pad, as PadPlace found it)
            return true;
        }
        var gap = (int)(GapZ * 1000);
        var at = 0;
        for (var i = 0; i < slab && i < st.SlabN; i++)
            at = st.Slabs![i].Z0Mm + st.Slabs[i].LenMm + gap; // (Place's gap walk, up to the column it would go before)
        if (slab >= st.SlabN && at + lenMm > HoldMm(rd))
        {
            at = 0; // (it goes in only once the columns close up: on the end of them closed up)
            for (var i = 0; i < st.SlabN; i++)
                at += st.Slabs![i].LenMm + gap;
        }
        origin = Origin(rd, new RackSlab { Def = d.Id, Z0Mm = (ushort)at, LenMm = (ushort)lenMm, Lanes = (byte)lanes, Layers = (byte)layers }, d, 0, 0);
        return true;
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

    /// <summary>
    /// The piece of a kind that <see cref="LastOf"/> takes after <paramref name="skip"/> takes before it (each the top of a stack): its
    /// column, stack and layer. A loader carrying pieces off a pad takes them in this order (ConstructStationPads.cs), and the pad doesn't
    /// draw the ones on their way (RouteRender.DrawRacks). Nothing allocates.
    /// </summary>
    public static bool NthLast(CStore st, ushort def, int skip, out int slab, out int lane, out int layer)
    {
        slab = lane = layer = -1;
        Span<byte> left = stackalloc byte[MaxLanes];
        for (var s = st.SlabN - 1; s >= 0; s--)
        {
            if (st.Slabs![s].Def != def)
                continue;
            int lanes = st.Slabs[s].Lanes;
            for (var l = 0; l < lanes; l++)
                left[l] = st.SlabLanes![s * MaxLanes + l];
            while (true)
            {
                var pick = -1;
                for (var l = 0; l < lanes; l++)
                    if (left[l] > 0 && (pick < 0 || left[l] >= left[pick]))
                        pick = l;
                if (pick < 0)
                    break; // (this column is all taken: the one before it next, as Take drops an emptied column)
                if (skip == 0)
                {
                    (slab, lane, layer) = (s, pick, left[pick] - 1);
                    return true;
                }
                left[pick]--;
                skip--;
            }
        }
        return false;
    }

    /// <summary>Where piece <paramref name="layer"/> of a stack has its own origin, in the rack part's frame (its rotation there: <see cref="Turn"/>).</summary>
    public static Vector3 Origin(ConstructPartDef rd, in RackSlab sl, ConstructPartDef d, int lane, int layer)
    {
        Hold(rd, out var lo, out var hi);
        Vector3 b0 = Lo(rd, d), s = Hi(rd, d) - b0; // (a piece lying turned: its turned box, Turned)
        var used = sl.Lanes * s.X + (sl.Lanes - 1) * GapX;
        // (a Pad Tile pad's column lies at its own place across, TilePad; a rack's, or a lone piece wider than the pad, is centred)
        var x = (TilePad(rd) && used <= hi.X - lo.X + 1e-3f ? lo.X + sl.X0Mm * 0.001f : lo.X + (hi.X - lo.X - used) * 0.5f) + lane * (s.X + GapX);
        var z = lo.Z + sl.Z0Mm * 0.001f;
        // (a Pad Tile pad fed from its -z end or -x side is drawn mirrored, so it fills from the far side and a piece coming in passes
        // none parked: ConstructPads.FeedFlip; the packing, the store and the wire are the same either way)
        if (rd.RackFlipX)
            x = lo.X + hi.X - x - s.X;
        if (rd.RackFlipZ)
            z = lo.Z + hi.Z - z - s.Z;
        return new Vector3(x, lo.Y + layer * (s.Y + GapY), z) - b0;
    }

    /// <summary>A stack as one box (rack frame): its pieces from the bed to the top one.</summary>
    public static void StackBox(ConstructPartDef rd, in RackSlab sl, ConstructPartDef d, int lane, int count, out Vector3 min, out Vector3 max)
    {
        var o = Origin(rd, sl, d, lane, 0);
        min = o + Lo(rd, d);
        max = Origin(rd, sl, d, lane, count - 1) + Hi(rd, d);
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
        Vector3 a = Origin(rd, sl, d, Math.Max(first, 0), 0) + Lo(rd, d), b = Origin(rd, sl, d, Math.Max(last, 0), Math.Max(top - 1, 0)) + Hi(rd, d);
        var e = Hi(rd, d) - Lo(rd, d); // (a mirrored pad's first stack is on its +x side: the box spans both ends' stacks)
        min = Vector3.Min(a, b - e);
        max = Vector3.Max(b, a + e);
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
            var key = ConstructParts.Get(sl.Def) is { IsExtrusion: true } ex ? ex.ExKey.Stable() : sl.Def; // (an extrusion's id is this machine's own)
            h = (h ^ key ^ (ulong)sl.Z0Mm << 20 ^ (ulong)sl.Lanes << 40 ^ (ulong)sl.X0Mm << 48) * 1099511628211UL;
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
            w.U8((byte)Math.Max(0, st.KindIndex(sl.Def))).U16(sl.Z0Mm).U16(sl.X0Mm).U16(sl.WidMm).U8(sl.Lanes);
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
            var x0 = r.U16();
            var wid = r.U16();
            int lanes = r.U8();
            if (kind >= st.KindN || lanes > MaxLanes)
                throw new MalformedPacketException($"rack column {s}: kind {kind} of {st.KindN}, {lanes} stacks");
            var def = st.KindDef![kind];
            var lenMm = ConstructParts.Get(def) is { } d ? (int)MathF.Ceiling((d.BoundsMax.Z - d.BoundsMin.Z) * 1000 - 0.01f) : 0;
            const int layers = 255;
            st.Slabs[s] = new RackSlab { Def = def, Z0Mm = z0, X0Mm = x0, WidMm = wid, LenMm = (ushort)lenMm, Lanes = (byte)lanes, Layers = (byte)layers };
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
