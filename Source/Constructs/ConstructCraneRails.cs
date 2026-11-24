using System.Numerics;

namespace CallOfSlop;

/// <summary>A body's Bridge Cranes' reach, worked out from its Crane Rails when its parts change (ConstructCraneRails.cs).</summary>
public sealed partial class Construct
{
    internal int CraneStamp = -1, CranePartCount = -1;
    internal (ushort Part, LifterDef Def)[] CraneDefs = [];
    internal int CraneN;
    internal long CraneKey = -1; // (what the walk read: its rails' and bridges' places; a stamp bumped by a shape refresh alone walks nothing)
}

/// <summary>
/// The Bridge Crane's rails (docs/sandbox/design/arms-and-cranes.md 3.4, phase 6). A crane is Crane Rail sections end to end in two
/// runs a span apart, and a Crane Bridge (the lifter part, kind "bridge") bolted to the outer side of one run: its frame's +x crosses
/// the span, +z runs along the rails, its Base is the near rail's top line. Sections join by where they lie, not by their links: in
/// line within 2 cm and 3 degrees, end to end within 2 cm, at most 64 a run, the same size class. The far run is the one whose top
/// line lies a span across within 10 cm. The reach along the rails is what both runs share. Worked out on each peer on a rebuild of
/// a body with a bridge (nothing saved or sent); nothing for a body without one. A bridge whose rails are gone has no reach: it
/// stops where it is with the reason on its look line, and its piece stays in its hand.
/// </summary>
public static partial class ConstructLifters
{
    public const int MaxRailSections = 64;
    public const int MaxRailParts = 512; // rail parts a crane's walk lists: its near run and what lies across its span
    public const float RailLine = 0.02f, RailEnds = 0.02f, RailDeg = 3f, FarRail = 0.1f;
    public const float MaxRun = 300f; // along the rails from the bridge (the lift word's cm fit in a short)

    /// <summary>
    /// The lifter def a part works by: the part file's own for an arm; for a crane, a copy with its rails' reach in it (made again when
    /// the body's parts change). Null: not a lifter.
    /// </summary>
    public static LifterDef? DefOf(Construct c, int part)
    {
        var ld = c.DefOf(part).Lifter;
        if (ld is { Kit: true })
            return ArmKit.DefOf(c, part, ld);
        if (ld is not { Bridge: true })
            return ld;
        if (c.CraneStamp != c.StructStamp || c.CranePartCount != c.PartCount)
        {
            c.CraneStamp = c.StructStamp;
            c.CranePartCount = c.PartCount;
            var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            var key = CraneKeyOf(c);
            var t1 = System.Diagnostics.Stopwatch.GetTimestamp();
            LifterProbe.Cost(ref LifterProbe.RailKeyCost, t1 - t0); // (phase 7: the key's cost on each rebuild of a body with a bridge, and the walk's when it runs)
            if (key != c.CraneKey)
            {
                c.CraneKey = key;
                Cranes(c);
                LifterProbe.Cost(ref LifterProbe.RailWalkCost, System.Diagnostics.Stopwatch.GetTimestamp() - t1);
            }
        }
        for (var k = 0; k < c.CraneN; k++)
            if (c.CraneDefs[k].Part == part)
                return c.CraneDefs[k].Def;
        return ld; // (its own def has RailWhy 1: no reach)
    }

    /// <summary>
    /// A Crane Rail Block's lamp (drawing 24): does it flash now? While a crane on its body moves (carrying or going back) with its bridge
    /// within <see cref="LampNear"/> of the block's middle along the rails, on for half of each third of a second. Drawing only, either peer;
    /// allocates nothing.
    /// </summary>
    public static bool RailLit(ConstructWorld world, Construct c, int rail, double now)
    {
        if (c.CraneN == 0 || now * 3 % 1 > 0.5)
            return false;
        var d = c.DefOf(rail);
        Freeform.PartWorld(c, rail, out var rp, out var rr);
        var mid = rp + Vector3.Transform((d.BoundsMin + d.BoundsMax) * 0.5f, rr);
        for (var k = 0; k < c.CraneN; k++)
        {
            var (part, ld) = c.CraneDefs[k];
            if (ld.RailWhy != 0 || Find(world, c.Id, part) is not { State: LiftState.Carry or LiftState.Return, Moved: true } l)
                continue;
            Freeform.PartWorld(c, part, out var bp, out var br);
            var at = Vector3.Transform(mid - bp, Quaternion.Conjugate(br));
            var hookAge = AgeNow(l);
            var hook = Pose(ld, l, hookAge, out var hookRot);
            if (Fetching(l, hookAge))
                hook = HandPose(ld, l, hookAge); // (the bridge goes to fetch the piece, which still lies where it was)
            else if (l.Def != 0 && ConstructParts.Get(l.Def) is { } hd)
                hook += Vector3.Transform((hd.BoundsMin + hd.BoundsMax) * 0.5f, hookRot); // (the bridge is over the piece's middle, not its origin)
            if (MathF.Abs(at.Z - hook.Z) < LampNear && at.X > ld.Base.X - 1.5f && at.X < ld.Base.X + ld.Span + 1.5f)
                return true;
        }
        return false;
    }

    public const float LampNear = 3f;
    public const float HallBridgeTop = 1f; // m over the rails' top: the bridge's end trucks and cab roof (what a part over the span must clear)

    /// <summary>The window tool's "lifter cranepoints": the points a crane reaches, and each nearby part's middle in its box's frame.</summary>
    public static string CranePoints(ConstructWorld world, Construct c, int part)
    {
        if (DefOf(c, part) is not { Bridge: true } ld)
            return "not a crane";
        var pts = new LiftPoint[MaxPoints];
        var n = 0;
        Frame(c, part, ld, out _, out var lr, out var shoulder);
        var s = "";
        foreach (var b in world.Bodies)
        {
            if (Vector3.Distance(b.Pos, shoulder) > ld.ReachMax + ld.Up + ld.Down + b.Radius)
                continue;
            PointsOn(b, b == c ? part : -1, ld, shoulder, lr, pts, ref n);
            if (b != c)
            {
                var d = Vector3.Transform(b.Pos - shoulder, Quaternion.Conjugate(lr));
                s += $" [{b.DefOf(0).Name} body {b.Id} at {d.X:0.0},{d.Y:0.0},{d.Z:0.0}]";
            }
        }
        for (var k = 0; k < n; k++)
            s += $" point {pts[k].Kind} {world.Get(pts[k].Body)?.DefOf(pts[k].Part).Name}";
        if (Find(world, c.Id, part) is { State: LiftState.Carry or LiftState.Return } hl)
        {
            var hand = Pose(ld, hl, AgeAt(hl, Now), out _);
            s += $" hand at {hand.Y - ld.Base.Y:0.0} m under the rails' top ({AgeAt(hl, Now):0}/{hl.Dur} ticks; from {hl.From.Y - ld.Base.Y:0.0}, to {hl.To.Y - ld.Base.Y:0.0}, rise {hl.Rise:0.0})";
        }
        return $"{n} points; box x {ld.AcrossMin:0.0}..{ld.AcrossMax:0.0} z {ld.RunLo:0.0}..{ld.RunHi:0.0} y -{ld.Down:0.0}..-{ld.Depth:0.0};{s}";
    }

    /// <summary>Is a point of a crane's move (its part frame) past the rails it has now (a section taken out mid-move)?</summary>
    private static bool OffRails(LifterDef ld, CLift l, bool to)
    {
        // (the piece's middle, as its reach was tested: InReach takes a part's middle; an empty hand's point is its own)
        var p = to ? l.To : l.From;
        if (l.Def != 0 && !(to && l.Step >= 0 && !l.ToHold) && ConstructParts.Get(l.Def) is { } d) // (a fit's place was tested at its origin: StationJob plan points)
            p += Vector3.Transform((d.BoundsMin + d.BoundsMax) * 0.5f, to ? l.ToRot : l.FromRot);
        return p.Z - ld.Base.Z < ld.RunLo - 0.05f || p.Z - ld.Base.Z > ld.RunHi + 0.05f;
    }

    /// <summary>A hash of what a crane's reach depends on: each rail's and bridge's index, place, turn, segment and whether it is whole (no allocation).</summary>
    private static long CraneKeyOf(Construct c)
    {
        var h = 1000003L; // (not the part count: an Extruder's growing piece on the body changed it every cut, and each change walked the rails again: lifters phase 7)
        var fixedParts = 0L; // (a Hall Crane's drop and its clearance check read every fixed part under it: their ids and places, never a run-time piece's)
        for (var i = 0; i < c.PartCount; i++)
        {
            var d = c.DefOf(i);
            if (!d.RuntimeMade)
                fixedParts = fixedParts * 17 + HashCode.Combine(d.Id, i, c.Parts[i].Pos, c.Parts[i].Rot, c.Parts[i].Broken); // (turned or broken in place: the drop and the clearance change)
            if (!d.CraneRail && d.Lifter is not { Bridge: true })
                continue;
            ref readonly var p = ref c.Parts[i];
            var x = HashCode.Combine(i, d.Id, p.Pos, p.Rot, p.Seg, p.Broken);
            h = h * 31 + x;
        }
        return h * 31 + fixedParts;
    }

    private static void Cranes(Construct c)
    {
        var n = 0;
        for (var i = 0; i < c.PartCount; i++)
            if (c.DefOf(i).Lifter is { Bridge: true })
                n++;
        if (c.CraneDefs.Length < n)
            c.CraneDefs = new (ushort, LifterDef)[n];
        c.CraneN = 0;
        for (var i = 0; i < c.PartCount && c.CraneN < n; i++)
        {
            if (c.DefOf(i).Lifter is not { Bridge: true } own)
                continue;
            var copy = c.CraneDefs[c.CraneN].Def is { } old && old.Of == own && c.CraneDefs[c.CraneN].Part == i ? old : own.CraneCopy();
            copy.Of = own;
            Walk(c, i, own, copy);
            c.CraneDefs[c.CraneN++] = ((ushort)i, copy);
        }
    }

    /// <summary>A crane's near run, far run and the reach box they give, into <paramref name="copy"/>.</summary>
    private static void Walk(Construct c, int part, LifterDef own, LifterDef copy)
    {
        copy.RailWhy = 1;
        copy.RunLo = copy.RunHi = 0;
        copy.ReachMin = 0;
        copy.ReachMax = own.Span;
        copy.Up = 0;
        if (c.Parts[part].Broken)
            return;
        var cls = c.DefOf(part).Class;
        Freeform.PartWorld(c, part, out var bp, out var br);
        var inv = Quaternion.Conjugate(br);
        // Each section of this class in the bridge's frame: its top line's x and y, and its ends along z (those along +z only).
        // (room for two runs of a works' 158 m: 2026-11-23, the list held 128 and a bay crane's far run was cut off at 98 m)
        Span<float> x = stackalloc float[MaxRailParts], y = stackalloc float[MaxRailParts];
        Span<float> z0 = stackalloc float[MaxRailParts], z1 = stackalloc float[MaxRailParts];
        var m = 0;
        var cos = MathF.Cos(RailDeg * MathF.PI / 180f);
        for (var i = 0; i < c.PartCount && m < x.Length; i++)
        {
            var d = c.DefOf(i);
            if (!d.CraneRail || d.Class != cls || d.HallRail != own.Hall || c.Parts[i].Broken) // (a Small Crane Bridge and a Crane Core are both Small: each keeps to its own rails)
                continue;
            Freeform.PartWorld(c, i, out var rp, out var rr);
            var q = inv * rr;
            if (MathF.Abs(Vector3.Transform(Vector3.UnitZ, q).Z) < cos)
                continue;
            var mid = new Vector3((d.BoundsMin.X + d.BoundsMax.X) * 0.5f, d.BoundsMax.Y, 0);
            var a = Vector3.Transform(rp + Vector3.Transform(mid with { Z = d.BoundsMin.Z }, rr) - bp, inv) - own.Base;
            var b = Vector3.Transform(rp + Vector3.Transform(mid with { Z = d.BoundsMax.Z }, rr) - bp, inv) - own.Base;
            var ax = (a.X + b.X) * 0.5f;
            if (ax < -1f || ax > (own.Hall ? HallSpan : own.Span) + 1f)
                continue; // (only its near run, at x 0, and what lies across its span count: a works with several spans has far more rail blocks than the list holds, and the later cranes found none of theirs)
            (x[m], y[m], z0[m], z1[m]) = (ax, (a.Y + b.Y) * 0.5f, MathF.Min(a.Z, b.Z), MathF.Max(a.Z, b.Z));
            m++;
        }
        if (!Run(x[..m], y[..m], z0[..m], z1[..m], 0, 0, FarRail, 0, out var n0, out var n1, out var nx, out var ny))
            return; // (not on a crane rail)
        copy.RailWhy = 2;
        var span = own.Span;
        if (own.Hall)
        {
            // A Hall Crane's span is the gap to the nearest parallel run across (+x), at the same height, along the near run, within HallSpan.
            span = float.MaxValue;
            for (var k = 0; k < m; k++)
                if (MathF.Abs(y[k] - ny) <= FarRail && x[k] - nx > HallMinSpan && x[k] - nx <= HallSpan + FarRail && z1[k] > n0 && z0[k] < n1)
                    span = MathF.Min(span, x[k] - nx);
            if (span == float.MaxValue)
                return;
        }
        if (!Run(x[..m], y[..m], z0[..m], z1[..m], nx + span, ny, FarRail, (n0 + n1) * 0.5f, out var f0, out var f1, out var fx, out _, n0, n1))
            return; // (its far end has no rail)
        var lo = Math.Max(Math.Max(n0, f0), -MaxRun);
        var hi = Math.Min(Math.Min(n1, f1), MaxRun);
        if (hi - lo < 0.5f)
            return;
        copy.RailWhy = 0;
        (copy.RunLo, copy.RunHi) = (lo, hi);
        if (own.Hall)
            HallWalk(c, part, own, copy, x[..m], z0[..m], z1[..m], nx, fx, lo, hi);
        var zz = MathF.Max(MathF.Abs(lo), MathF.Abs(hi));
        copy.ReachMax = MathF.Sqrt(copy.Span * copy.Span + zz * zz); // (the box's horizontal radius: what the distance cuts read)
    }

    public const float HallSpan = 32f, HallMinSpan = 3.5f; // a Hall Crane's widest span (16 cells: drawing 24) and its narrowest (one free cell between two rails)
    public const float HallLift = 20000f, HallLiftPerBlock = 5000f; // kg: 20 t plus 5 t a rail block (drawing 24)
    public const float HallMargin = 1f; // the hook's nearest to either rail's top line

    /// <summary>
    /// A Hall Crane's numbers from its rails (design page drawing 24): the span is the gap between its runs, the reach across stops
    /// <see cref="HallMargin"/> short of each, the lift is <see cref="HallLift"/> plus <see cref="HallLiftPerBlock"/> a rail block in both
    /// runs, the hook drops to the lowest foot of what stands under the rails (the masts' feet: the floor), and every other part under the
    /// span must stay under the hook's highest point: the first that doesn't stops it (RailWhy 3, named on its screen). Rebuild only.
    /// </summary>
    private static void HallWalk(Construct c, int part, LifterDef own, LifterDef copy, Span<float> x, Span<float> z0, Span<float> z1, float nx, float fx, float lo, float hi)
    {
        var span = fx - nx;
        copy.Span = span;
        copy.AcrossMin = HallMargin;
        copy.AcrossMax = MathF.Max(HallMargin + 0.5f, span - HallMargin);
        var blocks = 0;
        for (var k = 0; k < x.Length; k++)
            if ((MathF.Abs(x[k] - nx) <= RailLine || MathF.Abs(x[k] - fx) <= RailLine) && z1[k] > lo + RailEnds && z0[k] < hi - RailEnds)
                blocks++;
        copy.Payload = MathF.Min(1e7f, HallLift + HallLiftPerBlock * blocks);
        copy.PieceAcross = MathF.Max(0.5f, copy.AcrossMax - copy.AcrossMin);
        copy.PieceLong = MathF.Max(copy.PieceAcross, MathF.Min(64f, hi - lo));
        // The floor under it and anything standing up into the bridge's way, in the bridge's frame from the near rail's top line.
        Freeform.PartWorld(c, part, out var bp, out var br);
        var inv = Quaternion.Conjugate(br);
        float floor = 0;
        string? blocker = null;
        var blockerTop = float.MinValue;
        Span<Vector3> corner = stackalloc Vector3[8];
        for (var i = 0; i < c.PartCount; i++)
        {
            var d = c.DefOf(i);
            if (i == part || d.RuntimeMade || c.Parts[i].Broken)
                continue;
            Freeform.PartWorld(c, i, out var pp, out var pr);
            Vector3 mn = new(float.MaxValue), mx = new(float.MinValue);
            for (var k = 0; k < 8; k++)
            {
                var v = new Vector3((k & 1) == 0 ? d.BoundsMin.X : d.BoundsMax.X, (k & 2) == 0 ? d.BoundsMin.Y : d.BoundsMax.Y, (k & 4) == 0 ? d.BoundsMin.Z : d.BoundsMax.Z);
                var q = Vector3.Transform(pp + Vector3.Transform(v, pr) - bp, inv) - own.Base;
                mn = Vector3.Min(mn, q);
                mx = Vector3.Max(mx, q);
            }
            if (mx.Z <= lo || mn.Z >= hi || mx.X <= -1f || mn.X >= span + 1f)
                continue; // (not under the rails)
            if (d.BlockRole == BlockRole.Shared)
                floor = MathF.Min(floor, mn.Y); // (the masts' feet: the floor the rails stand on; with no mast, the file's drop)
            if (d.CraneRail || d.BlockRole == BlockRole.Shared || d.Lifter is { Bridge: true })
                continue; // (the rails, their masts and the cores: what the crane stands on)
            if (mx.X <= HallMargin * 0.5f || mn.X >= span - HallMargin * 0.5f)
                continue; // (beside the runs, outside the span)
            if (mx.Y > -own.Depth + 0.01f && mn.Y < HallBridgeTop && mx.Y > blockerTop) // (up into the band the bridge sweeps: a roof or a lamp above it is no blocker)
                (blocker, blockerTop) = (d.Name, mx.Y);
        }
        copy.Down = floor < 0 ? Math.Clamp(-floor, own.Depth + 1f, 64f) : own.Down;
        copy.Blocker = blocker;
        if (blocker != null)
            copy.RailWhy = 3;
    }

    /// <summary>
    /// The run whose top line is at (<paramref name="atX"/>, <paramref name="atY"/>) within <paramref name="tol"/>: the section nearest
    /// <paramref name="atZ"/> (or overlapping [<paramref name="wantLo"/>, <paramref name="wantHi"/>]) and every section in line end to end with it.
    /// </summary>
    /// <summary>The run through (<paramref name="atX"/>, <paramref name="atY"/>) nearest <paramref name="atZ"/>: its ends (an arm kit's Rail Carriage, ArmKit.Carry).</summary>
    internal static bool RunOf(Span<float> x, Span<float> y, Span<float> z0, Span<float> z1, float atX, float atY, float tol, float atZ, out float lo, out float hi)
        => Run(x, y, z0, z1, atX, atY, tol, atZ, out lo, out hi, out _, out _);

    private static bool Run(Span<float> x, Span<float> y, Span<float> z0, Span<float> z1, float atX, float atY, float tol, float atZ,
        out float lo, out float hi, out float lineX, out float lineY, float wantLo = float.NaN, float wantHi = float.NaN)
    {
        lo = hi = lineX = lineY = 0;
        var seed = -1;
        var best = float.MaxValue;
        for (var k = 0; k < x.Length; k++)
        {
            if (MathF.Abs(x[k] - atX) > tol || MathF.Abs(y[k] - atY) > tol)
                continue;
            float dist;
            if (float.IsNaN(wantLo))
                dist = atZ < z0[k] - tol ? z0[k] - atZ : atZ > z1[k] + tol ? atZ - z1[k] : 0;
            else
                dist = z1[k] < wantLo || z0[k] > wantHi ? float.MaxValue / 2 : MathF.Abs((z0[k] + z1[k]) * 0.5f - atZ);
            if (dist < best && (dist <= tol || !float.IsNaN(wantLo)))
                (best, seed) = (dist, k);
        }
        if (seed < 0)
            return false;
        (lo, hi, lineX, lineY) = (z0[seed], z1[seed], x[seed], y[seed]);
        // Grow both ways: a section in line with the seed's line whose end meets the run's end. At most one pass a section.
        for (var grew = true; grew;)
        {
            grew = false;
            for (var k = 0; k < x.Length; k++)
            {
                if (MathF.Abs(x[k] - lineX) > RailLine || MathF.Abs(y[k] - lineY) > RailLine)
                    continue;
                if (MathF.Abs(z1[k] - lo) <= RailEnds && z0[k] < lo - RailEnds)
                    (lo, grew) = (z0[k], true);
                else if (MathF.Abs(z0[k] - hi) <= RailEnds && z1[k] > hi + RailEnds)
                    (hi, grew) = (z1[k], true);
            }
        }
        return true;
    }
}
