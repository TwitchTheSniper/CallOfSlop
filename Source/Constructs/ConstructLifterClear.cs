using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// A crane's carry height over what stands in its way (Sean 2026-11-23: a crane carried a plate through the middle of a Panel
/// Former). When a Bridge Crane starts a loaded carry the server looks along the straight run its piece makes at the carry
/// height (the piece's own footprint, both its end turns) for the tallest box of any body there, and raises the move's rise so
/// the piece's underside passes over it with <see cref="Margin"/> to spare. The rise goes to clients in the lift word as before,
/// so both peers pose the same path. What can't be cleared (a column up past the hook's highest carry, or what hangs over the
/// piece where it is picked or put down) is left out: the crane still goes as high as it must for the rest.
/// Cost: once per carry start, a few collider walks of the bodies whose bounds reach the run; nothing allocates.
/// </summary>
public static partial class ConstructLifters
{
    public const float Margin = 0.3f; // the piece's underside passes this far over the tallest thing in its way
    private const float Touch = 0.05f; // the footprint shrinks this much, so a part the piece sits against at an end is not in the way
    private const int ClearChunks = 32;
    private const int MaxSplits = 8; // (a chunk whose leaves fill the buffer is halved this many times at most: 256 boxes)

    /// <summary>The rise for a loaded crane carry whose ends (From, To and their turns) are set: the part's carry height setting, or more to clear the way.</summary>
    internal static float ClearRise(ConstructWorld world, Construct c, CLift l, LifterDef ld, ConstructPartDef d)
    {
        var rise = RiseOf(c, l.Part, ld);
        if (!ld.Bridge)
            return rise;
        var mid = (d.BoundsMin + d.BoundsMax) * 0.5f;
        var half = (d.BoundsMax - d.BoundsMin) * 0.5f;
        var hf = AbsRot(l.FromRot, half);
        var ht = AbsRot(l.ToRot, half);
        var h = Vector3.Max(hf, ht);
        var from = l.From + Vector3.Transform(mid, l.FromRot);
        var to = l.To + Vector3.Transform(mid, l.ToRot);
        var high = MathF.Max(from.Y, to.Y);
        var cap = ld.Base.Y - ld.Depth - WristLift(d) - h.Y; // (the piece's underside at the highest carry)
        if (cap <= high - h.Y)
            return rise;
        Frame(c, l.Part, ld, out var lp, out var lr, out _);
        var top = Tallest(world, c, l.Part, lp, lr, from, to, hf, ht, h, cap);
        if (top == float.NegativeInfinity)
            return rise;
        return Math.Clamp(MathF.Max(rise, top + Margin + h.Y - high), 0, 600);
    }

    /// <summary>The highest top (lifter frame) of any box in the run's way under <paramref name="cap"/>; -infinity: none.</summary>
    private static float Tallest(ConstructWorld world, Construct own, int ownPart, Vector3 lp, Quaternion lr, Vector3 from, Vector3 to, Vector3 hf, Vector3 ht, Vector3 h, float cap)
    {
        var low = MathF.Min(from.Y - hf.Y, to.Y - ht.Y);
        var flat = new Vector3(to.X - from.X, 0, to.Z - from.Z);
        var len = flat.Length();
        var n = Math.Clamp((int)MathF.Ceiling(len / 4f), 1, ClearChunks);
        var foot = new Vector3(MathF.Max(0, h.X - Touch), 0, MathF.Max(0, h.Z - Touch));
        // The run's bounds in the world, to pick the bodies near it.
        var runMid = new Vector3((from.X + to.X) * 0.5f, (low + cap) * 0.5f, (from.Z + to.Z) * 0.5f);
        var runR = (new Vector3(len * 0.5f + foot.X, (cap - low) * 0.5f, foot.Z)).Length() + len * 0.5f;
        var runWorld = lp + Vector3.Transform(runMid, lr);
        var best = float.NegativeInfinity;
        Span<int> found = stackalloc int[512];
        var inv = Quaternion.Conjugate(lr);
        foreach (var b in world.Bodies)
        {
            if (b.PartCount == 0)
                continue;
            var bc = b.ToWorld((b.LocalMin + b.LocalMax) * 0.5f);
            var br = (b.LocalMax - b.LocalMin).Length() * 0.5f;
            if (Vector3.DistanceSquared(bc, runWorld) > (br + runR) * (br + runR))
                continue;
            var col = b.Collider;
            for (var g = 0; g < col.GroupCount; g++)
            {
                b.WorldFrame(col.Groups[g].Seg, out var gq, out var gt);
                var toLifter = Quaternion.Normalize(inv * gq); // (group frame into the lifter's)
                var gOrigin = Vector3.Transform(gt - lp, inv); // (the group's origin in the lifter's frame)
                for (var k = 0; k < n; k++)
                {
                    var a = from + (to - from) * ((float)k / n);
                    var z = from + (to - from) * ((float)(k + 1) / n);
                    var cMid = new Vector3((a.X + z.X) * 0.5f, (low + cap) * 0.5f, (a.Z + z.Z) * 0.5f);
                    var cHalf = new Vector3(MathF.Abs(z.X - a.X) * 0.5f + foot.X, (cap - low) * 0.5f, MathF.Abs(z.Z - a.Z) * 0.5f + foot.Z);
                    Scan(col, g, b == own ? ownPart : -1, gOrigin, toLifter, cMid, cHalf, from, to, hf, ht, low, cap, ref best, found, 0);
                }
            }
        }
        return best;
    }

    /// <summary>
    /// The boxes of group <paramref name="g"/> in one box of the run (lifter frame): the tallest that can be cleared raises
    /// <paramref name="best"/>. A box whose leaves fill the buffer is halved along its longest side and each half looked at (a
    /// big body, the frigate on its station, puts thousands of leaves in a chunk); still full at the deepest split, it says so.
    /// </summary>
    private static void Scan(ConstructCollider col, int g, int skipPart, Vector3 gOrigin, Quaternion toLifter, Vector3 cMid, Vector3 cHalf,
        Vector3 from, Vector3 to, Vector3 hf, Vector3 ht, float low, float cap, ref float best, Span<int> found, int depth)
    {
        var toGroup = Quaternion.Conjugate(toLifter);
        var count = col.Collect(g, Vector3.Transform(cMid - gOrigin, toGroup), AbsRot(toGroup, cHalf), found); // (the box's bounds in the group's frame)
        if (count >= found.Length)
        {
            if (depth < MaxSplits)
            {
                var axis = cHalf.X >= cHalf.Y && cHalf.X >= cHalf.Z ? Vector3.UnitX : cHalf.Y >= cHalf.Z ? Vector3.UnitY : Vector3.UnitZ;
                var half = cHalf - axis * (Vector3.Dot(cHalf, axis) * 0.5f);
                var shift = axis * Vector3.Dot(half, axis);
                Scan(col, g, skipPart, gOrigin, toLifter, cMid - shift, half, from, to, hf, ht, low, cap, ref best, found, depth + 1);
                Scan(col, g, skipPart, gOrigin, toLifter, cMid + shift, half, from, to, hf, ht, low, cap, ref best, found, depth + 1);
                return;
            }
            Console.WriteLine($"[lifter] carry clearance: over {found.Length} boxes in a {cHalf.X * 2:0.0} x {cHalf.Y * 2:0.0} x {cHalf.Z * 2:0.0} m box even split {MaxSplits} times; the tallest there may be missed");
        }
        for (var i = 0; i < count; i++)
        {
            ref var leaf = ref col.Leaves[found[i]];
            if (leaf.Dead || leaf.Stub || leaf.Part == skipPart)
                continue;
            var lh = leaf.Turned ? Vector3.Abs(leaf.AX) * leaf.Half.X + Vector3.Abs(leaf.AY) * leaf.Half.Y + Vector3.Abs(leaf.AZ) * leaf.Half.Z : leaf.Half;
            var wc = gOrigin + Vector3.Transform(leaf.Centre, toLifter);
            var wh = AbsRot(toLifter, lh);
            var lo = wc - wh;
            var hi = wc + wh;
            if (hi.Y > cap || hi.Y <= best || hi.Y <= low)
                continue; // (too tall to clear, no taller than what's found, or under the piece's lowest end)
            if (MathF.Abs(wc.X - cMid.X) > wh.X + cHalf.X || MathF.Abs(wc.Z - cMid.Z) > wh.Z + cHalf.Z || lo.Y > cMid.Y + cHalf.Y)
                continue; // (the tree's bounds reached the box, the leaf itself doesn't)
            if (Over(lo, hi, from, hf) || Over(lo, hi, to, ht) || Inside(lo, hi, from, hf) || Inside(lo, hi, to, ht))
                continue; // (it hangs over the piece where it is picked or put down, or is where the piece lies: the hoist goes past it anyway)
            best = hi.Y;
        }
    }

    /// <summary>Does a box stand over the piece's footprint at an end (its underside at or over the piece's top there)?</summary>
    private static bool Over(Vector3 lo, Vector3 hi, Vector3 end, Vector3 half) =>
        lo.Y >= end.Y + half.Y - Touch
        && lo.X < end.X + half.X - Touch && hi.X > end.X - half.X + Touch
        && lo.Z < end.Z + half.Z - Touch && hi.Z > end.Z - half.Z + Touch;

    /// <summary>Does a box overlap the piece's own box at an end (the piece itself still on its pad, or what it is fitted into)?</summary>
    private static bool Inside(Vector3 lo, Vector3 hi, Vector3 end, Vector3 half) =>
        lo.X < end.X + half.X - Touch && hi.X > end.X - half.X + Touch
        && lo.Y < end.Y + half.Y - Touch && hi.Y > end.Y - half.Y + Touch
        && lo.Z < end.Z + half.Z - Touch && hi.Z > end.Z - half.Z + Touch;

    /// <summary>A box's half size seen from a frame turned by <paramref name="q"/>: the bounds of the turned box.</summary>
    private static Vector3 AbsRot(Quaternion q, Vector3 half) =>
        Vector3.Abs(Vector3.Transform(Vector3.UnitX, q)) * half.X + Vector3.Abs(Vector3.Transform(Vector3.UnitY, q)) * half.Y + Vector3.Abs(Vector3.Transform(Vector3.UnitZ, q)) * half.Z;
}
