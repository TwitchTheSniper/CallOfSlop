using System.Numerics;
using CallOfSlop.ShipDesigner;

namespace CallOfSlop;

/// <summary>
/// Design parts, the hull windows (DesignParts.cs has the map of the files): each window laid on the skin (<see cref="HullWindow"/>) is an
/// element of its own, its glass, reveal and boarding as <see cref="DesignScene.HullWindowMesh"/> makes them (Karl, 2026-11-27: only the
/// designer's page drew a hull window's glass, so in the game it was a hole in the skin onto the room's wall). Its glass is drawn see-through
/// (<see cref="LookBuilder"/>), and it is welded to the band of the body it lies on there.
/// </summary>
public static partial class DesignParts
{
    private static IEnumerable<int> AddWindows(Steps st)
    {
        var (d, els) = (st.D, st.Els);
        foreach (var w in d.Windows)
        {
            var m = DesignScene.HullWindowMesh(d, w);
            if (m.Tris == 0)
                continue;
            var e = new Element { Id = $"w{w.Id}", Kind = "piece", Name = "Hull window", Mesh = new LookBuilder() };
            for (var t = 0; t < m.Tris; t++)
                e.Mesh.AddTri(m, t, Vector3.One, new Vector3(float.NaN));
            e.Area = e.Mesh.Area;
            e.Kg = e.Area * PieceKgPerM2;
            HullOf(e, m.P);
            var at = els.Count;
            els.Add(e);
            // Welded to the band its middle is in (of any body's copy), else the nearest by its z.
            var mid = (e.BoxMin + e.BoxMax) * 0.5f;
            var band = -1;
            foreach (var ((hull, copy), _) in st.BandOf)
                if (st.BandAt(hull, copy, mid.Z) is var b and >= 0 && (band < 0 || Inside(els[b], mid)))
                    band = b;
            if (band >= 0)
                Weld(els, band, at);
            yield return els.Count;
        }

        static bool Inside(Element b, Vector3 p) => p.X >= b.BoxMin.X && p.X <= b.BoxMax.X && p.Y >= b.BoxMin.Y && p.Y <= b.BoxMax.Y;
    }
}
