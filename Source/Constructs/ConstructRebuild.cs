using System.Numerics;
using System.Reflection;

namespace CallOfSlop;

/// <summary>
/// Which part types are <b>plain</b> structure (docs/sandbox/CONSTRUCTS.md, "Rebuilds"): a frame or a panel whose file
/// sets nothing a system looks for. Every field of the type is compared with a new <see cref="ConstructPartDef"/>; only
/// the fields that shape structure (mass, health, boxes, struct sockets, looks, the inventory's words) may differ. A field
/// added later counts against plainness until it is listed here, so a new system is never skipped by mistake.
/// </summary>
internal static class PlainParts
{
    private static readonly HashSet<string> Structural =
    [
        nameof(ConstructPartDef.Id), nameof(ConstructPartDef.Name), nameof(ConstructPartDef.File), nameof(ConstructPartDef.Folder),
        nameof(ConstructPartDef.Description), nameof(ConstructPartDef.Group), nameof(ConstructPartDef.Section), nameof(ConstructPartDef.Role),
        nameof(ConstructPartDef.Tool), nameof(ConstructPartDef.AttachSeconds), nameof(ConstructPartDef.Mass), nameof(ConstructPartDef.Health),
        nameof(ConstructPartDef.Armour), nameof(ConstructPartDef.Material), nameof(ConstructPartDef.SurfaceOverride), nameof(ConstructPartDef.Friction),
        nameof(ConstructPartDef.OnBreak), nameof(ConstructPartDef.Yield), nameof(ConstructPartDef.Boxes), nameof(ConstructPartDef.SolidBoxes),
        nameof(ConstructPartDef.ColliderBoxes), nameof(ConstructPartDef.HasGlass), nameof(ConstructPartDef.Sockets), nameof(ConstructPartDef.Class),
        nameof(ConstructPartDef.CentreOptOut), nameof(ConstructPartDef.BoundsMin), nameof(ConstructPartDef.BoundsMax),
        nameof(ConstructPartDef.DamagedBelow), nameof(ConstructPartDef.DamagedName), nameof(ConstructPartDef.DamagedTo),
        nameof(ConstructPartDef.Generated), nameof(ConstructPartDef.CreativeOnly), nameof(ConstructPartDef.Legacy),
        nameof(ConstructPartDef.Faces), nameof(ConstructPartDef.FaceAxis),
        nameof(ConstructPartDef.PhysicsOnly), nameof(ConstructPartDef.PhysicsWhy), // (how the inventory hands it out)
        nameof(ConstructPartDef.CfgSlot), // (follows Config, which is checked)
    ];

    private static readonly FieldInfo[] Fields = typeof(ConstructPartDef).GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly ConstructPartDef Blank = new();
    private static readonly ConstructPartDef?[] _seen = new ConstructPartDef?[ConstructParts.MaxTypes];
    private static readonly bool[] _plain = new bool[ConstructParts.MaxTypes];

    /// <summary>Is this part type plain structure? Cached by type (a reloaded file is looked at again).</summary>
    public static bool Of(ConstructPartDef d)
    {
        if (d.MatBase is { } b)
            return Of(b); // (a part of another material differs from its file part only in structural fields: PartMaterials.cs)
        if (d.Id >= ConstructParts.MaxTypes)
            return false; // (a run-time part: an extrusion, a design)
        if (!ReferenceEquals(Volatile.Read(ref _seen[d.Id]), d))
        {
            _plain[d.Id] = Work(d, null);
            Volatile.Write(ref _seen[d.Id], d); // (after the answer: a vehicle kit thread building at the same time reads it whole)
        }
        return _plain[d.Id];
    }

    /// <summary>Why a type isn't plain (the first field in the way), or null (for the docs and the harness).</summary>
    public static string? Why(ConstructPartDef d)
    {
        var why = new List<string>();
        return Work(d, why) ? null : string.Join(", ", why);
    }

    private static bool Work(ConstructPartDef d, List<string>? why)
    {
        var ok = true;
        if (d.Role is not (PartRole.Frame or PartRole.Panel))
        {
            why?.Add($"role {d.Role}");
            ok = false;
        }
        foreach (var s in d.Sockets)
            if (s.Kind != SocketKind.Struct)
            {
                why?.Add($"a {s.Kind} socket");
                ok = false;
                break;
            }
        foreach (var f in Fields)
        {
            if (Structural.Contains(f.Name) || f.IsInitOnly && f.IsStatic)
                continue;
            if (Same(f.GetValue(d), f.GetValue(Blank)))
                continue;
            why?.Add(f.Name);
            ok = false;
            if (why == null)
                break;
        }
        return ok;
    }

    private static bool Same(object? a, object? b)
    {
        if (a == null || b == null)
            return a == b;
        if (a is Array x && b is Array y)
            return x.Length == 0 && y.Length == 0;
        return a.Equals(b);
    }
}

/// <summary>
/// Rebuilds after an edit (docs/sandbox/CONSTRUCTS.md, "Rebuilds"; docs/perf/LIMITS.md, "Rebuilds"). An edit marks the
/// body <see cref="Dirty"/> and it is rebuilt once, before the next thing that needs it (the tick's first look at it), so
/// several edits in a tick cost one rebuild. A big body (<see cref="IncrementalMin"/> parts or more) keeps a journal of the
/// plain edits since its last rebuild (plain structure added, linked or taken off, see <see cref="IsPlainPart"/>): when
/// that is all that changed, <see cref="RebuildPlain"/> updates the mass sums by the parts that came and went, maps the
/// collider, the part tree and the special parts' list through the edits instead of building them again, and checks the
/// circuit only near the part that went; every system is built again from the special parts alone. Anything else (any
/// other <c>Dirty = true</c>) is a full <see cref="Rebuild"/>. <see cref="RebuildCheck"/> compares each journalled rebuild
/// with a full one. Allocates nothing once the arrays have grown.
/// </summary>
public sealed partial class Construct
{
    /// <summary>Parts from which a body keeps the journal and the mass sums. Smaller bodies rebuild as they always did.</summary>
    public const int IncrementalMin = 1024;

    /// <summary>Plain rebuilds before the mass sums are summed again from the parts (rounding never builds up).</summary>
    private const int ResumEvery = 256;

    /// <summary>
    /// Debug: after each journalled rebuild, a full one, and the two compared (mass, centre of mass, inertia, bounds,
    /// segments, circuit, drive line, wheels, special parts, collider and tree); differences go to <see cref="CheckLog"/>.
    /// Off unless set (the harness, or <c>CALLOFSLOP_REBUILD_CHECK=1</c>): it costs a full rebuild each time.
    /// </summary>
    internal static bool RebuildCheck = Environment.GetEnvironmentVariable("CALLOFSLOP_REBUILD_CHECK") == "1";

    internal static readonly List<string> CheckLog = [];
    internal static int CheckRuns, CheckFails, PlainRuns, FullRuns; // (counts for the check and a harness: every body, every thread, roughly)

    private bool _dirty = true;
    private bool _needFull = true; // (any Dirty = true: the journal can't say what changed)

    /// <summary>The parts changed: <see cref="Rebuild"/> before the next use. Setting it means a full rebuild; a plain edit calls <see cref="DirtyPlain"/>.</summary>
    public bool Dirty
    {
        get => _dirty;
        set
        {
            _dirty = value;
            if (value)
            {
                _needFull = true;
                _specialsLive = false;
                _fuseLive = false; // (the units follow the parts again at the rebuild: ConstructFuse.cs)
            }
        }
    }

    // ---- The journal: the edits since the last rebuild, state S0 with _jBase parts -----------------------------------

    private int _jBase; // parts at the last rebuild
    private int _jSurvivors; // of those, how many are still here (they come first: adds go on the end, compacting keeps the order)
    private int[] _jMap = []; // S0 index -> index now, -1 gone (valid while _jMapped)
    private bool _jMapped;
    private bool _jPower; // the circuit has to be flooded again (a powered part went and its neighbours didn't meet round it)
    private bool _jBounds; // a part on the bounds went: the bounds are summed again
    private bool _jAdded; // parts were added (they go into the collider's small tree of their own)
    private int _plainCount; // plain rebuilds since the sums were summed from the parts
    private int _editSerial, _serialAtRebuild; // (what the collider and the part tree were built at)

    /// <summary>A plain edit (journalled): the body is rebuilt, but only as far as the journal says.</summary>
    internal void DirtyPlain()
    {
        _dirty = true;
        _specialsLive = false;
        _fuseLive = false;
        _editSerial++;
    }

    /// <summary>Does this body keep the journal (and is the journal still good)?</summary>
    private bool Journalling => !_needFull && PartCount >= IncrementalMin && _jBase >= IncrementalMin && FreeformCount == 0 && MergeCount == 0 && LegCount == 0; // (a walker rebuilds whole: what rides its legs is found before the collider, ConstructWalkRiders.cs)

    /// <summary>
    /// Is part <paramref name="i"/> plain structure: a plain type (<see cref="PlainParts"/>) on the body's own segment,
    /// holding nothing, merged with nothing, the end of no routed link? Such a part counts in mass, collision and the
    /// circuit (the frame is the circuit) and nowhere else.
    /// </summary>
    public bool IsPlainPart(int i) => PlainShape(i) && !OnRoute(i);

    /// <summary><see cref="IsPlainPart"/> but for the routed links.</summary>
    private bool PlainShape(int i)
    {
        ref var p = ref Parts[i];
        return p.Seg == 0 && p.Store == 0 && (i >= MergeOf.Length || MergeOf[i] == 0) && PlainParts.Of(p.D);
    }

    /// <summary>Is part <paramref name="i"/> an end of a routed link, or does one of its nodes sit on it (taking it off cuts the link)?</summary>
    private bool OnRoute(int i)
    {
        for (var r = 0; r < RouteCount; r++)
            if (Routes[r].Source == i || Routes[r].To == i || Routes[r].Parent == 255 && Routes[r].From == i)
                return true;
        for (var k = 0; k < RouteNodeCount; k++)
            if (RouteNodes[k].Host == i)
                return true;
        return false;
    }

    /// <summary>A plain part was added at <paramref name="i"/> (the last index). Called by <see cref="ConstructWorld.AddPart"/>.</summary>
    internal bool JournalAdd(int i)
    {
        if (!Journalling || i != PartCount - 1 || !IsPlainPart(i))
            return false;
        _jAdded = true;
        DirtyPlain();
        return true;
    }

    /// <summary>A link between <paramref name="a"/> and <paramref name="b"/>: journalled when both are plain.</summary>
    internal bool JournalLink(int a, int b)
    {
        if (!Journalling || (uint)a >= (uint)PartCount || (uint)b >= (uint)PartCount || !IsPlainPart(a) || !IsPlainPart(b))
            return false;
        if (HasBattery && a < _jSurvivors && b < _jSurvivors && Parts[a].Powered != Parts[b].Powered)
            _jPower = true; // (a new way for the circuit between parts that were here: flooded again. A new part's own way is PowerAdded's)
        DirtyPlain();
        return true;
    }

    /// <summary>
    /// Can part <paramref name="i"/> go by the journal: plain, and every part it is linked to plain (taking it away
    /// changes nothing a special part sees, such as a wheel's mount)?
    /// </summary>
    internal bool PlainRemoval(int i)
    {
        if (!Journalling || !IsPlainPart(i))
            return false;
        for (var e = FirstLink(i); e >= 0; e = NextLink(e))
            if (!IsPlainPart(OtherEnd(e)))
                return false;
        return true;
    }

    /// <summary>
    /// Part <paramref name="i"/> (plain, now numbered) is about to go: its share comes off the sums (if it was there at
    /// the last rebuild) and the bounds are noted. Called before the parts are compacted. <paramref name="quiet"/>: a part
    /// of a plain piece that is cut off with it (the circuit is flooded again rather than looked at round each).
    /// </summary>
    internal void JournalRemove(int i, bool quiet = false)
    {
        if (i < _jSurvivors)
        {
            AddPartSums(i, -1);
            if (OnBounds(i))
                _jBounds = true;
        }
        if (quiet)
            _jPower |= HasBattery; // (a piece cut off: the circuit is flooded again, it is cheap next to the split)
        else if (Parts[i].Powered && HasBattery && !_jPower && !PowerMeetsRound(i))
            _jPower = true;
    }

    /// <summary>Is the journal taking the edits since the last rebuild (only plain ones so far, and some)?</summary>
    internal bool JournalOpen => _dirty && !_needFull;

    /// <summary>The parts were compacted by <paramref name="map"/> (old index -> new, -1 gone; <paramref name="oldCount"/> long): the journal's map follows.</summary>
    internal void JournalCompact(ReadOnlySpan<int> map, int oldCount)
    {
        if (!_dirty || _needFull)
            return;
        if (!_jMapped)
        {
            if (_jMap.Length < _jBase)
                _jMap = new int[Math.Max(_jBase, _jMap.Length * 2)];
            for (var k = 0; k < _jBase; k++)
                _jMap[k] = k;
            _jMapped = true;
        }
        var survivors = 0;
        for (var k = 0; k < _jBase; k++)
        {
            var at = _jMap[k];
            _jMap[k] = at >= 0 && at < oldCount ? map[at] : -1;
            if (_jMap[k] >= 0)
                survivors++;
        }
        _jSurvivors = survivors;
        _editSerial++;
    }

    /// <summary>Starts the journal again at the parts as they are now (after any rebuild).</summary>
    private void JournalReset()
    {
        _needFull = false;
        _jBase = _jSurvivors = PartCount;
        _jMapped = _jPower = _jBounds = _jAdded = false;
        _serialAtRebuild = _editSerial;
    }

    // ---- The special parts: every part that isn't plain ---------------------------------------------------------

    private int[] _special = [];

    /// <summary>The parts that aren't plain (<see cref="IsPlainPart"/>), in part order: the systems look only at these.</summary>
    internal int SpecialCount;

    internal ReadOnlySpan<int> Specials => _specialsLive ? _special.AsSpan(0, SpecialCount) : AllIndices.AsSpan(0, PartCount);

    private bool _specialsLive; // (the list follows the parts: false from an edit until the rebuild makes it again, when every part is looked at)
    private static readonly int[] AllIndices = MakeIndices();

    private static int[] MakeIndices()
    {
        var a = new int[MaxParts];
        for (var i = 0; i < a.Length; i++)
            a[i] = i;
        return a;
    }

    private void BuildSpecials()
    {
        if (_special.Length < PartCount)
            Array.Resize(ref _special, Math.Max(PartCount, _special.Length * 2));
        SpecialCount = 0;
        using var scratch = Scratch.Begin();
        Span<bool> routed = scratch.Take<bool>(PartCount); // (every part a routed link ends on or runs over, in one walk)
        routed.Clear();
        for (var r = 0; r < RouteCount; r++)
        {
            ref var rt = ref Routes[r];
            if (rt.Source < PartCount)
                routed[rt.Source] = true;
            if (rt.To < PartCount)
                routed[rt.To] = true;
            if (rt.Parent == 255 && rt.From < PartCount)
                routed[rt.From] = true;
        }
        for (var k = 0; k < RouteNodeCount; k++)
            if (RouteNodes[k].Host < PartCount)
                routed[RouteNodes[k].Host] = true;
        for (var i = 0; i < PartCount; i++)
            if (routed[i] || !PlainShape(i))
                _special[SpecialCount++] = i;
        _specialsLive = true;
    }

    /// <summary>The special parts through the journal's map (none went: plain removals only).</summary>
    private void MapSpecials()
    {
        if (_jMapped)
            for (var k = 0; k < SpecialCount; k++)
                _special[k] = _jMap[_special[k]];
        _specialsLive = true;
    }

    // ---- The mass sums: every part's share, kept so an edit adds or takes off its own ----------------------------

    private double _sM, _sBm; // part mass; mass of the solid boxes (the inertia's)
    private Vector3D _sC, _sB; // first moments: part mass at the parts' middles (the centre of mass), box mass at the boxes' (the inertia)
    private double _sXX, _sYY, _sZZ, _sXY, _sXZ, _sYZ; // second moments of the boxes' mass about the body origin
    private double _jXX, _jYY, _jZZ, _jXY, _jXZ, _jYZ; // the boxes' own inertia, summed
    private Vector3 _sMin, _sMax; // the parts' bounds (structure: a merged receiver's and an envelope's drawn boxes too)
    private double[] _ssOwnM = [], _ssM = [], _ssSq = []; // each segment's own and carried mass, and its spin term (big bodies only: made at the first sum)
    private Vector3D[] _ssOwnC = [], _ssC = [];

    internal struct Vector3D
    {
        public double X, Y, Z;

        public void Add(Vector3 v, double m)
        {
            X += v.X * m;
            Y += v.Y * m;
            Z += v.Z * m;
        }

        public readonly Vector3 Over(double m) => new((float)(X / m), (float)(Y / m), (float)(Z / m));
    }

    /// <summary>Sums every part's share again (a full rebuild of a big body, and every <see cref="ResumEvery"/> plain ones).</summary>
    private void SumParts()
    {
        _sM = _sBm = 0;
        _sC = _sB = default;
        _sXX = _sYY = _sZZ = _sXY = _sXZ = _sYZ = 0;
        _jXX = _jYY = _jZZ = _jXY = _jXZ = _jYZ = 0;
        if (_ssM.Length == 0)
        {
            _ssOwnM = new double[MaxJoints + 1];
            _ssM = new double[MaxJoints + 1];
            _ssSq = new double[MaxJoints + 1];
            _ssOwnC = new Vector3D[MaxJoints + 1];
            _ssC = new Vector3D[MaxJoints + 1];
        }
        for (var s = 0; s <= MaxJoints; s++)
        {
            _ssOwnM[s] = _ssM[s] = _ssSq[s] = 0;
            _ssOwnC[s] = _ssC[s] = default;
        }
        SumBounds();
        for (var i = 0; i < PartCount; i++)
            if (!FuseMassHeld(i))
                AddPartSums(i, 1);
        FuseAddMass(); // (each fused unit's plain parts as one share, summed again only when the unit changed: ConstructFuse.cs)
        _plainCount = 0;
    }

    private void SumBounds()
    {
        _sMin = new Vector3(float.MaxValue);
        _sMax = new Vector3(float.MinValue);
        Array.Clear(_sAt);
        for (var i = 0; i < PartCount; i++)
        {
            StructBounds(i, out var mn, out var mx);
            GrowBounds(mn, mx);
        }
    }

    /// <summary>How many parts reach each face of the bounds (-x, -y, -z, +x, +y, +z): a face moves in only when its last one goes.</summary>
    private readonly int[] _sAt = new int[6];

    private void GrowBounds(Vector3 mn, Vector3 mx)
    {
        for (var a = 0; a < 3; a++)
        {
            if (mn[a] < _sMin[a])
            {
                _sMin[a] = mn[a];
                _sAt[a] = 1;
            }
            else if (mn[a] == _sMin[a])
                _sAt[a]++;
            if (mx[a] > _sMax[a])
            {
                _sMax[a] = mx[a];
                _sAt[3 + a] = 1;
            }
            else if (mx[a] == _sMax[a])
                _sAt[3 + a]++;
        }
    }

    private void StructBounds(int i, out Vector3 mn, out Vector3 mx)
    {
        RestBounds(i, out mn, out mx);
        if (MergeOf[i] != 0 || Parts[i].D.IsEnvelope)
        {
            ShapeBounds(i, out var smn, out var smx); // (a merged receiver's drawn box reaches further than its members, and a lift cell's envelope than its block)
            mn = Vector3.Min(mn, smn);
            mx = Vector3.Max(mx, smx);
        }
    }

    /// <summary>Part <paramref name="i"/> goes: true when it was the last part on a face of the bounds (they are summed again then).</summary>
    private bool OnBounds(int i)
    {
        StructBounds(i, out var mn, out var mx);
        var last = false;
        for (var a = 0; a < 3; a++)
        {
            if (mn[a] <= _sMin[a] && --_sAt[a] <= 0)
                last = true;
            if (mx[a] >= _sMax[a] && --_sAt[3 + a] <= 0)
                last = true;
        }
        return last;
    }

    /// <summary>Adds (<paramref name="sign"/> 1) or takes off (-1) part <paramref name="i"/>'s share of the sums (its load is not in them: <see cref="FinishMass"/>).</summary>
    private void AddPartSums(int i, double sign)
    {
        RestBounds(i, out var mn, out var mx);
        var pm = PartMass(i) * sign;
        _sM += pm;
        _sC.Add((mn + mx) * 0.5f, pm);
        var d = Parts[i].D;
        var vol = 0f;
        for (var b = 0; b < d.SolidBoxes; b++)
            vol += d.Boxes[b].Half.X * d.Boxes[b].Half.Y * d.Boxes[b].Half.Z;
        var turned = FreeformCount > 0 && Parts[i].Seg != 0;
        var rq = Quaternion.Identity;
        var rt = Vector3.Zero;
        if (turned)
            RestPose(Parts[i].Seg, out rq, out rt);
        for (var b = 0; b < d.SolidBoxes; b++)
        {
            BoxOf(i, b, out var c, out var h);
            var m = PartMass(i) * (d.Boxes[b].Half.X * d.Boxes[b].Half.Y * d.Boxes[b].Half.Z) / MathF.Max(vol, 1e-9f);
            var s = h * 2;
            var r = c;
            float xx = 0, yy = 0, zz = 0, xy = 0, xz = 0, yz = 0;
            if (turned)
            {
                r = rt + Vector3.Transform(c, rq);
                TurnedBox(m, s, rq, ref xx, ref yy, ref zz, ref xy, ref xz, ref yz);
            }
            else
            {
                xx = m / 12f * (s.Y * s.Y + s.Z * s.Z);
                yy = m / 12f * (s.X * s.X + s.Z * s.Z);
                zz = m / 12f * (s.X * s.X + s.Y * s.Y);
            }
            AddBox(m * sign, r, xx * sign, yy * sign, zz * sign, xy * sign, xz * sign, yz * sign);
        }
        // Its segment's own and carried mass (BuildSegMass), at its middle in its segment's frame.
        PartBounds(i, out var pmn, out var pmx);
        SegSums(i, (pmn + pmx) * 0.5f, (pmx - pmn) * 0.5f, PartMass(i), sign, _ssOwnM, _ssOwnC, _ssM, _ssC, _ssSq);
    }

    private void AddBox(double m, Vector3 r, double xx, double yy, double zz, double xy, double xz, double yz)
    {
        _sBm += m;
        _sB.Add(r, m);
        _sXX += m * r.X * r.X;
        _sYY += m * r.Y * r.Y;
        _sZZ += m * r.Z * r.Z;
        _sXY += m * r.X * r.Y;
        _sXZ += m * r.X * r.Z;
        _sYZ += m * r.Y * r.Z;
        _jXX += xx;
        _jYY += yy;
        _jZZ += zz;
        _jXY += xy;
        _jXZ += xz;
        _jYZ += yz;
    }

    /// <summary>
    /// The mass, centre of mass, inertia and bounds of a big body from the sums, the loads (only special parts hold
    /// any), the cargo and the inertia floor: what BuildMass gives, to rounding. O(special parts).
    /// </summary>
    private void FinishMass()
    {
        var mass = _sM;
        var com = _sC;
        var bm = _sBm;
        var b = _sB;
        double sxx = _sXX, syy = _sYY, szz = _sZZ, sxy = _sXY, sxz = _sXZ, syz = _sYZ;
        double jxx = _jXX, jyy = _jYY, jzz = _jZZ, jxy = _jXY, jxz = _jXZ, jyz = _jYZ;
        LoadMass = 0;
        LoadKey = 0;
        LocalMin = _sMin;
        LocalMax = _sMax;
        void Box(double m, Vector3 r, double xx, double yy, double zz, double xy, double xz, double yz, ref double bm, ref Vector3D b,
            ref double sxx, ref double syy, ref double szz, ref double sxy, ref double sxz, ref double syz,
            ref double jxx, ref double jyy, ref double jzz, ref double jxy, ref double jxz, ref double jyz)
        {
            bm += m;
            b.Add(r, m);
            sxx += m * r.X * r.X;
            syy += m * r.Y * r.Y;
            szz += m * r.Z * r.Z;
            sxy += m * r.X * r.Y;
            sxz += m * r.X * r.Z;
            syz += m * r.Y * r.Z;
            jxx += xx;
            jyy += yy;
            jzz += zz;
            jxy += xy;
            jxz += xz;
            jyz += yz;
        }
        foreach (var i in Specials)
        {
            if (!LoadOf(i, out var lm, out var lc, out var lh))
                continue;
            var at = RestLoad(i, lc);
            com.Add(at, lm);
            mass += lm;
            LoadMass += lm;
            LoadKey += LoadKeyOf(i, lm, lc);
            var s = lh * 2;
            float xx = 0, yy = 0, zz = 0, xy = 0, xz = 0, yz = 0;
            if (FreeformCount > 0 && Parts[i].Seg != 0)
            {
                RestPose(Parts[i].Seg, out var rq, out _);
                TurnedBox(lm, s, rq, ref xx, ref yy, ref zz, ref xy, ref xz, ref yz);
            }
            else
            {
                xx = lm / 12f * (s.Y * s.Y + s.Z * s.Z);
                yy = lm / 12f * (s.X * s.X + s.Z * s.Z);
                zz = lm / 12f * (s.X * s.X + s.Y * s.Y);
            }
            Box(lm, at, xx, yy, zz, xy, xz, yz, ref bm, ref b, ref sxx, ref syy, ref szz, ref sxy, ref sxz, ref syz, ref jxx, ref jyy, ref jzz, ref jxy, ref jxz, ref jyz);
        }
        for (var k = 0; k < CargoCount; k++)
        {
            var m = Cargo[k].Mass;
            var s = Cargo[k].Half * 2;
            com.Add(Cargo[k].Centre, m);
            mass += m;
            Box(m, Cargo[k].Centre, m / 12f * (s.Y * s.Y + s.Z * s.Z), m / 12f * (s.X * s.X + s.Z * s.Z), m / 12f * (s.X * s.X + s.Y * s.Y), 0, 0, 0,
                ref bm, ref b, ref sxx, ref syy, ref szz, ref sxy, ref sxz, ref syz, ref jxx, ref jyy, ref jzz, ref jxy, ref jxz, ref jyz);
        }
        Mass = (float)mass;
        Com = com.Over(mass);
        InvMass = 1f / Mass;
        // About the centre of mass: sum m (r - C)(r - C) = S - C B' - B C' + M_b C C'.
        double cx = Com.X, cy = Com.Y, cz = Com.Z;
        var qxx = sxx - 2 * cx * b.X + bm * cx * cx;
        var qyy = syy - 2 * cy * b.Y + bm * cy * cy;
        var qzz = szz - 2 * cz * b.Z + bm * cz * cz;
        var qxy = sxy - cx * b.Y - cy * b.X + bm * cx * cy;
        var qxz = sxz - cx * b.Z - cz * b.X + bm * cx * cz;
        var qyz = syz - cy * b.Z - cz * b.Y + bm * cy * cz;
        var floor = Mass * 0.05f; // (a little extra so thin builds don't spin like tops: BuildMass)
        InvInertiaLocal = Invert3(jxx + qyy + qzz + floor, jyy + qxx + qzz + floor, jzz + qxx + qyy + floor, jxy - qxy, jxz - qxz, jyz - qyz, Matrix4x4.Identity);
        Radius = MathF.Max(Vector3.Distance(Com, LocalMin), Vector3.Distance(Com, LocalMax)) + 0.5f;
    }

    /// <summary>Each segment's carried mass from the sums and the loads (BuildSegMass, for a big body).</summary>
    private void FinishSegMass()
    {
        Span<double> ownM = stackalloc double[MaxJoints + 1];
        Span<double> m = stackalloc double[MaxJoints + 1];
        Span<double> sq = stackalloc double[MaxJoints + 1];
        Span<Vector3D> ownC = stackalloc Vector3D[MaxJoints + 1];
        Span<Vector3D> cc = stackalloc Vector3D[MaxJoints + 1];
        for (var s = 0; s <= JointCount; s++)
        {
            ownM[s] = _ssOwnM[s];
            m[s] = _ssM[s];
            sq[s] = _ssSq[s];
            ownC[s] = _ssOwnC[s];
            cc[s] = _ssC[s];
        }
        foreach (var i in Specials)
            if (LoadOf(i, out var kg, out var at, out var half))
                SegSums(i, at, half, kg, 1, ownM, ownC, m, cc, sq);
        for (var s = 0; s < SegMass.Length; s++)
        {
            SegMass[s] = SegOwnMass[s] = 0;
            SegCom[s] = SegOwnCom[s] = Vector3.Zero;
            _segSq[s] = 0;
        }
        for (var s = 0; s <= JointCount; s++)
        {
            SegMass[s] = (float)m[s];
            SegOwnMass[s] = (float)ownM[s];
            SegCom[s] = m[s] > 0 ? cc[s].Over(m[s]) : Vector3.Zero;
            SegOwnCom[s] = ownM[s] > 0 ? ownC[s].Over(ownM[s]) : Vector3.Zero;
            _segSq[s] = sq[s];
            SegSpin[s] = s == 0 ? 0 : (float)Math.Max(0, _segSq[s] - SegMass[s] * Across(SegCom[s], Joints[s - 1].Axis));
        }
    }


    /// <summary>AddSegMass into double sums (the same walk up the joints, the same frames).</summary>
    private void SegSums(int i, Vector3 at, Vector3 half, float mass, double sign, Span<double> ownM, Span<Vector3D> ownC, Span<double> segM, Span<Vector3D> segC, Span<double> sq)
    {
        var m = mass * sign;
        var c = at;
        var cb = c;
        var pq = Quaternion.Identity;
        var toBox = Quaternion.Identity;
        if (FreeformCount > 0 && Parts[i].Seg != 0)
        {
            RestPose(Parts[i].Seg, out pq, out var pt);
            cb = pt + Vector3.Transform(c, pq);
        }
        for (int s = Parts[i].Seg, guard = 0; guard <= MaxJoints; guard++)
        {
            if (s < 0 || s > JointCount)
                break;
            if (FreeformCount > 0)
            {
                RestPose(s, out var sq2, out var st);
                c = Vector3.Transform(cb - st, Quaternion.Conjugate(sq2));
                toBox = Quaternion.Conjugate(Quaternion.Conjugate(sq2) * pq);
            }
            if (s == Parts[i].Seg)
            {
                ownM[s] += m;
                ownC[s].Add(c, m);
            }
            segM[s] += m;
            segC[s].Add(c, m);
            if (s > 0)
            {
                var axis = Joints[s - 1].Axis;
                var a = Vector3.Transform(axis, toBox);
                sq[s] += m * ((double)Across(c, axis) + (half.X * half.X * (1 - a.X * a.X) + half.Y * half.Y * (1 - a.Y * a.Y) + half.Z * half.Z * (1 - a.Z * a.Z)) / 3);
            }
            if (s == 0)
                break;
            s = Joints[s - 1].ParentSeg;
        }
    }

    // ---- The circuit near a part that goes --------------------------------------------------------------------------

    private int[] _meetStamp = [];
    private int _meetSerial;
    private const int MeetBudget = 512; // parts a look round may visit before it gives up (and the circuit is flooded again)

    /// <summary>
    /// Do the working parts linked to <paramref name="gone"/> still meet each other without it, within a short walk? Then
    /// the circuit (and the body) is whole without it, and nothing else's power changes. False when they don't, or the
    /// walk ran past its budget (a long thin build): flooded again then.
    /// </summary>
    private bool PowerMeetsRound(int gone) => MeetsRound(gone, skipBroken: true);

    /// <summary>
    /// Do all the parts linked to <paramref name="gone"/> meet without it within a short walk over links (any part, broken
    /// or not)? Then taking it off cuts nothing away: the split's flood can be skipped (<see cref="ConstructWorld.RemovePart"/>).
    /// </summary>
    internal bool StructureMeetsRound(int gone) => MeetsRound(gone, skipBroken: false);

    private bool MeetsRound(int gone, bool skipBroken)
    {
        if (_meetStamp.Length < PartCount)
            _meetStamp = new int[Math.Max(PartCount, _meetStamp.Length * 2)];
        if (++_meetSerial == int.MaxValue)
        {
            Array.Clear(_meetStamp);
            _meetSerial = 1;
        }
        var stamp = _meetSerial;
        Span<int> want = stackalloc int[16];
        var wants = 0;
        for (var e = FirstLink(gone); e >= 0; e = NextLink(e))
        {
            var o = OtherEnd(e);
            if (o == gone || skipBroken && Parts[o].Broken)
                continue;
            var dup = false;
            for (var k = 0; k < wants; k++)
                dup |= want[k] == o;
            if (dup)
                continue;
            if (wants == want.Length)
                return false;
            want[wants++] = o;
        }
        if (wants <= 1)
            return true; // (a leaf: nothing hangs on it but itself)
        using var scratch = Scratch.Begin();
        Span<int> queue = scratch.Take<int>(MeetBudget);
        int head = 0, tail = 0;
        _meetStamp[gone] = stamp;
        _meetStamp[want[0]] = stamp;
        queue[tail++] = want[0];
        var found = 1;
        while (head < tail)
        {
            var at = queue[head++];
            for (var e = FirstLink(at); e >= 0; e = NextLink(e))
            {
                var o = OtherEnd(e);
                if (_meetStamp[o] == stamp || skipBroken && Parts[o].Broken)
                    continue;
                _meetStamp[o] = stamp;
                for (var k = 1; k < wants; k++)
                    if (want[k] == o && ++found == wants)
                        return true;
                if (tail == MeetBudget)
                    return false;
                queue[tail++] = o;
            }
        }
        return false;
    }

    /// <summary>The circuit after plain parts came: a new part next to a powered one is powered, and so is what it now reaches.</summary>
    private void PowerAdded(int from)
    {
        if (!HasBattery)
            return;
        using var scratch = Scratch.Begin();
        Span<int> queue = scratch.Take<int>(PartCount);
        int head = 0, tail = 0;
        for (var i = from; i < PartCount; i++)
        {
            Parts[i].Powered = false;
            if (Parts[i].Broken)
                continue;
            for (var e = FirstLink(i); e >= 0; e = NextLink(e))
                if (Parts[OtherEnd(e)].Powered)
                {
                    Parts[i].Powered = true;
                    queue[tail++] = i;
                    break;
                }
        }
        while (head < tail)
        {
            var at = queue[head++];
            for (var e = FirstLink(at); e >= 0; e = NextLink(e))
            {
                var o = OtherEnd(e);
                if (Parts[o].Powered || Parts[o].Broken)
                    continue;
                if (!IsPlainPart(o))
                {
                    _jPower = true; // (it reaches a special part that had no power: flooded again, the honest way)
                    return;
                }
                Parts[o].Powered = true;
                queue[tail++] = o;
            }
        }
    }

    // ---- The rebuild by the journal -------------------------------------------------------------------------------------

    /// <summary>
    /// After plain edits only: what <see cref="Rebuild"/> gives, from the journal. The collider and the part tree are
    /// mapped (or built again if parts came), the sums take the new parts, the systems are built from the special parts
    /// (none of which changed: only their numbers), and the circuit is flooded only if the journal says so.
    /// </summary>
    private void RebuildPlain()
    {
        _dirty = false;
        PlainRuns++;
        RebuildStamp++;
        RideAxes = null;
        var added = _jSurvivors; // (parts from here on came since the last rebuild)
        MapSpecials();
        ColliderJournal();
        TreeJournal();
        if (FuseRefresh())
            _colliderStale = true; // (a fused unit lost members or wants fitting: its rough boxes are made again with the collider, ConstructFuse.cs)
        for (var i = added; i < PartCount; i++)
        {
            AddPartSums(i, 1);
            StructBounds(i, out var mn, out var mx);
            if (!_jBounds)
                GrowBounds(mn, mx);
        }
        if (_jBounds)
            SumBounds();
        if (++_plainCount >= ResumEvery)
            SumParts();
        FinishMass();
        BuildRails(); // (the rails' parts by number)
        ConstructRoutes.Rebuild(this); // (renumbered with the parts: the logic's wires, shafts' and cables' edges)
        ConstructModules.Build(this);
        ConstructTracks.Rebuild(this);
        Radius = MathF.Max(Radius, JointReach() + 0.5f);
        BuildWheels();
        BuildDrivetrain();
        BuildPlants();
        BuildThrusters();
        ConstructFluid.Build(this);
        ConstructLift.Build(this);
        ConstructAero.Build(this);
        ConstructRotor.Build(this);
        ConstructBoost.Build(this);
        ConstructFlight.Build(this);
        ConstructOrdnance.Build(this);
        ConstructConveyor.BuildRacks(this);
        ConstructHydraulics.Build(this);
        ConstructSeal.Build(this);
        BuildSegments();
        if (!_jPower && added < PartCount)
            PowerAdded(added);
        if (_jPower)
            ConstructPower.Rebuild(this);
        StoreNetKey = ConstructBeacons.KeyOf(this);
        ConstructWeapons.BuildGuns(this);
        CountSpecials();
        BuildFreeChain();
        Root = PickRoot();
        FinishRebuild();
        JournalReset();
    }
}

public sealed partial class Construct
{
    // ---- The collider and the part tree through the journal ------------------------------------------------------

    private int _colliderSerial = -1, _treeSerial = -1;

    /// <summary>The collider as it was at the last rebuild, renumbered and without the parts that went; built again (when next asked) if parts came or it wasn't current.</summary>
    private void ColliderJournal()
    {
        if (_collider == null || _colliderStale)
        {
            _colliderStale = true;
            return;
        }
        if (_colliderSerial != _serialAtRebuild || _jMapped && !_collider.Remap(_jMap, _jBase) || _jAdded && !_collider.AddParts(this, _jSurvivors, PartCount))
        {
            _colliderStale = true; // (built at another point of the journal, too many holes, or no room for what came)
            return;
        }
        _colliderSerial = _editSerial; // (current again: as of this rebuild)
    }

    /// <summary>The part tree likewise: renumbered, the parts that went left out; parts that came are handed back as loose (as before).</summary>
    private void TreeJournal()
    {
        if (_treeParts < 0)
            return;
        if (_treeSerial != _serialAtRebuild || _treeParts > _jBase)
        {
            TreeStale();
            return;
        }
        if (!_jMapped)
        {
            _treeSerial = _editSerial; // (only adds: they are handed back loose, as before)
            return;
        }
        var dead = 0;
        for (var k = 0; k < _treeOrderCount; k++)
        {
            var i = _treeOrder[k];
            _treeOrder[k] = i < 0 ? -1 : _jMap[i];
            if (_treeOrder[k] < 0)
                dead++;
        }
        for (var k = 0; k < _treeLooseCount; k++)
            if ((_treeLoose[k] = _jMap[_treeLoose[k]]) < 0)
            {
                TreeStale(); // (a loose part went: not by the journal, but safe)
                return;
            }
        var inTree = 0;
        for (var k = 0; k < _treeParts; k++)
        {
            var to = _jMap[k];
            if (to < 0)
                continue;
            inTree++;
            if (to != k)
            {
                _partMin[to] = _partMin[k]; // (the map only moves parts down: copying upward in order is safe)
                _partMax[to] = _partMax[k];
            }
        }
        _treeParts = inTree; // (the parts added since it was built still come after these: handed back loose)
        _treeDead += dead;
        _treeSerial = _editSerial;
        if (_treeDead * 4 > _treeOrderCount)
            TreeStale();
    }
}

internal sealed partial class ConstructCollider
{
    private int _dead;

    /// <summary>
    /// The leaves renumbered by <paramref name="map"/> (old part index -> new, -1 gone; <paramref name="count"/> old
    /// parts), each gone part's leaves taken out of its node (the bounds stay as they were: a little loose, never short).
    /// False when that isn't safe (a group's root would empty, a quarter of the leaves are gone): build it again then.
    /// </summary>
    public bool Remap(int[] map, int count)
    {
        for (var g = 0; g < GroupCount; g++)
        {
            ref var root = ref Nodes[Groups[g].Root];
            if (root.Count > 0)
                for (var k = root.Start; k < root.Start + root.Count; k++)
                    if (Leaves[k].Part >= count || map[Leaves[k].Part] < 0)
                        return false;
        }
        for (var n = 0; n < NodeCount; n++)
        {
            ref var node = ref Nodes[n];
            if (node.Count <= 0)
                continue;
            var keep = node.Start;
            for (var k = node.Start; k < node.Start + node.Count; k++)
            {
                var p = Leaves[k].Part;
                var to = p < count ? map[p] : -1;
                if (to < 0)
                {
                    _dead++;
                    continue;
                }
                var leaf = Leaves[k];
                leaf.Part = (ushort)to;
                Leaves[keep++] = leaf;
            }
            for (var k = keep; k < node.Start + node.Count; k++)
                Leaves[k].Dead = true; // (a hole in its group's run: the walks over a group skip it)
            node.Count = keep - node.Start;
            if (node.Count == 0)
            {
                node.Centre = Vector3.Zero;
                node.Half = new Vector3(-1e30f); // (an empty node: no box test passes it)
            }
        }
        return _dead * 4 <= LeafCount;
    }
}

public sealed partial class Construct
{
    // ---- The debug check: a journalled rebuild against a full one ------------------------------------------------

    /// <summary>What a rebuild gave, for <see cref="RebuildCheck"/> (allocates: a debug tool).</summary>
    private sealed class Seen
    {
        public float Mass, Radius;
        public Vector3 Com, Min, Max;
        public Matrix4x4 Inv;
        public float[] SegM = [], SegOwn = [];
        public Vector3[] SegC = [];
        public bool[] Powered = [];
        public bool HasBattery, CanDrive, Anchored;
        public float Charge;
        public int Engine, Transmission, Root, Guns, Wheels, Specials;
        public (int Part, bool Driven, int Group, float Ratio)[] WheelRows = [];
        public int[] SpecialList = [];
        public List<(int Part, Vector3 C, Vector3 H)>? Leaves;
        public List<int>? Hits;
        public int TreeMissed;
    }

    /// <summary>Leaves a box query through each group's tree missed, against a walk over the group's leaves (must be none).</summary>
    private int ColliderProbe()
    {
        var col = Collider;
        var missed = 0;
        var rng = new Random(PartCount + 7);
        var into = new int[col.LeafCount + 8];
        for (var g = 0; g < col.GroupCount; g++)
            for (var q = 0; q < 6; q++)
            {
                var root = col.Nodes[col.Groups[g].Root];
                var at = root.Centre + root.Half * new Vector3(rng.NextSingle() * 2 - 1, rng.NextSingle() * 2 - 1, rng.NextSingle() * 2 - 1);
                var half = new Vector3(1.2f);
                var n = col.Collect(g, at, half, into);
                var got = new HashSet<int>(into.Take(n));
                for (var k = col.Groups[g].First; k < col.Groups[g].First + col.Groups[g].Count; k++)
                {
                    ref var l = ref col.Leaves[k];
                    if (l.Dead || l.Turned)
                        continue;
                    var d = Vector3.Abs(l.Centre - at) - l.Half - half;
                    if (d.X <= 0 && d.Y <= 0 && d.Z <= 0 && !got.Contains(k))
                        missed++;
                }
                foreach (var k in got)
                    if (col.Leaves[k].Dead)
                        missed++; // (a hole handed back)
            }
        return missed;
    }

    private Seen CheckTake()
    {
        var s = new Seen
        {
            Mass = Mass, Radius = Radius, Com = Com, Min = LocalMin, Max = LocalMax, Inv = InvInertiaLocal,
            SegM = SegMass[..(JointCount + 1)], SegOwn = SegOwnMass[..(JointCount + 1)], SegC = SegCom[..(JointCount + 1)],
            HasBattery = HasBattery, CanDrive = CanDrive, Anchored = Anchored, Charge = ChargeCapacity, Engine = Engine,
            Transmission = Transmission, Root = Root, Guns = GunCount, Wheels = WheelCount, Specials = SpecialCount,
            SpecialList = Specials.ToArray(),
        };
        s.Powered = new bool[PartCount];
        for (var i = 0; i < PartCount; i++)
            s.Powered[i] = Parts[i].Powered;
        s.WheelRows = new (int, bool, int, float)[WheelCount];
        for (var k = 0; k < WheelCount; k++)
            s.WheelRows[k] = (Wheels[k].Part, Wheels[k].Driven, Wheels[k].Group, Wheels[k].Ratio);
        if (_collider != null && !_colliderStale)
        {
            s.Leaves = LeafSet();
            s.TreeMissed = ColliderProbe();
        }
        s.Hits = TreeProbe();
        return s;
    }

    private List<(int, Vector3, Vector3)> LeafSet()
    {
        var col = Collider;
        var list = new List<(int, Vector3, Vector3)>();
        for (var g = 0; g < col.GroupCount; g++)
            for (var k = col.Groups[g].First; k < col.Groups[g].First + col.Groups[g].Count; k++)
                if (!col.Leaves[k].Dead)
                    list.Add((col.Leaves[k].Part, col.Leaves[k].Centre, col.Leaves[k].Half));
        list.Sort((a, b) => a.Item1 != b.Item1 ? a.Item1.CompareTo(b.Item1) : a.Item2.X != b.Item2.X ? a.Item2.X.CompareTo(b.Item2.X) : a.Item2.Y != b.Item2.Y ? a.Item2.Y.CompareTo(b.Item2.Y)
            : a.Item2.Z != b.Item2.Z ? a.Item2.Z.CompareTo(b.Item2.Z) : a.Item3.X != b.Item3.X ? a.Item3.X.CompareTo(b.Item3.X) : a.Item3.Y != b.Item3.Y ? a.Item3.Y.CompareTo(b.Item3.Y) : a.Item3.Z.CompareTo(b.Item3.Z));
        return list;
    }

    /// <summary>Which parts a few boxes over the body meet, by the tree, each checked against every part's bounds (what the tree must never miss).</summary>
    private List<int> TreeProbe()
    {
        var hits = new List<int>();
        var into = new int[MaxParts];
        var rng = new Random(PartCount);
        for (var q = 0; q < 8; q++)
        {
            var at = LocalMin + (LocalMax - LocalMin) * new Vector3(rng.NextSingle(), rng.NextSingle(), rng.NextSingle());
            var half = new Vector3(1.5f);
            var n = PartsInBox(at - half, at + half, into);
            var got = new HashSet<int>(into.Take(n));
            for (var i = 0; i < PartCount; i++)
            {
                RestBounds(i, out var mn, out var mx);
                var touches = mn.X <= at.X + half.X && mx.X >= at.X - half.X && mn.Y <= at.Y + half.Y && mx.Y >= at.Y - half.Y && mn.Z <= at.Z + half.Z && mx.Z >= at.Z - half.Z;
                if (touches && Rigid(Parts[i].Seg))
                {
                    hits.Add(i);
                    if (!got.Contains(i))
                        hits.Add(-1 - i); // (missed: always a difference)
                }
            }
        }
        return hits;
    }

    private void CheckAgainst(Seen a)
    {
        CheckRuns++;
        var b = CheckTake();
        var bad = new List<string>();
        static bool Near(float x, float y, float rel, float abs) => MathF.Abs(x - y) <= abs + rel * MathF.Max(MathF.Abs(x), MathF.Abs(y));
        static bool NearV(Vector3 x, Vector3 y, float abs) => Vector3.Distance(x, y) <= abs;
        if (!Near(a.Mass, b.Mass, 1e-5f, 1e-3f))
            bad.Add($"mass {a.Mass} vs {b.Mass}");
        if (!NearV(a.Com, b.Com, 1e-3f))
            bad.Add($"centre of mass {a.Com} vs {b.Com}");
        if (!NearV(a.Min, b.Min, 1e-4f) || !NearV(a.Max, b.Max, 1e-4f))
            bad.Add($"bounds {a.Min}-{a.Max} vs {b.Min}-{b.Max}");
        if (!Near(a.Radius, b.Radius, 1e-4f, 1e-3f))
            bad.Add($"radius {a.Radius} vs {b.Radius}");
        var big = 0f;
        for (var r = 0; r < 3; r++)
            for (var c = 0; c < 3; c++)
                big = MathF.Max(big, MathF.Abs(b.Inv[r, c]));
        for (var r = 0; r < 3; r++)
            for (var c = 0; c < 3; c++)
                if (MathF.Abs(a.Inv[r, c] - b.Inv[r, c]) > 1e-3f * big)
                    bad.Add($"inverse inertia [{r},{c}] {a.Inv[r, c]} vs {b.Inv[r, c]}");
        for (var s = 0; s < a.SegM.Length; s++)
            if (!Near(a.SegM[s], b.SegM[s], 1e-5f, 1e-3f) || !Near(a.SegOwn[s], b.SegOwn[s], 1e-5f, 1e-3f) || !NearV(a.SegC[s], b.SegC[s], 1e-3f))
                bad.Add($"segment {s}: {a.SegM[s]} at {a.SegC[s]} vs {b.SegM[s]} at {b.SegC[s]}");
        if (!a.Powered.AsSpan().SequenceEqual(b.Powered))
            bad.Add($"circuit: {a.Powered.Count(x => x)} powered vs {b.Powered.Count(x => x)}");
        if (a.HasBattery != b.HasBattery || a.Charge != b.Charge)
            bad.Add("batteries");
        if (a.CanDrive != b.CanDrive || a.Engine != b.Engine || a.Transmission != b.Transmission)
            bad.Add($"drive line: {a.CanDrive} {a.Engine} {a.Transmission} vs {b.CanDrive} {b.Engine} {b.Transmission}");
        if (a.Wheels != b.Wheels || !a.WheelRows.SequenceEqual(b.WheelRows))
            bad.Add("wheels");
        if (a.Root != b.Root || a.Guns != b.Guns || a.Anchored != b.Anchored)
            bad.Add($"root {a.Root}/{b.Root}, guns {a.Guns}/{b.Guns}, anchored {a.Anchored}/{b.Anchored}");
        if (!a.SpecialList.AsSpan().SequenceEqual(b.SpecialList))
            bad.Add($"special parts {a.Specials} vs {b.Specials}: only journalled [{string.Join(",", a.SpecialList.Except(b.SpecialList).Select(i => i >= 0 && i < PartCount ? $"{i} {Parts[i].D.Name} seg {Parts[i].Seg} store {Parts[i].Store} merge {MergeOf[i]} route {OnRoute(i)} plaindef {PlainParts.Of(Parts[i].D)} broken {Parts[i].Broken}" : $"{i}"))}] only full [{string.Join(",", b.SpecialList.Except(a.SpecialList))}]");
        if (a.Leaves != null)
        {
            var full = LeafSet();
            if (a.Leaves.Count != full.Count || a.Leaves.Zip(full).Any(p => p.First.Part != p.Second.Item1 || !NearV(p.First.C, p.Second.Item2, 1e-4f) || !NearV(p.First.H, p.Second.Item3, 1e-4f)))
            {
                var at = a.Leaves.Zip(full).Select((p, k) => (p, k)).First(x => x.p.First.Part != x.p.Second.Item1 || !NearV(x.p.First.C, x.p.Second.Item2, 1e-4f) || !NearV(x.p.First.H, x.p.Second.Item3, 1e-4f));
                bad.Add($"collider: {a.Leaves.Count} leaves vs {full.Count}; at {at.k}: {at.p.First} vs {at.p.Second}");
            }
        }
        if (a.TreeMissed > 0)
            bad.Add($"collider tree: {a.TreeMissed} leaves missed by a query");
        if (a.Hits!.Any(h => h < 0) || !a.Hits!.SequenceEqual(b.Hits!))
            bad.Add($"part tree: {a.Hits!.Count(h => h < 0)} missed");
        if (bad.Count == 0)
            return;
        CheckFails++;
        if (CheckLog.Count < 64)
            CheckLog.Add($"construct {Id} ({PartCount} parts): {string.Join("; ", bad)}");
    }
}
