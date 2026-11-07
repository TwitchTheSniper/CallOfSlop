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
    internal const byte WireKind = 246; // (after Extrusion.WireMark: the key as text)
    private const float T = 0.04f; // a facet's thickness, as the fixed bays'
    private const float DoorPlane = 1.0f, DoorTop = 2.0f;
    private const string Steel = "#7a7f86", Rust = "#4a3a2e", Roof = "#5c5f62";
    private const int MaxPoints = 8;

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

        /// <summary>The waist, where a pannier or a sponson hangs: the widest height from 0.4 m to 1.5 m (the one nearest 0.85 m among equals), on a 5 cm grid.</summary>
        public float WaistY
        {
            get
            {
                var (best, wx) = (0.85f, XAt(0.85f));
                for (var y = 0.4f; y <= 1.501f; y += 0.05f)
                    if (XAt(y) is var x && (x > wx + 0.002f || MathF.Abs(x - wx) <= 0.002f && MathF.Abs(y - 0.85f) < MathF.Abs(best - 0.85f)))
                        (best, wx) = (y, x);
                return MathF.Round(best * 100) / 100;
            }
        }

        public string Key() => string.Join("/", P.Select(p => string.Create(CultureInfo.InvariantCulture, $"{Cm(p.X)},{Cm(p.Y)}")));

        public static Section? Parse(string s)
        {
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
            if (list[0].Y != 0 || list[^1].X != 0 || list[0].X < 0.6f)
                return null; // (from the floor to the centreline)
            for (var i = 1; i < list.Length; i++)
                if (list[i].Y < list[i - 1].Y - 0.001f)
                    return null; // (a profile only goes up)
            return new Section(list);
        }

        /// <summary>The same section with x scaled and y scaled above <paramref name="from"/>, to the centimetre (the key's grid).</summary>
        public Section Scaled(float kx, float ky, float from = 0)
            => new(P.Select(p => new Vector2(Snap(p.X * kx), Snap(p.Y <= from ? p.Y : from + (p.Y - from) * ky))).ToArray());
    }

    private static int Cm(float m) => (int)MathF.Round(m * 100);
    private static float Snap(float m) => Cm(m) / 100f;

    public static string Key(float length, bool door, bool front, bool back, Section a, Section b)
        => $"{Prefix}{Cm(length)}{(door ? "d" : "")}{(front ? "f" : "")}{(back ? "r" : "")} {a.Key()} {b.Key()})";

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
        if (Section.Parse(words[1]) is not { } a || Section.Parse(words[2]) is not { } b || a.P.Length != b.P.Length)
            return null;
        var door = flags.Contains('d');
        if (door && (a.Top < DoorTop + 0.1f || b.Top < DoorTop + 0.1f))
            return null; // (a doorway is 2 m tall)
        ConstructPartDef def;
        try
        {
            def = ConstructParts.Parse(Json(name, cm / 100f, door, flags.Contains('f'), flags.Contains('r'), a, b), name);
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

        public void Facet(IReadOnlyList<Vector3> q, Vector3 n, string colour, float t = T, bool paint = true)
        {
            n = Vector3.Normalize(n);
            var pts = new JsonArray();
            var (mn, mx) = (new Vector3(float.MaxValue), new Vector3(float.MinValue));
            foreach (var p in q)
                foreach (var s in (float[])[-1, 1])
                {
                    var v = p + n * (s * t / 2);
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
        /// A four-cornered panel: one facet when its corners lie in a plane, else two triangles, each flat. (A twisted quad whose corners are out of plane by a
        /// fraction of a millimetre makes ConvexHull.Of take its two outer triangles for one face, their normals being within 1e-6 of each other, and drop one:
        /// a hole the rays find.)
        /// </summary>
        public void Panel(Vector3[] q, Vector3 n, string colour, bool paint)
        {
            var plane = Vector3.Normalize(Vector3.Cross(q[1] - q[0], q[3] - q[0]));
            if (MathF.Abs(Vector3.Dot(q[2] - q[0], plane)) < 1e-5f)
            {
                Facet(q, n, colour, paint: paint);
                return;
            }
            foreach (var t in new[] { new[] { q[0], q[1], q[2] }, new[] { q[0], q[2], q[3] } })
            {
                var tn = Vector3.Normalize(Vector3.Cross(t[1] - t[0], t[2] - t[0]));
                Facet(t, Vector3.Dot(tn, n) < 0 ? -tn : tn, colour, paint: paint);
            }
        }

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

    private static void Sides(Builder p, Section a, Section b, float z0, float z1, bool door)
    {
        foreach (var s in (float[])[-1, 1])
            for (var k = 0; k + 1 < a.P.Length; k++)
            {
                var (a0, a1, b0, b1) = (a.P[k], a.P[k + 1], b.P[k], b.P[k + 1]);
                if (door && s > 0 && a1.Y <= DoorTop + 0.05f)
                    continue; // (the doorway's side: its frame, below)
                if (door && s > 0 && a0.Y < DoorTop + 0.05f)
                    (a0, b0) = (new Vector2(DoorPlane, DoorTop), new Vector2(DoorPlane, DoorTop)); // (the cant over the doorway starts at its top)
                var q = new[] { new Vector3(s * a0.X, a0.Y, z0), new Vector3(s * a1.X, a1.Y, z0), new Vector3(s * b1.X, b1.Y, z1), new Vector3(s * b0.X, b0.Y, z1) };
                if (a1.X == 0 && b1.X == 0)
                    (q[1].X, q[2].X) = (-s * 0.01f, -s * 0.01f); // (the two halves of a roof overlap a centimetre at the centreline: a ray straight up the middle meets one)
                var n = new Vector3((a1.Y - a0.Y) * s, -(a1.X - a0.X), 0);
                if (n.LengthSquared() < 1e-6f)
                    continue;
                var (colour, paint) = Row(a0, a1, k);
                p.Panel(q, n, colour, paint);
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
        if (door)
        {
            // The flat frame round the doorway (hull_parts.py's door bay), the door's plane at x = 1.
            foreach (var (fz0, fz1) in new[] { (z0, z0 + 0.125f), (z1 - 0.125f, z1) })
            {
                p.Facet([new(DoorPlane, 0, fz0), new(DoorPlane, DoorTop, fz0), new(DoorPlane, DoorTop, fz1), new(DoorPlane, 0, fz1)], Vector3.UnitX, Rust);
                p.Hide(new Vector3(DoorPlane - 0.03f, 0, fz0), new Vector3(DoorPlane + 0.03f, DoorTop, fz1));
            }
            p.Hide(new Vector3(DoorPlane - 0.03f, DoorTop, z0), new Vector3(DoorPlane + 0.03f, DoorTop + 0.1f, z1));
            // Where the section comes in past the doorway's plane under its top (a tumblehome), the frame stands proud of the bays either side: close its ends.
            foreach (var (s, z) in new[] { (a, z0), (b, z1) })
                if (Crossing(s) is { } yc && yc < DoorTop - 0.01f && s.P.FirstOrDefault(q => q.Y > DoorTop + 0.05f) is var pj && pj != default)
                    p.Facet([new(DoorPlane, yc, z), new(DoorPlane, DoorTop, z), new(pj.X, pj.Y, z)], Vector3.UnitZ, Steel, 0.05f);
        }
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
                p.Facet([new(-from.X, from.Y, z), new(from.X, from.Y, z), new(b.X, b.Y, z), new(-b.X, b.Y, z)], Vector3.UnitZ, Steel, 0.05f);
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
