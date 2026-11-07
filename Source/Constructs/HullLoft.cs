using System.Globalization;
using System.Numerics;
using System.Text.Json.Nodes;

namespace CallOfSlop;

/// <summary>
/// A hull bay made at run time for a cross-section no part file has (docs/vehiclekit/BODY-STYLES.md, "Lofted hull bays"): a slice of walk-in body between two
/// cross-sections, as tools/constructs/hull_parts.py makes its fixed bays, but with any section. A section is a half profile, floor edge first, up the side and over
/// the roof to the centreline, in centimetres; the bay lofts from its front section to its back one, so a roof steps, a nose wedges or a tail boat-tails by the
/// sections it is given. Like an Ordnance Rack's shapes, its name is its key: <c>Hull Loft (100fr 104,0/108,185/82,222/0,222 ...)</c> (length in cm; <c>d</c> a door in
/// the +x side, <c>f</c> its front closed, <c>r</c> its back closed), so a blueprint or a save names it and any machine makes the same part from the name.
/// </summary>
public static class HullLoft
{
    public const string Prefix = "Hull Loft (";
    internal const byte WireKind = 245; // (after Extrusion.WireMark: the key as text)
    private const float T = 0.04f; // a facet's thickness, as the fixed bays'
    private const float DoorPlane = 1.0f, DoorTop = 2.0f;
    private const string Steel = "#7a7f86", Rust = "#4a3a2e", Roof = "#5c5f62";
    public const int MaxPoints = 16; // (a smooth section: 8 to 12 facets a side, docs/vehiclekit/BODY-STYLES.md)
    private const string Digits = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz-_"; // (a compact section: three of these a point)

    /// <summary>A half profile: points from the floor edge (y 0) up and over to the centreline (x 0), metres.</summary>
    public sealed record Section(Vector2[] P)
    {
        public float Top => P[^1].Y;

        /// <summary>The side's half width at height <paramref name="y"/> (the first row that reaches it), or -1 above the section.</summary>
        public float XAt(float y)
        {
            for (var k = 0; k + 1 < P.Length; k++)
                if (y >= P[k].Y && y <= P[k + 1].Y && P[k + 1].Y > P[k].Y)
                    return P[k].X + (P[k + 1].X - P[k].X) * (y - P[k].Y) / (P[k + 1].Y - P[k].Y);
            return -1;
        }

        /// <summary>
        /// The waist, where a pannier or a sponson hangs: the height from 0.4 m to 1.5 m nearest 0.85 m among those within 3 cm of the widest (an upright wall that
        /// leans out a little keeps its waist low, clear of eaves or a ledge above), on a 5 cm grid.
        /// </summary>
        public float WaistY
        {
            get
            {
                var most = 0f;
                for (var y = 0.4f; y <= 1.501f; y += 0.05f)
                    most = MathF.Max(most, XAt(y));
                var best = 0.85f;
                var gap = float.MaxValue;
                for (var pass = 0; pass < 2 && gap == float.MaxValue; pass++) // (an upright stretch of wall first: on a ledge's step a mount's back would sit in the wall above)
                    for (var y = 0.4f; y <= 1.501f; y += 0.05f)
                        if (XAt(y) >= most - 0.03f && (pass == 1 || Upright(y)) && MathF.Abs(y - 0.85f) < gap)
                            (best, gap) = (y, MathF.Abs(y - 0.85f));
                return MathF.Round(best * 100) / 100;
            }
        }

        /// <summary>
        /// The section as a key: <c>~</c> then three characters a point, x and y in centimetres (9 bits each, base 64), so a 13-point smooth section fits a part's
        /// name (<see cref="ConstructParts.MaxRuntimeName"/>). The first prototype's "x,y/x,y" text still reads.
        /// </summary>
        public string Key()
        {
            var sb = new System.Text.StringBuilder("~", 1 + 3 * P.Length);
            foreach (var p in P)
            {
                var v = Math.Clamp(Cm(p.X), 0, 511) << 9 | Math.Clamp(Cm(p.Y), 0, 511);
                sb.Append(Digits[v >> 12 & 63]).Append(Digits[v >> 6 & 63]).Append(Digits[v & 63]);
            }
            return sb.ToString();
        }

        public static Section? Parse(string s)
        {
            if (s.StartsWith('~'))
                return ParseCompact(s);
            var pts = s.Split('/');
            if (pts.Length is < 2 or > MaxPoints)
                return null;
            var list = new Vector2[pts.Length];
            for (var i = 0; i < pts.Length; i++)
            {
                var xy = pts[i].Split(',');
                if (xy.Length != 2 || !int.TryParse(xy[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var x) || !int.TryParse(xy[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var y))
                    return null;
                if (x is < 0 or > 130 || y is < 0 or > 300)
                    return null;
                list[i] = new Vector2(x / 100f, y / 100f);
            }
            return Valid(list);
        }

        private static Section? ParseCompact(string s)
        {
            if ((s.Length - 1) % 3 != 0 || (s.Length - 1) / 3 is < 2 or > MaxPoints)
                return null;
            var list = new Vector2[(s.Length - 1) / 3];
            for (var i = 0; i < list.Length; i++)
            {
                var v = 0;
                for (var k = 0; k < 3; k++)
                {
                    var d = Digits.IndexOf(s[1 + 3 * i + k]);
                    if (d < 0)
                        return null;
                    v = v << 6 | d;
                }
                var (x, y) = (v >> 9, v & 511);
                if (x > 130 || y > 300)
                    return null;
                list[i] = new Vector2(x / 100f, y / 100f);
            }
            return Valid(list);
        }

        private static Section? Valid(Vector2[] list)
        {
            if (list[0].Y != 0 || list[^1].X != 0 || list[0].X < 0.6f)
                return null; // (from the floor to the centreline)
            for (var i = 1; i < list.Length; i++)
                if (list[i].Y < list[i - 1].Y - 0.001f)
                    return null; // (a profile only goes up)
            return new Section(list);
        }

        /// <summary>Is height <paramref name="y"/> on an upright row (twice as tall as it leans), 4 cm or more from its ends?</summary>
        private bool Upright(float y)
        {
            for (var k = 0; k + 1 < P.Length; k++)
                if (y >= P[k].Y && y <= P[k + 1].Y && P[k + 1].Y > P[k].Y)
                    return MathF.Abs(P[k + 1].Y - P[k].Y) >= 2 * MathF.Abs(P[k + 1].X - P[k].X) && y >= P[k].Y + 0.04f && y <= P[k + 1].Y - 0.04f;
            return false;
        }

        /// <summary>The roof's height at half width <paramref name="x"/> (the last row that reaches it).</summary>
        public float YAt(float x)
        {
            for (var k = P.Length - 1; k > 0; k--)
                if (x <= P[k - 1].X && x >= P[k].X && P[k - 1].X > P[k].X)
                    return P[k].Y + (P[k - 1].Y - P[k].Y) * (x - P[k].X) / (P[k - 1].X - P[k].X);
            return x <= P[^1].X ? Top : -1;
        }

        /// <summary>The widest the side gets between heights <paramref name="y0"/> and <paramref name="y1"/> (5 cm steps).</summary>
        public float WidestBetween(float y0, float y1)
        {
            var most = 0f;
            for (var y = y0; y <= y1 + 1e-4f; y += 0.05f)
                most = MathF.Max(most, XAt(MathF.Min(y, Top)));
            return most;
        }

        public bool SameAs(Section o) => o.P.Length == P.Length && P.Zip(o.P).All(t => Cm(t.First.X) == Cm(t.Second.X) && Cm(t.First.Y) == Cm(t.Second.Y));

        /// <summary>The same section with x scaled and y scaled above <paramref name="from"/>, to the centimetre (the key's grid).</summary>
        public Section Scaled(float kx, float ky, float from = 0)
            => new(P.Select(p => new Vector2(Snap(p.X * kx), Snap(p.Y <= from ? p.Y : from + (p.Y - from) * ky))).ToArray());
    }

    private static int Cm(float m) => (int)MathF.Round(m * 100);
    private static float Snap(float m) => Cm(m) / 100f;

    public static string Key(float length, bool door, bool front, bool back, Section a, Section b)
        => $"{Prefix}{Cm(length)}{(door ? "d" : "")}{(front ? "f" : "")}{(back ? "r" : "")} {a.Key()} {(b.SameAs(a) ? "=" : b.Key())})"; // ("=": the back section is the front's)

    /// <summary>What a key says: the bay's length, its door and closed ends, and its two sections.</summary>
    public readonly record struct Shape(float Length, bool Door, bool Front, bool Back, Section A, Section B);

    /// <summary>A key read back (null: not a lofted bay's, or one no machine would write). The vehicle kit reads a bay's real roof and flanks from it.</summary>
    public static Shape? ShapeOf(string name)
    {
        if (!name.StartsWith(Prefix, StringComparison.Ordinal) || !name.EndsWith(')'))
            return null;
        var words = name[Prefix.Length..^1].Split(' ');
        if (words.Length != 3)
            return null;
        var head = words[0];
        var digits = head.TakeWhile(char.IsDigit).Count();
        if (digits == 0 || !int.TryParse(head[..digits], NumberStyles.Integer, CultureInfo.InvariantCulture, out var cm) || cm is < 50 or > 200)
            return null;
        var flags = head[digits..];
        if (flags.Any(c => c is not ('d' or 'f' or 'r')))
            return null;
        if (Section.Parse(words[1]) is not { } a)
            return null;
        var b = words[2] == "=" ? a : Section.Parse(words[2]);
        if (b == null || a.P.Length != b.P.Length)
            return null;
        return new Shape(cm / 100f, flags.Contains('d'), flags.Contains('f'), flags.Contains('r'), a, b);
    }

    private static readonly Dictionary<string, ConstructPartDef?> Interned = new(StringComparer.Ordinal);

    /// <summary>The part a key names, made the first time it is asked for. Null: a key no machine would write, or the run-time table is full.</summary>
    public static ConstructPartDef? Parse(string name)
    {
        if (!name.StartsWith(Prefix, StringComparison.Ordinal) || !name.EndsWith(')') || name.Length > ConstructParts.MaxRuntimeName)
            return null;
        lock (Interned)
        {
            if (Interned.TryGetValue(name, out var have))
                return have;
            var made = Make(name);
            Interned[name] = made;
            return made;
        }
    }

    private static ConstructPartDef? Make(string name)
    {
        if (ShapeOf(name) is not { } sh)
            return null;
        var (door, a, b) = (sh.Door, sh.A, sh.B);
        if (door && (a.Top < DoorTop + 0.1f || b.Top < DoorTop + 0.1f))
            return null; // (a doorway is 2 m tall)
        ConstructPartDef def;
        try
        {
            def = ConstructParts.Parse(Json(name, sh.Length, door, sh.Front, sh.Back, a, b), name);
        }
        catch (ConstructPartException e)
        {
            Console.WriteLine($"[vehicles] {name}: refused, {e.Message}");
            return null;
        }
        def.IsNamedRuntime = true;
        def.ExHash = Fnv(name);
        return Extrusion.Adopt(def) ? def : null;
    }

    private static uint Fnv(string s)
    {
        var h = 2166136261u;
        foreach (var ch in s)
            h = (h ^ ch) * 16777619u;
        return h;
    }

    /// <summary>After <see cref="Extrusion.WireMark"/>: the key, as text.</summary>
    public static void Write(PacketWriter w, ConstructPartDef d) => w.Str(d.Name);

    /// <summary>The key from the wire, as this machine's part id: a stand-in, never a dropped packet, when it can't be made.</summary>
    public static ushort Read(ref PacketReader r)
    {
        var name = r.Str();
        if (!name.StartsWith(Prefix, StringComparison.Ordinal))
            throw new MalformedPacketException($"a run-time part named \"{name}\"");
        return Parse(name) is { } d ? d.Id : ConstructParts.Need("Hull Bay T").Id;
    }

    // ---- The part file -------------------------------------------------------------------------------------------

    private static JsonArray V(float x, float y, float z) => new(R(x), R(y), R(z));
    private static float R(float v) => MathF.Round(v, 4);

    private sealed class Builder
    {
        public readonly JsonArray Boxes = new(), Sockets = new();
        public float Centre = float.MaxValue; // the lowest of the roof's slabs over the centreline: a lever hangs under it

        /// <param name="off">Each corner's own offset for the thickness (a mitre, <see cref="Mitre"/>), so facets meeting at an angle share the joint's plane; null: along <paramref name="n"/>.</param>
        public void Facet(IReadOnlyList<Vector3> q, Vector3 n, string colour, float t = T, bool paint = true, Vector3[]? off = null)
        {
            n = Vector3.Normalize(n);
            var pts = new JsonArray();
            var (mn, mx) = (new Vector3(float.MaxValue), new Vector3(float.MinValue));
            for (var c = 0; c < q.Count; c++)
                foreach (var s in (float[])[-1, 1])
                {
                    var v = q[c] + (off?[c] ?? n) * (s * t / 2);
                    v = new Vector3(R(v.X), R(v.Y), R(v.Z));
                    pts.Add(V(v.X, v.Y, v.Z));
                    (mn, mx) = (Vector3.Min(mn, v), Vector3.Max(mx, v));
                }
            var b = new JsonObject { ["min"] = V(mn.X, mn.Y, mn.Z), ["max"] = V(mx.X, mx.Y, mx.Z), ["color"] = colour, ["detail"] = true, ["shape"] = "hull", ["points"] = pts };
            if (!paint)
                b["paintable"] = false;
            Boxes.Add(b);
        }

        /// <summary>
        /// A four-cornered panel, one facet even when its corners are a little out of plane (a lofted bay between two different sections twists): the drawn shape is
        /// the convex hull of its corners, which keeps both near-twin faces since ConvexHull.Of told them apart (2026-11-07; it used to drop one, a hole the rays found).
        /// </summary>
        public void Panel(Vector3[] q, Vector3 n, string colour, bool paint, Vector3[]? off = null) => Facet(q, n, colour, paint: paint, off: off);

        public void Hide(Vector3 a, Vector3 b)
        {
            var (mn, mx) = (Vector3.Min(a, b), Vector3.Max(a, b));
            if (mx.X - mn.X < 0.0099f || mx.Y - mn.Y < 0.0099f || mx.Z - mn.Z < 0.0099f)
                return;
            Boxes.Add(new JsonObject { ["min"] = V(mn.X, mn.Y, mn.Z), ["max"] = V(mx.X, mx.Y, mx.Z), ["hidden"] = true });
        }

        public void Sock(string name, Vector3 at, string dir) => Sockets.Add(new JsonObject { ["name"] = name, ["at"] = V(at.X, at.Y, at.Z), ["dir"] = dir });

        /// <summary>A socket with a small hidden pad behind it (hull_parts.py's pad): something for the build rules to stand it on.</summary>
        public void Pad(string name, Vector3 at, string dir, float size = 0.03f)
        {
            var v = dir switch { "+x" => Vector3.UnitX, "-x" => -Vector3.UnitX, "+y" => Vector3.UnitY, "-y" => -Vector3.UnitY, "+z" => Vector3.UnitZ, _ => -Vector3.UnitZ };
            Vector3 mn = default, mx = default;
            for (var k = 0; k < 3; k++)
            {
                var c = k == 0 ? v.X : k == 1 ? v.Y : v.Z;
                var p = k == 0 ? at.X : k == 1 ? at.Y : at.Z;
                var lo = p - (c != 0 ? 0 : size) - (c > 0 ? 2 * size : 0);
                var hi = p + (c != 0 ? 0 : size) + (c < 0 ? 2 * size : 0);
                if (k == 0) (mn.X, mx.X) = (lo, hi);
                else if (k == 1) (mn.Y, mx.Y) = (lo, hi);
                else (mn.Z, mx.Z) = (lo, hi);
            }
            Hide(mn, mx);
            Sock(name, at, dir);
        }
    }

    /// <summary>The row colours as the fixed bays have them: the lowest row rust, rows steeper than 45 degrees steel (painted), the flatter ones roof.</summary>
    private static (string Colour, bool Paint) Row(Vector2 p0, Vector2 p1, int k) =>
        k == 0 ? (Rust, false) : MathF.Abs(p1.Y - p0.Y) >= MathF.Abs(p1.X - p0.X) ? (Steel, true) : (Roof, false);

    /// <summary>A row's outward normal on the +x side (x, y): from point k to k + 1 of a section.</summary>
    private static Vector2 RowNormal(Vector2 p0, Vector2 p1) => Vector2.Normalize(new Vector2(p1.Y - p0.Y, -(p1.X - p0.X)) + new Vector2(1e-9f, 0));

    /// <summary>Where two facets meet (normals n1, n2): the bisector, lengthened so each face keeps its own offset (2026-11-07: their end faces then meet on an edge, not overlap).</summary>
    private static Vector2 Mitre(Vector2 n1, Vector2 n2)
    {
        var m = n1 + n2;
        if (m.LengthSquared() < 1e-8f)
            return n1;
        m = Vector2.Normalize(m);
        return m / MathF.Max(0.3f, Vector2.Dot(m, n1)); // (at most 3.3 times the thickness on a hairpin)
    }

    /// <summary>Each point's thickness offset on the +x side: its row's normal at the floor edge, the mitre where two rows meet, and at the centreline the last row's own (the roof's halves overlap there).</summary>
    private static Vector2[] Mitres(Section s)
    {
        var m = new Vector2[s.P.Length];
        for (var k = 0; k < s.P.Length; k++)
        {
            var before = k > 0 ? RowNormal(s.P[k - 1], s.P[k]) : (Vector2?)null;
            var after = k + 1 < s.P.Length ? RowNormal(s.P[k], s.P[k + 1]) : (Vector2?)null;
            m[k] = before is { } b && after is { } a ? Mitre(b, a) : before ?? after ?? Vector2.UnitY;
        }
        return m;
    }

    private static Vector3 Off(Vector2 m, float s) => new(s * m.X, m.Y, 0);

    /// <summary>The ridge of a pitched roof: the mitre of a half's last row and its mirror, straight up (x 0), so both halves' offsets meet there.</summary>
    private static Vector2 Ridge(Vector2 p0, Vector2 p1)
    {
        var n = RowNormal(p0, p1);
        return new Vector2(0, 1 / MathF.Max(0.3f, n.Y)); // (at most 3.3 times the thickness, as Mitre)
    }

    /// <summary>A door bay's +x side: the cant over the doorway starts at the doorway's top, so the mitres there and where it meets the next row are its own.</summary>
    private static Vector2[] DoorMitres(Section s, Vector2[] own)
    {
        var m = (Vector2[])own.Clone();
        for (var k = 0; k + 1 < s.P.Length; k++)
            if (s.P[k].Y < DoorTop + 0.05f && s.P[k + 1].Y > DoorTop + 0.05f)
            {
                var cant = RowNormal(new Vector2(DoorPlane, DoorTop), s.P[k + 1]);
                m[k] = Mitre(Vector2.UnitX, cant);
                if (k + 2 < s.P.Length)
                    m[k + 1] = Mitre(cant, RowNormal(s.P[k + 1], s.P[k + 2]));
                break;
            }
        return m;
    }

    private static void Sides(Builder p, Section a, Section b, float z0, float z1, bool door)
    {
        var (ma, mb) = (Mitres(a), Mitres(b));
        var (da, db) = door ? (DoorMitres(a, ma), DoorMitres(b, mb)) : (ma, mb); // (the +x side's)
        var flatRoof = a.P[^1].Y == a.P[^2].Y && b.P[^1].Y == b.P[^2].Y; // (one facet across the top: no seam on the centreline)
        foreach (var s in (float[])[-1, 1])
            for (var k = 0; k + 1 < a.P.Length; k++)
            {
                var (a0, a1, b0, b1) = (a.P[k], a.P[k + 1], b.P[k], b.P[k + 1]);
                var (oa0, oa1, ob0, ob1) = s > 0 ? (da[k], da[k + 1], db[k], db[k + 1]) : (ma[k], ma[k + 1], mb[k], mb[k + 1]);
                if (flatRoof && k + 2 == a.P.Length)
                {
                    if (s < 0)
                    {
                        var r = new[] { new Vector3(-a0.X, a0.Y, z0), new Vector3(a0.X, a0.Y, z0), new Vector3(b0.X, b0.Y, z1), new Vector3(-b0.X, b0.Y, z1) };
                        p.Panel(r, Vector3.UnitY, Roof, false, [Off(oa0, -1), Off(da[k], 1), Off(db[k], 1), Off(ob0, -1)]);
                        var y = MathF.Min(a0.Y, b0.Y);
                        var w = MathF.Max(a0.X, b0.X);
                        p.Hide(new Vector3(-w, y - 0.05f, z0), new Vector3(w, y - 0.01f, z1));
                        if (w > 0.05f)
                            p.Centre = MathF.Min(p.Centre, y - 0.05f);
                    }
                    continue;
                }
                if (door && s > 0 && a1.Y <= DoorTop + 0.05f)
                    continue; // (the doorway's side: its frame, below)
                if (door && s > 0 && a0.Y < DoorTop + 0.05f)
                {
                    (a0, b0) = (new Vector2(DoorPlane, DoorTop), new Vector2(DoorPlane, DoorTop)); // (the cant over the doorway starts at its top; its mitres: DoorMitres)
                }
                var q = new[] { new Vector3(s * a0.X, a0.Y, z0), new Vector3(s * a1.X, a1.Y, z0), new Vector3(s * b1.X, b1.Y, z1), new Vector3(s * b0.X, b0.Y, z1) };
                if (a1.X == 0 && b1.X == 0)
                    (oa1, ob1) = (Ridge(a0, a1), Ridge(b0, b1)); // (a pitched roof's two halves meet on the centreline's plane: the ridge's mitre, so their ends don't overlap there)
                var n = new Vector3((a1.Y - a0.Y) * s, -(a1.X - a0.X), 0);
                if (n.LengthSquared() < 1e-6f)
                    continue;
                var (colour, paint) = Row(a0, a1, k);
                p.Panel(q, n, colour, paint, [Off(oa0, s), Off(oa1, s), Off(ob1, s), Off(ob0, s)]);
                // What stands for it to the build rules and a player's feet: slabs along the row, just inside its skin (a sponson or a pannier on the waist
                // stands against the skin, and furniture reaches a metre out), a slanted row in steps of 30 cm at most.
                var steps = Math.Max(1, (int)MathF.Ceiling(Vector2.Distance(a0, a1) / 0.3f));
                for (var i = 0; i < steps; i++)
                {
                    float L(float u, float v) => u + (v - u) * i / steps;
                    float H(float u, float v) => u + (v - u) * (i + 1) / steps;
                    var (c0, c1) = (new Vector2(L(a0.X, a1.X), L(a0.Y, a1.Y)), new Vector2(H(a0.X, a1.X), H(a0.Y, a1.Y)));
                    var (d0, d1) = (new Vector2(L(b0.X, b1.X), L(b0.Y, b1.Y)), new Vector2(H(b0.X, b1.X), H(b0.Y, b1.Y)));
                    if (MathF.Abs(c1.Y - c0.Y) >= MathF.Abs(c1.X - c0.X))
                    {
                        var x = (c0.X + c1.X + d0.X + d1.X) / 4;
                        p.Hide(new Vector3(s * (x - 0.05f), MathF.Min(c0.Y, d0.Y), z0), new Vector3(s * (x - 0.01f), MathF.Min(c1.Y, d1.Y), z1));
                    }
                    else
                    {
                        var y = MathF.Min(MathF.Min(c0.Y, c1.Y), MathF.Min(d0.Y, d1.Y));
                        var (xlo, xhi) = (MathF.Min(MathF.Min(c0.X, c1.X), MathF.Min(d0.X, d1.X)), MathF.Max(MathF.Max(c0.X, c1.X), MathF.Max(d0.X, d1.X)));
                        p.Hide(new Vector3(s * xlo, y - 0.05f, z0), new Vector3(s * xhi, y - 0.01f, z1));
                        if (xlo < 0.05f)
                            p.Centre = MathF.Min(p.Centre, y - 0.05f);
                    }
                }
            }
        if (!flatRoof && a.P[^1].X == 0 && b.P[^1].X == 0)
        {
            // A strip along a pitched roof's ridge, just clear of the two halves' outer faces: they meet on the centreline's plane, and a ray straight up the
            // middle would pass along their seam (the Gaps check's hole, Scam Box Truck, 2026-11-07).
            float Apex(Section sec) => sec.Top + T / 2 / MathF.Max(0.3f, RowNormal(sec.P[^2], sec.P[^1]).Y) + 0.011f;
            var (ya, yb) = (Apex(a), Apex(b));
            p.Facet([new(-0.02f, ya, z0), new(0.02f, ya, z0), new(0.02f, yb, z1), new(-0.02f, yb, z1)], Vector3.UnitY, Roof, 0.02f, paint: false);
        }
        if (door)
        {
            // The flat frame round the doorway (hull_parts.py's door bay), the door's plane at x = 1.
            foreach (var (fz0, fz1) in new[] { (z0, z0 + 0.125f), (z1 - 0.125f, z1) })
            {
                p.Facet([new(DoorPlane, 0, fz0), new(DoorPlane, DoorTop, fz0), new(DoorPlane, DoorTop, fz1), new(DoorPlane, 0, fz1)], Vector3.UnitX, Rust, off: [Vector3.UnitX, Off(FrameTop(a), 1), Off(FrameTop(b), 1), Vector3.UnitX]);
                p.Hide(new Vector3(DoorPlane - 0.03f, 0, fz0), new Vector3(DoorPlane + 0.03f, DoorTop, fz1));
            }
            p.Hide(new Vector3(DoorPlane - 0.03f, DoorTop, z0), new Vector3(DoorPlane + 0.03f, DoorTop + 0.1f, z1));
            // Where the section comes in past the doorway's plane under its top (a tumblehome), the frame stands proud of the bays either side: close its ends.
            foreach (var (s, z) in new[] { (a, z0), (b, z1) })
                if (Crossing(s) is { } yc && yc < DoorTop - 0.01f && s.P.FirstOrDefault(q => q.Y > DoorTop + 0.05f) is var pj && pj != default)
                    p.Facet([new(DoorPlane, yc, z), new(DoorPlane, DoorTop, z), new(pj.X, pj.Y, z)], Vector3.UnitZ, Steel, 0.05f);
        }
    }

    /// <summary>The frame's top where the cant over the doorway starts (the mitre of the frame and the cant), as <see cref="Sides"/> has it.</summary>
    private static Vector2 FrameTop(Section s)
    {
        for (var k = 0; k + 1 < s.P.Length; k++)
            if (s.P[k + 1].Y > DoorTop + 0.05f && s.P[k].Y < DoorTop + 0.05f)
                return Mitre(Vector2.UnitX, RowNormal(new Vector2(DoorPlane, DoorTop), s.P[k + 1]));
        return Vector2.UnitX;
    }

    /// <summary>Where a section's side comes in past the doorway's plane on its way up (null: it stays out to the doorway's top).</summary>
    private static float? Crossing(Section s)
    {
        for (var k = 0; k + 1 < s.P.Length; k++)
        {
            var (a, b) = (s.P[k], s.P[k + 1]);
            if (a.Y >= DoorTop)
                return null;
            if (b.X < DoorPlane - 0.01f)
            {
                if (a.X < DoorPlane)
                    return a.Y;
                var t = (a.X - DoorPlane) / MathF.Max(1e-4f, a.X - b.X);
                var y = a.Y + (b.Y - a.Y) * t;
                return y < DoorTop ? y : null;
            }
        }
        return null;
    }

    /// <summary>Closes an end: a strip from the centreline to each row of the section, so a section with a ledge closes without filling it.</summary>
    private static void Cap(Builder p, Section s, float z)
    {
        var from = s.P[0];
        for (var k = 1; k < s.P.Length; k++)
        {
            var b = s.P[k];
            if (b.Y - from.Y < 0.02f && k + 1 < s.P.Length)
                continue; // (a row too flat to stand on its own: the next strip takes it in)
            if (b.Y - from.Y >= 0.012f)
            {
                var y0 = from.Y < 0.001f ? 0.003f : from.Y; // (3 mm off the floor: its underside would share the floor's plane with the walls' undersides)
                p.Facet([new(-from.X, y0, z), new(from.X, y0, z), new(b.X, b.Y, z), new(-b.X, b.Y, z)], Vector3.UnitZ, Steel, 0.05f);
                var w = MathF.Min(from.X, b.X) - 0.05f;
                if (w > 0.05f)
                    p.Hide(new Vector3(-w, from.Y, z - 0.03f), new Vector3(w, b.Y, z + 0.03f));
            }
            from = b;
        }
    }

    private static string Json(string name, float length, bool door, bool front, bool back, Section a, Section b)
    {
        var p = new Builder();
        Sides(p, a, b, 0, length, door);
        if (front)
            Cap(p, a, 0);
        if (back)
            Cap(p, b, length);
        foreach (var (k, z) in new[] { (0, 0.125f), (1, length - 0.125f) })
        {
            p.Sock($"footL{k}", new Vector3(-0.75f, 0, z), "-y");
            p.Sock($"footR{k}", new Vector3(0.75f, 0, z), "-y");
        }
        var y = (a.WaistY + b.WaistY) / 2;
        var w = (MathF.Max(a.XAt(y), 0) + MathF.Max(b.XAt(y), 0)) / 2;
        p.Pad("-x0", new Vector3(-w, y, length / 2), "-x");
        if (!door)
            p.Pad("+x0", new Vector3(w, y, length / 2), "+x");
        var top = (a.Top + b.Top) / 2; // (the roof at the middle: a lofted roof slopes from one section to the next)
        p.Pad("top", new Vector3(0, top + 0.03f, length / 2), "+y");
        p.Pad("lever", new Vector3(0, MathF.Min(top, p.Centre) - 0.03f, length / 2), "-y");
        // Mass by the skin's area, as the fixed bays (a metre of tall bay, 6.2 m of skin, is 120 kg).
        float Around(Section s) => 2 * Enumerable.Range(0, s.P.Length - 1).Sum(k => Vector2.Distance(s.P[k], s.P[k + 1]));
        var skin = (Around(a) + Around(b)) / 2 * length + (front ? a.Top * a.P[0].X * 2 : 0) + (back ? b.Top * b.P[0].X * 2 : 0);
        var mass = MathF.Round(Math.Clamp(skin * 19.5f, 20, 600), 1);
        var what = door ? "a doorway in its side" : front ? "its front closed" : back ? "its back closed" : "open at both ends";
        var part = new JsonObject
        {
            ["format"] = "call-of-slop-construct-part/1",
            ["name"] = name,
            ["description"] = $"{length:0.##} m of walk-in hull lofted between two sections, {what}. Made by the vehicle kit for one body; bays stand on a bolt deck, one after another.",
            ["group"] = "Vehicle Body",
            ["section"] = "Hull bays",
            ["role"] = "panel",
            ["tool"] = "welder",
            ["mass"] = mass,
            ["health"] = MathF.Round(Math.Clamp(skin * 80, 200, 900)),
            ["armour"] = 8,
            ["material"] = "steel",
            ["color"] = Steel,
            ["attachSeconds"] = 2.0,
            ["stage"] = true, // (never offered: only a vehicle made by the kit has one)
            ["boxes"] = p.Boxes,
            ["sockets"] = p.Sockets,
        };
        return part.ToJsonString();
    }
}
