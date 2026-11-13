using System.Numerics;
using System.Runtime.InteropServices;

namespace CallOfSlop;

/// <summary>One vertex of a city's meshes (40 bytes): position from its chunk's origin, normal, the face's own metres (along
/// a facade and up it, or along a street and across it), the colour and four bytes the shader reads (style, seed and two
/// numbers whose meaning depends on the style).</summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct CityVertex
{
    public Vector3 Position;
    public Vector3 Normal;
    public Vector2 Uv;
    public uint Color;
    public uint Params;

    public const int SizeInBytes = 40;
}

/// <summary>A piece of the city drawn together and culled together: its vertices start at its own origin.</summary>
public struct CityChunk
{
    public Vector3 Origin;
    public Vector3 Centre;
    public float Radius;

    /// <summary>Its indices: the first <see cref="Main"/> are drawn at any distance, the <see cref="Detail"/> after them only near.</summary>
    public int First, Main, Detail;
}

/// <summary>
/// The meshes and colliders of a <see cref="CityPlan"/>: buildings as upright prisms in their own frames, streets and
/// pavements draped on the ground, kerbs, lamps. Made once per map on the PC that plays it (a few tens of milliseconds).
/// </summary>
public sealed class CityGeometry
{
    public const float ChunkSize = 128f;

    /// <summary>A pavement's and a block's top over the carriageway (m), and the carriageway's over the ground.</summary>
    public const float Kerb = 0.15f, RoadLift = 0.03f;

    /// <summary>Lamps along a street: their spacing is about this, the first this far from the crossing (m).</summary>
    public const float LampSpacing = 24f, LampStart = 6f;

    public CityVertex[] Vertices = [];
    public uint[] Indices = [];
    public CityChunk[] Chunks = [];
    public List<IMeshCollider> Colliders = new();
    public int Triangles, CollisionTriangles;

    private sealed class Chunk
    {
        public Vector3 Origin;
        public readonly List<CityVertex> V = new();
        public readonly List<uint> Main = new(), Detail = new();
        public readonly List<Vector3> CP = new();
        public readonly List<int> CI = new();
        public Vector3 Min = new(float.MaxValue), Max = new(float.MinValue);
    }

    private readonly Dictionary<(int, int), Chunk> _chunks = new();
    private readonly CityPlan _plan;

    private CityGeometry(CityPlan plan) => _plan = plan;

    public static CityGeometry Make(CityPlan plan)
    {
        var g = new CityGeometry(plan);
        g.Build();
        return g;
    }

    private Chunk At(Vector2 p)
    {
        var key = ((int)MathF.Floor(p.X / ChunkSize), (int)MathF.Floor(p.Y / ChunkSize));
        if (!_chunks.TryGetValue(key, out var c))
        {
            var mid = new Vector2((key.Item1 + 0.5f) * ChunkSize, (key.Item2 + 0.5f) * ChunkSize);
            _chunks[key] = c = new Chunk { Origin = _plan.Ground(mid) };
        }
        return c;
    }

    /// <summary>The four parameter bytes.</summary>
    public static uint Pack(CityStyle style, uint seed, float a, float b, bool flag = false) =>
        (uint)style | (flag ? 0x80u : 0) | (seed & 255) << 8 | (uint)Math.Clamp((int)MathF.Round(a), 0, 255) << 16 | (uint)Math.Clamp((int)MathF.Round(b), 0, 255) << 24;

    // ---- Writing faces -----------------------------------------------------------------------------------------------

    private static void Tri(Chunk c, bool detail, uint a, uint b, uint d)
    {
        var l = detail ? c.Detail : c.Main;
        l.Add(a);
        l.Add(b);
        l.Add(d);
    }

    private uint Vert(Chunk c, Vector3 world, Vector3 n, Vector2 uv, uint color, uint par)
    {
        c.Min = Vector3.Min(c.Min, world);
        c.Max = Vector3.Max(c.Max, world);
        c.V.Add(new CityVertex { Position = world - c.Origin, Normal = n, Uv = uv, Color = color, Params = par });
        return (uint)(c.V.Count - 1);
    }

    /// <summary>A quad a b c d (in order round it), wound so its front faces along <paramref name="n"/>.</summary>
    private void Quad(Chunk c, bool detail, Vector3 a, Vector3 b, Vector3 cc, Vector3 d, Vector3 na, Vector3 nb, Vector3 nc, Vector3 nd,
        Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud, uint color, uint par, Vector3 facing)
    {
        var flip = Vector3.Dot(Vector3.Cross(b - a, cc - a), facing) < 0;
        uint i0 = Vert(c, a, na, ua, color, par), i1 = Vert(c, b, nb, ub, color, par), i2 = Vert(c, cc, nc, uc, color, par), i3 = Vert(c, d, nd, ud, color, par);
        if (flip)
        {
            Tri(c, detail, i0, i2, i1);
            Tri(c, detail, i0, i3, i2);
        }
        else
        {
            Tri(c, detail, i0, i1, i2);
            Tri(c, detail, i0, i2, i3);
        }
    }

    private static void ColTri(Chunk c, Vector3 a, Vector3 b, Vector3 d, Vector3 facing)
    {
        var i = c.CP.Count;
        c.CP.Add(a);
        if (Vector3.Dot(Vector3.Cross(b - a, d - a), facing) < 0)
        {
            c.CP.Add(d);
            c.CP.Add(b);
        }
        else
        {
            c.CP.Add(b);
            c.CP.Add(d);
        }
        c.CI.Add(i);
        c.CI.Add(i + 1);
        c.CI.Add(i + 2);
    }

    // ---- Buildings ---------------------------------------------------------------------------------------------------

    private static int Outline(in CityMass m, Span<Vector2> pts)
    {
        var h = m.Half;
        switch (m.Shape)
        {
            case 1:
            {
                var c = MathF.Min(m.Cut, MathF.Min(h.X, h.Y) * 0.9f);
                ReadOnlySpan<Vector2> p8 =
                [
                    new(h.X - c, h.Y), new(h.X, h.Y - c), new(h.X, -h.Y + c), new(h.X - c, -h.Y),
                    new(-h.X + c, -h.Y), new(-h.X, -h.Y + c), new(-h.X, h.Y - c), new(-h.X + c, h.Y),
                ];
                for (var i = 0; i < 8; i++)
                    pts[i] = m.At + p8[i];
                return 8;
            }
            case 2:
                for (var i = 0; i < 16; i++)
                {
                    var (s, co) = MathF.SinCos(-i * MathF.Tau / 16);
                    pts[i] = m.At + new Vector2(co * h.X, s * h.Y);
                }
                return 16;
            default:
                pts[0] = m.At + new Vector2(h.X, h.Y);
                pts[1] = m.At + new Vector2(h.X, -h.Y);
                pts[2] = m.At + new Vector2(-h.X, -h.Y);
                pts[3] = m.At + new Vector2(-h.X, h.Y);
                return 4;
        }
    }

    private static readonly uint RoofColor = CityPlanColours.Rgb(0x54575b);

    private void Mass(in CityMass m)
    {
        ref readonly var b = ref _plan.Buildings[m.Building];
        var f = _plan.FrameOf(b, 0.3f);
        var c = At(b.At);
        var up = new Vector3(f.M21, f.M22, f.M23);
        Vector3 P(Vector2 q, float y) => Vector3.Transform(new Vector3(q.X, y, q.Y), f);
        Vector3 Dir(Vector2 q) => Vector3.Normalize(Vector3.TransformNormal(new Vector3(q.X, 0, q.Y), f));
        Span<Vector2> pts = stackalloc Vector2[16];
        var n = Outline(m, pts);
        var style = (CityStyle)m.Style;
        var facade = style is CityStyle.Glass or CityStyle.Grid or CityStyle.Brick or CityStyle.Ribbon;
        var par = Pack(style, b.Seed, b.FloorHeight * 20, (facade ? b.Lit : 0.5f) * 255, facade && b.Shops && m.Bottom <= 0.01f);
        var round = m.Shape == 2;
        var u = 0f;
        for (var i = 0; i < n; i++)
        {
            Vector2 a = pts[i], e = pts[(i + 1) % n];
            var len = Vector2.Distance(a, e);
            var mid = (a + e) * 0.5f - m.At;
            var edge = e - a;
            var outN = Dir(new Vector2(edge.Y, -edge.X));
            if (Vector3.Dot(outN, Dir(mid)) < 0)
                outN = -outN;
            Vector3 na = round ? Dir(a - m.At) : outN, ne = round ? Dir(e - m.At) : outN;
            Quad(c, m.Detail, P(a, m.Bottom), P(e, m.Bottom), P(e, m.Top), P(a, m.Top), na, ne, ne, na,
                new Vector2(u, m.Bottom), new Vector2(u + len, m.Bottom), new Vector2(u + len, m.Top), new Vector2(u, m.Top), m.Color, par, outN);
            if (m.Solid)
            {
                ColTri(c, P(a, m.Bottom), P(e, m.Bottom), P(e, m.Top), outN);
                ColTri(c, P(a, m.Bottom), P(e, m.Top), P(a, m.Top), outN);
            }
            u += len;
        }
        // The top: a fan round its middle (roofs of facade masses are roofing; a plain mass keeps its own colour).
        var topPar = facade ? Pack(CityStyle.Roof, b.Seed, 0, 0) : par;
        var topColor = facade ? RoofColor : m.Color;
        var centre = Vert(c, P(m.At, m.Top), up, m.At, topColor, topPar);
        var first = c.V.Count;
        for (var i = 0; i < n; i++)
            Vert(c, P(pts[i], m.Top), up, pts[i], topColor, topPar);
        for (var i = 0; i < n; i++)
        {
            uint i0 = (uint)(first + i), i1 = (uint)(first + (i + 1) % n);
            var flip = Vector3.Dot(Vector3.Cross(P(pts[i], 0) - P(m.At, 0), P(pts[(i + 1) % n], 0) - P(m.At, 0)), up) < 0;
            if (flip)
                Tri(c, m.Detail, centre, i1, i0);
            else
                Tri(c, m.Detail, centre, i0, i1);
            if (m.Solid)
            {
                ColTri(c, P(m.At, m.Top), P(pts[i], m.Top), P(pts[(i + 1) % n], m.Top), up);
                ColTri(c, P(m.At, m.Bottom), P(pts[i], m.Bottom), P(pts[(i + 1) % n], m.Bottom), -up);
            }
        }
    }

    // ---- Ground: streets, crossings, slabs ---------------------------------------------------------------------------

    /// <summary>The sheet's x and z axes at a point of the plan, in the ground's plane there.</summary>
    private (Vector3 X, Vector3 Z, Vector3 Up) Axes(Vector2 p)
    {
        var up = _plan.DirOf(p);
        var x = Vector3.Normalize(_plan.East - up * Vector3.Dot(_plan.East, up));
        return (x, Vector3.Cross(x, up), up);
    }

    /// <summary>A flat piece of ground lying on the curve: a rectangle of the plan at <paramref name="y"/> m, cut every few metres.
    /// <paramref name="uv"/> maps a plan point to the face's own metres.</summary>
    private void Sheet(Chunk c, Vector2 min, Vector2 max, float y, uint color, uint par, Func<Vector2, Vector2> uv, bool detail = false)
    {
        var nx = Math.Max(1, (int)MathF.Ceiling((max.X - min.X) / 8f));
        var nz = Math.Max(1, (int)MathF.Ceiling((max.Y - min.Y) / 8f));
        var first = c.V.Count;
        for (var j = 0; j <= nz; j++)
            for (var i = 0; i <= nx; i++)
            {
                var p = new Vector2(min.X + (max.X - min.X) * i / nx, min.Y + (max.Y - min.Y) * j / nz);
                Vert(c, _plan.Ground(p, y), _plan.DirOf(p), uv(p), color, par);
            }
        var up = _plan.DirOf((min + max) * 0.5f);
        for (var j = 0; j < nz; j++)
            for (var i = 0; i < nx; i++)
            {
                uint a = (uint)(first + j * (nx + 1) + i), b = a + 1, d = a + (uint)(nx + 1), e = d + 1;
                var flip = Vector3.Dot(Vector3.Cross(_plan.East, _plan.North), up) < 0;
                // (x then z round the cell: with x east and z north the cell's winding faces down from the sheet's axes; checked once)
                if (Vector3.Dot(Vector3.Cross(c.V[(int)b].Position - c.V[(int)a].Position, c.V[(int)e].Position - c.V[(int)a].Position), up) < 0)
                {
                    Tri(c, detail, a, e, b);
                    Tri(c, detail, a, d, e);
                }
                else
                {
                    Tri(c, detail, a, b, e);
                    Tri(c, detail, a, e, d);
                }
                _ = flip;
            }
    }

    /// <summary>A solid draped slab: its top at <paramref name="top"/>, its foot under the ground, in the physics world only.</summary>
    private void SlabCollision(Chunk c, Vector2 min, Vector2 max, float top)
    {
        const float Foot = -0.6f;
        var nx = Math.Max(1, (int)MathF.Ceiling((max.X - min.X) / 8f));
        var nz = Math.Max(1, (int)MathF.Ceiling((max.Y - min.Y) / 8f));
        Vector2 Pt(int i, int j) => new(min.X + (max.X - min.X) * i / nx, min.Y + (max.Y - min.Y) * j / nz);
        var up = _plan.DirOf((min + max) * 0.5f);
        for (var j = 0; j < nz; j++)
            for (var i = 0; i < nx; i++)
            {
                Vector2 a = Pt(i, j), b = Pt(i + 1, j), d = Pt(i, j + 1), e = Pt(i + 1, j + 1);
                ColTri(c, _plan.Ground(a, top), _plan.Ground(b, top), _plan.Ground(e, top), up);
                ColTri(c, _plan.Ground(a, top), _plan.Ground(e, top), _plan.Ground(d, top), up);
                ColTri(c, _plan.Ground(a, Foot), _plan.Ground(b, Foot), _plan.Ground(e, Foot), -up);
                ColTri(c, _plan.Ground(a, Foot), _plan.Ground(e, Foot), _plan.Ground(d, Foot), -up);
            }
        // The four sides.
        var (ax, az, _) = Axes((min + max) * 0.5f);
        for (var i = 0; i < nx; i++)
        {
            Side(Pt(i, 0), Pt(i + 1, 0), -az);
            Side(Pt(i, nz), Pt(i + 1, nz), az);
        }
        for (var j = 0; j < nz; j++)
        {
            Side(Pt(0, j), Pt(0, j + 1), -ax);
            Side(Pt(nx, j), Pt(nx, j + 1), ax);
        }

        void Side(Vector2 p, Vector2 q, Vector3 outN)
        {
            ColTri(c, _plan.Ground(p, Foot), _plan.Ground(q, Foot), _plan.Ground(q, top), outN);
            ColTri(c, _plan.Ground(p, Foot), _plan.Ground(q, top), _plan.Ground(p, top), outN);
        }
    }

    /// <summary>A kerb's face between two points of the plan, from the carriageway up to the pavement, facing <paramref name="outN"/>.</summary>
    private void KerbFace(Chunk c, Vector2 p, Vector2 q, Vector3 outN, uint color, float along0)
    {
        var len = Vector2.Distance(p, q);
        var par = Pack(CityStyle.Kerb, 0, 0, 0);
        var n = Math.Max(1, (int)MathF.Ceiling(len / 8f));
        for (var k = 0; k < n; k++)
        {
            Vector2 a = Vector2.Lerp(p, q, k / (float)n), b = Vector2.Lerp(p, q, (k + 1) / (float)n);
            float ua = along0 + len * k / n, ub = along0 + len * (k + 1) / n;
            Quad(c, false, _plan.Ground(a, RoadLift - 0.02f), _plan.Ground(b, RoadLift - 0.02f), _plan.Ground(b, Kerb + RoadLift), _plan.Ground(a, Kerb + RoadLift),
                outN, outN, outN, outN, new Vector2(ua, 0), new Vector2(ub, 0), new Vector2(ub, Kerb), new Vector2(ua, Kerb), color, par, outN);
        }
    }

    private static readonly uint RoadColor = CityPlanColours.Rgb(0x2e3033), PaveColor = CityPlanColours.Rgb(0x8f8c86), KerbColor = CityPlanColours.Rgb(0xa9a7a1),
        CourtColor = CityPlanColours.Rgb(0x7d7a74), SquareColor = CityPlanColours.Rgb(0xa39a8c), PostColor = CityPlanColours.Rgb(0x2b2d30), LampColor = CityPlanColours.Rgb(0xffd49a);

    private void Street(CityStreet s)
    {
        var c = At((s.A + s.B) * 0.5f);
        var len = Vector2.Distance(s.A, s.B);
        var lamps = Math.Max(1, (int)MathF.Round((len - 2 * LampStart) / LampSpacing));
        var spacing = len > 2 * LampStart ? (len - 2 * LampStart) / lamps : len;
        // along runs from A; across is + towards the sheet's +z (a street along x) or +x (along z).
        Vector2 Pt(float along, float across) => s.AlongX ? new Vector2(s.A.X + along, s.A.Y + across) : new Vector2(s.A.X + across, s.A.Y + along);
        Vector2 Uv(Vector2 p) => s.AlongX ? new Vector2(p.X - s.A.X, p.Y - s.A.Y) : new Vector2(p.Y - s.A.Y, p.X - s.A.X);
        var road = Pack(CityStyle.Road, s.Avenue ? 1u : 0u, s.Carriage * 20, spacing * 4);
        var pave = Pack(CityStyle.Pavement, s.Avenue ? 1u : 0u, s.Carriage * 20, spacing * 4);
        Rect(Pt(0, -s.Carriage), Pt(len, s.Carriage), RoadLift, RoadColor, road);
        Rect(Pt(0, s.Carriage), Pt(len, s.Half), RoadLift + Kerb, PaveColor, pave);
        Rect(Pt(0, -s.Half), Pt(len, -s.Carriage), RoadLift + Kerb, PaveColor, pave);
        var (ax, az, _) = Axes((s.A + s.B) * 0.5f);
        var across = s.AlongX ? az : ax;
        KerbFace(c, Pt(0, s.Carriage), Pt(len, s.Carriage), -across, KerbColor, 0);
        KerbFace(c, Pt(0, -s.Carriage), Pt(len, -s.Carriage), across, KerbColor, 0);
        SlabMin(Pt(0, s.Carriage), Pt(len, s.Half));
        SlabMin(Pt(0, -s.Half), Pt(len, -s.Carriage));
        for (var k = 0; k <= lamps && len > 2 * LampStart; k++)
            foreach (var side in new[] { 1f, -1f })
                Lamp(Pt(LampStart + k * spacing, side * (s.Carriage + 0.7f)), s.AlongX ? new Vector2(0, -side) : new Vector2(-side, 0));

        void Rect(Vector2 a, Vector2 b, float y, uint col, uint par) => Sheet(c, Vector2.Min(a, b), Vector2.Max(a, b), y, col, par, Uv);
        void SlabMin(Vector2 a, Vector2 b) => SlabCollision(c, Vector2.Min(a, b), Vector2.Max(a, b), RoadLift + Kerb);
    }

    private void Crossing(CityCrossing x)
    {
        var c = At(x.At);
        var par = Pack(CityStyle.Crossing, 0, x.CarX * 20, x.CarZ * 20);
        Vector2 Uv(Vector2 p) => p - x.At;
        var o = x.At;
        // The carriageways' cross: the middle strip along z, and the two arms along x either side.
        Sheet(c, o + new Vector2(-x.CarX, -x.HalfZ), o + new Vector2(x.CarX, x.HalfZ), RoadLift, RoadColor, par, Uv);
        Sheet(c, o + new Vector2(x.CarX, -x.CarZ), o + new Vector2(x.HalfX, x.CarZ), RoadLift, RoadColor, par, Uv);
        Sheet(c, o + new Vector2(-x.HalfX, -x.CarZ), o + new Vector2(-x.CarX, x.CarZ), RoadLift, RoadColor, par, Uv);
        var pave = Pack(CityStyle.Pavement, 0, x.CarX * 20, 0, true);
        var (ax, az, _) = Axes(o);
        foreach (var sx in new[] { 1f, -1f })
            foreach (var sz in new[] { 1f, -1f })
            {
                var a = o + new Vector2(sx * x.CarX, sz * x.CarZ);
                var b = o + new Vector2(sx * x.HalfX, sz * x.HalfZ);
                Sheet(c, Vector2.Min(a, b), Vector2.Max(a, b), RoadLift + Kerb, PaveColor, pave, Uv);
                SlabCollision(c, Vector2.Min(a, b), Vector2.Max(a, b), RoadLift + Kerb);
                KerbFace(c, a, new Vector2(b.X, a.Y), -sz * az, KerbColor, 0);
                KerbFace(c, a, new Vector2(a.X, b.Y), -sx * ax, KerbColor, 0);
            }
    }

    /// <summary>A street lamp on the pavement: a post, an arm over the road towards <paramref name="toward"/>, a lit head.</summary>
    private void Lamp(Vector2 at, Vector2 toward)
    {
        var c = At(at);
        var (ax, az, up) = Axes(at);
        var foot = _plan.Ground(at, RoadLift + Kerb - 0.02f);
        var over = Vector3.Normalize(ax * toward.X + az * toward.Y);
        var side = Vector3.Cross(up, over);
        const float H = 7.2f, R = 0.09f, Arm = 1.6f;
        Box(c, foot + up * (H * 0.5f), side * R, up * (H * 0.5f), over * R, PostColor, Pack(CityStyle.Metal, 0, 0, 0), true);
        Box(c, foot + up * (H - 0.08f) + over * (Arm * 0.5f), side * 0.05f, up * 0.05f, over * (Arm * 0.5f), PostColor, Pack(CityStyle.Metal, 0, 0, 0), false);
        Box(c, foot + up * (H - 0.2f) + over * (Arm - 0.25f), side * 0.18f, up * 0.07f, over * 0.32f, LampColor, Pack(CityStyle.Lamp, 0, 0, 0), false);
    }

    /// <summary>A box from its middle and three half axes (detail: drawn near only); a solid one stands in the physics world.</summary>
    private void Box(Chunk c, Vector3 mid, Vector3 hx, Vector3 hy, Vector3 hz, uint color, uint par, bool solid, bool detail = true)
    {
        Span<Vector3> axes = [hx, hy, hz];
        for (var k = 0; k < 3; k++)
            foreach (var s in new[] { 1f, -1f })
            {
                Vector3 n = axes[k] * s, a1 = axes[(k + 1) % 3], a2 = axes[(k + 2) % 3];
                var nn = Vector3.Normalize(n);
                Vector3 p0 = mid + n - a1 - a2, p1 = mid + n + a1 - a2, p2 = mid + n + a1 + a2, p3 = mid + n - a1 + a2;
                float l1 = a1.Length() * 2, l2 = a2.Length() * 2;
                Quad(c, detail, p0, p1, p2, p3, nn, nn, nn, nn, Vector2.Zero, new Vector2(l1, 0), new Vector2(l1, l2), new Vector2(0, l2), color, par, nn);
                if (solid)
                {
                    ColTri(c, p0, p1, p2, nn);
                    ColTri(c, p0, p2, p3, nn);
                }
            }
    }

    private void Build()
    {
        foreach (var m in _plan.Masses)
            Mass(m);
        foreach (var s in _plan.Streets)
            Street(s);
        foreach (var x in _plan.Crossings)
            Crossing(x);
        foreach (var b in _plan.Blocks)
        {
            if (b.Kind == 1)
                continue; // (under the square)
            var c = At((b.Min + b.Max) * 0.5f);
            Sheet(c, b.Min, b.Max, RoadLift + Kerb, CourtColor, Pack(CityStyle.Court, 0, 0, 0), p => p);
            SlabCollision(c, b.Min, b.Max, RoadLift + Kerb);
        }
        Square();
        if (_plan.Rail is { } rail)
            Railway(rail);
        Pack();
    }

    /// <summary>The square in the middle: paving, a ring of lamps, and a column in the centre with a lamp on top.</summary>
    private void Square()
    {
        Vector2 min = _plan.SquareMin, max = _plan.SquareMax;
        // Paving in chunk-sized pieces, so each piece is culled with its own chunk.
        for (var x = min.X; x < max.X - 0.01f; x += ChunkSize * 0.5f)
            for (var z = min.Y; z < max.Y - 0.01f; z += ChunkSize * 0.5f)
            {
                var a = new Vector2(x, z);
                var b = Vector2.Min(max, a + new Vector2(ChunkSize * 0.5f));
                var c = At((a + b) * 0.5f);
                Sheet(c, a, b, RoadLift + Kerb, SquareColor, Pack(CityStyle.Court, 1, 0, 0), p => p);
                SlabCollision(c, a, b, RoadLift + Kerb);
            }
        // Lamps round its edge, facing in.
        for (var t = 6f; t < max.X - min.X - 3; t += 18f)
        {
            Lamp(new Vector2(min.X + t, min.Y + 1.2f), new Vector2(0, 1));
            Lamp(new Vector2(min.X + t, max.Y - 1.2f), new Vector2(0, -1));
        }
        for (var t = 6f; t < max.Y - min.Y - 3; t += 18f)
        {
            Lamp(new Vector2(min.X + 1.2f, min.Y + t), new Vector2(1, 0));
            Lamp(new Vector2(max.X - 1.2f, min.Y + t), new Vector2(-1, 0));
        }
        // Trees in planters along the square's sides, benches between them, two kiosks.
        var d = new CityPlan.Dice((uint)_plan.Spec.Seed * 977u + 5);
        for (var t = 9f; t < max.X - min.X - 6; t += 13f)
            foreach (var z in new[] { min.Y + 7f, max.Y - 7f })
            {
                Tree(new Vector2(min.X + t, z), ref d);
                if (t + 13f < max.X - min.X - 6)
                    Bench(new Vector2(min.X + t + 6.5f, z), true);
            }
        for (var t = 22f; t < max.Y - min.Y - 19; t += 13f)
            foreach (var x in new[] { min.X + 7f, max.X - 7f })
            {
                Tree(new Vector2(x, min.Y + t), ref d);
                if (t + 13f < max.Y - min.Y - 19)
                    Bench(new Vector2(x, min.Y + t + 6.5f), false);
            }
        Kiosk(new Vector2(min.X + 22, (min.Y + max.Y) * 0.5f + 18), ref d);
        Kiosk(new Vector2(max.X - 22, (min.Y + max.Y) * 0.5f - 18), ref d);
        // The column at the pole: a stepped plinth, a tall shaft, a lantern.
        var mid = Vector2.Zero;
        var cc = At(mid);
        var (ax, az, up) = Axes(mid);
        var foot = _plan.Ground(mid, RoadLift + Kerb - 0.05f);
        uint stone = CityPlanColours.Rgb(0xb8ae9c), plain = Pack(CityStyle.Plain, 7, 0, 0);
        Box(cc, foot + up * 0.4f, ax * 6, up * 0.45f, az * 6, stone, plain, true, false);
        Box(cc, foot + up * 1.2f, ax * 4.5f, up * 0.45f, az * 4.5f, stone, plain, true, false);
        Box(cc, foot + up * 3.6f, ax * 2.2f, up * 2f, az * 2.2f, stone, plain, true, false);
        Box(cc, foot + up * 19.6f, ax * 1.1f, up * 14f, az * 1.1f, stone, plain, true, false);
        Box(cc, foot + up * 34.2f, ax * 1.6f, up * 0.6f, az * 1.6f, stone, plain, false, false);
        Box(cc, foot + up * 35.6f, ax * 0.9f, up * 0.8f, az * 0.9f, LampColor, Pack(CityStyle.Lamp, 0, 0, 0), false, false);
    }

    private static readonly uint Bark = CityPlanColours.Rgb(0x4a3a2c), Planter = CityPlanColours.Rgb(0x8d877c), Wood = CityPlanColours.Rgb(0x6b4a30);

    /// <summary>An upright prism: <paramref name="sides"/> sides, radius r0 at its foot and r1 at its top (a cone's frustum).</summary>
    private void Prism(Chunk c, Vector2 at, float y0, float y1, float r0, float r1, int sides, uint color, uint par, bool solid, bool detail = true, float turn = 0)
    {
        var (ax, az, up) = Axes(at);
        var o = _plan.Ground(at);
        Span<Vector3> ring = stackalloc Vector3[sides];
        for (var i = 0; i < sides; i++)
        {
            var (sn, cs) = MathF.SinCos(turn + i * MathF.Tau / sides);
            ring[i] = ax * cs + az * sn;
        }
        var slope = (r0 - r1) / MathF.Max(y1 - y0, 0.01f);
        var u = 0f;
        for (var i = 0; i < sides; i++)
        {
            Vector3 da = ring[i], db = ring[(i + 1) % sides];
            Vector3 a0 = o + up * y0 + da * r0, b0 = o + up * y0 + db * r0, a1 = o + up * y1 + da * r1, b1 = o + up * y1 + db * r1;
            var mid = Vector3.Normalize(da + db);
            var nn = Vector3.Normalize(mid + up * slope);
            var len = Vector3.Distance(a0, b0);
            Quad(c, detail, a0, b0, b1, a1, nn, nn, nn, nn, new Vector2(u, y0), new Vector2(u + len, y0), new Vector2(u + len, y1), new Vector2(u, y1), color, par, nn);
            if (solid)
            {
                ColTri(c, a0, b0, b1, nn);
                ColTri(c, a0, b1, a1, nn);
            }
            u += len;
        }
        var top = Vert(c, o + up * y1, up, Vector2.Zero, color, par);
        for (var i = 0; i < sides; i++)
        {
            Vector3 a1 = o + up * y1 + ring[i] * r1, b1 = o + up * y1 + ring[(i + 1) % sides] * r1;
            var ia = Vert(c, a1, up, Vector2.Zero, color, par);
            var ib = Vert(c, b1, up, Vector2.Zero, color, par);
            if (Vector3.Dot(Vector3.Cross(a1 - (o + up * y1), b1 - (o + up * y1)), up) < 0)
                Tri(c, detail, top, ib, ia);
            else
                Tri(c, detail, top, ia, ib);
            if (solid)
            {
                ColTri(c, o + up * y1, a1, b1, up);
                ColTri(c, o + up * y0, o + up * y0 + ring[i] * r0, o + up * y0 + ring[(i + 1) % sides] * r0, -up);
            }
        }
    }

    /// <summary>A tree in a stone planter: a trunk and a crown of two leafy cones, each a little different.</summary>
    private void Tree(Vector2 at, ref CityPlan.Dice d)
    {
        var c = At(at);
        var y = RoadLift + Kerb;
        Prism(c, at, y - 0.05f, y + 0.5f, 1.3f, 1.3f, 8, Planter, Pack(CityStyle.Plain, 3, 0, 0), true, turn: MathF.PI / 8);
        var h = d.Range(5.5f, 8f);
        var leaves = Pack(CityStyle.Leaves, d.Next(), 0, 0);
        uint green = CityPlanColours.Rgb(d.Pick(3) switch { 0 => 0x3d5a2e, 1 => 0x4a6b33, _ => 0x55602f });
        var turn = d.Range(0, 1);
        Prism(c, at, y + 0.5f, y + h * 0.55f, 0.16f, 0.12f, 6, Bark, Pack(CityStyle.Plain, 4, 0, 0), true);
        Prism(c, at, y + h * 0.35f, y + h * 0.78f, d.Range(2.2f, 2.8f), 1.7f, 8, green, leaves, false, turn: turn);
        Prism(c, at, y + h * 0.78f, y + h, 1.7f, 0.5f, 8, green, leaves, false, turn: turn + 0.2f);
    }

    /// <summary>A bench: a slatted seat on two stone ends, along x or along z.</summary>
    private void Bench(Vector2 at, bool alongX)
    {
        var c = At(at);
        var (ax, az, up) = Axes(at);
        if (!alongX)
            (ax, az) = (az, ax);
        var foot = _plan.Ground(at, RoadLift + Kerb);
        var stone = Pack(CityStyle.Plain, 5, 0, 0);
        Box(c, foot + ax * 0.85f + up * 0.22f, ax * 0.12f, up * 0.22f, az * 0.25f, Planter, stone, false);
        Box(c, foot - ax * 0.85f + up * 0.22f, ax * 0.12f, up * 0.22f, az * 0.25f, Planter, stone, false);
        Box(c, foot + up * 0.47f, ax * 1.0f, up * 0.04f, az * 0.26f, Wood, Pack(CityStyle.Plain, 6, 0, 0), true);
    }

    /// <summary>A kiosk: a small lit booth with a striped awning.</summary>
    private void Kiosk(Vector2 at, ref CityPlan.Dice d)
    {
        var c = At(at);
        var (ax, az, up) = Axes(at);
        var foot = _plan.Ground(at, RoadLift + Kerb - 0.02f);
        var shopPar = Pack(CityStyle.Grid, d.Next(), 3.6f * 20, 0.9f * 255, true);
        Box(c, foot + up * 1.4f, ax * 2.2f, up * 1.4f, az * 1.6f, CityPlanColours.Rgb(0x2f5a4a), shopPar, true, false);
        Box(c, foot + up * 2.95f, ax * 2.6f, up * 0.15f, az * 2.0f, CityPlanColours.Rgb(0xc8483c), Pack(CityStyle.Plain, 8, 0, 0), true, false);
    }

    // ---- The elevated railway ----------------------------------------------------------------------------------------

    private static readonly uint Concrete = CityPlanColours.Rgb(0x8a857c), Steel = CityPlanColours.Rgb(0x3d4247), Ballast = CityPlanColours.Rgb(0x5a534b);

    /// <summary>The railway's point <paramref name="i"/> (wrapping round the loop): its plan point and the plan's unit sideways (left of the way along).</summary>
    private static (Vector2 P, Vector2 Side) RailAt(CityRail r, int i)
    {
        var n = r.Path.Length;
        var p = r.Path[((i % n) + n) % n];
        var t = r.Path[(((i + 1) % n) + n) % n] - r.Path[(((i - 1) % n) + n) % n];
        t = Vector2.Normalize(t);
        return (p, new Vector2(-t.Y, t.X));
    }

    /// <summary>A section (across, up) swept along the railway from point i0 to i1 (wrapping round the loop); capped at both
    /// ends unless it runs right round.</summary>
    private void Sweep(CityRail r, int i0, int i1, ReadOnlySpan<Vector2> section, uint color, uint par, bool solid, bool detail, bool loop)
    {
        var area = 0f;
        for (var j = 0; j < section.Length; j++)
        {
            var a = section[j];
            var b = section[(j + 1) % section.Length];
            area += a.X * b.Y - b.X * a.Y;
        }
        var ccw = area > 0;
        float along = 0;
        for (var i = i0; i < i1; i++)
        {
            var (p, s) = RailAt(r, i);
            var (q, sq) = RailAt(r, i + 1);
            var c = At(p);
            var len = Vector2.Distance(p, q);
            var up = _plan.DirOf(p);
            var sideW = Vector3.Normalize(_plan.Ground(p + s) - _plan.Ground(p));
            for (var j = 0; j < section.Length; j++)
            {
                var a = section[j];
                var b = section[(j + 1) % section.Length];
                var e = b - a;
                var nn2 = Vector2.Normalize(ccw ? new Vector2(e.Y, -e.X) : new Vector2(-e.Y, e.X));
                var nn = Vector3.Normalize(sideW * nn2.X + up * nn2.Y);
                Vector3 pa = _plan.Ground(p + s * a.X, r.Height + a.Y), pb = _plan.Ground(p + s * b.X, r.Height + b.Y);
                Vector3 qa = _plan.Ground(q + sq * a.X, r.Height + a.Y), qb = _plan.Ground(q + sq * b.X, r.Height + b.Y);
                var upright = MathF.Abs(e.X) < 0.01f; // (an upright face: across it is its height)
                float va = upright ? a.Y : a.X, vb = upright ? b.Y : b.X;
                Quad(c, detail, pa, pb, qb, qa, nn, nn, nn, nn, new Vector2(along, va), new Vector2(along, vb), new Vector2(along + len, vb), new Vector2(along + len, va), color, par, nn);
                if (solid)
                {
                    ColTri(c, pa, pb, qb, nn);
                    ColTri(c, pa, qb, qa, nn);
                }
            }
            along += len;
        }
        if (loop)
            return;
        // The ends: a fan round the section's middle.
        var mid = Vector2.Zero;
        foreach (var v in section)
            mid += v;
        mid /= section.Length;
        foreach (var (i, sign) in new[] { (i0, -1), (i1, 1) })
        {
            var (p, s) = RailAt(r, i);
            var (q, _) = RailAt(r, i + sign);
            var c = At(p);
            var outN = Vector3.Normalize(_plan.Ground(q) - _plan.Ground(p));
            var m = _plan.Ground(p + s * mid.X, r.Height + mid.Y);
            for (var j = 0; j < section.Length; j++)
            {
                var sa = section[j];
                var sb = section[(j + 1) % section.Length];
                Vector3 a = _plan.Ground(p + s * sa.X, r.Height + sa.Y), b = _plan.Ground(p + s * sb.X, r.Height + sb.Y);
                var im = Vert(c, m, outN, Vector2.Zero, color, par);
                var ia = Vert(c, a, outN, Vector2.Zero, color, par);
                var ib = Vert(c, b, outN, Vector2.Zero, color, par);
                if (Vector3.Dot(Vector3.Cross(a - m, b - m), outN) < 0)
                    Tri(c, detail, im, ib, ia);
                else
                    Tri(c, detail, im, ia, ib);
                if (solid)
                    ColTri(c, m, a, b, outN);
            }
        }
    }

    /// <summary>Where along the railway (a point's index) each station stands.</summary>
    private static int[] StationPoints(CityRail r)
    {
        var st = new int[r.Stations.Length];
        for (var k = 0; k < st.Length; k++)
            for (var i = 1; i < r.Path.Length; i++)
                if (Vector2.DistanceSquared(r.Path[i], r.Stations[k]) < Vector2.DistanceSquared(r.Path[st[k]], r.Stations[k]))
                    st[k] = i;
        Array.Sort(st);
        return st;
    }

    /// <summary>A station's platforms run this many points (4 m each) either side of its middle.</summary>
    public const int StationHalf = 6;

    private void Railway(CityRail r)
    {
        var n = r.Path.Length;
        // The deck: a concrete box girder; its top the track bed.
        Sweep(r, 0, n, [new(-3.9f, -1.2f), new(3.9f, -1.2f), new(3.9f, 0f), new(-3.9f, 0f)], Concrete, Pack(CityStyle.Plain, 9, 0, 0), true, false, true);
        Sweep(r, 0, n, [new(-3.85f, 0f), new(3.85f, 0f), new(3.85f, 0.03f), new(-3.85f, 0.03f)], Ballast, Pack(CityStyle.Rail, 0, 0, 0), false, false, true);
        var st = StationPoints(r);
        var parapet = Pack(CityStyle.Plain, 10, 0, 0);
        for (var k = 0; k < st.Length; k++)
        {
            int a = st[k] + StationHalf, b = st[(k + 1) % st.Length] - StationHalf + (k + 1 == st.Length ? n : 0);
            if (b <= a)
                continue;
            Sweep(r, a, b, [new(3.6f, 0.03f), new(3.9f, 0.03f), new(3.9f, 1.1f), new(3.6f, 1.1f)], Concrete, parapet, true, true, false);
            Sweep(r, a, b, [new(-3.9f, 0.03f), new(-3.6f, 0.03f), new(-3.6f, 1.1f), new(-3.9f, 1.1f)], Concrete, parapet, true, true, false);
        }
        // Columns every 20 m under the middle, with a crosshead under the deck.
        for (var i = 0; i < n; i += 5)
        {
            var (p, s) = RailAt(r, i);
            var c = At(p);
            var up = _plan.DirOf(p);
            var sideW = Vector3.Normalize(_plan.Ground(p + s) - _plan.Ground(p));
            var along = Vector3.Cross(up, sideW);
            var foot = _plan.Ground(p, RoadLift);
            var top = r.Height - 1.2f;
            Box(c, foot + up * (top * 0.5f - 0.6f), sideW * 0.45f, up * (top * 0.5f - 0.6f), along * 0.45f, Steel, Pack(CityStyle.Metal, 0, 0, 0), true, false);
            Box(c, foot + up * (top - 0.55f), sideW * 3.2f, up * 0.55f, along * 0.5f, Steel, Pack(CityStyle.Metal, 0, 0, 0), true, false);
        }
        foreach (var k in st)
            Station(r, k);
    }

    /// <summary>A station: side platforms beyond the track, a canopy on columns with lamps under it, stairs down to each pavement.</summary>
    private void Station(CityRail r, int k)
    {
        var plat = Pack(CityStyle.Pavement, 0, 0, 0, true);
        int a = k - StationHalf, b = k + StationHalf;
        foreach (var side in new[] { 1f, -1f })
        {
            Vector2 S(float x0, float y0, float x1, float y1, int corner) => corner switch
            {
                0 => new Vector2(side > 0 ? x0 : -x1, y0),
                1 => new Vector2(side > 0 ? x1 : -x0, y0),
                2 => new Vector2(side > 0 ? x1 : -x0, y1),
                _ => new Vector2(side > 0 ? x0 : -x1, y1),
            };
            ReadOnlySpan<Vector2> Box4(float x0, float y0, float x1, float y1) => new[] { S(x0, y0, x1, y1, 0), S(x0, y0, x1, y1, 1), S(x0, y0, x1, y1, 2), S(x0, y0, x1, y1, 3) };
            // The platform: its top a metre over the track bed (a train's floor), from just off the track to 8 m out.
            Sweep(r, a, b, Box4(3.95f, -1.0f, 10.8f, 0.98f), Concrete, Pack(CityStyle.Plain, 9, 0, 0), true, false, false);
            Sweep(r, a, b, Box4(3.95f, 0.98f, 10.8f, 1.0f), PaveColor, plat, true, false, false);
            Sweep(r, a, b, Box4(3.95f, 1.0f, 4.5f, 1.012f), CityPlanColours.Rgb(0xd8b02a), Pack(CityStyle.Plain, 11, 0, 0), false, true, false);
            // The canopy over it, and its lamps.
            Sweep(r, a, b, Box4(3.5f, 4.6f, 11.2f, 4.9f), CityPlanColours.Rgb(0x2f4a5c), Pack(CityStyle.Plain, 12, 0, 0), true, false, false);
            Sweep(r, a + 1, b - 1, Box4(7.0f, 4.5f, 7.3f, 4.6f), LampColor, Pack(CityStyle.Lamp, 0, 0, 0), false, true, false);
            for (var i = a; i <= b; i += 3)
            {
                var (p, s) = RailAt(r, i);
                var c = At(p);
                var up = _plan.DirOf(p);
                var sideW = Vector3.Normalize(_plan.Ground(p + s) - _plan.Ground(p));
                var along = Vector3.Cross(up, sideW);
                var at = _plan.Ground(p + s * side * 10.4f, r.Height + 1.0f);
                Box(c, at + up * 1.8f, sideW * 0.12f, up * 1.8f, along * 0.12f, Steel, Pack(CityStyle.Metal, 0, 0, 0), true);
            }
            Stairs(r, b, side);
        }
    }

    /// <summary>A flight of stairs from the platform's end at point <paramref name="i"/> on down the street beside the deck.</summary>
    private void Stairs(CityRail r, int i, float side)
    {
        var (p, s) = RailAt(r, i);
        var (q, _) = RailAt(r, i + 1);
        var t = Vector2.Normalize(q - p);
        var foot = RoadLift + Kerb;
        var rise = r.Height + 1.0f - foot;
        var run = rise / MathF.Tan(33f * MathF.PI / 180);
        const float W = 2.4f;
        var across = side * 9.6f; // the flight's middle, under the platform's outer half
        var c = At(p + t * run * 0.5f);
        var up = _plan.DirOf(p + t * run * 0.5f);
        Vector3 G(float a, float x, float y) => _plan.Ground(p + t * a + s * (across + x), y);
        var stairs = Pack(CityStyle.Stairs, 0, 0, 0);
        var plain = Pack(CityStyle.Plain, 9, 0, 0);
        var sideN = Vector3.Normalize(G(0, 1, 0) - G(0, 0, 0));
        var fwd = Vector3.Normalize(G(1, 0, 0) - G(0, 0, 0));
        Vector3 t0a = G(-0.05f, -W / 2, rise + foot), t0b = G(-0.05f, W / 2, rise + foot), t1a = G(run, -W / 2, foot), t1b = G(run, W / 2, foot);
        Vector3 s0a = G(-0.05f, -W / 2, rise + foot - 0.7f), s0b = G(-0.05f, W / 2, rise + foot - 0.7f), s1a = G(run, -W / 2, foot - 0.3f), s1b = G(run, W / 2, foot - 0.3f);
        var slopeN = Vector3.Normalize(Vector3.Cross(t1a - t0a, t0b - t0a));
        if (Vector3.Dot(slopeN, up) < 0)
            slopeN = -slopeN;
        var slopeLen = Vector3.Distance(t0a, t1a);
        Quad(c, false, t0a, t0b, t1b, t1a, slopeN, slopeN, slopeN, slopeN, new Vector2(-W / 2, 0), new Vector2(W / 2, 0), new Vector2(W / 2, slopeLen), new Vector2(-W / 2, slopeLen), PaveColor, stairs, slopeN);
        Quad(c, false, s0a, s0b, s1b, s1a, -slopeN, -slopeN, -slopeN, -slopeN, Vector2.Zero, Vector2.Zero, Vector2.Zero, Vector2.Zero, Concrete, plain, -slopeN);
        Quad(c, false, t0a, t0b, s0b, s0a, -fwd, -fwd, -fwd, -fwd, Vector2.Zero, Vector2.Zero, Vector2.Zero, Vector2.Zero, Concrete, plain, -fwd);
        foreach (var (a0, a1, b0, b1, nn) in new[] { (t0a, t1a, s0a, s1a, -sideN), (t0b, t1b, s0b, s1b, sideN) })
            Quad(c, false, a0, a1, b1, b0, nn, nn, nn, nn, new Vector2(0, 0.7f), new Vector2(run, 0.3f), new Vector2(run, 0), Vector2.Zero, Concrete, plain, nn);
        // Solid: the flight as a closed wedge.
        ColTri(c, t0a, t0b, t1b, slopeN);
        ColTri(c, t0a, t1b, t1a, slopeN);
        ColTri(c, s0a, s0b, s1b, -slopeN);
        ColTri(c, s0a, s1b, s1a, -slopeN);
        ColTri(c, t0a, t0b, s0b, -fwd);
        ColTri(c, t0a, s0b, s0a, -fwd);
        ColTri(c, t1a, t1b, s1b, fwd);
        ColTri(c, t1a, s1b, s1a, fwd);
        ColTri(c, t0a, t1a, s1a, -sideN);
        ColTri(c, t0a, s1a, s0a, -sideN);
        ColTri(c, t0b, t1b, s1b, sideN);
        ColTri(c, t0b, s1b, s0b, sideN);
        // Handrails on both sides.
        foreach (var sx in new[] { -1f, 1f })
        {
            Vector3 top = G(0, sx * (W / 2 - 0.05f), rise + foot + 0.95f), bottom = G(run, sx * (W / 2 - 0.05f), foot + 0.95f);
            var mid = (top + bottom) * 0.5f;
            var axis = (bottom - top) * 0.5f;
            var side2 = sideN * 0.03f;
            Box(c, mid, axis, Vector3.Normalize(Vector3.Cross(axis, side2)) * 0.03f, side2, Steel, Pack(CityStyle.Metal, 0, 0, 0), false);
        }
    }

    private void Pack()
    {
        var verts = new List<CityVertex>();
        var idx = new List<uint>();
        var chunks = new List<CityChunk>();
        foreach (var c in _chunks.Values)
        {
            var baseV = (uint)verts.Count;
            verts.AddRange(c.V);
            var first = idx.Count;
            foreach (var i in c.Main)
                idx.Add(baseV + i);
            foreach (var i in c.Detail)
                idx.Add(baseV + i);
            var centre = (c.Min + c.Max) * 0.5f;
            chunks.Add(new CityChunk { Origin = c.Origin, Centre = centre, Radius = (c.Max - c.Min).Length() * 0.5f, First = first, Main = c.Main.Count, Detail = c.Detail.Count });
            if (c.CI.Count > 0)
            {
                Colliders.Add(new TriangleMeshCollider(c.CP.ToArray(), c.CI.ToArray()));
                CollisionTriangles += c.CI.Count / 3;
            }
        }
        Vertices = verts.ToArray();
        Indices = idx.ToArray();
        Chunks = chunks.ToArray();
        Triangles = Indices.Length / 3;
    }
}

/// <summary>Colours written as 0xRRGGBB for the vertex's RGBA8.</summary>
public static class CityPlanColours
{
    public static uint Rgb(uint hex) => 0xff000000u | (hex >> 16 & 255) | (hex & 0xff00) | (hex & 255) << 16;
}
