using System.Numerics;

namespace CallOfSlop.PartKit.Generators;

public sealed partial class MainframeParts
{
    /// <summary>A point or a way in ship metres, in doubles (the hull's sums; parts stand to the centimetre).</summary>
    private readonly record struct D3(double X, double Y, double Z)
    {
        public static D3 operator +(D3 a, D3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static D3 operator -(D3 a, D3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static D3 operator -(D3 a) => new(-a.X, -a.Y, -a.Z);
        public static D3 operator *(D3 a, double k) => new(a.X * k, a.Y * k, a.Z * k);
        public double Dot(D3 b) => X * b.X + Y * b.Y + Z * b.Z;
        public D3 Cross(D3 b) => new(Y * b.Z - Z * b.Y, Z * b.X - X * b.Z, X * b.Y - Y * b.X);
        public double Len => Math.Sqrt(Dot(this));
        public D3 Unit => this * (1 / Len);
        public Vector3 F => new((float)X, (float)Y, (float)Z);
        public static D3 Of(Vector3 v) => new(v.X, v.Y, v.Z);
    }

    // ---- The lines

    /// <summary>
    /// The midship section, half of it, round from the keel's edge: (out from the centreline, up from the keel). A flat keel 68 m across,
    /// the sides flaring out to the beam line at 52 m (150 m across), leaning in above it to the spine's edge, a flat spine 64 m across at 100 m.
    /// </summary>
    private static readonly (double H, double Y)[] Mid = [(34, 0), (64, 20), (75, 52), (63, 86), (32, 100)];

    /// <summary>
    /// The stations: at z the section is <see cref="Mid"/> scaled by S about its keel's middle and lifted by Y0, so every band between
    /// two stations is ruled by parallel edges and each facet is a flat trapezoid. The bow narrows and lifts to a chisel 9 m across, the
    /// keel sweeping up and the spine down to it; aft of 870 m the drive section stands a little proud, a shoulder round the stern.
    /// </summary>
    private static readonly (double Z, double S, double Y0)[] Stations = [(0, 0.06, 35), (55, 0.30, 24), (140, 0.66, 9), (230, 0.92, 1.5), (300, 1, 0), (870, 1, 0), (900, 1.05, -2.5), (1000, 1.05, -2.5)];

    private const double Bow = 0, Stern = 1000;

    private static (double S, double Y0) Scale(double z)
    {
        for (var i = 0; i + 2 < Stations.Length; i++)
            if (z <= Stations[i + 1].Z)
                return Lerp(i, z);
        return Lerp(Stations.Length - 2, z);

        static (double, double) Lerp(int i, double z)
        {
            var (za, sa, ya) = Stations[i];
            var (zb, sb, yb) = Stations[i + 1];
            var t = Math.Clamp((z - za) / (zb - za), 0, 1);
            return (sa + (sb - sa) * t, ya + (yb - ya) * t);
        }
    }

    /// <summary>The section's ten corners at z: the starboard keel edge (0) round the starboard side, over the spine and down the port side to the port keel edge (9).</summary>
    private static D3[] Ring(double z)
    {
        var (s, y0) = Scale(z);
        var r = new D3[10];
        for (var j = 0; j < 5; j++)
        {
            r[j] = new D3(s * Mid[j].H, y0 + s * Mid[j].Y, z);
            r[9 - j] = new D3(-s * Mid[j].H, y0 + s * Mid[j].Y, z);
        }
        return r;
    }

    /// <summary>The skin's half width at height y and station z (0 above or below the hull).</summary>
    private static double HalfWidth(double z, double y)
    {
        var (s, y0) = Scale(z);
        var v = (y - y0) / s;
        if (v < 0 || v > Mid[^1].Y)
            return 0;
        for (var j = 0; j + 1 < Mid.Length; j++)
            if (v <= Mid[j + 1].Y)
                return s * (Mid[j].H + (Mid[j + 1].H - Mid[j].H) * (v - Mid[j].Y) / (Mid[j + 1].Y - Mid[j].Y));
        return s * Mid[^1].H;
    }

    /// <summary>The keel's and spine's heights at z.</summary>
    private static (double Keel, double Spine) Heights(double z)
    {
        var (s, y0) = Scale(z);
        return (y0, y0 + s * Mid[^1].Y);
    }

    // ---- Planes and plates

    /// <summary>
    /// A plane plates are laid in: (V across, out, U along), V and U in centimetres. On the body's own frame (a deck, a bulkhead, the
    /// flat keel: <see cref="Node"/> -1, the plates turned by <see cref="Rot"/>), or on a frame node welded in a slanting facet (the node's
    /// frame is the plane's: x across, y out, z along).
    /// </summary>
    private sealed class Plane
    {
        public int Node = -1;
        public byte Rot;
        public D3 O, V, N, U; // ship metres: the origin and the three ways
        public string Name = "";

        public (double V, double U) Of(D3 p) => ((p - O).Dot(V) * 100, (p - O).Dot(U) * 100);

        public D3 At(double v, double n, double u) => O + V * (v / 100) + N * n + U * (u / 100);
    }

    /// <summary>A plane on the body's frame through ship point <paramref name="o"/> (to the centimetre): plates' local y along <paramref name="out_"/>, z along <paramref name="along"/>.</summary>
    private static Plane Flat(D3 o, byte out_, byte along, string name)
    {
        var r = TurnYZ(out_, along);
        o = new D3(Math.Round(o.X * 100) / 100, Math.Round(o.Y * 100) / 100, Math.Round(o.Z * 100) / 100);
        return new Plane { Rot = r, O = o, V = Way(Rot24.ApplyDir(r, PX)), N = Way(out_), U = Way(along), Name = name };
    }

    private static D3 Way(byte dir) => D3.Of(ConstructParts.DirVector(dir));

    /// <summary>An opening to cut: its middle (U along, V across) and size (A along, B across), its corners cut back by C, all cm in its plane.</summary>
    private readonly record struct Hole(double U, double V, double A, double B, double C = 0)
    {
        public double U0 => U - A / 2;
        public double U1 => U + A / 2;
        public double V0 => V - B / 2;
        public double V1 => V + B / 2;
    }

    /// <summary>One sheet of a tiling: its rectangle in the plane (cm) and the cuts the Laser Cutter makes in it (in the sheet's own U, V).</summary>
    private sealed class Tile
    {
        public int V0, V1, U0, U1;
        public readonly List<HullLoft.Cut> Cuts = [];
        public readonly List<Hole> Holes = [];
        public int W => V1 - V0;
        public int L => U1 - U0;
    }

    private static int Even(double cm) => (int)Math.Round(cm / 2) * 2;
    private static int Five(double cm) => (int)Math.Round(cm / 5) * 5;

    /// <summary>Whether a cut plate's V runs from its local -x (false) or +x (true): read once from the game's own sheet (HullLoft.CutSpot).</summary>
    private static readonly Lazy<bool> CutMirror = new(() =>
    {
        var key = HullLoft.CutKey(true, "plate 200x16x300 steel", [new HullLoft.Cut(false, 150, 60, 40, 40, 0)]);
        if (!HullLoft.CutSpot(key, 0, 0, out var a, out _) || !HullLoft.CutSpot(key, 300, 0, out var b, out _))
            throw new InvalidOperationException("mainframe: the game's cut plate has no sheet");
        if (Math.Abs(a.Z) > 0.01 || Math.Abs(b.Z - 3) > 0.01 || Math.Abs(Math.Abs(a.X) - 1) > 0.01)
            throw new InvalidOperationException($"mainframe: a cut plate's sheet isn't where it was: {a} {b}");
        return a.X > 0;
    });

    /// <summary>
    /// A convex outline in a plane (cm) dealt into sheets: strips along U (split at its corners and to <paramref name="maxL"/>), each strip
    /// in columns across V (to <paramref name="maxW"/>), each sheet a rectangle whose corners are cut off where the outline's slanting sides
    /// cross it, and with the openings that fall in it. No split runs through an opening. Plain sheets in a column run on to
    /// <paramref name="maxPlain"/> as one piece.
    /// </summary>
    private static List<Tile> Tiles(IReadOnlyList<(double V, double U)> poly, IReadOnlyList<Hole> holes, int maxW = 3200, int maxL = 6400, int maxPlain = 19200, string what = "")
    {
        var tiles = new List<Tile>();
        double umin = poly.Min(p => p.U), umax = poly.Max(p => p.U);
        var breaks = poly.Select(p => (double)Five(p.U)).Append(Five(umin)).Append(Five(umax)).Distinct().OrderBy(u => u).ToList();
        for (var i = breaks.Count - 2; i > 0; i--)
            if (breaks[i] - breaks[i - 1] < 100 || breaks[^1] - breaks[i] < 100 && i == breaks.Count - 2)
                breaks.RemoveAt(i); // (corners closer than a metre in U: one strip, its sides near enough straight)
        for (var i = 0; i + 1 < breaks.Count; i++)
        {
            double a = breaks[i], b = breaks[i + 1];
            var at = a;
            while (b - at >= 1)
            {
                var left = (int)Math.Ceiling((b - at) / maxL - 1e-9);
                var next = b;
                if (left > 1)
                {
                    next = Clear(Five(at + (b - at) / left), holes, true, at, b);
                    if (next - at > maxL || next <= at + 20) // (moved past a sheet's length off an opening: the split goes before the opening instead)
                        next = ClearBelow(Five(at + maxL), holes, at);
                }
                if (next - at >= 20)
                    Strip(poly, holes, (int)at, (int)next, maxW, tiles, what);
                at = next;
            }
        }
        // Plain sheets one after another in a column: one long piece (an Extruder makes them to 192 m).
        var merged = new List<Tile>();
        foreach (var t in tiles.OrderBy(t => t.V0).ThenBy(t => t.V1).ThenBy(t => t.U0))
        {
            if (merged.Count > 0 && merged[^1] is var last && last.Cuts.Count == 0 && t.Cuts.Count == 0 && last.V0 == t.V0 && last.V1 == t.V1 && last.U1 == t.U0 && t.U1 - last.U0 <= maxPlain)
                last.U1 = t.U1;
            else
                merged.Add(t);
        }
        foreach (var t in merged)
            if (t.Cuts.Count > 0 && HullLoft.CutRefusal(Key(t, 16)) is { } why)
                throw new InvalidOperationException($"mainframe: {what}: sheet V {t.V0}..{t.V1} U {t.U0}..{t.U1}: {why} ({Key(t, 16)})");
        return merged;
    }

    /// <summary>A split line moved off any opening it would run through (with 10 cm of sheet each side), staying within lo..hi.</summary>
    private static double Clear(double x, IReadOnlyList<Hole> holes, bool alongU, double lo, double hi)
    {
        for (var pass = 0; pass < 4; pass++)
        {
            var moved = false;
            foreach (var h in holes)
            {
                double h0 = (alongU ? h.U0 : h.V0) - 12, h1 = (alongU ? h.U1 : h.V1) + 12;
                if (x <= h0 || x >= h1)
                    continue;
                var down = Math.Floor(h0 / 5) * 5;
                var up = Math.Ceiling(h1 / 5) * 5;
                x = x - down < up - x && down - lo >= 20 || up >= hi - 20 ? down : up;
                moved = true;
            }
            if (!moved)
                break;
        }
        return x;
    }

    /// <summary>A split line moved back (to lower U) off any opening it would run through, with 12 cm of sheet each side, not below lo + 20.</summary>
    private static double ClearBelow(double x, IReadOnlyList<Hole> holes, double lo)
    {
        for (var pass = 0; pass < 64; pass++)
        {
            var moved = false;
            foreach (var h in holes)
                if (x > h.U0 - 12 && x < h.U1 + 12)
                {
                    x = Math.Floor((h.U0 - 12) / 5) * 5;
                    moved = true;
                }
            if (!moved)
                break;
        }
        return Math.Max(x, lo + 20);
    }

    /// <summary>The outline's span across (V) at U = u.</summary>
    private static (double Lo, double Hi) Span(IReadOnlyList<(double V, double U)> poly, double u)
    {
        double lo = double.MaxValue, hi = double.MinValue;
        for (var i = 0; i < poly.Count; i++)
        {
            var (a, b) = (poly[i], poly[(i + 1) % poly.Count]);
            if (Math.Abs(a.U - b.U) < 1e-9)
            {
                if (Math.Abs(a.U - u) < 1e-6)
                {
                    lo = Math.Min(lo, Math.Min(a.V, b.V));
                    hi = Math.Max(hi, Math.Max(a.V, b.V));
                }
                continue;
            }
            var t = (u - a.U) / (b.U - a.U);
            if (t < -1e-9 || t > 1 + 1e-9)
                continue;
            var v = a.V + (b.V - a.V) * t;
            lo = Math.Min(lo, v);
            hi = Math.Max(hi, v);
        }
        return (lo, hi);
    }

    private static void Strip(IReadOnlyList<(double V, double U)> poly, IReadOnlyList<Hole> holes, int ua, int ub, int maxW, List<Tile> tiles, string what)
    {
        double umin = poly.Min(p => p.U), umax = poly.Max(p => p.U);
        var (loA, hiA) = Span(poly, Math.Clamp(ua, umin + 0.01, umax - 0.01));
        var (loB, hiB) = Span(poly, Math.Clamp(ub, umin + 0.01, umax - 0.01));
        if (loA > hiA || loB > hiB)
            return;
        int vL = Even(Math.Min(loA, loB)), vR = Even(Math.Max(hiA, hiB));
        double dl = Math.Abs(loB - loA), dr = Math.Abs(hiB - hiA);
        var width = vR - vL;
        if (width < 20)
            return;
        var n = Math.Max(1, (int)Math.Ceiling(width / (double)maxW));
        var fits = (n == 1 ? width >= dl + dr + 12 : true) && dl + 14 <= maxW && dr + 14 <= maxW; // (a wedge wider than a sheet: shorter strips)
        if (!fits && ub - ua >= 400)
        {
            var mid = Clear(Five((ua + ub) / 2.0), holes, true, ua, ub);
            Strip(poly, holes, ua, (int)mid, maxW, tiles, what);
            Strip(poly, holes, (int)mid, ub, maxW, tiles, what);
            return;
        }
        var strip = holes.Where(h => h.U1 > ua && h.U0 < ub).ToList();
        var made = new List<Tile>();
        List<int> cols;
        for (; ; n++)
        {
            cols = [vL];
            for (var k = 1; k < n; k++)
                cols.Add(Even(vL + width * (double)k / n));
            cols.Add(vR);
            if (n > 1)
            {
                cols[1] = Math.Max(cols[1], Even(vL + dl + 14)); // (the first sheet takes the whole of the slanting side's wedge, the last the other's)
                cols[^2] = Math.Min(cols[^2], Even(vR - dr - 14));
            }
            for (var k = 1; k < cols.Count - 1; k++)
                cols[k] = Even(Clear(cols[k], strip, false, cols[k - 1], cols[k + 1]));
            var widest = 0;
            for (var k = 0; k + 1 < cols.Count; k++)
                widest = Math.Max(widest, cols[k + 1] - cols[k]);
            if (widest <= maxW || n > 64)
                break;
        }
        for (var k = 0; k + 1 < cols.Count; k++)
        {
            int va = cols[k], vb = cols[k + 1];
            if (vb - va < 20)
                continue;
            if (vb - va > maxW)
                throw new InvalidOperationException($"mainframe: {what}: a sheet {vb - va} cm across");
            var t = new Tile { V0 = va, V1 = vb, U0 = ua, U1 = ub };
            var l = ub - ua;
            if (k == 0)
            {
                double a = loA - va, b = loB - va;
                if (b - Math.Max(a, 0) >= 5)
                    Corner(t, 3, l, Math.Round(b - Math.Max(a, 0)));
                else if (a - Math.Max(b, 0) >= 5)
                    Corner(t, 0, l, Math.Round(a - Math.Max(b, 0)));
            }
            if (k + 2 == cols.Count)
            {
                double c = vb - hiA, d = vb - hiB;
                if (d - Math.Max(c, 0) >= 5)
                    Corner(t, 2, l, Math.Round(d - Math.Max(c, 0)));
                else if (c - Math.Max(d, 0) >= 5)
                    Corner(t, 1, l, Math.Round(c - Math.Max(d, 0)));
            }
            foreach (var h in strip)
            {
                if (h.V1 <= va || h.V0 >= vb)
                    continue;
                if (h.U0 < ua || h.U1 > ub || h.V0 < va || h.V1 > vb)
                    throw new InvalidOperationException($"mainframe: {what}: an opening U {h.U0:0}..{h.U1:0} V {h.V0:0}..{h.V1:0} crosses the sheet U {ua}..{ub} V {va}..{vb}");
                var v = h.V - va;
                if (CutMirror.Value)
                    v = t.W - v;
                t.Cuts.Add(new HullLoft.Cut(false, (int)Math.Round(h.U - ua), (int)Math.Round(v), (int)Math.Round(h.A), (int)Math.Round(h.B), (int)Math.Round(h.C)));
                t.Holes.Add(h);
            }
            made.Add(t);
        }
        // (a cutter makes at most HullLoft.MaxCuts cuts in a sheet: a strip with more in one sheet is laid as two shorter strips)
        if (made.Any(t => t.Cuts.Count > HullLoft.MaxCuts) && ub - ua >= 200)
        {
            var half = Clear(Five((ua + ub) / 2.0), holes, true, ua, ub);
            if (half - ua >= 20 && ub - half >= 20)
            {
                Strip(poly, holes, ua, (int)half, maxW, tiles, what);
                Strip(poly, holes, (int)half, ub, maxW, tiles, what);
                return;
            }
        }
        tiles.AddRange(made);
    }

    /// <summary>A corner cut off a sheet: the triangle at corner K (0 back left, 1 back right, 2 nose right, 3 nose left, in the plane's V) with legs l along and b across.</summary>
    private static void Corner(Tile t, int k, int l, double b)
    {
        var a = l - HullLoft.CutMinCm;
        var bb = (int)Math.Min(b, t.W - HullLoft.CutMinCm);
        if (bb < HullLoft.CutMinCm || a < HullLoft.CutMinCm)
            return;
        if (CutMirror.Value)
            k = k switch { 0 => 1, 1 => 0, 2 => 3, _ => 2 };
        t.Cuts.Add(new HullLoft.Cut(true, k, 0, a, bb, 0));
    }

    /// <summary>A sheet's part: plain plate off the Extruder, or plate the Laser Cutter cut. <paramref name="h"/> is the stock's height (a plate is a quarter of it thick).</summary>
    private static string Key(Tile t, int h) =>
        t.Cuts.Count == 0 ? $"{Extrusion.Prefix}plate {t.W}x{h}x{t.L} steel" : HullLoft.CutKey(true, $"plate {t.W}x{h}x{t.L} steel", t.Cuts);

    /// <summary>Thickness codes: a plate's stock height (cm; the plate is a quarter of it thick).</summary>
    private const int SkinH = 16, ArmourH = 60, DeckH = 16, WallH = 40, BulkheadH = 40;

    private static double Thick(int h) => h / 400.0;

    /// <summary>
    /// Lays one sheet in its plane, its middle <paramref name="off"/> m out from the plane: on the body's frame linked to what it touches,
    /// or hung on the plane's frame node by its back.
    /// </summary>
    private static int Sheet(Writer w, Plane pl, Tile t, int h, double off, Look look)
    {
        var key = Key(t, h);
        var mid = (t.V0 + t.V1) / 2.0;
        if (pl.Node < 0)
        {
            var at = B(pl.O.X, pl.O.Y, pl.O.Z) + Rot24.Apply(pl.Rot, new Vector3((float)(mid / 100), (float)off, (float)(t.U0 / 100.0)));
            return w.Root(key, at, pl.Rot, look);
        }
        var d = w.Def(key);
        var local = new Vector3((float)(mid / 100), (float)off, (float)(t.U0 / 100.0));
        // (by any socket left on it, the node's that faces it: a cut plate keeps its blank's sockets the cuts left)
        foreach (var (dir, node) in (ReadOnlySpan<(byte, char)>)[(NY, 'u'), (NX, 'e'), (PX, 'w'), (NZ, 'n'), (PZ, 's'), (PY, 'd')])
            foreach (var sk in d.Sockets)
                if (sk.Dir == dir && w.Free(pl.Node, node))
                    return w.OnSocket(pl.Node, w.Take(pl.Node, node), key, sk.Name, 0, local, look);
        throw new InvalidOperationException($"mainframe: {key} has no socket to hang it by (it has {string.Join(" ", d.Sockets.Select(x => $"{x.Name}:{x.Dir}"))})");
    }

    /// <summary>A whole outline in its plane, tiled and laid. The steps.</summary>
    private static List<int> Plate(Writer w, Plane pl, IReadOnlyList<(double V, double U)> poly, IReadOnlyList<Hole> holes, int h, double off, Look look, int maxW = 3200, int maxL = 6400)
    {
        var steps = new List<int>();
        foreach (var t in Tiles(poly, holes, maxW, maxL, what: pl.Name))
            steps.Add(Sheet(w, pl, t, h, off, look));
        return steps;
    }

    // ---- The hull's facets

    /// <summary>One facet of one band: its corners, its plane (a frame node's, or the body's when it lies flat), its outline in the plane.</summary>
    private sealed class Facet
    {
        public int Band, F;
        public D3 A0, A1, B0, B1;
        public Plane Plane = null!;
        public List<(double V, double U)> Poly = [];
        public bool Flat;
        public string Name = "";
    }

    private static readonly string[] FacetNames = ["lower flank", "belt", "upper belt", "shoulder", "spine", "shoulder", "upper belt", "belt", "lower flank", "keel"];

    /// <summary>Every facet of the hull, band by band from the bow (the planes not yet laid: <see cref="Frames"/> welds their nodes).</summary>
    private static List<Facet> Facets()
    {
        var list = new List<Facet>();
        for (var b = 0; b + 1 < Stations.Length; b++)
        {
            var sa = Ring(Stations[b].Z);
            var sb = Ring(Stations[b + 1].Z);
            var mid = 0.5 * (Stations[b].Z + Stations[b + 1].Z);
            var (keel, spine) = Heights(mid);
            var centre = new D3(0, 0.5 * (keel + spine), mid);
            for (var f = 0; f < 10; f++)
            {
                var g = (f + 1) % 10;
                var fc = new Facet { Band = b, F = f, A0 = sa[f], A1 = sa[g], B0 = sb[f], B1 = sb[g], Name = $"{FacetNames[f]} {(f is 4 or 9 ? "" : f < 4 ? "starboard " : "port ")}{Stations[b].Z:0}-{Stations[b + 1].Z:0} m" };
                var v = (fc.A1 - fc.A0).Unit;
                var n = (fc.A1 - fc.A0).Cross(fc.B0 - fc.A0).Unit;
                var c = (fc.A0 + fc.A1 + fc.B0 + fc.B1) * 0.25;
                if (n.Dot(c - centre) < 0)
                    n = -n;
                var u = v.Cross(n);
                if (u.Dot(fc.B0 - fc.A0) < 0)
                {
                    v = -v;
                    u = -u;
                }
                fc.Flat = Math.Abs(Math.Abs(n.Y) - 1) < 1e-9 && Math.Abs(u.Z - 1) < 1e-9;
                fc.Plane = fc.Flat
                    ? Flat(new D3(0, fc.A0.Y, fc.A0.Z), n.Y > 0 ? PY : NY, PZ, fc.Name)
                    : new Plane { O = c, V = v, N = n, U = u, Name = fc.Name };
                fc.Poly = [fc.Plane.Of(fc.A0), fc.Plane.Of(fc.A1), fc.Plane.Of(fc.B1), fc.Plane.Of(fc.B0)];
                list.Add(fc);
            }
        }
        return list;
    }

    /// <summary>The weld of a facet's frame node: its place and turn (x across, y out, z along) in the body's frame.</summary>
    private static (Vector3 Pos, Quaternion Rot) NodePose(Plane pl)
    {
        var (v, n, u) = (pl.V, pl.N, pl.U);
        var m = new Matrix4x4((float)v.X, (float)v.Y, (float)v.Z, 0, (float)n.X, (float)n.Y, (float)n.Z, 0, (float)u.X, (float)u.Y, (float)u.Z, 0, 0, 0, 0, 1);
        var q = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(m));
        var pos = new Vector3((float)(pl.O.X - Origin.X), (float)(pl.O.Y - Origin.Y), (float)(pl.O.Z - Origin.Z));
        return (pos, q);
    }
}
