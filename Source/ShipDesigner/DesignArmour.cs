using System.Text;
using System.Text.Json.Nodes;

namespace CallOfSlop.ShipDesigner;

/// <summary>
/// Stand-off armour plates (Karl, 2026-11-27: "giant armor plates, held off the hull from an arm... more akin to a wall"): a plate that
/// follows its hull's shape at a set stand-off, held off the skin on struts. It is dropped on a body's skin like any piece; its middle is
/// where it was dropped, its width runs along the body (z) and its height round the profile (metres of arc at its middle), and every point
/// of it lies on the skin's normal there, its stand-off out, so it bends with the hull as the skin does. Its outer face takes the hull
/// cladding styles (the same <see cref="DesignScene.LayPlates"/> the bodies and connections clad with); its inner, ship-facing face is
/// plain panels. In the game it is solid: one convex hull a cell of its plate and one a strut (DesignParts.Pieces.cs, ArmourHulls).
/// All its geometry, JSON and page fields are here, so the rest of the designer only calls in.
/// </summary>
public static class DesignArmour
{
    public const string Kind = "Armour plate";
    public const double MinW = 1, MaxW = 80, DefW = 8, MinH = 1, MaxH = 40, DefH = 4, MinT = 0.05, MaxT = 2, DefT = 0.3, MinOff = 0.3, MaxOff = 8, DefOff = 1.2;
    public const string DefClad = "Plates";
    public const double DefCladSize = 1.5;
    /// <summary>Struts about this far apart (m) each way, at least two each way on a plate long enough.</summary>
    private const double StrutSpacing = 3, StrutHalf = 0.12;
    /// <summary>The inner face's panels: about this size (m), the seam between them, and how far they stand off the plate's back (no shared plane).</summary>
    private const double PanelSize = 1.25, PanelSeam = 0.03, PanelRise = 0.006;
    private const string Back = "#3b3f44", Panel = "#62676d";
    /// <summary>The collider cells: about this size (m), at most this many in all.</summary>
    private const double CellSize = 2.5;
    private const int MaxCells = 32;

    public static readonly PieceKind[] Kinds =
    [
        new(Kind, "Armour", "skin", null, "", "A giant stand-off armour plate held off the hull on struts. It follows the hull's shape at its stand-off; set its width (along), height (round the hull), thickness and stand-off. Its outer face takes the hull cladding styles; its inner face is plain panels.", _ => { }),
    ];

    public static bool Is(Piece p) => p.Kind == Kind;

    public static JsonObject Defaults() => new() { ["armW"] = DefW, ["armH"] = DefH, ["armT"] = DefT, ["armOff"] = DefOff, ["armClad"] = DefClad, ["armCladSize"] = DefCladSize };

    /// <summary>A plate's JSON fields (only a plate's: every other piece's JSON stays as it was, so its design's hash does).</summary>
    public static JsonObject Write(Piece p, JsonObject o)
    {
        if (!Is(p))
            return o;
        o["armW"] = ShipDesign.R(p.ArmW);
        o["armH"] = ShipDesign.R(p.ArmH);
        o["armT"] = ShipDesign.R(p.ArmT);
        o["armOff"] = ShipDesign.R(p.ArmOff);
        o["armClad"] = p.ArmClad;
        o["armCladSize"] = ShipDesign.R(p.ArmCladSize);
        return o;
    }

    public static Piece Read(JsonObject o, Piece p)
    {
        if (!Is(p))
            return p;
        p.ArmW = Math.Clamp(ShipDesign.F(o["armW"], DefW), MinW, MaxW);
        p.ArmH = Math.Clamp(ShipDesign.F(o["armH"], DefH), MinH, MaxH);
        p.ArmT = Math.Clamp(ShipDesign.F(o["armT"], DefT), MinT, MaxT);
        p.ArmOff = Math.Clamp(ShipDesign.F(o["armOff"], DefOff), MinOff, MaxOff);
        p.ArmClad = HullBody.CladKinds.Contains((string?)o["armClad"]) ? (string)o["armClad"]! : DefClad;
        p.ArmCladSize = Math.Clamp(ShipDesign.F(o["armCladSize"], DefCladSize), HullBody.CladSizeMin, HullBody.CladSizeMax);
        return p;
    }

    /// <summary>The page's setPiece fields; an error text, or empty.</summary>
    public static string Set(Piece p, JsonObject o)
    {
        if (!Is(p))
            return "";
        static double Snap(double v, double s) => Math.Round(v / s) * s;
        if (o["armW"] is not null) p.ArmW = Math.Clamp(Snap(ShipDesign.F(o["armW"], DefW), 0.05), MinW, MaxW);
        if (o["armH"] is not null) p.ArmH = Math.Clamp(Snap(ShipDesign.F(o["armH"], DefH), 0.05), MinH, MaxH);
        if (o["armT"] is not null) p.ArmT = Math.Clamp(Snap(ShipDesign.F(o["armT"], DefT), 0.01), MinT, MaxT);
        if (o["armOff"] is not null) p.ArmOff = Math.Clamp(Snap(ShipDesign.F(o["armOff"], DefOff), 0.05), MinOff, MaxOff);
        if (o["armCladSize"] is not null) p.ArmCladSize = Math.Clamp(Snap(ShipDesign.F(o["armCladSize"], DefCladSize), 0.05), HullBody.CladSizeMin, HullBody.CladSizeMax);
        if ((string?)o["armClad"] is { } c)
        {
            if (!HullBody.CladKinds.Contains(c))
                return $"an armour plate is clad one of these ways: {string.Join(", ", HullBody.CladKinds)}";
            p.ArmClad = c;
        }
        return "";
    }

    /// <summary>The page's plate fields.</summary>
    public static string SceneJson(Piece p) => !Is(p) ? "" :
        new StringBuilder(",\"armour\":true,\"armW\":").Append(ShipDesign.S(p.ArmW)).Append(",\"armH\":").Append(ShipDesign.S(p.ArmH))
            .Append(",\"armT\":").Append(ShipDesign.S(p.ArmT)).Append(",\"armOff\":").Append(ShipDesign.S(p.ArmOff))
            .Append(",\"armClad\":").Append(DesignScene.Q(p.ArmClad)).Append(",\"armCladSize\":").Append(ShipDesign.S(p.ArmCladSize)).ToString();

    // ---- where it lies

    /// <summary>
    /// A plate's grid in the ship's frame: (Nz + 1) x (Nt + 1) points on the skin (S) with the way out of it (N, smoothed over the profile's
    /// corners so the plate rounds them at its stand-off), row i along z, column j round the profile; and its length and arc in metres.
    /// </summary>
    public sealed record Grid(int Nz, int Nt, double[][] S, double[][] N, double Len, double Arc)
    {
        public double[] At(int i, int j, double off) { var (s, n) = (S[i * (Nt + 1) + j], N[i * (Nt + 1) + j]); return [s[0] + n[0] * off, s[1] + n[1] * off, s[2] + n[2] * off]; }
        public double[] Nrm(int i, int j) => N[i * (Nt + 1) + j];

        /// <summary>A point at shares (u round, v along; 0..1) off the skin by <paramref name="off"/>, bilinear between the grid's points.</summary>
        public double[] Lerp(double u, double v, double off)
        {
            var (j, fu) = Cell(u, Nt);
            var (i, fv) = Cell(v, Nz);
            var a = At(i, j, off); var b = At(i, j + 1, off); var c = At(i + 1, j, off); var d = At(i + 1, j + 1, off);
            var r = new double[3];
            for (var k = 0; k < 3; k++)
                r[k] = (a[k] * (1 - fu) + b[k] * fu) * (1 - fv) + (c[k] * (1 - fu) + d[k] * fu) * fv;
            return r;
        }

        public double[] LerpN(double u, double v)
        {
            var (j, fu) = Cell(u, Nt);
            var (i, fv) = Cell(v, Nz);
            var r = new double[3];
            for (var k = 0; k < 3; k++)
                r[k] = (Nrm(i, j)[k] * (1 - fu) + Nrm(i, j + 1)[k] * fu) * (1 - fv) + (Nrm(i + 1, j)[k] * (1 - fu) + Nrm(i + 1, j + 1)[k] * fu) * fv;
            return Norm(r);
        }

        private static (int, double) Cell(double s, int n)
        {
            var q = Math.Clamp(s, 0, 1) * n;
            var i = Math.Min(n - 1, (int)Math.Floor(q));
            return (i, q - i);
        }
    }

    /// <summary>A plate's grid (null for any other piece, one off the skin, or one with no room on its body).</summary>
    public static Grid? GridOf(ShipDesign d, Piece p)
    {
        if (!Is(p) || p.Surface != "skin" || d.Hull(p.Hull) is not { } h)
            return null;
        var cx = h.CopyX(Math.Min(p.Copy, h.Copies - 1));
        double z0 = Math.Clamp(p.Z - p.ArmW / 2, h.Front + 0.05, h.Back - 0.05), z1 = Math.Clamp(p.Z + p.ArmW / 2, h.Front + 0.05, h.Back - 0.05);
        if (z1 - z0 < 0.2)
            return null;
        // The profile's arc at the plate's middle, to turn its height in metres into a share of t (each row keeps the same shares).
        const int K = 240;
        var arc = new double[K + 1];
        var prev = h.OnSkin(p.Z, 0, p.Side);
        for (var k = 1; k <= K; k++)
        {
            var s = h.OnSkin(p.Z, (double)k / K, p.Side);
            arc[k] = arc[k - 1] + Math.Sqrt((s.X - prev.X) * (s.X - prev.X) + (s.Y - prev.Y) * (s.Y - prev.Y));
            prev = s;
        }
        var total = arc[K];
        if (total < 0.2)
            return null;
        double TAt(double a)
        {
            a = Math.Clamp(a, 0, total);
            var k = 1;
            while (k < K && arc[k] < a) k++;
            var f = arc[k] - arc[k - 1] < 1e-9 ? 0 : (a - arc[k - 1]) / (arc[k] - arc[k - 1]);
            return (k - 1 + f) / K;
        }
        var mid = arc[(int)Math.Round(Math.Clamp(p.T, 0, 1) * K)];
        double a0 = Math.Clamp(mid - p.ArmH / 2, 0, total), a1 = Math.Clamp(mid + p.ArmH / 2, 0, total);
        if (a1 - a0 < 0.2)
            return null;
        double t0 = TAt(a0), t1 = TAt(a1);
        int nz = Math.Clamp((int)Math.Ceiling((z1 - z0) / 0.6), 1, 64), nt = Math.Clamp((int)Math.Ceiling((a1 - a0) / 0.3), 1, 96);
        var S = new double[(nz + 1) * (nt + 1)][];
        var N = new double[S.Length][];
        var eps = 0.5 * (t1 - t0) / nt;
        for (var i = 0; i <= nz; i++)
        {
            var z = z0 + (z1 - z0) * i / nz;
            for (var j = 0; j <= nt; j++)
            {
                var t = t0 + (t1 - t0) * j / nt;
                var s = h.OnSkin(z, t, p.Side);
                // Its way out: the skin's either side of it, so a corner of the profile is rounded at the stand-off, not split.
                var sa = h.OnSkin(z, Math.Max(0, t - eps), p.Side);
                var sb = h.OnSkin(z, Math.Min(1, t + eps), p.Side);
                S[i * (nt + 1) + j] = [cx + s.X, h.Y + s.Y, h.Z + s.Z];
                N[i * (nt + 1) + j] = Norm([s.Nx + sa.Nx + sb.Nx, s.Ny + sa.Ny + sb.Ny, 0]);
            }
        }
        return new Grid(nz, nt, S, N, z1 - z0, a1 - a0);
    }

    /// <summary>What a plate's mesh depends on beyond its own fields and the bodies' shapes: its body's skin colour and cladding material.</summary>
    public static string Key(ShipDesign d, Piece p) => Is(p) && d.Hull(p.Hull) is { } h ? $"|ar{h.Skin}" : "";

    // ---- the drawing

    /// <summary>Draws a plate (in the ship's frame): its outer face clad, its inner face panelled, its rim and its struts; false for any other piece.</summary>
    public static bool Draw(ShipDesign d, Piece p, DesignMesh m)
    {
        if (!Is(p))
            return false;
        if (GridOf(d, p) is not { } g || d.Hull(p.Hull) is not { } h)
            return true; // (no room on its body: nothing drawn)
        m.World();
        double off = p.ArmOff, top = p.ArmOff + p.ArmT;
        var skin = DesignMesh.Hex(h.Skin);
        var face = DesignMesh.Tone(skin, 0.92);
        var rim = DesignMesh.Tone(skin, 0.75);
        var back = DesignMesh.Hex(Back);
        int nz = g.Nz, nt = g.Nt;
        for (var i = 0; i < nz; i++)
            for (var j = 0; j < nt; j++)
            {
                var n = g.Nrm(i, j);
                Face(m, g.At(i, j, top), g.At(i + 1, j, top), g.At(i + 1, j + 1, top), g.At(i, j + 1, top), n, face);
                Face(m, g.At(i, j, off), g.At(i + 1, j, off), g.At(i + 1, j + 1, off), g.At(i, j + 1, off), [-n[0], -n[1], -n[2]], back);
            }
        // The rim: the plate's four edges, each facing out of the plate.
        for (var i = 0; i < nz; i++)
        {
            Face(m, g.At(i, 0, off), g.At(i + 1, 0, off), g.At(i + 1, 0, top), g.At(i, 0, top), Sub(g.At(i, 0, off), g.At(i, 1, off)), rim);
            Face(m, g.At(i, nt, off), g.At(i + 1, nt, off), g.At(i + 1, nt, top), g.At(i, nt, top), Sub(g.At(i, nt, off), g.At(i, nt - 1, off)), rim);
        }
        for (var j = 0; j < nt; j++)
        {
            Face(m, g.At(0, j, off), g.At(0, j + 1, off), g.At(0, j + 1, top), g.At(0, j, top), Sub(g.At(0, j, off), g.At(1, j, off)), rim);
            Face(m, g.At(nz, j, off), g.At(nz, j + 1, off), g.At(nz, j + 1, top), g.At(nz, j, top), Sub(g.At(nz, j, off), g.At(nz - 1, j, off)), rim);
        }
        InnerPanels(m, g, off);
        if (p.ArmClad != "Off")
            Clad(m, p, g, top, skin);
        Struts(m, g, off);
        return true;
    }

    /// <summary>The inner face's plain panels (Karl: "the side facing the ship will be a simple panel cladding style"): a grid of flat panels
    /// about <see cref="PanelSize"/> each, seamed, standing <see cref="PanelRise"/> in off the plate's back, each bent with the plate.</summary>
    private static void InnerPanels(DesignMesh m, Grid g, double off)
    {
        int pu = Math.Max(1, (int)Math.Round(g.Arc / PanelSize)), pv = Math.Max(1, (int)Math.Round(g.Len / PanelSize));
        double su = PanelSeam / 2 / g.Arc, sv = PanelSeam / 2 / g.Len;
        var col = DesignMesh.Hex(Panel);
        for (var a = 0; a < pu; a++)
            for (var b = 0; b < pv; b++)
            {
                double u0 = (double)a / pu + su, u1 = (double)(a + 1) / pu - su, v0 = (double)b / pv + sv, v1 = (double)(b + 1) / pv - sv;
                // Each panel in steps no longer than the grid's, so it follows the plate's curve.
                int ku = Math.Max(1, (int)Math.Ceiling((u1 - u0) * g.Nt)), kv = Math.Max(1, (int)Math.Ceiling((v1 - v0) * g.Nz));
                var tone = DesignMesh.Tone(col, 0.92 + 0.08 * ((a * 7 + b * 3) % 3) / 2.0);
                for (var x = 0; x < ku; x++)
                    for (var y = 0; y < kv; y++)
                    {
                        double ua = u0 + (u1 - u0) * x / ku, ub = u0 + (u1 - u0) * (x + 1) / ku, va = v0 + (v1 - v0) * y / kv, vb = v0 + (v1 - v0) * (y + 1) / kv;
                        var n = g.LerpN((ua + ub) / 2, (va + vb) / 2);
                        var o = off - PanelRise;
                        Face(m, g.Lerp(ua, va, o), g.Lerp(ub, va, o), g.Lerp(ub, vb, o), g.Lerp(ua, vb, o), [-n[0], -n[1], -n[2]], tone);
                    }
            }
    }

    /// <summary>The outer face clad in a hull cladding style (Plates, Heavy or Hex), the bodies' own plate layer laid over the plate's face.</summary>
    private static void Clad(DesignMesh m, Piece p, Grid g, double top, (byte, byte, byte) skin)
    {
        var size = Math.Clamp(p.ArmCladSize, HullBody.CladSizeMin, HullBody.CladSizeMax);
        int nu = Math.Clamp((int)Math.Round(g.Arc / size), 1, 64), nv = Math.Clamp((int)Math.Round(g.Len / size), 1, 64);
        (double X, double Y, double Z) P(double u, double v) { var q = g.Lerp(u, v, top); return (q[0], q[1], q[2]); }
        (double X, double Y, double Z) Nrm(double u, double v) { var q = g.LerpN(u, v); return (q[0], q[1], q[2]); }
        (double, double, double) W(double u, double v, double o) { var q = g.Lerp(u, v, top); var n = g.LerpN(u, v); return (q[0] + n[0] * o, q[1] + n[1] * o, q[2] + n[2] * o); }
        DesignScene.LayPlates(m, nu, nv, g.Arc, g.Len, new Random(unchecked(p.Id * 7919 + 4111)), p.ArmClad == "Heavy", 1, skin, P, Nrm, W, new bool[nu, nv], hex: p.ArmClad == "Hex");
    }

    /// <summary>Where its struts stand: shares (u round, v along) of the grid, about <see cref="StrutSpacing"/> apart, in from the plate's edges.</summary>
    public static List<(double U, double V)> StrutSpots(Grid g)
    {
        int cu = Math.Clamp((int)Math.Round(g.Arc / StrutSpacing), g.Arc > 1.5 ? 2 : 1, 8), cv = Math.Clamp((int)Math.Round(g.Len / StrutSpacing), g.Len > 1.5 ? 2 : 1, 12);
        var o = new List<(double, double)>();
        for (var a = 0; a < cu; a++)
            for (var b = 0; b < cv; b++)
                o.Add(((a + 0.5) / cu, (b + 0.5) / cv));
        return o;
    }

    /// <summary>Its struts: a square post from 5 cm under the skin to 2 cm into the plate, with a foot pad on the skin.</summary>
    private static void Struts(DesignMesh m, Grid g, double off)
    {
        foreach (var (u, v) in StrutSpots(g))
        {
            var s = g.Lerp(u, v, 0);
            var n = g.LerpN(u, v);
            var along = Norm(Sub(g.Lerp(u, Math.Min(1, v + 0.01), 0), g.Lerp(u, Math.Max(0, v - 0.01), 0)));
            along = Norm(Add(along, n, -Dot(along, n)));
            var x = Cross(n, along);
            m.Frame(s[0], s[1], s[2], x, n, along);
            m.Box(-StrutHalf, -0.05, -StrutHalf, StrutHalf, off + 0.02, StrutHalf, PieceKinds.Gun);
            m.Box(-StrutHalf * 2.2, -0.05, -StrutHalf * 2.2, StrutHalf * 2.2, 0.06, StrutHalf * 2.2, PieceKinds.Armour); // (its foot)
            m.Box(-StrutHalf * 1.6, off - 0.09, -StrutHalf * 1.6, StrutHalf * 1.6, off - 0.002 - PanelRise, StrutHalf * 1.6, PieceKinds.Armour); // (its head, under the panels)
        }
        m.World();
    }

    // ---- solid in the game

    /// <summary>
    /// The plate's solid as point sets in the ship's frame, each one convex hull: a cell of the plate (its inner and outer points, about
    /// <see cref="CellSize"/> each way, at most <see cref="MaxCells"/>) and each strut's post.
    /// </summary>
    public static List<List<double[]>> Solids(ShipDesign d, Piece p)
    {
        var o = new List<List<double[]>>();
        if (GridOf(d, p) is not { } g)
            return o;
        double off = p.ArmOff, top = p.ArmOff + p.ArmT;
        int cu = Math.Clamp((int)Math.Round(g.Arc / CellSize), 1, g.Nt), cv = Math.Clamp((int)Math.Round(g.Len / CellSize), 1, g.Nz);
        while (cu * cv > MaxCells)
            if (cu >= cv) cu--; else cv--;
        for (var a = 0; a < cu; a++)
            for (var b = 0; b < cv; b++)
            {
                int j0 = g.Nt * a / cu, j1 = g.Nt * (a + 1) / cu, i0 = g.Nz * b / cv, i1 = g.Nz * (b + 1) / cv;
                var pts = new List<double[]>();
                for (var i = i0; i <= i1; i++)
                    for (var j = j0; j <= j1; j++)
                    {
                        pts.Add(g.At(i, j, off));
                        pts.Add(g.At(i, j, top));
                    }
                o.Add(pts);
            }
        foreach (var (u, v) in StrutSpots(g))
        {
            var s = g.Lerp(u, v, 0);
            var n = g.LerpN(u, v);
            var along = Norm(Sub(g.Lerp(u, Math.Min(1, v + 0.01), 0), g.Lerp(u, Math.Max(0, v - 0.01), 0)));
            along = Norm(Add(along, n, -Dot(along, n)));
            var x = Cross(n, along);
            var pts = new List<double[]>();
            foreach (var sx in new[] { -StrutHalf, StrutHalf })
                foreach (var sz in new[] { -StrutHalf, StrutHalf })
                    foreach (var sy in new[] { 0.05, off })
                        pts.Add([s[0] + x[0] * sx + n[0] * sy + along[0] * sz, s[1] + x[1] * sx + n[1] * sy + along[1] * sz, s[2] + x[2] * sx + n[2] * sy + along[2] * sz]);
            o.Add(pts);
        }
        return o;
    }

    /// <summary>A quad in world space, wound so its face looks along <paramref name="want"/>.</summary>
    private static void Face(DesignMesh m, double[] a, double[] b, double[] c, double[] d, double[] want, (byte, byte, byte) col)
    {
        var n = Cross(Sub(b, a), Sub(c, a));
        if (Dot(n, n) < 1e-16)
            n = Cross(Sub(c, a), Sub(d, a));
        if (Dot(n, want) >= 0)
            m.Quad(T(a), T(b), T(c), T(d), col, false);
        else
            m.Quad(T(a), T(d), T(c), T(b), col, false);
    }

    private static (double, double, double) T(double[] v) => (v[0], v[1], v[2]);
    private static double[] Add(double[] a, double[] b, double k) => [a[0] + b[0] * k, a[1] + b[1] * k, a[2] + b[2] * k];
    private static double[] Sub(double[] a, double[] b) => [a[0] - b[0], a[1] - b[1], a[2] - b[2]];
    private static double Dot(double[] a, double[] b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
    private static double[] Norm(double[] v) { var l = Math.Sqrt(Dot(v, v)); return l < 1e-9 ? [0, 1, 0] : [v[0] / l, v[1] / l, v[2] / l]; }
    private static double[] Cross(double[] a, double[] b) => [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]];
}
