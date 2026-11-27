using System.Numerics;

namespace CallOfSlop;

/// <summary>One crack a round left in a design's window (<see cref="ConstructWorld.AddGlassCrack"/>).</summary>
public struct GlassCrack
{
    public ushort Body;
    public ushort Part; // its index when made (found again by Key when the parts were renumbered)
    public Vector3 Key; // the part's Pos then
    public ConstructPartDef? Def; // ...and its def: a crack goes with its pane
    public Vector3 Pos, X, Y, N; // where the round struck, the pane's face there and out of it toward the shooter (the part's segment frame)
    public float Depth; // the pane's thickness behind the face (its far face found, else a guess): the crack's lines go through it
    public float Radius, Speed; // how far it runs, and how fast it spreads (metres a second)
    public byte Segs; // its lines, at Seg[k * MaxGlassCrackSegs ...]
    public double Born; // the clock the game draws by
}

/// <summary>A line of a crack: from A to B on the pane (in the crack's X, Y), seen from when the crack has run to Start until it reaches End.</summary>
public struct GlassCrackSeg
{
    public Vector2 A, B;
    public float Start, End, Width;
}

/// <summary>
/// Cracks in the windows of ship designs (Karl 2026-11-27: "windows show damage as cracks which form", "the bullet size is factored in",
/// "location-based"): each round that ends on a design's glass leaves a star of cracks where it struck, run out from the point over a
/// fraction of a second, its reach and its arms from the gun's damage and calibre. Made on every PC from the shot events the server
/// already sends (<see cref="CFireKind.Shot"/>, nothing new on the wire), drawn by Program.SandboxCracks.cs. Kept in a ring of
/// <see cref="MaxGlassCracks"/> (the oldest go first) with their lines in one array made with the first; a crack goes when its pane does.
/// The lines are laid once, when the crack is made, and kept on the pane: each is cut where it would leave the glass it struck.
/// </summary>
public sealed partial class ConstructWorld
{
    public const int MaxGlassCracks = 192, MaxGlassCrackSegs = 72;

    public GlassCrack[] GlassCracks = []; // (made with the first)
    public GlassCrackSeg[] GlassCrackSegs = [];
    public int GlassCrackCount;
    private int _crackNext;

    // The struck face's glass triangles flat on it (the crack's X, Y), while a crack is laid: the main thread's alone.
    private static readonly Vector2[] _paneTris = new Vector2[3 * 512];
    private static int _paneTriCount;

    /// <summary>
    /// A round from <paramref name="from"/> ended at <paramref name="at"/> (world): if that is on a design's glass, a crack there, sized by
    /// the gun's <paramref name="damage"/> and <paramref name="calibre"/> (0 small and snappy, 1 huge). True when one was made.
    /// </summary>
    public bool AddGlassCrack(Vector3 from, Vector3 at, float damage, float calibre, double now)
    {
        foreach (var c in Bodies)
        {
            if (!c.HasDesign)
                continue;
            c.WorldBounds(out var bmin, out var bmax);
            if (at.X < bmin.X - 1 || at.Y < bmin.Y - 1 || at.Z < bmin.Z - 1 || at.X > bmax.X + 1 || at.Y > bmax.Y + 1 || at.Z > bmax.Z + 1)
                continue;
            for (var i = 0; i < c.PartCount; i++)
                if (DesignParts.LookOf(c.Parts[i].D) is { GlassVertices: > 0 } l && CrackOn(c, i, l, from, at, damage, calibre, now))
                    return true;
        }
        return false;
    }

    private bool CrackOn(Construct c, int i, DesignParts.Look l, Vector3 from, Vector3 at, float damage, float calibre, double now)
    {
        ref var p = ref c.Parts[i];
        c.WorldFrame(p.Seg, out var sq, out var st);
        var pq = Rot24.Quat(p.Rot);
        var toPart = Quaternion.Conjugate(sq * pq);
        var local = Vector3.Transform(at - st, Quaternion.Conjugate(sq)) - p.Pos;
        local = Vector3.Transform(local, Quaternion.Conjugate(pq));
        const float Near = 0.12f; // (how far off its glass a round's end may be: the round stops on the collider, which the look is near)
        if (local.X < l.Min.X - Near || local.Y < l.Min.Y - Near || local.Z < l.Min.Z - Near || local.X > l.Max.X + Near || local.Y > l.Max.Y + Near || local.Z > l.Max.Z + Near)
            return false;
        var way = Vector3.Transform(at - from, toPart);
        way = way.LengthSquared() > 1e-8f ? Vector3.Normalize(way) : Vector3.UnitZ;
        // The glass triangle nearest the round's end (its point on the triangle within Near), facing the round.
        var best = float.MaxValue;
        var hit = default(Vector3);
        var n = default(Vector3);
        var pos = l.Pos;
        for (var v = 0; v + 2 < l.Vertices; v += 3)
        {
            if (!DesignParts.IsGlass(l.Colour[v]))
                continue;
            var a = new Vector3(pos[v * 3], pos[v * 3 + 1], pos[v * 3 + 2]);
            var b = new Vector3(pos[v * 3 + 3], pos[v * 3 + 4], pos[v * 3 + 5]);
            var d = new Vector3(pos[v * 3 + 6], pos[v * 3 + 7], pos[v * 3 + 8]);
            var fn = Vector3.Cross(b - a, d - a);
            if (fn.LengthSquared() < 1e-10f)
                continue;
            fn = Vector3.Normalize(fn);
            if (Vector3.Dot(fn, way) > 0)
                fn = -fn; // (out toward the shooter)
            var q = Closest(local, a, b, d);
            var dist = Vector3.Distance(q, local);
            // A face the round met head on beats one it grazes at the same distance (the pane's edge).
            var score = dist + (1f - MathF.Abs(Vector3.Dot(fn, way))) * 0.03f;
            if (dist < Near && score < best)
                (best, hit, n) = (score, q, fn);
        }
        if (best == float.MaxValue)
            return false;
        // The crack's frame on the face, and the face's glass laid flat in it (what its lines are cut to).
        var x = MathF.Abs(n.Y) < 0.9f ? Vector3.Normalize(Vector3.Cross(Vector3.UnitY, n)) : Vector3.Normalize(Vector3.Cross(Vector3.UnitX, n));
        var y = Vector3.Cross(n, x);
        _paneTriCount = 0;
        var depth = 0f;
        for (var v = 0; v + 2 < l.Vertices && _paneTriCount < _paneTris.Length / 3; v += 3)
        {
            if (!DesignParts.IsGlass(l.Colour[v]))
                continue;
            var a = new Vector3(pos[v * 3], pos[v * 3 + 1], pos[v * 3 + 2]);
            var b = new Vector3(pos[v * 3 + 3], pos[v * 3 + 4], pos[v * 3 + 5]);
            var d = new Vector3(pos[v * 3 + 6], pos[v * 3 + 7], pos[v * 3 + 8]);
            var fn = Vector3.Cross(b - a, d - a);
            if (fn.LengthSquared() < 1e-10f)
                continue;
            fn = Vector3.Normalize(fn);
            var across = MathF.Abs(Vector3.Dot(fn, n));
            if (across < 0.98f)
                continue;
            var off = Vector3.Dot(a - hit, n);
            var a2 = new Vector2(Vector3.Dot(a - hit, x), Vector3.Dot(a - hit, y));
            var b2 = new Vector2(Vector3.Dot(b - hit, x), Vector3.Dot(b - hit, y));
            var d2 = new Vector2(Vector3.Dot(d - hit, x), Vector3.Dot(d - hit, y));
            if (MathF.Abs(off) < 0.01f)
            {
                var t = _paneTriCount++ * 3;
                (_paneTris[t], _paneTris[t + 1], _paneTris[t + 2]) = (a2, b2, d2);
            }
            else if (off < 0 && off > -0.15f && InTri(Vector2.Zero, a2, b2, d2))
                depth = MathF.Max(depth, -off); // (the pane's far face, behind the point)
        }
        if (depth <= 0)
            depth = 0.02f;
        // Its size: a small round stars a hand's width of glass, a cannon's a metre and more; a big calibre throws more arms.
        var radius = Math.Clamp(0.1f + MathF.Sqrt(MathF.Max(damage, 1f)) * 0.025f + calibre * 0.1f, 0.1f, 1.6f);
        if (GlassCracks.Length == 0)
        {
            GlassCracks = new GlassCrack[MaxGlassCracks];
            GlassCrackSegs = new GlassCrackSeg[MaxGlassCracks * MaxGlassCrackSegs];
        }
        var k = _crackNext;
        _crackNext = (_crackNext + 1) % MaxGlassCracks;
        GlassCrackCount = Math.Min(GlassCrackCount + 1, MaxGlassCracks);
        // Kept in the segment's frame (as bore marks are): the part's turn and place put on.
        var segs = Lay(k, radius, damage, calibre, Seed(at));
        GlassCracks[k] = new GlassCrack
        {
            Body = c.Id, Part = (ushort)i, Key = p.Pos, Def = c.DefOf(i), Born = now, Radius = radius, Depth = depth,
            Speed = radius / (0.18f + radius * 0.2f), Segs = (byte)segs,
            Pos = p.Pos + Vector3.Transform(hit, pq), X = Vector3.Transform(x, pq), Y = Vector3.Transform(y, pq), N = Vector3.Transform(n, pq),
        };
        return true;
    }

    /// <summary>Lays crack <paramref name="k"/>'s lines: arms from the point, jagged, and rings joining them part way out. Returns their count.</summary>
    private int Lay(int k, float radius, float damage, float calibre, uint seed)
    {
        var at = k * MaxGlassCrackSegs;
        var count = 0;
        var arms = Math.Clamp(5 + (int)(calibre * 4) + (int)(damage / 250f), 5, 10);
        var steps = radius > 0.5f ? 4 : 3;
        var rings = radius > 0.35f ? 2 : 1;
        Span<Vector2> ringAt = stackalloc Vector2[10 * 2];
        Span<bool> ringOk = stackalloc bool[10 * 2];
        ringOk.Clear();
        var turn = Rand(ref seed) * MathF.Tau;
        var width = 0.004f + radius * 0.006f;
        for (var r = 0; r < arms; r++)
        {
            var angle = turn + (r + (Rand(ref seed) - 0.5f) * 0.6f) * MathF.Tau / arms;
            var length = radius * (0.55f + 0.45f * Rand(ref seed));
            var from = Vector2.Zero;
            var run = 0f;
            for (var s = 0; s < steps && count < MaxGlassCrackSegs; s++)
            {
                angle += (Rand(ref seed) - 0.5f) * 0.5f;
                var to = from + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * (length / steps);
                var cut = !OnPane(to);
                if (cut)
                {
                    // Back to where the line leaves the glass (halving the step).
                    var lo = 0f;
                    var hi = 1f;
                    for (var h = 0; h < 6; h++)
                    {
                        var mid = (lo + hi) * 0.5f;
                        if (OnPane(Vector2.Lerp(from, to, mid)))
                            lo = mid;
                        else
                            hi = mid;
                    }
                    to = Vector2.Lerp(from, to, lo);
                }
                var len = Vector2.Distance(from, to);
                if (len > 0.002f)
                {
                    var taper = 1f - 0.6f * s / steps;
                    GlassCrackSegs[at + count++] = new GlassCrackSeg { A = from, B = to, Start = run, End = run + len, Width = width * taper };
                }
                run += len;
                for (var j = 0; j < rings; j++)
                {
                    var ring = radius * (rings == 1 ? 0.35f : j == 0 ? 0.28f : 0.6f);
                    if (!ringOk[r * 2 + j] && to.Length() >= ring && from.Length() < ring)
                    {
                        var f = (ring - from.Length()) / MathF.Max(to.Length() - from.Length(), 1e-4f);
                        (ringAt[r * 2 + j], ringOk[r * 2 + j]) = (Vector2.Lerp(from, to, Math.Clamp(f, 0f, 1f)), true);
                    }
                }
                from = to;
                if (cut)
                    break;
            }
        }
        // The rings: most neighbouring arms joined, each piece seen once the crack has run out that far.
        for (var j = 0; j < rings; j++)
            for (var r = 0; r < arms && count < MaxGlassCrackSegs; r++)
            {
                var r2 = (r + 1) % arms;
                if (!ringOk[r * 2 + j] || !ringOk[r2 * 2 + j] || Rand(ref seed) < 0.25f)
                    continue;
                var a = ringAt[r * 2 + j];
                var b = ringAt[r2 * 2 + j];
                var len = Vector2.Distance(a, b);
                if (len > radius) // (two arms far apart: no ring between them)
                    continue;
                var start = a.Length() * 1.2f;
                GlassCrackSegs[at + count++] = new GlassCrackSeg { A = a, B = b, Start = start, End = start + len, Width = width * 0.55f };
            }
        // The crushed spot the round struck: a few short lines across it.
        for (var s = 0; s < 3 && count < MaxGlassCrackSegs; s++)
        {
            var angle = turn + s * MathF.PI / 3;
            var half = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius * 0.06f;
            GlassCrackSegs[at + count++] = new GlassCrackSeg { A = -half, B = half, Start = 0, End = 0.0001f, Width = width * 1.6f };
        }
        return count;
    }

    /// <summary>Is a point (the crack's X, Y) on the struck face's glass?</summary>
    private static bool OnPane(Vector2 q)
    {
        for (var t = 0; t < _paneTriCount * 3; t += 3)
            if (InTri(q, _paneTris[t], _paneTris[t + 1], _paneTris[t + 2]))
                return true;
        return false;
    }

    private static bool InTri(Vector2 q, Vector2 a, Vector2 b, Vector2 c)
    {
        static float Side(Vector2 p, Vector2 u, Vector2 v) => (u.X - p.X) * (v.Y - p.Y) - (u.Y - p.Y) * (v.X - p.X);
        var d1 = Side(q, a, b);
        var d2 = Side(q, b, c);
        var d3 = Side(q, c, a);
        const float E = 1e-6f;
        return !((d1 < -E || d2 < -E || d3 < -E) && (d1 > E || d2 > E || d3 > E));
    }

    /// <summary>The point of triangle a b c nearest <paramref name="p"/> (Ericson, Real-Time Collision Detection 5.1.5).</summary>
    private static Vector3 Closest(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        var ab = b - a;
        var ac = c - a;
        var ap = p - a;
        float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
        if (d1 <= 0 && d2 <= 0)
            return a;
        var bp = p - b;
        float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
        if (d3 >= 0 && d4 <= d3)
            return b;
        var vc = d1 * d4 - d3 * d2;
        if (vc <= 0 && d1 >= 0 && d3 <= 0)
            return a + ab * (d1 / (d1 - d3));
        var cp = p - c;
        float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
        if (d6 >= 0 && d5 <= d6)
            return c;
        var vb = d5 * d2 - d1 * d6;
        if (vb <= 0 && d2 >= 0 && d6 <= 0)
            return a + ac * (d2 / (d2 - d6));
        var va = d3 * d6 - d5 * d4;
        if (va <= 0 && d4 - d3 >= 0 && d5 - d6 >= 0)
            return b + (c - b) * ((d4 - d3) / (d4 - d3 + (d5 - d6)));
        var denom = 1f / (va + vb + vc);
        return a + ab * (vb * denom) + ac * (vc * denom);
    }

    // (the same on every PC: HashCode is seeded per run)
    private static uint Seed(Vector3 at) => ((uint)(int)MathF.Round(at.X * 100) * 73856093u ^ (uint)(int)MathF.Round(at.Y * 100) * 19349663u ^ (uint)(int)MathF.Round(at.Z * 100) * 83492791u) | 1u;

    private static float Rand(ref uint s)
    {
        s ^= s << 13;
        s ^= s >> 17;
        s ^= s << 5;
        return (s & 0xffffff) / 16777216f;
    }

    /// <summary>Is crack <paramref name="k"/>'s pane still on its body? Finds it again after a renumbering; false (and the crack goes) when it is gone.</summary>
    public bool GlassCrackLive(int k, out Construct c)
    {
        ref var m = ref GlassCracks[k];
        c = null!;
        if (m.Body == 0 || Get(m.Body) is not { } body)
        {
            m.Body = 0;
            return false;
        }
        c = body;
        var i = Math.Min(m.Part, body.PartCount - 1);
        if (i >= 0 && body.Parts[i].Pos == m.Key && ReferenceEquals(body.DefOf(i), m.Def))
            return true;
        for (i = body.PartCount - 1; i >= 0; i--)
            if (body.Parts[i].Pos == m.Key && ReferenceEquals(body.DefOf(i), m.Def))
            {
                m.Part = (ushort)i;
                return true;
            }
        m.Body = 0;
        return false;
    }
}
