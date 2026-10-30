using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The configure tool's previews in the world (docs/sandbox/CONSTRUCTS.md, "The configure tool"), while a part's settings
/// panel is open: the part outlined, the other selected parts of its kind, its wiring (lines to the parts wired into it
/// and out of it, those parts tinted), a lamp lit in its colour, a piston's travel from its pulled-in to its pushed-out
/// end, a hinge's limits as an arc, a proximity sensor's range as a sphere. The values come from the panel
/// (<see cref="PreviewCfg"/>), so a slider shows at once, before the server's answer. Unlit lines, no allocation.
/// </summary>
public static partial class ConstructRender
{
    public static Construct? PreviewBody; // the part whose panel is open (null: no previews)
    public static int PreviewPart = -1;
    public static readonly float[] PreviewCfg = new float[ConstructConfig.MaxSlots]; // the panel's values
    public static readonly byte[] PreviewSelected = new byte[ConstructEdits.MaxConfigParts]; // the other parts it applies to
    public static int PreviewSelectedCount;
    public static int PreviewHighlightWire = -1; // a wire the panel's row is hovered on (index in the construct's wires; -1: all shown plainly)
    public static int LastPreviewLines { get; private set; }

    private static readonly Vector3 CfgPurple = new(0.86f, 0.5f, 0.95f), CfgIn = new(0.4f, 1f, 0.45f), CfgOut = new(1f, 0.6f, 0.2f), CfgLimit = new(1f, 0.85f, 0.3f);

    public static void ClearPreview()
    {
        PreviewBody = null;
        PreviewPart = -1;
        PreviewSelectedCount = 0;
        PreviewHighlightWire = -1;
    }

    private static float PreviewValue(ConstructPartDef d, CfgKey k, float fallback)
    {
        var s = d.CfgSlot[(int)k];
        return s < 0 ? fallback : PreviewCfg[s];
    }

    public static void DrawConfigPreview(Renderer gfx, ConstructWorld world)
    {
        LastPreviewLines = 0;
        if (PreviewBody is not { } c || world.Get(c.Id) != c || PreviewPart < 0 || PreviewPart >= c.PartCount)
            return;
        var i = PreviewPart;
        var d = c.DefOf(i);
        gfx.SetDepthTest(false);
        var pulse = 0.35f + 0.15f * MathF.Sin((float)Now * 5f);
        Tint(gfx, c, i, CfgPurple, pulse + 0.1f);
        for (var k = 0; k < PreviewSelectedCount; k++)
            if (PreviewSelected[k] != i && PreviewSelected[k] < c.PartCount)
                Tint(gfx, c, PreviewSelected[k], CfgPurple, pulse);
        // Its wiring: in (green) and out (orange); a hovered row stands out.
        var centre = PartCentre(c, i);
        for (var k = 0; k < c.WireCount; k++)
        {
            ref var w = ref c.Wires[k];
            if (w.From >= c.PartCount || w.To >= c.PartCount || (w.From != i && w.To != i))
                continue;
            var into = w.To == i;
            var other = into ? w.From : w.To;
            var hot = PreviewHighlightWire == k;
            var col = into ? CfgIn : CfgOut;
            if (other != i)
            {
                Line(gfx, centre, PartCentre(c, other), col, hot ? 1f : 0.6f);
                Tint(gfx, c, other, col, hot ? 0.55f : 0.25f);
            }
            LastPreviewLines++;
        }
        c.WorldFrame(c.Parts[i].Seg, out var q, out var t);
        // A piston's travel; a hinge's limits.
        if (d.HasJoint && c.JointOf(i) is var j and >= 0)
        {
            ref var jt = ref c.Joints[j];
            c.WorldFrame(jt.ParentSeg, out var pq, out var pt);
            var axis = Vector3.Normalize(Vector3.Transform(jt.Axis, pq));
            if (jt.Slides && d.LeafIndex >= 0)
            {
                c.SocketAt(i, d.LeafIndex, out var leaf, out _);
                var basePt = pt + Vector3.Transform(leaf, pq);
                var lo = Math.Clamp(PreviewValue(d, CfgKey.Min, d.JointMin), d.JointMin, d.JointMax);
                var hi = Math.Clamp(PreviewValue(d, CfgKey.Max, d.JointMax), d.JointMin, d.JointMax);
                var a = basePt + axis * MathF.Min(lo, hi);
                var b = basePt + axis * MathF.Max(lo, hi);
                Line(gfx, basePt, a, new Vector3(0.6f), 0.5f);
                Line(gfx, a, b, CfgLimit, 0.9f);
                gfx.Draw(Matrix4x4.CreateScale(0.16f, 0.03f, 0.16f) * Basis(axis) * Matrix4x4.CreateTranslation(a), CfgLimit, 0.7f, mode: 2);
                gfx.Draw(Matrix4x4.CreateScale(0.3f, 0.3f, 0.04f) * Basis(axis) * Matrix4x4.CreateTranslation(b), CfgLimit, 0.45f, mode: 2); // (the ghost of the leaf at full travel)
                LastPreviewLines += 2;
            }
            else if (!jt.Slides && d.JointLimited)
            {
                const float deg = MathF.PI / 180f;
                var lo = Math.Clamp(PreviewValue(d, CfgKey.Min, d.JointMin / deg) * deg, d.JointMin, d.JointMax);
                var hi = Math.Clamp(PreviewValue(d, CfgKey.Max, d.JointMax / deg) * deg, d.JointMin, d.JointMax);
                if (lo > hi)
                    (lo, hi) = (hi, lo);
                var pivot = pt + Vector3.Transform(jt.Pivot, pq);
                // Zero: towards the leaf from the pivot (at rest), else any square to the axis.
                var leafAt = d.LeafIndex >= 0 ? LeafRest(c, i, d, pq, pt) : centre;
                var r0 = leafAt - pivot;
                r0 -= axis * Vector3.Dot(r0, axis);
                if (r0.LengthSquared() < 1e-4f)
                    r0 = Vector3.Cross(axis, MathF.Abs(axis.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX);
                var radius = Math.Clamp(r0.Length(), 0.4f, 1.2f);
                r0 = Vector3.Normalize(r0) * radius;
                const int steps = 18;
                var prev = pivot + Vector3.Transform(r0, Quaternion.CreateFromAxisAngle(axis, lo));
                Line(gfx, pivot, prev, CfgLimit, 0.9f);
                for (var s = 1; s <= steps; s++)
                {
                    var at = pivot + Vector3.Transform(r0, Quaternion.CreateFromAxisAngle(axis, lo + (hi - lo) * s / steps));
                    Line(gfx, prev, at, CfgLimit, 0.75f);
                    prev = at;
                }
                Line(gfx, pivot, prev, CfgLimit, 0.9f);
                Line(gfx, pivot, pivot + Vector3.Transform(r0 * 1.1f, Quaternion.CreateFromAxisAngle(axis, jt.Angle)), new Vector3(1f), 0.8f); // (where it is now)
                LastPreviewLines += steps + 3;
            }
        }
        // A proximity sensor's range.
        if (d.Logic == CLogic.Proximity && ConstructConfig.Has(d, CfgKey.Radius))
        {
            var r = PreviewValue(d, CfgKey.Radius, 6);
            Ring(gfx, centre, Vector3.UnitX, Vector3.UnitZ, r);
            Ring(gfx, centre, Vector3.UnitX, Vector3.UnitY, r);
            Ring(gfx, centre, Vector3.UnitZ, Vector3.UnitY, r);
        }
        gfx.SetDepthTest(true);
    }

    private static Vector3 LeafRest(Construct c, int i, ConstructPartDef d, Quaternion pq, Vector3 pt)
    {
        c.SocketAt(i, d.LeafIndex, out var leaf, out var dir);
        return pt + Vector3.Transform(leaf + ConstructParts.DirVector(dir) * 0.3f, pq);
    }

    private static void Ring(Renderer gfx, Vector3 centre, Vector3 u, Vector3 v, float r)
    {
        const int steps = 32;
        var prev = centre + u * r;
        for (var s = 1; s <= steps; s++)
        {
            var a = MathF.Tau * s / steps;
            var at = centre + (u * MathF.Cos(a) + v * MathF.Sin(a)) * r;
            Line(gfx, prev, at, CfgPurple, 0.6f);
            prev = at;
        }
        LastPreviewLines += steps;
    }

    /// <summary>A part's bounds drawn over it, see-through (the selection and the wired parts).</summary>
    private static void Tint(Renderer gfx, Construct c, int part, Vector3 colour, float opacity)
    {
        c.WorldFrame(c.Parts[part].Seg, out var q, out var t);
        c.PartBounds(part, out var mn, out var mx);
        var size = mx - mn + new Vector3(0.04f);
        gfx.Draw(Matrix4x4.CreateScale(size) * Matrix4x4.CreateFromQuaternion(q) * Matrix4x4.CreateTranslation(t + Vector3.Transform((mn + mx) * 0.5f, q)), colour, opacity, mode: 2);
    }
}
