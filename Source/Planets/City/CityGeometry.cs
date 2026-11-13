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

    /// <summary>The apron's indices (drawn first, pushed back in depth so nothing on it ever fights it).</summary>
    public int UnderFirst, Under;
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
        public readonly List<uint> Main = new(), Detail = new(), Under = new();
        public readonly List<Vector3> CP = new();
        public readonly List<int> CI = new();
        public Vector3 Min = new(float.MaxValue), Max = new(float.MinValue);
    }

    private readonly Dictionary<(int, int), Chunk> _chunks = new();
    [ThreadStatic] private static bool _under; // (Tri is static: set while the apron is written)
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
        var l = _under ? c.Under : detail ? c.Detail : c.Main;
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
        if (style == CityStyle.Neon)
        {
            // A blade or a billboard: its word (from the building's seed), its wide and narrow faces' widths, and which
            // way round they go (the outline starts down the +x side: a mass wider in x has its narrow face first).
            var wideX = m.Half.X >= m.Half.Y;
            float wide = 2 * MathF.Max(m.Half.X, m.Half.Y), narrow = 2 * MathF.Min(m.Half.X, m.Half.Y);
            var word = (uint)((b.Seed >> 3) % (uint)CitySigns.Shops.Length);
            // A blade stacks its letters 0.85 m apart from 5.7 m up: pick a name short enough to fit its height.
            if (m.Color != 0 && !(MathF.Min(MathF.Min((m.Color & 255) / 255f, (m.Color >> 8 & 255) / 255f), (m.Color >> 16 & 255) / 255f) > 0.6f))
            {
                var room = (int)((m.Top - 5.8f) / 0.85f);
                for (var k = 0; k < CitySigns.Shops.Length && CitySigns.Shops[word].Length > room; k++)
                    word = (word + 1) % (uint)CitySigns.Shops.Length;
            }
            par = Pack(style, word, wide * 10, narrow * 100, !wideX);
        }
        var round = m.Shape == 2;
        var cm = m;
        Vector2 T(Vector2 q) => cm.Shape == 3 ? cm.At + (q - cm.At) * cm.Cut : q; // (a tapered mass: a hipped roof, a spire)
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
            if (m.Shape == 3)
            {
                var sloped = Vector3.Normalize(Vector3.Cross(P(e, m.Bottom) - P(a, m.Bottom), P(T(a), m.Top) - P(a, m.Bottom)));
                outN = Vector3.Dot(sloped, outN) < 0 ? -sloped : sloped;
            }
            Vector3 na = round ? Dir(a - m.At) : outN, ne = round ? Dir(e - m.At) : outN;
            Quad(c, m.Detail, P(a, m.Bottom), P(e, m.Bottom), P(T(e), m.Top), P(T(a), m.Top), na, ne, ne, na,
                new Vector2(u, m.Bottom), new Vector2(u + len, m.Bottom), new Vector2(u + len, m.Top), new Vector2(u, m.Top), m.Color, par, outN);
            if (m.Solid)
            {
                ColTri(c, P(a, m.Bottom), P(e, m.Bottom), P(T(e), m.Top), outN);
                ColTri(c, P(a, m.Bottom), P(T(e), m.Top), P(T(a), m.Top), outN);
            }
            u += len;
        }
        // The top: a fan round its middle (roofs of facade masses are roofing; a plain mass keeps its own colour).
        var topPar = facade ? Pack(CityStyle.Roof, b.Seed, 0, 0) : par;
        var topColor = facade ? RoofColor : m.Color;
        var centre = Vert(c, P(m.At, m.Top), up, m.At, topColor, topPar);
        var first = c.V.Count;
        for (var i = 0; i < n; i++)
            Vert(c, P(T(pts[i]), m.Top), up, T(pts[i]), topColor, topPar);
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
                ColTri(c, P(m.At, m.Top), P(T(pts[i]), m.Top), P(T(pts[(i + 1) % n]), m.Top), up);
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
    private void SlabCollision(Chunk c, Vector2 min, Vector2 max, float top, float foot = -0.6f)
    {
        var Foot = foot;
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

        void Rect(Vector2 a, Vector2 b, float y, uint col, uint par)
        {
            foreach (var (lo, hi) in y > RoadLift ? Uncut(Vector2.Min(a, b), Vector2.Max(a, b)) : [(Vector2.Min(a, b), Vector2.Max(a, b))])
                if (hi.X - lo.X > 0.01f && hi.Y - lo.Y > 0.01f)
                    Sheet(c, lo, hi, y, col, par, Uv);
        }
        void SlabMin(Vector2 a, Vector2 b)
        {
            foreach (var (lo, hi) in Uncut(Vector2.Min(a, b), Vector2.Max(a, b)))
                if (hi.X - lo.X > 0.01f && hi.Y - lo.Y > 0.01f)
                    SlabCollision(c, lo, hi, RoadLift + Kerb);
        }
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
                PavedSlab(c, Vector2.Min(a, b), Vector2.Max(a, b), RoadLift + Kerb, PaveColor, pave, Uv);
                KerbFace(c, a, new Vector2(b.X, a.Y), -sz * az, KerbColor, 0);
                KerbFace(c, a, new Vector2(a.X, b.Y), -sx * ax, KerbColor, 0);
            }
    }

    /// <summary>A street lamp on the pavement: a post, an arm over the road towards <paramref name="toward"/>, a lit head.</summary>
    private void Lamp(Vector2 at, Vector2 toward)
    {
        if (InHole(at, 0.6f))
            return;
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
                // A side face's metres read as a viewer outside it sees them: x to their right, y up (hy is up), so a
                // sign's word is never mirrored.
                if (k != 1)
                {
                    var yUp = Vector3.Normalize(hy);
                    (a1, a2) = MathF.Abs(Vector3.Dot(Vector3.Normalize(a2), yUp)) > 0.5f ? (a1, a2) : (a2, a1);
                    if (Vector3.Dot(a1, Vector3.Cross(yUp, nn)) < 0)
                        a1 = -a1;
                    if (Vector3.Dot(a2, yUp) < 0)
                        a2 = -a2;
                }
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

    /// <summary>How far from the centre the city's ground reaches (m): the apron's edge, inside which the planetoid's own
    /// ground is not drawn (PlanetRenderer.CityClip).</summary>
    public float Reach;

    /// <summary>The apron: a disc of rough asphalt just over the ground under the whole city, so nothing of the planetoid's
    /// ground shows between its streets and blocks (the terrain under it is left out of the drawing).</summary>
    private void Apron()
    {
        var reach = 0f;
        foreach (var b in _plan.Blocks)
            foreach (var c in new[] { b.Min, b.Max, new Vector2(b.Min.X, b.Max.Y), new Vector2(b.Max.X, b.Min.Y) })
                reach = MathF.Max(reach, c.Length());
        Reach = reach + 30;
        const int Seg = 256;
        var rings = (int)MathF.Ceiling(Reach / 16f);
        var par = Pack(CityStyle.Road, 2, 0, 0);
        var colour = CityPlanColours.Rgb(0x34322f);
        for (var i = 0; i < rings; i++)
        {
            float r0 = Reach * i / rings, r1 = Reach * (i + 1) / rings;
            var seg = i == 0 ? 16 : Seg;
            for (var j = 0; j < seg; j++)
            {
                float a0 = MathF.Tau * j / seg, a1 = MathF.Tau * (j + 1) / seg;
                Vector2 p00 = new Vector2(MathF.Cos(a0), MathF.Sin(a0)) * r0, p01 = new Vector2(MathF.Cos(a1), MathF.Sin(a1)) * r0;
                Vector2 p10 = new Vector2(MathF.Cos(a0), MathF.Sin(a0)) * r1, p11 = new Vector2(MathF.Cos(a1), MathF.Sin(a1)) * r1;
                var mid = (p00 + p11) * 0.5f;
                var c = At(mid);
                var up = _plan.DirOf(mid);
                Quad(c, false, _plan.Ground(p00, 0.01f), _plan.Ground(p10, 0.01f), _plan.Ground(p11, 0.01f), _plan.Ground(p01, 0.01f), _plan.DirOf(p00), _plan.DirOf(p10), _plan.DirOf(p11), _plan.DirOf(p01),
                    p00, p10, p11, p01, colour, par, up);
            }
        }
    }

    private void Build()
    {
        if (_plan.Metro is { } metro)
            _holes.AddRange(metro.Holes());
        _under = true;
        Apron();
        _under = false;
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
            PavedSlab(c, b.Min, b.Max, RoadLift + Kerb, CourtColor, Pack(CityStyle.Court, (uint)(b.Kind == 2 ? 3 : 0), 0, 0), p => p);
            if (b.Kind == 2)
                Market(b);
        }
        Square();
        if (_plan.Rail is { } rail)
            Railway(rail);
        if (_plan.Metro is { } underground)
            Metro(underground);
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
                PavedSlab(c, a, b, RoadLift + Kerb, SquareColor, Pack(CityStyle.Court, 1, 0, 0), p => p);
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
        // A fountain round the column's plinth: a stone basin, water in it.
        {
            var cc0 = At(Vector2.Zero);
            Prism(cc0, Vector2.Zero, RoadLift + Kerb - 0.05f, RoadLift + Kerb + 0.55f, 10.4f, 10.4f, 32, CityPlanColours.Rgb(0xb0a594), Pack(CityStyle.Plain, 7, 0, 0), true, false);
            Prism(cc0, Vector2.Zero, RoadLift + Kerb + 0.55f, RoadLift + Kerb + 0.6f, 9.9f, 9.9f, 32, CityPlanColours.Rgb(0x2a4a58), Pack(CityStyle.Water, 0, 0, 0), false, false);
        }
        // Four planted beds in the square's quarters, each with its low stone kerb and three trees.
        foreach (var (qx, qz) in new[] { (-1f, -1f), (1f, -1f), (-1f, 1f), (1f, 1f) })
        {
            var centre = new Vector2(qx * MathF.Min(36f, (max.X - min.X) * 0.25f), qz * MathF.Min(28f, (max.Y - min.Y) * 0.22f));
            Vector2 half = new(8f, 5f);
            var cb = At(centre);
            var (bx, bz, bu) = Axes(centre);
            var bedFoot = _plan.Ground(centre, RoadLift + Kerb - 0.05f);
            Box(cb, bedFoot + bu * 0.25f, bx * half.X, bu * 0.25f, bz * half.Y, CityPlanColours.Rgb(0xa89c88), Pack(CityStyle.Plain, 3, 0, 0), true, false);
            Box(cb, bedFoot + bu * 0.27f, bx * (half.X - 0.3f), bu * 0.25f, bz * (half.Y - 0.3f), CityPlanColours.Rgb(0x4d6b35), Pack(CityStyle.Leaves, 9, 0, 0), false, false);
            for (var t = -1; t <= 1; t++)
                Tree(centre + new Vector2(t * 5f, 0), ref d);
        }
        // Round the fountain: benches facing the water, and a ring of trees further out.
        for (var i = 0; i < 12; i++)
        {
            var a = i * MathF.Tau / 12;
            var at = new Vector2(MathF.Cos(a), MathF.Sin(a)) * 13.5f;
            Bench(at, MathF.Abs(MathF.Cos(a)) < 0.7f);
        }
        for (var i = 0; i < 16; i++)
        {
            var a = (i + 0.5f) * MathF.Tau / 16;
            var at = new Vector2(MathF.Cos(a), MathF.Sin(a)) * 30f;
            if (!InHole(at, 3f))
                Tree(at, ref d);
        }
        // Cafe terraces: tables under umbrellas in four clusters between the beds, a kiosk serving each.
        foreach (var (qx, qz) in new[] { (0f, -1f), (0f, 1f), (-1f, 0f), (1f, 0f) })
        {
            var centre = new Vector2(qx * MathF.Min(52f, (max.X - min.X) * 0.5f - 22f), qz * MathF.Min(70f, (max.Y - min.Y) * 0.5f - 22f));
            Kiosk(centre + new Vector2(qx * 6f, qz * 6f), ref d);
            for (var t = 0; t < 6; t++)
            {
                var off = new Vector2((t % 3 - 1) * 4.2f, (t / 3 - 0.5f) * 4.5f);
                if (qx != 0)
                    off = new Vector2(off.Y, off.X);
                CafeTable(centre + off, ref d);
            }
        }
        // Lamps standing across the open paving, in a ring round the fountain.
        for (var i = 0; i < 10; i++)
        {
            var a = i * MathF.Tau / 10 + 0.3f;
            var at = new Vector2(MathF.Cos(a), MathF.Sin(a)) * 21f;
            if (!InHole(at, 2f))
                Lamp(at, -Vector2.Normalize(at));
        }
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

    /// <summary>A cafe table: a round top on a post, four stools, an umbrella over it in a cheerful colour.</summary>
    private void CafeTable(Vector2 at, ref CityPlan.Dice d)
    {
        var c = At(at);
        var y = RoadLift + Kerb;
        var metal = Pack(CityStyle.Metal, 0, 0, 0);
        Prism(c, at, y + 0.7f, y + 0.75f, 0.45f, 0.45f, 8, CityPlanColours.Rgb(0xd8d2c4), Pack(CityStyle.Plain, 20, 0, 0), true);
        Prism(c, at, y, y + 0.7f, 0.04f, 0.04f, 4, Steel, metal, false);
        for (var k = 0; k < 4; k++)
        {
            var a = k * MathF.PI * 0.5f + 0.4f;
            var sp = at + new Vector2(MathF.Cos(a), MathF.Sin(a)) * 0.75f;
            Prism(c, sp, y, y + 0.45f, 0.17f, 0.17f, 6, Wood, Pack(CityStyle.Plain, 6, 0, 0), false);
        }
        uint colour = CityPlanColours.Rgb(d.Pick(4) switch { 0 => 0xc8483c, 1 => 0xe8d8b0, 2 => 0x2f7a5a, _ => 0xd8a030 });
        Prism(c, at, y + 0.75f, y + 2.3f, 0.03f, 0.03f, 4, Steel, metal, false);
        Prism(c, at, y + 2.05f, y + 2.6f, 1.5f, 0.05f, 8, colour, Pack(CityStyle.Plain, 21, 0, 0), false, turn: d.Range(0, 1));
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

    /// <summary>A sign box's parameters: its word, the width and height of its big faces (the shader centres the word).</summary>
    private static uint SignBox(int word, float width, float height) => Pack(CityStyle.Neon, 128u + (uint)word, width * 10, height * 100);

    // ---- The market hall ------------------------------------------------------------------------------------------------

    /// <summary>The market's aisles (along x) are this far apart, the first this far in from the block's south edge; its end
    /// aisles (along z) this far in from its east and west edges. CityLife's shoppers walk these.</summary>
    public const float MarketStep = 7f, MarketFirst = 4f, MarketEnd = 2.6f, MarketRoof = 9f;

    private static readonly uint[] Awnings = [CityPlanColours.Rgb(0xc8483c), CityPlanColours.Rgb(0x2f7a5a), CityPlanColours.Rgb(0xd8a030), CityPlanColours.Rgb(0x3a5a9a), CityPlanColours.Rgb(0xe0e0d8), CityPlanColours.Rgb(0x8a3a7a)];

    /// <summary>A covered market on a block: a steel roof on columns over rows of stalls, each with its counter, awning and
    /// lamp and its wares, aisles between them, open all round; a lit sign over the side facing the square.</summary>
    private void Market(CityBlock blk)
    {
        var d = new CityPlan.Dice((uint)_plan.Spec.Seed * 3571u + 11);
        Vector2 min = blk.Min, max = blk.Max, mid = (min + max) * 0.5f;
        var y0 = RoadLift + Kerb;
        var metal = Pack(CityStyle.Metal, 0, 0, 0);
        // The roof: a slab over the whole block (in chunk-sized pieces), its edge a deep fascia, lamps under it.
        for (var x = min.X; x < max.X - 0.01f; x += 32)
            for (var z = min.Y; z < max.Y - 0.01f; z += 32)
            {
                var a = new Vector2(x, z);
                var b = Vector2.Min(max, a + new Vector2(32));
                var c = At((a + b) * 0.5f);
                var (ax, az, up) = Axes((a + b) * 0.5f);
                var centre = _plan.Ground((a + b) * 0.5f, y0 + MarketRoof + 0.3f);
                Box(c, centre, ax * ((b.X - a.X) * 0.5f), up * 0.3f, az * ((b.Y - a.Y) * 0.5f), CityPlanColours.Rgb(0x3d4a52), Pack(CityStyle.Plain, 17, 0, 0), true, false);
            }
        // Over the ceiling a sawtooth roof: each 8 m bay a slope rising to the east, then a lit glazed face back down
        // (north lights, the way a market hall or a mill lets in light without the sun).
        for (var x = min.X; x < max.X - 4; x += 8)
        {
            var x1 = MathF.Min(x + 8, max.X);
            for (var z = min.Y; z < max.Y - 0.01f; z += 16)
            {
                var z1 = MathF.Min(z + 16, max.Y);
                var mid0 = new Vector2((x + x1) * 0.5f, (z + z1) * 0.5f);
                var c = At(mid0);
                var (ax, az, up) = Axes(mid0);
                float lo = y0 + MarketRoof + 0.6f, hi = y0 + MarketRoof + 3.0f;
                Vector3 p00 = _plan.Ground(new Vector2(x, z), lo), p01 = _plan.Ground(new Vector2(x, z1), lo);
                Vector3 p10 = _plan.Ground(new Vector2(x1, z), hi), p11 = _plan.Ground(new Vector2(x1, z1), hi);
                var slopeN = Vector3.Normalize(Vector3.Cross(p01 - p00, p10 - p00));
                if (Vector3.Dot(slopeN, up) < 0)
                    slopeN = -slopeN;
                Quad(c, false, p00, p10, p11, p01, slopeN, slopeN, slopeN, slopeN, new Vector2(x, z), new Vector2(x1, z), new Vector2(x1, z1), new Vector2(x, z1),
                    CityPlanColours.Rgb(0x4a565e), Pack(CityStyle.Roof, 0, 0, 0), slopeN);
                // The glazing, lit from inside, facing west (seen from the square's side as warm strips).
                Vector3 g0 = _plan.Ground(new Vector2(x1, z), lo), g1 = _plan.Ground(new Vector2(x1, z1), lo);
                Quad(c, false, g0, g1, p11, p10, ax, ax, ax, ax, new Vector2(z, 0), new Vector2(z1, 0), new Vector2(z1, 2.4f), new Vector2(z, 2.4f),
                    CityPlanColours.Rgb(0x9a7848), Pack(CityStyle.Lamp, 0, 0, 0), ax);
                Quad(c, false, g0, g1, p11, p10, -ax, -ax, -ax, -ax, new Vector2(z, 0), new Vector2(z1, 0), new Vector2(z1, 2.4f), new Vector2(z, 2.4f),
                    CityPlanColours.Rgb(0x9a7848), Pack(CityStyle.Lamp, 0, 0, 0), -ax);
            }
        }
        // Columns down each end aisle's outer side and along the stall rows' middles.
        for (var z = min.Y + 1; z <= max.Y - 1 + 0.01f; z += 8)
            foreach (var x in new[] { min.X + 0.8f, max.X - 0.8f })
                Column(new Vector2(x, z));
        // Stall rows between the aisles: two rows back to back, facing the aisle on each side.
        var zA = min.Y + MarketFirst;
        while (zA + MarketStep < max.Y - 2)
        {
            var rowMid = zA + MarketStep * 0.5f;
            for (var x = min.X + MarketEnd + 2.5f; x < max.X - MarketEnd - 3.5f; x += 3.4f)
                foreach (var face in new[] { -1f, 1f })
                    Stall(new Vector2(x + 1.6f, rowMid + face * 1.05f), face, ref d);
            for (var x = min.X + 10; x < max.X - 8; x += 16)
                Column(new Vector2(x, rowMid));
            zA += MarketStep;
        }
        // Lamps hanging over the aisles.
        for (var z = min.Y + MarketFirst; z < max.Y - 1; z += MarketStep)
            for (var x = min.X + 6; x < max.X - 4; x += 8)
            {
                var p = new Vector2(x, z);
                var c = At(p);
                var (ax, az, up) = Axes(p);
                Box(c, _plan.Ground(p, y0 + MarketRoof - 1.2f), ax * 0.35f, up * 0.12f, az * 0.35f, LampColor, Pack(CityStyle.Lamp, 0, 0, 0), false);
                Box(c, _plan.Ground(p, y0 + MarketRoof - 0.55f), ax * 0.02f, up * 0.55f, az * 0.02f, Steel, metal, false);
            }
        // The sign over the side facing the square (the south side).
        {
            var p = new Vector2(mid.X, min.Y - 0.4f);
            var c = At(p);
            var (ax, az, up) = Axes(p);
            Box(c, _plan.Ground(p, y0 + MarketRoof + 1.6f), ax * 9f, up * 1.0f, az * 0.15f, CityPlanColours.Rgb(0xffd060), SignBox(CitySigns.MarketWord, 18f, 2f), false, false);
        }

        void Column(Vector2 p)
        {
            var c = At(p);
            var (ax, az, up) = Axes(p);
            Box(c, _plan.Ground(p, y0 + MarketRoof * 0.5f), ax * 0.18f, up * (MarketRoof * 0.5f), az * 0.18f, Steel, metal, true, false);
        }
    }

    /// <summary>A market stall: a counter facing <paramref name="face"/> (+z or -z), a shelf behind, an awning on two posts,
    /// a lamp under it and its wares on the counter.</summary>
    private void Stall(Vector2 at, float face, ref CityPlan.Dice d)
    {
        var c = At(at);
        var (ax, az, up) = Axes(at);
        var f = az * face;
        var foot = _plan.Ground(at, RoadLift + Kerb);
        var wood = Pack(CityStyle.Plain, 6, 0, 0);
        Box(c, foot + f * 0.45f + up * 0.5f, ax * 1.4f, up * 0.5f, az * 0.28f, Wood, wood, true);
        Box(c, foot - f * 0.55f + up * 0.9f, ax * 1.4f, up * 0.9f, az * 0.18f, CityPlanColours.Rgb(0x5a4632), wood, true);
        var awning = Awnings[d.Pick(Awnings.Length)];
        Box(c, foot + f * 0.35f + up * 2.45f, ax * 1.55f, up * 0.05f, az * 0.9f, awning, Pack(CityStyle.Plain, 18, 0, 0), false);
        foreach (var sx in new[] { -1f, 1f })
            Box(c, foot + ax * (1.45f * sx) + f * 1.1f + up * 1.2f, ax * 0.03f, up * 1.2f, az * 0.03f, Steel, Pack(CityStyle.Metal, 0, 0, 0), false);
        Box(c, foot + f * 0.4f + up * 2.3f, ax * 0.5f, up * 0.05f, az * 0.08f, LampColor, Pack(CityStyle.Lamp, 0, 0, 0), false);
        // Wares: a few crates and heaps of colour along the counter.
        var n = 3 + d.Pick(3);
        for (var k = 0; k < n; k++)
        {
            var x = -1.2f + 2.4f * (k + 0.5f) / n;
            var colour = CityPlanColours.Rgb(d.Pick(6) switch { 0 => 0xd04030, 1 => 0xe0a020, 2 => 0x50a040, 3 => 0xe0d0b0, 4 => 0x8a5a3a, _ => 0x6a3a8a });
            Box(c, foot + ax * x + f * 0.5f + up * 1.1f, ax * 0.18f, up * d.Range(0.06f, 0.16f), az * 0.16f, colour, Pack(CityStyle.Plain, 19, 0, 0), false);
        }
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
            // The station's name on a board hanging under the canopy, over the platform's middle.
            {
                var (p, sv) = RailAt(r, k);
                var c = At(p);
                var up = _plan.DirOf(p);
                var sideW = Vector3.Normalize(_plan.Ground(p + sv) - _plan.Ground(p));
                var along = Vector3.Cross(up, sideW);
                var board = _plan.Ground(p + sv * side * 6.2f, r.Height + 1.0f + 3.0f);
                var word = CitySigns.LoopWord(Array.IndexOf(StationPoints(r), k));
                Box(c, board, along * 2.2f, up * 0.35f, sideW * 0.06f, CityPlanColours.Rgb(0x1f3a6a), SignBox(word, 4.4f, 0.7f), false);
            }
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

    // ---- Holes in the street (the metro's stairs) ------------------------------------------------------------------------

    private readonly List<(Vector2 Min, Vector2 Max)> _holes = new();

    /// <summary>A rectangle less the holes that cut it: the pieces left (all of it when no hole touches it).</summary>
    private List<(Vector2 Min, Vector2 Max)> Uncut(Vector2 min, Vector2 max)
    {
        var parts = new List<(Vector2 Min, Vector2 Max)> { (min, max) };
        foreach (var (hmin, hmax) in _holes)
        {
            var next = new List<(Vector2 Min, Vector2 Max)>();
            foreach (var (a, b) in parts)
            {
                if (hmax.X <= a.X || hmin.X >= b.X || hmax.Y <= a.Y || hmin.Y >= b.Y)
                {
                    next.Add((a, b));
                    continue;
                }
                float x0 = MathF.Max(a.X, hmin.X), x1 = MathF.Min(b.X, hmax.X);
                if (hmin.X > a.X)
                    next.Add((a, new Vector2(hmin.X, b.Y)));
                if (hmax.X < b.X)
                    next.Add((new Vector2(hmax.X, a.Y), b));
                if (hmin.Y > a.Y)
                    next.Add((new Vector2(x0, a.Y), new Vector2(x1, hmin.Y)));
                if (hmax.Y < b.Y)
                    next.Add((new Vector2(x0, hmax.Y), new Vector2(x1, b.Y)));
            }
            parts = next;
        }
        return parts;
    }

    /// <summary>A sheet and its slab under it, less the holes.</summary>
    private void PavedSlab(Chunk c, Vector2 min, Vector2 max, float y, uint color, uint par, Func<Vector2, Vector2> uv)
    {
        foreach (var (a, b) in Uncut(min, max))
        {
            if (b.X - a.X < 0.01f || b.Y - a.Y < 0.01f)
                continue;
            Sheet(c, a, b, y, color, par, uv);
            SlabCollision(c, a, b, y);
        }
    }

    private bool InHole(Vector2 p, float margin)
    {
        foreach (var (a, b) in _holes)
            if (p.X > a.X - margin && p.X < b.X + margin && p.Y > a.Y - margin && p.Y < b.Y + margin)
                return true;
        return false;
    }

    // ---- The metro -----------------------------------------------------------------------------------------------------

    private static readonly uint Tile = CityPlanColours.Rgb(0xe6e1d3), TileBand = CityPlanColours.Rgb(0x1f5a8a), Soffit = CityPlanColours.Rgb(0x9a968c), Tunnel = CityPlanColours.Rgb(0x141516);

    /// <summary>An upright wall between two plan points, from y0 to y1 m, facing <paramref name="facing"/> (draped along the
    /// curve every few metres); solid unless said.</summary>
    private void Wall(Vector2 p, Vector2 q, float y0, float y1, Vector3 facing, uint color, uint par, bool solid = true, float u0 = 0)
    {
        var len = Vector2.Distance(p, q);
        var n = Math.Max(1, (int)MathF.Ceiling(len / 8f));
        for (var k = 0; k < n; k++)
        {
            Vector2 a = Vector2.Lerp(p, q, k / (float)n), b = Vector2.Lerp(p, q, (k + 1) / (float)n);
            var c = At((a + b) * 0.5f);
            float ua = u0 + len * k / n, ub = u0 + len * (k + 1) / n;
            // (x runs to the right of a viewer facing the wall, so the station's name reads the right way round)
            if (Vector2.Dot(q - p, new Vector2(Vector3.Dot(Vector3.Cross(_plan.DirOf(a), facing), _plan.East), Vector3.Dot(Vector3.Cross(_plan.DirOf(a), facing), _plan.North))) < 0)
                (ua, ub) = (u0 + len - len * k / n, u0 + len - len * (k + 1) / n);
            Vector3 a0 = _plan.Ground(a, y0), b0 = _plan.Ground(b, y0), b1 = _plan.Ground(b, y1), a1 = _plan.Ground(a, y1);
            Quad(c, false, a0, b0, b1, a1, facing, facing, facing, facing, new Vector2(ua, y0), new Vector2(ub, y0), new Vector2(ub, y1), new Vector2(ua, y1), color, par, facing);
            if (solid)
            {
                ColTri(c, a0, b0, b1, facing);
                ColTri(c, a0, b1, a1, facing);
            }
        }
    }

    /// <summary>A flat ceiling over a rectangle of the plan at y, seen from below (no collision: the ground is over it).</summary>
    private void Ceiling(Vector2 min, Vector2 max, float y, uint color, uint par)
    {
        foreach (var (a, b) in Uncut(min, max))
        {
            if (b.X - a.X < 0.01f || b.Y - a.Y < 0.01f)
                continue;
            var c = At((a + b) * 0.5f);
            var first = c.V.Count;
            var nx = Math.Max(1, (int)MathF.Ceiling((b.X - a.X) / 8f));
            var nz = Math.Max(1, (int)MathF.Ceiling((b.Y - a.Y) / 8f));
            for (var j = 0; j <= nz; j++)
                for (var i = 0; i <= nx; i++)
                {
                    var p = new Vector2(a.X + (b.X - a.X) * i / nx, a.Y + (b.Y - a.Y) * j / nz);
                    Vert(c, _plan.Ground(p, y), -_plan.DirOf(p), p, color, par);
                }
            var down = -_plan.DirOf((a + b) * 0.5f);
            for (var j = 0; j < nz; j++)
                for (var i = 0; i < nx; i++)
                {
                    uint i0 = (uint)(first + j * (nx + 1) + i), i1 = i0 + 1, i2 = i0 + (uint)(nx + 1), i3 = i2 + 1;
                    // (both windings: a ceiling seen from under it, whichever way the sheet's axes turn here)
                    Tri(c, false, i0, i3, i1);
                    Tri(c, false, i0, i2, i3);
                    Tri(c, false, i0, i1, i3);
                    Tri(c, false, i0, i3, i2);
                    _ = down;
                }
        }
    }

    private void Metro(CityMetro m)
    {
        var tiles = Pack(CityStyle.MetroWall, 0, 0, 0);
        var floor = Pack(CityStyle.Pavement, 2, 0, 0, true);
        var plain = Pack(CityStyle.Plain, 14, 0, 0, true);
        float top = -m.Depth, bed = -m.Depth - CityMetro.Bed, ceil = -m.Depth + CityMetro.Ceiling;
        var x0 = m.X;
        for (var si = 0; si < m.Stations.Length; si++)
        {
            var z = m.Stations[si];
            tiles = Pack(CityStyle.MetroWall, 0, CitySigns.MetroWord(si), 0);
            var (ax, az, _) = Axes(new Vector2(x0, z));
            float zl = z - CityMetro.HallZ, zh = z + CityMetro.HallZ;
            // The platforms: concrete with a stone top, and their faces down to the track bed.
            foreach (var sx in new[] { -1f, 1f })
            {
                float xa = x0 + sx * CityMetro.Edge, xb = x0 + sx * CityMetro.HallX;
                Vector2 pmin = new(MathF.Min(xa, xb), zl), pmax = new(MathF.Max(xa, xb), zh);
                Sheet(At(new Vector2((xa + xb) * 0.5f, z)), pmin, pmax, top, PaveColor, floor, p => new Vector2(p.Y - z, p.X - x0));
                SlabCollision(At(new Vector2((xa + xb) * 0.5f, z)), pmin, pmax, top, bed - 0.5f);
                // The yellow line along the edge.
                Vector2 ymin = new(MathF.Min(xa, xa + sx * 0.5f), zl), ymax = new(MathF.Max(xa, xa + sx * 0.5f), zh);
                Sheet(At(new Vector2(xa, z)), ymin, ymax, top + 0.008f, CityPlanColours.Rgb(0xd8b02a), Pack(CityStyle.Plain, 11, 0, 0, true), p => p, true);
                Wall(new Vector2(xa, zl), new Vector2(xa, zh), bed, top, -sx * ax, Concrete, plain);
                // The hall's side wall: tiles, a coloured band at head height (the line's colour), the station's frieze.
                Wall(new Vector2(xb, zl), new Vector2(xb, zh), top - 0.02f, ceil, -sx * ax, Tile, tiles, true, zl);
                // Benches against it.
                for (var bz = zl + 12; bz < zh - 10; bz += 16)
                {
                    var cc = At(new Vector2(xb - sx * 0.6f, bz));
                    var foot = _plan.Ground(new Vector2(xb - sx * 0.6f, bz), top);
                    var upv = _plan.DirOf(new Vector2(xb, bz));
                    Box(cc, foot + upv * 0.42f, ax * 0.25f, upv * 0.05f, az * 1.0f, Wood, Pack(CityStyle.Plain, 6, 0, 0, true), true);
                    Box(cc, foot + upv * 0.19f, ax * 0.2f, upv * 0.19f, az * 0.08f, Steel, Pack(CityStyle.Metal, 0, 0, 0, true), false);
                }
                // Strip lights over the platform.
                for (var lz = zl + 6; lz < zh - 4; lz += 7)
                {
                    var lx = x0 + sx * 8f;
                    var cc = At(new Vector2(lx, lz));
                    var upv = _plan.DirOf(new Vector2(lx, lz));
                    Box(cc, _plan.Ground(new Vector2(lx, lz), ceil - 0.08f), ax * 0.12f, upv * 0.05f, az * 1.4f, CityPlanColours.Rgb(0xf4f8ff), Pack(CityStyle.Lamp, 1, 0, 0, true), false);
                }
            }
            // The track bed between the platforms, with its rails.
            var bmin = new Vector2(x0 - CityMetro.Edge, zl - CityMetro.Stub);
            var bmax = new Vector2(x0 + CityMetro.Edge, zh + CityMetro.Stub);
            Sheet(At(new Vector2(x0, z)), bmin, bmax, bed, Ballast, Pack(CityStyle.Rail, 0, 0, 0, true), p => new Vector2(p.Y, p.X - x0));
            SlabCollision(At(new Vector2(x0, z)), bmin, bmax, bed, bed - 1.0f);
            // The ceiling, less the flights' shafts.
            var openings = _holes.Count;
            foreach (var (ft, fd) in m.Flights(z))
            {
                Vector2 fa = ft + new Vector2(-CityMetro.StairW * 0.5f - 0.05f, 0), fb = ft + new Vector2(CityMetro.StairW * 0.5f + 0.05f, fd * m.StairRun);
                _holes.Add((Vector2.Min(fa, fb), Vector2.Max(fa, fb)));
            }
            Ceiling(new Vector2(x0 - CityMetro.HallX, zl), new Vector2(x0 + CityMetro.HallX, zh), ceil, Soffit, Pack(CityStyle.MetroWall, 2, 0, 0, true));
            _holes.RemoveRange(openings, _holes.Count - openings);
            // The ends: tiled walls with the tunnel mouth over the tracks; the tunnel runs on, dark, until it bends out of sight.
            foreach (var sz in new[] { -1f, 1f })
            {
                var ze = z + sz * CityMetro.HallZ;
                var outN = -sz * az;
                foreach (var sx in new[] { -1f, 1f })
                    Wall(new Vector2(x0 + sx * CityMetro.Edge, ze), new Vector2(x0 + sx * CityMetro.HallX, ze), bed, ceil, outN, Tile, tiles);
                Wall(new Vector2(x0 - CityMetro.Edge, ze), new Vector2(x0 + CityMetro.Edge, ze), top + 3.6f, ceil, outN, Tile, tiles);
                var zt = ze + sz * CityMetro.Stub;
                Wall(new Vector2(x0 - CityMetro.Edge, ze), new Vector2(x0 - CityMetro.Edge, zt), bed, top + 3.6f, ax, Tunnel, Pack(CityStyle.Plain, 16, 0, 0, true));
                Wall(new Vector2(x0 + CityMetro.Edge, ze), new Vector2(x0 + CityMetro.Edge, zt), bed, top + 3.6f, -ax, Tunnel, Pack(CityStyle.Plain, 16, 0, 0, true));
                Ceiling(new Vector2(x0 - CityMetro.Edge, MathF.Min(ze, zt)), new Vector2(x0 + CityMetro.Edge, MathF.Max(ze, zt)), top + 3.6f, Tunnel, Pack(CityStyle.Plain, 16, 0, 0, true));
                Wall(new Vector2(x0 - CityMetro.Edge, zt), new Vector2(x0 + CityMetro.Edge, zt), bed, top + 3.6f, outN, Tunnel, Pack(CityStyle.Plain, 16, 0, 0, true));
            }
            // The flights from the pavements, each in its shaft.
            foreach (var (stairTop, down) in m.Flights(z))
                MetroStairs(m, stairTop, down);
        }
    }

    /// <summary>A flight from the pavement down to the platform, along z towards the hall's middle, in a lined shaft with a
    /// railing round its opening and a lamp post with the line's sign.</summary>
    private void MetroStairs(CityMetro m, Vector2 top, float down)
    {
        var foot = RoadLift + Kerb;
        var run = m.StairRun;
        float ceil = -m.Depth + CityMetro.Ceiling, under = -0.35f;
        const float W = CityMetro.StairW;
        var dirZ = new Vector2(0, down);
        var c = At(top + dirZ * run * 0.5f);
        var (ax, az, up) = Axes(top);
        var fwd = az * down;
        Vector3 G(float along, float x, float y) => _plan.Ground(top + dirZ * along + new Vector2(x, 0), y);
        var stairs = Pack(CityStyle.Stairs, 0, 0, 0, true);
        var plain = Pack(CityStyle.Plain, 14, 0, 0, true);
        Vector3 t0a = G(0, -W / 2, foot), t0b = G(0, W / 2, foot), t1a = G(run, -W / 2, -m.Depth), t1b = G(run, W / 2, -m.Depth);
        Vector3 s0a = G(0, -W / 2, foot - 0.6f), s0b = G(0, W / 2, foot - 0.6f), s1a = G(run, -W / 2, -m.Depth - 0.4f), s1b = G(run, W / 2, -m.Depth - 0.4f);
        var slopeN = Vector3.Normalize(Vector3.Cross(t1a - t0a, t0b - t0a));
        if (Vector3.Dot(slopeN, up) < 0)
            slopeN = -slopeN;
        var slopeLen = Vector3.Distance(t0a, t1a);
        Quad(c, false, t0a, t0b, t1b, t1a, slopeN, slopeN, slopeN, slopeN, new Vector2(-W / 2, 0), new Vector2(W / 2, 0), new Vector2(W / 2, slopeLen), new Vector2(-W / 2, slopeLen), PaveColor, stairs, slopeN);
        ColTri(c, t0a, t0b, t1b, slopeN);
        ColTri(c, t0a, t1b, t1a, slopeN);
        ColTri(c, s0a, s0b, s1b, -slopeN);
        ColTri(c, s0a, s1b, s1a, -slopeN);
        ColTri(c, t1a, t1b, s1b, fwd);
        ColTri(c, t1a, s1b, s1a, fwd);
        // The shaft: tiled walls each side from the hall's ceiling (or the stair, below it) up to the street, a soffit under
        // the pavement past the opening, and a wall closing its lower end.
        foreach (var sx in new[] { -1f, 1f })
        {
            var x = sx * (W / 2 + 0.02f);
            var p = top + new Vector2(x, 0);
            var q = top + new Vector2(x, 0) + dirZ * run;
            Wall(p, q, MathF.Min(ceil, -m.Depth) - 0.2f, foot + 0.02f, -sx * ax, Tile, Pack(CityStyle.MetroWall, 1, 0, 0, true));
            // A handrail along it.
            Vector3 h0 = G(0, x - sx * 0.08f, foot + 0.95f), h1 = G(run, x - sx * 0.08f, -m.Depth + 0.95f);
            var mid = (h0 + h1) * 0.5f;
            var axis = (h1 - h0) * 0.5f;
            Box(c, mid, axis, Vector3.Normalize(Vector3.Cross(axis, ax)) * 0.03f, ax * 0.03f, Steel, Pack(CityStyle.Metal, 0, 0, 0, true), false);
        }
        var under0 = top + dirZ * CityMetro.Opening;
        var under1 = top + dirZ * run;
        Ceiling(Vector2.Min(under0, under1) - new Vector2(W / 2 + 0.05f, 0), Vector2.Max(under0, under1) + new Vector2(W / 2 + 0.05f, 0), under, Soffit, Pack(CityStyle.MetroWall, 2, 0, 0, true));
        Wall(under1 + new Vector2(-W / 2, 0), under1 + new Vector2(W / 2, 0), ceil, under, -fwd, Tile, Pack(CityStyle.MetroWall, 1, 0, 0, true), false);
        Wall(top + new Vector2(-W / 2, 0), top + new Vector2(W / 2, 0), -1.2f, foot + 0.02f, fwd, Tile, Pack(CityStyle.MetroWall, 1, 0, 0, true));
        // Railings round the opening on the street, and the line's lamp post at its head.
        var rail = Pack(CityStyle.Metal, 0, 0, 0);
        var railCol = CityPlanColours.Rgb(0x2c4a3a);
        foreach (var sx in new[] { -1f, 1f })
        {
            var a = G(0, sx * (W / 2 + 0.1f), foot + 0.55f);
            var b = G(CityMetro.Opening, sx * (W / 2 + 0.1f), foot + 0.55f);
            Box(c, (a + b) * 0.5f, (b - a) * 0.5f, up * 0.55f, ax * 0.04f, railCol, rail, true);
        }
        Box(c, G(-0.1f, 0, foot + 0.55f), ax * (W / 2 + 0.14f), up * 0.55f, fwd * 0.04f, railCol, rail, true);
        var post = G(-0.5f, -W / 2 - 0.5f, foot);
        Box(c, post + up * 1.6f, ax * 0.07f, up * 1.6f, az * 0.07f, railCol, rail, true);
        Box(c, post + up * 3.45f, ax * 0.32f, up * 0.32f, az * 0.32f, CityPlanColours.Rgb(0x4ab0ff), Pack(CityStyle.Lamp, 2, 0, 0), false);
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
            var underFirst = idx.Count;
            foreach (var i in c.Under)
                idx.Add(baseV + i);
            var first = idx.Count;
            foreach (var i in c.Main)
                idx.Add(baseV + i);
            foreach (var i in c.Detail)
                idx.Add(baseV + i);
            var centre = (c.Min + c.Max) * 0.5f;
            chunks.Add(new CityChunk { Origin = c.Origin, Centre = centre, Radius = (c.Max - c.Min).Length() * 0.5f, First = first, Main = c.Main.Count, Detail = c.Detail.Count, UnderFirst = underFirst, Under = c.Under.Count });
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
