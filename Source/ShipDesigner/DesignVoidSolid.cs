namespace CallOfSlop.ShipDesigner;

/// <summary>
/// Option E of the void preview (DesignVoid.cs): the space between skin and rooms read as a solid fill. Only its surface is drawn: an
/// inward-facing layer just inside the skin and a layer on the rooms' outer faces, finished by COS_VOID_SKIN: S structural (plate with
/// stiffeners and frames), I insulation (quilted blankets), C cables and wires (looms with clamps), M structural mesh (a welded grid).
/// </summary>
public static partial class DesignScene
{
    public static readonly string VoidSkin = (Environment.GetEnvironmentVariable("COS_VOID_SKIN") ?? "S").Trim().ToUpperInvariant();

    private readonly record struct Patch(V3 P00, V3 P10, V3 P11, V3 P01, V3 N)
    {
        /// <summary>A point at (u, v) across the patch, h out along its normal.</summary>
        public V3 At(double u, double v, double h = 0) =>
            (P00 * ((1 - u) * (1 - v)) + P10 * (u * (1 - v)) + P11 * (u * v) + P01 * ((1 - u) * v)) + N * h;
        public double LenU => ((P10 - P00).Len + (P11 - P01).Len) / 2;
        public double LenV => ((P01 - P00).Len + (P11 - P10).Len) / 2;
    }

    private static void VoidSolid(VoidCtx c)
    {
        var patches = new List<Patch>();
        // The skin's inside: the profile 1.5 cm in, panels between stations every 1.5 m, each facet in ~1.2 m pieces (u round, v along).
        const double inset = 0.015;
        var zs = VoidStations(c.H, 1.5).Select(z => z - 0.75).Append(c.H.Back).Where(z => z >= c.H.Front).ToList();
        if (zs[0] > c.H.Front + 0.01)
            zs.Insert(0, c.H.Front);
        for (var i = 0; i + 1 < zs.Count; i++)
        {
            var ra = VoidRing(c, zs[i] + 0.005, 100);
            var rb = VoidRing(c, zs[i + 1] - 0.005, 100);
            for (var si = 0; si < 2; si++)
            {
                var (a, b) = (ra[si], rb[si]);
                if (a.Count != b.Count)
                    continue;
                for (var k = 0; k + 1 < a.Count; k++)
                {
                    var a0 = a[k].P + a[k].N * inset;
                    var a1 = a[k + 1].P + a[k + 1].N * inset;
                    var b0 = b[k].P + b[k].N * inset;
                    var b1 = b[k + 1].P + b[k + 1].N * inset;
                    var cnt = Math.Max(1, (int)Math.Round(Math.Max((a1 - a0).Len, (b1 - b0).Len) / 1.2));
                    var n = (a[k].N + a[k + 1].N).Unit;
                    for (var j = 0; j < cnt; j++)
                    {
                        double f0 = (double)j / cnt, f1 = (double)(j + 1) / cnt;
                        var p = new Patch(a0 + (a1 - a0) * f0, a0 + (a1 - a0) * f1, b0 + (b1 - b0) * f1, b0 + (b1 - b0) * f0, n);
                        if (c.Clear(p.At(0.5, 0.5, 0.05), 0.02) && c.Clear(p.At(0.02, 0.02, 0.04), 0) && c.Clear(p.At(0.98, 0.02, 0.04), 0) && c.Clear(p.At(0.98, 0.98, 0.04), 0) && c.Clear(p.At(0.02, 0.98, 0.04), 0))
                            patches.Add(p);
                    }
                }
            }
        }
        // The rooms' outer faces that look into the void, in ~1.2 m cells (v along the ship on the side walls).
        foreach (var r in c.D.Rooms)
        {
            var bx = c.D.Box(r);
            for (var ax = 0; ax < 3; ax++)
                for (var dir = -1; dir <= 1; dir += 2)
                {
                    int ua = (ax + 1) % 3, va = (ax + 2) % 3;
                    var face = (dir < 0 ? bx[ax * 2] : bx[ax * 2 + 1]) + dir * 0.006;
                    double u0 = bx[ua * 2], u1 = bx[ua * 2 + 1], v0 = bx[va * 2], v1 = bx[va * 2 + 1];
                    V3 At(double a, double u, double v)
                    {
                        var q = new double[3];
                        q[ax] = a; q[ua] = u; q[va] = v;
                        return new V3(q[0], q[1], q[2]);
                    }
                    var n = At(dir, 0, 0);
                    int nu = Math.Max(1, (int)Math.Round((u1 - u0) / 1.2)), nv = Math.Max(1, (int)Math.Round((v1 - v0) / 1.2));
                    for (var iu = 0; iu < nu; iu++)
                        for (var iv = 0; iv < nv; iv++)
                        {
                            double pu0 = u0 + (u1 - u0) * iu / nu, pu1 = u0 + (u1 - u0) * (iu + 1) / nu;
                            double pv0 = v0 + (v1 - v0) * iv / nv, pv1 = v0 + (v1 - v0) * (iv + 1) / nv;
                            var probe = At(face, (pu0 + pu1) / 2, (pv0 + pv1) / 2) + n * 0.12;
                            if (!c.InHull(probe, 0.03) || c.InRoom(probe, 0.0))
                                continue;
                            patches.Add(new Patch(At(face, pu0, pv0), At(face, pu1, pv0), At(face, pu1, pv1), At(face, pu0, pv1), n));
                        }
                }
        }
        var rng = new Random(7);
        foreach (var p in patches)
            switch (VoidSkin)
            {
                case "I": Insulation(c.M, p, rng); break;
                case "C": Cables(c.M, p, rng); break;
                case "M": Mesh(c.M, p); break;
                default: Structural(c.M, p); break;
            }
    }

    /// <summary>A bar lying on a patch from (u0, v0) to (u1, v1): its top and two sides, w wide, h proud.</summary>
    private static void Bar(DesignMesh m, Patch p, double u0, double v0, double u1, double v1, double w, double h, (byte, byte, byte) col, double lift = 0)
    {
        var a = p.At(u0, v0, lift);
        var b = p.At(u1, v1, lift);
        var e = (b - a).Unit;
        var side = p.N.Cross(e).Unit * (w / 2);
        var up = p.N * h;
        var (a0, a1, b0, b1) = (a - side, a + side, b - side, b + side);
        Face(m, a0 + up, a1 + up, b1 + up, b0 + up, p.N, col, 1.0);
        Face(m, a0, a0 + up, b0 + up, b0, side * -1, col, 0.8);
        Face(m, a1, b1, b1 + up, a1 + up, side, col, 0.85);
    }

    /// <summary>A quad wound to face n.</summary>
    private static void Face(DesignMesh m, V3 a, V3 b, V3 cc, V3 d, V3 n, (byte, byte, byte) col, double shade)
    {
        var c = Tone(col, shade);
        if ((b - a).Cross(cc - a).Dot(n) < 0)
            m.Quad(a.T, d.T, cc.T, b.T, c);
        else
            m.Quad(a.T, b.T, cc.T, d.T, c);
    }

    private static void Base(DesignMesh m, Patch p, (byte, byte, byte) col) => Face(m, p.P00, p.P10, p.P11, p.P01, p.N, col, 1.0);

    // S: plate, flat-bar stiffeners every ~0.4 m along the ship, a heavier frame bar at each patch's fore edge, weld seams at its edges.
    private static void Structural(DesignMesh m, Patch p)
    {
        var plate = DesignMesh.Hex("#5b6065");
        var bar = DesignMesh.Hex("#4c5156");
        var frame = DesignMesh.Hex("#454a4f");
        Base(m, p, plate);
        var n = Math.Max(1, (int)Math.Round(p.LenU / 0.4));
        for (var k = 1; k < n; k++)
            Bar(m, p, (double)k / n, 0.01, (double)k / n, 0.99, 0.012, 0.07, bar);
        var dv = 0.05 / Math.Max(0.1, p.LenV);
        Bar(m, p, 0, dv, 1, dv, 0.09, 0.12, frame);
        Bar(m, p, 0, 0.002, 0, 0.998, 0.008, 0.006, frame);
    }

    // I: quilted blankets: 2 x 2 pillows a patch, raised 4-6 cm with bevelled edges, off-white with some foil-faced, taped seams.
    private static void Insulation(DesignMesh m, Patch p, Random rng)
    {
        var cloth = DesignMesh.Hex("#b5ae9a");
        var foil = DesignMesh.Hex("#9ea3a6");
        var tape = DesignMesh.Hex("#7d7a70");
        Base(m, p, tape);
        var foiled = rng.NextDouble() < 0.3;
        int nu = Math.Max(1, (int)Math.Round(p.LenU / 0.6)), nv = Math.Max(1, (int)Math.Round(p.LenV / 0.75));
        double gu = 0.025 / Math.Max(0.1, p.LenU), gv = 0.025 / Math.Max(0.1, p.LenV);
        double bu = 0.06 / Math.Max(0.1, p.LenU), bv = 0.06 / Math.Max(0.1, p.LenV);
        for (var iu = 0; iu < nu; iu++)
            for (var iv = 0; iv < nv; iv++)
            {
                double u0 = (double)iu / nu + gu, u1 = (double)(iu + 1) / nu - gu, v0 = (double)iv / nv + gv, v1 = (double)(iv + 1) / nv - gv;
                var h = 0.04 + 0.02 * rng.NextDouble();
                var col = foiled ? foil : DesignMesh.Tone(cloth, 0.93 + 0.1 * rng.NextDouble());
                V3 o00 = p.At(u0, v0, 0.003), o10 = p.At(u1, v0, 0.003), o11 = p.At(u1, v1, 0.003), o01 = p.At(u0, v1, 0.003);
                V3 i00 = p.At(u0 + bu, v0 + bv, h), i10 = p.At(u1 - bu, v0 + bv, h), i11 = p.At(u1 - bu, v1 - bv, h), i01 = p.At(u0 + bu, v1 - bv, h);
                Face(m, i00, i10, i11, i01, p.N, col, 1.0);
                var mid = p.At((u0 + u1) / 2, (v0 + v1) / 2, 0);
                void Skirt(V3 a, V3 b, V3 cc, V3 d, double s) => Face(m, a, b, cc, d, ((a + b + cc + d) * 0.25 - mid).Unit + p.N * 0.3, col, s);
                Skirt(o00, o10, i10, i00, 0.85);
                Skirt(o10, o11, i11, i10, 0.9);
                Skirt(o11, o01, i01, i11, 0.8);
                Skirt(o01, o00, i00, i01, 0.88);
            }
    }

    // C: dark backing, two looms along the ship of three cables each (black, grey and one coloured), a clamp across each at mid-patch.
    private static void Cables(DesignMesh m, Patch p, Random rng)
    {
        var backing = DesignMesh.Hex("#3b3e41");
        var clamp = DesignMesh.Hex("#8a8e90");
        (byte, byte, byte)[] colours = [DesignMesh.Hex("#202224"), DesignMesh.Hex("#55595d"), DesignMesh.Hex("#b4561f"), DesignMesh.Hex("#2f5f86"), DesignMesh.Hex("#8c2f2a"), DesignMesh.Hex("#b8902a")];
        Base(m, p, backing);
        var du = 0.035 / Math.Max(0.1, p.LenU);
        foreach (var at in new[] { 0.28, 0.68 })
        {
            var k0 = rng.Next(colours.Length);
            for (var w = 0; w < 3; w++)
            {
                var u = at + (w - 1) * du;
                var col = w == 1 ? colours[(k0 + 2) % colours.Length] : colours[w % 2];
                Bar(m, p, u, 0, u, 1, 0.03, 0.03 + 0.01 * (w % 2), col);
            }
            Bar(m, p, at - 2.4 * du, 0.5, at + 2.4 * du, 0.5, 0.04, 0.05, clamp);
        }
        // A thin wire wandering across now and then.
        if (rng.NextDouble() < 0.35)
            Bar(m, p, 0.05, rng.NextDouble(), 0.95, rng.NextDouble(), 0.012, 0.015, colours[2 + rng.Next(4)]);
    }

    // M: a dark backing and a welded square mesh 30 cm, flat bars 1.5 cm wide standing 3 cm off it.
    private static void Mesh(DesignMesh m, Patch p)
    {
        var backing = DesignMesh.Hex("#3f4347");
        var grid = DesignMesh.Hex("#6b7075");
        Base(m, p, backing);
        int nu = Math.Max(1, (int)Math.Round(p.LenU / 0.3)), nv = Math.Max(1, (int)Math.Round(p.LenV / 0.3));
        for (var k = 0; k <= nu; k++)
            Bar(m, p, (double)k / nu, 0, (double)k / nu, 1, 0.015, 0.025, grid, 0.02);
        for (var k = 0; k <= nv; k++)
            Bar(m, p, 0, (double)k / nv, 1, (double)k / nv, 0.015, 0.025, DesignMesh.Tone(grid, 0.9), 0.045);
    }
}
