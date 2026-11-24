using System.Text.Json.Nodes;

namespace CallOfSlop.ShipDesigner;

/// <summary>A station: where along the hull (local z) and its half profile, keel first, each point a half width and a height (metres).</summary>
public sealed class Station
{
    public double Z;
    public List<(double H, double Y)> P = [];

    public Station Clone() => new() { Z = Z, P = [.. P] };
}

/// <summary>
/// A hull body: a lofted, faceted skin between stations, as the Zero Day's (TorpedoFrigateParts: At, SkinHalf, MustFit) and the game's Hull
/// Sections (HullLoft.Section.cs: a keel-to-spine half profile of 3 to 8 points, mirrored by side). Every station of a body has the same
/// number of points, so each facet runs the body's length. A body sits at an offset; Mirror puts a twin at -x (a pair of pods or nacelles).
/// Cut: where this body and another meet, the skin inside the other is cut away, so the two open into one space.
/// </summary>
public sealed class HullBody
{
    public const int MinPoints = 3, MaxPoints = 16;
    public const double MinGap = 0.25; // between stations

    public int Id;
    public string Name = "Hull";
    public double X, Y, Z;
    public bool Mirror;
    public bool Cut = true;
    public string Skin = "#c9cdd2";
    public List<Station> Stations = [];

    public int Copies => Mirror && Math.Abs(X) > 0.01 ? 2 : 1;
    public double CopyX(int copy) => copy == 1 ? -X : X;
    public int Points => Stations.Count == 0 ? 0 : Stations.Max(s => s.P.Count); // the most any station has
    /// <summary>The most points a lofted profile between two stations can have: every corner of both ends.</summary>
    public const int MaxLoft = 2 * MaxPoints;

    /// <summary>A segment's two end profiles at one shared set of points (Karl, 2026-11-24: stations may differ in point count). With the
    /// same count they are the stations' own points, matched one to one; otherwise every corner of each end, placed by how far round
    /// its profile it is (keel 0, spine 1), and the other end sampled at that same fraction, so neither end's shape changes.</summary>
    public int Pair(int i, Span<double> ah, Span<double> ay, Span<double> bh, Span<double> by)
    {
        var a = Stations[i].P;
        var b = Stations[i + 1].P;
        if (a.Count == b.Count)
        {
            for (var k = 0; k < a.Count; k++)
                (ah[k], ay[k], bh[k], by[k]) = (a[k].H, a[k].Y, b[k].H, b[k].Y);
            return a.Count;
        }
        Span<double> fa = stackalloc double[MaxPoints];
        Span<double> fb = stackalloc double[MaxPoints];
        Fractions(a, fa);
        Fractions(b, fb);
        Span<double> f = stackalloc double[MaxLoft];
        int n = 0, ia = 0, ib = 0;
        while (ia < a.Count || ib < b.Count)
        {
            var next = ib >= b.Count || (ia < a.Count && fa[ia] <= fb[ib]) ? fa[ia++] : fb[ib++];
            if (n == 0 || next - f[n - 1] > 1e-4)
                f[n++] = next;
        }
        for (var k = 0; k < n; k++)
        {
            (ah[k], ay[k]) = Sample(a, fa, f[k]);
            (bh[k], by[k]) = Sample(b, fb, f[k]);
        }
        return n;
    }

    private static void Fractions(List<(double H, double Y)> p, Span<double> f)
    {
        f[0] = 0;
        for (var k = 1; k < p.Count; k++)
            f[k] = f[k - 1] + Len(p[k].H - p[k - 1].H, p[k].Y - p[k - 1].Y);
        var total = f[p.Count - 1];
        for (var k = 0; k < p.Count; k++)
            f[k] = total < 1e-9 ? (double)k / (p.Count - 1) : f[k] / total;
    }

    private static (double H, double Y) Sample(List<(double H, double Y)> p, Span<double> f, double at)
    {
        for (var k = 0; k < p.Count - 1; k++)
            if (at <= f[k + 1] + 1e-12)
            {
                var t = f[k + 1] - f[k] < 1e-12 ? 0 : Math.Clamp((at - f[k]) / (f[k + 1] - f[k]), 0, 1);
                return (p[k].H + (p[k + 1].H - p[k].H) * t, p[k].Y + (p[k + 1].Y - p[k].Y) * t);
            }
        return p[^1];
    }
    public double Front => Stations[0].Z;
    public double Back => Stations[^1].Z;

    public JsonObject ToJson() => new()
    {
        ["name"] = Name, ["x"] = ShipDesign.R(X), ["y"] = ShipDesign.R(Y), ["z"] = ShipDesign.R(Z), ["mirror"] = Mirror, ["cut"] = Cut, ["skin"] = Skin,
        ["stations"] = new JsonArray([.. Stations.Select(s => (JsonNode)new JsonObject
        {
            ["z"] = ShipDesign.R(s.Z),
            ["p"] = new JsonArray([.. s.P.Select(q => (JsonNode)new JsonArray(ShipDesign.R(q.H), ShipDesign.R(q.Y)))]),
        })]),
    };

    public static HullBody FromJson(JsonObject o, int id)
    {
        var h = new HullBody
        {
            Id = id, Name = (string?)o["name"] ?? "Hull", X = ShipDesign.F(o["x"], 0), Y = ShipDesign.F(o["y"], 0), Z = ShipDesign.F(o["z"], 0),
            Mirror = (bool?)o["mirror"] ?? false, Cut = (bool?)o["cut"] ?? true, Skin = (string?)o["skin"] ?? "#c9cdd2",
        };
        foreach (var s in o["stations"]?.AsArray() ?? [])
        {
            if (s is not JsonObject so || h.Stations.Count >= ShipDesign.MaxStations)
                continue;
            var st = new Station { Z = ShipDesign.F(so["z"], 0) };
            foreach (var q in so["p"]?.AsArray() ?? [])
                if (q is JsonArray a && a.Count == 2 && st.P.Count < MaxPoints)
                    st.P.Add(((double)a[0]!, (double)a[1]!));
            h.Stations.Add(st);
        }
        // Stations may have different point counts (Karl, 2026-11-24): each segment is lofted through both ends' corners.
        if (h.Stations.Count < 2 || h.Stations.Any(s => s.P.Count < MinPoints))
            throw new FormatException($"hull \"{h.Name}\": needs 2 or more stations of {MinPoints} to {MaxPoints} points each");
        h.Tidy();
        return h;
    }

    public HullBody Clone(int id) => new()
    {
        Id = id, Name = Name, X = X, Y = Y, Z = Z, Mirror = Mirror, Cut = Cut, Skin = Skin, Stations = [.. Stations.Select(s => s.Clone())],
    };

    /// <summary>
    /// Keeps the shape buildable: stations in order and apart, each profile's heights rising from the keel (a point at least 5 cm over the last),
    /// half widths from 5 cm to 16 m (a Hull Section's reach, HullLoft.SectionMaxHalfCm).
    /// </summary>
    public void Tidy()
    {
        Stations.Sort((a, b) => a.Z.CompareTo(b.Z));
        for (var i = 1; i < Stations.Count; i++)
            if (Stations[i].Z < Stations[i - 1].Z + MinGap)
                Stations[i].Z = Stations[i - 1].Z + MinGap;
        foreach (var s in Stations)
            for (var k = 0; k < s.P.Count; k++)
            {
                var (h, y) = s.P[k];
                h = Math.Clamp(h, 0.05, 16);
                if (k > 0 && y < s.P[k - 1].Y + 0.05)
                    y = s.P[k - 1].Y + 0.05;
                s.P[k] = (Math.Round(h, 3), Math.Round(y, 3));
            }
    }

    // ---- the shape

    /// <summary>The half profile at local z (straight lines between stations): its point count, or 0 outside the body's length.
    /// The spans hold MaxLoft points.</summary>
    public int At(double z, Span<double> hs, Span<double> ys)
    {
        if (z < Front - 1e-6 || z > Back + 1e-6)
            return 0;
        Span<double> ah = stackalloc double[MaxLoft];
        Span<double> ay = stackalloc double[MaxLoft];
        Span<double> bh = stackalloc double[MaxLoft];
        Span<double> by = stackalloc double[MaxLoft];
        for (var i = 0; i < Stations.Count - 1; i++)
        {
            var a = Stations[i];
            var b = Stations[i + 1];
            if (z > b.Z + 1e-9 && i < Stations.Count - 2)
                continue;
            var t = b.Z - a.Z < 1e-9 ? 0 : Math.Clamp((z - a.Z) / (b.Z - a.Z), 0, 1);
            var n = Pair(i, ah, ay, bh, by);
            for (var k = 0; k < n; k++)
            {
                hs[k] = ah[k] + (bh[k] - ah[k]) * t;
                ys[k] = ay[k] + (by[k] - ay[k]) * t;
            }
            return n;
        }
        return 0;
    }

    /// <summary>The skin's half width at local z and height y, or -1 outside the body (the Zero Day's SkinHalf).</summary>
    public double SkinHalf(double z, double y)
    {
        Span<double> hs = stackalloc double[MaxLoft];
        Span<double> ys = stackalloc double[MaxLoft];
        var n = At(z, hs, ys);
        if (n == 0)
            return -1;
        if (y < ys[0] || y > ys[n - 1])
            return -1;
        for (var k = 0; k < n - 1; k++)
            if (y <= ys[k + 1])
                return hs[k] + (hs[k + 1] - hs[k]) * (y - ys[k]) / (ys[k + 1] - ys[k]);
        return hs[n - 1];
    }

    /// <summary>How far a world point is inside this body (either copy), metres in from the skin across; negative outside.</summary>
    public double Depth(double wx, double wy, double wz)
    {
        var best = double.NegativeInfinity;
        for (var c = 0; c < Copies; c++)
        {
            var lz = wz - Z;
            var ly = wy - Y;
            var half = SkinHalf(lz, ly);
            if (half < 0)
                continue;
            var lx = Math.Abs(wx - CopyX(c));
            var d = Math.Min(half - lx, Math.Min(Math.Min(lz - Front, Back - lz), Math.Min(ly - Bottom(lz), Top(lz) - ly)));
            best = Math.Max(best, d);
        }
        return best;
    }

    private double Bottom(double z) => Edge(z, 0);
    private double Top(double z) => Edge(z, -1);

    /// <summary>A profile point's height at z (k -1: the last, the spine).</summary>
    private double Edge(double z, int k)
    {
        Span<double> hs = stackalloc double[MaxLoft];
        Span<double> ys = stackalloc double[MaxLoft];
        var n = At(z, hs, ys);
        return n == 0 ? 0 : ys[k < 0 ? n - 1 : k];
    }

    /// <summary>
    /// A point on the skin and its outward normal (hull-local, side +1 or -1): z along, t round the half profile's path from the keel's middle
    /// (0) out along the keel, up the side and in along the spine to its middle (1).
    /// </summary>
    public (double X, double Y, double Z, double Nx, double Ny) OnSkin(double z, double t, int side)
    {
        Span<double> hs = stackalloc double[MaxLoft + 2];
        Span<double> ys = stackalloc double[MaxLoft + 2];
        z = Math.Clamp(z, Front, Back);
        var n = Path(z, hs, ys);
        var total = 0.0;
        for (var k = 0; k < n - 1; k++)
            total += Len(hs[k + 1] - hs[k], ys[k + 1] - ys[k]);
        var want = Math.Clamp(t, 0, 1) * total;
        for (var k = 0; k < n - 1; k++)
        {
            var dx = hs[k + 1] - hs[k];
            var dy = ys[k + 1] - ys[k];
            var l = Len(dx, dy);
            if (want <= l || k == n - 2)
            {
                var f = l < 1e-9 ? 0 : Math.Clamp(want / l, 0, 1);
                // The outward normal of a run going round the +x side from the keel to the spine: (dy, -dx).
                var nx = dy / Math.Max(l, 1e-9);
                var ny = -dx / Math.Max(l, 1e-9);
                return (side * (hs[k] + dx * f), ys[k] + dy * f, z, side * nx, ny);
            }
            want -= l;
        }
        return (0, ys[0], z, 0, -1);
    }

    /// <summary>The t of the skin point nearest a hull-local point (its side taken from x).</summary>
    public double NearestT(double x, double y, double z)
    {
        Span<double> hs = stackalloc double[MaxLoft + 2];
        Span<double> ys = stackalloc double[MaxLoft + 2];
        var n = Path(Math.Clamp(z, Front, Back), hs, ys);
        var ax = Math.Abs(x);
        double total = 0, bestAt = 0, best = double.MaxValue;
        for (var k = 0; k < n - 1; k++)
        {
            var dx = hs[k + 1] - hs[k];
            var dy = ys[k + 1] - ys[k];
            var l = Len(dx, dy);
            var f = l < 1e-9 ? 0 : Math.Clamp(((ax - hs[k]) * dx + (y - ys[k]) * dy) / (l * l), 0, 1);
            var d = Len(hs[k] + dx * f - ax, ys[k] + dy * f - y);
            if (d < best)
                (best, bestAt) = (d, total + f * l);
            total += l;
        }
        return total < 1e-9 ? 0 : bestAt / total;
    }

    /// <summary>The path round the half profile at z: the keel's middle, the profile's points, the spine's middle.</summary>
    private int Path(double z, Span<double> hs, Span<double> ys)
    {
        var n = At(z, hs[1..], ys[1..]);
        if (n == 0)
            return 0;
        hs[0] = 0;
        ys[0] = ys[1];
        hs[n + 1] = 0;
        ys[n + 1] = ys[n];
        return n + 2;
    }

    private static double Len(double x, double y) => Math.Sqrt(x * x + y * y);

    /// <summary>The body's extent in world space (both copies).</summary>
    public (double X0, double X1, double Y0, double Y1, double Z0, double Z1) Bounds()
    {
        double w = 0, y0 = double.MaxValue, y1 = double.MinValue;
        foreach (var s in Stations)
            foreach (var (h, y) in s.P)
            {
                w = Math.Max(w, h);
                y0 = Math.Min(y0, y);
                y1 = Math.Max(y1, y);
            }
        var xs = Enumerable.Range(0, Copies).Select(CopyX).ToArray();
        return (xs.Min() - w, xs.Max() + w, Y + y0, Y + y1, Z + Front, Z + Back);
    }
}
