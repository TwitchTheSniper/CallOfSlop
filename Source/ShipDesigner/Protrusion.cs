using System.Text.Json.Nodes;

namespace CallOfSlop.ShipDesigner;

/// <summary>
/// A raised plate on a hull's skin (the Sculpt tab): an angular armour plate, a blister, a sponson, laid on the skin so it follows the hull's
/// curve, with a chamfered top and a skirt sunk into the hull. Anchored as a skin piece is (<see cref="Z"/> along the hull, <see cref="T"/>
/// round the half profile, <see cref="Side"/>); its numbers are the truth, <see cref="Kind"/> is only the gallery name it started from.
/// </summary>
public sealed class Protrusion
{
    public const double MinLen = 0.3, MaxLen = 40, MinWid = 0.2, MaxWid = 20, MinRise = 0.05, MaxRise = 3;
    public const int Max = 256;
    public int Id, Hull, Copy;
    public string Kind = "Blister";
    public double Z, T = 0.5;
    public int Side = 1;
    public bool Mirror = true; // also on the other side of its hull copy
    public double Len = 4, Wid = 1.5, Rise = 0.3; // along z, along the skin (metres of arc), out
    public double Chamfer = 0.5; // 0..0.95: how far the bevel runs in from the edge, as a share of 2.5 x Rise
    public double Taper = 0; // -0.9..0.9: the plate narrows toward the bow (+) or the stern (-)
    public int Facets = 1; // 1 a flat top, 2..4 fold lines across it

    public static readonly (string Name, double Len, double Wid, double Rise, double Chamfer, double Taper, int Facets)[] Gallery =
    [
        ("Blister", 4.0, 1.6, 0.35, 0.6, 0.0, 1),
        ("Wedge", 3.0, 1.2, 0.5, 0.3, 0.8, 1),
        ("Sponson", 6.0, 2.4, 0.9, 0.4, 0.2, 2),
        ("Strake", 10.0, 0.3, 0.2, 0.8, 0.0, 1),
        ("Box", 2.0, 1.5, 0.6, 0.1, 0.0, 1),
        ("Fairing", 5.0, 2.0, 0.5, 0.9, 0.5, 3),
    ];

    public void Preset(string kind)
    {
        var g = Array.FindIndex(Gallery, k => k.Name == kind);
        if (g < 0)
            throw new InvalidOperationException("no such protrusion preset");
        (Kind, Len, Wid, Rise, Chamfer, Taper, Facets) = Gallery[g];
    }

    public void Clamp()
    {
        Len = Math.Clamp(Len, MinLen, MaxLen);
        Wid = Math.Clamp(Wid, MinWid, MaxWid);
        Rise = Math.Clamp(Rise, MinRise, MaxRise);
        Chamfer = Math.Clamp(Chamfer, 0, 0.95);
        Taper = Math.Clamp(Taper, -0.9, 0.9);
        Facets = Math.Clamp(Facets, 1, 4);
        T = Math.Clamp(T, 0, 1);
        var k = new string([.. Kind.Where(ch => ch >= 32 && ch < 127)]);
        Kind = k.Length == 0 ? "Plate" : k[..Math.Min(24, k.Length)];
    }

    public JsonObject ToJson() => new()
    {
        ["kind"] = Kind, ["copy"] = Copy, ["z"] = ShipDesign.R(Z), ["t"] = Math.Round(T, 4), ["side"] = Side, ["mirror"] = Mirror,
        ["len"] = ShipDesign.R(Len), ["wid"] = ShipDesign.R(Wid), ["rise"] = ShipDesign.R(Rise), ["chamfer"] = ShipDesign.R(Chamfer), ["taper"] = ShipDesign.R(Taper), ["facets"] = Facets,
    };

    /// <summary>Reads one (its hull is set by the caller).</summary>
    public static Protrusion FromJson(JsonObject o, int id)
    {
        var p = new Protrusion
        {
            Id = id, Kind = (string?)o["kind"] ?? "Plate", Copy = (int?)o["copy"] is 1 ? 1 : 0, Z = ShipDesign.F(o["z"], 0), T = ShipDesign.F(o["t"], 0.5),
            Side = (int?)o["side"] is -1 ? -1 : 1, Mirror = (bool?)o["mirror"] ?? true, Len = ShipDesign.F(o["len"], 4), Wid = ShipDesign.F(o["wid"], 1.5),
            Rise = ShipDesign.F(o["rise"], 0.3), Chamfer = ShipDesign.F(o["chamfer"], 0.5), Taper = ShipDesign.F(o["taper"], 0), Facets = (int)Math.Round(ShipDesign.F(o["facets"], 1)),
        };
        p.Clamp();
        return p;
    }
}

/// <summary>A protrusion's triangles, in world space: a grid laid on the skin, lifted along grid-neighbour normals into a chamfered,
/// faceted plateau, with a skirt round its rim sunk into the hull so no gap shows where the skin curves.</summary>
public static class ProtrusionMesher
{
    private const double StandOff = 0.004, Sink = 0.05;

    /// <summary>Builds it into m; false when the patch ran off the hull's ends or the skin had no point.</summary>
    public static bool Build(ShipDesign d, Protrusion pr, DesignMesh m)
    {
        var h = d.Hull(pr.Hull);
        if (h is null || h.Stations.Count < 2)
            return false;
        m.World();
        var copy = Math.Min(pr.Copy, h.Copies - 1);
        var cx = h.CopyX(copy);
        var ok = true;
        double hl = pr.Len / 2, hw = pr.Wid / 2;
        // The bevel's run in from the edge, as a share of each half size; the plateau inside it.
        var inset = pr.Chamfer * pr.Rise * 2.5;
        double au = 1 - Math.Min(0.95, inset / hl), av = 1 - Math.Min(0.95, inset / hw);
        var us = Breaks([-1, -au, au, 1], Math.Max(0.05, 0.25 / hl));
        var vb = new List<double> { -1, -av, av, 1 };
        for (var f = 1; f < pr.Facets; f++)
            vb.Add(-av + 2 * av * f / pr.Facets);
        if (pr.Facets > 1 && pr.Facets % 2 == 0) vb.Add(0);
        vb.Sort();
        var vs = Breaks([.. vb.Distinct()], Math.Max(0.05, 0.15 / hw));
        int nu = us.Count, nv = vs.Count;
        var B = new double[nu, nv][];
        var S = new double[nu, nv][]; // OnSkin's normal, for which way is out
        var lift = new double[nu, nv];
        var wrapped = false;
        var add = StandOff + 0.002 * (pr.Id % 5);
        for (var i = 0; i < nu; i++)
        {
            var z = pr.Z + us[i] * hl;
            if (z < h.Front - 1e-6 || z > h.Back + 1e-6) ok = false;
            z = Math.Clamp(z, h.Front, h.Back);
            var path = Math.Max(0.01, h.PathLen(z));
            // The plan's taper: narrower toward the bow (u -1) for +Taper, toward the stern for -Taper.
            var sc = 1 - Math.Abs(pr.Taper) * (pr.Taper > 0 ? (1 - us[i]) / 2 : (1 + us[i]) / 2);
            for (var j = 0; j < nv; j++)
            {
                var t = pr.T + vs[j] * hw * sc / path;
                var side = pr.Side;
                // Past the keel's or the spine's middle, it carries on over onto the other side.
                if (t < 0) { t = -t; side = -side; wrapped = true; }
                if (t > 1) { t = 2 - t; side = -side; wrapped = true; }
                if (t < 0 || t > 1) { ok = false; t = Math.Clamp(t, 0, 1); }
                var s = h.OnSkin(z, t, side);
                B[i, j] = [cx + s.X, h.Y + s.Y, h.Z + s.Z];
                S[i, j] = [s.Nx, s.Ny, 0];
                lift[i, j] = pr.Rise * Along(us[i], au) * Across(vs[j], av, pr.Facets) + add;
            }
        }
        var N = new double[nu, nv][];
        for (var i = 0; i < nu; i++)
            for (var j = 0; j < nv; j++)
            {
                var a = Sub(B[Math.Min(i + 1, nu - 1), j], B[Math.Max(i - 1, 0), j]);
                var b = Sub(B[i, Math.Min(j + 1, nv - 1)], B[i, Math.Max(j - 1, 0)]);
                var n = Cross(a, b);
                var l = Math.Sqrt(Dot(n, n));
                if (l < 1e-9) n = S[i, j];
                else { n = [n[0] / l, n[1] / l, n[2] / l]; if (Dot(n, S[i, j]) < 0) n = [-n[0], -n[1], -n[2]]; }
                N[i, j] = n;
            }
        double[] Top(int i, int j) => Add(B[i, j], N[i, j], lift[i, j]);
        double[] Foot(int i, int j) => Add(B[i, j], N[i, j], -Sink);
        var skin = DesignMesh.Hex(h.Skin);
        var colTop = DesignMesh.Tone(skin, 0.84);
        var colBevel = DesignMesh.Tone(skin, 0.68);
        var colSkirt = DesignMesh.Tone(skin, 0.58);
        var start = m.Tris;
        var mid = Top(nu / 2, nv / 2);
        for (var i = 0; i < nu - 1; i++)
            for (var j = 0; j < nv - 1; j++)
            {
                var flat = Math.Abs(us[i]) <= au + 1e-9 && Math.Abs(us[i + 1]) <= au + 1e-9 && Math.Abs(vs[j]) <= av + 1e-9 && Math.Abs(vs[j + 1]) <= av + 1e-9;
                var nOut = N[i, j];
                Quad(m, Top(i, j), Top(i + 1, j), Top(i + 1, j + 1), Top(i, j + 1), nOut, flat ? colTop : colBevel);
            }
        // The skirt: the rim's four edges, from the top's edge down into the hull.
        void Skirt(int i0, int j0, int i1, int j1)
        {
            var a = Top(i0, j0); var b = Top(i1, j1);
            var q = Mid(a, b);
            var outw = Sub(q, mid);
            Quad(m, a, b, Foot(i1, j1), Foot(i0, j0), outw, colSkirt);
        }
        for (var i = 0; i < nu - 1; i++) { Skirt(i, 0, i + 1, 0); Skirt(i, nv - 1, i + 1, nv - 1); }
        for (var j = 0; j < nv - 1; j++) { Skirt(0, j, 0, j + 1); Skirt(nu - 1, j, nu - 1, j + 1); }
        // Its twin across its hull copy's centreline (not when it already runs over the keel or the spine onto the other side).
        if (pr.Mirror && !wrapped)
            m.MirrorFrom(start, cx);
        // On a mirrored pair of bodies, the other body has it too.
        if (h.Copies == 2)
            m.MirrorFrom(start, 0);
        return ok;
    }

    /// <summary>The breaks, each span cut into steps no wider than step.</summary>
    private static List<double> Breaks(double[] b, double step)
    {
        var o = new List<double> { b[0] };
        for (var k = 0; k < b.Length - 1; k++)
        {
            var span = b[k + 1] - b[k];
            if (span < 1e-6) continue;
            var n = Math.Clamp((int)Math.Ceiling(span / step), 1, 40);
            for (var s = 1; s <= n; s++) o.Add(b[k] + span * s / n);
        }
        return o;
    }

    private static double Along(double u, double a) => Math.Abs(u) <= a ? 1 : Math.Max(0, (1 - Math.Abs(u)) / Math.Max(1e-6, 1 - a));

    /// <summary>Across: a faceted crown on the plateau (fold lines at the breaks), then the bevel down to the rim.</summary>
    private static double Across(double v, double a, int facets)
    {
        double Crown(double x) => facets <= 1 ? 1 : 1 - 0.3 * (x / Math.Max(a, 1e-6)) * (x / Math.Max(a, 1e-6));
        var av = Math.Abs(v);
        if (av > a) return Crown(a) * Math.Max(0, (1 - av) / Math.Max(1e-6, 1 - a));
        if (facets <= 1) return 1;
        // Piecewise between the fold lines, so each facet is a flat plate.
        var step = 2 * a / facets;
        var k = Math.Floor((v + a) / step);
        var x0 = -a + k * step;
        var x1 = Math.Min(a, x0 + step);
        var f = x1 - x0 < 1e-9 ? 0 : (v - x0) / (x1 - x0);
        double c0 = Crown(x0), c1 = Crown(x1);
        if (facets % 2 == 0 && x0 < 0 && x1 > 0)
            return v < 0 ? c0 + (Crown(0) - c0) * (v - x0) / -x0 : Crown(0) + (c1 - Crown(0)) * v / x1;
        return c0 + (c1 - c0) * f;
    }

    private static void Quad(DesignMesh m, double[] a, double[] b, double[] c, double[] d, double[] outw, (byte, byte, byte) col)
    {
        var n = Cross(Sub(c, a), Sub(d, b));
        if (Dot(n, outw) < 0) (b, d) = (d, b);
        m.Quad((a[0], a[1], a[2]), (b[0], b[1], b[2]), (c[0], c[1], c[2]), (d[0], d[1], d[2]), col, false);
    }

    private static double[] Sub(double[] a, double[] b) => [a[0] - b[0], a[1] - b[1], a[2] - b[2]];
    private static double[] Mid(double[] a, double[] b) => [(a[0] + b[0]) / 2, (a[1] + b[1]) / 2, (a[2] + b[2]) / 2];
    private static double[] Add(double[] a, double[] n, double s) => [a[0] + n[0] * s, a[1] + n[1] * s, a[2] + n[2] * s];
    private static double Dot(double[] a, double[] b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
    private static double[] Cross(double[] a, double[] b) => [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]];
}
