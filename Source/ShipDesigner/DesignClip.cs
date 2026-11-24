namespace CallOfSlop.ShipDesigner;

/// <summary>
/// Rooms kept inside the ship (the user, 2026-11-24: a room's hologram and its shell poked out through the curved upper hull): every vertex
/// of a room's meshes that is outside all the bodies, or closer to a skin than an inset, is pulled in onto that skin less the inset. A box
/// face against the skin so bends to it, and a shell's outward thickness, roof and corners all end inside.
/// </summary>
public static partial class DesignScene
{
    /// <summary>The insets (m): the shell's outer face, the hologram inside it and the interior cladding inside that, so none share a face.</summary>
    public const double ShellInset = 0.012, HoloInset = 0.022, InteriorInset = 0.03;

    /// <summary>Pulls every vertex of a world-space mesh to at least inset inside a body's skin.</summary>
    public static void ClipToHulls(ShipDesign d, DesignMesh m, double inset)
    {
        if (d.Hulls.Count == 0)
            return;
        var p = m.P;
        for (var i = 0; i + 2 < p.Count; i += 3)
        {
            double x = p[i], y = p[i + 1], z = p[i + 2];
            for (var pass = 0; pass < 3; pass++)
            {
                HullBody? best = null;
                var bd = double.NegativeInfinity;
                foreach (var h in d.Hulls)
                {
                    var dh = h.Depth(x, y, z);
                    if (dh > bd)
                        (bd, best) = (dh, h);
                }
                if (best is null || bd >= inset * 0.95)
                    break;
                var h0 = best;
                var copy = h0.Copies == 2 && Math.Abs(x - h0.CopyX(1)) < Math.Abs(x - h0.CopyX(0)) ? 1 : 0;
                var e = inset * (1 + pass); // a profile's corner: pulled in further on the next pass
                double lx = x - h0.CopyX(copy), ly = y - h0.Y, lz = Math.Clamp(z - h0.Z, h0.Front + e, h0.Back - e);
                var s = h0.OnSkin(lz, h0.NearestT(lx, ly, lz), lx < -0.01 ? -1 : 1);
                (x, y, z) = (h0.CopyX(copy) + s.X - s.Nx * e, h0.Y + s.Y - s.Ny * e, h0.Z + lz);
            }
            (p[i], p[i + 1], p[i + 2]) = (x, y, z);
        }
    }

    /// <summary>
    /// The hologram of a room not out to the hull: its box (3 cm in from its own, as the page drew it) with each face cut into a grid about
    /// 40 cm square, so pulled inside the skin it bends to the hull's curve. Its floor slab is the page's own.
    /// </summary>
    public static DesignMesh RoomBoxMesh(ShipDesign d, Room r)
    {
        var m = new DesignMesh();
        m.World();
        var b = d.Box(r);
        var col = DesignMesh.Hex(RoomKinds.Find(r.Kind)!.Colour);
        GridBox(m, b[0] + 0.03, b[2] + 0.03, b[4] + 0.03, b[1] - 0.03, b[3] - 0.03, b[5] - 0.03, col, 0.4);
        ClipToHulls(d, m, HoloInset);
        return m;
    }

    /// <summary>An axis-aligned box whose six faces are grids of cells about step square.</summary>
    public static void GridBox(DesignMesh m, double x0, double y0, double z0, double x1, double y1, double z1, (byte, byte, byte) col, double step)
    {
        double[] lo = [x0, y0, z0], hi = [x1, y1, z1];
        for (var ax = 0; ax < 3; ax++)
        {
            int a1 = (ax + 1) % 3, a2 = (ax + 2) % 3;
            int n1 = Math.Clamp((int)Math.Ceiling((hi[a1] - lo[a1]) / step), 1, 200), n2 = Math.Clamp((int)Math.Ceiling((hi[a2] - lo[a2]) / step), 1, 200);
            foreach (var f in new[] { lo[ax], hi[ax] })
            {
                var c = DesignMesh.Tone(col, f == lo[ax] ? 0.8 : 1.0);
                (double, double, double) Pt(int i, int j)
                {
                    var v = new double[3];
                    v[ax] = f;
                    v[a1] = lo[a1] + (hi[a1] - lo[a1]) * i / n1;
                    v[a2] = lo[a2] + (hi[a2] - lo[a2]) * j / n2;
                    return (v[0], v[1], v[2]);
                }
                for (var i = 0; i < n1; i++)
                    for (var j = 0; j < n2; j++)
                        m.Quad(Pt(i, j), Pt(i + 1, j), Pt(i + 1, j + 1), Pt(i, j + 1), c, false);
            }
        }
    }
}
