namespace CallOfSlop.PartKit;

/// <summary>
/// No coplanar faces (z-fighting) for models made of boxes: the part kit's port of tools/constructs/zfight.py, line for line,
/// so that generators give the same numbers the Python did (CLAUDE.md, "No coplanar faces"). The game's own check is
/// Source/App/ZFight.cs; this is the corrector the generators call.
///   <see cref="Resolve"/>      construct parts: the generators' {"min", "max"} dicts
///   <see cref="SettleProps"/>  prop parts ({"size", "offset", "rotation", "color", ...}), any rotation
/// How far, what moves and why: the comment at the top of the Python module, kept in docs/sandbox/PARTKIT.md.
/// Every sum goes through <see cref="Py.Sum"/> and every rounding through <see cref="Py.Round"/>: the order of float
/// operations is the Python's, which is what keeps the output byte for byte.
/// </summary>
public static class PartZFight
{
    public const double Tol = 0.00055;
    public const double MinArea = 0.9e-5;
    public const double Sliver = 0.00045;
    public const double AoTop = 0.45;
    public const int Thin = 4;
    private static readonly int[] Signs = [1, -1], Mults = [1, 2, 3];
    private static readonly bool[] OutFirst = [true, false], InFirst = [false, true];

    public static double GapFor(double size)
    {
        if (size <= 0.6)
            return 0.002;
        if (size <= 1.5)
            return 0.003;
        if (size <= 4)
            return 0.005;
        if (size <= 8)
            return 0.010;
        return Math.Min(0.05, Py.Round(size / 800, 3));
    }

    private static double Dot(double[] a, double[] b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];

    private static double[] Cross(double[] a, double[] b) => [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]];

    private static double[] Norm(double[] a)
    {
        var n = Math.Sqrt(Dot(a, a));
        return [a[0] / n, a[1] / n, a[2] / n];
    }

    /// <summary>A Python number that remembers whether it is an int: sum() treats ints apart (axes_of's matrices hold both).</summary>
    private readonly record struct PyNum(double V, bool IsInt)
    {
        public static PyNum operator *(PyNum a, PyNum b) => new(a.V * b.V, a.IsInt && b.IsInt);
    }

    /// <summary>sum() over ints and floats: the int path until the first float, then the compensated float path (ints added plainly).</summary>
    private static PyNum SumMixed(PyNum[] items)
    {
        long i = 0;
        var k = 0;
        for (; k < items.Length && items[k].IsInt; k++)
            i += (long)items[k].V;
        if (k == items.Length)
            return new PyNum(i, true);
        double hi = i + items[k].V, lo = 0;
        for (k++; k < items.Length; k++)
        {
            var x = items[k].V;
            if (items[k].IsInt)
            {
                hi += x;
                continue;
            }
            var t = hi + x;
            if (Math.Abs(hi) >= Math.Abs(x))
                lo += (hi - t) + x;
            else
                lo += (x - t) + hi;
            hi = t;
        }
        return new PyNum(lo != 0 && double.IsFinite(lo) ? hi + lo : hi, false);
    }

    /// <summary>The box's three axes as world vectors: rotation [pitch, yaw, roll] applied as yaw * pitch * roll (PropPart.Rotation).</summary>
    public static double[][] AxesOf(double[] rot)
    {
        double p = rot[0] * (Math.PI / 180), y = rot[1] * (Math.PI / 180), r = rot[2] * (Math.PI / 180); // math.radians: x * (pi / 180)
        PyNum F(double v) => new(v, false);
        PyNum I(int v) => new(v, true);
        double cp = Math.Cos(p), sp = Math.Sin(p), cy = Math.Cos(y), sy = Math.Sin(y), cr = Math.Cos(r), sr = Math.Sin(r);
        PyNum[][] ry = [[F(cy), I(0), F(sy)], [I(0), I(1), I(0)], [F(-sy), I(0), F(cy)]];
        PyNum[][] rx = [[I(1), I(0), I(0)], [I(0), F(cp), F(-sp)], [I(0), F(sp), F(cp)]];
        PyNum[][] rz = [[F(cr), F(-sr), I(0)], [F(sr), F(cr), I(0)], [I(0), I(0), I(1)]];
        PyNum[][] Mul(PyNum[][] a, PyNum[][] b)
        {
            var m = new PyNum[3][];
            for (var i = 0; i < 3; i++)
            {
                m[i] = new PyNum[3];
                for (var j = 0; j < 3; j++)
                    m[i][j] = SumMixed([a[i][0] * b[0][j], a[i][1] * b[1][j], a[i][2] * b[2][j]]);
            }
            return m;
        }
        var mm = Mul(Mul(ry, rx), rz);
        var res = new double[3][];
        for (var k = 0; k < 3; k++)
            res[k] = [mm[0][k].V, mm[1][k].V, mm[2][k].V];
        return res;
    }

    /// <summary>One drawn box: centre, half sizes, three unit axes, and what may be done to it.</summary>
    public sealed class ZBox
    {
        public double[] C, H;
        public double[][] Ax;
        public string? Look;
        public bool Moving, Detail, Whole;
        public PyDict? Ref;
        public readonly HashSet<(int A, int S)> Fixed = [];
        public readonly HashSet<(int A, int S)> NoOut = [];

        public ZBox(double[] c, double[] h, double[][]? ax = null, string? look = null, bool moving = false, bool detail = false, bool whole = true, PyDict? @ref = null)
        {
            C = [.. c];
            H = [.. h];
            Ax = ax ?? [[1, 0, 0], [0, 1, 0], [0, 0, 1]];
            Look = look;
            Moving = moving;
            Detail = detail;
            Whole = whole;
            Ref = @ref;
        }

        public double Reach() => Math.Sqrt(Dot(H, H));

        public (double[] N, double D, double[][] Corners) Face(int a, int s)
        {
            double[] n = [s * Ax[a][0], s * Ax[a][1], s * Ax[a][2]];
            var ca = new double[3];
            for (var i = 0; i < 3; i++)
                ca[i] = C[i] + n[i] * H[a];
            int u = (a + 1) % 3, w = (a + 2) % 3;
            double[] du = [Ax[u][0] * H[u], Ax[u][1] * H[u], Ax[u][2] * H[u]];
            double[] dw = [Ax[w][0] * H[w], Ax[w][1] * H[w], Ax[w][2] * H[w]];
            var corners = new double[4][];
            (int su, int sw)[] signs = [(-1, -1), (1, -1), (1, 1), (-1, 1)];
            for (var k = 0; k < 4; k++)
            {
                var (su, sw) = signs[k];
                corners[k] = [ca[0] + su * du[0] + sw * dw[0], ca[1] + su * du[1] + sw * dw[1], ca[2] + su * du[2] + sw * dw[2]];
            }
            return (n, Dot(n, C) + H[a], corners);
        }

        public bool Contains(double[] p, double tol)
        {
            double[] d = [p[0] - C[0], p[1] - C[1], p[2] - C[2]];
            for (var k = 0; k < 3; k++)
                if (!(Math.Abs(Dot(d, Ax[k])) <= H[k] + tol))
                    return false;
            return true;
        }

        public double Area(int a) => 4 * H[(a + 1) % 3] * H[(a + 2) % 3];

        public (double[], double[]) Snapshot() => ([.. C], [.. H]);

        public void Restore((double[] C, double[] H) s)
        {
            C = [.. s.C];
            H = [.. s.H];
        }
    }

    /// <summary>The overlap of two coplanar rectangles: (area, centre, polygon).</summary>
    private static (double Area, double[]? Centre, double[][]? Poly) Clip(double[] n, double[][] a, double[][] b)
    {
        var e1 = Math.Abs(n[1]) < 0.9 ? Norm(Cross(n, [0, 1, 0])) : Norm(Cross(n, [1, 0, 0]));
        var e2 = Cross(n, e1);
        var o = a[0];
        (double, double) Flat(double[] p)
        {
            double[] d = [p[0] - o[0], p[1] - o[1], p[2] - o[2]];
            return (Dot(d, e1), Dot(d, e2));
        }
        var subject = a.Select(Flat).ToList();
        var clip = b.Select(Flat).ToArray();
        var signed = Py.Sum(Enumerable.Range(0, 4).Select(k => clip[k].Item1 * clip[(k + 1) % 4].Item2 - clip[k].Item2 * clip[(k + 1) % 4].Item1));
        var sgn = signed >= 0 ? 1 : -1;
        double Side((double, double) c0, (double, double) c1, (double, double) p) =>
            ((c1.Item1 - c0.Item1) * (p.Item2 - c0.Item2) - (c1.Item2 - c0.Item2) * (p.Item1 - c0.Item1)) * sgn;
        for (var k = 0; k < 4; k++)
        {
            if (subject.Count == 0)
                break;
            var c0 = clip[k];
            var c1 = clip[(k + 1) % 4];
            var src = subject;
            subject = [];
            for (var m = 0; m < src.Count; m++)
            {
                var p = src[m];
                var q = src[(m + 1) % src.Count];
                double ps = Side(c0, c1, p), qs = Side(c0, c1, q);
                var t = ps != qs ? ps / (ps - qs) : 0;
                var cut = (p.Item1 + (q.Item1 - p.Item1) * t, p.Item2 + (q.Item2 - p.Item2) * t);
                if (ps >= 0)
                {
                    subject.Add(p);
                    if (qs < 0)
                        subject.Add(cut);
                }
                else if (qs >= 0)
                    subject.Add(cut);
            }
        }
        if (subject.Count < 3)
            return (0, null, null);
        var xs = subject.Select(p => p.Item1).ToList();
        var ys = subject.Select(p => p.Item2).ToList();
        if (xs.Max() - xs.Min() < Sliver || ys.Max() - ys.Min() < Sliver)
            return (0, null, null);
        var cnt = subject.Count;
        var area = Math.Abs(Py.Sum(Enumerable.Range(0, cnt).Select(k => subject[k].Item1 * subject[(k + 1) % cnt].Item2 - subject[k].Item2 * subject[(k + 1) % cnt].Item1))) / 2;
        double cx = Py.Sum(xs) / xs.Count, cyy = Py.Sum(ys) / ys.Count;
        double[] World((double, double) p) => [o[0] + e1[0] * p.Item1 + e2[0] * p.Item2, o[1] + e1[1] * p.Item1 + e2[1] * p.Item2, o[2] + e1[2] * p.Item1 + e2[2] * p.Item2];
        return (area, World((cx, cyy)), subject.Select(World).ToArray());
    }

    private static bool Covered(List<ZBox> boxes, int i, int j, double[] n, double[] centre, double[][] poly)
    {
        for (var k = -1; k < poly.Length; k++)
        {
            var p = k < 0 ? centre : [poly[k][0] + (centre[0] - poly[k][0]) * 0.1, poly[k][1] + (centre[1] - poly[k][1]) * 0.1, poly[k][2] + (centre[2] - poly[k][2]) * 0.1];
            double[] o = [p[0] + n[0] * 0.001, p[1] + n[1] * 0.001, p[2] + n[2] * 0.001];
            var any = false;
            for (var m = 0; m < boxes.Count && !any; m++)
                if (m != i && m != j && boxes[m].Contains(o, 1e-5))
                    any = true;
            if (!any)
                return false;
        }
        return true;
    }

    private static bool SameLookHidden(ZBox a, ZBox b, double[][] ca, double[][] cb, bool shade)
    {
        if (a.Look is null || a.Look != b.Look)
            return false;
        if (!shade)
            return true;
        var ya = ca.Select(p => p[1]).ToArray();
        var yb = cb.Select(p => p[1]).ToArray();
        if (ya.Min() >= AoTop && yb.Min() >= AoTop)
            return true;
        return Math.Abs(ya.Min() - yb.Min()) < 0.001 && Math.Abs(ya.Max() - yb.Max()) < 0.001;
    }

    public readonly record struct FaceRef(int I, int A, int S) : IComparable<FaceRef>
    {
        public int CompareTo(FaceRef o) => I != o.I ? I.CompareTo(o.I) : A != o.A ? A.CompareTo(o.A) : S.CompareTo(o.S);
    }

    public readonly record struct Hit(FaceRef A, FaceRef B, double Area);

    /// <summary>Coplanar same-facing overlapping pairs not covered by a third box.</summary>
    public static List<Hit> Find(List<ZBox> boxes, bool onGround = false, bool shade = false, int? only = null)
    {
        var found = new List<Hit>();
        var n = boxes.Count;
        IEnumerable<int> outer = only is { } o ? [o] : Enumerable.Range(0, n);
        foreach (var i in outer)
        {
            var a = boxes[i];
            if (a.Moving)
                continue;
            for (var j = only is not null ? 0 : i + 1; j < n; j++)
            {
                var b = boxes[j];
                if (j == i || b.Moving)
                    continue;
                double ra = a.Reach(), rb = b.Reach();
                double[] d = [a.C[0] - b.C[0], a.C[1] - b.C[1], a.C[2] - b.C[2]];
                if (Dot(d, d) > Math.Pow(ra + rb + Tol, 2))
                    continue;
                for (var aa = 0; aa < 3; aa++)
                    foreach (var sa in Signs)
                    {
                        var (na, da, ca) = a.Face(aa, sa);
                        for (var ab = 0; ab < 3; ab++)
                        {
                            var x = Dot(a.Ax[aa], b.Ax[ab]);
                            if (Math.Abs(x) < 0.9999)
                                continue;
                            var sb = x > 0 ? sa : -sa;
                            var db = Dot(na, b.C) + b.H[ab];
                            if (Math.Abs(da - db) > Tol)
                                continue;
                            if (onGround && na[1] < -0.9999 && -da <= Tol)
                                continue;
                            var cb = b.Face(ab, sb).Corners;
                            var (area, centre, poly) = Clip(na, ca, cb);
                            if (area < MinArea)
                                continue;
                            if (SameLookHidden(a, b, ca, cb, shade) || Covered(boxes, i, j, na, centre!, poly!))
                                continue;
                            found.Add(new Hit(new FaceRef(i, aa, sa), new FaceRef(j, ab, sb), area));
                        }
                    }
            }
        }
        return found;
    }

    private static bool Touching(List<ZBox> boxes, int i, int a, int s)
    {
        var box = boxes[i];
        var (n, d, ca) = box.Face(a, s);
        for (var j = 0; j < boxes.Count; j++)
        {
            var b = boxes[j];
            if (j == i || b.Moving)
                continue;
            for (var ab = 0; ab < 3; ab++)
            {
                var x = Dot(box.Ax[a], b.Ax[ab]);
                if (Math.Abs(x) < 0.9999)
                    continue;
                var sb = x > 0 ? -s : s;
                var db = Dot(n, b.C) - b.H[ab];
                if (Math.Abs(d - db) > Tol)
                    continue;
                if (Clip(n, ca, b.Face(ab, sb).Corners).Area >= MinArea)
                    return true;
            }
        }
        return false;
    }

    private static List<(bool Whole, double D)> Options(List<ZBox> boxes, int i, int a, int s, double gap, bool contained, bool free = false)
    {
        var box = boxes[i];
        var res = new List<(bool, double)>();
        if (box.Fixed.Contains((a, s)) && !free)
            return res;
        var thick = box.H[a] * 2;
        var bothFree = box.Whole && !box.Fixed.Contains((a, -s));
        var outFirst = contained || Touching(boxes, i, a, s);
        foreach (var mult in Mults)
        {
            var d = gap * mult;
            foreach (var o in outFirst ? OutFirst : InFirst)
            {
                if (o && box.NoOut.Contains((a, s)))
                    continue;
                if (!o && !bothFree && thick - d < 0.002)
                    continue;
                var thin = thick < Thin * d;
                if (thin && bothFree)
                    res.Add((true, o ? d : -d));
                else if (o || thick - d >= 0.002)
                    res.Add((false, o ? d : -d));
            }
        }
        return res;
    }

    private static void Apply(ZBox box, int a, int s, bool whole, double d)
    {
        if (whole)
            for (var k = 0; k < 3; k++)
                box.C[k] += s * box.Ax[a][k] * d;
        else
        {
            box.H[a] += d / 2;
            for (var k = 0; k < 3; k++)
                box.C[k] += s * box.Ax[a][k] * d / 2;
        }
    }

    /// <summary>Which of two faces stays: (fixed, solid, area, -index), compared as the Python tuple.</summary>
    private static int CompareRank(List<ZBox> boxes, FaceRef f, FaceRef g)
    {
        var bf = boxes[f.I];
        var bg = boxes[g.I];
        var c = bf.Fixed.Contains((f.A, f.S)).CompareTo(bg.Fixed.Contains((g.A, g.S)));
        if (c != 0)
            return c;
        c = (!bf.Detail).CompareTo(!bg.Detail);
        if (c != 0)
            return c;
        c = bf.Area(f.A).CompareTo(bg.Area(g.A));
        if (c != 0)
            return c;
        return (-f.I).CompareTo(-g.I);
    }

    /// <summary>Moves faces until <see cref="Find"/> is empty. Returns the number of hits it couldn't clear.</summary>
    public static int Solve(List<ZBox> boxes, bool onGround = false, bool shade = false)
    {
        for (var pass = 0; pass < 40 + 4 * boxes.Count; pass++)
        {
            var hits = Find(boxes, onGround, shade);
            if (hits.Count == 0)
                return 0;
            var progress = false;
            hits = [.. hits.OrderBy(h => -h.Area).ThenBy(h => h.A).ThenBy(h => h.B)];
            foreach (var h in hits)
            {
                // sorted((a, b), key=rank): stable, so b goes first only when it ranks strictly lower
                FaceRef[] order = CompareRank(boxes, h.B, h.A) < 0 ? [h.B, h.A] : [h.A, h.B];
                var gap = GapFor(Math.Sqrt(h.Area));
                var bothFixed = order.All(f => boxes[f.I].Fixed.Contains((f.A, f.S)));
                (int Left, FaceRef F, bool Whole, double D)? best = null;
                foreach (var f in order)
                {
                    var box = boxes[f.I];
                    var before = Find(boxes, onGround, shade, f.I).Count;
                    var contained = h.Area >= box.Area(f.A) * 0.98;
                    var snap = box.Snapshot();
                    foreach (var (whole, d) in Options(boxes, f.I, f.A, f.S, gap, contained, bothFixed && f == order[0]))
                    {
                        Apply(box, f.A, f.S, whole, d);
                        var left = Find(boxes, onGround, shade, f.I).Count;
                        box.Restore(snap);
                        if (left < before && (best is null || left < best.Value.Left))
                            best = (left, f, whole, d);
                        if (left == 0)
                            break;
                    }
                    if (best is { Left: 0 })
                        break;
                }
                if (best is { } bb)
                {
                    Apply(boxes[bb.F.I], bb.F.A, bb.F.S, bb.Whole, bb.D);
                    progress = true;
                    break; // the hit list is stale now
                }
            }
            if (!progress)
                return hits.Count;
        }
        return Find(boxes, onGround, shade).Count;
    }

    // ---- construct parts: the generators' {"min", "max"} dicts ----

    private static double[] V3(object? list)
    {
        var l = (System.Collections.IList)list!;
        return [Py.D(l[0]), Py.D(l[1]), Py.D(l[2])];
    }

    private static bool Drawn(PyDict b) => !Py.Truthy(b.Get("hidden")) && !Py.Truthy(b.Get("shape"));

    private static HashSet<(int, int, int)> Locked(List<PyDict> boxes, List<PyDict>? sockets)
    {
        var locked = new HashSet<(int, int, int)>();
        foreach (var sk in sockets ?? [])
        {
            var d = (string)sk["dir"]!;
            var a = "xyz".IndexOf(d[1]);
            var s = d[0] == '+' ? 1 : -1;
            var at = V3(sk["at"]);
            for (var i = 0; i < boxes.Count; i++)
            {
                var b = boxes[i];
                if (Py.Truthy(b.Get("detail")) || !Drawn(b))
                    continue;
                double[] mn = V3(b["min"]), mx = V3(b["max"]);
                var plane = s > 0 ? mx[a] : mn[a];
                if (Math.Abs(plane - at[a]) < 1e-3 && Enumerable.Range(0, 3).Where(k => k != a).All(k => mn[k] - 1e-3 <= at[k] && at[k] <= mx[k] + 1e-3))
                    locked.Add((i, a, s));
            }
        }
        return locked;
    }

    private static List<ZBox> ConstructBoxes(List<PyDict> boxes, List<PyDict>? sockets)
    {
        var locked = Locked(boxes, sockets);
        var solids = boxes.Where(b => !Py.Truthy(b.Get("detail")) && Drawn(b)).ToList();
        var o = new List<ZBox>();
        for (var i = 0; i < boxes.Count; i++)
        {
            var b = boxes[i];
            if (!Drawn(b))
                continue;
            double[] mn = V3(b["min"]), mx = V3(b["max"]);
            double[] c = [(mn[0] + mx[0]) / 2, (mn[1] + mx[1]) / 2, (mn[2] + mx[2]) / 2];
            double[] h = [(mx[0] - mn[0]) / 2, (mx[1] - mn[1]) / 2, (mx[2] - mn[2]) / 2];
            var detail = Py.Truthy(b.Get("detail"));
            var z = new ZBox(c, h, detail: detail, whole: detail, @ref: b);
            for (var a = 0; a < 3; a++)
                foreach (var s in Signs)
                {
                    if (locked.Contains((i, a, s)))
                        z.Fixed.Add((a, s));
                    if (!z.Detail && solids.Count > 0)
                    {
                        var plane = s > 0 ? mx[a] : mn[a];
                        var limit = s > 0 ? solids.Max(q => V3(q["max"])[a]) : solids.Min(q => V3(q["min"])[a]);
                        if (Math.Abs(plane - limit) < 1e-4)
                            z.NoOut.Add((a, s));
                    }
                }
            o.Add(z);
        }
        return o;
    }

    /// <summary>Corrects <paramref name="boxes"/> in place until no pair is coplanar. Returns the number of boxes changed.</summary>
    public static int Resolve(List<PyDict> boxes, List<PyDict>? sockets = null)
    {
        var zs = ConstructBoxes(boxes, sockets);
        if (Find(zs).Count == 0)
            return 0;
        var start = zs.Select(z => z.Snapshot()).ToList();
        Solve(zs);
        var changed = 0;
        for (var k = 0; k < zs.Count; k++)
        {
            var z = zs[k];
            if (z.C.SequenceEqual(start[k].Item1) && z.H.SequenceEqual(start[k].Item2))
                continue;
            changed++;
            z.Ref!["min"] = Py.L(Py.Round(z.C[0] - z.H[0], 4), Py.Round(z.C[1] - z.H[1], 4), Py.Round(z.C[2] - z.H[2], 4));
            z.Ref!["max"] = Py.L(Py.Round(z.C[0] + z.H[0], 4), Py.Round(z.C[1] + z.H[1], 4), Py.Round(z.C[2] + z.H[2], 4));
        }
        return changed;
    }

    // ---- props: part dicts, any rotation ----

    private static readonly string[] MovingAnims = ["follow", "spin", "swing", "bob", "shake"];

    private static string Num(object? v) => Py.D(v).ToString("R", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The key of a plain look (parts with one draw the same pixels), null for anything else.</summary>
    private static string? PropLook(PyDict q)
    {
        if (Py.Truthy(q.Get("material")) || Py.Truthy(q.Get("glass")) || Py.Truthy(q.Get("anim")))
            return null;
        var c = q.Get("color");
        var colour = c is string s ? "s:" + s.ToLowerInvariant() : Py.Truthy(c) ? "t:" + string.Join(",", ((System.Collections.IList)c!).Cast<object?>().Select(Num)) : "none";
        return colour + "|" + Num(q.Get("glow") ?? 0);
    }

    public static List<ZBox> PropBoxes(List<PyDict> parts)
    {
        var zs = new List<ZBox>();
        foreach (var q in parts)
        {
            if (!q.Has("size") || Py.Truthy(q.Get("corners")))
                continue;
            var anim = q.Get("anim") as PyDict;
            var moving = Py.Truthy(anim) && MovingAnims.Contains((anim!.Get("type")?.ToString() ?? "").ToLowerInvariant());
            var size = V3(q["size"]);
            var z = new ZBox(q.Has("offset") ? V3(q["offset"]) : [0, 0, 0], [size[0] / 2, size[1] / 2, size[2] / 2],
                AxesOf(q.Has("rotation") ? V3(q["rotation"]) : [0, 0, 0]), PropLook(q), moving, @ref: q);
            if (Math.Abs(z.C[1] - Py.Sum(Enumerable.Range(0, 3).Select(k => Math.Abs(z.Ax[k][1]) * z.H[k]))) < 1e-4) // stands on the ground: stays down
                for (var a = 0; a < 3; a++)
                    if (Math.Abs(z.Ax[a][1]) > 0.9999)
                        z.Fixed.Add((a, z.Ax[a][1] > 0 ? -1 : 1));
            zs.Add(z);
        }
        return zs;
    }

    /// <summary>Corrects prop parts in place until no pair is coplanar. Returns (parts changed, hits left).</summary>
    public static (int Changed, int Left) SettleProps(List<PyDict> parts)
    {
        var zs = PropBoxes(parts);
        if (Find(zs, true, true).Count == 0)
            return (0, 0);
        var start = zs.Select(z => z.Snapshot()).ToList();
        var left = Solve(zs, true, true);
        var changed = 0;
        for (var k = 0; k < zs.Count; k++)
        {
            var z = zs[k];
            if (z.C.SequenceEqual(start[k].Item1) && z.H.SequenceEqual(start[k].Item2))
                continue;
            changed++;
            z.Ref!["size"] = Py.L(Py.Round(z.H[0] * 2, 5), Py.Round(z.H[1] * 2, 5), Py.Round(z.H[2] * 2, 5));
            z.Ref!["offset"] = Py.L(Py.Round(z.C[0], 5), Py.Round(z.C[1], 5), Py.Round(z.C[2], 5));
        }
        return (changed, left);
    }
}
