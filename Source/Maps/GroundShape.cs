using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The ground drill's shaping ops (docs/sandbox/TERRAIN-DIGGING.md "Shaping"): Flatten and Fill slope are one op, a grade
/// (<see cref="DigKind.GradeDirt"/>..: the ground along a segment made a ramp between its ends' heights, a pad when they are one
/// point), and Clean holes is a smooth (<see cref="DigKind.SmoothDirt"/>..: pits filled up to the ground around them). The kinds,
/// their limits and the closing the smooth uses, shared by flat ground (TerrainEditsShape.cs) and planetoids (PlanetEditsShape.cs).
/// Every peer applies the same ops with the same maths (comparisons, products and square roots only), so the ground matches.
/// </summary>
public static class GroundShape
{
    /// <summary>A grade cuts away the ground this far over its surface (metres): room to stand and drive on it.</summary>
    public const float Headroom = 4f;

    /// <summary>A grade fills at most this far under its surface (metres); deeper gaps are left under a slab this thick.</summary>
    public const float Depth = 12f;

    /// <summary>Clean holes closes the surface by this radius (metres): pits up to about twice it across fill, wider dips and every slope stay.</summary>
    public const float Kernel = 1.25f;

    /// <summary>Clean holes raises a column at most this far in one go (metres).</summary>
    public const float SmoothRise = 3f;

    /// <summary>The longest ramp (metres, end to end).</summary>
    public const float MaxLength = 32f;

    /// <summary>The widest shaping op (its radius, metres): an op over it changes nothing, on every peer.</summary>
    public const float MaxRadius = 4f;

    /// <summary>The last kind of op there is (the wire's and the save's check).</summary>
    public const DigKind LastKind = DigKind.SmoothSand;

    public static bool IsGrade(DigKind k) => k is >= DigKind.GradeDirt and <= DigKind.GradeSand;

    public static bool IsSmooth(DigKind k) => k is >= DigKind.SmoothDirt and <= DigKind.SmoothSand;

    public static bool IsShape(DigKind k) => k is >= DigKind.GradeDirt and <= DigKind.SmoothSand;

    /// <summary>Does this kind put ground in (a fill or a shaping op)?</summary>
    public static bool Adds(DigKind k) => k >= DigKind.FillDirt && k <= LastKind;

    /// <summary>What a fill, grade or smooth lays.</summary>
    public static GroundKind GroundOf(DigKind k) =>
        (GroundKind)(k >= DigKind.SmoothDirt ? k - DigKind.SmoothDirt : k >= DigKind.GradeDirt ? k - DigKind.GradeDirt : k - DigKind.FillDirt);

    public static DigKind Grade(GroundKind g) => DigKind.GradeDirt + (byte)g;

    public static DigKind Smooth(GroundKind g) => DigKind.SmoothDirt + (byte)g;

    /// <summary>
    /// Where a column whose top a shaping op sets to <paramref name="target"/> gets new ground from: the ground under the target, at
    /// most <paramref name="depth"/> down. False when it is solid there already (nothing to fill).
    /// </summary>
    public static bool FillFrom(ReadOnlySpan<float> spans, float target, float depth, out float lo)
    {
        var below = TerrainEdits.GroundBelow(spans, target);
        lo = MathF.Max(below, target - depth);
        return below < target && target - lo >= TerrainEdits.MinSpan;
    }

    /// <summary>Is any of [lo, hi] solid in a column (more than a sliver)?</summary>
    public static bool SolidIn(ReadOnlySpan<float> spans, float lo, float hi)
    {
        for (var k = 0; k < spans.Length; k += 2)
            if (MathF.Min(spans[k + 1], hi) - MathF.Max(spans[k], lo) > 0.001f)
                return true;
        return false;
    }

    /// <summary>
    /// A morphological closing of the surface heights <paramref name="s"/> (columns at <paramref name="pos"/>) by a disc of radius
    /// <paramref name="k"/>: each height the most of its neighbours' within k, then the least of those. A pit narrower than about 2k
    /// comes up to the ground round it, and a plane, a slope or a bump comes back as it was. In place; <paramref name="tmp"/> as long.
    /// </summary>
    public static void Close(ReadOnlySpan<Vector3> pos, Span<float> s, float k, Span<float> tmp)
    {
        var n = s.Length;
        var k2 = k * k;
        for (var i = 0; i < n; i++)
        {
            var m = s[i];
            for (var j = 0; j < n; j++)
                if (s[j] > m && Vector3.DistanceSquared(pos[i], pos[j]) <= k2)
                    m = s[j];
            tmp[i] = m;
        }
        for (var i = 0; i < n; i++)
        {
            var m = tmp[i];
            for (var j = 0; j < n; j++)
                if (tmp[j] < m && Vector3.DistanceSquared(pos[i], pos[j]) <= k2)
                    m = tmp[j];
            s[i] = m;
        }
    }

    /// <summary>Is a grade's segment one this game shapes (finite, no longer than <see cref="MaxLength"/>, no wider than <see cref="MaxRadius"/>)?</summary>
    public static bool Fits(in DigOp op) =>
        op.Radius > 0 && op.Radius <= MaxRadius && float.IsFinite(op.At.X + op.At.Y + op.At.Z)
        && (!IsGrade(op.Kind) || float.IsFinite(op.To.X + op.To.Y + op.To.Z) && Vector3.Distance(op.At, op.To) <= MaxLength + 0.01f);
}
