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
                    els.Add(new Element { Id = $"h{hi}.{copy}.b{i}", Kind = "band", Name = $"{h.Name}{(h.Copies > 1 ? copy == 0 ? " (right)" : " (left)" : "")}, band {i + 1}", Mesh = new LookBuilder(), Points = [], Runs = [] });
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

    // ---- Colliders ---------------------------------------------------------------------------------------------------------

    /// <summary>A body copy's bands from element <paramref name="first"/> on: each one's hull points (one run, or a few across a big profile), skin area, mass and bounds.</summary>
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
            if (MathF.Abs(zb - za) < 1e-3f)
                continue;
            var pts = e.Points!;
            if (4 * n <= ConvexHull.MaxCorners)
            {
                for (var k = 0; k < n; k++)
                {
                    pts.Add(A(k, 1)); pts.Add(A(k, -1)); pts.Add(B(k, 1)); pts.Add(B(k, -1));
                }
                e.Runs!.Add(pts.Count);
                continue;
            }
            // Too many corners for one: each side in runs of points, each run closed on the centreline.
            const int Run = ConvexHull.MaxCorners / 2 - 3;
            foreach (var s in new[] { -1, 1 })
                for (var k0 = 0; k0 < n - 1; k0 += Run - 1)
                {
                    var k1 = Math.Min(n - 1, k0 + Run - 1);
                    for (var k = k0; k <= k1; k++)
                    {
                        pts.Add(A(k, s)); pts.Add(B(k, s));
                    }
                    pts.Add(A(k0, 0)); pts.Add(A(k1, 0)); pts.Add(B(k0, 0)); pts.Add(B(k1, 0));
                    e.Runs!.Add(pts.Count);
                }
        }
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

    private static float Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d) => 0.5f * Vector3.Cross(c - a, d - b).Length();
}
