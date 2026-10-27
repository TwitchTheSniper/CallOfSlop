using System.Globalization;
using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The contoured shapes a part's model box may have besides a plain box (Parts/README.md, "Shapes"): cylinder, tube,
/// wedge, bevel, rounded, capsule, sphere and arc. Each one is a small triangle mesh that fits inside its box (the unit
/// cube scaled by the box's size), so everything that treats a gun's pieces as boxes (bounds, the optic seat, the
/// workbench's framing and picking, the cart's collision) keeps working unchanged; only drawing uses the real shape.
///
/// A mesh is generated once per distinct (shape, settings, size) when a part loads and is shared by every part and build
/// that uses it (a shape id on the box, <see cref="PartBox.ShapeId"/>, then on the built gun's <see cref="GunPart.Shape"/>
/// and the scene node's <see cref="Renderable.Shape"/>). The renderer uploads new meshes into its box vertex buffer the
/// first time one is drawn (<see cref="Version"/>), and draws a shaped piece with one draw call like a box. Nothing is
/// allocated per frame.
///
/// Vertices are stored in unit-cube space (metres divided by the size) and normals as the true normal divided by the
/// size, so the box's own scale turns them back into the right directions in the shader (mat3(model) * normal).
/// tools/parts/shapes.py mirrors this file for the contact sheet and the connectivity checker.
/// </summary>
public static class PartShapes
{
    /// <summary>The contoured shapes (box, rod and plate are the plain ones).</summary>
    public static readonly string[] Names = ["cylinder", "tube", "wedge", "bevel", "rounded", "capsule", "sphere", "arc"];

    /// <summary>Settings each contoured shape takes (any other key on a box with that shape is an error).</summary>
    public static string[] SettingsOf(string shape) => shape switch
    {
        "cylinder" or "tube" => ["segments", "inner", "ends", "taper", "shift"],
        "wedge" => ["taper", "shift"],
        "bevel" or "rounded" => ["radius", "edges", "segments"],
        "capsule" or "sphere" => ["segments"],
        "arc" => ["segments", "inner", "angle", "dir"],
        _ => [],
    };

    private const int FloatsPerVertex = 6;

    private static readonly object Gate = new();
    private static readonly Dictionary<string, ushort> Ids = new(StringComparer.Ordinal);
    private static readonly List<float[]> Meshes = [null!]; // id 0 = the renderer's own unit cube
    private static readonly List<Matrix4x4[]> ProxyList = [[Matrix4x4.Identity]];

    /// <summary>Goes up whenever a new mesh is added (the renderer re-uploads its vertex buffer when it changes).</summary>
    public static int Version { get; private set; }

    /// <summary>Number of shape ids so far (id 0 is the cube).</summary>
    public static int Count
    {
        get
        {
            lock (Gate)
                return Meshes.Count;
        }
    }

    /// <summary>The shape id for box <paramref name="b"/>: 0 for box, rod and plate; otherwise its (shared) mesh.</summary>
    public static ushort For(PartBox b)
    {
        if (Array.IndexOf(Names, b.Shape) < 0)
            return 0;
        var key = KeyOf(b);
        lock (Gate)
        {
            if (Ids.TryGetValue(key, out var id))
                return id;
        }
        var (mesh, proxies) = Generate(b);
        lock (Gate)
        {
            if (Ids.TryGetValue(key, out var id))
                return id;
            if (Meshes.Count >= ushort.MaxValue)
                throw new WeaponFileException("too many different part shapes (65535)");
            id = (ushort)Meshes.Count;
            Meshes.Add(mesh);
            ProxyList.Add(proxies);
            Ids[key] = id;
            Version++;
            return id;
        }
    }

    /// <summary>The vertices (position, normal; unit-cube space) of shape <paramref name="id"/> (empty for the cube).</summary>
    public static float[] Mesh(int id)
    {
        lock (Gate)
            return id > 0 && id < Meshes.Count ? Meshes[id] : [];
    }

    /// <summary>Triangles drawn for a box with shape id <paramref name="id"/> (12 for a plain box).</summary>
    public static int Triangles(int id) => id == 0 ? 12 : Mesh(id).Length / (FloatsPerVertex * 3);

    /// <summary>
    /// Boxes inside the shape (unit-cube space; multiply by the piece's world matrix) that stand for its volume when
    /// checking that a model's pieces touch (the self-test "Model connectivity", tools/parts/floaters.py). The plain
    /// cube for id 0.
    /// </summary>
    public static Matrix4x4[] Proxies(int id)
    {
        lock (Gate)
            return id > 0 && id < ProxyList.Count ? ProxyList[id] : ProxyList[0];
    }

    /// <summary>Copies every mesh after the cube into <paramref name="into"/> and records where each one starts (vertex index after the cube's 36), plus where the last one ends.</summary>
    internal static int CopyAll(List<float> into, List<int> first)
    {
        lock (Gate)
        {
            first.Clear();
            first.Add(0);
            var at = 36;
            for (var i = 1; i < Meshes.Count; i++)
            {
                first.Add(at);
                into.AddRange(Meshes[i]);
                at += Meshes[i].Length / FloatsPerVertex;
            }
            first.Add(at); // the end of the last one
            return Version;
        }
    }

    private static string F(float v) => v.ToString("0.#####", CultureInfo.InvariantCulture);

    private static string KeyOf(PartBox b) =>
        $"{b.Shape}|{F(b.Size.X)},{F(b.Size.Y)},{F(b.Size.Z)}|s{b.Segments}|i{F(b.Inner)}|e{b.Ends}|t{F(b.Taper.X)},{F(b.Taper.Y)}|h{F(b.Shift.X)},{F(b.Shift.Y)}"
        + $"|r{F(b.Radius)}|g{b.Edges}|a{F(b.Angle)}|d{F(b.Dir)}";

    // ---- Settings with their defaults ----------------------------------------------------------------------------

    /// <summary>Segments round a cylinder, tube, capsule or sphere (default 12), along an arc (one per 15 degrees), per rounded corner (4).</summary>
    public static int SegmentsOf(PartBox b) => b.Segments > 0
        ? b.Segments
        : b.Shape switch
        {
            "arc" => Math.Clamp((int)MathF.Ceiling(AngleOf(b) / 15f), 2, 24),
            "rounded" => 4,
            "bevel" => 1,
            _ => 12,
        };

    /// <summary>Inner size as a fraction of the outer (a tube's bore, an arc's inside): tube 0.7, arc 0.6, cylinder 0 (solid).</summary>
    public static float InnerOf(PartBox b) => b.Inner >= 0 ? b.Inner : b.Shape switch { "tube" => 0.7f, "arc" => 0.6f, _ => 0f };

    /// <summary>Which ends are capped: "closed", "open", "front" (-Z) or "back" (+Z). Cylinder: closed; tube: open (rims only).</summary>
    public static string EndsOf(PartBox b) => b.Ends != "" ? b.Ends : b.Shape == "tube" ? "open" : "closed";

    /// <summary>The front (-Z) end's size as a fraction of the back's: wedge [1, 0.3], cylinder [1, 1].</summary>
    public static Vector2 TaperOf(PartBox b) => b.Taper.X >= 0 ? b.Taper : b.Shape == "wedge" ? new Vector2(1f, 0.3f) : Vector2.One;

    /// <summary>A bevel's or rounding's size, as a fraction of the smallest side it cuts into (default 0.25).</summary>
    public static float RadiusOf(PartBox b) => b.Radius > 0 ? b.Radius : 0.25f;

    /// <summary>Which edges a bevel or rounding takes off: "long" (the four along Z) or "all" (default).</summary>
    public static string EdgesOf(PartBox b) => b.Edges != "" ? b.Edges : "all";

    /// <summary>An arc's sweep in degrees (default 90).</summary>
    public static float AngleOf(PartBox b) => b.Angle > 0 ? b.Angle : 90f;

    // ---- Generation ----------------------------------------------------------------------------------------------

    private sealed class Builder(Vector3 size)
    {
        public readonly List<float> V = new(512);
        public readonly List<Matrix4x4> P = [];
        private readonly Vector3 _inv = new(1f / size.X, 1f / size.Y, 1f / size.Z);

        private void Vert(Vector3 p, Vector3 n)
        {
            var q = p * _inv;
            var m = Vector3.Normalize(n) * _inv;
            V.Add(q.X); V.Add(q.Y); V.Add(q.Z); V.Add(m.X); V.Add(m.Y); V.Add(m.Z);
        }

        /// <summary>A triangle with one normal per corner (smooth).</summary>
        public void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 na, Vector3 nb, Vector3 nc)
        {
            if (Vector3.Cross(b - a, c - a).LengthSquared() < 1e-14f)
                return;
            Vert(a, na);
            Vert(b, nb);
            Vert(c, nc);
        }

        /// <summary>A flat triangle; its normal faces away from <paramref name="inside"/>.</summary>
        public void Flat(Vector3 a, Vector3 b, Vector3 c, Vector3 inside)
        {
            var n = Vector3.Cross(b - a, c - a);
            if (n.LengthSquared() < 1e-14f)
                return;
            if (Vector3.Dot(n, (a + b + c) / 3f - inside) < 0)
                n = -n;
            Tri(a, b, c, n, n, n);
        }

        public void FlatQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 inside)
        {
            Flat(a, b, c, inside);
            Flat(a, c, d, inside);
        }

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 na, Vector3 nb, Vector3 nc, Vector3 nd)
        {
            Tri(a, b, c, na, nb, nc);
            Tri(a, c, d, na, nc, nd);
        }

        /// <summary>A proxy box (metres, box space): centre, size and a turn about X.</summary>
        public void Proxy(Vector3 centre, Vector3 sz, float rotX = 0f) =>
            P.Add(Matrix4x4.CreateScale(Vector3.Max(sz, new Vector3(1e-4f))) * Matrix4x4.CreateRotationX(rotX) * Matrix4x4.CreateTranslation(centre)
                  * Matrix4x4.CreateScale(_inv));
    }

    private static (float[] Mesh, Matrix4x4[] Proxies) Generate(PartBox b)
    {
        var bl = new Builder(b.Size);
        var h = b.Size * 0.5f;
        switch (b.Shape)
        {
            case "cylinder":
            case "tube":
                Lathe(bl, h, SegmentsOf(b), InnerOf(b), EndsOf(b), TaperOf(b), b.Shift);
                break;
            case "wedge":
                Wedge(bl, h, TaperOf(b), b.Shift);
                break;
            case "bevel":
            case "rounded":
                Rounded(bl, h, RadiusOf(b), EdgesOf(b) == "long", b.Shape == "bevel" ? 1 : SegmentsOf(b), b.Shape == "bevel");
                break;
            case "capsule":
            case "sphere":
                Capsule(bl, h, SegmentsOf(b), b.Shape == "sphere");
                break;
            case "arc":
                Arc(bl, h, SegmentsOf(b), InnerOf(b), AngleOf(b), b.Dir);
                break;
        }
        return (bl.V.ToArray(), bl.P.ToArray());
    }

    /// <summary>A point of the end ring: angle <paramref name="a"/> on the ellipse of half sizes <paramref name="h"/>, scaled and moved for the end.</summary>
    private static Vector3 Ring(Vector3 h, float a, float k, Vector2 scale, Vector2 centre, float z) =>
        new(centre.X + MathF.Cos(a) * h.X * scale.X * k, centre.Y + MathF.Sin(a) * h.Y * scale.Y * k, z);

    /// <summary>Cylinder and tube: an N-sided prism (flats on top and bottom) along Z, optionally hollow, tapered and shifted at the front.</summary>
    private static void Lathe(Builder bl, Vector3 h, int n, float inner, string ends, Vector2 taper, Vector2 shift)
    {
        var back = new Vector2(1, 1);
        var frontC = new Vector2(shift.X * h.X * 2, shift.Y * h.Y * 2);
        var smooth = n >= 8;
        float A(int i) => MathF.PI / 2 - MathF.PI / n + MathF.Tau * i / n;
        Vector3 Outer(int i, bool front, float k) => front ? Ring(h, A(i), k, taper, frontC, -h.Z) : Ring(h, A(i), k, back, Vector2.Zero, h.Z);

        // Sides: outer wall (and the inner one of a tube).
        foreach (var k in inner > 0 ? new[] { 1f, inner } : [1f])
        {
            var sign = k < 1f ? -1f : 1f;
            for (var i = 0; i < n; i++)
            {
                var p0 = Outer(i, false, k);
                var p1 = Outer(i + 1, false, k);
                var q0 = Outer(i, true, k);
                var q1 = Outer(i + 1, true, k);
                if (!smooth)
                {
                    // Flat sides: the outer wall faces away from the axis, the bore towards it.
                    var c = (p0 + p1 + q0 + q1) * 0.25f;
                    var axis = new Vector3(Lerp2(Vector2.Zero, frontC, 0.5f - c.Z / (2 * h.Z)), c.Z);
                    bl.FlatQuad(p0, p1, q1, q0, sign > 0 ? axis : c + (c - axis));
                    continue;
                }
                Vector3 N(int j, bool front)
                {
                    var a = A(j);
                    // Normal of the side: across the ring tangent and the side line.
                    var t = new Vector3(-MathF.Sin(a) * h.X, MathF.Cos(a) * h.Y, 0);
                    var line = Outer(j, true, k) - Outer(j, false, k);
                    var nn = Vector3.Cross(t, line);
                    var radial = new Vector3(MathF.Cos(a) / h.X, MathF.Sin(a) / h.Y, 0);
                    if (Vector3.Dot(nn, radial) < 0)
                        nn = -nn;
                    return nn * sign;
                }
                bl.Quad(p0, p1, q1, q0, N(i, false), N(i + 1, false), N(i + 1, true), N(i, true));
            }
        }

        // Ends: rims of a tube, and discs where an end is closed.
        foreach (var front in new[] { false, true })
        {
            var z = front ? -h.Z : h.Z;
            var nrm = new Vector3(0, 0, front ? -1 : 1);
            var centre = front ? new Vector3(frontC, z) : new Vector3(0, 0, z);
            var closed = ends == "closed" || ends == (front ? "front" : "back");
            if (inner > 0)
                for (var i = 0; i < n; i++)
                    bl.Quad(Outer(i, front, 1), Outer(i + 1, front, 1), Outer(i + 1, front, inner), Outer(i, front, inner), nrm, nrm, nrm, nrm);
            if (closed)
            {
                var k = inner > 0 ? inner : 1f;
                for (var i = 0; i < n; i++)
                    bl.Tri(centre, Outer(i, front, k), Outer(i + 1, front, k), nrm, nrm, nrm);
            }
        }

        // Proxies: slabs along Z with the smaller end's cross-section (a cross of two boxes; a tube's wall as eight).
        var slabs = taper == Vector2.One && shift == Vector2.Zero ? 1 : 4;
        var apothem = MathF.Cos(MathF.PI / n);
        for (var s = 0; s < slabs; s++)
        {
            float z0 = h.Z - 2 * h.Z * s / slabs, z1 = h.Z - 2 * h.Z * (s + 1) / slabs;
            float t0 = 0.5f - z0 / (2 * h.Z), t1 = 0.5f - z1 / (2 * h.Z);
            var sc = Vector2.Min(Lerp2(Vector2.One, taper, t0), Lerp2(Vector2.One, taper, t1)) * apothem;
            var c = Lerp2(Vector2.Zero, frontC, (t0 + t1) * 0.5f);
            var hz = (z0 - z1) * 0.5f;
            var zc = (z0 + z1) * 0.5f;
            Vector3 hx = new(h.X * sc.X, h.Y * sc.Y, hz);
            CrossSection(bl, new Vector3(c, zc), hx, inner);
        }
        if (inner > 0)
            foreach (var front in new[] { false, true })
                if (ends == "closed" || ends == (front ? "front" : "back"))
                {
                    var sc = (front ? taper : Vector2.One) * apothem;
                    var c = front ? frontC : Vector2.Zero;
                    var zc = front ? -h.Z * 0.97f : h.Z * 0.97f;
                    CrossSection(bl, new Vector3(c, zc), new Vector3(h.X * sc.X, h.Y * sc.Y, h.Z * 0.03f), 0f);
                }
    }

    private static Vector2 Lerp2(Vector2 a, Vector2 b, float t) => a + (b - a) * t;

    /// <summary>Proxy boxes of an elliptical cross-section of half sizes <paramref name="hx"/>: a solid cross, or a ring of eight for a hollow one.</summary>
    private static void CrossSection(Builder bl, Vector3 c, Vector3 hx, float inner)
    {
        const float Cos = 0.924f, Sin = 0.383f;
        if (inner <= 0)
        {
            bl.Proxy(c, new Vector3(2 * hx.X * Cos, 2 * hx.Y * Sin, 2 * hx.Z));
            bl.Proxy(c, new Vector3(2 * hx.X * Sin, 2 * hx.Y * Cos, 2 * hx.Z));
            return;
        }
        // Walls: top, bottom, left, right, and four at the diagonals (in unit-circle terms, then scaled).
        var ri = inner;
        var y1 = ri + (1 - ri) * 0.6f;
        var half = MathF.Sqrt(MathF.Max(0, 1 - y1 * y1));
        foreach (var s in new[] { 1f, -1f })
        {
            bl.Proxy(c + new Vector3(0, s * (ri + y1) * 0.5f * hx.Y, 0), new Vector3(2 * half * hx.X, (y1 - ri) * hx.Y, 2 * hx.Z));
            bl.Proxy(c + new Vector3(s * (ri + y1) * 0.5f * hx.X, 0, 0), new Vector3((y1 - ri) * hx.X, 2 * half * hx.Y, 2 * hx.Z));
        }
        var rm = (ri + 1) * 0.5f;
        var cc = rm / MathF.Sqrt(2);
        var hh = MathF.Max(0.01f, MathF.Min(1 / MathF.Sqrt(2) - cc, cc - ri / MathF.Sqrt(2)));
        foreach (var sx in new[] { 1f, -1f })
            foreach (var sy in new[] { 1f, -1f })
                bl.Proxy(c + new Vector3(sx * cc * hx.X, sy * cc * hx.Y, 0), new Vector3(2 * hh * hx.X, 2 * hh * hx.Y, 2 * hx.Z));
    }

    /// <summary>A box whose front (-Z) face is scaled by <paramref name="taper"/> and moved by <paramref name="shift"/> (fractions of the size).</summary>
    private static void Wedge(Builder bl, Vector3 h, Vector2 taper, Vector2 shift)
    {
        var fc = new Vector2(shift.X * h.X * 2, shift.Y * h.Y * 2);
        Vector3 P(int x, int y, bool front) => front
            ? new Vector3(fc.X + x * h.X * taper.X, fc.Y + y * h.Y * taper.Y, -h.Z)
            : new Vector3(x * h.X, y * h.Y, h.Z);
        var inside = new Vector3(fc * 0.5f, 0);
        bl.FlatQuad(P(-1, -1, false), P(1, -1, false), P(1, 1, false), P(-1, 1, false), inside); // back
        bl.FlatQuad(P(-1, -1, true), P(1, -1, true), P(1, 1, true), P(-1, 1, true), inside); // front
        bl.FlatQuad(P(1, -1, false), P(1, 1, false), P(1, 1, true), P(1, -1, true), inside);
        bl.FlatQuad(P(-1, -1, false), P(-1, 1, false), P(-1, 1, true), P(-1, -1, true), inside);
        bl.FlatQuad(P(-1, 1, false), P(1, 1, false), P(1, 1, true), P(-1, 1, true), inside);
        bl.FlatQuad(P(-1, -1, false), P(1, -1, false), P(1, -1, true), P(-1, -1, true), inside);
        for (var s = 0; s < 4; s++)
        {
            float t0 = s / 4f, t1 = (s + 1) / 4f;
            // The slab box is the overlap of both ends' rectangles (every cross-section in the slab covers it).
            var a0 = Lerp2(Vector2.Zero, fc, t0) - new Vector2(h.X, h.Y) * Lerp2(Vector2.One, taper, t0);
            var a1 = Lerp2(Vector2.Zero, fc, t1) - new Vector2(h.X, h.Y) * Lerp2(Vector2.One, taper, t1);
            var b0 = Lerp2(Vector2.Zero, fc, t0) + new Vector2(h.X, h.Y) * Lerp2(Vector2.One, taper, t0);
            var b1 = Lerp2(Vector2.Zero, fc, t1) + new Vector2(h.X, h.Y) * Lerp2(Vector2.One, taper, t1);
            var mn = Vector2.Max(a0, a1);
            var mx = Vector2.Min(b0, b1);
            var size = Vector2.Max(mx - mn, new Vector2(1e-4f));
            var zc = h.Z - 2 * h.Z * (t0 + t1) * 0.5f;
            bl.Proxy(new Vector3((mn + mx) * 0.5f, zc), new Vector3(size, 2 * h.Z / 4));
        }
    }

    /// <summary>
    /// A box with its edges rounded (<paramref name="flat"/>: bevelled) by <paramref name="radius"/> times the smallest side cut into.
    /// Made by pushing the points of a grid on the box's faces onto the rounded surface round an inner box.
    /// </summary>
    private static void Rounded(Builder bl, Vector3 h, float radius, bool longOnly, int segments, bool flat)
    {
        var minSide = longOnly ? MathF.Min(h.X, h.Y) : MathF.Min(h.X, MathF.Min(h.Y, h.Z));
        var r = radius * 2 * minSide; // radius is a fraction of the smallest full side
        r = MathF.Min(r, minSide * 0.999f);
        var rv = new Vector3(r, r, longOnly ? 0 : r);
        var e = h - rv;
        var bands = flat ? 1 : Math.Max(1, (segments + 1) / 2);
        float[] Samples(float half, float ri)
        {
            if (ri <= 0)
                return [-half, half];
            var list = new List<float>();
            var inner = half - ri;
            for (var k = bands; k >= 1; k--)
                list.Add(-(inner + ri * (flat ? 1 : MathF.Tan(MathF.PI / 4 * k / bands))));
            list.Add(-inner);
            list.Add(inner);
            for (var k = 1; k <= bands; k++)
                list.Add(inner + ri * (flat ? 1 : MathF.Tan(MathF.PI / 4 * k / bands)));
            return list.ToArray();
        }
        float[][] s = [Samples(h.X, rv.X), Samples(h.Y, rv.Y), Samples(h.Z, rv.Z)];
        Vector3 Map(Vector3 p, out Vector3 n, Vector3 face)
        {
            var q = Vector3.Clamp(p, -e, e);
            var d = p - q;
            if (d.LengthSquared() < 1e-14f)
            {
                n = face;
                return p;
            }
            if (flat)
            {
                var l1 = MathF.Abs(d.X) + MathF.Abs(d.Y) + MathF.Abs(d.Z);
                n = face;
                return q + d * (r / l1);
            }
            n = Vector3.Normalize(d);
            return q + n * r;
        }
        for (var axis = 0; axis < 3; axis++)
        {
            var u = (axis + 1) % 3;
            var v = (axis + 2) % 3;
            foreach (var sign in new[] { -1f, 1f })
            {
                var face = Vector3.Zero;
                Set(ref face, axis, sign);
                for (var i = 0; i + 1 < s[u].Length; i++)
                    for (var j = 0; j + 1 < s[v].Length; j++)
                    {
                        Vector3 Corner(int di, int dj)
                        {
                            var p = Vector3.Zero;
                            Set(ref p, axis, sign * Get(h, axis));
                            Set(ref p, u, s[u][i + di]);
                            Set(ref p, v, s[v][j + dj]);
                            return p;
                        }
                        var a = Map(Corner(0, 0), out var na, face);
                        var b = Map(Corner(1, 0), out var nb, face);
                        var c = Map(Corner(1, 1), out var nc, face);
                        var d = Map(Corner(0, 1), out var nd, face);
                        if (flat)
                            bl.FlatQuad(a, b, c, d, Vector3.Zero);
                        else
                            bl.Quad(a, b, c, d, na, nb, nc, nd);
                    }
            }
        }
        // Proxies: the box cut back by the rounding on each pair of sides.
        bl.Proxy(Vector3.Zero, new Vector3(2 * h.X, 2 * e.Y, 2 * e.Z));
        bl.Proxy(Vector3.Zero, new Vector3(2 * e.X, 2 * h.Y, 2 * e.Z));
        if (!longOnly)
            bl.Proxy(Vector3.Zero, new Vector3(2 * e.X, 2 * e.Y, 2 * h.Z));
        // ...and a square in each rounded corner along Z (inside the arc, or inside the bevel's cut).
        var q = flat ? r * 0.5f : r / MathF.Sqrt(2);
        foreach (var sx in new[] { -1f, 1f })
            foreach (var sy in new[] { -1f, 1f })
                bl.Proxy(new Vector3(sx * (e.X + q * 0.5f), sy * (e.Y + q * 0.5f), 0), new Vector3(q, q, 2 * e.Z));
    }

    private static float Get(Vector3 v, int i) => i == 0 ? v.X : i == 1 ? v.Y : v.Z;

    private static void Set(ref Vector3 v, int i, float x)
    {
        if (i == 0)
            v.X = x;
        else if (i == 1)
            v.Y = x;
        else
            v.Z = x;
    }

    /// <summary>A capsule along Z (round ends as deep as the section is wide, or less on a short one), or an ellipsoid filling the box.</summary>
    private static void Capsule(Builder bl, Vector3 h, int n, bool sphere)
    {
        var cap = sphere ? h.Z : MathF.Min(MathF.Max(h.X, h.Y), h.Z);
        var zc = h.Z - cap;
        var rings = Math.Max(2, n / 4);
        // Profile from the back pole (+Z) to the front pole (-Z): (z, scale, cap centre z or NaN for the straight part).
        var prof = new List<(float Z, float S, float C)>();
        for (var k = 0; k <= rings; k++)
        {
            var t = MathF.PI / 2 * k / rings;
            prof.Add((zc + cap * MathF.Cos(t), MathF.Sin(t), zc));
        }
        for (var k = sphere ? 1 : 0; k <= rings; k++)
        {
            var t = MathF.PI / 2 * (rings - k) / rings;
            prof.Add((-zc - cap * MathF.Cos(t), MathF.Sin(t), -zc));
        }
        float A(int i) => MathF.PI / 2 - MathF.PI / n + MathF.Tau * i / n;
        Vector3 P(int ri, int i) => new(MathF.Cos(A(i)) * h.X * prof[ri].S, MathF.Sin(A(i)) * h.Y * prof[ri].S, prof[ri].Z);
        Vector3 N(int ri, int i)
        {
            var p = P(ri, i);
            var (z, s, c) = prof[ri];
            if (s < 1e-4f)
                return new Vector3(0, 0, MathF.Sign(z));
            var dz = MathF.Abs(z) > zc + 1e-6f || sphere ? (z - c) / (cap * cap) : 0f;
            return new Vector3(p.X / (h.X * h.X), p.Y / (h.Y * h.Y), dz);
        }
        for (var ri = 0; ri + 1 < prof.Count; ri++)
            for (var i = 0; i < n; i++)
                bl.Quad(P(ri, i), P(ri, i + 1), P(ri + 1, i + 1), P(ri + 1, i), N(ri, i), N(ri, i + 1), N(ri + 1, i + 1), N(ri + 1, i));
        // Proxies: the straight part, and slabs through the caps.
        var ap = MathF.Cos(MathF.PI / n);
        if (zc > 1e-4f)
            CrossSection(bl, Vector3.Zero, new Vector3(h.X * ap, h.Y * ap, zc), 0f);
        foreach (var sign in new[] { 1f, -1f })
            for (var k = 0; k < 2; k++)
            {
                float z0 = cap * k / 2f, z1 = cap * (k + 1) / 2f;
                var sc = MathF.Sqrt(MathF.Max(0, 1 - z1 / cap * (z1 / cap))) * ap;
                sc = MathF.Max(sc, 0.25f * ap);
                CrossSection(bl, new Vector3(0, 0, sign * (zc + (z0 + z1) * 0.5f)), new Vector3(h.X * sc, h.Y * sc, (z1 - z0) * 0.5f), 0f);
            }
    }

    /// <summary>
    /// An arc: a ring sector of <paramref name="angle"/> degrees in the Y-Z plane, as wide as the box in X, fitted to fill the box's height
    /// and length. Its middle points along <paramref name="dir"/> (0 = down, 90 = forward (-Z), 180 = up, 270 = back).
    /// </summary>
    private static void Arc(Builder bl, Vector3 h, int n, float inner, float angle, float dir)
    {
        var a0 = (dir - angle / 2) * MathF.PI / 180;
        var a1 = (dir + angle / 2) * MathF.PI / 180;
        static Vector2 D(float a) => new(-MathF.Cos(a), -MathF.Sin(a)); // (y, z)
        // Bounds of the sector on the unit circle.
        var lo = new Vector2(float.MaxValue);
        var hi = new Vector2(float.MinValue);
        for (var i = 0; i <= 720; i++)
        {
            var d = D(a0 + (a1 - a0) * i / 720);
            foreach (var p in new[] { d, d * inner })
            {
                lo = Vector2.Min(lo, p);
                hi = Vector2.Max(hi, p);
            }
        }
        var span = Vector2.Max(hi - lo, new Vector2(1e-4f));
        var mid = (lo + hi) * 0.5f;
        Vector2 Fit(Vector2 p) => new((p.X - mid.X) / span.X * 2 * h.Y, (p.Y - mid.Y) / span.Y * 2 * h.Z);
        Vector3 At(int i, float k, float x)
        {
            var q = Fit(D(a0 + (a1 - a0) * i / n) * k);
            return new Vector3(x, q.X, q.Y);
        }
        var centre = Fit(Vector2.Zero);
        var full = angle >= 359.9f;
        Vector3 Normal(int i, float k)
        {
            var prev = At(i == 0 && full ? n - 1 : Math.Max(i - 1, 0), k, 0);
            var next = At(i == n && full ? 1 : Math.Min(i + 1, n), k, 0);
            var t = next - prev;
            var nn = new Vector3(0, -t.Z, t.Y);
            var p = At(i, k, 0);
            var outward = new Vector3(0, p.Y - centre.X, p.Z - centre.Y);
            if (Vector3.Dot(nn, outward) < 0)
                nn = -nn;
            return k < 1f ? -nn : nn;
        }
        for (var i = 0; i < n; i++)
        {
            // Outer and inner faces (smooth), then the two flat sides.
            bl.Quad(At(i, 1, -h.X), At(i + 1, 1, -h.X), At(i + 1, 1, h.X), At(i, 1, h.X), Normal(i, 1), Normal(i + 1, 1), Normal(i + 1, 1), Normal(i, 1));
            if (inner > 0)
                bl.Quad(At(i, inner, -h.X), At(i + 1, inner, -h.X), At(i + 1, inner, h.X), At(i, inner, h.X),
                    Normal(i, inner), Normal(i + 1, inner), Normal(i + 1, inner), Normal(i, inner));
            foreach (var x in new[] { -h.X, h.X })
            {
                var nx = new Vector3(MathF.Sign(x), 0, 0);
                bl.Quad(At(i, 1, x), At(i + 1, 1, x), At(i + 1, inner, x), At(i, inner, x), nx, nx, nx, nx);
            }
            // Proxy: the segment's box between its inner and outer chords.
            var o0 = At(i, 1, 0);
            var o1 = At(i + 1, 1, 0);
            var i0 = At(i, inner, 0);
            var i1 = At(i + 1, inner, 0);
            var om = (o0 + o1) * 0.5f;
            var im = (i0 + i1) * 0.5f;
            var ic = (i1 - i0).Length();
            var along = ic > (o1 - o0).Length() * 0.4f ? ic : (o1 - o0).Length() * 0.4f; // the inner chord: it stays inside the sector
            var radial = om - im;
            var thick = radial.Length();
            var tz = o1 - o0;
            var rot = MathF.Atan2(tz.Y, -tz.Z); // turn about X so the proxy's Z runs along the segment
            bl.Proxy((om + im) * 0.5f, new Vector3(2 * h.X, thick * 0.85f, along * 0.9f), rot); // a little inside: the fit to the box skews the segment
        }
        if (!full)
            foreach (var i in new[] { 0, n })
                bl.FlatQuad(At(i, 1, -h.X), At(i, 1, h.X), At(i, inner, h.X), At(i, inner, -h.X),
                    i == 0 ? At(1, (1 + inner) / 2, 0) : At(n - 1, (1 + inner) / 2, 0));
    }
}
