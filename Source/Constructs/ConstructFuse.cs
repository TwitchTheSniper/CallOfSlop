using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Fused units (the fuse tool; docs/sandbox/CONSTRUCTS.md, "Fusing"). A part's <see cref="CPart.Fuse"/> tag says which unit
/// it is in; the parts with one tag on one segment, linked to each other, are one unit. The tags are the truth (they move
/// with the parts through every compaction, split, merge, save, blueprint and the wire); everything here is a cache made
/// from them at each rebuild (<see cref="FuseRefresh"/>): the units, their members, a rough collider fitted over their
/// plain parts (<see cref="ConstructFuseFit"/>, when the collider is next built: a client never fits one) and their plain
/// parts' share of the mass sums. What a unit changes: the collider has the rough boxes in place of the members' own
/// (construct against construct and against the map), and a full rebuild adds each unit's cached share of the mass sums
/// instead of summing its parts. What it doesn't: the part tree (rays, bullets, blasts, players walking, building) and
/// every system see the exact parts, and every part keeps its own health.
/// </summary>
public sealed partial class Construct
{
    /// <summary>One fused unit, as the last rebuild found it.</summary>
    internal sealed class FuseUnit
    {
        public uint Tag;
        public byte Seg; // the segment every member is on
        public int Count; // members now
        public int First; // its members' run in _fuseMem
        public int Members = -1; // members at the last refresh (-1: new)
        public int FitMembers; // members when the rough shape was fitted (0: never)
        public bool NeedFit = true;
        public bool SumsOk; // Share holds its covered parts' share of the mass sums
        public MassShare Share;
        // The rough shape: boxes in its segment's frame (the frame SolidBox gives), over the covered members.
        public Vector3[] RMin = [], RMax = [];
        public int RoughCount;
        public float Cell; // the fit's cell size (the error bound: a rough box is within a cell diagonal of the real boxes it covers)
        public double RoughVolume, ExactVolume; // what the rough boxes enclose and the covered boxes' own volume (overlaps counted twice)
        public int Covered; // members inside the rough shape (the plain ones)
    }

    /// <summary>A part of the mass sums (ConstructRebuild.cs): the doubles <see cref="AddPartSums"/> adds to, for segment 0.</summary>
    internal struct MassShare
    {
        public double M, Bm, XX, YY, ZZ, XY, XZ, YZ, JXX, JYY, JZZ, JXY, JXZ, JYZ, OwnM, SegM;
        public Vector3D C, B, OwnC, SegC;
    }

    private FuseUnit[] _units = [];
    private int _unitCount;
    private int[] _fuseIx = []; // part -> unit index, -1: none (valid while _fuseLive)
    private int[] _fuseMem = []; // every unit's members, unit by unit, in part order
    private int[] _fuseSeen = [];
    private int _fuseStamp;
    private int[] _unitRemap = [];
    private bool _fuseLive;

    /// <summary>Units in this body (as of the last rebuild).</summary>
    internal int UnitCount => _fuseLive ? _unitCount : 0;

    internal FuseUnit Unit(int u) => _units[u];

    /// <summary>Parts in a unit, as of the last rebuild.</summary>
    public int FusedParts { get; private set; }

    /// <summary>The unit part <paramref name="i"/> is in (as of the last rebuild), or -1.</summary>
    internal int UnitOf(int i) => _fuseLive && (uint)i < (uint)PartCount && i < _fuseIx.Length ? _fuseIx[i] : -1;

    /// <summary>The members of unit <paramref name="u"/>, in part order.</summary>
    internal ReadOnlySpan<int> UnitMembers(int u) => _fuseMem.AsSpan(_units[u].First, _units[u].Count);

    /// <summary>How many parts are in part <paramref name="i"/>'s unit (0: it isn't fused, or the body waits for its rebuild).</summary>
    public int UnitSize(int i) => UnitOf(i) is var u and >= 0 ? _units[u].Count : 0;

    /// <summary>Is the whole body one fused unit (every part in it): it counts as a single piece where a single part is asked for.</summary>
    public bool IsOneUnit => _fuseLive && _unitCount == 1 && FusedParts == PartCount && PartCount > 1;

    /// <summary>Part <paramref name="i"/>'s boxes are inside its unit's rough shape (its own leaves are left out of the collider).</summary>
    internal bool FuseRough(int i) => UnitOf(i) is var u and >= 0 && _units[u].RoughCount > 0 && FuseCovers(i);

    /// <summary>Does a unit's rough shape and mass share take part <paramref name="i"/> in: plain structure (frames, panels, wedges), not a merged receiver.</summary>
    internal bool FuseCovers(int i) => (i >= MergeOf.Length || MergeOf[i] == 0) && PlainParts.Of(Parts[i].D);

    /// <summary>Is part <paramref name="i"/>'s share of the mass sums held by its unit (a full rebuild adds the unit's share instead)?</summary>
    private bool FuseMassHeld(int i) => UnitOf(i) is var u and >= 0 && _units[u].Seg == 0 && FuseCovers(i);

    /// <summary>A unit wants its rough shape fitted (the collider fits it when it is next built).</summary>
    internal bool FuseFitDue
    {
        get
        {
            for (var u = 0; u < UnitCount; u++)
                if (_units[u].NeedFit)
                    return true;
            return false;
        }
    }

    /// <summary>Every unit's rough shape that wants it, fitted now (from the collider's build).</summary>
    internal void FuseFitNow()
    {
        for (var u = 0; u < UnitCount; u++)
            if (_units[u].NeedFit)
            {
                ConstructFuseFit.Fit(this, _units[u], UnitMembers(u));
                _units[u].NeedFit = false;
                _units[u].FitMembers = _units[u].Count;
            }
    }

    /// <summary>The mass sums' unit shares are summed again at the next full sum (a part's shape changed in place).</summary>
    internal void FuseMassStale()
    {
        for (var u = 0; u < _unitCount; u++)
            _units[u].SumsOk = false;
    }

    /// <summary>
    /// The units from the parts' tags (every rebuild, before the mass and the collider): counts each unit's members, drops the
    /// empty ones, finds what a lost member cut apart (each connected group of two or more stays fused, the largest keeping the
    /// tag; a part left alone is an ordinary part again) and marks what must be fitted again. True when a unit's rough shape
    /// or members changed (the collider has to be built again). O(parts), plus O(members) of a unit that lost some.
    /// </summary>
    internal bool FuseRefresh()
    {
        _fuseLive = false;
        var n = PartCount;
        if (_unitCount == 0)
        {
            var any = false;
            for (var i = 0; i < n && !any; i++)
                any = Parts[i].Fuse != 0;
            if (!any)
            {
                FusedParts = 0;
                _fuseLive = true;
                return false;
            }
        }
        if (_fuseIx.Length < n)
            _fuseIx = new int[Math.Max(n, _fuseIx.Length * 2)];
        var changed = false;
        for (var pass = 0; pass < 3; pass++)
        {
            for (var u = 0; u < _unitCount; u++)
                _units[u].Count = 0;
            uint lastTag = 0;
            var lastU = -1;
            for (var i = 0; i < n; i++)
            {
                ref var p = ref Parts[i];
                if (p.Fuse == 0)
                {
                    _fuseIx[i] = -1;
                    continue;
                }
                if (p.Fuse != lastTag)
                {
                    lastTag = p.Fuse;
                    lastU = FindUnit(p.Fuse, p.Seg);
                }
                var unit = _units[lastU];
                if (unit.Seg != p.Seg)
                {
                    p.Fuse = 0; // (a member moved off its unit's segment: an ordinary part)
                    _fuseIx[i] = -1;
                    changed = true;
                    continue;
                }
                _fuseIx[i] = lastU;
                unit.Count++;
            }
            // Drop the empty units (their index moves down: the parts follow).
            var keep = 0;
            if (_unitRemap.Length < _unitCount)
                _unitRemap = new int[Math.Max(_unitCount, _unitRemap.Length * 2)];
            var remap = _unitRemap;
            for (var u = 0; u < _unitCount; u++)
            {
                if (_units[u].Count == 0)
                {
                    remap[u] = -1;
                    changed = true;
                    continue;
                }
                remap[u] = keep;
                (_units[keep], _units[u]) = (_units[u], _units[keep]);
                keep++;
            }
            if (keep != _unitCount)
            {
                for (var i = 0; i < n; i++)
                    if (_fuseIx[i] >= 0)
                        _fuseIx[i] = remap[_fuseIx[i]];
                _unitCount = keep;
            }
            // Members unit by unit.
            if (_fuseMem.Length < n)
                _fuseMem = new int[Math.Max(n, _fuseMem.Length * 2)];
            var at = 0;
            for (var u = 0; u < _unitCount; u++)
            {
                _units[u].First = at;
                at += _units[u].Count;
                _units[u].Count = 0;
            }
            FusedParts = at;
            for (var i = 0; i < n; i++)
                if (_fuseIx[i] is var u and >= 0)
                    _fuseMem[_units[u].First + _units[u].Count++] = i;
            // What changed since the last rebuild.
            var split = false;
            for (var u = 0; u < _unitCount; u++)
            {
                var unit = _units[u];
                if (unit.Count == unit.Members)
                    continue;
                changed = true;
                unit.SumsOk = false;
                if (unit.Count < 2 || !FuseConnected(u))
                {
                    FuseSplit(u);
                    split = true;
                    continue;
                }
                if (unit.Members < 0 || unit.Count > unit.FitMembers || (unit.FitMembers - unit.Count) * 16 >= unit.FitMembers)
                    unit.NeedFit = true; // (new, grown, or a sixteenth gone since it was fitted: the rough shape stays loose until then, never short)
                unit.Members = unit.Count;
            }
            if (!split)
                break;
        }
        for (var u = 0; u < _unitCount; u++)
            if (_units[u].NeedFit)
                changed = true;
        _fuseLive = true;
        return changed;
    }

    private int FindUnit(uint tag, byte seg)
    {
        for (var u = 0; u < _unitCount; u++)
            if (_units[u].Tag == tag)
                return u;
        if (_unitCount == _units.Length)
            Array.Resize(ref _units, Math.Max(4, _units.Length * 2));
        var unit = _units[_unitCount] ??= new FuseUnit();
        unit.Tag = tag;
        unit.Seg = seg;
        unit.Members = -1;
        unit.FitMembers = 0;
        unit.NeedFit = true;
        unit.SumsOk = false;
        unit.RoughCount = 0;
        unit.Count = 0;
        return _unitCount++;
    }

    /// <summary>Are unit <paramref name="u"/>'s members one piece over their own links?</summary>
    private bool FuseConnected(int u)
    {
        var label = FuseLabels(1);
        return FuseFlood(u, _fuseMem[_units[u].First], label, label) == _units[u].Count;
    }

    /// <summary><paramref name="count"/> fresh labels for <see cref="FuseFlood"/>: the first is returned, every one after it is new too.</summary>
    private int FuseLabels(int count)
    {
        if (_fuseSeen.Length < PartCount)
            _fuseSeen = new int[Math.Max(PartCount, _fuseSeen.Length * 2)];
        if (_fuseStamp > int.MaxValue - count - 2)
        {
            Array.Clear(_fuseSeen);
            _fuseStamp = 0;
        }
        return _fuseStamp + 1;
    }

    /// <summary>
    /// Members of unit <paramref name="u"/> reached from <paramref name="from"/> over links between members, labelled
    /// <paramref name="label"/>; a member with a label from <paramref name="since"/> on is taken as seen. Returns how many.
    /// </summary>
    private int FuseFlood(int u, int from, int label, int since)
    {
        _fuseStamp = Math.Max(_fuseStamp, label);
        using var scratch = Scratch.Begin();
        var queue = scratch.Take<int>(_units[u].Count);
        int head = 0, tail = 0;
        _fuseSeen[from] = label;
        queue[tail++] = from;
        while (head < tail)
        {
            var at = queue[head++];
            for (var e = FirstLink(at); e >= 0; e = NextLink(e))
            {
                var o = OtherEnd(e);
                if (_fuseIx[o] != u || _fuseSeen[o] >= since)
                    continue;
                _fuseSeen[o] = label;
                queue[tail++] = o;
            }
        }
        return tail;
    }

    /// <summary>
    /// Unit <paramref name="u"/> came apart: each connected group of its members of two or more parts is a unit (the largest
    /// keeps the tag, the first of equals; the rest get new ones); a member alone is an ordinary part again. Only the tags
    /// change here: the next pass of <see cref="FuseRefresh"/> makes the units.
    /// </summary>
    private void FuseSplit(int u)
    {
        var unit = _units[u];
        unit.Members = -1; // (whatever keeps the tag is fitted again)
        unit.NeedFit = true;
        var mem = UnitMembers(u);
        if (unit.Count < 2)
        {
            foreach (var i in mem)
                Parts[i].Fuse = 0;
            return;
        }
        var since = FuseLabels(mem.Length);
        using var scratch = Scratch.Begin();
        var sizes = scratch.Take<int>(mem.Length);
        var groups = 0;
        var best = 0;
        foreach (var i in mem)
        {
            if (_fuseSeen[i] >= since)
                continue;
            var size = FuseFlood(u, i, since + groups, since);
            sizes[groups] = size;
            if (size > sizes[best])
                best = groups;
            groups++;
        }
        var tags = scratch.Take<uint>(groups);
        foreach (var i in mem)
        {
            var g = _fuseSeen[i] - since;
            if (g == best)
                continue;
            if (sizes[g] < 2)
            {
                Parts[i].Fuse = 0;
                continue;
            }
            if (tags[g] == 0)
                tags[g] = ConstructFuse.NewTag();
            Parts[i].Fuse = tags[g];
        }
    }

    // ---- The mass sums: a unit's plain parts as one share --------------------------------------------------------

    private MassShare TakeShare() => new()
    {
        M = _sM, Bm = _sBm, XX = _sXX, YY = _sYY, ZZ = _sZZ, XY = _sXY, XZ = _sXZ, YZ = _sYZ,
        JXX = _jXX, JYY = _jYY, JZZ = _jZZ, JXY = _jXY, JXZ = _jXZ, JYZ = _jYZ,
        C = _sC, B = _sB, OwnM = _ssOwnM[0], SegM = _ssM[0], OwnC = _ssOwnC[0], SegC = _ssC[0],
    };

    private void PutShare(in MassShare s)
    {
        _sM = s.M;
        _sBm = s.Bm;
        _sXX = s.XX;
        _sYY = s.YY;
        _sZZ = s.ZZ;
        _sXY = s.XY;
        _sXZ = s.XZ;
        _sYZ = s.YZ;
        _jXX = s.JXX;
        _jYY = s.JYY;
        _jZZ = s.JZZ;
        _jXY = s.JXY;
        _jXZ = s.JXZ;
        _jYZ = s.JYZ;
        _sC = s.C;
        _sB = s.B;
        _ssOwnM[0] = s.OwnM;
        _ssM[0] = s.SegM;
        _ssOwnC[0] = s.OwnC;
        _ssC[0] = s.SegC;
    }

    private static void AddInto(ref MassShare a, in MassShare s)
    {
        a.M += s.M;
        a.Bm += s.Bm;
        a.XX += s.XX;
        a.YY += s.YY;
        a.ZZ += s.ZZ;
        a.XY += s.XY;
        a.XZ += s.XZ;
        a.YZ += s.YZ;
        a.JXX += s.JXX;
        a.JYY += s.JYY;
        a.JZZ += s.JZZ;
        a.JXY += s.JXY;
        a.JXZ += s.JXZ;
        a.JYZ += s.JYZ;
        a.OwnM += s.OwnM;
        a.SegM += s.SegM;
        a.C.X += s.C.X; a.C.Y += s.C.Y; a.C.Z += s.C.Z;
        a.B.X += s.B.X; a.B.Y += s.B.Y; a.B.Z += s.B.Z;
        a.OwnC.X += s.OwnC.X; a.OwnC.Y += s.OwnC.Y; a.OwnC.Z += s.OwnC.Z;
        a.SegC.X += s.SegC.X; a.SegC.Y += s.SegC.Y; a.SegC.Z += s.SegC.Z;
    }

    /// <summary>
    /// SumParts' units: each unit on the body's own segment adds its covered parts' share as one, summed from its parts only
    /// when it changed since (a full rebuild after an edit elsewhere doesn't sum a fused hull's parts again).
    /// </summary>
    private void FuseAddMass()
    {
        if (!_fuseLive || _unitCount == 0)
            return;
        var total = TakeShare();
        for (var u = 0; u < _unitCount; u++)
        {
            var unit = _units[u];
            if (unit.Seg != 0)
                continue;
            if (!unit.SumsOk)
            {
                PutShare(default);
                foreach (var i in UnitMembers(u))
                    if (FuseCovers(i))
                        AddPartSums(i, 1);
                unit.Share = TakeShare();
                unit.SumsOk = true;
            }
            AddInto(ref total, unit.Share);
        }
        PutShare(total);
    }
}

/// <summary>The fuse tags and their wire form (docs/sandbox/CONSTRUCTS.md, "Fusing").</summary>
public static class ConstructFuse
{
    private static uint _next = 1;

    /// <summary>A tag no unit has (the server's; one counter for every world in the process, so bodies merged together never mix two units).</summary>
    public static uint NewTag()
    {
        var t = Interlocked.Increment(ref _next);
        if (t == 0)
            t = Interlocked.Increment(ref _next);
        return t;
    }

    /// <summary>A tag came from outside (a client's copy): the counter stays past it.</summary>
    public static void Saw(uint tag)
    {
        uint seen;
        while ((seen = Volatile.Read(ref _next)) < tag && Interlocked.CompareExchange(ref _next, tag, seen) != seen)
        {
        }
    }

    /// <summary>The tags as runs of parts (most units are runs of parts added together): u16 runs, each u16 first part, u16 length, u32 tag.</summary>
    public static void WriteFull(PacketWriter w, Construct c)
    {
        var at = w.Length;
        w.U16(0);
        var runs = 0;
        for (var i = 0; i < c.PartCount;)
        {
            var tag = c.Parts[i].Fuse;
            var end = i + 1;
            while (end < c.PartCount && c.Parts[end].Fuse == tag && end - i < ushort.MaxValue)
                end++;
            if (tag != 0)
            {
                w.U16((ushort)i).U16((ushort)(end - i)).U32(tag);
                runs++;
            }
            i = end;
        }
        w.PatchU16(at, (ushort)runs);
    }

    public static void ReadFull(ref PacketReader r, Construct c)
    {
        int runs = r.U16();
        for (var k = 0; k < runs; k++)
        {
            int first = r.U16();
            int count = r.U16();
            var tag = r.U32();
            if (first + count > c.PartCount || tag == 0)
                throw new MalformedPacketException("a fused unit of parts that aren't there");
            for (var i = first; i < first + count; i++)
                c.Parts[i].Fuse = tag;
        }
    }

    /// <summary>Tags as a save or blueprint numbers them: 1, 2, ... in the order the units' first parts come (0: not fused).</summary>
    public static int[] LocalNumbers(Construct c, ReadOnlySpan<int> parts)
    {
        var map = new Dictionary<uint, int>();
        var local = new int[parts.Length];
        for (var k = 0; k < parts.Length; k++)
        {
            var tag = c.Parts[parts[k]].Fuse;
            if (tag == 0)
                continue;
            if (!map.TryGetValue(tag, out var n))
                map[tag] = n = map.Count + 1;
            local[k] = n;
        }
        return local;
    }

    /// <summary>Local unit numbers (a save's, a blueprint's) to new tags, one each; 0 stays 0.</summary>
    public sealed class Renumber
    {
        private readonly Dictionary<int, uint> _tags = new();

        public uint Tag(int local)
        {
            if (local <= 0)
                return 0;
            if (!_tags.TryGetValue(local, out var t))
                _tags[local] = t = NewTag();
            return t;
        }
    }
}
