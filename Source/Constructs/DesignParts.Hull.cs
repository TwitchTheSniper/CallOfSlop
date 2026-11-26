using System.Numerics;
using CallOfSlop.ShipDesigner;

namespace CallOfSlop;

/// <summary>
/// Design parts, the hull (DesignParts.cs has the map of the files): the hull bodies' bands (<see cref="AddBands"/>), the joins between
/// bodies (<see cref="AddJoins"/>), and the convex colliders an element takes from its mesh (<see cref="HullOf"/>, which joins and pieces use too).
/// </summary>
public static partial class DesignParts
{
    /// <summary>The hull bodies: each copy's bands, its skin and cladding cut between their rings (the end caps with the end bands).</summary>
    private static IEnumerable<int> AddBands(Steps st)
    {
        var (d, els) = (st.D, st.Els);
        _hc = default;
        for (var hi = 0; hi < d.Hulls.Count; hi++)
        {
            var h = d.Hulls[hi];
            var rings = h.Ring;
            if (rings.Count < 2)
                continue;
            var tint = Tint(h.Material);
            var src = d.CladSource(h);
            var cladTint = Tint(src.CladMaterial is "Hull" or "" ? h.Material : src.CladMaterial);
            var zs = rings.Select(r => h.Z + r.Z).ToArray();
            for (var copy = 0; copy < h.Copies; copy++)
            {
                var first = els.Count;
                for (var i = 0; i + 1 < rings.Count; i++)
                    els.Add(new Element { Id = $"h{hi}.{copy}.b{i}", Kind = "band", Name = $"{h.Name}{(h.Copies > 1 ? copy == 0 ? " (right)" : " (left)" : "")}, band {i + 1}", Mesh = new LookBuilder() });
                Bands(h, copy, els, first);
                yield return els.Count;
                var (skin, fore, aft, sc, fc, ac) = DesignScene.HullMeshes(d, h, copy);
                yield return els.Count;
                var axis = new Vector3((float)h.CopyX(copy), (float)(h.Y + 0.5 * (h.Bounds().Y0 + h.Bounds().Y1) - h.Y), float.NaN);
                void Slice(DesignMesh m, Vector3 t)
                {
                    for (var tr = 0; tr < m.Tris; tr++)
                    {
                        var z = (m.P[tr * 9 + 2] + m.P[tr * 9 + 5] + m.P[tr * 9 + 8]) / 3;
                        var b = Array.BinarySearch(zs, z);
                        b = b >= 0 ? b : ~b - 1;
                        els[first + Math.Clamp(b, 0, rings.Count - 2)].Mesh!.AddTri(m, tr, t, axis);
                    }
                }
                Slice(skin, tint);
                Slice(fore, tint);
                Slice(aft, tint);
                Slice(sc, cladTint);
                Slice(fc, cladTint);
                Slice(ac, cladTint);
                foreach (var _ in Plates(h, copy, els, first, zs, skin, fore, aft)) // (the colliders: thin plates where the skin is, its openings left open)
                    yield return els.Count;
                for (var k = first; k < els.Count; k++)
                    if (k > first)
                        Weld(els, k - 1, k);
                st.BandOf[(h.Id, copy)] = (first, rings.Count - 1, zs);
                yield return els.Count;
            }
        }
    }

    /// <summary>Joins: one element a side (a mirrored one's twin is its own), welded to the bands it meets.</summary>
    private static IEnumerable<int> AddJoins(Steps st)
    {
        var (d, els) = (st.D, st.Els);
        for (var ji = 0; ji < d.Joins.Count; ji++)
        {
            var j = d.Joins[ji];
            var m = j.Mesh(d);
            var tint = Tint(j.PaintOf(d).Material);
            var sides = j.Mirror ? 2 : 1;
            for (var s = 0; s < sides; s++)
            {
                var e = new Element { Id = sides > 1 ? $"j{ji}.{s}" : $"j{ji}", Kind = "join", Name = $"join {ji + 1}{(sides > 1 ? s == 0 ? " (right)" : " (left)" : "")}", Mesh = new LookBuilder() };
                var pts = new List<double>();
                for (var tr = 0; tr < m.Tris; tr++)
                {
                    var x = m.P[tr * 9] + m.P[tr * 9 + 3] + m.P[tr * 9 + 6];
                    if (sides > 1 && (x < 0) != (s == 1))
                        continue;
                    e.Mesh.AddTri(m, tr, tint, new Vector3(float.NaN));
                    for (var k = 0; k < 9; k++)
                        pts.Add(m.P[tr * 9 + k]);
                }
                if (pts.Count == 0)
                    continue;
                e.Area = e.Mesh.Area;
                e.Kg = e.Area * SkinKgPerM2;
                HullOf(e, pts);
                var at = els.Count;
                els.Add(e);
                var mid = (e.BoxMin + e.BoxMax) * 0.5f;
                foreach (var end in new[] { j.A, j.B })
                    if (st.BandAt(end.Hull, s == 1 && sides > 1 ? 1 - end.Copy : end.Copy, mid.Z) is var b and >= 0)
                        Weld(els, b, at);
            }
            yield return els.Count;
        }
    }

    // ---- Colliders: a hollow hull (step 4, package B) ------------------------------------------------------------------------
    // The skin collides as thin plates, one a facet of the loft (each side's profile step, the keel and the spine, between two rings),
    // thickened inwards, and the two true end caps in strips: a person fits inside and walks on the rooms' floors. A facet is plated where
    // the designer's own skin mesh covers it (HullMeshes, with every socket's opening, connection mouth and cutting body already cut), so the
    // openings are the designer's, nothing worked out again: a facet the skin covers whole is one plate, one it covers in part is tested in
    // cells and the cells it covers merged into rectangles, one it doesn't cover at all is open.

    public const float PlateM = 0.05f; // a hull plate's collider, inwards from the skin (thin: tunnelling is the risk; thicker eats the rooms)
    private const float PlateCell = 0.2f; // m: the cells a part-covered facet is tested in (the designer cuts holed bands at 20 cm too)
    private const float OnSkin = 0.05f; // m: how far a skin triangle may lie off its facet (dents, a twisted facet) and still cover it

    /// <summary>What the last design's hollow hull came to (a check prints it): plates, facets whole, in cells and open, plates refused, pieces clipped.</summary>
    public static string LastHollow = "";

    [ThreadStatic] private static HollowCount _hc;

    private struct HollowCount
    {
        public int Plates, Whole, Celled, Open, Refused, Clipped, ClipHulls, Dropped, InHole;
        public double PlateMs; // (the longest band's plates took, a step)
    }

    /// <summary>A body copy's bands from element <paramref name="first"/> on: each one's skin area, mass and bounds (its plates are <see cref="Plates"/>'s).</summary>
    private static void Bands(HullBody h, int copy, List<Element> els, int first)
    {
        var rings = h.Ring;
        var cx = h.CopyX(copy);
        Span<double> ah = stackalloc double[HullBody.MaxLoft], ay = stackalloc double[HullBody.MaxLoft];
        Span<double> bh = stackalloc double[HullBody.MaxLoft], by = stackalloc double[HullBody.MaxLoft];
        for (var i = 0; i + 1 < rings.Count; i++)
        {
            var e = els[first + i];
            var za = (float)(h.Z + rings[i].Z);
            var zb = (float)(h.Z + rings[i + 1].Z);
            var n = h.Pair(i, ah, ay, bh, by);
            var sh = ah.ToArray();
            var sy = ay.ToArray();
            var th = bh.ToArray();
            var ty = by.ToArray();
            Vector3 A(int k, int s) => new((float)(cx + s * sh[k]), (float)(h.Y + sy[k]), za);
            Vector3 B(int k, int s) => new((float)(cx + s * th[k]), (float)(h.Y + ty[k]), zb);
            var area = 0f;
            for (var k = 0; k + 1 < n; k++)
                area += 2 * Quad(A(k, 1), A(k + 1, 1), B(k + 1, 1), B(k, 1));
            area += Quad(A(0, -1), A(0, 1), B(0, 1), B(0, -1)) + Quad(A(n - 1, -1), A(n - 1, 1), B(n - 1, 1), B(n - 1, -1));
            e.Area = area;
            e.Kg = area * SkinKgPerM2;
            Vector3 lo = new(float.MaxValue), hi = new(float.MinValue);
            for (var k = 0; k < n; k++)
                foreach (var v in new[] { A(k, 1), A(k, -1), B(k, 1), B(k, -1) })
                    (lo, hi) = (Vector3.Min(lo, v), Vector3.Max(hi, v));
            (e.BoxMin, e.BoxMax) = (lo, hi);
        }
    }

    private readonly record struct Tri(Vector3 A, Vector3 B, Vector3 C, Vector3 Lo, Vector3 Hi, Vector3 N, float Area);

    /// <summary>
    /// A body copy's skin and end caps (<paramref name="skin"/>, <paramref name="fore"/>, <paramref name="aft"/>: its drawn meshes, already cut
    /// for every opening) into each band's plates: every facet of the loft between its two rings, and the end caps on the end bands. A step a band
    /// (the held spawn's ticks).
    /// </summary>
    private static IEnumerable<int> Plates(HullBody h, int copy, List<Element> els, int first, double[] zs, DesignMesh skin, DesignMesh fore, DesignMesh aft)
    {
        var rings = h.Ring;
        var bands = rings.Count - 1;
        var cx = h.CopyX(copy);
        var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        // Each band's triangles, by the z of their middle (as Slice cuts the look).
        var tris = new List<Tri>[bands];
        for (var b = 0; b < bands; b++)
            tris[b] = [];
        foreach (var m in new[] { skin, fore, aft })
            for (var tr = 0; tr < m.Tris; tr++)
            {
                var i = tr * 9;
                var a = new Vector3((float)m.P[i], (float)m.P[i + 1], (float)m.P[i + 2]);
                var b = new Vector3((float)m.P[i + 3], (float)m.P[i + 4], (float)m.P[i + 5]);
                var c = new Vector3((float)m.P[i + 6], (float)m.P[i + 7], (float)m.P[i + 8]);
                var nn = Vector3.Cross(b - a, c - a);
                var len = nn.Length();
                if (len < 1e-7f)
                    continue;
                var at = Array.BinarySearch(zs, (double)((a.Z + b.Z + c.Z) / 3));
                at = Math.Clamp(at >= 0 ? at : ~at - 1, 0, bands - 1);
                tris[at].Add(new Tri(a, b, c, Vector3.Min(a, Vector3.Min(b, c)), Vector3.Max(a, Vector3.Max(b, c)), nn / len, 0.5f * len));
            }
        double[] ah = new double[HullBody.MaxLoft], ay = new double[HullBody.MaxLoft], bh = new double[HullBody.MaxLoft], by = new double[HullBody.MaxLoft];
        var hulls = new List<ConvexHull>();
        var near = new List<Tri>();
        for (var i = 0; i < bands; i++)
        {
            if (i > 0)
            {
                _hc.PlateMs = Math.Max(_hc.PlateMs, System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds);
                yield return i;
                t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            }
            var e = els[first + i];
            var za = (float)zs[i];
            var zb = (float)zs[i + 1];
            var n = h.Pair(i, ah, ay, bh, by);
            if (n < 2)
                continue;
            var pa = new Vector3[n];
            var pb = new Vector3[n];
            for (var k = 0; k < n; k++)
            {
                pa[k] = new Vector3((float)ah[k], (float)(h.Y + ay[k]), za);
                pb[k] = new Vector3((float)bh[k], (float)(h.Y + by[k]), zb);
            }
            Vector3 A(int k, int s) => new((float)(cx + s * pa[k].X), pa[k].Y, za);
            Vector3 B(int k, int s) => new((float)(cx + s * pb[k].X), pb[k].Y, zb);
            var cy = 0f;
            for (var k = 0; k < n; k++)
                cy += pa[k].Y + pb[k].Y;
            var centre = new Vector3((float)cx, cy / (2 * n), (za + zb) / 2);
            hulls.Clear();
            var band = tris[i];
            if (MathF.Abs(zb - za) >= 1e-3f)
            {
                foreach (var s in new[] { -1, 1 })
                    for (var k = 0; k + 1 < n; k++)
                        Facet(A(k, s), A(k + 1, s), B(k, s), B(k + 1, s), centre, band, near, hulls);
                Facet(A(0, -1), A(0, 1), B(0, -1), B(0, 1), centre, band, near, hulls); // the keel
                Facet(A(n - 1, 1), A(n - 1, -1), B(n - 1, 1), B(n - 1, -1), centre, band, near, hulls); // the spine
            }
            // The true end caps only (the rings between bands are open): in strips across the profile.
            if (i == 0)
                for (var k = 0; k + 1 < n; k++)
                    Facet(A(k, -1), A(k, 1), A(k + 1, -1), A(k + 1, 1), centre with { Z = za + 1 }, band, near, hulls);
            if (i == bands - 1)
                for (var k = 0; k + 1 < n; k++)
                    Facet(B(k, -1), B(k, 1), B(k + 1, -1), B(k + 1, 1), centre with { Z = zb - 1 }, band, near, hulls);
            e.Hulls = [.. hulls];
        }
        _hc.PlateMs = Math.Max(_hc.PlateMs, System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds);
    }

    /// <summary>
    /// One facet of the loft (corners p00 p10 along the profile, p01 p11 a ring on), plated where the skin's triangles cover it, each plate
    /// <see cref="PlateM"/> thick towards <paramref name="inside"/>.
    /// </summary>
    private static void Facet(Vector3 p00, Vector3 p10, Vector3 p01, Vector3 p11, Vector3 inside, List<Tri> band, List<Tri> near, List<ConvexHull> hulls)
    {
        var area = Quad(p00, p10, p11, p01);
        if (area < 1e-4f)
            return;
        var nrm = Vector3.Cross(p10 - p00 + (p11 - p01), p01 - p00 + (p11 - p10));
        if (nrm.LengthSquared() < 1e-12f)
            return;
        nrm = Vector3.Normalize(nrm);
        if (Vector3.Dot(nrm, inside - (p00 + p10 + p01 + p11) * 0.25f) < 0)
            nrm = -nrm;
        var lo = Vector3.Min(Vector3.Min(p00, p10), Vector3.Min(p01, p11)) - new Vector3(OnSkin);
        var hi = Vector3.Max(Vector3.Max(p00, p10), Vector3.Max(p01, p11)) + new Vector3(OnSkin);
        near.Clear();
        var covered = 0f;
        foreach (var t in band)
        {
            if (t.Hi.X < lo.X || t.Lo.X > hi.X || t.Hi.Y < lo.Y || t.Lo.Y > hi.Y || t.Hi.Z < lo.Z || t.Lo.Z > hi.Z || MathF.Abs(Vector3.Dot(t.N, nrm)) < 0.5f)
                continue;
            near.Add(t);
            var mid = (t.A + t.B + t.C) / 3;
            if (OnTri(mid, p00, p10, p11) || OnTri(mid, p00, p11, p01))
                covered += t.Area;
        }
        if (covered >= 0.995f * area)
        {
            _hc.Whole++;
            Plate(0, 1, 0, 1);
            return;
        }
        // Not covered whole by triangles lying in it (an opening, a mouth, another body's cut; or an end cap's fan, whose triangles cross
        // several strips): in cells, each cell whose middle the skin covers plated, merged in rectangles.
        var nu = Math.Clamp((int)MathF.Ceiling(MathF.Max(Vector3.Distance(p00, p10), Vector3.Distance(p01, p11)) / PlateCell), 1, 128);
        var nt = Math.Clamp((int)MathF.Ceiling(MathF.Max(Vector3.Distance(p00, p01), Vector3.Distance(p10, p11)) / PlateCell), 1, 512);
        var cov = new bool[nu * nt];
        var count = 0;
        var last = 0;
        for (var t = 0; t < nt; t++)
            for (var u = 0; u < nu; u++)
            {
                var p = At((u + 0.5f) / nu, (t + 0.5f) / nt);
                for (var k = 0; k < near.Count; k++)
                {
                    var tr = near[(last + k) % near.Count]; // (the one that held the last cell first: neighbours mostly share one)
                    if (p.X < tr.Lo.X - OnSkin || p.X > tr.Hi.X + OnSkin || p.Y < tr.Lo.Y - OnSkin || p.Y > tr.Hi.Y + OnSkin || p.Z < tr.Lo.Z - OnSkin || p.Z > tr.Hi.Z + OnSkin || !OnTri(p, tr.A, tr.B, tr.C))
                        continue;
                    cov[t * nu + u] = true;
                    count++;
                    last = (last + k) % near.Count;
                    break;
                }
            }
        if (count == cov.Length)
        {
            _hc.Whole++;
            Plate(0, 1, 0, 1);
            return;
        }
        if (count == 0)
        {
            _hc.Open++;
            return;
        }
        _hc.Celled++;
        for (var t = 0; t < nt; t++)
            for (var u = 0; u < nu; u++)
            {
                if (!cov[t * nu + u])
                    continue;
                var u1 = u;
                while (u1 + 1 < nu && cov[t * nu + u1 + 1])
                    u1++;
                var t1 = t;
                while (t1 + 1 < nt && Row(t1 + 1, u, u1))
                    t1++;
                for (var tt = t; tt <= t1; tt++)
                    for (var uu = u; uu <= u1; uu++)
                        cov[tt * nu + uu] = false;
                Plate(u / (float)nu, (u1 + 1) / (float)nu, t / (float)nt, (t1 + 1) / (float)nt);
            }

        bool Row(int t, int u0, int u1)
        {
            for (var u = u0; u <= u1; u++)
                if (!cov[t * nu + u])
                    return false;
            return true;
        }
        Vector3 At(float u, float t) => Vector3.Lerp(Vector3.Lerp(p00, p10, u), Vector3.Lerp(p01, p11, u), t);
        void Plate(float u0, float u1, float t0, float t1)
        {
            Span<Vector3> c = stackalloc Vector3[8];
            (c[0], c[1], c[2], c[3]) = (At(u0, t0), At(u1, t0), At(u0, t1), At(u1, t1));
            for (var k = 0; k < 4; k++)
                c[4 + k] = c[k] + nrm * PlateM;
            if (ConvexHull.Of(c) is { } hl)
            {
                hulls.Add(hl);
                _hc.Plates++;
            }
            else
                _hc.Refused++;
        }
    }

    /// <summary>Whether <paramref name="p"/> lies on triangle a b c: within <see cref="OnSkin"/> of its plane and inside its edges (a hair's give).</summary>
    private static bool OnTri(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        var n = Vector3.Cross(b - a, c - a);
        var nn = n.LengthSquared();
        if (nn < 1e-14f)
            return false;
        var d = Vector3.Dot(p - a, n);
        if (d * d > OnSkin * OnSkin * nn)
            return false;
        var q = p - n * (d / nn);
        const float Give = -1e-4f;
        return Vector3.Dot(Vector3.Cross(b - a, q - a), n) >= Give * nn && Vector3.Dot(Vector3.Cross(c - b, q - b), n) >= Give * nn && Vector3.Dot(Vector3.Cross(a - c, q - c), n) >= Give * nn;
    }

    /// <summary>
    /// Before the elements go into their frames: each join's and piece's collider kept out of the rooms and out of the sockets' openings (a
    /// piece that reaches into one keeps only what is outside them all, split by the side of each it is on, so no hull bridges a room or closes
    /// a hole: a hatch keeps its frame), and the design's colliders held to <see cref="MaxHulls"/> (the last left out, logged).
    /// </summary>
    private static IEnumerable<int> ClipHulls(Steps st)
    {
        var (d, els) = (st.D, st.Els);
        var keeps = KeepOuts(d, out var rooms);
        var pts = new List<Vector3>();
        foreach (var e in els)
        {
            if (e.Kind is not ("join" or "piece") || e.Hulls.Length != 1 || e.Mesh == null || keeps.Count == 0)
                continue;
            var p = e.Mesh.Pos;
            pts.Clear();
            for (var i = 0; i + 2 < p.Count; i += 3)
                pts.Add(new Vector3(p[i], p[i + 1], p[i + 2]));
            if (Reaching(pts, keeps) < 0)
                continue;
            var all = pts.Count;
            var inRoom = 0;
            pts.RemoveAll(v =>
            {
                for (var k = 0; k < keeps.Count; k++)
                    if (keeps[k].Holds(v))
                    {
                        inRoom += k < rooms ? 1 : 0;
                        return true;
                    }
                return false;
            });
            if (pts.Count < 4)
            {
                // All of it inside: a fitting standing in a room keeps its collider; a piece wholly in its own opening (a lid) keeps it too, logged.
                if (inRoom < all)
                    _hc.InHole++;
                continue;
            }
            var kept = new List<ConvexHull>();
            Split(pts, keeps, 0, kept);
            if (kept.Count == 0)
                continue;
            e.Hulls = [.. kept];
            _hc.Clipped++;
            _hc.ClipHulls += kept.Count;
        }
        yield return els.Count;
        var total = 0;
        foreach (var e in els)
            total += e.Hulls.Length;
        if (total > MaxHulls)
        {
            var over = total - MaxHulls;
            _hc.Dropped = over;
            for (var k = els.Count - 1; k >= 0 && over > 0; k--)
            {
                var cut = Math.Min(over, els[k].Hulls.Length);
                els[k].Hulls = els[k].Hulls[..^cut];
                over -= cut;
            }
            Console.WriteLine($"[design] \"{d.Name}\" makes {total} colliders, past the {MaxHulls} a design may: the last {total - MaxHulls} (from the end of its elements) are left out");
        }
        if (_hc.Refused > 0)
            Console.WriteLine($"[design] \"{d.Name}\": {_hc.Refused} hull plates came out flat and are left out (holes in its collider)");
        if (_hc.InHole > 0)
            Console.WriteLine($"[design] \"{d.Name}\": {_hc.InHole} pieces lie wholly in a socket's opening and keep their collider there (it closes the hole)");
        LastHollow = $"{Math.Min(total, MaxHulls)} colliders (of {MaxHulls} at most): {_hc.Plates} plates ({_hc.Whole} facets whole, {_hc.Celled} in cells, {_hc.Open} open), {_hc.Refused} refused, the longest band's in {_hc.PlateMs:0.0} ms; "
            + $"{_hc.Clipped} joins or pieces kept out of {rooms} rooms and {keeps.Count - rooms} openings ({_hc.ClipHulls} hulls), {_hc.InHole} wholly in an opening" + (_hc.Dropped > 0 ? $"; {_hc.Dropped} left out past the cap" : "");
        _hc = default;
    }

    /// <summary>A box colliders are kept out of (a room, or a socket's opening through the skin and in and out from it), in its own frame.</summary>
    private readonly record struct KeepOut(Vector3 C, Vector3 X, Vector3 Y, Vector3 Z, Vector3 Half)
    {
        public Vector3 Local(Vector3 v) => new(Vector3.Dot(v - C, X), Vector3.Dot(v - C, Y), Vector3.Dot(v - C, Z));

        public bool Holds(Vector3 v)
        {
            var l = Local(v);
            return MathF.Abs(l.X) < Half.X && MathF.Abs(l.Y) < Half.Y && MathF.Abs(l.Z) < Half.Z;
        }
    }

    // How far in from the skin and out from it a socket's opening is kept clear of piece colliders (m).
    private const float OpeningIn = 3f, OpeningOut = 1.5f;

    /// <summary>The rooms (the first <paramref name="rooms"/>, 3 cm inside their walls) and every socket's opening on the skin (each copy, mirror and pair, as HullMeshes cuts it).</summary>
    private static List<KeepOut> KeepOuts(ShipDesign d, out int rooms)
    {
        var l = new List<KeepOut>();
        foreach (var r in d.Rooms)
        {
            var b = d.Box(r);
            var (lo, hi) = (new Vector3((float)b[0], (float)b[2], (float)b[4]) + new Vector3(0.03f), new Vector3((float)b[1], (float)b[3], (float)b[5]) - new Vector3(0.03f));
            if (lo.X < hi.X && lo.Y < hi.Y && lo.Z < hi.Z)
                l.Add(new KeepOut((lo + hi) * 0.5f, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, (hi - lo) * 0.5f));
        }
        rooms = l.Count;
        foreach (var p in d.Pieces)
        {
            if (d.Hull(p.Hull) is not { } h || PieceKinds.Find(p.Kind) is not { Opening: not null } pk || PieceDocking.FootprintOf(p, pk) is not { } op)
                continue;
            var w = DesignScene.Where(h, p);
            if (DesignHangar.OpeningOf(p, w) is { } sized)
                op = sized;
            var hs = p.Size * p.Hole;
            var places = new List<DesignScene.Place> { w };
            if (p.Mirror)
                places.Add(Mirror(w, h.CopyX(Math.Min(p.Copy, h.Copies - 1))));
            if (p.Pair && h.Copies == 2)
                for (int k = 0, n = places.Count; k < n; k++)
                    places.Add(Mirror(places[k], 0));
            foreach (var q in places)
            {
                // (Just inside its edge, so a hatch's frame on the edge stays.)
                var half = new Vector3((float)(op.Hx * hs) * 0.97f, (OpeningIn + OpeningOut) / 2, (float)((op.Round ? op.Hx : op.Hz) * hs) * 0.97f);
                l.Add(new KeepOut(V(q.O) + V(q.Y) * ((OpeningOut - OpeningIn) / 2), V(q.X), V(q.Y), V(q.Z), half));
            }
        }
        return l;
    }

    /// <summary>The first keep-out these points' bounds (in its frame) reach into by more than 5 cm every way, or -1.</summary>
    private static int Reaching(List<Vector3> pts, List<KeepOut> keeps)
    {
        for (var r = 0; r < keeps.Count; r++)
        {
            var k = keeps[r];
            Vector3 lo = new(float.MaxValue), hi = new(float.MinValue);
            foreach (var v in pts)
            {
                var l = k.Local(v);
                (lo, hi) = (Vector3.Min(lo, l), Vector3.Max(hi, l));
            }
            if (MathF.Min(hi.X, k.Half.X) - MathF.Max(lo.X, -k.Half.X) > 0.05f && MathF.Min(hi.Y, k.Half.Y) - MathF.Max(lo.Y, -k.Half.Y) > 0.05f && MathF.Min(hi.Z, k.Half.Z) - MathF.Max(lo.Z, -k.Half.Z) > 0.05f)
                return r;
        }
        return -1;
    }

    /// <summary>Points outside every keep-out into hulls: while they reach into one, split by the side of it each point is on.</summary>
    private static void Split(List<Vector3> pts, List<KeepOut> keeps, int depth, List<ConvexHull> into)
    {
        var r = depth < 4 ? Reaching(pts, keeps) : -1;
        if (r < 0)
        {
            if (Extreme(pts) is { } one)
                into.Add(one);
            return;
        }
        var k = keeps[r];
        var sides = new List<Vector3>[6];
        Span<float> out6 = stackalloc float[6];
        foreach (var v in pts)
        {
            var l = k.Local(v);
            (out6[0], out6[1], out6[2], out6[3], out6[4], out6[5]) = (-l.X - k.Half.X, l.X - k.Half.X, -l.Y - k.Half.Y, l.Y - k.Half.Y, -l.Z - k.Half.Z, l.Z - k.Half.Z);
            var best = 0;
            for (var s = 1; s < 6; s++)
                if (out6[s] > out6[best])
                    best = s;
            (sides[best] ??= []).Add(v);
        }
        foreach (var s in sides)
            if (s is { Count: >= 4 })
                Split(s, keeps, depth + 1, into);
    }

    /// <summary>The 26 directions a hull of a mesh is taken along (the cube's faces, edges and corners): its extreme point along each.</summary>
    private static readonly Vector3[] Dirs = MakeDirs();

    private static Vector3[] MakeDirs()
    {
        var l = new List<Vector3>();
        for (var x = -1; x <= 1; x++)
            for (var y = -1; y <= 1; y++)
                for (var z = -1; z <= 1; z++)
                    if (x != 0 || y != 0 || z != 0)
                        l.Add(Vector3.Normalize(new Vector3(x, y, z)));
        return [.. l];
    }

    /// <summary>An element's hull from its mesh's points (the extreme point along each of <see cref="Dirs"/>) and its bounds.</summary>
    private static void HullOf(Element e, IReadOnlyList<double> p)
    {
        Span<Vector3> best = stackalloc Vector3[26];
        Span<float> at = stackalloc float[26];
        at.Fill(float.MinValue);
        Vector3 lo = new(float.MaxValue), hi = new(float.MinValue);
        for (var i = 0; i + 2 < p.Count; i += 3)
        {
            var v = new Vector3((float)p[i], (float)p[i + 1], (float)p[i + 2]);
            (lo, hi) = (Vector3.Min(lo, v), Vector3.Max(hi, v));
            for (var k = 0; k < Dirs.Length; k++)
            {
                var s = Vector3.Dot(v, Dirs[k]);
                if (s > at[k])
                    (at[k], best[k]) = (s, v);
            }
        }
        (e.BoxMin, e.BoxMax) = (lo, hi);
        if (ConvexHull.Of(best) is { } hull)
            e.Hulls = [hull];
    }

    /// <summary>A hull of points: the extreme one along each of <see cref="Dirs"/> (null: they lie flat).</summary>
    private static ConvexHull? Extreme(List<Vector3> pts)
    {
        Span<Vector3> best = stackalloc Vector3[26];
        Span<float> at = stackalloc float[26];
        at.Fill(float.MinValue);
        foreach (var v in pts)
            for (var k = 0; k < Dirs.Length; k++)
            {
                var s = Vector3.Dot(v, Dirs[k]);
                if (s > at[k])
                    (at[k], best[k]) = (s, v);
            }
        return ConvexHull.Of(best);
    }

    private static float Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d) => 0.5f * Vector3.Cross(c - a, d - b).Length();
}
