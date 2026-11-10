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
public static partial class HullLoft
{
    public const string Prefix = "Hull Loft (";
    internal const byte WireKind = 245; // (after Extrusion.WireMark: the key as text)
    private const float T = 0.04f; // a facet's thickness, as the fixed bays'
    private const float DoorPlane = 1.0f, DoorTop = 2.0f;
    private const string Steel = "#7a7f86", Rust = "#4a3a2e", Roof = "#5c5f62";
    private const string SteelLow = "#686d74", SteelHigh = "#8c9198"; // (phase 2: a painted wall's rows by height, so a paint's tone shows the shape: ConstructPaint.ToneOf)
    public const int MaxPoints = 16; // (a smooth section: 8 to 12 facets a side, docs/vehiclekit/BODY-STYLES.md)

    /// <summary>
    /// A section's reach (phase 2, the scale classes of docs/vehiclekit/BODY-STYLES.md): 2 m of half width and 4 m of height, for any class from a buggy's cover to a
    /// giant's room (the key's 9 bits go to 5.11 m; phase 1 read 1.3 m and 3 m, which every walk-in room still keeps).
    /// </summary>
    public const int MaxCm = 200, MaxHeightCm = 400;

    /// <summary>
    /// Where a bay's feet stand from its middle, on the Bolt Deck's quarter grid: 0.75 m for every walk-in floor edge from 0.76 m to 1.25 m (phase 1's, which a room's
    /// seats half a metre out are laid around), further out on a wider floor, further in on a narrower one (a non-room body).
    /// </summary>
    public static float FootX(float floorHalf) =>
        floorHalf < 0.76f ? MathF.Max(0.25f, 0.25f * MathF.Floor((floorHalf - 0.01f) / 0.25f)) : floorHalf < 1.26f ? 0.75f : MathF.Min(1.75f, 0.75f + 0.25f * MathF.Floor((floorHalf - 1.01f) / 0.25f));

    /// <summary>A bay's feet, from the narrower of its two floor edges.</summary>
    public static float FootX(Shape s) => FootX(MathF.Min(s.A.P[0].X, s.B.P[0].X));
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
                if (x is < 0 or > MaxCm || y is < 0 or > MaxHeightCm)
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
                if (x > MaxCm || y > MaxHeightCm)
                    return null;
                list[i] = new Vector2(x / 100f, y / 100f);
            }
            return Valid(list);
        }

        private static Section? Valid(Vector2[] list)
        {
            if (list[0].Y != 0 || list[^1].X != 0 || list[0].X < 0.3f)
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

    public static string Key(float length, bool door, bool front, bool back, Section a, Section b, bool backDoor = false, string extra = "")
        => $"{Prefix}{Cm(length)}{(door ? "d" : "")}{(front ? "f" : "")}{(back ? "r" : "")}{(backDoor ? "b" : "")} {a.Key()} {(b.SameAs(a) ? "=" : b.Key())}{(extra.Length > 0 ? " " + extra : "")})"; // ("=": the back section is the front's; "b": a doorway in its closed back, phase 2; extra: phase 5's spaces)

    /// <summary>
    /// What a key says: the bay's length, its door and closed ends, and its two sections. <see cref="Extra"/> (phase 5, docs/vehiclekit/BODY-STYLES.md section 11): a fourth word,
    /// <c>c</c> and a pattern for a caged bay (<see cref="CageKinds"/>), <c>o</c> for an open top (a bulwark: its roof row isn't drawn), <c>u</c> for a flat roof that is an
    /// upper floor (walkable, with sockets for an upper storey and its furniture), with a hatch <c>uX0,X1,Z0,Z1</c> (centimetres in the bay's frame) where a stair comes up.
    /// <see cref="Lining"/> (phase 7a, section 13): a last word <c>i</c>, a kind and a mode, the room's lining (HullLoft.Lining.cs).
    /// </summary>
    public readonly record struct Shape(float Length, bool Door, bool Front, bool Back, Section A, Section B, bool BackDoor = false, string Extra = "", string Lining = "")
    {
        /// <summary>A caged bay's pattern (0: a skinned bay).</summary>
        public char Cage => Extra.Length == 2 && Extra[0] == 'c' ? Extra[1] : '\0';
        public bool Open => Extra == "o";
        public bool Floor => Extra.Length > 0 && Extra[0] == 'u';

        /// <summary>Phase 7b: the windows' kind (a <see cref="WindowKinds"/> letter, the lining word's fourth; 0: none).</summary>
        public char Window => Lining.Length == 4 ? Lining[3] : '\0';

        /// <summary>The hatch in an upper floor, metres in the bay's frame (null: none).</summary>
        public (float X0, float X1, float Z0, float Z1)? Hatch => Floor && Extra.Length > 1 && Extra[1..].Split(',') is { Length: 4 } w && w.All(x => int.TryParse(x, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            ? (int.Parse(w[0], CultureInfo.InvariantCulture) / 100f, int.Parse(w[1], CultureInfo.InvariantCulture) / 100f, int.Parse(w[2], CultureInfo.InvariantCulture) / 100f, int.Parse(w[3], CultureInfo.InvariantCulture) / 100f)
            : null;
    }

    /// <summary>A hatch word for a key (<see cref="Shape.Extra"/>).</summary>
    public static string HatchWord((float X0, float X1, float Z0, float Z1)? h) =>
        h is { } v ? string.Create(CultureInfo.InvariantCulture, $"u{Cm(v.X0)},{Cm(v.X1)},{Cm(v.Z0)},{Cm(v.Z1)}") : "u";

    /// <summary>Is a key's fourth word one this machine makes?</summary>
    private static bool ExtraOk(string e, float length)
    {
        if (e == "o" || e == "u")
            return true;
        if (e.Length == 2 && e[0] == 'c')
            return CageKinds.Contains(e[1]);
        if (e.Length > 1 && e[0] == 'u' && e[1..].Split(',') is { Length: 4 } w)
        {
            var n = new int[4];
            for (var i = 0; i < 4; i++)
                if (!int.TryParse(w[i], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out n[i]))
                    return false;
            return n[0] >= -MaxCm && n[1] <= MaxCm && n[1] - n[0] >= 30 && n[2] >= 0 && n[3] <= Cm(length) && n[3] - n[2] >= 4; // (a hatch may cross into a bay by a few centimetres: the stair's head room starts there)
        }
        return false;
    }

    /// <summary>A key read back (null: not a lofted bay's, or one no machine would write). The vehicle kit reads a bay's real roof and flanks from it.</summary>
    public static Shape? ShapeOf(string name)
    {
        if (!name.StartsWith(Prefix, StringComparison.Ordinal) || !name.EndsWith(')'))
            return null;
        var words = name[Prefix.Length..^1].Split(' ');
        var lining = words.Length >= 4 && LiningOk(words[^1]) ? words[^1] : "";
        if (lining.Length > 0)
            words = words[..^1];
        if (words.Length is not (3 or 4))
            return null;
        var head = words[0];
        var digits = head.TakeWhile(char.IsDigit).Count();
        if (digits == 0 || !int.TryParse(head[..digits], NumberStyles.Integer, CultureInfo.InvariantCulture, out var cm) || cm is < 50 or > 200)
            return null;
        var flags = head[digits..];
        if (flags.Any(c => c is not ('d' or 'f' or 'r' or 'b')) || flags.Contains('b') && (!flags.Contains('r') || flags.Contains('d')))
            return null;
        if (Section.Parse(words[1]) is not { } a)
            return null;
        var b = words[2] == "=" ? a : Section.Parse(words[2]);
        if (b == null || a.P.Length != b.P.Length)
            return null;
        var extra = words.Length == 4 ? words[3] : "";
        if (extra.Length > 0 && (!ExtraOk(extra, cm / 100f) || extra == "o" && flags.Contains('d') || extra[0] == 'u' && flags.Contains('b')))
            return null;
        if (lining.Length > 0 && extra.Length > 0 && (extra[0] == 'c' || extra == "o" && lining is not { Length: 3 } || extra == "o" && lining[2] != 'f'))
            return null; // (a frame of bars isn't lined; phase 7c: an open top's bulwark is, following its rows, unglazed)
        return new Shape(cm / 100f, flags.Contains('d'), flags.Contains('f'), flags.Contains('r'), a, b, flags.Contains('b'), extra, lining);
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
            {
                if (have != null)
                    have.Touched = Environment.TickCount64;
                return have;
            }
            var made = Make(name);
            Interned[name] = made;
            return made;
        }
    }

    /// <summary>(RuntimeParts) Drops the doomed parts from the key table, and the keys that made nothing (they are made again if asked for).</summary>
    internal static void Forget(Func<ConstructPartDef, bool> doomed)
    {
        lock (Interned)
            RuntimeParts.DropOrNull(Interned, d => d == null || doomed(d));
    }

    private static ConstructPartDef? Make(string name)
    {
        if (MakeFront(name) is { } front)
            return front.Ok ? front.Def : null; // (phase 3: a lofted cab, hood or window pane: HullLoft.Cabs.cs)
        if (MakeTrim(name) is { } trim)
            return trim.Ok ? trim.Def : null; // (phase 4: a bay's trim or a fitting: HullLoft.Trim.cs)
        if (MakeSpace(name) is { } space)
            return space.Ok ? space.Def : null; // (phase 5: a stair or a side pod: HullLoft.Spaces.cs)
        if (ShapeOf(name) is not { } sh)
            return null;
        var (door, a, b) = (sh.Door, sh.A, sh.B);
        if (door && (a.Top < DoorTop + 0.1f || b.Top < DoorTop + 0.1f))
            return null; // (a doorway is 2 m tall)
        if (sh.BackDoor && (b.Top < DoorTop + 0.1f || b.P[0].X < RearJamb + 0.12f || b.XAt(DoorTop) < RearJamb + 0.12f))
            return null; // (a doorway in the back: 2 m tall, its jambs 0.625 m out and a frame beside them)
        if (sh.Floor && !(a.P[^1].Y == a.P[^2].Y && b.P[^1].Y == b.P[^2].Y && a.Top == b.Top))
            return null; // (an upper floor is a level roof)
        ConstructPartDef def;
        try
        {
            def = ConstructParts.Parse(Json(name, sh, out var lamps), name);
            def.Lamps = lamps;
        }
        catch (ConstructPartException e)
        {
            Console.WriteLine($"[vehicles] {name}: refused, {e.Message}");
            return null;
        }
        return Adopt(def, name);
    }

    /// <summary>A part made from its key, adopted into the run-time table (null: it is full).</summary>
    private static ConstructPartDef? Adopt(ConstructPartDef def, string name)
    {
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
        public readonly List<(Vector3 At, Vector3 Colour)> Lamps = new(); // (phase 7b: the lamps that light the room: ConstructPartDef.Lamps)
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
    private static (string Colour, bool Paint) Row(Vector2 p0, Vector2 p1, int k)
    {
        if (k == 0)
            return (Rust, false);
        if (MathF.Abs(p1.Y - p0.Y) < MathF.Abs(p1.X - p0.X))
            return (Roof, false);
        var y = (p0.Y + p1.Y) / 2; // (phase 2: the lower wall darker, the upper lighter, so rows read through any paint and any wear)
        return (y < 0.95f ? SteelLow : y > 1.75f ? SteelHigh : Steel, true);
    }

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

    /// <param name="open">Phase 5: the roof row isn't drawn (a bulwark round an open deck).</param>
    /// <param name="floor">Phase 5: the flat roof is an upper floor, walked on (its slab's top is the panel's), cut round a hatch (x0, x1, z0, z1; all 0: none).</param>
    /// <param name="sh">Phase 7b: the bay's shape, for its windows (<see cref="WindowOf"/>; null: none).</param>
    private static void Sides(Builder p, Section a, Section b, float z0, float z1, bool door, bool open = false, (float X0, float X1, float Z0, float Z1)? floor = null, Shape? sh = null)
    {
        var (ma, mb) = (Mitres(a), Mitres(b));
        var (da, db) = door ? (DoorMitres(a, ma), DoorMitres(b, mb)) : (ma, mb); // (the +x side's)
        var flatRoof = a.P[^1].Y == a.P[^2].Y && b.P[^1].Y == b.P[^2].Y; // (one facet across the top: no seam on the centreline)
        foreach (var s in (float[])[-1, 1])
            for (var k = 0; k + 1 < a.P.Length; k++)
            {
                var (a0, a1, b0, b1) = (a.P[k], a.P[k + 1], b.P[k], b.P[k + 1]);
                var (oa0, oa1, ob0, ob1) = s > 0 ? (da[k], da[k + 1], db[k], db[k + 1]) : (ma[k], ma[k + 1], mb[k], mb[k + 1]);
                if (open && k + 2 == a.P.Length)
                    continue; // (an open top: the bulwark's walls only)
                if (flatRoof && k + 2 == a.P.Length && floor is { } fl)
                {
                    if (s < 0)
                        FloorRoof(p, a0, b0, z0, z1, Off(oa0, -1), Off(da[k], 1), Off(db[k], 1), Off(ob0, -1), fl);
                    continue;
                }
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
                if (sh is { } shw && WindowOf(shw, s) is { } win && k >= win.K0 && k <= win.K1)
                    WindowRow(p, a, b, k, s, z0, z1, win, n, colour, paint, (oa0, oa1, ob0, ob1));
                else
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
                        var (ys0, ys1) = (MathF.Min(c0.Y, d0.Y), MathF.Min(c1.Y, d1.Y));
                        if (sh is { } shs && WindowOf(shs, s) is { } ws && ys1 > ws.Sill - 0.03f && ys0 < ws.Head + 0.02f)
                        {
                            // (phase 7b: a slab across a window stops either side of it, clear of its pane's seal; the opening is too small for a player)
                            p.Hide(new Vector3(s * (x - 0.05f), ys0, z0), new Vector3(s * (x - 0.01f), ys1, ws.W0 - 0.01f));
                            p.Hide(new Vector3(s * (x - 0.05f), ys0, ws.W1 + 0.01f), new Vector3(s * (x - 0.01f), ys1, z1));
                        }
                        else
                            p.Hide(new Vector3(s * (x - 0.05f), ys0, z0), new Vector3(s * (x - 0.01f), ys1, z1));
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
        if (!flatRoof && !open && a.P[^1].X == 0 && b.P[^1].X == 0)
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
            foreach (var (fz0, fz1) in new[] { (z0 + 0.003f, z0 + 0.125f), (z1 - 0.125f, z1 - 0.003f) }) // (3 mm in from the ends: a roof row cut level under an upper storey runs down to the frame's top there, phase 5)
            {
                p.Facet([new(DoorPlane, 0, fz0), new(DoorPlane, DoorTop, fz0), new(DoorPlane, DoorTop, fz1), new(DoorPlane, 0, fz1)], Vector3.UnitX, Rust, off: [Vector3.UnitX, Off(FrameTop(a), 1), Off(FrameTop(b), 1), Vector3.UnitX]);
                p.Hide(new Vector3(DoorPlane - 0.03f, 0, MathF.Max(fz0, z0 + 0.01f)), new Vector3(DoorPlane + 0.03f, DoorTop, MathF.Min(fz1, z1 - 0.01f))); // (a centimetre short of the bay's ends: a neighbour's wall slab stands in line with it)
            }
            p.Hide(new Vector3(DoorPlane - 0.03f, DoorTop, z0), new Vector3(DoorPlane + 0.03f, DoorTop + 0.1f, z1));
            // Where the section stands out past the doorway's plane (a flare, an overhang over the wheels, a strake), the bays either side are wider than the frame:
            // close the door bay's ends between the frame (and the cant over it) and the section, a row at a time (phase 2).
            foreach (var (s, z) in new[] { (a, z0), (b, z1) })
                DoorEnds(p, s, z);
            // Where the section comes in past the doorway's plane under its top (a tumblehome), the frame stands proud of the bays either side: close its ends.
            foreach (var (s, z) in new[] { (a, z0), (b, z1) })
                if (Crossing(s) is { } yc && yc < DoorTop - 0.01f && s.P.FirstOrDefault(q => q.Y > DoorTop + 0.05f) is var pj && pj != default)
                    p.Facet([new(DoorPlane, yc, z), new(DoorPlane, DoorTop, z), new(pj.X, pj.Y, z)], Vector3.UnitZ, Steel, 0.05f);
        }
    }

    /// <summary>
    /// Phase 7b: a wall row with a window cut in it: under the sill (its row) and over the head (its row) the bay's length, and a jamb either side of the opening on every row it crosses. Pieces meet
    /// on cuts across the row (their faces there face each other), the row's own mitres at its ends, the row's normal at a cut.
    /// </summary>
    private static void WindowRow(Builder p, Section a, Section b, int k, float s, float z0, float z1, (int K0, int K1, float Sill, float Head, float W0, float W1) win,
        Vector3 n, string colour, bool paint, (Vector2 A0, Vector2 A1, Vector2 B0, Vector2 B1) m)
    {
        var len = z1 - z0;
        var (na, nb) = (RowNormal(a.P[k], a.P[k + 1]), RowNormal(b.P[k], b.P[k + 1]));
        // y: float.MinValue the row's foot, float.MaxValue its top; t along the bay
        (Vector3 At, Vector3 Off) C(float y, float t)
        {
            Vector2 On(Section sec, int e) => y == float.MinValue ? sec.P[k] : y == float.MaxValue ? sec.P[k + 1] : Vector2.Lerp(sec.P[k], sec.P[k + 1], (y - sec.P[k].Y) / (sec.P[k + 1].Y - sec.P[k].Y));
            var v = Vector2.Lerp(On(a, 0), On(b, 1), t);
            var o = y == float.MinValue ? Vector2.Lerp(m.A0, m.B0, t) : y == float.MaxValue ? Vector2.Lerp(m.A1, m.B1, t) : Vector2.Normalize(Vector2.Lerp(na, nb, t));
            return (new Vector3(s * v.X, v.Y, z0 + len * t), Off(o, s));
        }
        void Piece(float y0, float y1, float t0, float t1, float thick = T)
        {
            var c = new[] { C(y0, t0), C(y1, t0), C(y1, t1), C(y0, t1) };
            p.Facet([c[0].At, c[1].At, c[2].At, c[3].At], n, colour, thick, paint, [c[0].Off, c[1].Off, c[2].Off, c[3].Off]);
        }
        var (tw0, tw1) = ((win.W0 - z0) / len, (win.W1 - z0) / len);
        var (lo, hi) = (k == win.K0 ? win.Sill : float.MinValue, k == win.K1 ? win.Head : float.MaxValue); // (a row between the sill's and the head's: jambs only)
        if (k == win.K0)
            Piece(float.MinValue, win.Sill, 0, 1);
        if (k == win.K1)
            Piece(win.Head, float.MaxValue, 0, 1);
        Piece(lo, hi, 0, tw0, T - 0.006f); // (the jambs 3 mm in from both faces: on a twisting bay their faces and the pieces' over and under them met nearly in a plane)
        Piece(lo, hi, tw1, 1, T - 0.006f);
    }

    /// <summary>The +x outline of a door bay's side at height <paramref name="y"/>: the frame's plane up to its top, then the cant to the first point above it.</summary>
    private static float DoorOutline(Section s, float y)
    {
        if (y <= DoorTop + 1e-4f)
            return DoorPlane;
        var j = Array.FindIndex(s.P, q => q.Y > DoorTop + 0.05f);
        if (j < 0)
            return DoorPlane;
        if (y >= s.P[j].Y)
            return s.XAt(y);
        var t = (y - DoorTop) / (s.P[j].Y - DoorTop);
        return DoorPlane + (s.P[j].X - DoorPlane) * t;
    }

    /// <summary>Strips at a door bay's end from its outline (<see cref="DoorOutline"/>) out to the section where the section stands outside it, below the cant's top.</summary>
    private static void DoorEnds(Builder p, Section s, float z)
    {
        var j = Array.FindIndex(s.P, q => q.Y > DoorTop + 0.05f);
        if (j < 0)
            return;
        var cuts = new List<float>();
        for (var k = 0; k <= j; k++)
            cuts.Add(s.P[k].Y);
        cuts.Add(DoorTop);
        cuts = cuts.Where(y => y <= s.P[j].Y + 1e-4f).Distinct().OrderBy(y => y).ToList();
        for (var i = 0; i + 1 < cuts.Count; i++)
        {
            var (y0, y1) = (cuts[i] < 0.001f ? 0.003f : cuts[i], cuts[i + 1]);
            if (y1 - y0 < 0.01f)
                continue;
            var (o0, o1) = (s.XAt(y0), s.XAt(y1 - 1e-4f));
            var (i0, i1) = (DoorOutline(s, y0) + 0.023f, DoorOutline(s, y1) + 0.023f); // (from 3 mm outside the frame's and the cant's own outer face: a strip from its middle shared that face's plane)
            if (o0 < i0 + 0.01f && o1 < i1 + 0.01f)
                continue; // (the section is the outline here, or inside it: the tumblehome's own close, below)
            p.Facet([new(i0, y0, z), new(MathF.Max(o0, i0 + 0.001f), y0, z), new(MathF.Max(o1, i1 + 0.001f), y1, z), new(i1, y1, z)], Vector3.UnitZ, Steel, 0.05f);
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
    /// <summary>A back doorway's half width: its jambs, the door's 1.25 m opening.</summary>
    public const float RearJamb = 0.625f;

    /// <summary>A strip of a back cap with the doorway cut out under its top: one either side of the opening below the lintel, the whole width above it.</summary>
    private static void CapDoorway(Builder p, Vector2 from, Vector2 b, float z)
    {
        void Strip(Vector2 lo, Vector2 hi, bool open)
        {
            if (hi.Y - lo.Y < 0.012f)
                return;
            var y0 = lo.Y < 0.001f ? 0.003f : lo.Y;
            if (!open)
            {
                p.Facet([new(-lo.X, y0, z), new(lo.X, y0, z), new(hi.X, hi.Y, z), new(-hi.X, hi.Y, z)], Vector3.UnitZ, Steel, 0.05f);
                var w = MathF.Min(lo.X, hi.X) - 0.05f;
                if (w > 0.05f)
                    p.Hide(new Vector3(-w, lo.Y, z - 0.03f), new Vector3(w, hi.Y, z + 0.03f));
                return;
            }
            foreach (var sx in (float[])[-1, 1]) // (the jambs: the cap either side of the opening, its own frame)
            {
                p.Facet([new(sx * RearJamb, y0, z), new(sx * lo.X, y0, z), new(sx * hi.X, hi.Y, z), new(sx * RearJamb, hi.Y, z)], Vector3.UnitZ, Rust, 0.05f);
                var (x0, x1) = (RearJamb + 0.01f, MathF.Min(lo.X, hi.X) - 0.05f);
                if (x1 - x0 > 0.0099f)
                    p.Hide(new Vector3(sx > 0 ? x0 : -x1, lo.Y, z - 0.03f), new Vector3(sx > 0 ? x1 : -x0, hi.Y, z + 0.03f));
            }
        }
        if (b.Y <= DoorTop + 1e-4f)
            Strip(from, b, true);
        else if (from.Y >= DoorTop - 1e-4f)
            Strip(from, b, false);
        else
        {
            var mid = Vector2.Lerp(from, b, (DoorTop - from.Y) / (b.Y - from.Y));
            Strip(from, mid, true);
            Strip(mid, b, false);
        }
    }

    private static void Cap(Builder p, Section s, float z, bool doorway = false, bool open = false)
    {
        var from = s.P[0];
        for (var k = 1; k < (open ? s.P.Length - 1 : s.P.Length); k++) // (an open top: the end's bulwark only, up to the roof row)
        {
            var b = s.P[k];
            if (b.Y - from.Y < 0.02f && k + 1 < s.P.Length)
                continue; // (a row too flat to stand on its own: the next strip takes it in)
            if (doorway && b.Y - from.Y >= 0.012f)
                CapDoorway(p, from, b, z);
            else if (b.Y - from.Y >= 0.012f)
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

    private static string Json(string name, Shape sh, out (Vector3 At, Vector3 Colour)[] lamps)
    {
        var (length, door, front, back, a, b, backDoor) = (sh.Length, sh.Door, sh.Front, sh.Back, sh.A, sh.B, sh.BackDoor);
        var p = new Builder();
        lamps = [];
        if (sh.Cage != '\0')
            CageSides(p, sh); // (phase 5: a frame of bars, HullLoft.Spaces.cs)
        else
            Sides(p, a, b, 0, length, door, sh.Open, sh.Floor ? sh.Hatch ?? (0, 0, 0, 0) : null, sh);
        foreach (var (socket, pane) in RoomPanesOf(sh))
            p.Pad(socket, pane.Socket, "+y", 0.015f); // (phase 7b: a window's pane stands on its sill's middle, a hidden pad under it)
        var ceiling = sh.Lining.Length > 0 ? Lining(p, sh) : null; // (phase 7a: the room's lining, HullLoft.Lining.cs; an inset ceiling's underside, the lever under it)
        if (front)
        {
            if (sh.Cage != '\0')
                CageCap(p, a, 0, sh.Cage);
            else
                Cap(p, a, 0, false, sh.Open);
        }
        if (back)
        {
            if (sh.Cage != '\0')
                CageCap(p, b, length, sh.Cage, backDoor);
            else
                Cap(p, b, length, backDoor, sh.Open);
        }
        // The feet 12 cm in from each end (phase 2; 12.5 before): a part's place is kept to the centimetre, and a deck's sockets are on the quarter metre, so a foot an
        // eighth in put a bay's front on a half centimetre that rounded one way after one length and the other way after another (a 0.75 m bay after a metre one met
        // its neighbour). A bay stands by its front foot ("footL0"), or turned round by its back one ("footR1"), so both ends are 12 cm in.
        foreach (var (k, z) in new[] { (0, 0.12f), (1, length - 0.12f) })
        {
            var foot = FootX(MathF.Min(a.P[0].X, b.P[0].X)); // (0.75 m on every truck's room: phase 1's)
            p.Sock($"footL{k}", new Vector3(-foot, 0, z), "-y");
            p.Sock($"footR{k}", new Vector3(foot, 0, z), "-y");
        }
        var y = (a.WaistY + b.WaistY) / 2;
        var w = (MathF.Max(a.XAt(y), 0) + MathF.Max(b.XAt(y), 0)) / 2;
        p.Pad("-x0", new Vector3(-w, y, length / 2), "-x");
        if (!door)
            p.Pad("+x0", new Vector3(w, y, length / 2), "+x");
        var top = (a.Top + b.Top) / 2; // (the roof at the middle: a lofted roof slopes from one section to the next)
        if (!sh.Open && !sh.Floor) // (an upper floor's roof is walked on and may be cut by a hatch: nothing hangs under it, nothing stands on its middle)
        {
            p.Pad("top", new Vector3(0, top + 0.03f, length / 2), "+y");
            p.Pad("lever", new Vector3(0, MathF.Min(MathF.Min(top, p.Centre), ceiling ?? float.MaxValue) - 0.03f, length / 2), "-y");
        }
        if (sh.Floor)
            FloorSockets(p, sh); // (phase 5: an upper storey's feet and its furniture stand on the roof: HullLoft.Spaces.cs)
        // Mass by the skin's area, as the fixed bays (a metre of tall bay, 6.2 m of skin, is 120 kg).
        float Around(Section s) => 2 * Enumerable.Range(0, s.P.Length - 1).Sum(k => Vector2.Distance(s.P[k], s.P[k + 1]));
        var skin = (Around(a) + Around(b)) / 2 * length + (front ? a.Top * a.P[0].X * 2 : 0) + (back ? b.Top * b.P[0].X * 2 : 0);
        if (sh.Cage != '\0')
            skin *= 0.35f; // (a frame of bars: about a third of a skin's steel)
        var mass = MathF.Round(Math.Clamp(skin * 19.5f, 20, 600), 1);
        var what = (door ? "a doorway in its side" : backDoor ? "a doorway in its closed back" : front ? "its front closed" : back ? "its back closed" : "open at both ends")
            + (sh.Cage != '\0' ? ", a frame of bars you can see and shoot through" : sh.Open ? ", open on top (a bulwark round a deck)" : sh.Floor ? ", its roof an upper floor" : "");
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
        lamps = [.. p.Lamps];
        return part.ToJsonString();
    }
}
