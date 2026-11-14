using System.Numerics;

namespace CallOfSlop;

/// <summary>A body's Bridge Cranes' reach, worked out from its Crane Rails when its parts change (ConstructCraneRails.cs).</summary>
public sealed partial class Construct
{
    internal int CraneStamp = -1, CranePartCount = -1;
    internal (ushort Part, LifterDef Def)[] CraneDefs = [];
    internal int CraneN;
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
    public const float RailLine = 0.02f, RailEnds = 0.02f, RailDeg = 3f, FarRail = 0.1f;
    public const float MaxRun = 300f; // along the rails from the bridge (the lift word's cm fit in a short)

    /// <summary>
    /// The lifter def a part works by: the part file's own for an arm; for a crane, a copy with its rails' reach in it (made again when
    /// the body's parts change). Null: not a lifter.
    /// </summary>
    public static LifterDef? DefOf(Construct c, int part)
    {
        var ld = c.DefOf(part).Lifter;
        if (ld is not { Bridge: true })
            return ld;
        if (c.CraneStamp != c.RebuildStamp || c.CranePartCount != c.PartCount)
            Cranes(c);
        for (var k = 0; k < c.CraneN; k++)
            if (c.CraneDefs[k].Part == part)
                return c.CraneDefs[k].Def;
        return ld; // (its own def has RailWhy 1: no reach)
    }

    private static void Cranes(Construct c)
    {
        c.CraneStamp = c.RebuildStamp;
        c.CranePartCount = c.PartCount;
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
        Span<float> x = stackalloc float[MaxRailSections * 2], y = stackalloc float[MaxRailSections * 2];
        Span<float> z0 = stackalloc float[MaxRailSections * 2], z1 = stackalloc float[MaxRailSections * 2];
        var m = 0;
        var cos = MathF.Cos(RailDeg * MathF.PI / 180f);
        for (var i = 0; i < c.PartCount && m < x.Length; i++)
        {
            var d = c.DefOf(i);
            if (!d.CraneRail || d.Class != cls || c.Parts[i].Broken)
                continue;
            Freeform.PartWorld(c, i, out var rp, out var rr);
            var q = inv * rr;
            if (MathF.Abs(Vector3.Transform(Vector3.UnitZ, q).Z) < cos)
                continue;
            var mid = new Vector3((d.BoundsMin.X + d.BoundsMax.X) * 0.5f, d.BoundsMax.Y, 0);
            var a = Vector3.Transform(rp + Vector3.Transform(mid with { Z = d.BoundsMin.Z }, rr) - bp, inv) - own.Base;
            var b = Vector3.Transform(rp + Vector3.Transform(mid with { Z = d.BoundsMax.Z }, rr) - bp, inv) - own.Base;
            (x[m], y[m], z0[m], z1[m]) = ((a.X + b.X) * 0.5f, (a.Y + b.Y) * 0.5f, MathF.Min(a.Z, b.Z), MathF.Max(a.Z, b.Z));
            m++;
        }
        if (!Run(x[..m], y[..m], z0[..m], z1[..m], 0, 0, FarRail, 0, out var n0, out var n1, out var nx, out var ny))
            return; // (not on a crane rail)
        copy.RailWhy = 2;
        if (!Run(x[..m], y[..m], z0[..m], z1[..m], nx + own.Span, ny, FarRail, (n0 + n1) * 0.5f, out var f0, out var f1, out _, out _, n0, n1))
            return; // (its far end has no rail)
        var lo = Math.Max(Math.Max(n0, f0), -MaxRun);
        var hi = Math.Min(Math.Min(n1, f1), MaxRun);
        if (hi - lo < 0.5f)
            return;
        copy.RailWhy = 0;
        (copy.RunLo, copy.RunHi) = (lo, hi);
        var zz = MathF.Max(MathF.Abs(lo), MathF.Abs(hi));
        copy.ReachMax = MathF.Sqrt(own.Span * own.Span + zz * zz); // (the box's horizontal radius: what the distance cuts read)
    }

    /// <summary>
    /// The run whose top line is at (<paramref name="atX"/>, <paramref name="atY"/>) within <paramref name="tol"/>: the section nearest
    /// <paramref name="atZ"/> (or overlapping [<paramref name="wantLo"/>, <paramref name="wantHi"/>]) and every section in line end to end with it.
    /// </summary>
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
