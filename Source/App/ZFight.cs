using System.Numerics;
using System.Text;

namespace CallOfSlop;

/// <summary>
/// The z-fighting check (the user, 2026-10-30: "Z fighting is incredibly common where models line up exactly with each
/// other"): two drawn boxes of one model that have a face each on the same plane (within <see cref="PlaneTolerance"/>),
/// both facing the same way, with the two rectangles sharing area, flicker through each other because the depth buffer
/// can't tell which is in front. Faces covered by a third box (inside it, or pressed against it) are skipped, and so are
/// faces that face opposite ways (two boxes stacked or side by side: the faces that touch are culled or hidden).
/// The fix is always in the data: make one box a little different (inset or proud by 2-5 mm, more for big or far-seen
/// pieces). Used by the self-test section "No z-fighting" and by <c>--check-part</c>, <c>--check-parts</c>,
/// <c>--check-construct-parts</c> and the prop checks, so modders see it too.
/// </summary>
public static class ZFight
{
    /// <summary>Two faces are on the same plane when their planes are at most this far apart (0.5 mm).</summary>
    public const float PlaneTolerance = 0.0005f;

    /// <summary>The overlap of two coplanar faces must be at least this large (m2) to count: 0.1 cm2 (a corner touch is no fight).</summary>
    public const float MinArea = 1e-5f;

    /// <summary>One box of a model: centre, its three unit axes, half sizes and a label for messages.</summary>
    public readonly record struct ZBox(string Name, Vector3 C, Vector3 X, Vector3 Y, Vector3 Z, Vector3 H, bool Moving = false, int Look = 0)
    {
        public static ZBox Of(string name, Matrix4x4 w)
        {
            var x = new Vector3(w.M11, w.M12, w.M13);
            var y = new Vector3(w.M21, w.M22, w.M23);
            var z = new Vector3(w.M31, w.M32, w.M33);
            float lx = MathF.Max(x.Length(), 1e-9f), ly = MathF.Max(y.Length(), 1e-9f), lz = MathF.Max(z.Length(), 1e-9f);
            return new ZBox(name, w.Translation, x / lx, y / ly, z / lz, new Vector3(lx, ly, lz) * 0.5f);
        }

        public static ZBox Of(string name, Vector3 centre, Vector3 half, Quaternion rot)
        {
            var m = Matrix4x4.CreateFromQuaternion(rot);
            return new ZBox(name, centre, new Vector3(m.M11, m.M12, m.M13), new Vector3(m.M21, m.M22, m.M23), new Vector3(m.M31, m.M32, m.M33), half);
        }

        public static ZBox Aligned(string name, Vector3 centre, Vector3 half) => new(name, centre, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, half);

        public Vector3 Axis(int a) => a == 0 ? X : a == 1 ? Y : Z;

        public float Half(int a) => a == 0 ? H.X : a == 1 ? H.Y : H.Z;

        public float Reach => H.Length();

        public bool Contains(Vector3 p, float tol)
        {
            var d = p - C;
            return MathF.Abs(Vector3.Dot(d, X)) <= H.X + tol && MathF.Abs(Vector3.Dot(d, Y)) <= H.Y + tol && MathF.Abs(Vector3.Dot(d, Z)) <= H.Z + tol;
        }
    }

    /// <summary>One coplanar pair: the two boxes' indexes, the shared face normal, a point in the overlap and its area.</summary>
    public readonly record struct Hit(int A, int B, Vector3 Normal, Vector3 At, float Area, float Gap);

    /// <summary>Every coplanar, same-facing, overlapping pair of faces among <paramref name="boxes"/> that is not covered by a third box.</summary>
    /// <param name="boxes">The model's boxes. A box marked <see cref="ZBox.Moving"/> (animated) is left out: it is never at rest.</param>
    /// <param name="sameLookOk">Two boxes with the same non-zero <see cref="ZBox.Look"/> (colour and texture, drawn with world-aligned texture coordinates, as map and building boxes are) show no difference where they overlap.</param>
    /// <param name="onGround">The model stands on the ground at y = 0 (props, buildings): a face pointing down at y = 0 is never seen.</param>
    public static List<Hit> Find(IReadOnlyList<ZBox> boxes, bool onGround = false, bool sameLookOk = false)
    {
        var hits = new List<Hit>();
        var n = boxes.Count;
        if (n < 2)
            return hits;
        Span<Vector3> pa = stackalloc Vector3[4];
        Span<Vector3> pb = stackalloc Vector3[4];
        var poly = new List<Vector3>(8);
        for (var i = 0; i < n; i++)
        {
            var a = boxes[i];
            for (var j = i + 1; j < n; j++)
            {
                var b = boxes[j];
                if (a.Moving || b.Moving || (sameLookOk && a.Look != 0 && a.Look == b.Look))
                    continue;
                if (Vector3.Distance(a.C, b.C) > a.Reach + b.Reach + PlaneTolerance)
                    continue;
                for (var fa = 0; fa < 6; fa++)
                {
                    var na = a.Axis(fa >> 1) * ((fa & 1) == 0 ? 1f : -1f);
                    var da = Vector3.Dot(na, a.C) + a.Half(fa >> 1);
                    for (var fb = 0; fb < 6; fb++)
                    {
                        var nb = b.Axis(fb >> 1) * ((fb & 1) == 0 ? 1f : -1f);
                        if (Vector3.Dot(na, nb) < 0.9999f)
                            continue;
                        var db = Vector3.Dot(na, b.C) + b.Half(fb >> 1);
                        if (MathF.Abs(da - db) > PlaneTolerance)
                            continue;
                        if (onGround && na.Y < -0.9999f && -da <= PlaneTolerance)
                            continue; // the underside on the ground: hidden
                        Corners(a, fa, pa);
                        Corners(b, fb, pb);
                        var area = OverlapArea(na, pa, pb, out var centre, poly);
                        if (area < MinArea)
                            continue;
                        if (Covered(boxes, i, j, na, centre, poly))
                            continue;
                        hits.Add(new Hit(i, j, na, centre, area, db - da));
                    }
                }
            }
        }
        return hits;
    }

    private static void Corners(ZBox b, int face, Span<Vector3> o)
    {
        int ax = face >> 1, u = (ax + 1) % 3, v = (ax + 2) % 3;
        var n = b.Axis(ax) * ((face & 1) == 0 ? 1f : -1f);
        var c = b.C + n * b.Half(ax);
        var du = b.Axis(u) * b.Half(u);
        var dv = b.Axis(v) * b.Half(v);
        o[0] = c - du - dv;
        o[1] = c + du - dv;
        o[2] = c + du + dv;
        o[3] = c - du + dv;
    }

    /// <summary>Area of the overlap of two coplanar rectangles (corners in order), by clipping one against the other; the overlap's centre in <paramref name="centre"/>.</summary>
    private static float OverlapArea(Vector3 n, ReadOnlySpan<Vector3> a, ReadOnlySpan<Vector3> b, out Vector3 centre, List<Vector3> poly)
    {
        var e1 = MathF.Abs(n.Y) < 0.9f ? Vector3.Normalize(Vector3.Cross(n, Vector3.UnitY)) : Vector3.Normalize(Vector3.Cross(n, Vector3.UnitX));
        var e2 = Vector3.Cross(n, e1);
        var origin = a[0];
        var subject = new List<Vector2>(8);
        var clip = new Vector2[4];
        foreach (var p in a)
            subject.Add(new Vector2(Vector3.Dot(p - origin, e1), Vector3.Dot(p - origin, e2)));
        for (var k = 0; k < 4; k++)
            clip[k] = new Vector2(Vector3.Dot(b[k] - origin, e1), Vector3.Dot(b[k] - origin, e2));
        // Orientation of the clip polygon (either way round).
        var signed = 0f;
        for (var k = 0; k < 4; k++)
            signed += Cross(clip[k], clip[(k + 1) % 4]);
        var sgn = signed >= 0 ? 1f : -1f;
        for (var k = 0; k < 4 && subject.Count > 0; k++)
        {
            var c0 = clip[k];
            var c1 = clip[(k + 1) % 4];
            var input = subject.ToArray();
            subject.Clear();
            for (var m = 0; m < input.Length; m++)
            {
                var p = input[m];
                var q = input[(m + 1) % input.Length];
                var ps = Side(c0, c1, p) * sgn;
                var qs = Side(c0, c1, q) * sgn;
                if (ps >= 0)
                {
                    subject.Add(p);
                    if (qs < 0)
                        subject.Add(Cut(p, q, ps, qs));
                }
                else if (qs >= 0)
                    subject.Add(Cut(p, q, ps, qs));
            }
        }
        centre = default;
        poly.Clear();
        if (subject.Count < 3)
            return 0;
        var area = 0f;
        var cen = Vector2.Zero;
        foreach (var p in subject)
            cen += p / subject.Count;
        for (var k = 0; k < subject.Count; k++)
            area += Cross(subject[k], subject[(k + 1) % subject.Count]);
        centre = origin + e1 * cen.X + e2 * cen.Y;
        foreach (var q in subject)
            poly.Add(origin + e1 * q.X + e2 * q.Y);
        // A sliver thinner than the plane tolerance is two boxes meeting edge to edge (float noise), not a fight.
        float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
        foreach (var q in subject)
        {
            minX = MathF.Min(minX, q.X);
            maxX = MathF.Max(maxX, q.X);
            minY = MathF.Min(minY, q.Y);
            maxY = MathF.Max(maxY, q.Y);
        }
        if (maxX - minX < PlaneTolerance || maxY - minY < PlaneTolerance)
            return 0;
        return MathF.Abs(area) * 0.5f;
    }

    private static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;

    private static float Side(Vector2 a, Vector2 b, Vector2 p) => (b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X);

    private static Vector2 Cut(Vector2 p, Vector2 q, float ps, float qs) => p + (q - p) * (ps / (ps - qs));

    /// <summary>Is the whole shared patch hidden by a third box (a point just outside the faces is inside it) at every sample?</summary>
    private static bool Covered(IReadOnlyList<ZBox> boxes, int i, int j, Vector3 n, Vector3 centre, List<Vector3> poly)
    {
        // Samples: the middle of the patch and each corner pulled a tenth of the way in; hidden only if all are covered.
        for (var k = -1; k < poly.Count; k++)
        {
            var p = k < 0 ? centre : Vector3.Lerp(poly[k], centre, 0.1f);
            var outside = p + n * 0.001f;
            var hidden = false;
            for (var m = 0; m < boxes.Count && !hidden; m++)
                if (m != i && m != j && boxes[m].Contains(outside, 1e-5f))
                    hidden = true;
            if (!hidden)
                return false;
        }
        return true;
    }

    // ---- Messages ----

    private static string Dir(Vector3 n)
    {
        var ax = MathF.Abs(n.X) >= MathF.Abs(n.Y) && MathF.Abs(n.X) >= MathF.Abs(n.Z) ? 0 : MathF.Abs(n.Y) >= MathF.Abs(n.Z) ? 1 : 2;
        var s = n[ax] >= 0 ? "+" : "-";
        return s + "XYZ"[ax];
    }

    /// <summary>A short label for a box: "#3 [0.20 x 0.10 x 0.05 at (0.00, 1.00, 0.00)]".</summary>
    public static string Label(int index, ZBox b) =>
        $"#{index} {b.Name} [{b.H.X * 2:0.###} x {b.H.Y * 2:0.###} x {b.H.Z * 2:0.###} at ({b.C.X:0.###}, {b.C.Y:0.###}, {b.C.Z:0.###})]";

    /// <summary>One line describing a hit: the two boxes, the face direction and the shared area.</summary>
    public static string Describe(IReadOnlyList<ZBox> boxes, Hit h) =>
        $"{Label(h.A, boxes[h.A])} and {Label(h.B, boxes[h.B])} share a {Dir(h.Normal)} face plane ({h.Area * 10000:0.#} cm2, {h.Gap * 1000:0.##} mm apart)";

    /// <summary>The check's lines for one model, or none: the model's name and each hit (at most <paramref name="max"/>).</summary>
    public static List<string> Report(string model, IReadOnlyList<ZBox> boxes, bool onGround = false, int max = 6)
    {
        var lines = new List<string>();
        var hits = Find(boxes, onGround);
        if (hits.Count == 0)
            return lines;
        foreach (var h in hits.Take(max))
            lines.Add($"{model}: {Describe(boxes, h)}");
        if (hits.Count > max)
            lines.Add($"{model}: ... and {hits.Count - max} more coplanar pair(s)");
        return lines;
    }

    // ---- Adapters: the boxes each kind of data draws, built by the real loaders ----

    /// <summary>A prop's boxes (prefabs are flat already; flag cloth in code-built corner parts is skipped).</summary>
    public static List<ZBox> Boxes(PropDef def)
    {
        var list = new List<ZBox>(def.Parts.Count);
        for (var i = 0; i < def.Parts.Count; i++)
        {
            var p = def.Parts[i];
            if (p.Corners != null)
                continue;
            list.Add(ZBox.Of("part " + i, p.Offset, p.Size * 0.5f, p.Rotation) with { Moving = p.Anim is { Type: PropAnimType.Follow or PropAnimType.Spin or PropAnimType.Swing or PropAnimType.Bob or PropAnimType.Shake } });
        }
        return list;
    }

    /// <summary>A construct part's drawn plain boxes (solid and detail; hidden collision boxes and contoured shapes are skipped).</summary>
    public static List<ZBox> Boxes(ConstructPartDef def)
    {
        var list = new List<ZBox>(def.Boxes.Length);
        for (var i = 0; i < def.Boxes.Length; i++)
        {
            var b = def.Boxes[i];
            if (b.Hidden || b.Shape != 0)
                continue;
            list.Add(ZBox.Aligned((i < def.SolidBoxes ? "box " : "detail ") + i, b.Center, b.Half));
        }
        return list;
    }

    /// <summary>A weapon-builder part's plain boxes (box and plate; contoured shapes and rods are skipped), all of them as if every tag were drawn.</summary>
    public static List<ZBox> Boxes(PartDef def)
    {
        var list = new List<ZBox>(def.Boxes.Length);
        for (var i = 0; i < def.Boxes.Length; i++)
        {
            var b = def.Boxes[i];
            if (b.ShapeId != 0 || b.Shape is not ("box" or "plate"))
                continue;
            list.Add(ZBox.Of("box " + i + (b.Tag.Length > 0 ? " (" + b.Tag + ")" : ""), b.Pos, b.Size * 0.5f, b.Rotation));
        }
        return list;
    }

    /// <summary>An assembled weapon's plain boxes (gun space), named by their index in the model.</summary>
    public static List<ZBox> Boxes(GunMesh mesh)
    {
        var list = new List<ZBox>(mesh.Parts.Length);
        for (var i = 0; i < mesh.Parts.Length; i++)
        {
            if (mesh.Parts[i].Shape != 0)
                continue;
            list.Add(ZBox.Of("box " + i, mesh.Matrices[i]));
        }
        return list;
    }

    /// <summary>A building's shell boxes, door leaves and window panes.</summary>
    public static List<ZBox> Boxes(BuildingPlan plan)
    {
        var list = new List<ZBox>(plan.Shell.Count + plan.Doors.Count + plan.Windows.Count);
        for (var i = 0; i < plan.Shell.Count; i++)
            list.Add(ZBox.Aligned("shell " + i, plan.Shell[i].Box.Center, plan.Shell[i].Box.Half) with { Look = HashCode.Combine(plan.Shell[i].Color, plan.Shell[i].Layer) | 1 });
        for (var i = 0; i < plan.Doors.Count; i++)
            list.Add(ZBox.Aligned("door " + i, plan.Doors[i].Closed.Center, plan.Doors[i].Closed.Half));
        for (var i = 0; i < plan.Windows.Count; i++)
            list.Add(ZBox.Aligned("window " + i, plan.Windows[i].Glass.Center, plan.Windows[i].Glass.Half));
        return list;
    }

    // ---- Resolve (used by the fix tool: nudges the smaller box of each pair) ----

    /// <summary>
    /// Nudges boxes until <see cref="Find"/> finds nothing: of each coplanar pair the box with the smaller face moves by
    /// <paramref name="delta"/>: a face lying inside the other's face comes proud, any other face is inset (and comes
    /// proud when the box is too thin to inset). Returns the new boxes (same order).
    /// </summary>
    public static ZBox[] Resolve(IReadOnlyList<ZBox> input, float delta, bool onGround, out int passes, float minThick = 0.002f)
    {
        var b = input.ToArray();
        passes = 0;
        for (; passes < 20; passes++)
        {
            var hits = Find(b, onGround);
            if (hits.Count == 0)
                break;
            // Faces joined by hits form groups on one plane; the biggest face stays, the others step back by one delta more each.
            var adj = new Dictionary<int, List<int>>();
            int Key(int box, Vector3 n)
            {
                var (ax, sg) = FaceOf(b[box], n);
                return box * 6 + ax * 2 + (sg > 0 ? 0 : 1);
            }
            var contained = new HashSet<int>();
            foreach (var h in hits)
            {
                int ka = Key(h.A, h.Normal), kb = Key(h.B, h.Normal);
                if (!adj.TryGetValue(ka, out var la))
                    adj[ka] = la = new List<int>();
                if (!adj.TryGetValue(kb, out var lb))
                    adj[kb] = lb = new List<int>();
                la.Add(kb);
                lb.Add(ka);
                if (h.Area >= FaceArea(b[h.A], ka % 6 / 2) * 0.98f)
                    contained.Add(ka);
                if (h.Area >= FaceArea(b[h.B], kb % 6 / 2) * 0.98f)
                    contained.Add(kb);
            }
            // Greedy levels: the biggest face of a conflict stays (level 0); a face steps back one more delta than any
            // face it conflicts with that was placed before it, so touching faces always end up at least one delta apart.
            var level = new Dictionary<int, int>();
            foreach (var k in adj.Keys.OrderByDescending(k => FaceArea(b[k / 6], k % 6 / 2)).ThenBy(k => k))
            {
                var used = new HashSet<int>();
                foreach (var nb in adj[k])
                    if (level.TryGetValue(nb, out var lv))
                        used.Add(lv);
                var l = 0;
                while (used.Contains(l))
                    l++;
                level[k] = l;
            }
            foreach (var (k, l) in level)
            {
                if (l == 0)
                    continue;
                int box = k / 6, ax = k % 6 / 2;
                var n = b[box].Axis(ax) * (k % 2 == 0 ? 1f : -1f);
                var v = b[box];
                var step = delta * l * (1f + 0.37f * passes);
                var proud = contained.Contains(k) || v.Half(ax) * 2 - step < minThick;
                var d = proud ? step : -step;
                var half = v.H;
                half[ax] += d * 0.5f;
                b[box] = v with { C = v.C + n * (d * 0.5f), H = half };
            }
        }
        return b;
    }

    private static (int Axis, int Sign) FaceOf(ZBox b, Vector3 n)
    {
        for (var a = 0; a < 3; a++)
        {
            var d = Vector3.Dot(b.Axis(a), n);
            if (MathF.Abs(d) > 0.9999f)
                return (a, d > 0 ? 1 : -1);
        }
        return (0, 1);
    }

    private static float FaceArea(ZBox b, int axis) => 4 * b.Half((axis + 1) % 3) * b.Half((axis + 2) % 3);

    // ---- Scans over every shipped model ----

    /// <summary>One model with coplanar faces: its kind ("prop", "building", "construct", "part", "build"), name and the report lines.</summary>
    public sealed record Finding(string Kind, string Model, int Pairs, List<string> Lines);

    private static void Add(List<Finding> into, string kind, string model, IReadOnlyList<ZBox> boxes)
    {
        var hits = Find(boxes, kind is "prop" or "building", kind == "building");
        if (hits.Count == 0)
            return;
        var lines = hits.Take(6).Select(h => Describe(boxes, h)).ToList();
        if (hits.Count > 6)
            lines.Add($"... and {hits.Count - 6} more");
        into.Add(new Finding(kind, model, hits.Count, lines));
    }

    /// <summary>Props: the global library and every pack's own props.</summary>
    public static List<Finding> ScanProps()
    {
        var r = new List<Finding>();
        foreach (var d in Props.All)
            Add(r, "prop", d.Key + " (" + d.SourceFile + ")", Boxes(d));
        var maps = Path.Combine(AppFolders.Data, "Maps");
        if (Directory.Exists(maps))
            foreach (var dir in Directory.GetDirectories(maps).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                if (Directory.Exists(Path.Combine(dir, "Props")))
                    foreach (var d in Props.ForPack(dir).Local)
                        Add(r, "prop", d.Key + " (" + d.SourceFile + ")", Boxes(d));
        return r;
    }

    /// <summary>Buildings: each building's default build and its fewest and most floors (the shell, doors and window panes).</summary>
    public static List<Finding> ScanBuildings()
    {
        var r = new List<Finding>();
        var scope = Props.Global;
        foreach (var d in Buildings.All)
        {
            var floors = new List<int> { 0 };
            if (d.MaxFloors > d.MinFloors)
                floors.AddRange([d.MinFloors, d.MaxFloors]);
            foreach (var f in floors.Distinct())
            {
                var plan = BuildingGen.Generate(d, new BuildingVariant { Furnish = false, Floors = f }, 0, Vector3.Zero, 0, scope);
                Add(r, "building", d.Name + (f > 0 ? " (" + f + " floors)" : ""), Boxes(plan));
            }
        }
        return r;
    }

    /// <summary>Sandbox construct parts.</summary>
    public static List<Finding> ScanConstructs()
    {
        var r = new List<Finding>();
        foreach (var d in ConstructParts.All)
            Add(r, "construct", d.Name + " (" + d.File + ")", Boxes(d));
        return r;
    }

    /// <summary>Weapon-builder parts, each on its own.</summary>
    public static List<Finding> ScanParts()
    {
        var r = new List<Finding>();
        foreach (var p in Parts.All)
            Add(r, "part", p.Name, Boxes(p));
        return r;
    }

    /// <summary>Standard builds, assembled (parts against each other).</summary>
    public static List<Finding> ScanBuilds()
    {
        var r = new List<Finding>();
        foreach (var sb in StandardBuilds.All)
            Add(r, "build", sb.Name, Boxes(BuildModel.Build(sb.Build(), Vector3.One).Mesh));
        return r;
    }

    /// <summary>Joins report lines for a message (for the CLI checks).</summary>
    public static string Join(IEnumerable<string> lines)
    {
        var sb = new StringBuilder();
        foreach (var l in lines)
            sb.AppendLine(l);
        return sb.ToString();
    }
}
