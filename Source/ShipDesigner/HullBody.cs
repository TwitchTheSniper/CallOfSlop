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
    public const int MinPoints = 3, MaxPoints = 8;
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
    public int Points => Stations.Count == 0 ? 0 : Stations[0].P.Count;
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
        var n = h.Stations.Count == 0 ? 0 : h.Stations[0].P.Count;
        if (h.Stations.Count < 2 || n < MinPoints || h.Stations.Any(s => s.P.Count != n))
            throw new FormatException($"hull \"{h.Name}\": needs 2 or more stations of the same {MinPoints} to {MaxPoints} points");
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

    /// <summary>The half profile at local z (straight lines between stations), or false outside the body's length.</summary>
    public bool At(double z, Span<double> hs, Span<double> ys)
    {
        if (z < Front - 1e-6 || z > Back + 1e-6)
            return false;
        var n = Points;
        for (var i = 0; i < Stations.Count - 1; i++)
        {
            var a = Stations[i];
            var b = Stations[i + 1];
            if (z > b.Z + 1e-9 && i < Stations.Count - 2)
                continue;
            var t = b.Z - a.Z < 1e-9 ? 0 : Math.Clamp((z - a.Z) / (b.Z - a.Z), 0, 1);
            for (var k = 0; k < n; k++)
            {
                hs[k] = a.P[k].H + (b.P[k].H - a.P[k].H) * t;
                ys[k] = a.P[k].Y + (b.P[k].Y - a.P[k].Y) * t;
            }
            return true;
        }
        return false;
    }

    /// <summary>The skin's half width at local z and height y, or -1 outside the body (the Zero Day's SkinHalf).</summary>
    public double SkinHalf(double z, double y)
    {
        Span<double> hs = stackalloc double[MaxPoints];
        Span<double> ys = stackalloc double[MaxPoints];
        if (!At(z, hs, ys))
            return -1;
        var n = Points;
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
    private double Top(double z) => Edge(z, Points - 1);

    private double Edge(double z, int k)
    {
        Span<double> hs = stackalloc double[MaxPoints];
        Span<double> ys = stackalloc double[MaxPoints];
        return At(z, hs, ys) ? ys[k] : 0;
    }

    /// <summary>
    /// A point on the skin and its outward normal (hull-local, side +1 or -1): z along, t round the half profile's path from the keel's middle
    /// (0) out along the keel, up the side and in along the spine to its middle (1).
    /// </summary>
    public (double X, double Y, double Z, double Nx, double Ny) OnSkin(double z, double t, int side)
    {
        Span<double> hs = stackalloc double[MaxPoints + 2];
        Span<double> ys = stackalloc double[MaxPoints + 2];
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
        Span<double> hs = stackalloc double[MaxPoints + 2];
        Span<double> ys = stackalloc double[MaxPoints + 2];
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
        var n = Points;
        At(z, hs[1..], ys[1..]);
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
