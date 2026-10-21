using System.Numerics;

namespace CallOfSlop;

/// <summary>The outline of a map's playable area (the map file's "boundary" section; see Maps/README.md, "Boundaries").</summary>
public enum BoundaryShape : byte
{
    /// <summary>A rectangle (optionally with rounded corners); without a boundary section every map is this.</summary>
    Rect,

    Circle,
    Ellipse,

    /// <summary>Straight edges through the given points.</summary>
    Polygon,

    /// <summary>A closed smooth curve through the given points (centripetal Catmull-Rom): beans, blobs, lagoons.</summary>
    Smooth,
}

/// <summary>How the boundary is shown (the invisible barrier is always there).</summary>
public enum BoundaryEdgeStyle : byte
{
    /// <summary>Nothing visible: only the invisible barrier.</summary>
    None,

    /// <summary>A solid-looking wall along the outline (outside it), one segment per outline edge.</summary>
    Wall,

    /// <summary>The terrain rises steeply outside the outline (terrain maps only).</summary>
    Cliff,

    /// <summary>Wooden posts and two rails.</summary>
    Fence,

    /// <summary>A tall green hedge.</summary>
    Hedge,

    /// <summary>A line of boulders straddling the outline.</summary>
    Rocks,

    /// <summary>Props from the Props folder lined up along the outline.</summary>
    Props,
}

/// <summary>Seeded wobble added to an outline (offset along its normal), so edges aren't perfectly regular.</summary>
public sealed class BoundaryNoise
{
    /// <summary>Largest offset in metres (in or out).</summary>
    public float Amplitude;

    /// <summary>Metres between neighbouring bumps (the biggest ones; finer octaves are half as long each).</summary>
    public float Scale = 12f;

    public int Seed = 1;

    /// <summary>1 to 4 layers of detail, each half as long and half as strong as the last.</summary>
    public int Octaves = 2;
}

/// <summary>The visible edge of a boundary. Zero / empty fields mean the style's default (<see cref="MapBoundary.Defaults"/>).</summary>
public sealed class BoundaryEdge
{
    public BoundaryEdgeStyle Style = BoundaryEdgeStyle.None;
    public float Height;
    public float Thickness;
    public Vector3? Color;
    public string Material = "";

    /// <summary>Fence posts, rocks and props: metres between them along the outline.</summary>
    public float Spacing;

    /// <summary>Cliff: how far outside the outline the ground takes to rise to full height.</summary>
    public float Width;

    /// <summary>Props style: prop names or category names (repeat a name to make it more common).</summary>
    public string[] Props = [];

    public int Seed;
}

/// <summary>The "boundary" section of a map file, as written (see <see cref="MapBoundary"/> for the built outline).</summary>
public sealed class BoundarySpec
{
    public BoundaryShape Shape;

    /// <summary>Centre of a rect, circle or ellipse.</summary>
    public Vector2 Center;

    /// <summary>Rect: half size (0 = the map's halfX / halfZ) and corner rounding radius.</summary>
    public float HalfX, HalfZ, Round;

    /// <summary>Circle radius.</summary>
    public float Radius;

    /// <summary>Ellipse radii and turn (degrees, clockwise seen from above like a prop's yaw).</summary>
    public float RadiusX, RadiusZ, Rotation;

    /// <summary>Polygon corners or smooth-curve control points, [x, z], in order round the outline (either direction).</summary>
    public Vector2[] Points = [];

    public BoundaryNoise? Noise;
    public BoundaryEdge Edge = new();

    /// <summary>Flat maps: how far the floor is drawn past the outline (beyond it a darker ground fades into the fog).</summary>
    public float GroundMargin = 6f;

    /// <summary>Height of the invisible barrier above the ground (0 = the edge's height, at least 2.5 m).</summary>
    public float BarrierHeight;

    /// <summary>How far (metres) the straight outline segments may stray from the true curve. Smaller = more segments.</summary>
    public float Tolerance = 0.05f;
}

/// <summary>
/// A map's built boundary: the outline as a closed polygon (counter-clockwise in x/z, no repeated end point) plus fast
/// queries on it. The polygon is the one truth: point-in-shape, the movement clamp, the barrier colliders, the wall
/// segments and the generated content all use exactly these vertices, so they agree with each other.
///
/// <para>Everything is deterministic (fixed-order loops, no hashing containers), because the movement clamp runs in
/// client prediction and on the server.</para>
///
/// <para>Speed: a coarse grid (about 48 cells across) stores, per cell, whether its centre is inside and a lower bound
/// on its distance to the outline. A query in a cell well away from the edge is answered from the grid; only queries
/// near the edge walk the segments.</para>
/// </summary>
public sealed class MapBoundary
{
    /// <summary>Longest straight segment the outline is cut into (walls and barrier pieces follow the ground per segment).</summary>
    public const float MaxSegment = 8f;

    /// <summary>The most vertices an outline may have.</summary>
    public const int MaxVertices = 4096;

    public readonly BoundarySpec Spec;

    /// <summary>The outline, counter-clockwise (positive signed area), no repeated end point.</summary>
    public readonly Vector2[] Outline;

    /// <summary>Outward unit normal of segment i (from Outline[i] to Outline[i + 1]).</summary>
    public readonly Vector2[] Normals;

    public readonly Vector2 Min, Max;

    /// <summary>Area inside the outline (m²).</summary>
    public readonly float Area;

    /// <summary>Length of the outline (m).</summary>
    public readonly float Perimeter;

    /// <summary>A point well inside (the grid cell furthest from the edge): where the clamp falls back to.</summary>
    public readonly Vector2 Pole;

    // Acceleration grid.
    private readonly float _gx0, _gz0, _cell;
    private readonly int _gw, _gh;
    private readonly bool[] _cellInside;
    private readonly float[] _clearance; // lower bound on the distance from any point of the cell to the outline

    public int SegmentCount => Outline.Length;

    /// <summary>Centre of the outline's bounding box (thumbnails frame this).</summary>
    public Vector2 BoxCenter => (Min + Max) * 0.5f;

    /// <summary>How long the build took (ms).</summary>
    public double BuildMs { get; }

    public MapBoundary(BoundarySpec spec, float mapHalfX, float mapHalfZ)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Spec = spec;
        Outline = BuildOutline(spec, mapHalfX, mapHalfZ);
        var n = Outline.Length;
        Normals = new Vector2[n];
        Min = new Vector2(float.MaxValue);
        Max = new Vector2(float.MinValue);
        double area = 0, perim = 0;
        for (var i = 0; i < n; i++)
        {
            var a = Outline[i];
            var b = Outline[(i + 1) % n];
            var d = b - a;
            var len = d.Length();
            Normals[i] = new Vector2(d.Y, -d.X) / len;
            perim += len;
            area += (double)a.X * b.Y - (double)b.X * a.Y;
            Min = Vector2.Min(Min, a);
            Max = Vector2.Max(Max, a);
        }
        Area = (float)(area * 0.5);
        Perimeter = (float)perim;

        // Grid over the bounding box plus 4 m.
        const float pad = 4f;
        var w = Max.X - Min.X + 2 * pad;
        var h = Max.Y - Min.Y + 2 * pad;
        _cell = MathF.Max(0.5f, MathF.Max(w, h) / 48f);
        _gw = Math.Max(1, (int)MathF.Ceiling(w / _cell));
        _gh = Math.Max(1, (int)MathF.Ceiling(h / _cell));
        _gx0 = Min.X - pad;
        _gz0 = Min.Y - pad;
        _cellInside = new bool[_gw * _gh];
        _clearance = new float[_gw * _gh];
        var halfDiag = _cell * 0.70711f;
        var best = -1f;
        for (var j = 0; j < _gh; j++)
        for (var i = 0; i < _gw; i++)
        {
            var c = new Vector2(_gx0 + (i + 0.5f) * _cell, _gz0 + (j + 0.5f) * _cell);
            var k = j * _gw + i;
            _cellInside[k] = ContainsSlow(c.X, c.Y);
            var dist = MathF.Sqrt(NearestSq(c, out _, out _));
            _clearance[k] = dist - halfDiag;
            if (_cellInside[k] && dist > best)
            {
                best = dist;
                Pole = c;
            }
        }
        if (best < 0)
            Pole = Outline[0] - Normals[0] * 0.5f; // (can't happen for a valid outline: some cell centre is inside)
        BuildMs = sw.Elapsed.TotalMilliseconds;
    }

    // ---- Queries --------------------------------------------------------------------------------------------

    /// <summary>Is (x, z) inside the outline?</summary>
    public bool Contains(float x, float z)
    {
        var k = CellOf(x, z);
        if (k < 0)
            return false;
        if (_clearance[k] > 0)
            return _cellInside[k];
        return ContainsSlow(x, z);
    }

    /// <summary>Is (x, z) inside the outline and at least <paramref name="margin"/> metres from it?</summary>
    public bool Inside(float x, float z, float margin = 0f)
    {
        var k = CellOf(x, z);
        if (k < 0)
            return false;
        if (_clearance[k] >= MathF.Max(margin, 0f) && _clearance[k] > 0)
            return _cellInside[k];
        if (!ContainsSlow(x, z))
            return false;
        return margin <= 0 || NearestSq(new Vector2(x, z), out _, out _) >= margin * margin;
    }

    /// <summary>Distance to the outline: negative inside, positive outside.</summary>
    public float SignedDistance(float x, float z)
    {
        var k = CellOf(x, z);
        var d = MathF.Sqrt(NearestSq(new Vector2(x, z), out _, out _));
        var inside = k >= 0 && (_clearance[k] > 0 ? _cellInside[k] : ContainsSlow(x, z));
        return inside ? -d : d;
    }

    /// <summary>
    /// A lower bound on the signed distance's size at (x, z) that costs one lookup: points in a cell well away from the
    /// edge get that cell's clearance, others 0 (unknown). Used to skip exact work far from the edge.
    /// </summary>
    public float QuickClearance(float x, float z)
    {
        var k = CellOf(x, z);
        if (k < 0)
        {
            // Outside the grid: at least the distance to the grid's box (which is 4 m past the outline everywhere).
            var dx = MathF.Max(MathF.Max(_gx0 - x, x - (_gx0 + _gw * _cell)), 0);
            var dz = MathF.Max(MathF.Max(_gz0 - z, z - (_gz0 + _gh * _cell)), 0);
            return MathF.Sqrt(dx * dx + dz * dz) + 4f;
        }
        return MathF.Max(_clearance[k], 0);
    }

    /// <summary>The closest point of the outline to <paramref name="p"/>, its segment and the outward normal there.</summary>
    public Vector2 Nearest(Vector2 p, out int segment, out Vector2 outward)
    {
        NearestSq(p, out segment, out var t);
        var a = Outline[segment];
        var b = Outline[(segment + 1) % Outline.Length];
        var q = a + (b - a) * t;
        outward = Normals[segment];
        if (t <= 0f || t >= 1f)
        {
            // At a vertex: average the two segments' normals (a vertex normal), unless the point gives a clear direction.
            var v = t <= 0f ? segment : (segment + 1) % Outline.Length;
            var prev = (v - 1 + Outline.Length) % Outline.Length;
            var vn = Normals[prev] + Normals[v];
            outward = vn.LengthSquared() > 1e-8f ? Vector2.Normalize(vn) : Normals[v];
        }
        return q;
    }

    /// <summary>
    /// The point closest to <paramref name="p"/> that is inside the outline and at least <paramref name="inset"/> from it
    /// (p itself if it already is). Deterministic; used by the movement clamp, grenades and debris.
    /// </summary>
    public Vector2 Clamp(Vector2 p, float inset)
    {
        for (var iter = 0; iter < 8; iter++)
        {
            if (Inside(p.X, p.Y, inset))
                return p;
            var q = Nearest(p, out _, out var outward);
            var off = p - q;
            var d = off.Length();
            var inside = Contains(p.X, p.Y);
            Vector2 dirIn;
            if (d > 1e-5f)
                dirIn = inside ? off / d : -off / d;
            else
                dirIn = -outward;
            // A point outside right next to a sharp inward corner can give a direction pointing out: use the normal then.
            if (Vector2.Dot(dirIn, outward) > 0.2f)
                dirIn = -outward;
            p = q + dirIn * (inset + 0.001f);
        }
        if (Inside(p.X, p.Y, inset))
            return p;
        // Fallback (very tight corners): walk towards the pole until there's room.
        var to = Pole - p;
        var steps = (int)MathF.Ceiling(to.Length() / 0.1f);
        for (var s = 1; s <= steps; s++)
        {
            var c = p + to * (s / (float)steps);
            if (Inside(c.X, c.Y, inset))
                return c;
        }
        return Pole;
    }

    /// <summary>Keeps a player's feet inside (horizontal only; y unchanged).</summary>
    public Vector3 ClampFeet(Vector3 feet, float inset)
    {
        if (Inside(feet.X, feet.Z, inset))
            return feet;
        var c = Clamp(new Vector2(feet.X, feet.Z), inset);
        return new Vector3(c.X, feet.Y, c.Y);
    }

    /// <summary>
    /// Keeps a flying object inside: if it's outside (or closer than <paramref name="inset"/>), it's put back and its
    /// velocity bounces off the outline. Returns true if it bounced.
    /// </summary>
    public bool KeepInside(ref Vector3 pos, ref Vector3 vel, float inset, float restitution)
    {
        if (Inside(pos.X, pos.Z, inset))
            return false;
        Nearest(new Vector2(pos.X, pos.Z), out _, out var n);
        var c = Clamp(new Vector2(pos.X, pos.Z), inset);
        pos.X = c.X;
        pos.Z = c.Y;
        var vn = vel.X * n.X + vel.Z * n.Y;
        if (vn > 0)
        {
            vel.X -= (1 + restitution) * vn * n.X;
            vel.Z -= (1 + restitution) * vn * n.Y;
        }
        return true;
    }

    /// <summary>Distance from <paramref name="origin"/> along <paramref name="dir"/> (unit) to the first crossing of the outline, or +inf.</summary>
    public float RayExit(Vector2 origin, Vector2 dir)
    {
        var best = float.PositiveInfinity;
        var n = Outline.Length;
        for (var i = 0; i < n; i++)
        {
            var a = Outline[i];
            var e = Outline[(i + 1) % n] - a;
            var den = dir.X * e.Y - dir.Y * e.X;
            if (MathF.Abs(den) < 1e-9f)
                continue;
            var ao = a - origin;
            var t = (ao.X * e.Y - ao.Y * e.X) / den;
            var u = (ao.X * dir.Y - ao.Y * dir.X) / den;
            if (t > 1e-4f && u >= 0 && u <= 1 && t < best)
                best = t;
        }
        return best;
    }

    /// <summary>Do any two non-neighbouring outline segments cross? (Sweep over x; exact for the float vertices.)</summary>
    public static bool SelfIntersects(IReadOnlyList<Vector2> pts, out int segA, out int segB)
    {
        var n = pts.Count;
        var order = new int[n];
        var lo = new float[n];
        for (var i = 0; i < n; i++)
        {
            order[i] = i;
            lo[i] = MathF.Min(pts[i].X, pts[(i + 1) % n].X);
        }
        Array.Sort(lo, order); // by left end (ties don't matter for a yes/no answer)
        var active = new List<int>();
        foreach (var i in order)
        {
            var minX = MathF.Min(pts[i].X, pts[(i + 1) % n].X);
            active.RemoveAll(k => MathF.Max(pts[k].X, pts[(k + 1) % n].X) < minX);
            foreach (var k in active)
            {
                if (Math.Abs(i - k) <= 1 || Math.Abs(i - k) == n - 1)
                    continue; // neighbours share a vertex
                if (SegmentsCross(pts[i], pts[(i + 1) % n], pts[k], pts[(k + 1) % n]))
                {
                    segA = Math.Min(i, k);
                    segB = Math.Max(i, k);
                    return true;
                }
            }
            active.Add(i);
        }
        segA = segB = -1;
        return false;
    }

    private static bool SegmentsCross(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        static double Cross(Vector2 o, Vector2 p, Vector2 q) => ((double)p.X - o.X) * ((double)q.Y - o.Y) - ((double)p.Y - o.Y) * ((double)q.X - o.X);
        var d1 = Cross(c, d, a);
        var d2 = Cross(c, d, b);
        var d3 = Cross(a, b, c);
        var d4 = Cross(a, b, d);
        if (((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0)))
            return true;
        // Touching or overlapping counts too (a pinched outline).
        static bool On(Vector2 p, Vector2 q, Vector2 r) =>
            MathF.Min(p.X, q.X) <= r.X && r.X <= MathF.Max(p.X, q.X) && MathF.Min(p.Y, q.Y) <= r.Y && r.Y <= MathF.Max(p.Y, q.Y);
        return (d1 == 0 && On(c, d, a)) || (d2 == 0 && On(c, d, b)) || (d3 == 0 && On(a, b, c)) || (d4 == 0 && On(a, b, d));
    }

    /// <summary>Style defaults: height, thickness, spacing, colour and material for an edge left at 0 / empty.</summary>
    public static (float Height, float Thickness, float Spacing, Vector3 Color, string Material) Defaults(BoundaryEdgeStyle style) => style switch
    {
        BoundaryEdgeStyle.Wall => (4f, 0.8f, 0f, new Vector3(0.55f, 0.55f, 0.57f), "Concrete"),
        BoundaryEdgeStyle.Fence => (1.4f, 0.14f, 2.5f, new Vector3(0.45f, 0.33f, 0.22f), "Wood"),
        BoundaryEdgeStyle.Hedge => (2.4f, 1.4f, 0f, new Vector3(0.22f, 0.36f, 0.15f), "Dirt"),
        BoundaryEdgeStyle.Rocks => (2.6f, 0f, 2.4f, new Vector3(0.47f, 0.45f, 0.42f), "Concrete"),
        BoundaryEdgeStyle.Props => (0f, 0f, 3f, Vector3.One, ""),
        BoundaryEdgeStyle.Cliff => (10f, 0f, 0f, Vector3.One, ""),
        _ => (0f, 0f, 0f, Vector3.One, ""),
    };

    public float EdgeHeight => Spec.Edge.Height > 0 ? Spec.Edge.Height : Defaults(Spec.Edge.Style).Height;
    public float EdgeThickness => Spec.Edge.Thickness > 0 ? Spec.Edge.Thickness : Defaults(Spec.Edge.Style).Thickness;
    public float EdgeSpacing => Spec.Edge.Spacing > 0 ? Spec.Edge.Spacing : Defaults(Spec.Edge.Style).Spacing;
    public Vector3 EdgeColor => Spec.Edge.Color ?? Defaults(Spec.Edge.Style).Color;
    public string EdgeMaterial => Spec.Edge.Material.Length > 0 ? Spec.Edge.Material : Defaults(Spec.Edge.Style).Material;

    /// <summary>Cliff: horizontal width of the rise outside the outline.</summary>
    public float CliffWidth => Spec.Edge.Width > 0 ? Spec.Edge.Width : 5f;

    /// <summary>Height of the invisible barrier above the highest ground along each segment.</summary>
    public float BarrierHeight => Spec.BarrierHeight > 0 ? Spec.BarrierHeight
        : Spec.Edge.Style is BoundaryEdgeStyle.Cliff or BoundaryEdgeStyle.None or BoundaryEdgeStyle.Props ? 2.5f : MathF.Max(2.5f, EdgeHeight);

    /// <summary>Thickness of the barrier pieces (outwards from the outline).</summary>
    public float BarrierThickness => MathF.Max(1f, Spec.Edge.Style == BoundaryEdgeStyle.Wall ? EdgeThickness : 0f);

    // ---- Internals ----------------------------------------------------------------------------------------------

    private int CellOf(float x, float z)
    {
        var fi = (x - _gx0) / _cell;
        var fj = (z - _gz0) / _cell;
        if (!(fi >= 0 && fj >= 0 && fi < _gw && fj < _gh))
            return -1;
        return (int)fj * _gw + (int)fi;
    }

    /// <summary>Crossing-number point-in-polygon test (half-open rule, so shared vertices count once).</summary>
    private bool ContainsSlow(float x, float z)
    {
        var inside = false;
        var pts = Outline;
        var n = pts.Length;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            var a = pts[i];
            var b = pts[j];
            if ((a.Y > z) != (b.Y > z))
            {
                var xi = (b.X - a.X) * (z - a.Y) / (b.Y - a.Y) + a.X;
                if (x < xi)
                    inside = !inside;
            }
        }
        return inside;
    }

    /// <summary>Squared distance from p to the outline, the closest segment (first in order on ties) and where along it (0..1).</summary>
    private float NearestSq(Vector2 p, out int segment, out float t)
    {
        var best = float.MaxValue;
        segment = 0;
        t = 0;
        var pts = Outline;
        var n = pts.Length;
        for (var i = 0; i < n; i++)
        {
            var a = pts[i];
            var e = pts[i + 1 == n ? 0 : i + 1] - a;
            var len2 = e.LengthSquared();
            var u = len2 > 0 ? Math.Clamp(Vector2.Dot(p - a, e) / len2, 0f, 1f) : 0f;
            var q = a + e * u;
            var d2 = Vector2.DistanceSquared(p, q);
            if (d2 < best)
            {
                best = d2;
                segment = i;
                t = u;
            }
        }
        return best;
    }

    // ---- Building the outline ------------------------------------------------------------------------------------

    /// <summary>
    /// The outline for a spec: the shape sampled densely, noise added, simplified to within <see cref="BoundarySpec.Tolerance"/>
    /// of the dense curve (so curvy parts get more segments than straight ones), long segments split, made counter-clockwise
    /// and checked. Throws <see cref="InvalidOperationException"/> with a readable message for a bad shape.
    /// </summary>
    public static Vector2[] BuildOutline(BoundarySpec spec, float mapHalfX, float mapHalfZ)
    {
        var tol = Math.Clamp(spec.Tolerance, 0.005f, 1f);
        var pts = BaseCurve(spec, mapHalfX, mapHalfZ);
        if (pts.Count < 3)
            throw new InvalidOperationException("the boundary needs at least 3 points");
        if (SignedArea(pts) < 0)
            pts.Reverse();
        if (spec.Noise is { Amplitude: > 0 } noise)
            pts = AddNoise(pts, noise);
        pts = Simplify(pts, tol);
        pts = SplitLong(pts, MaxSegment);
        RemoveDuplicates(pts);
        if (pts.Count < 3)
            throw new InvalidOperationException("the boundary collapsed to fewer than 3 points");
        if (pts.Count > MaxVertices)
            throw new InvalidOperationException($"the boundary has {pts.Count} segments (the limit is {MaxVertices}): raise \"tolerance\" or use less noise");
        var area = SignedArea(pts);
        if (area < 0)
        {
            pts.Reverse();
            area = -area;
        }
        if (area < 100)
            throw new InvalidOperationException($"the boundary encloses only {area:0} square metres (at least 100)");
        if (SelfIntersects(pts, out var sa, out var sb))
            throw new InvalidOperationException($"the boundary crosses itself (near ({pts[sa].X:0.#}, {pts[sa].Y:0.#}) and ({pts[sb].X:0.#}, {pts[sb].Y:0.#}))"
                + (spec.Noise != null ? ": lower the noise amplitude or raise its scale" : ": check the order of the points"));
        foreach (var p in pts)
            if (!float.IsFinite(p.X + p.Y) || MathF.Abs(p.X) > 500 || MathF.Abs(p.Y) > 500)
                throw new InvalidOperationException("the boundary must stay within 500 m of the map's centre");
        return pts.ToArray();
    }

    private static double SignedArea(List<Vector2> pts)
    {
        double a = 0;
        for (var i = 0; i < pts.Count; i++)
        {
            var p = pts[i];
            var q = pts[(i + 1) % pts.Count];
            a += (double)p.X * q.Y - (double)q.X * p.Y;
        }
        return a * 0.5;
    }

    /// <summary>The shape before noise: exact corners for polygons and rects, dense samples (about 0.25 m apart) on curves.</summary>
    private static List<Vector2> BaseCurve(BoundarySpec s, float mapHalfX, float mapHalfZ)
    {
        const double step = 0.25; // metres between samples on curves (simplified afterwards)
        var pts = new List<Vector2>();
        switch (s.Shape)
        {
            case BoundaryShape.Rect:
            {
                var hx = s.HalfX > 0 ? s.HalfX : mapHalfX;
                var hz = s.HalfZ > 0 ? s.HalfZ : mapHalfZ;
                if (hx < 5 || hz < 5)
                    throw new InvalidOperationException("a rect boundary needs halfX and halfZ of at least 5");
                var r = Math.Clamp(s.Round, 0, MathF.Min(hx, hz) - 0.01f);
                double cx = s.Center.X, cz = s.Center.Y;
                if (r <= 0)
                {
                    pts.Add(new Vector2((float)(cx - hx), (float)(cz - hz)));
                    pts.Add(new Vector2((float)(cx + hx), (float)(cz - hz)));
                    pts.Add(new Vector2((float)(cx + hx), (float)(cz + hz)));
                    pts.Add(new Vector2((float)(cx - hx), (float)(cz + hz)));
                    break;
                }
                // Rounded corners: a quarter circle at each corner, centres inset by r.
                var arcN = Math.Max(4, (int)Math.Ceiling(r * Math.PI / 2 / step));
                (double X, double Z, double A0)[] corners = [(cx + hx - r, cz - hz + r, -Math.PI / 2), (cx + hx - r, cz + hz - r, 0), (cx - hx + r, cz + hz - r, Math.PI / 2), (cx - hx + r, cz - hz + r, Math.PI)];
                foreach (var (x, z, a0) in corners)
                    for (var k = 0; k <= arcN; k++)
                    {
                        var a = a0 + k * (Math.PI / 2) / arcN;
                        pts.Add(new Vector2((float)(x + r * Math.Cos(a)), (float)(z + r * Math.Sin(a))));
                    }
                break;
            }
            case BoundaryShape.Circle:
            {
                if (s.Radius < 6)
                    throw new InvalidOperationException("a circle boundary needs a radius of at least 6");
                var n = Math.Clamp((int)Math.Ceiling(2 * Math.PI * s.Radius / step), 64, 16384);
                for (var k = 0; k < n; k++)
                {
                    var a = k * 2 * Math.PI / n;
                    pts.Add(new Vector2((float)(s.Center.X + s.Radius * Math.Cos(a)), (float)(s.Center.Y + s.Radius * Math.Sin(a))));
                }
                break;
            }
            case BoundaryShape.Ellipse:
            {
                if (s.RadiusX < 6 || s.RadiusZ < 6)
                    throw new InvalidOperationException("an ellipse boundary needs radiusX and radiusZ of at least 6");
                var n = Math.Clamp((int)Math.Ceiling(2 * Math.PI * Math.Max(s.RadiusX, s.RadiusZ) / step), 64, 16384);
                var rot = s.Rotation * Math.PI / 180;
                double cr = Math.Cos(rot), sr = Math.Sin(rot);
                for (var k = 0; k < n; k++)
                {
                    var a = k * 2 * Math.PI / n;
                    double lx = s.RadiusX * Math.Cos(a), lz = s.RadiusZ * Math.Sin(a);
                    // Same turn as a prop's yaw: local x goes to (cos, -sin).
                    pts.Add(new Vector2((float)(s.Center.X + lx * cr + lz * sr), (float)(s.Center.Y - lx * sr + lz * cr)));
                }
                break;
            }
            case BoundaryShape.Polygon:
                if (s.Points.Length < 3)
                    throw new InvalidOperationException("a polygon boundary needs at least 3 points");
                pts.AddRange(s.Points);
                break;
            case BoundaryShape.Smooth:
                if (s.Points.Length < 3)
                    throw new InvalidOperationException("a smooth boundary needs at least 3 points");
                pts.AddRange(CatmullRom(s.Points, step));
                break;
        }
        return pts;
    }

    /// <summary>A closed centripetal Catmull-Rom curve through the points (no cusps or loops for any spacing of points).</summary>
    public static List<Vector2> CatmullRom(IReadOnlyList<Vector2> cp, double step)
    {
        var n = cp.Count;
        var outPts = new List<Vector2>();
        for (var i = 0; i < n; i++)
        {
            var p0 = cp[(i - 1 + n) % n];
            var p1 = cp[i];
            var p2 = cp[(i + 1) % n];
            var p3 = cp[(i + 2) % n];
            double Knot(Vector2 a, Vector2 b)
            {
                var d = Math.Sqrt(((double)b.X - a.X) * ((double)b.X - a.X) + ((double)b.Y - a.Y) * ((double)b.Y - a.Y));
                if (d < 1e-3)
                    throw new InvalidOperationException($"two neighbouring boundary points are at the same place ({a.X:0.#}, {a.Y:0.#})");
                return Math.Sqrt(d); // centripetal: alpha = 0.5
            }
            double t0 = 0, t1 = t0 + Knot(p0, p1), t2 = t1 + Knot(p1, p2), t3 = t2 + Knot(p2, p3);
            var spanLen = Vector2.Distance(p1, p2);
            var steps = Math.Clamp((int)Math.Ceiling(spanLen / step), 4, 2000);
            for (var k = 0; k < steps; k++) // t2 itself is the next span's first sample
            {
                var t = t1 + (t2 - t1) * k / steps;
                (double X, double Z) L((double X, double Z) a, (double X, double Z) b, double ta, double tb) =>
                    ((tb - t) / (tb - ta) * a.X + (t - ta) / (tb - ta) * b.X, (tb - t) / (tb - ta) * a.Z + (t - ta) / (tb - ta) * b.Z);
                (double, double) P(Vector2 v) => (v.X, v.Y);
                var a1 = L(P(p0), P(p1), t0, t1);
                var a2 = L(P(p1), P(p2), t1, t2);
                var a3 = L(P(p2), P(p3), t2, t3);
                var b1 = L(a1, a2, t0, t2);
                var b2 = L(a2, a3, t1, t3);
                var c = L(b1, b2, t1, t2);
                outPts.Add(new Vector2((float)c.X, (float)c.Z));
            }
        }
        return outPts;
    }

    /// <summary>
    /// Offsets the (counter-clockwise) outline along its normals by periodic fractal value noise of the arc length, so
    /// the wobble joins up seamlessly where the outline closes. Densifies first so every bump is drawn smoothly.
    /// </summary>
    private static List<Vector2> AddNoise(List<Vector2> pts, BoundaryNoise noise)
    {
        var scale = Math.Clamp(noise.Scale, 1f, 200f);
        var octaves = Math.Clamp(noise.Octaves, 1, 4);
        var h = MathF.Min(0.5f, scale / (8f * (1 << (octaves - 1))));
        h = MathF.Max(h, 0.1f);
        // Densify (keeping the original vertices).
        var dense = new List<Vector2>();
        for (var i = 0; i < pts.Count; i++)
        {
            var a = pts[i];
            var b = pts[(i + 1) % pts.Count];
            var k = Math.Max(1, (int)MathF.Ceiling(Vector2.Distance(a, b) / h));
            for (var s = 0; s < k; s++)
                dense.Add(Vector2.Lerp(a, b, s / (float)k));
        }
        var n = dense.Count;
        var arc = new double[n + 1];
        for (var i = 0; i < n; i++)
            arc[i + 1] = arc[i] + Vector2.Distance(dense[i], dense[(i + 1) % n]);
        var total = arc[n];
        var outPts = new List<Vector2>(n);
        for (var i = 0; i < n; i++)
        {
            var prev = dense[(i - 1 + n) % n];
            var next = dense[(i + 1) % n];
            var d = next - prev;
            var normal = d.LengthSquared() > 1e-12f ? Vector2.Normalize(new Vector2(d.Y, -d.X)) : Vector2.UnitX;
            double sum = 0, norm = 0, amp = 1;
            for (var o = 0; o < octaves; o++)
            {
                var lattice = Math.Max(3, (int)Math.Round(total / (scale / (1 << o))));
                var u = arc[i] / total * lattice;
                var i0 = (int)Math.Floor(u);
                var f = u - i0;
                f = f * f * (3 - 2 * f);
                var v0 = TerrainGen.Hash(i0 % lattice, o, noise.Seed);
                var v1 = TerrainGen.Hash((i0 + 1) % lattice, o, noise.Seed);
                sum += amp * (v0 + (v1 - v0) * f);
                norm += amp;
                amp *= 0.5;
            }
            var off = (float)(noise.Amplitude * sum / norm);
            outPts.Add(dense[i] + normal * off);
        }
        return outPts;
    }

    /// <summary>Douglas-Peucker on a closed polygon: keeps the fewest vertices that stay within <paramref name="tol"/> of every original one.</summary>
    private static List<Vector2> Simplify(List<Vector2> pts, float tol)
    {
        var n = pts.Count;
        if (n <= 4)
            return pts;
        // Split the loop at vertex 0 and the vertex furthest from it.
        var far = 0;
        var farD = -1f;
        for (var i = 1; i < n; i++)
        {
            var d = Vector2.DistanceSquared(pts[0], pts[i]);
            if (d > farD)
            {
                farD = d;
                far = i;
            }
        }
        var keep = new bool[n];
        keep[0] = keep[far] = true;
        var stack = new Stack<(int A, int B)>();
        stack.Push((0, far));
        stack.Push((far, n));
        var tol2 = (double)tol * tol;
        while (stack.Count > 0)
        {
            var (a, b) = stack.Pop();
            if (b - a < 2)
                continue;
            var pa = pts[a];
            var pb = pts[b % n];
            var worst = -1.0;
            var at = -1;
            for (var i = a + 1; i < b; i++)
            {
                var d = SegDist2(pts[i], pa, pb);
                if (d > worst)
                {
                    worst = d;
                    at = i;
                }
            }
            if (worst > tol2)
            {
                keep[at] = true;
                stack.Push((a, at));
                stack.Push((at, b));
            }
        }
        var result = new List<Vector2>();
        for (var i = 0; i < n; i++)
            if (keep[i])
                result.Add(pts[i]);
        return result;
    }

    private static double SegDist2(Vector2 p, Vector2 a, Vector2 b)
    {
        double ex = b.X - a.X, ez = b.Y - a.Y, px = p.X - a.X, pz = p.Y - a.Y;
        var len2 = ex * ex + ez * ez;
        var t = len2 > 0 ? Math.Clamp((px * ex + pz * ez) / len2, 0, 1) : 0;
        double dx = px - ex * t, dz = pz - ez * t;
        return dx * dx + dz * dz;
    }

    private static List<Vector2> SplitLong(List<Vector2> pts, float max)
    {
        var result = new List<Vector2>(pts.Count);
        for (var i = 0; i < pts.Count; i++)
        {
            var a = pts[i];
            var b = pts[(i + 1) % pts.Count];
            var k = Math.Max(1, (int)MathF.Ceiling(Vector2.Distance(a, b) / max));
            for (var s = 0; s < k; s++)
                result.Add(s == 0 ? a : Vector2.Lerp(a, b, s / (float)k));
        }
        return result;
    }

    private static void RemoveDuplicates(List<Vector2> pts)
    {
        for (var i = pts.Count - 1; i >= 0 && pts.Count > 0; i--)
            if (Vector2.DistanceSquared(pts[i], pts[(i + 1) % pts.Count]) < 1e-6f)
                pts.RemoveAt(i);
    }
}
