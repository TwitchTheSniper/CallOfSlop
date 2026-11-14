using System.Globalization;
using System.Numerics;
using System.Text;

namespace CallOfSlop;

/// <summary>
/// Holes through parts: the phase 0 spike of "Punching holes through capital ships" (docs/sandbox/design/hull-holes-spike.md). A railgun's
/// line through a ship leaves an opening in every part it crosses, which you can see, shoot, walk and look through.
/// <para>
/// A holed part is a named run-time part (<see cref="ConstructPartDef.IsNamedRuntime"/>, as a cut panel or a pressed part is): its key is
/// <c>Holed (Rust Bucket Deck | 120,340,-15,707,0,-707,15;...)</c>, the file part's name, then each hole as a line in the part's own frame
/// (a point on it in cm, its direction in thousandths, its radius in cm). Every machine makes the same part from the key, so saves, blueprints
/// and the wire (after <see cref="Extrusion.WireMark"/> and <see cref="HullLoft.WireKind"/>, as text) carry it with nothing new.
/// </para>
/// <para>
/// The cut, per box: a plain box (solid, hidden or drawn) loses a square tunnel along the axis that opens least, as up to four boxes round
/// it; a flat hull facet (<c>"shape": "hull"</c>, the faceted ships' skin) is cut in its own plane, as up to four convex facets round the
/// opening; any other shape a hole reaches is left out (its hidden boxes are still cut). A part keeps its sockets, health and links; its mass
/// goes with the solid volume left. Limits: <see cref="MaxHoles"/> a part, <see cref="ConstructParts.MaxBoxes"/> boxes, the key's length.
/// </para>
/// </summary>
public static class HullHoles
{
    public const string Prefix = "Holed (";
    private const string Split = " | ";

    /// <summary>Holes a part may take (the key holds them all).</summary>
    public const int MaxHoles = 8;

    public const float MinRadius = 0.03f, MaxRadius = 3f;

    /// <summary>A hull facet thinner than this share of its narrower side, and than <see cref="ThinMax"/>, is cut as a sheet.</summary>
    private const float ThinShare = 0.3f, ThinMax = 0.6f;

    /// <summary>A piece thinner than this (m) is left out.</summary>
    private const float MinPiece = 0.004f;

    /// <summary>One hole: a line in the part's frame and its radius.</summary>
    public readonly record struct Hole(int X, int Y, int Z, short Dx, short Dy, short Dz, ushort R)
    {
        public Vector3 Origin => new Vector3(X, Y, Z) / 100f;
        public Vector3 Dir => Vector3.Normalize(new Vector3(Dx, Dy, Dz));
        public float Radius => R / 100f;
        public string Text => string.Create(CultureInfo.InvariantCulture, $"{X},{Y},{Z},{Dx},{Dy},{Dz},{R}");

        public static Hole Of(Vector3 origin, Vector3 dir, float radius)
        {
            dir = Vector3.Normalize(dir);
            return new Hole((int)MathF.Round(origin.X * 100), (int)MathF.Round(origin.Y * 100), (int)MathF.Round(origin.Z * 100),
                (short)MathF.Round(dir.X * 1000), (short)MathF.Round(dir.Y * 1000), (short)MathF.Round(dir.Z * 1000),
                (ushort)Math.Clamp((int)MathF.Round(radius * 100), (int)(MinRadius * 100), (int)(MaxRadius * 100)));
        }

        public static bool TryParse(string s, out Hole h)
        {
            h = default;
            var f = s.Split(',');
            if (f.Length != 7)
                return false;
            Span<int> v = stackalloc int[7];
            for (var i = 0; i < 7; i++)
                if (!int.TryParse(f[i], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out v[i]))
                    return false;
            if (Math.Abs(v[3]) > 1000 || Math.Abs(v[4]) > 1000 || Math.Abs(v[5]) > 1000 || v[3] == 0 && v[4] == 0 && v[5] == 0)
                return false;
            if (v[6] < MinRadius * 100 || v[6] > MaxRadius * 100 || Math.Abs(v[0]) > 40000 || Math.Abs(v[1]) > 40000 || Math.Abs(v[2]) > 40000)
                return false;
            h = new Hole(v[0], v[1], v[2], (short)v[3], (short)v[4], (short)v[5], (ushort)v[6]);
            return true;
        }
    }

    private sealed record Made(ConstructPartDef Base, Hole[] Holes);

    private static readonly Dictionary<string, ConstructPartDef?> Interned = new(StringComparer.Ordinal);
    private static readonly Dictionary<ConstructPartDef, Made> Info = new();

    /// <summary>What the spike reads: parts made, the time it took, boxes added and pieces left out, refusals.</summary>
    public static int DefsMade, Refused, PiecesDropped, FacetsCut, BoxesCut;
    public static double BuildMs;
    public static string LastRefusal = "";

    /// <summary>The file part a part was holed from (itself when it has no holes).</summary>
    public static ConstructPartDef BaseOf(ConstructPartDef d)
    {
        lock (Interned)
            return Info.TryGetValue(d, out var m) ? m.Base : d;
    }

    /// <summary>The holes a part has (none for a whole part).</summary>
    public static Hole[] HolesOf(ConstructPartDef d)
    {
        lock (Interned)
            return Info.TryGetValue(d, out var m) ? m.Holes : [];
    }

    public static bool IsHoled(ConstructPartDef d) => d.IsNamedRuntime && d.Name.StartsWith(Prefix, StringComparison.Ordinal);

    /// <summary>Can this file part take holes? Structure, plates, foundations and tanks with no moving, lit or worked boxes.</summary>
    public static string? Refusal(ConstructPartDef b)
    {
        if (b.Id >= ConstructParts.MaxTypes || b.RuntimeMade)
            return "made at run time";
        if (b.Role is not (PartRole.Frame or PartRole.Panel or PartRole.Foundation or PartRole.FuelTank or PartRole.FluidTank or PartRole.Storage))
            return $"role {b.Role}";
        if (b.HasJoint || b.Rotor != null || b.HasRig || b.HasWork || b.HasEngineBoxes || b.HasRouterBoxes || b.UseBoxes.Length > 0 || b.StatusBox >= 0
            || b.Panels.Length > 0 || b.Explosive != null || b.IsEnvelope || b.Core || b.DamagedTo >= 0 || b.Stage)
            return "it has boxes or a job a hole would break";
        return null;
    }

    /// <summary>
    /// <paramref name="d"/> with one more hole (made the first time its key is asked for). Null: the part can't take holes, or has all it
    /// may, or the cut would pass a limit (<see cref="LastRefusal"/> says which).
    /// </summary>
    public static ConstructPartDef? WithHole(ConstructPartDef d, Hole h)
    {
        var b = BaseOf(d);
        var had = HolesOf(d);
        if (Refusal(b) is { } why)
            return Refuse(why);
        if (had.Length >= MaxHoles)
            return Refuse($"{MaxHoles} holes already");
        var key = KeyOf(b, [.. had, h]);
        if (key.Length > ConstructParts.MaxRuntimeName)
            return Refuse("the key is too long");
        return Parse(key) ?? Refuse(LastRefusal.Length > 0 ? LastRefusal : "the cut failed");
    }

    private static ConstructPartDef? Refuse(string why)
    {
        LastRefusal = why;
        Refused++;
        return null;
    }

    public static string KeyOf(ConstructPartDef b, IReadOnlyList<Hole> holes)
    {
        var s = new StringBuilder(Prefix).Append(b.Name).Append(Split);
        for (var i = 0; i < holes.Count; i++)
            s.Append(i == 0 ? "" : ";").Append(holes[i].Text);
        return s.Append(')').ToString();
    }

    /// <summary>The holed part a key names, or null when it isn't one (or can't be made).</summary>
    public static ConstructPartDef? Parse(string name)
    {
        if (!name.StartsWith(Prefix, StringComparison.Ordinal) || !name.EndsWith(')') || name.Length > ConstructParts.MaxRuntimeName)
            return null;
        var cut = name.LastIndexOf(Split, StringComparison.Ordinal);
        if (cut <= Prefix.Length)
            return null;
        var baseName = name[Prefix.Length..cut];
        var list = name[(cut + Split.Length)..^1].Split(';');
        if (list.Length is 0 or > MaxHoles)
            return null;
        var holes = new Hole[list.Length];
        for (var i = 0; i < list.Length; i++)
            if (!Hole.TryParse(list[i], out holes[i]))
                return null;
        if (ConstructParts.Find(baseName) is not { } b || Refusal(b) != null) // (before the lock: the first ask loads the parts)
            return null;
        lock (Interned)
        {
            if (Interned.TryGetValue(name, out var have))
            {
                if (have != null)
                    have.Touched = Environment.TickCount64;
                return have;
            }
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var made = Build(b, holes, name);
            if (made != null && !Extrusion.Adopt(made))
            {
                PartShapes.Release(made.OwnShapes);
                made = null;
                LastRefusal = "the run-time part table is full";
            }
            BuildMs += clock.Elapsed.TotalMilliseconds;
            Interned[name] = made;
            if (made != null)
            {
                Info[made] = new Made(b, holes);
                DefsMade++;
            }
            return made;
        }
    }

    /// <summary>A key off the wire (after <see cref="HullLoft.WireKind"/>), as this machine's part id: the whole part, never a dropped packet, when it can't be made.</summary>
    internal static ushort Read(string name)
    {
        if (Parse(name) is { } d)
            return d.Id;
        var cut = name.LastIndexOf(Split, StringComparison.Ordinal);
        return cut > Prefix.Length && ConstructParts.Find(name[Prefix.Length..cut]) is { Id: < ConstructParts.MaxTypes } b ? b.Id : ConstructParts.Need("Frame Beam").Id;
    }

    /// <summary>(RuntimeParts) Drops the doomed parts from the key table.</summary>
    internal static void Forget(Func<ConstructPartDef, bool> doomed)
    {
        lock (Interned)
        {
            RuntimeParts.DropOrNull(Interned, d => d == null || doomed(d));
            List<ConstructPartDef>? gone = null;
            foreach (var d in Info.Keys)
                if (doomed(d))
                    (gone ??= new()).Add(d);
            if (gone != null)
                foreach (var d in gone)
                    Info.Remove(d);
        }
    }

    // ---- The cut -----------------------------------------------------------------------------------------------------

    private static ConstructPartDef? Build(ConstructPartDef b, Hole[] holes, string name)
    {
        var outer = PartShapes.BeginCollect();
        ConstructPartDef? d = null;
        try
        {
            var list = new List<CPartBox>(b.Boxes);
            var next = new List<CPartBox>(list.Count + 16);
            foreach (var h in holes)
            {
                next.Clear();
                var o = h.Origin;
                var dir = h.Dir;
                var r = h.Radius;
                foreach (var x in list)
                    Cut(x, o, dir, r, next);
                (list, next) = (next, list);
                if (list.Count > ConstructParts.MaxBoxes)
                {
                    LastRefusal = $"over {ConstructParts.MaxBoxes} boxes";
                    return null;
                }
            }
            var solid = 0;
            foreach (var x in list)
                solid += x.Detail ? 0 : 1;
            if (solid == 0 || solid == list.Count && list.TrueForAll(x => x.Hidden))
            {
                LastRefusal = "nothing would be left of it";
                return null;
            }
            var boxes = new CPartBox[list.Count];
            int si = 0, di = solid;
            foreach (var x in list)
                boxes[x.Detail ? di++ : si++] = x;
            d = b.ShallowCopy();
            d.Name = name;
            d.File = "";
            d.Description = $"{b.Description} Holed through ({holes.Length}).";
            d.Boxes = boxes;
            d.SolidBoxes = solid;
            d.HasGlass = Array.Exists(boxes, x => x.IsGlass);
            d.Hulls = null;
            d.HullCovers = null;
            d.Mass = Math.Clamp(b.Mass * SolidVolume(boxes, solid) / MathF.Max(SolidVolume(b.Boxes, b.SolidBoxes), 1e-6f), b.Mass * 0.1f, b.Mass);
            d.PhysicsOnly = true;
            d.PhysicsWhy = "holed";
            d.Stage = true; // (never offered: a hole makes it)
            d.IsNamedRuntime = true;
            d.MatBase = null;
            d.ExHash = Fnv(name);
            return d;
        }
        finally
        {
            var shapes = PartShapes.EndCollect(outer);
            if (d != null)
                d.OwnShapes = shapes;
            else
                PartShapes.Release(shapes);
        }
    }

    private static float SolidVolume(CPartBox[] boxes, int solid)
    {
        var v = 0f;
        for (var i = 0; i < solid; i++)
            v += boxes[i].Half.X * boxes[i].Half.Y * boxes[i].Half.Z * 8;
        return v;
    }

    /// <summary>Box <paramref name="x"/> with the tunnel of line (o, d) and radius r taken out of it, into <paramref name="into"/>.</summary>
    private static void Cut(in CPartBox x, Vector3 o, Vector3 d, float r, List<CPartBox> into)
    {
        if (x.Shape != 0 && PartShapes.KindOf(x.Shape) == "hull" && CutFacet(x, o, d, r, into))
            return;
        var lo = x.Center - x.Half;
        var hi = x.Center + x.Half;
        if (!Crosses(lo - new Vector3(r), hi + new Vector3(r), o, d))
        {
            into.Add(x);
            return;
        }
        if (x.Shape != 0 && x.Detail)
        {
            PiecesDropped++; // (a shaped detail the tunnel reaches: gone; the hidden boxes under it are cut)
            return;
        }
        // The tunnel runs along the box's axis that opens least: a plate's thickness, a beam's length when the line runs down it.
        var best = -1;
        float bestArea = float.MaxValue, bu = 0, bv = 0;
        for (var a = 0; a < 3; a++)
        {
            var da = MathF.Abs(d[a]);
            if (da < 0.2f)
                continue;
            int u = (a + 1) % 3, v = (a + 2) % 3;
            var hu = r + MathF.Abs(d[u] / d[a]) * x.Half[a];
            var hv = r + MathF.Abs(d[v] / d[a]) * x.Half[a];
            if (hu * hv < bestArea)
                (best, bestArea, bu, bv) = (a, hu * hv, hu, hv);
        }
        if (best < 0)
        {
            into.Add(x);
            return;
        }
        int ax = best, ux = (best + 1) % 3, vx = (best + 2) % 3;
        var p = o + d * ((x.Center[ax] - o[ax]) / d[ax]);
        float u0 = p[ux] - bu, u1 = p[ux] + bu, v0 = p[vx] - bv, v1 = p[vx] + bv;
        if (u1 <= lo[ux] || u0 >= hi[ux] || v1 <= lo[vx] || v0 >= hi[vx])
        {
            into.Add(x);
            return;
        }
        BoxesCut++;
        float cu0 = MathF.Max(lo[ux], u0), cu1 = MathF.Min(hi[ux], u1);
        Piece(x, ax, ux, vx, lo[ux], u0, lo[vx], hi[vx], into);
        Piece(x, ax, ux, vx, u1, hi[ux], lo[vx], hi[vx], into);
        Piece(x, ax, ux, vx, cu0, cu1, lo[vx], v0, into);
        Piece(x, ax, ux, vx, cu0, cu1, v1, hi[vx], into);
    }

    private static void Piece(in CPartBox x, int ax, int ux, int vx, float u0, float u1, float v0, float v1, List<CPartBox> into)
    {
        if (u1 - u0 < MinPiece || v1 - v0 < MinPiece)
            return;
        var c = x.Center;
        var h = x.Half;
        c[ux] = (u0 + u1) * 0.5f;
        h[ux] = (u1 - u0) * 0.5f;
        c[vx] = (v0 + v1) * 0.5f;
        h[vx] = (v1 - v0) * 0.5f;
        into.Add(x with { Center = c, Half = h, Shape = 0 });
    }

    /// <summary>Does the line (o, d) pass through the box?</summary>
    private static bool Crosses(Vector3 lo, Vector3 hi, Vector3 o, Vector3 d)
    {
        float t0 = float.NegativeInfinity, t1 = float.PositiveInfinity;
        for (var a = 0; a < 3; a++)
        {
            if (MathF.Abs(d[a]) < 1e-7f)
            {
                if (o[a] < lo[a] || o[a] > hi[a])
                    return false;
                continue;
            }
            var ta = (lo[a] - o[a]) / d[a];
            var tb = (hi[a] - o[a]) / d[a];
            t0 = MathF.Max(t0, MathF.Min(ta, tb));
            t1 = MathF.Min(t1, MathF.Max(ta, tb));
        }
        return t1 >= t0;
    }

    /// <summary>
    /// A flat hull facet cut in its own plane: up to four convex facets round a square opening (long along the line's slant across the
    /// sheet). False: not a thin sheet, so the caller treats it as any other shaped box.
    /// </summary>
    private static bool CutFacet(in CPartBox x, Vector3 o, Vector3 d, float r, List<CPartBox> into)
    {
        var mesh = PartShapes.Mesh(x.Shape);
        if (mesh.Length < PartShapes.FloatsPerVertex * 3)
            return false;
        var size = x.Half * 2;
        var pts = new List<Vector3>(mesh.Length / PartShapes.FloatsPerVertex);
        var n = Vector3.Zero;
        var bestArea = 0f;
        for (var k = 0; k + PartShapes.FloatsPerVertex * 3 <= mesh.Length; k += PartShapes.FloatsPerVertex * 3)
        {
            var a = x.Center + new Vector3(mesh[k], mesh[k + 1], mesh[k + 2]) * size;
            var b = x.Center + new Vector3(mesh[k + 6], mesh[k + 7], mesh[k + 8]) * size;
            var c = x.Center + new Vector3(mesh[k + 12], mesh[k + 13], mesh[k + 14]) * size;
            var cr = Vector3.Cross(b - a, c - a);
            if (cr.LengthSquared() > bestArea)
                (bestArea, n) = (cr.LengthSquared(), cr);
            AddPoint(pts, a);
            AddPoint(pts, b);
            AddPoint(pts, c);
        }
        if (bestArea < 1e-10f)
            return false;
        n = Vector3.Normalize(n);
        var u0 = MathF.Abs(n.X) < 0.9f ? Vector3.Normalize(Vector3.Cross(n, Vector3.UnitX)) : Vector3.Normalize(Vector3.Cross(n, Vector3.UnitY));
        var v0 = Vector3.Cross(n, u0);
        float lo = float.MaxValue, hi = float.MinValue, minU = float.MaxValue, maxU = float.MinValue, minV = float.MaxValue, maxV = float.MinValue;
        foreach (var p in pts)
        {
            var w = Vector3.Dot(p, n);
            (lo, hi) = (MathF.Min(lo, w), MathF.Max(hi, w));
            var pu = Vector3.Dot(p, u0);
            var pv = Vector3.Dot(p, v0);
            (minU, maxU, minV, maxV) = (MathF.Min(minU, pu), MathF.Max(maxU, pu), MathF.Min(minV, pv), MathF.Max(maxV, pv));
        }
        var thick = hi - lo;
        if (thick > ThinMax || thick > ThinShare * MathF.Min(maxU - minU, maxV - minV))
            return false;
        var dn = Vector3.Dot(d, n);
        if (MathF.Abs(dn) < 0.15f)
        {
            into.Add(x); // (the line runs along the sheet: no opening through it)
            return true;
        }
        var at = o + d * ((lo + hi) * 0.5f - Vector3.Dot(o, n)) / dn;
        var slant = d - n * dn;
        var uu = slant.LengthSquared() > 1e-6f ? Vector3.Normalize(slant) : u0;
        var vv = Vector3.Cross(n, uu);
        var hu = r + slant.Length() / MathF.Abs(dn) * thick * 0.5f;
        var hv = r;
        var poly = new List<Vector2>(pts.Count);
        foreach (var p in pts)
            poly.Add(new Vector2(Vector3.Dot(p, uu), Vector3.Dot(p, vv)));
        poly = Hull2(poly);
        var pu0 = Vector3.Dot(at, uu);
        var pv0 = Vector3.Dot(at, vv);
        var whole = Area(poly);
        var pa = Clip(poly, new Vector2(1, 0), pu0 - hu);
        var pb = Clip(poly, new Vector2(-1, 0), -(pu0 + hu));
        var mid = Clip(Clip(poly, new Vector2(-1, 0), -(pu0 - hu)), new Vector2(1, 0), pu0 + hu);
        var pc = Clip(mid, new Vector2(0, 1), pv0 - hv);
        var pd = Clip(mid, new Vector2(0, -1), -(pv0 + hv));
        if (Area(pa) + Area(pb) + Area(pc) + Area(pd) >= whole - 1e-5f)
        {
            into.Add(x); // (the opening misses this facet)
            return true;
        }
        FacetsCut++;
        foreach (var piece in (List<Vector2>[])[pa, pb, pc, pd])
            Facet(x, piece, uu, vv, n, lo, hi, into);
        return true;
    }

    private static void AddPoint(List<Vector3> pts, Vector3 p)
    {
        foreach (var q in pts)
            if (Vector3.DistanceSquared(p, q) < 1e-8f)
                return;
        pts.Add(p);
    }

    private static void Facet(in CPartBox x, List<Vector2> poly, Vector3 uu, Vector3 vv, Vector3 n, float lo, float hi, List<CPartBox> into)
    {
        if (poly.Count < 3 || Area(poly) < MinPiece * MinPiece * 4)
            return;
        var pts = new Vector3[poly.Count * 2];
        var mn = new Vector3(float.MaxValue);
        var mx = new Vector3(float.MinValue);
        for (var i = 0; i < poly.Count; i++)
        {
            var at = uu * poly[i].X + vv * poly[i].Y;
            pts[i * 2] = at + n * lo;
            pts[i * 2 + 1] = at + n * hi;
            mn = Vector3.Min(mn, Vector3.Min(pts[i * 2], pts[i * 2 + 1]));
            mx = Vector3.Max(mx, Vector3.Max(pts[i * 2], pts[i * 2 + 1]));
        }
        var centre = (mn + mx) * 0.5f;
        var half = Vector3.Max((mx - mn) * 0.5f, new Vector3(0.0005f));
        for (var i = 0; i < pts.Length; i++)
            pts[i] -= centre;
        if (pts.Length > 64)
        {
            PiecesDropped++;
            return;
        }
        try
        {
            var id = PartShapes.For(new PartBox { Shape = "hull", Size = half * 2, Points = pts });
            into.Add(x with { Center = centre, Half = half, Shape = id });
        }
        catch (WeaponFileException)
        {
            PiecesDropped++; // (too thin or too many corners to make a hull of)
        }
    }

    /// <summary>The convex hull of 2D points, anticlockwise (Andrew's monotone chain).</summary>
    private static List<Vector2> Hull2(List<Vector2> p)
    {
        p.Sort((a, b) => a.X != b.X ? a.X.CompareTo(b.X) : a.Y.CompareTo(b.Y));
        var h = new List<Vector2>(p.Count * 2);
        static float Turn(Vector2 o, Vector2 a, Vector2 b) => (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);
        foreach (var q in p)
        {
            while (h.Count >= 2 && Turn(h[^2], h[^1], q) <= 1e-9f)
                h.RemoveAt(h.Count - 1);
            h.Add(q);
        }
        var lower = h.Count + 1;
        for (var i = p.Count - 2; i >= 0; i--)
        {
            while (h.Count >= lower && Turn(h[^2], h[^1], p[i]) <= 1e-9f)
                h.RemoveAt(h.Count - 1);
            h.Add(p[i]);
        }
        if (h.Count > 1)
            h.RemoveAt(h.Count - 1);
        return h;
    }

    /// <summary>The part of a convex polygon where dot(k, p) is at most c (Sutherland-Hodgman against one line).</summary>
    private static List<Vector2> Clip(List<Vector2> poly, Vector2 k, float c)
    {
        var o = new List<Vector2>(poly.Count + 2);
        for (var i = 0; i < poly.Count; i++)
        {
            var a = poly[i];
            var b = poly[(i + 1) % poly.Count];
            var da = Vector2.Dot(k, a) - c;
            var db = Vector2.Dot(k, b) - c;
            if (da <= 0)
                o.Add(a);
            if (da < 0 && db > 0 || da > 0 && db < 0)
                o.Add(a + (b - a) * (da / (da - db)));
        }
        return o;
    }

    private static float Area(List<Vector2> p)
    {
        var s = 0f;
        for (var i = 0; i < p.Count; i++)
        {
            var a = p[i];
            var b = p[(i + 1) % p.Count];
            s += a.X * b.Y - b.X * a.Y;
        }
        return MathF.Abs(s) * 0.5f;
    }

    private static uint Fnv(string s)
    {
        var h = 2166136261u;
        foreach (var ch in s)
            h = (h ^ ch) * 16777619u;
        return h;
    }

    // ---- The railgun's line --------------------------------------------------------------------------------------------

    /// <summary>What one line through a body did.</summary>
    public struct BoreReport
    {
        public int Parts, Holed, Refused, Skipped;
        public double Ms;
    }

    private static int[] _rayParts = new int[256];
    private static readonly List<(float T, int Part)> _hits = new(64);

    /// <summary>
    /// The railgun's line through <paramref name="c"/> (server): every part it crosses within <paramref name="reach"/>, nearest first, takes
    /// a hole of <paramref name="radius"/> until <paramref name="power"/> is spent (each holed part's armour comes off it). Parts that can't
    /// take a hole are passed (counted as skipped). The body is marked for a rebuild and its structure goes out, as a wall losing a chunk does.
    /// </summary>
    public static BoreReport Bore(ConstructWorld w, Construct c, Vector3 from, Vector3 dir, float radius, float reach, float power = 100000f)
    {
        var rep = new BoreReport();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        dir = Vector3.Normalize(dir);
        if (_rayParts.Length < c.PartCount + 64)
            _rayParts = new int[c.PartCount + 1024];
        var n = c.PartsOnRay(c.ToLocal(from), Vector3.Transform(dir, Quaternion.Conjugate(c.Rot)), reach, _rayParts);
        _hits.Clear();
        for (var k = 0; k < n; k++)
        {
            var i = _rayParts[k];
            if (c.Rides(i))
                continue;
            Freeform.PartWorld(c, i, out var pos, out var rot);
            var iq = Quaternion.Conjugate(rot);
            var o = Vector3.Transform(from - pos, iq);
            var d = Vector3.Transform(dir, iq);
            var near = float.MaxValue;
            foreach (var x in c.DefOf(i).Boxes)
                if (Entry(x.Center - x.Half - new Vector3(radius), x.Center + x.Half + new Vector3(radius), o, d, reach) is var t and >= 0 && t < near)
                    near = t;
            if (near < float.MaxValue)
                _hits.Add((near, i));
        }
        _hits.Sort((a, b) => a.T.CompareTo(b.T));
        rep.Parts = _hits.Count;
        foreach (var (_, i) in _hits)
        {
            if (power <= 0)
                break;
            ref var p = ref c.Parts[i];
            var d = c.DefOf(i);
            if (c.MergeOf[i] != 0 || p.Fuse != 0 || p.Broken || Refusal(BaseOf(d)) != null)
            {
                rep.Skipped++;
                continue;
            }
            Freeform.PartWorld(c, i, out var pos, out var rot);
            var iq = Quaternion.Conjugate(rot);
            var o = Vector3.Transform(from - pos, iq);
            var ld = Vector3.Transform(dir, iq);
            var mid = (d.BoundsMin + d.BoundsMax) * 0.5f;
            o += ld * Vector3.Dot(mid - o, ld); // (the point on the line nearest the part's middle: small numbers in the key)
            if (WithHole(d, Hole.Of(o, ld, radius)) is not { } holed)
            {
                rep.Refused++;
                continue;
            }
            p.Def = holed.Id;
            rep.Holed++;
            power -= MathF.Max(1f, d.Armour);
        }
        if (rep.Holed > 0)
        {
            c.Dirty = true;
            w.StructureDirty.Add(c.Id);
            c.Wake();
        }
        rep.Ms = clock.Elapsed.TotalMilliseconds;
        return rep;
    }

    /// <summary>Where a ray from <paramref name="o"/> along <paramref name="d"/> enters the box within <paramref name="far"/>, or -1.</summary>
    private static float Entry(Vector3 lo, Vector3 hi, Vector3 o, Vector3 d, float far)
    {
        float t0 = 0, t1 = far;
        for (var a = 0; a < 3; a++)
        {
            if (MathF.Abs(d[a]) < 1e-7f)
            {
                if (o[a] < lo[a] || o[a] > hi[a])
                    return -1;
                continue;
            }
            var ta = (lo[a] - o[a]) / d[a];
            var tb = (hi[a] - o[a]) / d[a];
            t0 = MathF.Max(t0, MathF.Min(ta, tb));
            t1 = MathF.Min(t1, MathF.Max(ta, tb));
        }
        return t1 >= t0 ? t0 : -1;
    }
}
