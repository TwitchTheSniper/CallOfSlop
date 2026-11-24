using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// A crane's or an arm's carry height over what stands in its way (Sean 2026-11-23: a crane carried a plate through the middle of a
/// Panel Former; 2026-11-24: the Shipyard Loader Arms swing 17 m decks from the yard's pads over the pads' masts and the frigate). When
/// a loaded carry starts the server looks along the path its piece makes at the carry height (the piece's own footprint as it turns,
/// both its end turns: a crane's straight run, an arm's arc round its base in angle and radius, as <c>PoseMiddle</c> poses it) for the
/// tallest box of any body there, and raises the move's rise so the piece's underside passes over it with <see cref="Margin"/> to
/// spare. The rise goes to clients in the lift word as before, so both peers pose the same path. What can't be cleared (a column up
/// past the highest carry: a crane's hook top, an arm's reach at the wider end, <c>CarryY</c>; or what hangs over the piece where it is
/// picked or put down) is left out: the lifter still goes as high as it must for the rest.
/// Cost: once per carry start, a few collider walks of the bodies whose bounds reach the path; nothing allocates.
/// </summary>
public static partial class ConstructLifters
{
    public const float Margin = 0.3f; // the piece's underside passes this far over the tallest thing in its way
    private const float Touch = 0.05f; // the footprint shrinks this much, so a part the piece sits against at an end is not in the way
    private const int ClearChunks = 32;
    private const float ChunkLong = 4f; // (m of path a chunk covers at most)
    private const float ChunkTurn = MathF.PI / 8; // (an arm's arc: radians a chunk turns at most, so its chord stays near the arc)
    private const int MaxSplits = 8; // (a chunk whose leaves fill the buffer is halved this many times at most: 256 boxes)

    /// <summary>The rise for a loaded carry whose ends (From, To and their turns) are set: the part's carry height setting, or more to clear the way.</summary>
    internal static float ClearRise(ConstructWorld world, Construct c, CLift l, LifterDef ld, ConstructPartDef d)
    {
        var rise = RiseOf(c, l.Part, ld);
        var mid = (d.BoundsMin + d.BoundsMax) * 0.5f;
        var half = (d.BoundsMax - d.BoundsMin) * 0.5f;
        var hf = AbsRot(l.FromRot, half);
        var ht = AbsRot(l.ToRot, half);
        var h = Vector3.Max(hf, ht);
        var from = l.From + Vector3.Transform(mid, l.FromRot);
        var to = l.To + Vector3.Transform(mid, l.ToRot);
        var high = MathF.Max(from.Y, to.Y);
        var cap = CarryY(ld, from, to, WristLift(d), 600) - h.Y; // (the piece's underside at the highest carry)
        if (cap <= high - h.Y)
            return rise;
        // The path at the carry height in chunks (lifter frame, x and z): each chunk's ends and the piece's half footprint over it.
        Span<Vector4> runs = stackalloc Vector4[ClearChunks];
        Span<Vector2> feet = stackalloc Vector2[ClearChunks];
        var n = ld.Bridge ? StraightChunks(from, to, h, runs, feet) : ArcChunks(ld, l, from, to, half, runs, feet);
        Frame(c, l.Part, ld, out var lp, out var lr, out _);
        var top = Tallest(world, c, l.Part, lp, lr, from, to, hf, ht, cap, runs[..n], feet[..n]);
        if (top == float.NegativeInfinity)
            return rise;
        return Math.Clamp(MathF.Max(rise, top + Margin + h.Y - high), 0, 600);
    }

    /// <summary>A crane's straight run in chunks of at most <see cref="ChunkLong"/>, the piece's widest footprint over each.</summary>
    private static int StraightChunks(Vector3 from, Vector3 to, Vector3 h, Span<Vector4> runs, Span<Vector2> feet)
    {
        var len = new Vector2(to.X - from.X, to.Z - from.Z).Length();
        var n = Math.Clamp((int)MathF.Ceiling(len / ChunkLong), 1, runs.Length);
        var foot = new Vector2(MathF.Max(0, h.X - Touch), MathF.Max(0, h.Z - Touch));
        for (var k = 0; k < n; k++)
        {
            var a = from + (to - from) * ((float)k / n);
            var z = from + (to - from) * ((float)(k + 1) / n);
            runs[k] = new Vector4(a.X, a.Z, z.X, z.Z);
            feet[k] = foot;
        }
        return n;
    }

    /// <summary>
    /// An arm's carry round its base (<c>PoseMiddle</c>: angle and radius together, the piece turning from its pick turn to its put
    /// turn), in chords of at most <see cref="ChunkLong"/> and <see cref="ChunkTurn"/>; over each, the piece's footprint at the
    /// chord's two ends, and the chord's bulge from its arc.
    /// </summary>
    private static int ArcChunks(LifterDef ld, CLift l, Vector3 from, Vector3 to, Vector3 half, Span<Vector4> runs, Span<Vector2> feet)
    {
        Polar(ld, from, out var r0, out var a0);
        Polar(ld, to, out var r1, out var a1);
        var turn = Turn(a0, a1);
        var arc = MathF.Abs(turn) * (r0 + r1) * 0.5f + MathF.Abs(r1 - r0);
        var n = Math.Clamp(Math.Max((int)MathF.Ceiling(arc / ChunkLong), (int)MathF.Ceiling(MathF.Abs(turn) / ChunkTurn)), 1, runs.Length);
        var step = MathF.Abs(turn) / n;
        var bulge = MathF.Max(r0, r1) * (1 - MathF.Cos(step * 0.5f)); // (how far the arc passes outside its chord)
        var pa = new Vector2(ld.Base.X + r0 * MathF.Cos(a0), ld.Base.Z + r0 * MathF.Sin(a0));
        var fa = AbsRot(l.FromRot, half);
        for (var k = 0; k < n; k++)
        {
            var f = (float)(k + 1) / n;
            var r = r0 + (r1 - r0) * f;
            var a = a0 + turn * f;
            var pz = new Vector2(ld.Base.X + r * MathF.Cos(a), ld.Base.Z + r * MathF.Sin(a));
            var fz = AbsRot(Quaternion.Slerp(l.FromRot, l.ToRot, f), half);
            runs[k] = new Vector4(pa.X, pa.Y, pz.X, pz.Y);
            feet[k] = new Vector2(MathF.Max(0, MathF.Max(fa.X, fz.X) + bulge - Touch), MathF.Max(0, MathF.Max(fa.Z, fz.Z) + bulge - Touch));
            (pa, fa) = (pz, fz);
        }
        return n;
    }

    /// <summary>
    /// The highest top (lifter frame) under <paramref name="cap"/> of any box in the path's way; -infinity: none. The path is its
    /// chunks (<paramref name="runs"/>: each one's ends in x and z, <paramref name="feet"/>: the piece's half footprint over it), from
    /// the piece's lowest underside at either end up to <paramref name="cap"/>.
    /// </summary>
    private static float Tallest(ConstructWorld world, Construct own, int ownPart, Vector3 lp, Quaternion lr, Vector3 from, Vector3 to, Vector3 hf, Vector3 ht, float cap,
        ReadOnlySpan<Vector4> runs, ReadOnlySpan<Vector2> feet)
    {
        var low = MathF.Min(from.Y - hf.Y, to.Y - ht.Y);
        // The path's bounds in the world, to pick the bodies near it.
        var lo = new Vector2(float.MaxValue);
        var hi = new Vector2(float.MinValue);
        for (var k = 0; k < runs.Length; k++)
        {
            var (r, f) = (runs[k], feet[k]);
            lo = Vector2.Min(lo, new Vector2(MathF.Min(r.X, r.Z) - f.X, MathF.Min(r.Y, r.W) - f.Y));
            hi = Vector2.Max(hi, new Vector2(MathF.Max(r.X, r.Z) + f.X, MathF.Max(r.Y, r.W) + f.Y));
        }
        var runMid = new Vector3((lo.X + hi.X) * 0.5f, (low + cap) * 0.5f, (lo.Y + hi.Y) * 0.5f);
        var runR = new Vector3((hi.X - lo.X) * 0.5f, (cap - low) * 0.5f, (hi.Y - lo.Y) * 0.5f).Length();
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
                for (var k = 0; k < runs.Length; k++)
                {
                    var (r, f) = (runs[k], feet[k]);
                    var cMid = new Vector3((r.X + r.Z) * 0.5f, (low + cap) * 0.5f, (r.Y + r.W) * 0.5f);
                    var cHalf = new Vector3(MathF.Abs(r.Z - r.X) * 0.5f + f.X, (cap - low) * 0.5f, MathF.Abs(r.W - r.Y) * 0.5f + f.Y);
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
