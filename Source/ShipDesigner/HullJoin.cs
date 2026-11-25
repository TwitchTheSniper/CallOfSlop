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
    public double WA = 1.2, HA = 1.2, WB = 1.2, HB = 1.2; // each end's size: the strut tapers between (the user, 2026-11-24: wing-like arms)
    public double Twist; // roll from end A to end B (degrees)
    public double SweepUp, SweepFore; // the middle line's bow at mid-span (m): up/down and fore/aft
    public double Bias; // -0.9..0.9: the section's mass shifted up (+) or down (-), one side thicker
    public const double MaxSweep = 20, MaxTwist = 180, MaxBias = 0.9;

    /// <summary>The sections a connection takes beyond the hull gallery's (the user, 2026-11-24).</summary>
    public static readonly string[] ExtraSections = ["TriangleUp", "TriangleDown", "Wedge", "Diamond", "Hexagon"];

    /// <summary>Both ends' size at once (the larger end's when read).</summary>
    public double W { get => Math.Max(WA, WB); set => WA = WB = value; }
    public double H { get => Math.Max(HA, HB); set => HA = HB = value; }
    public int Segments = 6;
    public bool Mirror; // off by default (Karl, 2026-11-25)
    public double Flare = 1.6, Buttress = 0.8; // the cladding at each end: how much wider it is where it meets the skin, and how far up the strut it runs (m)
    public bool Follow; // the buttresses' bases laid onto the skin, following its curve, the rings between blending out to them (the user, 2026-11-24)
    public double OpenA = 0.9, OpenB = 0.9;
    public string Clad = "Off"; // plated cladding on the strut (as a body's: HullBody.CladKinds), its seed, panel size and depth
    public int CladSeed = 1;
    public double CladSize = 1.5, CladDepth = 1;
    public string ButtressClad = "Off"; // plated cladding on the flared ends (the buttresses), apart from the strut's: kind, seed, panel size, depth
    public int ButtressSeed = 1;
    public double ButtressSize = 0.8, ButtressDepth = 1;
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
        ["a"] = EndJson(d, A), ["b"] = EndJson(d, B), ["section"] = Section, ["w"] = ShipDesign.R(WA), ["h"] = ShipDesign.R(HA), ["wb"] = ShipDesign.R(WB), ["hb"] = ShipDesign.R(HB),
        ["twist"] = ShipDesign.R(Twist), ["sweepUp"] = ShipDesign.R(SweepUp), ["sweepFore"] = ShipDesign.R(SweepFore), ["bias"] = ShipDesign.R(Bias),
        ["segments"] = Segments, ["mirror"] = Mirror, ["flare"] = ShipDesign.R(Flare), ["buttress"] = ShipDesign.R(Buttress), ["follow"] = Follow, ["squareHole"] = SquareHole, ["openA"] = ShipDesign.R(OpenA), ["openB"] = ShipDesign.R(OpenB),
        ["clad"] = Clad == "Off" ? null : new JsonObject { ["kind"] = Clad, ["seed"] = CladSeed, ["size"] = ShipDesign.R(CladSize), ["depth"] = ShipDesign.R(CladDepth) },
        ["buttressClad"] = ButtressClad == "Off" ? null : new JsonObject { ["kind"] = ButtressClad, ["seed"] = ButtressSeed, ["size"] = ShipDesign.R(ButtressSize), ["depth"] = ShipDesign.R(ButtressDepth) },
    };

    private static JsonObject EndJson(ShipDesign d, End e) => new()
    {
        ["hull"] = d.HullIndex(e.Hull), ["copy"] = e.Copy, ["z"] = ShipDesign.R(e.Z), ["t"] = ShipDesign.R4(e.T), ["side"] = e.Side,
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
        var j = new HullJoin { Id = id, A = a, B = b, Section = (string?)o["section"] ?? "Round", WA = ShipDesign.F(o["w"], 1.2), HA = ShipDesign.F(o["h"], 1.2),
            Segments = (int?)o["segments"] ?? 6, Mirror = (bool?)o["mirror"] ?? true, Flare = ShipDesign.F(o["flare"], 1.6), Buttress = ShipDesign.F(o["buttress"], 0.8), Follow = (bool?)o["follow"] ?? false, SquareHole = (bool?)o["squareHole"] ?? false, OpenA = ShipDesign.F(o["openA"], 0.9), OpenB = ShipDesign.F(o["openB"], 0.9) };
        // Files from before the taper hold one size: both ends take it.
        j.WB = ShipDesign.F(o["wb"], j.WA);
        j.HB = ShipDesign.F(o["hb"], j.HA);
        j.Twist = ShipDesign.F(o["twist"], 0);
        j.SweepUp = ShipDesign.F(o["sweepUp"], 0);
        j.SweepFore = ShipDesign.F(o["sweepFore"], 0);
        j.Bias = ShipDesign.F(o["bias"], 0);
        if (o["clad"] is JsonObject c)
        {
            j.Clad = (string?)c["kind"] ?? "Off";
            j.CladSeed = (int?)c["seed"] ?? 1;
            j.CladSize = ShipDesign.F(c["size"], 1.5);
            j.CladDepth = ShipDesign.F(c["depth"], 1);
        }
        if (o["buttressClad"] is JsonObject bc)
        {
            j.ButtressClad = (string?)bc["kind"] ?? "Off";
            j.ButtressSeed = (int?)bc["seed"] ?? 1;
            j.ButtressSize = ShipDesign.F(bc["size"], 0.8);
            j.ButtressDepth = ShipDesign.F(bc["depth"], 1);
        }
        j.Tidy();
        return j;
    }

    public void Tidy()
    {
        if (Array.FindIndex(DesignTemplates.Sections, s => s.Name == Section) < 0 && !ExtraSections.Contains(Section))
            Section = "Round";
        WA = Math.Clamp(WA, 0.2, 20);
        HA = Math.Clamp(HA, 0.2, 20);
        WB = Math.Clamp(WB, 0.2, 20);
        HB = Math.Clamp(HB, 0.2, 20);
        Twist = Math.Clamp(Twist, -MaxTwist, MaxTwist);
        SweepUp = Math.Clamp(SweepUp, -MaxSweep, MaxSweep);
        SweepFore = Math.Clamp(SweepFore, -MaxSweep, MaxSweep);
        Bias = Math.Clamp(Bias, -MaxBias, MaxBias);
        Segments = Math.Clamp(Segments, 1, MaxSegments);
        OpenA = Math.Clamp(OpenA, 0, 1.5);
        Flare = Math.Clamp(Flare, 1, 4);
        Buttress = Math.Clamp(Buttress, 0, 10);
        OpenB = Math.Clamp(OpenB, 0, 1.5);
        if (!HullBody.CladKinds.Contains(Clad))
            Clad = "Off";
        CladSeed = Math.Clamp(CladSeed, 0, 99999);
        CladSize = Math.Clamp(CladSize, HullBody.CladSizeMin, HullBody.CladSizeMax);
        CladDepth = Math.Clamp(CladDepth, HullBody.CladDepthMin, HullBody.CladDepthMax);
        if (!HullBody.CladKinds.Contains(ButtressClad))
            ButtressClad = "Off";
        ButtressSeed = Math.Clamp(ButtressSeed, 0, 99999);
        ButtressSize = Math.Clamp(ButtressSize, HullBody.CladSizeMin, HullBody.CladSizeMax);
        ButtressDepth = Math.Clamp(ButtressDepth, HullBody.CladDepthMin, HullBody.CladDepthMax);
    }

    /// <summary>The section as a closed ring of (across, up) points fitting a unit square (-0.5..0.5), the +x side first.</summary>
    public static List<(double U, double V)> Ring(string section)
    {
        if (Polygon(section) is { } poly)
            return poly;
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

    /// <summary>The connection's own sections as rings in the unit square (null for a hull gallery section): corners with points along each edge.</summary>
    private static List<(double U, double V)>? Polygon(string section)
    {
        List<(double, double)> Edges((double, double)[] c, int per)
        {
            var l = new List<(double, double)>();
            for (var i = 0; i < c.Length; i++)
            {
                var (a, b) = (c[i], c[(i + 1) % c.Length]);
                for (var k = 0; k < per; k++)
                    l.Add((a.Item1 + (b.Item1 - a.Item1) * k / per, a.Item2 + (b.Item2 - a.Item2) * k / per));
            }
            return l;
        }
        switch (section)
        {
            case "TriangleUp": return Edges([(0.5, -0.5), (0, 0.5), (-0.5, -0.5)], 6);
            case "TriangleDown": return Edges([(0.5, 0.5), (-0.5, 0.5), (0, -0.5)], 6);
            case "Diamond": return Edges([(0.5, 0), (0, 0.5), (-0.5, 0), (0, -0.5)], 4);
            case "Hexagon":
            {
                var c = new (double, double)[6];
                for (var i = 0; i < 6; i++)
                    c[i] = (0.5 * Math.Cos(i * Math.PI / 3), 0.5 * Math.Sin(i * Math.PI / 3) / Math.Sin(Math.PI / 3));
                return Edges(c, 3);
            }
            case "Wedge":
            {
                // An aerofoil (NACA 00xx thickness): a round leading edge at -U, thinning to a sharp trailing edge at +U, scaled to the full height.
                static double T(double x) => 0.2969 * Math.Sqrt(x) - 0.126 * x - 0.3516 * x * x + 0.2843 * x * x * x - 0.1036 * x * x * x * x;
                const int n = 14;
                var tmax = T(0.3);
                var l = new List<(double, double)>();
                for (var k = n; k >= 0; k--)
                {
                    var x = 0.5 - 0.5 * Math.Cos(Math.PI * k / n);
                    l.Add((x - 0.5, 0.5 * T(x) / tmax));
                }
                for (var k = 1; k < n; k++)
                {
                    var x = 0.5 - 0.5 * Math.Cos(Math.PI * k / n);
                    l.Add((x - 0.5, -0.5 * T(x) / tmax));
                }
                return l;
            }
        }
        return null;
    }

    /// <summary>A ring point's height with the thickness bias: the up side grows by 1 + bias, the down side shrinks by 1 - bias.</summary>
    private double Biased(double v) => v * (v > 0 ? 1 + Bias : 1 - Bias);

    /// <summary>The join's mesh (and its twin when mirrored off the centreline); empty when an end's body is gone.</summary>
    public DesignMesh Mesh(ShipDesign d) => Build(d, null);

    /// <summary>The strut's middle line as rings' frames (middle, across, up), so rooms can run through it (Karl, 2026-11-24).</summary>
    public List<(double[] C, double[] S, double[] U, double W, double H)> Spine(ShipDesign d)
    {
        var key = GeomKey(d);
        if (_spine is { } hit && hit.Key == key)
            return hit.L;
        var l = new List<(double[] C, double[] S, double[] U, double W, double H)>();
        Build(d, null, l);
        if (Mirror && l.Count > 0 && World(d, A) is { } a && Math.Abs(a.P[0]) > 0.3)
            foreach (var (c, sd, u, w, hh) in l.ToArray())
                l.Add(([-c[0], c[1], c[2]], [-sd[0], sd[1], sd[2]], [-u[0], u[1], u[2]], w, hh));
        _spine = (key, l);
        return l;
    }

    /// <summary>Whether a world point is inside a strut's walls (margin m in from them), by its nearest spine frame.</summary>
    public bool Inside(List<(double[] C, double[] S, double[] U, double W, double H)> spine, double x, double y, double z, double margin)
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
        var (cc, sd, up, fw, fh) = spine[bi];
        double qx = x - cc[0], qy = y - cc[1], qz = z - cc[2];
        var hw = fw / 2 - margin;
        var a = qx * up[0] + qy * up[1] + qz * up[2];
        // The bias makes the up and down halves different heights.
        var hh = fh / 2 * (a > 0 ? 1 + Bias : 1 - Bias) - margin;
        if (hw <= 0 || hh <= 0)
            return false;
        var u = (qx * sd[0] + qy * sd[1] + qz * sd[2]) / hw;
        var v = a / hh;
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
        var key = GeomKey(d);
        if (_mouths is { } hit && hit.Key == key)
            return hit.L;
        var l = new List<Mouth>();
        Build(d, l);
        _mouths = (key, l);
        return l;
    }

    // The last mouths and spine worked out, under the key of all they come from (the checks ask for them once a room).
    private (string Key, List<Mouth> L)? _mouths;
    private (string Key, List<(double[] C, double[] S, double[] U, double W, double H)> L)? _spine;

    /// <summary>Everything the connection's shape comes from (not its cladding), with the shapes of the bodies it joins, as text.</summary>
    public string GeomKey(ShipDesign d) => ShipDesign.Whole(() =>
    {
        var o = ToJson(d);
        o.Remove("clad");
        o.Remove("buttressClad");
        return Id + ":" + o.ToJsonString() + "|" + d.Hull(A.Hull)?.ShapeKey() + "|" + d.Hull(B.Hull)?.ShapeKey();
    });

    private DesignMesh Build(ShipDesign d, List<Mouth>? mouths, List<(double[] C, double[] S, double[] U, double W, double H)>? spine = null)
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
        var centres = new List<double[]>();
        double[]? side = null;
        var ends = new List<Mouth>(); // this build's own two mouths (unmirrored), kept clear of the buttresses' plates
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
        double[] bowDir = [0, SweepUp, SweepFore];
        for (var i = 0; i <= last; i++)
        {
            var (c, t) = Curve(at[i].S);
            var sc = at[i].Scale;
            // Along the span 0..1: the taper, the twist and the bow (zero at both ends, so they still meet their skins).
            var q = Math.Clamp((at[i].S - s0) / (s1 - s0), 0, 1);
            double rw = WA + (WB - WA) * q, rh = HA + (HB - HA) * q;
            if (SweepUp != 0 || SweepFore != 0)
            {
                var bow = 4 * q * (1 - q);
                var dbow = 4 * (1 - 2 * q) / ((s1 - s0) * len);
                for (var e = 0; e < 3; e++)
                {
                    c[e] += bowDir[e] * bow;
                    t[e] += bowDir[e] * dbow;
                }
                Norm(t);
            }
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
            var upv0 = Cross(t, side);
            // The twist rolls this ring's frame round the line; the carried axis stays untwisted.
            var tw = Twist * q * Math.PI / 180;
            double cs = Math.Cos(tw), sn = Math.Sin(tw);
            double[] rs = [side[0] * cs + upv0[0] * sn, side[1] * cs + upv0[1] * sn, side[2] * cs + upv0[2] * sn];
            double[] upv = [upv0[0] * cs - side[0] * sn, upv0[1] * cs - side[1] * sn, upv0[2] * cs - side[2] * sn];
            // The strut's own rings (not the flared ends), sampled finely enough for the room check.
            if (spine is not null && sc == 1 || spine is not null && i > 0 && i < last)
                spine.Add((c, rs, upv, rw, rh));
            if (i == 0 || i == last)
            {
                var hid = i == 0 ? A.Hull : B.Hull;
                var f = i == 0 ? OpenA : OpenB;
                // Each end's own size (and the bias's thicker half, so the hole covers it).
                var mh = rh * f * (1 + Math.Abs(Bias));
                ends.Add(new Mouth(hid, c, t, rs, upv, rw * f, mh, SquareHole));
                if (mouths is not null)
                mouths.Add(new Mouth(hid, c, t, rs, upv, rw * f, mh, SquareHole));
                if (mouths is not null && Mirror && Math.Abs(a.P[0]) > 0.3)
                    mouths.Add(new Mouth(hid, [-c[0], c[1], c[2]], [-t[0], t[1], t[2]], [-rs[0], rs[1], rs[2]], [-upv[0], upv[1], upv[2]], rw * f, mh, SquareHole));
            }
            var r = new (double, double, double)[ring.Count];
            for (var j = 0; j < ring.Count; j++)
            {
                var (u, v) = ring[j];
                double uw = u * rw * sc, vh = Biased(v) * rh * sc;
                r[j] = (c[0] + rs[0] * uw + upv[0] * vh, c[1] + rs[1] * uw + upv[1] * vh, c[2] + rs[2] * uw + upv[2] * vh);
            }
            // An end ring slid along the line onto its skin, each point where it meets it.
            if (i == 0 || i == last)
            {
                var h = i == 0 ? ha : hb;
                var dir = i == 0 ? -1.0 : 1.0;
                var reach = 3 * Math.Max(rw, rh) * sc * (1 + Math.Abs(Bias)) + 2;
                for (var j = 0; j < r.Length; j++)
                {
                    var v = r[j];
                    double[] At(double f) => [v.Item1 + t[0] * dir * f, v.Item2 + t[1] * dir * f, v.Item3 + t[2] * dir * f];
                    // f from 0 (this point) to reach (deeper in): the first f inside the skin.
                    // Inside already: back out to where it leaves the skin; else on in to where it meets it.
                    var inside = In(h, At(0));
                    if (inside ? In(h, At(-reach)) : !In(h, At(reach)))
                    {
                        // The line along the strut misses this body (a wide flare past a curved side): the nearest skin point instead.
                        r[j] = Nearest(h, v.Item1, v.Item2, v.Item3);
                        continue;
                    }
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
            centres.Add(c);
        }
        if (spine is not null || mouths is not null)
            return m; // the mouths and the spine are known by here: what follows only draws
        // Follow the hull: each flared end's outer ring laid onto the skin 4 mm off it, the rings between blending from the strut's section
        // out to that footprint, so the buttress's base wraps the hull's curve and meets it flush.
        var blend = 0;
        if (Follow && dl > 1e-4)
        {
            blend = FollowRings;
            FollowEnd(rings, centres, 0, 1, ha);
            FollowEnd(rings, centres, rings.Count - 1, rings.Count - 2, hb);
            last = rings.Count - 1;
        }
        for (var i = 0; i < last; i++)
            for (var j = 0; j < ring.Count; j++)
            {
                var j1 = (j + 1) % ring.Count;
                // Hull A's paint (the joint takes its colour from A): along by the ring's share of the length, round by j.
                var pc = ha.Paint(len * i / Math.Max(1, last), (j + 0.5) / ring.Count, 4, Id * 4099 + i * 64 + j);
                var shade = DesignMesh.Tone(pc, (i % 2 == 0 ? 1.0 : 0.94) * (j % 2 == 0 ? 1.0 : 0.9));
                m.Quad(rings[i][j], rings[i + 1][j], rings[i + 1][j1], rings[i][j1], shade, false);
            }
        // The strut's own rings are clad; the flared ends into the skins (dl > 0) stay bare.
        if (Clad != "Off")
            CladStrut(m, rings, centres, dl > 1e-4 ? 1 + blend : 0, dl > 1e-4 ? last - 1 - blend : last, col, ha);
        // The buttresses: their own cladding, in rows from the strut out to where the flare sinks under each skin.
        if (ButtressClad != "Off" && dl > 1e-4)
        {
            bool Open((double X, double Y, double Z) p) => ends.Any(e => e.Holds(p.X, p.Y, p.Z));
            CladButtress(m, rings, centres, 1 + blend, 0, col, ha, Open, 1);
            CladButtress(m, rings, centres, last - 1 - blend, last, col, ha, Open, 2);
        }
        if (Mirror && Math.Abs(a.P[0]) > 0.3)
            m.MirrorFrom(0, 0);
        return m;
    }

    /// <summary>
    /// Plated cladding on the strut between rings i0 and i1 (the user, 2026-11-24: the nacelles' arms had none): a grid round the ring
    /// (by its length) and along the strut (by the middle line's), laid with the bodies' plates (<see cref="DesignScene.LayPlates"/>),
    /// each plate's normal out from the middle line. Seeded, so the same design always clads the same.
    /// </summary>
    private void CladStrut(DesignMesh m, List<(double, double, double)[]> rings, List<double[]> centres, int i0, int i1, (byte, byte, byte) col, HullBody ha)
    {
        if (i1 <= i0)
            return;
        var nr = rings[0].Length;
        // Across: the share of the way round the ring, by the length of the strut's middle ring; along: by the middle line's length.
        var mid = rings[(i0 + i1) / 2];
        var cu = new double[nr + 1];
        for (var j = 0; j < nr; j++)
        {
            var (p, q) = (mid[j], mid[(j + 1) % nr]);
            cu[j + 1] = cu[j] + Math.Sqrt(Sq(q.Item1 - p.Item1) + Sq(q.Item2 - p.Item2) + Sq(q.Item3 - p.Item3));
        }
        var cv = new double[i1 - i0 + 1];
        for (var i = i0; i < i1; i++)
            cv[i - i0 + 1] = cv[i - i0] + Math.Sqrt(Sq(centres[i + 1][0] - centres[i][0]) + Sq(centres[i + 1][1] - centres[i][1]) + Sq(centres[i + 1][2] - centres[i][2]));
        double arc = cu[nr], len = cv[^1];
        if (arc < 0.3 || len < 0.3)
            return;
        static (int K, double F) Find(double[] c, double x)
        {
            var l = Math.Clamp(x, 0, 1) * c[^1];
            for (var k = 0; k < c.Length - 2; k++)
                if (l <= c[k + 1])
                    return (k, c[k + 1] - c[k] < 1e-9 ? 0 : (l - c[k]) / (c[k + 1] - c[k]));
            var n = c.Length - 2;
            return (n, c[n + 1] - c[n] < 1e-9 ? 0 : Math.Clamp((l - c[n]) / (c[n + 1] - c[n]), 0, 1));
        }
        (double X, double Y, double Z) Lerp((double, double, double) p, (double, double, double) q, double f) =>
            (p.Item1 + (q.Item1 - p.Item1) * f, p.Item2 + (q.Item2 - p.Item2) * f, p.Item3 + (q.Item3 - p.Item3) * f);
        (double X, double Y, double Z) P(double u, double v)
        {
            var (j, fu) = Find(cu, u);
            var (i, fv) = Find(cv, v);
            i += i0;
            var j1 = (j + 1) % nr;
            var p = Lerp(rings[i][j], rings[i][j1], fu);
            var q = Lerp(rings[i + 1][j], rings[i + 1][j1], fu);
            return Lerp(p, q, fv);
        }
        (double X, double Y, double Z) Centre(double v)
        {
            var (i, fv) = Find(cv, v);
            i += i0;
            var (a, b) = (centres[i], centres[i + 1]);
            return Lerp((a[0], a[1], a[2]), (b[0], b[1], b[2]), fv);
        }
        (double X, double Y, double Z) Nrm(double u, double v)
        {
            const double e = 1e-3;
            var pu = Sub(P(Math.Min(1, u + e), v), P(Math.Max(0, u - e), v));
            var pv = Sub(P(u, Math.Min(1, v + e)), P(u, Math.Max(0, v - e)));
            double x = pu.Y * pv.Z - pu.Z * pv.Y, y = pu.Z * pv.X - pu.X * pv.Z, z = pu.X * pv.Y - pu.Y * pv.X;
            var l = Math.Sqrt(x * x + y * y + z * z);
            var o = Sub(P(u, v), Centre(v));
            if (l < 1e-12)
            {
                var ol = Math.Max(1e-9, Math.Sqrt(o.X * o.X + o.Y * o.Y + o.Z * o.Z));
                return (o.X / ol, o.Y / ol, o.Z / ol);
            }
            var sg = x * o.X + y * o.Y + z * o.Z >= 0 ? 1 : -1;
            return (sg * x / l, sg * y / l, sg * z / l);
        }
        (double, double, double) W(double u, double v, double off)
        {
            var p = P(u, v);
            var q = Nrm(u, v);
            return (p.X + q.X * off, p.Y + q.Y * off, p.Z + q.Z * off);
        }
        var size = Math.Clamp(CladSize, HullBody.CladSizeMin, HullBody.CladSizeMax);
        int nu = Math.Clamp((int)Math.Round(arc / size), 1, 64), nv = Math.Clamp((int)Math.Round(len / size), 1, 64);
        var start = m.Tris;
        DesignScene.LayPlates(m, nu, nv, arc, len, new Random(unchecked(CladSeed * 1000003 + Id * 7919)), Clad == "Heavy", CladDepth, col, P, Nrm, W, new bool[nu, nv],
            (u, v, cell) => ha.Paint(len * v, u, arc, Id * 8191 + cell));
        if (m.Tris - start > DesignScene.MaxCladTris)
            Console.WriteLine($"ship designer: cladding on connection {Id} has {m.Tris - start} triangles, past the bodies' cap of {DesignScene.MaxCladTris}");
    }

    /// <summary>
    /// Plated cladding on one buttress, the flared band between ring iS (on the strut) and ring iE (sunk under the skin): rows along the flare,
    /// each with its own count of cells round by that row's girth (as on a body's tapering terminus bands), so plates don't sliver where it
    /// widens. Cells that reach a mouth stay bare; the plates stand off the surface by at least LayPlates' floor (6 mm).
    /// </summary>
    private void CladButtress(DesignMesh m, List<(double, double, double)[]> rings, List<double[]> centres, int iS, int iE, (byte, byte, byte) col, HullBody ha,
        Func<(double X, double Y, double Z), bool> open, int end)
    {
        var nr = rings[0].Length;
        // The rings from the strut (iS) to the skin (iE): one band, or several when the buttress follows the hull.
        var step = iE > iS ? 1 : -1;
        var nb = Math.Abs(iE - iS);
        static (double X, double Y, double Z) L((double, double, double) p, (double, double, double) q, double f) =>
            (p.Item1 + (q.Item1 - p.Item1) * f, p.Item2 + (q.Item2 - p.Item2) * f, p.Item3 + (q.Item3 - p.Item3) * f);
        (int B, double F) Band(double v)
        {
            var x = Math.Clamp(v, 0, 1) * nb;
            var b = Math.Min(nb - 1, (int)x);
            return (iS + b * step, x - b);
        }
        (double X, double Y, double Z) Along(int j, double v)
        {
            var (i, f) = Band(v);
            return L(rings[i][j], rings[i + step][j], f);
        }
        // The flare as a surface: u round the ring (by point index), v from the strut (0) to the skin (1).
        (double X, double Y, double Z) S(double u, double v)
        {
            var x = (u - Math.Floor(u)) * nr;
            var j = Math.Min(nr - 1, (int)x);
            var fu = x - j;
            var j1 = (j + 1) % nr;
            var (p, q) = (Along(j, v), Along(j1, v));
            return L((p.X, p.Y, p.Z), (q.X, q.Y, q.Z), fu);
        }
        (double X, double Y, double Z) C(double v)
        {
            var (i, f) = Band(v);
            var (a, b) = (centres[i], centres[i + step]);
            return L((a[0], a[1], a[2]), (b[0], b[1], b[2]), f);
        }
        double Girth(double v)
        {
            var g = 0.0;
            for (var j = 0; j < nr; j++)
            {
                var (p, q) = (Along(j, v), Along((j + 1) % nr, v));
                g += Math.Sqrt(Sq(q.X - p.X) + Sq(q.Y - p.Y) + Sq(q.Z - p.Z));
            }
            return g;
        }
        var reach = 0.0;
        for (var j = 0; j < nr; j++)
            for (var i = iS; i != iE; i += step)
                reach += Math.Sqrt(Sq(rings[i + step][j].Item1 - rings[i][j].Item1) + Sq(rings[i + step][j].Item2 - rings[i][j].Item2) + Sq(rings[i + step][j].Item3 - rings[i][j].Item3)) / nr;
        if (reach < 0.1 || Girth(0) < 0.3)
            return;
        var size = Math.Clamp(ButtressSize, HullBody.CladSizeMin, HullBody.CladSizeMax);
        var rows = Math.Clamp((int)Math.Round(reach / size), 1, 24);
        var heavy = ButtressClad == "Heavy";
        var rng = new Random(unchecked(ButtressSeed * 1000003 + Id * 7919 + end * 31337));
        var start = m.Tris;
        for (var r = 0; r < rows; r++)
        {
            double v0 = (double)r / rows, v1 = (double)(r + 1) / rows;
            var rowArc = Girth((v0 + v1) / 2);
            var cells = Math.Clamp((int)Math.Round(rowArc / size), 3, 64);
            var shift = (r & 1) == 1 ? 0.5 / cells : 0;
            (double X, double Y, double Z) P(double u, double v) => S(u + shift, v0 + (v1 - v0) * v);
            (double X, double Y, double Z) N(double u, double v)
            {
                const double e = 1e-3;
                var pu = Sub(P(u + e, v), P(u - e, v));
                var pv = Sub(P(u, v + e), P(u, v - e));
                double x = pu.Y * pv.Z - pu.Z * pv.Y, y = pu.Z * pv.X - pu.X * pv.Z, z = pu.X * pv.Y - pu.Y * pv.X;
                var o = Sub(P(u, v), C(v0 + (v1 - v0) * v));
                var l = Math.Sqrt(x * x + y * y + z * z);
                if (l < 1e-12)
                {
                    var ol = Math.Max(1e-9, Math.Sqrt(o.X * o.X + o.Y * o.Y + o.Z * o.Z));
                    return (o.X / ol, o.Y / ol, o.Z / ol);
                }
                var sg = x * o.X + y * o.Y + z * o.Z >= 0 ? 1 : -1;
                return (sg * x / l, sg * y / l, sg * z / l);
            }
            (double, double, double) W(double u, double v, double off)
            {
                var p = P(u, v);
                var q = N(u, v);
                return (p.X + q.X * off, p.Y + q.Y * off, p.Z + q.Z * off);
            }
            var taken = new bool[cells, 1];
            for (var a = 0; a < cells; a++)
            {
                double u0 = (double)a / cells, u1 = (double)(a + 1) / cells;
                for (var i = 0; i <= 2 && !taken[a, 0]; i++)
                    for (var k = 0; k <= 2; k++)
                    {
                        var q = W(u0 + (u1 - u0) * i / 2, k / 2.0, 0.004);
                        if (open((q.Item1, q.Item2, q.Item3)))
                        {
                            taken[a, 0] = true;
                            break;
                        }
                    }
            }
            DesignScene.LayPlates(m, cells, 1, rowArc, Math.Max(0.05, reach / rows), rng, heavy, ButtressDepth, col, P, N, W, taken,
                (u, v, cell) => ha.Paint(v0 * reach, u, rowArc, Id * 12289 + end * 5000 + r * 97 + cell));
        }
        if (m.Tris - start > DesignScene.MaxCladTris)
            Console.WriteLine($"ship designer: buttress cladding on connection {Id} has {m.Tris - start} triangles, past the bodies' cap of {DesignScene.MaxCladTris}");
    }

    /// <summary>The rings a following buttress blends through between the strut and its footprint on the skin.</summary>
    public const int FollowRings = 4;

    /// <summary>How far a following buttress's base stands off the skin (m): flush, but not on it.</summary>
    public const double FollowLift = 0.004;

    /// <summary>
    /// A following buttress at one end: ring iEnd (the flared ring already slid onto the skin) laid onto the skin FollowLift off it, by the
    /// nearest skin point (its normal), and FollowRings rings put in between it and the strut's ring iStrut, each point's offset from the
    /// middle line growing from the strut's to the footprint's on an easing curve (a fillet), held FollowLift off the skin where it would dip in.
    /// </summary>
    private static void FollowEnd(List<(double, double, double)[]> rings, List<double[]> centres, int iEnd, int iStrut, HullBody h)
    {
        var nr = rings[iEnd].Length;
        var foot = new (double, double, double)[nr];
        for (var j = 0; j < nr; j++)
        {
            var (x, y, z) = rings[iEnd][j];
            foot[j] = OnTop(h, x, y, z, FollowLift);
        }
        double[] cf = [0, 0, 0];
        foreach (var (x, y, z) in foot)
        {
            cf[0] += x / nr; cf[1] += y / nr; cf[2] += z / nr;
        }
        var (rsS, csS) = (rings[iStrut], centres[iStrut]);
        var add = new List<((double, double, double)[] R, double[] C)>();
        for (var k = 1; k <= FollowRings; k++)
        {
            var v = (double)k / (FollowRings + 1); // 0 at the strut, 1 at the skin
            var e = v * v; // the fillet: the section holds near the strut and spreads at the base
            double[] c = [csS[0] + (cf[0] - csS[0]) * v, csS[1] + (cf[1] - csS[1]) * v, csS[2] + (cf[2] - csS[2]) * v];
            var r = new (double, double, double)[nr];
            for (var j = 0; j < nr; j++)
            {
                double ox = rsS[j].Item1 - csS[0], oy = rsS[j].Item2 - csS[1], oz = rsS[j].Item3 - csS[2];
                double fx = foot[j].Item1 - cf[0], fy = foot[j].Item2 - cf[1], fz = foot[j].Item3 - cf[2];
                double px = c[0] + ox + (fx - ox) * e, py = c[1] + oy + (fy - oy) * e, pz = c[2] + oz + (fz - oz) * e;
                r[j] = h.Depth(px, py, pz) > -FollowLift ? OnTop(h, px, py, pz, FollowLift) : (px, py, pz);
            }
            add.Add((r, c));
        }
        rings[iEnd] = foot;
        centres[iEnd] = cf;
        // Put in nearest the strut first: at the start (iEnd 0) the order runs skin..strut, at the end strut..skin.
        if (iEnd == 0)
        {
            add.Reverse();
            rings.InsertRange(1, add.Select(a => a.R));
            centres.InsertRange(1, add.Select(a => a.C));
        }
        else
        {
            rings.InsertRange(iEnd, add.Select(a => a.R));
            centres.InsertRange(iEnd, add.Select(a => a.C));
        }
    }

    /// <summary>The skin point nearest a world point on a body, lifted off it along its normal by lift (negative: in under it).</summary>
    private static (double, double, double) OnTop(HullBody h, double x, double y, double z, double lift)
    {
        var copy = h.Copies == 2 && Math.Abs(x - h.CopyX(1)) < Math.Abs(x - h.CopyX(0)) ? 1 : 0;
        double lx = x - h.CopyX(copy), ly = y - h.Y, lz = Math.Clamp(z - h.Z, h.Front, h.Back);
        var s = h.OnSkin(lz, h.NearestT(lx, ly, lz), lx < -0.01 ? -1 : 1);
        return (h.CopyX(copy) + s.X + s.Nx * lift, h.Y + s.Y + s.Ny * lift, h.Z + s.Z);
    }

    private static (double X, double Y, double Z) Sub((double X, double Y, double Z) a, (double X, double Y, double Z) b) => (a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    private static (double, double, double) Nearest(HullBody h, double x, double y, double z)
    {
        var copy = h.Copies == 2 && Math.Abs(x - h.CopyX(1)) < Math.Abs(x - h.CopyX(0)) ? 1 : 0;
        double lx = x - h.CopyX(copy), ly = y - h.Y, lz = Math.Clamp(z - h.Z, h.Front, h.Back);
        var s = h.OnSkin(lz, h.NearestT(lx, ly, lz), lx < -0.01 ? -1 : 1);
        // 1 cm in under the skin, so no seam shows.
        return (h.CopyX(copy) + s.X - s.Nx * 0.01, h.Y + s.Y - s.Ny * 0.01, h.Z + s.Z);
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
