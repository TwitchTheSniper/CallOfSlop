using System.Numerics;
using CallOfSlop.Space;

namespace CallOfSlop;

/// <summary>
/// One source of gravity in a <see cref="GravityModel"/> (docs/space/GRAVITY.md, the architecture's phase 5): a point mass, a
/// set of mascons, a body made of tetrahedra, or a uniform pull. Every source knows its total pull (<see cref="Mu"/>, GM in
/// m^3/s^2), its centre of mass and a sphere round that centre holding all of its mass (<see cref="Bound"/>), so a far
/// query can treat it as one point. Positions are doubles in the field's own frame (a map's world, or a body's body-fixed
/// frame for ships). Queries never allocate.
/// </summary>
public abstract class GravitySource
{
    /// <summary>A label for messages.</summary>
    public string Name = "";

    /// <summary>Centre of mass.</summary>
    public Vec3d Center;

    /// <summary>Total GM (m^3/s^2).</summary>
    public double Mu;

    /// <summary>Radius of a sphere round <see cref="Center"/> holding all the mass (0: a point).</summary>
    public double Bound;

    /// <summary>The acceleration at <paramref name="p"/> at full accuracy.</summary>
    public abstract Vec3d Exact(Vec3d p);

    /// <summary>
    /// The acceleration at <paramref name="p"/> from the cheapest level that is accurate there: a point mass beyond
    /// <paramref name="far"/> times <see cref="Bound"/>, else the source's own levels.
    /// </summary>
    public virtual Vec3d Accel(Vec3d p, double far)
    {
        var d = p - Center;
        var d2 = d.LengthSquared;
        if (Bound > 0 && d2 > far * far * Bound * Bound)
        {
            Counters.Far++;
            return d * (-Mu / (d2 * Math.Sqrt(d2)));
        }
        return Exact(p);
    }

    /// <summary>A rough size of this source's pull at <paramref name="p"/> (which source dominates there).</summary>
    public virtual double Strength(Vec3d p)
    {
        var d2 = Math.Max((p - Center).LengthSquared, Bound * Bound);
        return d2 > 0 ? Mu / d2 : 0;
    }

    /// <summary>Fingerprint data.</summary>
    public abstract void Mix(Action<float> mix);

    /// <summary>How many queries took each level (the self-test and the F9 window). Not thread-safe: the game thread only.</summary>
    public static class Counters
    {
        public static long Far, Mid, Exact;
    }
}

/// <summary>
/// A round body: a point mass outside <see cref="Radius"/>, and inside it the pull of a uniform ball (falling linearly to
/// zero at the centre). <see cref="Radius"/> 0 is a bare point.
/// </summary>
public sealed class PointSource : GravitySource
{
    public readonly double Radius;

    public PointSource(Vec3d center, double mu, double radius, string name = "")
    {
        Center = center;
        Mu = mu;
        Radius = Math.Max(radius, 0);
        Bound = Radius;
        Name = name;
    }

    public override Vec3d Exact(Vec3d p)
    {
        var d = p - Center;
        var d2 = d.LengthSquared;
        if (d2 >= Radius * Radius)
            return d2 > 0 ? d * (-Mu / (d2 * Math.Sqrt(d2))) : Vec3d.Zero;
        return d * (-Mu / (Radius * Radius * Radius)); // inside a uniform ball
    }

    public override Vec3d Accel(Vec3d p, double far) => Exact(p); // (as cheap as a far point)

    public override void Mix(Action<float> mix)
    {
        mix(20f);
        mix((float)Center.X); mix((float)Center.Y); mix((float)Center.Z);
        mix((float)Mu); mix((float)Radius);
    }
}

/// <summary>The same pull everywhere (a hand-placed field inside a sum). It has no centre and no mass.</summary>
public sealed class UniformSource : GravitySource
{
    public readonly Vec3d G;

    public UniformSource(Vec3d g, string name = "")
    {
        G = g;
        Name = name;
    }

    public override Vec3d Exact(Vec3d p) => G;

    public override Vec3d Accel(Vec3d p, double far) => G;

    public override double Strength(Vec3d p) => G.Length;

    public override void Mix(Action<float> mix)
    {
        mix(21f);
        mix((float)G.X); mix((float)G.Y); mix((float)G.Z);
    }
}

/// <summary>
/// Mascons: point masses spread through a body (irregular rocks, a stitched pair at medium range). Each is softened
/// (Plummer, <see cref="Soft"/>) so the field stays finite inside the body.
/// </summary>
public sealed class MasconSource : GravitySource
{
    public readonly Vec3d[] Points;
    public readonly double[] Mus;
    public readonly double Soft;

    /// <param name="weights">Relative weights (null: all equal); scaled so they sum to <paramref name="mu"/>.</param>
    public MasconSource(Vec3d[] points, double[]? weights, double mu, double soft, string name = "")
    {
        if (points.Length == 0)
            throw new ArgumentException("mascons need at least one point");
        Points = points;
        Mus = new double[points.Length];
        var sum = 0.0;
        for (var i = 0; i < points.Length; i++)
        {
            var w = weights?[i] ?? 1.0;
            if (!(w > 0) || !double.IsFinite(w))
                throw new ArgumentException($"mascon {i}: weight must be above 0");
            sum += w;
        }
        var c = Vec3d.Zero;
        for (var i = 0; i < points.Length; i++)
        {
            Mus[i] = mu * (weights?[i] ?? 1.0) / sum;
            c += points[i] * (Mus[i] / mu);
        }
        Center = c;
        Mu = mu;
        Soft = Math.Max(soft, 0);
        var b = 0.0;
        foreach (var q in points)
            b = Math.Max(b, (q - c).Length);
        Bound = b + Soft;
        Name = name;
    }

    public override Vec3d Exact(Vec3d p)
    {
        Counters.Mid++;
        var e2 = Soft * Soft;
        var gx = 0.0;
        var gy = 0.0;
        var gz = 0.0;
        var pts = Points;
        var mus = Mus;
        for (var i = 0; i < pts.Length; i++)
        {
            var dx = p.X - pts[i].X;
            var dy = p.Y - pts[i].Y;
            var dz = p.Z - pts[i].Z;
            var r2 = dx * dx + dy * dy + dz * dz + e2;
            if (r2 <= 0)
                continue;
            var k = mus[i] / (r2 * Math.Sqrt(r2));
            gx -= dx * k;
            gy -= dy * k;
            gz -= dz * k;
        }
        return new Vec3d(gx, gy, gz);
    }

    public override void Mix(Action<float> mix)
    {
        mix(22f);
        mix(Points.Length); mix((float)Soft);
        for (var i = 0; i < Points.Length; i++)
        {
            mix((float)Points[i].X); mix((float)Points[i].Y); mix((float)Points[i].Z); mix((float)Mus[i]);
        }
    }
}

/// <summary>
/// A body made of tetrahedra, each with its own density (stitched planetoids, hollow worlds, a dense core). Close in, the
/// field is exact: the closed form for uniform polyhedra (Werner and Scheeres 1997), summed over the boundary faces and
/// edges of each density, so it holds inside the body too (hollow worlds, the neck of a stitched pair). Faces shared by two
/// tetrahedra of the same density cancel when the body is built, so a fine mesh costs only its surface. At medium range
/// each tetrahedron is its own point mass (mascons), and far away the whole body is one point.
/// </summary>
public sealed class TetraSource : GravitySource
{
    /// <summary>Medium range: tetrahedra become point masses when the query is this many of the largest tetrahedron's radius outside <see cref="GravitySource.Bound"/>.</summary>
    public double MidFactor = 10;

    public readonly Vec3d[] Vertices;
    public readonly int TetraCount;

    // Boundary faces: three vertex indices, the outward unit normal, G times density.
    private readonly int[] _fv;
    private readonly Vec3d[] _fn;
    private readonly double[] _fg;
    // Edges: two vertex indices, the length, the summed G rho n_f n_fe^T dyads (row-major 3x3).
    private readonly int[] _ev;
    private readonly double[] _elen;
    private readonly double[] _ee;
    // Medium range: each tetrahedron's centroid and GM.
    private readonly Vec3d[] _tc;
    private readonly double[] _tmu;
    private readonly double _tetraRadius;

    public int FaceCount => _fg.Length;
    public int EdgeCount => _elen.Length;

    /// <param name="tetras">Four vertex indices per tetrahedron (any winding).</param>
    /// <param name="density">Relative density per tetrahedron (null: uniform); scaled so the body's GM is <paramref name="mu"/>.</param>
    public TetraSource(Vec3d[] vertices, int[][] tetras, double[]? density, double mu, string name = "")
    {
        if (tetras.Length == 0)
            throw new ArgumentException("needs at least one tetrahedron");
        Vertices = vertices;
        TetraCount = tetras.Length;
        Name = name;
        var t4 = new int[tetras.Length * 4];
        var vol = new double[tetras.Length];
        var mass = 0.0; // sum of relative density x volume
        for (var i = 0; i < tetras.Length; i++)
        {
            var t = tetras[i];
            if (t.Length != 4)
                throw new ArgumentException($"tetrahedron {i}: needs 4 vertex indices");
            foreach (var k in t)
                if ((uint)k >= (uint)vertices.Length)
                    throw new ArgumentException($"tetrahedron {i}: vertex {k} doesn't exist");
            int a = t[0], b = t[1], c = t[2], d = t[3];
            var v = Det(vertices[b] - vertices[a], vertices[c] - vertices[a], vertices[d] - vertices[a]) / 6;
            if (v < 0)
            {
                (b, c) = (c, b);
                v = -v;
            }
            var size = Math.Max((vertices[b] - vertices[a]).LengthSquared, Math.Max((vertices[c] - vertices[a]).LengthSquared, (vertices[d] - vertices[a]).LengthSquared));
            if (!(v > 1e-9 * size * Math.Sqrt(size)))
                throw new ArgumentException($"tetrahedron {i}: flat (its four corners lie in one plane)");
            var rho = density?[i] ?? 1.0;
            if (!(rho > 0) || !double.IsFinite(rho))
                throw new ArgumentException($"tetrahedron {i}: density must be above 0");
            t4[i * 4] = a; t4[i * 4 + 1] = b; t4[i * 4 + 2] = c; t4[i * 4 + 3] = d;
            vol[i] = v;
            mass += rho * v;
        }
        Mu = mu;
        var gScale = mu / mass; // G rho per unit relative density
        _tc = new Vec3d[tetras.Length];
        _tmu = new double[tetras.Length];
        var com = Vec3d.Zero;
        for (var i = 0; i < tetras.Length; i++)
        {
            var c = (vertices[t4[i * 4]] + vertices[t4[i * 4 + 1]] + vertices[t4[i * 4 + 2]] + vertices[t4[i * 4 + 3]]) * 0.25;
            _tc[i] = c;
            _tmu[i] = gScale * (density?[i] ?? 1.0) * vol[i];
            com += c * (_tmu[i] / mu);
        }
        Center = com;
        var bound = 0.0;
        var tr = 0.0;
        for (var i = 0; i < tetras.Length; i++)
            for (var k = 0; k < 4; k++)
            {
                var q = vertices[t4[i * 4 + k]];
                bound = Math.Max(bound, (q - com).Length);
                tr = Math.Max(tr, (q - _tc[i]).Length);
            }
        Bound = bound;
        _tetraRadius = tr;

        // Outward faces of every tetrahedron (with positive volume): (a,c,b), (a,b,d), (a,d,c), (b,c,d). A face met twice
        // (by two tetrahedra of the same density) is inside the body and cancels.
        var faces = new Dictionary<(int, int, int, double), (int A, int B, int C, int Count)>();
        for (var i = 0; i < tetras.Length; i++)
        {
            int a = t4[i * 4], b = t4[i * 4 + 1], c = t4[i * 4 + 2], d = t4[i * 4 + 3];
            var g = gScale * (density?[i] ?? 1.0);
            AddFace(faces, a, c, b, g);
            AddFace(faces, a, b, d, g);
            AddFace(faces, a, d, c, g);
            AddFace(faces, b, c, d, g);
        }
        var fv = new List<int>();
        var fn = new List<Vec3d>();
        var fg = new List<double>();
        var edges = new Dictionary<(int, int), double[]>();
        foreach (var (key, f) in faces)
        {
            if (f.Count % 2 == 0)
                continue;
            var n = Vec3d.Cross(vertices[f.B] - vertices[f.A], vertices[f.C] - vertices[f.A]).Normalized();
            fv.Add(f.A); fv.Add(f.B); fv.Add(f.C);
            fn.Add(n);
            fg.Add(key.Item4);
            AddEdge(edges, vertices, f.A, f.B, n, key.Item4);
            AddEdge(edges, vertices, f.B, f.C, n, key.Item4);
            AddEdge(edges, vertices, f.C, f.A, n, key.Item4);
        }
        _fv = fv.ToArray();
        _fn = fn.ToArray();
        _fg = fg.ToArray();
        _ev = new int[edges.Count * 2];
        _elen = new double[edges.Count];
        _ee = new double[edges.Count * 9];
        var e = 0;
        foreach (var (key, m) in edges)
        {
            _ev[e * 2] = key.Item1;
            _ev[e * 2 + 1] = key.Item2;
            _elen[e] = (vertices[key.Item2] - vertices[key.Item1]).Length;
            Array.Copy(m, 0, _ee, e * 9, 9);
            e++;
        }
    }

    private static void AddFace(Dictionary<(int, int, int, double), (int, int, int, int)> faces, int a, int b, int c, double g)
    {
        // The key is the sorted triple; the stored winding is the first one met (its twin, if any, is the other way round).
        int x = a, y = b, z = c;
        if (x > y) (x, y) = (y, x);
        if (y > z) (y, z) = (z, y);
        if (x > y) (x, y) = (y, x);
        var key = (x, y, z, g);
        faces[key] = faces.TryGetValue(key, out var f) ? (f.Item1, f.Item2, f.Item3, f.Item4 + 1) : (a, b, c, 1);
    }

    private static void AddEdge(Dictionary<(int, int), double[]> edges, Vec3d[] v, int u, int w, Vec3d n, double g)
    {
        // The edge's outward normal in the face's plane: (w - u) x n for a face wound anticlockwise seen from outside.
        var ne = Vec3d.Cross(v[w] - v[u], n).Normalized();
        var key = u < w ? (u, w) : (w, u);
        if (!edges.TryGetValue(key, out var m))
            edges[key] = m = new double[9];
        m[0] += g * n.X * ne.X; m[1] += g * n.X * ne.Y; m[2] += g * n.X * ne.Z;
        m[3] += g * n.Y * ne.X; m[4] += g * n.Y * ne.Y; m[5] += g * n.Y * ne.Z;
        m[6] += g * n.Z * ne.X; m[7] += g * n.Z * ne.Y; m[8] += g * n.Z * ne.Z;
    }

    private static double Det(Vec3d a, Vec3d b, Vec3d c) => Vec3d.Dot(a, Vec3d.Cross(b, c));

    public override Vec3d Accel(Vec3d p, double far)
    {
        var d = p - Center;
        var d2 = d.LengthSquared;
        if (d2 > far * far * Bound * Bound)
        {
            Counters.Far++;
            return d * (-Mu / (d2 * Math.Sqrt(d2)));
        }
        var gap = Math.Sqrt(d2) - Bound;
        if (gap > MidFactor * _tetraRadius)
            return Mascons(p);
        return Exact(p);
    }

    /// <summary>Each tetrahedron as a point mass at its centroid (medium range).</summary>
    public Vec3d Mascons(Vec3d p)
    {
        Counters.Mid++;
        var g = Vec3d.Zero;
        for (var i = 0; i < _tc.Length; i++)
        {
            var d = p - _tc[i];
            var d2 = d.LengthSquared;
            g -= d * (_tmu[i] / (d2 * Math.Sqrt(d2)));
        }
        return g;
    }

    /// <summary>The closed form for uniform polyhedra (exact inside and out).</summary>
    public override Vec3d Exact(Vec3d p)
    {
        Counters.Exact++;
        var v = Vertices;
        double gx = 0, gy = 0, gz = 0;
        // Edges: -E_e r_e L_e.
        for (var e = 0; e < _elen.Length; e++)
        {
            var r1 = v[_ev[e * 2]] - p;
            var r2 = v[_ev[e * 2 + 1]] - p;
            var a = r1.Length;
            var b = r2.Length;
            var len = _elen[e];
            var den = a + b - len;
            if (den <= 1e-12 * (a + b))
                continue; // on the edge itself: E r_e -> 0 faster than L grows
            var l = Math.Log((a + b + len) / den);
            var o = e * 9;
            gx -= (_ee[o] * r1.X + _ee[o + 1] * r1.Y + _ee[o + 2] * r1.Z) * l;
            gy -= (_ee[o + 3] * r1.X + _ee[o + 4] * r1.Y + _ee[o + 5] * r1.Z) * l;
            gz -= (_ee[o + 6] * r1.X + _ee[o + 7] * r1.Y + _ee[o + 8] * r1.Z) * l;
        }
        // Faces: + G rho n (n . r_f) omega_f.
        for (var f = 0; f < _fg.Length; f++)
        {
            var r1 = v[_fv[f * 3]] - p;
            var r2 = v[_fv[f * 3 + 1]] - p;
            var r3 = v[_fv[f * 3 + 2]] - p;
            var l1 = r1.Length;
            var l2 = r2.Length;
            var l3 = r3.Length;
            var num = Vec3d.Dot(r1, Vec3d.Cross(r2, r3));
            var den = l1 * l2 * l3 + l1 * Vec3d.Dot(r2, r3) + l2 * Vec3d.Dot(r3, r1) + l3 * Vec3d.Dot(r1, r2);
            var omega = 2 * Math.Atan2(num, den);
            var n = _fn[f];
            var k = _fg[f] * Vec3d.Dot(n, r1) * omega;
            gx += n.X * k;
            gy += n.Y * k;
            gz += n.Z * k;
        }
        return new Vec3d(gx, gy, gz);
    }

    public override void Mix(Action<float> mix)
    {
        mix(23f);
        mix(TetraCount); mix(_fg.Length);
        for (var i = 0; i < _tc.Length; i++)
        {
            mix((float)_tc[i].X); mix((float)_tc[i].Y); mix((float)_tc[i].Z); mix((float)_tmu[i]);
        }
    }
}

/// <summary>
/// Gravity as a sum of sources (docs/space/GRAVITY.md): point masses, mascons, tetrahedral bodies and uniform pulls. Each
/// source answers from its cheapest accurate level (one point far away, mascons nearer, the exact polyhedron close in). A
/// pure function of position, in doubles: the bubble wraps it as an <see cref="IGravityField"/> (<see cref="SummedGravity"/>)
/// and ships feel it through <see cref="BodyGravityForce"/>.
/// </summary>
public sealed class GravityModel
{
    /// <summary>A source is one point mass beyond this many of its bounding radii (about 0.4% off for a dumbbell at 20).</summary>
    public double FarFactor = 20;

    public readonly GravitySource[] Sources;

    public GravityModel(GravitySource[] sources)
    {
        Sources = sources;
    }

    /// <summary>The total GM of every source with mass.</summary>
    public double Mu
    {
        get
        {
            var m = 0.0;
            foreach (var s in Sources)
                m += s.Mu;
            return m;
        }
    }

    /// <summary>The acceleration at a position (m/s^2).</summary>
    public Vec3d Accel(Vec3d p)
    {
        var g = Vec3d.Zero;
        var s = Sources;
        for (var i = 0; i < s.Length; i++)
            g += s[i].Accel(p, FarFactor);
        return g;
    }

    /// <summary>The acceleration at full accuracy everywhere (the self-test's reference).</summary>
    public Vec3d Exact(Vec3d p)
    {
        var g = Vec3d.Zero;
        foreach (var src in Sources)
            g += src.Exact(p);
        return g;
    }

    /// <summary>The source pulling hardest at a position (a rough measure: what "height" is measured from).</summary>
    public int Dominant(Vec3d p)
    {
        var best = 0;
        var bestS = -1.0;
        var s = Sources;
        for (var i = 0; i < s.Length; i++)
        {
            var k = s[i].Strength(p);
            if (k > bestS)
            {
                bestS = k;
                best = i;
            }
        }
        return best;
    }

    public void Mix(Action<float> mix)
    {
        mix(4f); // the kind
        mix((float)FarFactor);
        mix(Sources.Length);
        foreach (var s in Sources)
            s.Mix(mix);
    }
}

/// <summary>
/// A summed gravity field on a map (the map file's <c>"gravity": { "type": "sum", "sources": [...] }</c>): up is against the
/// pull, and where the pull nearly vanishes (the middle of a stitched pair's neck) it is away from the strongest source.
/// The last query is remembered, since the mover and the frames ask for gravity, up and height at the same spot in a row.
/// </summary>
public sealed class SummedGravity : IGravityField
{
    public readonly GravityModel Model;

    /// <summary>The reference axis for the frames' north (as <see cref="PointGravity.Axis"/>).</summary>
    public Vector3 Axis { get; }

    // One-entry cache (the game thread only).
    private Vector3 _lastP = new(float.NaN);
    private Vector3 _lastG;

    public SummedGravity(GravityModel model, Vector3 axis)
    {
        Model = model;
        Axis = axis.LengthSquared() > 1e-8f ? Vector3.Normalize(axis) : -Vector3.UnitZ;
    }

    /// <summary>Below this pull (m/s^2) up comes from the strongest source instead of the field's direction.</summary>
    public const float WeakPull = 1e-3f;

    public Vector3 GravityAt(Vector3 position)
    {
        if (position == _lastP)
            return _lastG;
        var g = Model.Accel(Vec3d.From(position)).ToVector3();
        _lastP = position;
        _lastG = g;
        return g;
    }

    public Vector3 UpAt(Vector3 position)
    {
        var g = GravityAt(position);
        var len = g.Length();
        if (len > WeakPull)
            return -g / len;
        var s = Model.Sources[Model.Dominant(Vec3d.From(position))];
        if (s is UniformSource u)
            return u.G.LengthSquared > 0 ? (-u.G).Normalized().ToVector3() : Vector3.UnitY;
        var d = position - s.Center.ToVector3();
        return d.LengthSquared() > 1e-10f ? Vector3.Normalize(d) : Vector3.UnitY;
    }

    public Vector3 AxisAt(Vector3 position) => Axis;

    /// <summary>The distance from the strongest source's centre (against a uniform source: the height along its up).</summary>
    public float HeightOf(Vector3 position)
    {
        var s = Model.Sources[Model.Dominant(Vec3d.From(position))];
        if (s is UniformSource u)
            return u.G.LengthSquared > 0 ? Vector3.Dot(position, (-u.G).Normalized().ToVector3()) : position.Y;
        return Vector3.Distance(position, s.Center.ToVector3());
    }

    public void Mix(Action<float> mix)
    {
        Model.Mix(mix);
        mix(Axis.X); mix(Axis.Y); mix(Axis.Z);
    }
}

/// <summary>
/// A force hook in the bubble (docs/space/GRAVITY.md): an extra pull added to the map's gravity for players, constructs and
/// everything that falls (gravity-tech parts, anomalies, repulsors). Must not allocate and must give the same answer on
/// every peer for the same state.
/// </summary>
public interface IGravityHook
{
    /// <summary>The extra acceleration (m/s^2) at a world position.</summary>
    Vector3 AccelAt(Vector3 position);
}

/// <summary>A pull towards (or, with a negative <see cref="Mu"/>, a push away from) a point, fading to nothing at <see cref="Range"/>.</summary>
public sealed class PointHook : IGravityHook
{
    public Vector3 At;
    public float Mu;
    public float Range;
    /// <summary>Softening (m): the pull stays finite at the point itself.</summary>
    public float Soft = 1f;

    public PointHook(Vector3 at, float mu, float range)
    {
        At = at;
        Mu = mu;
        Range = range;
    }

    public Vector3 AccelAt(Vector3 p)
    {
        var d = p - At;
        var d2 = d.LengthSquared();
        if (d2 >= Range * Range)
            return Vector3.Zero;
        var r2 = d2 + Soft * Soft;
        var fade = 1f - d2 / (Range * Range); // (smooth to zero at the edge)
        return d * (-Mu * fade * fade / (r2 * MathF.Sqrt(r2)));
    }
}

/// <summary>
/// A body's lumps for ships in space (the ship integrator's force hook, <see cref="ISpaceForce"/>): a <see cref="GravityModel"/>
/// in the body's body-fixed frame (metres from its centre), less the point mass the integrator already pulls with, turned
/// into ecliptic axes as the body spins. Beyond the model's far range it adds nothing.
/// </summary>
public sealed class BodyGravityForce : ISpaceForce
{
    public readonly StarSystem System;
    public readonly int Body;
    public readonly GravityModel Model;
    private readonly double _reach2;

    public BodyGravityForce(StarSystem system, int body, GravityModel model)
    {
        System = system;
        Body = body;
        Model = model;
        var reach = 0.0;
        foreach (var s in model.Sources)
            reach = Math.Max(reach, s.Center.Length + s.Bound * model.FarFactor);
        _reach2 = reach * reach;
    }

    public Vec3d Accel(SpacePos pos, Vec3d vel, SpaceTime t)
    {
        var bodyPos = System.PositionOf(Body, t);
        var off = pos - bodyPos; // ecliptic axes, exact difference
        var r2 = off.LengthSquared;
        if (r2 > _reach2)
            return Vec3d.Zero;
        var spin = System.SpinRotation(Body, t);
        var local = spin.InverseRotate(off);
        var g = Model.Accel(local);
        var mu = System.Bodies[Body].Mu;
        var l2 = local.LengthSquared;
        g += local * (mu / (l2 * Math.Sqrt(l2))); // (less the point mass the integrator already has)
        return spin.Rotate(g);
    }
}
