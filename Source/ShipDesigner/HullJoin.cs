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
    public double Flare = 1.6, Buttress = 0.8; // the cladding at each end: how much wider it is where it meets the skin, and how far up the strut it runs (m)
    public double OpenA = 0.9, OpenB = 0.9;
    public bool SquareHole; // the hole each end cuts: a simple oval, or a rectangle (Karl, 2026-11-24: simple holes, not the section's outline) // each end's socket: the hole it cuts, a share of the connection's size (0 closed)

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
        ["segments"] = Segments, ["mirror"] = Mirror, ["flare"] = ShipDesign.R(Flare), ["buttress"] = ShipDesign.R(Buttress), ["squareHole"] = SquareHole, ["openA"] = ShipDesign.R(OpenA), ["openB"] = ShipDesign.R(OpenB),
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
            Segments = (int?)o["segments"] ?? 6, Mirror = (bool?)o["mirror"] ?? true, Flare = ShipDesign.F(o["flare"], 1.6), Buttress = ShipDesign.F(o["buttress"], 0.8), SquareHole = (bool?)o["squareHole"] ?? false, OpenA = ShipDesign.F(o["openA"], 0.9), OpenB = ShipDesign.F(o["openB"], 0.9) };
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
        OpenA = Math.Clamp(OpenA, 0, 1.5);
        Flare = Math.Clamp(Flare, 1, 4);
        Buttress = Math.Clamp(Buttress, 0, 10);
        OpenB = Math.Clamp(OpenB, 0, 1.5);
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
    public DesignMesh Mesh(ShipDesign d) => Build(d, null);

    /// <summary>The strut's middle line as rings' frames (middle, across, up), so rooms can run through it (Karl, 2026-11-24).</summary>
    public List<(double[] C, double[] S, double[] U)> Spine(ShipDesign d)
    {
        var l = new List<(double[] C, double[] S, double[] U)>();
        Build(d, null, l);
        if (Mirror && l.Count > 0 && World(d, A) is { } a && Math.Abs(a.P[0]) > 0.3)
            foreach (var (c, sd, u) in l.ToArray())
                l.Add(([-c[0], c[1], c[2]], [-sd[0], sd[1], sd[2]], [-u[0], u[1], u[2]]));
        return l;
    }

    /// <summary>Whether a world point is inside a strut's walls (margin m in from them), by its nearest spine frame.</summary>
    public bool Inside(List<(double[] C, double[] S, double[] U)> spine, double x, double y, double z, double margin)
    {
        var best = double.MaxValue;
        var bi = -1;
        for (var i = 0; i < spine.Count; i++)
        {
            var c = spine[i].C;
            var dd = Sq(x - c[0]) + Sq(y - c[1]) + Sq(z - c[2]);
            if (dd < best) (best, bi) = (dd, i);
        }
        if (bi < 0)
            return false;
        var (cc, sd, up) = spine[bi];
        double qx = x - cc[0], qy = y - cc[1], qz = z - cc[2];
        var hw = W / 2 - margin;
        var hh = H / 2 - margin;
        if (hw <= 0 || hh <= 0)
            return false;
        var u = (qx * sd[0] + qy * sd[1] + qz * sd[2]) / hw;
        var v = (qx * up[0] + qy * up[1] + qz * up[2]) / hh;
        return u * u + v * v <= 1;
    }

    /// <summary>A connection's mouth on a body: where its end ring sits (middle, along, across, up) and the section's size.</summary>
    public readonly record struct Mouth(int Hull, double[] C, double[] T, double[] S, double[] U, double W, double H, bool Square)
    {
        /// <summary>Whether a world point is in the opening (the body's skin is left out there, so the connection opens into it).</summary>
        public bool Holds(double x, double y, double z)
        {
            double qx = x - C[0], qy = y - C[1], qz = z - C[2];
            var reach = Math.Max(W, H) + 0.5;
            var a = qx * T[0] + qy * T[1] + qz * T[2];
            if (Math.Abs(a) > reach)
                return false;
            if (W < 0.01 || H < 0.01)
                return false;
            var u = (qx * S[0] + qy * S[1] + qz * S[2]) / W;
            var v = (qx * U[0] + qy * U[1] + qz * U[2]) / H;
            return Square ? Math.Abs(u) <= 0.5 && Math.Abs(v) <= 0.5 : u * u + v * v <= 0.25;
        }
    }

    /// <summary>The mouths at both ends (and the twin's when mirrored).</summary>
    public List<Mouth> Mouths(ShipDesign d)
    {
        var l = new List<Mouth>();
        Build(d, l);
        return l;
    }

    private DesignMesh Build(ShipDesign d, List<Mouth>? mouths, List<(double[] C, double[] S, double[] U)>? spine = null)
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
        var ha = d.Hull(A.Hull)!;
        var hb = d.Hull(B.Hull)!;
        (double[] C, double[] T) Curve(double s)
        {
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
            return (c, t);
        }
        // Where the middle line leaves the first skin and enters the second: the connection is drawn between, its end rings laid on the skins
        // (Karl, 2026-11-24: it meets the hull, not a hoop inside it).
        static double Cross0(Func<double, bool> inside, double lo, double hi)
        {
            if (!inside(lo) || inside(hi))
                return lo;
            for (var i = 0; i < 24; i++)
            {
                var mid = (lo + hi) / 2;
                if (inside(mid)) lo = mid; else hi = mid;
            }
            return lo;
        }
        bool In(HullBody h, double[] q) => h.Depth(q[0], q[1], q[2]) > 0;
        var s0 = Cross0(s => In(ha, Curve(s).C), 0, 0.5);
        var s1 = 1 - Cross0(s => In(hb, Curve(1 - s).C), 0, 0.5);
        if (s1 <= s0 + 1e-3)
            return m;
        var rings = new List<(double, double, double)[]>();
        double[]? side = null;
        // Where the rings go: the ends on the skins (flared into the buttresses' cladding), the strut's own rings between.
        var dl = Math.Min(Buttress / len, 0.3 * (s1 - s0));
        var at = new List<(double S, double Scale)>();
        if (dl > 1e-4)
            at.Add((s0, Flare));
        for (var q = 0; q <= Segments; q++)
            at.Add((s0 + dl + (s1 - s0 - 2 * dl) * q / Segments, dl > 1e-4 ? 1 : Flare));
        if (dl > 1e-4)
            at.Add((s1, Flare));
        var last = at.Count - 1;
        for (var i = 0; i <= last; i++)
        {
            var (c, t) = Curve(at[i].S);
            var sc = at[i].Scale;
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
            // The strut's own rings (not the flared ends), sampled finely enough for the room check.
            if (spine is not null && sc == 1 || spine is not null && i > 0 && i < last)
                spine.Add((c, [.. side], upv));
            if (mouths is not null && (i == 0 || i == last))
            {
                var hid = i == 0 ? A.Hull : B.Hull;
                var f = i == 0 ? OpenA : OpenB;
                mouths.Add(new Mouth(hid, c, t, [.. side], upv, W * f, H * f, SquareHole));
                if (Mirror && Math.Abs(a.P[0]) > 0.3)
                    mouths.Add(new Mouth(hid, [-c[0], c[1], c[2]], [-t[0], t[1], t[2]], [-side[0], side[1], side[2]], [-upv[0], upv[1], upv[2]], W * f, H * f, SquareHole));
            }
            var r = new (double, double, double)[ring.Count];
            for (var j = 0; j < ring.Count; j++)
            {
                var (u, v) = ring[j];
                double uw = u * W * sc, vh = v * H * sc;
                r[j] = (c[0] + side[0] * uw + upv[0] * vh, c[1] + side[1] * uw + upv[1] * vh, c[2] + side[2] * uw + upv[2] * vh);
            }
            // An end ring slid along the line onto its skin, each point where it meets it.
            if (i == 0 || i == last)
            {
                var h = i == 0 ? ha : hb;
                var dir = i == 0 ? -1.0 : 1.0;
                var reach = 2 * Math.Max(W, H) * sc + 1;
                for (var j = 0; j < r.Length; j++)
                {
                    var v = r[j];
                    double[] At(double f) => [v.Item1 + t[0] * dir * f, v.Item2 + t[1] * dir * f, v.Item3 + t[2] * dir * f];
                    // f from 0 (this point) to reach (deeper in): the first f inside the skin.
                    // Inside already: back out to where it leaves the skin; else on in to where it meets it.
                    var inside = In(h, At(0));
                    if (inside ? In(h, At(-reach)) : !In(h, At(reach)))
                        continue;
                    double lo = inside ? -reach : 0, hi = inside ? 0 : reach;
                    for (var it = 0; it < 20; it++)
                    {
                        var mid = (lo + hi) / 2;
                        if (In(h, At(mid))) hi = mid; else lo = mid;
                    }
                    var e = At(hi + 0.01);
                    r[j] = (e[0], e[1], e[2]);
                }
            }
            rings.Add(r);
        }
        if (spine is not null)
            return m;
        for (var i = 0; i < last; i++)
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
