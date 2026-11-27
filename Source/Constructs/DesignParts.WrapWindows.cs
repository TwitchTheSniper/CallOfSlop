using System.Numerics;
using CallOfSlop.ShipDesigner;

namespace CallOfSlop;

/// <summary>
/// Design parts, the wraparound windows (DesignParts.cs has the map of the files): each <see cref="WrapWindow"/> an element of its own, its
/// glass, frame, sill and header as <see cref="DesignScene.WrapMesh"/> makes them, the glass drawn see-through. Its colliders are thin hulls,
/// one a few degrees of glass at a time and one for each such run of its sill and its header, so the cockpit behind it stays open; it is
/// welded to the band of its body at its foot.
/// </summary>
public static partial class DesignParts
{
    private static IEnumerable<int> AddWrapWindows(Steps st)
    {
        var (d, els) = (st.D, st.Els);
        foreach (var w in d.WrapWindows)
        {
            var l = DesignScene.WrapOnce(d, w);
            if (!l.Ok)
                continue;
            var m = DesignScene.WrapMesh(d, w);
            if (m.Tris == 0)
                continue;
            var e = new Element { Id = $"ww{w.Id}", Kind = "piece", Name = "Wraparound window", Mesh = new LookBuilder() };
            // (each face lit from outside the body's axis: the sill and header are seen from out and in)
            var middle = new Vector3((float)l.Ax, (float)l.Ay, float.NaN);
            for (var t = 0; t < m.Tris; t++)
                e.Mesh.AddTri(m, t, Vector3.One, middle);
            e.Area = e.Mesh.Area;
            e.Kg = e.Area * PieceKgPerM2;
            HullOf(e, m.P); // (its bounds; the hulls below stand for it)
            var hulls = new List<ConvexHull>();
            static Vector3 V(double[] p) => new((float)p[0], (float)p[1], (float)p[2]);
            void Thin(IEnumerable<double[]> pts, double[] n)
            {
                var list = new List<Vector3>();
                var nv = V(n) * 0.03f;
                foreach (var p in pts)
                {
                    list.Add(V(p));
                    list.Add(V(p) - nv);
                }
                if (Extreme(list) is { } hl)
                    hulls.Add(hl);
            }
            for (var a = 0; a < l.N - 1; a += 2)
            {
                var b = Math.Min(l.N - 1, a + 2);
                var n = l.Nm[a];
                Thin([l.B[a], l.B[b], l.T[a], l.T[b]], n);
                Thin([l.B[a], l.B[b], l.Sc[a], l.Sc[b]], l.R[a]);
                Thin([l.T[a], l.T[b], l.Hc[a], l.Hc[b]], l.R[a]);
            }
            e.Hulls = [.. hulls];
            var at = els.Count;
            els.Add(e);
            var foot = (float)(d.Hull(w.Hull) is { } h ? h.Z + w.Z : (e.BoxMin.Z + e.BoxMax.Z) / 2);
            var band = -1;
            foreach (var ((hull, copy), _) in st.BandOf)
                if (hull == w.Hull && copy == 0 && st.BandAt(hull, copy, foot) is var bd and >= 0)
                    band = bd;
            if (band < 0)
                foreach (var ((hull, copy), _) in st.BandOf)
                    if (st.BandAt(hull, copy, foot) is var bd and >= 0)
                        band = bd;
            if (band >= 0)
                Weld(els, band, at);
            yield return els.Count;
        }
    }
}
