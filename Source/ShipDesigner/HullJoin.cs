using System.Text.Json.Nodes;

namespace CallOfSlop.ShipDesigner;

/// <summary>
/// A connection lofted between two points on hull skins (Karl, 2026-11-24): a pylon, a strut or a neck out to a nacelle or a pod. Each end
/// is a skin point (<see cref="End"/>); the body between is a section shape (one of the hull gallery's), sized and cut into segments, that
/// leaves each skin along its normal and bends to meet the other. Mirror draws a twin across the centreline.
/// </summary>
public sealed class HullJoin
{
    public const int MaxSegments = 24;
    public const double Sink = 0.3; // how far each end goes in under its skin, so no gap shows where it meets

    /// <summary>A point on a body's skin: the hull, its copy, z along it (hull-local), t round the half profile and the side.</summary>
    public struct End
    {
        public int Hull, Copy, Side;
        public double Z, T;
    }

    public int Id;
    public End A, B;
    public string Section = "Round";
    public double W = 1.2, H = 1.2;
    public int Segments = 6;
    public bool Mirror = true;

    /// <summary>The end nearest a world point on a body's copy.</summary>
    public static End At(HullBody h, int copy, double x, double y, double z)
    {
        copy = Math.Clamp(copy, 0, h.Copies - 1);
        double lx = x - h.CopyX(copy), ly = y - h.Y, lz = Math.Clamp(z - h.Z, h.Front, h.Back);
        return new End { Hull = h.Id, Copy = copy, Z = lz, T = h.NearestT(lx, ly, lz), Side = lx < -0.01 ? -1 : 1 };
    }

    /// <summary>An end's world point and outward normal (null when its body is gone).</summary>
    public static (double[] P, double[] N)? World(ShipDesign d, End e)
    {
        if (d.Hull(e.Hull) is not { } h)
            return null;
        var s = h.OnSkin(e.Z, e.T, e.Side);
        var c = Math.Min(e.Copy, h.Copies - 1);
        return ([h.CopyX(c) + s.X, h.Y + s.Y, h.Z + s.Z], [s.Nx, s.Ny, 0]);
    }

    public JsonObject ToJson(ShipDesign d) => new()
    {
        ["a"] = EndJson(d, A), ["b"] = EndJson(d, B), ["section"] = Section, ["w"] = ShipDesign.R(W), ["h"] = ShipDesign.R(H),
        ["segments"] = Segments, ["mirror"] = Mirror,
    };

    private static JsonObject EndJson(ShipDesign d, End e) => new()
    {
        ["hull"] = d.HullIndex(e.Hull), ["copy"] = e.Copy, ["z"] = ShipDesign.R(e.Z), ["t"] = Math.Round(e.T, 4), ["side"] = e.Side,
    };

    /// <summary>Reads a join; null when an end names no body.</summary>
    public static HullJoin? FromJson(ShipDesign d, JsonObject o, int id)
    {
        End? E(JsonNode? n)
        {
            if (n is not JsonObject e)
                return null;
            var hi = (int?)e["hull"] ?? -1;
            if (hi < 0 || hi >= d.Hulls.Count)
                return null;
            return new End
            {
                Hull = d.Hulls[hi].Id, Copy = (int?)e["copy"] is 1 ? 1 : 0, Z = ShipDesign.F(e["z"], 0), T = Math.Clamp(ShipDesign.F(e["t"], 0.5), 0, 1),
                Side = (int?)e["side"] is -1 ? -1 : 1,
            };
        }
        if (E(o["a"]) is not { } a || E(o["b"]) is not { } b)
            return null;
        var j = new HullJoin { Id = id, A = a, B = b, Section = (string?)o["section"] ?? "Round", W = ShipDesign.F(o["w"], 1.2), H = ShipDesign.F(o["h"], 1.2),
            Segments = (int?)o["segments"] ?? 6, Mirror = (bool?)o["mirror"] ?? true };
        j.Tidy();
        return j;
    }

    public void Tidy()
    {
        if (Array.FindIndex(DesignTemplates.Sections, s => s.Name == Section) < 0)
            Section = "Round";
        W = Math.Clamp(W, 0.2, 20);
        H = Math.Clamp(H, 0.2, 20);
        Segments = Math.Clamp(Segments, 1, MaxSegments);
    }

    /// <summary>The section as a closed ring of (across, up) points fitting a unit square (-0.5..0.5), the +x side first.</summary>
    public static List<(double U, double V)> Ring(string section)
    {
        var p = Array.Find(DesignTemplates.Sections, s => s.Name == section).P ?? DesignTemplates.Sections[0].P;
        var w = p.Max(q => q.H);
        double y0 = p.Min(q => q.Y), y1 = p.Max(q => q.Y);
        var ring = new List<(double, double)>();
        foreach (var (h, y) in p)
            ring.Add((h / w / 2, (y - (y0 + y1) / 2) / Math.Max(1e-6, y1 - y0)));
        for (var k = p.Count - 1; k >= 0; k--)
            ring.Add((-p[k].H / w / 2, (p[k].Y - (y0 + y1) / 2) / Math.Max(1e-6, y1 - y0)));
        return ring;
    }

    /// <summary>The join's mesh (and its twin when mirrored off the centreline); empty when an end's body is gone.</summary>
    public DesignMesh Mesh(ShipDesign d)
    {
        var m = new DesignMesh();
        if (World(d, A) is not { } a || World(d, B) is not { } b)
            return m;
        double[] p0 = [a.P[0] - a.N[0] * Sink, a.P[1] - a.N[1] * Sink, a.P[2]];
        double[] p1 = [b.P[0] - b.N[0] * Sink, b.P[1] - b.N[1] * Sink, b.P[2]];
        var len = Math.Sqrt(Sq(p1[0] - p0[0]) + Sq(p1[1] - p0[1]) + Sq(p1[2] - p0[2]));
        if (len < 0.05)
            return m;
        // A Hermite curve leaving each skin along its normal: a pylon stands off the hull and meets the pod square on.
        var k = len * 0.9;
        double[] m0 = [a.N[0] * k, a.N[1] * k, a.N[2] * k], m1 = [-b.N[0] * k, -b.N[1] * k, -b.N[2] * k];
        var ring = Ring(Section);
        var col = DesignMesh.Hex(d.Hull(A.Hull)?.Skin ?? "#c9cdd2");
        var rings = new List<(double, double, double)[]>();
        double[]? side = null;
        for (var i = 0; i <= Segments; i++)
        {
            var s = (double)i / Segments;
            double h00 = 2 * s * s * s - 3 * s * s + 1, h10 = s * s * s - 2 * s * s + s, h01 = -2 * s * s * s + 3 * s * s, h11 = s * s * s - s * s;
            double d00 = 6 * s * s - 6 * s, d10 = 3 * s * s - 4 * s + 1, d01 = -6 * s * s + 6 * s, d11 = 3 * s * s - 2 * s;
            var c = new double[3];
            var t = new double[3];
            for (var q = 0; q < 3; q++)
            {
                c[q] = h00 * p0[q] + h10 * m0[q] + h01 * p1[q] + h11 * m1[q];
                t[q] = d00 * p0[q] + d10 * m0[q] + d01 * p1[q] + d11 * m1[q];
            }
            Norm(t);
            // The ring's across axis: carried along from the last ring (so the strut doesn't twist), first from the world's up.
            if (side is null)
            {
                double[] up = Math.Abs(t[1]) > 0.9 ? [0, 0, 1] : [0, 1, 0];
                side = Cross(up, t);
            }
            else
            {
                var dot = side[0] * t[0] + side[1] * t[1] + side[2] * t[2];
                side = [side[0] - t[0] * dot, side[1] - t[1] * dot, side[2] - t[2] * dot];
            }
            Norm(side);
            var upv = Cross(t, side);
            var r = new (double, double, double)[ring.Count];
            for (var j = 0; j < ring.Count; j++)
            {
                var (u, v) = ring[j];
                r[j] = (c[0] + side[0] * u * W + upv[0] * v * H, c[1] + side[1] * u * W + upv[1] * v * H, c[2] + side[2] * u * W + upv[2] * v * H);
            }
            rings.Add(r);
        }
        for (var i = 0; i < Segments; i++)
            for (var j = 0; j < ring.Count; j++)
            {
                var j1 = (j + 1) % ring.Count;
                var shade = DesignMesh.Tone(col, (i % 2 == 0 ? 1.0 : 0.94) * (j % 2 == 0 ? 1.0 : 0.9));
                m.Quad(rings[i][j], rings[i + 1][j], rings[i + 1][j1], rings[i][j1], shade, false);
            }
        if (Mirror && Math.Abs(a.P[0]) > 0.3)
            m.MirrorFrom(0, 0);
        return m;
    }

    private static double Sq(double v) => v * v;
    private static double[] Cross(double[] a, double[] b) => [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]];
    private static void Norm(double[] v)
    {
        var l = Math.Sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2]);
        if (l < 1e-9) { v[0] = 1; v[1] = 0; v[2] = 0; return; }
        v[0] /= l; v[1] /= l; v[2] /= l;
    }
}
