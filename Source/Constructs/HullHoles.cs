using System.Globalization;
using System.Numerics;
using System.Text;

namespace CallOfSlop;

/// <summary>
/// Holes through parts (Karl, 2026-11-18: a boarding cutter takes a door-sized hole out of a hull piece's mesh and collision, so you
/// see space through it and walk through the gap; the phase 0 spike of "Punching holes through capital ships", draft PR 201,
/// docs/sandbox/design/hull-holes-spike.md, is where the cut was first tried). Used by the breaching cutter (ConstructBreach.cs).
/// <para>
/// A holed part is a named run-time part (<see cref="ConstructPartDef.IsNamedRuntime"/>, as a cut panel or a pressed part is): its key is
/// <c>Holed (Zero Day Upper Fore Hull Port 1 | x,y,z,dx,dy,dz,ux,uy,uz,w,h,l;...)</c>, the file part's name, then each hole as a
/// rectangular tunnel in the part's own frame: the middle of its mouth (cm), the way in (thousandths), its up (thousandths), its half
/// width and half height (cm) and its depth (cm). Every machine makes the same part from the key, so saves, blueprints and the wire
/// (after <see cref="Extrusion.WireMark"/> and <see cref="HullLoft.WireKind"/>, as text) carry it with nothing new.
/// </para>
/// <para>
/// The cut, per box: a plain box (solid, hidden or drawn) loses the tunnel along the axis it runs down most, as up to four boxes round
/// it (the slices of the box before and past the tunnel's depth stay whole); a thin hull facet (<c>"shape": "hull"</c>, the ships'
/// skin, which collides since PR 275) is cut in its own plane into up to four convex facets round the opening; any other shaped detail
/// the tunnel reaches is left out. A part keeps its sockets, health and links; its mass goes with the solid volume left.
/// </para>
/// </summary>
public static class HullHoles
{
    public const string Prefix = "Holed (";
    private const string Split = " | ";

    /// <summary>Holes a part may take (the key holds them all).</summary>
    public const int MaxHoles = 4;

    /// <summary>A hull facet thinner than this share of its narrower side, and than <see cref="ThinMax"/>, is cut as a sheet.</summary>
    private const float ThinShare = 0.3f, ThinMax = 0.6f;

    /// <summary>A piece thinner than this (m) is left out.</summary>
    private const float MinPiece = 0.004f;

    /// <summary>One hole: a rectangular tunnel in the part's frame.</summary>
    public readonly record struct Hole(int X, int Y, int Z, short Dx, short Dy, short Dz, short Ux, short Uy, short Uz, ushort W, ushort H, ushort L)
    {
        public Vector3 Origin => new Vector3(X, Y, Z) / 100f;
        public Vector3 Dir => Vector3.Normalize(new Vector3(Dx, Dy, Dz));
        public float HalfW => W / 100f;
        public float HalfH => H / 100f;
        public float Depth => L / 100f;

        /// <summary>Its up, square to its way in.</summary>
        public Vector3 Up
        {
            get
            {
                var d = Dir;
                var u = new Vector3(Ux, Uy, Uz);
                u -= d * Vector3.Dot(u, d);
                if (u.LengthSquared() < 1e-6f)
                    u = MathF.Abs(d.Y) < 0.9f ? Vector3.UnitY - d * d.Y : Vector3.UnitX - d * d.X;
                return Vector3.Normalize(u);
            }
        }

        public string Text => string.Create(CultureInfo.InvariantCulture, $"{X},{Y},{Z},{Dx},{Dy},{Dz},{Ux},{Uy},{Uz},{W},{H},{L}");

        public static Hole Of(Vector3 origin, Vector3 dir, Vector3 up, float halfW, float halfH, float depth)
        {
            dir = Vector3.Normalize(dir);
            up = Vector3.Normalize(up);
            static short T(float v) => (short)Math.Clamp((int)MathF.Round(v * 1000), -1000, 1000);
            static ushort C(float v) => (ushort)Math.Clamp((int)MathF.Round(v * 100), 1, 2000);
            return new Hole((int)MathF.Round(origin.X * 100), (int)MathF.Round(origin.Y * 100), (int)MathF.Round(origin.Z * 100),
                T(dir.X), T(dir.Y), T(dir.Z), T(up.X), T(up.Y), T(up.Z), C(halfW), C(halfH), C(depth));
        }

        public static bool TryParse(string s, out Hole h)
        {
            h = default;
            var f = s.Split(',');
            if (f.Length != 12)
                return false;
            Span<int> v = stackalloc int[12];
            for (var i = 0; i < 12; i++)
                if (!int.TryParse(f[i], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out v[i]))
                    return false;
            for (var i = 3; i < 9; i++)
                if (Math.Abs(v[i]) > 1000)
                    return false;
            if (v[3] == 0 && v[4] == 0 && v[5] == 0)
                return false;
            for (var i = 9; i < 12; i++)
                if (v[i] < 1 || v[i] > 2000)
                    return false;
            if (Math.Abs(v[0]) > 40000 || Math.Abs(v[1]) > 40000 || Math.Abs(v[2]) > 40000)
                return false;
            h = new Hole(v[0], v[1], v[2], (short)v[3], (short)v[4], (short)v[5], (short)v[6], (short)v[7], (short)v[8], (ushort)v[9], (ushort)v[10], (ushort)v[11]);
            return true;
        }
    }

    private sealed record Made(ConstructPartDef Base, Hole[] Holes);

    /// <summary>The tables, made on the first hole (a nested class, so a sweep or a look before any hole allocates nothing).</summary>
    private static class Store
    {
        internal static readonly Dictionary<string, ConstructPartDef?> Interned = new(StringComparer.Ordinal);
        internal static readonly Dictionary<ConstructPartDef, Made> Info = new();
    }

    /// <summary>Set by the first <see cref="Parse"/>: until then there is nothing to look up or forget.</summary>
    private static volatile bool _used;

    /// <summary>Counts for a check: parts made, the time it took, pieces left out, refusals.</summary>
    public static int DefsMade, Refused, PiecesDropped, FacetsCut, BoxesCut;
    public static double BuildMs;

    /// <summary>Why the last <see cref="WithHole"/> or <see cref="Parse"/> gave nothing ("misses": the tunnel reaches none of its boxes).</summary>
    [ThreadStatic] public static string? LastRefusal;

    public const string Misses = "misses";
    public const string NothingLeft = "nothing would be left of it";

    /// <summary>The file part a part was holed from (itself when it has no holes).</summary>
    public static ConstructPartDef BaseOf(ConstructPartDef d)
    {
        if (!_used)
            return d;
        lock (Store.Interned)
            return Store.Info.TryGetValue(d, out var m) ? m.Base : d;
    }

    /// <summary>The holes a part has (none for a whole part).</summary>
    public static Hole[] HolesOf(ConstructPartDef d)
    {
        if (!_used)
            return [];
        lock (Store.Interned)
            return Store.Info.TryGetValue(d, out var m) ? m.Holes : [];
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
            || b.Panels.Length > 0 || b.Explosive != null || b.IsEnvelope || b.DamagedTo >= 0 || b.Stage || b.Breach != BreachKind.None)
            return "it has boxes or a job a hole would break";
        return null;
    }

    /// <summary>
    /// <paramref name="d"/> with one more hole (made the first time its key is asked for). Null: the part can't take holes, or has all it
    /// may, or the hole misses it, or the cut would pass a limit (<see cref="LastRefusal"/> says which).
    /// </summary>
    public static ConstructPartDef? WithHole(ConstructPartDef d, Hole h)
    {
        var b = BaseOf(d);
        var had = HolesOf(d);
        if (Refusal(b) is { } why)
            return Refuse(why);
        if (had.Length >= MaxHoles)
            return Refuse($"{MaxHoles} holes already");
        if (!Reaches(d, h))
            return Refuse(Misses);
        var key = KeyOf(b, [.. had, h]);
        if (key.Length > ConstructParts.MaxRuntimeName)
            return Refuse("the key is too long");
        LastRefusal = null;
        return Parse(key) ?? Refuse(LastRefusal ?? "the cut failed");
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
        _used = true;
        var Interned = Store.Interned;
        var Info = Store.Info;
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
            if (made == null)
                Console.WriteLine($"[holes] REFUSED \"{name}\": {LastRefusal}");
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
        if (_used)
            ForgetUsed(doomed); // (the lambda in a method of its own: its closure is made when the scope opens)
    }

    private static void ForgetUsed(Func<ConstructPartDef, bool> doomed)
    {
        var Interned = Store.Interned;
        var Info = Store.Info;
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

    // ---- The tunnel --------------------------------------------------------------------------------------------------

    /// <summary>A hole's tunnel in the part's frame: mouth middle, way in, across, up, half sizes, depth.</summary>
    private readonly struct Tunnel
    {
        public readonly Vector3 O, D, U, V;
        public readonly float W, H, L;

        public Tunnel(in Hole h)
        {
            O = h.Origin;
            D = h.Dir;
            V = h.Up;
            U = Vector3.Normalize(Vector3.Cross(V, D));
            W = h.HalfW;
            H = h.HalfH;
            L = h.Depth;
        }

        /// <summary>Its eight corners' bounds.</summary>
        public void Bounds(out Vector3 lo, out Vector3 hi)
        {
            lo = new Vector3(float.MaxValue);
            hi = new Vector3(float.MinValue);
            for (var k = 0; k < 8; k++)
            {
                var p = O + D * ((k & 1) != 0 ? L : 0) + U * ((k & 2) != 0 ? W : -W) + V * ((k & 4) != 0 ? H : -H);
                lo = Vector3.Min(lo, p);
                hi = Vector3.Max(hi, p);
            }
        }
    }

    /// <summary>Does the tunnel reach any of the part's boxes (their bounds, the skin's too)?</summary>
    private static bool Reaches(ConstructPartDef d, in Hole h)
    {
        new Tunnel(h).Bounds(out var lo, out var hi);
        foreach (var x in d.Boxes)
        {
            var a = x.Center - x.Half;
            var b = x.Center + x.Half;
            if (a.X < hi.X && b.X > lo.X && a.Y < hi.Y && b.Y > lo.Y && a.Z < hi.Z && b.Z > lo.Z)
                return true;
        }
        return false;
    }

    /// <summary>Does hole <paramref name="local"/> (in the part's frame) reach part bounds <paramref name="mn"/>..<paramref name="mx"/>?</summary>
    public static bool ReachesBounds(Vector3 mn, Vector3 mx, in Hole local)
    {
        new Tunnel(local).Bounds(out var lo, out var hi);
        return mn.X < hi.X && mx.X > lo.X && mn.Y < hi.Y && mx.Y > lo.Y && mn.Z < hi.Z && mx.Z > lo.Z;
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
            var cutAny = false;
            foreach (var h in holes)
            {
                next.Clear();
                var t = new Tunnel(h);
                foreach (var x in list)
                    cutAny |= Cut(x, t, next);
                (list, next) = (next, list);
                if (list.Count > ConstructParts.MaxBoxes)
                {
                    LastRefusal = $"over {ConstructParts.MaxBoxes} boxes";
                    return null;
                }
            }
            if (!cutAny)
            {
                LastRefusal = Misses;
                return null;
            }
            var solid = 0;
            foreach (var x in list)
                solid += x.Detail ? 0 : 1;
            if (solid == 0 || solid == list.Count && list.TrueForAll(x => x.Hidden))
            {
                LastRefusal = NothingLeft;
                return null;
            }
            var boxes = new CPartBox[list.Count];
            int si = 0, di = solid;
            foreach (var x in list)
                boxes[x.Detail ? di++ : si++] = x;
            d = b.ShallowCopy();
            d.Name = name;
            d.File = "";
            d.Description = $"{b.Description} Cut through ({holes.Length}).";
            d.Boxes = boxes;
            d.SolidBoxes = solid;
            d.HasGlass = Array.Exists(boxes, x => x.IsGlass);
            d.Hulls = null; // (made again from the cut boxes on first use: ConvexHull.Of)
            d.HullCovers = null;
            d.HullSkin = null;
            d.Mass = Math.Clamp(b.Mass * SolidVolume(boxes, solid) / MathF.Max(SolidVolume(b.Boxes, b.SolidBoxes), 1e-6f), b.Mass * 0.1f, b.Mass);
            d.PhysicsOnly = true;
            d.PhysicsWhy = "holed";
            d.Stage = true; // (never offered: a cutter makes it)
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

    /// <summary>Box <paramref name="x"/> with the tunnel taken out of it, into <paramref name="into"/>. True: it was cut.</summary>
    private static bool Cut(in CPartBox x, in Tunnel t, List<CPartBox> into)
    {
        if (x.Shape != 0 && PartShapes.KindOf(x.Shape) == "hull" && CutFacet(x, t, into, out var cut))
            return cut;
        var lo = x.Center - x.Half;
        var hi = x.Center + x.Half;
        t.Bounds(out var tlo, out var thi);
        if (!(lo.X < thi.X && hi.X > tlo.X && lo.Y < thi.Y && hi.Y > tlo.Y && lo.Z < thi.Z && hi.Z > tlo.Z))
        {
            into.Add(x);
            return false;
        }
        if (x.Shape != 0 && x.Detail)
        {
            PiecesDropped++; // (a shaped detail the tunnel reaches: gone; the boxes under it are cut)
            return true;
        }
        // The tunnel runs along the box's axis it goes down most.
        var d = t.D;
        var ax = MathF.Abs(d.X) >= MathF.Abs(d.Y) && MathF.Abs(d.X) >= MathF.Abs(d.Z) ? 0 : MathF.Abs(d.Y) >= MathF.Abs(d.Z) ? 1 : 2;
        int ux = (ax + 1) % 3, vx = (ax + 2) % 3;
        // Along that axis the tunnel spans its mouth to its depth (its corners: a slant widens it); the box's slices before and past it stay whole.
        float s0 = MathF.Max(lo[ax], tlo[ax]), s1 = MathF.Min(hi[ax], thi[ax]);
        if (s1 - s0 < MinPiece)
        {
            into.Add(x);
            return false;
        }
        // The opening in the box's other two axes: the tunnel's cross-section where its middle line crosses the slab's two ends.
        float u0 = float.MaxValue, u1 = float.MinValue, v0 = float.MaxValue, v1 = float.MinValue;
        var eu = MathF.Abs(t.U[ux]) * t.W + MathF.Abs(t.V[ux]) * t.H;
        var ev = MathF.Abs(t.U[vx]) * t.W + MathF.Abs(t.V[vx]) * t.H;
        for (var e = 0; e < 2; e++)
        {
            var s = e == 0 ? s0 : s1;
            var k = MathF.Abs(d[ax]) > 1e-4f ? Math.Clamp((s - t.O[ax]) / d[ax], 0, t.L) : 0;
            var p = t.O + d * k;
            u0 = MathF.Min(u0, p[ux] - eu);
            u1 = MathF.Max(u1, p[ux] + eu);
            v0 = MathF.Min(v0, p[vx] - ev);
            v1 = MathF.Max(v1, p[vx] + ev);
        }
        if (u1 <= lo[ux] || u0 >= hi[ux] || v1 <= lo[vx] || v0 >= hi[vx])
        {
            into.Add(x);
            return false;
        }
        BoxesCut++;
        Slab(x, ax, lo[ax], s0, into);
        Slab(x, ax, s1, hi[ax], into);
        float cu0 = MathF.Max(lo[ux], u0), cu1 = MathF.Min(hi[ux], u1);
        Piece(x, ax, s0, s1, ux, vx, lo[ux], u0, lo[vx], hi[vx], into);
        Piece(x, ax, s0, s1, ux, vx, u1, hi[ux], lo[vx], hi[vx], into);
        Piece(x, ax, s0, s1, ux, vx, cu0, cu1, lo[vx], v0, into);
        Piece(x, ax, s0, s1, ux, vx, cu0, cu1, v1, hi[vx], into);
        return true;
    }

    private static void Slab(in CPartBox x, int ax, float a0, float a1, List<CPartBox> into)
    {
        if (a1 - a0 < MinPiece)
            return;
        var c = x.Center;
        var h = x.Half;
        c[ax] = (a0 + a1) * 0.5f;
        h[ax] = (a1 - a0) * 0.5f;
        into.Add(x with { Center = c, Half = h, Shape = 0 });
    }

    private static void Piece(in CPartBox x, int ax, float a0, float a1, int ux, int vx, float u0, float u1, float v0, float v1, List<CPartBox> into)
    {
        if (u1 - u0 < MinPiece || v1 - v0 < MinPiece || a1 - a0 < MinPiece)
            return;
        var c = x.Center;
        var h = x.Half;
        c[ax] = (a0 + a1) * 0.5f;
        h[ax] = (a1 - a0) * 0.5f;
        c[ux] = (u0 + u1) * 0.5f;
        h[ux] = (u1 - u0) * 0.5f;
        c[vx] = (v0 + v1) * 0.5f;
        h[vx] = (v1 - v0) * 0.5f;
        into.Add(x with { Center = c, Half = h, Shape = 0 });
    }

    /// <summary>
    /// A flat hull facet cut in its own plane: up to four convex facets round the opening (the tunnel's cross-section where it meets the
    /// sheet). False: not a thin sheet, so the caller treats it as any other shaped box. <paramref name="cut"/>: it was cut.
    /// </summary>
    private static bool CutFacet(in CPartBox x, in Tunnel t, List<CPartBox> into, out bool cut)
    {
        cut = false;
        var mesh = PartShapes.Mesh(x.Shape);
        var fv = PartShapes.FloatsPerVertex;
        if (mesh.Length < fv * 3)
            return false;
        var pts = new List<Vector3>(mesh.Length / fv);
        var n = Vector3.Zero;
        var bestArea = 0f;
        for (var k = 0; k + fv * 3 <= mesh.Length; k += fv * 3)
        {
            var a = x.ShapePoint(new Vector3(mesh[k], mesh[k + 1], mesh[k + 2]));
            var b = x.ShapePoint(new Vector3(mesh[k + fv], mesh[k + fv + 1], mesh[k + fv + 2]));
            var c = x.ShapePoint(new Vector3(mesh[k + fv * 2], mesh[k + fv * 2 + 1], mesh[k + fv * 2 + 2]));
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
        var dn = Vector3.Dot(t.D, n);
        if (MathF.Abs(dn) < 0.15f)
        {
            into.Add(x); // (the tunnel runs along the sheet: no opening through it)
            return true;
        }
        // Where the tunnel's middle line meets the sheet: within its depth (and the sheet's thickness), or it misses.
        var along = ((lo + hi) * 0.5f - Vector3.Dot(t.O, n)) / dn;
        var slack = thick / MathF.Abs(dn);
        if (along < -slack || along > t.L + slack)
        {
            into.Add(x);
            return true;
        }
        var at = t.O + t.D * along;
        // The opening on the sheet: the tunnel's four edges carried along its way in onto the plane, as a rectangle round them along the
        // tunnel's up as it lies on the sheet.
        var vv = t.V - n * Vector3.Dot(t.V, n);
        vv = vv.LengthSquared() > 1e-6f ? Vector3.Normalize(vv) : v0;
        var uu = Vector3.Cross(n, vv);
        float ou0 = float.MaxValue, ou1 = float.MinValue, ov0 = float.MaxValue, ov1 = float.MinValue;
        for (var k = 0; k < 4; k++)
        {
            var c = at + t.U * ((k & 1) != 0 ? t.W : -t.W) + t.V * ((k & 2) != 0 ? t.H : -t.H);
            c -= t.D * (Vector3.Dot(c - at, n) / dn); // (onto the sheet's mid-plane along the way in)
            var cu = Vector3.Dot(c, uu);
            var cv = Vector3.Dot(c, vv);
            (ou0, ou1, ov0, ov1) = (MathF.Min(ou0, cu), MathF.Max(ou1, cu), MathF.Min(ov0, cv), MathF.Max(ov1, cv));
        }
        var slant = t.D - n * dn; // (through a slanted sheet the far face's opening is offset: take both)
        var su = MathF.Abs(Vector3.Dot(slant, uu)) / MathF.Abs(dn) * thick * 0.5f;
        var sv = MathF.Abs(Vector3.Dot(slant, vv)) / MathF.Abs(dn) * thick * 0.5f;
        ou0 -= su;
        ou1 += su;
        ov0 -= sv;
        ov1 += sv;
        var poly = new List<Vector2>(pts.Count);
        foreach (var p in pts)
            poly.Add(new Vector2(Vector3.Dot(p, uu), Vector3.Dot(p, vv)));
        poly = Hull2(poly);
        var whole = Area(poly);
        var pa = Clip(poly, new Vector2(1, 0), ou0);
        var pb = Clip(poly, new Vector2(-1, 0), -ou1);
        var mid = Clip(Clip(poly, new Vector2(-1, 0), -ou0), new Vector2(1, 0), ou1);
        var pc = Clip(mid, new Vector2(0, 1), ov0);
        var pd = Clip(mid, new Vector2(0, -1), -ov1);
        if (Area(pa) + Area(pb) + Area(pc) + Area(pd) >= whole - 1e-5f)
        {
            into.Add(x); // (the opening misses this facet)
            return true;
        }
        FacetsCut++;
        cut = true;
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
            into.Add(x with { Center = centre, Half = half, Shape = id, Front = 0 });
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
}
